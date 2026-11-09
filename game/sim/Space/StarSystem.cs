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
    /// <summary>Identified by a long-range scan (or a ship arriving, before its sweep is done): name, class, size, moons.</summary>
    Scanned,
    /// <summary>Swept from orbit: class, moons, gravity, atmosphere, temperature, the resources that may be there; kept for good.</summary>
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
    public bool IsNeutral => Owner < 0 && AllyOf < 0 && TradeFor < 0;

    /// <summary>An allied native ship (<see cref="AllyOf"/> ≥ 0): Owner -1, follows its player's orders.</summary>
    public bool IsAlly => AllyOf >= 0;

    /// <summary>In flight (not in orbit of anything).</summary>
    public bool InFlight => AtBody == null;

    /// <summary>When its long-range scanners are ready again (sim tick).</summary>
    public long ScanReadyTick { get; internal set; }

    /// <summary>When it last ran a long-range scan (sim tick; -1 = never), for the view's sweep effect.</summary>
    public long LastScanTick { get; internal set; } = -1;

    /// <summary>Neutral traffic: trips flown so far (seeds its next destination) and when it leaves orbit next.</summary>
    public int Trips { get; internal set; }
    public long LeaveTick { get; internal set; }

    /// <summary>The body its orbital sensor sweep is charting (started on entering orbit of a body its owner hasn't
    /// Charted), or null; done at <see cref="SweepDoneTick"/>. Leaving orbit calls it off.</summary>
    public string? SweepBody { get; internal set; }

    /// <summary>An allied native ship (phase 2): the player it follows (-1 = not an ally), its home body and task.</summary>
    public int AllyOf { get; internal set; } = -1;
    public string? AllyHome { get; internal set; }
    public AllyTask AllyTask { get; internal set; }
    /// <summary>Scouting: the body it was sent to chart.</summary>
    public string? ScoutBody { get; internal set; }
    /// <summary>Escorting: when it sets off after the flagship (-1 = not waiting to follow).</summary>
    public long FollowTick { get; internal set; } = -1;
    public long SweepStartTick { get; internal set; }
    public long SweepDoneTick { get; internal set; }

    /// <summary>A native trade freighter (phase 2 follow-up): the player whose trade route it flies (-1 = not one), its
    /// home world, and when it next reaches the colony (a whole minute, when trade is paid). Owner -1, not an ally, not
    /// neutral traffic.</summary>
    public int TradeFor { get; internal set; } = -1;
    public string? TradeHome { get; internal set; }
    public long TradeArriveTick { get; internal set; }

    /// <summary>A native trade freighter (<see cref="TradeFor"/> ≥ 0).</summary>
    public bool IsTradeFreighter => TradeFor >= 0;

    /// <summary>A sensor sweep is running.</summary>
    public bool Sweeping => SweepBody != null;

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
        Text(ref h, SweepBody);
        h.Add(SweepStartTick);
        h.Add(SweepDoneTick);
        h.Add(AllyOf);
        if (AllyOf >= 0)
        {
            Text(ref h, AllyHome);
            h.Add((int)AllyTask);
            Text(ref h, ScoutBody);
            h.Add(FollowTick);
        }
        h.Add(TradeFor);
        if (TradeFor >= 0)
        {
            Text(ref h, TradeHome);
            h.Add(TradeArriveTick);
        }
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
/// <param name="Structures">Live only: what sensors see built on it, per owner (<see cref="StarSystemState.Structures"/>).</param>
/// <remarks>Scanned fills Name, ClassId, ClassName, Moons and Landable; Charted the rest.</remarks>
public sealed record BodyReport(string BodyId, BodyKnowledge Knowledge, bool Live, FixVec2 Position, int Size, string RoughSize, string Zone,
    string? ParentId, string? Name, string? ClassId, string? ClassName, IReadOnlyList<string> Moons, Fix64? GravityG, Fix64? PressureKpa,
    IReadOnlyList<GasAmount> Atmosphere, Fix64? MeanTemperatureC, bool Landable, IReadOnlyList<string> Resources,
    IReadOnlyList<StructureSighting> Structures);

/// <summary>What orbital sensors (a ship in orbit or a satellite) see built on a body for one owner: colony cores,
/// other buildings (finished or not), vehicles on the ground. Counts only, no places.</summary>
public sealed record StructureSighting(int Owner, int Bases, int Buildings, int Vehicles);

/// <summary>Live readings of a body (<see cref="StarSystemState.LiveReadings"/>): only while the player has a satellite
/// there, or for the colony's own body once its uplink is up.</summary>
/// <param name="FromUplink">The colony's own body with its uplink up (else a satellite).</param>
/// <param name="TemperatureC">Now: at the colony's landing site (uplink / satellite over the colony's body) or at the
/// equator under the reference meridian (other bodies).</param>
/// <param name="Weather">Now over the landing site on the colony's body; elsewhere the season's prevailing weather.</param>
/// <param name="Surface">The full outpost telemetry when the reading comes from the colony's surface, else null.</param>
public sealed record LiveReadings(string BodyId, long Tick, bool FromUplink, Fix64? GravityG, Fix64? PressureKpa,
    IReadOnlyList<GasAmount> Atmosphere, Fix64? TemperatureC, Fix64? MeanTemperatureC, Fix64? Insolation, Fix64? DayFraction, bool Night,
    string Season, WeatherKind Weather, bool HasWeather, SurfaceReadings? Surface);

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

    /// <summary>Goes up whenever knowledge, a ship's course or orbit (arrivals, departures, cargo), a satellite or a scan
    /// changes, so the view can skip rebuilding. A deterministic counter for the view only (not in the state hash: every
    /// change it counts is).</summary>
    public int Version { get; private set; }

    /// <summary>How long a long-range scan takes to recharge, in ticks (maps/system.json scan.cooldown_seconds).</summary>
    public long ScanCooldownTicks => (long)(Data?.SystemRules?.ScanCooldownSeconds ?? 90) * Simulation.TickRate;

    /// <summary>The long-range scan range in map units (maps/system.json scan.range).</summary>
    public Fix64 ScanRange => Data?.SystemRules?.ScanRange ?? Fix64.FromInt(320);

    /// <summary>The game this system belongs to (set by <see cref="Attach"/>; data, tick, surface).</summary>
    private Simulation? _sim;

    private GameData? Data => _sim?.Data;
    private long Now => _sim?.Tick ?? 0;

    /// <summary>Joins a game: from then on <c>sim.System</c> is this. Call before adding players.</summary>
    internal void Attach(Simulation sim)
    {
        _sim = sim;
        InitCivilizations(sim.Data); // Civilizations.cs: the sites are rolled once, with the system
    }

    // ---------------------------------------------------------------- the contract

    public BodyKnowledge KnowledgeOf(int player, string bodyId)
    {
        var i = IndexOf(bodyId);
        if (i < 0 || player < 0) return BodyKnowledge.Unknown;
        return player < _knowledge.Count ? _knowledge[player][i] : BodyKnowledge.Detected;
    }

    /// <summary>True while the player has a satellite at the body, a ship of theirs in orbit of it once it is Charted
    /// (its sensors read it like a satellite), or its colony there with the uplink up.</summary>
    public bool HasLiveData(int player, string bodyId)
    {
        foreach (var s in _satellites)
            if (s.Owner == player && s.BodyId == bodyId) return true;
        if (LiveFromShip(player, bodyId)) return true;
        return bodyId == SurfaceBody && _sim?.Surface is { } surface && surface.UplinkOnline(player, Now);
    }

    /// <summary>A ship of the player's is in orbit of the body and it is Charted: live readings from the ship's sensors.</summary>
    public bool LiveFromShip(int player, string bodyId)
    {
        if (player < 0 || KnowledgeOf(player, bodyId) < BodyKnowledge.Charted) return false;
        foreach (var s in _ships)
            if (s.Owner == player && s.AtBody == bodyId) return true;
        return false;
    }

    /// <summary>The player's ship sweeping this body now, or null.</summary>
    public SystemShip? SweepingShip(int player, string bodyId)
    {
        foreach (var s in _ships)
            if (s.Owner == player && s.SweepBody == bodyId && s.AtBody == bodyId) return s;
        return null;
    }

    /// <summary>How far a ship's sensor sweep is (0..1; 0 when it isn't sweeping).</summary>
    public Fix64 SweepShare(SystemShip ship)
    {
        if (ship.SweepBody == null) return Fix64.Zero;
        var span = ship.SweepDoneTick - ship.SweepStartTick;
        if (span <= 0) return Fix64.One;
        var done = System.Math.Clamp(Now - ship.SweepStartTick, 0, span);
        return Fix64.FromRaw((long)(((System.Int128)done << Fix64.FractionalBits) / span));
    }

    /// <summary>Seconds (rounded up) until a ship's sensor sweep is done; 0 when it isn't sweeping.</summary>
    public int SweepSecondsLeft(SystemShip ship) => ship.SweepBody == null ? 0 : SecondsUntil(ship.SweepDoneTick);

    /// <summary>How long an orbital sensor sweep takes, in ticks (maps/system.json sweep.seconds).</summary>
    public long SweepTicks => (long)(Data?.SystemRules?.SweepSeconds ?? 15) * Simulation.TickRate;

    /// <summary>What orbital sensors see built on a body, per owner (own included), while the player has live data there;
    /// empty otherwise. Only the colony's body has a surface today, so other bodies show nothing built.</summary>
    public IReadOnlyList<StructureSighting> Structures(int player, string bodyId)
    {
        var list = new List<StructureSighting>();
        if (bodyId != SurfaceBody || _sim?.Surface is not { } surface || !HasLiveData(player, bodyId)) return list;
        var owners = surface.Structures.Select(x => x.Owner).Concat(surface.Units.Select(u => u.Owner)).Where(o => o >= 0).Distinct().OrderBy(o => o);
        foreach (var o in owners)
        {
            var bases = surface.Structures.Count(x => x.Owner == o && Data!.Building(x.DefId).IsCore);
            var buildings = surface.Structures.Count(x => x.Owner == o && !Data!.Building(x.DefId).IsCore);
            var vehicles = surface.Units.Count(u => u.Owner == o);
            list.Add(new StructureSighting(o, bases, buildings, vehicles));
        }
        return list;
    }

    /// <summary>The signal delay between the player (aboard their flagship) and their colony, in ticks: 0 in orbit of the
    /// colony's body (or with no colony body or no ship), else maps/system.json comms by the ship's distance from it.
    /// Orders to the colony (<see cref="Simulation.IsColonyOrder"/>) wait this long before they arrive.</summary>
    public long CommsDelayTicks(int player)
    {
        if (SurfaceBody == null || Body(SurfaceBody) is not { } colony) return 0;
        var ship = FlagshipOf(player);
        if (ship == null || ship.AtBody == SurfaceBody) return 0;
        var r = Data?.SystemRules;
        var min = r?.CommsMinSeconds ?? 1;
        var max = r?.CommsMaxSeconds ?? 10;
        var near = r?.CommsNear ?? Fix64.Zero;
        var far = r?.CommsFar ?? Fix64.FromInt(1000);
        var share = Fix64.Clamp((FixVec2.Distance(ShipPosition(ship, Now), colony.Position) - near) / (far - near), Fix64.Zero, Fix64.One);
        var seconds = Fix64.FromInt(min) + Fix64.FromInt(max - min) * share;
        return System.Math.Max(min > 0 ? 1L : 0L, (seconds * Simulation.TickRate).FloorToInt());
    }

    /// <summary><see cref="CommsDelayTicks"/> in seconds (rounded up).</summary>
    public int CommsDelaySeconds(int player) => (int)((CommsDelayTicks(player) + Simulation.TickRate - 1) / Simulation.TickRate);

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
        if (StarfleetHoldProblem(player) is { } held) return held;
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

    /// <summary>A body's name as the player knows it: its name once Scanned, else "unknown body".</summary>
    public string NameFor(int player, string bodyId) =>
        KnowledgeOf(player, bodyId) >= BodyKnowledge.Scanned ? Body(bodyId)?.Name ?? bodyId : UnknownBodyName;

    public const string UnknownBodyName = "unknown body";

    /// <summary>The orbit zone of a body ("inner", "habitable", "outer"; "" for a system from a maps.json map).</summary>
    public string ZoneOf(string bodyId) => Generated?.Entry(bodyId)?.Zone ?? "";

    /// <summary>"small" (size 1), "medium" (2-3), "large" (4-5) or "giant" (6+): what a Detected body shows.</summary>
    public static string RoughSize(int size) => size <= 1 ? "small" : size <= 3 ? "medium" : size <= 5 ? "large" : "giant";

    /// <summary>A body's surface gravity in g: its own climate entry's gravity_g if it has one, else its class's
    /// environment's; null for a body with no surface (giants, belts). Not fogged: <see cref="Report"/> shows it once Charted.
    /// (Generated bodies carry their class's gravity today.)</summary>
    public Fix64? GravityOf(string bodyId)
    {
        var body = Body(bodyId);
        var data = Data;
        if (body == null || data == null) return null;
        return Generated?.Climate.Body(bodyId)?.GravityG ?? data.Climate?.Body(bodyId)?.GravityG ?? data.EnvironmentOf(body.ClassId)?.GravityG;
    }

    /// <summary>Live readings of a body now, or null without live data there (<see cref="HasLiveData"/>). On the colony's
    /// body they are the outpost telemetry at the landing site (or the map's middle before landing); elsewhere what a
    /// satellite reads from orbit: the class environment's air, the climate's sunlight, season and temperature now, and
    /// the season's prevailing weather.</summary>
    public LiveReadings? LiveReadings(int player, string bodyId)
    {
        var body = Body(bodyId);
        var data = Data;
        if (body == null || data == null || !HasLiveData(player, bodyId)) return null;
        var env = data.EnvironmentOf(body.ClassId);
        var gravity = GravityOf(bodyId);
        var uplink = bodyId == SurfaceBody && _sim?.Surface is { } s && s.UplinkOnline(player, Now);
        var climate = Climate.Now(data, Generated?.Climate, bodyId, Now);
        if (bodyId == SurfaceBody && _sim?.Surface is { } surface)
        {
            var map = surface.Map;
            var tile = player < surface.Players.Count && surface.Players[player].LandingTile >= 0 ? surface.Players[player].LandingTile : map.Grid.Count / 2;
            if (Telemetry.Read(data, map, tile, Now) is { } r)
                return new LiveReadings(bodyId, Now, uplink, r.GravityG, r.PressureKpa, env?.Atmosphere ?? new List<GasAmount>(), r.TemperatureC,
                    climate?.MeanTemperatureC, climate?.Insolation, climate?.DayFraction, r.Night, r.Season, r.Weather, env?.HasAtmosphere ?? false, r);
        }
        return new LiveReadings(bodyId, Now, false, gravity, env?.PressureKpa, env?.Atmosphere ?? new List<GasAmount>(), climate?.TemperatureC,
            climate?.MeanTemperatureC, climate?.Insolation, climate?.DayFraction, climate?.Night ?? false, climate?.Season ?? "",
            climate?.Weather ?? WeatherKind.Clear, climate?.HasWeather ?? false, null);
    }

    /// <summary>Everything the player knows about a body, for its card.</summary>
    public BodyReport? Report(int player, string bodyId)
    {
        var body = Body(bodyId);
        var data = Data;
        if (body == null) return null;
        var knowledge = KnowledgeOf(player, bodyId);
        var live = HasLiveData(player, bodyId);
        var structures = Structures(player, bodyId);
        if (knowledge < BodyKnowledge.Scanned || data == null)
            return new BodyReport(bodyId, knowledge, live, body.Position, body.Size, RoughSize(body.Size), ZoneOf(bodyId), body.Parent,
                null, null, null, new List<string>(), null, null, new List<GasAmount>(), null, false, new List<string>(), structures);
        var env = data.EnvironmentOf(body.ClassId);
        var cls = data.PlanetClasses.FirstOrDefault(c => c.Id == body.ClassId);
        var moons = MoonsOf(bodyId).Select(m => m.Id).ToList();
        if (knowledge < BodyKnowledge.Charted)
            return new BodyReport(bodyId, knowledge, live, body.Position, body.Size, RoughSize(body.Size), ZoneOf(bodyId), body.Parent,
                body.Name, body.ClassId, cls?.Name ?? body.ClassId, moons, null, null, new List<GasAmount>(), null,
                data.IsLandableClass(body.ClassId), new List<string>(), structures);
        var climate = Generated != null ? Climate.Summary(data, Generated.Climate, bodyId) : Climate.Summary(data, bodyId);
        var gravity = GravityOf(bodyId);
        return new BodyReport(bodyId, knowledge, live, body.Position, body.Size, RoughSize(body.Size), ZoneOf(bodyId), body.Parent,
            body.Name, body.ClassId, cls?.Name ?? body.ClassId, moons, gravity, env?.PressureKpa,
            env?.Atmosphere ?? new List<GasAmount>(), climate?.MeanTemperatureC, data.IsLandableClass(body.ClassId), PossibleResources(player, bodyId),
            structures);
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
        if (StarfleetHoldProblem(player) is { } held) return held;
        if (ship.AtBody == null) return "the ship is in flight (scan from orbit)";
        if (ship.ScanReadyTick > Now) return $"the long-range scanners are recharging ({SecondsUntil(ship.ScanReadyTick)} s)";
        return null;
    }

    /// <summary>The bodies a long-range scan from this ship would chart now (within range of where it is).</summary>
    public IReadOnlyList<string> ScanTargets(SystemShip ship)
    {
        var range = ScanRange;
        var at = ShipPosition(ship, Now);
        return Bodies.Where(b => FixVec2.Distance(b.Position, at) <= range).Select(b => b.Id).ToList();
    }

    /// <summary>Why the player can't leave a satellite at this body now, or null.</summary>
    public string? SatelliteProblem(int player, string bodyId)
    {
        if (_sim == null || player < 0 || player >= _sim.Players.Count) return "no such player";
        var body = Body(bodyId);
        if (body == null || KnowledgeOf(player, bodyId) == BodyKnowledge.Unknown) return "no such body";
        if (Recalled(player)) return "recalled to Starfleet";
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
        if (home != null) Chart(slot, home, report: false); // the home body is known from the start: no achievement event
        AddCivPlayer(slot, faction); // Civilizations.cs
        Version++;
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
        Version++;
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
                foreach (var id in ScanTargets(ship)) Identify(scan.Player, id);
                ship.LastScanTick = sim.Tick;
                Version++;
                ship.ScanReadyTick = sim.Tick + ScanCooldownTicks;
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
                Version++;
                return;
            }
            case ObservationPostCommand post:
                ApplyObservationPost(sim, post); // Civilizations.cs
                return;
            case FirstContactCommand or ContactChoiceCommand or MembershipCommand or AllyShipOrderCommand:
                ApplyContact(sim, command); // Contact.cs
                return;
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
                Version++;
                if (ship.Owner >= 0 && ship.AtBody != null) BeginSweep(ship, tick);
                if (ship.Owner >= 0 && ship.AtBody != null && FlagshipOf(ship.Owner) == ship) sim.Achievements.FlagshipArrived(ship.Owner, ship.AtBody);
                if (ship.IsNeutral && sim.Data.SystemRules is { } r)
                    ship.LeaveTick = tick + (long)SystemGenerator.Int(Hash(TripSalt, ship.Id * 1024L + ship.Trips), r.DwellMinSeconds, r.DwellMaxSeconds) * Simulation.TickRate;
            }
            if (ship.SweepBody != null && ship.AtBody == ship.SweepBody && tick >= ship.SweepDoneTick && !ship.IsAlly) // allies: Contact.cs
            {
                Chart(ship.Owner, ship.SweepBody);
                ship.SweepBody = null;
                Version++;
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
        StepCivilizations(sim); // Civilizations.cs
        StepContact(sim); // Contact.cs
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
        AddCivsToHash(ref h); // Civilizations.cs
    }

    // ---------------------------------------------------------------- helpers

    private const ulong TrafficSalt = 0x7AFF1C0001UL, TripSalt = 0x7AFF1C0002UL, CargoSalt = 0x7AFF1C0003UL;

    private ulong Hash(ulong salt, long index) => SystemGenerator.H(Seed, salt, index);

    private string? PickCargo(StarSystemRules rules, SystemShip ship)
    {
        var cargo = rules.CargoFor(ship.Role);
        return cargo.Count == 0 ? null : cargo[(int)(Hash(CargoSalt, ship.Id * 1024L + ship.Trips) % (ulong)cargo.Count)];
    }

    private void Depart(SystemShip ship, string to, long tick, long ticks)
    {
        Version++;
        ship.SweepBody = null; // leaving orbit calls the sweep off
        ship.FromBody = ship.AtBody;
        ship.ToBody = to;
        ship.AtBody = null;
        ship.DepartTick = tick;
        ship.ArriveTick = tick + ticks;
    }

    /// <summary>A ship entering orbit: the body is identified at once, and a sensor sweep starts if it isn't Charted yet.</summary>
    private void BeginSweep(SystemShip ship, long tick)
    {
        var body = ship.AtBody!;
        Identify(ship.Owner, body);
        if (KnowledgeOf(ship.Owner, body) >= BodyKnowledge.Charted) return;
        ship.SweepBody = body;
        ship.SweepStartTick = tick;
        ship.SweepDoneTick = tick + SweepTicks;
        Version++;
    }

    /// <summary>The player identifies a body (Detected → Scanned; never lowers what they know).</summary>
    private void Identify(int player, string bodyId)
    {
        var i = IndexOf(bodyId);
        if (i < 0 || player < 0 || player >= _knowledge.Count || _knowledge[player][i] >= BodyKnowledge.Scanned) return;
        _knowledge[player][i] = BodyKnowledge.Scanned;
        Version++;
    }

    /// <summary>The player learns a body for good.</summary>
    private void Chart(int player, string bodyId, bool report = true)
    {
        var i = IndexOf(bodyId);
        if (i < 0 || player < 0 || player >= _knowledge.Count || _knowledge[player][i] == BodyKnowledge.Charted) return;
        _knowledge[player][i] = BodyKnowledge.Charted;
        Version++;
        if (report) _sim?.Achievements.Note(player, "charted_class", Bodies[i].ClassId);
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
