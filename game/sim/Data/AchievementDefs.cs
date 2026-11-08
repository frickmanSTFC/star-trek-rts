using System.Collections.Generic;

namespace FinalFrontier.Sim.Data;

// CONTRACT (2026-11-08, achievements; docs/achievements-contract.md). The sim agent reads data/achievements/ into these.

/// <summary>data/achievements/rules.json categories.</summary>
public sealed record AchievementCategory(string Id, string Name, int Order, string Icon);

/// <summary>data/achievements/rules.json tiers (bronze, silver, gold).</summary>
public sealed record AchievementTier(string Id, string Name, string Colour, int Points);

/// <summary>data/achievements/rules.json cosmetics: what a reward id looks like.</summary>
/// <param name="Kind">rover_name, hull_number, paint, finish, dropship_name, beam_colour, dome_tint.</param>
/// <param name="Value">rover_name/dropship_name: the text; hull_number: the number as text; paint/beam_colour/
/// dome_tint: a "#rrggbb" colour; finish: a finish id the view knows (brushed_gold, chrome, blue_white, gunmetal,
/// pearl, rescue_white).</param>
/// <param name="Colour2">paint: a second colour for patterns ("#rrggbb" or ""); Pattern: solid, band, chevrons, stripes.</param>
public sealed record CosmeticDef(string Id, string Kind, string Name, string Value, string Colour2, string Pattern);

/// <summary>One achievement (data/achievements/&lt;category&gt;.json).</summary>
/// <param name="Reward">A cosmetic id from rules.json cosmetics.</param>
/// <param name="Trigger">The rule, kept as parsed JSON-ish fields (the tracker reads it); Kind = milestone, built,
/// count, share, unit_counter, event, streak, set, view_count, all_of, any_of.</param>
public sealed record AchievementDef(string Id, string Name, string Description, string Category, string Tier, string Icon,
    bool Hidden, bool Progress, string Reward, AchievementTrigger Trigger, AchievementWorlds? Worlds);

/// <summary>A trigger: its kind plus named fields (strings and numbers) and sub-rules for all_of / any_of.</summary>
public sealed record AchievementTrigger(string Kind, IReadOnlyDictionary<string, string> Fields, IReadOnlyList<string> List,
    IReadOnlyList<AchievementTrigger> Rules);

/// <summary>Optional world filter: planet classes, gravity range, habitability tier.</summary>
public sealed record AchievementWorlds(IReadOnlyList<string> Classes, decimal? MinGravityG, decimal? MaxGravityG, string? Tier);

/// <summary>Everything in data/achievements/.</summary>
public sealed record AchievementData(IReadOnlyList<AchievementCategory> Categories, IReadOnlyList<AchievementTier> Tiers,
    IReadOnlyList<CosmeticDef> Cosmetics, IReadOnlyList<AchievementDef> List)
{
    public AchievementDef? Find(string id)
    {
        foreach (var a in List)
            if (a.Id == id) return a;
        return null;
    }

    public CosmeticDef? Cosmetic(string id)
    {
        foreach (var c in Cosmetics)
            if (c.Id == id) return c;
        return null;
    }

    public AchievementTier? Tier(string id)
    {
        foreach (var t in Tiers)
            if (t.Id == id) return t;
        return null;
    }
}

public sealed partial class GameData
{
    /// <summary>data/achievements/, or null when the document is missing. STUB until the sim agent loads it.</summary>
    public AchievementData? Achievements { get; private set; }
}
