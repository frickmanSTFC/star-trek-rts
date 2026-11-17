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
        // A ship that never lands: what is left of the hold's body satellites, posts and probes (they count down in play).
        if (_sim.System?.PickedShip(s) is { } pick && FitOutRules.IsSpace(_data, pick.Id))
        {
            int sats = _sim.FitOutFreeSystemSatellites(LocalPlayer), posts = _sim.FitOutFreePosts(LocalPlayer), probes = _sim.FitOutProbes(LocalPlayer);
            rows.Add(new HelmRow("ABOARD", sats + posts + probes == 0 ? "nothing left"
                : $"{sats} {(sats == 1 ? "satellite" : "satellites")} · {posts} {(posts == 1 ? "post" : "posts")} · {probes} {(probes == 1 ? "probe" : "probes")}"));
        }
    }

    /// <summary>The scan button's text: "LONG-RANGE SCAN", or "· PROBE n" while the scanners recharge and a sensor probe
    /// from the hold would make the scan (a fit-out of a ship that never lands).</summary>
    private string ScanLabel()
    {
        if (_sim.System is not { } sys || sys.FlagshipOf(LocalPlayer) is not { } f || !sys.ScanUsesProbe(LocalPlayer, f.Id)) return "LONG-RANGE SCAN";
        return $"LONG-RANGE SCAN · PROBE {_sim.FitOutProbes(LocalPlayer)}";
    }

    /// <summary>Two lines for the pause card ("FIT-OUT: EXPLORER" and the crew split), or empty.</summary>
    private string FitOutPauseText() => LocalFitOut() == null ? "" : $"FIT-OUT: {_fitOutFocus.ToUpperInvariant()}\n{_fitOutCrewLong}";

    /// <summary>One line for the Output panel at the start of a game (the tester's log).</summary>
    private static string FitOutLogLine(GameData data, FitOut fit) =>
        $"{fit.Focus}, crew {FitOutText.CrewLine(data, fit, shortNames: true)} ({string.Join("/", fit.Crew)}), cargo " +
        (fit.Cargo.Count == 0 ? "none" : string.Join(", ", fit.Cargo.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key, System.StringComparer.Ordinal).Select(kv => $"{kv.Key} ×{kv.Value}")));
}
