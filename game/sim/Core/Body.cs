using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

/// <summary>A planet, moon, asteroid field or other body in the star system. Index = position in Simulation.Bodies.</summary>
public sealed class Body
{
    public int Index { get; }
    public string Id { get; }
    public string Name { get; }
    public string ClassId { get; }
    public int Size { get; }
    public FixVec2 Position { get; }

    /// <summary>Index of the body this one orbits, or -1.</summary>
    public int ParentIndex { get; }

    /// <summary>What's left to mine.</summary>
    public ResourceBag Deposits { get; }

    /// <summary>Id of the colony on this body, or 0 for none.</summary>
    public int ColonyId { get; internal set; }

    internal Body(int index, string id, string name, string classId, int size, FixVec2 position, int parentIndex, ResourceBag deposits)
    {
        Index = index;
        Id = id;
        Name = name;
        ClassId = classId;
        Size = size;
        Position = position;
        ParentIndex = parentIndex;
        Deposits = deposits;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Index);
        h.Add(ColonyId);
        Deposits.AddToHash(ref h);
    }
}
