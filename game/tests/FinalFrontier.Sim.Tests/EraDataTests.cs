using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Tests;

/// <summary>eras.json and tech_tree.json: loaded read-only into GameData and checked against factions.json and the rest.</summary>
public class EraDataTests
{
    private static void HasProblem(GameData data, string part) =>
        Assert.Contains(data.Validate(), p => p.Contains(part));

    [Fact]
    public void ErasAndTheTechTreeLoad()
    {
        var data = TestData.Real;
        Assert.Contains(data.Eras, e => e.Id == "dominion_war" && e.YearFrom == 2373 && e.YearTo == 2379); // design rule
        Assert.Equal(data.Orbit!.Era, data.Maps.Single().Era);
        Assert.NotNull(data.Era(data.Orbit.Era));
        Assert.NotEmpty(data.TechTree.Techs);
        Assert.Equal(7, data.TechTree.Trees.Count);
        var borg = Assert.Single(data.TechTree.FactionRules, r => r.Faction == "borg");
        Assert.False(borg.Research);
        Assert.Equal("nanoprobes", borg.CombineResource);
        Assert.Contains("picard", data.Faction("borg").Eras);
        Assert.Equal("minor", data.Faction("xindi").Role);
        Assert.Contains("eras/eras.json", data.Files);
        Assert.Contains("tech_tree/rules.json", data.Files);
    }

    [Fact]
    public void AnEraNamingAnUnknownFactionIsAProblem() =>
        HasProblem(TestData.Patched(("eras/eras.json", "\"krenim\"]", "\"krenim\", \"malon_typo\"]")),
            "Era voy: unknown faction 'malon_typo'");

    [Fact]
    public void OnlyPlayableFactionsCanBePlayedInAnEra() =>
        HasProblem(TestData.Patched(("eras/eras.json", "\"playable\": [\"federation_lost_ship\", \"borg\"]", "\"playable\": [\"federation_lost_ship\", \"borg\", \"kazon\"]")),
            "Era voy: kazon is playable here, but factions.json marks it \"pirate\"");

    [Fact]
    public void FactionsAndErasMustAgreeOnWhichErasAFactionIsIn()
    {
        HasProblem(TestData.Patched(("factions/borg.json", "\"voy\", \"picard\"]", "\"voy\"]")), "Era picard lists borg, but factions.json borg eras doesn't list picard.");
        HasProblem(TestData.Patched(("factions/dominion.json", "\"eras\": [\"dominion_war\"]", "\"eras\": [\"dominion_war\", \"tng\"]")),
            "Faction dominion: lists era tng, but eras.json tng has it in neither playable nor ai_only.");
    }

    [Fact]
    public void TheMatchEraIsTheMapsEra() =>
        HasProblem(TestData.Patched(("surfaces/orbit.json", "\"era\": \"dominion_war\"", "\"era\": \"tng\"")),
            "Map mvp_duel is set in era 'dominion_war', but surfaces.json orbit.era is 'tng'");

    [Fact]
    public void AnEraLayerMayChangeTheMatchEra() =>
        Assert.Empty(GameData.Load(TestData.Source, new DataSetup(Era: "ent")).Validate());

    [Fact]
    public void TechTreeReferencesMustResolve()
    {
        HasProblem(TestData.Patched(("tech_tree/techs/propulsion.json", "\"prereqs\": [\"warp_5\"], \"unlocks\": [\"recrystallizer\"]", "\"prereqs\": [\"warp_55\"], \"unlocks\": [\"recrystallizer\"]")),
            "Tech tree dilithium_recrystallization: unknown prerequisite 'warp_55'.");
        HasProblem(TestData.Patched(("tech_tree/techs/propulsion.json", "{ \"id\": \"warp_8\",", "{ \"id\": \"warp_5\",")), "Tech tree: duplicate tech id 'warp_5'.");
    }

    [Fact]
    public void TheBorgNeverResearchRuleMustBeThere() =>
        HasProblem(TestData.Patched(("tech_tree/rules.json", "{\"borg\": {\"research\": false,", "{\"borg\": {\"research\": true,")), "the Borg never research");
}
