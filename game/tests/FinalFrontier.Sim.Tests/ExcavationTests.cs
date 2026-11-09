using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Ruins on the colony's world and digging them (docs/civilizations-phase2-contract.md, data surfaces/excavation.json):
/// the ruin roll, placement rules, dig layers and every kind of find, defences, pathogens and vehicles that carry them,
/// precursor rewards, the orbital region hint, test-panel ruins, determinism and the hash.</summary>
public class ExcavationTests
{
    private static GameData Real => TestData.Real;
    private static Fix64 F(int v) => Fix64.FromInt(v);
    private static int Sec(int s) => s * Simulation.TickRate;
    private static void InRange(Fix64 v, Fix64 lo, Fix64 hi) => Assert.True(v >= lo && v <= hi, $"{v} not in {lo}..{hi}");
    private const string Ex = "surfaces/excavation.json";

    // ---------------------------------------------------------------- helpers

    /// <summary>The real data with only this find kind left (and layers of 2 s at full speed).</summary>
    private static GameData Only(string kind, params (string File, string From, string To)[] more)
    {
        var weights = new Dictionary<string, int> { ["artifact"] = 26, ["data_core"] = 14, ["tech_fragment"] = 10, ["resources"] = 26, ["defence"] = 14, ["pathogen"] = 10 };
        var patches = weights.Select(w => (Ex, $"{{ \"kind\": \"{w.Key}\", \"weight\": {w.Value} }}", $"{{ \"kind\": \"{w.Key}\", \"weight\": {(w.Key == kind ? 100 : 0)} }}")).ToList();
        patches.Add((Ex, "\"seconds_per_layer\": 120", "\"seconds_per_layer\": 2"));
        patches.AddRange(more);
        return TestData.Patched(patches.ToArray());
    }

    private static Simulation Landed(GameData data, out SurfaceStructure hub, ulong seed = 1701)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, seed);
        TestData.NoStockTargets(sim);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        hub = sim.Surface.Bases(0).Single();
        Do(sim, new DebugSurveyAllCommand(0, 0));
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, $"refused: {sim.LastRefusal}");
    }

    private static void Refused(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
    }

    private static void StepUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), "timed out");
    }

    private static void Steps(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    /// <summary>A ruin (test panel) on open buildable land in the hub's range, with a free buildable neighbour also in range.</summary>
    private static SurfaceRuin AddRuin(Simulation sim, SurfaceStructure hub, out int neighbour)
    {
        var s = sim.Surface!;
        var g = s.Map.Grid;
        bool Open(int t) => s.Map.Terrain(t).Buildable && s.StructureAt(t) == null && s.Map.DepositResource[t] < 0 && s.RuinAt(t) == null &&
                            !s.Map.NearNatives(t) && !s.Units.Any(u => u.Tile == t) && g.Distance(hub.Tile, t) <= 3;
        var tile = g.Within(hub.Tile, 3).First(t => g.Distance(hub.Tile, t) >= 2 && Open(t) && g.Neighbours(t).Any(n => n != hub.Tile && Open(n)));
        neighbour = g.Neighbours(tile).First(n => n != hub.Tile && Open(n));
        Do(sim, new DebugRuinCommand(0, tile, true));
        return s.RuinAt(tile)!;
    }

    /// <summary>A finished Excavation Site on a fresh ruin beside the hub.</summary>
    private static SurfaceStructure Site(Simulation sim, SurfaceStructure hub, out SurfaceRuin ruin, out int neighbour)
    {
        ruin = AddRuin(sim, hub, out neighbour);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "excavation_site", ruin.Tile, 0));
        var site = sim.Surface!.StructureAt(ruin.Tile)!;
        Assert.Equal(StructureState.Complete, site.State);
        return site;
    }

    private static DigFind DigOne(Simulation sim, SurfaceStructure site)
    {
        var before = sim.Surface!.Finds.Count;
        StepUntil(sim, () => sim.Surface.Finds.Count > before, Sec(30));
        return sim.Surface.Finds[^1];
    }

    // ---------------------------------------------------------------- data and the roll

    [Fact]
    public void DataLoadsAndPasses()
    {
        var x = Real.Excavation!;
        Assert.Equal(F(1) / F(4), x.Chance);
        Assert.Equal(("excavation_site", 3, 5, 120), (x.Building, x.LayersMin, x.LayersMax, x.SecondsPerLayer));
        Assert.Equal((20, 40, 1, 180), (x.Defence.DamageMin, x.Defence.DamageMax, x.Defence.Radius, x.Defence.LockSeconds));
        Assert.Equal(300, x.Pathogen.SickSeconds);
        Assert.True(Real.Building("excavation_site").RequiresRuin);
        Assert.True(Real.Building("excavation_site").Crewed);
        Assert.Contains("excavation_site", Real.BuildMenu);
        Assert.Equal("mechanical_spares", Real.KitOfBuilding("excavation_site"));
        Assert.True(Real.HasTech("artifact_analysis") && Real.HasTech("xenomedicine"));
        Assert.Equal(new[] { "iconian", "tkon", "preserver" }, x.Precursors.Select(p => p.Id));
        Assert.Empty(Real.Validate());
        var bad = TestData.Patched((Ex, "\"terrain\": [\"hills\"]", "\"terrain\": [\"plains\"]"), (Ex, "\"tech\": \"xenomedicine\"", "\"tech\": \"nope\""));
        Assert.Contains(bad.Validate(), p => p.Contains("landing ground"));
        Assert.Contains(bad.Validate(), p => p.Contains("pathogen tech 'nope'"));
    }

    [Fact]
    public void RuinsRollFromTheSeedOnAboutOneMapInFourAndNeverOnDepositsNativesOrWater()
    {
        var withRuins = 0;
        const int seeds = 100;
        for (var seed = 1UL; seed <= seeds; seed++)
        {
            var s = MatchSetup.CreatePlanetLanding(Real, seed).Surface!;
            var again = MatchSetup.CreatePlanetLanding(Real, seed).Surface!;
            Assert.Equal(s.Ruins.Select(r => (r.Tile, r.Species, r.Layers, r.Precursor)), again.Ruins.Select(r => (r.Tile, r.Species, r.Layers, r.Precursor)));
            if (s.Ruins.Count > 0) withRuins++;
            foreach (var r in s.Ruins)
            {
                var terrain = s.Map.Terrain(r.Tile);
                Assert.Equal("hills", terrain.Id);
                Assert.True(terrain.Buildable && terrain.Passable);
                Assert.True(s.Map.DepositResource[r.Tile] < 0, "on a deposit");
                Assert.False(s.Map.NearNatives(r.Tile));
                Assert.False(s.Map.Toxic[r.Tile]);
                Assert.False(s.CanLand(0, r.Tile));
                Assert.InRange(r.Layers, 3, r.Precursor == null ? 5 : 7);
                Assert.Equal(0, r.LayersDug);
                Assert.True(s.RuinRegionHint(r.Tile));
            }
        }
        Assert.InRange(withRuins, 12, 40); // chance 0.25
    }

    [Fact]
    public void RuinsDontTouchTheTerrainAndShowAsARegionHint()
    {
        var off = TestData.Patched((Ex, "\"chance\": 0.25", "\"chance\": 0"));
        var on = TestData.Patched((Ex, "\"chance\": 0.25", "\"chance\": 1"));
        var a = MatchSetup.CreatePlanetLanding(off, 1701).Surface!;
        var b = MatchSetup.CreatePlanetLanding(on, 1701).Surface!;
        Assert.Empty(a.Ruins);
        Assert.Single(b.Ruins);
        var g = a.Map.Grid;
        Assert.All(Enumerable.Range(0, g.Count), t => // same terrain, deposits, life
            Assert.Equal((a.Map.Terrain(t).Id, a.Map.DepositResource[t], a.Map.DepositAmount[t], a.Map.Toxic[t], a.Map.WildlifeAt[t]),
                (b.Map.Terrain(t).Id, b.Map.DepositResource[t], b.Map.DepositAmount[t], b.Map.Toxic[t], b.Map.WildlifeAt[t])));
        Assert.Equal(a.Map.NativeTile, b.Map.NativeTile);
        var ruin = b.Ruins[0];
        Assert.Contains(b.Map.Region(ruin.Tile), b.RuinHintRegions);
        Assert.Single(b.RuinHintRegions);
        var other = Enumerable.Range(0, b.Map.Grid.Count).First(t => b.Map.Region(t) != b.Map.Region(ruin.Tile));
        Assert.False(b.RuinRegionHint(other));
        Assert.False(a.RuinRegionHint(ruin.Tile));
        Assert.Equal("artificial structures detected", PlanetSurface.RuinHintText);
    }

    // ---------------------------------------------------------------- placement

    [Fact]
    public void OnlyAnExcavationSiteGoesOnARuinAndOnlyThere()
    {
        var sim = Landed(Real, out var hub);
        var s = sim.Surface!;
        var ruin = AddRuin(sim, hub, out var free);
        sim.Players[0].Stock.Add(Real.Resource("duranium").Index, F(500));
        Assert.Equal(PlacementProblem.NeedsRuin, s.CheckPlacement(sim, 0, "excavation_site", free));
        Assert.Equal(PlacementProblem.RuinInTheWay, s.CheckPlacement(sim, 0, "storage_container", ruin.Tile));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "excavation_site", ruin.Tile));
        Assert.NotNull(PlanetSurface.RuinPlacementText(PlacementProblem.NeedsRuin));
        Assert.NotNull(PlanetSurface.RuinPlacementText(PlacementProblem.RuinInTheWay));
        Refused(sim, new PlaceBuildingCommand(0, "excavation_site", free, 0));
        Refused(sim, new PlaceBuildingCommand(0, "storage_container", ruin.Tile, 0));
        Refused(sim, new DebugPlaceBuildingCommand(0, 0, "storage_container", ruin.Tile, 0));
        Do(sim, new PlaceBuildingCommand(0, "excavation_site", ruin.Tile, 0));
        Assert.Equal("excavation_site", s.StructureAt(ruin.Tile)!.DefId);
        StepUntil(sim, () => s.StructureAt(ruin.Tile)!.State == StructureState.Complete, Sec(120));
        Assert.Equal(StructureStatus.Working, s.StructureAt(ruin.Tile)!.Status);
        Assert.Null(s.DigProblem(s.StructureAt(ruin.Tile)!));
    }

    [Fact]
    public void NoColonyLandsOrFoundsOnRuins()
    {
        var sim = MatchSetup.CreatePlanetLanding(Real, 1701);
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t) && s.Map.DepositResource[t] < 0);
        Do(sim, new DebugRuinCommand(0, site, true));
        Assert.False(s.CanLand(0, site));
        Refused(sim, new LandColonyCommand(0, site));
    }

    // ---------------------------------------------------------------- digging and finds

    [Fact]
    public void LayersAreDugAtTheWorkRateAndEachRollsAFind()
    {
        var data = TestData.Patched((Ex, "\"seconds_per_layer\": 120", "\"seconds_per_layer\": 4"),
            (Ex, "{ \"kind\": \"defence\", \"weight\": 14 }", "{ \"kind\": \"defence\", \"weight\": 0 }"),
            (Ex, "{ \"kind\": \"pathogen\", \"weight\": 10 }", "{ \"kind\": \"pathogen\", \"weight\": 0 }"));
        var sim = Landed(data, out var hub);
        var site = Site(sim, hub, out var ruin, out _);
        var s = sim.Surface!;
        Steps(sim, Sec(2));
        InRange(s.DigProgress(site), F(4) / F(10), F(6) / F(10));
        Assert.Empty(s.Finds);
        StepUntil(sim, () => ruin.LayersDug == 1, Sec(4));
        Assert.Single(s.Finds);
        Assert.Equal((0, ruin.Tile), (s.Finds[0].Player, s.Finds[0].Tile));
        Assert.Contains(s.Finds[0].Kind, new[] { "artifact", "data_core", "tech_fragment", "resources" });
        // Half speed (the test panel's work speed is × the dig too) → about twice as long.
        Do(sim, new DebugStructureCommand(0, site.Id, DebugStructureField.WorkSpeed, F(1) / F(2)));
        var start = sim.Tick;
        StepUntil(sim, () => ruin.LayersDug == 2, Sec(20));
        Assert.InRange(sim.Tick - start, Sec(7), Sec(9));
        // Paused: nothing moves.
        Do(sim, new PauseCommand(0, site.Id, true));
        var progress = s.DigProgress(site);
        Steps(sim, Sec(3));
        Assert.Equal(progress, s.DigProgress(site));
        Assert.Equal("paused", s.DigProblem(site));
    }

    [Fact]
    public void ArtifactsAndDataCoresGiveResearchIntoTheActiveTechOrTheBank()
    {
        foreach (var kind in new[] { "artifact", "data_core" })
        {
            var sim = Landed(Only(kind), out var hub);
            var s = sim.Surface!;
            var site = Site(sim, hub, out _, out _);
            var tech = sim.Data.Tech("ore_conveyors");
            Do(sim, new ResearchCommand(0, "ore_conveyors"));
            var find = DigOne(sim, site);
            Assert.Equal(kind, find.Kind);
            var (min, max) = kind == "artifact" ? (20, 45) : (60, 80);
            InRange(s.Players[0].ResearchProgress[tech.Index], F(min), F(max)); // a data core can finish the 80-point tech
            Assert.Contains("research", find.Effect);
            // No tech running → banked, then paid into the next one started.
            if (s.Players[0].ActiveTech >= 0) Do(sim, new DebugTechCommand(0, 0, "ore_conveyors", true, tech.Cost));
            var banked = DigOne(sim, site);
            Assert.Contains("banked", banked.Effect);
            var bank = s.DigResearchBank(0);
            Assert.True(bank > Fix64.Zero);
            var next = sim.Data.Tech("improved_smelting");
            Do(sim, new ResearchCommand(0, "improved_smelting"));
            sim.Step();
            Assert.Equal(Fix64.Min(bank, next.Cost), s.Players[0].ResearchProgress[next.Index]);
        }
    }

    [Fact]
    public void TechFragmentsGiveAModifierOrResearch()
    {
        var data = Only("tech_fragment", (Ex, "\"name\": \"a self-lubricating bearing alloy\", \"weight\": 3", "\"name\": \"a self-lubricating bearing alloy\", \"weight\": 0"),
            (Ex, "\"name\": \"fragments of field theory\", \"weight\": 4", "\"name\": \"fragments of field theory\", \"weight\": 0"));
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var site = Site(sim, hub, out _, out _);
        Assert.Equal(Fix64.Zero, s.ResearchBonus(0, "refinery", Modifier.Yield));
        var find = DigOne(sim, site);
        Assert.Equal(("tech_fragment", "smelter schematics"), (find.Kind, find.Name));
        Assert.Equal(Fix64.Parse("0.1"), s.ResearchBonus(0, "refinery", Modifier.Yield));
        Assert.Equal(Fix64.Zero, s.ResearchBonus(0, "mine", Modifier.Yield));
        Assert.Single(s.DigModifiers(0));
        Assert.Empty(s.DigModifiers(1));
    }

    [Fact]
    public void ResourceCachesGoIntoStorage()
    {
        var data = Only("resources", (Ex, "{ \"name\": \"a sealed metal store\", \"weight\": 4", "{ \"name\": \"a sealed metal store\", \"weight\": 0"),
            (Ex, "{ \"name\": \"a dilithium cache\", \"weight\": 2", "{ \"name\": \"a dilithium cache\", \"weight\": 0"));
        var sim = Landed(data, out var hub);
        var site = Site(sim, hub, out _, out _);
        var latinum = sim.Data.Resource("latinum").Index;
        var before = sim.Players[0].Stock[latinum];
        var find = DigOne(sim, site);
        Assert.Equal("resources", find.Kind);
        InRange(sim.Players[0].Stock[latinum] - before, F(4), F(15));
        Assert.Contains("Latinum", find.Effect, StringComparison.OrdinalIgnoreCase);

        // Metal that doesn't fit is lost.
        var metal = Only("resources", (Ex, "{ \"name\": \"a dilithium cache\", \"weight\": 2", "{ \"name\": \"a dilithium cache\", \"weight\": 0"),
            (Ex, "{ \"name\": \"a vault of latinum\", \"weight\": 2", "{ \"name\": \"a vault of latinum\", \"weight\": 0"));
        var full = Landed(metal, out var hub2);
        var site2 = Site(full, hub2, out _, out _);
        var cap = full.Surface!.StorageCapacity(0);
        full.Players[0].Stock.Add(full.Data.Resource("duranium").Index, cap); // over full
        var lost = DigOne(full, site2);
        Assert.Contains("lost", lost.Effect);
    }

    [Fact]
    public void ADefenceDamagesWhatIsNearLocksTheDigAndNeverWrecks()
    {
        var data = Only("defence", (Ex, "\"disarmed_chance\": 0.15", "\"disarmed_chance\": 0"));
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var site = Site(sim, hub, out var ruin, out var near);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "prefab_habitat", near, 0));
        var store = s.StructureAt(near)!;
        Do(sim, new DebugStructureCommand(0, store.Id, DebugStructureField.Condition, F(10)));
        var far = s.Structures.Single(x => x.Id == hub.Id); // the hub is 2+ tiles away
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, F(5)));
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, near));
        var hubCondition = far.Condition;
        var siteCondition = site.Condition;
        var find = DigOne(sim, site);
        Assert.Equal("defence", find.Kind);
        Assert.Contains("locked", find.Effect);
        InRange(store.Condition, F(9) / F(10), F(1)); // 10 − 20..40 → clamped at 1 (then a tick of wear)
        Assert.False(store.Broken);
        Assert.Equal(F(1), rover.Integrity);
        Assert.False(rover.Broken);
        InRange(siteCondition - site.Condition, F(19), F(41)); // the site itself is within the radius (plus a little wear)
        Assert.True(far.Condition >= hubCondition - F(1)); // out of reach (only wear)
        // Locked: no power, no progress, a countdown.
        Assert.True(ruin.LockedUntilTick > sim.Tick);
        sim.Step();
        Assert.Equal(StructureStatus.NoInput, site.Status);
        Assert.Equal("locked by a defence system: 3:00", s.DigProblem(site)); // 180 s, rounded up
        var dug = ruin.LayersDug;
        Steps(sim, Sec(170));
        Assert.Equal(dug, ruin.LayersDug);
        StepUntil(sim, () => ruin.LayersDug > dug, Sec(15));
    }

    [Fact]
    public void ArtifactAnalysisDisarmsMoreAndHalvesTheDamage()
    {
        var disarm = Only("defence", (Ex, "\"tech_disarmed_chance\": 0.6", "\"tech_disarmed_chance\": 1"));
        var sim = Landed(disarm, out var hub);
        var site = Site(sim, hub, out var ruin, out _);
        Do(sim, new DebugTechCommand(0, 0, "artifact_analysis", true, sim.Data.Tech("artifact_analysis").Cost));
        var find = DigOne(sim, site);
        Assert.Equal("found disarmed", find.Effect);
        Assert.Equal(0, ruin.LockedUntilTick);

        var half = Only("defence", (Ex, "\"tech_disarmed_chance\": 0.6", "\"tech_disarmed_chance\": 0"), (Ex, "\"disarmed_chance\": 0.15", "\"disarmed_chance\": 0"));
        var sim2 = Landed(half, out var hub2);
        var site2 = Site(sim2, hub2, out _, out _);
        Do(sim2, new DebugTechCommand(0, 0, "artifact_analysis", true, sim2.Data.Tech("artifact_analysis").Cost));
        var before = site2.Condition;
        DigOne(sim2, site2);
        InRange(before - site2.Condition, F(9), F(21)); // 20..40 × 0.5 (plus a little wear)
    }

    [Fact]
    public void APathogenMakesTheBaseSickAndSlowsItsWork()
    {
        var sim = Landed(Only("pathogen"), out var hub);
        var s = sim.Surface!;
        var site = Site(sim, hub, out _, out var near);
        var find = DigOne(sim, site);
        Assert.Equal("pathogen", find.Kind);
        Assert.InRange(s.CrewSickSeconds(hub.Id), 299, 300);
        Assert.Equal(Fix64.Parse("0.6"), s.CrewHealth(hub.Id));

        // Construction at the sick base runs at 0.6.
        Do(sim, new PauseCommand(0, site.Id, true));
        Do(sim, new PlaceBuildingCommand(0, "storage_container", near, 0));
        var building = s.StructureAt(near)!;
        Steps(sim, Sec(10));
        Assert.InRange(building.BuildTicks, Sec(10) * 6 / 10 - 2, Sec(10) * 6 / 10 + 2);

        // It wears off.
        Steps(sim, Sec(300));
        Assert.Equal(0, s.CrewSickSeconds(hub.Id));
        Assert.Equal(Fix64.One, s.CrewHealth(hub.Id));
    }

    [Fact]
    public void XenomedicineShortensTheSickness()
    {
        var sim = Landed(Only("pathogen"), out var hub);
        var site = Site(sim, hub, out _, out _);
        Do(sim, new DebugTechCommand(0, 0, "xenomedicine", true, sim.Data.Tech("xenomedicine").Cost));
        DigOne(sim, site);
        Assert.InRange(sim.Surface!.CrewSickSeconds(hub.Id), 119, 120); // 300 × 0.4
    }

    [Fact]
    public void ASickCrewRepairsSlower()
    {
        var sim = Landed(Only("pathogen"), out var hub);
        var s = sim.Surface!;
        var site = Site(sim, hub, out _, out _);
        DigOne(sim, site);
        Do(sim, new PauseCommand(0, site.Id, true));
        var kits = sim.Data.Resource("mechanical_spares").Index;
        sim.Players[0].Stock.Add(kits, F(10));
        // Wear a site down and have the crew fix it: 10 a second healthy, 6 sick.
        Do(sim, new DebugStructureCommand(0, site.Id, DebugStructureField.Condition, F(40)));
        Do(sim, new MaintainCommand(0, site.Id));
        StepUntil(sim, () => hub.CrewPhase == CrewPhase.Working, Sec(20));
        var c0 = site.Condition;
        Steps(sim, Sec(5));
        InRange(site.Condition - c0, F(28), F(31)); // 5 s × 10 × 0.6
    }

    [Fact]
    public void VehiclesCarryThePathogenToTheOtherBases()
    {
        var sim = Landed(Only("pathogen"), out var hub);
        var s = sim.Surface!;
        var site = Site(sim, hub, out _, out _);
        var g = s.Map.Grid;
        var other = Enumerable.Range(0, g.Count).First(t => g.Distance(t, hub.Tile) >= 12 && s.Map.Terrain(t).Buildable && s.StructureAt(t) == null &&
                                                              s.Map.DepositResource[t] < 0 && s.RuinAt(t) == null && !s.Map.NearNatives(t));
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "colony_hub", other, 0));
        var second = s.StructureAt(other)!;
        var rover = s.Units.First(u => u.DefId == "survey_rover");
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, other)); // nothing to pick up yet
        Assert.False(s.Carrier(rover));
        DigOne(sim, site);
        Do(sim, new PauseCommand(0, site.Id, true));
        Assert.True(s.CrewSickSeconds(hub.Id) > 290);
        Assert.Equal(0, s.CrewSickSeconds(second.Id));

        Do(sim, new DebugMoveUnitCommand(0, rover.Id, hub.Tile));
        sim.Step();
        Assert.True(s.Carrier(rover));
        Assert.InRange(s.CarrierSeconds(rover), s.CrewSickSeconds(hub.Id) - 1, s.CrewSickSeconds(hub.Id) + 1);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, other));
        sim.Step();
        var spread = s.CrewSickSeconds(second.Id);
        Assert.InRange(spread, s.CarrierSeconds(rover) / 2 - 2, s.CarrierSeconds(rover) / 2 + 2); // spread_share 0.5
        Assert.True(s.ImmuneSeconds(second.Id) > spread);

        // Once well again it is immune for a while: the carrier, still carrying from the first base, can't re-infect it.
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, hub.Tile));
        Steps(sim, Sec(spread + 2));
        Assert.Equal(0, s.CrewSickSeconds(second.Id));
        Assert.True(s.Carrier(rover));
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, other));
        Steps(sim, 3);
        Assert.Equal(0, s.CrewSickSeconds(second.Id));
        // Everything runs out.
        Steps(sim, Sec(400));
        Assert.False(s.Carrier(rover));
        Assert.Equal(0, s.CrewSickSeconds(hub.Id));
        Assert.Equal(0, s.ImmuneSeconds(second.Id));
    }

    [Fact]
    public void AnExhaustedSiteStopsAndCanBeSalvaged()
    {
        var data = TestData.Patched((Ex, "\"seconds_per_layer\": 120", "\"seconds_per_layer\": 2"), (Ex, "\"layers\": [3, 5]", "\"layers\": [2, 2]"),
            (Ex, "{ \"kind\": \"defence\", \"weight\": 14 }", "{ \"kind\": \"defence\", \"weight\": 0 }"),
            (Ex, "{ \"kind\": \"pathogen\", \"weight\": 10 }", "{ \"kind\": \"pathogen\", \"weight\": 0 }"),
            (Ex, "\"species\": { \"weight\": 60", "\"species\": { \"weight\": 1000000"));
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var site = Site(sim, hub, out var ruin, out _);
        Assert.Equal(2, ruin.Layers);
        StepUntil(sim, () => ruin.Exhausted, Sec(10));
        Assert.Equal(2, s.Finds.Count);
        sim.Step();
        Assert.Equal(StructureStatus.Depleted, site.Status);
        Assert.Equal("site exhausted: every layer is dug", s.DigProblem(site));
        Assert.Equal(Fix64.One, s.DigProgress(site));
        Steps(sim, Sec(10));
        Assert.Equal(2, s.Finds.Count);
        // Salvage like any building; the ruin stays, dug out, and other buildings may go there now.
        var duranium = sim.Data.Resource("duranium").Index;
        var before = sim.Players[0].Stock[duranium];
        Do(sim, new SalvageCommand(0, site.Id));
        Assert.Null(s.StructureAt(ruin.Tile));
        Assert.True(sim.Players[0].Stock[duranium] > before);
        Assert.Same(ruin, s.RuinAt(ruin.Tile));
        Assert.True(ruin.Exhausted);
        Assert.Equal(PlacementProblem.NeedsRuin, s.CheckPlacement(sim, 0, "excavation_site", ruin.Tile));
        Assert.NotEqual(PlacementProblem.RuinInTheWay, s.CheckPlacement(sim, 0, "storage_container", ruin.Tile));
    }

    [Fact]
    public void APrecursorSiteGivesItsRewardOnTheLastLayer()
    {
        var data = TestData.Patched((Ex, "\"seconds_per_layer\": 120", "\"seconds_per_layer\": 2"),
            (Ex, "\"species\": { \"weight\": 60", "\"species\": { \"weight\": 0"),
            (Ex, "\"species\": \"Iconians\", \"weight\": 1, \"layers\": [5, 7], \"danger\": 2", "\"species\": \"Iconians\", \"weight\": 1, \"layers\": [1, 1], \"danger\": 0"),
            (Ex, "\"species\": \"T'Kon\", \"weight\": 1", "\"species\": \"T'Kon\", \"weight\": 0"),
            (Ex, "\"species\": \"Preservers\", \"weight\": 1", "\"species\": \"Preservers\", \"weight\": 0"));
        var sim = Landed(data, out var hub);
        var s = sim.Surface!;
        var site = Site(sim, hub, out var ruin, out _);
        Assert.Equal(("iconian", "Iconians", 1), (ruin.Precursor, ruin.Species, ruin.Layers));
        StepUntil(sim, () => ruin.Exhausted, Sec(10));
        Assert.Equal(2, s.Finds.Count); // the layer's find, then the reward
        Assert.Equal(("precursor", "an Iconian gateway fragment"), (s.Finds[1].Kind, s.Finds[1].Name));
        Assert.Contains(s.Finds[0].Kind, new[] { "artifact", "data_core", "tech_fragment", "resources" }); // danger 0
        Assert.Equal(F(1) / F(2), s.ShuttleTurnaround(0));
        Assert.Equal(Fix64.One, s.ShuttleTurnaround(1));
        // The drop ship comes back in half the time.
        sim.Players[0].Stock.Add(sim.Data.Resource("deuterium").Index, F(50));
        sim.Players[0].Stock.Add(sim.Data.Resource("duranium").Index, F(500));
        sim.Players[0].Stock.Add(sim.Data.Resource("tritanium").Index, F(500));
        var tile = s.Map.Grid.Within(hub.Tile, 6).First(t => s.ShuttleProblem(sim, 0, t, "survey_rover") == null);
        var flight = s.DescentTicks(sim.Data.Orbit!.ShuttleSeconds);
        var now = sim.Tick;
        Do(sim, new ShuttleCommand(0, tile, "survey_rover"));
        Assert.Equal(now + flight, s.Players[0].ShuttleBackTick);

        // T'Kon: mines and deep-core mines faster.
        var tkon = TestData.Patched((Ex, "\"seconds_per_layer\": 120", "\"seconds_per_layer\": 2"),
            (Ex, "\"species\": { \"weight\": 60", "\"species\": { \"weight\": 0"),
            (Ex, "\"species\": \"Iconians\", \"weight\": 1", "\"species\": \"Iconians\", \"weight\": 0"),
            (Ex, "\"species\": \"T'Kon\", \"weight\": 1, \"layers\": [5, 7], \"danger\": 2", "\"species\": \"T'Kon\", \"weight\": 1, \"layers\": [1, 1], \"danger\": 0"),
            (Ex, "\"species\": \"Preservers\", \"weight\": 1", "\"species\": \"Preservers\", \"weight\": 0"));
        var sim2 = Landed(tkon, out var hub2);
        var site2 = Site(sim2, hub2, out var ruin2, out _);
        StepUntil(sim2, () => ruin2.Exhausted, Sec(10));
        Assert.Equal(Fix64.Parse("0.3"), sim2.Surface!.ResearchBonus(0, "mine", Modifier.Speed));
        Assert.Equal(Fix64.Zero, sim2.Surface.ResearchBonus(0, "refinery", Modifier.Speed));
    }

    [Fact]
    public void PrecursorSitesAreMoreDangerous()
    {
        // With danger 2, defence + pathogen weigh 48 of 124 instead of 24 of 100.
        var data = TestData.Patched((Ex, "\"seconds_per_layer\": 120", "\"seconds_per_layer\": 1"),
            (Ex, "\"species\": { \"weight\": 60", "\"species\": { \"weight\": 0"),
            (Ex, "\"layers\": [5, 7]", "\"layers\": [50, 50]"),
            (Ex, "\"lock_seconds\": 180", "\"lock_seconds\": 0"));
        var sim = Landed(data, out var hub);
        var site = Site(sim, hub, out var ruin, out _);
        Do(sim, new DebugFlagCommand(0, 0, DebugFlag.UnlimitedPower, true));
        Do(sim, new DebugStructureCommand(0, site.Id, DebugStructureField.NoWear, Fix64.One));
        StepUntil(sim, () => ruin.LayersDug >= 40, Sec(200));
        var finds = sim.Surface!.Finds.Where(f => f.Kind != "precursor").ToList();
        var bad = finds.Count(f => f.Kind is "defence" or "pathogen");
        Assert.InRange(bad * 100 / finds.Count, 20, 65); // ~39% (danger 1.5 for Preservers: ~32%)
    }

    // ---------------------------------------------------------------- test panel, determinism, hash

    [Fact]
    public void TestPanelAddsAndRemovesRuinsAndRefusesBadTiles()
    {
        var sim = Landed(Real, out var hub);
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var water = Enumerable.Range(0, g.Count).First(t => !s.Map.Terrain(t).Passable);
        var deposit = Enumerable.Range(0, g.Count).First(t => s.Map.DepositResource[t] >= 0 && s.Map.Terrain(t).Buildable && s.StructureAt(t) == null);
        Assert.Equal("can't put ruins on that ground", sim.DebugProblem(new DebugRuinCommand(0, water, true)));
        Assert.Equal("a deposit is there", sim.DebugProblem(new DebugRuinCommand(0, deposit, true)));
        Assert.Equal("a building is there", sim.DebugProblem(new DebugRuinCommand(0, hub.Tile, true)));
        Assert.Equal("off the map", sim.DebugProblem(new DebugRuinCommand(0, -1, true)));
        Refused(sim, new DebugRuinCommand(0, water, true));

        var ruin = AddRuin(sim, hub, out var free);
        Assert.Equal("ruins are already there", sim.DebugProblem(new DebugRuinCommand(0, ruin.Tile, true)));
        Assert.Equal("no ruins there", sim.DebugProblem(new DebugRuinCommand(0, free, false)));
        Assert.InRange(ruin.Layers, 3, 7);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "excavation_site", ruin.Tile, 0));
        Assert.Equal("a building is on the ruins (remove it first)", sim.DebugProblem(new DebugRuinCommand(0, ruin.Tile, false)));
        Do(sim, new DebugRemoveStructureCommand(0, s.StructureAt(ruin.Tile)!.Id));
        var count = s.Ruins.Count;
        Do(sim, new DebugRuinCommand(0, ruin.Tile, false));
        Assert.Null(s.RuinAt(ruin.Tile));
        Assert.Equal(count - 1, s.Ruins.Count);
    }

    private static List<ulong> Run(GameData data)
    {
        var sim = Landed(data, out var hub);
        var site = Site(sim, hub, out _, out var near);
        Do(sim, new PlaceBuildingCommand(0, "storage_container", near, 0));
        var rover = sim.Surface!.Units.First(u => u.DefId == "survey_rover");
        var hashes = new List<ulong>();
        for (var i = 0; i < Sec(240); i++)
        {
            if (i == Sec(20)) sim.Schedule(sim.Tick, new DebugMoveUnitCommand(0, rover.Id, hub.Tile));
            sim.Step();
            if (i % 30 == 0) hashes.Add(sim.StateHash());
        }
        Assert.True(sim.Surface.Finds.Count >= 10);
        return hashes;
    }

    [Fact]
    public void DiggingIsDeterministicAndHashed()
    {
        var data = TestData.Patched((Ex, "\"seconds_per_layer\": 120", "\"seconds_per_layer\": 3"), (Ex, "\"layers\": [3, 5]", "\"layers\": [40, 40]"),
            (Ex, "\"lock_seconds\": 180", "\"lock_seconds\": 5"));
        Assert.Equal(Run(data), Run(data));

        // Ruins are in the hash; a map with none (here: one added and removed again) hashes exactly as before ruins existed.
        var a = Landed(Real, out var hub);
        var b = Landed(Real, out _);
        Assert.Equal(a.StateHash(), b.StateHash());
        var ruin = AddRuin(a, hub, out _);
        Do(b, new DebugSurveyAllCommand(0, 0)); // the same number of test-panel edits
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Do(a, new DebugRuinCommand(0, ruin.Tile, false));
        Do(b, new DebugSurveyAllCommand(0, 0));
        Assert.Empty(a.Surface!.Ruins);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void RefusedRuinCommandsChangeNothing()
    {
        var sim = Landed(Real, out var hub);
        var water = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => !sim.Surface.Map.Terrain(t).Passable);
        sim.Step();
        var a = sim.StateHash();
        var b = Landed(Real, out _);
        b.Step();
        Assert.Equal(a, b.StateHash());
        Refused(sim, new DebugRuinCommand(0, water, true));
        Refused(sim, new DebugRuinCommand(0, hub.Tile, false));
        b.Step();
        b.Step();
        Assert.Equal(b.StateHash(), sim.StateHash());
    }
}
