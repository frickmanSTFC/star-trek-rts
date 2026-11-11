using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Missions;

// The test panel's MISSIONS tab (DebugMissionCommand, counted in Simulation.DebugEdits like every test-panel edit).
public sealed partial class MissionBook
{
    /// <summary>Why the debug command would be refused now, or null.</summary>
    internal string? DebugProblem(DebugMissionCommand c)
    {
        if (_sim == null) return "no game";
        if (c.Action == DebugMissionAction.Enable) return EnableProblem(c.MissionId);
        if (!Enabled) return "missions are off";
        if (_sim.Surface == null) return "no planet surface";
        if (c.Target < 0 || c.Target >= _sim.Players.Count || c.Target >= _sim.Surface.Players.Count) return "no such player";
        Track(c.Target);
        if (RunOf(c.Target, c.MissionId) is not { } run || Def(run) is not { } d) return $"no mission '{c.MissionId}' in this chain";
        switch (c.Action)
        {
            case DebugMissionAction.Start:
                return null;
            case DebugMissionAction.GotoStage:
                return d.StageIndex(c.Stage) >= 0 ? null : $"no stage '{c.Stage}'";
            case DebugMissionAction.CompleteObjective:
                if (run.State != MissionState.Active) return "not active";
                foreach (var o in d.Stages[run.Stage].Objectives)
                    if (o.Id == c.Objective) return null;
                return $"no objective '{c.Objective}' in the current stage";
            case DebugMissionAction.Complete:
            case DebugMissionAction.Fail:
                return run.State is MissionState.Completed or MissionState.Failed ? "it has ended already" : null;
            case DebugMissionAction.Reset:
                return null;
            default:
                return "unknown action";
        }
    }

    /// <summary>Runs a debug command that passed DebugProblem.</summary>
    internal void ApplyDebug(DebugMissionCommand c)
    {
        if (c.Action == DebugMissionAction.Enable)
        {
            TurnOn(c.MissionId);
            return;
        }
        var p = c.Target;
        var run = RunOf(p, c.MissionId)!;
        var d = Def(run)!;
        switch (c.Action)
        {
            case DebugMissionAction.Start:
                Restart(p, run, d, c.Tile);
                break;
            case DebugMissionAction.GotoStage:
                if (run.State != MissionState.Active) Restart(p, run, d, c.Tile);
                if (run.State == MissionState.Active) EnterStage(p, run, d, d.StageIndex(c.Stage));
                break;
            case DebugMissionAction.CompleteObjective:
            {
                var objectives = d.Stages[run.Stage].Objectives;
                for (var k = 0; k < objectives.Count && k < run.Done.Length; k++)
                {
                    if (objectives[k].Id != c.Objective) continue;
                    run.Done[k] = true;
                    var target = objectives[k].HoldSeconds > 0 ? Fix64.FromInt(objectives[k].HoldSeconds) : Target(p, run, objectives[k].Cond);
                    run.Progress[k] = Fix64.Max(run.Progress[k], target);
                    MarkSitesDone(run, objectives[k].Cond);
                }
                break;
            }
            case DebugMissionAction.Complete:
                Complete(p, run, d);
                break;
            case DebugMissionAction.Fail:
                Fail(p, run, d);
                break;
            case DebugMissionAction.Reset:
                Clear(run);
                break;
        }
    }

    /// <summary>Back to a fresh Locked run (its sites are removed).</summary>
    private void Clear(MissionRun run)
    {
        _sites.RemoveAll(s => s.Owner >= 0 && s.MissionId == run.Id && run.Bindings.Exists(b => b.Sites.Contains(s.Id)));
        run.State = MissionState.Locked;
        run.Stage = -1;
        run.StageStart = run.Start = run.End = run.OfferEnd = run.TimerEnd = -1;
        run.Progress = System.Array.Empty<Fix64>();
        run.Done = System.Array.Empty<bool>();
        run.Base = System.Array.Empty<Fix64>();
        run.Visited.Clear();
        run.Bindings.Clear();
        run.Markers.Clear();
        run.Rewarded = false;
        run.Outcome = "";
    }

    /// <summary>Starts a mission from its first stage whatever its state: the trigger binds what it can now, and every
    /// binding it names that didn't hold is bound to the tile (a unit binding to the player's unit on it, if any).</summary>
    private void Restart(int p, MissionRun run, MissionDef d, int tile)
    {
        var s = _sim!.Surface!;
        Clear(run);
        var bindings = new List<Binding>();
        if (d.Trigger != null) Check(p, null, -1, d.Trigger, bindings);
        if (tile < 0 || tile >= s.Map.Grid.Count) tile = Hub(s, p);
        foreach (var (name, unit) in TriggerBinds(d.Trigger))
        {
            if (bindings.Exists(b => b.Name == name) || tile < 0) continue;
            var b = new Binding { Name = name, Text = TileText(s, tile) };
            if (unit)
                foreach (var u in s.Units)
                    if (u.Owner == p && u.Tile == tile)
                    {
                        b.UnitId = u.Id;
                        b.Text = CallSign(u);
                        break;
                    }
            if (b.UnitId == 0) b.Tiles.Add(tile);
            if (s.Map.DepositResource[tile] >= 0) b.Res = _sim.Data.Resources[s.Map.DepositResource[tile]].Id;
            bindings.Add(b);
        }
        Activate(p, run, d, bindings, brief: true);
    }

    /// <summary>The names a trigger binds, and whether each is a unit.</summary>
    private static IEnumerable<(string Name, bool Unit)> TriggerBinds(MissionCondDef? c)
    {
        if (c == null) yield break;
        if (c.F("bind") is { } name) yield return (name, c.Kind == "unit");
        foreach (var child in c.Children)
            foreach (var x in TriggerBinds(child))
                yield return x;
    }
}
