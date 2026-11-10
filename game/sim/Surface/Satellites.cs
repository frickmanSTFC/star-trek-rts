using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Survey satellites, refined (docs/satellite-contract.md "Sim + view + UI"; data surfaces/orbit.json satellite).
// A satellite gets a call sign, rises and unfolds for deploy_seconds before its first sweep, counts its passes, and
// keeps a bounded log of what each sweep newly surveyed or spotted. Everything here is a pure function of hashed
// state and the data: no sim.Rng (call signs come from the map seed), all of it in the state hash.

public sealed partial class PlanetSurface
{
    /// <summary>Call-sign names when orbit.satellite.call_signs is empty (original bird names).</summary>
    private static readonly string[] DefaultCallSigns =
    {
        "KESTREL", "OSPREY", "MERLIN", "HERON", "PETREL", "CURLEW", "TERN", "KITE", "SHRIKE", "PLOVER", "EGRET", "GANNET",
    };

    /// <summary>The owner's firsts: (owner, kind, detail) keys of findings already logged once by any of their satellites.</summary>
    private readonly SortedSet<long> _satelliteFirsts = new();

    private static long FirstKey(int owner, SatelliteFindingKind kind, int detail) =>
        ((long)owner << 24) | ((long)(int)kind << 20) | (uint)(detail & 0xFFFFF);

    /// <summary>"SAT-2 · KESTREL": the owner's n-th satellite (1-based) and a name from the list, the list's start
    /// shuffled per owner from the map seed (so two players' first satellites differ), beyond its end "KESTREL II".</summary>
    public string SatelliteCallSign(int owner, int ordinal)
    {
        var names = _data.Orbit?.SatelliteCallSigns is { Count: > 0 } list ? list : DefaultCallSigns;
        var start = (int)(Mix(Map.Seed ^ (0x5A7E111700000000UL + (ulong)owner * 0x9E3779B97F4A7C15UL)) % (ulong)names.Count);
        var i = ordinal - 1;
        var name = names[(start + i) % names.Count];
        var round = i / names.Count;
        return $"SAT-{ordinal} · {name}{(round > 0 ? " " + Roman(round + 1) : "")}";
    }

    private static string Roman(int n) => n switch { 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };

    /// <summary>A satellite by id (null if there is none).</summary>
    public Satellite? SatelliteById(int id)
    {
        foreach (var s in _satellites)
            if (s.Id == id) return s;
        return null;
    }

    /// <summary>The rows a satellite's band covers (clamped to the map).</summary>
    public (int First, int Last) SatelliteBand(Satellite sat)
    {
        var half = (_data.Orbit?.SatelliteBandRows ?? 1) / 2;
        return (System.Math.Max(0, sat.Row - half), System.Math.Min(Map.Grid.Height - 1, sat.Row + half));
    }

    /// <summary>Share 0..1 of the band's tiles the owner has surveyed (by any means).</summary>
    public Fix64 SatelliteCoverage(Satellite sat)
    {
        var g = Map.Grid;
        var (first, last) = SatelliteBand(sat);
        var surveyed = _players[sat.Owner].Surveyed;
        int done = 0, all = 0;
        for (var r = first; r <= last; r++)
            for (var c = 0; c < g.Width; c++)
            {
                all++;
                if (surveyed[g.Index(c, r)]) done++;
            }
        return all == 0 ? Fix64.Zero : Fix64.FromInt(done) / Fix64.FromInt(all);
    }

    /// <summary>Progress 0..1 through the current lap (0 while deploying before the first sweep).</summary>
    public Fix64 SatelliteLapProgress(Satellite sat) =>
        Fix64.FromInt(sat.Swept % Map.Grid.Width) / Fix64.FromInt(Map.Grid.Width);

    /// <summary>Why this satellite can't move to the band centred on row (null = it can).</summary>
    public string? RetaskProblem(Simulation sim, int player, int satelliteId, int row)
    {
        if (!IsPlayer(player)) return "no such player";
        if (_data.Orbit == null) return "no ship in orbit";
        var sat = SatelliteById(satelliteId);
        if (sat == null) return "no such satellite";
        if (sat.Owner != player) return "not your satellite";
        if (row < 0 || row >= Map.Grid.Height) return "off the map";
        if (sat.Deploying(sim.Tick)) return sat.Retasks == 0 ? "still deploying" : "still manoeuvring to its band";
        if (row == sat.Row) return "already over that band";
        return null;
    }

    private static long Ticks(int seconds) => (long)seconds * Simulation.TickRate;

    /// <summary>SatelliteCommand, already checked: pay, and put it on its way up.</summary>
    private void LaunchSatellite(Simulation sim, SatelliteCommand command)
    {
        var orbit = _data.Orbit!;
        sim.Players[command.Player].Stock.TrySpend(orbit.SatelliteCost);
        var ordinal = 1;
        foreach (var s in _satellites)
            if (s.Owner == command.Player) ordinal++;
        _satellites.Add(new Satellite(_nextSatelliteId++, command.Player, command.Row, sim.Tick, sim.Tick + Ticks(orbit.SatelliteDeploySeconds),
            ordinal, SatelliteCallSign(command.Player, ordinal)));
    }

    private bool ApplyRetask(Simulation sim, RetaskSatelliteCommand command)
    {
        if (RetaskProblem(sim, command.Player, command.SatelliteId, command.Row) is { } problem) return sim.Refuse(command, problem);
        var sat = SatelliteById(command.SatelliteId)!;
        sat.Row = command.Row;
        sat.SweepBase = sat.Swept; // carry on from the column it had reached
        sat.ReadyTick = sim.Tick + Ticks(_data.Orbit!.SatelliteRetaskSeconds);
        sat.Retasks++;
        sat.WeatherLast = WeatherKind.Clear;
        return true;
    }

    /// <summary>Each satellite (once deployed) sweeps east across its band, surveying the columns it passes and logging
    /// what was new.</summary>
    private void SweepSatellites(Simulation sim)
    {
        var orbit = _data.Orbit;
        if (orbit == null) return;
        var g = Map.Grid;
        var lapTicks = (long)orbit.SatelliteSweepSeconds * Simulation.TickRate;
        foreach (var sat in _satellites)
        {
            if (sim.Tick < sat.ReadyTick) continue;
            var target = sat.SweepBase + (int)((sim.Tick - sat.ReadyTick) * g.Width / lapTicks);
            var (first, last) = SatelliteBand(sat);
            for (; sat.Swept < target; sat.Swept++)
            {
                var col = sat.Swept % g.Width;
                for (var r = first; r <= last; r++) SurveyFromOrbit(sim, sat, g.Index(col, r));
                WeatherUnder(sim, sat, g.Index(col, System.Math.Clamp(sat.Row, 0, g.Height - 1)));
                sat.Passes = (sat.Swept + 1) / g.Width;
            }
        }
    }

    /// <summary>Survey one tile for a satellite; if it was new to the owner, log what it shows.</summary>
    private void SurveyFromOrbit(Simulation sim, Satellite sat, int tile)
    {
        var surveyed = _players[sat.Owner].Surveyed;
        if (surveyed[tile]) return;
        Survey(sat.Owner, tile);
        sat.TilesSurveyed++;
        var map = Map;
        if (map.DepositResource[tile] >= 0) Log(sim, sat, SatelliteFindingKind.Deposit, tile, map.DepositResource[tile]);
        if (map.NativeTile == tile) Log(sim, sat, SatelliteFindingKind.Natives, tile, 0);
        if (_ruins.Count > 0 && RuinAt(tile) != null) Log(sim, sat, SatelliteFindingKind.Ruins, tile, 0);
        // Patches (herds, predator ranges, toxic ground, mountain massifs): only the first tile of one the owner hadn't
        // surveyed any of yet, so a patch is one line, not one per tile.
        var wild = map.WildlifeAt[tile];
        if (wild != Wildlife.None && NewPatch(surveyed, tile, t => map.WildlifeAt[t] == wild)) Log(sim, sat, SatelliteFindingKind.Life, tile, (int)wild);
        if (map.Toxic[tile] && NewPatch(surveyed, tile, t => map.Toxic[t])) Log(sim, sat, SatelliteFindingKind.Toxic, tile, 0);
        var terrain = map.Terrain(tile);
        if (!terrain.Passable && terrain.Height > 0 && NewPatch(surveyed, tile, t => map.TerrainOf[t] == map.TerrainOf[tile]))
            Log(sim, sat, SatelliteFindingKind.Terrain, tile, map.TerrainOf[tile]);
    }

    /// <summary>No surveyed neighbour shares the feature (so this tile starts a patch the owner hadn't seen).</summary>
    private bool NewPatch(bool[] surveyed, int tile, System.Func<int, bool> same)
    {
        foreach (var n in Map.Grid.Neighbours(tile))
            if (surveyed[n] && same(n)) return false;
        return true;
    }

    /// <summary>Notable weather under the track: logged when the satellite flies into it (not every column inside it).</summary>
    private void WeatherUnder(Simulation sim, Satellite sat, int tile)
    {
        var kind = Climate.Weather(_data, Map, tile, sim.Tick).Kind;
        var notable = kind is WeatherKind.Storm or WeatherKind.Blizzard or WeatherKind.DustStorm or WeatherKind.Fog ? kind : WeatherKind.Clear;
        if (notable != WeatherKind.Clear && notable != sat.WeatherLast) Log(sim, sat, SatelliteFindingKind.Weather, tile, (int)notable);
        sat.WeatherLast = notable;
    }

    private void Log(Simulation sim, Satellite sat, SatelliteFindingKind kind, int tile, int detail)
    {
        var first = _satelliteFirsts.Add(FirstKey(sat.Owner, kind, detail));
        sat._findings.Add(new SatelliteFinding(sim.Tick, kind, tile, detail, first));
        sat.FindingsTotal++;
        var max = System.Math.Max(1, _data.Orbit?.SatelliteLog ?? 40);
        if (sat._findings.Count > max) sat._findings.RemoveRange(0, sat._findings.Count - max);
    }

    private void AddSatelliteFirstsToHash(ref StateHasher h)
    {
        h.Add(_satelliteFirsts.Count);
        foreach (var k in _satelliteFirsts) h.Add(k);
    }
}
