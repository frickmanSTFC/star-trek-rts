using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Missions (data/missions/, docs/missions-contract.md C): the definitions as read from the files. The engine is
// game/sim/Missions/ (MissionBook.cs steps them, Conditions.cs evaluates the conditions, MissionActions.cs runs the
// actions); the loader and the checks are GameData.Missions.cs.

/// <summary>
/// One condition: Kind ("surveyed", "site", "all", …), its named fields (strings and numbers, as their raw JSON text),
/// its list fields (e.g. "ids", "kinds") and, for all / any / not, the nested conditions.
/// </summary>
public sealed record MissionCondDef(string Kind, IReadOnlyDictionary<string, string> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Lists, IReadOnlyList<MissionCondDef> Children)
{
    public string? F(string field) => Fields.TryGetValue(field, out var v) ? v : null;

    public IReadOnlyList<string> L(string field) => Lists.TryGetValue(field, out var v) ? v : Array.Empty<string>();

    /// <summary>A number field (parsed from its raw text), or the fallback.</summary>
    public Fix64 N(string field, Fix64 fallback) => Fields.TryGetValue(field, out var v) && GameData.TryFix(v, out var n) ? n : fallback;

    public int I(string field, int fallback) => Fields.TryGetValue(field, out var v) && GameData.TryFix(v, out var n) ? n.FloorToInt() : fallback;
}

/// <summary>One action: Kind ("message", "grant", "place_site", …), its fields and lists, and for grant the amounts.</summary>
public sealed record MissionActionDef(string Kind, IReadOnlyDictionary<string, string> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Lists, IReadOnlyList<(string Resource, Fix64 Amount)> Amounts)
{
    public string? F(string field) => Fields.TryGetValue(field, out var v) ? v : null;

    public IReadOnlyList<string> L(string field) => Lists.TryGetValue(field, out var v) ? v : Array.Empty<string>();

    public Fix64 N(string field, Fix64 fallback) => Fields.TryGetValue(field, out var v) && GameData.TryFix(v, out var n) ? n : fallback;

    public int I(string field, int fallback) => Fields.TryGetValue(field, out var v) && GameData.TryFix(v, out var n) ? n.FloorToInt() : fallback;
}

/// <summary>An objective of a stage: done when Cond is met (a counter shows Progress / Target); with HoldSeconds &gt; 0
/// it must stay met that long (progress = seconds held, back to 0 when it breaks). Optional = not needed to finish the
/// stage. Hint = a line for the tracker.</summary>
public sealed record MissionObjectiveDef(string Id, string Text, MissionCondDef Cond, bool Optional, int HoldSeconds, string Hint);

/// <summary>A branch button on a stage: picking it (MissionChoiceCommand) goes to Next (a stage id, "complete" or "fail").</summary>
public sealed record MissionChoiceDef(string Id, string Text, string Next);

/// <summary>A stage: OnEnter runs once when it starts; it finishes when every required objective is done (or the
/// player picks one of Choices) and goes to Next ("complete", "fail" or a stage id). TimerSeconds &gt; 0: when it runs
/// out, OnTimeout ("fail" by default, or a stage id / "complete") happens.</summary>
public sealed record MissionStageDef(string Id, string Title, IReadOnlyList<MissionActionDef> OnEnter,
    IReadOnlyList<MissionObjectiveDef> Objectives, IReadOnlyList<MissionChoiceDef> Choices, string Next, int TimerSeconds,
    string OnTimeout, string Hint);

/// <summary>
/// One mission (data/missions/&lt;file&gt;.json "missions"). Chain = the chain id it belongs to (rules.json chains);
/// Kind main (story: starts by itself once the previous main mission of the chain is done and Trigger holds) or side
/// (offered when Trigger holds; accept / decline). Needs = data ids the mission can't run without (a data set that
/// lacks one leaves the mission out: GameData.MissionAvailable). OfferSeconds 0 = the rules' default.
/// </summary>
public sealed record MissionDef(string Id, string Chain, string Kind, string Title, string Giver, string Briefing, string Debrief,
    MissionCondDef? Trigger, IReadOnlyList<MissionStageDef> Stages, IReadOnlyList<MissionActionDef> Rewards,
    IReadOnlyList<string> Needs, int OfferSeconds)
{
    public bool IsMain => Kind == "main";

    public int StageIndex(string id)
    {
        for (var i = 0; i < Stages.Count; i++)
            if (Stages[i].Id == id) return i;
        return -1;
    }
}

/// <summary>A voice that sends missions (rules.json givers): id ("control"), Name ("Flight Director Moreau"), Role
/// ("Earth Mission Control"), Initials for the portrait placeholder.</summary>
public sealed record MissionGiverDef(string Id, string Name, string Role, string Initials);

/// <summary>A mission chain (rules.json chains): Id ("first_light"), Name ("First Light"), the era it belongs to.</summary>
public sealed record MissionChainDef(string Id, string Name, string Era);

/// <summary>rules.json: MaxSideActive 2 side jobs at once, OfferSeconds 90 for an offer to stand, MessageLog lines kept
/// per player, RichDeposit = a deposit at least this big counts as rich (deposits_found rich), SiteKinds the view draws.</summary>
public sealed record MissionRules(int MaxSideActive, int OfferSeconds, int MessageLog, Fix64 RichDeposit, IReadOnlyList<string> SiteKinds);

/// <summary>Everything in data/missions/.</summary>
public sealed record MissionData(MissionRules Rules, IReadOnlyList<MissionGiverDef> Givers, IReadOnlyList<MissionChainDef> Chains,
    IReadOnlyList<MissionDef> List)
{
    public MissionDef? Find(string id)
    {
        foreach (var m in List)
            if (m.Id == id) return m;
        return null;
    }

    public MissionGiverDef? Giver(string id)
    {
        foreach (var g in Givers)
            if (g.Id == id) return g;
        return null;
    }

    public MissionChainDef? Chain(string id)
    {
        foreach (var c in Chains)
            if (c.Id == id) return c;
        return null;
    }
}
