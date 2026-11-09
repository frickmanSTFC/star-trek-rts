using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// A faction (factions.json). <see cref="Playable"/> = "playable": true; <see cref="Role"/> is "playable" or the file's
/// other value (minor, dlc_or_minor, pirate, insurgent, crisis); <see cref="Eras"/> = the eras it appears in (eras.json ids).
/// </summary>
public sealed record FactionDef(string Id, string Name, bool Playable)
{
    public string Role { get; init; } = Playable ? "playable" : "";
    public IReadOnlyList<string> Eras { get; init; } = Array.Empty<string>();
}

/// <summary>Hull stats from ships.json. Speed is in map units per second (placeholder scale). Crew is the canon
/// complement (lore); what a ship costs in the crew resource is its class's CrewCost. Roles: the roles a class on
/// this hull may have (the civilian hull's; empty = any).</summary>
public sealed record HullDef(string Id, int Size, int Hp, Fix64 Speed, int Crew, IReadOnlyList<string> Roles);

/// <summary>A faction's ship class: a named template on a hull. Key is "faction.Name". Role is a ships.json roles
/// id ("combat" when the class has none). Cost, BuildSeconds, CrewCost and the tech gate come from the formula in
/// ships/rules.json (tools/ship_costs.py); RequiresTech is a tech_tree.json id (null = a starter hull),
/// RequiresAssimilated the same for a faction that doesn't research (the Borg). The sim doesn't read tech_tree.json,
/// so those two ids are checked by the ship designs page (tools/gen_ship_designs_page.py).</summary>
public sealed record ShipClassDef(string Faction, string Name, string HullId, string Era, string Source)
{
    public string Key => $"{Faction}.{Name}";
    public string Role { get; init; } = GameData.CombatRole;
    public IReadOnlyList<ResourceAmount> Cost { get; init; } = Array.Empty<ResourceAmount>();
    public Fix64 BuildSeconds { get; init; }
    public int CrewCost { get; init; }
    public string? RequiresTech { get; init; }
    public string? RequiresAssimilated { get; init; }
}

/// <summary>
/// Read-only game data loaded from the design repo's data/*.json files (the same files the
/// design pages use). Lists keep file order, so iteration is deterministic.
/// Numbers are parsed from their raw text into Fix64, never through double.
/// </summary>
public sealed partial class GameData
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

    /// <summary>Buildings the player can place on a planet surface, in menu order (surfaces.json build_menu).</summary>
    public IReadOnlyList<string> BuildMenu { get; }

    /// <summary>Resource that is a per-base power budget on a surface, not a stockpile (surfaces.json power_resource).</summary>
    public string PowerResource { get; }

    /// <summary>Resources that don't take storage space on a surface (surfaces.json storage_exempt).</summary>
    public IReadOnlyList<ResourceDef> StorageExempt { get; }

    /// <summary>Building wear and maintenance rules, or null when buildings never wear.</summary>
    public MaintenanceRules? Maintenance { get; }

    /// <summary>The ship in orbit and the ways down to the surface, or null.</summary>
    public OrbitRules? Orbit { get; }

    /// <summary>Surface techs (research), in file order.</summary>
    public IReadOnlyList<TechDef> Techs { get; }

    public TechDef Tech(string id) => Get(_techs, id, "surface tech");
    public bool HasTech(string id) => _techs.ContainsKey(id);

    /// <summary>Share of a finished building's cost returned when it is salvaged.</summary>
    public Fix64 SalvageRefund { get; }

    /// <summary>Whether a resource takes storage space on a surface (everything but the exempt ones and power).</summary>
    public bool IsStored(int resource) => _stored[resource];

    /// <summary>Stockpiled resources shown in the planet HUD's top bar, in order.</summary>
    public IReadOnlyList<ResourceDef> HudResources { get; }
    public LandingRules Landing { get; }

    /// <summary>Names and hull numbers handed to new units (surfaces.json unit_names).</summary>
    public UnitNameRules UnitNames { get; private init; } = new(new List<string>(), 10, 99);

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
    private readonly bool[] _stored;
    private readonly Dictionary<string, TechDef> _techs;

    /// <summary>Problems found while reading (unknown resource ids, etc.). Reported by Validate().</summary>
    private readonly List<string> _loadProblems;

    private GameData(
        List<FactionDef> factions, List<HullDef> hulls, List<ShipClassDef> classes, List<ResourceDef> resources,
        List<PlanetClassDef> planetClasses, List<BuildingDef> buildings, List<MapDef> maps,
        List<TerrainDef> terrain, List<PlanetSurfaceDef> surfaces, List<SurfaceUnitDef> surfaceUnits,
        HintRules hints, LandingRules landing, List<string> buildMenu, string powerResource, List<string> hudResources,
        List<string> storageExempt, Fix64 salvageRefund, MaintenanceRules? maintenance, List<TechDef> techs, OrbitRules? orbit,
        Dictionary<string, string> groundVehicles, List<string> loadProblems)
    {
        Maintenance = maintenance;
        Techs = techs;
        Orbit = orbit;
        SalvageRefund = salvageRefund;
        var exempt = new List<ResourceDef>();
        foreach (var id in storageExempt)
        {
            var r = resources.FirstOrDefault(x => x.Id == id);
            if (r != null) exempt.Add(r);
            else loadProblems.Add($"storage_exempt: unknown resource '{id}'.");
        }
        StorageExempt = exempt;
        _stored = resources.Select(r => r.Id != powerResource && exempt.All(e => e.Index != r.Index)).ToArray();
        BuildMenu = buildMenu;
        PowerResource = powerResource;
        var hud = new List<ResourceDef>();
        foreach (var id in hudResources)
        {
            var r = resources.FirstOrDefault(x => x.Id == id);
            if (r != null) hud.Add(r);
            else loadProblems.Add($"hud_resources: unknown resource '{id}'.");
        }
        HudResources = hud;
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
        _techs = ToLookup(techs, t => t.Id, "surface tech");
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

    /// <summary>The data documents the sim reads (data/manifest.json "documents"), in a fixed order.</summary>
    public static readonly IReadOnlyList<string> Documents = new[]
    {
        "factions", "ships", "resources", "planet_classes", "buildings", "maps", "surfaces", "vehicles", "climate",
        "eras", "tech_tree", // read-only: loaded and checked, not played yet (GameData.Eras.cs)
        "achievements", // optional: a data set without it has no achievements (GameData.Achievements.cs)
        "civilizations", // optional: a data set without it has no natives or ruins (GameData.Civilizations.cs)
    };

    /// <summary>Documents a data set may leave out (the property is then null).</summary>
    private static readonly IReadOnlyList<string> OptionalDocuments = new[] { "achievements", "civilizations" };

    /// <summary>
    /// The text of every file this was loaded from, by path under data/ ("manifest.json", "surfaces/orbit.json",
    /// "layers/eras/ent.json"), before layers were applied (a draft, if any, is already in). The test panel's data
    /// tuning patches these and loads a new GameData from them (<see cref="DataTuning"/>); the pack holds the same.
    /// </summary>
    public IReadOnlyDictionary<string, string> Sources { get; private init; } = new Dictionary<string, string>();

    /// <summary>The files of the sim's documents in load order (what the test panel's DATA tab lists).</summary>
    public IReadOnlyList<string> Files { get; private init; } = Array.Empty<string>();

    /// <summary>The layers this was loaded with.</summary>
    public DataSetup Setup { get; private init; } = DataSetup.Base;

    /// <summary>
    /// SHA-256 of the data set (<see cref="DataFingerprint"/>): the same for the JSON folders and the release pack made
    /// from them. Meant for the multiplayer lobby: every player must show the same fingerprint (and setup).
    /// </summary>
    public string Fingerprint { get; private init; } = "";

    /// <summary>
    /// Loads the base data from a data/ folder (manifest + the sim's documents + layers; no draft). This is the simple
    /// entry point for tools (tests, balance runs). Unlisted .json files are reported by <see cref="Validate"/>.
    /// </summary>
    public static GameData Load(string dataDirectory) => Load(dataDirectory, DataSetup.Base);

    /// <summary>Loads from a data/ folder with the era / faction / scenario layers of <paramref name="setup"/> applied.</summary>
    public static GameData Load(string dataDirectory, DataSetup setup)
    {
        var problems = new List<string>();
        var files = DataStore.ReadBase(dataDirectory, problems);
        return Load(files, setup, problems);
    }

    /// <summary>Loads a release pack (see <see cref="DataPack"/>); throws <see cref="InvalidDataException"/> if it is damaged or was changed.</summary>
    public static GameData LoadPack(byte[] pack, DataSetup? setup = null) => Load(DataPack.Read(pack, out _), setup ?? DataSetup.Base);

    /// <summary>Loads from file texts already in memory, by path under data/ (see <see cref="Sources"/>).</summary>
    public static GameData Load(IReadOnlyDictionary<string, string> files, DataSetup? setup = null) => Load(files, setup ?? DataSetup.Base, new List<string>());

    private static GameData Load(IReadOnlyDictionary<string, string> files, DataSetup setup, List<string> problems)
    {
        if (!files.TryGetValue(DataManifest.FileName, out var manifestText))
            throw new InvalidDataException("The data has no manifest.json.");
        var manifest = DataManifest.Parse(manifestText);
        var texts = new Dictionary<string, string>(files, StringComparer.Ordinal);
        // Layer patches on documents the game doesn't read (tech_tree, hazards, …) are the pages' business.
        problems.AddRange(DataPatcher.CheckLayers(files, manifest, Documents.ToList()));
        foreach (var layerPath in setup.LayerPaths(files, problems))
        {
            var layerProblems = new List<string>();
            DataLayer layer;
            try
            {
                layer = DataLayer.Parse(layerPath, files[layerPath]);
            }
            catch (JsonException)
            {
                continue; // reported by CheckLayers
            }
            DataPatcher.Apply(texts, manifest, layer with { Patches = layer.Patches.Where(p => Documents.Contains(p.Doc) || !manifest.HasDocument(p.Doc)).ToList() }, layerProblems);
            foreach (var p in layerProblems.Where(p => !problems.Contains(p))) problems.Add(p);
        }
        var roots = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var doc in Documents)
        {
            if (!manifest.HasDocument(doc))
            {
                if (OptionalDocuments.Contains(doc)) continue;
                throw new InvalidDataException($"data/manifest.json has no document '{doc}'.");
            }
            roots[doc] = manifest.Assemble(texts, doc, problems);
        }
        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { [DataManifest.FileName] = manifestText };
        var parts = Documents.Where(manifest.HasDocument).SelectMany(manifest.PartsOf).ToList();
        foreach (var path in parts.Concat(files.Keys.Where(k => DataPatcher.Kind(k) != null).OrderBy(k => k, StringComparer.Ordinal)))
            if (files.TryGetValue(path, out var text)) sources[path] = text;
        return FromDocuments(roots, problems, sources, parts, setup);
    }

    private static GameData FromDocuments(IReadOnlyDictionary<string, JsonElement> roots, List<string> problems,
        Dictionary<string, string> sources, List<string> parts, DataSetup setup)
    {
        JsonElement Read(string doc) => roots[doc];

        var factionsRoot = Read("factions");
        var shipsRoot = Read("ships");
        var resourcesRoot = Read("resources");
        var planetsRoot = Read("planet_classes");
        var buildingsRoot = Read("buildings");
        var mapsRoot = Read("maps");
        var surfacesRoot = Read("surfaces");
        var vehiclesRoot = Read("vehicles");
        var climateRoot = Read("climate");
        var erasRoot = Read("eras");
        var techTreeRoot = Read("tech_tree");

        var factions = factionsRoot.GetProperty("factions").EnumerateArray()
            .Select(f =>
            {
                var playable = f.GetProperty("playable");
                return new FactionDef(Str(f, "id"), Str(f, "name"), playable.ValueKind == JsonValueKind.True)
                {
                    Role = playable.ValueKind == JsonValueKind.True ? "playable" : playable.ValueKind == JsonValueKind.String ? playable.GetString()! : playable.GetRawText(),
                    Eras = f.TryGetProperty("eras", out var fe) ? fe.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>(),
                };
            })
            .ToList();

        var hulls = shipsRoot.GetProperty("hulls").EnumerateArray()
            .Select(h => new HullDef(Str(h, "id"), h.GetProperty("size").GetInt32(), h.GetProperty("hp").GetInt32(),
                Num(h.GetProperty("speed")), h.GetProperty("crew").GetInt32(),
                h.TryGetProperty("roles", out var hr) ? hr.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>()))
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

        var classes = ReadShipClasses(shipsRoot, Amounts, problems);

        List<Modifier> Modifiers(JsonElement parent, string owner)
        {
            var list = new List<Modifier>();
            if (!parent.TryGetProperty("modifiers", out var arr)) return list;
            foreach (var m in arr.EnumerateArray())
            {
                var stat = Str(m, "stat");
                if (!Modifier.Stats.Contains(stat)) problems.Add($"{owner}: unknown modifier stat '{stat}'.");
                List<string> Ids(string p) => m.TryGetProperty(p, out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>();
                list.Add(new Modifier(stat, Ids("buildings"), Ids("units"), m.TryGetProperty("add", out var add) ? Num(add) : Fix64.Zero));
            }
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
                int Int(string prop) => b.TryGetProperty(prop, out var v) ? v.GetInt32() : 0;
                return new BuildingDef(id, Str(b, "name"), Str(b, "layer"), Str(b, "category"),
                    b.TryGetProperty("effect", out var effect) ? effect.GetString() ?? "" : "",
                    Amounts(b, "cost", $"Building {id}"), Amounts(b, "output_per_min", $"Building {id}"),
                    Amounts(b, "upkeep_per_min", $"Building {id}"),
                    Int("build_seconds"),
                    b.TryGetProperty("requires_deposit", out var rd) && rd.GetBoolean(),
                    b.TryGetProperty("extract_per_min", out var ex) ? Num(ex) : Fix64.Zero,
                    Int("build_radius"), Int("build_queues"),
                    b.TryGetProperty("power_supply", out var ps) ? Num(ps) : Fix64.Zero,
                    b.TryGetProperty("power_use", out var pu) ? Num(pu) : Fix64.Zero,
                    Int("level"),
                    b.TryGetProperty("upgrades_to", out var up) ? up.GetString() ?? "" : "",
                    b.TryGetProperty("storage", out var st) ? Num(st) : Fix64.Zero,
                    b.TryGetProperty("recipes", out var recipes)
                        ? recipes.EnumerateArray().Select(r => new Recipe(Amounts(r, "in", $"Building {id} recipe"), Amounts(r, "out", $"Building {id} recipe"))).ToList()
                        : new List<Recipe>(),
                    b.TryGetProperty("cycles_per_min", out var cy) ? Num(cy) : Fix64.Zero,
                    b.TryGetProperty("requires_depleted", out var rdp) && rdp.GetBoolean(),
                    b.TryGetProperty("reopen_share", out var rs) ? Num(rs) : Fix64.Zero,
                    Int("requires_level"),
                    b.TryGetProperty("produces", out var pr) ? pr.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>(),
                    Int("production_queue"),
                    b.TryGetProperty("wear_per_min", out var wr) ? Num(wr) : Fix64.Zero,
                    b.TryGetProperty("research_per_min", out var rpm) ? Num(rpm) : Fix64.Zero,
                    b.TryGetProperty("levels", out var lv)
                        ? lv.EnumerateArray().Select(l => new BuildingLevel(Str(l, "name"), Amounts(l, "cost", $"Building {id} level"),
                            l.TryGetProperty("build_seconds", out var lbs) ? lbs.GetInt32() : 0,
                            l.TryGetProperty("requires_level", out var lrl) ? lrl.GetInt32() : 0,
                            Modifiers(l, $"Building {id} level"))).ToList()
                        : new List<BuildingLevel>(),
                    b.TryGetProperty("requires_tech", out var rt) ? rt.GetString() ?? "" : "",
                    b.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "")
                {
                    Crewed = b.TryGetProperty("crewed", out var cw) && cw.ValueKind == JsonValueKind.True,
                };
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
                    }).ToList(),
                    p.TryGetProperty("life", out var life) ? ReadLife(life) : null);
            })
            .ToList();

        var hintsEl = surfacesRoot.GetProperty("hints");
        var hints = new HintRules(hintsEl.GetProperty("likely").GetInt32(), hintsEl.GetProperty("rich").GetInt32());

        var landingEl = surfacesRoot.GetProperty("landing");
        var landing = new LandingRules(
            landingEl.GetProperty("terrain").EnumerateArray().Select(t => t.GetString()!).ToList(),
            landingEl.GetProperty("descent_seconds").GetInt32(),
            landingEl.TryGetProperty("assemble_seconds", out var assemble) ? assemble.GetInt32() : 0,
            Amounts(landingEl, "start_stock", "Landing"),
            landingEl.GetProperty("reveal_radius").GetInt32(),
            Str(landingEl, "hub"),
            landingEl.GetProperty("units").EnumerateArray().Select(u => u.GetString()!).ToList());

        var surfaceUnits = surfacesRoot.GetProperty("units").EnumerateArray()
            .Select(u => new SurfaceUnitDef(Str(u, "id"), Num(u.GetProperty("speed")), u.GetProperty("survey_radius").GetInt32(),
                u.TryGetProperty("extract_per_min", out var ex) ? Num(ex) : Fix64.Zero,
                u.TryGetProperty("hopper", out var hop) ? Num(hop) : Fix64.Zero,
                u.TryGetProperty("deploy_seconds", out var dep) ? dep.GetInt32() : 0,
                u.TryGetProperty("pod_speed", out var pod) ? Num(pod) : Fix64.Zero,
                u.TryGetProperty("breakdown", out var br)
                    ? new BreakdownRules(Num(br.GetProperty("chance_per_min")), Num(br.GetProperty("slowest_drive")),
                        br.GetProperty("severities").EnumerateArray().Select(v =>
                        {
                            var dmg = v.GetProperty("damage");
                            return new BreakdownSeverity(Str(v, "id"), Str(v, "name"), v.GetProperty("weight").GetInt32(),
                                dmg[0].GetInt32(), dmg[1].GetInt32(), v.TryGetProperty("loses_load", out var ll) && ll.GetBoolean());
                        }).ToList())
                    : null,
                u.TryGetProperty("repair", out var rp)
                    ? new RepairRules(Num(rp.GetProperty("per_second")), rp.GetProperty("crew_seconds").GetInt32(),
                        Amounts(rp, "cost_per_point", $"Surface unit {Str(u, "id")} repair"))
                    : null,
                Amounts(u, "cost", $"Surface unit {Str(u, "id")}"),
                u.TryGetProperty("build_seconds", out var bs) ? bs.GetInt32() : 0,
                u.TryGetProperty("description", out var ud) ? ud.GetString() ?? "" : "",
                u.TryGetProperty("mark", out var mk) ? mk.GetInt32() : null,
                ReadDamage(u), ReadTyres(u, Amounts), ReadFound(u, Amounts)))
            .ToList();

        var unitNames = new UnitNameRules(new List<string>(), 10, 99);
        if (surfacesRoot.TryGetProperty("unit_names", out var un))
        {
            var numbers = un.GetProperty("numbers");
            unitNames = new UnitNameRules(un.GetProperty("pool").EnumerateArray().Select(n => n.GetString() ?? "").ToList(),
                numbers[0].GetInt32(), numbers[1].GetInt32());
        }

        var groundVehicles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var list in new[] { "ground_vehicles", "ground_forces" }) // surface units are vehicles or teams
            foreach (var v in vehiclesRoot.GetProperty(list).EnumerateArray())
                groundVehicles.TryAdd(Str(v, "id"), Str(v, "name"));

        return new GameData(factions, hulls, classes, resources, planetClasses, buildings, maps,
            terrain, surfaces, surfaceUnits, hints, landing,
            surfacesRoot.GetProperty("build_menu").EnumerateArray().Select(b => b.GetString()!).ToList(),
            Str(surfacesRoot, "power_resource"),
            surfacesRoot.GetProperty("hud_resources").EnumerateArray().Select(r => r.GetString()!).ToList(),
            surfacesRoot.TryGetProperty("storage_exempt", out var ex)
                ? ex.EnumerateArray().Select(r => r.GetString()!).ToList() : new List<string>(),
            surfacesRoot.TryGetProperty("salvage_refund", out var sr) ? Num(sr) : Fix64.Zero,
            surfacesRoot.TryGetProperty("maintenance", out var mt)
                ? new MaintenanceRules(Num(mt.GetProperty("decline_below")), Num(mt.GetProperty("slowest_output")),
                    Num(mt.GetProperty("break_chance_per_min_at_zero")), Num(mt.GetProperty("auto_below")),
                    Num(mt.GetProperty("crew_walk_seconds")), Num(mt.GetProperty("crew_seconds_per_tile")),
                    Num(mt.GetProperty("repair_per_second")), Amounts(mt, "cost_per_point", "Maintenance"))
                : null,
            surfacesRoot.TryGetProperty("research", out var rs)
                ? rs.EnumerateArray().Select((t, i) => new TechDef(i, Str(t, "id"), Str(t, "name"), Num(t.GetProperty("cost")),
                    t.GetProperty("requires").EnumerateArray().Select(x => x.GetString()!).ToList(),
                    t.TryGetProperty("effect", out var te) ? te.GetString() ?? "" : "", Modifiers(t, $"Tech {Str(t, "id")}"),
                    t.TryGetProperty("description", out var td) ? td.GetString() ?? "" : "")).ToList()
                : new List<TechDef>(),
            surfacesRoot.TryGetProperty("orbit", out var ob) ? ReadOrbit(ob, Amounts) : null,
            groundVehicles, problems)
        {
            UnitNames = unitNames,
            ShipRoles = ReadShipRoles(shipsRoot),
            Environments = ReadEnvironments(surfacesRoot),
            Telemetry = ReadTelemetry(surfacesRoot),
            Climate = ReadClimate(climateRoot, problems),
            Achievements = roots.TryGetValue("achievements", out var achievementsRoot) ? ReadAchievements(achievementsRoot, problems) : null,
            Civilizations = roots.TryGetValue("civilizations", out var civRoot) ? ReadCivilizations(civRoot, Amounts, problems) : null,
            Eras = ReadEras(erasRoot),
            TechTree = ReadTechTree(techTreeRoot),
            StartingSetRefs = ReadStartingSetRefs(buildingsRoot, Amounts),
            Lanes = ReadLanes(surfacesRoot),
            BuildingFootprints = ReadBuildingFootprints(buildingsRoot, problems),
            UnitFootprints = ReadUnitFootprints(surfacesRoot),
            Gravity = ReadGravity(surfacesRoot, problems),
            Habitability = ReadHabitability(surfacesRoot, problems),
            BuildingWorlds = ReadBuildingWorlds(buildingsRoot),
            SystemGenerator = ReadSystemGenerator(mapsRoot),
            SystemRules = ReadSystemRules(mapsRoot, Amounts),
            Sources = sources,
            Files = parts,
            Setup = setup,
            Fingerprint = DataFingerprint.Of(sources),
        };
    }

    private static OrbitRules ReadOrbit(JsonElement o, Func<JsonElement, string, string, List<ResourceAmount>> amounts)
    {
        List<string> Ids(JsonElement e, string p) => e.GetProperty(p).EnumerateArray().Select(x => x.GetString()!).ToList();
        var drop = o.GetProperty("outpost_drop");
        var shuttle = o.GetProperty("shuttle");
        var sat = o.GetProperty("satellite");
        var tr = o.GetProperty("transporter");
        return new OrbitRules(
            Str(o, "era"),
            o.GetProperty("ships").EnumerateObject().Select(p => (p.Name, new OrbitShip(Str(p.Value, "name"), Str(p.Value, "class")))).ToList(),
            amounts(o, "hold", "Orbit hold"),
            Str(drop, "building"), amounts(drop, "cost", "Outpost drop"), drop.GetProperty("descent_seconds").GetInt32(), drop.GetProperty("min_distance").GetInt32(),
            shuttle.GetProperty("flight_seconds").GetInt32(), Ids(shuttle, "payloads"),
            amounts(sat, "cost", "Satellite"), sat.GetProperty("max").GetInt32(), sat.GetProperty("band_rows").GetInt32(), sat.GetProperty("sweep_seconds").GetInt32(),
            tr.GetProperty("levels").EnumerateArray().Select((l, i) => new TransporterLevel(i + 1, Str(l, "name"), Ids(l, "payloads"),
                l.GetProperty("range").GetInt32(), Num(l.GetProperty("cargo")), l.GetProperty("cooldown_seconds").GetInt32())).ToList(),
            tr.GetProperty("by_era").EnumerateObject().Select(p => (p.Name, p.Value.GetInt32())).ToList(),
            tr.TryGetProperty("factions", out var tf) ? tf.EnumerateObject().Select(p => (p.Name, p.Value.GetInt32())).ToList() : new List<(string, int)>())
        {
            ShuttleFuel = amounts(shuttle, "fuel", "Drop ship fuel"),
        };
    }

    private static LifeRules ReadLife(JsonElement l)
    {
        PatchRule Patch(JsonElement e, string count)
        {
            var r = e.GetProperty("radius");
            return new PatchRule(e.GetProperty(count).GetInt32(), r[0].GetInt32(), r[1].GetInt32(),
                e.GetProperty("terrain").EnumerateArray().Select(t => t.GetString()!).ToList());
        }
        var wildlife = l.GetProperty("wildlife");
        return new LifeRules(Num(l.GetProperty("natives_chance")), l.GetProperty("native_radius").GetInt32(),
            Patch(wildlife, "herds"), Num(wildlife.GetProperty("predator_share")), Patch(l.GetProperty("toxic"), "patches"),
            l.GetProperty("biomass").EnumerateObject().Select(p => (p.Name, Num(p.Value))).ToList());
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
        ValidateShips(problems);

        foreach (var p in PlanetClasses)
            foreach (var y in p.Yields)
                if (!_resources.ContainsKey(y)) problems.Add($"Planet class {p.Id}: unknown yield '{y}'.");

        foreach (var m in Maps) ValidateMap(m, problems);
        ValidateSurfaces(problems);
        ValidateUnitNames(problems);
        ValidateEnvironments(problems);
        ValidateRoverDamage(problems);
        ValidateClimate(problems);
        ValidateFounding(problems);
        ValidateLanes(problems);
        ValidateWorlds(problems);
        ValidateSystem(problems);
        ValidateEras(problems);
        ValidateTechTree(problems);
        ValidateStartingSets(problems);
        ValidateAchievements(problems);
        ValidateCivilizations(problems);
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
            if (s.Life is { } life)
            {
                foreach (var t in life.Wildlife.Terrain.Concat(life.Toxic.Terrain).Concat(life.Biomass.Select(b => b.Terrain))) CheckTerrain(t, $"{where} life");
                if (life.NativesChance < Fix64.Zero || life.NativesChance > Fix64.One) problems.Add($"{where} life: natives_chance must be in [0, 1].");
                foreach (var pr in new[] { life.Wildlife, life.Toxic })
                    if (pr.Count < 0 || pr.RadiusMin < 0 || pr.RadiusMax < pr.RadiusMin) problems.Add($"{where} life: patch counts and radius ranges are invalid.");
            }
        }

        foreach (var t in Landing.Terrain)
        {
            CheckTerrain(t, "Landing");
            if (_terrain.TryGetValue(t, out var def) && !(def.Passable && def.Buildable))
                problems.Add($"Landing: terrain '{t}' must be passable and buildable.");
        }
        if (!_buildings.TryGetValue(Landing.HubBuilding, out var hub)) problems.Add($"Landing: unknown hub building '{Landing.HubBuilding}'.");
        else if (hub.BuildRadius < 1 || hub.BuildQueues < 1) problems.Add($"Landing: hub '{hub.Id}' needs build_radius and build_queues of at least 1.");
        if (!_resources.TryGetValue(PowerResource, out var power)) problems.Add($"power_resource: unknown resource '{PowerResource}'.");
        if (HudResources.Any(r => r.Id == PowerResource)) problems.Add("hud_resources: the power resource is a budget, not a stockpile; leave it out.");
        if (Landing.StartStock.Any(a => power != null && a.Resource == power.Index)) problems.Add("Landing start_stock: the power resource can't be stockpiled.");

        // Stocks, holds and costs are never negative: a negative start stock or hold starts a player below zero, a
        // negative cost pays out (the command fuzz test's data edits found these accepted).
        void NotNegative(IEnumerable<ResourceAmount> amounts, string where)
        {
            if (amounts.Any(a => a.Amount < Fix64.Zero)) problems.Add($"{where}: amounts can't be negative.");
        }
        NotNegative(Landing.StartStock, "Landing start_stock");
        foreach (var m in Maps) NotNegative(m.StartStock, $"Map {m.Id} start_stock");
        if (Orbit is { } held)
        {
            NotNegative(held.Hold, "Orbit hold");
            NotNegative(held.OutpostCost, "Orbit outpost_drop cost");
            NotNegative(held.SatelliteCost, "Orbit satellite cost");
        }
        foreach (var b in Buildings)
        {
            NotNegative(b.Cost, $"Building {b.Id} cost");
            foreach (var l in b.Levels) NotNegative(l.Cost, $"Building {b.Id} level {l.Name} cost");
        }
        foreach (var u in SurfaceUnits)
        {
            NotNegative(u.Cost, $"Surface unit {u.Id} cost");
            if (u.Found is { } found) NotNegative(found.Cost, $"Surface unit {u.Id} found cost");
        }

        // The core upgrade line from the landing hub: each step exists, is buildable and goes up one level.
        var seen = new HashSet<string>();
        for (var core = hub; core != null && seen.Add(core.Id);)
        {
            if (!core.IsCore || core.BuildQueues < 1) problems.Add($"Core {core.Id}: needs build_radius and build_queues of at least 1.");
            if (core.UpgradesTo.Length == 0) break;
            if (!_buildings.TryGetValue(core.UpgradesTo, out var next))
            {
                problems.Add($"Core {core.Id}: upgrades_to unknown building '{core.UpgradesTo}'.");
                break;
            }
            if (next.BuildSeconds < 1) problems.Add($"Core {next.Id}: needs build_seconds (the upgrade time).");
            if (next.Level != core.Level + 1) problems.Add($"Core {next.Id}: level should be {core.Level + 1}.");
            core = next;
        }

        foreach (var id in EveryBuildMenu.Concat(seen))
        {
            if (!_buildings.TryGetValue(id, out var b) || power == null) continue;
            if (b.OutputPerMinute.Concat(b.UpkeepPerMinute).Any(a => a.Resource == power.Index))
                problems.Add($"Building {id}: {PowerResource} is a power budget on the surface; use power_supply / power_use instead of output or upkeep.");
        }

        foreach (var id in EveryBuildMenu)
        {
            if (!_buildings.TryGetValue(id, out var b))
            {
                problems.Add($"Build menu: unknown building '{id}'.");
                continue;
            }
            if (b.Layer != "surface") problems.Add($"Build menu: {id} is a {b.Layer} building, not surface.");
            if (b.BuildSeconds < 1) problems.Add($"Build menu: {id} needs build_seconds.");
            if (b.Cost.Count == 0) problems.Add($"Build menu: {id} has no cost.");
            if (b.RequiresDeposit && b.ExtractPerMinute <= Fix64.Zero) problems.Add($"Build menu: {id} requires a deposit but has no extract_per_min.");
            if (b.RequiresDeposit && b.RequiresDepleted) problems.Add($"Build menu: {id} can't need both a deposit and a used-up one.");
            if (b.RequiresDepleted && (b.ReopenShare <= Fix64.Zero || b.ReopenShare > Fix64.One || b.ExtractPerMinute <= Fix64.Zero))
                problems.Add($"Build menu: {id} reopens deposits: needs reopen_share in (0, 1] and extract_per_min.");
            if (b.Recipes.Count > 0 && b.CyclesPerMinute <= Fix64.Zero) problems.Add($"Build menu: {id} has recipes but no cycles_per_min.");
            foreach (var r in b.Recipes)
                if (r.In.Count == 0 || r.Out.Count == 0 || r.In.Concat(r.Out).Any(a => a.Amount <= Fix64.Zero || (power != null && a.Resource == power.Index)))
                    problems.Add($"Build menu: {id} recipe needs positive in and out amounts, and no {PowerResource}.");
            foreach (var unit in b.Produces)
            {
                if (!_surfaceUnits.TryGetValue(unit, out var ud)) problems.Add($"Build menu: {id} produces unknown surface unit '{unit}'.");
                else if (ud.BuildSeconds < 1 || ud.Cost.Count == 0) problems.Add($"Surface unit {unit}: {id} builds it, so it needs cost and build_seconds.");
            }
            if (b.RequiresTech.Length > 0 && !_techs.ContainsKey(b.RequiresTech))
                problems.Add($"Build menu: {id} requires tech '{b.RequiresTech}', which isn't in surfaces.json research.");
            if (b.Wears && Maintenance == null) problems.Add($"Build menu: {id} wears, but surfaces.json has no maintenance rules.");
            foreach (var l in b.Levels)
            {
                if (l.Cost.Count == 0 || l.BuildSeconds < 1) problems.Add($"Build menu: {id} level {l.Name} needs a cost and build_seconds.");
                if (l.RequiresLevel > 0 && !seen.Any(c => _buildings[c].Level >= l.RequiresLevel))
                    problems.Add($"Build menu: {id} level {l.Name} needs a level {l.RequiresLevel} core, which the upgrade line doesn't reach.");
            }
            if (b.IsFactory && b.ProductionQueue < 1) problems.Add($"Build menu: {id} produces units but has no production_queue.");
            if (b.RequiresLevel > 0 && !seen.Any(c => _buildings[c].Level >= b.RequiresLevel))
                problems.Add($"Build menu: {id} needs a level {b.RequiresLevel} core, but the upgrade line doesn't reach it.");
        }
        foreach (var t in Techs)
        {
            if (t.Cost <= Fix64.Zero) problems.Add($"Tech {t.Id}: cost must be positive.");
            foreach (var r in t.Requires)
                if (!_techs.TryGetValue(r, out var req) || req.Index >= t.Index) problems.Add($"Tech {t.Id}: requires '{r}', which must be listed before it.");
            foreach (var m in t.Modifiers)
            {
                foreach (var bid in m.Buildings)
                    if (!_buildings.ContainsKey(bid)) problems.Add($"Tech {t.Id}: modifier names unknown building '{bid}'.");
                foreach (var uid in m.Units)
                    if (!_surfaceUnits.ContainsKey(uid)) problems.Add($"Tech {t.Id}: modifier names unknown unit '{uid}'.");
            }
        }
        if (Techs.Count > 0 && !BuildMenu.Any(id => _buildings.TryGetValue(id, out var lab) && lab.IsLab))
            problems.Add("Research: there are techs but no Research Lab in the build menu.");
        if (Maintenance is { } mr && (mr.RepairPerSecond <= Fix64.Zero || mr.DeclineBelow <= Fix64.Zero || mr.SlowestOutput <= Fix64.Zero || mr.SlowestOutput > Fix64.One))
            problems.Add("Maintenance: needs positive repair_per_second and decline_below, and slowest_output in (0, 1].");
        if (Orbit is { } orbit)
        {
            foreach (var (faction, ship) in orbit.Ships)
            {
                if (!_factions.ContainsKey(faction)) problems.Add($"Orbit ship: unknown faction '{faction}'.");
                if (!HasShipClass($"{faction}.{ship.ClassName}")) problems.Add($"Orbit ship for {faction}: no ships.json class '{ship.ClassName}'.");
            }
            if (!_buildings.TryGetValue(orbit.OutpostBuilding, out var drop) || !drop.IsCore) problems.Add($"Orbit outpost_drop: '{orbit.OutpostBuilding}' must be a colony core.");
            foreach (var u in orbit.ShuttlePayloads.Concat(orbit.TransporterLevels.SelectMany(l => l.Payloads)).Where(p => p != OrbitRules.Supplies))
                if (!_surfaceUnits.TryGetValue(u, out var ud)) problems.Add($"Orbit: payload '{u}' has no surface stats.");
                else if (ud.Cost.Count == 0) problems.Add($"Orbit: payload '{u}' needs a cost.");
            foreach (var (_, level) in orbit.TransporterByEra.Concat(orbit.TransporterFactions))
                if (level < 0 || level > orbit.TransporterLevels.Count) problems.Add($"Orbit transporter: level {level} doesn't exist.");
            if (orbit.SatelliteMax < 1 || orbit.SatelliteBandRows < 1 || orbit.SatelliteSweepSeconds < 1) problems.Add("Orbit satellite: needs max, band_rows and sweep_seconds of at least 1.");
        }
        if (SalvageRefund < Fix64.Zero || SalvageRefund > Fix64.One) problems.Add("salvage_refund must be between 0 and 1.");
        if (!seen.Any(c => _buildings[c].Storage > Fix64.Zero)) problems.Add($"Landing: hub '{Landing.HubBuilding}' has no storage, so nothing could be stockpiled.");

        // Every mined deposit must be something storage holds, and raw ores must have a refinery recipe in the build menu.
        foreach (var s in Surfaces)
            foreach (var d in s.Deposits)
            {
                if (!_resources.TryGetValue(d.ResourceId, out var r)) continue;
                if (!IsStored(r.Index)) problems.Add($"Surface {s.ClassId} deposit {r.Id}: is storage-exempt, so mining it would bypass storage.");
                if (r.Tier == "raw" && !BuildMenu.Any(id => _buildings.TryGetValue(id, out var b) && b.Recipes.Any(rc => rc.In.Any(a => a.Resource == r.Index))))
                    problems.Add($"Surface {s.ClassId} deposit {r.Id}: raw ore that no building in the build menu refines.");
            }
        foreach (var u in Landing.Units)
            if (!_surfaceUnits.ContainsKey(u)) problems.Add($"Landing: unit '{u}' has no surface stats.");
        foreach (var u in SurfaceUnits)
        {
            if (!GroundVehicleNames.ContainsKey(u.Id)) problems.Add($"Surface unit {u.Id}: not a ground vehicle in vehicles.json.");
            if (u.Speed <= Fix64.Zero) problems.Add($"Surface unit {u.Id}: speed must be positive.");
            if (u.Mark is { } mark && (mark < 1 || mark > 3)) problems.Add($"Surface unit {u.Id}: mark must be 1, 2 or 3.");
            if (u.CanMine && (u.Hopper <= Fix64.Zero || u.PodSpeed <= Fix64.Zero || u.DeploySeconds < 1))
                problems.Add($"Surface unit {u.Id}: a miner needs hopper, pod_speed and deploy_seconds.");
            if (u.Breakdown is { } b)
            {
                if (u.Repair == null) problems.Add($"Surface unit {u.Id}: it can break down, so it needs repair rules.");
                if (b.ChancePerMinute < Fix64.Zero) problems.Add($"Surface unit {u.Id}: breakdown chance can't be negative.");
                if (b.SlowestDrive <= Fix64.Zero || b.SlowestDrive > Fix64.One) problems.Add($"Surface unit {u.Id}: slowest_drive must be in (0, 1].");
                if (b.Severities.Count == 0 || b.Severities.Any(v => v.Weight < 1)) problems.Add($"Surface unit {u.Id}: breakdown needs severities with positive weights.");
                foreach (var v in b.Severities)
                    if (v.DamageMin < 1 || v.DamageMax < v.DamageMin || v.DamageMax > 100) problems.Add($"Surface unit {u.Id} breakdown {v.Id}: damage must be within 1..100.");
            }
            if (u.Repair is { } r && (r.PerSecond <= Fix64.Zero || r.CrewSeconds < 0 || r.CostPerPoint.Any(a => a.Amount < Fix64.Zero)))
                problems.Add($"Surface unit {u.Id}: repair needs a positive per_second, crew_seconds of 0 or more and costs of 0 or more.");
        }
    }

    private void ValidateUnitNames(List<string> problems)
    {
        var names = UnitNames;
        if (names.Pool.Count == 0) problems.Add("unit_names: the name pool is empty.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in names.Pool)
        {
            if (n.Length == 0 || n.Any(c => c < 'A' || c > 'Z')) problems.Add($"unit_names: '{n}' must be uppercase letters A-Z only.");
            if (!seen.Add(n)) problems.Add($"unit_names: '{n}' is listed more than once.");
        }
        if (names.NumbersFrom < 0 || names.NumbersTo < names.NumbersFrom || names.NumbersTo > 9999)
            problems.Add($"unit_names: numbers [{names.NumbersFrom}, {names.NumbersTo}] must be a range within 0..9999.");
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
            if (File.Exists(Path.Combine(candidate, DataManifest.FileName))) return candidate;
        }
        throw new DirectoryNotFoundException($"No data/{DataManifest.FileName} found above '{startDirectory}'.");
    }
}
