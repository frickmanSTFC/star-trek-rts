using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Deposit fields (owner 2026-11-12: "restrict the amount of mining types there are, and make them larger"): only
/// duranium ore, tritanium ore and deuterium on the landable worlds, each in fields of 3–7 neighbouring hexes with more
/// ore a hex; generated from the map seed, every hex its own deposit.
/// </summary>
public class FieldTests
{
    private static GameData Data => TestData.Real;

    private static readonly string[] Basics = { "deuterium", "duranium_ore", "tritanium_ore" };

    private static SurfaceMap Map(ulong seed, string world) =>
        MatchSetup.CreatePlanetLanding(Data, seed, "federation", null, world).Surface!.Map;

    /// <summary>Every field on the map (each deposit tile in exactly one).</summary>
    private static List<List<int>> Fields(SurfaceMap map)
    {
        var seen = new bool[map.Grid.Count];
        var fields = new List<List<int>>();
        for (var t = 0; t < map.Grid.Count; t++)
        {
            if (map.DepositResource[t] < 0 || seen[t]) continue;
            var f = map.FieldOf(t);
            foreach (var x in f) seen[x] = true;
            fields.Add(f);
        }
        return fields;
    }

    public static IEnumerable<object[]> Worlds() =>
        from seed in new ulong[] { 1, 1701, 99999 } from world in new[] { "tarsis", "veyra" } select new object[] { seed, world };

    [Theory]
    [MemberData(nameof(Worlds))]
    public void FieldsAreContiguousBlobsOfTheirSizeOnTheirTerrain(ulong seed, string world)
    {
        var map = Map(seed, world);
        var fields = Fields(map);
        foreach (var rule in map.Def.Deposits)
        {
            Assert.NotNull(rule.Fields);
            var res = Data.Resource(rule.ResourceId).Index;
            var mine = fields.Where(f => map.DepositResource[f[0]] == res).ToList();
            var want = rule.Fields!.CountFor(map.Grid.Count);
            Assert.InRange(mine.Count, want * 9 / 10, want); // nearly every field the rule asks for fits
            foreach (var f in mine)
            {
                Assert.InRange(f.Count, rule.Fields.SizeMin, rule.Fields.SizeMax);
                foreach (var t in f)
                {
                    Assert.Contains(map.Terrain(t).Id, rule.Terrain);
                    if (rule.Coastal) Assert.True(map.TouchesWater(t));
                    Assert.InRange(map.DepositAmount[t].FloorToInt(), rule.Min, rule.Max);
                    Assert.Equal(map.DepositAmount[t], map.DepositStart[t]);
                }
            }
        }
        // Fields never touch each other: neighbouring deposit hexes are always one field (so FieldOf is exact).
        for (var t = 0; t < map.Grid.Count; t++)
            if (map.DepositResource[t] >= 0)
                foreach (var n in map.Grid.Neighbours(t))
                    if (map.DepositResource[n] >= 0) Assert.Contains(n, map.FieldOf(t));
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void OnlyTheThreeBasicResourcesAreMined(ulong seed, string world)
    {
        var map = Map(seed, world);
        var found = Enumerable.Range(0, map.Grid.Count).Where(t => map.DepositResource[t] >= 0)
            .Select(t => Data.Resources[map.DepositResource[t]].Id).Distinct().OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(Basics, found);
        Assert.Equal(Basics, map.Def.Deposits.Select(d => d.ResourceId).OrderBy(x => x, StringComparer.Ordinal));
        // The resources themselves stay defined (they come back later).
        foreach (var later in new[] { "dilithium", "topaline", "cormaline", "zenite" }) Assert.True(Data.Resource(later).Index >= 0);
    }

    [Fact]
    public void FewerDepositsWithMoreOreEach()
    {
        foreach (var seed in new ulong[] { 1, 1701, 99999 })
        {
            var map = Map(seed, "tarsis");
            var fields = Fields(map);
            double Total(string id) => Enumerable.Range(0, map.Grid.Count).Where(t => map.DepositResource[t] == Data.Resource(id).Index)
                .Sum(t => map.DepositAmount[t].ToDouble());
            // The scattered single tiles they replaced: ~450 duranium deposits (949k), ~120 tritanium (194k), ~60 deuterium (22k).
            Assert.InRange(fields.Count(f => map.DepositResource[f[0]] == Data.Resource("duranium_ore").Index), 80, 120);
            Assert.True(Total("duranium_ore") > 1_000_000, $"seed {seed}: duranium {Total("duranium_ore")}");
            Assert.True(Total("tritanium_ore") > 200_000, $"seed {seed}: tritanium {Total("tritanium_ore")}");
            Assert.True(Total("deuterium") > 30_000, $"seed {seed}: deuterium {Total("deuterium")}");
        }
    }

    [Fact]
    public void SameSeedSameFieldsAndNoSimRng()
    {
        var a = MatchSetup.CreatePlanetLanding(Data, 4242);
        var b = MatchSetup.CreatePlanetLanding(Data, 4242);
        Assert.Equal(a.Surface!.Map.DepositResource, b.Surface!.Map.DepositResource);
        Assert.Equal(a.Surface.Map.DepositAmount, b.Surface.Map.DepositAmount);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.Surface.Map.DepositResource, Map(4243, "tarsis").DepositResource);
        // Map generation leaves the game's own random stream where it was (fields roll on a stream of their own).
        Assert.Equal(a.Rng.NextULong(), MatchSetup.CreatePlanetLanding(Data, 4242).Rng.NextULong());
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void LandingSitesHaveTheBasicsInReach(ulong seed, string world)
    {
        var map = Map(seed, world);
        var grid = map.Grid;
        var radius = Data.Building(Data.Landing.HubBuilding).BuildRadius;
        int Index(string id) => Data.Resource(id).Index;
        bool Near(int site, string res, int reach) => grid.Within(site, reach).Any(t => map.DepositResource[t] == Index(res) && map.Terrain(t).Passable
            && Pathfinder.Find(map, site, t) != null);
        var sites = Enumerable.Range(0, grid.Count).Where(t => map.IsLandingSite(t) && map.Safety(t) != LandingSafety.Unsafe).ToList();
        // Duranium ore usually inside the hub's radius (plains are landing ground).
        var withDuranium = sites.Count(t => grid.Within(t, radius).Any(x => map.DepositResource[x] == Index("duranium_ore")));
        Assert.True(withDuranium * 100 / sites.Count >= 65, $"{withDuranium * 100 / sites.Count}% of landing sites have duranium ore in the hub's radius");
        // A good site: duranium in the hub's radius, tritanium and deuterium a crawler drive away, all reachable by ground.
        Assert.Contains(sites, t => Near(t, "duranium_ore", radius) && Near(t, "tritanium_ore", 16) && Near(t, "deuterium", 16));
        // Tritanium lies in hills only, never in the hub's radius on most sites (it is the crawler's job).
        Assert.All(Enumerable.Range(0, grid.Count).Where(t => map.DepositResource[t] == Index("tritanium_ore")), t => Assert.Equal("hills", map.Terrain(t).Id));
    }

    [Fact]
    public void OrbitHintsStillReflectTheFields()
    {
        var map = Map(1701, "tarsis");
        for (var region = 0; region < map.RegionCount; region++)
        {
            var present = Enumerable.Range(0, map.Grid.Count).Where(t => map.Region(t) == region && map.DepositResource[t] >= 0)
                .Select(t => map.DepositResource[t]).Distinct().OrderBy(r => r);
            Assert.Equal(present, map.Hints(region).Select(h => h.Resource).OrderBy(r => r));
        }
    }

    [Fact]
    public void AMineWorksOneHexOfAField()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        TestData.NoStockTargets(sim);
        var s = sim.Surface!;
        var map = s.Map;
        var site = Enumerable.Range(0, map.Grid.Count).First(t => s.CanLand(0, t)
            && map.Grid.Within(t, 3).Any(x => map.DepositResource[x] == Data.Resource("duranium_ore").Index && map.FieldOf(x).Count >= 3));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
        Assert.Equal(LandingPhase.Landed, s.Players[0].Phase);
        var hex = map.Grid.Within(site, 3).First(x => map.DepositResource[x] == Data.Resource("duranium_ore").Index && x != site
            && s.CheckPlacement(sim, 0, "mine", x) == PlacementProblem.None);
        var field = map.FieldOf(hex);
        var others = field.Where(t => t != hex).ToDictionary(t => t, t => map.DepositAmount[t]);
        sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, "mine", hex, 0));
        sim.Schedule(sim.Tick, new DebugFlagCommand(0, 0, DebugFlag.InstantBuild, true));
        var start = map.DepositAmount[hex];
        for (var i = 0; i < 90 * Simulation.TickRate; i++) sim.Step();
        Assert.True(map.DepositAmount[hex] < start, "the mine dug its own hex");
        foreach (var (t, amount) in others) Assert.Equal(amount, map.DepositAmount[t]); // the rest of the field untouched
    }

    [Theory]
    [InlineData("\"fields\": { \"per_1000_tiles\": 9, \"size\": [3, 7]", "\"fields\": { \"per_1000_tiles\": 9, \"size\": [0, 7]", "fields size")]
    [InlineData("\"fields\": { \"per_1000_tiles\": 9, \"size\": [3, 7]", "\"fields\": { \"per_1000_tiles\": 0, \"size\": [3, 7]", "count or per_1000_tiles")]
    [InlineData("\"fields\": { \"per_1000_tiles\": 9, \"size\": [3, 7]", "\"chance\": 0.1, \"fields\": { \"per_1000_tiles\": 9, \"size\": [3, 7]", "fields or chance")]
    public void BadFieldRulesAreReported(string from, string to, string problem)
    {
        var data = TestData.Patched(("surfaces/planets.json", from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }

    [Fact]
    public void ThePerTileRollStillWorksForARuleWithoutFields()
    {
        var data = TestData.Patched(("surfaces/planets.json",
            "\"terrain\": [\"plains\", \"desert\"], \"coastal\": true, \"fields\": { \"per_1000_tiles\": 3, \"size\": [3, 5], \"spacing\": 3, \"amount\": [360, 520] } }",
            "\"terrain\": [\"plains\", \"desert\"], \"coastal\": true, \"chance\": 0.12, \"amount\": [200, 500] }"));
        Assert.Empty(data.Validate());
        var map = MatchSetup.CreatePlanetLanding(data, 1701).Surface!.Map;
        var deuterium = Enumerable.Range(0, map.Grid.Count).Where(t => map.DepositResource[t] == data.Resource("deuterium").Index).ToList();
        Assert.NotEmpty(deuterium);
        Assert.All(deuterium, t => Assert.InRange(map.DepositAmount[t].FloorToInt(), 200, 500));
    }
}
