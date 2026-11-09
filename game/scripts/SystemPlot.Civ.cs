using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Civilization marks on the system plot (civilizations phase 1, the First Contact mockup's plot): a body with known
// signals or a living civilization gets two signal arcs beside it on the left (peach) and a "SIGNALS" / species tag; ruins get
// broken pillars beside it (rust) and a "RUINS" tag. From what the player knows (CivView), never the truth. Billboard
// sprites from two small textures painted once; the plot rebuilds when CivStamp changes.
public partial class SystemPlot
{
    private static readonly Color Rust = new(0.784f, 0.4f, 0.29f); // #c8664a
    private static ImageTexture? _signalTexture, _ruinTexture;

    /// <summary>What the player knows of every body's civilization, as a stamp (rebuild the plot when it changes).</summary>
    private static long CivStamp(SystemSource source)
    {
        if (source.State is not { } sys) return 0;
        unchecked
        {
            long h = 7;
            foreach (var b in source.Bodies)
            {
                var r = sys.CivView(source.Player, b.Id);
                h = h * 31 + (r == null ? 0 : 1 + (r.Charted ? 2 : 0) + (r.Living ? 4 : 0) + (r.PostHere ? 8 : 0) + (r.PostFound ? 16 : 0));
            }
            return h * 1000003L;
        }
    }

    /// <summary>The mark and tag for a body's civilization as known, or (null, null).</summary>
    private static (Node3D? Glyph, string? Tag) CivGlyph(SystemSource source, string bodyId, float radius)
    {
        if (source.State?.CivView(source.Player, bodyId) is not { } r) return (null, null);
        var ruins = r.Charted && !r.Living;
        var sprite = new Sprite3D
        {
            Name = ruins ? "Ruins" : "Signals",
            Texture = ruins ? RuinTexture() : SignalTexture(),
            Modulate = ruins ? Rust : Peach,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            PixelSize = 0.09f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // left of the body: the label sits on top and the satellite glyph on the right
            Position = new Vector3(-radius - 3.4f, radius * 0.25f, 0f),
        };
        var tag = !r.Charted ? "SIGNALS" : ruins ? "RUINS" : (r.Species ?? "NATIVES").ToUpperInvariant();
        if (r.PostHere && !r.PostFound) tag += " · POST";
        return (sprite, tag);
    }

    /// <summary>A dot with two arcs over it (white; tinted by Modulate), 64².</summary>
    private static ImageTexture SignalTexture()
    {
        if (_signalTexture != null) return _signalTexture;
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        var centre = new Vector2(32f, 50f);
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - centre;
                var d = p.Length();
                var a = 0f;
                a = Mathf.Max(a, Mathf.Clamp(5.5f - d, 0f, 1f)); // the dot
                var up = p.Y < 0f && Mathf.Abs(p.X) < -p.Y * 1.25f; // within ±51° of straight up
                if (up)
                {
                    a = Mathf.Max(a, Mathf.Clamp(2.2f - Mathf.Abs(d - 17f), 0f, 1f));
                    a = Mathf.Max(a, Mathf.Clamp(2.2f - Mathf.Abs(d - 29f), 0f, 1f));
                }
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        img.GenerateMipmaps();
        return _signalTexture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>Broken pillars on a base with a cracked lintel (white; tinted by Modulate), 64².</summary>
    private static ImageTexture RuinTexture()
    {
        if (_ruinTexture != null) return _ruinTexture;
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        img.Fill(new Color(1f, 1f, 1f, 0f));
        void Box(int x0, int y0, int x1, int y1)
        {
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    img.SetPixel(x, y, Colors.White);
        }
        Box(6, 54, 58, 59); // base
        int[] xs = { 12, 25, 38, 51 };
        int[] tops = { 18, 28, 14, 34 };
        for (var i = 0; i < xs.Length; i++) Box(xs[i] - 3, tops[i], xs[i] + 3, 54);
        Box(8, 12, 30, 16); // the lintel, broken in the middle
        Box(34, 9, 44, 13);
        img.GenerateMipmaps();
        return _ruinTexture = ImageTexture.CreateFromImage(img);
    }
}
