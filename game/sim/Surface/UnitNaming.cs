using System.Collections.Generic;
using System.Globalization;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// Names and hull numbers for new units (surfaces.json unit_names). The k-th name a player gets is the k-th entry of
/// that player's shuffled order of the pool; once the pool is used up the next round is shuffled afresh and carries
/// " II", then " III" and so on. Numbers work the same way over the first band (10..99), then the next, one digit
/// longer (100..999, ...). Each shuffle comes from its own stream seeded by the map seed, the player and the round, so
/// it never touches Simulation.Rng; the same seed always gives a player the same names in the same order, and a name
/// or number is never handed to the same player twice.
/// </summary>
internal static class UnitNaming
{
    private const ulong NameSalt = 0x4E414D45535EEDUL;
    private const ulong NumberSalt = 0x4E554D4245525EUL;
    private static readonly string[] FallbackPool = { "UNIT" };

    /// <summary>The name for the player's k-th unit (k = how many names they have had so far).</summary>
    public static string NameFor(UnitNameRules rules, ulong seed, int owner, int k)
    {
        IReadOnlyList<string> pool = rules.Pool.Count > 0 ? rules.Pool : FallbackPool;
        var round = k / pool.Count;
        var order = Shuffled(pool.Count, Stream(seed, NameSalt, owner, round));
        var name = pool[order[k % pool.Count]];
        return round == 0 ? name : name + " " + Roman(round + 1);
    }

    /// <summary>The hull number for the player's k-th unit, zero-padded to its band's width.</summary>
    public static string NumberFor(UnitNameRules rules, ulong seed, int owner, int k)
    {
        long from = rules.NumbersFrom, to = rules.NumbersTo, index = k;
        var band = 0;
        while (index >= to - from + 1)
        {
            index -= to - from + 1;
            from = to + 1;
            to = from * 10 - 1;
            band++;
        }
        var order = Shuffled((int)(to - from + 1), Stream(seed, NumberSalt, owner, band));
        var width = to.ToString(CultureInfo.InvariantCulture).Length;
        return (from + order[(int)index]).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    /// <summary>A hull number as painted: zero-padded to the first band's width (1 → "01").</summary>
    public static string FormatNumber(UnitNameRules rules, int number) =>
        number.ToString(CultureInfo.InvariantCulture).PadLeft(rules.NumbersTo.ToString(CultureInfo.InvariantCulture).Length, '0');

    /// <summary>The player's next name: their achievement extras first, then the pool (skipping a pool name equal to an extra).</summary>
    public static string NextName(UnitNameRules rules, ulong seed, int owner, SurfacePlayer p)
    {
        var used = p._usedUnitNames.Count;
        if (p._extraNames.Count == 0) return NameFor(rules, seed, owner, used);
        if (used < p._extraNames.Count) return p._extraNames[used];
        while (true)
        {
            var name = NameFor(rules, seed, owner, p.NamePoolNext++);
            if (!p._extraNames.Contains(name)) return name;
        }
    }

    /// <summary>The player's next hull number, the same way.</summary>
    public static string NextNumber(UnitNameRules rules, ulong seed, int owner, SurfacePlayer p)
    {
        var used = p._usedUnitNumbers.Count;
        if (p._extraNumbers.Count == 0) return NumberFor(rules, seed, owner, used);
        if (used < p._extraNumbers.Count) return FormatNumber(rules, p._extraNumbers[used]);
        while (true)
        {
            var number = NumberFor(rules, seed, owner, p.NumberPoolNext++);
            var taken = false;
            foreach (var x in p._extraNumbers)
                if (FormatNumber(rules, x) == number) taken = true;
            if (!taken) return number;
        }
    }

    private static DeterministicRng Stream(ulong seed, ulong salt, int owner, int round) =>
        new(seed ^ salt ^ ((ulong)(owner + 1) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(round + 1) * 0xC2B2AE3D27D4EB4FUL));

    /// <summary>A Fisher-Yates shuffle of 0..count-1.</summary>
    private static int[] Shuffled(int count, DeterministicRng rng)
    {
        var order = new int[count];
        for (var i = 0; i < count; i++) order[i] = i;
        for (var i = count - 1; i > 0; i--)
        {
            var j = rng.NextInt(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    private static string Roman(int n)
    {
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var s = "";
        for (var i = 0; i < values.Length; i++)
            while (n >= values[i])
            {
                s += symbols[i];
                n -= values[i];
            }
        return s;
    }
}
