using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Visual pass G2 (docs/visual-pass-contract.md), GraphicsQuality.Decals: marks on the ground. Render-only.
//  - Tracks: a tread segment every TrackStep behind each ground vehicle that is driving and shown live (ours, or a
//    rival's in sight), tinted by the ground it is on (SprayRig kinds), fading over TrackLife; one ring pool of TrackCap.
//  - Scorch: a burn and a pale blast ring where a shown drop ship touches down or lifts off (DropShipView.Scorched);
//    a second burn on the same spot refreshes it. ScorchCap pairs.
//  - Worn ground: a trodden patch round every finished building and an oil stain at its access point (WearCap).
//  - Footpaths: where the crew walkers (PlanetView.Walkers.cs) keep crossing the same 2.5-unit cells, a worn strip
//    grows along their way (PathCap); unwalked cells fade back over minutes.
// Decals project only onto the terrain (its mesh gets DecalGroundLayer) and, while the outpost feed shows the surface
// in orbit, onto everything on the feed layer (SurfaceFeed moves the terrain there; the marks must stay). Every decal
// fades with distance (DecalFadeFrom + DecalFadeLength), so far ones cost nothing in Godot's cluster budget, and is
// dimmed by the fog of war at its tile. Built on first use, pooled; nothing is allocated per frame.
public partial class PlanetView
{
    /// <summary>Render layer bit the ground decals project onto (layer 19), added to the terrain mesh.</summary>
    private const uint DecalGroundLayer = 1u << 18;

    private const int TrackCap = 220, ScorchCap = 8, WearCap = 96, PathCap = 160;
    private const float TrackLife = 60f, ScorchLife = 300f, RingLife = 120f;
    private const float TrackStep = 3.4f;
    private const float DecalFadeFrom = 240f, DecalFadeLength = 90f;
    private const float PathCell = 2.5f;

    private sealed class Mark
    {
        public required Decal Node;
        public float Age = float.MaxValue;
        public float Life;
        public float Peak;      // modulate alpha when fresh
        public Color Tint;
        public int Tile = -1;
        public float Shown = -1f; // last alpha written
        public bool Hidden;       // its building isn't shown (a rival's out of sight and not remembered)
    }

    private sealed class PathCellMark
    {
        public int Passes;
        public Vector2 Way; // summed walking directions
        public Vector3 At;
        public Mark? Mark;
    }

    private Node3D? _decalRoot;
    private bool _decalsOn;
    private readonly List<Mark> _tracks = new();
    private int _trackNext;
    private readonly List<Mark> _scorches = new(), _rings = new();
    private int _scorchNext;
    private readonly Dictionary<int, (Mark Wear, Mark Stain)> _wear = new();
    private readonly Dictionary<long, PathCellMark> _paths = new();
    private readonly List<Mark> _pathPool = new(); // free path decals
    private int _pathCount;
    private readonly Dictionary<int, Vector3> _trackLast = new();
    private readonly Dictionary<ulong, (long Cell, Vector3 At)> _walkerCell = new();
    private readonly List<int> _decalScratch = new();
    private readonly List<long> _pathScratch = new();
    private float _decalTick, _wearTick, _pathTick, _pathDecay;
    private bool _scorchWired;

    private static Texture2D? _treadTex, _treadNormal, _trackTex, _trackNormal, _scorchTex, _ringTex, _wearTex, _stainTex, _pathTex;

    // ---------------------------------------------------------------- switching

    private void ApplyDecals(bool on)
    {
        if (!_scorchWired && _dropShips != null)
        {
            _scorchWired = true;
            _dropShips.Scorched += OnScorched;
        }
        if (on == _decalsOn) return;
        _decalsOn = on;
        if (on)
        {
            if (_decalRoot == null)
            {
                _decalRoot = new Node3D { Name = "GroundDecals" };
                AddChild(_decalRoot);
            }
            _decalRoot.Visible = true;
            _wearTick = 999f; // place the building patches now
            return;
        }
        // Off: everything goes (exactly the old scene); the pools are rebuilt if it comes back.
        if (_decalRoot != null)
        {
            _decalRoot.QueueFree();
            _decalRoot = null;
        }
        _tracks.Clear();
        _trackNext = 0;
        _scorches.Clear();
        _rings.Clear();
        _scorchNext = 0;
        _wear.Clear();
        _paths.Clear();
        _pathPool.Clear();
        _pathCount = 0;
        _trackLast.Clear();
        _walkerCell.Clear();
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Lay tracks, keep the building patches and footpaths in step, fade everything (Refresh, after the
    /// rovers and walkers are posed).</summary>
    private void UpdateDecals(float delta)
    {
        if (!_decalsOn || _decalRoot == null) return;
        LayTracks();
        _wearTick += delta;
        if (_wearTick >= 1f)
        {
            _wearTick = 0f;
            UpdateWear();
        }
        _pathTick += delta;
        if (_pathTick >= 0.8f)
        {
            SampleWalkers(_pathTick);
            _pathTick = 0f;
        }
        _pathDecay += delta;
        if (_pathDecay >= 90f)
        {
            _pathDecay = 0f;
            DecayPaths();
        }
        // Ageing every frame is cheap; writing Modulate only 5 times a second.
        foreach (var m in _tracks) m.Age += delta;
        foreach (var m in _scorches) m.Age += delta;
        foreach (var m in _rings) m.Age += delta;
        _decalTick += delta;
        if (_decalTick < 0.2f) return;
        _decalTick = 0f;
        foreach (var m in _tracks) Fade(m);
        foreach (var m in _scorches) Fade(m);
        foreach (var m in _rings) Fade(m);
        foreach (var (wear, stain) in _wear.Values)
        {
            Fade(wear);
            Fade(stain);
        }
        foreach (var cell in _paths.Values)
            if (cell.Mark != null)
                Fade(cell.Mark);
    }

    /// <summary>Alpha now: full for the first 60% of its life, then down to 0; × the fog of war at its tile.</summary>
    private void Fade(Mark m)
    {
        var life = m.Life <= 0f ? 1f : 1f - Mathf.Clamp((m.Age - m.Life * 0.6f) / (m.Life * 0.4f), 0f, 1f);
        if (m.Life > 0f && m.Age >= m.Life) life = 0f;
        var alpha = m.Hidden ? 0f : m.Peak * life * FogShade(m.Tile);
        if (Mathf.Abs(alpha - m.Shown) < 0.01f) return;
        m.Shown = alpha;
        var on = alpha > 0.005f;
        if (m.Node.Visible != on) m.Node.Visible = on;
        if (on) m.Node.Modulate = m.Tint with { A = alpha };
    }

    /// <summary>1 in sight, 0.4 explored, 0 never seen (decals are drawn after the terrain's fog darkening).</summary>
    private float FogShade(int tile)
    {
        if (tile < 0) return 1f;
        if (Sees(tile)) return 1f;
        var me = _surface.Players[_localPlayer];
        return tile < me.Seen.Length && (me.Seen[tile] || me.Surveyed[tile]) ? 0.4f : 0f;
    }

    // ---------------------------------------------------------------- tracks

    private void LayTracks()
    {
        var units = _surface.Units;
        for (var i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!_rovers.TryGetValue(u.Id, out var model) || !model.Visible || !ShowsLive(u.Owner, u.Tile)) continue;
            var pos = model.Position;
            var ground = GroundAt(pos);
            if (pos.Y - ground > 0.7f) // on its drop ship's ramp or in the bay: no marks
            {
                _trackLast.Remove(u.Id);
                continue;
            }
            pos.Y = ground;
            if (!_trackLast.TryGetValue(u.Id, out var last))
            {
                _trackLast[u.Id] = pos;
                continue;
            }
            var d = (pos - last) with { Y = 0f };
            var len = d.Length();
            if (len < TrackStep) continue;
            _trackLast[u.Id] = pos;
            if (len > TrackStep * 3f) continue; // teleported or rolled off a ship: start again here
            var tile = TileAt(pos);
            if (tile < 0) continue;
            var kind = SprayRig.KindFor(_surface.Map, tile, FrostAt(tile));
            if (kind is SprayKind.Water or SprayKind.None) continue;
            var def = _sim.Data.SurfaceUnit(u.DefId);
            var crawler = def.CanMine;
            var half = def.CanMine ? 1.13f : def.CanFound ? 1.35f : def.Mark switch { 1 => 1.3f, 3 => 1.5f, _ => 1.35f };
            LayTrack(last.Lerp(pos, 0.5f), d / len, len + 0.35f, half * 2f + (crawler ? 0.9f : 0.6f), crawler, kind, tile);
        }
        // Forget vehicles that are gone (a few at a time, no allocation beyond the scratch list).
        if (_trackLast.Count > units.Count + 8)
        {
            _decalScratch.Clear();
            foreach (var id in _trackLast.Keys)
                if (!_rovers.ContainsKey(id))
                    _decalScratch.Add(id);
            foreach (var id in _decalScratch) _trackLast.Remove(id);
        }
    }

    private void LayTrack(Vector3 centre, Vector3 dir, float length, float width, bool crawler, SprayKind ground, int tile)
    {
        Mark m;
        if (_tracks.Count < TrackCap)
        {
            m = NewMark("Track", Vector3.One, DecalFadeFrom * 0.8f);
            m.Node.NormalFade = 0.3f;
            _tracks.Add(m);
        }
        else
        {
            m = _tracks[_trackNext];
            _trackNext = (_trackNext + 1) % TrackCap;
        }
        m.Node.TextureAlbedo = crawler ? _trackTex : _treadTex;
        m.Node.TextureNormal = crawler ? _trackNormal : _treadNormal;
        m.Node.Size = new Vector3(width, 3.2f, length);
        m.Node.Transform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), centre);
        var (tint, peak) = TrackTint(ground);
        m.Tint = tint;
        m.Peak = peak;
        m.Life = TrackLife;
        m.Age = 0f;
        m.Tile = tile;
        m.Shown = -1f;
        Fade(m);
    }

    private static (Color Tint, float Peak) TrackTint(SprayKind ground) => ground switch
    {
        SprayKind.Dust => (new Color(0.52f, 0.4f, 0.26f), 0.55f),
        SprayKind.Dirt => (new Color(0.2f, 0.16f, 0.1f), 0.5f),
        SprayKind.Grit => (new Color(0.28f, 0.24f, 0.19f), 0.5f),
        SprayKind.Mud => (new Color(0.1f, 0.075f, 0.05f), 0.65f),
        SprayKind.Snow => (new Color(0.6f, 0.65f, 0.74f), 0.55f),
        SprayKind.RustDust => (new Color(0.4f, 0.24f, 0.14f), 0.55f),
        SprayKind.FrostGrit => (new Color(0.45f, 0.45f, 0.47f), 0.5f),
        _ => (new Color(0.22f, 0.18f, 0.12f), 0.5f),
    };

    // ---------------------------------------------------------------- scorch

    private void OnScorched(Vector3 at, bool liftOff)
    {
        if (!_decalsOn || _decalRoot == null) return;
        var tile = TileAt(at);
        at.Y = GroundAt(at);
        // The same pad again (lift-off after touchdown): refresh it.
        for (var i = 0; i < _scorches.Count; i++)
            if (_scorches[i].Node.Position.DistanceSquaredTo(at) < 16f)
            {
                _scorches[i].Age = 0f;
                _scorches[i].Peak = Mathf.Min(0.95f, _scorches[i].Peak + 0.1f);
                _rings[i].Age = 0f;
                _scorches[i].Shown = _rings[i].Shown = -1f;
                return;
            }
        Mark scorch, ring;
        if (_scorches.Count < ScorchCap)
        {
            scorch = NewMark("Scorch", Vector3.One, DecalFadeFrom * 1.5f);
            ring = NewMark("BlastRing", Vector3.One, DecalFadeFrom * 1.5f);
            _scorches.Add(scorch);
            _rings.Add(ring);
        }
        else
        {
            scorch = _scorches[_scorchNext];
            ring = _rings[_scorchNext];
            _scorchNext = (_scorchNext + 1) % ScorchCap;
        }
        var yaw = new Basis(Vector3.Up, (at.X * 0.37f + at.Z * 0.61f) % Mathf.Tau);
        scorch.Node.TextureAlbedo = _scorchTex;
        scorch.Node.Size = new Vector3(15f, 6f, 15f);
        scorch.Node.Transform = new Transform3D(yaw, at);
        scorch.Tint = new Color(0.06f, 0.05f, 0.045f);
        scorch.Peak = liftOff ? 0.75f : 0.85f;
        scorch.Life = ScorchLife;
        ring.Node.TextureAlbedo = _ringTex;
        ring.Node.Size = new Vector3(32f, 6f, 32f);
        ring.Node.Transform = new Transform3D(yaw.Rotated(Vector3.Up, 1.3f), at);
        ring.Tint = Look.SprayDust.Lightened(0.15f) with { A = 1f };
        ring.Peak = 0.5f;
        ring.Life = RingLife;
        foreach (var m in new[] { scorch, ring })
        {
            m.Age = 0f;
            m.Tile = tile;
            m.Shown = -1f;
            Fade(m);
        }
    }

    // ---------------------------------------------------------------- worn ground round buildings

    private void UpdateWear()
    {
        var list = _surface.Structures;
        _decalScratch.Clear();
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s.State != StructureState.Complete || !_structures.TryGetValue(s.Id, out var v)) continue;
            _decalScratch.Add(s.Id);
            if (_wear.ContainsKey(s.Id) || _wear.Count >= WearCap) continue;
            var centre = v.Model.Position with { Y = 0f };
            centre.Y = GroundAt(centre);
            var radius = Mathf.Max(5f, _surface.BlockRadius(s.Tile).ToFloat());
            var hash = (uint)(s.Id * 2654435761u);
            var wear = NewMark("Worn", Vector3.One, DecalFadeFrom);
            wear.Node.TextureAlbedo = _wearTex;
            wear.Node.Size = new Vector3(radius * 2.9f, 8f, radius * 2.9f);
            wear.Node.Transform = new Transform3D(new Basis(Vector3.Up, (hash & 1023) / 1023f * Mathf.Tau), centre);
            wear.Tint = WearTint(s.Tile);
            wear.Peak = 0.55f;
            wear.Tile = s.Tile;
            var stainAt = _surface.AccessPoint(s.Tile) is { } access ? SimPoint(access) : centre + new Vector3(radius * 0.9f, 0f, 0f);
            var stain = NewMark("Stain", Vector3.One, DecalFadeFrom * 0.7f);
            stain.Node.TextureAlbedo = _stainTex;
            stain.Node.Size = new Vector3(4.2f, 4f, 3.4f);
            stain.Node.Transform = new Transform3D(new Basis(Vector3.Up, ((hash >> 10) & 1023) / 1023f * Mathf.Tau), stainAt);
            stain.Tint = new Color(0.05f, 0.045f, 0.04f);
            stain.Peak = 0.6f;
            stain.Tile = s.Tile;
            _wear[s.Id] = (wear, stain);
            Fade(wear);
            Fade(stain);
        }
        // Buildings that are gone (salvaged, removed) take their patches with them.
        if (_wear.Count > _decalScratch.Count)
        {
            _pathScratch.Clear();
            foreach (var id in _wear.Keys)
                if (!_decalScratch.Contains(id))
                    _pathScratch.Add(id);
            foreach (var id in _pathScratch)
            {
                var (wear, stain) = _wear[(int)id];
                wear.Node.QueueFree();
                stain.Node.QueueFree();
                _wear.Remove((int)id);
            }
        }
        // Rivals' patches only while their building is shown (the fog shade handles the rest).
        for (var i = 0; i < list.Count; i++)
            if (_wear.TryGetValue(list[i].Id, out var w))
                w.Wear.Hidden = w.Stain.Hidden = !Shows(list[i]);
    }

    private Color WearTint(int tile)
    {
        var kind = tile >= 0 ? SprayRig.KindFor(_surface.Map, tile) : SprayKind.Dirt;
        return kind switch
        {
            SprayKind.Dust => new Color(0.6f, 0.47f, 0.32f),
            SprayKind.Snow => new Color(0.55f, 0.55f, 0.56f),
            SprayKind.RustDust => new Color(0.46f, 0.3f, 0.2f),
            SprayKind.Mud => new Color(0.2f, 0.15f, 0.1f),
            _ => new Color(0.34f, 0.28f, 0.2f),
        };
    }

    // ---------------------------------------------------------------- footpaths

    private void SampleWalkers(float seconds)
    {
        foreach (var w in _walkers)
        {
            if (!w.Active || !w.Model.Visible || !w.Model.IsInsideTree()) continue;
            var p = ToLocal(w.Model.GlobalPosition);
            var key = w.Model.GetInstanceId();
            var cell = CellKey(p);
            if (!_walkerCell.TryGetValue(key, out var was))
            {
                _walkerCell[key] = (cell, p);
                continue;
            }
            if (was.Cell == cell) continue;
            _walkerCell[key] = (cell, p);
            var way = new Vector2(p.X - was.At.X, p.Z - was.At.Z);
            if (way.LengthSquared() > 64f) continue; // swapped figure or a jump: not a step
            if (!_paths.TryGetValue(cell, out var c))
            {
                if (_paths.Count > PathCap * 6) continue; // bounded bookkeeping
                c = new PathCellMark();
                _paths[cell] = c;
            }
            c.Passes++;
            // Fold the direction in (either way along a path is the same path).
            if (c.Way != Vector2.Zero && c.Way.Dot(way) < 0f) way = -way;
            c.Way += way.Normalized();
            c.At = c.At == Vector3.Zero ? p : c.At.Lerp(p, 0.2f);
            ShowPath(c);
        }
        if (_walkerCell.Count > _walkers.Count * 2 + 16) _walkerCell.Clear();
    }

    private static long CellKey(Vector3 p) =>
        ((long)Mathf.FloorToInt(p.X / PathCell) << 32) ^ (uint)Mathf.FloorToInt(p.Z / PathCell);

    private void ShowPath(PathCellMark c)
    {
        if (c.Passes < 4) return;
        if (c.Mark == null)
        {
            if (_pathPool.Count > 0)
            {
                c.Mark = _pathPool[^1];
                _pathPool.RemoveAt(_pathPool.Count - 1);
            }
            else if (_pathCount < PathCap)
            {
                c.Mark = NewMark("Footpath", Vector3.One, DecalFadeFrom * 0.8f);
                c.Mark.Node.TextureAlbedo = _pathTex;
                _pathCount++;
            }
            else return;
        }
        var dir = c.Way.LengthSquared() > 0.01f ? c.Way.Normalized() : Vector2.Right;
        var at = c.At with { Y = GroundAt(c.At) };
        var tile = TileAt(at);
        c.Mark.Node.Size = new Vector3(1.7f, 3f, PathCell * 1.7f);
        c.Mark.Node.Transform = new Transform3D(Basis.LookingAt(new Vector3(dir.X, 0f, dir.Y), Vector3.Up), at);
        c.Mark.Tint = WearTint(tile);
        c.Mark.Peak = Mathf.Clamp((c.Passes - 3) / 14f, 0.12f, 0.6f);
        c.Mark.Life = 0f; // paths fade by DecayPaths, not by age
        c.Mark.Tile = tile;
        c.Mark.Shown = -1f;
        Fade(c.Mark);
    }

    /// <summary>Every 90 s: each cell loses a pass; cells walked no more give their decal back.</summary>
    private void DecayPaths()
    {
        _pathScratch.Clear();
        foreach (var (key, c) in _paths)
        {
            c.Passes--;
            if (c.Passes > 3)
            {
                if (c.Mark != null)
                {
                    c.Mark.Peak = Mathf.Clamp((c.Passes - 3) / 14f, 0.12f, 0.6f);
                    c.Mark.Shown = -1f;
                }
                continue;
            }
            if (c.Mark != null)
            {
                c.Mark.Node.Visible = false;
                c.Mark.Shown = 0f;
                _pathPool.Add(c.Mark);
                c.Mark = null;
            }
            if (c.Passes <= 0) _pathScratch.Add(key);
        }
        foreach (var key in _pathScratch) _paths.Remove(key);
    }

    // ---------------------------------------------------------------- decal nodes and textures

    private Mark NewMark(string name, Vector3 size, float fadeFrom)
    {
        EnsureDecalTextures();
        var node = new Decal
        {
            Name = name,
            Size = size,
            CullMask = DecalGroundLayer | SurfaceFeed.FeedLayer,
            UpperFade = 0.35f,
            LowerFade = 0.35f,
            AlbedoMix = 1f,
            DistanceFadeEnabled = true,
            DistanceFadeBegin = fadeFrom,
            DistanceFadeLength = DecalFadeLength,
            Visible = false,
        };
        _decalRoot!.AddChild(node);
        return new Mark { Node = node };
    }

    private static void EnsureDecalTextures()
    {
        if (_treadTex != null) return;
        (_treadTex, _treadNormal) = TreadTextures(false);
        (_trackTex, _trackNormal) = TreadTextures(true);
        _scorchTex = RadialTexture(256, (r, a, n) =>
        {
            // A black-brown burn, streaked outward, ragged at the rim.
            var rays = 0.55f + 0.45f * Hash1(Mathf.FloorToInt(a / Mathf.Tau * 48f));
            var edge = 1f - DecalSmooth(0.55f + 0.2f * n, 1f, r / rays);
            var core = 1f - DecalSmooth(0.0f, 0.5f, r);
            return (0.6f + 0.4f * n, Mathf.Clamp(edge * (0.55f + 0.45f * core) * (0.75f + 0.25f * n), 0f, 1f));
        });
        _ringTex = RadialTexture(256, (r, a, n) =>
        {
            // Dust blown out into a pale ring round the pad, swept clean inside.
            var ring = DecalSmooth(0.42f, 0.7f, r + 0.08f * (n - 0.5f)) * (1f - DecalSmooth(0.78f, 1f, r + 0.1f * (n - 0.5f)));
            var streak = 0.7f + 0.3f * Hash1(Mathf.FloorToInt(a / Mathf.Tau * 90f) + 7);
            return (0.85f + 0.15f * n, ring * streak * (0.6f + 0.4f * n));
        });
        _wearTex = RadialTexture(256, (r, a, n) =>
        {
            // A trodden patch: patchy, strongest near the building, soft edge.
            var cover = (1f - DecalSmooth(0.45f, 1f, r + 0.25f * (n - 0.5f))) * DecalSmooth(0.3f, 0.65f, n + 0.35f * (1f - r));
            return (0.8f + 0.2f * n, Mathf.Clamp(cover, 0f, 1f) * 0.85f);
        });
        _stainTex = RadialTexture(128, (r, a, n) =>
        {
            var blot = 1f - DecalSmooth(0.35f + 0.3f * n, 0.9f, r);
            return (0.7f + 0.3f * n, blot * 0.9f);
        });
        _pathTex = StripTexture();
    }

    private static float DecalSmooth(float a, float b, float x)
    {
        var t = Mathf.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Hash1(int i)
    {
        var h = (uint)i * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xffffff) / 16777215f;
    }

    /// <summary>Smooth value noise in 0..1 on a periodic grid (period cells), for the decal textures.</summary>
    private static float Noise2(float x, float y, int period)
    {
        var xi = Mathf.FloorToInt(x);
        var yi = Mathf.FloorToInt(y);
        var fx = x - xi;
        var fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float H(int a, int b) => Hash1(((a % period + period) % period) * 7919 + ((b % period + period) % period) * 104729 + 17);
        return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), fx), Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), fx), fy);
    }

    /// <summary>A square white-ish texture with alpha from f(radius 0..1, angle, noise) = (brightness, alpha).</summary>
    private static ImageTexture RadialTexture(int size, Func<float, float, float, (float Bright, float Alpha)> f)
    {
        var data = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size * 2f - 1f;
                var v = (y + 0.5f) / size * 2f - 1f;
                var r = Mathf.Sqrt(u * u + v * v);
                var a = Mathf.Atan2(v, u) + Mathf.Pi;
                var n = 0.6f * Noise2(x / 16f, y / 16f, 64) + 0.4f * Noise2(x / 5f, y / 5f, 256);
                var (bright, alpha) = r >= 1f ? (1f, 0f) : f(r, a, n);
                var i = (y * size + x) * 4;
                var b = (byte)(Mathf.Clamp(bright, 0f, 1f) * 255f);
                data[i] = data[i + 1] = data[i + 2] = b;
                data[i + 3] = (byte)(Mathf.Clamp(alpha, 0f, 1f) * 255f);
            }
        var image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A worn strip along v (a footpath cell), ragged at the sides and soft at both ends so cells overlap.</summary>
    private static ImageTexture StripTexture()
    {
        const int w = 64, h = 128;
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var u = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                var v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                var n = Noise2(x / 6f, y / 6f, 64);
                var alpha = (1f - DecalSmooth(0.35f + 0.35f * n, 1f, u)) * (1f - DecalSmooth(0.55f, 1f, v)) * (0.7f + 0.3f * n);
                var i = (y * w + x) * 4;
                data[i] = data[i + 1] = data[i + 2] = (byte)(200 + 55 * n);
                data[i + 3] = (byte)(Mathf.Clamp(alpha, 0f, 1f) * 255f);
            }
        var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Two tread strips along v (wheel tyres: chevrons; crawler: wide plates), plus a normal map that presses
    /// them in (ruts with raised edges). Tileable along v, so a segment of any length reads right.</summary>
    private static (ImageTexture Albedo, ImageTexture Normal) TreadTextures(bool crawler)
    {
        const int w = 64, h = 128;
        var height = new float[w * h];
        var alpha = new float[w * h];
        var stripW = crawler ? 0.27f : 0.19f; // share of the width per side
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w;
                var v = (y + 0.5f) / h;
                // Distance into the nearest strip (0 at its middle, 1 at its edge).
                var left = Mathf.Abs(u - stripW * 0.5f - 0.02f) / (stripW * 0.5f);
                var right = Mathf.Abs(u - (1f - stripW * 0.5f - 0.02f)) / (stripW * 0.5f);
                var across = Mathf.Min(left, right);
                var inStrip = 1f - DecalSmooth(0.75f, 1.05f, across);
                var side = left < right ? -1f : 1f;
                float lug;
                if (crawler)
                    lug = DecalSmooth(0.2f, 0.35f, Mathf.Abs(Mathf.PosMod(v * 10f, 1f) - 0.5f)); // plates across the track
                else
                    lug = DecalSmooth(0.25f, 0.4f, Mathf.Abs(Mathf.PosMod(v * 14f + side * across * 0.6f, 1f) - 0.5f)); // chevrons
                var n = Noise2(x / 4f, y / 4f, 32);
                height[y * w + x] = -inStrip * (0.6f + 0.4f * lug) + DecalSmooth(0.95f, 1.2f, across) * (1f - DecalSmooth(1.2f, 1.6f, across)) * 0.35f;
                alpha[y * w + x] = inStrip * (0.55f + 0.45f * lug) * (0.75f + 0.25f * n);
            }
        var albedo = new byte[w * h * 4];
        var normal = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                var b = (byte)(170 + 60 * Noise2(x / 3f, y / 3f, 64));
                albedo[i * 4] = albedo[i * 4 + 1] = albedo[i * 4 + 2] = b;
                albedo[i * 4 + 3] = (byte)(Mathf.Clamp(alpha[i], 0f, 1f) * 255f);
                // Central differences (wrapping along v), OpenGL convention like the terrain textures.
                var hx = height[y * w + Math.Min(w - 1, x + 1)] - height[y * w + Math.Max(0, x - 1)];
                var hy = height[((y + 1) % h) * w + x] - height[((y - 1 + h) % h) * w + x];
                var nv = new Vector3(-hx * 2.2f, hy * 2.2f, 1f).Normalized();
                normal[i * 4] = (byte)((nv.X * 0.5f + 0.5f) * 255f);
                normal[i * 4 + 1] = (byte)((nv.Y * 0.5f + 0.5f) * 255f);
                normal[i * 4 + 2] = (byte)((nv.Z * 0.5f + 0.5f) * 255f);
                normal[i * 4 + 3] = 255;
            }
        var a = Image.CreateFromData(w, h, false, Image.Format.Rgba8, albedo);
        a.GenerateMipmaps();
        var nm = Image.CreateFromData(w, h, false, Image.Format.Rgba8, normal);
        nm.GenerateMipmaps(true);
        return (ImageTexture.CreateFromImage(a), ImageTexture.CreateFromImage(nm));
    }
}
