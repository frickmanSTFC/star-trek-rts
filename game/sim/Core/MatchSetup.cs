using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

/// <summary>
/// Starting conditions. The MVP is one era (Dominion War), two factions, single-player vs AI,
/// one star system (maps.json "mvp_duel"). Each player starts with the map's start_stock,
/// a Colony Hub on their home world, and a placeholder fleet parked beside it.
/// </summary>
public static class MatchSetup
{
    public const string MvpEra = "dominion_war";
    public const string MvpMap = "mvp_duel";
    public const string StartingBuilding = "colony_hub";
    public static readonly string[] MvpFactions = { "federation", "cardassian" };

    /// <summary>Starter fleets by faction: ship class keys ("faction.Name").</summary>
    public static readonly IReadOnlyDictionary<string, string[]> StarterFleets = new Dictionary<string, string[]>
    {
        ["federation"] = new[] { "federation.Galaxy", "federation.Defiant", "federation.Miranda", "federation.Miranda" },
        ["cardassian"] = new[] { "cardassian.Galor", "cardassian.Galor", "cardassian.Keldon", "cardassian.Hideki" },
    };

    public const string MvpPlanetClass = "class_m";

    /// <summary>
    /// The planet-first MVP: one player in orbit over a generated Class M world, about to choose a landing site.
    /// The same seed always gives the same planet.
    /// </summary>
    public static Simulation CreatePlanetLanding(GameData data, ulong seed, string faction = "federation", string planetClass = MvpPlanetClass)
    {
        var sim = new Simulation(data, seed);
        sim.LoadSurface(planetClass, seed);
        sim.AddPlayer(faction);
        return sim;
    }

    public static Simulation CreateMvpSkirmish(GameData data, ulong seed) =>
        CreateSkirmish(data, data.Map(MvpMap), MvpFactions, seed);

    public static Simulation CreateSkirmish(GameData data, MapDef map, IReadOnlyList<string> factions, ulong seed)
    {
        var sim = new Simulation(data, seed);
        sim.LoadMap(map);
        for (var slot = 0; slot < factions.Count; slot++)
        {
            var faction = factions[slot];
            sim.AddPlayer(faction);

            StartDef? start = null;
            foreach (var s in map.Starts)
                if (s.Slot == slot) start = s;
            var home = sim.FindBody(start!.HomeBodyId)!;
            sim.FoundColony(slot, home.Index, StartingBuilding);

            // Park the fleet in a column on the star-facing side of the home world, facing the star.
            var toStar = (-home.Position).Normalized();
            var side = new FixVec2(-toStar.Y, toStar.X);
            var anchor = home.Position + toStar * Fix64.FromInt(70);
            if (!StarterFleets.TryGetValue(faction, out var fleet)) continue;
            for (var i = 0; i < fleet.Length; i++)
            {
                var offset = side * Fix64.FromInt((i - fleet.Length / 2) * 25);
                sim.SpawnShip(slot, fleet[i], anchor + offset, toStar);
            }
        }
        return sim;
    }
}
