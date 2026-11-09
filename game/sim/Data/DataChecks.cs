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
        foreach (var seed in PlanetSeeds) problems.AddRange(SystemProblems(data, seed));
        problems.AddRange(CivilizationProblems(data));
        return problems;
    }

    /// <summary>Seeds the civilization odds are measured over (<see cref="CivilizationProblems"/>).</summary>
    public const int CivilizationRateSeeds = 200;

    /// <summary>Civilizations (data/civilizations/): on seeds 1/1701/99999, with every landable body as the landing world,
    /// no site sits on the landing world and no body has two; every living site has a town map with towns on it (default
    /// landing world); and over seeds 1..200 the share of systems with a living civilization is inside rules.json
    /// sites.expected_living_share (owner: about half while a match is one system).</summary>
    public static List<string> CivilizationProblems(GameData data)
    {
        var problems = new List<string>();
        if (data.Civilizations is not { } c || data.SystemGenerator == null) return problems;
        foreach (var seed in PlanetSeeds)
        {
            var where = $"Civilizations, system seed {seed}";
            Space.GeneratedSystem system;
            try
            {
                system = Space.SystemGenerator.Generate(data, seed);
            }
            catch (System.Exception)
            {
                continue; // SystemProblems reports it
            }
            var landable = system.Landable(data);
            if (landable.Count == 0) continue;
            foreach (var home in landable)
            {
                var sites = Space.CivGenerator.Roll(data, system.Bodies, seed, home.Id);
                if (sites.Any(x => x.BodyId == home.Id)) problems.Add($"{where}: a site sits on the landing world {home.Id}.");
                if (sites.Select(x => x.BodyId).Distinct().Count() != sites.Count) problems.Add($"{where}: two sites on one body (landing on {home.Id}).");
            }
            var defaultHome = Core.MatchSetup.HomeBody(data, system);
            foreach (var site in Space.CivGenerator.Roll(data, system.Bodies, seed, defaultHome.Id).Where(x => x.Living))
            {
                var body = system.Body(site.BodyId)!;
                var cls = data.IsLandableClass(body.ClassId) ? body.ClassId : c.Sites.TownMapFor(body.ClassId);
                if (cls == null || !data.IsLandableClass(cls))
                {
                    problems.Add($"{where}: the {site.Species} on {body.Id} ({body.ClassId}) have no town map.");
                    continue;
                }
                var map = Surface.SurfaceMap.Generate(data, data.Surface(cls), Space.SystemGenerator.SurfaceSeed(seed, body.Id), body.Id, system.Climate);
                if (Space.CivGenerator.Towns(data, site, map).Count == 0) problems.Add($"{where}: the {site.Species} on {body.Id} got no towns.");
            }
        }
        var living = 0;
        for (ulong seed = 1; seed <= CivilizationRateSeeds; seed++)
        {
            var system = Space.SystemGenerator.Generate(data, seed);
            if (system.Landable(data).Count == 0) continue;
            if (Space.CivGenerator.Roll(data, system.Bodies, seed, Core.MatchSetup.HomeBody(data, system).Id).Any(x => x.Living)) living++;
        }
        var share = Math.Fix64.Ratio(living, CivilizationRateSeeds);
        if (share < c.Sites.ExpectedLivingMin || share > c.Sites.ExpectedLivingMax)
            problems.Add($"Civilizations: {living} of {CivilizationRateSeeds} systems have a living civilization ({share}), outside sites.expected_living_share " +
                         $"[{c.Sites.ExpectedLivingMin}, {c.Sites.ExpectedLivingMax}].");
        return problems;
    }

    /// <summary>A star system generated on this seed: bodies, at least one landable, distinct ids and names, every body's
    /// climate entry resolving (template, star, summary) and moons sharing their planet's year.</summary>
    public static List<string> SystemProblems(GameData data, ulong seed)
    {
        var problems = new List<string>();
        var where = $"System seed {seed}";
        Space.GeneratedSystem system;
        try
        {
            system = Space.SystemGenerator.Generate(data, seed);
        }
        catch (System.Exception e)
        {
            problems.Add($"{where}: generation failed ({e.Message}).");
            return problems;
        }
        if (system.Bodies.Count == 0) problems.Add($"{where}: no bodies.");
        if (system.Landable(data).Count == 0) problems.Add($"{where}: no landable body.");
        if (system.Bodies.Select(b => b.Id).Distinct().Count() != system.Bodies.Count) problems.Add($"{where}: body ids repeat.");
        if (system.Bodies.Select(b => b.Name).Distinct().Count() != system.Bodies.Count) problems.Add($"{where}: body names repeat.");
        if (data.Climate?.Star(system.Star.Class) == null) problems.Add($"{where}: star class {system.Star.Class} has no climate entry.");
        foreach (var e in system.Entries)
        {
            if (data.Climate?.Template(e.Climate.ClimateId) == null) problems.Add($"{where}: {e.Id} ({e.Body.ClassId}) has no climate template.");
            else if (Surface.Climate.Summary(data, system.Climate, e.Id) == null) problems.Add($"{where}: {e.Id} has no climate summary.");
            if (e.Climate.Parent is { } parent && system.Climate.Body(parent) is { } pc
                && Math.Fix64.Abs(e.Climate.YearSeconds - pc.YearSeconds) > pc.YearSeconds / 50)
                problems.Add($"{where}: moon {e.Id}'s year doesn't match its planet's.");
        }
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
