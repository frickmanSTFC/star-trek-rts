using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

public sealed class Building
{
    public int Id { get; }
    public string DefId { get; }

    internal Building(int id, string defId)
    {
        Id = id;
        DefId = defId;
    }
}

/// <summary>A player's settlement on a body. Step 1: just the Colony Hub; construction comes next.</summary>
public sealed class Colony
{
    public int Id { get; }
    public int Owner { get; }
    public int BodyIndex { get; }
    public IReadOnlyList<Building> Buildings => _buildings;

    private readonly List<Building> _buildings = new();

    internal Colony(int id, int owner, int bodyIndex)
    {
        Id = id;
        Owner = owner;
        BodyIndex = bodyIndex;
    }

    internal void AddBuilding(Building b) => _buildings.Add(b);

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(BodyIndex);
        h.Add(_buildings.Count);
        foreach (var b in _buildings)
        {
            h.Add(b.Id);
            h.Add(b.DefId);
        }
    }
}
