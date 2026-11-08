using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Construction drawn by exocomps (owner 2026-11-06: "the building to be done with exocomps/drones with beams that go
/// around the building for x seconds. pieces are built into it"). Render-only: the time is the sim's build_seconds and
/// the progress is <see cref="PlanetSurface.BuildFraction"/>.
///
/// <para>Each building under construction is split into build pieces (<see cref="CollectPieces"/>): optional child
/// nodes named Build0, Build1, … in that order, otherwise every MeshInstance3D sorted bottom-up then inside-out with
/// glowing parts last, merged into at most <see cref="MaxPieces"/>. A piece is a faint blue hologram until its share of
/// the build fraction comes up, then it is printed from the bottom up (build_print.gdshader: solid below a ragged
/// glowing cut, hologram above), flashes as it locks, and turns into its own solid materials with the glowing surfaces
/// dark (lights stay off until the building is finished).</para>
///
/// <para>2–5 exocomps (by size) fly out from the nearest finished core or Vehicle Bay when construction starts, circle
/// the site at varied heights, hover at work spots and fire beams (build_beam.gdshader) at the cut of the piece being
/// printed, with sparks, a glow at the emitter and a flare + light where the beam lands. A queued building is only a
/// faint ghost; a paused one (no free build queue, or the sim not advancing it) keeps its drones hovering with the beams
/// off. On completion PlanetView swaps in the finished model; <see cref="Attach"/> then plays a shimmer over it
/// (build_shimmer.gdshader), fades its lights up, and the drones peel off home. Upgrades (Mk levels, core upgrades) get
/// the drones and beams on the standing building, without the piece split.</para>
/// </summary>
public partial class ConstructionView : Node3D
{
    /// <summary>At most this many beams at once over the whole map (the rest of the drones just hover).</summary>
    public const int MaxBeams = 12;

    /// <summary>A building is split into at most this many pieces when it has no Build0.. groups.</summary>
    public const int MaxPieces = 12;

    /// <summary>The exocomp is drawn a bit bigger than life (1.9 long next to a 7-wide building) so it reads from the RTS camera.</summary>
    private const float DroneScale = 1.25f;

    private const float CruiseSpeed = 24f;
    private const float MinFlight = 1.3f;
    private const float MaxFlight = 3.4f;
    private const float LaunchGap = 0.3f;
    private const float LockSeconds = 0.45f;
    private const float ShimmerSeconds = 1.3f;
    private const float QueuedGhost = 0.1f;
    private const float BuildGhost = 0.24f;

    private static readonly StringName PYMin = "y_min";
    private static readonly StringName PYMax = "y_max";
    private static readonly StringName PProgress = "progress";
    private static readonly StringName PGhost = "ghost";
    private static readonly StringName PScan = "scan";
    private static readonly StringName PFlash = "flash";
    private static readonly StringName PBase = "base_colour";
    private static readonly StringName PTex = "albedo_tex";
    private static readonly StringName PMetal = "metal";
    private static readonly StringName PRough = "rough";
    private static readonly StringName PPhase = "phase";
    private static readonly StringName PSweep = "sweep";
    private static readonly StringName PFade = "fade";

    private readonly PlanetView _view;
    private readonly Simulation _sim;
    private readonly PlanetSurface _surface;
    private readonly RandomNumberGenerator _rng = new() { Seed = 7311UL }; // render-only

    private Shader? _printShader;
    private Shader _shimmerShader = null!;
    private Shader _beamShader = null!;
    private CylinderMesh _beamMesh = null!;
    private QuadMesh _flareMesh = null!;
    private ShaderMaterial _impactFlare = null!;
    private ShaderMaterial _emitterFlare = null!;
    private SphereMesh _sparkMesh = null!;
    private StandardMaterial3D _unpowered = null!;

    // Achievement beam colour on our own sites (TintBeam): the shader's and the light's standard blue, and our copies.
    private static readonly StringName PBeamColour = "beam_colour";
    private static readonly Color BeamBlue = new(0.35f, 0.75f, 1f), ImpactBlue = new(0.55f, 0.85f, 1f);
    private Color? _ourBeam;
    private ShaderMaterial? _ourImpact, _ourEmitter;
    private SphereMesh? _ourSparks;

    private readonly Dictionary<int, Node3D> _models = new();   // every structure model PlanetView has attached
    private readonly Dictionary<int, Site> _byId = new();
    private readonly List<Site> _sites = new();
    private readonly List<Exocomp> _drones = new();
    private readonly List<Beam> _beams = new();
    private readonly List<Shimmer> _shimmers = new();
    private float _time;

    private enum PieceState { Ghost, Printing, Locking, Solid }

    private sealed class Piece
    {
        public required MeshInstance3D[] Meshes;
        public required Material?[] Originals;  // each mesh's MaterialOverride before construction
        public required ShaderMaterial Print;
        public Aabb Box;                        // world space
        public float Start, End;                // its share of the build fraction
        public PieceState State;
        public float LockedAt;
        public float LastProgress = -1f, LastGhost = -1f, LastScan = -1f, LastFlash = -1f;
    }

    private sealed class Site
    {
        public required int Id;
        public required int Owner;
        public required Node3D Model;
        public required Piece[] Pieces;   // empty for an upgrade
        public required bool Upgrade;
        public Vector3 Centre;            // tile centre on the ground
        public float Radius;              // half the footprint's largest side
        public float Floor, Top;          // world y of the building's bounds
        public int Want;                  // drones this size of building gets
        public int Live;                  // drones assigned (flying out or working)
        public float NextLaunch;
        public float Shown;               // the build fraction as drawn (eased toward the sim's)
        public bool Working;              // progress is advancing this frame
        public bool Fresh = true;         // not started yet (queued with no work done): ghost only
        public int LastTicks = -1;
        public long TickAtAdvance;
        public float Spin;                // the drones' slow orbit
        public float CutY;                // where the beams aim (top of the printed part of the current piece)
        public Aabb Focus;                // the piece being printed (or the whole building for an upgrade)
        public bool InSight = true;       // fog: the local player sees the site now (else its drones and beams are hidden)
    }

    private enum DroneState { Outbound, Working, Home }

    private sealed class Exocomp
    {
        public required Node3D Node;
        public Node3D? Body;
        public Vector3 BodyRest;
        public Vector3 BodyScale = Vector3.One;
        public Node3D? Emitter;
        public Site? Site;
        public int Slot;
        public DroneState State;
        public Vector3 From;
        public Vector3 To;                // home point (Home only)
        public float T;                   // flight 0..1 (Home: negative = waiting to peel off)
        public float Duration;
        public bool Started;
        public float Phase;
        public Vector3 Spot;              // current work spot (world)
        public float NextHop;
        public float HopAngle, HopHeight, HopRadius;
        public Vector3 Aim;               // where the beam lands (smoothed)
        public Vector3 AimOffset;         // its random part, re-picked every half second or so
        public float NextAim;
        public Vector3 LastPos;
    }

    private sealed class Beam
    {
        public required MeshInstance3D Line;
        public required ShaderMaterial Material;
        public required MeshInstance3D Impact;
        public required MeshInstance3D Glow;     // at the emitter
        public required CpuParticles3D Sparks;
        public required OmniLight3D Light;
        public bool On;
        public Color? Tint;                       // null = the standard blue (TintBeam)
    }

    private sealed class Shimmer
    {
        public required Node3D Model;
        public required ShaderMaterial Material;
        public required List<GeometryInstance3D> Meshes;
        public required List<Light3D> Lights;
        public required float[] Energy;
        public float Age;
    }

    public ConstructionView(PlanetView view, Simulation sim)
    {
        _view = view;
        _sim = sim;
        _surface = sim.Surface!;
    }

    /// <summary>Shared shaders, meshes and materials, made on first use (Attach can come before _Ready).</summary>
    private void EnsureAssets()
    {
        if (_printShader != null) return;
        _printShader = GD.Load<Shader>("res://shaders/build_print.gdshader");
        _shimmerShader = GD.Load<Shader>("res://shaders/build_shimmer.gdshader");
        _beamShader = GD.Load<Shader>("res://shaders/build_beam.gdshader");
        var flare = GD.Load<Shader>("res://shaders/build_flare.gdshader");
        // Height 1 along local Y (-0.5..0.5), radius 1: each beam's transform stretches it from emitter to target.
        _beamMesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 8, Rings = 1, CapTop = false, CapBottom = false };
        _flareMesh = new QuadMesh { Size = Vector2.One };
        _impactFlare = new ShaderMaterial { Shader = flare };
        _impactFlare.SetShaderParameter("flare_colour", new Color(0.55f, 0.85f, 1f));
        _impactFlare.SetShaderParameter("energy", 3f);
        _emitterFlare = new ShaderMaterial { Shader = flare };
        _emitterFlare.SetShaderParameter("flare_colour", new Color(0.7f, 0.92f, 1f));
        _emitterFlare.SetShaderParameter("energy", 2.2f);
        _sparkMesh = new SphereMesh
        {
            Radius = 0.04f,
            Height = 0.08f,
            RadialSegments = 4,
            Rings = 2,
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.9f, 0.6f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.85f, 0.5f),
                EmissionEnergyMultiplier = 4f,
            },
        };
        // Glowing parts of a piece that is built but not yet switched on.
        _unpowered = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.12f, 0.15f), Metallic = 0.3f, Roughness = 0.35f };
    }

    // ---------------------------------------------------------------- PlanetView hooks

    /// <summary>
    /// PlanetView made a new model for a structure (placed, finished, or a core upgrade changed it). Unfinished: it
    /// becomes a build site (pieces, ghost). Finished while it was a site: the completion beat plays on the new model.
    /// </summary>
    private static void HideSigns(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Label3D label) label.Visible = false;
            HideSigns(child);
        }
    }

    public void Attach(SurfaceStructure s, Node3D model)
    {
        EnsureAssets();
        _models[s.Id] = model;
        var complete = s.State is StructureState.Complete or StructureState.Assembling;
        _byId.TryGetValue(s.Id, out var old);
        if (complete)
        {
            if (old == null) return;
            StartShimmer(model);
            Finish(old);
            return;
        }
        HideSigns(model); // painted numbers (variety decals) appear with the finished model, not over the ghost
        var site = NewSite(s, model, upgrade: false);
        if (old != null) // the unfinished model was replaced: keep the drones
        {
            site.Live = old.Live;
            site.Fresh = old.Fresh;
            site.Shown = old.Shown;
            foreach (var d in _drones)
                if (d.Site == old) d.Site = site;
            _sites.Remove(old);
        }
        _byId[s.Id] = site;
        _sites.Add(site);
    }

    /// <summary>The structure is gone (cancelled, salvaged): its drones fly home.</summary>
    public void Forget(int id)
    {
        _models.Remove(id);
        if (_byId.TryGetValue(id, out var site)) Finish(site);
    }

    // ---------------------------------------------------------------- sites

    private Site NewSite(SurfaceStructure s, Node3D model, bool upgrade)
    {
        var pieces = upgrade ? System.Array.Empty<Piece>() : CollectPieces(model);
        var bounds = pieces.Length == 0 ? BoundsOf(model) : Union(pieces);
        var centre = _view.TileCentre(s.Tile);
        var radius = Mathf.Max(Mathf.Max(bounds.End.X - centre.X, centre.X - bounds.Position.X), Mathf.Max(bounds.End.Z - centre.Z, centre.Z - bounds.Position.Z));
        radius = Mathf.Clamp(radius, 2f, 10f);
        var diag = bounds.Size.Length();
        if (!upgrade)
            foreach (var l in Lights(model)) l.Visible = false; // lights stay off until it is finished
        return new Site
        {
            Id = s.Id,
            Owner = s.Owner,
            Model = model,
            Pieces = pieces,
            Upgrade = upgrade,
            Centre = centre,
            Radius = radius,
            Floor = Mathf.Min(bounds.Position.Y, centre.Y),
            Top = Mathf.Max(bounds.End.Y, centre.Y + 2f),
            Want = Mathf.Clamp(Mathf.RoundToInt(2f + (diag - 8f) / 3f), 2, 5),
            NextLaunch = _time,
            Focus = bounds,
            CutY = Mathf.Min(bounds.Position.Y, centre.Y) + 1f,
            Spin = _rng.Randf() * Mathf.Tau,
        };
    }

    /// <summary>Construction or upgrade is over: the drones peel off home one after another.</summary>
    private void Finish(Site site)
    {
        _byId.Remove(site.Id);
        _sites.Remove(site);
        var k = 0;
        foreach (var d in _drones)
        {
            if (d.Site != site) continue;
            GoHome(d, -0.25f - k * 0.2f);
            k++;
        }
    }

    public void Update(float delta)
    {
        EnsureAssets();
        _time += delta;
        var structures = _surface.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var s = structures[i];
            var upgrading = s.State == StructureState.Complete && (s.UpgradingLevel > 0 || s.UpgradingTo != null);
            if (_byId.TryGetValue(s.Id, out var site))
            {
                if (site.Upgrade && !upgrading)
                {
                    // A level upgrade finished (a core upgrade swaps the model and comes through Attach instead).
                    if (GodotObject.IsInstanceValid(site.Model)) StartShimmer(site.Model);
                    Finish(site);
                    continue;
                }
                UpdateSite(s, site, delta);
            }
            else if (upgrading && _models.TryGetValue(s.Id, out var model) && GodotObject.IsInstanceValid(model))
            {
                site = NewSite(s, model, upgrade: true);
                site.Fresh = false;
                _byId[s.Id] = site;
                _sites.Add(site);
                UpdateSite(s, site, delta);
            }
        }

        var beams = 0;
        for (var i = _drones.Count - 1; i >= 0; i--)
        {
            var d = _drones[i];
            if (!UpdateDrone(d, delta, ref beams))
            {
                d.Node.QueueFree();
                _drones.RemoveAt(i);
            }
        }
        for (var i = beams; i < _beams.Count; i++) SetBeam(_beams[i], false);
        UpdateShimmers(delta);
    }

    private void UpdateSite(SurfaceStructure s, Site site, float delta)
    {
        if (!GodotObject.IsInstanceValid(site.Model)) return;
        var ticks = site.Upgrade ? s.UpgradeTicks : s.BuildTicks;
        if (ticks != site.LastTicks)
        {
            site.LastTicks = ticks;
            site.TickAtAdvance = _sim.Tick;
        }
        // Advancing = the sim moved it within the last couple of ticks (a paused game keeps the last look).
        var stalled = _sim.Tick - site.TickAtAdvance > 2;
        site.Working = (site.Upgrade || s.State == StructureState.Building) && !stalled;
        if (!site.Upgrade && ticks > 0) site.Fresh = false;
        var visible = site.Model.Visible;
        site.InSight = _view.ShowsLive(s.Owner, s.Tile);
        site.Spin += delta * 0.16f;

        // Drones: none for a building still waiting in the queue; the rest fly out one after another.
        var want = site.Fresh || !visible ? 0 : site.Want;
        if (site.Live < want && _time >= site.NextLaunch)
        {
            Launch(site);
            site.NextLaunch = _time + LaunchGap;
        }
        else if (site.Live > want)
        {
            foreach (var d in _drones)
                if (d.Site == site && d.State != DroneState.Home)
                {
                    GoHome(d, 0f);
                    break;
                }
        }

        if (site.Upgrade)
        {
            site.CutY = Mathf.Lerp(site.Floor + 1f, site.Top - 0.5f, 0.5f + 0.4f * Mathf.Sin(_time * 0.7f + site.Id));
            return;
        }

        var target = _surface.BuildFraction(s).ToFloat();
        if (target < site.Shown - 0.02f)
        {
            // Progress went back (test panel): pieces past it turn back into holograms.
            site.Shown = target;
            foreach (var p in site.Pieces)
                if (p.State == PieceState.Solid && p.End > target) Unbuild(p);
        }
        else site.Shown += (target - site.Shown) * (1f - Mathf.Exp(-delta * 5f));
        var ghost = site.Fresh ? QueuedGhost : BuildGhost;
        var focusSet = false;
        foreach (var p in site.Pieces)
        {
            if (p.State == PieceState.Solid) continue;
            var f = Mathf.Clamp((site.Shown - p.Start) / Mathf.Max(0.0001f, p.End - p.Start), 0f, 1f);
            if (p.State == PieceState.Locking || f >= 0.999f)
            {
                if (p.State != PieceState.Locking)
                {
                    p.State = PieceState.Locking;
                    p.LockedAt = _time;
                }
                var flash = 1f - (_time - p.LockedAt) / LockSeconds;
                if (flash <= 0f)
                {
                    MakeSolid(p);
                    continue;
                }
                SetPiece(p, 1f, ghost, 0f, flash);
                continue;
            }
            p.State = f > 0f ? PieceState.Printing : PieceState.Ghost;
            SetPiece(p, f, ghost, f > 0f ? (site.Working ? 1f : 0.25f) : 0f, 0f);
            if (!focusSet && f > 0f)
            {
                focusSet = true;
                site.Focus = p.Box;
                site.CutY = Mathf.Lerp(p.Box.Position.Y, p.Box.End.Y, f);
            }
        }
        if (!focusSet)
        {
            // Between pieces: aim at the next one's foot.
            foreach (var p in site.Pieces)
            {
                if (p.State != PieceState.Ghost) continue;
                site.Focus = p.Box;
                site.CutY = p.Box.Position.Y + 0.2f;
                break;
            }
        }
    }

    private void SetPiece(Piece p, float progress, float ghost, float scan, float flash)
    {
        if (!Mathf.IsEqualApprox(p.LastProgress, progress)) p.Print.SetShaderParameter(PProgress, progress);
        if (!Mathf.IsEqualApprox(p.LastGhost, ghost)) p.Print.SetShaderParameter(PGhost, ghost);
        if (!Mathf.IsEqualApprox(p.LastScan, scan)) p.Print.SetShaderParameter(PScan, scan);
        if (!Mathf.IsEqualApprox(p.LastFlash, flash)) p.Print.SetShaderParameter(PFlash, flash);
        p.LastProgress = progress;
        p.LastGhost = ghost;
        p.LastScan = scan;
        p.LastFlash = flash;
    }

    /// <summary>The piece is in place: its own materials back, with anything glowing dark until the building is done.</summary>
    private void MakeSolid(Piece p)
    {
        p.State = PieceState.Solid;
        for (var i = 0; i < p.Meshes.Length; i++)
        {
            var mi = p.Meshes[i];
            if (!GodotObject.IsInstanceValid(mi)) continue;
            var original = p.Originals[i];
            if (original != null)
            {
                mi.MaterialOverride = Emissive(original) ? _unpowered : original;
                continue;
            }
            mi.MaterialOverride = null;
            if (mi.Mesh == null) continue;
            for (var k = 0; k < mi.Mesh.GetSurfaceCount(); k++)
                if (Emissive(mi.GetSurfaceOverrideMaterial(k) ?? mi.Mesh.SurfaceGetMaterial(k)))
                    mi.SetSurfaceOverrideMaterial(k, _unpowered); // this model is thrown away when the building finishes
        }
    }

    private static void Unbuild(Piece p)
    {
        p.State = PieceState.Ghost;
        p.LastProgress = p.LastGhost = p.LastScan = p.LastFlash = -1f;
        foreach (var mi in p.Meshes)
            if (GodotObject.IsInstanceValid(mi)) mi.MaterialOverride = p.Print;
    }

    private static bool Emissive(Material? m) =>
        m is BaseMaterial3D b && b.EmissionEnabled && b.EmissionEnergyMultiplier > 0.6f && b.Emission.Luminance > 0.05f;

    // ---------------------------------------------------------------- pieces

    /// <summary>
    /// Splits a model into build pieces, in build order. Contract (CLAUDE.md): a model may group its meshes under child
    /// nodes named Build0, Build1, … (any depth); those are built in that order and any meshes outside them come last.
    /// Otherwise every visible MeshInstance3D (not the repair pad) is a part, sorted by the bottom of its bounds (0.6
    /// bands), then by distance from the centre, glowing parts last; consecutive parts are merged into at most
    /// <see cref="MaxPieces"/> pieces. Every piece gets its own print material.
    /// </summary>
    private Piece[] CollectPieces(Node3D model)
    {
        var groups = new List<List<MeshInstance3D>>();
        var claimed = new HashSet<MeshInstance3D>();
        for (var k = 0; k < 64; k++)
        {
            if (model.FindChild($"Build{k}", true, false) is not Node group) continue;
            var list = new List<MeshInstance3D>();
            Meshes(group, model, list);
            list.RemoveAll(m => !claimed.Add(m));
            if (list.Count > 0) groups.Add(list);
        }
        var all = new List<MeshInstance3D>();
        Meshes(model, model, all);
        if (groups.Count > 0)
        {
            var rest = all.FindAll(m => !claimed.Contains(m));
            if (rest.Count > 0) groups.Add(rest);
        }
        else
        {
            var origin = model.GlobalPosition;
            var parts = new List<(MeshInstance3D Mesh, Aabb Box, bool Light)>();
            foreach (var m in all) parts.Add((m, WorldBox(m), Glows(m)));
            parts.Sort((a, b) =>
            {
                if (a.Light != b.Light) return a.Light ? 1 : -1;
                var ba = Mathf.FloorToInt((a.Box.Position.Y - origin.Y) / 0.6f);
                var bb = Mathf.FloorToInt((b.Box.Position.Y - origin.Y) / 0.6f);
                if (ba != bb) return ba.CompareTo(bb);
                var da = Mathf.RoundToInt(Flat(a.Box.GetCenter() - origin).Length() * 10f); // whole tenths: a consistent order
                var db = Mathf.RoundToInt(Flat(b.Box.GetCenter() - origin).Length() * 10f);
                if (da != db) return da.CompareTo(db);
                return Mathf.RoundToInt(a.Box.End.Y * 10f).CompareTo(Mathf.RoundToInt(b.Box.End.Y * 10f));
            });
            var n = Mathf.Min(MaxPieces, parts.Count);
            for (var i = 0; i < n; i++)
            {
                var list = new List<MeshInstance3D>();
                for (var j = i * parts.Count / n; j < (i + 1) * parts.Count / n; j++) list.Add(parts[j].Mesh);
                groups.Add(list);
            }
        }

        var pieces = new Piece[groups.Count];
        var weights = new float[groups.Count];
        var maxVolume = 0.001f;
        for (var i = 0; i < groups.Count; i++)
        {
            var box = WorldBox(groups[i][0]);
            for (var j = 1; j < groups[i].Count; j++) box = box.Merge(WorldBox(groups[i][j]));
            var originals = new Material?[groups[i].Count];
            for (var j = 0; j < originals.Length; j++) originals[j] = groups[i][j].MaterialOverride;
            var print = PrintMaterial(groups[i][0], box);
            foreach (var m in groups[i]) m.MaterialOverride = print;
            pieces[i] = new Piece { Meshes = groups[i].ToArray(), Originals = originals, Print = print, Box = box };
            var v = Mathf.Max(0.05f, box.Size.X) * Mathf.Max(0.05f, box.Size.Y) * Mathf.Max(0.05f, box.Size.Z);
            weights[i] = v;
            maxVolume = Mathf.Max(maxVolume, v);
        }
        // Bigger pieces take longer (but even a small one takes a moment).
        var total = 0f;
        for (var i = 0; i < weights.Length; i++) total += weights[i] = 0.35f + Mathf.Sqrt(weights[i] / maxVolume);
        var at = 0f;
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i].Start = at / total;
            at += weights[i];
            pieces[i].End = at / total;
            SetPiece(pieces[i], 0f, QueuedGhost, 0f, 0f);
        }
        return pieces;
    }

    private ShaderMaterial PrintMaterial(MeshInstance3D sample, Aabb box)
    {
        var m = new ShaderMaterial { Shader = _printShader };
        m.SetShaderParameter(PYMin, box.Position.Y);
        m.SetShaderParameter(PYMax, box.End.Y);
        var source = sample.MaterialOverride
            ?? (sample.Mesh != null && sample.Mesh.GetSurfaceCount() > 0 ? sample.GetSurfaceOverrideMaterial(0) ?? sample.Mesh.SurfaceGetMaterial(0) : null);
        if (source is BaseMaterial3D b)
        {
            m.SetShaderParameter(PBase, b.AlbedoColor);
            if (b.AlbedoTexture != null) m.SetShaderParameter(PTex, b.AlbedoTexture);
            m.SetShaderParameter(PMetal, b.Metallic);
            m.SetShaderParameter(PRough, b.Roughness);
        }
        return m;
    }

    /// <summary>Visible meshes under a node (stopping at the repair pad, which is not built).</summary>
    private static void Meshes(Node node, Node3D model, List<MeshInstance3D> into)
    {
        if (node is Node3D n3 && node != model && (!n3.Visible || node.Name == "RepairPad")) return;
        if (node is MeshInstance3D mi && mi.Mesh != null) into.Add(mi);
        foreach (var child in node.GetChildren()) Meshes(child, model, into);
    }

    private static bool Glows(MeshInstance3D m)
    {
        if (m.MaterialOverride != null) return Emissive(m.MaterialOverride);
        if (m.Mesh == null || m.Mesh.GetSurfaceCount() == 0) return false;
        for (var k = 0; k < m.Mesh.GetSurfaceCount(); k++)
            if (!Emissive(m.GetSurfaceOverrideMaterial(k) ?? m.Mesh.SurfaceGetMaterial(k))) return false;
        return true; // all its surfaces glow (a lamp, a light strip)
    }

    private static Aabb WorldBox(MeshInstance3D m) => m.GlobalTransform * m.GetAabb();

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    private static Aabb Union(Piece[] pieces)
    {
        if (pieces.Length == 0) return new Aabb(Vector3.Zero, Vector3.One);
        var box = pieces[0].Box;
        for (var i = 1; i < pieces.Length; i++) box = box.Merge(pieces[i].Box);
        return box;
    }

    private static Aabb BoundsOf(Node3D model, Aabb? fallback = null)
    {
        var list = new List<MeshInstance3D>();
        Meshes(model, model, list);
        if (list.Count == 0) return fallback ?? new Aabb(model.GlobalPosition - new Vector3(3, 0, 3), new Vector3(6, 6, 6));
        var box = WorldBox(list[0]);
        for (var i = 1; i < list.Count; i++) box = box.Merge(WorldBox(list[i]));
        return box;
    }

    private static List<Light3D> Lights(Node node, List<Light3D>? into = null)
    {
        into ??= new List<Light3D>();
        foreach (var child in node.GetChildren())
        {
            if (child is Light3D l) into.Add(l);
            Lights(child, into);
        }
        return into;
    }

    // ---------------------------------------------------------------- drones

    private void Launch(Site site)
    {
        var colour = PlanetDriver.ColorOf(site.Owner);
        var node = BuildingModels.Exocomp(colour);
        AddChild(node);
        var home = HomeFor(site.Owner, site.Centre, site.Id);
        var d = new Exocomp
        {
            Node = node,
            Body = node.FindChild("Body", true, false) as Node3D,
            BodyRest = (node.FindChild("Body", true, false) as Node3D)?.Position ?? Vector3.Zero,
            BodyScale = ((node.FindChild("Body", true, false) as Node3D)?.Scale ?? Vector3.One) * DroneScale,
            Emitter = node.FindChild("Emitter", true, false) as Node3D,
            Site = site,
            Slot = NextSlot(site),
            State = DroneState.Outbound,
            From = home,
            Phase = _rng.Randf() * Mathf.Tau,
            NextHop = 0f,
            LastPos = home,
        };
        node.GlobalPosition = home;
        Hop(d, site);
        d.Spot = SpotFor(d, site);
        d.Duration = Mathf.Clamp(home.DistanceTo(d.Spot) / CruiseSpeed, MinFlight, MaxFlight);
        d.Aim = site.Focus.GetCenter();
        _drones.Add(d);
        site.Live++;
    }

    private int NextSlot(Site site)
    {
        for (var slot = 0; slot < 8; slot++)
        {
            var taken = false;
            foreach (var d in _drones)
                if (d.Site == site && d.State != DroneState.Home && d.Slot == slot) taken = true;
            if (!taken) return slot;
        }
        return 0;
    }

    private void GoHome(Exocomp d, float wait)
    {
        if (d.State == DroneState.Home) return;
        if (d.Site != null) d.Site.Live--;
        d.State = DroneState.Home;
        d.T = wait;
        d.Started = false;
        d.To = HomeFor(d.Site?.Owner ?? 0, d.Node.GlobalPosition, d.Site?.Id ?? 0);
        d.Site = null;
    }

    /// <summary>Exocomp docks by site (structure id): a hub founded on the ground launches and takes back its drones at
    /// the construction rover parked beside it (PlanetView.Founding.cs keeps this up to date).</summary>
    public readonly Dictionary<int, Vector3> Docks = new();

    /// <summary>Where a player's drones live: the site's own dock if it has one (a founded hub's construction rover),
    /// else on top of the nearest finished colony core or Vehicle Bay (or in orbit above the point if there is none).
    /// Never the building they just worked on.</summary>
    private Vector3 HomeFor(int owner, Vector3 near, int except)
    {
        if (Docks.TryGetValue(except, out var dock)) return dock;
        var best = near + new Vector3(0, 90f, 0);
        var bestDist = float.MaxValue;
        var structures = _surface.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var s = structures[i];
            if (s.Owner != owner || s.State != StructureState.Complete || s.Id == except) continue;
            var def = _sim.Data.Building(s.DefId);
            if (!def.IsCore && !def.IsFactory) continue;
            var c = _view.TileCentre(s.Tile);
            var dist = Flat(c - near).LengthSquared();
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = c + new Vector3(0, def.IsCore ? 5.5f : 6.2f, 0);
        }
        return best;
    }

    /// <summary>Picks the drone's next work spot round the site: its slot's angle plus a jitter, a radius and a height.</summary>
    private void Hop(Exocomp d, Site site)
    {
        d.HopAngle = (_rng.Randf() - 0.5f) * 0.9f;
        d.HopHeight = 0.8f + _rng.Randf() * 2.6f + (d.Slot % 2) * 1.2f;
        d.HopRadius = site.Radius + 1.6f + _rng.Randf() * 1.8f;
        d.NextHop = _time + 2.4f + _rng.Randf() * 2.2f;
    }

    private Vector3 SpotFor(Exocomp d, Site site)
    {
        var a = site.Spin + d.Slot * Mathf.Tau / Mathf.Max(site.Want, 1) + d.HopAngle;
        var y = Mathf.Clamp(site.CutY + d.HopHeight, site.Floor + 1.6f, site.Top + 3f);
        return new Vector3(site.Centre.X + Mathf.Cos(a) * d.HopRadius, y, site.Centre.Z + Mathf.Sin(a) * d.HopRadius);
    }

    /// <summary>Flies, hovers and beams one drone. False when it is home and can go.</summary>
    private bool UpdateDrone(Exocomp d, float delta, ref int beams)
    {
        if (!GodotObject.IsInstanceValid(d.Node)) return false;
        var pos = d.Node.GlobalPosition;
        var site = d.Site;
        var bob = Mathf.Sin(_time * 2.3f + d.Phase) * 0.14f;
        Vector3 face;
        var firing = false;
        var size = 1f; // shrinks into / grows out of its dock on the home building

        switch (d.State)
        {
            case DroneState.Outbound when site != null:
            {
                if (_time >= d.NextHop) Hop(d, site);
                d.Spot = SpotFor(d, site);
                d.T += delta / d.Duration;
                var t = Mathf.Min(d.T, 1f);
                var e = t * t * (3f - 2f * t);
                var arc = (3f + d.From.DistanceTo(d.Spot) * 0.12f) * Mathf.Sin(Mathf.Pi * t);
                pos = d.From.Lerp(d.Spot, e) + new Vector3(0, arc, 0);
                face = d.Spot - pos;
                size = Mathf.Clamp(d.T / 0.15f, 0.3f, 1f);
                if (d.T >= 1f) d.State = DroneState.Working;
                break;
            }
            case DroneState.Working when site != null:
            {
                if (_time >= d.NextHop) Hop(d, site);
                if (site.Working) d.Spot = SpotFor(d, site); // paused: stay where it is
                pos = pos.Lerp(d.Spot + new Vector3(0, bob, 0), 1f - Mathf.Exp(-delta * 2.2f));
                // Beam on most of the time, with short breaks while it lines up again.
                firing = site.Working && Mathf.Sin(_time * 0.9f + d.Phase * 3f) > -0.75f;
                if (_time >= d.NextAim)
                {
                    var ext = site.Focus.Size * 0.5f;
                    d.AimOffset = new Vector3((_rng.Randf() - 0.5f) * ext.X, (_rng.Randf() - 0.5f) * 0.3f, (_rng.Randf() - 0.5f) * ext.Z);
                    d.NextAim = _time + 0.35f + _rng.Randf() * 0.45f;
                }
                // Aim at the near side of the piece, at the cut.
                var focus = site.Focus.GetCenter();
                var toward = Flat(pos - focus);
                var side = toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward;
                var ext2 = site.Focus.Size * 0.5f;
                var reach = Mathf.Min(ext2.X, ext2.Z) * 0.75f;
                var aim = new Vector3(focus.X, site.CutY, focus.Z) + side * reach + d.AimOffset * 0.6f;
                d.Aim = d.Aim.Lerp(aim, 1f - Mathf.Exp(-delta * 7f));
                face = d.Aim - pos;
                break;
            }
            default:
            {
                if (d.State != DroneState.Home) GoHome(d, 0f); // its site went away
                if (d.T < 0f)
                {
                    d.T += delta; // waiting its turn to peel off
                    face = d.Aim - pos;
                    pos += new Vector3(0, delta * 0.6f, 0);
                    break;
                }
                if (!d.Started)
                {
                    d.Started = true;
                    d.From = pos;
                    d.T = 0f;
                    d.Duration = Mathf.Clamp(pos.DistanceTo(d.To) / CruiseSpeed, MinFlight, MaxFlight);
                }
                d.T += delta / d.Duration;
                var t = Mathf.Min(d.T, 1f);
                var e = t * t * (3f - 2f * t);
                pos = d.From.Lerp(d.To, e) + new Vector3(0, (2.5f + d.From.DistanceTo(d.To) * 0.1f) * Mathf.Sin(Mathf.Pi * t), 0);
                face = d.To - pos;
                size = Mathf.Clamp((1f - d.T) / 0.15f, 0.3f, 1f);
                if (d.T >= 1f) return false; // docked
                break;
            }
        }

        // Fog: another player's exocomps over a site out of sight are hidden (they keep flying, so they're right when it shows).
        var hidden = site != null && !site.InSight;
        d.Node.Visible = !hidden;
        firing &= !hidden;
        d.Node.GlobalPosition = pos;
        var velocity = delta > 0f ? (pos - d.LastPos) / delta : Vector3.Zero;
        d.LastPos = pos;
        var flat = Flat(face);
        if (flat.LengthSquared() > 0.0001f)
        {
            var look = Basis.LookingAt(flat.Normalized(), Vector3.Up);
            // Lean into the flight a little (nose down when it goes fast).
            var pitch = Mathf.Clamp(Flat(velocity).Length() * 0.02f, 0f, 0.35f);
            var want = look * new Basis(Vector3.Right, -pitch);
            d.Node.Basis = d.Node.Basis.Orthonormalized().Slerp(want, Mathf.Min(1f, delta * 6f));
        }
        if (d.Body != null)
        {
            d.Body.Position = d.BodyRest + new Vector3(0, d.State == DroneState.Working ? bob * 0.5f : 0f, 0);
            d.Body.Scale = d.BodyScale * size;
        }

        if (firing && beams < MaxBeams)
        {
            var from = d.Emitter != null ? d.Emitter.GlobalPosition : pos;
            DrawBeam(BeamAt(beams), from, d.Aim, d.Phase, site != null && site.Owner == _view.LocalPlayer ? Cosmetics.BeamColour : null);
            beams++;
        }
        return true;
    }

    // ---------------------------------------------------------------- beams

    private Beam BeamAt(int i)
    {
        while (_beams.Count <= i)
        {
            var k = _beams.Count;
            var material = new ShaderMaterial { Shader = _beamShader };
            material.SetShaderParameter(PPhase, k * 0.137f);
            var line = new MeshInstance3D { Mesh = _beamMesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            var impact = new MeshInstance3D { Mesh = _flareMesh, MaterialOverride = _impactFlare, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            var glow = new MeshInstance3D { Mesh = _flareMesh, MaterialOverride = _emitterFlare, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            var sparks = new CpuParticles3D
            {
                Amount = 14,
                Lifetime = 0.45f,
                Emitting = false,
                LocalCoords = false,
                Spread = 65f,
                InitialVelocityMin = 1.2f,
                InitialVelocityMax = 3.6f,
                Gravity = new Vector3(0, -9f, 0),
                Mesh = _sparkMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            var light = new OmniLight3D { LightColor = new Color(0.55f, 0.85f, 1f), LightEnergy = 1.2f, OmniRange = 5f, ShadowEnabled = false, Visible = false };
            AddChild(line);
            AddChild(impact);
            AddChild(glow);
            AddChild(sparks);
            AddChild(light);
            _beams.Add(new Beam { Line = line, Material = material, Impact = impact, Glow = glow, Sparks = sparks, Light = light });
        }
        return _beams[i];
    }

    private void DrawBeam(Beam b, Vector3 from, Vector3 to, float phase, Color? colour)
    {
        TintBeam(b, colour);
        var dir = to - from;
        var len = dir.Length();
        if (len < 0.05f)
        {
            SetBeam(b, false);
            return;
        }
        SetBeam(b, true);
        var y = dir / len;
        var x = y.Cross(Mathf.Abs(y.Y) < 0.95f ? Vector3.Up : Vector3.Right).Normalized();
        var z = x.Cross(y);
        var r = 0.17f * (1f + 0.25f * Mathf.Sin(_time * 23f + phase * 9f));
        b.Line.GlobalTransform = new Transform3D(new Basis(x * r, y * len, z * r), (from + to) * 0.5f);
        var flare = 1.1f + 0.35f * Mathf.Sin(_time * 31f + phase * 5f);
        b.Impact.GlobalTransform = new Transform3D(Basis.Identity.Scaled(Vector3.One * flare), to - y * 0.1f);
        b.Glow.GlobalTransform = new Transform3D(Basis.Identity.Scaled(Vector3.One * (0.55f + 0.1f * Mathf.Sin(_time * 19f + phase))), from);
        b.Sparks.GlobalPosition = to;
        b.Sparks.Direction = -y;
        b.Light.GlobalPosition = to - y * 0.6f;
        b.Light.LightEnergy = 1.1f + 0.4f * Mathf.Sin(_time * 27f + phase * 3f);
    }

    /// <summary>A pooled beam in the standard blue (null) or our achievement beam colour (Cosmetics.BeamColour, local
    /// player's sites only): beam, impact and emitter flares, sparks and the impact light. Only changes on a switch.</summary>
    private void TintBeam(Beam b, Color? colour)
    {
        if (b.Tint == colour) return;
        b.Tint = colour;
        if (colour is { } c)
        {
            OurBeamLook(c);
            b.Material.SetShaderParameter(PBeamColour, c);
            b.Impact.MaterialOverride = _ourImpact;
            b.Glow.MaterialOverride = _ourEmitter;
            b.Sparks.Mesh = _ourSparks;
            b.Light.LightColor = c.Lerp(Colors.White, 0.2f);
        }
        else
        {
            b.Material.SetShaderParameter(PBeamColour, BeamBlue);
            b.Impact.MaterialOverride = _impactFlare;
            b.Glow.MaterialOverride = _emitterFlare;
            b.Sparks.Mesh = _sparkMesh;
            b.Light.LightColor = ImpactBlue;
        }
    }

    /// <summary>The flares and sparks in our beam colour, remade only when the colour changes.</summary>
    private void OurBeamLook(Color c)
    {
        if (_ourBeam == c && _ourImpact != null) return;
        _ourBeam = c;
        var flare = _impactFlare.Shader;
        _ourImpact = new ShaderMaterial { Shader = flare };
        _ourImpact.SetShaderParameter("flare_colour", c.Lerp(Colors.White, 0.25f));
        _ourImpact.SetShaderParameter("energy", 3f);
        _ourEmitter = new ShaderMaterial { Shader = flare };
        _ourEmitter.SetShaderParameter("flare_colour", c.Lerp(Colors.White, 0.45f));
        _ourEmitter.SetShaderParameter("energy", 2.2f);
        var spark = c.Lerp(Colors.White, 0.5f);
        _ourSparks = new SphereMesh
        {
            Radius = 0.04f,
            Height = 0.08f,
            RadialSegments = 4,
            Rings = 2,
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = spark,
                EmissionEnabled = true,
                Emission = spark,
                EmissionEnergyMultiplier = 4f,
            },
        };
    }

    private static void SetBeam(Beam b, bool on)
    {
        if (b.On == on) return;
        b.On = on;
        b.Line.Visible = b.Impact.Visible = b.Glow.Visible = b.Light.Visible = on;
        b.Sparks.Emitting = on;
    }

    // ---------------------------------------------------------------- completion

    /// <summary>A shimmer runs up the finished building and its lights come up.</summary>
    private void StartShimmer(Node3D model)
    {
        var box = BoundsOf(model);
        var material = new ShaderMaterial { Shader = _shimmerShader };
        material.SetShaderParameter(PYMin, box.Position.Y);
        material.SetShaderParameter(PYMax, box.End.Y);
        material.SetShaderParameter(PSweep, 0f);
        material.SetShaderParameter(PFade, 1f);
        var meshes = new List<MeshInstance3D>();
        Meshes(model, model, meshes);
        var geometry = new List<GeometryInstance3D>();
        foreach (var m in meshes)
        {
            if (m.MaterialOverlay != null) continue; // somebody else's overlay: leave it be
            m.MaterialOverlay = material;
            geometry.Add(m);
        }
        var lights = Lights(model);
        var energy = new float[lights.Count];
        for (var i = 0; i < lights.Count; i++)
        {
            energy[i] = lights[i].LightEnergy;
            lights[i].LightEnergy = 0f;
        }
        _shimmers.Add(new Shimmer { Model = model, Material = material, Meshes = geometry, Lights = lights, Energy = energy });
    }

    private void UpdateShimmers(float delta)
    {
        for (var i = _shimmers.Count - 1; i >= 0; i--)
        {
            var sh = _shimmers[i];
            sh.Age += delta;
            if (!GodotObject.IsInstanceValid(sh.Model))
            {
                _shimmers.RemoveAt(i);
                continue;
            }
            var t = sh.Age / ShimmerSeconds;
            sh.Material.SetShaderParameter(PSweep, Mathf.Min(t * 1.15f, 1.2f));
            sh.Material.SetShaderParameter(PFade, Mathf.Clamp(1.6f - t * 1.6f, 0f, 1f));
            // Lights come on as the sweep passes, with a stutter.
            var on = Mathf.Clamp((t - 0.35f) / 0.5f, 0f, 1f);
            var stutter = on < 1f && Mathf.Sin(sh.Age * 60f) < -0.3f ? 0.3f : 1f;
            for (var k = 0; k < sh.Lights.Count; k++)
                if (GodotObject.IsInstanceValid(sh.Lights[k])) sh.Lights[k].LightEnergy = sh.Energy[k] * on * stutter;
            if (t < 1f) continue;
            foreach (var m in sh.Meshes)
                if (GodotObject.IsInstanceValid(m) && m.MaterialOverlay == sh.Material) m.MaterialOverlay = null;
            for (var k = 0; k < sh.Lights.Count; k++)
                if (GodotObject.IsInstanceValid(sh.Lights[k])) sh.Lights[k].LightEnergy = sh.Energy[k];
            _shimmers.RemoveAt(i);
        }
    }
}
