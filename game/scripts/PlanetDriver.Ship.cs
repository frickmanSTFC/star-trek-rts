using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

// The flagship's 3D model on the helm (docs/ship-model-contract.md "Orbit view"; render-only, no commands): the SHIP
// entry on the rail (Planet and Orbit displays, while the ship has a model and is in orbit) and a click on the ship in
// the 3D view make the camera fly to it and follow it (OrbitView.Ship.cs); SHIP again, Esc, a drag or WASD, a click on
// the planet, or arming a deployment lets go. The wheel zooms the follow view. Hover shows a ring and a readout.
// Debug builds: Shift+F11 swaps a primitive stand-in in for a missing glb (ShipModels.DebugStandIn).
// On the System display our flagship's card has VIEW SHIP, which switches to the planet display and follows it.
// Hooks in PlanetDriver.cs: OrbitHover → ShipHover, OrbitClick → ShipClick, OrbitKey (Esc, Shift+F11), OrbitHud
// (hint, rail, readout), OnHelmAction "ship"; in PlanetDriver.System.cs: ShipCard → AddShipViewButton.
public partial class PlanetDriver
{
    private bool _shipHover;

    /// <summary>What the helm calls our ship: the flagship's name, else the picked ship's, else the faction's ship in orbit.</summary>
    private string FlagshipName =>
        _system.Flagship?.Name ?? _sim.ShipChoiceOf(LocalPlayer)?.Name ?? _data.Orbit?.ShipFor(_sim.Players[LocalPlayer].FactionId)?.Name ?? "Your ship";

    /// <summary>Hover over the ship (OrbitHover, after the satellites): only with nothing armed, and it takes the hover
    /// from the tile under it. Arming a deployment or a retask lets go of the follow: those aim at the globe.</summary>
    private void ShipHover(Vector2 mouse)
    {
        if (_orbitAction != null || _retasking) _orbit.FollowShip = false;
        _shipHover = !_mouseOverUi && !_retasking && _orbitAction == null && _satHover == null && !(_pressed && _dragging) && !_orbit.FollowShip && _orbit.PickFlagship(mouse);
        _orbit.HoverShip = _shipHover;
        if (_shipHover) _orbitHover = -1;
    }

    /// <summary>A click on the ship (OrbitClick, after the satellites): follow it. True when the click was used.</summary>
    private bool ShipClick()
    {
        if (!_shipHover || _orbitAction != null) return false;
        FollowShip();
        return true;
    }

    /// <summary>SHIP on the rail: follow the ship, or let go when already following.</summary>
    private void ToggleShipFollow()
    {
        if (_orbit.FollowShip) _orbit.FollowShip = false;
        else FollowShip();
    }

    private void FollowShip()
    {
        if (!_orbit.ShipAvailable)
        {
            Sfx.Refused();
            _hud.PushEvent(_system.FlagshipInFlight ? $"Can't show the {FlagshipName}: it is under way" : $"Can't show the {FlagshipName}: no 3D model for it yet", new Color(1f, 0.7f, 0.42f));
            return;
        }
        // The follow view looks at the ship, not the ground: nothing stays armed that a stray click would send down.
        _orbitAction = null;
        _retasking = false;
        if (_info != null && _info.StartsWith("orbit:")) _info = null;
        if (_orbit.Display is not (OrbitDisplay.Planet or OrbitDisplay.Orbit)) SetOrbitDisplay(OrbitDisplay.Planet);
        _orbit.FollowShip = true;
        Sfx.Ui("open");
    }

    /// <summary>The rail's SHIP entry (cyan while following), on the displays that show the ship.</summary>
    private void AddShipRail(List<HelmAction> rail, OrbitDisplay display)
    {
        if (display is OrbitDisplay.Planet or OrbitDisplay.Orbit && _orbit.ShipAvailable)
            rail.Add(new HelmAction("ship", "", "SHIP", Colour: _orbit.FollowShip ? HelmScreen.Cyan : null));
    }

    /// <summary>The System display's card for our flagship: VIEW SHIP switches to the planet display and follows it.</summary>
    private void AddShipViewButton(FinalFrontier.Sim.Space.SystemShip s, List<HelmAction> buttons)
    {
        if (s.Owner == LocalPlayer && s.Id == _system.Flagship?.Id && _orbit.ShipAvailable)
            buttons.Add(new HelmAction("ship", "", "VIEW SHIP", Colour: HelmScreen.Peach));
    }

    /// <summary>The helm title's hint while the camera follows the ship, else null.</summary>
    private string? ShipFollowHint() =>
        _orbit.FollowShip ? $"FOLLOWING {FlagshipName.ToUpperInvariant()} · WHEEL ZOOMS · DRAG OR ESC LETS GO" : null;

    /// <summary>The cursor readout over the ship (OrbitHud). True when it showed one.</summary>
    private bool ShipReadout(Vector2 mouse)
    {
        if (!_shipHover) return false;
        _helm.ShowReadout(FlagshipName.ToUpperInvariant(), $"{ShipLine()}\nClick to follow it close up (or SHIP on the left)", HelmScreen.Peach, mouse + new Vector2(18, 14));
        return true;
    }

    /// <summary>Debug builds, Shift+F11 in orbit: ships without a glb get the primitive stand-in (and back).</summary>
    private void ToggleShipStandIn()
    {
        ShipModels.DebugStandIn = !ShipModels.DebugStandIn;
        _orbit.RebuildShip();
        _hud.PushEvent(ShipModels.DebugStandIn ? "Debug: stand-in ship model on for ships without a glb" : "Debug: stand-in ship model off", new Color(1f, 0.6f, 0.2f));
    }
}
