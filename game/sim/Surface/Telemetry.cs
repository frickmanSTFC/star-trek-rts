using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>The sky over the outpost, from its humidity, cloud, wind and temperature readings.</summary>
public enum SkyCondition
{
    Clear,
    PartlyCloudy,
    Overcast,
    Fog,
    Rain,
    Snow,
    DustStorm,

    /// <summary>No atmosphere at all.</summary>
    Vacuum,
}

/// <summary>What a reading warns about.</summary>
public enum HazardKind
{
    /// <summary>Not breathable: too little or too much oxygen, or the pressure is out of range (Value = O2 %, Limit = pressure kPa).</summary>
    Unbreathable,

    /// <summary>No atmosphere: suits only.</summary>
    Vacuum,

    /// <summary>A gas above its exposure limit (Gas; Value = sea-level-equivalent ppm, Limit = the limit).</summary>
    ToxicGas,

    Radiation,
    Heat,
    Cold,
    HighWinds,
    DustStorm,
}

/// <summary>One hazard: Severe ones are well past their limit (10× for gases, the danger level for radiation).</summary>
public sealed record TelemetryHazard(HazardKind Kind, string Gas, Fix64 Value, Fix64 Limit, bool Severe)
{
    /// <summary>A stable key for "already reported" checks (gas hazards per gas).</summary>
    public string Key => Kind == HazardKind.ToxicGas ? $"gas:{Gas}" : Kind.ToString();
}

/// <summary>
/// One gas in the air: Value in percent for a main gas (Percent = true), in ppm for a trace gas. Ppm is always the
/// concentration in ppm; LimitPpm the exposure limit (null = none), compared against the concentration scaled to sea-level
/// pressure (thin air carries less of a gas per breath).
/// </summary>
public sealed record GasReading(string Gas, Fix64 Value, bool Percent, Fix64 Ppm, Fix64? LimitPpm, bool OverLimit);

/// <summary>
/// The outpost sensors' readings at one tile and tick. Temperatures in °C, humidity in %, wind in m/s (WindFromDeg = the
/// compass bearing it blows from, 0 = north = the map's top), pressure in kPa, radiation in µSv/h, altitude in km,
/// latitude as a share of the map's half-height (+1 = the top row, -1 = the bottom). Sun = height of the sun (-1..1).
/// </summary>
public sealed record SurfaceReadings(
    int Tile,
    long Tick,
    Fix64 LocalHours,
    Fix64 Sun,
    Fix64 AltitudeKm,
    Fix64 Latitude,
    Fix64 TemperatureC,
    Fix64 HumidityPct,
    Fix64 WindMs,
    Fix64 GustMs,
    int WindFromDeg,
    Fix64 PressureKpa,
    Fix64 O2PartialKpa,
    Fix64 RadiationUsvH,
    Fix64 UvIndex,
    Fix64 GravityG,
    Fix64 CloudCover,
    SkyCondition Sky,
    IReadOnlyList<GasReading> Gases,
    IReadOnlyList<TelemetryHazard> Hazards)
{
    public bool Daylight => Sun > Fix64.Zero;
}

/// <summary>
/// The outpost's environmental telemetry: pure, deterministic functions of the data (surfaces.json environments +
/// telemetry), the map, a tile and a tick. Nothing is stored and nothing is rolled on the sim's RNG; the "weather" is
/// value noise along time, seeded from the map. Shape: a day/night cycle by the tile's longitude (DaySeconds per day),
/// colder toward the poles and with altitude, wetter by water and plants, windier on high ground, slowly drifting
/// weather and gusts, toxic patches fouling the air nearby.
/// </summary>
public static class Telemetry
{
    private static readonly Fix64 TwoPi = Fix64.Parse("6.283185307179586");
    private static readonly Fix64 Quarter = Fix64.Ratio(1, 4);
    private static readonly Fix64 SeaLevelKpa = Fix64.Parse("101.325");
    private static readonly Fix64 Hundred = Fix64.FromInt(100);

    /// <summary>The readings at a tile, or null when the planet class has no environment (or the data no telemetry rules).</summary>
    public static SurfaceReadings? Read(GameData data, SurfaceMap map, int tile, long tick)
    {
        var env = data.EnvironmentOf(map.Def.ClassId);
        var rules = data.Telemetry;
        if (env == null || rules == null || tile < 0 || tile >= map.Grid.Count) return null;
        return Read(env, rules, map, tile, tick);
    }

    public static SurfaceReadings Read(EnvironmentDef env, TelemetryRules rules, SurfaceMap map, int tile, long tick)
    {
        var terrain = map.Terrain(tile);
        var weather = rules.WeatherOf(terrain.Id);
        var noise = new ValueNoise(map.Seed ^ 0x7E1E3E7A5UL);
        Fix64 N(int channel, int periodSeconds) =>
            noise.Sample(TimeIn(tick, periodSeconds * Simulation.TickRate), Fix64.FromInt(channel * 1000 + tile % 997));

        var atmosphere = env.HasAtmosphere;
        var altitude = AltitudeKm(env, map, tile);
        var latitude = Latitude(map, tile);
        var latSq = latitude * latitude;

        // Day and night: local solar time from the tile's longitude. Phase 0 = midnight, 0.5 = noon; the warmest hour
        // lags noon (0.6, about 14:24 on a 24-hour clock).
        var phase = DayPhase(env, map, tile, tick);
        var sun = CosTurns(phase - Fix64.Half) * (Fix64.One - Fix64.Ratio(2, 5) * latSq);
        var sunlight = Fix64.Max(Fix64.Zero, sun);
        var thermal = CosTurns(phase - Fix64.Ratio(3, 5));

        var mean = (env.DayC + env.NightC) / 2;
        // Terrain sets the swing through moisture and air; with no atmosphere it barely matters.
        var terrainSwing = Fix64.One + (weather.Swing - Fix64.One) * (atmosphere ? Fix64.One : Quarter);
        var swing = (env.DayC - env.NightC) / 2 * terrainSwing;
        var jitter = atmosphere ? Fix64.Parse("1.5") : Fix64.Half;
        var temperature = mean + swing * thermal - env.PolarDropC * latSq - env.LapsePerKm * altitude + weather.OffsetC
                          + (N(1, 60) - Fix64.Half) * 2 * jitter;
        temperature = Fix64.Max(temperature, Fix64.FromInt(-270)); // nothing reads colder than a few kelvin

        // Humidity: the ground's wetness picks within the class's range; drier in the afternoon, damper at night.
        var wet = map.IsWater(tile) ? Fix64.One
            : (Fix64.Ratio(35, 100) + Fix64.Ratio(2, 5) * map.Biomass[tile] + (map.TouchesWater(tile) ? Quarter : Fix64.Zero)) * weather.Humidity;
        wet = Fix64.Clamp(wet, Fix64.Zero, Fix64.One);
        var humidity = env.HumidityMin + (env.HumidityMax - env.HumidityMin) * wet;
        humidity = humidity * (Fix64.One - Fix64.Ratio(18, 100) * thermal) + (N(2, 45) - Fix64.Half) * 8;
        humidity = Fix64.Clamp(humidity, env.HumidityMin, env.HumidityMax);

        // Wind: stronger on high ground, slowly rising and falling, with gusts on top.
        var high = env.ReliefKm > Fix64.Zero ? altitude / env.ReliefKm : Fix64.Zero;
        var wind = env.WindMean * weather.Wind * (Fix64.One + Fix64.Ratio(3, 5) * high) * (Fix64.Half + N(3, 90));
        var gust = wind + (env.WindGust - env.WindMean) * weather.Wind * N(4, 3);
        var windFrom = (N(5, 400) * 720).FloorToInt() % 360;

        // Pressure: falls with altitude, drifts a little with the weather.
        var weatherNoise = N(6, 240);
        var pressure = atmosphere
            ? env.PressureKpa * Exp(-(altitude / env.ScaleHeightKm)) * (Fix64.One + (weatherNoise - Fix64.Half) * Fix64.Ratio(24, 1000))
            : Fix64.Zero;
        var o2 = pressure * env.Percent("O2") / Hundred;

        var cloud = atmosphere
            ? Fix64.Clamp((humidity - Fix64.FromInt(40)) / 55 * (Fix64.Ratio(3, 5) + Fix64.Ratio(4, 5) * weatherNoise), Fix64.Zero, Fix64.One)
            : Fix64.Zero;

        var radiation = (env.RadiationNight + (env.RadiationDay - env.RadiationNight) * sunlight)
                        * (Fix64.One + Fix64.Ratio(12, 100) * altitude) * (Fix64.One + Quarter * latSq)
                        * (Fix64.Ratio(95, 100) + Fix64.Ratio(1, 10) * N(7, 10));
        var uv = env.UvIndex * sunlight * (Fix64.One + Fix64.Ratio(6, 100) * altitude) * (Fix64.One - Fix64.Ratio(3, 5) * cloud);

        var sky = !atmosphere ? SkyCondition.Vacuum
            : weather.Humidity < Fix64.Half && gust >= Fix64.FromInt(15) && humidity < Fix64.FromInt(40) ? SkyCondition.DustStorm
            : humidity >= Fix64.FromInt(90) && cloud >= Fix64.Ratio(3, 4) ? (temperature <= Fix64.One ? SkyCondition.Snow : SkyCondition.Rain)
            : humidity >= Fix64.FromInt(95) && wind < Fix64.FromInt(3) ? SkyCondition.Fog
            : cloud >= Fix64.Ratio(7, 10) ? SkyCondition.Overcast
            : cloud >= Fix64.Ratio(3, 10) ? SkyCondition.PartlyCloudy
            : SkyCondition.Clear;

        // The air: main gases as measured, trace gases with a little drift, plus whatever a nearby toxic patch gives off.
        var gases = new List<GasReading>();
        var hazards = new List<TelemetryHazard>();
        var toPressure = atmosphere ? pressure / SeaLevelKpa : Fix64.Zero;
        void AddGas(string gas, Fix64 value, bool percent)
        {
            var ppm = percent ? value * 10000 : value;
            var limit = rules.LimitPpm(gas);
            var effective = ppm * toPressure;
            var over = limit is { } l && effective > l;
            gases.Add(new GasReading(gas, value, percent, ppm, limit, over));
            if (over) hazards.Add(new TelemetryHazard(HazardKind.ToxicGas, gas, effective, limit!.Value, effective > limit.Value * 10));
        }
        foreach (var g in env.Atmosphere) AddGas(g.Gas, g.Amount, true);
        var exposure = ToxicExposure(map, tile);
        var channel = 20;
        foreach (var g in env.Trace)
        {
            var value = g.Amount * (Fix64.Ratio(95, 100) + Fix64.Ratio(1, 10) * N(channel++, 20)) + ToxicPpm(env, g.Gas) * exposure * (Fix64.Ratio(3, 5) + Fix64.Ratio(4, 5) * N(channel++, 8));
            AddGas(g.Gas, value, false);
        }
        foreach (var g in env.Toxic)
        {
            if (Listed(env.Trace, g.Gas) || exposure <= Fix64.Zero) continue;
            AddGas(g.Gas, g.Amount * exposure * (Fix64.Ratio(3, 5) + Fix64.Ratio(4, 5) * N(channel++, 8)), false);
        }

        if (!atmosphere) hazards.Insert(0, new TelemetryHazard(HazardKind.Vacuum, "", Fix64.Zero, Fix64.Zero, true));
        else
        {
            var o2Share = env.Percent("O2");
            if (o2Share < rules.BreathableO2Min || o2Share > rules.BreathableO2Max || pressure < rules.BreathableKpaMin || pressure > rules.BreathableKpaMax)
                hazards.Insert(0, new TelemetryHazard(HazardKind.Unbreathable, "O2", o2Share, pressure, true));
        }
        if (radiation >= rules.RadiationWarn) hazards.Add(new TelemetryHazard(HazardKind.Radiation, "", radiation, rules.RadiationWarn, radiation >= rules.RadiationDanger));
        if (temperature >= rules.HeatWarnC) hazards.Add(new TelemetryHazard(HazardKind.Heat, "", temperature, rules.HeatWarnC, temperature >= rules.HeatWarnC * 2));
        if (temperature <= rules.ColdWarnC) hazards.Add(new TelemetryHazard(HazardKind.Cold, "", temperature, rules.ColdWarnC, temperature <= rules.ColdWarnC * 2));
        if (gust >= rules.GustWarn) hazards.Add(new TelemetryHazard(HazardKind.HighWinds, "", gust, rules.GustWarn, gust >= rules.GustWarn * 2));
        if (sky == SkyCondition.DustStorm) hazards.Add(new TelemetryHazard(HazardKind.DustStorm, "", gust, Fix64.FromInt(15), false));

        return new SurfaceReadings(tile, tick, phase * env.DayHours, sun, altitude, latitude, temperature, humidity, wind, gust, windFrom,
            pressure, o2, radiation, uv, env.GravityG, cloud, sky, gases, hazards);
    }

    /// <summary>Where the tile is in its local day: 0 = midnight, 0.5 = noon. Longitude shifts it (the map wraps east–west).</summary>
    public static Fix64 DayPhase(EnvironmentDef env, SurfaceMap map, int tile, long tick) =>
        Frac(TimeIn(tick, env.DaySeconds * Simulation.TickRate) + Fix64.Ratio(map.Grid.Column(tile), map.Grid.Width) + Fix64.Ratio(35, 100));

    /// <summary>Altitude in km: land rises with the cube of its elevation share above the ocean (lowlands stay low), to ReliefKm; water is 0.</summary>
    public static Fix64 AltitudeKm(EnvironmentDef env, SurfaceMap map, int tile)
    {
        if (map.IsWater(tile)) return Fix64.Zero;
        var ocean = map.Def.OceanBelow;
        var share = ocean >= Fix64.One ? Fix64.Zero : Fix64.Clamp((map.Elevation[tile] - ocean) / (Fix64.One - ocean), Fix64.Zero, Fix64.One);
        return env.ReliefKm * share * share * share;
    }

    /// <summary>Latitude as a share of the map's half-height: +1 = the top row (north), -1 = the bottom row.</summary>
    public static Fix64 Latitude(SurfaceMap map, int tile)
    {
        var rows = map.Grid.Height - 1;
        if (rows <= 0) return Fix64.Zero;
        return Fix64.FromInt(rows - 2 * map.Grid.Row(tile)) / rows;
    }

    /// <summary>How much a toxic patch fouls the air here: 1 on it, 1/(1+d)² at d tiles, nothing beyond 3.</summary>
    public static Fix64 ToxicExposure(SurfaceMap map, int tile)
    {
        var best = int.MaxValue;
        foreach (var t in map.Grid.Within(tile, 3))
            if (map.Toxic[t]) best = System.Math.Min(best, map.Grid.Distance(tile, t));
        if (best == int.MaxValue) return Fix64.Zero;
        return Fix64.One / ((best + 1) * (best + 1));
    }

    private static Fix64 ToxicPpm(EnvironmentDef env, string gas)
    {
        foreach (var g in env.Toxic)
            if (g.Gas == gas) return g.Amount;
        return Fix64.Zero;
    }

    private static bool Listed(IReadOnlyList<GasAmount> list, string gas)
    {
        foreach (var g in list)
            if (g.Gas == gas) return true;
        return false;
    }

    /// <summary>tick / periodTicks without overflowing Fix64's integer part for long games.</summary>
    private static Fix64 TimeIn(long tick, int periodTicks)
    {
        if (periodTicks <= 0) return Fix64.Zero;
        var whole = tick / periodTicks;
        var rest = (int)(tick % periodTicks);
        return Fix64.FromInt((int)(whole % 1_000_000)) + Fix64.Ratio(rest, periodTicks);
    }

    private static Fix64 Frac(Fix64 x) => x - Fix64.FromInt(x.FloorToInt());

    /// <summary>cos(2π·turns), from a Taylor series on [0, π/2] (error about 3e-6).</summary>
    public static Fix64 CosTurns(Fix64 turns)
    {
        var x = Frac(turns);
        if (x > Fix64.Half) x = Fix64.One - x;
        var sign = Fix64.One;
        if (x > Quarter)
        {
            x = Fix64.Half - x;
            sign = -Fix64.One;
        }
        var a = x * TwoPi;
        var a2 = a * a;
        var a4 = a2 * a2;
        var r = Fix64.One - a2 / 2 + a4 / 24 - a4 * a2 / 720 + a4 * a4 / 40320;
        return sign * r;
    }

    /// <summary>e^x by halving into range, a short series and squaring back (meant for x within about ±20).</summary>
    public static Fix64 Exp(Fix64 x)
    {
        var halvings = 0;
        while (Fix64.Abs(x) > Fix64.Half && halvings < 30)
        {
            x = x / 2;
            halvings++;
        }
        var term = Fix64.One;
        var sum = Fix64.One;
        for (var i = 1; i < 10; i++)
        {
            term = term * x / i;
            sum += term;
        }
        for (var i = 0; i < halvings; i++) sum = sum * sum;
        return sum;
    }
}
