using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The first landing, drawn (render-only): three drop ships bring the Level 1 outpost down in three modules, set them
/// down round the landing tile, land beside it and unload the rovers and the crawler; the modules crawl together onto
/// the tile centre, lock into the round hub and unfurl (owner 2026-11-05: "come down in 3 drop ships and then
/// unfurl/combine into the level1 outpost"). The sim knows the landing tile, LandsAtTick (touchdown: the Colony Hub
/// appears as <see cref="StructureState.Assembling"/> and the units roll out) and <see cref="SurfacePlayer.UplinkTick"/>
/// (touchdown + landing.assemble_seconds = the hub's <see cref="SurfaceStructure.ReadyTick"/>, when it turns Complete and
/// starts working); this view hides the real hub model until then and plays the assembly on its own copy of the same model
/// (<see cref="BuildingModels.Build"/>, outpost_l1.glb), whose final pose is the glb's rest pose, so the swap is seamless.
/// Outpost drops from orbit (<see cref="DeliveryKind.Outpost"/>) play the same sequence with empty ships that fly
/// straight back up (if the site was taken, they take their modules back up); their core is ready at ArriveTick +
/// assemble_seconds, the same end this view uses.
/// <para>Timeline in seconds from touchdown T (ship times fixed; assembly times are for assemble_seconds 20 and scale
/// with it):</para>
/// <list type="bullet">
/// <item>ships k = 0, 1, 2 come down like rocket landers (<see cref="RocketDescent"/>: a near-ballistic fall from 260 up
/// and 140 out, a full-throttle deceleration burn, straight down for the last ~60, a brief hover correction;
/// staggered 0.35 s, approach headings 11° apart) from -descent_seconds to a rocket hover at -1.6 + 0.45k (throttle
/// ~0.66, flickering, a slight bob and drift), the module hanging on four cables 3 above the ground, nose outward
/// over its set-down spot (module origin 8.5 out along its wedge's centre line);</item>
/// <item>the cables pay out for 1.6 s: module k touches down at 0.45k (fine dust), the clamps let go, the cables reel
/// in; the ship lifts at +0.6 and hops (4–7 s: pops up 8 on a strong burn, leans over into the hop and back to brake,
/// drops and brakes hard onto its spot) to its landing spot beside the tile the sim gave its unit; from there
/// <see cref="DropShipView"/> unloads it (ramp, drive-off, climb out). Empty ships climb straight up and pitch away
/// (<see cref="RocketClimb"/>);</item>
/// <item>modules crawl in on their glowing skids from set-down + 1.6 to 8.2, lock at 8.7 (seams flash);</item>
/// <item>unfurl 9.4 → 15.2 (<see cref="Folds"/>: mast up, telescopes, dish opens; solar wings out, outer leaves open;
/// radiators up; floodlight arm up); the repair pad rolls out 10.5 → 14.5;</item>
/// <item>lights on 16 → 17.6 (windows module by module with a flicker, hub light, floodlight);</item>
/// <item>comms on 18.6 → 20: dish feed and mast ring glow, the red beacon starts flashing, an uplink beam goes up
/// 19 → 20 and fades exactly at UplinkTick, when the copy is freed and the real hub shows.</item>
/// </list>
/// </summary>
public partial class LandingView : Node3D
{
    // ---------------------------------------------------------------- timeline (seconds from touchdown)

    private const float HoverAt = -1.6f, HoverStagger = 0.45f, StartStagger = 0.35f;
    private const float LowerSeconds = 1.6f, LowerBy = 3f;  // cables pay out this long, the module drops this far
    private const float LiftAfter = 0.6f;                   // after its module touches down, the ship lifts
    private const float CablesIn = 0.9f;
    private const float CrawlAfter = 1.6f;
    private const float StartHeight = 260f, ApproachDistance = 140f;
    private const float FlyAwaySeconds = 7.5f;

    // Assembly, for assemble_seconds = 20.
    private const float DockAt = 8.2f, LockSeconds = 0.5f;
    private const float PadFrom = 10.5f, PadTo = 14.5f;
    private const float LightsAt = 16f, LightsStagger = 0.5f, FlickerSeconds = 0.7f, FloodAt = 17.6f;
    private const float CommsAt = 18.6f, BeamFrom = 19f;
    private const float Reference = 20f;

    /// <summary>One unfurling part: from/to (assembly seconds, scaled), and how far from its rest pose it starts:
    /// a turn about its local X, a slide along its local Y and a scale (build_outpost_l1.py STOWED, same signs).</summary>
    private readonly record struct Fold(string Part, float From, float To, float Degrees, float Slide, Vector3 Scale);

    private static readonly Fold[] Folds =
    {
        new("Mast", 9.4f, 12f, 90f, 0f, Vector3.One),
        new("MastTop", 12f, 13.6f, 0f, -2.8f, Vector3.One),
        new("Dish", 13.4f, 15.2f, -40f, 0f, new Vector3(0.28f, 1f, 0.28f)),
        new("SolarB0", 10f, 12.4f, -100f, 0f, Vector3.One),
        new("SolarB1", 10.3f, 12.7f, -100f, 0f, Vector3.One),
        new("SolarB0Outer", 12.2f, 14.2f, 180f, 0f, Vector3.One),
        new("SolarB1Outer", 12.5f, 14.5f, 180f, 0f, Vector3.One),
        new("RadiatorC0", 10.6f, 12.8f, -170f, 0f, Vector3.One),
        new("RadiatorC1", 10.9f, 13.1f, -170f, 0f, Vector3.One),
        new("Flood", 12.4f, 14f, 90f, 0f, Vector3.One),
    };

    // ---------------------------------------------------------------- the modules (hub frame: front +Z, up +Y)

    private const float SetDistance = 8.5f;  // module origin (the wedge's apex) from the hub centre when set down
    private const float CentroidOut = 2.6f;  // the wedge's centroid from its apex
    private const float CarryDrop = 4f;      // module base below the ship's origin while carried (top just under the belly)
    private const float CarryAft = 0.3f;     // the centroid hangs this far aft of the ship's origin
    private const float BellyY = 0.85f;      // build_dropship.py BELLY
    private const float HubClear = 15.5f;    // landing ships keep their footprint this far from the hub centre

    /// <summary>The convoy ships' names per player (render-only), three each.</summary>
    private static readonly (string Name, string Number)[] ConvoyHulls =
    {
        ("ANCHOR", "03"), ("KEDGE", "05"), ("FATHOM", "09"), ("SHACKLE", "14"), ("BOLLARD", "16"), ("HAWSER", "19"),
        ("CLEAT", "22"), ("WINDLASS", "27"), ("BOWSPRIT", "33"),
    };

    private static readonly string[] GlowNames = { "Window", "Seam", "Skid", "Beacon", "Comms", "Lamp" };

    private readonly PlanetView _view = null!;
    private readonly Simulation _sim = null!;
    private readonly PlanetSurface _surface = null!;
    private readonly DropShipView _ships = null!;
    private readonly int _localPlayer;
    private readonly Dictionary<int, Convoy> _convoys = new();   // key: -(player + 1) for a landing, delivery id for a drop
    private readonly HashSet<int> _finished = new();
    private readonly List<int> _done = new();

    private static CylinderMesh? _cableMesh;
    private static StandardMaterial3D? _cableMaterial;

    private sealed class Glow
    {
        public required BaseMaterial3D Material;
        public required float Energy;
    }

    private sealed class Slot
    {
        public required int Index;
        public required Node3D Module;
        public required Transform3D RestHub;      // the module node's rest pose in the hub frame
        public required Transform3D ParentHub;    // its parent's transform in the hub frame
        public required Vector3 Dir;              // hub frame, horizontal
        public required Transform3D Carry;        // module (rest) in the ship's frame while carried, cables not paid out
        public required Vector3 Back;             // where its ship comes in from (world, horizontal)
        public required float SetGround;          // ground height under the set-down spot
        public float Start, Hover, Set, Lift, Touch, CrawlFrom;
        public RocketDescent Descent;             // the way down from Start to the hover
        public Transform3D HoverPose;             // the ship's
        public Transform3D Landed;                // the ship's landing spot (Lands only)
        public bool Lands;
        public int UnitIndex = -1;
        public int ShipKey;
        public long ArriveTick;
        public readonly Dictionary<string, Glow> Glows = new();
        public Vector3[] HardPoints = System.Array.Empty<Vector3>(); // ship frame
        public Vector3[] Attach = System.Array.Empty<Vector3>();     // hub frame at rest
        public float[] CableRest = System.Array.Empty<float>();
        public MeshInstance3D[] Cables = System.Array.Empty<MeshInstance3D>();
        public CpuParticles3D Puff = null!, Trail = null!;
        public bool Puffed;
    }

    private sealed class Convoy
    {
        public required int Key;
        public required int Owner;
        public required int Tile;
        public required long TouchTick;
        public required float End;
        public required float Scale;              // assemble_seconds / 20
        public required bool FirstLanding;
        public required Node3D Model;
        public required Transform3D Hub;
        public required Slot[] Slots;
        public required float ShowFrom;           // the first ship's Start
        public readonly List<(Node3D Node, Transform3D Home, Fold Fold)> Parts = new();
        public Node3D? Pad;
        public Transform3D PadRest;
        public Node3D? Dish, Beacon;
        public Light3D? HubLight, FloodLight, BeaconLight;
        public float HubEnergy, FloodEnergy, BeaconEnergy;
        public MeshInstance3D Beam = null!;
        public StandardMaterial3D BeamMaterial = null!;
        public int[]? Units;                      // landed unit ids by data order (first landing), -1 = none
        public bool Aborted;
        public float AbortedAt;
        public float Time;
    }

    public LandingView(PlanetView view, Simulation sim, int localPlayer, DropShipView ships)
    {
        _view = view;
        _sim = sim;
        _surface = sim.Surface!;
        _localPlayer = localPlayer;
        _ships = ships;
    }

    // Parameterless constructor for Godot's script machinery; such an instance draws nothing.
    public LandingView() { }

    // ---------------------------------------------------------------- per frame

    /// <summary>Start a convoy for every landing or outpost drop under way, move them along, free finished ones. Call
    /// before <see cref="DropShipView.Update"/> and before the structures are drawn (<see cref="Hides"/>).</summary>
    public void Update(float alpha, float delta)
    {
        if (_surface == null) return;
        var assemble = (float)_sim.Data.Landing.AssembleSeconds;
        for (var p = 0; p < _surface.Players.Count; p++)
        {
            var me = _surface.Players[p];
            var key = -(p + 1);
            if (_convoys.ContainsKey(key) || _finished.Contains(key)) continue;
            if (me.Phase == LandingPhase.Descending
                || (me.Phase == LandingPhase.Landed && me.UplinkTick > 0 && _sim.Tick < me.UplinkTick))
            {
                var end = me.Phase == LandingPhase.Landed ? (me.UplinkTick - me.LandsAtTick) / (float)Simulation.TickRate : assemble;
                var descent = (me.LandsAtTick - me.DescentStartTick) / (float)Simulation.TickRate;
                Start(key, p, me.LandingTile, me.LandsAtTick, descent, end, _sim.Data.Landing.HubBuilding, true);
            }
        }
        foreach (var d in _surface.Deliveries)
            if (d.Kind == DeliveryKind.Outpost && !_convoys.ContainsKey(d.Id) && !_finished.Contains(d.Id))
                Start(d.Id, d.Owner, d.Tile, d.ArriveTick, (d.ArriveTick - d.LaunchTick) / (float)Simulation.TickRate, assemble, d.Payload, false);

        var done = _done;
        done.Clear();
        foreach (var c in _convoys.Values)
        {
            c.Time = (_sim.Tick - c.TouchTick + alpha) / Simulation.TickRate;
            if (c.Time >= c.End || Gone(c))
            {
                done.Add(c.Key);
                continue;
            }
            Animate(c, delta);
        }
        foreach (var key in done) Finish(key);
    }

    /// <summary>True while the landing animation stands in for this structure (the real model is hidden).</summary>
    public bool Hides(SurfaceStructure s)
    {
        foreach (var c in _convoys.Values)
            if (c.Tile == s.Tile && c.Owner == s.Owner && !c.Aborted && c.Time < c.End) return true;
        return false;
    }

    /// <summary>The hub it builds has gone (test panel), or an outpost drop found its site taken.</summary>
    private bool Gone(Convoy c)
    {
        if (c.Time < 0.05f) return false;
        var s = _surface.StructureAt(c.Tile);
        var there = s != null && s.Owner == c.Owner && _sim.Data.Building(s.DefId).IsCore;
        if (there) return false;
        if (c.FirstLanding) return true;
        if (!c.Aborted)
        {
            c.Aborted = true;     // the ships take their modules back up and the convoy ends when they're gone
            c.AbortedAt = c.Time;
        }
        return c.Time > c.AbortedAt + FlyAwaySeconds;
    }

    private void Finish(int key)
    {
        var c = _convoys[key];
        c.Model.QueueFree();
        c.Beam.QueueFree();
        foreach (var s in c.Slots)
        {
            foreach (var cable in s.Cables) cable.QueueFree();
            s.Puff.QueueFree();
            s.Trail.QueueFree();
        }
        _convoys.Remove(key);
        _finished.Add(key);
    }

    // ---------------------------------------------------------------- setting up

    private void Start(int key, int owner, int tile, long touchTick, float descent, float end, string hubId, bool firstLanding)
    {
        var now = (_sim.Tick - touchTick) / (float)Simulation.TickRate;
        if (now >= end || tile < 0) return;
        var colour = PlanetDriver.ColorOf(owner);
        var model = BuildingModels.Build(hubId, colour);
        var modules = new Node3D?[3];
        for (var k = 0; k < 3; k++) modules[k] = model.FindChild(BuildingModels.OutpostModules[k], true, false) as Node3D;
        if (modules.Any(m => m == null))
        {
            model.Free(); // not the modular outpost: leave the hub to appear as it always did
            _finished.Add(key);
            return;
        }
        var centre = _view.TileCentre(tile);
        var hub = new Transform3D(Basis.Identity, centre); // landings and drops are placed with facing 0
        model.Transform = hub;
        model.Visible = false;
        AddChild(model);

        var scale = Mathf.Max(0.25f, end / Reference);
        var slots = new Slot[3];
        var heading = 0.9f + 1.3f * owner + (firstLanding ? 0f : 0.7f * key);
        var haze = SprayRig.HazeColour(SprayRig.KindFor(_surface.Map, tile));
        haze.A = Mathf.Max(haze.A, 0.45f);
        for (var k = 0; k < 3; k++)
        {
            var module = modules[k]!;
            var dir = BuildingModels.OutpostModuleDirs[k];
            var parentHub = ModelSpace(module.GetParent() as Node3D, model);
            var restHub = parentHub * module.Transform;
            // In the ship's frame the module's centre line points forward (-Z), its centroid hangs under the ship.
            var yaw = Mathf.Pi - Mathf.Atan2(dir.X, dir.Z);
            var turn = new Basis(Vector3.Up, yaw);
            var carry = new Transform3D(turn, new Vector3(0, -CarryDrop, CarryAft) - turn * (dir * CentroidOut)) * restHub;
            var a = heading + (k - 1) * 0.2f;
            var groundAt = hub * (dir * (SetDistance + CentroidOut));
            var slot = new Slot
            {
                Index = k,
                Module = module,
                RestHub = restHub,
                ParentHub = parentHub,
                Dir = dir,
                Carry = carry,
                Back = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)),
                SetGround = _view.GroundAt(groundAt),
            };
            slot.Hover = HoverAt + HoverStagger * k;
            slot.Start = Mathf.Min(slot.Hover - 2.5f, -descent + StartStagger * k);
            slot.Set = slot.Hover + LowerSeconds;
            slot.Descent = new RocketDescent(slot.Hover - slot.Start, StartHeight, ApproachDistance);
            slot.Lift = slot.Set + LiftAfter;
            slot.CrawlFrom = Mathf.Min(slot.Set + CrawlAfter, DockAt * scale - 0.5f);
            // The ship hovers so the module's base is LowerBy above the ground at its set-down spot.
            var setDown = SetPose(hub, slot, SetDistance, slot.SetGround + LowerBy);
            slot.HoverPose = setDown * carry.AffineInverse(); // module = ship * carry
            Cables(slot);
            slot.Puff = DropShipView.Downwash(haze, burst: true);
            slot.Trail = DropShipView.Downwash(haze, burst: false);
            slot.Trail.Amount = 120;
            AddChild(slot.Puff);
            AddChild(slot.Trail);
            slot.Puffed = now > slot.Set + 0.5f;
            Collect(module, slot);
            slots[k] = slot;
        }

        var c = new Convoy
        {
            Key = key,
            Owner = owner,
            Tile = tile,
            TouchTick = touchTick,
            End = end,
            Scale = scale,
            FirstLanding = firstLanding,
            Model = model,
            Hub = hub,
            Slots = slots,
            ShowFrom = slots.Min(s => s.Start),
        };
        foreach (var f in Folds)
            if (model.FindChild(f.Part, true, false) is Node3D node)
                c.Parts.Add((node, node.Transform, f));
        if (model.FindChild("RepairPad", true, false) is Node3D pad)
        {
            c.Pad = pad;
            c.PadRest = pad.Transform;
        }
        c.Dish = model.FindChild("Dish", true, false) as Node3D;
        c.Beacon = model.FindChild("Beacon", true, false) as Node3D;
        c.HubLight = model.FindChild("HubLight", true, false) as Light3D;
        c.FloodLight = model.FindChild("FloodLight", true, false) as Light3D;
        c.BeaconLight = model.FindChild("BeaconLight", true, false) as Light3D;
        c.HubEnergy = c.HubLight?.LightEnergy ?? 0f;
        c.FloodEnergy = c.FloodLight?.LightEnergy ?? 0f;
        c.BeaconEnergy = c.BeaconLight?.LightEnergy ?? 0f;
        c.BeamMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(0.5f, 0.85f, 1f, 0f),
        };
        c.Beam = new MeshInstance3D
        {
            Name = "UplinkBeam",
            Mesh = new CylinderMesh { TopRadius = 1.8f, BottomRadius = 0.25f, Height = 160f, RadialSegments = 12, Rings = 1 },
            MaterialOverride = c.BeamMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(c.Beam);

        // Which ship carries which landed unit: the sim puts unit i on the i-th free passable neighbour (in neighbour
        // order) at touchdown; each goes in the ship whose module sets down nearest that side.
        var others = new List<Vector3>();
        if (firstLanding)
        {
            var spots = new List<int>();
            foreach (var n in _surface.Map.Grid.Neighbours(tile))
                if (_surface.Map.Terrain(n).Passable && _surface.StructureAt(n) == null) spots.Add(n);
            var units = _sim.Data.Landing.Units;
            for (var i = 0; i < units.Count && i < 3 && spots.Count > 0; i++)
            {
                var unitTile = spots[i % spots.Count];
                var toward = (_view.TileCentre(unitTile) - centre) with { Y = 0 };
                toward = toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Back;
                Slot? best = null;
                foreach (var s in slots)
                    if (s.UnitIndex < 0 && (best == null || s.Dir.Dot(toward) > best.Dir.Dot(toward))) best = s;
                if (best == null) break;
                best.UnitIndex = i;
                best.Lands = true;
                var crawler = _sim.Data.SurfaceUnit(units[i]).CanMine;
                best.Landed = _ships.ConvoyLanding(unitTile, key * 3 + best.Index, crawler, tile, HubClear, others);
                others.Add(best.Landed.Origin);
            }
        }

        _convoys[key] = c;
        for (var k = 0; k < 3; k++)
        {
            var s = slots[k];
            s.ShipKey = -(1000 + (firstLanding ? -key : 100000 + key) * 4 + k);
            if (s.Lands)
            {
                var hop = Mathf.Clamp(2.4f + Flat(s.Landed.Origin - s.HoverPose.Origin).Length() / 9f, 4f, 7f);
                s.ArriveTick = touchTick + (long)Mathf.Ceil((s.Lift + hop + 2f) * Simulation.TickRate);
                s.Touch = (s.ArriveTick - touchTick) / (float)Simulation.TickRate - 2f;
            }
            else s.ArriveTick = touchTick;
            var offset = (s.ArriveTick - touchTick) / (float)Simulation.TickRate;
            var slot = s;
            var hull = ConvoyHulls[(owner * 3 + k) % ConvoyHulls.Length];
            var crawlerShip = s.Lands && _sim.Data.SurfaceUnit(_sim.Data.Landing.Units[s.UnitIndex]).CanMine;
            _ships.AddConvoyShip(new DropShipView.ConvoyShip
            {
                Key = s.ShipKey,
                Owner = owner,
                ArriveTick = s.ArriveTick,
                ShowFrom = s.Start - offset,
                Landed = s.Lands ? s.Landed : s.HoverPose,
                Crawler = crawlerShip,
                Name = hull.Name,
                Number = hull.Number,
                Approach = t => ShipAt(c, slot, t + offset),
                Resolve = () => UnitFor(c, slot),
                FliesAway = !s.Lands,
                GoneAt = s.Lift + FlyAwaySeconds - offset,
            });
        }
    }

    /// <summary>The module's pose (world) with its origin <paramref name="distance"/> out along its centre line and
    /// its base at height <paramref name="y"/>.</summary>
    private static Transform3D SetPose(Transform3D hub, Slot s, float distance, float y)
    {
        var at = hub * (s.Dir * distance);
        return new Transform3D(hub.Basis, at with { Y = y }) * s.RestHub;
    }

    /// <summary>Four cables from hard points under the ship's belly straight down to the module's roof (two on the
    /// deck ring, two on the dome), measured in the carry pose.</summary>
    private void Cables(Slot s)
    {
        _cableMesh ??= new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 1f, RadialSegments = 6, Rings = 1 };
        _cableMaterial ??= new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.11f), Metallic = 0.6f, Roughness = 0.5f };
        var side = Vector3.Up.Cross(s.Dir).Normalized();
        s.Attach = new[]
        {
            s.Dir * 3.9f + side * 1.3f + Vector3.Up * 2.8f, s.Dir * 3.9f - side * 1.3f + Vector3.Up * 2.8f,
            s.Dir * 1.8f + side * 0.9f + Vector3.Up * 4.12f, s.Dir * 1.8f - side * 0.9f + Vector3.Up * 4.12f,
        };
        var restInv = s.RestHub.AffineInverse();
        s.HardPoints = new Vector3[4];
        s.CableRest = new float[4];
        s.Cables = new MeshInstance3D[4];
        for (var i = 0; i < 4; i++)
        {
            var p = s.Carry * (restInv * s.Attach[i]);
            s.HardPoints[i] = new Vector3(p.X, BellyY, p.Z);
            s.CableRest[i] = Mathf.Max(0.2f, BellyY - p.Y);
            s.Cables[i] = new MeshInstance3D { Mesh = _cableMesh, MaterialOverride = _cableMaterial, Visible = false };
            AddChild(s.Cables[i]);
        }
    }

    /// <summary>This module's own copies of its glowing materials (so modules light up one by one, and no other
    /// outpost changes with them).</summary>
    private static void Collect(Node node, Slot s)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            if (mi.MaterialOverride is BaseMaterial3D o)
            {
                if (GlowNames.Contains(o.ResourceName)) mi.MaterialOverride = Own(s, o);
            }
            else
                for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                {
                    var m = mi.GetSurfaceOverrideMaterial(i) as BaseMaterial3D ?? mi.Mesh.SurfaceGetMaterial(i) as BaseMaterial3D;
                    if (m != null && GlowNames.Contains(m.ResourceName)) mi.SetSurfaceOverrideMaterial(i, Own(s, m));
                }
        }
        foreach (var child in node.GetChildren()) Collect(child, s);
    }

    private static BaseMaterial3D Own(Slot s, BaseMaterial3D m)
    {
        if (s.Glows.TryGetValue(m.ResourceName, out var g)) return g.Material;
        var copy = (BaseMaterial3D)m.Duplicate();
        s.Glows[m.ResourceName] = new Glow { Material = copy, Energy = m.EmissionEnergyMultiplier };
        return copy;
    }

    private int UnitFor(Convoy c, Slot s)
    {
        if (!s.Lands) return -1;
        if (c.Units == null)
        {
            if (_surface.Players[c.Owner].Phase != LandingPhase.Landed) return 0;
            var want = _sim.Data.Landing.Units.Count;
            var landed = _surface.Units.Where(u => u.Owner == c.Owner && u.DeliveryId == 0 && u.BuiltAtTick < 0)
                .OrderBy(u => u.Id).Take(want).Select(u => u.Id).ToList();
            c.Units = Enumerable.Range(0, want).Select(i => i < landed.Count ? landed[i] : -1).ToArray();
        }
        return s.UnitIndex < c.Units.Length ? c.Units[s.UnitIndex] : -1;
    }

    // ---------------------------------------------------------------- the ships' flight

    /// <summary>Where ship <paramref name="s"/> is at T (seconds from touchdown) until it lands (or forever, for an
    /// empty one): down from orbit to the hover, holding while the module is lowered, then a hop to its landing spot or
    /// a climb back out.</summary>
    private DropShipView.Flight ShipAt(Convoy c, Slot s, float T)
    {
        if (T < s.Hover)
        {
            // A rocket lander's way down (RocketDescent), ending in the hover instead of a touchdown.
            var d = s.Descent;
            var tau = s.Hover - T;
            var side = Vector3.Up.Cross(s.Back).Normalized() * (s.Index % 2 == 0 ? 1f : -1f);
            var slide = d.SideAt(tau, out var roll);
            var pos = s.HoverPose.Origin + Vector3.Up * d.HeightAt(tau) + s.Back * d.ReachAt(tau) + side * slide;
            var travel = new Quaternion(Basis.LookingAt(-s.Back, Vector3.Up));
            var q = new Quaternion(s.HoverPose.Basis.Orthonormalized()).Slerp(travel, d.TurnAt(tau));
            var basis = DropShipView.Lean(s.Back, d.LeanAt(tau)) * DropShipView.Lean(-side, roll) * new Basis(q);
            return new DropShipView.Flight(new Transform3D(basis, pos), Mathf.Min(1f, d.Throttle(tau) + 0.05f), 0f, 0f);
        }
        var climbFrom = c.Aborted ? Mathf.Max(c.AbortedAt + 0.8f, s.Hover) : s.Lift;
        if (T < climbFrom)
        {
            // A rocket hover with a module on the cables: the burn flickers round two thirds, a slight bob and drift
            // that die out as the module is set down.
            var calm = 1f - RocketPlume.S(s.Hover, s.Set, T);
            var bob = Vector3.Up * 0.1f * Mathf.Sin(2.3f * T + s.Index)
                + s.Back * 0.12f * Mathf.Sin(1.3f * T + 2f * s.Index);
            var hold = 0.66f + 0.04f * Mathf.Sin(5.3f * T + s.Index) + 0.025f * Mathf.Sin(11.7f * T);
            return new DropShipView.Flight(new Transform3D(s.HoverPose.Basis, s.HoverPose.Origin + bob * calm), hold, 0f, 0f);
        }
        if (!s.Lands || c.Aborted)
        {
            // Straight up on a growing burn, accelerating, then it pitches over toward its nose and away.
            var k = T - climbFrom;
            var forward = (-s.HoverPose.Basis.Z) with { Y = 0 };
            forward = forward.LengthSquared() > 1e-6f ? forward.Normalized() : Vector3.Forward;
            var pos = s.HoverPose.Origin + Vector3.Up * RocketClimb.Up(k) + forward * RocketClimb.Ahead(k);
            var basis = s.HoverPose.Basis * new Basis(Vector3.Right, -Mathf.DegToRad(RocketClimb.Pitch(k)));
            return new DropShipView.Flight(new Transform3D(basis, pos), Mathf.Max(0.66f, RocketClimb.Throttle(k)), RocketClimb.Aft(k), 0f);
        }
        // Hop: pop up to a low cruise on a strong burn, lean into the hop and back to brake it, then drop and brake
        // hard onto the landing spot (straight down for the last part) with the legs coming out.
        var u = Mathf.Clamp((T - s.Lift) / Mathf.Max(0.1f, s.Touch - s.Lift), 0f, 1f);
        var from = s.HoverPose.Origin;
        var to = s.Landed.Origin;
        var cruise = Mathf.Max(from.Y, to.Y) + 8f;
        var x = (u - 0.15f) / 0.6f;
        var across = from.Lerp(to, RocketPlume.S(0.15f, 0.75f, u));
        float y;
        if (u < 0.35f) y = Mathf.Lerp(from.Y, cruise, RocketPlume.S(0f, 0.35f, u));
        else if (u < 0.55f) y = cruise;
        else y = Mathf.Lerp(cruise, to.Y, RocketPlume.S(0.55f, 1f, u));
        var lean = x > 0f && x < 1f ? 12f * Mathf.Sin(Mathf.Tau * x) : 0f; // into the hop, then back against it
        var turn = new Quaternion(s.HoverPose.Basis.Orthonormalized()).Slerp(new Quaternion(s.Landed.Basis.Orthonormalized()), RocketPlume.S(0.1f, 0.75f, u));
        var hopBasis = DropShipView.Lean(to - from, lean) * new Basis(turn);
        var throttle = 0.9f;
        throttle = Mathf.Lerp(throttle, 0.6f, RocketPlume.S(0.25f, 0.35f, u));   // cruising across
        throttle = Mathf.Lerp(throttle, 0.35f, RocketPlume.S(0.55f, 0.62f, u));  // dropping
        throttle = Mathf.Lerp(throttle, 0.95f, RocketPlume.S(0.72f, 0.78f, u));  // the braking burn
        throttle = Mathf.Lerp(throttle, 0.55f, RocketPlume.S(0.92f, 0.98f, u));  // settle
        return new DropShipView.Flight(new Transform3D(hopBasis, across with { Y = y }), throttle, 0f,
            RocketPlume.S(0.6f, 0.92f, u));
    }

    // ---------------------------------------------------------------- the assembly

    private void Animate(Convoy c, float delta)
    {
        var T = c.Time;
        var S = c.Scale;
        var shown = T >= c.ShowFrom && (c.Owner == _localPlayer || _view.Sees(c.Tile));
        c.Model.Visible = shown;
        var hubY = c.Hub.Origin.Y;
        var dock = DockAt * S;
        var locked = dock + LockSeconds;

        foreach (var s in c.Slots)
        {
            Transform3D world;
            var carried = T < s.Set || c.Aborted;
            if (carried)
            {
                var ship = ShipAt(c, s, T).Pose;
                var paid = LowerBy * Mathf.SmoothStep(s.Hover, s.Set, Mathf.Min(T, c.Aborted ? c.AbortedAt : T));
                if (c.Aborted) paid *= 1f - Mathf.SmoothStep(c.AbortedAt, c.AbortedAt + 0.8f, T); // reel it back in
                world = ship * Transform3D.Identity.Translated(Vector3.Down * paid) * s.Carry;
                for (var i = 0; i < 4; i++)
                {
                    var top = ship * s.HardPoints[i];
                    var bottom = world * (s.RestHub.AffineInverse() * s.Attach[i]);
                    Cable(s.Cables[i], top, bottom, shown);
                }
            }
            else
            {
                // Crawl in from the set-down spot, then the last 0.3 into the lock.
                var d = SetDistance;
                if (T >= s.CrawlFrom) d = Mathf.Lerp(SetDistance, 0.3f, Mathf.SmoothStep(s.CrawlFrom, dock, T));
                if (T >= dock) d = 0.3f * (1f - Mathf.Clamp((T - dock) / LockSeconds, 0f, 1f) * Mathf.Clamp((T - dock) / LockSeconds, 0f, 1f));
                var y = Mathf.Lerp(s.SetGround, hubY, 1f - d / SetDistance);
                world = SetPose(c.Hub, s, d, y);
                // The cables come loose and reel up into the ship.
                var reel = (T - s.Set) / CablesIn;
                var ship = reel < 1f ? ShipAt(c, s, T).Pose : Transform3D.Identity;
                for (var i = 0; i < 4; i++)
                {
                    if (reel >= 1f)
                    {
                        if (s.Cables[i].Visible) s.Cables[i].Visible = false;
                        continue;
                    }
                    var top = ship * s.HardPoints[i];
                    var length = Mathf.Lerp(s.CableRest[i] + LowerBy, 0.3f, Mathf.SmoothStep(0f, 1f, reel));
                    Cable(s.Cables[i], top, top + Vector3.Down * length, shown);
                }
                if (!s.Puffed)
                {
                    s.Puffed = true;
                    if (shown)
                    {
                        s.Puff.Position = (c.Hub * (s.Dir * (SetDistance + CentroidOut))) with { Y = s.SetGround + 0.3f };
                        s.Puff.Restart();
                    }
                }
            }
            s.Module.Transform = (c.Hub * s.ParentHub).AffineInverse() * world;

            var crawling = !carried && T >= s.CrawlFrom && T < dock;
            var centroid = world * (s.RestHub.AffineInverse() * (s.Dir * CentroidOut));
            s.Trail.Position = centroid with { Y = _view.GroundAt(centroid) + 0.3f };
            var trail = shown && crawling;
            if (s.Trail.Emitting != trail) s.Trail.Emitting = trail;

            // Glows: skids lit while it crawls, seams flash as it locks, windows come on one module at a time.
            var skid = carried ? 0.4f : crawling ? 4f : 1f;
            Set(s, "Skid", skid);
            Set(s, "Seam", T < locked ? 0.4f : 1f + 6f * Mathf.Exp(-3f * (T - locked)));
            var on = (LightsAt + LightsStagger * s.Index) * S;
            Set(s, "Window", Flicker(T - on));
            var flood = FloodAt * S;
            Set(s, "Lamp", T < flood ? 0.04f : Mathf.Min(1f, 0.04f + (T - flood) / 0.3f));
            var comms = CommsAt * S;
            Set(s, "Comms", T < comms ? 0.1f : T < comms + 0.6f * S ? Mathf.Lerp(0.1f, 2.5f, (T - comms) / (0.6f * S))
                : Mathf.Lerp(2.5f, 1f, Mathf.Clamp((T - comms - 0.6f * S) / Mathf.Max(0.1f, c.End - comms - 0.6f * S), 0f, 1f)));
            Set(s, "Beacon", Beacon(c, T));
        }

        // Unfurl.
        foreach (var (node, rest, f) in c.Parts)
        {
            var k = 1f - Mathf.SmoothStep(f.From * S, f.To * S, T);
            var basis = new Basis(Vector3.Right, Mathf.DegToRad(f.Degrees * k)) * Basis.FromScale(Vector3.One + (f.Scale - Vector3.One) * k);
            node.Transform = rest * new Transform3D(basis, new Vector3(0, f.Slide * k, 0));
        }
        if (c.Pad != null)
        {
            // The pad rolls out from the hub's side: it grows outward from its inner edge (3.7 in from its centre).
            var p = Mathf.SmoothStep(PadFrom * S, PadTo * S, T);
            c.Pad.Visible = p > 0.001f;
            var sx = Mathf.Max(0.02f, p);
            var sy = 0.12f + 0.88f * Mathf.SmoothStep(0.55f, 1f, p);
            c.Pad.Transform = new Transform3D(c.PadRest.Basis * Basis.FromScale(new Vector3(sx, sy, 1f)),
                c.PadRest.Origin - c.PadRest.Basis.X.Normalized() * 3.7f * (1f - sx));
        }

        // Lights.
        if (c.HubLight != null) c.HubLight.LightEnergy = c.HubEnergy * Mathf.SmoothStep(LightsAt * S, (LightsAt + 2f) * S, T);
        if (c.FloodLight != null) c.FloodLight.LightEnergy = c.FloodEnergy * Mathf.Clamp((T - FloodAt * S) / 0.3f, 0f, 1f);
        if (c.BeaconLight != null) c.BeaconLight.LightEnergy = c.BeaconEnergy * Beacon(c, T);

        // The uplink beam: up from the dish (or the beacon) for the last second, gone exactly at UplinkTick.
        var beam = (T - BeamFrom * S) / Mathf.Max(0.1f, c.End - BeamFrom * S);
        var showBeam = shown && beam > 0f && beam < 1f;
        c.Beam.Visible = showBeam;
        if (showBeam && (c.Dish ?? c.Beacon) is { } from && from.IsInsideTree())
        {
            c.Beam.Position = from.GlobalPosition + Vector3.Up * 80f;
            c.BeamMaterial.AlbedoColor = c.BeamMaterial.AlbedoColor with { A = 0.55f * Mathf.Sin(Mathf.Pi * beam) };
        }
    }

    /// <summary>Windows: dim until their time, then a fluorescent flicker for 0.7 s, then on.</summary>
    private static float Flicker(float since)
    {
        if (since < 0f) return 0.06f;
        if (since >= FlickerSeconds) return 1f;
        var step = (int)(since / FlickerSeconds * 7f);
        return step is 0 or 2 or 5 ? 1f : step == 3 ? 0.5f : 0.15f;
    }

    /// <summary>The red beacon: dark until comms come on, then flashing (0.45 s on in every 0.9 s), on at the end.</summary>
    private static float Beacon(Convoy c, float T)
    {
        if (T < CommsAt * c.Scale) return 0.05f;
        var left = c.End - T;
        return Mathf.PosMod(left, 0.9f) < 0.45f ? 1f : 0.08f;
    }

    private static void Set(Slot s, string name, float share)
    {
        if (s.Glows.TryGetValue(name, out var g)) g.Material.EmissionEnergyMultiplier = g.Energy * share;
    }

    private static void Cable(MeshInstance3D cable, Vector3 top, Vector3 bottom, bool shown)
    {
        var v = bottom - top;
        var length = v.Length();
        cable.Visible = shown && length > 0.05f;
        if (!cable.Visible) return;
        var y = v / length;
        var x = Mathf.Abs(y.Dot(Vector3.Forward)) > 0.95f ? y.Cross(Vector3.Right).Normalized() : y.Cross(Vector3.Forward).Normalized();
        var z = x.Cross(y).Normalized();
        cable.Transform = new Transform3D(new Basis(x, y * length, z), (top + bottom) / 2f);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    /// <summary>A node's transform relative to an ancestor, from the local transforms (works before it's in the tree).</summary>
    private static Transform3D ModelSpace(Node3D? node, Node3D ancestor)
    {
        if (node == null || node == ancestor) return Transform3D.Identity;
        var t = node.Transform;
        for (var p = node.GetParent(); p != null && p != ancestor; p = p.GetParent())
            if (p is Node3D p3) t = p3.Transform * t;
        return t;
    }
}
