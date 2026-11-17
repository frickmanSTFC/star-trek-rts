using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

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

    /// <summary>
    /// A game in a star system generated from the seed (docs/system-contract.md, <see cref="SystemGenerator"/>), starting in
    /// orbit of <paramref name="homeBody"/> (a landable body of that system, by id or name, any case; null = the generator's
    /// pick: a Class M body if there is one, else Class L, else the first landable one), whose PlanetSurface is that body's
    /// (its class, its climate entry, seed <see cref="SystemGenerator.SurfaceSeed"/>). The player's flagship is in orbit of
    /// it (Charted; every other body Detected), and 3-5 neutral civilian ships fly between the bodies. Throws for a home
    /// body that isn't a landable body of this system.
    /// </summary>
    public static Simulation CreateSystemGame(GameData data, ulong seed, string faction = "federation", string? homeBody = null)
        => CreateSystemGame(data, seed, faction, homeBody, null);

    /// <summary>As the 5-argument game, with the player's fit-out (docs/fit-out-contract.md; data/missions/fit_out.json).
    /// Null = no fit-out = exactly the 5-argument game, and so is a fit-out for a ship the data doesn't fit out (the four
    /// that never land) or with no ship at all. A fit-out that fails <see cref="FitOutRules.Problem"/> is replaced by the
    /// ship's suggested preset. With one: the landing stock's replaced resources (duranium, tritanium, deuterium) come from
    /// the cargo instead of the landing's start stock, the cargo's other stock is added, its research is banked toward the
    /// first tech, its satellites launch free, its vehicles land with the colony (PlanetSurface.Touchdown), and
    /// <see cref="Simulation.FitOutFactor"/> moves the levers; the ship's own start bonus and units are added as always.</summary>
    public static Simulation CreateSystemGame(GameData data, ulong seed, string faction, string? homeBody, string? shipId, FitOut? fitOut)
    {
        if (fitOut == null || shipId == null || data.FitOut?.Ship(shipId) is not { } fitted || data.ShipChoice(shipId) is not { Colonizes: true })
            return CreateSystemGame(data, seed, faction, homeBody, shipId);
        if (FitOutRules.Problem(data, shipId, fitOut) != null) fitOut = FitOutRules.Preset(data, shipId, fitted.Suggest);
        return CreatePickedGame(data, seed, faction, homeBody, shipId, fitOut);
    }

    /// <summary>
    /// A system game with the campaign's ship pick (docs/ship-pick-contract.md): <paramref name="shipId"/> is a
    /// <see cref="GameData.ShipChoices"/> id or null (null = exactly the 4-argument game). The flagship is that ship (class
    /// "prewarp.&lt;id&gt;", its name, a "UE-nn" registry, its speed dividing flight times). A colonizer plays as before on
    /// <paramref name="homeBody"/> and gets its start bonus (resources added to the landing stock now; start units land with
    /// the colony). A ship that doesn't colonize starts in orbit of <paramref name="homeBody"/> (any body of the system, by id
    /// or name; null = the generator's landable pick), Charted; the game's surface is still the generator's landable pick,
    /// which it never lands on. Throws for an unknown ship or body.
    /// </summary>
    public static Simulation CreateSystemGame(GameData data, ulong seed, string faction, string? homeBody, string? shipId)
    {
        if (shipId == null) return CreateSystemGameCore(data, seed, faction, homeBody);
        return CreatePickedGame(data, seed, faction, homeBody, shipId, null);
    }

    private static Simulation CreatePickedGame(GameData data, ulong seed, string faction, string? homeBody, string shipId, FitOut? fitOut)
    {
        var ship = data.ShipChoice(shipId) ?? throw new ArgumentException($"No ship '{shipId}' to pick.");
        var system = SystemGenerator.Generate(data, seed);
        var home = HomeBody(data, system, ship.Colonizes ? homeBody : null);
        string? start = null;
        if (!ship.Colonizes && homeBody != null)
            start = (system.Bodies.FirstOrDefault(b => string.Equals(b.Id, homeBody, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(b.Name, homeBody, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"No body '{homeBody}' in this system.")).Id;
        var sim = new Simulation(data, seed);
        sim.LoadSurface(home.ClassId, SystemGenerator.SurfaceSeed(seed, home.Id), home.Id, system.Climate);
        var state = new StarSystemState { Star = system.Star, Bodies = system.Bodies, SurfaceBody = home.Id, Seed = seed, Generated = system };
        sim.LoadSystem(state);
        sim.SetShipChoice(0, ship.Id);
        if (fitOut != null) sim.SetFitOut(0, ship.Id, fitOut);
        if (start != null) state.SetStartBody(0, start);
        var player = sim.AddPlayer(faction);
        sim.System!.SpawnTraffic(data);
        if (fitOut != null && data.FitOut is { } rules)
        {
            // The hold instead of the landing's own metal and fuel; everything else in the start stock stays.
            foreach (var res in rules.ReplacesStartStock)
            {
                var index = data.Resource(res).Index;
                player.Stock.Add(index, -player.Stock[index]);
            }
            foreach (var s in FitOutRules.CargoStock(data, fitOut)) player.Stock.Add(data.Resource(s.Resource).Index, Fix64.FromInt(s.Amount));
            var research = FitOutRules.ResearchBanked(data, fitOut);
            if (research > 0) sim.Surface!.BankResearch(player.Slot, Fix64.FromInt(research)); // waits for the first tech (Excavation.cs)
        }
        if (ship.Colonizes)
            foreach (var (res, amount) in ship.StartBonus) // into the landing stock (StartUnits land at touchdown: PlanetSurface)
                player.Stock.Add(data.Resource(res).Index, Fix64.FromInt(amount));
        return sim;
    }

    /// <summary>Whether the system this seed generates has a living (not ruined) civilization (never on the generator's
    /// landable pick, like every system game): Meridian's system page only offers those.</summary>
    public static bool HasLivingCivilization(GameData data, ulong seed)
    {
        var system = SystemGenerator.Generate(data, seed);
        return CivGenerator.Roll(data, system.Bodies, seed, HomeBody(data, system).Id).Any(s => s.Living);
    }

    private static Simulation CreateSystemGameCore(GameData data, ulong seed, string faction, string? homeBody)
    {
        var system = SystemGenerator.Generate(data, seed);
        var home = HomeBody(data, system, homeBody);
        var sim = new Simulation(data, seed);
        sim.LoadSurface(home.ClassId, SystemGenerator.SurfaceSeed(seed, home.Id), home.Id, system.Climate);
        sim.LoadSystem(new StarSystemState { Star = system.Star, Bodies = system.Bodies, SurfaceBody = home.Id, Seed = seed, Generated = system });
        sim.AddPlayer(faction);
        sim.System!.SpawnTraffic(data);
        return sim;
    }

    /// <summary>The bodies of the system this seed generates that have a playable surface (the chooser's list), in system order.</summary>
    public static IReadOnlyList<BodyDef> LandableBodies(GameData data, ulong seed) => SystemGenerator.Generate(data, seed).Landable(data);

    /// <summary>The home body of a system game: the named one (id or name, any case), else the first Class M, else the first
    /// Class L, else the first landable body.</summary>
    public static BodyDef HomeBody(GameData data, GeneratedSystem system, string? homeBody = null)
    {
        var landable = system.Landable(data);
        if (homeBody != null)
            return landable.FirstOrDefault(b => string.Equals(b.Id, homeBody, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(b.Name, homeBody, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException(system.Body(homeBody) != null || system.Bodies.Any(b => string.Equals(b.Name, homeBody, StringComparison.OrdinalIgnoreCase))
                       ? $"Body '{homeBody}' has no surface to land on." : $"No body '{homeBody}' in this system.");
        return landable.FirstOrDefault(b => b.ClassId == MvpPlanetClass) ?? landable.FirstOrDefault(b => b.ClassId == "class_l") ?? landable[0];
    }

    /// <summary>The world the planet-first MVP starts on by default.</summary>
    public const string DefaultWorld = "tarsis";

    /// <summary>
    /// The same, on a named world of the duel map: a body's name or id ("tarsis", "veyra"; any case). The world gives the
    /// planet class (its surfaces.json planets entry) and the climate body: Veyra = class_l on body west_l (its climate,
    /// gravity and habitability tier: a Domes world). Throws for an unknown world or one with no surface yet.
    /// </summary>
    public static Simulation CreatePlanetLanding(GameData data, ulong seed, string faction, string? planetClass, string world)
    {
        var (cls, body) = ResolveWorld(data, world);
        if (planetClass != null && planetClass != cls) throw new ArgumentException($"World '{world}' is {cls}, not {planetClass}.");
        var sim = new Simulation(data, seed);
        sim.LoadSurface(cls, seed, body);
        sim.AddPlayer(faction);
        return sim;
    }

    /// <summary>A world's planet class and climate body: the first map body (mvp_duel first) whose name or id is the world,
    /// ignoring case. Throws when there is none, or its class has no surface.</summary>
    public static (string PlanetClass, string Body) ResolveWorld(GameData data, string world)
    {
        foreach (var m in data.Maps.OrderBy(m => m.Id == MvpMap ? 0 : 1))
            foreach (var b in m.Bodies)
                if (string.Equals(b.Id, world, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Name, world, StringComparison.OrdinalIgnoreCase))
                {
                    if (!data.Surfaces.Any(s => s.ClassId == b.ClassId)) throw new ArgumentException($"World '{world}' ({b.ClassId}) has no surface yet.");
                    return (b.ClassId, b.Id);
                }
        throw new ArgumentException($"Unknown world '{world}'.");
    }

    /// <summary>Whether a world name resolves to a body with a playable surface.</summary>
    public static bool IsWorld(GameData data, string world)
    {
        try
        {
            ResolveWorld(data, world);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
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
