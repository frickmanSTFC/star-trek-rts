using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Comms relay satellites on the helm (docs/missions-contract.md B "UI"; commands only): a RELAY pill on the orbit
// action strip next to SATELLITE ("orbit:relay", key 4) arms a click on the globe (green / red hover by
// PlanetSurface.RelayProblem, the reason in the cursor readout and on the info card, the coverage disc previewed by
// agent V1's OrbitView through `Seam`); the click sends RelaySatelliteCommand. Relays are listed in the SATELLITES
// window (their own rows: station, status, coverage, which bases and drones they reach) and picked there the globe
// turns to the station. Relay satellites come from PlanetSurface.CommsSources (Kind Relay), so this works before and
// after S1 adds Satellite.Kind. Numbers the sim agent adds to the data (relay cost, max, radius) are read by
// reflection until the merge (TODO(merge S1)).
public partial class PlanetDriver
{
    private readonly HashSet<int> _relaysReady = new();
    private readonly HashSet<int> _relaysKnown = new();

    /// <summary>OrbitCards: the RELAY card right after the survey satellite.</summary>
    private void AddRelayCard(List<(string Id, string Name)> list) => list.Add(("orbit:relay", "Relay Satellite"));

    private IEnumerable<CommsSource> MyRelays() => Surface.CommsSources(LocalPlayer).Where(c => c.Kind == CommsSourceKind.Relay);

    /// <summary>The relay's coverage radius in hexes: the data's (once S1 adds it), else an existing relay's, else 0.</summary>
    private int RelayRadius()
    {
        var r = DefNumber(_data, "Comms", "RelayRadius") ?? DefNumber(_data.Orbit, "RelayRadius") ?? DefNumber(_data.Orbit, "Relay", "Radius");
        if (r is { } v && v > 0) return Mathf.RoundToInt(v);
        return MyRelays().Select(c => c.Radius).DefaultIfEmpty(0).Max();
    }

    private int? RelayMax()
    {
        var m = DefNumber(_data, "Comms", "RelayMax") ?? DefNumber(_data.Orbit, "RelayMax") ?? DefNumber(_data.Orbit, "Relay", "Max");
        return m is { } v ? Mathf.RoundToInt(v) : null;
    }

    private IReadOnlyList<ResourceAmount> RelayCost()
    {
        object? Get(object? o, params string[] path)
        {
            foreach (var name in path) o = o?.GetType().GetProperty(name)?.GetValue(o);
            return o;
        }
        return (Get(_data, "Comms", "RelayCost") ?? Get(_data.Orbit, "RelayCost") ?? Get(_data.Orbit, "Relay", "Cost")) as IReadOnlyList<ResourceAmount>
            ?? System.Array.Empty<ResourceAmount>();
    }

    private string RelayTag() => RelayMax() is { } max ? $" {MyRelays().Count()}/{max}" : MyRelays().Any() ? $" {MyRelays().Count()}" : "";

    /// <summary>OrbitProblem for "relay".</summary>
    private string? RelayOrbitProblem(int tile) => Surface.RelayProblem(_sim, LocalPlayer, tile);

    /// <summary>OrbitClick for "relay" (the problem was checked just before).</summary>
    private void SendRelay(int tile)
    {
        _sim.Schedule(_sim.Tick + InputDelayTicks, new RelaySatelliteCommand(LocalPlayer, tile));
        var radius = RelayRadius();
        _hud.PushEvent($"Relay satellite going up over {Surface.Map.Terrain(tile).Name.ToLowerInvariant()} ({Coordinates(tile)})" +
            (radius > 0 ? $": comms {radius} tiles round it once on station" : ""), HelmScreen.Cyan);
    }

    /// <summary>The info card for the RELAY pill (OrbitInfo).</summary>
    private InfoView RelayInfo()
    {
        var stock = _sim.Players[LocalPlayer].Stock;
        var cost = RelayCost();
        var count = MyRelays().Count();
        var max = RelayMax();
        var radius = RelayRadius();
        var rows = new List<(string, string)>
        {
            ("Covers", radius > 0 ? $"{radius} tiles round the tile you click, once it is on station" : "a wide circle round the tile you click, once it is on station"),
            ("Why", "drones only fly inside comms coverage; your colony cores' antennas reach a few tiles, relays reach the rest"),
            ("Needs", "landed, with the outpost's uplink online; paid from the ship's hold"),
        };
        var deploy = DefNumber(_data, "Comms", "RelayDeploySeconds") ?? DefNumber(_data.Orbit, "RelayDeploySeconds");
        if (deploy is { } ds) rows.Add(("Deploy", $"{Mathf.RoundToInt(ds)} s drifting to station"));
        var hover = _orbitHover >= 0 ? RelayOrbitProblem(_orbitHover) : "pick a spot on the planet";
        var footer = _orbitAction == "relay"
            ? (_orbitHover < 0 ? "Click the planet where it should hang  ·  right-click / Esc cancel" : hover == null ? "Click to put it over this tile  ·  Shift+click to keep going" : $"Can't put it here: {hover}")
            : "Pick this card again to cancel";
        return new InfoView("orbit:relay", "Relay Satellite", max is { } m ? $"{count} of {m} relays in orbit" : $"{count} relay{(count == 1 ? "" : "s")} in orbit",
            cost.Count == 0 ? "Cost  from the hold" : $"Cost  {Amounts(cost, false)}", stock.CanAfford(cost),
            "Park a communications relay in a stationary orbit over one spot of the planet. Everything inside its circle can talk to the colony: scout drones can fly there, and missions that need contact with home count it. Survey satellites and relays are counted separately.",
            rows, -1f, "", "", false, footer);
    }

    /// <summary>Every frame (UpdateMissionUi): the coverage preview on the globe while RELAY is armed, events when a relay
    /// reaches its station.</summary>
    private void UpdateRelays()
    {
        var armed = _inOrbit && _orbitAction == "relay" && _orbit.AtColony;
        var tile = armed ? _orbitHover : -1;
        Seam(_orbit, "ShowRelayPreview", tile, tile >= 0 ? RelayRadius() : 0, tile >= 0 && RelayOrbitProblem(tile) == null);

        foreach (var c in MyRelays())
        {
            if (_relaysKnown.Add(c.SatelliteId) && c.Ready) _relaysReady.Add(c.SatelliteId); // already up when first seen
            if (!c.Ready || !_relaysReady.Add(c.SatelliteId)) continue;
            var name = Surface.SatelliteById(c.SatelliteId)?.CallSign ?? "Relay";
            _hud.PushEvent($"{name} on station over {Surface.Map.Terrain(c.Tile).Name.ToLowerInvariant()}: comms {c.Radius} tiles round it", HelmScreen.Cyan);
        }
    }

    // ---------------------------------------------------------------- SATELLITES window (PlanetDriver.Satellites.cs)

    /// <summary>Satellite ids that are relays (left out of the survey rows).</summary>
    private HashSet<int> RelaySatelliteIds() => MyRelays().Select(c => c.SatelliteId).ToHashSet();

    private string RelayName(CommsSource c, int index) => Surface.SatelliteById(c.SatelliteId)?.CallSign ?? $"RELAY-{index + 1}";

    private List<SatListEntry> RelayListEntries()
    {
        var list = new List<SatListEntry>();
        var i = 0;
        foreach (var c in MyRelays())
        {
            list.Add(new SatListEntry($"relay:{c.SatelliteId}", RelayName(c, i), c.Ready ? $"RELAY · {c.Radius} TILES" : "RELAY · DRIFTING", HelmScreen.Lavender));
            i++;
        }
        return list;
    }

    private CommsSource? RelayByKey(string key)
    {
        if (!int.TryParse(key[6..], out var id)) return null;
        foreach (var c in MyRelays())
            if (c.SatelliteId == id) return c;
        return null;
    }

    private bool RelayGone(string key) => RelayByKey(key) == null;

    /// <summary>A relay picked in the list: turn the globe to its station.</summary>
    private void LocateRelay(string key)
    {
        if (RelayByKey(key) is { } c && _orbit.AtColony && c.Tile >= 0) _orbit.Locate(c.Tile);
    }

    private SatCardView? RelayCardView(string key)
    {
        if (RelayByKey(key) is not { } c) return null;
        var index = MyRelays().ToList().FindIndex(x => x.SatelliteId == c.SatelliteId);
        var g = Surface.Map.Grid;
        var bases = Surface.Bases(LocalPlayer).Where(b => g.Distance(b.Tile, c.Tile) <= c.Radius).Select(b => BaseName(b.Id)).ToList();
        var drones = Surface.Units.Count(u => u.Owner == LocalPlayer && IsDrone(u) && g.Distance(u.Tile, c.Tile) <= c.Radius);
        var sat = Surface.SatelliteById(c.SatelliteId);
        var rows = new List<HelmRow>
        {
            new("MODEL", _data.Orbit?.Era == "dawn_of_warp" ? "comms relay (pre-warp)" : "comms relay"),
            new("STATION", c.Tile >= 0 ? $"{Surface.Map.Terrain(c.Tile).Name} · {Coordinates(c.Tile)}" : "—"),
            new("STATUS", c.Ready ? "on station · relaying" : "drifting to its station", c.Ready ? HelmScreen.Green : HelmScreen.Peach),
            new("COVERAGE", $"{c.Radius} tiles round its station"),
            new("REACHES", bases.Count > 0 ? string.Join(", ", bases) : "none of your bases"),
            new("DRONES", drones == 0 ? "none inside its circle" : $"{drones} inside its circle"),
        };
        if (sat != null) rows.Insert(0, new HelmRow("LAUNCHED", Clock(sat.LaunchTick)));
        return new SatCardView(key, RelayName(c, index), "comms relay · stationary orbit", rows, false, false, false, false, null,
            System.Array.Empty<SatFindingLine>(), 0, Relay: true);
    }
}
