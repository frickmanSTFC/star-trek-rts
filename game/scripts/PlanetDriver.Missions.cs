using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Missions on the HUD (docs/missions-contract.md C "UI"; render-only + MissionChoiceCommand): the objective tracker
// (MissionTracker, top right under the resource bar on the surface, top right of the helm's open viewscreen in orbit),
// incoming transmissions (a pulsing banner on the tracker + an event-feed line; Y or a click opens the comms card,
// MissionComms, with ACCEPT / DECLINE / branch choices), debrief cards on completion (the chapter's end gets the large
// one, opened by itself), and the mission log (N, MissionLog). Everything shown comes from Simulation.Missions
// (MissionBook: For / Messages / Markers / Sites); the only command is MissionChoiceCommand. While the tutorial runs
// the tracker stays hidden (transmissions wait in the queue; the feed lines still show). Campaign starts turn missions
// on (GameSetup.MissionChain) before tick 0: see BeginMissions (merge seam). Also calls the drone and relay UI's
// per-frame updates (PlanetDriver.Drones.cs, PlanetDriver.Relays.cs) so the main driver needs one line.
public partial class PlanetDriver
{
    /// <summary>One thing the comms card can show. Kind "message" (a comms-channel transmission), "offer" (a side job
    /// waiting for an answer), "briefing" (re-read from the tracker / log), "debrief" (completed or failed).</summary>
    private sealed record CommsItem(string Key, string Kind, string MissionId, MissionMessage? Message, bool Large = false);

    private static readonly Color MissionGold = new(0.98f, 0.8f, 0.4f);
    private static readonly Color MissionSide = new(0.45f, 0.86f, 0.8f);
    private static readonly Color MissionOffer = new(0.75f, 0.62f, 1f);
    private static readonly Color MissionBad = new(1f, 0.45f, 0.4f);

    private MissionTracker _tracker = null!;
    private MissionComms _commsCard = null!;
    private MissionLog _missionLog = null!;
    private bool _trackerCollapsed;
    private IReadOnlyList<MissionView> _missions = System.Array.Empty<MissionView>();
    private IReadOnlyList<MissionMarker> _missionMarkers = System.Array.Empty<MissionMarker>();
    private IReadOnlyList<MissionSite> _missionSites = System.Array.Empty<MissionSite>();
    private List<TrackerEntry> _trackerEntries = new();
    private double _missionsAt = -1;
    private readonly Dictionary<string, (MissionState State, string Stage)> _missionSeen = new();
    private readonly HashSet<string> _messagesSeen = new();
    private readonly Dictionary<string, string> _giverNames = new();
    private readonly List<CommsItem> _commsQueue = new(); // unread, oldest first
    private CommsItem? _commsShown;
    private CommsItem? _commsLast;
    private int _commsSerial;
    private string? _chapterCheck;
    private double _chapterCheckAt;
    private bool _missionsWarned;

    /// <summary>The contract's placeholder givers, for a name before any of their messages has arrived.</summary>
    private static readonly Dictionary<string, string> KnownGivers = new()
    {
        ["control"] = "Flight Director Moreau",
        ["ops"] = "Chief Okafor",
        ["science"] = "Dr. Reyes",
    };

    /// <summary>In _Ready (after SetupCiv): the three panels in the HUD's overlay layer (above the helm in orbit, under
    /// F1 help and the tooltip), then missions on for a campaign start.</summary>
    private void SetupMissions()
    {
        _tracker = new MissionTracker { Name = "MissionTracker" };
        _hud.AddOverlay(_tracker);
        _missionLog = new MissionLog { Name = "MissionLog" };
        _hud.AddOverlay(_missionLog);
        _commsCard = new MissionComms { Name = "MissionComms" };
        _hud.AddOverlay(_commsCard);
        _tracker.EntryPicked += TrackerPicked;
        _tracker.BannerPicked += () => OpenNextComms(true);
        _tracker.CollapseToggled += () => _trackerCollapsed = !_trackerCollapsed;
        _tracker.LogPicked += ToggleMissionLog;
        _commsCard.ButtonPicked += CommsButtonPicked;
        _missionLog.Locate += id => LocateMission(id);
        BeginMissions();
    }

    /// <summary>Campaign start: missions on before tick 0 (the sim hashes them only once enabled).</summary>
    private void BeginMissions()
    {
        if (GameSetup.MissionChain is not { } chain || _sim.Missions.Enabled) return;
        if (_sim.Missions.Enable(chain) is { } problem) GD.PushWarning($"Missions not started: {problem}");
    }

    /// <summary>The mouse is over a mission panel (counts as UI for the map and the globe).</summary>
    private bool MissionsBlockMouse(Vector2 mouse) =>
        _tracker.IsOverUi(mouse) || _commsCard.IsOverUi(mouse) || _missionLog.IsOverUi(mouse) || DroneUiBlocksMouse(mouse);

    /// <summary>N, Y and Esc for the mission panels (surface and orbit). True when the key was used.</summary>
    private bool MissionInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return false;
        switch (key.Keycode)
        {
            case Key.Escape when _commsCard.IsOpen:
                Sfx.Ui("back");
                _commsShown = null;
                return true;
            case Key.Escape when _missionLog.IsOpen:
                _missionLog.Close();
                return true;
            case Key.N:
                ToggleMissionLog();
                return true;
            case Key.Y:
                OpenNextComms(false);
                return true;
        }
        return false;
    }

    private void ToggleMissionLog()
    {
        if (_missionLog.IsOpen) _missionLog.Close();
        else _missionLog.Open();
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Once a frame at the end of _Process: drones, relays, then missions (events, tracker, card, log).</summary>
    private void UpdateMissionUi(double delta)
    {
        UpdateDrones(); // PlanetDriver.Drones.cs
        UpdateRelays(); // PlanetDriver.Relays.cs
        try
        {
            UpdateMissions();
        }
        catch (System.Exception ex) // a stub or a half-merged sim must never break the frame: warn once
        {
            if (!_missionsWarned) GD.PushWarning($"Missions UI: {ex.Message}");
            _missionsWarned = true;
        }
    }

    private void UpdateMissions()
    {
        var book = _sim.Missions;
        if (_missionsAt < 0 || _clock - _missionsAt >= 0.25)
        {
            _missionsAt = _clock;
            _missions = book.For(LocalPlayer);
            _missionMarkers = book.Markers(LocalPlayer);
            _missionSites = book.Sites(LocalPlayer);
            MissionEvents(book);
            _trackerEntries = TrackerEntries(); // timers tick at this rate too
        }
        if (_chapterCheck != null && _clock >= _chapterCheckAt) ChapterCheck();

        var screen = GetViewport().GetVisibleRect().Size;
        var scale = HudKit.ScaleFor(screen);
        var blocked = _bigCard.IsOpen || _inventory.IsOpen || _achScreen.IsOpen || _dossier.IsOpen || _chooser.IsOpen;
        var testWidth = !_inOrbit && _test != null && _test.Visible ? TestPanel.Width + 16f : 0f;
        Rect2 area = _inOrbit && _helm.OpenRect.Size.X > 64f
            ? _helm.OpenRect
            : new Rect2(0, PlanetHud.TopHeight + 8, screen.X - testWidth, Mathf.Max(120f, screen.Y - PlanetHud.TopHeight - PlanetHud.BottomHeight - 16));

        // Tracker: hidden while the tutorial runs or a full-screen card is up; in orbit only while the SATELLITES window
        // (same corner) is closed.
        var entries = _trackerEntries;
        var banner = TrackerBannerView();
        var tutorialRunning = _tutorial != null;
        var hideTracker = blocked || tutorialRunning || _missionLog.IsOpen || (_inOrbit && _satPanelOpen);
        if (hideTracker) _tracker.Refresh(null, null, Vector2.Zero, Vector2.Zero, 0, scale, false, "");
        else
        {
            Vector2 anchor;
            float maxHeight;
            if (_inOrbit)
            {
                anchor = new Vector2(area.End.X - 12f * scale, area.Position.Y + 12f * scale);
                maxHeight = area.Size.Y * 0.62f;
            }
            else
            {
                anchor = new Vector2(screen.X - 16f - testWidth, PlanetHud.TopHeight + 36f);
                maxHeight = screen.Y - PlanetHud.TopHeight - PlanetHud.BottomHeight - 60f;
            }
            var chip = $"MISSIONS · {entries.Count}" + (banner != null ? "  ·  NEW" : "");
            _tracker.Refresh(entries, banner, anchor, new Vector2(1f, 0f), maxHeight, scale, _trackerCollapsed, chip);
        }

        // Comms card.
        if (_commsShown != null && !CommsStillValid(_commsShown)) _commsShown = null;
        _commsCard.Refresh(_commsShown != null && !blocked ? CommsCard(_commsShown) : null, area, scale);

        // Log.
        if (_missionLog.IsOpen && blocked) _missionLog.Close();
        if (_missionLog.IsOpen) _missionLog.Refresh(LogEntries(), LogDetail(_missionLog.Picked), area, scale);
    }

    // ---------------------------------------------------------------- events

    private static string MessageKey(MissionMessage m) => $"{m.Tick}|{m.MissionId}|{m.Giver}|{m.Text}";

    /// <summary>New messages and mission state changes → event-feed lines and comms items.</summary>
    private void MissionEvents(MissionBook book)
    {
        foreach (var m in book.Messages(LocalPlayer))
        {
            if (!_messagesSeen.Add(MessageKey(m))) continue;
            if (m.GiverName.Length > 0) _giverNames[m.Giver] = m.GiverName;
            var who = m.GiverName.Length > 0 ? m.GiverName : GiverName(m.Giver);
            if (m.Channel == "comms")
            {
                Enqueue(new CommsItem($"msg:{++_commsSerial}", "message", m.MissionId, m));
                _hud.PushEvent($"Incoming transmission · {who}  ·  Y to read", MissionGold);
                Sfx.Ui("tutorial", -6f);
            }
            else _hud.PushEvent($"{who}: {m.Text}", MissionGold);
        }

        foreach (var v in _missions)
        {
            var now = (v.State, v.StageTitle);
            var known = _missionSeen.TryGetValue(v.Id, out var was);
            _missionSeen[v.Id] = now;
            if (known && was == now) continue;
            var title = v.Title;
            if (!known)
            {
                if (v.State == MissionState.Offered)
                {
                    if (!HasQueued(v.Id)) Enqueue(new CommsItem($"offer:{v.Id}", "offer", v.Id, null));
                    _hud.PushEvent($"Side job offered: {title}{OfferTimeText(v)}  ·  Y to read", MissionOffer);
                }
                else if (v.State == MissionState.Active)
                    _hud.PushEvent(v.Kind == MissionKind.Main ? $"New mission: {title}" : $"Side job under way: {title}", v.Kind == MissionKind.Main ? MissionGold : MissionSide);
                continue;
            }
            if (was.State != v.State)
            {
                switch (v.State)
                {
                    case MissionState.Active when was.State == MissionState.Offered:
                        _hud.PushEvent($"Side job accepted: {title}", MissionSide);
                        break;
                    case MissionState.Active:
                        _hud.PushEvent($"Mission under way: {title}", MissionGold);
                        break;
                    case MissionState.Offered:
                        if (!HasQueued(v.Id)) Enqueue(new CommsItem($"offer:{v.Id}", "offer", v.Id, null));
                        _hud.PushEvent($"Side job offered: {title}{OfferTimeText(v)}  ·  Y to read", MissionOffer);
                        break;
                    case MissionState.Completed:
                        Enqueue(new CommsItem($"debrief:{v.Id}", "debrief", v.Id, null));
                        _hud.PushEvent($"Mission complete: {title}" + (v.Rewards.Count > 0 ? $"  ·  {string.Join(", ", v.Rewards)}" : "") + "  ·  Y debrief", MissionGold);
                        Sfx.Ui("tutorial_done");
                        if (v.Kind == MissionKind.Main)
                        {
                            _chapterCheck = v.Id; // the chain's last main mission → the chapter debrief
                            _chapterCheckAt = _clock + 3.0;
                        }
                        break;
                    case MissionState.Failed:
                        Enqueue(new CommsItem($"debrief:{v.Id}", "debrief", v.Id, null));
                        _hud.PushEvent($"Mission failed: {title}  ·  the reward is lost", MissionBad);
                        break;
                    case MissionState.Declined:
                        _hud.PushEvent(was.State == MissionState.Offered && v.TimerEndTick >= 0 && v.TimerEndTick <= _sim.Tick
                            ? $"Offer expired: {title}" : $"Side job declined: {title}", new Color(0.6f, 0.65f, 0.75f));
                        break;
                }
            }
            else if (v.State == MissionState.Active && v.StageTitle.Length > 0)
                _hud.PushEvent($"{title}: next — {v.StageTitle}", v.Kind == MissionKind.Main ? MissionGold : MissionSide);
        }
        // Offers that are no longer offers leave the queue (answered elsewhere, or expired).
        _commsQueue.RemoveAll(i => i.Kind == "offer" && MissionById(i.MissionId)?.State != MissionState.Offered);
    }

    /// <summary>A main mission completed a few seconds ago and nothing main followed: the chapter is over.</summary>
    private void ChapterCheck()
    {
        var id = _chapterCheck!;
        _chapterCheck = null;
        if (_missions.Any(m => m.Kind == MissionKind.Main && m.State is MissionState.Active or MissionState.Offered)) return;
        if (MissionById(id) is not { State: MissionState.Completed }) return;
        _commsQueue.RemoveAll(i => i.Kind == "debrief" && i.MissionId == id);
        var item = new CommsItem($"chapter:{id}", "debrief", id, null, Large: true);
        _hud.PushEvent("Chapter complete: the debrief is in  ·  Y", MissionGold);
        // The chapter's end is the one card that opens by itself (unless the tutorial or another card is up).
        if (_tutorial == null && _commsShown == null) Show(item);
        else Enqueue(item);
    }

    private void Enqueue(CommsItem item)
    {
        _commsQueue.RemoveAll(i => i.Key == item.Key);
        _commsQueue.Add(item);
        while (_commsQueue.Count > 12) _commsQueue.RemoveAt(0);
    }

    private bool HasQueued(string missionId) => _commsQueue.Any(i => i.MissionId == missionId && i.Kind is "message" or "offer");

    private void Show(CommsItem item)
    {
        _commsQueue.RemoveAll(i => i.Key == item.Key);
        _commsShown = item;
        _commsLast = item;
        Sfx.Ui("open");
    }

    /// <summary>Y (or the banner): the oldest unread item; with none, the card closes if open, else the last one again.</summary>
    private void OpenNextComms(bool fromBanner)
    {
        if (_commsQueue.Count > 0)
        {
            Show(_commsQueue[0]);
            return;
        }
        if (_commsShown != null && !fromBanner)
        {
            Sfx.Ui("back");
            _commsShown = null;
            return;
        }
        if (_commsLast != null && CommsStillValid(_commsLast))
        {
            Show(_commsLast);
            return;
        }
        if (!fromBanner) _hud.PushEvent(_sim.Missions.Enabled ? "No transmissions yet" : "No missions in this game (a campaign start turns them on)", new Color(0.62f, 0.82f, 1f));
    }

    private bool CommsStillValid(CommsItem item) => item.Kind == "message" || MissionById(item.MissionId) != null;

    private MissionView? MissionById(string id)
    {
        foreach (var m in _missions) if (m.Id == id) return m;
        return null;
    }

    // ---------------------------------------------------------------- tracker

    private List<TrackerEntry> TrackerEntries()
    {
        var list = new List<TrackerEntry>();
        foreach (var pass in new[] { 0, 1, 2 })
            foreach (var m in _missions)
            {
                var wanted = pass switch
                {
                    0 => m.Kind == MissionKind.Main && m.State == MissionState.Active,
                    1 => m.Kind == MissionKind.Side && m.State == MissionState.Active,
                    _ => m.State == MissionState.Offered,
                };
                if (!wanted) continue;
                var offer = m.State == MissionState.Offered;
                var locatable = HasMapPoint(m.Id);
                var hint = offer ? "Click or Y: read the offer, accept or decline" : m.Options.Count > 0 ? "A decision is waiting: click to answer" : "";
                list.Add(new TrackerEntry(m.Id, offer ? "OFFER" : m.Kind == MissionKind.Main ? "MAIN" : "SIDE", m.Title,
                    offer ? "" : m.StageTitle, offer ? System.Array.Empty<MissionObjectiveLine>() : ObjectiveLines(m), TimerText(m.TimerEndTick), hint,
                    offer ? MissionOffer : m.Kind == MissionKind.Main ? MissionGold : MissionSide, locatable && !offer));
            }
        return list;
    }

    private static List<MissionObjectiveLine> ObjectiveLines(MissionView m)
    {
        var lines = new List<MissionObjectiveLine>();
        foreach (var o in m.Objectives)
        {
            var counted = o.Target > Fix64.Zero;
            var progress = counted ? $"{Hud.Whole(Fix64.Min(o.Progress, o.Target))} / {Hud.Whole(o.Target)}" : "";
            var fraction = counted ? Mathf.Clamp((o.Progress / o.Target).ToFloat(), 0f, 1f) : -1f;
            lines.Add(new MissionObjectiveLine(o.Text, o.Done, o.Optional, progress, fraction));
        }
        return lines;
    }

    /// <summary>The mission has a marker or a found site to fly the camera to.</summary>
    private bool HasMapPoint(string missionId) =>
        _missionMarkers.Any(k => k.MissionId == missionId) || _missionSites.Any(s => s.MissionId == missionId && s.Found);

    /// <summary>"1:24" until the tick, "" with no timer (or once it has passed).</summary>
    private string TimerText(long endTick)
    {
        if (endTick < 0 || endTick <= _sim.Tick) return "";
        var s = (int)((endTick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate);
        return $"{s / 60}:{s % 60:00}";
    }

    private string OfferTimeText(MissionView v) => TimerText(v.TimerEndTick) is { Length: > 0 } t ? $" (answer within {t})" : "";

    private TrackerBanner? TrackerBannerView()
    {
        if (_commsQueue.Count == 0) return null;
        var item = _commsQueue[^1];
        var more = _commsQueue.Count > 1 ? $"  +{_commsQueue.Count - 1}" : "";
        var m = MissionById(item.MissionId);
        var (text, colour) = item.Kind switch
        {
            "message" => ($"INCOMING · {(item.Message!.Value.GiverName.Length > 0 ? item.Message.Value.GiverName : GiverName(item.Message.Value.Giver)).ToUpperInvariant()}  [Y]{more}", MissionGold),
            "offer" => ($"SIDE JOB OFFERED · {(m?.Title ?? "").ToUpperInvariant()}  [Y]{more}", MissionOffer),
            _ when item.Large => ($"CHAPTER COMPLETE  [Y]{more}", MissionGold),
            _ when m?.State == MissionState.Failed => ($"MISSION FAILED · {(m?.Title ?? "").ToUpperInvariant()}  [Y]{more}", MissionBad),
            _ => ($"MISSION COMPLETE · {(m?.Title ?? "").ToUpperInvariant()}  [Y]{more}", MissionGold),
        };
        return new TrackerBanner(text, "Click or press Y to open it", colour);
    }

    /// <summary>A tracker entry clicked: a waiting offer or transmission about it opens; else the camera flies to its
    /// marker; else its briefing.</summary>
    private void TrackerPicked(string missionId)
    {
        var waiting = _commsQueue.FirstOrDefault(i => i.MissionId == missionId);
        if (waiting != null)
        {
            Show(waiting);
            return;
        }
        var m = MissionById(missionId);
        if (m == null) return;
        if (m.State == MissionState.Offered || m.Options.Count > 0)
        {
            Show(new CommsItem($"offer:{m.Id}", m.State == MissionState.Offered ? "offer" : "briefing", m.Id, null));
            return;
        }
        if (!LocateMission(missionId)) Show(new CommsItem($"brief:{m.Id}", "briefing", m.Id, null));
    }

    /// <summary>Fly the camera to the mission's marker (a unit's tile for a unit marker), else a found site of it; in
    /// orbit, turn the globe there. False when it has nothing on the map.</summary>
    private bool LocateMission(string missionId)
    {
        var tile = -1;
        foreach (var k in _sim.Missions.Markers(LocalPlayer))
        {
            if (k.MissionId != missionId) continue;
            tile = k.UnitId > 0 && Surface.FindUnit(k.UnitId) is { } u ? u.Tile : k.Tile;
            if (tile >= 0) break;
        }
        if (tile < 0)
            foreach (var s in _sim.Missions.Sites(LocalPlayer))
                if (s.MissionId == missionId && s.Found)
                {
                    tile = s.Tile;
                    break;
                }
        if (tile < 0 || tile >= Surface.Map.Grid.Count) return false;
        if (_missionLog.IsOpen) _missionLog.Close();
        if (_inOrbit)
        {
            if (_orbit.AtColony)
            {
                if (_orbit.Display == OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.Planet);
                _orbit.Locate(tile);
            }
        }
        else FlyTo(tile, Mathf.Clamp(_distanceTarget, 160f, 320f));
        return true;
    }

    // ---------------------------------------------------------------- comms card

    private string GiverName(string giver) =>
        _giverNames.TryGetValue(giver, out var n) ? n : KnownGivers.TryGetValue(giver, out var k) ? k
        : giver.Length == 0 ? "Mission Control" : char.ToUpperInvariant(giver[0]) + giver[1..].Replace('_', ' ');

    private static string Initials(string name)
    {
        var words = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "MC";
        var first = char.ToUpperInvariant(words[0][0]);
        return words.Length == 1 ? first.ToString() : $"{first}{char.ToUpperInvariant(words[^1][0])}";
    }

    private static Color GiverColour(string giver) => giver switch
    {
        "control" => MissionGold,
        "ops" => new Color(1f, 0.62f, 0.35f),
        "science" => new Color(0.55f, 0.85f, 1f),
        _ => MissionOffer,
    };

    private static string StateName(MissionState s) => s switch
    {
        MissionState.Offered => "offered",
        MissionState.Active => "active",
        MissionState.Completed => "completed",
        MissionState.Failed => "failed",
        MissionState.Declined => "declined",
        _ => "locked",
    };

    private CommsCardView CommsCard(CommsItem item)
    {
        var m = MissionById(item.MissionId);
        var giver = item.Message is { } msg ? msg.Giver : m?.Giver ?? "control";
        var giverName = item.Message is { GiverName.Length: > 0 } named ? named.GiverName : GiverName(giver);
        var ended = m != null && m.State is MissionState.Completed or MissionState.Failed && m.EndTick >= 0;
        var stamp = Clock(item.Message?.Tick ?? (ended ? m!.EndTick : _sim.Tick));
        var title = m?.Title ?? "Mission Control";
        var subtitle = m == null ? "" : $"{(m.Kind == MissionKind.Main ? "Main mission" : "Side job")} · {StateName(m.State)}" + (m.State == MissionState.Active && m.StageTitle.Length > 0 ? $" · {m.StageTitle}" : "");
        var objectives = m != null && m.State is MissionState.Active or MissionState.Offered ? ObjectiveLines(m) : new List<MissionObjectiveLine>();
        var rewards = m?.Rewards ?? (IReadOnlyList<string>)System.Array.Empty<string>();
        var stats = new List<(string, string)>();
        var buttons = new List<CommsButton>();
        string kicker, body;
        var rewardsCaption = "REWARD";
        var footer = "";
        var colour = GiverColour(giver);

        switch (item.Kind)
        {
            case "message":
                kicker = "INCOMING TRANSMISSION";
                body = item.Message!.Value.Text;
                break;
            case "offer":
                kicker = "SIDE JOB OFFERED";
                body = m?.Briefing ?? "";
                if (m != null && TimerText(m.TimerEndTick) is { Length: > 0 } left) footer = $"The offer stands for {left}";
                break;
            case "debrief":
                var failed = m?.State == MissionState.Failed;
                kicker = item.Large ? "CHAPTER COMPLETE" : failed ? "MISSION FAILED" : "MISSION COMPLETE";
                body = m?.Debrief is { Length: > 0 } d ? d : failed ? "The window has closed. There will be other chances." : "Good work. The data is on its way home.";
                rewardsCaption = failed ? "REWARD LOST" : "REWARDS PAID";
                if (failed) colour = MissionBad;
                if (item.Large)
                {
                    stats.Add(("Missions done", $"{_missions.Count(x => x.State == MissionState.Completed)} ({_missions.Count(x => x.Kind == MissionKind.Main && x.State == MissionState.Completed)} main)"));
                    stats.Add(("Time on the surface", Clock(_sim.Tick)));
                    stats.Add(("Tiles surveyed", Me.Surveyed.Count(x => x).ToString()));
                    stats.Add(("Bases", Surface.Bases(LocalPlayer).Count.ToString()));
                }
                break;
            default:
                kicker = "MISSION BRIEFING";
                body = m?.Briefing ?? "";
                break;
        }

        // Choices: an offer's accept / decline (the sim's options, else the two defaults), or a branch's options.
        if (m != null && (m.State == MissionState.Offered || (m.State == MissionState.Active && m.Options.Count > 0)) && item.Kind != "debrief")
        {
            var options = m.Options.Count > 0 ? m.Options
                : m.State == MissionState.Offered ? new[] { new MissionOption("accept", "Accept"), new MissionOption("decline", "Decline") } : System.Array.Empty<MissionOption>();
            var sides = _missions.Count(x => x.Kind == MissionKind.Side && x.State == MissionState.Active);
            foreach (var o in options)
            {
                var accept = o.Id == "accept";
                var full = accept && sides >= 2; // the contract's limit; the sim refuses it anyway
                buttons.Add(new CommsButton(o.Id, o.Text.ToUpperInvariant(), accept || (o.Id != "decline" && options.Count == 1), !full,
                    full ? "Two side jobs are under way already: finish or let one go first" : ""));
            }
        }
        var waiting = _commsQueue.Count;
        if (waiting > 0) buttons.Add(new CommsButton("next", $"NEXT ({waiting})", false, true, "The next transmission waiting (Y)"));
        if (m != null) buttons.Add(new CommsButton("log", "LOG [N]", false, true, "This mission in the mission log"));
        buttons.Add(new CommsButton("close", "CLOSE", buttons.All(b => !b.Primary), true, "Close (Esc / Y)"));
        if (footer.Length == 0) footer = waiting > 0 ? $"{waiting} more waiting  ·  Y next" : "Y closes  ·  N mission log";

        return new CommsCardView(item.Key, kicker, giverName, Initials(giverName), colour, stamp, title, subtitle, body, objectives,
            rewardsCaption, rewards, stats, buttons, footer, item.Large);
    }

    private void CommsButtonPicked(string key, string id)
    {
        var item = _commsShown;
        if (item == null || item.Key != key) return;
        switch (id)
        {
            case "close":
                _commsShown = null;
                return;
            case "next":
                OpenNextComms(false);
                return;
            case "log":
                _commsShown = null;
                var m0 = MissionById(item.MissionId);
                _missionLog.Open(item.MissionId, m0 == null ? -1 : LogGroup(m0.State));
                return;
        }
        var m = MissionById(item.MissionId);
        if (m == null) return;
        var valid = m.Options.Any(o => o.Id == id) || (m.State == MissionState.Offered && m.Options.Count == 0 && id is "accept" or "decline");
        if (!valid)
        {
            _hud.PushEvent($"Can't answer {m.Title} now: it isn't waiting for that", new Color(1f, 0.7f, 0.42f));
            return;
        }
        Sfx.Ui(id == "decline" ? "cancel" : "confirm");
        _sim.Schedule(_sim.Tick + InputDelayTicks, new MissionChoiceCommand(LocalPlayer, m.Id, id));
        _commsShown = null;
        if (_commsQueue.Count > 0) Show(_commsQueue[0]);
    }

    // ---------------------------------------------------------------- log

    private static int LogGroup(MissionState s) => s switch
    {
        MissionState.Completed => 1,
        MissionState.Failed => 2,
        MissionState.Declined => 3,
        _ => 0,
    };

    private List<MissionLogEntry> LogEntries()
    {
        var list = new List<MissionLogEntry>();
        foreach (var m in _missions)
        {
            if (m.State == MissionState.Locked) continue;
            var ended = m.State is MissionState.Completed or MissionState.Failed or MissionState.Declined && m.EndTick >= 0;
            var when = ended ? Clock(m.EndTick) : m.StartTick >= 0 ? Clock(m.StartTick) : "";
            list.Add(new MissionLogEntry(m.Id, m.Title, m.State == MissionState.Offered ? "OFFER" : m.Kind == MissionKind.Main ? "MAIN" : "SIDE",
                LogGroup(m.State), m.State == MissionState.Failed ? MissionBad : m.State == MissionState.Offered ? MissionOffer : m.Kind == MissionKind.Main ? MissionGold : MissionSide, when));
        }
        return list;
    }

    private MissionLogDetail? LogDetail(string? id)
    {
        if (id == null || MissionById(id) is not { } m) return null;
        var messages = new List<(string, string, string)>();
        foreach (var msg in _sim.Missions.Messages(LocalPlayer))
            if (msg.MissionId == id) messages.Add((Clock(msg.Tick), msg.GiverName.Length > 0 ? msg.GiverName : GiverName(msg.Giver), msg.Text));
        var subtitle = $"{(m.Kind == MissionKind.Main ? "MAIN MISSION" : "SIDE JOB")} · {StateName(m.State).ToUpperInvariant()} · {GiverName(m.Giver).ToUpperInvariant()}";
        var live = m.State is MissionState.Active or MissionState.Offered;
        var locatable = live && HasMapPoint(id);
        return new MissionLogDetail(m.Id, m.Title, subtitle, m.State == MissionState.Active ? m.StageTitle : "", m.Briefing,
            live ? ObjectiveLines(m) : new List<MissionObjectiveLine>(), m.State is MissionState.Completed or MissionState.Failed ? m.Debrief : "",
            m.Rewards, m.State == MissionState.Completed, messages, locatable);
    }
}
