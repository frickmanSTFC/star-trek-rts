using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 4 in the site view (docs/space-jobs-contract.md "Phase 4"; render-only): a ship running dark (the
// derelict model darkened by SiteModels, its red and green running lights blinking slowly) and an alien probe (its Glow
// flickering in a repeating pattern while it "talks", steady once it is answered; a lavender beam from the flagship to it
// while the crew decode it or the answer goes out). The parts are found once per site scene (CollectSiteParts); the beam
// is made once per scene (a child of it, freed with it): no per-frame allocation. Called from UpdateSiteView.
public partial class OrbitView
{
    private readonly List<Node3D> _siteRunLights = new();
    private readonly List<Node3D> _siteGlows = new();
    private MeshInstance3D? _chainBeam;
    private StandardMaterial3D? _chainBeamMat;

    /// <summary>The probe's "words": on / off, a step every 0.18 s, looping.</summary>
    private const string ProbePattern = "1101001110100011011";

    private void ClearChainFx()
    {
        _siteRunLights.Clear();
        _siteGlows.Clear();
        _chainBeam = null; // a child of the site scene, freed with it
        _chainBeamMat = null;
    }

    private void UpdateChainSite(SpaceSite site)
    {
        if (_siteScene == null || _source.State is not { } sys) return;
        if (site.Kind == SiteKind.HiddenShip)
        {
            for (var i = 0; i < _siteRunLights.Count; i++)
                if (IsInstanceValid(_siteRunLights[i])) _siteRunLights[i].Visible = Mathf.PosMod(_siteTime + i * 0.6f, 1.6f) < 1.1f;
            return;
        }
        if (site.Kind != SiteKind.AlienProbe) return;
        var answered = sys.ProbeAnswered(_source.Player, site.Id);
        var step = Mathf.FloorToInt(_siteTime / 0.18f) % ProbePattern.Length;
        var on = answered || ProbePattern[step] == '1';
        for (var i = 0; i < _siteGlows.Count; i++)
            if (IsInstanceValid(_siteGlows[i])) _siteGlows[i].Visible = on;

        var action = sys.WorkSiteOf(_source.Player) == site.Id ? sys.WorkActionOf(_source.Player) : null;
        var replying = sys.AwayRunSiteOf(_source.Player) == site.Id;
        if (_chainBeam == null)
        {
            _chainBeamMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = Colors.White,
            };
            _chainBeam = new MeshInstance3D
            {
                Name = "ProbeBeam",
                Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 8, Rings = 1 },
                MaterialOverride = _chainBeamMat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _siteScene.AddChild(_chainBeam);
        }
        if ((action == "decode" || replying) && _shipHolder != null)
        {
            var from = _shipHolder.GlobalPosition;
            var to = _siteScene.GlobalPosition;
            Stretch(_chainBeam, from, to, 0.35f + 0.12f * Mathf.Sin(_siteTime * 7f)); // OrbitView.Mining.cs
            _chainBeamMat!.AlbedoColor = new Color(0.66f, 0.6f, 1f, (replying ? 0.45f : 0.3f) + 0.15f * (on ? 1f : 0f));
            _chainBeam.Visible = true;
        }
        else _chainBeam.Visible = false;
    }
}
