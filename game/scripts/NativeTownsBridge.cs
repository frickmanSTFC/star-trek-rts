using System;
using System.Reflection;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The view's one door to the native town models (civilizations phase 1, docs/civilizations-contract.md "Models").
/// The model agent's <c>NativeTowns</c> (game/scripts/NativeTowns.cs: <c>Build(CivLevel level, int variant, Color
/// speciesColour, int size)</c>, <c>Far(CivLevel level, Color colour)</c>, <c>SpeciesColour(int seed)</c>,
/// <c>SetNight(Node3D, float)</c>, all static) is looked up by reflection, so this builds and runs whether or not that
/// file is in the build yet: when the class (or one of those signatures) is missing, small primitive stand-ins are used
/// instead (one warning in the Output panel). Once NativeTowns.cs is merged nothing here needs to change; the stand-ins
/// can then be deleted and the calls made direct.
/// </summary>
public static class NativeTownsBridge
{
    private static readonly Type? Towns = typeof(NativeTownsBridge).Assembly.GetType("FinalFrontier.NativeTowns");
    private static readonly MethodInfo? BuildMethod = Find("Build", typeof(CivLevel), typeof(int), typeof(Color), typeof(int));
    private static readonly MethodInfo? FarMethod = Find("Far", typeof(CivLevel), typeof(Color));
    private static readonly MethodInfo? ColourMethod = Find("SpeciesColour", typeof(int));
    private static readonly MethodInfo? NightMethod = Find("SetNight", typeof(Node3D), typeof(float));
    private static bool _warned;

    /// <summary>True when the real NativeTowns models are in the build.</summary>
    public static bool Available => BuildMethod != null;

    private static MethodInfo? Find(string name, params Type[] args) =>
        Towns?.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null);

    private static object? Call(MethodInfo? method, params object[] args)
    {
        if (method == null)
        {
            Warn(Towns == null ? "NativeTowns is not in this build" : "a NativeTowns method has another signature than the contract's");
            return null;
        }
        try
        {
            return method.Invoke(null, args);
        }
        catch (Exception e)
        {
            Warn($"NativeTowns.{method.Name} failed: {(e.InnerException ?? e).Message}");
            return null;
        }
    }

    private static void Warn(string why)
    {
        if (_warned) return;
        _warned = true;
        GD.PushWarning($"Native towns: {why}; using primitive stand-ins (NativeTownsBridge).");
    }

    /// <summary>A town of this level (size 1..3), coloured for the species; a primitive stand-in without NativeTowns.</summary>
    public static Node3D Build(CivLevel level, int variant, Color speciesColour, int size) =>
        Call(BuildMethod, level, variant, speciesColour, size) as Node3D ?? StandIn(level, speciesColour, size, false);

    /// <summary>The cheap far stand-in of a town of this level.</summary>
    public static Node3D Far(CivLevel level, Color colour) =>
        Call(FarMethod, level, colour) as Node3D ?? StandIn(level, colour, 1, true);

    /// <summary>A species' colour from its seed.</summary>
    public static Color SpeciesColour(int seed)
    {
        if (Call(ColourMethod, seed) is Color c) return c;
        var h = (uint)seed * 2654435761u;
        return Color.FromHsv((h % 1000u) / 1000f, 0.35f + (h >> 10) % 100u / 400f, 0.7f + (h >> 20) % 100u / 500f);
    }

    /// <summary>0 = day, 1 = night: the town's lights come on (CityLight).</summary>
    public static void SetNight(Node3D town, float night)
    {
        if (NightMethod != null && !town.HasMeta(StandInMeta))
        {
            Call(NightMethod, town, night);
            return;
        }
        if (town.HasMeta(StandInMeta) && town.GetMeta(StandInMeta).AsGodotObject() is StandardMaterial3D lights)
            lights.EmissionEnergyMultiplier = 0.15f + 3.2f * Mathf.Clamp(night, 0f, 1f);
    }

    private const string StandInMeta = "civ_stand_in_lights";

    /// <summary>Primitive huts / blocks / towers by level (≈ 20 across at size 1, like the contract's towns), with a lit
    /// window material for L3+ that SetNight drives.</summary>
    private static Node3D StandIn(CivLevel level, Color colour, int size, bool far)
    {
        var n = Mathf.Clamp((int)level, 1, 6);
        var root = new Node3D { Name = $"Town_L{n}{(far ? "_far" : "")}" };
        var body = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f };
        var trim = new StandardMaterial3D { AlbedoColor = colour.Darkened(0.45f), Roughness = 0.85f };
        var lights = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.18f, 0.12f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.78f, 0.42f),
            EmissionEnergyMultiplier = n >= 3 ? 0.15f : 0f,
        };
        root.SetMeta(StandInMeta, lights);
        var rng = new RandomNumberGenerator { Seed = (ulong)(n * 7919 + size * 104729) };
        var count = far ? 3 : 5 + 3 * Mathf.Clamp(size, 1, 3);
        var spread = 7f * Mathf.Sqrt(Mathf.Clamp(size, 1, 3));
        for (var i = 0; i < count; i++)
        {
            var a = rng.RandfRange(0f, Mathf.Tau);
            var r = Mathf.Sqrt(rng.Randf()) * spread;
            var at = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Mesh mesh;
            float h;
            switch (n)
            {
                case 1:
                    h = rng.RandfRange(1.6f, 2.4f);
                    mesh = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 1.4f, Height = h, RadialSegments = 6 }; // huts
                    break;
                case 2:
                    h = rng.RandfRange(2f, 4f);
                    mesh = new BoxMesh { Size = new Vector3(2.4f, h, 2.4f) };
                    break;
                case 3 or 4:
                    h = rng.RandfRange(3f, n == 3 ? 6f : 11f);
                    mesh = new BoxMesh { Size = new Vector3(rng.RandfRange(2f, 3.4f), h, rng.RandfRange(2f, 3.4f)) };
                    break;
                default:
                    h = rng.RandfRange(8f, n == 5 ? 16f : 26f);
                    mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1.6f, Height = h, RadialSegments = 8 }; // towers / arcologies
                    break;
            }
            root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = i % 3 == 2 ? trim : body, Position = at + Vector3.Up * h / 2f });
            if (n >= 3 && !far)
                root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, h * 0.6f, 0.5f) }, MaterialOverride = lights, Position = at + new Vector3(0.9f, h * 0.45f, 0.9f) });
        }
        if (n == 3 && !far)
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.6f, Height = 9f, RadialSegments = 6 }, MaterialOverride = trim, Position = new Vector3(2f, 4.5f, -2f) }); // a smokestack
        return root;
    }
}
