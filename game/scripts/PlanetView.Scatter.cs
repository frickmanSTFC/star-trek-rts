using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Landscape scatter (render-only randomness, never the sim's RNG): trees, bushes, ferns, grass, boulders, scree,
/// sandstone and ice from <see cref="ScatterAssets"/>, placed per terrain by <see cref="ScatterRules"/> on the exact
/// terrain surface, and drawn as one MultiMesh per model per 8×8-tile chunk (frustum culling, visibility ranges).
/// Trees and big rocks swap to a cheap far mesh at 330–400 units with a per-pixel dither (lod_band in the shaders,
/// measured from the game camera); bushes, scree and grass simply fade or sink away. Nothing grows on water; trees,
/// rocks and bushes keep the middle of buildable tiles clear, and every instance near a building (the whole site of
/// a colony core and its repair pad, and a landing site from the moment the pod descends) is hidden.
/// </summary>
public partial class PlanetView
{
    private enum ScatterKind { Tree, Plant, Grass, Rock, Small }

    /// <summary>One kind of scatter on one terrain.</summary>
    /// <param name="Assets">Model names, picked at random (repeat a name to weight it).</param>
    /// <param name="PerTile">Average number per tile (placed within the tile's circumcircle, so borders blend).</param>
    /// <param name="Tints">sRGB multipliers on the model's own colours, one picked per instance (then ±8%).</param>
    /// <param name="Clump">0 = even; higher = follows a large-scale noise more (groves, meadows, bare patches).</param>
    /// <param name="Snow">Snow cover on rocks (rock.gdshader); mountains add more above the snow line.</param>
    /// <param name="Moss">Moss on rocks' up-facing faces.</param>
    /// <param name="MaxHeight">Highest ground it grows on (trees on the lower mountain slopes only).</param>
    /// <param name="MinUp">Steepest ground it stands on (the ground normal's Y).</param>
    /// <param name="Dry">Grass: how much the meadow noise turns it straw-coloured.</param>
    /// <param name="YScale">Squashes (below 1) the model's height: class L's lichen mats are flattened grass tufts and
    /// fern rosettes, its shrubs low bushes.</param>
    private sealed record ScatterRule(ScatterKind Kind, string[] Assets, float PerTile, float MinScale, float MaxScale, Color[] Tints,
        float Clump = 0f, float Snow = 0f, float Moss = 0f, float MaxHeight = 1e9f, float MinUp = 0f, float Dry = 0f, float YScale = 1f);

    private static readonly Color[] TreeTints = { new(1f, 1f, 1f), new(0.9f, 1f, 0.88f), new(1.06f, 1f, 0.86f), new(0.86f, 0.95f, 0.97f), new(0.96f, 0.93f, 0.8f) };
    private static readonly Color[] GrassTints = { new(1f, 1f, 1f), new(0.92f, 1.02f, 0.9f), new(1.08f, 1.05f, 0.82f) };
    private static readonly Color[] ForestFloorTints = { new(0.78f, 0.88f, 0.76f), new(0.88f, 0.94f, 0.8f) };
    private static readonly Color[] HillGrassTints = { new(1.15f, 1.05f, 0.76f), new(1.28f, 1.1f, 0.68f), new(1f, 1f, 0.9f) };
    private static readonly Color[] DryGrassTints = { new(1.35f, 1.12f, 0.68f), new(1.45f, 1.2f, 0.76f) };
    private static readonly Color[] SageTints = { new(1f, 1f, 1f), new(1.1f, 1f, 0.84f), new(1.22f, 1.02f, 0.74f) };
    private static readonly Color[] ForestStoneTints = { new(0.84f, 0.9f, 0.84f) };
    private static readonly Color[] HillStoneTints = { new(1.06f, 0.98f, 0.88f), new(0.96f, 0.95f, 0.94f) };
    private static readonly Color[] MountainStoneTints = { new(0.92f, 0.93f, 0.96f), new(1f, 0.98f, 0.95f) };
    private static readonly Color[] SandstoneTints = { new(1f, 1f, 1f), new(1.06f, 0.97f, 0.9f), new(0.95f, 0.9f, 0.88f) };
    private static readonly Color[] DesertStoneTints = { new(1.22f, 1f, 0.8f), new(1.14f, 0.96f, 0.8f) };
    private static readonly Color[] IceTints = { new(1f, 1f, 1f), new(0.93f, 0.97f, 1.03f) };
    private static readonly Color DryGrass = new(1.3f, 1.1f, 0.66f);

    /// <summary>What grows or lies on each land terrain (none on ocean / shallows). Budget on the 128×88 Class M map
    /// (seed 1701, before the slope / height filters): ~28k trees, ~150k grass clumps, ~36k bushes/ferns/shrubs,
    /// ~18k rocks, ~9k scree/pebble patches, plus ~46k far copies of the trees and rocks.</summary>
    private static readonly Dictionary<string, ScatterRule[]> ScatterRules = new()
    {
        ["forest"] = new ScatterRule[]
        {
            new(ScatterKind.Tree, new[] { "conifer_a", "conifer_a", "conifer_a", "conifer_a", "conifer_b", "conifer_b", "conifer_c", "conifer_c", "conifer_c" }, 17f, 0.75f, 1.3f, TreeTints, Clump: 0.8f),
            new(ScatterKind.Tree, new[] { "broadleaf_b" }, 0.6f, 0.8f, 1.1f, TreeTints, Clump: 2f),
            new(ScatterKind.Plant, new[] { "bush_b", "bush_b", "bush_a" }, 3f, 0.6f, 1.2f, TreeTints),
            new(ScatterKind.Plant, new[] { "fern" }, 12f, 0.7f, 1.3f, ForestFloorTints, Clump: 1f),
            new(ScatterKind.Grass, new[] { "grass_a", "grass_c" }, 8f, 0.7f, 1.2f, ForestFloorTints, Clump: 1f),
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_b" }, 0.4f, 0.5f, 1.3f, ForestStoneTints, Moss: 0.75f),
        },
        ["plains"] = new ScatterRule[]
        {
            new(ScatterKind.Tree, new[] { "broadleaf_a", "broadleaf_a", "broadleaf_b" }, 1.4f, 0.75f, 1.25f, TreeTints, Clump: 2.2f),
            new(ScatterKind.Plant, new[] { "bush_a", "bush_a", "bush_a", "bush_b" }, 3.5f, 0.6f, 1.2f, TreeTints, Clump: 1.2f),
            new(ScatterKind.Grass, new[] { "grass_a", "grass_a", "grass_b", "grass_c" }, 55f, 0.8f, 1.5f, GrassTints, Clump: 1f, Dry: 0.55f),
            new(ScatterKind.Small, new[] { "pebbles" }, 0.6f, 0.8f, 1.3f, HillStoneTints),
            new(ScatterKind.Rock, new[] { "boulder_c", "boulder_a" }, 0.3f, 0.4f, 0.9f, HillStoneTints, Moss: 0.2f),
        },
        ["hills"] = new ScatterRule[]
        {
            new(ScatterKind.Tree, new[] { "pine_a" }, 0.8f, 0.8f, 1.15f, TreeTints, Clump: 1.5f, MinUp: 0.8f),
            new(ScatterKind.Tree, new[] { "conifer_c" }, 0.4f, 0.7f, 1.1f, TreeTints, Clump: 1.5f, MinUp: 0.8f),
            new(ScatterKind.Plant, new[] { "bush_b", "bush_a" }, 2f, 0.6f, 1f, TreeTints),
            new(ScatterKind.Grass, new[] { "grass_a", "grass_c" }, 22f, 0.7f, 1.3f, HillGrassTints, Clump: 1f, Dry: 0.3f),
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_b", "boulder_c" }, 4f, 0.6f, 2.2f, HillStoneTints, Clump: 0.8f, Moss: 0.15f),
            new(ScatterKind.Rock, new[] { "rock_cluster" }, 0.8f, 0.7f, 1.3f, HillStoneTints, Moss: 0.1f),
            new(ScatterKind.Small, new[] { "scree" }, 1.5f, 0.8f, 1.4f, HillStoneTints),
        },
        ["desert"] = new ScatterRule[]
        {
            new(ScatterKind.Rock, new[] { "sandstone_a", "sandstone_a", "sandstone_b" }, 1.5f, 0.7f, 2.4f, SandstoneTints, Clump: 1f),
            new(ScatterKind.Rock, new[] { "boulder_b" }, 0.4f, 0.5f, 1.4f, DesertStoneTints),
            new(ScatterKind.Plant, new[] { "shrub_desert" }, 6f, 0.6f, 1.3f, SageTints, Clump: 1.2f),
            new(ScatterKind.Plant, new[] { "snag" }, 0.25f, 0.7f, 1.1f, new[] { new Color(1f, 1f, 1f) }),
            new(ScatterKind.Grass, new[] { "grass_c" }, 5f, 0.6f, 1.1f, DryGrassTints, Clump: 1f),
            new(ScatterKind.Small, new[] { "pebbles" }, 2f, 0.8f, 1.4f, DesertStoneTints),
        },
        ["mountains"] = new ScatterRule[]
        {
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_b", "boulder_c" }, 6f, 0.8f, 3.2f, MountainStoneTints, Clump: 0.6f),
            new(ScatterKind.Rock, new[] { "rock_cluster" }, 1.2f, 0.8f, 1.8f, MountainStoneTints),
            new(ScatterKind.Small, new[] { "scree" }, 3f, 1f, 2f, MountainStoneTints),
            new(ScatterKind.Tree, new[] { "conifer_c", "pine_a" }, 1f, 0.7f, 1.1f, TreeTints, MaxHeight: 26f, MinUp: 0.7f),
        },
        ["ice"] = new ScatterRule[]
        {
            new(ScatterKind.Rock, new[] { "ice_a", "ice_b" }, 3f, 0.6f, 1.8f, IceTints, Clump: 1f),
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_c" }, 1.5f, 0.6f, 1.6f, MountainStoneTints, Snow: 0.85f),
            new(ScatterKind.Small, new[] { "scree" }, 1f, 0.8f, 1.4f, MountainStoneTints, Snow: 0.7f),
        },
    };

    // Class L (Veyra): cold tundra with few or no trees. The same scatter.glb models, tinted, squashed and thinned out.
    private static readonly Color[] LShrubTints = { new(1.45f, 0.95f, 0.7f), new(1.3f, 1f, 0.76f), new(1.6f, 1.05f, 0.7f) };   // olive-brown, rusty
    private static readonly Color[] LichenTints = { new(1.6f, 1.15f, 1.55f), new(1.55f, 1.25f, 1.25f), new(1.85f, 1.35f, 1.15f) }; // pale grey-green, ochre
    private static readonly Color[] LStuntedTints = { new(0.95f, 0.85f, 0.75f), new(1.05f, 0.9f, 0.72f) };
    private static readonly Color[] TundraStoneTints = { new(1.05f, 0.97f, 0.86f), new(0.92f, 0.9f, 0.87f) };
    private static readonly Color[] RustStoneTints = { new(1.22f, 0.88f, 0.66f), new(1.12f, 0.86f, 0.7f), new(1f, 0.86f, 0.76f) };
    private static readonly Color[] CarvedTints = { new(1f, 0.82f, 0.7f), new(0.9f, 0.78f, 0.7f), new(0.8f, 0.74f, 0.7f) };       // wind-carved, rust-grey
    private static readonly Color[] BasaltTints = { new(0.55f, 0.53f, 0.53f), new(0.62f, 0.58f, 0.56f) };

    /// <summary>Class L's scatter by terrain role (PlanetLook.Role: tundra = plains, lichen lowlands = forest, rust hills,
    /// dust flats = desert, basalt = mountains, water ice): sparse low shrubs, lichen mats, wind-carved rocks (the
    /// sandstone models), frost-rimmed boulders (Snow = a frosty rim on top, the night frost adds more), and only a few
    /// stunted conifers in the damp lowlands. Roughly a quarter of class M's instance count.</summary>
    private static readonly Dictionary<string, ScatterRule[]> ScatterRulesL = new()
    {
        ["plains"] = new ScatterRule[]
        {
            new(ScatterKind.Plant, new[] { "bush_a", "bush_b" }, 1.6f, 0.3f, 0.55f, LShrubTints, Clump: 1.4f, YScale: 0.7f),
            new(ScatterKind.Grass, new[] { "grass_b", "grass_c" }, 16f, 1f, 2f, LichenTints, Clump: 1.2f, YScale: 0.2f),
            new(ScatterKind.Plant, new[] { "fern" }, 2f, 0.6f, 1f, LichenTints, Clump: 1f, YScale: 0.22f),
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_c" }, 0.7f, 0.4f, 1.1f, TundraStoneTints, Snow: 0.3f, Moss: 0.35f),
            new(ScatterKind.Rock, new[] { "sandstone_a", "sandstone_b" }, 0.35f, 0.5f, 1.4f, CarvedTints, Clump: 1.2f),
            new(ScatterKind.Small, new[] { "pebbles" }, 1.5f, 0.8f, 1.3f, TundraStoneTints),
        },
        ["forest"] = new ScatterRule[]
        {
            new(ScatterKind.Grass, new[] { "grass_b", "grass_c", "grass_a" }, 30f, 1f, 2.2f, LichenTints, Clump: 1f, YScale: 0.22f),
            new(ScatterKind.Plant, new[] { "fern" }, 5f, 0.6f, 1.1f, LichenTints, Clump: 1f, YScale: 0.25f),
            new(ScatterKind.Plant, new[] { "bush_b", "bush_a" }, 3f, 0.35f, 0.65f, LShrubTints, Clump: 1.2f, YScale: 0.75f),
            new(ScatterKind.Tree, new[] { "conifer_c" }, 0.15f, 0.35f, 0.55f, LStuntedTints, Clump: 2.5f),
            new(ScatterKind.Plant, new[] { "snag" }, 0.1f, 0.5f, 0.8f, new[] { new Color(0.9f, 0.85f, 0.8f) }),
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_b" }, 0.5f, 0.5f, 1.2f, TundraStoneTints, Snow: 0.2f, Moss: 0.7f),
        },
        ["hills"] = new ScatterRule[]
        {
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_b", "boulder_c" }, 3f, 0.6f, 2f, RustStoneTints, Clump: 0.8f, Snow: 0.25f, Moss: 0.15f),
            new(ScatterKind.Rock, new[] { "sandstone_a", "sandstone_b" }, 1.2f, 0.7f, 2.2f, CarvedTints, Clump: 1f),
            new(ScatterKind.Rock, new[] { "rock_cluster" }, 0.6f, 0.7f, 1.3f, RustStoneTints, Snow: 0.2f),
            new(ScatterKind.Small, new[] { "scree" }, 1.8f, 0.8f, 1.4f, RustStoneTints),
            new(ScatterKind.Plant, new[] { "bush_b" }, 0.8f, 0.3f, 0.5f, LShrubTints, Clump: 1.2f, MinUp: 0.8f, YScale: 0.7f),
            new(ScatterKind.Grass, new[] { "grass_b", "grass_c" }, 6f, 0.9f, 1.8f, LichenTints, Clump: 1f, YScale: 0.2f),
        },
        ["desert"] = new ScatterRule[]
        {
            new(ScatterKind.Rock, new[] { "sandstone_a", "sandstone_a", "sandstone_b" }, 1.4f, 0.6f, 2.6f, CarvedTints, Clump: 1f),
            new(ScatterKind.Small, new[] { "pebbles" }, 2.5f, 0.8f, 1.4f, RustStoneTints),
            new(ScatterKind.Plant, new[] { "shrub_desert" }, 1f, 0.4f, 0.8f, LShrubTints, Clump: 1.4f, YScale: 0.8f),
            new(ScatterKind.Rock, new[] { "boulder_b" }, 0.3f, 0.5f, 1.3f, RustStoneTints, Snow: 0.2f),
        },
        ["mountains"] = new ScatterRule[]
        {
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_b", "boulder_c" }, 6f, 0.8f, 3.2f, BasaltTints, Clump: 0.6f, Snow: 0.15f),
            new(ScatterKind.Rock, new[] { "rock_cluster" }, 1.2f, 0.8f, 1.8f, BasaltTints),
            new(ScatterKind.Small, new[] { "scree" }, 3f, 1f, 2f, BasaltTints),
        },
        ["ice"] = new ScatterRule[]
        {
            new(ScatterKind.Rock, new[] { "ice_a", "ice_b" }, 3f, 0.6f, 1.8f, IceTints, Clump: 1f),
            new(ScatterKind.Rock, new[] { "boulder_a", "boulder_c" }, 1.2f, 0.6f, 1.6f, BasaltTints, Snow: 0.85f),
            new(ScatterKind.Small, new[] { "scree" }, 1f, 0.8f, 1.4f, BasaltTints, Snow: 0.7f),
        },
    };

    /// <summary>The scatter for a terrain on this world (by its class-M role on other classes), or null.</summary>
    private ScatterRule[]? RulesFor(string terrainId)
    {
        var set = Look.ScatterSet == "class_l" ? ScatterRulesL : ScatterRules;
        return set.TryGetValue(Look.Role(terrainId), out var rules) ? rules : null;
    }

    /// <summary>Cheaper stand-ins for distant trees and rocks (scaled per instance to the near model's size).</summary>
    private static readonly Dictionary<string, string> ScatterFar = new()
    {
        ["conifer_a"] = "conifer_a_far", ["conifer_b"] = "conifer_a_far", ["conifer_c"] = "conifer_a_far",
        ["pine_a"] = "pine_a_far",
        ["broadleaf_a"] = "broadleaf_a_far", ["broadleaf_b"] = "broadleaf_a_far",
        ["boulder_a"] = "boulder_a_far", ["boulder_b"] = "boulder_a_far", ["boulder_c"] = "boulder_a_far",
        ["rock_cluster"] = "rock_cluster_far",
        ["sandstone_a"] = "sandstone_a_far", ["sandstone_b"] = "sandstone_a_far",
        ["ice_a"] = "ice_a_far", ["ice_b"] = "ice_a_far",
    };

    /// <summary>Colours (sRGB) for the primitive stand-ins used while the glb isn't imported.</summary>
    private static Color ScatterFallbackColour(string name) => name switch
    {
        "conifer_a" or "conifer_b" or "conifer_c" => new Color(0.15f, 0.3f, 0.15f),
        "pine_a" => new Color(0.2f, 0.32f, 0.18f),
        "broadleaf_a" or "broadleaf_b" => new Color(0.24f, 0.4f, 0.17f),
        "bush_a" or "bush_b" => new Color(0.24f, 0.37f, 0.19f),
        "shrub_desert" => new Color(0.45f, 0.47f, 0.36f),
        "sandstone_a" or "sandstone_b" => new Color(0.7f, 0.5f, 0.36f),
        "ice_a" or "ice_b" => new Color(0.82f, 0.9f, 0.96f),
        _ => new Color(0.45f, 0.43f, 0.4f),
    };

    private const int ChunkTiles = 8;
    private const float ChunkReach = 170f; // chunk centre to its farthest instance (half diagonal ~132) plus height spread
    private const float FarFadeStart = 330f, FarFadeEnd = 400f;
    private const float ClearRadius = 5.5f; // middle of a buildable tile kept free of trees, rocks and bushes

    private sealed class ScatterBatch
    {
        public required MultiMesh Multi;
        public required Vector3[] World; // instance origins in world space (for clearings)
        public required Transform3D[] Local; // instance transforms as built (restored when a clearing goes)
    }

    private sealed class ScatterBuilder
    {
        public required string Mesh;
        public required ScatterKind Kind;
        public required bool Far;
        public required int Chunk;
        public readonly List<Transform3D> World = new();
        public readonly List<Color> Colours = new();
        public readonly List<Color> Custom = new();
    }

    private ScatterAssets _scatterAssets = null!;
    private Node3D? _scatterRoot;
    private List<ScatterBatch>[] _scatterChunks = System.Array.Empty<List<ScatterBatch>>();
    private int _chunkColumns, _chunkRows;
    private readonly List<ShaderMaterial> _scatterMaterials = new();
    private readonly List<(ScatterBatch Batch, int Index)> _scatterHidden = new();
    private long _clearingSignature = long.MinValue;
    private Vector3 _scatterEye = new(float.MaxValue, 0, 0);
    private bool? _bufferLayoutOk;

    /// <summary>Instances per model after building (for the report / debugging).</summary>
    public Dictionary<string, int> ScatterCounts { get; } = new();

    private float ChunkWidth => ChunkTiles * HexWidth;
    private float ChunkDepth => ChunkTiles * 0.866f * HexWidth;

    private void BuildScatter()
    {
        _scatterAssets = ScatterAssets.Load();
        _scatterRoot = new Node3D { Name = "Scatter" };
        AddChild(_scatterRoot);

        var map = _surface.Map;
        var grid = map.Grid;
        _chunkColumns = Mathf.CeilToInt((grid.Width + 1) * HexWidth / ChunkWidth);
        _chunkRows = Mathf.CeilToInt((grid.Height + 1) * 0.866f * HexWidth / ChunkDepth);
        _scatterChunks = new List<ScatterBatch>[_chunkColumns * _chunkRows];
        for (var i = 0; i < _scatterChunks.Length; i++) _scatterChunks[i] = new List<ScatterBatch>();

        var rng = new RandomNumberGenerator { Seed = 4242UL }; // render-only, the same scatter every run
        var groves = new FastNoiseLite { Seed = 4243, Frequency = 0.006f, FractalOctaves = 3 };
        var meadow = new FastNoiseLite { Seed = 4244, Frequency = 0.02f, FractalOctaves = 2 };
        var builders = new Dictionary<(int, string), ScatterBuilder>();
        var farScale = new Dictionary<string, Vector3>();

        for (var t = 0; t < grid.Count; t++)
        {
            var terrain = map.Terrain(t);
            if (map.IsWater(t) || RulesFor(terrain.Id) is not { } rules) continue;
            var centre = TileCentre(t) with { Y = 0 };
            foreach (var rule in rules)
            {
                var tries = rule.PerTile * (rule.Clump > 0 ? 2f : 1f);
                var n = (int)(tries * rng.RandfRange(0.75f, 1.25f) + rng.Randf());
                for (var i = 0; i < n; i++)
                {
                    var angle = rng.RandfRange(0, Mathf.Tau);
                    var r = Mathf.Sqrt(rng.Randf()) * HexRadius;
                    var p = centre + new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r);
                    if (rule.Clump > 0)
                    {
                        var g = groves.GetNoise2D(p.X, p.Z) * 0.5f + 0.5f;
                        if (rng.Randf() > Mathf.Clamp(0.5f + (g - 0.5f) * rule.Clump * 2f, 0.03f, 1f)) continue;
                    }
                    var tile = TileAt(p);
                    if (tile < 0 || map.IsWater(tile)) continue;
                    var tileCentre = TileCentre(tile) with { Y = 0 };
                    var local = p - tileCentre;
                    var keepsClear = rule.Kind is ScatterKind.Tree or ScatterKind.Rock or ScatterKind.Plant;
                    if (keepsClear && map.Terrain(tile).Buildable && local.Length() < ClearRadius) continue;
                    var (y, normal) = SurfaceAt(tile, local);
                    if (y < SeaLevel + 0.25f || y > rule.MaxHeight || normal.Y < rule.MinUp) continue;

                    var name = rule.Assets[rng.RandiRange(0, rule.Assets.Length - 1)];
                    var mesh = _scatterAssets.Get(name);
                    if (mesh == null) continue;
                    var s = Mathf.Lerp(rule.MinScale, rule.MaxScale, Mathf.Pow(rng.Randf(), 1.4f)); // more small ones
                    var yaw = rng.RandfRange(0, Mathf.Tau);
                    Basis basis;
                    float sink;
                    switch (rule.Kind)
                    {
                        case ScatterKind.Tree:
                        case ScatterKind.Plant:
                            basis = new Basis(Vector3.Up, yaw) * Basis.FromScale(new Vector3(s, s * rng.RandfRange(0.9f, 1.15f) * rule.YScale, s));
                            sink = 0.1f * s;
                            break;
                        case ScatterKind.Grass:
                            basis = Lean(normal, 0.5f, yaw, new Vector3(s, s * rng.RandfRange(0.85f, 1.2f) * rule.YScale, s));
                            sink = 0.03f;
                            break;
                        default:
                            var tilt = new Vector3(rng.RandfRange(-0.2f, 0.2f), 0, rng.RandfRange(-0.2f, 0.2f));
                            basis = Lean((normal + tilt).Normalized(), 0.85f, yaw,
                                new Vector3(s * rng.RandfRange(0.85f, 1.15f), s * rng.RandfRange(0.8f, 1.2f) * rule.YScale, s * rng.RandfRange(0.85f, 1.15f)));
                            sink = 0.06f * s;
                            break;
                    }
                    var world = new Transform3D(basis, p with { Y = y - sink });

                    var tint = rule.Tints[rng.RandiRange(0, rule.Tints.Length - 1)] * rng.RandfRange(0.92f, 1.08f);
                    if (rule.Dry > 0) tint = tint.Lerp(DryGrass, Mathf.Clamp(meadow.GetNoise2D(p.X, p.Z) * 1.6f + 0.2f, 0f, 1f) * rule.Dry);
                    if (_scatterAssets.IsFallback(name)) tint *= ScatterFallbackColour(name);
                    tint = new Color(tint.R, tint.G, tint.B).SrgbToLinear();
                    var snow = rule.Snow;
                    if (Look.Role(terrain.Id) == "mountains" || Look.Role(map.Terrain(tile).Id) == "mountains") snow = Mathf.Max(snow, Mathf.SmoothStep(28f, 38f, y));
                    var custom = new Color(rng.Randf(), snow, rule.Moss, 0f);

                    var chunk = ChunkOf(p);
                    Add(builders, chunk, name, rule.Kind, false, world, tint, custom);
                    if (ScatterFar.TryGetValue(name, out var farName) && _scatterAssets.Model(farName) is { } farMesh && !_scatterAssets.IsFallback(name))
                    {
                        if (!farScale.TryGetValue(name, out var fs))
                        {
                            Vector3 a = mesh.GetAabb().Size, b = farMesh.GetAabb().Size;
                            var h = (a.X + a.Z) / Mathf.Max(0.01f, b.X + b.Z);
                            fs = farScale[name] = new Vector3(h, a.Y / Mathf.Max(0.01f, b.Y), h);
                        }
                        var farWorld = new Transform3D(basis * Basis.FromScale(fs), world.Origin);
                        Add(builders, chunk, farName, rule.Kind, true, farWorld, tint, custom);
                    }
                }
            }
        }

        foreach (var b in builders.Values) MakeBatch(b);
    }

    private static void Add(Dictionary<(int, string), ScatterBuilder> builders, int chunk, string mesh, ScatterKind kind, bool far,
        Transform3D world, Color colour, Color custom)
    {
        if (!builders.TryGetValue((chunk, mesh), out var b))
            builders[(chunk, mesh)] = b = new ScatterBuilder { Mesh = mesh, Kind = kind, Far = far, Chunk = chunk };
        b.World.Add(world);
        b.Colours.Add(colour);
        b.Custom.Add(custom);
    }

    /// <summary>Up axis leaned from straight up toward the ground normal by <paramref name="follow"/>, turned by yaw
    /// about it, then scaled (rotation × scale, no shear: foliage.gdshader relies on that).</summary>
    private static Basis Lean(Vector3 normal, float follow, float yaw, Vector3 scale)
    {
        var up = Vector3.Up.Lerp(normal, follow).Normalized();
        var tilt = up.IsEqualApprox(Vector3.Up) ? Basis.Identity : new Basis(new Quaternion(Vector3.Up, up));
        return tilt * new Basis(Vector3.Up, yaw) * Basis.FromScale(scale);
    }

    private int ChunkOf(Vector3 p)
    {
        var cx = Mathf.Clamp(Mathf.FloorToInt(p.X / ChunkWidth), 0, _chunkColumns - 1);
        var cz = Mathf.Clamp(Mathf.FloorToInt(p.Z / ChunkDepth), 0, _chunkRows - 1);
        return cz * _chunkColumns + cx;
    }

    private void MakeBatch(ScatterBuilder b)
    {
        var mesh = _scatterAssets.Get(b.Mesh);
        if (mesh == null || b.World.Count == 0) return;
        var count = b.World.Count;
        ScatterCounts[b.Mesh] = ScatterCounts.GetValueOrDefault(b.Mesh) + count;

        // The node sits at the chunk's centre (visibility ranges are measured to it), at the instances' mean height.
        var cx = b.Chunk % _chunkColumns;
        var cz = b.Chunk / _chunkColumns;
        var meanY = 0f;
        foreach (var w in b.World) meanY += w.Origin.Y;
        var origin = new Vector3((cx + 0.5f) * ChunkWidth, meanY / count, (cz + 0.5f) * ChunkDepth);

        var local = new Transform3D[count];
        var worldPos = new Vector3[count];
        const int stride = 20; // 12 transform (3 rows + origin) + 4 colour + 4 custom
        var buffer = new float[count * stride];
        var meshBox = mesh.GetAabb();
        var box = new Aabb();
        for (var i = 0; i < count; i++)
        {
            var t = new Transform3D(b.World[i].Basis, b.World[i].Origin - origin);
            local[i] = t;
            worldPos[i] = b.World[i].Origin;
            var o = i * stride;
            buffer[o + 0] = t.Basis.Row0.X; buffer[o + 1] = t.Basis.Row0.Y; buffer[o + 2] = t.Basis.Row0.Z; buffer[o + 3] = t.Origin.X;
            buffer[o + 4] = t.Basis.Row1.X; buffer[o + 5] = t.Basis.Row1.Y; buffer[o + 6] = t.Basis.Row1.Z; buffer[o + 7] = t.Origin.Y;
            buffer[o + 8] = t.Basis.Row2.X; buffer[o + 9] = t.Basis.Row2.Y; buffer[o + 10] = t.Basis.Row2.Z; buffer[o + 11] = t.Origin.Z;
            var c = b.Colours[i];
            buffer[o + 12] = c.R; buffer[o + 13] = c.G; buffer[o + 14] = c.B; buffer[o + 15] = c.A;
            var k = b.Custom[i];
            buffer[o + 16] = k.R; buffer[o + 17] = k.G; buffer[o + 18] = k.B; buffer[o + 19] = k.A;
            var instBox = t * meshBox;
            box = i == 0 ? instBox : box.Merge(instBox);
        }

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = mesh,
        };
        mm.InstanceCount = count;
        if (_bufferLayoutOk != false)
        {
            mm.Buffer = buffer;
            // Guard against a buffer layout mistake, checked on the first batch only (reading back costs a GPU
            // download): if one instance doesn't read back as written, set every instance one by one instead.
            static bool Near(Color x, Color y) => Mathf.Abs(x.R - y.R) + Mathf.Abs(x.G - y.G) + Mathf.Abs(x.B - y.B) + Mathf.Abs(x.A - y.A) < 0.02f;
            _bufferLayoutOk ??= mm.GetInstanceTransform(0).IsEqualApprox(local[0])
                && Near(mm.GetInstanceColor(0), b.Colours[0]) && Near(mm.GetInstanceCustomData(0), b.Custom[0]);
            if (_bufferLayoutOk == false) GD.PushWarning("Scatter: MultiMesh buffer layout differs; setting instances one by one.");
        }
        if (_bufferLayoutOk == false)
            for (var i = 0; i < count; i++)
            {
                mm.SetInstanceTransform(i, local[i]);
                mm.SetInstanceColor(i, b.Colours[i]);
                mm.SetInstanceCustomData(i, b.Custom[i]);
            }
        mm.CustomAabb = box.Grow(1.5f); // a little room for the wind

        var node = new MultiMeshInstance3D
        {
            Name = $"{b.Mesh}_{b.Chunk}",
            Multimesh = mm,
            Position = origin,
            MaterialOverride = ScatterMaterial(b.Mesh, b.Kind, b.Far, mesh),
            CastShadow = !b.Far && b.Kind is ScatterKind.Tree or ScatterKind.Rock or ScatterKind.Plant
                ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        // Chunk-level culling; the shaders do the exact per-pixel fade inside these ranges.
        var (begin, end) = b.Far ? (Mathf.Max(0f, FarFadeStart - ChunkReach), 0f) : (0f, NearFade(b.Kind).Y + ChunkReach);
        node.VisibilityRangeBegin = begin;
        node.VisibilityRangeEnd = end;
        _scatterRoot!.AddChild(node);
        _scatterChunks[b.Chunk].Add(new ScatterBatch { Multi = mm, World = worldPos, Local = local });
    }

    /// <summary>Distances from the camera over which each kind goes: near trees and rocks hand over to their far
    /// meshes, bushes and scree dither away, grass sinks into the ground.</summary>
    private static Vector2 NearFade(ScatterKind kind) => kind switch
    {
        ScatterKind.Tree or ScatterKind.Rock => new Vector2(FarFadeStart, FarFadeEnd),
        ScatterKind.Plant => new Vector2(300f, 360f),
        ScatterKind.Small => new Vector2(220f, 270f),
        _ => new Vector2(140f, 215f), // grass
    };

    private readonly Dictionary<string, ShaderMaterial> _scatterMaterialCache = new();

    private ShaderMaterial ScatterMaterial(string name, ScatterKind kind, bool far, Mesh mesh)
    {
        if (_scatterMaterialCache.TryGetValue(name, out var cached)) return cached;
        var rock = kind is ScatterKind.Rock or ScatterKind.Small;
        var m = FogMaterial(rock ? "res://shaders/rock.gdshader" : "res://shaders/foliage.gdshader");
        var nearFade = NearFade(kind);
        m.SetShaderParameter("lod_band", far ? new Vector4(FarFadeStart, FarFadeEnd, 1e9f, 2e9f)
            : kind == ScatterKind.Grass ? new Vector4(-2f, -1f, 1e9f, 2e9f) // grass sinks instead (shrink_band)
            : new Vector4(-2f, -1f, nearFade.X, nearFade.Y));
        if (rock)
        {
            if (!Look.IsDefault)
            {
                m.SetShaderParameter("moss_colour", Look.Moss); // class L: pale lichen
                m.SetShaderParameter("snow_colour", Look.RockSnow); // frost rims
            }
            if (name.StartsWith("ice"))
            {
                m.SetShaderParameter("roughness", 0.22f);
                m.SetShaderParameter("specular", 0.65f);
                m.SetShaderParameter("detail_scale", 1.4f);
            }
            else if (name.StartsWith("sandstone"))
            {
                m.SetShaderParameter("roughness", 0.92f);
                m.SetShaderParameter("specular", 0.2f);
                m.SetShaderParameter("detail_scale", 3.5f);
            }
        }
        else
        {
            var fallback = _scatterAssets.IsFallback(name);
            m.SetShaderParameter("use_atlas", _scatterAssets.Atlas != null && !fallback);
            if (_scatterAssets.Atlas != null) m.SetShaderParameter("atlas", _scatterAssets.Atlas);
            m.SetShaderParameter("sway_height", Mathf.Max(0.3f, mesh.GetAabb().End.Y));
            var grass = kind == ScatterKind.Grass;
            m.SetShaderParameter("sway", far || fallback ? 0.01f : grass ? 0.12f : kind == ScatterKind.Plant ? 0.05f : 0.025f);
            m.SetShaderParameter("flutter", far || fallback ? 0f : grass ? 0.05f : kind == ScatterKind.Plant ? 0.04f : 0.07f);
            m.SetShaderParameter("translucency", far ? 0.1f : grass ? 0.35f : 0.25f);
            m.SetShaderParameter("normal_up", grass ? 0.65f : name == "fern" ? 0.35f : 0f);
            m.SetShaderParameter("alpha_cut", grass ? 0.4f : 0.45f);
            if (grass) m.SetShaderParameter("shrink_band", nearFade);
        }
        _scatterMaterials.Add(m);
        _scatterMaterialCache[name] = m;
        return m;
    }

    /// <summary>Per frame: the camera position for the shaders' LOD fades, and the clearings under buildings.</summary>
    private void UpdateScatter()
    {
        if (_scatterRoot == null) return;
        if (GetViewport()?.GetCamera3D() is { } camera)
        {
            var eye = camera.GlobalPosition;
            if (eye.DistanceSquaredTo(_scatterEye) > 0.04f)
            {
                _scatterEye = eye;
                foreach (var m in _scatterMaterials) m.SetShaderParameter("eye_position", eye);
            }
        }
        var signature = ClearingSignature();
        if (signature == _clearingSignature) return;
        _clearingSignature = signature;
        UpdateClearings();
    }

    private long ClearingSignature()
    {
        var h = 17L;
        foreach (var s in _surface.Structures) h = h * 31 + (Shows(s) ? s.Tile * 2 + (_sim.Data.Building(s.DefId).IsCore ? 1 : 0) : -1);
        for (var i = 0; i < _surface.Players.Count; i++) h = h * 31 + ClearedLanding(i);
        return h;
    }

    /// <summary>A player's landing site while it clears the scatter (from the descent on), or -1: in orbit, or another
    /// player's site out of sight (fog: a clearing would give a hidden base away).</summary>
    private int ClearedLanding(int player)
    {
        var p = _surface.Players[player];
        if (p.Phase == LandingPhase.InOrbit || p.LandingTile < 0) return -1;
        return player == _localPlayer || Sees(p.LandingTile) || _surface.StructureAt(p.LandingTile) is { } s && Shows(s) ? p.LandingTile : -1;
    }

    /// <summary>Hide every scatter instance on a building's site: a colony core with its repair pad clears most of its
    /// tile (also the landing site from the moment the pod descends), other buildings a smaller circle. Instances come
    /// back when the building goes (salvaged or cancelled).</summary>
    private void UpdateClearings()
    {
        foreach (var (batch, index) in _scatterHidden) batch.Multi.SetInstanceTransform(index, batch.Local[index]);
        _scatterHidden.Clear();

        var sites = new List<(Vector3 Centre, float Radius)>();
        foreach (var s in _surface.Structures)
            if (Shows(s)) sites.Add((TileCentre(s.Tile), _sim.Data.Building(s.DefId).IsCore ? 14.5f : 8.5f)); // fog: known ones only
        for (var i = 0; i < _surface.Players.Count; i++)
            if (ClearedLanding(i) is var landing && landing >= 0) sites.Add((TileCentre(landing), 14.5f));

        var zero = new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero);
        var done = new HashSet<(ScatterBatch, int)>();
        foreach (var (centre, radius) in sites)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((centre.X - radius) / ChunkWidth), 0, _chunkColumns - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((centre.X + radius) / ChunkWidth), 0, _chunkColumns - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((centre.Z - radius) / ChunkDepth), 0, _chunkRows - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((centre.Z + radius) / ChunkDepth), 0, _chunkRows - 1);
            var r2 = radius * radius;
            for (var cz = z0; cz <= z1; cz++)
                for (var cx = x0; cx <= x1; cx++)
                    foreach (var batch in _scatterChunks[cz * _chunkColumns + cx])
                        for (var i = 0; i < batch.World.Length; i++)
                        {
                            var d = batch.World[i] - centre;
                            if (d.X * d.X + d.Z * d.Z > r2 || !done.Add((batch, i))) continue;
                            batch.Multi.SetInstanceTransform(i, new Transform3D(zero, batch.Local[i].Origin));
                            _scatterHidden.Add((batch, i));
                        }
        }
    }

    /// <summary>
    /// The terrain mesh's own height and face normal at an offset from a tile's centre (same triangles as
    /// <see cref="BuildTerrain"/>: centre, bumped inner ring, shared corners), so scatter sits on the ground.
    /// </summary>
    private (float Y, Vector3 Normal) SurfaceAt(int tile, Vector3 offset)
    {
        var map = _surface.Map;
        var id = map.Terrain(tile).Id;
        var water = map.IsWater(tile);
        var c = TileCentre(tile) with { Y = 0 };
        var centre = c with { Y = _centreHeight[tile] };
        var deg = Mathf.RadToDeg(Mathf.Atan2(offset.X, offset.Z));
        if (deg < 0) deg += 360f;
        var k = Mathf.Min(5, (int)(deg / 60f));
        var k1 = (k + 1) % 6;
        Vector3 CornerAt(int i) => (c + Corner(i)) with { Y = _cornerHeight[tile * 6 + i] };
        Vector3 MidAt(int i)
        {
            if (id == "mountains")
            {
                var m = centre.Lerp(CornerAt(i), 0.42f);
                m.Y += Jag(m) * 7f;
                return m;
            }
            var mid = centre.Lerp(CornerAt(i), 0.5f);
            if (!water) mid.Y += Bump(mid) * (id == "hills" ? 1.4f : 0.7f);
            return mid;
        }
        Vector3 ck = CornerAt(k), ck1 = CornerAt(k1), mk = MidAt(k), mk1 = MidAt(k1);
        var q = c + offset;
        if (OnTriangle(q, centre, mk, mk1, out var y, out var n)
            || OnTriangle(q, mk, ck, ck1, out y, out n)
            || OnTriangle(q, mk, ck1, mk1, out y, out n))
            return (y, n);
        return (GroundHeight(tile, offset), Vector3.Up);
    }

    /// <summary>Height and upward face normal of triangle abc above q (X/Z), if q lies on it.</summary>
    private static bool OnTriangle(Vector3 q, Vector3 a, Vector3 b, Vector3 c, out float y, out Vector3 normal)
    {
        y = 0;
        normal = Vector3.Up;
        var d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
        if (Mathf.Abs(d) < 1e-6f) return false;
        var wa = ((b.Z - c.Z) * (q.X - c.X) + (c.X - b.X) * (q.Z - c.Z)) / d;
        var wb = ((c.Z - a.Z) * (q.X - c.X) + (a.X - c.X) * (q.Z - c.Z)) / d;
        var wc = 1f - wa - wb;
        const float eps = -1e-3f;
        if (wa < eps || wb < eps || wc < eps) return false;
        y = wa * a.Y + wb * b.Y + wc * c.Y;
        normal = (b - a).Cross(c - a).Normalized();
        if (normal.Y < 0) normal = -normal;
        return true;
    }
}
