using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// People never walk through anything (owner 2026-11-08: "people can walk through vehicles. Make sure that they can't
// walk through anything"). Render-only. Once a frame the view gathers what stands on the ground as blockers:
//   * every shown building model, vehicle model, founder stand-in and grounded drop ship, as an oriented box fitted to
//     its meshes in model space (flat pads and decals under 0.35 tall are left out, so people still cross the repair
//     pad and aprons), plus a disc round a deployed crawler / unfolding constructor (its legs and jacks reach out);
//   * trees, rocks and bushes from the scatter (a disc per instance: a trunk for trees, most of the footprint for rocks
//     and bushes; grass, ferns and pebbles don't block), looked up by tile;
//   * every other person (walkers and repair crews), as soft discs that share the push.
// Walkers steer round blockers ahead (sidestep along the blocker's edge), wait for a moving vehicle to pass, give up on
// a waypoint they can't reach, and after every step are pushed out of anything they overlap. Repair crews follow the
// sim's timing as before and get the same push-out. Vehicles (sim) don't know about people: a vehicle driving into
// someone shoves them aside.
public partial class PlanetView
{
    private const float PersonRadius = 0.38f;
    private const float PersonGap = 0.12f;         // a little air between a person and what they walk round
    private const float FlatBelow = 0.35f;          // meshes whose top is lower than this don't block (pads, decals)
    private const string BoxMeta = "people_box";

    private struct Blocker
    {
        public Vector2 C, X, Z;  // centre, unit axes (ground plane)
        public Vector2 Half;     // half extents along X, Z (box) — or (radius, 0) for a disc
        public bool Disc, Moving;
        public float Reach;      // centre to farthest point, for a quick distance check
    }

    private readonly List<Blocker> _blockers = new();
    private readonly List<Node3D> _people = new();
    private readonly List<Node3D> _groundedShips = new();
    private ulong _blockersFrame = ulong.MaxValue;

    // scatter blockers by tile: (position, radius, batch, index)
    private readonly Dictionary<int, List<(Vector2 P, float R, ScatterBatch B, int I)>> _scatterBlockers = new();
    private readonly HashSet<(ScatterBatch, int)> _scatterHiddenSet = new();

    /// <summary>Register a near scatter batch's trees, rocks and bushes as blockers (from PlanetView.Scatter MakeBatch).</summary>
    private void AddScatterBlockers(ScatterBatch batch, ScatterBuilder b, Aabb meshBox)
    {
        var half = Mathf.Max(Mathf.Max(Mathf.Abs(meshBox.Position.X), Mathf.Abs(meshBox.End.X)),
            Mathf.Max(Mathf.Abs(meshBox.Position.Z), Mathf.Abs(meshBox.End.Z)));
        float baseRadius = b.Kind switch
        {
            ScatterKind.Tree => b.Mesh.StartsWith("snag") ? 0.3f : 0.42f,  // the trunk; people pass under the crown
            ScatterKind.Rock => half * 0.8f,
            ScatterKind.Plant => b.Mesh is "fern" ? 0f : half * 0.6f,      // bushes and shrubs, not ferns
            _ => 0f,                                                        // grass, scree, pebbles
        };
        if (baseRadius < 0.12f) return;
        for (var i = 0; i < batch.World.Length; i++)
        {
            var basis = b.World[i].Basis;
            var scale = Mathf.Max(basis.X.Length(), basis.Z.Length());
            var r = baseRadius * scale;
            if (r < 0.12f) continue;
            var p = batch.World[i];
            var tile = TileAt(p);
            if (tile < 0) continue;
            if (!_scatterBlockers.TryGetValue(tile, out var list)) _scatterBlockers[tile] = list = new();
            list.Add((new Vector2(p.X, p.Z), r, batch, i));
        }
    }

    /// <summary>Gather this frame's blockers and people once (any number of calls per frame).</summary>
    private void EnsureBlockers()
    {
        var frame = Engine.GetProcessFrames();
        if (frame == _blockersFrame) return;
        _blockersFrame = frame;
        _blockers.Clear();
        _people.Clear();

        foreach (var (_, v) in _structures)
            if (v.Model.Visible && v.Model.IsInsideTree()) AddBoxBlocker(v.Model, false);
        foreach (var (_, prop) in _founderProps)
            if (prop.Model.Visible && prop.Model.IsInsideTree()) AddBoxBlocker(prop.Model, false);

        foreach (var u in _surface.Units)
        {
            if (!_rovers.TryGetValue(u.Id, out var model) || !model.Visible || !model.IsInsideTree()) continue;
            AddBoxBlocker(model, Rolling(u));
            if (u.Mode != UnitMode.Mobile)
            {
                var r = _sim.Data.DeployedFootprintOf(u.DefId).ToFloat() * 0.6f;
                var p = model.GlobalPosition;
                _blockers.Add(new Blocker { C = new Vector2(p.X, p.Z), Half = new Vector2(r, 0), Disc = true, Reach = r });
            }
        }

        _groundedShips.Clear();
        _dropShips.GroundedModels(_groundedShips);
        foreach (var m in _groundedShips) if (m.IsInsideTree()) AddBoxBlocker(m, true);

        foreach (var w in _walkers) if (w.Model.Visible) _people.Add(w.Model);
        foreach (var (_, crew) in _buildingCrews) foreach (var p in crew) if (p.Visible) _people.Add(p);
        foreach (var (_, crew) in _crews) foreach (var p in crew.People) if (p.Visible) _people.Add(p);
    }

    /// <summary>An oriented ground box round a model, from its meshes in model space (cached on the model).</summary>
    private void AddBoxBlocker(Node3D model, bool moving)
    {
        var local = LocalBox(model);
        if (local.Size.X <= 0f) return;
        var g = model.GlobalTransform;
        var ax = new Vector2(g.Basis.X.X, g.Basis.X.Z);
        var az = new Vector2(g.Basis.Z.X, g.Basis.Z.Z);
        var sx = ax.Length();
        var sz = az.Length();
        if (sx < 1e-4f || sz < 1e-4f) return;
        var centreLocal = local.Position + local.Size * 0.5f;
        var centre = g * new Vector3(centreLocal.X, 0, centreLocal.Y);
        var half = new Vector2(local.Size.X * 0.5f * sx, local.Size.Y * 0.5f * sz);
        _blockers.Add(new Blocker
        {
            C = new Vector2(centre.X, centre.Z), X = ax / sx, Z = az / sz, Half = half, Moving = moving,
            Reach = half.Length(),
        });
    }

    /// <summary>The model's footprint in its own X/Z (Rect2: position = min corner, size), from every visible mesh taller
    /// than <see cref="FlatBelow"/>; the repair pad, labels and particles are left out. Cached on the model.</summary>
    private static Rect2 LocalBox(Node3D model)
    {
        if (model.HasMeta(BoxMeta)) return model.GetMeta(BoxMeta).AsRect2();
        var have = false;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        void Walk(Node node, Transform3D toModel)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Node3D n || !n.Visible) continue;
                var name = n.Name.ToString();
                // not solid: the repair pad (walked on), headlight cones, selection rings, effects
                if (name.StartsWith("RepairPad") || name.StartsWith("LightCone") || name.StartsWith("Selected")
                    || n is GpuParticles3D or CpuParticles3D) continue;
                var t = toModel * n.Transform;
                if (n is MeshInstance3D mi && mi.Mesh != null)
                {
                    var box = t * mi.Mesh.GetAabb();
                    if (box.End.Y >= FlatBelow && box.Size.Y > 0.05f)
                    {
                        min = new Vector2(Mathf.Min(min.X, box.Position.X), Mathf.Min(min.Y, box.Position.Z));
                        max = new Vector2(Mathf.Max(max.X, box.End.X), Mathf.Max(max.Y, box.End.Z));
                        have = true;
                    }
                }
                Walk(n, t);
            }
        }
        Walk(model, Transform3D.Identity);
        var rect = have ? new Rect2(min, max - min) : new Rect2(0, 0, 0, 0);
        if (have) model.SetMeta(BoxMeta, rect); // only cache a real box (a glb may still be loading its children)
        return rect;
    }

    /// <summary>Closest point of a blocker to p (p itself when inside) and whether p is inside.</summary>
    private static Vector2 Closest(in Blocker b, Vector2 p, out bool inside)
    {
        var d = p - b.C;
        if (b.Disc)
        {
            var len = d.Length();
            inside = len < b.Half.X;
            return len < 1e-5f ? b.C + new Vector2(b.Half.X, 0) : b.C + d / len * b.Half.X;
        }
        var lx = d.Dot(b.X);
        var lz = d.Dot(b.Z);
        inside = Mathf.Abs(lx) < b.Half.X && Mathf.Abs(lz) < b.Half.Y;
        if (inside)
        {
            // nearest face
            var ox = b.Half.X - Mathf.Abs(lx);
            var oz = b.Half.Y - Mathf.Abs(lz);
            if (ox < oz) lx = Mathf.Sign(lx == 0 ? 1 : lx) * b.Half.X;
            else lz = Mathf.Sign(lz == 0 ? 1 : lz) * b.Half.Y;
        }
        else
        {
            lx = Mathf.Clamp(lx, -b.Half.X, b.Half.X);
            lz = Mathf.Clamp(lz, -b.Half.Y, b.Half.Y);
        }
        return b.C + b.X * lx + b.Z * lz;
    }

    /// <summary>The blocker (box, disc or scatter) a person standing at p would overlap, if any: its push-out normal and
    /// depth. Moving tells whether it is a driving vehicle.</summary>
    private bool Blocked(Vector2 p, float r, out Vector2 normal, out float depth, out bool moving)
    {
        normal = Vector2.Zero;
        depth = 0f;
        moving = false;
        var best = 0f;
        foreach (var b in _blockers)
        {
            if ((p - b.C).LengthSquared() > (b.Reach + r) * (b.Reach + r)) continue;
            var c = Closest(b, p, out var inside);
            var d = p - c;
            var dist = d.Length();
            float pen;
            Vector2 n;
            if (inside) { pen = r + dist; n = dist > 1e-5f ? -d / dist : new Vector2(1, 0); }
            else if (dist < r) { pen = r - dist; n = dist > 1e-5f ? d / dist : new Vector2(1, 0); }
            else continue;
            if (pen > best) { best = pen; normal = n; moving = b.Moving; }
        }
        var tile = TileAt(new Vector3(p.X, 0, p.Y));
        if (tile >= 0)
        {
            ScanScatter(tile, p, r, ref best, ref normal, ref moving);
            // a neighbour's trees only matter near the shared edge (inradius 12.5 + the biggest rock + a person)
            foreach (var n in _surface.Map.Grid.Neighbours(tile))
            {
                var c = TileCentre(n);
                if ((new Vector2(c.X, c.Z) - p).LengthSquared() < 17.5f * 17.5f) ScanScatter(n, p, r, ref best, ref normal, ref moving);
            }
        }
        depth = best;
        return best > 0f;
    }

    private void ScanScatter(int tile, Vector2 p, float r, ref float best, ref Vector2 normal, ref bool moving)
    {
        if (!_scatterBlockers.TryGetValue(tile, out var list)) return;
        foreach (var (q, rr, batch, index) in list)
        {
            var d = p - q;
            var reach = rr + r;
            var len2 = d.LengthSquared();
            if (len2 >= reach * reach || _scatterHiddenSet.Contains((batch, index))) continue;
            var len = Mathf.Sqrt(len2);
            var pen = reach - len;
            if (pen <= best) continue;
            best = pen;
            normal = len > 1e-5f ? d / len : new Vector2(1, 0);
            moving = false;
        }
    }

    /// <summary>Move a person standing at p out of everything they overlap (a few passes), and a little apart from other
    /// people. Returns the corrected ground point (Y on the ground).</summary>
    private Vector3 PushOut(Node3D self, Vector3 pos)
    {
        EnsureBlockers();
        var p = new Vector2(pos.X, pos.Z);
        const float r = PersonRadius + PersonGap;
        for (var pass = 0; pass < 4; pass++)
        {
            if (!Blocked(p, r, out var n, out var depth, out _)) break;
            p += n * (depth + 0.01f);
        }
        // people: soft, each takes half the overlap
        foreach (var other in _people)
        {
            if (other == self) continue;
            var o = new Vector2(other.Position.X, other.Position.Z);
            var d = p - o;
            var len = d.Length();
            const float apart = PersonRadius * 2f;
            if (len >= apart) continue;
            p += (len > 1e-4f ? d / len : new Vector2(0.7f, 0.7f)) * (apart - len) * 0.5f;
        }
        // one more hard pass so the soft push never ends inside something
        if (Blocked(p, r, out var n2, out var depth2, out _)) p += n2 * (depth2 + 0.01f);
        var result = new Vector3(p.X, 0, p.Y);
        result.Y = GroundAt(result);
        return result;
    }

    /// <summary>Steer a walker heading along <paramref name="dir"/> (unit, ground plane) round whatever is just ahead.
    /// Returns the direction to take, or null to stand still (a moving vehicle is in the way).</summary>
    private Vector2? Steer(Vector2 p, Vector2 dir, float look)
    {
        EnsureBlockers();
        const float r = PersonRadius + PersonGap;
        var ahead = p + dir * look;
        if (!Blocked(ahead, r, out var n, out _, out var moving)) return dir;
        if (moving) return null;
        // slide along the blocker: the tangent on the side that keeps us going forward
        var t = new Vector2(-n.Y, n.X);
        if (t.Dot(dir) < 0) t = -t;
        var steer = (t + n * 0.35f + dir * 0.15f).Normalized();
        // if that way is blocked too, try the other side
        if (Blocked(p + steer * look, r, out _, out _, out _))
        {
            var other = (-t + n * 0.35f + dir * 0.15f).Normalized();
            if (!Blocked(p + other * look, r, out _, out _, out _)) steer = other;
        }
        return steer;
    }

    /// <summary>A point people can stand on near <paramref name="spot"/>: pushed out of anything there.</summary>
    private Vector3 FreeSpotNear(Vector3 spot)
    {
        EnsureBlockers();
        var p = new Vector2(spot.X, spot.Z);
        const float r = PersonRadius + PersonGap;
        for (var pass = 0; pass < 6; pass++)
        {
            if (!Blocked(p, r, out var n, out var depth, out _)) break;
            p += n * (depth + 0.05f);
        }
        return new Vector3(p.X, spot.Y, p.Y);
    }
}
