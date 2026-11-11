using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Missions;

/// <summary>A condition now: met, and for counters how far (Progress of Target; Target 0 = a plain yes / no).</summary>
internal readonly record struct MissionEval(bool Met, Fix64 Progress, Fix64 Target)
{
    public static readonly MissionEval No = new(false, Fix64.Zero, Fix64.Zero);
    public static readonly MissionEval Yes = new(true, Fix64.Zero, Fix64.Zero);

    public static MissionEval Of(bool met) => met ? Yes : No;

    public static MissionEval Count(long have, long need) =>
        new(have >= need, Fix64.FromInt((int)System.Math.Clamp(have, 0, int.MaxValue)), need > 1 ? Fix64.FromInt((int)System.Math.Min(need, int.MaxValue)) : Fix64.Zero);
}

// The condition vocabulary (GameData.MissionConditionFields lists it; data/missions/notes.json explains it). Pure reads of
// the game state; the only memory is the run's per-objective Visited list (drone_at) and baselines (fabricated, deposit).
public sealed partial class MissionBook
{
    /// <summary>Evaluates a condition for a player. run / obj = the objective it belongs to (null / -1 for a trigger);
    /// bind = where a trigger's bindings go (null = don't bind).</summary>
    private MissionEval Check(int p, MissionRun? run, int obj, MissionCondDef c, List<Binding>? bind)
    {
        if (_sim?.Surface is not { } s || p < 0 || p >= s.Players.Count) return MissionEval.No;
        var data = _sim.Data;
        var me = s.Players[p];
        switch (c.Kind)
        {
            case "all":
            {
                var all = c.Children.Count > 0;
                foreach (var child in c.Children)
                    all &= Check(p, run, obj, child, bind).Met;
                return MissionEval.Of(all);
            }
            case "any":
                foreach (var child in c.Children)
                {
                    var mine = bind != null ? new List<Binding>() : null;
                    if (!Check(p, run, obj, child, mine).Met) continue;
                    if (mine != null) bind!.AddRange(mine);
                    return MissionEval.Yes;
                }
                return MissionEval.No;
            case "not":
                return MissionEval.Of(c.Children.Count == 1 && !Check(p, run, obj, c.Children[0], null).Met);
            case "resource":
            {
                if (c.F("res") is not { } id || !Has(data, id, out var r)) return MissionEval.No;
                var need = c.N("amount", Fix64.Zero);
                var have = _sim.Players[p].Stock[r];
                return new MissionEval(have >= need, have, need);
            }
            case "building":
            {
                var ids = Ids(c);
                var state = c.F("state") ?? "complete";
                var at = c.F("at") is { } ar ? TilesOf(p, run, ar) : null;
                var radius = c.I("radius", 0);
                var n = 0L;
                foreach (var x in s.Structures)
                {
                    if (x.Owner != p || !ids.Contains(x.DefId)) continue;
                    if (state == "complete" && x.State != StructureState.Complete) continue;
                    if (state == "building" && x.State == StructureState.Complete) continue;
                    if (at != null && !Near(s, x.Tile, at, radius)) continue;
                    n++;
                }
                return MissionEval.Count(n, c.I("count", 1));
            }
            case "unit":
            {
                var ids = Ids(c);
                var state = c.F("state") ?? "any";
                var at = c.F("at") is { } ar ? TilesOf(p, run, ar) : null;
                var radius = c.I("radius", 0);
                var far = c.I("far_from_pad", -1);
                var n = 0L;
                SurfaceUnit? first = null;
                foreach (var u in s.Units)
                {
                    if (u.Owner != p || !ids.Contains(u.DefId) || !UnitIn(s, u, state)) continue;
                    if (at != null && !Near(s, u.Tile, at, radius)) continue;
                    if (far >= 0 && !FarFromPads(s, p, u.Tile, far)) continue;
                    first ??= u;
                    n++;
                }
                var need = c.I("count", 1);
                if (n >= need && first != null && c.F("bind") is { } name && bind != null)
                {
                    var b = new Binding { Name = name, UnitId = first.Id, Text = CallSign(first) };
                    b.Tiles.Add(first.Tile);
                    bind.Add(b);
                }
                return MissionEval.Count(n, need);
            }
            case "surveyed":
            {
                if (c.F("at") is { } ar)
                {
                    var radius = c.I("radius", 0);
                    foreach (var t in TilesOf(p, run, ar))
                        foreach (var x in s.Map.Grid.Within(t, radius))
                            if (!me.Surveyed[x]) return MissionEval.No;
                    return MissionEval.Of(TilesOf(p, run, ar).Count > 0);
                }
                var n = 0L;
                foreach (var x in me.Surveyed)
                    if (x) n++;
                var need = c.I("count", 1);
                return new MissionEval(n >= need, Fix64.FromInt((int)n), Fix64.FromInt(need));
            }
            case "deposits_found":
            {
                var rich = c.F("rich") == "true";
                var only = c.F("res") is { } rid && Has(data, rid, out var ri) ? ri : -1;
                var seen = new List<int>();
                var firstTile = -1;
                for (var t = 0; t < s.Map.Grid.Count; t++)
                {
                    var res = s.Map.DepositResource[t];
                    if (res < 0 || !me.Surveyed[t]) continue;
                    if (rich && s.Map.DepositStart[t] < Defs!.Rules.RichDeposit) continue;
                    if (only >= 0 && res != only) continue;
                    if (firstTile < 0) firstTile = t;
                    if (!seen.Contains(res)) seen.Add(res);
                }
                var need = c.I("count", 1);
                if (seen.Count >= need && firstTile >= 0 && c.F("bind") is { } name && bind != null)
                {
                    var b = new Binding { Name = name, Res = data.Resources[s.Map.DepositResource[firstTile]].Id, Text = TileText(s, firstTile) };
                    b.Tiles.Add(firstTile);
                    bind.Add(b);
                }
                return new MissionEval(seen.Count >= need, Fix64.FromInt(seen.Count), Fix64.FromInt(need));
            }
            case "deposit":
            {
                var tiles = c.F("at") is { } ar ? TilesOf(p, run, ar) : new List<int>();
                if (tiles.Count == 0) return MissionEval.No;
                var need = c.N("extracted", Fix64.Zero);
                var got = Extracted(s, tiles[0]) - (run != null && obj >= 0 && obj < run.Base.Length ? run.Base[obj] : Fix64.Zero);
                got = Fix64.Max(Fix64.Zero, got);
                return new MissionEval(got >= need, got, need);
            }
            case "site":
            {
                if (run == null || c.F("site") is not { } name || run.Find(name) is not { } b || b.Sites.Count == 0) return MissionEval.No;
                var state = c.F("state") ?? "found";
                var radius = c.I("radius", 1);
                var n = 0;
                foreach (var id in b.Sites)
                {
                    if (Site(id) is not { } site) continue;
                    var ok = state switch
                    {
                        "found" => site.Found,
                        "done" => site.Done,
                        "reached" => GroundParkedNear(s, p, site.Tile, radius, -1),
                        "visited" => DroneOver(s, p, site.Tile, radius),
                        _ => false,
                    };
                    if (ok) n++;
                }
                return MissionEval.Count(n, b.Sites.Count);
            }
            case "drone_launched":
            {
                var n = 0L;
                foreach (var d in World.Drones(s, p))
                    if (d.State is not (DroneState.None or DroneState.Docked or DroneState.Lost)) n++;
                return MissionEval.Count(n, c.I("count", 1));
            }
            case "drone_at":
            {
                var tiles = c.F("at") is { } ar ? TilesOf(p, run, ar) : new List<int>();
                if (tiles.Count == 0) return MissionEval.No;
                var radius = c.I("radius", 1);
                var need = System.Math.Min(c.I("count", tiles.Count), tiles.Count);
                var n = 0;
                for (var k = 0; k < tiles.Count; k++)
                {
                    var key = obj * 65536 + k;
                    var seen = run != null && obj >= 0 && run.Visited.Contains(key);
                    if (!seen && DroneOver(s, p, tiles[k], radius))
                    {
                        seen = true;
                        if (run != null && obj >= 0)
                        {
                            run.Visited.Add(key);
                            run.Visited.Sort();
                            MarkSiteAt(run, c.F("at")!, k);
                        }
                    }
                    if (seen) n++;
                }
                return MissionEval.Count(n, need) with { Target = Fix64.FromInt(need) };
            }
            case "relay":
            {
                var relays = World.Sources(s, p).Where(x => x.Kind == CommsSourceKind.Relay && x.Ready).ToList();
                var need = c.I("count", 1);
                if (relays.Count < need) return MissionEval.Count(relays.Count, need);
                if (c.F("covers") is not { } cov) return MissionEval.Count(relays.Count, need);
                var tiles = TilesOf(p, run, cov);
                if (tiles.Count == 0) return MissionEval.No;
                foreach (var t in tiles)
                    if (!relays.Any(x => s.Map.Grid.Distance(x.Tile, t) <= x.Radius)) return MissionEval.No;
                return MissionEval.Yes;
            }
            case "covered":
            {
                var tiles = c.F("at") is { } ar ? TilesOf(p, run, ar) : new List<int>();
                return MissionEval.Of(tiles.Count > 0 && tiles.All(t => World.Covered(s, p, t)));
            }
            case "fabricated":
            {
                var need = c.I("count", 1);
                var made = Have(s, p, c.F("item") ?? "") - (run != null && obj >= 0 && obj < run.Base.Length ? run.Base[obj].FloorToInt() : 0);
                return MissionEval.Count(System.Math.Max(0, made), need) with { Target = Fix64.FromInt(need) };
            }
            case "outpost":
            {
                var cores = s.Bases(p).Where(x => x.State == StructureState.Complete).ToList();
                var covered = c.F("covered") == "true";
                var sources = covered ? World.Sources(s, p) : new List<CommsSource>();
                var n = 0;
                for (var i = 0; i < cores.Count; i++)
                {
                    var core = cores[i];
                    if (covered && i > 0 && !sources.Any(x => x.Ready && x.StructureId != core.Id && s.Map.Grid.Distance(x.Tile, core.Tile) <= x.Radius)) continue;
                    n++;
                }
                var need = c.I("count", 1);
                return MissionEval.Count(n, need) with { Target = Fix64.FromInt(need) };
            }
            case "weather":
            {
                if (data.Climate == null) return MissionEval.No;
                var kinds = c.L("kinds");
                var tiles = TilesOf(p, run, c.F("near") ?? "base");
                var ahead = c.I("within_seconds", 0);
                foreach (var t in tiles)
                    for (var sec = 0; sec <= ahead; sec += 10)
                    {
                        var w = Climate.Weather(data, s.Map, t, Now + (long)sec * Simulation.TickRate);
                        if (kinds.Contains(GameData.WeatherId(w.Kind))) return MissionEval.Yes;
                    }
                return MissionEval.No;
            }
            case "exposed":
            {
                if (data.Climate == null) return MissionEval.No;
                var kinds = c.L("kinds");
                foreach (var u in s.Units)
                {
                    if (u.Owner != p || s.IsAerial(u) || u.Mode != UnitMode.Mobile || u.Path.Count > 0) continue;
                    var open = s.NeedsDomes ? !s.Covered(p, u.Tile) : s.StructureAt(u.Tile) == null;
                    if (!open) continue;
                    if (kinds.Contains(GameData.WeatherId(Climate.Weather(data, s.Map, u.Tile, Now).Kind))) return MissionEval.Yes;
                }
                return MissionEval.No;
            }
            case "time":
            {
                var from = (c.F("since") ?? "start") switch
                {
                    "uplink" => me.UplinkTick,
                    "mission" => run?.Start ?? Now,
                    "stage" => run?.StageStart ?? Now,
                    _ => 0,
                };
                var seconds = (Now - from) / Simulation.TickRate;
                var ok = true;
                if (c.F("after_seconds") != null) ok &= seconds >= c.I("after_seconds", 0);
                if (c.F("before_seconds") != null) ok &= seconds < c.I("before_seconds", 0);
                return MissionEval.Of(ok);
            }
            case "flag":
                return MissionEval.Of(c.F("name") is { } f && Track(p).Flags.Contains(f));
            case "mission":
            {
                if (c.F("id") is not { } id || RunOf(p, id) is not { } other) return MissionEval.No;
                var want = c.F("state") ?? "completed";
                return MissionEval.Of(other.State.ToString().ToLowerInvariant() == want);
            }
            case "tech":
                return MissionEval.Of(c.F("id") is { } tid && data.HasTech(tid) && me.Known[data.Tech(tid).Index]);
            case "uplink":
                return MissionEval.Of(s.UplinkOnline(p, Now));
            case "ruin_hint":
            {
                foreach (var r in s.Ruins)
                {
                    if (me.Surveyed[r.Tile]) continue;
                    if (c.F("bind") is { } name && bind != null)
                    {
                        var b = new Binding { Name = name, Text = TileText(s, r.Tile) };
                        b.Tiles.Add(r.Tile);
                        bind.Add(b);
                    }
                    return MissionEval.Yes;
                }
                return MissionEval.No;
            }
            case "satellite_share":
            {
                var swept = 0L;
                foreach (var sat in s.Satellites)
                    if (sat.Owner == p && sat.Kind == SatelliteKind.Survey) swept += sat.TilesSurveyed; // relays don't sweep
                var share = c.N("share", Fix64.One);
                var total = s.Map.Grid.Count;
                var pct = total == 0 ? 0 : (int)System.Math.Min(100, swept * 100 / total);
                var target = (share * Fix64.FromInt(100)).FloorToInt();
                return new MissionEval(Fix64.FromInt((int)System.Math.Min(swept, int.MaxValue)) >= share * Fix64.FromInt(total), Fix64.FromInt(pct), Fix64.FromInt(target));
            }
            case "near":
            {
                if (run == null || c.F("at") is not { } name || run.Find(name) is not { UnitId: > 0 } b || s.FindUnit(b.UnitId) is not { } who) return MissionEval.No;
                var radius = c.I("radius", 1);
                foreach (var u in s.Units)
                    if (u.Owner == p && u.Id != who.Id && !s.IsAerial(u) && s.Map.Grid.Distance(u.Tile, who.Tile) <= radius) return MissionEval.Yes;
                return MissionEval.No;
            }
            default:
                return MissionEval.No;
        }
    }

    /// <summary>The counter target an objective shows before it has been checked (0 = yes / no).</summary>
    private Fix64 Target(int p, MissionRun run, MissionCondDef c) => c.Kind switch
    {
        "surveyed" when c.F("at") == null => Fix64.FromInt(c.I("count", 1)),
        "deposits_found" => Fix64.FromInt(c.I("count", 1)),
        "deposit" => c.N("extracted", Fix64.Zero),
        "resource" => c.N("amount", Fix64.Zero),
        "fabricated" or "outpost" => Fix64.FromInt(c.I("count", 1)),
        "drone_at" => Fix64.FromInt(c.F("count") != null ? c.I("count", 1) : System.Math.Max(1, c.F("at") is { } a ? TilesOf(p, run, a).Count : 1)),
        "building" or "unit" or "drone_launched" when c.I("count", 1) > 1 => Fix64.FromInt(c.I("count", 1)),
        "satellite_share" => (c.N("share", Fix64.One) * Fix64.FromInt(100)),
        _ => Fix64.Zero,
    };

    /// <summary>What a counter that counts "since the stage began" starts from.</summary>
    private Fix64 Baseline(int p, MissionRun run, MissionCondDef c)
    {
        if (_sim?.Surface is not { } s) return Fix64.Zero;
        switch (c.Kind)
        {
            case "fabricated":
                return Fix64.FromInt(Have(s, p, c.F("item") ?? ""));
            case "deposit":
                return c.F("at") is { } ar && TilesOf(p, run, ar) is { Count: > 0 } tiles ? Extracted(s, tiles[0]) : Fix64.Zero;
            default:
                return Fix64.Zero;
        }
    }

    private static IReadOnlyList<string> Ids(MissionCondDef c)
    {
        if (c.F("id") is not { } one) return c.L("ids");
        var list = c.L("ids").ToList();
        list.Add(one);
        return list;
    }

    private static bool Has(GameData data, string id, out int index)
    {
        foreach (var r in data.Resources)
            if (r.Id == id)
            {
                index = r.Index;
                return true;
            }
        index = -1;
        return false;
    }

    /// <summary>How many of an item the player has: in stock plus as units of that id (a drone loaded onto a pad).</summary>
    private int Have(PlanetSurface s, int p, string item)
    {
        var n = 0;
        if (Has(_sim!.Data, item, out var r)) n += _sim.Players[p].Stock[r].FloorToInt();
        foreach (var u in s.Units)
            if (u.Owner == p && u.DefId == item) n++;
        return n;
    }

    private static Fix64 Extracted(PlanetSurface s, int tile) =>
        tile >= 0 && tile < s.Map.Grid.Count && s.Map.DepositResource[tile] >= 0
            ? Fix64.Max(Fix64.Zero, s.Map.DepositStart[tile] - s.Map.DepositAmount[tile]) : Fix64.Zero;

    private bool UnitIn(PlanetSurface s, SurfaceUnit u, string state)
    {
        switch (state)
        {
            case "any": return true;
            case "mobile": return u.Mode == UnitMode.Mobile;
            case "parked": return u.Mode == UnitMode.Mobile && u.Path.Count == 0;
            case "deployed": return u.Mode == UnitMode.Deployed;
            case "broken": return u.Broken;
            case "aerial": return s.IsAerial(u);
        }
        var drone = s.DroneOf(u).State;
        return state switch
        {
            "away" => drone is not (DroneState.None or DroneState.Docked or DroneState.Lost),
            "docked" => drone == DroneState.Docked,
            "launching" => drone == DroneState.Launching,
            "flying" => drone == DroneState.Flying,
            "loitering" => drone == DroneState.Loitering,
            "returning" => drone == DroneState.Returning,
            "landing" => drone == DroneState.Landing,
            "lost" => drone == DroneState.Lost,
            _ => false,
        };
    }

    private static bool Near(PlanetSurface s, int tile, List<int> tiles, int radius)
    {
        foreach (var t in tiles)
            if (s.Map.Grid.Distance(tile, t) <= radius) return true;
        return false;
    }

    /// <summary>More than this many hexes from every finished colony core of the player's (their repair pads).</summary>
    private static bool FarFromPads(PlanetSurface s, int p, int tile, int far)
    {
        foreach (var b in s.Bases(p))
            if (b.State == StructureState.Complete && s.Map.Grid.Distance(b.Tile, tile) <= far) return false;
        return true;
    }

    /// <summary>A ground vehicle of the player's parked within radius (not the excluded unit, not disabled).</summary>
    private static bool GroundParkedNear(PlanetSurface s, int p, int tile, int radius, int except)
    {
        foreach (var u in s.Units)
            if (u.Owner == p && u.Id != except && !s.IsAerial(u) && !u.Broken && u.Mode == UnitMode.Mobile && u.Path.Count == 0
                && s.Map.Grid.Distance(u.Tile, tile) <= radius)
                return true;
        return false;
    }

    /// <summary>One of the player's drones is loitering within radius of the tile.</summary>
    private bool DroneOver(PlanetSurface s, int p, int tile, int radius)
    {
        foreach (var d in World.Drones(s, p))
            if (d.State == DroneState.Loitering && d.Tile >= 0 && d.Tile < s.Map.Grid.Count && s.Map.Grid.Distance(d.Tile, tile) <= radius) return true;
        return false;
    }

    private void MarkSiteAt(MissionRun run, string bindingName, int index)
    {
        if (run.Find(bindingName) is { } b && index < b.Sites.Count && Site(b.Sites[index]) is { } site) site.Done = true;
    }

    /// <summary>The tiles a reference names: hub (the first base's core), base (every finished core), or a binding's
    /// tiles (its sites' tiles; a unit's current tile).</summary>
    private List<int> TilesOf(int p, MissionRun? run, string name)
    {
        var tiles = new List<int>();
        if (_sim?.Surface is not { } s || p < 0 || p >= s.Players.Count) return tiles;
        switch (name)
        {
            case "hub":
                if (Hub(s, p) is var hub and >= 0) tiles.Add(hub);
                return tiles;
            case "base":
                foreach (var b in s.Bases(p))
                    if (b.State == StructureState.Complete) tiles.Add(b.Tile);
                return tiles;
        }
        if (run?.Find(name) is not { } binding) return tiles;
        if (binding.UnitId > 0)
        {
            if (s.FindUnit(binding.UnitId) is { } u) tiles.Add(u.Tile);
            return tiles;
        }
        if (binding.Sites.Count > 0)
        {
            foreach (var id in binding.Sites)
                if (Site(id) is { } site) tiles.Add(site.Tile);
            return tiles;
        }
        tiles.AddRange(binding.Tiles);
        return tiles;
    }

    /// <summary>The player's first base's tile (else their landing tile, else -1).</summary>
    private static int Hub(PlanetSurface s, int p)
    {
        foreach (var b in s.Bases(p)) return b.Tile;
        return s.Players[p].LandingTile;
    }

    /// <summary>A unit's call sign as the game writes it ("Kalty 44").</summary>
    internal static string CallSign(SurfaceUnit u)
    {
        var words = u.Name.Split(' ');
        for (var i = 0; i < words.Length; i++)
            if (words[i].Length > 1 && words[i].Any(ch => ch != 'I')) words[i] = words[i][0] + words[i][1..].ToLowerInvariant();
        var name = string.Join(' ', words).Trim();
        return name.Length == 0 ? $"unit {u.Id}" : u.Number.Length > 0 ? $"{name} {u.Number}" : name;
    }

    /// <summary>A tile as the texts write it: "hex 34,12" (column, row).</summary>
    internal static string TileText(PlanetSurface s, int tile) =>
        tile < 0 || tile >= s.Map.Grid.Count ? "an unknown spot" : $"hex {s.Map.Grid.Column(tile)},{s.Map.Grid.Row(tile)}";
}
