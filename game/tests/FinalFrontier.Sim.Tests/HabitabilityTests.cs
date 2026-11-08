using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Habitability tiers (surfaces.json habitability + telemetry limits): every class environment's tier and the
/// checks behind it.</summary>
public class HabitabilityTests
{
    private static GameData Data => TestData.Real;

    [Theory]
    [InlineData("class_m", HabitabilityTier.Open)]
    [InlineData("class_l", HabitabilityTier.Domes)]   // O2 13.5% (below 16%: owner 2026-11-08), CO2 4.5% (6.9× the limit: masks)
    [InlineData("class_h", HabitabilityTier.Masks)]   // hyperonic radiation 18 µSv/h
    [InlineData("class_n", HabitabilityTier.Masks)]   // O3 0.08 ppm at 132 kPa is just over its 0.1 ppm limit at sea-level equivalent
    [InlineData("class_k", HabitabilityTier.Domes)]   // 0.6 kPa
    [InlineData("class_d", HabitabilityTier.Domes)]   // vacuum, -170 to 120 °C
    [InlineData("ice_moon", HabitabilityTier.Domes)]  // 0.4 kPa, -182 °C
    [InlineData("class_y", HabitabilityTier.Tech)]    // 9200 kPa, 390-480 °C, 2400 µSv/h, SO2
    public void EveryClassEnvironmentHasItsTier(string planetClass, HabitabilityTier tier)
    {
        Assert.Equal(tier, WorldHabitability.Assess(Data, planetClass).Tier);
    }

    [Fact]
    public void EveryEnvironmentIsCovered()
    {
        foreach (var e in Data.Environments)
        {
            var a = WorldHabitability.Assess(Data, e.ClassId);
            Assert.Equal(HabitabilityRules.CheckIds.Count, a.Checks.Count);
            Assert.Equal(a.Checks.Max(c => c.Tier), a.Tier);
        }
    }

    [Fact]
    public void TheChecksBehindVeyraAndClassM()
    {
        var m = WorldHabitability.Assess(Data, "class_m");
        Assert.All(m.Checks, c => Assert.Equal(HabitabilityTier.Open, c.Tier));
        var l = WorldHabitability.Assess(Data, "class_l").Checks.ToDictionary(c => c.Check);
        Assert.Equal(HabitabilityTier.Domes, l["o2_pct"].Tier);
        Assert.Equal(HabitabilityTier.Masks, l["gas_limit_x"].Tier); // CO2
        Assert.Equal(HabitabilityTier.Open, l["gravity_g"].Tier);
        Assert.Equal(HabitabilityTier.Open, l["pressure_kpa"].Tier);
        Assert.Equal(HabitabilityTier.Open, l["temperature_c"].Tier);
        Assert.Equal(HabitabilityTier.Open, l["radiation_usv_h"].Tier);
        var d = WorldHabitability.Assess(Data, "class_d").Checks.ToDictionary(c => c.Check);
        Assert.Equal(HabitabilityTier.Suits, d["gravity_g"].Tier);
        Assert.Equal(HabitabilityTier.Suits, d["radiation_usv_h"].Tier);
    }

    [Fact]
    public void GravityAloneCanMakeAWorldImpossibleOrTech()
    {
        Assert.Equal(HabitabilityTier.Impossible, WorldHabitability.Assess(Data, "class_m", Fix64.FromInt(8)).Tier);
        Assert.Equal(HabitabilityTier.Tech, WorldHabitability.Assess(Data, "class_m", Fix64.FromInt(2)).Tier);
        Assert.Equal(HabitabilityTier.Masks, WorldHabitability.Assess(Data, "class_m", Fix64.Parse("1.3")).Tier);
        Assert.Equal(HabitabilityTier.Suits, WorldHabitability.Assess(Data, "class_m", Fix64.Parse("0.1")).Tier);
    }

    [Fact]
    public void TheTierIsFixedOnTheSurfaceAndClassMNeedsNoDomes()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Equal(HabitabilityTier.Open, sim.Surface!.Tier);
        Assert.False(sim.Surface.NeedsDomes);
        Assert.Equal(Data.BuildMenu, sim.Surface.BuildMenu);
        Assert.True(sim.Surface.Covered(0, 0));
        Assert.Equal(Fix64.One, sim.Surface.LifeSupportOf(1));
    }

    [Fact]
    public void BadThresholdsAreReported()
    {
        var unnested = TestData.Patched(("surfaces/worlds.json", "\"masks\": [16, 30], \"domes\": [0, 100]", "\"masks\": [16, 30], \"domes\": [20, 100]"));
        Assert.Contains(unnested.Validate(), p => p.Contains("o2_pct") && p.Contains("hold"));
        var unknown = TestData.Patched(("surfaces/worlds.json", "\"o2_pct\":", "\"o3_pct\":"));
        Assert.Contains(unknown.Validate(), p => p.Contains("'o3_pct'"));
        Assert.Contains(unknown.Validate(), p => p.Contains("'o2_pct'"));
    }
}
