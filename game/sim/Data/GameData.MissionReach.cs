using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// "Can every mission be done?" (owner 2026-11-17, after Wings asked for scout drones whose Power Coupling needs Parsteel
// while the Alloy Foundry was only Wings' reward: "We need to make sure that missions can be accomplished.")
//
// A check on the data alone, part of Validate(). For every colony chain it walks the main missions in file order, the way
// a campaign plays them, keeping what the player has by then:
//   - the buildings they may place: surfaces start_menu plus every unlock_buildings so far (a stage's on_enter counts
//     from that stage on, a mission's rewards from its end; what only some branch gives doesn't count);
//   - one-off stock: the landing's start_stock plus every grant so far, minus what earlier objectives used up;
//   - from those, what can be had without limit (MissionEconomy): the colony hub's trickle, the deposits every starting
//     world has (with a mine or a crawler to dig them), what the open converters, habitats, the hub workshop and an open
//     fabricator make (recipe inputs followed all the way down), the vehicles the landing brings and an open bay or pad
//     makes, the techs an open lab can research.
// Then every objective that needs a building, a unit, an item, an amount of a resource or a tech must be within reach,
// costs included. A side job is checked at the earliest point of the chain its trigger can hold (it can be offered
// then, so it must be doable then). Problems read "Mission Wings can't be done (objective "Fabricate 2 scout drones"):
// Scout Drone needs Power Coupling, which needs Parsteel; nothing unlocked by then makes Parsteel (…)".
//
// Not covered: rates and time (timers, how long a trickle takes), storage room, power, the map (whether a site can be
// driven to, comms range), luck (weather, ruins on this world), a ship's start bonus (the walk assumes none), and
// stock the player wastes. A space chain (no colony) only gets ValidateSpaceReach below.
//
// Fit-outs (data/missions/fit_out.json): a fitted-out ship lands with its hold's metal and fuel instead of the landing's
// own, so each colony chain is walked a second time with the leanest stock any fit-out can give (every cargo line at
// its minimum, none of its extras: LeanestFitOutStock); what that walk finds is reported as "With the leanest fit-out …".
public sealed partial class GameData
{
    /// <summary>The problems of the mission reach check alone (Validate() includes them): empty = every mission of every
    /// chain can be finished with what is unlocked by then.</summary>
    public IReadOnlyList<string> MissionReachProblems()
    {
        var problems = new List<string>();
        ValidateMissionReach(problems);
        return problems;
    }

    private void ValidateMissionReach(List<string> problems)
    {
        if (Missions is not { } data) return;
        foreach (var chain in data.Chains)
        {
            if (chain.Space) ValidateSpaceReach(data, chain, problems);
            else new MissionReach(this, data, chain, problems).Walk();
        }
        // The same walk on the leanest landing stock a fit-out can give (only when the usual walk is clean, so nothing is said twice).
        if (problems.Count > 0 || LeanestFitOutStock() is not { } lean) return;
        var found = new List<string>();
        foreach (var chain in data.Chains)
            if (!chain.Space) new MissionReach(this, data, chain, found, lean).Walk();
        foreach (var p in found) problems.Add($"With the leanest fit-out (every cargo line at its minimum): {p}");
    }

    /// <summary>The landing stock of the leanest fit-out the rules allow: the landing's start stock with the replaced
    /// resources (duranium, tritanium, deuterium) at what the cargo lines' minimums give, and nothing else from the hold.
    /// Null when the data set has no fit-outs.</summary>
    public IReadOnlyList<ResourceAmount>? LeanestFitOutStock()
    {
        if (FitOut is not { } f) return null;
        var list = new List<ResourceAmount>();
        foreach (var a in Landing.StartStock)
            if (!f.ReplacesStartStock.Contains(Resources[a.Resource].Id)) list.Add(a);
        foreach (var res in f.ReplacesStartStock)
        {
            if (!_resources.TryGetValue(res, out var def)) continue;
            var amount = 0;
            foreach (var l in f.Cargo)
                foreach (var s in l.Stock)
                    if (s.Resource == res) amount += s.Amount * System.Math.Max(0, l.Min);
            list.Add(new ResourceAmount(def.Index, Fix64.FromInt(amount)));
        }
        return list;
    }

    /// <summary>A space chain's ship never lands, so it has no income: the body satellites and observation posts its
    /// objectives ask for must fit the rules' limits and be paid for out of the stock every player starts with. (A data
    /// set without star-system rules or without civilizations has no such costs; those documents are optional.)
    /// Fit-outs (phase 2): a ship that never lands keeps the start stock whatever it loads, and its hold can only add body
    /// satellites and posts that cost nothing; the leanest legal load carries the space lines' minimums of them (0 today,
    /// FitOutRules.FreeSystemSatellites / FreePosts of that load), so the walk below with no fit-out (every one paid for) is
    /// already the hardest case and a second walk with the leanest load could never find more.</summary>
    private void ValidateSpaceReach(MissionData data, MissionChainDef chain, List<string> problems)
    {
        var satellites = 0;
        var posts = 0;
        var w = $"Mission chain {chain.Name}";
        void Look(MissionDef m, MissionCondDef c)
        {
            foreach (var child in c.Children) Look(m, child);
            if (c.Kind == "system_satellites")
            {
                var n = c.I("count", 1);
                satellites = System.Math.Max(satellites, n);
                if (SystemRules != null && n > SystemRules.SatelliteMax)
                    problems.Add($"Mission {m.Title} can't be done: it asks for {n} body satellites, and a player may have {SystemRules.SatelliteMax} (maps system satellite max).");
            }
            else if (c.Kind == "contact" && c.F("stage") == "post") posts = System.Math.Max(posts, 1);
        }
        foreach (var m in data.List)
        {
            if (m.Chain != chain.Id || !MissionAvailable(m)) continue;
            foreach (var s in m.Stages)
                foreach (var o in s.Objectives)
                    if (!o.Optional) Look(m, o.Cond);
        }
        var need = new Fix64[Resources.Count];
        if (SystemRules is { } sr)
            foreach (var a in sr.SatelliteCost) need[a.Resource] += a.Amount * satellites;
        if (Civilizations is { } civ)
            foreach (var a in civ.Post.Cost) need[a.Resource] += a.Amount * posts;
        for (var r = 0; r < need.Length; r++)
        {
            if (need[r] <= Fix64.Zero) continue;
            var have = Fix64.Zero;
            foreach (var a in Landing.StartStock)
                if (a.Resource == r) have += a.Amount;
            if (have < need[r])
                problems.Add($"{w} can't be done: its {satellites} body satellite(s) and {posts} observation post(s) cost {need[r]} {Resources[r].Name}, " +
                             $"and a ship that never lands starts with {have} and earns none.");
        }
    }

    /// <summary>What a player has at one point of a chain, as far as the data can tell: the buildings they may place,
    /// their one-off stock (landing stock + grants, by resource index) and the flags set.</summary>
    private sealed class ReachState
    {
        public HashSet<string> Open = new(StringComparer.Ordinal);
        public Dictionary<int, Fix64> Stock = new();
        public HashSet<string> Flags = new(StringComparer.Ordinal);

        public ReachState Copy() => new() { Open = new(Open, StringComparer.Ordinal), Stock = new(Stock), Flags = new(Flags, StringComparer.Ordinal) };

        /// <summary>Keeps only what both have (two ways to get here: only what both give is sure).</summary>
        public void Meet(ReachState o)
        {
            Open.IntersectWith(o.Open);
            Flags.IntersectWith(o.Flags);
            foreach (var r in Stock.Keys.ToList())
                Stock[r] = Fix64.Min(Stock[r], o.Stock.TryGetValue(r, out var v) ? v : Fix64.Zero);
        }

        public bool Same(ReachState o) =>
            Open.SetEquals(o.Open) && Flags.SetEquals(o.Flags) && Stock.Count == o.Stock.Count && Stock.All(kv => o.Stock.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    /// <summary>What can be had without limit in a state: resources (Made, by index), buildings that can be put up,
    /// units the player has or can make, techs a lab can reach.</summary>
    private sealed class MissionEconomy
    {
        public bool[] Made = Array.Empty<bool>();
        public HashSet<string> Buildable = new(StringComparer.Ordinal);
        public HashSet<string> Units = new(StringComparer.Ordinal);
        public HashSet<string> Built = new(StringComparer.Ordinal); // the ones an open bay can build more of
        public HashSet<string> Techs = new(StringComparer.Ordinal);
        public bool Lab, Fabricator, Extractor;
    }

    private sealed class MissionReach
    {
        private readonly GameData _d;
        private readonly MissionData _data;
        private readonly MissionChainDef _chain;
        private readonly List<string> _problems;
        private readonly IReadOnlyList<ResourceAmount> _startStock; // the landing's, or a fit-out's leanest
        private readonly List<MissionDef> _mains, _sides;
        private readonly List<BuildingDef> _surface; // every building a colony could ever place or upgrade to
        private readonly BuildingDef? _hub;
        private readonly HashSet<string> _cores = new(StringComparer.Ordinal); // the landing hub and what it upgrades to
        private readonly HashSet<int> _deposits = new(); // resources every starting world has deposits of
        private readonly int _power;
        private readonly HashSet<string> _done = new(StringComparer.Ordinal); // main missions over by this point
        private string _running = ""; // the main mission being walked
        private Dictionary<int, Fix64> _spent = new(); // one-off stock earlier objectives used up

        public MissionReach(GameData d, MissionData data, MissionChainDef chain, List<string> problems, IReadOnlyList<ResourceAmount>? startStock = null)
        {
            _d = d;
            _startStock = startStock ?? d.Landing.StartStock;
            _data = data;
            _chain = chain;
            _problems = problems;
            _mains = data.List.Where(m => m.Chain == chain.Id && m.IsMain && d.MissionAvailable(m)).ToList();
            _sides = data.List.Where(m => m.Chain == chain.Id && !m.IsMain && d.MissionAvailable(m)).ToList();
            _power = d._resources.TryGetValue(d.PowerResource, out var p) ? p.Index : -1;
            d._buildings.TryGetValue(d.Landing.HubBuilding, out _hub);
            for (var core = _hub; core != null && _cores.Add(core.Id);)
                core = core.UpgradesTo.Length > 0 && d._buildings.TryGetValue(core.UpgradesTo, out var next) ? next : null;
            var menu = d.EveryBuildMenu;
            _surface = d.Buildings.Where(b => menu.Contains(b.Id) || _cores.Contains(b.Id)).ToList();
            var first = true;
            foreach (var s in d.Surfaces)
            {
                var here = s.Deposits.Where(r => d._resources.ContainsKey(r.ResourceId)).Select(r => d._resources[r.ResourceId].Index).ToHashSet();
                if (first) _deposits.UnionWith(here);
                else _deposits.IntersectWith(here);
                first = false;
            }
        }

        // ------------------------------------------------------------ the walk

        public void Walk()
        {
            var state = new ReachState();
            foreach (var id in _d.StartMenu) state.Open.Add(id);
            foreach (var a in _startStock) state.Stock[a.Resource] = (state.Stock.TryGetValue(a.Resource, out var v) ? v : Fix64.Zero) + a.Amount;
            var pending = new List<MissionDef>(_sides);
            CheckSides(pending, state, _mains.Count > 0 ? $"from the start, before {_mains[0].Title}" : "from the start");
            foreach (var m in _mains)
            {
                _running = m.Id;
                if (m.Trigger != null && Lacks(m, m.Trigger, Solve(state), state, new Dictionary<int, Fix64>(_spent), trigger: true) is { Count: > 0 } never)
                    _problems.Add($"Mission {m.Title} can never start: {never[0]}");
                state = WalkStages(m, state, _spent, "", (s, at) => CheckSides(pending, at, $"while {m.Title} runs (stage \"{s.Title}\")"));
                _done.Add(m.Id);
                _running = "";
                CheckSides(pending, state, $"once {m.Title} is over");
            }
            foreach (var side in pending)
            {
                var why = side.Trigger != null ? Lacks(side, side.Trigger, Solve(state), state, new Dictionary<int, Fix64>(_spent), trigger: true) : new List<string>();
                _problems.Add($"Side job {side.Title} can never be offered: {(why.Count > 0 ? why[0] : "its trigger never holds")}");
            }
        }

        /// <summary>Side jobs whose trigger can hold from this point on: each must be doable with what the player has here.</summary>
        private void CheckSides(List<MissionDef> pending, ReachState state, string when)
        {
            var eco = Solve(state);
            foreach (var side in pending.ToList())
            {
                if (side.Trigger != null && Lacks(side, side.Trigger, eco, state, new Dictionary<int, Fix64>(_spent), trigger: true).Count > 0) continue;
                pending.Remove(side);
                WalkStages(side, state.Copy(), new Dictionary<int, Fix64>(_spent), $" when it is first offered ({when})", null);
            }
        }

        /// <summary>Checks every objective of the mission's stages, each with the state the player surely has on reaching
        /// it, and returns the state once the mission is over (whichever way it ends).</summary>
        private ReachState WalkStages(MissionDef m, ReachState start, Dictionary<int, Fix64> spent, string when, Action<MissionStageDef, ReachState>? atStage)
        {
            var n = m.Stages.Count;
            if (n == 0) return start;
            var into = new ReachState?[n];
            var outOf = new ReachState?[n];
            into[0] = start;
            for (var round = 0; round <= n + 1; round++)
            {
                var changed = false;
                for (var i = 0; i < n; i++)
                {
                    if (i > 0)
                    {
                        ReachState? met = null;
                        for (var j = 0; j < n; j++)
                        {
                            if (outOf[j] == null || !Exits(m.Stages[j]).Contains(m.Stages[i].Id)) continue;
                            if (met == null) met = outOf[j]!.Copy();
                            else met.Meet(outOf[j]!);
                        }
                        if (met == null) continue;
                        if (into[i] == null || !into[i]!.Same(met)) changed = true;
                        into[i] = met;
                    }
                    var after = into[i]!.Copy();
                    Apply(after, m.Stages[i].OnEnter);
                    if (outOf[i] == null || !outOf[i]!.Same(after)) changed = true;
                    outOf[i] = after;
                }
                if (!changed) break;
            }
            var kind = m.IsMain ? "Mission" : "Side job";
            ReachState? end = null;
            for (var i = 0; i < n; i++)
            {
                if (outOf[i] is not { } here) continue; // a stage nothing leads to (Validate reports it)
                var s = m.Stages[i];
                atStage?.Invoke(s, here);
                var eco = Solve(here, spent);
                foreach (var o in s.Objectives)
                {
                    if (o.Optional) continue;
                    foreach (var lack in Lacks(m, o.Cond, eco, here, spent, trigger: false))
                        _problems.Add($"{kind} {m.Title} can't be done{when} (objective \"{o.Text}\"): {lack}");
                }
                foreach (var exit in Exits(s))
                {
                    if (exit is not (MissionComplete or MissionFail)) continue;
                    var over = here.Copy();
                    if (exit == MissionComplete) Apply(over, m.Rewards);
                    if (end == null) end = over;
                    else end.Meet(over);
                }
            }
            return end ?? start;
        }

        private void Apply(ReachState state, IEnumerable<MissionActionDef> actions)
        {
            foreach (var a in actions)
                switch (a.Kind)
                {
                    case "unlock_buildings":
                        foreach (var id in UnlockIds(a)) state.Open.Add(id);
                        break;
                    case "grant":
                        foreach (var (res, amount) in a.Amounts)
                            if (_d._resources.TryGetValue(res, out var r) && amount > Fix64.Zero)
                                state.Stock[r.Index] = (state.Stock.TryGetValue(r.Index, out var v) ? v : Fix64.Zero) + amount;
                        break;
                    case "set_flag":
                        if (a.F("name") is { } flag) state.Flags.Add(flag);
                        break;
                }
        }

        // ------------------------------------------------------------ what can be had

        private Fix64 Left(ReachState state, Dictionary<int, Fix64> spent, int r) =>
            (state.Stock.TryGetValue(r, out var have) ? have : Fix64.Zero) - (spent.TryGetValue(r, out var used) ? used : Fix64.Zero);

        private MissionEconomy Solve(ReachState state) => Solve(state, _spent);

        private MissionEconomy Solve(ReachState state, Dictionary<int, Fix64> spent)
        {
            var e = new MissionEconomy { Made = new bool[_d.Resources.Count] };
            bool Affordable(IEnumerable<ResourceAmount> cost) =>
                cost.All(a => a.Amount <= Fix64.Zero || a.Resource == _power || e.Made[a.Resource] || Left(state, spent, a.Resource) >= a.Amount);
            foreach (var u in _d.Landing.Units) e.Units.Add(u);
            var f = _d.Fabrication;
            for (var changed = true; changed;)
            {
                changed = false;
                void Make(int r)
                {
                    if (r == _power || e.Made[r]) return;
                    e.Made[r] = true;
                    changed = true;
                }
                // Techs an open lab can research.
                e.Lab = e.Buildable.Any(id => _d._buildings[id].IsLab);
                if (e.Lab)
                    foreach (var t in _d.Techs)
                        if (!e.Techs.Contains(t.Id) && t.Requires.All(e.Techs.Contains))
                        {
                            e.Techs.Add(t.Id);
                            changed = true;
                        }
                // Buildings that can be put up: open (or an upgrade of the landing hub), researched, affordable.
                foreach (var b in _surface)
                {
                    if (e.Buildable.Contains(b.Id) || !(state.Open.Contains(b.Id) || _cores.Contains(b.Id))) continue;
                    if (b.RequiresTech.Length > 0 && !e.Techs.Contains(b.RequiresTech)) continue;
                    if (b != _hub && !Affordable(b.Cost)) continue; // the landing hub stands already
                    e.Buildable.Add(b.Id);
                    changed = true;
                }
                // What they make: output (while their upkeep can be had), recipes whose inputs can be had.
                foreach (var b in _surface)
                {
                    if (!e.Buildable.Contains(b.Id)) continue;
                    if (b.UpkeepPerMinute.All(a => a.Resource == _power || e.Made[a.Resource]))
                        foreach (var a in b.OutputPerMinute) Make(a.Resource);
                    foreach (var recipe in b.Recipes)
                        if (recipe.In.All(a => e.Made[a.Resource]))
                            foreach (var a in recipe.Out) Make(a.Resource);
                    if (b.RequiresDeposit && b.IsMine && !b.RequiresDepleted) e.Extractor = true;
                    foreach (var unit in b.Produces)
                        if (!e.Built.Contains(unit) && _d._surfaceUnits.TryGetValue(unit, out var u) && Affordable(u.Cost))
                        {
                            e.Built.Add(unit);
                            e.Units.Add(unit);
                            changed = true;
                        }
                }
                // Drones: the packed item in stock and a pad to unpack it.
                foreach (var drone in _d.Drones)
                    if (!e.Units.Contains(drone.UnitId) && HasPad(e) && _d._resources.TryGetValue(drone.UnitId, out var item)
                        && (e.Made[item.Index] || Left(state, spent, item.Index) >= Fix64.One))
                    {
                        e.Units.Add(drone.UnitId);
                        changed = true;
                    }
                // Deposits: every starting world has these, and something digs them.
                e.Extractor |= e.Units.Any(id => _d._surfaceUnits.TryGetValue(id, out var u) && u.CanMine);
                if (e.Extractor)
                    foreach (var r in _deposits) Make(r);
                // The parts economy: an open fabricator makes any recipe whose inputs can be had; the hub's workshop makes
                // its kits straight from materials (the tree flattened).
                e.Fabricator = e.Buildable.Any(id => _d._buildings[id].Fabricator != null);
                if (f != null)
                {
                    if (e.Fabricator)
                        foreach (var recipe in f.Recipes)
                            if (recipe.In.All(a => e.Made[a.Resource]) && _d._resources.TryGetValue(recipe.Id, out var made)) Make(made.Index);
                    foreach (var id in f.Hub.Recipes)
                        if (_d._resources.TryGetValue(id, out var kit) && _d.Flattened(id) is { } flat && flat.In.All(a => e.Made[a.Resource])) Make(kit.Index);
                }
            }
            return e;
        }

        private bool HasPad(MissionEconomy e) => e.Buildable.Any(id => _d.DroneBaysOf(id) > 0);

        /// <summary>Uses up this much of a resource for an objective: nothing when it can be had without limit, else the
        /// one-off stock, else (an item with a recipe) its inputs the same way. Null when it works out, else the chain of
        /// names from the resource down to the thing that can't be had. fresh = it must be newly made (stock doesn't count).</summary>
        private List<string>? Take(MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent, int r, Fix64 amount, bool fresh = false, int depth = 0)
        {
            if (amount <= Fix64.Zero || r == _power) return null;
            var def = _d.Resources[r];
            if (!fresh)
            {
                if (e.Made[r]) return null;
                var left = Fix64.Max(Fix64.Zero, Left(state, spent, r));
                var use = Fix64.Min(left, amount);
                if (use > Fix64.Zero) spent[r] = (spent.TryGetValue(r, out var used) ? used : Fix64.Zero) + use;
                amount -= use;
                if (amount <= Fix64.Zero) return null;
            }
            var recipe = _d.Fabrication?.Recipe(def.Id);
            var hub = _d.Fabrication?.Hub.Recipes.Contains(def.Id) == true;
            if (recipe != null && depth < 12 && (e.Fabricator || hub))
            {
                if (!e.Fabricator && _d.Flattened(def.Id) is { } flat) // the hub's workshop: straight from materials
                {
                    foreach (var a in flat.In)
                        if (Take(e, state, spent, a.Resource, a.Amount * amount, false, depth + 1) is { } lack)
                            return ViaRecipes(recipe, lack);
                    return null;
                }
                foreach (var a in recipe.In)
                    if (Take(e, state, spent, a.Resource, a.Amount * amount, false, depth + 1) is { } lack)
                    {
                        lack.Insert(0, def.Name);
                        return lack;
                    }
                return null;
            }
            return new List<string> { def.Name };
        }

        /// <summary>The names from a hub recipe down to a flattened input that can't be had (through the recipe tree).</summary>
        private List<string> ViaRecipes(FabRecipe recipe, List<string> lack)
        {
            var missing = lack[0];
            List<string>? Path(FabRecipe r, int depth)
            {
                foreach (var a in r.In)
                {
                    var input = _d.Resources[a.Resource];
                    if (input.Name == missing) return new List<string> { NameOf(r.Id), missing };
                    if (depth < 12 && _d.Fabrication!.Recipe(input.Id) is { } sub && Path(sub, depth + 1) is { } below)
                    {
                        below.Insert(0, NameOf(r.Id));
                        return below;
                    }
                }
                return null;
            }
            var path = Path(recipe, 0) ?? new List<string> { NameOf(recipe.Id), missing };
            path.AddRange(lack.Skip(1));
            return path;
        }

        // ------------------------------------------------------------ what a condition needs

        /// <summary>Why a condition can't be met with what the player has here (empty = it can). An objective uses up
        /// one-off stock (spent); a trigger is only asked whether it could ever hold at this point.</summary>
        private List<string> Lacks(MissionDef m, MissionCondDef c, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent, bool trigger)
        {
            var lacks = new List<string>();
            switch (c.Kind)
            {
                case "all":
                    foreach (var child in c.Children) lacks.AddRange(Lacks(m, child, e, state, spent, trigger));
                    break;
                case "any":
                {
                    List<string>? first = null;
                    foreach (var child in c.Children)
                    {
                        var trial = new Dictionary<int, Fix64>(spent);
                        var mine = Lacks(m, child, e, state, trial, trigger);
                        if (mine.Count == 0)
                        {
                            foreach (var kv in trial) spent[kv.Key] = kv.Value;
                            return lacks;
                        }
                        first ??= mine;
                    }
                    if (first != null) lacks.Add(c.Children.Count > 1 ? $"none of its {c.Children.Count} ways works; the first: {first[0]}" : first[0]);
                    break;
                }
                case "resource":
                {
                    if (c.F("res") is not { } id || !_d._resources.TryGetValue(id, out var r)) break;
                    var need = c.N("amount", Fix64.Zero);
                    if (need > Fix64.Zero && !e.Made[r.Index] && Left(state, spent, r.Index) < need)
                        lacks.Add($"{need} {r.Name} can't be had; {WhyNot(m, r.Index, e, state, spent)}");
                    break;
                }
                case "building":
                {
                    var ids = IdsOf(c).Where(_d._buildings.ContainsKey).ToList();
                    if (ids.Count == 0) break;
                    var count = c.I("count", 1);
                    string? first = null;
                    var ok = false;
                    foreach (var id in ids)
                    {
                        var b = _d._buildings[id];
                        var trial = new Dictionary<int, Fix64>(spent);
                        var why = BuildingLack(m, b, count - (b == _hub ? 1 : 0), e, state, trial);
                        if (why == null)
                        {
                            foreach (var kv in trial) spent[kv.Key] = kv.Value;
                            ok = true;
                            break;
                        }
                        first ??= why;
                    }
                    if (!ok && first != null) lacks.Add(first);
                    break;
                }
                case "unit":
                {
                    var ids = IdsOf(c).Where(_d._surfaceUnits.ContainsKey).ToList();
                    if (ids.Count == 0 || ids.Any(e.Units.Contains)) break;
                    lacks.Add(UnitLack(m, ids[0], e, state, spent));
                    break;
                }
                case "fabricated":
                {
                    if (c.F("item") is not { } id || !_d._resources.TryGetValue(id, out var item)) break;
                    var count = c.I("count", 1);
                    if (_d.Fabrication?.Recipe(id) == null)
                    {
                        lacks.Add($"{item.Name} has no fabrication recipe, so none can be made.");
                        break;
                    }
                    if (!e.Fabricator && _d.Fabrication.Hub.Recipes.Contains(id) != true)
                    {
                        lacks.Add($"{item.Name} is made at a {FabricatorNames()}, but {FabricatorLack(m, e, state, spent)}.");
                        break;
                    }
                    if (Take(e, state, spent, item.Index, Fix64.FromInt(count), fresh: true) is { } path) lacks.Add(PathText(m, path, e, state, spent));
                    break;
                }
                case "drone_launched":
                    if (DroneLack(m, c.I("count", 1), e, state, spent) is { } launch) lacks.Add(launch);
                    break;
                case "drone_at":
                    if (DroneLack(m, 1, e, state, spent) is { } over) lacks.Add(over);
                    break;
                case "site":
                    if (c.F("state") == "visited")
                    {
                        if (DroneLack(m, 1, e, state, spent) is { } visit) lacks.Add(visit);
                    }
                    else if (c.F("state") is "reached" or "found" or null && GroundLack(e, 1) is { } drive) lacks.Add(drive);
                    break;
                case "surveyed":
                case "exposed":
                    if (GroundLack(e, 1) is { } rover) lacks.Add(rover);
                    break;
                case "near":
                    if (GroundLack(e, 2) is { } other && DroneLack(m, 1, e, state, new Dictionary<int, Fix64>(spent)) != null) lacks.Add(other);
                    break;
                case "deposits_found":
                {
                    if (c.F("res") is { } res && _d._resources.TryGetValue(res, out var r) && !_deposits.Contains(r.Index))
                        lacks.Add($"{r.Name} is not a deposit on every starting world.");
                    else if (c.F("res") == null && c.I("count", 1) > _deposits.Count)
                        lacks.Add($"it asks for {c.I("count", 1)} different resources found, and the starting worlds all have only {_deposits.Count} kinds of deposit.");
                    if (GroundLack(e, 1) is { } scout) lacks.Add(scout);
                    break;
                }
                case "deposit":
                    if (!e.Extractor) lacks.Add("nothing the player has or can build by then digs a deposit (no mine, no mining vehicle).");
                    break;
                case "relay":
                {
                    var count = c.I("count", 1);
                    if (_d.Comms?.Relay is not { } relay) lacks.Add("the data has no relay satellites (surfaces comms relay).");
                    else if (count > relay.Max) lacks.Add($"it asks for {count} relay satellites, and a player may have {relay.Max}.");
                    else if (!trigger && CostLack(m, relay.Cost, 1, "a relay satellite", e, state, spent) is { } cost) lacks.Add(cost);
                    break;
                }
                case "satellite_share":
                    if (_d.Orbit is not { } orbit || orbit.SatelliteMax < 1) lacks.Add("the data has no survey satellites.");
                    else if (!trigger && CostLack(m, orbit.SatelliteCost, 1, "a survey satellite", e, state, spent) is { } sat) lacks.Add(sat);
                    break;
                case "outpost":
                {
                    var more = c.I("count", 1) - 1;
                    if (more <= 0) break;
                    string? first = null;
                    var ok = false;
                    foreach (var u in _d.SurfaceUnits.Where(u => u.Found != null))
                    {
                        if (!e.Units.Contains(u.Id))
                        {
                            first ??= UnitLack(m, u.Id, e, state, spent);
                            continue;
                        }
                        var trial = new Dictionary<int, Fix64>(spent);
                        if (CostLack(m, u.Found!.Cost, more, $"founding an outpost with a {UnitName(u.Id)}", e, state, trial) is { } why)
                        {
                            first ??= why;
                            continue;
                        }
                        foreach (var kv in trial) spent[kv.Key] = kv.Value;
                        ok = true;
                        break;
                    }
                    if (!ok && _d.Orbit is { } o && _d._buildings.ContainsKey(o.OutpostBuilding))
                    {
                        var trial = new Dictionary<int, Fix64>(spent);
                        if (CostLack(m, o.OutpostCost, more, "an outpost drop", e, state, trial) is { } why) first ??= why;
                        else
                        {
                            foreach (var kv in trial) spent[kv.Key] = kv.Value;
                            ok = true;
                        }
                    }
                    if (!ok) lacks.Add(first ?? "nothing founds a second outpost (no construction vehicle, no outpost drop).");
                    break;
                }
                case "tech":
                    if (c.F("id") is { } tech && _d._techs.TryGetValue(tech, out var t) && !e.Techs.Contains(tech)) lacks.Add(TechLack(m, t, e, state, spent) + ".");
                    break;
                case "flag":
                    if (c.F("name") is { } flag && !state.Flags.Contains(flag))
                    {
                        var setters = _data.List.Where(x => x.Chain == _chain.Id && x.Rewards.Concat(x.Stages.SelectMany(s => s.OnEnter)).Any(a => a.Kind == "set_flag" && a.F("name") == flag)).ToList();
                        if (setters.Count == 0) lacks.Add($"nothing in the chain sets the flag '{flag}'.");
                        else if (trigger) lacks.Add($"the flag '{flag}' is not set yet ({setters[0].Title} sets it).");
                        else if (setters.All(x => x.IsMain && !_done.Contains(x.Id) && x.Id != _running && x.Id != m.Id))
                            lacks.Add($"the flag '{flag}' is only set later, by {setters[0].Title}.");
                    }
                    break;
                case "mission":
                {
                    if (!trigger || c.F("id") is not { } id || _data.Find(id) is not { IsMain: true } target || target.Chain != _chain.Id) break;
                    var want = c.F("state") ?? "completed";
                    var now = _done.Contains(id) ? "over" : _running == id ? "active" : "locked";
                    var ok = want switch
                    {
                        "completed" or "failed" => now == "over",
                        "active" => now == "active",
                        "locked" => now == "locked",
                        _ => true,
                    };
                    if (!ok) lacks.Add($"{target.Title} is not {want} yet.");
                    break;
                }
                // not, time, weather, uplink, ruin_hint, covered and the space kinds: nothing the data can rule out.
            }
            return lacks;
        }

        private static IReadOnlyList<string> IdsOf(MissionCondDef c)
        {
            if (c.F("id") is not { } one) return c.L("ids");
            var list = c.L("ids").ToList();
            list.Add(one);
            return list;
        }

        /// <summary>Why count of this building can't be put up (null = they can; their cost is taken).</summary>
        private string? BuildingLack(MissionDef m, BuildingDef b, int count, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            if (count <= 0) return null;
            if (!e.Buildable.Contains(b.Id)) return $"no {b.Name} can be built by then: {WhyNoBuilding(m, b, e, state, spent)}.";
            if (b.RequiresRuin && !Mentions(m, "ruin_hint")) return $"a {b.Name} only goes on a ruin, and not every world has one.";
            return CostLack(m, b.Cost, count, $"the {b.Name}", e, state, spent);
        }

        private static bool Mentions(MissionDef m, string kind)
        {
            bool In(MissionCondDef? c) => c != null && (c.Kind == kind || c.Children.Any(x => In(x)));
            return In(m.Trigger);
        }

        private string? CostLack(MissionDef m, IEnumerable<ResourceAmount> cost, int count, string what, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            foreach (var a in cost)
                if (Take(e, state, spent, a.Resource, a.Amount * count) is { } path)
                    return $"{what} costs {a.Amount * count} {_d.Resources[a.Resource].Name}; {PathText(m, path, e, state, spent)}";
            return null;
        }

        /// <summary>"Scout Drone needs Power Coupling, which needs Parsteel; nothing unlocked by then makes Parsteel (…)".</summary>
        private string PathText(MissionDef m, List<string> path, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            var text = path[0];
            for (var i = 1; i < path.Count; i++) text += i == 1 ? $" needs {path[i]}" : $", which needs {path[i]}";
            var last = _d.Resources.FirstOrDefault(r => r.Name == path[^1]);
            return last == null ? text + "." : $"{text}{(path.Count > 1 ? "; " : ": ")}{WhyNot(m, last.Index, e, state, spent)}";
        }

        /// <summary>Why a resource can't be had: who would make it and why they can't yet.</summary>
        private string WhyNot(MissionDef m, int r, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            var def = _d.Resources[r];
            var have = Fix64.Max(Fix64.Zero, state.Stock.TryGetValue(r, out var s) ? s : Fix64.Zero);
            var stock = have > Fix64.Zero ? $" The landing and earlier rewards give only {have} in all." : "";
            var head = $"nothing unlocked by then makes {def.Name}";
            if (_d.Fabrication?.Recipe(def.Id) != null && !e.Fabricator)
                return $"{head}: a {FabricatorNames()} does, but {FabricatorLack(m, e, state, spent)}.{stock}";
            var makers = _surface.Where(b => b.Recipes.Any(x => x.Out.Any(a => a.Resource == r)) || b.OutputPerMinute.Any(a => a.Resource == r)).ToList();
            if (makers.Count > 0)
            {
                var b = makers.FirstOrDefault(x => e.Buildable.Contains(x.Id)) ?? makers[0];
                if (!e.Buildable.Contains(b.Id)) return $"{head}: the {b.Name} does, but {WhyNoBuilding(m, b, e, state, spent)}.{stock}";
                var input = b.Recipes.Where(x => x.Out.Any(a => a.Resource == r)).SelectMany(x => x.In).Concat(b.UpkeepPerMinute)
                    .Where(a => a.Resource != _power && !e.Made[a.Resource]).Select(a => _d.Resources[a.Resource].Name).FirstOrDefault();
                return $"{head}: the {b.Name} does, but nothing makes its {input ?? "inputs"}.{stock}";
            }
            if (_d.Surfaces.Any(x => x.Deposits.Any(rule => rule.ResourceId == def.Id)))
                return _deposits.Contains(r)
                    ? $"{head}: it is mined, and the player has no mine or mining vehicle by then.{stock}"
                    : $"{head}: it is a deposit on some worlds only, {string.Join(", ", _d.Surfaces.Where(x => x.Deposits.All(rule => rule.ResourceId != def.Id)).Select(x => x.ClassId))} has none.{stock}";
            return $"{head}, and nothing in the build menu does.{stock}";
        }

        private string FabricatorNames() => string.Join(" or ", _surface.Where(b => b.Fabricator != null).Select(b => b.Name).DefaultIfEmpty("fabricator"));

        private string FabricatorLack(MissionDef m, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent) =>
            _surface.FirstOrDefault(b => b.Fabricator != null) is { } shop ? WhyNoBuilding(m, shop, e, state, spent) : "no building has one";

        /// <summary>Why this building can't be built at this point: locked (and which mission opens it), its tech, its cost.</summary>
        private string WhyNoBuilding(MissionDef m, BuildingDef b, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            if (!state.Open.Contains(b.Id) && !_cores.Contains(b.Id)) return $"it is not unlocked yet ({UnlockText(b.Id)})";
            if (b.RequiresTech.Length > 0 && !e.Techs.Contains(b.RequiresTech))
                return _d._techs.TryGetValue(b.RequiresTech, out var t) ? $"it needs research: {TechLack(m, t, e, state, spent)}" : $"it needs the tech '{b.RequiresTech}', which is not a surface tech";
            foreach (var a in b.Cost)
                if (a.Amount > Fix64.Zero && a.Resource != _power && !e.Made[a.Resource] && Left(state, spent, a.Resource) < a.Amount)
                    return $"its cost of {a.Amount} {_d.Resources[a.Resource].Name} can't be had";
            return "it can't be built";
        }

        /// <summary>Which mission of the chain opens a building, and when.</summary>
        private string UnlockText(string buildingId)
        {
            foreach (var m in _mains.Concat(_sides))
            {
                for (var i = 0; i < m.Stages.Count; i++)
                    if (m.Stages[i].OnEnter.Any(a => UnlockIds(a).Contains(buildingId)))
                        return i == 0 ? $"{m.Title} opens it when it starts" : $"{m.Title} opens it at its stage \"{m.Stages[i].Title}\"";
                if (m.Rewards.Any(a => UnlockIds(a).Contains(buildingId)))
                    return m.IsMain ? $"{m.Title} only opens it as its reward" : $"only the side job {m.Title} opens it, as its reward";
            }
            return "no mission opens it";
        }

        private string TechLack(MissionDef m, TechDef t, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            if (!e.Lab)
                return _surface.FirstOrDefault(b => b.IsLab) is { } lab ? $"{t.Name} can't be researched, no {lab.Name} can be built by then: {WhyNoBuilding(m, lab, e, state, spent)}" : $"{t.Name} can't be researched: no building is a lab";
            var missing = t.Requires.FirstOrDefault(x => !e.Techs.Contains(x));
            return $"{t.Name} can't be researched{(missing != null ? $" (it requires '{missing}', which can't be)" : "")}";
        }

        private string UnitName(string id) => _d.GroundVehicleNames.TryGetValue(id, out var name) ? name : _d._resources.TryGetValue(id, out var r) ? r.Name : id;

        private string NameOf(string resourceId) => _d._resources.TryGetValue(resourceId, out var r) ? r.Name : resourceId;

        /// <summary>Why the player can't have this unit at this point.</summary>
        private string UnitLack(MissionDef m, string id, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            var name = UnitName(id);
            if (_d.DroneOf(id) != null) return DroneLack(m, 1, e, state, spent) ?? $"no {name} can be had";
            var bays = _surface.Where(b => b.Produces.Contains(id)).ToList();
            if (bays.Count == 0) return $"no {name}: nothing builds one and the landing brings none.";
            var bay = bays.FirstOrDefault(b => e.Buildable.Contains(b.Id));
            if (bay == null) return $"no {name}: the {bays[0].Name} builds it, but {WhyNoBuilding(m, bays[0], e, state, spent)}.";
            return CostLack(m, _d._surfaceUnits[id].Cost, 1, $"a {name}", e, state, spent) ?? $"no {name} can be had";
        }

        /// <summary>Why count drones can't fly (null = they can): a pad to fly from and the packed drones in stock.</summary>
        private string? DroneLack(MissionDef m, int count, MissionEconomy e, ReachState state, Dictionary<int, Fix64> spent)
        {
            if (_d.Drones.Count == 0) return "the data has no drones.";
            if (!HasPad(e))
                return _surface.FirstOrDefault(b => _d.DroneBaysOf(b.Id) > 0) is { } pad
                    ? $"it needs a drone, and no {pad.Name} can be built by then: {WhyNoBuilding(m, pad, e, state, spent)}."
                    : "it needs a drone, and no building has drone bays.";
            string? first = null;
            foreach (var drone in _d.Drones)
            {
                if (!_d._resources.TryGetValue(drone.UnitId, out var item)) continue;
                var trial = new Dictionary<int, Fix64>(spent);
                if (Take(e, state, trial, item.Index, Fix64.FromInt(count)) is { } path)
                {
                    first ??= $"it needs {(count == 1 ? "a drone" : count + " drones")}: {PathText(m, path, e, state, spent)}";
                    continue;
                }
                foreach (var kv in trial) spent[kv.Key] = kv.Value;
                return null;
            }
            return first ?? "it needs a drone, and no drone is an item a pad can unpack.";
        }

        /// <summary>Null when the player surely has (or can build) this many ground vehicles.</summary>
        private string? GroundLack(MissionEconomy e, int count)
        {
            var landed = _d.Landing.Units.Count(id => !_d.IsAerialUnit(id));
            if (landed >= count || e.Built.Any(id => !_d.IsAerialUnit(id))) return null;
            return landed == 0 ? "it needs a ground vehicle, and the landing brings none and no bay can be built by then." : $"it needs {count} ground vehicles, and the landing brings {landed} and no bay can be built by then.";
        }
    }
}
