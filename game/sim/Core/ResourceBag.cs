using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

/// <summary>
/// Amounts of every resource, indexed by ResourceDef.Index (file order), so iteration is deterministic.
/// Fix64 lets income arrive in fractions per tick.
/// </summary>
public sealed class ResourceBag
{
    private readonly Fix64[] _amounts;

    public ResourceBag(int resourceCount) => _amounts = new Fix64[resourceCount];

    public ResourceBag(int resourceCount, IEnumerable<Data.ResourceAmount> initial) : this(resourceCount)
    {
        foreach (var a in initial) _amounts[a.Resource] += a.Amount;
    }

    public int Count => _amounts.Length;
    public Fix64 this[int resource] => _amounts[resource];

    public void Add(int resource, Fix64 amount) => _amounts[resource] += amount;

    public bool CanAfford(IEnumerable<Data.ResourceAmount> cost)
    {
        foreach (var c in cost)
            if (_amounts[c.Resource] < c.Amount) return false;
        return true;
    }

    /// <summary>Spends the cost if affordable; returns false and changes nothing otherwise.</summary>
    public bool TrySpend(IReadOnlyList<Data.ResourceAmount> cost)
    {
        if (!CanAfford(cost)) return false;
        foreach (var c in cost) _amounts[c.Resource] -= c.Amount;
        return true;
    }

    internal void AddToHash(ref StateHasher h)
    {
        foreach (var a in _amounts) h.Add(a);
    }
}
