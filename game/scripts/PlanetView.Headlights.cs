using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Vehicle headlights (VehicleLights.cs has the lamps and the placeholder numbers; render-only, no sim changes).
// - When: automatic = dark (daylight at the vehicle from SkyField, so the test panel's forced time counts) or murky
//   (Climate.Weather at its tile, or the test panel's forced weather: fog, storms, blizzards, dust, heavy rain/snow, poor
//   visibility), with hysteresis and a short hold so they don't flicker at the threshold; the L key cycles the selected
//   vehicles auto → on → off. Disabled / broken down → off; integrity < 50 → flicker. Another player's vehicle out of
//   sight (fog of war) keeps its lights off. A deployed crawler tips its lamps onto the dig and lights its work lamp.
// - Budget, once a frame after the climate (PlanetDriver.Sky → UpdateHeadlights): lit rigs sorted by distance to the
//   camera; the nearest get real lights up to VehicleLights.MaxSpots within LightDistance, the first ShadowRigs within
//   ShadowDistance get shadows from their main lamp; the rest show only their ground pool (out to PoolDistance).
public partial class PlanetView
{
    private readonly Dictionary<int, VehicleLights.Rig> _headlights = new();
    private readonly List<VehicleLights.Rig> _lightRigs = new();
    private float[] _rigDistance = System.Array.Empty<float>();
    private int[] _rigOrder = System.Array.Empty<int>();
    private float _headlightClock;

    /// <summary>Build a new unit model's lamps (UpdateRovers, before the model enters the tree).</summary>
    private void AttachHeadlights(SurfaceUnit u, SurfaceUnitDef def, Node3D model)
    {
        var rig = VehicleLights.Rig.Attach(model, VehicleLights.KindOf(def), u.Id);
        rig.UnitId = u.Id;
        _headlights[u.Id] = rig;
        AddLightRig(rig);
    }

    /// <summary>A unit's model is gone.</summary>
    private void ForgetHeadlights(int unitId)
    {
        if (!_headlights.Remove(unitId, out var rig)) return;
        RemoveLightRig(rig);
    }

    /// <summary>Lamps that aren't a unit's (drop ships) join the same budget.</summary>
    public void AddLightRig(VehicleLights.Rig rig)
    {
        rig.NextWeather = _headlightClock + rig.Stagger; // spread the weather look-ups
        _lightRigs.Add(rig);
    }

    public void RemoveLightRig(VehicleLights.Rig rig) => _lightRigs.Remove(rig);

    /// <summary>This frame's state of a unit's lamps from the sim (UpdateRovers, every frame).</summary>
    private void UnitHeadlights(SurfaceUnit u, SurfaceUnitDef def)
    {
        if (!_headlights.TryGetValue(u.Id, out var rig)) return;
        rig.Dead = u.Broken;
        rig.Damaged = u.Integrity < Fix64.FromInt(VehicleLights.FlickerBelow);
        rig.Hidden = u.Owner != _localPlayer && FogAt(u.Tile) != Fog.Visible;
        if (def.CanMine)
        {
            // Deployed: the lamps tip down onto the dig site and the boom-tip work lamp comes on.
            var f = _surface.DeployFraction(u).ToFloat();
            rig.Tilt = 14f * Mathf.SmoothStep(0.3f, 0.75f, f);
            for (var i = 0; i < rig.Demand.Length; i++)
                if (rig.RoleOf(i) == VehicleLights.Role.Work) rig.Demand[i] = f > 0.5f ? 1f : 0f;
        }
    }

    /// <summary>Step the headlights of these vehicles to the next setting (auto → on → off → auto), all to the same one,
    /// starting from the first vehicle's. Null when none of them has lamps.</summary>
    public VehicleLights.Mode? CycleHeadlights(IEnumerable<int> unitIds)
    {
        VehicleLights.Mode? next = null;
        foreach (var id in unitIds)
        {
            if (!_headlights.TryGetValue(id, out var rig)) continue;
            next ??= rig.Mode switch { VehicleLights.Mode.Auto => VehicleLights.Mode.On, VehicleLights.Mode.On => VehicleLights.Mode.Off, _ => VehicleLights.Mode.Auto };
            rig.Mode = next.Value;
        }
        return next;
    }

    /// <summary>A short line for the vehicle panel: "auto · on (night)", "on", "off", "off (disabled)".</summary>
    public string HeadlightText(int unitId)
    {
        if (!_headlights.TryGetValue(unitId, out var rig)) return "none";
        if (rig.Dead) return "off (disabled)";
        return rig.Mode switch
        {
            VehicleLights.Mode.On => rig.Damaged ? "on, flickering (damaged)" : "on",
            VehicleLights.Mode.Off => "off",
            _ => rig.AutoOn ? $"auto · on ({rig.Reason}){(rig.Damaged ? ", flickering" : "")}" : $"auto · off ({rig.Reason})",
        };
    }

    /// <summary>
    /// Once a frame after the sky (PlanetDriver.Sky): decide which lamps are on and which of them are real lights.
    /// <paramref name="daylightAt"/> = daylight 0..1 at a world position (SkyField, test-panel time included);
    /// <paramref name="eye"/> = the camera the budget is measured from.
    /// </summary>
    public void UpdateHeadlights(System.Func<Vector3, float> daylightAt, Vector3 eye, float delta)
    {
        _headlightClock += delta;
        var count = _lightRigs.Count;
        if (_rigDistance.Length < count)
        {
            _rigDistance = new float[count + 16];
            _rigOrder = new int[count + 16];
        }

        var sky = SkyOverride;
        var lit = 0;
        for (var i = count - 1; i >= 0; i--)
        {
            var rig = _lightRigs[i];
            if (!IsInstanceValid(rig.Model))
            {
                _lightRigs.RemoveAt(i);
                continue;
            }
            if (!rig.Model.IsInsideTree()) continue;
            var pos = rig.Model.GlobalPosition;

            // Weather at the vehicle, about once a second each (forced by the test panel, or Climate's).
            var murky = rig.Murk > 0.35f;
            if (_headlightClock >= rig.NextWeather)
            {
                rig.NextWeather = _headlightClock + 1.0 + 0.3 * rig.Stagger;
                rig.Murk = MurkAt(pos, sky, out rig.Reason);
                murky = rig.Murk > 0.35f;
            }

            // Automatic: dark (with hysteresis) or murky, held a moment before the lamps follow.
            var day = daylightAt(pos);
            var dark = rig.AutoOn ? day < VehicleLights.DarkOff : day < VehicleLights.DarkOn;
            var want = dark || murky;
            if (want != rig.AutoOn)
            {
                rig.AutoHeld += delta;
                if (rig.AutoHeld >= VehicleLights.SwitchDelay + rig.Stagger)
                {
                    rig.AutoOn = want;
                    rig.AutoHeld = 0f;
                }
            }
            else rig.AutoHeld = 0f;
            if (!murky) rig.Reason = day < 0.02f ? "night" : day < VehicleLights.DarkOff ? "dusk" : "daylight";

            var on = rig.Mode switch { VehicleLights.Mode.On => true, VehicleLights.Mode.Off => false, _ => rig.AutoOn };
            if (rig.Dead || rig.Hidden || !rig.Model.Visible) on = false;
            rig.Level = Mathf.MoveToward(rig.Level, on ? 1f : 0f, delta / (on ? VehicleLights.OnSeconds : VehicleLights.OffSeconds));
            if (rig.Level <= 0.001f)
            {
                rig.Apply(false, false, false, _headlightClock);
                continue;
            }
            _rigDistance[lit] = pos.DistanceSquaredTo(eye);
            _rigOrder[lit] = i;
            lit++;
        }

        // Nearest first: real lights up to the cap, shadows on the closest few, pools for the rest.
        System.Array.Sort(_rigDistance, _rigOrder, 0, lit);
        var spots = 0;
        var shadows = 0;
        for (var k = 0; k < lit; k++)
        {
            var rig = _lightRigs[_rigOrder[k]];
            var d2 = _rigDistance[k];
            var need = rig.SpotsWanted;
            var real = d2 < VehicleLights.LightDistance * VehicleLights.LightDistance && spots + need <= VehicleLights.MaxSpots;
            var shadow = real && need > 0 && shadows < VehicleLights.ShadowRigs && d2 < VehicleLights.ShadowDistance * VehicleLights.ShadowDistance;
            if (real) spots += need;
            if (shadow) shadows++;
            var pool = !real && d2 < VehicleLights.PoolDistance * VehicleLights.PoolDistance;
            rig.Apply(real, shadow, pool, _headlightClock);
        }
    }

    /// <summary>How murky the air is at a spot, 0..1 (0.35+ turns the lamps on), and why.</summary>
    private float MurkAt(Vector3 pos, SkyOverride sky, out string reason)
    {
        WeatherKind kind;
        float intensity, visibility;
        if (sky.WeatherOn)
        {
            kind = sky.Kind;
            intensity = sky.Intensity;
            visibility = SkyModel.Defaults(kind, intensity).Visibility;
        }
        else
        {
            var tile = TileAt(pos);
            if (tile < 0)
            {
                reason = "daylight";
                return 0f;
            }
            var w = Climate.Weather(_sim.Data, _surface.Map, tile, _sim.Tick);
            kind = w.Kind;
            intensity = w.Intensity.ToFloat();
            visibility = w.Visibility.ToFloat();
        }
        var heavy = kind switch
        {
            WeatherKind.Fog or WeatherKind.Storm or WeatherKind.Blizzard or WeatherKind.DustStorm => 0.6f + 0.4f * intensity,
            WeatherKind.Rain or WeatherKind.Snow or WeatherKind.Dust => intensity >= 0.5f ? 0.4f + 0.4f * intensity : 0.25f * intensity,
            _ => 0f,
        };
        // Poor visibility alone also does it: 0.35 at the threshold, 1 in a white-out.
        var seeing = visibility < VehicleLights.MurkVisibility ? 0.35f + 0.65f * (VehicleLights.MurkVisibility - visibility) / VehicleLights.MurkVisibility : 0f;
        var murk = Mathf.Clamp(Mathf.Max(heavy, seeing), 0f, 1f);
        reason = kind switch
        {
            WeatherKind.Fog => "fog",
            WeatherKind.Storm => "storm",
            WeatherKind.Blizzard => "blizzard",
            WeatherKind.DustStorm => "dust storm",
            WeatherKind.Dust => "dust",
            WeatherKind.Rain => "rain",
            WeatherKind.Snow => "snow",
            _ => "poor visibility",
        };
        return murk;
    }
}
