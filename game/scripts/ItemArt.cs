using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The Machine Shop items' pictures: realistic renders (384×384 RGBA) in <c>res://textures/items/&lt;recipe id&gt;.png</c>,
/// made by <c>blender/build_items.py --out game/textures/items</c> (the same PNGs as the mockup's). They import as
/// ordinary textures; this gives them mipmaps once (the default 2D import has none, and the tiles draw them at 36–60
/// px) and caches them. <see cref="Picture"/> returns null when an item has no picture: callers fall back to the line
/// icon (<see cref="Icons.Get"/>).
/// The Vehicle Bay's and Research Lab's tabbed panels use the same loader for <c>res://textures/vehicles/&lt;unit
/// id&gt;.jpg</c> and <c>res://textures/techs/&lt;tech id&gt;.jpg</c> (480×360 renders from the approved mockup
/// docs/mockups/bay-lab-panel): picture keys "vehicle:&lt;id&gt;" / "tech:&lt;id&gt;" (<see cref="ForKey"/>); a plain
/// id is an item. A vehicle without a picture falls back to <see cref="VehicleFallback"/> (the driver's runtime model
/// render, once it is ready), then the line icon.
/// </summary>
public static class ItemArt
{
    private const string Folder = "res://textures/items/";
    private static readonly Dictionary<string, Texture2D?> Cache = new();

    /// <summary>A vehicle's runtime render when it has no picture (set by the driver: ModelThumbnails; null until ready).</summary>
    public static System.Func<string, Texture2D?>? VehicleFallback;

    public static Texture2D? Picture(string id) => Load(Folder + id + ".png");

    /// <summary>A Vehicle Bay product's picture (unit id), null if none.</summary>
    public static Texture2D? Vehicle(string id) => Load("res://textures/vehicles/" + id + ".jpg");

    /// <summary>A surface tech's picture (tech id), null if none.</summary>
    public static Texture2D? Tech(string id) => Load("res://textures/techs/" + id + ".jpg");

    /// <summary>The picture for a key: "vehicle:&lt;id&gt;", "tech:&lt;id&gt;", else an item id; falls back to the line icon.</summary>
    public static Texture2D ForKey(string key, Color colour)
    {
        if (key.StartsWith("vehicle:"))
        {
            var id = key[8..];
            return Vehicle(id) ?? VehicleFallback?.Invoke(id) ?? Icons.Get("vehicle_bay", colour);
        }
        if (key.StartsWith("tech:")) return Tech(key[5..]) ?? Icons.Get("research", colour);
        return PictureOrIcon(key, colour);
    }

    /// <summary>True when the key has a real picture file (not a line icon or a runtime render).</summary>
    public static bool HasPicture(string key) =>
        key.StartsWith("vehicle:") ? Vehicle(key[8..]) != null : key.StartsWith("tech:") ? Tech(key[5..]) != null : Picture(key) != null;

    private static Texture2D? Load(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        Texture2D? texture = null;
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
        Cache[path] = texture;
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
