using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Space;

// CONTRACT (2026-11-08, "system view: ships, travel, system fog, generated systems"). Shapes are fixed so the view
// agent can build against them. See docs/system-contract.md. Filled in by the sim branch (2026-11-08): flight, the three
// fog levels, body satellites, long-range scans and neutral traffic, all in the state hash.

/// <summary>What a player knows about a body. Live readings are separate (<see cref="StarSystemState.HasLiveData"/>).</summary>
public enum BodyKnowledge
{
    /// <summary>Not even known to exist (not used at start: every body is at least Detected).</summary>
    Unknown,
    /// <summary>Position and rough size only.</summary>
    Detected,
    /// <summary>Class, moons, gravity, atmosphere, the resources that may be there; kept for good once learned.</summary>
    Charted,
}

/// <summary>A ship in the star system: ours, a rival's, or neutral traffic (Owner -1).</summary>
public sealed class SystemShip
{
    public required int Id { get; init; }
    /// <summary>Player slot, or -1 for neutral traffic.</summary>
    public required int Owner { get; init; }
    /// <summary>ships.json class id (neutral traffic uses civilian classes).</summary>
    public required string ClassId { get; init; }
    public required string Name { get; init; }
    public string Registry { get; init; } = "";
    /// <summary>Body it is in orbit of, or null while in flight.</summary>
    public string? AtBody { get; set; }
    /// <summary>In flight: where from / to and when (sim ticks).</summary>
    public string? FromBody { get; set; }
    public string? ToBody { get; set; }
    public long DepartTick { get; set; }
    public long ArriveTick { get; set; }
    /// <summary>What it carries, for the info card (neutral traffic: "deuterium", "ore", "colonists"…), or null.</summary>
    public string? Cargo { get; set; }

    // ---- added by the sim branch

    /// <summary>The faction it belongs to (a player's faction; neutral traffic: its class's faction).</summary>
    public string Faction { get; init; } = "";

    /// <summary>ships.json role of its class ("freighter", "tanker", "science", "colony"; "combat" for warships).</summary>
    public string Role { get; init; } = "";

    /// <summary>Neutral traffic: true.</summary>
    public bool IsNeutral => Owner < 0;

    /// <summary>In flight (not in orbit of anything).</summary>
    public bool InFlight => AtBody == null;

    /// <summary>When its long-range scanners are ready again (sim tick).</summary>
    public long ScanReadyTick { get; internal set; }

    /// <summary>When it last ran a long-range scan (sim tick; -1 = never), for the view's sweep effect.</summary>
    public long LastScanTick { get; internal set; } = -1;

    /// <summary>Neutral traffic: trips flown so far (seeds its next destination) and when it leaves orbit next.</summary>
    public int Trips { get; internal set; }
    public long LeaveTick { get; internal set; }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(ClassId);
        h.Add(Name);
        h.Add(Registry);
        h.Add(Faction);
        h.Add(Role);
        Text(ref h, AtBody);
        Text(ref h, FromBody);
        Text(ref h, ToBody);
        h.Add(DepartTick);
        h.Add(ArriveTick);
        Text(ref h, Cargo);
        h.Add(ScanReadyTick);
        h.Add(LastScanTick);
        h.Add(Trips);
        h.Add(LeaveTick);
    }

    internal static void Text(ref StateHasher h, string? s)
    {
        h.Add(s != null);
        if (s != null) h.Add(s);
    }
}

/// <summary>A satellite a player left at a body: live readings there for good.</summary>
public sealed record SystemSatellite(int Owner, string BodyId, long PlacedTick);

/// <summary>What a player knows about a body, for the body card (fields beyond the knowledge level are null or empty).</summary>
/// <param name="Name">Charted: the body's name; Detected: null (an unknown body).</param>
/// <param name="RoughSize">"small", "medium", "large" or "giant": what a Detected body shows.</param>
/// <param name="Moons">Charted: the ids of its moons.</param>
/// <param name="GravityG">Charted: surface gravity (null for a body with no surface: giants, belts).</param>
/// <param name="PressureKpa">Charted: sea-level pressure (0 = airless; null when unknown).</param>
/// <param name="Atmosphere">Charted: main gases in percent.</param>
/// <param name="MeanTemperatureC">Charted: mean temperature from its sunlight, albedo and greenhouse (null without climate data).</param>
/// <param name="Landable">Charted: it has a playable surface.</param>
/// <param name="Resources">Charted: resources that may be found there (<see cref="StarSystemState.PossibleResources"/>).</param>
/// <param name="Zone">The orbit zone ("inner", "habitable", "outer"): what its distance from the star says.</param>
public sealed record BodyReport(string BodyId, BodyKnowledge Knowledge, bool Live, FixVec2 Position, int Size, string RoughSize, string Zone,
    string? ParentId, string? Name, string? ClassId, string? ClassName, IReadOnlyList<string> Moons, Fix64? GravityG, Fix64? PressureKpa,
    IReadOnlyList<GasAmount> Atmosphere, Fix64? MeanTemperatureC, bool Landable, IReadOnlyList<string> Resources);

/// <summary>
/// The star system of a game: its star, its bodies (static positions, MapDef units, like maps.json), the ships, and
/// each player's knowledge of every body. Built by <see cref="SystemGenerator"/> from the seed (or from a maps.json
/// map). Lives on <c>Simulation.System</c> (null for games without one, e.g. the balance runner's planet-only games).
/// State changes only through commands (<see cref="SetCourseCommand"/>, <see cref="LongRangeScanCommand"/>,
/// <see cref="SystemSatelliteCommand"/>) and <see cref="Step"/>; all of it is in <see cref="AddToHash"/>.
/// </summary>
public sealed partial class StarSystemState
{
    public StarDef Star { get; init; } = new("Kalandra", "G", Fix64.FromInt(40));
    public IReadOnlyList<BodyDef> Bodies { get; init; } = new List<BodyDef>();
    public IReadOnlyList<SystemShip> Ships => _ships;
    private readonly List<SystemShip> _ships = new();

    /// <summary>The body the colony surface belongs to (where the game's PlanetSurface is), or null.</summary>
    public string? SurfaceBody { get; init; }

    /// <summary>The seed the system was generated from (its hash streams: neutral traffic).</summary>
    public ulong Seed { get; init; }

    /// <summary>The generated system (zones, climates), or null for a system built from a maps.json map.</summary>
    public GeneratedSystem? Generated { get; init; }

    /// <summary>Satellites left at bodies, in the order they went up.</summary>
    public IReadOnlyList<SystemSatellite> Satellites => _satellites;
    private readonly List<SystemSatellite> _satellites = new();

    /// <summary>Per player slot, the knowledge of each body (index = position in <see cref="Bodies"/>).</summary>
    private readonly List<BodyKnowledge[]> _knowledge = new();

    private int _nextShipId = 1;

    /// <summary>The game this system belongs to (set by <see cref="Attach"/>; data, tick, surface).</summary>
    private Simulation? _sim;

    private GameData? Data => _sim?.Data;
    private long Now => _sim?.Tick ?? 0;

    /// <summary>Joins a game: from then on <c>sim.System</c> is this. Call before adding players.</summary>
    internal void Attach(Simulation sim) => _sim = sim;

    // ---------------------------------------------------------------- the contract

    public BodyKnowledge KnowledgeOf(int player, string bodyId)
    {
        var i = IndexOf(bodyId);
        if (i < 0 || player < 0) return BodyKnowledge.Unknown;
        return player < _knowledge.Count ? _knowledge[player][i] : BodyKnowledge.Detected;
    }

    /// <summary>True while the player has a satellite at the body (or its colony there with the uplink up).</summary>
    public bool HasLiveData(int player, string bodyId)
    {
        foreach (var s in _satellites)
            if (s.Owner == player && s.BodyId == bodyId) return true;
        return bodyId == SurfaceBody && _sim?.Surface is { } surface && surface.UplinkOnline(player, Now);
    }

    /// <summary>The player's own ship (their flagship), or null.</summary>
    public SystemShip? FlagshipOf(int player)
    {
        if (player < 0) return null;
        foreach (var s in _ships)
            if (s.Owner == player) return s;
        return null;
    }

    /// <summary>Where a ship is on the plot at a tick (MapDef units; interpolated in flight).</summary>
    public FixVec2 ShipPosition(SystemShip ship, long tick)
    {
        if (ship.AtBody != null) return Body(ship.AtBody)?.Position ?? FixVec2.Zero;
        var from = ship.FromBody != null ? Body(ship.FromBody) : null;
        var to = ship.ToBody != null ? Body(ship.ToBody) : null;
        if (from == null || to == null) return to?.Position ?? from?.Position ?? FixVec2.Zero;
        return from.Position + (to.Position - from.Position) * FlightShare(ship, tick);
    }

    /// <summary>Flight time between two bodies in ticks (impulse; 20-90 s by distance: maps/system.json travel).</summary>
    public long TravelTicks(string fromBody, string toBody)
    {
        var a = Body(fromBody);
        var b = Body(toBody);
        if (a == null || b == null || a.Id == b.Id) return 0;
        var r = Data?.SystemRules;
        var min = r?.TravelMinSeconds ?? 20;
        var max = r?.TravelMaxSeconds ?? 90;
        var near = r?.TravelNear ?? Fix64.Zero;
        var far = r?.TravelFar ?? Fix64.FromInt(1000);
        var share = Fix64.Clamp((FixVec2.Distance(a.Position, b.Position) - near) / (far - near), Fix64.Zero, Fix64.One);
        var seconds = Fix64.FromInt(min) + Fix64.FromInt(max - min) * share;
        return System.Math.Max(1L, (seconds * Simulation.TickRate).FloorToInt());
    }

    /// <summary>Why the ship can't set course there now, or null.</summary>
    public string? CourseProblem(int player, int shipId, string bodyId)
    {
        var ship = FindShip(shipId);
        if (ship == null) return "no such ship";
        if (ship.Owner != player || player < 0) return "not your ship";
        if (bodyId == null || Body(bodyId) == null || KnowledgeOf(player, bodyId) == BodyKnowledge.Unknown) return "no such body";
        if (ship.AtBody == null)
            return $"in flight to {NameFor(player, ship.ToBody!)} (arrives in {SecondsUntil(ship.ArriveTick)} s)";
        if (ship.AtBody == bodyId) return $"already in orbit of {NameFor(player, bodyId)}";
        return null;
    }

    /// <summary>Resources that may be found on a body as the player knows it (Charted: from its class's yields and any
    /// survey; Detected: none). Ids from resources.json.</summary>
    public IReadOnlyList<string> PossibleResources(int player, string bodyId)
    {
        var list = new List<string>();
        var body = Body(bodyId);
        var data = Data;
        if (body == null || data == null || KnowledgeOf(player, bodyId) < BodyKnowledge.Charted) return list;
        if (data.PlanetClasses.FirstOrDefault(c => c.Id == body.ClassId) is { } cls)
            foreach (var y in cls.Yields)
                if (data.Resources.Any(r => r.Id == y) && !list.Contains(y)) list.Add(y);
        // What the colony's rovers have found on its own world.
        if (bodyId == SurfaceBody && _sim?.Surface is { } surface && player < surface.Players.Count)
        {
            var map = surface.Map;
            var surveyed = surface.Players[player].Surveyed;
            var found = new bool[data.Resources.Count];
            for (var t = 0; t < map.Grid.Count; t++)
                if (surveyed[t] && map.DepositResource[t] >= 0 && map.DepositResource[t] < found.Length) found[map.DepositResource[t]] = true;
            for (var r = 0; r < found.Length; r++)
                if (found[r] && !list.Contains(data.Resources[r].Id)) list.Add(data.Resources[r].Id);
        }
        return list;
    }

    // ---------------------------------------------------------------- more for the view

    public BodyDef? Body(string? id)
    {
        if (id == null) return null;
        foreach (var b in Bodies)
            if (b.Id == id) return b;
        return null;
    }

    public SystemShip? FindShip(int id)
    {
        foreach (var s in _ships)
            if (s.Id == id) return s;
        return null;
    }

    /// <summary>The moons of a body, in system order.</summary>
    public IReadOnlyList<BodyDef> MoonsOf(string bodyId) => Bodies.Where(b => b.Parent == bodyId).ToList();

    /// <summary>A body's name as the player knows it: its name once Charted, else "unknown body".</summary>
    public string NameFor(int player, string bodyId) =>
        KnowledgeOf(player, bodyId) >= BodyKnowledge.Charted ? Body(bodyId)?.Name ?? bodyId : UnknownBodyName;

    public const string UnknownBodyName = "unknown body";

    /// <summary>The orbit zone of a body ("inner", "habitable", "outer"; "" for a system from a maps.json map).</summary>
    public string ZoneOf(string bodyId) => Generated?.Entry(bodyId)?.Zone ?? "";

    /// <summary>"small" (size 1), "medium" (2-3), "large" (4-5) or "giant" (6+): what a Detected body shows.</summary>
    public static string RoughSize(int size) => size <= 1 ? "small" : size <= 3 ? "medium" : size <= 5 ? "large" : "giant";

    /// <summary>Everything the player knows about a body, for its card.</summary>
    public BodyReport? Report(int player, string bodyId)
    {
        var body = Body(bodyId);
        var data = Data;
        if (body == null) return null;
        var knowledge = KnowledgeOf(player, bodyId);
        var live = HasLiveData(player, bodyId);
        if (knowledge < BodyKnowledge.Charted || data == null)
            return new BodyReport(bodyId, knowledge, live, body.Position, body.Size, RoughSize(body.Size), ZoneOf(bodyId), body.Parent,
                null, null, null, new List<string>(), null, null, new List<GasAmount>(), null, false, new List<string>());
        var env = data.EnvironmentOf(body.ClassId);
        var cls = data.PlanetClasses.FirstOrDefault(c => c.Id == body.ClassId);
        var climate = Generated != null ? Climate.Summary(data, Generated.Climate, bodyId) : Climate.Summary(data, bodyId);
        var gravity = Generated?.Climate.Body(bodyId)?.GravityG ?? data.Climate?.Body(bodyId)?.GravityG ?? env?.GravityG;
        return new BodyReport(bodyId, knowledge, live, body.Position, body.Size, RoughSize(body.Size), ZoneOf(bodyId), body.Parent,
            body.Name, body.ClassId, cls?.Name ?? body.ClassId, MoonsOf(bodyId).Select(m => m.Id).ToList(), gravity, env?.PressureKpa,
            env?.Atmosphere ?? new List<GasAmount>(), climate?.MeanTemperatureC, data.IsLandableClass(body.ClassId), PossibleResources(player, bodyId));
    }

    /// <summary>Seconds (rounded up) until a ship in flight arrives; 0 in orbit.</summary>
    public int EtaSeconds(SystemShip ship, long tick) => ship.AtBody != null ? 0 : (int)System.Math.Max(0, (ship.ArriveTick - tick + Simulation.TickRate - 1) / Simulation.TickRate);

    /// <summary>Direction of travel (unit vector) of a ship in flight, or zero in orbit.</summary>
    public FixVec2 Heading(SystemShip ship)
    {
        if (ship.AtBody != null || Body(ship.FromBody) is not { } a || Body(ship.ToBody) is not { } b) return FixVec2.Zero;
        var d = b.Position - a.Position;
        return d.LengthSquared() > Fix64.Zero ? d.Normalized() : FixVec2.Zero;
    }

    /// <summary>Why orbit actions (landing, outpost drops, drop ships, transporters, row satellites) can't be used now: the
    /// player's flagship isn't in orbit of the colony's body. Null when it is.</summary>
    public string? OrbitProblem(int player)
    {
        if (SurfaceBody == null) return null;
        var ship = FlagshipOf(player);
        if (ship == null) return "no ship in orbit";
        return ship.AtBody == SurfaceBody ? null : $"the ship is not in orbit of {Body(SurfaceBody)?.Name ?? SurfaceBody}";
    }

    /// <summary>Why the ship can't run a long-range scan now, or null.</summary>
    public string? ScanProblem(int player, int shipId)
    {
        var ship = FindShip(shipId);
        if (ship == null) return "no such ship";
        if (ship.Owner != player || player < 0) return "not your ship";
        if (ship.AtBody == null) return "the ship is in flight (scan from orbit)";
        if (ship.ScanReadyTick > Now) return $"the long-range scanners are recharging ({SecondsUntil(ship.ScanReadyTick)} s)";
        return null;
    }

    /// <summary>The bodies a long-range scan from this ship would chart now (within range of where it is).</summary>
    public IReadOnlyList<string> ScanTargets(SystemShip ship)
    {
        var range = Data?.SystemRules?.ScanRange ?? Fix64.FromInt(320);
        var at = ShipPosition(ship, Now);
        return Bodies.Where(b => FixVec2.Distance(b.Position, at) <= range).Select(b => b.Id).ToList();
    }

    /// <summary>Why the player can't leave a satellite at this body now, or null.</summary>
    public string? SatelliteProblem(int player, string bodyId)
    {
        if (_sim == null || player < 0 || player >= _sim.Players.Count) return "no such player";
        var body = Body(bodyId);
        if (body == null || KnowledgeOf(player, bodyId) == BodyKnowledge.Unknown) return "no such body";
        var ship = FlagshipOf(player);
        if (ship == null) return "no ship in orbit";
        if (ship.AtBody != bodyId) return $"the ship is not in orbit of {NameFor(player, bodyId)}";
        if (_satellites.Any(s => s.Owner == player && s.BodyId == bodyId)) return $"a satellite is already at {body.Name}";
        var rules = Data?.SystemRules;
        var max = rules?.SatelliteMax ?? 4;
        if (_satellites.Count(s => s.Owner == player) >= max) return $"all {max} satellites are out";
        if (rules != null && !_sim.Players[player].Stock.CanAfford(rules.SatelliteCost)) return "not enough resources";
        return null;
    }

    // ---------------------------------------------------------------- set-up

    /// <summary>A new player: every body Detected, their flagship in orbit of the colony's body (which it charts).</summary>
    internal void AddPlayer(Simulation sim, int slot, string faction)
    {
        while (_knowledge.Count <= slot)
        {
            var row = new BodyKnowledge[Bodies.Count];
            System.Array.Fill(row, BodyKnowledge.Detected);
            _knowledge.Add(row);
        }
        var orbitShip = sim.Data.Orbit?.ShipFor(faction);
        var key = orbitShip != null && sim.Data.HasShipClass($"{faction}.{orbitShip.ClassName}") ? $"{faction}.{orbitShip.ClassName}" : orbitShip?.ClassName ?? "";
        var role = key.Length > 0 && sim.Data.HasShipClass(key) ? sim.Data.ShipClass(key).Role : GameData.CombatRole;
        var home = SurfaceBody ?? (Bodies.Count > 0 ? Bodies[0].Id : null);
        _ships.Add(new SystemShip
        {
            Id = _nextShipId++, Owner = slot, ClassId = key, Name = orbitShip?.Name ?? $"{faction} ship", Faction = faction, Role = role,
            AtBody = home,
        });
        if (home != null) Chart(slot, home);
    }

    /// <summary>Puts the neutral traffic in (maps/system.json traffic): civilian ships at random bodies, leaving at staggered times.</summary>
    internal void SpawnTraffic(GameData data)
    {
        var rules = data.SystemRules;
        var classes = data.TrafficClasses();
        if (rules == null || classes.Count == 0 || Bodies.Count < 2) return;
        var count = SystemGenerator.Int(Hash(TrafficSalt, 0), rules.TrafficMin, rules.TrafficMax);
        var names = SystemGenerator.Shuffled(rules.ShipNames, Seed ^ TrafficSalt);
        for (var k = 0; k < count && k < names.Count; k++)
        {
            var cls = classes[(int)(Hash(TrafficSalt, 1 + k * 4) % (ulong)classes.Count)];
            var number = SystemGenerator.Int(Hash(TrafficSalt, 2 + k * 4), rules.RegistryMin, rules.RegistryMax);
            var at = Bodies[(int)(Hash(TrafficSalt, 3 + k * 4) % (ulong)Bodies.Count)].Id;
            var ship = new SystemShip
            {
                Id = _nextShipId++, Owner = -1, ClassId = cls.Key, Name = names[k], Faction = cls.Faction, Role = cls.Role,
                Registry = rules.RegistryPrefix(cls.Faction) + number, AtBody = at,
            };
            ship.Cargo = PickCargo(rules, ship);
            ship.LeaveTick = SystemGenerator.Int(Hash(TrafficSalt, 4 + k * 4), 0, rules.DwellMaxSeconds) * (long)Simulation.TickRate;
            _ships.Add(ship);
        }
    }

    // ---------------------------------------------------------------- commands and ticks

    /// <summary>Runs a star-system command (refusals through sim.Refuse; a refused command changes nothing).</summary>
    internal void Apply(Simulation sim, Command command)
    {
        switch (command)
        {
            case SetCourseCommand course:
            {
                if (CourseProblem(course.Player, course.ShipId, course.BodyId) is { } problem)
                {
                    sim.Refuse(course, problem);
                    return;
                }
                var ship = FindShip(course.ShipId)!;
                Depart(ship, course.BodyId, sim.Tick, TravelTicks(ship.AtBody!, course.BodyId));
                return;
            }
            case LongRangeScanCommand scan:
            {
                if (ScanProblem(scan.Player, scan.ShipId) is { } problem)
                {
                    sim.Refuse(scan, problem);
                    return;
                }
                var ship = FindShip(scan.ShipId)!;
                foreach (var id in ScanTargets(ship)) Chart(scan.Player, id);
                ship.LastScanTick = sim.Tick;
                ship.ScanReadyTick = sim.Tick + (long)(sim.Data.SystemRules?.ScanCooldownSeconds ?? 90) * Simulation.TickRate;
                return;
            }
            case SystemSatelliteCommand satellite:
            {
                if (SatelliteProblem(satellite.Player, satellite.BodyId) is { } problem)
                {
                    sim.Refuse(satellite, problem);
                    return;
                }
                if (sim.Data.SystemRules is { } rules) sim.Players[satellite.Player].Stock.TrySpend(rules.SatelliteCost);
                _satellites.Add(new SystemSatellite(satellite.Player, satellite.BodyId, sim.Tick));
                return;
            }
        }
    }

    /// <summary>One tick: arrivals (a ship that reaches a body charts it for its owner) and neutral departures.</summary>
    internal void Step(Simulation sim)
    {
        var tick = sim.Tick;
        foreach (var ship in _ships)
        {
            if (ship.AtBody == null && tick >= ship.ArriveTick)
            {
                ship.AtBody = ship.ToBody;
                ship.FromBody = null;
                ship.ToBody = null;
                if (ship.Owner >= 0 && ship.AtBody != null) Chart(ship.Owner, ship.AtBody);
                if (ship.IsNeutral && sim.Data.SystemRules is { } r)
                    ship.LeaveTick = tick + (long)SystemGenerator.Int(Hash(TripSalt, ship.Id * 1024L + ship.Trips), r.DwellMinSeconds, r.DwellMaxSeconds) * Simulation.TickRate;
            }
            if (ship.IsNeutral && ship.AtBody != null && tick >= ship.LeaveTick && Bodies.Count > 1 && sim.Data.SystemRules is { } rules)
            {
                // Somewhere else, picked by the system's hash of the ship and its trip count.
                var pick = (int)(Hash(TripSalt ^ 0x77, ship.Id * 1024L + ship.Trips) % (ulong)(Bodies.Count - 1));
                var from = IndexOf(ship.AtBody);
                var to = Bodies[pick >= from ? pick + 1 : pick].Id;
                var ticks = (Fix64.FromInt((int)TravelTicks(ship.AtBody, to)) * rules.NeutralScale).FloorToInt();
                ship.Trips++;
                ship.Cargo = PickCargo(rules, ship);
                Depart(ship, to, tick, System.Math.Max(1, ticks));
            }
        }
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Seed);
        SystemShip.Text(ref h, SurfaceBody);
        h.Add(Bodies.Count);
        foreach (var b in Bodies) h.Add(b.Id);
        h.Add(_nextShipId);
        h.Add(_ships.Count);
        foreach (var s in _ships) s.AddToHash(ref h);
        h.Add(_knowledge.Count);
        foreach (var row in _knowledge)
            foreach (var k in row) h.Add((int)k);
        h.Add(_satellites.Count);
        foreach (var s in _satellites)
        {
            h.Add(s.Owner);
            h.Add(s.BodyId);
            h.Add(s.PlacedTick);
        }
    }

    // ---------------------------------------------------------------- helpers

    private const ulong TrafficSalt = 0x7AFF1C0001UL, TripSalt = 0x7AFF1C0002UL, CargoSalt = 0x7AFF1C0003UL;

    private ulong Hash(ulong salt, long index) => SystemGenerator.H(Seed, salt, index);

    private string? PickCargo(StarSystemRules rules, SystemShip ship)
    {
        var cargo = rules.CargoFor(ship.Role);
        return cargo.Count == 0 ? null : cargo[(int)(Hash(CargoSalt, ship.Id * 1024L + ship.Trips) % (ulong)cargo.Count)];
    }

    private static void Depart(SystemShip ship, string to, long tick, long ticks)
    {
        ship.FromBody = ship.AtBody;
        ship.ToBody = to;
        ship.AtBody = null;
        ship.DepartTick = tick;
        ship.ArriveTick = tick + ticks;
    }

    /// <summary>The player learns a body for good.</summary>
    private void Chart(int player, string bodyId)
    {
        var i = IndexOf(bodyId);
        if (i >= 0 && player >= 0 && player < _knowledge.Count) _knowledge[player][i] = BodyKnowledge.Charted;
    }

    private int IndexOf(string? bodyId)
    {
        if (bodyId == null) return -1;
        for (var i = 0; i < Bodies.Count; i++)
            if (Bodies[i].Id == bodyId) return i;
        return -1;
    }

    private Fix64 FlightShare(SystemShip ship, long tick)
    {
        var span = ship.ArriveTick - ship.DepartTick;
        if (span <= 0) return Fix64.One;
        var done = System.Math.Clamp(tick - ship.DepartTick, 0, span);
        return Fix64.FromRaw((long)(((System.Int128)done << Fix64.FractionalBits) / span));
    }

    private int SecondsUntil(long tick) => (int)System.Math.Max(0, (tick - Now + Simulation.TickRate - 1) / Simulation.TickRate);
}
