using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A surface terrain type from surfaces.json. Index = position in the file.</summary>
public sealed record TerrainDef(int Index, string Id, string Name, string Color, bool Passable, bool Buildable, int MoveCost, int Height);

/// <summary>Lowland terrain picked by moisture share: the first band whose Below is above the tile's moisture.</summary>
public sealed record LowlandBand(Fix64 Below, string TerrainId);

/// <summary>A deposit rule: on matching terrain, roll Chance per tile; amount in [Min, Max].</summary>
public sealed record DepositRule(string ResourceId, IReadOnlyList<string> Terrain, bool Coastal, Fix64 Chance, int Min, int Max);

/// <summary>How to generate the surface of one planet class. Elevation thresholds are shares of the map.</summary>
public sealed record PlanetSurfaceDef(
    string ClassId,
    int Width,
    int Height,
    int FeatureSize,
    Fix64 OceanBelow,
    Fix64 ShallowsBelow,
    Fix64 HillsFrom,
    Fix64 MountainsFrom,
    IReadOnlyList<LowlandBand> Lowland,
    int PolarRows,
    string PolarTerrain,
    int RegionColumns,
    int RegionRows,
    IReadOnlyList<DepositRule> Deposits);

public sealed record HintRules(int Likely, int Rich);

public sealed record LandingRules(
    IReadOnlyList<string> Terrain,
    int DescentSeconds,
    IReadOnlyList<ResourceAmount> StartStock,
    int RevealRadius,
    string HubBuilding,
    IReadOnlyList<string> Units);

/// <summary>
/// Surface stats for a ground vehicle (the vehicle itself is in vehicles.json). A unit with ExtractPerMinute
/// is a mining crawler: it unfurls on a deposit in DeploySeconds (and packs up just as long), fills a hopper of
/// Hopper, and sends each full load home as a cargo pod flying PodSpeed tiles per second.
/// </summary>
public sealed record SurfaceUnitDef(string Id, Fix64 Speed, int SurveyRadius, Fix64 ExtractPerMinute, Fix64 Hopper, int DeploySeconds, Fix64 PodSpeed)
{
    public bool CanMine => ExtractPerMinute > Fix64.Zero;
}
