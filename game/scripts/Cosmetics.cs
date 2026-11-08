using Godot;

namespace FinalFrontier;

// CONTRACT (2026-11-08, achievements; docs/achievements-contract.md). The UI agent sets these from the profile's picks
// at game start (AchievementProfile → Cosmetics.Set…); the cosmetics agent reads them in the render code. All apply to
// the LOCAL player's own models only; null = the normal look.
public static class Cosmetics
{
    /// <summary>Hull stripe colour (the "Owner" material) of our vehicles, or null = the player colour.</summary>
    public static Color? Paint { get; set; }

    /// <summary>Second paint colour for patterns, or null.</summary>
    public static Color? Paint2 { get; set; }

    /// <summary>Paint pattern: "solid", "band", "chevrons", "stripes" (null = solid).</summary>
    public static string? Pattern { get; set; }

    /// <summary>Special finish id for our vehicles and drop ships (brushed_gold, chrome, blue_white, gunmetal, pearl,
    /// rescue_white), or null.</summary>
    public static string? Finish { get; set; }

    /// <summary>Name painted on our drop ships ("HAILER 01"), or null = the fixed list.</summary>
    public static string? DropshipName { get; set; }

    /// <summary>Colour of our exocomps' build beams, or null = the default blue.</summary>
    public static Color? BeamColour { get; set; }

    /// <summary>Glass tint of our domes on hostile worlds, or null = clear.</summary>
    public static Color? DomeTint { get; set; }

    /// <summary>Goes up whenever a pick changes, so render code can refresh materials it has already made.</summary>
    public static int Version { get; set; }

    /// <summary>A special finish's material look (render-only; <see cref="CosmeticLook"/> puts it on our vehicles' and
    /// drop ships' hull paint surfaces). Hull = the paint colour (the metal's own colour for metallic finishes); Trim =
    /// the "Owner" stripes' colour when the finish brings its own (an explicit <see cref="Paint"/> still wins), else
    /// null; Ink = the painted names/numbers; Brushed = fine roughness streaks along the hull (brushed metal).</summary>
    public readonly record struct FinishLook(Color Hull, float Metallic, float Roughness, float Clearcoat, float ClearcoatRoughness,
        float Rim, float RimTint, bool Brushed, Color? Trim, Color Ink);

    /// <summary>The navy of the hull numbers (RoverLook's decal ink).</summary>
    public static readonly Color NavyInk = new(0.12f, 0.15f, 0.3f);

    /// <summary>The finish presets by id; null for an unknown id or none.</summary>
    public static FinishLook? FinishPreset(string? id) => id switch
    {
        "brushed_gold" => new FinishLook(new Color(1f, 0.77f, 0.34f), 1f, 0.3f, 0f, 0.3f, 0f, 0f, true, null, NavyInk),
        "chrome" => new FinishLook(new Color(0.95f, 0.96f, 0.97f), 1f, 0.08f, 0f, 0.3f, 0f, 0f, false, null, NavyInk),
        "blue_white" => new FinishLook(new Color(0.93f, 0.94f, 0.96f), 0.05f, 0.32f, 0.5f, 0.15f, 0f, 0f, false, new Color(0.13f, 0.34f, 0.82f), NavyInk),
        "gunmetal" => new FinishLook(new Color(0.21f, 0.22f, 0.245f), 0.85f, 0.3f, 0.3f, 0.2f, 0f, 0f, false, null, new Color(0.86f, 0.88f, 0.92f)),
        "pearl" => new FinishLook(new Color(0.95f, 0.94f, 0.915f), 0.12f, 0.22f, 1f, 0.06f, 0.3f, 0.2f, false, null, NavyInk),
        "rescue_white" => new FinishLook(new Color(0.95f, 0.95f, 0.96f), 0.05f, 0.4f, 0.2f, 0.3f, 0f, 0f, false, new Color(0.86f, 0.1f, 0.08f), NavyInk),
        _ => null,
    };

    /// <summary>The current finish's look, or null.</summary>
    public static FinishLook? CurrentFinish => FinishPreset(Finish);

    /// <summary>Pattern id → the livery shader's pattern number (0 = none: solid, null or unknown).</summary>
    public static int PatternCode(string? pattern) => pattern switch
    {
        "band" => 1,
        "chevrons" => 2,
        "stripes" => 3,
        _ => 0,
    };

    /// <summary>Our drop ship's painted name and number from <see cref="DropshipName"/> ("HAILER 01" → HAILER, 01; the
    /// n-th ship down at once counts on: 02, 03 …), or null when no name is picked.</summary>
    public static (string Name, string Number)? DropshipHull(int index)
    {
        if (string.IsNullOrWhiteSpace(DropshipName)) return null;
        var text = DropshipName.Trim().ToUpperInvariant();
        var space = text.LastIndexOf(' ');
        if (space <= 0) return (text, index > 0 ? (index + 1).ToString("00") : "");
        var name = text[..space].TrimEnd();
        var number = text[(space + 1)..];
        if (index > 0 && int.TryParse(number, out var n)) number = (n + index).ToString(new string('0', number.Length));
        return (name, number);
    }

    /// <summary>Clears every pick (normal look).</summary>
    public static void Clear()
    {
        Paint = Paint2 = BeamColour = DomeTint = null;
        Pattern = Finish = DropshipName = null;
        Version++;
    }
}
