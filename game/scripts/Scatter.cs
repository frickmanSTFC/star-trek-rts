using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The landscape scatter models (blender/build_scatter.py → models/scatter/scatter.glb): one mesh per named node
/// (conifer_a, broadleaf_a, grass_b, boulder_a, …, and cheaper "&lt;name&gt;_far" stand-ins) plus the foliage atlas
/// taken from the "Foliage" material. A MultiMesh needs a Mesh resource, so the meshes are lifted out of the imported
/// scene, which is then freed. If the glb is missing or Godot hasn't imported it yet, <see cref="Get"/> returns
/// primitive stand-ins (cones, squashed spheres) for the trees, bushes and rocks and null for the rest.
/// </summary>
public sealed class ScatterAssets
{
    public const string Path = "res://models/scatter/scatter.glb";

    private readonly Dictionary<string, Mesh> _meshes = new();
    private readonly Dictionary<string, Mesh> _fallback = new();

    /// <summary>The foliage atlas (needles, leaves, grass, twigs, bark, speckle), or null when not imported.</summary>
    public Texture2D? Atlas { get; private set; }

    /// <summary>True when the glb was found and held meshes.</summary>
    public bool Imported => _meshes.Count > 0;

    public static ScatterAssets Load()
    {
        var assets = new ScatterAssets();
        if (ResourceLoader.Exists(Path) && GD.Load<PackedScene>(Path)?.Instantiate() is Node root)
        {
            assets.Collect(root);
            root.Free();
        }
        return assets;
    }

    private void Collect(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            _meshes[mi.Name.ToString()] = mi.Mesh;
            if (Atlas == null && mi.Mesh.GetSurfaceCount() > 0 && mi.GetActiveMaterial(0) is BaseMaterial3D m && m.AlbedoTexture != null)
                Atlas = m.AlbedoTexture;
        }
        foreach (var child in node.GetChildren()) Collect(child);
    }

    /// <summary>The imported mesh, or null.</summary>
    public Mesh? Model(string name) => _meshes.GetValueOrDefault(name);

    /// <summary>The imported mesh, else a primitive stand-in for the kind of thing it is (null for grass, ferns,
    /// pebbles and far meshes, which are simply left out without the glb).</summary>
    public Mesh? Get(string name) => Model(name) ?? Fallback(name);

    /// <summary>True when <see cref="Get"/> would return a primitive (instance colours then carry the whole colour).</summary>
    public bool IsFallback(string name) => !_meshes.ContainsKey(name) && Fallback(name) != null;

    private Mesh? Fallback(string name)
    {
        if (_fallback.TryGetValue(name, out var made)) return made;
        Mesh? mesh = name switch
        {
            "conifer_a" or "conifer_b" or "conifer_c" or "pine_a" =>
                Raised(new CylinderMesh { TopRadius = 0f, BottomRadius = 1.6f, Height = 7f, RadialSegments = 7, Rings = 1 }, 3.5f),
            "broadleaf_a" or "broadleaf_b" => Raised(new SphereMesh { Radius = 2.6f, Height = 4.6f, RadialSegments = 8, Rings = 4 }, 4.6f),
            "bush_a" or "bush_b" or "shrub_desert" => Raised(new SphereMesh { Radius = 0.9f, Height = 1.2f, RadialSegments = 6, Rings = 3 }, 0.5f),
            "boulder_a" or "boulder_b" or "boulder_c" or "rock_cluster" or "sandstone_a" or "sandstone_b" or "ice_a" or "ice_b" =>
                Raised(new SphereMesh { Radius = 1f, Height = 1.4f, RadialSegments = 6, Rings = 3 }, 0.35f),
            _ => null,
        };
        if (mesh != null) _fallback[name] = mesh;
        return mesh;
    }

    /// <summary>A primitive's triangles moved up by <paramref name="dy"/> so its foot sits at the origin.</summary>
    private static Mesh Raised(PrimitiveMesh primitive, float dy)
    {
        var arrays = primitive.GetMeshArrays();
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for (var i = 0; i < verts.Length; i++) verts[i].Y += dy;
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
