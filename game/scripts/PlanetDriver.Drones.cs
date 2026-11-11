using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Scout drones on the HUD (docs/missions-contract.md B "UI"; commands only, each after the sim's own check): the Drone
// Pad's building panel (one row per bay: the drone's call sign, a battery gauge and what it is doing; drones in stock /
// "make drones at a Machine Shop"; LAUNCH [L] and RECALL [R]), LAUNCH arms a tile pick on the map (green / red hover by
// PlanetSurface.LaunchProblem, the reason in the tooltip, the comms coverage overlay on; click sends LaunchDroneCommand,
// Shift keeps picking for the next drone, right-click / Esc stop), a selected drone's panel (battery bar, state,
// target, pad, coverage; RETURN [R], LAUNCH [L] while docked), right-click retargets flying drones (MoveUnitsCommand
// inside coverage) or launches docked ones there, R sends selected drones home (RecallDroneCommand), G toggles the
// comms coverage overlay (also on while a launch is armed or drones are selected), and event lines for launches,
// returns, landings and losses. World-space drawing is agent V1's: PlanetView methods are called through `Seam`
// (reflection, so this builds and runs before V1's files are merged; replace with direct calls at the merge).
public partial class PlanetDriver
{
    /// <summary>The Drone Pad a launch is being aimed from (0 = not picking).</summary>
    private int _launchPad;

    /// <summary>G: the comms coverage overlay is switched on by the player.</summary>
    private bool _commsOverlay;

    private readonly Dictionary<int, DroneState> _droneSeen = new();
    private int _droneScoutIndex = -2;

    private static readonly Color DroneBlue = new(0.55f, 0.85f, 1f);

    // ---------------------------------------------------------------- merge seams (agent V1's PlanetView / OrbitView)

    // ---------------------------------------------------------------- what is a drone, what is selected

    private bool IsDrone(SurfaceUnit u) => Surface.DroneOf(u).State != DroneState.None || Surface.IsAerial(u);

    private bool IsDroneId(int id) => Surface.FindUnit(id) is { } u && IsDrone(u);

    private static bool Airborne(DroneState s) => s is DroneState.Launching or DroneState.Flying or DroneState.Loitering or DroneState.Returning;

    /// <summary>Our selected drones (by id), empty when none.</summary>
    private List<SurfaceUnit> SelectedDrones() =>
        _view.SelectedUnits.OrderBy(id => id).Select(Surface.FindUnit).Where(u => u != null && u.Owner == LocalPlayer && IsDrone(u)).Select(u => u!).ToList();

    private bool OnlyDronesSelected() => _view.SelectedUnits.Count > 0 && _view.SelectedUnits.All(IsDroneId);

    /// <summary>The selected building if it is one of our finished Drone Pads.</summary>
    private SurfaceStructure? SelectedPad()
    {
        var s = _selectedStructure != 0 ? Surface.FindStructure(_selectedStructure) : null;
        return s != null && s.Owner == LocalPlayer && s.State == StructureState.Complete && Surface.DroneBays(s) > 0 ? s : null;
    }

    private int ResourceIndex(string id)
    {
        for (var i = 0; i < _data.Resources.Count; i++)
            if (_data.Resources[i].Id == id) return i;
        return -1;
    }

    /// <summary>Scout drones in stock (made at a Machine Shop, waiting for a free bay).</summary>
    private int DronesInStock()
    {
        if (_droneScoutIndex == -2) _droneScoutIndex = ResourceIndex("scout_drone");
        return _droneScoutIndex < 0 ? 0 : _sim.Players[LocalPlayer].Stock[_droneScoutIndex].FloorToInt();
    }

    private static string Gauge(Fix64 battery)
    {
        var f = Mathf.Clamp(battery.ToFloat(), 0f, 1f);
        var lit = Mathf.RoundToInt(f * 5);
        return new string('▮', lit) + new string('▯', 5 - lit) + $" {Mathf.RoundToInt(f * 100)}%";
    }

    private string DroneStateText(SurfaceUnit u, DroneInfo d)
    {
        var tiles = d.Target >= 0 ? Surface.Map.Grid.Distance(u.Tile, d.Target) : 0;
        return d.State switch
        {
            DroneState.Docked => d.Battery >= Fix64.One ? "charged, ready" : "charging",
            DroneState.Launching => "lifting off",
            DroneState.Flying => tiles > 0 ? $"flying to its target · {tiles} tiles" : "flying to its target",
            DroneState.Loitering => "circling the target, surveying",
            DroneState.Returning => d.ReturnReason switch
            {
                "battery" => "heading home: battery low",
                "coverage" => "heading home: out of comms coverage",
                "done" => "heading home: job done",
                _ => "heading home",
            },
            DroneState.Landing => "setting down",
            DroneState.Lost => "lost: the battery ran out",
            _ => "",
        };
    }

    // ---------------------------------------------------------------- input (one hook in _UnhandledInput)

    /// <summary>Keys and clicks for drones on the surface: while a launch is armed the clicks are the pick; G overlay;
    /// R / L for selected drones or a selected Drone Pad. True when used.</summary>
    private bool DroneInput(InputEvent e)
    {
        if (Me.Phase != LandingPhase.Landed) return false;
        if (_launchPad != 0)
        {
            switch (e)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                    if (!_mouseOverUi) LaunchClick(click.ShiftPressed);
                    return !_mouseOverUi;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    Sfx.Ui("cancel");
                    StopLaunch();
                    return true;
            }
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return false;
        switch (key.Keycode)
        {
            case Key.G:
                _commsOverlay = !_commsOverlay;
                _hud.PushEvent(_commsOverlay ? "Comms coverage shown (G)" : "Comms coverage hidden (G)", DroneBlue);
                return true;
            case Key.R when _placing == null && OnlyDronesSelected():
                RecallDrones(SelectedDrones());
                return true;
            case Key.R when _placing == null && SelectedPad() is { } pad && _view.SelectedUnits.Count == 0:
                RecallDrones(Surface.DronesOf(pad.Id).ToList());
                return true;
            case Key.L when SelectedPad() is { } pad && _view.SelectedUnits.Count == 0:
                ArmLaunch(pad.Id);
                return true;
            case Key.L when OnlyDronesSelected():
                var docked = SelectedDrones().Select(u => Surface.DroneOf(u)).FirstOrDefault(d => d.State == DroneState.Docked);
                if (docked.State == DroneState.Docked) ArmLaunch(docked.Pad);
                else _hud.PushEvent("The selected drones are out: right-click to send them somewhere else, R to bring them home", RoverWarn);
                return true;
        }
        return false;
    }

    /// <summary>The building / drone panel buttons (OnAction). True when it was one of ours.</summary>
    private bool DroneAction(string id)
    {
        switch (id)
        {
            case "drone_launch":
                if (SelectedPad() is { } pad) ArmLaunch(pad.Id);
                else if (SelectedDrones().Select(u => Surface.DroneOf(u)).FirstOrDefault(d => d.State == DroneState.Docked) is { State: DroneState.Docked } d)
                    ArmLaunch(d.Pad);
                return true;
            case "drone_recall":
                if (SelectedPad() is { } p) RecallDrones(Surface.DronesOf(p.Id).ToList());
                else RecallDrones(SelectedDrones());
                return true;
        }
        return false;
    }

    private void RecallDrones(IReadOnlyList<SurfaceUnit> drones)
    {
        var going = drones.Where(u => u.Owner == LocalPlayer && Surface.DroneOf(u).State is DroneState.Launching or DroneState.Flying or DroneState.Loitering).ToList();
        if (going.Count == 0)
        {
            _hud.PushEvent(drones.Any(u => Surface.DroneOf(u).State == DroneState.Returning) ? "Already heading home" : "No drone out to call back", RoverWarn);
            return;
        }
        Sfx.Ui("confirm");
        _sim.Schedule(_sim.Tick + InputDelayTicks, new RecallDroneCommand(LocalPlayer, going.Select(u => u.Id).ToList()));
        _hud.PushEvent(going.Count == 1 ? $"{UnitName(going[0])} called home" : $"{going.Count} drones called home", DroneBlue);
    }

    // ---------------------------------------------------------------- launch pick

    private void ArmLaunch(int padId)
    {
        if (_launchPad == padId)
        {
            StopLaunch();
            return;
        }
        var pad = Surface.FindStructure(padId);
        if (pad == null || pad.Owner != LocalPlayer) return;
        if (!Surface.DronesOf(padId).Any(u => Surface.DroneOf(u).State == DroneState.Docked))
        {
            _hud.PushEvent(DronesInStock() > 0 ? "No drone docked yet: the pad loads one from stock" : "No drone on this pad: make scout drones at a Machine Shop", RoverWarn);
            return;
        }
        if (_placing != null) StopPlacing();
        StopFounding();
        _launchPad = padId;
        Sfx.Ui("open");
    }

    private void StopLaunch()
    {
        if (_launchPad == 0) return;
        _launchPad = 0;
        _view.SetHover(-1, Colors.White);
    }

    /// <summary>UpdateHover while a launch is armed: the hovered tile green or red by LaunchProblem. True = handled.</summary>
    private bool LaunchHover()
    {
        if (_launchPad == 0) return false;
        _view.SetGhost(null, -1, 0, false);
        var ok = _hoverTile >= 0 && Surface.LaunchProblem(_sim, LocalPlayer, _launchPad, _hoverTile) == null;
        _view.SetHover(_hoverTile, ok ? new Color(0.3f, 1f, 0.4f, 0.7f) : new Color(1f, 0.3f, 0.25f, 0.6f), Mathf.Clamp(_distance * 0.006f, 0.12f, 2.4f));
        return true;
    }

    private void LaunchClick(bool shift)
    {
        if (_hoverTile < 0) return;
        var problem = Surface.LaunchProblem(_sim, LocalPlayer, _launchPad, _hoverTile);
        if (problem != null)
        {
            _hud.PushEvent($"Can't launch there: {problem}", RoverWarn);
            return;
        }
        Sfx.Ui("place");
        _sim.Schedule(_sim.Tick + InputDelayTicks, new LaunchDroneCommand(LocalPlayer, _launchPad, _hoverTile));
        _hud.PushEvent($"Drone launching to {Surface.Map.Terrain(_hoverTile).Name.ToLowerInvariant()} {Surface.Map.Grid.Distance(Surface.FindStructure(_launchPad)?.Tile ?? _hoverTile, _hoverTile)} tiles out", DroneBlue);
        if (!shift) StopLaunch();
    }

    /// <summary>The tooltip while a launch is armed.</summary>
    private string LaunchTooltip(int tile)
    {
        if (tile < 0) return "";
        var sb = new StringBuilder(Surface.Map.Terrain(tile).Name);
        if (Surface.FindStructure(_launchPad) is { } pad) sb.Append("\n").Append(Surface.Map.Grid.Distance(pad.Tile, tile)).Append(" tiles from the pad");
        sb.Append(Surface.CommsCovered(LocalPlayer, tile) ? "\nInside comms coverage" : "\nOutside comms coverage");
        var problem = Surface.LaunchProblem(_sim, LocalPlayer, _launchPad, tile);
        sb.Append(problem == null ? "\nClick to launch a drone here (Shift: keep picking)" : $"\nCan't launch here: {problem}");
        return sb.ToString();
    }

    private string LaunchBanner() =>
        "Launch a drone: click a tile inside comms coverage (blue rim)  ·  it flies straight there over anything, surveys, circles and flies home  ·  Shift+click launch more  ·  right-click / Esc stop";

    // ---------------------------------------------------------------- right-click (one hook in RightClick)

    /// <summary>Right-click with drones selected: flying ones are sent to the tile (inside coverage), docked ones
    /// launch there from their pads. True when the selection was only drones (nothing else to do).</summary>
    private bool DroneRightClick()
    {
        var drones = SelectedDrones();
        if (drones.Count == 0) return false;
        var tile = _hoverTile;
        var flying = drones.Where(u => Surface.DroneOf(u).State is DroneState.Flying or DroneState.Loitering or DroneState.Returning or DroneState.Launching).ToList();
        if (flying.Count > 0)
        {
            if (!Surface.CommsCovered(LocalPlayer, tile))
                _hud.PushEvent("Out of comms range: put a relay satellite over it", RoverWarn);
            else
            {
                _sim.Schedule(_sim.Tick + InputDelayTicks, new MoveUnitsCommand(LocalPlayer, flying.Select(u => u.Id).ToList(), tile));
                Sfx.Ui("confirm");
            }
        }
        foreach (var padId in drones.Select(u => Surface.DroneOf(u)).Where(d => d.State == DroneState.Docked).Select(d => d.Pad).Distinct())
        {
            var problem = Surface.LaunchProblem(_sim, LocalPlayer, padId, tile);
            if (problem == null) _sim.Schedule(_sim.Tick + InputDelayTicks, new LaunchDroneCommand(LocalPlayer, padId, tile));
            else _hud.PushEvent($"Can't launch there: {problem}", RoverWarn);
        }
        return OnlyDronesSelected();
    }

    // ---------------------------------------------------------------- panels

    /// <summary>A Drone Pad's rows and buttons (ShowStructure, before its panel goes up).</summary>
    private void DronePadPanel(SurfaceStructure s, bool complete, List<(string, string)> rows, List<ActionView> actions, ref string status, ref Color statusColour)
    {
        var bays = Surface.DroneBays(s);
        if (bays <= 0 || s.Owner != LocalPlayer) return;
        var drones = Surface.DronesOf(s.Id);
        var stock = DronesInStock();
        var padRows = new List<(string, string)>();
        int docked = 0, ready = 0, airborne = 0;
        for (var bay = 0; bay < bays; bay++)
        {
            var u = drones.FirstOrDefault(x => Surface.DroneOf(x).Bay == bay);
            if (u == null)
            {
                padRows.Add(($"Bay {bay + 1}", stock > 0 ? "empty: loading a drone from stock" : "empty"));
                continue;
            }
            var d = Surface.DroneOf(u);
            if (d.State == DroneState.Docked) docked++;
            if (d.State == DroneState.Docked && d.Battery >= Fix64.One) ready++;
            if (Airborne(d.State)) airborne++;
            padRows.Add(($"Bay {bay + 1}", $"{CallSign(u)}  {Gauge(d.Battery)}  ·  {DroneStateText(u, d)}"));
        }
        padRows.Add(("Drones", stock > 0 ? $"{stock} in stock" : "none in stock: make scout drones at a Machine Shop"));
        rows.InsertRange(0, padRows);
        while (rows.Count > 6) rows.RemoveAt(rows.Count - 1);

        if (!complete) return;
        if (s.Status is StructureStatus.Working or StructureStatus.LowPower or StructureStatus.NoInput)
        {
            status = airborne > 0 ? $"{airborne} drone{(airborne == 1 ? "" : "s")} out · {docked} docked" : docked > 0 ? $"{docked} docked · {ready} ready" : "No drones: make them at a Machine Shop";
            statusColour = docked + airborne > 0 ? RoverGood : new Color(0.6f, 0.65f, 0.75f);
        }
        var launch = docked == 0 ? (stock > 0 ? "loading from stock" : "no drone docked") : _launchPad == s.Id ? "pick a tile…" : "pick a tile in coverage";
        actions.Insert(0, new ActionView("drone_launch", _launchPad == s.Id ? "LAUNCHING… [L]" : "LAUNCH [L]", launch, docked > 0));
        if (airborne > 0) actions.Insert(1, new ActionView("drone_recall", "RECALL ALL [R]", $"{airborne} out", true));
        while (actions.Count > 4) actions.RemoveAt(actions.Count - 1); // X still salvages, M maintains
    }

    /// <summary>ShowUnits: one drone gets the rover-style panel; several drones (only) a summary. True = shown.</summary>
    private bool ShowDrones(IReadOnlyList<SurfaceUnit> units)
    {
        if (units.Count == 0 || units.Any(u => !IsDrone(u))) return false;
        if (units.Count > 1)
        {
            var sb = new StringBuilder();
            foreach (var u in units.Take(6))
            {
                var d = Surface.DroneOf(u);
                sb.Append(CallSign(u)).Append("  ").Append(Gauge(d.Battery)).Append("  ·  ").Append(DroneStateText(u, d)).Append('\n');
            }
            if (units.Count > 6) sb.Append($"+{units.Count - 6} more\n");
            sb.Append("Right-click: fly there (inside comms coverage)  ·  R: call them home  ·  G: coverage");
            _hud.ShowDetail($"{units.Count} scout drones", "Selected", sb.ToString());
            return true;
        }
        var drone = units[0];
        var info = Surface.DroneOf(drone);
        var def = _data.SurfaceUnit(drone.DefId);
        var droneDef = _data.DroneOf(drone.DefId);
        float? batterySeconds = droneDef?.BatterySeconds.ToFloat();
        float? recharge = droneDef?.RechargeSeconds.ToFloat();
        var battery = Mathf.Clamp(info.Battery.ToFloat(), 0f, 1f);
        var (status, colour) = info.State switch
        {
            DroneState.Lost => ("Lost: the battery ran out", RoverBad),
            DroneState.Returning when info.ReturnReason is "battery" or "coverage" => (Capital(DroneStateText(drone, info)), RoverWarn),
            DroneState.Docked => (Capital(DroneStateText(drone, info)), info.Battery >= Fix64.One ? RoverGood : DroneBlue),
            _ => (Capital(DroneStateText(drone, info)), RoverGood),
        };
        var rows = new List<(string, string)>
        {
            ("State", DroneStateText(drone, info)),
            ("Target", info.Target >= 0 ? $"{Surface.Map.Terrain(info.Target).Name} · {Surface.Map.Grid.Distance(drone.Tile, info.Target)} tiles" : "—"),
            ("Pad", Surface.FindStructure(info.Pad) is { } pad ? $"{_data.Building(pad.DefId).Name} · bay {info.Bay + 1} · {Surface.Map.Grid.Distance(drone.Tile, pad.Tile)} tiles" : "none: it lands on any free bay of yours"),
            ("Comms", Surface.CommsCovered(LocalPlayer, drone.Tile) ? "in coverage" : "OUT OF COVERAGE: it turns home"),
            ("Survey", $"{Surface.SurveyRadiusOf(drone)} tiles around it as it flies"),
        };
        var left = batterySeconds is { } bs ? $"  ·  ~{Mathf.FloorToInt(bs * battery) / 60}:{Mathf.FloorToInt(bs * battery) % 60:00} of flight" : "";
        var conditionText = $"{Mathf.RoundToInt(battery * 100)}% battery{left}";
        var maintenance = info.State == DroneState.Docked
            ? (battery >= 0.999f ? "charged" : recharge is { } rs ? $"charging on the pad ({Mathf.RoundToInt(rs)} s from flat)" : "charging on the pad")
            : "turns home by itself when what is left only just covers the way back";
        var progress = -1f;
        var progressLabel = "";
        var progressText = "";
        if (info.State is DroneState.Launching or DroneState.Landing or DroneState.Loitering)
        {
            progress = Mathf.Clamp(info.Phase.ToFloat(), 0f, 1f);
            progressLabel = info.State == DroneState.Launching ? "Lift-off" : info.State == DroneState.Landing ? "Landing" : "Loiter";
            progressText = $"{Mathf.RoundToInt(progress * 100)}%";
        }
        var actions = new List<ActionView>();
        var canReturn = info.State is DroneState.Launching or DroneState.Flying or DroneState.Loitering;
        actions.Add(new ActionView("drone_recall", "RETURN [R]", canReturn ? "fly home now" : info.State == DroneState.Returning ? "on its way" : "not out", canReturn));
        if (info.State == DroneState.Docked)
            actions.Add(new ActionView("drone_launch", _launchPad == info.Pad ? "LAUNCHING… [L]" : "LAUNCH [L]", "pick a tile in coverage", battery > 0.2f));
        _hud.ShowStructure(new StructureView(UnitName(drone), $"{TypeName(drone)}  ·  over {Surface.Map.Terrain(drone.Tile).Name.ToLowerInvariant()}", "rover",
            status, colour, rows, battery * 100f, conditionText, maintenance, progress, progressLabel, progressText, actions));
        return true;
    }

    /// <summary>Context hints (ContextHints) for a launch pick, a selected Drone Pad or selected drones; null = none.</summary>
    private string? DroneHints()
    {
        if (_launchPad != 0) return "Click a tile inside coverage  ·  Shift+click launch more  ·  right-click / Esc stop  ·  G coverage  ·  F1 controls";
        if (SelectedPad() != null && _view.SelectedUnits.Count == 0) return "L launch a drone  ·  R recall all  ·  G coverage  ·  M maintain  ·  P pause  ·  X salvage  ·  Esc back  ·  F1 controls";
        if (OnlyDronesSelected()) return "Right-click fly there (inside coverage)  ·  R return  ·  L launch (docked)  ·  G coverage  ·  Esc back  ·  F1 controls";
        return null;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Drone panels are inside the HUD (nothing of their own over the map).</summary>
    private bool DroneUiBlocksMouse(Vector2 mouse) => false;

    // ---------------------------------------------------------------- per frame

    private void UpdateDrones()
    {
        if (_launchPad != 0 && (_inOrbit || _placing != null || _founding != 0 || Surface.FindStructure(_launchPad) is not { } pad || pad.Owner != LocalPlayer))
            StopLaunch();
        DroneEvents();

        // V1's world view: the coverage overlay (G, while a launch is armed, while drones are selected) and the selected
        // drone's target ring (or the tile a launch would go to).
        var drones = _inOrbit ? new List<SurfaceUnit>() : SelectedDrones();
        _view.ShowComms(!_inOrbit && _commsOverlay);
        _view.ArmComms(!_inOrbit && (_launchPad != 0 || drones.Count > 0));
        var target = -1;
        if (_launchPad != 0) target = _hoverTile;
        else if (drones.Count == 1 && Surface.DroneOf(drones[0]) is { } d && Airborne(d.State)) target = d.Target;
        _view.ShowDroneTarget(target, _launchPad == 0 || target < 0 || Surface.LaunchProblem(_sim, LocalPlayer, _launchPad, target) == null);
    }

    /// <summary>Event lines when one of our drones changes state.</summary>
    private void DroneEvents()
    {
        foreach (var u in Surface.Units)
        {
            if (u.Owner != LocalPlayer) continue;
            var d = Surface.DroneOf(u);
            if (d.State == DroneState.None) continue;
            if (_droneSeen.TryGetValue(u.Id, out var was) && was != d.State)
            {
                var name = UnitName(u);
                switch (d.State)
                {
                    case DroneState.Launching:
                        ColonyReport($"{name} launched", DroneBlue);
                        break;
                    case DroneState.Loitering:
                        ColonyReport($"{name} over its target: circling and surveying", DroneBlue);
                        break;
                    case DroneState.Returning when d.ReturnReason == "battery":
                        ColonyReport($"{name} heading home: battery low", RoverWarn);
                        break;
                    case DroneState.Returning when d.ReturnReason == "coverage":
                        ColonyReport($"{name} out of comms coverage: heading home", RoverWarn);
                        break;
                    case DroneState.Docked when was is DroneState.Landing or DroneState.Returning:
                        ColonyReport($"{name} back on its pad: recharging", RoverGood);
                        break;
                    case DroneState.Lost:
                        ColonyReport($"{name} lost: its battery ran out", RoverBad);
                        break;
                }
            }
            else if (!_droneSeen.ContainsKey(u.Id) && d.State == DroneState.Docked && _sim.Tick > Simulation.TickRate * 5)
                ColonyReport($"{UnitName(u)} docked on the Drone Pad: ready once charged", DroneBlue);
            _droneSeen[u.Id] = d.State;
        }
    }
}
