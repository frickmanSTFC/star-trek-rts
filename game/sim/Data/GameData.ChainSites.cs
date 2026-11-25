using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Data;

// Space jobs, phase 4 (docs/space-jobs-contract.md "Phase 4"; data maps/sites.json hidden_ship and alien_probe): the checks of
// the chain-only kinds, which only a mission places (place_space_site): never rolled with the system, never an event.
public sealed partial class GameData
{
    /// <summary>The site kinds only a chain mission places (never rolled with the system or by the event clock).</summary>
    public static readonly IReadOnlyList<SiteKind> ChainOnlyKinds = new[] { SiteKind.HiddenShip, SiteKind.AlienProbe };

    private void ValidateChainSites(SpaceSiteRules r, List<string> problems)
    {
        static bool Range(int min, int max, int lo, int hi) => min >= lo && max >= min && max <= hi;
        foreach (var k in r.Kinds)
        {
            var kw = $"Space sites kind {k.Id}";
            if (k.Kind == SiteKind.HiddenShip && k.Hidden == null) problems.Add($"{kw}: needs its hidden rules.");
            if (k.Kind == SiteKind.AlienProbe && k.Probe == null) problems.Add($"{kw}: needs its probe rules.");
            if (ChainOnlyKinds.Contains(k.Kind) && (k.Mining != null || k.Tow != null || k.Sweep != null || k.Logs != null || k.Reactor != null || k.AnyAway))
                problems.Add($"{kw}: a chain-only kind can't be mined, towed, swept, read, boarded or have a reactor.");
            if (k.Hidden is { } hd)
            {
                if (k.Kind != SiteKind.HiddenShip) problems.Add($"{kw}: hidden rules belong to hidden_ship.");
                if (hd.Fixes < 1 || hd.Fixes > 10) problems.Add($"{kw}: hidden fixes must be 1..10.");
                if (hd.FixRange <= Fix64.Zero || hd.FixRange > Fix64.FromInt(2000) || hd.Spacing < Fix64.Zero || hd.Spacing > Fix64.FromInt(1000)
                    || hd.Area <= Fix64.Zero || hd.Area > Fix64.FromInt(2000))
                    problems.Add($"{kw}: hidden fix_range and area 0..2000 (above 0), spacing 0..1000.");
                if (hd.InspectSeconds < 1 || hd.InspectSeconds > 3600 || hd.RunChance < 0 || hd.RunChance > 100 || hd.RunDistance < Fix64.Zero
                    || hd.RunDistance > Fix64.FromInt(2000) || hd.RunSeconds < 1 || hd.RunSeconds > 3600)
                    problems.Add($"{kw}: hidden inspect_seconds 1..3600, run_chance 0..100, run_distance 0..2000, run_seconds 1..3600.");
                if (!Range(hd.LatinumMin, hd.LatinumMax, 0, 100000) || !Range(hd.ResearchMin, hd.ResearchMax, 0, 100000)) problems.Add($"{kw}: hidden reward needs 0 <= min <= max (latinum, research).");
                if (k.Alongside == null) problems.Add($"{kw}: inspecting a hidden ship needs alongside.");
            }
            if (k.Probe is { } pr)
            {
                if (k.Kind != SiteKind.AlienProbe) problems.Add($"{kw}: probe rules belong to alien_probe.");
                if (pr.Keep < Fix64.Zero || pr.Keep > Fix64.FromInt(1000) || pr.Speed <= Fix64.Zero || pr.Speed > Fix64.FromInt(100)) problems.Add($"{kw}: probe keep 0..1000 and speed 0..100 (above 0).");
                if (pr.DecodeSeconds < 1 || pr.DecodeSeconds > 3600) problems.Add($"{kw}: probe decode_seconds must be 1..3600.");
                if (pr.Shock < -100 || pr.Shock > 100) problems.Add($"{kw}: probe shock must be -100..100.");
                if (ShipStatus?.Table(pr.ReplyTable) is not { } table) problems.Add($"{kw}: probe reply_table '{pr.ReplyTable}' is not in ship_status away_team tables.");
                else
                {
                    if (pr.Good.Count == 0) problems.Add($"{kw}: probe good needs at least one outcome of its reply table.");
                    foreach (var g in pr.Good)
                        if (table.Outcomes.All(o => o.Id != g)) problems.Add($"{kw}: probe good '{g}' is not an outcome of {table.Id}.");
                    if (table.Outcomes.Any(o => o.Hull > 0 || o.Injure > 0 || o.Quarantine || o.System.Length > 0 || o.Deflect))
                        problems.Add($"{kw}: probe reply table {table.Id}: no one leaves the ship, so no outcome may damage, injure, quarantine, knock out or deflect.");
                }
            }
        }
        foreach (var x in r.Weights)
            if (SpaceSiteRules.KindOf(x.Id) is { } k && ChainOnlyKinds.Contains(k)) problems.Add($"Space sites: {x.Id} is placed only by a mission, not by generation weight.");
        if (r.Events is { } e)
            foreach (var x in e.Weights)
                if (SpaceSiteRules.KindOf(x.Id) is { } k && ChainOnlyKinds.Contains(k)) problems.Add($"Space events: {x.Id} is placed only by a mission, not by the event clock.");
    }
}
