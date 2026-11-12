using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Missions;

// The action vocabulary (GameData.MissionActionFields; data/missions/notes.json), text placeholders, and where
// place_site / place_tiles / marker offsets go (hashes of the map seed, player, mission and binding: never sim.Rng).
public sealed partial class MissionBook
{
    /// <summary>Runs one action of a mission (on_enter, or a reward via Reward).</summary>
    private void Act(int p, MissionRun run, MissionDef d, MissionActionDef a)
    {
        if (_sim?.Surface is not { } s) return;
        switch (a.Kind)
        {
            case "message":
                Say(p, run, a.F("giver") ?? d.Giver, Fill(p, run, a.F("text") ?? ""), a.F("channel") ?? "comms");
                break;
            case "place_site":
            case "place_tiles":
            {
                var name = a.F("bind") ?? "";
                if (run.Find(name) is { } old) _sites.RemoveAll(x => old.Sites.Contains(x.Id) && x.MissionId == run.Id); // placed again
                var tiles = PickTiles(s, p, run, a);
                var b = new Binding { Name = name };
                b.Tiles.AddRange(tiles);
                if (tiles.Count > 0) b.Text = TileText(s, tiles[0]);
                if (a.Kind == "place_site")
                    foreach (var t in tiles)
                    {
                        var site = new SiteRun { Id = _nextSiteId++, Owner = p, Tile = t, MissionId = run.Id, Kind = a.F("kind") ?? "" };
                        site.Found = s.Players[p].Surveyed[t] || s.IsVisible(p, t);
                        _sites.Add(site);
                        b.Sites.Add(site.Id);
                    }
                run.Bind(b);
                break;
            }
            case "marker":
            {
                if (a.F("at") is not { } at) break;
                var style = a.F("style") ?? "target";
                var radius = a.I("radius", 1);
                var offset = a.I("offset", 0);
                var label = Fill(p, run, a.F("label") ?? "");
                if (run.Find(at) is { UnitId: > 0 } ub)
                {
                    run.Markers.Add(new MarkerRun { Style = "unit", UnitId = ub.UnitId, Radius = radius, Label = label });
                    break;
                }
                var sites = run.Find(at)?.Sites ?? new List<int>();
                var tiles = TilesOf(p, run, at);
                for (var k = 0; k < tiles.Count; k++)
                {
                    var centre = offset > 0 ? Offset(s, p, run.Id, at, k, tiles[k], offset) : tiles[k];
                    // A marker on a site goes when that site is done, but only for a site still open: a site imported
                    // from an earlier mission and done there (Calling Home's Pathfinder wreck) keeps its marker.
                    var site = k < sites.Count && style != "area" ? Site(sites[k]) : null;
                    run.Markers.Add(new MarkerRun
                    {
                        Style = style, Tile = centre, Radius = radius, Label = label,
                        SiteId = site is { Done: false } ? site.Id : 0,
                    });
                }
                break;
            }
            case "start_mission":
            {
                if (a.F("id") is not { } id || RunOf(p, id) is not { } other || other == run || Def(other) is not { } od) break;
                if (other.State is MissionState.Locked or MissionState.Offered or MissionState.Declined)
                {
                    var bindings = new List<Binding>();
                    if (od.Trigger != null) Check(p, null, -1, od.Trigger, bindings);
                    Activate(p, other, od, bindings, brief: true);
                }
                break;
            }
            case "complete":
                Complete(p, run, d);
                break;
            case "fail":
                Fail(p, run, d);
                break;
            default:
                Reward(p, run, a);
                break;
        }
    }

    /// <summary>Runs a reward action; returns what was paid for the debrief line ("" for none).</summary>
    private string Reward(int p, MissionRun run, MissionActionDef a)
    {
        if (_sim?.Surface is not { } s) return "";
        var data = _sim.Data;
        switch (a.Kind)
        {
            case "grant":
            {
                var lines = new List<string>();
                foreach (var (id, amount) in a.Amounts)
                {
                    if (id == data.PowerResource || !Has(data, id, out var r)) continue;
                    var got = s.MissionGrant(_sim, p, r, amount);
                    if (got > Fix64.Zero) lines.Add($"{Whole(got)} {data.Resources[r].Name}");
                }
                return string.Join(", ", lines);
            }
            case "research":
                return s.MissionResearch(_sim, p, a.N("points", Fix64.Zero));
            case "reveal":
            {
                var radius = a.I("radius", 1);
                var tiles = a.F("at") is { } at ? TilesOf(p, run, at) : new List<int>();
                foreach (var t in tiles) s.MissionReveal(p, t, radius);
                return tiles.Count > 0 ? $"map revealed round {(tiles.Count == 1 ? TileText(s, tiles[0]) : $"{tiles.Count} spots")}" : "";
            }
            case "unlock_buildings":
            {
                var added = s.UnlockBuildings(p, GameData.UnlockIds(a));
                return added.Count > 0 ? "new buildings: " + string.Join(", ", added.Select(id => data.Building(id).Name)) : "";
            }
            case "set_flag":
            {
                var book = Track(p);
                if (a.F("name") is { } f && !book.Flags.Contains(f))
                {
                    book.Flags.Add(f);
                    book.Flags.Sort(System.StringComparer.Ordinal);
                }
                return "";
            }
            case "message":
            case "place_site":
            case "place_tiles":
            case "marker":
            case "start_mission":
                if (Def(run) is { } d) Act(p, run, d, a);
                return "";
            default:
                return "";
        }
    }

    /// <summary>A reward as the mission card lists it before it is paid.</summary>
    private string RewardText(int p, MissionRun run, MissionActionDef a)
    {
        var data = _sim!.Data;
        switch (a.Kind)
        {
            case "grant":
                return string.Join(", ", a.Amounts.Where(x => Has(data, x.Resource, out _)).Select(x => $"{Whole(x.Amount)} {data.Resource(x.Resource).Name}"));
            case "research":
                return $"+{Whole(a.N("points", Fix64.Zero))} research";
            case "reveal":
                return $"map revealed ({a.I("radius", 1)} hexes round {(a.F("at") is { } at && run.Find(at) is { } b && b.Text.Length > 0 ? b.Text : "the spot")})";
            case "unlock_buildings":
            {
                var ids = GameData.UnlockIds(a).Where(data.EveryBuildMenu.Contains).ToList();
                return ids.Count > 0 ? "unlocks " + string.Join(", ", ids.Select(id => data.Building(id).Name)) : "";
            }
            default:
                return "";
        }
    }

    private static string Whole(Fix64 x) => x.FloorToInt().ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- text

    private static readonly Regex PlaceholderPattern = new(@"\{([a-z_][a-z0-9_]*)(?:\.([a-z_]+))?\}", RegexOptions.CultureInvariant);

    /// <summary>A text with its {binding} and {binding.prop} placeholders filled from the run's bindings now.</summary>
    internal string Fill(int p, MissionRun run, string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 || _sim?.Surface is not { } s) return text ?? "";
        return PlaceholderPattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            var prop = m.Groups[2].Value;
            var tiles = TilesOf(p, run, name);
            var b = run.Find(name);
            switch (prop)
            {
                case "res":
                    return b != null && Has(_sim.Data, b.Res, out var r) ? _sim.Data.Resources[r].Name.ToLowerInvariant() : "unknown";
                case "distance":
                {
                    var hub = Hub(s, p);
                    return tiles.Count > 0 && hub >= 0 ? s.Map.Grid.Distance(hub, tiles[0]).ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
                }
                case "count":
                    return tiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case "tile":
                    return tiles.Count > 0 ? TileText(s, tiles[0]) : "an unknown spot";
                default:
                    if (name == "hub" || name == "base") return "the outpost";
                    if (b != null && b.Text.Length > 0) return b.Text;
                    return tiles.Count > 0 ? TileText(s, tiles[0]) : "?";
            }
        });
    }

    // ---------------------------------------------------------------- placement

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Fnv(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in text)
        {
            h ^= ch;
            h *= 1099511628211UL;
        }
        return h;
    }

    private static ulong Roll(PlanetSurface s, int p, string mission, string what, int k) =>
        Mix(Mix(Mix(s.Map.Seed ^ Fnv(mission)) ^ Fnv(what) ^ (ulong)(uint)p * 0x51ED27UL) + (ulong)(uint)k);

    /// <summary>The centre of a search area: a tile within offset of the target, so the ring doesn't give it away.</summary>
    private static int Offset(PlanetSurface s, int p, string mission, string what, int k, int target, int offset)
    {
        var options = s.Map.Grid.Within(target, offset).Where(t => s.Map.Grid.Distance(t, target) >= System.Math.Max(1, offset - 1)).ToList();
        if (options.Count == 0) return target;
        options.Sort();
        return options[(int)(Roll(s, p, mission, what + "#offset", k) % (ulong)options.Count)];
    }

    /// <summary>Where place_site / place_tiles put their tiles: count tiles min..max hexes from the hub (or the nearest
    /// tile of near), matching terrain, not on a building, not native land, not another mission site; unsurveyed /
    /// behind if asked (rules dropped one by one, then a wider ring, when nothing fits); spacing apart.</summary>
    private List<int> PickTiles(PlanetSurface s, int p, MissionRun run, MissionActionDef a)
    {
        var grid = s.Map.Grid;
        var anchors = a.F("near") is { } near ? TilesOf(p, run, near) : TilesOf(p, run, "hub");
        var picks = new List<int>();
        if (anchors.Count == 0) return picks;
        var count = System.Math.Max(1, a.I("count", 1));
        var min = System.Math.Max(0, a.I("min", 0));
        var max = System.Math.Max(min, a.I("max", min));
        var terrain = a.F("terrain") ?? "any";
        var spacing = System.Math.Max(0, a.I("spacing", 0));
        var unsurveyed = a.F("unsurveyed") == "true";
        var behind = a.F("behind") == "true";
        var hub = Hub(s, p);
        var ground = GroundSteps(s, hub); // from the base: "behind" and "passable" (a passable site must be reachable by ground)
        var taken = new List<int>();
        foreach (var site in _sites)
            if (site.Owner == p) taken.Add(site.Tile);
        var bind = a.F("bind") ?? "";
        for (var pass = 0; pass < 4 && picks.Count < count; pass++)
        {
            var needBehind = behind && pass < 1;
            var needUnsurveyed = unsurveyed && pass < 2;
            var reach = pass < 3 ? max : max * 2;
            var candidates = new List<int>();
            for (var t = 0; t < grid.Count; t++)
            {
                var row = grid.Row(t);
                if (row == 0 || row == grid.Height - 1) continue;
                var d = int.MaxValue;
                foreach (var x in anchors) d = System.Math.Min(d, grid.Distance(x, t));
                if (d < min || d > reach) continue;
                var passable = s.Map.Terrain(t).Passable;
                if (terrain == "passable" && !passable) continue;
                if (terrain == "impassable" && passable) continue;
                if (s.StructureAt(t) != null || s.Map.NearNatives(t)) continue;
                if (taken.Contains(t) || picks.Contains(t)) continue;
                if (picks.Any(x => grid.Distance(x, t) < spacing)) continue;
                if (needUnsurveyed && s.Players[p].Surveyed[t]) continue;
                if (terrain == "passable" && ground[t] < 0 && pass < 3) continue;
                if (needBehind && !(ground[t] < 0 || (hub >= 0 && ground[t] >= grid.Distance(hub, t) + 3))) continue;
                candidates.Add(t);
            }
            while (picks.Count < count && candidates.Count > 0)
            {
                var t = candidates[(int)(Roll(s, p, run.Id, bind, picks.Count) % (ulong)candidates.Count)];
                picks.Add(t);
                candidates.RemoveAll(x => grid.Distance(x, t) < System.Math.Max(1, spacing));
            }
        }
        return picks;
    }

    /// <summary>Steps over passable land from a tile to every tile (-1 = can't be reached by ground).</summary>
    private static int[] GroundSteps(PlanetSurface s, int from)
    {
        var grid = s.Map.Grid;
        var steps = new int[grid.Count];
        System.Array.Fill(steps, -1);
        if (from < 0 || from >= grid.Count) return steps;
        var queue = new Queue<int>();
        steps[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            foreach (var n in grid.Neighbours(t))
            {
                if (steps[n] >= 0 || !s.Map.Terrain(n).Passable) continue;
                steps[n] = steps[t] + 1;
                queue.Enqueue(n);
            }
        }
        return steps;
    }
}
