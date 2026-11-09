using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using Xunit.Abstractions;

namespace FinalFrontier.Sim.Tests;

/// <summary>Civilizations phase 2, star-system side (docs/civilizations-phase2-contract.md; data/civilizations/contact.json):
/// first contact, relations and membership, trade income, unique techs, ally ships, aggressive natives, L4 → L5, the civ
/// debug command, determinism and the hash.</summary>
public class ContactTests
{
    private readonly ITestOutputHelper _out;

    public ContactTests(ITestOutputHelper output) => _out = output;

    private static GameData Real => TestData.Real;

    private const string Contact = "civilizations/contact.json";

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static void Send(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>Sends a command and asserts it was accepted.</summary>
    private static void Ok(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        Send(sim, c);
        Assert.True(refused == sim.RefusedCommands, sim.LastRefusal);
    }

    private static void Civ(Simulation sim, string body, DebugCivField field, int value = 0, int target = 0) =>
        Ok(sim, new DebugCivCommand(0, target, body, field, Fix64.FromInt(value)));

    /// <summary>The first seed whose system has a living civilization (not on the landing world).</summary>
    private static (ulong Seed, CivSite Site) FindSeed(GameData data)
    {
        for (ulong seed = 1; seed < 3000; seed++)
        {
            var g = SystemGenerator.Generate(data, seed);
            var site = CivGenerator.Roll(data, g.Bodies, seed, MatchSetup.HomeBody(data, g).Id).FirstOrDefault(s => s.Living);
            if (site != null) return (seed, site);
        }
        throw new InvalidOperationException("no seed has a living civilization");
    }

    private static void GoTo(Simulation sim, string bodyId, int player = 0)
    {
        var sys = sim.System!;
        var ship = sys.FlagshipOf(player)!;
        for (var i = 0; i < 4000 && ship.InFlight; i++) sim.Step();
        if (ship.AtBody == bodyId) return;
        Ok(sim, new SetCourseCommand(player, ship.Id, bodyId));
        for (var i = 0; i < 4000 && ship.AtBody != bodyId; i++) sim.Step();
        Assert.Equal(bodyId, ship.AtBody);
    }

    /// <summary>A system game with the flagship in orbit of a living civilization (made the given level and temperament
    /// with the test panel), Charted and studied: ready for first contact.</summary>
    private static (Simulation Sim, string Body) Ready(GameData data, int level = 5, Temperament temperament = Temperament.Peaceful, string era = "")
    {
        var (seed, site) = FindSeed(data);
        var sim = MatchSetup.CreateSystemGame(data, seed);
        Civ(sim, site.BodyId, DebugCivField.Level, level);
        Civ(sim, site.BodyId, DebugCivField.Temperament, (int)temperament);
        GoTo(sim, site.BodyId);
        Civ(sim, site.BodyId, DebugCivField.FinishStudy);
        Assert.Equal(BodyKnowledge.Charted, sim.System!.KnowledgeOf(0, site.BodyId));
        if (level >= 5) Assert.Null(sim.System.FirstContactProblem(0, site.BodyId));
        return (sim, site.BodyId);
    }

    private static GameData Odds(int shock) => TestData.Patched((Contact, "\"shock_min\": 5,", $"\"shock_min\": {shock},"),
        (Contact, "\"shock_max\": 95,", $"\"shock_max\": {shock},"));

    /// <summary>Starts first contact and picks the first choice at every step.</summary>
    private static void Through(Simulation sim, string body)
    {
        Ok(sim, new FirstContactCommand(0, body));
        var steps = sim.Data.Civilizations!.Contact!.Steps.Count;
        for (var k = 0; k < steps; k++) Ok(sim, new ContactChoiceCommand(0, body, 0));
        Assert.Null(sim.System!.ContactEventOf(0, body));
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void RealDataHasContactRulesAndTheChecksCatchBadOnes()
    {
        var c = Real.Civilizations!.Contact!;
        Assert.Empty(Real.Validate());
        Assert.Equal(3, c.Steps.Count);
        Assert.All(c.Steps, s => Assert.InRange(s.Choices.Count, 2, 3));
        Assert.Equal(new[] { "envoy", "offer", "technology" }, c.Steps.Select(s => s.Id));
        Assert.InRange(c.StepSeconds * c.Steps.Count, 180, 300); // ~4 min
        Assert.Equal(1, c.Ally.ShipsTradePartner);
        Assert.Equal(2, c.Ally.ShipsAllied);
        Assert.Equal(Fix64.FromInt(2), c.AlliedMultiplier);
        Assert.Contains(c.TradeIncome, x => Real.Resources[x.Resource].Id == "latinum");
        Assert.Contains(c.TradeIncome, x => Real.Resources[x.Resource].Id == "duranium");
        Assert.True(c.Advance.ChancePerMin < Fix64.Ratio(1, 100)); // rare: ~1 in 10 over 30 min
        Assert.True(c.Aggressive.ShootDownStanding < 0);

        Assert.Contains(TestData.Patched((Contact, "\"min_level\": 5,", "\"min_level\": 9,")).Validate(), p => p.Contains("min_level"));
        Assert.Contains(TestData.Patched((Contact, "\"buildings\": [\"refinery\"], \"add\": 0.15", "\"buildings\": [\"nowhere\"], \"add\": 0.15")).Validate(),
            p => p.Contains("unknown building 'nowhere'"));
        Assert.Contains(TestData.Patched((Contact, "\"shock_min\": 5,", "\"shock_min\": 99,")).Validate(), p => p.Contains("shock_min"));
        Assert.Contains(TestData.Patched((Contact, "\"walkout_chance\": 0.3,", "\"walkout_chance\": 3,")).Validate(), p => p.Contains("walkout_chance"));
        Assert.Contains(TestData.Patched((Contact, "\"allied\": 2 },\n      \"classes\"", "\"allied\": 0 },\n      \"classes\"")).Validate(),
            p => p.Contains("ships_by_stage"));
    }

    // ---------------------------------------------------------------- the event

    [Fact]
    public void FirstContactIsRefusedWithReasons()
    {
        var (seed, site) = FindSeed(Real);
        var sim = MatchSetup.CreateSystemGame(Real, seed);
        var sys = sim.System!;
        var body = site.BodyId;
        Civ(sim, body, DebugCivField.Level, 5);
        Assert.Contains("not in orbit", sys.FirstContactProblem(0, body));
        Assert.Equal("no such body", sys.FirstContactProblem(0, "nowhere"));
        Assert.Equal("no such player", sys.FirstContactProblem(7, body));
        GoTo(sim, body);
        if (sys.KnowledgeOf(0, body) < BodyKnowledge.Charted) Assert.Contains("sensor sweep", sys.FirstContactProblem(0, body));
        Run(sim, (int)sys.SweepTicks + 2);
        Assert.Contains("finish studying", sys.FirstContactProblem(0, body));
        Civ(sim, body, DebugCivField.FinishStudy);
        Assert.Null(sys.FirstContactProblem(0, body));
        Ok(sim, new FirstContactCommand(0, body));
        Assert.Contains("under way", sys.FirstContactProblem(0, body));
        Assert.Contains("under way", sys.MembershipProblem(0, body));
        // Ruins: nobody to talk to.
        Civ(sim, body, DebugCivField.SpawnRuins);
        Assert.Contains("only ruins", sys.FirstContactProblem(0, body));
        Assert.Contains("no civilization", sys.MembershipProblem(0, body));
        Civ(sim, body, DebugCivField.Remove);
        Assert.Contains("no civilization", sys.FirstContactProblem(0, body));

        // Another power (Cardassian): Federation only.
        var other = MatchSetup.CreateSystemGame(Real, seed, "cardassian");
        Assert.Equal("only the Federation can make first contact for now", other.System!.FirstContactProblem(0, body));
    }

    [Fact]
    public void TheEventRunsStepByStepWithDefaultsWhenTimeRunsOut()
    {
        var (sim, body) = Ready(Real);
        var sys = sim.System!;
        var c = Real.Civilizations!.Contact!;
        var stepTicks = c.StepSeconds * Simulation.TickRate;
        Ok(sim, new FirstContactCommand(0, body));
        var e = sys.ContactEventOf(0, body)!;
        Assert.Equal(0, e.Step);
        Assert.Equal(3, e.Steps);
        Assert.Equal(c.Steps[0].Choices.Count, e.Choices.Count);
        Assert.Equal(c.Steps[0].Title, e.StepTitle);
        Assert.Equal(new[] { -1, -1, -1 }, e.Chosen);
        Assert.Equal(e.StartTick + 3L * stepTicks, e.EndTick);
        Assert.False(e.Violation);
        Assert.Contains(sys.CivEvents, x => x.Kind == "contact_started");
        var before = e.ShockChance;

        // Pick the envoy team (shock -8): on to step 2 at once, the chance drops.
        Ok(sim, new ContactChoiceCommand(0, body, 1));
        e = sys.ContactEventOf(0, body)!;
        Assert.Equal(1, e.Step);
        Assert.Equal(1, e.Chosen[0]);
        Assert.Equal(System.Math.Clamp(before + c.Steps[0].Choices[1].Shock, c.ShockMin, c.ShockMax), e.ShockChance);
        Assert.Equal(sim.Tick - 1 + stepTicks, e.StepEndTick);

        // A choice out of range is refused and changes nothing.
        var h = sim.StateHash();
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, new ContactChoiceCommand(0, body, 9));
        Assert.Equal(refused, sim.RefusedCommands);
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
        Assert.Equal(1, sys.ContactEventOf(0, body)!.Step);

        // Let step 2 run out: its first choice is taken.
        Run(sim, stepTicks);
        e = sys.ContactEventOf(0, body)!;
        Assert.Equal(2, e.Step);
        Assert.Equal(0, e.Chosen[1]);
        Run(sim, stepTicks);
        Assert.Null(sys.ContactEventOf(0, body)); // over: rolled
        Assert.NotEqual(ContactStage.None, sys.RelationsWith(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind is "contact_smooth" or "culture_shock");
        _ = h;
    }

    [Fact]
    public void ASmoothStartMakesATradePartnerWithResearchATechAndAnAlly()
    {
        var data = Odds(0);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        var c = data.Civilizations!.Contact!;
        var bank = sys.ObservationBank(0);
        Through(sim, body);
        Assert.Equal(ContactStage.TradePartner, sys.RelationsWith(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "contact_smooth");
        Assert.Contains(sys.CivEvents, x => x.Kind == "unique_tech");
        // One-off research: the boost (+ the choices' research) waits in the bank with no surface tech active.
        Assert.Equal(bank + Fix64.FromInt(c.ResearchBoost), sys.ObservationBank(0));
        // Unique tech: named, on the surface, and its modifiers count like a researched tech's.
        var name = sys.UniqueTechFrom(0, body);
        Assert.NotNull(name);
        var tech = sim.Surface!.Players[0].SharedTechs.Single();
        Assert.Equal(name, tech.Name);
        var m = tech.Modifiers[0];
        var bonus = m.Units.Count > 0 ? sim.Surface.UnitResearchBonus(0, m.Units[0], m.Stat)
            : sim.Surface.ResearchBonus(0, m.Buildings.Count > 0 ? m.Buildings[0] : "refinery", m.Stat);
        Assert.Equal(m.Add, bonus);
        Assert.Equal(Fix64.Zero, sim.Surface.ResearchBonus(1 - 1 + 0, "nothing_here", "speed"));
        // One ally ship, a native class, at their world.
        var ally = sys.AlliesOf(0).Single();
        Assert.Equal(0, ally.AllyOf);
        Assert.Equal(-1, ally.Owner);
        Assert.False(ally.IsNeutral);
        Assert.Equal(body, ally.AllyHome);
        Assert.Equal(AllyTask.Escort, ally.AllyTask);
        Assert.NotNull(sys.AllyClass(ally));
        Assert.Contains(body, sys.TradePartners(0));
        Assert.Contains("already in contact", sys.FirstContactProblem(0, body));
    }

    [Fact]
    public void CultureShockWaitsBeforeTradeAndShakesTheNatives()
    {
        var data = Odds(100);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        var c = data.Civilizations!.Contact!;
        var awareness = sys.AwarenessOf(body);
        Through(sim, body);
        Assert.Equal(ContactStage.Contact, sys.RelationsWith(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "culture_shock");
        Assert.True(sys.CultureShockSecondsLeft(body) > 0);
        Assert.True(sys.AwarenessOf(body) >= awareness + c.CultureShockAwareness);
        Assert.Empty(sys.AlliesOf(0));
        Assert.Empty(sys.TradeIncomePerMinute(0));
        Assert.Contains("not ready to trade", sys.MembershipProblem(0, body));
        Assert.Null(sys.UniqueTechFrom(0, body));
        // The first choices change the wait by 0 + 0 + 0: shock_wait_minutes.
        Run(sim, c.ShockWaitMinutes * Simulation.TicksPerMinute + 2);
        Assert.Equal(ContactStage.TradePartner, sys.RelationsWith(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "trade_partner");
        Assert.Single(sys.AlliesOf(0));
        Assert.NotNull(sys.UniqueTechFrom(0, body));
    }

    [Fact]
    public void WarpHelpThatGoesWrongCostsStanding()
    {
        var data = Odds(100);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        var standing = sys.StandingOf(0)!.Value;
        Ok(sim, new FirstContactCommand(0, body));
        Ok(sim, new ContactChoiceCommand(0, body, 0));
        Ok(sim, new ContactChoiceCommand(0, body, 0));
        Ok(sim, new ContactChoiceCommand(0, body, 2)); // help with their warp drive
        Assert.Equal(ContactStage.Contact, sys.RelationsWith(0, body));
        var warp = data.Civilizations!.Contact!.Steps[2].Choices[2];
        Assert.Equal(standing + warp.StandingIfShock, sys.StandingOf(0));
    }

    [Fact]
    public void ForcedContactBelowWarpIsAViolation()
    {
        var data = Odds(0); // even with no shock at all, a violation always ends in culture shock
        var (seed, site) = FindSeed(data);
        var sim = MatchSetup.CreateSystemGame(data, seed);
        var sys = sim.System!;
        var body = site.BodyId;
        Civ(sim, body, DebugCivField.Level, 3);
        GoTo(sim, body);
        Civ(sim, body, DebugCivField.FinishStudy);
        Assert.StartsWith("General Order 1", sys.FirstContactProblem(0, body));
        var standing = sys.StandingOf(0)!.Value;
        Ok(sim, new FirstContactCommand(0, body)); // goes ahead anyway
        var e = sys.ContactEventOf(0, body)!;
        Assert.True(e.Violation);
        Assert.Equal(100, e.ShockChance);
        Assert.Equal(standing + data.Civilizations!.Contact!.ViolationStanding, sys.StandingOf(0));
        Assert.Equal(1, sys.IncidentsOn(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "violation");
        Assert.Contains(sys.CivEvents, x => x.Kind == "incident");
        Assert.True(sys.RefusesContactSeconds(0, body) > 0); // an incident: they refuse new contact a while
        for (var k = 0; k < 3; k++) Ok(sim, new ContactChoiceCommand(0, body, 0));
        Assert.Equal(ContactStage.Contact, sys.RelationsWith(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "culture_shock");

        // Before the Prime Directive (ENT): no rule, no Standing, no violation.
        var ent = GameData.Load(TestData.Source, new DataSetup(Era: "ent"));
        var game = MatchSetup.CreateSystemGame(ent, seed);
        Civ(game, body, DebugCivField.Level, 3);
        GoTo(game, body);
        Civ(game, body, DebugCivField.FinishStudy);
        Assert.Null(game.System!.FirstContactProblem(0, body));
        Ok(game, new FirstContactCommand(0, body));
        Assert.False(game.System.ContactEventOf(0, body)!.Violation);
        Assert.Equal(0, game.System.IncidentsOn(0, body));
    }

    [Fact]
    public void LeavingOrbitCallsTheEventOff()
    {
        var (sim, body) = Ready(Real);
        var sys = sim.System!;
        Ok(sim, new FirstContactCommand(0, body));
        Ok(sim, new ContactChoiceCommand(0, body, 0));
        Ok(sim, new SetCourseCommand(0, sys.FlagshipOf(0)!.Id, sys.SurfaceBody!));
        Run(sim, 2);
        Assert.Null(sys.ContactEventOf(0, body));
        Assert.Equal(ContactStage.None, sys.RelationsWith(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "contact_called_off" && x.Text.Contains("left orbit"));
        // Back in orbit, it can start again.
        GoTo(sim, body);
        Assert.Null(sys.FirstContactProblem(0, body));
    }

    // ---------------------------------------------------------------- relations, trade, techs

    [Fact]
    public void MembershipGoesTradePartnerCandidateAlliedWithAStandingGate()
    {
        var data = Odds(0);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        var m = data.Civilizations!.Contact!.Membership;
        Assert.Contains("first contact", sys.MembershipProblem(0, body));
        Through(sim, body);
        Assert.Contains("trade partners for", sys.MembershipProblem(0, body));
        Run(sim, m.PartnerMinutes * Simulation.TicksPerMinute);
        // Standing gate.
        Civ(sim, body, DebugCivField.Standing, m.MinStanding - 5);
        Assert.Contains($"Standing {m.MinStanding} needed", sys.MembershipProblem(0, body));
        var refused = sim.RefusedCommands;
        Send(sim, new MembershipCommand(0, body));
        Assert.Equal(refused + 1, sim.RefusedCommands);
        Assert.Equal(ContactStage.TradePartner, sys.RelationsWith(0, body));
        Civ(sim, body, DebugCivField.Standing, 80);
        Ok(sim, new MembershipCommand(0, body));
        Assert.Equal(ContactStage.Candidate, sys.RelationsWith(0, body));
        Assert.Contains("membership talks", sys.MembershipProblem(0, body));
        Run(sim, m.CandidateMinutes * Simulation.TicksPerMinute);
        Ok(sim, new MembershipCommand(0, body));
        Assert.Equal(ContactStage.Allied, sys.RelationsWith(0, body));
        Assert.Contains("allied world", sys.MembershipProblem(0, body));
        Assert.Equal(2, sys.AlliesOf(0).Count);
        Assert.Contains(sys.CivEvents, x => x.Kind == "membership" && x.Text.Contains("allied world"));
    }

    [Fact]
    public void AFracturedWorldMayHaveANationWalkOut()
    {
        var data = TestData.Patched((Contact, "\"shock_min\": 5,", "\"shock_min\": 0,"), (Contact, "\"shock_max\": 95,", "\"shock_max\": 0,"),
            (Contact, "\"walkout_chance\": 0.3,", "\"walkout_chance\": 1,"), (Contact, "\"partner_minutes\": 3,", "\"partner_minutes\": 0,"),
            (Contact, "\"candidate_minutes\": 5,", "\"candidate_minutes\": 0,"));
        var (seed, site) = FindSeed(data);
        var sim = MatchSetup.CreateSystemGame(data, seed);
        var sys = sim.System!;
        var body = site.BodyId;
        Civ(sim, body, DebugCivField.Level, 5);
        Civ(sim, body, DebugCivField.Relations, 2);
        var full = sys.TradeIncomePerMinute(0).Sum(x => x.Amount.ToDouble());
        Ok(sim, new MembershipCommand(0, body));
        Assert.Equal(ContactStage.Candidate, sys.RelationsWith(0, body));
        var expected = sys.CivAt(body)!.Unity == CivUnity.Fractured ? 1 : 0;
        Assert.Equal(expected, sys.WalkedOut(0, body));
        if (expected == 1)
        {
            Assert.Contains(sys.CivEvents, x => x.Kind == "walkout");
            var cut = data.Civilizations!.Contact!.Membership.WalkoutIncomeCut.ToDouble();
            Assert.Equal(full * (1 - cut), sys.TradeIncomePerMinute(0).Sum(x => x.Amount.ToDouble()), 3);
        }
    }

    [Fact]
    public void TradeIncomeArrivesEveryMinuteUnderTheStorageRulesAndAlliesPayDouble()
    {
        var (seed, site) = FindSeed(Real);
        var sim = MatchSetup.CreateSystemGame(Real, seed);
        var sys = sim.System!;
        var body = site.BodyId;
        var c = Real.Civilizations!.Contact!;
        Civ(sim, body, DebugCivField.Level, 5);
        Civ(sim, body, DebugCivField.Relations, (int)ContactStage.TradePartner);
        var income = sys.TradeIncomePerMinute(0);
        Assert.Equal(c.TradeIncome, income);
        var latinum = Real.Resource("latinum").Index;
        var duranium = Real.Resource("duranium").Index;
        var stock = sim.Players[0].Stock;
        // Not landed: no storage, so only latinum (storage-exempt) arrives.
        Run(sim, Simulation.TicksPerMinute - (int)(sim.Tick % Simulation.TicksPerMinute));
        var l0 = stock[latinum];
        var d0 = stock[duranium];
        Run(sim, Simulation.TicksPerMinute);
        Assert.Equal(l0 + c.TradeIncome.Single(x => x.Resource == latinum).Amount, stock[latinum]);
        Assert.Equal(d0, stock[duranium]);
        // With room (test panel: no storage cap) the metal arrives too.
        Ok(sim, new DebugFlagCommand(0, 0, DebugFlag.NoStorageCap, true));
        Run(sim, Simulation.TicksPerMinute - (int)(sim.Tick % Simulation.TicksPerMinute));
        d0 = stock[duranium];
        Run(sim, Simulation.TicksPerMinute);
        Assert.Equal(d0 + c.TradeIncome.Single(x => x.Resource == duranium).Amount, stock[duranium]);
        // Allied worlds pay double.
        Civ(sim, body, DebugCivField.Relations, (int)ContactStage.Allied);
        Assert.Equal(c.TradeIncome.Select(x => x.Amount * 2), sys.TradeIncomePerMinute(0).Select(x => x.Amount));
        Assert.Equal(2, sys.AlliesOf(0).Count);
    }

    // ---------------------------------------------------------------- ally ships

    [Fact]
    public void AnEscortFollowsTheFlagshipAndAScoutChartsABody()
    {
        var data = Odds(0);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        var c = data.Civilizations!.Contact!;
        Through(sim, body);
        var ally = sys.AlliesOf(0).Single();
        var flagship = sys.FlagshipOf(0)!;
        Assert.Equal(body, ally.AtBody);
        Assert.Contains("already escorting", sys.AllyOrderProblem(0, ally.Id, null));
        Assert.Equal("not one of your allies", sys.AllyOrderProblem(1, ally.Id, null));
        Assert.Equal("no such ship", sys.AllyOrderProblem(0, 9999, null));

        // The flagship leaves: the escort follows escort_delay_seconds later.
        var home = sys.SurfaceBody!;
        Ok(sim, new SetCourseCommand(0, flagship.Id, home));
        Run(sim, c.Ally.EscortDelaySeconds * Simulation.TickRate - 3);
        Assert.Equal(body, ally.AtBody);
        Run(sim, 5);
        Assert.True(ally.InFlight);
        Assert.Equal(home, ally.ToBody);
        for (var i = 0; i < 4000 && ally.AtBody != home; i++) sim.Step();
        Assert.Equal(home, ally.AtBody);
        Assert.True(ally.ArriveTick > flagship.ArriveTick || flagship.AtBody == home);

        // Scout a body nobody has charted: it flies there, sweeps, charts it for us, then comes back to escort.
        var target = sys.Bodies.First(b => sys.KnowledgeOf(0, b.Id) < BodyKnowledge.Charted && b.Id != home).Id;
        Assert.Null(sys.AllyOrderProblem(0, ally.Id, target));
        Ok(sim, new AllyShipOrderCommand(0, ally.Id, target));
        Assert.Equal(AllyTask.Scout, ally.AllyTask);
        Assert.Equal(target, ally.ScoutBody);
        Assert.Contains("already scouting", sys.AllyOrderProblem(0, ally.Id, target));
        Run(sim, 2);
        Assert.Equal(target, ally.ToBody);
        for (var i = 0; i < 6000 && sys.KnowledgeOf(0, target) < BodyKnowledge.Charted; i++) sim.Step();
        Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, target));
        Assert.Equal(AllyTask.Escort, ally.AllyTask);
        Assert.Contains(sys.CivEvents, x => x.Kind == "scout_charted" && x.BodyId == target);
        Assert.Contains("already charted", sys.AllyOrderProblem(0, ally.Id, target));
        Run(sim, c.Ally.EscortDelaySeconds * Simulation.TickRate + 2);
        Assert.Equal(home, ally.ToBody ?? ally.AtBody);
        // Allies are not neutral traffic: they never wander off on their own.
        Assert.DoesNotContain(sys.Ships.Where(s => s.IsNeutral), s => s.AllyOf >= 0);
    }

    // ---------------------------------------------------------------- aggressive natives, L4 → L5

    [Fact]
    public void AggressiveNativesShootDownSatellitesAndPostsAndRefuseContact()
    {
        var data = TestData.Patched((Contact, "\"shoot_chance\": 0.25,", "\"shoot_chance\": 1,"),
            ("civilizations/rules.json", "\"discovery_minutes\": 10,", "\"discovery_minutes\": 100,"));
        var (sim, body) = Ready(data, 4, Temperament.Aggressive);
        var sys = sim.System!;
        var g = data.Civilizations!.Contact!.Aggressive;
        Ok(sim, new SystemSatelliteCommand(0, body));
        Ok(sim, new ObservationPostCommand(0, body));
        Assert.Single(sys.Satellites.Where(s => s.BodyId == body));
        var standing = sys.StandingOf(0)!.Value;
        Civ(sim, body, DebugCivField.Awareness, 85);
        Run(sim, Simulation.TicksPerMinute + 1);
        Assert.Empty(sys.Satellites.Where(s => s.BodyId == body));
        var post = sys.Posts.Single();
        Assert.True(post.ShotDown);
        Assert.True(post.Found);
        Assert.Equal(standing + 2 * g.ShootDownStanding, sys.StandingOf(0));
        Assert.Contains(sys.CivEvents, x => x.Kind == "hostile");
        Assert.Equal(2, sys.CivEvents.Count(x => x.Kind == "shot_down"));
        Assert.InRange(sys.RefusesContactSeconds(0, body), (g.RefuseMinutes - 1) * 60, g.RefuseMinutes * 60);
        Assert.Contains("refuse contact", sys.FirstContactProblem(0, body));
        // Below L4 nothing is shot down (but they still refuse).
        var (low, b2) = Ready(data, 3, Temperament.Aggressive);
        Ok(low, new SystemSatelliteCommand(0, b2));
        Civ(low, b2, DebugCivField.Awareness, 85);
        Run(low, Simulation.TicksPerMinute + 1);
        Assert.Single(low.System!.Satellites.Where(s => s.BodyId == b2));
        Assert.True(low.System.RefusesContactSeconds(0, b2) > 0);
    }

    [Fact]
    public void AnAtomicWorldMayReachWarpAndASpikeCanWreckIt()
    {
        var data = TestData.Patched((Contact, "\"chance_per_min\": 0.0035,", "\"chance_per_min\": 1,"));
        var (seed, site) = FindSeed(data);
        var sim = MatchSetup.CreateSystemGame(data, seed);
        var sys = sim.System!;
        var body = site.BodyId;
        Civ(sim, body, DebugCivField.Level, 4);
        Run(sim, Simulation.TicksPerMinute + 1);
        Assert.Equal(CivLevel.EarlyWarp, sys.CivAt(body)!.Level);

        var wreck = TestData.Patched((Contact, "\"chance_per_min\": 0.0035,", "\"chance_per_min\": 1,"), (Contact, "\"wreck_chance\": 0.75", "\"wreck_chance\": 1"));
        var game = MatchSetup.CreateSystemGame(wreck, seed);
        GoTo(game, body); // charted: the player hears of it
        Run(game, (int)game.System!.SweepTicks + 2);
        Civ(game, body, DebugCivField.Level, 4);
        Civ(game, body, DebugCivField.Awareness, game.System.AwarenessOf(body) + 30); // a spike since the last minute's mark
        Run(game, Simulation.TicksPerMinute - (int)(game.Tick % Simulation.TicksPerMinute) + 1);
        Assert.Equal(CivLevel.Atomic, game.System.CivAt(body)!.Level);
        Assert.Contains(game.System.CivEvents, x => x.Kind == "warp_wrecked");
        Run(game, Simulation.TicksPerMinute); // no spike this minute: it flies
        Assert.Equal(CivLevel.EarlyWarp, game.System.CivAt(body)!.Level);
        Assert.Contains(game.System.CivEvents, x => x.Kind == "warp");

        // Real odds: rare (about 1 in 10 over 30 minutes with no contamination).
        var c = Real.Civilizations!.Contact!.Advance;
        var never = 1 - System.Math.Pow(1 - c.ChancePerMin.ToDouble(), 30);
        Assert.InRange(never, 0.05, 0.15);
    }

    // ---------------------------------------------------------------- the test panel

    [Fact]
    public void EveryDebugFieldWorksAndBadOnesAreRefused()
    {
        var (seed, site) = FindSeed(Real);
        var sim = MatchSetup.CreateSystemGame(Real, seed);
        var sys = sim.System!;
        var body = site.BodyId;
        string? P(DebugCivField f, int v = 0, string? b = null, int target = 0) =>
            sim.DebugProblem(new DebugCivCommand(0, target, b ?? body, f, Fix64.FromInt(v)));

        Assert.Equal("no such body", P(DebugCivField.Level, 3, "nowhere"));
        Assert.Contains("landing world", P(DebugCivField.SpawnLiving, 3, sys.SurfaceBody));
        Assert.Contains("level must be", P(DebugCivField.SpawnLiving, 9));
        Assert.Contains("level must be", P(DebugCivField.Level, 0));
        Assert.Equal("awareness must be 0..100", P(DebugCivField.Awareness, 101));
        Assert.Contains("temperament", P(DebugCivField.Temperament, 4));
        Assert.Equal("no such player", P(DebugCivField.FinishStudy, 0, null, 5));
        Assert.Contains("Standing must be", P(DebugCivField.Standing, 101));
        Assert.Contains("no working post", P(DebugCivField.FindPost));
        Assert.Equal("relations must be 0..4", P(DebugCivField.Relations, 5));
        Assert.NotNull(sim.DebugProblem(new DebugCivCommand(0, 0, body, DebugCivField.Level, Fix64.FromInt(3) + Fix64.Half)));
        var belt = sys.Bodies.FirstOrDefault(b => Real.SystemGenerator!.IsBelt(b.ClassId));
        if (belt != null) Assert.Contains("no ground", P(DebugCivField.SpawnLiving, 3, belt.Id));

        // Spawn living with a generated species, then each field.
        Civ(sim, body, DebugCivField.SpawnLiving, 2);
        var s = sys.CivAt(body)!;
        Assert.True(s.Living);
        Assert.Equal(CivLevel.IronMedieval, s.Level);
        Assert.False(string.IsNullOrEmpty(s.Species));
        Civ(sim, body, DebugCivField.Level, 6);
        Assert.Equal(CivLevel.Spacefaring, sys.CivAt(body)!.Level);
        Civ(sim, body, DebugCivField.Awareness, 42);
        Assert.Equal(42, sys.AwarenessOf(body));
        Civ(sim, body, DebugCivField.Temperament, 3);
        Assert.Equal(Temperament.Aggressive, sys.CivAt(body)!.Temperament);
        Civ(sim, body, DebugCivField.FinishStudy);
        Assert.Equal(Fix64.One, sys.StudyOf(0, body));
        Assert.Equal(BodyKnowledge.Charted, sys.KnowledgeOf(0, body));
        // A post, found on demand.
        GoTo(sim, body);
        Ok(sim, new ObservationPostCommand(0, body));
        Civ(sim, body, DebugCivField.FindPost);
        Assert.True(sys.Posts.Single().Found);
        Assert.Equal(1, sys.IncidentsOn(0, body));
        Civ(sim, body, DebugCivField.Standing, 45);
        Assert.Equal(45, sys.StandingOf(0));
        Assert.Equal(2, sys.StandingRung(0));
        Civ(sim, body, DebugCivField.Standing, 80);
        Assert.Equal(0, sys.StandingRung(0));
        Civ(sim, body, DebugCivField.Relations, 4);
        Assert.Equal(ContactStage.Allied, sys.RelationsWith(0, body));
        Assert.Equal(2, sys.AlliesOf(0).Count);
        Civ(sim, body, DebugCivField.Relations, 1);
        Assert.Equal(ContactStage.Contact, sys.RelationsWith(0, body));
        Assert.Empty(sys.AlliesOf(0));
        Civ(sim, body, DebugCivField.Relations, 0);
        Assert.Equal(ContactStage.None, sys.RelationsWith(0, body));
        // Ruins, then remove.
        Civ(sim, body, DebugCivField.SpawnRuins);
        Assert.False(sys.CivAt(body)!.Living);
        Assert.Contains("no living civilization", P(DebugCivField.Level, 3));
        Civ(sim, body, DebugCivField.Remove);
        Assert.Null(sys.CivAt(body));
        Assert.Contains("no civilization or ruins", P(DebugCivField.Remove));
        // A new site on another body (inserted in body order; study arrays follow).
        var other = sys.Bodies.FirstOrDefault(b => b.Id != sys.SurfaceBody && sys.CivAt(b.Id) == null
            && (Real.IsLandableClass(b.ClassId) || Real.Civilizations!.Sites.TownMapFor(b.ClassId) != null));
        if (other != null)
        {
            Civ(sim, other.Id, DebugCivField.SpawnLiving, 4);
            Assert.Equal(CivLevel.Atomic, sys.CivAt(other.Id)!.Level);
        }
        Assert.Equal(sys.Civilizations.Select(c => sys.Bodies.ToList().FindIndex(b => b.Id == c.BodyId)).OrderBy(i => i), sys.Civilizations.Select(c => sys.Bodies.ToList().FindIndex(b => b.Id == c.BodyId)));
        Run(sim, 60);
        // Standing: a player without it (Cardassian added by the test panel) is refused.
        Ok(sim, new DebugAddPlayerCommand(0, "cardassian"));
        Assert.Contains("no Starfleet Standing", P(DebugCivField.Standing, 50, null, 1));
        Assert.True(sim.DebugEdits > 10);
        // A planet-only game has no star system for it.
        var planet = MatchSetup.CreatePlanetLanding(Real, 1701, "federation", "class_m", "tarsis");
        Assert.Equal("no star system", planet.DebugProblem(new DebugCivCommand(0, 0, body, DebugCivField.Level, Fix64.One)));
    }

    // ---------------------------------------------------------------- determinism, the hash, refusals

    [Fact]
    public void SameCommandsSameGameAndContactStateIsHashed()
    {
        var data = TestData.Patched((Contact, "\"chance_per_min\": 0.0035,", "\"chance_per_min\": 0.3,"),
            (Contact, "\"walkout_chance\": 0.3,", "\"walkout_chance\": 0.5,"));
        var games = new[] { Ready(data), Ready(data) };
        Assert.Equal(games[0].Sim.StateHash(), games[1].Sim.StateHash());
        foreach (var (sim, body) in games)
        {
            var h = sim.StateHash();
            Send(sim, new FirstContactCommand(0, body));
            Assert.NotEqual(h, sim.StateHash());
            Run(sim, 3 * data.Civilizations!.Contact!.StepSeconds * Simulation.TickRate + 5); // all defaults, rolled on sim.Rng
            Run(sim, 8 * Simulation.TicksPerMinute);
            if (sim.System!.MembershipProblem(0, body) == null) Send(sim, new MembershipCommand(0, body));
            Run(sim, 3 * Simulation.TicksPerMinute);
        }
        Assert.Equal(games[0].Sim.StateHash(), games[1].Sim.StateHash());
        Assert.Equal(games[0].Sim.System!.CivEvents, games[1].Sim.System!.CivEvents);
        _out.WriteLine(string.Join("\n", games[0].Sim.System!.CivEvents.Select(e => $"{e.Tick} {e.Kind}: {e.Text}")));
        // The ally's task is in the hash.
        var (g, b) = games[0];
        if (g.System!.AlliesOf(0).FirstOrDefault() is { } ally)
        {
            var target = g.System.Bodies.FirstOrDefault(x => g.System.KnowledgeOf(0, x.Id) < BodyKnowledge.Charted);
            if (target != null)
            {
                var before = g.StateHash();
                Send(g, new AllyShipOrderCommand(0, ally.Id, target.Id));
                Send(games[1].Sim, new AllyShipOrderCommand(0, ally.Id, target.Id));
                Assert.NotEqual(before, g.StateHash());
                Assert.Equal(g.StateHash(), games[1].Sim.StateHash());
            }
        }
        _ = b;
    }

    [Fact]
    public void RefusedContactCommandsChangeNothing()
    {
        var (a, body) = Ready(Real);
        var (b, _) = Ready(Real);
        Assert.Equal(a.StateHash(), b.StateHash());
        var ally = 9999;
        foreach (var c in new Command[]
                 {
                     new ContactChoiceCommand(0, body, 0), // no event
                     new MembershipCommand(0, body), // no contact yet
                     new AllyShipOrderCommand(0, ally, null),
                     new AllyShipOrderCommand(0, ally, body),
                     new FirstContactCommand(0, "nowhere"),
                     new FirstContactCommand(5, body),
                     new MembershipCommand(0, "nowhere"),
                     new DebugCivCommand(0, 0, body, DebugCivField.Level, Fix64.FromInt(9)),
                     new DebugCivCommand(0, 0, "nowhere", DebugCivField.Remove, Fix64.Zero),
                 })
        {
            var refused = a.RefusedCommands;
            a.Schedule(a.Tick, c);
            a.Step();
            b.Step();
            Assert.Equal(refused + 1, a.RefusedCommands);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
    }
}
