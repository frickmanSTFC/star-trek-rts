using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The outpost's telemetry as the helm shows it (render-only): keeps a short history of the sim's readings
/// (<see cref="Telemetry.Read"/>, one sample every <see cref="SampleSeconds"/>) for trend arrows and a temperature
/// sparkline, and turns readings into helm sections (ATMOSPHERE / WEATHER / RADIATION), a compact block for the Planet
/// display, the feed's ticker line and hazard texts.
/// </summary>
public sealed class SurfaceTelemetry
{
    public const float SampleSeconds = 4f;
    private const int HistoryLength = 16;

    private static readonly Color Warn = HelmScreen.Peach;
    private static readonly Color Danger = HelmScreen.Red;
    private static readonly Color Good = HelmScreen.Green;

    private readonly List<Sample> _history = new();
    private double _lastSample = double.NegativeInfinity;

    private readonly record struct Sample(float Temperature, float Humidity, float Wind, float Pressure, float Radiation);

    /// <summary>Adds a sample when enough time has passed (clock = real seconds).</summary>
    public void Record(SurfaceReadings r, double clock)
    {
        if (clock - _lastSample < SampleSeconds) return;
        _lastSample = clock;
        _history.Add(new Sample(r.TemperatureC.ToFloat(), r.HumidityPct.ToFloat(), r.WindMs.ToFloat(), r.PressureKpa.ToFloat(), r.RadiationUsvH.ToFloat()));
        if (_history.Count > HistoryLength) _history.RemoveAt(0);
    }

    public void Clear()
    {
        _history.Clear();
        _lastSample = double.NegativeInfinity;
    }

    // ------------------------------------------------------------------ helm sections

    /// <summary>The Atmosphere display's telemetry sections (or one AWAITING section with the uplink's status).</summary>
    public List<HelmSection> Sections(SurfaceReadings? r, string status)
    {
        if (r == null)
            return new List<HelmSection> { new("SURFACE SENSORS", HelmScreen.Lavender, new List<HelmRow> { new("STATUS", status, HelmScreen.Peach) }) };

        var air = new List<HelmRow>();
        var breathable = !r.Hazards.Any(h => h.Kind is HazardKind.Unbreathable or HazardKind.Vacuum);
        if (r.Sky == SkyCondition.Vacuum) air.Add(new HelmRow("PRESSURE", "none · vacuum", Danger));
        else
        {
            air.Add(new HelmRow("PRESSURE", $"{Kpa(r.PressureKpa.ToFloat())}{Trend(s => s.Pressure, 0.05f)}"));
            var o2 = r.Gases.FirstOrDefault(g => g.Gas == "O2");
            if (o2 != null) air.Add(new HelmRow("O2", $"{Pct(o2.Value.ToFloat())} · {Kpa(r.O2PartialKpa.ToFloat())}", breathable ? null : Danger));
            foreach (var g in r.Gases.Where(g => g.Percent && g.Gas != "O2").Take(2))
                air.Add(new HelmRow(g.Gas, Pct(g.Value.ToFloat()), g.OverLimit ? Danger : null));
            var rest = r.Gases.Where(g => g.Percent && g.Gas != "O2").Skip(2).ToList();
            if (rest.Count > 0) air.Add(new HelmRow("OTHER", string.Join(" · ", rest.Select(g => $"{g.Gas} {Pct(g.Value.ToFloat())}")), rest.Any(g => g.OverLimit) ? Danger : null));
        }
        var trace = r.Gases.Where(g => !g.Percent).ToList();
        if (trace.Count > 0)
            air.Add(new HelmRow(r.Sky == SkyCondition.Vacuum ? "EXOSPHERE" : "TRACE", string.Join(" · ", trace.Select(g => $"{g.Gas} {Ppm(g.Ppm.ToFloat())}")) + " ppm",
                trace.Any(g => g.OverLimit) ? Danger : null));
        air.Add(new HelmRow("BREATHABLE", breathable ? "yes" : r.Sky == SkyCondition.Vacuum ? "no · pressure suits" : "no · suits or masks", breathable ? Good : Danger));

        var weather = new List<HelmRow>
        {
            new("TEMP", $"{Celsius(r.TemperatureC.ToFloat())}{Trend(s => s.Temperature, 0.1f)}", Hot(r)),
            new("TREND", Sparkline(), HelmScreen.Cyan),
        };
        if (r.Sky != SkyCondition.Vacuum)
        {
            weather.Add(new HelmRow("HUMIDITY", $"{r.HumidityPct.ToFloat():0} %{Trend(s => s.Humidity, 0.5f)}"));
            weather.Add(new HelmRow("WIND", $"{CompassPoint(r.WindFromDeg)} {r.WindMs.ToFloat():0.0} m/s · gusts {r.GustMs.ToFloat():0}{Trend(s => s.Wind, 0.3f)}",
                r.Hazards.Any(h => h.Kind == HazardKind.HighWinds) ? Warn : null));
            weather.Add(new HelmRow("SKY", $"{SkyName(r.Sky)} · cloud {r.CloudCover.ToFloat() * 100f:0} %", r.Sky == SkyCondition.DustStorm ? Warn : null));
        }
        weather.Add(new HelmRow("LOCAL TIME", $"{Clock(r.LocalHours.ToFloat())} · {(r.Daylight ? "day" : "night")}"));

        var radiation = new List<HelmRow>();
        var rad = r.Hazards.FirstOrDefault(h => h.Kind == HazardKind.Radiation);
        radiation.Add(new HelmRow("DOSE", $"{Dose(r.RadiationUsvH.ToFloat())}{Trend(s => s.Radiation, 0.02f, true)}", rad == null ? null : rad.Severe ? Danger : Warn));
        radiation.Add(new HelmRow("UV INDEX", $"{r.UvIndex.ToFloat():0.0}", r.UvIndex.ToFloat() >= 8 ? Warn : null));
        radiation.Add(new HelmRow("GRAVITY", $"{r.GravityG.ToFloat():0.00} g · alt {r.AltitudeKm.ToFloat():0.0} km"));

        return new List<HelmSection>
        {
            new("ATMOSPHERE", HelmScreen.Sky, air),
            new("WEATHER", HelmScreen.Lavender, weather),
            new("RADIATION", HelmScreen.Peach, radiation),
        };
    }

    /// <summary>A short block for the Planet display: the outpost's weather at a glance (or the uplink's status).</summary>
    public HelmSection Glance(SurfaceReadings? r, string status)
    {
        if (r == null) return new HelmSection("OUTPOST SENSORS", HelmScreen.Lavender, new List<HelmRow> { new("STATUS", status, HelmScreen.Peach) });
        var rows = new List<HelmRow>
        {
            new("TEMP", $"{Celsius(r.TemperatureC.ToFloat())}{Trend(s => s.Temperature, 0.1f)} · {Clock(r.LocalHours.ToFloat())}", Hot(r)),
        };
        if (r.Sky != SkyCondition.Vacuum)
            rows.Add(new HelmRow("WEATHER", $"{SkyName(r.Sky)} · {CompassPoint(r.WindFromDeg)} {r.WindMs.ToFloat():0} m/s"));
        var worst = r.Hazards.OrderByDescending(h => h.Severe).FirstOrDefault();
        rows.Add(worst == null ? new HelmRow("HAZARDS", "none", Good) : new HelmRow("HAZARDS", $"{r.Hazards.Count} · {ShortHazard(worst)}", worst.Severe ? Danger : Warn));
        return new HelmSection("OUTPOST SENSORS", HelmScreen.Lavender, rows);
    }

    /// <summary>The ticker along the bottom of the video feed.</summary>
    public static string Ticker(SurfaceReadings r)
    {
        var parts = new List<string> { Celsius(r.TemperatureC.ToFloat()) };
        if (r.Sky != SkyCondition.Vacuum)
        {
            parts.Add($"RH {r.HumidityPct.ToFloat():0} %");
            parts.Add($"WIND {CompassPoint(r.WindFromDeg)} {r.WindMs.ToFloat():0} M/S");
            parts.Add(Kpa(r.PressureKpa.ToFloat()).ToUpperInvariant());
        }
        parts.Add(SkyName(r.Sky).ToUpperInvariant());
        parts.Add($"{Dose(r.RadiationUsvH.ToFloat()).Replace("µSv/h", "µSV/H")}");
        return string.Join("  ·  ", parts);
    }

    // ------------------------------------------------------------------ hazards

    /// <summary>What the crews need on a world of this tier (the habitability rules), or the given words.</summary>
    private static string Protection(HabitabilityTier tier, string otherwise) => tier switch
    {
        HabitabilityTier.Masks => "breathing masks outside",
        HabitabilityTier.Suits => "EVA suits outside, sealed buildings",
        HabitabilityTier.Domes => "crews live under domes; EVA suits outside them",
        HabitabilityTier.Tech or HabitabilityTier.Impossible => "no unprotected crew can work here",
        _ => otherwise,
    };

    /// <summary>A full sentence for the event feed and the findings list.</summary>
    /// <param name="tier">The world's protection tier (PlanetSurface.Tier): says what the crews need.</param>
    public static string HazardText(TelemetryHazard h, SurfaceReadings r, HabitabilityTier tier = HabitabilityTier.Open) => h.Kind switch
    {
        HazardKind.Vacuum => $"No atmosphere at the outpost: {Protection(tier, "pressure suits outside")}",
        HazardKind.Unbreathable => $"Unbreathable air at the outpost: O2 {Pct(h.Value.ToFloat())} at {Kpa(h.Limit.ToFloat())} — {Protection(tier, "suits or masks outside")}",
        HazardKind.ToxicGas => $"Toxic gas at the outpost: {h.Gas} {Ppm(GasPpm(r, h.Gas))} ppm (limit {Ppm(h.Limit.ToFloat())}){(h.Severe ? " — lethal levels" : "")}",
        HazardKind.Radiation => $"{(h.Severe ? "Dangerous radiation" : "High radiation")} at the outpost: {Dose(h.Value.ToFloat())} (warning level {Dose(h.Limit.ToFloat())})",
        HazardKind.Heat => $"Extreme heat at the outpost: {Celsius(h.Value.ToFloat())}",
        HazardKind.Cold => $"Extreme cold at the outpost: {Celsius(h.Value.ToFloat())}",
        HazardKind.HighWinds => $"High winds at the outpost: gusts {h.Value.ToFloat():0} m/s",
        HazardKind.DustStorm => "Dust storm at the outpost: visibility down",
        _ => h.Kind.ToString(),
    };

    private static string ShortHazard(TelemetryHazard h) => h.Kind switch
    {
        HazardKind.Vacuum => "vacuum",
        HazardKind.Unbreathable => "unbreathable",
        HazardKind.ToxicGas => $"toxic {h.Gas}",
        HazardKind.Radiation => "radiation",
        HazardKind.Heat => "heat",
        HazardKind.Cold => "cold",
        HazardKind.HighWinds => "high winds",
        HazardKind.DustStorm => "dust storm",
        _ => h.Kind.ToString().ToLowerInvariant(),
    };

    private static float GasPpm(SurfaceReadings r, string gas) => r.Gases.FirstOrDefault(g => g.Gas == gas)?.Ppm.ToFloat() ?? 0f;

    private static Color? Hot(SurfaceReadings r) =>
        r.Hazards.FirstOrDefault(h => h.Kind is HazardKind.Heat or HazardKind.Cold) is { } h ? (h.Severe ? Danger : Warn) : null;

    // ------------------------------------------------------------------ trends

    /// <summary>" ▲" / " ▼" when the value moved more than the threshold over the last three samples (relative = a share).</summary>
    private string Trend(System.Func<Sample, float> pick, float threshold, bool relative = false)
    {
        if (_history.Count < 4) return "";
        var now = pick(_history[^1]);
        var before = pick(_history[^4]);
        var diff = now - before;
        var limit = relative ? Mathf.Abs(before) * threshold : threshold;
        return diff > limit ? " ▲" : diff < -limit ? " ▼" : "";
    }

    /// <summary>The temperature over the last minute as block characters (▁ … █), oldest first.</summary>
    private string Sparkline()
    {
        if (_history.Count < 2) return "gathering…";
        const string blocks = "▁▂▃▄▅▆▇█";
        var min = _history.Min(s => s.Temperature);
        var max = _history.Max(s => s.Temperature);
        var span = Mathf.Max(max - min, 0.5f);
        var chars = _history.Select(s => blocks[Mathf.Clamp(Mathf.RoundToInt((s.Temperature - min) / span * 7f), 0, 7)]).ToArray();
        return $"{new string(chars)}  {min:0.#}–{max:0.#}";
    }

    // ------------------------------------------------------------------ formatting

    public static string Celsius(float c) => Mathf.Abs(c) >= 100 ? $"{c:0} °C" : $"{c:0.0} °C";

    public static string Pct(float p) => p >= 10 ? $"{p:0.#} %" : p >= 0.1f ? $"{p:0.##} %" : $"{p:0.###} %";

    public static string Kpa(float k) => k >= 1000
        ? $"{k.ToString("#,0", CultureInfo.InvariantCulture)} kPa ({k / 101.325f:0} atm)"
        : k >= 10 ? $"{k:0.0} kPa" : $"{k:0.00} kPa";

    public static string Ppm(float p) => p >= 100 ? p.ToString("#,0", CultureInfo.InvariantCulture) : p >= 1 ? $"{p:0.#}" : $"{p:0.##}";

    public static string Dose(float d) => d >= 100 ? $"{d.ToString("#,0", CultureInfo.InvariantCulture)} µSv/h" : d >= 1 ? $"{d:0.0} µSv/h" : $"{d:0.00} µSv/h";

    /// <summary>Local time on the planet's own clock ("14:32 LT"; a 26-hour day runs to 25:59).</summary>
    public static string Clock(float hours)
    {
        var minutes = Mathf.FloorToInt(hours * 60f);
        return $"{minutes / 60:00}:{minutes % 60:00} LT";
    }

    private static readonly string[] Points = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

    public static string CompassPoint(int degrees) => Points[Mathf.PosMod(Mathf.RoundToInt(degrees / 22.5f), 16)];

    public static string SkyName(SkyCondition sky) => sky switch
    {
        SkyCondition.Clear => "clear",
        SkyCondition.PartlyCloudy => "partly cloudy",
        SkyCondition.Overcast => "overcast",
        SkyCondition.Fog => "fog",
        SkyCondition.Rain => "rain",
        SkyCondition.Snow => "snow",
        SkyCondition.DustStorm => "dust storm",
        _ => "no atmosphere",
    };
}
