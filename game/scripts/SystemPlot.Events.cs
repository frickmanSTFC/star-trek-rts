using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 3 on the system plot (docs/space-jobs-contract.md "Phase 3"; render-only): moving sites drawn between
// sim ticks (a rogue asteroid, a comet, a drifting ion storm: StarSystemState.SitePosition at this tick and the next,
// lerped by the frame's alpha), and, through the same ImmediateMesh as the course lines (no allocation), a rogue
// asteroid's course to the body it will strike with a cross on the impact point, a comet's path with its active tail
// window drawn brighter, an ion storm's three rings (data radii), and an escort line from a ship to the stranded ship
// following it. Labels get the countdowns (IMPACT 3:12, TAIL IN 1:05 / TAIL ACTIVE 0:40, 5 PODS · AIR 1:20, AIR 6:00,
// DISPERSES 12:00, RECORDING INNER).
public partial class SystemPlot
{
    /// <summary>A rock on a collision course (and its course line).</summary>
    public static readonly Color SiteDanger = Color.FromHtml("#ff7a5c");

    private StarSystemState? _plotSys;
    private long _plotTick;
    private float _plotAlpha;

    /// <summary>From Refresh, before the ships move: the tick and alpha moving sites are drawn at this frame.</summary>
    private void PrepareSitePlot(SystemSource source, float alpha)
    {
        _plotSys = source.State;
        _plotTick = source.Sim.Tick;
        _plotAlpha = Mathf.Clamp(alpha, 0f, 1f);
    }

    /// <summary>A site's map position this frame: on its path between this tick and the next for a moving site.</summary>
    private Vector2 SiteMapPosition(SpaceSite s)
    {
        if (_plotSys == null || s.PathStart < 0) return new Vector2(s.Position.X.ToFloat(), s.Position.Y.ToFloat());
        var a = _plotSys.SitePosition(s, _plotTick);
        var b = _plotSys.SitePosition(s, _plotTick + 1);
        return new Vector2(a.X.ToFloat(), a.Y.ToFloat()).Lerp(new Vector2(b.X.ToFloat(), b.Y.ToFloat()), _plotAlpha);
    }

    private static Vector3 MapPlot(FixVec2 p) => new(p.X.ToFloat() * MapScale, 0f, -p.Y.ToFloat() * MapScale);

    private string PlotClock(long tick)
    {
        var s = (int)System.Math.Max(0, (tick - _plotTick + Simulation.TickRate - 1) / Simulation.TickRate);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>The label's countdown for an event site ("" for anything else or an event that is over).</summary>
    private string EventTag(StarSystemState sys, int player, SpaceSite s, SiteKnowledge k)
    {
        if (k == SiteKnowledge.Unknown || s.Outcome.Length > 0) return "";
        switch (s.Kind)
        {
            case SiteKind.RogueAsteroid when s.PathEnd >= 0:
                return $" · IMPACT {PlotClock(s.PathEnd)}";
            case SiteKind.Comet when s.PathStart >= 0:
            {
                var (ws, we) = sys.TailWindow(s);
                return _plotTick < ws ? $" · TAIL IN {PlotClock(ws)}" : _plotTick <= we ? $" · TAIL ACTIVE {PlotClock(we)}" : $" · LEAVING {PlotClock(s.PathEnd)}";
            }
            case SiteKind.Lifepods:
            {
                var waiting = 0;
                var least = long.MaxValue;
                for (var i = 0; i < s.PodCount && i < 31; i++)
                {
                    if (((s.PodsTaken | s.PodsLost) & (1 << i)) != 0) continue;
                    waiting++;
                    least = System.Math.Min(least, sys.PodAirTick(s, i));
                }
                return waiting == 0 ? "" : $" · {waiting} POD{(waiting == 1 ? "" : "S")} · AIR {PlotClock(least)}";
            }
            case SiteKind.StrandedShip:
                return s.AirTick >= 0 ? $" · AIR {PlotClock(s.AirTick)}" : " · POWERED";
            case SiteKind.Anomaly:
            {
                var tag = s.Event && s.PathEnd >= 0 ? $" · DISPERSES {PlotClock(s.PathEnd)}" : "";
                if (sys.WorkSiteOf(player) == s.Id && sys.WorkActionOf(player) is { } a && a.StartsWith("record_", System.StringComparison.Ordinal))
                    tag += $" · RECORDING {a[7..].ToUpperInvariant()}";
                return tag;
            }
        }
        return "";
    }

    /// <summary>Into the course mesh (DrawSiteCourses): a rogue's course and impact cross, a comet's path and window, a storm's
    /// rings, escort lines. Only sites the player knows; nothing for events that are over.</summary>
    private void DrawEventLines(SystemSource source, ref bool started)
    {
        if (source.State is not { } sys || _siteCourseMesh == null) return;
        var player = source.Player;
        var sites = sys.Sites;
        for (var i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            if (s.Outcome.Length > 0 || sys.SiteKnowledgeOf(player, s.Id) == SiteKnowledge.Unknown) continue;
            switch (s.Kind)
            {
                case SiteKind.RogueAsteroid when s.PathEnd >= 0 && sys.Body(s.TargetBody) is { } body:
                {
                    var from = SitePlot(s) + Vector3.Up * 0.7f;
                    var to = MapPlot(body.Position) + Vector3.Up * 0.7f;
                    Dashes(ref started, from, to, SiteDanger with { A = 0.85f }, 2.0f);
                    const float x = 2.4f;
                    Segment(ref started, to + new Vector3(-x, 0, -x), to + new Vector3(x, 0, x), SiteDanger);
                    Segment(ref started, to + new Vector3(-x, 0, x), to + new Vector3(x, 0, -x), SiteDanger);
                    break;
                }
                case SiteKind.Comet when s.PathStart >= 0:
                {
                    var (ws, we) = sys.TailWindow(s);
                    var a = MapPlot(s.PathFrom) + Vector3.Up * 0.5f;
                    var b = MapPlot(s.PathTo) + Vector3.Up * 0.5f;
                    Dashes(ref started, a, b, SiteAmber with { A = 0.28f }, 1.2f);
                    var w0 = MapPlot(sys.SitePosition(s, ws)) + Vector3.Up * 0.55f;
                    var w1 = MapPlot(sys.SitePosition(s, we)) + Vector3.Up * 0.55f;
                    Segment(ref started, w0, w1, SiteCyan with { A = _plotTick <= we ? 0.85f : 0.3f });
                    break;
                }
                case SiteKind.Anomaly when sys.SiteKindOf(s)?.Anomaly is { } an:
                {
                    var c = SitePlot(s) + Vector3.Up * 0.3f;
                    for (var r = 0; r < an.Rings.Count; r++)
                    {
                        var radius = an.Rings[r].Radius.ToFloat() * MapScale;
                        var colour = SiteLavender with { A = 0.25f + 0.2f * r };
                        const int n = 40;
                        var prev = c + new Vector3(radius, 0f, 0f);
                        for (var j = 1; j <= n; j++)
                        {
                            var t = j / (float)n * Mathf.Tau;
                            var next = c + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                            if (r < an.Rings.Count - 1 || j % 2 == 0) Segment(ref started, prev, next, colour); // the inner ring dashed
                            prev = next;
                        }
                    }
                    break;
                }
                case SiteKind.StrandedShip when sys.EscorterOf(s.Id) is var p and >= 0 && TowedPlot(sys, s, out var shipAt) is { } at:
                    Dashes(ref started, shipAt with { Y = 0.6f }, at with { Y = 0.6f }, (p == player ? SiteCyan : Neutral) with { A = 0.9f }, 0.8f);
                    break;
            }
        }
    }

    private void Begin(ref bool started)
    {
        if (started) return;
        _siteCourseMesh!.SurfaceBegin(Mesh.PrimitiveType.Lines);
        started = true;
    }

    private void Segment(ref bool started, Vector3 a, Vector3 b, Color colour)
    {
        Begin(ref started);
        _siteCourseMesh!.SurfaceSetColor(colour);
        _siteCourseMesh.SurfaceAddVertex(a);
        _siteCourseMesh.SurfaceSetColor(colour);
        _siteCourseMesh.SurfaceAddVertex(b);
    }

    private void Dashes(ref bool started, Vector3 a, Vector3 b, Color colour, float dash)
    {
        var length = a.DistanceTo(b);
        if (length < 0.5f) return;
        var n = Mathf.Clamp(Mathf.FloorToInt(length / dash), 1, 400);
        for (var d = 0; d < n; d += 2) Segment(ref started, a.Lerp(b, d / (float)n), a.Lerp(b, Mathf.Min(1f, (d + 1) / (float)n)), colour);
    }
}
