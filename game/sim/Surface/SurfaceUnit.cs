using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>What a mining crawler is doing with its rig. Other units are always Mobile.</summary>
public enum UnitMode
{
    /// <summary>Folded up: can drive.</summary>
    Mobile,

    /// <summary>Unfurling on a deposit (ModeTicks counts up to the deploy time).</summary>
    Deploying,

    /// <summary>Unfurled and mining.</summary>
    Deployed,

    /// <summary>Folding back up; drives off along its path once done.</summary>
    Packing,
}

/// <summary>How a deployed crawler's mining is going.</summary>
public enum MiningStatus
{
    None,
    Mining,

    /// <summary>The hopper is full and storage has no room for another load.</summary>
    StorageFull,

    /// <summary>Nowhere to send loads: no storage building or refinery.</summary>
    NoDestination,

    /// <summary>The deposit ran out; the crawler packed up.</summary>
    Depleted,
}

/// <summary>A ground vehicle on the planet surface. Moves tile to tile along a path.</summary>
public sealed class SurfaceUnit
{
    public int Id { get; }
    public int Owner { get; }
    public string DefId { get; }
    /// <summary>From its data (speed, survey_radius); refreshed when the test panel tunes the data.</summary>
    public Fix64 SpeedPerTick { get; internal set; }

    public int SurveyRadius { get; internal set; }

    public int Tile { get; internal set; }

    /// <summary>Tiles still to enter, next first. Empty when idle.</summary>
    public IReadOnlyList<int> Path => _path;

    internal readonly List<int> _path = new();

    // Driving inside the hexes (Lanes.cs). Path[0] == Tile means it is in its last tile, driving to its parking spot.

    /// <summary>Where it is on the map plane in world units (X east, Y = the view's Z, south), the same coordinates as
    /// PlanetView (tile centre = HexGrid.Centre × <see cref="PlanetSurface.TileSize"/>). Always inside <see cref="Tile"/>.</summary>
    public FixVec2 Pos { get; internal set; }

    /// <summary>Pos at the start of the last tick: the view draws Lerp(PrevPos, Pos, alpha).</summary>
    public FixVec2 PrevPos { get; internal set; }

    /// <summary>The points it still drives through on its current tile: to the edge it leaves by (or its parking spot), bending
    /// round a building or a parked vehicle. Empty = it plans the next stretch on the next tick.</summary>
    public IReadOnlyList<FixVec2> Route => _route;

    internal readonly List<FixVec2> _route = new();

    /// <summary>Ticks it has waited for a vehicle in front of it; at lanes.wait_seconds it drives on regardless.</summary>
    public int WaitTicks { get; internal set; }

    /// <summary>Ticks left of driving on regardless of the vehicles in front (after waiting too long).</summary>
    public int PushTicks { get; internal set; }

    /// <summary>The vehicle it is waiting for (0 = none).</summary>
    public int BlockedBy { get; internal set; }

    /// <summary>Driving (or about to): it has somewhere to go.</summary>
    public bool Driving => _path.Count > 0;

    public UnitMode Mode { get; internal set; }

    /// <summary>Ticks of unfurling or packing done.</summary>
    public int ModeTicks { get; internal set; }

    /// <summary>Unfurl when the path ends (a mine order).</summary>
    public bool DeployOnArrival { get; internal set; }

    public MiningStatus MiningStatus { get; internal set; }

    /// <summary>Ore in the hopper, of HopperResource (-1 = empty).</summary>
    public Fix64 Hopper { get; internal set; }

    public int HopperResource { get; internal set; } = -1;

    /// <summary>Condition, 0..100 (100 = perfect). Breakdowns knock it down; repairs bring it back to 100.</summary>
    public Fix64 Integrity { get; internal set; } = Fix64.FromInt(100);

    /// <summary>Broken down: can't mine until repaired at a colony core, and drives slower.</summary>
    public bool Broken { get; internal set; }

    /// <summary>Index into the unit's breakdown severities of the last breakdown (-1 = none yet).</summary>
    public int Breakdown { get; internal set; } = -1;

    /// <summary>Tick of the last breakdown (-1 = never); the view plays the bang/smoke from this.</summary>
    public long BrokeAtTick { get; internal set; } = -1;

    /// <summary>Ticks spent on a repair pad so far (the first stretch is the crew walking out).</summary>
    public int RepairTicks { get; internal set; }

    /// <summary>On the pad but the player can't pay for the next repair step.</summary>
    public bool RepairWaiting { get; internal set; }

    // Rover damage and flat tyres (RoverDamage.cs). For a rover, Broken means disabled (0 integrity).

    /// <summary>Flat tyres, one bit per wheel (see <see cref="Wheels"/> for the order; 0 = none).</summary>
    public int FlatTyres { get; internal set; }

    /// <summary>Spare tyres on board (the data's tyres.spares when new; a repair pad restocks them).</summary>
    public int Spares { get; internal set; }

    /// <summary>Ticks of a field tyre change done so far (0 = not changing one).</summary>
    public int TyreChangeTicks { get; internal set; }

    /// <summary>Tick of the last flat (-1 = never) and the wheel that went (for the event feed and the view).</summary>
    public long FlatAtTick { get; internal set; } = -1;

    public int LastFlat { get; internal set; } = -1;

    /// <summary>Tick of the last hazard hit (-1 = never) and what it was: a hazard id from the data, or
    /// <see cref="PlanetSurface.WildlifeHazard"/>.</summary>
    public long HazardAtTick { get; internal set; } = -1;

    public string LastHazard { get; internal set; } = "";

    /// <summary>Integrity the last hazard knocked off.</summary>
    public int LastHazardDamage { get; internal set; }

    /// <summary>Tick it rolled out of a Vehicle Bay (-1 = came with the landing), and that bay's tile.</summary>
    public long BuiltAtTick { get; internal set; } = -1;

    public int BuiltAtTile { get; internal set; } = -1;

    /// <summary>The <see cref="Delivery"/> (its Id) that brought it down from orbit, 0 = landed with the colony or
    /// built here. The view uses it to drive the unit out of its drop ship.</summary>
    public int DeliveryId { get; internal init; }

    /// <summary>
    /// The unit's own name, painted on its hull (e.g. "KALTY", or "KALTY II" once the pool has gone round), and its hull
    /// number (e.g. "44"). Set once when the unit is created and never reused by its owner, even after it is gone.
    /// </summary>
    public string Name { get; internal init; } = "";

    public string Number { get; internal init; } = "";

    // Founding an outpost (construction rovers, Founding.cs).

    /// <summary>The tile it is going to found an outpost on (driving there, or unfolding on it), -1 = none. While it is
    /// Deploying with a FoundTile the founding cost has been paid.</summary>
    public int FoundTile { get; internal set; } = -1;

    /// <summary>Tick it last arrived at a site and couldn't found there (-1 = never), and why (for the event feed).</summary>
    public long FoundFailedTick { get; internal set; } = -1;

    public FoundProblem FoundFailure { get; internal set; }

    internal SurfaceUnit(int id, int owner, string defId, Fix64 speedPerTick, int surveyRadius, int tile)
    {
        Id = id;
        Owner = owner;
        DefId = defId;
        SpeedPerTick = speedPerTick;
        SurveyRadius = surveyRadius;
        Tile = tile;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(DefId);
        h.Add(Tile);
        h.Add(_path.Count);
        foreach (var t in _path) h.Add(t);
        h.Add(Pos);
        h.Add(PrevPos);
        h.Add(_route.Count);
        foreach (var p in _route) h.Add(p);
        h.Add(WaitTicks);
        h.Add(PushTicks);
        h.Add(BlockedBy);
        h.Add((int)Mode);
        h.Add(ModeTicks);
        h.Add(DeployOnArrival);
        h.Add((int)MiningStatus);
        h.Add(Hopper);
        h.Add(HopperResource);
        h.Add(Integrity);
        h.Add(Broken);
        h.Add(Breakdown);
        h.Add(BrokeAtTick);
        h.Add(RepairTicks);
        h.Add(RepairWaiting);
        h.Add(FlatTyres);
        h.Add(Spares);
        h.Add(TyreChangeTicks);
        h.Add(FlatAtTick);
        h.Add(LastFlat);
        h.Add(HazardAtTick);
        h.Add(LastHazard);
        h.Add(LastHazardDamage);
        h.Add(BuiltAtTick);
        h.Add(BuiltAtTile);
        h.Add(DeliveryId);
        h.Add(Name);
        h.Add(Number);
        h.Add(FoundTile);
        h.Add(FoundFailedTick);
        h.Add((int)FoundFailure);
    }
}

/// <summary>How something reaches the surface from the ship.</summary>
public enum DeliveryKind
{
    /// <summary>A Colony Hub dropped as a new outpost.</summary>
    Outpost,

    /// <summary>The ship's drop ship (the orbit "shuttle") bringing a unit down.</summary>
    Shuttle,

    /// <summary>A transporter beam: a team or supplies.</summary>
    Beam,
}

/// <summary>Something on its way down from orbit; it arrives at ArriveTick. Payload: a building or unit id, or "supplies" (then Cargo).</summary>
public sealed class Delivery
{
    public int Id { get; }
    public int Owner { get; }
    public DeliveryKind Kind { get; }
    public int Tile { get; }
    public string Payload { get; }
    public IReadOnlyList<Data.ResourceAmount> Cargo { get; }
    public long LaunchTick { get; }
    public long ArriveTick { get; }

    internal Delivery(int id, int owner, DeliveryKind kind, int tile, string payload, IReadOnlyList<Data.ResourceAmount> cargo, long launchTick, long arriveTick)
    {
        Id = id;
        Owner = owner;
        Kind = kind;
        Tile = tile;
        Payload = payload;
        Cargo = cargo;
        LaunchTick = launchTick;
        ArriveTick = arriveTick;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add((int)Kind);
        h.Add(Tile);
        h.Add(Payload);
        h.Add(Cargo.Count);
        foreach (var c in Cargo)
        {
            h.Add(c.Resource);
            h.Add(c.Amount);
        }
        h.Add(LaunchTick);
        h.Add(ArriveTick);
    }
}

/// <summary>A survey satellite over a band of rows centred on Row; it sweeps east every sweep_seconds, surveying what it passes.</summary>
public sealed class Satellite
{
    public int Id { get; }
    public int Owner { get; }
    public int Row { get; }
    public long LaunchTick { get; }

    /// <summary>Columns swept so far (keeps counting past the width as it goes round again).</summary>
    public int Swept { get; internal set; }

    internal Satellite(int id, int owner, int row, long launchTick)
    {
        Id = id;
        Owner = owner;
        Row = row;
        LaunchTick = launchTick;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(Row);
        h.Add(LaunchTick);
        h.Add(Swept);
    }
}

/// <summary>A load of ore flying from a crawler to a storage building; it lands in stock at ArriveTick.</summary>
public sealed class CargoPod
{
    public int Id { get; }
    public int Owner { get; }
    public int Resource { get; }
    public Fix64 Amount { get; }
    public int FromTile { get; }
    public int ToTile { get; }
    public long LaunchTick { get; }
    public long ArriveTick { get; }

    internal CargoPod(int id, int owner, int resource, Fix64 amount, int fromTile, int toTile, long launchTick, long arriveTick)
    {
        Id = id;
        Owner = owner;
        Resource = resource;
        Amount = amount;
        FromTile = fromTile;
        ToTile = toTile;
        LaunchTick = launchTick;
        ArriveTick = arriveTick;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(Resource);
        h.Add(Amount);
        h.Add(FromTile);
        h.Add(ToTile);
        h.Add(LaunchTick);
        h.Add(ArriveTick);
    }
}

public enum StructureState
{
    /// <summary>Paid for and placed, waiting for a free build queue.</summary>
    Queued,

    /// <summary>Under construction.</summary>
    Building,

    Complete,

    /// <summary>
    /// A colony core that has come down from orbit (the first landing's Colony Hub, or an outpost drop) and is still
    /// combining its modules and unfurling: it stands on its tile but does nothing (no output, power, storage, build range,
    /// build queues, repair pad, depot, crew or wear) until <see cref="SurfaceStructure.ReadyTick"/>, when it turns
    /// Complete. Last in the enum so the hashed values of the others stay put.
    /// </summary>
    Assembling,
}

public enum StructureStatus
{
    Working,

    /// <summary>Couldn't pay its upkeep this tick (e.g. a mine with no energy), so it produced nothing.</summary>
    NoUpkeep,

    /// <summary>A mine whose deposit is used up.</summary>
    Depleted,

    /// <summary>Its base uses more power than it supplies, so this power user runs slower.</summary>
    LowPower,

    /// <summary>A mine with nowhere to put what it digs: storage is full.</summary>
    StorageFull,

    /// <summary>A refinery or foundry with nothing in stock to work on, a Vehicle Bay with no orders, or a lab with no tech chosen.</summary>
    NoInput,

    /// <summary>Worn out and broken down: does nothing until maintained.</summary>
    Broken,
}

/// <summary>What a base's maintenance crew is doing.</summary>
public enum CrewPhase
{
    None,

    /// <summary>Walking from the core to the job.</summary>
    Out,

    /// <summary>Fixing the building.</summary>
    Working,

    /// <summary>Walking back to the core.</summary>
    Back,
}

/// <summary>A building on a surface tile.</summary>
public sealed class SurfaceStructure
{
    public int Id { get; }
    public int Owner { get; }
    /// <summary>Changes when a colony core finishes an upgrade.</summary>
    public string DefId { get; internal set; }

    public int Tile { get; }

    /// <summary>Structure id of the colony core this building belongs to (a core's own id for the core itself).</summary>
    public int BaseId { get; internal set; }

    /// <summary>The level this core is upgrading to, or null.</summary>
    public string? UpgradingTo { get; internal set; }

    /// <summary>Ticks of upgrade work done so far.</summary>
    public int UpgradeTicks { get; internal set; }

    /// <summary>Which way it faces, 0–5 in 60° steps (hex sides). Cosmetic for now; entrances and footprints later.</summary>
    public int Facing { get; }

    public StructureState State { get; internal set; }

    /// <summary>Ticks of construction done so far.</summary>
    public int BuildTicks { get; internal set; }

    /// <summary>For a core that came down from orbit: the tick it has finished assembling and is Complete (touchdown +
    /// landing.assemble_seconds; for the first landing this is <see cref="SurfacePlayer.UplinkTick"/>). 0 = it never assembled.</summary>
    public long ReadyTick { get; internal set; }

    /// <summary>Still assembling after coming down from orbit (see <see cref="StructureState.Assembling"/>).</summary>
    public bool Assembling => State == StructureState.Assembling;

    /// <summary>Unit orders at a Vehicle Bay, the one being assembled first (all already paid for).</summary>
    public IReadOnlyList<string> Production => _production;

    /// <summary>Work done on the first order, in ticks at full power.</summary>
    public Fix64 ProductionProgress { get; internal set; }

    internal readonly List<string> _production = new();

    /// <summary>Upgrade level for buildings with levels: 1 = Mk I (the base), 2 = Mk II, ...</summary>
    public int Level { get; internal set; } = 1;

    /// <summary>The level it's being upgraded to (0 = not upgrading to a level).</summary>
    public int UpgradingLevel { get; internal set; }

    /// <summary>Condition 0..100 for buildings that wear (100 = perfect).</summary>
    public Fix64 Condition { get; internal set; } = Fix64.FromInt(100);

    /// <summary>Broken down: does nothing until maintained back to 100.</summary>
    public bool Broken { get; internal set; }

    /// <summary>The player asked the base's crew to maintain it (and when, for the crew's order of work).</summary>
    public bool MaintenanceOrdered { get; internal set; }

    public long MaintenanceOrderTick { get; internal set; }

    /// <summary>Maintenance drones are fixing it (auto-maintained buildings below the threshold).</summary>
    public bool AutoRepairing { get; internal set; }

    /// <summary>Being fixed, but the player can't pay for the next step.</summary>
    public bool MaintenanceWaiting { get; internal set; }

    /// <summary>On a colony core: its maintenance crew's current job (structure id, 0 = none), phase and timing.</summary>
    public int CrewJob { get; internal set; }

    public CrewPhase CrewPhase { get; internal set; }

    public int CrewTicks { get; internal set; }

    /// <summary>How long the crew's walk to (and back from) the current job takes, in ticks.</summary>
    public int CrewWalkTicks { get; internal set; }

    public StructureStatus Status { get; internal set; }

    /// <summary>Test panel: it doesn't wear.</summary>
    public bool NoWear { get; internal set; }

    /// <summary>Test panel: multiplies its work speed (1 = normal).</summary>
    public Fix64 WorkSpeed { get; internal set; } = Fix64.One;

    /// <summary>Placed without paying (test panel): cancelling or salvaging it refunds nothing for the building itself.</summary>
    public bool Free { get; internal set; }

    /// <summary>For a colony core founded on the ground: the construction rover that was used up to found it (kept off the
    /// map; cancelling the core before it is finished puts it back). Null for every other structure.</summary>
    public SurfaceUnit? Founder { get; internal set; }

    /// <summary>Founded on the ground by a construction rover (built by exocomps instead of assembling from orbit).</summary>
    public bool Founded => Founder != null;

    internal SurfaceStructure(int id, int owner, string defId, int tile, int facing, StructureState state, int baseId)
    {
        BaseId = baseId == 0 ? id : baseId;
        Id = id;
        Owner = owner;
        DefId = defId;
        Tile = tile;
        Facing = facing;
        State = state;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(DefId);
        h.Add(Tile);
        h.Add(Facing);
        h.Add((int)State);
        h.Add(BuildTicks);
        h.Add(ReadyTick);
        h.Add((int)Status);
        h.Add(BaseId);
        h.Add(UpgradingTo ?? "");
        h.Add(UpgradeTicks);
        h.Add(_production.Count);
        foreach (var p in _production) h.Add(p);
        h.Add(ProductionProgress);
        h.Add(Level);
        h.Add(UpgradingLevel);
        h.Add(Condition);
        h.Add(Broken);
        h.Add(MaintenanceOrdered);
        h.Add(MaintenanceOrderTick);
        h.Add(AutoRepairing);
        h.Add(MaintenanceWaiting);
        h.Add(CrewJob);
        h.Add((int)CrewPhase);
        h.Add(CrewTicks);
        h.Add(CrewWalkTicks);
        h.Add(NoWear);
        h.Add(WorkSpeed);
        h.Add(Free);
        h.Add(Founder != null);
        Founder?.AddToHash(ref h);
    }
}

public enum LandingPhase
{
    /// <summary>Scanning from orbit, choosing a landing site.</summary>
    InOrbit,

    /// <summary>The colony pod is falling.</summary>
    Descending,

    /// <summary>The colony is down; rovers are out.</summary>
    Landed,
}

/// <summary>One player's view of the planet: landing progress and which tiles they've surveyed.</summary>
public sealed class SurfacePlayer
{
    public LandingPhase Phase { get; internal set; } = LandingPhase.InOrbit;
    public int LandingTile { get; internal set; } = -1;
    public long LandsAtTick { get; internal set; }
    public long DescentStartTick { get; internal set; }

    /// <summary>Tick the landed outpost finishes assembling (its modules combined and unfurled) and its uplink to the
    /// ship in orbit comes online: touchdown + landing.assemble_seconds. 0 = not landed yet.</summary>
    public long UplinkTick { get; internal set; }

    /// <summary>Tiles whose deposits this player knows exactly.</summary>
    public bool[] Surveyed { get; }

    /// <summary>Researched surface techs, by TechDef.Index.</summary>
    public bool[] Known { get; }

    /// <summary>Research points put into each tech so far (kept when switching).</summary>
    public Fix64[] ResearchProgress { get; }

    /// <summary>The tech the labs are working on (TechDef.Index), or -1.</summary>
    public int ActiveTech { get; internal set; } = -1;

    /// <summary>The ship's cargo hold, by resource index (supplies beam down from here).</summary>
    public Fix64[] Hold { get; }

    /// <summary>Transporter level from the faction and era (0 = none).</summary>
    public int TransporterLevel { get; internal set; }

    /// <summary>The shuttle is away until this tick (down and back up).</summary>
    public long ShuttleBackTick { get; internal set; }

    /// <summary>The transporter can beam again from this tick.</summary>
    public long TransporterReadyTick { get; internal set; }

    /// <summary>Every unit name this player has handed out, oldest first (kept after the unit is gone, so none repeats).</summary>
    public IReadOnlyList<string> UsedUnitNames => _usedUnitNames;

    /// <summary>Every hull number this player has handed out, oldest first.</summary>
    public IReadOnlyList<string> UsedUnitNumbers => _usedUnitNumbers;

    internal readonly List<string> _usedUnitNames = new();
    internal readonly List<string> _usedUnitNumbers = new();

    // Test-panel limits (DebugFlagCommand).

    /// <summary>Storage never fills.</summary>
    public bool NoStorageCap { get; internal set; }

    /// <summary>Every base's power supply covers its use.</summary>
    public bool UnlimitedPower { get; internal set; }

    /// <summary>Placing buildings costs nothing.</summary>
    public bool FreeBuilding { get; internal set; }

    /// <summary>Construction and upgrades finish at once.</summary>
    public bool InstantBuild { get; internal set; }

    internal SurfacePlayer(int tileCount, int techCount, int resourceCount)
    {
        Surveyed = new bool[tileCount];
        Known = new bool[techCount];
        ResearchProgress = new Fix64[techCount];
        Hold = new Fix64[resourceCount];
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add((int)Phase);
        h.Add(LandingTile);
        h.Add(LandsAtTick);
        h.Add(DescentStartTick);
        h.Add(UplinkTick);
        foreach (var s in Surveyed) h.Add(s);
        foreach (var k in Known) h.Add(k);
        foreach (var r in ResearchProgress) h.Add(r);
        h.Add(ActiveTech);
        foreach (var x in Hold) h.Add(x);
        h.Add(TransporterLevel);
        h.Add(ShuttleBackTick);
        h.Add(TransporterReadyTick);
        h.Add(_usedUnitNames.Count);
        foreach (var n in _usedUnitNames) h.Add(n);
        h.Add(_usedUnitNumbers.Count);
        foreach (var n in _usedUnitNumbers) h.Add(n);
        h.Add(NoStorageCap);
        h.Add(UnlimitedPower);
        h.Add(FreeBuilding);
        h.Add(InstantBuild);
    }
}
