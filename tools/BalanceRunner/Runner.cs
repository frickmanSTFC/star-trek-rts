using System.Diagnostics;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;

namespace FinalFrontier.Balance;

/// <summary>Plays one match: a fresh planet from the seed, one bot following the build order, stepped as fast as
/// possible for the given game time, with a <see cref="Recorder"/> watching.</summary>
public static class Runner
{
    public static RunResult Run(GameData data, BuildOrder order, ulong seed, double minutes, int sampleSeconds = 10, string variant = "A")
    {
        var result = new RunResult
        {
            Order = order.Name,
            Variant = variant,
            Faction = order.Faction,
            Seed = seed,
            Minutes = minutes,
            StepsTotal = order.Steps.Count,
        };
        var sim = order.World != null
            ? MatchSetup.CreatePlanetLanding(data, seed, order.Faction, null, order.World)
            : MatchSetup.CreatePlanetLanding(data, seed, order.Faction, order.Planet);
        var bot = new Bot(sim, 0, order);
        var recorder = new Recorder(sim, 0, bot, sampleSeconds, result);
        var ticks = (long)System.Math.Round(minutes * Simulation.TicksPerMinute);
        var clock = Stopwatch.StartNew();
        recorder.Sample();
        try
        {
            while (sim.Tick < ticks)
            {
                bot.Think();
                sim.Step();
                recorder.AfterStep();
            }
        }
        catch (Exception e) when (e is ArithmeticException or InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException or KeyNotFoundException)
        {
            // A sim (or bot) bug: keep what was measured and say where it broke, so a batch still finishes.
            var frame = e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("FinalFrontier"))?.Trim() ?? "";
            result.Crash = $"{e.GetType().Name} at tick {sim.Tick} ({sim.Tick / (double)Simulation.TicksPerMinute:0.0} min): {e.Message} {frame}";
        }
        clock.Stop();
        recorder.Finish();
        result.Ticks = sim.Tick;
        result.WallMs = System.Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        result.TicksPerSecond = System.Math.Round(sim.Tick / System.Math.Max(1e-6, clock.Elapsed.TotalSeconds));
        result.Speedup = System.Math.Round(result.TicksPerSecond / Simulation.TickRate, 1);
        result.StateHash = "0x" + sim.StateHash().ToString("x16");
        result.LandingTile = bot.LandingTile;
        return result;
    }
}
