using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Drives one survey satellite model's parts (BuildingModels.Satellite; part names from docs/satellite-contract.md):
/// the wings fold/unfold about their hinges, the dish turns, the camera gimbal aims (nadir by default, a finding's
/// tile while it photographs it), the beacon blinks, the flash pulses on each picture (with a small light), the RCS
/// thrusters glow while it manoeuvres. Every glowing part gets its own material copies, so satellites blink apart.
/// Render-only; no per-frame allocation.
/// </summary>
public sealed class SatelliteRig
{
    public Node3D Root { get; }

    private readonly Node3D? _panelL, _panelR, _dish, _camera;
    private readonly Basis _panelLRest, _panelRRest, _cameraRest;
    private readonly List<(BaseMaterial3D Material, float Energy)> _beacon = new();
    private readonly List<BaseMaterial3D> _flash = new();
    private readonly List<BaseMaterial3D> _lens = new();
    private readonly List<BaseMaterial3D>[] _thrusters = { new(), new(), new(), new() };
    private readonly OmniLight3D _flashLight;
    private readonly float _phase;
    private float _flashT = 99f;
    private Quaternion _aim = Quaternion.Identity;

    /// <summary>Seconds since the last picture (large when none).</summary>
    public float SincePicture => _flashT;

    public SatelliteRig(Node3D model, float phase)
    {
        Root = model;
        _phase = phase;
        _panelL = model.FindChild("PanelL", true, false) as Node3D;
        _panelR = model.FindChild("PanelR", true, false) as Node3D;
        _dish = model.FindChild("Dish", true, false) as Node3D;
        _camera = model.FindChild("Camera", true, false) as Node3D;
        _panelLRest = _panelL?.Basis ?? Basis.Identity;
        _panelRRest = _panelR?.Basis ?? Basis.Identity;
        _cameraRest = _camera?.Basis ?? Basis.Identity;

        foreach (var (m, e) in OwnMaterials(model.FindChild("Beacon", true, false))) _beacon.Add((m, Mathf.Max(e, 2f)));
        foreach (var (m, _) in OwnMaterials(model.FindChild("Flash", true, false)))
        {
            m.EmissionEnabled = true;
            m.EmissionEnergyMultiplier = 0f;
            _flash.Add(m);
        }
        foreach (var (m, _) in OwnMaterials(model.FindChild("Lens", true, false))) _lens.Add(m);
        for (var k = 0; k < 4; k++)
            foreach (var (m, _) in OwnMaterials(model.FindChild($"Thruster{k}", true, false)))
            {
                m.EmissionEnabled = true;
                m.EmissionEnergyMultiplier = 0f;
                _thrusters[k].Add(m);
            }

        _flashLight = new OmniLight3D { Name = "FlashLight", LightColor = new Color(1f, 0.97f, 0.92f), LightEnergy = 0f, OmniRange = 3f, ShadowEnabled = false, Visible = false };
        (_camera ?? model).AddChild(_flashLight);
        _flashLight.Position = new Vector3(0, -0.25f, 0);
    }

    /// <summary>Take a picture: the flash pulses (0.35 s) and the light pops.</summary>
    public void Flash() => _flashT = 0f;

    /// <param name="fold">0 = wings open .. 1 = folded against the bus.</param>
    /// <param name="thrust">0..1 RCS glow (manoeuvring).</param>
    /// <param name="aimLocal">Where the camera should look, in the model's local space (null = straight down).</param>
    public void Pose(float fold, float thrust, Vector3? aimLocal, float time, float delta)
    {
        var a = Mathf.DegToRad(90f) * Mathf.Clamp(fold, 0f, 1f);
        if (_panelL != null) _panelL.Basis = _panelLRest * new Basis(Vector3.Back, -a);
        if (_panelR != null) _panelR.Basis = _panelRRest * new Basis(Vector3.Back, a);
        _dish?.RotateY(delta * (fold > 0.5f ? 0.2f : 1.1f));

        if (_camera != null)
        {
            var want = Quaternion.Identity;
            if (aimLocal is { } dir && dir.LengthSquared() > 1e-4f)
            {
                var d = dir.Normalized();
                if (d.Dot(Vector3.Down) > -0.95f) want = new Quaternion(Vector3.Down, d);
            }
            _aim = _aim.Slerp(want, 1f - Mathf.Exp(-6f * delta));
            _camera.Basis = new Basis(_aim) * _cameraRest;
        }

        // Beacon: a double blink every 2 s, satellites out of step.
        var b = Mathf.PosMod(time + _phase, 2f);
        var on = b < 0.08f || (b > 0.22f && b < 0.3f);
        foreach (var (m, e) in _beacon) m.EmissionEnergyMultiplier = on ? e : e * 0.04f;

        _flashT += delta;
        var flash = _flashT < 0.35f ? Mathf.Pow(1f - _flashT / 0.35f, 2f) : 0f;
        foreach (var m in _flash) m.EmissionEnergyMultiplier = 9f * flash;
        foreach (var m in _lens) m.EmissionEnergyMultiplier = 1.4f + 5f * flash;
        _flashLight.Visible = flash > 0.01f;
        _flashLight.LightEnergy = 4f * flash;

        for (var k = 0; k < 4; k++)
        {
            var flicker = 0.75f + 0.25f * Mathf.Sin(time * 37f + k * 1.7f + _phase * 5f);
            var e = thrust * 3.5f * flicker;
            foreach (var m in _thrusters[k]) m.EmissionEnergyMultiplier = e;
        }
    }

    /// <summary>Own copies of every material on the node (and the meshes under it): its override, or each surface's.</summary>
    private static List<(BaseMaterial3D Material, float Energy)> OwnMaterials(Node? node)
    {
        var list = new List<(BaseMaterial3D, float)>();
        if (node == null) return list;
        var stack = new Stack<Node>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var child in n.GetChildren()) stack.Push(child);
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            if (mi.MaterialOverride is BaseMaterial3D over)
            {
                var copy = (BaseMaterial3D)over.Duplicate();
                mi.MaterialOverride = copy;
                list.Add((copy, copy.EmissionEnergyMultiplier));
                continue;
            }
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i)) is not BaseMaterial3D m) continue;
                var copy = (BaseMaterial3D)m.Duplicate();
                mi.SetSurfaceOverrideMaterial(i, copy);
                list.Add((copy, copy.EmissionEnergyMultiplier));
            }
        }
        return list;
    }
}
