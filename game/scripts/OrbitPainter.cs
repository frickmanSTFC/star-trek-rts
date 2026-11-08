using System;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Paints the orbital globe's textures from the tile map, pixel by pixel (equirectangular, the map's rows over
/// v = VTop..VBottom, ice caps beyond). Each pixel samples the tiles around it with a gentle noise warp so coastlines
/// and biome edges come out organic rather than hex-blocky: water vs land is a noisy threshold on the blended water
/// share; on land the dominant terrain (blended weight plus noise) picks the palette, tinted by biomass and elevation,
/// with mountains rock and snow-capped; oceans darken with depth and lighten to turquoise near the coast; beaches line
/// lowland shores. Height (with fine and ridged noise on mountains) becomes a normal map in Godot's own convention.
/// Two splat masks say how much of each satellite-photo ground layer (plains, forest, desert, hills / rock, ice) the
/// planet shader blends in at each pixel: soft near biome borders, rock up the mountains, ice on snow and caps, sand on
/// beaches and under the sea (so coastlines filter cleanly).
/// Render-only. Noise fields are computed at a lower resolution and upsampled, so painting takes a fraction of a second.
/// </summary>
public static class OrbitPainter
{
    public sealed record Result(Image Albedo, Image Water, Image Normal, Image SplatA, Image SplatB);

    /// <summary>The photo ground layers, in splat order: SplatA = plains, forest, desert, hills; SplatB = rock, ice.</summary>
    public static readonly string[] PhotoLayers = { "plains", "forest", "desert", "hills", "rock", "ice" };

    private static readonly string[] Kinds = { "plains", "forest", "desert", "hills", "mountains", "ice" };

    public static Result Paint(SurfaceMap map, int width, int height, float vTop, float vBottom)
    {
        var g = map.Grid;
        int W = g.Width, H = g.Height;
        var look = PlanetLook.For(map.Def.ClassId); // the planet class's colours (class M: as before)
        var pal = look.Orbit;

        // Per-tile inputs.
        var kind = new int[g.Count];
        var water = new float[g.Count];
        var elev = new float[g.Count];
        var bio = new float[g.Count];
        var tileH = new float[g.Count];
        var oceanBelow = map.Def.OceanBelow.ToFloat();
        var hillsFrom = map.Def.HillsFrom.ToFloat();
        var mountainsFrom = map.Def.MountainsFrom.ToFloat();
        for (var t = 0; t < g.Count; t++)
        {
            var id = look.Role(map.Terrain(t).Id); // another class's terrain ids play the class-M kinds
            kind[t] = Array.IndexOf(Kinds, id);
            water[t] = map.IsWater(t) ? 1f : 0f;
            elev[t] = map.Elevation[t].ToFloat();
            bio[t] = map.Biomass[t].ToFloat();
            tileH[t] = map.IsWater(t) ? 0f
                : id == "mountains" ? 0.5f + (elev[t] - mountainsFrom) * 3.2f
                : id == "hills" ? 0.3f + (elev[t] - hillsFrom) * 2f
                : id == "ice" ? 0.15f
                : 0.1f + (elev[t] - oceanBelow) * 0.25f;
        }

        // Noise fields at reduced resolution (all wrap east–west), sampled bilinearly per pixel.
        var warpY = new NoiseField(256, 128, 11, 16, 3);
        var warpX = new NoiseField(256, 128, 23, 16, 3);
        var coast = new NoiseField(1024, 512, 37, 64, 4);
        var detail = new NoiseField(1024, 512, 51, 128, 5);
        var ridge = new NoiseField(1024, 512, 61, 96, 5, ridged: true);
        var caps = new NoiseField(512, 256, 71, 32, 4);
        // A stronger, two-scale warp used only to pick the biome (coastlines keep the gentle one, so they match the tiles).
        var bioWarpY = new NoiseField(512, 256, 13, 40, 3);
        var bioWarpX = new NoiseField(512, 256, 29, 40, 3);
        var bioWarpFineY = new NoiseField(1024, 512, 17, 120, 3);
        var bioWarpFineX = new NoiseField(1024, 512, 31, 120, 3);
        var biomeFine = new NoiseField(1024, 512, 89, 160, 3);
        var biome = new NoiseField[Kinds.Length];
        for (var k = 0; k < Kinds.Length; k++) biome[k] = new NoiseField(512, 256, 101 + k, 48, 4);

        var albedo = new byte[width * height * 4];
        var waterMask = new byte[width * height * 4];
        var heights = new float[width * height];
        var weights = new float[Kinds.Length];
        var noisy = new float[Kinds.Length];
        var splat = new float[Kinds.Length]; // same order as Kinds and PhotoLayers (mountains = rock)
        var splatA = new byte[width * height * 4];
        var splatB = new byte[width * height * 4];
        void Toward(int layer, float amount)
        {
            if (amount <= 0f) return;
            for (var j = 0; j < splat.Length; j++) splat[j] *= 1f - amount;
            splat[layer] += amount;
        }
        const float warp = 0.9f; // NoiseField values sit near 0.5 (std ~0.14), so this moves edges ~0.25 tile

        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5f) / height;
            var band = (v - vTop) / (vBottom - vTop);
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5f) / width;
                var i = y * width + x;
                var c = Colors.Black;
                float h = 0f, isWater = 0f;
                Array.Clear(splat);
                // Polar caps beyond the map's rows, with a ragged edge that wanders a little either way.
                var capEdge = MathF.Max(vTop - v, v - vBottom) + (caps.Sample(u, v) - 0.5f) * 0.05f + (detail.Sample(u, v) - 0.5f) * 0.02f;
                var capness = Smooth(-0.012f, 0.012f, capEdge);
                if (capness < 1f)
                {
                    // Fractional tile coordinates; each row interpolates with its own half-tile stagger.
                    var fy = band * H - 0.5f + (warpY.Sample(u, v) - 0.5f) * 2f * warp + (bioWarpFineY.Sample(u, v) - 0.5f);
                    var wx = (warpX.Sample(u, v) - 0.5f) * 2f * warp + (bioWarpFineX.Sample(u, v) - 0.5f);
                    var y0 = (int)MathF.Floor(fy);
                    var ty = fy - y0;
                    var r0 = Math.Clamp(y0, 0, H - 1);
                    var r1 = Math.Clamp(y0 + 1, 0, H - 1);
                    Row(u, r0, W, wx, out var a0, out var a1, out var ta);
                    Row(u, r1, W, wx, out var b0, out var b1, out var tb);
                    int t00 = g.Index(a0, r0), t01 = g.Index(a1, r0), t10 = g.Index(b0, r1), t11 = g.Index(b1, r1);
                    float w00 = (1 - ta) * (1 - ty), w01 = ta * (1 - ty), w10 = (1 - tb) * ty, w11 = tb * ty;
                    float Blend(float[] f) => f[t00] * w00 + f[t01] * w01 + f[t10] * w10 + f[t11] * w11;

                    var wf = Blend(water);
                    var e = Blend(elev);
                    // Coast: a noisy threshold on the blended water share, softened over a pixel or so (anti-aliased).
                    var shore = wf + (coast.Sample(u, v) - 0.5f) * 1.1f - 0.5f;
                    isWater = Smooth(-0.035f, 0.035f, shore);
                    var sea = Colors.Black;
                    if (isWater > 0f)
                    {
                        // Sea: deeper = darker; near the coast, turquoise shallows; a faint large-scale mottle (currents, plankton).
                        float depth = Math.Clamp((water[t00] > 0 ? elev[t00] : oceanBelow) * w00 + (water[t01] > 0 ? elev[t01] : oceanBelow) * w01
                            + (water[t10] > 0 ? elev[t10] : oceanBelow) * w10 + (water[t11] > 0 ? elev[t11] : oceanBelow) * w11, 0f, oceanBelow) / oceanBelow;
                        sea = pal.SeaDeep.Lerp(pal.SeaMid, depth);
                        sea = sea.Lerp(pal.SeaCoast, Math.Clamp(1f - (wf - 0.5f) * 4f, 0f, 1f) * 0.7f);
                        sea *= 0.88f + 0.24f * caps.Sample(u * 2f % 1f, v);
                    }
                    if (isWater >= 1f)
                    {
                        c = sea;
                        h = 0f;
                        splat[2] = 1f;
                    }
                    else
                    {
                        // Land: the dominant terrain around this pixel, picked from the tiles under a stronger warp (falling
                        // back to the gentle one near water), with two scales of noise so the borders wander off the hexes.
                        Array.Clear(weights);
                        void Add(int t, float w)
                        {
                            if (kind[t] >= 0) weights[kind[t]] += w;
                        }
                        {
                            var by = fy + (bioWarpY.Sample(u, v) - 0.5f) * 3.5f + (bioWarpFineY.Sample(u, v) - 0.5f) * 1.4f;
                            var bx = wx + (bioWarpX.Sample(u, v) - 0.5f) * 3.5f + (bioWarpFineX.Sample(u, v) - 0.5f) * 1.4f;
                            var by0 = (int)MathF.Floor(by);
                            var bty = by - by0;
                            var br0 = Math.Clamp(by0, 0, H - 1);
                            var br1 = Math.Clamp(by0 + 1, 0, H - 1);
                            Row(u, br0, W, bx, out var c0, out var c1, out var bta);
                            Row(u, br1, W, bx, out var d0, out var d1, out var btb);
                            Add(g.Index(c0, br0), (1 - bta) * (1 - bty));
                            Add(g.Index(c1, br0), bta * (1 - bty));
                            Add(g.Index(d0, br1), (1 - btb) * bty);
                            Add(g.Index(d1, br1), btb * bty);
                        }
                        var landShare = 0f;
                        foreach (var lw in weights) landShare += lw;
                        if (landShare < 0.3f)
                        {
                            Array.Clear(weights);
                            Add(t00, w00);
                            Add(t01, w01);
                            Add(t10, w10);
                            Add(t11, w11);
                        }
                        var best = -1f;
                        var k = 0;
                        for (var j = 0; j < Kinds.Length; j++)
                        {
                            noisy[j] = -9f;
                            if (weights[j] <= 0.02f) continue;
                            var w = weights[j] + (biome[j].Sample(u, v) - 0.5f) * 0.9f + (biomeFine.Sample(u, (v + j * 0.13f) % 1f) - 0.5f) * 0.4f;
                            noisy[j] = w;
                            if (w > best)
                            {
                                best = w;
                                k = j;
                            }
                        }
                        var b = Blend(bio);
                        c = Kinds[k] switch
                        {
                            "plains" => pal.PlainsDry.Lerp(pal.PlainsLush, b),
                            "forest" => pal.ForestDry.Lerp(pal.ForestLush, b),
                            "desert" => pal.DesertLow.Lerp(pal.DesertHigh, Math.Clamp((e - 0.4f) * 3f, 0f, 1f)),
                            "hills" => pal.HillsDry.Lerp(pal.HillsLush, b),
                            "mountains" => pal.Mountains,
                            _ => pal.Ice,
                        };
                        h = Blend(tileH);
                        var d = detail.Sample(u, v);
                        var mount = Math.Clamp((h - 0.45f) * 2.5f, 0f, 1f);
                        h += (d - 0.5f) * 0.06f + ridge.Sample(u, v) * mount * 0.22f;
                        c *= 0.8f + 0.4f * d;
                        c = c.Lerp(pal.MountRock, mount * 0.55f);
                        c = c.Lerp(pal.Snow, Math.Clamp((h - 0.86f) * 9f, 0f, 1f)); // snow on the high peaks
                        var beach = Math.Clamp(1f - MathF.Abs(wf - 0.45f) * 5f, 0f, 1f) * (1f - mount);
                        c = c.Lerp(pal.Beach, beach * 0.5f);

                        // Photo layers: soft across biome borders (within 0.15 of the winner), then rock, snow, sand.
                        var total = 0f;
                        for (var j = 0; j < Kinds.Length; j++)
                        {
                            splat[j] = Math.Clamp((noisy[j] - best) / 0.15f + 1f, 0f, 1f);
                            total += splat[j];
                        }
                        for (var j = 0; j < Kinds.Length; j++) splat[j] /= MathF.Max(total, 1e-4f);
                        Toward(4, mount * 0.55f);
                        Toward(5, Math.Clamp((h - 0.86f) * 9f, 0f, 1f));
                        Toward(2, beach * 0.5f);

                        // Along the shore, fade into the sea (sand under the water).
                        if (isWater > 0f)
                        {
                            c = c.Lerp(sea, isWater);
                            h *= 1f - isWater;
                            Toward(2, isWater);
                        }
                    }
                }
                if (capness > 0f)
                {
                    // Ice: brightness drifts, blue-grey crevasse ridges, a little relief for the normal map.
                    var n = caps.Sample(u, v);
                    var r = ridge.Sample(u, v);
                    var d = detail.Sample(u, v);
                    var ice = pal.CapLow.Lerp(pal.CapHigh, n) * (0.93f + 0.07f * d);
                    ice = ice.Lerp(pal.CapCrevasse, Smooth(0.82f, 1f, r) * 0.35f);
                    var iceH = 0.12f + n * 0.1f + r * 0.05f + (d - 0.5f) * 0.03f;
                    c = c.Lerp(ice, capness);
                    h += (iceH - h) * capness;
                    isWater *= 1f - capness;
                    Toward(5, capness);
                }
                albedo[i * 4] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
                albedo[i * 4 + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
                albedo[i * 4 + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
                albedo[i * 4 + 3] = 255;
                var m = (byte)(isWater * 255f);
                waterMask[i * 4] = m;
                waterMask[i * 4 + 1] = m;
                waterMask[i * 4 + 2] = m;
                waterMask[i * 4 + 3] = 255;
                heights[i] = h;
                for (var j = 0; j < 4; j++) splatA[i * 4 + j] = (byte)(Math.Clamp(splat[j], 0f, 1f) * 255f + 0.5f);
                splatB[i * 4] = (byte)(Math.Clamp(splat[4], 0f, 1f) * 255f + 0.5f);
                splatB[i * 4 + 1] = (byte)(Math.Clamp(splat[5], 0f, 1f) * 255f + 0.5f);
                splatB[i * 4 + 3] = 255;
            }
        }

        // Normal map, as Godot's bump_map_to_normal_map builds it: n = (-(right - here)·s, -(here - above)·s, 1).
        var normal = new byte[width * height * 4];
        const float scale = 6f;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var here = heights[y * width + x];
                var right = heights[y * width + (x + 1) % width];
                var above = heights[Math.Max(0, y - 1) * width + x];
                var nx = -(right - here) * scale;
                var ny = -(here - above) * scale;
                var len = MathF.Sqrt(nx * nx + ny * ny + 1f);
                var i = (y * width + x) * 4;
                normal[i] = (byte)((nx / len * 0.5f + 0.5f) * 255f);
                normal[i + 1] = (byte)((ny / len * 0.5f + 0.5f) * 255f);
                normal[i + 2] = (byte)((1f / len * 0.5f + 0.5f) * 255f);
                normal[i + 3] = 255;
            }

        return new Result(
            Image.CreateFromData(width, height, false, Image.Format.Rgba8, albedo),
            Image.CreateFromData(width, height, false, Image.Format.Rgba8, waterMask),
            Image.CreateFromData(width, height, false, Image.Format.Rgba8, normal),
            Image.CreateFromData(width, height, false, Image.Format.Rgba8, splatA),
            Image.CreateFromData(width, height, false, Image.Format.Rgba8, splatB));
    }

    private static float Smooth(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Column pair and blend for one row at this u: the row's own half-tile stagger, plus the warp.</summary>
    private static void Row(float u, int row, int w, float warp, out int c0, out int c1, out float t)
    {
        var f = u * w - 0.5f - ((row & 1) == 1 ? 0.5f : 0f) + warp;
        var a = (int)MathF.Floor(f);
        t = f - a;
        c0 = ((a % w) + w) % w;
        c1 = (c0 + 1) % w;
    }

    /// <summary>Fractal value noise baked into a grid that wraps east–west, sampled bilinearly in (u, v).</summary>
    private sealed class NoiseField
    {
        private readonly float[] _v;
        private readonly int _w, _h;

        public NoiseField(int w, int h, int seed, int baseFrequency, int octaves, bool ridged = false)
        {
            _w = w;
            _h = h;
            _v = new float[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float sum = 0f, amp = 1f, total = 0f;
                    var f = baseFrequency;
                    for (var o = 0; o < octaves; o++)
                    {
                        sum += Value(u * f, v * f * 0.5f, seed + o, f) * amp;
                        total += amp;
                        amp *= 0.5f;
                        f *= 2;
                    }
                    var n = sum / total;
                    _v[y * w + x] = ridged ? 1f - MathF.Abs(n * 2f - 1f) : n;
                }
        }

        public float Sample(float u, float v)
        {
            var fx = u * _w - 0.5f;
            var fy = Math.Clamp(v * _h - 0.5f, 0f, _h - 1.001f);
            var x0 = (int)MathF.Floor(fx);
            var y0 = (int)fy;
            float tx = fx - x0, ty = fy - y0;
            var xa = ((x0 % _w) + _w) % _w;
            var xb = (xa + 1) % _w;
            var y1 = Math.Min(y0 + 1, _h - 1);
            var top = _v[y0 * _w + xa] * (1 - tx) + _v[y0 * _w + xb] * tx;
            var bottom = _v[y1 * _w + xa] * (1 - tx) + _v[y1 * _w + xb] * tx;
            return top + (bottom - top) * ty;
        }

        private static float Value(float x, float y, int seed, int period)
        {
            var xi = (int)MathF.Floor(x);
            var yi = (int)MathF.Floor(y);
            float fx = x - xi, fy = y - yi;
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi, seed, period), b = Hash(xi + 1, yi, seed, period);
            float c = Hash(xi, yi + 1, seed, period), d = Hash(xi + 1, yi + 1, seed, period);
            var top = a + (b - a) * sx;
            var bottom = c + (d - c) * sx;
            return top + (bottom - top) * sy;
        }

        private static float Hash(int x, int y, int seed, int period)
        {
            x = ((x % period) + period) % period;
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }
}
