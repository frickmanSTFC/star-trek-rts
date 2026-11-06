using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// CONTRACT (2026-11-06, owner: "each planet should have a light side and dark side based on distance from sun,
// spin of planet, etc. Rain, wind, snow should happen during specific seasons ... stored in data tables").
// Decisions: a local day stays ~12 min of play (Class M day_seconds 720); seasons cycle over a short year (data);
// weather is in the deterministic sim with mild gameplay effects; every body of the system map gets climate data.
//
// Pure, deterministic functions of data/climate.json (+ surfaces.json environments), the map and the tick: no sim.Rng
// draws and no stored state; weather comes from hashes of the map seed + body + weather cell + time slot. The view
// draws from them. Keep these signatures and the meaning of every field; add fields/members if needed.
//
// Which body a surface is: SurfaceMap.ClimateBodyId, set when the map is generated (Simulation.LoadSurface's
// climateBody, else GameData.DefaultClimateBody: the lowest player slot's home body of that planet class, Tarsis on
// mvp_duel). A surface with no body (or data without climate.json) gets its class's climate template on a stand-in
// orbit tuned to the class environment.
//
// The model: the subsolar column moves one map width per local day (prograde: east to west; retrograde: west to east;
// locked to the star: it never moves); declination = tilt × sin(2π·year); sunlight = luminosity / r² with r from the
// eccentric orbit. Temperature = the body's mean (278.6 K·((1-albedo)·sunlight)^¼ − 273.15 + greenhouse) + the equator's
// share of the polar drop − polar_drop·latitude² + swing·(sun height a little after noon and the solstice) + the season's
// offset (scaled by latitude band) − lapse·altitude + terrain. Weather: cells about cell_tiles across drift east or west;
// each cell runs spells (restarting every season change and epoch) whose kind is picked by the season's weights ×
// the latitude band's multipliers, with a peak intensity and smooth ramps; rain falls as snow below snow_below_c.

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
/// <param name="Night">The sun is below the horizon.</param>
/// <param name="SeasonIndex">Season at this tile's hemisphere.</param>
/// <param name="TemperatureC">Air temperature at the tile (what the outpost's telemetry reads).</param>
/// <param name="HumidityPct">Relative humidity, at least the current weather's humidity_min_pct.</param>
public readonly record struct TileClimate(Fix64 Daylight, Fix64 SunElevationDeg, Fix64 SunAzimuthDeg, bool Night,
    int SeasonIndex, string SeasonName, Fix64 TemperatureC, Fix64 HumidityPct);

/// <summary>Weather over a tile.</summary>
/// <param name="Intensity">0..1 within the kind (drizzle → downpour): the spell's peak × its ramp in and out.</param>
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

/// <summary>A body's climate at a glance (for the system display and the docs): mean sunlight and temperature, day and year.</summary>
/// <param name="MeanInsolation">Sunlight at the body's mean distance (1 = Earth).</param>
/// <param name="MeanTemperatureC">The body's mean temperature from sunlight, albedo and greenhouse (before latitude and day/night).</param>
public sealed record ClimateSummary(string BodyId, string ClimateId, string ClimateName, Fix64 Au, Fix64 MeanInsolation, Fix64 MeanTemperatureC,
    int DaySeconds, Fix64 DayHours, Fix64 YearDays, Fix64 YearSeconds, Fix64 AxialTiltDeg, bool Retrograde, SpinLock Locked,
    IReadOnlyList<string> Seasons, bool HasWeather, bool Landable);

public static class Climate
{
    private static readonly Fix64 Quarter = Fix64.Ratio(1, 4);
    private static readonly Fix64 Hundred = Fix64.FromInt(100);
    private static readonly Fix64 Deg360 = Fix64.FromInt(360);
    private static readonly Fix64 Pi = Fix64.Parse("3.14159265358979");
    private static readonly Fix64 RadToDeg = Fix64.Parse("57.2957795130823");
    private static readonly Fix64 KelvinOffset = Fix64.Parse("273.15");
    private static readonly Fix64 EquilibriumK = Fix64.Parse("278.6");

    // ---------------------------------------------------------------- the contract

    /// <summary>The star's position for the whole planet.</summary>
    public static SunState Sun(GameData data, SurfaceMap map, long tick) => Sun(ContextOf(data, map), tick, map.Grid.Width);

    /// <summary>Light, season, temperature and humidity at a tile.</summary>
    public static TileClimate At(GameData data, SurfaceMap map, int tile, long tick)
    {
        var c = ContextOf(data, map);
        var geo = Geometry(c, map, tile, tick);
        var temperature = Temperature(c, map, tile, tick, geo);
        var weather = Core(c, map, tile, tick, temperature);
        return Tile(c, map, tile, tick, geo, temperature, weather);
    }

    /// <summary>The weather over a tile (weather comes in cells several tiles wide that drift with the wind).</summary>
    public static WeatherState Weather(GameData data, SurfaceMap map, int tile, long tick)
    {
        var c = ContextOf(data, map);
        return Core(c, map, tile, tick, Temperature(c, map, tile, tick, Geometry(c, map, tile, tick))).State;
    }

    /// <summary>Gameplay effects of the weather at a tile.</summary>
    public static WeatherEffects Effects(GameData data, SurfaceMap map, int tile, long tick)
    {
        var c = ContextOf(data, map);
        var geo = Geometry(c, map, tile, tick);
        return EffectsOf(c, map, tile, Core(c, map, tile, tick, Temperature(c, map, tile, tick, geo)), geo);
    }

    // ---------------------------------------------------------------- more for the view and the sim

    /// <summary>The climate.json body the map is (null = its class template on a stand-in orbit).</summary>
    public static BodyClimateDef? BodyOf(GameData data, SurfaceMap map) => ContextOf(data, map).Body;

    /// <summary>The season names of the map's body, in year order (index = SunState/TileClimate.SeasonIndex).</summary>
    public static IReadOnlyList<SeasonDef> SeasonsOf(GameData data, SurfaceMap map) => ContextOf(data, map).Seasons;

    /// <summary>A weather kind's data (name, look and effects at full strength), or null without climate data.</summary>
    public static WeatherDef? Def(GameData data, WeatherKind kind) => data.Climate?.Of(kind);

    /// <summary>Display name of a weather kind ("Dust storm").</summary>
    public static string Name(GameData data, WeatherKind kind) => data.Climate?.Of(kind).Name ?? kind.ToString();

    /// <summary>Everything about the weather and light at a tile in one go (cheaper than calling each).</summary>
    public static (TileClimate Tile, WeatherState Weather, WeatherEffects Effects) Report(GameData data, SurfaceMap map, int tile, long tick)
    {
        var c = ContextOf(data, map);
        var geo = Geometry(c, map, tile, tick);
        var temperature = Temperature(c, map, tile, tick, geo);
        var weather = Core(c, map, tile, tick, temperature);
        return (Tile(c, map, tile, tick, geo, temperature, weather), weather.State, EffectsOf(c, map, tile, weather, geo));
    }

    /// <summary>Why drop ships (and the first landing) can't come down on this tile now, or null: "a storm over the
    /// site, drop ships can't land (clears in about 40 s)". <paramref name="stormsOnly"/>: only the weather kinds that
    /// ground drop ships count, not gusts (the first landing, so the start can't be blown out for long).</summary>
    public static string? LandingProblem(GameData data, SurfaceMap map, int tile, long tick, bool stormsOnly = false)
    {
        if (tile < 0 || tile >= map.Grid.Count) return null;
        var c = ContextOf(data, map);
        if (c.Rules == null) return null;
        var geo = Geometry(c, map, tile, tick);
        var w = Core(c, map, tile, tick, Temperature(c, map, tile, tick, geo));
        var stormy = w.Def != null && w.Def.DropShipsGrounded && w.Ramp >= GroundedRamp;
        var gusty = !stormsOnly && w.State.GustMs >= c.Rules.Surface.DropShipMaxGustMs;
        if (!stormy && !gusty) return null;
        var what = !stormy ? "high winds" : w.State.Kind switch
        {
            WeatherKind.Storm => "a storm",
            WeatherKind.Blizzard => "a blizzard",
            WeatherKind.DustStorm => "a dust storm",
            _ => "high winds",
        };
        var left = System.Math.Max(1, (w.State.EndTick - tick + Simulation.TickRate - 1) / Simulation.TickRate);
        return $"{what} over the site: drop ships can't land (clears in about {left} s)";
    }

    /// <summary>A body's climate at a glance, or null when it has no climate.json entry.</summary>
    public static ClimateSummary? Summary(GameData data, string bodyId)
    {
        var rules = data.Climate;
        var body = rules?.Body(bodyId);
        if (rules == null || body == null) return null;
        var c = ForBody(data, rules, body, ClassOf(data, bodyId));
        var template = c.Template;
        var seasons = new List<string>();
        foreach (var s in c.Seasons) seasons.Add(s.Name);
        return new ClimateSummary(body.Id, body.ClimateId, template?.Name ?? body.ClimateId, c.Au, c.MeanInsolation, MeanTemperature(c, c.MeanInsolation),
            body.DaySeconds, body.DayHours, body.YearDays, body.YearSeconds, body.AxialTiltDeg, body.Retrograde, body.Locked, seasons,
            template?.Air ?? false, body.Landable);
    }

    /// <summary>Geometry, tile climate and weather at once, for Telemetry.</summary>
    internal static (Geo Geo, TileClimate Tile, WeatherCore Weather) Evaluate(Context c, SurfaceMap map, int tile, long tick)
    {
        var geo = Geometry(c, map, tile, tick);
        var temperature = Temperature(c, map, tile, tick, geo);
        var weather = Core(c, map, tile, tick, temperature);
        return (geo, Tile(c, map, tile, tick, geo, temperature, weather), weather);
    }

    // ---------------------------------------------------------------- resolved numbers

    /// <summary>A body's (or stand-in's) climate numbers, resolved once per map and data.</summary>
    internal sealed class Context
    {
        public GameData? Data;
        public ClimateRules? Rules;
        public BodyClimateDef? Body;
        public ClimateTemplate? Template;
        public EnvironmentDef? Env;
        public IReadOnlyList<SeasonDef> Seasons = new List<SeasonDef>();
        public Fix64 Luminosity = Fix64.One;
        public Fix64 Au = Fix64.One;
        public Fix64 Eccentricity;
        public Fix64 Perihelion;
        public Fix64 MeanInsolation = Fix64.One;
        public long DayTicks = 720 * Simulation.TickRate;
        public long YearTicks = 8 * 720 * Simulation.TickRate;
        public Fix64 YearStart;
        public bool Retrograde;
        public SpinLock Locked;
        public Fix64 Facing;
        public Fix64 TiltDeg;
        public Fix64 Albedo = Fix64.Parse("0.3");
        public Fix64 GreenhouseC = Fix64.FromInt(33);
        public Fix64? MeanOverrideC;
        public Fix64 SwingC;
        public Fix64 PolarDropC;
        public Fix64 LapsePerKm;
        public bool Air;
        public ulong BodyHash;

        // Surface rules (defaults = climate.json's when there is none).
        public Fix64 LatitudeSpanDeg = Fix64.FromInt(65);
        public Fix64 StartDay = Fix64.Parse("0.35");
        public Fix64 SinTwilight = Fix64.Parse("0.1045");
        public Fix64 ThermalLag = Fix64.Ratio(1, 10);
        public Fix64 SeasonLag = Fix64.Parse("0.06");
        public Fix64 EquatorShare = Fix64.Parse("0.27");
    }

    private sealed class Cached
    {
        public GameData Data = null!;
        public Context Context = null!;
    }

    private static string? ClassOf(GameData data, string bodyId)
    {
        foreach (var m in data.Maps)
            foreach (var b in m.Bodies)
                if (b.Id == bodyId) return b.ClassId;
        return null;
    }

    private static StarTypeDef? StarOf(GameData data, ClimateRules rules, string bodyId)
    {
        foreach (var m in data.Maps)
            foreach (var b in m.Bodies)
                if (b.Id == bodyId) return rules.Star(m.Star.Class);
        return null;
    }

    internal static Context ContextOf(GameData data, SurfaceMap map)
    {
        if (map.ClimateCache is Cached cached && ReferenceEquals(cached.Data, data)) return cached.Context;
        var rules = data.Climate;
        var body = map.ClimateBodyId != null ? rules?.Body(map.ClimateBodyId) : null;
        var context = rules != null && body != null
            ? ForBody(data, rules, body, ClassOf(data, body.Id) ?? map.Def.ClassId) // the body's own class environment
            : Standin(data, rules, map.Def.ClassId, data.EnvironmentOf(map.Def.ClassId));
        map.ClimateCache = new Cached { Data = data, Context = context };
        return context;
    }

    /// <summary>The numbers for one climate.json body; classId = the planet class whose environment it reads.</summary>
    private static Context ForBody(GameData data, ClimateRules rules, BodyClimateDef body, string? classId)
    {
        var template = rules.Template(body.ClimateId);
        var env = classId != null ? data.EnvironmentOf(classId) : null;
        var orbit = body.Parent != null ? rules.Body(body.Parent) ?? body : body;
        var c = new Context
        {
            Data = data,
            Rules = rules,
            Body = body,
            Template = template,
            Env = env,
            Seasons = body.Seasons.Count > 0 ? body.Seasons : template?.Seasons ?? new List<SeasonDef>(),
            Luminosity = StarOf(data, rules, body.Id)?.Luminosity ?? Fix64.One,
            Au = orbit.Au > Fix64.Zero ? orbit.Au : Fix64.One,
            Eccentricity = orbit.Eccentricity,
            Perihelion = orbit.Perihelion,
            DayTicks = System.Math.Max(1L, (long)body.DaySeconds * Simulation.TickRate),
            YearTicks = System.Math.Max(1L, (body.YearSeconds * Simulation.TickRate).FloorToInt()),
            YearStart = body.YearStart,
            Retrograde = body.Retrograde,
            Locked = body.Locked,
            Facing = body.Facing,
            TiltDeg = body.AxialTiltDeg,
            Albedo = body.Albedo,
            GreenhouseC = body.GreenhouseC,
            Air = template?.Air ?? env?.HasAtmosphere ?? false,
            BodyHash = Fnv(body.Id),
        };
        Fill(c, rules, template, env);
        c.MeanInsolation = c.Luminosity / (c.Au * c.Au);
        return c;
    }

    /// <summary>A surface with no climate.json body: the class's template on a stand-in orbit whose mean matches the
    /// class environment (or Earth's numbers when there's no environment either).</summary>
    private static Context Standin(GameData? data, ClimateRules? rules, string classId, EnvironmentDef? env)
    {
        var templateId = rules?.ClassClimate(classId);
        var template = templateId != null ? rules!.Template(templateId) : null;
        var c = new Context
        {
            Data = data,
            Rules = rules,
            Template = template,
            Env = env,
            Seasons = template?.Seasons ?? new List<SeasonDef>(),
            DayTicks = (long)(env?.DaySeconds ?? 720) * Simulation.TickRate,
            Air = template?.Air ?? env?.HasAtmosphere ?? false,
            BodyHash = Fnv("class:" + classId),
        };
        c.YearTicks = c.DayTicks * 8;
        Fill(c, rules, template, env);
        if (env != null) c.MeanOverrideC = (env.DayC + env.NightC) / 2 - c.PolarDropC * c.EquatorShare;
        return c;
    }

    /// <summary>The stand-in for a bare environment (Telemetry.Read without the game data): no seasons, no weather.</summary>
    internal static Context ForEnvironment(EnvironmentDef env) => Standin(null, null, env.ClassId, env);

    private static void Fill(Context c, ClimateRules? rules, ClimateTemplate? template, EnvironmentDef? env)
    {
        if (rules != null)
        {
            var s = rules.Surface;
            c.LatitudeSpanDeg = s.LatitudeSpanDeg;
            c.StartDay = s.StartDayFraction;
            c.SinTwilight = SinDeg(s.TwilightDeg);
            c.ThermalLag = s.ThermalLagDay;
            c.SeasonLag = s.SeasonLagYear;
            c.EquatorShare = s.EquatorShare;
        }
        c.SwingC = env != null ? (env.DayC - env.NightC) / 2 : template?.SwingC ?? Fix64.FromInt(7);
        c.PolarDropC = env?.PolarDropC ?? template?.PolarDropC ?? Fix64.FromInt(38);
        c.LapsePerKm = env?.LapsePerKm ?? Fix64.Zero;
    }

    // ---------------------------------------------------------------- the sun

    /// <summary>The sun for a map <paramref name="width"/> columns wide (noon where local time is 0.5).</summary>
    private static SunState Sun(Context c, long tick, int width)
    {
        var day = DayFraction(c, tick);
        var year = YearFraction(c, tick);
        var subsolar = c.Retrograde && c.Locked != SpinLock.Star ? Frac(day - Fix64.Half) : Frac(Fix64.Half - day);
        var season = SeasonIndex(c.Seasons, year);
        return new SunState(subsolar * width, Declination(c, year), day, year, season, SeasonName(c.Seasons, season), Insolation(c, year));
    }

    /// <summary>Local time at column 0 (0 = midnight): turning with the spin, or fixed for a body locked to its star.</summary>
    private static Fix64 DayFraction(Context c, long tick) =>
        c.Locked == SpinLock.Star ? Frac(Fix64.Half - c.Facing) : Frac(c.StartDay + Fix64.Ratio((int)(tick % c.DayTicks), (int)c.DayTicks));

    private static Fix64 YearFraction(Context c, long tick)
    {
        var t = ((tick % c.YearTicks) + c.YearTicks) % c.YearTicks;
        return Frac(c.YearStart + FromLongRatio(t, c.YearTicks));
    }

    private static Fix64 Declination(Context c, Fix64 year) => c.TiltDeg * SinTurns(year);

    private static Fix64 Insolation(Context c, Fix64 year)
    {
        var r = c.Au * (Fix64.One - c.Eccentricity * Telemetry.CosTurns(year - c.Perihelion));
        return c.Luminosity / (r * r);
    }

    /// <summary>The body's mean temperature for this sunlight (before latitude, day/night and season).</summary>
    private static Fix64 MeanTemperature(Context c, Fix64 insolation)
    {
        if (c.MeanOverrideC is { } m) return m;
        var absorbed = Fix64.Max(Fix64.Zero, (Fix64.One - c.Albedo) * insolation);
        return EquilibriumK * Fix64.Sqrt(Fix64.Sqrt(absorbed)) - KelvinOffset + c.GreenhouseC;
    }

    // ---------------------------------------------------------------- one tile

    /// <summary>Sun geometry at a tile and tick.</summary>
    internal readonly record struct Geo(Fix64 LatitudeShare, Fix64 LatitudeDeg, Fix64 LocalTime, Fix64 Year, Fix64 SinElevation,
        Fix64 ElevationDeg, Fix64 AzimuthDeg, Fix64 Daylight, Fix64 Insolation);

    /// <summary>Local solar time at a tile (0 = midnight, 0.5 = noon).</summary>
    private static Fix64 LocalTime(Context c, SurfaceMap map, int tile, long tick)
    {
        var x = map.Grid.Centre(tile).X / map.Grid.Width;
        var day = DayFraction(c, tick);
        return c.Retrograde && c.Locked != SpinLock.Star ? Frac(day - x) : Frac(day + x);
    }

    internal static Geo Geometry(Context c, SurfaceMap map, int tile, long tick)
    {
        var share = Telemetry.Latitude(map, tile);
        var lat = share * c.LatitudeSpanDeg;
        var local = LocalTime(c, map, tile, tick);
        var year = YearFraction(c, tick);
        var dec = Declination(c, year);
        var hour = local - Fix64.Half; // turns from noon, + = afternoon
        var sinLat = SinDeg(lat);
        var cosLat = CosDeg(lat);
        var sinDec = SinDeg(dec);
        var cosDec = CosDeg(dec);
        var cosH = Telemetry.CosTurns(hour);
        var sinEl = Fix64.Clamp(sinLat * sinDec + cosLat * cosDec * cosH, -Fix64.One, Fix64.One);
        var elevation = AsinDeg(sinEl);
        // Azimuth from north, clockwise: a prograde sun travels east → south/north → west; a retrograde one the other way.
        var travel = c.Retrograde && c.Locked != SpinLock.Star ? -hour : hour;
        var y = -cosDec * SinTurns(travel);
        var x = sinDec * cosLat - cosDec * sinLat * cosH;
        var azimuth = Wrap360(Atan2Deg(y, x));
        var daylight = Fix64.Clamp((sinEl + c.SinTwilight) / (Fix64.One + c.SinTwilight), Fix64.Zero, Fix64.One);
        return new Geo(share, lat, local, year, sinEl, elevation, azimuth, daylight, Insolation(c, year));
    }

    /// <summary>Air temperature at a tile (weather-free: the weather's rain/snow line reads it).</summary>
    private static Fix64 Temperature(Context c, SurfaceMap map, int tile, long tick, Geo g)
    {
        // The warmth lags the sun: the sun's height a little after noon and a little after the solstice.
        var lagYear = g.Year - c.SeasonLag;
        var dec = c.TiltDeg * SinTurns(lagYear);
        var hot = SinDeg(g.LatitudeDeg) * SinDeg(dec) + CosDeg(g.LatitudeDeg) * CosDeg(dec) * Telemetry.CosTurns(g.LocalTime - Fix64.Half - c.ThermalLag);

        var terrain = c.Data?.Telemetry?.WeatherOf(map.Terrain(tile).Id);
        var swingScale = terrain == null ? Fix64.One : Fix64.One + (terrain.Swing - Fix64.One) * (c.Air ? Fix64.One : Quarter);
        var altitude = c.Env != null ? Telemetry.AltitudeKm(c.Env, map, tile) : Fix64.Zero;

        var season = Fix64.Zero;
        if (c.Seasons.Count > 0)
        {
            var hemisphereYear = g.LatitudeShare >= Fix64.Zero ? lagYear : lagYear + Fix64.Half;
            var scale = c.Template?.BandAt(g.LatitudeDeg).SeasonScale ?? Fix64.One;
            season = Blend(c.Seasons, Frac(hemisphereYear), s => s.TempC) * scale;
        }

        var noise = new ValueNoise(map.Seed ^ c.BodyHash ^ 0x7E1E3E7A5UL);
        var jitter = (noise.Sample(Seconds(tick) / 60, Fix64.FromInt(1000 + tile % 997)) - Fix64.Half) * (c.Air ? 3 : 1);

        var t = MeanTemperature(c, g.Insolation) + c.PolarDropC * c.EquatorShare - c.PolarDropC * g.LatitudeShare * g.LatitudeShare
                + c.SwingC * swingScale * hot + season - c.LapsePerKm * altitude + (terrain?.OffsetC ?? Fix64.Zero) + jitter;
        return Fix64.Max(t, Fix64.FromInt(-270)); // nothing reads colder than a few kelvin
    }

    private static TileClimate Tile(Context c, SurfaceMap map, int tile, long tick, Geo g, Fix64 temperature, WeatherCore weather)
    {
        var hemisphereYear = g.LatitudeShare >= Fix64.Zero ? g.Year : g.Year + Fix64.Half;
        var season = SeasonIndex(c.Seasons, Frac(hemisphereYear));
        return new TileClimate(g.Daylight, g.ElevationDeg, g.AzimuthDeg, g.SinElevation < Fix64.Zero, season, SeasonName(c.Seasons, season),
            temperature, Humidity(c, map, tile, tick, g, weather));
    }

    /// <summary>Relative humidity: the ground's wetness picks within the class's range, drier in the afternoon, the
    /// season's offset on top, and at least what the weather needs (fog is near 100%).</summary>
    private static Fix64 Humidity(Context c, SurfaceMap map, int tile, long tick, Geo g, WeatherCore weather)
    {
        if (!c.Air) return Fix64.Zero;
        var min = c.Env?.HumidityMin ?? Fix64.FromInt(10);
        var max = c.Env?.HumidityMax ?? Fix64.FromInt(95);
        var terrain = c.Data?.Telemetry?.WeatherOf(map.Terrain(tile).Id);
        var wet = map.IsWater(tile) ? Fix64.One
            : (Fix64.Ratio(35, 100) + Fix64.Ratio(2, 5) * map.Biomass[tile] + (map.TouchesWater(tile) ? Quarter : Fix64.Zero)) * (terrain?.Humidity ?? Fix64.One);
        wet = Fix64.Clamp(wet, Fix64.Zero, Fix64.One);
        var thermal = Telemetry.CosTurns(g.LocalTime - Fix64.Half - c.ThermalLag);
        var noise = new ValueNoise(map.Seed ^ c.BodyHash ^ 0x4D1D1E5UL);
        var h = (min + (max - min) * wet) * (Fix64.One - Fix64.Ratio(18, 100) * thermal)
                + (noise.Sample(Seconds(tick) / 45, Fix64.FromInt(2000 + tile % 997)) - Fix64.Half) * 8;
        if (c.Seasons.Count > 0)
        {
            var hemisphereYear = g.LatitudeShare >= Fix64.Zero ? g.Year : g.Year + Fix64.Half;
            h += Blend(c.Seasons, Frac(hemisphereYear), s => s.HumidityPct);
        }
        if (weather.Def != null) h = Fix64.Max(h, min + (weather.Def.HumidityMinPct - min) * weather.Ramp);
        return Fix64.Clamp(h, min, max);
    }

    // ---------------------------------------------------------------- weather

    /// <summary>The weather plus what Effects needs: the spell's ramp (0..1) and the kind's data.</summary>
    internal readonly record struct WeatherCore(WeatherState State, Fix64 Ramp, WeatherDef? Def);

    /// <summary>Spells under this ramp don't ground drop ships yet (the storm is still building or already passing).</summary>
    private static readonly Fix64 GroundedRamp = Quarter;

    internal static WeatherCore Core(Context c, SurfaceMap map, int tile, long tick, Fix64 temperature)
    {
        var rules = c.Rules;
        var template = c.Template;
        if (rules == null || template == null || !c.Air || c.Seasons.Count == 0) return Calm(c, map, tile, tick);

        var s = rules.Surface;
        var grid = map.Grid;
        var secs = Seconds(tick);
        var p = grid.Centre(tile);

        // The cell over the tile: a lattice of cells drifting east or west (the map wraps), edges warped by noise.
        var cellsX = System.Math.Max(1, (Fix64.FromInt(grid.Width) / s.CellTiles + Fix64.Half).FloorToInt());
        var cellW = Fix64.FromInt(grid.Width) / cellsX;
        var cellH = s.CellTiles;
        var drift = template.DriftTilesPerMin * secs / 60;
        var u = (p.X - drift) / cellW;
        var v = p.Y / cellH;
        var warp = new ValueNoise(map.Seed ^ c.BodyHash ^ 0x5EA7E7UL);
        var wu = (warp.Sample(u * 2, v * 2, cellsX * 2) - Fix64.Half) * Fix64.Ratio(6, 10);
        var wv = (warp.Sample(u * 2 + Fix64.FromInt(57), v * 2, cellsX * 2) - Fix64.Half) * Fix64.Ratio(6, 10);
        var cx = (((u + wu).FloorToInt() % cellsX) + cellsX) % cellsX;
        var cy = (v + wv).FloorToInt();

        // The cell's latitude picks its hemisphere's season and its band.
        var rows = grid.Height - 1;
        var cellRow = (Fix64.FromInt(cy) + Fix64.Half) * cellH / HexGrid.RowSpacing;
        var share = rows <= 0 ? Fix64.Zero : Fix64.Clamp((Fix64.FromInt(rows) - cellRow * 2) / rows, -Fix64.One, Fix64.One);
        var band = template.BandAt(share * c.LatitudeSpanDeg);
        var north = share >= Fix64.Zero;

        // The season (in ticks) for that hemisphere, then the epoch within it.
        var startTicks = Scale(c.YearStart, c.YearTicks);
        var offset = north ? 0L : c.YearTicks / 2;
        var pos = (((tick + startTicks + offset) % c.YearTicks) + c.YearTicks) % c.YearTicks;
        var k = 0;
        long b0 = 0, b1 = c.YearTicks;
        var cum = Fix64.Zero;
        for (var i = 0; i < c.Seasons.Count; i++)
        {
            var lo = Scale(cum, c.YearTicks);
            cum += c.Seasons[i].Share;
            var hi = i == c.Seasons.Count - 1 ? c.YearTicks : Scale(cum, c.YearTicks);
            if (pos >= lo && pos < hi)
            {
                k = i;
                b0 = lo;
                b1 = hi;
                break;
            }
        }
        var season = c.Seasons[k];
        var seasonStart = tick - (pos - b0);
        var seasonEnd = seasonStart + (b1 - b0);
        var epochTicks = (long)s.EpochSeconds * Simulation.TickRate;
        var epochStart = seasonStart + (tick - seasonStart) / epochTicks * epochTicks;
        var epochEnd = System.Math.Min(seasonEnd, epochStart + epochTicks);

        // The calm start: clear everywhere until calm_start_seconds.
        var calmTicks = (long)s.CalmStartSeconds * Simulation.TickRate;
        if (tick < calmTicks)
        {
            var calm = Calm(c, map, tile, tick);
            return calm with { State = calm.State with { StartTick = 0, EndTick = calmTicks } };
        }

        // Spells from the epoch's start: each lasts spell_s (hashed); a sliver left at the end joins the last one.
        var minTicks = (long)season.SpellMinSeconds * Simulation.TickRate;
        var spanTicks = (long)(season.SpellMaxSeconds - season.SpellMinSeconds) * Simulation.TickRate;
        var seed = map.Seed ^ c.BodyHash;
        long start = epochStart, end = epochEnd;
        var index = 0;
        for (; index < 10000; index++)
        {
            var h = Hash(seed, cx, cy, epochStart, index, 1);
            var length = minTicks + (spanTicks > 0 ? (long)(h % (ulong)(spanTicks + 1)) : 0);
            end = start + System.Math.Max(Simulation.TickRate, length);
            if (epochEnd - end < minTicks / 2 || end > epochEnd) end = epochEnd;
            if (tick < end) break;
            start = end;
        }

        if (start < calmTicks) start = calmTicks;

        // Pick the kind by the season's weights × the band's multipliers.
        var total = Fix64.Zero;
        for (var i = 0; i < season.Weights.Count; i++) total += Weight(season, band, i);
        var kind = WeatherKind.Clear;
        if (total > Fix64.Zero)
        {
            var roll = Unit(Hash(seed, cx, cy, epochStart, index, 2)) * total;
            for (var i = 0; i < season.Weights.Count; i++)
            {
                var w = Weight(season, band, i);
                if (w <= Fix64.Zero) continue;
                kind = (WeatherKind)i;
                if (roll < w) break;
                roll -= w;
            }
        }
        // Rain falls as snow (and storms as blizzards) where it's freezing, and snow as rain where it isn't.
        var freezing = temperature < s.SnowBelowC;
        kind = kind switch
        {
            WeatherKind.Rain when freezing => WeatherKind.Snow,
            WeatherKind.Storm when freezing => WeatherKind.Blizzard,
            WeatherKind.Snow when !freezing => WeatherKind.Rain,
            WeatherKind.Blizzard when !freezing => WeatherKind.Storm,
            _ => kind,
        };

        var peak = Fix64.Ratio(35, 100) + Fix64.Ratio(65, 100) * Unit(Hash(seed, cx, cy, epochStart, index, 3));
        var rampTicks = System.Math.Min((s.RampShare * (int)System.Math.Min(end - start, int.MaxValue)).FloorToInt(), (long)s.RampMaxSeconds * Simulation.TickRate);
        var ramp = Fix64.One;
        if (rampTicks > 0)
        {
            var edge = System.Math.Min(tick - start, end - tick);
            ramp = edge >= rampTicks ? Fix64.One : Smooth(FromLongRatio(System.Math.Max(0, edge), rampTicks));
        }

        var def = rules.Of(kind);
        var clear = rules.Of(WeatherKind.Clear);
        var n = new ValueNoise(seed ^ 0x9E57UL);
        var channel = Fix64.FromInt(cx * 31 + cy * 7 + 3000);
        var nWind = n.Sample(secs / 90, channel);
        var nGust = n.Sample(secs / 3, channel + Fix64.FromInt(100) + Fix64.FromInt(tile % 997));
        var nDir = n.Sample(secs / 400, channel + Fix64.FromInt(200));

        var clearWind = clear.WindMin + (clear.WindMax - clear.WindMin) * nWind;
        var kindWind = def.WindMin + (def.WindMax - def.WindMin) * Fix64.Clamp(peak * (Fix64.Ratio(3, 4) + Fix64.Half * nWind), Fix64.Zero, Fix64.One);
        var wind = (clearWind + (kindWind - clearWind) * ramp) * template.WindScale * Blend(c.Seasons, SeasonPosition(c, tick, north), x => x.Wind) * LocalWind(c, map, tile);
        var gustFactor = clear.Gust + (def.Gust - clear.Gust) * ramp;
        var gust = wind * (Fix64.One + (gustFactor - Fix64.One) * nGust);
        var dir = Wrap360(band.WindTowardDeg + (nDir - Fix64.Half) * 80 + (Unit(Hash(seed, cx, cy, epochStart, index, 4)) - Fix64.Half) * 50);
        var precip = (def.PrecipMin + (def.PrecipMax - def.PrecipMin) * peak) * ramp;
        var visPeak = Fix64.One - (Fix64.One - def.Visibility) * (Fix64.Ratio(6, 10) + Fix64.Ratio(4, 10) * peak);
        var visibility = clear.Visibility + (visPeak - clear.Visibility) * ramp;
        var cloud = clear.Cloud + (def.Cloud - clear.Cloud) * ramp;
        return new WeatherCore(new WeatherState(kind, peak * ramp, wind, gust, dir, precip, visibility, cloud, start, end), ramp, def);
    }

    private static Fix64 Weight(SeasonDef season, LatitudeBand band, int i) =>
        Fix64.Max(Fix64.Zero, season.Weights[i] * (i < band.Multipliers.Count ? band.Multipliers[i] : Fix64.One));

    /// <summary>Terrain and altitude scale the wind (high ground is windier, forest calmer).</summary>
    private static Fix64 LocalWind(Context c, SurfaceMap map, int tile)
    {
        var terrain = c.Data?.Telemetry?.WeatherOf(map.Terrain(tile).Id)?.Wind ?? Fix64.One;
        var high = c.Env != null && c.Env.ReliefKm > Fix64.Zero ? Telemetry.AltitudeKm(c.Env, map, tile) / c.Env.ReliefKm : Fix64.Zero;
        return terrain * (Fix64.One + Fix64.Ratio(3, 5) * high);
    }

    /// <summary>No weather: clear (and still, without air). With air but no weather table, the class's mean wind drifting.</summary>
    private static WeatherCore Calm(Context c, SurfaceMap map, int tile, long tick)
    {
        var clear = c.Rules?.Of(WeatherKind.Clear);
        if (!c.Air) return new WeatherCore(new WeatherState(WeatherKind.Clear, Fix64.Zero, Fix64.Zero, Fix64.Zero, Fix64.Zero, Fix64.Zero,
            Fix64.One, Fix64.Zero, 0, long.MaxValue), Fix64.Zero, clear);
        var n = new ValueNoise(map.Seed ^ c.BodyHash ^ 0xCA1AUL);
        var secs = Seconds(tick);
        var nWind = n.Sample(secs / 90, Fix64.FromInt(tile % 997));
        var nGust = n.Sample(secs / 3, Fix64.FromInt(1000 + tile % 997));
        var mean = c.Env?.WindMean ?? Fix64.FromInt(3);
        var gustTop = c.Env?.WindGust ?? Fix64.FromInt(8);
        var local = LocalWind(c, map, tile);
        var wind = mean * (Fix64.Half + nWind) * local;
        var gust = wind + (gustTop - mean) * local * nGust;
        var dir = Wrap360(c.Template?.BandAt(Telemetry.Latitude(map, tile) * c.LatitudeSpanDeg).WindTowardDeg ?? Fix64.FromInt(90));
        return new WeatherCore(new WeatherState(WeatherKind.Clear, Fix64.Zero, wind, Fix64.Max(wind, gust), dir, Fix64.Zero,
            clear?.Visibility ?? Fix64.One, clear?.Cloud ?? Fix64.Zero, 0, long.MaxValue), Fix64.Zero, clear);
    }

    private static WeatherEffects EffectsOf(Context c, SurfaceMap map, int tile, WeatherCore w, Geo g)
    {
        var def = w.Def;
        if (def == null || c.Rules == null) return new WeatherEffects(Fix64.One, 0, Fix64.One, false, g.Daylight);
        var f = w.Ramp;
        var speed = Fix64.One + (def.RoverSpeed - Fix64.One) * f;
        var damage = Fix64.One + (def.RoverDamage - Fix64.One) * f;
        var terrain = map.Terrain(tile).Id;
        foreach (var m in c.Rules.Terrain)
        {
            if (!m.Weather.Contains(w.State.Kind) || !m.Terrain.Contains(terrain)) continue;
            speed *= Fix64.One + (m.RoverSpeed - Fix64.One) * f;
            damage *= Fix64.One + (m.RoverDamage - Fix64.One) * f;
        }
        var survey = f >= Fix64.Half ? def.SurveyRadiusDelta : 0;
        var grounded = (def.DropShipsGrounded && f >= GroundedRamp) || w.State.GustMs >= c.Rules.Surface.DropShipMaxGustMs;
        var solar = g.Daylight * (Fix64.One + (def.Solar - Fix64.One) * f);
        return new WeatherEffects(speed, survey, damage, grounded, solar);
    }

    // ---------------------------------------------------------------- seasons

    private static int SeasonIndex(IReadOnlyList<SeasonDef> seasons, Fix64 year)
    {
        var cum = Fix64.Zero;
        for (var i = 0; i < seasons.Count; i++)
        {
            cum += seasons[i].Share;
            if (year < cum) return i;
        }
        return System.Math.Max(0, seasons.Count - 1);
    }

    private static string SeasonName(IReadOnlyList<SeasonDef> seasons, int index) =>
        index >= 0 && index < seasons.Count ? seasons[index].Name : "Year-round";

    /// <summary>Year fraction of a hemisphere's seasons now (the south runs half a year behind).</summary>
    private static Fix64 SeasonPosition(Context c, long tick, bool north)
    {
        var year = YearFraction(c, tick);
        return north ? year : Frac(year + Fix64.Half);
    }

    /// <summary>A season value blended linearly between the middles of neighbouring seasons (cyclic).</summary>
    private static Fix64 Blend(IReadOnlyList<SeasonDef> seasons, Fix64 year, System.Func<SeasonDef, Fix64> value)
    {
        if (seasons.Count == 0) return Fix64.One;
        if (seasons.Count == 1) return value(seasons[0]);
        var cum = Fix64.Zero;
        var prev = seasons[^1];
        var prevMid = -(prev.Share / 2); // the middle of the last season, a year back
        var total = Fix64.Zero;
        foreach (var s in seasons) total += s.Share;
        for (var i = 0; i < seasons.Count; i++)
        {
            var mid = cum + seasons[i].Share / 2;
            if (year < mid)
            {
                var t = mid > prevMid ? (year - prevMid) / (mid - prevMid) : Fix64.Zero;
                return value(prev) + (value(seasons[i]) - value(prev)) * t;
            }
            prevMid = mid;
            prev = seasons[i];
            cum += seasons[i].Share;
        }
        // Past the last middle: toward the first season's middle a year on.
        var nextMid = total + seasons[0].Share / 2;
        var u = nextMid > prevMid ? (year - prevMid) / (nextMid - prevMid) : Fix64.Zero;
        return value(prev) + (value(seasons[0]) - value(prev)) * u;
    }

    // ---------------------------------------------------------------- math

    /// <summary>Ticks as seconds.</summary>
    private static Fix64 Seconds(long tick) =>
        Fix64.FromInt((int)(tick / Simulation.TickRate % 1_000_000_000)) + Fix64.Ratio((int)(tick % Simulation.TickRate), Simulation.TickRate);

    /// <summary>a / b for 0 ≤ a ≤ b without overflowing Fix64.</summary>
    private static Fix64 FromLongRatio(long a, long b)
    {
        if (b <= 0) return Fix64.Zero;
        return Fix64.FromRaw((long)(((System.Int128)a << Fix64.FractionalBits) / b));
    }

    /// <summary>floor(share × n) for a long n.</summary>
    private static long Scale(Fix64 share, long n) => (long)(((System.Int128)share.Raw * n) >> Fix64.FractionalBits);

    private static Fix64 Frac(Fix64 x) => x - Fix64.FromInt(x.FloorToInt());

    private static Fix64 Smooth(Fix64 t) => t * t * (Fix64.FromInt(3) - t * 2);

    private static Fix64 Wrap360(Fix64 deg)
    {
        var turns = Frac(deg / Deg360);
        return turns * Deg360;
    }

    /// <summary>sin(2π·turns).</summary>
    public static Fix64 SinTurns(Fix64 turns) => Telemetry.CosTurns(turns - Quarter);

    public static Fix64 SinDeg(Fix64 deg) => SinTurns(deg / Deg360);
    public static Fix64 CosDeg(Fix64 deg) => Telemetry.CosTurns(deg / Deg360);

    /// <summary>atan(z) in radians for |z| ≤ 1 (minimax polynomial, error ~1e-5).</summary>
    private static Fix64 AtanUnit(Fix64 z)
    {
        var z2 = z * z;
        var r = Fix64.Parse("0.0208351");
        r = r * z2 - Fix64.Parse("0.0851330");
        r = r * z2 + Fix64.Parse("0.1801410");
        r = r * z2 - Fix64.Parse("0.3302995");
        r = r * z2 + Fix64.Parse("0.9998660");
        return r * z;
    }

    /// <summary>atan2(y, x) in degrees (−180..180].</summary>
    public static Fix64 Atan2Deg(Fix64 y, Fix64 x)
    {
        if (x == Fix64.Zero && y == Fix64.Zero) return Fix64.Zero;
        Fix64 a;
        if (Fix64.Abs(y) <= Fix64.Abs(x))
        {
            a = AtanUnit(y / x);
            if (x < Fix64.Zero) a += y >= Fix64.Zero ? Pi : -Pi;
        }
        else
        {
            a = (y > Fix64.Zero ? Pi / 2 : -Pi / 2) - AtanUnit(x / y);
        }
        return a * RadToDeg;
    }

    /// <summary>asin(s) in degrees.</summary>
    public static Fix64 AsinDeg(Fix64 s)
    {
        s = Fix64.Clamp(s, -Fix64.One, Fix64.One);
        return Atan2Deg(s, Fix64.Sqrt(Fix64.Max(Fix64.Zero, Fix64.One - s * s)));
    }

    private static ulong Fnv(string s)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in s)
        {
            h ^= ch;
            h *= 1099511628211UL;
        }
        return h;
    }

    private static ulong Mix(ulong h)
    {
        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33;
        h *= 0xC4CEB9FE1A85EC53UL;
        h ^= h >> 33;
        return h;
    }

    private static ulong Hash(ulong seed, int cx, int cy, long epoch, int index, int salt)
    {
        var h = Mix(seed ^ 0xC11A7EUL);
        h = Mix(h ^ (ulong)(uint)cx * 0x9E3779B97F4A7C15UL);
        h = Mix(h ^ (ulong)(uint)cy * 0xC2B2AE3D27D4EB4FUL);
        h = Mix(h ^ (ulong)epoch);
        h = Mix(h ^ ((ulong)(uint)index << 8) ^ (ulong)(uint)salt);
        return h;
    }

    /// <summary>A hash as [0, 1).</summary>
    private static Fix64 Unit(ulong h) => Fix64.FromRaw((long)(h >> 32));
}
