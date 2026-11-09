using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Native settlements by development level (civilizations phase 1, docs/civilizations-contract.md "Models"):
/// blender/build_native_towns.py → models/natives/towns.glb, one mesh per town type (town_l1_a/b … town_l4_a/b,
/// town_l5_a, town_l6_a, town_l1_far … town_l6_far) with empties Smoke0.. on the L3/L4 stacks. The glb is read once
/// (like <see cref="ScatterAssets"/>): its meshes and smoke points are kept and the imported scene freed, so a town is
/// a fresh MeshInstance3D sharing the mesh, with its own material copies: "Species" (the species colour),
/// "SpeciesTrim" (<see cref="TrimColour"/>), "CityLight" (emission driven by <see cref="SetNight"/>), "Glass",
/// "Ground"; vertex colours shade them. Without the glb (not imported yet) every town is a primitive stand-in with
/// the same node names. Render-only, no sim state; a town is ~20 across (one hex) at scale 1.
/// </summary>
public static class NativeTowns
{
    public const string Path = "res://models/natives/towns.glb";

    /// <summary>Meta on a built town's root: the Array of its CityLight materials (<see cref="SetNight"/>).</summary>
    public static readonly StringName LightsMeta = "native_town_lights";

    /// <summary>Radius of one town at scale 1 (its ground pad).</summary>
    public const float TownRadius = 9.6f;

    private const float DayLight = 0.12f;
    private const float NightLight = 3.2f;

    private sealed class Template
    {
        public required Mesh Mesh;
        public Transform3D Transform = Transform3D.Identity;
        public readonly List<(string Name, Transform3D At)> Smoke = new();
    }

    private static Dictionary<string, Template>? _templates;

    private static Dictionary<string, Template> Templates
    {
        get
        {
            if (_templates != null) return _templates;
            _templates = new Dictionary<string, Template>();
            if (ResourceLoader.Exists(Path) && GD.Load<PackedScene>(Path)?.Instantiate() is Node root)
            {
                Collect(root);
                root.Free();
            }
            return _templates;
        }
    }

    /// <summary>True when the glb was found and held the town meshes (else primitives stand in).</summary>
    public static bool Imported => Templates.Count > 0;

    private static void Collect(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null && mi.Name.ToString().StartsWith("town_", StringComparison.Ordinal))
        {
            var t = new Template { Mesh = mi.Mesh, Transform = mi.Transform };
            foreach (var child in mi.GetChildren())
                if (child is Node3D c && SmokeName(c.Name.ToString()) is { } smoke) t.Smoke.Add((smoke, c.Transform));
            _templates![mi.Name.ToString()] = t;
        }
        foreach (var child in node.GetChildren()) Collect(child);
    }

    /// <summary>"Smoke3" for "Smoke3", "Smoke3_001", "Smoke3.001" (Blender's unique-name suffix); else null.</summary>
    private static string? SmokeName(string name)
    {
        if (!name.StartsWith("Smoke", StringComparison.Ordinal)) return null;
        var end = 5;
        while (end < name.Length && char.IsDigit(name[end])) end++;
        return end > 5 ? name[..end] : null;
    }

    // ------------------------------------------------------------------ API

    /// <summary>
    /// A native settlement of the level, recoloured for the species. Size 1 = one town; size 2 = 2–3 towns, size 3 =
    /// 3–4 (the main one in the middle, smaller satellites round it, some a level lower as outskirts), all placed,
    /// scaled and turned deterministically from (level, variant, size). Children are named after their town node
    /// (town_l3_a, town_l3_b_2 …); smoke points (L3/L4) are Node3Ds "Smoke0".. under them (<see cref="SmokePoints"/>).
    /// The root carries <see cref="LightsMeta"/>; it starts in the day look (call <see cref="SetNight"/>).
    /// </summary>
    public static Node3D Build(CivLevel level, int variant, Color speciesColour, int size)
    {
        var lv = LevelOf(level);
        var root = new Node3D { Name = $"NativeTown_L{lv}" };
        var mats = new Materials(speciesColour);
        var names = new HashSet<string>();
        foreach (var m in Layout(lv, variant, size))
        {
            var town = Town(m.Key, mats);
            var name = m.Key;
            for (var k = 2; !names.Add(name); k++) name = $"{m.Key}_{k}";
            town.Name = name;
            town.Transform = new Transform3D(new Basis(Vector3.Up, m.Yaw).Scaled(Vector3.One * m.Scale), m.Position) * town.Transform;
            root.AddChild(town);
        }
        root.SetMeta(LightsMeta, mats.Lights);
        return root;
    }

    /// <summary>The cheap stand-in for the level (≤ 300 triangles; for the globe and far views), recoloured. Size 2–3
    /// scales it up to roughly the cluster's footprint (<see cref="Radius"/>).</summary>
    public static Node3D Far(CivLevel level, Color speciesColour, int size = 1)
    {
        var lv = LevelOf(level);
        var root = new Node3D { Name = $"NativeTownFar_L{lv}" };
        var mats = new Materials(speciesColour);
        var town = Town($"town_l{lv}_far", mats);
        var s = size <= 1 ? 1f : size == 2 ? 1.5f : 1.8f;
        town.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * s), Vector3.Zero) * town.Transform;
        root.AddChild(town);
        root.SetMeta(LightsMeta, mats.Lights);
        return root;
    }

    /// <summary>How far a <see cref="Build"/> result reaches from its origin (world units at scale 1).</summary>
    public static float Radius(CivLevel level, int variant, int size)
    {
        var r = 0f;
        foreach (var m in Layout(LevelOf(level), variant, size))
            r = Mathf.Max(r, new Vector2(m.Position.X, m.Position.Z).Length() + TownRadius * m.Scale);
        return r;
    }

    /// <summary>
    /// Drive the "CityLight" emission of a town built here: 0 = day (windows dark, fires faint), 1 = full night.
    /// Cheap (no tree walk when the root from <see cref="Build"/>/<see cref="Far"/> is passed).
    /// </summary>
    public static void SetNight(Node3D town, float night)
    {
        var n = Mathf.Clamp(night, 0f, 1f);
        var energy = Mathf.Lerp(DayLight, NightLight, n * n * (3f - 2f * n));
        if (town.HasMeta(LightsMeta))
        {
            foreach (var v in town.GetMeta(LightsMeta).AsGodotArray())
                if (v.AsGodotObject() is BaseMaterial3D m) m.EmissionEnergyMultiplier = energy;
            return;
        }
        SetNightIn(town, energy);
    }

    private static void SetNightIn(Node node, float energy)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.GetSurfaceOverrideMaterial(i) is BaseMaterial3D m && m.ResourceName == "CityLight")
                    m.EmissionEnergyMultiplier = energy;
        foreach (var child in node.GetChildren()) SetNightIn(child, energy);
    }

    /// <summary>Every smoke point (Node3D "Smoke0"..) under a built town, for chimney smoke on L3/L4.</summary>
    public static List<Node3D> SmokePoints(Node3D town)
    {
        var list = new List<Node3D>();
        AddSmoke(town, list);
        return list;
    }

    private static void AddSmoke(Node node, List<Node3D> list)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Node3D n && SmokeName(n.Name.ToString()) != null) list.Add(n);
            AddSmoke(child, list);
        }
    }

    /// <summary>A pleasant species colour, the same for the same seed: one of twelve muted base hues (ochre,
    /// terracotta, sage, teal, slate, lilac, rose, sand, moss, coral, plum, jade) nudged a little in hue,
    /// saturation and value.</summary>
    public static Color SpeciesColour(int seed)
    {
        var h = Mix((ulong)(uint)seed ^ 0x6E61746976657321UL);
        var pick = Palette[(int)(h % (ulong)Palette.Length)];
        var hue = pick.H + (Unit(h >> 8) - 0.5f) * 0.05f;
        var sat = Mathf.Clamp(pick.S * (0.9f + 0.2f * Unit(h >> 24)), 0.12f, 0.8f);
        var val = Mathf.Clamp(pick.V * (0.92f + 0.14f * Unit(h >> 40)), 0.3f, 0.92f);
        return Color.FromHsv(hue - Mathf.Floor(hue), sat, val);
    }

    /// <summary>The trim (roofs, domes, bands) for a species colour: hue nudged, darker for light colours and lighter
    /// for dark ones, so the two always read apart. Same rule as the Blender previews' trim_of.</summary>
    public static Color TrimColour(Color species)
    {
        species.ToHsv(out var h, out var s, out var v);
        var hue = h + 0.06f;
        hue -= Mathf.Floor(hue);
        return v > 0.55f
            ? Color.FromHsv(hue, Mathf.Min(1f, s * 0.85f), v * 0.55f)
            : Color.FromHsv(hue, s * 0.6f, Mathf.Min(1f, v * 1.5f + 0.12f));
    }

    private static readonly Color[] Palette =
    {
        new(0.72f, 0.56f, 0.30f), // ochre
        new(0.66f, 0.40f, 0.28f), // terracotta
        new(0.56f, 0.62f, 0.46f), // sage
        new(0.30f, 0.56f, 0.58f), // teal
        new(0.42f, 0.50f, 0.62f), // slate
        new(0.58f, 0.50f, 0.68f), // lilac
        new(0.70f, 0.48f, 0.54f), // rose
        new(0.78f, 0.70f, 0.52f), // sand
        new(0.46f, 0.52f, 0.30f), // moss
        new(0.80f, 0.50f, 0.40f), // coral
        new(0.50f, 0.34f, 0.46f), // plum
        new(0.34f, 0.62f, 0.48f), // jade
    };

    // ------------------------------------------------------------------ layout (deterministic, no RNG state)

    private readonly record struct Member(string Key, Vector3 Position, float Yaw, float Scale);

    private static int LevelOf(CivLevel level) => Math.Clamp((int)level, 1, 6);

    private static string[] VariantsOf(int lv) => lv <= 4 ? new[] { "a", "b" } : new[] { "a" };

    private static string KeyOf(int lv, int variant)
    {
        var v = VariantsOf(lv);
        return $"town_l{lv}_{v[(int)((uint)variant % (uint)v.Length)]}";
    }

    private static List<Member> Layout(int lv, int variant, int size)
    {
        var list = new List<Member>();
        var seed = Mix(((ulong)(uint)lv << 40) ^ ((ulong)(uint)variant << 8) ^ (ulong)(uint)Math.Clamp(size, 1, 3));
        list.Add(new Member(KeyOf(lv, variant), Vector3.Zero, Unit(seed) * Mathf.Tau, 1f));
        if (size <= 1) return list;
        var extra = size == 2 ? 1 + (int)(Mix(seed + 1) % 2) : 2 + (int)(Mix(seed + 1) % 2);
        var start = Unit(Mix(seed + 2)) * Mathf.Tau;
        for (var i = 0; i < extra; i++)
        {
            var h = Mix(seed + 10 + (ulong)i);
            // outskirts: often the other variant, sometimes a level lower (L5/L6 towns sit among L4/L5 ones)
            var slv = lv > 1 && Unit(h >> 4) < (lv >= 5 ? 0.6f : 0.4f) ? lv - 1 : lv;
            var key = KeyOf(slv, variant + 1 + i);
            var scale = size == 2 ? 0.62f + 0.16f * Unit(h >> 12) : 0.68f + 0.18f * Unit(h >> 12);
            var angle = start + Mathf.Tau * i / extra + (Unit(h >> 20) - 0.5f) * 0.6f;
            var dist = TownRadius * (0.86f + scale * 0.86f);
            list.Add(new Member(key, new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist), Unit(h >> 28) * Mathf.Tau, scale));
        }
        return list;
    }

    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private static float Unit(ulong h) => (h & 0xFFFFFF) / (float)0x1000000;

    // ------------------------------------------------------------------ materials

    /// <summary>One set of material copies per Build/Far call (a cluster shares them).</summary>
    private sealed class Materials
    {
        private readonly Color _species, _trim, _light;
        private readonly Dictionary<string, BaseMaterial3D> _made = new();
        public readonly Godot.Collections.Array Lights = new();

        public Materials(Color species)
        {
            _species = species;
            _trim = TrimColour(species);
            species.ToHsv(out var h, out _, out _);
            _light = new Color(1f, 0.74f, 0.42f).Lerp(Color.FromHsv(h, 0.35f, 1f), 0.25f);
        }

        /// <summary>Our copy of the named material (from the glb's material when there is one).</summary>
        public BaseMaterial3D Get(string name, Material? source)
        {
            if (_made.TryGetValue(name, out var made)) return made;
            var m = source is BaseMaterial3D b ? (BaseMaterial3D)b.Duplicate() : new StandardMaterial3D { ResourceName = name };
            m.ResourceName = name;
            switch (name)
            {
                case "Species":
                    m.AlbedoColor = _species;
                    m.VertexColorUseAsAlbedo = source != null;
                    m.Metallic = 0f;
                    m.Roughness = 0.8f;
                    break;
                case "SpeciesTrim":
                    m.AlbedoColor = _trim;
                    m.VertexColorUseAsAlbedo = source != null;
                    m.Metallic = 0f;
                    m.Roughness = 0.65f;
                    break;
                case "CityLight":
                    m.AlbedoColor = new Color(0.3f, 0.25f, 0.18f);
                    m.VertexColorUseAsAlbedo = false;
                    m.EmissionEnabled = true;
                    m.Emission = _light;
                    m.EmissionEnergyMultiplier = DayLight;
                    m.SetMeta(VehicleLights.NoBoostMeta, true); // PlanetView.Sky's night boost: SetNight drives these
                    Lights.Add(m);
                    break;
                case "Glass":
                    m.AlbedoColor = new Color(0.08f, 0.14f, 0.18f).Lerp(_species * 0.35f, 0.2f);
                    m.VertexColorUseAsAlbedo = false;
                    m.Metallic = 0.45f;
                    m.Roughness = 0.12f;
                    break;
                case "Ground":
                    if (source == null) m.AlbedoColor = new Color(0.5f, 0.43f, 0.33f);
                    m.VertexColorUseAsAlbedo = source != null;
                    m.Roughness = 0.95f;
                    break;
            }
            _made[name] = m;
            return m;
        }
    }

    // ------------------------------------------------------------------ towns

    private static Node3D Town(string key, Materials mats)
    {
        if (!Templates.TryGetValue(key, out var t)) return Primitive(key, mats);
        var mi = new MeshInstance3D { Name = key, Mesh = t.Mesh, Transform = t.Transform };
        for (var i = 0; i < t.Mesh.GetSurfaceCount(); i++)
        {
            var src = t.Mesh.SurfaceGetMaterial(i);
            var name = src?.ResourceName ?? "";
            if (name.Length > 0) mi.SetSurfaceOverrideMaterial(i, mats.Get(name, src));
        }
        foreach (var (smoke, at) in t.Smoke) mi.AddChild(new Node3D { Name = smoke, Transform = at });
        return mi;
    }

    // ------------------------------------------------------------------ primitive stand-ins (no glb)

    private static readonly Dictionary<string, Mesh> Shapes = new();

    private static Mesh Shape(string id, Func<Mesh> make)
    {
        if (!Shapes.TryGetValue(id, out var m)) Shapes[id] = m = make();
        return m;
    }

    private static Mesh Box => Shape("box", () => new BoxMesh { Size = Vector3.One });
    private static Mesh Cyl => Shape("cyl", () => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 1 });
    private static Mesh Cone => Shape("cone", () => new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 });
    private static Mesh Ball => Shape("ball", () => new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 });
    private static Mesh Ring => Shape("ring", () => new TorusMesh { InnerRadius = 0.7f, OuterRadius = 1f, Rings = 16, RingSegments = 4 });

    /// <summary>A shape of unit size scaled to (sx, sy, sz) standing on y0 (centred for Ball/Ring).</summary>
    private static void Put(Node3D parent, Mesh mesh, BaseMaterial3D mat, float x, float y0, float z, float sx, float sy, float sz, float yawDeg = 0f)
    {
        var centred = mesh == Ball || mesh == Ring;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            Transform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)).Scaled(new Vector3(sx, sy, sz)),
                new Vector3(x, centred ? y0 : y0 + sy / 2f, z)),
        });
    }

    private static Node3D Primitive(string key, Materials mats)
    {
        var n = new Node3D { Name = key };
        var species = mats.Get("Species", null);
        var trim = mats.Get("SpeciesTrim", null);
        var light = mats.Get("CityLight", null);
        var glass = mats.Get("Glass", null);
        var ground = mats.Get("Ground", null);
        var lv = key.Length > 6 && char.IsDigit(key[6]) ? key[6] - '0' : 1;
        var far = key.EndsWith("_far", StringComparison.Ordinal);
        var b = key.EndsWith("_b", StringComparison.Ordinal);
        var turn = b ? 90f : 0f;
        Put(n, Cyl, ground, 0, 0, 0, TownRadius * 2f, 0.1f, TownRadius * 2f);
        var smoke = 0;

        void Stack(float x, float z, float h)
        {
            Put(n, Cyl, species, x, 0, z, 0.9f, h, 0.9f);
            n.AddChild(new Node3D { Name = $"Smoke{smoke++}", Position = new Vector3(x, h + 0.2f, z) });
        }

        (float X, float Z) At(float x, float z)
        {
            var a = Mathf.DegToRad(turn);
            return (x * Mathf.Cos(a) - z * Mathf.Sin(a), x * Mathf.Sin(a) + z * Mathf.Cos(a));
        }

        switch (lv)
        {
            case 1:
                for (var i = 0; i < (far ? 5 : 7); i++)
                {
                    var a = Mathf.Tau * i / (far ? 5 : 7) + (b ? 0.4f : 0f);
                    Put(n, Ball, species, 4.5f * Mathf.Cos(a), 0, 4.5f * Mathf.Sin(a), 2f, 4f, 2f);
                }
                Put(n, Cone, light, 0, 0, 0, 0.6f, 0.8f, 0.6f);
                break;
            case 2:
                for (var i = 0; i < 12; i++)
                {
                    var a = Mathf.Tau * i / 12;
                    Put(n, Box, species, 7.6f * Mathf.Cos(a), 0, 7.6f * Mathf.Sin(a), 0.55f, 1.7f, 4.1f, -Mathf.RadToDeg(a));
                }
                for (var i = 0; i < 3; i++)
                {
                    var a = Mathf.Tau * i / 3 + Mathf.DegToRad(turn);
                    Put(n, Cyl, species, 8.2f * Mathf.Cos(a), 0, 8.2f * Mathf.Sin(a), 1.6f, 3.6f, 1.6f);
                    Put(n, Ball, trim, 8.2f * Mathf.Cos(a), 4.2f, 8.2f * Mathf.Sin(a), 1.8f, 2.2f, 1.8f);
                }
                Put(n, Cone, trim, 0, 0, 0, 2.4f, 8.5f, 2.4f);
                if (!far)
                    for (var i = 0; i < 6; i++)
                    {
                        var a = Mathf.Tau * i / 6 + 0.5f;
                        Put(n, Box, species, 4.6f * Mathf.Cos(a), 0, 4.6f * Mathf.Sin(a), 1.6f, 1.2f, 1.4f);
                        Put(n, Ball, trim, 4.6f * Mathf.Cos(a), 1.2f, 4.6f * Mathf.Sin(a), 1.6f, 1.2f, 1.4f);
                    }
                break;
            case 3:
            {
                var (hx, hz) = At(-1.5f, 5.3f);
                Put(n, Box, species, hx, 0, hz, 7f, 2.8f, 3.2f, turn);
                Put(n, Box, light, hx, 1.2f, hz, 7.05f, 0.4f, 3.25f, turn);
                for (var i = 0; i < (far ? 1 : 3); i++)
                {
                    var (sx, sz) = At(-3.7f + 2.2f * i, 6.3f);
                    Stack(sx, sz, 7.5f + (i == 1 ? 1f : 0f));
                }
                foreach (var (x, z) in new[] { (-5f, -4.6f), (-1.8f, -4.6f), (4.6f, -4.4f), (-4.7f, 0.6f) })
                {
                    var (px, pz) = At(x, z);
                    Put(n, Box, trim, px, 0, pz, 2.6f, 1.8f, 1.9f, turn);
                    Put(n, Box, light, px, 0.8f, pz, 2.65f, 0.25f, 1.95f, turn);
                }
                var (gx, gz) = At(6f, 6f);
                Put(n, Cyl, species, gx, 0, gz, 3f, 2.4f, 3f);
                break;
            }
            case 4:
            {
                var (cx, cz) = At(-2.4f, -3.2f);
                Put(n, Cyl, species, cx, 0, cz, 1.8f, 13.5f, 1.8f);
                Put(n, Ring, trim, cx, 8f, cz, 2.2f, 1.2f, 2.2f);
                Put(n, Ball, glass, cx, 14.2f, cz, 2f, 1.9f, 2f);
                foreach (var (x, z, w, d, h) in new[] { (4.4f, -4f, 2.6f, 1.8f, 7.5f), (5f, 2.5f, 2.2f, 2.2f, 5.5f), (-4.6f, 2.5f, 3f, 1.6f, 4.6f) })
                {
                    var (px, pz) = At(x, z);
                    Put(n, Box, glass, px, 0, pz, w, h, d, turn);
                    Put(n, Box, light, px, h * 0.3f, pz, w + 0.06f, h * 0.4f, d + 0.06f, turn);
                }
                var (tx, tz) = At(4f, 7f);
                Put(n, Cyl, species, tx, 0, tz, 3.2f, 4.2f, 3.2f);
                n.AddChild(new Node3D { Name = $"Smoke{smoke++}", Position = new Vector3(tx, 4.5f, tz) });
                break;
            }
            case 5:
                Put(n, Cyl, trim, 3.4f, 0, 4.6f, 7.2f, 0.3f, 7.2f);
                Put(n, Cyl, species, 3.4f, 0.3f, 4.6f, 1.2f, 8f, 1.2f);
                Put(n, Cone, species, 3.4f, 8.3f, 4.6f, 1.2f, 3.5f, 1.2f);
                Put(n, Box, trim, 5.6f, 0.3f, 3.6f, 1.4f, 9f, 1.4f);
                Put(n, Cyl, species, -3.6f, 0, -4.2f, 2.4f, 12f, 2.4f);
                Put(n, Box, light, -3.6f, 3f, -4.2f, 2.5f, 5f, 2.5f);
                Put(n, Box, glass, 5.4f, 0, -3.2f, 2.4f, 6f, 1.6f);
                Put(n, Ball, glass, -1.6f, 0, 6.4f, 3.2f, 2.6f, 3.2f);
                break;
            default:
                foreach (var (x, z, h, r) in new[] { (-1.8f, -1.4f, 30f, 2.6f), (3.4f, -3.4f, 21f, 2f), (-1f, 4f, 16f, 1.8f) })
                {
                    Put(n, Cone, species, x, 0, z, r * 2.4f, h, r * 2.4f);
                    Put(n, Cyl, light, x, h * 0.25f, z, r * 1.7f, h * 0.35f, r * 1.7f);
                }
                foreach (var (x, z, h, r) in new[] { (6f, 3f, 5f, 1.9f), (-5.8f, -4.6f, 4f, 1.7f), (-6f, 3f, 2.8f, 1.6f) })
                {
                    Put(n, Cyl, species, x, 0, z, 0.9f, h, 0.9f);
                    Put(n, Cyl, trim, x, h, z, r * 2f, 0.3f, r * 2f);
                }
                Put(n, Ball, glass, 2.8f, 0, 5.6f, 3.8f, 2.8f, 3.8f);
                break;
        }
        return n;
    }
}
