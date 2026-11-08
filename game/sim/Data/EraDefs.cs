using System.Collections.Generic;

namespace FinalFrontier.Sim.Data;

// Read-only design data the game loads and checks but does not play yet (2026-11-07): eras.json and tech_tree.json.
// Nothing in the sim reads these: the surface research (surfaces.json "research") and the tech tree's tiers are
// separate systems until the owner decides how they meet, and a match's era is still orbit.era / maps.json "era".

/// <summary>
/// A starting era (eras.json): years, stardate style, the highest tech tier it allows, which factions can be played
/// (<see cref="Playable"/>) or only appear as AI (<see cref="AiOnly"/>), and its event ids (free-form flavour ids,
/// hazards.json ids where one exists).
/// </summary>
public sealed record EraDef(
    string Id,
    string Name,
    int YearFrom,
    int YearTo,
    string StardateFormat,
    int TechCapTier,
    bool Optional,
    IReadOnlyList<string> Playable,
    IReadOnlyList<string> AiOnly,
    IReadOnlyList<string> Events);

/// <summary>A tech of the strategic tech tree (tech_tree.json techs; not a surface tech, see <see cref="TechDef"/>).</summary>
public sealed record TreeTechDef(
    string Id,
    string Name,
    string Tree,
    int Tier,
    IReadOnlyList<string> Prereqs,
    IReadOnlyList<string> Unlocks,
    IReadOnlyList<string> FactionOnly,
    string RequiresResource,
    bool Forbidden);

/// <summary>
/// A faction that gets techs another way (tech_tree.json faction_rules): the Borg never research (<see cref="Research"/>
/// false); they assimilate techs and combine them for <see cref="CombinePerTier"/> × tier of <see cref="CombineResource"/>.
/// </summary>
public sealed record FactionTechRule(string Faction, bool Research, string CombineResource, int CombinePerTier);

/// <summary>The strategic tech tree (tech_tree.json): its trees, techs in file order, starting and assimilated modules and faction rules.</summary>
public sealed record TechTreeDef(
    IReadOnlyList<string> Trees,
    IReadOnlyList<TreeTechDef> Techs,
    IReadOnlyList<string> StartingModules,
    IReadOnlyList<string> AssimilatedModules,
    IReadOnlyList<FactionTechRule> FactionRules);
