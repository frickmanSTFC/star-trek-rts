using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>One source of comms coverage (docs/missions-contract.md B): a finished colony core's antenna or a relay
/// satellite parked over a tile. Radius in hexes; SatelliteId 0 for a core; Ready = deployed (a relay still drifting
/// to its station covers nothing yet).</summary>
public readonly record struct CommsSource(CommsSourceKind Kind, int Tile, int Radius, int StructureId, int SatelliteId, bool Ready);

public enum CommsSourceKind { Core, Relay }

// Comms coverage (data surfaces/comms.json): a tile is covered for a player when it lies within core_radius of one of
// their finished colony cores or within relay.radius of the station of one of their relay satellites that has finished
// drifting there. Drones only launch to (and only stay over) covered tiles. Coverage is worked out from hashed state
// (structures, satellites, the tick) whenever it is asked for, so it needs no state of its own.
public sealed partial class PlanetSurface
{
    private CommsRules? CommsRules => _data.Comms;

    /// <summary>The tick coverage is judged at: the game's, else the last stepped one.</summary>
    private long CommsTick => Game?.Tick ?? _weatherTick;

    /// <summary>True when the tile is inside this player's comms coverage (any ready source's radius). Always true with
    /// data that has no comms rules; false for a bad player or tile.</summary>
    public bool CommsCovered(int player, int tile)
    {
        if (CommsRules is not { } rules) return IsPlayer(player) && OnMap(tile);
        if (!IsPlayer(player) || !OnMap(tile)) return false;
        var g = Map.Grid;
        foreach (var s in _structures)
            if (s.Owner == player && CoreAntenna(s) && g.Distance(s.Tile, tile) <= rules.CoreRadius) return true;
        if (rules.Relay is { } relay)
        {
            var now = CommsTick;
            foreach (var sat in _satellites)
                if (sat.Owner == player && sat.Kind == SatelliteKind.Relay && !sat.Deploying(now) && sat.StationTile >= 0
                    && g.Distance(sat.StationTile, tile) <= relay.Radius) return true;
        }
        return false;
    }

    /// <summary>A finished colony core: its antenna reaches core_radius.</summary>
    private bool CoreAntenna(SurfaceStructure s) => s.State == StructureState.Complete && _data.Building(s.DefId).IsCore;

    /// <summary>This player's coverage sources, cores first (by structure id) then relays (by satellite id).</summary>
    public IReadOnlyList<CommsSource> CommsSources(int player)
    {
        var list = new List<CommsSource>();
        if (CommsRules is not { } rules || !IsPlayer(player)) return list;
        var cores = new List<SurfaceStructure>();
        foreach (var s in _structures)
            if (s.Owner == player && CoreAntenna(s)) cores.Add(s);
        cores.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var s in cores) list.Add(new CommsSource(CommsSourceKind.Core, s.Tile, rules.CoreRadius, s.Id, 0, true));
        if (rules.Relay is { } relay)
        {
            var now = CommsTick;
            var relays = new List<Satellite>();
            foreach (var sat in _satellites)
                if (sat.Owner == player && sat.Kind == SatelliteKind.Relay) relays.Add(sat);
            relays.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var sat in relays)
                list.Add(new CommsSource(CommsSourceKind.Relay, sat.StationTile, relay.Radius, 0, sat.Id, !sat.Deploying(now)));
        }
        return list;
    }

    /// <summary>How many relay satellites the player has up (deploying ones included).</summary>
    public int RelayCount(int player)
    {
        var n = 0;
        foreach (var sat in _satellites)
            if (sat.Owner == player && sat.Kind == SatelliteKind.Relay) n++;
        return n;
    }

    /// <summary>True when every tile a straight flight from one point to another (world units) passes over is covered.</summary>
    public bool CommsCoveredLine(int player, FixVec2 from, FixVec2 to)
    {
        if (CommsRules == null) return true;
        var d = to - from;
        var len = d.Length();
        var step = TileSize / 3;
        var n = (len / step).FloorToInt() + 1;
        var last = -1;
        for (var i = 0; i <= n; i++)
        {
            var tile = TileAtPos(from + d * (Fix64.FromInt(i) / Fix64.FromInt(n)));
            if (tile == last) continue;
            last = tile;
            if (!CommsCovered(player, tile)) return false;
        }
        return true;
    }

    /// <summary>The tile whose hex holds this point (world units, as <see cref="SurfaceUnit.Pos"/>): the nearest tile
    /// centre, clamped to the map.</summary>
    public int TileAtPos(FixVec2 p)
    {
        var g = Map.Grid;
        var row = System.Math.Clamp((p.Y / (HexGrid.RowSpacing * TileSize) + Fix64.Half).FloorToInt(), 0, g.Height - 1);
        var x = p.X / TileSize - ((row & 1) == 1 ? Fix64.Half : Fix64.Zero);
        var col = System.Math.Clamp((x + Fix64.Half).FloorToInt(), 0, g.Width - 1);
        var best = g.Index(col, row);
        var bestDist = (CentreOf(best) - p).LengthSquared();
        foreach (var n in g.Neighbours(best))
        {
            var dist = (CentreOf(n) - p).LengthSquared();
            if (dist < bestDist)
            {
                best = n;
                bestDist = dist;
            }
        }
        return best;
    }

    /// <summary>"RELAY-2 · LANTERN": the owner's n-th relay and a name from comms relay.call_signs, the list's start
    /// picked per owner from the map seed (another salt than the survey satellites'), beyond its end "LANTERN II".</summary>
    public string RelayCallSign(int owner, int ordinal)
    {
        var names = CommsRules?.Relay?.CallSigns is { Count: > 0 } list ? list : DefaultRelayCallSigns;
        var start = (int)(Mix(Map.Seed ^ (0x2E1A75A700000000UL + (ulong)owner * 0x9E3779B97F4A7C15UL)) % (ulong)names.Count);
        var i = ordinal - 1;
        var name = names[(start + i) % names.Count];
        var round = i / names.Count;
        return $"RELAY-{ordinal} · {name}{(round > 0 ? " " + Roman(round + 1) : "")}";
    }

    private static readonly string[] DefaultRelayCallSigns = { "BEACON", "LANTERN", "HERALD", "ECHO" };

    /// <summary>Why RelaySatelliteCommand(player, tile) would be refused now, or null.</summary>
    public string? RelayProblem(Simulation sim, int player, int tile)
    {
        if (!IsPlayer(player)) return "no such player";
        if (CommsRules?.Relay is not { } relay) return "no relay satellites in this game";
        if (_data.Orbit == null) return "no ship in orbit";
        if (sim.System?.OrbitProblem(player) is { } away) return away;
        if (_players[player].Phase != LandingPhase.Landed) return "land first: a relay links the colony to its drones";
        if (!UplinkOnline(player, sim.Tick)) return "the uplink isn't online yet";
        if (!OnMap(tile)) return "off the map";
        if (RelayCount(player) >= relay.Max) return $"all {relay.Max} relay satellites are up";
        if (!sim.Players[player].Stock.CanAfford(relay.Cost)) return "not enough resources";
        return null;
    }

    /// <summary>RelaySatelliteCommand: pay and put a relay on its way to its station.</summary>
    private bool ApplyRelay(Simulation sim, RelaySatelliteCommand command)
    {
        if (RelayProblem(sim, command.Player, command.Tile) is { } problem) return sim.Refuse(command, problem);
        var relay = CommsRules!.Relay!;
        sim.Players[command.Player].Stock.TrySpend(relay.Cost);
        var ordinal = RelayCount(command.Player) + 1;
        _satellites.Add(new Satellite(_nextSatelliteId++, command.Player, Map.Grid.Row(command.Tile), sim.Tick,
            sim.Tick + Ticks(relay.DeploySeconds), ordinal, RelayCallSign(command.Player, ordinal), SatelliteKind.Relay, command.Tile));
        Achieve(command.Player, "relay_up");
        return true;
    }

    /// <summary>Why a relay can't move its station to this tile (RetaskSatelliteCommand's Row = the tile), or null.</summary>
    private string? RelayRetaskProblem(Simulation sim, Satellite sat, int tile)
    {
        if (CommsRules?.Relay == null) return "no relay satellites in this game";
        if (!OnMap(tile)) return "off the map";
        if (sat.Deploying(sim.Tick)) return sat.Retasks == 0 ? "still drifting to its station" : "still moving to its new station";
        if (tile == sat.StationTile) return "already over that tile";
        return null;
    }

    /// <summary>A checked relay retask: it drifts to the new station for retask_seconds (covering nothing meanwhile).</summary>
    private void MoveRelay(Simulation sim, Satellite sat, int tile)
    {
        sat.StationTile = tile;
        sat.Row = Map.Grid.Row(tile);
        sat.ReadyTick = sim.Tick + Ticks(CommsRules!.Relay!.RetaskSeconds);
        sat.Retasks++;
    }
}
