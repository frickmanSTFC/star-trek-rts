using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The guided start (docs/menu-contract.md C): one TutorialCard at a time, steps and conditions in Tutorial.cs. Runs only
// when GameSetup.Tutorial is on (a campaign start with tutorial hints on); debug builds can (re)start it with F4.
// Facts come from the sim and the view a few times a second; the card's anchor is a real HUD / helm rect when one can
// be found (helm pills by id; PlanetHud's top bar and BUILD button and the drawer's tiles by their private fields, read
// with reflection so no other file had to change: a renamed field just means no arrow).
public partial class PlanetDriver
{
    private Tutorial? _tutorial;
    private TutorialCard? _tutorialCard;
    private readonly TutorialFacts _tutorialFacts = new();
    private double _tutorialFactsAt = -1;
    private int _tutorialVersion = -1;
    private ScanMode _tutorialScan;
    private int _tutorialScans;
    private readonly HashSet<int> _tutorialPods = new();
    private readonly HashSet<int> _tutorialPodsNow = new();
    private int _tutorialPodArrivals;
    private bool _tutorialKeyHeld;
    private bool _tutorialWarned;
    private Control? _tutorialAnchorControl;
    private Rect2 _tutorialAnchorExtra; // helm scans: the union of the scan pills (several controls)
    private TutorialAnchor _tutorialAnchorKind = TutorialAnchor.None;

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo? HudTopBarField = typeof(PlanetHud).GetField("_topBar", Private);
    private static readonly FieldInfo? HudBuildButtonField = typeof(PlanetHud).GetField("_buildButton", Private);
    private static readonly FieldInfo? DrawerTilesField = typeof(BuildDrawer).GetField("_tiles", Private);

    /// <summary>Once, at the end of _Ready: start the tutorial if this game wants it.</summary>
    private void BeginTutorial()
    {
        if (GameSetup.Tutorial) StartTutorial();
    }

    private void StartTutorial()
    {
        EndTutorial(false);
        _tutorial = new Tutorial();
        _tutorialVersion = -1;
        _tutorialFactsAt = -1;
        _tutorialScan = _orbit.Mode;
        _tutorialScans = 0;
        _tutorialPods.Clear();
        foreach (var p in Surface.Pods) if (p.Owner == LocalPlayer) _tutorialPods.Add(p.Id);
        _tutorialPodArrivals = 0;
        _tutorialFacts.World = PlanetName;
        _tutorialFacts.Names = _data.Buildings.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First().Name);
    }

    private void EndTutorial(bool skipped)
    {
        if (_tutorialCard != null && GodotObject.IsInstanceValid(_tutorialCard)) _tutorialCard.Close();
        _tutorialCard = null;
        _tutorialAnchorControl = null;
        if (_tutorial != null && skipped) _hud.PushEvent("Tutorial skipped: F1 shows the controls", new Color(0.62f, 0.82f, 1f));
        _tutorial = null;
    }

    /// <summary>The mouse is over the tutorial card (counts as UI for the map).</summary>
    private bool TutorialBlocksMouse(Vector2 mouse) => _tutorialCard != null && GodotObject.IsInstanceValid(_tutorialCard) && _tutorialCard.IsOverUi(mouse);

    /// <summary>Once a frame from _Process: watch the game, move the steps on, keep the card and its arrow in place.</summary>
    private void UpdateTutorial(double delta)
    {
        TutorialDebugKey();
        if (_tutorial == null) return;

        // Counters that need every frame (a pod can land between two fact refreshes; a scan can be switched and back).
        if (_orbit.Mode != _tutorialScan)
        {
            _tutorialScan = _orbit.Mode;
            _tutorialScans++;
        }
        _tutorialPodsNow.Clear();
        foreach (var p in Surface.Pods) if (p.Owner == LocalPlayer) _tutorialPodsNow.Add(p.Id);
        foreach (var id in _tutorialPods) if (!_tutorialPodsNow.Contains(id)) _tutorialPodArrivals++;
        _tutorialPods.Clear();
        _tutorialPods.UnionWith(_tutorialPodsNow);

        if (_tutorialFactsAt < 0 || _clock - _tutorialFactsAt >= 0.2)
        {
            _tutorialFactsAt = _clock;
            try
            {
                FillTutorialFacts(_tutorialFacts);
                _tutorial.Update(_tutorialFacts);
            }
            catch (System.Exception e) // never let the tutorial break the frame: warn once, keep the last facts
            {
                if (!_tutorialWarned) GD.PushWarning($"Tutorial: {e.Message}");
                _tutorialWarned = true;
            }
        }
        if (_tutorial.Finished)
        {
            Sfx.Ui("tutorial_done");
            EndTutorial(false);
            return;
        }
        var step = _tutorial.Current!;

        if (_tutorialCard == null || !GodotObject.IsInstanceValid(_tutorialCard))
        {
            _tutorialCard = new TutorialCard { Name = "TutorialCard" };
            _hud.AddOverlay(_tutorialCard); // above the bottom panel, drawer and large card, under F1 help and the tooltip
            _tutorialCard.Next += () =>
            {
                if (_tutorial == null) return;
                _tutorial.Acknowledge(_tutorialFacts);
                _tutorialFactsAt = -1; // fresh facts next frame
            };
            _tutorialCard.SkipAll += () => EndTutorial(true);
            _tutorialVersion = -1;
        }
        if (_tutorialVersion != _tutorial.Version)
        {
            _tutorialVersion = _tutorial.Version;
            _tutorialAnchorKind = (TutorialAnchor)(-1); // look the anchor up again
            _tutorialCard.Show(new TutorialStep(step.Id, Tutorial.Fill(step.Title, _tutorialFacts), Tutorial.Fill(step.Body, _tutorialFacts),
                _tutorial.StepLabel, new Rect2(), step.NextText));
        }
        _tutorialCard.SetHint(_tutorial.HintFor(_tutorialFacts));
        Rect2 anchor;
        try { anchor = TutorialAnchorRect(step); }
        catch (System.Exception) { anchor = new Rect2(); } // a HUD field changed shape: no arrow
        _tutorialCard.SetAnchor(anchor);
        _tutorialCard.SetArea(TutorialArea());
        // Out of the way while a full-screen card or screen is up (the large building card shows PLACE itself).
        // The mission comms card and log sit in the same middle area: the tutorial waits under them (PlanetDriver.Missions.cs).
        _tutorialCard.Visible = !(_bigCard.IsOpen || _inventory.IsOpen || _achScreen.IsOpen || _dossier.IsOpen || _chooser.IsOpen || _commsCard.IsOpen || _missionLog.IsOpen);
    }

    /// <summary>Debug builds: F4 (re)starts the tutorial, so it can be tried before the main menu is merged.</summary>
    private void TutorialDebugKey()
    {
        if (!OS.IsDebugBuild()) return;
        var down = Input.IsKeyPressed(Key.F4) && GetViewport().GuiGetFocusOwner() is not LineEdit;
        if (down && !_tutorialKeyHeld)
        {
            StartTutorial();
            _hud.PushEvent("Tutorial restarted (F4, debug builds)", new Color(0.62f, 0.82f, 1f));
        }
        _tutorialKeyHeld = down;
    }

    private void FillTutorialFacts(TutorialFacts f)
    {
        var me = Me;
        var surface = Surface;
        f.World = PlanetName;
        f.InOrbit = _inOrbit;
        f.Phase = me.Phase;
        f.LandArmed = _orbitAction == "land";
        f.ScanChanges = _tutorialScans;
        f.PodArrivals = _tutorialPodArrivals;

        f.HubReady = false;
        f.AssembleSecondsLeft = 0;
        foreach (var b in surface.Bases(LocalPlayer))
        {
            if (b.State == StructureState.Complete) f.HubReady = true;
            else if (b.Assembling) f.AssembleSecondsLeft = Mathf.Max(f.AssembleSecondsLeft, PlanetSurface.AssembleSecondsLeft(b, _sim.Tick));
        }
        f.HubSelected = !_inOrbit && me.Phase == LandingPhase.Landed && _selectedBase != 0 && _selectedStructure == 0
            && _view.SelectedUnits.Count == 0 && _selectedTile < 0 && _placing == null && _founding == 0;

        f.Placed.Clear();
        foreach (var s in surface.Structures)
            if (s.Owner == LocalPlayer) f.Placed.Add(s.DefId);
        f.Available.Clear();
        foreach (var id in surface.BuildMenu)
            if (f.Names.ContainsKey(id)) f.Available.Add(id);

        f.RoverDriving = f.RoverSelected = f.CrawlerOrdered = f.CrawlerSelected = false;
        foreach (var u in surface.Units)
        {
            if (u.Owner != LocalPlayer) continue;
            var def = _data.SurfaceUnit(u.DefId);
            var selected = _view.SelectedUnits.Contains(u.Id);
            if (def.CanMine)
            {
                if (u.Mode is UnitMode.Deploying or UnitMode.Deployed || (u.DeployOnArrival && u.Driving)) f.CrawlerOrdered = true;
                if (selected) f.CrawlerSelected = true;
            }
            else if (!def.CanFound && def.SurveyRadius > 0)
            {
                // Driving to another tile (the last tile stays in Path while it parks, so a parking shuffle doesn't count).
                if (u.Path.Count > 0 && u.Path[^1] != u.Tile) f.RoverDriving = true;
                if (selected) f.RoverSelected = true;
            }
        }
        if (f.RoverSelected && f.CrawlerSelected) f.RoverSelected = f.CrawlerSelected = false; // the landing's whole group

        var surveyed = 0;
        var oreKnown = false;
        var map = surface.Map;
        var resources = _data.Resources;
        for (var t = 0; t < me.Surveyed.Length; t++)
        {
            if (!me.Surveyed[t]) continue;
            surveyed++;
            var r = map.DepositResource[t];
            if (!oreKnown && r >= 0 && r < resources.Count && resources[r].Id.Contains("ore") && !map.IsDepleted(t)) oreKnown = true;
        }
        f.Surveyed = surveyed;
        f.OreKnown = oreKnown;
        f.DrawerOpen = !_inOrbit && _drawer.IsOpen;
        f.Placing = _inOrbit ? null : _placing;
    }

    /// <summary>Where a card without an anchor sits: the helm's open viewscreen in orbit, between the top bar and the
    /// bottom panel on the surface.</summary>
    private Rect2? TutorialArea()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        if (_inOrbit) return _helm.OpenRect is { Size.X: > 64f, Size.Y: > 64f } open ? open : null;
        return new Rect2(0, PlanetHud.TopHeight, screen.X, Mathf.Max(64f, screen.Y - PlanetHud.TopHeight - PlanetHud.BottomHeight));
    }

    /// <summary>The screen rect the current step points at, or an empty rect (no arrow).</summary>
    private Rect2 TutorialAnchorRect(TutorialStepDef step)
    {
        var kind = step.Anchor;
        var surfaceAnchor = kind is TutorialAnchor.TopBar or TutorialAnchor.Build;
        if (surfaceAnchor == _inOrbit || ((kind is TutorialAnchor.HelmLand or TutorialAnchor.HelmScans) && (!_inOrbit || Me.Phase != LandingPhase.InOrbit)))
            return new Rect2();
        if (kind == TutorialAnchor.Build && (_placing != null || _founding != 0)) return new Rect2(); // the map is what matters now

        // Look the control up again when the step changes, a few times a second (pills are rebuilt), or when it went away.
        var refresh = kind != _tutorialAnchorKind || _tutorialFactsAt == _clock || _tutorialAnchorControl == null
            || !GodotObject.IsInstanceValid(_tutorialAnchorControl);
        if (refresh)
        {
            _tutorialAnchorKind = kind;
            _tutorialAnchorControl = null;
            _tutorialAnchorExtra = new Rect2();
            switch (kind)
            {
                case TutorialAnchor.HelmLand:
                    _tutorialAnchorControl = HelmPills(id => id == "orbit:land").FirstOrDefault();
                    break;
                case TutorialAnchor.HelmScans:
                    var pills = HelmPills(id => id.StartsWith("scan:")).ToList();
                    _tutorialAnchorControl = pills.FirstOrDefault();
                    foreach (var p in pills) _tutorialAnchorExtra = _tutorialAnchorExtra.Size == Vector2.Zero ? p.GetGlobalRect() : _tutorialAnchorExtra.Merge(p.GetGlobalRect());
                    break;
                case TutorialAnchor.TopBar:
                    _tutorialAnchorControl = HudTopBarField?.GetValue(_hud) as Control;
                    break;
                case TutorialAnchor.Build:
                    _tutorialAnchorControl = (_drawer.IsOpen && step.Building != null ? DrawerTile(step.Building) : null)
                        ?? (_drawer.IsOpen ? _drawer : HudBuildButtonField?.GetValue(_hud) as Control);
                    break;
            }
        }
        var c = _tutorialAnchorControl;
        if (c == null || !GodotObject.IsInstanceValid(c) || !c.IsVisibleInTree()) return new Rect2();
        if (kind == TutorialAnchor.HelmScans && _tutorialAnchorExtra.Size.X > 1f) return _tutorialAnchorExtra;
        var rect = c.GetGlobalRect();
        return rect.Size.X > 1f && rect.Size.Y > 1f ? rect : new Rect2();
    }

    /// <summary>The helm's pills (rail and action strip) whose id matches.</summary>
    private IEnumerable<HelmPill> HelmPills(System.Func<string, bool> match)
    {
        foreach (var node in _helm.FindChildren("*", "", true, false))
            if (node is HelmPill p && p.IsVisibleInTree() && match(p.Id)) yield return p;
    }

    /// <summary>The build drawer's tile button for a building (BuildDrawer._tiles: Data.Id, Button), or null.</summary>
    private Control? DrawerTile(string buildingId)
    {
        if (DrawerTilesField?.GetValue(_drawer) is not System.Collections.IEnumerable tiles) return null;
        foreach (var tile in tiles)
        {
            if (tile == null) continue;
            var type = tile.GetType();
            if (type.GetField("Data")?.GetValue(tile) is DrawerTile data && data.Id == buildingId)
                return type.GetField("Button")?.GetValue(tile) as Control;
        }
        return null;
    }
}
