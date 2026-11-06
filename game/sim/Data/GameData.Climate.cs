using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Data;

// Planetary climate (data/climate.json): parse + checks. Kept in its own file so the loader and the checks stay together.
public sealed partial class GameData
{
    /// <summary>Stars, body orbits and spins, climate templates and weather kinds (climate.json), or null when the data has none.</summary>
    public ClimateRules? Climate { get; private init; }

    /// <summary>Snake-case data id of a weather kind ("dust_storm").</summary>
    public static string WeatherId(WeatherKind kind)
    {
        var name = kind.ToString();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    private static readonly WeatherKind[] AllWeather = Enum.GetValues<WeatherKind>();

    private static WeatherKind? WeatherFromId(string id)
    {
        foreach (var k in AllWeather)
            if (WeatherId(k) == id) return k;
        return null;
    }

    /// <summary>{ "clear": 30, "rain": 12 } → one value per WeatherKind (missing = fallback).</summary>
    private static List<Fix64> PerKind(JsonElement parent, string property, Fix64 fallback, string where, List<string> problems)
    {
        var list = AllWeather.Select(_ => fallback).ToList();
        if (!parent.TryGetProperty(property, out var obj) || obj.ValueKind != JsonValueKind.Object) return list;
        foreach (var p in obj.EnumerateObject())
        {
            if (WeatherFromId(p.Name) is { } k) list[(int)k] = Num(p.Value);
            else problems.Add($"{where}: unknown weather kind '{p.Name}'.");
        }
        return list;
    }

    private static List<SeasonDef> ReadSeasons(JsonElement arr, string where, List<string> problems) =>
        arr.EnumerateArray().Select(s =>
        {
            var spell = s.GetProperty("spell_s");
            var name = Str(s, "name");
            return new SeasonDef(name, Num(s.GetProperty("share")), Num(s.GetProperty("temp_c")), Num(s.GetProperty("humidity_pct")),
                Num(s.GetProperty("wind")), spell[0].GetInt32(), spell[1].GetInt32(), PerKind(s, "weights", Fix64.Zero, $"{where} season {name}", problems));
        }).ToList();

    private static ClimateRules? ReadClimate(JsonElement root, List<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("bodies", out var bodiesEl)) return null;

        var stars = root.GetProperty("stars").EnumerateArray()
            .Select(s => new StarTypeDef(Str(s, "class"), Str(s, "name"), Num(s.GetProperty("luminosity")), s.GetProperty("colour_k").GetInt32(), Str(s, "colour")))
            .ToList();

        var bodies = bodiesEl.EnumerateObject().Select(p =>
        {
            var b = p.Value;
            var orbit = b.GetProperty("orbit");
            var spin = b.GetProperty("spin");
            var parent = orbit.TryGetProperty("parent", out var pa) && pa.ValueKind == JsonValueKind.String ? pa.GetString() : null;
            Fix64 Opt(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? Num(v) : Fix64.Zero;
            var lockText = spin.TryGetProperty("locked", out var lk) ? lk.GetString() ?? "none" : "none";
            var locked = lockText switch { "parent" => SpinLock.Parent, "star" => SpinLock.Star, "none" => SpinLock.None, _ => (SpinLock?)null };
            if (locked == null) problems.Add($"Climate body {p.Name}: spin locked must be none, parent or star (not '{lockText}').");
            var direction = spin.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "" : "prograde";
            if (direction is not ("prograde" or "retrograde")) problems.Add($"Climate body {p.Name}: spin direction must be prograde or retrograde.");
            return new BodyClimateDef(p.Name, parent, Opt(orbit, "au"), Opt(orbit, "eccentricity"), Opt(orbit, "perihelion"),
                spin.GetProperty("day_seconds").GetInt32(), Num(spin.GetProperty("day_hours")), direction == "retrograde", locked ?? SpinLock.None,
                Opt(spin, "facing"), Num(b.GetProperty("axial_tilt_deg")), Num(b.GetProperty("year_days")), Opt(b, "year_start"),
                Num(b.GetProperty("albedo")), Num(b.GetProperty("greenhouse_c")), Str(b, "climate"),
                b.TryGetProperty("seasons", out var bs) ? ReadSeasons(bs, $"Climate body {p.Name}", problems) : new List<SeasonDef>(),
                b.TryGetProperty("landable", out var la) && la.GetBoolean(),
                b.TryGetProperty("notes", out var no) ? no.GetString() ?? "" : "");
        }).ToList();

        var classes = root.TryGetProperty("classes", out var cl)
            ? cl.EnumerateObject().Select(p => (p.Name, p.Value.GetString() ?? "")).ToList()
            : new List<(string, string)>();

        var s = root.GetProperty("surface");
        var surface = new ClimateSurfaceRules(Num(s.GetProperty("latitude_span_deg")), Num(s.GetProperty("start_day_fraction")),
            Num(s.GetProperty("twilight_deg")), Num(s.GetProperty("thermal_lag_day")), Num(s.GetProperty("season_lag_year")),
            Num(s.GetProperty("equator_share")), Num(s.GetProperty("snow_below_c")), Num(s.GetProperty("cell_tiles")),
            Num(s.GetProperty("ramp_share")), s.GetProperty("ramp_max_seconds").GetInt32(), s.GetProperty("epoch_seconds").GetInt32(),
            s.GetProperty("calm_start_seconds").GetInt32(), Num(s.GetProperty("drop_ship_max_gust_ms")), Num(s.GetProperty("au_per_map_unit")));

        var templates = root.GetProperty("climates").EnumerateObject().Select(p =>
        {
            var t = p.Value;
            var where = $"Climate {p.Name}";
            return new ClimateTemplate(p.Name, Str(t, "name"), t.GetProperty("air").GetBoolean(), Num(t.GetProperty("wind_scale")),
                Num(t.GetProperty("drift_tiles_per_min")), Num(t.GetProperty("swing_c")), Num(t.GetProperty("polar_drop_c")),
                ReadSeasons(t.GetProperty("seasons"), where, problems),
                t.GetProperty("bands").EnumerateArray().Select(b => new LatitudeBand(Str(b, "name"), Num(b.GetProperty("to_deg")),
                    Num(b.GetProperty("season_scale")), Num(b.GetProperty("wind_toward_deg")),
                    PerKind(b, "weights", Fix64.One, $"{where} band {Str(b, "name")}", problems))).ToList());
        }).ToList();

        var weather = new List<WeatherDef>();
        foreach (var w in root.GetProperty("weather").EnumerateArray())
        {
            var id = Str(w, "id");
            if (WeatherFromId(id) is not { } kind)
            {
                problems.Add($"Weather '{id}': not a weather kind (one of {string.Join(", ", AllWeather.Select(WeatherId))}).");
                continue;
            }
            var precip = w.GetProperty("precip_mm_h");
            var wind = w.GetProperty("wind_ms");
            var e = w.GetProperty("effects");
            weather.Add(new WeatherDef(kind, id, Str(w, "name"), Num(w.GetProperty("cloud")), Num(w.GetProperty("visibility")),
                Num(precip[0]), Num(precip[1]), Num(wind[0]), Num(wind[1]), Num(w.GetProperty("gust")), Num(w.GetProperty("humidity_min_pct")),
                Num(e.GetProperty("rover_speed")), e.GetProperty("survey_radius_delta").GetInt32(), Num(e.GetProperty("rover_damage")),
                e.GetProperty("drop_ships_grounded").GetBoolean(), Num(e.GetProperty("solar"))));
        }

        var terrain = root.TryGetProperty("terrain", out var te)
            ? te.EnumerateArray().Select(t =>
            {
                var id = Str(t, "id");
                var kinds = new List<WeatherKind>();
                foreach (var k in t.GetProperty("weather").EnumerateArray())
                {
                    if (WeatherFromId(k.GetString() ?? "") is { } wk) kinds.Add(wk);
                    else problems.Add($"Climate terrain effect {id}: unknown weather kind '{k.GetString()}'.");
                }
                return new TerrainWeatherEffect(id, Str(t, "name"), kinds, t.GetProperty("terrain").EnumerateArray().Select(x => x.GetString()!).ToList(),
                    Num(t.GetProperty("rover_speed")), Num(t.GetProperty("rover_damage")));
            }).ToList()
            : new List<TerrainWeatherEffect>();

        return new ClimateRules(stars, bodies, classes, surface, templates, weather, terrain);
    }

    /// <summary>
    /// The body a surface of this planet class stands for when nothing says otherwise: the home body of the lowest player
    /// slot (the local player in the planet-first MVP) in the first map whose home is of this class (mvp_duel: Tarsis);
    /// else the first body of the class in any map; else null (the class's climate template with a stand-in orbit).
    /// </summary>
    public string? DefaultClimateBody(string planetClassId)
    {
        foreach (var m in Maps)
            foreach (var start in m.Starts.OrderBy(s => s.Slot))
            {
                var home = m.Bodies.FirstOrDefault(b => b.Id == start.HomeBodyId);
                if (home != null && home.ClassId == planetClassId) return home.Id;
            }
        foreach (var m in Maps)
            foreach (var b in m.Bodies)
                if (b.ClassId == planetClassId) return b.Id;
        return null;
    }

    /// <summary>Climate checks: every map body covered, references resolve, weights and shares sane, ranges physical.</summary>
    private void ValidateClimate(List<string> problems)
    {
        if (Climate is not { } c)
        {
            problems.Add("climate.json: no climate data (stars, bodies, climates, weather).");
            return;
        }
        var zero = Fix64.Zero;
        var one = Fix64.One;

        // Stars.
        var starClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in c.Stars)
        {
            if (!starClasses.Add(s.Class)) problems.Add($"Star class {s.Class}: listed more than once.");
            if (s.Luminosity <= zero || s.Luminosity > Fix64.FromInt(1_000_000)) problems.Add($"Star class {s.Class}: luminosity must be in (0, 1000000].");
            if (s.ColourK < 1000 || s.ColourK > 60000) problems.Add($"Star class {s.Class}: colour_k must be within 1000..60000.");
            if (!IsHexColour(s.Colour)) problems.Add($"Star class {s.Class}: colour must be like \"#fff4ea\".");
        }
        foreach (var m in Maps)
            if (c.Star(m.Star.Class) == null) problems.Add($"Map {m.Id}: star class '{m.Star.Class}' has no entry in climate.json stars.");

        // Weather kinds.
        foreach (var k in AllWeather)
        {
            var n = c.Weather.Count(w => w.Kind == k);
            if (n != 1) problems.Add($"Weather '{WeatherId(k)}': must be listed exactly once in climate.json weather (it is listed {n} times).");
        }
        foreach (var w in c.Weather)
        {
            var where = $"Weather {w.Id}";
            if (w.Cloud < zero || w.Cloud > one || w.Visibility <= zero || w.Visibility > one) problems.Add($"{where}: cloud must be within 0..1 and visibility within (0, 1].");
            if (w.PrecipMin < zero || w.PrecipMax < w.PrecipMin || w.PrecipMax > Fix64.FromInt(200)) problems.Add($"{where}: precip_mm_h must be a range within 0..200.");
            if (w.WindMin < zero || w.WindMax < w.WindMin || w.WindMax > Fix64.FromInt(100)) problems.Add($"{where}: wind_ms must be a range within 0..100.");
            if (w.Gust < one || w.Gust > Fix64.FromInt(4)) problems.Add($"{where}: gust must be within 1..4 (gust / wind).");
            if (w.HumidityMinPct < zero || w.HumidityMinPct > Fix64.FromInt(100)) problems.Add($"{where}: humidity_min_pct must be within 0..100.");
            if (w.RoverSpeed <= zero || w.RoverSpeed > Fix64.Parse("1.5")) problems.Add($"{where}: rover_speed must be in (0, 1.5].");
            if (w.RoverDamage < zero || w.RoverDamage > Fix64.FromInt(5)) problems.Add($"{where}: rover_damage must be within 0..5.");
            if (w.SurveyRadiusDelta is < -10 or > 10) problems.Add($"{where}: survey_radius_delta must be within -10..10.");
            if (w.Solar < zero || w.Solar > Fix64.Parse("1.5")) problems.Add($"{where}: solar must be within 0..1.5.");
        }
        if (c.Of(WeatherKind.Clear).DropShipsGrounded) problems.Add("Weather clear: can't ground drop ships (the first landing could never happen).");
        foreach (var t in c.Terrain)
        {
            foreach (var id in t.Terrain)
                if (!_terrain.ContainsKey(id)) problems.Add($"Climate terrain effect {t.Id}: unknown terrain '{id}'.");
            if (t.Weather.Count == 0 || t.Terrain.Count == 0) problems.Add($"Climate terrain effect {t.Id}: needs weather kinds and terrain.");
            if (t.RoverSpeed <= zero || t.RoverSpeed > Fix64.Parse("1.5") || t.RoverDamage < zero || t.RoverDamage > Fix64.FromInt(5))
                problems.Add($"Climate terrain effect {t.Id}: rover_speed must be in (0, 1.5] and rover_damage within 0..5.");
        }

        // Surface rules.
        var sr = c.Surface;
        if (sr.LatitudeSpanDeg <= zero || sr.LatitudeSpanDeg > Fix64.FromInt(90)) problems.Add("Climate surface: latitude_span_deg must be in (0, 90].");
        if (sr.StartDayFraction < zero || sr.StartDayFraction >= one) problems.Add("Climate surface: start_day_fraction must be within [0, 1).");
        if (sr.TwilightDeg < zero || sr.TwilightDeg > Fix64.FromInt(18)) problems.Add("Climate surface: twilight_deg must be within 0..18.");
        if (sr.ThermalLagDay < zero || sr.ThermalLagDay > Fix64.Half || sr.SeasonLagYear < zero || sr.SeasonLagYear > Fix64.Half)
            problems.Add("Climate surface: thermal_lag_day and season_lag_year must be within 0..0.5.");
        if (sr.EquatorShare < zero || sr.EquatorShare > one) problems.Add("Climate surface: equator_share must be within 0..1.");
        if (sr.CellTiles < Fix64.FromInt(2) || sr.CellTiles > Fix64.FromInt(200)) problems.Add("Climate surface: cell_tiles must be within 2..200.");
        if (sr.RampShare < zero || sr.RampShare > Fix64.Half || sr.RampMaxSeconds < 0) problems.Add("Climate surface: ramp_share must be within 0..0.5 and ramp_max_seconds 0 or more.");
        if (sr.EpochSeconds < 60) problems.Add("Climate surface: epoch_seconds must be at least 60.");
        if (sr.CalmStartSeconds < 0) problems.Add("Climate surface: calm_start_seconds can't be negative.");
        if (sr.DropShipMaxGustMs <= zero) problems.Add("Climate surface: drop_ship_max_gust_ms must be positive.");
        if (sr.AuPerMapUnit <= zero) problems.Add("Climate surface: au_per_map_unit must be positive.");

        // Templates.
        var templateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in c.Templates)
        {
            var where = $"Climate {t.Id}";
            if (!templateIds.Add(t.Id)) problems.Add($"{where}: listed more than once.");
            if (t.WindScale < zero || t.WindScale > Fix64.FromInt(20)) problems.Add($"{where}: wind_scale must be within 0..20.");
            if (t.SwingC < zero || t.PolarDropC < zero) problems.Add($"{where}: swing_c and polar_drop_c can't be negative.");
            ValidateSeasons(t.Seasons, where, t.Air, sr, problems);
            if (t.Bands.Count == 0) problems.Add($"{where}: needs at least one latitude band.");
            var last = zero;
            foreach (var b in t.Bands)
            {
                if (b.ToDeg <= last) problems.Add($"{where} band {b.Name}: to_deg must rise from band to band.");
                last = b.ToDeg;
                if (b.SeasonScale < zero || b.SeasonScale > Fix64.FromInt(5)) problems.Add($"{where} band {b.Name}: season_scale must be within 0..5.");
                if (b.WindTowardDeg < zero || b.WindTowardDeg >= Fix64.FromInt(360)) problems.Add($"{where} band {b.Name}: wind_toward_deg must be within 0..359.");
                if (b.Multipliers.Any(x => x < zero)) problems.Add($"{where} band {b.Name}: weight multipliers can't be negative.");
            }
            if (t.Bands.Count > 0 && t.Bands[^1].ToDeg < Fix64.FromInt(90)) problems.Add($"{where}: the last band must reach to_deg 90.");
        }
        foreach (var (classId, climateId) in c.Classes)
        {
            if (!_planetClasses.ContainsKey(classId)) problems.Add($"Climate classes: unknown planet class '{classId}'.");
            if (c.Template(climateId) == null) problems.Add($"Climate classes {classId}: unknown climate '{climateId}'.");
        }
        foreach (var s in Surfaces)
            if (c.ClassClimate(s.ClassId) == null) problems.Add($"Surface {s.ClassId}: no climate template in climate.json classes.");

        // Bodies: every map body listed, references resolve, numbers physical.
        var mapBodies = Maps.SelectMany(m => m.Bodies).ToList();
        foreach (var b in mapBodies)
            if (c.Body(b.Id) == null) problems.Add($"Map body {b.Id} ({b.Name}): no entry in climate.json bodies.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in c.Bodies)
        {
            var where = $"Climate body {b.Id}";
            if (!seen.Add(b.Id)) problems.Add($"{where}: listed more than once.");
            var onMap = mapBodies.FirstOrDefault(m => m.Id == b.Id);
            if (onMap == null) problems.Add($"{where}: not a body on any map (maps.json).");
            var template = c.Template(b.ClimateId);
            if (template == null) problems.Add($"{where}: unknown climate '{b.ClimateId}'.");
            if (b.Seasons.Count > 0) ValidateSeasons(b.Seasons, where, template?.Air ?? true, sr, problems);

            if (onMap != null && onMap.Parent != b.Parent)
                problems.Add($"{where}: orbit parent must match maps.json ({onMap.Parent ?? "none"}).");
            if (b.Parent != null)
            {
                var parent = c.Body(b.Parent);
                if (parent == null) problems.Add($"{where}: unknown parent '{b.Parent}'.");
                else if (parent.IsMoon) problems.Add($"{where}: its parent can't be a moon.");
                else if (Fix64.Abs(b.YearSeconds - parent.YearSeconds) > parent.YearSeconds / 50)
                    problems.Add($"{where}: a moon's year (year_days × day_seconds = {b.YearSeconds} s) must match its planet's ({parent.YearSeconds} s) within 2%.");
            }
            else
            {
                if (b.Au < Fix64.Ratio(1, 100) || b.Au > Fix64.FromInt(200)) problems.Add($"{where}: orbit au must be within 0.01..200.");
                if (b.Eccentricity < zero || b.Eccentricity >= Fix64.Parse("0.9")) problems.Add($"{where}: eccentricity must be within [0, 0.9).");
                if (b.Perihelion < zero || b.Perihelion >= one) problems.Add($"{where}: perihelion must be a year fraction within [0, 1).");
                if (b.Locked == SpinLock.Parent) problems.Add($"{where}: only a moon can be locked to its parent.");
            }
            if (b.DaySeconds < 30) problems.Add($"{where}: day_seconds must be at least 30.");
            if (b.DayHours <= zero || b.DayHours > Fix64.FromInt(100000)) problems.Add($"{where}: day_hours must be in (0, 100000].");
            if (b.Locked == SpinLock.Star && b.YearDays != one) problems.Add($"{where}: locked to the star, so its year is one day (year_days 1).");
            if (b.Facing < zero || b.Facing >= one) problems.Add($"{where}: facing must be a share of the map width within [0, 1).");
            if (b.AxialTiltDeg < zero || b.AxialTiltDeg > Fix64.FromInt(90)) problems.Add($"{where}: axial_tilt_deg must be within 0..90 (use direction retrograde for a backwards spin).");
            if (b.YearDays <= zero || b.YearDays > Fix64.FromInt(100000)) problems.Add($"{where}: year_days must be in (0, 100000].");
            if (b.YearStart < zero || b.YearStart >= one) problems.Add($"{where}: year_start must be within [0, 1).");
            if (b.Albedo < zero || b.Albedo >= one) problems.Add($"{where}: albedo must be within [0, 1).");
            if (b.GreenhouseC < Fix64.FromInt(-100) || b.GreenhouseC > Fix64.FromInt(800)) problems.Add($"{where}: greenhouse_c must be within -100..800.");

            if (onMap != null && template != null)
            {
                var env = EnvironmentOf(onMap.ClassId);
                if (env != null && env.HasAtmosphere != template.Air)
                    problems.Add($"{where}: climate '{template.Id}' {(template.Air ? "has weather but the class has no atmosphere" : "has no weather but the class has an atmosphere")} (surfaces.json environments).");
            }
        }
        foreach (var m in Maps)
            foreach (var s in m.Starts)
                if (c.Body(s.HomeBodyId) is { Landable: false }) problems.Add($"Map {m.Id}: home body {s.HomeBodyId} must be landable in climate.json.");
    }

    private static void ValidateSeasons(IReadOnlyList<SeasonDef> seasons, string where, bool air, ClimateSurfaceRules sr, List<string> problems)
    {
        if (seasons.Count == 0)
        {
            problems.Add($"{where}: needs at least one season.");
            return;
        }
        var total = Fix64.Zero;
        foreach (var s in seasons)
        {
            var w = $"{where} season {s.Name}";
            total += s.Share;
            if (s.Share <= Fix64.Zero) problems.Add($"{w}: share must be positive.");
            if (Fix64.Abs(s.TempC) > Fix64.FromInt(100) || Fix64.Abs(s.HumidityPct) > Fix64.FromInt(100)) problems.Add($"{w}: temp_c and humidity_pct must be within -100..100.");
            if (s.Wind < Fix64.Zero || s.Wind > Fix64.FromInt(10)) problems.Add($"{w}: wind must be within 0..10.");
            if (s.SpellMinSeconds < 10 || s.SpellMaxSeconds < s.SpellMinSeconds) problems.Add($"{w}: spell_s needs 10 <= min <= max.");
            if (s.SpellMaxSeconds > sr.EpochSeconds) problems.Add($"{w}: spell_s max can't be longer than epoch_seconds ({sr.EpochSeconds}).");
            if (s.Weights.Any(x => x < Fix64.Zero)) problems.Add($"{w}: weather weights can't be negative.");
            var sum = Fix64.Zero;
            foreach (var x in s.Weights) sum += x;
            if (sum <= Fix64.Zero) problems.Add($"{w}: weather weights need a positive sum.");
            if (!air)
                for (var k = 1; k < s.Weights.Count; k++)
                    if (s.Weights[k] > Fix64.Zero) problems.Add($"{w}: no air, so only clear weather (weight for {WeatherId((WeatherKind)k)}).");
        }
        if (Fix64.Abs(total - Fix64.One) > Fix64.Ratio(1, 1000)) problems.Add($"{where}: season shares must add up to 1 (they add up to {total}).");
    }
}
