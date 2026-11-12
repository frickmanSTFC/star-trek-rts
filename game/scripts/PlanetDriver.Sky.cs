using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Day/night, seasons and weather drawn from the sim's Climate (owner 2026-11-06: "each planet should have a light side
// and dark side ... rain, wind, snow should happen during specific seasons"). Render-only: nothing here changes the sim.
//
// The "site" is the ground the camera looks at (on the surface) or the outpost (in orbit, for the helm's feed).
// - SkyField bakes every tile's daylight (Climate.At) and the snow / wet / puddles lying on it into two textures the
//   terrain, water, foliage and rock shaders read (sky.gdshaderinc), so the far-off night side reads dark from above.
// - The Sun follows the sun at the site (elevation, azimuth; warm and dim low down), a cold moon light takes over at
//   night, the ambient goes blue-dark, stars come out in the sky dome, lamps and windows brighten (PlanetView.Sky).
// - The weather at the site (eased over a couple of seconds) drives WeatherView's rain/snow/dust/lightning around the
//   camera, the haze (density, colour, low ground fog), the foliage's wind, and the cloud dimming of the sun.
// - The orbit globe's sun stands over Climate's subsolar column at its declination; the feed gets FeedClimate.
// - The top bar shows "DAY 3 · SPRING · 14:20" / "RAIN · 12 °C" for the site.
// The test panel's SKY tab (PlanetView.SkyOverride) can force the time, season, weather and wind (render-only).
public partial class PlanetDriver
{
    private SkyField _skyField = null!;
    private WeatherView _weatherView = null!;
    private WeatherAudio _weatherAudio = null!;
    private MeshInstance3D _skyDome = null!;
    private ShaderMaterial _skyDomeMaterial = null!;
    private DirectionalLight3D _moonLight = null!;
    private Color _skyColour = new(0.53f, 0.71f, 0.89f);
    private Color _baseAmbientColour, _baseFogColour, _baseSunColour;
    private float _baseAmbientEnergy, _baseSunEnergy, _baseGlowThreshold, _baseGlowIntensity;
    private bool _skySurfaceOn;

    // Climate at the site, sampled a few times a second.
    private TileClimate _siteClimate;
    private WeatherState _siteWeather;
    private int _siteTile = -1;
    private double _siteSampledAt = -10;

    // Eased weather and what was last handed to the shaders.
    private WeatherLook _look;
    private Vector3 _windEased = new(4f, 0f, 0f);
    private float _visibilityEased = 1f, _cloudEased;
    private float _lastShift = float.NaN, _lastRain = -1f, _lastWindStrength = -1f;
    private Vector3 _lastRef = new(-1, -1, -1);
    private Vector2 _lastWindDir;
    private bool _skyTexturesSet;

    // Test panel: a forced time keeps its own subsolar column (set from the view's centre when the hour is picked).
    private float _forcedSubsolar, _forcedHourSeen = float.NaN;
    private double _skyHudAt = -10;

    // ---- Night (owner 2026-11-12, the third time: "The darkness is still way too dark"). A bright moonlit night: terrain,
    // buildings and vehicles read plainly with a cool tint, ~45-60% of the day's readability, while lamps and windows
    // (x2.4 / x2.6 at night, PlanetView.Sky) and headlights (VehicleLights, 7.5-11) still stand out. History: ambient 0.1 -> 0.13 -> 0.3
    // (2026-11-10) -> 0.45; moon 0.2 -> 0.26 -> 0.6 -> 0.8. Estimated with ACES (Classic): ground at the screen centre
    // L* ~24 -> ~39 against ~56 by day (0.43 -> ~0.68 of the day's lightness; the old 0.43 read as "way too dark").
    // SkyModel.Shade / sky.gdshaderinc sky_shade (the far view's night side) were raised to match.

    /// <summary>Ambient light energy at full night (the day's comes from Planet.tscn, 0.38).</summary>
    private const float NightAmbientEnergy = 0.45f;
    /// <summary>Ambient colour at night: a moonlit blue, less saturated than before so warm ground still reads warm-ish
    /// (was (0.42, 0.5, 0.8), before that (0.3, 0.4, 0.75)).</summary>
    private static readonly Color NightAmbient = new(0.5f, 0.58f, 0.82f);
    /// <summary>Clouds at night scatter a little light back: the night ambient is x (1 + this x cloud).</summary>
    private const float NightCloudAmbient = 0.15f;
    /// <summary>The moon: a no-shadow directional key light at night (was 0.6).</summary>
    private const float MoonEnergy = 0.8f;
    /// <summary>How much full cloud cover dims the moon (was 0.6: overcast nights went nearly black).</summary>
    private const float MoonCloudDim = 0.4f;
    /// <summary>Moonlight colour (was (0.62, 0.72, 1): so blue that green and brown ground reflected little of it).</summary>
    private static readonly Color MoonColour = new(0.7f, 0.78f, 1f);
    /// <summary>The sky dome's horizon and zenith at night (were (0.02, 0.03, 0.06) and (0.004, 0.006, 0.018)): a moonlit
    /// sky, still dark enough for the stars.</summary>
    private static readonly Color NightHorizon = new(0.05f, 0.07f, 0.13f);
    private static readonly Color NightZenith = new(0.012f, 0.018f, 0.045f);
    /// <summary>The haze never goes darker than this at night (before, the near-black (0.03, 0.04, 0.08) haze took 36% of
    /// the ground at the view's centre and more toward the horizon, which made the night look darker than its light).</summary>
    private static readonly Color NightHaze = new(0.07f, 0.09f, 0.15f);
    /// <summary>Weather haze (fog, rain, snow, dust) brightness at night, x its day colour (was 0.08: a black wall), and the
    /// moonlit tint it takes on.</summary>
    private const float NightWeatherLight = 0.25f;
    private static readonly Color NightWeatherTint = new(0.82f, 0.9f, 1.15f);
    /// <summary>Classic: the glow threshold at full night (was 0.7; 0.8 keeps moonlit snow from blooming, lamps still do).</summary>
    private const float NightGlowThreshold = 0.8f;
    /// <summary>Enhanced (AgX): exposure by day and at full night (unchanged: the lights themselves are brighter now) and the
    /// glow threshold at night (was 0.72).</summary>
    private const float EnhancedDayExposure = 1.05f, EnhancedNightExposure = 1.22f, EnhancedNightGlowThreshold = 0.8f;

    // ---- Weather haze (owner 2026-11-12: "When fog (not fog of war) is there, it is also too thick. It should be there,
    // but not prevent you from seeing anything"). MoveCamera sets the clear-air haze (density x camera distance =
    // HazeDepth, ~36% at the view's centre); the weather adds to it, softly capped so the ground at the view's centre (up
    // close: WeatherHazeReach away) never fades by more than 1 - e^-WeatherHazeMaxDepth (~46%): a visible grey haze, the
    // distance fading, your base and vehicles clear. Before: x (1 + 5 fog + 3 dust + 0.8 rain + 1.2 snow), about x5.5 in
    // thick fog (92% at the centre), plus a ~45% height-fog floor everywhere, and in Enhanced a fixed volumetric
    // 0.004 x fog that was a wall at the default zoom.

    /// <summary>MoveCamera's clear-air haze: density = HazeDepth / max(60, camera distance).</summary>
    private const float HazeDepth = 0.45f;
    /// <summary>How much each kind of weather thickens the haze (x the clear density, before the cap; less from far up).</summary>
    private const float WeatherFog = 1.2f, WeatherDust = 1.0f, WeatherRain = 0.35f, WeatherSnow = 0.5f;
    /// <summary>The cap: optical depth at the view's centre the weather can bring it to (close/default zoom, far overview).</summary>
    private const float WeatherHazeMaxDepth = 0.62f, WeatherHazeMaxDepthFar = 0.5f;
    /// <summary>Up close the cap holds out to this distance (your base, not just the vehicle in front of the camera).</summary>
    private const float WeatherHazeReach = 100f;
    /// <summary>Weather may always be this much thicker than clear air (so fog shows up close, where clear air already
    /// fades the base a little).</summary>
    private const float WeatherHazeMinBoost = 1.25f;
    /// <summary>Low ground fog: Godot's height fog adds 1 - e^-(depth below FogHeight x density) to everything under
    /// FogHeight whatever its distance, so it is a floor: ~21% on the ground in thick fog, ~12% in a dust storm, less from
    /// far up (was 0.03 / 0.015, ~45%).</summary>
    private const float HeightFogFog = 0.012f, HeightFogDust = 0.006f, HeightFogAbove = 22f;
    /// <summary>Enhanced: the share of the capped haze drawn as volumetric fog, and what the exponential fog keeps (total
    /// ~1, so the cap holds; was 0.3 + 0.85 plus a fixed 0.004 fog + 0.002 dust).</summary>
    private const float VolumetricShare = 0.3f, ExponentialShareWithVolumetric = 0.75f;

    private void SetupSky()
    {
        var map = Surface.Map;
        var env = _data.EnvironmentOf(map.Def.ClassId);
        if (env != null) _skyColour = Color.FromHtml(env.Sky);
        if (_environment != null)
        {
            _baseAmbientColour = _environment.AmbientLightColor;
            _baseAmbientEnergy = _environment.AmbientLightEnergy;
            _baseFogColour = _environment.FogLightColor;
            _baseGlowThreshold = _environment.GlowHdrThreshold;
            _baseGlowIntensity = _environment.GlowIntensity;
        }
        _baseSunColour = _sun?.LightColor ?? new Color(1f, 0.96f, 0.88f);
        _baseSunEnergy = _sun?.LightEnergy ?? 1.3f;

        _skyField = new SkyField(map);
        var sun = Climate.Sun(_data, map, _sim.Tick);
        _skyField.Step(_data, _sim.Tick, _clock, _view.SkyOverride, sun, 0f, 0f, synchronous: true);

        _weatherView = new WeatherView { Name = "Weather", Visible = false }; // shown on the surface only
        AddChild(_weatherView);
        _weatherView.Build();
        _weatherView.DustColour = _view.Look.StormDust; // rust on Veyra (PlanetLook)
        _weatherAudio = new WeatherAudio { Name = "WeatherAudio" }; // wind, rain, snow, dust, thunder on the Ambience bus
        AddChild(_weatherAudio);

        _skyDomeMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky_dome.gdshader"), RenderPriority = -100 };
        _skyDome = new MeshInstance3D
        {
            Name = "SkyDome",
            Mesh = new SphereMesh { Radius = 9000f, Height = 18000f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _skyDomeMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,
            Visible = false, // shown on the surface only (the orbit camera would see it around the globe)
        };
        AddChild(_skyDome);

        _moonLight = new DirectionalLight3D { Name = "Moon", LightColor = MoonColour, LightEnergy = 0f, ShadowEnabled = false, Visible = false };
        AddChild(_moonLight);

        _view.WatchLamps();
        _lampNightAt = p => SkyModel.Night(_skyField.DaylightAt(p, _skySubsolar));
        _daylightAt = p => _skyField.DaylightAt(p, _skySubsolar);
        _groundAt = _view.GroundAt;
    }

    // Cached delegates (no per-frame allocation) and the subsolar column they read.
    private System.Func<Vector3, float> _lampNightAt = null!, _groundAt = null!, _daylightAt = null!;
    private float _skySubsolar;

    /// <summary>Once a frame, after the view's refresh (and before the uplink, which hands the feed its climate).</summary>
    private void UpdateSky(float delta)
    {
        var map = Surface.Map;
        var g = map.Grid;
        var tick = _sim.Tick;
        var sky = _view.SkyOverride;
        var gameRate = (_test?.Paused ?? false) ? 0f : (float)(_test?.Speed ?? 1.0);

        // Where we look at the sky from.
        var landed = Me.Phase == LandingPhase.Landed;
        var site = _inOrbit ? (landed ? _view.TileCentre(Me.LandingTile) : _view.MapCentre()) : _focus;
        var siteTile = _view.TileAt(site);
        if (siteTile < 0) siteTile = _view.TileAt(_view.MapCentre());
        if (siteTile < 0) siteTile = 0;
        var col = site.X / PlanetView.HexWidth;
        var row = Mathf.Clamp(site.Z / (0.866f * PlanetView.HexWidth), 0f, g.Height - 1);

        // The star for the whole planet (or the test panel's).
        var sun = Climate.Sun(_data, map, tick);
        var daySeconds = Mathf.Max(1, _data.EnvironmentOf(map.Def.ClassId)?.DaySeconds ?? 720);
        if (sky.TimeOn)
        {
            if (!Mathf.IsEqualApprox(sky.Hour, _forcedHourSeen))
                _forcedSubsolar = col - (sky.Hour - 12f) * g.Width / 24f; // the picked hour at the view's centre
            if (sky.RunRate > 0f) _forcedSubsolar -= g.Width * delta * gameRate * sky.RunRate / daySeconds; // westward
            sky.Hour = Mathf.PosMod(12f + 24f * SkyModel.Wrap(col - _forcedSubsolar, g.Width) / g.Width, 24f);
            _forcedHourSeen = sky.Hour;
        }
        else _forcedHourSeen = float.NaN;
        var forcedSun = sky.TimeOn || sky.SeasonOn;
        var subsolar = sky.TimeOn ? _forcedSubsolar : sun.SubsolarColumn.ToFloat();
        var declination = sky.SeasonOn ? SkyModel.TestTiltDeg * Mathf.Sin(sky.Season * Mathf.Pi / 2f) : sun.DeclinationDeg.ToFloat();
        _skyField.Step(_data, tick, _clock, sky, sun, subsolar, declination);
        var shift = SkyModel.Wrap(subsolar - _skyField.BakedSubsolar, g.Width);
        _skySubsolar = subsolar;

        // The site's climate and weather, a few times a second.
        if (siteTile != _siteTile || _clock - _siteSampledAt >= 0.25)
        {
            _siteTile = siteTile;
            _siteSampledAt = _clock;
            _siteClimate = Climate.At(_data, map, siteTile, tick);
            _siteWeather = Climate.Weather(_data, map, siteTile, tick);
        }

        // Sun at the site.
        float elevation, azimuth;
        var latitude = SkyModel.LatitudeDeg(row, g.Height);
        if (forcedSun)
            (elevation, azimuth) = SkyModel.SunAngles(latitude, declination, SkyModel.HourAngle(col, subsolar, g.Width));
        else
        {
            elevation = _siteClimate.SunElevationDeg.ToFloat();
            azimuth = _siteClimate.SunAzimuthDeg.ToFloat();
        }
        var daySite = _skyField.DaylightAt(site, subsolar);

        // The daylight the scene's lights stand for: the site's, or from high above the brightest part of the view.
        var wide = _inOrbit ? 0f : Mathf.SmoothStep(150f, 900f, _distance);
        var dayRef = daySite;
        if (wide > 0f)
        {
            var right = new Vector3(1, 0, 0).Rotated(Vector3.Up, _yaw) * _distance * 0.5f;
            var forward = new Vector3(0, 0, -1).Rotated(Vector3.Up, _yaw) * _distance * 0.3f;
            var brightest = daySite;
            brightest = Mathf.Max(brightest, _skyField.DaylightAt(site + right, subsolar));
            brightest = Mathf.Max(brightest, _skyField.DaylightAt(site - right, subsolar));
            brightest = Mathf.Max(brightest, _skyField.DaylightAt(site + forward, subsolar));
            brightest = Mathf.Max(brightest, _skyField.DaylightAt(site - forward, subsolar));
            dayRef = Mathf.Lerp(daySite, brightest, wide);
        }

        // Weather at the site (forced, or Climate's), eased.
        var (kind, intensity, visibility, cloud, windMs, windDeg, gust) = SiteWeather(sky);
        var desert = map.Terrain(siteTile).Id is "desert" or "plains";
        var targetRain = kind == WeatherKind.Rain ? 0.3f + 0.7f * intensity : kind == WeatherKind.Storm ? 0.75f + 0.25f * intensity : 0f;
        var targetSnow = kind == WeatherKind.Snow ? 0.3f + 0.7f * intensity : kind == WeatherKind.Blizzard ? 0.8f + 0.2f * intensity : 0f;
        var targetDust = kind == WeatherKind.Dust ? 0.25f + 0.45f * intensity : kind == WeatherKind.DustStorm ? 0.75f + 0.25f * intensity
            : kind == WeatherKind.HighWind && desert ? 0.15f + 0.15f * intensity : 0f;
        var ease = 1f - Mathf.Exp(-delta / 2.2f);
        _look.Rain = Mathf.Lerp(_look.Rain, targetRain, ease);
        _look.Snow = Mathf.Lerp(_look.Snow, targetSnow, ease);
        _look.Dust = Mathf.Lerp(_look.Dust, targetDust, ease);
        _visibilityEased = Mathf.Lerp(_visibilityEased, visibility, ease);
        _cloudEased = Mathf.Lerp(_cloudEased, cloud, ease);
        _look.Fog = Mathf.Clamp(1f - _visibilityEased, 0f, 1f);
        _look.Cloud = _cloudEased;
        _look.Lightning = kind == WeatherKind.Storm ? 0.4f + 0.6f * intensity : 0f;
        var bearing = Mathf.DegToRad(windDeg);
        var windTarget = new Vector3(Mathf.Sin(bearing), 0f, -Mathf.Cos(bearing)) * windMs;
        _windEased = _windEased.Lerp(windTarget, ease);
        _look.Wind = _windEased;
        _look.Light = Mathf.SmoothStep(0f, 0.35f, dayRef) * (1f - 0.45f * _cloudEased);
        _look.DesertGround = desert;

        var night = SkyModel.Night(dayRef);
        var sunOn = Mathf.SmoothStep(0f, 0.08f, dayRef);
        var cloudDim = 1f - 0.65f * _cloudEased;
        var sunEnergy = _baseSunEnergy * sunOn * Mathf.Lerp(1.3f, 1f, Mathf.SmoothStep(0.05f, 0.4f, dayRef)) * cloudDim;

        // The Sun's direction (and colour) also matter in orbit, where the feed lights the outpost with it.
        var lightElevation = Mathf.Max(elevation, 7f);
        if (wide > 0f && dayRef > daySite + 0.05f)
            lightElevation = Mathf.Lerp(lightElevation, Mathf.Max(Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(dayRef, 0f, 1f))), 7f), wide);
        lightElevation = Mathf.Min(lightElevation, 85f);
        var toSun = SkyModel.SunDirection(lightElevation, azimuth);
        if (_sun != null)
        {
            _sun.Basis = Basis.LookingAt(-toSun, Vector3.Up);
            var warm = SkyModel.Warm(dayRef);
            _sun.LightColor = (_baseSunColour * warm).Lerp(new Color(0.85f, 0.88f, 0.92f), 0.4f * _cloudEased);
        }

        if (_inOrbit) LeaveSurfaceSky();
        else ApplySurfaceSky(delta, site, toSun, elevation, azimuth, dayRef, night, sunEnergy, cloudDim);

        // The surface's own materials: day/night and lying weather (also seen through the feed from orbit).
        if (!_skyTexturesSet)
        {
            _skyTexturesSet = true;
            _view.SetSkyTextures(_skyField.DayTexture, _skyField.WeatherTexture, _skyField);
        }
        var refShade = SkyModel.Shade(dayRef);
        if (!Mathf.IsEqualApprox(shift, _lastShift) || !refShade.IsEqualApprox(_lastRef) || Mathf.Abs(_look.Rain - _lastRain) > 0.01f)
        {
            _lastShift = shift;
            _lastRef = refShade;
            _lastRain = _look.Rain;
            _view.SetSky(shift, refShade, _look.Rain);
        }
        var windLength = _windEased.Length();
        var windDir = windLength > 0.05f ? new Vector2(_windEased.X, _windEased.Z) / windLength : _lastWindDir;
        // 1 at 4 m/s (the look the foliage was tuned for), calmer below, up to 3× in a gale.
        var strength = Mathf.Clamp(0.35f + 0.65f * windLength / 4f, 0.25f, 3f);
        if (Mathf.Abs(strength - _lastWindStrength) > 0.01f || windDir.DistanceTo(_lastWindDir) > 0.01f)
        {
            _lastWindStrength = strength;
            _lastWindDir = windDir;
            _view.SetWind(windDir, strength, 0.8f + 0.05f * Mathf.Min(windLength, 30f));
        }

        // Lamps and windows: brighter at night (each light by the night where it stands).
        var nightSite = SkyModel.Night(daySite);
        _view.UpdateLamps(_inOrbit ? nightSite : Mathf.Lerp(nightSite, night, wide), _lampNightAt, delta);
        // Vehicle headlights: on by the dark / weather at each vehicle, budgeted by distance to the camera that sees them.
        _view.UpdateHeadlights(_daylightAt, _inOrbit ? _feed.CameraPosition ?? site : _camera.GlobalPosition, delta);

        // The globe's sun over the subsolar column, tilted by the season; storm rings where Climate has storms.
        if (_inOrbit)
        {
            _orbit.SetSun(_orbit.SunLocal(subsolar, declination));
            if (_clock - _stormsSampledAt >= 5.0)
            {
                _stormsSampledAt = _clock;
                SampleStorms(tick);
            }
            _orbit.SetClimateStorms(_stormTiles, _stormVersion);
        }

        // The weather's sound (WeatherAudio): from the same eased weather, so the SKY tab's forced weather counts; full
        // close to the ground, quieter from far up, very quiet in orbit with the outpost feed open, off otherwise.
        _view.RainNow = _inOrbit ? 0f : Mathf.Max(_look.Rain, _look.Snow); // rain or snowfall: auto wipers + wet glass
        var presence = !_inOrbit ? 1f : landed && _feed.CameraPosition != null ? 0.18f : 0f;
        _weatherAudio.Update(_look, _windEased.Length(), gust, _distance, presence, _weatherView.Bolts, delta);

        // The helm's surface feed: the outpost's sky from the same numbers.
        _feed.Climate = landed
            ? new FeedClimate(daySite, Mathf.Sin(Mathf.DegToRad(elevation)), sunEnergy, kind, intensity, _cloudEased, _visibilityEased, windDeg, windMs)
            : null;

        if (!_inOrbit && _clock - _skyHudAt >= 0.25)
        {
            _skyHudAt = _clock;
            ShowSkyReadout(sky, sun, col, subsolar, latitude, elevation, azimuth, daySite, kind, intensity, visibility, cloud, windMs, windDeg, gust, daySeconds);
        }
    }

    private readonly System.Collections.Generic.List<int> _stormTiles = new();
    private readonly System.Collections.Generic.List<int> _stormScratch = new();
    private int _stormVersion;
    private double _stormsSampledAt = -10;

    /// <summary>Up to four stormy places (storm, blizzard, dust storm) from Climate on a coarse grid, at least 12 tiles
    /// apart, for the globe's Atmosphere display (the sim's weather, not the test panel's).</summary>
    private void SampleStorms(long tick)
    {
        var map = Surface.Map;
        var g = map.Grid;
        _stormScratch.Clear();
        for (var row = 2; row < g.Height && _stormScratch.Count < 4; row += 4)
            for (var col = 2; col < g.Width && _stormScratch.Count < 4; col += 4)
            {
                var tile = g.Index(col, row);
                var kind = Climate.Weather(_data, map, tile, tick).Kind;
                if (kind is not (WeatherKind.Storm or WeatherKind.Blizzard or WeatherKind.DustStorm)) continue;
                var apart = true;
                foreach (var other in _stormScratch)
                {
                    var dc = Mathf.Abs(g.Column(other) - col);
                    dc = Mathf.Min(dc, g.Width - dc);
                    if (dc < 12 && Mathf.Abs(g.Row(other) - row) < 12) apart = false;
                }
                if (apart) _stormScratch.Add(tile);
            }
        if (_stormScratch.Count == _stormTiles.Count && _stormScratch.TrueForAll(_stormTiles.Contains)) return;
        _stormTiles.Clear();
        _stormTiles.AddRange(_stormScratch);
        _stormVersion++;
    }

    /// <summary>The weather at the site: the test panel's, or Climate's.</summary>
    private (WeatherKind Kind, float Intensity, float Visibility, float Cloud, float WindMs, float WindDeg, float Gust) SiteWeather(SkyOverride sky)
    {
        var w = _siteWeather;
        var kind = sky.WeatherOn ? sky.Kind : w.Kind;
        var intensity = sky.WeatherOn ? sky.Intensity : w.Intensity.ToFloat();
        float visibility, cloud, windMs = w.WindMs.ToFloat(), windDeg = w.WindDirDeg.ToFloat(), gust = w.GustMs.ToFloat();
        if (sky.WeatherOn)
        {
            float defaultWind;
            (visibility, cloud, defaultWind) = SkyModel.Defaults(kind, intensity);
            windMs = Mathf.Max(windMs, defaultWind);
            gust = windMs * 1.5f;
        }
        else
        {
            visibility = w.Visibility.ToFloat();
            cloud = w.CloudCover.ToFloat();
        }
        if (sky.WindOn)
        {
            windMs = sky.WindMs;
            windDeg = sky.WindDeg;
            gust = windMs * 1.4f;
        }
        return (kind, Mathf.Clamp(intensity, 0f, 1f), Mathf.Clamp(visibility, 0f, 1f), Mathf.Clamp(cloud, 0f, 1f), windMs, windDeg, gust);
    }

    private void ApplySurfaceSky(float delta, Vector3 site, Vector3 toSun, float elevation, float azimuth, float dayRef, float night,
        float sunEnergy, float cloudDim)
    {
        _skySurfaceOn = true;
        var flash = _weatherView.Flash;

        if (_sun != null)
        {
            _sun.LightEnergy = sunEnergy;
            _sun.ShadowOpacity = 1f - 0.6f * _cloudEased;
            var on = sunEnergy > 0.005f;
            if (_sun.Visible != on) _sun.Visible = on;
        }
        var moonEnergy = MoonEnergy * (1f - Mathf.SmoothStep(0f, 0.08f, dayRef)) * (1f - MoonCloudDim * _cloudEased);
        _moonLight.LightEnergy = moonEnergy;
        _moonLight.Basis = Basis.LookingAt(-SkyModel.SunDirection(50f, azimuth + 180f), Vector3.Up);
        var moonOn = moonEnergy > 0.003f;
        if (_moonLight.Visible != moonOn) _moonLight.Visible = moonOn;

        var light = Mathf.Lerp(0.06f, 1f, Mathf.SmoothStep(0f, 0.35f, dayRef)) * (1f - 0.35f * _cloudEased);
        var tilt = 1f - Mathf.SmoothStep(60f, 400f, _distance); // close up the haze is the sky's colour; from above it stays dark
        var wide = Mathf.SmoothStep(150f, 900f, _distance);
        var grey = new Color(0.62f, 0.65f, 0.7f);
        var dayHorizon = _skyColour.Lightened(0.25f).Lerp(grey, 0.7f * _cloudEased) * SkyModel.Warm(Mathf.Max(dayRef, 0.2f));
        var horizon = NightHorizon.Lerp(dayHorizon, light);
        // Weather haze: grey (or the world's dust); at night dimmed but moonlit, never black.
        var weatherTint = (_look.Dust > Mathf.Max(_look.Fog, Mathf.Max(_look.Rain, _look.Snow)) ? _view.Look.FogDust : grey)
                          * Colors.White.Lerp(NightWeatherTint, night) * Mathf.Max(light, NightWeatherLight);
        var thick = Mathf.Clamp(Mathf.Max(Mathf.Max(_look.Fog, _look.Rain * 0.5f), Mathf.Max(_look.Snow * 0.6f, _look.Dust)), 0f, 1f) * (1f - 0.5f * wide);
        // Close up the haze takes on the sky (darkened: it fills the near air too), so far ground fades into the horizon.
        var fogColour = _baseFogColour.Lerp(horizon * 0.65f, tilt * 0.75f).Lerp(weatherTint, thick);
        // At night the haze is moonlit, not a black curtain over the ground (scaled by the night, so days are unchanged).
        var hazeFloor = NightHaze * night;
        fogColour = new Color(Mathf.Max(fogColour.R, hazeFloor.R), Mathf.Max(fogColour.G, hazeFloor.G), Mathf.Max(fogColour.B, hazeFloor.B))
                    + new Color(0.5f, 0.55f, 0.7f) * flash * 0.3f;
        // Visual pass (PlanetDriver.Graphics.cs): Enhanced = the world class's haze tint (PlanetLook grade); Classic as above.
        var grading = GraphicsQuality.Grading;
        if (grading) fogColour *= _view.Look.GradeFogTint;
        _gfxNight = night;
        _gfxSunElevation = elevation;

        if (_environment != null)
        {
            var a = Mathf.SmoothStep(0f, 0.3f, dayRef);
            _environment.AmbientLightEnergy = Mathf.Lerp(NightAmbientEnergy * (1f + NightCloudAmbient * _cloudEased), _baseAmbientEnergy, a)
                                              * (1f + 0.25f * _cloudEased * a) + flash * 0.8f;
            _environment.AmbientLightColor = NightAmbient.Lerp(_baseAmbientColour, a).Lerp(grey, 0.3f * _cloudEased);
            // MoveCamera set this frame's clear haze for the zoom; the weather thickens it, softly capped (see the constants).
            _environment.FogDensity = WeatherHaze(_environment.FogDensity, wide);
            if (_volFogOn)
            {
                // Enhanced fog: part of the (capped) haze becomes volumetric (lit by the sun, moon, lamps and headlights),
                // the exponential/height fog keeps the rest. Classic: neither line runs.
                _gfxVolDensity = _environment.FogDensity * VolumetricShare;
                _environment.FogDensity *= ExponentialShareWithVolumetric;
            }
            _environment.FogLightColor = fogColour;
            _environment.FogHeight = _view.GroundAt(site) + HeightFogAbove;
            _environment.FogHeightDensity = (HeightFogFog * _look.Fog + HeightFogDust * _look.Dust) * (1f - 0.5f * wide);
            if (grading)
            {
                // Enhanced: nothing lit by daylight blooms (threshold above 1), lamps still do; night exposure lifted
                // (owner 2026-11-10: nights too dark) on top of the same ambient.
                _environment.GlowHdrThreshold = Mathf.Lerp(1.08f, EnhancedNightGlowThreshold, night);
                _environment.GlowIntensity = Mathf.Lerp(0.72f, 0.95f, night);
                _environment.TonemapExposure = Mathf.Lerp(EnhancedDayExposure, EnhancedNightExposure, night);
            }
            else
            {
                _environment.GlowHdrThreshold = Mathf.Lerp(_baseGlowThreshold, NightGlowThreshold, night);
                _environment.GlowIntensity = Mathf.Lerp(_baseGlowIntensity, _baseGlowIntensity * 1.3f, night);
            }
        }

        // Sky dome around the camera (only the part above the horizon is drawn).
        if (!_skyDome.Visible) _skyDome.Visible = true;
        _skyDome.GlobalPosition = _camera.GlobalPosition;
        var zenith = NightZenith.Lerp(_skyColour.Darkened(0.35f), light);
        _skyDomeMaterial.SetShaderParameter("haze_colour", fogColour);
        _skyDomeMaterial.SetShaderParameter("horizon_colour", horizon.Lerp(weatherTint, thick));
        _skyDomeMaterial.SetShaderParameter("zenith_colour", zenith);
        _skyDomeMaterial.SetShaderParameter("sun_dir", SkyModel.SunDirection(elevation, azimuth));
        _skyDomeMaterial.SetShaderParameter("sun_colour", _sun?.LightColor ?? Colors.White);
        _skyDomeMaterial.SetShaderParameter("sun_strength", Mathf.SmoothStep(-2f, 2f, elevation));
        _skyDomeMaterial.SetShaderParameter("stars", night);
        _skyDomeMaterial.SetShaderParameter("flash", flash * 0.6f);
        _skyDomeMaterial.SetShaderParameter("cloud", _cloudEased);

        if (!_weatherView.Visible) _weatherView.Visible = true;
        _weatherView.Update(_look, _focus with { Y = _view.GroundAt(_focus) }, _camera.GlobalPosition, _distance, _groundAt, delta);
    }

    /// <summary>The haze density for this frame's weather: the clear density (MoveCamera's, for the zoom) thickened by fog,
    /// dust, rain and snow, then softly capped: the extra saturates toward the density at which the ground at the view's
    /// centre (up close: WeatherHazeReach away) fades by at most 1 - e^-WeatherHazeMaxDepth. Clear air is unchanged.</summary>
    private float WeatherHaze(float clear, float wide)
    {
        var weather = (WeatherFog * _look.Fog + WeatherDust * _look.Dust + WeatherRain * _look.Rain + WeatherSnow * _look.Snow) * (1f - 0.6f * wide);
        var extra = clear * weather;
        if (extra <= 0f) return clear;
        var depth = Mathf.Lerp(WeatherHazeMaxDepth, WeatherHazeMaxDepthFar, wide);
        var cap = Mathf.Max(depth / Mathf.Max(_distance, WeatherHazeReach), clear * WeatherHazeMinBoost);
        var room = cap - clear;
        return room <= 0f ? clear : clear + room * (1f - Mathf.Exp(-extra / room));
    }

    /// <summary>In orbit: put back what the orbit view shares with the surface (ambient colour, ground fog) and stop the
    /// surface-only pieces. Once, on the way up.</summary>
    private void LeaveSurfaceSky()
    {
        if (!_skySurfaceOn) return;
        _skySurfaceOn = false;
        if (_environment != null)
        {
            _environment.AmbientLightColor = _baseAmbientColour;
            _environment.FogHeightDensity = 0f;
            _environment.FogLightColor = _baseFogColour;
        }
        _moonLight.Visible = false;
        _skyDome.Visible = false;
        _weatherView.Stop();
        _weatherView.Visible = false;
    }

    private void ShowSkyReadout(SkyOverride sky, SunState sun, float col, float subsolar, float latitude, float elevation, float azimuth,
        float daylight, WeatherKind kind, float intensity, float visibility, float cloud, float windMs, float windDeg, float gust, int daySeconds)
    {
        var g = Surface.Map.Grid;
        // Local time: the hour angle from the subsolar column. Climate may spin either way: compare its own day fraction
        // at column 0 with the westward guess and flip if it disagrees.
        var sign = 1f;
        if (!sky.TimeOn)
        {
            var guess = Mathf.PosMod(0.5f + SkyModel.Wrap(-subsolar, g.Width) / g.Width, 1f);
            var diff = Mathf.Abs(Mathf.PosMod(guess - sun.DayFraction.ToFloat() + 0.5f, 1f) - 0.5f);
            if (diff > 0.05f) sign = -1f;
        }
        var hours = Mathf.PosMod(12f + 24f * sign * SkyModel.Wrap(col - subsolar, g.Width) / g.Width, 24f);
        var minutes = Mathf.FloorToInt(hours * 60f) % (24 * 60);
        var day = 1 + _sim.Tick / ((long)daySeconds * Simulation.TickRate);
        var southern = latitude < 0f;
        var season = sky.SeasonOn ? SkyModel.TestSeasons[(sky.Season + (southern ? 2 : 0)) % 4] : _siteClimate.SeasonName;
        var temperature = _siteClimate.TemperatureC.ToFloat();
        if (sky.WeatherOn && kind is WeatherKind.Snow or WeatherKind.Blizzard) temperature = Mathf.Min(temperature, -3f);
        else if (sky.WeatherOn && kind is WeatherKind.Rain or WeatherKind.Storm) temperature = Mathf.Max(temperature, 6f);
        var dark = daylight < 0.02f;
        var name = kind == WeatherKind.Clear && dark ? "CLEAR NIGHT" : SkyModel.Name(kind, intensity);
        var glyph = kind switch
        {
            WeatherKind.Clear => dark ? SkyGlyph.Moon : SkyGlyph.Sun,
            WeatherKind.Cloudy or WeatherKind.Overcast => SkyGlyph.Cloud,
            WeatherKind.Fog => SkyGlyph.Fog,
            WeatherKind.Rain => SkyGlyph.Rain,
            WeatherKind.Storm => SkyGlyph.Storm,
            WeatherKind.Snow or WeatherKind.Blizzard => SkyGlyph.Snow,
            WeatherKind.Dust or WeatherKind.DustStorm => SkyGlyph.Dust,
            _ => SkyGlyph.Wind,
        };
        var forced = sky.Any;
        var main = $"DAY {day} · {season.ToUpperInvariant()} · {minutes / 60:00}:{minutes % 60:00}";
        var sub = $"{name} · {Mathf.RoundToInt(temperature)} °C" + (forced ? " · TEST" : "");
        var tile = _siteTile;
        var tip = $"Local time {minutes / 60:00}:{minutes % 60:00} at the view's centre ({Mathf.Abs(latitude):0}° {(southern ? "S" : "N")})\n" +
                  $"Sun {elevation:0}° above the horizon, bearing {azimuth:0}° · daylight {Mathf.RoundToInt(daylight * 100)}%\n" +
                  $"Season: {season} · {Mathf.RoundToInt(sun.YearFraction.ToFloat() * 100)}% through the year\n" +
                  $"Weather: {name.ToLowerInvariant()}{(kind == WeatherKind.Clear ? "" : $" ({Mathf.RoundToInt(intensity * 100)}%)")} · " +
                  $"wind {windMs:0} m/s toward {SkyModel.Compass(windDeg)}, gusts {gust:0}\n" +
                  $"Visibility {Mathf.RoundToInt(visibility * 100)}% · cloud {Mathf.RoundToInt(cloud * 100)}% · humidity {Mathf.RoundToInt(_siteClimate.HumidityPct.ToFloat())}%\n" +
                  $"Ground: snow {Mathf.RoundToInt(_skyField.SnowAt(tile) * 100)}% · wet {Mathf.RoundToInt(_skyField.WetAt(tile) * 100)}%" +
                  (forced ? "\nTest panel (F9 › SKY) is overriding the sky: render only, the game's own weather still applies" : "");
        _hud.ShowSky(main, sub, tip, glyph, forced);
    }
}
