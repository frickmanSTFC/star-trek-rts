using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Weather's gameplay hooks (data/climate.json weather effects, mild): ground vehicles drive slower and survey less in
// rain, snow, fog and storms; rovers wear and roll hazards and flats more often; drop ships, outpost drops and the first
// landing are refused under storms (and drop ships under gusts too). The weather is a pure function of the data, the
// map and the tick (Climate), so nothing here is new state: the hash covers it through the tick and the map.
public sealed partial class PlanetSurface
{
    /// <summary>The tick being stepped (the last one stepped, between steps): the weather's time for the hooks that take
    /// no tick (speed, survey radius, wear). Equal to Simulation.Tick during a step, so not separate state.</summary>
    private long _weatherTick;

    /// <summary>The weather's gameplay effects on a tile now (as of the last step).</summary>
    public WeatherEffects WeatherEffectsAt(int tile) =>
        tile >= 0 && tile < Map.Grid.Count ? Climate.Effects(_data, Map, tile, _weatherTick) : new WeatherEffects(Fix64.One, 0, Fix64.One, false, Fix64.One);

    /// <summary>How fast a unit drives now: <see cref="DriveSpeedPerTick"/> (damage, flats, breakdowns) × the weather on its tile.</summary>
    public Fix64 SpeedPerTick(SurfaceUnit unit) => DriveSpeedPerTick(unit) * WeatherEffectsAt(unit.Tile).RoverSpeed;

    /// <summary>The weather's share of a unit's speed now (1 = no effect), for the HUD.</summary>
    public Fix64 WeatherSpeed(SurfaceUnit unit) => WeatherEffectsAt(unit.Tile).RoverSpeed;

    /// <summary>How far it surveys now: its sensors' radius (damage, disabled) plus the weather's delta, never below 1
    /// (a unit that sees nothing, like a disabled rover, stays at 0).</summary>
    public int SurveyRadiusOf(SurfaceUnit u)
    {
        var radius = SensorRadiusOf(u);
        if (radius <= 0) return radius;
        return System.Math.Max(1, radius + WeatherEffectsAt(u.Tile).SurveyRadiusDelta);
    }

    /// <summary>Multiplier on a rover's wear, hazard and flat-tyre chances from the weather on its tile.</summary>
    private Fix64 WeatherDamage(SurfaceUnit u) => WeatherEffectsAt(u.Tile).RoverDamage;

    /// <summary>Why a drop ship or an outpost drop can't come down on this tile at this tick (storm or high winds), or null.</summary>
    public string? DropWeatherProblem(long tick, int tile) => Climate.LandingProblem(_data, Map, tile, tick);

    /// <summary>
    /// Why the player can't send the first landing to this tile now (null = they can): not in orbit, an unsafe site,
    /// or a storm over it (only the storm kinds count, not gusts, and the map starts calm: climate.json
    /// calm_start_seconds), so the start can't be blown out; storms pass in a few minutes and are regional.
    /// </summary>
    public string? LandProblem(Simulation sim, int player, int tile)
    {
        if (player < 0 || player >= _players.Count) return "no such player";
        if (_players[player].Phase != LandingPhase.InOrbit) return "the colony has landed";
        if (!OnMap(tile)) return "off the map";
        if (!CanLand(player, tile))
            return Map.NearNatives(tile) ? "that's the natives' land"
                : Map.Safety(tile) == LandingSafety.Unsafe ? "not a safe landing site"
                : PodLandingNear(tile) ? "another colony is landing there" : "too close to a building";
        return Climate.LandingProblem(_data, Map, tile, sim.Tick, stormsOnly: true);
    }
}
