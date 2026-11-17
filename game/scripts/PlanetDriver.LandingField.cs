using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The landing field in the HUD and the helm (owner 2026-11-17: drop ships "land in an area near the outpost to fly back
// up after re-fueling"; sim/Surface/LandingField.cs): where the delivery drop ship is while it is away ("refuelling at
// the landing field · 0:32") for the orbit strip's DROP pills and the drop-ship cards, a pad's line in the tile tooltip,
// and event-feed lines when a drop ship of ours has to wait (no free pad, no fuel) and when the delivery drop ship is
// back aboard. Render-only: it reads the sim's DropShips.
public partial class PlanetDriver
{
    private static readonly Color FieldBlue = new(0.62f, 0.82f, 1f), FieldWarn = new(1f, 0.72f, 0.3f);

    /// <summary>What our drop ships were doing last time we looked, by id (the feed reports changes).</summary>
    private readonly Dictionary<int, DropShipState> _dropShipStates = new();
    private readonly List<int> _dropShipsGone = new();
    private bool _shuttleWasAway;

    /// <summary>Where our delivery drop ship is while it is away ("refuelling at the landing field · 0:32"), or null
    /// when it is aboard.</summary>
    private string? DropShipWhere() => Surface.ShuttleAway(LocalPlayer, _sim.Tick) ? Surface.ShuttleWhere(LocalPlayer, _sim.Tick) ?? "away" : null;

    /// <summary>The short tag on the orbit strip's DROP pills while the drop ship is away.</summary>
    private string DropShipTag()
    {
        var ship = Surface.ShuttleShip(LocalPlayer);
        var word = ship?.State switch
        {
            null => "DOWN",
            DropShipState.Unloading => "UNLOADING",
            DropShipState.WaitingForPad => "NO PAD",
            DropShipState.Hopping => "TO FIELD",
            DropShipState.WaitingForFuel => "NO FUEL",
            DropShipState.Refuelling => "FUELLING",
            _ => "CLIMBING",
        };
        // The clock is the estimate until it is back aboard; a waiting ship has none worth showing.
        var waiting = ship?.State is DropShipState.WaitingForPad or DropShipState.WaitingForFuel;
        var left = Surface.Players[LocalPlayer].ShuttleBackTick - _sim.Tick;
        return waiting || left <= 0 ? $" {word}" : $" {word} {PlanetSurface.Clock(left)}";
    }

    /// <summary>The drop-ship card's rows: the flight, the stay at the landing field, the fuel.</summary>
    private List<(string, string)> DropShipRows(OrbitRules orbit)
    {
        var rows = new List<(string, string)>();
        var fuel = Surface.ShuttleFuel;
        if (_data.LandingField is { } field)
        {
            var back = (Surface.DescentTicks(orbit.ShuttleSeconds) + Surface.GroundStayTicks(LocalPlayer, field.Distance)) / Simulation.TickRate;
            rows.Add(("Flight", $"{orbit.ShuttleSeconds} s down; it unloads, hops to the landing field by your base, refuels there ({Surface.RefuelTicks(LocalPlayer) / Simulation.TickRate} s) and climbs back"));
            rows.Add(("Away", $"about {PlanetSurface.Clock(back * Simulation.TickRate)} before it can fly again (longer if every pad is taken or the fuel is short)"));
            if (fuel.Count > 0) rows.Add(("Fuel", $"{Amounts(fuel, false)}, taken from the colony when it refuels on its pad"));
        }
        else
        {
            rows.Add(("Flight", $"{orbit.ShuttleSeconds} s down, {orbit.ShuttleSeconds} s back up before it can fly again"));
            if (fuel.Count > 0) rows.Add(("Fuel", Amounts(fuel, false)));
        }
        return rows;
    }

    /// <summary>A pad tile's line in the tile tooltip ("\nLanding pad 2 · Base 1 · a drop ship is refuelling …"), or "".</summary>
    private string LandingPadTooltip(int tile)
    {
        var coreId = Surface.PadCoreAt(tile);
        if (coreId == 0 || Surface.FindStructure(coreId) is not { } core || !_view.Shows(core)) return "";
        var number = 1;
        for (var i = 0; i < core.Field.Count; i++)
            if (core.Field[i] == tile) number = i + 1;
        var text = $"\nLanding pad {number}" + (core.Owner == LocalPlayer ? $" · {BaseName(core.Id)}" : "") + " · nothing can be built here";
        if (Surface.ShipOnPad(tile) is { } ship && _view.ShowsLive(ship.Owner, tile))
            text += $"\nDrop ship: {Surface.DropShipStatus(ship, _sim.Tick)}";
        else text += "\nFree: drop ships refuel here before they fly back up";
        return text;
    }

    /// <summary>Feed lines for our drop ships' stay on the ground: a ship that has to wait (for a pad, for fuel), fuel
    /// taken as one starts refuelling, and the delivery drop ship back aboard.</summary>
    private void DropShipEvents()
    {
        _dropShipsGone.Clear();
        foreach (var id in _dropShipStates.Keys) _dropShipsGone.Add(id);
        foreach (var ship in Surface.DropShips)
        {
            if (ship.Owner != LocalPlayer) continue;
            _dropShipsGone.Remove(ship.Id);
            var known = _dropShipStates.TryGetValue(ship.Id, out var was);
            if (known && was == ship.State) continue;
            _dropShipStates[ship.Id] = ship.State;
            var which = ship.Role == DropShipRole.Delivery ? "The drop ship" : "A drop ship of the convoy";
            switch (ship.State)
            {
                case DropShipState.WaitingForPad:
                    ColonyReport($"{which} is waiting where it unloaded: every pad of the landing field is taken", FieldWarn);
                    break;
                case DropShipState.WaitingForFuel:
                    ColonyReport($"{which} is on its pad without fuel: it needs {Amounts(Surface.ShuttleFuel, false)} to fly back up", FieldWarn);
                    break;
                case DropShipState.Refuelling when ship.Role == DropShipRole.Delivery:
                    ColonyReport($"The drop ship is refuelling at the landing field ({Amounts(Surface.ShuttleFuel, false)}) · back aboard in about {PlanetSurface.Clock(Surface.Players[LocalPlayer].ShuttleBackTick - _sim.Tick)}", FieldBlue);
                    break;
                case DropShipState.Hopping when ship.Role == DropShipRole.Convoy && ship.Slot == 0 && known:
                    ColonyReport("The landing convoy is moving to the landing field to refuel before it flies back up", FieldBlue);
                    break;
            }
        }
        foreach (var id in _dropShipsGone) _dropShipStates.Remove(id);

        var away = Surface.ShuttleAway(LocalPlayer, _sim.Tick);
        if (_shuttleWasAway && !away) ColonyReport("The drop ship is back aboard: ready for another drop", FieldBlue);
        _shuttleWasAway = away;
    }
}
