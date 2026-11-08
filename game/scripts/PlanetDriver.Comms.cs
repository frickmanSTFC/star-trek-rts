using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Signal delay and the orbital sensor sweep (owner 2026-11-08: "if the ship is not in orbit, communications to them is
// delayed by the distance. So any SOS or support is going to be delayed"; "if you go to a non-explored body, how do you
// scan it?"). The sim relays colony orders with the delay (Simulation.OrdersInTransit, StarSystemState.CommsDelayTicks);
// here the colony's reports (event lines that come from the surface: finished buildings, breakdowns, hazards, SOS) wait
// for the same delay in game time before they reach the feed, the top bar shows SIGNAL DELAY, and the flagship's
// sensor sweeps get their event lines.
public partial class PlanetDriver
{
    private readonly List<(long Due, string Text, Color Dot, int Seconds)> _relayedReports = new();
    private int _transitSeen;
    private double _relayNotedAt = -100;
    private string? _sweepSeen;

    /// <summary>The signal delay to the colony now, in sim ticks (0 in orbit of its world or without a star system).</summary>
    private long CommsDelayTicks => _sim.System?.CommsDelayTicks(LocalPlayer) ?? 0;

    /// <summary>A report from the colony for the event feed: at once in orbit of its world, else after the signal delay
    /// (game time, so a paused game holds it), tagged with how long it took.</summary>
    private void ColonyReport(string text, Color dot)
    {
        var delay = CommsDelayTicks;
        if (delay <= 0)
        {
            _hud.PushEvent(text, dot);
            return;
        }
        var seconds = (int)((delay + FinalFrontier.Sim.Core.Simulation.TickRate - 1) / FinalFrontier.Sim.Core.Simulation.TickRate);
        _relayedReports.Add((_sim.Tick + delay, text, dot, seconds));
    }

    /// <summary>Once a frame after the events: delivers relayed reports that have arrived, notes orders going out with the
    /// delay, shows the SIGNAL DELAY tag, and reports the flagship's sensor sweeps.</summary>
    private void UpdateComms()
    {
        for (var i = 0; i < _relayedReports.Count; i++)
        {
            var r = _relayedReports[i];
            if (r.Due > _sim.Tick) continue;
            _hud.PushEvent($"{r.Text}  · relayed, {r.Seconds} s old", r.Dot);
            _relayedReports.RemoveAt(i--);
        }

        var transit = 0;
        long next = long.MaxValue;
        foreach (var o in _sim.OrdersInTransit)
        {
            if (o.Command.Player != LocalPlayer) continue;
            transit++;
            next = System.Math.Min(next, o.ArriveTick);
        }
        if (transit > _transitSeen && _clock - _relayNotedAt > 4)
        {
            _relayNotedAt = _clock;
            var secs = (int)((next - _sim.Tick + FinalFrontier.Sim.Core.Simulation.TickRate - 1) / FinalFrontier.Sim.Core.Simulation.TickRate);
            _hud.PushEvent($"Order relayed to the colony: it arrives in {System.Math.Max(1, secs)} s (signal delay)", new Color(1f, 0.7f, 0.42f));
        }
        _transitSeen = transit;
        var delay = _sim.System?.CommsDelaySeconds(LocalPlayer) ?? 0;
        _hud.ShowSignal(delay, transit);

        SweepEvents();
    }

    /// <summary>"Sensor sweep of X: 15 s" on entering orbit of an uncharted body, "X charted" when it's done.</summary>
    private void SweepEvents()
    {
        var f = _system.Flagship;
        var sweeping = f?.SweepBody;
        if (sweeping == _sweepSeen) return;
        var was = _sweepSeen;
        _sweepSeen = sweeping;
        var sys = _system.State;
        if (sys == null || f == null) return;
        if (sweeping != null && _system.Body(sweeping) is { } b)
            _hud.PushEvent($"{f.Name}: sensor sweep of {_system.KnownName(b)} under way ({sys.SweepSecondsLeft(f)} s): atmosphere, surface and structures", HelmScreen.Cyan);
        if (was != null && _system.Body(was) is { } done)
        {
            if (_system.Charted(was)) _hud.PushEvent($"Sensor sweep complete: {done.Name} charted. Live readings while {f.Name} stays in orbit", HelmScreen.Cyan);
            else _hud.PushEvent($"Sensor sweep of {_system.KnownName(done)} called off: the ship left orbit", new Color(1f, 0.7f, 0.42f));
        }
    }
}
