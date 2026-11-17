using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Finding your vehicles (owner 2026-11-17: "We need a way to find vehicles on the surface, there are too many rocks and
// things to find them. Maybe a "Ping" to show where they are."). Render-only: nothing here goes to the sim.
//
// - Ping: Z, or PING on the survey map's caption. For PingSeconds every vehicle of the local player on the map gets a
//   beam with its name, rings on the ground and a flash on its minimap dot; the ones off screen get an arrow at the rim
//   of the view with the name and the distance (PingOverlay.cs, drawn over the scene, so scatter never hides it).
//   Rovers, crawlers (folded or deployed), construction rovers, survey teams and scout drones count; vehicles riding
//   in a drop ship don't; never another player's. Pressing again starts it over. One quiet blip.
// - Vehicle list (VehicleList.cs): up while the ping is, bottom-left over the minimap. Z twice quickly, or its PIN
//   button, keeps it open; × closes it. A click on a row selects the vehicle and flies the camera to it; the mouse on
//   a row keeps that vehicle's beacon up. Hidden under the build drawer, a building's card and the inventory.
//
// While nothing is up (no ping, list not pinned) UpdatePing does nothing.
public partial class PlanetDriver
{
    private const Key PingKey = Key.Z;
    private const string PingKeyName = "Z";
    private const float PingSeconds = 6f;
    private const double PinTapSeconds = 0.45; // two presses within this pin (or unpin) the list
    private const double PingFactsEvery = 0.25;

    private static readonly Color PingBusy = new(0.62f, 0.82f, 1f);   // driving, flying
    private static readonly Color PingWork = new(0.56f, 0.83f, 0.69f); // digging, surveying, founding
    private static readonly Color PingIdle = new(0.6f, 0.65f, 0.75f);
    private static readonly Color PingBad = new(1f, 0.62f, 0.3f);

    private PingOverlay _pingOverlay = null!;
    private VehicleList _vehicleList = null!;
    private double _pingAt = -1000.0;    // _clock when the ping began
    private double _pingKeyAt = -1000.0;
    private double _pingFactsAt = -1000.0;
    private bool _pingPinned;
    private readonly List<PingTarget> _pingTargets = new();
    private readonly Dictionary<int, PingMark> _pingMarks = new();
    private readonly List<PingMark> _pingShown = new();
    private readonly List<SurfaceUnit> _pingUnits = new();
    private readonly List<VehicleList.Row> _pingRows = new();
    private readonly List<int> _pingGone = new();

    private bool PingUp => _clock - _pingAt < PingSeconds;

    private void SetupPing()
    {
        _pingOverlay = new PingOverlay { Name = "PingOverlay" };
        _hud.AddUnderlay(_pingOverlay);
        _vehicleList = new VehicleList { Name = "VehicleList" };
        _hud.AddOverlay(_vehicleList); // above the bottom panel, under F1 help and the tooltip
        _vehicleList.Picked += PickVehicle;
        _vehicleList.PinPressed += () => _pingPinned = !_pingPinned;
        _vehicleList.ClosePressed += () =>
        {
            _pingPinned = false;
            _pingAt = -1000.0;
        };
        _hud.AddPingButton(PingKeyName, StartPing);
    }

    /// <summary>The mouse is on the vehicle list (so the map doesn't react to it).</summary>
    private bool PingBlocksMouse(Vector2 mouse) => _vehicleList.IsOverUi(mouse);

    /// <summary>Z on the surface: ping; twice quickly also pins (or unpins) the vehicle list. True when the key was used.</summary>
    private bool PingInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: PingKey } key) return false;
        if (key.CtrlPressed || key.MetaPressed || key.AltPressed) return false; // Ctrl+Z is the test panel's undo
        var twice = _clock - _pingKeyAt < PinTapSeconds;
        _pingKeyAt = twice ? -1000.0 : _clock;
        if (twice)
        {
            _pingPinned = !_pingPinned;
            _hud.PushEvent(_pingPinned ? $"Vehicle list pinned  ·  {PingKeyName} twice or its PIN button lets it go" : "Vehicle list unpinned", PingBusy);
        }
        StartPing();
        return true;
    }

    /// <summary>Starts the ping (again: from the beginning).</summary>
    private void StartPing()
    {
        if (_inOrbit) return;
        var count = 0;
        var units = Surface.Units;
        for (var i = 0; i < units.Count; i++)
            if (units[i].Owner == LocalPlayer) count++;
        if (count == 0)
        {
            _hud.PushEvent(Me.Phase == LandingPhase.Landed ? "No vehicles to find: a Vehicle Bay builds them" : "No vehicles on the surface yet", new Color(1f, 0.7f, 0.42f));
            return;
        }
        _pingAt = _clock;
        _pingFactsAt = -1000.0;
        Sfx.Ui("tutorial", -13f, 1.6f); // one quiet, high blip
    }

    /// <summary>Once a frame from _Process, after the view's Refresh (vehicle positions) and the camera.</summary>
    private void UpdatePing(float delta)
    {
        var pinging = !_inOrbit && PingUp;
        var listWanted = !_inOrbit && (pinging || _pingPinned) && Me.Phase == LandingPhase.Landed
                         && !_drawer.IsOpen && !_bigCard.IsOpen && !_inventory.IsOpen;
        _hud.Minimap.PingAge = pinging ? (float)(_clock - _pingAt) : -1f;
        if (!pinging && !listWanted)
        {
            _pingOverlay.Clear();
            _vehicleList.Close();
            return;
        }

        // Where each vehicle is drawn this frame.
        _view.PingTargets(_pingTargets);
        foreach (var mark in _pingMarks.Values) mark.Live = false;
        _pingShown.Clear();
        for (var i = 0; i < _pingTargets.Count; i++)
        {
            var t = _pingTargets[i];
            if (!_pingMarks.TryGetValue(t.Id, out var mark))
            {
                if (Surface.FindUnit(t.Id) is not { } u) continue;
                mark = new PingMark { Id = u.Id, Name = HullName(u), Kind = TypeName(u), Alert = PingAlert(u) };
                _pingMarks[u.Id] = mark;
            }
            mark.At = t.At;
            mark.GroundY = t.GroundY;
            mark.Top = t.Top;
            mark.Live = true;
            _pingShown.Add(mark);
        }

        // A few times a second: who is in trouble, who is gone, and the list's rows.
        if (_clock - _pingFactsAt >= PingFactsEvery)
        {
            _pingFactsAt = _clock;
            PingFacts(listWanted);
        }

        var solo = listWanted ? _vehicleList.HoveredId : 0;
        if (pinging || solo != 0)
            _pingOverlay.Present(_camera, _pingShown, pinging ? (float)(_clock - _pingAt) : -1f, PingSeconds, solo, _focus, _hud.BottomNow, delta);
        else _pingOverlay.Clear();

        if (listWanted)
        {
            var screen = GetViewport().GetVisibleRect().Size;
            var s = HudKit.ScaleFor(screen);
            var room = screen.Y - PlanetHud.TopHeight - _hud.BottomNow - 200f - 40f * s;
            var maxRows = Mathf.Clamp(Mathf.FloorToInt(room / (24f * s)), 3, 10);
            _vehicleList.Present(_pingRows, _pingPinned, new Vector2(_hud.PingListLeft, screen.Y - _hud.BottomNow - 12f), maxRows, screen);
        }
        else _vehicleList.Close();
    }

    /// <summary>The local player's vehicles in list order (by type, then the order they arrived), each mark's trouble
    /// flag, and marks of vehicles that are gone.</summary>
    private void PingFacts(bool rows)
    {
        _pingUnits.Clear();
        var units = Surface.Units;
        for (var i = 0; i < units.Count; i++)
            if (units[i].Owner == LocalPlayer) _pingUnits.Add(units[i]);
        _pingUnits.Sort(static (a, b) =>
        {
            var byType = string.CompareOrdinal(a.DefId, b.DefId);
            return byType != 0 ? byType : a.Id.CompareTo(b.Id);
        });

        _pingGone.Clear();
        foreach (var id in _pingMarks.Keys)
            if (Surface.FindUnit(id) == null) _pingGone.Add(id);
        for (var i = 0; i < _pingGone.Count; i++) _pingMarks.Remove(_pingGone[i]);

        _pingRows.Clear();
        for (var i = 0; i < _pingUnits.Count; i++)
        {
            var u = _pingUnits[i];
            _pingMarks.TryGetValue(u.Id, out var mark);
            if (mark != null) mark.Alert = PingAlert(u);
            if (!rows) continue;
            var (state, colour) = VehicleState(u);
            // The names are kept on the mark once it has one (a vehicle still in its drop ship has none yet).
            _pingRows.Add(new VehicleList.Row { Id = u.Id, Kind = mark?.Kind ?? TypeName(u), Name = mark?.Name ?? HullName(u), State = state, StateColour = colour });
        }
    }

    /// <summary>The name painted on the hull, as painted ("TALUS 17"); the type for a vehicle without one.</summary>
    private string HullName(SurfaceUnit u) => string.IsNullOrEmpty(u.Name) ? TypeName(u).ToUpperInvariant() : $"{u.Name} {u.Number}";

    private bool PingAlert(SurfaceUnit u) => u.Broken || (Surface.IsAerial(u) && Surface.DroneOf(u).State == DroneState.Lost);

    /// <summary>What a vehicle is doing, in a word or two, and the colour for it.</summary>
    private (string, Color) VehicleState(SurfaceUnit u)
    {
        if (Surface.IsAerial(u))
        {
            var drone = Surface.DroneOf(u);
            return drone.State switch
            {
                DroneState.Docked => drone.Battery.ToFloat() < 0.995f ? ("charging", PingIdle) : ("docked", PingIdle),
                DroneState.Launching => ("launching", PingBusy),
                DroneState.Flying => ("flying", PingBusy),
                DroneState.Loitering => ("surveying", PingWork),
                DroneState.Returning => ("returning", PingBusy),
                DroneState.Landing => ("landing", PingBusy),
                DroneState.Lost => ("lost", PingBad),
                _ => ("idle", PingIdle),
            };
        }
        if (_view.RidingDropShip(u.Id)) return ("in the drop ship", PingIdle);
        var def = _data.SurfaceUnit(u.DefId);
        if (u.Broken) return (def.CanMine ? "broken down" : "disabled", PingBad);
        if (Surface.Unfolding(u)) return ("founding", PingWork);
        switch (u.Mode)
        {
            case UnitMode.Deploying: return ("unfolding", PingWork);
            case UnitMode.Packing: return ("packing up", PingBusy);
            case UnitMode.Mobile: break;
            default:
                return u.MiningStatus switch
                {
                    MiningStatus.Mining => ("digging", PingWork),
                    MiningStatus.StorageFull => ("storage full", PingBad),
                    MiningStatus.NoDestination => ("no storage", PingBad),
                    MiningStatus.Depleted => ("deposit used up", PingBad),
                    _ => ("deployed", PingWork),
                };
        }
        if (u.TyreChangeTicks > 0) return ("changing a tyre", PingBusy);
        if (u.Path.Count > 0) return (u.WaitTicks > 0 ? "waiting" : "driving", PingBusy);
        if (Surface.IsRepairing(u)) return (u.RepairWaiting ? "waiting for kits" : "repairing", u.RepairWaiting ? PingBad : PingBusy);
        if (u.FlatTyres != 0) return ("flat tyre", PingBad);
        return ("parked", PingIdle);
    }

    /// <summary>A row of the vehicle list: select that vehicle and fly to it.</summary>
    private void PickVehicle(int unitId)
    {
        if (_inOrbit || Surface.FindUnit(unitId) is not { } u || u.Owner != LocalPlayer) return;
        if (_placing != null) StopPlacing();
        if (_founding != 0) StopFounding();
        ClearSelection();
        _view.SelectedUnits.Add(u.Id);
        ArmSelectionSound();
        FlyTo(u.Tile, Mathf.Clamp(_distanceTarget, 70f, 240f));
        if (_view.UnitModel(u.Id) is { } model) _focusTarget = model.Position with { Y = 0 }; // where it really stands on its tile
    }
}
