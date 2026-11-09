using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Civilizations phase 2 hooks on the colony surface (game/sim/Space/Contact.cs): unique techs shared by trade partners and
// their trade income.
public sealed partial class PlanetSurface
{
    /// <summary>A civilization shared a unique local tech: its modifiers count like a researched tech's from now on
    /// (ResearchBonus, UnitResearchBonus, AutoMaintained). Once per tech id.</summary>
    internal void ShareTech(int player, UniqueTechDef tech)
    {
        if (player < 0 || player >= _players.Count) return;
        var shared = _players[player]._sharedTechs;
        foreach (var t in shared)
            if (t.Id == tech.Id) return;
        shared.Add(tech);
    }

    /// <summary>Trade income into the player's stock under the storage rules (what doesn't fit is lost; latinum and other
    /// storage-exempt resources always fit). Returns how much went in.</summary>
    internal Fix64 StoreTrade(Simulation sim, int player, int resource, Fix64 amount) =>
        player >= 0 && player < _players.Count ? Store(sim, player, resource, amount) : Fix64.Zero;
}
