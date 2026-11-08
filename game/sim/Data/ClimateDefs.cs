using System.Collections.Generic;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Data;

// Planetary climate (data/climate.json): stars, every map body's orbit and spin, climate templates with seasons and
// weather odds, weather kinds and their gameplay effects. Read by GameData.Climate.cs, used by Surface/Climate.cs.

/// <summary>A spectral class: luminosity relative to Sol, colour temperature and a colour for the view.</summary>
public sealed record StarTypeDef(string Class, string Name, Fix64 Luminosity, int ColourK, string Colour);

/// <summary>How a body turns: Locked = what it keeps facing.</summary>
public enum SpinLock
{
    /// <summary>Turns freely: a day/night cycle of DaySeconds.</summary>
    None,

    /// <summary>A moon that always shows its planet the same face: its day is one orbit around the planet.</summary>
    Parent,

    /// <summary>Always shows the star the same face: permanent light and dark sides (the year is one day).</summary>
    Star,
}

/// <summary>
/// One body's climate numbers (climate.json bodies, keyed by the maps.json body id). Planets carry their own orbit
/// (Au, Eccentricity, Perihelion as a year fraction); a moon has Parent set and shares its planet's orbit around the
/// star. DaySeconds = play seconds per local day (DayHours on the local clock); YearDays local days per year;
/// YearStart = year fraction at tick 0 (0 = northern spring equinox); Facing = for a star-locked body, the share of the
/// map width where the star stands overhead. Seasons, when not empty, replace the climate template's.
/// </summary>
public sealed record BodyClimateDef(
    string Id,
    string? Parent,
    Fix64 Au,
    Fix64 Eccentricity,
    Fix64 Perihelion,
    int DaySeconds,
    Fix64 DayHours,
    bool Retrograde,
    SpinLock Locked,
    Fix64 Facing,
    Fix64 AxialTiltDeg,
    Fix64 YearDays,
    Fix64 YearStart,
    Fix64 Albedo,
    Fix64 GreenhouseC,
    string ClimateId,
    IReadOnlyList<SeasonDef> Seasons,
    bool Landable,
    string Notes)
{
    public bool IsMoon => Parent != null;

    /// <summary>The body's own surface gravity in g (climate.json gravity_g), overriding its class environment's; null = the class's.</summary>
    public Fix64? GravityG { get; init; }

    /// <summary>Play seconds per year.</summary>
    public Fix64 YearSeconds => YearDays * DaySeconds;
}

/// <summary>
/// One season of a climate template: Share of the year, TempC / HumidityPct offsets and a Wind multiplier (blended
/// smoothly between seasons' middles), how long weather spells last, and the weight of each weather kind
/// (index = (int)WeatherKind).
/// </summary>
public sealed record SeasonDef(string Name, Fix64 Share, Fix64 TempC, Fix64 HumidityPct, Fix64 Wind, int SpellMinSeconds, int SpellMaxSeconds,
    IReadOnlyList<Fix64> Weights);

/// <summary>A latitude band (from the equator up to ToDeg): how strongly the seasons' temperatures apply, the
/// prevailing wind direction, and multipliers on the season weights (index = (int)WeatherKind).</summary>
public sealed record LatitudeBand(string Name, Fix64 ToDeg, Fix64 SeasonScale, Fix64 WindTowardDeg, IReadOnlyList<Fix64> Multipliers);

/// <summary>
/// A weather template: Air = false means no weather at all; WindScale scales every kind's wind; weather cells drift
/// DriftTilesPerMin east (negative = west); SwingC / PolarDropC stand in for a class environment when there is none.
/// </summary>
public sealed record ClimateTemplate(string Id, string Name, bool Air, Fix64 WindScale, Fix64 DriftTilesPerMin, Fix64 SwingC, Fix64 PolarDropC,
    IReadOnlyList<SeasonDef> Seasons, IReadOnlyList<LatitudeBand> Bands)
{
    /// <summary>The band a latitude (degrees, either hemisphere) falls in.</summary>
    public LatitudeBand BandAt(Fix64 latitudeDeg)
    {
        var a = Fix64.Abs(latitudeDeg);
        foreach (var b in Bands)
            if (a <= b.ToDeg) return b;
        return Bands[^1];
    }
}

/// <summary>A weather kind's look and gameplay effects at full strength (climate.json weather).</summary>
public sealed record WeatherDef(
    WeatherKind Kind,
    string Id,
    string Name,
    Fix64 Cloud,
    Fix64 Visibility,
    Fix64 PrecipMin,
    Fix64 PrecipMax,
    Fix64 WindMin,
    Fix64 WindMax,
    Fix64 Gust,
    Fix64 HumidityMinPct,
    Fix64 RoverSpeed,
    int SurveyRadiusDelta,
    Fix64 RoverDamage,
    bool DropShipsGrounded,
    Fix64 Solar);

/// <summary>Extra effects of some weather kinds on some terrain (mud, drifts, sand), multiplied on top.</summary>
public sealed record TerrainWeatherEffect(string Id, string Name, IReadOnlyList<WeatherKind> Weather, IReadOnlyList<string> Terrain,
    Fix64 RoverSpeed, Fix64 RoverDamage);

/// <summary>How a body's numbers become a map (climate.json surface; see its _notes).</summary>
public sealed record ClimateSurfaceRules(
    Fix64 LatitudeSpanDeg,
    Fix64 StartDayFraction,
    Fix64 TwilightDeg,
    Fix64 ThermalLagDay,
    Fix64 SeasonLagYear,
    Fix64 EquatorShare,
    Fix64 SnowBelowC,
    Fix64 CellTiles,
    Fix64 RampShare,
    int RampMaxSeconds,
    int EpochSeconds,
    int CalmStartSeconds,
    Fix64 DropShipMaxGustMs,
    Fix64 AuPerMapUnit);

/// <summary>Everything in climate.json.</summary>
public sealed record ClimateRules(
    IReadOnlyList<StarTypeDef> Stars,
    IReadOnlyList<BodyClimateDef> Bodies,
    IReadOnlyList<(string ClassId, string ClimateId)> Classes,
    ClimateSurfaceRules Surface,
    IReadOnlyList<ClimateTemplate> Templates,
    IReadOnlyList<WeatherDef> Weather,
    IReadOnlyList<TerrainWeatherEffect> Terrain)
{
    public StarTypeDef? Star(string spectralClass)
    {
        foreach (var s in Stars)
            if (s.Class == spectralClass) return s;
        return null;
    }

    public BodyClimateDef? Body(string id)
    {
        foreach (var b in Bodies)
            if (b.Id == id) return b;
        return null;
    }

    public ClimateTemplate? Template(string id)
    {
        foreach (var t in Templates)
            if (t.Id == id) return t;
        return null;
    }

    /// <summary>The climate template for a planet class (climate.json classes), or null.</summary>
    public string? ClassClimate(string classId)
    {
        foreach (var (c, t) in Classes)
            if (c == classId) return t;
        return null;
    }

    /// <summary>The weather kind's definition (every kind is listed: Validate checks).</summary>
    public WeatherDef Of(WeatherKind kind)
    {
        foreach (var w in Weather)
            if (w.Kind == kind) return w;
        return Weather[0];
    }
}

/// <summary>
/// The climate entries of a generated star system's bodies (Space/SystemGenerator.cs), which are not in climate.json:
/// one <see cref="BodyClimateDef"/> per body (moons have Parent set and share their planet's orbit), the planet class of
/// each and the star's spectral class. A surface of one of these bodies carries it (SurfaceMap.SystemClimate), so
/// Climate finds its day, seasons and weather. A pure function of the data and the system's seed.
/// </summary>
public sealed class SystemClimate
{
    private readonly List<BodyClimateDef> _bodies = new();
    private readonly List<string> _classes = new();

    public SystemClimate(string starClass, IEnumerable<(BodyClimateDef Body, string ClassId)> bodies)
    {
        StarClass = starClass;
        foreach (var (b, c) in bodies)
        {
            _bodies.Add(b);
            _classes.Add(c);
        }
    }

    /// <summary>The star's spectral class (climate.json stars).</summary>
    public string StarClass { get; }

    public IReadOnlyList<BodyClimateDef> Bodies => _bodies;

    public BodyClimateDef? Body(string id)
    {
        for (var i = 0; i < _bodies.Count; i++)
            if (_bodies[i].Id == id) return _bodies[i];
        return null;
    }

    /// <summary>The planet class of a body (null if it isn't one of this system's).</summary>
    public string? ClassOf(string id)
    {
        for (var i = 0; i < _bodies.Count; i++)
            if (_bodies[i].Id == id) return _classes[i];
        return null;
    }
}
