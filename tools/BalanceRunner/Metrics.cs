using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Balance;

/// <summary>A period when something held the economy back (seconds from the start).</summary>
public sealed record Stall(string Kind, double Start, double End)
{
    public double Seconds => End - Start;
}

/// <summary>Everything one run measured. Plain data: written as JSON and CSV, read back for reports and comparisons.</summary>
public sealed class RunResult
{
    public string Order { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Faction { get; set; } = "";
    public ulong Seed { get; set; }
    public double Minutes { get; set; }
    public long Ticks { get; set; }
    public double WallMs { get; set; }
    public double TicksPerSecond { get; set; }

    /// <summary>Game time per wall time (×).</summary>
    public double Speedup { get; set; }

    public string StateHash { get; set; } = "";
    public int LandingTile { get; set; } = -1;
    public double SampleSeconds { get; set; }

    /// <summary>Sample times in seconds; every series has one value per time.</summary>
    public List<double> Times { get; set; } = new();
    public Dictionary<string, List<double>> Series { get; set; } = new();

    /// <summary>Seconds from the start at which each milestone was first reached (missing = not reached).</summary>
    public Dictionary<string, double> Milestones { get; set; } = new();

    public List<Stall> Stalls { get; set; } = new();
    public Dictionary<string, double> StallSeconds { get; set; } = new();

    /// <summary>Building-seconds per building type per status (Working, LowPower, NoInput, StorageFull, NoUpkeep, Depleted, Broken).</summary>
    public Dictionary<string, Dictionary<string, double>> StatusSeconds { get; set; } = new();

    /// <summary>Crawler-seconds per state (mining, storage_full, no_destination, unfolding, driving, broken, repairing, idle).</summary>
    public Dictionary<string, double> CrawlerSeconds { get; set; } = new();

    /// <summary>Counts: flats, hazards, rovers_disabled, crawler_breakdowns, building_breakdowns, pods, pod_ore,
    /// maintenance_orders, weather_lost_vehicle_s, drops_grounded_s, ore_mined, metal_refined, parasteel_made.</summary>
    public Dictionary<string, double> Totals { get; set; } = new();

    public Dictionary<string, double> EndStock { get; set; } = new();
    public Dictionary<string, int> EndBuildings { get; set; } = new();
    public Dictionary<string, int> EndUnits { get; set; } = new();
    public List<string> EndTechs { get; set; } = new();
    public int StepsDone { get; set; }
    public int StepsTotal { get; set; }

    /// <summary>"" normally; otherwise the exception that stopped the run early (a sim bug), with the tick.</summary>
    public string Crash { get; set; } = "";

    /// <summary>Where the build order was at the end and why it waited ("" = finished).</summary>
    public string StuckOn { get; set; } = "";
    public List<BotEvent> Log { get; set; } = new();
}

/// <summary>
/// Watches a run tick by tick (after each <c>sim.Step()</c>): accumulates per-tick counters (building statuses, crawler
/// states, weather slow-down, events) and every second checks milestones and stalls; every SampleSeconds it samples
/// stocks, income, storage, power, buildings, units and research into the series.
/// </summary>
public sealed class Recorder
{
    private readonly Simulation _sim;
    private readonly int _p;
    private readonly Bot _bot;
    private readonly int _sampleTicks;
    public RunResult Result { get; }

    private readonly List<ResourceDef> _resources;
    private readonly HashSet<int> _pods = new();
    private readonly Dictionary<int, (int Resource, Fix64 Amount)> _podLoad = new();
    private readonly HashSet<int> _brokenBuildings = new();
    private readonly HashSet<int> _disabledRovers = new();
    private readonly Dictionary<string, (double Start, bool Open)> _open = new();
    private double _weatherLost;
    private const double Dt = 1.0 / Simulation.TickRate;

    public static readonly string[] StallKinds = { "storage_full", "mining_blocked", "refinery_idle", "power_short", "building_broken" };

    public Recorder(Simulation sim, int player, Bot bot, int sampleSeconds, RunResult result)
    {
        _sim = sim;
        _p = player;
        _bot = bot;
        _sampleTicks = System.Math.Max(1, sampleSeconds) * Simulation.TickRate;
        Result = result;
        Result.SampleSeconds = sampleSeconds;
        var hud = sim.Data.HudResources.ToList();
        _resources = hud;
        foreach (var k in StallKinds) Result.StallSeconds[k] = 0;
        foreach (var k in new[] { "flats", "hazards", "rovers_disabled", "crawler_breakdowns", "building_breakdowns", "pods", "pod_ore", "maintenance_orders", "weather_lost_vehicle_s", "drops_grounded_s" })
            Result.Totals[k] = 0;
    }

    private PlanetSurface S => _sim.Surface!;
    private GameData Data => _sim.Data;
    private double Now => _sim.Tick * Dt;

    /// <summary>Call once after every step.</summary>
    public void AfterStep()
    {
        var last = _sim.Tick - 1; // the tick just stepped
        var me = S.Players[_p];
        if (me.Phase != LandingPhase.Landed)
        {
            if (_sim.Tick % _sampleTicks == 0) Sample();
            return;
        }

        foreach (var s in S.Structures)
        {
            if (s.Owner != _p || s.State != StructureState.Complete) continue;
            var def = Data.Building(s.DefId);
            if (!Result.StatusSeconds.TryGetValue(s.DefId, out var bag)) Result.StatusSeconds[s.DefId] = bag = new Dictionary<string, double>();
            var key = s.Status.ToString();
            bag[key] = bag.GetValueOrDefault(key) + Dt;
            if (s.Broken && _brokenBuildings.Add(s.Id)) Result.Totals["building_breakdowns"]++;
            if (!s.Broken) _brokenBuildings.Remove(s.Id);
            if (s.MaintenanceOrdered && s.MaintenanceOrderTick == last) Result.Totals["maintenance_orders"]++;
        }

        foreach (var u in S.Units)
        {
            if (u.Owner != _p) continue;
            var def = Data.SurfaceUnit(u.DefId);
            if (def.CanMine)
            {
                var state = u.Broken && S.IsRepairing(u) ? "repairing"
                    : u.Broken ? "broken"
                    : S.IsRepairing(u) ? "repairing"
                    : u.Mode == UnitMode.Deployed ? u.MiningStatus switch
                    {
                        MiningStatus.Mining => "mining",
                        MiningStatus.StorageFull => "storage_full",
                        MiningStatus.NoDestination => "no_destination",
                        _ => "deployed_other",
                    }
                    : u.Mode is UnitMode.Deploying or UnitMode.Packing ? "unfolding"
                    : u.Path.Count > 0 ? "driving" : "idle";
                Result.CrawlerSeconds[state] = Result.CrawlerSeconds.GetValueOrDefault(state) + Dt;
                if (u.BrokeAtTick == last) Result.Totals["crawler_breakdowns"]++;
            }
            if (def.Damage != null)
            {
                if (u.FlatAtTick == last) Result.Totals["flats"]++;
                if (u.HazardAtTick == last) Result.Totals["hazards"]++;
                if (S.Disabled(u) && _disabledRovers.Add(u.Id)) Result.Totals["rovers_disabled"]++;
                if (!S.Disabled(u)) _disabledRovers.Remove(u.Id);
            }
            if (u.Path.Count > 0 && u.Mode == UnitMode.Mobile)
            {
                var lost = 1 - S.WeatherSpeed(u).ToDouble();
                if (lost > 0) _weatherLost += lost * Dt;
            }
        }
        Result.Totals["weather_lost_vehicle_s"] = _weatherLost;

        // Pods: count the ones that landed since last tick.
        var now = new HashSet<int>();
        foreach (var pod in S.Pods)
            if (pod.Owner == _p)
            {
                now.Add(pod.Id);
                if (_pods.Add(pod.Id)) _podLoad[pod.Id] = (pod.Resource, pod.Amount);
            }
        foreach (var id in _pods.Where(id => !now.Contains(id)).ToList())
        {
            _pods.Remove(id);
            Result.Totals["pods"]++;
            Result.Totals["pod_ore"] += _podLoad[id].Amount.ToDouble();
            _podLoad.Remove(id);
            Milestone("first_pod");
        }

        if (_sim.Tick % Simulation.TickRate == 0) EverySecond();
        if (_sim.Tick % _sampleTicks == 0) Sample();
    }

    private void Milestone(string key)
    {
        if (!Result.Milestones.ContainsKey(key)) Result.Milestones[key] = System.Math.Round(Now, 1);
    }

    private void EverySecond()
    {
        var me = S.Players[_p];
        Milestone("landed");
        if (S.UplinkOnline(_p, _sim.Tick)) Milestone("hub_ready");
        if (S.Deliveries.Count == 0 && S.UplinkOnline(_p, _sim.Tick) && Climate.Effects(Data, S.Map, me.LandingTile, _sim.Tick).DropShipsGrounded)
            Result.Totals["drops_grounded_s"] += 1;
        var cores = 0;
        var refineries = 0;
        var refineriesIdle = 0;
        var anyBroken = false;
        var miningBlocked = false;
        foreach (var s in S.Structures)
        {
            if (s.Owner != _p) continue;
            var def = Data.Building(s.DefId);
            if (s.State != StructureState.Complete) continue;
            if (def.IsCore)
            {
                if (s.Id == s.BaseId) cores++;
                if (def.Level >= 2) Milestone("core:" + s.DefId);
            }
            else Milestone("built:" + s.DefId);
            if (s.Level >= 2) Milestone("mk2");
            if (s.Level >= 3) Milestone("mk3");
            if (s.Level >= 2) Milestone($"mk{s.Level}:{s.DefId}");
            if (s.DefId == "refinery")
            {
                refineries++;
                if (s.Status == StructureStatus.NoInput) refineriesIdle++;
                if (s.Status is StructureStatus.Working or StructureStatus.LowPower) Milestone("first_metal");
            }
            if (s.Broken) anyBroken = true;
            if (def.IsMine && s.Status == StructureStatus.StorageFull) miningBlocked = true;
        }
        if (cores >= 2) Milestone("second_base");
        foreach (var u in S.Units)
        {
            if (u.Owner != _p) continue;
            if (Data.SurfaceUnit(u.DefId).CanMine && u.Mode == UnitMode.Deployed)
            {
                if (u.MiningStatus == MiningStatus.Mining) Milestone("crawler_mining");
                if (u.MiningStatus is MiningStatus.StorageFull or MiningStatus.NoDestination) miningBlocked = true;
            }
            if (u.BuiltAtTick >= 0) Milestone("unit:" + u.DefId);
        }
        foreach (var t in Data.Techs)
            if (me.Known[t.Index]) Milestone("research:" + t.Id);
        if (Data.Techs.Count > 0 && Data.Techs.All(t => t.Optional || me.Known[t.Index])) Milestone("research_all");
        var parasteel = Data.Resources.FirstOrDefault(r => r.Id == "parasteel");
        if (parasteel != null && _sim.Players[_p].Stock[parasteel.Index] > Fix64.Zero) Milestone("first_parasteel");
        if (_bot.StepIndex >= Result.StepsTotal && Result.StepsTotal > 0) Milestone("order_done");

        var ready = S.UplinkOnline(_p, _sim.Tick);
        var storageFull = ready && S.FreeStorage(_sim, _p) < Fix64.One;
        if (storageFull) Milestone("storage_full");
        var cap = S.StorageCapacity(_p);
        if (ready && cap > Fix64.Zero && (S.Stored(_sim, _p) + S.InFlight(_p)) * Fix64.FromInt(5) >= cap * Fix64.FromInt(4)) Milestone("storage_80");
        var powerShort = S.Bases(_p).Any(b => b.State == StructureState.Complete && S.PowerOf(b.Id).Efficiency < Fix64.One);
        // The first time a base draws more power than its own core supplies: a reactor (or pausing something) is now needed.
        if (S.Bases(_p).Any(b => b.State == StructureState.Complete && S.PowerOf(b.Id).Use > Data.Building(b.DefId).PowerSupply)) Milestone("power_needed");
        Track("storage_full", storageFull);
        Track("mining_blocked", ready && miningBlocked);
        Track("refinery_idle", refineries > 0 && refineriesIdle == refineries);
        Track("power_short", powerShort);
        Track("building_broken", anyBroken);
    }

    private void Track(string kind, bool on)
    {
        var (start, open) = _open.GetValueOrDefault(kind);
        if (on)
        {
            Result.StallSeconds[kind] += 1;
            if (!open) _open[kind] = (Now - 1, true);
        }
        else if (open)
        {
            Result.Stalls.Add(new Stall(kind, start, Now - 1));
            _open[kind] = (0, false);
        }
    }

    private void Put(string name, double value)
    {
        if (!Result.Series.TryGetValue(name, out var list))
        {
            Result.Series[name] = list = new List<double>();
            while (list.Count < Result.Times.Count - 1) list.Add(0); // a series that starts late (a new base) is 0 before
        }
        list.Add(System.Math.Round(value, 3));
    }

    /// <summary>One row of the series.</summary>
    public void Sample()
    {
        Result.Times.Add(System.Math.Round(Now, 2));
        var stock = _sim.Players[_p].Stock;
        var landed = S.Players[_p].Phase == LandingPhase.Landed;
        var income = _sim.IncomePerMinute(_p);
        foreach (var r in _resources)
        {
            Put("stock:" + r.Id, stock[r.Index].ToDouble());
            Put("income:" + r.Id, landed ? income[r.Index].ToDouble() : 0);
        }
        var cap = S.StorageCapacity(_p).ToDouble();
        var used = S.Stored(_sim, _p).ToDouble();
        var flight = S.InFlight(_p).ToDouble();
        Put("storage_used", used);
        Put("storage_cap", cap);
        Put("storage_in_flight", flight);
        Put("storage_pct", cap > 0 ? 100 * (used + flight) / cap : 0);
        var bases = S.Bases(_p);
        Put("bases", bases.Count(b => b.State == StructureState.Complete));
        for (var i = 0; i < bases.Count; i++)
        {
            var power = S.PowerOf(bases[i].Id);
            Put($"power_supply:{i + 1}", power.Supply.ToDouble());
            Put($"power_use:{i + 1}", power.Use.ToDouble());
        }
        var mine = S.Structures.Where(s => s.Owner == _p).ToList();
        Put("buildings", mine.Count(s => s.State == StructureState.Complete));
        Put("buildings_unfinished", mine.Count(s => s.State is StructureState.Queued or StructureState.Building));
        Put("buildings_idle", mine.Count(s => s.State == StructureState.Complete && s.Status is not (StructureStatus.Working or StructureStatus.LowPower)));
        Put("buildings_broken", mine.Count(s => s.Broken));
        var units = S.Units.Where(u => u.Owner == _p).ToList();
        Put("units", units.Count);
        Put("crawlers", units.Count(u => Data.SurfaceUnit(u.DefId).CanMine));
        Put("crawlers_mining", units.Count(u => u.Mode == UnitMode.Deployed && u.MiningStatus == MiningStatus.Mining));
        var rovers = units.Where(u => Data.SurfaceUnit(u.DefId).Damage != null).ToList();
        Put("rover_integrity_avg", rovers.Count == 0 ? 0 : rovers.Average(u => u.Integrity.ToDouble()));
        Put("rover_integrity_min", rovers.Count == 0 ? 0 : rovers.Min(u => u.Integrity.ToDouble()));
        Put("flats_total", Result.Totals["flats"]);
        Put("weather_lost_s", _weatherLost);
        var me = S.Players[_p];
        Put("research_known", Data.Techs.Count(t => me.Known[t.Index]));
        Put("research_active_pct", me.ActiveTech >= 0 ? 100 * (me.ResearchProgress[me.ActiveTech] / Data.Techs[me.ActiveTech].Cost).ToDouble() : 0);
        Put("research_per_min", S.ResearchPerMinute(_p).ToDouble());
        Put("surveyed_tiles", me.Surveyed.Count(x => x));
        foreach (var list in Result.Series.Values)
            while (list.Count < Result.Times.Count) list.Add(0); // e.g. a base's power after it was gone
    }

    /// <summary>Close open stalls and fill the end-of-run fields.</summary>
    public void Finish()
    {
        foreach (var (kind, (start, open)) in _open.ToList())
            if (open) Result.Stalls.Add(new Stall(kind, start, Now));
        Result.Stalls.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : string.CompareOrdinal(a.Kind, b.Kind));
        var stock = _sim.Players[_p].Stock;
        foreach (var r in Data.Resources)
            if (stock[r.Index] != Fix64.Zero) Result.EndStock[r.Id] = System.Math.Round(stock[r.Index].ToDouble(), 2);
        foreach (var s in S.Structures.Where(s => s.Owner == _p && s.State == StructureState.Complete).OrderBy(s => s.Id))
        {
            var key = s.Level >= 2 ? $"{s.DefId} Mk{s.Level}" : s.DefId;
            Result.EndBuildings[key] = Result.EndBuildings.GetValueOrDefault(key) + 1;
        }
        foreach (var u in S.Units.Where(u => u.Owner == _p).OrderBy(u => u.Id))
            Result.EndUnits[u.DefId] = Result.EndUnits.GetValueOrDefault(u.DefId) + 1;
        Result.EndTechs = Data.Techs.Where(t => S.Players[_p].Known[t.Index]).Select(t => t.Id).ToList();
        var mined = Fix64.Zero;
        for (var t = 0; t < S.Map.Grid.Count; t++)
            if (S.Map.DepositResource[t] >= 0) mined += S.Map.DepositStart[t] - S.Map.DepositAmount[t];
        Result.Totals["ore_mined"] = System.Math.Round(mined.ToDouble(), 1);
        Result.StepsDone = _bot.StepIndex;
        Result.StuckOn = _bot.StuckOn;
        Result.Log = _bot.Log.ToList();
    }
}
