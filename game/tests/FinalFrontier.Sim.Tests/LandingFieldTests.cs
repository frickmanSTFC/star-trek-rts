using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Landing fields (sim/Surface/LandingField.cs, surfaces "landing_field"): every colony core's pads, what is refused
/// on them, and the drop ships' stay on the ground: unload, hop to a pad, refuel (a delivery's fuel is paid there, and it
/// waits without it), climb back; pads fill; the first landing's vehicles each come down in a ship of the convoy.
/// </summary>
public class LandingFieldTests
{
    private static GameData Data => TestData.Real;
    private static LandingFieldRules Rules => Data.LandingField!;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int s) => s * Simulation.TickRate;
    private static int Ticks(Fix64 seconds) => (seconds * Simulation.TickRate).FloorToInt();

    /// <summary>Lands the colony at tick 0 and steps to the tick after touchdown (the hub is still assembling).</summary>
    private static Simulation TouchedDown(out SurfaceStructure hub, GameData? data = null, Simulation? game = null)
    {
        data ??= Data;
        var sim = game ?? MatchSetup.CreatePlanetLanding(data, 1701);
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        sim.Schedule(sim.Tick, new LandColonyCommand(0, site));
        while (s.Players[0].Phase != LandingPhase.Landed) sim.Step();
        hub = s.StructureAt(site)!;
        return sim;
    }

    /// <summary>Landed, the hub assembled and every convoy ship back in orbit; stock for drops.</summary>
    private static Simulation Settled(out SurfaceStructure hub, GameData? data = null)
    {
        var sim = TouchedDown(out hub, data);
        TestData.NoStockTargets(sim);
        for (var i = 0; i < Sec(600) && (sim.Surface!.DropShips.Count > 0 || !sim.Surface.UplinkOnline(0, sim.Tick)); i++) sim.Step();
        Assert.Empty(sim.Surface!.DropShips);
        var stock = sim.Players[0].Stock;
        stock.Add(R("duranium"), Fix64.FromInt(2000));
        stock.Add(R("tritanium"), Fix64.FromInt(1000));
        stock.Add(R("deuterium"), Fix64.FromInt(100));
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static void StepUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), "never happened");
    }

    /// <summary>A tile a delivery can go to, a few hexes from the hub and off its field.</summary>
    private static int DropSpot(Simulation sim, SurfaceStructure hub) =>
        sim.Surface!.Map.Grid.Within(hub.Tile, 8).First(t => sim.Surface.Map.Grid.Distance(t, hub.Tile) >= 6 && sim.Surface.ShuttleProblem(sim, 0, t, "survey_rover") == null);

    // ---------------------------------------------------------------- data and the field

    [Fact]
    public void TheDataLoadsAndIsChecked()
    {
        Assert.Empty(Data.Validate());
        Assert.Equal(4, Rules.Pads);
        Assert.Equal(3, Rules.Distance);
        Assert.Equal(Fix64.FromInt(45), Rules.RefuelSeconds);
        Assert.Contains(TestData.Patched(("surfaces.json", "\"pads\": 4", "\"pads\": 9")).Validate(), p => p.Contains("pads must be"));
        Assert.Contains(TestData.Patched(("surfaces.json", "\"pads\": 4, \"distance\": 3", "\"pads\": 4, \"distance\": 1")).Validate(), p => p.Contains("distance must be"));
        Assert.Contains(TestData.Patched(("surfaces.json", "\"footprint_radius\": 8.5,\n    \"unload_seconds\"", "\"footprint_radius\": 11,\n    \"unload_seconds\"")).Validate(),
            p => p.Contains("landing_field: footprint_radius"));
        Assert.Contains(TestData.Patched(("surfaces.json", "\"hop_max_seconds\": 40", "\"hop_max_seconds\": 5")).Validate(), p => p.Contains("hop_max_seconds"));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(1701UL)]
    [InlineData(99999UL)]
    public void EveryCoreGetsAFieldOfPadsNearIt(ulong seed)
    {
        var sim = TouchedDown(out var hub, game: MatchSetup.CreatePlanetLanding(Data, seed));
        var s = sim.Surface!;
        var g = s.Map.Grid;
        Assert.InRange(hub.Field.Count, 1, Rules.Pads);
        Assert.Equal(Rules.Distance, g.Distance(hub.Tile, hub.Field[0]));
        Assert.Equal(hub.Field.Count, hub.Field.Distinct().Count());
        foreach (var pad in hub.Field)
        {
            Assert.InRange(g.Distance(hub.Tile, pad), Rules.Distance, Rules.Distance + 1);
            Assert.True(s.Map.Terrain(pad).Passable && s.Map.Terrain(pad).Buildable);
            Assert.True(s.Map.DepositResource[pad] < 0); // never on ore
            Assert.Null(s.StructureAt(pad));
            Assert.Equal(hub.Id, s.PadCoreAt(pad));
            Assert.True(s.IsLandingPad(pad));
            // One cluster: every pad after the first touches an earlier one.
            if (pad != hub.Field[0]) Assert.Contains(hub.Field.TakeWhile(p => p != pad), p => g.Distance(p, pad) == 1);
        }
        Assert.False(s.IsLandingPad(hub.Tile));
        Assert.Equal(0, s.PadCoreAt(-1));
        Assert.All(s.Structures.Where(x => x != hub), x => Assert.Empty(x.Field)); // only cores have one
    }

    [Fact]
    public void NothingIsBuiltOrDroppedOnAPad()
    {
        var sim = Settled(out var hub);
        var s = sim.Surface!;
        var pad = hub.Field[0];
        foreach (var t in s.Map.Grid.Within(hub.Tile, 5)) s.Players[0].Surveyed[t] = true;
        Assert.Equal(PlacementProblem.LandingField, s.CheckPlacement(sim, 0, "storage_container", pad));
        var refused = sim.RefusedCommands;
        Do(sim, new PlaceBuildingCommand(0, "storage_container", pad, 0));
        Assert.Equal(refused + 1, sim.RefusedCommands);
        Assert.Null(s.StructureAt(pad));
        Assert.Equal(PlanetSurface.LandingFieldProblem, s.ShuttleProblem(sim, 0, pad, "survey_rover"));
        Assert.Equal(PlanetSurface.LandingFieldProblem, sim.DebugProblem(new DebugPlaceBuildingCommand(0, 0, "storage_container", pad, 0)));
        Assert.Equal(PlanetSurface.LandingFieldProblem, sim.DebugProblem(new DebugRuinCommand(0, pad, true)));
        // A tile beside the field is as buildable as ever.
        var beside = s.Map.Grid.Within(hub.Tile, 2).First(t => t != hub.Tile && !s.IsLandingPad(t) && s.CheckPlacement(sim, 0, "storage_container", t) == PlacementProblem.None);
        Do(sim, new PlaceBuildingCommand(0, "storage_container", beside, 0));
        Assert.NotNull(s.StructureAt(beside));
    }

    [Fact]
    public void VehiclesDoNotParkOnAPad()
    {
        var sim = Settled(out var hub);
        var s = sim.Surface!;
        var pad = hub.Field[0];
        Assert.Equal(Rules.FootprintRadius, s.BlockRadius(pad)); // its middle is kept clear, ship or no ship
        var rover = s.Units.First(u => u.Owner == 0 && u.DefId == "survey_rover");
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, pad));
        StepUntil(sim, () => rover.Tile == pad && rover.Path.Count == 0, Sec(240));
        Assert.True((rover.Pos - s.CentreOf(pad)).Length() >= Rules.FootprintRadius, $"parked {(rover.Pos - s.CentreOf(pad)).Length()} from the pad's centre");
        // A crawler can't unfurl there either (there is never ore on a pad, and the pad itself refuses).
        var crawler = s.Units.First(u => u.Owner == 0 && u.DefId == "mining_crawler");
        Assert.NotNull(s.MineProblem(0, crawler.Id, pad));
    }

    // ---------------------------------------------------------------- the first landing's convoy

    [Fact]
    public void TheConvoyShipsUnloadHopToTheFieldRefuelAndClimb()
    {
        var sim = TouchedDown(out var hub);
        var s = sim.Surface!;
        var touch = s.Players[0].LandsAtTick;
        var deuterium = sim.Players[0].Stock[R("deuterium")];
        Assert.Equal(PlanetSurface.ConvoyShips, s.DropShips.Count);
        var landed = s.Units.Where(u => u.Owner == 0).OrderBy(u => u.Id).ToList();
        Assert.Equal(3, landed.Count);
        for (var k = 0; k < 3; k++)
        {
            var ship = s.FindDropShip(0, DropShipRole.Convoy, 0, k)!;
            Assert.Equal((DropShipState.Unloading, landed[k].Id, landed[k].Tile, -1), (ship.State, ship.UnitId, ship.Tile, ship.Pad));
            Assert.Equal(touch + Ticks(Rules.ConvoyUnloadSeconds + Rules.StaggerSeconds * k), ship.UntilTick);
            Assert.Same(ship, s.DropShipCarrying(landed[k].Id));
            Assert.True(ship.Landed);
        }
        Assert.Null(s.ShuttleShip(0)); // the delivery drop ship is still aboard
        Assert.Null(s.ShuttleWhere(0, sim.Tick));

        // Each hops to a pad of its own, one after the other, while the hub is still assembling.
        var first = s.FindDropShip(0, DropShipRole.Convoy, 0, 0)!;
        StepUntil(sim, () => first.State == DropShipState.Hopping, Sec(60));
        Assert.Equal(touch + Ticks(Rules.ConvoyUnloadSeconds), first.StateTick);
        Assert.Equal(first.StateTick + s.HopTicks(s.Map.Grid.Distance(first.Tile, first.Pad)), first.UntilTick);
        Assert.Equal((hub.Id, hub.Field[0]), (first.CoreId, first.Pad));
        Assert.Same(first, s.ShipOnPad(first.Pad));
        StepUntil(sim, () => s.DropShips.All(x => x.State >= DropShipState.Hopping), Sec(60));
        Assert.Equal(3, s.DropShips.Select(x => x.Pad).Distinct().Count());
        Assert.All(s.DropShips, x => Assert.Contains(x.Pad, hub.Field));

        // On the pad: refuelling for refuel_seconds (their own fuel: nothing from the colony), then the climb.
        StepUntil(sim, () => first.State == DropShipState.Refuelling, Sec(60));
        Assert.True(first.OnPad && first.HoldsPad);
        Assert.Equal(first.StateTick + s.RefuelTicks(0), first.UntilTick);
        Assert.Equal(Ticks(Rules.RefuelSeconds), s.RefuelTicks(0)); // class M: lift cost 1
        Assert.Contains("refuelling at the landing field", s.DropShipStatus(first, sim.Tick));
        StepUntil(sim, () => first.State == DropShipState.Climbing, Sec(60));
        Assert.Equal(first.StateTick + s.ClimbTicks(0), first.UntilTick);
        Assert.False(first.HoldsPad); // free again from lift-off
        Assert.Null(s.ShipOnPad(first.Pad));
        StepUntil(sim, () => s.DropShips.Count == 0, Sec(200));
        Assert.Equal(deuterium, sim.Players[0].Stock[R("deuterium")]);
    }

    [Fact]
    public void EveryVehicleOfTheFirstLandingComesDownInAShip()
    {
        // The Cochrane brings a construction rover: a fourth ship of the convoy carries it (it used to stand on the site
        // before any ship had set anything down).
        var cochrane = TouchedDown(out var hub, game: MatchSetup.CreateSystemGame(Data, 1701, "federation", null, "cochrane"));
        var s = cochrane.Surface!;
        Assert.Equal(4, s.DropShips.Count);
        var rover = s.Units.Single(u => u.DefId == "construction_rover");
        var fourth = s.FindDropShip(0, DropShipRole.Convoy, 0, 3)!;
        Assert.Equal(rover.Id, fourth.UnitId);
        Assert.Equal(rover.Tile, fourth.Tile);
        Assert.Equal(s.Units.Where(u => u.Owner == 0).Select(u => u.Id).OrderBy(i => i), s.DropShips.Select(x => x.UnitId).OrderBy(i => i));
        // Four ships, four pads (when the ground has room): each gets one; then all are gone.
        StepUntil(cochrane, () => s.DropShips.All(x => x.State >= DropShipState.Hopping), Sec(90));
        if (hub.Field.Count >= 4) Assert.Equal(4, s.DropShips.Select(x => x.Pad).Distinct().Count());
        StepUntil(cochrane, () => s.DropShips.Count == 0, Sec(600));

        // The Horizon has no start units: the usual three.
        var horizon = TouchedDown(out _, game: MatchSetup.CreateSystemGame(Data, 1701, "federation", null, "horizon"));
        Assert.Equal(3, horizon.Surface!.DropShips.Count);
        Assert.All(horizon.Surface.DropShips, x => Assert.NotEqual(0, x.UnitId));
    }

    // ---------------------------------------------------------------- deliveries

    [Fact]
    public void ADeliverysShipGoesFieldRefuelOrbitAndTheTurnaroundMatches()
    {
        var sim = Settled(out var hub);
        var s = sim.Surface!;
        var spot = DropSpot(sim, hub);
        var before = sim.Players[0].Stock[R("deuterium")];
        var unitDeuterium = Data.SurfaceUnit("survey_rover").Cost.Where(c => c.Resource == R("deuterium")).Aggregate(Fix64.Zero, (a, c) => a + c.Amount);
        var flight = s.DescentTicks(Data.Orbit!.ShuttleSeconds);
        var ordered = sim.Tick;
        Do(sim, new ShuttleCommand(0, spot, "survey_rover"));
        var arrive = ordered + flight;
        Assert.True(s.ShuttleAway(0, sim.Tick));
        Assert.StartsWith("on its way down", s.ShuttleWhere(0, sim.Tick));
        Assert.Equal(arrive + s.GroundStayTicks(0), s.Players[0].ShuttleBackTick);
        Assert.Equal(before - unitDeuterium, sim.Players[0].Stock[R("deuterium")]); // no fuel taken yet

        StepUntil(sim, () => s.ShuttleShip(0) != null, (int)flight + 2);
        var ship = s.ShuttleShip(0)!;
        var unit = s.Units[^1];
        Assert.Equal((DropShipRole.Delivery, DropShipState.Unloading, unit.Id, unit.DeliveryId), (ship.Role, ship.State, ship.UnitId, ship.DeliveryId));
        Assert.Equal(arrive + Ticks(Rules.UnloadSeconds), ship.UntilTick);
        Assert.Equal("unloading", s.ShuttleWhere(0, sim.Tick));

        StepUntil(sim, () => ship.State == DropShipState.Hopping, Sec(30));
        var hopStart = ship.StateTick;
        Assert.Equal(arrive + Ticks(Rules.UnloadSeconds), hopStart);
        var hop = s.HopTicks(s.Map.Grid.Distance(ship.Tile, ship.Pad));
        Assert.Equal(hub.Field[0], ship.Pad);
        Assert.StartsWith("hopping to the landing field", s.ShuttleWhere(0, sim.Tick));

        StepUntil(sim, () => ship.State == DropShipState.Refuelling, Sec(60));
        Assert.Equal(hopStart + hop, ship.StateTick);
        Assert.Equal(before - unitDeuterium - Fix64.FromInt(5), sim.Players[0].Stock[R("deuterium")]); // the trip's fuel, taken now
        Assert.StartsWith(PlanetSurface.ShuttleAwayProblem + ": refuelling at the landing field · 0:45", s.ShuttleProblem(sim, 0, spot, "survey_rover"));

        StepUntil(sim, () => ship.State == DropShipState.Climbing, Sec(60));
        Assert.Equal(hopStart + hop + s.RefuelTicks(0), ship.StateTick);
        Assert.StartsWith("climbing back to the ship", s.ShuttleWhere(0, sim.Tick));
        Assert.True(s.ShuttleAway(0, sim.Tick));

        StepUntil(sim, () => !s.ShuttleAway(0, sim.Tick), Sec(60));
        var back = hopStart + hop + s.RefuelTicks(0) + s.ClimbTicks(0);
        Assert.Equal(back + 1, sim.Tick); // the step at tick `back` brought it aboard
        Assert.Equal(back, s.Players[0].ShuttleBackTick);
        Assert.Equal(arrive + s.GroundStayTicks(0, s.Map.Grid.Distance(ship.Tile, ship.Pad)), back); // the turnaround, as announced
        Assert.Empty(s.DropShips);
        Assert.Null(s.ShuttleProblem(sim, 0, spot, "survey_rover"));
        Assert.Null(s.ShuttleWhere(0, sim.Tick));
    }

    [Fact]
    public void WithoutFuelTheShipWaitsOnItsPadUntilThereIsSome()
    {
        var sim = Settled(out var hub);
        var s = sim.Surface!;
        var stock = sim.Players[0].Stock;
        Do(sim, new ShuttleCommand(0, DropSpot(sim, hub), "survey_rover"));
        StepUntil(sim, () => s.ShuttleShip(0) is { State: DropShipState.Hopping }, Sec(60));
        var ship = s.ShuttleShip(0)!;
        stock.Add(R("deuterium"), -stock[R("deuterium")] + Fix64.FromInt(2)); // spent on something else meanwhile
        StepUntil(sim, () => ship.State == DropShipState.WaitingForFuel, Sec(60));
        var since = ship.StateTick;
        for (var i = 0; i < Sec(90); i++) sim.Step(); // far longer than a refuelling
        Assert.Equal(DropShipState.WaitingForFuel, ship.State);
        Assert.Equal(since, ship.StateTick);
        Assert.True(ship.OnPad);
        Assert.Same(ship, s.ShipOnPad(ship.Pad)); // it keeps its pad
        Assert.Equal(Fix64.FromInt(2), stock[R("deuterium")]);
        Assert.Contains("waiting for fuel (needs 5 deuterium)", s.ShuttleWhere(0, sim.Tick));
        Assert.True(s.Players[0].ShuttleBackTick > sim.Tick); // the estimate keeps moving out
        Assert.StartsWith(PlanetSurface.ShuttleAwayProblem, s.ShuttleProblem(sim, 0, hub.Field[0], "survey_rover"));

        stock.Add(R("deuterium"), Fix64.FromInt(10));
        sim.Step();
        Assert.Equal(DropShipState.Refuelling, ship.State);
        Assert.Equal(Fix64.FromInt(7), stock[R("deuterium")]);
        StepUntil(sim, () => !s.ShuttleAway(0, sim.Tick), Sec(120));
    }

    [Fact]
    public void WhenEveryPadIsTakenAShipWaitsWhereItUnloaded()
    {
        var data = TestData.Patched(("surfaces.json", "\"pads\": 4", "\"pads\": 1"));
        var sim = TouchedDown(out var hub, data);
        var s = sim.Surface!;
        Assert.Single(hub.Field);
        var ships = s.DropShips.ToList();
        var waited = new bool[ships.Count];
        var onPad = new List<int>(); // the order they got the pad in
        for (var i = 0; i < Sec(600) && s.DropShips.Count > 0; i++)
        {
            sim.Step();
            Assert.True(s.DropShips.Count(x => x.HoldsPad) <= 1, "two ships on one pad");
            for (var k = 0; k < ships.Count; k++)
            {
                if (ships[k].State == DropShipState.WaitingForPad)
                {
                    waited[k] = true;
                    Assert.Equal(-1, ships[k].Pad);
                    Assert.Equal("waiting for a free pad at the landing field", s.DropShipStatus(ships[k], sim.Tick));
                }
                if (ships[k].HoldsPad && !onPad.Contains(k)) onPad.Add(k);
            }
        }
        Assert.Empty(s.DropShips);
        Assert.Equal(new[] { false, true, true }, waited); // the first went straight in
        Assert.Equal(new[] { 0, 1, 2 }, onPad);            // oldest first
    }

    [Fact]
    public void WithNoFieldShipsClimbStraightBack()
    {
        var data = TestData.Patched(("surfaces.json", "\"pads\": 4", "\"pads\": 0"));
        Assert.Empty(data.Validate());
        var sim = TouchedDown(out var hub, data);
        var s = sim.Surface!;
        Assert.Empty(hub.Field);
        var first = s.FindDropShip(0, DropShipRole.Convoy, 0, 0)!;
        StepUntil(sim, () => first.State != DropShipState.Unloading, Sec(60));
        Assert.Equal((DropShipState.Climbing, -1), (first.State, first.Pad));
        StepUntil(sim, () => s.DropShips.Count == 0, Sec(120));
    }

    // ---------------------------------------------------------------- outposts

    [Fact]
    public void AnOutpostDropsShipsUseTheNewOutpostsField()
    {
        var sim = Settled(out _);
        var s = sim.Surface!;
        sim.Players[0].Stock.Add(R("crew"), Fix64.FromInt(100));
        var far = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.OutpostProblem(sim, 0, t) == null);
        var deuterium = sim.Players[0].Stock[R("deuterium")];
        Do(sim, new OutpostDropCommand(0, far));
        var delivery = s.Deliveries.Single();
        StepUntil(sim, () => s.Deliveries.Count == 0, Sec(Data.Orbit!.OutpostDescentSeconds) + 2);
        var core = s.StructureAt(far)!;
        Assert.NotEmpty(core.Field);
        Assert.Equal(3, s.DropShips.Count);
        Assert.All(s.DropShips, x => Assert.Equal((DropShipRole.OutpostDrop, delivery.Id, 0, false), (x.Role, x.DeliveryId, x.UnitId, x.Landed)));
        Assert.False(s.ShuttleAway(0, sim.Tick)); // these aren't the delivery drop ship
        StepUntil(sim, () => s.DropShips.All(x => x.State >= DropShipState.Hopping), Sec(30));
        foreach (var ship in s.DropShips.Where(x => x.CoreId == core.Id)) Assert.Contains(ship.Pad, core.Field);
        Assert.Equal(System.Math.Min(3, core.Field.Count), s.DropShips.Count(x => x.CoreId == core.Id)); // its own field first (it is nearest)
        StepUntil(sim, () => s.DropShips.Count == 0, Sec(200));
        Assert.True(sim.Players[0].Stock[R("deuterium")] >= deuterium - Fix64.One); // their own fuel (the hub's upkeep aside)
    }

    [Fact]
    public void AFieldGoesWithItsCoreAndItsShipsCarryOn()
    {
        var sim = TouchedDown(out var hub);
        var s = sim.Surface!;
        var pads = hub.Field.ToList();
        var first = s.FindDropShip(0, DropShipRole.Convoy, 0, 0)!;
        StepUntil(sim, () => first.State == DropShipState.Refuelling, Sec(120));
        Do(sim, new DebugRemoveStructureCommand(0, hub.Id));
        Assert.Null(s.StructureAt(hub.Tile));
        Assert.All(pads, p => Assert.False(s.IsLandingPad(p)));
        StepUntil(sim, () => s.DropShips.Count == 0, Sec(300)); // on the pad: refuels and climbs; waiting ones find no field and climb
    }

    // ---------------------------------------------------------------- determinism and the hash

    private static List<ulong> Play()
    {
        var sim = Settled(out var hub);
        var hashes = new List<ulong>();
        Do(sim, new ShuttleCommand(0, DropSpot(sim, hub), "survey_rover"));
        for (var i = 0; i < Sec(150); i++)
        {
            sim.Step();
            if (i % 15 == 0) hashes.Add(sim.StateHash());
        }
        Assert.Empty(sim.Surface!.DropShips);
        return hashes;
    }

    [Fact]
    public void DropShipsAreDeterministicAndHashed()
    {
        Assert.Equal(Play(), Play());

        var sim = TouchedDown(out var hub);
        var s = sim.Surface!;
        var ship = s.DropShips[0];
        void Changes(string property, object value)
        {
            var p = typeof(DropShip).GetProperty(property)!;
            var old = p.GetValue(ship);
            var before = sim.StateHash();
            p.SetValue(ship, value);
            Assert.True(before != sim.StateHash(), $"DropShip.{property} is not in the state hash");
            p.SetValue(ship, old);
            Assert.Equal(before, sim.StateHash());
        }
        Changes(nameof(DropShip.State), DropShipState.Refuelling);
        Changes(nameof(DropShip.StateTick), 12345L);
        Changes(nameof(DropShip.UntilTick), 12345L);
        Changes(nameof(DropShip.CoreId), 77);
        Changes(nameof(DropShip.Pad), hub.Field[0]);

        // A core's field is in the hash too.
        var field = typeof(SurfaceStructure).GetField("_field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var pads = (int[])field.GetValue(hub)!;
        var hash = sim.StateHash();
        field.SetValue(hub, Enumerable.Reverse(pads).ToArray());
        if (pads.Length > 1) Assert.NotEqual(hash, sim.StateHash());
        field.SetValue(hub, pads);
        Assert.Equal(hash, sim.StateHash());

        // Before any landing nothing of this is hashed (a game in orbit hashes as it always did): no ship, no field.
        var orbit = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Empty(orbit.Surface!.DropShips);
    }
}
