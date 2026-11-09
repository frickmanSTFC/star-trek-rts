using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier;

/// <summary>
/// What the tutorial looks at, filled by the driver (PlanetDriver.Tutorial.cs) from the real game state a few times a
/// second: sim queries (landing phase, structures, units, survey, pods) and the view's own state (orbit, selection,
/// drawer, placing). Counters (scan picks, surveyed tiles, pod arrivals) only grow, so a step can compare them with the
/// value it started with (<see cref="TutorialStepDef.Reacted"/>).
/// </summary>
public sealed record TutorialFacts
{
    public string World { get; set; } = "";

    /// <summary>Building id → name, from the data (for "{refinery}" in the texts).</summary>
    public Dictionary<string, string> Names { get; set; } = new();

    public bool InOrbit { get; set; } = true;
    public LandingPhase Phase { get; set; } = LandingPhase.InOrbit;

    /// <summary>The LAND COLONY deployment is armed on the helm.</summary>
    public bool LandArmed { get; set; }

    /// <summary>How many times the player has switched the orbital scan.</summary>
    public int ScanChanges { get; set; }

    /// <summary>Seconds until the first hub has assembled (0 when it isn't assembling).</summary>
    public int AssembleSecondsLeft { get; set; }

    /// <summary>A colony core of ours is Complete (the landed hub has assembled).</summary>
    public bool HubReady { get; set; }

    /// <summary>The base is selected on the surface and nothing else (its panel shows).</summary>
    public bool HubSelected { get; set; }

    /// <summary>Tiles we have surveyed.</summary>
    public int Surveyed { get; set; }

    public bool RoverDriving { get; set; }
    public bool RoverSelected { get; set; }

    /// <summary>A mining crawler has been sent to mine (driving there to unfurl) or is unfurling / digging.</summary>
    public bool CrawlerOrdered { get; set; }

    public bool CrawlerSelected { get; set; }

    /// <summary>A surveyed ore deposit (a resource id with "ore" in it) that isn't used up.</summary>
    public bool OreKnown { get; set; }

    /// <summary>Cargo pods of ours that have landed since the tutorial started.</summary>
    public int PodArrivals { get; set; }

    /// <summary>Building ids of every structure of ours, in any state (placed counts).</summary>
    public HashSet<string> Placed { get; set; } = new();

    /// <summary>Building ids in this world's build menu that the data has.</summary>
    public HashSet<string> Available { get; set; } = new();

    public bool DrawerOpen { get; set; }
    public string? Placing { get; set; }

    public string NameOf(string buildingId) => Names.TryGetValue(buildingId, out var n) ? n : buildingId.Replace('_', ' ');
}

/// <summary>Where a step's card points: a HUD or helm area the driver can find a rect for, or nothing.</summary>
public enum TutorialAnchor
{
    None,

    /// <summary>The helm's scan buttons on the left rail.</summary>
    HelmScans,

    /// <summary>The LAND pill on the helm's action strip.</summary>
    HelmLand,

    /// <summary>The resource bar along the top of the surface HUD.</summary>
    TopBar,

    /// <summary>The BUILD [B] button, or the step's building tile while the drawer is open.</summary>
    Build,
}

/// <summary>
/// One tutorial step as data. <see cref="Done"/> = the state shows it's done (checked for later steps too: a step done
/// early is skipped when its turn comes); <see cref="Reacted"/> = done while it shows, compared with the facts when it
/// came up (e.g. "switched a scan since"); <see cref="NextText"/> = a GOT IT button for read-only steps (null = waits for
/// the player). <see cref="Building"/>: the step is skipped when this world's build menu has no such building.
/// <see cref="OnSurface"/>: a surface step (in orbit it says how to get back down). Texts may name buildings as
/// "{building_id}" and the world as "{world}".
/// </summary>
public sealed record TutorialStepDef(
    string Id,
    string Title,
    string Body,
    string? NextText,
    TutorialAnchor Anchor,
    Func<TutorialFacts, bool> Done,
    Func<TutorialFacts, TutorialFacts, bool>? Reacted = null,
    Func<TutorialFacts, string?>? Hint = null,
    string? Building = null,
    bool OnSurface = false);

/// <summary>
/// The guided start (docs/menu-contract.md C): the steps as one list, and which one is showing. Progress lives only in
/// this object (the current game). Pure logic: the driver feeds it facts, the card shows <see cref="Current"/>.
/// </summary>
public sealed class Tutorial
{
    private const string GotIt = "GOT IT";

    /// <summary>The steps, in order. Change the texts and conditions here.</summary>
    public static readonly IReadOnlyList<TutorialStepDef> Steps = new TutorialStepDef[]
    {
        new("welcome", "Welcome to {world}",
            "You are at the helm, in standard orbit over {world}. This is where your colony starts: the ship's sensors can tell you a lot " +
            "about the world before anyone sets foot on it.\n\nThese cards walk you through the first steps. Most move on by themselves " +
            "as soon as you have done what they ask.",
            GotIt, TutorialAnchor.None,
            f => f.Phase != LandingPhase.InOrbit),

        new("scans", "Read the scans",
            "The buttons down the left switch the orbital scan: true colour, resources, lifeforms, biologics and landing sites " +
            "(Tab cycles them). Try one.\n\nThe FINDINGS list on the right sums up what the sensors know: which ores are likely " +
            "down there, wildlife, toxic ground.",
            GotIt, TutorialAnchor.HelmScans,
            f => f.Phase != LandingPhase.InOrbit,
            Reacted: (f, start) => f.ScanChanges > start.ScanChanges),

        new("land", "Choose a landing site",
            "LAND COLONY is armed. On the landing scan, green hexes are safe sites: open plains or desert with no predators or " +
            "toxic ground next to them. Plains near hills usually have ore close by.\n\nClick a green spot on the planet to send the colony down.",
            null, TutorialAnchor.HelmLand,
            f => f.Phase != LandingPhase.InOrbit,
            Hint: f => f.LandArmed ? null : "Press 1, or LAND on the strip below, to arm the landing again."),

        new("convoy", "Touchdown",
            "Three drop ships bring the Colony Hub down in three modules. They set the modules on the ground and unload your two " +
            "survey rovers and the mining crawler; the modules crawl together, lock and unfurl.\n\nUntil the hub has assembled it " +
            "gives no power, storage or build range, but the vehicles can drive at once.",
            null, TutorialAnchor.None,
            f => f.HubReady,
            Hint: f => f.AssembleSecondsLeft > 0 ? $"Assembling… {f.AssembleSecondsLeft} s" : null,
            OnSurface: true),

        new("hub", "Your Colony Hub",
            "Click the hub to select it: the bottom panel shows the base, its power and its build queues.\n\nThe bar along the top is " +
            "your stock: metal, ore, crew and the rest, and how much storage is left. Storage is small and fills fast.",
            GotIt, TutorialAnchor.TopBar,
            _ => false,
            Reacted: (f, _) => f.HubSelected,
            OnSurface: true),

        new("survey", "Survey the ground",
            "Ore deposits only show up once a rover has surveyed the ground. Click a survey rover to select it, then right-click " +
            "dark, unexplored ground to send it there.\n\nGlowing crystals mark the deposits it finds on the way.",
            null, TutorialAnchor.None,
            f => f.RoverDriving,
            Reacted: (f, start) => f.Surveyed > start.Surveyed,
            Hint: f => f.RoverSelected ? "Now right-click unexplored ground." : null,
            OnSurface: true),

        new("storage", "Build a {storage_container}",
            "Storage runs short first. Press BUILD (B) to open the build drawer, pick the {storage_container} and press PLACE, " +
            "then click a free hex inside the hub's range.\n\nExocomp drones fly out and build it for you.",
            null, TutorialAnchor.Build,
            f => f.Placed.Contains("storage_container"),
            Hint: f => BuildHint(f, "storage_container"),
            Building: "storage_container", OnSurface: true),

        new("refinery", "Add an {refinery}",
            "Builders need metal, not raw ore: an {refinery} turns 2 ore into 1 metal, and its yard holds a little ore too.\n\n" +
            "Place one near the hub the same way.",
            null, TutorialAnchor.Build,
            f => f.Placed.Contains("refinery"),
            Hint: f => BuildHint(f, "refinery"),
            Building: "refinery", OnSurface: true),

        new("crawler", "Start mining",
            "Select the mining crawler and right-click a surveyed ore deposit (duranium ore lies in plains and hills). It drives " +
            "there, unfurls and starts digging.\n\nF unfurls it where it stands; a move order packs it up first.",
            null, TutorialAnchor.None,
            f => f.CrawlerOrdered,
            Hint: f => !f.OreKnown ? "No ore surveyed yet: keep a rover driving, toward the hills if you can."
                : f.CrawlerSelected ? "Now right-click a glowing ore deposit." : null,
            OnSurface: true),

        new("ore", "Ore on its way",
            "Each full hopper flies home as a cargo pod to the nearest storage building or refinery. Watch the ore and metal in the " +
            "top bar.\n\nWhen storage is full, mining stops: that is when you need another container, or a second refinery.",
            GotIt, TutorialAnchor.TopBar,
            _ => false,
            Reacted: (f, start) => f.PodArrivals > start.PodArrivals,
            OnSurface: true),

        new("lab", "Research",
            "A {science_lab} earns research points. Build one, then select it to pick a tech: faster mines, tougher rovers, " +
            "deep-core mining and more.",
            null, TutorialAnchor.Build,
            f => f.Placed.Contains("science_lab"),
            Hint: f => BuildHint(f, "science_lab"),
            Building: "science_lab", OnSurface: true),

        new("done", "You're on your own",
            "That is a working colony. From here: more ore, a Fusion Reactor when power runs short, a Vehicle Bay for more vehicles, " +
            "and outposts dropped from orbit (O).\n\nPress F1 any time for the controls.",
            "FINISH", TutorialAnchor.None,
            _ => false),
    };

    private static string BuildHint(TutorialFacts f, string id) =>
        f.Placing == id ? "Click a free hex inside the base's range (R turns it, right-click cancels)."
        : f.DrawerOpen ? $"Pick the {f.NameOf(id)} tile."
        : "Press B, or BUILD on the command card.";

    private readonly bool[] _latched = new bool[Steps.Count];
    private TutorialFacts? _start;

    /// <summary>Index of the step showing (Steps.Count once finished).</summary>
    public int Index { get; private set; }

    public bool Finished => Index >= Steps.Count;

    /// <summary>The step showing, or null once finished.</summary>
    public TutorialStepDef? Current => Finished ? null : Steps[Index];

    /// <summary>"3 / 12".</summary>
    public string StepLabel => $"{System.Math.Min(Index + 1, Steps.Count)} / {Steps.Count}";

    /// <summary>Goes up whenever the step changes (the card re-shows then).</summary>
    public int Version { get; private set; }

    /// <summary>Looks at the facts: steps done early are remembered, and the current one moves on when it's done.</summary>
    public void Update(TutorialFacts now)
    {
        if (Finished) return;
        _start ??= now with { };
        for (var i = Index + 1; i < Steps.Count; i++)
            if (!_latched[i] && Applies(Steps[i], now) && Safe(Steps[i].Done, now)) _latched[i] = true;
        while (!Finished)
        {
            var step = Steps[Index];
            var done = !Applies(step, now) || _latched[Index] || Safe(step.Done, now)
                || (step.Reacted != null && SafeReacted(step.Reacted, now, _start));
            if (!done) break;
            Advance(now);
        }
    }

    /// <summary>GOT IT / FINISH on the card.</summary>
    public void Acknowledge(TutorialFacts now)
    {
        if (Finished) return;
        Advance(now);
        Update(now);
    }

    private void Advance(TutorialFacts now)
    {
        Index++;
        _start = now with { };
        Version++;
    }

    private static bool Applies(TutorialStepDef step, TutorialFacts f) => step.Building == null || f.Available.Contains(step.Building);

    // A condition that throws (a renamed id, missing data) counts as not done rather than breaking the frame.
    private static bool Safe(Func<TutorialFacts, bool> test, TutorialFacts f)
    {
        try { return test(f); }
        catch (Exception) { return false; }
    }

    private static bool SafeReacted(Func<TutorialFacts, TutorialFacts, bool> test, TutorialFacts f, TutorialFacts start)
    {
        try { return test(f, start); }
        catch (Exception) { return false; }
    }

    /// <summary>A step's text with "{world}" and "{building_id}" filled in from the facts.</summary>
    public static string Fill(string text, TutorialFacts f)
    {
        if (text.IndexOf('{') < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var close = text[i] == '{' ? text.IndexOf('}', i + 1) : -1;
            if (close < 0)
            {
                sb.Append(text[i]);
                continue;
            }
            var key = text[(i + 1)..close];
            sb.Append(key == "world" ? f.World : f.NameOf(key));
            i = close;
        }
        return sb.ToString();
    }

    /// <summary>The hint line for the current step (how to get back down when a surface step shows in orbit).</summary>
    public string? HintFor(TutorialFacts f)
    {
        var step = Current;
        if (step == null) return null;
        if (step.OnSurface && f.InOrbit && f.Phase == LandingPhase.Landed) return "Press O, or SURFACE on the strip, to go back down.";
        try { return step.Hint?.Invoke(f) is { } h ? Fill(h, f) : null; }
        catch (Exception) { return null; }
    }
}
