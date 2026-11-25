using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Space;
using static FinalFrontier.Sim.Tests.SpaceJobs;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Space jobs, phase 4 (docs/space-jobs-contract.md "Phase 4"): the eight chain missions' sites and rules. The chain-only
/// kinds (a hidden ship found by fixes, an alien probe that follows, is decoded and answered), the mission tweaks of older
/// kinds (lifepods that need hold room, a cold reactor, a rogue rock aimed at a named body, a tow handed over at a body),
/// and on_lost for main missions. The chains themselves are played to the end, twice, by ShipPickTests.
/// </summary>
public class SpaceChainJobTests
{
    private const ulong Seed = 1701;

    /// <summary>A space ship's game with its chain on and every main mission before `mission` completed by the test panel.</summary>
    private static Simulation ChainAt(string ship, string mission, ulong seed = Seed, string? start = null, FitOut? fit = null)
    {
        var sim = fit != null ? MatchSetup.CreateSystemGame(Campaign, seed, "federation", start, ship, fit) : MatchSetup.CreateSystemGame(Campaign, seed, "federation", start, ship);
        Assert.Null(sim.Missions.Enable(Campaign.ShipChoice(ship)!.Chain));
        Seconds(sim, 1);
        foreach (var (id, _) in sim.Missions.States(0))
        {
            if (id == mission) break;
            Do(sim, new DebugMissionCommand(0, 0, id, DebugMissionAction.Complete, "", "", -1));
        }
        Until(sim, () => sim.Missions.View(0, mission)?.State == MissionState.Active, 5, $"{mission} starts");
        return sim;
    }

    private static string MarkerSite(Simulation sim, string label) =>
        sim.Missions.SpaceMarkers(0).First(m => m.SiteId != null && m.Label == label).SiteId!;

    private static void Ok(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        Do(sim, c);
        Assert.True(refused == sim.RefusedCommands, $"refused: {sim.LastRefusal}");
    }

    private static void FlyToSite(Simulation sim, string site)
    {
        var ship = Ship(sim);
        Until(sim, () => !ship.InFlight, 300, "lands first");
        if (ship.AtSite == site) return;
        Ok(sim, new SiteCourseCommand(0, ship.Id, site));
        Until(sim, () => ship.AtSite == site, 300, $"reaches {site}");
    }

    private static void ScanWhenReady(Simulation sim)
    {
        var ship = Ship(sim);
        Until(sim, () => sim.System!.ScanProblem(0, ship.Id) == null && !sim.System.ScanUsesProbe(0, ship.Id), 200, "scanners ready");
        Ok(sim, new LongRangeScanCommand(0, ship.Id));
    }

    private static ulong CivSeed()
    {
        ulong seed = 1;
        while (!MatchSetup.HasLivingCivilization(Campaign, seed)) seed++;
        return seed;
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void ChainOnlyKindsAreNeverRolledNorArriveAsEvents()
    {
        Assert.Empty(Campaign.Validate());
        Assert.NotNull(Campaign.SpaceSites!.Kind(SiteKind.HiddenShip)!.Hidden);
        Assert.NotNull(Campaign.SpaceSites.Kind(SiteKind.AlienProbe)!.Probe);
        // Events come fast and often on this copy: still none of the chain-only kinds.
        var fast = TestData.Patched(("maps/events.json", "\"calm_seconds\": 300", "\"calm_seconds\": 5"), ("maps/events.json", "\"every_seconds\": [300, 420]", "\"every_seconds\": [20, 30]"));
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var sim = MatchSetup.CreateSystemGame(fast, seed);
            Assert.DoesNotContain(sim.System!.Sites, s => GameData.ChainOnlyKinds.Contains(s.Kind));
            Seconds(sim, 300);
            Assert.DoesNotContain(sim.System.Sites, s => GameData.ChainOnlyKinds.Contains(s.Kind));
        }
        var rolled = TestData.Patched(("maps/sites.json", "\"weights\": { \"derelict\": 3, \"known_derelict\": 3, \"debris\": 4 }",
            "\"weights\": { \"derelict\": 3, \"known_derelict\": 3, \"debris\": 4, \"hidden_ship\": 1 }"));
        Assert.Contains(rolled.Validate(), p => p.Contains("hidden_ship is placed only by a mission"));
        var badProbe = TestData.Patched(("maps/sites.json", "\"reply_table\": \"probe_reply\"", "\"reply_table\": \"nope\""));
        Assert.Contains(badProbe.Validate(), p => p.Contains("reply_table 'nope'"));
    }

    [Theory]
    [InlineData("watch_station.json", "\"title\": \"Turn it aside\", \"on_lost\": \"turn\"", "\"title\": \"Turn it aside\", \"on_lost\": \"nowhere\"", "on_lost 'nowhere'")]
    [InlineData("space_jobs.json", "{ \"id\": \"identify\", \"title\": \"Find the rock\",", "{ \"id\": \"identify\", \"title\": \"Find the rock\", \"on_lost\": \"complete\",", "on_lost is for main missions")]
    [InlineData("priority_run.json", "\"state\": \"delivered\", \"body\": \"port\"", "\"state\": \"delivered\"", "needs a body")]
    [InlineData("priority_run.json", "\"pods\": 12, \"hold_room\": true", "\"pods\": 40, \"hold_room\": true", "pods (1..31)")]
    [InlineData("watch_station.json", "\"bind\": \"smuggler\", \"near\": \"belt\"", "\"bind\": \"smuggler\", \"near\": \"moon\"", "near 'moon'")]
    [InlineData("envoy.json", "{ \"civ\": { \"at\": \"world\", \"incident\": 30 } }", "{ \"civ\": { \"at\": \"world\" } }", "needs awareness, incident or settle")]
    public void BadPhase4MissionDataIsReported(string file, string from, string to, string problem)
    {
        var data = TestData.Patched(("missions/" + file, from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }

    // ---------------------------------------------------------------- the hidden ship

    [Fact]
    public void TheSmugglerIsFoundByFixesFromDifferentRocksThenInspected()
    {
        var sim = ChainAt("bastion", "smugglers_belt");
        var sys = sim.System!;
        var search = Assert.Single(sim.Missions.SpaceMarkers(0), m => m.Kind == "search");
        var smuggler = search.SiteId!;
        Assert.Equal(SiteKnowledge.Unknown, sys.SiteKnowledgeOf(0, smuggler));
        Assert.Null(sys.SiteReportFor(0, smuggler)); // nothing to see of her yet
        Assert.Equal(SiteKind.HiddenShip, sys.Site(smuggler)!.Kind);
        Assert.Contains("no such site", Refused(sim, new SiteCourseCommand(0, Ship(sim).Id, smuggler)));
        var area = sys.HiddenSearchArea(0, smuggler)!.Value;
        var rocks = sim.Missions.SpaceMarkers(0).Where(m => m.Label == "Scan from here").Select(m => m.SiteId!).ToList();
        Assert.Equal(3, rocks.Count);
        var hd = Campaign.SpaceSites!.Kind(SiteKind.HiddenShip)!.Hidden!;
        Assert.All(rocks, r => Assert.True(FixVec2.Distance(sys.Site(r)!.Position, sys.Site(smuggler)!.Position) <= hd.FixRange));

        FlyToSite(sim, rocks[0]);
        ScanWhenReady(sim);
        Assert.Equal((1, 3), sys.HiddenFixes(0, smuggler));
        Assert.True(sys.HiddenSearchArea(0, smuggler)!.Value.Radius < area.Radius, "the area shrinks");
        ScanWhenReady(sim); // the same spot again: no new fix
        Assert.Equal((1, 3), sys.HiddenFixes(0, smuggler));
        Assert.Contains(sys.StatusOf(0)!.Log, l => l.Text.StartsWith("No new fix", StringComparison.Ordinal));
        FlyToSite(sim, rocks[1]);
        ScanWhenReady(sim);
        FlyToSite(sim, rocks[2]);
        ScanWhenReady(sim);
        Assert.Equal((3, 3), sys.HiddenFixes(0, smuggler));
        Assert.Equal(SiteKnowledge.Detected, sys.SiteKnowledgeOf(0, smuggler));
        Assert.Null(sys.HiddenSearchArea(0, smuggler));
        Until(sim, () => sim.Missions.View(0, "smugglers_belt")!.StageIndex == 1, 3, "the hunt is over");

        Identify(sim, smuggler);
        var site = sys.Site(smuggler)!;
        if (site.PathStart >= 0 && sim.Tick < site.PathEnd) // she ran: no coming alongside until she stops
        {
            Assert.Contains("running", sys.SiteWorkProblem(0, Ship(sim).Id, smuggler, "alongside"));
            Until(sim, () => sim.Tick >= site.PathEnd, 120, "she stops");
        }
        var latinum = sim.Players[0].Stock[Campaign.Resource("latinum").Index];
        Assert.Equal("come alongside first", sys.SiteWorkProblem(0, Ship(sim).Id, smuggler, "inspect"));
        Work(sim, smuggler, "alongside", () => sys.SiteWork(0, smuggler)!.Alongside);
        Work(sim, smuggler, "inspect", () => sys.SiteWork(0, smuggler)!.Inspected);
        Assert.True(sim.Players[0].Stock[Campaign.Resource("latinum").Index] > latinum, "fined");
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, smuggler));
        Until(sim, () => sim.Missions.View(0, "smugglers_belt")!.State == MissionState.Completed, 3, "mission done");
    }

    [Fact]
    public void SomeSmugglersRunOnceWhenFoundOthersStay()
    {
        var ran = 0;
        var stayed = 0;
        for (ulong seed = 1; seed <= 6 && (ran == 0 || stayed == 0); seed++)
        {
            var sim = ChainAt("bastion", "smugglers_belt", seed);
            var sys = sim.System!;
            var smuggler = sim.Missions.SpaceMarkers(0).First(m => m.Kind == "search").SiteId!;
            foreach (var rock in sim.Missions.SpaceMarkers(0).Where(m => m.Label == "Scan from here").Select(m => m.SiteId!).ToList())
            {
                FlyToSite(sim, rock);
                ScanWhenReady(sim);
            }
            if (sys.SiteKnowledgeOf(0, smuggler) < SiteKnowledge.Detected) continue; // two rocks too close together on this seed
            var site = sys.Site(smuggler)!;
            if (site.PathStart < 0)
            {
                stayed++;
                continue;
            }
            ran++;
            var hd = Campaign.SpaceSites!.Kind(SiteKind.HiddenShip)!.Hidden!;
            Assert.Equal(hd.RunSeconds * (long)Simulation.TickRate, site.PathEnd - site.PathStart);
            Assert.InRange(FixVec2.Distance(site.PathFrom, site.PathTo).FloorToInt(), hd.RunDistance.FloorToInt() - 1, hd.RunDistance.FloorToInt());
            Seconds(sim, hd.RunSeconds + 1);
            Assert.Equal(site.PathTo, site.Position); // stopped where it ran to, and runs no more
            Assert.Equal(site.PathTo, sys.SitePosition(site, sim.Tick + 6000));
        }
        Assert.True(ran > 0 && stayed > 0, $"ran {ran}, stayed {stayed}");
    }

    // ---------------------------------------------------------------- the alien probe

    [Fact]
    public void AnAlienProbeFollowsIsDecodedAndAnswered()
    {
        var sim = Game("meridian", CivSeed());
        var sys = sim.System!;
        Debug(sim, DebugSpaceField.SpawnSite, "alien_probe", -1);
        var probe = sys.Sites.Last();
        Assert.Equal(SiteKind.AlienProbe, probe.Kind);
        Assert.Equal(0, probe.Follow);
        var pr = Campaign.SpaceSites!.Kind(SiteKind.AlienProbe)!.Probe!;
        // It closes in to keep its distance from the ship (holding in orbit) and stops there.
        Seconds(sim, 20);
        Assert.True(FixVec2.Distance(probe.Position, sys.ShipPosition(Ship(sim), sim.Tick)) <= pr.Keep + Fix64.One);
        Ok(sim, new SiteScanCommand(0, Ship(sim).Id, probe.Id));
        Until(sim, () => sys.SiteKnowledgeOf(0, probe.Id) == SiteKnowledge.Identified, 60, "identified");

        // No Science crew, no decoding.
        FlyToSite(sim, probe.Id);
        Assert.Equal("decode the pattern first", sys.SiteWorkProblem(0, Ship(sim).Id, probe.Id, "answer"));
        var science = sys.StatusOf(0)!.Crew.First(c => c.Division == "science").OnDuty;
        Debug(sim, DebugSpaceField.Crew, "science", -science);
        Assert.Equal("no Science crew on duty", sys.SiteWorkProblem(0, Ship(sim).Id, probe.Id, "decode"));
        Debug(sim, DebugSpaceField.Crew, "science", science);

        // Decoding is kept when the ship leaves; the probe follows it away.
        Ok(sim, new SiteWorkCommand(0, Ship(sim).Id, probe.Id, "decode"));
        Seconds(sim, 20);
        var share = sys.SiteWork(0, probe.Id)!.Decode;
        Assert.True(share > Fix64.Zero && share < Fix64.One);
        var away = sys.Bodies.First(b => b.Id != Ship(sim).FromBody).Id;
        Ok(sim, new SetCourseCommand(0, Ship(sim).Id, away));
        Until(sim, () => Ship(sim).AtBody == away, 200, "away");
        Assert.Null(sys.WorkActionOf(0));
        Assert.Equal(share, sys.SiteWork(0, probe.Id)!.Decode);
        Seconds(sim, 60);
        FlyToSite(sim, probe.Id);
        Work(sim, probe.Id, "decode", () => sys.SiteWork(0, probe.Id)!.Decoded);

        // The answer: a reply event with no one leaving the ship; choices, then the outcome.
        Ok(sim, new SiteWorkCommand(0, Ship(sim).Id, probe.Id, "answer"));
        var reply = sys.AwayTeamOf(0)!;
        Assert.Empty(reply.Team);
        Assert.Equal(3, reply.Steps);
        Assert.Equal("nothing there to board", sys.AwayTeamProblem(0, probe.Id, null));
        while (sys.AwayTeamOf(0) is { Done: false } v && v.Step < v.Steps) Ok(sim, new AwayTeamChoiceCommand(0, probe.Id, 1));
        Until(sim, () => sys.AwayTeamOf(0) is { Done: true }, 120, "answer sent");
        var work = sys.SiteWork(0, probe.Id)!;
        Assert.NotEqual("", work.Answer);
        Assert.Equal(pr.Good.Contains(work.Answer), work.AnsweredWell);
        Assert.Equal(work.AnsweredWell, sys.ProbeAnsweredWell(0));
        Assert.Equal(SiteKnowledge.Spent, sys.SiteKnowledgeOf(0, probe.Id));
        Assert.Equal(-1, probe.Follow);
        Assert.Equal(sys.StatusOf(0)!.CrewStart, sys.StatusOf(0)!.CrewTotal); // no one hurt
    }

    [Fact]
    public void AGoodAnswerSoftensTheLaterFirstContact()
    {
        int Shock(bool answered)
        {
            var sim = Game("meridian", CivSeed());
            var sys = sim.System!;
            var civ = sys.Civilizations.First(c => c.Living);
            Do(sim, new DebugCivCommand(0, 0, civ.BodyId, DebugCivField.Level, Fix64.FromInt(5)));
            if (answered)
            {
                Debug(sim, DebugSpaceField.SpawnSite, "alien_probe", -1);
                var probe = sys.Sites.Last().Id;
                Debug(sim, DebugSpaceField.IdentifySite, probe, 0);
                FlyToSite(sim, probe);
                Work(sim, probe, "decode", () => sys.SiteWork(0, probe)!.Decoded);
                Ok(sim, new SiteWorkCommand(0, Ship(sim).Id, probe, "answer"));
                var understood = Campaign.ShipStatus!.Table("probe_reply")!.Outcomes.ToList().FindIndex(o => o.Id == "understood");
                Debug(sim, DebugSpaceField.FinishAwayTeam, null, understood);
                Assert.True(sys.ProbeAnsweredWell(0));
            }
            var ship = Ship(sim);
            Until(sim, () => !ship.InFlight, 300, "lands");
            if (ship.AtBody != civ.BodyId)
            {
                Ok(sim, new SetCourseCommand(0, ship.Id, civ.BodyId));
                Until(sim, () => ship.AtBody == civ.BodyId, 300, "at the civilization");
            }
            Do(sim, new DebugCivCommand(0, 0, civ.BodyId, DebugCivField.FinishStudy, Fix64.Zero));
            Until(sim, () => sys.FirstContactProblem(0, civ.BodyId) == null, 120, "contact possible");
            Ok(sim, new FirstContactCommand(0, civ.BodyId));
            return sys.ContactEventOf(0, civ.BodyId)!.ShockChance;
        }
        var c = Campaign.Civilizations!.Contact!;
        var plain = Shock(false);
        Assert.Equal(System.Math.Clamp(plain + Campaign.SpaceSites!.Kind(SiteKind.AlienProbe)!.Probe!.Shock, c.ShockMin, c.ShockMax), Shock(true));
    }

    // ---------------------------------------------------------------- lifepods, tows, rogues

    [Fact]
    public void TwelveLifepodsNeedHoldRoomSoTheArmstrongMakesTrips()
    {
        var fit = FitOutRules.Preset(Campaign, "armstrong", "builder")!;
        var sim = ChainAt("armstrong", "twelve_lifepods", fit: fit);
        var sys = sim.System!;
        var liner = MarkerSite(sim, "Lifepods");
        var station = MarkerSite(sim, "Set them down here");
        Assert.Equal(12, sys.Site(liner)!.PodCount);
        Assert.True(sys.Site(liner)!.HoldRoom);
        Assert.Equal(SiteKnowledge.Identified, sys.SiteKnowledgeOf(0, liner)); // known: the mission says what it is
        FlyToSite(sim, liner);
        // Room for a few: pick up until the hold is full.
        var problem = sys.SiteWorkProblem(0, Ship(sim).Id, liner, "pickup");
        while (problem != null && problem.Contains("no room"))
        {
            var line = sys.StatusOf(0)!.Hold.First(l => sys.HoldProblem(0, "jettison", l.Item, 1) == null);
            Ok(sim, new ShipHoldCommand(0, Ship(sim).Id, "jettison", line.Item, 1));
            problem = sys.SiteWorkProblem(0, Ship(sim).Id, liner, "pickup");
        }
        Ok(sim, new SiteWorkCommand(0, Ship(sim).Id, liner, "pickup"));
        Until(sim, () => sys.WorkActionOf(0) == null, 200, "the pick-up stops");
        var status = sys.StatusOf(0)!;
        var picked = sys.SiteWork(0, liner)!.Picked;
        Assert.True(picked > 0 && sys.PodsOf(liner).Any(p => p.State == "waiting"), "stopped with pods left");
        Assert.True(status.HoldUsed <= status.HoldSize, "never over the hold");
        Assert.Contains("no room", sys.SiteWorkProblem(0, Ship(sim).Id, liner, "pickup"));
        // Set them down at the station, come back: room again.
        FlyToSite(sim, station);
        var aboard = status.Hold.Where(l => l.Item.StartsWith(StarSystemState.SurvivorsPrefix, StringComparison.Ordinal)).Sum(l => l.Count);
        Ok(sim, new ShipHoldCommand(0, Ship(sim).Id, "drop", "survivors", aboard));
        FlyToSite(sim, liner);
        Assert.Null(sys.SiteWorkProblem(0, Ship(sim).Id, liner, "pickup"));
    }

    [Fact]
    public void ATowIsHandedOverAtItsBodyAndALostOneIsReplaced()
    {
        // Lost: alongside, then the reactor runs out before she is towed in: a fresh freighter, the mission goes on.
        var sim = ChainAt("armstrong", "tow_line");
        var sys = sim.System!;
        var hauler = MarkerSite(sim, "Disabled freighter");
        FlyToSite(sim, hauler);
        Work(sim, hauler, "alongside", () => sys.SiteWork(0, hauler)!.Alongside);
        Seconds(sim, Campaign.SpaceSites!.Kind(SiteKind.KnownDerelict)!.Reactor!.Seconds + 5);
        var view = sim.Missions.View(0, "tow_line")!;
        Assert.Equal(MissionState.Active, view.State);
        var fresh = MarkerSite(sim, "Disabled freighter");
        Assert.NotEqual(hauler, fresh);
        Assert.Contains(sim.Missions.Messages(0), m => m.Text.StartsWith("Tow Line: her reactor failed", StringComparison.Ordinal));

        // Delivered: towed into orbit of the port, handed over, gone.
        var port = sim.Missions.SpaceMarkers(0).First(m => m.Kind == "target").BodyId!;
        Assert.False(Campaign.SystemGenerator!.IsBelt(sys.Body(port)!.ClassId));
        FlyToSite(sim, fresh);
        Work(sim, fresh, "alongside", () => sys.SiteWork(0, fresh)!.Alongside);
        Work(sim, fresh, "tow", () => sys.TowOf(0) == fresh);
        var latinum = sim.Players[0].Stock[Campaign.Resource("latinum").Index];
        Ok(sim, new SetCourseCommand(0, Ship(sim).Id, port));
        for (var i = 0; i < 300 * Simulation.TickRate && sim.Missions.View(0, "tow_line")!.State != MissionState.Completed; i++) sim.Step();
        Assert.True(sim.Missions.View(0, "tow_line")!.State == MissionState.Completed,
            $"towed in: at {Ship(sim).AtBody} (port {port}), tow {sys.TowOf(0)}, {string.Join(" | ", sys.StatusOf(0)!.Log.TakeLast(12).Select(m => $"{m.Tick / 15}: {m.Text}"))}");
        Assert.Null(sys.TowOf(0));
        Assert.Equal(port, sys.SiteWork(0, fresh)!.Delivered);
        Assert.True(sim.Players[0].Stock[Campaign.Resource("latinum").Index] > latinum);
        Seconds(sim, 2);
        Assert.Null(sys.Site(fresh)); // handed over, gone
    }

    [Fact]
    public void TheRogueRockFliesAtTheBoundBodyAndAStrikePlacesAnother()
    {
        var sim = ChainAt("bastion", "watch_rogue_rock");
        var sys = sim.System!;
        var rock = MarkerSite(sim, "Rogue rock");
        var target = sim.Missions.SpaceMarkers(0).First(m => m.Kind == "target").BodyId!;
        Assert.Equal(target, sys.Site(rock)!.TargetBody);
        Assert.Equal(SiteKnowledge.Identified, sys.SiteKnowledgeOf(0, rock));
        Assert.False(Campaign.SystemGenerator!.IsBelt(sys.Body(target)!.ClassId) && sys.Civilizations.All(c => !c.Living || c.BodyId != target),
            $"{target} is {sys.Body(target)!.ClassId}: {string.Join(", ", sys.Bodies.Select(b => b.Id + "=" + b.ClassId))}");
        // Nobody stops it: it strikes, and the mission puts another on course.
        Until(sim, () => sys.Site(rock)?.Outcome == "impact", 600, "it strikes");
        Seconds(sim, 2);
        Assert.Equal(MissionState.Active, sim.Missions.View(0, "watch_rogue_rock")!.State);
        var next = MarkerSite(sim, "Rogue rock");
        Assert.NotEqual(rock, next);
        Assert.Equal("", sys.Site(next)!.Outcome);
    }

    [Fact]
    public void TheirLostShipCanBeStudiedInsteadOfTowedHome()
    {
        var seed = CivSeed();
        var system = SystemGenerator.Generate(Campaign, seed);
        var civ = CivGenerator.Roll(Campaign, system.Bodies, seed, MatchSetup.HomeBody(Campaign, system).Id).First(c => c.Living);
        var start = system.Bodies.First(b => b.Id != civ.BodyId).Id;
        var sim = ChainAt("meridian", "their_lost_ship", seed, start);
        var sys = sim.System!;
        var lost = MarkerSite(sim, "Their lost ship");
        Assert.True(sys.Site(lost)!.NoReactor);
        FlyToSite(sim, lost);
        Work(sim, lost, "alongside", () => sys.SiteWork(0, lost)!.Alongside);
        Seconds(sim, 2);
        Assert.Equal(1, sim.Missions.View(0, "their_lost_ship")!.StageIndex);
        Assert.Equal(new[] { "tow", "study" }, sim.Missions.View(0, "their_lost_ship")!.Options.Select(o => o.Id));
        Ok(sim, new MissionChoiceCommand(0, "their_lost_ship", "study"));
        Work(sim, lost, "restore_power", () => sys.SiteWork(0, lost)!.Powered);
        Work(sim, lost, "recover_logs", () => sys.SiteWork(0, lost)!.Logs);
        Until(sim, () => sim.Missions.View(0, "their_lost_ship")!.State == MissionState.Completed, 60, "studied");
        var work = sys.SiteWork(0, lost)!;
        Assert.True(work.Logs && !work.Towed && work.Delivered.Length == 0); // studied, not towed home
        Assert.Equal(-1, sys.Site(lost) is { } s && s.NoReactor ? -1 : 0); // still cold: no reactor clock ever started
        Assert.DoesNotContain(sys.StatusOf(0)!.Log, l => l.Text.Contains("reactor is failing", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public void Phase4RefusalsChangeNothing()
    {
        var a = Game("meridian", CivSeed());
        var b = Game("meridian", CivSeed());
        foreach (var g in new[] { a, b })
        {
            Debug(g, DebugSpaceField.SpawnSite, "alien_probe", -1);
            Debug(g, DebugSpaceField.SpawnSite, "hidden_ship", -1);
        }
        var probe = a.System!.Sites.First(s => s.Kind == SiteKind.AlienProbe).Id;
        var hidden = a.System.Sites.First(s => s.Kind == SiteKind.HiddenShip).Id;
        var derelict = a.System.Sites.First(s => s.Kind is SiteKind.Derelict or SiteKind.KnownDerelict or SiteKind.Debris).Id;
        var ship = Ship(a).Id;
        var bad = new Command[]
        {
            new SiteWorkCommand(0, ship, probe, "decode"), // not at it, not identified
            new SiteWorkCommand(0, ship, probe, "answer"),
            new SiteWorkCommand(0, ship, hidden, "inspect"), // unknown
            new SiteCourseCommand(0, ship, hidden),
            new SiteScanCommand(0, ship, hidden),
            new SiteWorkCommand(0, ship, derelict, "decode"),
            new AwayTeamCommand(0, ship, probe, new[] { 1, 1, 0, 0 }),
            new AwayTeamChoiceCommand(0, probe, 0),
        };
        foreach (var c in bad)
        {
            var refused = a.RefusedCommands;
            Do(a, c);
            Do(b, new LongRangeScanCommand(9, 99)); // b steps too (a command refused for a missing player)
            Assert.Equal(refused + 1, a.RefusedCommands);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
    }
}
