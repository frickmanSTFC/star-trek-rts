using System.Collections.Generic;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Render-only: the working animation of the detailed Mining Complex, Storage Container yard and Fusion Reactor models
/// (BuildingModels.Detailed.cs), driven by each structure's sim status, like PlanetView.Refinery.cs does for the
/// refinery. Parts are looked up by name once per model (a fresh model after completion or an upgrade gets a fresh
/// lookup); primitive stand-ins have none of them, so nothing moves on those except their old "Spin".
/// </summary>
public partial class PlanetView
{
    /// <summary>The detailed buildings' animations, after UpdateStructures (models exist and are in the tree).</summary>
    private void UpdateDetailedBuildings(float delta)
    {
        var alive = new HashSet<int>();
        _fillShare.Clear();
        foreach (var s in _surface.Structures)
        {
            if (s.State != StructureState.Complete || !_structures.TryGetValue(s.Id, out var v)) continue;
            switch (s.DefId)
            {
                case "mine":
                    alive.Add(s.Id);
                    if (!_mines.TryGetValue(s.Id, out var m) || m.Model != v.Model) _mines[s.Id] = m = MineParts(v.Model);
                    AnimateMine(s, m, delta);
                    break;
                case "storage_container":
                    alive.Add(s.Id);
                    if (!_yards.TryGetValue(s.Id, out var y) || y.Model != v.Model) _yards[s.Id] = y = YardParts(v.Model, s.Id);
                    AnimateYard(s, y, delta);
                    break;
                case "fusion_reactor":
                    alive.Add(s.Id);
                    if (!_reactors.TryGetValue(s.Id, out var r) || r.Model != v.Model) _reactors[s.Id] = r = ReactorParts(v.Model);
                    AnimateReactor(s, r, delta);
                    break;
            }
        }
        Prune(_mines, alive);
        Prune(_yards, alive);
        Prune(_reactors, alive);
    }

    private static void Prune<T>(Dictionary<int, T> visuals, HashSet<int> alive)
    {
        foreach (var id in new List<int>(visuals.Keys))
            if (!alive.Contains(id)) visuals.Remove(id);
    }

    // ---------------------------------------------------------------- shared bits

    /// <summary>Lamps and blinking beacons of a detailed model (materials by name, each copy once).</summary>
    private sealed class Glows
    {
        public readonly List<(BaseMaterial3D Material, float Energy)> Lamps = new();
        public readonly List<BaseMaterial3D> Beacons = new();
        public readonly Dictionary<string, List<BaseMaterial3D>> Named = new();

        public Glows(Node3D model, params string[] named)
        {
            foreach (var name in named) Named[name] = new List<BaseMaterial3D>();
            var seen = new HashSet<BaseMaterial3D>();
            foreach (var m in MaterialsOf(model))
            {
                if (!seen.Add(m)) continue;
                var name = m.ResourceName;
                if (Named.TryGetValue(name, out var list)) list.Add(m);
                else if (name == "Beacon") Beacons.Add(m);
                else if (name is "Lamp" or "PadLight" or "Window" or "Interior" or "ShaftGlow")
                    Lamps.Add((m, m.EmissionEnergyMultiplier > 0f ? m.EmissionEnergyMultiplier : 1f));
            }
        }

        public List<BaseMaterial3D> this[string name] => Named[name];

        /// <summary>Lamps at `level` (1 = full), beacons blinking red once every 1.5 s.</summary>
        public void Set(float level, float time)
        {
            foreach (var (m, energy) in Lamps) m.EmissionEnergyMultiplier = energy * level;
            var blink = Mathf.PosMod(time, 1.5f) < 0.35f ? 1f : 0.12f;
            foreach (var m in Beacons) m.EmissionEnergyMultiplier = 8f * blink;
        }
    }

    /// <summary>Lamp level for a status: full while working, dimmed idle or on low power, flickering when broken.</summary>
    private float LampLevel(bool broken, bool working, bool lowPower, float seed = 0f) =>
        broken ? (Mathf.Sin(_time * 17f + seed) * Mathf.Sin(_time * 5.3f + seed) > 0.2f ? 0.7f : 0.15f)
        : working && !lowPower ? 1f : 0.45f;

    private static Node3D? Part(Node3D model, string name) => model.FindChild(name, true, false) as Node3D;

    /// <summary>A sibling marker of a part (the glbs keep a part's travel ends next to it).</summary>
    private static Node3D? Sibling(Node3D? part, string name) => part?.GetParent()?.GetNodeOrNull<Node3D>(name);

    private static CpuParticles3D Sparks(Node3D at)
    {
        var sparks = new CpuParticles3D
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
        at.AddChild(sparks);
        return sparks;
    }

    private static CpuParticles3D? PuffsAt(Node3D model, string marker, string name, Color colour, int amount, float life, float size0, float size1, float drift)
    {
        if (Part(model, marker) is not { } at) return null;
        var p = BuildingModels.Puffs(name, Vector3.Zero, colour, amount, life, size0, size1, drift);
        at.AddChild(p);
        return p;
    }

    private static void Emit(CpuParticles3D? p, bool on)
    {
        if (p != null && p.Emitting != on) p.Emitting = on;
    }

    /// <summary>Ore lumps riding a belt: OreFlow at the foot, its child OreEnd at the head, lumps "Ore*" between.</summary>
    private sealed class OreRun
    {
        public readonly List<(Node3D Lump, Vector3 Across, float Phase, Vector3 Scale)> Lumps = new();
        public Vector3 Dir;
        public float Length;

        public OreRun(Node3D model)
        {
            if (Part(model, "OreFlow") is not { } flow || flow.FindChild("OreEnd", false, false) is not Node3D end) return;
            Length = end.Position.Length();
            Dir = Length > 0.01f ? end.Position / Length : Vector3.Zero;
            foreach (var child in flow.GetChildren())
                if (child is Node3D lump && lump != end && child.Name.ToString().StartsWith("Ore"))
                {
                    var along = lump.Position.Dot(Dir);
                    Lumps.Add((lump, lump.Position - Dir * along, Length > 0 ? along / Length : 0f, lump.Scale));
                }
        }

        public void Ride(float flow, bool shown)
        {
            foreach (var (lump, across, phase, scale) in Lumps)
            {
                lump.Visible = shown;
                if (!shown || Length <= 0f) continue;
                var d = Mathf.PosMod(phase * Length + flow, Length);
                lump.Position = across + Dir * d;
                var k = Mathf.Clamp(Mathf.Min(d / 0.3f, (Length - d) / 0.35f), 0f, 1f);
                lump.Scale = scale * Mathf.Max(0.01f, k);
            }
        }
    }

    // ---------------------------------------------------------------- mine

    private sealed class MineVisual
    {
        public required Node3D Model;
        public Node3D? Wheel, Hoist, Cage, Drill, Fan;
        public Vector3 HoistTop, HoistLow, CageLow, CageHigh, DrillTop, DrillLow;
        public required OreRun Ore;
        public required Glows Glow;
        public CpuParticles3D? Dust, Exhaust, Sparks;
        public OmniLight3D? Light;
        public float Run, Cycle, Flow, Depth, DrillDepth;
        public bool Cycling;
    }

    private readonly Dictionary<int, MineVisual> _mines = new();

    // the hoist cycle in seconds at full speed: dump at the top, run down, load at the bottom, run up
    private const float HoistDump = 2.2f, HoistRun = 4.2f, HoistLoad = 1.8f, WheelRadius = 1.2f;

    private static MineVisual MineParts(Node3D model)
    {
        var m = new MineVisual
        {
            Model = model,
            Wheel = Part(model, "Wheel"),
            Hoist = Part(model, "Hoist"),
            Cage = Part(model, "Cage"),
            Drill = Part(model, "Drill"),
            Fan = Part(model, "Fan0"),
            Ore = new OreRun(model),
            Glow = new Glows(model, "Belt"),
            Light = model.FindChild("MineOmni", true, false) as OmniLight3D,
        };
        if (m.Hoist != null) { m.HoistTop = m.Hoist.Position; m.HoistLow = Sibling(m.Hoist, "HoistLow")?.Position ?? m.HoistTop; }
        if (m.Cage != null) { m.CageLow = m.Cage.Position; m.CageHigh = Sibling(m.Cage, "CageHigh")?.Position ?? m.CageLow; }
        if (m.Drill != null) { m.DrillTop = m.Drill.Position; m.DrillLow = Sibling(m.Drill, "DrillLow")?.Position ?? m.DrillTop; }
        m.Dust = PuffsAt(model, "Dust", "OreDust", new Color(0.5f, 0.42f, 0.33f), 20, 2.6f, 0.6f, 1.6f, 0.4f);
        m.Exhaust = PuffsAt(model, "Smoke0", "Exhaust", new Color(0.3f, 0.3f, 0.3f), 12, 2.4f, 0.35f, 1.0f, 0.3f);
        if (Part(model, "SparkPoint") is { } sp) m.Sparks = Sparks(sp);
        return m;
    }

    /// <summary>Working: the skip runs up and down the headframe (the cage the other way, the sheaves turning with the
    /// ropes), the belt runs with ore on it, dust rises off the heap, the drill turns and strokes, the generator fan
    /// spins and smokes. Low power: half speed. Idle (deposit used up, storage full, no upkeep): the skip finishes
    /// its run and parks at the dump, lamps dim. Broken: stopped, lamps flicker, sparks at the generator.</summary>
    private void AnimateMine(SurfaceStructure s, MineVisual m, float delta)
    {
        var broken = s.Broken;
        var lowPower = s.Status == StructureStatus.LowPower;
        var working = !broken && (s.Status == StructureStatus.Working || lowPower);
        m.Run = Mathf.MoveToward(m.Run, working ? (lowPower ? 0.5f : 1f) : 0f, delta / 1.5f);

        // the hoist: depth 0 = skip at the dump .. 1 = at the bottom; parks at the top when it stops
        var period = 2f * HoistRun + HoistDump + HoistLoad;
        var before = m.Depth;
        // a new cycle only starts with the skip at the dump; stopping, it climbs back there and waits
        if (working && !m.Cycling && m.Depth <= 0.001f)
        {
            m.Cycling = true;
            m.Cycle = 0f;
        }
        if (!working) m.Cycling = false;
        if (m.Cycling)
        {
            m.Cycle = Mathf.PosMod(m.Cycle + delta * m.Run, period);
            var t = m.Cycle;
            m.Depth = t < HoistDump ? 0f
                : t < HoistDump + HoistRun ? Ease((t - HoistDump) / HoistRun)
                : t < HoistDump + HoistRun + HoistLoad ? 1f
                : 1f - Ease((t - HoistDump - HoistRun - HoistLoad) / HoistRun);
        }
        else m.Depth = Mathf.MoveToward(m.Depth, 0f, delta * 0.6f / HoistRun);
        if (m.Hoist != null) m.Hoist.Position = m.HoistTop.Lerp(m.HoistLow, m.Depth);
        if (m.Cage != null) m.Cage.Position = m.CageLow.Lerp(m.CageHigh, m.Depth);
        var travel = m.HoistTop.DistanceTo(m.HoistLow);
        m.Wheel?.RotateObjectLocal(Vector3.Up, (m.Depth - before) * travel / WheelRadius);

        // the drill: turns while it works and strokes down slowly, then pulls up for the next rod
        if (m.Drill != null)
        {
            m.Drill.RotateObjectLocal(Vector3.Up, delta * 3.2f * m.Run);
            if (m.Run > 0.01f) m.DrillDepth = Mathf.PosMod(m.DrillDepth + delta * m.Run / 14f, 1f);
            var k = m.DrillDepth < 0.85f ? m.DrillDepth / 0.85f : 1f - (m.DrillDepth - 0.85f) / 0.15f;
            m.Drill.Position = m.DrillTop.Lerp(m.DrillLow, k);
        }
        m.Fan?.RotateObjectLocal(Vector3.Up, delta * 9f * (broken ? 0f : Mathf.Max(m.Run, 0.25f)));

        m.Flow += delta * 1.0f * m.Run;
        foreach (var belt in m.Glow["Belt"])
            belt.Uv1Offset = new Vector3(0f, Mathf.PosMod(m.Flow / 0.6f, 1f), 0f);
        m.Ore.Ride(m.Flow, m.Run > 0.05f);

        var lamps = LampLevel(broken, working, lowPower, s.Id);
        if (s.Status == StructureStatus.Depleted && !broken) lamps = 0.25f;
        m.Glow.Set(lamps, _time + s.Id * 0.37f);
        if (m.Light != null) m.Light.LightEnergy = 0.9f * lamps;
        Emit(m.Dust, m.Run > 0.4f);
        Emit(m.Exhaust, !broken && m.Run > 0.1f);
        Emit(m.Sparks, broken);
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    // ---------------------------------------------------------------- storage yard

    private sealed class YardVisual
    {
        public required Node3D Model;
        public Node3D? Gantry, Trolley, Hook, Cable, Pad;
        public Vector3 G0, G1, T0, T1, HookUp, HookDown;
        public float CableRest;
        public readonly List<BaseMaterial3D> Fill = new();
        public required Glows Glow;
        public OmniLight3D? PadLight;
        public required RandomNumberGenerator Rng;
        // crane job: move to (g, t), lower, hold, raise; Busy counts down after the stock changes
        public float GFrom, GTo, TFrom, TTo, Job, JobLength, Busy, LastStored = -1f;
        public float Gp = 0.5f, Tp = 0.5f, Hp;
        public int Phase = 3;
        public bool Lifting;
    }

    private readonly Dictionary<int, YardVisual> _yards = new();
    private readonly Dictionary<int, float> _fillShare = new();

    private static YardVisual YardParts(Node3D model, int id)
    {
        var y = new YardVisual
        {
            Model = model,
            Gantry = Part(model, "Gantry"),
            Trolley = Part(model, "Trolley"),
            Hook = Part(model, "Hook"),
            Cable = Part(model, "Cable"),
            Pad = Part(model, "PodPad"),
            Glow = new Glows(model),
            PadLight = model.FindChild("PadOmni", true, false) as OmniLight3D,
            Rng = new RandomNumberGenerator { Seed = (ulong)(id * 7919 + 17) },
        };
        if (y.Gantry != null)
        {
            y.G0 = Sibling(y.Gantry, "GantryMin")?.Position ?? y.Gantry.Position;
            y.G1 = Sibling(y.Gantry, "GantryMax")?.Position ?? y.Gantry.Position;
            var span = y.G0.DistanceTo(y.G1);
            y.Gp = span > 0.01f ? y.G0.DistanceTo(y.Gantry.Position) / span : 0.5f;
        }
        if (y.Trolley != null)
        {
            y.T0 = Sibling(y.Trolley, "TrolleyMin")?.Position ?? y.Trolley.Position;
            y.T1 = Sibling(y.Trolley, "TrolleyMax")?.Position ?? y.Trolley.Position;
            var span = y.T0.DistanceTo(y.T1);
            y.Tp = span > 0.01f ? y.T0.DistanceTo(y.Trolley.Position) / span : 0.5f;
        }
        if (y.Hook != null)
        {
            y.HookUp = y.Hook.Position;
            y.HookDown = Sibling(y.Hook, "HookLow")?.Position ?? y.HookUp;
        }
        if (y.Cable != null) y.CableRest = y.Cable.Scale.Y;
        for (var k = 0; k < 8; k++)
            if (model.FindChild($"FillLight{k}", true, false) is MeshInstance3D light && light.Mesh != null && light.Mesh.GetSurfaceCount() > 0
                && light.GetSurfaceOverrideMaterial(0) is BaseMaterial3D m)
                y.Fill.Add(m);
        y.GFrom = y.GTo = y.Gp;
        y.TFrom = y.TTo = y.Tp;
        return y;
    }

    /// <summary>The owner's storage use 0..1 (stored / capacity), once per frame per owner.</summary>
    private float FillShare(int owner)
    {
        if (_fillShare.TryGetValue(owner, out var share)) return share;
        var cap = _surface.StorageCapacity(owner).ToFloat();
        share = cap > 0f ? Mathf.Clamp(_surface.Stored(_sim, owner).ToFloat() / cap, 0f, 1f) : 0f;
        _fillShare[owner] = share;
        return share;
    }

    /// <summary>The fill gauge lights bottom-up with the owner's storage share (green, amber near the top, red at the
    /// top; all of them blink red when full). The gantry crane works while goods come and go (the stock changed in
    /// the last ~10 s): it rolls to a stack, runs the trolley over it, lowers the spreader, lifts, moves on; then it
    /// parks. Pods land on the pad (PodPadAt).</summary>
    private void AnimateYard(SurfaceStructure s, YardVisual y, float delta)
    {
        var share = FillShare(s.Owner);
        var stored = _surface.Stored(_sim, s.Owner).ToFloat();
        if (y.LastStored >= 0f && Mathf.Abs(stored - y.LastStored) > 0.01f) y.Busy = 10f;
        y.LastStored = stored;
        y.Busy = Mathf.Max(0f, y.Busy - delta);

        // the crane: 0 travel, 1 lower, 2 hold, 3 raise
        y.Job += delta;
        if (y.Job >= y.JobLength)
        {
            y.Job = 0f;
            y.Phase = (y.Phase + 1) % 4;
            if (y.Phase == 0)
            {
                y.GFrom = y.Gp;
                y.TFrom = y.Tp;
                y.Lifting = y.Busy > 0f;
                if (y.Lifting)
                {
                    y.GTo = y.Rng.RandiRange(0, 2) * 0.5f;
                    y.TTo = y.Rng.RandiRange(0, 1);
                }
                else
                {
                    y.GTo = y.Gp;
                    y.TTo = y.Tp;
                }
            }
            var dist = Mathf.Abs(y.GTo - y.GFrom) * y.G0.DistanceTo(y.G1) + Mathf.Abs(y.TTo - y.TFrom) * y.T0.DistanceTo(y.T1);
            y.JobLength = y.Phase switch
            {
                0 => y.Lifting ? 1.2f + dist / 1.1f : 2f,
                1 or 3 => y.Lifting ? 1.8f : 0f,
                _ => y.Lifting ? 1.4f : 0f,
            };
        }
        var f = y.JobLength > 0f ? Mathf.Clamp(y.Job / y.JobLength, 0f, 1f) : 1f;
        if (y.Phase == 0)
        {
            y.Gp = Mathf.Lerp(y.GFrom, y.GTo, Ease(Mathf.Clamp(f * 1.25f, 0f, 1f)));
            y.Tp = Mathf.Lerp(y.TFrom, y.TTo, Ease(Mathf.Clamp(f * 1.25f - 0.25f, 0f, 1f)));
        }
        y.Hp = !y.Lifting ? 0f : y.Phase switch { 1 => Ease(f), 2 => 1f, 3 => 1f - Ease(f), _ => 0f };
        if (y.Gantry != null) y.Gantry.Position = y.G0.Lerp(y.G1, y.Gp);
        if (y.Trolley != null) y.Trolley.Position = y.T0.Lerp(y.T1, y.Tp);
        if (y.Hook != null)
        {
            y.Hook.Position = y.HookUp.Lerp(y.HookDown, y.Hp);
            if (y.Cable != null) y.Cable.Scale = new Vector3(1f, y.CableRest + (y.HookUp.Y - y.Hook.Position.Y), 1f);
        }

        // the fill gauge
        var lit = Mathf.CeilToInt(share * y.Fill.Count - 0.001f);
        var full = share >= 0.98f;
        var blink = Mathf.PosMod(_time, 0.8f) < 0.4f;
        for (var k = 0; k < y.Fill.Count; k++)
        {
            var m = y.Fill[k];
            var top = k >= y.Fill.Count - 1;
            var colour = full ? new Color(1f, 0.15f, 0.08f) : top ? new Color(1f, 0.2f, 0.1f) : k >= y.Fill.Count - 3 ? new Color(1f, 0.65f, 0.15f) : new Color(0.3f, 1f, 0.45f);
            m.Emission = colour;
            m.AlbedoColor = colour;
            m.EmissionEnergyMultiplier = k < lit ? (full && !blink ? 0.6f : 4f) : 0.05f;
        }
        var lamps = LampLevel(s.Broken, true, false, s.Id);
        y.Glow.Set(lamps, _time + s.Id * 0.37f);
        if (y.PadLight != null) y.PadLight.LightEnergy = 0.6f * lamps;
    }

    /// <summary>Where a cargo pod heading for this tile sets down on a storage yard's pad (finished yard only).</summary>
    private Vector3? YardPadAt(int tile)
    {
        foreach (var s in _surface.Structures)
        {
            if (s.Tile != tile || s.DefId != "storage_container" || s.State != StructureState.Complete) continue;
            if (_yards.TryGetValue(s.Id, out var y) && y.Pad != null && y.Pad.IsInsideTree() && _structures.TryGetValue(s.Id, out var v) && y.Model == v.Model)
                return y.Pad.GlobalPosition;
        }
        return null;
    }

    // ---------------------------------------------------------------- reactor

    private sealed class ReactorVisual
    {
        public required Node3D Model;
        public readonly List<Node3D> Rings = new(), Fans = new();
        public required Glows Glow;
        public OmniLight3D? Core;
        public CpuParticles3D? Vent0, Vent1, Sparks;
        public float Run, Heat;
    }

    private readonly Dictionary<int, ReactorVisual> _reactors = new();

    private static ReactorVisual ReactorParts(Node3D model)
    {
        var r = new ReactorVisual
        {
            Model = model,
            Glow = new Glows(model, "CoreGlow", "Plasma", "Status"),
            Core = model.FindChild("CoreOmni", true, false) as OmniLight3D,
        };
        for (var k = 0; k < 4; k++)
        {
            if (Part(model, $"Ring{k}") is { } ring) r.Rings.Add(ring);
            if (Part(model, $"Fan{k}") is { } fan) r.Fans.Add(fan);
        }
        r.Vent0 = PuffsAt(model, "Vent0", "Steam", new Color(0.66f, 0.67f, 0.68f), 22, 3.6f, 1.0f, 2.4f, 0.35f);
        r.Vent1 = PuffsAt(model, "Vent1", "Steam", new Color(0.66f, 0.67f, 0.68f), 22, 3.6f, 1.0f, 2.4f, 0.35f);
        if (Part(model, "SparkPoint") is { } sp) r.Sparks = Sparks(sp);
        return r;
    }

    /// <summary>Working: the plasma glows and pulses with the load (the base's power use ÷ this base's supply), the
    /// constrictor rings spin (alternate ways, faster under load), the tower fans turn and steam rises. Little or no
    /// power use: a low idle glow, slow rings, no steam. No upkeep (out of deuterium): the plasma dies down to a
    /// faint glow, rings spin down. Broken: the core goes dark and flickers, sparks at the converter, the status
    /// board turns red.</summary>
    private void AnimateReactor(SurfaceStructure s, ReactorVisual r, float delta)
    {
        var broken = s.Broken;
        var working = !broken && s.Status is StructureStatus.Working or StructureStatus.LowPower;
        var power = _surface.PowerOf(s.BaseId);
        var load = power.Supply > Fix64.Zero ? Mathf.Clamp(power.Use.ToFloat() / power.Supply.ToFloat(), 0f, 1f) : 0f;
        var target = working ? 0.3f + 0.7f * load : 0f;
        r.Run = Mathf.MoveToward(r.Run, target, delta / 2.5f);
        r.Heat = Mathf.MoveToward(r.Heat, working ? 0.35f + 0.65f * load : broken ? 0f : 0.06f, delta / (working ? 2f : 5f));

        for (var i = 0; i < r.Rings.Count; i++)
            r.Rings[i].RotateObjectLocal(Vector3.Up, delta * (i % 2 == 0 ? 1f : -1.3f) * (0.6f + 5f * r.Run));
        foreach (var fan in r.Fans) fan.RotateObjectLocal(Vector3.Up, delta * 7f * Mathf.Min(1f, r.Run * 1.4f));

        // the core: a slow breathing pulse plus a faster shimmer, quicker under load; broken = dark with dying flickers
        var pulse = 1f + (0.1f + 0.1f * load) * Mathf.Sin(_time * (1.6f + 2.4f * load)) + 0.05f * Mathf.Sin(_time * 9.3f + 0.7f);
        if (broken) pulse = Mathf.Sin(_time * 23f) * Mathf.Sin(_time * 3.1f) > 0.6f ? 3f : 0.2f;
        var glow = Mathf.Lerp(0.15f, 6f, r.Heat) * pulse;
        if (broken) glow = 0.12f * pulse;
        foreach (var m in r.Glow["CoreGlow"])
        {
            m.EmissionEnabled = true;
            m.EmissionEnergyMultiplier = glow;
        }
        // plasma conduits: brightness with the load, a travelling throb
        var throb = 0.85f + 0.15f * Mathf.Sin(_time * 5f);
        foreach (var m in r.Glow["Plasma"]) m.EmissionEnergyMultiplier = broken ? 0.1f : Mathf.Lerp(0.2f, 4.5f, r.Heat) * throb;
        if (r.Core != null) r.Core.LightEnergy = (broken ? 0.15f * pulse : 2.2f * r.Heat * pulse);

        // the status board: green running, amber idle / no deuterium, red broken
        var status = broken ? new Color(1f, 0.15f, 0.08f) : working ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.65f, 0.15f);
        foreach (var m in r.Glow["Status"])
        {
            m.Emission = status;
            m.AlbedoColor = status;
            m.EmissionEnergyMultiplier = broken && Mathf.PosMod(_time, 0.6f) < 0.3f ? 0.4f : 3f;
        }
        r.Glow.Set(LampLevel(broken, working, false, s.Id), _time + s.Id * 0.37f);
        Emit(r.Vent0, r.Run > 0.35f);
        Emit(r.Vent1, r.Run > 0.6f);
        Emit(r.Sparks, broken);
    }
}
