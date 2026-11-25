using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Space jobs, phase 2 (docs/space-jobs-contract.md "Phase 2"; data maps/sites.json: generation.asteroids, the asteroid
// kind's mining, ore): asteroids rolled with the system (most at a belt's distance from the star), mining them a container
// of ore at a time into the hold (the tractor must work; rock falls on sim.Rng), and the ore's way out: sold at the station
// or to a neutral freighter / tanker in orbit with the flagship, dropped into a colony ship's colony stock (through
// storage), or sold to a trade partner's world. What a player sold, dropped, mined and towed in is kept as a tally that
// mission conditions read (space_tally). Hashed with the flagship's status (system games only).
public sealed partial class StarSystemState
{
    private const ulong AsteroidSalt = 0x5_17E5_0003UL;

    // ---------------------------------------------------------------- generation

    /// <summary>The system's asteroids (generation.asteroids), after the other sites and on their own salt: near_belt
    /// percent of them at a belt's distance from the star ± belt_spread (when the system has a belt), the rest anywhere in
    /// generation.orbit; clear of the bodies and of other sites by the asteroids' spacing.</summary>
    private void InitAsteroids(GameData data, SpaceSiteRules r)
    {
        if (r.Asteroids is not { CountMax: > 0 } ag || r.Kind(SiteKind.Asteroid) is not { } def) return;
        var belts = data.SystemGenerator is { } g ? Bodies.Where(b => g.IsBelt(b.ClassId)).ToList() : new List<BodyDef>();
        var count = SystemGenerator.Int(Hash(AsteroidSalt, 1), ag.CountMin, ag.CountMax);
        for (var k = 0; k < count; k++)
        {
            var nearBelt = belts.Count > 0 && (int)(Hash(AsteroidSalt, 10 + k) % 100) < ag.NearBelt;
            FixVec2? at = null;
            for (var t = 0; t < System.Math.Max(1, r.Tries) && at == null; t++)
            {
                var slot = 1000L + k * 1000L + t * 4;
                var turns = SystemGenerator.Unit(Hash(AsteroidSalt, slot));
                Fix64 radius;
                if (nearBelt)
                {
                    var belt = belts[(int)(Hash(AsteroidSalt, slot + 1) % (ulong)belts.Count)];
                    var mid = belt.Position.Length();
                    radius = Fix64.Max(Fix64.FromInt(10), mid + ag.BeltSpread * (SystemGenerator.Unit(Hash(AsteroidSalt, slot + 2)) * Fix64.FromInt(2) - Fix64.One));
                }
                else radius = r.OrbitMin + (r.OrbitMax - r.OrbitMin) * SystemGenerator.Unit(Hash(AsteroidSalt, slot + 3));
                var p = new FixVec2(radius * Surface.Telemetry.CosTurns(turns), radius * Surface.Climate.SinTurns(turns));
                if (Clear(r, p, ag.Spacing)) at = p;
            }
            if (at is not { } spot) continue;
            var variant = def.Variants.Count > 0 ? SystemGenerator.Pick(def.Variants, Hash(AsteroidSalt, 20 + k)) : "";
            AddSite(SiteKind.Asteroid, variant, null, spot, SystemGenerator.Int(Hash(AsteroidSalt, 30 + k), def.SizeMin, def.SizeMax), 0);
        }
    }

    // ---------------------------------------------------------------- what an asteroid holds (pure: its seed)

    /// <summary>Containers of ore the asteroid holds in all (0 for anything that isn't mined).</summary>
    public int AsteroidContainers(SpaceSite site) =>
        SiteKindOf(site)?.Mining?.OreOf(site.Variant) is { } o ? SystemGenerator.Int(SiteRoll(site, 500), o.ContainersMin, o.ContainersMax) : 0;

    /// <summary>The ore (resource id) of the k-th container mined there.</summary>
    public string AsteroidOre(SpaceSite site, int k) =>
        SiteKindOf(site)?.Mining?.OreOf(site.Variant) is { Ores.Count: > 0 } o ? SystemGenerator.Pick(o.Ores, SiteRoll(site, 510 + k)) : "";

    /// <summary>What is left to mine there for the player: containers by ore (resource id, containers), most first.</summary>
    public IReadOnlyList<(string Ore, int Containers)> AsteroidOreLeft(int player, SpaceSite site)
    {
        var mined = Record(player, site.Id)?.Mined ?? 0;
        var total = AsteroidContainers(site);
        var counts = new List<(string Ore, int Containers)>();
        for (var k = mined; k < total; k++)
        {
            var ore = AsteroidOre(site, k);
            var i = counts.FindIndex(c => c.Ore == ore);
            if (i < 0) counts.Add((ore, 1));
            else counts[i] = (ore, counts[i].Containers + 1);
        }
        counts.Sort((a, b) => a.Containers != b.Containers ? b.Containers.CompareTo(a.Containers) : string.CompareOrdinal(a.Ore, b.Ore));
        return counts;
    }

    /// <summary>An asteroid is worked out for the player: mined out, and its cavity (a hollow one) searched.</summary>
    private bool AsteroidDone(int player, SpaceSite site) =>
        Record(player, site.Id) is { } r && r.Mined >= AsteroidContainers(site) && (SiteKindOf(site)?.AwayTableFor(site.Variant) is not { Length: > 0 } || r.Boarded);

    /// <summary>The hold item of an ore ("ore:duranium_ore").</summary>
    public static string OreItem(string ore) => OrePrefix + ore;

    private string OreName(string ore) => SiteRules?.Ore(ore)?.Name ?? ore;

    private int OrePerContainer => System.Math.Max(1, SiteRules?.OrePerContainer ?? 30);

    // ---------------------------------------------------------------- the card

    private void AsteroidRows(int player, SpaceSite site, SiteRecord r, List<SiteRow> rows)
    {
        var total = AsteroidContainers(site);
        var left = total - r.Mined;
        var ores = AsteroidOreLeft(player, site);
        rows.Add(new SiteRow("ORE", left <= 0 ? "mined out" : $"{left} of {total} containers · {string.Join(", ", ores.Select(o => $"{OreName(o.Ore).ToLowerInvariant()} {o.Containers}"))}",
            left <= 0 ? "warn" : ""));
        if (r.Mined > 0) rows.Add(new SiteRow("MINED", $"{r.Mined} container{(r.Mined == 1 ? "" : "s")} · {OrePerContainer} ore each", "good"));
        if (SiteKindOf(site)?.Mining is { } mn)
        {
            var seconds = MiningSeconds(player, mn);
            rows.Add(new SiteRow("RATE", $"a container every {seconds} s{(mn.Rockfall is { ChancePerMin: > 0 } rf ? $" · rock falls {rf.ChancePerMin}% a minute" : "")}", mn.Rockfall != null ? "warn" : ""));
        }
        if (SiteKindOf(site)?.AwayTableFor(site.Variant) is { Length: > 0 })
        {
            rows.Add(new SiteRow("CAVITY", r.Boarded ? "searched" : "a cavity in the rock: something inside", r.Boarded ? "good" : "warn"));
            rows.Add(new SiteRow("ALONGSIDE", r.Alongside ? "yes" : "no", r.Alongside ? "good" : ""));
        }
    }

    /// <summary>Seconds a container takes for the player (mining.seconds ÷ the mining lever, rounded up).</summary>
    public int MiningSeconds(int player, SiteMiningDef mn)
    {
        var ticks = MiningTicks(player, mn);
        return (int)((ticks + Simulation.TickRate - 1) / Simulation.TickRate);
    }

    private long MiningTicks(int player, SiteMiningDef mn) => System.Math.Max(1L, Faster((long)mn.Seconds * Simulation.TickRate, Fit(player, FitOutRules.Mining)));

    // ---------------------------------------------------------------- mining

    private string? MineProblem(int player, ShipState st, SpaceSite site, SiteRecord r)
    {
        if (SiteKindOf(site)?.Mining == null) return "nothing to mine";
        var total = AsteroidContainers(site);
        if (r.Mined >= total) return $"{site.Name} is mined out";
        if (IsDown(st, ShipSystem.Tractor)) return "the tractor is down: no mining";
        if (st.Tow.Length > 0) return $"the tractor is holding {SiteNameFor(player, st.Tow)} in tow: release or drop it first";
        if (st.Away is { Done: false }) return "an away team is out";
        var ore = AsteroidOre(site, r.Mined);
        if (!Fits(st, OreItem(ore), OrePerContainer)) return "the hold is full: sell, drop or jettison something first";
        return null;
    }

    private void BeginMining(Simulation sim, int player, SpaceSite site, SiteRecord r)
    {
        var st = Status(player)!;
        var ticks = MiningTicks(player, SiteKindOf(site)!.Mining!);
        st.Work = new WorkRun { Site = site.Id, Action = "mine", Start = sim.Tick, End = sim.Tick + ticks };
        ShipLog(player, $"Mining {site.Name}: {AsteroidContainers(site) - r.Mined} containers of ore left", "");
    }

    /// <summary>One tick of mining: the tractor must hold, a rock-fall roll each whole second, and a container of ore into
    /// the hold at the end of each run; then the next, until the hold has no room or the asteroid is mined out.</summary>
    private void StepMining(Simulation sim, int player, ShipState st, SpaceSite site, SiteRecord r, WorkRun w)
    {
        if (SiteKindOf(site)?.Mining is not { } mn || IsDown(st, ShipSystem.Tractor))
        {
            st.Work = null;
            Version++;
            return;
        }
        if (mn.Rockfall is { ChancePerMin: > 0 } rf && sim.Tick > w.Start && (sim.Tick - w.Start) % Simulation.TickRate == 0
            && sim.Rng.NextInt(6000) < rf.ChancePerMin) // chance_per_min percent a minute = that many in 6000 a second
        {
            var hull = sim.Rng.NextInt(rf.HullMin, rf.HullMax + 1);
            Damage(sim, player, Fix64.FromInt(hull), $"a rock fall at {site.Name}");
            if (st.Work == null) return; // crippled by it
            if (rf.Tractor > 0 && sim.Rng.NextInt(100) < rf.Tractor && StatusRules is { } rules)
            {
                KnockOut(sim, player, ShipSystem.Tractor, sim.Rng.NextInt(rules.DownMin, rules.DownMax + 1), $"a rock fall at {site.Name}");
                return; // KnockOut stopped the mining
            }
        }
        if (sim.Tick < w.End) return;
        var ore = AsteroidOre(site, r.Mined);
        var item = OreItem(ore);
        var per = OrePerContainer;
        if (!Fits(st, item, per))
        {
            st.Work = null;
            ShipLog(player, $"Mining at {site.Name} stopped: the hold is full", "warn");
            Version++;
            return;
        }
        Add(st, item, per);
        AddTally(st, "mined:" + item, per);
        r.Mined++;
        Version++;
        var total = AsteroidContainers(site);
        if (r.Mined >= total)
        {
            st.Work = null;
            if (AsteroidDone(player, site)) Spend(player, site, "mined out");
            else ShipLog(player, $"{site.Name} mined out: the cavity is still to search", "good");
            return;
        }
        if (!Fits(st, OreItem(AsteroidOre(site, r.Mined)), per))
        {
            st.Work = null;
            ShipLog(player, $"Mining at {site.Name} stopped: the hold is full ({r.Mined} of {total} containers mined)", "warn");
            return;
        }
        st.Work = new WorkRun { Site = site.Id, Action = "mine", Start = sim.Tick, End = sim.Tick + MiningTicks(player, mn) };
    }

    // ---------------------------------------------------------------- the tally

    private static void AddTally(ShipState st, string key, int n)
    {
        if (n <= 0) return;
        var i = st.Tally.FindIndex(t => t.Key == key);
        if (i >= 0) st.Tally[i] = (key, st.Tally[i].Count + n);
        else
        {
            st.Tally.Add((key, n));
            st.Tally.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        }
    }

    /// <summary>What the player sold, dropped, mined or towed in so far: the sum of every tally key starting with the
    /// prefix ("sold:ore" = all ore sold, "drop:ore:deuterium", "mined:", "tow:station", "tow:" = every tow delivered).</summary>
    public int Tally(int player, string prefix)
    {
        if (Status(player) is not { } st || prefix == null) return 0;
        var n = 0;
        foreach (var (k, c) in st.Tally)
            if (k.StartsWith(prefix, System.StringComparison.Ordinal)) n += c;
        return n;
    }

    // ---------------------------------------------------------------- selling and dropping ore

    /// <summary>"sell:&lt;ship id&gt;" names the buyer: the action becomes "sell" and buyer that id (-1 = none named).
    /// Returns a problem when the id isn't a number.</summary>
    private static string? SellerAction(ref string action, out int buyer)
    {
        buyer = -1;
        if (!action.StartsWith("sell:", System.StringComparison.Ordinal)) return null;
        if (!int.TryParse(action[5..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out buyer) || buyer < 0)
        {
            buyer = -1;
            return "no such buyer";
        }
        action = "sell";
        return null;
    }

    /// <summary>The neutral ships that buy ore from the player now: freighters and tankers (ore.traders roles) in orbit of
    /// the body the flagship is in orbit of, by id.</summary>
    public IReadOnlyList<SystemShip> OreBuyers(int player)
    {
        var list = new List<SystemShip>();
        if (FlagshipOf(player) is not { AtBody: { } body } || SiteRules is not { } r || r.TraderRoles.Count == 0) return list;
        foreach (var s in _ships)
            if (s.IsNeutral && s.AtBody == body && r.TraderRoles.Contains(s.Role)) list.Add(s);
        return list;
    }

    /// <summary>Whether that ship buys ore from the player now (a neutral trader in orbit with the flagship).</summary>
    public bool BuysOre(int player, int shipId)
    {
        foreach (var s in OreBuyers(player))
            if (s.Id == shipId) return true;
        return false;
    }

    /// <summary>Latinum the player gets for count units of an ore item: at the station (buyer -1 at a station) the ore's
    /// price, to a trader ship its share of it, at a trade partner's world the full price. Rounded down.</summary>
    public int OreValue(string item, int count, bool trader)
    {
        if (SiteRules is not { } r || !item.StartsWith(OrePrefix, System.StringComparison.Ordinal) || r.Ore(item[OrePrefix.Length..]) is not { } ore) return 0;
        var v = ore.Latinum * Fix64.FromInt(count);
        if (trader) v *= r.TraderShare;
        return v.FloorToInt();
    }

    /// <summary>The trade partner's world the flagship is in orbit of (its natives buy ore), or null.</summary>
    private string? TradeWorldAt(int player, SystemShip ship) =>
        ship.AtBody is { } body && RelationsWith(player, body) >= ContactStage.TradePartner ? body : null;

    /// <summary>Whether the player's colony is on the body the flagship orbits (landed): ore and tows go down to it.</summary>
    private bool AtOwnColony(int player, SystemShip ship) =>
        ship.AtBody != null && ship.AtBody == SurfaceBody && _sim?.Surface is { } s && player < s.Players.Count && s.Players[player].LandingTile >= 0;

    /// <summary>Units of an ore the colony's storage takes now (all of them when it takes no space).</summary>
    private int ColonyRoom(Simulation sim, int player, string ore, int want)
    {
        if (sim.Surface is not { } s || !sim.Data.HasResource(ore)) return 0;
        var index = sim.Data.Resource(ore).Index;
        if (!sim.Data.IsStored(index)) return want;
        var volume = sim.Data.VolumeOf(index);
        if (volume <= Fix64.Zero) return want;
        var room = (s.FreeStorage(sim, player) / volume).FloorToInt();
        return (int)System.Math.Clamp(room, 0, want);
    }

    private string? OreHoldProblem(int player, ShipState st, SystemShip ship, string action, string item, int count, int buyer, bool atStation)
    {
        if (!KnownHoldItem(item)) return $"no ore '{item[OrePrefix.Length..]}'";
        var have = Count(st, item);
        switch (action)
        {
            case "sell":
                if (have < count) return $"have {have}";
                if (buyer >= 0)
                {
                    if (FindShip(buyer) is not { } b) return "no such buyer";
                    return BuysOre(player, buyer) ? null : $"{b.Name} isn't buying from us here (a freighter or tanker in orbit with the ship)";
                }
                if (atStation) return null;
                return OreBuyers(player).Count > 0 ? null : "sell ore at a station, or to a freighter or tanker in orbit with the ship";
            case "drop":
                if (have < count) return $"have {have}";
                if (_sim is { } sim && AtOwnColony(player, ship))
                    return ColonyRoom(sim, player, item[OrePrefix.Length..], count) > 0 ? null : "the colony's storage is full";
                if (TradeWorldAt(player, ship) != null) return null;
                return atStation ? "sell ore at the station" : "drop ore at the colony's body, or at a world that trades with us";
            case "jettison":
                return have < count ? $"have {have}" : null;
        }
        return $"no hold action '{action}' for ore";
    }

    private void ApplyOreHold(Simulation sim, int p, ShipState st, SystemShip ship, string action, string item, int count, int buyer)
    {
        var name = ItemLabel(item).ToLowerInvariant();
        var ore = item[OrePrefix.Length..];
        switch (action)
        {
            case "sell":
            {
                var atStation = ship.AtSite is { } sid && Site(sid) is { Kind: SiteKind.Station };
                var to = buyer >= 0 ? FindShip(buyer) : atStation ? null : OreBuyers(p).FirstOrDefault();
                Take(st, item, count);
                var paid = GiveLatinum(sim, p, OreValue(item, count, to != null));
                AddTally(st, "sold:" + item, count);
                ShipLog(p, $"Sold {count} {name} {(to != null ? $"to the {to.Name}" : $"at {Site(ship.AtSite)?.Name ?? "the station"}")} for {paid} latinum", "good");
                return;
            }
            case "drop":
            {
                if (AtOwnColony(p, ship))
                {
                    var n = ColonyRoom(sim, p, ore, count);
                    var stored = sim.Surface!.StoreTrade(sim, p, sim.Data.Resource(ore).Index, Fix64.FromInt(n)).FloorToInt();
                    Take(st, item, (int)stored);
                    AddTally(st, "drop:" + item, (int)stored);
                    ShipLog(p, $"{stored} {name} sent down to the colony{(stored < count ? $" ({count - stored} stay aboard: storage is full)" : "")}", "good");
                    return;
                }
                var world = TradeWorldAt(p, ship)!;
                Take(st, item, count);
                var paid = GiveLatinum(sim, p, OreValue(item, count, false));
                AddTally(st, "drop:" + item, count);
                ShipLog(p, $"{count} {name} traded down to {NameFor(p, world)} for {paid} latinum", "good");
                return;
            }
            case "jettison":
                Take(st, item, count);
                ShipLog(p, $"Jettisoned {count} {name}", "warn");
                return;
        }
    }
}
