using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A gas and an amount: percent in an atmosphere's main gases, ppm in trace and toxic lists and limits.</summary>
public sealed record GasAmount(string Gas, Fix64 Amount);

/// <summary>
/// What the outpost's sensors read on one planet class (surfaces.json environments), once its uplink to the ship is
/// online. Temperatures are the equator's sea-level afternoon high (DayC) and pre-dawn low (NightC); PolarDropC is how
/// much colder the map's top and bottom rows are; LapsePerKm cools with altitude (land altitude reaches ReliefKm on the
/// highest ground). Humidity is relative, in percent; wind in m/s; pressure at sea level in kPa, falling with altitude
/// over ScaleHeightKm (0 = no atmosphere); radiation in µSv/h; UvIndex at noon. Atmosphere = main gases in percent
/// (summing to 100; empty for a vacuum), Trace = trace gases in ppm, Toxic = what a toxic patch adds to the air on it
/// (ppm, fading over three tiles). DaySeconds = game seconds per local day (DayHours on the local clock).
/// </summary>
public sealed record EnvironmentDef(
    string ClassId,
    string Sky,
    Fix64 GravityG,
    Fix64 DayHours,
    int DaySeconds,
    Fix64 ReliefKm,
    Fix64 DayC,
    Fix64 NightC,
    Fix64 PolarDropC,
    Fix64 LapsePerKm,
    Fix64 HumidityMin,
    Fix64 HumidityMax,
    Fix64 WindMean,
    Fix64 WindGust,
    Fix64 PressureKpa,
    Fix64 ScaleHeightKm,
    Fix64 RadiationDay,
    Fix64 RadiationNight,
    Fix64 UvIndex,
    IReadOnlyList<GasAmount> Atmosphere,
    IReadOnlyList<GasAmount> Trace,
    IReadOnlyList<GasAmount> Toxic)
{
    public bool HasAtmosphere => PressureKpa > Fix64.Zero;

    /// <summary>A main gas's share in percent (0 if it isn't one).</summary>
    public Fix64 Percent(string gas)
    {
        foreach (var g in Atmosphere)
            if (g.Gas == gas) return g.Amount;
        return Fix64.Zero;
    }
}

/// <summary>How a terrain type shapes the local weather: Swing scales the day/night temperature swing, OffsetC shifts
/// the temperature, Humidity scales the ground's wetness and Wind the wind speed.</summary>
public sealed record TerrainWeather(string Terrain, Fix64 Swing, Fix64 OffsetC, Fix64 Humidity, Fix64 Wind);

/// <summary>
/// Telemetry rules (surfaces.json telemetry): exposure limits per gas in ppm (readings above them are hazards), the
/// breathable oxygen share and pressure ranges, warning levels for radiation, heat, cold and gusts, and terrain effects.
/// </summary>
public sealed record TelemetryRules(
    IReadOnlyList<GasAmount> ExposureLimitsPpm,
    Fix64 BreathableO2Min,
    Fix64 BreathableO2Max,
    Fix64 BreathableKpaMin,
    Fix64 BreathableKpaMax,
    Fix64 RadiationWarn,
    Fix64 RadiationDanger,
    Fix64 HeatWarnC,
    Fix64 ColdWarnC,
    Fix64 GustWarn,
    IReadOnlyList<TerrainWeather> Terrain)
{
    /// <summary>The exposure limit for a gas in ppm, or null when it has none.</summary>
    public Fix64? LimitPpm(string gas)
    {
        foreach (var g in ExposureLimitsPpm)
            if (g.Gas == gas) return g.Amount;
        return null;
    }

    /// <summary>The terrain's weather effects (neutral when the terrain isn't listed).</summary>
    public TerrainWeather WeatherOf(string terrain)
    {
        foreach (var t in Terrain)
            if (t.Terrain == terrain) return t;
        return new TerrainWeather(terrain, Fix64.One, Fix64.Zero, Fix64.One, Fix64.One);
    }
}
