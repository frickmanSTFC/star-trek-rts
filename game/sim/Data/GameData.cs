using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

public sealed record FactionDef(string Id, string Name, bool Playable);

/// <summary>Hull stats from ships.json. Speed is in map units per second (placeholder scale).</summary>
public sealed record HullDef(string Id, int Size, int Hp, Fix64 Speed, int Crew);

/// <summary>A faction's ship class: a named template on a hull. Key is "faction.Name".</summary>
public sealed record ShipClassDef(string Faction, string Name, string HullId, string Era, string Source)
{
    public string Key => $"{Faction}.{Name}";
}

/// <summary>
/// Read-only game data loaded from the design repo's data/*.json files (the same files the
/// design pages use). Lists keep file order, so iteration is deterministic.
/// Numbers are parsed from their raw text into Fix64, never through double.
/// </summary>
public sealed class GameData
{
    public IReadOnlyList<FactionDef> Factions { get; }
    public IReadOnlyList<HullDef> Hulls { get; }
    public IReadOnlyList<ShipClassDef> ShipClasses { get; }
    public IReadOnlyList<ResourceDef> Resources { get; }
    public IReadOnlyList<PlanetClassDef> PlanetClasses { get; }
    public IReadOnlyList<BuildingDef> Buildings { get; }
    public IReadOnlyList<MapDef> Maps { get; }
    public IReadOnlyList<TerrainDef> Terrain { get; }
    public IReadOnlyList<PlanetSurfaceDef> Surfaces { get; }
    public IReadOnlyList<SurfaceUnitDef> SurfaceUnits { get; }
    public HintRules Hints { get; }
    public LandingRules Landing { get; }

    /// <summary>Ground vehicle ids and names from vehicles.json.</summary>
    public IReadOnlyDictionary<string, string> GroundVehicleNames { get; }

    /// <summary>Resources shown on the HUD (tier "flow"), in file order.</summary>
    public IReadOnlyList<ResourceDef> FlowResources { get; }

    private readonly Dictionary<string, FactionDef> _factions;
    private readonly Dictionary<string, HullDef> _hulls;
    private readonly Dictionary<string, ShipClassDef> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceDef> _resources;
    private readonly Dictionary<string, PlanetClassDef> _planetClasses;
    private readonly Dictionary<string, BuildingDef> _buildings;
    private readonly Dictionary<string, MapDef> _maps;
    private readonly Dictionary<string, TerrainDef> _terrain;
    private readonly Dictionary<string, PlanetSurfaceDef> _surfaces;
    private readonly Dictionary<string, SurfaceUnitDef> _surfaceUnits;

    /// <summary>Problems found while reading (unknown resource ids, etc.). Reported by Validate().</summary>
    private readonly List<string> _loadProblems;

    private GameData(
        List<FactionDef> factions, List<HullDef> hulls, List<ShipClassDef> classes, List<ResourceDef> resources,
        List<PlanetClassDef> planetClasses, List<BuildingDef> buildings, List<MapDef> maps,
        List<TerrainDef> terrain, List<PlanetSurfaceDef> surfaces, List<SurfaceUnitDef> surfaceUnits,
        HintRules hints, LandingRules landing, Dictionary<string, string> groundVehicles, List<string> loadProblems)
    {
        Terrain = terrain;
        Surfaces = surfaces;
        SurfaceUnits = surfaceUnits;
        Hints = hints;
        Landing = landing;
        GroundVehicleNames = groundVehicles;
        Factions = factions;
        Hulls = hulls;
        ShipClasses = classes;
        Resources = resources;
        PlanetClasses = planetClasses;
        Buildings = buildings;
        Maps = maps;
        FlowResources = resources.Where(r => r.Tier == "flow").ToList();
        _loadProblems = loadProblems;

        _factions = ToLookup(factions, f => f.Id, "faction");
        _hulls = ToLookup(hulls, h => h.Id, "hull");
        foreach (var c in classes) _classes.TryAdd(c.Key, c);
        _resources = ToLookup(resources, r => r.Id, "resource");
        _planetClasses = ToLookup(planetClasses, p => p.Id, "planet class");
        _buildings = ToLookup(buildings, b => b.Id, "building");
        _maps = ToLookup(maps, m => m.Id, "map");
        _terrain = ToLookup(terrain, t => t.Id, "terrain");
        _surfaces = ToLookup(surfaces, p => p.ClassId, "surface planet class");
        _surfaceUnits = ToLookup(surfaceUnits, u => u.Id, "surface unit");
    }

    private Dictionary<string, T> ToLookup<T>(IEnumerable<T> items, Func<T, string> key, string what)
    {
        var d = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
            if (!d.TryAdd(key(item), item)) _loadProblems.Add($"Duplicate {what} id '{key(item)}'.");
        return d;
    }

    public FactionDef Faction(string id) => Get(_factions, id, "faction");
    public HullDef Hull(string id) => Get(_hulls, id, "hull");
    public ShipClassDef ShipClass(string key) => Get(_classes, key, "ship class");
    public ResourceDef Resource(string id) => Get(_resources, id, "resource");
    public PlanetClassDef PlanetClass(string id) => Get(_planetClasses, id, "planet class");
    public BuildingDef Building(string id) => Get(_buildings, id, "building");
    public MapDef Map(string id) => Get(_maps, id, "map");
    public bool HasShipClass(string key) => _classes.ContainsKey(key);
    public TerrainDef TerrainType(string id) => Get(_terrain, id, "terrain");
    public PlanetSurfaceDef Surface(string planetClassId) => Get(_surfaces, planetClassId, "surface planet class");
    public SurfaceUnitDef SurfaceUnit(string id) => Get(_surfaceUnits, id, "surface unit");

    private static T Get<T>(Dictionary<string, T> d, string id, string what) =>
        d.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"Unknown {what} '{id}'.");

    public static GameData Load(string dataDirectory)
    {
        JsonElement Read(string file)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, file)));
            return doc.RootElement.Clone();
        }

        var problems = new List<string>();
        var factionsRoot = Read("factions.json");
        var shipsRoot = Read("ships.json");
        var resourcesRoot = Read("resources.json");
        var planetsRoot = Read("planet_classes.json");
        var buildingsRoot = Read("buildings.json");
        var mapsRoot = Read("maps.json");
        var surfacesRoot = Read("surfaces.json");
        var vehiclesRoot = Read("vehicles.json");

        var factions = factionsRoot.GetProperty("factions").EnumerateArray()
            .Select(f => new FactionDef(Str(f, "id"), Str(f, "name"), f.GetProperty("playable").ValueKind == JsonValueKind.True))
            .ToList();

        var hulls = shipsRoot.GetProperty("hulls").EnumerateArray()
            .Select(h => new HullDef(Str(h, "id"), h.GetProperty("size").GetInt32(), h.GetProperty("hp").GetInt32(),
                Num(h.GetProperty("speed")), h.GetProperty("crew").GetInt32()))
            .ToList();

        var classes = shipsRoot.GetProperty("classes").EnumerateArray()
            .Select(c => new ShipClassDef(Str(c, "faction"), Str(c, "name"), Str(c, "hull"), Str(c, "era"), Str(c, "source")))
            .ToList();

        var resources = resourcesRoot.GetProperty("resources").EnumerateArray()
            .Select((r, i) => new ResourceDef(i, Str(r, "id"), Str(r, "name"), Str(r, "tier")))
            .ToList();
        var resourceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in resources) resourceIndex.TryAdd(r.Id, r.Index);

        // Resource maps ({"duranium": 100, ...}) become amounts sorted by resource index.
        List<ResourceAmount> Amounts(JsonElement parent, string property, string owner)
        {
            var list = new List<ResourceAmount>();
            if (!parent.TryGetProperty(property, out var obj) || obj.ValueKind != JsonValueKind.Object) return list;
            foreach (var p in obj.EnumerateObject())
            {
                if (resourceIndex.TryGetValue(p.Name, out var idx)) list.Add(new ResourceAmount(idx, Num(p.Value)));
                else problems.Add($"{owner}: unknown resource '{p.Name}' in {property}.");
            }
            list.Sort((a, b) => a.Resource.CompareTo(b.Resource));
            return list;
        }

        var planetClasses = planetsRoot.GetProperty("classes").EnumerateArray()
            .Select(p => new PlanetClassDef(Str(p, "id"), Str(p, "name"), Num(p.GetProperty("habitability")), Str(p, "colony"),
                p.GetProperty("yields").EnumerateArray().Select(y => y.GetString()!).ToList()))
            .ToList();

        var buildings = buildingsRoot.GetProperty("buildings").EnumerateArray()
            .Select(b =>
            {
                var id = Str(b, "id");
                return new BuildingDef(id, Str(b, "name"), Str(b, "layer"), Str(b, "category"),
                    Amounts(b, "cost", $"Building {id}"), Amounts(b, "output_per_min", $"Building {id}"));
            })
            .ToList();

        var maps = mapsRoot.GetProperty("maps").EnumerateArray()
            .Select(m =>
            {
                var id = Str(m, "id");
                var star = m.GetProperty("star");
                var bodies = m.GetProperty("bodies").EnumerateArray().Select(b =>
                {
                    var pos = b.GetProperty("pos");
                    var parent = b.GetProperty("parent");
                    var bodyId = Str(b, "id");
                    return new BodyDef(bodyId, Str(b, "name"), Str(b, "class"), b.GetProperty("size").GetInt32(),
                        new FixVec2(Num(pos[0]), Num(pos[1])),
                        parent.ValueKind == JsonValueKind.Null ? null : parent.GetString(),
                        Amounts(b, "deposits", $"Map {id} body {bodyId}"));
                }).ToList();
                var starts = m.GetProperty("starts").EnumerateArray()
                    .Select(s => new StartDef(s.GetProperty("slot").GetInt32(), Str(s, "home")))
                    .ToList();
                return new MapDef(id, Str(m, "name"), Str(m, "era"), m.GetProperty("players").GetInt32(),
                    new StarDef(Str(star, "name"), Str(star, "class"), Num(star.GetProperty("radius"))),
                    Amounts(m, "start_stock", $"Map {id}"), starts, bodies);
            })
            .ToList();

        var terrain = surfacesRoot.GetProperty("terrain").EnumerateArray()
            .Select((t, i) => new TerrainDef(i, Str(t, "id"), Str(t, "name"), Str(t, "color"),
                t.GetProperty("passable").GetBoolean(), t.GetProperty("buildable").GetBoolean(),
                t.GetProperty("move_cost").GetInt32(), t.GetProperty("height").GetInt32()))
            .ToList();

        var surfaces = surfacesRoot.GetProperty("planets").EnumerateArray()
            .Select(p =>
            {
                var elev = p.GetProperty("elevation");
                var regions = p.GetProperty("regions");
                return new PlanetSurfaceDef(Str(p, "class"), p.GetProperty("width").GetInt32(), p.GetProperty("height").GetInt32(),
                    p.GetProperty("feature_size").GetInt32(),
                    Num(elev.GetProperty("ocean")), Num(elev.GetProperty("shallows")),
                    Num(elev.GetProperty("hills")), Num(elev.GetProperty("mountains")),
                    p.GetProperty("lowland").EnumerateArray().Select(b => new LowlandBand(Num(b.GetProperty("below")), Str(b, "terrain"))).ToList(),
                    p.GetProperty("polar_rows").GetInt32(), Str(p, "polar_terrain"),
                    regions[0].GetInt32(), regions[1].GetInt32(),
                    p.GetProperty("deposits").EnumerateArray().Select(d =>
                    {
                        var amount = d.GetProperty("amount");
                        return new DepositRule(Str(d, "resource"),
                            d.GetProperty("terrain").EnumerateArray().Select(t => t.GetString()!).ToList(),
                            d.TryGetProperty("coastal", out var coastal) && coastal.GetBoolean(),
                            Num(d.GetProperty("chance")), amount[0].GetInt32(), amount[1].GetInt32());
                    }).ToList());
            })
            .ToList();

        var hintsEl = surfacesRoot.GetProperty("hints");
        var hints = new HintRules(hintsEl.GetProperty("likely").GetInt32(), hintsEl.GetProperty("rich").GetInt32());

        var landingEl = surfacesRoot.GetProperty("landing");
        var landing = new LandingRules(
            landingEl.GetProperty("terrain").EnumerateArray().Select(t => t.GetString()!).ToList(),
            landingEl.GetProperty("descent_seconds").GetInt32(),
            Amounts(landingEl, "start_stock", "Landing"),
            landingEl.GetProperty("reveal_radius").GetInt32(),
            Str(landingEl, "hub"),
            landingEl.GetProperty("units").EnumerateArray().Select(u => u.GetString()!).ToList());

        var surfaceUnits = surfacesRoot.GetProperty("units").EnumerateArray()
            .Select(u => new SurfaceUnitDef(Str(u, "id"), Num(u.GetProperty("speed")), u.GetProperty("survey_radius").GetInt32()))
            .ToList();

        var groundVehicles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in vehiclesRoot.GetProperty("ground_vehicles").EnumerateArray())
            groundVehicles.TryAdd(Str(v, "id"), Str(v, "name"));

        return new GameData(factions, hulls, classes, resources, planetClasses, buildings, maps,
            terrain, surfaces, surfaceUnits, hints, landing, groundVehicles, problems);
    }

    private static string Str(JsonElement e, string property) => e.GetProperty(property).GetString()!;
    private static Fix64 Num(JsonElement e) => Fix64.Parse(e.GetRawText());

    /// <summary>Problems the sim can't work around. Should come back empty; fix the data, not the check.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>(_loadProblems);

        foreach (var c in ShipClasses)
        {
            if (!_hulls.ContainsKey(c.HullId)) problems.Add($"Ship class {c.Key}: unknown hull '{c.HullId}'.");
            if (!_factions.ContainsKey(c.Faction)) problems.Add($"Ship class {c.Key}: unknown faction '{c.Faction}'.");
        }
        foreach (var dup in ShipClasses.GroupBy(c => c.Key).Where(g => g.Count() > 1))
            problems.Add($"Ship class {dup.Key} is listed {dup.Count()} times.");

        foreach (var p in PlanetClasses)
            foreach (var y in p.Yields)
                if (!_resources.ContainsKey(y)) problems.Add($"Planet class {p.Id}: unknown yield '{y}'.");

        foreach (var m in Maps) ValidateMap(m, problems);
        ValidateSurfaces(problems);
        return problems;
    }

    private void ValidateSurfaces(List<string> problems)
    {
        void CheckTerrain(string id, string where)
        {
            if (!_terrain.ContainsKey(id)) problems.Add($"{where}: unknown terrain '{id}'.");
        }

        foreach (var t in Terrain)
            if (t.Passable && t.MoveCost < 1) problems.Add($"Terrain {t.Id}: passable terrain needs move_cost of at least 1.");

        foreach (var s in Surfaces)
        {
            var where = $"Surface {s.ClassId}";
            if (!_planetClasses.ContainsKey(s.ClassId)) problems.Add($"{where}: unknown planet class.");
            if (s.Width < 8 || s.Height < 8) problems.Add($"{where}: map must be at least 8x8.");
            foreach (var role in new[] { "ocean", "shallows", "hills", "mountains" }) CheckTerrain(role, where);
            CheckTerrain(s.PolarTerrain, where);
            if (!(s.OceanBelow <= s.ShallowsBelow && s.ShallowsBelow <= s.HillsFrom && s.HillsFrom <= s.MountainsFrom))
                problems.Add($"{where}: elevation thresholds must rise ocean <= shallows <= hills <= mountains.");
            foreach (var b in s.Lowland) CheckTerrain(b.TerrainId, $"{where} lowland");
            if (s.Lowland.Count == 0 || s.Lowland[^1].Below <= Fix64.One) problems.Add($"{where}: last lowland band must reach above 1.");
            if (s.RegionColumns < 1 || s.RegionRows < 1) problems.Add($"{where}: regions must be at least 1x1.");
            foreach (var d in s.Deposits)
            {
                if (!_resources.ContainsKey(d.ResourceId)) problems.Add($"{where}: unknown deposit resource '{d.ResourceId}'.");
                foreach (var t in d.Terrain) CheckTerrain(t, $"{where} deposit {d.ResourceId}");
                if (d.Min < 1 || d.Max < d.Min) problems.Add($"{where} deposit {d.ResourceId}: amount range is invalid.");
                if (d.Chance <= Fix64.Zero || d.Chance > Fix64.One) problems.Add($"{where} deposit {d.ResourceId}: chance must be in (0, 1].");
            }
        }

        foreach (var t in Landing.Terrain)
        {
            CheckTerrain(t, "Landing");
            if (_terrain.TryGetValue(t, out var def) && !(def.Passable && def.Buildable))
                problems.Add($"Landing: terrain '{t}' must be passable and buildable.");
        }
        if (!_buildings.ContainsKey(Landing.HubBuilding)) problems.Add($"Landing: unknown hub building '{Landing.HubBuilding}'.");
        foreach (var u in Landing.Units)
            if (!_surfaceUnits.ContainsKey(u)) problems.Add($"Landing: unit '{u}' has no surface stats.");
        foreach (var u in SurfaceUnits)
        {
            if (!GroundVehicleNames.ContainsKey(u.Id)) problems.Add($"Surface unit {u.Id}: not a ground vehicle in vehicles.json.");
            if (u.Speed <= Fix64.Zero) problems.Add($"Surface unit {u.Id}: speed must be positive.");
        }
    }

    private void ValidateMap(MapDef m, List<string> problems)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in m.Bodies)
            if (!ids.Add(b.Id)) problems.Add($"Map {m.Id}: duplicate body id '{b.Id}'.");

        foreach (var b in m.Bodies)
        {
            var where = $"Map {m.Id} body {b.Id}";
            if (b.Parent != null && !ids.Contains(b.Parent)) problems.Add($"{where}: unknown parent '{b.Parent}'.");
            if (b.Size < 1) problems.Add($"{where}: size must be at least 1.");
            if (!_planetClasses.TryGetValue(b.ClassId, out var cls))
            {
                problems.Add($"{where}: unknown class '{b.ClassId}'.");
                continue;
            }
            foreach (var d in b.Deposits)
            {
                var res = Resources[d.Resource];
                if (!cls.Yields.Contains(res.Id)) problems.Add($"{where}: deposit '{res.Id}' is not a {cls.Id} yield.");
                if (d.Amount <= Fix64.Zero) problems.Add($"{where}: deposit '{res.Id}' must be positive.");
            }
        }

        if (m.Starts.Count != m.Players) problems.Add($"Map {m.Id}: {m.Players} players but {m.Starts.Count} starts.");
        for (var slot = 0; slot < m.Starts.Count; slot++)
            if (m.Starts.All(s => s.Slot != slot)) problems.Add($"Map {m.Id}: no start for slot {slot}.");
        foreach (var s in m.Starts)
        {
            var home = m.Bodies.FirstOrDefault(b => b.Id == s.HomeBodyId);
            if (home == null) problems.Add($"Map {m.Id}: start {s.Slot} home '{s.HomeBodyId}' is not a body.");
            else if (_planetClasses.TryGetValue(home.ClassId, out var cls) && !cls.IsFullColony)
                problems.Add($"Map {m.Id}: start {s.Slot} home '{home.Id}' ({cls.Id}) can't hold a full colony.");
        }
    }

    /// <summary>Walks up from <paramref name="startDirectory"/> to find the design repo's data/ folder.</summary>
    public static string FindDataDirectory(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "ships.json"))) return candidate;
        }
        throw new DirectoryNotFoundException($"No data/ships.json found above '{startDirectory}'.");
    }
}
