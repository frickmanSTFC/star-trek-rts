using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// A civilization's world seen from orbit (civilizations phase 1, docs/civilizations-contract.md "View"; render-only):
//  - city lights on the night side at the sim's TownsOn tiles (lit towns, L3+): on a look-only globe (class M / L)
//    through the ground shader's lights_tex, the same path as the colony's city lights (CivLightsImage, painted with the
//    look-only map's grid); on a procedural globe (body_globe.gdshader, no lights texture) as a few additive dots whose
//    brightness follows the night side every frame;
//  - a faint smog shell over L3–L4 worlds (lit, so it is dark at night);
//  - the towns themselves (NativeTowns.Far: the cheap ≤ 300-triangle stand-ins made for the globe, sized by the
//    town's size), at most MaxTownModels of the biggest, shown only when zoomed in close on the Planet / Orbit displays,
//    lights on at night (NativeTowns.SetNight).
// What is physically there (CivAt / TownsOn), not what the player knows: the globe is what the ship sees.
public partial class OrbitView
{
    private const int MaxTownModels = 18;
    private const int MaxLightDots = 120;
    private const float TownsBelowDistance = 215f; // camera distance from the globe's centre (zoom range 135–520)

    private Node3D? _civTowns;
    private MeshInstance3D? _civSmog;
    private MultiMeshInstance3D? _civDots;
    private readonly List<(Node3D Town, Vector3 Dir)> _civTownNodes = new();
    private readonly List<(Vector3 Dir, float Strength)> _civDotDirs = new();
    private float _civNightClock;

    /// <summary>How many town models the globe of the body shown has (0 at the colony or without a civilization).</summary>
    public int CivTownCount => _civTownNodes.Count;

    /// <summary>Globe-local direction of a tile's centre on any grid (the same mapping as <see cref="TileDirection"/>).</summary>
    private static Vector3 TileDirectionOn(HexGrid g, int tile)
    {
        var col = g.Column(tile);
        var row = g.Row(tile);
        var u = (col + 0.5f + ((row & 1) == 1 ? 0.5f : 0f)) / g.Width;
        var v = VTop + (row + 0.5f) / g.Height * (VBottom - VTop);
        var theta = v * Mathf.Pi;
        var phi = u * Mathf.Tau;
        return new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
    }

    /// <summary>The grid a body's towns are on: its look-only map's, else a nominal one the size of the class's surface
    /// (or class M's) so tiles still land somewhere sensible on a procedural globe.</summary>
    private HexGrid CivGridOf(BodyDef body)
    {
        if (LookMapOf(body.Id) is { } map) return map.Grid;
        PlanetSurfaceDef? def = null;
        foreach (var s in _sim.Data.Surfaces)
            if (s.ClassId == body.ClassId || (def == null && s.ClassId == "class_m")) def = s;
        return new HexGrid(def?.Width ?? 128, def?.Height ?? 88);
    }

    private IReadOnlyList<NativeTown> TownsOf(string bodyId) => _source.State?.TownsOn(bodyId) ?? System.Array.Empty<NativeTown>();

    /// <summary>The night-side lights texture of a look-only globe: each lit town's tile glows by its size, its
    /// neighbours faintly (black where nobody lives).</summary>
    private Image CivLightsImage(BodyDef body, SurfaceMap map)
    {
        var g = map.Grid;
        var glow = new Dictionary<int, float>();
        foreach (var town in TownsOf(body.Id))
        {
            if (!town.Lit || town.Tile < 0 || town.Tile >= g.Count) continue;
            var strength = 0.35f + 0.22f * Mathf.Clamp(town.Size, 1, 3);
            glow[town.Tile] = Mathf.Max(glow.GetValueOrDefault(town.Tile), strength);
            foreach (var n in g.Neighbours(town.Tile)) glow[n] = Mathf.Max(glow.GetValueOrDefault(n), strength * (town.Size >= 3 ? 0.45f : 0.25f));
        }
        if (glow.Count == 0)
        {
            var dark = Image.CreateEmpty(4, 2, false, Image.Format.Rgba8);
            dark.Fill(Colors.Black);
            return dark;
        }
        var warm = new Color(1f, 0.8f, 0.5f);
        var tiles = Image.CreateEmpty(g.Width * 2, g.Height, false, Image.Format.Rgba8);
        tiles.Fill(Colors.Black);
        foreach (var (t, s) in glow)
        {
            var row = g.Row(t);
            var x = g.Column(t) * 2 + ((row & 1) == 1 ? 1 : 0);
            var c = warm * s;
            c.A = 1f;
            tiles.SetPixel(x % (g.Width * 2), row, c);
            tiles.SetPixel((x + 1) % (g.Width * 2), row, c);
        }
        var top = Mathf.RoundToInt(TextureHeight * VTop);
        var band = Mathf.RoundToInt(TextureHeight * (VBottom - VTop));
        tiles.Resize(TextureWidth, band, Image.Interpolation.Cubic);
        var full = Image.CreateEmpty(TextureWidth, TextureHeight, false, Image.Format.Rgba8);
        full.Fill(Colors.Black);
        full.BlitRect(tiles, new Rect2I(0, 0, TextureWidth, band), new Vector2I(0, top));
        full.GenerateMipmaps();
        return full;
    }

    /// <summary>After another body went up on the globe displays: its smog, light dots (procedural globes) and towns.</summary>
    private void ShowCivilization(BodyDef body)
    {
        ClearCivilization();
        if (_source.State?.CivAt(body.Id) is not { Living: true } site) return;
        var level = site.Level;
        var towns = TownsOf(body.Id);
        var grid = CivGridOf(body);
        var lookOnly = _lookGround.Visible; // the look-only globe carries the lights in its texture already

        // Smog over industrial and atomic worlds: a faint lit haze just above the ground.
        if (level is CivLevel.Industrial or CivLevel.Atomic && _bodySolid)
        {
            _civSmog = new MeshInstance3D
            {
                Name = "CivSmog",
                Mesh = new SphereMesh { Radius = Radius + 0.9f, Height = (Radius + 0.9f) * 2f, RadialSegments = 96, Rings = 48 },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = level == CivLevel.Atomic ? new Color(0.55f, 0.5f, 0.42f, 0.16f) : new Color(0.5f, 0.45f, 0.38f, 0.12f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 1f,
                    SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
                },
            };
            _bodyGlobe.AddChild(_civSmog);
        }

        // Light dots for procedural globes (no lights texture there).
        if (!lookOnly && _bodySolid)
        {
            foreach (var town in towns)
            {
                if (!town.Lit || town.Tile < 0 || town.Tile >= grid.Count || _civDotDirs.Count >= MaxLightDots) continue;
                _civDotDirs.Add((TileDirectionOn(grid, town.Tile), 0.45f + 0.2f * Mathf.Clamp(town.Size, 1, 3)));
            }
            if (_civDotDirs.Count > 0)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = new SphereMesh { Radius = 0.55f, Height = 0.4f, RadialSegments = 8, Rings = 3 },
                    InstanceCount = _civDotDirs.Count,
                };
                for (var i = 0; i < _civDotDirs.Count; i++)
                {
                    var (dir, strength) = _civDotDirs[i];
                    var scale = 0.8f + strength;
                    mm.SetInstanceTransform(i, new Transform3D(new Basis(new Quaternion(Vector3.Up, dir)) * Basis.FromScale(Vector3.One * scale), dir * (Radius + 0.15f)));
                    mm.SetInstanceColor(i, new Color(1f, 0.8f, 0.5f, 0f));
                }
                _civDots = new MultiMeshInstance3D
                {
                    Name = "CivLights",
                    Multimesh = mm,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = new StandardMaterial3D
                    {
                        VertexColorUseAsAlbedo = true,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    },
                };
                _bodyGlobe.AddChild(_civDots);
            }
        }

        // The towns themselves: the biggest few, seen only up close.
        if (_bodySolid && towns.Count > 0)
        {
            _civTowns = new Node3D { Name = "CivTowns", Visible = false };
            _bodyGlobe.AddChild(_civTowns);
            var colour = NativeTowns.SpeciesColour(site.Seed);
            var picked = towns.Select((t, i) => (Town: t, Index: i)).Where(x => x.Town.Tile >= 0 && x.Town.Tile < grid.Count)
                .OrderByDescending(x => x.Town.Size).ThenBy(x => x.Index).Take(MaxTownModels).ToList();
            foreach (var (town, index) in picked)
            {
                var dir = TileDirectionOn(grid, town.Tile);
                var model = NativeTowns.Far(level, colour, Mathf.Clamp(town.Size, 1, 3)); // Far scales itself up for size 2–3
                // a town reaches NativeTowns.TownRadius (9.6) on the surface; a tile here is ≈ 5 (2πR / 128): ~half a tile
                var scale = 1.3f / NativeTowns.TownRadius;
                var basis = new Basis(new Quaternion(Vector3.Up, dir)) * new Basis(Vector3.Up, (index * 2.399f) % Mathf.Tau) * Basis.FromScale(Vector3.One * scale);
                var holder = new Node3D { Name = $"Town{index}", Transform = new Transform3D(basis, dir * (Radius + 0.05f)) };
                holder.AddChild(model);
                _civTowns.AddChild(holder);
                _civTownNodes.Add((model, dir));
            }
        }
        _civNightClock = 1f; // set the lights on the first frame
    }

    private void ClearCivilization()
    {
        _civSmog?.QueueFree();
        _civSmog = null;
        _civDots?.QueueFree();
        _civDots = null;
        _civTowns?.QueueFree();
        _civTowns = null;
        _civTownNodes.Clear();
        _civDotDirs.Clear();
    }

    /// <summary>0 on the day side, 1 on the night side (soft over the terminator), for a globe-local direction.</summary>
    private float NightAt(Vector3 localDir)
    {
        var world = _bodyGlobe.Basis * localDir;
        return Mathf.Clamp((0.08f - world.Dot(_sunDir)) / 0.22f, 0f, 1f);
    }

    /// <summary>Every frame (from RefreshBodyGlobe): light dots follow the night side, towns show up close and light up.</summary>
    private void RefreshCivilization(float delta)
    {
        if (_atColony || !_bodyGlobe.Visible) return;
        if (_civDots != null)
        {
            var mm = _civDots.Multimesh;
            for (var i = 0; i < _civDotDirs.Count && i < mm.InstanceCount; i++)
                mm.SetInstanceColor(i, new Color(1f, 0.8f, 0.5f, NightAt(_civDotDirs[i].Dir) * _civDotDirs[i].Strength));
        }
        if (_civTowns == null) return;
        var close = Display is OrbitDisplay.Planet or OrbitDisplay.Orbit && _distance * (Display == OrbitDisplay.Orbit ? _zoomOut : 1f) < TownsBelowDistance;
        if (_civTowns.Visible != close) _civTowns.Visible = close;
        if (!close) return;
        _civNightClock += delta;
        if (_civNightClock < 0.3f) return;
        _civNightClock = 0f;
        foreach (var (town, dir) in _civTownNodes)
            if (GodotObject.IsInstanceValid(town)) NativeTowns.SetNight(town, NightAt(dir));
    }
}
