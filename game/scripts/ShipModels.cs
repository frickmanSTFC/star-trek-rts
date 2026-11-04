using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The player's ship seen from orbit (render-only). Hulls are lathed meshes (a profile turned around an axis, with
/// analytic normals and UVs: u around, v along the profile) shaded by ship_hull.gdshader (panel plating and lit
/// window rows); glowing parts use ship_glow.gdshader (pulsing bussard collectors, blinking running lights).
/// Forward is -Z, up is +Y. The Federation ship is the detailed one; other powers keep simpler shapes on the same
/// hull and glow materials.
/// </summary>
public static class ShipModels
{
    public static Node3D Build(string faction, Color owner)
    {
        var ship = new Node3D { Name = "Ship" };
        switch (faction)
        {
            case "borg": Borg(ship); break;
            case "klingon": Klingon(ship); break;
            case "romulan": Romulan(ship); break;
            case "federation": Federation(ship, owner, new Color(0.8f, 0.82f, 0.86f)); break;
            default: Federation(ship, owner, new Color(0.62f, 0.58f, 0.54f)); break;
        }
        ship.AddChild(new OmniLight3D { LightColor = new Color(0.9f, 0.95f, 1f), LightEnergy = 0.5f, OmniRange = 45f, Position = new Vector3(12, 12, 6) });
        return ship;
    }

    // ---------------------------------------------------------------- Federation

    private static void Federation(Node3D ship, Color owner, Color hullColour)
    {
        // Saucer: elliptical (narrower front to back), bridge dome on top, window rows above and below the rim.
        Vector2[] saucer =
        {
            new(0f, 1.75f), new(0.7f, 1.7f), new(1.25f, 1.42f), new(2.1f, 1.18f), new(4.6f, 0.86f), new(7f, 0.47f),
            new(8.8f, 0.14f), new(9.2f, 0f), new(9.05f, -0.24f), new(7.1f, -0.56f), new(4.2f, -0.9f), new(2.1f, -1.1f),
            new(0.9f, -1.42f), new(0f, -1.5f),
        };
        var (saucerMesh, sv) = Lathe(saucer, 72);
        var saucerHull = Hull(hullColour, new Vector2(72f, 14f),
            new Vector4((sv[5] + sv[6]) * 0.5f, (sv[6] - sv[5]) * 0.18f, 160f, 0.55f),
            new Vector4((sv[8] + sv[9]) * 0.5f, (sv[9] - sv[8]) * 0.16f, 150f, 0.5f));
        Part(ship, saucerMesh, saucerHull, new Vector3(0f, 1.5f, -6f), scale: new Vector3(1f, 1f, 0.88f));

        // Neck: a swept wedge from the saucer's underside down to the engineering hull.
        Part(ship, new BoxMesh { Size = new Vector3(1.7f, 3.2f, 5.2f) }, Hull(hullColour, new Vector2(4f, 6f)), new Vector3(0f, -1.1f, 1.2f), new Vector3(-38f, 0f, 0f));

        // Engineering hull (lathed along the ship's length, a little flattened), deflector dish at the front.
        Vector2[] engineering =
        {
            new(0f, 7.7f), new(1.5f, 7.5f), new(2.25f, 6.7f), new(2.6f, 5f), new(2.6f, 1f), new(2.35f, -3f), new(1.85f, -6f),
            new(1.25f, -7.6f), new(0f, -7.8f),
        };
        var (engMesh, ev) = Lathe(engineering, 40);
        Part(ship, engMesh, Hull(hullColour, new Vector2(40f, 18f),
                new Vector4((ev[3] + ev[4]) * 0.5f, 0.012f, 70f, 0.5f), new Vector4((ev[4] + ev[5]) * 0.5f, 0.012f, 70f, 0.45f)),
            new Vector3(0f, -3.3f, 4.6f), new Vector3(-90f, 0f, 0f), new Vector3(1f, 1f, 0.8f));
        Part(ship, new SphereMesh { Radius = 1.5f, Height = 1.1f, RadialSegments = 32, Rings = 12 }, Glow(new Color(0.3f, 0.6f, 1f), 3.2f, pulse: 0.12f, speed: 1.3f),
            new Vector3(0f, -3.4f, -2.75f), new Vector3(90f, 0f, 0f));
        Part(ship, new TorusMesh { InnerRadius = 1.45f, OuterRadius = 1.75f, Rings = 32, RingSegments = 8 }, Glow(new Color(1f, 0.55f, 0.2f), 1.6f),
            new Vector3(0f, -3.4f, -2.7f), new Vector3(90f, 0f, 0f));

        // Pylons up and out to the nacelles.
        foreach (var side in new[] { -1f, 1f })
            Part(ship, new BoxMesh { Size = new Vector3(6.6f, 0.45f, 3.2f) }, Hull(hullColour, new Vector2(6f, 3f)),
                new Vector3(side * 4.3f, -1.15f, 8.6f), new Vector3(0f, 0f, side * 31.7f));

        // Nacelles: long and a little flattened; blue warp grille on the inboard side, red bussard collector in front.
        Vector2[] nacelle =
        {
            new(0f, 8f), new(0.85f, 7.8f), new(1.15f, 7.1f), new(1.2f, 5.2f), new(1.2f, -6f), new(1.02f, -7.4f), new(0f, -7.9f),
        };
        var (nacMesh, _) = Lathe(nacelle, 28);
        foreach (var side in new[] { -1f, 1f })
        {
            var at = new Vector3(side * 7.1f, 0.85f, 9.6f);
            Part(ship, nacMesh, Hull(hullColour, new Vector2(28f, 20f)), at, new Vector3(-90f, 0f, 0f), new Vector3(1f, 1f, 0.78f));
            Part(ship, new BoxMesh { Size = new Vector3(0.14f, 0.55f, 11.5f) }, Glow(new Color(0.35f, 0.65f, 1f), 3.4f, pulse: 0.08f, speed: 3f),
                at + new Vector3(-side * 1.16f, 0f, 0.4f));
            Part(ship, new SphereMesh { Radius = 1.08f, Height = 2.1f, RadialSegments = 24, Rings = 12 }, Glow(new Color(1f, 0.28f, 0.12f), 3.6f, pulse: 0.25f, speed: 2.2f),
                at + new Vector3(0f, 0f, -7.3f), scale: new Vector3(1f, 0.78f, 1f)); // pokes out of the nacelle's front
            Part(ship, new SphereMesh { Radius = 0.12f, Height = 0.24f }, Glow(Colors.White, 6f, blink: 0.7f), at + new Vector3(0f, 0.95f, 6.5f));
        }

        // Impulse engines at the back of the saucer; running lights at the rim (red port, green starboard).
        Part(ship, new BoxMesh { Size = new Vector3(3.4f, 0.32f, 0.3f) }, Glow(new Color(1f, 0.3f, 0.15f), 3f, pulse: 0.1f), new Vector3(0f, 1.62f, 2.05f));
        Part(ship, new SphereMesh { Radius = 0.16f, Height = 0.32f }, Glow(new Color(1f, 0.15f, 0.1f), 5f, blink: 0.5f), new Vector3(-9.25f, 1.5f, -6f));
        Part(ship, new SphereMesh { Radius = 0.16f, Height = 0.32f }, Glow(new Color(0.2f, 1f, 0.3f), 5f, blink: 0.5f), new Vector3(9.25f, 1.5f, -6f));

        // The owner's colour: a thin stripe around the saucer's upper surface.
        Part(ship, new TorusMesh { InnerRadius = 6.85f, OuterRadius = 7.05f, Rings = 72, RingSegments = 6 }, Glow(owner, 1.1f),
            new Vector3(0f, 2.0f, -6f), scale: new Vector3(1f, 0.25f, 0.88f));
    }

    // ---------------------------------------------------------------- other powers (simpler)

    private static void Borg(Node3D ship)
    {
        Part(ship, new BoxMesh { Size = new Vector3(22, 22, 22) }, Hull(new Color(0.18f, 0.2f, 0.18f), new Vector2(18f, 18f), metal: 0.7f), Vector3.Zero);
        for (var i = 0; i < 10; i++)
            Part(ship, new BoxMesh { Size = new Vector3(22.2f, 0.4f, 0.4f) }, Glow(new Color(0.3f, 1f, 0.35f), 2f, pulse: 0.2f, speed: 1f + i * 0.17f), new Vector3(0, -10f + i * 2.2f, 11.1f));
    }

    private static void Klingon(Node3D ship)
    {
        var green = new Color(0.32f, 0.38f, 0.3f);
        Part(ship, new BoxMesh { Size = new Vector3(4, 2.4f, 16) }, Hull(green, new Vector2(6f, 12f)), Vector3.Zero);
        Part(ship, new SphereMesh { Radius = 3f, Height = 3.4f }, Hull(green, new Vector2(16f, 8f)), new Vector3(0, 0, -9f));
        foreach (var x in new[] { -1f, 1f })
            Part(ship, new BoxMesh { Size = new Vector3(14, 0.6f, 6) }, Hull(green, new Vector2(14f, 6f)), new Vector3(x * 8f, -1.5f, 3f), new Vector3(0, 0, x * -12f));
        Part(ship, new SphereMesh { Radius = 1f, Height = 2f }, Glow(new Color(1f, 0.25f, 0.2f), 3f, pulse: 0.2f), new Vector3(0, -0.6f, -11.6f));
    }

    private static void Romulan(Node3D ship)
    {
        var green = new Color(0.3f, 0.5f, 0.38f);
        foreach (var y in new[] { -3.5f, 3.5f })
            Part(ship, new CylinderMesh { TopRadius = 7f, BottomRadius = 7f, Height = 0.8f, RadialSegments = 32 }, Hull(green, new Vector2(40f, 6f)), new Vector3(0, y, 2f));
        Part(ship, new BoxMesh { Size = new Vector3(2, 8, 4) }, Hull(green, new Vector2(4f, 8f)), new Vector3(0, 0, 8f));
        Part(ship, new SphereMesh { Radius = 2f, Height = 3f }, Hull(green, new Vector2(16f, 8f)), new Vector3(0, 0, -6f));
        Part(ship, new SphereMesh { Radius = 0.9f, Height = 1.8f }, Glow(new Color(0.4f, 1f, 0.5f), 3f, pulse: 0.2f), new Vector3(0, 0, 8f));
    }

    // ---------------------------------------------------------------- helpers

    private static void Part(Node3D ship, Mesh mesh, Material material, Vector3 at, Vector3? rotationDegrees = null, Vector3? scale = null)
    {
        var part = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at };
        if (rotationDegrees is { } r) part.RotationDegrees = r;
        if (scale is { } s) part.Scale = s;
        ship.AddChild(part);
    }

    private static ShaderMaterial Hull(Color colour, Vector2 panels, Vector4? windowsA = null, Vector4? windowsB = null, float metal = 0.4f)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ship_hull.gdshader") };
        m.SetShaderParameter("hull_colour", new Vector3(colour.R, colour.G, colour.B));
        m.SetShaderParameter("panels", panels);
        m.SetShaderParameter("metal", metal);
        m.SetShaderParameter("windows_a", windowsA ?? Vector4.Zero);
        m.SetShaderParameter("windows_b", windowsB ?? Vector4.Zero);
        return m;
    }

    private static ShaderMaterial Glow(Color colour, float energy, float pulse = 0f, float speed = 2f, float blink = 0f)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ship_glow.gdshader") };
        m.SetShaderParameter("colour", new Vector3(colour.R, colour.G, colour.B));
        m.SetShaderParameter("energy", energy);
        m.SetShaderParameter("pulse", pulse);
        m.SetShaderParameter("pulse_speed", speed);
        m.SetShaderParameter("blink", blink);
        return m;
    }

    /// <summary>
    /// Turn a profile (radius, height) around the Y axis. The profile runs from the top down; normals come from the
    /// profile's slope, u goes around and v along the profile by arc length (returned per profile point, so window
    /// rows can be placed between points). Same winding as OrbitView's globe (front faces seen from outside).
    /// Rotate the part by -90° about X to lay it along the ship (+Y becomes forward, -Z).
    /// </summary>
    private static (ArrayMesh Mesh, float[] V) Lathe(IReadOnlyList<Vector2> profile, int segments)
    {
        var n = profile.Count;
        var v = new float[n];
        for (var k = 1; k < n; k++) v[k] = v[k - 1] + (profile[k] - profile[k - 1]).Length();
        for (var k = 0; k < n; k++) v[k] /= v[n - 1];

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (var k = 0; k < n; k++)
        {
            // Tangent along the profile (central difference); outward normal in the (radius, height) plane is (-t.y, t.x).
            var t = profile[Mathf.Min(k + 1, n - 1)] - profile[Mathf.Max(k - 1, 0)];
            var n2 = new Vector2(-t.Y, t.X).Normalized();
            for (var i = 0; i <= segments; i++)
            {
                var u = i / (float)segments;
                var phi = u * Mathf.Tau;
                float sin = Mathf.Sin(phi), cos = Mathf.Cos(phi);
                st.SetNormal(new Vector3(n2.X * sin, n2.Y, n2.X * cos).Normalized());
                st.SetUV(new Vector2(u, v[k]));
                st.AddVertex(new Vector3(profile[k].X * sin, profile[k].Y, profile[k].X * cos));
            }
        }
        var row = segments + 1;
        for (var k = 0; k < n - 1; k++)
            for (var i = 0; i < segments; i++)
            {
                var a = k * row + i;
                var b = a + row;
                st.AddIndex(a);
                st.AddIndex(a + 1);
                st.AddIndex(b);
                st.AddIndex(a + 1);
                st.AddIndex(b + 1);
                st.AddIndex(b);
            }
        return (st.Commit(), v);
    }
}
