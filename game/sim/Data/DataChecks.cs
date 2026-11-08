using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// The full data checks, shared by the tests, Promote (draft → base files) and the release packer:
/// <see cref="GameData.Validate"/> (which includes every layer patch fitting and layer conflicts), the playable-planet
/// checks on three seeds, the duel map's point symmetry and its twin bodies' climates. Empty = good.
/// </summary>
public static class DataChecks
{
    public static readonly ulong[] PlanetSeeds = { 1, 1701, 99999 };

    public static List<string> All(GameData data)
    {
        var problems = data.Validate().ToList();
        if (problems.Count > 0) return problems; // the deeper checks assume the data at least loads cleanly
        foreach (var seed in PlanetSeeds) problems.AddRange(PlanetProblems(data, seed));
        problems.AddRange(SymmetryProblems(data));
        return problems;
    }

    /// <summary>Every planet class with a surface (surfaces.json planets: class M = Tarsis, class L = Veyra), generated on this seed.</summary>
    public static List<string> PlanetProblems(GameData data, ulong seed)
    {
        var problems = new List<string>();
        foreach (var def in data.Surfaces) problems.AddRange(PlanetProblems(data, seed, def.ClassId));
        return problems;
    }

    /// <summary>A generated planet of this class: ocean share near its threshold (ranking makes it exact, minus the polar
    /// rows: from 10 points under to 1 over; class M 20–31%), at least 50 landing sites, the basic deposits, deposits on
    /// allowed terrain.</summary>
    public static List<string> PlanetProblems(GameData data, ulong seed, string planetClass)
    {
        var problems = new List<string>();
        var map = MatchSetup.CreatePlanetLanding(data, seed, planetClass: planetClass).Surface!.Map;
        var def = map.Def;
        var tiles = Enumerable.Range(0, map.Grid.Count).ToList();
        var where = $"Planet {def.ClassId} seed {seed}";
        var ocean = tiles.Count(t => map.Terrain(t).Id == "ocean");
        var (lo, hi) = OceanRange(def, map.Grid.Count);
        if (ocean < lo || ocean > hi)
            problems.Add($"{where}: {ocean * 100 / map.Grid.Count}% ocean (should be {lo * 100 / map.Grid.Count}–{hi * 100 / map.Grid.Count}%).");
        var sites = tiles.Count(map.IsLandingSite);
        if (sites < 50) problems.Add($"{where}: only {sites} landing sites (needs 50).");
        foreach (var basic in new[] { "duranium_ore", "tritanium_ore", "deuterium" })
            if (!tiles.Any(t => map.DepositResource[t] == data.Resource(basic).Index)) problems.Add($"{where}: no {basic} deposit.");
        foreach (var t in tiles.Where(t => map.DepositResource[t] >= 0))
        {
            var res = data.Resources[map.DepositResource[t]].Id;
            if (!def.Deposits.Any(r => r.ResourceId == res && r.Terrain.Contains(map.Terrain(t).Id)))
            {
                problems.Add($"{where}: {res} deposit on {map.Terrain(t).Id} at tile {t}, which its rule doesn't allow.");
                break;
            }
        }
        return problems;
    }

    /// <summary>Tiles of ocean a generated planet may have: its ocean threshold's share from 10 points under to 1 point over
    /// (polar rows take some). Class M, the home world, keeps its fixed 20–31% whatever its threshold says.</summary>
    public static (int Min, int Max) OceanRange(PlanetSurfaceDef def, int tiles)
    {
        if (def.ClassId == MatchSetup.MvpPlanetClass) return (tiles * 20 / 100, tiles * 31 / 100);
        var pct = (def.OceanBelow * 100).FloorToInt();
        return (tiles * System.Math.Max(0, pct - 10) / 100, tiles * (pct + 1) / 100);
    }

    /// <summary>The duel map is point-symmetric (every body has a twin at -position: same class, size, deposits, moon-ness) and twins share their climate.</summary>
    public static List<string> SymmetryProblems(GameData data)
    {
        var problems = new List<string>();
        var map = data.Map(MatchSetup.MvpMap);
        foreach (var b in map.Bodies)
        {
            var twin = map.Bodies.FirstOrDefault(o => o.Position == -b.Position);
            if (twin == null)
            {
                problems.Add($"Map {map.Id}: {b.Id} has no mirror body at {-b.Position}.");
                continue;
            }
            if (b.ClassId != twin.ClassId || b.Size != twin.Size || !b.Deposits.SequenceEqual(twin.Deposits) || (b.Parent == null) != (twin.Parent == null))
                problems.Add($"Map {map.Id}: {b.Id} and its mirror {twin.Id} differ (class, size, deposits or moon).");
            if (data.Climate?.Body(b.Id) is { } x && data.Climate.Body(twin.Id) is { } y
                && (x with { Id = "", Parent = null, Notes = "", Seasons = y.Seasons } != y with { Id = "", Parent = null, Notes = "" }
                    || (x.Parent == null) != (y.Parent == null) || x.Seasons.Count != y.Seasons.Count))
                problems.Add($"Climate: twin bodies {b.Id} and {twin.Id} of the duel map have different climates.");
        }
        return problems;
    }
}
