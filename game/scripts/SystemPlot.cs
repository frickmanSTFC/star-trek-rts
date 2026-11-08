using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The helm's star-system plot (OrbitView's System display): a top-down chart of the system's bodies (SystemSource: the
/// sim's StarSystemState, else the duel map), laid flat in the XZ plane at MapScale world units per map unit (map +y =
/// world -Z, away from the camera). A glowing star with a point light at the centre, one faint orbit ring per orbital
/// radius (the flagship's body in peach), each body a sphere coloured by class and sized by size, moons at their own map
/// positions beside their parent, asteroid belts as a dotted ring of rocks, billboard labels.
/// System fog: a body only Detected is a dim grey sphere labelled "UNKNOWN BODY · SIZE ~N" (no class colour); Charted
/// shows its class colour and name; Live adds a small satellite glyph and a "LIVE" tag.
/// Ships: a small chevron each (ours in the owner colour, neutral traffic grey-white, rivals in their colour), parked
/// round the body they orbit, or moving along a dashed course line from FromBody to ToBody (our ships' labels carry
/// the ETA). Render-only. The bodies and rings are rebuilt only when SystemSource.PlotStamp changes, the course lines
/// (one line mesh) only when CourseStamp changes; per frame only the few ship nodes move.
/// </summary>
public partial class SystemPlot : Node3D
{
    /// <summary>World units per map unit: the mvp_duel bodies (out to ~540) fit in a radius of ~160.</summary>
    public const float MapScale = 0.3f;

    public static readonly Color Cyan = new(0.373f, 0.949f, 0.902f); // #5ff2e6
    public static readonly Color Peach = new(0.949f, 0.651f, 0.353f); // #f2a65a
    public static readonly Color Unknown = new(0.42f, 0.43f, 0.46f);
    public static readonly Color Neutral = new(0.86f, 0.88f, 0.92f);
    private static readonly Color LiveColour = new(0.5f, 1f, 0.9f);

    private readonly List<SystemBody> _bodies = new();
    private readonly List<SystemShipMark> _shipMarks = new();
    private readonly Dictionary<string, (Node3D Node, float Radius)> _nodes = new();
    private readonly Dictionary<int, (Node3D Node, Label3D? Label, string Text)> _ships = new();
    private Node3D? _static;
    private Node3D? _shipLayer;
    private MeshInstance3D? _courses;
    private MeshInstance3D? _highlight;
    private string? _highlightBody;
    private int? _highlightShip;
    private long _plotStamp = long.MinValue, _courseStamp = long.MinValue;
    private static ArrayMesh? _chevron;

    public IReadOnlyList<SystemBody> Bodies => _bodies;
    public IReadOnlyList<SystemShipMark> Ships => _shipMarks;

    /// <summary>A map's body by name or id (any case), or null.</summary>
    public static BodyDef? FindBody(MapDef? map, string? nameOrId)
    {
        if (map == null || string.IsNullOrEmpty(nameOrId)) return null;
        foreach (var b in map.Bodies)
            if (string.Equals(b.Name, nameOrId, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Id, nameOrId, StringComparison.OrdinalIgnoreCase))
                return b;
        return null;
    }

    /// <summary>Colour of a body by planet class id (render-only palette).</summary>
    public static Color ClassColour(string classId) => classId switch
    {
        "class_m" => new Color(0.247f, 0.498f, 0.816f),
        "class_l" => new Color(0.851f, 0.722f, 0.471f),
        "class_d" => new Color(0.769f, 0.604f, 0.478f),
        "class_h" => new Color(0.878f, 0.69f, 0.44f),
        "class_k" => new Color(0.72f, 0.39f, 0.24f),
        "class_n" => new Color(0.18f, 0.44f, 0.69f),
        "class_j" => new Color(0.784f, 0.635f, 0.42f),
        "class_t" => new Color(0.73f, 0.56f, 0.41f),
        "class_y" => new Color(0.75f, 0.29f, 0.19f),
        "ice_moon" => new Color(0.737f, 0.847f, 0.937f),
        "asteroid" => new Color(0.6f, 0.57f, 0.52f),
        "asteroid_rare" => new Color(0.72f, 0.63f, 0.85f),
        _ => new Color(0.67f, 0.67f, 0.67f),
    };

    public static bool IsRocks(string classId) => classId is "asteroid" or "asteroid_rare";

    /// <summary>Sphere radius of a body in the plot (world units) by its map size.</summary>
    public static float BodyRadius(int size) => 1.5f + size * 1.25f;

    private static Vector3 PlotPosition(BodyDef b) => new(b.Position.X.ToFloat() * MapScale, 0f, -b.Position.Y.ToFloat() * MapScale);

    private static Vector3 PlotPosition(Vector2 mapPos) => new(mapPos.X * MapScale, 0f, -mapPos.Y * MapScale);

    /// <summary>Every frame while the plot shows: rebuild what changed, then move the ships.</summary>
    /// <param name="ownerColour">A player's colour (PlanetDriver.ColorOf).</param>
    public void Refresh(SystemSource source, float alpha, Func<int, Color> ownerColour)
    {
        var stamp = source.PlotStamp();
        if (stamp != _plotStamp)
        {
            _plotStamp = stamp;
            Build(source);
            _courseStamp = long.MinValue;
        }
        var courses = source.CourseStamp();
        if (courses != _courseStamp)
        {
            _courseStamp = courses;
            BuildCourses(source, ownerColour);
        }
        MoveShips(source, alpha, ownerColour);
        PlaceHighlight();
    }

    /// <summary>Force a rebuild on the next Refresh (e.g. after the game's data changed).</summary>
    public void Invalidate()
    {
        _plotStamp = long.MinValue;
        _courseStamp = long.MinValue;
    }

    // ---------------------------------------------------------------- bodies

    private void Build(SystemSource source)
    {
        _bodies.Clear();
        _nodes.Clear();
        if (_static != null)
        {
            RemoveChild(_static);
            _static.QueueFree();
        }
        _static = new Node3D { Name = "Bodies" };
        AddChild(_static);
        if (_highlight == null)
        {
            _highlight = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 1f, OuterRadius = 1.08f, Rings = 64, RingSegments = 4 }, MaterialOverride = Glow(Cyan, 2f), Visible = false };
            AddChild(_highlight);
        }
        if (_shipLayer == null)
        {
            _shipLayer = new Node3D { Name = "Ships" };
            AddChild(_shipLayer);
        }

        var star = source.Star;
        if (star == null) return;
        var bodies = source.Bodies;
        var orbitBody = source.FlagshipInFlight ? null : source.OrbitBodyId;
        var colonyBody = source.ColonyBodyId;

        // The star: an emissive ball with two additive halos (the orbit glow picks it up), and the light for the plot.
        var starColour = new Color(1f, 0.78f, 0.4f);
        var starRadius = Mathf.Max(6f, star.Radius.ToFloat() * MapScale);
        _static.AddChild(new MeshInstance3D { Mesh = Sphere(starRadius, 32), MaterialOverride = Glow(starColour, 4f) });
        foreach (var (k, a) in new[] { (1.6f, 0.22f), (2.6f, 0.08f) })
            _static.AddChild(new MeshInstance3D
            {
                Mesh = Sphere(starRadius * k, 24),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = starColour with { A = a },
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            });
        _static.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.92f, 0.8f), LightEnergy = 3f, OmniRange = 600f, OmniAttenuation = 0.4f, ShadowEnabled = false });
        _static.AddChild(Label($"{star.Name.ToUpperInvariant()} · {star.Class}-CLASS STAR", new Vector3(0, starRadius + 3f, 0), starColour, 26));

        // One ring per orbital radius (pairs on the same radius share one); belts get rocks instead.
        var rings = new List<(float Radius, bool Current)>();
        var belts = new List<(float Radius, bool Charted)>();
        foreach (var b in bodies)
        {
            if (b.Parent != null || !source.Known(b.Id)) continue;
            var r = PlotPosition(b).Length();
            if (b.ClassId == "asteroid")
            {
                var i0 = belts.FindIndex(x => Mathf.Abs(x.Radius - r) < 5f);
                if (i0 < 0) belts.Add((r, source.Identified(b.Id)));
                else if (source.Identified(b.Id)) belts[i0] = (belts[i0].Radius, true);
                continue;
            }
            if (IsRocks(b.ClassId)) continue;
            var i = rings.FindIndex(x => Mathf.Abs(x.Radius - r) < 1f);
            var isCurrent = b.Id == orbitBody;
            if (i < 0) rings.Add((r, isCurrent));
            else if (isCurrent) rings[i] = (rings[i].Radius, true);
        }
        foreach (var (r, cur) in rings)
            _static.AddChild(OrbitLines.Instance(OrbitLines.Circle(r, 160), cur ? Peach with { A = 0.85f } : Cyan with { A = 0.28f }));
        var rng = new RandomNumberGenerator { Seed = 4711 };
        foreach (var (r, charted) in belts)
            _static.AddChild(Rocks(rng, 200, p => new Vector3(Mathf.Sin(p) * r, 0f, Mathf.Cos(p) * r), 3f, charted ? ClassColour("asteroid") : Unknown));

        foreach (var b in bodies)
        {
            var knowledge = source.Knowledge(b.Id);
            if (knowledge == BodyKnowledge.Unknown) continue;
            var charted = knowledge >= BodyKnowledge.Scanned; // name and class known (Scanned = no sweep yet)
            var live = source.HasLive(b.Id);
            var pos = PlotPosition(b);
            var isCurrent = b.Id == orbitBody;
            var isColony = b.Id == colonyBody;
            var colour = charted ? ClassColour(b.ClassId) : Unknown;
            var radius = BodyRadius(b.Size);
            var node = new Node3D { Name = b.Id, Position = pos };
            _static.AddChild(node);
            if (IsRocks(b.ClassId))
            {
                radius = 4f + b.Size * 2f;
                node.AddChild(Rocks(rng, 26, _ => Vector3.Zero, radius * 0.8f, colour));
            }
            else
                node.AddChild(new MeshInstance3D
                {
                    Mesh = Sphere(radius, 24),
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = colour,
                        Roughness = 0.9f,
                        EmissionEnabled = true,
                        Emission = colour,
                        EmissionEnergyMultiplier = charted ? 0.18f : 0.06f,
                    },
                });
            if (isCurrent)
                node.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = radius + 2.4f, OuterRadius = radius + 3.1f, Rings = 48, RingSegments = 6 }, MaterialOverride = Glow(Peach, 1.6f) });
            if (live) node.AddChild(SatelliteGlyph(radius));

            var moons = 0;
            foreach (var m in bodies)
                if (m.Parent == b.Id && source.Known(m.Id)) moons++;
            var name = charted ? b.Name.ToUpperInvariant() : $"UNKNOWN BODY · SIZE ~{b.Size}";
            var tags = new List<string> { name };
            if (isCurrent) tags.Add("IN ORBIT");
            else if (isColony) tags.Add("COLONY");
            else if (charted && moons > 0) tags.Add($"{moons} MOON{(moons > 1 ? "S" : "")}");
            if (knowledge == BodyKnowledge.Scanned) tags.Add(source.State?.SweepingShip(source.Player, b.Id) != null ? "SWEEPING" : "UNCHARTED");
            if (live) tags.Add("LIVE");
            var text = string.Join(" · ", tags);
            var textColour = isCurrent ? Peach : !charted ? new Color(0.62f, 0.64f, 0.68f) : IsRocks(b.ClassId) ? Cyan : new Color(0.95f, 0.9f, 0.85f);
            if (b.Parent == null) node.AddChild(Label(text, new Vector3(0, radius + 3f, 0), textColour, 24));
            else
            {
                var moonLabel = Label(text, Vector3.Zero, textColour with { A = 0.8f }, 18);
                moonLabel.HorizontalAlignment = HorizontalAlignment.Left;
                moonLabel.VerticalAlignment = VerticalAlignment.Center;
                moonLabel.Offset = new Vector2(radius / moonLabel.PixelSize + 10f, 0f);
                node.AddChild(moonLabel);
            }

            _nodes[b.Id] = (node, radius);
            _bodies.Add(new SystemBody(b.Id, charted ? b.Name : "Unknown body", charted ? source.ClassName(b.ClassId) : "", b.Size,
                SystemSource.PositionOf(b), b.Parent, isCurrent, knowledge, live, isColony, b.ClassId));
        }
    }

    /// <summary>A tiny survey satellite beside a body with live data: a box, two panels and a lit dish, plus a faint orbit ring.</summary>
    private static Node3D SatelliteGlyph(float bodyRadius)
    {
        var glyph = new Node3D { Name = "Satellite", Position = new Vector3(bodyRadius + 2.6f, 2.2f, -bodyRadius * 0.4f) };
        var body = Glow(new Color(0.85f, 0.88f, 0.95f), 0.6f);
        glyph.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.9f, 1.1f) }, MaterialOverride = body });
        foreach (var x in new[] { -1.5f, 1.5f })
            glyph.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.8f, 0.08f, 0.9f) }, Position = new Vector3(x, 0, 0), MaterialOverride = Glow(new Color(0.25f, 0.4f, 0.9f), 1f) });
        glyph.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f, RadialSegments = 8, Rings = 4 }, Position = new Vector3(0, 0.7f, 0), MaterialOverride = Glow(LiveColour, 4f) });
        var ring = OrbitLines.Instance(OrbitLines.Circle(bodyRadius + 2.6f, 48, dashed: true), LiveColour with { A = 0.45f });
        ring.Position = new Vector3(-(bodyRadius + 2.6f), -2.2f, bodyRadius * 0.4f); // centred on the body again
        glyph.AddChild(ring);
        return glyph;
    }

    // ---------------------------------------------------------------- ships

    /// <summary>The dashed course lines of every ship in flight, as one line mesh with vertex colours.</summary>
    private void BuildCourses(SystemSource source, Func<int, Color> ownerColour)
    {
        if (_courses != null)
        {
            _courses.QueueFree();
            _courses = null;
        }
        var points = new List<Vector3>();
        var colours = new List<Color>();
        foreach (var s in source.Ships)
        {
            if (!SystemSource.InFlight(s) || source.Body(s.FromBody) is not { } from || source.Body(s.ToBody) is not { } to) continue;
            var a = PlotPosition(from) + Vector3.Up * 0.6f;
            var b = PlotPosition(to) + Vector3.Up * 0.6f;
            var length = a.DistanceTo(b);
            if (length < 0.5f) continue;
            var colour = ShipColour(s, source.Player, ownerColour) with { A = s.Owner == source.Player ? 0.85f : 0.45f };
            var dash = 2.4f;
            var n = Mathf.Max(1, Mathf.FloorToInt(length / dash));
            for (var i = 0; i < n; i += 2)
            {
                points.Add(a.Lerp(b, i / (float)n));
                points.Add(a.Lerp(b, Mathf.Min(1f, (i + 1) / (float)n)));
                colours.Add(colour);
                colours.Add(colour);
            }
        }
        if (points.Count == 0 || _shipLayer == null) return;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        _courses = new MeshInstance3D
        {
            Name = "Courses",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        _shipLayer.AddChild(_courses);
    }

    private static Color ShipColour(SystemShip s, int localPlayer, Func<int, Color> ownerColour) =>
        s.Owner < 0 ? Neutral : ownerColour(s.Owner);

    /// <summary>Puts every ship where it is now: in flight on its course, else parked round the body it orbits.</summary>
    private void MoveShips(SystemSource source, float alpha, Func<int, Color> ownerColour)
    {
        _shipMarks.Clear();
        if (_shipLayer == null) return;
        var alive = new HashSet<int>();
        var parked = new Dictionary<string, int>();
        foreach (var s in source.Ships)
        {
            var ours = s.Owner == source.Player;
            var flying = SystemSource.InFlight(s);
            alive.Add(s.Id);
            if (!_ships.TryGetValue(s.Id, out var entry))
            {
                var colour = ShipColour(s, source.Player, ownerColour);
                var node = new Node3D { Name = $"Ship{s.Id}" };
                node.AddChild(new MeshInstance3D
                {
                    Mesh = Chevron(),
                    Scale = Vector3.One * (ours ? 1.25f : 0.9f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = colour,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                        EmissionEnabled = true,
                        Emission = colour,
                        EmissionEnergyMultiplier = ours ? 1.6f : 0.8f,
                    },
                });
                Label3D? label = null;
                if (ours)
                {
                    label = Label("", new Vector3(0, 2.5f, 0), colour, 18);
                    node.AddChild(label);
                }
                _shipLayer.AddChild(node);
                entry = (node, label, "");
                _ships[s.Id] = entry;
            }

            Vector3 at, facing;
            if (!flying && source.Body(s.AtBody) is { } body && _nodes.TryGetValue(body.Id, out var bn))
            {
                // Parked: round the body, a little apart from each other, nose along the orbit.
                var k = parked.GetValueOrDefault(body.Id);
                parked[body.Id] = k + 1;
                var angle = 2.3f + k * 0.75f;
                var ring = bn.Radius + 4.5f;
                var offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * ring;
                at = bn.Node.Position + offset + Vector3.Up * 1.2f;
                facing = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle));
            }
            else
            {
                at = PlotPosition(source.ShipPosition(s, alpha)) + Vector3.Up * 1.2f;
                var from = source.Body(s.FromBody);
                var to = source.Body(s.ToBody);
                facing = from != null && to != null ? PlotPosition(to) - PlotPosition(from) : Vector3.Forward;
            }
            entry.Node.Position = at;
            if (facing.LengthSquared() > 1e-4f)
            {
                var f = facing.Normalized();
                entry.Node.Basis = Basis.LookingAt(f, Vector3.Up);
            }
            if (entry.Label != null)
            {
                var text = s.Name.ToUpperInvariant();
                if (flying) text += $" · ETA {source.SecondsToArrival(s)} S";
                if (text != entry.Text)
                {
                    entry.Label.Text = text;
                    _ships[s.Id] = (entry.Node, entry.Label, text);
                }
                // keep the label upright over the chevron whatever way it faces
                entry.Label.Position = entry.Node.Basis.Inverse() * new Vector3(0f, 2.5f, 0f);
            }
            _shipMarks.Add(new SystemShipMark(s.Id, s.Name, s.Owner, ours, flying));
        }
        foreach (var id in new List<int>(_ships.Keys))
        {
            if (alive.Contains(id)) continue;
            _ships[id].Node.QueueFree();
            _ships.Remove(id);
        }
    }

    /// <summary>A flat arrowhead pointing along -Z (Basis.LookingAt's forward), notched at the back.</summary>
    private static ArrayMesh Chevron()
    {
        if (_chevron != null) return _chevron;
        Vector3 tip = new(0f, 0f, -2.6f), left = new(-1.7f, 0f, 1.6f), notch = new(0f, 0f, 0.5f), right = new(1.7f, 0f, 1.6f);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { tip, left, notch, tip, notch, right };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up };
        _chevron = new ArrayMesh();
        _chevron.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return _chevron;
    }

    // ---------------------------------------------------------------- selection, picking

    /// <summary>Ring one body in cyan (the helm's selection); null clears it.</summary>
    public void Highlight(string? id)
    {
        _highlightBody = id;
        _highlightShip = null;
        PlaceHighlight();
    }

    /// <summary>Ring one ship in cyan; null clears it.</summary>
    public void HighlightShip(int? id)
    {
        _highlightShip = id;
        _highlightBody = null;
        PlaceHighlight();
    }

    private void PlaceHighlight()
    {
        if (_highlight == null) return;
        if (_highlightShip is { } sid && _ships.TryGetValue(sid, out var ship))
        {
            _highlight.Visible = true;
            _highlight.Position = ship.Node.Position;
            _highlight.Scale = Vector3.One * 4.2f;
            return;
        }
        if (_highlightBody == null || !_nodes.TryGetValue(_highlightBody, out var n))
        {
            _highlight.Visible = false;
            return;
        }
        _highlight.Visible = true;
        _highlight.Position = n.Node.Position;
        _highlight.Scale = Vector3.One * (n.Radius + 5f); // the torus is built at radius 1
    }

    /// <summary>Where a body is (global) and its drawn radius; false for an unknown id or name.</summary>
    public bool TryLocate(string nameOrId, out Vector3 global, out float radius)
    {
        foreach (var b in _bodies)
            if (string.Equals(b.Id, nameOrId, StringComparison.OrdinalIgnoreCase) || (b.Charted && string.Equals(b.Name, nameOrId, StringComparison.OrdinalIgnoreCase)))
            {
                var n = _nodes[b.Id];
                global = n.Node.GlobalPosition;
                radius = n.Radius;
                return true;
            }
        global = Vector3.Zero;
        radius = 0f;
        return false;
    }

    /// <summary>Where a ship's chevron is (global); false when it isn't on the plot.</summary>
    public bool TryLocateShip(int id, out Vector3 global)
    {
        if (_ships.TryGetValue(id, out var s) && s.Node.IsInsideTree())
        {
            global = s.Node.GlobalPosition;
            return true;
        }
        global = Vector3.Zero;
        return false;
    }

    // ---------------------------------------------------------------- helpers

    private static SphereMesh Sphere(float radius, int segments) => new() { Radius = radius, Height = radius * 2f, RadialSegments = segments, Rings = segments / 2 };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
    };

    private static Label3D Label(string text, Vector3 at, Color colour, int fontSize) => new()
    {
        Text = text,
        Position = at,
        FontSize = fontSize,
        PixelSize = 0.3f,
        Modulate = colour,
        OutlineSize = 8,
        OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        VerticalAlignment = VerticalAlignment.Bottom,
        RenderPriority = 21,
        OutlineRenderPriority = 20,
    };

    /// <summary>Low-poly rocks: count of them at place(angle), jittered by spread (flatter vertically).</summary>
    private static MultiMeshInstance3D Rocks(RandomNumberGenerator rng, int count, Func<float, Vector3> place, float spread, Color colour)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new SphereMesh { Radius = 0.7f, Height = 1.2f, RadialSegments = 5, Rings = 3 },
            InstanceCount = count,
        };
        for (var i = 0; i < count; i++)
        {
            var angle = (i + rng.RandfRange(-0.3f, 0.3f)) / count * Mathf.Tau;
            var jitter = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-1f, 1f)) * spread;
            var axis = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f));
            if (axis.LengthSquared() < 0.01f) axis = Vector3.Up;
            var basis = new Basis(axis.Normalized(), rng.RandfRange(0f, Mathf.Tau)).Scaled(Vector3.One * rng.RandfRange(0.5f, 1.5f));
            mm.SetInstanceTransform(i, new Transform3D(basis, place(angle) + jitter));
            mm.SetInstanceColor(i, colour * rng.RandfRange(0.75f, 1.15f));
        }
        return new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
