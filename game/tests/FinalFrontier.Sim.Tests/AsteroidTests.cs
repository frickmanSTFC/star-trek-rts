using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>Space jobs phase 2 (docs/space-jobs-contract.md "Phase 2"): asteroids rolled with the system, mining them into
/// the hold, rock falls, selling ore at the station or to a freighter, a colony ship dropping it into its colony's storage,
/// a hollow asteroid's cavity boarded by an away team.</summary>
public class AsteroidTests
{
    /// <summary>The campaign data with a changed text in maps/sites.json (e.g. no rock falls).</summary>
    private static GameData Sites(params (string From, string To)[] patches)
    {
        var dir = TestData.CopyData();
        try
        {
            foreach (var (from, to) in patches) TestData.Replace(dir, "maps/sites.json", from, to);
            return GameData.Load(dir, new DataSetup(Era: "dawn_of_warp", Factions: new[] { "federation" }));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static readonly Lazy<GameData> NoRockfall = new(() => Sites(("\"chance_per_min\": 15", "\"chance_per_min\": 0")));

    /// <summary>A system game whose system has an asteroid of the variant (the first seed from 1).</summary>
    internal static (Simulation Sim, SpaceSite Rock) WithAsteroid(string variant, string? ship = "tereshkova", GameData? data = null)
    {
        data ??= Campaign;
        for (ulong seed = 1; seed < 400; seed++)
        {
            var g = Game(ship, seed, data: data);
            if (g.System!.Sites.FirstOrDefault(s => s.Kind == SiteKind.Asteroid && s.Variant == variant) is { } rock) return (g, rock);
        }
        throw new InvalidOperationException($"no seed with a {variant} asteroid");
    }

    private static int Hold(Simulation sim, string item) => sim.System!.StatusOf(0)!.Hold.Where(l => l.Item == item).Sum(l => l.Count);

    private static Fix64 Latinum(Simulation sim) => sim.Players[0].Stock[sim.Data.Resource("latinum").Index];

    [Fact]
    public void AsteroidsAreRolledAfterTheOtherSitesMostlyNearTheBelts()
    {
        var rules = Campaign.SpaceSites!.Asteroids!;
        var none = Sites(("\"count\": [3, 6], \"near_belt\"", "\"count\": [0, 0], \"near_belt\""));
        var variants = new HashSet<string>();
        int rocks = 0, nearBelt = 0, beltSystems = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var a = Game(null, seed);
            var sys = a.System!;
            // The other sites are exactly where they were without asteroids (their own salt, rolled first).
            var without = Game(null, seed, data: none).System!.Sites;
            Assert.Equal(without.Select(s => (s.Id, s.Kind, s.Variant, s.Position)), sys.Sites.Where(s => s.Kind != SiteKind.Asteroid).Select(s => (s.Id, s.Kind, s.Variant, s.Position)));
            var asteroids = sys.Sites.Where(s => s.Kind == SiteKind.Asteroid).ToList();
            Assert.InRange(asteroids.Count, 0, rules.CountMax);
            var belts = sys.Bodies.Where(b => Campaign.SystemGenerator!.IsBelt(b.ClassId)).ToList();
            if (belts.Count > 0) beltSystems++;
            foreach (var r in asteroids)
            {
                rocks++;
                variants.Add(r.Variant);
                Assert.All(sys.Bodies, b => Assert.True(FixVec2.Distance(b.Position, r.Position) >= Campaign.SpaceSites.ClearOfBody));
                Assert.InRange(sys.AsteroidContainers(r), 1, 20);
                if (belts.Any(b => Fix64.Abs(r.Position.Length() - b.Position.Length()) <= rules.BeltSpread + Fix64.One)) nearBelt++;
            }
        }
        Assert.True(rocks >= 40 * rules.CountMin / 2, $"{rocks} asteroids over 40 systems");
        Assert.Superset(new HashSet<string> { "common", "metal", "ice", "hollow" }, variants);
        Assert.True(beltSystems > 0 && nearBelt * 2 > rocks, $"{nearBelt} of {rocks} near a belt");
    }

    [Fact]
    public void MiningPutsAContainerOfOreInTheHoldEveryThirtySecondsUntilFullOrMinedOut()
    {
        var (sim, rock) = WithAsteroid("common", data: NoRockfall.Value);
        var sys = sim.System!;
        Identify(sim, rock.Id);
        var ship = Ship(sim);
        var first = sys.AsteroidOre(rock, 0);
        Assert.Contains(sys.SiteReportFor(0, rock.Id)!.Rows, r => r.Label == "ORE");
        Assert.Contains(sys.SiteReportFor(0, rock.Id)!.Actions, a => a.Id == "mine" && a.Problem == null);
        Assert.DoesNotContain(sys.SiteReportFor(0, rock.Id)!.Actions, a => a.Id == "alongside"); // nothing to board on a common rock
        Do(sim, new SiteWorkCommand(0, ship.Id, rock.Id, "mine"));
        Seconds(sim, 29);
        Assert.Equal(0, Hold(sim, StarSystemState.OreItem(first)));
        Assert.Equal("Mining", sys.SiteWorkProgress(0, rock.Id)!.Value.Label);
        Seconds(sim, 1);
        Assert.Equal(30, Hold(sim, StarSystemState.OreItem(first)));
        Assert.Equal(1, sys.SiteWork(0, rock.Id)!.Mined);
        Until(sim, () => sys.SiteWorkProgress(0, rock.Id) == null, 30 * 40, "mining stops");
        var st = sys.StatusOf(0)!;
        Assert.True(st.HoldUsed <= st.HoldSize, $"hold {st.HoldUsed} of {st.HoldSize}");
        var mined = sys.SiteWork(0, rock.Id)!.Mined;
        Assert.Equal(mined * 30, st.Hold.Where(l => l.Item.StartsWith(StarSystemState.OrePrefix)).Sum(l => l.Count));
        if (mined == sys.AsteroidContainers(rock)) Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, rock.Id));
        else Assert.StartsWith("the hold is full", sys.SiteWorkProblem(0, ship.Id, rock.Id, "mine"));
        Assert.Equal(mined * 30, sys.Tally(0, "mined:ore"));
    }

    [Fact]
    public void RockFallsScuffTheHullAndCanKnockOutTheTractor()
    {
        var data = Sites(("\"chance_per_min\": 15, \"hull\": [2, 5], \"tractor\": 20", "\"chance_per_min\": 100, \"hull\": [2, 5], \"tractor\": 100"));
        var (sim, rock) = WithAsteroid("metal", data: data);
        var sys = sim.System!;
        Identify(sim, rock.Id);
        var hull = sys.StatusOf(0)!.Hull;
        Do(sim, new SiteWorkCommand(0, Ship(sim).Id, rock.Id, "mine"));
        Until(sim, () => sys.StatusOf(0)!.Down.Any(d => d.System == ShipSystem.Tractor), 300, "a rock fall takes the tractor out");
        Assert.True(sys.StatusOf(0)!.Hull < hull);
        Assert.Null(sys.SiteWorkProgress(0, rock.Id));
        Assert.Contains("tractor is down", sys.SiteWorkProblem(0, Ship(sim).Id, rock.Id, "mine"));
        Assert.EndsWith("tractor is down: no mining", Refused(sim, new SiteWorkCommand(0, Ship(sim).Id, rock.Id, "mine")));
    }

    [Fact]
    public void OreSellsAtTheStationAndForLessToAFreighterInOrbitWithTheShip()
    {
        var sim = Game();
        var sys = sim.System!;
        var ship = Ship(sim);
        Debug(sim, DebugSpaceField.HoldItem, "ore:duranium_ore", 90);
        Assert.Equal(3, sys.StatusOf(0)!.Hold.First(l => l.Item == "ore:duranium_ore").Containers);
        Assert.NotNull(sys.HoldProblem(0, "sell", "ore:duranium_ore", 30)); // not at a station, maybe no freighter here
        var station = First(sim, SiteKind.Station)!;
        GoTo(sim, station.Id);
        var before = Latinum(sim);
        Assert.Null(sys.HoldProblem(0, "sell", "ore:duranium_ore", 30));
        Do(sim, new ShipHoldCommand(0, ship.Id, "sell", "ore:duranium_ore", 30));
        Assert.Equal(before + Fix64.FromInt(15), Latinum(sim)); // 0.5 a unit
        Assert.Equal(60, Hold(sim, "ore:duranium_ore"));
        Assert.Equal(30, sys.Tally(0, "sold:ore"));
        Assert.EndsWith("only salvage sells", Refused(sim, new ShipHoldCommand(0, ship.Id, "sell", "patch_kit", 1)));
        Assert.EndsWith("have 60", Refused(sim, new ShipHoldCommand(0, ship.Id, "sell", "ore:duranium_ore", 61)));
        Assert.EndsWith("no such buyer", Refused(sim, new ShipHoldCommand(0, ship.Id, "sell:x", "ore:duranium_ore", 1)));

        // A freighter or tanker in orbit of the same body buys at the traders' share.
        SystemShip? buyer = null;
        for (var tries = 0; tries < 40 && buyer == null; tries++)
        {
            buyer = sys.Ships.Where(s => s.IsNeutral && s.AtBody != null && s.Role is "freighter" or "tanker"
                && s.LeaveTick - sim.Tick > sys.TravelTicksFor(ship, sys.ShipPosition(ship, sim.Tick), sys.Body(s.AtBody)!.Position) + 30).FirstOrDefault();
            if (buyer == null) Seconds(sim, 10);
        }
        if (buyer == null) return; // this system's traffic never lingers long enough (traffic is the system's hash)
        Do(sim, new SetCourseCommand(0, ship.Id, buyer.AtBody!));
        Until(sim, () => ship.AtBody == buyer.AtBody, 300, "in orbit with the freighter");
        Assert.Contains(sys.OreBuyers(0), s => s.Id == buyer.Id);
        before = Latinum(sim);
        Do(sim, new ShipHoldCommand(0, ship.Id, $"sell:{buyer.Id}", "ore:duranium_ore", 30));
        Assert.Equal(before + Fix64.FromInt(9), Latinum(sim)); // 15 × 0.6
        Assert.Equal(60, sys.Tally(0, "sold:ore:duranium_ore"));
    }

    [Fact]
    public void AColonyShipDropsOreIntoItsColonysStorage()
    {
        var sim = Game("horizon");
        var sys = sim.System!;
        var map = sim.Surface!.Map;
        Do(sim, new LandColonyCommand(0, Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t))));
        for (var i = 0; i <= TestData.ReadyTicks(Campaign) + Simulation.TickRate; i++) sim.Step();
        var ship = Ship(sim);
        Assert.Equal(sys.SurfaceBody, ship.AtBody);
        Debug(sim, DebugSpaceField.HoldItem, "ore:tritanium_ore", 60);
        var ore = Campaign.Resource("tritanium_ore").Index;
        var stock = sim.Players[0].Stock[ore];
        Do(sim, new ShipHoldCommand(0, ship.Id, "drop", "ore:tritanium_ore", 60));
        Assert.Equal(stock + Fix64.FromInt(60), sim.Players[0].Stock[ore]);
        Assert.Equal(0, Hold(sim, "ore:tritanium_ore"));
        Assert.Equal(60, sys.Tally(0, "drop:ore:tritanium_ore"));
    }

    [Fact]
    public void AHollowAsteroidsCavityIsBoardedAndItStaysUntilMinedOut()
    {
        var (sim, rock) = WithAsteroid("hollow", data: NoRockfall.Value);
        var sys = sim.System!;
        Identify(sim, rock.Id);
        var report = sys.SiteReportFor(0, rock.Id)!;
        Assert.Contains(report.Rows, r => r.Label == "CAVITY");
        Assert.Contains(report.Actions, a => a.Id == "alongside");
        Assert.Contains(report.Actions, a => a.Id == "away_team");
        Work(sim, rock.Id, "alongside", () => sys.SiteWork(0, rock.Id)!.Alongside);
        Board(sim, rock.Id, 1);
        Assert.True(sys.SiteWork(0, rock.Id)!.Boarded);
        Assert.Equal(SiteKnowledge.Identified, sys.SiteKnowledgeOf(0, rock.Id)); // ore still in it
        Work(sim, rock.Id, "mine", () => sys.SiteWork(0, rock.Id)!.MinedOut || sys.SiteWorkProgress(0, rock.Id) == null);
        Until(sim, () => sys.SiteWorkProgress(0, rock.Id) == null, 600, "mining done");
        if (sys.SiteWork(0, rock.Id)!.MinedOut) Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, rock.Id));
    }

    [Fact]
    public void RefusedOreAndMiningCommandsChangeNothing()
    {
        var (a, rock) = WithAsteroid("common");
        var (b, _) = WithAsteroid("common");
        var bad = new Command[]
        {
            new SiteWorkCommand(0, Ship(b).Id, rock.Id, "mine"), // not there, not identified
            new ShipHoldCommand(0, Ship(b).Id, "sell", "ore:duranium_ore", 1),
            new ShipHoldCommand(0, Ship(b).Id, "drop", "ore:nope", 1),
            new ShipHoldCommand(0, Ship(b).Id, "sell:99", "ore:duranium_ore", 1),
            new ShipHoldCommand(0, Ship(b).Id, "drop", "tow", 1),
            new SiteWorkCommand(0, Ship(b).Id, rock.Id, "tow"),
            new SiteWorkCommand(0, Ship(b).Id, rock.Id, "release"),
        };
        foreach (var c in bad) b.Schedule(b.Tick, c);
        a.Step();
        b.Step();
        Assert.Equal(bad.Length, b.RefusedCommands);
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
