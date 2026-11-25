using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 4 on the system plot (docs/space-jobs-contract.md "Phase 4"; render-only): the chain-only sites. A ship
// running dark that the player hunts (StarSystemState.HiddenSearchArea) shows as a dashed rust circle where the search
// stands, shrinking with every fix, with one label "SEARCH AREA · FIX n OF 3" (one Label3D, its text set only when the
// fixes change: no per-frame allocation); once found it is a dark hull glyph with two running lights. An alien probe is a
// lavender diamond with four antennae; it moves every tick as it follows the flagship. Labels get RUNNING 0:40 (a hidden
// ship that bolted), FOLLOWING US / DECODED 40 % / ANSWERED (a probe).
public partial class SystemPlot
{
    private Label3D? _searchLabel;
    private int _searchFixes = -1, _searchNeed = -1;

    /// <summary>The label's words for a chain-only site ("" for anything else).</summary>
    private string ChainTag(StarSystemState sys, int player, SpaceSite s, SiteKnowledge k)
    {
        if (k == SiteKnowledge.Unknown || k == SiteKnowledge.Spent) return "";
        switch (s.Kind)
        {
            case SiteKind.HiddenShip when s.PathStart >= 0 && _plotTick < s.PathEnd:
                return $" · RUNNING {PlotClock(s.PathEnd)}";
            case SiteKind.AlienProbe when sys.SiteWork(player, s.Id) is { } w:
                if (w.Answer.Length > 0) return w.AnsweredWell ? " · UNDERSTOOD" : " · ANSWERED";
                if (w.Decoded) return " · DECODED · ANSWER IT";
                if (w.Decode.ToFloat() > 0f) return $" · DECODED {Mathf.RoundToInt(Mathf.Clamp(w.Decode.ToFloat(), 0f, 1f) * 100)} %";
                return s.Follow == player ? " · FOLLOWING US" : "";
        }
        return "";
    }

    /// <summary>Into the course mesh: a dashed circle for every hidden ship the player is hunting, and the one label.</summary>
    private void DrawSearchAreas(SystemSource source, ref bool started)
    {
        if (source.State is not { } sys || _siteCourseMesh == null) return;
        var labelled = false;
        var sites = sys.Sites;
        for (var i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            if (s.Kind != SiteKind.HiddenShip || sys.HiddenSearchArea(source.Player, s.Id) is not { } area) continue;
            var (fixes, need) = sys.HiddenFixes(source.Player, s.Id);
            if (!sys.IsHunting(source.Player, s.Id)) continue; // only one a mission (or the test panel) set us on
            var c = MapPlot(area.Centre) + Vector3.Up * 0.35f;
            var radius = Mathf.Max(1f, area.Radius.ToFloat() * MapScale);
            const int n = 56;
            var prev = c + new Vector3(radius, 0f, 0f);
            for (var j = 1; j <= n; j++)
            {
                var t = j / (float)n * Mathf.Tau;
                var next = c + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                if (j % 2 == 0) Segment(ref started, prev, next, SiteRust with { A = 0.75f });
                prev = next;
            }
            if (labelled) continue;
            labelled = true;
            if (_searchLabel == null)
            {
                _searchLabel = Label("", Vector3.Zero, SiteRust, 16);
                _searchLabel.Name = "SearchArea";
                _siteLayer!.AddChild(_searchLabel);
            }
            if (fixes != _searchFixes || need != _searchNeed)
            {
                _searchFixes = fixes;
                _searchNeed = need;
                _searchLabel.Text = $"SEARCH AREA · FIX {fixes} OF {need}";
            }
            _searchLabel.Position = c + new Vector3(0f, 0.6f, -radius - 1.2f);
            _searchLabel.Visible = true;
        }
        if (!labelled && _searchLabel != null) _searchLabel.Visible = false;
    }

    // ---------------------------------------------------------------- glyphs (painted once, see GlyphTexture)

    /// <summary>An alien probe: a diamond body and four thin antennae out of its corners.</summary>
    private static float ProbeAlpha(Vector2 p)
    {
        const float k = 2.6f;
        var body = Mathf.Abs(p.X) + Mathf.Abs(p.Y) <= 4.2f * k ? 1f : Stroke(Mathf.Abs(Mathf.Abs(p.X) + Mathf.Abs(p.Y) - 4.2f * k), 1f);
        var a = body;
        a = Mathf.Max(a, Stroke(SegmentDistance(p, new Vector2(-4.2f, 0) * k, new Vector2(-10.5f, 0) * k), 1f));
        a = Mathf.Max(a, Stroke(SegmentDistance(p, new Vector2(4.2f, 0) * k, new Vector2(10.5f, 0) * k), 1f));
        a = Mathf.Max(a, Stroke(SegmentDistance(p, new Vector2(0, -4.2f) * k, new Vector2(0, -10.5f) * k), 1f));
        a = Mathf.Max(a, Stroke(SegmentDistance(p, new Vector2(0, 4.2f) * k, new Vector2(0, 10.5f) * k), 1f));
        foreach (var tip in ProbeTips) a = Mathf.Max(a, Disc(p, tip * k, 1.4f * k));
        return a;
    }

    private static readonly Vector2[] ProbeTips = { new(-10.5f, 0), new(10.5f, 0), new(0, -10.5f), new(0, 10.5f) };

    /// <summary>A ship running dark: the wreck's hull outline, dashed, with a running light at each end.</summary>
    private static float DarkHullAlpha(Vector2 p)
    {
        var hull = PolyStroke(p, WreckHull, 1.4f);
        var along = (p.X + p.Y * 0.4f) / 2.6f;
        var dashed = Mathf.PosMod(along, 5f) < 3f ? hull : hull * 0.25f;
        var lights = Mathf.Max(Disc(p, WreckHull[0], 2.2f * 2.6f), Disc(p, WreckHull[2], 2.2f * 2.6f));
        return Mathf.Max(dashed, lights);
    }
}
