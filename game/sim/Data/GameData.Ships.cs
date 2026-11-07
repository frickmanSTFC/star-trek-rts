using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A ships.json role (ships/rules.json "roles"): what a class is for. Civilian = a civilian or support ship.
/// CostScale/BuildScale/CrewScale are the formula's multipliers for the role (tools/ship_costs.py applies them);
/// Tech is a tech_tree.json id the role needs, or null.</summary>
public sealed record ShipRoleDef(string Id, string Label, bool Civilian, Fix64 CostScale, Fix64 BuildScale, Fix64 CrewScale, string? Tech);

// Ship classes' build cost, build time, crew cost and tech gate (ships/classes/*.json, roles in ships/rules.json): read and
// checked here. Nothing in the sim builds ships yet; the checks keep the data ready for the shipyard step.
public sealed partial class GameData
{
    /// <summary>The role of a class that lists none.</summary>
    public const string CombatRole = "combat";

    /// <summary>The fields every ship class must have (requires_tech may be null; tools/ship_costs.py writes them).</summary>
    public static readonly IReadOnlyList<string> ShipClassFields = new[] { "cost", "build_seconds", "crew_cost", "requires_tech" };

    /// <summary>ships.json roles by id (file order).</summary>
    public IReadOnlyDictionary<string, ShipRoleDef> ShipRoles { get; private init; } = new Dictionary<string, ShipRoleDef>();

    private static List<ShipClassDef> ReadShipClasses(JsonElement ships, Func<JsonElement, string, string, List<ResourceAmount>> amounts,
        List<string> problems)
    {
        string? OptId(JsonElement c, string property) =>
            c.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var list = new List<ShipClassDef>();
        foreach (var c in ships.GetProperty("classes").EnumerateArray())
        {
            var def = new ShipClassDef(Str(c, "faction"), Str(c, "name"), Str(c, "hull"), Str(c, "era"), Str(c, "source"));
            var where = $"Ship class {def.Key}";
            foreach (var field in ShipClassFields)
                if (!c.TryGetProperty(field, out _)) problems.Add($"{where}: missing '{field}' (run tools/ship_costs.py --write).");
            foreach (var field in new[] { "requires_tech", "requires_assimilated" })
                if (c.TryGetProperty(field, out var v) && v.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    problems.Add($"{where}: {field} must be a tech id or null.");
            var buildOk = c.TryGetProperty("build_seconds", out var bs) && bs.ValueKind == JsonValueKind.Number;
            var crewOk = c.TryGetProperty("crew_cost", out var cc) && cc.ValueKind == JsonValueKind.Number && cc.TryGetInt32(out _);
            if (c.TryGetProperty("crew_cost", out _) && !crewOk) problems.Add($"{where}: crew_cost must be a whole number.");
            list.Add(def with
            {
                Role = OptId(c, "role") ?? CombatRole,
                Cost = amounts(c, "cost", where),
                BuildSeconds = buildOk ? Num(bs) : Fix64.Zero,
                CrewCost = crewOk ? cc.GetInt32() : 0,
                RequiresTech = OptId(c, "requires_tech"),
                RequiresAssimilated = OptId(c, "requires_assimilated"),
            });
        }
        return list;
    }

    private static Dictionary<string, ShipRoleDef> ReadShipRoles(JsonElement ships)
    {
        var roles = new Dictionary<string, ShipRoleDef>(StringComparer.Ordinal);
        if (!ships.TryGetProperty("roles", out var table) || table.ValueKind != JsonValueKind.Object) return roles;
        Fix64 Scale(JsonElement r, string p) => r.TryGetProperty(p, out var v) ? Num(v) : Fix64.One;
        foreach (var p in table.EnumerateObject())
        {
            var r = p.Value;
            roles[p.Name] = new ShipRoleDef(p.Name, r.TryGetProperty("label", out var l) ? l.GetString() ?? p.Name : p.Name,
                r.TryGetProperty("civilian", out var civ) && civ.ValueKind == JsonValueKind.True,
                Scale(r, "cost"), Scale(r, "build"), Scale(r, "crew"),
                r.TryGetProperty("tech", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null);
        }
        return roles;
    }

    private void ValidateShips(List<string> problems)
    {
        if (!ShipRoles.ContainsKey(CombatRole)) problems.Add($"ships.json roles: needs '{CombatRole}' (the role of a class that lists none).");
        foreach (var r in ShipRoles.Values)
            if (r.CostScale <= Fix64.Zero || r.BuildScale <= Fix64.Zero || r.CrewScale <= Fix64.Zero)
                problems.Add($"ships.json role {r.Id}: cost, build and crew must be above 0.");

        foreach (var c in ShipClasses)
        {
            var where = $"Ship class {c.Key}";
            if (!ShipRoles.ContainsKey(c.Role)) problems.Add($"{where}: unknown role '{c.Role}'.");
            if (_hulls.TryGetValue(c.HullId, out var hull) && hull.Roles.Count > 0 && !hull.Roles.Contains(c.Role))
                problems.Add($"{where}: hull {hull.Id} takes the roles {string.Join(", ", hull.Roles)}, not '{c.Role}'.");
            if (c.Cost.Count == 0) problems.Add($"{where}: needs a cost.");
            if (c.Cost.Any(a => a.Amount <= Fix64.Zero)) problems.Add($"{where}: cost amounts must be above 0.");
            foreach (var a in c.Cost)
            {
                var id = Resources[a.Resource].Id;
                if (id == PowerResource) problems.Add($"{where}: cost lists {id}, which is a power budget, not a stockpile.");
                if (id == "crew") problems.Add($"{where}: cost lists crew; use crew_cost.");
            }
            if (c.BuildSeconds <= Fix64.Zero) problems.Add($"{where}: build_seconds must be above 0.");
            if (c.CrewCost < 1) problems.Add($"{where}: crew_cost must be at least 1.");
            if (c.RequiresTech != null && c.RequiresAssimilated != null)
                problems.Add($"{where}: has both requires_tech and requires_assimilated (one gate only).");
        }
    }
}
