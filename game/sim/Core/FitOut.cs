using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

// "Fit Out Your Ship", game side (docs/fit-out-contract.md; data/missions/fit_out.json). FitOutRules are pure functions
// of the data, used by the menu page and the sim alike so the page's summary and the game agree. The Simulation keeps
// each player's fit-out (set once by MatchSetup.CreateSystemGame(..., shipId, fitOut), hashed only when set) and answers
// FitOutFactor(player, lever) at the places gravity, weather and research already multiply. No fit-out (null) always
// means the game as it was: every factor is exactly 1 and nothing is added to the hash.

/// <summary>A player's fit-out: the focus id, the crew per division (heads, in the data's division order) and the cargo
/// (line id → count). Set once by MatchSetup.CreateSystemGame(..., shipId, fitOut).</summary>
public sealed record FitOut(string Focus, IReadOnlyList<int> Crew, IReadOnlyDictionary<string, int> Cargo);

/// <summary>Pure rules shared by the sim and the menu page, so the page's summary and the game agree.</summary>
public static class FitOutRules
{
    // The levers the sim reads (fit_out.json may list fewer; a lever it doesn't list stays at 1).
    public const string DriveSpeed = "drive_speed";
    public const string SatelliteSweep = "satellite_sweep";
    public const string DroneBattery = "drone_battery";
    public const string RoverHazard = "rover_hazard";
    public const string BuildTime = "build_time";
    public const string RepairSpeed = "repair_speed";
    public const string Fabrication = "fabrication";
    public const string Wear = "wear";
    public const string ResearchRate = "research_rate";
    public const string DigTime = "dig_time";
    public const string Sickness = "sickness";
    public const string Wildlife = "wildlife";
    public const string RuinDefence = "ruin_defence";
    public const string CrewLoss = "crew_loss";
    /// <summary>Damage a rover takes from a hazard (no division: cargo only).</summary>
    public const string HazardDamage = "hazard_damage";
    /// <summary>Damage a vehicle takes from a wildlife attack (no division: perk and cargo).</summary>
    public const string WildlifeDamage = "wildlife_damage";
    /// <summary>A vehicle's share of a woken ruin defence's damage (no division: perk).</summary>
    public const string RuinVehicleDamage = "ruin_vehicle_damage";
    /// <summary>The hub workshop's speed, on top of <see cref="Fabrication"/> (no division: perk).</summary>
    public const string HubWorkshop = "hub_workshop";
    /// <summary>Research points from missions (no division: perk).</summary>
    public const string MissionResearch = "mission_research";

    // The star-system levers: they move only on a ship that never lands (phase 2 of the contract).
    /// <summary>Long-range scan recharge (maps/system.json scan.cooldown_seconds).</summary>
    public const string ScanCooldown = "scan_cooldown";
    /// <summary>The orbital sensor sweep of a body not yet charted (sweep.seconds).</summary>
    public const string SweepTime = "sweep_time";
    /// <summary>Flight time between bodies (StarSystemState.TravelTicksFor).</summary>
    public const string FlightTime = "flight_time";
    /// <summary>Time to study a civilization (rules.json study seconds).</summary>
    public const string StudyTime = "study_time";
    /// <summary>The culture-shock chance at first contact (a multiplier on the percentage).</summary>
    public const string ShockChance = "shock_chance";
    /// <summary>Seconds a with_ship objective must hold (mission hold_seconds).</summary>
    public const string AlongsideTime = "alongside_time";
    /// <summary>Long-range scan reach (scan.range; no division: the Explorer's perk).</summary>
    public const string ScanRange = "scan_range";
    /// <summary>Space jobs: the Engineering crew's field repair of the hull (ship_status repairs.field).</summary>
    public const string FieldRepair = "field_repair";
    /// <summary>Space jobs: each away-team member's loss chance (Defense).</summary>
    public const string AwayLoss = "away_loss";
    /// <summary>Space jobs phase 2: asteroid mining speed (Engineering; the cutting_gear cargo adds to it).</summary>
    public const string Mining = "mining";

    /// <summary>Every lever id the sim reads; the data may only list these.</summary>
    public static readonly IReadOnlyList<string> KnownLevers = new[]
    {
        DriveSpeed, SatelliteSweep, DroneBattery, RoverHazard, BuildTime, RepairSpeed, Fabrication, Wear, ResearchRate, DigTime,
        Sickness, Wildlife, RuinDefence, CrewLoss, HazardDamage, WildlifeDamage, RuinVehicleDamage, HubWorkshop, MissionResearch,
        ScanCooldown, SweepTime, FlightTime, StudyTime, ShockChance, AlongsideTime, ScanRange, FieldRepair, AwayLoss, Mining,
    };

    /// <summary>The levers of the star-system layer (the data marks them "space": true; they move only on a ship that never
    /// lands, and the others only on a colony ship).</summary>
    public static readonly IReadOnlyList<string> SpaceLevers = new[] { ScanCooldown, SweepTime, FlightTime, StudyTime, ShockChance, AlongsideTime, ScanRange, FieldRepair, AwayLoss, Mining };

    /// <summary>Whether this is a fitted-out ship that never lands (its cargo and levers are the space ones).</summary>
    public static bool IsSpace(GameData data, string? shipId) => data.FitOut?.Ship(shipId) != null && data.ShipChoice(shipId) is { Colonizes: false };

    /// <summary>No lever's multiplier goes below this, whatever the data says.</summary>
    public static readonly Fix64 LeastFactor = Fix64.Ratio(1, 20);

    /// <summary>Whether this ship is fitted out at all (the data lists it).</summary>
    public static bool Applies(GameData data, string? shipId) => data.FitOut?.Ship(shipId) != null;

    /// <summary>The focus's preset for this ship: a legal fit-out, or null when the ship is not fitted out (or the focus is
    /// unknown). Crew: the focus's percentages in heads, rounded by largest remainder and never below a floor. Cargo: the
    /// ship's base load, then its fill order for this focus, one item at a time, round and round, while the line is under
    /// its max and the hold has room.</summary>
    public static FitOut? Preset(GameData data, string shipId, string focusId)
    {
        if (data.FitOut is not { } rules || rules.Ship(shipId) is not { } ship || rules.Focus(focusId) is not { } focus) return null;
        return new FitOut(focus.Id, PresetCrew(rules, ship, focus), PresetCargo(rules, ship, focus, IsSpace(data, shipId)));
    }

    private static int[] PresetCrew(FitOutData rules, FitOutShip ship, FitOutFocus focus)
    {
        var n = rules.Divisions.Count;
        var crew = ship.Crew;
        var heads = new int[n];
        var floors = new int[n];
        var want = new long[n]; // the focus's share of the crew in hundredths of a head
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            floors[i] = Floor(rules, ship, focus, i);
            want[i] = (long)(i < focus.CrewPct.Count ? focus.CrewPct[i] : 0) * crew;
            heads[i] = System.Math.Max(floors[i], (int)(want[i] / 100));
            sum += heads[i];
        }
        for (var guard = 0; sum != crew && guard < 100_000; guard++)
        {
            // Short: one more head where the rounding lost most. Over (floors pushed it up): one fewer where it gained most.
            var best = -1;
            var bestValue = long.MinValue;
            for (var i = 0; i < n; i++)
            {
                if (sum > crew && heads[i] <= floors[i]) continue;
                var v = sum < crew ? want[i] - heads[i] * 100L : heads[i] * 100L - want[i];
                if (v <= bestValue) continue;
                bestValue = v;
                best = i;
            }
            if (best < 0) break;
            var step = sum < crew ? 1 : -1;
            heads[best] += step;
            sum += step;
        }
        return heads;
    }

    private static Dictionary<string, int> PresetCargo(FitOutData rules, FitOutShip ship, FitOutFocus focus, bool space)
    {
        // Only the lines this kind of ship carries (a colony ship the landing lines, a ship that never lands the space ones).
        var cargo = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in rules.LinesFor(space)) cargo[l.Id] = 0;
        foreach (var b in ship.Base)
            if (cargo.ContainsKey(b.Line)) cargo[b.Line] = b.Count;
        var used = 0;
        foreach (var l in rules.LinesFor(space)) used += cargo[l.Id] * l.Containers;
        if (!ship.Fill.TryGetValue(focus.Id, out var order)) return cargo;
        for (var pass = 0; pass < 64; pass++)
        {
            var added = false;
            foreach (var id in order)
            {
                if (rules.Line(id) is not { } l || !cargo.ContainsKey(l.Id) || cargo[l.Id] >= l.Max || used + l.Containers > ship.Hold) continue;
                cargo[l.Id]++;
                used += l.Containers;
                added = true;
            }
            if (!added) break;
        }
        return cargo;
    }

    /// <summary>How many of a cargo line this fit-out carries (0 for a line it doesn't name).</summary>
    public static int CargoCount(FitOut fitOut, string lineId) =>
        fitOut.Cargo != null && fitOut.Cargo.TryGetValue(lineId, out var n) ? n : 0;

    /// <summary>Why this fit-out is not allowed ("Science needs at least 12 crew"), or null.</summary>
    public static string? Problem(GameData data, string shipId, FitOut fitOut)
    {
        if (data.FitOut is not { } rules) return "this game has no fit-out rules";
        if (rules.Ship(shipId) is not { } ship) return $"the {data.ShipChoice(shipId)?.Name ?? shipId} is not fitted out";
        if (fitOut == null) return "no fit-out";
        if (rules.Focus(fitOut.Focus) is not { } focus) return $"unknown focus '{fitOut.Focus}'";
        if (fitOut.Crew == null || fitOut.Crew.Count != rules.Divisions.Count) return $"the crew list needs one number for each of the {rules.Divisions.Count} divisions";
        if (fitOut.Cargo == null) return "no cargo list";

        var assigned = 0L;
        for (var i = 0; i < rules.Divisions.Count; i++)
        {
            var floor = Floor(rules, ship, focus, i);
            if (fitOut.Crew[i] < floor) return $"{rules.Divisions[i].Name} needs at least {floor} crew";
            assigned += fitOut.Crew[i];
        }
        if (assigned < ship.Crew) return ship.Crew - assigned == 1 ? "1 of the crew is still unassigned" : $"{ship.Crew - assigned} of the crew are still unassigned";
        if (assigned > ship.Crew) return $"{assigned} crew assigned, and the ship has {ship.Crew}";

        var space = IsSpace(data, shipId);
        foreach (var id in fitOut.Cargo.Keys.OrderBy(k => k, StringComparer.Ordinal)) // sorted: the same sentence on every machine
        {
            if (rules.Line(id) is not { } line) return $"unknown cargo '{id}'";
            if (line.Space != space && CargoCount(fitOut, id) != 0)
                return space ? $"{line.Name} lands with a colony, and the {data.ShipChoice(shipId)?.Name ?? shipId} never lands"
                    : $"{line.Name} is for a ship that never lands";
        }
        var used = 0L;
        foreach (var l in rules.LinesFor(space))
        {
            var n = CargoCount(fitOut, l.Id);
            if (n < 0) return $"{l.Name}: the count can't be below 0";
            if (n < l.Min) return $"{l.Name}: at least {l.Min} must be loaded";
            if (n > l.Max) return $"{l.Name}: at most {l.Max} can be loaded";
            used += (long)n * l.Containers;
        }
        foreach (var g in rules.Groups)
        {
            var held = 0L;
            foreach (var l in rules.LinesFor(space))
                if (l.Group == g.Id) held += (long)CargoCount(fitOut, l.Id) * l.Containers;
            if (held < g.FloorFor(space)) return $"{g.Name} needs at least {g.FloorFor(space)} containers (it has {held})";
        }
        if (used > ship.Hold) return $"the hold takes {ship.Hold} containers, and this load needs {used}";
        return null;
    }

    /// <summary>The least crew this division may hold under this focus (0 for a ship, focus or division the data lacks).</summary>
    public static int CrewFloor(GameData data, string shipId, string focusId, string divisionId)
    {
        if (data.FitOut is not { } rules || rules.Ship(shipId) is not { } ship || rules.Focus(focusId) is not { } focus) return 0;
        var i = rules.DivisionIndex(divisionId);
        return i < 0 ? 0 : Floor(rules, ship, focus, i);
    }

    /// <summary>The floor in heads: the share of the crew, rounded up (at least 1).</summary>
    private static int Floor(FitOutData rules, FitOutShip ship, FitOutFocus focus, int division)
    {
        var share = focus.Division == rules.Divisions[division].Id ? rules.FocusFloorShare : rules.CrewFloorShare;
        var heads = Fix64.FromInt(ship.Crew) * share - Fix64.Ratio(1, 1000); // 0.10 isn't exact in binary: 12.0000001 is 12
        var whole = heads.FloorToInt();
        if (Fix64.FromInt(whole) < heads) whole++;
        return System.Math.Max(1, whole);
    }

    /// <summary>Containers the cargo takes.</summary>
    public static int ContainersUsed(GameData data, FitOut fitOut)
    {
        if (data.FitOut is not { } rules || fitOut?.Cargo == null) return 0;
        var used = 0;
        foreach (var l in rules.Cargo) used += CargoCount(fitOut, l.Id) * l.Containers;
        return used;
    }

    /// <summary>Containers the cargo takes in one group.</summary>
    public static int ContainersUsed(GameData data, FitOut fitOut, string groupId)
    {
        if (data.FitOut is not { } rules || fitOut?.Cargo == null) return 0;
        var used = 0;
        foreach (var l in rules.Cargo)
            if (l.Group == groupId) used += CargoCount(fitOut, l.Id) * l.Containers;
        return used;
    }

    /// <summary>A division's strength: EffectPerPoint × (its share in percent − 25). 0 with an even split.</summary>
    public static Fix64 Strength(GameData data, string shipId, FitOut fitOut, string divisionId)
    {
        if (data.FitOut is not { } rules || rules.Ship(shipId) is not { } ship || fitOut?.Crew == null || ship.Crew <= 0) return Fix64.Zero;
        var i = rules.DivisionIndex(divisionId);
        if (i < 0 || i >= fitOut.Crew.Count) return Fix64.Zero;
        var share = Fix64.FromInt(100 * fitOut.Crew[i]) / Fix64.FromInt(ship.Crew);
        return rules.EffectPerPoint * (share - rules.EvenSharePct);
    }

    /// <summary>The multiplier for a lever (crew strength, the focus's perk and cargo that acts on it all included).
    /// 1 with no fit-out or an unknown lever.</summary>
    public static Fix64 Factor(GameData data, string? shipId, FitOut? fitOut, string leverId)
    {
        if (fitOut == null || shipId == null || data.FitOut is not { } rules || rules.Ship(shipId) == null || rules.Lever(leverId) is not { } lever) return Fix64.One;
        var space = IsSpace(data, shipId);
        if (lever.Space != space) return Fix64.One; // a colony lever on a ship that never lands, or the other way round
        var factor = Fix64.One;
        if (lever.Division.Length > 0)
        {
            var strength = Strength(data, shipId, fitOut, lever.Division);
            factor = lever.MoreIsBetter ? Fix64.One + strength : Fix64.One - strength;
        }
        if (rules.Focus(fitOut.Focus) is { } focus)
            foreach (var e in focus.PerkLeversFor(space))
                if (e.Lever == leverId) factor *= e.Value;
        var cargo = Fix64.One;
        foreach (var l in rules.LinesFor(space))
            foreach (var e in l.Levers)
                if (e.Lever == leverId) cargo += e.Value * CargoCount(fitOut, l.Id);
        factor *= cargo;
        return Fix64.Max(LeastFactor, factor);
    }

    /// <summary>Hexes the landing reveals round the hub under this fit-out's perk, or 0 (the landing's own radius).</summary>
    public static int LandingReveal(GameData data, FitOut? fitOut) => fitOut == null ? 0 : data.FitOut?.Focus(fitOut.Focus)?.LandingReveal ?? 0;

    /// <summary>Survey satellites this fit-out lets the player launch without paying.</summary>
    public static int FreeSatellites(GameData data, FitOut? fitOut)
    {
        if (fitOut == null || data.FitOut is not { } rules) return 0;
        var n = 0;
        foreach (var l in rules.Cargo) n += l.Satellites * CargoCount(fitOut, l.Id);
        return n;
    }

    /// <summary>Body satellites (SystemSatelliteCommand) this fit-out lets the player place without paying (space cargo).</summary>
    public static int FreeSystemSatellites(GameData data, FitOut? fitOut) => SumSpace(data, fitOut, l => l.SystemSatellites);

    /// <summary>Hidden observation posts (ObservationPostCommand) this fit-out lets the player place without paying.</summary>
    public static int FreePosts(GameData data, FitOut? fitOut) => SumSpace(data, fitOut, l => l.Posts);

    /// <summary>Long-range scans this fit-out's sensor probes make with no recharge wait.</summary>
    public static int Probes(GameData data, FitOut? fitOut) => SumSpace(data, fitOut, l => l.Probes);

    /// <summary>Percentage points the cargo adds to the culture-shock chance at first contact (gifts: below 0).</summary>
    public static int ShockPoints(GameData data, FitOut? fitOut) => SumSpace(data, fitOut, l => l.ShockPoints);

    private static int SumSpace(GameData data, FitOut? fitOut, Func<FitOutCargoLine, int> each)
    {
        if (fitOut == null || data.FitOut is not { } rules) return 0;
        var n = 0;
        foreach (var l in rules.LinesFor(true)) n += each(l) * CargoCount(fitOut, l.Id);
        return n;
    }

    /// <summary>Research points this fit-out banks toward the first tech researched.</summary>
    public static int ResearchBanked(GameData data, FitOut? fitOut)
    {
        if (fitOut == null || data.FitOut is not { } rules) return 0;
        var n = 0;
        foreach (var l in rules.Cargo) n += l.Research * CargoCount(fitOut, l.Id);
        return n;
    }

    /// <summary>The vehicles the cargo adds to the landing party (unit ids, in cargo-line order).</summary>
    public static IReadOnlyList<string> CargoUnits(GameData data, FitOut? fitOut)
    {
        var list = new List<string>();
        if (fitOut == null || data.FitOut is not { } rules) return list;
        foreach (var l in rules.Cargo)
            if (l.Unit.Length > 0)
                for (var i = CargoCount(fitOut, l.Id); i > 0; i--) list.Add(l.Unit);
        return list;
    }

    /// <summary>What the cargo puts into the landing stock (resource id, amount), in cargo-line order.</summary>
    public static IReadOnlyList<FitOutStock> CargoStock(GameData data, FitOut? fitOut)
    {
        var list = new List<FitOutStock>();
        if (fitOut == null || data.FitOut is not { } rules) return list;
        foreach (var l in rules.Cargo)
        {
            var n = CargoCount(fitOut, l.Id);
            if (n <= 0) continue;
            foreach (var s in l.Stock) list.Add(new FitOutStock(s.Resource, s.Amount * n));
        }
        return list;
    }

    /// <summary>The whole landing stock this fit-out gives (the base start stock, the ship's own bonus and the cargo), for the
    /// page's summary: resource id → amount. The start stock's replaced resources (duranium, tritanium, deuterium) come
    /// from the cargo alone.</summary>
    public static IReadOnlyDictionary<string, int> LandingStock(GameData data, string shipId, FitOut fitOut)
    {
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string id, int n) => stock[id] = (stock.TryGetValue(id, out var v) ? v : 0) + n;
        var fitted = fitOut != null && data.FitOut?.Ship(shipId) != null && !IsSpace(data, shipId); // nothing lands from a ship that never lands
        foreach (var a in data.Landing.StartStock)
        {
            var id = data.Resources[a.Resource].Id;
            if (fitted && data.FitOut!.ReplacesStartStock.Contains(id))
            {
                if (!stock.ContainsKey(id)) stock[id] = 0;
                continue;
            }
            Add(id, a.Amount.FloorToInt());
        }
        if (data.ShipChoice(shipId) is { Colonizes: true } ship)
            foreach (var (res, n) in ship.StartBonus.OrderBy(b => b.Key, StringComparer.Ordinal)) Add(res, n);
        if (fitted)
            foreach (var s in CargoStock(data, fitOut)) Add(s.Resource, s.Amount);
        return stock;
    }

    /// <summary>Every vehicle that lands with the colony under this fit-out (unit ids, the usual landing party included),
    /// in landing order: the landing's own, the ship's start units, the cargo's.</summary>
    public static IReadOnlyList<string> LandingUnits(GameData data, string shipId, FitOut fitOut)
    {
        var list = new List<string>(data.Landing.Units);
        if (data.ShipChoice(shipId) is { Colonizes: true } ship) list.AddRange(ship.StartUnits);
        if (data.FitOut?.Ship(shipId) != null && !IsSpace(data, shipId)) list.AddRange(CargoUnits(data, fitOut));
        return list;
    }
}

public sealed partial class Simulation
{
    /// <summary>One player's fit-out and what is left of the things it brought. Factors are worked out once per GameData
    /// (the test panel can swap the data), not state.</summary>
    private sealed class FitOutState
    {
        public string ShipId = "";
        public FitOut FitOut = null!;
        public int FreeSatellites;
        /// <summary>A ship that never lands: what is left of its body satellites, posts and probes (hashed only for such a ship).</summary>
        public bool Space;
        public int FreeSystemSatellites, FreePosts, Probes;
        public GameData? FactorsFor;
        public Dictionary<string, Fix64> Factors = new(StringComparer.Ordinal);
    }

    /// <summary>Per player slot (null = none), only as long as the highest slot that has one. Empty in every game without a
    /// fit-out: then nothing here is read or hashed.</summary>
    private readonly List<FitOutState?> _fitOuts = new();

    private FitOutState? FitOutStateOf(int player) => player >= 0 && player < _fitOuts.Count ? _fitOuts[player] : null;

    /// <summary>This player's fit-out, or null (none: the game plays as it always did).</summary>
    public FitOut? FitOutOf(int player) => FitOutStateOf(player)?.FitOut;

    /// <summary>The multiplier this player's fit-out puts on a lever (1 with none).</summary>
    public Fix64 FitOutFactor(int player, string leverId)
    {
        // Space jobs: a flagship that lost or has injured crew moves its levers by the crew on duty (Space/ShipStatus.cs).
        if (System is { } system && system.CrewFactor(player, leverId) is { } crew) return crew;
        if (_fitOuts.Count == 0 || FitOutStateOf(player) is not { } s) return Fix64.One;
        if (!ReferenceEquals(s.FactorsFor, Data))
        {
            s.Factors.Clear();
            foreach (var id in FitOutRules.KnownLevers) s.Factors[id] = FitOutRules.Factor(Data, s.ShipId, s.FitOut, id);
            s.FactorsFor = Data;
        }
        return s.Factors.TryGetValue(leverId, out var f) ? f : Fix64.One;
    }

    /// <summary>Survey satellites this player may still launch without paying (the fit-out's cargo; 0 with none).</summary>
    public int FitOutFreeSatellites(int player) => FitOutStateOf(player)?.FreeSatellites ?? 0;

    /// <summary>Uses one of the fit-out's ready satellites; false when there is none (then the launch is paid for).</summary>
    internal bool UseFreeSatellite(int player)
    {
        if (FitOutStateOf(player) is not { FreeSatellites: > 0 } s) return false;
        s.FreeSatellites--;
        return true;
    }

    /// <summary>Body satellites this player may still place without paying (a space fit-out's cargo; 0 with none).</summary>
    public int FitOutFreeSystemSatellites(int player) => FitOutStateOf(player)?.FreeSystemSatellites ?? 0;

    /// <summary>Uses one of the fit-out's ready body satellites; false when there is none (then it is paid for).</summary>
    internal bool UseFreeSystemSatellite(int player)
    {
        if (FitOutStateOf(player) is not { FreeSystemSatellites: > 0 } s) return false;
        s.FreeSystemSatellites--;
        return true;
    }

    /// <summary>Hidden observation posts this player may still place without paying (0 with none).</summary>
    public int FitOutFreePosts(int player) => FitOutStateOf(player)?.FreePosts ?? 0;

    /// <summary>Uses one of the fit-out's ready posts; false when there is none (then it is paid for).</summary>
    internal bool UseFreePost(int player)
    {
        if (FitOutStateOf(player) is not { FreePosts: > 0 } s) return false;
        s.FreePosts--;
        return true;
    }

    /// <summary>Sensor probes this player has left: long-range scans with no recharge wait (0 with none).</summary>
    public int FitOutProbes(int player) => FitOutStateOf(player)?.Probes ?? 0;

    /// <summary>Uses a sensor probe; false when there is none left.</summary>
    internal bool UseProbe(int player)
    {
        if (FitOutStateOf(player) is not { Probes: > 0 } s) return false;
        s.Probes--;
        return true;
    }

    /// <summary>Percentage points this player's cargo adds to the culture-shock chance at first contact (0 with none).</summary>
    public int FitOutShockPoints(int player) => FitOutStateOf(player) is { } s ? FitOutRules.ShockPoints(Data, s.FitOut) : 0;

    /// <summary>Hexes the landing reveals round the hub: the fit-out's perk, or 0 (the landing's own radius).</summary>
    public int FitOutLandingReveal(int player) => FitOutRules.LandingReveal(Data, FitOutOf(player));

    /// <summary>The vehicles this player's cargo adds to the landing party (empty with none).</summary>
    public IReadOnlyList<string> FitOutUnits(int player) =>
        FitOutStateOf(player) is { } s && FitOutRules.Applies(Data, s.ShipId) ? FitOutRules.CargoUnits(Data, s.FitOut) : Array.Empty<string>();

    /// <summary>Records the fit-out (MatchSetup, before the player is added and before tick 0). A copy is kept, so the
    /// caller's lists can change afterwards.</summary>
    internal void SetFitOut(int player, string shipId, FitOut fitOut)
    {
        while (_fitOuts.Count <= player) _fitOuts.Add(null);
        var cargo = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, n) in fitOut.Cargo)
            if (n != 0) cargo[id] = n;
        var copy = new FitOut(fitOut.Focus, fitOut.Crew.ToArray(), cargo);
        _fitOuts[player] = new FitOutState
        {
            ShipId = shipId, FitOut = copy, FreeSatellites = FitOutRules.FreeSatellites(Data, copy), Space = FitOutRules.IsSpace(Data, shipId),
            FreeSystemSatellites = FitOutRules.FreeSystemSatellites(Data, copy), FreePosts = FitOutRules.FreePosts(Data, copy), Probes = FitOutRules.Probes(Data, copy),
        };
    }

    /// <summary>The fit-outs, only when one is set (games without one keep the hashes they always had): ship, focus, crew,
    /// cargo by line id, the ready satellites left and, for a ship that never lands, its body satellites, posts and probes left.</summary>
    private void AddFitOutToHash(ref StateHasher h)
    {
        if (_fitOuts.Count == 0) return;
        h.Add("fit_out");
        h.Add(_fitOuts.Count);
        foreach (var s in _fitOuts)
        {
            h.Add(s != null);
            if (s == null) continue;
            h.Add(s.ShipId);
            h.Add(s.FitOut.Focus);
            h.Add(s.FitOut.Crew.Count);
            foreach (var c in s.FitOut.Crew) h.Add(c);
            h.Add(s.FitOut.Cargo.Count);
            foreach (var (id, n) in s.FitOut.Cargo) // a SortedDictionary (SetFitOut): ordinal order
            {
                h.Add(id);
                h.Add(n);
            }
            h.Add(s.FreeSatellites);
            if (!s.Space) continue; // colony fit-outs hash as they did in phase 1
            h.Add(s.FreeSystemSatellites);
            h.Add(s.FreePosts);
            h.Add(s.Probes);
        }
    }
}
