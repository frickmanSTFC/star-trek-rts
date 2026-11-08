using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

/// <summary>One earned achievement in the profile: when (local date), on which world and seed, at what game time.</summary>
public sealed record EarnedAchievement(string Id, string Date, string World, ulong Seed, double GameSeconds);

/// <summary>
/// The commander profile (docs/achievements-contract.md, "View → UI agent"): one per machine, kept in
/// user://achievements.cfg, never reset by the game (owner 2026-11-08: no reset button). Holds every earned achievement
/// (id → date, world, seed, game seconds) and the HANGAR picks per cosmetic kind (one id or none; rover names and hull
/// numbers: a list). Unlocked cosmetics are not stored: they are the rewards of the earned achievements (data).
/// Static, so it outlives the scene reload that starts a new game.
/// </summary>
public static class AchievementProfile
{
    private const string File = "user://achievements.cfg";
    private const string EarnedPrefix = "earned:";
    private const string PicksSection = "picks";

    /// <summary>The cosmetic kinds in HANGAR order; the last two are multi-pick and go to the sim at game start.</summary>
    public static readonly string[] Kinds = { "paint", "finish", "dropship_name", "beam_colour", "dome_tint", "rover_name", "hull_number" };

    /// <summary>Kinds where several can be picked (handed out in the order picked).</summary>
    public static bool IsMulti(string kind) => kind is "rover_name" or "hull_number";

    /// <summary>Kinds the sim holds (names and numbers are game state): they apply to the next new game.</summary>
    public static bool NextGameOnly(string kind) => IsMulti(kind);

    private static bool _loaded;
    private static readonly Dictionary<string, EarnedAchievement> _earned = new();
    private static readonly Dictionary<string, List<string>> _picks = new();

    /// <summary>Goes up on every change (earned or picks), so screens know to refresh.</summary>
    public static int Version { get; private set; }

    public static IReadOnlyDictionary<string, EarnedAchievement> Earned
    {
        get
        {
            Load();
            return _earned;
        }
    }

    public static bool IsEarned(string id)
    {
        Load();
        return _earned.ContainsKey(id);
    }

    public static EarnedAchievement? EarnedOf(string id)
    {
        Load();
        return _earned.GetValueOrDefault(id);
    }

    /// <summary>Records an achievement as earned now (local date). False if it already was (nothing changes).</summary>
    public static bool Record(string id, string world, ulong seed, double gameSeconds)
    {
        Load();
        if (_earned.ContainsKey(id)) return false;
        var date = DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        _earned[id] = new EarnedAchievement(id, date, world, seed, gameSeconds);
        Version++;
        Save();
        return true;
    }

    /// <summary>The cosmetics the earned achievements unlock (their rewards), in the data's cosmetics order.</summary>
    public static List<CosmeticDef> Unlocked(AchievementData? data)
    {
        var list = new List<CosmeticDef>();
        if (data == null) return list;
        var rewards = new HashSet<string>();
        foreach (var a in data.List)
            if (a.Reward.Length > 0 && IsEarned(a.Id)) rewards.Add(a.Reward);
        foreach (var c in data.Cosmetics)
            if (rewards.Contains(c.Id)) list.Add(c);
        return list;
    }

    /// <summary>Whether a cosmetic is unlocked (some earned achievement rewards it).</summary>
    public static bool IsUnlocked(AchievementData? data, string cosmeticId)
    {
        if (data == null) return false;
        foreach (var a in data.List)
            if (a.Reward == cosmeticId && IsEarned(a.Id)) return true;
        return false;
    }

    /// <summary>The picks of a kind, in the order picked (single kinds: at most one). May hold ids no longer unlocked
    /// or in the data; <see cref="ActivePicks"/> filters those.</summary>
    public static IReadOnlyList<string> Picks(string kind)
    {
        Load();
        return _picks.TryGetValue(kind, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <summary>The picks of a kind that are unlocked and of that kind in the data.</summary>
    public static List<CosmeticDef> ActivePicks(AchievementData? data, string kind)
    {
        var list = new List<CosmeticDef>();
        if (data == null) return list;
        foreach (var id in Picks(kind))
            if (data.Cosmetic(id) is { } c && c.Kind == kind && IsUnlocked(data, id)) list.Add(c);
        return list;
    }

    /// <summary>Single kinds: picks this cosmetic (null = Standard). Multi kinds: null clears the list.</summary>
    public static void SetPick(string kind, string? cosmeticId)
    {
        Load();
        if (cosmeticId == null) _picks.Remove(kind);
        else _picks[kind] = new List<string> { cosmeticId };
        Version++;
        Save();
    }

    /// <summary>Multi kinds: adds the cosmetic to the end of the list, or takes it out if it is there.</summary>
    public static void TogglePick(string kind, string cosmeticId)
    {
        Load();
        if (!_picks.TryGetValue(kind, out var list)) _picks[kind] = list = new List<string>();
        if (!list.Remove(cosmeticId)) list.Add(cosmeticId);
        if (list.Count == 0) _picks.Remove(kind);
        Version++;
        Save();
    }

    public static bool IsPicked(string kind, string cosmeticId) => Picks(kind).Contains(cosmeticId);

    /// <summary>Sets the render-side picks (<see cref="Cosmetics"/>) from the profile; Standard where nothing is picked.</summary>
    public static void ApplyCosmetics(AchievementData? data)
    {
        Cosmetics.Paint = Cosmetics.Paint2 = Cosmetics.BeamColour = Cosmetics.DomeTint = null;
        Cosmetics.Pattern = Cosmetics.Finish = Cosmetics.DropshipName = null;
        if (ActivePicks(data, "paint").FirstOrDefault() is { } paint)
        {
            Cosmetics.Paint = ParseColour(paint.Value);
            Cosmetics.Paint2 = ParseColour(paint.Colour2);
            Cosmetics.Pattern = paint.Pattern.Length > 0 ? paint.Pattern : "solid";
        }
        if (ActivePicks(data, "finish").FirstOrDefault() is { } finish) Cosmetics.Finish = finish.Value;
        if (ActivePicks(data, "dropship_name").FirstOrDefault() is { } ship) Cosmetics.DropshipName = ship.Value;
        if (ActivePicks(data, "beam_colour").FirstOrDefault() is { } beam) Cosmetics.BeamColour = ParseColour(beam.Value);
        if (ActivePicks(data, "dome_tint").FirstOrDefault() is { } dome) Cosmetics.DomeTint = ParseColour(dome.Value);
        Cosmetics.Version++;
    }

    /// <summary>The sim-side picks for a new game: rover names and hull numbers, in the order picked.</summary>
    public static PlayerCosmetics GameCosmetics(AchievementData? data)
    {
        var names = ActivePicks(data, "rover_name").Select(c => c.Value.Trim()).Where(v => v.Length > 0).ToList();
        var numbers = new List<int>();
        foreach (var c in ActivePicks(data, "hull_number"))
            if (int.TryParse(c.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0) numbers.Add(n);
        return names.Count == 0 && numbers.Count == 0 ? PlayerCosmetics.None : new PlayerCosmetics(names, numbers);
    }

    /// <summary>"#rrggbb" → a colour; null for "" or anything that doesn't parse.</summary>
    public static Color? ParseColour(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return null;
        if (!t.StartsWith('#')) t = "#" + t;
        return Color.HtmlIsValid(t) ? Color.FromHtml(t) : null;
    }

    /// <summary>Game time as "m:ss" or "h:mm:ss".</summary>
    public static string GameTime(double seconds)
    {
        var s = Math.Max(0, (long)seconds);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    // ---------------------------------------------------------------- file

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        foreach (var section in cfg.GetSections())
        {
            if (section.StartsWith(EarnedPrefix, StringComparison.Ordinal))
            {
                var id = section[EarnedPrefix.Length..];
                if (id.Length == 0) continue;
                var date = cfg.GetValue(section, "date", "").AsString();
                var world = cfg.GetValue(section, "world", "").AsString();
                ulong.TryParse(cfg.GetValue(section, "seed", "0").AsString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed);
                double.TryParse(cfg.GetValue(section, "seconds", "0").AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
                _earned[id] = new EarnedAchievement(id, date, world, seed, seconds);
            }
        }
        if (cfg.HasSection(PicksSection))
            foreach (var kind in cfg.GetSectionKeys(PicksSection))
            {
                var ids = cfg.GetValue(PicksSection, kind, "").AsString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (ids.Count > 0) _picks[kind] = IsMulti(kind) ? ids : new List<string> { ids[0] };
            }
    }

    private static void Save()
    {
        // Written whole each time (small): the earned list, then the picks. Values as strings so seeds and seconds keep
        // their exact text whatever ConfigFile does with numbers.
        var cfg = new ConfigFile();
        foreach (var e in _earned.Values.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var section = EarnedPrefix + e.Id;
            cfg.SetValue(section, "date", e.Date);
            cfg.SetValue(section, "world", e.World);
            cfg.SetValue(section, "seed", e.Seed.ToString(CultureInfo.InvariantCulture));
            cfg.SetValue(section, "seconds", e.GameSeconds.ToString("0.#", CultureInfo.InvariantCulture));
        }
        foreach (var (kind, ids) in _picks)
            if (ids.Count > 0) cfg.SetValue(PicksSection, kind, string.Join(",", ids));
        var err = cfg.Save(File);
        if (err != Error.Ok) GD.PushWarning($"Achievements: couldn't save {File} ({err})");
    }
}
