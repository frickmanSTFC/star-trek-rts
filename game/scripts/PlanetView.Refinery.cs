using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Render-only: per-instance looks for buildings (seed and number), the Ore Refinery's working animation, and cargo
/// pods setting down on a refinery's pad.
/// </summary>
public partial class PlanetView
{
    /// <summary>The variety seed of a structure (BuildingModels.Look): from its tile only, so the same building on
    /// the same tile always looks the same (hologram, finished model, upgrades).</summary>
    private static int VisualSeed(SurfaceStructure s) => unchecked((int)((uint)(s.Tile + 1) * 2654435761u));

    /// <summary>Its number among the owner's buildings of the same type (oldest first): "STORAGE 03".</summary>
    private int VisualSerial(SurfaceStructure s)
    {
        var n = 1;
        foreach (var o in _surface.Structures)
            if (o.Owner == s.Owner && o.DefId == s.DefId && o.Id < s.Id) n++;
        return n;
    }

    // ---------------------------------------------------------------- refinery

    private sealed class RefineryVisual
    {
        public required Node3D Model;
        public Node3D? Crusher, Pad;
        public readonly List<Node3D> Fans = new();
        public readonly List<(Node3D Lump, Vector3 Across, float Phase, Vector3 Scale)> Ore = new();
        public Vector3 OreDir;
        public float OreLength;
        public readonly List<BaseMaterial3D> Belts = new(), Glows = new(), Beacons = new();
        public readonly List<(BaseMaterial3D Material, float Energy)> Lamps = new();
        public OmniLight3D? Furnace, PadLight;
        public CpuParticles3D? Steam0, Steam1, Sparks;
        public float Run, Heat, Flow;
    }

    private readonly Dictionary<int, RefineryVisual> _refineries = new();

    /// <summary>Animates every finished refinery from its sim status: working → the crusher's flywheel and the fans
    /// spin, the belt runs with ore on it, the furnace glows and pulses, steam rises from the stacks; low power → all of
    /// that at half speed; idle (no ore, storage full) → machinery spins down, the furnace banks to a dull glow, lamps
    /// dim; broken → stopped, lamps flicker, sparks at the furnace (the generic broken-building smoke comes from
    /// ShowUpkeep).</summary>
    private void UpdateRefineries(float delta)
    {
        var alive = new HashSet<int>();
        foreach (var s in _surface.Structures)
        {
            if (s.DefId != "refinery" || s.State != StructureState.Complete || !_structures.TryGetValue(s.Id, out var v)) continue;
            alive.Add(s.Id);
            if (!_refineries.TryGetValue(s.Id, out var r) || r.Model != v.Model)
                _refineries[s.Id] = r = RefineryParts(v.Model);
            AnimateRefinery(s, r, delta);
        }
        foreach (var id in new List<int>(_refineries.Keys))
            if (!alive.Contains(id)) _refineries.Remove(id);
    }

    private static RefineryVisual RefineryParts(Node3D model)
    {
        var r = new RefineryVisual
        {
            Model = model,
            Crusher = model.FindChild("Crusher", true, false) as Node3D,
            Pad = model.FindChild("PodPad", true, false) as Node3D,
            Furnace = model.FindChild("FurnaceOmni", true, false) as OmniLight3D,
            PadLight = model.FindChild("PadOmni", true, false) as OmniLight3D,
        };
        for (var k = 0; k < 4; k++)
            if (model.FindChild($"Fan{k}", true, false) is Node3D fan) r.Fans.Add(fan);
        if (model.FindChild("OreFlow", true, false) is Node3D flow && flow.FindChild("OreEnd", false, false) is Node3D end)
        {
            r.OreLength = end.Position.Length();
            r.OreDir = r.OreLength > 0.01f ? end.Position / r.OreLength : Vector3.Zero;
            foreach (var child in flow.GetChildren())
                if (child is Node3D lump && child.Name.ToString().StartsWith("Ore") && lump != end)
                {
                    var along = lump.Position.Dot(r.OreDir);
                    r.Ore.Add((lump, lump.Position - r.OreDir * along, r.OreLength > 0 ? along / r.OreLength : 0f, lump.Scale));
                }
        }
        var seen = new HashSet<BaseMaterial3D>();
        foreach (var m in MaterialsOf(model))
        {
            if (!seen.Add(m)) continue;
            switch (m.ResourceName)
            {
                case "Belt": r.Belts.Add(m); break;
                case "FurnaceGlow": r.Glows.Add(m); break;
                case "Beacon": r.Beacons.Add(m); break;
                case "Lamp" or "PadLight" or "Window" or "Interior":
                    r.Lamps.Add((m, m.EmissionEnergyMultiplier > 0f ? m.EmissionEnergyMultiplier : 1f));
                    break;
            }
        }
        // steam from the stack tips (off until it works) and sparks at the furnace when it's broken
        foreach (var (name, k) in new[] { ("Smoke0", 0), ("Smoke1", 1) })
            if (model.FindChild(name, true, false) is Node3D tip)
            {
                var steam = BuildingModels.Puffs("Steam", Vector3.Zero, new Color(0.62f, 0.62f, 0.6f), k == 0 ? 26 : 16, 4f, 1.0f, k == 0 ? 2.6f : 1.8f, 0.35f);
                tip.AddChild(steam);
                if (k == 0) r.Steam0 = steam; else r.Steam1 = steam;
            }
        if (model.FindChild("SparkPoint", true, false) is Node3D spark)
        {
            r.Sparks = new CpuParticles3D
            {
                Name = "Sparks",
                Amount = 22,
                Lifetime = 0.8f,
                Emitting = false,
                LocalCoords = false,
                Explosiveness = 0.5f,
                Direction = new Vector3(0, 0.6f, 1f),
                Spread = 60f,
                InitialVelocityMin = 2.5f,
                InitialVelocityMax = 6f,
                Gravity = new Vector3(0, -12f, 0),
                Mesh = new SphereMesh
                {
                    Radius = 0.05f, Height = 0.1f, RadialSegments = 4, Rings = 2,
                    Material = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.35f), EmissionEnabled = true, Emission = new Color(1f, 0.75f, 0.3f), EmissionEnergyMultiplier = 5f },
                },
            };
            spark.AddChild(r.Sparks);
        }
        return r;
    }

    /// <summary>Every surface-override material of an imported model (its per-instance copies).</summary>
    private static IEnumerable<BaseMaterial3D> MaterialsOf(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null && mi.MaterialOverride == null)
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.GetSurfaceOverrideMaterial(i) is BaseMaterial3D m) yield return m;
        foreach (var child in node.GetChildren())
            foreach (var m in MaterialsOf(child))
                yield return m;
    }

    private void AnimateRefinery(SurfaceStructure s, RefineryVisual r, float delta)
    {
        var broken = s.Broken;
        var lowPower = s.Status == StructureStatus.LowPower;
        var working = !broken && (s.Status == StructureStatus.Working || lowPower);
        r.Run = Mathf.MoveToward(r.Run, working ? (lowPower ? 0.45f : 1f) : 0f, delta / 1.6f);
        r.Heat = Mathf.MoveToward(r.Heat, working ? 1f : broken ? 0.08f : 0.25f, delta / (working ? 3f : 8f));

        r.Crusher?.RotateObjectLocal(Vector3.Up, delta * 7f * r.Run);
        foreach (var fan in r.Fans) fan.RotateObjectLocal(Vector3.Up, delta * 8f * r.Run);
        const float beltSpeed = 1.1f; // units per second along the belt
        r.Flow += delta * beltSpeed * r.Run;
        foreach (var belt in r.Belts)
            belt.Uv1Offset = new Vector3(0f, Mathf.PosMod(r.Flow / 0.6f, 1f), 0f); // the glb's belt UV repeats every 0.6
        var oreShown = working || r.Run > 0.05f && s.Status != StructureStatus.NoInput;
        foreach (var (lump, across, phase, scale) in r.Ore)
        {
            lump.Visible = oreShown;
            if (!oreShown || r.OreLength <= 0f) continue;
            var d = Mathf.PosMod(phase * r.OreLength + r.Flow, r.OreLength);
            lump.Position = across + r.OreDir * d;
            // grow in at the foot, shrink into the charging hood at the head
            var k = Mathf.Clamp(Mathf.Min(d / 0.3f, (r.OreLength - d) / 0.35f), 0f, 1f);
            lump.Scale = scale * Mathf.Max(0.01f, k);
        }

        // furnace: a bright pulsing melt while working, a dull banked glow idle, a dying flicker when broken
        var pulse = 1f + 0.12f * Mathf.Sin(_time * 2.3f) + 0.06f * Mathf.Sin(_time * 7.1f + 1.3f);
        if (broken) pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(_time * 13f) * Mathf.Sin(_time * 3.7f));
        var glow = Mathf.Lerp(0.35f, 6f, r.Heat) * pulse;
        foreach (var m in r.Glows)
        {
            m.EmissionEnabled = true;
            m.EmissionEnergyMultiplier = glow;
        }
        if (r.Furnace != null) r.Furnace.LightEnergy = 1.8f * r.Heat * pulse;

        // lamps: full while working, dimmed idle or on low power, flickering when broken
        var lamps = broken ? (Mathf.Sin(_time * 17f) * Mathf.Sin(_time * 5.3f) > 0.2f ? 0.7f : 0.15f) : working && !lowPower ? 1f : 0.45f;
        foreach (var (m, energy) in r.Lamps) m.EmissionEnergyMultiplier = energy * lamps;
        if (r.PadLight != null) r.PadLight.LightEnergy = 0.7f * lamps;
        // aviation lamps blink regardless (red, once every 1.5 s)
        var blink = Mathf.PosMod(_time, 1.5f) < 0.35f ? 1f : 0.12f;
        foreach (var m in r.Beacons) m.EmissionEnergyMultiplier = 8f * blink;

        if (r.Steam0 != null && r.Steam0.Emitting != (r.Run > 0.3f)) r.Steam0.Emitting = r.Run > 0.3f;
        if (r.Steam1 != null && r.Steam1.Emitting != (r.Run > 0.6f)) r.Steam1.Emitting = r.Run > 0.6f;
        if (r.Sparks != null && r.Sparks.Emitting != broken) r.Sparks.Emitting = broken;
    }

    // ---------------------------------------------------------------- cargo pods

    /// <summary>Where a cargo pod heading for this tile sets down: the PodPad of a finished refinery or storage yard
    /// there, else null (it flies to the tile centre as before).</summary>
    private Vector3? PodPadAt(int tile)
    {
        foreach (var s in _surface.Structures)
        {
            if (s.Tile != tile || s.DefId != "refinery" || s.State != StructureState.Complete) continue;
            if (_refineries.TryGetValue(s.Id, out var r) && r.Pad != null && r.Pad.IsInsideTree() && _structures.TryGetValue(s.Id, out var v) && r.Model == v.Model)
                return r.Pad.GlobalPosition;
        }
        return YardPadAt(tile); // PlanetView.Detailed.cs: a storage yard's pod pad
    }

    /// <summary>A cargo pod's position and heading at the current frame: a high arc from the crawler; for a refinery
    /// the arc ends over its pad and the pod settles straight down onto it, level, for the last part of the flight.</summary>
    private (Vector3 Position, Basis? Basis) PodPose(CargoPod p, float alpha)
    {
        var span = Mathf.Max(1f, p.ArriveTick - p.LaunchTick);
        var t = Mathf.Clamp((_sim.Tick - p.LaunchTick + alpha) / span, 0f, 1f);
        var a = TileCentre(p.FromTile) + new Vector3(0, 4f, 0);
        var pad = PodPadAt(p.ToTile);
        var b = pad is { } padAt ? padAt + new Vector3(0, 0.6f, 0) : TileCentre(p.ToTile) + new Vector3(0, 3f, 0);
        var height = 18f + a.DistanceTo(b) * 0.12f;
        const float settle = 0.82f; // pad landings: arc until here, then straight down
        var over = b + new Vector3(0, 7f, 0);
        Vector3 At(float k)
        {
            if (pad == null) return a.Lerp(b, k) + new Vector3(0, Mathf.Sin(k * Mathf.Pi) * height, 0);
            if (k < settle)
            {
                var q = k / settle;
                return a.Lerp(over, q) + new Vector3(0, Mathf.Sin(q * Mathf.Pi) * height, 0);
            }
            var e = (k - settle) / (1f - settle);
            return over.Lerp(b, 1f - (1f - e) * (1f - e)); // ease out onto the pad
        }
        var here = At(t);
        var ahead = At(Mathf.Min(1f, t + 0.02f)) - here;
        Basis? arc = null;
        if (ahead.LengthSquared() > 0.0001f)
        {
            var dir = ahead.Normalized();
            arc = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Forward : Vector3.Up);
        }
        var flat = new Vector3(b.X - a.X, 0, b.Z - a.Z);
        if (pad == null || flat.LengthSquared() < 0.01f) return (here, arc);
        // level out over the pad: blend from the arc's heading to flat, then keep flat while it settles
        var level = Basis.LookingAt(flat.Normalized(), Vector3.Up);
        var w = Mathf.SmoothStep(settle - 0.12f, settle - 0.01f, t);
        if (arc is not { } heading || w >= 1f) return (here, level);
        return (here, new Basis(heading.GetRotationQuaternion().Slerp(level.GetRotationQuaternion(), w)));
    }
}
