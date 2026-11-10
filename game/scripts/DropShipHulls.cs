using System.Globalization;

namespace FinalFrontier;

/// <summary>
/// Which drop ship model a player flies and what each ship is called, for pre-warp matches (render-only, owner
/// 2026-11-10: "go with a mix … random. could be any of them per orbiting ship"; "hull names random like before").
/// Pure C# with no Godot types, so the tests compile it too (linked into FinalFrontier.Sim.Tests).
/// <list type="bullet">
/// <item>Model: in a Dawn of Warp match (the data's orbit era <see cref="PreWarpEra"/>) each player's orbiting ship
/// carries one of the three pre-warp drop ships (A Lander, B Hopper, C Freighter: <see cref="PreWarpModel"/>), picked
/// from the map seed and the player's slot, so it is the same for the whole game and after a reload, and every drop ship
/// of that player (deliveries, the first-landing convoy, outpost drops) is that model.</item>
/// <item>Names: each ship gets a name from <see cref="PreWarpNames"/> and a hull number "UE-NN", drawn per player from
/// a shuffled order (map seed + player + round, like the sim's rover names in UnitNaming) at the ship's serial
/// (<see cref="Serial"/>: the delivery that brought it and its slot in a convoy), so the same ship always gets the same
/// name and a player never sees a name twice: after every name has been used once a round number is added
/// ("TALLGRASS II").</item>
/// </list>
/// </summary>
public static class DropShipHulls
{
    /// <summary>The era id whose matches fly the pre-warp ships (data/layers/eras/dawn_of_warp.json sets orbit/era).</summary>
    public const string PreWarpEra = "dawn_of_warp";

    /// <summary>How many pre-warp models there are (A, B, C).</summary>
    public const int PreWarpModels = 3;

    /// <summary>Original Earth-expedition-style names (places, weather, birds, landmarks): no real agency's or canon
    /// ship's names. 41 (a prime) so the per-round shuffles never line up.</summary>
    public static readonly string[] PreWarpNames =
    {
        "TALLGRASS", "IRONWOOD", "SOUTHWIND", "FARSHORE", "BLUE HERON", "LONGREACH", "HIGHMEADOW", "STONEBRIDGE",
        "RIVERRUN", "MAPLEFORD", "SILVERBIRCH", "WILLOWBANK", "REDCLIFF", "GREYGULL", "SANDPIPER", "PRAIRIE ROSE",
        "CEDAR HOLLOW", "FOXGLOVE", "THISTLEDOWN", "MIDSUMMER", "FIRST LIGHT", "LANTERN", "HEARTHSTONE", "MILESTONE",
        "WAYMARK", "TRAILHEAD", "LODESTAR", "HARVEST MOON", "NORTH FORK", "CLEARWATER", "DEEPWELL", "HIGH TIDE",
        "LONG HAUL", "BRIGHTWATER", "OAKHAVEN", "SUMMIT RIDGE", "COPPER CREEK", "WINTERSET", "DRYSTONE", "FIELDFARE",
        "GOOD HOPE",
    };

    private const ulong ModelSalt = 0x4D4F44454C5F5057UL;  // "MODEL_PW"
    private const ulong NameSalt = 0x484E414D455F5057UL;   // "HNAME_PW"
    private const ulong NumberSalt = 0x484E554D5F5F5057UL; // "HNUM__PW"
    private const int Numbers = 99;                        // UE-01 … UE-99

    /// <summary>True when the match's orbit era is the pre-warp one.</summary>
    public static bool IsPreWarp(string? era) => era == PreWarpEra;

    /// <summary>The pre-warp model (0 = A Lander, 1 = B Hopper, 2 = C Freighter) for a player in a game on this map seed.</summary>
    public static int PreWarpModel(ulong seed, int player) => (int)(Mix(seed ^ ModelSalt, (ulong)(uint)player, 0) % PreWarpModels);

    /// <summary>A ship's serial for naming: the first landing's three ships are 0..2 (delivery 0), a delivery's ship(s)
    /// 3 × delivery id + slot (a shuttle has one ship, slot 0; an outpost drop three, slots 0..2). Delivery ids are
    /// unique per game, so every ship of a player gets its own serial.</summary>
    public static int Serial(int deliveryId, int slot) => 3 * deliveryId + slot;

    /// <summary>The name and hull number of a player's ship with this serial.</summary>
    public static (string Name, string Number) PreWarp(ulong seed, int player, int serial)
    {
        if (serial < 0) serial = 0;
        var n = PreWarpNames.Length;
        var round = serial / n;
        var order = Shuffled(n, Mix(seed ^ NameSalt, (ulong)(uint)player, (ulong)round));
        var name = PreWarpNames[order[serial % n]];
        if (round > 0) name += " " + Roman(round + 1);
        var numbers = Shuffled(Numbers, Mix(seed ^ NumberSalt, (ulong)(uint)player, (ulong)(serial / Numbers)));
        var number = "UE-" + (1 + numbers[serial % Numbers]).ToString("00", CultureInfo.InvariantCulture);
        return (name, number);
    }

    // ---------------------------------------------------------------- helpers

    private static ulong Mix(ulong a, ulong b, ulong c) => SplitMix(SplitMix(SplitMix(a) ^ b) ^ c);

    private static ulong SplitMix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A Fisher–Yates order of 0..n-1 from a splitmix stream.</summary>
    private static int[] Shuffled(int n, ulong state)
    {
        var order = new int[n];
        for (var i = 0; i < n; i++) order[i] = i;
        for (var i = n - 1; i > 0; i--)
        {
            state = SplitMix(state);
            var j = (int)(state % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    private static string Roman(int n)
    {
        var parts = new (int Value, string Text)[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var s = "";
        foreach (var (value, text) in parts)
            while (n >= value)
            {
                s += text;
                n -= value;
            }
        return s;
    }
}
