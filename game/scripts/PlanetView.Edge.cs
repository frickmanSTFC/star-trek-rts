using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

// The landscape's edge (owner 2026-11-17: "When you zoom out, the round world becomes a simple rectangle. We need to
// clamp that so you don't see the end of the landscape."). Render-only. The camera's limits are in
// PlanetDriver.ViewClamp.cs; this is what they are measured against, plus two things that make the edge clean:
//
// - Edge tiles. The hex mesh's own edges are a sawtooth (rows staggered by half a hex west and east, pointed tops north
//   and south), so a straight line either cuts off part of the outer tiles or shows the gaps between them. One ring of
//   extra tiles goes round the map: each a flat copy of the map tile next to it (its colour, the corner heights where
//   they meet, its water), drawn with the terrain's and the water's own materials so fog of war, daylight, weather and
//   haze match. They are scenery: no sim tile, no scatter, never picked (TileAt ignores them). The camera's stop lines
//   (EdgeWest … EdgeSouth) lie a little outside the real tiles and well inside the ring, so every real tile can be seen
//   whole and a small error in the clamp shows more ground, not a gap.
// - The apron. One very large dark plane well below the sea bed. Up close the camera looks toward the horizon and the
//   clamp can only keep the map's edge behind the haze (ViewClamp's haze cap); the apron gives that haze something to
//   lie on, so the far edge fades into dark, unexplored-looking ground instead of ending in a hard black band. It is
//   never on screen from the strategy view (the clamp is exact there), and inside the map it is under everything.
public partial class PlanetView
{
    private const float ApronDepth = -6f;       // below the deepest sea bed (-4.5)
    private const float ApronSize = 30000f;     // past the camera's far plane (14000) from anywhere on the map
    private const float CragAllowance = 8f;     // mountains' crags and the corner bumps above a tile's centre height
    private const float EdgeSlackX = 3f;        // the stop lines: this far outside the real tiles, west and east…
    private const float EdgeSlackZ = 2f;        // …and north and south (the ring is solid for another ~9 and ~12 units)

    /// <summary>The lines the camera's view stops at (world x / z): just outside the real tiles, inside the ring of
    /// edge tiles.</summary>
    public float EdgeWest { get; private set; }
    public float EdgeEast { get; private set; }
    public float EdgeNorth { get; private set; }
    public float EdgeSouth { get; private set; }

    /// <summary>The lowest ground (or the sea) along each edge of the map (0 west, 1 east, 2 north, 3 south): the
    /// farthest a ray can run before it lands there.</summary>
    public float EdgeBottom(int side) => _edgeBottom[side];

    /// <summary>The highest ground anywhere on the map, crags included.</summary>
    public float HighestGround { get; private set; }

    private readonly float[] _edgeBottom = new float[4];

    /// <summary>After BuildWater: the edge tiles, the apron and the numbers above.</summary>
    private void BuildEdge()
    {
        var g = _surface.Map.Grid;
        int w = g.Width, h = g.Height;

        // Real tiles reach from half a hex west of the first column's even-row centres to a full hex east of the last
        // column's (the odd rows), and a hex radius north and south of the first and last rows' centres (their points).
        var first = FlatCentre(0);
        var last = FlatCentre(g.Count - 1);
        EdgeWest = first.X - HexWidth * 0.5f - EdgeSlackX;
        EdgeEast = first.X + w * HexWidth + EdgeSlackX;
        EdgeNorth = first.Z - HexRadius - EdgeSlackZ;
        EdgeSouth = last.Z + HexRadius + EdgeSlackZ;

        // The lowest ground along each edge, and the highest anywhere (crags included).
        for (var i = 0; i < 4; i++) _edgeBottom[i] = float.MaxValue;
        for (var t = 0; t < g.Count; t++)
        {
            var y = TopY(t);
            HighestGround = Mathf.Max(HighestGround, y + CragAllowance);
            int c = g.Column(t), r = g.Row(t);
            if (c == 0) _edgeBottom[0] = Mathf.Min(_edgeBottom[0], y);
            if (c == w - 1) _edgeBottom[1] = Mathf.Min(_edgeBottom[1], y);
            if (r == 0) _edgeBottom[2] = Mathf.Min(_edgeBottom[2], y);
            if (r == h - 1) _edgeBottom[3] = Mathf.Min(_edgeBottom[3], y);
        }

        BuildEdgeTiles();

        var apron = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.016f, 0.02f, 0.022f), // about what unexplored ground comes to (albedo × 0.08)
            Roughness = 1f,
            MetallicSpecular = 0f,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Apron",
            Mesh = new PlaneMesh { Size = new Vector2(ApronSize, ApronSize), SubdivideWidth = 48, SubdivideDepth = 48 },
            MaterialOverride = apron,
            Position = new Vector3((EdgeWest + EdgeEast) * 0.5f, ApronDepth, (EdgeNorth + EdgeSouth) * 0.5f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        });
    }

    /// <summary>A tile's centre on the ground plane (y 0).</summary>
    private Vector3 FlatCentre(int tile)
    {
        var c = _surface.Map.Grid.Centre(tile);
        return new Vector3(c.X.ToFloat() * HexWidth, 0, c.Y.ToFloat() * HexWidth);
    }

    /// <summary>One ring of flat tiles round the map, in the terrain's (and the water's) own material.</summary>
    private void BuildEdgeTiles()
    {
        var map = _surface.Map;
        var g = map.Grid;
        int w = g.Width, h = g.Height;
        ShaderMaterial? terrainMaterial = null, waterMaterial = null;
        foreach (var child in GetChildren())
        {
            if (child is not MeshInstance3D { MaterialOverride: ShaderMaterial m } || m.Shader == null) continue;
            var path = m.Shader.ResourcePath;
            if (terrainMaterial == null && path.EndsWith("terrain.gdshader")) terrainMaterial = m;
            else if (waterMaterial == null && path.EndsWith("water.gdshader")) waterMaterial = m;
        }
        if (terrainMaterial == null) return;

        var origin = FlatCentre(0);
        var rowStep = h > 1 ? FlatCentre(g.Index(0, 1)).Z - origin.Z : 0.8660254f * HexWidth;

        // Corner heights of the map's outer tiles, by position: where an edge tile meets them it takes theirs.
        var known = new Dictionary<long, float>();
        for (var t = 0; t < g.Count; t++)
        {
            int c = g.Column(t), r = g.Row(t);
            if (c != 0 && c != w - 1 && r != 0 && r != h - 1) continue;
            var centre = FlatCentre(t);
            for (var k = 0; k < 6; k++) known[CornerKey(centre + Corner(k))] = _cornerHeight[t * 6 + k];
        }

        var land = new SurfaceTool();
        land.Begin(Mesh.PrimitiveType.Triangles);
        var sea = new SurfaceTool();
        sea.Begin(Mesh.PrimitiveType.Triangles);
        int landIndex = 0, seaIndex = 0;
        var pos = new Vector3[7];
        var normals = new Vector3[7];
        for (var r = -1; r <= h; r++)
            for (var c = -1; c <= w; c++)
            {
                if (g.InBounds(c, r)) continue;
                // Where the grid would put column c of row r (odd rows sit half a hex east), and the map tile next to it.
                var centre = new Vector3(origin.X + (c + ((r & 1) == 1 ? 0.5f : 0f)) * HexWidth, 0, origin.Z + r * rowStep);
                var source = g.Index(Mathf.Clamp(c, 0, w - 1), Mathf.Clamp(r, 0, h - 1));

                var sum = 0f;
                var found = 0;
                for (var k = 0; k < 6; k++)
                {
                    var p = centre + Corner(k);
                    if (known.TryGetValue(CornerKey(p), out var y))
                    {
                        pos[1 + k] = p with { Y = y };
                        sum += y;
                        found++;
                    }
                    else pos[1 + k] = p with { Y = float.NaN };
                }
                var level = found > 0 ? sum / found : BaseHeight(source);
                for (var k = 0; k < 6; k++)
                    if (float.IsNaN(pos[1 + k].Y)) pos[1 + k].Y = level; // the corners on the far side: level with the rest
                pos[0] = centre with { Y = level };

                var terrain = map.Terrain(source);
                var water = map.IsWater(source);
                var colour = Look.TerrainColour(terrain.Id, terrain.Color).Lightened((map.Elevation[source].ToFloat() - 0.5f) * 0.15f);
                if (water) colour = Look.Seabed.Lerp(colour, 0.35f);
                var mountains = terrain.Id == "mountains";

                System.Array.Clear(normals);
                for (var k = 0; k < 6; k++)
                {
                    int a = 0, b = 1 + k, d = 1 + (k + 1) % 6;
                    var n = (pos[b] - pos[a]).Cross(pos[d] - pos[a]);
                    if (n.Y < 0) n = -n;
                    normals[a] += n;
                    normals[b] += n;
                    normals[d] += n;
                }
                for (var v = 0; v < 7; v++)
                {
                    var vertexColour = mountains ? RockColour(pos[v].Y, pos[v]) : colour;
                    land.SetColor(vertexColour.SrgbToLinear());
                    land.SetUV(new Vector2(v == 0 ? 0f : 1f, 0)); // 0 centre, 1 corners: the shader's hex outline, as on real tiles
                    land.SetNormal(normals[v].Normalized());
                    land.AddVertex(pos[v]);
                }
                for (var k = 0; k < 6; k++)
                {
                    land.AddIndex(landIndex);
                    land.AddIndex(landIndex + 1 + k);
                    land.AddIndex(landIndex + 1 + (k + 1) % 6);
                }
                landIndex += 7;

                if (!water || waterMaterial == null) continue;
                // The same water hex BuildWater makes (COLOR.r: 1 deep ocean, 0 shallows).
                var top = centre with { Y = SeaLevel };
                var depth = terrain.Id == "ocean" ? new Color(1, 0, 0) : new Color(0, 0, 0);
                sea.SetColor(depth);
                sea.SetNormal(Vector3.Up);
                sea.AddVertex(top);
                for (var k = 0; k < 6; k++)
                {
                    sea.SetColor(depth);
                    sea.SetNormal(Vector3.Up);
                    sea.AddVertex(top + Corner(k) * 1.04f);
                }
                for (var k = 0; k < 6; k++)
                {
                    sea.AddIndex(seaIndex);
                    sea.AddIndex(seaIndex + 1 + k);
                    sea.AddIndex(seaIndex + 1 + (k + 1) % 6);
                }
                seaIndex += 7;
            }
        if (landIndex > 0)
            AddChild(new MeshInstance3D { Name = "EdgeTiles", Mesh = land.Commit(), MaterialOverride = terrainMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (seaIndex > 0)
            AddChild(new MeshInstance3D { Name = "EdgeWater", Mesh = sea.Commit(), MaterialOverride = waterMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }
}
