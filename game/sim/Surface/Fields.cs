using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Deposit fields (owner 2026-11-12: "restrict the amount of mining types there are, and make them larger"): instead of
// scattered single-tile deposits, each deposit rule with "fields" places a few contiguous blobs of neighbouring hexes on
// its terrain. Every hex of a field is its own deposit (its own amount: a mine or a crawler still works one hex), so
// mining, depletion, deep-core reopening, hints and markers all keep working per tile. Generated once with the map, from
// its own DeterministicRng stream off the map seed (never sim.Rng), so the same seed always gives the same fields.
public sealed partial class SurfaceMap
{
    /// <summary>Random seed tiles looked at per field (the farthest from the fields so far wins).</summary>
    private const int SeedCandidates = 6;

    private bool FieldTerrain(DepositRule rule, int tile) =>
        rule.Terrain.Contains(Terrain(tile).Id) && (!rule.Coastal || TouchesWater(tile));

    /// <summary>A deposit (of any field) on the tile or within radius of it.</summary>
    private bool DepositWithin(int tile, int radius)
    {
        foreach (var t in Grid.Within(tile, radius))
            if (DepositResource[t] >= 0) return true;
        return false;
    }

    /// <summary>Places every field rule's fields, rules in file order. Seeds: of a few random matching tiles (coastal ones
    /// touch water) at least Spacing (at least 2) hexes from every deposit placed so far, the one farthest from the rule's
    /// fields so far; then the field grows one hex at a time
    /// onto matching neighbours that don't touch another field, compact hexes (more neighbours already in the field)
    /// more likely, up to its rolled size. A field that can't reach SizeMin is dropped and another seed tried.</summary>
    internal void PlaceFields(GameData data, DeterministicRng rng)
    {
        var grid = Grid;
        var inField = new bool[grid.Count];
        foreach (var rule in Def.Deposits)
        {
            if (rule.Fields is not { } fr) continue;
            var resource = data.Resource(rule.ResourceId).Index;
            var seeds = new List<int>();
            for (var t = 0; t < grid.Count; t++)
                if (FieldTerrain(rule, t)) seeds.Add(t);
            var want = fr.CountFor(grid.Count);
            var clear = System.Math.Max(2, fr.Spacing) - 1;
            var placed = 0;
            var field = new List<int>();
            var frontier = new List<int>();
            var weights = new List<int>();
            var centres = new List<int>();
            for (var attempt = 0; placed < want && attempt < want * 25 && seeds.Count > 0; attempt++)
            {
                // Best of a few random candidates: the one farthest from this resource's fields so far, so the fields
                // spread over the map (more landing sites get each resource in reach) instead of bunching.
                var seed = -1;
                var best = -1;
                for (var k = 0; k < SeedCandidates && seeds.Count > 0; k++)
                {
                    var i = rng.NextInt(seeds.Count);
                    var t = seeds[i];
                    if (DepositWithin(t, clear))
                    {
                        seeds[i] = seeds[^1]; // never a seed again: deposits only get added
                        seeds.RemoveAt(seeds.Count - 1);
                        continue;
                    }
                    var d = int.MaxValue;
                    foreach (var c in centres) d = System.Math.Min(d, grid.Distance(c, t));
                    if (d > best)
                    {
                        best = d;
                        seed = t;
                    }
                }
                if (seed < 0) continue;
                seeds.Remove(seed);
                var size = rng.NextInt(fr.SizeMin, fr.SizeMax + 1);
                field.Clear();
                field.Add(seed);
                inField[seed] = true;
                while (field.Count < size)
                {
                    frontier.Clear();
                    weights.Clear();
                    foreach (var f in field)
                        foreach (var n in grid.Neighbours(f))
                        {
                            if (inField[n] || frontier.Contains(n) || DepositResource[n] >= 0 || !FieldTerrain(rule, n)) continue;
                            var touchesOther = false;
                            var own = 0;
                            foreach (var m in grid.Neighbours(n))
                            {
                                if (inField[m]) own++;
                                else if (DepositResource[m] >= 0) touchesOther = true;
                            }
                            if (touchesOther) continue;
                            frontier.Add(n);
                            weights.Add(own * own);
                        }
                    if (frontier.Count == 0) break;
                    var total = 0;
                    foreach (var w in weights) total += w;
                    var roll = rng.NextInt(total);
                    var k = 0;
                    while (roll >= weights[k]) roll -= weights[k++];
                    field.Add(frontier[k]);
                    inField[frontier[k]] = true;
                }
                foreach (var t in field) inField[t] = false;
                if (field.Count < fr.SizeMin) continue;
                centres.Add(seed);
                foreach (var t in field)
                {
                    DepositResource[t] = resource;
                    DepositAmount[t] = Fix64.FromInt(rng.NextInt(rule.Min, rule.Max + 1));
                    DepositStart[t] = DepositAmount[t];
                }
                placed++;
            }
        }
    }

    /// <summary>The field a deposit tile belongs to: it and every deposit of the same resource joined to it through
    /// neighbouring deposit hexes (fields never touch each other, so this is exactly one field). Empty when the tile
    /// has no deposit.</summary>
    public List<int> FieldOf(int tile)
    {
        var list = new List<int>();
        if (tile < 0 || tile >= Grid.Count || DepositResource[tile] < 0) return list;
        var res = DepositResource[tile];
        list.Add(tile);
        for (var i = 0; i < list.Count; i++)
            foreach (var n in Grid.Neighbours(list[i]))
                if (DepositResource[n] == res && !list.Contains(n)) list.Add(n);
        return list;
    }
}
