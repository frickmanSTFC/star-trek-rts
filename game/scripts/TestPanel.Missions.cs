using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Missions;
using Godot;

namespace FinalFrontier;

// The MISSIONS tab (docs/missions-contract.md C "Test panel MISSIONS tab", debug builds): whether missions are on (and
// a switch that turns them on in a sandbox game), every mission of the local player with its state and stage, and for
// the picked one START / GOTO STAGE n / COMPLETE OBJECTIVE n / COMPLETE / FAIL / RESET. Every edit goes through
// `MissionDebug` → S2's DebugMissionCommand (new tag 119) with Simulation.DebugProblem first (Apply), so it counts for
// TEST MODE; no undo (mission state can't be put back from here).
//
// MERGE SEAMS (TODO(merge S2)): `MissionDebug` and `MissionsOnCommand` return null until S2's DebugMissionCommand is
// merged; the buttons then say so instead of doing anything. Replace their bodies with the real command, e.g.
//   new DebugMissionCommand(Me, missionId, DebugMissionAction.Start / GotoStage / CompleteObjective / Complete / Fail / Reset, arg)
// and for the switch either a DebugMissionCommand "enable" action or nothing if S2 enables at set-up only.
public partial class TestPanel
{
    private int _missionPick;
    private int _missionArg;

    private static readonly string[] MissionActions = { "start", "goto_stage", "complete_objective", "complete", "fail", "reset" };

    /// <summary>TODO(merge S2): the debug command for one mission action (null = not in this build).</summary>
    private DebugCommand? MissionDebug(string missionId, string action, int arg) => null;

    /// <summary>TODO(merge S2): the debug command that turns missions on in a running sandbox game, e.g. with the
    /// Dawn of Warp chain (null = not in this build; a campaign start turns them on at set-up instead).</summary>
    private DebugCommand? MissionsOnCommand(string chain) => null;

    /// <summary>Every mission id the panel can act on: the ones the book shows, then any the data lists (by reflection
    /// on GameData.Missions until S2's defs are merged), in order, no repeats.</summary>
    private List<string> MissionIds()
    {
        var ids = new List<string>();
        foreach (var m in Sim.Missions.For(Me)) if (!ids.Contains(m.Id)) ids.Add(m.Id);
        if (Data.GetType().GetProperty("Missions")?.GetValue(Data) is IEnumerable defs)
            foreach (var d in defs)
                if (d?.GetType().GetProperty("Id")?.GetValue(d) is string id && !ids.Contains(id)) ids.Add(id);
        return ids;
    }

    private string MissionsShape() => $"missions|{Sim.Missions.Enabled}|{string.Join(",", MissionIds())}";

    private void ApplyMission(string text, DebugCommand? command)
    {
        if (command == null)
        {
            _host.Notify("Test panel: mission debug commands aren't in this build yet (S2's DebugMissionCommand)", true);
            return;
        }
        Apply(text, new[] { command }, null);
    }

    private void BuildMissions()
    {
        var book = Sim.Missions;
        var state = Wrapped("", 13, Soft);
        _content.AddChild(state);
        _refreshers.Add(() =>
        {
            var list = book.For(Me);
            state.Text = book.Enabled
                ? $"Missions on: {list.Count(m => m.State == MissionState.Active)} active, {list.Count(m => m.State == MissionState.Offered)} offered, {list.Count(m => m.State == MissionState.Completed)} done."
                : "Missions are off in this game (a campaign start turns them on).";
        });
        if (!book.Enabled)
        {
            _content.AddChild(MakeButton("Turn missions on (Dawn of Warp chain)", Green, () =>
                ApplyMission("missions: on", MissionsOnCommand(GameSetup.MissionChain ?? "dawn_of_warp")), 0, 34));
        }

        var ids = MissionIds();
        _content.AddChild(Section("MISSIONS"));
        if (ids.Count == 0)
        {
            _content.AddChild(Wrapped("No missions listed (the mission data or the book isn't in this build yet).", 13, Dim));
            return;
        }
        // Every mission with its state, refreshed each frame.
        var rows = Wrapped("", 13, Ink);
        _content.AddChild(rows);
        _refreshers.Add(() =>
        {
            var views = book.For(Me);
            rows.Text = string.Join("\n", ids.Select(id =>
            {
                var v = views.FirstOrDefault(x => x.Id == id);
                if (v == null) return $"{id}: locked";
                var done = v.Objectives.Count(o => o.Done);
                var timer = v.TimerEndTick > Sim.Tick ? $" · {(v.TimerEndTick - Sim.Tick) / Simulation.TickRate} s" : "";
                return $"{(v.Kind == MissionKind.Main ? "MAIN" : "SIDE")} {v.Title}: {v.State.ToString().ToLowerInvariant()}" +
                       (v.State == MissionState.Active ? $" · {v.StageTitle} · {done}/{v.Objectives.Count} objectives" : "") + timer;
            }));
        });

        _content.AddChild(Section("EDIT"));
        _missionPick = Math.Clamp(_missionPick, 0, ids.Count - 1);
        _content.AddChild(MakeOption(ids.ToArray(), _missionPick, i => _missionPick = i));
        var argRow = Row();
        argRow.AddChild(Text("Stage / objective", 14, Ink));
        var argLabel = Text($"{_missionArg + 1}", 14, Accent, _caps);
        argLabel.CustomMinimumSize = new Vector2(28, 0);
        var slider = MakeSlider(0, 9, _ => { });
        slider.Value = _missionArg;
        slider.ValueChanged += v =>
        {
            _missionArg = (int)v;
            argLabel.Text = $"{_missionArg + 1}";
        };
        argRow.AddChild(slider);
        argRow.AddChild(argLabel);
        _content.AddChild(argRow);

        var grid = Grid(3);
        string Picked() => ids[Math.Clamp(_missionPick, 0, ids.Count - 1)];
        grid.AddChild(MakeButton("Start", Green, () => ApplyMission($"mission {Picked()}: start", MissionDebug(Picked(), MissionActions[0], 0)), 0, 34));
        grid.AddChild(MakeButton("Go to stage", Blue, () => ApplyMission($"mission {Picked()}: stage {_missionArg + 1}", MissionDebug(Picked(), MissionActions[1], _missionArg)), 0, 34));
        grid.AddChild(MakeButton("Objective done", Blue, () => ApplyMission($"mission {Picked()}: objective {_missionArg + 1} done", MissionDebug(Picked(), MissionActions[2], _missionArg)), 0, 34));
        grid.AddChild(MakeButton("Complete", Green, () => ApplyMission($"mission {Picked()}: complete", MissionDebug(Picked(), MissionActions[3], 0)), 0, 34));
        grid.AddChild(MakeButton("Fail", Red, () => ApplyMission($"mission {Picked()}: fail", MissionDebug(Picked(), MissionActions[4], 0)), 0, 34));
        grid.AddChild(MakeButton("Reset", Yellow, () => ApplyMission($"mission {Picked()}: reset", MissionDebug(Picked(), MissionActions[5], 0)), 0, 34));
        _content.AddChild(grid);
        _content.AddChild(Wrapped("Stage and objective numbers count from 1. Edits count for TEST MODE and can't be undone. The tracker, comms card (Y) and log (N) follow at once.", 12, Dim));
    }
}
