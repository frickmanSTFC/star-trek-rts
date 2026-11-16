using System;
using System.Collections.Generic;
using System.Text;
using FinalFrontier.Sim.Missions;
using Godot;

namespace FinalFrontier;

// Space-chain mission markers on the system plot (docs/ship-pick-contract.md C4; render-only): MissionBook.SpaceMarkers
// for the local player, read 4 times a second. Kind "beacon" = an amber ring round the body with a second ring pulsing
// out from it; "pickup" = a green arrow pointing up out of the body (take cargo from here); "dropoff" = a blue arrow
// pointing down into it (deliver here); "target" = a red ring with four ticks turning round it; "ship" = a lavender ring
// pulsing round the bound ship's chevron. Each has a label under its body or ship ("BEACON · Distress buoy"), stacked
// when a body has several. Markers on bodies or ships not on the plot (unknown, gone) are hidden. The nodes are rebuilt
// only when the list changes; per frame they only follow their body / ship and animate.
public partial class SystemPlot
{
    private static readonly Color BeaconAmber = new(1f, 0.74f, 0.3f);
    private static readonly Color PickupGreen = new(0.56f, 0.9f, 0.5f);
    private static readonly Color DropoffBlue = new(0.45f, 0.75f, 1f);
    private static readonly Color TargetRed = new(1f, 0.38f, 0.32f);
    private static readonly Color ShipLavender = new(0.78f, 0.66f, 1f);

    private sealed class MarkerNode
    {
        public SpaceMarker Marker = null!;
        public Node3D Root = null!;
        public Node3D? Spin; // target ticks, beacon pulse ring, arrows (animated part)
        public StandardMaterial3D? PulseMaterial;
        public Label3D Label = null!;
        public int Slot; // index among the markers on the same body / ship (labels stack)
        public float Phase; // so two markers don't pulse in step
    }

    private Node3D? _markerLayer;
    private readonly List<MarkerNode> _markerNodes = new();
    private string _markerKey = "";
    private ulong _markersReadAt;
    private bool _markersWarned;

    /// <summary>Every frame from Refresh (after the ships moved): read the markers now and then, rebuild on a change,
    /// then follow and animate.</summary>
    private void UpdateMarkers(SystemSource source)
    {
        var now = Time.GetTicksMsec();
        if (_markersReadAt == 0 || now - _markersReadAt >= 250)
        {
            _markersReadAt = now;
            IReadOnlyList<SpaceMarker> markers;
            try
            {
                markers = source.Sim.Missions.SpaceMarkers(source.Player);
            }
            catch (Exception e) // a stub or half-merged sim must never break the plot: warn once, show none
            {
                if (!_markersWarned) GD.PushWarning($"System plot: mission markers: {e.Message}");
                _markersWarned = true;
                markers = Array.Empty<SpaceMarker>();
            }
            var key = MarkerKey(markers);
            if (key != _markerKey)
            {
                _markerKey = key;
                BuildMarkers(markers);
            }
        }
        if (_markerNodes.Count == 0) return;
        var t = now / 1000f;
        foreach (var m in _markerNodes) PlaceMarker(m, t);
    }

    private static string MarkerKey(IReadOnlyList<SpaceMarker> markers)
    {
        if (markers.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var m in markers) sb.Append(m.Kind).Append('|').Append(m.BodyId).Append('|').Append(m.ShipId).Append('|').Append(m.Label).Append('\n');
        return sb.ToString();
    }

    private void BuildMarkers(IReadOnlyList<SpaceMarker> markers)
    {
        foreach (var m in _markerNodes) m.Root.QueueFree();
        _markerNodes.Clear();
        if (markers.Count == 0) return;
        if (_markerLayer == null)
        {
            _markerLayer = new Node3D { Name = "MissionMarkers" };
            AddChild(_markerLayer);
        }
        var slots = new Dictionary<string, int>();
        for (var i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            var where = m.Kind == "ship" ? $"ship:{m.ShipId}" : $"body:{m.BodyId}";
            var slot = slots.GetValueOrDefault(where);
            slots[where] = slot + 1;
            var node = NewMarker(m, i);
            node.Slot = slot;
            _markerLayer.AddChild(node.Root);
            _markerNodes.Add(node);
        }
    }

    private static (Color Colour, string Word) MarkerLook(string kind) => kind switch
    {
        "beacon" => (BeaconAmber, "BEACON"),
        "pickup" => (PickupGreen, "PICK UP"),
        "dropoff" => (DropoffBlue, "DROP OFF"),
        "target" => (TargetRed, "TARGET"),
        "ship" => (ShipLavender, "MEET"),
        _ => (Neutral, kind.ToUpperInvariant()),
    };

    private MarkerNode NewMarker(SpaceMarker m, int index)
    {
        var (colour, word) = MarkerLook(m.Kind);
        var root = new Node3D { Name = $"Marker{index}", Visible = false };
        var node = new MarkerNode { Marker = m, Root = root, Phase = index * 0.37f };
        switch (m.Kind)
        {
            case "pickup":
            case "dropoff":
            {
                // An arrow above the body: up and out for a pick-up, down into it for a drop-off.
                var arrow = new Node3D { Name = "Arrow" };
                var material = MarkerMaterial(colour, 2.2f);
                arrow.AddChild(new MeshInstance3D
                {
                    Name = "Head",
                    Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 1.5f, Height = 2.2f, RadialSegments = 12, Rings = 1 },
                    Position = new Vector3(0f, 1.1f, 0f),
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                arrow.AddChild(new MeshInstance3D
                {
                    Name = "Shaft",
                    Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.45f, Height = 2.6f, RadialSegments = 8, Rings = 1 },
                    Position = new Vector3(0f, -1.3f, 0f),
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                if (m.Kind == "dropoff") arrow.Basis = new Basis(Vector3.Right, Mathf.Pi); // point down
                root.AddChild(arrow);
                node.Spin = arrow;
                break;
            }
            case "target":
            {
                root.AddChild(Ring(colour, 1.6f, 1f, 1.07f));
                var ticks = new Node3D { Name = "Ticks" };
                var material = MarkerMaterial(colour, 2f);
                for (var k = 0; k < 4; k++)
                {
                    var a = k * Mathf.Pi / 2f;
                    ticks.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.04f, 0.3f) }, // at ring radius 1; the holder is scaled to the ring
                        Position = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.22f,
                        Basis = new Basis(Vector3.Up, a),
                        MaterialOverride = material,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                }
                root.AddChild(ticks);
                node.Spin = ticks;
                break;
            }
            default: // beacon, ship, anything new: a steady ring and one pulsing out of it
            {
                root.AddChild(Ring(colour, 1.8f, 1f, 1.07f));
                var pulse = Ring(colour, 2.4f, 1f, 1.05f);
                node.PulseMaterial = (StandardMaterial3D)pulse.MaterialOverride;
                var holder = new Node3D { Name = "Pulse" };
                holder.AddChild(pulse);
                root.AddChild(holder);
                node.Spin = holder;
                break;
            }
        }
        var text = string.IsNullOrEmpty(m.Label) ? word : $"{word} · {m.Label.ToUpperInvariant()}";
        node.Label = Label(text, Vector3.Zero, colour, 18);
        node.Label.VerticalAlignment = VerticalAlignment.Top;
        node.Label.Name = "Label";
        root.AddChild(node.Label);
        return node;
    }

    /// <summary>Follow the body / ship and animate (time in seconds).</summary>
    private void PlaceMarker(MarkerNode n, float t)
    {
        var m = n.Marker;
        Vector3 at;
        float radius;
        if (m.Kind == "ship")
        {
            if (m.ShipId < 0 || !_ships.TryGetValue(m.ShipId, out var ship))
            {
                n.Root.Visible = false;
                return;
            }
            at = ship.Node.Position;
            radius = 2.6f;
        }
        else if (m.BodyId != null && _nodes.TryGetValue(m.BodyId, out var body) && IsInstanceValid(body.Node))
        {
            at = body.Node.Position;
            radius = body.Radius;
        }
        else
        {
            n.Root.Visible = false;
            return;
        }
        n.Root.Visible = true;
        n.Root.Position = at;
        var phase = t + n.Phase;
        var ring = radius + 4f + n.Slot * 2.2f; // rings of several markers on one body sit one outside the other
        switch (m.Kind)
        {
            case "pickup":
            case "dropoff":
            {
                var bob = Mathf.Sin(phase * 2.4f) * 0.8f;
                // beside the body's top (the name label is straight over it), a little apart per slot
                n.Spin!.Position = new Vector3(radius + 2.6f + n.Slot * 3.4f, radius + 4.5f + bob, 0f);
                break;
            }
            case "target":
                foreach (var child in n.Root.GetChildren())
                    if (child is MeshInstance3D mesh) mesh.Scale = Vector3.One * ring;
                n.Spin!.Scale = Vector3.One * ring;
                n.Spin.Rotation = new Vector3(0f, phase * 0.6f, 0f);
                break;
            default:
            {
                foreach (var child in n.Root.GetChildren())
                    if (child is MeshInstance3D mesh) mesh.Scale = Vector3.One * ring;
                var f = (phase / 1.6f) % 1f; // 0 → 1 every 1.6 s
                n.Spin!.Scale = Vector3.One * (ring * (1f + f * (m.Kind == "ship" ? 0.6f : 1.2f)));
                if (n.PulseMaterial != null) n.PulseMaterial.AlbedoColor = n.PulseMaterial.AlbedoColor with { A = 0.85f * (1f - f) };
                break;
            }
        }
        // Labels under the body (its own name sits over it), stacked by slot.
        n.Label.Position = new Vector3(0f, -(radius + 2.5f) - n.Slot * 4.2f, 0f);
    }

    private static MeshInstance3D Ring(Color colour, float energy, float inner, float outer) => new()
    {
        Mesh = new TorusMesh { InnerRadius = inner, OuterRadius = outer, Rings = 64, RingSegments = 4 },
        MaterialOverride = MarkerMaterial(colour, energy),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>An unshaded glowing material with alpha (its own per marker: the pulse fades it).</summary>
    private static StandardMaterial3D MarkerMaterial(Color colour, float energy) => new()
    {
        AlbedoColor = colour,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        EmissionEnabled = true,
        Emission = colour,
        EmissionEnergyMultiplier = energy,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
