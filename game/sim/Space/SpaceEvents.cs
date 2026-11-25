using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Space jobs, phase 3 (docs/space-jobs-contract.md "Phase 3"; data maps/events.json): sites that arrive during the match
// (a rogue asteroid on a collision course, a comet crossing the system, lifepods with their air running out, a stranded
// ship calling for help) and anomalies (ion storms: some rolled with the system, some arriving). When and what is the
// system's hash of the event's number (never sim.Rng); outcome rolls (an ambush's damage, a system knocked out in a storm)
// are on sim.Rng. An event is announced on every player's ship log and is Detected for everyone. Moving sites follow a
// straight path (SitePosition). Hashed with the space sites (system games only).

/// <summary>One lifepod of a lifepods site: Index, Survivors aboard, AirTick (its air runs out then), State "waiting" |
/// "taken" | "lost".</summary>
public sealed record PodView(int Index, int Survivors, long AirTick, string State);

/// <summary>An announcement: an event arrived (Distress: someone is calling for help) or ended. Text as on the ship log.</summary>
public sealed record SpaceEventNote(long Tick, string SiteId, SiteKind Kind, string Text, bool Distress);

/// <summary>What the view needs of an event site: its path (PathStart &lt; 0 = it doesn't move), its target / destination
/// body, the impact tick (a rogue asteroid still on course; -1 otherwise), the comet's tail window, the stranded ship's air
/// (-1 = powered or none), the pods, and how it ended (Outcome "" while it runs: deflected, impact, gone, cleared, rescued,
/// trap, lost; OutcomeBy = the player who did it, -1 none).</summary>
public sealed record SpaceEventView(string SiteId, SiteKind Kind, FixVec2 PathFrom, FixVec2 PathTo, long PathStart, long PathEnd, string TargetBody,
    long ImpactTick, long WindowStart, long WindowEnd, long AirTick, IReadOnlyList<PodView> Pods, string Outcome, int OutcomeBy, long OutcomeTick);

public sealed partial class StarSystemState
{
    private const ulong EventSalt = 0x5_17E5_0004UL, AnomalySalt = 0x5_17E5_0005UL;

    /// <summary>When the next event turn comes (-1 = no events in this data set); turns taken; events spawned.</summary>
    private long _nextEventTick = -1;
    private int _eventTurn, _eventSerial;

    private readonly List<SpaceEventNote> _eventNotes = new();

    /// <summary>The newest announcements (at most 20), oldest first.</summary>
    public IReadOnlyList<SpaceEventNote> EventNotes => _eventNotes;

    /// <summary>Announcements ever made (the view can tell new ones by it).</summary>
    public int EventNoteCount { get; private set; }

    /// <summary>The next event turn (sim tick), or -1.</summary>
    public long NextEventTick => _nextEventTick;

    private static bool IsEventKind(SiteKind kind) => GameData.EventKinds.Contains(kind);

    // ---------------------------------------------------------------- set-up

    /// <summary>With the system (after the asteroids, on their own salt): the anomalies of generation.anomalies, and the
    /// event clock.</summary>
    private void InitSpaceEvents(SpaceSiteRules r)
    {
        if (r.Anomalies is { CountMax: > 0 } ag && r.Kind(SiteKind.Anomaly) is { } def)
        {
            var count = SystemGenerator.Int(Hash(AnomalySalt, 1), ag.CountMin, ag.CountMax);
            for (var k = 0; k < count; k++)
            {
                if (PlaceBy(r, AnomalySalt, 1000L + k * 1000L, r.OrbitMin, r.OrbitMax, r.Spacing) is not { } at) continue;
                var variant = def.Variants.Count > 0 ? SystemGenerator.Pick(def.Variants, Hash(AnomalySalt, 20 + k)) : "";
                var site = AddSite(SiteKind.Anomaly, variant, null, at, SystemGenerator.Int(Hash(AnomalySalt, 30 + k), def.SizeMin, def.SizeMax), 0);
                SetPath(site, at, at + Direction(Hash(AnomalySalt, 40 + k)) * ag.Drift, 0, System.Math.Max(1L, (long)ag.DriftSeconds * Simulation.TickRate));
            }
        }
        if (r.Events is { } ev) _nextEventTick = (long)ev.CalmSeconds * Simulation.TickRate + EventGap(ev, 0);
    }

    private long EventGap(SpaceEventRules ev, int turn) => (long)SystemGenerator.Int(Hash(EventSalt, 100_000 + turn), ev.EveryMin, ev.EveryMax) * Simulation.TickRate;

    /// <summary>A unit vector at the hash's angle.</summary>
    private static FixVec2 Direction(ulong h)
    {
        var turns = SystemGenerator.Unit(h);
        return new FixVec2(Surface.Telemetry.CosTurns(turns), Surface.Climate.SinTurns(turns));
    }

    /// <summary>A free spot between min and max from the star (clear of bodies and of sites by spacing), by the salt's
    /// hash; failing that the last spot clear of the bodies; null when even that fails.</summary>
    private FixVec2? PlaceBy(SpaceSiteRules r, ulong salt, long slot, Fix64 min, Fix64 max, Fix64 spacing)
    {
        FixVec2? fallback = null;
        for (var t = 0; t < System.Math.Max(1, r.Tries); t++)
        {
            var radius = min + (max - min) * SystemGenerator.Unit(Hash(salt, slot + t * 2 + 1));
            var at = Direction(Hash(salt, slot + t * 2)) * radius;
            if (Clear(r, at, spacing)) return at;
            if (Clear(r, at, Fix64.Zero)) fallback = at;
        }
        return fallback;
    }

    /// <summary>A spot about distance from a centre (by the hash), clear of every body; the centre's own spot when none is.</summary>
    private FixVec2? PlaceNear(SpaceSiteRules r, ulong salt, long slot, FixVec2 centre, Fix64 distance)
    {
        for (var t = 0; t < System.Math.Max(1, r.Tries); t++)
        {
            var d = distance * (Fix64.Half + SystemGenerator.Unit(Hash(salt, slot + t * 2 + 1)));
            var at = centre + Direction(Hash(salt, slot + t * 2)) * d;
            if (Clear(r, at, Fix64.Zero)) return at;
        }
        return Clear(r, centre, Fix64.Zero) ? centre : null;
    }

    private static void SetPath(SpaceSite site, FixVec2 from, FixVec2 to, long start, long end)
    {
        site.PathFrom = from;
        site.PathTo = to;
        site.PathStart = start;
        site.PathEnd = end;
        site.Position = from;
    }

    /// <summary>Where a site is at a tick: on its path (a moving site: rogue asteroid, comet, a drifting storm), else where it
    /// is now (a site in tow or under escort follows its ship).</summary>
    public FixVec2 SitePosition(SpaceSite site, long tick)
    {
        if (site.PathStart < 0 || site.PathEnd <= site.PathStart) return site.Position;
        var span = site.PathEnd - site.PathStart;
        var done = System.Math.Clamp(tick - site.PathStart, 0, span);
        var share = Fix64.FromRaw((long)(((System.Int128)done << Fix64.FractionalBits) / span));
        return site.PathFrom + (site.PathTo - site.PathFrom) * share;
    }

    // ---------------------------------------------------------------- spawning

    /// <summary>The event sites still running (no outcome yet).</summary>
    public int LiveEvents()
    {
        var n = 0;
        foreach (var s in _sites)
            if (s.Event && s.Outcome.Length == 0) n++;
        return n;
    }

    /// <summary>An event of that kind arrives now (variant null = by the hash): placed, announced and Detected for every
    /// player. Null when the data has no such kind or nowhere to put it. A chain mission may set the number of lifepods
    /// (pods &gt; 0) and a rogue asteroid's target body (phase 4).</summary>
    internal SpaceSite? SpawnEvent(Simulation sim, SiteKind kind, string? variant, int pods = 0, string? target = null)
    {
        if (SiteRules is not { } r || r.Kind(kind) is not { } def) return null;
        var n = _eventSerial++;
        ulong Roll(int k) => Hash(EventSalt, n * 1000L + k);
        var edgeMin = r.Events?.EdgeMin ?? Fix64.FromInt(500);
        var edgeMax = r.Events?.EdgeMax ?? Fix64.FromInt(560);
        var v = variant != null && def.Variants.Any(x => x.Id == variant) ? variant
            : def.Variants.Count > 0 ? SystemGenerator.Pick(def.Variants, Roll(1)) : "";
        var size = SystemGenerator.Int(Roll(2), def.SizeMin, def.SizeMax);
        SpaceSite? site = null;
        var text = "";
        var distress = false;
        switch (kind)
        {
            case SiteKind.RogueAsteroid when def.Rogue is { } ro:
            {
                if ((target != null ? Body(target) : RogueTarget(ro, Roll(3), Roll(4))) is not { } aim) return null;
                var start = Direction(Roll(5)) * (edgeMin + (edgeMax - edgeMin) * SystemGenerator.Unit(Roll(6)));
                var ticks = (long)SystemGenerator.Int(Roll(7), ro.FlightMin, ro.FlightMax) * Simulation.TickRate;
                site = AddSite(kind, v, null, start, size, sim.Tick);
                SetPath(site, start, aim.Position, sim.Tick, sim.Tick + ticks);
                site.TargetBody = aim.Id;
                text = $"Sensor alert: {site.Name}, a rock on a collision course with {aim.Name}: impact in {Clock((int)(ticks / Simulation.TickRate))}";
                break;
            }
            case SiteKind.Comet when def.Comet is { } co:
            {
                var dir = Direction(Roll(3));
                var side = (Roll(4) & 1) == 0 ? Fix64.One : -Fix64.One;
                var normal = new FixVec2(-dir.Y, dir.X) * side;
                var pass = co.PassMin + (co.PassMax - co.PassMin) * SystemGenerator.Unit(Roll(5));
                var radius = edgeMin + (edgeMax - edgeMin) * SystemGenerator.Unit(Roll(6));
                var from = dir * radius + normal * pass;
                var to = dir * -radius + normal * pass;
                var ticks = (long)SystemGenerator.Int(Roll(7), co.CrossMin, co.CrossMax) * Simulation.TickRate;
                site = AddSite(kind, v, null, from, size, sim.Tick);
                SetPath(site, from, to, sim.Tick, sim.Tick + ticks);
                var (ws, we) = CometWindow(site, co);
                text = $"Sensor alert: {site.Name} entering the system: its tail is active in {Clock(SecondsFrom(sim.Tick, ws))} for {Clock((int)((we - ws) / Simulation.TickRate))}";
                break;
            }
            case SiteKind.Lifepods when def.Pods is { } po:
            {
                var debris = _sites.Where(s => s.Kind == SiteKind.Debris).ToList();
                FixVec2? at = debris.Count > 0
                    ? PlaceNear(r, EventSalt, n * 1000L + 100, debris[(int)(Roll(3) % (ulong)debris.Count)].Position, po.NearDebris)
                    : PlaceBy(r, EventSalt, n * 1000L + 100, r.OrbitMin, r.OrbitMax, r.Spacing);
                if (at is not { } spot) return null;
                site = AddSite(kind, v, null, spot, size, sim.Tick);
                site.PodCount = pods > 0 ? System.Math.Min(pods, 31) : SystemGenerator.Int(Roll(4), po.CountMin, po.CountMax);
                text = $"Distress call: {site.PodCount} lifepods adrift at {site.Name}, their air running out";
                distress = true;
                break;
            }
            case SiteKind.StrandedShip when def.Stranded is { } sd:
            {
                if (PlaceBy(r, EventSalt, n * 1000L + 100, r.OrbitMin, r.OrbitMax, r.Spacing) is not { } spot) return null;
                if (PickBody(Roll(3)) is not { } dest) return null;
                site = AddSite(kind, v, null, spot, size, sim.Tick);
                site.TargetBody = dest.Id;
                site.AirTick = sim.Tick + (long)SystemGenerator.Int(Roll(4), sd.AirMin, sd.AirMax) * Simulation.TickRate;
                text = $"Distress call: {site.Name}, adrift, life support failing ({Clock(SecondsFrom(sim.Tick, site.AirTick))} of air)";
                distress = true;
                break;
            }
            case SiteKind.Anomaly when def.Anomaly is { } an:
            {
                if (PlaceBy(r, EventSalt, n * 1000L + 100, r.OrbitMin, r.OrbitMax, r.Spacing) is not { } spot) return null;
                site = AddSite(kind, v, null, spot, size, sim.Tick);
                var life = (long)SystemGenerator.Int(Roll(4), an.LifeMin, an.LifeMax) * Simulation.TickRate;
                SetPath(site, spot, spot + Direction(Roll(5)) * an.Drift, sim.Tick, sim.Tick + life);
                text = $"Sensor alert: {site.Name} forming between the orbits (gone in {Clock((int)(life / Simulation.TickRate))})";
                break;
            }
        }
        if (site == null) return null;
        site.Event = true;
        Announce(site, text, distress);
        return site;
    }

    /// <summary>A rogue asteroid's target: the colony's body (colony_chance percent, when someone has landed there), else a
    /// body by the hash (never a belt).</summary>
    private BodyDef? RogueTarget(SiteRogueDef ro, ulong colonyRoll, ulong bodyRoll)
    {
        if (SurfaceBody != null && _sim?.Surface is { } s && s.Players.Any(p => p.LandingTile >= 0) && (int)(colonyRoll % 100) < ro.ColonyChance
            && Body(SurfaceBody) is { } colony)
            return colony;
        return PickBody(bodyRoll);
    }

    private BodyDef? PickBody(ulong roll)
    {
        var bodies = Bodies.Where(b => Data?.SystemGenerator is not { } g || !g.IsBelt(b.ClassId)).ToList();
        return bodies.Count == 0 ? null : bodies[(int)(roll % (ulong)bodies.Count)];
    }

    private void Announce(SpaceSite site, string text, bool distress)
    {
        AddNote(site, text, distress);
        for (var p = 0; p < _status.Count; p++)
        {
            if (_status[p] == null) continue;
            SetKnowledge(p, site.Id, SiteKnowledge.Detected);
            ShipLog(p, text, distress ? "bad" : "warn");
        }
    }

    private void AddNote(SpaceSite site, string text, bool distress)
    {
        _eventNotes.Add(new SpaceEventNote(Now, site.Id, site.Kind, text, distress));
        if (_eventNotes.Count > 20) _eventNotes.RemoveAt(0);
        EventNoteCount++;
        Version++;
    }

    private static string Clock(int seconds) => $"{System.Math.Max(0, seconds) / 60}:{System.Math.Max(0, seconds) % 60:00}";

    private static int SecondsFrom(long now, long tick) => (int)System.Math.Max(0, (tick - now + Simulation.TickRate - 1) / Simulation.TickRate);

    // ---------------------------------------------------------------- the view

    /// <summary>An event site's state for the plot and the card (null for a site that isn't an event kind).</summary>
    public SpaceEventView? EventOf(string? siteId)
    {
        if (Site(siteId) is not { } site || !IsEventKind(site.Kind)) return null;
        var (ws, we) = SiteKindOf(site)?.Comet is { } co ? CometWindow(site, co) : (-1L, -1L);
        var impact = site.Kind == SiteKind.RogueAsteroid && site.Outcome.Length == 0 ? site.PathEnd : -1;
        return new SpaceEventView(site.Id, site.Kind, site.PathFrom, site.PathTo, site.PathStart, site.PathEnd, site.TargetBody, impact, ws, we,
            site.Outcome.Length == 0 ? site.AirTick : -1, PodsOf(site.Id), site.Outcome, site.OutcomeBy, site.OutcomeTick);
    }

    /// <summary>The pods of a lifepods site, by index (empty for any other site).</summary>
    public IReadOnlyList<PodView> PodsOf(string? siteId)
    {
        var list = new List<PodView>();
        if (Site(siteId) is not { Kind: SiteKind.Lifepods } site || SiteKindOf(site)?.Pods is not { } po) return list;
        for (var k = 0; k < site.PodCount && k < 31; k++)
        {
            var state = (site.PodsTaken & (1 << k)) != 0 ? "taken" : (site.PodsLost & (1 << k)) != 0 ? "lost" : "waiting";
            list.Add(new PodView(k, PodSurvivors(site, po, k), PodAir(site, po, k), state));
        }
        return list;
    }

    /// <summary>The site the player's flagship is escorting, or null.</summary>
    public string? EscortOf(int player) => Status(player) is { Escort.Length: > 0 } st ? st.Escort : null;

    /// <summary>The player whose flagship escorts the site (-1 = none).</summary>
    public int EscorterOf(string? siteId)
    {
        if (siteId == null) return -1;
        for (var p = 0; p < _status.Count; p++)
            if (_status[p] is { } st && st.Escort.Length > 0 && st.Escort == siteId) return p;
        return -1;
    }

    /// <summary>The anomaly ring the player's flagship is recording at now ("outer", "middle", "inner"), or null.</summary>
    public string? RecordingRing(int player) =>
        Status(player)?.Work is { } w && w.Action.StartsWith("record_", System.StringComparison.Ordinal) ? w.Action[7..] : null;

    /// <summary>When the k-th pod of a lifepods site runs out of air (sim tick; -1 for any other site). No allocation: the
    /// view reads it every frame.</summary>
    public long PodAirTick(SpaceSite site, int k) => SiteKindOf(site)?.Pods is { } po && k >= 0 && k < site.PodCount ? PodAir(site, po, k) : -1;

    /// <summary>The comet's tail window in ticks ((-1, -1) for any other site). No allocation.</summary>
    public (long Start, long End) TailWindow(SpaceSite site) => SiteKindOf(site)?.Comet is { } co ? CometWindow(site, co) : (-1, -1);

    /// <summary>The player's running work: its action id ("mine", "push", "record_inner"…) and site, or null. No allocation:
    /// the view reads it every frame.</summary>
    public string? WorkActionOf(int player) => Status(player)?.Work?.Action;
    public string? WorkSiteOf(int player) => Status(player)?.Work?.Site;

    /// <summary>A comet's tail window (ticks): the window shares of its crossing.</summary>
    private static (long Start, long End) CometWindow(SpaceSite site, SiteCometDef co)
    {
        if (site.PathStart < 0) return (-1, -1);
        var span = Fix64.FromInt((int)System.Math.Min(int.MaxValue, site.PathEnd - site.PathStart));
        return (site.PathStart + (span * co.WindowFrom).FloorToInt(), site.PathStart + (span * co.WindowTo).FloorToInt());
    }

    private int PodSurvivors(SpaceSite site, SitePodsDef po, int k) => SystemGenerator.Int(SiteRoll(site, 900 + k), po.SurvivorsMin, po.SurvivorsMax);

    private long PodAir(SpaceSite site, SitePodsDef po, int k) => site.SpawnTick + (long)SystemGenerator.Int(SiteRoll(site, 950 + k), po.AirMin, po.AirMax) * Simulation.TickRate;

    private int PodsLeft(SpaceSite site)
    {
        var n = 0;
        for (var k = 0; k < site.PodCount && k < 31; k++)
            if (((site.PodsTaken | site.PodsLost) & (1 << k)) == 0) n++;
        return n;
    }

    /// <summary>A stranded ship's call is bait (by its hash): arriving springs an ambush.</summary>
    private bool IsBait(SpaceSite site) => SiteKindOf(site)?.Stranded is { } sd && (int)(SiteRoll(site, 800) % 100) < sd.Bait;

    /// <summary>Ticks of pushing a rogue asteroid needs for the player (push_seconds by its hash ÷ the mining lever).</summary>
    private long PushNeed(int player, SpaceSite site) =>
        SiteKindOf(site)?.Rogue is { } ro ? Faster((long)SystemGenerator.Int(SiteRoll(site, 1200), ro.PushMin, ro.PushMax) * Simulation.TickRate, Fit(player, FitOutRules.Mining)) : 1;

    /// <summary>The card's rows for an event site.</summary>
    private void EventRows(int player, SpaceSite site, SiteRecord r, List<SiteRow> rows)
    {
        var def = SiteKindOf(site);
        var target = Body(site.TargetBody)?.Name ?? "";
        switch (site.Kind)
        {
            case SiteKind.RogueAsteroid:
                rows.Add(new SiteRow("TARGET", target, site.TargetBody == SurfaceBody && _sim?.Surface != null ? "bad" : "warn"));
                if (site.Outcome.Length == 0)
                {
                    rows.Add(new SiteRow("IMPACT", $"in {Clock(SecondsUntil(site.PathEnd))}", "bad"));
                    rows.Add(new SiteRow("ALONGSIDE", r.Alongside ? "yes" : "no", r.Alongside ? "good" : ""));
                    if (r.PushTicks > 0) rows.Add(new SiteRow("PUSHED", $"{Percent(r.PushTicks, PushNeed(player, site))}% of what it takes", "good"));
                    else if (def?.Rogue is { } ro) rows.Add(new SiteRow("PUSH", $"{ro.PushMin}–{ro.PushMax} s alongside with the tractor, or charges (away team)"));
                }
                break;
            case SiteKind.Comet when def?.Comet is { } co:
            {
                var (ws, we) = CometWindow(site, co);
                if (site.Outcome.Length == 0)
                {
                    rows.Add(new SiteRow("TAIL", Now < ws ? $"active in {Clock(SecondsUntil(ws))}" : Now <= we ? $"active · {Clock(SecondsUntil(we))} left" : "quiet now",
                        Now >= ws && Now <= we ? "good" : "warn"));
                    rows.Add(new SiteRow("LEAVES", $"in {Clock(SecondsUntil(site.PathEnd))}"));
                }
                rows.Add(new SiteRow("SAMPLED", r.Sampled ? "yes" : $"no · {co.SampleSeconds} s at the comet while the tail is active", r.Sampled ? "good" : ""));
                break;
            }
            case SiteKind.Lifepods when def?.Pods is { } po:
            {
                var pods = PodsOf(site.Id);
                var waiting = pods.Where(p => p.State == "waiting").ToList();
                rows.Add(new SiteRow("PODS", $"{waiting.Count} waiting · {pods.Count(p => p.State == "taken")} picked up · {pods.Count(p => p.State == "lost")} lost",
                    waiting.Count > 0 ? "warn" : ""));
                if (waiting.Count > 0) rows.Add(new SiteRow("AIR", $"least: {Clock(SecondsUntil(waiting.Min(p => p.AirTick)))}", "bad"));
                if (r.Picked > 0) rows.Add(new SiteRow("RESCUED", $"{r.Picked} pod{(r.Picked == 1 ? "" : "s")} by us", "good"));
                break;
            }
            case SiteKind.StrandedShip:
                rows.Add(new SiteRow("BOUND FOR", target));
                if (site.Outcome.Length == 0)
                {
                    rows.Add(new SiteRow("AIR", site.AirTick >= 0 ? $"{Clock(SecondsUntil(site.AirTick))} left" : "power restored", site.AirTick >= 0 ? "bad" : "good"));
                    rows.Add(new SiteRow("ALONGSIDE", r.Alongside ? "yes" : "no", r.Alongside ? "good" : ""));
                    rows.Add(new SiteRow("POWER", r.Powered ? "restored" : "dark", r.Powered ? "good" : "warn"));
                    var escort = EscorterOf(site.Id);
                    if (escort == player) rows.Add(new SiteRow("ESCORT", $"following us to {target}", "good"));
                    else if (escort >= 0) rows.Add(new SiteRow("ESCORT", "following another ship", "warn"));
                }
                break;
            case SiteKind.Anomaly when def?.Anomaly is { } an:
                foreach (var ring in an.Rings)
                    rows.Add(new SiteRow(ring.Name.ToUpperInvariant(), $"+{ring.Research} research / {an.EverySeconds} s"
                        + (ring.Hull > 0 ? $" · hull −{ring.Hull}%" : "") + (ring.Knock > 0 ? $" · {ring.Knock}% a system goes down" : ""), ring.Hull > 0 ? "warn" : ""));
                rows.Add(new SiteRow("RECORDED", $"{r.Recorded} of {an.ResearchCap} research", r.Recorded > 0 ? "good" : ""));
                if (site.Event && site.Outcome.Length == 0) rows.Add(new SiteRow("DISPERSES", $"in {Clock(SecondsUntil(site.PathEnd))}"));
                break;
        }
        if (site.Outcome.Length > 0) rows.Add(new SiteRow("OUTCOME", OutcomeText(player, site), site.OutcomeBy == player ? "good" : "warn"));
    }

    private static int Percent(long done, long need) => need <= 0 ? 100 : (int)System.Math.Clamp(done * 100 / need, 0, 100);

    private string OutcomeText(int player, SpaceSite site)
    {
        var target = Body(site.TargetBody)?.Name ?? "a body";
        var who = site.OutcomeBy == player ? "by us" : site.OutcomeBy >= 0 ? "by another ship" : "";
        return site.Outcome switch
        {
            "deflected" => $"turned aside {who}".Trim(),
            "impact" => $"struck {target}",
            "gone" => site.Kind == SiteKind.Comet ? "left the system" : "dispersed",
            "cleared" => "no pods left",
            "rescued" => $"brought in to {target} {who}".Trim(),
            "trap" => "it was bait: an ambush",
            "lost" => "its crew didn't make it",
            _ => site.Outcome,
        };
    }

    // ---------------------------------------------------------------- work at event sites

    /// <summary>Whether the card offers this event action now.</summary>
    private bool OffersEvent(int player, SpaceSite site, SiteRecord r, string action)
    {
        if (site.Outcome.Length > 0) return false;
        var def = SiteKindOf(site);
        return action switch
        {
            "push" => def?.Rogue != null,
            "sample" => def?.Comet != null && !r.Sampled,
            "pickup" => def?.Pods != null && PodsLeft(site) > 0,
            "escort" => def?.Stranded != null && EscortOf(player) != site.Id,
            "record_outer" or "record_middle" or "record_inner" => def?.Anomaly?.Ring(action[7..]) != null && RecordingRing(player) != action[7..],
            _ => false,
        };
    }

    /// <summary>Why an event action can't run (the common checks are done: at the site, identified, not crippled, not
    /// busy, hazardous ok).</summary>
    private string? EventWorkProblem(int player, ShipState st, SpaceSite site, SiteRecord r, string action)
    {
        var def = SiteKindOf(site);
        if (site.Outcome.Length > 0) return $"too late: {OutcomeText(player, site)}";
        switch (action)
        {
            case "push":
                if (def?.Rogue == null) return "nothing to push";
                if (!r.Alongside) return "come alongside first";
                if (IsDown(st, ShipSystem.Tractor)) return "the tractor is down: nothing to push with";
                if (st.Tow.Length > 0) return $"the tractor is holding {SiteNameFor(player, st.Tow)} in tow: release or drop it first";
                if (st.Away is { Done: false }) return "an away team is out";
                return null;
            case "sample":
            {
                if (def?.Comet is not { } co) return "nothing to sample";
                if (r.Sampled) return "sampled already";
                var (ws, we) = CometWindow(site, co);
                if (Now < ws) return $"the tail isn't active yet (in {Clock(SecondsUntil(ws))})";
                if (Now + (long)co.SampleSeconds * Simulation.TickRate > we) return "the tail is going quiet: not enough time left to sample";
                return null;
            }
            case "pickup":
                if (def?.Pods == null) return "no pods here";
                if (PodsLeft(site) <= 0) return "no pods left";
                if (IsDown(st, ShipSystem.Tractor)) return "the tractor is down: no pods can be brought in";
                if (st.Tow.Length > 0) return $"the tractor is holding {SiteNameFor(player, st.Tow)} in tow: release or drop it first";
                if (def?.Pods is { } pods && !PodFits(player, st, site, pods)) return "no room in the hold for the next pod's survivors: set them down at a station first"; // phase 4
                return null;
            case "escort":
                if (def?.Stranded == null) return "nothing to escort";
                if (!r.Powered) return "restore its power first";
                if (st.Escort.Length > 0) return st.Escort == site.Id ? "escorting it already" : $"escorting {SiteNameFor(player, st.Escort)} already";
                if (st.Tow.Length > 0) return $"towing {SiteNameFor(player, st.Tow)}: drop or release it first";
                if (EscorterOf(site.Id) >= 0) return "another ship is escorting it";
                return null;
            case "record_outer":
            case "record_middle":
            case "record_inner":
            {
                if (def?.Anomaly is not { } an || an.Ring(action[7..]) is not { } ring) return "no such ring";
                if (r.Recorded >= an.ResearchCap) return "recorded all it has";
                if (ring.Hazardous && !HazardousOk(st)) return "the hull is critical: not the inner ring until it is repaired";
                return null;
            }
        }
        return "no such work";
    }

    private static string EventWorkText(string action) => action switch
    {
        "push" => "Pushing the rock aside",
        "sample" => "Sampling the tail",
        "pickup" => "Picking up lifepods",
        "record_outer" => "Recording (outer ring)",
        "record_middle" => "Recording (middle ring)",
        "record_inner" => "Recording (inner ring)",
        _ => action,
    };

    private string EventWorkLabel(string action, SpaceSite site) => action switch
    {
        "push" => "PUSH IT ASIDE",
        "sample" => "SAMPLE THE TAIL",
        "pickup" => "PICK UP PODS",
        "escort" => $"ESCORT TO {(Body(site.TargetBody)?.Name ?? "PORT").ToUpperInvariant()}",
        "record_outer" or "record_middle" or "record_inner" => $"RECORD · {(SiteKindOf(site)?.Anomaly?.Ring(action[7..])?.Name ?? action[7..]).ToUpperInvariant()}",
        _ => action.ToUpperInvariant(),
    };

    /// <summary>Starts an event action (its problem is checked).</summary>
    private void StartEventWork(Simulation sim, int player, ShipState st, SpaceSite site, SiteRecord r, string action)
    {
        var def = SiteKindOf(site)!;
        switch (action)
        {
            case "push":
            {
                var need = PushNeed(player, site);
                st.Work = new WorkRun { Site = site.Id, Action = "push", Start = sim.Tick, End = sim.Tick + System.Math.Max(1L, need - r.PushTicks) };
                ShipLog(player, $"Pushing {site.Name} aside with the tractor", "");
                return;
            }
            case "sample":
                st.Work = new WorkRun { Site = site.Id, Action = "sample", Start = sim.Tick, End = sim.Tick + (long)def.Comet!.SampleSeconds * Simulation.TickRate };
                ShipLog(player, $"Into the tail of {site.Name}: sampling", "warn");
                return;
            case "pickup":
                st.Work = new WorkRun { Site = site.Id, Action = "pickup", Start = sim.Tick, End = sim.Tick + (long)def.Pods!.PickupSeconds * Simulation.TickRate };
                return;
            case "escort":
                st.Escort = site.Id;
                st.EscortTick = sim.Tick;
                var f = def.Stranded!.EscortFlight;
                ShipLog(player, $"Escorting {site.Name} to {Body(site.TargetBody)?.Name ?? "port"}: flights {((f - Fix64.One) * Fix64.FromInt(100)).FloorToInt()}% slower", "good");
                return;
            default: // record_<ring>
                st.Work = new WorkRun { Site = site.Id, Action = action, Start = sim.Tick, End = sim.Tick };
                ShipLog(player, $"{EventWorkText(action)} at {site.Name}", action == "record_inner" ? "warn" : "");
                return;
        }
    }

    /// <summary>One tick of an event action; false when the action isn't one (the caller carries on).</summary>
    private bool StepEventWork(Simulation sim, int player, ShipState st, SpaceSite site, SiteRecord r, WorkRun w)
    {
        if (w.Action is not ("push" or "sample" or "pickup" or "record_outer" or "record_middle" or "record_inner")) return false;
        if (site.Outcome.Length > 0)
        {
            st.Work = null;
            Version++;
            return true;
        }
        var def = SiteKindOf(site);
        switch (w.Action)
        {
            case "push":
                if (IsDown(st, ShipSystem.Tractor))
                {
                    st.Work = null;
                    Version++;
                    return true;
                }
                r.PushTicks++;
                if (r.PushTicks >= PushNeed(player, site))
                {
                    st.Work = null;
                    Deflect(sim, player, site, "pushed aside with the tractor");
                }
                return true;
            case "sample":
            {
                if (def?.Comet is not { } co) return true;
                var (_, we) = CometWindow(site, co);
                if (sim.Tick > we)
                {
                    st.Work = null;
                    ShipLog(player, $"Sampling {site.Name} stopped: the tail went quiet", "warn");
                    Version++;
                    return true;
                }
                var every = (long)co.HullEverySeconds * Simulation.TickRate;
                if (co.Hull > 0 && every > 0 && sim.Tick > w.Start && (sim.Tick - w.Start) % every == 0)
                {
                    Damage(sim, player, Fix64.FromInt(co.Hull), $"dust in the tail of {site.Name}", quiet: true);
                    if (st.Work == null) return true; // crippled by it
                }
                if (sim.Tick < w.End) return true;
                st.Work = null;
                FinishSample(sim, player, st, site, r, co);
                return true;
            }
            case "pickup":
                if (def?.Pods is not { } po) return true;
                if (IsDown(st, ShipSystem.Tractor))
                {
                    st.Work = null;
                    Version++;
                    return true;
                }
                if (sim.Tick < w.End) return true;
                st.Work = null;
                if (!PodFits(player, st, site, po)) // phase 4: a mission's pods need hold room
                {
                    ShipLog(player, $"Pick-up at {site.Name} stopped: no room in the hold for the next pod's survivors", "warn");
                    Version++;
                    return true;
                }
                PickUpPod(sim, player, site, r, po);
                if (site.Outcome.Length == 0 && PodsLeft(site) > 0)
                    st.Work = new WorkRun { Site = site.Id, Action = "pickup", Start = sim.Tick, End = sim.Tick + (long)po.PickupSeconds * Simulation.TickRate };
                return true;
            default:
                StepRecording(sim, player, st, site, r, w);
                return true;
        }
    }

    private void FinishSample(Simulation sim, int player, ShipState st, SpaceSite site, SiteRecord r, SiteCometDef co)
    {
        r.Sampled = true;
        Version++;
        var research = GiveSpaceResearch(sim, player, Fix64.FromInt(SystemGenerator.Int(SiteRoll(site, 1100), co.ResearchMin, co.ResearchMax)));
        var containers = SystemGenerator.Int(SiteRoll(site, 1101), co.IceMin, co.IceMax);
        var item = OreItem("deuterium");
        var got = 0;
        for (var k = 0; k < containers && SiteRules?.Ore("deuterium") != null; k++)
        {
            if (!Fits(st, item, OrePerContainer)) break;
            Add(st, item, OrePerContainer);
            AddTally(st, "mined:" + item, OrePerContainer);
            got++;
        }
        var parts = new List<string>();
        if (research.Length > 0) parts.Add(research);
        if (got > 0) parts.Add($"{got * OrePerContainer} deuterium (ice) aboard");
        if (got < containers) parts.Add($"{(containers - got) * OrePerContainer} ice left behind: the hold is full");
        ShipLog(player, $"Tail of {site.Name} sampled{(parts.Count > 0 ? ": " + string.Join(", ", parts) : "")}", "good");
        SpendFor(player, site);
    }

    private void PickUpPod(Simulation sim, int player, SpaceSite site, SiteRecord r, SitePodsDef po)
    {
        var best = -1;
        long bestAir = long.MaxValue;
        for (var k = 0; k < site.PodCount && k < 31; k++)
        {
            if (((site.PodsTaken | site.PodsLost) & (1 << k)) != 0) continue;
            var air = PodAir(site, po, k);
            if (air < bestAir)
            {
                bestAir = air;
                best = k;
            }
        }
        if (best < 0) return;
        site.PodsTaken |= 1 << best;
        r.Picked++;
        Version++;
        var n = PodSurvivors(site, po, best);
        var divisions = DivisionIds();
        for (var j = 0; j < n && divisions.Count > 0; j++)
            TakeSurvivor(sim, player, divisions[(int)(SiteRoll(site, 1000 + best * 8 + j) % (ulong)divisions.Count)], site.Name);
        ShipLog(player, $"Lifepod {best + 1} of {site.Name} aboard: {n} survivor{(n == 1 ? "" : "s")} ({PodsLeft(site)} pods left)", "good");
        if (PodsLeft(site) == 0) EndEvent(site, "cleared", -1, $"Every lifepod at {site.Name} is accounted for", "good");
    }

    /// <summary>One tick of recording at an anomaly's ring: every every_seconds the ring's research (up to the cap), its hull
    /// wear and its chance (sim.Rng) of knocking a system out.</summary>
    private void StepRecording(Simulation sim, int player, ShipState st, SpaceSite site, SiteRecord r, WorkRun w)
    {
        if (SiteKindOf(site)?.Anomaly is not { } an || an.Ring(w.Action[7..]) is not { } ring)
        {
            st.Work = null;
            return;
        }
        var every = (long)an.EverySeconds * Simulation.TickRate;
        if (every <= 0 || sim.Tick <= w.Start || (sim.Tick - w.Start) % every != 0) return;
        var gain = System.Math.Min(ring.Research, an.ResearchCap - r.Recorded);
        if (gain > 0)
        {
            r.Recorded += gain;
            GiveSpaceResearch(sim, player, Fix64.FromInt(gain));
            Version++;
        }
        if (ring.Hull > 0)
        {
            Damage(sim, player, Fix64.FromInt(ring.Hull), $"the {ring.Name.ToLowerInvariant()} of {site.Name}", quiet: true);
            if (st.Work == null) return; // crippled
        }
        if (ring.Knock > 0 && an.Systems.Count > 0 && sim.Rng.NextInt(100) < ring.Knock && StatusRules is { } rules)
        {
            var system = GameData.ShipSystemOf(an.Systems[sim.Rng.NextInt(an.Systems.Count)]);
            if (system is { } sys) KnockOut(sim, player, sys, sim.Rng.NextInt(rules.DownMin, rules.DownMax + 1), $"a discharge in {site.Name}");
        }
        if (r.Recorded >= an.ResearchCap)
        {
            st.Work = null;
            ShipLog(player, $"{site.Name} recorded: {r.Recorded} research in all", "good");
            SpendFor(player, site);
        }
    }

    /// <summary>A rogue asteroid turned aside by the player: it veers out of the system, the player gets the reward, a living
    /// civilization it was heading for never knew how close it came.</summary>
    private void Deflect(Simulation sim, int player, SpaceSite site, string how)
    {
        if (SiteKindOf(site)?.Rogue is not { } ro || site.Outcome.Length > 0) return;
        var pos = SitePosition(site, sim.Tick);
        var outward = pos.Length() > Fix64.Zero ? pos.Normalized() : new FixVec2(Fix64.One, Fix64.Zero);
        var r = RecordFor(player, site.Id);
        r.Deflected = true;
        var latinum = GiveLatinum(sim, player, SystemGenerator.Int(SiteRoll(site, 700), ro.LatinumMin, ro.LatinumMax));
        var research = GiveSpaceResearch(sim, player, Fix64.FromInt(SystemGenerator.Int(SiteRoll(site, 701), ro.ResearchMin, ro.ResearchMax)));
        if (CivAt(site.TargetBody) is { Living: true } && ro.CivAwareness != 0) ShiftAwareness(site.TargetBody, ro.CivAwareness);
        EndEvent(site, "deflected", player, $"{site.Name} turned aside before it reached {Body(site.TargetBody)?.Name ?? "its target"}", "good");
        SetPath(site, pos, pos + outward * ro.Veer, sim.Tick, sim.Tick + (long)ro.VeerSeconds * Simulation.TickRate);
        site.ExpireTick = site.PathEnd;
        ShipLog(player, $"We turned {site.Name} ({how}): {latinum} latinum{(research.Length > 0 ? $", {research}" : "")}", "good");
    }

    /// <summary>A rogue asteroid reaches its body: on the colony's body the surface takes a bounded hit (nothing destroyed);
    /// a living civilization's world notices; elsewhere a log line.</summary>
    private void Impact(Simulation sim, SpaceSite site)
    {
        var body = Body(site.TargetBody);
        site.Position = body?.Position ?? site.Position;
        site.PathStart = site.PathEnd = -1;
        var name = body?.Name ?? "a body";
        var text = $"{site.Name} struck {name}";
        if (site.TargetBody == SurfaceBody && sim.Surface is { } s && SiteKindOf(site)?.Rogue is { } ro)
            text += s.RogueImpact(sim, ro.Impact, site.Seed) is { } hit ? $": {hit}" : ", far from any colony";
        if (CivAt(site.TargetBody) is { Living: true } && SiteKindOf(site)?.Rogue is { CivStrikeAwareness: not 0 } r2) ShiftAwareness(site.TargetBody, r2.CivStrikeAwareness);
        EndEvent(site, "impact", -1, text, "bad");
    }

    /// <summary>A stranded ship brought into orbit of its destination by the player.</summary>
    private void Rescue(Simulation sim, int player, SpaceSite site)
    {
        if (SiteKindOf(site)?.Stranded is not { } sd) return;
        var st = Status(player)!;
        st.Escort = "";
        st.EscortTick = -1;
        RecordFor(player, site.Id).Rescued = true;
        var latinum = GiveLatinum(sim, player, SystemGenerator.Int(SiteRoll(site, 710), sd.LatinumMin, sd.LatinumMax));
        var research = GiveSpaceResearch(sim, player, Fix64.FromInt(SystemGenerator.Int(SiteRoll(site, 711), sd.ResearchMin, sd.ResearchMax)));
        EndEvent(site, "rescued", player, $"{site.Name} brought safe into orbit of {Body(site.TargetBody)?.Name ?? "port"}", "good");
        site.ExpireTick = sim.Tick;
        ShipLog(player, $"{site.Name}'s crew thank us: {latinum} latinum{(research.Length > 0 ? $", {research}" : "")}", "good");
    }

    /// <summary>A bait call: the ship arriving at it is ambushed (hull damage, a system knocked out; sim.Rng) and the site is spent.</summary>
    private void SpringAmbush(Simulation sim, int player, SpaceSite site)
    {
        if (SiteKindOf(site)?.Stranded is not { } sd || StatusRules is not { } rules) return;
        EndEvent(site, "trap", -1, $"{site.Name} was bait: an ambush", "bad");
        Damage(sim, player, Fix64.FromInt(sim.Rng.NextInt(sd.AmbushHullMin, sd.AmbushHullMax + 1)), $"an ambush at {site.Name}");
        if (sd.AmbushSystems.Count > 0 && GameData.ShipSystemOf(sd.AmbushSystems[sim.Rng.NextInt(sd.AmbushSystems.Count)]) is { } system)
            KnockOut(sim, player, system, sim.Rng.NextInt(rules.DownMin, rules.DownMax + 1), "the ambush");
    }

    /// <summary>A stranded ship's power is back: its air is no longer running out.</summary>
    private void EventPowered(SpaceSite site)
    {
        if (site.Kind != SiteKind.StrandedShip || site.AirTick < 0) return;
        site.AirTick = -1;
        Version++;
    }

    /// <summary>An event ends for everyone: its outcome, every player who knows it has it worked out (their work there
    /// stops, an escort of it ends), a log line each, and it drifts off spent_seconds later (unless set otherwise).</summary>
    private void EndEvent(SpaceSite site, string outcome, int by, string text, string tone)
    {
        site.Outcome = outcome;
        site.OutcomeBy = by;
        site.OutcomeTick = Now;
        if (site.ExpireTick < 0) site.ExpireTick = Now + (long)(SiteRules?.SpentSeconds ?? 300) * Simulation.TickRate;
        AddNote(site, text, false);
        for (var p = 0; p < _status.Count; p++)
        {
            if (_status[p] is not { } st || Record(p, site.Id) is not { } r || r.Knowledge < SiteKnowledge.Detected) continue;
            if (r.Knowledge != SiteKnowledge.Spent)
            {
                r.Knowledge = SiteKnowledge.Spent;
                r.SpentTick = Now;
            }
            if (st.Work is { } w && w.Site == site.Id) st.Work = null;
            if (st.Away is { Done: false } away && away.Site == site.Id && _sim != null) RecallAwayTeam(_sim, p, text.ToLowerInvariant());
            if (st.Escort == site.Id)
            {
                st.Escort = "";
                st.EscortTick = -1;
            }
            ShipLog(p, text, p == by ? "good" : tone);
        }
        Version++;
    }

    /// <summary>One player has worked an event site out (sampled, recorded): Spent for them only (the site stays for the others).</summary>
    private void SpendFor(int player, SpaceSite site)
    {
        var r = RecordFor(player, site.Id);
        if (r.Knowledge == SiteKnowledge.Spent) return;
        r.Knowledge = SiteKnowledge.Spent;
        r.SpentTick = Now;
        Version++;
    }

    /// <summary>A civilization's awareness moves (saved quietly: down; struck: up), clamped 0..100.</summary>
    private void ShiftAwareness(string bodyId, int amount)
    {
        for (var i = 0; i < _civs.Count && i < _awareness.Count; i++)
        {
            if (_civs[i].BodyId != bodyId || !_civs[i].Living) continue;
            if (amount > 0) RaiseAwareness(i, Fix64.FromInt(amount));
            else
            {
                _awareness[i] = Fix64.Max(Fix64.Zero, _awareness[i] + Fix64.FromInt(amount));
                Version++;
            }
        }
    }

    // ---------------------------------------------------------------- ticks

    /// <summary>Each tick (after the tows): the event clock, moving sites, rogue impacts, comets leaving, pods' and stranded
    /// ships' air, storms dispersing, escorts following their ships and arriving.</summary>
    private void StepSpaceEvents(Simulation sim)
    {
        var tick = sim.Tick;
        if (_nextEventTick >= 0 && tick >= _nextEventTick && SiteRules?.Events is { } ev)
        {
            _eventTurn++;
            if (LiveEvents() < ev.MaxLive && ev.Weights.Count > 0 && SpaceSiteRules.KindOf(SystemGenerator.Pick(ev.Weights, Hash(EventSalt, 200_000 + _eventTurn))) is { } kind)
                SpawnEvent(sim, kind, null);
            _nextEventTick += System.Math.Max(Simulation.TickRate, EventGap(ev, _eventTurn));
            Version++;
        }
        for (var i = 0; i < _sites.Count; i++)
        {
            var site = _sites[i];
            if (site.PathStart >= 0) site.Position = SitePosition(site, tick);
            if (!IsEventKind(site.Kind) || site.Outcome.Length > 0) continue;
            switch (site.Kind)
            {
                case SiteKind.RogueAsteroid when tick >= site.PathEnd && site.PathEnd >= 0:
                    Impact(sim, site);
                    break;
                case SiteKind.Comet when tick >= site.PathEnd && site.PathEnd >= 0:
                    EndEvent(site, "gone", -1, $"{site.Name} has left the system", "");
                    site.ExpireTick = tick;
                    break;
                case SiteKind.Anomaly when site.Event && tick >= site.PathEnd && site.PathEnd >= 0:
                    EndEvent(site, "gone", -1, $"{site.Name} has dispersed", "");
                    site.ExpireTick = tick;
                    break;
                case SiteKind.StrandedShip when site.AirTick >= 0 && tick >= site.AirTick:
                    EndEvent(site, "lost", -1, $"{site.Name}'s life support failed: no one aboard made it", "bad");
                    break;
                case SiteKind.Lifepods when SiteKindOf(site)?.Pods is { } po:
                    for (var k = 0; k < site.PodCount && k < 31; k++)
                    {
                        if (((site.PodsTaken | site.PodsLost) & (1 << k)) != 0 || tick < PodAir(site, po, k)) continue;
                        site.PodsLost |= 1 << k;
                        Version++;
                        var n = PodSurvivors(site, po, k);
                        for (var p = 0; p < _status.Count; p++)
                            if (Record(p, site.Id) is { Knowledge: >= SiteKnowledge.Detected })
                                ShipLog(p, $"Lifepod {k + 1} at {site.Name} ran out of air: {n} lost", "bad");
                    }
                    if (PodsLeft(site) == 0) EndEvent(site, "cleared", -1, $"No lifepods left at {site.Name}", "warn");
                    break;
            }
        }
        for (var p = 0; p < _status.Count; p++)
        {
            if (_status[p] is not { Escort.Length: > 0 } st) continue;
            if (Site(st.Escort) is not { } site || FlagshipOf(p) is not { } ship || site.Outcome.Length > 0)
            {
                st.Escort = "";
                st.EscortTick = -1;
                Version++;
                continue;
            }
            site.Position = ShipPosition(ship, tick);
            if (ship.AtBody != null && ship.AtBody == site.TargetBody) Rescue(sim, p, site);
        }
    }

    /// <summary>The escort ends where the ship is (the stranded ship stays there; it can be escorted again).</summary>
    internal void StopEscort(int player, string why)
    {
        if (Status(player) is not { Escort.Length: > 0 } st) return;
        var name = Site(st.Escort)?.Name ?? "the ship";
        st.Escort = "";
        st.EscortTick = -1;
        Version++;
        ShipLog(player, $"{name} no longer under escort: {why}", "warn");
    }

    /// <summary>Why flying to a moving event site is pointless: it will have left the system (a comet, a storm) or struck
    /// (a rock still on course) before the ship gets there; null otherwise.</summary>
    private string? EventCourseProblem(int player, SystemShip ship, SpaceSite site)
    {
        if (!IsEventKind(site.Kind) || site.Outcome.Length > 0 || site.PathEnd < 0 || !site.Event) return null;
        var arrive = Now + TravelTicksFor(ship, ShipPosition(ship, Now), SitePosition(site, Now));
        if (arrive < site.PathEnd) return null;
        return site.Kind == SiteKind.RogueAsteroid ? $"{SiteNameFor(player, site.Id)} would strike before we got there"
            : $"{SiteNameFor(player, site.Id)} will be gone before we get there";
    }

    /// <summary>A ship arriving at an event site: a stranded ship's bait springs.</summary>
    private void ArriveAtEvent(Simulation sim, SystemShip ship, SpaceSite site)
    {
        if (site.Kind == SiteKind.StrandedShip && site.Outcome.Length == 0 && ship.Owner >= 0 && FlagshipOf(ship.Owner) == ship && IsBait(site))
            SpringAmbush(sim, ship.Owner, site);
    }

    // ---------------------------------------------------------------- missions

    /// <summary>Whether a mission's space_site state can still come true for the player at that site; null = it can, else
    /// why not (the job then fails). Only the event states can be lost.</summary>
    internal string? SiteStateLost(int player, string siteId, string state, int count)
    {
        var r = Record(player, siteId);
        var site = Site(siteId);
        switch (state)
        {
            case "deflected":
                if (r is { Deflected: true }) return null;
                if (site == null) return "the rock is gone";
                return site.Outcome switch { "" => null, "impact" => $"{site.Name} struck {Body(site.TargetBody)?.Name ?? "its target"}", _ => "another ship turned it" };
            case "sampled":
            {
                if (r is { Sampled: true }) return null;
                if (site == null || site.Outcome.Length > 0) return "the comet has gone";
                if (SiteKindOf(site)?.Comet is { } co && Now + (long)co.SampleSeconds * Simulation.TickRate > CometWindow(site, co).End && Status(player)?.Work is not { Action: "sample" } )
                    return "the comet's tail has gone quiet";
                return null;
            }
            case "picked":
            {
                var picked = r?.Picked ?? 0;
                if (picked >= count) return null;
                var left = site == null ? 0 : PodsLeft(site);
                return picked + left < count ? $"not enough lifepods left ({picked} picked up, {left} waiting)" : null;
            }
            case "escorted":
                if (r is { Rescued: true }) return null;
                if (site == null) return "the ship is gone";
                return site.Outcome switch
                {
                    "" => null,
                    "trap" => "it was a trap",
                    "lost" => "its crew didn't make it",
                    _ => "another ship brought it in",
                };
            case "recorded":
                if ((r?.Recorded ?? 0) >= count) return null;
                if (site == null || site.Outcome.Length > 0) return "the storm has gone";
                return null;
            case "delivered": // phase 4: a mission's tow, before it is handed over
                if (r is { Delivered.Length: > 0 }) return null;
                if (r is { ReactorDone: true }) return "her reactor failed before we got her there";
                if (site == null) return "she is gone";
                if (r is { Towed: true } || (TowerOf(siteId) is var tower and >= 0 && tower != player)) return "someone else took her in";
                return null;
        }
        return null;
    }

    // ---------------------------------------------------------------- hash

    private void AddEventsToHash(ref StateHasher h)
    {
        if (_nextEventTick < 0 && _eventSerial == 0 && _eventNotes.Count == 0) return;
        h.Add("space_events");
        h.Add(_nextEventTick);
        h.Add(_eventTurn);
        h.Add(_eventSerial);
        h.Add(EventNoteCount);
        h.Add(_eventNotes.Count);
        foreach (var n in _eventNotes)
        {
            h.Add(n.Tick);
            h.Add(n.SiteId);
            h.Add((int)n.Kind);
            h.Add(n.Text);
            h.Add(n.Distress);
        }
    }
}
