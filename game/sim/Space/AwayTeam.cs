using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// CONTRACT STUB (2026-11-25, docs/space-jobs-contract.md): the away-team event, shaped like first contact (three timed
// steps, the game never pauses, a step that runs out takes its first choice). Stubs: no events.

/// <summary>One choice at an away-team step: Label, Hint, and how it moves the odds (percentage points per person for
/// injury and loss; find = chance of a good find).</summary>
public sealed record AwayChoice(string Label, string Hint, int Injury, int Loss, int Find, int Seconds);

/// <summary>A member of the team: Name ("Lt. Okonjo"), Role ("pilot"), Division id.</summary>
public sealed record AwayMember(string Name, string Role, string Division);

/// <summary>A running (or just finished) away-team event. Step 0-based; Picked = choice per finished step (-1 none);
/// odds now in percent; Outcome/Result once done.</summary>
public sealed record AwayTeamView(string SiteId, int Player, int Step, int Steps, string StepTitle, IReadOnlyList<AwayChoice> Choices,
    IReadOnlyList<int> Picked, long StepStartTick, long StepEndTick, IReadOnlyList<AwayMember> Team, int InjuryPct, int LossPct,
    int FindPct, bool Done, string? Outcome, string? Result);

public sealed partial class StarSystemState
{
    /// <summary>The player's running away-team event (or the last one for a short while after it ends), or null.</summary>
    public AwayTeamView? AwayTeamOf(int player) => null;

    /// <summary>Why an away team can't go to the site now (or with that crew), or null.</summary>
    public string? AwayTeamProblem(int player, string siteId, IReadOnlyList<int>? crew) => "not built yet";

    /// <summary>Why the choice can't be picked now, or null.</summary>
    public string? AwayChoiceProblem(int player, string siteId, int choice) => "not built yet";

    /// <summary>The largest team the ship can send (half its fit crew, at most 6; the Armstrong 2).</summary>
    public int MaxAwayTeam(int player) => 0;
}
