using System.Collections.Generic;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Words and colours shared by the menu's Fit Out Your Ship page (MainMenu.FitOut.cs) and the game's two read-outs of a
/// fit-out (the flagship's card, the pause card): the department colours of the design page (docs/mockups/loadout), signed
/// percentages and the one-line crew split. Every number comes from the FitOut record and GameData.FitOut.
/// </summary>
public static class FitOutText
{
    private static readonly Dictionary<string, (Color Fill, Color Text)> Colours = new()
    {
        ["exploration"] = (new Color(0.243f, 0.498f, 0.839f), new Color(0.553f, 0.733f, 0.949f)),
        ["engineering"] = (new Color(0.851f, 0.635f, 0.227f), new Color(0.941f, 0.765f, 0.369f)),
        ["science"] = (new Color(0.306f, 0.604f, 0.353f), new Color(0.561f, 0.847f, 0.596f)),
        ["defense"] = (new Color(0.784f, 0.259f, 0.227f), new Color(0.953f, 0.604f, 0.565f)),
    };

    private static readonly (Color Fill, Color Text) Plain = (new Color(0.553f, 0.565f, 0.604f), new Color(0.725f, 0.737f, 0.776f));

    /// <summary>A division's (or cargo group's) fill colour; grey for "ship", no division or an id the page doesn't know.</summary>
    public static Color Fill(string? id) => id != null && Colours.TryGetValue(id, out var c) ? c.Fill : Plain.Fill;

    /// <summary>The lighter colour for text on the dark panels.</summary>
    public static Color Text(string? id) => id != null && Colours.TryGetValue(id, out var c) ? c.Text : Plain.Text;

    /// <summary>A figure that is worse than today's (a division below a quarter of the crew).</summary>
    public static readonly Color Bad = new(0.94f, 0.54f, 0.5f);

    /// <summary>Heads as a whole percentage of a crew (0 for an empty crew).</summary>
    public static int SharePercentOf(int heads, int crew) => crew <= 0 ? 0 : Mathf.RoundToInt(100f * heads / crew);

    /// <summary>"+8%", "−6%", or "as today" for nothing.</summary>
    public static string Signed(int percent) => percent == 0 ? "as today" : percent > 0 ? $"+{percent}%" : $"−{-percent}%";

    /// <summary>The crew in every division together.</summary>
    public static int Heads(FitOut fit)
    {
        var sum = 0;
        for (var i = 0; i < fit.Crew.Count; i++) sum += fit.Crew[i];
        return sum;
    }

    /// <summary>Division i's share of the assigned crew, in whole percent.</summary>
    public static int SharePercent(FitOut fit, int i)
    {
        var heads = Heads(fit);
        return heads <= 0 || i < 0 || i >= fit.Crew.Count ? 0 : Mathf.RoundToInt(100f * fit.Crew[i] / heads);
    }

    /// <summary>The focus's name ("Explorer"), or its id when the data doesn't list it.</summary>
    public static string FocusName(GameData data, FitOut fit) => data.FitOut?.Focus(fit.Focus)?.Name ?? fit.Focus.Replace('_', ' ');

    /// <summary>"Exploration 45% · Engineering 25% · Science 18% · Defense 12%" (short: "EXP 45% · ENG 25% · …").</summary>
    public static string CrewLine(GameData data, FitOut fit, bool shortNames)
    {
        var divisions = data.FitOut?.Divisions;
        var sb = new StringBuilder();
        for (var i = 0; i < fit.Crew.Count; i++)
        {
            var name = divisions != null && i < divisions.Count ? divisions[i].Name : $"Division {i + 1}";
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(shortNames ? HudKit.Short(name) : name).Append(' ').Append(SharePercent(fit, i)).Append('%');
        }
        return sb.ToString();
    }
}
