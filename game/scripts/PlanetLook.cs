using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// How a planet class looks on the surface and from orbit (render-only): the ground colour per terrain, mountain rock,
/// seabed and sea, the orbit painter's biome colours, the dust colours of storms and wheel spray, and which effects
/// run (night frost, dust devils, CO₂ ground haze). <see cref="For"/> picks by the surface's planet class
/// (<c>SurfaceMap.Def.ClassId</c>). Class M, and every class without a look of its own yet, gets <see cref="ClassM"/>,
/// whose numbers are exactly the ones the code used before classes had looks (terrain colours from the data).
/// <para>Class L (Veyra, Oscen): cold, marginal, thin CO₂-rich air: ochre-grey tundra, rust-brown hills, pale
/// lichen-green lowlands, dark basalt rock, water-ice poles. Its terrain ids are mapped onto the seven class-M roles
/// (<see cref="Role"/>), so a class-L <c>planets</c> entry can reuse plains/forest/hills/… or bring new ids
/// (tundra, lichen, regolith, ridge, dune, basalt, ice_plain…) and still get a colour, scatter and spray.</para>
/// </summary>
public sealed class PlanetLook
{
    /// <summary>The planet class this look is for ("class_m" for the default).</summary>
    public string ClassId { get; private init; } = "class_m";

    /// <summary>True for the class-M look (no per-class colours: the data's terrain colours as before).</summary>
    public bool IsDefault { get; private init; } = true;

    /// <summary>Ground colours (sRGB) by role or terrain id; a terrain missing here keeps its data colour.</summary>
    private Dictionary<string, Color> Ground { get; init; } = new();

    /// <summary>Mountain rock low → high (sRGB), and what lies above the snow line.</summary>
    public Color RockLow { get; private init; } = new(0.33f, 0.31f, 0.3f);
    public Color RockHigh { get; private init; } = new(0.5f, 0.48f, 0.46f);
    public Color Peak { get; private init; } = new(0.93f, 0.95f, 0.98f);

    /// <summary>Seabed under shallow water (sRGB, mixed 65% with the terrain colour).</summary>
    public Color Seabed { get; private init; } = new(0.36f, 0.33f, 0.24f);

    /// <summary>water.gdshader's deep / shallow colours (the shader's own defaults for class M).</summary>
    public Color WaterDeep { get; private init; } = new(0.02f, 0.09f, 0.2f, 0.92f);
    public Color WaterShallow { get; private init; } = new(0.08f, 0.3f, 0.42f, 0.8f);

    /// <summary>The orbit painter's colours (sRGB bytes written straight into the globe's albedo).</summary>
    public OrbitPalette Orbit { get; private init; } = OrbitPalette.ClassM;

    /// <summary>planet.gdshader photo_mix: how far land takes the Blue Marble photos' own colour (0.5 on class M).
    /// Earth photos would paint Veyra green, so other looks keep mostly their painted colour (photo detail only).</summary>
    public float PhotoMix { get; private init; } = 0.5f;

    /// <summary>WeatherView's blowing dust and the fog tint in a dust storm (sRGB).</summary>
    public Color StormDust { get; private init; } = new(0.78f, 0.58f, 0.36f);
    public Color FogDust { get; private init; } = new(0.7f, 0.5f, 0.3f);

    /// <summary>Rock moss (rock.gdshader moss_colour) and the frost/snow on rocks (snow_colour).</summary>
    public Color Moss { get; private init; } = new(0.24f, 0.31f, 0.12f);
    public Color RockSnow { get; private init; } = new(0.86f, 0.9f, 0.96f);

    /// <summary>Wheel spray on this world's dust and the colour the grime takes from it.</summary>
    public Color SprayDust { get; private init; } = new(0.8f, 0.66f, 0.45f, 0.55f);

    /// <summary>Night frost on the ground, rocks and plants (sky.gdshaderinc sky_frost_on).</summary>
    public bool Frost { get; private init; }

    /// <summary>Dust devils on warm, calm afternoons (PlanetView.Veyra.cs).</summary>
    public bool DustDevils { get; private init; }

    /// <summary>CO₂ ground haze pooling in low hollows on calm nights (PlanetView.Veyra.cs).</summary>
    public bool Co2Haze { get; private init; }

    /// <summary>Scatter set (PlanetView.Scatter.cs): "class_m" or "class_l".</summary>
    public string ScatterSet { get; private init; } = "class_m";

    private Dictionary<string, string> Roles { get; init; } = new();

    /// <summary>The class-M terrain a terrain id plays on this world (plains, forest, desert, hills, mountains, ice,
    /// ocean, shallows), for scatter, spray and the orbit painter. Unknown ids map to themselves.</summary>
    public string Role(string terrainId) => Roles.TryGetValue(terrainId, out var r) ? r : terrainId;

    /// <summary>The tile colour (sRGB) before the elevation lightening: this look's colour for the terrain (by id,
    /// then by role), else the data's.</summary>
    public Color TerrainColour(string terrainId, string dataColour)
    {
        if (!IsDefault && (Ground.TryGetValue(terrainId, out var c) || Ground.TryGetValue(Role(terrainId), out c))) return c;
        return Color.FromHtml(dataColour);
    }

    private static readonly Dictionary<string, PlanetLook> Cache = new();

    /// <summary>The look for a planet class (class M's for any class without one).</summary>
    public static PlanetLook For(string? classId)
    {
        classId ??= "class_m";
        lock (Cache)
        {
            if (Cache.TryGetValue(classId, out var look)) return look;
            look = classId switch
            {
                "class_l" => ClassL(),
                _ => new PlanetLook { ClassId = classId },
            };
            Cache[classId] = look;
            return look;
        }
    }

    /// <summary>Class M (and the fallback): the data's colours, exactly as before.</summary>
    public static PlanetLook ClassM => For("class_m");

    private static PlanetLook ClassL() => new()
    {
        ClassId = "class_l",
        IsDefault = false,
        Ground = new Dictionary<string, Color>
        {
            ["plains"] = new(0.6f, 0.53f, 0.4f),     // ochre-grey tundra
            ["forest"] = new(0.55f, 0.58f, 0.44f),   // pale lichen-green lowlands
            ["desert"] = new(0.66f, 0.5f, 0.34f),    // dry ochre dust flats
            ["hills"] = new(0.52f, 0.36f, 0.25f),    // rust-brown hills
            ["mountains"] = new(0.24f, 0.23f, 0.23f), // dark basalt (the mesh paints RockColour over it)
            ["ice"] = new(0.86f, 0.89f, 0.9f),       // water ice, a little dusty
            ["ocean"] = new(0.2f, 0.25f, 0.26f),     // cold, dark brine
            ["shallows"] = new(0.3f, 0.35f, 0.33f),
        },
        Roles = new Dictionary<string, string>
        {
            ["tundra"] = "plains", ["regolith"] = "plains", ["steppe"] = "plains",
            ["lichen"] = "forest", ["lowland"] = "forest", ["marsh"] = "forest", ["fen"] = "forest",
            ["dune"] = "desert", ["dust"] = "desert", ["sand"] = "desert", ["flats"] = "desert",
            ["ridge"] = "hills", ["crater"] = "hills", ["badlands"] = "hills",
            ["basalt"] = "mountains", ["massif"] = "mountains",
            ["ice_plain"] = "ice", ["ice_ridge"] = "ice", ["polar_ice"] = "ice", ["frost"] = "ice",
            ["brine"] = "ocean", ["sea"] = "ocean",
        },
        RockLow = new(0.19f, 0.18f, 0.18f),
        RockHigh = new(0.32f, 0.29f, 0.27f),
        Peak = new(0.86f, 0.89f, 0.92f),
        Seabed = new(0.24f, 0.22f, 0.2f),
        WaterDeep = new(0.02f, 0.06f, 0.08f, 0.94f),
        WaterShallow = new(0.1f, 0.2f, 0.22f, 0.84f),
        Orbit = OrbitPalette.ClassL,
        PhotoMix = 0.12f,
        StormDust = new(0.66f, 0.4f, 0.24f),
        FogDust = new(0.6f, 0.37f, 0.22f),
        Moss = new(0.46f, 0.5f, 0.36f),          // pale lichen on the boulders
        RockSnow = new(0.84f, 0.88f, 0.92f),     // frost rims
        SprayDust = new(0.62f, 0.42f, 0.28f, 0.55f),
        Frost = true,
        DustDevils = true,
        Co2Haze = true,
        ScatterSet = "class_l",
    };
}

/// <summary>The orbit painter's colours for one look (sRGB). <see cref="ClassM"/> holds the numbers OrbitPainter used
/// before looks existed.</summary>
public sealed record OrbitPalette(
    Color PlainsDry, Color PlainsLush, Color ForestDry, Color ForestLush, Color DesertLow, Color DesertHigh,
    Color HillsDry, Color HillsLush, Color Mountains, Color Ice, Color MountRock, Color Snow, Color Beach,
    Color SeaDeep, Color SeaMid, Color SeaCoast, Color CapLow, Color CapHigh, Color CapCrevasse)
{
    public static readonly OrbitPalette ClassM = new(
        new(0.58f, 0.6f, 0.33f), new(0.25f, 0.45f, 0.17f), new(0.16f, 0.31f, 0.12f), new(0.06f, 0.2f, 0.08f),
        new(0.84f, 0.71f, 0.49f), new(0.72f, 0.53f, 0.33f), new(0.5f, 0.44f, 0.31f), new(0.37f, 0.39f, 0.24f),
        new(0.4f, 0.37f, 0.34f), new(0.9f, 0.93f, 0.97f), new(0.42f, 0.39f, 0.36f), new(0.95f, 0.96f, 0.99f),
        new(0.83f, 0.77f, 0.57f), new(0.015f, 0.05f, 0.16f), new(0.04f, 0.18f, 0.38f), new(0.07f, 0.36f, 0.5f),
        new(0.8f, 0.85f, 0.91f), new(0.97f, 0.98f, 1f), new(0.66f, 0.74f, 0.85f));

    /// <summary>Veyra: ochre-grey tundra, pale lichen lowlands, rust hills and dust, basalt, frosty water-ice caps,
    /// cold dark seas.</summary>
    public static readonly OrbitPalette ClassL = new(
        new(0.6f, 0.53f, 0.4f), new(0.52f, 0.52f, 0.38f), new(0.56f, 0.58f, 0.44f), new(0.46f, 0.52f, 0.38f),
        new(0.68f, 0.48f, 0.31f), new(0.58f, 0.38f, 0.24f), new(0.52f, 0.35f, 0.24f), new(0.46f, 0.33f, 0.24f),
        new(0.23f, 0.22f, 0.21f), new(0.86f, 0.88f, 0.9f), new(0.24f, 0.22f, 0.21f), new(0.86f, 0.88f, 0.91f),
        new(0.58f, 0.5f, 0.38f), new(0.02f, 0.045f, 0.06f), new(0.05f, 0.11f, 0.14f), new(0.12f, 0.22f, 0.24f),
        new(0.8f, 0.82f, 0.84f), new(0.95f, 0.96f, 0.97f), new(0.62f, 0.68f, 0.74f));
}
