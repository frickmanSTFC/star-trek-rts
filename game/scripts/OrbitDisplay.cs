using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Shared contract between the helm screen (HelmScreen.cs, the frame and panels drawn over the orbit view) and the
// orbit view (OrbitView.cs, the 3D viewscreen). PlanetDriver wires the two together. Render-only: nothing here
// touches the sim.

/// <summary>What the helm viewscreen is showing (the top-bar buttons on the helm screen).</summary>
public enum OrbitDisplay
{
    Planet,
    System,
    Orbit,
    Atmosphere,
}

/// <summary>One labelled value in a helm panel ("GRAVITY" / "0.97 g"), with an optional value colour.</summary>
public sealed record HelmRow(string Label, string Value, Color? ValueColour = null);

/// <summary>A titled block of rows in the helm's data panel.</summary>
public sealed record HelmSection(string Title, Color Colour, IReadOnlyList<HelmRow> Rows);

/// <summary>One deploy or command button along the helm's bottom strip. Id is what the driver acts on.</summary>
public sealed record HelmAction(string Id, string Key, string Label, bool Enabled = true, bool Highlight = false, Color? Colour = null);

/// <summary>Something tracked in orbit (our ship, survey satellites, moons, deliveries on the way down).</summary>
public sealed record OrbitObject(string Name, string Kind, string Status, Color Colour);

/// <summary>A body in the star system plot (from the system's bodies; render-only). Name and ClassName are what the
/// player knows: a body only Detected is "Unknown body" with no class. Current = the flagship is in orbit of it;
/// Colony = the colony's surface is on it; Live = a satellite (or the colony's uplink) gives live readings there.</summary>
public sealed record SystemBody(string Id, string Name, string ClassName, int Size, Vector2 Position, string? ParentId, bool Current,
    BodyKnowledge Knowledge = BodyKnowledge.Charted, bool Live = false, bool Colony = false, string ClassId = "")
{
    public bool Charted => Knowledge == BodyKnowledge.Charted;
}

/// <summary>A ship on the system plot (render-only): id, name, owner (-1 neutral), whether it is ours, in flight.</summary>
public sealed record SystemShipMark(int Id, string Name, int Owner, bool Ours, bool InFlight);
