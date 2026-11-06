using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Tests;

public class DeterminismTests
{
    /// <summary>Runs a scripted match and returns the state hash at every tick.</summary>
    private static List<ulong> RunScript(ulong seed, int ticks, Action<Simulation>? extraOrders = null)
    {
        var sim = MatchSetup.CreateMvpSkirmish(TestData.Real, seed);
        var fed = sim.Ships.Where(s => s.Owner == 0).Select(s => s.Id).ToArray();
        var card = sim.Ships.Where(s => s.Owner == 1).Select(s => s.Id).ToArray();
        sim.Schedule(3, new MoveCommand(0, fed, FixVec2.FromInts(50, 40)));
        sim.Schedule(3, new MoveCommand(1, card, FixVec2.FromInts(-60, -25)));
        sim.Schedule(90, new MoveCommand(0, fed[..2], FixVec2.FromInts(-120, 75)));
        extraOrders?.Invoke(sim);

        var hashes = new List<ulong>();
        for (var i = 0; i < ticks; i++)
        {
            sim.Step();
            hashes.Add(sim.StateHash());
        }
        return hashes;
    }

    [Fact]
    public void SameInputsGiveIdenticalStateEveryTick() =>
        Assert.Equal(RunScript(99, 600), RunScript(99, 600));

    [Fact]
    public void DifferentInputsDiverge()
    {
        var baseline = RunScript(99, 300);
        var changed = RunScript(99, 300, sim => sim.Schedule(10, new MoveCommand(1, new[] { 5 }, FixVec2.FromInts(0, 0))));
        // The order is in the state hash while it waits in the queue, so a peer that never got it differs at once.
        Assert.All(Enumerable.Range(0, 10), i => Assert.NotEqual(baseline[i], changed[i]));
        Assert.NotEqual(baseline[^1], changed[^1]);
    }

    [Fact]
    public void AnOrderThatChangesNothingLeavesNoTraceOnceItHasRun()
    {
        var baseline = RunScript(99, 300);
        var ignored = RunScript(99, 300, sim => sim.Schedule(10, new MoveCommand(0, new[] { 5 }, FixVec2.FromInts(0, 0)))); // an enemy ship
        Assert.NotEqual(baseline[5], ignored[5]); // waiting in the queue
        Assert.Equal(baseline[10..], ignored[10..]); // ran at tick 10 (hash after that step) and did nothing
    }

    [Fact]
    public void ShipsArriveExactlyAtTheirTarget()
    {
        var sim = MatchSetup.CreateMvpSkirmish(TestData.Real, 1);
        var ship = sim.Ships.First(s => s.Owner == 0);
        var target = FixVec2.FromInts(37, -12);
        sim.Schedule(0, new MoveCommand(0, new[] { ship.Id }, target));
        for (var i = 0; i < Simulation.TickRate * 120; i++) sim.Step();
        Assert.Equal(target, ship.Position);
        Assert.False(ship.HasMoveTarget);
    }

    [Fact]
    public void PlayersCannotOrderEnemyShips()
    {
        var sim = MatchSetup.CreateMvpSkirmish(TestData.Real, 1);
        var enemy = sim.Ships.First(s => s.Owner == 1);
        var start = enemy.Position;
        sim.Schedule(0, new MoveCommand(0, new[] { enemy.Id }, FixVec2.Zero));
        for (var i = 0; i < 30; i++) sim.Step();
        Assert.Equal(start, enemy.Position);
    }

    [Fact]
    public void SchedulingInThePastIsRejected()
    {
        var sim = MatchSetup.CreateMvpSkirmish(TestData.Real, 1);
        sim.Step();
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Schedule(0, new MoveCommand(0, Array.Empty<int>(), FixVec2.Zero)));
    }
}

public class AiTests
{
    private static ulong RunWithAi(ulong seed)
    {
        var sim = MatchSetup.CreateMvpSkirmish(TestData.Real, seed);
        var ai = new FinalFrontier.Sim.Ai.ChaseAi(1);
        for (var i = 0; i < 600; i++)
        {
            foreach (var c in ai.Think(sim)) sim.Schedule(sim.Tick + 2, c);
            sim.Step();
        }
        return sim.StateHash();
    }

    [Fact]
    public void AiIsDeterministic() => Assert.Equal(RunWithAi(5), RunWithAi(5));

    [Fact]
    public void AiClosesDistanceToTheEnemy()
    {
        var sim = MatchSetup.CreateMvpSkirmish(TestData.Real, 1);
        var ai = new FinalFrontier.Sim.Ai.ChaseAi(1);
        var ship = sim.Ships.First(s => s.Owner == 1);
        var before = ship.Position.X;
        for (var i = 0; i < 150; i++)
        {
            foreach (var c in ai.Think(sim)) sim.Schedule(sim.Tick + 2, c);
            sim.Step();
        }
        Assert.True(ship.Position.X < before);
    }
}
