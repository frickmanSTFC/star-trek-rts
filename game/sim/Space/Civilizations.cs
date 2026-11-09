using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Space;

// Civilizations phase 1 (docs/civilizations-contract.md; design doc "Civilizations & First Contact"; data/civilizations/).
// Sites are rolled with the system from its seed (CivGenerator: pure SplitMix, never sim.Rng), never on the colony's landing
// body. What a player knows follows the system fog plus study from orbit; hidden observation posts roll for discovery on
// sim.Rng every discovery_minutes of game time. Awareness, study, posts, Standing, incidents, the observation research bank
// and the event list are in the state hash (only system games have a StarSystemState, so planet-only hashes are unchanged).

/// <summary>Development level of a living civilization (L1..L6).</summary>
public enum CivLevel { None = 0, StoneBronze = 1, IronMedieval = 2, Industrial = 3, Atomic = 4, EarlyWarp = 5, Spacefaring = 6 }

public enum Temperament { Unknown, Peaceful, Wary, Aggressive }

public enum CivUnity { Unknown, Unified, Fractured }

/// <summary>A civilization rolled with the system (pure: from the system seed). Living = false: ruins of a dead one (Level =
/// how far it got; Temperament/Unity Unknown, no nations or population).</summary>
public sealed record CivSite(string BodyId, string Species, bool Living, CivLevel Level, Temperament Temperament, CivUnity Unity,
    int Nations, long Population, int Seed)
{
    /// <summary>Ruins: the ruins.json kind id; null for a living civilization.</summary>
    public string? Ruin { get; init; }

    /// <summary>The species' name root (nation names are made from roots like it).</summary>
    public string Root { get; init; } = "";
}

/// <summary>A native town on the body's look-only surface map (<see cref="StarSystemState.TownMap"/>), for the globe and the feed.
/// Size 1..3; Lit = shows lights at night (L3+). The first town is the largest settlement (the capital).</summary>
public readonly record struct NativeTown(int Tile, int Size, bool Lit);

/// <summary>What one player knows about the civilization on a body. LevelMin..LevelMax narrows to one level once studied
/// (Study = 1); Temperament/Unity are Unknown until then. Signals = true at Detected/Scanned ("possible civilization").</summary>
public sealed record CivReport(string BodyId, bool Signals, bool Charted, bool Living, string? Species, CivLevel LevelMin, CivLevel LevelMax,
    Temperament Temperament, CivUnity Unity, int Nations, long Population, int Awareness, Fix64 Study, bool Live, bool PostHere,
    bool PostFound)
{
    /// <summary>Charted ruins: the ruin kind's name ("Buried city").</summary>
    public string? RuinName { get; init; }

    /// <summary>Charted: the awareness band's name ("Unaware", "Rumours", …).</summary>
    public string AwarenessBand { get; init; } = "";

    /// <summary>Studied: the nations' names (one for a unified world).</summary>
    public IReadOnlyList<string> NationNames { get; init; } = System.Array.Empty<string>();

    /// <summary>Charted: what orbital sensors pick up (the level range's signs; the exact level's once studied).</summary>
    public IReadOnlyList<string> OrbitalSigns { get; init; } = System.Array.Empty<string>();

    /// <summary>Charted: incidents this player has had on the body.</summary>
    public int Incidents { get; init; }
}

/// <summary>A hidden observation post (<see cref="ObservationPostCommand"/>). Found = the natives found it: it stops working
/// (no study, research or rolls) and stays listed.</summary>
public sealed class ObservationPost
{
    public required int Owner { get; init; }
    public required string BodyId { get; init; }
    public required long PlacedTick { get; init; }
    /// <summary>The tick of its next discovery roll.</summary>
    public long NextRollTick { get; internal set; }
    public bool Found { get; internal set; }
    public long FoundTick { get; internal set; } = -1;
}

/// <summary>A line for the view's feed. Player = whose it is (every event is addressed to one player). Kind: "study_complete",
/// "post_placed", "post_found", "incident", "standing" (a step reached; Text names it), "recalled", "awareness" (the natives
/// of a body the player has Charted moved up an awareness band).</summary>
public sealed record CivEvent(long Tick, int Player, string BodyId, string Kind, string Text);

/// <summary>Rolls the civilizations of a system and the parts of one that never change (names, towns). Pure.</summary>
public static class CivGenerator
{
    private const ulong LivingSalt = 0xC1F1_0001UL, RuinsSalt = 0xC1F1_0002UL, SiteSalt = 0xC1F1_0003UL, NameSalt = 0xC1F1_0004UL,
        TownSalt = 0xC1F1_0005UL, NationSalt = 0xC1F1_0006UL, RangeSalt = 0xC1F1_0007UL;

    /// <summary>The sites of a system: at most one living civilization (rules.json sites.living_chance) and one ruins site
    /// (ruins_chance), each a weighted pick among the bodies whose class has a weight, never <paramref name="exclude"/> (the
    /// colony's landing body) and never both on one body. In body order. Empty without civilizations data.</summary>
    public static List<CivSite> Roll(GameData data, IReadOnlyList<BodyDef> bodies, ulong seed, string? exclude)
    {
        var list = new List<CivSite>();
        if (data.Civilizations is not { } c) return list;
        int living = -1, ruins = -1;
        if (SystemGenerator.Unit(SystemGenerator.H(seed, LivingSalt, 0)) < c.Sites.LivingChance)
        {
            living = PickBody(bodies, c.Sites.LivingWeights, exclude, -1, SystemGenerator.H(seed, LivingSalt, 1));
            if (living >= 0) list.Add(Living(data, c, bodies[living], SystemGenerator.H(seed, SiteSalt, living)));
        }
        if (SystemGenerator.Unit(SystemGenerator.H(seed, RuinsSalt, 0)) < c.Sites.RuinsChance)
        {
            ruins = PickBody(bodies, c.Sites.RuinsWeights, exclude, living, SystemGenerator.H(seed, RuinsSalt, 1));
            if (ruins >= 0) list.Add(Ruins(c, bodies[ruins], SystemGenerator.H(seed, SiteSalt, ruins)));
        }
        if (living >= 0 && ruins >= 0 && ruins < living) list.Reverse();
        return list;
    }

    private static int PickBody(IReadOnlyList<BodyDef> bodies, IReadOnlyList<Weighted> weights, string? exclude, int taken, ulong h)
    {
        long total = 0;
        for (var i = 0; i < bodies.Count; i++)
            if (i != taken && bodies[i].Id != exclude) total += WeightOf(weights, bodies[i].ClassId);
        if (total <= 0) return -1;
        var roll = (long)(h % (ulong)total);
        for (var i = 0; i < bodies.Count; i++)
        {
            if (i == taken || bodies[i].Id == exclude) continue;
            var w = WeightOf(weights, bodies[i].ClassId);
            if (roll < w) return i;
            roll -= w;
        }
        return -1;
    }

    private static int WeightOf(IReadOnlyList<Weighted> weights, string classId)
    {
        foreach (var w in weights)
            if (w.Id == classId) return System.Math.Max(0, w.Weight);
        return 0;
    }

    private static int PickIndex(IReadOnlyList<int> weights, ulong h)
    {
        long total = 0;
        foreach (var w in weights) total += System.Math.Max(0, w);
        if (total <= 0) return 0;
        var roll = (long)(h % (ulong)total);
        for (var i = 0; i < weights.Count; i++)
        {
            var w = System.Math.Max(0, weights[i]);
            if (roll < w) return i;
            roll -= w;
        }
        return weights.Count - 1;
    }

    private static CivSite Living(GameData data, CivilizationData c, BodyDef body, ulong h)
    {
        var s = c.Sites;
        var level = PickIndex(s.LevelWeightsFor(body.ClassId), SystemGenerator.H(h, SiteSalt, 1)) + 1;
        var temperament = SystemGenerator.Pick(s.TemperamentWeights, SystemGenerator.H(h, SiteSalt, 2)) switch
        {
            "peaceful" => Temperament.Peaceful,
            "aggressive" => Temperament.Aggressive,
            _ => Temperament.Wary,
        };
        var unifiedChance = level - 1 < s.UnifiedChance.Count ? s.UnifiedChance[level - 1] : Fix64.Half;
        var unified = SystemGenerator.Unit(SystemGenerator.H(h, SiteSalt, 3)) < unifiedChance;
        var nations = unified ? 1 : SystemGenerator.Int(SystemGenerator.H(h, SiteSalt, 4), s.NationsMin, s.NationsMax);
        var l = c.Level(level);
        var span = (ulong)System.Math.Max(0, l.PopulationMax - l.PopulationMin) + 1;
        var population = l.PopulationMin + (long)(SystemGenerator.H(h, SiteSalt, 5) % span);
        var root = Root(c.Names, SystemGenerator.H(h, NameSalt, 0));
        return new CivSite(body.Id, Species(c.Names, root, SystemGenerator.H(h, NameSalt, 1)), true, (CivLevel)level, temperament,
            unified ? CivUnity.Unified : CivUnity.Fractured, nations, population, Seed32(h)) { Root = root };
    }

    private static CivSite Ruins(CivilizationData c, BodyDef body, ulong h)
    {
        var level = PickIndex(c.Sites.RuinsLevels, SystemGenerator.H(h, SiteSalt, 1)) + 1;
        var kinds = c.Ruins.Select(r => new Weighted(r.Id, r.Weight)).ToList();
        var kind = kinds.Count > 0 ? c.Ruin(SystemGenerator.Pick(kinds, SystemGenerator.H(h, SiteSalt, 6))) : null;
        var root = Root(c.Names, SystemGenerator.H(h, NameSalt, 0));
        var species = kind?.Species ?? Species(c.Names, root, SystemGenerator.H(h, NameSalt, 1));
        return new CivSite(body.Id, species, false, (CivLevel)level, Temperament.Unknown, CivUnity.Unknown, 0, 0, Seed32(h))
        {
            Ruin = kind?.Id, Root = root,
        };
    }

    private static int Seed32(ulong h) => (int)(h >> 33);

    /// <summary>A generated name root ("Kethar"): syllables of onset + vowel, the last with a coda.</summary>
    public static string Root(CivNameRules n, ulong h)
    {
        if (n.Onsets.Count == 0 || n.Vowels.Count == 0) return "Unknown";
        var count = SystemGenerator.Int(SystemGenerator.H(h, NameSalt, 0), System.Math.Max(1, n.SyllablesMin), System.Math.Max(1, n.SyllablesMax));
        var sb = new StringBuilder();
        for (var k = 0; k < count; k++)
        {
            sb.Append(n.Onsets[(int)(SystemGenerator.H(h, NameSalt, 1 + k * 3) % (ulong)n.Onsets.Count)].ToLowerInvariant());
            sb.Append(n.Vowels[(int)(SystemGenerator.H(h, NameSalt, 2 + k * 3) % (ulong)n.Vowels.Count)]);
        }
        if (n.Codas.Count > 0) sb.Append(n.Codas[(int)(SystemGenerator.H(h, NameSalt, 99) % (ulong)n.Codas.Count)]);
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    private static string Species(CivNameRules n, string root, ulong h) =>
        n.Endings.Count == 0 ? root : root + n.Endings[(int)(h % (ulong)n.Endings.Count)];

    /// <summary>The nations of a living civilization (one, from the species' root, for a unified world); empty for ruins.</summary>
    public static IReadOnlyList<string> NationNames(GameData data, CivSite site)
    {
        var list = new List<string>();
        if (!site.Living || data.Civilizations is not { } c || c.Names.NationForms.Count == 0) return list;
        var forms = c.Names.NationForms;
        var h = SystemGenerator.H((ulong)(uint)site.Seed, NationSalt, 0);
        for (var k = 0; k < site.Nations; k++)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var root = k == 0 && site.Unity == CivUnity.Unified ? site.Root : Root(c.Names, SystemGenerator.H(h, NationSalt, 1 + k * 16 + attempt));
                var form = forms[(int)(SystemGenerator.H(h, NationSalt, 1000 + k * 16 + attempt) % (ulong)forms.Count)];
                var name = form.Replace("{root}", root);
                if (list.Contains(name) && attempt < 7) continue;
                list.Add(name);
                break;
            }
        }
        return list;
    }

    /// <summary>The level range a player sees before study: rules.json sites.level_range levels wide, holding the true level
    /// (where in the range: by the site's seed), clamped to L1..L6.</summary>
    public static (CivLevel Min, CivLevel Max) LevelRange(GameData data, CivSite site)
    {
        var width = System.Math.Clamp(data.Civilizations?.Sites.LevelRange ?? 2, 1, GameData.CivLevels);
        var level = (int)site.Level;
        var below = (int)(SystemGenerator.H((ulong)(uint)site.Seed, RangeSalt, 0) % (ulong)width);
        var min = System.Math.Clamp(level - below, 1, GameData.CivLevels - width + 1);
        return ((CivLevel)min, (CivLevel)(min + width - 1));
    }

    /// <summary>The towns of a living civilization on its look-only map: levels.json towns / town_size by level, on land tiles
    /// picked by rules.json town_terrain (+ coastal_bonus next to water), kept town_spacing tiles apart (less if the map has
    /// no room). Empty for ruins.</summary>
    public static List<NativeTown> Towns(GameData data, CivSite site, SurfaceMap map)
    {
        var towns = new List<NativeTown>();
        if (!site.Living || data.Civilizations is not { } c) return towns;
        var level = c.Level((int)site.Level);
        var h = SystemGenerator.H((ulong)(uint)site.Seed, TownSalt, 0);
        var count = SystemGenerator.Int(SystemGenerator.H(h, TownSalt, 0), level.TownsMin, level.TownsMax);

        // Candidate land tiles with their weights (prefix sums for weighted picks).
        var tiles = new List<int>();
        var prefix = new List<long>();
        long total = 0;
        for (var t = 0; t < map.Grid.Count; t++)
        {
            var terrain = map.Terrain(t);
            if (!terrain.Passable || map.IsWater(t)) continue;
            var w = 0;
            foreach (var x in c.Sites.TownTerrain)
                if (x.Id == terrain.Id) w = System.Math.Max(0, x.Weight);
            if (w == 0) continue;
            if (map.TouchesWater(t)) w += c.Sites.CoastalBonus;
            total += w;
            tiles.Add(t);
            prefix.Add(total);
        }
        if (total <= 0) return towns;

        var spacing = System.Math.Max(1, c.Sites.TownSpacing);
        var taken = new List<int>();
        var attempt = 0;
        while (taken.Count < count && spacing >= 1)
        {
            for (var k = 0; k < 64 && taken.Count < count; k++, attempt++)
            {
                var roll = (long)(SystemGenerator.H(h, TownSalt, 1 + attempt) % (ulong)total);
                var i = prefix.BinarySearch(roll + 1);
                if (i < 0) i = ~i;
                var tile = tiles[System.Math.Min(i, tiles.Count - 1)];
                var clear = true;
                foreach (var other in taken)
                    if (map.Grid.Distance(other, tile) < spacing)
                    {
                        clear = false;
                        break;
                    }
                if (clear) taken.Add(tile);
            }
            if (taken.Count < count) spacing -= 2;
        }
        for (var k = 0; k < taken.Count; k++)
        {
            var size = k == 0 ? level.SizeMax : SystemGenerator.Int(SystemGenerator.H(h, TownSalt, 10000 + k), level.SizeMin, level.SizeMax);
            towns.Add(new NativeTown(taken[k], size, level.Lit));
        }
        return towns;
    }
}

public sealed partial class StarSystemState
{
    /// <summary>One player's civilization state: study per site, Starfleet Standing and its steps, incidents, the bank of
    /// observation research waiting for a surface tech.</summary>
    private sealed class CivPlayer
    {
        public Fix64[] Study = System.Array.Empty<Fix64>();
        public bool HasStanding;
        public Fix64 Standing;
        /// <summary>The step whose consequences were last applied (0..5).</summary>
        public int Rung;
        public bool Recalled;
        public long LastIncidentTick = -1;
        public long HeldUntilTick;
        public long SanctionsUntilTick;
        public Fix64 Bank;
        /// <summary>Incidents per body, in the order they first happened.</summary>
        public readonly List<(string Body, int Count)> Incidents = new();
    }

    private List<CivSite> _civs = new();
    private Fix64[] _awareness = System.Array.Empty<Fix64>();
    private readonly List<CivPlayer> _civPlayers = new();
    private readonly List<ObservationPost> _posts = new();
    private readonly List<CivEvent> _civEvents = new();

    // Pure caches (not state): look-only maps and their towns, per data set.
    private GameData? _townData;
    private readonly Dictionary<string, SurfaceMap?> _townMaps = new(System.StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<NativeTown>> _towns = new(System.StringComparer.Ordinal);

    private CivilizationData? CivData => Data?.Civilizations;

    /// <summary>Every civilization site in the system (living and ruins), by body order. Never the colony's landing body.</summary>
    public IReadOnlyList<CivSite> Civilizations => _civs;

    /// <summary>The site on a body, or null (the truth: the view shows <see cref="CivView"/> instead).</summary>
    public CivSite? CivAt(string bodyId)
    {
        foreach (var c in _civs)
            if (c.BodyId == bodyId) return c;
        return null;
    }

    /// <summary>Hidden observation posts, in the order they were placed (found ones stay).</summary>
    public IReadOnlyList<ObservationPost> Posts => _posts;

    /// <summary>Feed lines for the view, oldest first (each addressed to one player).</summary>
    public IReadOnlyList<CivEvent> CivEvents => _civEvents;

    /// <summary>What the player knows about the body's civilization, or null when there is none or nothing is known yet
    /// (L1–L2 and ruins give no signals: nothing shows before the body is Charted).</summary>
    public CivReport? CivView(int player, string bodyId)
    {
        var i = CivIndex(bodyId);
        var data = Data;
        if (i < 0 || data?.Civilizations is not { } c || player < 0) return null;
        var site = _civs[i];
        var knowledge = KnowledgeOf(player, bodyId);
        if (knowledge == BodyKnowledge.Unknown) return null;
        if (knowledge < BodyKnowledge.Charted)
        {
            if (!site.Living || !c.Level((int)site.Level).Signals) return null;
            return new CivReport(bodyId, true, false, true, null, CivLevel.None, CivLevel.None, Temperament.Unknown, CivUnity.Unknown, 0, 0, 0,
                Fix64.Zero, false, false, false);
        }
        var postHere = _posts.Any(p => p.Owner == player && p.BodyId == bodyId && !p.Found);
        var postFound = _posts.Any(p => p.Owner == player && p.BodyId == bodyId && p.Found);
        var live = HasLiveData(player, bodyId) || postHere;
        var incidents = IncidentsOn(player, bodyId);
        if (!site.Living)
            return new CivReport(bodyId, false, true, false, site.Species, CivLevel.None, CivLevel.None, Temperament.Unknown, CivUnity.Unknown, 0, 0, 0,
                Fix64.Zero, live, postHere, postFound) { RuinName = c.Ruin(site.Ruin)?.Name ?? "Ruins", Incidents = incidents };
        var study = StudyOf(player, bodyId);
        var studied = study >= Fix64.One;
        var (min, max) = studied ? (site.Level, site.Level) : CivGenerator.LevelRange(data, site);
        var signs = new List<string>();
        for (var l = (int)min; l <= (int)max; l++)
            foreach (var s in c.Level(l).OrbitalSigns)
                if (!signs.Contains(s)) signs.Add(s);
        var awareness = AwarenessOf(bodyId);
        return new CivReport(bodyId, false, true, true, site.Species, min, max, studied ? site.Temperament : Temperament.Unknown,
            studied ? site.Unity : CivUnity.Unknown, studied ? site.Nations : 0, site.Population, awareness, study, live, postHere, postFound)
        {
            AwarenessBand = c.Awareness.BandOf(awareness).Name,
            NationNames = studied ? CivGenerator.NationNames(data, site) : System.Array.Empty<string>(),
            OrbitalSigns = signs,
            Incidents = incidents,
        };
    }

    /// <summary>How far the player's study of the body's civilization is (0..1; kept).</summary>
    public Fix64 StudyOf(int player, string bodyId)
    {
        var i = CivIndex(bodyId);
        if (i < 0 || player < 0 || player >= _civPlayers.Count) return Fix64.Zero;
        var study = _civPlayers[player].Study;
        return i < study.Length ? study[i] : Fix64.Zero;
    }

    /// <summary>The natives' awareness of aliens, 0..100 (shared by every visitor; only rises).</summary>
    public int AwarenessOf(string bodyId)
    {
        var i = CivIndex(bodyId);
        return i < 0 || i >= _awareness.Length ? 0 : System.Math.Clamp(_awareness[i].FloorToInt(), 0, 100);
    }

    /// <summary>The look-only surface map a civilization's towns sit on: the body's own class's surface, or the rules.json
    /// town_maps stand-in for a class without one, generated from <see cref="SystemGenerator.SurfaceSeed"/>. Null for a body
    /// without a living civilization. Pure; cached per body.</summary>
    public SurfaceMap? TownMap(string bodyId)
    {
        var data = Data;
        var site = CivAt(bodyId);
        if (data?.Civilizations is not { } c || site is not { Living: true } || Body(bodyId) is not { } body) return null;
        Recache(data);
        if (_townMaps.TryGetValue(bodyId, out var cached)) return cached;
        var cls = data.IsLandableClass(body.ClassId) ? body.ClassId : c.Sites.TownMapFor(body.ClassId);
        SurfaceMap? map = null;
        if (cls != null && data.IsLandableClass(cls))
            map = SurfaceMap.Generate(data, data.Surface(cls), SystemGenerator.SurfaceSeed(Seed, bodyId), Generated != null ? bodyId : null, Generated?.Climate);
        _townMaps[bodyId] = map;
        return map;
    }

    /// <summary>Native towns on the body's look-only map (<see cref="TownMap"/>; empty for ruins and no civ). Pure; cached per body.</summary>
    public IReadOnlyList<NativeTown> TownsOn(string bodyId)
    {
        var data = Data;
        var site = CivAt(bodyId);
        if (data == null || site is not { Living: true }) return System.Array.Empty<NativeTown>();
        Recache(data);
        if (_towns.TryGetValue(bodyId, out var cached)) return cached;
        var map = TownMap(bodyId);
        IReadOnlyList<NativeTown> towns = map == null ? System.Array.Empty<NativeTown>() : CivGenerator.Towns(data, site, map);
        _towns[bodyId] = towns;
        return towns;
    }

    /// <summary>The nations of a body's civilization (the truth; <see cref="CivReport.NationNames"/> is what a player knows).</summary>
    public IReadOnlyList<string> NationNamesOf(string bodyId) =>
        CivAt(bodyId) is { } site && Data is { } data ? CivGenerator.NationNames(data, site) : System.Array.Empty<string>();

    private void Recache(GameData data)
    {
        if (ReferenceEquals(_townData, data)) return;
        _townData = data;
        _townMaps.Clear();
        _towns.Clear();
    }

    /// <summary>The match era has the Prime Directive (its first year is at or after rules.json standing.prime_directive_from_year;
    /// an era the data doesn't list counts as having it).</summary>
    public bool PrimeDirective
    {
        get
        {
            var data = Data;
            if (data?.Civilizations is not { } c) return false;
            var era = data.Orbit?.Era is { } id ? data.Era(id) : null;
            return era == null || era.YearFrom >= c.Standing.PrimeDirectiveFromYear;
        }
    }

    /// <summary>Starfleet Standing 0..100 for a Federation player in an era with the Prime Directive (null otherwise).</summary>
    public int? StandingOf(int player) =>
        player >= 0 && player < _civPlayers.Count && _civPlayers[player].HasStanding
            ? System.Math.Clamp(_civPlayers[player].Standing.FloorToInt(), 0, 100) : null;

    /// <summary>The consequence step reached: 0 none, 1 warning (&lt;70), 2 inquiry (&lt;50), 3 relieved (&lt;30), 4 sanctions (&lt;10),
    /// 5 recalled for good (0, or a third incident on one world).</summary>
    public int StandingRung(int player)
    {
        if (player < 0 || player >= _civPlayers.Count || !_civPlayers[player].HasStanding) return 0;
        var p = _civPlayers[player];
        return p.Recalled ? 5 : RungFor(p.Standing);
    }

    /// <summary>A step's name for the chip and the ladder: "In good standing", "Warning", "Inquiry", "Captain relieved",
    /// "Sanctions", "Recalled".</summary>
    public static string RungName(int rung) => rung switch
    {
        1 => "Warning",
        2 => "Inquiry",
        3 => "Captain relieved",
        4 => "Sanctions",
        5 => "Recalled",
        _ => "In good standing",
    };

    /// <summary>The player was recalled to Starfleet for good (Standing step 5): no more ship orders or posts.</summary>
    public bool Recalled(int player) => player >= 0 && player < _civPlayers.Count && _civPlayers[player].Recalled;

    /// <summary>Seconds left on a Starfleet inquiry hold (the flagship can't set course or scan); 0 when none.</summary>
    public int InquirySecondsLeft(int player) =>
        player >= 0 && player < _civPlayers.Count ? SecondsUntil(_civPlayers[player].HeldUntilTick) : 0;

    /// <summary>Seconds left on sanctions (observation research × sanctions_research); 0 when none.</summary>
    public int SanctionsSecondsLeft(int player) =>
        player >= 0 && player < _civPlayers.Count ? SecondsUntil(_civPlayers[player].SanctionsUntilTick) : 0;

    /// <summary>Observation research waiting for a surface tech to go into (points).</summary>
    public Fix64 ObservationBank(int player) => player >= 0 && player < _civPlayers.Count ? _civPlayers[player].Bank : Fix64.Zero;

    /// <summary>Contamination incidents the player has had on a body.</summary>
    public int IncidentsOn(int player, string bodyId)
    {
        if (player < 0 || player >= _civPlayers.Count) return 0;
        foreach (var (body, count) in _civPlayers[player].Incidents)
            if (body == bodyId) return count;
        return 0;
    }

    /// <summary>Research points a minute the player gets from observing civilizations now: while studying (a ship in orbit or a
    /// satellite, until Study = 1) rules.json research.observe_per_min by level, plus post_per_min for each working post; × the
    /// sanctions share while under sanctions; 0 once recalled. Goes into the player's active surface tech (with none active it
    /// waits in <see cref="ObservationBank"/>).</summary>
    public Fix64 ObservationResearchPerMinute(int player)
    {
        if (CivData is not { } c || player < 0 || player >= _civPlayers.Count) return Fix64.Zero;
        var p = _civPlayers[player];
        if (p.Recalled) return Fix64.Zero;
        var total = Fix64.Zero;
        for (var i = 0; i < _civs.Count; i++)
        {
            var site = _civs[i];
            if (!site.Living || KnowledgeOf(player, site.BodyId) < BodyKnowledge.Charted) continue;
            var lvl = (int)site.Level - 1;
            var (ship, satellite, posts) = Observers(player, site.BodyId);
            if ((ship || satellite) && i < p.Study.Length && p.Study[i] < Fix64.One && lvl < c.Study.ObservePerMin.Count) total += c.Study.ObservePerMin[lvl];
            if (posts > 0 && lvl < c.Study.PostPerMin.Count) total += c.Study.PostPerMin[lvl] * posts;
        }
        if (p.SanctionsUntilTick > Now) total *= c.Standing.SanctionsResearch;
        return total;
    }

    /// <summary>Seconds until a working post's next discovery roll (0 for a found one).</summary>
    public int SecondsToNextRoll(ObservationPost post) => post.Found ? 0 : SecondsUntil(post.NextRollTick);

    /// <summary>Why the player can't place a hidden observation post at that body now (null = they can).</summary>
    public string? PostProblem(int player, string bodyId)
    {
        if (_sim == null || player < 0 || player >= _sim.Players.Count) return "no such player";
        if (CivData is not { } c) return "no civilizations in this game";
        if (!c.Post.Factions.Contains(_sim.Players[player].FactionId)) return "only the Federation can place hidden observation posts for now";
        if (Recalled(player)) return "recalled to Starfleet";
        var body = Body(bodyId);
        if (body == null || KnowledgeOf(player, bodyId) == BodyKnowledge.Unknown) return "no such body";
        var ship = FlagshipOf(player);
        if (ship == null) return "no ship in orbit";
        if (ship.AtBody != bodyId) return $"the ship is not in orbit of {NameFor(player, bodyId)}";
        if (KnowledgeOf(player, bodyId) < BodyKnowledge.Charted) return $"finish the sensor sweep of {body.Name} first";
        var site = CivAt(bodyId);
        if (site == null) return $"no civilization on {body.Name} to observe";
        if (!site.Living) return $"only ruins on {body.Name}: nobody to observe";
        if (_posts.Any(p => p.Owner == player && p.BodyId == bodyId && !p.Found)) return $"a post is already hidden on {body.Name}";
        if (_posts.Count(p => p.Owner == player && !p.Found) >= c.Post.Max) return $"all {c.Post.Max} observation posts are out";
        if (!_sim.Players[player].Stock.CanAfford(c.Post.Cost)) return "not enough resources";
        return null;
    }

    /// <summary>Places the post (cost from the player's stock, like a satellite) and schedules its first discovery roll.</summary>
    private void ApplyObservationPost(Simulation sim, ObservationPostCommand post)
    {
        if (PostProblem(post.Player, post.BodyId) is { } problem)
        {
            sim.Refuse(post, problem);
            return;
        }
        var c = CivData!;
        sim.Players[post.Player].Stock.TrySpend(c.Post.Cost);
        _posts.Add(new ObservationPost
        {
            Owner = post.Player, BodyId = post.BodyId, PlacedTick = sim.Tick,
            NextRollTick = sim.Tick + (long)c.Post.DiscoveryMinutes * Simulation.TicksPerMinute,
        });
        Event(post.Player, post.BodyId, "post_placed", $"Hidden observation post placed on {Body(post.BodyId)!.Name}");
    }

    /// <summary>Why the player's flagship can't take ship orders now because of Starfleet (recalled, or held for an inquiry), or null.</summary>
    internal string? StarfleetHoldProblem(int player)
    {
        if (player < 0 || player >= _civPlayers.Count) return null;
        var p = _civPlayers[player];
        if (p.Recalled) return "recalled to Starfleet";
        if (p.HeldUntilTick > Now) return $"held for a Starfleet inquiry ({SecondsUntil(p.HeldUntilTick)} s)";
        return null;
    }

    // ---------------------------------------------------------------- set-up, ticks and the hash

    /// <summary>Rolls the system's sites (pure, from <see cref="Seed"/>; never <see cref="SurfaceBody"/>) and their starting
    /// awareness. Called once, when the system joins a game.</summary>
    private void InitCivilizations(GameData data)
    {
        _civs = CivGenerator.Roll(data, Bodies, Seed, SurfaceBody);
        _awareness = new Fix64[_civs.Count];
        if (data.Civilizations is { } c)
            for (var i = 0; i < _civs.Count; i++)
                if (_civs[i].Living && (int)_civs[i].Level - 1 < c.Awareness.Start.Count)
                    _awareness[i] = Fix64.FromInt(c.Awareness.Start[(int)_civs[i].Level - 1]);
    }

    /// <summary>A player joins: no study yet; Starfleet Standing at its start for a Federation player in a Prime Directive era.</summary>
    private void AddCivPlayer(int slot, string faction)
    {
        while (_civPlayers.Count <= slot) _civPlayers.Add(new CivPlayer { Study = new Fix64[_civs.Count] });
        var p = _civPlayers[slot];
        if (CivData is { } c && PrimeDirective && c.Standing.Factions.Contains(faction))
        {
            p.HasStanding = true;
            p.Standing = Fix64.FromInt(c.Standing.Start);
        }
    }

    /// <summary>Which of the player's observers are at a body: a ship in orbit, a satellite, working posts.</summary>
    private (bool Ship, bool Satellite, int Posts) Observers(int player, string bodyId)
    {
        var ship = false;
        foreach (var s in _ships)
            if (s.Owner == player && s.AtBody == bodyId) ship = true;
        var satellite = false;
        foreach (var s in _satellites)
            if (s.Owner == player && s.BodyId == bodyId) satellite = true;
        var posts = 0;
        foreach (var p in _posts)
            if (p.Owner == player && p.BodyId == bodyId && !p.Found) posts++;
        return (ship, satellite, posts);
    }

    /// <summary>One tick of the civilizations: awareness from what the natives can see, study and observation research, post
    /// discovery rolls (sim.Rng), Standing recovery and its steps.</summary>
    private void StepCivilizations(Simulation sim)
    {
        if (CivData is not { } c || _civs.Count == 0) return;
        var tick = sim.Tick;

        // Awareness: ships seen by L ship_seen_from_level+, satellites by L satellite_seen_from_level+, every working post.
        for (var i = 0; i < _civs.Count; i++)
        {
            var site = _civs[i];
            if (!site.Living || _awareness[i] >= Fix64.FromInt(100)) continue;
            var level = (int)site.Level;
            var perMin = Fix64.Zero;
            if (level >= c.Awareness.ShipFromLevel)
                foreach (var s in _ships)
                    if (s.Owner >= 0 && s.AtBody == site.BodyId) perMin += c.Awareness.ShipPerMin;
            if (level >= c.Awareness.SatelliteFromLevel)
                foreach (var s in _satellites)
                    if (s.BodyId == site.BodyId) perMin += c.Awareness.SatellitePerMin;
            foreach (var p in _posts)
                if (p.BodyId == site.BodyId && !p.Found) perMin += c.Awareness.PostPerMin;
            if (perMin > Fix64.Zero) RaiseAwareness(i, perMin / Simulation.TicksPerMinute);
        }

        for (var player = 0; player < _civPlayers.Count; player++)
        {
            var p = _civPlayers[player];
            if (p.Study.Length < _civs.Count) System.Array.Resize(ref p.Study, _civs.Count);

            // Study: ship ×1, satellite ×2, post ×4 (rules.json study seconds), only once the body is Charted.
            for (var i = 0; i < _civs.Count; i++)
            {
                var site = _civs[i];
                if (!site.Living || p.Study[i] >= Fix64.One || KnowledgeOf(player, site.BodyId) < BodyKnowledge.Charted) continue;
                var (ship, satellite, posts) = Observers(player, site.BodyId);
                var rate = Fix64.Zero;
                if (ship) rate += Fix64.One / (c.Study.OrbitSeconds * Simulation.TickRate);
                if (satellite) rate += Fix64.One / (c.Study.SatelliteSeconds * Simulation.TickRate);
                if (posts > 0) rate += Fix64.One / (c.Study.PostSeconds * Simulation.TickRate);
                if (rate <= Fix64.Zero) continue;
                var before = (p.Study[i] * 20).FloorToInt();
                p.Study[i] = Fix64.Min(Fix64.One, p.Study[i] + rate);
                if ((p.Study[i] * 20).FloorToInt() != before) Version++;
                if (p.Study[i] >= Fix64.One)
                    Event(player, site.BodyId, "study_complete",
                        $"Study of the {site.Species} on {Body(site.BodyId)!.Name} complete: {c.Level((int)site.Level).Name}");
            }

            // Observation research into the active surface tech (or the bank).
            var gain = ObservationResearchPerMinute(player) / Simulation.TicksPerMinute;
            if (gain > Fix64.Zero) p.Bank += gain;
            if (p.Bank > Fix64.Zero) PayResearch(sim, player, p);

            // Standing: recovery with no recent incident (up to the start), then the steps.
            if (p.HasStanding && !p.Recalled)
            {
                var quiet = p.LastIncidentTick < 0 || tick - p.LastIncidentTick >= (long)c.Standing.RecoveryAfterMinutes * Simulation.TicksPerMinute;
                var start = Fix64.FromInt(c.Standing.Start);
                if (quiet && p.Standing < start)
                {
                    var before = p.Standing.FloorToInt();
                    p.Standing = Fix64.Min(start, p.Standing + c.Standing.RecoveryPerMin / Simulation.TicksPerMinute);
                    if (p.Standing.FloorToInt() != before) Version++;
                }
                UpdateRung(sim, player, p);
            }
        }

        // Discovery rolls: every discovery_minutes of game time per working post (doubled chance above the awareness mark).
        foreach (var post in _posts)
        {
            if (post.Found || tick < post.NextRollTick) continue;
            post.NextRollTick += (long)c.Post.DiscoveryMinutes * Simulation.TicksPerMinute;
            var chance = c.Post.DiscoveryChance;
            if (AwarenessOf(post.BodyId) > c.Post.DoubleAboveAwareness) chance *= 2;
            if (sim.Rng.NextFix() < Fix64.Min(Fix64.One, chance)) Discover(sim, post);
        }
    }

    /// <summary>The natives found a post: it stops working, awareness jumps, an incident, Standing falls; a third incident on
    /// one world (or Standing 0) recalls the captain for good.</summary>
    private void Discover(Simulation sim, ObservationPost post)
    {
        var c = CivData!;
        post.Found = true;
        post.FoundTick = sim.Tick;
        var i = CivIndex(post.BodyId);
        if (i >= 0) RaiseAwareness(i, Fix64.FromInt(c.Awareness.PostFound));
        var name = Body(post.BodyId)?.Name ?? post.BodyId;
        Event(post.Owner, post.BodyId, "post_found", $"The natives of {name} found our hidden observation post");
        if (post.Owner < 0 || post.Owner >= _civPlayers.Count) return;
        var p = _civPlayers[post.Owner];
        var k = p.Incidents.FindIndex(x => x.Body == post.BodyId);
        if (k < 0) p.Incidents.Add((post.BodyId, 1));
        else p.Incidents[k] = (post.BodyId, p.Incidents[k].Count + 1);
        var count = IncidentsOn(post.Owner, post.BodyId);
        p.LastIncidentTick = sim.Tick;
        Event(post.Owner, post.BodyId, "incident", $"Contamination incident on {name} ({count} there)");
        if (!p.HasStanding || p.Recalled) return;
        p.Standing = Fix64.Max(Fix64.Zero, p.Standing + Fix64.FromInt(c.Post.StandingChange));
        if (p.Standing <= Fix64.Zero || count >= c.Standing.RecalledIncidents) p.Recalled = true;
        UpdateRung(sim, post.Owner, p);
    }

    /// <summary>Applies each step newly reached (inquiry: ship held; sanctions: research cut; 5: recalled) and notes it.</summary>
    private void UpdateRung(Simulation sim, int player, CivPlayer p)
    {
        if (!p.HasStanding) return;
        var c = CivData!;
        if (p.Standing <= Fix64.Zero) p.Recalled = true;
        var rung = p.Recalled ? 5 : RungFor(p.Standing);
        if (rung == p.Rung) return;
        for (var r = p.Rung + 1; r <= rung; r++)
        {
            if (r == 2) p.HeldUntilTick = sim.Tick + (long)c.Standing.InquiryHoldSeconds * Simulation.TickRate;
            if (r == 4) p.SanctionsUntilTick = sim.Tick + (long)c.Standing.SanctionsSeconds * Simulation.TickRate;
            var body = SurfaceBody ?? "";
            if (r == 5) Event(player, body, "recalled", "Recalled to Starfleet: the Prime Directive was broken once too often");
            else Event(player, body, "standing", $"Starfleet Standing: {RungName(r)}" + r switch
            {
                1 => " (a formal warning from Starfleet Command)",
                2 => $" (the ship is held {c.Standing.InquiryHoldSeconds} s for a board of inquiry)",
                3 => " (the captain is relieved of command)",
                4 => $" (sanctions: observation research cut for {c.Standing.SanctionsSeconds} s)",
                _ => "",
            });
        }
        p.Rung = rung;
        Version++;
    }

    private int RungFor(Fix64 standing)
    {
        var t = CivData?.Standing.Thresholds;
        if (t == null) return 0;
        var rung = 0;
        for (var k = 0; k < t.Count; k++)
            if (standing < Fix64.FromInt(t[k])) rung = k + 1;
        return rung;
    }

    /// <summary>The bank goes into the player's active surface tech (up to its cost; the rest waits for the next one).</summary>
    private void PayResearch(Simulation sim, int player, CivPlayer p)
    {
        if (sim.Surface is not { } surface || player >= surface.Players.Count) return;
        var sp = surface.Players[player];
        if (sp.ActiveTech < 0 || sp.ActiveTech >= sim.Data.Techs.Count) return;
        var tech = sim.Data.Techs[sp.ActiveTech];
        var need = tech.Cost - sp.ResearchProgress[tech.Index];
        if (need <= Fix64.Zero) return;
        var pay = Fix64.Min(need, p.Bank);
        sp.ResearchProgress[tech.Index] += pay;
        p.Bank -= pay;
        if (sp.ResearchProgress[tech.Index] >= tech.Cost)
        {
            sp.ResearchProgress[tech.Index] = tech.Cost;
            sp.Known[tech.Index] = true;
            sp.ActiveTech = -1;
        }
    }

    private void RaiseAwareness(int i, Fix64 amount)
    {
        var c = CivData!;
        var before = _awareness[i].FloorToInt();
        _awareness[i] = Fix64.Min(Fix64.FromInt(100), _awareness[i] + amount);
        var after = _awareness[i].FloorToInt();
        if (after == before) return;
        Version++;
        var band = c.Awareness.BandOf(after);
        if (band.From <= before) return;
        var site = _civs[i];
        for (var player = 0; player < _knowledge.Count; player++)
            if (KnowledgeOf(player, site.BodyId) >= BodyKnowledge.Charted)
                Event(player, site.BodyId, "awareness", $"The {site.Species} of {Body(site.BodyId)!.Name}: {band.Name.ToLowerInvariant()} of visitors (awareness {band.From})");
    }

    private void Event(int player, string bodyId, string kind, string text)
    {
        _civEvents.Add(new CivEvent(Now, player, bodyId, kind, text));
        Version++;
    }

    private int CivIndex(string? bodyId)
    {
        if (bodyId == null) return -1;
        for (var i = 0; i < _civs.Count; i++)
            if (_civs[i].BodyId == bodyId) return i;
        return -1;
    }

    private void AddCivsToHash(ref StateHasher h)
    {
        h.Add(_civs.Count);
        foreach (var c in _civs) h.Add(c.BodyId);
        foreach (var a in _awareness) h.Add(a);
        h.Add(_civPlayers.Count);
        foreach (var p in _civPlayers)
        {
            h.Add(p.Study.Length);
            foreach (var s in p.Study) h.Add(s);
            h.Add(p.HasStanding);
            h.Add(p.Standing);
            h.Add(p.Rung);
            h.Add(p.Recalled);
            h.Add(p.LastIncidentTick);
            h.Add(p.HeldUntilTick);
            h.Add(p.SanctionsUntilTick);
            h.Add(p.Bank);
            h.Add(p.Incidents.Count);
            foreach (var (body, count) in p.Incidents)
            {
                h.Add(body);
                h.Add(count);
            }
        }
        h.Add(_posts.Count);
        foreach (var p in _posts)
        {
            h.Add(p.Owner);
            h.Add(p.BodyId);
            h.Add(p.PlacedTick);
            h.Add(p.NextRollTick);
            h.Add(p.Found);
            h.Add(p.FoundTick);
        }
        h.Add(_civEvents.Count);
        foreach (var e in _civEvents)
        {
            h.Add(e.Tick);
            h.Add(e.Player);
            h.Add(e.BodyId);
            h.Add(e.Kind);
        }
    }
}
