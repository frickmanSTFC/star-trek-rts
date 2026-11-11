using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Jack raid (owner 2026-11-11, a joke for Craig; docs/jack-contract.md): a minute after a player's colony touches down,
// sensors pick up an incoming "alien", and Jack Skellington, the Pumpkin King, rises out of a green mist near the base,
// stomps from building to building throwing glowing pumpkins and setting bats loose, then bows and sinks away. It never
// destroys anything: buildings and vehicles never drop below 1, and each one takes at most JackRules.MaxDamage per raid.
// Off unless JackEnabled (the game turns it on; tests and the balance bot leave it off, so nothing else changes).
// Randomness is a hash of (tick, player, salt), never sim.Rng, so the rest of the game rolls exactly as before.

/// <summary>Where a raid is: warned (countdown), rising out of the mist, stomping about, bowing out, gone.</summary>
public enum JackPhase { Warning, Rising, Rampage, Leaving, Gone }

/// <summary>Timings and damage of a Jack raid (seconds; placeholder numbers, kept in code: it's a joke, not a feature).</summary>
public static class JackRules
{
    public const int WarnAfter = 60;      // seconds after touchdown: INCOMING ATTACK
    public const int ArriveAfter = 20;    // countdown from the warning to his arrival
    public const int RiseSeconds = 3;     // rising out of the green mist (no moving, no attacks)
    public const int RampageSeconds = 150;
    public const int LeaveSeconds = 6;    // bow (3 s) then sink back into the mist (3 s)
    public const int SpawnDistance = 5;   // hexes from the colony core
    public const int StepTicks = 24;      // one hex per 1.6 s (giant strides)
    public const int AttackTicks = 75;    // an attack every 5 s
    public const int ThrowTicks = 15;     // he stands still 1 s for each attack
    public const int PumpkinRange = 4;
    public const int PumpkinFlightTicks = 22; // ~1.5 s
    public const int PumpkinRadius = 1;
    public const int BatsRadius = 2;
    public const int BatsDelayTicks = 15; // bats reach their targets 1 s after he lets them go
    public const int PumpkinMin = 4, PumpkinMax = 8;
    public const int BatsMin = 2, BatsMax = 4;
    public const int MaxDamage = 30;      // per building or vehicle per raid
}

/// <summary>One player's Jack raid. Position: Jack stands on Tile, or is striding FromTile → Tile between StepStartTick
/// and StepEndTick (the view lerps). FacingTile = where he looks (his target).</summary>
public sealed class JackRaid
{
    public int Player { get; }
    public int BaseId { get; internal set; }
    public long WarnTick { get; }
    public long ArriveTick => WarnTick + (long)JackRules.ArriveAfter * Simulation.TickRate;
    public long RampageTick => ArriveTick + (long)JackRules.RiseSeconds * Simulation.TickRate;
    public long LeaveTick { get; internal set; }
    public long GoneTick => LeaveTick + (long)JackRules.LeaveSeconds * Simulation.TickRate;
    public int Tile { get; internal set; } = -1;
    public int FromTile { get; internal set; } = -1;
    public long StepStartTick { get; internal set; }
    public long StepEndTick { get; internal set; }
    public int FacingTile { get; internal set; } = -1;
    public long NextAttackTick { get; internal set; }
    public long BusyUntilTick { get; internal set; }
    public int Attacks { get; internal set; }

    internal readonly Dictionary<int, int> _structureDamage = new();
    internal readonly Dictionary<int, int> _unitDamage = new();

    internal JackRaid(int player, int baseId, long warnTick)
    {
        Player = player;
        BaseId = baseId;
        WarnTick = warnTick;
        LeaveTick = RampageTick + (long)JackRules.RampageSeconds * Simulation.TickRate;
    }

    public JackPhase PhaseAt(long tick) =>
        tick < ArriveTick ? JackPhase.Warning
        : tick < RampageTick ? JackPhase.Rising
        : tick < LeaveTick ? JackPhase.Rampage
        : tick < GoneTick ? JackPhase.Leaving
        : JackPhase.Gone;

    /// <summary>Seconds until he arrives (0 once he's here).</summary>
    public int SecondsToArrival(long tick) => (int)System.Math.Max(0, (ArriveTick - tick + Simulation.TickRate - 1) / Simulation.TickRate);

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Player); h.Add(BaseId); h.Add(WarnTick); h.Add(LeaveTick); h.Add(Tile); h.Add(FromTile);
        h.Add(StepStartTick); h.Add(StepEndTick); h.Add(FacingTile); h.Add(NextAttackTick); h.Add(BusyUntilTick); h.Add(Attacks);
        foreach (var kv in _structureDamage.OrderBy(k => k.Key)) { h.Add(kv.Key); h.Add(kv.Value); }
        foreach (var kv in _unitDamage.OrderBy(k => k.Key)) { h.Add(kv.Key); h.Add(kv.Value); }
    }
}

/// <summary>An attack: "pumpkin" (thrown FromTile → Tile, bursts at LandTick), "bats" (released round FromTile, they bite
/// at LandTick), "shriek" (a line, no damage). Damage/Hits are filled in when it lands.</summary>
public sealed class JackAttack
{
    public long Tick { get; }
    public int Player { get; }
    public string Kind { get; }
    public int FromTile { get; }
    public int Tile { get; }
    public long LandTick { get; }
    public int Damage { get; internal set; }
    public int Hits { get; internal set; }
    public bool Landed { get; internal set; }

    internal JackAttack(long tick, int player, string kind, int fromTile, int tile, long landTick)
    {
        Tick = tick; Player = player; Kind = kind; FromTile = fromTile; Tile = tile; LandTick = landTick;
    }
}

/// <summary>A line for the event feed: warning / arrived / pumpkin / bats / shriek / leaving / gone.</summary>
public sealed record JackEvent(long Tick, int Player, string Kind, string Text);

public sealed partial class PlanetSurface
{
    /// <summary>Jack Skellington raids each colony a minute after it lands (set by the game before the first tick).</summary>
    public bool JackEnabled { get; set; }

    public IReadOnlyList<JackRaid> Jacks => _jacks;
    public IReadOnlyList<JackAttack> JackAttacks => _jackAttacks;
    public IReadOnlyList<JackEvent> JackEvents => _jackEvents;

    private readonly List<JackRaid> _jacks = new();
    private readonly List<JackAttack> _jackAttacks = new();
    private readonly List<JackEvent> _jackEvents = new();

    public JackRaid? JackOf(int player) => _jacks.LastOrDefault(j => j.Player == player);

    private static readonly string[] JackLines =
    {
        "\"BOO! ...did I scare you? Be honest.\"",
        "\"Halloween is over? Nobody told ME.\"",
        "\"What a charming little colony. It needs more pumpkins.\"",
        "\"Your sensors call me an 'unknown lifeform'. How rude!\"",
        "\"Lovely base! It would look so much better with a few cobwebs.\"",
        "\"Has anyone here seen Sally?\"",
        "\"Space! So dark, so cold, so wonderfully spooky.\"",
        "\"Don't mind me, I'm just redecorating.\"",
    };

    /// <summary>Each step: start raids a minute after touchdown, then run every raid in progress.</summary>
    private void JackStep(Simulation sim)
    {
        if (JackEnabled)
            for (var i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.Phase != LandingPhase.Landed || p.LandsAtTick <= 0) continue;
                if (sim.Tick != p.LandsAtTick + (long)JackRules.WarnAfter * Simulation.TickRate) continue;
                if (_jacks.Any(j => j.Player == i)) continue;
                StartJack(sim, i);
            }

        foreach (var j in _jacks)
        {
            var phase = j.PhaseAt(sim.Tick);
            if (phase == JackPhase.Gone) continue;
            if (sim.Tick == j.ArriveTick) JackArrives(sim, j);
            if (phase == JackPhase.Rampage) JackRampage(sim, j);
            if (sim.Tick == j.LeaveTick)
                Say(sim, j, "leaving", "Jack Skellington takes a long, graceful bow... \"Happy Halloween, Craig!\"");
            if (sim.Tick == j.GoneTick - 1)
                Say(sim, j, "gone", "Jack Skellington sinks back into the green mist. Sensors: all clear. (Was that real?)");
        }

        foreach (var a in _jackAttacks)
            if (!a.Landed && sim.Tick >= a.LandTick) JackLand(sim, a);
    }

    /// <summary>INCOMING ATTACK for this player from now (also the test panel's SUMMON JACK).</summary>
    private void StartJack(Simulation sim, int player)
    {
        var core = Bases(player).OrderBy(b => b.Id).FirstOrDefault();
        var j = new JackRaid(player, core?.Id ?? 0, sim.Tick);
        _jacks.Add(j);
        Say(sim, j, "warning",
            $"INCOMING ATTACK! Sensors: one unknown lifeform, 16 m tall, closing on the colony. Arrival in {JackRules.ArriveAfter} s. " +
            "Profile match... JACK SKELLINGTON, the Pumpkin King?!");
    }

    private void JackArrives(Simulation sim, JackRaid j)
    {
        var core = FindStructure(j.BaseId) ?? Bases(j.Player).OrderBy(b => b.Id).FirstOrDefault();
        var centre = core?.Tile ?? (_players[j.Player].LandingTile >= 0 ? _players[j.Player].LandingTile : 0);
        j.Tile = JackSpawnTile(centre, j);
        j.FromTile = j.Tile;
        j.FacingTile = centre;
        j.StepStartTick = j.StepEndTick = sim.Tick;
        j.NextAttackTick = j.RampageTick + JackRules.AttackTicks / 2;
        Say(sim, j, "arrived", "Jack Skellington rises out of a green mist beside the colony! Pumpkins incoming!");
    }

    /// <summary>A free tile SpawnDistance hexes from the core (closer if none), picked by hash; passable land preferred.</summary>
    private int JackSpawnTile(int centre, JackRaid j)
    {
        for (var r = JackRules.SpawnDistance; r >= 1; r--)
        {
            var ring = Map.Grid.Within(centre, r).Where(t => Map.Grid.Distance(t, centre) == r && _structureAt[t] == 0).ToList();
            var land = ring.Where(t => Map.Terrain(t).Passable).ToList();
            var pick = land.Count > 0 ? land : ring;
            if (pick.Count > 0) return pick[(int)(JackHash(j.WarnTick, j.Player, 7) % (ulong)pick.Count)];
        }
        return centre;
    }

    private void JackRampage(Simulation sim, JackRaid j)
    {
        var target = JackTarget(j);
        if (target == null) { j.LeaveTick = System.Math.Min(j.LeaveTick, sim.Tick + 1); return; } // nothing left to stomp
        var moving = sim.Tick < j.StepEndTick;
        if (moving) return;
        j.FromTile = j.Tile;
        j.FacingTile = target.Tile;

        if (sim.Tick >= j.NextAttackTick)
        {
            JackAttackNow(sim, j);
            j.NextAttackTick = sim.Tick + JackRules.AttackTicks;
            j.BusyUntilTick = sim.Tick + JackRules.ThrowTicks;
            return;
        }
        if (sim.Tick < j.BusyUntilTick) return;

        if (Map.Grid.Distance(j.Tile, target.Tile) <= 1) return; // right beside it: stand and throw
        var next = JackStepToward(j.Tile, target.Tile);
        if (next < 0) return;
        j.FromTile = j.Tile;
        j.Tile = next;
        j.StepStartTick = sim.Tick;
        j.StepEndTick = sim.Tick + JackRules.StepTicks;
    }

    /// <summary>The raided player's structure nearest Jack that he hasn't maxed out yet (else the nearest at all).</summary>
    private SurfaceStructure? JackTarget(JackRaid j)
    {
        var mine = _structures.Where(s => s.Owner == j.Player).ToList();
        if (mine.Count == 0) return null;
        var fresh = mine.Where(s => !j._structureDamage.TryGetValue(s.Id, out var d) || d < JackRules.MaxDamage).ToList();
        var from = fresh.Count > 0 ? fresh : mine;
        return from.OrderBy(s => Map.Grid.Distance(j.Tile, s.Tile)).ThenBy(s => s.Id).First();
    }

    /// <summary>The neighbour closest to the goal without a building on it, passable land first (he can wade if he must).</summary>
    private int JackStepToward(int from, int goal)
    {
        var best = -1;
        var bestScore = int.MaxValue;
        foreach (var n in Map.Grid.Neighbours(from))
        {
            if (_structureAt[n] != 0) continue;
            var score = Map.Grid.Distance(n, goal) * 10 + (Map.Terrain(n).Passable ? 0 : 25);
            if (score < bestScore || (score == bestScore && n < best)) { best = n; bestScore = score; }
        }
        return best;
    }

    private void JackAttackNow(Simulation sim, JackRaid j)
    {
        var kind = (j.Attacks % 4) switch { 0 => "pumpkin", 1 => "bats", 2 => "pumpkin", _ => "shriek" };
        j.Attacks++;
        if (kind == "pumpkin")
        {
            var targets = _structures.Where(s => s.Owner == j.Player && Map.Grid.Distance(j.Tile, s.Tile) <= JackRules.PumpkinRange)
                .OrderBy(s => j._structureDamage.TryGetValue(s.Id, out var d) ? d : 0).ThenBy(s => s.Id).ToList();
            if (targets.Count == 0) kind = "bats";
            else
            {
                var t = targets[(int)(JackHash(sim.Tick, j.Player, 11) % (ulong)System.Math.Min(3, targets.Count))];
                j.FacingTile = t.Tile;
                _jackAttacks.Add(new JackAttack(sim.Tick, j.Player, "pumpkin", j.Tile, t.Tile, sim.Tick + JackRules.PumpkinFlightTicks));
                return;
            }
        }
        if (kind == "bats")
        {
            _jackAttacks.Add(new JackAttack(sim.Tick, j.Player, "bats", j.Tile, j.Tile, sim.Tick + JackRules.BatsDelayTicks));
            return;
        }
        var line = JackLines[(j.Attacks / 4 + (int)(JackHash(j.WarnTick, j.Player, 3) % (ulong)JackLines.Length)) % JackLines.Length];
        _jackAttacks.Add(new JackAttack(sim.Tick, j.Player, "shriek", j.Tile, j.Tile, sim.Tick) { Landed = true });
        Say(sim, j, "shriek", $"Jack Skellington: {line}");
    }

    /// <summary>A pumpkin bursts or the bats bite: damage to the raided player's buildings and vehicles in range, never
    /// below 1 and at most MaxDamage each per raid.</summary>
    private void JackLand(Simulation sim, JackAttack a)
    {
        a.Landed = true;
        var j = JackOf(a.Player);
        if (j == null) return;
        var pumpkin = a.Kind == "pumpkin";
        var radius = pumpkin ? JackRules.PumpkinRadius : JackRules.BatsRadius;
        var lo = pumpkin ? JackRules.PumpkinMin : JackRules.BatsMin;
        var hi = pumpkin ? JackRules.PumpkinMax : JackRules.BatsMax;
        var damage = lo + (int)(JackHash(a.LandTick, a.Player, pumpkin ? 21UL : 22UL) % (ulong)(hi - lo + 1));
        a.Damage = damage;
        var buildings = 0;
        var vehicles = 0;
        foreach (var s in _structures)
        {
            if (s.Owner != a.Player || Map.Grid.Distance(s.Tile, a.Tile) > radius) continue;
            var done = j._structureDamage.TryGetValue(s.Id, out var d) ? d : 0;
            var dmg = System.Math.Min(damage, JackRules.MaxDamage - done);
            if (dmg <= 0) continue;
            j._structureDamage[s.Id] = done + dmg;
            s.Condition = Fix64.Max(Fix64.Min(s.Condition, Fix64.One), s.Condition - Fix64.FromInt(dmg));
            buildings++;
        }
        foreach (var u in _units)
        {
            if (u.Owner != a.Player || u.Drone != null || Map.Grid.Distance(u.Tile, a.Tile) > radius) continue;
            var done = j._unitDamage.TryGetValue(u.Id, out var d) ? d : 0;
            var dmg = System.Math.Min(damage, JackRules.MaxDamage - done);
            if (dmg <= 0) continue;
            j._unitDamage[u.Id] = done + dmg;
            u.Integrity = Fix64.Max(Fix64.Min(u.Integrity, Fix64.One), u.Integrity - Fix64.FromInt(dmg));
            u.HazardAtTick = sim.Tick;
            u.LastHazard = pumpkin ? "Jack's pumpkin" : "Jack's bats";
            u.LastHazardDamage = dmg;
            vehicles++;
        }
        a.Hits = buildings + vehicles;
        if (a.Hits == 0) return;
        var what = pumpkin ? "A flaming pumpkin bursts over the colony" : "A swarm of bats tears through the colony";
        var parts = new List<string>();
        if (buildings > 0) parts.Add($"{buildings} building{(buildings == 1 ? "" : "s")}");
        if (vehicles > 0) parts.Add($"{vehicles} vehicle{(vehicles == 1 ? "" : "s")}");
        Say(sim, j, a.Kind, $"{what}: −{damage} to {string.Join(" and ", parts)}");
    }

    private void Say(Simulation sim, JackRaid j, string kind, string text) => _jackEvents.Add(new JackEvent(sim.Tick, j.Player, kind, text));

    private static ulong JackHash(long tick, int player, ulong salt)
    {
        var z = (ulong)tick * 0x9E3779B97F4A7C15UL + (ulong)(player + 1) * 0xBF58476D1CE4E5B9UL + salt * 0x94D049BB133111EBUL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    internal string? JackDebugProblem(Simulation sim, DebugJackCommand c)
    {
        if (c.Target < 0 || c.Target >= _players.Count) return "no such player";
        if (_players[c.Target].Phase != LandingPhase.Landed) return "that player hasn't landed yet";
        if (!Bases(c.Target).Any()) return "no colony to raid";
        var j = JackOf(c.Target);
        return j != null && j.PhaseAt(sim.Tick) != JackPhase.Gone ? "Jack is already here" : null;
    }

    internal void ApplyJackDebug(Simulation sim, DebugJackCommand c) => StartJack(sim, c.Target);

    /// <summary>Only once a raid exists or the switch is on, so games without Jack keep their old hashes.</summary>
    private void AddJackToHash(ref StateHasher h)
    {
        if (!JackEnabled && _jacks.Count == 0) return;
        h.Add(JackEnabled);
        h.Add(_jacks.Count);
        foreach (var j in _jacks) j.AddToHash(ref h);
        h.Add(_jackAttacks.Count);
        foreach (var a in _jackAttacks) { h.Add(a.Tick); h.Add(a.Player); h.Add(a.FromTile); h.Add(a.Tile); h.Add(a.LandTick); h.Add(a.Landed); h.Add(a.Damage); h.Add(a.Hits); }
    }
}
