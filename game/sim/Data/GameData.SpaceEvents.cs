using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Data;

// Space jobs, phase 3 (docs/space-jobs-contract.md "Phase 3"; data maps/events.json): the checks of the events, the event
// kinds' rules and the anomalies rolled with a system (read with the rest of space_sites in GameData.SpaceJobs.cs).
public sealed partial class GameData
{
    /// <summary>The site kinds that arrive as events (and the one that is also rolled with the system: anomaly).</summary>
    public static readonly IReadOnlyList<SiteKind> EventKinds = new[] { SiteKind.RogueAsteroid, SiteKind.Comet, SiteKind.Lifepods, SiteKind.StrandedShip, SiteKind.Anomaly };

    private void ValidateSpaceEvents(SpaceSiteRules r, List<string> problems)
    {
        const string w = "Space events";
        static bool Range(int min, int max, int lo, int hi) => min >= lo && max >= min && max <= hi;
        foreach (var k in r.Kinds)
        {
            var kw = $"Space sites kind {k.Id}";
            var needs = k.Kind switch
            {
                SiteKind.RogueAsteroid => k.Rogue == null ? "rogue" : null,
                SiteKind.Comet => k.Comet == null ? "comet" : null,
                SiteKind.Lifepods => k.Pods == null ? "pods" : null,
                SiteKind.StrandedShip => k.Stranded == null ? "stranded" : null,
                SiteKind.Anomaly => k.Anomaly == null ? "anomaly" : null,
                _ => null,
            };
            if (needs != null) problems.Add($"{kw}: needs its {needs} rules.");
            if (EventKinds.Contains(k.Kind) && (k.Mining != null || k.Tow != null || k.Sweep != null || k.Logs != null || k.Reactor != null))
                problems.Add($"{kw}: an event kind can't be mined, towed, swept, read or have a reactor.");
            if (k.Rogue is { } ro)
            {
                if (k.Kind != SiteKind.RogueAsteroid) problems.Add($"{kw}: rogue rules belong to rogue_asteroid.");
                if (!Range(ro.FlightMin, ro.FlightMax, 30, 3600)) problems.Add($"{kw}: rogue flight_seconds needs 30 <= min <= max <= 3600.");
                if (ro.ColonyChance < 0 || ro.ColonyChance > 100) problems.Add($"{kw}: rogue colony_chance must be 0..100.");
                if (!Range(ro.PushMin, ro.PushMax, 1, 3600)) problems.Add($"{kw}: rogue push_seconds needs 1 <= min <= max <= 3600.");
                if (ro.Veer <= Fix64.Zero || ro.Veer > Fix64.FromInt(5000) || ro.VeerSeconds < 1 || ro.VeerSeconds > 3600) problems.Add($"{kw}: rogue veer 0..5000 (above 0) and veer_seconds 1..3600.");
                if (!Range(ro.LatinumMin, ro.LatinumMax, 0, 100000) || !Range(ro.ResearchMin, ro.ResearchMax, 0, 100000)) problems.Add($"{kw}: rogue reward needs 0 <= min <= max (latinum, research).");
                if (ro.CivAwareness < -100 || ro.CivAwareness > 100 || ro.CivStrikeAwareness < -100 || ro.CivStrikeAwareness > 100) problems.Add($"{kw}: rogue civ_awareness and civ_strike_awareness must be -100..100.");
                var im = ro.Impact;
                if (im.Spread < 0 || im.Spread > 20 || im.Radius < 0 || im.Radius > 10 || !Range(im.DamageMin, im.DamageMax, 0, 99) || im.MaxBuildings < 0 || im.MaxBuildings > 50
                    || im.MaxVehicles < 0 || im.MaxVehicles > 50)
                    problems.Add($"{kw}: rogue impact needs spread 0..20, radius 0..10, 0 <= damage min <= max <= 99, max_buildings and max_vehicles 0..50.");
                if (k.Alongside == null) problems.Add($"{kw}: pushing a rogue asteroid needs alongside.");
                if (k.AnyAway && ShipStatus?.Tables.FirstOrDefault(t => t.Id == k.AwayTable) is { } table && table.Outcomes.All(o => !o.Deflect))
                    problems.Add($"{kw}: away table '{k.AwayTable}' has no outcome that turns the rock (deflect).");
            }
            if (k.Comet is { } co)
            {
                if (k.Kind != SiteKind.Comet) problems.Add($"{kw}: comet rules belong to comet.");
                if (!Range(co.CrossMin, co.CrossMax, 60, 7200)) problems.Add($"{kw}: comet cross_seconds needs 60 <= min <= max <= 7200.");
                if (co.PassMin < Fix64.Zero || co.PassMax < co.PassMin || co.PassMax > Fix64.FromInt(2000)) problems.Add($"{kw}: comet pass needs 0 <= min <= max <= 2000.");
                if (co.WindowFrom < Fix64.Zero || co.WindowTo <= co.WindowFrom || co.WindowTo > Fix64.One) problems.Add($"{kw}: comet window needs 0 <= from < to <= 1.");
                if (co.SampleSeconds < 1 || co.SampleSeconds > 600 || co.Hull < 0 || co.Hull > 100 || co.HullEverySeconds < 1 || co.HullEverySeconds > 600)
                    problems.Add($"{kw}: comet sample_seconds 1..600, hull 0..100, hull_every_seconds 1..600.");
                if (!Range(co.ResearchMin, co.ResearchMax, 0, 100000) || !Range(co.IceMin, co.IceMax, 0, 20)) problems.Add($"{kw}: comet research 0 <= min <= max, ice 0 <= min <= max <= 20.");
                if (co.IceMax > 0 && r.Ore("deuterium") == null) problems.Add($"{kw}: comet ice needs the ore kind 'deuterium'.");
                if (r.Events is { } ev && Fix64.FromInt(co.CrossMin) * (co.WindowTo - co.WindowFrom) < Fix64.FromInt(co.SampleSeconds))
                    problems.Add($"{kw}: the comet's window must be longer than sample_seconds.");
            }
            if (k.Pods is { } po)
            {
                if (k.Kind != SiteKind.Lifepods) problems.Add($"{kw}: pods rules belong to lifepods.");
                if (!Range(po.CountMin, po.CountMax, 1, 30)) problems.Add($"{kw}: pods count needs 1 <= min <= max <= 30.");
                if (!Range(po.SurvivorsMin, po.SurvivorsMax, 0, 20)) problems.Add($"{kw}: pods survivors needs 0 <= min <= max <= 20.");
                if (!Range(po.AirMin, po.AirMax, 10, 36000)) problems.Add($"{kw}: pods air_seconds needs 10 <= min <= max <= 36000.");
                if (po.PickupSeconds < 1 || po.PickupSeconds > 600 || po.NearDebris < Fix64.Zero || po.NearDebris > Fix64.FromInt(1000)) problems.Add($"{kw}: pods pickup_seconds 1..600 and near_debris 0..1000.");
            }
            if (k.Stranded is { } sd)
            {
                if (k.Kind != SiteKind.StrandedShip) problems.Add($"{kw}: stranded rules belong to stranded_ship.");
                if (!Range(sd.AirMin, sd.AirMax, 10, 36000)) problems.Add($"{kw}: stranded air_seconds needs 10 <= min <= max <= 36000.");
                if (sd.EscortFlight < Fix64.One || sd.EscortFlight > Fix64.FromInt(10)) problems.Add($"{kw}: stranded escort_flight must be 1..10.");
                if (sd.Bait < 0 || sd.Bait > 100 || !Range(sd.AmbushHullMin, sd.AmbushHullMax, 0, 100)) problems.Add($"{kw}: stranded bait 0..100 and 0 <= ambush_hull min <= max <= 100.");
                foreach (var x in sd.AmbushSystems)
                    if (ShipSystemOf(x) == null) problems.Add($"{kw}: stranded ambush_systems: unknown system '{x}'.");
                if (!Range(sd.LatinumMin, sd.LatinumMax, 0, 100000) || !Range(sd.ResearchMin, sd.ResearchMax, 0, 100000)) problems.Add($"{kw}: stranded reward needs 0 <= min <= max (latinum, research).");
                if (k.Alongside == null || k.RestorePower == null) problems.Add($"{kw}: a stranded ship needs alongside and restore_power.");
            }
            if (k.Anomaly is { } an)
            {
                if (k.Kind != SiteKind.Anomaly) problems.Add($"{kw}: anomaly rules belong to anomaly.");
                if (an.EverySeconds < 1 || an.EverySeconds > 600 || an.ResearchCap < 1 || an.ResearchCap > 100000) problems.Add($"{kw}: anomaly every_seconds 1..600 and research_cap 1..100000.");
                if (!Range(an.LifeMin, an.LifeMax, 60, 36000) || an.Drift < Fix64.Zero || an.Drift > Fix64.FromInt(2000)) problems.Add($"{kw}: anomaly life_seconds 60 <= min <= max <= 36000 and drift 0..2000.");
                foreach (var x in an.Systems)
                    if (ShipSystemOf(x) == null) problems.Add($"{kw}: anomaly systems: unknown system '{x}'.");
                var ids = new HashSet<string>(System.StringComparer.Ordinal);
                if (an.Rings.Count < 1 || an.Rings.Count > 3) problems.Add($"{kw}: anomaly needs 1 to 3 rings (outer, middle, inner).");
                foreach (var ring in an.Rings)
                {
                    if (ring.Id is not ("outer" or "middle" or "inner") || !ids.Add(ring.Id) || ring.Name.Length == 0) problems.Add($"{kw}: anomaly ring '{ring.Id}' must be one of outer / middle / inner, once, with a name.");
                    if (ring.Radius <= Fix64.Zero || ring.Radius > Fix64.FromInt(500) || ring.Research < 0 || ring.Research > 10000 || ring.Hull < 0 || ring.Hull > 100 || ring.Knock < 0 || ring.Knock > 100)
                        problems.Add($"{kw}: anomaly ring {ring.Id}: radius 0..500 (above 0), research 0..10000, hull and knock 0..100.");
                    if (ring.Knock > 0 && an.Systems.Count == 0) problems.Add($"{kw}: anomaly ring {ring.Id} knocks systems out but systems is empty.");
                }
            }
        }
        if (r.Events is { } e)
        {
            if (e.CalmSeconds < 0 || e.CalmSeconds > 36000) problems.Add($"{w}: calm_seconds must be 0..36000.");
            if (!Range(e.EveryMin, e.EveryMax, 10, 36000)) problems.Add($"{w}: every_seconds needs 10 <= min <= max <= 36000.");
            if (e.MaxLive < 1 || e.MaxLive > 20) problems.Add($"{w}: max_live must be 1..20.");
            if (e.EdgeMin <= Fix64.Zero || e.EdgeMax < e.EdgeMin || e.EdgeMax > Fix64.FromInt(5000)) problems.Add($"{w}: edge needs 0 < min <= max <= 5000.");
            if (e.Weights.Count == 0 || e.Weights.Sum(x => System.Math.Max(0, x.Weight)) <= 0) problems.Add($"{w}: weights need a positive sum.");
            foreach (var x in e.Weights)
            {
                if (x.Weight < 0) problems.Add($"{w}: weight for {x.Id} can't be negative.");
                if (SpaceSiteRules.KindOf(x.Id) is not { } kind || !EventKinds.Contains(kind)) problems.Add($"{w}: '{x.Id}' is not an event kind ({string.Join(", ", EventKinds.Select(SpaceSiteRules.IdOf))}).");
                else if (r.Kind(kind) == null) problems.Add($"{w}: weight for {x.Id}, but space_sites has no {x.Id} kind.");
            }
        }
        if (r.Anomalies is { } ag)
        {
            if (ag.CountMin < 0 || ag.CountMax < ag.CountMin || ag.CountMax > 10) problems.Add($"{w}: generation anomalies count needs 0 <= min <= max <= 10.");
            if (ag.Drift < Fix64.Zero || ag.Drift > Fix64.FromInt(2000) || ag.DriftSeconds < 1 || ag.DriftSeconds > 36000) problems.Add($"{w}: generation anomalies drift 0..2000 and drift_seconds 1..36000.");
            if (ag.CountMax > 0 && r.Kind(SiteKind.Anomaly) == null) problems.Add($"{w}: generation anomalies needs an anomaly kind.");
        }
        foreach (var x in r.Weights)
            if (SpaceSiteRules.KindOf(x.Id) is { } k && EventKinds.Contains(k)) problems.Add($"Space sites: {x.Id} arrives as an event (or generation.anomalies), not by generation weight.");
    }
}
