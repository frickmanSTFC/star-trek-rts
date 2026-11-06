using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Vehicles drive round buildings inside the hexes, park at access points and keep their distance (Lanes.cs).</summary>
public class LaneTests
{
    private static int Sec(int seconds) => seconds * Simulation.TickRate;

    /// <summary>The real data with no luck involved (no hazards, flats or predator attacks), so rovers keep driving.</summary>
    private static GameData Calm() => TestData.Regexed("surfaces.json",
        ("\"hazard\": [0-9.]+", "\"hazard\": 0"), ("\"flat\": [0-9.]+", "\"flat\": 0"), ("\"chance_per_min\": 0\\.4,", "\"chance_per_min\": 0,"));

    private static readonly Lazy<GameData> CalmData = new(Calm);

    private static Simulation Landed(out int hub)
    {
        var data = CalmData.Value;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        hub = site;
        Do(sim, new DebugSurveyAllCommand(0, 0));
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        if (c is DebugCommand debug) Assert.Null(sim.DebugProblem(debug));
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>Plain, buildable tiles in the hub's build range with no building, for a small base.</summary>
    private static List<int> BaseTiles(Simulation sim, int hub) =>
        sim.Surface!.Map.Grid.Within(hub, 3)
            .Where(t => t != hub && sim.Surface.Map.Terrain(t).Buildable && sim.Surface.StructureAt(t) == null && sim.Surface.Units.All(u => u.Tile != t))
            .ToList();

    private static SurfaceUnit Spawn(Simulation sim, string unitId, int tile)
    {
        var before = sim.Surface!.Units.Count;
        Do(sim, new DebugSpawnUnitCommand(0, 0, unitId, tile));
        Assert.Equal(before + 1, sim.Surface.Units.Count);
        return sim.Surface.Units[^1];
    }

    /// <summary>No mobile vehicle is inside any building's footprint, or a deployed vehicle's.</summary>
    private static void AssertClearOfFootprints(Simulation sim)
    {
        var s = sim.Surface!;
        foreach (var u in s.Units)
        {
            if (u.Mode != UnitMode.Mobile) continue;
            foreach (var b in s.Structures)
            {
                var r = sim.Data.FootprintOf(b.DefId).Radius;
                var d = (u.Pos - s.CentreOf(b.Tile)).Length();
                Assert.True(d >= r - Fix64.Parse("0.01"), $"unit {u.Id} is {d} from the centre of {b.DefId} on {b.Tile} (footprint {r}) at tick {sim.Tick}");
            }
            foreach (var w in s.Units)
            {
                if (w.Mode == UnitMode.Mobile) continue;
                var r = sim.Data.DeployedFootprintOf(w.DefId);
                var d = (u.Pos - s.CentreOf(w.Tile)).Length();
                Assert.True(d >= r - Fix64.Parse("0.01"), $"unit {u.Id} is {d} from deployed {w.DefId} on {w.Tile} at tick {sim.Tick}");
            }
        }
    }

    [Fact]
    public void RealDataHasFootprintsAndAccessPoints()
    {
        var d = TestData.Real;
        foreach (var id in d.BuildMenu.Append(d.Landing.HubBuilding))
        {
            var f = d.FootprintOf(id);
            Assert.InRange(f.Radius.ToDouble(), 5, 12 - d.Lanes.Clearance.ToDouble());
        }
        Assert.NotNull(d.FootprintOf("colony_hub").Access);
        Assert.NotNull(d.FootprintOf("vehicle_bay").Access);
        Assert.True(d.DeployedFootprintOf("mining_crawler") > Fix64.Zero);
    }

    [Fact]
    public void BadFootprintDataIsReported()
    {
        var data = TestData.Patched(
            ("buildings.json", "\"storage\": 2000, \"footprint_radius\": 7.5, \"access\": [0, 10] },", "\"storage\": 2000, \"footprint_radius\": 11, \"access\": [0, 3] },"),
            ("surfaces.json", "\"lane_offset\": 2.5,", "\"lane_offset\": 9,"));
        var problems = data.Validate();
        Assert.Contains(problems, p => p.Contains("storage_container: footprint_radius must be positive"));
        Assert.Contains(problems, p => p.Contains("storage_container: access must be between"));
        Assert.Contains(problems, p => p.Contains("lane_offset"));
    }

    [Fact]
    public void AccessPointsTurnWithTheBuilding()
    {
        var a = new FixVec2(Fix64.FromInt(9), Fix64.FromInt(2));
        Assert.Equal(a, PlanetSurface.Turn(a, 0));
        // Facing 3 = 180°: the offset points the other way.
        var back = PlanetSurface.Turn(a, 3);
        Assert.Equal(-a.X, back.X);
        Assert.Equal(-a.Y, back.Y);
        // Facing 1 = the view's Basis(Up, -60°): (x, z) → (x cos60 − z sin60, x sin60 + z cos60).
        var one = PlanetSurface.Turn(new FixVec2(Fix64.One, Fix64.Zero), 1);
        Assert.Equal(0.5, one.X.ToDouble(), 6);
        Assert.Equal(0.866, one.Y.ToDouble(), 3);
    }

    [Fact]
    public void ARouteThroughABuildingHexKeepsOutOfItsFootprint()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        // A straight line of three tiles A – B – C in the base, B and all round it the same buildable ground (so the
        // shortest way from A to C is through B even with the extra cost of a building there), a building put on B.
        (int A, int B, int C)? line = null;
        foreach (var b in Enumerable.Range(0, map.Grid.Count).Where(t => map.Grid.Distance(hub, t) is >= 2 and <= 4 && map.Terrain(t).Buildable
                     && s.StructureAt(t) == null && s.Units.All(u => u.Tile != t)))
        {
            var ns = map.Grid.Neighbours(b).ToList();
            if (ns.Count < 6 || ns.Any(n => map.Terrain(n) != map.Terrain(b) || s.StructureAt(n) != null)) continue;
            line = (ns[0], b, ns[3]);
            break;
        }
        Assert.NotNull(line);
        var (ta, tb, tc) = line!.Value;
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "storage_container", tb, 0));
        var rover = Spawn(sim, "survey_rover", ta);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, ta));
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, tc));

        var throughB = false;
        for (var i = 0; i < Sec(60) && (rover.Tile != tc || rover.Path.Count > 0); i++)
        {
            sim.Step();
            throughB |= rover.Tile == tb;
            AssertClearOfFootprints(sim);
            var r = sim.Data.FootprintOf("storage_container").Radius + sim.Data.Lanes.Clearance;
            Assert.True((rover.Pos - s.CentreOf(tb)).Length() >= r - Fix64.Parse("0.05"), $"rover inside the ring at tick {sim.Tick}");
        }
        Assert.True(throughB, "the shortest way crosses the building's hex");
        Assert.Equal(tc, rover.Tile);
        Assert.Empty(rover.Path);
        Assert.Equal(s.CentreOf(tc), rover.Pos); // nothing else on C: parked in the middle
    }

    [Fact]
    public void ArrivingAtABuildingTileEndsAtItsAccessPoint()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var spots = BaseTiles(sim, hub);
        var bay = spots[0];
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "vehicle_bay", bay, 2));
        var habitat = spots.First(t => t != bay && sim.Surface.Map.Grid.Distance(t, bay) > 1);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "prefab_habitat", habitat, 0));
        var rover = s.Units.First(u => u.DefId == "survey_rover");

        void DriveTo(int tile)
        {
            Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, tile));
            for (var i = 0; i < Sec(90) && (rover.Tile != tile || rover.Path.Count > 0); i++)
            {
                sim.Step();
                AssertClearOfFootprints(sim);
            }
            Assert.Equal(tile, rover.Tile);
            Assert.Empty(rover.Path);
        }

        DriveTo(bay);
        Assert.Equal(s.AccessPoint(bay), rover.Pos); // the apron, turned with the bay (facing 2)
        DriveTo(hub);
        Assert.Equal(s.AccessPoint(hub), rover.Pos); // the repair pad
        DriveTo(habitat); // no access point: it parks toward a corner, clear of the building
        Assert.Null(s.AccessPoint(habitat));
        Assert.True((rover.Pos - s.CentreOf(habitat)).Length() > sim.Data.FootprintOf("prefab_habitat").Radius + sim.Data.Lanes.Clearance);
    }

    [Fact]
    public void TwoVehiclesInTheSameHexKeepTheirDistance()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        var start = Enumerable.Range(0, map.Grid.Count).First(t => map.Grid.Distance(hub, t) == 5 && map.Terrain(t).Id == "plains"
            && map.Grid.Neighbours(t).All(n => map.Terrain(n).Passable));
        var a = Spawn(sim, "survey_rover", start);
        var b = Spawn(sim, "survey_rover_mk2", start);
        Do(sim, new DebugMoveUnitCommand(0, a.Id, start));
        Do(sim, new DebugMoveUnitCommand(0, b.Id, start));
        Assert.Equal(a.Tile, b.Tile);
        Assert.True((a.Pos - b.Pos).Length() >= sim.Data.Lanes.AvoidRadius, "two parking spots on one tile");

        var goal = Enumerable.Range(0, map.Grid.Count).First(t => map.Grid.Distance(start, t) == 4 && map.Terrain(t).Passable
            && s.StructureAt(t) == null && Pathfinder.Find(map, start, t) != null && map.Grid.Distance(hub, t) > 3);
        Do(sim, new MoveUnitsCommand(0, new[] { a.Id, b.Id }, goal));
        var closest = Fix64.MaxValue;
        for (var i = 0; i < Sec(60) && (a.Path.Count > 0 || b.Path.Count > 0); i++)
        {
            sim.Step();
            if (a.PushTicks == 0 && b.PushTicks == 0) closest = Fix64.Min(closest, (a.Pos - b.Pos).Length());
        }
        Assert.Empty(a.Path);
        Assert.Empty(b.Path);
        Assert.Equal(goal, a.Tile);
        Assert.Equal(goal, b.Tile);
        Assert.True(closest >= Fix64.FromInt(3), $"they came within {closest} of each other");
        Assert.True((a.Pos - b.Pos).Length() >= sim.Data.Lanes.AvoidRadius, "parked apart");
    }

    private static Simulation CrowdedBase(out List<SurfaceUnit> rovers, out int hub, out int far)
    {
        var sim = Landed(out var h);
        hub = h;
        var s = sim.Surface!;
        var map = s.Map;
        var tiles = BaseTiles(sim, h).Where(t => map.Grid.Distance(h, t) <= 2).ToList();
        var kinds = new[] { "storage_container", "refinery", "fusion_reactor", "vehicle_bay", "science_lab", "prefab_habitat" };
        for (var i = 0; i < kinds.Length && i * 2 < tiles.Count; i++) Do(sim, new DebugPlaceBuildingCommand(0, 0, kinds[i], tiles[i * 2], i));
        var goal = Enumerable.Range(0, map.Grid.Count)
            .Where(t => map.Grid.Distance(h, t) == 7 && map.Terrain(t).Passable && s.StructureAt(t) == null && Pathfinder.Find(map, h, t) != null)
            .OrderBy(t => t).First();
        far = goal;
        rovers = new List<SurfaceUnit>();
        var ids = new[] { "survey_rover", "survey_rover_mk2", "survey_rover_mk3", "construction_rover" };
        // Ten rovers on the far side of the base from where they are going (spawned on and round that tile).
        var start = Enumerable.Range(0, map.Grid.Count)
            .Where(t => map.Grid.Distance(h, t) == 3 && map.Terrain(t).Passable && s.StructureAt(t) == null && Pathfinder.Find(map, t, goal) != null)
            .OrderByDescending(t => map.Grid.Distance(t, goal)).ThenBy(t => t).First();
        for (var i = 0; i < 10; i++) rovers.Add(Spawn(sim, ids[i % ids.Length], start));
        return sim;
    }

    [Fact]
    public void TenRoversCrossACrowdedBaseWithoutDeadlock()
    {
        var sim = CrowdedBase(out var rovers, out var hub, out var far);
        var s = sim.Surface!;
        var ids = rovers.Select(r => r.Id).ToArray();

        void Send(int tile, int seconds)
        {
            Do(sim, new MoveUnitsCommand(0, ids, tile));
            for (var i = 0; i < Sec(seconds) && rovers.Any(r => r.Path.Count > 0); i++)
            {
                sim.Step();
                AssertClearOfFootprints(sim);
            }
            Assert.All(rovers, r => Assert.Empty(r.Path));
        }

        Send(far, 120);   // across the base
        Assert.All(rovers, r => Assert.True(s.Map.Grid.Distance(r.Tile, far) <= 0, $"{r.Id} stopped on {r.Tile}"));
        Send(hub, 150);   // all into the hub's tile: one on the pad, the rest round it
        var parked = rovers.Select(r => r.Pos).ToList();
        Assert.Contains(s.AccessPoint(hub)!.Value, parked);
        Send(far, 150);   // and back out through the base
    }

    [Fact]
    public void ADeployedCrawlerBlocksTheMiddleOfItsTile()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        var crawler = s.Units.First(u => u.DefId == "mining_crawler");
        var deposit = Enumerable.Range(0, map.Grid.Count)
            .Where(t => map.DepositResource[t] >= 0 && map.Grid.Distance(hub, t) is >= 2 and <= 6 && s.StructureAt(t) == null
                && map.Grid.Neighbours(t).Count(n => map.Terrain(n).Passable) == 6)
            .OrderBy(t => map.Grid.Distance(hub, t)).ThenBy(t => t).First();
        // A rover parked in the middle of the deposit gets out of the way when the crawler comes to unfurl there.
        var rover = Spawn(sim, "survey_rover", deposit);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, deposit));
        Assert.Equal(s.CentreOf(deposit), rover.Pos);
        Do(sim, new MineCommand(0, crawler.Id, deposit));
        for (var i = 0; i < Sec(120) && crawler.Mode != UnitMode.Deployed; i++)
        {
            sim.Step();
            AssertClearOfFootprints(sim);
        }
        Assert.Equal(UnitMode.Deployed, crawler.Mode);
        Assert.Equal(s.CentreOf(deposit), crawler.Pos);
        for (var i = 0; i < Sec(10) && rover.Path.Count > 0; i++) sim.Step();
        Assert.True((rover.Pos - s.CentreOf(deposit)).Length() >= sim.Data.DeployedFootprintOf("mining_crawler") + sim.Data.Lanes.Clearance);

        // Driving across the deposit tile goes round the rig.
        var across = map.Grid.Neighbours(deposit).ToList();
        var from = across[0];
        var to = across[3];
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, from));
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, to));
        for (var i = 0; i < Sec(60) && (rover.Tile != to || rover.Path.Count > 0); i++)
        {
            sim.Step();
            AssertClearOfFootprints(sim);
        }
        Assert.Equal(to, rover.Tile);
    }

    [Fact]
    public void LaneDrivingIsDeterministicAndHashed()
    {
        ulong Run()
        {
            var sim = CrowdedBase(out var rovers, out _, out var far);
            sim.Schedule(sim.Tick, new MoveUnitsCommand(0, rovers.Select(r => r.Id).ToArray(), far));
            for (var i = 0; i < Sec(40); i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());

        // The position is in the hash: the same game with one rover nudged to another spot differs.
        var a = CrowdedBase(out var ra, out _, out _);
        var b = CrowdedBase(out var rb, out _, out _);
        Assert.Equal(a.StateHash(), b.StateHash());
        b.Schedule(b.Tick, new MoveUnitsCommand(0, new[] { rb[0].Id }, rb[0].Tile));
        a.Step();
        b.Step();
        a.Step();
        b.Step();
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void TimingStaysCloseToTheTileMoveCost()
    {
        // Ten plains tiles in a straight-ish line take about 10 × move_cost / speed seconds, as before lanes.
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        var rover = Spawn(sim, "survey_rover", hub);
        foreach (var start in Enumerable.Range(0, map.Grid.Count).Where(t => map.Terrain(t).Id == "plains" && map.Grid.Distance(hub, t) >= 8))
        {
            var goal = Enumerable.Range(0, map.Grid.Count).FirstOrDefault(t => map.Grid.Distance(start, t) == 6 && map.Terrain(t).Id == "plains"
                && Pathfinder.Find(map, start, t) is { } p && p.Count == 6 && p.All(x => map.Terrain(x).Id == "plains" && s.StructureAt(x) == null), -1);
            if (goal < 0) continue;
            Do(sim, new DebugMoveUnitCommand(0, rover.Id, start));
            Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, goal));
            var ticks = 1;
            for (; ticks < Sec(60) && (rover.Tile != goal || rover.Path.Count > 0); ticks++) sim.Step();
            var expected = 6 * map.Terrain(goal).MoveCost / sim.Data.SurfaceUnit("survey_rover").Speed.ToDouble() * Simulation.TickRate;
            Assert.InRange(ticks, expected * 0.8, expected * 1.1);
            return;
        }
        Assert.Fail("no straight plains run found");
    }
}
