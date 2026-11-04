using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Simple line icons (24×24 SVG paths) turned into textures at runtime, so the HUD needs no image files.
/// Resource colours match the deposit markers on the map.
/// </summary>
public static class Icons
{
    public static readonly Dictionary<string, Color> ResourceColors = new()
    {
        ["duranium"] = new(0.79f, 0.82f, 0.86f),
        ["duranium_ore"] = new(0.66f, 0.6f, 0.54f),
        ["tritanium_ore"] = new(0.46f, 0.56f, 0.72f),
        ["parasteel"] = new(0.6f, 0.86f, 0.92f),
        ["storage"] = new(0.85f, 0.78f, 0.55f),
        ["research"] = new(0.55f, 0.88f, 1f),
        ["tritanium"] = new(0.5f, 0.7f, 1f),
        ["deuterium"] = new(0.37f, 0.89f, 0.94f),
        ["dilithium"] = new(1f, 0.54f, 0.85f),
        ["latinum"] = new(0.95f, 0.8f, 0.36f),
        ["crew"] = new(0.9f, 0.92f, 0.96f),
        ["energy"] = new(0.96f, 0.65f, 0.14f),
        ["topaline"] = new(1f, 0.85f, 0.3f),
        ["cormaline"] = new(0.5f, 1f, 0.55f),
    };

    private static readonly Dictionary<string, string> Paths = new()
    {
        ["duranium"] = "<path d='M12 3l7 4v10l-7 4-7-4V7z'/><path d='M5 7l7 4 7-4M12 11v10'/>",
        ["tritanium"] = "<rect x='4' y='5' width='16' height='5' rx='1'/><rect x='4' y='14' width='16' height='5' rx='1'/>",
        ["deuterium"] = "<path d='M12 3c4 5 6 8 6 11a6 6 0 0 1-12 0c0-3 2-6 6-11z'/>",
        ["dilithium"] = "<path d='M12 2l5 7-5 13-5-13z'/><path d='M7 9h10'/>",
        ["latinum"] = "<path d='M3 16l3-7h12l3 7z'/><path d='M3 16h18v3H3z'/>",
        ["crew"] = "<circle cx='12' cy='8' r='3.5'/><path d='M5 20c1-4 4-6 7-6s6 2 7 6'/>",
        ["energy"] = "<path d='M13 2L5 14h6l-1 8 8-12h-6z'/>",
        ["duranium_ore"] = "<path d='M4 17l3-7 5-3 6 3 2 7-7 3z'/><path d='M9 12l3 2 4-2'/>",
        ["tritanium_ore"] = "<path d='M3 18l4-9 4-3 4 2 3 2 3 8z'/><circle cx='10' cy='13' r='1.3'/><circle cx='15' cy='14' r='1.3'/>",
        ["parasteel"] = "<path d='M3 15l5-5h13l-5 5z'/><path d='M3 15v3h13v-3M16 18l5-5v-3'/>",
        ["storage"] = "<rect x='3' y='11' width='8' height='8'/><rect x='13' y='11' width='8' height='8'/><rect x='8' y='3' width='8' height='8'/>",
        ["storage_container"] = "<rect x='3' y='11' width='8' height='8'/><rect x='13' y='11' width='8' height='8'/><rect x='8' y='3' width='8' height='8'/>",
        ["refinery"] = "<path d='M4 20V11l5 3V11l5 3V5h4v15z'/><path d='M8 17h2M13 17h2'/>",
        ["foundry"] = "<path d='M5 9h14l-2 6H7z'/><path d='M12 15v3M8 21h8M9 9V5M15 9V6'/>",
        ["deep_core_mine"] = "<path d='M8 21l4-18 4 18M9 15h6M10 9h4M4 21h16'/><path d='M12 21v-3'/>",
        ["science_lab"] = "<path d='M9 3h6M10 3v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3'/><path d='M7.5 15h9'/>",
        ["research"] = "<path d='M9 3h6M10 3v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3'/><path d='M7.5 15h9'/>",
        ["tech"] = "<circle cx='12' cy='12' r='3'/><path d='M12 2v4M12 18v4M2 12h4M18 12h4M5 5l3 3M16 16l3 3M19 5l-3 3M8 16l-3 3'/>",
        ["orbit_drop"] = "<path d='M12 2v11M8 9l4 4 4-4'/><path d='M5 21h14M7 21l2-5h6l2 5'/>",
        ["orbit_shuttle"] = "<path d='M3 15l9-9 9 9-4 1-5-3-5 3z'/><path d='M12 13v6'/>",
        ["orbit_beam"] = "<path d='M8 3v18M16 3v18'/><path d='M11 6v1M13 10v1M11 14v1M13 18v1'/>",
        ["orbit_satellite"] = "<rect x='9' y='9' width='6' height='6'/><path d='M2 12h6M16 12h6M12 2v4M12 18v4'/><circle cx='12' cy='12' r='9' stroke-dasharray='2 3'/>",
        ["vehicle_bay"] = "<path d='M3 20V9l9-5 9 5v11'/><path d='M7 20v-7h10v7M7 16h10'/>",
        ["unit:survey_rover"] = "<rect x='4' y='8' width='16' height='7' rx='2'/><circle cx='8' cy='17.5' r='2'/><circle cx='16' cy='17.5' r='2'/><path d='M14 8V5h3'/>",
        ["unit:mining_crawler"] = "<rect x='3' y='12' width='18' height='5' rx='2.5'/><path d='M6 12V9h6v3M12 9l6-4M18 5v6'/>",
        ["crawler"] = "<rect x='3' y='12' width='18' height='5' rx='2.5'/><path d='M6 12V9h6v3M12 9l6-4M18 5v6'/>",
        ["colony_hub"] = "<path d='M3 18h18M6 18a6 6 0 0 1 12 0M12 12V4M12 4l3 1.5'/>",
        ["mine"] = "<path d='M8 20l4-16 4 16M9.4 14h5.2M10.4 9h3.2M4 20h16'/>",
        ["fusion_reactor"] = "<ellipse cx='12' cy='12' rx='9' ry='4'/><circle cx='12' cy='12' r='2.5'/><path d='M12 3v3M12 18v3'/>",
        ["prefab_habitat"] = "<path d='M2 19h20M4 19a3.5 3.5 0 0 1 7 0M11 19a5 5 0 0 1 10 0'/>",
        ["rover"] = "<rect x='4' y='8' width='16' height='7' rx='2'/><circle cx='8' cy='17.5' r='2'/><circle cx='16' cy='17.5' r='2'/><path d='M14 8V5h3'/>",
        ["generic"] = "<rect x='5' y='7' width='14' height='12' rx='1.5'/><path d='M9 7V4h6v3'/>",
    };

    private static readonly Dictionary<string, Texture2D> Cache = new();

    /// <summary>Icon for a resource or building id (falls back to a generic box). Colour defaults to the resource colour.</summary>
    public static Texture2D Get(string id, Color? colour = null)
    {
        var c = colour ?? (ResourceColors.TryGetValue(id, out var rc) ? rc : new Color(0.9f, 0.92f, 0.96f));
        var key = id + "|" + c.ToHtml();
        if (Cache.TryGetValue(key, out var cached)) return cached;

        if (id.StartsWith("tech:")) id = "tech";
        if (id.StartsWith("orbit:")) id = id.Split(':')[1] switch { "land" or "outpost" => "orbit_drop", "shuttle" => "orbit_shuttle", "beam" => "orbit_beam", _ => "orbit_satellite" };
        var body = Paths.TryGetValue(id, out var p) ? p
            : id.StartsWith("colonial") || id.StartsWith("planetary") ? Paths["colony_hub"] : Paths["generic"];
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' " +
                  $"stroke='#{c.ToHtml(false)}' stroke-width='1.9' stroke-linecap='round' stroke-linejoin='round'>{body}</svg>";
        var image = new Image();
        image.LoadSvgFromString(svg, 2f);
        var texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }
}
