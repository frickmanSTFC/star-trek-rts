using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Civilizations phase 2, surface side: ruins on the colony's own world and digging them (docs/civilizations-phase2-contract.md,
// data surfaces/excavation.json). Ruins are rolled from the map seed when the surface is made (own SplitMix hash, never
// sim.Rng; no terrain changes). An Excavation Site (buildings.json requires_ruin) on a ruin tile digs it layer by layer in the
// economy pass; each finished layer rolls a find on sim.Rng. Pathogens make a base's crew sick (crewed work, repairs and
// construction slower) and vehicles carry it to the player's other bases. All of it is hashed only once a map has any of it.

/// <summary>A ruin site on the colony's map: the dead species, how many layers it has and how many are dug.</summary>
public sealed record SurfaceRuin(int Tile, string Species, int Layers)
{
    public int LayersDug { get; internal set; }
    /// <summary>Locked by a woken defence system until this tick (0 = not locked).</summary>
    public long LockedUntilTick { get; internal set; }
    public bool Exhausted => LayersDug >= Layers;

    /// <summary>The canon precursor this site belongs to (excavation.json precursors id), or null for an invented species.</summary>
    public string? Precursor { get; internal init; }

    /// <summary>Work done on the current layer (in work-ticks; a layer takes seconds_per_layer × tick rate at full speed).</summary>
    internal Fix64 Progress { get; set; }
}

/// <summary>Something a dig layer turned up. Kind: artifact, data_core, tech_fragment, resources, defence, pathogen, and
/// precursor (the reward of a precursor site, on its last layer).</summary>
public sealed record DigFind(long Tick, int Player, int Tile, string Kind, string Name, string Effect);

public sealed partial class PlanetSurface
{
    private readonly List<SurfaceRuin> _ruins = new();
    private readonly List<DigFind> _finds = new();
    private readonly SortedDictionary<int, long> _sickUntil = new(); // base (core id) → tick the crew are well again
    private readonly SortedDictionary<int, long> _immuneUntil = new(); // base → tick it can catch a carried pathogen again
    private readonly SortedDictionary<int, long> _carrierUntil = new(); // unit id → tick it stops carrying the pathogen
    private readonly SortedDictionary<int, Fix64> _buildCarry = new(); // base → construction work owed while sick (< 1 tick)
    private readonly SortedDictionary<int, Fix64> _digBank = new(); // player → dig research waiting for an active tech
    private readonly SortedDictionary<int, Fix64> _shuttleFactor = new(); // player → × drop-ship turnaround (precursor reward)
    private readonly List<(int Player, Modifier Modifier)> _digModifiers = new(); // tech fragments and precursor rewards

    private ExcavationRules? Ex => _data.Excavation;

    /// <summary>Every ruin site on the map (about 1 colony world in 4 has one).</summary>
    public IReadOnlyList<SurfaceRuin> Ruins => _ruins;

    /// <summary>The ruin on a tile, or null.</summary>
    public SurfaceRuin? RuinAt(int tile)
    {
        foreach (var r in _ruins)
            if (r.Tile == tile) return r;
        return null;
    }

    /// <summary>Progress 0..1 on the current layer of the dig at this Excavation Site (0 if it isn't one; 1 once dug out).</summary>
    public Fix64 DigProgress(SurfaceStructure site)
    {
        if (!_data.Building(site.DefId).RequiresRuin || RuinAt(site.Tile) is not { } ruin || Ex is not { } x) return Fix64.Zero;
        if (ruin.Exhausted) return Fix64.One;
        return Fix64.Min(Fix64.One, ruin.Progress / Fix64.FromInt(LayerTicks(x)));
    }

    /// <summary>Why the dig at this site isn't moving ("locked by a defence system: 2:10", "site exhausted"), or null.</summary>
    public string? DigProblem(SurfaceStructure site)
    {
        if (!_data.Building(site.DefId).RequiresRuin) return "not an Excavation Site";
        if (NotReadyProblem(site) is { } notReady) return notReady;
        if (RuinAt(site.Tile) is not { } ruin) return "no ruins here";
        if (ruin.Exhausted) return "site exhausted: every layer is dug";
        if (ruin.LockedUntilTick > Now) return "locked by a defence system: " + Clock(SecondsUntil(ruin.LockedUntilTick));
        if (site.Broken) return "broken down: needs maintenance";
        if (site.Paused) return "paused";
        if (site.Status == StructureStatus.NoUpkeep) return "can't pay its upkeep";
        return null;
    }

    /// <summary>Every find so far, oldest first.</summary>
    public IReadOnlyList<DigFind> Finds => _finds;

    /// <summary>Seconds the base's crew stay sick from a released pathogen (0 = healthy).</summary>
    public int CrewSickSeconds(int baseId) => _sickUntil.TryGetValue(baseId, out var until) ? SecondsUntil(until) : 0;

    /// <summary>Whether this vehicle carries a pathogen from a sick base (it infects the next of its owner's bases it reaches).</summary>
    public bool Carrier(SurfaceUnit u) => CarrierSeconds(u) > 0;

    /// <summary>Seconds this vehicle keeps carrying a pathogen (0 = clean).</summary>
    public int CarrierSeconds(SurfaceUnit u) => _carrierUntil.TryGetValue(u.Id, out var until) ? SecondsUntil(until) : 0;

    /// <summary>Seconds before a base that has been sick can catch a carried pathogen again (0 = it can).</summary>
    public int ImmuneSeconds(int baseId) => _immuneUntil.TryGetValue(baseId, out var until) ? SecondsUntil(until) : 0;

    /// <summary>The share of normal work a base's crew manage right now: pathogen.work_multiplier while sick, else 1.</summary>
    public Fix64 CrewHealth(int baseId) => IsSick(baseId) && Ex is { } x ? x.Pathogen.WorkMultiplier : Fix64.One;

    /// <summary>What the orbital scan says over a region that holds ruins.</summary>
    public const string RuinHintText = "artificial structures detected";

    /// <summary>Whether the orbital scan reports artificial structures over this tile's region (a ruin lies somewhere in it;
    /// surveying finds the exact hex).</summary>
    public bool RuinRegionHint(int tile)
    {
        if (_ruins.Count == 0 || !OnMap(tile)) return false;
        var region = Map.Region(tile);
        foreach (var r in _ruins)
            if (Map.Region(r.Tile) == region) return true;
        return false;
    }

    /// <summary>The regions (SurfaceMap.Region ids, as for the deposit hints) the orbital scan reports artificial structures in.</summary>
    public IReadOnlyList<int> RuinHintRegions => _ruins.Select(r => Map.Region(r.Tile)).Distinct().OrderBy(r => r).ToList();

    /// <summary>Dig research waiting for the player to start a surface tech (points).</summary>
    public Fix64 DigResearchBank(int player) => _digBank.TryGetValue(player, out var v) ? v : Fix64.Zero;

    /// <summary>Modifiers the player has from tech fragments and precursor rewards (kept for the rest of the game).</summary>
    public IReadOnlyList<Modifier> DigModifiers(int player) => _digModifiers.Where(x => x.Player == player).Select(x => x.Modifier).ToList();

    /// <summary>× the drop ship's turnaround time for this player (an Iconian gateway fragment halves it; 1 = no change).</summary>
    public Fix64 ShuttleTurnaround(int player) => _shuttleFactor.TryGetValue(player, out var f) ? f : Fix64.One;

    /// <summary>What players are told when an Excavation Site is put off a ruin, or another building on one.</summary>
    public static string? RuinPlacementText(PlacementProblem p) => p switch
    {
        PlacementProblem.NeedsRuin => "needs a ruin site that isn't dug out",
        PlacementProblem.RuinInTheWay => "ruins are there: only an Excavation Site can go on them",
        _ => null,
    };

    // ---------------------------------------------------------------- helpers

    private long Now => Game?.Tick ?? _weatherTick;

    private int SecondsUntil(long tick) => (int)System.Math.Max(0, (tick - Now + Simulation.TickRate - 1) / Simulation.TickRate);

    private static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    private static int LayerTicks(ExcavationRules x) => System.Math.Max(1, x.SecondsPerLayer * Simulation.TickRate);

    private bool IsSick(int baseId) => _sickUntil.Count > 0 && _sickUntil.TryGetValue(baseId, out var until) && until > Now;

    /// <summary>× on crewed work, repairs and construction at this base (1 when healthy).</summary>
    private Fix64 SickShare(int baseId) => _sickUntil.Count == 0 ? Fix64.One : CrewHealth(baseId);

    private bool Knows(int player, string techId) =>
        _data.HasTech(techId) && player >= 0 && player < _players.Count && _players[player].Known[_data.Tech(techId).Index];

    /// <summary>A ruin that isn't dug out stands on this tile (nothing but an Excavation Site, no core, no landing there).</summary>
    private bool RuinBlocks(int tile) => _ruins.Count > 0 && RuinAt(tile) is { Exhausted: false };

    /// <summary>The ruin rule of placement: an Excavation Site needs a ruin that isn't dug out; nothing else goes on one.</summary>
    private PlacementProblem RuinPlacement(string buildingId, int tile)
    {
        var ruin = RuinAt(tile);
        if (_data.Building(buildingId).RequiresRuin) return ruin is { Exhausted: false } ? PlacementProblem.None : PlacementProblem.NeedsRuin;
        return ruin is { Exhausted: false } ? PlacementProblem.RuinInTheWay : PlacementProblem.None;
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A small SplitMix stream for rolling ruins (never the game's dice).</summary>
    private sealed class RuinDice
    {
        private ulong _state;
        public RuinDice(ulong seed) => _state = seed;
        public ulong Next() => Mix(_state += 0x632BE59BD9B4E019UL);
        public int Int(int maxExclusive) => maxExclusive <= 1 ? 0 : (int)(Next() % (ulong)maxExclusive);
        public int Range(int min, int max) => max <= min ? min : min + Int(max - min + 1);
    }

    /// <summary>The species (or precursor) and layers of a new ruin site.</summary>
    private SurfaceRuin NewRuin(ExcavationRules x, RuinDice dice, int tile)
    {
        var total = System.Math.Max(0, x.SpeciesWeight) + x.Precursors.Sum(p => System.Math.Max(0, p.Weight));
        var roll = dice.Int(System.Math.Max(1, total));
        if (roll >= System.Math.Max(0, x.SpeciesWeight))
        {
            roll -= System.Math.Max(0, x.SpeciesWeight);
            foreach (var p in x.Precursors)
            {
                var w = System.Math.Max(0, p.Weight);
                if (roll < w) return new SurfaceRuin(tile, p.Species, dice.Range(p.LayersMin, p.LayersMax)) { Precursor = p.Id };
                roll -= w;
            }
        }
        var species = x.Species.Count > 0 ? x.Species[dice.Int(x.Species.Count)] : "unknown";
        return new SurfaceRuin(tile, species, dice.Range(x.LayersMin, x.LayersMax));
    }

    /// <summary>Whether the ruin roll may put a site on this tile: listed (buildable, non-landing) terrain, no deposit, not
    /// toxic, no predators, outside the natives' land, edge_rows from the poles.</summary>
    private bool RuinGround(ExcavationRules x, int tile)
    {
        var row = Map.Grid.Row(tile);
        if (row < x.EdgeRows || row >= Map.Grid.Height - x.EdgeRows) return false;
        var terrain = Map.Terrain(tile);
        if (!terrain.Buildable || !terrain.Passable || !x.Terrain.Contains(terrain.Id)) return false;
        return Map.DepositResource[tile] < 0 && !Map.Toxic[tile] && Map.WildlifeAt[tile] != Wildlife.Predators && !Map.NearNatives(tile);
    }

    /// <summary>Rolls the map's ruins once, when the surface is made (after generation; terrain untouched).</summary>
    private void RollRuins()
    {
        if (Ex is not { } x || x.Chance <= Fix64.Zero) return;
        var dice = new RuinDice(Mix(Map.Seed ^ 0x5255494E53UL)); // "RUINS"
        if (Fix64.Ratio(dice.Int(1_000_000), 1_000_000) >= x.Chance) return;
        var candidates = new List<int>();
        for (var t = 0; t < Map.Grid.Count; t++)
            if (RuinGround(x, t)) candidates.Add(t);
        if (candidates.Count == 0) return;
        var sites = dice.Range(x.SitesMin, x.SitesMax);
        for (var s = 0; s < sites; s++)
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var tile = candidates[dice.Int(candidates.Count)];
                if (_ruins.Any(r => Map.Grid.Distance(r.Tile, tile) < x.MinDistance)) continue;
                _ruins.Add(NewRuin(x, dice, tile));
                break;
            }
    }

    // ---------------------------------------------------------------- the dig

    /// <summary>Economy's status pass for an Excavation Site: idle (no power) when there's nothing to dig or it's locked.</summary>
    private StructureStatus? DigIdle(SurfaceStructure s)
    {
        if (RuinAt(s.Tile) is not { } ruin || ruin.Exhausted || Ex == null) return StructureStatus.Depleted;
        if (ruin.LockedUntilTick > Now) return StructureStatus.NoInput;
        return null;
    }

    /// <summary>One tick of digging at a working site: progress × its work rate (power, condition, speed, crew health).</summary>
    private void DigStep(Simulation sim, SurfaceStructure s, Fix64 rate)
    {
        if (Ex is not { } x || RuinAt(s.Tile) is not { } ruin || ruin.Exhausted) return;
        ruin.Progress += rate;
        if (ruin.Progress < Fix64.FromInt(LayerTicks(x))) return;
        ruin.Progress = Fix64.Zero;
        ruin.LayersDug++;
        RollFind(sim, s, ruin, x);
        if (ruin.Exhausted && x.Precursor(ruin.Precursor) is { } precursor) Reward(sim, s, precursor);
    }

    private void AddFind(Simulation sim, SurfaceStructure s, string kind, string name, string effect) =>
        _finds.Add(new DigFind(sim.Tick, s.Owner, s.Tile, kind, name, effect));

    private static T Pick<T>(Simulation sim, IReadOnlyList<T> items, System.Func<T, int> weight)
    {
        var total = 0;
        foreach (var i in items) total += System.Math.Max(0, weight(i));
        if (total <= 0) return items[0];
        var roll = sim.Rng.NextInt(total);
        foreach (var i in items)
        {
            var w = System.Math.Max(0, weight(i));
            if (roll < w) return i;
            roll -= w;
        }
        return items[^1];
    }

    private static string PickName(Simulation sim, IReadOnlyList<string> names) => names.Count == 0 ? "something" : names[sim.Rng.NextInt(names.Count)];

    /// <summary>A finished layer's find, by weight (a precursor site's danger × the defence and pathogen weights).</summary>
    private void RollFind(Simulation sim, SurfaceStructure s, SurfaceRuin ruin, ExcavationRules x)
    {
        var danger = x.Precursor(ruin.Precursor)?.Danger ?? Fix64.One;
        var kind = Pick(sim, x.Finds, f => f.Kind is "defence" or "pathogen" ? (Fix64.FromInt(f.Weight) * danger).FloorToInt() : f.Weight).Kind;
        switch (kind)
        {
            case "artifact":
            case "data_core":
            {
                var rf = kind == "artifact" ? x.Artifact : x.DataCore;
                var points = sim.Rng.NextInt(rf.Min, rf.Max + 1);
                AddFind(sim, s, kind, PickName(sim, rf.Names), GiveResearch(sim, s.Owner, Fix64.FromInt(points)));
                break;
            }
            case "tech_fragment":
            {
                var f = Pick(sim, x.TechFragments, t => t.Weight);
                var parts = new List<string>();
                if (f.Research > Fix64.Zero) parts.Add(GiveResearch(sim, s.Owner, f.Research));
                foreach (var m in f.Modifiers) _digModifiers.Add((s.Owner, m));
                if (f.Modifiers.Count > 0) parts.Add(Describe(f.Modifiers));
                AddFind(sim, s, kind, f.Name, string.Join("; ", parts));
                break;
            }
            case "resources":
            {
                var c = Pick(sim, x.Caches, k => k.Weight);
                var parts = new List<string>();
                foreach (var (res, min, max) in c.Give)
                {
                    var amount = sim.Rng.NextInt(min, max + 1);
                    var r = _data.Resource(res);
                    var stored = Store(sim, s.Owner, r.Index, Fix64.FromInt(amount)).FloorToInt();
                    parts.Add(stored < amount ? $"+{stored} {r.Name} ({amount - stored} lost: storage full)" : $"+{amount} {r.Name}");
                }
                AddFind(sim, s, kind, c.Name, string.Join(", ", parts));
                break;
            }
            case "defence":
                Defence(sim, s, ruin, x.Defence);
                break;
            case "pathogen":
            {
                var p = x.Pathogen;
                var seconds = Fix64.FromInt(p.SickSeconds);
                if (Knows(s.Owner, p.Tech)) seconds *= p.TechSecondsMultiplier;
                var ticks = System.Math.Max(1, (seconds * Simulation.TickRate).FloorToInt());
                Infect(s.BaseId, ticks, p, sim.Tick);
                AddFind(sim, s, kind, PickName(sim, p.Names), $"the base's crew are sick for {Clock(ticks / Simulation.TickRate)} (work ×{p.WorkMultiplier})");
                break;
            }
        }
    }

    private string Describe(IReadOnlyList<Modifier> modifiers) => string.Join(", ", modifiers.Select(m =>
    {
        var what = m.Buildings.Count > 0 ? string.Join("/", m.Buildings.Select(b => _data.Building(b).Name))
            : m.Units.Count > 0 ? string.Join("/", m.Units) : "every building";
        var pct = (m.Add * Fix64.FromInt(100)).FloorToInt();
        return m.Stat == Modifier.AutoMaintain ? $"auto-maintenance for {what}" : $"{m.Stat} {(pct >= 0 ? "+" : "")}{pct}% for {what}";
    }));

    /// <summary>Research points into the player's active surface tech (the rest, or all with none active, waits in the bank).</summary>
    private string GiveResearch(Simulation sim, int player, Fix64 points)
    {
        _digBank[player] = DigResearchBank(player) + points;
        var into = player < _players.Count && _players[player].ActiveTech >= 0 ? _data.Techs[_players[player].ActiveTech].Name : null;
        PayDigResearch(player);
        return into != null ? $"+{points.FloorToInt()} research ({into})" : $"+{points.FloorToInt()} research (banked until a tech is started)";
    }

    private void PayDigResearch(int player)
    {
        if (!_digBank.TryGetValue(player, out var bank) || bank <= Fix64.Zero || player >= _players.Count) return;
        var sp = _players[player];
        if (sp.ActiveTech < 0) return;
        var tech = _data.Techs[sp.ActiveTech];
        var pay = Fix64.Min(tech.Cost - sp.ResearchProgress[tech.Index], bank);
        if (pay > Fix64.Zero)
        {
            sp.ResearchProgress[tech.Index] += pay;
            bank -= pay;
        }
        if (sp.ResearchProgress[tech.Index] >= tech.Cost)
        {
            sp.ResearchProgress[tech.Index] = tech.Cost;
            sp.Known[tech.Index] = true;
            sp.ActiveTech = -1;
        }
        if (bank <= Fix64.Zero) _digBank.Remove(player);
        else _digBank[player] = bank;
    }

    /// <summary>A woken defence system: maybe found disarmed; otherwise it hits every building and vehicle within radius
    /// (never below 1, never broken by the blast) and locks the dig.</summary>
    private void Defence(Simulation sim, SurfaceStructure site, SurfaceRuin ruin, DigDefenceRules d)
    {
        var name = PickName(sim, d.Names);
        var tech = Knows(site.Owner, d.Tech);
        if (sim.Rng.NextFix() < (tech ? d.TechDisarmedChance : d.DisarmedChance))
        {
            AddFind(sim, site, "defence", name, "found disarmed");
            return;
        }
        var damage = Fix64.FromInt(sim.Rng.NextInt(d.DamageMin, d.DamageMax + 1));
        if (tech) damage *= d.TechDamageMultiplier;
        var buildings = 0;
        var vehicles = 0;
        foreach (var s in _structures)
        {
            if (Map.Grid.Distance(s.Tile, site.Tile) > d.Radius) continue;
            s.Condition = Fix64.Max(Fix64.Min(s.Condition, Fix64.One), s.Condition - damage);
            buildings++;
        }
        foreach (var u in _units)
        {
            if (Map.Grid.Distance(u.Tile, site.Tile) > d.Radius) continue;
            u.Integrity = Fix64.Max(Fix64.Min(u.Integrity, Fix64.One), u.Integrity - damage);
            u.HazardAtTick = sim.Tick;
            u.LastHazard = "ruin defence";
            u.LastHazardDamage = damage.FloorToInt();
            vehicles++;
        }
        ruin.LockedUntilTick = sim.Tick + (long)d.LockSeconds * Simulation.TickRate;
        AddFind(sim, site, "defence", name,
            $"−{damage.FloorToInt()} to {buildings} building{(buildings == 1 ? "" : "s")} and {vehicles} vehicle{(vehicles == 1 ? "" : "s")} within {d.Radius}; dig locked {Clock(d.LockSeconds)}");
    }

    /// <summary>A base's crew fall sick for this many ticks from now (the clock starts again if they already are);
    /// it can't catch a carried pathogen again until immunity_seconds after it recovers.</summary>
    private void Infect(int baseId, long ticks, DigPathogenRules p, long now)
    {
        _sickUntil[baseId] = now + ticks;
        _immuneUntil[baseId] = now + ticks + (long)p.ImmunitySeconds * Simulation.TickRate;
    }

    /// <summary>The last layer of a precursor site: its reward (research, modifiers for the rest of the game, faster drop ships).</summary>
    private void Reward(Simulation sim, SurfaceStructure s, PrecursorDef p)
    {
        var r = p.Reward;
        var parts = new List<string> { r.Effect };
        if (r.Research > Fix64.Zero) parts.Add(GiveResearch(sim, s.Owner, r.Research));
        foreach (var m in r.Modifiers) _digModifiers.Add((s.Owner, m));
        if (r.ShuttleTurnaround > Fix64.Zero && r.ShuttleTurnaround < Fix64.One) _shuttleFactor[s.Owner] = ShuttleTurnaround(s.Owner) * r.ShuttleTurnaround;
        AddFind(sim, s, "precursor", r.Name, string.Join("; ", parts));
    }

    /// <summary>Each tick, after the vehicles have moved (only when the map has any of this): sickness, immunity and carriers run
    /// out, vehicles on a sick base's core tile pick the pathogen up and carry it to the owner's other bases, and banked dig
    /// research goes into a newly started tech.</summary>
    private void ExcavationStep(Simulation sim)
    {
        if (_sickUntil.Count == 0 && _carrierUntil.Count == 0 && _immuneUntil.Count == 0 && _digBank.Count == 0) return;
        var now = sim.Tick;
        foreach (var player in _digBank.Keys.ToList()) PayDigResearch(player);
        if (Ex is { } x && (_sickUntil.Count > 0 || _carrierUntil.Count > 0))
            foreach (var u in _units)
            {
                var core = StructureAt(u.Tile);
                if (core == null || core.Owner != u.Owner || core.Id != core.BaseId || !_data.Building(core.DefId).IsCore) continue;
                if (_sickUntil.TryGetValue(core.Id, out var until) && until > now)
                {
                    if (!_carrierUntil.TryGetValue(u.Id, out var carrying) || carrying < until) _carrierUntil[u.Id] = until;
                    continue;
                }
                if (!_carrierUntil.TryGetValue(u.Id, out var left) || left <= now) continue;
                if (_immuneUntil.TryGetValue(core.Id, out var immune) && immune > now) continue;
                var ticks = (Fix64.FromInt((int)System.Math.Min(int.MaxValue, left - now)) * x.Pathogen.SpreadShare).FloorToInt();
                if (ticks >= 1) Infect(core.Id, ticks, x.Pathogen, now);
            }
        Prune(_sickUntil, now);
        Prune(_immuneUntil, now);
        Prune(_carrierUntil, now);
        foreach (var id in _carrierUntil.Keys.Where(id => FindUnit(id) == null).ToList()) _carrierUntil.Remove(id); // gone (removed, founded)
        foreach (var id in _buildCarry.Keys.Where(id => !_sickUntil.ContainsKey(id)).ToList()) _buildCarry.Remove(id);
    }

    private static void Prune(SortedDictionary<int, long> map, long now)
    {
        if (map.Count == 0) return;
        foreach (var k in map.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) map.Remove(k);
    }

    /// <summary>Construction at a sick base runs at the crew's health: true when this tick's build work goes ahead.</summary>
    private bool BuildsThisTick(int baseId)
    {
        if (!IsSick(baseId)) return true;
        var carry = (_buildCarry.TryGetValue(baseId, out var c) ? c : Fix64.Zero) + CrewHealth(baseId);
        var go = carry >= Fix64.One;
        _buildCarry[baseId] = go ? carry - Fix64.One : carry;
        return go;
    }

    // ---------------------------------------------------------------- test panel

    private string? RuinDebugProblem(DebugRuinCommand command)
    {
        if (Ex == null) return "this data has no excavation rules";
        if (!OnMap(command.Tile)) return "off the map";
        var ruin = RuinAt(command.Tile);
        if (!command.Add)
        {
            if (ruin == null) return "no ruins there";
            if (_structureAt[command.Tile] != 0) return "a building is on the ruins (remove it first)";
            return null;
        }
        if (ruin != null) return "ruins are already there";
        var terrain = Map.Terrain(command.Tile);
        if (!terrain.Buildable || !terrain.Passable) return "can't put ruins on that ground";
        if (_structureAt[command.Tile] != 0) return "a building is there";
        if (Map.DepositResource[command.Tile] >= 0) return "a deposit is there";
        if (Map.NearNatives(command.Tile)) return "that's the natives' land";
        if (PodLandingNear(command.Tile)) return "a colony is landing there";
        if (_units.Any(u => u.Tile == command.Tile && u.Mode != UnitMode.Mobile)) return "a vehicle is unfolded there";
        return null;
    }

    private void ApplyRuinDebug(DebugRuinCommand command)
    {
        if (command.Add)
        {
            var dice = new RuinDice(Mix(Map.Seed ^ 0x5255494E53UL ^ ((ulong)(uint)command.Tile << 20)));
            _ruins.Add(NewRuin(Ex!, dice, command.Tile));
        }
        else _ruins.Remove(RuinAt(command.Tile)!);
    }

    // ---------------------------------------------------------------- hash

    private void AddExcavationToHash(ref StateHasher h)
    {
        if (_ruins.Count == 0 && _finds.Count == 0 && _sickUntil.Count == 0 && _immuneUntil.Count == 0 && _carrierUntil.Count == 0 &&
            _buildCarry.Count == 0 && _digBank.Count == 0 && _shuttleFactor.Count == 0 && _digModifiers.Count == 0) return; // maps without ruins keep their hashes
        h.Add(0x5255494EUL);
        h.Add(_ruins.Count);
        foreach (var r in _ruins)
        {
            h.Add(r.Tile);
            h.Add(r.Species);
            h.Add(r.Precursor ?? "");
            h.Add(r.Layers);
            h.Add(r.LayersDug);
            h.Add(r.LockedUntilTick);
            h.Add(r.Progress);
        }
        h.Add(_finds.Count);
        foreach (var f in _finds)
        {
            h.Add(f.Tick);
            h.Add(f.Player);
            h.Add(f.Tile);
            h.Add(f.Kind);
            h.Add(f.Name);
            h.Add(f.Effect);
        }
        foreach (var map in new[] { _sickUntil, _immuneUntil, _carrierUntil })
        {
            h.Add(map.Count);
            foreach (var (k, v) in map)
            {
                h.Add(k);
                h.Add(v);
            }
        }
        foreach (var map in new[] { _buildCarry, _digBank, _shuttleFactor })
        {
            h.Add(map.Count);
            foreach (var (k, v) in map)
            {
                h.Add(k);
                h.Add(v);
            }
        }
        h.Add(_digModifiers.Count);
        foreach (var (p, m) in _digModifiers)
        {
            h.Add(p);
            h.Add(m.Stat);
            h.Add(m.Add);
            h.Add(m.Buildings.Count);
            foreach (var b in m.Buildings) h.Add(b);
            h.Add(m.Units.Count);
            foreach (var u in m.Units) h.Add(u);
        }
    }
}
