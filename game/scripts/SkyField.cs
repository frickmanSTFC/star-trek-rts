using System.Diagnostics;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Two one-texel-per-tile textures the surface shaders read (sky.gdshaderinc), baked from Climate a slice of tiles per
/// frame (a time budget, so the real climate model can be as slow as it likes):
/// <list type="bullet">
/// <item><see cref="DayTexture"/> (R8): each tile's daylight from <c>Climate.At</c>, plus the subsolar column it was baked
/// at (<see cref="BakedSubsolar"/>); the shader reads it shifted by how far the star has moved since, so the terminator
/// sweeps smoothly between bakes.</item>
/// <item><see cref="WeatherTexture"/> (RGBA8): R = snow lying, G = wetness, B = puddles. Render-only accumulation from
/// <c>Climate.Weather</c> and the temperature: snow builds while it snows and the air is cold and melts when it's warm
/// (rain melts it faster), rain wets the ground at once and it dries over minutes (faster in sun and heat), puddles fill
/// in long rain and dry slowly. Game time (sim ticks), so pause and game speed apply.</item>
/// </list>
/// With the test panel's SKY overrides the sun and/or the weather come from <see cref="SkyOverride"/> instead.
/// </summary>
public sealed class SkyField
{
    private readonly SurfaceMap _map;
    private readonly int _w, _h;
    private readonly byte[] _dayWork, _wx;
    private readonly float[] _snow, _wet, _puddle;
    private readonly long[] _lastTick;
    private readonly byte[] _dayShown;
    private readonly Image _dayImage, _wxImage;

    // The pass in progress.
    private int _cursor = -1;
    private long _passTick;
    private float _passSubsolar, _passDeclination;
    private bool _passForcedSun;
    private double _passStartedAt = -10;
    private int _overrideVersion = -1;

    public ImageTexture DayTexture { get; }
    public ImageTexture WeatherTexture { get; }

    /// <summary>The subsolar column the shown day texture was baked at.</summary>
    public float BakedSubsolar { get; private set; }

    /// <summary>Seconds of frame time a slice may take.</summary>
    private const double BudgetSeconds = 0.0012;

    public SkyField(SurfaceMap map)
    {
        _map = map;
        _w = map.Grid.Width;
        _h = map.Grid.Height;
        var count = _w * _h;
        _dayWork = new byte[count];
        _dayShown = new byte[count];
        System.Array.Fill(_dayShown, (byte)255);
        System.Array.Fill(_dayWork, (byte)255);
        _wx = new byte[count * 4];
        _snow = new float[count];
        _wet = new float[count];
        _puddle = new float[count];
        _lastTick = new long[count];
        System.Array.Fill(_lastTick, -1L);
        _dayImage = Image.CreateFromData(_w, _h, false, Image.Format.R8, _dayShown);
        _wxImage = Image.CreateFromData(_w, _h, false, Image.Format.Rgba8, _wx);
        DayTexture = ImageTexture.CreateFromImage(_dayImage);
        WeatherTexture = ImageTexture.CreateFromImage(_wxImage);
    }

    /// <summary>The shown daylight at a world point (nearest tile, shifted like the shader reads it).</summary>
    public float DaylightAt(Vector3 world, float subsolarNow)
    {
        var col = world.X / PlanetView.HexWidth - SkyModel.Wrap(subsolarNow - BakedSubsolar, _w);
        var row = Mathf.Clamp(Mathf.RoundToInt(world.Z / (0.866f * PlanetView.HexWidth)), 0, _h - 1);
        var c = Mathf.PosMod(Mathf.RoundToInt(col), _w);
        return _dayShown[row * _w + c] / 255f;
    }

    /// <summary>Snow lying on a tile (0..1), for the HUD and the feed.</summary>
    public float SnowAt(int tile) => tile >= 0 && tile < _snow.Length ? _snow[tile] : 0f;

    /// <summary>Wetness of a tile (0..1).</summary>
    public float WetAt(int tile) => tile >= 0 && tile < _wet.Length ? _wet[tile] : 0f;

    /// <summary>
    /// Bake a slice of tiles (a full pass at most every <paramref name="period"/> seconds of real time; a changed
    /// override restarts it at once). <paramref name="now"/> = the driver's clock. <paramref name="synchronous"/> bakes
    /// the whole map now (the first frame, so nothing pops).
    /// </summary>
    public void Step(GameData data, long tick, double now, SkyOverride sky, SunState sun, float forcedSubsolar,
        float forcedDeclination, bool synchronous = false, double period = 0.5)
    {
        if (sky.Version != _overrideVersion)
        {
            _overrideVersion = sky.Version;
            _cursor = -1; // start over with the new settings
        }
        if (_cursor < 0)
        {
            if (!synchronous && now - _passStartedAt < period) return;
            _passStartedAt = now;
            _cursor = 0;
            _passTick = tick;
            _passForcedSun = sky.TimeOn || sky.SeasonOn;
            _passSubsolar = _passForcedSun ? forcedSubsolar : sun.SubsolarColumn.ToFloat();
            _passDeclination = forcedDeclination;
        }
        var started = Stopwatch.GetTimestamp();
        var budget = (long)(BudgetSeconds * Stopwatch.Frequency);
        var count = _w * _h;
        while (_cursor < count)
        {
            BakeTile(data, sky, _cursor);
            _cursor++;
            if (!synchronous && (_cursor & 31) == 0 && Stopwatch.GetTimestamp() - started > budget) return;
        }
        // Pass complete: show it.
        System.Array.Copy(_dayWork, _dayShown, count); // the work buffer stays separate from the shown one
        BakedSubsolar = _passSubsolar;
        _dayImage.SetData(_w, _h, false, Image.Format.R8, _dayShown);
        DayTexture.Update(_dayImage);
        _wxImage.SetData(_w, _h, false, Image.Format.Rgba8, _wx);
        WeatherTexture.Update(_wxImage);
        _cursor = -1;
    }

    private void BakeTile(GameData data, SkyOverride sky, int index)
    {
        var col = index % _w;
        var row = index / _w;
        var tile = index; // HexGrid.Index(col, row) = row * width + col
        var at = Climate.At(data, _map, tile, _passTick);
        float daylight;
        if (_passForcedSun)
        {
            var (el, _) = SkyModel.SunAngles(SkyModel.LatitudeDeg(row, _h), _passDeclination, SkyModel.HourAngle(col, _passSubsolar, _w));
            daylight = SkyModel.Daylight(el);
        }
        else daylight = at.Daylight.ToFloat();
        _dayWork[index] = (byte)Mathf.Clamp(Mathf.RoundToInt(daylight * 255f), 0, 255);

        // Weather lying on the ground.
        WeatherKind kind;
        float intensity;
        var temperature = at.TemperatureC.ToFloat();
        if (sky.WeatherOn)
        {
            kind = sky.Kind;
            intensity = sky.Intensity;
            if (kind is WeatherKind.Snow or WeatherKind.Blizzard) temperature = Mathf.Min(temperature, -3f);
            else if (kind is WeatherKind.Rain or WeatherKind.Storm) temperature = Mathf.Max(temperature, 6f);
        }
        else
        {
            var w = Climate.Weather(data, _map, tile, _passTick);
            kind = w.Kind;
            intensity = w.Intensity.ToFloat();
        }
        var last = _lastTick[index];
        _lastTick[index] = _passTick;
        if (last < 0)
        {
            // First look: cold, damp tiles start with some snow on them.
            if (temperature < -2f && at.HumidityPct.ToFloat() > 55f) _snow[index] = Mathf.Clamp(-temperature / 10f, 0f, 0.7f);
        }
        else
        {
            var dt = Mathf.Clamp((_passTick - last) / (float)Simulation.TickRate, 0f, 30f);
            if (dt > 0f) Accumulate(index, kind, intensity, temperature, daylight, dt);
        }
        var o = index * 4;
        _wx[o] = (byte)Mathf.RoundToInt(_snow[index] * 255f);
        _wx[o + 1] = (byte)Mathf.RoundToInt(_wet[index] * 255f);
        _wx[o + 2] = (byte)Mathf.RoundToInt(_puddle[index] * 255f);
        _wx[o + 3] = 255;
    }

    private void Accumulate(int i, WeatherKind kind, float intensity, float temperature, float daylight, float dt)
    {
        var snowing = kind is WeatherKind.Snow or WeatherKind.Blizzard;
        var raining = kind is WeatherKind.Rain or WeatherKind.Storm;
        if (snowing && temperature < 1.5f) _snow[i] += dt * (0.004f + 0.016f * intensity); // ~50 s to cover in heavy snow
        var melt = temperature > 0.5f ? 0.006f * Mathf.Min(temperature, 15f) : 0f;
        if (raining) melt += 0.02f;
        var melted = Mathf.Min(_snow[i], dt * melt);
        _snow[i] -= melted;
        _snow[i] = Mathf.Clamp(_snow[i], 0f, 1f);

        if (raining) _wet[i] += dt * 0.1f * (0.3f + intensity);
        else _wet[i] -= dt * 0.004f * (1f + 2f * daylight) * (1f + Mathf.Max(temperature, 0f) / 15f) * (kind is WeatherKind.Dust or WeatherKind.DustStorm or WeatherKind.HighWind ? 2.5f : 1f);
        _wet[i] = Mathf.Clamp(_wet[i] + melted * 1.5f, 0f, 1f);

        if (raining && _wet[i] > 0.7f) _puddle[i] += dt * 0.012f * intensity;
        else _puddle[i] -= dt * 0.0015f * (1f + daylight);
        if (temperature < -1f) _puddle[i] -= dt * 0.004f; // frozen over: reads as ground
        _puddle[i] = Mathf.Clamp(_puddle[i], 0f, 1f);
    }
}
