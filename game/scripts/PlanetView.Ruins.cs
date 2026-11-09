using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Render-only: ruin props (RuinModels.cs) on the colony map's ruin sites (<see cref="PlanetSurface.Ruins"/>, civilizations
/// phase 2). Once a second: a prop on every ruin tile the local player has seen (in sight now or explored), the variant
/// picked by the tile (precursor species get their own: Preservers the obelisk ring, Iconians the gateway dome, T'Kon
/// the colonnade), turned by the tile, the stone tinted a little toward the dead species' colour; precursor sites glow
/// faintly (a slow pulse, every frame). An Excavation Site on the tile (placed or finished, as far as the player knows
/// it) hides the core, leaving the outliers round the dig. The scatter (trees, rocks) is cleared under every shown ruin:
/// the clearings in PlanetView.Scatter.cs are recomputed whenever the shown set changes, then the ruins' holes are cut
/// on top of them.
/// </summary>
public partial class PlanetView
{
    /// <summary>Scatter within this distance of a ruin's centre is hidden (the props reach ~8.6; rims ~10).</summary>
    private const float RuinClearing = 9.6f;

    private sealed class RuinProp
    {
        public required Node3D Node;
        public required string Species;
        public required bool Precursor;
        public Node3D? Core, Far;
    }

    private readonly Dictionary<int, RuinProp> _ruinProps = new(); // by tile
    private readonly HashSet<int> _ruinsShown = new();
    private readonly List<int> _ruinsGone = new();
    private float _ruinClock = 1f;
    private long _ruinSet = long.MinValue;            // the shown ruin tiles last second
    private long _ruinScatterFor = long.MinValue;     // the clearing signature the ruin holes were cut into

    /// <summary>Ruin props, their glow and the scatter holes under them; after UpdateScatter.</summary>
    private void UpdateRuins(float delta)
    {
        _ruinClock += delta;
        if (_ruinClock >= 1f)
        {
            _ruinClock = 0f;
            RefreshRuins();
        }
        foreach (var (tile, p) in _ruinProps)
            if (p.Precursor) RuinModels.SetGlow(p.Node, 0.45f + 0.25f * Mathf.Sin(_time * 1.3f + tile * 0.7f));
        // UpdateClearings restores every hidden instance and cuts only the buildings' clearings: cut ours again after it
        if (_scatterRoot != null && _clearingSignature != long.MinValue && _clearingSignature != _ruinScatterFor) CutRuinScatter();
    }

    private bool SeesRuin(int tile) => Sees(tile) || _surface.IsExplored(_localPlayer, tile);

    private void RefreshRuins()
    {
        _ruinsShown.Clear();
        var set = 17L;
        foreach (var r in _surface.Ruins)
        {
            if (r.Tile < 0 || r.Tile >= _surface.Map.Grid.Count || !SeesRuin(r.Tile)) continue;
            _ruinsShown.Add(r.Tile);
            set = set * 31 + r.Tile;
            if (!_ruinProps.TryGetValue(r.Tile, out var p) || p.Species != r.Species)
            {
                p?.Node.QueueFree();
                _ruinProps[r.Tile] = p = MakeRuin(r);
            }
            // an Excavation Site there (placed or finished, as far as we know it) hides the core: the rim stays round it
            var dug = _surface.StructureAt(r.Tile) is { DefId: "excavation_site" } site && Shows(site);
            if (p.Core != null) p.Core.Visible = !dug;
            if (p.Far != null) p.Far.Visible = !dug;
        }
        _ruinsGone.Clear();
        foreach (var tile in _ruinProps.Keys)
            if (!_ruinsShown.Contains(tile)) _ruinsGone.Add(tile);
        foreach (var tile in _ruinsGone)
        {
            _ruinProps[tile].Node.QueueFree();
            _ruinProps.Remove(tile);
        }
        if (set == _ruinSet) return;
        _ruinSet = set;
        // UpdateScatter redoes the clearings next frame (bringing back trees where a ruin went), then we cut ours
        _clearingSignature = long.MinValue;
        _ruinScatterFor = long.MinValue;
    }

    private RuinProp MakeRuin(SurfaceRuin r)
    {
        var h = Hash01(r.Tile * 92821 + 7);
        var precursor = RuinModels.PrecursorVariant(r.Species);
        var variant = precursor >= 0 ? precursor : Mathf.Min(RuinModels.Variants - 1, (int)(h * RuinModels.Variants));
        var node = RuinModels.Build(variant, NativeTowns.SpeciesColour(StableHash(r.Species)));
        node.Name = $"Ruin{r.Tile}";
        node.Position = TileCentre(r.Tile);
        node.RotationDegrees = new Vector3(0f, Hash01(r.Tile * 15485863 + 3) * 360f, 0f);
        AddChild(node);
        RuinModels.SetGlow(node, precursor >= 0 ? 0.5f : 0f);
        return new RuinProp
        {
            Node = node,
            Species = r.Species,
            Precursor = precursor >= 0,
            Core = node.GetNodeOrNull<Node3D>("Core"),
            Far = node.GetNodeOrNull<Node3D>("Far"),
        };
    }

    /// <summary>A stable hash of a name (string.GetHashCode is randomised per process).</summary>
    private static int StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) h = (h ^ c) * 16777619u;
        return unchecked((int)h);
    }

    /// <summary>Hide the scatter instances round every shown ruin (added to the clearings' lists, so the next
    /// UpdateClearings brings them back like any other).</summary>
    private void CutRuinScatter()
    {
        _ruinScatterFor = _clearingSignature;
        if (_ruinProps.Count == 0 || _scatterChunks.Length == 0) return;
        var zero = new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero);
        var r2 = RuinClearing * RuinClearing;
        foreach (var tile in _ruinProps.Keys)
        {
            var centre = TileCentre(tile);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((centre.X - RuinClearing) / ChunkWidth), 0, _chunkColumns - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((centre.X + RuinClearing) / ChunkWidth), 0, _chunkColumns - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((centre.Z - RuinClearing) / ChunkDepth), 0, _chunkRows - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((centre.Z + RuinClearing) / ChunkDepth), 0, _chunkRows - 1);
            for (var cz = z0; cz <= z1; cz++)
                for (var cx = x0; cx <= x1; cx++)
                    foreach (var batch in _scatterChunks[cz * _chunkColumns + cx])
                        for (var i = 0; i < batch.World.Length; i++)
                        {
                            var d = batch.World[i] - centre;
                            if (d.X * d.X + d.Z * d.Z > r2 || !_scatterHiddenSet.Add((batch, i))) continue;
                            batch.Multi.SetInstanceTransform(i, new Transform3D(zero, batch.Local[i].Origin));
                            _scatterHidden.Add((batch, i));
                        }
        }
    }
}
