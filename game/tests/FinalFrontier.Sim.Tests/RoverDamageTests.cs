using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Rover damage (terrain wear, hazards, toxic ground, predators), flat tyres, field tyre changes and pad repair.</summary>
public class RoverDamageTests
{
    private static int Sec(int seconds) => seconds * Simulation.TickRate;
    private static readonly Fix64 Hundred = Fix64.FromInt(100);
    private static Fix64 F(int v) => Fix64.FromInt(v);
    private static int R(GameData d, string id) => d.Resource(id).Index;

    /// <summary>The real data with no luck involved: no hazards, no flats, no predator attacks (wear and toxic ground stay).</summary>
    private static GameData Calm(params (string Pattern, string To)[] more) => TestData.Regexed("surfaces.json",
        new[] { ("\"hazard\": [0-9.]+", "\"hazard\": 0"), ("\"flat\": [0-9.]+", "\"flat\": 0"), ("\"chance_per_min\": 0\\.4,", "\"chance_per_min\": 0,") }
            .Concat(more).ToArray());

    private static Simulation Landed(GameData data, out int hub)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        hub = site;
        if (data.Fabrication != null) sim.Players[0].Stock.Add(data.Resource("rover_spares").Index, Fix64.FromInt(20)); // pad repairs take kits
        return sim;
    }

    private static SurfaceUnit Rover(Simulation sim) => sim.Surface!.Units.First(u => u.DefId == "survey_rover");

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static bool Clean(Simulation sim, int t)
    {
        var map = sim.Surface!.Map;
        return map.Terrain(t).Passable && !map.Toxic[t] && map.WildlifeAt[t] != Wildlife.Predators && sim.Surface.StructureAt(t) == null;
    }

    /// <summary>Two neighbouring clean tiles of this terrain (no toxic ground, predators or buildings).</summary>
    private static (int From, int To) Pair(Simulation sim, string terrain)
    {
        var map = sim.Surface!.Map;
        for (var t = 0; t < map.Grid.Count; t++)
        {
            if (map.Terrain(t).Id != terrain || !Clean(sim, t)) continue;
            foreach (var n in map.Grid.Neighbours(t))
                if (map.Terrain(n).Id == terrain && Clean(sim, n)) return (t, n);
        }
        throw new InvalidOperationException($"No two {terrain} tiles side by side.");
    }

    /// <summary>Puts the unit on From and drives it to the neighbouring To; returns the ticks it spent driving.</summary>
    private static int Drive(Simulation sim, SurfaceUnit u, int from, int to)
    {
        Do(sim, new DebugMoveUnitCommand(0, u.Id, from));
        Do(sim, new MoveUnitsCommand(0, new[] { u.Id }, to));
        var ticks = 1;
        for (; (u.Tile != to || u.Path.Count > 0) && ticks < Sec(120); ticks++) sim.Step(); // into the tile and parked
        Assert.Equal(to, u.Tile);
        return ticks;
    }

    [Fact]
    public void RealRoversHaveDamageAndTyres()
    {
        foreach (var id in new[] { "survey_rover", "survey_rover_mk2", "survey_rover_mk3" })
        {
            var def = TestData.Real.SurfaceUnit(id);
            Assert.NotNull(def.Damage);
            Assert.NotNull(def.Tyres);
            Assert.NotNull(def.Repair);
        }
        Assert.Equal(4, TestData.Real.SurfaceUnit("survey_rover").Tyres!.Wheels);
        Assert.Equal(6, TestData.Real.SurfaceUnit("survey_rover_mk2").Tyres!.Wheels);
        Assert.Null(TestData.Real.SurfaceUnit("mining_crawler").Damage); // the crawler keeps its breakdowns
        // Mk III is the toughest: less wear on hills, more spares.
        var mk1 = TestData.Real.SurfaceUnit("survey_rover");
        var mk3 = TestData.Real.SurfaceUnit("survey_rover_mk3");
        Assert.True(mk3.Damage!.On("hills")!.Wear < mk1.Damage!.On("hills")!.Wear);
        Assert.True(mk3.Tyres!.Spares > mk1.Tyres!.Spares);
    }

    [Fact]
    public void BadDamageDataIsReported()
    {
        var data = TestData.Patched(("surfaces.json", "\"tyres\": { \"wheels\": 4,", "\"tyres\": { \"wheels\": 5,"),
            ("surfaces.json", "\"slow_below\": 40,", "\"slow_below\": 0,"));
        var problems = data.Validate();
        Assert.Contains(problems, p => p.Contains("survey_rover tyres: wheels must be 4 or 6"));
        Assert.Contains(problems, p => p.Contains("survey_rover_mk3 damage: slow_below"));
    }

    [Fact]
    public void RoughGroundWearsMoreThanPlains()
    {
        var data = Calm();
        var sim = Landed(data, out _);
        var rover = Rover(sim);
        var wear = data.SurfaceUnit("survey_rover").Damage!;

        var (pf, pt) = Pair(sim, "plains");
        var plainsTicks = Drive(sim, rover, pf, pt);
        var plainsLoss = Hundred - rover.Integrity;
        Assert.InRange(plainsLoss.ToDouble(), 0, 1);
        Assert.Equal(wear.On("plains")!.Wear.ToDouble() * plainsTicks / Simulation.TicksPerMinute, plainsLoss.ToDouble(), 4);

        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, Hundred));
        var (hf, ht) = Pair(sim, "hills");
        var hillsTicks = Drive(sim, rover, hf, ht);
        var hillsLoss = Hundred - rover.Integrity;
        Assert.Equal(wear.On("hills")!.Wear.ToDouble() * hillsTicks / Simulation.TicksPerMinute, hillsLoss.ToDouble(), 4);
        Assert.True(hillsLoss > plainsLoss * 4, $"hills {hillsLoss} vs plains {plainsLoss}");

        // Parked, it doesn't wear.
        var before = rover.Integrity;
        Run(sim, Sec(10));
        Assert.Equal(before, rover.Integrity);
    }

    [Fact]
    public void HazardsHitWhileDriving()
    {
        // Every tick of driving on plains rolls a hazard; plains hazards are rock strikes and sinkholes, fixed at 10.
        var data = Calm(("\"plains\": \\{ \"wear\": ([0-9.]+), \"hazard\": 0,", "\"plains\": { \"wear\": $1, \"hazard\": 900,"),
            ("\"damage\": \\[\\d+, \\d+\\], \"terrain\"", "\"damage\": [10, 10], \"terrain\""));
        var sim = Landed(data, out _);
        var rover = Rover(sim);
        var (from, to) = Pair(sim, "plains");
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, from));
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, to));
        Assert.Equal(sim.Tick - 1, rover.HazardAtTick);
        Assert.Contains(rover.LastHazard, new[] { "rock", "sinkhole" });
        Assert.Equal(10, rover.LastHazardDamage);
        Assert.Equal("Rock strike", data.SurfaceUnit("survey_rover").Damage!.Hazards.First(h => h.Id == "rock").Name);
        Assert.True(rover.Integrity < F(91));
    }

    [Fact]
    public void ZeroIntegrityDisablesARover()
    {
        var data = Calm();
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var rover = Rover(sim);
        var (from, to) = Pair(sim, "plains");
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, from));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, Fix64.Zero));
        Assert.True(rover.Broken);
        Assert.True(s.Disabled(rover));
        Assert.Equal(sim.Tick - 1, rover.BrokeAtTick);
        Assert.Equal(0, s.SurveyRadiusOf(rover));

        // Move orders are ignored; a repair order sends it limping home at a crawl.
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, to));
        Assert.Empty(rover.Path);
        Assert.NotNull(s.DriveProblem(rover));
        Do(sim, new RepairCommand(0, new[] { rover.Id }));
        Assert.NotEmpty(rover.Path);
        Assert.Equal(rover.SpeedPerTick * data.SurfaceUnit("survey_rover").Damage!.Crawl, s.SpeedPerTick(rover));
        Assert.Equal(hub, s.NearestRepairPad(0, rover.Tile)!.Tile);
    }

    [Fact]
    public void LowIntegritySlowsItAndDimsItsSensors()
    {
        var sim = Landed(TestData.Real, out _);
        var s = sim.Surface!;
        var rover = Rover(sim);
        var d = TestData.Real.SurfaceUnit("survey_rover").Damage!;
        Assert.Equal(rover.SpeedPerTick, s.SpeedPerTick(rover));
        Assert.Equal(rover.SurveyRadius, s.SurveyRadiusOf(rover));

        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, F(60)));
        Assert.Equal(rover.SpeedPerTick, s.SpeedPerTick(rover)); // above slow_below
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, F(40)));
        var share = d.SlowestDrive + (Fix64.One - d.SlowestDrive) * F(40) / d.SlowBelow;
        Assert.Equal(rover.SpeedPerTick * share, s.SpeedPerTick(rover));
        Assert.Equal(rover.SurveyRadius, s.SurveyRadiusOf(rover)); // sensors still fine at 40
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, F(20)));
        Assert.Equal(rover.SurveyRadius - d.SensorLoss, s.SurveyRadiusOf(rover));
        Assert.False(rover.Broken);
    }

    [Fact]
    public void FlatTyresSlowItDown()
    {
        var sim = Landed(Calm(), out _);
        var s = sim.Surface!;
        var rover = Rover(sim);
        var t = TestData.Real.SurfaceUnit("survey_rover").Tyres!;
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, F(1)));
        Assert.Equal(1, s.FlatCount(rover));
        Assert.Equal(0, rover.LastFlat);
        Assert.Equal("front left", Wheels.Name(t.Wheels, rover.LastFlat));
        Assert.Equal(rover.SpeedPerTick * t.FlatSpeed, s.SpeedPerTick(rover));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, F(0b1001)));
        Assert.Equal(2, s.FlatCount(rover));
        Assert.Equal(3, rover.LastFlat);
        Assert.Equal("rear right", Wheels.Name(t.Wheels, 3));
        Assert.Equal(rover.SpeedPerTick * t.TwoFlatSpeed, s.SpeedPerTick(rover));

        // A flat also wears it faster.
        var (from, to) = Pair(sim, "plains");
        var ticks = Drive(sim, rover, from, to);
        var wear = TestData.Real.SurfaceUnit("survey_rover").Damage!.On("plains")!.Wear * (Fix64.One + t.FlatWear * 2);
        Assert.Equal(wear.ToDouble() * ticks / Simulation.TicksPerMinute, (Hundred - rover.Integrity).ToDouble(), 4);
    }

    [Fact]
    public void RoughGroundGivesFlats()
    {
        var data = Calm(("\"hills\": \\{ \"wear\": ([0-9.]+), \"hazard\": 0, \"flat\": 0", "\"hills\": { \"wear\": $1, \"hazard\": 0, \"flat\": 900"));
        var sim = Landed(data, out _);
        var rover = Rover(sim);
        var (from, to) = Pair(sim, "hills");
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, from));
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, to));
        Assert.Equal(1, sim.Surface!.FlatCount(rover));
        Assert.Equal(sim.Tick - 1, rover.FlatAtTick);
        Assert.InRange(rover.LastFlat, 0, 3);
        Run(sim, 3); // one more each tick, until every wheel is flat
        Assert.Equal(4, sim.Surface.FlatCount(rover));
        Run(sim, 3);
        Assert.Equal(4, sim.Surface.FlatCount(rover));
    }

    [Fact]
    public void TheCrewChangeATyreWithTheSpare()
    {
        var data = Calm();
        var sim = Landed(data, out _);
        var s = sim.Surface!;
        var rover = Rover(sim);
        var t = data.SurfaceUnit("survey_rover").Tyres!;
        Assert.Equal(t.Spares, rover.Spares);
        Assert.Equal("no flat tyre", s.ChangeTyreProblem(0, rover.Id));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, F(0b0110)));
        Assert.Null(s.ChangeTyreProblem(0, rover.Id));

        // A move order during the change cancels it.
        var (from, to) = Pair(sim, "plains");
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, from));
        Do(sim, new ChangeTyreCommand(0, new[] { rover.Id }));
        Assert.True(s.ChangingTyre(rover));
        Run(sim, Sec(5));
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, to));
        Assert.False(s.ChangingTyre(rover));
        Assert.Equal(2, s.FlatCount(rover));
        Run(sim, Sec(30));

        Do(sim, new ChangeTyreCommand(0, new[] { rover.Id }));
        Assert.Empty(rover.Path);
        Run(sim, t.ChangeSeconds * Simulation.TickRate - 3);
        Assert.True(s.ChangingTyre(rover));
        Assert.True(s.TyreChangeFraction(rover) > Fix64.Half);
        Run(sim, 3);
        Assert.False(s.ChangingTyre(rover));
        Assert.Equal(0b0100, rover.FlatTyres); // the lowest flat wheel was changed
        Assert.Equal(t.Spares - 1, rover.Spares);
        Assert.Equal("no spare left: drive to a repair pad", s.ChangeTyreProblem(0, rover.Id));
        Assert.Equal("it has no tyres to change", s.ChangeTyreProblem(0, s.Units.First(u => u.DefId == "mining_crawler").Id));
    }

    [Fact]
    public void ThePadFixesEverything()
    {
        var data = Calm();
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var rover = Rover(sim);
        var def = data.SurfaceUnit("survey_rover");
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, F(40)));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, F(1)));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Spares, Fix64.Zero));
        Assert.True(s.NeedsRepair(rover));
        var cost = s.RepairCost(rover);
        var kit = R(data, "rover_spares");
        // 60 points at one kit per kit_points (rounded up), plus pad_tyre_kits for each of two tyres (the flat and the missing spare).
        var f = data.Fabrication!;
        var expected = Fix64.FromInt((int)System.Math.Ceiling(60 / f.KitPoints.ToDouble()) + 2 * f.PadTyreKits);
        Assert.Equal(expected, cost.Single(c => c.Resource == kit).Amount);
        Assert.Equal(expected, Fix64.FromInt(s.KitsNeeded(rover)));

        Do(sim, new RepairCommand(0, new[] { rover.Id }));
        for (var i = 0; i < Sec(300) && (rover.Tile != hub || rover.Path.Count > 0); i++) sim.Step(); // parked on the pad
        Assert.Equal(hub, rover.Tile);
        Assert.Equal(s.AccessPoint(hub), rover.Pos);
        Assert.True(s.IsRepairing(rover));
        var stock = sim.Players[0].Stock[kit];
        for (var i = 0; i < Sec(60) && s.NeedsRepair(rover); i++) sim.Step();
        Assert.False(s.NeedsRepair(rover));
        Assert.Equal(Hundred, rover.Integrity);
        Assert.Equal(0, rover.FlatTyres);
        Assert.Equal(def.Tyres!.Spares, rover.Spares);
        Assert.False(s.IsRepairing(rover));
        Assert.Equal(stock - expected, sim.Players[0].Stock[kit]); // the quoted kits, taken when the crew started
    }

    [Fact]
    public void ADisabledRoverIsRepairedOnThePad()
    {
        var sim = Landed(Calm(), out var hub);
        var s = sim.Surface!;
        var rover = Rover(sim);
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, Fix64.Zero));
        Assert.True(s.Disabled(rover));
        Do(sim, new RepairCommand(0, new[] { rover.Id }));
        for (var i = 0; i < Sec(600) && rover.Tile != hub; i++) sim.Step();
        Assert.Equal(hub, rover.Tile);
        for (var i = 0; i < Sec(60) && s.NeedsRepair(rover); i++) sim.Step();
        Assert.False(rover.Broken);
        Assert.Equal(Hundred, rover.Integrity);
        Assert.Equal(rover.SurveyRadius, s.SurveyRadiusOf(rover));
    }

    [Fact]
    public void ToxicGroundCorrodesAndPredatorsAttack()
    {
        var data = Calm(("\"wildlife\": \\{ \"chance_per_min\": 0,", "\"wildlife\": { \"chance_per_min\": 900,"));
        var sim = Landed(data, out _);
        var s = sim.Surface!;
        var map = s.Map;
        var rover = Rover(sim);
        var d = data.SurfaceUnit("survey_rover").Damage!;

        var toxic = Enumerable.Range(0, map.Grid.Count).First(t => map.Toxic[t] && map.Terrain(t).Passable && map.WildlifeAt[t] != Wildlife.Predators && s.StructureAt(t) == null);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, toxic));
        var before = rover.Integrity;
        Run(sim, Sec(60));
        Assert.Equal(d.ToxicPerMinute.ToDouble(), (before - rover.Integrity).ToDouble(), 3);

        var lair = Enumerable.Range(0, map.Grid.Count).First(t => map.WildlifeAt[t] == Wildlife.Predators && !map.Toxic[t] && map.Terrain(t).Passable && s.StructureAt(t) == null);
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, Hundred));
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, lair));
        Assert.Equal(PlanetSurface.WildlifeHazard, rover.LastHazard);
        Assert.InRange(rover.LastHazardDamage, d.WildlifeMin, d.WildlifeMax);
        Assert.Equal("Wildlife attack", s.HazardName(rover));
    }

    [Fact]
    public void TestPanelSetsTyresAndRefusesBadValues()
    {
        var sim = Landed(TestData.Real, out _);
        var s = sim.Surface!;
        var rover = Rover(sim);
        var crawler = s.Units.First(u => u.DefId == "mining_crawler");
        Assert.Equal("this vehicle has no tyres", sim.DebugProblem(new DebugUnitCommand(0, crawler.Id, DebugUnitField.FlatTyres, F(1))));
        Assert.NotNull(sim.DebugProblem(new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, F(16)))); // Mk I has 4 wheels
        Assert.NotNull(sim.DebugProblem(new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, Fix64.Half)));
        Assert.NotNull(sim.DebugProblem(new DebugUnitCommand(0, rover.Id, DebugUnitField.Spares, F(21))));

        var edits = sim.DebugEdits;
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, F(0b0100)));
        Assert.Equal(0b0100, rover.FlatTyres);
        Assert.Equal(2, rover.LastFlat);
        // Undo = set back what it was.
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.FlatTyres, Fix64.Zero));
        Assert.Equal(0, rover.FlatTyres);
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Spares, F(3)));
        Assert.Equal(3, rover.Spares);
        Assert.Equal(edits + 3, sim.DebugEdits);

        // The panel's "broken" on a rover disables it where it stands.
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Broken, Fix64.One));
        Assert.True(s.Disabled(rover));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Broken, Fix64.Zero));
        Assert.False(s.Disabled(rover));
    }

    [Fact]
    public void RoverDamageIsDeterministicAndHashed()
    {
        // Rough luck everywhere: hazards and flats every few seconds of driving.
        var data = TestData.Regexed("surfaces.json", ("\"hazard\": ([0-9.]+)", "\"hazard\": 6"), ("\"flat\": ([0-9.]+)", "\"flat\": 4"));
        Simulation Play()
        {
            var sim = Landed(data, out var hub);
            var map = sim.Surface!.Map;
            var rovers = sim.Surface.Units.Where(u => u.DefId.StartsWith("survey_rover")).Select(u => u.Id).ToList();
            var far = Enumerable.Range(0, map.Grid.Count).Where(t => map.Terrain(t).Passable && map.Grid.Distance(t, hub) is > 8 and < 14).Take(6).ToList();
            for (var leg = 0; leg < 6; leg++)
            {
                sim.Schedule(sim.Tick, new MoveUnitsCommand(0, rovers, far[leg]));
                Run(sim, Sec(20));
                sim.Schedule(sim.Tick, new ChangeTyreCommand(0, rovers));
                Run(sim, Sec(10));
            }
            return sim;
        }
        var a = Play();
        var b = Play();
        Assert.Equal(a.StateHash(), b.StateHash());
        var hit = a.Surface!.Units.Where(u => u.DefId.StartsWith("survey_rover")).ToList();
        Assert.Contains(hit, u => u.HazardAtTick >= 0);
        Assert.Contains(hit, u => u.FlatAtTick >= 0);
        Assert.Contains(hit, u => u.Integrity < Hundred);

        // A flat tyre (or a spare) is part of the state.
        var rover = hit[0];
        var hash = b.StateHash();
        b.Schedule(b.Tick, new DebugUnitCommand(0, rover.Id, DebugUnitField.Spares, F(rover.Spares + 1)));
        a.Schedule(a.Tick, new DebugUnitCommand(0, rover.Id, DebugUnitField.Spares, F(rover.Spares)));
        a.Step();
        b.Step();
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.NotEqual(hash, b.StateHash());
    }

    [Fact]
    public void Mk3IsTougher()
    {
        var sim = Landed(Calm(), out var hub);
        var s = sim.Surface!;
        Do(sim, new DebugSpawnUnitCommand(0, 0, "survey_rover_mk3", hub));
        var mk3 = s.Units.Last();
        Assert.Equal("survey_rover_mk3", mk3.DefId);
        Assert.Equal(2, mk3.Spares);
        var mk1 = Rover(sim);
        var (from, to) = Pair(sim, "hills");
        Drive(sim, mk1, from, to);
        Drive(sim, mk3, from, to);
        Assert.True(mk3.Integrity > mk1.Integrity);
        Do(sim, new DebugUnitCommand(0, mk3.Id, DebugUnitField.FlatTyres, F(0b100000)));
        Assert.Equal("rear right", Wheels.Name(6, 5));
        Assert.Equal(mk3.SpeedPerTick * TestData.Real.SurfaceUnit("survey_rover_mk3").Tyres!.FlatSpeed, s.SpeedPerTick(mk3));
    }
}
