using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Missions;

/// <summary>A space-chain marker: Kind "beacon" | "pickup" | "dropoff" | "target" | "ship"; BodyId (null for a ship);
/// ShipId (-1 unless Kind is "ship"); Label for the plot.</summary>
public sealed record SpaceMarker(string Kind, string? BodyId, int ShipId, string Label);

/// <summary>Where a piece of mission cargo is: at its pick-up body, aboard the flagship, or delivered.</summary>
public enum CargoState { Waiting, Aboard, Delivered }

// Space chains (docs/ship-pick-contract.md B.3-4): the conditions, actions and cargo of the missions a picked ship that
// never lands plays on the helm's system display. Everything reads the star system (Simulation.System) and the player's
// flagship; randomness is a hash of the system seed, player, mission and binding (never sim.Rng). Mission cargo lives here
// (PlayerBook.Cargo), not in SystemShip.Cargo (that is the neutral traffic's trade goods).
public sealed partial class MissionBook
{
    /// <summary>One piece of mission cargo (load_cargo): Id groups pieces a condition counts ("mail"); Key = the action's
    /// raw label (loading again with the same mission, id and key replaces it: a retried run); To = drop-off body; From =
    /// pick-up body (null = aboard from the start).</summary>
    internal sealed class CargoRun
    {
        public string MissionId = "", Id = "", Key = "", Label = "", To = "";
        public string? From;
        public CargoState State;
        public long Tick;

        public void AddToHash(ref StateHasher h)
        {
            h.Add(MissionId);
            h.Add(Id);
            h.Add(Key);
            h.Add(Label);
            h.Add(To);
            h.Add(From != null);
            if (From != null) h.Add(From);
            h.Add((int)State);
            h.Add(Tick);
        }
    }

    private StarSystemState? Sys => _sim?.System;

    private SystemShip? Flagship(int p) => Sys?.FlagshipOf(p);

    /// <summary>The body a reference names: a body binding (bind_body); null when there is none.</summary>
    private static string? BodyRef(MissionRun? run, string? name) => name != null && run?.Find(name) is { Body: { } body } ? body : null;

    /// <summary>The ship a reference names (bind_ship), or null.</summary>
    private SystemShip? ShipRef(MissionRun? run, string? name) =>
        name != null && run?.Find(name) is { Ship: > 0 } b ? Sys?.FindShip(b.Ship) : null;

    private int BodyIndex(string? id)
    {
        if (Sys is not { } sys || id == null) return -1;
        for (var i = 0; i < sys.Bodies.Count; i++)
            if (sys.Bodies[i].Id == id) return i;
        return -1;
    }

    // ---------------------------------------------------------------- conditions

    /// <summary>The space conditions (MissionConditions.Check's default). False for anything else, or without a star system.</summary>
    private MissionEval SpaceCheck(int p, MissionRun? run, int obj, MissionCondDef c)
    {
        if (Sys is not { } sys || Flagship(p) is not { } ship) return MissionEval.No;
        switch (c.Kind)
        {
            case "ship_at":
                return MissionEval.Of(BodyRef(run, c.F("body")) is { } at && ship.AtBody == at);
            case "visited":
            {
                if (run == null || obj < 0) return MissionEval.No;
                if (ship.AtBody != null && ship.ArriveTick >= run.StageStart && BodyIndex(ship.AtBody) is var i and >= 0) Remember(run, obj * 65536 + i);
                return MissionEval.Count(Remembered(run, obj), c.I("count", 1));
            }
            case "charted":
            {
                var charted = sys.Bodies.Count(b => sys.KnowledgeOf(p, b.Id) == BodyKnowledge.Charted);
                if (c.F("share") != null)
                {
                    var share = c.N("share", Fix64.One);
                    var total = sys.Bodies.Count;
                    var pct = total == 0 ? 0 : charted * 100 / total;
                    return new MissionEval(total > 0 && Fix64.FromInt(charted) >= share * Fix64.FromInt(total), Fix64.FromInt(pct), (share * Fix64.FromInt(100)).FloorToInt() is var tp ? Fix64.FromInt(tp) : Fix64.Zero);
                }
                var since = run != null && obj >= 0 && obj < run.Base.Length ? run.Base[obj].FloorToInt() : 0;
                var need = c.I("count", 1);
                return MissionEval.Count(System.Math.Max(0, charted - since), need) with { Target = Fix64.FromInt(need) };
            }
            case "scanned":
            {
                if (run == null || obj < 0) return MissionEval.No;
                if (ship.LastScanTick >= 0 && ship.LastScanTick >= run.StageStart)
                {
                    var key = ((long)obj << 40) | ship.LastScanTick;
                    if (!run.Marks.Contains(key))
                    {
                        run.Marks.Add(key);
                        run.Marks.Sort();
                    }
                }
                var n = run.Marks.Count(m => m >> 40 == obj);
                return MissionEval.Count(n, c.I("count", 1));
            }
            case "swept":
            {
                if (run == null || obj < 0) return MissionEval.No;
                var want = c.F("body") != null ? BodyRef(run, c.F("body")) : null;
                if (c.F("body") != null && want == null) return MissionEval.No;
                var orbit = ship.AtBody;
                if (orbit != null && (want == null || orbit == want) && sys.KnowledgeOf(p, orbit) == BodyKnowledge.Charted
                    && Now - System.Math.Max(ship.ArriveTick, run.StageStart) >= sys.SweepTicks && BodyIndex(orbit) is var i and >= 0)
                    Remember(run, obj * 65536 + i);
                if (want != null) return MissionEval.Of(BodyIndex(want) is var wi and >= 0 && run.Visited.Contains(obj * 65536 + wi));
                return MissionEval.Count(Remembered(run, obj), c.I("count", 1));
            }
            case "system_satellites":
                return MissionEval.Count(sys.Satellites.Count(s => s.Owner == p), c.I("count", 1));
            case "cargo":
            {
                if (run == null || c.F("id") is not { } id) return MissionEval.No;
                var mine = Track(p).Cargo.Where(x => x.MissionId == run.Id && x.Id == id).ToList();
                if (mine.Count == 0) return MissionEval.No;
                var want = c.F("state") == "aboard" ? CargoState.Aboard : CargoState.Delivered;
                var n = mine.Count(x => x.State == want);
                var need = c.F("count") != null ? c.I("count", 1) : mine.Count;
                return MissionEval.Count(n, need) with { Target = need > 1 ? Fix64.FromInt(need) : Fix64.Zero };
            }
            case "with_ship":
            {
                if (ShipRef(run, c.F("ship")) is not { } other) return MissionEval.No;
                if (c.F("at_arrival") == "true") return MissionEval.Of(run != null && obj >= 0 && WaitedForArrival(run, obj, ship, other));
                var together = (ship.AtBody != null && ship.AtBody == other.AtBody)
                    || (ship.AtBody == null && other.AtBody == null && ship.ToBody != null && ship.ToBody == other.ToBody);
                return MissionEval.Of(together);
            }
            case "contact":
            {
                if (BodyRef(run, c.F("body")) is not { } body) return MissionEval.No;
                switch (c.F("stage") ?? "met")
                {
                    case "post":
                        return MissionEval.Of(sys.Posts.Any(x => x.Owner == p && x.BodyId == body));
                    case "studied":
                    {
                        var study = sys.StudyOf(p, body);
                        return new MissionEval(study >= Fix64.One, Fix64.Min(Fix64.FromInt(100), (study * Fix64.FromInt(100))), Fix64.FromInt(100));
                    }
                    default:
                        return MissionEval.Of(sys.RelationsWith(p, body) >= ContactStage.Contact);
                }
            }
            default:
                return MissionEval.No;
        }
    }

    /// <summary>Marks of with_ship at_arrival: "the flagship was waiting in orbit of body (index) for the ship's arrival at
    /// tick", per objective (bit 62 set, so scanned's marks never match).</summary>
    private static long WaitMark(int obj, int body, long tick) => (1L << 62) | ((long)obj << 48) | ((long)body << 32) | (tick & 0xFFFFFFFFL);

    /// <summary>with_ship at_arrival: the flagship was in orbit of the ship's destination when it arrived there (during
    /// this stage). Checked once a second, so it remembers seeing the flagship waiting there for that arrival (Marks):
    /// a flagship that leaves right after the ship came in still counts.</summary>
    private bool WaitedForArrival(MissionRun run, int obj, SystemShip me, SystemShip other)
    {
        // Both there now, the flagship first, the ship arrived this stage.
        if (other.AtBody != null && other.ArriveTick >= run.StageStart && me.AtBody == other.AtBody && me.ArriveTick <= other.ArriveTick) return true;
        // Waiting at its destination while it flies in: remember it.
        if (other.AtBody == null && other.ToBody != null && me.AtBody == other.ToBody && me.ArriveTick <= other.ArriveTick
            && BodyIndex(other.ToBody) is var at and >= 0 && WaitMark(obj, at, other.ArriveTick) is var mark && !run.Marks.Contains(mark))
        {
            run.Marks.Add(mark);
            run.Marks.Sort();
        }
        foreach (var m in run.Marks)
        {
            if (m >> 62 != 1 || (int)((m >> 48) & 0x3FFF) != obj) continue;
            var body = Sys!.Bodies[(int)((m >> 32) & 0xFFFF)].Id;
            var tick = m & 0xFFFFFFFFL;
            var arrived = (other.AtBody == body && other.ArriveTick == tick) || (other.FromBody == body && other.DepartTick >= tick);
            var stayed = (me.AtBody == body && me.ArriveTick <= tick) || (me.FromBody == body && me.DepartTick >= tick);
            if (arrived && stayed) return true;
        }
        return false;
    }

    private static void Remember(MissionRun run, int key)
    {
        if (run.Visited.Contains(key)) return;
        run.Visited.Add(key);
        run.Visited.Sort();
    }

    private static int Remembered(MissionRun run, int obj) => run.Visited.Count(v => v / 65536 == obj);

    /// <summary>The counter target of a space condition before it is checked (0 = yes / no).</summary>
    private static Fix64 SpaceTarget(MissionCondDef c) => c.Kind switch
    {
        "charted" when c.F("share") != null => c.N("share", Fix64.One) * Fix64.FromInt(100),
        "charted" => Fix64.FromInt(c.I("count", 1)),
        "contact" when c.F("stage") == "studied" => Fix64.FromInt(100),
        "visited" or "scanned" or "system_satellites" or "swept" or "cargo" when c.I("count", 1) > 1 => Fix64.FromInt(c.I("count", 1)),
        _ => Fix64.Zero,
    };

    /// <summary>What a space counter that counts "since the stage began" starts from: charted {count} = bodies charted now.</summary>
    private Fix64 SpaceBaseline(int p, MissionCondDef c) =>
        c.Kind == "charted" && c.F("share") == null && Sys is { } sys
            ? Fix64.FromInt(sys.Bodies.Count(b => sys.KnowledgeOf(p, b.Id) == BodyKnowledge.Charted)) : Fix64.Zero;

    // ---------------------------------------------------------------- actions

    /// <summary>The space actions (bind_body, bind_ship, place_beacon, load_cargo); false when the kind isn't one.</summary>
    private bool SpaceAct(int p, MissionRun run, MissionDef d, MissionActionDef a)
    {
        switch (a.Kind)
        {
            case "bind_body":
            {
                if (a.F("as") is not { } name) return true;
                var body = PickBody(p, run, a.F("pick") ?? "random_other", Excluded(run, a), name);
                var b = new Binding { Name = name, Body = body, Text = body != null ? Sys!.Body(body)!.Name : "an unknown body" };
                run.Bind(b);
                if (body != null) Sys!.MissionIdentify(p, body); // the mission names it: its name shows from now on
                return true;
            }
            case "bind_ship":
            {
                if (a.F("as") is not { } name) return true;
                var ship = PickShip(p, run, a.F("pick") ?? "freighter", name);
                run.Bind(new Binding { Name = name, Ship = ship?.Id ?? 0, Text = ship != null ? ShipText(ship) : "an unknown ship" });
                return true;
            }
            case "place_beacon":
            {
                if (BodyRef(run, a.F("at")) is not { } body) return true;
                run.Markers.Add(new MarkerRun { Kind = "beacon", Body = body, Label = Fill(p, run, a.F("label") ?? "") });
                return true;
            }
            case "load_cargo":
            {
                if (a.F("id") is not { } id || BodyRef(run, a.F("to")) is not { } to) return true;
                var from = a.F("from") != null ? BodyRef(run, a.F("from")) : null;
                var key = a.F("label") ?? id;
                var book = Track(p);
                book.Cargo.RemoveAll(x => x.MissionId == run.Id && x.Id == id && x.Key == key); // loaded again (a retried run)
                var cargo = new CargoRun
                {
                    MissionId = run.Id, Id = id, Key = key, Label = Fill(p, run, key), To = to, From = from, Tick = Now,
                    State = from == null ? CargoState.Aboard : CargoState.Waiting,
                };
                book.Cargo.Add(cargo);
                if (cargo.State == CargoState.Waiting && Flagship(p)?.AtBody == from) Load(p, run, d, cargo); // already there
                return true;
            }
        }
        return false;
    }

    /// <summary>A marker action on a body or ship binding: a "target" ring on the body, or the ship highlighted. False when
    /// the reference isn't a space binding (the surface marker runs instead).</summary>
    private bool SpaceMarker(int p, MissionRun run, MissionActionDef a)
    {
        if (a.F("at") is not { } at || run.Find(at) is not { } b || (b.Body == null && b.Ship <= 0)) return false;
        var label = Fill(p, run, a.F("label") ?? "");
        run.Markers.Add(b.Body != null ? new MarkerRun { Kind = "target", Body = b.Body, Label = label } : new MarkerRun { Kind = "ship", Ship = b.Ship, Label = label });
        return true;
    }

    /// <summary>Bodies the action excludes: its exclude bindings (a field or a list).</summary>
    private static List<string> Excluded(MissionRun run, MissionActionDef a)
    {
        var names = a.L("exclude").ToList();
        if (a.F("exclude") is { } one) names.Add(one);
        var list = new List<string>();
        foreach (var n in names)
            if (BodyRef(run, n) is { } body) list.Add(body);
        return list;
    }

    /// <summary>Gas giants and ice bodies (bind_body gas_or_ice).</summary>
    private static readonly string[] GasOrIce = { "class_j", "class_t", "ice_moon" };

    /// <summary>The body a bind_body pick names (never the one the flagship is at or heading to, except "here"; never an
    /// excluded one), or null when the system has nothing left. Falls back to nearest_other when a pick finds nothing.</summary>
    private string? PickBody(int p, MissionRun run, string pick, List<string> exclude, string name)
    {
        if (Sys is not { } sys || Flagship(p) is not { } ship) return null;
        var here = ship.AtBody ?? ship.ToBody;
        if (pick == "here") return here;
        var at = sys.ShipPosition(ship, Now);
        var options = sys.Bodies.Where(b => b.Id != here && !exclude.Contains(b.Id)).ToList();
        if (options.Count == 0) return null;
        Fix64 FromStar(BodyDef b) => (b.Parent != null && sys.Body(b.Parent) is { } parent ? parent.Position : b.Position).Length();
        Fix64 FromShip(BodyDef b) => FixVec2.Distance(b.Position, at);
        BodyDef? Best(IEnumerable<BodyDef> list, System.Func<BodyDef, Fix64> key, bool most)
        {
            BodyDef? best = null;
            var bestKey = Fix64.Zero;
            foreach (var b in list)
            {
                var k = key(b);
                if (best == null || (most ? k > bestKey : k < bestKey)) (best, bestKey) = (b, k);
            }
            return best;
        }
        var nearest = Best(options, FromShip, most: false);
        switch (pick)
        {
            case "farthest":
                return (Best(options.Where(b => b.Parent == null), FromStar, most: true) ?? nearest)?.Id;
            case "outermost_moon":
                return (Best(options.Where(b => b.Parent != null), FromStar, most: true) ?? Best(options.Where(b => b.Parent == null), FromStar, most: true) ?? nearest)?.Id;
            case "gas_or_ice":
            {
                var gas = options.Where(b => GasOrIce.Contains(b.ClassId)).ToList();
                if (gas.Count == 0) // a system with none: the biggest bodies instead
                {
                    var biggest = options.Max(b => b.Size);
                    gas = options.Where(b => b.Size == biggest).ToList();
                }
                return (Best(gas.Where(b => sys.KnowledgeOf(p, b.Id) < BodyKnowledge.Charted), FromShip, most: false) ?? Best(gas, FromShip, most: false) ?? nearest)?.Id;
            }
            case "civ":
                foreach (var c in sys.Civilizations)
                    if (c.Living && options.Any(b => b.Id == c.BodyId)) return c.BodyId;
                foreach (var c in sys.Civilizations)
                    if (c.Living) return c.BodyId; // the flagship is there already
                return nearest?.Id;
            case "random_other":
                return options[(int)(SpaceRoll(p, run.Id, name, 0) % (ulong)options.Count)].Id;
            default: // nearest_other
                return nearest?.Id;
        }
    }

    /// <summary>The ship a bind_body pick names: freighter = a civilian freighter of the traffic (any civilian ship when
    /// there is none), picked by the hash; departing = the civilian ship whose next arrival the flagship can beat by the
    /// most (in flight: where it is heading; in orbit: where it goes next), so it can be met there.</summary>
    private SystemShip? PickShip(int p, MissionRun run, string pick, string name)
    {
        if (Sys is not { } sys || Flagship(p) is not { } me) return null;
        var traffic = sys.Ships.Where(s => s.IsNeutral).ToList();
        if (traffic.Count == 0) return null;
        if (pick == "departing")
        {
            SystemShip? best = null;
            var bestMargin = long.MinValue;
            var from = me.AtBody ?? me.ToBody;
            foreach (var s in traffic)
            {
                if (sys.NextDestination(s) is not { } to) continue;
                var mine = from == null || from == to ? 0 : sys.TravelTicksFor(me, from, to) + (me.AtBody == null ? me.ArriveTick - Now : 0);
                var margin = sys.NextArrivalTick(s) - Now - mine;
                if (margin > bestMargin) (best, bestMargin) = (s, margin);
            }
            return best;
        }
        var freighters = traffic.Where(s => s.Role == "freighter").ToList();
        if (freighters.Count == 0) freighters = traffic;
        return freighters[(int)(SpaceRoll(p, run.Id, name, 1) % (ulong)freighters.Count)];
    }

    private static string ShipText(SystemShip s) => s.Registry.Length > 0 ? $"{s.Name} ({s.Registry})" : s.Name;

    private ulong SpaceRoll(int p, string mission, string what, int k) =>
        Mix(Mix(Mix((Sys?.Seed ?? 0) ^ Fnv(mission)) ^ Fnv(what) ^ (ulong)(uint)p * 0x51ED27UL) + (ulong)(uint)k);

    /// <summary>A body or ship binding's placeholder: its name, or ".distance" = the flagship's flight time there in seconds.</summary>
    private string SpaceFill(int p, Binding b, string prop)
    {
        if (prop == "distance" && b.Body != null && Sys is { } sys && Flagship(p) is { } ship && (ship.AtBody ?? ship.ToBody) is { } from)
            return ((sys.TravelTicksFor(ship, from, b.Body) + Simulation.TickRate - 1) / Simulation.TickRate).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (prop == "distance") return "?";
        return b.Text;
    }

    // ---------------------------------------------------------------- cargo

    /// <summary>Once a second for a space chain (before the missions are checked): cargo waiting at the body the flagship
    /// is in orbit of goes aboard; cargo aboard is delivered at its drop-off body.</summary>
    private void StepCargo(int p, PlayerBook book)
    {
        if (book.Cargo.Count == 0 || Flagship(p) is not { AtBody: { } at }) return;
        foreach (var cargo in book.Cargo.ToList())
        {
            if (RunOf(p, cargo.MissionId) is not { State: MissionState.Active } run || Def(run) is not { } d) continue;
            if (cargo.State == CargoState.Waiting && cargo.From == at) Load(p, run, d, cargo);
            else if (cargo.State == CargoState.Aboard && cargo.To == at)
            {
                cargo.State = CargoState.Delivered;
                cargo.Tick = Now;
                Say(p, run, d.Giver, $"{cargo.Label}: delivered to {Sys!.Body(cargo.To)?.Name ?? cargo.To}.", "feed");
            }
        }
    }

    private void Load(int p, MissionRun run, MissionDef d, CargoRun cargo)
    {
        cargo.State = CargoState.Aboard;
        cargo.Tick = Now;
        Say(p, run, d.Giver, $"{cargo.Label}: aboard, for {Sys?.Body(cargo.To)?.Name ?? cargo.To}.", "feed");
    }

    /// <summary>A mission ended (or was reset): its cargo goes.</summary>
    private void DropCargo(int p, MissionRun run)
    {
        if (p >= 0 && p < _players.Count) _players[p].Cargo.RemoveAll(x => x.MissionId == run.Id);
    }

    // ---------------------------------------------------------------- views

    /// <summary>Space-chain markers on the system plot (docs/ship-pick-contract.md) for the player's offered and active
    /// missions: "target" rings on bound bodies (marker actions), "beacon"s (place_beacon), bound ships ("ship"; a ship in
    /// flight also gets a "target" on where it is heading), and the cargo's "pickup" (waiting) and "dropoff" (aboard) bodies.</summary>
    public IReadOnlyList<SpaceMarker> SpaceMarkers(int player)
    {
        if (!Enabled || player < 0 || player >= _players.Count || Sys is not { } sys) return System.Array.Empty<SpaceMarker>();
        var list = new List<SpaceMarker>();
        foreach (var run in _players[player].Runs)
        {
            if (run.State is not (MissionState.Active or MissionState.Offered)) continue;
            foreach (var m in run.Markers)
            {
                if (!m.InSpace) continue;
                if (m.Ship > 0)
                {
                    if (sys.FindShip(m.Ship) is not { } s) continue;
                    list.Add(new SpaceMarker("ship", null, s.Id, m.Label));
                    if (s.AtBody == null && s.ToBody != null) list.Add(new SpaceMarker("target", s.ToBody, -1, $"{s.Name}: heading here"));
                    else if (sys.NextDestination(s) is { } next) list.Add(new SpaceMarker("target", next, -1, $"{s.Name}: next stop"));
                }
                else list.Add(new SpaceMarker(m.Kind.Length > 0 ? m.Kind : "target", m.Body, -1, m.Label));
            }
        }
        foreach (var c in _players[player].Cargo)
        {
            if (c.State == CargoState.Waiting && c.From != null) list.Add(new SpaceMarker("pickup", c.From, -1, c.Label));
            else if (c.State == CargoState.Aboard) list.Add(new SpaceMarker("dropoff", c.To, -1, c.Label));
        }
        return list;
    }

    /// <summary>Mission cargo aboard this player's flagship, as labels ("Vaccine crates → Corsa"), in loading order.</summary>
    public IReadOnlyList<string> CargoAboard(int player)
    {
        if (!Enabled || player < 0 || player >= _players.Count) return System.Array.Empty<string>();
        var list = new List<string>();
        foreach (var c in _players[player].Cargo)
            if (c.State == CargoState.Aboard) list.Add($"{c.Label} → {Sys?.Body(c.To)?.Name ?? c.To}");
        return list;
    }
}
