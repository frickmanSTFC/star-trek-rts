using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;

namespace FinalFrontier;

// The game's read-outs of the ship's fit-out (docs/fit-out-contract.md; chosen on the menu's Fit Out Your Ship page,
// MainMenu.FitOut.cs): the focus and the four division percentages on our flagship's card (System display) and on the
// pause card, so a tester can see why a number differs. Nothing shows without a fit-out (Simulation.FitOutOf is null:
// the sandbox, the ships that never land, data with no fit-out rules). Render-only.
public partial class PlanetDriver
{
    private FitOut? _fitOutShown; // the record the two lines below were made from (it never changes in a match)
    private string _fitOutFocus = "", _fitOutCrewShort = "", _fitOutCrewLong = "";
    private Godot.Color _fitOutColour;

    /// <summary>The local player's fit-out with its lines made (once), or null when there is none.</summary>
    private FitOut? LocalFitOut()
    {
        var fit = _sim.FitOutOf(LocalPlayer);
        if (fit == null || ReferenceEquals(fit, _fitOutShown)) return fit;
        _fitOutShown = fit;
        _fitOutFocus = FitOutText.FocusName(_data, fit);
        _fitOutCrewShort = FitOutText.CrewLine(_data, fit, shortNames: true);
        _fitOutCrewLong = FitOutText.CrewLine(_data, fit, shortNames: false);
        _fitOutColour = FitOutText.Text(_data.FitOut?.Focus(fit.Focus)?.Division);
        return fit;
    }

    /// <summary>The flagship card's FOCUS and CREW rows (nothing on any other ship, or with no fit-out).</summary>
    private void AddFitOutRows(SystemShip s, List<HelmRow> rows)
    {
        if (s.Owner != LocalPlayer || s.Id != _system.Flagship?.Id || LocalFitOut() == null) return;
        rows.Add(new HelmRow("FOCUS", _fitOutFocus, _fitOutColour));
        rows.Add(new HelmRow("CREW", _fitOutCrewShort));
    }

    /// <summary>Two lines for the pause card ("FIT-OUT: EXPLORER" and the crew split), or empty.</summary>
    private string FitOutPauseText() => LocalFitOut() == null ? "" : $"FIT-OUT: {_fitOutFocus.ToUpperInvariant()}\n{_fitOutCrewLong}";

    /// <summary>One line for the Output panel at the start of a game (the tester's log).</summary>
    private static string FitOutLogLine(GameData data, FitOut fit) =>
        $"{fit.Focus}, crew {FitOutText.CrewLine(data, fit, shortNames: true)} ({string.Join("/", fit.Crew)}), cargo " +
        (fit.Cargo.Count == 0 ? "none" : string.Join(", ", fit.Cargo.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key, System.StringComparer.Ordinal).Select(kv => $"{kv.Key} ×{kv.Value}")));
}
