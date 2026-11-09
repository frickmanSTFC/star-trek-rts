using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Tests;

/// <summary>Civilizations phase 2 follow-ups (owner 2026-11-09; docs/civilizations-phase2-contract.md "Follow-ups";
/// data/civilizations/contact.json unrest / freighter / spacefaring): culture shock halves the world's trade for a while
/// and keeps its natives unsettled, a native trade freighter flies each trade route, and L6 (spacefaring) worlds are
/// stronger partners that turn away a captain with a low Starfleet Standing.</summary>
public class ContactFollowUpTests
{
    private static GameData Real => TestData.Real;

    private const string Contact = "civilizations/contact.json";

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static void Ok(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, sim.LastRefusal);
    }

    private static void Civ(Simulation sim, string body, DebugCivField field, int value = 0) =>
        Ok(sim, new DebugCivCommand(0, 0, body, field, Fix64.FromInt(value)));

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

    /// <summary>A system game with the flagship in orbit of a living civilization of that level, Charted and studied.</summary>
    private static (Simulation Sim, string Body) Ready(GameData data, int level = 5, int standing = 80)
    {
        var (seed, site) = FindSeed(data);
        var sim = MatchSetup.CreateSystemGame(data, seed);
        Civ(sim, site.BodyId, DebugCivField.Level, level);
        Civ(sim, site.BodyId, DebugCivField.Temperament, (int)Temperament.Peaceful);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        Ok(sim, new SetCourseCommand(0, ship.Id, site.BodyId));
        for (var i = 0; i < 4000 && ship.AtBody != site.BodyId; i++) sim.Step();
        Assert.Equal(site.BodyId, ship.AtBody);
        Civ(sim, site.BodyId, DebugCivField.FinishStudy);
        Civ(sim, site.BodyId, DebugCivField.Standing, standing);
        return (sim, site.BodyId);
    }

    private static GameData Odds(int shock, params (string File, string From, string To)[] more) =>
        TestData.Patched(new[] { (Contact, "\"shock_min\": 5,", $"\"shock_min\": {shock},"), (Contact, "\"shock_max\": 95,", $"\"shock_max\": {shock},") }
            .Concat(more).ToArray());

    private static void Through(Simulation sim, string body)
    {
        Ok(sim, new FirstContactCommand(0, body));
        var steps = sim.Data.Civilizations!.Contact!.Steps.Count;
        for (var k = 0; k < steps; k++) Ok(sim, new ContactChoiceCommand(0, body, 0));
        Assert.Null(sim.System!.ContactEventOf(0, body));
    }

    private static double TradeTotal(StarSystemState sys) => sys.TradeIncomePerMinute(0).Sum(x => x.Amount.ToDouble());

    private static double BaseTrade(GameData data) => data.Civilizations!.Contact!.TradeIncome.Sum(x => x.Amount.ToDouble());

    // ---------------------------------------------------------------- data

    [Fact]
    public void TheFollowUpRulesAreInTheDataAndChecked()
    {
        var c = Real.Civilizations!.Contact!;
        Assert.Empty(Real.Validate());
        Assert.Equal(Fix64.Ratio(1, 2), c.Unrest.TradeShare);
        Assert.Equal(10, c.Unrest.Minutes);
        Assert.InRange(c.Freighter.IntervalMinutes, 2, 5); // "every few minutes"
        Assert.Equal(6, c.Spacefaring.FromLevel);
        Assert.True(c.Spacefaring.TradeMultiplier > Fix64.One);
        Assert.Equal(2, c.Spacefaring.AllyShips);
        Assert.True(c.Spacefaring.PartnerMinutes < c.Membership.PartnerMinutes);
        Assert.True(c.Spacefaring.CandidateMinutes < c.Membership.CandidateMinutes);
        Assert.Contains(TestData.Patched((Contact, "\"trade_share\": 0.5,", "\"trade_share\": 2,")).Validate(), p => p.Contains("unrest.trade_share"));
        Assert.Contains(TestData.Patched((Contact, "\"interval_minutes\": 3,", "\"interval_minutes\": 0,")).Validate(), p => p.Contains("freighter.interval_minutes"));
        Assert.Contains(TestData.Patched((Contact, "\"id\": \"freighter\"", "\"id\": \"cutter\"")).Validate(), p => p.Contains("ally class"));
        Assert.Contains(TestData.Patched((Contact, "\"ally_ships\": 2,", "\"ally_ships\": 9,")).Validate(), p => p.Contains("spacefaring.ally_ships"));
        Assert.Contains(TestData.Patched((Contact, "\"low_standing_refuse_chance\": 0.3", "\"low_standing_refuse_chance\": 3")).Validate(),
            p => p.Contains("low_standing_refuse_chance"));
    }

    // ---------------------------------------------------------------- 1: culture shock has teeth

    [Fact]
    public void CultureShockHalvesTradeOnceItStartsAndKeepsTheNativesUnsettled()
    {
        var data = Odds(100);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        var c = data.Civilizations!.Contact!;
        Through(sim, body);
        Assert.Equal(ContactStage.Contact, sys.RelationsWith(0, body));
        Assert.True(sys.Unsettled(body));
        Assert.Equal(0, sys.UnrestTradeSecondsLeft(0, body)); // not trading yet

        // Trade starts after the shock wait: at half, for unrest.minutes, and the unrest runs on as long.
        Run(sim, c.ShockWaitMinutes * Simulation.TicksPerMinute + 2);
        Assert.Equal(ContactStage.TradePartner, sys.RelationsWith(0, body));
        Assert.InRange(sys.UnrestTradeSecondsLeft(0, body), c.Unrest.Minutes * 60 - 1, c.Unrest.Minutes * 60);
        Assert.Equal(BaseTrade(data) * c.Unrest.TradeShare.ToDouble(), TradeTotal(sys), 3);
        Assert.True(sys.CultureShockSecondsLeft(body) >= sys.UnrestTradeSecondsLeft(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "trade_partner" && x.Text.Contains("unsettled"));

        // Their awareness can't fall while the unrest lasts (the test panel lowers it; the next tick puts it back).
        Civ(sim, body, DebugCivField.Awareness, 5);
        Run(sim, 1);
        Assert.True(sys.AwarenessOf(body) >= c.Unrest.AwarenessFloor);

        // The paid trade really is halved (latinum always fits).
        var latinum = data.Resource("latinum").Index;
        var stock = sim.Players[0].Stock;
        Run(sim, Simulation.TicksPerMinute - (int)(sim.Tick % Simulation.TicksPerMinute));
        var l0 = stock[latinum];
        Run(sim, Simulation.TicksPerMinute);
        var full = c.TradeIncome.Single(x => x.Resource == latinum).Amount;
        Assert.Equal(l0 + full * c.Unrest.TradeShare, stock[latinum]);

        // Ten minutes later: full trade, settled, and awareness may be lowered again.
        Run(sim, c.Unrest.Minutes * Simulation.TicksPerMinute);
        Assert.Equal(0, sys.UnrestTradeSecondsLeft(0, body));
        Assert.False(sys.Unsettled(body));
        Assert.Equal(BaseTrade(data), TradeTotal(sys), 3);
        Assert.Contains(sys.CivEvents, x => x.Kind == "unrest_over");
        Civ(sim, body, DebugCivField.Awareness, 5);
        Run(sim, 1);
        Assert.True(sys.AwarenessOf(body) < c.Unrest.AwarenessFloor);
    }

    [Fact]
    public void ASmoothStartTradesInFullWithNoUnrest()
    {
        var data = Odds(0);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        Through(sim, body);
        Assert.Equal(ContactStage.TradePartner, sys.RelationsWith(0, body));
        Assert.Equal(0, sys.UnrestTradeSecondsLeft(0, body));
        Assert.False(sys.Unsettled(body));
        Assert.Equal(BaseTrade(data), TradeTotal(sys), 3);
    }

    // ---------------------------------------------------------------- 2: trade freighters

    [Fact]
    public void ATradeFreighterFliesEachRouteAndReachesTheColonyWhenTradeIsPaid()
    {
        var data = Odds(0);
        var (sim, body) = Ready(data);
        var sys = sim.System!;
        var f = data.Civilizations!.Contact!.Freighter;
        Assert.Empty(sys.TradeFreightersOf(0));
        Through(sim, body);
        var ship = sys.TradeFreightersOf(0).Single();
        Assert.Contains(sys.CivEvents, x => x.Kind == "trade_route");
        Assert.True(ship.IsTradeFreighter);
        Assert.False(ship.IsNeutral);
        Assert.False(ship.IsAlly);
        Assert.Equal(-1, ship.Owner);
        Assert.Equal(0, ship.TradeFor);
        Assert.Equal(body, ship.TradeHome);
        Assert.Equal(body, ship.AtBody);
        Assert.Equal("native." + f.ClassId, ship.ClassId);
        Assert.Equal(sys.CivAt(body)!.Species, ship.Registry);
        Assert.Contains("latinum", ship.Cargo);
        Assert.Contains("duranium", ship.Cargo);
        Assert.DoesNotContain(ship, sys.AlliesOf(0));
        Assert.Single(sys.AlliesOf(0)); // the ally is still there and separate

        // Two trips: each arrives at the colony body on a whole minute, waits, flies home; the next a few minutes on.
        var colony = sys.SurfaceBody!;
        var arrivals = new List<long>();
        for (var i = 0; i < 20 * Simulation.TicksPerMinute && arrivals.Count < 2; i++)
        {
            var before = ship.AtBody;
            if (ship.InFlight && ship.ToBody == colony) Assert.Equal(0, ship.ArriveTick % Simulation.TicksPerMinute);
            sim.Step();
            if (before != colony && ship.AtBody == colony) arrivals.Add(ship.ArriveTick);
        }
        Assert.Equal(2, arrivals.Count);
        Assert.True(arrivals[1] - arrivals[0] >= (long)f.IntervalMinutes * Simulation.TicksPerMinute);
        Run(sim, f.DwellSeconds * Simulation.TickRate + 2);
        Assert.True(ship.InFlight);
        Assert.Equal(body, ship.ToBody);
        Assert.Single(sys.TradeFreightersOf(0)); // never a second one on the same route

        // The route closes (test panel: back to Contact): the freighter is retired.
        Civ(sim, body, DebugCivField.Relations, (int)ContactStage.Contact);
        Run(sim, 1);
        Assert.Empty(sys.TradeFreightersOf(0));
        Assert.DoesNotContain(sys.Ships, s => s.TradeFor >= 0);
    }

    [Fact]
    public void FreightersAreDeterministicAndInTheHash()
    {
        Simulation Game()
        {
            var (sim, body) = Ready(Odds(0));
            Through(sim, body);
            Run(sim, 4 * Simulation.TicksPerMinute);
            return sim;
        }
        var a = Game();
        var b = Game();
        Assert.Equal(a.StateHash(), b.StateHash());
        var fa = a.System!.TradeFreightersOf(0).Single();
        var fb = b.System!.TradeFreightersOf(0).Single();
        Assert.Equal((fa.Name, fa.AtBody, fa.ToBody, fa.ArriveTick, fa.TradeArriveTick), (fb.Name, fb.AtBody, fb.ToBody, fb.ArriveTick, fb.TradeArriveTick));
        // Moving the freighter's schedule (and nothing else) moves the hash.
        var h = a.StateHash();
        typeof(SystemShip).GetProperty(nameof(SystemShip.TradeArriveTick))!.SetValue(fa, fa.TradeArriveTick + Simulation.TicksPerMinute);
        Assert.NotEqual(h, a.StateHash());
    }

    // ---------------------------------------------------------------- 4: L6 = a stronger partner

    [Fact]
    public void ASpacefaringWorldTradesMoreSendsTwoShipsAndJoinsFaster()
    {
        var data = Odds(0);
        var (sim, body) = Ready(data, 6);
        var sys = sim.System!;
        var c = data.Civilizations!.Contact!;
        Assert.True(sys.IsSpacefaring(body));
        Assert.Null(sys.ContactRisk(0, body)); // Standing 80: nothing extra to fear
        Through(sim, body);
        Assert.Equal(ContactStage.TradePartner, sys.RelationsWith(0, body));
        Assert.Equal(c.Spacefaring.AllyShips, sys.AlliesOf(0).Count);
        Assert.Equal(BaseTrade(data) * c.Spacefaring.TradeMultiplier.ToDouble(), TradeTotal(sys), 3);
        // Membership: spacefaring partner/candidate minutes instead of the usual ones.
        Assert.Contains("trade partners for", sys.MembershipProblem(0, body));
        Run(sim, c.Spacefaring.PartnerMinutes * Simulation.TicksPerMinute);
        Assert.Null(sys.MembershipProblem(0, body));
        Ok(sim, new MembershipCommand(0, body));
        Assert.Equal(ContactStage.Candidate, sys.RelationsWith(0, body));
        Run(sim, c.Spacefaring.CandidateMinutes * Simulation.TicksPerMinute);
        Assert.Null(sys.MembershipProblem(0, body));
        Ok(sim, new MembershipCommand(0, body));
        Assert.Equal(ContactStage.Allied, sys.RelationsWith(0, body));
        Assert.Equal(System.Math.Max(c.Ally.ShipsAllied, c.Spacefaring.AllyShips), sys.AlliesOf(0).Count);
        Assert.Equal(BaseTrade(data) * (c.AlliedMultiplier * c.Spacefaring.TradeMultiplier).ToDouble(), TradeTotal(sys), 3);

        // An L5 world keeps the usual numbers.
        var (l5, b5) = Ready(data, 5);
        Through(l5, b5);
        Assert.False(l5.System!.IsSpacefaring(b5));
        Assert.Single(l5.System.AlliesOf(0));
        Assert.Equal(BaseTrade(data), l5.System.TradeIncomePerMinute(0).Sum(x => x.Amount.ToDouble()), 3);
    }

    [Fact]
    public void ASpacefaringWorldJudgesALowStanding()
    {
        // More culture shock below spacefaring.low_standing (refusal off so the event runs).
        var noRefusal = TestData.Patched((Contact, "\"low_standing_refuse_chance\": 0.3", "\"low_standing_refuse_chance\": 0"));
        var c = noRefusal.Civilizations!.Contact!;
        var (good, gb) = Ready(noRefusal, 6, 80);
        var (low, lb) = Ready(noRefusal, 6, c.Spacefaring.LowStanding - 10);
        Assert.Null(good.System!.ContactRisk(0, gb));
        Assert.NotNull(low.System!.ContactRisk(0, lb));
        Ok(good, new FirstContactCommand(0, gb));
        Ok(low, new FirstContactCommand(0, lb));
        var a = good.System.ContactEventOf(0, gb)!.ShockChance;
        var b = low.System.ContactEventOf(0, lb)!.ShockChance;
        Assert.Equal(System.Math.Min(c.ShockMax, a + c.Spacefaring.LowStandingShock), b);

        // Turned away at the start (chance 1): no event, a refusal, an event line; the command itself was accepted.
        var always = TestData.Patched((Contact, "\"low_standing_refuse_chance\": 0.3", "\"low_standing_refuse_chance\": 1"));
        var (sim, body) = Ready(always, 6, c.Spacefaring.LowStanding - 10);
        var sys = sim.System!;
        Ok(sim, new FirstContactCommand(0, body));
        Assert.Null(sys.ContactEventOf(0, body));
        Assert.Equal(ContactStage.None, sys.RelationsWith(0, body));
        Assert.Contains(sys.CivEvents, x => x.Kind == "contact_refused");
        Assert.InRange(sys.RefusesContactSeconds(0, body), 1, always.Civilizations!.Contact!.RefuseAfterIncidentMinutes * 60);
        Assert.Contains("refuse contact", sys.FirstContactProblem(0, body));

        // A good Standing, or an L5 world, is never turned away.
        var (ok, ob) = Ready(always, 6, 80);
        Ok(ok, new FirstContactCommand(0, ob));
        Assert.NotNull(ok.System!.ContactEventOf(0, ob));
        var (l5, b5) = Ready(always, 5, c.Spacefaring.LowStanding - 10);
        Assert.Null(l5.System!.ContactRisk(0, b5));
        Ok(l5, new FirstContactCommand(0, b5));
        Assert.NotNull(l5.System.ContactEventOf(0, b5));
    }

    // ---------------------------------------------------------------- 3: shared techs (the sim side of the lab list)

    [Fact]
    public void ASharedTechNamesTheCivilizationThatSharedIt()
    {
        var (sim, body) = Ready(Odds(0));
        var sys = sim.System!;
        Through(sim, body);
        var tech = sim.Surface!.Players[0].SharedTechs.Single();
        Assert.Equal(body, sys.SharedTechSite(0, tech.Id)!.BodyId);
        Assert.Null(sys.SharedTechSite(0, "no_such_tech"));
        Assert.Null(sys.SharedTechSite(1, tech.Id));
    }
}
