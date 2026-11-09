using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// How rover damage and flat tyres look (render-only; the rules are in sim/Surface/RoverDamage.cs). A flat wheel is
/// squashed about its hub (the rover models' "Wheel&lt;code&gt;" pivots: WheelFL, WheelMR, ... — see Surface.Wheels; the
/// primitive rover names its wheel pivots the same way) and the body sits lower and leans toward it, wobbling while it
/// drives. Damage shows as scratches then scorch in the grime overlay, smoke below 25 integrity, sparks and heavy smoke
/// while disabled; a hazard hit throws up a dust puff and grit, a fresh flat a small pop of dust at the wheel. The repair
/// crew walk out to a rover on its pad as they do for the crawler.
/// </summary>
public partial class PlanetView
{
    /// <summary>One rover's wheel pivots (rest transforms) and how flat each one looks now (0..1, eased).</summary>
    private sealed class WheelRig
    {
        public required Node3D?[] Nodes;
        public required Transform3D[] Rest;
        public required float[] Squash;
        public long SeenHazard = -2;
        public long SeenFlat = -2;
        public Node3D? Smoke;
        public CpuParticles3D? Sparks;
    }

    private readonly Dictionary<int, WheelRig> _wheelRigs = new();

    /// <summary>The lean applied to each rover's model last frame (taken off again before the drive code turns it).</summary>
    private readonly Dictionary<int, Basis> _roverTilt = new();

    /// <summary>How much a flat tyre squashes the wheel (share of its height) and lowers its hub.</summary>
    private const float FlatSquash = 0.28f;

    /// <summary>Take last frame's lean off the model, so the heading code works on an upright model.</summary>
    private void UntiltRover(int id, Node3D model)
    {
        if (!_roverTilt.Remove(id, out var tilt)) return;
        model.Basis = (model.Basis * tilt.Inverse()).Orthonormalized();
    }

    private void ForgetRover(int id)
    {
        _wheelRigs.Remove(id);
        _roverTilt.Remove(id);
    }

    private WheelRig RigFor(SurfaceUnit u, SurfaceUnitDef def, Node3D model)
    {
        if (_wheelRigs.TryGetValue(u.Id, out var rig)) return rig;
        var wheels = def.Tyres?.Wheels ?? 0;
        var nodes = new Node3D?[wheels];
        var rest = new Transform3D[wheels];
        for (var i = 0; i < wheels; i++)
        {
            nodes[i] = model.FindChild("Wheel" + Wheels.Code(wheels, i), true, false) as Node3D;
            rest[i] = nodes[i]?.Transform ?? Transform3D.Identity;
        }
        rig = new WheelRig { Nodes = nodes, Rest = rest, Squash = new float[wheels], SeenHazard = u.HazardAtTick, SeenFlat = u.FlatAtTick };

        // Damage smoke over the rear deck and sparks by the front wheel arch (both off until needed).
        var height = def.Mark == 3 ? 2.4f : 2.0f;
        var smoke = BuildingModels.Puffs("DamageSmoke", new Vector3(0.3f, height, 1.0f), new Color(0.15f, 0.14f, 0.13f), 28, 2.8f, 0.8f, 2.0f, 0.12f);
        model.AddChild(smoke);
        rig.Smoke = smoke;
        var sparks = new CpuParticles3D
        {
            Name = "DamageSparks",
            Position = new Vector3(0.9f, 1.1f, -0.7f),
            Amount = 12,
            Lifetime = 0.6f,
            Emitting = false,
            LocalCoords = false,
            Explosiveness = 0.7f,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 2.5f,
            InitialVelocityMax = 6f,
            Gravity = new Vector3(0, -12f, 0),
            Mesh = new SphereMesh
            {
                Radius = 0.05f,
                Height = 0.1f,
                RadialSegments = 4,
                Rings = 2,
                Material = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.75f, 0.35f), EmissionEnergyMultiplier = 5f },
            },
        };
        model.AddChild(sparks);
        rig.Sparks = sparks;
        _wheelRigs[u.Id] = rig;
        return rig;
    }

    /// <summary>
    /// After the model is placed for this frame: squash flat wheels, lean and lower the body toward them (wobbling while
    /// it drives), set the damage look and effects, and play a puff for a fresh hazard hit or flat.
    /// </summary>
    private void UpdateRoverDamage(SurfaceUnit u, SurfaceUnitDef def, Node3D model, bool moving, float delta)
    {
        if (def.Damage == null) return;
        var rig = RigFor(u, def, model);
        var wheels = rig.Nodes.Length;

        // Wheels: ease each toward flat or round (a new tyre pops back quickly).
        var roll = 0f;
        var pitch = 0f;
        var lower = 0f;
        for (var i = 0; i < wheels; i++)
        {
            var flat = (u.FlatTyres & (1 << i)) != 0 ? 1f : 0f;
            rig.Squash[i] = Mathf.MoveToward(rig.Squash[i], flat, delta * (flat > rig.Squash[i] ? 3f : 1.5f));
            var f = rig.Squash[i];
            var rest = rig.Rest[i];
            if (rig.Nodes[i] is { } node)
                node.Transform = new Transform3D(rest.Basis * Basis.FromScale(new Vector3(1f, 1f - FlatSquash * f, 1f + 0.12f * f)), rest.Origin);
            if (f <= 0.001f) continue;
            // The hub drops by what the tyre lost; the body leans toward that corner (half from the roll, half from the
            // pitch for an end wheel) and sits a little lower overall.
            var radius = Mathf.Max(0.3f, rest.Origin.Y);
            var drop = radius * FlatSquash * f;
            var x = Mathf.Abs(rest.Origin.X) < 0.2f ? 1.3f * (i % 2 == 0 ? -1f : 1f) : rest.Origin.X;
            roll += -drop / (2f * x);
            if (Mathf.Abs(rest.Origin.Z) > 0.6f) pitch += 0.6f * drop / (2f * rest.Origin.Z);
            lower = Mathf.Max(lower, drop * 0.35f);
        }
        if (roll != 0f || pitch != 0f)
        {
            if (moving)
            {
                // A flat thumps and pulls: the lean rocks as the wheel turns.
                var wobble = 1f + 0.3f * Mathf.Sin(_time * 7f + u.Id) + 0.12f * Mathf.Sin(_time * 19f + u.Id * 2f);
                roll *= wobble;
                pitch *= wobble;
            }
            var tilt = Basis.FromEuler(new Vector3(pitch, moving ? 0.012f * Mathf.Sin(_time * 3.1f + u.Id) : 0f, roll));
            model.Basis = model.Basis * tilt;
            _roverTilt[u.Id] = tilt;
            model.Position -= new Vector3(0, lower, 0);
        }

        // Scratches, then scorch, below 75 integrity (blender/rover_variants.py DAMAGE bands); smoke below 25; disabled:
        // heavy smoke and sparks.
        var integrity = u.Integrity.ToFloat();
        if (_grime.TryGetValue(u.Id, out var grime)) grime.Damage = Mathf.Clamp((75f - integrity) / 75f, 0f, 1f);
        var disabled = u.Broken;
        var crewAtWork = _surface.IsRepairing(u) && _surface.CrewOut(u) >= Fix64.One;
        if (rig.Smoke is CpuParticles3D smoke)
        {
            var on = (integrity < 25f || disabled) && !crewAtWork;
            if (smoke.Emitting != on) smoke.Emitting = on;
            smoke.ScaleAmountMax = disabled ? 2.8f : 1.6f; // heavier while disabled
        }
        if (rig.Sparks is { } sparks)
        {
            var on = disabled && !crewAtWork && Mathf.Sin(_time * 2.3f + u.Id) > 0.35f;
            if (sparks.Emitting != on) sparks.Emitting = on;
        }

        // A fresh hazard hit or flat (not ones from before the model existed; only our own, as others' may be in fog).
        if (u.Owner != _localPlayer) rig.SeenHazard = u.HazardAtTick;
        if (u.Owner != _localPlayer) rig.SeenFlat = u.FlatAtTick;
        if (u.HazardAtTick != rig.SeenHazard)
        {
            rig.SeenHazard = u.HazardAtTick;
            Jolt(model.GlobalTransform * new Vector3(0, 0.6f, -1.2f), u.LastHazardDamage >= 12, SprayRig.HazeColour(SprayRig.KindFor(_surface.Map, u.Tile)));
        }
        if (u.FlatAtTick != rig.SeenFlat)
        {
            rig.SeenFlat = u.FlatAtTick;
            if (u.LastFlat >= 0 && u.LastFlat < wheels)
                Jolt(model.GlobalTransform * (rig.Rest[u.LastFlat].Origin with { Y = 0.3f }), false, SprayRig.HazeColour(SprayRig.KindFor(_surface.Map, u.Tile)));
        }
    }

    /// <summary>A short burst at a rover: dust (the ground's colour) and, for a hard hit, a spray of grit and sparks.</summary>
    private void Jolt(Vector3 at, bool hard, Color dust)
    {
        var root = new Node3D { Position = at };
        AddChild(root);
        var puff = BuildingModels.Puffs("Jolt", Vector3.Zero, dust, hard ? 22 : 12, 1.6f, 0.6f, hard ? 1.8f : 1.1f, 0f);
        puff.OneShot = true;
        puff.Explosiveness = 0.9f;
        puff.Spread = 75f;
        puff.InitialVelocityMax = hard ? 4.5f : 2.5f;
        puff.Emitting = true;
        root.AddChild(puff);
        if (hard)
            root.AddChild(new CpuParticles3D
            {
                Amount = 16,
                Lifetime = 0.8f,
                OneShot = true,
                Explosiveness = 1f,
                Direction = Vector3.Up,
                Spread = 70f,
                InitialVelocityMin = 3f,
                InitialVelocityMax = 8f,
                Gravity = new Vector3(0, -16f, 0),
                Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.08f, 0.12f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.3f, 0.27f), Roughness = 0.9f } },
                Emitting = true,
            });
        GetTree().CreateTimer(3.0).Timeout += root.QueueFree;
    }

    /// <summary>A rover's label over it: disabled, being repaired, changing a tyre, or a flat; empty when all is well.
    /// Also walks the repair crew out to it on its pad.</summary>
    private string RoverText(SurfaceUnit u, SurfaceUnitDef def, Node3D model, float alpha, float delta)
    {
        if (def.Damage == null) return "";
        UpdateCrew(u, model, alpha, delta);
        if (_surface.IsRepairing(u))
        {
            if (u.RepairWaiting) return "Repair waiting for kits";
            if (_surface.CrewOut(u) < Fix64.One) return "Repair crew on the way";
            return $"Repairing {Mathf.FloorToInt(u.Integrity.ToFloat())}%";
        }
        if (u.Broken) return u.Path.Count > 0 ? "Disabled: limping to the pad" : "Disabled";
        if (_surface.ChangingTyre(u)) return $"Changing tyre {Mathf.FloorToInt(_surface.TyreChangeFraction(u).ToFloat() * 100)}%";
        var flats = _surface.FlatCount(u);
        return flats switch { 0 => "", 1 => "Flat tyre", _ => $"{flats} flat tyres" };
    }
}
