using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// What the missions (game/sim/Missions/) do to the surface: the same code paths as the rest of the game (grants go
// into storage through Store, research points through the dig research bank, reveals survey like a landing). The
// mission sites themselves live in the MissionBook (hashed there, only in games with missions on).
public sealed partial class PlanetSurface
{
    /// <summary>A mission grant: into storage like any income (only what fits; returns what was stored).</summary>
    internal Fix64 MissionGrant(Simulation sim, int player, int resource, Fix64 amount) =>
        player >= 0 && player < _players.Count && resource >= 0 && resource < _data.Resources.Count && resource != PowerIndex()
            ? Store(sim, player, resource, amount) : Fix64.Zero;

    /// <summary>Mission research points: into the active surface tech, the rest (or all, with none active) banked until
    /// one is started (the excavation's research bank). Returns the text for the debrief.</summary>
    internal string MissionResearch(Simulation sim, int player, Fix64 points) =>
        player >= 0 && player < _players.Count && points > Fix64.Zero ? GiveResearch(sim, player, points) : "";

    /// <summary>Surveys every tile within radius of the centre for the player (a mission's reveal reward).</summary>
    internal void MissionReveal(int player, int centre, int radius)
    {
        if (player < 0 || player >= _players.Count || !OnMap(centre)) return;
        Reveal(player, centre, System.Math.Max(0, radius));
    }

    private int PowerIndex()
    {
        foreach (var r in _data.Resources)
            if (r.Id == _data.PowerResource) return r.Index;
        return -1;
    }
}
