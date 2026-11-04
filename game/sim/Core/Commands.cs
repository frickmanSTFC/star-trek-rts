using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

/// <summary>
/// A player order. Commands are the only thing sent over the network in lockstep:
/// every peer applies the same commands on the same tick and gets the same game.
/// </summary>
public abstract record Command(int Player);

/// <summary>Order ships to fly to a point. Ships the player doesn't own are ignored.</summary>
public sealed record MoveCommand(int Player, IReadOnlyList<int> ShipIds, FixVec2 Target) : Command(Player);

/// <summary>Send the colony pod down to a surface tile. Only valid from orbit, on a landing site.</summary>
public sealed record LandColonyCommand(int Player, int Tile) : Command(Player);

/// <summary>Drive surface units to a tile (each finds its own path). Units the player doesn't own are ignored.</summary>
public sealed record MoveUnitsCommand(int Player, IReadOnlyList<int> UnitIds, int Tile) : Command(Player);

/// <summary>Place a building on a surface tile, facing 0–5 (60° steps). The cost is paid when placed.</summary>
public sealed record PlaceBuildingCommand(int Player, string BuildingId, int Tile, int Facing) : Command(Player);

/// <summary>Cancel an unfinished building of your own; the full cost is refunded.</summary>
public sealed record CancelBuildingCommand(int Player, int StructureId) : Command(Player);

/// <summary>Start upgrading a finished colony core to its next level (pays that level's cost). Cancel with CancelBuildingCommand.</summary>
public sealed record UpgradeCommand(int Player, int StructureId) : Command(Player);

/// <summary>Send a mining crawler to a surveyed deposit; it unfurls and starts mining when it gets there (or at once if it's on it).</summary>
public sealed record MineCommand(int Player, int UnitId, int Tile) : Command(Player);

/// <summary>Fold up deployed mining crawlers so they can drive again.</summary>
public sealed record PackUpCommand(int Player, IReadOnlyList<int> UnitIds) : Command(Player);

/// <summary>Order a unit at a finished Vehicle Bay; the cost is paid now and it joins the bay's queue.</summary>
public sealed record BuildUnitCommand(int Player, int StructureId, string UnitId) : Command(Player);

/// <summary>Cancel the last order in a Vehicle Bay's queue (the one in progress if it's the only one); full refund.</summary>
public sealed record CancelUnitCommand(int Player, int StructureId) : Command(Player);

/// <summary>Drop another Colony Hub from orbit to found a new outpost (after the first landing).</summary>
public sealed record OutpostDropCommand(int Player, int Tile) : Command(Player);

/// <summary>Fly the ship's shuttle down to a tile with a new unit (paid now).</summary>
public sealed record ShuttleCommand(int Player, int Tile, string UnitId) : Command(Player);

/// <summary>Beam a team (a unit id) or "supplies" from the hold down to a tile.</summary>
public sealed record BeamCommand(int Player, int Tile, string Payload) : Command(Player);

/// <summary>Put a survey satellite into orbit over the band of rows centred on Row.</summary>
public sealed record SatelliteCommand(int Player, int Row) : Command(Player);

/// <summary>Pick the surface tech the Research Labs work on (progress on others is kept).</summary>
public sealed record ResearchCommand(int Player, string TechId) : Command(Player);

/// <summary>Ask the base's crew to maintain a building (again to cancel, if they haven't started on it).</summary>
public sealed record MaintainCommand(int Player, int StructureId) : Command(Player);

/// <summary>Send damaged units to the nearest colony core's repair pad; repair starts when they get there.</summary>
public sealed record RepairCommand(int Player, IReadOnlyList<int> UnitIds) : Command(Player);

/// <summary>Take down a finished building (not a colony core) for part of its cost back; frees the tile.</summary>
public sealed record SalvageCommand(int Player, int StructureId) : Command(Player);
