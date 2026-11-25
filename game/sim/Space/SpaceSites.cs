using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Space jobs, phase 1 (docs/space-jobs-contract.md; data maps/sites.json "space_sites"): sites between the bodies of a
// star system (a station, derelicts, debris fields), what each player knows of them, flights to them, scanning and the
// work done there. The flagship's hull, systems, crew and hold are ShipStatus.cs; boardings are AwayTeam.cs. The sites are
// rolled once with the system from its seed (a hash, never sim.Rng); outcomes (debris strikes, boardings) roll on sim.Rng.
// Everything here is hashed (AddSpaceJobsToHash), in system games only.

/// <summary>What a space site is. Phase 1: Station, Derelict, KnownDerelict, Debris. Phase 2: Asteroid. Phase 3:
/// RogueAsteroid, Comet, Lifepods, StrandedShip. Chain-only (phase 4): HiddenShip, AlienProbe.</summary>
public enum SiteKind { Station, Derelict, KnownDerelict, Debris, Asteroid, RogueAsteroid, Anomaly, Comet, Lifepods, StrandedShip, HiddenShip, AlienProbe }

/// <summary>What a player knows of a site: Detected = a grey contact with a rough size; Identified = scanned (kind,
/// hazards, contents); Spent = worked out (greyed, drifts off after a while).</summary>
public enum SiteKnowledge { Unknown, Detected, Identified, Spent }

/// <summary>A place in the star system between the bodies (map units, like BodyDef.Position).</summary>
public sealed class SpaceSite
{
    /// <summary>"s1", "s2"… (never reused in a match).</summary>
    public required string Id { get; init; }
    public required SiteKind Kind { get; init; }
    /// <summary>Kind-specific flavour: derelict origin "unknown|earth|trader|native"; asteroid "common|metal|ice|hollow";
    /// anomaly "ion_storm|…"; "" when the kind has none. Models and names key off it.</summary>
    public string Variant { get; init; } = "";
    /// <summary>Display name once identified ("Derelict C-3", "Kessa Station"); before that the view shows "Unknown contact".</summary>
    public string Name { get; init; } = "";
    /// <summary>Plot position now (moving sites update it every tick: comets, rogue asteroids, drifting wrecks).</summary>
    public FixVec2 Position { get; internal set; }
    /// <summary>Rough size 1 (small) .. 5 (huge), shown while only Detected.</summary>
    public int Size { get; init; } = 1;
    /// <summary>Sim tick it appeared; and when it leaves the system (-1 = stays).</summary>
    public long SpawnTick { get; init; }
    public long ExpireTick { get; internal set; } = -1;
    /// <summary>Its own seed (model variety, contents); a hash of the system seed and its id.</summary>
    public ulong Seed { get; init; }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add((int)Kind);
        h.Add(Variant);
        h.Add(Name);
        h.Add(Position.X);
        h.Add(Position.Y);
        h.Add(Size);
        h.Add(SpawnTick);
        h.Add(ExpireTick);
        h.Add(Seed);
    }
}

/// <summary>One line of a site card: Label, Value, Tone "" | "good" | "warn" | "bad".</summary>
public sealed record SiteRow(string Label, string Value, string Tone = "");

/// <summary>What the player can see of a site (null when Unknown). Actions = work actions open now (ids for
/// SiteWorkCommand), each with Problem null when it can run.</summary>
public sealed record SiteReport(string SiteId, SiteKind Kind, SiteKnowledge Knowledge, string Title, string Subtitle,
    FixVec2 Position, IReadOnlyList<SiteRow> Rows, IReadOnlyList<SiteAction> Actions, IReadOnlyList<string> Hazards);

/// <summary>A work action on a site card: Id (SiteWorkCommand.Action, or "scan", "course", "away_team"), Label, and
/// Problem (null = can run now).</summary>
public sealed record SiteAction(string Id, string Label, string? Problem);

public sealed partial class StarSystemState
{
    /// <summary>Every site in the system (including Spent ones still drifting); not fogged: use SiteKnowledgeOf.</summary>
    public IReadOnlyList<SpaceSite> Sites => _sites;
    private readonly List<SpaceSite> _sites = new();

    private int _nextSiteId = 1;

    private SpaceSiteRules? SiteRules => Data?.SpaceSites;

    private const ulong SiteSalt = 0x5_17E5_0001UL, SiteContentSalt = 0x5_17E5_0002UL;

    /// <summary>Work actions a site card can offer (SiteWorkCommand.Action).</summary>
    public static readonly IReadOnlyList<string> WorkActions = new[] { "alongside", "restore_power", "restore_power_kit", "recover_logs", "sweep", "dock", "stop" };

    public SpaceSite? Site(string? id)
    {
        if (id == null) return null;
        foreach (var s in _sites)
            if (s.Id == id) return s;
        return null;
    }

    public SiteKnowledge SiteKnowledgeOf(int player, string siteId) =>
        Record(player, siteId) is { } r ? r.Knowledge : SiteKnowledge.Unknown;

    /// <summary>The site a ship holds at (or flies to: <see cref="SiteBound"/>), or null.</summary>
    public string? ShipSite(SystemShip ship) => ship.AtSite;
    public string? SiteBound(SystemShip ship) => ship.ToSite;

    /// <summary>A site's name as the player knows it: its name once Identified, else "unknown contact".</summary>
    public string SiteNameFor(int player, string siteId) =>
        Site(siteId) is { } s && SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Identified ? s.Name : UnknownContactName;

    public const string UnknownContactName = "unknown contact";

    /// <summary>The kind's data (maps/sites.json), or null.</summary>
    public SiteKindDef? SiteKindOf(SpaceSite site) => SiteRules?.Kind(site.Kind);

    /// <summary>Sites the player knows (Detected or more), in id order.</summary>
    public IReadOnlyList<SpaceSite> KnownSites(int player) => _sites.Where(s => SiteKnowledgeOf(player, s.Id) >= SiteKnowledge.Detected).ToList();

    /// <summary>The flagship's flight time to a site in seconds (rounded up; 0 there), or -1 without a flagship or site.</summary>
    public int SiteFlightSeconds(int player, string siteId)
    {
        if (FlagshipOf(player) is not { } ship || Site(siteId) is not { } site) return -1;
        if (ship.AtSite == siteId) return 0;
        var ticks = TravelTicksFor(ship, ShipPosition(ship, Now), site.Position);
        return (int)((ticks + Simulation.TickRate - 1) / Simulation.TickRate);
    }

    // ---------------------------------------------------------------- generation

    /// <summary>The sites of a new system: the station, then generation.count others, placed by the system's hash.</summary>
    private void InitSpaceSites(GameData data)
    {
        if (_sites.Count > 0 || data.SpaceSites is not { } r || Bodies.Count == 0) return;
        var stationKind = r.Kind(SiteKind.Station);
        if (stationKind != null && Place(r, r.Station.OrbitMin, r.Station.OrbitMax, 0) is { } stationAt)
        {
            var name = r.Station.Names.Count > 0 ? r.Station.Names[(int)(Hash(SiteSalt, 1) % (ulong)r.Station.Names.Count)] : "Station";
            AddSite(SiteKind.Station, "trade", name, stationAt, SystemGenerator.Int(Hash(SiteSalt, 2), stationKind.SizeMin, stationKind.SizeMax), 0);
        }
        var count = SystemGenerator.Int(Hash(SiteSalt, 3), r.CountMin, r.CountMax);
        for (var k = 0; k < count; k++)
        {
            if (r.Weights.Count == 0) break;
            var kindId = SystemGenerator.Pick(r.Weights, Hash(SiteSalt, 100 + k));
            if (SpaceSiteRules.KindOf(kindId) is not { } kind || r.Kind(kind) is not { } def) continue;
            if (Place(r, r.OrbitMin, r.OrbitMax, 1 + k) is not { } at) continue;
            var variant = def.Variants.Count > 0 ? SystemGenerator.Pick(def.Variants, Hash(SiteSalt, 200 + k)) : "";
            AddSite(kind, variant, null, at, SystemGenerator.Int(Hash(SiteSalt, 300 + k), def.SizeMin, def.SizeMax), 0);
        }
    }

    /// <summary>A free spot between the orbits: tries angles and distances (by the hash) until one is clear of every body
    /// and site; null when none is.</summary>
    private FixVec2? Place(SpaceSiteRules r, Fix64 min, Fix64 max, int slot)
    {
        for (var t = 0; t < r.Tries; t++)
        {
            var turns = SystemGenerator.Unit(Hash(SiteSalt, 10_000 + slot * 1000L + t * 2));
            var radius = min + (max - min) * SystemGenerator.Unit(Hash(SiteSalt, 10_001 + slot * 1000L + t * 2));
            var at = new FixVec2(radius * Surface.Telemetry.CosTurns(turns), radius * Surface.Climate.SinTurns(turns));
            if (Clear(r, at)) return at;
        }
        return null;
    }

    private bool Clear(SpaceSiteRules r, FixVec2 at)
    {
        foreach (var b in Bodies)
            if (FixVec2.Distance(b.Position, at) < r.ClearOfBody) return false;
        foreach (var s in _sites)
            if (FixVec2.Distance(s.Position, at) < r.Spacing) return false;
        return true;
    }

    private SpaceSite AddSite(SiteKind kind, string variant, string? name, FixVec2 at, int size, long tick)
    {
        var number = _nextSiteId++;
        var id = $"s{number}";
        var def = SiteRules?.Kind(kind);
        var site = new SpaceSite
        {
            Id = id, Kind = kind, Variant = variant, Position = at, Size = System.Math.Max(1, size), SpawnTick = tick,
            Name = name ?? $"{def?.Name ?? SpaceSiteRules.IdOf(kind)} {def?.Letter ?? "X"}-{number}",
            Seed = SystemGenerator.H(Seed, SiteContentSalt, number),
        };
        _sites.Add(site);
        if (kind == SiteKind.Station) // the station is on every chart from the start
            for (var p = 0; p < _status.Count; p++)
                SetKnowledge(p, id, SiteKnowledge.Identified);
        Version++;
        return site;
    }

    /// <summary>A mission (place_space_site) or the test panel puts a new site near a spot: the hash of its number picks
    /// the angle and distance (min..max map units), clear of bodies and other sites where it can.</summary>
    internal SpaceSite? SpawnSite(SiteKind kind, string? variant, FixVec2 near, Fix64 min, Fix64 max)
    {
        if (SiteRules is not { } r || r.Kind(kind) is not { } def) return null;
        var number = _nextSiteId;
        FixVec2 at = near;
        for (var t = 0; t < System.Math.Max(1, r.Tries); t++)
        {
            var turns = SystemGenerator.Unit(Hash(SiteSalt, 50_000 + number * 1000L + t * 2));
            var radius = min + (max - min) * SystemGenerator.Unit(Hash(SiteSalt, 50_001 + number * 1000L + t * 2));
            at = near + new FixVec2(radius * Surface.Telemetry.CosTurns(turns), radius * Surface.Climate.SinTurns(turns));
            if (Clear(r, at)) break;
        }
        var v = variant != null && def.Variants.Any(x => x.Id == variant) ? variant
            : def.Variants.Count > 0 ? SystemGenerator.Pick(def.Variants, Hash(SiteSalt, 60_000 + number)) : "";
        return AddSite(kind, v, null, at, SystemGenerator.Int(Hash(SiteSalt, 61_000 + number), def.SizeMin, def.SizeMax), Now);
    }

    // ---------------------------------------------------------------- what a site holds (pure: its seed)

    private ulong SiteRoll(SpaceSite site, long k) => SystemGenerator.H(site.Seed, SiteContentSalt, k);

    /// <summary>Research a boarding's data or computer brings at this site (kind research [min, max] by its hash).</summary>
    public int SiteResearch(SpaceSite site) => SiteKindOf(site) is { } k ? SystemGenerator.Int(SiteRoll(site, 1), k.ResearchMin, k.ResearchMax) : 0;

    /// <summary>A known derelict's logs: latinum and research (by its hash).</summary>
    public (int Latinum, int Research) SiteLogs(SpaceSite site) =>
        SiteKindOf(site)?.Logs is { } l ? (SystemGenerator.Int(SiteRoll(site, 2), l.LatinumMin, l.LatinumMax), SystemGenerator.Int(SiteRoll(site, 3), l.ResearchMin, l.ResearchMax)) : (0, 0);

    /// <summary>What a debris field gives up as it is swept, in order: salvage ids or "lifepod".</summary>
    public IReadOnlyList<string> SiteFinds(SpaceSite site)
    {
        var list = new List<string>();
        if (SiteKindOf(site)?.Sweep is not { } sw) return list;
        var n = SystemGenerator.Int(SiteRoll(site, 4), sw.FindsMin, sw.FindsMax);
        foreach (var a in sw.Always)
            if (list.Count < n) list.Add(a);
        for (var k = 0; list.Count < n && sw.Items.Count > 0; k++) list.Add(SystemGenerator.Pick(sw.Items, SiteRoll(site, 10 + k)));
        return list;
    }

    /// <summary>Survivors in the k-th lifepod of a debris field (by its hash).</summary>
    private int LifepodSurvivors(SpaceSite site, int k) =>
        SiteKindOf(site)?.Sweep is { } sw ? SystemGenerator.Int(SiteRoll(site, 40 + k), sw.SurvivorsMin, sw.SurvivorsMax) : 0;

    /// <summary>The sweep ticks at which the k-th find (of n) turns up: evenly through the sweep, the last at its end.</summary>
    private static long FindAt(long need, int k, int n) => n <= 0 ? need : need * (k + 1) / n;

    // ---------------------------------------------------------------- per player

    /// <summary>A player's own record of one site: knowledge and the work done there (kept after the site leaves).</summary>
    private sealed class SiteRecord
    {
        public string Site = "";
        public SiteKnowledge Knowledge;
        public bool Alongside, Powered, Logs, Boarded, Swept, Recalled, ReactorDone;
        public long ReactorTick = -1, SpentTick = -1, RecalledTick = -1;
        public long SweepTicks;
        public int Finds;

        public void AddToHash(ref StateHasher h)
        {
            h.Add(Site);
            h.Add((int)Knowledge);
            h.Add(Alongside);
            h.Add(Powered);
            h.Add(Logs);
            h.Add(Boarded);
            h.Add(Swept);
            h.Add(Recalled);
            h.Add(ReactorDone);
            h.Add(ReactorTick);
            h.Add(SpentTick);
            h.Add(RecalledTick);
            h.Add(SweepTicks);
            h.Add(Finds);
        }
    }

    /// <summary>A timed job at a site (one at a time per flagship).</summary>
    private sealed class WorkRun
    {
        public string Site = "", Action = "";
        public long Start, End;

        public void AddToHash(ref StateHasher h)
        {
            h.Add(Site);
            h.Add(Action);
            h.Add(Start);
            h.Add(End);
        }
    }

    private SiteRecord? Record(int player, string? siteId)
    {
        if (siteId == null || player < 0 || player >= _status.Count || _status[player] is not { } st) return null;
        foreach (var r in st.Records)
            if (r.Site == siteId) return r;
        return null;
    }

    private SiteRecord RecordFor(int player, string siteId)
    {
        var st = _status[player]!;
        if (Record(player, siteId) is { } r) return r;
        r = new SiteRecord { Site = siteId };
        st.Records.Add(r);
        st.Records.Sort((a, b) => SiteNumber(a.Site).CompareTo(SiteNumber(b.Site)));
        return r;
    }

    private static int SiteNumber(string id) => id.Length > 1 && int.TryParse(id[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>Raises what the player knows of a site (never lowers it, except to Spent).</summary>
    private void SetKnowledge(int player, string siteId, SiteKnowledge k)
    {
        if (player < 0 || player >= _status.Count || _status[player] == null || Site(siteId) == null) return;
        var r = RecordFor(player, siteId);
        if (r.Knowledge >= k) return;
        var was = r.Knowledge;
        r.Knowledge = k;
        Version++;
        if (k == SiteKnowledge.Identified && was < k && _sim != null && Site(siteId) is { } s && s.Kind != SiteKind.Station)
            ShipLog(player, $"Identified {s.Name}: {SiteKindOf(s)?.OriginOf(s.Variant) ?? ""}", "");
    }

    /// <summary>A long-range scan from here: every site within range the player didn't know becomes Detected.</summary>
    private void DetectSites(int player, FixVec2 at, Fix64 range)
    {
        foreach (var s in _sites)
            if (SiteKnowledgeOf(player, s.Id) == SiteKnowledge.Unknown && FixVec2.Distance(s.Position, at) <= range)
                SetKnowledge(player, s.Id, SiteKnowledge.Detected);
    }

    /// <summary>The player has worked the site out: Spent, and it drifts off spent_seconds later.</summary>
    private void Spend(int player, SpaceSite site, string why)
    {
        if (site.Kind == SiteKind.Station) return;
        var r = RecordFor(player, site.Id);
        if (r.Knowledge == SiteKnowledge.Spent) return;
        r.Knowledge = SiteKnowledge.Spent;
        r.SpentTick = Now;
        if (site.ExpireTick < 0 && SiteRules is { } rules) site.ExpireTick = Now + (long)rules.SpentSeconds * Simulation.TickRate;
        Version++;
        ShipLog(player, $"{site.Name} worked out: {why}", "good");
    }

    // ---------------------------------------------------------------- the view

    /// <summary>The player's card for a site, or null if they don't know it.</summary>
    public SiteReport? SiteReportFor(int player, string siteId)
    {
        var site = Site(siteId);
        var know = SiteKnowledgeOf(player, siteId);
        if (site == null || know == SiteKnowledge.Unknown) return null;
        var def = SiteKindOf(site);
        var r = Record(player, siteId)!;
        var rows = new List<SiteRow>();
        var actions = new List<SiteAction>();
        var hazards = new List<string>();
        var ship = FlagshipOf(player);
        var flight = SiteFlightSeconds(player, siteId);
        var distance = ship != null ? FixVec2.Distance(ShipPosition(ship, Now), site.Position).FloorToInt() : -1;
        if (know == SiteKnowledge.Detected)
        {
            rows.Add(new SiteRow("SIZE", $"~{site.Size} · {RoughSize(site.Size)}"));
            if (distance >= 0) rows.Add(new SiteRow("DISTANCE", $"{distance} units"));
            if (flight > 0) rows.Add(new SiteRow("FLIGHT", $"{flight} s"));
            if (SiteScanShare(player, siteId) is { } share) rows.Add(new SiteRow("SCAN", $"{(share * Fix64.FromInt(100)).FloorToInt()}%", "good"));
            AddCourseAndScan(player, site, actions);
            return new SiteReport(siteId, site.Kind, know, "Unknown contact", $"a contact, size ~{site.Size}", site.Position, rows, actions, hazards);
        }
        var origin = def?.OriginOf(site.Variant) ?? "";
        rows.Add(new SiteRow("KIND", def?.Name ?? SpaceSiteRules.IdOf(site.Kind)));
        if (origin.Length > 0) rows.Add(new SiteRow("ORIGIN", origin));
        rows.Add(new SiteRow("SIZE", $"{site.Size} · {RoughSize(site.Size)}"));
        if (distance > 0) rows.Add(new SiteRow("DISTANCE", $"{distance} units · {flight} s"));
        if (know == SiteKnowledge.Spent) rows.Add(new SiteRow("STATE", site.ExpireTick >= 0 ? $"worked out · drifts off in {SecondsUntil(site.ExpireTick)} s" : "worked out", "warn"));
        if (def != null) hazards.AddRange(def.Hazards);
        switch (site.Kind)
        {
            case SiteKind.Station when SiteRules is { } sr:
                rows.Add(new SiteRow("DOCKYARD", $"hull back to 100% and every system fixed in {sr.Station.DockSeconds} s"));
                rows.Add(new SiteRow("BUYS", "salvage, for latinum"));
                var left = ship?.AtSite == siteId && _status[player] is { } st0 ? System.Math.Max(0, sr.Station.HirePerVisit - st0.HiredThisVisit) : sr.Station.HirePerVisit;
                rows.Add(new SiteRow("HIRES", $"{left} crew this visit · {sr.Station.HireLatinum} latinum each"));
                break;
            case SiteKind.Derelict:
                rows.Add(new SiteRow("POWER", "one trickle reading, aft"));
                rows.Add(new SiteRow("LIFE", "faint, one reading (unclear)"));
                rows.Add(new SiteRow("ALONGSIDE", r.Alongside ? "yes" : "no", r.Alongside ? "good" : ""));
                if (r.Boarded) rows.Add(new SiteRow("BOARDED", "yes", "good"));
                break;
            case SiteKind.KnownDerelict:
                rows.Add(new SiteRow("ALONGSIDE", r.Alongside ? "yes" : "no", r.Alongside ? "good" : ""));
                rows.Add(new SiteRow("POWER", r.Powered ? "restored" : "dark", r.Powered ? "good" : "warn"));
                rows.Add(new SiteRow("LOGS", r.Logs ? "recovered" : r.Powered ? "readable" : "need power", r.Logs ? "good" : ""));
                if (r.ReactorTick >= 0 && !r.ReactorDone) rows.Add(new SiteRow("REACTOR", $"fails in {SecondsUntil(r.ReactorTick)} s", "bad"));
                else if (r.ReactorDone) rows.Add(new SiteRow("REACTOR", "failed", "bad"));
                else rows.Add(new SiteRow("REACTOR", "near failure: a timer starts once alongside", "warn"));
                if (r.Boarded) rows.Add(new SiteRow("SEARCHED", "yes", "good"));
                break;
            case SiteKind.Debris:
            {
                var finds = SiteFinds(site);
                rows.Add(new SiteRow("SWEPT", r.Swept ? "all of it" : $"{r.Finds} of {finds.Count} finds", r.Swept ? "good" : ""));
                if (def?.Sweep is { } sw) rows.Add(new SiteRow("INSIDE", $"hull −{sw.Hull}% every {sw.HullEverySeconds} s", "warn"));
                break;
            }
        }
        if (SiteWorkProgress(player, siteId) is { } work)
            rows.Add(new SiteRow("WORK", $"{work.Label} · {(work.Share * Fix64.FromInt(100)).FloorToInt()}% · {work.SecondsLeft} s", "good"));
        if (AwayTeamOf(player) is { Done: false } away && away.SiteId == siteId)
            rows.Add(new SiteRow("AWAY TEAM", away.Step < away.Steps ? $"step {away.Step + 1} of {away.Steps}: {away.StepTitle}" : "coming back aboard", "good"));
        AddCourseAndScan(player, site, actions);
        if (know != SiteKnowledge.Spent || site.Kind == SiteKind.Station)
        {
            foreach (var a in WorkActions)
            {
                if (!Offers(site, r, a)) continue;
                actions.Add(new SiteAction(a, WorkLabel(a, def), ship == null ? "no ship" : SiteWorkProblem(player, ship.Id, siteId, a)));
            }
            if (def is { AwayTable.Length: > 0 } && !r.Boarded)
                actions.Add(new SiteAction("away_team", "AWAY TEAM", AwayTeamProblem(player, siteId, null)));
        }
        return new SiteReport(siteId, site.Kind, know, site.Name, origin, site.Position, rows, actions, hazards);
    }

    private void AddCourseAndScan(int player, SpaceSite site, List<SiteAction> actions)
    {
        var ship = FlagshipOf(player);
        if (ship?.AtSite != site.Id) actions.Add(new SiteAction("course", "SET COURSE", ship == null ? "no ship" : SiteCourseProblem(player, ship.Id, site.Id)));
        if (SiteKnowledgeOf(player, site.Id) == SiteKnowledge.Detected)
            actions.Add(new SiteAction("scan", "SCAN", ship == null ? "no ship" : SiteScanProblem(player, ship.Id, site.Id)));
    }

    /// <summary>Whether the card shows this work action for the site now (its kind has it and it isn't done).</summary>
    private bool Offers(SpaceSite site, SiteRecord r, string action)
    {
        var def = SiteKindOf(site);
        return action switch
        {
            "alongside" => def?.Alongside != null && !r.Alongside,
            "restore_power" or "restore_power_kit" => def?.RestorePower != null && !r.Powered && (action == "restore_power" || def.RestorePower.Kits > 0),
            "recover_logs" => def?.Logs != null && !r.Logs,
            "sweep" => def?.Sweep != null && !r.Swept,
            "dock" => site.Kind == SiteKind.Station,
            "stop" => false, // the view shows STOP on running work (SiteWorkProgress)
            _ => false,
        };
    }

    private static string WorkLabel(string action, SiteKindDef? def) => action switch
    {
        "alongside" => "COME ALONGSIDE",
        "restore_power" => "RESTORE POWER",
        "restore_power_kit" => $"RESTORE POWER ({def?.RestorePower?.Kits ?? 1} PATCH KIT)",
        "recover_logs" => "RECOVER LOGS",
        "sweep" => "SWEEP THE FIELD",
        "dock" => "DOCK FOR REPAIRS",
        _ => action.ToUpperInvariant(),
    };

    // ---------------------------------------------------------------- problems

    /// <summary>Why the player's ship can't fly to the site now, or null.</summary>
    public string? SiteCourseProblem(int player, int shipId, string siteId)
    {
        var ship = FindShip(shipId);
        if (ship == null) return "no such ship";
        if (ship.Owner != player || player < 0) return "not your ship";
        if (StarfleetHoldProblem(player) is { } held) return held;
        if (Site(siteId) is not { } site || SiteKnowledgeOf(player, siteId) == SiteKnowledge.Unknown) return "no such site";
        if (ship.InFlight) return $"in flight to {FlightGoalName(player, ship)} (arrives in {SecondsUntil(ship.ArriveTick)} s)";
        if (ship.AtSite == siteId) return $"already at {SiteNameFor(player, siteId)}";
        if (CrippledProblem(player, ship) is { } crippled) return crippled;
        if (site.ExpireTick >= 0 && site.ExpireTick - Now < Simulation.TickRate * 2) return $"{SiteNameFor(player, siteId)} is drifting out of the system";
        return null;
    }

    /// <summary>Why the player's ship can't scan the site now, or null.</summary>
    public string? SiteScanProblem(int player, int shipId, string siteId)
    {
        var ship = FindShip(shipId);
        if (ship == null) return "no such ship";
        if (ship.Owner != player || player < 0) return "not your ship";
        if (StarfleetHoldProblem(player) is { } held) return held;
        if (Site(siteId) is not { } site || SiteKnowledgeOf(player, siteId) == SiteKnowledge.Unknown) return "no such site";
        if (SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Identified) return $"{site.Name} is identified already";
        if (Status(player) is not { } st) return "this ship has no sensors fitted for it";
        if (st.Crippled) return "the ship is crippled";
        if (st.Scan != null) return st.Scan.Site == siteId ? "scanning it already" : $"already scanning {SiteNameFor(player, st.Scan.Site)}";
        var range = IdentifyRangeFor(player);
        var d = FixVec2.Distance(ShipPosition(ship, Now), site.Position);
        if (d > range) return $"out of scan range ({d.FloorToInt()} units; fly within {range.FloorToInt()})";
        return null;
    }

    /// <summary>How close the ship must be to identify a site (identify_range × a damaged hull's scan reach).</summary>
    public Fix64 IdentifyRangeFor(int player)
    {
        var range = SiteRules?.IdentifyRange ?? Fix64.FromInt(120);
        var f = ScanReachFactor(player);
        return f == Fix64.One ? range : range * f;
    }

    /// <summary>Why the work action can't run at the site now, or null.</summary>
    public string? SiteWorkProblem(int player, int shipId, string siteId, string action)
    {
        var ship = FindShip(shipId);
        if (ship == null) return "no such ship";
        if (ship.Owner != player || player < 0) return "not your ship";
        if (Status(player) is not { } st) return "this ship can't work sites";
        if (action == "stop") return st.Work != null && st.Work.Site == siteId ? null : "nothing is running there";
        if (Site(siteId) is not { } site || SiteKnowledgeOf(player, siteId) == SiteKnowledge.Unknown) return "no such site";
        if (!WorkActions.Contains(action)) return $"no such work '{action}'";
        var def = SiteKindOf(site);
        var r = Record(player, siteId)!;
        if (ship.AtSite != siteId) return $"the ship must be at {SiteNameFor(player, siteId)}";
        if (StarfleetHoldProblem(player) is { } held) return held;
        if (SiteKnowledgeOf(player, siteId) < SiteKnowledge.Identified) return "scan it first";
        if (SiteKnowledgeOf(player, siteId) == SiteKnowledge.Spent) return $"{site.Name} is worked out";
        if (st.Crippled) return "the ship is crippled";
        if (action == "dock") return RepairProblem(player, "dock");
        if (st.Work != null) return $"busy: {WorkText(st.Work.Action).ToLowerInvariant()}";
        if (def is { Hazardous: true } && !HazardousOk(st)) return "the hull is critical: no hazardous jobs until it is repaired";
        switch (action)
        {
            case "alongside":
                if (def?.Alongside == null) return "nothing to come alongside";
                return r.Alongside ? "alongside already" : null;
            case "restore_power":
            case "restore_power_kit":
                if (def?.RestorePower is not { } rp) return "it has no power to restore";
                if (r.Powered) return "power is on already";
                if (!r.Alongside) return "come alongside first";
                if (action == "restore_power_kit") return Count(st, PatchKitItem) < rp.Kits ? $"needs {rp.Kits} patch kit{(rp.Kits == 1 ? "" : "s")} (have {Count(st, PatchKitItem)})" : null;
                return OnDuty(st, "engineering") <= 0 ? "no Engineering crew on duty" : null;
            case "recover_logs":
                if (def?.Logs == null) return "no logs to recover";
                if (r.Logs) return "the logs are recovered";
                return !r.Powered ? "restore power first" : null;
            case "sweep":
            {
                if (def?.Sweep == null) return "nothing to sweep";
                if (r.Swept) return "swept already";
                if (IsDown(st, ShipSystem.Tractor)) return "the tractor is down: nothing can be picked up";
                var finds = SiteFinds(site);
                if (r.Finds < finds.Count && !Fits(st, finds[r.Finds], 1)) return "the hold is full: sell, drop or jettison something first";
                return null;
            }
        }
        return "no such work";
    }

    /// <summary>Scan progress 0..1 of the player's running site scan, or null.</summary>
    public Fix64? SiteScanShare(int player, string siteId)
    {
        if (Status(player)?.Scan is not { } scan || scan.Site != siteId) return null;
        return Share(scan.Start, scan.End);
    }

    /// <summary>Work in progress at the site for the player: label ("Sweeping the field", "Mining"), share 0..1 and
    /// seconds left; null when nothing runs.</summary>
    public (string Label, Fix64 Share, int SecondsLeft)? SiteWorkProgress(int player, string siteId)
    {
        if (Status(player) is not { } st) return null;
        if (st.Work is { } w && w.Site == siteId)
        {
            if (w.Action == "sweep" && Site(siteId) is { } site && SiteKindOf(site)?.Sweep is { } sw)
            {
                var need = (long)sw.Seconds * Simulation.TickRate;
                var done = Record(player, siteId)?.SweepTicks ?? 0;
                return (WorkText(w.Action), Fix64.Clamp(Fix64.FromRaw((long)(((System.Int128)done << Fix64.FractionalBits) / System.Math.Max(1, need))), Fix64.Zero, Fix64.One),
                    (int)((need - done + Simulation.TickRate - 1) / Simulation.TickRate));
            }
            return (WorkText(w.Action), Share(w.Start, w.End), SecondsUntil(w.End));
        }
        if (st.RepairAction == "dock" && FlagshipOf(player)?.AtSite == siteId)
            return ("Docked for repairs", Share(st.RepairStart, st.RepairEnd), SecondsUntil(st.RepairEnd));
        return null;
    }

    private static string WorkText(string action) => action switch
    {
        "alongside" => "Coming alongside",
        "restore_power" => "Restoring power",
        "recover_logs" => "Recovering the logs",
        "sweep" => "Sweeping the field",
        _ => action,
    };

    private Fix64 Share(long start, long end)
    {
        var span = end - start;
        if (span <= 0) return Fix64.One;
        var done = System.Math.Clamp(Now - start, 0, span);
        return Fix64.FromRaw((long)(((System.Int128)done << Fix64.FractionalBits) / span));
    }

    // ---------------------------------------------------------------- commands

    internal void ApplySpaceJobs(Simulation sim, Command command)
    {
        switch (command)
        {
            case SiteCourseCommand course:
            {
                if (SiteCourseProblem(course.Player, course.ShipId, course.SiteId) is { } problem)
                {
                    sim.Refuse(course, problem);
                    return;
                }
                var ship = FindShip(course.ShipId)!;
                var site = Site(course.SiteId)!;
                var ticks = System.Math.Max(1L, TravelTicksFor(ship, ShipPosition(ship, sim.Tick), site.Position));
                if (ship.AtSite != null) LeaveSite(sim, ship, $"set course for {SiteNameFor(course.Player, site.Id)}");
                Depart(ship, null!, sim.Tick, ticks);
                ship.ToBody = null;
                ship.ToSite = site.Id;
                return;
            }
            case SiteScanCommand scan:
            {
                if (SiteScanProblem(scan.Player, scan.ShipId, scan.SiteId) is { } problem)
                {
                    sim.Refuse(scan, problem);
                    return;
                }
                var st = Status(scan.Player)!;
                var site = Site(scan.SiteId)!;
                var ticks = (long)(SiteKindOf(site)?.ScanSeconds ?? 30) * Simulation.TickRate;
                ticks = Scaled(ticks, Fit(scan.Player, FitOutRules.SweepTime));
                if (IsDown(st, ShipSystem.Sensors) && StatusRules?.System(ShipSystem.Sensors) is { } sensors) ticks = Scaled(ticks, sensors.Scan);
                st.Scan = new ScanRun { Site = site.Id, Start = sim.Tick, End = sim.Tick + System.Math.Max(1L, ticks) };
                Version++;
                return;
            }
            case SiteWorkCommand work:
            {
                if (SiteWorkProblem(work.Player, work.ShipId, work.SiteId, work.Action) is { } problem)
                {
                    sim.Refuse(work, problem);
                    return;
                }
                StartWork(sim, work.Player, Site(work.SiteId)!, work.Action);
                return;
            }
            case AwayTeamCommand team:
                ApplyAwayTeam(sim, team); // AwayTeam.cs
                return;
            case AwayTeamChoiceCommand choice:
                ApplyAwayChoice(sim, choice); // AwayTeam.cs
                return;
            case ShipRepairCommand repair:
                ApplyRepair(sim, repair); // ShipStatus.cs
                return;
            case ShipHoldCommand hold:
                ApplyHold(sim, hold); // ShipStatus.cs
                return;
        }
    }

    private void StartWork(Simulation sim, int player, SpaceSite site, string action)
    {
        var st = Status(player)!;
        var def = SiteKindOf(site)!;
        var r = RecordFor(player, site.Id);
        Version++;
        switch (action)
        {
            case "stop":
                ShipLog(player, $"{WorkText(st.Work!.Action)} at {site.Name}: stopped", "");
                st.Work = null;
                return;
            case "dock":
                BeginDock(sim, player);
                return;
            case "restore_power_kit":
                Take(st, PatchKitItem, def.RestorePower!.Kits);
                r.Powered = true;
                ShipLog(player, $"Power restored aboard {site.Name} with {def.RestorePower.Kits} patch kit{(def.RestorePower.Kits == 1 ? "" : "s")}", "good");
                return;
        }
        long ticks = action switch
        {
            "alongside" => Scaled((long)def.Alongside!.Seconds * Simulation.TickRate, Fit(player, FitOutRules.AlongsideTime)),
            "restore_power" => Faster((long)def.RestorePower!.Seconds * Simulation.TickRate, Fit(player, FitOutRules.FieldRepair)),
            "recover_logs" => (long)def.Logs!.Seconds * Simulation.TickRate,
            "sweep" => (long)def.Sweep!.Seconds * Simulation.TickRate - r.SweepTicks,
            _ => Simulation.TickRate,
        };
        st.Work = new WorkRun { Site = site.Id, Action = action, Start = sim.Tick, End = sim.Tick + System.Math.Max(1L, ticks) };
        if (action == "sweep") ShipLog(player, $"Into the debris of {site.Name}: sweeping", "warn");
    }

    /// <summary>Ticks ÷ a more-is-better factor (at least 1 tick; unchanged when the factor is 1).</summary>
    private static long Faster(long ticks, Fix64 factor) =>
        factor == Fix64.One || factor <= Fix64.Zero ? ticks : System.Math.Max(1L, (Fix64.FromInt((int)System.Math.Min(int.MaxValue, ticks)) / factor).FloorToInt());

    /// <summary>A ship arriving at a site holds position there (not in orbit of anything): the site is at least Detected.</summary>
    private void ArriveAtSite(Simulation sim, SystemShip ship)
    {
        ship.AtSite = ship.ToSite;
        ship.ToSite = null;
        ship.FromBody = null;
        ship.FromSite = null;
        Version++;
        if (ship.Owner < 0 || ship.AtSite is not { } id || Site(id) is not { } site) return;
        SetKnowledge(ship.Owner, id, SiteKnowledge.Detected);
        if (Status(ship.Owner) is { } st)
        {
            st.HiredThisVisit = 0;
            ShipLog(ship.Owner, $"Holding position at {SiteNameFor(ship.Owner, id)}", "");
        }
    }

    /// <summary>The ship leaves its site: work there stops (alongside has to be done again), a dock repair stops, and an
    /// away team there is called back (the boarding fails).</summary>
    private void LeaveSite(Simulation sim, SystemShip ship, string why)
    {
        if (ship.AtSite is not { } id || Status(ship.Owner) is not { } st) return;
        if (st.Work is { } w && w.Site == id)
        {
            if (w.Action != "sweep") ShipLog(ship.Owner, $"{WorkText(w.Action)} called off: {why}", "warn");
            st.Work = null;
        }
        if (st.RepairAction == "dock")
        {
            st.RepairAction = "";
            st.RepairEnd = -1;
            ShipLog(ship.Owner, $"Dock repairs stopped: {why}", "warn");
        }
        if (Record(ship.Owner, id) is { } r) r.Alongside = false;
        if (st.Away is { Done: false } away && away.Site == id) RecallAwayTeam(sim, ship.Owner, $"the ship left {SiteNameFor(ship.Owner, id)}"); // AwayTeam.cs
        Version++;
    }

    /// <summary>The ship's flight goal as the player knows it.</summary>
    private string FlightGoalName(int player, SystemShip ship) =>
        ship.ToSite != null ? SiteNameFor(player, ship.ToSite) : ship.ToBody != null ? NameFor(player, ship.ToBody) : "somewhere";

    // ---------------------------------------------------------------- ticks

    /// <summary>One tick of space jobs: sites drifting off, then each flagship (ShipStatus.cs) and its scan, work and away team.</summary>
    private void StepSpaceJobs(Simulation sim)
    {
        if (_status.Count == 0 && _sites.Count == 0) return;
        for (var i = _sites.Count - 1; i >= 0; i--)
        {
            var s = _sites[i];
            if (s.ExpireTick < 0 || sim.Tick < s.ExpireTick || Referenced(s.Id)) continue;
            _sites.RemoveAt(i);
            Version++;
        }
        for (var p = 0; p < _status.Count; p++)
        {
            if (_status[p] is not { } st) continue;
            StepShipStatus(sim, p, st); // ShipStatus.cs
            StepScan(sim, p, st);
            StepWork(sim, p, st);
            StepReactors(sim, p, st);
            StepAwayTeam(sim, p, st); // AwayTeam.cs
        }
    }

    /// <summary>A ship is at, or flying to or from, the site (so it can't drift off yet).</summary>
    private bool Referenced(string siteId)
    {
        foreach (var s in _ships)
            if (s.AtSite == siteId || s.ToSite == siteId || s.FromSite == siteId) return true;
        return false;
    }

    private void StepScan(Simulation sim, int player, ShipState st)
    {
        if (st.Scan is not { } scan) return;
        var ship = FlagshipOf(player);
        var site = Site(scan.Site);
        if (ship == null || site == null || FixVec2.Distance(ShipPosition(ship, sim.Tick), site.Position) > IdentifyRangeFor(player))
        {
            st.Scan = null;
            Version++;
            if (site != null) ShipLog(player, $"Scan of {SiteNameFor(player, site.Id)} called off: out of range", "warn");
            return;
        }
        if (sim.Tick < scan.End) return;
        st.Scan = null;
        SetKnowledge(player, site.Id, SiteKnowledge.Identified);
    }

    private void StepWork(Simulation sim, int player, ShipState st)
    {
        if (st.Work is not { } w) return;
        var site = Site(w.Site);
        var ship = FlagshipOf(player);
        if (site == null || ship?.AtSite != w.Site || st.Crippled)
        {
            st.Work = null;
            Version++;
            return;
        }
        var def = SiteKindOf(site);
        var r = RecordFor(player, site.Id);
        if (w.Action == "sweep" && def?.Sweep is { } sw)
        {
            if (IsDown(st, ShipSystem.Tractor))
            {
                st.Work = null;
                ShipLog(player, $"Sweep of {site.Name} stopped: the tractor is down", "warn");
                Version++;
                return;
            }
            r.SweepTicks++;
            var every = (long)sw.HullEverySeconds * Simulation.TickRate;
            if (sw.Hull > 0 && every > 0 && r.SweepTicks % every == 0) Damage(sim, player, Fix64.FromInt(sw.Hull), $"debris inside {site.Name}", quiet: true);
            if (st.Work == null) return; // crippled by it
            var finds = SiteFinds(site);
            var need = (long)sw.Seconds * Simulation.TickRate;
            while (r.Finds < finds.Count && r.SweepTicks >= FindAt(need, r.Finds, finds.Count))
            {
                if (!PickUp(sim, player, site, finds[r.Finds], r.Finds))
                {
                    st.Work = null;
                    r.SweepTicks = System.Math.Max(0, r.SweepTicks - 1); // stays where it was: the find waits for room
                    ShipLog(player, $"Sweep of {site.Name} stopped: the hold is full", "warn");
                    Version++;
                    return;
                }
                r.Finds++;
                Version++;
            }
            if (r.SweepTicks >= need)
            {
                st.Work = null;
                r.Swept = true;
                Spend(player, site, "every find recovered");
            }
            return;
        }
        if (sim.Tick < w.End) return;
        st.Work = null;
        Version++;
        switch (w.Action)
        {
            case "alongside":
                r.Alongside = true;
                ShipLog(player, $"Alongside {site.Name}", "good");
                if (def?.Reactor is { } re && r.ReactorTick < 0 && !r.ReactorDone)
                {
                    r.ReactorTick = sim.Tick + (long)re.Seconds * Simulation.TickRate;
                    ShipLog(player, $"{site.Name}'s reactor is failing: {re.Seconds} s", "bad");
                }
                if (def?.Alongside is { Chance: > 0 } al && sim.Rng.NextInt(100) < al.Chance)
                    Damage(sim, player, Fix64.FromInt(sim.Rng.NextInt(al.HullMin, al.HullMax + 1)), $"debris strike at {site.Name}");
                break;
            case "restore_power":
                r.Powered = true;
                ShipLog(player, $"Power restored aboard {site.Name}", "good");
                break;
            case "recover_logs":
            {
                r.Logs = true;
                var (latinum, research) = SiteLogs(site);
                var paid = GiveLatinum(sim, player, latinum);
                var text = GiveSpaceResearch(sim, player, Fix64.FromInt(research));
                ShipLog(player, $"Logs of {site.Name} recovered{(paid > 0 ? $": {paid} latinum from the owners" : "")}{(text.Length > 0 ? $", {text}" : "")}", "good");
                break;
            }
        }
    }

    /// <summary>Known derelicts' reactors: when one fails with the ship still alongside it, the hull takes the blast; either
    /// way the wreck is gone (spent).</summary>
    private void StepReactors(Simulation sim, int player, ShipState st)
    {
        foreach (var r in st.Records)
        {
            if (r.ReactorTick < 0 || r.ReactorDone || sim.Tick < r.ReactorTick) continue;
            r.ReactorDone = true;
            Version++;
            if (Site(r.Site) is not { } site) continue;
            var ship = FlagshipOf(player);
            if (ship?.AtSite == site.Id && r.Alongside && SiteKindOf(site)?.Reactor is { } re)
            {
                if (st.Away is { Done: false } away && away.Site == site.Id) RecallAwayTeam(sim, player, "the reactor failed", injureAll: true);
                Damage(sim, player, Fix64.FromInt(re.Hull), $"{site.Name}'s reactor failed alongside");
            }
            else ShipLog(player, $"{site.Name}'s reactor failed", "warn");
            Spend(player, site, "its reactor failed");
        }
    }

    /// <summary>Something found in a debris field comes aboard: salvage into the hold (false when it doesn't fit), a
    /// lifepod's survivors (people always come aboard).</summary>
    private bool PickUp(Simulation sim, int player, SpaceSite site, string find, int index)
    {
        var st = Status(player)!;
        if (find == "lifepod")
        {
            var n = LifepodSurvivors(site, index);
            var divisions = DivisionIds();
            for (var k = 0; k < n && divisions.Count > 0; k++)
                TakeSurvivor(sim, player, divisions[(int)(SiteRoll(site, 70 + index * 8 + k) % (ulong)divisions.Count)], site.Name);
            ShipLog(player, $"A lifepod in {site.Name}: {n} survivor{(n == 1 ? "" : "s")} aboard", "good");
            return true;
        }
        if (!Fits(st, SalvageItemId(find), 1)) return false;
        Add(st, SalvageItemId(find), 1);
        ShipLog(player, $"Recovered from {site.Name}: {SiteRules?.SalvageItem(find)?.Name ?? find}", "good");
        return true;
    }

    // ---------------------------------------------------------------- rewards that never land

    /// <summary>Latinum into the player's stock (what stations pay; it never needs storage).</summary>
    private int GiveLatinum(Simulation sim, int player, int amount)
    {
        if (amount <= 0 || player < 0 || player >= sim.Players.Count || !sim.Data.HasResource(GameData.Latinum)) return 0;
        sim.Players[player].Stock.Add(sim.Data.Resource(GameData.Latinum).Index, Fix64.FromInt(amount));
        return amount;
    }

    /// <summary>Research from space jobs: where observation research goes (the active surface tech, else the bank).
    /// Returns the text for a log line ("" for none).</summary>
    internal string GiveSpaceResearch(Simulation sim, int player, Fix64 points)
    {
        if (points <= Fix64.Zero || player < 0 || player >= _civPlayers.Count) return "";
        var p = _civPlayers[player];
        p.Bank += points;
        PayResearch(sim, player, p); // Civilizations.cs
        return $"+{points.FloorToInt()} research";
    }

    // ---------------------------------------------------------------- debug (test panel SPACE tab)

    public string? DebugSpaceProblem(Simulation sim, DebugSpaceCommand command)
    {
        var p = command.Target;
        if (p < 0 || p >= sim.Players.Count) return "no such player";
        if (Status(p) is not { } st) return "that player's ship has no status (no ship status data)";
        switch (command.Field)
        {
            case DebugSpaceField.SpawnSite:
                if (SpaceSiteRules.KindOf(command.Id) is not { } kind || SiteRules?.Kind(kind) is not { } def) return $"no site kind '{command.Id}'";
                if (kind == SiteKind.Station) return "a system has one station";
                if (command.Value < -1 || command.Value >= def.Variants.Count) return $"variant index -1..{def.Variants.Count - 1}";
                return FlagshipOf(p) == null ? "no flagship to put it next to" : null;
            case DebugSpaceField.RemoveSite:
                if (Site(command.Id) is not { } rs) return $"no site '{command.Id}'";
                if (rs.Kind == SiteKind.Station) return "the station stays";
                return Referenced(rs.Id) ? "a ship is there or on its way" : null;
            case DebugSpaceField.IdentifySite:
                if (Site(command.Id) == null) return $"no site '{command.Id}'";
                return SiteKnowledgeOf(p, command.Id!) >= SiteKnowledge.Identified ? "identified already" : null;
            case DebugSpaceField.Hull:
                return command.Value is < 0 or > 100 ? "hull 0..100" : null;
            case DebugSpaceField.SystemDown:
                if (GameData.ShipSystemOf(command.Id) == null) return $"no system '{command.Id}'";
                return command.Value is < 0 or > 3600 ? "0..3600 s" : null;
            case DebugSpaceField.Crew:
            case DebugSpaceField.Injure:
            {
                var d = DivisionIndex(command.Id);
                if (d < 0 || d >= st.OnDuty.Length) return $"no division '{command.Id}'";
                if (command.Field == DebugSpaceField.Injure) return command.Value < 1 || command.Value > st.OnDuty[d] ? $"1..{st.OnDuty[d]} on duty" : null;
                if (command.Value == 0 || command.Value < -st.OnDuty[d] || command.Value > 1000) return $"-{st.OnDuty[d]}..1000, not 0";
                return null;
            }
            case DebugSpaceField.HoldItem:
                if (!KnownHoldItem(command.Id)) return $"no hold item '{command.Id}' (patch_kit, medical, salvage:<id>, survivors:<division>)";
                return command.Value is < 0 or > 1000 ? "0..1000" : null;
            case DebugSpaceField.FinishAwayTeam:
                if (st.Away is not { Done: false } away) return "no away team out";
                var table = AwayTableAt(away.Site);
                return command.Value < -1 || command.Value >= (table?.Outcomes.Count ?? 0) ? $"outcome -1..{(table?.Outcomes.Count ?? 0) - 1}" : null;
        }
        return "unknown field";
    }

    internal void ApplyDebugSpace(Simulation sim, DebugSpaceCommand command)
    {
        var p = command.Target;
        var st = Status(p)!;
        Version++;
        switch (command.Field)
        {
            case DebugSpaceField.SpawnSite:
            {
                var kind = SpaceSiteRules.KindOf(command.Id)!.Value;
                var def = SiteRules!.Kind(kind)!;
                var ship = FlagshipOf(p)!;
                var variant = command.Value >= 0 ? def.Variants[command.Value].Id : null;
                var site = SpawnSite(kind, variant, ShipPosition(ship, sim.Tick), Fix64.FromInt(25), Fix64.FromInt(45));
                if (site != null) SetKnowledge(p, site.Id, SiteKnowledge.Detected);
                return;
            }
            case DebugSpaceField.RemoveSite:
                _sites.RemoveAll(s => s.Id == command.Id);
                return;
            case DebugSpaceField.IdentifySite:
                SetKnowledge(p, command.Id!, SiteKnowledge.Identified);
                return;
            case DebugSpaceField.Hull:
                SetHull(sim, p, Fix64.FromInt(command.Value), "test panel");
                return;
            case DebugSpaceField.SystemDown:
            {
                var system = GameData.ShipSystemOf(command.Id)!.Value;
                if (command.Value == 0) FixSystem(p, system, "test panel");
                else KnockOut(sim, p, system, command.Value, "test panel");
                return;
            }
            case DebugSpaceField.Crew:
            {
                var d = DivisionIndex(command.Id);
                if (command.Value < 0) LoseCrew(p, d, -command.Value, "test panel");
                else GiveCrew(p, d, command.Value, "test panel");
                return;
            }
            case DebugSpaceField.Injure:
                InjureCrew(sim, p, DivisionIndex(command.Id), command.Value, "test panel");
                return;
            case DebugSpaceField.HoldItem:
                Set(st, command.Id!, command.Value);
                return;
            case DebugSpaceField.FinishAwayTeam:
                FinishAwayTeam(sim, p, command.Value); // AwayTeam.cs
                return;
        }
    }

    // ---------------------------------------------------------------- hash

    private void AddSpaceJobsToHash(ref StateHasher h)
    {
        if (_sites.Count == 0 && _status.Count == 0 && _nextSiteId == 1) return; // a data set with no space jobs keeps its hashes
        h.Add("space_jobs");
        h.Add(_nextSiteId);
        h.Add(_sites.Count);
        foreach (var s in _sites) s.AddToHash(ref h);
        h.Add(_status.Count);
        foreach (var st in _status)
        {
            h.Add(st != null);
            st?.AddToHash(ref h);
        }
    }
}
