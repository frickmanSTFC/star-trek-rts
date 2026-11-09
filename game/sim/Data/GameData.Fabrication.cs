using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A building's fabrication queue (buildings.json fabricator): Lines orders run at once, at Speed (× power,
/// condition and the building's speed bonuses).</summary>
public sealed record FabricatorDef(int Lines, Fix64 Speed);

/// <summary>One fabrication recipe (surfaces/fabrication.json recipes): Id = the item it makes (one per order), In = what
/// one order takes, Seconds at full speed. EraMin / EraMax ("" = any) are for later.</summary>
public sealed record FabRecipe(string Id, int Output, IReadOnlyList<ResourceAmount> In, Fix64 Seconds, string EraMin, string EraMax);

/// <summary>A colony core's small workshop: these recipes only, straight from materials (the tree flattened), taking
/// TimeFactor × the summed shop seconds of the whole tree. Speed is the same thing as a share of a shop's speed
/// (1 / TimeFactor), shown in the UI; it is not applied a second time.</summary>
public sealed record HubWorkshopRules(IReadOnlyList<string> Recipes, Fix64 Speed, Fix64 TimeFactor);

/// <summary>A repair without spare kits: up to MaxCondition, at Speed × the normal repair speed; until a proper repair
/// a building wears WearMultiplier × faster (a rover too) and a crawler breaks down BreakdownMultiplier × as often.</summary>
public sealed record JuryRigRules(Fix64 MaxCondition, Fix64 Speed, Fix64 WearMultiplier, Fix64 BreakdownMultiplier);

/// <summary>The parts economy on a surface (surfaces/fabrication.json). See its _notes_fabrication.</summary>
public sealed record FabricationRules(
    IReadOnlyList<FabRecipe> Recipes,
    HubWorkshopRules Hub,
    Fix64 KitPoints,
    IReadOnlyList<(string Id, string Kit)> BuildingKits,
    IReadOnlyList<(string Id, string Kit)> UnitKits,
    string DefaultKit,
    IReadOnlyList<(string Severity, int Kits)> BreakdownKits,
    string PadTyreKit,
    int PadTyreKits,
    JuryRigRules JuryRig,
    int DefaultTarget)
{
    /// <summary>The recipe that makes this item, or null.</summary>
    public FabRecipe? Recipe(string id)
    {
        foreach (var r in Recipes)
            if (r.Id == id) return r;
        return null;
    }

    /// <summary>Extra kits a broken-down crawler's repair takes for this breakdown severity (0 if none listed).</summary>
    public int KitsForBreakdown(string severity)
    {
        foreach (var (s, k) in BreakdownKits)
            if (s == severity) return k;
        return 0;
    }
}

// The parts economy (docs/parts-contract.md): read and checked here; played in Surface/Fabrication.cs.
public sealed partial class GameData
{
    /// <summary>Recipes, the hub workshop, which kit repairs take, jury-rigging and keep-in-stock defaults; null = no parts
    /// economy (repairs then need no kits).</summary>
    public FabricationRules? Fabrication { get; private init; }

    /// <summary>Storage space one unit of a resource takes on a surface.</summary>
    public Fix64 VolumeOf(int resource) => Resources[resource].Volume;

    /// <summary>The highest cap a keep-in-stock target can be set to.</summary>
    public const int MaxStockTarget = 999;

    private static FabricatorDef? ReadFabricator(JsonElement building)
    {
        if (!building.TryGetProperty("fabricator", out var f) || f.ValueKind != JsonValueKind.Object) return null;
        return new FabricatorDef(f.TryGetProperty("lines", out var l) ? l.GetInt32() : 1, f.TryGetProperty("speed", out var s) ? Num(s) : Fix64.One);
    }

    private static FabricationRules? ReadFabrication(JsonElement surfaces, Func<JsonElement, string, string, List<ResourceAmount>> amounts, List<string> problems)
    {
        if (!surfaces.TryGetProperty("fabrication", out var f)) return null;
        var recipes = f.GetProperty("recipes").EnumerateArray().Select(r => new FabRecipe(Str(r, "id"),
            r.TryGetProperty("out", out var o) ? o.GetInt32() : 1,
            amounts(r, "in", $"Fabrication recipe {Str(r, "id")}"), Num(r.GetProperty("seconds")),
            r.TryGetProperty("era_min", out var e0) ? e0.GetString() ?? "" : "",
            r.TryGetProperty("era_max", out var e1) ? e1.GetString() ?? "" : "")).ToList();
        var hub = f.GetProperty("hub");
        var kits = f.GetProperty("kits");
        List<(string, string)> Map(string p) => kits.TryGetProperty(p, out var m)
            ? m.EnumerateObject().Select(x => (x.Name, x.Value.GetString() ?? "")).ToList() : new List<(string, string)>();
        var jury = f.GetProperty("jury_rig");
        return new FabricationRules(recipes,
            new HubWorkshopRules(hub.GetProperty("recipes").EnumerateArray().Select(x => x.GetString()!).ToList(),
                Num(hub.GetProperty("speed")), Num(hub.GetProperty("time_factor"))),
            Num(f.GetProperty("kit_points")),
            Map("buildings"), Map("units"), kits.TryGetProperty("default", out var d) ? d.GetString() ?? "" : "",
            f.TryGetProperty("breakdown_kits", out var bk) ? bk.EnumerateObject().Select(x => (x.Name, x.Value.GetInt32())).ToList() : new List<(string, int)>(),
            f.TryGetProperty("pad_tyre_kit", out var ptk) ? ptk.GetString() ?? "" : "",
            f.TryGetProperty("pad_tyre_kits", out var pt) ? pt.GetInt32() : 0,
            new JuryRigRules(Num(jury.GetProperty("max_condition")), Num(jury.GetProperty("speed")),
                Num(jury.GetProperty("wear_multiplier")), Num(jury.GetProperty("breakdown_multiplier"))),
            f.TryGetProperty("default_target", out var dt) ? dt.GetInt32() : 0);
    }

    /// <summary>The spare kit a building uses for repairs (fabrication kits; buildings that don't wear need none), or null.</summary>
    public string? KitOfBuilding(string buildingId)
    {
        if (Fabrication is not { } f || !_buildings.TryGetValue(buildingId, out var b) || !b.Wears) return null;
        foreach (var (id, kit) in f.BuildingKits)
            if (id == buildingId) return kit;
        return f.DefaultKit.Length > 0 ? f.DefaultKit : null;
    }

    /// <summary>The spare kit a vehicle uses for repairs (vehicles without repair rules need none), or null.</summary>
    public string? KitOfUnit(string unitId)
    {
        if (Fabrication is not { } f || !_surfaceUnits.TryGetValue(unitId, out var u) || u.Repair == null) return null;
        foreach (var (id, kit) in f.UnitKits)
            if (id == unitId) return kit;
        return f.DefaultKit.Length > 0 ? f.DefaultKit : null;
    }

    private readonly Dictionary<string, (List<ResourceAmount> In, Fix64 Seconds)?> _flat = new(StringComparer.Ordinal);

    /// <summary>A recipe's tree flattened to materials: every input that has a recipe of its own replaced by that
    /// recipe's inputs (per item made), and the summed shop seconds of every order in the tree. Null for no recipe
    /// (or a cycle, which Validate reports).</summary>
    public (IReadOnlyList<ResourceAmount> In, Fix64 Seconds)? Flattened(string recipeId)
    {
        if (Fabrication is not { } f || f.Recipe(recipeId) == null) return null;
        lock (_flat) // GameData is shared (tests run in parallel); the cache only ever holds the same answer
        {
            if (_flat.TryGetValue(recipeId, out var done)) return done;
            var bag = new Fix64[Resources.Count];
            var seconds = Fix64.Zero;
            (List<ResourceAmount>, Fix64)? result = null;
            if (Flatten(f, recipeId, Fix64.One, bag, ref seconds, 0))
            {
                var list = new List<ResourceAmount>();
                for (var r = 0; r < bag.Length; r++)
                    if (bag[r] > Fix64.Zero) list.Add(new ResourceAmount(r, bag[r]));
                result = (list, seconds);
            }
            _flat[recipeId] = result;
            return result;
        }
    }

    private bool Flatten(FabricationRules f, string id, Fix64 orders, Fix64[] bag, ref Fix64 seconds, int depth)
    {
        if (depth > 16) return false; // a cycle
        var r = f.Recipe(id)!;
        seconds += r.Seconds * orders;
        foreach (var a in r.In)
        {
            var sub = Resources[a.Resource].Id;
            var subRecipe = f.Recipe(sub);
            if (subRecipe == null) bag[a.Resource] += a.Amount * orders;
            else if (!Flatten(f, sub, a.Amount * orders / Fix64.FromInt(System.Math.Max(1, subRecipe.Output)), bag, ref seconds, depth + 1)) return false;
        }
        return true;
    }

    /// <summary>
    /// Parts-economy checks: recipes make known part / spare / construction items (one recipe each, every item has one),
    /// with positive inputs of known resources (not power), positive seconds and no cycles; the hub's recipes are spare
    /// kits; kit mappings name known buildings / units and spare kits; breakdown kits name known severities; every vehicle
    /// with tyres uses the pad's tyre kit; jury-rig and target numbers in range; volumes not negative; some build-menu
    /// building is a fabricator, and fabricators have lines and speed.
    /// </summary>
    private void ValidateFabrication(List<string> problems)
    {
        foreach (var r in Resources)
            if (r.Volume < Fix64.Zero) problems.Add($"Resource {r.Id}: volume can't be negative.");
        foreach (var b in Buildings)
            if (b.Fabricator is { } fab && (fab.Lines < 1 || fab.Lines > 8 || fab.Speed <= Fix64.Zero))
                problems.Add($"Building {b.Id} fabricator: needs lines 1..8 and a positive speed.");
        if (Fabrication is not { } f)
        {
            if (Buildings.Any(b => b.Fabricator != null)) problems.Add("Fabrication: buildings have a fabricator, but surfaces has no fabrication rules.");
            return;
        }
        bool IsSpare(string id) => _resources.TryGetValue(id, out var r) && r.Tier == "spare";
        var power = _resources.TryGetValue(PowerResource, out var pr) ? pr.Index : -1;
        foreach (var r in f.Recipes)
        {
            var where = $"Fabrication recipe {r.Id}";
            if (!_resources.TryGetValue(r.Id, out var item)) problems.Add($"{where}: makes an unknown resource.");
            else if (!item.IsPartItem) problems.Add($"{where}: {r.Id} is a {item.Tier} resource, not a part, spare or construction part.");
            if (f.Recipes.Count(x => x.Id == r.Id) > 1) problems.Add($"{where}: listed more than once.");
            if (r.Output < 1) problems.Add($"{where}: out must be at least 1.");
            if (r.In.Count == 0 || r.In.Any(a => a.Amount <= Fix64.Zero)) problems.Add($"{where}: needs positive inputs.");
            if (r.In.Any(a => a.Resource == power)) problems.Add($"{where}: {PowerResource} is a power budget, not an input.");
            if (r.Seconds <= Fix64.Zero) problems.Add($"{where}: seconds must be positive.");
            if (Flattened(r.Id) == null && f.Recipe(r.Id) != null) problems.Add($"{where}: its inputs go round in a cycle.");
        }
        foreach (var r in Resources.Where(r => r.IsPartItem))
            if (f.Recipe(r.Id) == null) problems.Add($"Resource {r.Id}: a {r.Tier} item with no fabrication recipe.");
        foreach (var id in f.Hub.Recipes)
            if (f.Recipe(id) == null || !IsSpare(id)) problems.Add($"Fabrication hub: '{id}' must be a recipe of a spare kit.");
        if (f.Hub.Speed <= Fix64.Zero || f.Hub.Speed > Fix64.One || f.Hub.TimeFactor <= Fix64.Zero)
            problems.Add("Fabrication hub: speed must be in (0, 1] and time_factor positive.");
        if (f.KitPoints <= Fix64.Zero || f.KitPoints > Fix64.FromInt(100)) problems.Add("Fabrication: kit_points must be in (0, 100].");
        void Kit(string kit, string where)
        {
            if (!IsSpare(kit) || f.Recipe(kit) == null) problems.Add($"{where}: '{kit}' must be a spare kit with a recipe.");
        }
        foreach (var (id, kit) in f.BuildingKits)
        {
            if (!_buildings.ContainsKey(id)) problems.Add($"Fabrication kits: unknown building '{id}'.");
            Kit(kit, $"Fabrication kits building {id}");
        }
        foreach (var (id, kit) in f.UnitKits)
        {
            if (!_surfaceUnits.ContainsKey(id)) problems.Add($"Fabrication kits: unknown unit '{id}'.");
            Kit(kit, $"Fabrication kits unit {id}");
        }
        if (f.DefaultKit.Length > 0) Kit(f.DefaultKit, "Fabrication kits default");
        foreach (var b in EveryBuildMenu.Where(_buildings.ContainsKey).Select(id => _buildings[id]).Where(b => b.Wears))
            if (KitOfBuilding(b.Id) == null) problems.Add($"Fabrication kits: {b.Id} wears but uses no kit (give it one or a default).");
        foreach (var u in SurfaceUnits.Where(u => u.Repair != null))
            if (KitOfUnit(u.Id) == null) problems.Add($"Fabrication kits: {u.Id} can be repaired but uses no kit.");
        var severities = SurfaceUnits.Where(u => u.Breakdown != null).SelectMany(u => u.Breakdown!.Severities).Select(s => s.Id).ToHashSet();
        foreach (var (s, k) in f.BreakdownKits)
        {
            if (!severities.Contains(s)) problems.Add($"Fabrication breakdown_kits: unknown breakdown severity '{s}'.");
            if (k < 0 || k > 100) problems.Add($"Fabrication breakdown_kits {s}: must be 0..100.");
        }
        if (f.PadTyreKits < 0 || f.PadTyreKits > 100) problems.Add("Fabrication: pad_tyre_kits must be 0..100.");
        if (SurfaceUnits.Any(u => u.Tyres != null))
        {
            Kit(f.PadTyreKit, "Fabrication pad_tyre_kit");
            foreach (var u in SurfaceUnits.Where(u => u.Tyres != null && u.Repair != null))
                if (KitOfUnit(u.Id) != f.PadTyreKit) problems.Add($"Fabrication: {u.Id} has tyres, so it must use the pad's tyre kit '{f.PadTyreKit}'.");
        }
        var j = f.JuryRig;
        if (j.MaxCondition <= Fix64.Zero || j.MaxCondition > Fix64.FromInt(100) || j.Speed <= Fix64.Zero || j.WearMultiplier < Fix64.Zero || j.BreakdownMultiplier < Fix64.Zero)
            problems.Add("Fabrication jury_rig: max_condition must be in (0, 100], speed positive and the multipliers 0 or more.");
        if (f.DefaultTarget < 0 || f.DefaultTarget > MaxStockTarget) problems.Add($"Fabrication: default_target must be 0..{MaxStockTarget}.");
        if (!EveryBuildMenu.Any(id => _buildings.TryGetValue(id, out var b) && b.Fabricator != null))
            problems.Add("Fabrication: no building in the build menu has a fabricator (a Machine Shop).");
        foreach (var b in Buildings.Where(b => b.Fabricator != null && EveryBuildMenu.Contains(b.Id)))
            if (!b.Wears) problems.Add($"Building {b.Id}: a fabricator should wear (it needs spares of its own).");
    }
}
