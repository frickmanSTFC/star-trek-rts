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
