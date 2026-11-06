using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Planetary climate (data/climate.json + Surface/Climate.cs): day/night, seasons, weather and its effects.</summary>
public class ClimateTests
{
    private static GameData Data => TestData.Real;
    private static SurfaceMap Planet(ulong seed, string? body = null, GameData? data = null) =>
        SurfaceMap.Generate(data ?? Data, (data ?? Data).Surface("class_m"), seed, body);

    private static long DayTicks(GameData data, string body) => (long)data.Climate!.Body(body)!.DaySeconds * Simulation.TickRate;
    private static long CalmTicks(GameData data) => (long)data.Climate!.Surface.CalmStartSeconds * Simulation.TickRate;

    /// <summary>Column distance going east from a to b, 0..width.</summary>
    private static double East(double a, double b, int width) => ((b - a) % width + width) % width;

    /// <summary>A copy of the data where every season's weather is a storm and the map starts stormy (no calm start).</summary>
    private static GameData Stormy() => TestData.Regexed("climate.json",
        ("\"calm_start_seconds\": 120", "\"calm_start_seconds\": 0"),
        ("\"weights\": \\{ \"clear\"[^}]*\\}", "\"weights\": { \"storm\": 1 }"));

    [Fact]
    public void EveryMapBodyHasClimateAndTheDataChecksPass()
    {
        var c = Data.Climate!;
        foreach (var m in Data.Maps)
            foreach (var b in m.Bodies)
            {
                Assert.NotNull(c.Body(b.Id));
                Assert.NotNull(Climate.Summary(Data, b.Id));
            }
        Assert.Equal(Enum.GetValues<WeatherKind>().Length, c.Weather.Count);
        Assert.Equal("tarsis", Planet(1).ClimateBodyId); // the local player's home body on mvp_duel
        Assert.Equal(720, c.Body("tarsis")!.DaySeconds); // a local day stays ~12 min of play
        Assert.Equal(Data.EnvironmentOf("class_m")!.DaySeconds, c.Body("tarsis")!.DaySeconds);
        Assert.DoesNotContain(Data.Validate(), p => p.Contains("limate") || p.Contains("eather"));
    }

    [Fact]
    public void TwinBodiesOfTheDuelMapHaveTheSameClimate()
    {
        var map = Data.Map(MatchSetup.MvpMap);
        foreach (var b in map.Bodies)
        {
            var twin = map.Bodies.Single(o => o.Position == -b.Position);
            var x = Data.Climate!.Body(b.Id)!;
            var y = Data.Climate!.Body(twin.Id)!;
            Assert.Equal(x with { Id = "", Parent = null, Notes = "", Seasons = y.Seasons }, y with { Id = "", Parent = null, Notes = "" });
            Assert.Equal(x.Parent == null, y.Parent == null);
            Assert.Equal(x.Seasons.Count, y.Seasons.Count);
        }
    }

    [Theory]
    [InlineData("\"tarsis_a\":   {", "\"tarsis_x\":   {", "Map body tarsis_a")]
    [InlineData("\"clear\": 28,", "\"clear\": -28,", "can't be negative")]
    [InlineData("{ \"name\": \"Spring\", \"share\": 0.25", "{ \"name\": \"Spring\", \"share\": 0.35", "add up to 1")]
    [InlineData("\"climate\": \"marginal\"", "\"climate\": \"marginalx\"", "unknown climate 'marginalx'")]
    [InlineData("\"weights\": { \"clear\": 1 }", "\"weights\": { \"clear\": 0 }", "positive sum")]
    [InlineData("\"dust\": 2,  \"high_wind\": 3", "\"dusty\": 2,  \"high_wind\": 3", "unknown weather kind 'dusty'")]
    [InlineData("\"year_days\": 4,", "\"year_days\": 5,", "must match its planet's")]
    [InlineData("\"greenhouse_c\": 0,  \"climate\": \"airless\",   \"landable\": false,\n                    \"notes\": \"Airless moon", "\"greenhouse_c\": 0,  \"climate\": \"temperate\",   \"landable\": false,\n                    \"notes\": \"Airless moon", "class has no atmosphere")]
    [InlineData("\"albedo\": 0.3,", "\"albedo\": 1.3,", "albedo")]
    [InlineData("{ \"id\": \"storm\",      \"name\": \"Storm\"", "{ \"id\": \"squall\",      \"name\": \"Storm\"", "'squall': not a weather kind")]
    public void BadClimateDataIsCaught(string from, string to, string expected)
    {
        var problems = TestData.Patched(("climate.json", from, to)).Validate();
        Assert.Contains(problems, p => p.Contains(expected));
    }

    [Theory]
    [InlineData("tarsis", false)]
    [InlineData("west_l", true)] // Veyra spins retrograde: the sun rises in the west
    public void DayAndNightMoveAcrossTheColumnsOnceADay(string body, bool retrograde)
    {
        var map = Planet(1701, body);
        var width = map.Grid.Width;
        var day = DayTicks(Data, body);
        var a = Climate.Sun(Data, map, 1000);
        var quarter = Climate.Sun(Data, map, 1000 + day / 4);
        var full = Climate.Sun(Data, map, 1000 + day);
        // A quarter of a day moves the subsolar point a quarter of the map: west when prograde, east when retrograde.
        var east = East(a.SubsolarColumn.ToDouble(), quarter.SubsolarColumn.ToDouble(), width);
        Assert.Equal(retrograde ? width / 4.0 : width * 3 / 4.0, east, 1);
        Assert.Equal(a.SubsolarColumn.ToDouble(), full.SubsolarColumn.ToDouble(), 2);

        // Under the subsolar column on the equator it's day; half a map away it's night.
        var row = map.Grid.Height / 2;
        var noon = map.Grid.Index((int)a.SubsolarColumn.ToDouble() % width, row);
        var midnight = map.Grid.Index(((int)a.SubsolarColumn.ToDouble() + width / 2) % width, row);
        var lit = Climate.At(Data, map, noon, 1000);
        Assert.False(lit.Night);
        Assert.True(lit.SunElevationDeg > Fix64.FromInt(55), $"elevation {lit.SunElevationDeg}");
        Assert.True(lit.Daylight > Fix64.Ratio(8, 10));
        var dark = Climate.At(Data, map, midnight, 1000);
        Assert.True(dark.Night);
        Assert.Equal(Fix64.Zero, dark.Daylight);

        // The morning sun is in the east (west when retrograde). Local time runs later to the east when prograde (the
        // sun has already passed overhead there), earlier when retrograde, so the morning lies west of noon, or east.
        var morning = map.Grid.Index(((int)a.SubsolarColumn.ToDouble() + (retrograde ? width / 6 : -width / 6) + width) % width, row);
        var az = Climate.At(Data, map, morning, 1000).SunAzimuthDeg.ToDouble();
        if (retrograde) Assert.InRange(az, 200, 340);
        else Assert.InRange(az, 20, 160);
    }

    [Fact]
    public void ABodyLockedToItsStarNeverChanges()
    {
        var data = TestData.Patched(("climate.json",
            "\"day_seconds\": 720,  \"day_hours\": 26,  \"direction\": \"prograde\",   \"locked\": \"none\" },   \"axial_tilt_deg\": 23.5, \"year_days\": 8,",
            "\"day_seconds\": 720,  \"day_hours\": 26,  \"direction\": \"prograde\",   \"locked\": \"star\", \"facing\": 0.25 },   \"axial_tilt_deg\": 0, \"year_days\": 1,"));
        var map = Planet(1701, "tarsis", data);
        var width = map.Grid.Width;
        var first = Climate.Sun(data, map, 0);
        Assert.Equal(width / 4.0, first.SubsolarColumn.ToDouble(), 3);
        var lit = map.Grid.Index(width / 4, map.Grid.Height / 2);
        var dark = map.Grid.Index(width * 3 / 4, map.Grid.Height / 2);
        foreach (var tick in new long[] { 0, 777, 10_800, 99_999, 1_234_567 })
        {
            var sun = Climate.Sun(data, map, tick);
            Assert.Equal(first.SubsolarColumn, sun.SubsolarColumn);
            Assert.Equal(first.DeclinationDeg, sun.DeclinationDeg);
            Assert.Equal(Climate.At(data, map, lit, 0).SunElevationDeg, Climate.At(data, map, lit, tick).SunElevationDeg);
            Assert.False(Climate.At(data, map, lit, tick).Night);
            Assert.True(Climate.At(data, map, dark, tick).Night);
        }
        // The light side is far hotter than the dark side, always.
        Assert.True(Climate.At(data, map, lit, 5000).TemperatureC - Climate.At(data, map, dark, 5000).TemperatureC > Fix64.FromInt(8));
    }

    [Fact]
    public void SeasonsFlipBetweenHemispheres()
    {
        var map = Planet(1701);
        var seasons = Climate.SeasonsOf(Data, map);
        Assert.Equal(new[] { "Spring", "Summer", "Autumn", "Winter" }, seasons.Select(s => s.Name));
        var north = map.Grid.Index(30, 16);
        var south = map.Grid.Index(30, map.Grid.Height - 17);
        var day = DayTicks(Data, "tarsis");
        var year = (long)(Data.Climate!.Body("tarsis")!.YearSeconds * Simulation.TickRate).FloorToInt();
        var seen = new HashSet<int>();
        for (var i = 0; i < 16; i++)
        {
            var tick = i * year / 16;
            var sun = Climate.Sun(Data, map, tick);
            var n = Climate.At(Data, map, north, tick);
            var s = Climate.At(Data, map, south, tick);
            Assert.Equal(sun.SeasonIndex, n.SeasonIndex);
            Assert.Equal((n.SeasonIndex + 2) % 4, s.SeasonIndex);
            seen.Add(n.SeasonIndex);
            // Northern summer: the sun stands north of the equator.
            if (n.SeasonName == "Summer") Assert.True(sun.DeclinationDeg > Fix64.Zero);
            if (n.SeasonName == "Winter") Assert.True(sun.DeclinationDeg < Fix64.Zero);
        }
        Assert.Equal(4, seen.Count);

        // Mean temperature over a day in the north's midsummer vs its midwinter (mid-latitudes), and the south the other way round.
        double Mean(int tile, long from) => Enumerable.Range(0, 24).Average(h => Climate.At(Data, map, tile, from + h * day / 24).TemperatureC.ToDouble());
        var start = Data.Climate.Body("tarsis")!.YearStart.ToDouble();
        long At(double yearFraction) => (long)(((yearFraction - start + 1) % 1) * year);
        var northSummer = Mean(north, At(0.33));
        var northWinter = Mean(north, At(0.83));
        Assert.True(northSummer - northWinter > 8, $"north summer {northSummer:0.0} winter {northWinter:0.0}");
        Assert.True(Mean(south, At(0.83)) - Mean(south, At(0.33)) > 8);
    }

    [Fact]
    public void SunlightFallsWithDistance()
    {
        var bodies = Data.Climate!.Bodies.Where(b => !b.IsMoon).OrderBy(b => b.Au).Select(b => Climate.Summary(Data, b.Id)!).ToList();
        for (var i = 1; i < bodies.Count; i++) Assert.True(bodies[i].MeanInsolation <= bodies[i - 1].MeanInsolation);
        Assert.Equal(1.0, Climate.Summary(Data, "tarsis")!.MeanInsolation.ToDouble(), 2);
        Assert.InRange(Climate.Summary(Data, "tarsis")!.MeanTemperatureC.ToDouble(), 5, 20);
        Assert.True(Climate.Summary(Data, "west_giant")!.MeanInsolation < Fix64.Ratio(1, 5));
        Assert.True(Climate.Summary(Data, "west_rich")!.MeanTemperatureC > Climate.Summary(Data, "west_l")!.MeanTemperatureC);
        Assert.Equal(Climate.Summary(Data, "tarsis")!.MeanInsolation, Climate.Summary(Data, "tarsis_a")!.MeanInsolation); // a moon shares its planet's orbit

        // Eccentricity: Veyra gets more sunlight at perihelion than at aphelion.
        var map = Planet(1701, "west_l");
        var b = Data.Climate.Body("west_l")!;
        var year = (long)(b.YearSeconds * Simulation.TickRate).FloorToInt();
        var light = Enumerable.Range(0, 20).Select(i => Climate.Sun(Data, map, i * year / 20).Insolation.ToDouble()).ToList();
        Assert.True(light.Max() / light.Min() > 1.3, $"{light.Max():0.000} / {light.Min():0.000}");
    }

    [Fact]
    public void WeatherPicksFollowTheSeasonsWeights()
    {
        var map = Planet(4242);
        var template = Data.Climate!.Template("temperate")!;
        Assert.Equal("Temperate", template.BandAt(Fix64.FromInt(34)).Name);
        // Merge the kinds the temperature swaps (rain ↔ snow, storm ↔ blizzard).
        static int Group(WeatherKind k) => k switch { WeatherKind.Snow => (int)WeatherKind.Rain, WeatherKind.Blizzard => (int)WeatherKind.Storm, _ => (int)k };
        var counts = new Dictionary<string, int[]>();
        var row = 20; // weather cells there sit in the northern temperate band (~34°)
        var end = 40 * (long)(Data.Climate.Body("tarsis")!.YearSeconds * Simulation.TickRate).FloorToInt();
        for (var col = 0; col < map.Grid.Width; col += 12)
        {
            var tile = map.Grid.Index(col, row);
            var tick = CalmTicks(Data) + 1;
            while (tick < end)
            {
                var w = Climate.Weather(Data, map, tile, tick);
                var season = Climate.Sun(Data, map, w.StartTick).SeasonName;
                if (!counts.TryGetValue(season, out var c)) counts[season] = c = new int[Enum.GetValues<WeatherKind>().Length];
                c[Group(w.Kind)]++;
                tick = w.EndTick;
            }
        }
        foreach (var s in template.Seasons)
        {
            var c = counts[s.Name];
            var n = (double)c.Sum();
            Assert.True(n > 1500, $"{s.Name}: only {n} spells");
            var total = s.Weights.Sum(x => x.ToDouble());
            for (var k = 0; k < c.Length; k++)
            {
                if (Group((WeatherKind)k) != k) continue;
                var expected = (s.Weights[k].ToDouble() + (k == (int)WeatherKind.Rain ? s.Weights[(int)WeatherKind.Snow].ToDouble() : 0)
                                + (k == (int)WeatherKind.Storm ? s.Weights[(int)WeatherKind.Blizzard].ToDouble() : 0)) / total;
                Assert.True(System.Math.Abs(c[k] / n - expected) < 0.035, $"{s.Name} {(WeatherKind)k}: {c[k] / n:0.000} vs {expected:0.000}");
            }
        }
    }

    [Fact]
    public void SnowOnlyFallsBelowFreezingAndRainAbove()
    {
        var map = Planet(1701);
        int snow = 0, rain = 0;
        for (var t = 0; t < map.Grid.Count; t += 29)
            for (var i = 0; i < 40; i++)
            {
                var tick = CalmTicks(Data) + i * 2161L;
                var (here, weather, _) = Climate.Report(Data, map, t, tick);
                Assert.Equal(here, Climate.At(Data, map, t, tick));
                Assert.Equal(weather, Climate.Weather(Data, map, t, tick));
                if (weather.Kind is WeatherKind.Snow or WeatherKind.Blizzard)
                {
                    snow++;
                    Assert.True(here.TemperatureC < Fix64.Zero, $"snow at {here.TemperatureC} °C");
                }
                if (weather.Kind is WeatherKind.Rain or WeatherKind.Storm)
                {
                    rain++;
                    Assert.True(here.TemperatureC >= Fix64.Zero, $"rain at {here.TemperatureC} °C");
                }
            }
        Assert.True(snow > 20 && rain > 20, $"snow {snow} rain {rain}");
    }

    [Fact]
    public void AirlessBodiesHaveNoWeatherButHarshDayAndNight()
    {
        var map = Planet(1701, "tarsis_a");
        var day = DayTicks(Data, "tarsis_a");
        var tile = map.Grid.Index(10, map.Grid.Height / 2);
        var temps = new List<double>();
        for (var i = 0; i < 24; i++)
        {
            var tick = 5000 + i * day / 24;
            var w = Climate.Weather(Data, map, tile, tick);
            Assert.Equal(WeatherKind.Clear, w.Kind);
            Assert.Equal(Fix64.Zero, w.WindMs);
            Assert.Equal(Fix64.Zero, w.CloudCover);
            Assert.Equal(Fix64.Zero, Climate.At(Data, map, tile, tick).HumidityPct);
            Assert.Equal(Fix64.One, Climate.Effects(Data, map, tile, tick).RoverSpeed);
            temps.Add(Climate.At(Data, map, tile, tick).TemperatureC.ToDouble());
        }
        Assert.True(temps.Max() - temps.Min() > 150, $"{temps.Min():0} .. {temps.Max():0} °C");
    }

    [Fact]
    public void WeatherIsDeterministicAndDrifts()
    {
        var a = Planet(99);
        var b = Planet(99);
        var other = Planet(100);
        var differs = 0;
        var changes = 0;
        for (var t = 0; t < a.Grid.Count; t += 101)
            for (var i = 0; i < 10; i++)
            {
                var tick = CalmTicks(Data) + i * 4321L;
                Assert.Equal(Climate.Weather(Data, a, t, tick), Climate.Weather(Data, b, t, tick));
                Assert.Equal(Climate.At(Data, a, t, tick), Climate.At(Data, b, t, tick));
                Assert.Equal(Climate.Effects(Data, a, t, tick), Climate.Effects(Data, b, t, tick));
                if (Climate.Weather(Data, other, t, tick).Kind != Climate.Weather(Data, a, t, tick).Kind) differs++;
                if (Climate.Weather(Data, a, t, tick + 6000).Kind != Climate.Weather(Data, a, t, tick).Kind) changes++;
            }
        Assert.True(differs > 50 && changes > 50, $"differs {differs} changes {changes}");

        // A spell covers its cell, and the cells drift east (temperate: 1.5 tiles a minute): two minutes later the same
        // spell sits three columns further east.
        var map = Planet(99);
        var row = 20;
        var at = CalmTicks(Data) + 1;
        var spell = Climate.Weather(Data, map, map.Grid.Index(40, row), at);
        while (spell.EndTick - spell.StartTick < 2000 || spell.StartTick < CalmTicks(Data))
        {
            at = spell.EndTick;
            spell = Climate.Weather(Data, map, map.Grid.Index(40, row), at);
        }
        var later = Climate.Weather(Data, map, map.Grid.Index(43, row), spell.StartTick + 1 + 2 * Simulation.TicksPerMinute);
        Assert.Equal((spell.StartTick, spell.EndTick), (later.StartTick, later.EndTick));
    }

    [Fact]
    public void TheMapStartsCalmSoTheFirstLandingWorks()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var s = sim.Surface!;
        for (var t = 0; t < s.Map.Grid.Count; t += 7)
        {
            Assert.Equal(WeatherKind.Clear, Climate.Weather(Data, s.Map, t, 0).Kind);
            if (s.CanLand(0, t)) Assert.Null(s.LandProblem(sim, 0, t));
        }
    }

    [Fact]
    public void StormsGroundDropShipsAndTheFirstLanding()
    {
        var data = Stormy();
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var s = sim.Surface!;
        var map = s.Map;
        for (var i = 0; i < 40 * Simulation.TickRate; i++) sim.Step(); // into the first spells (past their ramps)
        var sites = Enumerable.Range(0, map.Grid.Count).Where(t => s.CanLand(0, t)).ToList();
        var stormy = sites.First(t => s.LandProblem(sim, 0, t) != null);
        Assert.Contains("over the site: drop ships can't land (clears in about", s.LandProblem(sim, 0, stormy));
        Assert.True(Climate.Effects(data, map, stormy, sim.Tick).DropShipsGrounded);
        sim.Schedule(sim.Tick, new LandColonyCommand(0, stormy));
        sim.Step();
        Assert.Equal(LandingPhase.InOrbit, s.Players[0].Phase); // refused: still in orbit

        // Wait for a site whose storm is ramping down or up, then land there.
        var clear = -1;
        for (var i = 0; i < 400 * Simulation.TickRate && clear < 0; i++)
        {
            sim.Step();
            clear = sites.FirstOrDefault(t => s.LandProblem(sim, 0, t) == null, -1);
        }
        Assert.True(clear >= 0, "the storms never let up");
        sim.Schedule(sim.Tick, new LandColonyCommand(0, clear));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        Assert.Equal(LandingPhase.Landed, s.Players[0].Phase);
        sim.Players[0].Stock.Add(data.Resource("duranium").Index, Fix64.FromInt(2000));

        // Drop ships and outpost drops are refused under the storm, with the reason.
        var refused = Enumerable.Range(0, map.Grid.Count).Where(t => map.Grid.Distance(t, clear) > 12 && map.Safety(t) == LandingSafety.Safe)
            .First(t => Climate.Effects(data, map, t, sim.Tick).DropShipsGrounded);
        Assert.Contains("over the site", s.ShuttleProblem(sim, 0, refused, "survey_rover"));
        Assert.Contains("over the site", s.OutpostProblem(sim, 0, refused));
        Assert.Equal(s.DropWeatherProblem(sim.Tick, refused), s.ShuttleProblem(sim, 0, refused, "survey_rover"));
    }

    [Fact]
    public void BadWeatherSlowsRoversShrinksSurveysAndWearsThem()
    {
        var data = Stormy();
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var s = sim.Surface!;
        var site = -1;
        for (var i = 0; i < 400 * Simulation.TickRate && site < 0; i++)
        {
            sim.Step();
            site = Enumerable.Range(0, s.Map.Grid.Count).FirstOrDefault(t => s.LandProblem(sim, 0, t) == null, -1);
        }
        sim.Schedule(sim.Tick, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        var rover = s.Units.First(u => u.DefId == "survey_rover");

        // Step until a full-strength storm sits over the rover.
        for (var i = 0; i < 600 * Simulation.TickRate && s.WeatherEffectsAt(rover.Tile).RoverSpeed > Fix64.Ratio(75, 100); i++) sim.Step();
        var fx = s.WeatherEffectsAt(rover.Tile);
        Assert.True(fx.RoverSpeed <= Fix64.Ratio(75, 100), $"speed ×{fx.RoverSpeed}");
        Assert.True(fx.RoverDamage > Fix64.One);
        Assert.True(fx.SurveyRadiusDelta < 0);
        Assert.Equal(rover.SpeedPerTick * s.DriveShare(rover) * fx.RoverSpeed, s.SpeedPerTick(rover));
        Assert.Equal(fx.RoverSpeed, s.WeatherSpeed(rover));
        Assert.Equal(System.Math.Max(1, rover.SurveyRadius + fx.SurveyRadiusDelta), s.SurveyRadiusOf(rover));
        Assert.True(s.SurveyRadiusOf(rover) < rover.SurveyRadius);

        // Clear weather (the real data's calm start) leaves everything as it was.
        var calm = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Equal(new WeatherEffects(Fix64.One, 0, Fix64.One, false, Climate.Effects(Data, calm.Surface!.Map, site, 0).Solar),
            calm.Surface.WeatherEffectsAt(site));
    }

    [Fact]
    public void StormyGamesStayDeterministic()
    {
        ulong Run()
        {
            var data = Stormy();
            var sim = MatchSetup.CreatePlanetLanding(data, 77);
            var s = sim.Surface!;
            var site = -1;
            for (var i = 0; i < 400 * Simulation.TickRate && site < 0; i++)
            {
                sim.Step();
                site = Enumerable.Range(0, s.Map.Grid.Count).FirstOrDefault(t => s.LandProblem(sim, 0, t) == null, -1);
            }
            sim.Schedule(sim.Tick, new LandColonyCommand(0, site));
            for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
            var rovers = s.Units.Where(u => u.DefId == "survey_rover").Select(u => u.Id).ToList();
            var target = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.Terrain(t).Passable && s.Map.Grid.Distance(t, site) == 9);
            sim.Schedule(sim.Tick, new MoveUnitsCommand(0, rovers, target));
            for (var i = 0; i < 90 * Simulation.TickRate; i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void TelemetryReadsTheClimate()
    {
        var map = Planet(1701);
        for (var t = 0; t < map.Grid.Count; t += 113)
            for (var i = 0; i < 6; i++)
            {
                var tick = CalmTicks(Data) + i * 3001L;
                var r = Telemetry.Read(Data, map, t, tick)!;
                var (here, weather, _) = Climate.Report(Data, map, t, tick);
                Assert.Equal(here.TemperatureC, r.TemperatureC);
                Assert.Equal(here.Night, r.Night);
                Assert.Equal(here.SeasonName, r.Season);
                Assert.Equal(weather.Kind, r.Weather);
                Assert.Equal(weather.WindMs, r.WindMs);
                Assert.Equal(weather.PrecipitationMmH, r.PrecipitationMmH);
                Assert.Equal(here.SunElevationDeg > Fix64.Zero, r.Daylight);
                var from = (weather.WindDirDeg.ToDouble() + 180) % 360;
                Assert.True(System.Math.Abs(((r.WindFromDeg - from) % 360 + 540) % 360 - 180) <= 1, $"from {r.WindFromDeg} vs {from:0.0}");
            }
    }
}
