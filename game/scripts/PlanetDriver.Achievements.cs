using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

// Achievements, view side (docs/achievements-contract.md, "View → UI agent"): the sim records unlocks
// (Simulation.Achievements); here new ones of the local player go into the machine's profile (AchievementProfile) with a
// toast (AchievementToasts) and an event line; the view-only rule ships_inspected (system ship cards opened) is checked
// here; the HANGAR picks set Cosmetics (render) and, at game start, the sim's rover names / hull numbers. F3 / the
// trophy chip / a toast / the world chooser open the screen (AchievementScreen). Draft data turns unlocks off.
public partial class PlanetDriver
{
    private AchievementToasts _toasts = null!;
    private AchievementScreen _achScreen = null!;
    private Button _trophyPill = null!;
    private int _unlocksSeen;
    private string? _achievementsOff; // why this game doesn't count, when the view turned it off (draft data)
    private readonly HashSet<int> _shipsInspected = new(); // view counter, this game only
    private int _earnedShown = -1;
    private int _earnedVersion = -1;
    private string _trophyText = "";
    private string? _trophyGate;

    private AchievementData? Achievements => _sim.Data.Achievements;

    /// <summary>Right after the match is made, before the first Step: draft data turns unlocks off; the HANGAR's rover
    /// names and hull numbers go to the sim; the render picks are set.</summary>
    private void BeginAchievements()
    {
        if (!GameDataLoader.Official)
        {
            _sim.AchievementsEnabled = false;
            _achievementsOff = "the game data is not the base data (a draft, or a fallback after refused data)";
        }
        _sim.SetCosmetics(LocalPlayer, AchievementProfile.GameCosmetics(Achievements));
        AchievementProfile.ApplyCosmetics(Achievements);
    }

    /// <summary>The screen (layer 4, over the world chooser), the orbit trophy pill, and the toasts (layer 5, on top).</summary>
    private void SetupAchievements()
    {
        var layer = new CanvasLayer { Name = "AchievementLayer", Layer = 4 };
        AddChild(layer);
        _trophyPill = new Button { Name = "TrophyPill", FocusMode = Control.FocusModeEnum.None, Visible = false, TooltipText = "Achievements (F3)" };
        HudKit.StyleTile(_trophyPill, 6, 6);
        _trophyPill.AddThemeFontOverride("font", HudKit.Condensed(true));
        _trophyPill.Pressed += OpenAchievements;
        layer.AddChild(_trophyPill);
        _achScreen = new AchievementScreen { Name = "AchievementScreen" };
        layer.AddChild(_achScreen);
        _achScreen.ProgressOf = AchievementProgressOf;
        _achScreen.CosmeticsChanged += () => AchievementProfile.ApplyCosmetics(Achievements); // Cosmetics.Version++ inside

        var toastLayer = new CanvasLayer { Name = "AchievementToastLayer", Layer = 5 };
        AddChild(toastLayer);
        _toasts = new AchievementToasts { Name = "AchievementToasts" };
        toastLayer.AddChild(_toasts);
        _toasts.Clicked += _ => OpenAchievements();

        _hud.TrophyPressed += OpenAchievements;
        _chooser.AchievementsRequested += OpenAchievements;
    }

    private void OpenAchievements()
    {
        _achScreen.Data = Achievements;
        _achScreen.Gate = AchievementGate();
        _achScreen.Open();
    }

    private void ToggleAchievements()
    {
        if (_achScreen.IsOpen) _achScreen.Close();
        else OpenAchievements();
    }

    /// <summary>Why unlocks don't count in this game, or null.</summary>
    private string? AchievementGate() => _achievementsOff ?? _sim.Achievements.Gate;

    private bool AchievementsBlockMouse(Vector2 mouse) =>
        _achScreen.IsOpen || _toasts.IsOverUi(mouse) || (_trophyPill.Visible && _trophyPill.GetGlobalRect().HasPoint(mouse));

    /// <summary>F3 toggles the screen; while it is open Esc closes it and other keys don't reach the game. True = used.</summary>
    private bool AchievementInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            ToggleAchievements();
            return true;
        }
        if (!_achScreen.IsOpen || e is not InputEventKey key) return false;
        if (key is { Pressed: true, Keycode: Key.Escape }) _achScreen.Close();
        return true;
    }

    /// <summary>Every frame: new unlocks, the view-only rules, the toast's place and hold, the trophy chip and pill.</summary>
    private void UpdateAchievements()
    {
        var data = Achievements;
        var unlocks = _sim.Achievements.Unlocks;
        for (var i = _unlocksSeen; i < unlocks.Count; i++)
            if (unlocks[i].Player == LocalPlayer) Earn(unlocks[i].Id, unlocks[i].Tick);
        _unlocksSeen = unlocks.Count;

        // the toasts wait while F1 help, a large card or the world chooser is up; on the surface they sit under the top
        // bar (clear of the hints line), in orbit under the helm title
        _toasts.Waiting = _hud.HelpVisible || _bigCard.IsOpen || _chooser.IsOpen;
        var screen = GetViewport().GetVisibleRect().Size;
        var scale = HudKit.ScaleFor(screen);
        if (_inOrbit && _helm.ViewRect.Size.X > 32f)
        {
            var open = _helm.OpenRect;
            _toasts.TopY = _helm.ViewRect.Position.Y + 14f * scale;
            _toasts.CentreX = open.Size.X > 300f ? open.Position.X + open.Size.X / 2f : -1f;
        }
        else
        {
            _toasts.TopY = PlanetHud.TopHeight + 38f;
            _toasts.CentreX = -1f;
        }

        if (_achScreen.IsOpen)
        {
            _achScreen.Data = data;
            _achScreen.Gate = AchievementGate();
        }

        // trophy chip (top bar) and, in orbit where the top bar is hidden, a pill beside the WORLD button
        if (data == null)
        {
            _hud.ShowTrophy(null, "");
            _trophyPill.Visible = false;
            return;
        }
        var gateNow = AchievementGate();
        if (_earnedVersion != AchievementProfile.Version || _earnedShown < 0 || gateNow != _trophyGate)
        {
            _earnedVersion = AchievementProfile.Version;
            _trophyGate = gateNow;
            var earned = data.List.Where(a => AchievementProfile.IsEarned(a.Id)).ToList();
            _earnedShown = earned.Count;
            _trophyText = $"{earned.Count} / {data.List.Count}";
            var points = earned.Sum(a => Medals.Points(data, a.Tier));
            var gate = AchievementGate();
            _hud.ShowTrophy(_trophyText, $"Achievements: {earned.Count} of {data.List.Count} earned, {points} points (F3)" +
                                         (gate != null ? $"\nNot counting in this game: {gate}" : ""));
            _trophyPill.Text = $"  {_trophyText}";
            _trophyPill.Icon = Medals.Line("trophy", HudKit.Ink, 32);
        }
        var world = _chooser.ButtonRect;
        var showPill = _inOrbit && world.Size.X > 0 && !_achScreen.IsOpen;
        if (_trophyPill.Visible != showPill) _trophyPill.Visible = showPill;
        if (showPill)
        {
            _trophyPill.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(15 * scale));
            _trophyPill.AddThemeConstantOverride("icon_max_width", Mathf.RoundToInt(16 * scale));
            _trophyPill.Size = new Vector2(0, world.Size.Y); // shrinks to its text, as tall as WORLD
            _trophyPill.Position = new Vector2(world.End.X + 8f * scale, world.Position.Y);
        }
    }

    /// <summary>An achievement the local player earned this game: into the profile (once ever), toast, event line.</summary>
    private void Earn(string id, long tick)
    {
        if (!AchievementProfile.Record(id, PlanetName, Seed, tick / (double)Simulation.TickRate)) return;
        var data = Achievements;
        var a = data?.Find(id);
        var name = a?.Name ?? id;
        var tier = a?.Tier ?? "bronze";
        var colour = Medals.TierColour(data, tier);
        _toasts.Push(new ToastView(id, name, a?.Description ?? "", tier, colour, a?.Icon ?? "hex", a?.Hidden ?? false,
            Medals.Points(data, tier), Medals.TierName(data, tier)));
        _hud.PushEvent($"Achievement: {name}", colour);
    }

    /// <summary>A ship card was opened on the system plot (PlanetDriver.System.cs SystemClick).</summary>
    private void NoteShipInspected(int shipId)
    {
        if (!_shipsInspected.Add(shipId)) return;
        CheckViewRules();
    }

    /// <summary>The view-only rules (trigger kind view_count): checked here, recorded straight into the profile. Only
    /// while unlocks count (no gate).</summary>
    private void CheckViewRules()
    {
        var data = Achievements;
        if (data == null || AchievementGate() != null) return;
        foreach (var a in data.List)
        {
            if (a.Trigger.Kind != "view_count" || AchievementProfile.IsEarned(a.Id)) continue;
            if (ViewProgress(a.Trigger) is { } p && p.Target > 0 && p.Current >= p.Target) Earn(a.Id, _sim.Tick);
        }
    }

    /// <summary>Progress of a view_count rule, or null when the view doesn't know its measure (or there's nothing to count).</summary>
    private AchievementProgress? ViewProgress(AchievementTrigger t)
    {
        var measure = t.Fields.GetValueOrDefault("measure", "");
        if (measure != "ships_inspected") return null;
        var neutral = _system.Ships.Where(s => s.IsNeutral).Select(s => s.Id).ToList();
        if (t.Fields.TryGetValue("at_least", out var atLeast) &&
            long.TryParse(atLeast, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            return new AchievementProgress(_shipsInspected.Count, n, "ships");
        // "all": every neutral ship of the current system
        if (neutral.Count == 0) return null;
        return new AchievementProgress(neutral.Count(_shipsInspected.Contains), neutral.Count, "ships");
    }

    /// <summary>The screen's progress bars: the view's own counters for view_count rules, else the sim's.</summary>
    private AchievementProgress? AchievementProgressOf(string id)
    {
        var a = Achievements?.Find(id);
        if (a?.Trigger.Kind == "view_count") return ViewProgress(a.Trigger);
        return _sim.Achievements.Progress(LocalPlayer, id);
    }
}
