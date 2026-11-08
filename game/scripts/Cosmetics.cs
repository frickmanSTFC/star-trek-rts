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

    /// <summary>Clears every pick (normal look).</summary>
    public static void Clear()
    {
        Paint = Paint2 = BeamColour = DomeTint = null;
        Pattern = Finish = DropshipName = null;
        Version++;
    }
}
