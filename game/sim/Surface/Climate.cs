using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// CONTRACT (2026-11-06, owner: "each planet should have a light side and dark side based on distance from sun,
// spin of planet, etc. Rain, wind, snow should happen during specific seasons ... stored in data tables").
// Decisions: a local day stays ~12 min of play (Class M day_seconds 720); seasons cycle over a short year (data);
// weather is in the deterministic sim with mild gameplay effects; every body of the system map gets climate data.
//
// The sim agent fills these functions in from data/climate.json (pure, deterministic functions of data, map and tick:
// no sim.Rng draws, weather from hashes of the map seed + body + time slot). The view agent draws from them.
// Keep these signatures and the meaning of every field; add fields/members if needed, don't rename or remove.
// Until filled in they return a neutral, always-noon, clear-weather answer so the view code compiles and runs.

/// <summary>Weather happening over a tile right now.</summary>
public enum WeatherKind
{
    Clear,
    Cloudy,
    Overcast,
    Fog,
    Rain,
    Storm,     // rain + strong wind + lightning
    Snow,
    Blizzard,  // snow + strong wind
    Dust,      // blowing dust / haze
    DustStorm,
    HighWind,  // dry wind, no precipitation
}

/// <summary>Where the star is for the whole planet at a tick.</summary>
/// <param name="SubsolarColumn">Map column (fractional, 0..map width, wraps east–west) where the star is overhead: noon there, midnight half a map away. Moves west→east or east→west by the body's spin.</param>
/// <param name="DeclinationDeg">Latitude where the star is overhead (degrees, + = north), from axial tilt and the year: drives seasons and polar day/night.</param>
/// <param name="DayFraction">0..1 through the local day at column 0 (0 = midnight).</param>
/// <param name="YearFraction">0..1 through the year.</param>
/// <param name="SeasonIndex">Index into the body's season list for the NORTHERN hemisphere (the south is offset by half a year).</param>
/// <param name="Insolation">Sunlight at the top of the air relative to Earth at 1 AU (1 = Earth), from star luminosity and orbital distance (with eccentricity).</param>
public readonly record struct SunState(Fix64 SubsolarColumn, Fix64 DeclinationDeg, Fix64 DayFraction, Fix64 YearFraction,
    int SeasonIndex, string SeasonName, Fix64 Insolation);

/// <summary>Light and season at one tile.</summary>
/// <param name="Daylight">0 = night .. 1 = star overhead (sine of the sun's elevation, clamped; twilight ramps over the last few degrees below the horizon).</param>
/// <param name="SunElevationDeg">Sun elevation above the horizon at this tile, degrees (negative = below).</param>
/// <param name="SunAzimuthDeg">Compass direction of the sun from this tile (0 = map north / -Z, 90 = east / +X).</param>
/// <param name="SeasonIndex">Season at this tile's hemisphere.</param>
public readonly record struct TileClimate(Fix64 Daylight, Fix64 SunElevationDeg, Fix64 SunAzimuthDeg, bool Night,
    int SeasonIndex, string SeasonName, Fix64 TemperatureC, Fix64 HumidityPct);

/// <summary>Weather over a tile.</summary>
/// <param name="Intensity">0..1 within the kind (drizzle → downpour).</param>
/// <param name="WindDirDeg">Direction the wind blows TOWARD (0 = map north / -Z, 90 = east / +X).</param>
/// <param name="PrecipitationMmH">Rain/snow rate (water equivalent).</param>
/// <param name="Visibility">0..1 (1 = clear air).</param>
/// <param name="StartTick">When this weather spell started over this tile's weather cell.</param>
/// <param name="EndTick">When it ends (the next spell starts).</param>
public readonly record struct WeatherState(WeatherKind Kind, Fix64 Intensity, Fix64 WindMs, Fix64 GustMs, Fix64 WindDirDeg,
    Fix64 PrecipitationMmH, Fix64 Visibility, Fix64 CloudCover, long StartTick, long EndTick);

/// <summary>Mild gameplay effects of the weather at a tile (all from data).</summary>
/// <param name="RoverSpeed">Multiplier on ground vehicle speed (1 = none).</param>
/// <param name="SurveyRadiusDelta">Added to survey radii (negative in storms/fog; never below 1 in use).</param>
/// <param name="RoverDamage">Multiplier on rover wear/hazard/flat chances.</param>
/// <param name="DropShipsGrounded">Drop ships / outpost drops can't land on this tile now (high wind, storm).</param>
/// <param name="Solar">Multiplier for future solar power (daylight × cloud).</param>
public readonly record struct WeatherEffects(Fix64 RoverSpeed, int SurveyRadiusDelta, Fix64 RoverDamage, bool DropShipsGrounded, Fix64 Solar);

public static class Climate
{
    /// <summary>The star's position for the whole planet.</summary>
    public static SunState Sun(GameData data, SurfaceMap map, long tick) =>
        new(Fix64.Zero, Fix64.Zero, Fix64.Half, Fix64.Zero, 0, "Summer", Fix64.One);

    /// <summary>Light, season, temperature and humidity at a tile.</summary>
    public static TileClimate At(GameData data, SurfaceMap map, int tile, long tick) =>
        new(Fix64.One, Fix64.FromInt(60), Fix64.FromInt(180), false, 0, "Summer", Fix64.FromInt(20), Fix64.FromInt(50));

    /// <summary>The weather over a tile (weather comes in cells several tiles wide that drift with the wind).</summary>
    public static WeatherState Weather(GameData data, SurfaceMap map, int tile, long tick) =>
        new(WeatherKind.Clear, Fix64.Zero, Fix64.FromInt(4), Fix64.FromInt(8), Fix64.FromInt(90), Fix64.Zero, Fix64.One, Fix64.Zero, 0, long.MaxValue);

    /// <summary>Gameplay effects of the weather at a tile.</summary>
    public static WeatherEffects Effects(GameData data, SurfaceMap map, int tile, long tick) =>
        new(Fix64.One, 0, Fix64.One, false, Fix64.One);
}
