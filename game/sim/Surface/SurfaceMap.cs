using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

public enum HintStrength
{
    Traces,
    Likely,
    Rich,
}

/// <summary>What the orbital scan says about one resource in one region.</summary>
public readonly record struct RegionHint(int Resource, HintStrength Strength);

/// <summary>
/// The terrain and deposits of one planet surface. Generated once, deterministically, from a seed.
/// Deposits shrink as they are mined (later); terrain never changes for now.
/// </summary>
public sealed class SurfaceMap
{
    public PlanetSurfaceDef Def { get; }
    public HexGrid Grid { get; }

    /// <summary>Terrain index (TerrainDef.Index) per tile.</summary>
    public int[] TerrainOf { get; }

    /// <summary>Elevation share per tile in [0, 1): 0.3 means 30% of the map is lower. Used for relief.</summary>
    public Fix64[] Elevation { get; }

    /// <summary>Deposit resource index per tile, or -1.</summary>
    public int[] DepositResource { get; }

    public Fix64[] DepositAmount { get; }

    /// <summary>How big each deposit was when the planet formed (what a deep-core mine reopens a share of).</summary>
    public Fix64[] DepositStart { get; }

    /// <summary>Deposits a deep-core mine has already reopened (each can be reopened once).</summary>
    public bool[] DepositReopened { get; }

    /// <summary>A deposit that was there and is now used up.</summary>
    public bool IsDepleted(int tile) => DepositResource[tile] >= 0 && DepositAmount[tile] <= Fix64.Zero;

    /// <summary>Cheapest move_cost of any passable terrain (for the pathfinding heuristic).</summary>
    public int MinMoveCost { get; }

    private readonly GameData _data;

    private SurfaceMap(GameData data, PlanetSurfaceDef def)
    {
        _data = data;
        Def = def;
        Grid = new HexGrid(def.Width, def.Height);
        TerrainOf = new int[Grid.Count];
        Elevation = new Fix64[Grid.Count];
        DepositResource = Enumerable.Repeat(-1, Grid.Count).ToArray();
        DepositAmount = new Fix64[Grid.Count];
        DepositStart = new Fix64[Grid.Count];
        DepositReopened = new bool[Grid.Count];
        MinMoveCost = data.Terrain.Where(t => t.Passable).Select(t => t.MoveCost).DefaultIfEmpty(1).Min();
    }

    public TerrainDef Terrain(int tile) => _data.Terrain[TerrainOf[tile]];

    public bool IsWater(int tile) => !Terrain(tile).Passable && Terrain(tile).Height == 0;

    public bool TouchesWater(int tile)
    {
        foreach (var n in Grid.Neighbours(tile))
            if (IsWater(n)) return true;
        return false;
    }

    /// <summary>Region (for orbital hints) a tile belongs to.</summary>
    public int Region(int tile)
    {
        var rc = Grid.Column(tile) * Def.RegionColumns / Grid.Width;
        var rr = Grid.Row(tile) * Def.RegionRows / Grid.Height;
        return rr * Def.RegionColumns + rc;
    }

    public int RegionCount => Def.RegionColumns * Def.RegionRows;

    /// <summary>Orbital scan of a region: resources present, strongest first, without exact positions or amounts.</summary>
    public IReadOnlyList<RegionHint> Hints(int region)
    {
        var totals = new Fix64[_data.Resources.Count];
        for (var t = 0; t < Grid.Count; t++)
            if (DepositResource[t] >= 0 && Region(t) == region) totals[DepositResource[t]] += DepositAmount[t];

        var hints = new List<(RegionHint hint, Fix64 total)>();
        for (var r = 0; r < totals.Length; r++)
        {
            if (totals[r] <= Fix64.Zero) continue;
            var strength = totals[r] < Fix64.FromInt(_data.Hints.Likely) ? HintStrength.Traces
                : totals[r] < Fix64.FromInt(_data.Hints.Rich) ? HintStrength.Likely
                : HintStrength.Rich;
            hints.Add((new RegionHint(r, strength), totals[r]));
        }
        return hints.OrderByDescending(h => h.total).ThenBy(h => h.hint.Resource).Select(h => h.hint).ToList();
    }

    /// <summary>
    /// A colony pod can land on a tile of a landing terrain whose every neighbour is passable land
    /// (so not on the map edge, by water or under mountains).
    /// </summary>
    public bool IsLandingSite(int tile)
    {
        if (!_data.Landing.Terrain.Contains(Terrain(tile).Id)) return false;
        if (Grid.NeighbourCount(tile) < 6) return false;
        foreach (var n in Grid.Neighbours(tile))
            if (!Terrain(n).Passable) return false;
        return true;
    }

    // ------------------------------------------------------------------ generation

    public static SurfaceMap Generate(GameData data, PlanetSurfaceDef def, ulong seed)
    {
        var map = new SurfaceMap(data, def);
        var grid = map.Grid;
        var elevationNoise = new ValueNoise(seed ^ 0xE1E7A7E5UL);
        var moistureNoise = new ValueNoise(seed ^ 0x3015E0DEUL);
        var feature = Fix64.FromInt(def.FeatureSize);

        var rawElevation = new Fix64[grid.Count];
        var rawMoisture = new Fix64[grid.Count];
        for (var t = 0; t < grid.Count; t++)
        {
            var p = grid.Centre(t);
            rawElevation[t] = elevationNoise.Fractal(p.X / feature, p.Y / feature, 4);
            rawMoisture[t] = moistureNoise.Fractal(p.X / feature, p.Y / feature, 3);
        }

        // Rank so thresholds are shares of the map, whatever the noise distribution looks like.
        var elevation = Rank(rawElevation);
        var moisture = Rank(rawMoisture);
        Array.Copy(elevation, map.Elevation, grid.Count);

        int T(string id) => data.TerrainType(id).Index;
        for (var t = 0; t < grid.Count; t++)
        {
            var row = grid.Row(t);
            var e = elevation[t];
            int terrain;
            if (e < def.OceanBelow) terrain = T("ocean");
            else if (e < def.ShallowsBelow) terrain = T("shallows");
            else if (e >= def.MountainsFrom) terrain = T("mountains");
            else if (e >= def.HillsFrom) terrain = T("hills");
            else terrain = T(def.Lowland.First(b => moisture[t] < b.Below).TerrainId);

            if (row < def.PolarRows || row >= grid.Height - def.PolarRows) terrain = T(def.PolarTerrain);
            map.TerrainOf[t] = terrain;
        }

        // Deposits: tiles in index order, rules in file order, at most one deposit per tile.
        var rng = new DeterministicRng(seed ^ 0xDE9051751UL);
        for (var t = 0; t < grid.Count; t++)
        {
            var terrainId = map.Terrain(t).Id;
            foreach (var rule in def.Deposits)
            {
                if (!rule.Terrain.Contains(terrainId)) continue;
                if (rule.Coastal && !map.TouchesWater(t)) continue;
                if (rng.NextFix() >= rule.Chance) continue;
                map.DepositResource[t] = data.Resource(rule.ResourceId).Index;
                map.DepositAmount[t] = Fix64.FromInt(rng.NextInt(rule.Min, rule.Max + 1));
                map.DepositStart[t] = map.DepositAmount[t];
                break;
            }
        }
        return map;
    }

    /// <summary>Replaces each value with its rank share in [0, 1); ties broken by tile index.</summary>
    private static Fix64[] Rank(Fix64[] values)
    {
        var order = Enumerable.Range(0, values.Length).ToArray();
        Array.Sort(order, (a, b) =>
        {
            var c = values[a].CompareTo(values[b]);
            return c != 0 ? c : a.CompareTo(b);
        });
        var ranked = new Fix64[values.Length];
        for (var i = 0; i < order.Length; i++) ranked[order[i]] = Fix64.Ratio(i, values.Length);
        return ranked;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Grid.Count);
        for (var t = 0; t < Grid.Count; t++)
        {
            h.Add(TerrainOf[t]);
            h.Add(DepositResource[t]);
            h.Add(DepositAmount[t]);
            h.Add(DepositReopened[t]);
        }
    }
}
