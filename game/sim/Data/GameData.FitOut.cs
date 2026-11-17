using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Fit Out Your Ship: reading data/missions/fit_out.json (the missions document's "fit_out") and its checks. The shapes
// are in FitOutDefs.cs, the rules in Core/FitOut.cs (FitOutRules). A data set without the block has no fit-outs.
public sealed partial class GameData
{
    /// <summary>The missions document's "fit_out" block, or null when it has none. Tolerant: what is missing or malformed
    /// is reported and read as empty, so the checks below can say what is wrong.</summary>
    private static FitOutData? ReadFitOut(JsonElement root, List<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("fit_out", out var fo)) return null;
        if (fo.ValueKind != JsonValueKind.Object)
        {
            problems.Add("Fit-out: fit_out must be an object.");
            return null;
        }
        string S(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        int I(JsonElement e, string p, string where, int fallback = 0)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v)) return fallback;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
            problems.Add($"{where}: {p} must be a whole number.");
            return fallback;
        }
        Fix64 N(JsonElement e, string p, string where)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && TryFix(v.GetRawText(), out var f)) return f;
            problems.Add($"{where}: needs a number {p}.");
            return Fix64.Zero;
        }
        IEnumerable<JsonElement> List(JsonElement e, string p, string where)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v)) return Array.Empty<JsonElement>();
            if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().ToList();
            problems.Add($"{where}: {p} must be a list.");
            return Array.Empty<JsonElement>();
        }
        IEnumerable<JsonProperty> Fields(JsonElement e, string p, string where)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v)) return Array.Empty<JsonProperty>();
            if (v.ValueKind == JsonValueKind.Object) return v.EnumerateObject().ToList();
            problems.Add($"{where}: {p} must be an object.");
            return Array.Empty<JsonProperty>();
        }
        List<FitOutLeverEffect> Effects(JsonElement e, string p, string where)
        {
            var list = new List<FitOutLeverEffect>();
            foreach (var f in Fields(e, p, where))
            {
                if (f.Value.ValueKind == JsonValueKind.Number && TryFix(f.Value.GetRawText(), out var v)) list.Add(new FitOutLeverEffect(f.Name, v));
                else problems.Add($"{where}: {p} {f.Name} must be a number.");
            }
            return list;
        }
        bool B(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
        List<string> Strings(IEnumerable<JsonElement> items) => items.Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : "").ToList();

        const string w = "Fit-out";
        var rules = fo.TryGetProperty("rules", out var r) && r.ValueKind == JsonValueKind.Object ? r : default;
        if (rules.ValueKind != JsonValueKind.Object) problems.Add($"{w}: needs rules.");

        var divisions = List(fo, "divisions", w).Select(d => new FitOutDivision(S(d, "id"), S(d, "name"), S(d, "who"))).ToList();

        var focuses = new List<FitOutFocus>();
        foreach (var f in List(fo, "focuses", w))
        {
            var id = S(f, "id");
            var pct = new List<int>();
            foreach (var p in List(f, "crew_pct", $"{w} focus {id}"))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) pct.Add(n);
                else problems.Add($"{w} focus {id}: crew_pct must be whole numbers.");
            }
            var division = S(f, "division");
            focuses.Add(new FitOutFocus(id, S(f, "name"), division.Length > 0 ? division : null, S(f, "line"), pct, S(f, "perk"))
            {
                PerkLevers = Effects(f, "perk_levers", $"{w} focus {id}"),
                LandingReveal = I(f, "landing_reveal", $"{w} focus {id}"),
                SpacePerkText = S(f, "space_perk"),
                SpacePerkLevers = Effects(f, "space_perk_levers", $"{w} focus {id}"),
            });
        }

        var levers = List(fo, "levers", w).Select(l => new FitOutLever(S(l, "id"), S(l, "division"), B(l, "more_is_better"), S(l, "name")) { Space = B(l, "space") }).ToList();

        var groups = List(fo, "cargo_groups", w).Select(g => new FitOutCargoGroup(S(g, "id"), S(g, "name"), I(g, "floor", $"{w} cargo group {S(g, "id")}"))
            { SpaceFloor = I(g, "space_floor", $"{w} cargo group {S(g, "id")}") }).ToList();

        var cargo = new List<FitOutCargoLine>();
        foreach (var c in List(fo, "cargo", w))
        {
            var id = S(c, "id");
            var where = $"{w} cargo {id}";
            var stock = new List<FitOutStock>();
            foreach (var f in Fields(c, "stock", where))
            {
                if (f.Value.ValueKind == JsonValueKind.Number && f.Value.TryGetInt32(out var n)) stock.Add(new FitOutStock(f.Name, n));
                else problems.Add($"{where}: stock {f.Name} must be a whole number.");
            }
            cargo.Add(new FitOutCargoLine(id, S(c, "name"), S(c, "group"), I(c, "containers", where), I(c, "min", where), I(c, "max", where), S(c, "text"))
            {
                Stock = stock,
                Unit = S(c, "unit"),
                Satellites = I(c, "satellites", where),
                Research = I(c, "research", where),
                Levers = Effects(c, "levers", where),
                Space = B(c, "space"),
                SystemSatellites = I(c, "system_satellites", where),
                Posts = I(c, "posts", where),
                Probes = I(c, "probes", where),
                ShockPoints = I(c, "shock_points", where),
            });
        }

        var ships = new List<FitOutShip>();
        foreach (var s in List(fo, "ships", w))
        {
            var id = S(s, "ship");
            var where = $"{w} ship {id}";
            var load = new List<FitOutLoad>();
            foreach (var f in Fields(s, "base", where))
            {
                if (f.Value.ValueKind == JsonValueKind.Number && f.Value.TryGetInt32(out var n)) load.Add(new FitOutLoad(f.Name, n));
                else problems.Add($"{where}: base {f.Name} must be a whole number.");
            }
            var fill = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var f in Fields(s, "fill", where))
            {
                if (f.Value.ValueKind == JsonValueKind.Array) fill[f.Name] = Strings(f.Value.EnumerateArray());
                else problems.Add($"{where}: fill {f.Name} must be a list of cargo ids.");
            }
            ships.Add(new FitOutShip(id, I(s, "crew", where), I(s, "hold", where), S(s, "suggest")) { Base = load, Fill = fill });
        }

        return new FitOutData
        {
            CrewFloorShare = N(rules, "crew_floor_share", $"{w} rules"),
            FocusFloorShare = N(rules, "focus_floor_share", $"{w} rules"),
            EffectPerPoint = N(rules, "effect_per_point", $"{w} rules"),
            ReplacesStartStock = Strings(List(rules, "replaces_start_stock", $"{w} rules")),
            Divisions = divisions,
            Focuses = focuses,
            Levers = levers,
            Groups = groups,
            Cargo = cargo,
            Ships = ships,
        };
    }

    /// <summary>The fit-out rules hold together: ids unique and known, floors and budgets consistent, resources and units
    /// exist, and every focus's preset is a legal fit-out on every listed ship.</summary>
    private void ValidateFitOut(List<string> problems)
    {
        if (FitOut is not { } f) return;
        const string w = "Fit-out";
        var before = problems.Count;
        var hundred = Fix64.FromInt(100);

        if (f.CrewFloorShare < Fix64.Zero || f.FocusFloorShare < f.CrewFloorShare || f.FocusFloorShare > Fix64.One)
            problems.Add($"{w} rules: needs 0 <= crew_floor_share <= focus_floor_share <= 1.");
        if (f.EffectPerPoint < Fix64.Zero) problems.Add($"{w} rules: effect_per_point can't be negative.");
        // A whole crew in one division must not turn a lever round (1 - strength stays above 0).
        else if (f.EffectPerPoint * (hundred - f.EvenSharePct) >= Fix64.One) problems.Add($"{w} rules: effect_per_point is so big a lever could reach 0.");
        foreach (var res in f.ReplacesStartStock)
        {
            if (!_resources.TryGetValue(res, out var rd)) problems.Add($"{w} rules: replaces_start_stock: unknown resource '{res}'.");
            else if (!f.Cargo.Any(l => l.Stock.Any(s => s.Resource == res))) problems.Add($"{w} rules: replaces_start_stock: no cargo line gives {rd.Name}, so a fitted-out ship would land with none.");
        }

        if (f.Divisions.Count == 0) problems.Add($"{w}: needs divisions.");
        var divisionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in f.Divisions)
        {
            if (!SnakeId.IsMatch(d.Id) || !divisionIds.Add(d.Id)) problems.Add($"{w} division '{d.Id}': the id must be a unique lower-case id.");
            if (d.Name.Length == 0) problems.Add($"{w} division {d.Id}: needs a name.");
        }

        var leverIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in f.Levers)
        {
            if (!leverIds.Add(l.Id)) problems.Add($"{w} lever '{l.Id}': listed twice.");
            if (!FitOutRules.KnownLevers.Contains(l.Id)) problems.Add($"{w} lever '{l.Id}': the game reads no such lever (it reads {string.Join(", ", FitOutRules.KnownLevers)}).");
            if (l.Division.Length > 0 && !divisionIds.Contains(l.Division)) problems.Add($"{w} lever {l.Id}: unknown division '{l.Division}'.");
            if (l.Name.Length == 0) problems.Add($"{w} lever {l.Id}: needs a name.");
            if (FitOutRules.KnownLevers.Contains(l.Id) && l.Space != FitOutRules.SpaceLevers.Contains(l.Id))
                problems.Add($"{w} lever {l.Id}: {(l.Space ? "the game reads it on the surface, so it can't be a space lever" : "the game reads it in the star system, so it needs \"space\": true")}.");
        }
        bool LeverSpace(string id) => f.Lever(id)?.Space ?? false;

        if (f.Focuses.Count == 0) problems.Add($"{w}: needs focuses.");
        var focusIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in f.Focuses)
        {
            var where = $"{w} focus {x.Id}";
            if (!SnakeId.IsMatch(x.Id) || !focusIds.Add(x.Id)) problems.Add($"{where}: the id must be a unique lower-case id.");
            if (x.Name.Length == 0) problems.Add($"{where}: needs a name.");
            if (x.Division != null && !divisionIds.Contains(x.Division)) problems.Add($"{where}: unknown division '{x.Division}'.");
            if (x.CrewPct.Count != f.Divisions.Count) problems.Add($"{where}: crew_pct needs one share for each of the {f.Divisions.Count} divisions.");
            else if (x.CrewPct.Any(p => p < 0) || x.CrewPct.Sum() != 100) problems.Add($"{where}: crew_pct must be shares of 0 or more that sum to 100.");
            foreach (var (field, effects, space) in new[] { ("perk_levers", x.PerkLevers, false), ("space_perk_levers", x.SpacePerkLevers, true) })
                foreach (var e in effects)
                {
                    if (!leverIds.Contains(e.Lever)) problems.Add($"{where}: {field}: '{e.Lever}' is not one of the levers.");
                    else if (LeverSpace(e.Lever) != space) problems.Add($"{where}: {field}: {e.Lever} is {(space ? "not a space lever" : "a space lever (it goes under space_perk_levers)")}.");
                    if (e.Value <= Fix64.Zero) problems.Add($"{where}: {field} {e.Lever} must be above 0 (a multiplier).");
                }
            if (x.LandingReveal < 0 || x.LandingReveal > 30) problems.Add($"{where}: landing_reveal must be 0 to 30 hexes.");
            if (x.PerkText.Length == 0 && (x.PerkLevers.Count > 0 || x.LandingReveal > 0)) problems.Add($"{where}: it has a perk but no perk text to show.");
            if (x.SpacePerkText.Length == 0 && x.SpacePerkLevers.Count > 0) problems.Add($"{where}: it has a space perk but no space_perk text to show.");
        }

        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in f.Groups)
        {
            if (!groupIds.Add(g.Id)) problems.Add($"{w} cargo group '{g.Id}': listed twice.");
            if (g.Id != "ship" && !divisionIds.Contains(g.Id)) problems.Add($"{w} cargo group '{g.Id}': the id is a division id or \"ship\".");
            if (g.Name.Length == 0) problems.Add($"{w} cargo group {g.Id}: needs a name.");
            if (g.Floor < 0 || g.SpaceFloor < 0) problems.Add($"{w} cargo group {g.Id}: floor and space_floor can't be negative.");
        }

        var lineIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in f.Cargo)
        {
            var where = $"{w} cargo {l.Id}";
            if (!SnakeId.IsMatch(l.Id) || !lineIds.Add(l.Id)) problems.Add($"{where}: the id must be a unique lower-case id.");
            if (l.Name.Length == 0) problems.Add($"{where}: needs a name.");
            if (!groupIds.Contains(l.Group)) problems.Add($"{where}: unknown cargo group '{l.Group}'.");
            if (l.Containers < 1) problems.Add($"{where}: containers must be 1 or more.");
            if (l.Min < 0 || l.Max < l.Min || l.Max > 1000) problems.Add($"{where}: needs 0 <= min <= max (max 1000).");
            foreach (var s in l.Stock)
            {
                if (!_resources.ContainsKey(s.Resource)) problems.Add($"{where}: stock: unknown resource '{s.Resource}'.");
                else if (s.Resource == PowerResource) problems.Add($"{where}: stock: never {s.Resource} (power is a per-base budget).");
                if (s.Amount <= 0) problems.Add($"{where}: stock {s.Resource} must be above 0.");
            }
            if (l.Unit.Length > 0)
            {
                if (!_surfaceUnits.ContainsKey(l.Unit)) problems.Add($"{where}: unknown surface unit '{l.Unit}'.");
                else if (IsAerialUnit(l.Unit)) problems.Add($"{where}: {l.Unit} flies from a pad; it can't land with the colony (load it as stock).");
            }
            if (l.Satellites < 0 || l.Research < 0 || l.SystemSatellites < 0 || l.Posts < 0 || l.Probes < 0) problems.Add($"{where}: satellites, research, system_satellites, posts and probes can't be negative.");
            foreach (var e in l.Levers)
            {
                if (!leverIds.Contains(e.Lever)) problems.Add($"{where}: levers: '{e.Lever}' is not one of the levers.");
                else if (LeverSpace(e.Lever) != l.Space) problems.Add($"{where}: levers: {e.Lever} is {(l.Space ? "not a space lever, and this line is for a ship that never lands" : "a space lever, and this line lands with the colony")}.");
                else if (Fix64.One + e.Value * System.Math.Max(0, l.Max) < FitOutRules.LeastFactor) problems.Add($"{where}: levers {e.Lever}: a full load would take the lever to 0.");
            }
            // What lands with the colony can't ride a ship that never lands, and what acts in the star system only on such a ship.
            if (l.Space && (l.Stock.Count > 0 || l.Unit.Length > 0 || l.Satellites > 0 || l.Research > 0))
                problems.Add($"{where}: a space line never lands, so it can't give stock, a unit, survey satellites or research.");
            if (!l.Space && (l.SystemSatellites > 0 || l.Posts > 0 || l.Probes > 0 || l.ShockPoints != 0))
                problems.Add($"{where}: system_satellites, posts, probes and shock_points are for ships that never land (\"space\": true).");
            if ((long)l.ShockPoints * System.Math.Max(0, l.Max) < -100 || l.ShockPoints > 100) problems.Add($"{where}: shock_points must stay within 100 points for a full load.");
            if (l.Stock.Count == 0 && l.Unit.Length == 0 && l.Satellites == 0 && l.Research == 0 && l.Levers.Count == 0
                && l.SystemSatellites == 0 && l.Posts == 0 && l.Probes == 0 && l.ShockPoints == 0)
                problems.Add($"{where}: it gives nothing (stock, unit, satellites, research, levers, system_satellites, posts, probes or shock_points).");
        }
        foreach (var g in f.Groups)
            foreach (var space in new[] { false, true })
                if (f.LinesFor(space).Where(l => l.Group == g.Id).Sum(l => (long)System.Math.Max(0, l.Max) * l.Containers) < g.FloorFor(space))
                    problems.Add($"{w} cargo group {g.Id}: its {(space ? "space " : "")}lines can't fill its {(space ? "space_floor" : "floor")} of {g.FloorFor(space)} containers.");
        if (Orbit is { } orbit && f.Cargo.Sum(l => (long)l.Satellites * System.Math.Max(0, l.Max)) > orbit.SatelliteMax)
            problems.Add($"{w}: the cargo can carry more ready satellites than a player may have up ({orbit.SatelliteMax}).");
        if (SystemRules is { } system && f.Cargo.Sum(l => (long)l.SystemSatellites * System.Math.Max(0, l.Max)) > system.SatelliteMax)
            problems.Add($"{w}: the cargo can carry more system satellites than a player may place ({system.SatelliteMax}).");
        if (Civilizations is { } civs && f.Cargo.Sum(l => (long)l.Posts * System.Math.Max(0, l.Max)) > civs.Post.Max)
            problems.Add($"{w}: the cargo can carry more listening posts than a player may have hidden ({civs.Post.Max}).");

        if (f.Ships.Count == 0) problems.Add($"{w}: needs at least one ship.");
        var shipIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in f.Ships)
        {
            var where = $"{w} ship {s.ShipId}";
            if (!shipIds.Add(s.ShipId)) problems.Add($"{where}: listed twice.");
            var pick = ShipChoice(s.ShipId);
            if (pick == null) problems.Add($"{where}: no such ship in ship_choice.json.");
            var shipSpace = pick is { Colonizes: false }; // a ship that never lands carries the space lines, a colony ship the others
            if (s.Crew < f.Divisions.Count) problems.Add($"{where}: crew must be at least one a division.");
            if (s.Hold < 1) problems.Add($"{where}: hold must be 1 or more containers.");
            if (!focusIds.Contains(s.Suggest)) problems.Add($"{where}: suggest: unknown focus '{s.Suggest}'.");
            string Kind(bool space) => space ? "a ship that never lands" : "a colony ship";
            foreach (var b in s.Base)
            {
                if (!lineIds.Contains(b.Line)) problems.Add($"{where}: base: unknown cargo '{b.Line}'.");
                else if (pick != null && f.Line(b.Line)!.Space != shipSpace) problems.Add($"{where}: base: {b.Line} is cargo for {Kind(!shipSpace)}.");
                if (b.Count < 0) problems.Add($"{where}: base {b.Line} can't be negative.");
            }
            foreach (var x in f.Focuses)
            {
                if (!s.Fill.TryGetValue(x.Id, out var order)) problems.Add($"{where}: fill has no order for focus {x.Id}.");
                else
                    foreach (var id in order)
                        if (!lineIds.Contains(id)) problems.Add($"{where}: fill {x.Id}: unknown cargo '{id}'.");
                        else if (pick != null && f.Line(id)!.Space != shipSpace) problems.Add($"{where}: fill {x.Id}: {id} is cargo for {Kind(!shipSpace)}.");
            }
            foreach (var id in s.Fill.Keys.OrderBy(k => k, StringComparer.Ordinal))
                if (!focusIds.Contains(id)) problems.Add($"{where}: fill: unknown focus '{id}'.");
        }

        // Every preset must be a fit-out the rules accept (only once the ids above are sound: Preset reads them).
        if (problems.Count != before) return;
        foreach (var s in f.Ships)
            foreach (var x in f.Focuses)
            {
                var preset = FitOutRules.Preset(this, s.ShipId, x.Id);
                var problem = preset == null ? "no preset" : FitOutRules.Problem(this, s.ShipId, preset);
                if (problem != null) problems.Add($"{w} ship {s.ShipId}: the {x.Name} preset is not a legal fit-out: {problem}.");
            }
    }
}
