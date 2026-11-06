using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Per-instance looks for buildings, so a base full of the same building doesn't look cookie-cutter (owner 2026-11-06).
/// Render-only and deterministic: everything comes from a seed the view derives from the structure's tile (no sim
/// RNG, no state), so the construction hologram, the finished model and every rebuild of it look the same. The
/// silhouette of each building type stays; what changes is the hull tint, a secondary accent colour, how weathered it
/// is, small proportion changes (±10% on towers, stacks, masts), which side asymmetric sub-parts sit on, one to three
/// add-ons beside it or on its roof (antenna, roof tanks, solar panel, crates, cargo pods, light pole, hazard sign,
/// barrels, pipe run) and its number ("STORAGE 03").
/// </summary>
public static partial class BuildingModels
{
    /// <summary>The variety draws for one building instance. <see cref="Plain"/> is the canonical look (placement
    /// ghost, the old two-argument <see cref="Build(string, Color)"/>): no mirroring, no add-ons, no number.</summary>
    public sealed class Look
    {
        public static readonly Look Plain = new();

        private ulong _state;
        public readonly bool IsPlain;
        /// <summary>±1: which side asymmetric sub-parts sit on (multiply their x).</summary>
        public readonly float Side = 1f;
        /// <summary>Proportion changes, 0.9–1.1 (towers, stacks, masts).</summary>
        public readonly float Height = 1f, Height2 = 1f, Height3 = 1f;
        /// <summary>0 = fresh paint .. 1 = well weathered.</summary>
        public readonly float Weather = 0.25f;
        public readonly Color Hull = HullColour;
        public readonly Color Accent = new(0.16f, 0.32f, 0.58f);

        private Look() => IsPlain = true;

        public Look(string defId, int seed)
        {
            _state = Mix((ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ Fnv(defId));
            Side = Next() < 0.5f ? -1f : 1f;
            Height = Range(0.9f, 1.1f);
            Height2 = Range(0.9f, 1.1f);
            Height3 = Range(0.92f, 1.08f);
            var w = Next();
            Weather = w * w * 0.9f + 0.05f;
            Hull = HullTints[Int(HullTints.Length)];
            Accent = Accents[Int(Accents.Length)];
        }

        /// <summary>The next draw in 0..1 (splitmix64).</summary>
        public float Next()
        {
            if (IsPlain) return 0.5f;
            _state += 0x9E3779B97F4A7C15UL;
            return (Mix(_state) >> 40) / (float)(1UL << 24);
        }

        public float Range(float a, float b) => a + (b - a) * Next();
        public int Int(int n) => IsPlain ? 0 : Mathf.Min(n - 1, (int)(Next() * n));
        public bool Chance(float p) => !IsPlain && Next() < p;
        public T Pick<T>(T[] items) => items[Int(items.Length)];

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>A stable string hash (string.GetHashCode is randomised per process).</summary>
        private static ulong Fnv(string s)
        {
            var h = 14695981039346656037UL;
            foreach (var c in s) h = (h ^ c) * 1099511628211UL;
            return h;
        }
    }

    /// <summary>Hull paints a building may get: the standard white and a few close neighbours.</summary>
    private static readonly Color[] HullTints =
    {
        new(0.84f, 0.86f, 0.9f),   // standard
        new(0.89f, 0.88f, 0.84f),  // warm white
        new(0.78f, 0.8f, 0.84f),   // light grey
        new(0.82f, 0.85f, 0.9f),   // cool white
        new(0.8f, 0.78f, 0.72f),   // sand-dulled
        new(0.72f, 0.75f, 0.8f),   // grey-blue
    };

    /// <summary>Secondary accent colours (Federation service colours, muted so the owner colour still leads).</summary>
    private static readonly Color[] Accents =
    {
        new(0.16f, 0.32f, 0.58f),  // Starfleet blue
        new(0.1f, 0.44f, 0.47f),   // teal
        new(0.76f, 0.58f, 0.18f),  // operations gold
        new(0.36f, 0.48f, 0.6f),   // science grey-blue
        new(0.66f, 0.3f, 0.15f),   // rust orange
        new(0.38f, 0.44f, 0.3f),   // olive
    };

    private static readonly Color GrimeColour = new(0.4f, 0.35f, 0.28f);

    /// <summary>Where add-ons and the number go for one building type (local frame, front = +Z): the footprint's half
    /// sizes, flat roof spots (x, roof height, z) and the number placard (position, yaw in degrees, text height).</summary>
    private readonly record struct Dressing(string Name, float Hx, float Hz, Vector3[] Roof, Vector3 Plate, float PlateYaw, float PlateHeight, bool PlateBoard, float PlateWidth = 3.2f);

    private static readonly Dictionary<string, Dressing> Dressings = new()
    {
        ["storage_container"] = new("STORAGE", 3.7f, 3.5f, new[] { new Vector3(-1.8f, 5.75f, -1.8f) }, new Vector3(-1.9f, 7.6f, 3.34f), 0f, 0.3f, false),
        ["refinery"] = new("REFINERY", 6.3f, 5.3f, System.Array.Empty<Vector3>(), Vector3.Zero, 0f, 0f, false),
        ["mine"] = new("MINE", 3.3f, 3.3f, new[] { new Vector3(0f, 7.95f, 0f) }, new Vector3(-2.3f, 1.25f, 3.45f), 0f, 0.3f, true),
        ["fusion_reactor"] = new("REACTOR", 4f, 4f, new[] { new Vector3(0f, 7.3f, 0f) }, new Vector3(-2.2f, 0.6f, 3.65f), 0f, 0.3f, true),
        ["prefab_habitat"] = new("HAB", 4.4f, 4f, System.Array.Empty<Vector3>(), new Vector3(3.2f, 1.05f, 4.0f), 0f, 0.3f, true),
        ["foundry"] = new("FOUNDRY", 3.6f, 3.5f, new[] { new Vector3(-1.8f, 4.05f, -2.0f), new Vector3(0.3f, 4.05f, -2.4f) }, new Vector3(-2.0f, 3.35f, 0.02f), 0f, 0.32f, false, 2.0f),
        ["deep_core_mine"] = new("DEEP CORE", 4.1f, 4.1f, new[] { new Vector3(0f, 11f, 0f) }, new Vector3(-2.4f, 1.25f, 3.75f), 0f, 0.28f, true),
        ["vehicle_bay"] = new("BAY", 4.1f, 4.4f, new[] { new Vector3(-2.0f, 6.07f, -2.0f), new Vector3(1.2f, 6.07f, -1.2f) }, new Vector3(0f, 5.18f, 2.31f), 0f, 0.36f, false),
        ["science_lab"] = new("LAB", 3.6f, 3.5f, new[] { new Vector3(-3.0f, 3.0f, -1.2f), new Vector3(1.3f, 3.0f, 2.3f) }, new Vector3(-0.8f, 2.36f, 2.72f), 0f, 0.3f, false),
    };

    private enum AddOn { Antenna, RoofTanks, SolarPanel, Crates, CargoPods, LightPole, HazardSign, Barrels, PipeRun }

    private static readonly AddOn[] GroundAddOns = { AddOn.Crates, AddOn.CargoPods, AddOn.LightPole, AddOn.HazardSign, AddOn.Barrels, AddOn.PipeRun, AddOn.SolarPanel, AddOn.Antenna };
    private static readonly AddOn[] RoofAddOns = { AddOn.Antenna, AddOn.RoofTanks, AddOn.SolarPanel };

    /// <summary>
    /// The per-instance pass after the type's builder: tint the hull, set the accent, weather the paint, add one to
    /// three add-ons and paint the number. Works on primitive models (their materials are fresh per model) and on
    /// imported ones (whose materials <see cref="OwnMaterials"/> has already copied per instance).
    /// </summary>
    private static void Dress(Node3D n, string defId, Look v, int serial)
    {
        if (v.IsPlain || !Dressings.TryGetValue(defId, out var d)) return;
        Paint(n, v);
        var slots = new List<(Vector3 At, float Yaw)>();
        // Ground spots: beside and behind the footprint (never in front, where the camera and the bay's apron are),
        // within ~8.4 of the centre (the scatter keeps 8.5 round a building clear of trees).
        foreach (var (x, z, yaw) in new[]
                 {
                     (-(d.Hx + 1.5f), -d.Hz * 0.45f, 90f), (-(d.Hx + 1.5f), d.Hz * 0.35f, 90f),
                     (d.Hx + 1.5f, -d.Hz * 0.45f, -90f), (d.Hx + 1.5f, d.Hz * 0.35f, -90f),
                     (-d.Hx * 0.5f, -(d.Hz + 1.5f), 0f), (d.Hx * 0.5f, -(d.Hz + 1.5f), 0f),
                 })
        {
            var at = new Vector3(x, 0f, z);
            if (at.Length() > 8.4f) at = at.Normalized() * 8.4f;
            slots.Add((at, yaw));
        }
        Shuffle(slots, v);
        var count = 1 + (v.Chance(0.6f) ? 1 : 0) + (v.Chance(0.3f) ? 1 : 0);
        var addOns = new Node3D { Name = "AddOns" };
        n.AddChild(addOns);
        for (var i = 0; i < count && i < slots.Count; i++)
        {
            var (at, yaw) = slots[i];
            var kind = v.Pick(GroundAddOns);
            AddGround(addOns, kind, at, yaw + v.Range(-15f, 15f), v);
        }
        // the derricks' caps move with their height
        var roof = defId switch
        {
            "mine" => new[] { new Vector3(0f, 0.95f + 7f * v.Height, 0f) },
            "deep_core_mine" => new[] { new Vector3(0f, 1f + 10f * v.Height3, 0f) },
            _ => d.Roof,
        };
        if (roof.Length > 0 && v.Chance(0.55f))
            AddRoof(addOns, v.Pick(RoofAddOns), v.Pick(roof), v.Range(0f, 180f), v);
        if (serial > 0 && d.PlateHeight > 0f)
        {
            var text = $"{d.Name} {serial:00}";
            if (d.PlateBoard)
            {
                // a small sign on two legs at the front corner
                var board = new Node3D { Name = "NumberBoard", Position = d.Plate with { Y = 0f }, RotationDegrees = new Vector3(0, d.PlateYaw, 0) };
                var legs = Metal(Trim, 0.4f, 0.5f);
                foreach (var x in new[] { -0.6f, 0.6f })
                    Add(board, new BoxMesh { Size = new Vector3(0.08f, d.Plate.Y + 0.2f, 0.08f) }, new Vector3(x, (d.Plate.Y + 0.2f) / 2f, -0.06f), legs);
                Add(board, new BoxMesh { Size = new Vector3(1.6f, 0.5f, 0.06f) }, new Vector3(0, d.Plate.Y, -0.06f), Metal(new Color(0.12f, 0.13f, 0.15f), 0.3f, 0.6f));
                board.AddChild(Decal(text, new Vector3(0, d.Plate.Y, -0.025f), 0f, d.PlateHeight, 1.45f, SignInk));
                addOns.AddChild(board);
            }
            else
            {
                n.AddChild(Decal(text, d.Plate, d.PlateYaw, d.PlateHeight, d.PlateWidth, SignInk));
            }
        }
        if (defId == "refinery") PaintRefinerySigns(n, serial);
    }

    private static readonly Color SignInk = new(0.93f, 0.94f, 0.96f);
    private static readonly Color PlateInk = new(0.08f, 0.08f, 0.1f);

    private static void Shuffle<T>(List<T> items, Look v)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = v.Int(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Hull tint and weathering on every non-glowing material of the model, each material once.</summary>
    private static void Paint(Node3D n, Look v)
    {
        var seen = new HashSet<BaseMaterial3D>();
        foreach (var m in Materials(n))
        {
            if (!seen.Add(m) || m.EmissionEnabled && m.ResourceName != "Owner") continue;
            if (m.ResourceName == "Owner") continue; // the player's colour stays exact
            var c = m.AlbedoColor;
            if (m.ResourceName is "Hull" or "Panel")
                c = new Color(c.R * v.Hull.R / HullColour.R, c.G * v.Hull.G / HullColour.G, c.B * v.Hull.B / HullColour.B, c.A);
            else if (m.ResourceName == "Accent")
                c = new Color(v.Accent.R, v.Accent.G, v.Accent.B, c.A);
            else if (m.ResourceName == "" && Near(c, HullColour))
                c = new Color(v.Hull.R, v.Hull.G, v.Hull.B, c.A);
            // weathering: a little darker and warmer, rougher
            var w = v.Weather;
            m.AlbedoColor = c.Lerp(new Color(GrimeColour.R, GrimeColour.G, GrimeColour.B, c.A), 0.2f * w * (c.Luminance > 0.25f ? 1f : 0.4f));
            m.Roughness = Mathf.Min(1f, m.Roughness + 0.18f * w);
        }
    }

    private static bool Near(Color a, Color b) => Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) < 0.03f;

    /// <summary>Every material a model draws with: primitives' MaterialOverride, imported models' surface overrides.</summary>
    private static IEnumerable<BaseMaterial3D> Materials(Node node)
    {
        if (node is MeshInstance3D mi)
        {
            if (mi.MaterialOverride is BaseMaterial3D o) yield return o;
            else if (mi.Mesh != null)
                for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    if (mi.GetSurfaceOverrideMaterial(i) is BaseMaterial3D s) yield return s;
        }
        foreach (var child in node.GetChildren())
            foreach (var m in Materials(child))
                yield return m;
    }

    /// <summary>Give an imported model its own copy of every surface material (instances of one glb share them), so
    /// tinting or animating one building leaves the others alone. Surfaces that already have an override (the Owner
    /// copies <see cref="Recolour"/> made) keep it.</summary>
    private static void OwnMaterials(Node node, Dictionary<Material, Material>? copies = null)
    {
        copies ??= new Dictionary<Material, Material>();
        if (node is MeshInstance3D mi && mi.Mesh != null)
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if (mi.GetSurfaceOverrideMaterial(i) != null || mi.Mesh.SurfaceGetMaterial(i) is not Material m) continue;
                if (!copies.TryGetValue(m, out var copy)) copies[m] = copy = (Material)m.Duplicate();
                mi.SetSurfaceOverrideMaterial(i, copy);
            }
        foreach (var child in node.GetChildren()) OwnMaterials(child, copies);
    }

    /// <summary>A painted decal: a shaded, alpha-cut Label3D facing local +Z (turned by yaw), text `height` tall and at
    /// most maxWidth wide (as <see cref="RoverLook.Paint"/> does for the vehicles).</summary>
    private static Label3D Decal(string text, Vector3 at, float yawDeg, float height, float maxWidth, Color ink)
    {
        const int size = 64;
        var label = new Label3D
        {
            Name = "Decal",
            Text = text,
            FontSize = size,
            PixelSize = height / size,
            OutlineSize = 0,
            Modulate = ink,
            Shaded = true,
            DoubleSided = false,
            AlphaCut = Label3D.AlphaCutMode.Discard,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = at,
            RotationDegrees = new Vector3(0, yawDeg, 0),
        };
        var font = label.Font ?? ThemeDB.FallbackFont;
        if (font != null && maxWidth > 0.001f)
        {
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X * label.PixelSize;
            if (width > maxWidth) label.PixelSize *= maxWidth / width;
        }
        return label;
    }

    /// <summary>Label3Ds on a glb's decal markers (build_vehicles.text --blank convention: scale (width, 1, height) in
    /// Godot, text normal = local +Y), as RoverLook.Paint does.</summary>
    private static void PaintMarker(Node3D model, string marker, string text, Color ink)
    {
        if (model.FindChild(marker, true, false) is not Node3D spot) return;
        var maxWidth = Mathf.Abs(spot.Scale.X);
        var height = Mathf.Abs(spot.Scale.Z);
        spot.Scale = Vector3.One;
        if (height <= 0.001f) return;
        var label = Decal(text, new Vector3(0f, 0.012f, 0f), 0f, height, maxWidth, ink);
        label.RotationDegrees = new Vector3(-90f, 0f, 0f);
        spot.AddChild(label);
    }

    // ---------------------------------------------------------------- add-ons

    private static void AddGround(Node3D parent, AddOn kind, Vector3 at, float yaw, Look v)
    {
        var n = new Node3D { Name = kind.ToString(), Position = at, RotationDegrees = new Vector3(0, yaw, 0) };
        parent.AddChild(n);
        var neutral = Metal(new Color(0.55f, 0.56f, 0.58f), 0.45f, 0.55f);
        var accent = Metal(v.Accent, 0.35f, 0.5f);
        var dark = Metal(Trim, 0.4f, 0.5f);
        switch (kind)
        {
            case AddOn.Crates:
                var crates = 2 + v.Int(3);
                for (var i = 0; i < crates; i++)
                {
                    var s = v.Range(0.8f, 1.2f);
                    var stacked = i == crates - 1 && crates > 2;
                    var p = stacked ? new Vector3(0.1f, 1.0f + s / 2f, 0f) : new Vector3((i - (crates - 1) / 2f) * 1.25f, s / 2f, v.Range(-0.3f, 0.3f));
                    var crate = Add(n, new BoxMesh { Size = new Vector3(s, s, s) }, p, i % 2 == 0 ? accent : neutral, new Vector3(0, v.Range(-12f, 12f), 0));
                    Add(crate, new BoxMesh { Size = new Vector3(s + 0.02f, 0.08f, s + 0.02f) }, new Vector3(0, s * 0.25f, 0), dark);
                }
                break;
            case AddOn.CargoPods:
                var hull = Metal(new Color(0.88f, 0.89f, 0.92f), 0.6f, 0.3f);
                for (var i = 0; i < 2; i++)
                {
                    var p = new Vector3((i - 0.5f) * 1.4f, 0.6f, v.Range(-0.3f, 0.3f));
                    Add(n, new CapsuleMesh { Radius = 0.55f, Height = 2.2f, RadialSegments = 12, Rings = 4 }, p, hull, new Vector3(90, 0, 0));
                    Add(n, new CylinderMesh { TopRadius = 0.58f, BottomRadius = 0.58f, Height = 0.22f, RadialSegments = 12 }, p, accent, new Vector3(90, 0, 0));
                    foreach (var z in new[] { -0.7f, 0.7f })
                        Add(n, new BoxMesh { Size = new Vector3(0.9f, 0.25f, 0.2f) }, new Vector3(p.X, 0.12f, p.Z + z), dark);
                }
                break;
            case AddOn.LightPole:
                var h = v.Range(5f, 6.2f);
                Add(n, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.13f, Height = h, RadialSegments = 8 }, new Vector3(0, h / 2f, 0), Metal(HullColour));
                Add(n, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.4f, RadialSegments = 8 }, new Vector3(0, 0.2f, 0), Metal(new Color(0.9f, 0.72f, 0.18f), 0.2f, 0.6f));
                Add(n, new BoxMesh { Size = new Vector3(0.9f, 0.22f, 0.45f) }, new Vector3(0, h, 0.25f), dark);
                Add(n, new BoxMesh { Size = new Vector3(0.75f, 0.04f, 0.35f) }, new Vector3(0, h - 0.12f, 0.25f), Glow(new Color(1f, 0.95f, 0.85f), 3f));
                break;
            case AddOn.HazardSign:
                Add(n, new BoxMesh { Size = new Vector3(0.08f, 1.6f, 0.08f) }, new Vector3(0, 0.8f, 0), dark);
                Add(n, new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.05f) }, new Vector3(0, 1.4f, 0.05f), Metal(new Color(0.9f, 0.72f, 0.18f), 0.2f, 0.6f), new Vector3(0, 0, 45));
                Add(n, new BoxMesh { Size = new Vector3(0.7f, 0.12f, 0.06f) }, new Vector3(0, 1.4f, 0.06f), Metal(new Color(0.1f, 0.1f, 0.12f), 0.2f, 0.6f), new Vector3(0, 0, -45));
                break;
            case AddOn.Barrels:
                var drums = 3 + v.Int(3);
                for (var i = 0; i < drums; i++)
                {
                    var a = i * 2.4f;
                    var r = i == 0 ? 0f : 0.62f;
                    Add(n, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.9f, RadialSegments = 10 }, new Vector3(Mathf.Cos(a) * r, 0.45f, Mathf.Sin(a) * r), i % 3 == 1 ? neutral : accent);
                }
                break;
            case AddOn.PipeRun:
                var steel = Metal(new Color(0.55f, 0.57f, 0.6f), 0.85f, 0.32f);
                Add(n, new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 4.2f, RadialSegments = 10 }, new Vector3(0, 0.6f, 0), steel, new Vector3(0, 0, 90));
                Add(n, new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.11f, Height = 4.2f, RadialSegments = 8 }, new Vector3(0, 0.6f, 0.45f), steel, new Vector3(0, 0, 90));
                foreach (var x in new[] { -1.8f, 0f, 1.8f })
                    Add(n, new BoxMesh { Size = new Vector3(0.12f, 0.6f, 0.9f) }, new Vector3(x, 0.3f, 0.22f), dark);
                Add(n, new TorusMesh { InnerRadius = 0.14f, OuterRadius = 0.2f, Rings = 12, RingSegments = 6 }, new Vector3(0.9f, 0.95f, 0), Metal(new Color(0.7f, 0.1f, 0.08f), 0.3f, 0.5f));
                Add(n, new BoxMesh { Size = new Vector3(4.21f, 0.06f, 0.06f) }, new Vector3(0, 0.6f, -0.17f), accent);
                break;
            case AddOn.SolarPanel:
                Add(n, new BoxMesh { Size = new Vector3(0.12f, 1.2f, 0.12f) }, new Vector3(0, 0.6f, 0), dark);
                Add(n, new BoxMesh { Size = new Vector3(2.2f, 0.06f, 1.3f) }, new Vector3(0, 1.3f, 0), Metal(new Color(0.05f, 0.1f, 0.22f), 0.6f, 0.2f), new Vector3(-28, 0, 0));
                Add(n, new BoxMesh { Size = new Vector3(2.3f, 0.04f, 0.06f) }, new Vector3(0, 1.62f, -0.58f), accent, new Vector3(-28, 0, 0));
                break;
            case AddOn.Antenna:
                Antenna(n, v.Range(3.4f, 5f), v);
                break;
            case AddOn.RoofTanks:
                RoofTanks(n, accent, dark);
                break;
        }
    }

    private static void AddRoof(Node3D parent, AddOn kind, Vector3 at, float yaw, Look v)
    {
        var n = new Node3D { Name = "Roof" + kind, Position = at, RotationDegrees = new Vector3(0, yaw, 0) };
        parent.AddChild(n);
        switch (kind)
        {
            case AddOn.Antenna:
                Antenna(n, v.Range(2.2f, 3.4f), v);
                break;
            case AddOn.RoofTanks:
                RoofTanks(n, Metal(v.Accent, 0.35f, 0.5f), Metal(Trim, 0.4f, 0.5f));
                break;
            default:
                Add(n, new BoxMesh { Size = new Vector3(0.1f, 0.5f, 0.1f) }, new Vector3(0, 0.25f, 0), Metal(Trim, 0.4f, 0.5f));
                Add(n, new BoxMesh { Size = new Vector3(1.6f, 0.05f, 1.0f) }, new Vector3(0, 0.55f, 0), Metal(new Color(0.05f, 0.1f, 0.22f), 0.6f, 0.2f), new Vector3(-22, 0, 0));
                break;
        }
    }

    private static void Antenna(Node3D n, float h, Look v)
    {
        var steel = Metal(new Color(0.6f, 0.62f, 0.66f), 0.8f, 0.3f);
        Add(n, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.09f, Height = h, RadialSegments = 6 }, new Vector3(0, h / 2f, 0), steel);
        Add(n, new BoxMesh { Size = new Vector3(0.5f, 0.3f, 0.5f) }, new Vector3(0, 0.15f, 0), Metal(Trim, 0.4f, 0.5f));
        for (var i = 1; i <= 2; i++)
            Add(n, new BoxMesh { Size = new Vector3(0.9f - i * 0.2f, 0.04f, 0.04f) }, new Vector3(0, h * (0.45f + i * 0.18f), 0), steel, new Vector3(0, i * 50f, 0));
        if (v.Chance(0.5f))
            Add(n, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.08f, Height = 0.22f, RadialSegments = 12 }, new Vector3(0.3f, h * 0.55f, 0), Metal(HullColour), new Vector3(0, 0, -60));
        Add(n, new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 6, Rings = 3 }, new Vector3(0, h + 0.05f, 0), Glow(new Color(1f, 0.15f, 0.08f), 4f));
    }

    private static void RoofTanks(Node3D n, Material paint, Material dark)
    {
        foreach (var x in new[] { -0.42f, 0.42f })
        {
            Add(n, new CapsuleMesh { Radius = 0.34f, Height = 1.7f, RadialSegments = 12, Rings = 3 }, new Vector3(x, 0.5f, 0), paint, new Vector3(90, 0, 0));
            foreach (var z in new[] { -0.5f, 0.5f })
                Add(n, new BoxMesh { Size = new Vector3(0.36f, 0.18f, 0.12f) }, new Vector3(x, 0.09f, z), dark);
        }
    }
}
