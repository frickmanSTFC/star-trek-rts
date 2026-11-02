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

/// <summary>A building on a surface tile.</summary>
public sealed class SurfaceStructure
{
    public int Id { get; }
    public int Owner { get; }
    public string DefId { get; }
    public int Tile { get; }

    internal SurfaceStructure(int id, int owner, string defId, int tile)
    {
        Id = id;
        Owner = owner;
        DefId = defId;
        Tile = tile;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(DefId);
        h.Add(Tile);
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
