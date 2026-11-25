using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Surface;

// Space jobs, phase 3 (docs/space-jobs-contract.md "Phase 3"; data maps/events.json rogue impact): a rogue asteroid that
// nobody turned aside strikes the colony's body. The hit is bounded and deterministic (the rock's own seed, never sim.Rng):
// a spot near a landed player's core, and the buildings and vehicles round it lose condition / integrity by distance,
// the nearest few only, never below 1: nothing is destroyed or broken by it.
public sealed partial class PlanetSurface
{
    private const ulong ImpactSalt = 0x1A7AC7_0001UL;

    /// <summary>A rogue asteroid strikes: a spot within spread hexes of a landed player's core (picked by the seed), then
    /// every building within radius loses damage × (1 − distance ÷ (radius + 1)) condition and every vehicle that much
    /// integrity (the max_buildings / max_vehicles nearest, by distance then id), never below 1. Returns the log text, or
    /// null when no one has a base (the rock falls far from anyone).</summary>
    internal string? RogueImpact(Simulation sim, RogueImpactDef d, ulong seed)
    {
        var cores = new List<SurfaceStructure>();
        foreach (var s in _structures)
            if (s.Id == s.BaseId && _data.Building(s.DefId).IsCore) cores.Add(s);
        if (cores.Count == 0) return null;
        var core = cores[(int)(SystemGenerator.H(seed, ImpactSalt, 1) % (ulong)cores.Count)];
        var around = Map.Grid.Within(core.Tile, System.Math.Max(0, d.Spread));
        var spot = around.Count == 0 ? core.Tile : around[(int)(SystemGenerator.H(seed, ImpactSalt, 2) % (ulong)around.Count)];
        var damage = SystemGenerator.Int(SystemGenerator.H(seed, ImpactSalt, 3), d.DamageMin, d.DamageMax);
        var reach = d.Radius + 1;
        Fix64 Hit(int distance) => Fix64.FromInt(damage * (reach - distance)) / Fix64.FromInt(reach);

        var buildings = _structures.Where(s => Map.Grid.Distance(s.Tile, spot) <= d.Radius)
            .OrderBy(s => Map.Grid.Distance(s.Tile, spot)).ThenBy(s => s.Id).Take(System.Math.Max(0, d.MaxBuildings)).ToList();
        foreach (var s in buildings)
        {
            var hit = Hit(Map.Grid.Distance(s.Tile, spot));
            s.Condition = Fix64.Max(Fix64.Min(s.Condition, Fix64.One), s.Condition - hit);
        }
        var vehicles = _units.Where(u => u.Drone == null && Map.Grid.Distance(u.Tile, spot) <= d.Radius)
            .OrderBy(u => Map.Grid.Distance(u.Tile, spot)).ThenBy(u => u.Id).Take(System.Math.Max(0, d.MaxVehicles)).ToList();
        foreach (var u in vehicles)
        {
            var hit = Hit(Map.Grid.Distance(u.Tile, spot));
            u.Integrity = Fix64.Max(Fix64.Min(u.Integrity, Fix64.One), u.Integrity - hit);
            u.HazardAtTick = sim.Tick;
            u.LastHazard = "meteor strike";
            u.LastHazardDamage = hit.FloorToInt();
        }
        LastImpactTile = spot;
        LastImpactTick = sim.Tick;
        var away = Map.Grid.Distance(spot, core.Tile);
        var where = away == 0 ? "on a base" : $"{away} hex{(away == 1 ? "" : "es")} from a base";
        return buildings.Count == 0 && vehicles.Count == 0
            ? $"it came down {where}; nothing was hit"
            : $"it came down {where}: −{damage} to {buildings.Count} building{(buildings.Count == 1 ? "" : "s")} and {vehicles.Count} vehicle{(vehicles.Count == 1 ? "" : "s")} (nothing destroyed)";
    }

    /// <summary>Where and when the last rogue asteroid came down on this surface (-1 = none): the view's crater and flash.</summary>
    public int LastImpactTile { get; private set; } = -1;
    public long LastImpactTick { get; private set; } = -1;
}
