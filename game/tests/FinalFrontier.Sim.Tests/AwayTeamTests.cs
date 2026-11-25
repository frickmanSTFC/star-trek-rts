using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>The away-team event (docs/space-jobs-contract.md): three timed steps, the first choice when a step runs out,
/// one outcome roll on sim.Rng when the team is back, then each member; leaving recalls the team.</summary>
public class AwayTeamTests
{
    private static (Simulation Sim, SpaceSite Hulk) AtHulk(string ship = "tereshkova")
    {
        var sim = Game(ship, SeedWithEveryKind(Campaign));
        var hulk = First(sim, SiteKind.Derelict)!;
        Identify(sim, hulk.Id);
        Work(sim, hulk.Id, "alongside", () => sim.System!.SiteWork(0, hulk.Id)!.Alongside);
        return (sim, hulk);
    }

    [Fact]
    public void ABoardingRunsThreeStepsThenRollsAndSpendsTheSite()
    {
        var (sim, hulk) = AtHulk();
        var sys = sim.System!;
        var team = Team(sim);
        Assert.InRange(team.Sum(), Campaign.ShipStatus!.AwayMin, sys.MaxAwayTeam(0));
        Assert.Equal(team.Sum(), sys.AwayTeamPreview(0, hulk.Id, team).Count);
        Assert.Equal(sys.AwayTeamPreview(0, hulk.Id, team).Select(m => m.Name), sys.AwayTeamPreview(0, hulk.Id, team).Select(m => m.Name)); // deterministic
        Do(sim, new AwayTeamCommand(0, Ship(sim).Id, hulk.Id, team));
        var view = sys.AwayTeamOf(0)!;
        Assert.Equal((0, 3, false), (view.Step, view.Steps, view.Done));
        Assert.Equal(sys.AwayTeamPreview(0, hulk.Id, team).Count, view.Team.Count);
        Assert.Equal(3, view.Choices.Count);
        var loss = view.LossPct;
        Do(sim, new AwayTeamChoiceCommand(0, hulk.Id, 1)); // cut through the hull: riskier
        Assert.True(sys.AwayTeamOf(0)!.LossPct >= loss);
        Assert.Equal(new[] { 1, -1, -1 }, sys.AwayTeamOf(0)!.Picked);
        Assert.EndsWith("an away team is out already", Refused(sim, new AwayTeamCommand(0, Ship(sim).Id, hulk.Id, team)));
        // Let the second step run out: it takes its first choice.
        Seconds(sim, Campaign.ShipStatus.StepSeconds + 1);
        Assert.Equal(0, sys.AwayTeamOf(0)!.Picked[1]);
        Do(sim, new AwayTeamChoiceCommand(0, hulk.Id, 0));
        Assert.Equal("Coming back aboard", sys.AwayTeamOf(0)!.StepTitle);
        Assert.EndsWith("the team is coming back aboard", Refused(sim, new AwayTeamChoiceCommand(0, hulk.Id, 0)));
        Until(sim, () => sys.AwayTeamOf(0) is { Done: true }, 200, "back aboard");
        var done = sys.AwayTeamOf(0)!;
        Assert.NotNull(done.Outcome);
        Assert.NotEmpty(done.Result!);
        Assert.True(sys.SiteWork(0, hulk.Id)!.Boarded);
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, hulk.Id));
        Assert.Contains(sys.StatusOf(0)!.Log, l => l.Text.StartsWith("Away team back from"));
        Seconds(sim, StarSystemState.AwayResultSeconds + 1);
        Assert.Null(sys.AwayTeamOf(0));
    }

    [Fact]
    public void LeavingTheSiteRecallsTheTeam()
    {
        var (sim, hulk) = AtHulk();
        var sys = sim.System!;
        Do(sim, new AwayTeamCommand(0, Ship(sim).Id, hulk.Id, Team(sim)));
        var crew = sys.StatusOf(0)!.CrewTotal;
        Do(sim, new SiteCourseCommand(0, Ship(sim).Id, First(sim, SiteKind.Station)!.Id));
        var view = sys.AwayTeamOf(0)!;
        Assert.True(view.Done);
        Assert.Equal("recalled", view.Outcome);
        Assert.True(sys.SiteWork(0, hulk.Id)!.Recalled);
        Assert.False(sys.SiteWork(0, hulk.Id)!.Boarded);
        Assert.Equal(crew, sys.StatusOf(0)!.CrewTotal); // no roll, no losses
        Assert.True(sys.AwayRecalledSince(0, hulk.Id, 0));
    }

    [Fact]
    public void TeamsAreLimitedByCrewAndShip()
    {
        var (sim, hulk) = AtHulk();
        var sys = sim.System!;
        var max = sys.MaxAwayTeam(0);
        Assert.InRange(max, 2, Campaign.ShipStatus!.AwayMax);
        Assert.Contains("at least", sys.AwayTeamProblem(0, hulk.Id, new[] { 1, 0, 0, 0 }));
        Assert.Contains("at most", sys.AwayTeamProblem(0, hulk.Id, new[] { max, 1, 0, 0 }) ?? sys.AwayTeamProblem(0, hulk.Id, new[] { max + 1, 0, 0, 0 }));
        Assert.Contains("one number", sys.AwayTeamProblem(0, hulk.Id, new[] { 1, 1 }));
        // The Armstrong (6 crew) sends 2 at most.
        var armstrong = Game("armstrong");
        Assert.Equal(2, armstrong.System!.MaxAwayTeam(0));
    }

    [Fact]
    public void OutcomesFollowTheTable()
    {
        // Forced outcomes (test panel): the surge damages the hull and knocks out the sensors; quarantine puts the team out.
        var (sim, hulk) = AtHulk();
        var sys = sim.System!;
        var table = Campaign.ShipStatus!.Table("derelict")!;
        Do(sim, new AwayTeamCommand(0, Ship(sim).Id, hulk.Id, Team(sim)));
        var hull = sys.StatusOf(0)!.Hull;
        Debug(sim, DebugSpaceField.FinishAwayTeam, null, table.Outcomes.ToList().FindIndex(o => o.Id == "surge"));
        Assert.Equal("surge", sys.AwayTeamOf(0)!.Outcome);
        Assert.True(sys.StatusOf(0)!.Hull < hull);
        Assert.Contains(sys.StatusOf(0)!.Down, d => d.System == ShipSystem.Sensors);
        Assert.True(sys.StatusOf(0)!.Crew.Sum(c => c.Injured + c.Lost) >= 1);

        var (q, qhulk) = AtHulk();
        var team = Team(q);
        Do(q, new AwayTeamCommand(0, Ship(q).Id, qhulk.Id, team));
        Debug(q, DebugSpaceField.FinishAwayTeam, null, table.Outcomes.ToList().FindIndex(o => o.Id == "quarantine"));
        Assert.Equal(team.Sum(), q.System!.StatusOf(0)!.Crew.Sum(c => c.Injured + c.Lost));
    }

    [Fact]
    public void BoardingsAreDeterministic()
    {
        (ulong, string) Run()
        {
            var (sim, hulk) = AtHulk();
            var done = Board(sim, hulk.Id, 2);
            Seconds(sim, 5);
            return (sim.StateHash(), done.Result!);
        }
        Assert.Equal(Run(), Run());
    }
}
