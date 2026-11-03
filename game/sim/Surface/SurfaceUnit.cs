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
    public Fix64 SpeedPerTick { get; }
    public int SurveyRadius { get; }

    public int Tile { get; internal set; }

    /// <summary>Tiles still to enter, next first. Empty when idle.</summary>
    public IReadOnlyList<int> Path => _path;

    /// <summary>Effort spent toward entering the next tile (compare with its move_cost).</summary>
    public Fix64 Progress { get; internal set; }

    internal readonly List<int> _path = new();

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
        h.Add(Progress);
        h.Add(_path.Count);
        foreach (var t in _path) h.Add(t);
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

    /// <summary>A refinery or foundry with nothing in stock to work on.</summary>
    NoInput,
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

    public StructureStatus Status { get; internal set; }

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
        h.Add((int)Status);
        h.Add(BaseId);
        h.Add(UpgradingTo ?? "");
        h.Add(UpgradeTicks);
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

    /// <summary>Tiles whose deposits this player knows exactly.</summary>
    public bool[] Surveyed { get; }

    internal SurfacePlayer(int tileCount) => Surveyed = new bool[tileCount];

    internal void AddToHash(ref StateHasher h)
    {
        h.Add((int)Phase);
        h.Add(LandingTile);
        h.Add(LandsAtTick);
        h.Add(DescentStartTick);
        foreach (var s in Surveyed) h.Add(s);
    }
}
