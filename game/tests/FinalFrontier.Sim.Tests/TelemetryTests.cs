using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Planet environments (surfaces.json environments + telemetry) and the outpost's readings (Telemetry).</summary>
public class TelemetryTests
{
    private static GameData Data => TestData.Real;
    private static SurfaceMap Planet(ulong seed) => SurfaceMap.Generate(Data, Data.Surface("class_m"), seed);

    /// <summary>A landing site near the equator, like the first outpost would sit on.</summary>
    private static int Site(SurfaceMap map) =>
        Enumerable.Range(0, map.Grid.Count).Where(map.IsLandingSite).OrderBy(t => System.Math.Abs(map.Grid.Row(t) - map.Grid.Height / 2)).First();

    private static long DayTicks(EnvironmentDef env) => (long)env.DaySeconds * Simulation.TickRate;

    [Fact]
    public void EverySurfaceHasAnEnvironmentAndTheGasesAddUp()
    {
        Assert.NotNull(Data.Telemetry);
        foreach (var s in Data.Surfaces) Assert.NotNull(Data.EnvironmentOf(s.ClassId));
        foreach (var e in Data.Environments.Where(e => e.HasAtmosphere))
            Assert.InRange(e.Atmosphere.Sum(g => g.Amount.ToDouble()), 99.0, 101.0);
        Assert.True(Data.Environments.Count >= 6);
        Assert.Equal(Fix64.Parse("20.95"), Data.EnvironmentOf("class_m")!.Percent("O2"));
    }

    [Theory]
    [InlineData("\"O2\": 20.95, \"Ar\": 0.93", "\"O2\": 30.95, \"Ar\": 0.93", "add up to 100")]
    [InlineData("\"temperature_c\": { \"day\": 24, \"night\": 11,", "\"temperature_c\": { \"day\": 11, \"night\": 24,", "night temperature")]
    [InlineData("\"humidity_pct\": [25, 95]", "\"humidity_pct\": [95, 25]", "humidity_pct")]
    [InlineData("{ \"class\": \"class_h\",", "{ \"class\": \"class_q\",", "unknown planet class")]
    [InlineData("\"class\": \"class_m\", \"sky\"", "\"class\": \"class_x\", \"sky\"", "no environment")]
    [InlineData("\"pressure_kpa\": 0, \"scale_height_km\": 0,", "\"pressure_kpa\": 0, \"scale_height_km\": 0, \"x\": 1,", null)]
    [InlineData("\"radiation_warn_usv_h\": 10, \"radiation_danger_usv_h\": 250", "\"radiation_warn_usv_h\": 300, \"radiation_danger_usv_h\": 250", "radiation_warn")]
    [InlineData("\"desert\": { \"swing\"", "\"dunes\": { \"swing\"", "unknown terrain 'dunes'")]
    [InlineData("\"toxic_ppm\": { \"H2S\": 30, \"NH3\": 40 }", "\"toxic_ppm\": { \"h2s\": 30, \"NH3\": 40 }", "chemical formula")]
    public void BadEnvironmentDataIsCaught(string from, string to, string? expected)
    {
        var problems = TestData.Patched(("surfaces.json", from, to)).Validate();
        if (expected == null) Assert.Empty(problems);
        else Assert.Contains(problems, p => p.Contains(expected));
    }

    [Fact]
    public void ReadingsAreDeterministicAndDrift()
    {
        var map = Planet(1701);
        var tile = Site(map);
        var a = Telemetry.Read(Data, map, tile, 4000)!;
        var b = Telemetry.Read(Data, Planet(1701), tile, 4000)!;
        Assert.Equal(a.TemperatureC, b.TemperatureC);
        Assert.Equal(a.WindMs, b.WindMs);
        Assert.Equal(a.GustMs, b.GustMs);
        Assert.Equal(a.PressureKpa, b.PressureKpa);
        Assert.Equal(a.Gases.Select(g => g.Value), b.Gases.Select(g => g.Value));
        var later = Telemetry.Read(Data, map, tile, 4000 + 30 * Simulation.TickRate)!;
        Assert.NotEqual(a.TemperatureC, later.TemperatureC);
        Assert.NotEqual(a.GustMs, later.GustMs);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(1701UL)]
    [InlineData(99999UL)]
    public void ClassMReadingsAreEarthLike(ulong seed)
    {
        var map = Planet(seed);
        var tile = Site(map);
        var env = Data.EnvironmentOf("class_m")!;
        var day = DayTicks(env);
        var readings = Enumerable.Range(0, 48).Select(i => Telemetry.Read(Data, map, tile, 1000 + i * day / 48)!).ToList();
        foreach (var r in readings)
        {
            Assert.InRange(r.TemperatureC.ToDouble(), -15, 45);
            Assert.InRange(r.HumidityPct.ToDouble(), 25, 95);
            Assert.InRange(r.PressureKpa.ToDouble(), 70, 103);
            Assert.InRange(r.O2PartialKpa.ToDouble(), 14, 22);
            Assert.InRange(r.WindMs.ToDouble(), 0, 45); // storms and high winds blow 14-30 m/s (climate.json weather), more on high ground
            Assert.True(r.GustMs >= r.WindMs);
            Assert.InRange(r.WindFromDeg, 0, 359);
            Assert.InRange(r.RadiationUsvH.ToDouble(), 0.05, 2);
            Assert.InRange(r.UvIndex.ToDouble(), 0, 14);
            Assert.InRange(r.LocalHours.ToDouble(), 0, 26);
            Assert.DoesNotContain(r.Hazards, h => h.Kind is HazardKind.Unbreathable or HazardKind.Vacuum);
            Assert.Contains(r.Gases, g => g.Gas == "N2" && g.Percent);
        }
        // A day/night cycle: several degrees between the warmest and coldest hour, warmest in daylight.
        var warmest = readings.MaxBy(r => r.TemperatureC)!;
        var coldest = readings.MinBy(r => r.TemperatureC)!;
        Assert.True((warmest.TemperatureC - coldest.TemperatureC).ToDouble() > 6);
        Assert.True(warmest.Daylight);
        Assert.False(coldest.Daylight);
        Assert.Contains(readings, r => r.Daylight);
        Assert.Contains(readings, r => !r.Daylight);
    }

    [Fact]
    public void PolesAndHighGroundAreColder()
    {
        var map = Planet(1701);
        var env = Data.EnvironmentOf("class_m")!;
        double Mean(Func<int, bool> pick) => Enumerable.Range(0, map.Grid.Count).Where(pick).Take(200)
            .Average(t => Telemetry.Read(Data, map, t, 5000)!.TemperatureC.ToDouble());
        var equator = Mean(t => System.Math.Abs(map.Grid.Row(t) - map.Grid.Height / 2) <= 2 && map.Terrain(t).Id == "plains");
        var poles = Mean(t => map.Grid.Row(t) <= 1);
        Assert.True(equator - poles > 20, $"equator {equator:0.0} poles {poles:0.0}");
        var mountain = Enumerable.Range(0, map.Grid.Count).Where(t => map.Terrain(t).Id == "mountains").MaxBy(t => map.Elevation[t]);
        Assert.True(Telemetry.AltitudeKm(env, map, mountain) > Fix64.FromInt(4));
        var low = Telemetry.Read(Data, map, Site(map), 5000)!;
        var high = Telemetry.Read(Data, map, mountain, 5000)!;
        Assert.True(high.PressureKpa < low.PressureKpa);
        Assert.True(high.RadiationUsvH > low.RadiationUsvH * Fix64.Ratio(9, 10));
    }

    [Fact]
    public void ToxicPatchesFoulTheAirNearby()
    {
        var map = Planet(1701);
        var toxic = Enumerable.Range(0, map.Grid.Count).First(t => map.Toxic[t]);
        Assert.Equal(Fix64.One, Telemetry.ToxicExposure(map, toxic));
        var on = Telemetry.Read(Data, map, toxic, 3000)!;
        var h2s = on.Gases.Single(g => g.Gas == "H2S");
        Assert.True(h2s.Ppm > Fix64.FromInt(10));
        Assert.Contains(on.Hazards, h => h.Kind == HazardKind.ToxicGas && h.Gas == "H2S");
        var clean = Enumerable.Range(0, map.Grid.Count).First(t => Telemetry.ToxicExposure(map, t) == Fix64.Zero && !map.IsWater(t));
        Assert.DoesNotContain(Telemetry.Read(Data, map, clean, 3000)!.Gases, g => g.Gas == "H2S");
    }

    [Theory]
    [InlineData("class_k", HazardKind.Unbreathable, null)]
    [InlineData("class_l", HazardKind.Unbreathable, "CO2")]
    [InlineData("class_y", HazardKind.Heat, "SO2")]
    [InlineData("class_d", HazardKind.Vacuum, null)]
    [InlineData("ice_moon", HazardKind.Unbreathable, null)]
    public void HostileWorldsRaiseHazards(string classId, HazardKind kind, string? gas)
    {
        var map = Planet(1701);
        var env = Data.EnvironmentOf(classId)!;
        var r = Telemetry.Read(env, Data.Telemetry!, map, Site(map), 2000);
        Assert.Contains(r.Hazards, h => h.Kind == kind && h.Severe);
        if (gas != null) Assert.Contains(r.Hazards, h => h.Kind == HazardKind.ToxicGas && h.Gas == gas);
        if (gas != null) Assert.True(r.Gases.Single(g => g.Gas == gas).OverLimit);
    }

    [Fact]
    public void EveryEnvironmentStaysPhysicalEverywhere()
    {
        var map = Planet(42);
        var rules = Data.Telemetry!;
        foreach (var env in Data.Environments)
            for (var t = 0; t < map.Grid.Count; t += 211)
                for (var i = 0; i < 6; i++)
                {
                    var r = Telemetry.Read(env, rules, map, t, 777 + i * DayTicks(env) / 6);
                    var where = $"{env.ClassId} tile {t}";
                    Assert.True(r.TemperatureC.ToDouble() > -273, where);
                    Assert.InRange(r.HumidityPct.ToDouble(), env.HumidityMin.ToDouble(), env.HumidityMax.ToDouble());
                    Assert.True(r.WindMs >= Fix64.Zero && r.GustMs >= r.WindMs, where);
                    Assert.InRange(r.PressureKpa.ToDouble(), 0, env.PressureKpa.ToDouble() * 1.02);
                    Assert.InRange(r.CloudCover.ToDouble(), 0, 1);
                    Assert.True(r.RadiationUsvH >= env.RadiationNight * Fix64.Ratio(9, 10), where);
                    Assert.True(r.UvIndex >= Fix64.Zero, where);
                    Assert.Equal(env.HasAtmosphere, r.Sky != SkyCondition.Vacuum);
                }
    }

    [Fact]
    public void MathHelpersAreAccurate()
    {
        for (var i = -20; i <= 40; i++)
        {
            var turns = i / 17.0;
            Assert.Equal(System.Math.Cos(turns * 2 * System.Math.PI), Telemetry.CosTurns(Fix64.FromDouble(turns)).ToDouble(), 4);
        }
        foreach (var x in new[] { -12.0, -3.3, -1, -0.25, 0, 0.4, 2.5 })
            Assert.Equal(System.Math.Exp(x), Telemetry.Exp(Fix64.FromDouble(x)).ToDouble(), 3);
    }
}
