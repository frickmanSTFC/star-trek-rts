using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The outpost's uplink to the ship (PlanetSurface.UplinkOnline: once the landed outpost has assembled): the helm's
// surface video feed (SurfaceFeed) and the environmental telemetry (sim Telemetry, shown by SurfaceTelemetry) in the
// Atmosphere and Planet displays, the findings feed and the event feed. Render-side only.
public partial class PlanetDriver
{
    private SurfaceFeed _feed = null!;
    private readonly SurfaceTelemetry _telemetry = new();
    private SurfaceReadings? _readings;
    private double _readingsAt = -1;
    private bool _uplinkAnnounced;
    private readonly HashSet<string> _hazardsPresent = new();
    private readonly Dictionary<string, double> _hazardsAnnouncedAt = new();

    private static readonly Color UplinkColour = new(0.37f, 0.95f, 0.9f);
    private static readonly Color HazardWarn = new(1f, 0.7f, 0.42f);
    private static readonly Color HazardSevere = new(1f, 0.42f, 0.36f);

    private void SetupUplink(CanvasLayer helmLayer)
    {
        _feed = new SurfaceFeed { Name = "SurfaceFeed" };
        helmLayer.AddChild(_feed); // after the helm, so the window draws over its frame
        _feed.Setup(_view, _orbit, _sun);
        var env = _data.EnvironmentOf(Surface.Map.Def.ClassId);
        if (env != null) _feed.SetSkyColour(Color.FromHtml(env.Sky));
    }

    private bool UplinkUp => Me.Phase == LandingPhase.Landed && Surface.UplinkOnline(LocalPlayer, _sim.Tick);

    /// <summary>Readings (4 a second), the uplink and hazard events, and the feed window. Every frame, after the view's refresh.</summary>
    private void UpdateUplink(float delta)
    {
        var online = UplinkUp;
        if (online && (_readings == null || _clock - _readingsAt >= 0.25))
        {
            _readingsAt = _clock;
            _readings = Telemetry.Read(_data, Surface.Map, Me.LandingTile, _sim.Tick);
            if (_readings != null) _telemetry.Record(_readings, _clock);
        }
        else if (!online && _readings != null)
        {
            _readings = null;
            _telemetry.Clear();
        }

        if (online && !_uplinkAnnounced)
        {
            _uplinkAnnounced = true;
            _hud.PushEvent($"{_data.Building(_data.Landing.HubBuilding).Name} assembled and working — uplink established, surface feed and telemetry online", UplinkColour);
            if (_inOrbit) _feed.Open = true;
            else _hud.PushEvent("Press O for the helm: the outpost's video feed and sensor readings are up", UplinkColour);
            _findingsAt = -1; // refresh the helm's findings now
        }
        if (_readings != null) AnnounceHazards(_readings);

        var show = _inOrbit && online && _feed.Open;
        var vehicles = new List<(Node3D Model, string Name)>();
        string site = PlanetName, stamp = "";
        var outpost = Vector3.Zero;
        if (show)
        {
            foreach (var u in Surface.Units.Where(u => u.Owner == LocalPlayer))
                if (_view.UnitModel(u.Id) is { } model) vehicles.Add((model, UnitName(u)));
            var core = Surface.Bases(LocalPlayer).FirstOrDefault();
            site = core != null ? BaseName(core.Id) : PlanetName;
            outpost = _view.TileCentre(Me.LandingTile);
            var since = System.Math.Max(0, (_sim.Tick - Me.UplinkTick) / Simulation.TickRate);
            stamp = $"T+{since / 60:00}:{since % 60:00}" + (_readings != null ? $"  {SurfaceTelemetry.Clock(_readings.LocalHours.ToFloat())}" : "");
        }
        _feed.Refresh(_inOrbit, show, _helm.OpenRect, _helm.UiScale, outpost, _view.MapCentre(), site, stamp, _readings, vehicles, delta);
    }

    /// <summary>An event line when a hazard shows up (once while it lasts, and not again within 90 s if it flickers).</summary>
    private void AnnounceHazards(SurfaceReadings r)
    {
        var now = new HashSet<string>();
        var fresh = new List<TelemetryHazard>();
        foreach (var h in r.Hazards)
        {
            now.Add(h.Key);
            if (_hazardsPresent.Contains(h.Key)) continue;
            if (_hazardsAnnouncedAt.TryGetValue(h.Key, out var at) && _clock - at < 90) continue;
            _hazardsAnnouncedAt[h.Key] = _clock;
            fresh.Add(h);
        }
        // A hostile world reports many at once: the worst three get a line each, the rest one summary line.
        fresh = fresh.OrderByDescending(h => h.Severe).ToList();
        foreach (var h in fresh.Take(fresh.Count > 4 ? 3 : 4))
            _hud.PushEvent(SurfaceTelemetry.HazardText(h, r), h.Severe ? HazardSevere : HazardWarn);
        if (fresh.Count > 4) _hud.PushEvent($"… and {fresh.Count - 3} more hazards at the outpost (helm findings)", HazardSevere);
        if (fresh.Count > 0) _findingsAt = -1;
        _hazardsPresent.Clear();
        _hazardsPresent.UnionWith(now);
    }

    /// <summary>Why there's no telemetry yet (or "online").</summary>
    private string UplinkStatus()
    {
        var me = Me;
        if (me.Phase == LandingPhase.InOrbit) return "awaiting surface uplink · no outpost on the surface";
        if (me.Phase == LandingPhase.Descending) return "awaiting surface uplink · colony pod descending";
        if (!Surface.UplinkOnline(LocalPlayer, _sim.Tick))
            return $"awaiting surface uplink · outpost assembling, {System.Math.Max(0, (me.UplinkTick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate)} s";
        return _readings == null ? "uplink online · no sensor data for this world" : "online";
    }

    /// <summary>The helm's findings from the outpost: its status and weather, then each hazard.</summary>
    private IEnumerable<string> UplinkFindings()
    {
        if (Me.Phase != LandingPhase.Landed) yield break;
        if (_readings is not { } r)
        {
            yield return $"Outpost: {UplinkStatus()}";
            yield break;
        }
        yield return $"Outpost uplink online · {SurfaceTelemetry.SkyName(r.Sky)}, {SurfaceTelemetry.Celsius(r.TemperatureC.ToFloat())}" +
                     (r.Sky == SkyCondition.Vacuum ? "" : $", wind {SurfaceTelemetry.CompassPoint(r.WindFromDeg)} {r.WindMs.ToFloat():0} m/s");
        foreach (var h in r.Hazards.OrderByDescending(h => h.Severe)) yield return $"HAZARD · {SurfaceTelemetry.HazardText(h, r)}";
    }

    /// <summary>The Atmosphere display's orbital rows; once the sensors report, the planet name and habitability make room for them.</summary>
    private IReadOnlyList<HelmRow> AtmosphereConditions()
    {
        var rows = _orbit.AtmosphereRows();
        return _readings == null ? rows : rows.Where(r => r.Label is not ("PLANET" or "HABITABILITY")).ToList();
    }

    /// <summary>The FEED pill on the helm's action strip (greyed until the uplink is up; it still says why).</summary>
    private HelmAction FeedPill()
    {
        var up = UplinkUp;
        var on = up && _feed.Open;
        return new HelmAction("feed", "F", "FEED", up, on, on ? HelmScreen.Cyan : null);
    }

    private void ToggleFeed()
    {
        if (!UplinkUp)
        {
            _hud.PushEvent($"No surface feed yet: {UplinkStatus().Replace("awaiting surface uplink · ", "")}", HazardWarn);
            return;
        }
        _feed.Open = !_feed.Open;
    }
}
