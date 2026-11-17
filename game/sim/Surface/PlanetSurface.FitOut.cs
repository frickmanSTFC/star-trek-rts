using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// "Fit Out Your Ship" on the surface (docs/fit-out-contract.md; rules in Core/FitOut.cs). A player's fit-out multiplies a
// handful of numbers ("levers") at the places gravity, weather and research already do; each site asks Fit(owner, lever).
// With no fit-out (every test, the sandbox, the balance runner) Fit is exactly 1 and nothing changes.
public sealed partial class PlanetSurface
{
    /// <summary>The owner's fit-out multiplier on a lever (FitOutRules lever ids); exactly 1 with no fit-out.</summary>
    private Fix64 Fit(int owner, string lever) => Game is { } game ? game.FitOutFactor(owner, lever) : Fix64.One;

    /// <summary>How long a building takes this player to build here, in ticks: <see cref="BuildTicksOf(string)"/> × their
    /// fit-out's build_time lever.</summary>
    public int BuildTicksOf(string buildingId, int owner) => (int)GravityProfile.Ticks(BuildTicksOf(buildingId), Fit(owner, FitOutRules.BuildTime));

    /// <summary>How long a building level takes this player here, in ticks (× their fit-out's build_time lever).</summary>
    public int LevelTicksOf(BuildingLevel level, int owner) => (int)GravityProfile.Ticks(LevelTicksOf(level), Fit(owner, FitOutRules.BuildTime));

    /// <summary>A hit's damage × a fit-out lever, rounded to whole points and never below 1 (unchanged when the lever is 1).</summary>
    private static int ScaledDamage(int damage, Fix64 factor) =>
        factor == Fix64.One || damage <= 0 ? damage : System.Math.Max(1, (Fix64.FromInt(damage) * factor + Fix64.Half).FloorToInt());

    /// <summary>Research points the ship brought (the fit-out's lab equipment): banked like a dig's research, paid into the
    /// first tech the player starts.</summary>
    internal void BankResearch(int player, Fix64 points)
    {
        if (player < 0 || player >= _players.Count || points <= Fix64.Zero) return;
        _digBank[player] = DigResearchBank(player) + points;
    }
}
