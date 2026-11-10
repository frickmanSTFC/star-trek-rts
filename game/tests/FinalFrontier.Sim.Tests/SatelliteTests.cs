using System.Reflection;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Survey satellites, refined (docs/satellite-contract.md): call signs, the deploy phase, passes, band
/// coverage, the findings log, retasking, and all of it in the state hash.</summary>
public class SatelliteTests
{
    private static GameData Data => TestData.Real;
    private static OrbitRules Orbit => Data.Orbit!;
    private static int Sec(int s) => s * Simulation.TickRate;

    /// <summary>The real data with a log long enough to keep a whole first pass.</summary>
    private static readonly Lazy<GameData> LongLog = new(() => TestData.Patched(("surfaces.json", "\"log\": 40", "\"log\": 500")));

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    /// <summary>A fresh game in orbit (nothing surveyed yet) with one satellite launched over the middle row.</summary>
    private static (Simulation Sim, Satellite Sat) Launched(GameData? data = null, ulong seed = 1701, int? row = null)
    {
        var sim = MatchSetup.CreatePlanetLanding(data ?? Data, seed);
        sim.AddPlayer("cardassian"); // a rival (whose satellites would have their own call signs)
        var g = sim.Surface!.Map.Grid;
        Do(sim, new SatelliteCommand(0, row ?? g.Height / 2));
        return (sim, sim.Surface.Satellites.Single());
    }

    [Fact]
    public void ItDeploysBeforeItSweeps()
    {
        var (sim, sat) = Launched();
        var s = sim.Surface!;
        Assert.Equal(sat.LaunchTick + Sec(Orbit.SatelliteDeploySeconds), sat.ReadyTick);
        Assert.True(Orbit.SatelliteDeploySeconds > 0);
        Run(sim, (int)(sat.ReadyTick - sim.Tick)); // up to the tick it is ready (its step not run yet)
        Assert.True(sat.Deploying(sim.Tick - 1));
        Assert.Equal(0, sat.Swept);
        Assert.Equal(0, sat.TilesSurveyed);
        Assert.DoesNotContain(true, s.Players[0].Surveyed);
        Run(sim, Sec(2));
        Assert.False(sat.Deploying(sim.Tick));
        Assert.True(sat.Swept > 0);
        Assert.True(sat.TilesSurveyed > 0);
    }

    [Fact]
    public void CallSignsAreDeterministicAndCountPerOwner()
    {
        var (sim, sat) = Launched();
        var s = sim.Surface!;
        Assert.StartsWith("SAT-1 · ", sat.CallSign);
        Assert.Contains(sat.CallSign["SAT-1 · ".Length..], Orbit.SatelliteCallSigns);
        Assert.Equal(1, sat.Ordinal);
        Do(sim, new SatelliteCommand(0, 10));
        var second = s.Satellites[1];
        Assert.StartsWith("SAT-2 · ", second.CallSign);
        Assert.NotEqual(sat.CallSign[8..], second.CallSign[8..]);
        Assert.Equal(sat.CallSign, Launched().Sat.CallSign); // same seed, same name
        Assert.Equal(s.SatelliteCallSign(0, 1), sat.CallSign);
        // Past the end of the list the names come round again with a number.
        Assert.EndsWith(" II", s.SatelliteCallSign(0, Orbit.SatelliteCallSigns.Count + 1));
        // Different seeds start the list in different places (the 24 names make a clash on all three unlikely).
        Assert.Contains(new ulong[] { 1, 99999, 4242 }, seed => Launched(seed: seed).Sat.CallSign != sat.CallSign);
    }

    [Fact]
    public void AFullPassSurveysTheBandAndLogsWhatItFound()
    {
        var (sim, sat) = Launched(LongLog.Value);
        var s = sim.Surface!;
        var map = s.Map;
        var g = map.Grid;
        Run(sim, (int)(sat.ReadyTick - sim.Tick) + Sec(Orbit.SatelliteSweepSeconds) + 2);
        Assert.Equal(1, sat.Passes);
        Assert.True(sat.Swept >= g.Width);
        Assert.Equal(Fix64.One, s.SatelliteCoverage(sat));
        var (first, last) = s.SatelliteBand(sat);
        Assert.Equal(Orbit.SatelliteBandRows, last - first + 1);
        Assert.Equal((last - first + 1) * g.Width, sat.TilesSurveyed); // nothing was surveyed before it

        var band = Enumerable.Range(0, g.Count).Where(t => g.Row(t) >= first && g.Row(t) <= last).ToList();
        Assert.All(sat.Findings, f => Assert.Contains(f.Tile, band));
        Assert.Equal(sat.FindingsTotal, sat.Findings.Count); // the long log kept everything

        // Every deposit in the band is logged once, by its resource.
        var deposits = band.Where(t => map.DepositResource[t] >= 0).ToList();
        Assert.NotEmpty(deposits);
        var logged = sat.Findings.Where(f => f.Kind == SatelliteFindingKind.Deposit).ToList();
        Assert.Equal(deposits.OrderBy(t => t), logged.Select(f => f.Tile).OrderBy(t => t));
        Assert.All(logged, f => Assert.Equal(map.DepositResource[f.Tile], f.Detail));

        // Patches are one line each, not one per tile; what is logged is really there.
        Assert.All(sat.Findings.Where(f => f.Kind == SatelliteFindingKind.Life), f => Assert.Equal((int)map.WildlifeAt[f.Tile], f.Detail));
        Assert.All(sat.Findings.Where(f => f.Kind == SatelliteFindingKind.Toxic), f => Assert.True(map.Toxic[f.Tile]));
        Assert.All(sat.Findings.Where(f => f.Kind == SatelliteFindingKind.Terrain), f => Assert.False(map.Terrain(f.Tile).Passable));
        var toxicTiles = band.Count(t => map.Toxic[t]);
        Assert.True(sat.Findings.Count(f => f.Kind == SatelliteFindingKind.Toxic) <= toxicTiles);
        if (toxicTiles > 1) Assert.True(sat.Findings.Count(f => f.Kind == SatelliteFindingKind.Toxic) < toxicTiles);
        Assert.Equal(band.Contains(map.NativeTile) ? 1 : 0, sat.Findings.Count(f => f.Kind == SatelliteFindingKind.Natives));
        Assert.Equal(band.Count(t => s.RuinAt(t) != null), sat.Findings.Count(f => f.Kind == SatelliteFindingKind.Ruins));
        Assert.Contains(sat.Findings, f => f.Kind is SatelliteFindingKind.Life or SatelliteFindingKind.Terrain or SatelliteFindingKind.Toxic);

        // First flags: exactly the first of each kind + detail.
        var seen = new HashSet<(SatelliteFindingKind, int)>();
        foreach (var f in sat.Findings) Assert.Equal(seen.Add((f.Kind, f.Detail)), f.First);
        // Oldest first, in tick order.
        Assert.True(sat.Findings.Zip(sat.Findings.Skip(1)).All(p => p.First.Tick <= p.Second.Tick));

        // A second lap finds nothing new on the ground (weather may still come and go).
        var before = sat.Findings.Count(f => f.Kind != SatelliteFindingKind.Weather);
        Run(sim, Sec(Orbit.SatelliteSweepSeconds));
        Assert.Equal(2, sat.Passes);
        Assert.Equal(before, sat.Findings.Count(f => f.Kind != SatelliteFindingKind.Weather));
    }

    [Fact]
    public void TheLogIsBoundedAndKeepsTheNewest()
    {
        var (sim, sat) = Launched();
        Run(sim, (int)(sat.ReadyTick - sim.Tick) + Sec(Orbit.SatelliteSweepSeconds) + 2);
        Assert.True(sat.FindingsTotal > Orbit.SatelliteLog, $"seed 1701's middle band should find more than {Orbit.SatelliteLog} things (found {sat.FindingsTotal})");
        Assert.Equal(Orbit.SatelliteLog, sat.Findings.Count);
        // The kept ones are the newest: the same as the tail of a long log.
        var (longSim, longSat) = Launched(LongLog.Value);
        Run(longSim, (int)(longSat.ReadyTick - longSim.Tick) + Sec(Orbit.SatelliteSweepSeconds) + 2);
        Assert.Equal(longSat.Findings.TakeLast(Orbit.SatelliteLog), sat.Findings);
    }

    [Fact]
    public void ASecondSatelliteOverlappingTheBandLogsOnlyWhatWasNew()
    {
        var (sim, sat) = Launched(LongLog.Value);
        var s = sim.Surface!;
        Run(sim, (int)(sat.ReadyTick - sim.Tick) + Sec(Orbit.SatelliteSweepSeconds) + 2);
        Do(sim, new SatelliteCommand(0, sat.Row + 2)); // overlaps all but two rows
        var second = s.Satellites[1];
        Run(sim, (int)(second.ReadyTick - sim.Tick) + Sec(Orbit.SatelliteSweepSeconds) + 2);
        Assert.Equal(2 * s.Map.Grid.Width, second.TilesSurveyed);
        var (first, last) = s.SatelliteBand(sat);
        Assert.All(second.Findings.Where(f => f.Kind != SatelliteFindingKind.Weather), f => Assert.True(s.Map.Grid.Row(f.Tile) > last));
        Assert.True(first < last);
    }

    [Fact]
    public void RetaskMovesTheBandAndCarriesOnFromItsColumn()
    {
        var (sim, sat) = Launched();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var row = sat.Row;
        Assert.Equal("still deploying", s.RetaskProblem(sim, 0, sat.Id, 5));
        Do(sim, new RetaskSatelliteCommand(0, sat.Id, 5)); // refused: nothing changes
        Assert.Equal(row, sat.Row);
        Assert.Equal(1, sim.RefusedCommands);
        Assert.NotEqual(s.SatelliteCallSign(0, 1), s.SatelliteCallSign(1, 1)); // the rival's list starts elsewhere

        Run(sim, (int)(sat.ReadyTick - sim.Tick) + Sec(20));
        Assert.Equal("not your satellite", s.RetaskProblem(sim, 1, sat.Id, 5));
        Assert.Equal("no such satellite", s.RetaskProblem(sim, 0, 999, 5));
        Assert.Equal("off the map", s.RetaskProblem(sim, 0, sat.Id, g.Height));
        Assert.Equal("already over that band", s.RetaskProblem(sim, 0, sat.Id, row));
        Assert.Null(s.RetaskProblem(sim, 0, sat.Id, 5));

        var swept = sat.Swept;
        Do(sim, new RetaskSatelliteCommand(0, sat.Id, 5));
        Assert.Equal(5, sat.Row);
        Assert.Equal(1, sat.Retasks);
        Assert.True(sat.Deploying(sim.Tick));
        Assert.Equal("still manoeuvring to its band", s.RetaskProblem(sim, 0, sat.Id, 20));
        var manoeuvre = (int)(sat.ReadyTick - sim.Tick);
        Assert.InRange(manoeuvre, 1, Sec(Orbit.SatelliteRetaskSeconds));
        Run(sim, manoeuvre);
        Assert.InRange(sat.Swept, swept, swept + 1); // no sweeping while it moved (the column it was on may finish)
        Run(sim, Sec(10));
        Assert.True(sat.Swept > swept + 1);
        // The new band is being surveyed from the column it had reached, eastward.
        Assert.True(s.Players[0].Surveyed[g.Index(swept % g.Width + 2, 5)]);
        Assert.False(s.Players[0].Surveyed[g.Index((swept + g.Width / 2) % g.Width, 3)]);
    }

    [Fact]
    public void SatellitesAreDeterministic()
    {
        (ulong Hash, List<SatelliteFinding> Log, string Sign) Play()
        {
            var (sim, sat) = Launched();
            Run(sim, Sec(30));
            sim.Schedule(sim.Tick, new SatelliteCommand(0, 8));
            sim.Schedule(sim.Tick + 1, new RetaskSatelliteCommand(0, sat.Id, 40));
            Run(sim, Sec(70));
            return (sim.StateHash(), sim.Surface!.Satellites.SelectMany(x => x.Findings).ToList(), string.Join("/", sim.Surface.Satellites.Select(x => x.CallSign)));
        }
        var a = Play();
        var b = Play();
        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(a.Log, b.Log);
        Assert.Equal(a.Sign, b.Sign);
        Assert.NotEmpty(a.Log);
    }

    [Fact]
    public void EverySatelliteFieldIsInTheHash()
    {
        var (sim, sat) = Launched();
        Run(sim, (int)(sat.ReadyTick - sim.Tick) + Sec(10));
        Assert.NotEmpty(sat.Findings);
        var checkedNames = new List<string>();
        foreach (var p in typeof(Satellite).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod != null))
        {
            var before = sim.StateHash();
            var value = p.GetValue(sat)!;
            object changed = value switch
            {
                int i => i + 1,
                long l => l + 1,
                WeatherKind w => w == WeatherKind.Storm ? WeatherKind.Fog : WeatherKind.Storm,
                _ => throw new InvalidOperationException($"Satellite.{p.Name}: add its type here"),
            };
            p.SetValue(sat, changed);
            Assert.True(before != sim.StateHash(), $"Satellite.{p.Name} is not in the state hash");
            p.SetValue(sat, value);
            Assert.Equal(before, sim.StateHash());
            checkedNames.Add(p.Name);
        }
        Assert.Equal(new[] { "FindingsTotal", "Passes", "ReadyTick", "Retasks", "Row", "SweepBase", "Swept", "TilesSurveyed", "WeatherLast" },
            checkedNames.OrderBy(n => n, StringComparer.Ordinal));

        // The log itself (each field of a line) and the owner's firsts.
        var log = (List<SatelliteFinding>)typeof(Satellite).GetField("_findings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sat)!;
        var line = log[^1];
        foreach (var edited in new[] { line with { Tick = line.Tick + 1 }, line with { Kind = line.Kind + 1 }, line with { Tile = line.Tile + 1 },
                     line with { Detail = line.Detail + 1 }, line with { First = !line.First } })
        {
            var before = sim.StateHash();
            log[^1] = edited;
            Assert.True(before != sim.StateHash(), $"finding {edited} is not in the state hash");
            log[^1] = line;
            Assert.Equal(before, sim.StateHash());
        }
        var firsts = (SortedSet<long>)typeof(PlanetSurface).GetField("_satelliteFirsts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sim.Surface)!;
        Assert.NotEmpty(firsts);
        var hash = sim.StateHash();
        firsts.Add(long.MaxValue);
        Assert.NotEqual(hash, sim.StateHash());
        firsts.Remove(long.MaxValue);
        Assert.Equal(hash, sim.StateHash());
    }

    [Fact]
    public void BadRetasksAreRefusedAndChangeNothing()
    {
        var (sim, sat) = Launched();
        Run(sim, (int)(sat.ReadyTick - sim.Tick) + 1);
        var hash = sim.StateHash();
        foreach (var c in new Command[] { new RetaskSatelliteCommand(0, -5, 3), new RetaskSatelliteCommand(0, sat.Id, -1), new RetaskSatelliteCommand(0, sat.Id, int.MaxValue),
                     new RetaskSatelliteCommand(7, sat.Id, 3) })
            sim.Schedule(sim.Tick, c);
        var refused = sim.RefusedCommands;
        sim.Step();
        Assert.Equal(refused + 4, sim.RefusedCommands);
        Assert.Equal(sat.Row, sim.Surface!.Map.Grid.Height / 2);
        Assert.Equal(0, sat.Retasks);
        Assert.NotEqual(hash, sim.StateHash()); // the tick moved on; the satellite kept sweeping its band
    }
}
