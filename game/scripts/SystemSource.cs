using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The star system as the view sees it (render-only reads, no state of its own): the sim's <see cref="StarSystemState"/>
/// (<c>Simulation.System</c>, docs/system-contract.md) when the game has one, else today's stand-in — the loaded map or
/// maps.json mvp_duel, every body charted, no ships, the colony's body = the world being played. SystemPlot, OrbitView
/// and the driver all read the system through this one place, so the fog, the flagship's body and the fallback agree.
/// </summary>
public sealed class SystemSource
{
    private readonly Simulation _sim;
    private readonly int _player;
    private readonly string? _fallbackHome;
    private MapDef? _fallbackMap;
    private bool _fallbackMapSet;

    private static readonly IReadOnlyList<BodyDef> NoBodies = Array.Empty<BodyDef>();
    private static readonly IReadOnlyList<SystemShip> NoShips = Array.Empty<SystemShip>();

    /// <param name="fallbackHome">Name or id of the colony's body on the stand-in map (PlanetDriver.PlanetName).</param>
    public SystemSource(Simulation sim, int player, string? fallbackHome)
    {
        _sim = sim;
        _player = player;
        _fallbackHome = fallbackHome;
    }

    public Simulation Sim => _sim;
    public GameData Data => _sim.Data;
    public int Player => _player;

    /// <summary>The sim's star system, or null (planet-only game: the duel map stands in).</summary>
    public StarSystemState? State => _sim.System;

    /// <summary>True when the bodies, fog and ships come from the sim (Simulation.System is set).</summary>
    public bool Live => _sim.System != null;

    /// <summary>The stand-in system: the loaded map, else maps.json mvp_duel (else the first map).</summary>
    public MapDef? FallbackMap
    {
        get
        {
            if (_fallbackMapSet) return _fallbackMap;
            _fallbackMapSet = true;
            _fallbackMap = _sim.Map;
            if (_fallbackMap == null)
                foreach (var m in _sim.Data.Maps)
                    if (m.Id == MatchSetup.MvpMap) _fallbackMap = m;
            if (_fallbackMap == null && _sim.Data.Maps.Count > 0) _fallbackMap = _sim.Data.Maps[0];
            return _fallbackMap;
        }
    }

    public StarDef? Star => State?.Star ?? FallbackMap?.Star;

    public IReadOnlyList<BodyDef> Bodies => State?.Bodies ?? FallbackMap?.Bodies ?? NoBodies;

    /// <summary>A body by id or name (any case), or null.</summary>
    public BodyDef? Body(string? nameOrId)
    {
        if (string.IsNullOrEmpty(nameOrId)) return null;
        foreach (var b in Bodies)
            if (string.Equals(b.Id, nameOrId, StringComparison.OrdinalIgnoreCase)) return b;
        foreach (var b in Bodies)
            if (string.Equals(b.Name, nameOrId, StringComparison.OrdinalIgnoreCase)) return b;
        return null;
    }

    /// <summary>The body the colony's surface is on (where the game's PlanetSurface is).</summary>
    public string? ColonyBodyId
    {
        get
        {
            if (State != null) return State.SurfaceBody;
            var home = Body(_fallbackHome);
            if (home != null) return home.Id;
            var map = FallbackMap;
            if (map != null)
                foreach (var s in map.Starts)
                    if (s.Slot == _player) return s.HomeBodyId;
            return null;
        }
    }

    /// <summary>What the local player knows of a body (the stand-in: every body charted, as before the fog).</summary>
    public BodyKnowledge Knowledge(string bodyId) => State?.KnowledgeOf(_player, bodyId) ?? BodyKnowledge.Charted;

    public bool Known(string bodyId) => Knowledge(bodyId) != BodyKnowledge.Unknown;

    public bool Charted(string bodyId) => Knowledge(bodyId) == BodyKnowledge.Charted;

    /// <summary>A satellite at the body (or the colony's uplink there): live local readings.</summary>
    public bool HasLive(string bodyId) => State?.HasLiveData(_player, bodyId) ?? false;

    /// <summary>The local player's flagship, or null (always null on the stand-in).</summary>
    public SystemShip? Flagship => State?.FlagshipOf(_player);

    public IReadOnlyList<SystemShip> Ships => State?.Ships ?? NoShips;

    public SystemShip? Ship(int id)
    {
        foreach (var s in Ships)
            if (s.Id == id) return s;
        return null;
    }

    public static bool InFlight(SystemShip ship) => ship.AtBody == null && ship.ToBody != null;

    /// <summary>The body the helm's PLANET / ORBIT / ATMOS displays show: where the flagship is in orbit; while it flies,
    /// the body it left; with no flagship, the colony's body.</summary>
    public string? OrbitBodyId
    {
        get
        {
            var f = Flagship;
            if (f != null) return f.AtBody ?? f.FromBody ?? ColonyBodyId;
            return ColonyBodyId;
        }
    }

    /// <summary>The flagship is between bodies.</summary>
    public bool FlagshipInFlight => Flagship is { } f && InFlight(f);

    /// <summary>In orbit of the colony's body (not flying away from it): the orbit actions (land, outpost, drop ship,
    /// transporter, survey satellite) and the scans are only here.</summary>
    public bool AtColony => !FlagshipInFlight && OrbitBodyId != null && OrbitBodyId == ColonyBodyId;

    /// <summary>Where a ship is on the plot (map units), smoothed between sim ticks by alpha.</summary>
    public Vector2 ShipPosition(SystemShip ship, float alpha)
    {
        if (State == null) return Vector2.Zero;
        var a = State.ShipPosition(ship, _sim.Tick);
        if (!InFlight(ship)) return new Vector2(a.X.ToFloat(), a.Y.ToFloat());
        var b = State.ShipPosition(ship, _sim.Tick + 1);
        return new Vector2(a.X.ToFloat(), a.Y.ToFloat()).Lerp(new Vector2(b.X.ToFloat(), b.Y.ToFloat()), Mathf.Clamp(alpha, 0f, 1f));
    }

    /// <summary>Whole seconds (rounded up) until a ship in flight arrives; 0 when it isn't flying.</summary>
    public long SecondsToArrival(SystemShip ship) =>
        InFlight(ship) ? Math.Max(0, (ship.ArriveTick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate) : 0;

    /// <summary>Flight time from the flagship's body to another, in whole seconds, or null (no flagship, in flight, same body).</summary>
    public long? FlightSeconds(string toBody)
    {
        var f = Flagship;
        if (State == null || f?.AtBody == null || f.AtBody == toBody) return null;
        var ticks = State.TravelTicks(f.AtBody, toBody);
        return ticks <= 0 ? null : (ticks + Simulation.TickRate - 1) / Simulation.TickRate;
    }

    /// <summary>Map position of a body as a Vector2 (map units).</summary>
    public static Vector2 PositionOf(BodyDef b) => new(b.Position.X.ToFloat(), b.Position.Y.ToFloat());

    /// <summary>Map distance from the flagship (or the colony's body when there's none) to a body, or null.</summary>
    public float? DistanceTo(BodyDef body, float alpha = 0f)
    {
        Vector2 from;
        var f = Flagship;
        if (f != null && State != null) from = InFlight(f) ? ShipPosition(f, alpha) : Body(f.AtBody) is { } at ? PositionOf(at) : ShipPosition(f, alpha);
        else if (Body(ColonyBodyId) is { } home) from = PositionOf(home);
        else return null;
        return from.DistanceTo(PositionOf(body));
    }

    public PlanetClassDef? PlanetClass(string classId)
    {
        foreach (var pc in _sim.Data.PlanetClasses)
            if (pc.Id == classId) return pc;
        return null;
    }

    /// <summary>A planet class's full name ("Class J — Gas Giant"), or its id.</summary>
    public string ClassName(string classId) => PlanetClass(classId)?.Name ?? classId;

    /// <summary>The class name before its " — " description ("CLASS J"), upper case.</summary>
    public string ClassLabel(string classId)
    {
        var name = ClassName(classId);
        var dash = name.IndexOf(" —", StringComparison.Ordinal);
        return (dash > 0 ? name[..dash] : name.Replace('_', ' ')).ToUpperInvariant();
    }

    /// <summary>A ships.json class by its key ("federation.Galaxy") or bare name, or null.</summary>
    public ShipClassDef? ShipClass(string classId)
    {
        foreach (var c in _sim.Data.ShipClasses)
            if (string.Equals(c.Key, classId, StringComparison.OrdinalIgnoreCase)) return c;
        foreach (var c in _sim.Data.ShipClasses)
            if (string.Equals(c.Name, classId, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    /// <summary>"Galaxy class" style text for a ship's class (the id itself when ships.json doesn't have it).</summary>
    public string ShipClassText(SystemShip ship)
    {
        var c = ShipClass(ship.ClassId);
        if (c == null) return ship.ClassId;
        return c.Role is GameData.CombatRole or "" ? $"{c.Name} class" : $"{c.Name} class {c.Role}";
    }

    /// <summary>Who flies a ship: "Neutral traffic", "Ours (Federation)", or the owner's faction.</summary>
    public string OwnerText(SystemShip ship)
    {
        if (ship.Owner < 0) return "Neutral traffic (civilian)";
        var faction = ship.Owner < _sim.Players.Count ? _sim.Players[ship.Owner].FactionId : null;
        var name = faction;
        if (faction != null)
            foreach (var f in _sim.Data.Factions)
                if (f.Id == faction) name = f.Name;
        name ??= $"Player {ship.Owner + 1}";
        return ship.Owner == _player ? $"Ours ({name})" : name;
    }

    /// <summary>A body's name as the player knows it: charted = its name, detected = "Unknown body".</summary>
    public string KnownName(BodyDef b) => Charted(b.Id) ? b.Name : "Unknown body";

    /// <summary>Changes whenever something the plot draws (other than ship positions) changes: the bodies, what we know
    /// of each, live data, the orbit body, which ships exist and their courses. Cheap enough to work out every frame.</summary>
    public long PlotStamp()
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) => h = (h ^ v) * 1099511628211L;
            var bodies = Bodies;
            Mix(bodies.Count);
            foreach (var b in bodies)
            {
                Mix(b.Id.GetHashCode());
                Mix((long)Knowledge(b.Id) * 3 + (HasLive(b.Id) ? 1 : 0));
            }
            Mix((OrbitBodyId ?? "").GetHashCode());
            Mix(FlagshipInFlight ? 1 : 0);
            return h;
        }
    }

    /// <summary>Changes when a ship appears, goes, docks or sets out (the course lines are rebuilt on it).</summary>
    public long CourseStamp()
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) => h = (h ^ v) * 1099511628211L;
            foreach (var s in Ships)
            {
                Mix(s.Id);
                Mix((s.AtBody ?? "").GetHashCode());
                Mix((s.FromBody ?? "").GetHashCode());
                Mix((s.ToBody ?? "").GetHashCode());
                Mix(s.DepartTick);
                Mix(s.ArriveTick);
            }
            return h;
        }
    }
}
