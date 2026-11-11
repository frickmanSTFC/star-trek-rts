using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>The Jack Skellington raid joke (Surface/Jack.cs, docs/jack-contract.md).</summary>
public class JackTests
{
    private static GameData Data => TestData.Real;
    private static int Sec(int seconds) => seconds * Simulation.TickRate;

    private static Simulation Landed(bool jack, out SurfaceStructure hub)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        sim.Surface!.JackEnabled = jack;
        var site = Enumerable.Range(0, sim.Surface.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        while (sim.Surface.Players[0].Phase != LandingPhase.Landed) sim.Step();
        hub = sim.Surface.StructureAt(site)!;
        return sim;
    }

    private static void StepFor(Simulation sim, int seconds)
    {
        for (var i = 0; i < Sec(seconds); i++) sim.Step();
    }

    [Fact]
    public void JackWarnsAMinuteAfterTouchdownThenRaidsAndLeaves()
    {
        var sim = Landed(true, out var hub);
        var s = sim.Surface!;
        var landed = s.Players[0].LandsAtTick;
        StepFor(sim, JackRules.WarnAfter - 2);
        Assert.Empty(s.Jacks);
        StepFor(sim, 3);
        var j = Assert.Single(s.Jacks);
        Assert.Equal(landed + Sec(JackRules.WarnAfter), j.WarnTick);
        Assert.Contains(s.JackEvents, e => e.Kind == "warning" && e.Text.Contains("JACK SKELLINGTON"));
        Assert.Equal(JackPhase.Warning, j.PhaseAt(sim.Tick));

        StepFor(sim, JackRules.ArriveAfter + JackRules.RiseSeconds + 1);
        Assert.Equal(JackPhase.Rampage, j.PhaseAt(sim.Tick));
        Assert.True(j.Tile >= 0);

        StepFor(sim, JackRules.RampageSeconds);
        Assert.Contains(s.JackAttacks, a => a.Kind == "pumpkin" && a.Landed);
        Assert.Contains(s.JackAttacks, a => a.Kind == "bats");
        Assert.Contains(s.JackEvents, e => e.Kind == "shriek");
        Assert.True(hub.Condition < Fix64.FromInt(100), "the hub took some pumpkins");
        Assert.True(hub.Condition >= Fix64.FromInt(100 - JackRules.MaxDamage) - Fix64.FromInt(5), "but not much"); // + a little wear
        Assert.All(s.Structures, st => Assert.True(st.Condition >= Fix64.One));
        Assert.All(s.Units, u => Assert.True(u.Integrity >= Fix64.One));

        StepFor(sim, JackRules.LeaveSeconds + 1);
        Assert.Equal(JackPhase.Gone, j.PhaseAt(sim.Tick));
        Assert.Contains(s.JackEvents, e => e.Kind == "gone");
        var events = s.JackEvents.Count;
        StepFor(sim, 120);
        Assert.Single(s.Jacks); // once per match
        Assert.Equal(events, s.JackEvents.Count);
    }

    [Fact]
    public void JackIsOffUnlessTheGameTurnsHimOn()
    {
        var sim = Landed(false, out _);
        StepFor(sim, 200);
        Assert.Empty(sim.Surface!.Jacks);
    }

    [Fact]
    public void JackOffLeavesTheStateHashAlone()
    {
        var a = Landed(false, out _);
        var b = Landed(false, out _);
        StepFor(a, 100);
        StepFor(b, 100);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void SummonJackStartsARaidNowAndIsDeterministic()
    {
        ulong Run()
        {
            var sim = Landed(false, out _);
            StepFor(sim, 25);
            Assert.Null(sim.DebugProblem(new DebugJackCommand(0, 0)));
            sim.Schedule(sim.Tick, new DebugJackCommand(0, 0));
            sim.Step();
            Assert.Single(sim.Surface!.Jacks);
            Assert.Equal("Jack is already here", sim.DebugProblem(new DebugJackCommand(0, 0)));
            StepFor(sim, 120);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
