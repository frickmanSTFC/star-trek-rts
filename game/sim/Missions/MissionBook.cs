using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

[assembly: InternalsVisibleTo("FinalFrontier.Sim.Tests")]

namespace FinalFrontier.Sim.Missions;

/// <summary>Where a mission is (docs/missions-contract.md C).</summary>
public enum MissionState { Locked, Offered, Active, Completed, Failed, Declined }

/// <summary>Main = the story chain (auto-accepted, one at a time); Side = optional jobs (offered, accept / decline).</summary>
public enum MissionKind { Main, Side }

/// <summary>One objective of the current stage for the view: Text already filled ({unit}, {tile}… placeholders
/// replaced); Progress / Target for counters (Target 0 = a plain yes/no objective); Optional = bonus only; Detail = a live
/// line while it is unfinished (relay / covered objectives: what is out of coverage and how far; "" = none).</summary>
public readonly record struct ObjectiveView(string Id, string Text, bool Done, bool Optional, Fix64 Progress, Fix64 Target, string Detail = "");

/// <summary>A choice button (a branch option, or accept / decline on an offer).</summary>
public readonly record struct MissionOption(string Id, string Text);

/// <summary>A world marker a mission asks the view to draw: a ring of RadiusTiles round Tile (style "area", "target",
/// "site"), or on a unit (UnitId &gt; 0, style "unit").</summary>
public readonly record struct MissionMarker(string MissionId, string Style, int Tile, int RadiusTiles, int UnitId, string Label);

/// <summary>What the view needs to draw one mission.</summary>
public sealed record MissionView(
    string Id, string Title, MissionKind Kind, MissionState State, string Giver, string Briefing,
    string StageTitle, IReadOnlyList<ObjectiveView> Objectives, IReadOnlyList<MissionOption> Options,
    long TimerEndTick, long StartTick, long EndTick, string Debrief, IReadOnlyList<string> Rewards)
{
    /// <summary>The giver's name ("Flight Director Moreau"); Giver is its id.</summary>
    public string GiverName { get; init; } = "";

    /// <summary>A line for the tracker: the first unfinished objective's hint, else the stage's (filled), or "".</summary>
    public string Hint { get; init; } = "";

    /// <summary>An offer lapses at this tick (-1 when not offered).</summary>
    public long OfferEndTick { get; init; } = -1;

    /// <summary>The current stage, 0-based (-1 when not active), and how many stages the mission has.</summary>
    public int StageIndex { get; init; } = -1;

    public int StageCount { get; init; }

    /// <summary>"expired" when an offer lapsed (State Declined), "timeout" when a timer failed it, else "".</summary>
    public string Outcome { get; init; } = "";
}

/// <summary>A line of mission comms: Tick, Giver (id), GiverName, Text (filled), MissionId, Channel ("comms" = the
/// incoming-transmission card, "feed" = the event feed only).</summary>
public readonly record struct MissionMessage(long Tick, string Giver, string GiverName, string Text, string MissionId, string Channel);

/// <summary>A place on the map a mission put there (a probe wreck, a signal source, a photo point). Found once a tile
/// is surveyed or seen; Done once its objective is met. Kind = a model id the view draws ("probe_wreck",
/// "signal_source", "photo_point", "survey_marker").</summary>
public readonly record struct MissionSite(int Id, int Owner, string MissionId, string Kind, int Tile, bool Found, bool Done);

/// <summary>
/// Every player's campaign missions (sim state, hashed once enabled). Off by default: a game turns them on before tick 0
/// with <see cref="Enable"/> (a campaign start), or the test panel with DebugMissionCommand Enable. Stepped once a second
/// (Simulation.Step, after the surface), players in order, missions in file order; nothing runs for a player before
/// their outpost's uplink is online. Main missions of the chain start by themselves, one at a time, each once the earlier
/// ones have ended and its trigger holds; side jobs are offered when their trigger holds (while fewer than
/// rules.max_side_active are active) and lapse after offer_seconds. Choices arrive as MissionChoiceCommand. Randomness
/// (where sites and search areas go) hashes the map seed, player, mission and binding, never sim.Rng.
/// </summary>
public sealed partial class MissionBook
{
    private readonly Simulation? _sim;
    private readonly List<PlayerBook> _players = new();
    private readonly List<SiteRun> _sites = new();
    private int _nextSiteId = 1;

    public MissionBook()
    {
    }

    internal MissionBook(Simulation sim) => _sim = sim;

    /// <summary>On for a campaign game (set at set-up, before tick 0; off = nothing below ever changes).</summary>
    public bool Enabled { get; internal set; }

    /// <summary>The mission chain this game plays ("first_light"), "" while off.</summary>
    public string ChainId { get; private set; } = "";

    /// <summary>Where drone and comms facts come from (tests replace it; see MissionWorld).</summary>
    internal MissionWorld World { get; set; } = MissionWorld.Default;

    private GameData? Data => _sim?.Data;
    private MissionData? Defs => _sim?.Data.Missions;

    // ---------------------------------------------------------------- set-up

    /// <summary>Why missions can't be turned on with this chain (a chain id, or an era id = its first chain; "" = the
    /// first chain), or null.</summary>
    public string? EnableProblem(string chainOrEra)
    {
        if (_sim == null) return "no game";
        if (Enabled) return "missions are already on";
        if (Defs is not { } defs) return "this data set has no missions";
        return ResolveChain(defs, chainOrEra ?? "") == null ? $"no mission chain '{chainOrEra}'" : null;
    }

    /// <summary>
    /// Turns campaign missions on for this game with a chain id ("first_light") or an era id ("dawn_of_warp": its first
    /// chain). Call before the first Step (a campaign start: after the surface and the players are set up, before tick 0).
    /// Returns why it couldn't (missions stay off), or null. After tick 0 it is refused; the test panel uses
    /// DebugMissionCommand Enable instead.
    /// </summary>
    public string? Enable(string chainOrEra)
    {
        if (_sim != null && _sim.Tick > 0) return "the game has started (use the test panel's debug command)";
        return TurnOn(chainOrEra);
    }

    internal string? TurnOn(string chainOrEra)
    {
        if (EnableProblem(chainOrEra) is { } problem) return problem;
        ChainId = ResolveChain(Defs!, chainOrEra ?? "")!.Id;
        Enabled = true;
        return null;
    }

    private static MissionChainDef? ResolveChain(MissionData defs, string id)
    {
        if (id.Length == 0) return defs.Chains.Count > 0 ? defs.Chains[0] : null;
        foreach (var c in defs.Chains)
            if (c.Id == id) return c;
        foreach (var c in defs.Chains)
            if (c.Era == id) return c;
        return null;
    }

    // ---------------------------------------------------------------- state

    internal sealed class Binding
    {
        public string Name = "";
        public readonly List<int> Tiles = new();
        public readonly List<int> Sites = new();
        public int UnitId;
        public string Text = "";
        public string Res = "";

        public Binding Copy()
        {
            var b = new Binding { Name = Name, UnitId = UnitId, Text = Text, Res = Res };
            b.Tiles.AddRange(Tiles);
            b.Sites.AddRange(Sites);
            return b;
        }
    }

    internal sealed class MarkerRun
    {
        public string Style = "";
        public int Tile = -1;
        public int Radius;
        public int UnitId;
        public int SiteId;
        public string Label = "";
    }

    internal sealed class SiteRun
    {
        public int Id, Owner, Tile;
        public string MissionId = "", Kind = "";
        public bool Found, Done;
    }

    internal sealed class MissionRun
    {
        public readonly string Id;
        public MissionState State = MissionState.Locked;
        public int Stage = -1;
        public long StageStart = -1, Start = -1, End = -1, OfferEnd = -1, TimerEnd = -1;
        public Fix64[] Progress = System.Array.Empty<Fix64>();
        public bool[] Done = System.Array.Empty<bool>();
        public Fix64[] Base = System.Array.Empty<Fix64>();

        /// <summary>Tiles of a binding already visited by a drone, per objective: objective × 65536 + tile index in the binding.</summary>
        public readonly List<int> Visited = new();

        public readonly List<Binding> Bindings = new();
        public readonly List<MarkerRun> Markers = new();
        public bool Rewarded;
        public string Outcome = "";

        public MissionRun(string id) => Id = id;

        public Binding? Find(string name)
        {
            foreach (var b in Bindings)
                if (b.Name == name) return b;
            return null;
        }

        public void Bind(Binding b)
        {
            Bindings.RemoveAll(x => x.Name == b.Name);
            Bindings.Add(b);
        }
    }

    internal sealed class PlayerBook
    {
        public readonly List<MissionRun> Runs = new();
        public readonly List<string> Flags = new(); // sorted
        public readonly List<MissionMessage> Messages = new();
    }

    /// <summary>A player's book (made on first use: the chain's missions available in this data set, in file order).</summary>
    private PlayerBook Track(int player)
    {
        while (_players.Count <= player)
        {
            var book = new PlayerBook();
            if (Defs is { } defs)
                foreach (var m in defs.List)
                    if (m.Chain == ChainId && Data!.MissionAvailable(m)) book.Runs.Add(new MissionRun(m.Id));
            _players.Add(book);
        }
        return _players[player];
    }

    private MissionDef? Def(MissionRun run) => Defs?.Find(run.Id);

    private MissionRun? RunOf(int player, string id)
    {
        if (player < 0 || player >= _players.Count) return null;
        foreach (var r in _players[player].Runs)
            if (r.Id == id) return r;
        return null;
    }

    private int ActiveSides(int player)
    {
        var n = 0;
        foreach (var r in Track(player).Runs)
            if (r.State == MissionState.Active && Def(r) is { IsMain: false }) n++;
        return n;
    }

    private long Now => _sim!.Tick;

    // ---------------------------------------------------------------- step

    /// <summary>Once a second (Simulation.Step, after Surface.Step).</summary>
    internal void Step()
    {
        if (!Enabled || _sim?.Surface is not { } s || Defs is not { } defs) return;
        for (var p = 0; p < _sim.Players.Count && p < s.Players.Count; p++)
        {
            var book = Track(p);
            if (!s.UplinkOnline(p, Now)) continue;
            UpdateSites(s, p);
            // Offers that lapsed.
            foreach (var run in book.Runs)
                if (run.State == MissionState.Offered && Now >= run.OfferEnd)
                {
                    run.State = MissionState.Declined;
                    run.Outcome = "expired";
                    run.End = Now;
                    if (Def(run) is { } d) Say(p, run, d.Giver, $"Offer lapsed: {d.Title}.", "feed");
                }
            StartMain(p, book);
            // Side jobs whose trigger holds now.
            foreach (var run in book.Runs)
            {
                if (run.State != MissionState.Locked || Def(run) is not { IsMain: false } d) continue;
                if (ActiveSides(p) >= defs.Rules.MaxSideActive) break;
                var bindings = new List<Binding>();
                if (d.Trigger != null && !Check(p, null, -1, d.Trigger, bindings).Met) continue;
                Offer(p, run, d, bindings);
            }
            // Active missions, in file order (a list copy: actions may start other missions).
            foreach (var run in book.Runs.ToList())
                if (run.State == MissionState.Active) StepActive(p, run);
        }
    }

    /// <summary>The next main mission of the chain starts when every earlier one has ended and its trigger holds.</summary>
    private void StartMain(int p, PlayerBook book)
    {
        foreach (var run in book.Runs)
            if (run.State is MissionState.Active or MissionState.Offered && Def(run) is { IsMain: true }) return; // one at a time
        foreach (var run in book.Runs)
        {
            if (Def(run) is not { IsMain: true } d || run.State != MissionState.Locked) continue;
            var bindings = new List<Binding>();
            if (d.Trigger == null || Check(p, null, -1, d.Trigger, bindings).Met) Activate(p, run, d, bindings, brief: true);
            return;
        }
    }

    /// <summary>Bindings the mission takes over from earlier ones (MissionDef.Imports), then the trigger's.</summary>
    private void BindAll(int p, MissionRun run, MissionDef d, List<Binding> bindings)
    {
        foreach (var (from, name) in d.Imports)
            if (RunOf(p, from)?.Find(name) is { } b) run.Bind(b.Copy());
        foreach (var b in bindings) run.Bind(b);
    }

    private void Offer(int p, MissionRun run, MissionDef d, List<Binding> bindings)
    {
        run.State = MissionState.Offered;
        BindAll(p, run, d, bindings);
        var seconds = d.OfferSeconds > 0 ? d.OfferSeconds : Defs!.Rules.OfferSeconds;
        run.OfferEnd = Now + (long)seconds * Simulation.TickRate;
        Say(p, run, d.Giver, Fill(p, run, d.Briefing), "comms");
    }

    private void Activate(int p, MissionRun run, MissionDef d, List<Binding> bindings, bool brief)
    {
        run.State = MissionState.Active;
        run.Start = Now;
        run.OfferEnd = -1;
        BindAll(p, run, d, bindings);
        if (brief) Say(p, run, d.Giver, Fill(p, run, d.Briefing), "comms");
        EnterStage(p, run, d, 0);
    }

    private void EnterStage(int p, MissionRun run, MissionDef d, int index)
    {
        var stage = d.Stages[index];
        run.Stage = index;
        run.StageStart = Now;
        run.Progress = new Fix64[stage.Objectives.Count];
        run.Done = new bool[stage.Objectives.Count];
        run.Base = new Fix64[stage.Objectives.Count];
        run.Visited.Clear();
        run.Markers.Clear();
        run.TimerEnd = stage.TimerSeconds > 0 ? Now + (long)stage.TimerSeconds * Simulation.TickRate : -1;
        if (index > 0) Say(p, run, d.Giver, $"{d.Title}: {stage.Title}.", "feed");
        foreach (var a in stage.OnEnter)
        {
            Act(p, run, d, a);
            if (run.State != MissionState.Active || run.Stage != index) return; // the action ended or moved the mission
        }
        for (var k = 0; k < stage.Objectives.Count; k++) run.Base[k] = Baseline(p, run, stage.Objectives[k].Cond);
    }

    private void StepActive(int p, MissionRun run)
    {
        if (Def(run) is not { } d || run.Stage < 0 || run.Stage >= d.Stages.Count) return;
        var stage = d.Stages[run.Stage];
        var all = true;
        for (var k = 0; k < stage.Objectives.Count && k < run.Done.Length; k++)
        {
            var o = stage.Objectives[k];
            if (!run.Done[k])
            {
                var e = Check(p, run, k, o.Cond, null);
                if (o.HoldSeconds > 0)
                {
                    run.Progress[k] = e.Met ? Fix64.Min(Fix64.FromInt(o.HoldSeconds), run.Progress[k] + Fix64.One) : Fix64.Zero;
                    run.Done[k] = run.Progress[k] >= Fix64.FromInt(o.HoldSeconds);
                }
                else
                {
                    if (e.Target > Fix64.Zero) run.Progress[k] = Fix64.Max(run.Progress[k], Fix64.Min(e.Progress, e.Target));
                    run.Done[k] = e.Met;
                }
                if (run.Done[k]) MarkSitesDone(run, o.Cond);
            }
            if (!run.Done[k] && !o.Optional) all = false;
        }
        if (stage.Objectives.Count > 0 && all)
        {
            Goto(p, run, d, stage.Next);
            return;
        }
        if (run.TimerEnd >= 0 && Now >= run.TimerEnd)
        {
            Say(p, run, d.Giver, $"{d.Title}: out of time.", "feed");
            if (stage.OnTimeout == GameData.MissionFail) run.Outcome = "timeout";
            Goto(p, run, d, stage.OnTimeout);
        }
    }

    private void Goto(int p, MissionRun run, MissionDef d, string target)
    {
        if (target == GameData.MissionComplete) Complete(p, run, d);
        else if (target == GameData.MissionFail) Fail(p, run, d);
        else if (d.StageIndex(target) is var i and >= 0) EnterStage(p, run, d, i);
    }

    private void Complete(int p, MissionRun run, MissionDef d)
    {
        run.State = MissionState.Completed;
        run.End = Now;
        run.TimerEnd = -1;
        run.Markers.Clear();
        var paid = new List<string>();
        if (!run.Rewarded)
        {
            run.Rewarded = true;
            foreach (var a in d.Rewards)
                if (Reward(p, run, a) is { Length: > 0 } line) paid.Add(line);
        }
        var debrief = Fill(p, run, d.Debrief);
        Say(p, run, d.Giver, paid.Count > 0 ? $"{debrief} ({string.Join(", ", paid)})" : debrief, "comms");
    }

    private void Fail(int p, MissionRun run, MissionDef d)
    {
        run.State = MissionState.Failed;
        run.End = Now;
        run.TimerEnd = -1;
        run.Markers.Clear();
        Say(p, run, d.Giver, $"Mission failed: {d.Title}.", "comms");
    }

    // ---------------------------------------------------------------- commands

    /// <summary>Why the choice would be refused now, or null.</summary>
    public string? ChoiceProblem(MissionChoiceCommand c)
    {
        if (!Enabled || _sim == null) return "missions are off";
        if (c.Player < 0 || c.Player >= _sim.Players.Count) return "no such player";
        Track(c.Player);
        if (RunOf(c.Player, c.MissionId) is not { } run || Def(run) is not { } d) return $"no mission '{c.MissionId}'";
        switch (c.Option)
        {
            case "accept":
                if (run.State != MissionState.Offered) return "not offered";
                return ActiveSides(c.Player) >= Defs!.Rules.MaxSideActive ? $"{Defs.Rules.MaxSideActive} side jobs are active already" : null;
            case "decline":
                return run.State == MissionState.Offered ? null : "not offered";
            default:
                if (run.State != MissionState.Active || run.Stage < 0 || run.Stage >= d.Stages.Count) return "not active";
                foreach (var ch in d.Stages[run.Stage].Choices)
                    if (ch.Id == c.Option) return null;
                return $"no option '{c.Option}' now";
        }
    }

    /// <summary>MissionChoiceCommand: accept / decline an offer, or pick a branch of the current stage.</summary>
    internal void Choose(MissionChoiceCommand c)
    {
        if (ChoiceProblem(c) is { } problem)
        {
            _sim?.Refuse(c, problem);
            return;
        }
        var run = RunOf(c.Player, c.MissionId)!;
        var d = Def(run)!;
        switch (c.Option)
        {
            case "accept":
                Activate(c.Player, run, d, new List<Binding>(), brief: false);
                break;
            case "decline":
                run.State = MissionState.Declined;
                run.End = Now;
                Say(c.Player, run, d.Giver, $"Declined: {d.Title}.", "feed");
                break;
            default:
                foreach (var ch in d.Stages[run.Stage].Choices)
                    if (ch.Id == c.Option)
                    {
                        Goto(c.Player, run, d, ch.Next);
                        break;
                    }
                break;
        }
    }

    // ---------------------------------------------------------------- messages, sites

    private void Say(int p, MissionRun run, string giver, string text, string channel)
    {
        var book = Track(p);
        var name = Defs?.Giver(giver)?.Name ?? giver;
        book.Messages.Add(new MissionMessage(Now, giver, name, text, run.Id, channel));
        var max = System.Math.Max(1, Defs?.Rules.MessageLog ?? 60);
        if (book.Messages.Count > max) book.Messages.RemoveRange(0, book.Messages.Count - max);
    }

    /// <summary>A site is found once its owner has surveyed its tile or has it in sight.</summary>
    private void UpdateSites(PlanetSurface s, int p)
    {
        foreach (var site in _sites)
            if (site.Owner == p && !site.Found && site.Tile >= 0 && site.Tile < s.Map.Grid.Count
                && (s.Players[p].Surveyed[site.Tile] || s.IsVisible(p, site.Tile)))
                site.Found = true;
    }

    private SiteRun? Site(int id)
    {
        foreach (var s in _sites)
            if (s.Id == id) return s;
        return null;
    }

    /// <summary>An objective that asked for a site to be reached or visited is done: so is the site.</summary>
    private void MarkSitesDone(MissionRun run, MissionCondDef c)
    {
        if (c.Kind == "site" && c.F("site") is { } name && run.Find(name) is { } b)
            foreach (var id in b.Sites)
                if (Site(id) is { } site) site.Done = true;
        if (c.Kind == "drone_at" && c.F("at") is { } at && run.Find(at) is { } db)
            foreach (var id in db.Sites)
                if (Site(id) is { } site) site.Done = true;
        foreach (var child in c.Children) MarkSitesDone(run, child);
    }

    // ---------------------------------------------------------------- views

    /// <summary>This player's missions that are not Locked, main chain first then sides, each in file order.</summary>
    public IReadOnlyList<MissionView> For(int player)
    {
        if (!Enabled || player < 0 || _sim == null || player >= _players.Count) return System.Array.Empty<MissionView>();
        var list = new List<MissionView>();
        foreach (var main in new[] { true, false })
            foreach (var run in _players[player].Runs)
                if (run.State != MissionState.Locked && Def(run) is { } d && d.IsMain == main) list.Add(View(player, run, d));
        return list;
    }

    /// <summary>One mission's view (also Locked ones, for the test panel), or null.</summary>
    public MissionView? View(int player, string missionId) =>
        Enabled && RunOf(player, missionId) is { } run && Def(run) is { } d ? View(player, run, d) : null;

    /// <summary>The ids of every mission of the player's chain (file order) and its state: the test panel's list.</summary>
    public IReadOnlyList<(string Id, MissionState State)> States(int player) =>
        Enabled && player >= 0 && player < _players.Count
            ? _players[player].Runs.Select(r => (r.Id, r.State)).ToList()
            : new List<(string, MissionState)>();

    private MissionView View(int p, MissionRun run, MissionDef d)
    {
        var stageIndex = run.State == MissionState.Active ? run.Stage : run.State == MissionState.Offered ? 0 : -1;
        var stage = stageIndex >= 0 && stageIndex < d.Stages.Count ? d.Stages[stageIndex] : null;
        var objectives = new List<ObjectiveView>();
        var options = new List<MissionOption>();
        var hint = "";
        if (stage != null)
        {
            for (var k = 0; k < stage.Objectives.Count; k++)
            {
                var o = stage.Objectives[k];
                var active = run.State == MissionState.Active && k < run.Done.Length;
                var done = active && run.Done[k];
                var target = o.HoldSeconds > 0 ? Fix64.FromInt(o.HoldSeconds) : Target(p, run, o.Cond);
                var progress = active ? (done && target > Fix64.Zero ? target : run.Progress[k]) : Fix64.Zero;
                var detail = active && !done ? Detail(p, run, o.Cond) : ""; // MissionConditions.cs
                objectives.Add(new ObjectiveView(o.Id, Fill(p, run, o.Text), done, o.Optional, progress, target, detail));
                if (!done && hint.Length == 0 && o.Hint.Length > 0) hint = Fill(p, run, o.Hint);
            }
            if (hint.Length == 0) hint = Fill(p, run, stage.Hint);
            if (run.State == MissionState.Offered)
            {
                options.Add(new MissionOption("accept", "Accept"));
                options.Add(new MissionOption("decline", "Decline"));
            }
            else
                foreach (var c in stage.Choices) options.Add(new MissionOption(c.Id, Fill(p, run, c.Text)));
        }
        return new MissionView(d.Id, d.Title, d.IsMain ? MissionKind.Main : MissionKind.Side, run.State, d.Giver, Fill(p, run, d.Briefing),
            stage != null && run.State == MissionState.Active ? stage.Title : "", objectives, options, run.TimerEnd, run.Start, run.End,
            Fill(p, run, d.Debrief), d.Rewards.Select(a => RewardText(p, run, a)).Where(t => t.Length > 0).ToList())
        {
            GiverName = Defs?.Giver(d.Giver)?.Name ?? d.Giver,
            Hint = hint,
            OfferEndTick = run.State == MissionState.Offered ? run.OfferEnd : -1,
            StageIndex = run.State == MissionState.Active ? run.Stage : -1,
            StageCount = d.Stages.Count,
            Outcome = run.Outcome,
        };
    }

    /// <summary>This player's mission comms, oldest first (bounded log).</summary>
    public IReadOnlyList<MissionMessage> Messages(int player) =>
        Enabled && player >= 0 && player < _players.Count ? _players[player].Messages.ToList() : System.Array.Empty<MissionMessage>();

    /// <summary>Markers of this player's offered / active missions.</summary>
    public IReadOnlyList<MissionMarker> Markers(int player)
    {
        if (!Enabled || player < 0 || player >= _players.Count || _sim?.Surface is not { } s) return System.Array.Empty<MissionMarker>();
        var list = new List<MissionMarker>();
        foreach (var run in _players[player].Runs)
        {
            if (run.State is not (MissionState.Active or MissionState.Offered)) continue;
            foreach (var m in run.Markers)
            {
                if (m.SiteId > 0 && Site(m.SiteId) is { Done: true }) continue;
                var tile = m.Tile;
                if (m.UnitId > 0)
                {
                    if (s.FindUnit(m.UnitId) is not { } u) continue;
                    tile = u.Tile;
                }
                list.Add(new MissionMarker(run.Id, m.Style, tile, m.Radius, m.UnitId, m.Label));
            }
        }
        return list;
    }

    /// <summary>Mission sites of this player (found or not: the view shows only found ones, or ones in sight).</summary>
    public IReadOnlyList<MissionSite> Sites(int player)
    {
        if (!Enabled) return System.Array.Empty<MissionSite>();
        var list = new List<MissionSite>();
        foreach (var s in _sites)
            if (s.Owner == player) list.Add(new MissionSite(s.Id, s.Owner, s.MissionId, s.Kind, s.Tile, s.Found, s.Done));
        return list;
    }

    /// <summary>Flags the player's missions have set ("earth_link"), sorted.</summary>
    public IReadOnlyList<string> Flags(int player) =>
        Enabled && player >= 0 && player < _players.Count ? _players[player].Flags.ToList() : new List<string>();

    // ---------------------------------------------------------------- hash

    /// <summary>Everything above, only while enabled (a game with missions off keeps the hash it always had).</summary>
    internal void AddToHash(ref StateHasher h)
    {
        if (!Enabled) return;
        h.Add("missions");
        h.Add(ChainId);
        h.Add(_nextSiteId);
        h.Add(_sites.Count);
        foreach (var s in _sites)
        {
            h.Add(s.Id);
            h.Add(s.Owner);
            h.Add(s.Tile);
            h.Add(s.MissionId);
            h.Add(s.Kind);
            h.Add(s.Found);
            h.Add(s.Done);
        }
        h.Add(_players.Count);
        foreach (var p in _players)
        {
            h.Add(p.Flags.Count);
            foreach (var f in p.Flags) h.Add(f);
            h.Add(p.Messages.Count);
            foreach (var m in p.Messages)
            {
                h.Add(m.Tick);
                h.Add(m.Giver);
                h.Add(m.Text);
                h.Add(m.MissionId);
                h.Add(m.Channel);
            }
            h.Add(p.Runs.Count);
            foreach (var r in p.Runs)
            {
                h.Add(r.Id);
                h.Add((int)r.State);
                h.Add(r.Stage);
                h.Add(r.StageStart);
                h.Add(r.Start);
                h.Add(r.End);
                h.Add(r.OfferEnd);
                h.Add(r.TimerEnd);
                h.Add(r.Rewarded);
                h.Add(r.Outcome);
                h.Add(r.Progress.Length);
                for (var k = 0; k < r.Progress.Length; k++)
                {
                    h.Add(r.Progress[k]);
                    h.Add(r.Done[k]);
                    h.Add(r.Base[k]);
                }
                h.Add(r.Visited.Count);
                foreach (var v in r.Visited) h.Add(v);
                h.Add(r.Bindings.Count);
                foreach (var b in r.Bindings)
                {
                    h.Add(b.Name);
                    h.Add(b.UnitId);
                    h.Add(b.Text);
                    h.Add(b.Res);
                    h.Add(b.Tiles.Count);
                    foreach (var t in b.Tiles) h.Add(t);
                    h.Add(b.Sites.Count);
                    foreach (var t in b.Sites) h.Add(t);
                }
                h.Add(r.Markers.Count);
                foreach (var m in r.Markers)
                {
                    h.Add(m.Style);
                    h.Add(m.Tile);
                    h.Add(m.Radius);
                    h.Add(m.UnitId);
                    h.Add(m.SiteId);
                    h.Add(m.Label);
                }
            }
        }
    }
}
