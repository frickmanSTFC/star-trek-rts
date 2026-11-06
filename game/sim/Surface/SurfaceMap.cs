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

/// <summary>Wildlife on a tile, from the lifeform scan.</summary>
public enum Wildlife
{
    None,

    /// <summary>Grazing herds: harmless.</summary>
    Herds,

    /// <summary>Predators: unsafe to land among.</summary>
    Predators,
}

/// <summary>How safe a tile is for a landing, from the landing scan.</summary>
public enum LandingSafety
{
    Safe,

    /// <summary>Allowed, but predators or a toxic zone are close by.</summary>
    Risky,

    /// <summary>Not a landing site: rough or wet ground, toxic, predators on it, or the natives' land.</summary>
    Unsafe,
}

/// <summary>What the orbital scan says about one resource in one region.</summary>
public readonly record struct RegionHint(int Resource, HintStrength Strength);

/// <summary>
/// The terrain, deposits and life of one planet surface. Generated once, deterministically, from a seed.
/// The map wraps east–west in its noise (so the orbital globe has no seam), though units can't cross the edge.
/// </summary>
public sealed class SurfaceMap
{
    public PlanetSurfaceDef Def { get; }

    /// <summary>The seed this surface was generated from (other seeded streams, like unit names, derive from it).</summary>
    public ulong Seed { get; private set; }

    /// <summary>
    /// The climate.json body this surface is (its day, seasons and weather: <see cref="Climate"/>), or null for the
    /// planet class's climate template on a stand-in orbit. Set at generation: the body passed to <see cref="Generate"/>,
    /// else <see cref="GameData.DefaultClimateBody"/> (the lowest player slot's home body of this class: mvp_duel's Tarsis).
    /// </summary>
    public string? ClimateBodyId { get; private set; }

    /// <summary>Climate's resolved numbers for this map and data (rebuilt when the test panel swaps the data).</summary>
    internal object? ClimateCache;

    /// <summary>Wildlife per tile (lifeform scan).</summary>
    public Wildlife[] WildlifeAt { get; }

    /// <summary>Plant biomass per tile, 0..1 (biologics scan).</summary>
    public Fix64[] Biomass { get; }

    /// <summary>Toxic or contaminated ground (biologics scan): no landing there.</summary>
    public bool[] Toxic { get; }

    /// <summary>The native settlement's tile, or -1 if this world has none.</summary>
    public int NativeTile { get; private set; } = -1;

    /// <summary>No landing or building within this many tiles of the settlement (Prime Directive).</summary>
    public int NativeRadius { get; private set; }

    /// <summary>Within the natives' land (only when there is a settlement).</summary>
    public bool NearNatives(int tile) => NativeTile >= 0 && Grid.Distance(tile, NativeTile) <= NativeRadius;

    /// <summary>
    /// Safe = a landing site with no hazards near; Risky = a landing site with predators or toxic ground within 2 tiles;
    /// Unsafe = not a landing site, toxic, predators on it, or the natives' land.
    /// </summary>
    public LandingSafety Safety(int tile)
    {
        if (!IsLandingSite(tile) || Toxic[tile] || WildlifeAt[tile] == Wildlife.Predators || NearNatives(tile)) return LandingSafety.Unsafe;
        foreach (var t in Grid.Within(tile, 2))
            if (Toxic[t] || WildlifeAt[t] == Wildlife.Predators) return LandingSafety.Risky;
        return LandingSafety.Safe;
    }
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
    public int MinMoveCost { get; private set; }

    private GameData _data;

    /// <summary>The test panel tuned the data: terrain stats (move costs, passable) now come from the new data. The map itself stays as generated.</summary>
    internal void ReplaceData(GameData data)
    {
        _data = data;
        ClimateCache = null;
        MinMoveCost = data.Terrain.Where(t => t.Passable).Select(t => t.MoveCost).DefaultIfEmpty(1).Min();
    }

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
        WildlifeAt = new Wildlife[Grid.Count];
        Biomass = new Fix64[Grid.Count];
        Toxic = new bool[Grid.Count];
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

    /// <param name="climateBody">The climate.json body this surface is (null = <see cref="GameData.DefaultClimateBody"/>).</param>
    public static SurfaceMap Generate(GameData data, PlanetSurfaceDef def, ulong seed, string? climateBody = null)
    {
        var map = new SurfaceMap(data, def) { Seed = seed, ClimateBodyId = climateBody ?? data.DefaultClimateBody(def.ClassId) };
        var grid = map.Grid;
        var elevationNoise = new ValueNoise(seed ^ 0xE1E7A7E5UL);
        var moistureNoise = new ValueNoise(seed ^ 0x3015E0DEUL);
        var feature = Fix64.FromInt(def.FeatureSize);
        // East–west wrap: a whole number of noise cells across the width, so column W lines up with column 0.
        var period = System.Math.Max(1, (grid.Width + def.FeatureSize / 2) / def.FeatureSize);
        var xScale = Fix64.FromInt(period) / Fix64.FromInt(grid.Width);

        var rawElevation = new Fix64[grid.Count];
        var rawMoisture = new Fix64[grid.Count];
        for (var t = 0; t < grid.Count; t++)
        {
            var p = grid.Centre(t);
            rawElevation[t] = elevationNoise.Fractal(p.X * xScale, p.Y / feature, 4, period);
            rawMoisture[t] = moistureNoise.Fractal(p.X * xScale, p.Y / feature, 3, period);
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
        if (def.Life != null) GenerateLife(def.Life, map, moisture, seed);
        return map;
    }

    /// <summary>Biomass from terrain and moisture; toxic patches; wildlife herds; maybe one native settlement. Own RNG stream.</summary>
    private static void GenerateLife(LifeRules life, SurfaceMap map, Fix64[] moisture, ulong seed)
    {
        var grid = map.Grid;
        var rng = new DeterministicRng(seed ^ 0x11FEF0E5UL);
        for (var t = 0; t < grid.Count; t++)
        {
            var id = map.Terrain(t).Id;
            var baseBiomass = Fix64.Zero;
            foreach (var (terrain, biomass) in life.Biomass)
                if (terrain == id) baseBiomass = biomass;
            map.Biomass[t] = Fix64.Min(Fix64.One, baseBiomass * (Fix64.Ratio(6, 10) + Fix64.Ratio(4, 10) * moisture[t]));
        }

        // Candidate centres for patches: tiles of the allowed terrain, outside the polar rows.
        List<int> Candidates(PatchRule rule) => Enumerable.Range(0, grid.Count)
            .Where(t => rule.Terrain.Contains(map.Terrain(t).Id) && grid.Row(t) >= map.Def.PolarRows && grid.Row(t) < grid.Height - map.Def.PolarRows).ToList();

        var toxicSpots = Candidates(life.Toxic);
        for (var i = 0; i < life.Toxic.Count && toxicSpots.Count > 0; i++)
        {
            var centre = toxicSpots[rng.NextInt(toxicSpots.Count)];
            var radius = rng.NextInt(life.Toxic.RadiusMin, life.Toxic.RadiusMax + 1);
            foreach (var t in grid.Within(centre, radius))
                if (map.Terrain(t).Passable) map.Toxic[t] = true;
        }

        var herdSpots = Candidates(life.Wildlife);
        for (var i = 0; i < life.Wildlife.Count && herdSpots.Count > 0; i++)
        {
            var centre = herdSpots[rng.NextInt(herdSpots.Count)];
            var radius = rng.NextInt(life.Wildlife.RadiusMin, life.Wildlife.RadiusMax + 1);
            var kind = rng.NextFix() < life.PredatorShare ? Wildlife.Predators : Wildlife.Herds;
            foreach (var t in grid.Within(centre, radius))
                if (map.Terrain(t).Passable && map.WildlifeAt[t] < kind) map.WildlifeAt[t] = kind;
        }

        map.NativeRadius = life.NativeRadius;
        if (rng.NextFix() < life.NativesChance)
        {
            // A settlement on good open land, well inside the map, not on a toxic patch.
            var homes = Enumerable.Range(0, grid.Count).Where(t => map.IsLandingSite(t) && !map.Toxic[t] &&
                grid.Row(t) >= life.NativeRadius + map.Def.PolarRows && grid.Row(t) < grid.Height - life.NativeRadius - map.Def.PolarRows).ToList();
            if (homes.Count > 0) map.NativeTile = homes[rng.NextInt(homes.Count)];
        }
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
        h.Add(Seed);
        h.Add(ClimateBodyId ?? "");
        h.Add(NativeTile);
        h.Add(NativeRadius);
        // Not hashed because they follow from the data: MinMoveCost, ClimateCache (Climate's numbers for this map + data).
        for (var t = 0; t < Grid.Count; t++)
        {
            h.Add(TerrainOf[t]);
            h.Add(Elevation[t]);
            h.Add(DepositResource[t]);
            h.Add(DepositAmount[t]);
            h.Add(DepositStart[t]);
            h.Add(DepositReopened[t]);
            h.Add((int)WildlifeAt[t]);
            h.Add(Toxic[t]);
            h.Add(Biomass[t]);
        }
    }
}
