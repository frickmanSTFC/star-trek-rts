using Godot;

namespace FinalFrontier;

/// <summary>
/// The weather's sound at the site (docs/sound-contract.md "Ambient sounds"; owner 2026-11-10: "If there is wind or rain,
/// then wind and rain"). Render-only, flat (not positional) on the Ambience bus; a child of PlanetDriver, so it pauses
/// with the game. <see cref="Update"/> runs once a frame from PlanetDriver.Sky with the eased weather the view draws (so
/// the SKY tab's forced weather counts): ambience/wind_loop by the wind speed (pitch rising with it) and wind_gust_a/b/c
/// one-shots every few seconds in a strong or gusty wind; rain_light_loop and rain_heavy_loop crossfaded by the rain;
/// snowfall_loop by the snow; dust_storm_loop by blowing dust; a thunder one-shot 0.3–3 s after each lightning flash
/// (<see cref="WeatherView.Bolts"/>: close and loud when soon after, distant and quieter when late). Everything is at
/// full level close to the ground, about -10 dB from the far zoom, very quiet in orbit while the outpost feed shows and
/// off in orbit otherwise. Loops start at a random point and stop once silent; no allocation per frame.
/// </summary>
public partial class WeatherAudio : Node
{
    private const int Beds = 5;
    private const int Wind = 0, RainLight = 1, RainHeavy = 2, Snowfall = 3, DustStorm = 4;
    private static readonly string[] BedPaths =
        { "ambience/wind_loop", "ambience/rain_light_loop", "ambience/rain_heavy_loop", "ambience/snowfall_loop", "ambience/dust_storm_loop" };
    private static readonly string[] GustPaths = { "ambience/wind_gust_a", "ambience/wind_gust_b", "ambience/wind_gust_c" };

    private readonly AudioStreamPlayer[] _beds = new AudioStreamPlayer[Beds];
    private readonly float[] _gain = new float[Beds];
    private readonly AudioStreamPlayer[] _gusts = new AudioStreamPlayer[2];
    private readonly AudioStreamPlayer[] _thunder = new AudioStreamPlayer[2];
    private readonly float[] _thunderAt = { -1f, -1f, -1f, -1f };     // seconds until a pending clap (-1 = none)
    private readonly float[] _thunderDelay = new float[4];          // how long after its flash it comes
    private readonly RandomNumberGenerator _rng = new() { Seed = 4711 };
    private float _level, _gustIn = 6f;
    private int _bolts = -1, _nextGust, _nextThunder;

    public WeatherAudio()
    {
        for (var i = 0; i < Beds; i++)
        {
            _beds[i] = Sfx.PlayerFlat($"Bed{i}", Sfx.AmbienceBusName);
            AddChild(_beds[i]);
        }
        for (var i = 0; i < 2; i++)
        {
            _gusts[i] = Sfx.PlayerFlat($"Gust{i}", Sfx.AmbienceBusName);
            AddChild(_gusts[i]);
            _thunder[i] = Sfx.PlayerFlat($"Thunder{i}", Sfx.AmbienceBusName);
            AddChild(_thunder[i]);
        }
    }

    /// <summary>
    /// Once a frame. <paramref name="w"/> = the eased weather at the site (WeatherView's), <paramref name="windMs"/> its
    /// wind speed, <paramref name="gustMs"/> the gusts, <paramref name="distance"/> the camera's zoom distance,
    /// <paramref name="presence"/> 1 on the surface, ~0.18 in orbit with the outpost feed open, 0 in orbit;
    /// <paramref name="bolts"/> = WeatherView.Bolts (lightning flashes so far).
    /// </summary>
    public void Update(in WeatherLook w, float windMs, float gustMs, float distance, float presence, int bolts, float delta)
    {
        if (delta <= 0f) return;
        // Close to the ground at full level, from the far zoom about -10 dB.
        var target = presence * (presence >= 1f ? 1f - 0.68f * Mathf.SmoothStep(200f, 1500f, distance) : 1f);
        _level = Mathf.MoveToward(_level, target, delta / 0.6f);

        var wind = Mathf.Pow(Mathf.Clamp((windMs - 0.5f) / 14f, 0f, 1f), 0.7f);
        var rain = Mathf.Clamp(w.Rain / 0.3f, 0f, 1f);
        var heavy = Mathf.SmoothStep(0.45f, 0.85f, w.Rain);
        Bed(Wind, 0.9f * wind, 0.85f + 0.35f * Mathf.Clamp(windMs / 25f, 0f, 1f), delta);
        Bed(RainLight, rain * Mathf.Sqrt(1f - heavy), 1f, delta);
        Bed(RainHeavy, rain * Mathf.Sqrt(heavy) * (0.7f + 0.3f * w.Rain), 1f, delta);
        Bed(Snowfall, Mathf.SmoothStep(0.02f, 0.5f, w.Snow) * (0.6f + 0.4f * w.Snow), 1f, delta);
        Bed(DustStorm, Mathf.SmoothStep(0.15f, 0.85f, w.Dust), 0.95f + 0.1f * Mathf.Clamp(windMs / 25f, 0f, 1f), delta);

        // Gusts: in a strong wind, or when the gusts run well above it.
        var gusty = windMs > 7f || gustMs - windMs > 4f;
        if (gusty && _level > 0.02f)
        {
            _gustIn -= delta * Mathf.Clamp(windMs / 10f, 0.5f, 2.5f);
            if (_gustIn <= 0f)
            {
                _gustIn = _rng.RandfRange(5f, 14f);
                var stream = Sfx.Load(GustPaths[_rng.RandiRange(0, GustPaths.Length - 1)]);
                var p = _gusts[_nextGust];
                _nextGust = 1 - _nextGust;
                if (stream != null)
                {
                    p.Stream = stream;
                    p.VolumeDb = Mathf.LinearToDb(_level * Mathf.Clamp(Mathf.Max(windMs, gustMs * 0.8f) / 18f, 0.3f, 1f)) - 2f;
                    p.PitchScale = _rng.RandfRange(0.9f, 1.1f);
                    p.Play();
                }
            }
        }

        // Thunder after each lightning flash, as far behind as the strike is away.
        if (_bolts < 0) _bolts = bolts;
        for (; _bolts < bolts; _bolts++)
        {
            for (var i = 0; i < _thunderAt.Length; i++)
            {
                if (_thunderAt[i] >= 0f) continue;
                var d = _rng.RandfRange(0.3f, 3f);
                _thunderAt[i] = d;
                _thunderDelay[i] = d;
                break;
            }
        }
        for (var i = 0; i < _thunderAt.Length; i++)
        {
            if (_thunderAt[i] < 0f) continue;
            _thunderAt[i] -= delta;
            if (_thunderAt[i] > 0f) continue;
            _thunderAt[i] = -1f;
            Thunder(_thunderDelay[i]);
        }
    }

    private void Bed(int i, float gain, float pitch, float delta)
    {
        // The weather is already eased (~2 s); this only smooths the zoom and the first frame.
        _gain[i] = Mathf.MoveToward(_gain[i], Mathf.Clamp(gain, 0f, 1f) * _level, delta / 0.5f);
        var p = _beds[i];
        if (_gain[i] < 0.003f)
        {
            if (p.Playing) p.Stop();
            return;
        }
        if (p.Stream == null)
        {
            p.Stream = Sfx.Load(BedPaths[i]);
            if (p.Stream == null) return; // missing file: silent (one warning from Sfx)
        }
        p.VolumeDb = Mathf.LinearToDb(_gain[i]);
        p.PitchScale = pitch;
        if (!p.Playing)
        {
            var length = (float)p.Stream.GetLength();
            p.Play(length > 0.5f ? _rng.Randf() * length * 0.9f : 0f);
        }
    }

    private void Thunder(float delay)
    {
        if (_level < 0.01f || !IsInsideTree()) return;
        var path = delay < 0.8f ? "ambience/thunder_close" : delay < 1.8f ? "ambience/thunder_b" : "ambience/thunder_a";
        var stream = Sfx.Load(path);
        if (stream == null) return;
        var p = _thunder[_nextThunder];
        _nextThunder = 1 - _nextThunder;
        p.Stream = stream;
        p.VolumeDb = Mathf.LinearToDb(_level) - 9f * Mathf.Clamp((delay - 0.3f) / 2.7f, 0f, 1f);
        p.PitchScale = _rng.RandfRange(0.92f, 1.06f);
        p.Play();
    }
}
