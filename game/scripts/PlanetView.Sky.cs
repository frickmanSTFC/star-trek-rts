using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

// Day/night and weather for the surface's own materials and lights (PlanetDriver.Sky drives it; render-only).
// SetSky hands sky.gdshaderinc's uniforms to every fog material (terrain, water, foliage, rock); SetWind sets the
// foliage sway. Night lights: every Light3D under the view and every lit emissive material (windows, lamps, glow
// strips, beacons…) is brightened at night; vehicle headlights and their lenses (VehicleLights.NoBoostMeta) are not,
// they set their own energy. Other code animates many of them (beacons blink, refinery lamps dim), so each frame the
// value found there is taken as the new base when it isn't the one written here last.
public partial class PlanetView
{
    /// <summary>The test panel's SKY overrides (render-only; PlanetDriver.Sky reads them).</summary>
    public SkyOverride SkyOverride { get; } = new();

    private Texture2D? _skyDay, _skyWeather;
    private int _skyMaterialCount;

    /// <summary>The two per-tile textures (SkyField); they keep their identity, so this is needed once. Turns sky_on on.</summary>
    /// <param name="field">The SkyField itself, for per-tile frost on the CPU side (spray; PlanetView.Veyra.cs).</param>
    public void SetSkyTextures(Texture2D day, Texture2D weather, SkyField? field = null)
    {
        _skyField = field ?? _skyField;
        _skyDay = day;
        _skyWeather = weather;
        _skyMaterialCount = 0;
    }

    /// <summary>The per-frame part: how far the star moved since the day texture was baked (columns), the shade the
    /// scene's lights stand for (SkyModel.Shade), and the rain falling at the view's centre.</summary>
    public void SetSky(float dayShift, Vector3 refShade, float rain)
    {
        if (_skyDay == null || _skyWeather == null) return;
        if (_skyMaterialCount != _fogMaterials.Count)
        {
            _skyMaterialCount = _fogMaterials.Count;
            foreach (var m in _fogMaterials)
            {
                m.SetShaderParameter("sky_on", 1f);
                m.SetShaderParameter("sky_day_tex", _skyDay);
                m.SetShaderParameter("sky_wx_tex", _skyWeather);
                m.SetShaderParameter("sky_frost_on", Look.Frost ? 1f : 0f); // night frost: class L only (PlanetLook)
            }
        }
        foreach (var m in _fogMaterials)
        {
            m.SetShaderParameter("sky_day_shift", dayShift);
            m.SetShaderParameter("sky_ref_shade", refShade);
            m.SetShaderParameter("sky_rain", rain);
        }
    }

    /// <summary>Foliage sway: downwind direction (x, z), sway/flutter strength (1 = the old look) and speed.</summary>
    public void SetWind(Vector2 downwind, float strength, float speed)
    {
        foreach (var m in _scatterMaterials)
        {
            m.SetShaderParameter("wind_dir", downwind);
            m.SetShaderParameter("wind_strength", strength);
            m.SetShaderParameter("wind_speed", speed);
        }
    }

    // ---------------------------------------------------------------- night lights

    private sealed class LampLight
    {
        public required Light3D Light;
        public required ulong Id;
        public float Base, Last = float.NaN, Night;
    }

    private sealed class LampMaterial
    {
        public required BaseMaterial3D Material;
        public required ulong Id;
        public float Base, Last = float.NaN;
        public readonly List<ulong> Owners = new();
    }

    private readonly List<LampLight> _lampLights = new();
    private readonly HashSet<ulong> _lampLightIds = new();
    private readonly Dictionary<ulong, LampMaterial> _lampMaterials = new();
    private readonly List<LampMaterial> _lampMaterialList = new();
    private readonly List<(ulong Id, double At)> _lampPending = new();
    private bool _lampsWatching;
    private double _lampClock, _lampPruneAt;
    private int _lampCursor;

    /// <summary>Seconds a new node waits before its lights are taken over (construction and landings fade their lights
    /// up first, so the base read is the real one).</summary>
    private const double LampDelay = 3.0;

    /// <summary>Start watching for lights (once, in the tree).</summary>
    public void WatchLamps()
    {
        if (_lampsWatching || !IsInsideTree()) return;
        _lampsWatching = true;
        GetTree().NodeAdded += OnLampNodeAdded;
        Collect(this);
    }

    public override void _ExitTree()
    {
        if (_lampsWatching && IsInsideTree()) GetTree().NodeAdded -= OnLampNodeAdded;
        _lampsWatching = false;
    }

    private void OnLampNodeAdded(Node node)
    {
        if (node is MeshInstance3D or Light3D && IsAncestorOf(node)) _lampPending.Add((node.GetInstanceId(), _lampClock + LampDelay));
    }

    private void Collect(Node node)
    {
        Register(node);
        foreach (var child in node.GetChildren()) Collect(child);
    }

    private void Register(Node node)
    {
        if (node is Light3D light and not DirectionalLight3D)
        {
            if (light.HasMeta(VehicleLights.NoBoostMeta)) return; // vehicle lamps set their own (night) energy every frame
            if (_lampLightIds.Add(light.GetInstanceId())) _lampLights.Add(new LampLight { Light = light, Id = light.GetInstanceId(), Base = light.LightEnergy });
            return;
        }
        if (node is not MeshInstance3D mi) return;
        if (mi.MaterialOverride is BaseMaterial3D over) RegisterMaterial(over, mi);
        if (mi.Mesh == null) return;
        for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            if (mi.GetActiveMaterial(i) is BaseMaterial3D m) RegisterMaterial(m, mi);
    }

    private void RegisterMaterial(BaseMaterial3D m, Node owner)
    {
        if (!m.EmissionEnabled || m.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded) return;
        if (m.HasMeta(VehicleLights.NoBoostMeta)) return; // headlight lenses: VehicleLights drives them
        var id = m.GetInstanceId();
        if (!_lampMaterials.TryGetValue(id, out var lamp))
        {
            lamp = new LampMaterial { Material = m, Id = id, Base = m.EmissionEnergyMultiplier };
            _lampMaterials[id] = lamp;
            _lampMaterialList.Add(lamp);
        }
        var ownerId = owner.GetInstanceId();
        if (!lamp.Owners.Contains(ownerId)) lamp.Owners.Add(ownerId);
    }

    /// <summary>
    /// Once a frame: brighten lights by the night at their own spot (<paramref name="nightAt"/>, 0 day .. 1 night) and
    /// emissive materials (shared between models, so by <paramref name="night"/> at the view's centre).
    /// </summary>
    public void UpdateLamps(float night, System.Func<Vector3, float> nightAt, float delta)
    {
        _lampClock += delta;
        for (var i = _lampPending.Count - 1; i >= 0; i--)
        {
            if (_lampPending[i].At > _lampClock) continue;
            if (GodotObject.InstanceFromId(_lampPending[i].Id) is Node node && IsInstanceValid(node) && node.IsInsideTree()) Register(node);
            _lampPending.RemoveAt(i);
        }

        // Lights: re-read their spot's night a few at a time (they rarely move far).
        var refresh = Mathf.Max(1, _lampLights.Count / 30);
        for (var k = 0; k < refresh && _lampLights.Count > 0; k++)
        {
            _lampCursor = (_lampCursor + 1) % _lampLights.Count;
            var l = _lampLights[_lampCursor];
            if (IsInstanceValid(l.Light) && l.Light.IsInsideTree()) l.Night = nightAt(l.Light.GlobalPosition);
        }
        for (var i = _lampLights.Count - 1; i >= 0; i--)
        {
            var l = _lampLights[i];
            if (!IsInstanceValid(l.Light))
            {
                _lampLightIds.Remove(l.Id);
                _lampLights.RemoveAt(i);
                if (_lampCursor >= _lampLights.Count) _lampCursor = 0;
                continue;
            }
            var current = l.Light.LightEnergy;
            if (current != l.Last) l.Base = current; // someone else set it (a blink, a fade): that is the new base
            var target = l.Base * (1f + 1.4f * l.Night);
            if (target != current) l.Light.LightEnergy = target;
            l.Last = target;
        }

        var boost = 1f + 1.6f * night;
        foreach (var m in _lampMaterialList)
        {
            var current = m.Material.EmissionEnergyMultiplier;
            if (current != m.Last) m.Base = current;
            var target = m.Base * boost;
            if (target != current) m.Material.EmissionEnergyMultiplier = target;
            m.Last = target;
        }

        // Forget materials whose models are all gone (handing back their own energy, in case another model still
        // shares one and gets registered again).
        if (_lampClock < _lampPruneAt) return;
        _lampPruneAt = _lampClock + 2.0;
        for (var i = _lampMaterialList.Count - 1; i >= 0; i--)
        {
            var m = _lampMaterialList[i];
            m.Owners.RemoveAll(id => GodotObject.InstanceFromId(id) is not Node n || !IsInstanceValid(n));
            if (m.Owners.Count > 0) continue;
            if (m.Material.EmissionEnergyMultiplier == m.Last) m.Material.EmissionEnergyMultiplier = m.Base;
            _lampMaterials.Remove(m.Id);
            _lampMaterialList.RemoveAt(i);
        }
    }
}
