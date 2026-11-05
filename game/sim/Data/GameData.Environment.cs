using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Planet environments and telemetry rules (surfaces.json environments + telemetry): what the outpost's sensors read
// once its uplink is online. Kept in its own file so the loader and the checks stay together.
public sealed partial class GameData
{
    /// <summary>Environment per planet class (surfaces.json environments), in file order.</summary>
    public IReadOnlyList<EnvironmentDef> Environments { get; private init; } = new List<EnvironmentDef>();

    /// <summary>Hazard limits and terrain effects for the outpost's readings, or null when the data has none.</summary>
    public TelemetryRules? Telemetry { get; private init; }

    /// <summary>The environment of a planet class, or null when the data has none for it.</summary>
    public EnvironmentDef? EnvironmentOf(string planetClassId)
    {
        foreach (var e in Environments)
            if (e.ClassId == planetClassId) return e;
        return null;
    }

    private static List<GasAmount> Gases(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var obj) && obj.ValueKind == JsonValueKind.Object
            ? obj.EnumerateObject().Select(p => new GasAmount(p.Name, Num(p.Value))).ToList()
            : new List<GasAmount>();

    private static List<EnvironmentDef> ReadEnvironments(JsonElement surfacesRoot)
    {
        if (!surfacesRoot.TryGetProperty("environments", out var list)) return new List<EnvironmentDef>();
        return list.EnumerateArray().Select(e =>
        {
            var t = e.GetProperty("temperature_c");
            var hum = e.GetProperty("humidity_pct");
            var wind = e.GetProperty("wind_ms");
            var rad = e.GetProperty("radiation_usv_h");
            return new EnvironmentDef(Str(e, "class"), Str(e, "sky"), Num(e.GetProperty("gravity_g")), Num(e.GetProperty("day_hours")),
                e.GetProperty("day_seconds").GetInt32(), Num(e.GetProperty("relief_km")),
                Num(t.GetProperty("day")), Num(t.GetProperty("night")), Num(t.GetProperty("polar_drop")), Num(t.GetProperty("lapse_per_km")),
                Num(hum[0]), Num(hum[1]), Num(wind.GetProperty("mean")), Num(wind.GetProperty("gust")),
                Num(e.GetProperty("pressure_kpa")), Num(e.GetProperty("scale_height_km")),
                Num(rad.GetProperty("day")), Num(rad.GetProperty("night")), Num(e.GetProperty("uv_index")),
                Gases(e, "atmosphere_pct"), Gases(e, "trace_ppm"), Gases(e, "toxic_ppm"));
        }).ToList();
    }

    private static TelemetryRules? ReadTelemetry(JsonElement surfacesRoot)
    {
        if (!surfacesRoot.TryGetProperty("telemetry", out var t)) return null;
        var o2 = t.GetProperty("breathable_o2_pct");
        var kpa = t.GetProperty("breathable_kpa");
        var terrain = t.TryGetProperty("terrain", out var tr)
            ? tr.EnumerateObject().Select(p => new TerrainWeather(p.Name,
                p.Value.TryGetProperty("swing", out var sw) ? Num(sw) : Fix64.One,
                p.Value.TryGetProperty("offset_c", out var oc) ? Num(oc) : Fix64.Zero,
                p.Value.TryGetProperty("humidity", out var hu) ? Num(hu) : Fix64.One,
                p.Value.TryGetProperty("wind", out var wi) ? Num(wi) : Fix64.One)).ToList()
            : new List<TerrainWeather>();
        return new TelemetryRules(Gases(t, "exposure_limits_ppm"), Num(o2[0]), Num(o2[1]), Num(kpa[0]), Num(kpa[1]),
            Num(t.GetProperty("radiation_warn_usv_h")), Num(t.GetProperty("radiation_danger_usv_h")),
            Num(t.GetProperty("heat_warn_c")), Num(t.GetProperty("cold_warn_c")), Num(t.GetProperty("gust_warn_ms")), terrain);
    }

    /// <summary>Environment and telemetry checks: known classes, every surface planet covered, sane ranges, gases summing to ~100%.</summary>
    private void ValidateEnvironments(List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in Environments)
        {
            var where = $"Environment {e.ClassId}";
            if (!seen.Add(e.ClassId)) problems.Add($"{where}: listed more than once.");
            if (!_planetClasses.ContainsKey(e.ClassId)) problems.Add($"{where}: unknown planet class.");
            if (!IsHexColour(e.Sky)) problems.Add($"{where}: sky must be a colour like \"#86b4e3\".");
            if (e.GravityG <= Fix64.Zero || e.GravityG > Fix64.FromInt(10)) problems.Add($"{where}: gravity_g must be in (0, 10].");
            if (e.DayHours <= Fix64.Zero || e.DaySeconds < 60) problems.Add($"{where}: needs day_hours above 0 and day_seconds of at least 60.");
            if (e.ReliefKm < Fix64.Zero || e.ReliefKm > Fix64.FromInt(30)) problems.Add($"{where}: relief_km must be within 0..30.");
            if (e.NightC > e.DayC) problems.Add($"{where}: the night temperature can't be above the day's.");
            if (e.NightC < Fix64.FromInt(-273) || e.DayC > Fix64.FromInt(1000)) problems.Add($"{where}: temperatures must be within -273..1000 °C.");
            if (e.PolarDropC < Fix64.Zero || e.LapsePerKm < Fix64.Zero || e.LapsePerKm > Fix64.FromInt(20))
                problems.Add($"{where}: polar_drop must be 0 or more and lapse_per_km within 0..20.");
            if (e.NightC - e.PolarDropC - e.LapsePerKm * e.ReliefKm < Fix64.FromInt(-273))
                problems.Add($"{where}: the coldest reading (night, pole, highest ground) would fall below absolute zero.");
            if (e.HumidityMin < Fix64.Zero || e.HumidityMax > Fix64.FromInt(100) || e.HumidityMin > e.HumidityMax)
                problems.Add($"{where}: humidity_pct must be a range within 0..100.");
            if (e.WindMean < Fix64.Zero || e.WindGust < e.WindMean || e.WindGust > Fix64.FromInt(150))
                problems.Add($"{where}: wind_ms needs 0 <= mean <= gust <= 150.");
            if (e.PressureKpa < Fix64.Zero || e.PressureKpa > Fix64.FromInt(100000)) problems.Add($"{where}: pressure_kpa must be within 0..100000.");
            if (e.HasAtmosphere && e.ScaleHeightKm <= Fix64.Zero) problems.Add($"{where}: an atmosphere needs a positive scale_height_km.");
            if (e.RadiationNight < Fix64.Zero || e.RadiationDay < e.RadiationNight) problems.Add($"{where}: radiation needs 0 <= night <= day.");
            if (e.UvIndex < Fix64.Zero || e.UvIndex > Fix64.FromInt(40)) problems.Add($"{where}: uv_index must be within 0..40.");
            if (!e.HasAtmosphere && (e.HumidityMax > Fix64.Zero || e.WindGust > Fix64.Zero))
                problems.Add($"{where}: no atmosphere (pressure 0), so humidity and wind must be 0.");

            var total = Fix64.Zero;
            foreach (var g in e.Atmosphere)
            {
                if (g.Amount <= Fix64.Zero || g.Amount > Fix64.FromInt(100)) problems.Add($"{where}: atmosphere_pct {g.Gas} must be in (0, 100].");
                total += g.Amount;
            }
            if (e.HasAtmosphere && (e.Atmosphere.Count == 0 || Fix64.Abs(total - Fix64.FromInt(100)) > Fix64.One))
                problems.Add($"{where}: atmosphere_pct must add up to 100 (it adds up to {total}).");
            if (!e.HasAtmosphere && e.Atmosphere.Count > 0) problems.Add($"{where}: no atmosphere (pressure 0), so atmosphere_pct must be empty.");
            foreach (var g in e.Trace.Concat(e.Toxic))
                if (g.Amount <= Fix64.Zero || g.Amount > Fix64.FromInt(10000)) problems.Add($"{where}: trace and toxic gas {g.Gas} must be in (0, 10000] ppm.");
            foreach (var g in e.Atmosphere.Concat(e.Trace).Concat(e.Toxic))
                if (!IsGas(g.Gas)) problems.Add($"{where}: '{g.Gas}' doesn't look like a chemical formula.");
        }

        foreach (var s in Surfaces)
            if (EnvironmentOf(s.ClassId) == null) problems.Add($"Surface {s.ClassId}: no environment in surfaces.json environments (the outpost's telemetry needs one).");

        if (Environments.Count > 0 && Telemetry == null) problems.Add("surfaces.json: environments need telemetry rules.");
        if (Telemetry is { } t)
        {
            foreach (var g in t.ExposureLimitsPpm)
                if (g.Amount <= Fix64.Zero || !IsGas(g.Gas)) problems.Add($"Telemetry: exposure limit for '{g.Gas}' must be a positive ppm.");
            if (t.BreathableO2Min <= Fix64.Zero || t.BreathableO2Max <= t.BreathableO2Min || t.BreathableO2Max > Fix64.FromInt(100))
                problems.Add("Telemetry: breathable_o2_pct must be a range within (0, 100].");
            if (t.BreathableKpaMin <= Fix64.Zero || t.BreathableKpaMax <= t.BreathableKpaMin) problems.Add("Telemetry: breathable_kpa must be a positive range.");
            if (t.RadiationWarn <= Fix64.Zero || t.RadiationDanger <= t.RadiationWarn) problems.Add("Telemetry: needs 0 < radiation_warn_usv_h < radiation_danger_usv_h.");
            if (t.ColdWarnC >= t.HeatWarnC) problems.Add("Telemetry: cold_warn_c must be below heat_warn_c.");
            if (t.GustWarn <= Fix64.Zero) problems.Add("Telemetry: gust_warn_ms must be positive.");
            foreach (var w in t.Terrain)
            {
                if (!_terrain.ContainsKey(w.Terrain)) problems.Add($"Telemetry terrain: unknown terrain '{w.Terrain}'.");
                if (w.Swing < Fix64.Zero || w.Humidity < Fix64.Zero || w.Wind < Fix64.Zero) problems.Add($"Telemetry terrain {w.Terrain}: swing, humidity and wind can't be negative.");
            }
        }
    }

    private static bool IsHexColour(string s) =>
        s.Length == 7 && s[0] == '#' && s.Skip(1).All(c => char.IsAsciiHexDigit(c));

    /// <summary>A chemical formula: starts with a capital letter, then letters and digits ("CO2", "H2S", "Na").</summary>
    private static bool IsGas(string s) =>
        s.Length is > 0 and <= 8 && char.IsAsciiLetterUpper(s[0]) && s.All(char.IsAsciiLetterOrDigit);
}
