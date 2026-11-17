using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The starships' 3D models (docs/ship-model-contract.md; render-only): <c>res://models/ships/&lt;ship id&gt;.glb</c>, ship id
/// = ShipChoiceDef.Id (cochrane.glb first, from blender/build_cochrane.py --game; the other ships as they are built). A
/// ship without a file has no model: <see cref="Has"/> is false and <see cref="Build"/> returns null, so the pick page
/// keeps its card picture and the orbit view shows nothing new. Units are metres (the Cochrane is about 480 long):
/// every view scales it down. Godot axes: bow -Z, dorsal +Y, port -X, origin on the long axis about amidships. The
/// glb's one root node is "Ship" (Godot's import puts it under a scene root of its own). Its children by name: "Hull"
/// (everything that does not turn), any number of "Ring…" nodes (RingF, RingA on the Cochrane; other ships have one,
/// two or other names: each is turned about its own Z by <see cref="Spin"/> at its own rate), and "Berth0".. (0 to 6
/// or none: empty nodes at the drop ship berths, local +Y pointing at the ship's axis; <see cref="HangLanders"/>).
/// Emissive materials "glass", "lamp", "navR", "navG", "navW" get a sensible glow when the import left them dark or
/// dim. <see cref="DebugStandIn"/> (debug builds, Shift+F11 on the ship page and in orbit) swaps a primitive stand-in
/// with the same names and axes in for a missing file.
/// </summary>
public static class ShipModels
{
    /// <summary>
    /// Ring spin when the glb doesn't say: radians a second about the ring's local Z (signed, as for RotateZ), by ship
    /// id then node name. The exporter writes each ring's own rate as the glTF custom property "spin", which Godot
    /// brings in as node metadata (<see cref="SpinOf"/>); this table is the fallback, and a ring in neither turns at
    /// <see cref="DefaultSpin"/>. Cochrane: the crew ring 0.8 g at 78 m (about 3 rpm), the workshop ring 0.3 g at
    /// 58 m, turning the other way.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, float>> SpinRates = new()
    {
        ["cochrane"] = new() { ["RingF"] = 0.3172f, ["RingA"] = -0.2253f },
    };

    public const float DefaultSpin = 0.25f;

    /// <summary>The ship a debug build shows when the game has no pick (the sandbox), so the model can be seen there.</summary>
    public const string DebugShip = "cochrane";

    /// <summary>Debug only: a ship without a glb gets the primitive stand-in instead of no model. Default off, so a
    /// missing file means "no model" as the contract says.</summary>
    public static bool DebugStandIn { get; set; }

    private const string StandInMeta = "ship_stand_in", IdMeta = "ship_id", SpinMeta = "spin", ExtrasMeta = "extras";

    private static readonly Dictionary<string, PackedScene?> Scenes = new();

    private sealed class Ring
    {
        public Node3D Node = null!;
        public Basis Rest;
        public float Rate, Angle;
    }

    /// <summary>Each built model's rings (found on the first <see cref="Spin"/>; gone with the model).</summary>
    private static readonly ConditionalWeakTable<Node3D, List<Ring>> RingsOf = new();

    public static string PathOf(string shipId) => $"res://models/ships/{shipId}.glb";

    /// <summary>Whether <see cref="Build"/> would give a model: the ship's glb is there and imported, or the debug
    /// stand-in is on.</summary>
    public static bool Has(string? shipId) => !string.IsNullOrEmpty(shipId) && (DebugStandIn || Scene(shipId) != null);

    private static readonly HashSet<string> Loading = new();

    /// <summary>Starts loading a ship's glb in the background (the ship page asks for every ship when it opens), so the
    /// first look at its model doesn't stall the frame for the whole file. Nothing happens for a ship without a glb,
    /// one already loaded or one already asked for.</summary>
    public static void Preload(string? shipId)
    {
        if (string.IsNullOrEmpty(shipId) || Scenes.ContainsKey(shipId) || Loading.Contains(shipId)) return;
        var path = PathOf(shipId);
        if (ResourceLoader.Exists(path) && ResourceLoader.LoadThreadedRequest(path) == Error.Ok) Loading.Add(shipId);
    }

    /// <summary>The glb's scene (loaded once and kept; null = missing, not imported yet, or it would not load: one
    /// warning at most, and only when the file is there).</summary>
    private static PackedScene? Scene(string shipId)
    {
        if (Scenes.TryGetValue(shipId, out var cached)) return cached;
        PackedScene? scene = null;
        var path = PathOf(shipId);
        if (ResourceLoader.Exists(path))
        {
            // A load Preload started in the background is collected here (waiting for it if it is still running).
            scene = Loading.Remove(shipId) ? ResourceLoader.LoadThreadedGet(path) as PackedScene : GD.Load<PackedScene>(path);
            if (scene == null) GD.PushWarning($"ShipModels: {path} did not load; the {shipId} has no 3D model");
        }
        else if (FileAccess.FileExists(path))
            GD.PushWarning($"ShipModels: {path} is not imported yet (open the project in Godot once); the {shipId} has no 3D model");
        Scenes[shipId] = scene;
        return scene;
    }

    /// <summary>A new instance of the ship's model in metres (not in the tree yet), or null when it has none. The glb's
    /// own hierarchy is kept; emissive materials are fixed up. With <see cref="DebugStandIn"/> on, a ship without a glb
    /// gets the primitive stand-in.</summary>
    public static Node3D? Build(string? shipId)
    {
        if (string.IsNullOrEmpty(shipId)) return null;
        var model = Scene(shipId)?.Instantiate() as Node3D;
        if (model != null) FixEmission(model);
        else if (DebugStandIn) model = StandIn();
        model?.SetMeta(IdMeta, shipId); // Spin's fallback rates are per ship
        return model;
    }

    /// <summary>True for the primitive stand-in (so a view can say so).</summary>
    public static bool IsStandIn(Node3D ship) => ship.HasMeta(StandInMeta);

    // ---------------------------------------------------------------- rings

    /// <summary>
    /// Turns every ring of the model by <paramref name="dt"/> seconds: each direct child of "Ship" whose name starts
    /// with "Ring", about its own Z, at its own rate (the same as RotateZ(rate · dt) every frame, without the drift).
    /// The rate is the glb's "spin" property on that node, else <see cref="SpinRates"/> for the ship and node, else
    /// <see cref="DefaultSpin"/>. A model with no rings is left alone. The rings are looked up once per model, so this
    /// allocates nothing after the first call.
    /// </summary>
    public static void Spin(Node3D ship, double dt)
    {
        if (!RingsOf.TryGetValue(ship, out var rings))
        {
            rings = FindRings(ship);
            RingsOf.Add(ship, rings);
        }
        for (var i = 0; i < rings.Count; i++)
        {
            var ring = rings[i];
            if (!GodotObject.IsInstanceValid(ring.Node)) continue;
            ring.Angle = Mathf.PosMod(ring.Angle + ring.Rate * (float)dt, Mathf.Tau);
            ring.Node.Basis = new Basis(Vector3.Back, ring.Angle) * ring.Rest;
        }
    }

    private static List<Ring> FindRings(Node3D ship)
    {
        var rings = new List<Ring>();
        var id = ship.HasMeta(IdMeta) ? ship.GetMeta(IdMeta).AsString() : "";
        SpinRates.TryGetValue(id, out var table);
        foreach (var child in ShipNode(ship).GetChildren())
        {
            if (child is not Node3D node) continue;
            var name = node.Name.ToString();
            if (!name.StartsWith("Ring", System.StringComparison.Ordinal)) continue;
            var rate = SpinOf(node) ?? (table != null && table.TryGetValue(name, out var listed) ? listed : DefaultSpin);
            rings.Add(new Ring { Node = node, Rest = node.Basis, Rate = rate });
        }
        return rings;
    }

    /// <summary>The node the contract calls "Ship" (the parent of Hull, the rings and the berths): the model's root when
    /// it has that name or holds those parts itself, else the "Ship" node under it (Godot's glTF import wraps the
    /// file's root node in a scene root of its own), else the root.</summary>
    private static Node3D ShipNode(Node3D model)
    {
        if (model.Name == "Ship" || model.HasNode("Hull")) return model;
        return model.FindChild("Ship", true, false) as Node3D ?? (model.FindChild("Hull", true, false)?.GetParent() as Node3D) ?? model;
    }

    /// <summary>A ring's own rate from the glb, read tolerantly: the glTF "extras" Godot keeps as node metadata (a
    /// Dictionary with "spin"), or a "spin" metadata value set directly; a number or a number as text. Null = not there.</summary>
    private static float? SpinOf(Node node)
    {
        if (node.HasMeta(ExtrasMeta))
        {
            var extras = node.GetMeta(ExtrasMeta);
            if (extras.VariantType == Variant.Type.Dictionary && extras.AsGodotDictionary().TryGetValue(SpinMeta, out var value) && Number(value) is { } fromExtras)
                return fromExtras;
        }
        return node.HasMeta(SpinMeta) ? Number(node.GetMeta(SpinMeta)) : null;
    }

    private static float? Number(Variant value) => value.VariantType switch
    {
        Variant.Type.Float => (float)value.AsDouble(),
        Variant.Type.Int => value.AsInt64(),
        Variant.Type.String or Variant.Type.StringName =>
            float.TryParse(value.AsString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
        _ => null,
    };

    // ---------------------------------------------------------------- bounds

    /// <summary>The visible meshes' box in the model's own space, from the local transforms (works before it is in the
    /// tree). A model with no meshes gives a 480 m box.</summary>
    public static Aabb Bounds(Node3D ship)
    {
        var any = false;
        var box = new Aabb();
        Walk(ship, Transform3D.Identity, true, ref box, ref any);
        return any ? box : new Aabb(new Vector3(-80f, -80f, -240f), new Vector3(160f, 160f, 480f));
    }

    private static void Walk(Node node, Transform3D parent, bool root, ref Aabb box, ref bool any)
    {
        var t = parent;
        if (node is Node3D n3)
        {
            if (!root)
            {
                if (!n3.Visible) return;
                t = parent * n3.Transform;
            }
        }
        if (node is MeshInstance3D { Mesh: not null } mi)
        {
            var b = t * mi.GetAabb();
            box = any ? box.Merge(b) : b;
            any = true;
        }
        foreach (var child in node.GetChildren()) Walk(child, t, false, ref box, ref any);
    }

    /// <summary>Bow to stern, in the model's units (metres).</summary>
    public static float LengthOf(Node3D ship) => Mathf.Max(1f, Bounds(ship).Size.Z);

    /// <summary>Puts every mesh under the model on these render layers as well (the orbit view's fill light reaches
    /// only the ship's own layer).</summary>
    public static void AddLayers(Node node, uint layers)
    {
        if (node is VisualInstance3D vi) vi.Layers |= layers;
        foreach (var child in node.GetChildren()) AddLayers(child, layers);
    }

    // ---------------------------------------------------------------- drop ships on the berths

    /// <summary>
    /// Hangs a lander on each empty berth: every direct child of "Ship" whose name starts with "Berth" (a ship has 0 to
    /// 6 of them, or none). The contract: the berth's local +Y points at the ship's axis, so a lander with its origin
    /// at its pads and +Y up hangs roof-in at (0, -height, 0), height = the top of its box. The landers keep their
    /// landed pose (legs out, ramp shut). Returns how many were hung: 0 when the model has no berths or the lander's
    /// glb is not there.
    /// </summary>
    public static int HangLanders(Node3D ship, DropShipSpec spec, Color owner)
    {
        var hung = 0;
        foreach (var child in ShipNode(ship).GetChildren())
        {
            if (child is not Node3D berth || !berth.Name.ToString().StartsWith("Berth", System.StringComparison.Ordinal) || berth.GetChildCount() > 0) continue;
            var lander = BuildingModels.DropShip(owner, spec.Id);
            if (lander == null) return hung; // no glb: the berths stay empty
            var box = Bounds(lander);
            lander.Name = "Lander";
            lander.Position = new Vector3(0f, -(box.Position.Y + box.Size.Y), 0f);
            berth.AddChild(lander);
            hung++;
        }
        return hung;
    }

    // ---------------------------------------------------------------- materials

    private static readonly Dictionary<string, (Color Colour, float Energy)> Glows = new()
    {
        ["glass"] = (new Color(1f, 0.87f, 0.62f), 1.6f),
        ["lamp"] = (new Color(1f, 0.97f, 0.9f), 3f),
        ["navR"] = (new Color(1f, 0.12f, 0.1f), 4f),
        ["navG"] = (new Color(0.15f, 1f, 0.3f), 4f),
        ["navW"] = (new Color(1f, 1f, 1f), 4f),
    };

    /// <summary>Windows, lamps and nav lights that came in dark (no emission, zero energy or a black emission colour)
    /// get their glow; ones the import lit keep their colour and are only brought up to the table's energy when dimmer
    /// (a glTF emissive factor arrives at energy 1, too dim for a lamp to read at these scales). One copy per source
    /// material, shared by its surfaces.</summary>
    private static void FixEmission(Node model)
    {
        Dictionary<Material, BaseMaterial3D>? fixedUp = null;
        var stack = new Stack<Node>();
        stack.Push(model);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var child in n.GetChildren()) stack.Push(child);
            if (n is not MeshInstance3D { Mesh: not null } mi) continue;
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if (mi.Mesh.SurfaceGetMaterial(i) is not BaseMaterial3D m || !Glows.TryGetValue(BaseName(m.ResourceName), out var glow)) continue;
                var lit = m.EmissionEnabled && m.EmissionEnergyMultiplier > 0.01f && (m.EmissionTexture != null || m.Emission.R + m.Emission.G + m.Emission.B > 0.05f);
                if (lit && m.EmissionEnergyMultiplier >= glow.Energy) continue;
                fixedUp ??= new Dictionary<Material, BaseMaterial3D>();
                if (!fixedUp.TryGetValue(m, out var copy))
                {
                    copy = (BaseMaterial3D)m.Duplicate();
                    copy.EmissionEnabled = true;
                    if (!lit) copy.Emission = glow.Colour;
                    copy.EmissionEnergyMultiplier = glow.Energy;
                    fixedUp[m] = copy;
                }
                mi.SetSurfaceOverrideMaterial(i, copy);
            }
        }
    }

    /// <summary>A material's name without a Blender ".001" suffix.</summary>
    private static string BaseName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 && int.TryParse(name[(dot + 1)..], out _) ? name[..dot] : name;
    }

    // ---------------------------------------------------------------- the stand-in

    private static StandardMaterial3D Mat(string name, Color colour, float metallic, float roughness, float glow = 0f, Color? emission = null)
    {
        var m = new StandardMaterial3D { ResourceName = name, AlbedoColor = colour, Metallic = metallic, Roughness = roughness };
        if (glow > 0f)
        {
            m.EmissionEnabled = true;
            m.Emission = emission ?? colour;
            m.EmissionEnergyMultiplier = glow;
        }
        return m;
    }

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Vector3 position, Material material, Vector3? rotationDegrees = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material };
        if (rotationDegrees is { } r) m.RotationDegrees = r;
        parent.AddChild(m);
        return m;
    }

    /// <summary>A cylinder along the ship's long axis (Z), centred at z.</summary>
    private static MeshInstance3D Barrel(Node3D parent, float radiusBow, float radiusStern, float length, float z, Material material, int sides = 20) =>
        Part(parent, new CylinderMesh { TopRadius = radiusBow, BottomRadius = radiusStern, Height = length, RadialSegments = sides }, new Vector3(0f, 0f, z), material, new Vector3(-90f, 0f, 0f));

    /// <summary>
    /// A primitive ring ship with the contract's node names and axes (root "Ship"; "Hull", "RingF", "RingA",
    /// "Berth0".."Berth3"; bow -Z, dorsal +Y; 480 long, the forward ring 78 in radius at z = -190, the aft ring 58 at
    /// z = +22), in metres. Debug only (<see cref="DebugStandIn"/>).
    /// </summary>
    public static Node3D StandIn()
    {
        var ship = new Node3D { Name = "Ship" };
        ship.SetMeta(StandInMeta, true);
        var white = Mat("hull", new Color(0.86f, 0.87f, 0.9f), 0.35f, 0.5f);
        var grey = Mat("frame", new Color(0.42f, 0.44f, 0.48f), 0.6f, 0.45f);
        var dark = Mat("dark", new Color(0.13f, 0.14f, 0.17f), 0.5f, 0.6f);
        var orange = Mat("trim", new Color(0.92f, 0.45f, 0.14f), 0.3f, 0.5f);
        var glass = Mat("glass", new Color(1f, 0.87f, 0.62f), 0.1f, 0.3f, 1.6f);
        var hot = Mat("hot", new Color(0.3f, 0.12f, 0.08f), 0.2f, 0.7f, 0.5f, new Color(1f, 0.3f, 0.1f));

        var hull = new Node3D { Name = "Hull" };
        ship.AddChild(hull);
        Barrel(hull, 7f, 7f, 420f, 5f, grey, 12); // the spine, z -205 .. 215
        Barrel(hull, 9f, 14f, 30f, -225f, white); // the bow module, z -240 .. -210
        Barrel(hull, 9.6f, 9.6f, 3f, -228f, glass); // its window band
        Barrel(hull, 16f, 16f, 44f, -190f, white); // the forward hub, inside the crew ring
        Barrel(hull, 24f, 24f, 16f, -207.4f, grey, 8); // the berth collar ahead of it
        Barrel(hull, 12f, 12f, 30f, 22f, white); // the aft hub
        for (var k = 0; k < 4; k++) // propellant tanks round the spine
        {
            var a = Mathf.DegToRad(45f + 90f * k);
            Part(hull, new CylinderMesh { TopRadius = 15f, BottomRadius = 15f, Height = 110f, RadialSegments = 16 },
                new Vector3(Mathf.Cos(a) * 24f, Mathf.Sin(a) * 24f, 120f), k % 2 == 0 ? white : orange, new Vector3(-90f, 0f, 0f));
        }
        Part(hull, new BoxMesh { Size = new Vector3(190f, 2f, 60f) }, new Vector3(0f, 0f, 80f), hot); // radiators, edge on to the rings
        Barrel(hull, 20f, 26f, 36f, 208f, dark); // the engine block
        Barrel(hull, 10f, 22f, 28f, 226f, dark, 16); // the bell, opening aft (z 212 .. 240)
        Part(hull, new SphereMesh { Radius = 2.4f, Height = 4.8f, RadialSegments = 8, Rings = 4 }, new Vector3(-27f, 0f, 208f), Mat("navR", new Color(1f, 0.12f, 0.1f), 0f, 0.4f, 4f));
        Part(hull, new SphereMesh { Radius = 2.4f, Height = 4.8f, RadialSegments = 8, Rings = 4 }, new Vector3(27f, 0f, 208f), Mat("navG", new Color(0.15f, 1f, 0.3f), 0f, 0.4f, 4f));
        Part(hull, new SphereMesh { Radius = 2.4f, Height = 4.8f, RadialSegments = 8, Rings = 4 }, new Vector3(0f, 15f, -225f), Mat("navW", Colors.White, 0f, 0.4f, 4f));

        // The rings carry their rates the way a glb's do (the "spin" property), so Spin needs no table for them.
        ship.AddChild(RingPart("RingF", -190f, 78f, 6f, 4, 0.3172f, white, grey, glass));
        ship.AddChild(RingPart("RingA", 22f, 58f, 5f, 3, -0.2253f, white, grey, glass));

        // Four berths on the collar ahead of the crew ring, where the Cochrane's glb has them (24 out, z -207.4;
        // Berth0 port-dorsal, then round by starboard): +Y at the axis, Z along the ship.
        for (var k = 0; k < 4; k++)
        {
            var a = Mathf.DegToRad(135f - 90f * k);
            var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            var y = -radial;
            var z = Vector3.Back;
            ship.AddChild(new Node3D { Name = $"Berth{k}", Transform = new Transform3D(new Basis(y.Cross(z), y, z), radial * 24f + new Vector3(0f, 0f, -207.4f)) });
        }
        return ship;
    }

    /// <summary>One ring about the Z axis at z: the torus, a lit window line on its rim, a collar and spokes to the hub.
    /// The node's own basis stays identity, so it spins about the ship's long axis.</summary>
    private static Node3D RingPart(string name, float z, float radius, float tube, int spokes, float spin, Material skin, Material frame, Material glass)
    {
        var ring = new Node3D { Name = name, Position = new Vector3(0f, 0f, z) };
        ring.SetMeta(SpinMeta, spin);
        Part(ring, new TorusMesh { InnerRadius = radius - tube, OuterRadius = radius + tube, Rings = 72, RingSegments = 12 }, Vector3.Zero, skin, new Vector3(90f, 0f, 0f));
        Part(ring, new TorusMesh { InnerRadius = radius + tube - 0.6f, OuterRadius = radius + tube + 0.5f, Rings = 72, RingSegments = 4 }, Vector3.Zero, glass, new Vector3(90f, 0f, 0f));
        Barrel(ring, 18f, 18f, 12f, 0f, frame);
        for (var k = 0; k < spokes; k++)
        {
            // A spoke from the collar to the ring: a cylinder along Y, turned about Z to its angle.
            var a = 360f / spokes * k;
            var rad = Mathf.DegToRad(a);
            var mid = (radius - tube + 18f) * 0.5f;
            Part(ring, new CylinderMesh { TopRadius = 2.4f, BottomRadius = 2.4f, Height = radius - tube - 18f, RadialSegments = 8 },
                new Vector3(-Mathf.Sin(rad) * mid, Mathf.Cos(rad) * mid, 0f), frame, new Vector3(0f, 0f, a));
        }
        return ring;
    }
}
