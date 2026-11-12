using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Survey satellites on the helm (docs/satellite-contract.md "View / UI"; render-only, commands only): the SATELLITES
// window (SatelliteCard: list + card), hover and click on a satellite in the 3D view, FOLLOW, RETASK (arms a click on
// the globe → RetaskSatelliteCommand), comms relays' rows (PlanetDriver.Relays.cs), the pictures (SatellitePhotos: every few seconds of sweeping while its card is
// open, and on findings), the flash on each picture, event lines for first finds, and the shutter tick.

public partial class PlanetDriver
{
    private const float PictureEvery = 6f;
    private const float FindingShotGap = 2.5f;

    private SatelliteCard _satCard = null!;
    private SatellitePhotos _photos = null!;
    private bool _satPanelOpen;
    private string? _satKey; // "sat:<id>" (the colony's) or "sys:<body>" (another body's)
    private int? _satHover;
    private bool _retasking;
    private int _locatedTile = -1;
    private readonly Dictionary<int, int> _satSeen = new(); // findings total already handled, per satellite
    private readonly Dictionary<int, float> _satNextPicture = new();
    private readonly Dictionary<int, float> _satFindingShot = new();
    private long _photoCountHeard;
    private float _satClock;
    private static readonly List<SatListEntry> NoSatellites = new();

    private static readonly Color DepositColour = HelmScreen.Peach;
    private static readonly Color ToxicColour = Color.FromHtml("#c8e35a");
    private static readonly Color TerrainColour = Color.FromHtml("#c9c2b8");

    private void SetupSatellites(CanvasLayer helmLayer)
    {
        _satCard = new SatelliteCard { Name = "SatelliteCard" };
        helmLayer.AddChild(_satCard); // after the feed window, so it draws over the helm frame too
        _satCard.Picked += SelectSatellite;
        _satCard.Closed += () => _satPanelOpen = false;
        _satCard.FollowToggled += ToggleFollow;
        _satCard.RetaskToggled += ToggleRetask;
        _satCard.FindingPicked += LocateFinding;
        _photos = new SatellitePhotos();
        AddChild(_photos);
        _photos.Setup(_view, _feed, _orbit);
    }

    private int? SelectedSatelliteId => _satKey != null && _satKey.StartsWith("sat:") && int.TryParse(_satKey[4..], out var id) ? id : null;

    private Satellite? SelectedSatellite => SelectedSatelliteId is { } id && Surface.SatelliteById(id) is { } s && s.Owner == LocalPlayer ? s : null;

    private void ToggleSatellites() => _satPanelOpen = !_satPanelOpen;

    private void SelectSatellite(string key)
    {
        _satPanelOpen = true;
        if (_satKey != key) _retasking = false;
        _satKey = key;
        if (key.StartsWith("relay:")) LocateRelay(key); // a comms relay: turn the globe to its station (PlanetDriver.Relays.cs)
    }

    private void ToggleFollow()
    {
        if (SelectedSatellite is not { } sat || !_orbit.AtColony) return;
        _orbit.FollowSatellite = _orbit.FollowSatellite == sat.Id ? null : sat.Id;
        if (_orbit.FollowSatellite != null && _orbit.Display == OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.Planet);
    }

    private void ToggleRetask()
    {
        if (_retasking)
        {
            _retasking = false;
            return;
        }
        if (SelectedSatellite is not { } sat) return;
        if (RetaskCardProblem(sat) is { } why)
        {
            Sfx.Refused();
            _hud.PushEvent($"Can't retask {sat.CallSign}: {why}", new Color(1f, 0.7f, 0.42f));
            return;
        }
        _orbitAction = null;
        if (_info != null && _info.StartsWith("orbit:")) _info = null;
        _orbit.FollowSatellite = null;
        if (_orbit.Display == OrbitDisplay.System) SetOrbitDisplay(OrbitDisplay.Planet);
        _retasking = true;
    }

    /// <summary>Why the card's RETASK can't arm (the sim re-checks the command).</summary>
    private string? RetaskCardProblem(Satellite sat)
    {
        if (!_orbit.AtColony) return $"the ship must be in orbit of {PlanetName} to aim it";
        if (sat.Deploying(_sim.Tick)) return sat.Retasks == 0 ? "still deploying" : "still manoeuvring to its band";
        return null;
    }

    /// <summary>A finding picked in the card: show it on the globe; picked again (and landed), go down to it.</summary>
    private void LocateFinding(int tile)
    {
        if (tile < 0) return;
        if (tile == _locatedTile && Me.Phase == LandingPhase.Landed && _orbit.AtColony)
        {
            _locatedTile = -1;
            ExitOrbit(tile, 230f);
            return;
        }
        _locatedTile = tile;
        _orbit.Locate(tile);
    }

    // ---------------------------------------------------------------- 3D hover / click / retask (from OrbitHover / OrbitClick)

    /// <summary>Hover over a satellite (OrbitHover): which one, and the globe's tile hover gives way to it.</summary>
    private void SatelliteHover(Vector2 mouse)
    {
        _satHover = _mouseOverUi || _retasking || _orbitAction != null ? null : _orbit.PickSatellite(mouse);
        _orbit.HoverSatellite = _satHover;
        _orbit.SelectedSatellite = _satPanelOpen ? SelectedSatelliteId : null;
        if (_satHover != null) _orbitHover = -1;
    }

    /// <summary>The hover ring's colour while RETASK is armed (green = that band is fine), else null.</summary>
    private Color? RetaskHoverColour()
    {
        if (!_retasking) return null;
        if (_orbitHover < 0 || SelectedSatellite is not { } sat) return new Color(1f, 0.3f, 0.25f, 0.9f);
        return Surface.RetaskProblem(_sim, LocalPlayer, sat.Id, Surface.Map.Grid.Row(_orbitHover)) == null ? new Color(0.3f, 1f, 0.4f, 0.95f) : new Color(1f, 0.3f, 0.25f, 0.9f);
    }

    /// <summary>A click in orbit: a satellite under the mouse opens its card; with RETASK armed, the clicked row is its new
    /// band. True when the click was used.</summary>
    private bool SatelliteClick()
    {
        if (_retasking)
        {
            if (_orbitHover < 0 || SelectedSatellite is not { } sat) return true;
            var row = Surface.Map.Grid.Row(_orbitHover);
            if (Surface.RetaskProblem(_sim, LocalPlayer, sat.Id, row) is { } problem)
            {
                Sfx.Refused();
                _hud.PushEvent($"Can't retask {sat.CallSign} there: {problem}", new Color(1f, 0.7f, 0.42f));
                return true;
            }
            Sfx.Ui("confirm");
            _sim.Schedule(_sim.Tick + InputDelayTicks, new RetaskSatelliteCommand(LocalPlayer, sat.Id, row));
            _hud.PushEvent($"{sat.CallSign} retasked to {LatitudeText(row)}", new Color(0.62f, 0.82f, 1f));
            _retasking = false;
            return true;
        }
        if (_satHover is { } id && _orbitAction == null)
        {
            SelectSatellite($"sat:{id}");
            Sfx.Ui("open");
            return true;
        }
        return false;
    }

    /// <summary>The cursor readout over a satellite (OrbitHud). True when it showed one.</summary>
    private bool SatelliteReadout(Vector2 mouse)
    {
        if (_satHover is not { } id || Surface.SatelliteById(id) is not { } sat) return false;
        _helm.ShowReadout(sat.CallSign, $"{SatelliteStatus(sat)}\nClick for its card: what it has found and its pictures", HelmScreen.Cyan, mouse + new Vector2(18, 14));
        return true;
    }

    /// <summary>The helm title's hint while RETASK is armed, else null.</summary>
    private string? SatelliteHint() =>
        _retasking && SelectedSatellite is { } sat ? $"CLICK A LATITUDE FOR {sat.CallSign} · RIGHT-CLICK CANCELS" : null;

    // ---------------------------------------------------------------- launch (OrbitClick)

    private readonly HashSet<int> _satsDeploying = new(); // survey satellites seen deploying, not yet reported up

    /// <summary>The SATELLITE action's click (checked just before): a survey satellite over the clicked latitude that
    /// unfolds over the clicked spot (SatelliteCommand.Column), so it is seen leaving the ship and arriving there.</summary>
    private void SendSatellite(long when, int tile)
    {
        var g = Surface.Map.Grid;
        _sim.Schedule(when, new SatelliteCommand(LocalPlayer, g.Row(tile), g.Column(tile)));
        var ordinal = Surface.Satellites.Count(x => x.Owner == LocalPlayer && x.Kind == SatelliteKind.Survey) + 1;
        var orbit = _data.Orbit!;
        _hud.PushEvent($"{Surface.SatelliteCallSign(LocalPlayer, ordinal)} launching: rising from the ship to {Coordinates(tile)}, " +
            $"unfolds and starts sweeping {LatitudeText(g.Row(tile))} in {orbit.SatelliteDeploySeconds} s", HelmScreen.Cyan);
    }

    /// <summary>"SAT-2 · OSPREY is sweeping …" once each satellite (launched while we watched) finishes deploying.</summary>
    private void SatelliteUpEvents()
    {
        foreach (var sat in Surface.Satellites)
        {
            if (sat.Owner != LocalPlayer || sat.Kind != SatelliteKind.Survey || sat.Retasks > 0) continue;
            if (sat.Deploying(_sim.Tick))
            {
                _satsDeploying.Add(sat.Id); // seen deploying: report it once it is up
                continue;
            }
            if (!_satsDeploying.Remove(sat.Id)) continue; // already up when first seen, or reported
            _hud.PushEvent($"{sat.CallSign} unfolded over {LatitudeText(sat.Row)}: sweeping its band, a lap every {_data.Orbit!.SatelliteSweepSeconds} s", HelmScreen.Cyan);
        }
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Every frame after the uplink: first-find events, the flash and pictures, the window.</summary>
    private void UpdateSatellites(float delta)
    {
        _satClock += delta;
        SatelliteUpEvents();
        if (!_inOrbit)
        {
            _retasking = false;
            _orbit.FollowSatellite = null;
        }
        var orbit = _data.Orbit;
        var atColony = _inOrbit && _orbit.AtColony;
        var selected = SelectedSatelliteId;
        foreach (var sat in Surface.Satellites)
        {
            if (sat.Owner != LocalPlayer || sat.Kind == SatelliteKind.Relay) continue;
            var seen = _satSeen.GetValueOrDefault(sat.Id, 0);
            if (sat.FindingsTotal > seen)
            {
                _satSeen[sat.Id] = sat.FindingsTotal;
                // Only the lines still in the log; a burst (one column can hold several) shows its firsts and pictures the newest.
                var fresh = sat.Findings.Skip(System.Math.Max(0, sat.Findings.Count - (sat.FindingsTotal - seen))).ToList();
                foreach (var f in fresh.Where(f => f.First))
                    _hud.PushEvent($"{sat.CallSign}: {FindingKind(f.Kind).ToLowerInvariant()} — {FindingText(f)}", FindingColour(f));
                if (atColony && fresh.Count > 0)
                {
                    var latest = fresh[^1];
                    _orbit.Snap(sat.Id, latest.Tile);
                    var notable = fresh.Any(f => f.First || f.Kind is SatelliteFindingKind.Natives or SatelliteFindingKind.Ruins or SatelliteFindingKind.Weather);
                    var watching = _satPanelOpen && selected == sat.Id;
                    if ((watching || notable) && _satClock - _satFindingShot.GetValueOrDefault(sat.Id, -99f) >= FindingShotGap)
                    {
                        _satFindingShot[sat.Id] = _satClock;
                        var shot = fresh.LastOrDefault(f => f.First) is { Tile: >= 0 } firstOne && !watching ? firstOne : latest;
                        _photos.Request(sat.Id, _sim.Tick, shot.Tile, _satCard.Mode, $"{sat.CallSign} · {FindingKind(shot.Kind)}: {FindingText(shot)}", PhotoStamp(sat, shot.Tile), finding: true);
                    }
                    _satNextPicture[sat.Id] = _satClock + PictureEvery;
                }
            }
            if (!atColony || sat.Deploying(_sim.Tick)) continue;
            if (_satClock >= _satNextPicture.GetValueOrDefault(sat.Id, 0f))
            {
                _satNextPicture[sat.Id] = _satClock + PictureEvery;
                var under = _orbit.SubSatelliteTile(sat.Id);
                _orbit.Snap(sat.Id, -1);
                if (under >= 0 && _satPanelOpen && selected == sat.Id && !_photos.Busy)
                    _photos.Request(sat.Id, _sim.Tick, under, _satCard.Mode, $"{sat.CallSign} · {Surface.Map.Terrain(under).Name}", PhotoStamp(sat, under), finding: false);
            }
        }

        var watchingCard = _inOrbit && _satPanelOpen && SelectedSatellite != null;
        _photos.Wanted = watchingCard && atColony;
        _photos.Update(_inOrbit);

        // The shutter tick when a picture of the satellite on the card comes in.
        var photos = selected is { } sid ? _photos.PhotosOf(sid) : (IReadOnlyList<SatellitePhoto>)System.Array.Empty<SatellitePhoto>();
        var count = photos.Count > 0 ? photos[^1].Tick * 31 + photos[^1].Tile + (selected ?? 0) * 7919L : 0L;
        if (watchingCard && count != _photoCountHeard && photos.Count > 0) Sfx.Ui("tutorial", -16f, 1.7f);
        _photoCountHeard = count;

        if (_satKey != null && SatelliteEntryGone(_satKey))
        {
            _satKey = null;
            _retasking = false;
        }
        var show = _inOrbit && _satPanelOpen && orbit != null;
        if (!show)
        {
            _satCard.Refresh(false, _helm.OpenRect, _helm.UiScale, NoSatellites, null, null, photos, false);
            return;
        }
        var list = SatelliteList();
        if (_satKey == null && list.Count > 0) _satKey = list[0].Key;
        var collapsed = _info != null && _info.StartsWith("orbit:");
        _satCard.Refresh(true, _helm.OpenRect, _helm.UiScale, list, _satKey, SatelliteCardView(), photos, collapsed);
    }

    private bool SatelliteEntryGone(string key)
    {
        if (key.StartsWith("sat:")) return SelectedSatellite == null;
        if (key.StartsWith("relay:")) return RelayGone(key);
        var body = key[4..];
        return _system.State?.Satellites.Any(s => s.Owner == LocalPlayer && s.BodyId == body) != true;
    }

    /// <summary>Our satellites: the colony world's survey satellites, then the ones left at other bodies.</summary>
    private List<SatListEntry> SatelliteList()
    {
        var list = new List<SatListEntry>();
        var relays = RelaySatelliteIds(); // comms relays get their own rows (PlanetDriver.Relays.cs)
        foreach (var sat in Surface.Satellites)
            if (sat.Owner == LocalPlayer && sat.Kind != SatelliteKind.Relay && !relays.Contains(sat.Id))
                list.Add(new SatListEntry($"sat:{sat.Id}", sat.CallSign, SatelliteShortStatus(sat), HelmScreen.Sky));
        list.AddRange(RelayListEntries());
        if (_system.State is { } state)
            foreach (var s in state.Satellites)
                if (s.Owner == LocalPlayer && _system.Body(s.BodyId) is { } b)
                    list.Add(new SatListEntry($"sys:{b.Id}", $"SAT · {_system.KnownName(b)}", "LIVE DATA", HelmScreen.Lavender));
        return list;
    }

    private string SatelliteShortStatus(Satellite sat)
    {
        if (sat.Deploying(_sim.Tick)) return sat.Retasks == 0 ? "DEPLOYING" : "MANOEUVRING";
        var g = Surface.Map.Grid;
        return $"PASS {sat.Passes + 1} · {sat.Swept % g.Width * 100 / g.Width}%";
    }

    private string SatelliteStatus(Satellite sat)
    {
        var orbit = _data.Orbit!;
        var tick = _sim.Tick;
        if (sat.Deploying(tick) && sat.Retasks == 0)
        {
            var f = (tick - sat.LaunchTick) / (float)System.Math.Max(1, sat.ReadyTick - sat.LaunchTick);
            var left = (sat.ReadyTick - tick + Simulation.TickRate - 1) / Simulation.TickRate;
            return f < 0.6f ? $"deploying · rising from the ship ({left} s to its first sweep)" : $"deploying · unfolding its wings ({left} s to its first sweep)";
        }
        if (sat.Deploying(tick)) return $"manoeuvring to {LatitudeText(sat.Row)} · {(sat.ReadyTick - tick + Simulation.TickRate - 1) / Simulation.TickRate} s";
        var lap = orbit.SatelliteSweepSeconds;
        return sat.Passes == 0 ? $"sweeping · first pass ({lap} s a lap)" : $"sweeping · pass {sat.Passes + 1} ({lap} s a lap)";
    }

    /// <summary>The card for the picked entry (colony satellite or another body's).</summary>
    private SatCardView? SatelliteCardView()
    {
        if (_satKey == null) return null;
        if (_satKey.StartsWith("relay:")) return RelayCardView(_satKey);
        if (_satKey.StartsWith("sys:"))
        {
            var b = _system.Body(_satKey[4..]);
            var placed = _system.State?.Satellites.FirstOrDefault(s => s.Owner == LocalPlayer && s.BodyId == _satKey[4..]);
            if (b == null || placed == null) return null;
            var rows = new List<HelmRow>
            {
                new("BODY", $"{_system.KnownName(b)} · {(_system.Identified(b.Id) ? _system.ClassName(b.ClassId) : "unidentified")}"),
                new("PLACED", Clock(placed.PlacedTick)),
            };
            rows.AddRange(LiveRows(b)); // PlanetDriver.System.cs
            return new SatCardView(_satKey, $"SAT · {_system.KnownName(b)}", "relay in orbit · live data", rows, false, false, false, false, null, System.Array.Empty<SatFindingLine>(), 0);
        }
        if (SelectedSatellite is not { } sat) return null;
        var g = Surface.Map.Grid;
        var (first, last) = Surface.SatelliteBand(sat);
        var coverage = Surface.SatelliteCoverage(sat).ToFloat();
        var lap = Surface.SatelliteLapProgress(sat).ToFloat();
        var deploying = sat.Deploying(_sim.Tick);
        var under = _orbit.SubSatelliteTile(sat.Id);
        var prewarp = _data.Orbit?.Era == "dawn_of_warp";
        var bandRows = last - first + 1;
        var megabytes = sat.Swept * bandRows * 2.4f + sat.FindingsTotal * 18f + _photos.PhotosOf(sat.Id).Count * 6f;
        var cardRows = new List<HelmRow>
        {
            new("MODEL", BuildingModels.SatelliteModelName(prewarp)),
            new("BAND", $"{LatitudeText(last)} – {LatitudeText(first)} · {bandRows} rows"),
            new("STATUS", SatelliteStatus(sat), deploying ? HelmScreen.Peach : HelmScreen.Green),
            new("SWEEP", deploying && sat.Passes == 0 && sat.Swept == 0 ? "starts once deployed" : $"{Mathf.RoundToInt(lap * 100)}% of lap {sat.Passes + 1}", HelmScreen.Cyan, lap),
            new("COVERAGE", $"{Mathf.RoundToInt(coverage * 100)}% of the band surveyed", coverage >= 0.999f ? HelmScreen.Green : HelmScreen.Cyan, coverage),
            new("SURVEYED", $"{sat.TilesSurveyed} tiles first by it"),
            new("DOWNLINK", megabytes >= 1000f ? $"{megabytes / 1000f:0.0} GB" : $"{megabytes:0} MB"),
            new("OVER", under >= 0 ? $"{Surface.Map.Terrain(under).Name} · {Coordinates(under)}" : "—"),
        };
        if (sat.Retasks > 0) cardRows.Add(new HelmRow("RETASKED", $"{sat.Retasks}×"));
        var lines = new List<SatFindingLine>();
        for (var i = sat.Findings.Count - 1; i >= 0; i--)
        {
            var f = sat.Findings[i];
            lines.Add(new SatFindingLine(Clock(f.Tick), FindingKind(f.Kind), FindingText(f), FindingColour(f), f.First, f.Tile));
        }
        var retaskProblem = RetaskCardProblem(sat);
        return new SatCardView(_satKey, sat.CallSign, $"launched {Clock(sat.LaunchTick)} · {LatitudeText(sat.Row)}", cardRows, true,
            _orbit.FollowSatellite == sat.Id, _orbit.AtColony, _retasking, retaskProblem, lines, sat.FindingsTotal);
    }

    // ---------------------------------------------------------------- words

    private static string FindingKind(SatelliteFindingKind kind) => kind switch
    {
        SatelliteFindingKind.Deposit => "DEPOSIT",
        SatelliteFindingKind.Life => "LIFE",
        SatelliteFindingKind.Natives => "NATIVES",
        SatelliteFindingKind.Toxic => "TOXIC",
        SatelliteFindingKind.Ruins => "RUINS",
        SatelliteFindingKind.Weather => "WEATHER",
        _ => "TERRAIN",
    };

    private string FindingText(SatelliteFinding f)
    {
        var map = Surface.Map;
        switch (f.Kind)
        {
            case SatelliteFindingKind.Deposit:
                var name = f.Detail >= 0 && f.Detail < _data.Resources.Count ? _data.Resources[f.Detail].Name : "unknown";
                return f.Tile >= 0 && f.Tile < map.Grid.Count ? $"{name} {Hud.Whole(map.DepositStart[f.Tile])}" : name;
            case SatelliteFindingKind.Life:
                return f.Detail == (int)Wildlife.Predators ? "predators" : "grazing herds";
            case SatelliteFindingKind.Natives:
                return "a native settlement: keep clear (Prime Directive)";
            case SatelliteFindingKind.Toxic:
                return "toxic ground";
            case SatelliteFindingKind.Ruins:
                return Surface.RuinAt(f.Tile) is { } ruin ? $"ruins ({ruin.Species})" : "ruins";
            case SatelliteFindingKind.Weather:
                return $"{Climate.Name(_data, (WeatherKind)f.Detail).ToLowerInvariant()} under its track";
            default:
                return f.Detail >= 0 && f.Detail < _data.Terrain.Count ? $"{_data.Terrain[f.Detail].Name.ToLowerInvariant()} massif" : "terrain";
        }
    }

    private static Color FindingColour(SatelliteFinding f) => f.Kind switch
    {
        SatelliteFindingKind.Deposit => DepositColour,
        SatelliteFindingKind.Life => f.Detail == (int)Wildlife.Predators ? HelmScreen.Red : HelmScreen.Green,
        SatelliteFindingKind.Natives or SatelliteFindingKind.Ruins => HelmScreen.Lavender,
        SatelliteFindingKind.Toxic => ToxicColour,
        SatelliteFindingKind.Weather => HelmScreen.Sky,
        _ => TerrainColour,
    };

    /// <summary>"12°N" for a map row (the globe's latitude of its centre).</summary>
    private string LatitudeText(int row)
    {
        var lat = Mathf.RoundToInt(_orbit.RowLatitude(row));
        return $"{Mathf.Abs(lat)}°{(lat >= 0 ? "N" : "S")}";
    }

    /// <summary>"12°N 140°E" for a tile.</summary>
    private string Coordinates(int tile)
    {
        var g = Surface.Map.Grid;
        var lon = Mathf.RoundToInt((g.Column(tile) + 0.5f) / g.Width * 360f - 180f);
        return $"{LatitudeText(g.Row(tile))} {Mathf.Abs(lon)}°{(lon >= 0 ? "E" : "W")}";
    }

    /// <summary>Game time of a tick, "T+12:04".</summary>
    private static string Clock(long tick)
    {
        var s = System.Math.Max(0, tick / Simulation.TickRate);
        return $"T+{s / 60:00}:{s % 60:00}";
    }

    private string PhotoStamp(Satellite sat, int tile) => $"{Clock(_sim.Tick)}  ·  {Coordinates(tile)}  ·  {Surface.Map.Terrain(tile).Name.ToUpperInvariant()}  ·  PASS {sat.Passes + 1}";
}
