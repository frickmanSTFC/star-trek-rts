using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Ai;

/// <summary>
/// Placeholder opponent: every few seconds, send the whole fleet toward the enemy fleet's centre.
/// AI only reads sim state and returns commands, so it is deterministic and could run on every peer.
/// </summary>
public sealed class ChaseAi
{
    public int Player { get; }
    public int ThinkEveryTicks { get; }

    public ChaseAi(int player, int thinkEveryTicks = Simulation.TickRate * 5)
    {
        Player = player;
        ThinkEveryTicks = thinkEveryTicks;
    }

    /// <summary>Commands to schedule this tick (empty on ticks the AI doesn't think).</summary>
    public IReadOnlyList<Command> Think(Simulation sim)
    {
        if (sim.Tick % ThinkEveryTicks != 0) return System.Array.Empty<Command>();

        var mine = new List<int>();
        var sum = FixVec2.Zero;
        var enemies = 0;
        foreach (var s in sim.Ships)
        {
            if (s.Owner == Player) mine.Add(s.Id);
            else
            {
                sum += s.Position;
                enemies++;
            }
        }
        if (mine.Count == 0 || enemies == 0) return System.Array.Empty<Command>();

        var centre = new FixVec2(sum.X / enemies, sum.Y / enemies);
        return new Command[] { new MoveCommand(Player, mine, centre) };
    }
}
