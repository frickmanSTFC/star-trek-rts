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

    private static readonly Color NightAmbient = new(0.3f, 0.4f, 0.75f);
    private static readonly Color MoonColour = new(0.62f, 0.72f, 1f);

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

        _weatherView = new WeatherView { Name = "Weather" };
        AddChild(_weatherView);
        _weatherView.Build();

        _skyDomeMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky_dome.gdshader"), RenderPriority = -100 };
        _skyDome = new MeshInstance3D
        {
            Name = "SkyDome",
            Mesh = new SphereMesh { Radius = 9000f, Height = 18000f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _skyDomeMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,
        };
        AddChild(_skyDome);

        _moonLight = new DirectionalLight3D { Name = "Moon", LightColor = MoonColour, LightEnergy = 0f, ShadowEnabled = false, Visible = false };
        AddChild(_moonLight);

        _view.WatchLamps();
    }

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
            _view.SetSkyTextures(_skyField.DayTexture, _skyField.WeatherTexture);
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
        var strength = Mathf.Clamp(0.35f + 0.65f * windLength / 4f, 0.25f, 3f) * (1f + 0.15f * Mathf.Clamp((gust - windMs) / 6f, 0f, 1f));
        if (Mathf.Abs(strength - _lastWindStrength) > 0.01f || windDir.DistanceTo(_lastWindDir) > 0.01f)
        {
            _lastWindStrength = strength;
            _lastWindDir = windDir;
            _view.SetWind(windDir, strength, 0.8f + 0.05f * Mathf.Min(windLength, 30f));
        }

        // Lamps and windows: brighter at night (each light by the night where it stands).
        var nightSite = SkyModel.Night(daySite);
        _view.UpdateLamps(_inOrbit ? nightSite : Mathf.Lerp(nightSite, night, wide), p => SkyModel.Night(_skyField.DaylightAt(p, subsolar)), delta);

        // The globe's sun over the subsolar column, tilted by the season.
        if (_inOrbit) _orbit.SetSun(_orbit.SunLocal(subsolar, declination));

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
        var moonEnergy = 0.2f * (1f - Mathf.SmoothStep(0f, 0.08f, dayRef)) * (1f - 0.6f * _cloudEased);
        _moonLight.LightEnergy = moonEnergy;
        _moonLight.Basis = Basis.LookingAt(-SkyModel.SunDirection(50f, azimuth + 180f), Vector3.Up);
        var moonOn = moonEnergy > 0.003f;
        if (_moonLight.Visible != moonOn) _moonLight.Visible = moonOn;

        var light = Mathf.Lerp(0.06f, 1f, Mathf.SmoothStep(0f, 0.35f, dayRef)) * (1f - 0.35f * _cloudEased);
        var tilt = 1f - Mathf.SmoothStep(60f, 400f, _distance); // close up the haze is the sky's colour; from above it stays dark
        var wide = Mathf.SmoothStep(150f, 900f, _distance);
        var grey = new Color(0.62f, 0.65f, 0.7f);
        var dayHorizon = _skyColour.Lightened(0.25f).Lerp(grey, 0.7f * _cloudEased) * SkyModel.Warm(Mathf.Max(dayRef, 0.2f));
        var nightHorizon = new Color(0.02f, 0.03f, 0.06f);
        var horizon = nightHorizon.Lerp(dayHorizon, light);
        var weatherTint = (_look.Dust > Mathf.Max(_look.Fog, Mathf.Max(_look.Rain, _look.Snow)) ? new Color(0.7f, 0.5f, 0.3f) : grey) * Mathf.Max(light, 0.08f);
        var thick = Mathf.Clamp(Mathf.Max(Mathf.Max(_look.Fog, _look.Rain * 0.5f), Mathf.Max(_look.Snow * 0.6f, _look.Dust)), 0f, 1f) * (1f - 0.5f * wide);
        var fogColour = _baseFogColour.Lerp(horizon, tilt).Lerp(weatherTint, thick) + new Color(0.5f, 0.55f, 0.7f) * flash * 0.3f;

        if (_environment != null)
        {
            var a = Mathf.SmoothStep(0f, 0.3f, dayRef);
            _environment.AmbientLightEnergy = Mathf.Lerp(0.1f, _baseAmbientEnergy, a) * (1f + 0.25f * _cloudEased * a) + flash * 0.8f;
            _environment.AmbientLightColor = NightAmbient.Lerp(_baseAmbientColour, a).Lerp(grey, 0.3f * _cloudEased);
            // MoveCamera set this frame's haze for the zoom; thicken it for the weather (less so from high above).
            _environment.FogDensity *= 1f + (5f * _look.Fog + 3f * _look.Dust + 0.8f * _look.Rain + 1.2f * _look.Snow) * (1f - 0.6f * wide);
            _environment.FogLightColor = fogColour;
            _environment.FogHeight = _view.GroundAt(site) + 22f;
            _environment.FogHeightDensity = 0.03f * _look.Fog + 0.015f * _look.Dust;
            _environment.GlowHdrThreshold = Mathf.Lerp(_baseGlowThreshold, 0.7f, night);
            _environment.GlowIntensity = Mathf.Lerp(_baseGlowIntensity, _baseGlowIntensity * 1.3f, night);
        }

        // Sky dome around the camera (only the part above the horizon is drawn).
        if (!_skyDome.Visible) _skyDome.Visible = true;
        _skyDome.GlobalPosition = _camera.GlobalPosition;
        var zenith = new Color(0.004f, 0.006f, 0.018f).Lerp(_skyColour.Darkened(0.35f), light);
        _skyDomeMaterial.SetShaderParameter("horizon_colour", fogColour);
        _skyDomeMaterial.SetShaderParameter("zenith_colour", zenith);
        _skyDomeMaterial.SetShaderParameter("sun_dir", SkyModel.SunDirection(elevation, azimuth));
        _skyDomeMaterial.SetShaderParameter("sun_colour", _sun?.LightColor ?? Colors.White);
        _skyDomeMaterial.SetShaderParameter("sun_strength", Mathf.SmoothStep(-2f, 2f, elevation));
        _skyDomeMaterial.SetShaderParameter("stars", night);
        _skyDomeMaterial.SetShaderParameter("flash", flash * 0.6f);
        _skyDomeMaterial.SetShaderParameter("cloud", _cloudEased);

        if (!_weatherView.Visible) _weatherView.Visible = true;
        _weatherView.Update(_look, _focus with { Y = _view.GroundAt(_focus) }, _camera.GlobalPosition, _distance, _view.GroundAt, delta);
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
