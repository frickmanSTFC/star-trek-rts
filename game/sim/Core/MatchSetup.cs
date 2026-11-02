using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

/// <summary>
/// Starting conditions. The MVP is one era (Dominion War), two factions, single-player vs AI,
/// one star system. Fleets are placeholders until the economy and shipyards exist.
/// </summary>
public static class MatchSetup
{
    public const string MvpEra = "dominion_war";
    public static readonly string[] MvpFactions = { "federation", "cardassian" };

    /// <summary>Starter fleets by faction: ship class keys ("faction.Name").</summary>
    public static readonly IReadOnlyDictionary<string, string[]> StarterFleets = new Dictionary<string, string[]>
    {
        ["federation"] = new[] { "federation.Galaxy", "federation.Defiant", "federation.Miranda", "federation.Miranda" },
        ["cardassian"] = new[] { "cardassian.Galor", "cardassian.Galor", "cardassian.Keldon", "cardassian.Hideki" },
    };

    /// <summary>Two players facing each other across the system, 400 units apart.</summary>
    public static Simulation CreateMvpSkirmish(GameData data, ulong seed)
    {
        var sim = new Simulation(data, seed);
        for (var player = 0; player < MvpFactions.Length; player++)
        {
            var side = player == 0 ? -1 : 1;
            var fleet = StarterFleets[MvpFactions[player]];
            for (var i = 0; i < fleet.Length; i++)
            {
                var pos = FixVec2.FromInts(side * 200, (i - fleet.Length / 2) * 30);
                sim.SpawnShip(player, fleet[i], pos, FixVec2.FromInts(-side, 0));
            }
        }
        return sim;
    }
}
