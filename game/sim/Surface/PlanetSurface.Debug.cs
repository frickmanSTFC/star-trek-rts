using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// The test panel's surface commands (dev builds only): set a vehicle's or building's values, put things on the map,
/// change stock, research and limits. Each is checked by <see cref="DebugProblem"/> when it runs (the panel asks first,
/// to say why), so a bad edit changes nothing. Simulation.ApplyDebug routes them here and counts them.
/// </summary>
public sealed partial class PlanetSurface
{
    private static readonly Fix64 MaxWorkSpeed = Fix64.FromInt(100);

    /// <summary>The test panel tuned the data: switch to it. Vehicles pick up their new speed and survey radius.</summary>
    internal void ReplaceData(GameData data)
    {
        _data = data;
        Map.ReplaceData(data);
        foreach (var u in _units)
        {
            var def = data.SurfaceUnit(u.DefId);
            u.SpeedPerTick = def.Speed * Simulation.SecondsPerTick;
            u.SurveyRadius = def.SurveyRadius;
        }
    }

    private bool IsPlayer(int player) => player >= 0 && player < _players.Count;

    private static bool IsShare(Fix64 v) => v >= Fix64.Zero && v <= Fix64.One;

    private static bool IsPercent(Fix64 v) => v >= Fix64.Zero && v <= Full;

    /// <summary>The tile itself if a vehicle can stand there with nothing else on it, else the nearest such tile within 2; -1 if none.</summary>
    public int FreeSpotNear(int tile)
    {
        if (!OnMap(tile)) return -1;
        var fallback = -1;
        foreach (var t in new[] { tile }.Concat(Map.Grid.Neighbours(tile)).Concat(Map.Grid.Within(tile, 2)))
        {
            if (!Map.Terrain(t).Passable || _structureAt[t] != 0) continue;
            if (_units.All(u => u.Tile != t)) return t;
            if (fallback < 0) fallback = t;
        }
        return fallback;
    }

    /// <summary>Why a test-panel surface command would be refused (null = it would run).</summary>
    public string? DebugProblem(Simulation sim, DebugCommand command)
    {
        switch (command)
        {
            case DebugUnitCommand c:
            {
                if (FindUnit(c.UnitId) is not { } unit) return "no such vehicle";
                var tyres = _data.SurfaceUnit(unit.DefId).Tyres;
                var whole = c.Value == Fix64.FromInt(c.Value.FloorToInt());
                return c.Field switch
                {
                    DebugUnitField.Integrity => IsPercent(c.Value) ? null : "integrity is 0 to 100",
                    DebugUnitField.Broken => null,
                    DebugUnitField.FlatTyres or DebugUnitField.Spares when tyres == null => "this vehicle has no tyres",
                    DebugUnitField.FlatTyres => whole && c.Value >= Fix64.Zero && c.Value.FloorToInt() < 1 << tyres!.Wheels ? null : $"flat tyres are a mask of its {tyres!.Wheels} wheels",
                    DebugUnitField.Spares => whole && c.Value >= Fix64.Zero && c.Value <= Fix64.FromInt(20) ? null : "spares are 0 to 20",
                    _ => "unknown field",
                };
            }
            case DebugMoveUnitCommand c:
                if (FindUnit(c.UnitId) == null) return "no such vehicle";
                if (!OnMap(c.Tile)) return "off the map";
                return Map.Terrain(c.Tile).Passable ? null : "vehicles can't stand there";
            case DebugRemoveUnitCommand c:
                return FindUnit(c.UnitId) == null ? "no such vehicle" : null;
            case DebugSpawnUnitCommand c:
                if (!IsPlayer(c.Owner)) return "no such player";
                if (_data.SurfaceUnits.All(u => u.Id != c.UnitId)) return "unknown vehicle";
                return FreeSpotNear(c.Tile) < 0 ? "no room there" : null;

            case DebugStructureCommand c:
            {
                var s = FindStructure(c.StructureId);
                if (s == null) return "no such building";
                var def = _data.Building(s.DefId);
                switch (c.Field)
                {
                    case DebugStructureField.Condition:
                        if (!def.Wears) return "this building doesn't wear";
                        return IsPercent(c.Value) ? null : "condition is 0 to 100";
                    case DebugStructureField.Broken:
                        return NotReadyProblem(s);
                    case DebugStructureField.NoWear:
                        return def.Wears ? null : "this building doesn't wear";
                    case DebugStructureField.Level:
                        if (def.Levels.Count == 0) return "this building has no levels";
                        if (NotReadyProblem(s) is { } notReady) return notReady;
                        return c.Value >= Fix64.One && c.Value <= Fix64.FromInt(def.MaxLevel) && c.Value == Fix64.FromInt(c.Value.FloorToInt())
                            ? null : $"levels are 1 to {def.MaxLevel}";
                    case DebugStructureField.WorkSpeed:
                        return c.Value > Fix64.Zero && c.Value <= MaxWorkSpeed ? null : "work speed is above 0, up to 100";
                    case DebugStructureField.BuildProgress:
                        if (s.State == StructureState.Complete) return "already built";
                        if (s.Assembling && c.Value < Fix64.One) return "still assembling: it can only be finished now";
                        return IsShare(c.Value) ? null : "progress is 0 to 1";
                    case DebugStructureField.UpgradeProgress:
                        if (s.UpgradingLevel == 0 && s.UpgradingTo == null) return "not upgrading";
                        return IsShare(c.Value) ? null : "progress is 0 to 1";
                    case DebugStructureField.ProductionProgress:
                        if (s._production.Count == 0) return "no order in progress";
                        return IsShare(c.Value) ? null : "progress is 0 to 1";
                    default:
                        return "unknown field";
                }
            }
            case DebugRemoveStructureCommand c:
            {
                var s = FindStructure(c.StructureId);
                if (s == null) return "no such building";
                if (s.Id == s.BaseId && _structures.Any(o => o != s && o.BaseId == s.Id)) return "remove the base's other buildings first";
                return null;
            }
            case DebugPlaceBuildingCommand c:
            {
                if (!IsPlayer(c.Owner)) return "no such player";
                if (_data.Buildings.All(b => b.Id != c.BuildingId)) return "unknown building";
                var def = _data.Building(c.BuildingId);
                if (!def.IsCore && !_data.BuildMenu.Contains(c.BuildingId)) return "not a surface building";
                if (!OnMap(c.Tile)) return "off the map";
                if (!Map.Terrain(c.Tile).Buildable) return "can't build on that ground";
                if (_structureAt[c.Tile] != 0) return "a building is there";
                if (_units.Any(u => u.Tile == c.Tile && u.Mode != UnitMode.Mobile)) return "a crawler is mining there";
                if (!def.IsCore && BaseFor(c.Owner, c.Tile) == 0) return "out of that player's build range (place a Colony Hub first)";
                return null;
            }
            case DebugBaseCommand c:
            {
                var core = FindStructure(c.BaseId);
                if (core == null || core.Id != core.BaseId) return "no such base";
                return c.Action switch
                {
                    DebugBaseAction.SetCondition => IsPercent(c.Value) ? null : "condition is 0 to 100",
                    DebugBaseAction.FinishConstruction or DebugBaseAction.RepairAll or DebugBaseAction.FinishOrders => null,
                    _ => "unknown action",
                };
            }

            case DebugFlagCommand c:
                return IsPlayer(c.Target) ? null : "no such player";
            case DebugTechCommand c:
            {
                if (!IsPlayer(c.Target)) return "no such player";
                if (!_data.HasTech(c.TechId)) return "unknown tech";
                var cost = _data.Tech(c.TechId).Cost;
                return c.Progress >= Fix64.Zero && c.Progress <= cost ? null : $"progress is 0 to {cost}";
            }
            case DebugSurveyAllCommand c:
                return IsPlayer(c.Target) ? null : "no such player";
            case DebugDepositCommand c:
                if (!OnMap(c.Tile)) return "off the map";
                if (Map.DepositResource[c.Tile] < 0) return "no deposit there";
                return c.Amount >= Fix64.Zero && c.Amount <= Simulation.DebugMaxAmount ? null : "amounts are 0 to 1 000 000 000";
            default:
                return "not a surface command";
        }
    }

    /// <summary>Runs a surface debug command that <see cref="DebugProblem"/> accepted.</summary>
    internal void ApplyDebug(Simulation sim, DebugCommand command)
    {
        switch (command)
        {
            case DebugUnitCommand c:
            {
                var u = FindUnit(c.UnitId)!;
                if (c.Field == DebugUnitField.Integrity) u.Integrity = c.Value;
                else if (c.Field == DebugUnitField.FlatTyres)
                {
                    var mask = c.Value.FloorToInt();
                    var added = mask & ~u.FlatTyres;
                    u.FlatTyres = mask;
                    if (added != 0)
                    {
                        u.LastFlat = Wheels.First(added, 32);
                        u.FlatAtTick = sim.Tick;
                    }
                    if (mask == 0) u.TyreChangeTicks = 0;
                }
                else if (c.Field == DebugUnitField.Spares) u.Spares = c.Value.FloorToInt();
                else if (c.Value != Fix64.Zero)
                {
                    if (u.Broken) break;
                    AbortFounding(sim, u); // a construction rover unfolding folds up, refunded
                    u.Broken = true;
                    u.RepairTicks = 0;
                    u.RepairWaiting = false;
                    StartPacking(u);
                    u.DeployOnArrival = false;
                    u.MiningStatus = MiningStatus.None;
                    if (_data.SurfaceUnit(u.DefId).Damage != null) Disable(sim, u); // a rover stops where it is
                }
                else
                {
                    u.Broken = false;
                    u.RepairTicks = 0;
                    u.RepairWaiting = false;
                }
                break;
            }
            case DebugMoveUnitCommand c:
            {
                var u = FindUnit(c.UnitId)!;
                AbortFounding(sim, u);
                u.Tile = c.Tile;
                u._path.Clear();
                u.Progress = Fix64.Zero;
                u.Mode = UnitMode.Mobile;
                u.ModeTicks = 0;
                u.DeployOnArrival = false;
                u.MiningStatus = MiningStatus.None;
                Reveal(u.Owner, c.Tile, u.SurveyRadius);
                break;
            }
            case DebugRemoveUnitCommand c:
                AbortFounding(sim, FindUnit(c.UnitId)!);
                _units.Remove(FindUnit(c.UnitId)!);
                break;
            case DebugSpawnUnitCommand c:
                NewUnit(c.Owner, _data.SurfaceUnit(c.UnitId), FreeSpotNear(c.Tile), -1, -1);
                break;

            case DebugStructureCommand c:
                SetStructure(sim, FindStructure(c.StructureId)!, c.Field, c.Value);
                break;
            case DebugRemoveStructureCommand c:
            {
                var s = FindStructure(c.StructureId)!;
                _structures.Remove(s);
                _structureAt[s.Tile] = 0;
                break;
            }
            case DebugPlaceBuildingCommand c:
            {
                var def = _data.Building(c.BuildingId);
                var s = PlaceStructure(c.Owner, c.BuildingId, c.Tile, ((c.Facing % 6) + 6) % 6, StructureState.Queued,
                    def.IsCore ? 0 : BaseFor(c.Owner, c.Tile));
                s.Free = true;
                Complete(s);
                if (def.IsCore) Reveal(c.Owner, c.Tile, _data.Landing.RevealRadius);
                break;
            }
            case DebugBaseCommand c:
                foreach (var s in _structures.Where(s => s.BaseId == c.BaseId).ToList())
                {
                    var def = _data.Building(s.DefId);
                    switch (c.Action)
                    {
                        case DebugBaseAction.FinishConstruction:
                            if (s.State != StructureState.Complete) Complete(s);
                            if (s.UpgradingLevel > 0) FinishLevel(s);
                            if (s.UpgradingTo != null) FinishCoreUpgrade(s);
                            break;
                        case DebugBaseAction.RepairAll when def.Wears && s.State == StructureState.Complete:
                            Repaired(s);
                            break;
                        case DebugBaseAction.SetCondition when def.Wears && s.State == StructureState.Complete:
                            s.Condition = c.Value;
                            break;
                        case DebugBaseAction.FinishOrders:
                            while (s._production.Count > 0) FinishOrder(sim, s);
                            break;
                    }
                }
                break;

            case DebugFlagCommand c:
            {
                var p = _players[c.Target];
                switch (c.Flag)
                {
                    case DebugFlag.NoStorageCap: p.NoStorageCap = c.On; break;
                    case DebugFlag.UnlimitedPower: p.UnlimitedPower = c.On; break;
                    case DebugFlag.FreeBuilding: p.FreeBuilding = c.On; break;
                    case DebugFlag.InstantBuild: p.InstantBuild = c.On; break;
                }
                break;
            }
            case DebugTechCommand c:
            {
                var p = _players[c.Target];
                var t = _data.Tech(c.TechId);
                p.Known[t.Index] = c.Known;
                p.ResearchProgress[t.Index] = c.Progress;
                if (c.Known && p.ActiveTech == t.Index) p.ActiveTech = -1;
                break;
            }
            case DebugSurveyAllCommand c:
                System.Array.Fill(_players[c.Target].Surveyed, true);
                break;
            case DebugDepositCommand c:
                Map.DepositAmount[c.Tile] = c.Amount;
                break;
        }
    }

    private void SetStructure(Simulation sim, SurfaceStructure s, DebugStructureField field, Fix64 value)
    {
        var def = _data.Building(s.DefId);
        switch (field)
        {
            case DebugStructureField.Condition:
                s.Condition = value;
                break;
            case DebugStructureField.Broken:
                if (value != Fix64.Zero)
                {
                    s.Broken = true;
                    s.Status = StructureStatus.Broken;
                }
                else Repaired(s, keepCondition: true);
                break;
            case DebugStructureField.NoWear:
                s.NoWear = value != Fix64.Zero;
                break;
            case DebugStructureField.Level:
                if (s.UpgradingLevel > 0) // an upgrade under way is called off and refunded, as with X
                {
                    foreach (var c in def.Levels[s.UpgradingLevel - 2].Cost) sim.Players[s.Owner].Stock.Add(c.Resource, c.Amount);
                    s.UpgradingLevel = 0;
                    s.UpgradeTicks = 0;
                }
                s.Level = value.FloorToInt();
                break;
            case DebugStructureField.WorkSpeed:
                s.WorkSpeed = value;
                break;
            case DebugStructureField.BuildProgress:
            {
                var total = def.BuildSeconds * Simulation.TickRate;
                if (value >= Fix64.One || total <= 0) Complete(s);
                else s.BuildTicks = System.Math.Min(total - 1, (value * total).FloorToInt());
                break;
            }
            case DebugStructureField.UpgradeProgress:
            {
                var total = (s.UpgradingLevel > 0 ? def.Levels[s.UpgradingLevel - 2].BuildSeconds : _data.Building(s.UpgradingTo!).BuildSeconds) * Simulation.TickRate;
                if (value >= Fix64.One || total <= 0)
                {
                    if (s.UpgradingLevel > 0) FinishLevel(s);
                    else FinishCoreUpgrade(s);
                }
                else s.UpgradeTicks = System.Math.Min(total - 1, (value * total).FloorToInt());
                break;
            }
            case DebugStructureField.ProductionProgress:
            {
                var total = _data.SurfaceUnit(s._production[0]).BuildSeconds * Simulation.TickRate;
                if (value >= Fix64.One || total <= 0) FinishOrder(sim, s);
                else s.ProductionProgress = value * total;
                break;
            }
        }
    }

    /// <summary>Fixed: back to 100 (unless keepCondition), working, and any maintenance order or drone job closed.</summary>
    private static void Repaired(SurfaceStructure s, bool keepCondition = false)
    {
        if (!keepCondition) s.Condition = Full;
        s.Broken = false;
        s.MaintenanceOrdered = false;
        s.MaintenanceWaiting = false;
        s.AutoRepairing = false;
    }

    /// <summary>The Vehicle Bay's current order is done: the unit rolls out and the next order starts from zero.</summary>
    private void FinishOrder(Simulation sim, SurfaceStructure bay)
    {
        RollOut(sim, bay, bay._production[0]);
        bay._production.RemoveAt(0);
        bay.ProductionProgress = Fix64.Zero;
    }
}
