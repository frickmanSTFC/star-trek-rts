using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Space jobs, phase 1 (docs/space-jobs-contract.md; data maps/ship_status.json "ship_status"): a player's flagship's hull
// (0..100 in bands), four systems a hazard can knock out, crew by division (on duty / injured / lost, from the fit-out),
// its hold (patch kits, medical stores, salvage, survivors; the fit-out's space cargo shown live) and a log. Field repairs,
// patch kits and the station's dockyard mend it; at 0 it is crippled (adrift; space jobs fail) and recovers slowly.
// Hashed with the star system (system games only).

/// <summary>Ship systems a hazard can knock out.</summary>
public enum ShipSystem { Sensors, Engines, Tractor, LifeSupport }

/// <summary>Hull bands (ship_status.json): Scuffed 75-100, Worn 50-75, Damaged 25-50, Critical 1-25, Crippled 0.</summary>
public enum HullBand { Scuffed, Worn, Damaged, Critical, Crippled }

/// <summary>A knocked-out system: which, and when it comes back on its own (sim tick).</summary>
public sealed record SystemDown(ShipSystem System, long BackTick);

/// <summary>Crew of one division: On duty, Injured (back later), Lost (gone for the match), the floor it shouldn't drop below.</summary>
public sealed record CrewLine(string Division, int OnDuty, int Injured, int Lost, int Floor);

/// <summary>One kind of thing in the hold: Item id ("patch_kit", "probe", "duranium_ore", "salvage:power_cell",
/// "survivors"…), Count, Containers it takes, Label.</summary>
public sealed record HoldLine(string Item, int Count, int Containers, string Label);

/// <summary>A line of the ship's damage / crew log (newest first in the view): Tick, Text, Tone "" | "warn" | "bad" | "good".</summary>
public sealed record ShipLogLine(long Tick, string Text, string Tone);

/// <summary>A flagship's state for the status screen.</summary>
public sealed record ShipStatusView(int ShipId, Fix64 Hull, HullBand Band, string BandEffect, IReadOnlyList<SystemDown> Down,
    IReadOnlyList<CrewLine> Crew, int CrewTotal, int CrewStart, IReadOnlyList<HoldLine> Hold, int HoldUsed, int HoldSize,
    bool Crippled, int FieldRepairTo, IReadOnlyList<ShipLogLine> Log)
{
    /// <summary>A repair running now: "patch" (a hull patch kit being fitted) or "dock" (at a dockyard), "" for none; its
    /// share done and seconds left.</summary>
    public string Repair { get; init; } = "";
    public Fix64 RepairShare { get; init; }
    public int RepairSecondsLeft { get; init; }

    /// <summary>Crippled: seconds until the hull is back to the crippled.recover_to share (0 otherwise).</summary>
    public int CrippledSecondsLeft { get; init; }

    /// <summary>Lines ever written to the log (the view can tell new lines by it; the log keeps only the newest).</summary>
    public int LogCount { get; init; }

    /// <summary>Injured crew and when each comes back (sim tick), oldest first.</summary>
    public IReadOnlyList<(string Division, long BackTick)> InjuredBack { get; init; } = new List<(string, long)>();
}

public sealed partial class StarSystemState
{
    /// <summary>A flagship's running state (per player slot).</summary>
    private sealed class ShipState
    {
        public string ShipKey = "";
        public Fix64 Hull = Fix64.FromInt(100);
        public bool Crippled;
        public long CrippledTick = -1;
        public readonly List<SystemDown> Down = new();
        public int[] Start = System.Array.Empty<int>(), OnDuty = System.Array.Empty<int>(), Lost = System.Array.Empty<int>(), Floors = System.Array.Empty<int>();
        /// <summary>Injured members (division, back tick), kept sorted by tick then division.</summary>
        public readonly List<(int Division, long Back)> Injured = new();
        public int HoldSize;
        /// <summary>What the hold carries besides the fit-out's own lines: patch_kit, medical, salvage:&lt;id&gt;,
        /// survivors:&lt;division&gt;. Sorted by item (ordinal).</summary>
        public readonly List<(string Item, int Count)> Hold = new();
        public string RepairAction = "";
        public long RepairStart = -1, RepairEnd = -1;
        public readonly List<ShipLogLine> Log = new();
        public int LogCount;
        public int HiredThisVisit;
        public int TeamsSent;
        public ScanRun? Scan;
        public WorkRun? Work;
        public AwayRun? Away;
        public readonly List<SiteRecord> Records = new();
        /// <summary>Phase 2: the site in tow ("" = none) and since when; what was sold, dropped, mined and towed in, by key
        /// ("sold:ore:duranium_ore", "drop:ore:deuterium", "mined:ore:tritanium_ore", "tow:station", "tow:colony"), sorted.</summary>
        public string Tow = "";
        public long TowTick = -1;
        public readonly List<(string Key, int Count)> Tally = new();
        /// <summary>Phase 3: the stranded ship under escort ("" = none) and since when (SpaceEvents.cs).</summary>
        public string Escort = "";
        public long EscortTick = -1;

        // Not state: the crew factors cache (worked out from the crew above).
        public bool CrewDirty;
        public GameData? FactorsFor;
        public readonly Dictionary<string, Fix64> Factors = new(System.StringComparer.Ordinal);

        public int CrewTotal => OnDuty.Sum() + Injured.Count;
        public int StartTotal => Start.Sum();

        public void AddToHash(ref StateHasher h)
        {
            h.Add(ShipKey);
            h.Add(Hull);
            h.Add(Crippled);
            h.Add(CrippledTick);
            h.Add(Down.Count);
            foreach (var d in Down)
            {
                h.Add((int)d.System);
                h.Add(d.BackTick);
            }
            foreach (var arr in new[] { Start, OnDuty, Lost, Floors })
            {
                h.Add(arr.Length);
                foreach (var x in arr) h.Add(x);
            }
            h.Add(Injured.Count);
            foreach (var (d, b) in Injured)
            {
                h.Add(d);
                h.Add(b);
            }
            h.Add(HoldSize);
            h.Add(Hold.Count);
            foreach (var (item, n) in Hold)
            {
                h.Add(item);
                h.Add(n);
            }
            h.Add(RepairAction);
            h.Add(RepairStart);
            h.Add(RepairEnd);
            h.Add(Log.Count);
            foreach (var l in Log)
            {
                h.Add(l.Tick);
                h.Add(l.Text);
                h.Add(l.Tone);
            }
            h.Add(LogCount);
            h.Add(HiredThisVisit);
            h.Add(TeamsSent);
            h.Add(Scan != null);
            Scan?.AddToHash(ref h);
            h.Add(Work != null);
            Work?.AddToHash(ref h);
            h.Add(Away != null);
            Away?.AddToHash(ref h);
            h.Add(Records.Count);
            foreach (var r in Records) r.AddToHash(ref h);
            if (Tow.Length > 0 || Tally.Count > 0) // phase 2 (a ship that never mined, sold or towed keeps its hash)
            {
                h.Add("p2");
                h.Add(Tow);
                h.Add(TowTick);
                h.Add(Tally.Count);
                foreach (var (k, n) in Tally)
                {
                    h.Add(k);
                    h.Add(n);
                }
            }
            if (Escort.Length > 0) // phase 3
            {
                h.Add("p3");
                h.Add(Escort);
                h.Add(EscortTick);
            }
        }
    }

    private sealed class ScanRun
    {
        public string Site = "";
        public long Start, End;

        public void AddToHash(ref StateHasher h)
        {
            h.Add(Site);
            h.Add(Start);
            h.Add(End);
        }
    }

    /// <summary>Per player slot (null = none: no ship status data, or a slot without a flagship).</summary>
    private readonly List<ShipState?> _status = new();

    private ShipStatusRules? StatusRules => Data?.ShipStatus;

    private ShipState? Status(int player) => player >= 0 && player < _status.Count ? _status[player] : null;

    /// <summary>Hold item ids.</summary>
    public const string PatchKitItem = "patch_kit", MedicalItem = "medical", SalvagePrefix = "salvage:", SurvivorsPrefix = "survivors:";

    /// <summary>Phase 2 hold items: ore by resource ("ore:duranium_ore", counted in units, ore.per_container a container),
    /// and the site in tow ("tow": a line of no containers while something is in tow).</summary>
    public const string OrePrefix = "ore:", TowItem = "tow";

    /// <summary>Phase 3: the stranded ship under escort (a line of no containers while one follows the ship; jettison =
    /// stop the escort).</summary>
    public const string EscortItem = "escort";

    private static string SalvageItemId(string salvage) => SalvagePrefix + salvage;

    // ---------------------------------------------------------------- set-up

    /// <summary>A new player's flagship status: crew from its fit-out (or its ship's suggested preset, or default_ship),
    /// the hold's size, patch kits and medical stores from the space cargo (or default_patch_kits without a fit-out).</summary>
    private void AddShipStatus(Simulation sim, int slot)
    {
        while (_status.Count <= slot) _status.Add(null);
        if (sim.Data.ShipStatus is not { } rules || sim.Data.FitOut is not { } fit) return;
        var pick = sim.ShipChoiceOf(slot);
        var fitOut = sim.FitOutOf(slot);
        var shipDef = fit.Ship(pick?.Id);
        var st = new ShipState { ShipKey = pick?.Id ?? "" };
        int[] crew;
        string focus;
        if (shipDef != null && fitOut != null)
        {
            crew = fitOut.Crew.ToArray();
            focus = fitOut.Focus;
        }
        else if (shipDef != null && FitOutRules.Preset(sim.Data, shipDef.ShipId, shipDef.Suggest) is { } preset)
        {
            crew = preset.Crew.ToArray();
            focus = preset.Focus;
        }
        else
        {
            crew = rules.DefaultCrew.Select(c => System.Math.Max(0, c)).ToArray();
            focus = rules.DefaultFocus;
        }
        var n = fit.Divisions.Count;
        if (crew.Length != n) crew = Enumerable.Repeat(1, n).ToArray();
        st.Start = crew.ToArray();
        st.OnDuty = crew.ToArray();
        st.Lost = new int[n];
        st.Floors = new int[n];
        var floorShip = shipDef?.ShipId ?? "";
        for (var i = 0; i < n; i++)
        {
            var floor = shipDef != null ? FitOutRules.CrewFloor(sim.Data, floorShip, focus, fit.Divisions[i].Id) : 0;
            if (floor <= 0) // no fit-out record: the shares as the fit-out would round them
            {
                var share = fit.Focus(focus)?.Division == fit.Divisions[i].Id ? fit.FocusFloorShare : fit.CrewFloorShare;
                floor = System.Math.Max(1, (Fix64.FromInt(crew.Sum()) * share).FloorToInt());
            }
            st.Floors[i] = floor;
        }
        st.HoldSize = shipDef?.Hold ?? rules.DefaultHold;
        if (fitOut != null && shipDef != null)
            foreach (var line in fit.LinesFor(FitOutRules.IsSpace(sim.Data, shipDef.ShipId)))
            {
                var count = FitOutRules.CargoCount(fitOut, line.Id);
                if (count <= 0) continue;
                if (line.PatchKits > 0) Add(st, PatchKitItem, line.PatchKits * count);
                if (line.Medical > 0) Add(st, MedicalItem, line.Medical * count);
            }
        else if (rules.DefaultPatchKits > 0) Add(st, PatchKitItem, rules.DefaultPatchKits);
        _status[slot] = st;
        foreach (var s in _sites) // the station is on every chart
            if (s.Kind == SiteKind.Station) SetKnowledge(slot, s.Id, SiteKnowledge.Identified);
    }

    // ---------------------------------------------------------------- the view

    /// <summary>The status of a player's flagship, or null (no system / not built yet).</summary>
    public ShipStatusView? StatusOf(int player)
    {
        if (Status(player) is not { } st || FlagshipOf(player) is not { } ship || StatusRules is not { } rules) return null;
        var band = BandFor(rules, st);
        var crew = new List<CrewLine>();
        var ids = DivisionIds();
        for (var i = 0; i < st.OnDuty.Length && i < ids.Count; i++)
            crew.Add(new CrewLine(ids[i], st.OnDuty[i], st.Injured.Count(x => x.Division == i), st.Lost[i], st.Floors[i]));
        var hold = HoldLines(player, st);
        return new ShipStatusView(ship.Id, st.Hull, band?.Band ?? HullBand.Scuffed, BandEffect(st, band), st.Down.ToList(), crew, st.CrewTotal, st.StartTotal,
            hold, hold.Sum(l => l.Containers), st.HoldSize, st.Crippled, rules.FieldUpTo, st.Log.ToList())
        {
            Repair = st.RepairAction,
            RepairShare = st.RepairAction.Length > 0 ? Share(st.RepairStart, st.RepairEnd) : Fix64.Zero,
            RepairSecondsLeft = st.RepairAction.Length > 0 ? SecondsUntil(st.RepairEnd) : 0,
            CrippledSecondsLeft = st.Crippled ? CrippledSeconds(st) : 0,
            LogCount = st.LogCount,
            InjuredBack = st.Injured.Select(x => (x.Division < ids.Count ? ids[x.Division] : "", x.Back)).ToList(),
        };
    }

    private string BandEffect(ShipState st, HullBandDef? band)
    {
        var parts = new List<string> { band?.Text ?? "" };
        if (st.Down.Count > 0) parts.Add(string.Join(", ", st.Down.Select(d => $"{StatusRules?.System(d.System)?.Name ?? d.System.ToString()} down")));
        if (st.CrewTotal * 2 < st.StartTotal) parts.Add("fewer than half the crew: flights slower");
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    /// <summary>The hold as lines: the fit-out's space cargo still aboard (probes, satellites, posts, reaction mass, gifts),
    /// then patch kits, medical stores, salvage and survivors.</summary>
    private List<HoldLine> HoldLines(int player, ShipState st)
    {
        var list = new List<HoldLine>();
        var data = Data;
        if (_sim != null && data?.FitOut is { } fit && _sim.FitOutOf(player) is { } fitOut && fit.Ship(st.ShipKey) is { } shipDef)
            foreach (var line in fit.LinesFor(FitOutRules.IsSpace(data, shipDef.ShipId)))
            {
                if (line.PatchKits > 0 || line.Medical > 0) continue; // counted below, as they are used
                var n = line.Probes > 0 ? _sim.FitOutProbes(player)
                    : line.SystemSatellites > 0 ? _sim.FitOutFreeSystemSatellites(player)
                    : line.Posts > 0 ? _sim.FitOutFreePosts(player)
                    : FitOutRules.CargoCount(fitOut, line.Id);
                if (n > 0) list.Add(new HoldLine(line.Id, n, n * line.Containers, line.Name));
            }
        foreach (var (item, n) in st.Hold)
            if (n > 0) list.Add(new HoldLine(item, n, Containers(item, n), ItemLabel(item)));
        if (st.Tow.Length > 0 && Site(st.Tow) is { } towed) list.Add(new HoldLine(TowItem, 1, 0, $"In tow: {towed.Name}"));
        if (st.Escort.Length > 0 && Site(st.Escort) is { } escorted)
            list.Add(new HoldLine(EscortItem, 1, 0, $"Escorting: {escorted.Name} to {Body(escorted.TargetBody)?.Name ?? "port"}"));
        return list;
    }

    private int ContainersEach(string item)
    {
        if (item == PatchKitItem) return Data?.FitOut?.Cargo.FirstOrDefault(l => l.PatchKits > 0)?.Containers ?? 1;
        if (item == MedicalItem) return Data?.FitOut?.Cargo.FirstOrDefault(l => l.Medical > 0)?.Containers ?? 1;
        if (item.StartsWith(SalvagePrefix, System.StringComparison.Ordinal)) return SiteRules?.SalvageItem(item[SalvagePrefix.Length..])?.Containers ?? 1;
        return 1;
    }

    /// <summary>Containers n of an item take (survivors: survivors_per_container to a container, rounded up, over every
    /// division together; worked out per line here, so a few in two divisions may take one more).</summary>
    private int Containers(string item, int n)
    {
        if (n <= 0) return 0;
        if (item.StartsWith(OrePrefix, System.StringComparison.Ordinal))
        {
            var per = System.Math.Max(1, SiteRules?.OrePerContainer ?? 30);
            return (n + per - 1) / per;
        }
        if (item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal))
        {
            var per = System.Math.Max(1, StatusRules?.SurvivorsPerContainer ?? 3);
            return (n + per - 1) / per;
        }
        return ContainersEach(item) * n;
    }

    private string ItemLabel(string item)
    {
        if (item == PatchKitItem) return "Hull patch kits";
        if (item == MedicalItem) return "Medical stores";
        if (item.StartsWith(SalvagePrefix, System.StringComparison.Ordinal)) return SiteRules?.SalvageItem(item[SalvagePrefix.Length..])?.Name ?? item;
        if (item.StartsWith(OrePrefix, System.StringComparison.Ordinal)) return SiteRules?.Ore(item[OrePrefix.Length..])?.Name ?? item;
        if (item == TowItem) return "Tow";
        if (item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal))
            return $"Survivors ({Data?.FitOut?.Divisions.FirstOrDefault(d => d.Id == item[SurvivorsPrefix.Length..])?.Name ?? item[SurvivorsPrefix.Length..]})";
        return item;
    }

    /// <summary>Containers in use now.</summary>
    private int HoldUsed(int player, ShipState st) => HoldLines(player, st).Sum(l => l.Containers);

    /// <summary>Whether n more of the item fit in the hold (survivors always come aboard: people, not cargo).</summary>
    private bool Fits(ShipState st, string item, int n)
    {
        if (item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal)) return true;
        var player = _status.IndexOf(st);
        var have = Count(st, item);
        return HoldUsed(player, st) + Containers(item, have + n) - Containers(item, have) <= st.HoldSize;
    }

    private bool KnownHoldItem(string? item)
    {
        if (item == null) return false;
        if (item is PatchKitItem or MedicalItem) return true;
        if (item.StartsWith(SalvagePrefix, System.StringComparison.Ordinal)) return SiteRules?.SalvageItem(item[SalvagePrefix.Length..]) != null;
        if (item.StartsWith(OrePrefix, System.StringComparison.Ordinal)) return SiteRules?.Ore(item[OrePrefix.Length..]) != null;
        if (item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal)) return DivisionIndex(item[SurvivorsPrefix.Length..]) >= 0;
        return false;
    }

    private static int Count(ShipState st, string item)
    {
        foreach (var (i, n) in st.Hold)
            if (i == item) return n;
        return 0;
    }

    private static void Set(ShipState st, string item, int n)
    {
        st.Hold.RemoveAll(x => x.Item == item);
        if (n > 0)
        {
            st.Hold.Add((item, n));
            st.Hold.Sort((a, b) => string.CompareOrdinal(a.Item, b.Item));
        }
    }

    private static void Add(ShipState st, string item, int n) => Set(st, item, Count(st, item) + n);

    private static void Take(ShipState st, string item, int n) => Set(st, item, System.Math.Max(0, Count(st, item) - n));

    private IReadOnlyList<string> DivisionIds() => Data?.FitOut?.Divisions.Select(d => d.Id).ToList() ?? new List<string>();

    private int DivisionIndex(string? id) => Data?.FitOut?.DivisionIndex(id) ?? -1;

    private int OnDuty(ShipState st, string division)
    {
        var i = DivisionIndex(division);
        return i >= 0 && i < st.OnDuty.Length ? st.OnDuty[i] : 0;
    }

    private static bool IsDown(ShipState st, ShipSystem system) => st.Down.Any(d => d.System == system);

    /// <summary>The band the ship is in: Crippled while it is (even as the hull creeps back), else by the hull.</summary>
    private static HullBandDef? BandFor(ShipStatusRules rules, ShipState st) =>
        st.Crippled ? rules.Bands.FirstOrDefault(b => b.Band == HullBand.Crippled) ?? rules.BandOf(st.Hull) : rules.BandOf(st.Hull);

    /// <summary>Whether the hull's band allows hazardous jobs.</summary>
    private bool HazardousOk(ShipState st) => !st.Crippled && ((StatusRules is { } br ? BandFor(br, st)?.Hazardous : null) ?? true);

    private int CrippledSeconds(ShipState st)
    {
        if (StatusRules is not { } r || r.RecoverTo <= 0) return 0;
        var missing = Fix64.FromInt(r.RecoverTo) - st.Hull;
        if (missing <= Fix64.Zero) return 0;
        return (missing * Fix64.FromInt(r.RecoverSeconds) / Fix64.FromInt(r.RecoverTo)).FloorToInt() + 1;
    }

    // ---------------------------------------------------------------- what the status does elsewhere

    /// <summary>× on a ship's flights: its hull band, engines down, fewer than half its crew on duty. Exactly 1 on a sound ship.</summary>
    private Fix64 StatusFlightFactor(SystemShip ship)
    {
        if (ship.Owner < 0 || Status(ship.Owner) is not { } st || FlagshipOf(ship.Owner) != ship || StatusRules is not { } rules) return Fix64.One;
        var f = BandFor(rules, st)?.Flight ?? Fix64.One;
        foreach (var d in st.Down)
            if (rules.System(d.System) is { } sys) f *= sys.Flight;
        if (st.CrewTotal * 2 < st.StartTotal) f *= rules.HalfCrewFlight;
        if (st.Tow.Length > 0 && SiteRules is { } sr) f *= sr.TowFlight; // phase 2: towing
        if (st.Escort.Length > 0 && Site(st.Escort) is { } escorted && SiteKindOf(escorted)?.Stranded is { } sd) f *= sd.EscortFlight; // phase 3: escorting
        return f;
    }

    /// <summary>× on the player's long-range scan reach and site identify range (a damaged hull's band). 1 when sound.</summary>
    private Fix64 ScanReachFactor(int player) =>
        Status(player) is { } st && StatusRules is { } rules ? BandFor(rules, st)?.ScanReach ?? Fix64.One : Fix64.One;

    /// <summary>Why a crippled flagship can't set course, or null.</summary>
    private string? CrippledProblem(int player, SystemShip ship) =>
        FlagshipOf(player) == ship && Status(player) is { Crippled: true } st
            ? $"the ship is crippled: adrift until the hull is back to {StatusRules?.RecoverTo ?? 20}% ({CrippledSeconds(st)} s)" : null;

    /// <summary>The player's flagship is crippled (hull at 0, not yet recovered).</summary>
    public bool IsCrippled(int player) => Status(player) is { Crippled: true };

    /// <summary>The fit-out lever factor with the crew on duty now (Simulation.FitOutFactor asks first): null while the
    /// crew is as it started (then the usual factor holds). With a fit-out: the fit-out's factor with the crew on duty;
    /// without one: the ship's suggested preset's factor with the crew on duty ÷ with the crew it started with (so a ship
    /// with no fit-out starts at exactly 1). A division below its floor counts as empty.</summary>
    internal Fix64? CrewFactor(int player, string lever)
    {
        if (player < 0 || player >= _status.Count || _status[player] is not { CrewDirty: true } st || _sim is not { } sim || sim.Data.FitOut is not { } fit) return null;
        if (!ReferenceEquals(st.FactorsFor, sim.Data))
        {
            st.Factors.Clear();
            st.FactorsFor = sim.Data;
        }
        if (st.Factors.TryGetValue(lever, out var cached)) return cached;
        var f = Fix64.One;
        if (fit.Ship(st.ShipKey) is { } shipDef)
        {
            var crew = new int[st.OnDuty.Length];
            for (var i = 0; i < crew.Length; i++) crew[i] = st.OnDuty[i] < st.Floors[i] ? 0 : st.OnDuty[i];
            if (sim.FitOutOf(player) is { } fitOut)
                f = FitOutRules.Factor(sim.Data, shipDef.ShipId, fitOut with { Crew = crew }, lever);
            else if (FitOutRules.Preset(sim.Data, shipDef.ShipId, shipDef.Suggest) is { } preset)
            {
                var start = FitOutRules.Factor(sim.Data, shipDef.ShipId, preset, lever);
                var now = FitOutRules.Factor(sim.Data, shipDef.ShipId, preset with { Crew = crew }, lever);
                f = start == now || start <= Fix64.Zero ? Fix64.One : now / start;
            }
        }
        st.Factors[lever] = f;
        return f;
    }

    /// <summary>The crew changed: the factors are worked out again (and only while it differs from the start).</summary>
    private void CrewChanged(ShipState st)
    {
        st.CrewDirty = !st.OnDuty.SequenceEqual(st.Start);
        st.Factors.Clear();
        Version++;
    }

    // ---------------------------------------------------------------- changes

    internal void ShipLog(int player, string text, string tone)
    {
        if (Status(player) is not { } st) return;
        st.Log.Add(new ShipLogLine(Now, text, tone));
        st.LogCount++;
        var max = System.Math.Max(1, StatusRules?.LogMax ?? 40);
        if (st.Log.Count > max) st.Log.RemoveRange(0, st.Log.Count - max);
        Version++;
    }

    /// <summary>Hull damage (at 0: crippled).</summary>
    internal void Damage(Simulation sim, int player, Fix64 amount, string why, bool quiet = false)
    {
        if (Status(player) is not { } st || amount <= Fix64.Zero) return;
        var before = st.Hull;
        st.Hull = Fix64.Max(Fix64.Zero, st.Hull - amount);
        Version++;
        if (!quiet) ShipLog(player, $"{Capital(why)} · hull −{amount.FloorToInt()}%", "bad");
        if (st.Hull <= Fix64.Zero && before > Fix64.Zero) Cripple(sim, player, st);
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Sets the hull (test panel, mission actions): 0 cripples; above 0 ends being crippled.</summary>
    internal void SetHull(Simulation sim, int player, Fix64 hull, string why)
    {
        if (Status(player) is not { } st) return;
        var before = st.Hull;
        st.Hull = Fix64.Clamp(hull, Fix64.Zero, Fix64.FromInt(100));
        Version++;
        if (st.Hull <= Fix64.Zero && before > Fix64.Zero) Cripple(sim, player, st);
        else if (st.Hull > Fix64.Zero && st.Crippled)
        {
            st.Crippled = false;
            st.CrippledTick = -1;
            ShipLog(player, $"Under way again ({why})", "good");
        }
    }

    /// <summary>Hull at 0: adrift (no course), every job at a site and the away team stop, active space jobs fail
    /// (MissionBook asks IsCrippled), and the hull starts its slow recovery.</summary>
    private void Cripple(Simulation sim, int player, ShipState st)
    {
        st.Crippled = true;
        st.CrippledTick = sim.Tick;
        st.Work = null;
        st.Scan = null;
        if (st.RepairAction == "dock")
        {
            st.RepairAction = "";
            st.RepairEnd = -1;
        }
        if (st.Away is { Done: false }) RecallAwayTeam(sim, player, "the ship is crippled");
        if (st.Tow.Length > 0) ReleaseTow(player, "the tow line parted as the ship was crippled");
        if (st.Escort.Length > 0) StopEscort(player, "it fell behind as the ship was crippled"); // SpaceEvents.cs
        ShipLog(player, "Hull at 0%: the ship is crippled and adrift", "bad");
        Version++;
    }

    internal void KnockOut(Simulation sim, int player, ShipSystem system, int seconds, string why)
    {
        if (Status(player) is not { } st || seconds <= 0) return;
        st.Down.RemoveAll(d => d.System == system);
        st.Down.Add(new SystemDown(system, sim.Tick + (long)seconds * Simulation.TickRate));
        st.Down.Sort((a, b) => a.System.CompareTo(b.System));
        ShipLog(player, $"{StatusRules?.System(system)?.Name ?? system.ToString()} knocked out ({why}): back in {seconds} s", "bad");
        if (system == ShipSystem.Tractor && st.Work is { Action: "sweep" or "mine" or "push" or "pickup" } w)
        {
            st.Work = null;
            ShipLog(player, $"{w.Action switch { "mine" => "Mining", "push" => "Pushing", "pickup" => "Picking up pods", _ => "Sweep" }} stopped: the tractor is down", "warn");
        }
        if (system == ShipSystem.Tractor && st.Tow.Length > 0) ReleaseTow(player, "the tractor went down");
        Version++;
    }

    internal void FixSystem(int player, ShipSystem system, string why)
    {
        if (Status(player) is not { } st || st.Down.RemoveAll(d => d.System == system) == 0) return;
        ShipLog(player, $"{StatusRules?.System(system)?.Name ?? system.ToString()} back on line ({why})", "good");
        Version++;
    }

    internal void LoseCrew(int player, int division, int n, string why)
    {
        if (Status(player) is not { } st || division < 0 || division >= st.OnDuty.Length) return;
        n = System.Math.Min(n, st.OnDuty[division]);
        if (n <= 0) return;
        st.OnDuty[division] -= n;
        st.Lost[division] += n;
        ShipLog(player, $"{n} of the {DivisionName(division)} crew lost: {why}", "bad");
        CrewChanged(st);
    }

    internal void GiveCrew(int player, int division, int n, string why)
    {
        if (Status(player) is not { } st || division < 0 || division >= st.OnDuty.Length || n <= 0) return;
        st.OnDuty[division] += n;
        ShipLog(player, $"{n} join{(n == 1 ? "s" : "")} the {DivisionName(division)} crew: {why}", "good");
        CrewChanged(st);
    }

    /// <summary>Injures n on-duty members of a division for injury_seconds (− medical stores; quarantine for its own seconds).</summary>
    internal void InjureCrew(Simulation sim, int player, int division, int n, string why, int seconds = -1)
    {
        if (Status(player) is not { } st || division < 0 || division >= st.OnDuty.Length) return;
        n = System.Math.Min(n, st.OnDuty[division]);
        if (n <= 0) return;
        var back = sim.Tick + (long)(seconds > 0 ? seconds : InjurySeconds(st)) * Simulation.TickRate;
        for (var k = 0; k < n; k++) st.Injured.Add((division, back));
        st.Injured.Sort((a, b) => a.Back != b.Back ? a.Back.CompareTo(b.Back) : a.Division.CompareTo(b.Division));
        st.OnDuty[division] -= n;
        ShipLog(player, $"{n} of the {DivisionName(division)} crew injured: {why} (back in {(back - sim.Tick) / Simulation.TickRate} s)", "warn");
        CrewChanged(st);
    }

    private int InjurySeconds(ShipState st)
    {
        if (StatusRules is not { } r) return 180;
        var med = System.Math.Min(Count(st, MedicalItem), r.MedicalMax);
        return System.Math.Max(r.InjuryMinSeconds, r.InjurySeconds - med * r.MedicalSeconds);
    }

    private string DivisionName(int division) => Data?.FitOut is { } f && division >= 0 && division < f.Divisions.Count ? f.Divisions[division].Name : "ship's";

    /// <summary>A survivor comes aboard: joins the crew while it is below where it started, else rides in the hold.</summary>
    private void TakeSurvivor(Simulation sim, int player, string division, string from)
    {
        if (Status(player) is not { } st) return;
        var d = DivisionIndex(division);
        if (d < 0) return;
        if (st.CrewTotal < st.StartTotal) GiveCrew(player, d, 1, $"a survivor from {from}");
        else Add(st, SurvivorsPrefix + division, 1);
    }

    // ---------------------------------------------------------------- repairs

    /// <summary>Why a ShipRepairCommand action can't run now, or null.</summary>
    public string? RepairProblem(int player, string action)
    {
        if (Status(player) is not { } st || FlagshipOf(player) is not { } ship) return "no ship status";
        if (action == null) return "no action";
        if (st.RepairAction.Length > 0) return st.RepairAction == "dock" ? "docked for repairs already" : "a patch kit is being fitted";
        if (action == "patch")
        {
            if (Count(st, PatchKitItem) <= 0) return "no hull patch kits in the hold";
            return st.Hull >= Fix64.FromInt(100) ? "the hull is whole" : null;
        }
        if (action.StartsWith("fix:", System.StringComparison.Ordinal))
        {
            if (GameData.ShipSystemOf(action[4..]) is not { } system) return $"no system '{action[4..]}'";
            if (!IsDown(st, system)) return $"{StatusRules?.System(system)?.Name ?? system.ToString()} is working";
            return Count(st, PatchKitItem) <= 0 ? "no hull patch kits in the hold" : null;
        }
        if (action == "dock")
        {
            if (!AtDockyard(player, ship)) return SurfaceBody != null && _sim?.CanColonize(player) == true
                ? $"dock at a station, or at {Body(SurfaceBody)?.Name ?? "the colony"}" : "dock at a station";
            return st.Hull >= Fix64.FromInt(100) && st.Down.Count == 0 && !st.Crippled ? "nothing to repair" : null;
        }
        return $"no repair '{action}'";
    }

    /// <summary>The ship is at a station, or a colony ship in orbit of its colony's body.</summary>
    private bool AtDockyard(int player, SystemShip ship) =>
        (ship.AtSite is { } id && Site(id) is { Kind: SiteKind.Station })
        || (ship.AtBody != null && ship.AtBody == SurfaceBody && _sim?.CanColonize(player) == true && _sim.ShipChoiceOf(player) != null);

    private void ApplyRepair(Simulation sim, ShipRepairCommand c)
    {
        var ship = FindShip(c.ShipId);
        if (ship == null || ship.Owner != c.Player || c.Player < 0 || FlagshipOf(c.Player) != ship)
        {
            sim.Refuse(c, ship == null ? "no such ship" : "not your flagship");
            return;
        }
        if (RepairProblem(c.Player, c.Action) is { } problem)
        {
            sim.Refuse(c, problem);
            return;
        }
        var st = Status(c.Player)!;
        Version++;
        if (c.Action == "patch")
        {
            Take(st, PatchKitItem, 1);
            st.RepairAction = "patch";
            st.RepairStart = sim.Tick;
            st.RepairEnd = sim.Tick + (long)(StatusRules?.PatchSeconds ?? 30) * Simulation.TickRate;
            ShipLog(c.Player, "Fitting a hull patch kit", "");
            return;
        }
        if (c.Action.StartsWith("fix:", System.StringComparison.Ordinal))
        {
            Take(st, PatchKitItem, 1);
            FixSystem(c.Player, GameData.ShipSystemOf(c.Action[4..])!.Value, "a patch kit");
            return;
        }
        BeginDock(sim, c.Player);
    }

    private void BeginDock(Simulation sim, int player)
    {
        var st = Status(player)!;
        var seconds = SiteRules?.Station.DockSeconds ?? 30;
        st.RepairAction = "dock";
        st.RepairStart = sim.Tick;
        st.RepairEnd = sim.Tick + (long)seconds * Simulation.TickRate;
        ShipLog(player, $"Docked for repairs: {seconds} s", "");
        Version++;
    }

    // ---------------------------------------------------------------- the hold

    /// <summary>Why a ShipHoldCommand can't run now, or null.</summary>
    public string? HoldProblem(int player, string action, string item, int count)
    {
        if (Status(player) is not { } st || FlagshipOf(player) is not { } ship || _sim is not { } sim) return "no ship status";
        if (action == null || item == null) return "nothing to do";
        if (count < 1 || count > 1000) return "count 1..1000";
        var atStation = ship.AtSite is { } sid && Site(sid) is { Kind: SiteKind.Station };
        if (SellerAction(ref action, out var buyer) is { } bad) return bad; // "sell:<ship id>": to a ship in orbit with us
        if (item == TowItem) return TowHoldProblem(player, st, ship, action); // Towing.cs
        if (item == EscortItem) // phase 3: an escort isn't cargo; jettison stops it where the ship is
            return action == "jettison" ? st.Escort.Length > 0 ? null : "not escorting anyone" : "bring the escorted ship to its destination, or stop the escort";
        if (item.StartsWith(OrePrefix, System.StringComparison.Ordinal)) return OreHoldProblem(player, st, ship, action, item, count, buyer, atStation); // Mining.cs
        if (buyer >= 0) return "only ore sells to a ship";
        switch (action)
        {
            case "hire":
            {
                if (!atStation) return "crew are hired at a station";
                var d = DivisionIndex(item);
                if (d < 0) return $"no division '{item}'";
                var per = SiteRules?.Station.HirePerVisit ?? 2;
                if (st.HiredThisVisit + count > per) return per - st.HiredThisVisit <= 0 ? "no one else to hire this visit" : $"only {per - st.HiredThisVisit} more to hire this visit";
                if (st.CrewTotal + count > st.StartTotal) return st.CrewTotal >= st.StartTotal ? "the crew is complete" : $"room for {st.StartTotal - st.CrewTotal} more";
                var cost = (SiteRules?.Station.HireLatinum ?? 0) * count;
                return Latinum(sim, player) < cost ? $"needs {cost} latinum (have {Latinum(sim, player)})" : null;
            }
            case "sell":
            {
                if (!atStation) return "sell at a station";
                if (!item.StartsWith(SalvagePrefix, System.StringComparison.Ordinal)) return "only salvage sells";
                return Count(st, item) < count ? $"have {Count(st, item)}" : null;
            }
            case "drop":
            {
                var survivors = item == "survivors" || item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal);
                if (!survivors && !item.StartsWith(SalvagePrefix, System.StringComparison.Ordinal)) return $"can't drop '{item}'";
                var have = item == "survivors" ? st.Hold.Where(x => x.Item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal)).Sum(x => x.Count) : Count(st, item);
                if (have < count) return $"have {have}";
                var atColony = ship.AtBody != null && ship.AtBody == SurfaceBody && sim.Surface is { } s && player < s.Players.Count && s.Players[player].LandingTile >= 0;
                return atStation || atColony ? null : "drop it at a station or the colony's body";
            }
            case "jettison":
                if (item == "survivors" || item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal)) return "people don't go out of the airlock";
                if (JettisonableLine(player, item) is { } line)
                {
                    var left = line.Probes > 0 ? sim.FitOutProbes(player) : line.SystemSatellites > 0 ? sim.FitOutFreeSystemSatellites(player) : sim.FitOutFreePosts(player);
                    return left < count ? $"have {left}" : null;
                }
                if (!KnownHoldItem(item)) return $"no '{item}' aboard";
                return Count(st, item) < count ? $"have {Count(st, item)}" : null;
        }
        return $"no hold action '{action}'";
    }

    /// <summary>A fit-out space cargo line the hold can throw out to make room (sensor probes, system satellites, listening
    /// posts: the ones that are used up), by its line id; null for anything else.</summary>
    private FitOutCargoLine? JettisonableLine(int player, string item) =>
        _sim?.FitOutOf(player) != null && Data?.FitOut?.Line(item) is { Space: true } line && (line.Probes > 0 || line.SystemSatellites > 0 || line.Posts > 0) ? line : null;

    private static int Latinum(Simulation sim, int player) =>
        sim.Data.HasResource(GameData.Latinum) ? sim.Players[player].Stock[sim.Data.Resource(GameData.Latinum).Index].FloorToInt() : 0;

    private void ApplyHold(Simulation sim, ShipHoldCommand c)
    {
        var ship = FindShip(c.ShipId);
        if (ship == null || ship.Owner != c.Player || c.Player < 0 || FlagshipOf(c.Player) != ship)
        {
            sim.Refuse(c, ship == null ? "no such ship" : "not your flagship");
            return;
        }
        if (HoldProblem(c.Player, c.Action, c.Item, c.Count) is { } problem)
        {
            sim.Refuse(c, problem);
            return;
        }
        var st = Status(c.Player)!;
        var p = c.Player;
        Version++;
        var action = c.Action;
        SellerAction(ref action, out var buyer);
        if (c.Item == TowItem)
        {
            ApplyTowHold(sim, p, st, ship, action); // Towing.cs
            return;
        }
        if (c.Item == EscortItem)
        {
            StopEscort(p, "cast off"); // SpaceEvents.cs
            return;
        }
        if (c.Item.StartsWith(OrePrefix, System.StringComparison.Ordinal))
        {
            ApplyOreHold(sim, p, st, ship, action, c.Item, c.Count, buyer); // Mining.cs
            return;
        }
        switch (action)
        {
            case "hire":
            {
                var cost = (SiteRules?.Station.HireLatinum ?? 0) * c.Count;
                if (cost > 0) sim.Players[p].Stock.Add(sim.Data.Resource(GameData.Latinum).Index, -Fix64.FromInt(cost));
                st.HiredThisVisit += c.Count;
                GiveCrew(p, DivisionIndex(c.Item), c.Count, $"hired at {Site(ship.AtSite)?.Name ?? "the station"} for {cost} latinum");
                return;
            }
            case "sell":
            {
                var each = SiteRules?.SalvageItem(c.Item[SalvagePrefix.Length..])?.Latinum ?? 0;
                Take(st, c.Item, c.Count);
                var paid = GiveLatinum(sim, p, each * c.Count);
                AddTally(st, "sold:" + c.Item, c.Count);
                ShipLog(p, $"Sold {c.Count} × {ItemLabel(c.Item)} for {paid} latinum", "good");
                return;
            }
            case "drop":
                if (c.Item == "survivors" || c.Item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal))
                {
                    var left = c.Count;
                    var paid = 0;
                    foreach (var (item, n) in st.Hold.Where(x => x.Item.StartsWith(SurvivorsPrefix, System.StringComparison.Ordinal)
                                 && (c.Item == "survivors" || x.Item == c.Item)).ToList())
                    {
                        var take = System.Math.Min(n, left);
                        Take(st, item, take);
                        left -= take;
                        var d = DivisionIndex(item[SurvivorsPrefix.Length..]);
                        for (var k = 0; k < take; k++)
                            if (st.CrewTotal < st.StartTotal) GiveCrew(p, d, 1, "a survivor signs on");
                            else paid += GiveLatinum(sim, p, SiteRules?.Station.SurvivorLatinum ?? 0);
                        if (left <= 0) break;
                    }
                    ShipLog(p, $"Survivors set down{(paid > 0 ? $": {paid} latinum from their people" : "")}", "good");
                }
                else
                {
                    var research = (SiteRules?.SalvageItem(c.Item[SalvagePrefix.Length..])?.Research ?? 0) * c.Count;
                    Take(st, c.Item, c.Count);
                    var text = GiveSpaceResearch(sim, p, Fix64.FromInt(research));
                    ShipLog(p, $"Handed in {c.Count} × {ItemLabel(c.Item)}{(text.Length > 0 ? $": {text}" : "")}", "good");
                }
                return;
            case "jettison":
                if (JettisonableLine(p, c.Item) is { } line)
                {
                    for (var k = 0; k < c.Count; k++)
                        _ = line.Probes > 0 ? sim.UseProbe(p) : line.SystemSatellites > 0 ? sim.UseFreeSystemSatellite(p) : sim.UseFreePost(p);
                    ShipLog(p, $"Jettisoned {c.Count} × {line.Name}", "warn");
                    return;
                }
                Take(st, c.Item, c.Count);
                ShipLog(p, $"Jettisoned {c.Count} × {ItemLabel(c.Item)}", "warn");
                return;
        }
    }

    // ---------------------------------------------------------------- ticks

    /// <summary>One tick of a flagship: systems coming back, injured crew back (not while life support is down), the
    /// crippled recovery, field repairs every 10 s, a patch kit or a dock repair finishing.</summary>
    private void StepShipStatus(Simulation sim, int player, ShipState st)
    {
        if (StatusRules is not { } rules) return;
        var tick = sim.Tick;
        for (var i = st.Down.Count - 1; i >= 0; i--)
            if (tick >= st.Down[i].BackTick)
            {
                var sys = st.Down[i].System;
                st.Down.RemoveAt(i);
                ShipLog(player, $"{rules.System(sys)?.Name ?? sys.ToString()} back on line", "good");
            }
        if (st.Injured.Count > 0)
        {
            if (IsDown(st, ShipSystem.LifeSupport))
            {
                for (var i = 0; i < st.Injured.Count; i++) st.Injured[i] = (st.Injured[i].Division, st.Injured[i].Back + 1); // no one recovers
            }
            else
            {
                var back = 0;
                while (st.Injured.Count > 0 && tick >= st.Injured[0].Back)
                {
                    st.OnDuty[st.Injured[0].Division]++;
                    st.Injured.RemoveAt(0);
                    back++;
                }
                if (back > 0)
                {
                    ShipLog(player, $"{back} injured crew back on duty", "good");
                    CrewChanged(st);
                }
            }
        }
        if (st.Crippled)
        {
            var per = Fix64.FromInt(rules.RecoverTo) / Fix64.FromInt(System.Math.Max(1, rules.RecoverSeconds * Simulation.TickRate));
            st.Hull = Fix64.Min(Fix64.FromInt(rules.RecoverTo), st.Hull + per);
            if (st.Hull >= Fix64.FromInt(rules.RecoverTo))
            {
                st.Crippled = false;
                st.CrippledTick = -1;
                ShipLog(player, $"Field repairs: hull back to {rules.RecoverTo}%, under way again", "good");
            }
        }
        else if (tick > 0 && tick % (10L * Simulation.TickRate) == 0 && st.Hull < Fix64.FromInt(rules.FieldUpTo) && FieldCrew(st))
        {
            var gain = rules.FieldPer10s * Fit(player, FitOutRules.FieldRepair);
            st.Hull = Fix64.Min(Fix64.FromInt(rules.FieldUpTo), st.Hull + gain);
            Version++;
            if (st.Hull >= Fix64.FromInt(rules.FieldUpTo)) ShipLog(player, $"Field repairs: hull back to {rules.FieldUpTo}% (the most the crew can do outside a dock)", "good");
        }
        if (st.RepairAction.Length > 0 && tick >= st.RepairEnd)
        {
            if (st.RepairAction == "patch")
            {
                SetHull(sim, player, Fix64.Min(Fix64.FromInt(100), st.Hull + Fix64.FromInt(rules.PatchHull)), "a patch kit");
                ShipLog(player, $"Hull patch fitted: +{rules.PatchHull}%", "good");
            }
            else
            {
                SetHull(sim, player, Fix64.FromInt(100), "the dockyard");
                st.Down.Clear();
                ShipLog(player, "Dock repairs done: hull 100%, every system working", "good");
            }
            st.RepairAction = "";
            st.RepairStart = st.RepairEnd = -1;
            Version++;
        }
    }

    /// <summary>Field repairs need the Engineering crew at its floor.</summary>
    private bool FieldCrew(ShipState st)
    {
        var e = DivisionIndex("engineering");
        return e < 0 || e >= st.OnDuty.Length || st.OnDuty[e] >= System.Math.Max(1, st.Floors[e]);
    }
}
