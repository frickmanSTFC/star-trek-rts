using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Crew walking between a base's buildings (owner 2026-11-08: "put the crew into the game walking between buildings").
// Render-only: the sim has no people, so nothing here reads or changes sim state beyond looking at finished buildings.
//
// Each finished base gets 2 + one per finished building of crew (max WalkersPerBase; WalkerBudget over the whole map),
// CrewModels figures (blender/build_crew.py) in the owner's colour with a look seeded by base + slot. A walker picks
// another stop in its base (a building's door/access spot, the core's door, or a stroll point on an empty land tile
// next to the core), walks there along hex-edge crossings (like the vehicles' lanes, a little off the middle so
// people pass), bending round every building's footprint on a ring, does a job there for a few seconds (Work at
// industry, Scan at labs and mines, Idle/Wave at the core) and goes on. From storage, a refinery or a foundry they
// often carry a crate to the next stop. They come out of the core's door when a base is new; when a base goes they go.
// Rival crews show only in sight; figures beyond WalkerRange of the eye are hidden and their animation stops.
// On a hostile world (PlanetView.Domes.cs NeedsSuit: a Domes world outside every dome/field of theirs) a walker swaps to
// an EVA-suited figure (CrewModels role "eva", same seed) as it crosses out of cover, and back to its own role inside.
public partial class PlanetView
{
    private const int WalkersPerBase = 8;
    private const int WalkerBudget = 48;
    private const float WalkerRange = 520f;
    private const float WalkerClearance = 1.6f; // walk this far outside a building's footprint ring
    private const float StopsRefresh = 1f; // seconds between re-reading each base's stops

    private sealed class Walker
    {
        public required Node3D Model; // the figure showing now (Shirt, or Suit outside cover)
        public required Node3D Shirt; // the walker's own role
        public Node3D? Suit;          // EVA suit, made the first time it's needed
        public required string Role;
        public required int Seed;
        public bool Suited;
        public int SuitTile = -2;
        public required int BaseId;
        public required int Owner;
        public required int Slot;
        public required RandomNumberGenerator Rng;
        public float Height = 1f;
        public float Side; // lateral lane offset, metres right of travel
        public readonly List<Vector3> Path = new();
        public int Leg;
        public float Wait;
        public int StopTile = -1;
        public string Moving = "Walk";
        public string Action = "";
        public bool Active = true;
    }

    private readonly record struct Stop(int Tile, Vector3 Spot, Vector3 Face, string Job);

    private readonly List<Walker> _walkers = new();
    private readonly Dictionary<int, List<Stop>> _baseStops = new();
    private float _stopsAge = StopsRefresh;
    private Vector3 _eye;

    /// <summary>Crew on foot round the bases; called once a frame after the structures are updated.</summary>
    private void UpdateWalkers(float delta)
    {
        _stopsAge += delta;
        if (_stopsAge >= StopsRefresh)
        {
            _stopsAge = 0f;
            RefreshStops();
        }

        for (var i = _walkers.Count - 1; i >= 0; i--)
        {
            var w = _walkers[i];
            if (_baseStops.ContainsKey(w.BaseId)) continue;
            w.Shirt.QueueFree();
            w.Suit?.QueueFree();
            _walkers.RemoveAt(i);
        }

        foreach (var (baseId, stops) in _baseStops)
        {
            var core = _surface.FindStructure(baseId);
            if (core == null) continue;
            var want = System.Math.Min(WalkersPerBase, 1 + stops.Count);
            var have = 0;
            foreach (var w in _walkers) if (w.BaseId == baseId) have++;
            for (var slot = have; slot < want && _walkers.Count < WalkerBudget; slot++)
                _walkers.Add(NewWalker(core, slot, stops));
        }

        foreach (var w in _walkers) StepWalker(w, delta);
    }

    /// <summary>Every finished base's stops: its core's door, each finished building's door or access spot, and up to
    /// two stroll points on empty land next to the core.</summary>
    private void RefreshStops()
    {
        _baseStops.Clear();
        foreach (var s in _surface.Structures)
        {
            if (s.State != StructureState.Complete || s.BaseId == 0) continue;
            var core = _surface.FindStructure(s.BaseId);
            if (core == null || core.State != StructureState.Complete) continue;
            if (!_baseStops.TryGetValue(core.Id, out var list)) _baseStops[core.Id] = list = new List<Stop>();
            var def = _sim.Data.Building(s.DefId);
            var centre = TileCentre(s.Tile);
            if (s.Id == core.Id)
            {
                list.Add(new Stop(s.Tile, CoreLocal(core, new Vector3(0, 0, 6.4f)), centre, "core"));
                continue;
            }
            var spot = _surface.AccessPoint(s.Tile) is { } a
                ? new Vector3(a.X.ToFloat(), 0, a.Y.ToFloat())
                : centre + new Basis(Vector3.Up, Mathf.DegToRad(-60f * s.Facing)) * new Vector3(0, 0, _surface.BlockRadius(s.Tile).ToFloat() + 1.2f);
            var job = def.IsLab ? "lab" : def.RequiresDeposit ? "mine" : def.IsFactory ? "bay"
                : def.Storage > Fix64.Zero && def.Recipes.Count == 0 ? "storage" : def.Recipes.Count > 0 ? "industry" : "other";
            list.Add(new Stop(s.Tile, spot, centre, job));
        }
        foreach (var (baseId, list) in _baseStops)
        {
            var core = _surface.FindStructure(baseId)!;
            var strolls = 0;
            foreach (var n in _surface.Map.Grid.Neighbours(core.Tile))
            {
                if (strolls >= 2) break;
                if (!_surface.Map.Terrain(n).Passable || _surface.StructureAt(n) != null) continue;
                var c = TileCentre(n);
                var jitter = new Vector3(((n * 37) % 7 - 3) * 0.9f, 0, ((n * 53) % 7 - 3) * 0.9f);
                list.Add(new Stop(n, c + jitter, TileCentre(core.Tile), "stroll")); // Face = the core; they look away from it
                strolls++;
            }
        }
    }

    private Walker NewWalker(SurfaceStructure core, int slot, List<Stop> stops)
    {
        var seed = unchecked(core.Id * 7919 + slot * 104729 + 17);
        var rng = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
        var roll = rng.Randf();
        var role = roll < 0.34f ? "engineer" : roll < 0.58f ? "science" : roll < 0.74f ? "security" : "colonist";
        var model = CrewModels.Build(role, PlanetDriver.ColorOf(core.Owner), seed);
        var door = stops.Count > 0 && stops[0].Job == "core" ? stops[0].Spot : TileCentre(core.Tile);
        door.Y = GroundAt(door);
        model.Position = door;
        AddChild(model);
        var w = new Walker
        {
            Model = model,
            Shirt = model,
            Role = role,
            Seed = seed,
            BaseId = core.Id,
            Owner = core.Owner,
            Slot = slot,
            Rng = rng,
            Height = CrewModels.Variant(seed, role).Height,
            Side = rng.RandfRange(0.8f, 2.6f) * (rng.Randf() < 0.5f ? -1f : 1f),
            StopTile = core.Tile,
            Wait = slot * 1.1f + rng.RandfRange(0f, 1.5f), // file out of the door one after another
        };
        return w;
    }

    private void StepWalker(Walker w, float delta)
    {
        var pos = w.Model.Position;
        var tile = TileAt(pos);
        SuitWalker(w, tile);
        var shown = tile >= 0 && ShowsLive(w.Owner, tile);
        var near = pos.DistanceSquaredTo(_eye) < WalkerRange * WalkerRange;
        var active = shown && near;
        if (active != w.Active)
        {
            w.Active = active;
            w.Model.Visible = active;
            w.Model.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        }

        if (w.Wait > 0f)
        {
            w.Wait -= delta;
            if (w.Wait > 0f) return;
            if (!PlanTrip(w)) { w.Wait = 2f; return; }
        }

        if (w.Path.Count == 0)
        {
            if (!PlanTrip(w)) w.Wait = 2f;
            return;
        }
        if (w.Leg >= w.Path.Count)
        {
            Arrive(w);
            return;
        }

        var speed = CrewModels.SpeedOf(w.Moving) * w.Height;
        var target = w.Path[w.Leg];
        var flat = new Vector3(target.X - pos.X, 0, target.Z - pos.Z);
        var dist = flat.Length();
        var step = speed * delta;
        if (dist <= step)
        {
            pos = new Vector3(target.X, pos.Y, target.Z);
            w.Leg++;
        }
        else pos += flat / dist * step;
        pos.Y = GroundAt(pos);
        w.Model.Position = pos;
        if (dist > 0.01f) Face(w, flat, delta);
    }

    /// <summary>Swap the walker into (or out of) an EVA suit when it crosses the edge of cover: the other figure takes
    /// over its place, facing, visibility and current action.</summary>
    private void SuitWalker(Walker w, int tile)
    {
        if (tile == w.SuitTile) return;
        w.SuitTile = tile;
        var suit = NeedsSuit(w.Owner, tile);
        if (suit == w.Suited) return;
        w.Suited = suit;
        Node3D next;
        if (suit)
        {
            if (w.Suit == null)
            {
                w.Suit = CrewModels.Build("eva", PlanetDriver.ColorOf(w.Owner), w.Seed);
                AddChild(w.Suit);
            }
            next = w.Suit;
        }
        else next = w.Shirt;
        var old = w.Model;
        next.Transform = old.Transform;
        next.Visible = old.Visible;
        next.ProcessMode = old.ProcessMode;
        old.Visible = false;
        old.ProcessMode = ProcessModeEnum.Disabled;
        w.Model = next;
        w.Height = CrewModels.Variant(w.Seed, suit ? "eva" : w.Role).Height;
        var walking = w.Path.Count > 0 && w.Leg < w.Path.Count && w.Wait <= 0f;
        CrewModels.Play(next, walking ? w.Moving : w.Action == "" ? "Idle" : w.Action, 0f);
    }

    private void Face(Walker w, Vector3 dir, float delta)
    {
        dir.Y = 0;
        if (dir.LengthSquared() < 1e-4f) return;
        // the figures' front is -Z, which is what Basis.LookingAt points
        var want = Basis.LookingAt(dir.Normalized(), Vector3.Up);
        w.Model.Basis = w.Model.Basis.Orthonormalized().Slerp(want, Mathf.Min(1f, delta * 8f));
    }

    /// <summary>Pick the next stop in the walker's base and lay out the walk there.</summary>
    private bool PlanTrip(Walker w)
    {
        if (!_baseStops.TryGetValue(w.BaseId, out var stops) || stops.Count == 0) return false;
        Stop next = default;
        var found = false;
        for (var tries = 0; tries < 6 && !found; tries++)
        {
            var s = stops[w.Rng.RandiRange(0, stops.Count - 1)];
            if (s.Tile == w.StopTile && stops.Count > 1) continue;
            next = s;
            found = true;
        }
        if (!found) next = stops[0];

        var from = w.Model.Position;
        var startTile = TileAt(from);
        var tiles = WalkTiles(startTile, next.Tile);
        if (tiles == null) return false;

        // stand a little apart at the stop, so several people don't stack on one spot
        var spot = next.Spot + new Vector3(w.Rng.RandfRange(-1.4f, 1.4f), 0, w.Rng.RandfRange(-1.4f, 1.4f));
        w.Path.Clear();
        w.Leg = 0;
        var at = from;
        for (var i = 0; i + 1 < tiles.Count; i++)
        {
            var a = TileCentre(tiles[i]);
            var b = TileCentre(tiles[i + 1]);
            var dir = new Vector3(b.X - a.X, 0, b.Z - a.Z).Normalized();
            var right = new Vector3(-dir.Z, 0, dir.X);
            var crossing = (a + b) * 0.5f + right * w.Side;
            AddWalkSegment(w.Path, at, crossing, tiles[i]);
            at = crossing;
        }
        AddWalkSegment(w.Path, at, spot, next.Tile);
        w.StopTile = next.Tile;

        var fromStop = FindStopAt(stops, startTile);
        w.Moving = fromStop is { Job: "storage" or "industry" } && w.Rng.Randf() < 0.6f ? "Carry" : "Walk";
        w.Action = next.Job switch
        {
            "industry" or "bay" => "Work",
            "lab" or "mine" or "stroll" => w.Rng.Randf() < 0.6f ? "Scan" : "Idle",
            "core" => w.Rng.Randf() < 0.15f ? "Wave" : "Idle",
            "storage" => w.Rng.Randf() < 0.5f ? "Work" : "Idle",
            _ => "Idle",
        };
        CrewModels.Play(w.Model, w.Moving);
        return true;
    }

    private static Stop? FindStopAt(List<Stop> stops, int tile)
    {
        foreach (var s in stops) if (s.Tile == tile) return s;
        return null;
    }

    private void Arrive(Walker w)
    {
        w.Path.Clear();
        w.Leg = 0;
        CrewModels.Play(w.Model, w.Action);
        if (_baseStops.TryGetValue(w.BaseId, out var stops) && FindStopAt(stops, w.StopTile) is { } stop)
        {
            var face = stop.Job == "core" ? TileCentre(stop.Tile) - w.Model.Position : stop.Face - w.Model.Position;
            if (stop.Job == "stroll") face = -face; // look out, not back at the base
            face.Y = 0;
            if (face.LengthSquared() > 0.01f) w.Model.Basis = Basis.LookingAt(face.Normalized(), Vector3.Up);
        }
        w.Wait = w.Action switch
        {
            "Work" => w.Rng.RandfRange(6f, 14f),
            "Scan" => w.Rng.RandfRange(4f, 9f),
            "Wave" => w.Rng.RandfRange(2.5f, 4f),
            _ => w.Rng.RandfRange(3f, 10f),
        };
    }

    /// <summary>Tiles to walk through from one tile to another (both included), breadth-first over land, at most 10 steps.</summary>
    private List<int>? WalkTiles(int from, int to)
    {
        if (from < 0 || to < 0) return null;
        if (from == to) return new List<int> { from };
        var grid = _surface.Map.Grid;
        var back = new Dictionary<int, int> { [from] = from };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (grid.Distance(from, t) > 10) continue;
            foreach (var n in grid.Neighbours(t))
            {
                if (back.ContainsKey(n) || (!_surface.Map.Terrain(n).Passable && n != to)) continue;
                back[n] = t;
                if (n == to)
                {
                    var path = new List<int> { to };
                    for (var c = to; c != from; c = back[c]) path.Add(back[c]);
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(n);
            }
        }
        return null;
    }

    /// <summary>Walk from p to q inside one tile, going round whatever stands in its middle on a ring just outside its
    /// footprint (out from a door first, in to one last).</summary>
    private void AddWalkSegment(List<Vector3> path, Vector3 p, Vector3 q, int tile)
    {
        var radius = tile >= 0 ? _surface.BlockRadius(tile).ToFloat() : 0f;
        if (radius <= 0f)
        {
            path.Add(q);
            return;
        }
        var c = TileCentre(tile);
        var ring = radius + WalkerClearance;
        var a = new Vector2(p.X - c.X, p.Z - c.Z);
        var b = new Vector2(q.X - c.X, q.Z - c.Z);
        if (SegmentDistance(a, b) >= ring - 0.05f)
        {
            path.Add(q);
            return;
        }
        var angA = Mathf.Atan2(a.Y, a.X);
        var angB = Mathf.Atan2(b.Y, b.X);
        var turn = Mathf.Wrap(angB - angA, -Mathf.Pi, Mathf.Pi);
        var steps = System.Math.Max(1, (int)Mathf.Ceil(Mathf.Abs(turn) / Mathf.DegToRad(25f)));
        if (a.Length() < ring) path.Add(c + new Vector3(Mathf.Cos(angA), 0, Mathf.Sin(angA)) * ring);
        for (var k = 1; k < steps; k++)
        {
            var ang = angA + turn * k / steps;
            path.Add(c + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * ring);
        }
        if (b.Length() < ring) path.Add(c + new Vector3(Mathf.Cos(angB), 0, Mathf.Sin(angB)) * ring);
        path.Add(q);
    }

    /// <summary>Closest distance from the origin to the segment a–b.</summary>
    private static float SegmentDistance(Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len = ab.LengthSquared();
        var t = len < 1e-6f ? 0f : Mathf.Clamp(-a.Dot(ab) / len, 0f, 1f);
        return (a + ab * t).Length();
    }
}
