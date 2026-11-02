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
