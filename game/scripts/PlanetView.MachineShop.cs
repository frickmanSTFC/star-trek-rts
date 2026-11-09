using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Render-only: the Machine Shop's working animation (BuildingModels.MachineShop.cs), driven by the fabricator's sim
/// status like PlanetView.Detailed.cs does for the mine and reactor. Working (<c>FabricatorStatus</c> Working or
/// LowPower; while the parts sim is still the contract stub, a plain <c>StructureStatus.Working</c> counts too): the
/// door rolls up, the two robot arms step through their weld path (sparks and a blue flash at the torch while they
/// weld), the press strokes, the belt runs with kit crates riding out to the rack, the roof fans turn, the status board
/// is green; low power = all of that at half speed. Idle (nothing queued, short of input): the arms park raised, the
/// press tops out, the belt stops, the door closes, status amber and dim. Paused: parked, status grey. Broken: dark
/// with flickering lamps, sparks spitting at the torches, status flashing red. Parts are looked up by name once per
/// model; another player's shop moves only while it is in sight.
/// </summary>
public partial class PlanetView
{
    /// <summary>One arm's weld path: turret yaw and elbow bend offsets from the rest pose (degrees), the hold at each
    /// stop (seconds) and whether it welds there. Arm 1 walks it from the other end, mirrored.</summary>
    private static readonly (float Yaw, float Bend, float Hold, bool Weld)[] ShopArmPath =
    {
        (0f, 0f, 1.4f, true), (16f, 3f, 1.1f, true), (-14f, -2f, 1.3f, true), (6f, 14f, 0.6f, false),
        (-22f, 6f, 1.0f, true), (10f, -4f, 0.9f, true), (0f, 24f, 0.8f, false),
    };

    private const float ArmParkBend = 35f, ArmYawSpeed = 55f, ArmBendSpeed = 40f, PressPeriod = 2.6f, ShopBeltSpeed = 0.55f;

    private sealed class ShopArm
    {
        public required Node3D Arm, Joint;
        public Basis ArmRest, JointRest;
        public readonly List<BaseMaterial3D> Tip = new();
        public CpuParticles3D? Sparks;
        public float Yaw, Bend = ArmParkBend, Hold;
        public int Stop, Step;
        public bool Welding;
    }

    private sealed class ShopVisual
    {
        public required Node3D Model;
        public required Glows Glow;
        public readonly List<ShopArm> Arms = new();
        public readonly List<Node3D> Fans = new();
        public readonly List<(Node3D Crate, Vector3 Across, float Phase, Vector3 Scale)> Crates = new();
        public Vector3 CrateDir;
        public float CrateLength;
        public Node3D? Press, Door;
        public Vector3 PressTop, PressLow;
        public OmniLight3D? Shop, Weld;
        public float Run, Flow, Open, Stroke, PressK;
    }

    private readonly Dictionary<int, ShopVisual> _shops = new();
    private readonly HashSet<int> _shopsAlive = new();
    private readonly List<int> _shopsGone = new();

    /// <summary>Every finished Machine Shop's animation, after UpdateDetailedBuildings (models exist, in the tree).</summary>
    private void UpdateMachineShops(float delta)
    {
        _shopsAlive.Clear();
        foreach (var s in _surface.Structures)
        {
            if (s.DefId != "machine_shop" || s.State != StructureState.Complete || !_structures.TryGetValue(s.Id, out var v)) continue;
            _shopsAlive.Add(s.Id);
            if (!_shops.TryGetValue(s.Id, out var m) || m.Model != v.Model) _shops[s.Id] = m = ShopParts(v.Model);
            if (!ShowsLive(s.Owner, s.Tile)) continue; // fog: a rival's shop keeps its last pose until it's in sight
            AnimateShop(s, m, delta);
        }
        if (_shops.Count == _shopsAlive.Count) return;
        _shopsGone.Clear();
        foreach (var id in _shops.Keys)
            if (!_shopsAlive.Contains(id)) _shopsGone.Add(id);
        foreach (var id in _shopsGone) _shops.Remove(id);
    }

    private static ShopVisual ShopParts(Node3D model)
    {
        var m = new ShopVisual
        {
            Model = model,
            Glow = new Glows(model, "Belt", "Status"),
            Press = Part(model, "Press"),
            Door = Part(model, "Door"),
            Shop = model.FindChild("ShopOmni", true, false) as OmniLight3D,
            Weld = model.FindChild("WeldOmni", true, false) as OmniLight3D,
        };
        for (var k = 0; k < 2; k++)
        {
            if (Part(model, $"Arm{k}") is not { } arm || arm.FindChild($"Arm{k}Joint", true, false) is not Node3D joint) continue;
            var a = new ShopArm { Arm = arm, Joint = joint, ArmRest = arm.Basis, JointRest = joint.Basis, Stop = k == 0 ? 0 : 3, Step = k == 0 ? 1 : -1 };
            foreach (var mat in MaterialsOf(joint))
                if (mat.ResourceName == "WeldGlow" && !a.Tip.Contains(mat)) a.Tip.Add(mat);
            if (joint.FindChild($"SparkPoint{k}", true, false) is Node3D sp) a.Sparks = Sparks(sp);
            // start parked (raised), not at the glb's working pose
            joint.Basis = a.JointRest * new Basis(Vector3.Right, Mathf.DegToRad(a.Bend));
            m.Arms.Add(a);
        }
        for (var k = 0; k < 2; k++)
            if (Part(model, $"Fan{k}") is { } fan) m.Fans.Add(fan);
        if (m.Press != null)
        {
            m.PressTop = m.Press.Position;
            m.PressLow = Sibling(m.Press, "PressLow")?.Position ?? m.PressTop + new Vector3(0, -0.85f, 0);
        }
        if (Part(model, "CrateFlow") is { } flow && flow.FindChild("CrateEnd", false, false) is Node3D end)
        {
            m.CrateLength = end.Position.Length();
            m.CrateDir = m.CrateLength > 0.01f ? end.Position / m.CrateLength : Vector3.Zero;
            foreach (var child in flow.GetChildren())
                if (child is Node3D crate && crate != end && child.Name.ToString().StartsWith("Crate"))
                {
                    var along = crate.Position.Dot(m.CrateDir);
                    m.Crates.Add((crate, crate.Position - m.CrateDir * along, m.CrateLength > 0 ? along / m.CrateLength : 0f, crate.Scale));
                }
        }
        return m;
    }

    private void AnimateShop(SurfaceStructure s, ShopVisual m, float delta)
    {
        var fab = _surface.FabricatorStatus(s);
        var broken = s.Broken || fab == FabStatus.Broken;
        var paused = !broken && (s.Paused || s.Status == StructureStatus.Paused || fab == FabStatus.Paused);
        var lowPower = fab == FabStatus.LowPower || s.Status == StructureStatus.LowPower;
        var working = !broken && !paused && (fab is FabStatus.Working or FabStatus.LowPower || s.Status is StructureStatus.Working or StructureStatus.LowPower);
        m.Run = Mathf.MoveToward(m.Run, working ? (lowPower ? 0.5f : 1f) : 0f, delta / 1.2f);

        // the door: up while it works, down a moment after it stops (left where it is when broken)
        if (!broken) m.Open = Mathf.MoveToward(m.Open, working ? 1f : 0f, delta / (working ? 1.4f : 2.2f));
        if (m.Door != null) m.Door.Scale = new Vector3(1f, Mathf.Lerp(1f, 0.08f, Mathf.SmoothStep(0f, 1f, m.Open)), 1f);

        // the arms
        var welding = false;
        for (var k = 0; k < m.Arms.Count; k++)
        {
            var a = m.Arms[k];
            float yaw, bend;
            if (working)
            {
                var p = ShopArmPath[a.Stop];
                yaw = k == 0 ? p.Yaw : -p.Yaw;
                bend = p.Bend;
                var there = Mathf.Abs(a.Yaw - yaw) < 0.05f && Mathf.Abs(a.Bend - bend) < 0.05f;
                a.Welding = there && p.Weld && m.Run > 0.3f;
                if (there)
                {
                    a.Hold += delta * m.Run;
                    if (a.Hold >= p.Hold)
                    {
                        a.Hold = 0f;
                        a.Stop = (a.Stop + a.Step + ShopArmPath.Length) % ShopArmPath.Length;
                    }
                }
            }
            else
            {
                a.Welding = false;
                a.Hold = 0f;
                yaw = 0f;
                bend = ArmParkBend;
            }
            if (!broken)
            {
                var pace = working ? m.Run : 0.6f;
                a.Yaw = Mathf.MoveToward(a.Yaw, yaw, delta * ArmYawSpeed * pace);
                a.Bend = Mathf.MoveToward(a.Bend, bend, delta * ArmBendSpeed * pace);
            }
            a.Arm.Basis = a.ArmRest * new Basis(Vector3.Up, Mathf.DegToRad(a.Yaw));
            a.Joint.Basis = a.JointRest * new Basis(Vector3.Right, Mathf.DegToRad(a.Bend));
            // the torch: a flickering blue-white tip while it welds
            var flicker = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(_time * 37f + k * 1.7f) * Mathf.Sin(_time * 11f + k));
            foreach (var tip in a.Tip)
            {
                tip.EmissionEnabled = true;
                tip.EmissionEnergyMultiplier = a.Welding ? 6f * flicker : 0.05f;
            }
            var spit = broken && Mathf.Sin(_time * 2.3f + k * 2.1f + s.Id) > 0.55f;
            Emit(a.Sparks, a.Welding || spit);
            welding |= a.Welding;
        }
        if (m.Weld != null)
            m.Weld.LightEnergy = welding ? 1.6f + 1.4f * Mathf.Abs(Mathf.Sin(_time * 29f) * Mathf.Sin(_time * 7.3f)) : 0f;

        // the press: a quick stroke down, a short dwell, a slower return, a pause; tops out when it stops
        if (m.Press != null)
        {
            if (working && m.Run > 0.05f)
            {
                m.Stroke = Mathf.PosMod(m.Stroke + delta * m.Run / PressPeriod, 1f);
                var t = m.Stroke;
                m.PressK = t < 0.18f ? Ease(t / 0.18f) : t < 0.28f ? 1f : t < 0.6f ? 1f - Ease((t - 0.28f) / 0.32f) : 0f;
            }
            else if (!broken) m.PressK = Mathf.MoveToward(m.PressK, 0f, delta * 0.8f);
            m.Press.Position = m.PressTop.Lerp(m.PressLow, m.PressK);
        }

        // the belt and the crates riding it out to the rack
        m.Flow += delta * ShopBeltSpeed * m.Run;
        foreach (var belt in m.Glow["Belt"]) belt.Uv1Offset = new Vector3(0f, Mathf.PosMod(m.Flow / 0.6f, 1f), 0f);
        var shown = working && m.Run > 0.05f;
        foreach (var (crate, across, phase, scale) in m.Crates)
        {
            crate.Visible = shown;
            if (!shown || m.CrateLength <= 0f) continue;
            var d = Mathf.PosMod(phase * m.CrateLength + m.Flow, m.CrateLength);
            crate.Position = across + m.CrateDir * d;
            // grow out of the press at the start, shrink onto the rack's roller table at the end
            var k = Mathf.Clamp(Mathf.Min(d / 0.25f, (m.CrateLength - d) / 0.25f), 0f, 1f);
            crate.Scale = scale * Mathf.Max(0.01f, k);
        }
        foreach (var fan in m.Fans) fan.RotateObjectLocal(Vector3.Up, delta * 8f * (broken || paused ? 0f : Mathf.Max(m.Run, 0.3f)));

        // the status board: green working, amber dim idle, grey paused, flashing red broken
        var colour = broken ? new Color(1f, 0.15f, 0.08f) : paused ? new Color(0.55f, 0.58f, 0.62f) : working ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.65f, 0.15f);
        var energy = broken ? (Mathf.PosMod(_time, 0.6f) < 0.3f ? 0.4f : 3f) : working ? 3f : paused ? 0.7f : 1.1f;
        foreach (var mat in m.Glow["Status"])
        {
            mat.Emission = colour;
            mat.AlbedoColor = colour;
            mat.EmissionEnergyMultiplier = energy;
        }
        var lamps = LampLevel(broken, working, lowPower, s.Id);
        m.Glow.Set(lamps, _time + s.Id * 0.37f);
        if (m.Shop != null) m.Shop.LightEnergy = 1.1f * lamps;
    }
}
