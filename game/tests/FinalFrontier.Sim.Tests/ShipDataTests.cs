using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Tests;

/// <summary>Ship classes' cost, build time, crew cost, tech gate and role (ships/rules.json, tools/ship_costs.py).</summary>
public class ShipDataTests
{
    private const string Miranda = "\"name\": \"Miranda\",       \"hull\": \"frigate\",       \"era\": \"tos\",  \"source\": \"canon\", " +
        "\"cost\": { \"duranium\": 270, \"tritanium\": 110, \"dilithium\": 18 }, \"build_seconds\": 100, \"crew_cost\": 28, \"requires_tech\": \"warp_5\" }";

    [Fact]
    public void EveryClassHasACostBuildTimeAndCrewCost()
    {
        var data = TestData.Real;
        foreach (var c in data.ShipClasses)
        {
            Assert.NotEmpty(c.Cost);
            Assert.True(c.BuildSeconds > Fix64.Zero, c.Key);
            Assert.True(c.CrewCost >= 1, c.Key);
            Assert.True(data.ShipRoles.ContainsKey(c.Role), c.Key);
        }
    }

    /// <summary>The canon complement stays on the hull; crew_cost is on the surface economy's scale (start crew 150).</summary>
    [Fact]
    public void CrewCostFitsTheStartCrewWhileTheHullKeepsTheCanonComplement()
    {
        var data = TestData.Real;
        var startCrew = data.Landing.StartStock.Single(a => data.Resources[a.Resource].Id == "crew").Amount;
        foreach (var c in data.ShipClasses)
        {
            Assert.True(Fix64.FromInt(c.CrewCost) < startCrew, $"{c.Key}: crew_cost {c.CrewCost} vs start crew {startCrew}");
            Assert.True(c.CrewCost <= data.Hull(c.HullId).Crew, c.Key);
        }
        Assert.Equal(1000, data.Hull("explorer").Crew);
    }

    [Fact]
    public void GreatPowersBesidesTheBorgHaveCivilianClasses()
    {
        var data = TestData.Real;
        foreach (var f in new[] { "federation", "klingon", "romulan", "cardassian", "dominion", "ferengi" })
        {
            var roles = data.ShipClasses.Where(c => c.Faction == f && data.ShipRoles[c.Role].Civilian).Select(c => c.Role).ToHashSet();
            Assert.Contains("construction", roles);
            Assert.True(roles.Count >= 2, $"{f}: {string.Join(", ", roles)}");
        }
        Assert.All(data.ShipClasses.Where(c => c.Faction == "borg"), c => Assert.Null(c.RequiresTech));
    }

    [Fact]
    public void BorgShipsAreGatedByAssimilationNotResearch()
    {
        var cube = TestData.Real.ShipClass("borg.Cube");
        Assert.Null(cube.RequiresTech);
        Assert.NotNull(cube.RequiresAssimilated);
        Assert.Contains(cube.Cost, a => TestData.Real.Resources[a.Resource].Id == "nanoprobes");
    }

    [Theory]
    [InlineData("\"cost\": { \"duranium\": 270,", "\"cost\": { \"unobtainium\": 270,", "unknown resource 'unobtainium'")]
    [InlineData("\"cost\": { \"duranium\": 270,", "\"cost\": { \"energy\": 5, \"duranium\": 270,", "power budget")]
    [InlineData("\"cost\": { \"duranium\": 270,", "\"cost\": { \"crew\": 5, \"duranium\": 270,", "use crew_cost")]
    [InlineData("\"build_seconds\": 100, \"crew_cost\": 28,", "\"build_seconds\": 0, \"crew_cost\": 28,", "build_seconds must be above 0")]
    [InlineData("\"build_seconds\": 100, \"crew_cost\": 28,", "\"build_seconds\": 100, \"crew_cost\": 0,", "crew_cost must be at least 1")]
    [InlineData("\"build_seconds\": 100, \"crew_cost\": 28,", "\"build_seconds\": 100,", "missing 'crew_cost'")]
    [InlineData("\"requires_tech\": \"warp_5\" }", "\"requires_tech\": 5 }", "requires_tech must be a tech id or null")]
    [InlineData("\"source\": \"canon\", \"cost\"", "\"source\": \"canon\", \"role\": \"pirate\", \"cost\"", "unknown role 'pirate'")]
    public void BadShipClassDataIsReported(string from, string to, string expected)
    {
        Assert.Contains(from, Miranda);
        var problems = TestData.Patched(("ships/classes/federation.json", Miranda, Miranda.Replace(from, to))).Validate();
        Assert.Contains(problems, p => p.Contains("federation.Miranda") && p.Contains(expected));
    }

    [Fact]
    public void ACivilianHullClassNeedsACivilianRole()
    {
        var problems = TestData.Patched(("ships/classes/federation.json", "\"hull\": \"civilian\", \"era\": \"tos\", \"source\": \"canon\", \"role\": \"freighter\"",
            "\"hull\": \"civilian\", \"era\": \"tos\", \"source\": \"canon\", \"role\": \"science\"")).Validate();
        Assert.Contains(problems, p => p.Contains("federation.Sydney") && p.Contains("hull civilian takes the roles"));
    }
}
