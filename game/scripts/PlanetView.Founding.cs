using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Founding outposts on the ground (sim/Surface/Founding.cs), render-only. A construction rover that unfolds on its
// site first rolls from the tile centre onto the spot where the new hub's repair pad will be, drops its jacks, opens the
// roof dock, slews and raises its crane and swings the folded hub off its deck onto the ground (PoseConstructor, by the
// sim's DeployFraction, so packing up plays it backwards). Once the sim has used it up, a stand-in of it (FounderProp)
// stays parked there fully unfolded, the panels gone into the build: ConstructionView launches the exocomps from its
// roof dock and brings them back there. When the hub is finished the rover fades out on its new pad; when the hub is
// cancelled the stand-in goes and the sim's rover (back on the map, packing up) takes its place in the same pose.
// Also the red spacing ring shown while picking a site (ShowSpacing).
public partial class PlanetView
{
    private MeshInstance3D? _spacingRing;

    private sealed class FounderProp
    {
        public required Node3D Model;
        public float Fade = -1f; // seconds since the hub was finished (-1 = still building)
    }

    private readonly Dictionary<int, FounderProp> _founderProps = new();

    /// <summary>How long the stand-in waits after the hub is finished (the exocomps fly home to it) before it fades.</summary>
    private const float FounderLinger = 4f;
    private const float FounderFade = 1.6f;

    /// <summary>Where the construction rover parks to unfold: the new hub's repair pad (a founded core faces 0).</summary>
    public Vector3 FoundSpot(int tile) => TileCentre(tile) + BuildingModels.RepairPadOffset;

    /// <summary>A red ring of hexes just inside the founding spacing around each of the player's colony cores: a new
    /// outpost has to go outside it.</summary>
    public void ShowSpacing(IReadOnlyList<int> cores, int minDistance)
    {
        if (_spacingRing == null)
        {
            _spacingRing = new MeshInstance3D { Name = "SpacingRing" };
            AddChild(_spacingRing);
        }
        foreach (var c in _spacingRing.GetChildren()) c.QueueFree();
        var tiles = new List<int>();
        var seen = new HashSet<int>();
        var g = _surface.Map.Grid;
        foreach (var core in cores)
            foreach (var t in g.Within(core, minDistance - 1))
                if (g.Distance(t, core) == minDistance - 1 && seen.Add(t)) tiles.Add(t);
        _spacingRing.Visible = tiles.Count > 0;
        if (tiles.Count == 0) return;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CylinderMesh { TopRadius = HexRadius * 0.8f, BottomRadius = HexRadius * 0.8f, Height = 0.12f, RadialSegments = 6, Rings = 1 },
            InstanceCount = tiles.Count,
        };
        for (var i = 0; i < tiles.Count; i++) mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, TileCentre(tiles[i]) + new Vector3(0, 0.25f, 0)));
        _spacingRing.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Overlay(new Color(1f, 0.35f, 0.3f, 0.16f)) });
    }

    public void HideSpacing()
    {
        if (_spacingRing != null) _spacingRing.Visible = false;
    }

    private static float Seg(float f, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp((f - from) / (to - from), 0f, 1f));

    private static readonly StringName RestMeta = "construction_rest";

    private static void Pose(Node3D model, string part, System.Func<Transform3D, Transform3D> pose)
    {
        if (model.FindChild(part, true, false) is not Node3D node) return;
        if (!node.HasMeta(RestMeta)) node.SetMeta(RestMeta, node.Transform);
        node.Transform = pose(node.GetMeta(RestMeta).AsTransform3D());
    }

    /// <summary>
    /// The construction rover's unfolding, f 0 (driving) to 1 (unfolded): jacks down 0.2–0.35, dock hatch 0.25–0.45, crane
    /// slew 0.35–0.55 and luff 0.4–0.62 (the hook hangs straight), the folded hub lifted off the deck and set down beside
    /// it 0.55–0.95; panelsGone hides the hub (it has gone into the build).
    /// </summary>
    public static void PoseConstructor(Node3D model, float f, bool panelsGone)
    {
        var jack = Seg(f, 0.2f, 0.35f) * BuildingModels.ConstructorJackDrop;
        for (var i = 0; i < 4; i++) Pose(model, $"Jack{i}", r => r.Translated(new Vector3(0, -jack, 0)));
        var lid = Seg(f, 0.25f, 0.45f) * BuildingModels.ConstructorLidOpen;
        Pose(model, "DockLid", r => r * new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(lid)), Vector3.Zero));
        var slew = Seg(f, 0.35f, 0.55f) * BuildingModels.ConstructorSlew;
        Pose(model, "Crane", r => r * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(slew)), Vector3.Zero));
        var luff = Seg(f, 0.4f, 0.62f) * BuildingModels.ConstructorLuff;
        Pose(model, "Boom", r => r * new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(luff)), Vector3.Zero));
        Pose(model, "Hook", r => r * new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(-luff)), Vector3.Zero));
        var lift = Seg(f, 0.55f, 0.95f);
        Pose(model, "Panels", r =>
        {
            // Up off the deck, out to the left (where the hub goes), down onto the ground.
            var o = r.Origin;
            var to = new Vector3(-4.6f, 0.05f, o.Z - 0.6f);
            var p = o.Lerp(to, lift) + new Vector3(0, Mathf.Sin(lift * Mathf.Pi) * 2.2f, 0);
            return new Transform3D(r.Basis, p);
        });
        if (model.FindChild("Panels", true, false) is Node3D panels) panels.Visible = !panelsGone;
    }

    /// <summary>A construction rover in the sim, every frame: while it unfolds or folds up, it stands between the tile
    /// centre and the pad spot (it rolls over first) and is posed by the deploy fraction.</summary>
    private void AnimateConstructor(Node3D model, SurfaceUnit u, float delta)
    {
        var f = _surface.DeployFraction(u).ToFloat();
        PoseConstructor(model, f, false);
        if (u.Mode is not (UnitMode.Deploying or UnitMode.Packing)) return;
        var roll = Seg(f, 0f, 0.2f);
        model.Position = SimPoint(u.Pos).Lerp(FoundSpot(u.Tile), roll) + new Vector3(0, 0.1f, 0); // the sim parks it on the centre
        if (roll > 0.02f) model.Basis = model.Basis.Orthonormalized().Slerp(new Basis(Vector3.Up, Mathf.DegToRad(BuildingModels.RepairPadYaw)), Mathf.Min(1f, delta * 4f));
    }

    /// <summary>Label text for a construction rover ("" when nothing to say).</summary>
    private string FounderLabel(SurfaceUnit u)
    {
        if (_surface.Unfolding(u)) return $"Unfolding {Mathf.RoundToInt(_surface.DeployFraction(u).ToFloat() * 100)}%";
        return u.Mode == UnitMode.Packing ? "Folding up" : "";
    }

    /// <summary>The used-up rovers of founded cores: parked, unfolded stand-ins with the exocomp dock, until the hub is done.</summary>
    private void UpdateFounders(float delta)
    {
        var live = new HashSet<int>();
        foreach (var s in _surface.Structures)
        {
            if (s.Founder is not { } rover) continue;
            var complete = s.State == StructureState.Complete;
            if (!_founderProps.TryGetValue(s.Id, out var prop))
            {
                if (complete) continue; // finished before we saw it (a loaded game): nothing to show
                var model = BuildingModels.Constructor(PlanetDriver.ColorOf(s.Owner));
                RoverLook.Paint(model, rover.Name, rover.Number);
                if (model.FindChild("Selected", false, false) is Node3D ring) ring.Visible = false;
                AddChild(model);
                model.Position = FoundSpot(s.Tile) + new Vector3(0, 0.1f, 0);
                model.Basis = new Basis(Vector3.Up, Mathf.DegToRad(BuildingModels.RepairPadYaw));
                PoseConstructor(model, 1f, true);
                prop = new FounderProp { Model = model };
                _founderProps[s.Id] = prop;
            }
            live.Add(s.Id);
            if (prop.Model.FindChild("Dock", true, false) is Node3D dock) _construction.Docks[s.Id] = dock.GlobalPosition;
            if (!complete) continue;
            prop.Fade = prop.Fade < 0f ? 0f : prop.Fade + delta;
            var t = Mathf.Clamp((prop.Fade - FounderLinger) / FounderFade, 0f, 1f);
            if (t > 0f) SetFade(prop.Model, t);
            if (t >= 1f) live.Remove(s.Id); // faded out: free it below
        }
        foreach (var id in new List<int>(_founderProps.Keys))
        {
            if (live.Contains(id)) continue;
            _founderProps[id].Model.QueueFree();
            _founderProps.Remove(id);
            _construction.Docks.Remove(id);
        }
    }

    /// <summary>Fades every mesh of a model out (0 = solid, 1 = gone) with the per-instance transparency.</summary>
    private static void SetFade(Node node, float t)
    {
        if (node is GeometryInstance3D g) g.Transparency = t;
        if (node is Light3D light) light.Visible = t < 0.5f;
        foreach (var child in node.GetChildren()) SetFade(child, t);
    }
}
