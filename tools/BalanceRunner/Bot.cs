using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Balance;

/// <summary>A line in the bot's log: what it did or gave up on, at which tick.</summary>
public sealed record BotEvent(long Tick, string Text);

/// <summary>
/// A deterministic scripted player for the surface economy. It only reads the sim's public state and the problem
/// checks the UI uses (LandProblem, CheckPlacement, MineProblem, UpgradeProblem, ProductionProblem, ResearchProblem,
/// SiteProblem, OutpostProblem, BeamProblem, ChangeTyreProblem, MaintenanceProblem), and acts only through commands
/// scheduled for the current tick. Call <see cref="Think"/> before <c>sim.Step()</c>; it decides every
/// <see cref="ThinkTicks"/> ticks (once a second), like a fast but not superhuman player.
/// </summary>
public sealed class Bot
{
    public const int ThinkTicks = Simulation.TickRate;

    private readonly Simulation _sim;
    private readonly int _p;
    private readonly BuildOrder _order;
    private PlanetSurface S => _sim.Surface!;
    private SurfaceMap Map => S.Map;
    private GameData Data => _sim.Data;

    public List<BotEvent> Log { get; } = new();

    /// <summary>Index of the step being worked on (Steps.Count = all done).</summary>
    public int StepIndex { get; private set; }

    private int _stepRepeat; // how many of the step's Count are done
    private long _stepSince; // tick the step became current
    private Pending? _pending; // a command sent last think, checked this think
    private int _failures;

    /// <summary>Why the current step is waiting (shown when it times out, and in the report).</summary>
    public string Waiting { get; private set; } = "";
    private readonly List<string> _research = new();
    private readonly Dictionary<int, int> _roverGoal = new(); // rover id → goal tile
    private readonly Dictionary<int, long> _repairSent = new(); // unit id → tick a repair order went out
    private readonly Dictionary<int, (int Tile, long Tick)> _mineSent = new();
    private readonly HashSet<(int Unit, int Tile)> _badMine = new();
    private readonly HashSet<int> _homeSent = new();
    private int _landingTile = -1;
    private long _roverOrderedTick = -1;

    private sealed record Pending(StepKind Kind, int Tile, int Id, int Before, long Tick);

    public Bot(Simulation sim, int player, BuildOrder order)
    {
        _sim = sim;
        _p = player;
        _order = order;
        _research.AddRange(order.Research);
    }

    public SurfacePlayer Me => S.Players[_p];
    private ResourceBag Stock => _sim.Players[_p].Stock;
    public int LandingTile => _landingTile;

    private void Say(string text) => Log.Add(new BotEvent(_sim.Tick, text));

    private void Send(Command c) => _sim.Schedule(_sim.Tick, c);

    /// <summary>Decide and queue commands for this tick (only every ThinkTicks ticks).</summary>
    public void Think()
    {
        if (_sim.Tick % ThinkTicks != 0) return;
        if (Me.Phase == LandingPhase.InOrbit)
        {
            Land();
            return;
        }
        if (Me.Phase != LandingPhase.Landed) return;
        Vehicles();
        Maintain();
        Research();
        Steps();
    }

    // ---------------------------------------------------------------- landing

    private List<int>? _sites;

    /// <summary>Ranks every legal site once (CanLand: the green tiles), by the orbital scan's region hints for the
    /// preferred ores, buildable ground in the hub's build radius and safety; then lands on the best one with no storm.</summary>
    private void Land()
    {
        if (_sites == null)
        {
            var hubRadius = Data.Building(Data.Landing.HubBuilding).BuildRadius;
            var regionScore = new Dictionary<int, int>();
            var scored = new List<(int Tile, int Score)>();
            for (var t = 0; t < Map.Grid.Count; t++)
            {
                if (!S.CanLand(_p, t)) continue;
                var region = Map.Region(t);
                if (!regionScore.TryGetValue(region, out var hint))
                {
                    hint = 0;
                    foreach (var h in Map.Hints(region))
                    {
                        var id = Data.Resources[h.Resource].Id;
                        var rank = _order.LandingPrefer.ToList().IndexOf(id);
                        if (rank < 0) continue;
                        hint += ((int)h.Strength + 1) * 10 * (_order.LandingPrefer.Count - rank);
                    }
                    regionScore[region] = hint;
                }
                var buildable = Map.Grid.Within(t, hubRadius).Count(x => Map.Terrain(x).Buildable && !Map.NearNatives(x));
                var safe = Map.Safety(t) == LandingSafety.Safe ? 5 : 0;
                // open land to drive and survey: passable tiles reachable within 8 steps (most is 217)
                scored.Add((t, hint + 3 * buildable + Reachable(t, 8) / 3 + safe));
            }
            _sites = scored.OrderByDescending(x => x.Score).ThenBy(x => x.Tile).Select(x => x.Tile).ToList();
            if (_sites.Count == 0) Say("no landing site on this planet");
        }
        foreach (var t in _sites)
        {
            if (S.LandProblem(_sim, _p, t) != null) continue;
            _landingTile = t;
            Send(new LandColonyCommand(_p, t));
            Say($"landing at tile {t} ({Map.Grid.Column(t)}, {Map.Grid.Row(t)})");
            return;
        }
    }

    /// <summary>How many passable tiles a vehicle could reach from a tile within this many steps.</summary>
    private int Reachable(int from, int steps)
    {
        var seen = new HashSet<int> { from };
        var frontier = new List<int> { from };
        for (var i = 0; i < steps; i++)
        {
            var next = new List<int>();
            foreach (var t in frontier)
                foreach (var n in Map.Grid.Neighbours(t))
                    if (Map.Terrain(n).Passable && seen.Add(n)) next.Add(n);
            frontier = next;
        }
        return seen.Count;
    }

    // ---------------------------------------------------------------- vehicles

    private SurfaceStructure? MainBase => S.Bases(_p).FirstOrDefault();

    private IEnumerable<SurfaceUnit> Mine() => S.Units.Where(u => u.Owner == _p);

    private bool IsSurveyRover(SurfaceUnit u) => u.DefId.StartsWith("survey_rover", StringComparison.Ordinal);

    private void Vehicles()
    {
        var units = Mine().ToList();
        foreach (var u in units) Upkeep(u);
        var crawlerIndex = 0;
        foreach (var u in units)
        {
            if (_repairSent.ContainsKey(u.Id) || u.FoundTile >= 0) continue;
            var def = Data.SurfaceUnit(u.DefId);
            if (def.CanMine) Crawler(u, crawlerIndex++);
            else if (IsSurveyRover(u)) Survey(u, units);
        }
    }

    /// <summary>Repairs, tyre changes. A unit with a repair order is left alone until it no longer needs one.</summary>
    private void Upkeep(SurfaceUnit u)
    {
        var def = Data.SurfaceUnit(u.DefId);
        if (_repairSent.TryGetValue(u.Id, out var sent))
        {
            if (!S.NeedsRepair(u) || (!u.Broken && u.Integrity >= Fix64.FromInt(100) && S.FlatCount(u) == 0))
            {
                _repairSent.Remove(u.Id);
                return;
            }
            // Re-send if it is parked off the pad (the order was refused, or it was pushed off).
            if (u.Path.Count == 0 && !S.IsRepairing(u) && _sim.Tick - sent > 10 * Simulation.TickRate) Repair(u);
            return;
        }
        if (u.FoundTile >= 0 || S.ChangingTyre(u)) return;
        var needs = def.CanMine ? u.Broken
            : def.Damage != null && (S.Disabled(u) || u.Integrity < Fix64.FromInt(_order.RepairBelow) || (S.FlatCount(u) > 0 && u.Spares == 0));
        if (needs)
        {
            Repair(u);
            return;
        }
        if (S.FlatCount(u) > 0 && S.ChangeTyreProblem(_p, u.Id) == null) Send(new ChangeTyreCommand(_p, new[] { u.Id }));
    }

    private void Repair(SurfaceUnit u)
    {
        if (S.NearestRepairPad(_p, u.Tile) == null) return; // no finished core yet
        _repairSent[u.Id] = _sim.Tick;
        _roverGoal.Remove(u.Id);
        Send(new RepairCommand(_p, new[] { u.Id }));
    }

    /// <summary>An idle crawler goes to the nearest surveyed deposit of its preferred ore (near a finished base).</summary>
    private void Crawler(SurfaceUnit u, int index)
    {
        if (u.Broken) return;
        if (u.Mode == UnitMode.Deployed && u.MiningStatus == MiningStatus.Mining && (_sim.Tick / ThinkTicks + u.Id) % 60 == 0)
        {
            // Once a minute: a crawler on a less wanted ore moves when a more wanted deposit is known now.
            var here = Rank(Map.DepositResource[u.Tile], index);
            if (here > 0 && BestDeposit(u, index) is { Tile: >= 0 } better && better.Rank < here) MineAt(u, better.Tile, "moves");
            return;
        }
        if (u.Mode != UnitMode.Mobile || u.Path.Count > 0 || u.DeployOnArrival) return;
        if (_mineSent.TryGetValue(u.Id, out var m))
        {
            if (_sim.Tick - m.Tick < 2 * Simulation.TickRate) return; // give the order a moment
            _badMine.Add((u.Id, m.Tile)); // still idle: the order didn't take (no path)
            _mineSent.Remove(u.Id);
        }
        var best = BestDeposit(u, index);
        if (best.Tile >= 0) MineAt(u, best.Tile, "to mine");
    }

    private void MineAt(SurfaceUnit u, int tile, string verb)
    {
        _mineSent[u.Id] = (tile, _sim.Tick);
        Send(new MineCommand(_p, u.Id, tile));
        Say($"{u.Name} {u.Number} {verb} {Data.Resources[Map.DepositResource[tile]].Id} at {tile}");
    }

    /// <summary>0 = the crawler's first choice of ore; crawler n starts at entry n of the preference list. -1 = not wanted.</summary>
    private int Rank(int resource, int index)
    {
        var prefer = _order.CrawlerPrefer;
        if (resource < 0) return -1;
        if (prefer.Count == 0) return 0;
        var at = -1;
        for (var i = 0; i < prefer.Count; i++)
            if (prefer[i] == Data.Resources[resource].Id) at = i;
        return at < 0 ? -1 : (at - index % prefer.Count + prefer.Count) % prefer.Count;
    }

    /// <summary>The best surveyed deposit for this crawler near a base: most wanted ore first, then nearest the bases
    /// (pods fly home), then nearest the crawler; only tiles the sim's MineProblem allows.</summary>
    private (int Tile, int Rank) BestDeposit(SurfaceUnit u, int index)
    {
        var bases = S.Bases(_p).Where(b => b.State == StructureState.Complete).ToList();
        if (bases.Count == 0) bases = S.Bases(_p).ToList();
        if (bases.Count == 0) return (-1, -1);
        var taken = new HashSet<int>();
        foreach (var o in Mine())
            if (o != u && Data.SurfaceUnit(o.DefId).CanMine)
            {
                if (o.Mode != UnitMode.Mobile) taken.Add(o.Tile);
                if (o.Path.Count > 0) taken.Add(o.Path[^1]);
            }
        int best = -1, bestRank = int.MaxValue, bestScore = int.MaxValue;
        var seen = new HashSet<int>();
        foreach (var b in bases)
            foreach (var t in Map.Grid.Within(b.Tile, _order.CrawlerMaxDistance))
            {
                if (!seen.Add(t) || t == u.Tile && u.Mode != UnitMode.Mobile || taken.Contains(t) || _badMine.Contains((u.Id, t))) continue;
                if (Map.DepositAmount[t] <= Fix64.Zero) continue;
                var rank = Rank(Map.DepositResource[t], index);
                if (rank < 0) continue;
                var score = 2 * bases.Min(x => Map.Grid.Distance(x.Tile, t)) + Map.Grid.Distance(u.Tile, t);
                if (rank > bestRank || (rank == bestRank && (score > bestScore || (score == bestScore && t > best)))) continue;
                if (S.MineProblem(_p, u.Id, t) != null) continue;
                best = t;
                bestRank = rank;
                bestScore = score;
            }
        return (best, best < 0 ? -1 : bestRank);
    }

    /// <summary>Frontier exploration: an idle rover (or one whose goal got surveyed) drives to the nearest reachable
    /// unsurveyed tile within the survey range, keeping clear of other rovers' goals and of toxic or predator ground.
    /// When nothing is left it goes home to the pad.</summary>
    private void Survey(SurfaceUnit u, List<SurfaceUnit> units)
    {
        if (S.Disabled(u) || S.ChangingTyre(u)) return;
        var surveyed = Me.Surveyed;
        var driving = u.Path.Count > 0;
        if (driving && _roverGoal.TryGetValue(u.Id, out var goal) && !surveyed[goal]) return;
        if (driving && !_roverGoal.ContainsKey(u.Id)) return; // going home
        var home = MainBase;
        if (home == null) return;
        var others = _roverGoal.Where(kv => kv.Key != u.Id).Select(kv => kv.Value).ToList();
        var target = Frontier(u.Tile, home.Tile, others);
        if (target < 0)
        {
            _roverGoal.Remove(u.Id);
            if (!driving && u.Tile != home.Tile && _homeSent.Add(u.Id))
            {
                Send(new MoveUnitsCommand(_p, new[] { u.Id }, home.Tile));
                Say($"{u.Name} {u.Number}: survey done, going home");
            }
            return;
        }
        _homeSent.Remove(u.Id);
        _roverGoal[u.Id] = target;
        Send(new MoveUnitsCommand(_p, new[] { u.Id }, target));
    }

    private int Frontier(int from, int home, List<int> avoid)
    {
        var g = Map.Grid;
        var surveyed = Me.Surveyed;
        var dist = new int[g.Count];
        Array.Fill(dist, -1);
        var queue = new Queue<int>();
        dist[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (!surveyed[t] && Safe(t) && avoid.All(a => g.Distance(a, t) > 3)) return t;
            foreach (var n in g.Neighbours(t))
            {
                if (dist[n] >= 0 || !Map.Terrain(n).Passable || g.Distance(home, n) > _order.SurveyMaxDistance) continue;
                dist[n] = dist[t] + 1;
                queue.Enqueue(n);
            }
        }
        return -1;
    }

    private bool Safe(int t) => !Map.Toxic[t] && Map.WildlifeAt[t] != Wildlife.Predators && !Map.NearNatives(t);

    // ---------------------------------------------------------------- upkeep and research

    private void Maintain()
    {
        if (_order.MaintainBelow <= 0) return;
        var below = Fix64.FromInt(_order.MaintainBelow);
        foreach (var s in S.Structures)
        {
            if (s.Owner != _p || s.State != StructureState.Complete || !Data.Building(s.DefId).Wears) continue;
            if (s.MaintenanceOrdered || s.AutoRepairing || !(s.Broken || s.Condition < below)) continue;
            if (S.MaintenanceProblem(_p, s.Id) != null) continue;
            Send(new MaintainCommand(_p, s.Id)); // a toggle: only ever sent when not ordered
        }
    }

    private void Research()
    {
        if (Me.ActiveTech >= 0) return;
        if (!S.Structures.Any(s => s.Owner == _p && s.State == StructureState.Complete && Data.Building(s.DefId).IsLab)) return;
        foreach (var id in _research)
        {
            if (!Data.HasTech(id) || Me.Known[Data.Tech(id).Index]) continue;
            if (S.ResearchProblem(_p, id) != null) continue;
            Send(new ResearchCommand(_p, id));
            Say($"research {id}");
            return;
        }
    }

    // ---------------------------------------------------------------- the build order

    private OrderStep? Current => StepIndex < _order.Steps.Count ? _order.Steps[StepIndex] : null;

    private void Next(string? why = null)
    {
        var step = Current!;
        if (why != null) Say($"step {step.Number} ({step.Label}) skipped: {why}");
        StepIndex++;
        _stepRepeat = 0;
        _stepSince = _sim.Tick;
        _pending = null;
        _failures = 0;
    }

    private void Steps()
    {
        if (Current is not { } step) return;
        if (_pending != null)
        {
            if (!Confirm(_pending))
            {
                if (++_failures >= 5) Next("the sim refused the command 5 times");
                else _pending = null;
                return;
            }
            _pending = null;
            _failures = 0;
            if (++_stepRepeat >= step.Count) Next();
            return;
        }
        if (step.Kind != StepKind.Wait && step.Seconds > 0 && _sim.Tick - _stepSince > (long)step.Seconds * Simulation.TickRate)
        {
            Next($"still waiting after {step.Seconds} s ({(Waiting.Length > 0 ? Waiting : "condition not met")})");
            return;
        }
        Waiting = "";
        if (!Ready(step))
        {
            Waiting = "condition not met";
            return;
        }
        switch (step.Kind)
        {
            case StepKind.Build: Build(step); break;
            case StepKind.Upgrade: Upgrade(step); break;
            case StepKind.Unit: Order(step); break;
            case StepKind.Research:
                if (!_research.Contains(step.Target)) _research.Insert(0, step.Target);
                Next();
                break;
            case StepKind.FoundOutpost: Found(step); break;
            case StepKind.DropOutpost: Drop(step); break;
            case StepKind.BeamSupplies: Beam(step); break;
            case StepKind.Wait:
                if (_sim.Tick - _stepSince >= (long)step.Seconds * Simulation.TickRate) Next();
                break;
        }
    }

    /// <summary>The step's "after" ids exist and its "when" holds.</summary>
    private bool Ready(OrderStep step)
    {
        foreach (var a in step.After)
            if (!Exists(a)) return false;
        return step.When == null || step.When.Holds(Value);
    }

    /// <summary>A finished building of that type, a known tech, a unit of that type, or a reached milestone.</summary>
    public bool Exists(string id)
    {
        if (Data.HasTech(id)) return Me.Known[Data.Tech(id).Index];
        if (Data.Buildings.Any(b => b.Id == id)) return S.Structures.Any(s => s.Owner == _p && s.DefId == id && s.State == StructureState.Complete);
        if (Data.SurfaceUnits.Any(u => u.Id == id)) return Mine().Any(u => u.DefId == id);
        return id switch
        {
            "second_base" => S.Bases(_p).Count(b => b.State == StructureState.Complete) >= 2,
            "landed" => Me.Phase == LandingPhase.Landed,
            "hub_ready" => S.UplinkOnline(_p, _sim.Tick),
            _ => throw new FormatException($"after: unknown id '{id}'"),
        };
    }

    /// <summary>Values a "when" condition can read.</summary>
    public double? Value(string name)
    {
        if (name == "minute") return _sim.Tick / (double)Simulation.TicksPerMinute;
        if (name == "storage_free") return S.FreeStorage(_sim, _p).ToDouble();
        if (name == "storage_pct")
        {
            var cap = S.StorageCapacity(_p).ToDouble();
            return cap <= 0 ? 100 : 100 * (S.Stored(_sim, _p) + S.InFlight(_p)).ToDouble() / cap;
        }
        if (name == "power_free") return MainBase is { } b ? (S.PowerOf(b.Id).Supply - S.PowerOf(b.Id).Use).ToDouble() : 0;
        if (name.StartsWith("income:", StringComparison.Ordinal))
            return Data.Resources.FirstOrDefault(r => r.Id == name[7..]) is { } ri ? _sim.IncomePerMinute(_p)[ri.Index].ToDouble() : null;
        if (name.StartsWith("count:", StringComparison.Ordinal))
        {
            var id = name[6..];
            return S.Structures.Count(s => s.Owner == _p && s.DefId == id && s.State == StructureState.Complete) + Mine().Count(u => u.DefId == id);
        }
        return Data.Resources.FirstOrDefault(r => r.Id == name) is { } res ? Stock[res.Index].ToDouble() : null;
    }

    private bool Confirm(Pending p)
    {
        switch (p.Kind)
        {
            case StepKind.Build:
                var s = S.StructureAt(p.Tile);
                return s != null && s.Owner == _p && s.Id >= p.Before;
            case StepKind.Upgrade:
                var u = S.FindStructure(p.Id);
                return u != null && (u.UpgradingLevel > 0 || u.UpgradingTo != null || u.Level > p.Before);
            case StepKind.Unit:
                var bay = S.FindStructure(p.Id);
                return bay != null && bay.Production.Count > p.Before;
            case StepKind.DropOutpost:
                return S.Deliveries.Any(d => d.Owner == _p && d.Kind == DeliveryKind.Outpost) || S.Bases(_p).Count > p.Before;
            case StepKind.BeamSupplies:
                return Me.TransporterReadyTick > p.Tick;
            default:
                return true;
        }
    }

    // ---- builds

    private SurfaceStructure? TargetBase(OrderStep step, string buildingId)
    {
        var bases = S.Bases(_p).Where(b => b.State == StructureState.Complete).ToList();
        if (bases.Count == 0) return null;
        if (step.Base == "main") return bases[0];
        if (step.Base == "newest") return bases[^1];
        if (int.TryParse(step.Base, out var n)) return n >= 1 && n <= bases.Count ? bases[n - 1] : null;
        // "any": the oldest base with a free queue and a spot
        foreach (var b in bases)
            if (Busy(b) < Data.Building(b.DefId).BuildQueues && Spot(b, buildingId, step.Deposit, out _) >= 0) return b;
        return bases[0];
    }

    /// <summary>Unfinished buildings at a base (each takes a build queue, or waits for one).</summary>
    private int Busy(SurfaceStructure core) =>
        S.Structures.Count(s => s.BaseId == core.Id && s.Id != core.Id && s.State is StructureState.Queued or StructureState.Building);

    private void Build(OrderStep step)
    {
        if (!Data.BuildMenu.Contains(step.Target))
        {
            Next($"{step.Target} is not in the build menu");
            return;
        }
        var core = TargetBase(step, step.Target);
        if (core == null)
        {
            Waiting = "no such finished base";
            return;
        }
        if (!step.Queue && Busy(core) >= Data.Building(core.DefId).BuildQueues)
        {
            Waiting = "build queue busy";
            return;
        }
        var tile = Spot(core, step.Target, step.Deposit, out var short_);
        if (tile < 0 || short_) // no spot yet (surveying may find one), or can't afford it yet
        {
            Waiting = tile < 0 ? "no valid tile in range" : "can't afford it";
            return;
        }
        _pending = new Pending(StepKind.Build, tile, 0, NextStructureId(), _sim.Tick);
        Send(new PlaceBuildingCommand(_p, step.Target, tile, 0));
        Say($"place {step.Target} at {tile} (base {core.Id})");
    }

    private int NextStructureId() => S.Structures.Count == 0 ? 1 : S.Structures.Max(s => s.Id) + 1;

    /// <summary>
    /// The best tile for a building at this base: the sim's CheckPlacement must say None (or only CannotAfford, then
    /// <paramref name="cannotAfford"/> is set). Nearest the core first; ordinary buildings keep off deposits and off tiles
    /// crawlers are heading for; mines go on the wanted ore, the biggest deposit first.
    /// </summary>
    private int Spot(SurfaceStructure core, string buildingId, string deposit, out bool cannotAfford)
    {
        cannotAfford = false;
        var def = Data.Building(buildingId);
        var radius = Data.Building(core.DefId).BuildRadius;
        var heading = Mine().Where(u => u.Path.Count > 0).Select(u => u.Path[^1]).ToHashSet();
        var wanted = deposit.Length > 0 ? Data.Resources.FirstOrDefault(r => r.Id == deposit)?.Index ?? -2 : -1;
        int best = -1;
        (int, long, int) bestKey = (int.MaxValue, 0, 0);
        var bestShort = false;
        foreach (var t in Map.Grid.Within(core.Tile, radius))
        {
            if (S.BaseFor(_p, t) != core.Id || heading.Contains(t)) continue;
            var hasDeposit = Map.DepositResource[t] >= 0;
            if (def.RequiresDeposit && wanted >= 0 && Map.DepositResource[t] != wanted) continue;
            var problem = S.CheckPlacement(_sim, _p, buildingId, t);
            if (problem is not (PlacementProblem.None or PlacementProblem.CannotAfford)) continue;
            // rank: ordinary buildings off deposits first; mines on the biggest deposit; then nearest the core
            var pass = def.RequiresDeposit || def.RequiresDepleted ? 0 : hasDeposit ? 1 : 0;
            var size = def.RequiresDeposit ? -Map.DepositAmount[t].Raw : 0;
            var key = (pass, size, Map.Grid.Distance(core.Tile, t));
            if (best >= 0 && key.CompareTo(bestKey) >= 0) continue;
            best = t;
            bestKey = key;
            bestShort = problem == PlacementProblem.CannotAfford;
        }
        cannotAfford = bestShort;
        return best;
    }

    private void Upgrade(OrderStep step)
    {
        var mine = S.Structures.Where(s => s.Owner == _p && s.DefId == step.Target && s.State == StructureState.Complete).OrderBy(s => s.Id).ToList();
        if (mine.Count == 0)
        {
            Waiting = "none built yet";
            return;
        }
        var waiting = false;
        foreach (var s in mine)
        {
            var problem = S.UpgradeProblem(_sim, _p, s.Id);
            if (problem == null)
            {
                _pending = new Pending(StepKind.Upgrade, s.Tile, s.Id, s.Level, _sim.Tick);
                Send(new UpgradeCommand(_p, s.Id));
                Say($"upgrade {s.DefId} {s.Id} ({S.LevelName(s)} →)");
                return;
            }
            if (problem != "top level")
            {
                waiting = true;
                Waiting = problem;
            }
        }
        if (!waiting) Next("every one is at the top level");
    }

    private void Order(OrderStep step)
    {
        if (!Data.SurfaceUnits.Any(u => u.Id == step.Target))
        {
            Next($"unknown unit {step.Target}");
            return;
        }
        foreach (var bay in S.Structures.Where(s => s.Owner == _p && s.State == StructureState.Complete && Data.Building(s.DefId).Produces.Contains(step.Target)))
        {
            if (S.ProductionProblem(_sim, _p, bay.Id, step.Target) != null) continue;
            _pending = new Pending(StepKind.Unit, bay.Tile, bay.Id, bay.Production.Count, _sim.Tick);
            Send(new BuildUnitCommand(_p, bay.Id, step.Target));
            Say($"order {step.Target} at bay {bay.Id}");
            return;
        }
        Waiting = S.Structures.FirstOrDefault(s => s.Owner == _p && s.State == StructureState.Complete && Data.Building(s.DefId).Produces.Contains(step.Target)) is { } any
            ? S.ProductionProblem(_sim, _p, any.Id, step.Target) ?? "" : "no finished Vehicle Bay";
    }

    // ---- outposts and orbit

    private int _coresAtStep = -1;

    /// <summary>Found a second (or next) outpost with a construction rover: orders one at a bay if there is none, then
    /// sends it to the best surveyed site (SiteProblem None) just past the spacing rule. Done when a new core exists.</summary>
    private void Found(OrderStep step)
    {
        var cores = S.Bases(_p).Count;
        if (_coresAtStep < 0) _coresAtStep = cores;
        if (cores > _coresAtStep)
        {
            _coresAtStep = -1;
            Next();
            return;
        }
        var rovers = Mine().Where(u => Data.SurfaceUnit(u.DefId).CanFound).ToList();
        if (rovers.Any(u => u.FoundTile >= 0)) return; // on its way or unfolding
        var rover = rovers.FirstOrDefault(u => !u.Broken && u.Mode == UnitMode.Mobile && !_repairSent.ContainsKey(u.Id));
        if (rover == null)
        {
            if (rovers.Count > 0) return; // being repaired
            if (S.Structures.Any(s => s.Owner == _p && s.Production.Contains(step.Target))) return; // being built
            if (_roverOrderedTick >= 0 && _sim.Tick - _roverOrderedTick < 3 * Simulation.TickRate) return;
            foreach (var bay in S.Structures.Where(s => s.Owner == _p && s.State == StructureState.Complete && Data.Building(s.DefId).Produces.Contains(step.Target)))
                if (S.ProductionProblem(_sim, _p, bay.Id, step.Target) == null)
                {
                    _roverOrderedTick = _sim.Tick;
                    Send(new BuildUnitCommand(_p, bay.Id, step.Target));
                    Say($"order {step.Target} at bay {bay.Id} (to found an outpost)");
                    return;
                }
            return;
        }
        var rules = Data.SurfaceUnit(rover.DefId).Found!;
        var site = OutpostSite(rules.MinDistance, t => S.SiteProblem(_sim, _p, t, rules, rover) switch
        {
            FoundProblem.None => 0,
            FoundProblem.CannotAfford => 1,
            _ => 2,
        }, rover.Tile);
        if (site.Tile < 0 || site.Short)
        {
            Waiting = site.Tile < 0 ? "no surveyed site far enough out" : "can't afford the founding cost";
            return;
        }
        Send(new FoundOutpostCommand(_p, rover.Id, site.Tile));
        Say($"{rover.Name} {rover.Number}: found an outpost at {site.Tile}");
    }

    /// <summary>The best site at least minDistance from every base: surveyed deposits within 3 tiles and buildable ground
    /// within 4 score, distance costs. check: 0 = fine, 1 = fine but can't afford yet, 2 = no.</summary>
    private (int Tile, bool Short) OutpostSite(int minDistance, Func<int, int> check, int from)
    {
        var g = Map.Grid;
        var bases = S.Bases(_p);
        if (bases.Count == 0) return (-1, false);
        var surveyed = Me.Surveyed;
        var candidates = new List<(int Tile, int Score)>();
        var seen = new HashSet<int>();
        foreach (var b in bases)
            foreach (var t in g.Within(b.Tile, minDistance + 6))
            {
                if (!seen.Add(t) || !surveyed[t] || !Map.Terrain(t).Buildable) continue;
                var near = bases.Min(x => g.Distance(x.Tile, t));
                if (near < minDistance) continue;
                var score = 0;
                foreach (var x in g.Within(t, 4))
                {
                    if (!surveyed[x]) continue;
                    if (Map.DepositResource[x] >= 0 && Map.DepositAmount[x] > Fix64.Zero && g.Distance(t, x) <= 3) score += 6;
                    if (Map.Terrain(x).Buildable) score += 1;
                }
                score -= 2 * (near - minDistance);
                candidates.Add((t, score));
            }
        foreach (var (t, _) in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Tile))
        {
            var c = check(t);
            if (c == 2) continue;
            if (from >= 0 && Pathfinder.Find(Map, from, t) == null) continue;
            return (t, c == 1);
        }
        return (-1, false);
    }

    private void Drop(OrderStep step)
    {
        if (Data.Orbit is not { } orbit)
        {
            Next("no ship in orbit");
            return;
        }
        var site = OutpostSite(orbit.OutpostMinDistance, t => S.OutpostProblem(_sim, _p, t) switch
        {
            null => 0,
            "not enough resources" => 1,
            _ => 2,
        }, -1);
        if (site.Tile < 0 || site.Short)
        {
            Waiting = site.Tile < 0 ? "no site" : "can't afford the drop";
            return;
        }
        _pending = new Pending(StepKind.DropOutpost, site.Tile, 0, S.Bases(_p).Count, _sim.Tick);
        Send(new OutpostDropCommand(_p, site.Tile));
        Say($"outpost drop at {site.Tile}");
    }

    private void Beam(OrderStep step)
    {
        if (MainBase is not { } home) return;
        if (S.BeamProblem(_sim, _p, home.Tile, OrbitRules.Supplies) is { } problem)
        {
            if (problem.StartsWith("no transporters", StringComparison.Ordinal) || problem.Contains("can't beam") || problem == "the hold is empty") Next(problem);
            else Waiting = problem;
            return;
        }
        _pending = new Pending(StepKind.BeamSupplies, home.Tile, 0, 0, _sim.Tick);
        Send(new BeamCommand(_p, home.Tile, OrbitRules.Supplies));
        Say("beam supplies to the hub");
    }
}
