using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The surface camera never shows the end of the landscape (owner 2026-11-17: "When you zoom out, the round world becomes
// a simple rectangle. We need to clamp that so you don't see the end of the landscape."). Render-only.
//
// What is on screen: the ground seen through the part of the screen the HUD leaves open, between the top bar and the
// bottom panel (both run the full width and are opaque). Its four corner rays are worked out from the camera's own
// numbers (field of view, aspect, the tilt it has at that zoom, the yaw, the height of the ground it looks at) and
// followed down to the ground; the box round those points is the view's footprint. Two things follow from it:
//
// - Zoom cap. The farthest zoom is the one whose footprint still fits between the map's stop lines (PlanetView.Edge.cs)
//   for the way the camera is turned: a wide screen or a view turned along the map's short side stops sooner. Turning
//   at the limit (Q / E) glides the zoom in to the new limit as the view comes round.
// - Pan limits. The focus may only go where the whole footprint stays between the stop lines, so near an edge the camera
//   stops with that edge at the rim of the open screen: the focus sits off-centre, and what you fly to (FlyTo, the
//   minimap, a selected base) is shown as near the middle as the edge allows. The targets are clamped (the easing
//   brings the camera to rest on the limit, no jitter) and so are the eased values (nothing shows while a zoom-out or a
//   turn is still moving).
//
// Close up the camera looks toward the horizon and no clamp could hide the edge; there the rays count only as far as
// the haze lets the ground be seen (HazeNear × the haze's reference distance), which is what hid the far view before.
// Beyond that the dark apron under the haze stands in for more land (PlanetView.Edge.cs).
//
// The ground's height under the focus changes the footprint (the camera looks at the ground, so over a mountain it
// stands higher and sees farther). It is followed up at once and down only while the limits are not pushing the camera,
// so a hill at the edge can't make the camera bounce against its own limit.
//
// Limits: the footprint is wider on its far side than its near side, so the last few hexes along a side edge only show
// in the upper part of the screen, and a near corner of the map not at all until the view is turned (Q / E); FlyTo
// turns it by itself when what it flies to would be off screen (TurnToShow). The
// limits are measured against each edge's lowest ground, not the exact relief (the ring of edge tiles takes up the
// difference), and assume the HUD's two bars are up (they always are on the surface).
public partial class PlanetDriver
{
    /// <summary>World units kept between the view and the stop lines (negative lets that much past them show).</summary>
    private const float EdgeMargin = 0f;

    /// <summary>Close up (camera distance up to HazeFloor) a ray counts for this many × HazeFloor along the ground: by
    /// then MoveCamera's haze (density HazeDepth / max(60, distance)) has taken ~60% of the ground's contrast.</summary>
    private const float HazeNear = 2f;

    /// <summary>From the strategy tilt on, effectively no cap: the footprint is exact where the haze is thin.</summary>
    private const float HazeFar = 9f;
    private const float HazeFloor = 60f;
    private const float HazeExactFrom = 200f;

    /// <summary>Pixels of the open screen's rim the bars are trusted to cover.</summary>
    private const float ViewPadPx = 3f;

    /// <summary>The zoom-limit hint ("O for the view from orbit") shows this many times a run.</summary>
    private const int ZoomHintTimes = 2;
    private const double ZoomHintGap = 25.0;

    private static int _zoomHints;
    private double _zoomHintAt = -1000.0;
    private float _clampTargetBefore = -1f;

    /// <summary>The ground height the clamp works with (see the note above): up at once, down only when not held.</summary>
    private float _clampY;
    private bool _clampHeld;

    // The open screen's rim as view-space slopes (tan of the angle off the camera's axis), read once a frame.
    private float _viewSlopeX, _viewSlopeTop, _viewSlopeBottom;

    /// <summary>Offsets from the focus (world x / z) of the box round the ground on screen.</summary>
    private struct ViewBox
    {
        public float MinX, MaxX, MinZ, MaxZ;
    }

    /// <summary>The camera's direction from the point it looks at, before the yaw: the 55° strategy view from
    /// <see cref="TiltFrom"/> out, flattening toward a 20° near-ground view at <see cref="MinZoom"/>. UpdateCamera
    /// uses it too, so the clamp measures the view the camera really has.</summary>
    private static Vector3 TiltAt(float distance)
    {
        var t = Mathf.SmoothStep(MinZoom, TiltFrom, distance);
        return new Vector3(0, 0.34f, 0.94f).Lerp(new Vector3(0, 0.82f, 0.57f), t).Normalized();
    }

    private void ReadViewSlopes()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        var height = Mathf.Max(screen.Y, 1f);
        var tan = Mathf.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f); // vertical field of view (keep-height aspect)
        float top = PlanetHud.TopHeight - ViewPadPx, bottom = height - PlanetHud.BottomHeight + ViewPadPx;
        if (bottom - top < height * 0.2f)
        {
            // A window too small for the bars to leave a view: measure the whole screen.
            top = 0f;
            bottom = height;
        }
        _viewSlopeX = screen.X / height * tan;
        _viewSlopeTop = (1f - 2f * top / height) * tan;
        _viewSlopeBottom = (1f - 2f * bottom / height) * tan;
    }

    /// <summary>The box round the ground on screen for a camera at this distance and yaw looking at ground of this
    /// height. Each side of the box is measured against that edge of the map's lowest ground (how far a ray runs
    /// before it lands there).</summary>
    private ViewBox Footprint(float distance, float yaw, float lookY)
    {
        var dir = TiltAt(distance);
        float dy = dir.Y, dz = dir.Z;
        var eyeY = lookY + dy * distance;
        var eyeBack = dz * distance; // the eye stands this far behind the look point (toward +z before the yaw)
        var cap = Mathf.Lerp(HazeNear, HazeFar, Mathf.SmoothStep(HazeFloor, HazeExactFrom, distance)) * Mathf.Max(HazeFloor, distance);
        float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
        var box = new ViewBox { MinX = float.MaxValue, MaxX = float.MinValue, MinZ = float.MaxValue, MaxZ = float.MinValue };
        for (var corner = 0; corner < 4; corner++)
        {
            var sx = (corner & 1) == 0 ? -_viewSlopeX : _viewSlopeX;
            var sy = corner < 2 ? _viewSlopeTop : _viewSlopeBottom;
            // The ray through this corner: forward (0, -dy, -dz) + right (1, 0, 0) · sx + up (0, dz, -dy) · sy.
            float rx = sx, ry = -dy + dz * sy, rz = -dz - dy * sy;
            var flat = Mathf.Max(Mathf.Sqrt(rx * rx + rz * rz), 1e-5f);
            for (var side = 0; side < 4; side++)
            {
                // Down to that edge's lowest ground: the farthest this ray can land (or the haze cap, toward the horizon).
                var reach = ry < -1e-4f ? Mathf.Min(Mathf.Max(eyeY - _view.EdgeBottom(side), 0f) / -ry * flat, cap) : cap;
                Widen(ref box, side, rx / flat * reach, eyeBack + rz / flat * reach, sin, cos);
            }
        }
        return box;
    }

    /// <summary>Takes a point (before the yaw) into one side of the box: 0 west, 1 east, 2 north, 3 south.</summary>
    private static void Widen(ref ViewBox box, int side, float x, float z, float sin, float cos)
    {
        // Vector3.Rotated(Up, yaw): x' = x cos + z sin, z' = -x sin + z cos.
        var wx = x * cos + z * sin;
        var wz = -x * sin + z * cos;
        switch (side)
        {
            case 0: if (wx < box.MinX) box.MinX = wx; break;
            case 1: if (wx > box.MaxX) box.MaxX = wx; break;
            case 2: if (wz < box.MinZ) box.MinZ = wz; break;
            default: if (wz > box.MaxZ) box.MaxZ = wz; break;
        }
    }

    /// <summary>The farthest zoom whose footprint fits the map with the camera turned this way. Measured over the
    /// map's highest ground, so the limit is one number for the whole map and panning over mountains at the limit
    /// doesn't breathe.</summary>
    private float MaxDistance(float yaw)
    {
        // From the strategy tilt out the footprint grows in a straight line with the distance: two samples give it.
        const float d1 = 600f, d2 = 1600f;
        var high = _view.HighestGround;
        var a = Footprint(d1, yaw, high);
        var b = Footprint(d2, yaw, high);
        var roomX = _view.EdgeEast - _view.EdgeWest - 2f * EdgeMargin;
        var roomZ = _view.EdgeSouth - _view.EdgeNorth - 2f * EdgeMargin;
        var limit = Mathf.Min(FitDistance(d1, a.MaxX - a.MinX, d2, b.MaxX - b.MinX, roomX), FitDistance(d1, a.MaxZ - a.MinZ, d2, b.MaxZ - b.MinZ, roomZ));
        return Mathf.Clamp(limit, TiltFrom * 1.5f, 20000f);
    }

    private static float FitDistance(float d1, float span1, float d2, float span2, float room)
    {
        var slope = (span2 - span1) / (d2 - d1);
        return slope > 1e-5f ? d1 + (room - span1) / slope : float.MaxValue;
    }

    /// <summary>The focus nearest <paramref name="p"/> whose footprint stays between the stop lines (the middle, on an
    /// axis the footprint is too big for).</summary>
    private Vector3 LimitFocus(Vector3 p, ViewBox box)
    {
        float x0 = _view.EdgeWest + EdgeMargin - box.MinX, x1 = _view.EdgeEast - EdgeMargin - box.MaxX;
        float z0 = _view.EdgeNorth + EdgeMargin - box.MinZ, z1 = _view.EdgeSouth - EdgeMargin - box.MaxZ;
        p.X = x0 <= x1 ? Mathf.Clamp(p.X, x0, x1) : (x0 + x1) * 0.5f;
        p.Z = z0 <= z1 ? Mathf.Clamp(p.Z, z0, z1) : (z0 + z1) * 0.5f;
        return p;
    }

    /// <summary>Once a frame from MoveCamera, after the easing and the yaw, before UpdateCamera: the zoom cap and the
    /// pan limits, on the targets and on the eased values.</summary>
    private void ClampView()
    {
        ReadViewSlopes();
        var restYaw = _yawSteps * Mathf.Pi / 2f;

        // Zoom: the target against the yaw the camera is coming to rest at, the eased value against the yaw it has now.
        var maxRest = MaxDistance(restYaw);
        if (_distanceTarget > maxRest + 0.5f)
        {
            if (_clampTargetBefore >= 0f && _distanceTarget > _clampTargetBefore + 0.5f) ZoomLimitHint(); // the player zoomed out against it
            _distanceTarget = maxRest;
        }
        _clampTargetBefore = _distanceTarget;
        var maxNow = Mathf.Abs(_yaw - restYaw) < 1e-3f ? maxRest : MaxDistance(_yaw);
        if (_distance > maxNow) _distance = maxNow;

        // The ground height the footprint is measured with: where the camera's look height is heading this frame.
        var tile = _view.TileAt(_focus);
        var heading = tile >= 0 ? Mathf.Lerp(_lookY, _view.TopY(tile), 0.12f) : _lookY;
        var ground = Mathf.Max(_lookY, heading);
        if (ground > _clampY) _clampY = ground;
        else if (!_clampHeld) _clampY = Mathf.Lerp(_clampY, ground, 0.2f);

        var focus = LimitFocus(_focus, Footprint(_distance, _yaw, _clampY));
        var target = LimitFocus(_focusTarget, Footprint(_distanceTarget, restYaw, _clampY));
        _clampHeld = Moved(focus, _focus) || Moved(target, _focusTarget);
        _focus = focus;
        _focusTarget = target;
    }

    private static bool Moved(Vector3 a, Vector3 b) => Mathf.Abs(a.X - b.X) > 0.05f || Mathf.Abs(a.Z - b.Z) > 0.05f;

    /// <summary>At the zoom-out limit, the first couple of times: the whole planet is seen from orbit.</summary>
    private void ZoomLimitHint()
    {
        if (_zoomHints >= ZoomHintTimes || _clock - _zoomHintAt < ZoomHintGap || Me.Phase != LandingPhase.Landed) return;
        _zoomHints++;
        _zoomHintAt = _clock;
        _hud.PushEvent("Zoomed out as far as the map goes  ·  O for the view from orbit", new Color(0.62f, 0.82f, 1f));
    }

    /// <summary>FlyTo's last step. Close to a side edge or in a corner of the map the pan limits can leave a spot off
    /// screen (the view is widest on its far side); a quarter turn puts that edge on the far side. Tries the view as it
    /// is first, then a quarter turn either way, then the half turn; changes nothing if none shows it.</summary>
    private void TurnToShow(Vector3 point)
    {
        ReadViewSlopes();
        var tile = _view.TileAt(point);
        var ground = tile >= 0 ? _view.TopY(tile) : 0f;
        for (var i = 0; i < 4; i++)
        {
            var turn = i switch { 0 => 0, 1 => 1, 2 => -1, _ => 2 };
            var yaw = (_yawSteps + turn) * Mathf.Pi / 2f;
            var distance = Mathf.Min(_distanceTarget, MaxDistance(yaw));
            var focus = LimitFocus(point, Footprint(distance, yaw, ground));
            if (!OnScreen(point, focus, distance, yaw)) continue;
            if (turn != 0)
            {
                _yawSteps += turn;
                if (_inOrbit) _yaw = _yawSteps * Mathf.Pi / 2f; // nothing eases it up there: be turned already at the cut to the surface
                if (!_inOrbit) _hud.PushEvent("View turned to show it (it is at the edge of the map)  ·  Q / E turn the view", new Color(0.62f, 0.82f, 1f));
            }
            return;
        }
    }

    /// <summary>True if a ground point shows in the open screen (a little inside its rim) for a camera looking at
    /// <paramref name="focus"/> from this distance and yaw; both taken at the same ground height.</summary>
    private bool OnScreen(Vector3 point, Vector3 focus, float distance, float yaw)
    {
        var dir = TiltAt(distance);
        float dy = dir.Y, dz = dir.Z;
        float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
        float wx = point.X - focus.X, wz = point.Z - focus.Z;
        // Back out of the yaw, then from the eye (0, dy·d, dz·d) in the camera's axes.
        var lx = wx * cos - wz * sin;
        var lz = wx * sin + wz * cos;
        float vy = -dy * distance, vz = lz - dz * distance;
        var depth = -vy * dy - vz * dz;
        if (depth <= 0.01f) return false;
        var x = lx / depth;
        var y = (vy * dz - vz * dy) / depth;
        var rim = (_viewSlopeTop - _viewSlopeBottom) * 0.03f;
        return Mathf.Abs(x) <= _viewSlopeX * 0.97f && y <= _viewSlopeTop - rim && y >= _viewSlopeBottom + rim;
    }
}
