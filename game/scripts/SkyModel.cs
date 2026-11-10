using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Render-side sky maths shared by the surface (PlanetDriver.Sky, SkyField, WeatherView) and the orbit globe: the
/// latitude of a map row (the band OrbitView paints the map over), the sun's height and compass bearing for a test-panel
/// time of day, and <see cref="Shade"/>, the C# twin of sky.gdshaderinc's sky_shade(). Nothing here touches the sim.
/// </summary>
public static class SkyModel
{
    /// <summary>Axial tilt used when the test panel forces a season (Climate's own data decides otherwise).</summary>
    public const float TestTiltDeg = 23.5f;

    /// <summary>Northern-hemisphere season names the test panel cycles through (Climate names its own).</summary>
    public static readonly string[] TestSeasons = { "Spring", "Summer", "Autumn", "Winter" };

    /// <summary>Latitude of a map row in degrees (+ = north; row 0 is the top / north edge), matching OrbitView's
    /// v 0.14–0.86 band, so a forced sun lights the same columns and rows on the surface and on the globe.</summary>
    public static float LatitudeDeg(float row, int height) => 90f - 180f * (0.14f + (row + 0.5f) / height * 0.72f);

    /// <summary>Hour angle (radians, + = afternoon, sun in the west) of a column, for a sun overhead at <paramref name="subsolar"/>.
    /// The sun moves west (toward -X) as the day goes on, as on Earth (east sees it first).</summary>
    public static float HourAngle(float column, float subsolar, int width) => Mathf.Tau * Wrap(column - subsolar, width) / width;

    /// <summary>A column difference wrapped into (-width/2, width/2].</summary>
    public static float Wrap(float columns, int width)
    {
        var w = (float)width;
        columns = Mathf.PosMod(columns, w);
        return columns > w / 2f ? columns - w : columns;
    }

    /// <summary>Sun elevation (degrees) and azimuth (degrees from map north / -Z, clockwise: 90 = east / +X).</summary>
    public static (float Elevation, float Azimuth) SunAngles(float latDeg, float decDeg, float hourAngle)
    {
        var lat = Mathf.DegToRad(latDeg);
        var dec = Mathf.DegToRad(decDeg);
        var up = Mathf.Sin(lat) * Mathf.Sin(dec) + Mathf.Cos(lat) * Mathf.Cos(dec) * Mathf.Cos(hourAngle);
        var east = -Mathf.Cos(dec) * Mathf.Sin(hourAngle);
        var north = Mathf.Cos(lat) * Mathf.Sin(dec) - Mathf.Sin(lat) * Mathf.Cos(dec) * Mathf.Cos(hourAngle);
        var elevation = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(up, -1f, 1f)));
        var azimuth = Mathf.RadToDeg(Mathf.Atan2(east, north));
        return (elevation, azimuth < 0 ? azimuth + 360f : azimuth);
    }

    /// <summary>Climate's daylight scale for a sun at this elevation: sine of the elevation, with a little twilight
    /// over the last ~6° below the horizon.</summary>
    public static float Daylight(float elevationDeg)
    {
        var s = Mathf.Sin(Mathf.DegToRad(elevationDeg));
        return Mathf.Clamp(Mathf.Max(s, 0f) + 0.04f * Mathf.SmoothStep(-0.1f, 0f, s), 0f, 1f);
    }

    /// <summary>World direction toward the sun from its elevation and azimuth.</summary>
    public static Vector3 SunDirection(float elevationDeg, float azimuthDeg)
    {
        var el = Mathf.DegToRad(elevationDeg);
        var az = Mathf.DegToRad(azimuthDeg);
        return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
    }

    /// <summary>The sunlight's colour for a daylight value: warm near the horizon, white higher up.</summary>
    public static Color Warm(float daylight)
    {
        var k = Mathf.SmoothStep(0.06f, 0.45f, Mathf.Clamp(daylight, 0f, 1f));
        return new Color(1f, Mathf.Lerp(0.56f, 1f, k), Mathf.Lerp(0.34f, 1f, k));
    }

    /// <summary>How lit a daylight value looks (sky.gdshaderinc sky_shade(): keep the two the same).</summary>
    public static Vector3 Shade(float daylight)
    {
        var d = Mathf.Clamp(daylight, 0f, 1f);
        var sunOn = Mathf.SmoothStep(0f, 0.08f, d);
        var warm = Warm(d);
        var a = Mathf.SmoothStep(0f, 0.3f, d);
        var ambient = new Vector3(Mathf.Lerp(0.065f, 0.22f, a), Mathf.Lerp(0.0845f, 0.23f, a), Mathf.Lerp(0.156f, 0.25f, a)); // = sky_shade (night floor +30%, 2026-11-10)
        return new Vector3(warm.R, warm.G, warm.B) * Mathf.Sqrt(d) * sunOn + ambient;
    }

    /// <summary>Night factor 0 (sun up) .. 1 (full night) for a daylight value.</summary>
    public static float Night(float daylight) => 1f - Mathf.SmoothStep(0f, 0.12f, daylight);

    /// <summary>The weather a forced kind brings when the test panel doesn't set it: visibility, cloud cover, wind m/s.</summary>
    public static (float Visibility, float Cloud, float Wind) Defaults(WeatherKind kind, float intensity) => kind switch
    {
        WeatherKind.Cloudy => (0.95f, 0.45f + 0.2f * intensity, 5f),
        WeatherKind.Overcast => (0.85f, 0.85f + 0.15f * intensity, 6f),
        WeatherKind.Fog => (0.35f - 0.3f * intensity, 0.7f, 1.5f),
        WeatherKind.Rain => (0.85f - 0.25f * intensity, 0.8f + 0.2f * intensity, 6f + 4f * intensity),
        WeatherKind.Storm => (0.55f - 0.25f * intensity, 1f, 14f + 8f * intensity),
        WeatherKind.Snow => (0.75f - 0.35f * intensity, 0.85f, 4f + 3f * intensity),
        WeatherKind.Blizzard => (0.3f - 0.2f * intensity, 1f, 16f + 8f * intensity),
        WeatherKind.Dust => (0.75f - 0.3f * intensity, 0.3f, 9f + 5f * intensity),
        WeatherKind.DustStorm => (0.35f - 0.25f * intensity, 0.6f, 18f + 8f * intensity),
        WeatherKind.HighWind => (0.9f, 0.3f, 15f + 8f * intensity),
        _ => (1f, 0.05f, 3f),
    };

    /// <summary>Short upper-case name for the HUD ("RAIN", "DRIZZLE", "DUST STORM", …).</summary>
    public static string Name(WeatherKind kind, float intensity) => kind switch
    {
        WeatherKind.Rain => intensity < 0.3f ? "DRIZZLE" : intensity > 0.8f ? "DOWNPOUR" : "RAIN",
        WeatherKind.Snow => intensity < 0.3f ? "LIGHT SNOW" : intensity > 0.8f ? "HEAVY SNOW" : "SNOW",
        WeatherKind.DustStorm => "DUST STORM",
        WeatherKind.HighWind => "HIGH WIND",
        _ => kind.ToString().ToUpperInvariant(),
    };

    /// <summary>Compass point for a bearing in degrees (0 = north).</summary>
    public static string Compass(float degrees)
    {
        string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return points[Mathf.PosMod(Mathf.RoundToInt(degrees / 45f), 8)];
    }
}

/// <summary>
/// The test panel's SKY overrides (F9 › SKY). Render-only: they change what the surface, the globe and the feed show,
/// never the sim (Climate keeps returning its own values, and gameplay effects follow those).
/// </summary>
public sealed class SkyOverride
{
    /// <summary>Force the local time at the view's centre to <see cref="Hour"/>.</summary>
    public bool TimeOn;
    public float Hour = 12f;
    /// <summary>While the time is forced: let it run, at this many times the planet's own day rate (0 = stopped).</summary>
    public float RunRate;

    /// <summary>Force the season (index into <see cref="SkyModel.TestSeasons"/>; sets the declination).</summary>
    public bool SeasonOn;
    public int Season = 1;

    /// <summary>Force the weather everywhere on the map.</summary>
    public bool WeatherOn;
    public WeatherKind Kind = WeatherKind.Rain;
    public float Intensity = 0.6f;

    /// <summary>Force the wind (direction it blows toward, degrees from north; speed m/s).</summary>
    public bool WindOn;
    public float WindDeg = 90f;
    public float WindMs = 8f;

    /// <summary>Bumped on any change that needs the per-tile textures baked again (season, weather).</summary>
    public int Version;

    public bool Any => TimeOn || SeasonOn || WeatherOn || WindOn;
}

/// <summary>
/// The outpost's sky for the helm's surface feed (PlanetDriver.Sky → SurfaceFeed.Climate), from the same Climate
/// values (and test-panel overrides) the surface draws: daylight 0..1, the sun's height as a sine (-1..1), the Sun
/// light's energy to use, the weather (kind, intensity, cloud cover, visibility) and the wind (direction it blows toward,
/// degrees from north; m/s).
/// </summary>
public readonly record struct FeedClimate(float Daylight, float SunSin, float SunEnergy, WeatherKind Kind, float Intensity,
    float Cloud, float Visibility, float WindDirDeg, float WindMs);
