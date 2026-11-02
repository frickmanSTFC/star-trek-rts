using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

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
