using System;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Survey map: the whole surface in the same fog colours as the 3D view, your bases and rovers,
/// and a frame showing what the camera sees. Click or drag on it to move the camera there.
/// </summary>
public partial class Minimap : Control
{
    private Simulation _sim = null!;
    private PlanetView _view = null!;
    private int _localPlayer;
    private ImageTexture? _texture;
    private Image? _image;
    private Vector2[] _frame = Array.Empty<Vector2>();

    /// <summary>Called with a world point (y = 0) when the player clicks or drags on the map.</summary>
    public Action<Vector3>? OnJump { get; set; }

    public void Setup(Simulation sim, PlanetView view, int localPlayer)
    {
        _sim = sim;
        _view = view;
        _localPlayer = localPlayer;
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(254, 140);
        ClipContents = true; // the camera frame never draws outside the map
    }

    /// <summary>World size of the map (x across, z down).</summary>
    private Vector2 WorldSize()
    {
        var g = _sim.Surface!.Map.Grid;
        return new Vector2((g.Width + 0.5f) * PlanetView.HexWidth, ((g.Height - 1) * 0.866f + 1.155f) * PlanetView.HexWidth);
    }

    private Vector2 ToMap(Vector3 world)
    {
        var w = WorldSize();
        return new Vector2(world.X / w.X * Size.X, world.Z / w.Y * Size.Y);
    }

    private Vector3 ToWorld(Vector2 local)
    {
        var w = WorldSize();
        return new Vector3(local.X / Size.X * w.X, 0, local.Y / Size.Y * w.Y);
    }

    /// <summary>Per frame: rebuild the terrain image when the fog changed, and update the camera frame.</summary>
    public void Refresh(Camera3D camera)
    {
        if (_texture == null || _view.FogChanged)
        {
            _view.FogChanged = false;
            RebuildImage();
        }

        // Where the four screen corners hit the ground.
        var vp = camera.GetViewport().GetVisibleRect().Size;
        var corners = new[] { new Vector2(0, 0), new Vector2(vp.X, 0), new Vector2(vp.X, vp.Y - 236), new Vector2(0, vp.Y - 236) };
        var frame = new Vector2[4];
        // Close up the camera tilts toward the horizon, so the top corners' rays hit the ground very far away or not
        // at all: cap the frame at a few times the camera's height (at least 250 units) from the point under it.
        var eye = camera.GlobalPosition;
        var reach = Mathf.Max(250f, eye.Y * 6f);
        for (var i = 0; i < 4; i++)
        {
            var origin = camera.ProjectRayOrigin(corners[i]);
            var dir = camera.ProjectRayNormal(corners[i]);
            var flat = new Vector3(dir.X, 0, dir.Z);
            if (flat.LengthSquared() < 1e-8f) flat = -camera.GlobalBasis.Z with { Y = 0 };
            flat = flat.Normalized();
            var hit = dir.Y < -1e-4f ? origin + dir * (-origin.Y / dir.Y) : origin + flat * reach;
            var away = new Vector3(hit.X - eye.X, 0, hit.Z - eye.Z);
            if (away.Length() > reach) hit = new Vector3(eye.X, 0, eye.Z) + flat * reach;
            frame[i] = ToMap(hit);
        }
        _frame = frame;
        QueueRedraw();
    }

    private void RebuildImage()
    {
        var g = _sim.Surface!.Map.Grid;
        // 4×3 pixels per hex, odd rows shifted by half a hex: close enough to the real layout at this size.
        _image ??= Image.CreateEmpty(g.Width * 4 + 2, g.Height * 3, false, Image.Format.Rgba8);
        _image.Fill(new Color(0.01f, 0.012f, 0.025f));
        for (var t = 0; t < g.Count; t++)
        {
            var colour = _view.TileColour(t);
            if (_view.FogAt(t) == PlanetView.Fog.Unexplored) colour = new Color(0.04f, 0.047f, 0.07f);
            var x0 = g.Column(t) * 4 + (g.Row(t) & 1) * 2;
            var y0 = g.Row(t) * 3;
            _image.FillRect(new Rect2I(x0, y0, 4, 3), colour);
        }
        if (_texture == null) _texture = ImageTexture.CreateFromImage(_image);
        else _texture.Update(_image);
    }

    public override void _Draw()
    {
        if (_texture == null) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.004f, 0.008f, 0.02f));
        DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);

        var surface = _sim.Surface!;
        foreach (var b in surface.Bases(_localPlayer))
        {
            var p = ToMap(_view.TileCentre(b.Tile));
            DrawRect(new Rect2(p - new Vector2(5, 5), new Vector2(10, 10)), PlanetDriver.PlayerColors[0]);
            DrawRect(new Rect2(p - new Vector2(5, 5), new Vector2(10, 10)), Colors.White, false, 1.5f);
        }
        foreach (var u in surface.Units)
            if (u.Owner == _localPlayer) DrawCircle(ToMap(_view.TileCentre(u.Tile)), 2.5f, Colors.White);

        if (_frame.Length == 4)
        {
            var accent = new Color(0.96f, 0.65f, 0.14f);
            for (var i = 0; i < 4; i++) DrawLine(_frame[i], _frame[(i + 1) % 4], accent, 1.5f);
        }
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.15f, 0.19f, 0.29f), false, 1f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        var jump = @event switch
        {
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b => (Vector2?)b.Position,
            InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Left) != 0 => m.Position,
            _ => null,
        };
        if (jump == null) return;
        OnJump?.Invoke(ToWorld(jump.Value));
        AcceptEvent();
    }
}
