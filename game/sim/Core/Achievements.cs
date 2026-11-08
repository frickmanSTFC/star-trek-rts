using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Core;

// CONTRACT (2026-11-08, achievements; docs/achievements-contract.md). Shapes are fixed so the UI and cosmetics agents
// can build against them. The tracker reads data/achievements/ (GameData.Achievements, GameData.Achievements.cs) and
// is fed events from the sim code where things happen (Simulation.Step's relayed orders, PlanetSurface, StarSystemState).

/// <summary>One unlock in this game: a player earned an achievement at a tick. In the state hash.</summary>
public sealed record AchievementUnlock(int Player, string Id, long Tick);

/// <summary>How far a player is toward an achievement with a counter, share or streak (for the screen's bar).</summary>
/// <param name="Unit">"hexes", "%", "s", "satellites", … (from the trigger; the view formats it).</param>
public sealed record AchievementProgress(long Current, long Target, string Unit);

/// <summary>A player's picked cosmetics that live in the sim (names and numbers are game state). Set before the game
/// starts with <see cref="Simulation.SetCosmetics"/>; hashed.</summary>
/// <param name="RoverNames">Extra names handed out first, in this order, before the shuffled pool.</param>
/// <param name="HullNumbers">Extra hull numbers handed out first, in this order (1 → "01").</param>
public sealed record PlayerCosmetics(IReadOnlyList<string> RoverNames, IReadOnlyList<int> HullNumbers)
{
    public static readonly PlayerCosmetics None = new(new List<string>(), new List<int>());
}

/// <summary>
/// Checks the achievement rules (GameData.Achievements) against sim state about once a second of game time and records
/// unlocks. Records nothing while <see cref="Gate"/> is non-null. View-only rules (trigger kind view_count) are never
/// checked here.
/// </summary>
/// <remarks>
/// Runs every <see cref="Simulation.TickRate"/> ticks, after the surface and the ships have stepped (Simulation.Step).
/// Events reported by the sim between two checks wait in a buffer and are read (then dropped) at the next check.
/// Everything it keeps (unlocks, per-player milestones, streaks, weather seen, a pending SOS, per-vehicle hex counters,
/// the buffer) is in the state hash, but only once any of it exists, so games where nothing was ever tracked (the
/// balance runner, with AchievementsEnabled false) hash exactly as before.
/// </remarks>
public sealed class AchievementTracker
{
    private readonly Simulation _sim;
    private readonly List<AchievementUnlock> _unlocks = new();
    private readonly List<PlayerTrack> _players = new();
    private readonly List<AchievementEvent> _events = new();
    private readonly SortedDictionary<int, UnitCount> _driven = new();

    internal AchievementTracker(Simulation sim) => _sim = sim;

    /// <summary>Every unlock of this game, oldest first (each player × id at most once).</summary>
    public IReadOnlyList<AchievementUnlock> Unlocks => _unlocks;

    /// <summary>Whether the player has earned it in this game.</summary>
    public bool Earned(int player, string id)
    {
        foreach (var u in _unlocks)
            if (u.Player == player && u.Id == id) return true;
        return false;
    }

    /// <summary>The player's unlocks in this game, oldest first.</summary>
    public IReadOnlyList<AchievementUnlock> UnlocksOf(int player) => _unlocks.Where(u => u.Player == player).ToList();

    /// <summary>Why nothing unlocks now ("test mode", "turned off", "no achievement data"), or null when unlocks count.
    /// The game turns achievements off itself when draft data is loaded.</summary>
    public string? Gate =>
        !_sim.AchievementsEnabled ? "turned off"
        : _sim.DebugEdits > 0 ? "test mode"
        : _sim.Data.Achievements == null ? "no achievement data"
        : null;

    /// <summary>Events the sim reported since the last check (read and cleared by it; for tests and debugging).</summary>
    public IReadOnlyList<AchievementEvent> PendingEvents => _events;

    /// <summary>The first tick a player reached a milestone ("uplink", "first_metal", "built:vehicle_bay", …), or null.</summary>
    public long? MilestoneTick(int player, string milestone) =>
        player >= 0 && player < _players.Count && _players[player].Milestones.TryGetValue(milestone, out var t) ? t : null;

    /// <summary>Hex edges a vehicle has crossed while achievements counted (0 when it isn't counted).</summary>
    public long TilesDriven(int unitId) => _driven.TryGetValue(unitId, out var c) ? c.Tiles : 0;

    /// <summary>Weather kinds the player has seen over their vehicles and cores (ids as in climate weather.json).</summary>
    public IReadOnlyList<string> WeatherSeen(int player) =>
        player >= 0 && player < _players.Count ? _players[player].Weather.ToList() : new List<string>();

    // ---------------------------------------------------------------- progress

    /// <summary>Progress toward an achievement with a progress bar, or null (no data yet / not that kind). Units: count
    /// "bodies" / "satellites" / "vehicles" / "techs", share "%", unit_counter "hexes", streak "s", set "kinds". Earned =
    /// target of target. view_count is the view's (null here).</summary>
    public AchievementProgress? Progress(int player, string id)
    {
        if (_sim.Data.Achievements?.Find(id) is not { } def || player < 0 || player >= _sim.Players.Count) return null;
        var p = Measure(player, def.Trigger);
        if (p == null) return null;
        return Earned(player, id) ? p with { Current = p.Target } : p with { Current = System.Math.Min(p.Current, p.Target) };
    }

    private AchievementProgress? Measure(int player, AchievementTrigger t)
    {
        var at = GameData.AchievementNumber(t, "at_least");
        var all = t.Fields.TryGetValue("all", out var a) && a == "true";
        t.Fields.TryGetValue("measure", out var measure);
        switch (t.Kind)
        {
            case "count":
            {
                if (CountOf(player, measure ?? "") is not var (current, total, unit)) return null;
                var target = all ? total : at?.FloorToInt() ?? 0;
                return target <= 0 ? null : new AchievementProgress(current, target, unit);
            }
            case "share":
            {
                if (_sim.Surface is not { } s || at is not { } share) return null;
                var count = SurveyedCount(player);
                var total = s.Map.Grid.Count;
                return new AchievementProgress(total == 0 ? 0 : count * 100L / total, (share * Fix64.FromInt(100)).FloorToInt(), "%");
            }
            case "unit_counter":
                return new AchievementProgress(MostDriven(player, t.List), at?.FloorToInt() ?? 0, "hexes");
            case "streak":
            {
                var seconds = GameData.AchievementNumber(t, "seconds")?.FloorToInt() ?? 0;
                return new AchievementProgress(StreakSeconds(player, t), seconds, "s");
            }
            case "set":
            {
                var seen = player < _players.Count ? _players[player].Weather : null;
                return new AchievementProgress(t.List.Count(k => seen != null && seen.Contains(k)), t.List.Count, "kinds");
            }
            default:
                return null;
        }
    }

    /// <summary>A count measure now: (current, the "all" total, unit), or null when it can't be measured in this game.</summary>
    private (long Current, long Total, string Unit)? CountOf(int player, string measure)
    {
        switch (measure)
        {
            case "bodies_charted":
                if (_sim.System is not { } sys || sys.Bodies.Count == 0) return null;
                return (sys.Bodies.Count(b => sys.KnowledgeOf(player, b.Id) == BodyKnowledge.Charted), sys.Bodies.Count, "bodies");
            case "system_satellites":
                if (_sim.System is not { } system) return null;
                return (system.Satellites.Count(s => s.Owner == player), 0, "satellites");
            case "units":
                if (_sim.Surface is not { } surface) return null;
                return (surface.Units.Count(u => u.Owner == player), 0, "vehicles");
            case "techs_known":
                if (_sim.Surface is not { } sf || player >= sf.Players.Count || _sim.Data.Techs.Count == 0) return null;
                return (sf.Players[player].Known.Count(k => k), _sim.Data.Techs.Count, "techs");
            default:
                return null;
        }
    }

    private long SurveyedCount(int player)
    {
        if (_sim.Surface is not { } s || player >= s.Players.Count) return 0;
        var n = 0L;
        foreach (var x in s.Players[player].Surveyed)
            if (x) n++;
        return n;
    }

    private long MostDriven(int player, IReadOnlyList<string> units)
    {
        var best = 0L;
        foreach (var c in _driven.Values)
            if (c.Owner == player && units.Contains(c.DefId) && c.Tiles > best) best = c.Tiles;
        return best;
    }

    private long StreakSeconds(int player, AchievementTrigger t)
    {
        if (player >= _players.Count || !_players[player].Streaks.TryGetValue(StreakKey(t), out var start) || start < 0) return 0;
        return (_sim.Tick - start) / Simulation.TickRate;
    }

    private static string StreakKey(AchievementTrigger t) =>
        (t.Fields.TryGetValue("condition", out var c) ? c : "") + (t.Fields.TryGetValue("from_start", out var f) && f == "true" ? "@start" : "");

    // ---------------------------------------------------------------- events from the sim

    /// <summary>The sim reports something that happened to a player ("crawler_explosion", "deposit_found" with its amount, …;
    /// GameData.AchievementEvents). Dropped while the gate is shut or when no achievement still to earn listens for it.</summary>
    internal void Note(int player, string name, string text = "", long number = 0)
    {
        if (!Counts(player) || !Listens(name)) return;
        _events.Add(new AchievementEvent(player, name, text, number));
    }

    /// <summary>A vehicle crossed into the next hex (Lanes.cs).</summary>
    internal void Drove(SurfaceUnit unit)
    {
        if (!Counts(unit.Owner) || !Counted(unit.DefId)) return;
        _driven[unit.Id] = _driven.TryGetValue(unit.Id, out var c) ? c with { Tiles = c.Tiles + 1 } : new UnitCount(unit.Owner, unit.DefId, 1);
    }

    /// <summary>A breakdown or a disabled vehicle: an SOS when the flagship is away from the colony's body.</summary>
    internal void Report(int player)
    {
        if (!Counts(player) || !Listens("sos_answered")) return;
        if (_sim.System is not { SurfaceBody: { } home } sys || sys.FlagshipOf(player) is not { } ship || ship.AtBody == home) return;
        Track(player).SosTick = _sim.Tick;
    }

    /// <summary>A player's flagship entered orbit of a body (StarSystemState.Step).</summary>
    internal void FlagshipArrived(int player, string bodyId)
    {
        if (!Counts(player) || _sim.System is not { } sys || sys.SurfaceBody is not { } home) return;
        if (sys.Body(home) is { } colony)
        {
            BodyDef? farthest = null;
            var best = Fix64.MinValue;
            foreach (var b in sys.Bodies)
            {
                if (b.Id == home) continue;
                var d = FixVec2.Distance(b.Position, colony.Position);
                if (d > best)
                {
                    best = d;
                    farthest = b;
                }
            }
            if (farthest != null && farthest.Id == bodyId) Note(player, "orbit_farthest");
        }
        if (bodyId == home && player < _players.Count && _players[player].SosTick >= 0)
        {
            var ticks = _sim.Tick - _players[player].SosTick;
            Note(player, "sos_answered", "", (ticks + Simulation.TickRate - 1) / Simulation.TickRate);
            _players[player].SosTick = -1;
        }
    }

    /// <summary>Whether things that happen to this player count now (the gate is open and it is a real player).</summary>
    private bool Counts(int player) => player >= 0 && player < _sim.Players.Count && Gate == null;

    // What the data listens for, worked out once per GameData (the test panel can swap it).
    private GameData? _cachedFor;
    private HashSet<string> _eventNames = new(StringComparer.Ordinal);
    private HashSet<string> _countedUnits = new(StringComparer.Ordinal);

    private void Cache()
    {
        if (ReferenceEquals(_cachedFor, _sim.Data)) return;
        _cachedFor = _sim.Data;
        _eventNames = new HashSet<string>(StringComparer.Ordinal);
        _countedUnits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in _sim.Data.Achievements?.List ?? Array.Empty<AchievementDef>())
            foreach (var t in Flatten(a.Trigger))
            {
                if (t.Kind == "event" && t.Fields.TryGetValue("event", out var e)) _eventNames.Add(e);
                if (t.Kind == "unit_counter") _countedUnits.UnionWith(t.List);
            }
    }

    private bool Listens(string eventName)
    {
        Cache();
        return _eventNames.Contains(eventName);
    }

    private bool Counted(string unitDef)
    {
        Cache();
        return _countedUnits.Contains(unitDef);
    }

    private static IEnumerable<AchievementTrigger> Flatten(AchievementTrigger t)
    {
        yield return t;
        foreach (var r in t.Rules)
            foreach (var x in Flatten(r))
                yield return x;
    }

    private PlayerTrack Track(int player)
    {
        while (_players.Count <= player) _players.Add(new PlayerTrack());
        return _players[player];
    }

    // ---------------------------------------------------------------- the check

    /// <summary>Once a second of game time (Simulation.Step): milestones, streaks and the weather seen move on, every rule
    /// still to earn is tested, and the event buffer is emptied.</summary>
    internal void Check()
    {
        if (Gate != null || _sim.Data.Achievements is not { } data)
        {
            _events.Clear();
            return;
        }
        var now = _sim.Tick;
        for (var p = 0; p < _sim.Players.Count; p++)
        {
            var open = data.List.Where(a => a.Trigger.Kind != "view_count" && !Earned(p, a.Id)).ToList();
            if (open.Count == 0) continue;
            var track = Track(p);
            var fresh = Milestones(p, track, now);
            if (open.Any(a => Flatten(a.Trigger).Any(t => t.Kind == "set"))) SeeWeather(p, track);
            if (open.Any(a => Flatten(a.Trigger).Any(t => t.Kind == "event" && t.Fields.GetValueOrDefault("event") == "domed_crewed_working"))
                && CrewedUnderDome(p))
                _events.Add(new AchievementEvent(p, "domed_crewed_working", "", 0));
            foreach (var a in open)
            {
                var met = Met(p, track, a.Trigger, fresh, now); // always evaluated: streaks move on even off-world
                if (met && WorldOk(a.Worlds)) _unlocks.Add(new AchievementUnlock(p, a.Id, now));
            }
        }
        _events.Clear();
    }

    /// <summary>Records milestones reached for the first time; returns the ones recorded now.</summary>
    private List<string> Milestones(int p, PlayerTrack track, long now)
    {
        var fresh = new List<string>();
        void Reach(string key)
        {
            if (track.Milestones.ContainsKey(key)) return;
            track.Milestones[key] = now;
            fresh.Add(key);
        }
        if (_sim.Surface is { } s && p < s.Players.Count)
        {
            var me = s.Players[p];
            if (me.Phase == LandingPhase.Landed) Reach("landed");
            if (s.UplinkOnline(p, now)) Reach("uplink");
            var cores = 0;
            foreach (var x in s.Structures)
            {
                if (x.Owner != p || x.State != StructureState.Complete) continue;
                var def = _sim.Data.Building(x.DefId);
                Reach("built:" + x.DefId);
                if (def.IsCore && x.Id == x.BaseId) cores++;
                if (x.DefId == "refinery" && x.Status is StructureStatus.Working or StructureStatus.LowPower) Reach("first_metal");
            }
            if (cores >= 2) Reach("second_base");
            foreach (var u in s.Units)
                if (u.Owner == p && u.BuiltAtTick >= 0) Reach("unit:" + u.DefId);
            if (_sim.Data.Techs.Count > 0 && me.Known.All(k => k)) Reach("research_all");
        }
        foreach (var r in _sim.Data.Resources)
            if (r.Id == "parasteel" && _sim.Players[p].Stock[r.Index] > Fix64.Zero) Reach("first_parasteel");
        return fresh;
    }

    /// <summary>The weather over each of the player's vehicles and finished cores goes into the set they have seen.</summary>
    private void SeeWeather(int p, PlayerTrack track)
    {
        if (_sim.Surface is not { } s || _sim.Data.Climate == null) return;
        var tiles = new List<int>();
        foreach (var u in s.Units)
            if (u.Owner == p && !tiles.Contains(u.Tile)) tiles.Add(u.Tile);
        foreach (var x in s.Structures)
            if (x.Owner == p && x.Id == x.BaseId && x.State == StructureState.Complete && !tiles.Contains(x.Tile)) tiles.Add(x.Tile);
        foreach (var t in tiles)
            track.Weather.Add(GameData.WeatherId(Climate.Weather(_sim.Data, s.Map, t, _sim.Tick).Kind));
    }

    /// <summary>A crewed building of the player's working inside a finished Pressure Dome's cover (Domes worlds).</summary>
    private bool CrewedUnderDome(int p)
    {
        if (_sim.Surface is not { NeedsDomes: true } s) return false;
        foreach (var x in s.Structures)
        {
            if (x.Owner != p || x.State != StructureState.Complete || x.Status != StructureStatus.Working) continue;
            if (_sim.Data.Building(x.DefId).IsCore || !_sim.Data.IsCrewed(x.DefId)) continue;
            foreach (var d in s.Structures)
            {
                if (d.Owner != p || _sim.Data.Building(d.DefId).IsCore) continue;
                var r = s.DomeRadiusOf(d);
                if (r > 0 && s.Map.Grid.Distance(d.Tile, x.Tile) <= r) return true;
            }
        }
        return false;
    }

    private bool WorldOk(AchievementWorlds? w)
    {
        if (w == null) return true;
        if (_sim.Surface is not { } s) return false;
        if (w.Classes.Count > 0 && !w.Classes.Contains(s.Map.Def.ClassId)) return false;
        var g = s.GravityG;
        if (w.MinGravityG is { } lo && g < Fix64.Parse(lo.ToString(CultureInfo.InvariantCulture))) return false;
        if (w.MaxGravityG is { } hi && g > Fix64.Parse(hi.ToString(CultureInfo.InvariantCulture))) return false;
        if (w.Tier != null && HabitabilityRules.TierId(s.Tier) != w.Tier) return false;
        return true;
    }

    private bool Met(int p, PlayerTrack track, AchievementTrigger t, List<string> fresh, long now)
    {
        string? F(string field) => t.Fields.TryGetValue(field, out var v) ? v : null;
        var atLeast = GameData.AchievementNumber(t, "at_least");
        switch (t.Kind)
        {
            case "milestone":
            {
                var ms = F("milestone") ?? "";
                if (!track.Milestones.TryGetValue(ms, out var at)) return false;
                if (GameData.AchievementNumber(t, "before_seconds") is { } before && Fix64.FromInt((int)System.Math.Min(at, int.MaxValue)) >= before * Fix64.FromInt(Simulation.TickRate)) return false;
                // "without": that building was never finished before the milestone (the same check counts as before).
                if (F("without") is { } without && track.Milestones.TryGetValue("built:" + without, out var builtAt) && builtAt <= at) return false;
                return true;
            }
            case "built":
            {
                if (_sim.Surface is not { } s) return false;
                var need = GameData.AchievementNumber(t, "count")?.FloorToInt() ?? 1;
                var id = F("building");
                var n = 0;
                foreach (var x in s.Structures)
                    if (x.Owner == p && x.DefId == id && x.State == StructureState.Complete) n++;
                return n >= System.Math.Max(1, need);
            }
            case "count":
            {
                if (CountOf(p, F("measure") ?? "") is not var (current, total, _)) return false;
                var target = F("all") == "true" ? total : atLeast?.FloorToInt() ?? 0;
                return target > 0 && current >= target;
            }
            case "share":
            {
                if (_sim.Surface is not { } s || atLeast is not { } share) return false;
                return Fix64.FromInt((int)SurveyedCount(p)) >= share * Fix64.FromInt(s.Map.Grid.Count);
            }
            case "unit_counter":
                return atLeast is { } need2 && Fix64.FromInt((int)System.Math.Min(MostDriven(p, t.List), int.MaxValue)) >= need2;
            case "event":
            {
                var name = F("event");
                if (name == null || !GameData.AchievementEvents.TryGetValue(name, out var fields)) return false;
                foreach (var e in _events)
                {
                    if (e.Player != p || e.Name != name) continue;
                    var ok = true;
                    foreach (var (field, compare) in fields)
                    {
                        if (F(field) is not { } want) continue;
                        if (compare == "equals") ok &= e.Text == want;
                        else if (GameData.AchievementNumber(t, field) is { } n)
                        {
                            var have = Fix64.FromInt((int)System.Math.Clamp(e.Number, int.MinValue, int.MaxValue));
                            ok &= compare == "at_least" ? have >= n : have <= n;
                        }
                    }
                    if (ok) return true;
                }
                return false;
            }
            case "streak":
            {
                var key = StreakKey(t);
                var fromStart = F("from_start") == "true";
                var holds = Holds(p, F("condition") ?? "");
                var has = track.Streaks.TryGetValue(key, out var start);
                if (has && start < 0) return false; // a from_start streak that broke: never again this game
                if (!holds)
                {
                    if (fromStart) track.Streaks[key] = -1;
                    else track.Streaks.Remove(key);
                    return false;
                }
                if (!has)
                {
                    if (fromStart && now > 0)
                    {
                        track.Streaks[key] = -1; // it wasn't holding (or counting) when the game started
                        return false;
                    }
                    track.Streaks[key] = start = now;
                }
                var seconds = GameData.AchievementNumber(t, "seconds") ?? Fix64.Zero;
                return Fix64.FromInt((int)System.Math.Min(now - start, int.MaxValue)) >= seconds * Fix64.FromInt(Simulation.TickRate);
            }
            case "set":
                return t.List.All(track.Weather.Contains);
            case "all_of":
            {
                var all = true;
                foreach (var r in t.Rules) all &= Met(p, track, r, fresh, now); // no short cut: streaks inside move on
                return all && t.Rules.Count > 0;
            }
            case "any_of":
            {
                var any = false;
                foreach (var r in t.Rules) any |= Met(p, track, r, fresh, now);
                return any;
            }
            default:
                return false;
        }
    }

    /// <summary>A streak condition now.</summary>
    private bool Holds(int p, string condition)
    {
        switch (condition)
        {
            case "no_power_short":
            {
                if (_sim.Surface is not { } s) return false;
                var any = false;
                foreach (var b in s.Bases(p))
                {
                    if (b.State != StructureState.Complete) continue;
                    any = true;
                    if (s.PowerOf(b.Id).Efficiency < Fix64.One) return false;
                }
                return any;
            }
            case "flagship_home":
                return _sim.System is { SurfaceBody: { } home } sys && sys.FlagshipOf(p) is { } ship && ship.AtBody == home;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- hash

    private bool Empty => _unlocks.Count == 0 && _events.Count == 0 && _driven.Count == 0 && _players.All(p => p.Empty);

    /// <summary>Unlocks, per-player state, vehicle counters and the event buffer, only once there is any (so a game that
    /// never tracked anything keeps the hash it always had).</summary>
    internal void AddToHash(ref StateHasher h)
    {
        if (Empty) return;
        h.Add("achievements");
        h.Add(_unlocks.Count);
        foreach (var u in _unlocks)
        {
            h.Add(u.Player);
            h.Add(u.Id);
            h.Add(u.Tick);
        }
        h.Add(_players.Count);
        foreach (var p in _players) p.AddToHash(ref h);
        h.Add(_driven.Count);
        foreach (var (id, c) in _driven)
        {
            h.Add(id);
            h.Add(c.Owner);
            h.Add(c.DefId);
            h.Add(c.Tiles);
        }
        h.Add(_events.Count);
        foreach (var e in _events)
        {
            h.Add(e.Player);
            h.Add(e.Name);
            h.Add(e.Text);
            h.Add(e.Number);
        }
    }

    private sealed record UnitCount(int Owner, string DefId, long Tiles);

    /// <summary>What the tracker keeps per player (ordered collections only: they are hashed in order).</summary>
    private sealed class PlayerTrack
    {
        public readonly SortedDictionary<string, long> Milestones = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, long> Streaks = new(StringComparer.Ordinal); // start tick; -1 = broken
        public readonly SortedSet<string> Weather = new(StringComparer.Ordinal);
        public long SosTick = -1;

        public bool Empty => Milestones.Count == 0 && Streaks.Count == 0 && Weather.Count == 0 && SosTick < 0;

        public void AddToHash(ref StateHasher h)
        {
            h.Add(Milestones.Count);
            foreach (var (k, v) in Milestones)
            {
                h.Add(k);
                h.Add(v);
            }
            h.Add(Streaks.Count);
            foreach (var (k, v) in Streaks)
            {
                h.Add(k);
                h.Add(v);
            }
            h.Add(Weather.Count);
            foreach (var w in Weather) h.Add(w);
            h.Add(SosTick);
        }
    }
}

/// <summary>Something the sim reported for the achievement tracker (see GameData.AchievementEvents): a name, and its
/// text (charted_class: the class) or number (deposit_found: amount, order_relayed: delay_seconds, sos_answered:
/// within_seconds).</summary>
public sealed record AchievementEvent(int Player, string Name, string Text, long Number);

public sealed partial class Simulation
{
    private AchievementTracker? _achievements;

    /// <summary>The achievement tracker of this game (never null).</summary>
    public AchievementTracker Achievements => _achievements ??= new AchievementTracker(this);

    /// <summary>False: the tracker records nothing (balance runner, draft data loaded by the game). Default true.</summary>
    public bool AchievementsEnabled { get; set; } = true;

    /// <summary>Most extra rover names or hull numbers one player may bring.</summary>
    public const int MaxCosmeticNames = 32;

    /// <summary>
    /// Sets a player's sim-side cosmetics (rover names, hull numbers); call before the first Step (ignored after tick 0,
    /// for a player that doesn't exist, or without a planet surface). Names are trimmed, upper-cased, at most 16
    /// characters; blanks and repeats are dropped; numbers 0..99999. They are handed out first, in order, by the
    /// surface's naming (UnitNaming), then the shuffled pool, which skips anything equal to one of them. Hashed (only
    /// when a player has any).
    /// </summary>
    public void SetCosmetics(int player, PlayerCosmetics cosmetics)
    {
        if (Tick > 0 || cosmetics == null || player < 0 || player >= _players.Count || Surface == null) return;
        var names = new List<string>();
        foreach (var n in cosmetics.RoverNames ?? Array.Empty<string>())
        {
            var name = (n ?? "").Trim().ToUpperInvariant();
            if (name.Length == 0 || name.Length > 16 || names.Contains(name) || names.Count >= MaxCosmeticNames) continue;
            names.Add(name);
        }
        var numbers = new List<int>();
        foreach (var x in cosmetics.HullNumbers ?? Array.Empty<int>())
        {
            if (x < 0 || x > 99999 || numbers.Contains(x) || numbers.Count >= MaxCosmeticNames) continue;
            numbers.Add(x);
        }
        Surface.SetCosmetics(player, names, numbers);
    }

    /// <summary>A player's sim-side cosmetics as the sim keeps them (after SetCosmetics' clean-up), or None.</summary>
    public PlayerCosmetics CosmeticsOf(int player) =>
        Surface != null && player >= 0 && player < Surface.Players.Count
            ? new PlayerCosmetics(Surface.Players[player].ExtraNames, Surface.Players[player].ExtraNumbers)
            : PlayerCosmetics.None;
}
