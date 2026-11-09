using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Civilizations phase 2, star-system view side (docs/civilizations-phase2-contract.md "View"; render-only, commands only
// through ContactOrders): the FIRST CONTACT button on the body card and the dossier (FirstContactProblem; below L5 the
// sim's General Order 1 text is a warning: the first click shows it and turns the button into CONTACT ANYWAY, a second
// click within 8 s sends it), the first-contact event panel (ContactPanel: step N of M, its timer, 2–3 choices with
// hints -> ContactChoiceCommand, the shock-chance meter, a result line when the talks end), the relations rows and
// track (Contact -> Trade partner -> Candidate -> Allied world) with MEMBERSHIP (MembershipProblem), the refusal
// countdown (RefusesContactSeconds), trade income (TradeIncomePerMinute; also a line in the top bar's tooltips) and the
// unique tech (UniqueTechFrom); allied native ships: their card with ESCORT and SCOUT (SCOUT arms a pick: the next body
// clicked on the plot or in the rail) -> AllyShipOrderCommand (AllyOrderProblem). The sim's own CivEvents carry the
// event lines (PlanetDriver.Civ.cs reads them).
public partial class PlanetDriver
{
    private ContactPanel _contactPanel = null!;
    private string? _contactBody;
    private double _contactScanAt = -1;
    private ContactEvent? _contactLast;
    private (string BodyId, string Text, Color Colour, double Until)? _contactResult;
    private string? _contactConfirm;
    private double _contactConfirmUntil;
    private int? _allyScout;
    private string _tradeLine = "";

    private const double ConfirmSeconds = 8;

    private void SetupContact(CanvasLayer layer)
    {
        _contactPanel = new ContactPanel { Name = "ContactPanel" };
        layer.AddChild(_contactPanel);
        layer.MoveChild(_contactPanel, 0); // under the dossier
        _contactPanel.ChoicePicked += OnContactChoice;
        _contactPanel.Dismissed += () => _contactResult = null;
    }

    private ContactStage StageOf(string bodyId) => _sim.System?.RelationsWith(LocalPlayer, bodyId) ?? ContactStage.None;

    private bool FlagshipAt(string bodyId) => _system.Flagship is { } f && f.AtBody == bodyId && !SystemSource.InFlight(f);

    private static string Clock(int seconds) => $"{Mathf.Max(0, seconds) / 60}:{Mathf.Max(0, seconds) % 60:00}";

    // ---------------------------------------------------------------- card rows and buttons

    /// <summary>The relations rows of a living, charted civilization (the body card / panel).</summary>
    private void AddContactRows(BodyDef b, CivReport r, List<HelmRow> rows)
    {
        if (_sim.System is not { } sys || !r.Charted || !r.Living) return;
        var stage = StageOf(b.Id);
        var ev = sys.ContactEventOf(LocalPlayer, b.Id);
        if (stage != ContactStage.None) rows.Add(new HelmRow("RELATIONS", CivText.StageName(stage).ToLowerInvariant(), CivText.StageColour(stage)));
        if (ev != null) rows.Add(new HelmRow("TALKS", $"first contact · step {ev.Step + 1} of {ev.Steps} · shock risk {ev.ShockChance} %", HelmScreen.Peach));
        if (sys.RefusesContactSeconds(LocalPlayer, b.Id) is var refuse and > 0) rows.Add(new HelmRow("REFUSES", $"they refuse contact · {Clock(refuse)} left", HelmScreen.Red));
        if (stage >= ContactStage.TradePartner && _tradeLine.Length > 0) rows.Add(new HelmRow("TRADE", TradeShort(), HelmScreen.Cyan));
        if (sys.UniqueTechFrom(LocalPlayer, b.Id) is { Length: > 0 } tech) rows.Add(new HelmRow("THEIR TECH", tech, HelmScreen.Green));
        if (UnrestText(b.Id) is { } unrest) rows.Add(new HelmRow("UNREST", unrest, HelmScreen.Red));
        if (CivText.Studied(r) && sys.IsSpacefaring(b.Id)) rows.Add(new HelmRow("SPACEFARING", SpacefaringText(), HelmScreen.Sky));
        if (stage == ContactStage.None && CivText.Studied(r) && sys.ContactRisk(LocalPlayer, b.Id) is { } risk) rows.Add(new HelmRow("RISK", risk, HelmScreen.Rust));
    }

    /// <summary>"unsettled after culture shock · 9:12 left · trade at 50% for 7:40" while the natives are unsettled, else null.</summary>
    private string? UnrestText(string bodyId)
    {
        if (_sim.System is not { } sys || !sys.Unsettled(bodyId)) return null;
        var text = $"unsettled after culture shock · {Clock(sys.CultureShockSecondsLeft(bodyId))} left";
        var share = _data.Civilizations?.Contact?.Unrest.TradeShare.ToFloat() ?? 0.5f;
        var half = sys.UnrestTradeSecondsLeft(LocalPlayer, bodyId);
        if (half > 0) text += $" · trade at {Mathf.RoundToInt(share * 100)}% for {Clock(half)}";
        else if (StageOf(bodyId) == ContactStage.Contact) text += $" · trade starts at {Mathf.RoundToInt(share * 100)}%";
        return text;
    }

    /// <summary>What an L6 partner gives (contact.json spacefaring).</summary>
    private string SpacefaringText()
    {
        if (_data.Civilizations?.Contact is not { } c) return "a stronger partner";
        var sf = c.Spacefaring;
        return $"a stronger partner: trade ×{sf.TradeMultiplier.ToFloat():0.##}, {sf.AllyShips} ally ships, membership in {sf.PartnerMinutes} + {sf.CandidateMinutes} min; "
            + $"wary of a Standing below {sf.LowStanding}";
    }

    /// <summary>FIRST CONTACT (flagship in orbit, no relations yet) or MEMBERSHIP (contacted, not allied yet).</summary>
    private void AddContactButtons(BodyDef b, CivReport r, List<HelmAction> buttons)
    {
        if (ContactAction(b.Id, r) is { } a) buttons.Add(a);
    }

    private HelmAction? ContactAction(string bodyId, CivReport r)
    {
        if (_sim.System is not { } sys || !r.Charted || !r.Living) return null;
        var stage = StageOf(bodyId);
        if (stage == ContactStage.None)
        {
            if (!FlagshipAt(bodyId)) return null;
            if (sys.ContactEventOf(LocalPlayer, bodyId) != null) return new HelmAction($"civ:contact:{bodyId}", "", "IN TALKS…", false, Colour: HelmScreen.Peach);
            if (sys.RefusesContactSeconds(LocalPlayer, bodyId) is var refuse and > 0)
                return new HelmAction($"civ:contact:{bodyId}", "", $"REFUSED {Clock(refuse)}", false, Colour: HelmScreen.Red);
            var problem = sys.FirstContactProblem(LocalPlayer, bodyId);
            var gentle = ContactOrders.IsGeneralOrder(problem);
            var armed = _contactConfirm == bodyId && _clock < _contactConfirmUntil;
            if (armed) return new HelmAction($"civ:contact:{bodyId}", "", "CONTACT ANYWAY", true, Colour: HelmScreen.Red);
            return new HelmAction($"civ:contact:{bodyId}", "", gentle ? "FIRST CONTACT ⚠" : "FIRST CONTACT", problem == null || gentle, Colour: gentle ? HelmScreen.Rust : HelmScreen.Green);
        }
        if (stage >= ContactStage.Allied) return null;
        var next = stage switch { ContactStage.TradePartner => " → CANDIDATE", ContactStage.Candidate => " → ALLIED", _ => "" };
        return new HelmAction($"civ:member:{bodyId}", "", $"MEMBERSHIP{next}", sys.MembershipProblem(LocalPlayer, bodyId) == null, Colour: HelmScreen.Lavender);
    }

    /// <summary>"civ:contact:<body>" / "civ:member:<body>" from the card, the strip or the dossier. True = handled.</summary>
    private bool OnContactAction(string id)
    {
        var when = _sim.Tick + InputDelayTicks;
        var warn = new Color(1f, 0.7f, 0.42f);
        if (id.StartsWith("civ:contact:"))
        {
            var bodyId = id[12..];
            var name = _system.Body(bodyId) is { } b ? _system.KnownName(b) : bodyId;
            var sys = _sim.System;
            if (sys == null)
            {
                _hud.PushEvent("Can't make first contact: there is no star system in this game", warn);
                return true;
            }
            if (!FlagshipAt(bodyId))
            {
                _hud.PushEvent("Can't make first contact: the flagship must be in orbit there", warn);
                return true;
            }
            var problem = sys.FirstContactProblem(LocalPlayer, bodyId);
            var gentle = ContactOrders.IsGeneralOrder(problem);
            var armed = _contactConfirm == bodyId && _clock < _contactConfirmUntil;
            if (gentle && !armed)
            {
                _contactConfirm = bodyId;
                _contactConfirmUntil = _clock + ConfirmSeconds;
                _hud.PushEvent($"General Order 1: {problem}. Press CONTACT ANYWAY within {ConfirmSeconds:0} s to break it (culture shock then costs Standing 10)", HelmScreen.Red);
                return true;
            }
            _contactConfirm = null;
            var why = ContactOrders.FirstContact(_sim, LocalPlayer, bodyId, when, armed);
            if (why != null) _hud.PushEvent($"Can't make first contact: {why}", warn);
            else
            {
                _contactBody = bodyId;
                _contactResult = null;
                _hud.PushEvent(gentle ? $"Hailing {name} against General Order 1: first contact talks begin" : $"Hailing {name}: first contact talks begin (about 4 minutes, choices along the way)",
                    gentle ? HelmScreen.Red : HelmScreen.Cyan);
            }
            return true;
        }
        if (id.StartsWith("civ:member:"))
        {
            var bodyId = id[11..];
            var stage = StageOf(bodyId);
            var why = ContactOrders.Membership(_sim, LocalPlayer, bodyId, when);
            if (why != null) _hud.PushEvent($"Can't take relations further: {why}", warn);
            else _hud.PushEvent(stage == ContactStage.TradePartner ? "Membership application sent: they become a candidate world" : "Alliance proposed: they become an allied world (it stays theirs)", HelmScreen.Cyan);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- dossier

    /// <summary>The dossier's relations strip (living, charted civilizations), or null.</summary>
    private DossierContact? ContactFor(string bodyId, CivReport r)
    {
        if (_sim.System is not { } sys || !r.Charted || !r.Living) return null;
        var stage = StageOf(bodyId);
        var ev = sys.ContactEventOf(LocalPlayer, bodyId);
        var refuse = sys.RefusesContactSeconds(LocalPlayer, bodyId);
        string next;
        if (stage == ContactStage.None)
        {
            var problem = sys.FirstContactProblem(LocalPlayer, bodyId);
            next = ev != null ? "talks under way"
                : refuse > 0 ? "they refuse contact for now"
                : problem == null ? "first contact possible: FIRST CONTACT"
                : ContactOrders.IsGeneralOrder(problem) ? $"General Order 1: {problem}"
                : $"first contact: {problem}";
        }
        else if (stage >= ContactStage.Allied) next = "allied: nothing further";
        else next = sys.MembershipProblem(LocalPlayer, bodyId) is { } why ? $"next step: {why}" : "next step ready: MEMBERSHIP";
        var talks = ev == null ? "" : $"first contact · step {ev.Step + 1} of {ev.Steps} · shock risk {ev.ShockChance} %";
        var extra = new List<string>();
        if (UnrestText(bodyId) is { } unrest) extra.Add(unrest);
        if (CivText.Studied(r) && sys.IsSpacefaring(bodyId)) extra.Add("spacefaring: " + SpacefaringText());
        if (stage == ContactStage.None && CivText.Studied(r) && sys.ContactRisk(LocalPlayer, bodyId) is { } risk) extra.Add("risk: " + risk);
        return new DossierContact(stage, stage >= ContactStage.TradePartner ? TradeShort() : "", sys.UniqueTechFrom(LocalPlayer, bodyId) ?? "", refuse, talks, next,
            string.Join("  ·  ", extra));
    }

    // ---------------------------------------------------------------- trade

    /// <summary>"+3 DUR · +1 LAT a minute (all worlds)", or "" with no income.</summary>
    private string TradeShort()
    {
        var income = _sim.System?.TradeIncomePerMinute(LocalPlayer);
        if (income == null || income.Count == 0) return "";
        return string.Join(" · ", income.Where(a => a.Amount != Fix64Zero).Select(a => $"+{Hud.Whole(a.Amount)} {ShortResource(_data.Resources[a.Resource].Name)}")) + " a minute (all worlds)";
    }

    private static readonly FinalFrontier.Sim.Math.Fix64 Fix64Zero = FinalFrontier.Sim.Math.Fix64.Zero;

    /// <summary>Once a second: the top bar's tooltip line for trade income.</summary>
    private void RefreshTrade()
    {
        var income = _sim.System?.TradeIncomePerMinute(LocalPlayer);
        _tradeLine = income == null || income.Count == 0 ? ""
            : "Trade with natives: " + string.Join(", ", income.Where(a => a.Amount != Fix64Zero).Select(a => $"+{Hud.Whole(a.Amount)} {_data.Resources[a.Resource].Name}")) + " a minute";
        _hud.TradeTipLine = _tradeLine;
    }

    // ---------------------------------------------------------------- the event panel

    /// <summary>Every frame (UpdateCiv): find the running first-contact event, feed the panel, the result line after.</summary>
    private void UpdateContact()
    {
        if (_contactConfirm != null && _clock >= _contactConfirmUntil) _contactConfirm = null;
        var sys = _sim.System;
        ContactEvent? ev = null;
        if (sys != null)
        {
            if (_contactBody != null) ev = sys.ContactEventOf(LocalPlayer, _contactBody);
            if (ev == null && (_clock - _contactScanAt >= 1.0 || _contactScanAt < 0))
            {
                _contactScanAt = _clock;
                foreach (var b in _system.Bodies)
                    if (sys.ContactEventOf(LocalPlayer, b.Id) is { } found)
                    {
                        ev = found;
                        _contactBody = b.Id;
                        break;
                    }
            }
        }
        if (ev == null && _contactLast != null) ContactEnded(_contactLast);
        _contactLast = ev;
        if (_contactResult is { } res && _clock >= res.Until) _contactResult = null;

        ContactPanelView? view = null;
        if (ev != null) view = EventView(ev);
        else if (_contactResult is { } done)
            view = new ContactPanelView(done.BodyId, $"FIRST CONTACT · {BodyTitle(done.BodyId)}", "", 0, 0, 0, 0, 0,
                System.Array.Empty<ContactChoiceView>(), -1, 0, done.Text, done.Colour);
        if (_dossier.IsOpen) view = null; // the dossier shows the talks' line
        var screen = GetViewport().GetVisibleRect().Size;
        if (_inOrbit)
        {
            var rect = _helm.ViewRect;
            _contactPanel.Show(view, rect.Size.X > 32 ? rect.Position.X + rect.Size.X / 2f : screen.X / 2f, rect.Size.Y > 32 ? rect.Position.Y + 10f : 80f);
        }
        else _contactPanel.Show(view, screen.X / 2f, PlanetHud.TopHeight + 96f);
    }

    private string BodyTitle(string bodyId) => (_system.Body(bodyId) is { } b ? _system.KnownName(b) : bodyId).ToUpperInvariant();

    private ContactPanelView EventView(ContactEvent ev)
    {
        var r = CivOf(ev.BodyId);
        var who = r?.Species is { Length: > 0 } ? CivText.The(r).ToUpperInvariant() : "THE NATIVES";
        var rate = (float)Simulation.TickRate;
        var stepLeft = Mathf.Max(0f, (ev.StepEndTick - _sim.Tick) / rate);
        var steps = Mathf.Max(1, ev.Steps);
        var stepSeconds = Mathf.Max(1f, (ev.EndTick - ev.StartTick) / rate / steps);
        var chosen = ev.Step >= 0 && ev.Step < ev.Chosen.Count ? ev.Chosen[ev.Step] : -1;
        var level = r != null && CivText.Studied(r) ? $" · {CivText.LevelName(r.LevelMax)}" : "";
        var gentle = r != null && CivText.N(r.LevelMax) < 5 && _sim.System?.StandingOf(LocalPlayer) != null ? " · AGAINST GENERAL ORDER 1" : "";
        return new ContactPanelView(ev.BodyId, $"FIRST CONTACT · {who} · {BodyTitle(ev.BodyId)}", $"KEEP THE FLAGSHIP IN ORBIT: LEAVING CALLS IT OFF{level.ToUpperInvariant()}{gentle}",
            ev.Step, steps, stepLeft, stepSeconds, Mathf.Max(0f, (ev.EndTick - _sim.Tick) / rate),
            ev.Choices.Select(c => new ContactChoiceView(c.Text, c.Hint, c.ShockShift)).ToList(), chosen, Mathf.Clamp(ev.ShockChance, 0, 100), null, HelmScreen.Peach);
    }

    /// <summary>The talks just ended: say how (from the relations now).</summary>
    private void ContactEnded(ContactEvent last)
    {
        var r = CivOf(last.BodyId);
        var who = r?.Species is { Length: > 0 } && r != null ? CivText.The(r) : "the natives";
        var stage = StageOf(last.BodyId);
        var finished = _sim.Tick >= last.EndTick - 1;
        (string, Color) result = stage switch
        {
            >= ContactStage.TradePartner => ($"A smooth start: {who} welcome us. They are a trade partner now (resources and latinum every minute, a unique tech shared).", HelmScreen.Green),
            ContactStage.Contact => ($"Culture shock: {who} are shaken. Talks go on; trade comes after a wait, at {Mathf.RoundToInt((_data.Civilizations?.Contact?.Unrest.TradeShare.ToFloat() ?? 0.5f) * 100)}% while the unrest lasts.", HelmScreen.Red),
            _ when !finished => ("First contact called off: the flagship left orbit.", HelmScreen.Peach),
            _ => ($"The talks with {who} ended without relations.", HelmScreen.Peach),
        };
        _contactResult = (last.BodyId, result.Item1, result.Item2, _clock + 25);
        _contactBody = null;
    }

    private void OnContactChoice(string bodyId, int choice)
    {
        var ev = _sim.System?.ContactEventOf(LocalPlayer, bodyId);
        var why = ContactOrders.Choose(_sim, LocalPlayer, bodyId, choice, _sim.Tick + InputDelayTicks);
        if (why != null) _hud.PushEvent($"Can't answer: {why}", new Color(1f, 0.7f, 0.42f));
        else if (ev != null && choice < ev.Choices.Count) _hud.PushEvent($"First contact: {ev.Choices[choice].Text}", HelmScreen.Peach);
    }

    private bool ContactBlocksMouse(Vector2 mouse) => _contactPanel.IsOverUi(mouse);

    // ---------------------------------------------------------------- allied native ships

    /// <summary>"Escorting the flagship", "Scouting X", "Home at X".</summary>
    private string AllyTaskText(SystemShip s)
    {
        string Name(string? id) => _system.Body(id) is { } b ? _system.KnownName(b) : "?";
        return s.AllyTask switch
        {
            AllyTask.Escort => "escorting the flagship",
            AllyTask.Scout => $"scouting {Name(s.ScoutBody)}",
            _ => $"home at {Name(s.AllyHome)}",
        };
    }

    /// <summary>The short task tag on the plot ("ESCORT", "SCOUT X", "HOME").</summary>
    public static string AllyTag(SystemShip s, SystemSource source)
    {
        string Name(string? id) => source.Body(id) is { } b ? source.KnownName(b).ToUpperInvariant() : "?";
        return s.AllyTask switch
        {
            AllyTask.Escort => "ESCORT",
            AllyTask.Scout => $"SCOUT {Name(s.ScoutBody)}",
            _ => "HOME",
        };
    }

    /// <summary>An ally's card: whose it is, its task, ESCORT / SCOUT (armed: pick a body).</summary>
    private void AllyCard(SystemShip s, List<HelmRow> rows, List<HelmAction> buttons)
    {
        if (s.AllyOf < 0) return;
        var home = _system.Body(s.AllyHome);
        var civ = home == null ? null : CivOf(home.Id);
        var who = civ?.Species is { Length: > 0 } && civ != null ? CivText.The(civ) : "natives";
        rows.Insert(0, new HelmRow("ALLY", s.AllyOf == LocalPlayer ? $"ours · {who}{(home != null ? $" of {_system.KnownName(home)}" : "")}" : $"allied with {PlayerName(s.AllyOf)}",
            s.AllyOf == LocalPlayer ? HelmScreen.Green : PlanetDriver.ColorOf(s.AllyOf)));
        rows.Insert(1, new HelmRow("TASK", AllyTaskText(s)));
        if (s.AllyOf != LocalPlayer) return;
        rows.Add(new HelmRow("", "Weak ships: they follow the flagship or chart a body, they never land anything.", DetectedGrey));
        if (_allyScout == s.Id) rows.Add(new HelmRow("SCOUT", "click a body on the plot or in the list on the left · Esc cancels", HelmScreen.Cyan));
        buttons.Clear();
        buttons.Add(new HelmAction($"sys:ally:escort:{s.Id}", "", s.AllyTask == AllyTask.Escort ? "ESCORT ✓" : "ESCORT",
            s.AllyTask != AllyTask.Escort && _sim.System?.AllyOrderProblem(LocalPlayer, s.Id, null) == null, Colour: HelmScreen.Green));
        buttons.Add(new HelmAction($"sys:ally:scout:{s.Id}", "", _allyScout == s.Id ? "SCOUT: PICK A BODY" : "SCOUT…", true, _allyScout == s.Id, HelmScreen.Cyan));
    }

    // ---------------------------------------------------------------- native trade freighters

    /// <summary>A trade freighter's card (SystemShip.TradeFor): "Trade freighter · species · carrying …", its route and timing.</summary>
    private void TradeCard(SystemShip s, List<HelmRow> rows)
    {
        if (s.TradeFor < 0) return;
        string Name(string? id) => _system.Body(id) is { } b ? _system.KnownName(b) : "?";
        var civ = CivOf(s.TradeHome);
        var species = civ?.Species is { Length: > 0 } sp ? sp : s.Registry;
        rows.RemoveAll(r => r.Label == "CARGO");
        rows.Insert(0, new HelmRow("TRADE", $"Trade freighter · {species} · carrying {(string.IsNullOrEmpty(s.Cargo) ? "trade goods" : s.Cargo)}",
            s.TradeFor == LocalPlayer ? HelmScreen.Cyan : PlanetDriver.ColorOf(s.TradeFor)));
        var colony = _sim.System?.SurfaceBody;
        var every = _data.Civilizations?.Contact?.Freighter.IntervalMinutes ?? 3;
        rows.Insert(1, new HelmRow("ROUTE", $"{Name(s.TradeHome)} ⇄ {Name(colony)} · every {every} min{(s.TradeFor == LocalPlayer ? "" : $" · for {PlayerName(s.TradeFor)}")}"));
        var left = (int)Mathf.Max(0, (s.TradeArriveTick - _sim.Tick) / Simulation.TickRate);
        var status = SystemSource.InFlight(s)
            ? s.ToBody == colony ? $"inbound with the trade · docks in {Clock((int)_system.SecondsToArrival(s))} (trade is paid then)" : $"flying home to {Name(s.TradeHome)}"
            : s.AtBody == colony ? "unloading at the colony" : $"loading at {Name(s.TradeHome)} · next delivery in {Clock(left)}";
        rows.Insert(2, new HelmRow("RUN", status));
    }

    private string PlayerName(int player) =>
        player >= 0 && player < _sim.Players.Count ? _data.Factions.FirstOrDefault(f => f.Id == _sim.Players[player].FactionId)?.Name ?? $"player {player + 1}" : $"player {player + 1}";

    /// <summary>"sys:ally:escort:<ship>" / "sys:ally:scout:<ship>" (the second arms the body pick). True = handled.</summary>
    private bool OnAllyAction(string id, long when)
    {
        if (!id.StartsWith("sys:ally:")) return false;
        var parts = id.Split(':');
        if (parts.Length < 4 || !int.TryParse(parts[3], out var shipId)) return true;
        if (parts[2] == "escort")
        {
            _allyScout = null;
            var why = ContactOrders.AllyOrder(_sim, LocalPlayer, shipId, null, when);
            _hud.PushEvent(why != null ? $"Can't order the ally: {why}" : $"{_system.Ship(shipId)?.Name ?? "Ally"}: escorting the flagship", why != null ? new Color(1f, 0.7f, 0.42f) : HelmScreen.Green);
        }
        else if (parts[2] == "scout")
        {
            _allyScout = _allyScout == shipId ? null : shipId;
            if (_allyScout != null && _orbit.Display != OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.System);
        }
        return true;
    }

    /// <summary>A body picked while SCOUT is armed (the plot or the rail). True = it was used for the scout order.</summary>
    private bool AllyScoutPick(string bodyId)
    {
        if (_allyScout is not { } shipId) return false;
        _allyScout = null;
        var name = _system.Body(bodyId) is { } b ? _system.KnownName(b) : bodyId;
        var why = ContactOrders.AllyOrder(_sim, LocalPlayer, shipId, bodyId, _sim.Tick + InputDelayTicks);
        _hud.PushEvent(why != null ? $"Can't send the ally to {name}: {why}" : $"{_system.Ship(shipId)?.Name ?? "Ally"}: off to scout {name}", why != null ? new Color(1f, 0.7f, 0.42f) : HelmScreen.Cyan);
        return true;
    }

    /// <summary>The SYSTEM display's title hint while SCOUT is armed, or null.</summary>
    private string? AllyHint()
    {
        if (_allyScout is not { } id) return null;
        if (_system.Ship(id) is not { } s)
        {
            _allyScout = null;
            return null;
        }
        return $"SCOUT: CLICK A BODY FOR {s.Name.ToUpperInvariant()} · ESC CANCELS";
    }
}
