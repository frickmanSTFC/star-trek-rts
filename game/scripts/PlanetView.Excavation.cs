using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Render-only: the Excavation Site's animation (BuildingModels.Excavation.cs), driven by the dig like
/// PlanetView.MachineShop.cs does for the fabricator. Digging (the structure Working / LowPower, or the sim's
/// <c>DigProgress</c> moving): the gantry crane runs trips (travel over a spot, the skip dips into the pit, a dust puff,
/// it comes up, runs back toward the sieves and tips), the skip swings as the crane moves, the sieves shake in bursts
/// with dust under them, the ruin's inlays glow faintly, the status board is green; low power = half speed. Locked
/// (<c>DigProblem</c> mentions "locked", or the ruin's <c>LockedUntilTick</c> is ahead): the crane parks, the defence
/// pylon wakes (its glow and light pulse red, sparks spit at its tip), the board flashes red. Exhausted (the ruin is
/// dug out) or idle: parked, the board blue-grey (exhausted) or amber (idle). Paused: parked, grey. Broken: frozen, dark,
/// lamps flicker. Parts are looked up once per model; another player's site moves only while it's in sight.
/// </summary>
public partial class PlanetView
{
    private const float CraneSpeed = 0.9f, HookSpeed = 0.75f, DigDwell = 1.4f, DumpDwell = 1.0f, SieveShake = 0.055f;

    private enum DigStage { Travel, Lower, Dig, Raise, Return, Dump }

    private sealed class DigVisual
    {
        public required Node3D Model;
        public required Glows Glow;
        public Node3D? Crane, Hook, Cable;
        public Vector3 CraneA, CraneB, HookUp, HookDown, CableAt;
        public Basis HookRest = Basis.Identity;
        public float CableRest = 1f, CraneT, ParkT, HookK, Sway, SwayVel, LastT;
        public readonly List<(Node3D Sieve, Vector3 At, Vector3 Axis)> Sieves = new();
        public CpuParticles3D? Dust0, Dust1, DigDust, Sparks;
        public OmniLight3D? Light, GlowLight;
        public float Run, StageTime, Shake;
        public DigStage Stage;
        public int Trip;
        public float LastProgress = -1f, MovedAt = -100f;
    }

    private readonly Dictionary<int, DigVisual> _digs = new();
    private readonly HashSet<int> _digsAlive = new();
    private readonly List<int> _digsGone = new();

    /// <summary>Every finished Excavation Site's animation, after UpdateMachineShops (models exist, in the tree).</summary>
    private void UpdateExcavations(float delta)
    {
        _digsAlive.Clear();
        foreach (var s in _surface.Structures)
        {
            if (s.DefId != "excavation_site" || s.State != StructureState.Complete || !_structures.TryGetValue(s.Id, out var v)) continue;
            _digsAlive.Add(s.Id);
            if (!_digs.TryGetValue(s.Id, out var m) || m.Model != v.Model) _digs[s.Id] = m = DigParts(v.Model);
            if (!ShowsLive(s.Owner, s.Tile)) continue; // fog: a rival's dig keeps its last pose until it's in sight
            AnimateDig(s, m, delta);
        }
        if (_digs.Count == _digsAlive.Count) return;
        _digsGone.Clear();
        foreach (var id in _digs.Keys)
            if (!_digsAlive.Contains(id)) _digsGone.Add(id);
        foreach (var id in _digsGone) _digs.Remove(id);
    }

    private static DigVisual DigParts(Node3D model)
    {
        var m = new DigVisual
        {
            Model = model,
            Glow = new Glows(model, "Status", "Glow"),
            Crane = Part(model, "Crane"),
            Hook = Part(model, "Hook"),
            Cable = Part(model, "Cable"),
            Light = model.FindChild("DigOmni", true, false) as OmniLight3D,
            GlowLight = model.FindChild("GlowOmni", true, false) as OmniLight3D,
        };
        if (m.Crane != null)
        {
            m.CraneA = Sibling(m.Crane, "CraneMin")?.Position ?? m.Crane.Position;
            m.CraneB = Sibling(m.Crane, "CraneMax")?.Position ?? m.Crane.Position;
            // where it stands when built (the Look) is where it parks
            var ab = m.CraneB - m.CraneA;
            m.ParkT = ab.LengthSquared() > 1e-4f ? Mathf.Clamp((m.Crane.Position - m.CraneA).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            m.CraneT = m.LastT = m.ParkT;
        }
        if (m.Hook != null)
        {
            m.HookUp = m.Hook.Position;
            m.HookDown = Sibling(m.Hook, "HookLow")?.Position ?? m.HookUp + new Vector3(0, -2f, 0);
            m.HookRest = m.Hook.Basis;
        }
        if (m.Cable != null)
        {
            m.CableRest = m.Cable.Scale.Y;
            m.CableAt = m.Cable.Position;
        }
        for (var k = 0; k < 2; k++)
            if (Part(model, $"Sieve{k}") is { } sieve) m.Sieves.Add((sieve, sieve.Position, sieve.Basis.X.Normalized()));
        m.Dust0 = PuffsAt(model, "Dust0", "SieveDust", new Color(0.55f, 0.45f, 0.34f), 14, 1.8f, 0.35f, 0.9f, 0.25f);
        m.Dust1 = PuffsAt(model, "Dust1", "SieveDust", new Color(0.55f, 0.45f, 0.34f), 14, 1.8f, 0.35f, 0.9f, 0.25f);
        m.DigDust = PuffsAt(model, "Dust2", "DigDust", new Color(0.5f, 0.41f, 0.32f), 16, 2.2f, 0.5f, 1.3f, 0.35f);
        if (Part(model, "SparkPoint") is { } sp) m.Sparks = Sparks(sp);
        return m;
    }

    private void AnimateDig(SurfaceStructure s, DigVisual m, float delta)
    {
        var ruin = _surface.RuinAt(s.Tile);
        var problem = _surface.DigProblem(s);
        var progress = _surface.DigProgress(s).ToFloat();
        if (m.LastProgress >= 0f && Mathf.Abs(progress - m.LastProgress) > 1e-5f) m.MovedAt = _time;
        m.LastProgress = progress;

        var broken = s.Broken;
        var paused = !broken && (s.Paused || s.Status == StructureStatus.Paused);
        var locked = !broken && (problem != null && problem.Contains("locked", StringComparison.OrdinalIgnoreCase)
                                 || ruin != null && ruin.LockedUntilTick > _sim.Tick);
        var exhausted = ruin?.Exhausted == true || problem != null && problem.Contains("exhaust", StringComparison.OrdinalIgnoreCase);
        var lowPower = s.Status == StructureStatus.LowPower;
        var moving = _time - m.MovedAt < 1.5f;
        var working = !broken && !paused && !locked && !exhausted
                      && (moving || s.Status is StructureStatus.Working or StructureStatus.LowPower);
        if (!broken) m.Run = Mathf.MoveToward(m.Run, working ? (lowPower ? 0.5f : 1f) : 0f, delta / 1.2f);

        if (!broken) RunCrane(m, s.Id, working, delta);
        PoseCrane(m, delta, broken);

        // the sieves: shake in bursts while it digs, dust underneath
        var burst = working && Mathf.PosMod(_time + s.Id * 0.7f, 4.6f) < 3.1f;
        if (!broken) m.Shake = Mathf.MoveToward(m.Shake, burst ? m.Run : 0f, delta * 2.5f);
        for (var k = 0; k < m.Sieves.Count; k++)
        {
            var (sieve, rest, axis) = m.Sieves[k];
            var x = SieveShake * m.Shake * Mathf.Sin(_time * Mathf.Tau * 3.2f + k * 1.9f);
            sieve.Position = rest + axis * x;
        }
        Emit(m.Dust0, m.Shake > 0.3f);
        Emit(m.Dust1, m.Shake > 0.3f && m.Sieves.Count > 1);
        Emit(m.DigDust, working && m.Stage == DigStage.Dig && m.Run > 0.2f);

        // the defence system: red pulse + sparks while locked, a faint teal while it digs
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5f + s.Id);
        Color glowColour;
        float glowEnergy;
        if (locked)
        {
            glowColour = new Color(1f, 0.16f, 0.08f);
            glowEnergy = 1.2f + 2.6f * pulse;
        }
        else
        {
            glowColour = new Color(0.35f, 0.95f, 0.85f);
            glowEnergy = broken ? 0.1f : exhausted ? 0.15f : 0.25f + 0.35f * m.Run;
        }
        foreach (var mat in m.Glow["Glow"])
        {
            mat.EmissionEnabled = true;
            mat.Emission = glowColour;
            mat.EmissionEnergyMultiplier = glowEnergy;
        }
        if (m.GlowLight != null)
        {
            m.GlowLight.LightColor = glowColour;
            m.GlowLight.LightEnergy = locked ? 0.6f + 1.4f * pulse : 0f;
        }
        Emit(m.Sparks, locked && Mathf.Sin(_time * 3.1f + s.Id) > 0.25f);

        // the status board: green digging, amber idle, flashing red locked, blue-grey dug out, grey paused, red broken
        var colour = broken ? new Color(1f, 0.15f, 0.08f)
            : locked ? new Color(1f, 0.12f, 0.06f)
            : paused ? new Color(0.55f, 0.58f, 0.62f)
            : exhausted ? new Color(0.45f, 0.6f, 0.8f)
            : working ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.65f, 0.15f);
        var energy = broken ? (Mathf.PosMod(_time, 0.6f) < 0.3f ? 0.3f : 1.6f)
            : locked ? (Mathf.PosMod(_time, 0.5f) < 0.25f ? 0.5f : 3.5f)
            : working ? 3f : paused ? 0.7f : exhausted ? 1.2f : 1.1f;
        foreach (var mat in m.Glow["Status"])
        {
            mat.Emission = colour;
            mat.AlbedoColor = colour;
            mat.EmissionEnergyMultiplier = energy;
        }
        var lamps = LampLevel(broken, working || locked, lowPower, s.Id);
        m.Glow.Set(lamps, _time + s.Id * 0.37f);
        if (m.Light != null) m.Light.LightEnergy = 1.1f * lamps;
    }

    /// <summary>One crane trip after another while digging: travel over a spot, lower, dig, raise, run back to the
    /// sieve end (CraneMax), tip, repeat; parked (hook up, at its built position) otherwise.</summary>
    private void RunCrane(DigVisual m, int id, bool working, float delta)
    {
        var pace = working ? Mathf.Max(m.Run, 0.15f) : 0.6f;
        var length = Mathf.Max(0.5f, (m.CraneB - m.CraneA).Length());
        var step = delta * CraneSpeed * pace / length;
        var hookStep = delta * HookSpeed * pace / Mathf.Max(0.5f, (m.HookUp - m.HookDown).Length());
        if (!working)
        {
            // raise first, then roll home
            m.Stage = DigStage.Travel;
            m.StageTime = 0f;
            m.HookK = Mathf.MoveToward(m.HookK, 0f, hookStep);
            if (m.HookK < 0.05f) m.CraneT = Mathf.MoveToward(m.CraneT, m.ParkT, step);
            return;
        }
        m.StageTime += delta * pace;
        switch (m.Stage)
        {
            case DigStage.Travel:
                var spot = 0.08f + 0.62f * Hash01(id * 7919 + m.Trip * 104729);
                m.HookK = Mathf.MoveToward(m.HookK, 0f, hookStep);
                m.CraneT = Mathf.MoveToward(m.CraneT, spot, step);
                if (Mathf.IsEqualApprox(m.CraneT, spot) && m.HookK <= 0f) Next(m, DigStage.Lower);
                break;
            case DigStage.Lower:
                m.HookK = Mathf.MoveToward(m.HookK, 1f, hookStep);
                if (m.HookK >= 1f) Next(m, DigStage.Dig);
                break;
            case DigStage.Dig:
                if (m.StageTime >= DigDwell) Next(m, DigStage.Raise);
                break;
            case DigStage.Raise:
                m.HookK = Mathf.MoveToward(m.HookK, 0f, hookStep);
                if (m.HookK <= 0f) Next(m, DigStage.Return);
                break;
            case DigStage.Return:
                m.CraneT = Mathf.MoveToward(m.CraneT, 1f, step);
                if (m.CraneT >= 1f) Next(m, DigStage.Dump);
                break;
            case DigStage.Dump:
                m.HookK = Mathf.MoveToward(m.HookK, 0.12f, hookStep);
                if (m.StageTime >= DumpDwell)
                {
                    m.Trip++;
                    Next(m, DigStage.Travel);
                }
                break;
        }
    }

    private static void Next(DigVisual m, DigStage stage)
    {
        m.Stage = stage;
        m.StageTime = 0f;
    }

    /// <summary>Crane position, hook height, rope length; the skip swings back from the crane's motion (a damped
    /// pendulum about the hoist).</summary>
    private static void PoseCrane(DigVisual m, float delta, bool frozen)
    {
        if (m.Crane == null) return;
        m.Crane.Position = m.CraneA.Lerp(m.CraneB, m.CraneT);
        if (!frozen && delta > 0f)
        {
            var speed = (m.CraneT - m.LastT) * (m.CraneB - m.CraneA).Length() / delta;
            // pendulum: the crane's acceleration kicks it, gravity pulls it back, a little damping
            m.SwayVel += (-speed * 0.9f - m.Sway * 4.5f) * delta - m.SwayVel * 1.2f * delta;
            m.Sway = Mathf.Clamp(m.Sway + m.SwayVel * delta, -0.35f, 0.35f);
        }
        m.LastT = m.CraneT;
        if (m.Hook == null) return;
        var hook = m.HookUp.Lerp(m.HookDown, Ease(m.HookK));
        // the travel runs along the crane's parent Z (front-back), so the skip swings about X
        var swing = new Basis(Vector3.Right, m.Sway);
        m.Hook.Position = m.CableAt + swing * (hook - m.CableAt);
        m.Hook.Basis = swing * m.HookRest;
        if (m.Cable != null)
            m.Cable.Basis = swing * Basis.FromScale(new Vector3(1f, m.CableRest + (m.HookUp.Y - hook.Y), 1f));
    }

    /// <summary>A stable 0..1 from an integer (no RNG state).</summary>
    private static float Hash01(int k)
    {
        var x = (uint)k * 2654435761u;
        x ^= x >> 15;
        x *= 2246822519u;
        x ^= x >> 13;
        return (x & 0xFFFFFF) / (float)0x1000000;
    }
}
