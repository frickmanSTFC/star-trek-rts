using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The Machine Shop items' pictures: realistic renders (384×384 RGBA) in <c>res://textures/items/&lt;recipe id&gt;.png</c>,
/// made by <c>blender/build_items.py --out game/textures/items</c> (the same PNGs as the mockup's). They import as
/// ordinary textures; this gives them mipmaps once (the default 2D import has none, and the tiles draw them at 36–60
/// px) and caches them. <see cref="Picture"/> returns null when an item has no picture: callers fall back to the line
/// icon (<see cref="Icons.Get"/>).
/// </summary>
public static class ItemArt
{
    private const string Folder = "res://textures/items/";
    private static readonly Dictionary<string, Texture2D?> Cache = new();

    public static Texture2D? Picture(string id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;
        Texture2D? texture = null;
        var path = Folder + id + ".png";
        if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is Texture2D imported)
        {
            texture = imported;
            var image = imported.GetImage(); // lossless import: the pixels are there
            if (image != null && !image.IsEmpty())
            {
                if (image.IsCompressed()) image.Decompress();
                if (!image.HasMipmaps()) image.GenerateMipmaps();
                texture = ImageTexture.CreateFromImage(image);
            }
        }
        Cache[id] = texture;
        return texture;
    }

    /// <summary>The picture, else the item's line icon in its tier colour.</summary>
    public static Texture2D PictureOrIcon(string id, Color colour) => Picture(id) ?? Icons.Get(id, colour);

    private static Shader? _grey;

    /// <summary>A canvas material that desaturates what it draws (greyed-out tiles: "can't make it right now").</summary>
    public static ShaderMaterial Greyed(float amount)
    {
        _grey ??= new Shader
        {
            Code = @"shader_type canvas_item;
uniform float amount = 0.65;
void fragment() {
    vec4 c = COLOR;
    float g = dot(c.rgb, vec3(0.299, 0.587, 0.114));
    COLOR = vec4(mix(c.rgb, vec3(g), amount), c.a);
}",
        };
        var m = new ShaderMaterial { Shader = _grey };
        m.SetShaderParameter("amount", amount);
        return m;
    }
}
