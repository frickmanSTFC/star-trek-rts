using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>Why a construction rover can't found an outpost on a tile (None = it can). Texts: <see cref="PlanetSurface.FoundText"/>.</summary>
public enum FoundProblem
{
    None,
    NotYours,

    /// <summary>The unit isn't a construction rover.</summary>
    CantFound,

    /// <summary>The rover is at 0 integrity.</summary>
    Disabled,

    /// <summary>It is already unfolding on that tile.</summary>
    AlreadyFounding,
    NotLanded,
    OffMap,
    NotSurveyed,
    Unbuildable,
    NativeLand,
    Toxic,
    Predators,

    /// <summary>A building on the tile or next to it (the hub needs its neighbours clear, like an outpost drop).</summary>
    BuildingInTheWay,

    /// <summary>A crawler is unfolded on the tile.</summary>
    VehicleInTheWay,

    /// <summary>Closer than the rover's min_distance to one of the player's colony cores.</summary>
    TooCloseToBase,

    /// <summary>Within min_distance of an outpost the player is dropping from orbit.</summary>
    OutpostIncoming,

    /// <summary>Within min_distance of a site another of the player's construction rovers is unfolding on.</summary>
    FounderNearby,
    CannotAfford,
}

// Founding a second outpost on the ground (surfaces.json units' "found": the construction rover). The rover drives to
// the site (FoundOutpostCommand: a path plus DeployOnArrival, like a crawler's mine order), checks the site and pays the
// founding cost when it gets there, unfolds over its deploy_seconds (UnitMode.Deploying; a move, pack-up or repair order
// folds it back up and refunds the cost), and is then used up: the found building (a colony core) stands on the tile as
// a new base, Queued -> Building -> Complete through the normal Construction step (the exocomps of ConstructionView),
// with one build queue of its own for itself. The rover is kept on the core (SurfaceStructure.Founder): cancelling the
// core before it is finished refunds the founding cost and the rover rolls back out, folding up.
public sealed partial class PlanetSurface
{
    /// <summary>The unit's founding rules (null = it isn't a construction rover).</summary>
    public FoundRules? FoundRulesOf(SurfaceUnit u) => _data.SurfaceUnit(u.DefId).Found;

    /// <summary>Unfolding on its site (the founding cost is paid).</summary>
    public bool Unfolding(SurfaceUnit u) => u.FoundTile >= 0 && u.Mode == UnitMode.Deploying;

    /// <summary>Why this unit can't be sent to found an outpost on this tile right now (None = it can).</summary>
    public FoundProblem FoundOutpostProblem(Simulation sim, int player, int unitId, int tile)
    {
        var u = FindUnit(unitId);
        if (u == null || u.Owner != player) return FoundProblem.NotYours;
        if (FoundRulesOf(u) is not { } rules) return FoundProblem.CantFound;
        if (Disabled(u)) return FoundProblem.Disabled;
        if (Unfolding(u) && u.FoundTile == tile) return FoundProblem.AlreadyFounding;
        return SiteProblem(sim, player, tile, rules, u);
    }

    /// <summary>
    /// Whether a new outpost could be founded on this tile by a rover with these rules: landed, on the map, surveyed,
    /// buildable, safe (no natives, toxic ground or predators), no building on it or next to it, no unfolded crawler on
    /// it, at least min_distance from the player's colony cores, drops in flight and other rovers' sites, affordable.
    /// <paramref name="except"/> is the rover itself (its own site doesn't count against it).
    /// </summary>
    public FoundProblem SiteProblem(Simulation sim, int player, int tile, FoundRules rules, SurfaceUnit? except)
    {
        if (player < 0 || player >= _players.Count || _players[player].Phase != LandingPhase.Landed) return FoundProblem.NotLanded;
        if (!OnMap(tile)) return FoundProblem.OffMap;
        if (!_players[player].Surveyed[tile]) return FoundProblem.NotSurveyed;
        if (!Map.Terrain(tile).Buildable || RuinBlocks(tile)) return FoundProblem.Unbuildable; // ruins: only an Excavation Site
        if (Map.NearNatives(tile)) return FoundProblem.NativeLand;
        if (Map.Toxic[tile]) return FoundProblem.Toxic;
        if (Map.WildlifeAt[tile] == Wildlife.Predators) return FoundProblem.Predators;
        if (FootprintProblem(tile, except) is var footprint and not FoundProblem.None) return footprint;
        if (ClearOfBases(player, tile, rules.MinDistance, except) is var near and not FoundProblem.None) return near;
        if (!sim.Players[player].Stock.CanAfford(rules.Cost)) return FoundProblem.CannotAfford;
        return FoundProblem.None;
    }

    /// <summary>The hub's footprint: no building on the tile or its neighbours, and no unfolded vehicle on the tile.</summary>
    private FoundProblem FootprintProblem(int tile, SurfaceUnit? except)
    {
        foreach (var t in Map.Grid.Within(tile, 1))
            if (_structureAt[t] != 0) return FoundProblem.BuildingInTheWay;
        if (PodLandingNear(tile)) return FoundProblem.BuildingInTheWay; // a colony pod is coming down there
        foreach (var u in _units)
            if (u != except && u.Tile == tile && u.Mode != UnitMode.Mobile) return FoundProblem.VehicleInTheWay;
        return FoundProblem.None;
    }

    /// <summary>
    /// The spacing rule shared by ground founding and outpost drops: a new colony core must be at least minDistance tiles
    /// from every colony core of the player (finished, assembling or under construction), from an outpost they are
    /// dropping from orbit and from a site one of their construction rovers is unfolding on (other than <paramref name="except"/>).
    /// </summary>
    internal FoundProblem ClearOfBases(int player, int tile, int minDistance, SurfaceUnit? except)
    {
        var g = Map.Grid;
        foreach (var s in _structures)
            if (s.Owner == player && s.Id == s.BaseId && _data.Building(s.DefId).IsCore && g.Distance(s.Tile, tile) < minDistance)
                return FoundProblem.TooCloseToBase;
        foreach (var d in _deliveries)
            if (d.Owner == player && d.Kind == DeliveryKind.Outpost && g.Distance(d.Tile, tile) < minDistance) return FoundProblem.OutpostIncoming;
        foreach (var u in _units)
            if (u != except && u.Owner == player && Unfolding(u) && g.Distance(u.FoundTile, tile) < minDistance) return FoundProblem.FounderNearby;
        return FoundProblem.None;
    }

    /// <summary>What the HUD says for a founding problem (minDistance for the spacing rule).</summary>
    public string FoundText(FoundProblem p, int tile = -1, int minDistance = 0) => p switch
    {
        FoundProblem.None => "",
        FoundProblem.NotYours => "not yours",
        FoundProblem.CantFound => "only a construction rover can found an outpost",
        FoundProblem.Disabled => "disabled: repair it first (R)",
        FoundProblem.AlreadyFounding => "already unfolding there",
        FoundProblem.NotLanded => "land the colony first",
        FoundProblem.OffMap => "off the map",
        FoundProblem.NotSurveyed => "not surveyed yet (send a rover)",
        FoundProblem.Unbuildable => OnMap(tile) ? $"can't build on {Map.Terrain(tile).Name.ToLowerInvariant()}" : "can't build there",
        FoundProblem.NativeLand => "that's the natives' land",
        FoundProblem.Toxic => "toxic ground: not a safe site",
        FoundProblem.Predators => "predators roam there: not a safe site",
        FoundProblem.BuildingInTheWay => "too close to a building (the hub needs the tile and its neighbours clear)",
        FoundProblem.VehicleInTheWay => "a crawler is unfolded there",
        FoundProblem.TooCloseToBase => $"too close to a base: at least {minDistance} tiles from your other colony cores",
        FoundProblem.OutpostIncoming => "an outpost from orbit is coming down near there",
        FoundProblem.FounderNearby => "another construction rover is founding an outpost near there",
        FoundProblem.CannotAfford => "not enough resources",
        _ => "not possible",
    };

    private void FoundOutpost(Simulation sim, FoundOutpostCommand c)
    {
        if (FoundOutpostProblem(sim, c.Player, c.UnitId, c.Tile) is var problem and not FoundProblem.None)
        {
            sim.Refuse(c, FoundText(problem));
            return;
        }
        var unit = FindUnit(c.UnitId)!;
        if (unit.Tile == c.Tile && (unit.Mode != UnitMode.Mobile || AtCentre(unit)))
        {
            AbortFounding(sim, unit);
            Halt(unit);
            unit.DeployOnArrival = false;
            unit.FoundTile = c.Tile;
            BeginFounding(sim, unit);
            return;
        }
        var path = FindPath(unit.Tile, c.Tile); // same tile, off its centre: an empty path, so it drives to the centre
        if (path == null)
        {
            sim.Refuse(c, "no way there");
            return;
        }
        AbortFounding(sim, unit); // a new site replaces the one it was unfolding on (refunded)
        SetPath(unit, path);
        StartPacking(unit);
        unit.FoundTile = c.Tile;
        unit.DeployOnArrival = true;
        unit.TyreChangeTicks = 0;
    }

    /// <summary>At the site: check it again, pay and start unfolding; or note why not (the event feed reads it).</summary>
    private void BeginFounding(Simulation sim, SurfaceUnit unit)
    {
        var rules = FoundRulesOf(unit)!;
        var problem = Disabled(unit) ? FoundProblem.Disabled : SiteProblem(sim, unit.Owner, unit.Tile, rules, unit);
        if (problem != FoundProblem.None)
        {
            Fail(sim, unit, problem);
            return;
        }
        sim.Players[unit.Owner].Stock.TrySpend(rules.Cost);
        unit.FoundTile = unit.Tile;
        StartDeploying(unit);
    }

    private static void Fail(Simulation sim, SurfaceUnit unit, FoundProblem problem)
    {
        unit.FoundTile = -1;
        unit.DeployOnArrival = false;
        unit.FoundFailedTick = sim.Tick;
        unit.FoundFailure = problem;
    }

    /// <summary>Stops a founding trip; if it was already unfolding, it folds back up and the cost comes back.</summary>
    private void AbortFounding(Simulation sim, SurfaceUnit unit)
    {
        if (unit.FoundTile < 0) return;
        if (Unfolding(unit))
        {
            foreach (var c in FoundRulesOf(unit)!.Cost) sim.Players[unit.Owner].Stock.Add(c.Resource, c.Amount);
            StartPacking(unit);
        }
        unit.FoundTile = -1;
        unit.DeployOnArrival = false;
    }

    /// <summary>
    /// Each tick: unfolding rovers count up (a rover moved off its site, e.g. by the test panel, or disabled aborts and
    /// refunds), a trip whose order was dropped (a tyre change, disabled on the way) is forgotten, and rovers that have
    /// finished unfolding become their new colony core.
    /// </summary>
    private void Founders(Simulation sim)
    {
        List<SurfaceUnit>? done = null;
        foreach (var u in _units)
        {
            if (u.FoundTile < 0) continue;
            if (u.Mode == UnitMode.Mobile && u._path.Count == 0 && !u.DeployOnArrival)
            {
                u.FoundTile = -1; // the trip was called off
                continue;
            }
            if (u.Mode != UnitMode.Deploying) continue;
            if (u.FoundTile != u.Tile || u.Broken)
            {
                AbortFounding(sim, u);
                continue;
            }
            if (++u.ModeTicks < DeployTicks(u)) continue;
            (done ??= new List<SurfaceUnit>()).Add(u);
        }
        if (done == null) return;
        foreach (var u in done) FinishFounding(sim, u);
    }

    /// <summary>The rover has unfolded: it is used up and the new colony core stands on the tile, waiting for its exocomps.</summary>
    private void FinishFounding(Simulation sim, SurfaceUnit unit)
    {
        var rules = FoundRulesOf(unit)!;
        // Someone may have built next to the site while it unfolded: fold back up, refunded.
        if (FootprintProblem(unit.Tile, unit) is var blocked and not FoundProblem.None)
        {
            AbortFounding(sim, unit);
            Fail(sim, unit, blocked);
            return;
        }
        _units.Remove(unit);
        Halt(unit);
        unit.Mode = UnitMode.Mobile;
        unit.ModeTicks = 0;
        unit.MiningStatus = MiningStatus.None;
        unit.DeployOnArrival = false;
        unit.TyreChangeTicks = 0;
        unit.RepairTicks = 0;
        unit.RepairWaiting = false;
        unit.FoundTile = -1;
        var core = PlaceStructure(unit.Owner, rules.Building, unit.Tile, 0, StructureState.Queued, 0);
        core.Founder = unit;
        Reveal(unit.Owner, unit.Tile, _data.Landing.RevealRadius);
    }

    /// <summary>Cancelling a founded core before it is finished: the founding cost comes back and the rover rolls out
    /// again on the tile, folding its crane up (its name, number, integrity and tyres are as they were).</summary>
    private void CancelFounded(Simulation sim, SurfaceStructure s)
    {
        var unit = s.Founder!;
        if (!s.Free && FoundRulesOf(unit) is { } rules)
            foreach (var c in rules.Cost) sim.Players[s.Owner].Stock.Add(c.Resource, c.Amount);
        _structures.Remove(s);
        _structureAt[s.Tile] = 0;
        PlaceUnit(unit, s.Tile, CentreOf(s.Tile));
        unit.Mode = UnitMode.Packing;
        unit.ModeTicks = 0;
        _units.Add(unit);
    }
}
