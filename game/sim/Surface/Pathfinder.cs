using System.Collections.Generic;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// A* over the hex grid. Cost of a step = the move_cost of the tile entered. Integer costs and a
/// (cost, tile index) priority make the result identical on every machine.
/// </summary>
public static class Pathfinder
{
    /// <summary>Path from start to goal, excluding start. Null if unreachable.</summary>
    public static List<int>? Find(SurfaceMap map, int start, int goal)
    {
        if (start == goal) return new List<int>();
        if (!map.Terrain(goal).Passable) return null;

        var grid = map.Grid;
        var minCost = map.MinMoveCost; // keeps the heuristic admissible

        var g = new int[grid.Count];
        var came = new int[grid.Count];
        var closed = new bool[grid.Count];
        for (var i = 0; i < g.Length; i++)
        {
            g[i] = int.MaxValue;
            came[i] = -1;
        }

        var open = new PriorityQueue<int, (int f, int tile)>();
        g[start] = 0;
        open.Enqueue(start, (grid.Distance(start, goal) * minCost, start));

        while (open.TryDequeue(out var current, out _))
        {
            if (closed[current]) continue;
            if (current == goal) return Rebuild(came, start, goal);
            closed[current] = true;

            foreach (var n in grid.Neighbours(current))
            {
                if (closed[n]) continue;
                var terrain = map.Terrain(n);
                if (!terrain.Passable) continue;
                var cost = g[current] + terrain.MoveCost;
                if (cost >= g[n]) continue;
                g[n] = cost;
                came[n] = current;
                open.Enqueue(n, (cost + grid.Distance(n, goal) * minCost, n));
            }
        }
        return null;
    }

    private static List<int> Rebuild(int[] came, int start, int goal)
    {
        var path = new List<int>();
        for (var t = goal; t != start; t = came[t]) path.Add(t);
        path.Reverse();
        return path;
    }
}
