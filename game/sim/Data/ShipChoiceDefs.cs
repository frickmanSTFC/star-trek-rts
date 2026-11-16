using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// The campaign's ship pick (docs/ship-pick-contract.md, owner 2026-11-16: "the player will decide which ship they are
// going to use ... if they pick a ship that is meant to colonize the planet, they get the colonize mission; any other ship
// gets a different mission and won't goto a planet or body"). Read from data/missions/ship_choice.json ("ships"), checked
// by ValidateShipChoices. The game side is Core/ShipChoice.cs and MatchSetup.CreateSystemGame(..., shipId).

/// <summary>One pickable ship: Id "tereshkova"; Name "Tereshkova"; ClassLabel "Long-range survey vessel"; Role
/// "survey" | "courier" | "patrol" | "diplomacy" | "colony"; Colonizes (lands and plays the colony chain); Chain (the mission
/// chain id it turns on); Crew text; LengthM; Speed (travel-time divisor, 1 = the system's normal speed); Card (res:// path of
/// the card art); Blurb; MissionTitle / MissionSummary (the pick screen's "Your mission" lines); StartBonus (resource → amount
/// added to the landing stock, colonizers only); StartUnits (unit ids that land with the colony, colonizers only).</summary>
public sealed record ShipChoiceDef(string Id, string Name, string ClassLabel, string Role, bool Colonizes, string Chain,
    string Crew, int LengthM, Fix64 Speed, string Card, string Blurb, string MissionTitle, string MissionSummary,
    IReadOnlyDictionary<string, int> StartBonus, IReadOnlyList<string> StartUnits);

public sealed partial class GameData
{
    /// <summary>The ships the campaign start offers, in pick-screen order (empty when the data set has none).</summary>
    public IReadOnlyList<ShipChoiceDef> ShipChoices { get; private init; } = Array.Empty<ShipChoiceDef>();

    /// <summary>The ship with this id, or null.</summary>
    public ShipChoiceDef? ShipChoice(string? id) => id == null ? null : ShipChoices.FirstOrDefault(s => s.Id == id);

    /// <summary>The roles a ship may have.</summary>
    public static readonly IReadOnlyList<string> ShipChoiceRoles = new[] { "colony", "survey", "courier", "patrol", "diplomacy" };

    /// <summary>The ships of data/missions/ship_choice.json (the missions document's "ships"), in file order.</summary>
    private static List<ShipChoiceDef> ReadShipChoices(JsonElement root, List<string> problems)
    {
        var list = new List<ShipChoiceDef>();
        if (!root.TryGetProperty("ships", out var ships)) return list;
        if (ships.ValueKind != JsonValueKind.Array)
        {
            problems.Add("Ship choice: ships must be a list.");
            return list;
        }
        string S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        foreach (var s in ships.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object)
            {
                problems.Add("Ship choice: each ship is an object.");
                continue;
            }
            var id = S(s, "id");
            var speed = s.TryGetProperty("speed", out var sp) && sp.ValueKind == JsonValueKind.Number && TryFix(sp.GetRawText(), out var sv) ? sv : Fix64.Zero;
            var length = s.TryGetProperty("length_m", out var lm) && lm.ValueKind == JsonValueKind.Number && lm.TryGetInt32(out var li) ? li : 0;
            var bonus = new Dictionary<string, int>(StringComparer.Ordinal);
            if (s.TryGetProperty("start_bonus", out var sb) && sb.ValueKind == JsonValueKind.Object)
                foreach (var b in sb.EnumerateObject())
                {
                    if (b.Value.ValueKind == JsonValueKind.Number && b.Value.TryGetInt32(out var n)) bonus[b.Name] = n;
                    else problems.Add($"Ship choice {id}: start_bonus {b.Name} must be a whole number.");
                }
            var units = s.TryGetProperty("start_units", out var su) && su.ValueKind == JsonValueKind.Array
                ? su.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
            list.Add(new ShipChoiceDef(id, S(s, "name"), S(s, "class_label"), S(s, "role"),
                s.TryGetProperty("colonizes", out var c) && c.ValueKind == JsonValueKind.True, S(s, "chain"), S(s, "crew"), length, speed,
                S(s, "card"), S(s, "blurb"), S(s, "mission_title"), S(s, "mission_summary"), bonus, units));
        }
        return list;
    }

    /// <summary>Unique ids, a chain that exists (a colonizer's a colony chain, any other ship's a space chain), bonus
    /// resources and units that exist (colonizers only), speed above 0.</summary>
    private void ValidateShipChoices(List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in ShipChoices)
        {
            var w = $"Ship choice {s.Id}";
            if (!SnakeId.IsMatch(s.Id) || !seen.Add(s.Id)) problems.Add($"{w}: the id must be a unique lower-case id.");
            if (s.Name.Length == 0 || s.ClassLabel.Length == 0) problems.Add($"{w}: needs a name and a class_label.");
            if (!ShipChoiceRoles.Contains(s.Role)) problems.Add($"{w}: role '{s.Role}' is not one of {string.Join(", ", ShipChoiceRoles)}.");
            if (s.Speed <= Fix64.Zero) problems.Add($"{w}: speed must be above 0.");
            if (s.LengthM < 0) problems.Add($"{w}: length_m must be 0 or more.");
            if (Missions?.Chain(s.Chain) is not { } chain) problems.Add($"{w}: unknown mission chain '{s.Chain}'.");
            else if (chain.Space == s.Colonizes)
                problems.Add(s.Colonizes ? $"{w}: a colonizer plays a colony chain, not the space chain {s.Chain}." : $"{w}: a ship that doesn't colonize plays a space chain (rules.json \"space\": true), not {s.Chain}.");
            if (!s.Colonizes && (s.StartBonus.Count > 0 || s.StartUnits.Count > 0)) problems.Add($"{w}: only a colonizer has a start bonus or start units (it never lands).");
            foreach (var (res, n) in s.StartBonus)
            {
                if (!_resources.ContainsKey(res)) problems.Add($"{w}: start_bonus: unknown resource '{res}'.");
                else if (res == PowerResource) problems.Add($"{w}: start_bonus: never {res} (power is a per-base budget).");
                if (n <= 0) problems.Add($"{w}: start_bonus {res} must be above 0.");
            }
            foreach (var u in s.StartUnits)
                if (!_surfaceUnits.ContainsKey(u)) problems.Add($"{w}: start_units: unknown surface unit '{u}'.");
        }
    }
}
