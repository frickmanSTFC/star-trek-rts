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
