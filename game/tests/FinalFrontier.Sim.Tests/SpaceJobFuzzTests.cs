using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>Random space-job commands (commands 40-46, debug 122) from a seeded RNG: mostly sensible ids taken from the
/// game, mixed with nonsense. Shared by CommandFuzzTests (a planet-only game: all refused) and the system-game fuzz below.</summary>
internal static class SpaceFuzz
{
    private static readonly string[] Actions = { "alongside", "restore_power", "restore_power_kit", "recover_logs", "sweep", "dock", "stop", "mine", "tow", "release", "mine", "tow", "", "fly" };
    private static readonly string[] Repairs = { "patch", "fix:sensors", "fix:engines", "fix:tractor", "fix:lifesupport", "fix:life_support", "dock", "fix:", "nope" };
    private static readonly string[] HoldActions = { "sell", "drop", "jettison", "hire", "steal", "sell:1", "sell:4", "sell:x", "drop", "sell" };
    private static readonly string[] Items =
    {
        "patch_kit", "medical", "salvage:cargo_pod", "salvage:black_box", "salvage:data_core", "salvage:nope", "survivors", "survivors:science",
        "science", "engineering", "probe", "ssat", "post", "mass", "",
        "ore:duranium_ore", "ore:tritanium_ore", "ore:deuterium", "ore:nope", "tow", "tow", "cutting_gear",
    };

    public static Command Next(DeterministicRng rng, Simulation sim)
    {
        int N(int n) => rng.NextInt(n);
        bool Chance(int pct) => N(100) < pct;
        T Pick<T>(IReadOnlyList<T> list) => list[N(list.Count)];
        var p = Chance(85) ? N(System.Math.Max(1, sim.Players.Count)) : rng.NextInt(-3, 5);
        var sys = sim.System;
        var ship = sys?.FlagshipOf(System.Math.Max(0, p));
        var shipId = ship != null && Chance(85) ? ship.Id : rng.NextInt(-2, 20);
        string? Site()
        {
            if (Chance(5)) return null;
            if (sys != null && sys.Sites.Count > 0 && Chance(85)) return Pick(sys.Sites).Id;
            return Pick(new[] { "s1", "s99", "", "x" });
        }
        IReadOnlyList<int> Crew()
        {
            if (Chance(5)) return null!;
            var n = Chance(85) ? 4 : N(7);
            return Enumerable.Range(0, n).Select(_ => Chance(90) ? N(4) : rng.NextInt(-5, 100)).ToList();
        }
        switch (N(10))
        {
            case 0: return new SiteCourseCommand(p, shipId, Site()!);
            case 1: return new SiteScanCommand(p, shipId, Site()!);
            case 2: return new SiteWorkCommand(p, shipId, Site()!, Chance(5) ? null! : Pick(Actions));
            case 3: return new AwayTeamCommand(p, shipId, Site()!, Crew());
            case 4: return new AwayTeamChoiceCommand(p, Site()!, Chance(90) ? N(3) : rng.NextInt(-5, 9));
            case 5: return new ShipRepairCommand(p, shipId, Chance(5) ? null! : Pick(Repairs));
            case 6: return new ShipHoldCommand(p, shipId, Chance(3) ? null! : Pick(HoldActions), Chance(3) ? null! : Pick(Items), Chance(85) ? 1 + N(3) : rng.NextInt(-5, 2000));
            case 7:
            {
                var field = Chance(95) ? (DebugSpaceField)N(Enum.GetValues<DebugSpaceField>().Length) : (DebugSpaceField)rng.NextInt(-3, 20);
                var id = field switch
                {
                    DebugSpaceField.SpawnSite => Pick(new[] { "derelict", "known_derelict", "debris", "station", "asteroid", "nope" }),
                    DebugSpaceField.SystemDown => Pick(new[] { "sensors", "engines", "tractor", "lifesupport", "life_support", "nope" }),
                    DebugSpaceField.Crew or DebugSpaceField.Injure => Pick(new[] { "exploration", "engineering", "science", "defense", "nope" }),
                    DebugSpaceField.HoldItem => Pick(Items),
                    _ => Site(),
                };
                return new DebugSpaceCommand(p, p, field, id, Chance(80) ? rng.NextInt(-3, 101) : rng.NextInt(-1000, 5000));
            }
            case 8: return ship != null && sys != null ? new SetCourseCommand(p, shipId, Pick(sys.Bodies).Id) : new SiteCourseCommand(p, shipId, "s1");
            default: return new LongRangeScanCommand(p, shipId);
        }
    }
}

public class SpaceJobFuzzTests
{
    [Theory]
    [InlineData("tereshkova", 11UL)]
    [InlineData("armstrong", 12UL)]
    [InlineData(null, 13UL)]
    public void RandomSpaceCommandsNeverCrashAndStayDeterministic(string? ship, ulong seed)
    {
        var a = Run(ship, seed);
        var b = Run(ship, seed);
        Assert.Equal(a, b);
    }

    private static List<ulong> Run(string? ship, ulong seed)
    {
        var sim = Game(ship, SeedWithEveryKind(Campaign));
        if (ship != null) Assert.Null(sim.Missions.Enable(Campaign.ShipChoice(ship)!.Chain));
        // Plenty of latinum, so hires get paid for sometimes.
        sim.Players[0].Stock.Add(Campaign.Resource("latinum").Index, Fix64.FromInt(500));
        var rng = new DeterministicRng(seed);
        var hashes = new List<ulong>();
        for (var tick = 0; tick < 9000; tick++)
        {
            if (rng.NextInt(4) == 0) sim.Schedule(sim.Tick, SpaceFuzz.Next(rng, sim));
            sim.Step();
            if (tick % 300 == 0)
            {
                hashes.Add(sim.StateHash());
                Check(sim);
            }
        }
        Check(sim);
        return hashes;
    }

    private static void Check(Simulation sim)
    {
        var sys = sim.System!;
        for (var p = 0; p < sim.Players.Count; p++)
        {
            if (sys.StatusOf(p) is not { } st) continue;
            Assert.True(st.Hull >= Fix64.Zero && st.Hull <= Fix64.FromInt(100), $"hull {st.Hull}");
            Assert.All(st.Crew, c => Assert.True(c.OnDuty >= 0 && c.Injured >= 0 && c.Lost >= 0));
            Assert.All(st.Hold, l => Assert.True(l.Count > 0 && l.Containers >= 0));
            var ship = sys.FlagshipOf(p)!;
            Assert.True((ship.AtBody != null ? 1 : 0) + (ship.AtSite != null ? 1 : 0) + (ship.InFlight ? 1 : 0) == 1, "at a body, at a site or in flight");
            if (ship.AtSite != null) Assert.NotNull(sys.Site(ship.AtSite));
            if (ship.ToSite != null) Assert.NotNull(sys.Site(ship.ToSite));
            if (sys.TowOf(p) is { } tow) // phase 2: a towed site sits where its ship is (as of the last tick)
            {
                Assert.NotNull(sys.Site(tow));
                Assert.Equal(p, sys.TowerOf(tow));
            }
            foreach (var s in sys.Sites) Assert.NotNull(sys.SiteKnowledgeOf(p, s.Id).ToString());
        }
        Assert.Equal(sys.Sites.Count, sys.Sites.Select(s => s.Id).Distinct().Count());
        Assert.All(sim.Players, pl => Assert.All(Enumerable.Range(0, pl.Stock.Count), r => Assert.True(pl.Stock[r] >= Fix64.Zero)));
    }
}
