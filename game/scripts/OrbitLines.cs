using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Thin line meshes for the helm viewscreen (orbit tracks, the globe's grid, the system plot's rings): ArrayMeshes
/// with the Lines / LineStrip primitives (one pixel wide), drawn unshaded and see-through. Render-only.
/// </summary>
public static class OrbitLines
{
    /// <summary>A circle round Y in the XZ plane (the plane TorusMesh and the globe's latitudes use). Dashed = every other segment.</summary>
    public static ArrayMesh Circle(float radius, int segments, bool dashed = false)
    {
        var points = new List<Vector3>();
        for (var i = 0; i <= segments; i++)
        {
            if (dashed && i == segments) break;
            var a = i / (float)segments * Mathf.Tau;
            var p = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            if (!dashed)
            {
                points.Add(p);
                continue;
            }
            var b = (i + 0.5f) / segments * Mathf.Tau;
            points.Add(p);
            points.Add(new Vector3(Mathf.Sin(b) * radius, 0f, Mathf.Cos(b) * radius));
        }
        return Build(points, dashed ? Mesh.PrimitiveType.Lines : Mesh.PrimitiveType.LineStrip);
    }

    /// <summary>Separate segments: points taken in pairs.</summary>
    public static ArrayMesh Segments(List<Vector3> pairs) => Build(pairs, Mesh.PrimitiveType.Lines);

    private static ArrayMesh Build(List<Vector3> points, Mesh.PrimitiveType primitive)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(primitive, arrays);
        return mesh;
    }

    /// <summary>Unshaded, see-through, not lit and casting no shadow. Priority orders it among the clouds (1, 2) and the air (10).</summary>
    public static StandardMaterial3D Material(Color colour, int priority = 0) => new()
    {
        AlbedoColor = colour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        RenderPriority = priority,
    };

    public static MeshInstance3D Instance(ArrayMesh mesh, Color colour, int priority = 0) => new()
    {
        Mesh = mesh,
        MaterialOverride = Material(colour, priority),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
