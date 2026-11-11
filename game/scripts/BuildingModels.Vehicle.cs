using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

public static partial class BuildingModels
{
    /// <summary>The model for a ground vehicle type: a miner gets the crawler, a founder the construction rover, anything
    /// else its own rover model (survey_rover, survey_rover_mk2, survey_rover_mk3, falling back to the Mk I). The one
    /// factory PlanetView and the HUD renders (ModelThumbnails) both use. Front = -Z.</summary>
    public static Node3D Vehicle(SurfaceUnitDef def, Color owner) =>
        def.Id == "scout_drone" ? ScoutDrone(owner) : def.CanMine ? Crawler(owner) : def.CanFound ? Constructor(owner) : Rover(owner, def.Id);
}
