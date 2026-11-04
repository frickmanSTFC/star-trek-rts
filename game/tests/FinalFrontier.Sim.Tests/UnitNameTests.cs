using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Unit names and hull numbers (surfaces.json unit_names), and the survey rover marks built at the Vehicle Bay.</summary>
public class UnitNameTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int seconds) => seconds * Simulation.TickRate;

    /// <summary>One or more players landed on the same planet (each at its own site), with plenty to spend.</summary>
    private static Simulation Landed(GameData data, ulong seed, int players = 1)
    {
        var sim = new Simulation(data, seed);
        sim.LoadSurface(MatchSetup.MvpPlanetClass, seed);
        for (var p = 0; p < players; p++) sim.AddPlayer("federation");
        var s = sim.Surface!;
        var taken = new List<int>();
        for (var p = 0; p < players; p++)
        {
            var site = Enumerable.Range(0, s.Map.Grid.Count)
                .First(t => s.CanLand(p, t) && taken.All(o => s.Map.Grid.Distance(o, t) > 12));
            taken.Add(site);
            sim.Schedule(0, new LandColonyCommand(p, site));
        }
        for (var i = 0; i <= data.Landing.DescentSeconds * Simulation.TickRate; i++) sim.Step();
        foreach (var pl in sim.Players)
        {
            pl.Stock.Add(R("duranium"), Fix64.FromInt(2000));
            pl.Stock.Add(R("tritanium"), Fix64.FromInt(800));
        }
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>A finished Vehicle Bay two tiles from the player's first base.</summary>
    private static SurfaceStructure Bay(Simulation sim, int player = 0)
    {
        var s = sim.Surface!;
        var hub = s.Bases(player)[0].Tile;
        var tile = s.Map.Grid.Within(hub, 2).First(t => s.Map.Grid.Distance(t, hub) == 2 &&
            s.CheckPlacement(sim, player, "vehicle_bay", t) == PlacementProblem.None);
        Do(sim, new PlaceBuildingCommand(player, "vehicle_bay", tile, 0));
        var bay = s.StructureAt(tile)!;
        while (bay.State != StructureState.Complete) sim.Step();
        sim.Step();
        return bay;
    }

    /// <summary>Keeps the bay's queue topped up with this unit until the player has made that many of it.</summary>
    private static void RollOut(Simulation sim, SurfaceStructure bay, string unitId, int count)
    {
        var s = sim.Surface!;
        var target = s.Units.Count(u => u.Owner == bay.Owner) + count;
        var queue = Data.Building("vehicle_bay").ProductionQueue;
        for (var guard = 0; s.Units.Count(u => u.Owner == bay.Owner) < target; guard++)
        {
            Assert.True(guard < Sec(4000), "roll-outs took too long");
            var ordered = s.Units.Count(u => u.Owner == bay.Owner) + bay.Production.Count;
            if (bay.Production.Count < queue && ordered < target) sim.Schedule(sim.Tick, new BuildUnitCommand(bay.Owner, bay.Id, unitId));
            sim.Step();
        }
    }

    private static GameData FastRovers() => TestData.Patched(("surfaces.json",
        "\"speed\": 2, \"survey_radius\": 2, \"cost\": { \"duranium\": 60 }, \"build_seconds\": 20 }",
        "\"speed\": 2, \"survey_radius\": 2, \"cost\": { \"duranium\": 1 }, \"build_seconds\": 1 }"));

    private static void AssertNamedAndUnique(IEnumerable<SurfaceUnit> units)
    {
        var list = units.ToList();
        Assert.All(list, u =>
        {
            Assert.False(string.IsNullOrEmpty(u.Name));
            Assert.False(string.IsNullOrEmpty(u.Number));
            Assert.All(u.Number, c => Assert.InRange(c, '0', '9'));
        });
        Assert.Equal(list.Count, list.Select(u => u.Name).Distinct().Count());
        Assert.Equal(list.Count, list.Select(u => u.Number).Distinct().Count());
    }

    [Fact]
    public void TheNamePoolAndMarksAreInTheData()
    {
        var names = Data.UnitNames;
        Assert.True(names.Pool.Count >= 100);
        Assert.Contains("KALTY", names.Pool);
        Assert.Equal((10, 99), (names.NumbersFrom, names.NumbersTo));
        Assert.Equal(1, Data.SurfaceUnit("survey_rover").Mark);
        Assert.Equal(2, Data.SurfaceUnit("survey_rover_mk2").Mark);
        Assert.Equal(3, Data.SurfaceUnit("survey_rover_mk3").Mark);
        Assert.Null(Data.SurfaceUnit("mining_crawler").Mark);
        Assert.Empty(Data.Validate());
    }

    [Fact]
    public void BadNamesNumbersAndMarksAreCaught()
    {
        void Flags(string from, string to, string expect)
        {
            var problems = TestData.Patched(("surfaces.json", from, to)).Validate();
            Assert.Contains(problems, p => p.Contains(expect));
        }
        Flags("\"KALTY\", \"TALUS\"", "\"KALTY\", \"KALTY\"", "listed more than once");
        Flags("\"PEBBLE\" ]", "\"Pebble\" ]", "uppercase letters");
        Flags("\"PEBBLE\" ]", "\"PEBBLE\", \"MK 2\" ]", "uppercase letters");
        Flags("\"numbers\": [10, 99]", "\"numbers\": [99, 10]", "numbers [99, 10]");
        Flags("\"id\": \"survey_rover_mk2\", \"mark\": 2,", "\"id\": \"survey_rover_mk2\", \"mark\": 4,", "mark must be 1, 2 or 3");
        Flags("\"pool\": [", "\"pool\": [ ], \"old_pool\": [", "name pool is empty");
    }

    [Fact]
    public void LandingRoversGetNamesAndNumbers()
    {
        var sim = Landed(Data, 1701);
        var units = sim.Surface!.Units;
        Assert.Equal(Data.Landing.Units.Count, units.Count);
        AssertNamedAndUnique(units);
        Assert.All(units, u =>
        {
            Assert.Contains(u.Name, Data.UnitNames.Pool);
            Assert.InRange(int.Parse(u.Number), 10, 99);
        });
        var p = sim.Surface.Players[0];
        Assert.Equal(units.Select(u => u.Name), p.UsedUnitNames);
        Assert.Equal(units.Select(u => u.Number), p.UsedUnitNumbers);
    }

    [Fact]
    public void DeliveredUnitsAreNamedToo()
    {
        var sim = Landed(Data, 1701);
        var s = sim.Surface!;
        var hub = s.Bases(0)[0].Tile;
        var spot = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.Grid.Distance(t, hub) > 20 && s.ShuttleProblem(sim, 0, t, "survey_rover") == null);
        Do(sim, new ShuttleCommand(0, spot, "survey_rover"));
        for (var i = 0; i < Sec(Data.Orbit!.ShuttleSeconds) + 1; i++) sim.Step();
        Assert.Equal(Data.Landing.Units.Count + 1, s.Units.Count);
        AssertNamedAndUnique(s.Units);
    }

    [Fact]
    public void NamesLookRandomAndDependOnTheSeed()
    {
        var a = Landed(Data, 1701).Surface!.Units.Select(u => u.Name + u.Number).ToList();
        var b = Landed(Data, 42).Surface!.Units.Select(u => u.Name + u.Number).ToList();
        Assert.NotEqual(a, b);
        // Not simply the pool in file order.
        Assert.NotEqual(Data.UnitNames.Pool.Take(a.Count), Landed(Data, 1701).Surface!.Units.Select(u => u.Name));
    }

    [Fact]
    public void APlayerNeverGetsTheSameNameOrNumberTwiceAcrossManyRollOuts()
    {
        var data = FastRovers();
        var sim = Landed(data, 1701);
        var bay = Bay(sim);
        RollOut(sim, bay, "survey_rover", 200);
        var s = sim.Surface!;
        var mine = s.Units.Where(u => u.Owner == 0).ToList();
        Assert.Equal(200 + data.Landing.Units.Count, mine.Count);
        AssertNamedAndUnique(mine);

        // The pool ran out, so names went round again with a numeral; the 10..99 numbers ran out, so they went to 100..999.
        var pool = data.UnitNames.Pool;
        Assert.Equal(pool.Count, mine.Count(u => pool.Contains(u.Name)));
        Assert.All(mine.Where(u => !pool.Contains(u.Name)), u => Assert.EndsWith(" II", u.Name));
        Assert.Equal(90, mine.Count(u => u.Number.Length == 2));
        Assert.All(mine.Where(u => u.Number.Length != 2), u => Assert.InRange(int.Parse(u.Number), 100, 999));
        // Handed out in order: the first rounds are used up before the next starts.
        Assert.All(mine.Take(pool.Count), u => Assert.Contains(u.Name, pool));
        Assert.All(mine.Take(90), u => Assert.Equal(2, u.Number.Length));
        Assert.Equal(mine.Select(u => u.Name), s.Players[0].UsedUnitNames);
    }

    [Fact]
    public void DifferentPlayersCountSeparately()
    {
        var sim = Landed(Data, 1701, players: 2);
        var s = sim.Surface!;
        var bay0 = Bay(sim, 0);
        var bay1 = Bay(sim, 1);
        sim.Schedule(sim.Tick, new BuildUnitCommand(0, bay0.Id, "survey_rover"));
        sim.Schedule(sim.Tick, new BuildUnitCommand(1, bay1.Id, "survey_rover"));
        sim.Schedule(sim.Tick, new BuildUnitCommand(1, bay1.Id, "survey_rover"));
        for (var i = 0; i < Sec(2 * Data.SurfaceUnit("survey_rover").BuildSeconds + 5); i++) sim.Step();

        var p0 = s.Units.Where(u => u.Owner == 0).ToList();
        var p1 = s.Units.Where(u => u.Owner == 1).ToList();
        Assert.Equal(Data.Landing.Units.Count + 1, p0.Count);
        Assert.Equal(Data.Landing.Units.Count + 2, p1.Count);
        AssertNamedAndUnique(p0);
        AssertNamedAndUnique(p1);
        // Each player has their own order (they may share a name or two, which is fine).
        Assert.NotEqual(p0.Select(u => u.Name + u.Number), p1.Take(p0.Count).Select(u => u.Name + u.Number));
        Assert.Equal(p1.Select(u => u.Name), s.Players[1].UsedUnitNames);
    }

    [Fact]
    public void NamesAreDeterministic()
    {
        (List<string> Names, ulong Hash) Run()
        {
            var sim = Landed(Data, 1701, players: 2);
            var bay = Bay(sim);
            var s = sim.Surface!;
            var hub = s.Bases(0)[0].Tile;
            var spot = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.Grid.Distance(t, hub) > 20 && s.ShuttleProblem(sim, 0, t, "survey_rover") == null);
            sim.Schedule(sim.Tick + 1, new BuildUnitCommand(0, bay.Id, "survey_rover_mk2"));
            sim.Schedule(sim.Tick + 2, new ShuttleCommand(0, spot, "mining_crawler"));
            sim.Schedule(sim.Tick + 3, new BuildUnitCommand(0, bay.Id, "survey_rover"));
            for (var i = 0; i < Sec(80); i++) sim.Step();
            return (s.Units.Select(u => $"{u.Owner}:{u.DefId}:{u.Name}:{u.Number}").ToList(), sim.StateHash());
        }
        var a = Run();
        var b = Run();
        Assert.Equal(Data.Landing.Units.Count * 2 + 3, a.Names.Count);
        Assert.Equal(a.Names, b.Names);
        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void NamesAreInTheStateHash()
    {
        // Same everything except the number range (which only reshuffles the numbers): the hashes differ because they do.
        var other = TestData.Patched(("surfaces.json", "\"numbers\": [10, 99]", "\"numbers\": [10, 98]"));
        var a = Landed(Data, 1701);
        var b = Landed(other, 1701);
        Assert.NotEqual(a.Surface!.Units.Select(u => u.Number), b.Surface!.Units.Select(u => u.Number));
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void TheMarksBuildAtTheBayWithTheirOwnStats()
    {
        var sim = Landed(Data, 1701);
        var bay = Bay(sim);
        var s = sim.Surface!;
        foreach (var id in new[] { "survey_rover_mk2", "survey_rover_mk3" })
        {
            var def = Data.SurfaceUnit(id);
            Assert.Contains(id, Data.Building("vehicle_bay").Produces);
            Assert.Null(s.ProductionProblem(sim, 0, bay.Id, id));
            var before = s.Units.Count;
            Do(sim, new BuildUnitCommand(0, bay.Id, id));
            for (var i = 0; i < Sec(def.BuildSeconds) + 3; i++) sim.Step();
            Assert.Equal(before + 1, s.Units.Count);
            var made = s.Units[^1];
            Assert.Equal(id, made.DefId);
            Assert.Equal(def.Speed * Simulation.SecondsPerTick, made.SpeedPerTick);
            Assert.Equal(def.SurveyRadius, made.SurveyRadius);
            Assert.Equal(bay.Tile, made.BuiltAtTile);
            Assert.All(s.Map.Grid.Within(made.Tile, def.SurveyRadius), t => Assert.True(s.Players[0].Surveyed[t]));
        }
        var mk1 = Data.SurfaceUnit("survey_rover");
        var mk2 = Data.SurfaceUnit("survey_rover_mk2");
        var mk3 = Data.SurfaceUnit("survey_rover_mk3");
        Assert.True(mk1.Speed < mk2.Speed && mk2.Speed < mk3.Speed);
        Assert.True(mk1.SurveyRadius < mk2.SurveyRadius && mk2.SurveyRadius < mk3.SurveyRadius);
        Assert.Contains(mk3.Cost, c => c.Resource == R("dilithium"));
        AssertNamedAndUnique(s.Units);
    }

    [Fact]
    public void ANameAndNumberStayFixedForItsLife()
    {
        var sim = Landed(Data, 1701);
        var s = sim.Surface!;
        var bay = Bay(sim);
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        var (name, number) = (rover.Name, rover.Number);
        var target = s.Map.Grid.Within(rover.Tile, 6).First(t => s.Map.Grid.Distance(t, rover.Tile) == 6 && s.Map.Terrain(t).Passable);
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, target));
        RollOut(sim, bay, "survey_rover", 3);
        for (var i = 0; i < Sec(30); i++) sim.Step();
        Assert.Equal(name, rover.Name);
        Assert.Equal(number, rover.Number);
        Assert.Equal(name, s.Players[0].UsedUnitNames[s.Units.ToList().IndexOf(rover)]);
        AssertNamedAndUnique(s.Units);
    }
}
