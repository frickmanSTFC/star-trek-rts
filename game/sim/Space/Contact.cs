using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Civilizations phase 2, star-system side (docs/civilizations-phase2-contract.md; data/civilizations/contact.json).
// First contact is a real-time event (the game doesn't pause): contact.json steps, each step_seconds long, the first choice
// taken when a step runs out; the flagship must stay in orbit (leaving calls it off); the outcome is rolled on sim.Rng when
// the last step ends. Relations Contact → Trade partner → Candidate → Allied world (MembershipCommand). Trade partners pay
// trade income each whole minute, share a one-off research boost (the observation bank) and a unique local tech (its
// modifiers go onto the player's surface, SurfacePlayer.SharedTechs), and send ally ships (SystemShip with AllyOf = the
// player, Owner -1) that escort the flagship or scout a body. Aggressive natives past awareness 80 refuse contact and
// shoot down satellites/posts (L4+); L4 worlds may reach warp (sim.Rng a minute). Everything here is in the state hash
// (system games only; the surface's shared techs only once there is one).

/// <summary>Relations between one player and a living civilization.</summary>
public enum ContactStage { None, Contact, TradePartner, Candidate, Allied }

/// <summary>What an allied native ship is doing.</summary>
public enum AllyTask { Home, Escort, Scout }

/// <summary>One option at a step of a first-contact event. ShockShift = how it moves the culture-shock chance (percentage points).</summary>
public sealed record ContactChoice(string Text, string Hint, int ShockShift);

/// <summary>A running first-contact event (~4 min): Step 0..Steps-1, each with its choices; Chosen = the picks so far
/// (-1 = not yet, the default is taken when the step's time runs out). ShockChance = the current chance (%) it ends in
/// culture shock instead of a smooth start as a trade partner.</summary>
public sealed record ContactEvent(string BodyId, int Player, long StartTick, long EndTick, int Step, int Steps,
    IReadOnlyList<ContactChoice> Choices, IReadOnlyList<int> Chosen, int ShockChance, long StepEndTick)
{
    /// <summary>The current step's title ("Who goes down").</summary>
    public string StepTitle { get; init; } = "";

    /// <summary>Contact below warp in a Prime Directive era: a violation (Standing hit, incident; always culture shock).</summary>
    public bool Violation { get; init; }
}

public sealed partial class StarSystemState
{
    /// <summary>A player's relations with the civilization on one body.</summary>
    private sealed class Relation
    {
        public string Body = "";
        public ContactStage Stage;
        /// <summary>When the current stage was reached.</summary>
        public long StageTick;
        /// <summary>Culture shock: becomes a trade partner at this tick (-1 = no wait running).</summary>
        public long TradeAfterTick = -1;
        public long RefuseUntilTick;
        /// <summary>Nations of a fractured world that walked out of the membership talks.</summary>
        public int WalkedOut;
        /// <summary>The research boost and the unique tech have been given (once).</summary>
        public bool Rewarded;
        public string? Tech;
        /// <summary>Research points the event's choices added to the boost.</summary>
        public int ResearchExtra;
        /// <summary>The first contact ended in culture shock (unrest follows once trade starts).</summary>
        public bool Shocked;
        /// <summary>Unrest after a culture shock: trade pays contact.json unrest.trade_share until this tick (0 = none).</summary>
        public long UnrestTradeUntilTick;

        public void AddToHash(ref StateHasher h)
        {
            h.Add(Body);
            h.Add((int)Stage);
            h.Add(StageTick);
            h.Add(TradeAfterTick);
            h.Add(RefuseUntilTick);
            h.Add(WalkedOut);
            h.Add(Rewarded);
            SystemShip.Text(ref h, Tech);
            h.Add(ResearchExtra);
            h.Add(Shocked);
            h.Add(UnrestTradeUntilTick);
        }
    }

    /// <summary>A running first-contact event.</summary>
    private sealed class ContactRun
    {
        public string Body = "";
        public long StartTick;
        public int Step;
        public long StepEndTick;
        public List<int> Chosen = new();
        public bool Violation;
        public bool PreWarp;

        public void AddToHash(ref StateHasher h)
        {
            h.Add(Body);
            h.Add(StartTick);
            h.Add(Step);
            h.Add(StepEndTick);
            h.Add(Chosen.Count);
            foreach (var c in Chosen) h.Add(c);
            h.Add(Violation);
            h.Add(PreWarp);
        }
    }

    private const ulong TechSalt = 0xC0A7_0001UL, AllySalt = 0xC0A7_0002UL, DebugSalt = 0xC0A7_0003UL, FreighterSalt = 0xC0A7_0004UL;

    /// <summary>Ally ships made so far (names and classes are picked by this count).</summary>
    private int _alliesMade;

    /// <summary>Trade freighters made so far (names are picked by this count).</summary>
    private int _freightersMade;

    private ContactRules? ContactData => CivData?.Contact;

    private long StepTicks => (long)(ContactData?.StepSeconds ?? 80) * Simulation.TickRate;

    // ---------------------------------------------------------------- the contract

    /// <summary>The player's relations with the body's civilization.</summary>
    public ContactStage RelationsWith(int player, string bodyId) => Rel(player, bodyId)?.Stage ?? ContactStage.None;

    /// <summary>The player's running first-contact event at the body, or null.</summary>
    public ContactEvent? ContactEventOf(int player, string bodyId)
    {
        var run = Run(player, bodyId);
        if (run == null || ContactData is not { } c || CivAt(bodyId) is not { } site) return null;
        var steps = c.Steps.Count;
        var step = System.Math.Min(run.Step, steps - 1);
        var choices = c.Steps[step].Choices.Select(x => new ContactChoice(x.Text, x.Hint, x.Shock)).ToList();
        var end = run.StepEndTick + (steps - 1 - run.Step) * StepTicks;
        return new ContactEvent(bodyId, player, run.StartTick, end, run.Step, steps, choices, run.Chosen.ToList(), ShockChance(player, run, site),
            run.StepEndTick) { StepTitle = c.Steps[step].Title, Violation = run.Violation };
    }

    /// <summary>Why the player can't start first contact there now (null = they can; a pre-warp world returns the
    /// General Order 1 text in a Prime Directive era, but FirstContactCommand still goes ahead as a violation).</summary>
    public string? FirstContactProblem(int player, string bodyId) => ContactBlock(player, bodyId) ?? GeneralOrderProblem(player, bodyId);

    /// <summary>Why the player can't take relations one step further now (null = they can).</summary>
    public string? MembershipProblem(int player, string bodyId)
    {
        if (_sim == null || player < 0 || player >= _sim.Players.Count) return "no such player";
        if (ContactData is not { } c) return "no first contact in this game";
        if (Recalled(player)) return "recalled to Starfleet";
        if (Body(bodyId) is not { } body || KnowledgeOf(player, bodyId) == BodyKnowledge.Unknown) return "no such body";
        if (CivAt(bodyId) is not { Living: true } site) return $"no civilization on {NameFor(player, bodyId)} to talk to";
        if (Run(player, bodyId) != null) return $"first contact with the {site.Species} is under way";
        var rel = Rel(player, bodyId);
        if (RefusesContactSeconds(player, bodyId) is var refuse && refuse > 0) return $"the {site.Species} refuse contact ({refuse} s)";
        switch (rel?.Stage ?? ContactStage.None)
        {
            case ContactStage.None:
                return $"make first contact with the {site.Species} first";
            case ContactStage.Contact:
                return rel!.TradeAfterTick >= 0
                    ? $"the {site.Species} are not ready to trade yet ({SecondsUntil(rel.TradeAfterTick)} s)"
                    : $"the {site.Species} are not trade partners yet";
            case ContactStage.TradePartner:
            {
                var ready = rel!.StageTick + (long)PartnerMinutes(c, bodyId) * Simulation.TicksPerMinute;
                if (Now < ready) return $"trade partners for {SecondsUntil(ready)} s more before they talk membership";
                break;
            }
            case ContactStage.Candidate:
            {
                var ready = rel!.StageTick + (long)CandidateMinutes(c, bodyId) * Simulation.TicksPerMinute;
                if (Now < ready) return $"membership talks with the {site.Species} take {SecondsUntil(ready)} s more";
                break;
            }
            default:
                return $"the {site.Species} of {body.Name} are already an allied world";
        }
        if (StandingOf(player) is { } standing && standing < c.Membership.MinStanding)
            return $"Starfleet Standing {c.Membership.MinStanding} needed (now {standing})";
        return null;
    }

    /// <summary>The civilization refuses contact with the player for this many more seconds (0 = it doesn't).</summary>
    public int RefusesContactSeconds(int player, string bodyId) => Rel(player, bodyId) is { } r ? SecondsUntil(r.RefuseUntilTick) : 0;

    /// <summary>What trade partners and allied worlds give the player a minute (paid at each whole minute of game time).</summary>
    public IReadOnlyList<ResourceAmount> TradeIncomePerMinute(int player)
    {
        var data = Data;
        if (ContactData is not { } c || data == null || player < 0 || player >= _civPlayers.Count) return System.Array.Empty<ResourceAmount>();
        var total = new Fix64[data.Resources.Count];
        var any = false;
        foreach (var r in _civPlayers[player].Relations)
        {
            var share = TradeShare(c, r);
            if (share <= Fix64.Zero) continue;
            foreach (var x in c.TradeIncome)
                if (x.Resource >= 0 && x.Resource < total.Length)
                {
                    total[x.Resource] += x.Amount * share;
                    any = true;
                }
        }
        if (!any) return System.Array.Empty<ResourceAmount>();
        var list = new List<ResourceAmount>();
        for (var i = 0; i < total.Length; i++)
            if (total[i] > Fix64.Zero) list.Add(new ResourceAmount(i, total[i]));
        return list;
    }

    /// <summary>The unique local tech a contacted civilization shared with the player (name), or null.</summary>
    public string? UniqueTechFrom(int player, string bodyId) =>
        Rel(player, bodyId) is { Rewarded: true, Tech: { } id } && ContactData?.UniqueTechs.FirstOrDefault(t => t.Id == id) is { } tech ? tech.Name : null;

    /// <summary>The player's allied native ships (SystemShip with AllyOf == player).</summary>
    public IReadOnlyList<SystemShip> AlliesOf(int player) => _ships.Where(s => s.AllyOf == player && player >= 0).ToList();

    /// <summary>Why the ally can't take that order (bodyId null = escort the flagship); null = it can.</summary>
    public string? AllyOrderProblem(int player, int shipId, string? bodyId)
    {
        var ship = FindShip(shipId);
        if (ship == null) return "no such ship";
        if (player < 0 || ship.AllyOf != player) return "not one of your allies";
        if (Recalled(player)) return "recalled to Starfleet";
        if (bodyId == null)
        {
            if (FlagshipOf(player) == null) return "no flagship to escort";
            if (ship.AllyTask == AllyTask.Escort) return "already escorting the flagship";
            return null;
        }
        if (Body(bodyId) == null || KnowledgeOf(player, bodyId) == BodyKnowledge.Unknown) return "no such body";
        if (ship.AllyTask == AllyTask.Scout && ship.ScoutBody == bodyId) return $"already scouting {NameFor(player, bodyId)}";
        if (KnowledgeOf(player, bodyId) >= BodyKnowledge.Charted) return $"{NameFor(player, bodyId)} is already charted";
        return null;
    }

    // ---------------------------------------------------------------- more for the view

    /// <summary>The bodies whose civilizations trade with the player (trade partner, candidate or allied), in the order the
    /// relations began: the trade routes.</summary>
    public IReadOnlyList<string> TradePartners(int player) =>
        player >= 0 && player < _civPlayers.Count
            ? _civPlayers[player].Relations.Where(r => r.Stage >= ContactStage.TradePartner).Select(r => r.Body).ToList()
            : new List<string>();

    /// <summary>Seconds left of culture shock (unrest) among the natives of a body; 0 when none.</summary>
    public int CultureShockSecondsLeft(string bodyId)
    {
        var i = CivIndex(bodyId);
        return i < 0 ? 0 : SecondsUntil(_shockUntil[i]);
    }

    /// <summary>The native class of an ally ship (contact.json ally.classes), or null for any other ship.</summary>
    public AllyClassDef? AllyClass(SystemShip ship) =>
        ship.AllyOf >= 0 && ContactData is { } c ? c.Ally.Classes.FirstOrDefault(x => "native." + x.Id == ship.ClassId) : null;

    /// <summary>Nations of a fractured world that walked out of the player's membership talks.</summary>
    public int WalkedOut(int player, string bodyId) => Rel(player, bodyId)?.WalkedOut ?? 0;

    /// <summary>Seconds left of the unrest after a culture shock during which the world pays the player only
    /// contact.json unrest.trade_share of its trade (0 = full trade, or trade hasn't started yet).</summary>
    public int UnrestTradeSecondsLeft(int player, string bodyId) => Rel(player, bodyId) is { } r ? SecondsUntil(r.UnrestTradeUntilTick) : 0;

    /// <summary>The natives of a body are unsettled after a culture shock (awareness held high; see <see cref="CultureShockSecondsLeft"/>).</summary>
    public bool Unsettled(string bodyId) => CultureShockSecondsLeft(bodyId) > 0;

    /// <summary>The body's civilization is spacefaring (contact.json spacefaring.from_level, L6): a stronger partner.</summary>
    public bool IsSpacefaring(string bodyId) => Spacefaring(bodyId);

    /// <summary>What a first contact with the body risks for the player beyond the usual (null = nothing): spacefaring
    /// natives turn away a captain with a low Starfleet Standing.</summary>
    public string? ContactRisk(int player, string bodyId)
    {
        if (!LowStandingWith(player, bodyId) || ContactData is not { } c || CivAt(bodyId) is not { } site) return null;
        var sf = c.Spacefaring;
        return $"the {site.Species} fly their own starships and judge us by our Standing ({StandingOf(player)} < {sf.LowStanding}): "
            + $"{Pct(sf.LowStandingRefuseChance)} they turn us away, +{sf.LowStandingShock}% culture shock";
    }

    /// <summary>The civilization that shared a unique tech (contact.json unique_techs id) with the player, or null.</summary>
    public CivSite? SharedTechSite(int player, string techId)
    {
        if (player < 0 || player >= _civPlayers.Count) return null;
        foreach (var r in _civPlayers[player].Relations)
            if (r.Rewarded && r.Tech == techId && CivAt(r.Body) is { } site) return site;
        return null;
    }

    /// <summary>The native trade freighters flying the player's trade routes (SystemShip.TradeFor == player).</summary>
    public IReadOnlyList<SystemShip> TradeFreightersOf(int player) => _ships.Where(s => s.TradeFor == player && player >= 0).ToList();

    // ---------------------------------------------------------------- spacefaring partners, ally counts

    private bool Spacefaring(string? bodyId) =>
        bodyId != null && ContactData is { } c && CivAt(bodyId) is { Living: true } site && c.IsSpacefaring((int)site.Level);

    /// <summary>A spacefaring world and a player whose Starfleet Standing is below spacefaring.low_standing.</summary>
    private bool LowStandingWith(int player, string bodyId) =>
        Spacefaring(bodyId) && StandingOf(player) is { } standing && standing < ContactData!.Spacefaring.LowStanding;

    private int PartnerMinutes(ContactRules c, string bodyId) => Spacefaring(bodyId) ? c.Spacefaring.PartnerMinutes : c.Membership.PartnerMinutes;

    private int CandidateMinutes(ContactRules c, string bodyId) => Spacefaring(bodyId) ? c.Spacefaring.CandidateMinutes : c.Membership.CandidateMinutes;

    /// <summary>Ally ships a relations stage brings from that world (spacefaring worlds at least spacefaring.ally_ships).</summary>
    private int ShipsWanted(ContactRules c, int stage, string bodyId)
    {
        var n = c.Ally.ShipsFor(stage);
        if (stage >= (int)ContactStage.TradePartner && Spacefaring(bodyId)) n = System.Math.Max(n, c.Spacefaring.AllyShips);
        return n;
    }

    private static string Pct(Fix64 share) => $"{(share * Fix64.FromInt(100)).FloorToInt()}%";

    // ---------------------------------------------------------------- problems

    /// <summary>Everything that stops a first contact (General Order 1 aside).</summary>
    private string? ContactBlock(int player, string bodyId)
    {
        if (_sim == null || player < 0 || player >= _sim.Players.Count) return "no such player";
        if (ContactData is not { } c) return "no first contact in this game";
        if (!c.Factions.Contains(_sim.Players[player].FactionId)) return "only the Federation can make first contact for now";
        if (Recalled(player)) return "recalled to Starfleet";
        var body = Body(bodyId);
        if (body == null || KnowledgeOf(player, bodyId) == BodyKnowledge.Unknown) return "no such body";
        var ship = FlagshipOf(player);
        if (ship == null) return "no ship in orbit";
        if (ship.AtBody != bodyId) return $"the ship is not in orbit of {NameFor(player, bodyId)}";
        if (KnowledgeOf(player, bodyId) < BodyKnowledge.Charted) return $"finish the sensor sweep of {body.Name} first";
        var site = CivAt(bodyId);
        if (site == null) return $"no civilization on {body.Name} to contact";
        if (!site.Living) return $"only ruins on {body.Name}: nobody to talk to";
        if (c.NeedsStudy && StudyOf(player, bodyId) < Fix64.One) return $"finish studying the {site.Species} first";
        if (Run(player, bodyId) != null) return $"first contact with the {site.Species} is under way";
        if (Rel(player, bodyId) is { Stage: > ContactStage.None }) return $"already in contact with the {site.Species}";
        if (RefusesContactSeconds(player, bodyId) is var refuse && refuse > 0) return $"the {site.Species} refuse contact ({refuse} s)";
        return null;
    }

    /// <summary>Contact would break the Prime Directive: a pre-warp world (below contact.json min_level) for a player with
    /// Starfleet Standing (Federation, Prime Directive era).</summary>
    private bool BreaksGeneralOrder(int player, string bodyId) =>
        ContactData is { } c && CivAt(bodyId) is { Living: true } site && (int)site.Level < c.MinLevel
        && player >= 0 && player < _civPlayers.Count && _civPlayers[player].HasStanding;

    private string? GeneralOrderProblem(int player, string bodyId) =>
        BreaksGeneralOrder(player, bodyId)
            ? $"General Order 1: the {CivAt(bodyId)!.Species} have no warp drive yet (contact breaks the Prime Directive)" : null;

    // ---------------------------------------------------------------- commands

    private void ApplyContact(Simulation sim, Command command)
    {
        switch (command)
        {
            case FirstContactCommand first:
            {
                if (ContactBlock(first.Player, first.BodyId) is { } problem)
                {
                    sim.Refuse(first, problem);
                    return;
                }
                StartContact(sim, first.Player, first.BodyId);
                return;
            }
            case ContactChoiceCommand choice:
            {
                var run = Run(choice.Player, choice.BodyId);
                var c = ContactData;
                if (run == null || c == null)
                {
                    sim.Refuse(choice, $"no first contact under way at {(Body(choice.BodyId) is { } b ? NameFor(choice.Player, b.Id) : "that body")}");
                    return;
                }
                if (choice.Choice < 0 || choice.Choice >= c.Steps[run.Step].Choices.Count)
                {
                    sim.Refuse(choice, "no such choice");
                    return;
                }
                run.Chosen[run.Step] = choice.Choice;
                AdvanceContact(sim, choice.Player, run);
                return;
            }
            case MembershipCommand membership:
            {
                if (MembershipProblem(membership.Player, membership.BodyId) is { } problem)
                {
                    sim.Refuse(membership, problem);
                    return;
                }
                StepMembership(sim, membership.Player, membership.BodyId);
                return;
            }
            case AllyShipOrderCommand order:
            {
                if (AllyOrderProblem(order.Player, order.ShipId, order.BodyId) is { } problem)
                {
                    sim.Refuse(order, problem);
                    return;
                }
                var ship = FindShip(order.ShipId)!;
                ship.FollowTick = -1;
                ship.SweepBody = null;
                if (order.BodyId == null)
                {
                    ship.AllyTask = AllyTask.Escort;
                    ship.ScoutBody = null;
                }
                else
                {
                    ship.AllyTask = AllyTask.Scout;
                    ship.ScoutBody = order.BodyId;
                }
                Version++;
                return;
            }
        }
    }

    private void StartContact(Simulation sim, int player, string bodyId)
    {
        var c = ContactData!;
        var site = CivAt(bodyId)!;
        var p = _civPlayers[player];
        if (LowStandingWith(player, bodyId) && sim.Rng.NextFix() < c.Spacefaring.LowStandingRefuseChance)
        {
            // Spacefaring natives check who is calling: a captain with a low Starfleet Standing is turned away.
            Event(player, bodyId, "contact_refused",
                $"The {site.Species} of {Body(bodyId)!.Name} turned our hail away: they know our Starfleet Standing ({StandingOf(player)}) and refuse contact for {c.RefuseAfterIncidentMinutes} min");
            RefuseContact(player, bodyId, c.RefuseAfterIncidentMinutes);
            return;
        }
        var run = new ContactRun
        {
            Body = bodyId, StartTick = sim.Tick, StepEndTick = sim.Tick + StepTicks, Violation = BreaksGeneralOrder(player, bodyId),
            PreWarp = (int)site.Level < c.MinLevel,
        };
        for (var k = 0; k < c.Steps.Count; k++) run.Chosen.Add(-1);
        p.Runs.Add(run);
        var name = Body(bodyId)!.Name;
        Event(player, bodyId, "contact_started", $"First contact with the {site.Species} of {name} begins");
        if (run.Violation)
        {
            Event(player, bodyId, "violation", $"General Order 1 broken: the {site.Species} of {name} have no warp drive");
            AddIncident(sim, player, bodyId, c.ViolationStanding);
        }
    }

    /// <summary>Moves the event to its next step, or ends it after the last one.</summary>
    private void AdvanceContact(Simulation sim, int player, ContactRun run)
    {
        var c = ContactData!;
        run.Step++;
        Version++;
        if (run.Step < c.Steps.Count)
        {
            run.StepEndTick = sim.Tick + StepTicks;
            return;
        }
        FinishContact(sim, player, run);
    }

    /// <summary>The culture-shock chance (%) of a running event now.</summary>
    private int ShockChance(int player, ContactRun run, CivSite site)
    {
        var c = ContactData!;
        if (run.Violation) return 100;
        var chance = c.BaseShock + c.ShockFor(TemperamentId(site.Temperament)) + (c.ShockPerAwareness * AwarenessOf(site.BodyId)).FloorToInt();
        if (run.PreWarp) chance += c.PreWarpShock;
        if (LowStandingWith(player, site.BodyId)) chance += c.Spacefaring.LowStandingShock; // spacefaring natives judge a low Standing
        for (var k = 0; k < run.Chosen.Count && k < c.Steps.Count; k++)
            if (run.Chosen[k] >= 0 && run.Chosen[k] < c.Steps[k].Choices.Count) chance += c.Steps[k].Choices[run.Chosen[k]].Shock;
        return System.Math.Clamp(chance, c.ShockMin, c.ShockMax);
    }

    private static string TemperamentId(Temperament t) => t switch
    {
        Temperament.Peaceful => "peaceful",
        Temperament.Aggressive => "aggressive",
        _ => "wary",
    };

    /// <summary>The last step is over: smooth start (trade partner at once) or culture shock (rolled on sim.Rng; always for a violation).</summary>
    private void FinishContact(Simulation sim, int player, ContactRun run)
    {
        var c = ContactData!;
        var p = _civPlayers[player];
        p.Runs.Remove(run);
        var site = CivAt(run.Body);
        if (site is not { Living: true }) return;
        var chance = ShockChance(player, run, site);
        var shock = run.Violation || sim.Rng.NextInt(100) < chance;
        var rel = RelFor(player, run.Body);
        int research = 0, standing = 0, wait = c.ShockWaitMinutes;
        for (var k = 0; k < run.Chosen.Count && k < c.Steps.Count; k++)
        {
            var pick = run.Chosen[k] >= 0 && run.Chosen[k] < c.Steps[k].Choices.Count ? c.Steps[k].Choices[run.Chosen[k]] : c.Steps[k].Choices[0];
            research += pick.Research;
            standing += pick.StandingIfShock;
            wait += pick.WaitMinutes;
        }
        rel.ResearchExtra = research;
        var name = Body(run.Body)!.Name;
        if (!shock)
        {
            Event(player, run.Body, "contact_smooth", $"First contact with the {site.Species} of {name} went smoothly: trade partners");
            SetStage(sim, player, run.Body, ContactStage.TradePartner);
            return;
        }
        SetStage(sim, player, run.Body, ContactStage.Contact);
        rel.Shocked = true;
        rel.TradeAfterTick = sim.Tick + System.Math.Max(0, wait) * (long)Simulation.TicksPerMinute;
        var i = CivIndex(run.Body);
        _shockUntil[i] = sim.Tick + (long)c.CultureShockMinutes * Simulation.TicksPerMinute;
        if (c.CultureShockAwareness > 0) RaiseAwareness(i, Fix64.FromInt(c.CultureShockAwareness));
        Event(player, run.Body, "culture_shock",
            $"Culture shock among the {site.Species} of {name}: talks go on, trade in {System.Math.Max(0, wait)} min (at {Pct(c.Unrest.TradeShare)} for {c.Unrest.Minutes} min while the unrest lasts)");
        if (standing != 0) ChangeStanding(sim, player, standing);
    }

    /// <summary>Trade partner → Candidate (a nation of a fractured world may walk out) → Allied world.</summary>
    private void StepMembership(Simulation sim, int player, string bodyId)
    {
        var c = ContactData!;
        var rel = Rel(player, bodyId)!;
        var site = CivAt(bodyId)!;
        var name = Body(bodyId)!.Name;
        if (site.Unity == CivUnity.Fractured && rel.WalkedOut < site.Nations - 1 && sim.Rng.NextFix() < c.Membership.WalkoutChance)
        {
            var nations = CivGenerator.NationNames(sim.Data, site);
            var who = rel.WalkedOut + 1 < nations.Count ? nations[rel.WalkedOut + 1] : $"a nation of {name}";
            rel.WalkedOut++;
            Event(player, bodyId, "walkout", $"{who} walked out of the membership talks (their trade goes with them)");
        }
        var next = rel.Stage == ContactStage.TradePartner ? ContactStage.Candidate : ContactStage.Allied;
        SetStage(sim, player, bodyId, next);
        Event(player, bodyId, "membership", next == ContactStage.Candidate
            ? $"The {site.Species} of {name} are candidates for membership"
            : $"The {site.Species} of {name} are an allied world");
    }

    /// <summary>Sets a relations stage; trade partner or better gives the rewards once and the stage's ally ships.</summary>
    private void SetStage(Simulation sim, int player, string bodyId, ContactStage stage)
    {
        var c = ContactData!;
        var rel = RelFor(player, bodyId);
        rel.Stage = stage;
        rel.StageTick = sim.Tick;
        if (stage != ContactStage.Contact) rel.TradeAfterTick = -1;
        Version++;
        if (stage < ContactStage.TradePartner) return;
        var site = CivAt(bodyId);
        if (!rel.Rewarded && site != null && c.UniqueTechs.Count > 0)
        {
            rel.Rewarded = true;
            var tech = c.UniqueTechs[(int)(SystemGenerator.H((ulong)(uint)site.Seed, TechSalt, 0) % (ulong)c.UniqueTechs.Count)];
            rel.Tech = tech.Id;
            var boost = System.Math.Max(0, c.ResearchBoost + rel.ResearchExtra);
            if (boost > 0) _civPlayers[player].Bank += Fix64.FromInt(boost);
            if (sim.Surface is { } surface && player < surface.Players.Count) surface.ShareTech(player, tech);
            Event(player, bodyId, "unique_tech", $"The {site.Species} shared {tech.Name}: {tech.Effect} (+{boost} research)");
        }
        var want = ShipsWanted(c, (int)stage, bodyId);
        var have = _ships.Count(s => s.AllyOf == player && s.AllyHome == bodyId);
        for (var k = have; k < want; k++) MakeAlly(sim, player, bodyId);
    }

    private void MakeAlly(Simulation sim, int player, string bodyId)
    {
        var c = ContactData!;
        var weights = c.Ally.Classes.Select(x => new Weighted(x.Id, x.Weight)).ToList();
        var cls = c.Ally.Classes.First(x => x.Id == SystemGenerator.Pick(weights, Hash(AllySalt, _alliesMade * 2L)));
        var names = c.Ally.Names;
        var name = names[(int)(Hash(AllySalt, _alliesMade * 2L + 1) % (ulong)names.Count)];
        if (_ships.Any(s => s.Name == name)) name += $" {_alliesMade + 1}";
        _alliesMade++;
        var site = CivAt(bodyId);
        var ship = new SystemShip
        {
            Id = _nextShipId++, Owner = -1, ClassId = "native." + cls.Id, Name = name, Faction = "native", Role = cls.Role, AtBody = bodyId,
            Registry = site?.Species ?? "",
        };
        ship.AllyOf = player;
        ship.AllyHome = bodyId;
        ship.AllyTask = AllyTask.Escort;
        _ships.Add(ship);
        Version++;
        Event(player, bodyId, "ally_ship", $"The {site?.Species ?? "natives"} sent the {cls.Name} {name} to fly with us");
    }

    /// <summary>A Standing change outside an incident (risky choices, shoot-downs); recalled at 0.</summary>
    private void ChangeStanding(Simulation sim, int player, int delta)
    {
        if (player < 0 || player >= _civPlayers.Count) return;
        var p = _civPlayers[player];
        if (!p.HasStanding || p.Recalled || delta == 0) return;
        p.Standing = Fix64.Clamp(p.Standing + Fix64.FromInt(delta), Fix64.Zero, Fix64.FromInt(CivData!.Standing.Max));
        Version++;
        UpdateRung(sim, player, p);
    }

    private void RefuseContact(int player, string bodyId, int minutes)
    {
        if (player < 0 || player >= _civPlayers.Count || minutes <= 0) return;
        var rel = RelFor(player, bodyId);
        rel.RefuseUntilTick = System.Math.Max(rel.RefuseUntilTick, Now + (long)minutes * Simulation.TicksPerMinute);
        Version++;
    }

    private void CancelContact(int player, ContactRun run, string why)
    {
        _civPlayers[player].Runs.Remove(run);
        var site = CivAt(run.Body);
        Event(player, run.Body, "contact_called_off", $"First contact with the {site?.Species ?? "natives"} called off: {why}");
    }

    // ---------------------------------------------------------------- ticks

    /// <summary>One tick of phase 2: events (timeouts, leaving orbit), the shock wait, ally ships, and at each whole minute
    /// trade income, aggressive natives and L4 → L5 (sim.Rng).</summary>
    private void StepContact(Simulation sim)
    {
        if (ContactData is not { } c) return;
        var tick = sim.Tick;
        for (var player = 0; player < _civPlayers.Count; player++)
        {
            var p = _civPlayers[player];
            for (var k = p.Runs.Count - 1; k >= 0; k--)
            {
                var run = p.Runs[k];
                if (CivAt(run.Body) is not { Living: true }) CancelContact(player, run, "nobody left to talk to");
                else if (FlagshipOf(player)?.AtBody != run.Body) CancelContact(player, run, "the ship left orbit");
                else if (tick >= run.StepEndTick)
                {
                    run.Chosen[run.Step] = 0; // the default
                    AdvanceContact(sim, player, run);
                }
            }
            for (var k = 0; k < p.Relations.Count; k++)
            {
                var rel = p.Relations[k];
                if (rel.Stage == ContactStage.Contact && rel.TradeAfterTick >= 0 && tick >= rel.TradeAfterTick && CivAt(rel.Body) is { Living: true } site)
                {
                    var unrest = rel.Shocked && c.Unrest.Minutes > 0;
                    Event(player, rel.Body, "trade_partner", $"The {site.Species} of {Body(rel.Body)!.Name} are ready to trade: trade partners"
                        + (unrest ? $" (still unsettled: {Pct(c.Unrest.TradeShare)} trade for {c.Unrest.Minutes} min)" : ""));
                    if (unrest)
                    {
                        // Culture shock has teeth: half trade (unrest.trade_share) and unrest for unrest.minutes once trade starts.
                        rel.UnrestTradeUntilTick = tick + (long)c.Unrest.Minutes * Simulation.TicksPerMinute;
                        var i = CivIndex(rel.Body);
                        if (i >= 0) _shockUntil[i] = System.Math.Max(_shockUntil[i], rel.UnrestTradeUntilTick);
                    }
                    SetStage(sim, player, rel.Body, ContactStage.TradePartner);
                }
                if (rel.UnrestTradeUntilTick > 0 && tick == rel.UnrestTradeUntilTick && CivAt(rel.Body) is { Living: true } calm)
                    Event(player, rel.Body, "unrest_over", $"The {calm.Species} of {Body(rel.Body)!.Name} have settled down: full trade again");
            }
        }

        StepAllies(tick, c);
        StepFreighters(tick, c);

        // Unrest after a culture shock: their awareness can't fall below unrest.awareness_floor while it lasts.
        for (var i = 0; i < _civs.Count; i++)
            if (_civs[i].Living && _shockUntil[i] > tick && _awareness[i] < Fix64.FromInt(c.Unrest.AwarenessFloor))
                RaiseAwareness(i, Fix64.FromInt(c.Unrest.AwarenessFloor) - _awareness[i]);

        if (tick <= 0 || tick % Simulation.TicksPerMinute != 0) return;
        PayTrade(sim, c);
        StepAggressive(sim, c);
        StepAdvance(sim, c);
        for (var i = 0; i < _civs.Count; i++) _awarenessMark[i] = _awareness[i];
    }

    private Fix64 TradeShare(ContactRules c, Relation r)
    {
        if (r.Stage < ContactStage.TradePartner) return Fix64.Zero;
        var share = c.MultiplierFor((int)r.Stage) * Fix64.Max(Fix64.Zero, Fix64.One - c.Membership.WalkoutIncomeCut * r.WalkedOut);
        if (Spacefaring(r.Body)) share *= c.Spacefaring.TradeMultiplier; // L6: a stronger partner
        if (Now < r.UnrestTradeUntilTick) share *= c.Unrest.TradeShare; // unrest after a culture shock
        return share;
    }

    /// <summary>Trade income at a whole minute: through the colony surface's storage when there is one (what doesn't fit
    /// is lost, latinum always fits), else straight into the stock.</summary>
    private void PayTrade(Simulation sim, ContactRules c)
    {
        for (var player = 0; player < _civPlayers.Count && player < sim.Players.Count; player++)
        {
            var income = TradeIncomePerMinute(player);
            foreach (var x in income)
            {
                if (sim.Surface is { } surface && player < surface.Players.Count) surface.StoreTrade(sim, player, x.Resource, x.Amount);
                else sim.Players[player].Stock.Add(x.Resource, x.Amount);
            }
        }
    }

    /// <summary>Aggressive natives at awareness ≥ from_awareness: on reaching it they refuse contact with everyone who has
    /// Charted them; from shoot_from_level they shoot down satellites and working posts over their world.</summary>
    private void StepAggressive(Simulation sim, ContactRules c)
    {
        var g = c.Aggressive;
        for (var i = 0; i < _civs.Count; i++)
        {
            var site = _civs[i];
            if (!site.Living || site.Temperament != Temperament.Aggressive || AwarenessOf(site.BodyId) < g.FromAwareness) continue;
            var name = Body(site.BodyId)?.Name ?? site.BodyId;
            if (!_hostile[i])
            {
                _hostile[i] = true;
                for (var player = 0; player < _civPlayers.Count; player++)
                    if (KnowledgeOf(player, site.BodyId) >= BodyKnowledge.Charted)
                    {
                        Event(player, site.BodyId, "hostile", $"The {site.Species} of {name} turn hostile: they refuse all contact for {g.RefuseMinutes} min");
                        Hostile(sim, player, site.BodyId, g.RefuseMinutes);
                    }
            }
            if ((int)site.Level < g.ShootFromLevel) continue;
            for (var k = 0; k < _satellites.Count; k++)
            {
                var s = _satellites[k];
                if (s.BodyId != site.BodyId || sim.Rng.NextFix() >= g.ShootChance) continue;
                _satellites.RemoveAt(k--);
                Version++;
                Event(s.Owner, site.BodyId, "shot_down", $"The {site.Species} of {name} shot down our satellite");
                ChangeStanding(sim, s.Owner, g.ShootDownStanding);
                Hostile(sim, s.Owner, site.BodyId, g.RefuseMinutes);
            }
            foreach (var post in _posts)
            {
                if (post.BodyId != site.BodyId || post.Found || sim.Rng.NextFix() >= g.ShootChance) continue;
                post.Found = true;
                post.ShotDown = true;
                post.FoundTick = sim.Tick;
                Event(post.Owner, site.BodyId, "shot_down", $"The {site.Species} of {name} shot down our hidden observation post");
                ChangeStanding(sim, post.Owner, g.ShootDownStanding);
                Hostile(sim, post.Owner, site.BodyId, g.RefuseMinutes);
            }
        }
    }

    /// <summary>The natives refuse the player for a while; a running first contact there is called off.</summary>
    private void Hostile(Simulation sim, int player, string bodyId, int minutes)
    {
        RefuseContact(player, bodyId, minutes);
        if (Run(player, bodyId) is { } run) CancelContact(player, run, "the natives turned hostile");
    }

    /// <summary>L4 worlds may reach warp: chance_per_min + awareness × per_awareness a minute (sim.Rng); a spike of
    /// wreck_spike awareness in the last minute wrecks the attempt with wreck_chance.</summary>
    private void StepAdvance(Simulation sim, ContactRules c)
    {
        var d = c.Advance;
        for (var i = 0; i < _civs.Count; i++)
        {
            var site = _civs[i];
            if (!site.Living || site.Level != CivLevel.Atomic) continue;
            var chance = d.ChancePerMin + d.PerAwareness * AwarenessOf(site.BodyId);
            if (sim.Rng.NextFix() >= chance) continue;
            var name = Body(site.BodyId)?.Name ?? site.BodyId;
            var spike = (_awareness[i] - _awarenessMark[i]).FloorToInt() >= d.WreckSpike;
            if (spike && sim.Rng.NextFix() < d.WreckChance)
            {
                for (var player = 0; player < _knowledge.Count; player++)
                    if (KnowledgeOf(player, site.BodyId) >= BodyKnowledge.Charted)
                        Event(player, site.BodyId, "warp_wrecked", $"The {site.Species} of {name} tried a warp flight and failed: the panic over visitors wrecked it");
                continue;
            }
            SetLevel(i, CivLevel.EarlyWarp);
            for (var player = 0; player < _knowledge.Count; player++)
                if (KnowledgeOf(player, site.BodyId) >= BodyKnowledge.Charted)
                    Event(player, site.BodyId, "warp", $"The {site.Species} of {name} flew their first warp ship");
        }
    }

    private void SetLevel(int i, CivLevel level)
    {
        _civs[i] = _civs[i] with { Level = level };
        _towns.Remove(_civs[i].BodyId);
        Version++;
    }

    /// <summary>Ally ships: escort the flagship (follow it escort_delay_seconds after it leaves), scout a body (fly there,
    /// sweep it for scout_sweep_seconds and chart it for the player, then escort again), or go home once recalled.</summary>
    private void StepAllies(long tick, ContactRules c)
    {
        foreach (var ship in _ships)
        {
            if (ship.AllyOf < 0 || ship.InFlight) continue;
            var player = ship.AllyOf;
            if (Recalled(player) && ship.AllyTask != AllyTask.Home)
            {
                ship.AllyTask = AllyTask.Home;
                ship.ScoutBody = null;
                ship.SweepBody = null;
                Version++;
            }
            if (ship.AllyTask == AllyTask.Home)
            {
                if (ship.AllyHome != null && ship.AtBody != ship.AllyHome) Depart(ship, ship.AllyHome, tick, AllyTravel(ship.AtBody!, ship.AllyHome, c));
                continue;
            }
            if (ship.AllyTask == AllyTask.Scout && ship.ScoutBody is { } target)
            {
                if (ship.AtBody != target)
                {
                    Depart(ship, target, tick, AllyTravel(ship.AtBody!, target, c));
                    continue;
                }
                if (ship.SweepBody == null)
                {
                    Identify(player, target);
                    if (KnowledgeOf(player, target) >= BodyKnowledge.Charted)
                    {
                        ScoutDone(ship);
                        continue;
                    }
                    ship.SweepBody = target;
                    ship.SweepStartTick = tick;
                    ship.SweepDoneTick = tick + (long)c.Ally.ScoutSweepSeconds * Simulation.TickRate;
                    Version++;
                    continue;
                }
                if (tick >= ship.SweepDoneTick)
                {
                    Chart(player, target);
                    Event(player, target, "scout_charted", $"Our ally the {ship.Name} charted {Body(target)!.Name}");
                    ScoutDone(ship);
                }
                continue;
            }
            var flagship = FlagshipOf(player);
            var goal = flagship?.AtBody ?? flagship?.ToBody;
            if (goal == null || goal == ship.AtBody)
            {
                ship.FollowTick = -1;
                continue;
            }
            if (ship.FollowTick < 0) ship.FollowTick = tick + (long)c.Ally.EscortDelaySeconds * Simulation.TickRate;
            if (tick < ship.FollowTick) continue;
            ship.FollowTick = -1;
            Depart(ship, goal, tick, AllyTravel(ship.AtBody!, goal, c));
        }
    }

    // ---------------------------------------------------------------- trade freighters

    /// <summary>Trade freighters: one per trade route (a trade partner or better of a player, never the colony's own body),
    /// made when the route opens and retired when it closes; each waits at home, leaves in time to reach the colony body on
    /// a whole minute (when trade is paid), stays freighter.dwell_seconds and flies home; every freighter.interval_minutes.</summary>
    private void StepFreighters(long tick, ContactRules c)
    {
        var colony = SurfaceBody;
        for (var k = _ships.Count - 1; k >= 0; k--)
        {
            var s = _ships[k];
            if (s.TradeFor < 0) continue;
            if (colony != null && Rel(s.TradeFor, s.TradeHome) is { Stage: >= ContactStage.TradePartner } && CivAt(s.TradeHome!) is { Living: true }) continue;
            _ships.RemoveAt(k);
            Version++;
        }
        if (colony == null) return;
        for (var player = 0; player < _civPlayers.Count; player++)
            foreach (var rel in _civPlayers[player].Relations)
                if (rel.Stage >= ContactStage.TradePartner && rel.Body != colony && CivAt(rel.Body) is { Living: true } site && !HasFreighter(player, rel.Body))
                    MakeFreighter(player, site, colony, tick, c);
        var f = c.Freighter;
        var interval = (long)f.IntervalMinutes * Simulation.TicksPerMinute;
        foreach (var ship in _ships)
        {
            if (ship.TradeFor < 0 || ship.InFlight) continue;
            var home = ship.TradeHome!;
            if (ship.AtBody == colony)
            {
                if (tick < ship.TradeArriveTick + (long)f.DwellSeconds * Simulation.TickRate) continue;
                var back = FreighterTravel(colony, home, c);
                Depart(ship, home, tick, back);
                ship.TradeArriveTick = WholeMinuteFrom(System.Math.Max(ship.TradeArriveTick + interval, tick + back + FreighterTravel(home, colony, c)));
                continue;
            }
            if (ship.AtBody != home)
            {
                Depart(ship, home, tick, FreighterTravel(ship.AtBody!, home, c));
                continue;
            }
            if (ship.TradeArriveTick - tick <= FreighterTravel(home, colony, c))
                Depart(ship, colony, tick, System.Math.Max(1L, ship.TradeArriveTick - tick));
        }
    }

    private bool HasFreighter(int player, string bodyId)
    {
        foreach (var s in _ships)
            if (s.TradeFor == player && s.TradeHome == bodyId) return true;
        return false;
    }

    private void MakeFreighter(int player, CivSite site, string colony, long tick, ContactRules c)
    {
        var f = c.Freighter;
        var name = f.Names[(int)(Hash(FreighterSalt, _freightersMade) % (ulong)f.Names.Count)];
        if (_ships.Any(s => s.Name == name)) name += $" {_freightersMade + 1}";
        _freightersMade++;
        var cargo = Data is { } data
            ? string.Join("/", c.TradeIncome.Where(x => x.Resource >= 0 && x.Resource < data.Resources.Count).Select(x => data.Resources[x.Resource].Name.ToLowerInvariant()))
            : null;
        var ship = new SystemShip
        {
            Id = _nextShipId++, Owner = -1, ClassId = "native." + f.ClassId, Name = name, Faction = "native", Role = f.Role, AtBody = site.BodyId,
            Registry = site.Species, Cargo = string.IsNullOrEmpty(cargo) ? null : cargo,
        };
        ship.TradeFor = player;
        ship.TradeHome = site.BodyId;
        ship.TradeArriveTick = WholeMinuteFrom(tick + FreighterTravel(site.BodyId, colony, c));
        _ships.Add(ship);
        Version++;
        Event(player, site.BodyId, "trade_route",
            $"The {site.Species} of {Body(site.BodyId)?.Name ?? site.BodyId} opened a trade route: the freighter {name} flies to {Body(colony)?.Name ?? colony} every {f.IntervalMinutes} min");
    }

    private long FreighterTravel(string from, string to, ContactRules c) =>
        System.Math.Max(1L, (Fix64.FromInt((int)TravelTicks(from, to)) * c.Freighter.TravelScale).FloorToInt());

    /// <summary>The first whole minute of game time at or after a tick (when trade is paid).</summary>
    private static long WholeMinuteFrom(long tick)
    {
        var m = (long)Simulation.TicksPerMinute;
        return System.Math.Max(m, (tick + m - 1) / m * m);
    }

    private void ScoutDone(SystemShip ship)
    {
        ship.AllyTask = AllyTask.Escort;
        ship.ScoutBody = null;
        ship.SweepBody = null;
        Version++;
    }

    private long AllyTravel(string from, string to, ContactRules c) =>
        System.Math.Max(1L, (Fix64.FromInt((int)TravelTicks(from, to)) * c.Ally.TravelScale).FloorToInt());

    // ---------------------------------------------------------------- lookups and the hash

    private Relation? Rel(int player, string? bodyId)
    {
        if (bodyId == null || player < 0 || player >= _civPlayers.Count) return null;
        foreach (var r in _civPlayers[player].Relations)
            if (r.Body == bodyId) return r;
        return null;
    }

    private Relation RelFor(int player, string bodyId)
    {
        if (Rel(player, bodyId) is { } r) return r;
        r = new Relation { Body = bodyId };
        _civPlayers[player].Relations.Add(r);
        return r;
    }

    private ContactRun? Run(int player, string? bodyId)
    {
        if (bodyId == null || player < 0 || player >= _civPlayers.Count) return null;
        foreach (var r in _civPlayers[player].Runs)
            if (r.Body == bodyId) return r;
        return null;
    }

    private void AddContactToHash(ref StateHasher h)
    {
        h.Add(_alliesMade);
        h.Add(_freightersMade);
    }

    // ---------------------------------------------------------------- test panel

    /// <summary>Test panel: why a civ debug command would be refused (null = it runs).</summary>
    public string? DebugCivProblem(Simulation sim, DebugCivCommand command)
    {
        var data = sim.Data;
        if (data.Civilizations is not { } c) return "no civilizations in this game";
        if (Body(command.BodyId) is not { } body && command.Field != DebugCivField.Standing) return "no such body";
        var v = command.Value;
        var whole = Fix64.FromInt(v.FloorToInt()) == v;
        var n = v.FloorToInt();
        var site = CivAt(command.BodyId);
        var name = Body(command.BodyId)?.Name ?? command.BodyId;
        bool TargetOk() => command.Target >= 0 && command.Target < sim.Players.Count;
        switch (command.Field)
        {
            case DebugCivField.SpawnLiving:
            case DebugCivField.SpawnRuins:
            {
                var b = Body(command.BodyId)!;
                if (b.Id == SurfaceBody) return "not on the colony's landing world";
                var ground = data.IsLandableClass(b.ClassId) || c.Sites.TownMapFor(b.ClassId) != null;
                if (command.Field == DebugCivField.SpawnLiving)
                {
                    if (!ground) return $"no ground for a civilization on {name}";
                    if (!whole || n < 1 || n > GameData.CivLevels) return $"level must be 1..{GameData.CivLevels}";
                }
                else if (!ground && !c.Sites.RuinsWeights.Any(x => x.Id == b.ClassId && x.Weight > 0)) return $"no ground for ruins on {name}";
                return null;
            }
            case DebugCivField.Remove:
                return site == null ? $"no civilization or ruins on {name}" : null;
            case DebugCivField.Level:
                if (site is not { Living: true }) return $"no living civilization on {name}";
                return whole && n >= 1 && n <= GameData.CivLevels ? null : $"level must be 1..{GameData.CivLevels}";
            case DebugCivField.Awareness:
                if (site is not { Living: true }) return $"no living civilization on {name}";
                return v >= Fix64.Zero && v <= Fix64.FromInt(100) ? null : "awareness must be 0..100";
            case DebugCivField.Temperament:
                if (site is not { Living: true }) return $"no living civilization on {name}";
                return whole && n >= 1 && n <= 3 ? null : "temperament must be 1 peaceful, 2 wary or 3 aggressive";
            case DebugCivField.FinishStudy:
                if (!TargetOk()) return "no such player";
                return site is { Living: true } ? null : $"no living civilization on {name}";
            case DebugCivField.Standing:
                if (!TargetOk()) return "no such player";
                if (command.Target >= _civPlayers.Count || !_civPlayers[command.Target].HasStanding) return "that player has no Starfleet Standing";
                return v >= Fix64.Zero && v <= Fix64.FromInt(c.Standing.Max) ? null : $"Standing must be 0..{c.Standing.Max}";
            case DebugCivField.FindPost:
                if (!TargetOk()) return "no such player";
                return _posts.Any(p => p.Owner == command.Target && p.BodyId == command.BodyId && !p.Found) ? null : $"no working post of that player on {name}";
            case DebugCivField.Relations:
                if (!TargetOk()) return "no such player";
                if (c.Contact == null) return "no first contact in this game";
                if (site is not { Living: true }) return $"no living civilization on {name}";
                return whole && n >= 0 && n <= (int)ContactStage.Allied ? null : "relations must be 0..4";
            default:
                return "unknown field";
        }
    }

    /// <summary>Test panel: runs an accepted civ debug command.</summary>
    internal void ApplyDebugCiv(Simulation sim, DebugCivCommand command)
    {
        var data = sim.Data;
        var n = command.Value.FloorToInt();
        var i = CivIndex(command.BodyId);
        switch (command.Field)
        {
            case DebugCivField.SpawnLiving:
            case DebugCivField.SpawnRuins:
            {
                var b = Body(command.BodyId)!;
                var h = SystemGenerator.H(Seed ^ DebugSalt, (ulong)sim.Tick, IndexOf(b.Id) * 4096L + sim.DebugEdits);
                PutSite(data, CivGenerator.DebugSite(data, b, h, command.Field == DebugCivField.SpawnLiving, n));
                break;
            }
            case DebugCivField.Remove:
                RemoveSite(i);
                break;
            case DebugCivField.Level:
                SetLevel(i, (CivLevel)n);
                break;
            case DebugCivField.Awareness:
                _awareness[i] = command.Value;
                break;
            case DebugCivField.Temperament:
                _civs[i] = _civs[i] with { Temperament = (Temperament)n };
                break;
            case DebugCivField.FinishStudy:
            {
                Identify(command.Target, command.BodyId);
                Chart(command.Target, command.BodyId, report: false);
                var p = _civPlayers[command.Target];
                if (p.Study.Length < _civs.Count) System.Array.Resize(ref p.Study, _civs.Count);
                p.Study[i] = Fix64.One;
                break;
            }
            case DebugCivField.Standing:
            {
                var p = _civPlayers[command.Target];
                p.Standing = command.Value;
                p.Recalled = command.Value <= Fix64.Zero; // the test panel can undo a recall
                UpdateRung(sim, command.Target, p);
                break;
            }
            case DebugCivField.FindPost:
                Discover(sim, _posts.First(p => p.Owner == command.Target && p.BodyId == command.BodyId && !p.Found));
                break;
            case DebugCivField.Relations:
            {
                var stage = (ContactStage)n;
                if (Run(command.Target, command.BodyId) is { } run) _civPlayers[command.Target].Runs.Remove(run);
                if (stage == ContactStage.None) RelFor(command.Target, command.BodyId).Stage = ContactStage.None;
                else SetStage(sim, command.Target, command.BodyId, stage);
                var rel = RelFor(command.Target, command.BodyId);
                if (stage == ContactStage.Contact) rel.TradeAfterTick = sim.Tick + (long)ContactData!.ShockWaitMinutes * Simulation.TicksPerMinute;
                // Lowering the stage sends the extra ally ships home for good (test panel only: they are never lost in play).
                var keep = stage >= ContactStage.TradePartner ? ShipsWanted(ContactData!, (int)stage, command.BodyId) : 0;
                var mine = _ships.Where(s => s.AllyOf == command.Target && s.AllyHome == command.BodyId).ToList();
                for (var k = keep; k < mine.Count; k++) _ships.Remove(mine[k]);
                break;
            }
        }
        Version++;
    }

    /// <summary>Puts a site on its body (replacing what is there; study, relations and events there start over), in body order.</summary>
    private void PutSite(GameData data, CivSite site)
    {
        var at = CivIndex(site.BodyId);
        if (at >= 0) RemoveSite(at);
        var order = IndexOf(site.BodyId);
        var i = 0;
        while (i < _civs.Count && IndexOf(_civs[i].BodyId) < order) i++;
        _civs.Insert(i, site);
        var a = StartAwareness(data, site);
        _awareness.Insert(i, a);
        _shockUntil.Insert(i, 0);
        _awarenessMark.Insert(i, a);
        _hostile.Insert(i, false);
        foreach (var p in _civPlayers)
        {
            var list = p.Study.ToList();
            while (list.Count < _civs.Count - 1) list.Add(Fix64.Zero);
            list.Insert(i, Fix64.Zero);
            p.Study = list.ToArray();
        }
        _towns.Remove(site.BodyId);
        _townMaps.Remove(site.BodyId);
    }

    private void RemoveSite(int i)
    {
        var body = _civs[i].BodyId;
        _civs.RemoveAt(i);
        _awareness.RemoveAt(i);
        _shockUntil.RemoveAt(i);
        _awarenessMark.RemoveAt(i);
        _hostile.RemoveAt(i);
        foreach (var p in _civPlayers)
        {
            var list = p.Study.ToList();
            if (i < list.Count) list.RemoveAt(i);
            p.Study = list.ToArray();
            p.Relations.RemoveAll(r => r.Body == body);
            p.Runs.RemoveAll(r => r.Body == body);
        }
        _towns.Remove(body);
        _townMaps.Remove(body);
    }
}
