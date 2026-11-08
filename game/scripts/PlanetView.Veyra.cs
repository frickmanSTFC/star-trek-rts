using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Class L's weather looks (Veyra, Oscen; render-only, PlanetLook.Frost / DustDevils / Co2Haze, so class M is untouched):
// - Night frost: SkyField accumulates it per tile (sky_wx_tex alpha); sky.gdshaderinc's sky_frost() whitens the
//   terrain, rocks and plants with it (sky_frost_on, set in PlanetView.Sky.SetSky). Spray turns to frost grit on it.
// - CO₂ ground haze: heavier-than-air CO₂ pools in hollows on calm nights. One sheet over every hollow tile
//   (shaders/co2_haze.gdshader): thicker the deeper the ground lies under it (depth texture), only where the tile's
//   own daylight says night, thinned by wind and weather.
// - Dust devils: small whirling dust columns wandering over dry ground near the camera on warm, calm afternoons
//   (shaders/dust_devil.gdshader on an open tapered cylinder + a little dust kicked up at the foot).
// The climate is sampled at the view's centre once a second (Climate.At / Weather, or the test panel's SKY overrides).
public partial class PlanetView
{
    private SkyField? _skyField;
    private Func<Vector3, float>? _skyDaylightAt;

    /// <summary>Night frost lying on a tile (0..1); 0 on worlds without frost or before the sky starts.</summary>
    private float FrostAt(int tile) => Look.Frost && _skyField != null ? _skyField.FrostAt(tile) : 0f;

    // The climate at the view's centre, sampled once a second.
    private float _climateAge = 99f;
    private float _focusTemperature = 10f, _focusWind, _focusDaylight = 1f;
    private WeatherKind _focusKind = WeatherKind.Clear;
    private Vector2 _focusDownwind = new(1f, 0f);
    private Vector3 _focus;

    private ShaderMaterial? _hazeMaterial;
    private float _hazeAmount = -1f;

    private sealed class DustDevil
    {
        public required Node3D Root;
        public required ShaderMaterial Material;
        public required CpuParticles3D Foot;
        public Vector3 Velocity;
        public float Age, Life, Strength, Turn;
    }

    private readonly List<DustDevil> _devils = new();
    private readonly RandomNumberGenerator _devilRng = new() { Seed = 7331UL };
    private float _devilSpawnWait = 2f;
    private const int MaxDevils = 3;
    private const float DevilHeight = 30f;

    /// <summary>Once a frame from Refresh: sample the climate at the view's centre, then the haze and the dust devils.</summary>
    private void UpdateVeyra(float delta)
    {
        if (!Look.DustDevils && !Look.Co2Haze) return;
        _focus = FocusPoint();
        _climateAge += delta;
        if (_climateAge >= 1f)
        {
            _climateAge = 0f;
            SampleFocusClimate();
        }
        if (_skyDaylightAt != null) _focusDaylight = _skyDaylightAt(_focus);

        var calm = 1f - Mathf.SmoothStep(3f, 9f, _focusWind);
        var still = _focusKind is WeatherKind.Clear or WeatherKind.Cloudy or WeatherKind.Fog ? 1f : _focusKind == WeatherKind.Overcast ? 0.6f : 0.1f;
        if (Look.Co2Haze && _hazeMaterial != null)
        {
            var amount = Mathf.Clamp(calm * still, 0f, 1f);
            // Ease, and only touch the material when it moved.
            var eased = _hazeAmount < 0f ? amount : Mathf.MoveToward(_hazeAmount, amount, delta * 0.1f);
            if (Mathf.Abs(eased - _hazeAmount) > 0.002f)
            {
                _hazeAmount = eased;
                _hazeMaterial.SetShaderParameter("amount", eased);
            }
        }
        if (Look.DustDevils) UpdateDustDevils(delta, calm);
    }

    /// <summary>The ground point the camera looks at (the headlights' eye when no camera is current, e.g. the feed).</summary>
    private Vector3 FocusPoint()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null && cam.IsInsideTree() && cam.GlobalPosition.DistanceSquaredTo(_eye) < 4f)
        {
            var forward = -cam.GlobalTransform.Basis.Z;
            if (forward.Y < -0.05f)
            {
                var ground = GroundAt(cam.GlobalPosition);
                var t = (cam.GlobalPosition.Y - ground) / -forward.Y;
                var p = cam.GlobalPosition + forward * Mathf.Min(t, 2000f);
                return p with { Y = GroundAt(p) };
            }
        }
        return _eye with { Y = GroundAt(_eye) };
    }

    private void SampleFocusClimate()
    {
        var tile = TileAt(_focus);
        if (tile < 0) return;
        var map = _surface.Map;
        var at = Climate.At(_sim.Data, map, tile, _sim.Tick);
        var w = Climate.Weather(_sim.Data, map, tile, _sim.Tick);
        _focusTemperature = at.TemperatureC.ToFloat();
        _focusKind = w.Kind;
        _focusWind = w.WindMs.ToFloat();
        var dirDeg = w.WindDirDeg.ToFloat();
        var sky = SkyOverride;
        if (sky.WeatherOn) _focusKind = sky.Kind;
        if (sky.WindOn)
        {
            _focusWind = sky.WindMs;
            dirDeg = sky.WindDeg;
        }
        // Compass bearing from map north (-Z), clockwise: the way the air moves (only a drift for the devils).
        var r = Mathf.DegToRad(dirDeg);
        _focusDownwind = new Vector2(Mathf.Sin(r), -Mathf.Cos(r));
        if (sky.TimeOn) _focusTemperature = 4f + 14f * Mathf.Clamp(_focusDaylight, 0f, 1f); // forced clock: a rough day curve
    }

    // ---------------------------------------------------------------- CO₂ haze

    /// <summary>One sheet over every hollow land tile (lower than its neighbours, or among the lowest land): corners
    /// are shared, so neighbouring hollows make one continuous pool; corners at the pool's edge fade to nothing.</summary>
    private void BuildCo2Haze()
    {
        if (!Look.Co2Haze) return;
        var map = _surface.Map;
        var grid = map.Grid;

        // Low land: the bottom fifth of the land tiles by elevation, or a tile sitting 0.5+ below its neighbours.
        var landElev = new List<float>();
        for (var t = 0; t < grid.Count; t++)
            if (!map.IsWater(t) && map.Terrain(t).Passable) landElev.Add(map.Elevation[t].ToFloat());
        if (landElev.Count == 0) return;
        landElev.Sort();
        var lowCut = landElev[landElev.Count / 5];
        var hollow = new bool[grid.Count];
        var count = 0;
        for (var t = 0; t < grid.Count; t++)
        {
            if (map.IsWater(t) || !map.Terrain(t).Passable || Look.Role(map.Terrain(t).Id) == "ice") continue;
            float sum = 0f;
            var n = 0;
            foreach (var nb in grid.Neighbours(t))
            {
                sum += map.IsWater(nb) ? SeaLevel : _centreHeight[nb];
                n++;
            }
            var below = n > 0 ? sum / n - _centreHeight[t] : 0f;
            if (below > 0.5f || map.Elevation[t].ToFloat() <= lowCut)
            {
                hollow[t] = true;
                count++;
            }
        }
        if (count == 0) return;

        // Corner heights and fades shared between hollow tiles.
        const float depth = 1.7f; // the pool's surface above the tile centres
        var corners = new Dictionary<long, (float Height, int Count, bool Edge)>();
        var centres = new Vector3[grid.Count];
        for (var t = 0; t < grid.Count; t++)
        {
            centres[t] = TileCentre(t) with { Y = 0 };
            var h = hollow[t] ? _centreHeight[t] + depth : 0f;
            for (var k = 0; k < 6; k++)
            {
                var key = CornerKey(centres[t] + Corner(k));
                corners.TryGetValue(key, out var c);
                corners[key] = hollow[t] ? (c.Height + h, c.Count + 1, c.Edge) : (c.Height, c.Count, true);
            }
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var index = 0;
        for (var t = 0; t < grid.Count; t++)
        {
            if (!hollow[t]) continue;
            st.SetColor(new Color(1, 1, 1, 1));
            st.SetNormal(Vector3.Up);
            st.AddVertex(centres[t] with { Y = _centreHeight[t] + depth });
            for (var k = 0; k < 6; k++)
            {
                var p = centres[t] + Corner(k);
                var c = corners[CornerKey(p)];
                st.SetColor(new Color(1, 1, 1, c.Edge ? 0f : 1f));
                st.SetNormal(Vector3.Up);
                st.AddVertex(p with { Y = c.Height / Mathf.Max(1, c.Count) });
            }
            for (var k = 0; k < 6; k++)
            {
                st.AddIndex(index);
                st.AddIndex(index + 1 + k);
                st.AddIndex(index + 1 + (k + 1) % 6);
            }
            index += 7;
        }
        _hazeMaterial = FogMaterial("res://shaders/co2_haze.gdshader");
        AddChild(new MeshInstance3D
        {
            Name = "Co2Haze",
            Mesh = st.Commit(),
            MaterialOverride = _hazeMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // ---------------------------------------------------------------- dust devils

    private void UpdateDustDevils(float delta, float calm)
    {
        // Warm (the warm part of the day lags the sun: afternoons), calm, dry and clear, and the camera close enough to see.
        var warm = Mathf.SmoothStep(7f, 12f, _focusTemperature) * Mathf.SmoothStep(0.35f, 0.55f, _focusDaylight);
        var clear = _focusKind is WeatherKind.Clear or WeatherKind.Cloudy ? 1f : 0f;
        var nearEnough = _eye.DistanceTo(_focus) < 700f;
        var want = warm * calm * clear > 0.5f && nearEnough && IsVisibleInTree() ? MaxDevils : 0;

        _devilSpawnWait -= delta;
        var alive = 0;
        foreach (var d in _devils) if (d.Age < d.Life) alive++;
        if (alive < want && _devilSpawnWait <= 0f)
        {
            _devilSpawnWait = _devilRng.RandfRange(4f, 12f);
            TrySpawnDevil();
        }

        for (var i = _devils.Count - 1; i >= 0; i--)
        {
            var d = _devils[i];
            d.Age += delta;
            var pos = d.Root.Position;
            // Wander: a slowly turning heading plus a drift downwind.
            d.Turn += _devilRng.RandfRange(-1f, 1f) * delta * 0.8f;
            d.Turn = Mathf.Clamp(d.Turn, -0.6f, 0.6f);
            d.Velocity = d.Velocity.Rotated(Vector3.Up, d.Turn * delta);
            var drift = new Vector3(_focusDownwind.X, 0, _focusDownwind.Y) * Mathf.Min(_focusWind, 6f) * 0.25f;
            pos += (d.Velocity + drift) * delta;
            var tile = TileAt(pos);
            var ok = tile >= 0 && DevilGround(tile) && _surface.StructureAt(tile) == null && Sees(tile);
            if (!ok || d.Age > d.Life || want == 0) d.Life = Mathf.Min(d.Life, d.Age); // dies down from here
            var target = d.Age < d.Life ? 1f : 0f;
            d.Strength = Mathf.MoveToward(d.Strength, target, delta / (target > 0f ? 3f : 4f));
            pos.Y = tile >= 0 ? GroundAt(pos) : pos.Y;
            d.Root.Position = pos;
            d.Material.SetShaderParameter("strength", d.Strength);
            d.Foot.Emitting = d.Strength > 0.3f;
            if (d.Strength <= 0f && d.Age >= d.Life)
            {
                d.Root.QueueFree();
                _devils.RemoveAt(i);
            }
        }
    }

    /// <summary>Dry, open ground a dust devil can run over (tundra, dust flats, hills).</summary>
    private bool DevilGround(int tile)
    {
        var map = _surface.Map;
        if (map.IsWater(tile) || !map.Terrain(tile).Passable) return false;
        return Look.Role(map.Terrain(tile).Id) is "plains" or "desert" or "hills";
    }

    private void TrySpawnDevil()
    {
        for (var tries = 0; tries < 8; tries++)
        {
            var angle = _devilRng.RandfRange(0f, Mathf.Tau);
            var r = _devilRng.RandfRange(40f, 220f);
            var p = _focus + new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r);
            var tile = TileAt(p);
            if (tile < 0 || !DevilGround(tile) || _surface.StructureAt(tile) != null || !Sees(tile)) continue;
            var crowded = false;
            foreach (var other in _devils) crowded |= other.Root.Position.DistanceTo(p) < 60f;
            if (crowded) continue;
            p.Y = GroundAt(p);
            _devils.Add(NewDevil(p));
            return;
        }
    }

    private static Shader? _devilShader;
    private static StandardMaterial3D? _devilPuff;

    private DustDevil NewDevil(Vector3 at)
    {
        var scale = _devilRng.RandfRange(0.7f, 1.3f);
        _devilShader ??= GD.Load<Shader>("res://shaders/dust_devil.gdshader");
        var material = new ShaderMaterial { Shader = _devilShader };
        material.SetShaderParameter("dust_colour", Look.StormDust);
        material.SetShaderParameter("strength", 0f);
        material.SetShaderParameter("seed", _devilRng.RandfRange(0f, 100f));
        material.SetShaderParameter("spin", _devilRng.RandfRange(2f, 3.5f) * (_devilRng.Randf() < 0.5f ? -1f : 1f));
        material.SetShaderParameter("column_height", DevilHeight);
        var root = new Node3D { Name = "DustDevil", Position = at, Scale = Vector3.One * scale };
        root.AddChild(new MeshInstance3D
        {
            Name = "Column",
            Mesh = new CylinderMesh
            {
                TopRadius = 4.5f, BottomRadius = 0.8f, Height = DevilHeight, RadialSegments = 18, Rings = 10,
                CapTop = false, CapBottom = false,
            },
            Position = new Vector3(0, DevilHeight * 0.5f - 0.3f, 0),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _devilPuff ??= new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f,
        };
        var c = Look.SprayDust;
        var foot = new CpuParticles3D
        {
            Name = "Foot",
            Amount = 36,
            Lifetime = 2.2f,
            Emitting = false,
            LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 2.2f,
            Direction = Vector3.Up,
            Spread = 50f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3.5f,
            Gravity = new Vector3(0, 0.4f, 0),
            DampingMin = 0.6f,
            DampingMax = 1.2f,
            ScaleAmountMin = 0.8f,
            ScaleAmountMax = 1.8f,
            ColorRamp = new Gradient { Offsets = new[] { 0f, 0.2f, 1f }, Colors = new[] { c with { A = 0f }, c, c with { A = 0f } } },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4, Material = _devilPuff },
            Position = new Vector3(0, 0.6f, 0),
        };
        root.AddChild(foot);
        AddChild(root);
        var heading = _devilRng.RandfRange(0f, Mathf.Tau);
        return new DustDevil
        {
            Root = root,
            Material = material,
            Foot = foot,
            Velocity = new Vector3(Mathf.Cos(heading), 0, Mathf.Sin(heading)) * _devilRng.RandfRange(2.5f, 4.5f),
            Life = _devilRng.RandfRange(25f, 60f),
        };
    }
}
