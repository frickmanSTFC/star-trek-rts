using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A resource from resources.json. Index is its position in the file (stable ordering for bags).</summary>
public sealed record ResourceDef(int Index, string Id, string Name, string Tier);

/// <summary>An amount of one resource, referenced by resource index.</summary>
public readonly record struct ResourceAmount(int Resource, Fix64 Amount);

/// <summary>A planet class or other body type from planet_classes.json.</summary>
public sealed record PlanetClassDef(string Id, string Name, Fix64 Habitability, string ColonyType, IReadOnlyList<string> Yields)
{
    /// <summary>"full" colony classes (Class M) can be a player's home.</summary>
    public bool IsFullColony => ColonyType == "full";
}

/// <summary>A structure from buildings.json (the "buildings" list).</summary>
public sealed record BuildingDef(
    string Id,
    string Name,
    string Layer,
    string Category,
    IReadOnlyList<ResourceAmount> Cost,
    IReadOnlyList<ResourceAmount> OutputPerMinute);

public sealed record StarDef(string Name, string Class, Fix64 Radius);

/// <summary>A body on a map. Parent is the id of the body a moon circles, or null.</summary>
public sealed record BodyDef(
    string Id,
    string Name,
    string ClassId,
    int Size,
    FixVec2 Position,
    string? Parent,
    IReadOnlyList<ResourceAmount> Deposits);

public sealed record StartDef(int Slot, string HomeBodyId);

/// <summary>A skirmish map from maps.json: one star system.</summary>
public sealed record MapDef(
    string Id,
    string Name,
    string Era,
    int Players,
    StarDef Star,
    IReadOnlyList<ResourceAmount> StartStock,
    IReadOnlyList<StartDef> Starts,
    IReadOnlyList<BodyDef> Bodies);
