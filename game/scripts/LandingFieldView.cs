using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The landing fields, drawn (render-only; the sim side is sim/Surface/LandingField.cs): for every pad of every colony
/// core's field (<see cref="SurfaceStructure.Field"/>) painted markings that follow the ground (a yellow ring, a white
/// centre circle, four approach bars and the pad's number), four flush pad lights, and a fuel bowser at its edge with a
/// hose reel and a status lamp. Pad lights: steady and low when the pad is free, flashing while a drop ship is hopping
/// in, steady and brighter while one stands on it. Bowser lamp: dim green idle, pulsing amber while its ship refuels,
/// flashing red while the ship waits for fuel. <see cref="DropShipView"/> asks it where a ship stands on a pad
/// (<see cref="Frame"/>) and where its hose starts (<see cref="HosePort"/>). Our own fields always show; another
/// player's only once its core is known (fog). Everything is in PlanetView's space (this node sits at its origin).
/// </summary>
public partial class LandingFieldView : Node3D
{
    private const float RingOuter = 9.9f, RingInner = 9.45f; // the painted ring (the lanes keep vehicles 10.5 out)
    private const float CentreOuter = 2.6f, CentreInner = 2.25f;
    private const float Lift = 0.28f;                         // paint above the ground (the terrain has small bumps)
    private const int RingSegments = 40;
    private static readonly Vector3 BowserAt = new(-8.7f, 0f, 2.6f); // pad frame: left of a ship whose nose is at -Z
    private static readonly Vector3 ReelAt = new(1.05f, 0.95f, 0f);  // bowser frame: its hose reel (the end toward the pad)

    private static readonly Color Paint = new(0.93f, 0.78f, 0.22f), White = new(0.88f, 0.88f, 0.84f);
    private static readonly Color Free = new(0.35f, 0.6f, 1f), Inbound = new(1f, 0.82f, 0.3f), Taken = new(1f, 0.6f, 0.2f);
    private static readonly Color Idle = new(0.3f, 0.9f, 0.45f), Fuelling = new(1f, 0.66f, 0.15f), NoFuel = new(1f, 0.2f, 0.15f);

    private readonly PlanetView _view = null!;
    private readonly Simulation _sim = null!;
    private readonly PlanetSurface _surface = null!;
    private readonly Dictionary<int, Pad> _pads = new(); // by tile
    private readonly List<int> _gone = new();
    private long _signature = long.MinValue;
    private float _clock;

    private static StandardMaterial3D? _paint, _white, _bodyWhite, _band, _dark;

    private sealed class Pad
    {
        public required int Tile, CoreId, Index;
        public required Node3D Root;
        public required Node3D Bowser;
        public required StandardMaterial3D Lamps, Status;
        public bool Seen; // still wanted this rebuild
    }

    public LandingFieldView(PlanetView view, Simulation sim)
    {
        _view = view;
        _sim = sim;
        _surface = sim.Surface!;
    }

    // Parameterless constructor for Godot's script machinery; such an instance draws nothing.
    public LandingFieldView() { }

    // ---------------------------------------------------------------- for DropShipView

    /// <summary>Where things stand on a pad: the origin on the ground at the tile's centre, -Z pointing away from the
    /// pad's colony core (a parked ship's nose; its ramp faces the base), +Y up. Works for any pad tile, drawn or not.</summary>
    public Transform3D Frame(int padTile)
    {
        var centre = _view.TileCentre(padTile);
        var away = Vector3.Forward;
        if (_surface.FindStructure(_surface.PadCoreAt(padTile)) is { } core)
        {
            var d = (centre - _view.TileCentre(core.Tile)) with { Y = 0 };
            if (d.LengthSquared() > 0.01f) away = d.Normalized();
        }
        return new Transform3D(Basis.LookingAt(away, Vector3.Up), centre);
    }

    /// <summary>The pad's bowser hose reel (where a refuelling hose starts), on the ground beside the pad.</summary>
    public Vector3 HosePort(int padTile)
    {
        var bowser = BowserFrame(Frame(padTile));
        return bowser * ReelAt;
    }

    /// <summary>The bowsers that show, for the people's obstacle boxes (PlanetView.People.cs).</summary>
    public void Furniture(List<Node3D> into)
    {
        foreach (var pad in _pads.Values)
            if (pad.Root.Visible) into.Add(pad.Bowser);
    }

    private Transform3D BowserFrame(Transform3D pad)
    {
        var at = pad * BowserAt;
        at.Y = _view.GroundAt(at);
        // Its reel end (+X of the bowser) points at the pad's centre.
        var toPad = (pad.Origin - at) with { Y = 0 };
        var x = toPad.LengthSquared() > 0.01f ? toPad.Normalized() : Vector3.Right;
        var z = x.Cross(Vector3.Up).Normalized();
        return new Transform3D(new Basis(x, Vector3.Up, z), at);
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Builds the pads of fields that are new (or newly known), frees those whose core has gone, and sets every
    /// pad's lights by the drop ship that has it.</summary>
    public void Update(float delta)
    {
        if (_surface == null) return;
        _clock = (_clock + delta) % 600f;
        var signature = 17L;
        foreach (var s in _surface.Structures)
            if (s.Field.Count > 0) signature = signature * 31 + (_view.Shows(s) ? s.Id * 8 + s.Field.Count : -s.Id);
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild();
        }

        foreach (var pad in _pads.Values)
        {
            var ship = _surface.ShipOnPad(pad.Tile);
            Color lamps;
            float energy;
            if (ship == null) (lamps, energy) = (Free, 0.9f);
            else if (ship.State == DropShipState.Hopping) (lamps, energy) = (Inbound, Mathf.PosMod(_clock, 0.5f) < 0.25f ? 4f : 0.3f);
            else (lamps, energy) = (Taken, 1.8f);
            pad.Lamps.Emission = lamps;
            pad.Lamps.EmissionEnergyMultiplier = energy;

            Color status;
            if (ship is { State: DropShipState.Refuelling }) (status, energy) = (Fuelling, 1.2f + 2.2f * (0.5f + 0.5f * Mathf.Sin(_clock * 5f)));
            else if (ship is { State: DropShipState.WaitingForFuel }) (status, energy) = (NoFuel, Mathf.PosMod(_clock, 0.8f) < 0.4f ? 4f : 0.2f);
            else (status, energy) = (Idle, 0.8f);
            pad.Status.Emission = status;
            pad.Status.EmissionEnergyMultiplier = energy;
        }
    }

    private void Rebuild()
    {
        foreach (var pad in _pads.Values) pad.Seen = false;
        foreach (var s in _surface.Structures)
        {
            if (s.Field.Count == 0 || !_view.Shows(s)) continue;
            for (var i = 0; i < s.Field.Count; i++)
            {
                var tile = s.Field[i];
                if (_pads.TryGetValue(tile, out var pad) && pad.CoreId == s.Id)
                {
                    pad.Seen = true;
                    continue;
                }
                if (pad != null) pad.Root.QueueFree(); // the tile changed hands (its old core went)
                _pads[tile] = Build(tile, s.Id, i);
            }
        }
        _gone.Clear();
        foreach (var (tile, pad) in _pads)
            if (!pad.Seen) _gone.Add(tile);
        foreach (var tile in _gone)
        {
            _pads[tile].Root.QueueFree();
            _pads.Remove(tile);
        }
    }

    // ---------------------------------------------------------------- building a pad

    private Pad Build(int tile, int coreId, int index)
    {
        _paint ??= Painted(Paint);
        _white ??= Painted(White);
        var frame = Frame(tile);
        var root = new Node3D { Name = $"Pad{tile}" };
        AddChild(root);

        // Paint: one mesh per colour, in this node's own space (= PlanetView's), every vertex set on the ground.
        var yellow = new SurfaceTool();
        yellow.Begin(Mesh.PrimitiveType.Triangles);
        Ring(yellow, frame.Origin, RingInner, RingOuter);
        for (var k = 0; k < 4; k++) // approach bars toward the four sides, the front one (the ship's nose) doubled
        {
            var a = Mathf.Pi / 2f * k;
            var dir = frame.Basis * new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a));
            var side = dir.Cross(Vector3.Up).Normalized();
            if (k == 0)
            {
                Bar(yellow, frame.Origin + side * 0.55f, dir, 5.2f, 8.6f, 0.34f);
                Bar(yellow, frame.Origin - side * 0.55f, dir, 5.2f, 8.6f, 0.34f);
            }
            else Bar(yellow, frame.Origin, dir, 6.4f, 8.6f, 0.34f);
        }
        root.AddChild(new MeshInstance3D { Name = "PadPaint", Mesh = yellow.Commit(), MaterialOverride = _paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var white = new SurfaceTool();
        white.Begin(Mesh.PrimitiveType.Triangles);
        Ring(white, frame.Origin, CentreInner, CentreOuter);
        root.AddChild(new MeshInstance3D { Name = "PadCentre", Mesh = white.Commit(), MaterialOverride = _white, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        // The pad's number, flat on the ground behind the ring's rear bar (read from the base's side).
        var numberAt = frame * new Vector3(0, 0, 11.1f);
        numberAt.Y = _view.GroundAt(numberAt) + Lift + 0.05f;
        var number = new Label3D
        {
            Name = "PadNumber",
            Text = (index + 1).ToString(),
            FontSize = 96,
            PixelSize = 0.022f,
            Modulate = Paint,
            OutlineSize = 0,
            Shaded = true,
            DoubleSided = true,
            NoDepthTest = false,
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            AlphaCut = Label3D.AlphaCutMode.Discard,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(number);
        // Lying on the ground, its top toward the pad's centre.
        number.Transform = new Transform3D(frame.Basis * new Basis(Vector3.Right, -Mathf.Pi / 2f), numberAt);

        // Four flush pad lights on the diagonals, one material so they change together.
        var lamps = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.13f, 0.15f), EmissionEnabled = true, Emission = Free, EmissionEnergyMultiplier = 0.9f, Roughness = 0.4f };
        lamps.SetMeta(VehicleLights.NoBoostMeta, true); // set every frame in Update: the night boost leaves it alone
        for (var k = 0; k < 4; k++)
        {
            var a = Mathf.Pi / 4f + Mathf.Pi / 2f * k;
            var at = frame * new Vector3(Mathf.Sin(a) * (RingOuter + 0.45f), 0, Mathf.Cos(a) * (RingOuter + 0.45f));
            at.Y = _view.GroundAt(at) + 0.07f;
            root.AddChild(new MeshInstance3D
            {
                Name = $"PadLight{k}",
                Mesh = LampMesh,
                MaterialOverride = lamps,
                Position = at,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        var status = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f), EmissionEnabled = true, Emission = Idle, EmissionEnergyMultiplier = 0.8f };
        status.SetMeta(VehicleLights.NoBoostMeta, true);
        var bowser = Bowser(status);
        root.AddChild(bowser);
        bowser.Transform = BowserFrame(frame);
        return new Pad { Tile = tile, CoreId = coreId, Index = index, Root = root, Bowser = bowser, Lamps = lamps, Status = status, Seen = true };
    }

    private static CylinderMesh? _lampMesh;
    private static CylinderMesh LampMesh => _lampMesh ??= new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.32f, Height = 0.14f, RadialSegments = 10, Rings = 1 };

    private static StandardMaterial3D Painted(Color colour)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = colour,
            Roughness = 0.95f,
            EmissionEnabled = true,
            Emission = colour,
            EmissionEnergyMultiplier = 0.12f, // just enough to read at night
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        m.SetMeta(VehicleLights.NoBoostMeta, true); // shared by every pad and kept across games: not a lamp for the night boost
        return m;
    }

    private Vector3 OnGround(Vector3 p) => p with { Y = _view.GroundAt(p) + Lift };

    /// <summary>A flat ring on the ground round a centre.</summary>
    private void Ring(SurfaceTool st, Vector3 centre, float inner, float outer)
    {
        for (var i = 0; i < RingSegments; i++)
        {
            float a0 = Mathf.Tau * i / RingSegments, a1 = Mathf.Tau * (i + 1) / RingSegments;
            Vector3 d0 = new(Mathf.Sin(a0), 0, Mathf.Cos(a0)), d1 = new(Mathf.Sin(a1), 0, Mathf.Cos(a1));
            Quad(st, OnGround(centre + d0 * outer), OnGround(centre + d1 * outer), OnGround(centre + d1 * inner), OnGround(centre + d0 * inner));
        }
    }

    /// <summary>A painted bar from <paramref name="from"/> to <paramref name="to"/> out along a direction, in short
    /// pieces so it follows the ground.</summary>
    private void Bar(SurfaceTool st, Vector3 origin, Vector3 dir, float from, float to, float width)
    {
        var side = dir.Cross(Vector3.Up).Normalized() * (width / 2f);
        const int pieces = 4;
        for (var i = 0; i < pieces; i++)
        {
            var a = origin + dir * Mathf.Lerp(from, to, (float)i / pieces);
            var b = origin + dir * Mathf.Lerp(from, to, (float)(i + 1) / pieces);
            Quad(st, OnGround(a - side), OnGround(b - side), OnGround(b + side), OnGround(a + side));
        }
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.SetNormal(Vector3.Up); st.AddVertex(a);
        st.SetNormal(Vector3.Up); st.AddVertex(b);
        st.SetNormal(Vector3.Up); st.AddVertex(c);
        st.SetNormal(Vector3.Up); st.AddVertex(a);
        st.SetNormal(Vector3.Up); st.AddVertex(c);
        st.SetNormal(Vector3.Up); st.AddVertex(d);
    }

    /// <summary>A small fuel bowser: a tank on a four-wheeled chassis, a hose reel at its +X end (toward the pad) and a
    /// status lamp on top. Origin on the ground under its middle.</summary>
    private static Node3D Bowser(StandardMaterial3D status)
    {
        _bodyWhite ??= new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.87f, 0.88f), Metallic = 0.2f, Roughness = 0.55f };
        _band ??= new StandardMaterial3D { AlbedoColor = new Color(0.93f, 0.45f, 0.12f), Roughness = 0.6f };
        _dark ??= new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.14f, 0.15f), Metallic = 0.4f, Roughness = 0.6f };
        var n = new Node3D { Name = "FuelBowser" };
        void Part(string name, Mesh mesh, Vector3 at, Material m, Vector3 turn = default) =>
            n.AddChild(new MeshInstance3D { Name = name, Mesh = mesh, Position = at, RotationDegrees = turn, MaterialOverride = m });
        Part("Chassis", new BoxMesh { Size = new Vector3(2.5f, 0.22f, 1.15f) }, new Vector3(0, 0.5f, 0), _dark);
        Part("Tank", new CylinderMesh { TopRadius = 0.58f, BottomRadius = 0.58f, Height = 1.9f, RadialSegments = 14, Rings = 1 }, new Vector3(-0.2f, 1.18f, 0), _bodyWhite, new Vector3(0, 0, 90));
        Part("TankBand", new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.32f, RadialSegments = 14, Rings = 1 }, new Vector3(-0.2f, 1.18f, 0), _band, new Vector3(0, 0, 90));
        Part("Reel", new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.3f, RadialSegments = 12, Rings = 1 }, new Vector3(ReelAt.X, ReelAt.Y, 0), _dark, new Vector3(90, 0, 0));
        Part("ReelPost", new BoxMesh { Size = new Vector3(0.14f, 0.5f, 0.5f) }, new Vector3(ReelAt.X, 0.72f, 0), _dark);
        for (var k = 0; k < 4; k++)
            Part($"Wheel{k}", new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.22f, RadialSegments = 10, Rings = 1 },
                new Vector3(k < 2 ? -0.85f : 0.85f, 0.3f, k % 2 == 0 ? -0.6f : 0.6f), _dark, new Vector3(90, 0, 0));
        Part("StatusLamp", new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 8, Rings = 4 }, new Vector3(-0.2f, 1.86f, 0), status);
        return n;
    }
}
