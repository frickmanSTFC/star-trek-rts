using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The star system on the helm (owner 2026-11-08, docs/system-contract.md; view side): the SYSTEM display's ships, fog,
// body and ship cards, the left rail and SYSTEM DATA panel; and orbit-any-body for the PLANET / ORBIT / ATMOS displays
// (the body the flagship is at, OrbitView.Bodies.cs). Everything reads SystemSource (Simulation.System, else the duel
// map with every body charted and no ships, as before). Orders go through SystemOrders (stubs until the sim lands).
public partial class PlanetDriver
{
    private SystemSource _system = null!;

    /// <summary>The open card on the system plot: "body:<id>" or "ship:<id>"; null = none.</summary>
    private string? _systemCard;
    private int? _selectedShip;
    private string? _orbitBodySeen;

    private static readonly Color DetectedGrey = new(0.62f, 0.64f, 0.68f);

    /// <summary>
    /// Sets up the game (from _Ready): a star-system game (MatchSetup.CreateSystemGame) whose home is the chosen body when
    /// it is one of the seed's landable bodies (MatchSetup.LandableBodies), else the generator's pick. While the sim has
    /// no system generator yet (LandableBodies empty), a duel-map world picked from the chooser's fallback list (Veyra,
    /// Morrow, Oscen) still starts the planet landing on it, as before. Sets PlanetName; returns the chooser's world id.
    /// </summary>
    private string StartGame(GameData data, string world)
    {
        static bool Is(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
        var landable = MatchSetup.LandableBodies(data, Seed);
        var home = world.Length == 0 ? null : landable.FirstOrDefault(b => Is(b.Id, world) || Is(b.Name, world));
        if (landable.Count == 0 && world.Length > 0 && !Is(world, MatchSetup.DefaultWorld) && MatchSetup.IsWorld(data, world))
        {
            var (cls, id) = MatchSetup.ResolveWorld(data, world);
            PlanetName = MapBodyName(data, id) ?? PlanetName;
            GD.Print($"World: {PlanetName} ({id}, {cls}), seed {Seed}, duel-map world (no generated system yet)");
            _sim = MatchSetup.CreatePlanetLanding(data, Seed, "federation", null, world);
            return id;
        }
        if (world.Length > 0 && home == null && !Is(world, MatchSetup.DefaultWorld))
        {
            if (landable.Count > 0) GD.PushWarning($"World '{world}' isn't a landable body of seed {Seed}'s system; the system's own pick is home.");
            else GD.PushError($"World '{world}' isn't a playable world (try \"tarsis\" or \"veyra\"); starting on {MatchSetup.DefaultWorld}.");
        }
        _sim = MatchSetup.CreateSystemGame(data, Seed, "federation", home?.Id);
        var sys = _sim.System;
        var body = sys?.Bodies.FirstOrDefault(b => b.Id == sys.SurfaceBody) ?? home;
        var classId = _sim.Surface?.Map.Def.ClassId ?? "?";
        if (body == null)
        {
            // The contract stub: a plain planet landing, no system: it is the default world of the duel map.
            var (_, id) = MatchSetup.ResolveWorld(data, MatchSetup.DefaultWorld);
            PlanetName = MapBodyName(data, id) ?? PlanetName;
            GD.Print($"World: {PlanetName} ({id}, {classId}), seed {Seed}, no star system (sim stub)");
            return id;
        }
        PlanetName = body.Name;
        GD.Print($"World: {PlanetName} ({body.Id}, {classId}), seed {Seed}, star system {sys?.Star.Name ?? "none"}");
        return body.Id;
    }

    private static string? MapBodyName(GameData data, string bodyId)
    {
        foreach (var m in data.Maps)
            foreach (var b in m.Bodies)
                if (b.Id == bodyId) return b.Name;
        return null;
    }

    /// <summary>In orbit of the colony's own body: the orbit actions, scans, findings and telemetry are for it.</summary>
    private bool AtColony => _system.AtColony;

    /// <summary>Every frame in orbit, before the orbit view's refresh: the globe displays follow the flagship.</summary>
    private void UpdateOrbitBody()
    {
        _orbit.SetOrbitBody(_system.OrbitBodyId, Seed);
        var now = _system.FlagshipInFlight ? $"flight:{_system.Flagship?.ToBody}" : _system.OrbitBodyId;
        if (now == _orbitBodySeen) return;
        var first = _orbitBodySeen == null;
        _orbitBodySeen = now;
        if (first) return;
        if (!AtColony)
        {
            // The deployments are the colony's: disarm and close their card away from it.
            _orbitAction = null;
            if (_info != null && _info.StartsWith("orbit:")) _info = null;
        }
        var f = _system.Flagship;
        if (f == null) return;
        if (SystemSource.InFlight(f) && _system.Body(f.ToBody) is { } to)
            _hud.PushEvent($"{f.Name} under way to {_system.KnownName(to)}: arrives in {_system.SecondsToArrival(f)} s", HelmScreen.Peach);
        else if (_system.Body(f.AtBody) is { } at)
            _hud.PushEvent($"{f.Name} in orbit of {_system.KnownName(at)}", HelmScreen.Peach);
    }

    // ---------------------------------------------------------------- names and lines

    /// <summary>Ship name and class for the helm's subtitles: the flagship's, else the orbit data's ship (as before).</summary>
    private string ShipLine()
    {
        var f = _system.Flagship;
        if (f != null)
        {
            var c = _system.ShipClass(f.ClassId);
            return $"{f.Name.ToUpperInvariant()}{(c != null ? $" · {c.Name.ToUpperInvariant()} CLASS" : "")}";
        }
        var ship = _data.Orbit?.ShipFor(_sim.Players[LocalPlayer].FactionId);
        return $"{ship?.Name?.ToUpperInvariant() ?? "YOUR SHIP"}{(ship?.ClassName is { Length: > 0 } cn ? $" · {cn.ToUpperInvariant()} CLASS" : "")}";
    }

    /// <summary>The body the globe displays show, or null (no system data at all).</summary>
    private BodyDef? OrbitBody => _system.Body(_system.OrbitBodyId);

    /// <summary>The name for the helm's title: the colony's world name at the colony, else the body as known.</summary>
    private string OrbitBodyTitle()
    {
        if (_orbit.AtColony) return PlanetName.ToUpperInvariant();
        var b = OrbitBody;
        return b == null ? "UNKNOWN BODY" : _system.KnownName(b).ToUpperInvariant();
    }

    /// <summary>"STANDARD ORBIT", or "UNDER WAY TO X · 34 S" while the flagship flies.</summary>
    private string OrbitState()
    {
        var f = _system.Flagship;
        var delay = _sim.System?.CommsDelaySeconds(LocalPlayer) ?? 0;
        var signal = delay > 0 ? $" · SIGNAL DELAY {delay} S" : "";
        if (f == null || !SystemSource.InFlight(f))
            return (f?.SweepBody != null && _system.State is { } sys ? $"SENSOR SWEEP · {sys.SweepSecondsLeft(f)} S" : "STANDARD ORBIT") + signal;
        var to = _system.Body(f.ToBody);
        return $"UNDER WAY TO {(to == null ? "?" : _system.KnownName(to).ToUpperInvariant())} · {_system.SecondsToArrival(f)} S{signal}";
    }

    private string ResourceName(string id) => _data.Resources.FirstOrDefault(r => r.Id == id)?.Name ?? id;

    /// <summary>"0.84 AU" for a map distance (climate.json au_per_map_unit), else map units.</summary>
    private string DistanceText(float mapUnits)
    {
        var au = _data.Climate?.Surface.AuPerMapUnit.ToFloat() ?? 0f;
        return au > 0f ? $"{mapUnits * au:0.00} AU" : $"{mapUnits:0} map units";
    }

    private static string TierText(HabitabilityTier tier) => tier switch
    {
        HabitabilityTier.Open => "open air",
        HabitabilityTier.Masks => "masks",
        HabitabilityTier.Suits => "suits",
        HabitabilityTier.Domes => "domes needed",
        HabitabilityTier.Tech => "needs tech",
        _ => "uninhabitable",
    };

    /// <summary>The habitability tier of a class with surface air data, else what its colony type allows.</summary>
    private string HabitabilityText(string classId)
    {
        if (_data.EnvironmentOf(classId) != null) return TierText(WorldHabitability.Assess(_data, classId).Tier);
        var colony = _system.PlanetClass(classId)?.ColonyType;
        return string.IsNullOrEmpty(colony) ? "unknown" : colony.Replace('_', ' ');
    }

    /// <summary>Main gases and pressure ("N2 78 % · O2 21 % · 101 kPa"), "none (vacuum)", or "unknown".</summary>
    private string AtmosphereText(string classId)
    {
        var env = _data.EnvironmentOf(classId);
        if (env == null) return classId is "class_j" or "class_t" ? "hydrogen-helium envelope" : "unknown";
        if (!env.HasAtmosphere) return "none (vacuum)";
        var gases = env.Atmosphere.OrderByDescending(g => g.Amount.ToFloat()).Take(2).Select(g => $"{g.Gas.ToUpperInvariant()} {g.Amount.ToFloat():0.#} %");
        return $"{string.Join(" · ", gases)} · {env.PressureKpa.ToFloat():0.#} kPa";
    }

    /// <summary>Resources that may be on a body as the player knows it (the sim's PossibleResources; on the stand-in
    /// system, the class's yields).</summary>
    private List<string> PossibleResources(BodyDef b)
    {
        IEnumerable<string> ids = _system.State != null
            ? _system.State.PossibleResources(LocalPlayer, b.Id)
            : _system.PlanetClass(b.ClassId)?.Yields ?? (IEnumerable<string>)System.Array.Empty<string>();
        return ids.Select(ResourceName).ToList();
    }

    private int KnownMoons(BodyDef b) => _system.Bodies.Count(m => m.Parent == b.Id && _system.Known(m.Id));

    // ---------------------------------------------------------------- body rows (card, panel, planet display)

    /// <summary>What the player knows of a body, by knowledge level: Detected = rough size, distance and flight time
    /// only; Scanned = class, size, moons (and the sweep's progress); Charted = class, gravity, habitability, atmosphere,
    /// moons, possible resources; Live adds current readings and what is built there.</summary>
    private List<HelmRow> BodyRows(BodyDef b, bool withDistance)
    {
        var rows = new List<HelmRow>();
        var knowledge = _system.Knowledge(b.Id);
        if (knowledge < BodyKnowledge.Scanned)
        {
            rows.Add(new HelmRow("", $"Unknown body · size ~{b.Size}", DetectedGrey));
            rows.Add(new HelmRow("DATA", "position and rough size only: a long-range scan identifies it, a visit charts it", DetectedGrey));
        }
        else if (knowledge == BodyKnowledge.Scanned)
        {
            rows.Add(new HelmRow("CLASS", _system.ClassName(b.ClassId)));
            rows.Add(new HelmRow("SIZE", $"{StarSystemState.RoughSize(b.Size)} (~{b.Size})"));
            var moons = _system.Bodies.Where(m => m.Parent == b.Id && _system.Known(m.Id)).Select(_system.KnownName).ToList();
            rows.Add(new HelmRow("MOONS", moons.Count == 0 ? "none" : string.Join(", ", moons)));
            rows.Add(SweepRow(b));
        }
        else
        {
            rows.Add(new HelmRow("CLASS", _system.ClassName(b.ClassId)));
            var env = _data.EnvironmentOf(b.ClassId);
            if (env != null) rows.Add(new HelmRow("GRAVITY", $"{env.GravityG.ToFloat():0.##} g"));
            rows.Add(new HelmRow("HABITABLE", HabitabilityText(b.ClassId)));
            rows.Add(new HelmRow("AIR", AtmosphereText(b.ClassId)));
            var moons = _system.Bodies.Where(m => m.Parent == b.Id && _system.Known(m.Id)).Select(_system.KnownName).ToList();
            rows.Add(new HelmRow("MOONS", moons.Count == 0 ? "none" : string.Join(", ", moons)));
            var res = PossibleResources(b);
            rows.Add(new HelmRow("MAY HOLD", res.Count == 0 ? "nothing known" : string.Join(", ", res)));
            rows.AddRange(StructureRows(b));
        }
        if (withDistance)
        {
            if (_system.DistanceTo(b) is { } d && b.Id != _system.Flagship?.AtBody) rows.Add(new HelmRow("DISTANCE", DistanceText(d)));
            if (_system.FlightSeconds(b.Id) is { } s) rows.Add(new HelmRow("ETA", $"{s} s at impulse"));
        }
        rows.AddRange(LiveRows(b));
        return rows;
    }

    /// <summary>The sensor sweep's state for a body that is only Scanned: running (seconds left, share) or not started.</summary>
    private HelmRow SweepRow(BodyDef b)
    {
        if (_system.State is { } sys && sys.SweepingShip(LocalPlayer, b.Id) is { } ship)
            return new HelmRow("SWEEP", $"sensor sweep {Mathf.RoundToInt(sys.SweepShare(ship).ToFloat() * 100)} % · {sys.SweepSecondsLeft(ship)} s left", HelmScreen.Cyan);
        return new HelmRow("DATA", "identified only: enter orbit for a sensor sweep (air, surface, structures, resources)", DetectedGrey);
    }

    /// <summary>What orbital sensors see built on a Charted body: per owner while there is live data (the ship in orbit
    /// or a satellite), else that it needs live data.</summary>
    private List<HelmRow> StructureRows(BodyDef b)
    {
        var rows = new List<HelmRow>();
        if (_system.State is not { } sys) return rows;
        if (!_system.HasLive(b.Id))
        {
            rows.Add(new HelmRow("STRUCTURES", "unknown: needs the ship in orbit or a satellite", DetectedGrey));
            return rows;
        }
        var seen = sys.Structures(LocalPlayer, b.Id);
        if (seen.Count == 0)
        {
            rows.Add(new HelmRow("STRUCTURES", "none detected"));
            return rows;
        }
        foreach (var s in seen)
        {
            var who = s.Owner == LocalPlayer ? "ours" : _sim.Data.Factions.FirstOrDefault(f => f.Id == _sim.Players[s.Owner].FactionId)?.Name ?? $"player {s.Owner + 1}";
            string N(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";
            rows.Add(new HelmRow(s.Owner == LocalPlayer ? "STRUCTURES" : "CONTACT", $"{who}: {N(s.Bases, "base")} · {N(s.Buildings, "building")} · {N(s.Vehicles, "vehicle")}",
                s.Owner == LocalPlayer ? null : PlanetDriver.ColorOf(s.Owner)));
        }
        return rows;
    }

    /// <summary>Where a body's live data comes from: the outpost's uplink, a satellite, or the ship's sensors in orbit.</summary>
    private string LiveSource(BodyDef b)
    {
        if (_system.State?.Satellites.Any(s => s.Owner == LocalPlayer && s.BodyId == b.Id) == true) return "live data from satellite";
        if (_system.LiveFromShip(b.Id)) return "live: ship's sensors in orbit";
        return "live data";
    }

    /// <summary>Live readings: the colony's body from the outpost's telemetry; another body from its satellite (look-only
    /// map readings where there is one, else a placeholder). Nothing without live data.</summary>
    private List<HelmRow> LiveRows(BodyDef b)
    {
        var rows = new List<HelmRow>();
        var colony = b.Id == _system.ColonyBodyId;
        if (!_system.HasLive(b.Id) && !(colony && _readings != null && _system.State == null)) return rows;
        if (colony)
        {
            if (_readings is { } r)
            {
                rows.Add(new HelmRow("LIVE", "outpost uplink", HelmScreen.Cyan));
                rows.Add(new HelmRow("TEMP", $"{r.TemperatureC.ToFloat():0.#} °C"));
                rows.Add(new HelmRow("WEATHER", $"{r.Weather} · wind {r.WindMs.ToFloat():0} m/s"));
                rows.Add(new HelmRow("PRESSURE", $"{r.PressureKpa.ToFloat():0.#} kPa"));
            }
            else rows.Add(new HelmRow("LIVE", $"{LiveSource(b)} · surface readings once the outpost uplink is up", HelmScreen.Cyan));
            return rows;
        }
        rows.Add(new HelmRow("LIVE", LiveSource(b), HelmScreen.Cyan));
        // the sim's satellite readings (StarSystemState.LiveReadings): temperature now, day/night, season; its weather is
        // the season's likeliest kind, not a live report, so it is labelled as such
        if (_system.State?.LiveReadings(LocalPlayer, b.Id) is { } live)
        {
            if (live.TemperatureC is { } t) rows.Add(new HelmRow("TEMP", $"{t.ToFloat():0.#} °C now{(live.Night ? " · night" : " · day")}"));
            else if (live.MeanTemperatureC is { } mean) rows.Add(new HelmRow("TEMP", $"{mean.ToFloat():0.#} °C mean"));
            if (live.PressureKpa is { } kpa) rows.Add(new HelmRow("PRESSURE", kpa > Fix64.Zero ? $"{kpa.ToFloat():0.#} kPa" : "airless"));
            if (live.GravityG is { } g) rows.Add(new HelmRow("GRAVITY", $"{g.ToFloat():0.##} g"));
            if (live.Season.Length > 0) rows.Add(new HelmRow("SEASON", live.Season));
            if (live.HasWeather) rows.Add(new HelmRow("TYPICAL", $"{live.Weather} this season"));
            return rows;
        }
        var env = _data.EnvironmentOf(b.ClassId);
        var map = _orbit.LookMapOf(b.Id);
        if (env != null && map != null && _data.Telemetry != null)
        {
            var tile = map.Grid.Index(map.Grid.Width / 2, map.Grid.Height / 2);
            var read = Telemetry.Read(env, _data.Telemetry, map, tile, _sim.Tick);
            rows.Add(new HelmRow("TEMP", $"{read.TemperatureC.ToFloat():0.#} °C (equator)"));
            rows.Add(new HelmRow("WEATHER", $"{read.Sky} · wind {read.WindMs.ToFloat():0} m/s"));
        }
        else rows.Add(new HelmRow("READINGS", "weather and temperature: awaiting satellite telemetry", DetectedGrey));
        return rows;
    }

    // ---------------------------------------------------------------- SYSTEM display

    /// <summary>The left rail on the SYSTEM display: the system's top-level bodies, fogged.</summary>
    private List<HelmAction> SystemRail()
    {
        var bodies = _orbit.SystemBodies();
        var current = _selectedBody ?? bodies.FirstOrDefault(x => x.Current)?.Id;
        return bodies.Where(b => b.ParentId == null)
            .Select(b => new HelmAction($"body:{b.Id}", "", b.Name.ToUpperInvariant(), Highlight: b.Id == current, Colour: b.Identified ? null : DetectedGrey))
            .ToList();
    }

    /// <summary>Title, SYSTEM DATA panel, cursor readout and the open card for the SYSTEM display.</summary>
    private void SystemDisplayHud(string? hint, Vector2 mouse)
    {
        var bodies = _orbit.SystemBodies();
        var star = _system.Star;
        var known = bodies.Count(b => b.ParentId == null);
        _helm.SetTitle(star != null ? $"{star.Name} SYSTEM" : "STAR SYSTEM", hint ?? $"{known} BODIES · {ShipLine()}{(OrbitState() is var state && state != "STANDARD ORBIT" ? $" · {state}" : "")}");

        var sections = new List<HelmSection>();
        var selected = _system.Body(_selectedBody ?? bodies.FirstOrDefault(b => b.Current)?.Id);
        if (selected != null && _system.Known(selected.Id))
            sections.Add(new HelmSection(_system.KnownName(selected).ToUpperInvariant(), _system.Identified(selected.Id) ? HelmScreen.Peach : DetectedGrey, BodyRows(selected, true)));
        sections.Add(new HelmSection("BODIES", HelmScreen.Sky, bodies.Where(b => b.ParentId == null).Select(b => new HelmRow(
            b.Name.ToUpperInvariant(),
            b.Charted ? b.ClassName + (b.Live ? " · live" : "") : b.Identified ? $"{b.ClassName} · uncharted" : $"detected · size ~{b.Size}",
            b.Current ? HelmScreen.Peach : b.Identified ? null : DetectedGrey)).ToList()));
        var ships = _system.Ships;
        if (ships.Count > 0)
            sections.Add(new HelmSection("SHIPS", HelmScreen.Lavender, ships.Select(s => new HelmRow(
                s.Name.ToUpperInvariant(), $"{(s.Owner == LocalPlayer ? "ours" : s.Owner < 0 ? "traffic" : _system.OwnerText(s))} · {HeadingText(s)}",
                s.Owner == LocalPlayer ? HelmScreen.Peach : null)).ToList()));
        _helm.SetPanel("SYSTEM DATA", sections);

        // Cursor readout: the ship or body under the mouse, as known.
        var shipId = _mouseOverUi ? null : _orbit.PickShip(mouse);
        if (shipId is { } sid && _system.Ship(sid) is { } hovered)
            _helm.ShowReadout(hovered.Name.ToUpperInvariant(), $"{_system.ShipClassText(hovered)} · {HeadingText(hovered)}", hovered.Owner == LocalPlayer ? HelmScreen.Peach : HelmScreen.Lavender, mouse + new Vector2(18, 14));
        else
        {
            var id = _mouseOverUi ? null : _orbit.PickBody(mouse);
            var b = id == null ? null : bodies.FirstOrDefault(x => x.Id == id);
            _helm.ShowReadout(b?.Name.ToUpperInvariant(), b == null ? "" : b.Identified ? b.ClassName : $"size ~{b.Size} · click for details", b?.Identified == false ? DetectedGrey : HelmScreen.Peach, mouse + new Vector2(18, 14));
        }
        ShowSystemCard();
    }

    /// <summary>"in orbit of X", "X → Y · 34 s", "under way".</summary>
    private string HeadingText(SystemShip s)
    {
        string Name(string? id) => _system.Body(id) is { } b ? _system.KnownName(b) : "?";
        if (!SystemSource.InFlight(s)) return s.AtBody != null ? $"in orbit of {Name(s.AtBody)}" : "holding position";
        return $"{Name(s.FromBody)} → {Name(s.ToBody)} · {_system.SecondsToArrival(s)} s";
    }

    /// <summary>The body or ship card beside its object on the plot (HelmScreen.ShowCard), or hidden.</summary>
    private void ShowSystemCard()
    {
        if (_orbit.Display != OrbitDisplay.System || _systemCard == null)
        {
            _helm.ShowCard(null, System.Array.Empty<HelmRow>(), System.Array.Empty<HelmAction>(), Vector2.Zero, HelmScreen.Peach);
            return;
        }
        if (_systemCard.StartsWith("ship:") && int.TryParse(_systemCard[5..], out var shipId))
        {
            var ship = _system.Ship(shipId);
            var at = _orbit.ScreenPositionOf($"ship:{shipId}");
            if (ship == null || at == null)
            {
                if (ship == null) CloseSystemCard();
                else _helm.ShowCard(null, System.Array.Empty<HelmRow>(), System.Array.Empty<HelmAction>(), Vector2.Zero, HelmScreen.Peach);
                return;
            }
            var (rows, buttons, colour) = ShipCard(ship);
            _helm.ShowCard(ship.Name, rows, buttons, at.Value, colour);
            return;
        }
        var body = _system.Body(_systemCard.StartsWith("body:") ? _systemCard[5..] : null);
        var pos = body == null ? null : _orbit.ScreenPositionOf(body.Id);
        if (body == null || !_system.Known(body.Id))
        {
            CloseSystemCard();
            return;
        }
        if (pos == null)
        {
            _helm.ShowCard(null, System.Array.Empty<HelmRow>(), System.Array.Empty<HelmAction>(), Vector2.Zero, HelmScreen.Peach);
            return;
        }
        var bodyRows = BodyRows(body, true);
        if (_system.State != null && CourseProblem(body.Id) is { } course) bodyRows.Add(new HelmRow("COURSE", course, HelmScreen.Red)); // why SET COURSE is greyed
        _helm.ShowCard(_system.KnownName(body), bodyRows, BodyButtons(body), pos.Value, _system.Identified(body.Id) ? HelmScreen.Peach : DetectedGrey);
    }

    private (List<HelmRow> Rows, List<HelmAction> Buttons, Color Colour) ShipCard(SystemShip s)
    {
        var rows = new List<HelmRow>();
        if (s.Registry.Length > 0) rows.Add(new HelmRow("REGISTRY", s.Registry));
        rows.Add(new HelmRow("CLASS", _system.ShipClassText(s)));
        if (_system.ShipClass(s.ClassId) is { } c) rows.Add(new HelmRow("HULL", $"{c.HullId.Replace('_', ' ')} · {_data.Factions.FirstOrDefault(f => f.Id == c.Faction)?.Name ?? c.Faction}"));
        rows.Add(new HelmRow("OWNER", _system.OwnerText(s), s.Owner == LocalPlayer ? HelmScreen.Peach : s.Owner < 0 ? null : PlanetDriver.ColorOf(s.Owner)));
        rows.Add(new HelmRow("HEADING", HeadingText(s)));
        rows.Add(new HelmRow("CARGO", string.IsNullOrEmpty(s.Cargo) ? "none declared" : s.Cargo));
        if (SystemSource.InFlight(s)) rows.Add(new HelmRow("ETA", $"{_system.SecondsToArrival(s)} s"));
        var buttons = new List<HelmAction>();
        if (s.Owner == LocalPlayer) buttons.Add(new HelmAction("sys:scan", "", "LONG-RANGE SCAN", ScanProblem() == null));
        var colour = s.Owner == LocalPlayer ? HelmScreen.Peach : s.Owner < 0 ? HelmScreen.Lavender : PlanetDriver.ColorOf(s.Owner);
        return (rows, buttons, colour);
    }

    private List<HelmAction> BodyButtons(BodyDef b)
    {
        var buttons = new List<HelmAction>();
        if (_system.State == null) return buttons; // the stand-in system (no star-system game): nothing to order
        var course = CourseProblem(b.Id);
        buttons.Add(new HelmAction($"sys:course:{b.Id}", "", "SET COURSE", course == null));
        var f = _system.Flagship;
        if (f != null && f.AtBody == b.Id && !SystemSource.InFlight(f))
            buttons.Add(new HelmAction($"sys:sat:{b.Id}", "", "DEPLOY SATELLITE", SatelliteProblem(b.Id) == null));
        buttons.Add(new HelmAction("sys:scan", "", "LONG-RANGE SCAN", ScanProblem() == null, Colour: HelmScreen.Lavender));
        return buttons;
    }

    /// <summary>Why the flagship can't set course for a body (the sim's CourseProblem), or null.</summary>
    private string? CourseProblem(string bodyId)
    {
        if (_system.State == null) return "no star-system game";
        var f = _system.Flagship;
        if (f == null) return "no ship under your command";
        if (f.AtBody == bodyId && !SystemSource.InFlight(f)) return "already in orbit here";
        return _system.State.CourseProblem(LocalPlayer, f.Id, bodyId);
    }

    /// <summary>Why a satellite can't go to this body now, or null (view-side check: the sim re-checks).</summary>
    private string? SatelliteProblem(string bodyId)
    {
        if (_system.State == null) return "no star-system game";
        var f = _system.Flagship;
        if (f == null || f.AtBody != bodyId || SystemSource.InFlight(f)) return "the flagship must be in orbit here";
        if (_system.HasLive(bodyId)) return "a satellite is already there";
        return null;
    }

    /// <summary>Why a long-range scan can't run now, or null (view-side check: the sim has the cooldown).</summary>
    private string? ScanProblem()
    {
        if (_system.State == null) return "no star-system game";
        return _system.Flagship == null ? "no ship under your command" : null;
    }

    private void CloseSystemCard()
    {
        _systemCard = null;
        _selectedShip = null;
        _orbit.HighlightShip(null);
        if (_selectedBody != null) _orbit.HighlightBody(_selectedBody);
    }

    /// <summary>A click on the system plot: a ship (they sit over bodies) or a body opens its card; empty space closes it.</summary>
    private void SystemClick(Vector2 mouse)
    {
        if (_orbit.PickShip(mouse) is { } shipId)
        {
            _selectedShip = shipId;
            _systemCard = $"ship:{shipId}";
            _orbit.HighlightShip(shipId);
            NoteShipInspected(shipId); // view-only achievement rule (PlanetDriver.Achievements.cs)
            return;
        }
        var body = _orbit.PickBody(mouse);
        if (body != null)
        {
            SelectSystemBody(body);
            return;
        }
        _systemCard = null;
        _selectedShip = null;
        _orbit.HighlightShip(null);
        _orbit.HighlightBody(_selectedBody);
    }

    private void SelectSystemBody(string bodyId)
    {
        _selectedBody = bodyId;
        _selectedShip = null;
        _systemCard = $"body:{bodyId}";
        _orbit.HighlightBody(bodyId);
    }

    /// <summary>The card buttons ("sys:course:<body>", "sys:sat:<body>", "sys:scan"); greyed buttons still land here,
    /// so each re-checks and says why it can't.</summary>
    private void OnSystemAction(string id)
    {
        var when = _sim.Tick + InputDelayTicks;
        var f = _system.Flagship;
        string? problem;
        string done;
        if (id.StartsWith("sys:course:"))
        {
            var bodyId = id[11..];
            problem = CourseProblem(bodyId) ?? SystemOrders.SetCourse(_sim, LocalPlayer, f!.Id, bodyId, when);
            done = $"Course laid in for {(_system.Body(bodyId) is { } b ? _system.KnownName(b) : bodyId)}";
            if (problem == null) _systemCard = null;
        }
        else if (id.StartsWith("sys:sat:"))
        {
            var bodyId = id[8..];
            problem = SatelliteProblem(bodyId) ?? SystemOrders.DeploySatellite(_sim, LocalPlayer, bodyId, when);
            done = "Satellite launched: live data once it settles into orbit";
        }
        else if (id == "sys:scan")
        {
            problem = ScanProblem() ?? SystemOrders.LongRangeScan(_sim, LocalPlayer, f!.Id, when);
            done = "Long-range scan: bodies in range identified (name, class, moons); orbit one for a sensor sweep";
        }
        else return;
        if (problem != null) _hud.PushEvent($"Can't do that: {problem}", new Color(1f, 0.7f, 0.42f));
        else _hud.PushEvent(done, HelmScreen.Cyan);
    }

    // ---------------------------------------------------------------- PLANET / ATMOS displays at another body

    /// <summary>The Planet display's panel at a body other than the colony's: rows by what the player knows.</summary>
    private List<HelmSection> OtherBodyPlanetSections()
    {
        var sections = new List<HelmSection>();
        var b = OrbitBody;
        if (b == null)
        {
            sections.Add(new HelmSection("", HelmScreen.Cyan, new List<HelmRow> { new("", "no data") }));
            return sections;
        }
        var charted = _system.Charted(b.Id);
        sections.Add(new HelmSection("", _system.Identified(b.Id) ? HelmScreen.Cyan : DetectedGrey, _system.Identified(b.Id)
            ? BodyRows(b, false)
            : new List<HelmRow> { new("SURFACE", "no data", DetectedGrey), new("SIZE", $"~{b.Size}", DetectedGrey), new("", "Unidentified: a long-range scan identifies it; orbiting it charts it.", DetectedGrey) }));
        var landable = b.ClassId is "class_m" or "class_l";
        sections.Add(new HelmSection("ORBIT", HelmScreen.Sky, new List<HelmRow>
        {
            new("", charted && landable
                ? $"A landable world. Orbital scans and landings work at {PlanetName}, the colony's world; here the globe is what the sensors see."
                : charted ? "No landing here: a body to orbit, chart and survey from space." : "Sensors see only the shape of it so far."),
        }));
        return sections;
    }

    /// <summary>The Atmosphere display's panel at another body: the class's air from the data once charted, live
    /// readings with a satellite there.</summary>
    private List<HelmSection> OtherBodyAtmosphereSections()
    {
        var sections = new List<HelmSection>();
        var b = OrbitBody;
        if (b == null || !_system.Charted(b.Id))
        {
            var rows0 = new List<HelmRow> { new("AIR", "no data", DetectedGrey) };
            rows0.Add(b != null && _system.Identified(b.Id) ? SweepRow(b) : new HelmRow("", "Uncharted: enter orbit for a sensor sweep.", DetectedGrey));
            sections.Add(new HelmSection("CONDITIONS", DetectedGrey, rows0));
            return sections;
        }
        var rows = new List<HelmRow> { new("BODY", b.Name), new("CLASS", _system.ClassName(b.ClassId)), new("AIR", AtmosphereText(b.ClassId)) };
        var env = _data.EnvironmentOf(b.ClassId);
        if (env != null)
        {
            rows.Add(new HelmRow("TEMPERATURE", $"{env.NightC.ToFloat():0} to {env.DayC.ToFloat():0} °C (typical)"));
            if (env.HasAtmosphere) rows.Add(new HelmRow("WIND", $"{env.WindMean.ToFloat():0} m/s mean, gusts {env.WindGust.ToFloat():0}"));
            rows.Add(new HelmRow("RADIATION", $"{env.RadiationDay.ToFloat():0.#} µSv/h by day"));
        }
        sections.Add(new HelmSection("CONDITIONS", HelmScreen.Sky, rows));
        var live = LiveRows(b);
        sections.Add(new HelmSection("LIVE", HelmScreen.Cyan, live.Count > 0 ? live : new List<HelmRow> { new("", "No live data here: stay in orbit, or deploy a satellite for live weather and temperature for good.", DetectedGrey) }));
        return sections;
    }
}
