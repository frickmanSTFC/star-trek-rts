using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Space;

/// <summary>One generated body: its map entry, the zone its orbit is in, its orbit distance from the star (for a moon, its
/// planet's) and its climate entry (synthesised from the class's climate template and the orbit).</summary>
public sealed record GeneratedBody(BodyDef Body, string Zone, Fix64 OrbitRadius, BodyClimateDef Climate)
{
    public string Id => Body.Id;
    public bool IsMoon => Body.Parent != null;
}

/// <summary>
/// A star system generated from a seed (<see cref="SystemGenerator.Generate"/>): like a maps.json map without players
/// (star, bodies with static positions in map units, star at 0, 0), plus each body's zone and climate entry.
/// </summary>
public sealed class GeneratedSystem
{
    internal GeneratedSystem(ulong seed, StarDef star, IReadOnlyList<GeneratedBody> entries, bool fallbackUsed)
    {
        Seed = seed;
        Star = star;
        Entries = entries;
        Bodies = entries.Select(e => e.Body).ToList();
        Climate = new SystemClimate(star.Class, entries.Select(e => (e.Climate, e.Body.ClassId)));
        FallbackUsed = fallbackUsed;
    }

    public ulong Seed { get; }
    public StarDef Star { get; }

    /// <summary>Planets and belts in orbit order, each followed by its moons.</summary>
    public IReadOnlyList<BodyDef> Bodies { get; }

    public IReadOnlyList<GeneratedBody> Entries { get; }

    /// <summary>The bodies' climate entries (what a surface of one of them needs: <see cref="SurfaceMap.SystemClimate"/>).</summary>
    public SystemClimate Climate { get; }

    /// <summary>No rolled body was landable, so the generator turned one into the landable_fallback class.</summary>
    public bool FallbackUsed { get; }

    public GeneratedBody? Entry(string id) => Entries.FirstOrDefault(e => e.Body.Id == id);
    public BodyDef? Body(string id) => Bodies.FirstOrDefault(b => b.Id == id);

    /// <summary>The bodies with a playable surface (surfaces.json planets), in system order.</summary>
    public IReadOnlyList<BodyDef> Landable(GameData data) => Bodies.Where(b => data.IsLandableClass(b.ClassId)).ToList();

    /// <summary>As a maps.json map (no players, no start stock), for code that wants a MapDef.</summary>
    public MapDef ToMapDef(string era) =>
        new($"system_{Seed}", Star.Name, era, 0, Star, Array.Empty<ResourceAmount>(), Array.Empty<StartDef>(), Bodies);
}

/// <summary>
/// Generates a star system from a seed with the rules in maps/generator.json (<see cref="SystemGeneratorRules"/>). Pure and
/// deterministic: its own hash of the seed (SplitMix64), never <c>sim.Rng</c>, so the same seed gives the same system on
/// every machine and generating one doesn't move the game's random stream. Class M is a rare roll; every system gets at
/// least one landable body (the landable_fallback rule).
/// </summary>
public static class SystemGenerator
{
    // Salts for the hash streams (any distinct constants).
    private const ulong StarSalt = 0x5747A12D0001UL, CountSalt = 0x5747A12D0002UL, RadiusSalt = 0x5747A12D0003UL, ClassSalt = 0x5747A12D0004UL,
        AngleSalt = 0x5747A12D0005UL, SizeSalt = 0x5747A12D0006UL, NameSalt = 0x5747A12D0007UL, MoonSalt = 0x5747A12D0008UL,
        ClimateSalt = 0x5747A12D0009UL, SurfaceSalt = 0x5747A12D000AUL;

    public static GeneratedSystem Generate(GameData data, ulong seed)
    {
        var g = data.SystemGenerator ?? throw new InvalidOperationException("The data has no system_generator (maps/generator.json).");

        // The star.
        var starClass = Pick(g.StarClasses, H(seed, StarSalt, 0));
        var starName = g.StarNames[(int)(H(seed, StarSalt, 1) % (ulong)g.StarNames.Count)];
        var star = new StarDef(starName, starClass, Fix64.FromInt(Int(H(seed, StarSalt, 2), g.StarRadiusMin, g.StarRadiusMax)));
        var luminosity = data.Climate?.Star(starClass)?.Luminosity ?? Fix64.One;
        var auScale = Fix64.Sqrt(luminosity);

        // Planets and belts, one per orbit slot.
        var names = Shuffled(g.Names, seed);
        var count = Int(H(seed, CountSalt, 0), g.PlanetsMin, g.PlanetsMax);
        var slot = (g.OrbitMax - g.OrbitMin) / count;
        var planets = new List<Planet>();
        for (var i = 0; i < count; i++)
        {
            var share = (Fix64.One - g.SlotJitter) / 2 + g.SlotJitter * Unit(H(seed, RadiusSalt, i));
            var radius = g.OrbitMin + slot * (Fix64.FromInt(i) + share);
            var zone = g.ZoneAt(radius);
            planets.Add(new Planet(i, radius, zone, Pick(zone.Weights, H(seed, ClassSalt, i)), Unit(H(seed, AngleSalt, i)), names[i]));
        }

        // At least one landable body: if nothing rolled has a surface (moons included), the habitable-zone planet nearest the
        // zone's middle becomes the fallback class (else the nearest rocky planet, else the nearest planet).
        var moonClasses = new List<List<string>>();
        foreach (var p in planets) moonClasses.Add(MoonClasses(g, seed, p));
        var fallback = false;
        if (!planets.Any(p => data.IsLandableClass(p.ClassId)) && !moonClasses.Any(m => m.Any(data.IsLandableClass)))
        {
            var hz = g.Zone(g.HabitableZone) ?? g.Zones[g.Zones.Count / 2];
            var giants = new[] { "class_j", "class_t" };
            Planet? best = null;
            foreach (var pass in new Func<Planet, bool>[]
                     {
                         p => p.Zone == hz && !g.IsBelt(p.ClassId) && !giants.Contains(p.ClassId),
                         p => !g.IsBelt(p.ClassId) && !giants.Contains(p.ClassId),
                         _ => true,
                     })
            {
                best = planets.Where(pass).OrderBy(p => Fix64.Abs(p.Radius - hz.Middle)).ThenBy(p => p.Index).FirstOrDefault();
                if (best != null) break;
            }
            best!.ClassId = g.LandableFallback;
            moonClasses[best.Index] = MoonClasses(g, seed, best);
            fallback = true;
        }

        // Bodies, climates and moons.
        var entries = new List<GeneratedBody>();
        foreach (var p in planets)
        {
            var id = $"b{p.Index + 1}";
            var turns = p.Angle;
            var pos = new FixVec2(p.Radius * Telemetry.CosTurns(turns), p.Radius * Climate.SinTurns(turns));
            var name = g.IsBelt(p.ClassId) ? p.Name + g.BeltSuffix : p.Name;
            var size = Size(g, seed, p.ClassId, p.Index * 8);
            var body = new BodyDef(id, name, p.ClassId, size, pos, null, Array.Empty<ResourceAmount>());
            var au = p.Zone.AuAt(p.Radius) * auScale;
            var climate = PlanetClimate(data, g, seed, id, p.ClassId, p.Index * 8, au);
            entries.Add(new GeneratedBody(body, p.Zone.Id, p.Radius, climate));

            var moons = moonClasses[p.Index];
            var baseTurn = Unit(H(seed, MoonSalt, p.Index * 8));
            for (var k = 0; k < moons.Count; k++)
            {
                var salt = p.Index * 8 + k + 1;
                var mt = baseTurn + Fix64.Ratio(k, moons.Count);
                var dist = g.MoonDistanceMin + (g.MoonDistanceMax - g.MoonDistanceMin) * Unit(H(seed, MoonSalt, salt)) + Fix64.FromInt(k * 6);
                var mpos = pos + new FixVec2(dist * Telemetry.CosTurns(mt), dist * Climate.SinTurns(mt));
                var mid = $"{id}{(char)('a' + k)}";
                var mbody = new BodyDef(mid, p.Name + g.MoonSuffixes[k], moons[k], Size(g, seed, moons[k], salt), mpos, id, Array.Empty<ResourceAmount>());
                entries.Add(new GeneratedBody(mbody, p.Zone.Id, p.Radius, MoonClimate(data, g, seed, mid, moons[k], salt, climate)));
            }
        }
        return new GeneratedSystem(seed, star, entries, fallback);
    }

    /// <summary>The seed of a generated body's surface: the same game seed and body always give the same map, and different
    /// bodies of one system get different maps.</summary>
    public static ulong SurfaceSeed(ulong seed, string bodyId)
    {
        var h = Mix(seed ^ SurfaceSalt);
        foreach (var ch in bodyId) h = Mix(h ^ ch);
        return h;
    }

    // ---------------------------------------------------------------- pieces

    private sealed class Planet
    {
        public Planet(int index, Fix64 radius, OrbitZone zone, string classId, Fix64 angle, string name)
        {
            Index = index;
            Radius = radius;
            Zone = zone;
            ClassId = classId;
            Angle = angle;
            Name = name;
        }

        public int Index { get; }
        public Fix64 Radius { get; }
        public OrbitZone Zone { get; }
        public string ClassId { get; set; }
        public Fix64 Angle { get; }
        public string Name { get; }
    }

    private static List<string> MoonClasses(SystemGeneratorRules g, ulong seed, Planet p)
    {
        var (min, max) = g.MoonsFor(p.ClassId);
        var n = Int(H(seed, MoonSalt ^ 0xFF, p.Index), min, max);
        var weights = g.MoonWeightsFor(p.ClassId);
        var list = new List<string>();
        for (var k = 0; k < n && weights.Count > 0; k++) list.Add(Pick(weights, H(seed, MoonSalt ^ 0xFE, p.Index * 8 + k)));
        return list;
    }

    private static int Size(SystemGeneratorRules g, ulong seed, string classId, int salt)
    {
        var (min, max) = g.SizeOf(classId);
        return Int(H(seed, SizeSalt, salt), min, max);
    }

    private static BodyClimateDef PlanetClimate(GameData data, SystemGeneratorRules g, ulong seed, string id, string classId, int salt, Fix64 au)
    {
        var c = g.ClimateOf(classId)!;
        var day = Int(H(seed, ClimateSalt, salt), c.DayMin, c.DayMax);
        var yearSeconds = Fix64.FromInt(g.YearSecondsAt1Au) * au * Fix64.Sqrt(au); // Kepler: T ∝ a^1.5
        var yearDays = Fix64.Max(Fix64.Ratio(1, 10), yearSeconds / day);
        return new BodyClimateDef(id, null, au,
            Lerp(c.EccentricityMin, c.EccentricityMax, Unit(H(seed, ClimateSalt, salt + 1))),
            Unit(H(seed, ClimateSalt, salt + 2)),
            day, DayHours(day), Unit(H(seed, ClimateSalt, salt + 3)) >= c.Prograde, SpinLock.None, Fixed0,
            Lerp(c.TiltMin, c.TiltMax, Unit(H(seed, ClimateSalt, salt + 4))),
            yearDays, Unit(H(seed, ClimateSalt, salt + 5)), c.Albedo, c.GreenhouseC,
            data.Climate?.ClassClimate(classId) ?? "airless", Array.Empty<SeasonDef>(), data.IsLandableClass(classId), "Generated.");
    }

    /// <summary>A moon: locked to its planet, sharing its orbit (and so its year) round the star.</summary>
    private static BodyClimateDef MoonClimate(GameData data, SystemGeneratorRules g, ulong seed, string id, string classId, int salt, BodyClimateDef parent)
    {
        var c = g.ClimateOf(classId)!;
        var day = Int(H(seed, ClimateSalt ^ 0xAA, salt), g.MoonDayMin, g.MoonDayMax);
        return new BodyClimateDef(id, parent.Id, Fixed0, Fixed0, Fixed0, day, DayHours(day), false, SpinLock.Parent, Fixed0,
            Lerp(c.TiltMin, c.TiltMax, Unit(H(seed, ClimateSalt ^ 0xAA, salt + 1))),
            parent.YearSeconds / day, parent.YearStart, c.Albedo, c.GreenhouseC,
            data.Climate?.ClassClimate(classId) ?? "airless", Array.Empty<SeasonDef>(), data.IsLandableClass(classId), "Generated moon.");
    }

    private static readonly Fix64 Fixed0 = Fix64.Zero;

    /// <summary>Local clock hours of a day: Tarsis's 720 s = 26 h.</summary>
    private static Fix64 DayHours(int daySeconds) => Fix64.FromInt(daySeconds * 26) / 720;

    private static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t) => a + (b - a) * t;

    // ---------------------------------------------------------------- the hash

    private static ulong Mix(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    internal static ulong H(ulong seed, ulong salt, long index) => unchecked(Mix(Mix(seed ^ salt) + (ulong)index));

    /// <summary>A whole number in [min, max].</summary>
    internal static int Int(ulong h, int min, int max) => max <= min ? min : min + (int)(h % (ulong)(max - min + 1));

    /// <summary>A share in [0, 1).</summary>
    internal static Fix64 Unit(ulong h) => Fix64.FromRaw((long)(h >> (64 - Fix64.FractionalBits)));

    internal static string Pick(IReadOnlyList<Weighted> weights, ulong h)
    {
        var total = 0L;
        foreach (var w in weights) total += System.Math.Max(0, w.Weight);
        if (total <= 0) return weights[0].Id;
        var roll = (long)(h % (ulong)total);
        foreach (var w in weights)
        {
            var x = System.Math.Max(0, w.Weight);
            if (roll < x) return w.Id;
            roll -= x;
        }
        return weights[^1].Id;
    }

    /// <summary>The list in an order shuffled by the seed (Fisher–Yates on the hash).</summary>
    internal static List<string> Shuffled(IReadOnlyList<string> items, ulong seed)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = (int)(H(seed, NameSalt, i) % (ulong)(i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
