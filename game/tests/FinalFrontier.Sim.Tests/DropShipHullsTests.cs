using FinalFrontier;

namespace FinalFrontier.Sim.Tests;

/// <summary>The pre-warp drop ship pick and hull names (scripts/DropShipHulls.cs, render-only, linked in here).</summary>
public class DropShipHullsTests
{
    [Fact]
    public void OnlyTheDawnOfWarpEraFliesPreWarpShips()
    {
        Assert.True(DropShipHulls.IsPreWarp("dawn_of_warp"));
        Assert.False(DropShipHulls.IsPreWarp("dominion_war"));
        Assert.False(DropShipHulls.IsPreWarp(null));
    }

    [Fact]
    public void ModelPickIsStablePerSeedAndPlayerAndUsesAllThree()
    {
        var seen = new HashSet<int>();
        for (ulong seed = 1; seed < 200; seed++)
            for (var player = 0; player < 4; player++)
            {
                var pick = DropShipHulls.PreWarpModel(seed, player);
                Assert.InRange(pick, 0, DropShipHulls.PreWarpModels - 1);
                Assert.Equal(pick, DropShipHulls.PreWarpModel(seed, player)); // same for the whole game and on reload
                seen.Add(pick);
            }
        Assert.Equal(3, seen.Count);
        // Players on one map don't all get the same ship.
        var mixed = 0;
        for (ulong seed = 1; seed < 100; seed++)
            if (DropShipHulls.PreWarpModel(seed, 0) != DropShipHulls.PreWarpModel(seed, 1)) mixed++;
        Assert.InRange(mixed, 40, 90);
    }

    [Fact]
    public void NamesNeverRepeatForAPlayerAndAreDeterministic()
    {
        const ulong seed = 1701;
        var names = new HashSet<string>();
        var pairs = new HashSet<string>();
        // The first landing (delivery 0, slots 0..2), then 60 deliveries with up to three ships each.
        for (var delivery = 0; delivery <= 60; delivery++)
            for (var slot = 0; slot < 3; slot++)
            {
                var serial = DropShipHulls.Serial(delivery, slot);
                var (name, number) = DropShipHulls.PreWarp(seed, 2, serial);
                Assert.Equal((name, number), DropShipHulls.PreWarp(seed, 2, serial));
                Assert.True(names.Add(name), $"name {name} twice (serial {serial})");
                Assert.True(pairs.Add(name + " " + number));
                Assert.Matches(@"^UE-\d\d$", number);
            }
        Assert.Contains(names, n => n.EndsWith(" II", StringComparison.Ordinal)); // past the list: a round number
    }

    [Fact]
    public void NameListIsUniqueAndOriginal()
    {
        var list = DropShipHulls.PreWarpNames;
        Assert.True(list.Length >= 30);
        Assert.Equal(list.Length, list.Distinct().Count());
        // No real agency's or canon ship's names.
        string[] banned = { "ENTERPRISE", "PHOENIX", "COLUMBIA", "CHALLENGER", "DISCOVERY", "ATLANTIS", "ENDEAVOUR", "APOLLO",
                            "ORION", "ARES", "VALIANT", "HORIZON", "EAGLE", "DRAGON", "NASA", "ESA", "INTREPID", "DEFIANT" };
        foreach (var name in list)
            Assert.DoesNotContain(banned, b => name.Contains(b, StringComparison.Ordinal));
    }

    [Fact]
    public void PlayersGetDifferentOrders()
    {
        var a = Enumerable.Range(0, 6).Select(i => DropShipHulls.PreWarp(42, 0, i).Name).ToArray();
        var b = Enumerable.Range(0, 6).Select(i => DropShipHulls.PreWarp(42, 1, i).Name).ToArray();
        Assert.NotEqual(a, b);
    }
}
