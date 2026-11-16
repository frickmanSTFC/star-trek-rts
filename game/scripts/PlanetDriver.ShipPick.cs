using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// The campaign's ship pick in the game (docs/ship-pick-contract.md C3/C4; render-only + the existing commands). A ship
// that colonizes plays as before. Any other ship never lands (Simulation.CanColonize false): the game opens on the helm's
// SYSTEM display with nothing armed, the landing / outpost / drop ship / beam cards are gone from the strip and the keys
// (OrbitCards), and if one is asked for anyway the sim's ColonizeProblem is the reason; there is no way to the surface
// (no SURFACE / FEED pills, O ignored, no landing camera). The mission chain's markers are drawn on the system plot
// (SystemPlot.Markers.cs, from MissionBook.SpaceMarkers) and the cargo aboard (MissionBook.CargoAboard) is listed on the
// flagship's card and in the SYSTEM DATA panel. The tutorial swaps its landing steps for three space ones (Tutorial.cs).
public partial class PlanetDriver
{
    /// <summary>This player's ship never lands (a non-colonizer from the campaign's ship pick).</summary>
    private bool SpaceShip => _sim != null && !_sim.CanColonize(LocalPlayer);

    /// <summary>An orbit card that lands or sends something down to the colony's surface ("land", "outpost",
    /// "shuttle:&lt;unit&gt;", "beam:&lt;payload&gt;"); the survey satellite and relays stay in orbit, so they aren't.</summary>
    private static bool IsColonyCard(string action) =>
        action is "land" or "outpost" || action.StartsWith("shuttle:") || action.StartsWith("beam:");

    /// <summary>From _Ready, right after EnterOrbit, for a ship that never lands: the SYSTEM display, the flagship's
    /// body selected, a line in the feed saying what kind of game this is.</summary>
    private void BeginSpaceStart()
    {
        _orbitAction = null;
        _info = null;
        SetOrbitDisplay(OrbitDisplay.System);
        if (_system.OrbitBodyId is { } here)
        {
            _selectedBody = here;
            _orbit.HighlightBody(here);
        }
        var ship = _sim.ShipChoiceOf(LocalPlayer);
        var at = _system.Body(_system.OrbitBodyId) is { } b ? _system.KnownName(b) : "the system";
        _hud.PushEvent($"The {ship?.Name ?? "ship"} is in orbit of {at}. It never lands: its missions play out on the system display.", HelmScreen.Peach);
    }

    // ---------------------------------------------------------------- cargo

    /// <summary>The mission cargo aboard our flagship (MissionBook.CargoAboard), never throwing (a stub or half-merged
    /// sim gives none).</summary>
    private IReadOnlyList<string> CargoAboard()
    {
        try
        {
            return _sim.Missions.CargoAboard(LocalPlayer);
        }
        catch (System.Exception)
        {
            return System.Array.Empty<string>();
        }
    }

    /// <summary>The flagship card's cargo rows: one per mission cargo aboard (nothing when there is none).</summary>
    private void AddCargoRows(SystemShip s, List<HelmRow> rows)
    {
        if (s.Owner != LocalPlayer || s.Id != _system.Flagship?.Id) return;
        var cargo = CargoAboard();
        for (var i = 0; i < cargo.Count; i++)
            rows.Add(new HelmRow(i == 0 ? "ABOARD" : "", cargo[i], MissionGold));
    }

    /// <summary>The SYSTEM DATA panel's MISSION CARGO section while the flagship carries any.</summary>
    private void AddCargoSection(List<HelmSection> sections)
    {
        var cargo = CargoAboard();
        if (cargo.Count == 0) return;
        sections.Add(new HelmSection("MISSION CARGO", MissionGold, cargo.Select(c => new HelmRow("", c, MissionGold)).ToList()));
    }

    // ---------------------------------------------------------------- tracker → plot

    /// <summary>The tracker's "show on map" for a game that never lands: the SYSTEM display with the first body the
    /// space markers name selected. False when there is none.</summary>
    private bool LocateSpaceMarker()
    {
        if (!SpaceShip || !_inOrbit) return false;
        IReadOnlyList<SpaceMarker> markers;
        try
        {
            markers = _sim.Missions.SpaceMarkers(LocalPlayer);
        }
        catch (System.Exception)
        {
            return false;
        }
        foreach (var k in markers)
        {
            if (k.Kind == "ship" && k.ShipId >= 0 && _system.Ship(k.ShipId) != null)
            {
                if (_orbit.Display != OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.System);
                _selectedShip = k.ShipId;
                _systemCard = $"ship:{k.ShipId}";
                _orbit.HighlightShip(k.ShipId);
                return true;
            }
            if (k.BodyId != null && _system.Body(k.BodyId) is { } b && _system.Known(b.Id))
            {
                if (_orbit.Display != OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.System);
                SelectSystemBody(b.Id);
                return true;
            }
        }
        return false;
    }
}
