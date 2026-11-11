"""Bake the tileable ground detail textures for the surface terrain shader (visual pass G2).

Run with plain system Python (numpy + Pillow, no Blender needed):

    python3 blender/bake_terrain_textures.py [--size 1024] [--out game/textures/terrain] [--only grass,rock]

Writes, per ground kind, <kind>_albedo.jpg (sRGB colour), <kind>_normal.jpg (tangent-space normal, OpenGL / Godot
convention: green = up in the image) and <kind>_rough.jpg (grey roughness 0..1). Every map tiles seamlessly: all the
noise is built periodically (FFT-filtered white noise, Voronoi cells on a wrapped grid, strokes drawn at the wrapped
offsets near the edges). The shader (game/shaders/terrain.gdshader) only uses the albedo as a variation around the
mesh's per-tile colour (texel / the texture's mean), so the absolute colours here only steer the hue of that variation.

Procedural because the network refused Poly Haven and ambientCG when this was made (2026-11-11). Deterministic: the
same seed gives the same files.
"""

import argparse
import os

import numpy as np
from PIL import Image, ImageDraw

KINDS = ["grass", "forest_floor", "dirt", "sand", "rock", "snow", "mud", "tundra", "lichen", "basalt"]


# ---------------------------------------------------------------------------------------------------------- noise kit

class Kit:
    def __init__(self, n, seed):
        self.n = n
        self.rng = np.random.default_rng(seed)
        f = np.fft.fftfreq(n) * n
        self.fx, self.fy = np.meshgrid(f, f)
        y, x = np.mgrid[0:n, 0:n].astype(np.float32)
        self.x, self.y = x, y

    def spectral(self, beta, fmin=1.0, fmax=None, aniso=(1.0, 1.0), angle=0.0):
        """Periodic fractal noise (zero mean, unit std): white noise shaped by k^-beta between fmin and fmax cycles per
        tile; aniso stretches it (e.g. (1, 4) = streaks along x)."""
        n = self.n
        w = self.rng.standard_normal((n, n)).astype(np.float32)
        c, s = np.cos(angle), np.sin(angle)
        ux = self.fx * c + self.fy * s
        uy = -self.fx * s + self.fy * c
        k = np.sqrt((ux * aniso[0]) ** 2 + (uy * aniso[1]) ** 2)
        amp = np.where(k >= fmin, np.maximum(k, 1e-6) ** (-beta), 0.0)
        if fmax is not None:
            amp *= np.exp(-((k / fmax) ** 4))
        out = np.real(np.fft.ifft2(np.fft.fft2(w) * amp)).astype(np.float32)
        out -= out.mean()
        return out / (out.std() + 1e-8)

    def blur(self, img, sigma):
        """Periodic Gaussian blur (sigma in pixels)."""
        g = np.exp(-2.0 * (np.pi * sigma / self.n) ** 2 * (self.fx ** 2 + self.fy ** 2))
        if img.ndim == 2:
            return np.real(np.fft.ifft2(np.fft.fft2(img) * g)).astype(np.float32)
        return np.stack([self.blur(img[..., i], sigma) for i in range(img.shape[2])], axis=-1)

    def worley(self, cells, jitter=0.9, warp=0.0):
        """Voronoi on a wrapped cells x cells grid: (F1, F2 in cell units, cell id, cell random 0..1 x3). warp bends
        the cell walls by a periodic noise (in cells)."""
        n = self.n
        pts = self.rng.random((cells, cells, 2)).astype(np.float32) * jitter + (1 - jitter) / 2
        rnd = self.rng.random((cells * cells, 3)).astype(np.float32)
        u = self.x / n * cells
        v = self.y / n * cells
        if warp > 0:
            u = u + self.spectral(1.5, fmin=1, fmax=cells * 2) * warp
            v = v + self.spectral(1.5, fmin=1, fmax=cells * 2) * warp
        ci = np.floor(u).astype(np.int32)
        cj = np.floor(v).astype(np.int32)
        f1 = np.full((n, n), 9.0, np.float32)
        f2 = np.full((n, n), 9.0, np.float32)
        ident = np.zeros((n, n), np.int32)
        for dj in (-1, 0, 1):
            for di in (-1, 0, 1):
                cx = (ci + di) % cells
                cy = (cj + dj) % cells
                px = ci + di + pts[cy, cx, 0]
                py = cj + dj + pts[cy, cx, 1]
                d = np.hypot(u - px, v - py)
                closer = d < f1
                f2 = np.where(closer, f1, np.minimum(f2, d))
                ident = np.where(closer, cy * cells + cx, ident)
                f1 = np.where(closer, d, f1)
        return f1, f2, ident, rnd[ident]

    def warp(self, img, dx, dy):
        """Sample img (periodic, bilinear) at (x + dx, y + dy) pixels."""
        n = self.n
        x = self.x + dx
        y = self.y + dy
        x0 = np.floor(x).astype(np.int32)
        y0 = np.floor(y).astype(np.int32)
        fx = (x - x0)[..., None] if img.ndim == 3 else x - x0
        fy = (y - y0)[..., None] if img.ndim == 3 else y - y0
        x0 %= n
        y0 %= n
        x1 = (x0 + 1) % n
        y1 = (y0 + 1) % n
        a = img[y0, x0] * (1 - fx) + img[y0, x1] * fx
        b = img[y1, x0] * (1 - fx) + img[y1, x1] * fx
        return a * (1 - fy) + b * fy

    def stones(self, cells, chance, size, shape_noise=0.25, flat=0.5):
        """Pebbles: a dome in some Voronoi cells. Returns (height 0..1, mask 0..1, per-stone random 0..1 x3)."""
        f1, _, _, rnd = self.worley(cells)
        wob = self.spectral(1.2, fmin=cells * 0.5, fmax=cells * 6) * shape_noise
        r = size * (0.6 + 0.6 * rnd[..., 1])
        d = (f1 + wob * r) / r
        dome = np.clip(1.0 - d * d, 0.0, 1.0)
        dome = np.sqrt(dome) * (1 - flat) + np.clip(dome * 3, 0, 1) * flat
        on = (rnd[..., 0] < chance).astype(np.float32)
        h = dome * on * (0.5 + 0.5 * rnd[..., 2])
        mask = np.clip((1.0 - d) * 6.0, 0.0, 1.0) * on
        return h, mask, rnd

    def strokes(self, count, length, width, colours, height_range, angle_spread=np.pi, base_angle=0.0, curve=0.0,
                canvas=None, hcanvas=None, dark_base=0.7, taper=True):
        """Thin strokes (grass blades, needles, twigs) drawn with wrap-around. Returns (rgb float, height float)."""
        n = self.n
        img = canvas if canvas is not None else Image.new("RGB", (n, n))
        him = hcanvas if hcanvas is not None else Image.new("L", (n, n))
        d = ImageDraw.Draw(img)
        dh = ImageDraw.Draw(him)
        rng = self.rng
        cols = np.asarray(colours, np.float32)
        for _ in range(count):
            x, y = rng.random() * n, rng.random() * n
            a = base_angle + (rng.random() - 0.5) * 2 * angle_spread
            L = length[0] + rng.random() * (length[1] - length[0])
            w = max(1, int(round(width[0] + rng.random() * (width[1] - width[0]))))
            col = cols[rng.integers(len(cols))] * (0.85 + 0.3 * rng.random())
            h = height_range[0] + rng.random() * (height_range[1] - height_range[0])
            bend = (rng.random() - 0.5) * curve
            seg = 3
            pts = []
            for i in range(seg + 1):
                t = i / seg
                aa = a + bend * t
                pts.append((x + np.cos(aa) * L * t, y + np.sin(aa) * L * t))
            xs = [p[0] for p in pts]
            ys = [p[1] for p in pts]
            offs = [(0, 0)]
            if min(xs) < w + 2 or max(xs) > n - w - 2 or min(ys) < w + 2 or max(ys) > n - w - 2:
                offs = [(ox, oy) for ox in (-n, 0, n) for oy in (-n, 0, n)]
            for i in range(seg):
                t = (i + 0.5) / seg
                k = dark_base + (1 - dark_base) * t
                c = tuple(int(np.clip(v * k, 0, 1) * 255) for v in col)
                hv = int(np.clip(h * (0.55 + 0.45 * t), 0, 1) * 255)
                ww = max(1, int(round(w * (1 - 0.6 * t)))) if taper else w
                for ox, oy in offs:
                    seg_pts = [(pts[i][0] + ox, pts[i][1] + oy), (pts[i + 1][0] + ox, pts[i + 1][1] + oy)]
                    d.line(seg_pts, fill=c, width=ww)
                    dh.line(seg_pts, fill=hv, width=ww)
        return img, him

    def leaves(self, count, size, colours, canvas, hcanvas, height=0.6):
        """Fallen leaves: small pointed ovals with a midrib, any angle, wrapped."""
        n = self.n
        d = ImageDraw.Draw(canvas)
        dh = ImageDraw.Draw(hcanvas)
        rng = self.rng
        cols = np.asarray(colours, np.float32)
        for _ in range(count):
            x, y = rng.random() * n, rng.random() * n
            a = rng.random() * 2 * np.pi
            L = size[0] + rng.random() * (size[1] - size[0])
            W = L * (0.3 + 0.25 * rng.random())
            col = cols[rng.integers(len(cols))] * (0.75 + 0.4 * rng.random())
            pts = []
            for i in range(12):
                t = i / 12 * 2 * np.pi
                lx = np.cos(t) * L / 2
                ly = np.sin(t) * W / 2 * (1 - 0.35 * np.cos(t))
                pts.append((x + lx * np.cos(a) - ly * np.sin(a), y + lx * np.sin(a) + ly * np.cos(a)))
            c = tuple(int(np.clip(v, 0, 1) * 255) for v in col)
            rib = tuple(int(np.clip(v * 0.7, 0, 1) * 255) for v in col)
            hv = int(np.clip(height * (0.6 + 0.4 * rng.random()), 0, 1) * 255)
            offs = [(0, 0)]
            if x < L or x > n - L or y < L or y > n - L:
                offs = [(ox, oy) for ox in (-n, 0, n) for oy in (-n, 0, n)]
            for ox, oy in offs:
                p = [(px + ox, py + oy) for px, py in pts]
                d.polygon(p, fill=c)
                dh.polygon(p, fill=hv)
                d.line([p[0], p[6]], fill=rib, width=1)
        return canvas, hcanvas


def to_arr(img):
    a = np.asarray(img, np.float32) / 255.0
    return a


def mix(a, b, t):
    t = t[..., None] if (np.ndim(t) == 2 and np.ndim(a) == 3) else t
    return a * (1 - t) + b * t


def ramp(t, stops):
    """Colour ramp: t 0..1 through [(pos, (r, g, b)), ...]."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,), np.float32)
    pos = [p for p, _ in stops]
    for c in range(3):
        out[..., c] = np.interp(t, pos, [col[c] for _, col in stops])
    return out


def norm01(a):
    lo, hi = np.percentile(a, 1), np.percentile(a, 99)
    return np.clip((a - lo) / (hi - lo + 1e-8), 0, 1)


# ------------------------------------------------------------------------------------------------------- ground kinds

def grass(k):
    n = k.n
    soil_n = norm01(k.spectral(1.6, fmin=2))
    soil = ramp(soil_n, [(0, (0.22, 0.18, 0.12)), (1, (0.36, 0.3, 0.2))])
    base = Image.fromarray((soil * 255).astype(np.uint8))
    hbase = Image.fromarray((soil_n * 40).astype(np.uint8))
    # Under-layer of short dark blades, then longer lit ones on top: clumped by a density noise.
    greens = [(0.24, 0.36, 0.12), (0.3, 0.44, 0.16), (0.36, 0.5, 0.18), (0.44, 0.54, 0.22), (0.52, 0.56, 0.27),
              (0.6, 0.58, 0.32)]
    img, him = k.strokes(int(26000 * (n / 1024) ** 2), (n * 0.008, n * 0.018), (1, 2), greens[:3], (0.25, 0.5),
                         curve=0.6, canvas=base, hcanvas=hbase, dark_base=0.55)
    img, him = k.strokes(int(30000 * (n / 1024) ** 2), (n * 0.012, n * 0.03), (1, 3), greens, (0.5, 1.0),
                         curve=0.9, canvas=img, hcanvas=him, dark_base=0.6)
    alb = to_arr(img)
    h = to_arr(him)
    clump = norm01(k.spectral(1.4, fmin=3, fmax=40))
    alb = alb * (0.85 + 0.3 * clump[..., None])
    dry = norm01(k.spectral(1.8, fmin=1, fmax=12))
    alb = mix(alb, alb * np.array([1.18, 1.02, 0.78], np.float32), np.clip((dry - 0.6) * 2.5, 0, 1) * 0.6)
    rough = 0.82 + 0.12 * (1 - h) + 0.04 * clump
    return alb, h, rough, 2.2


def forest_floor(k):
    n = k.n
    soil_n = norm01(k.spectral(1.5, fmin=2))
    soil = ramp(soil_n, [(0, (0.12, 0.09, 0.06)), (1, (0.24, 0.18, 0.11))])
    base = Image.fromarray((soil * 255).astype(np.uint8))
    hbase = Image.fromarray((soil_n * 50).astype(np.uint8))
    s = n / 1024
    needles = [(0.36, 0.24, 0.12), (0.45, 0.31, 0.15), (0.3, 0.2, 0.1)]
    img, him = k.strokes(int(14000 * s * s), (n * 0.008, n * 0.016), (1, 1), needles, (0.2, 0.4), canvas=base,
                         hcanvas=hbase, dark_base=0.8, taper=False)
    leafc = [(0.42, 0.25, 0.1), (0.52, 0.32, 0.12), (0.34, 0.2, 0.09), (0.55, 0.42, 0.18), (0.28, 0.18, 0.1),
             (0.46, 0.36, 0.14)]
    img, him = k.leaves(int(5200 * s * s), (n * 0.018, n * 0.04), leafc, img, him, height=0.55)
    twigs = [(0.26, 0.19, 0.12), (0.34, 0.26, 0.17)]
    img, him = k.strokes(int(420 * s * s), (n * 0.04, n * 0.11), (2, 4), twigs, (0.7, 1.0), curve=0.5, canvas=img,
                         hcanvas=him, dark_base=0.9, taper=False)
    img, him = k.leaves(int(1400 * s * s), (n * 0.02, n * 0.045), leafc, img, him, height=0.75)
    alb = to_arr(img)
    h = to_arr(him)
    # Moss in patches.
    moss_n = norm01(k.spectral(1.7, fmin=2, fmax=30))
    fine = k.spectral(1.0, fmin=40)
    moss = np.clip((moss_n - 0.62) * 5 + fine * 0.15, 0, 1)
    moss_col = ramp(norm01(fine), [(0, (0.16, 0.24, 0.07)), (1, (0.3, 0.4, 0.12))])
    alb = mix(alb, moss_col, moss * 0.9)
    h = np.maximum(h, moss * (0.45 + 0.1 * fine))
    rough = 0.86 + 0.1 * (1 - h) - 0.06 * moss
    return alb, h, rough, 2.6


def dirt(k):
    n = k.n
    a = norm01(k.spectral(1.7, fmin=1))
    b = norm01(k.spectral(1.1, fmin=8))
    col = ramp(a * 0.7 + b * 0.3, [(0, (0.26, 0.2, 0.14)), (0.5, (0.4, 0.31, 0.21)), (1, (0.52, 0.42, 0.3))])
    h = a * 0.35 + b * 0.25
    # Dry cracks (thin dark Voronoi edges, faded in patches).
    f1, f2, _, _ = k.worley(9)
    crack = np.clip(1 - (f2 - f1) / 0.05, 0, 1) * np.clip((norm01(k.spectral(1.6, fmin=1, fmax=8)) - 0.45) * 3, 0, 1)
    h -= crack * 0.25
    col *= 1 - 0.35 * crack[..., None]
    # Pebbles and grit, two sizes.
    for cells, chance, size, tint in ((22, 0.55, 0.32, (0.5, 0.46, 0.4)), (60, 0.5, 0.3, (0.44, 0.38, 0.3))):
        sh, mask, rnd = k.stones(cells, chance, size)
        stone = np.asarray(tint, np.float32) * (0.75 + 0.5 * rnd[..., 2:3]) * (0.8 + 0.3 * sh[..., None])
        col = mix(col, stone, mask)
        h = np.maximum(h, 0.45 + sh * 0.5 * mask) * mask + h * (1 - mask)
    specks = (k.rng.random((n, n)) < 0.012).astype(np.float32)
    col *= 1 - 0.3 * k.blur(specks, 0.7)[..., None] * 3
    rough = 0.9 - 0.15 * np.clip(h - 0.5, 0, 1)
    return col, norm01(h), rough, 2.0


def sand(k):
    n = k.n
    warp = k.spectral(1.8, fmin=1, fmax=6)
    warp2 = k.spectral(1.5, fmin=2, fmax=10)
    u = k.x / n
    v = k.y / n
    # Wind ripples: integer frequencies keep them tileable; the warp bends them.
    phase = 2 * np.pi * (14 * u + 3 * v) + warp * 2.2 + warp2 * 0.8
    rip = np.sin(phase)
    rip = np.sign(rip) * np.abs(rip) ** 0.7
    rip2 = np.sin(2 * np.pi * (5 * u - 22 * v) + warp2 * 1.5)
    ripple_mask = np.clip(norm01(k.spectral(1.6, fmin=1, fmax=6)) * 1.5 - 0.2, 0, 1)
    grain = k.spectral(0.3, fmin=200)
    dune = norm01(k.spectral(2.0, fmin=1))
    h = 0.5 + 0.25 * rip * ripple_mask + 0.08 * rip2 * (1 - ripple_mask) + 0.04 * grain + 0.2 * (dune - 0.5)
    col = ramp(dune * 0.6 + norm01(rip) * 0.25 + 0.15 * norm01(grain), [(0, (0.68, 0.56, 0.38)), (1, (0.86, 0.74, 0.52))])
    dark = (k.rng.random((n, n)) < 0.03).astype(np.float32)
    col *= 1 - 0.35 * dark[..., None]
    light = (k.rng.random((n, n)) < 0.02).astype(np.float32)
    col = mix(col, np.ones_like(col) * 0.95, light * 0.4)
    rough = 0.88 + 0.06 * norm01(grain) - 0.04 * ripple_mask
    return col, norm01(h), rough, 1.3


def rock(k, dark=False):
    n = k.n
    a = k.spectral(1.9, fmin=1)
    ridged = 1 - np.abs(k.spectral(1.6, fmin=2))
    detail = k.spectral(1.2, fmin=12)
    v = k.y / n
    strata = np.sin(2 * np.pi * (6 * v) + a * 0.9 + k.spectral(1.8, fmin=1, fmax=5) * 1.4)
    h = norm01(a) * 0.45 + norm01(ridged) * 0.3 + norm01(detail) * 0.15 + 0.1 * strata
    f1, f2, _, rnd = k.worley(7, warp=0.35)
    keep = np.clip((norm01(k.spectral(1.4, fmin=2, fmax=20)) - 0.3) * 2.5, 0, 1)
    crack = np.clip(1 - (f2 - f1) / 0.035, 0, 1) * keep
    f1b, f2b, _, _ = k.worley(19, warp=0.3)
    crack2 = np.clip(1 - (f2b - f1b) / 0.03, 0, 1) * 0.6 * (1 - keep * 0.7)
    h -= (crack + crack2) * 0.22
    h += (rnd[..., 0] - 0.5) * 0.12  # each slab a little proud or sunk
    h = norm01(h)
    if dark:
        stops = [(0, (0.08, 0.08, 0.085)), (0.5, (0.17, 0.165, 0.165)), (1, (0.27, 0.25, 0.24))]
    else:
        stops = [(0, (0.26, 0.24, 0.22)), (0.5, (0.43, 0.41, 0.38)), (1, (0.6, 0.58, 0.54))]
    col = ramp(h * 0.7 + norm01(detail) * 0.3, stops)
    col *= (0.9 + 0.2 * rnd[..., 1:2])
    col *= 1 - 0.5 * np.maximum(crack, crack2)[..., None]
    if not dark:
        # A few lichen spots and iron stains.
        spots = np.clip((norm01(k.spectral(1.0, fmin=30, fmax=200)) - 0.8) * 6, 0, 1) * np.clip(h * 2 - 0.6, 0, 1)
        col = mix(col, np.array([0.55, 0.56, 0.42], np.float32), spots * 0.55)
        stain = np.clip((norm01(k.spectral(1.8, fmin=1, fmax=10)) - 0.65) * 3, 0, 1)
        col = mix(col, col * np.array([1.15, 0.92, 0.75], np.float32), stain * 0.6)
    rough = 0.78 + 0.16 * (1 - h) + 0.05 * norm01(detail)
    return col, h, rough, 3.2


def snow(k):
    n = k.n
    drift = k.spectral(2.0, fmin=1)
    sastrugi = k.spectral(1.8, fmin=3, aniso=(1.0, 3.0), angle=0.4)
    fine = k.spectral(0.6, fmin=60)
    h = norm01(drift) * 0.6 + norm01(sastrugi) * 0.25 + norm01(fine) * 0.15
    col = ramp(h, [(0, (0.74, 0.79, 0.88)), (0.6, (0.88, 0.91, 0.96)), (1, (0.96, 0.97, 1.0))])
    sparkle = (k.rng.random((n, n)) < 0.004).astype(np.float32)
    col = mix(col, np.ones_like(col), sparkle * 0.8)
    rough = 0.72 + 0.12 * norm01(fine) - 0.5 * sparkle
    return col, h, rough, 0.9


def mud(k):
    n = k.n
    a = norm01(k.spectral(1.9, fmin=3))
    b = k.spectral(1.2, fmin=10)
    h = a * 0.7 + norm01(b) * 0.2
    # Smeared ruts: anisotropic streaks.
    smear = k.spectral(1.6, fmin=2, aniso=(1.0, 6.0), angle=1.1)
    h += 0.12 * norm01(smear)
    wet = np.clip((0.42 - a) * 4.0, 0, 1)  # water standing in the low parts
    col = ramp(norm01(h), [(0, (0.12, 0.09, 0.06)), (0.6, (0.22, 0.17, 0.11)), (1, (0.32, 0.26, 0.18))])
    col *= (1 - 0.35 * wet)[..., None]
    sh, mask, rnd = k.stones(30, 0.25, 0.28)
    col = mix(col, np.array([0.3, 0.27, 0.22], np.float32) * (0.8 + 0.4 * rnd[..., 2:3]), mask * 0.9)
    h = np.where(mask > 0, np.maximum(h, 0.6 + 0.3 * sh), h)
    h = norm01(h) * (1 - 0.6 * wet)
    rough = np.clip(0.5 + 0.35 * norm01(b) - 0.45 * wet + 0.2 * mask, 0.05, 1)
    return col, h, rough, 1.6


def tundra(k):
    n = k.n
    a = norm01(k.spectral(1.7, fmin=1))
    col = ramp(a, [(0, (0.36, 0.32, 0.26)), (1, (0.52, 0.47, 0.38))])
    h = a * 0.3
    # Frost-shattered gravel: grey and ochre chips, angular (flat-topped domes).
    for cells, chance, size, flat in ((26, 0.7, 0.42, 0.85), (70, 0.6, 0.38, 0.7)):
        sh, mask, rnd = k.stones(cells, chance, size, shape_noise=0.45, flat=flat)
        tint = mix(np.array([0.5, 0.48, 0.45], np.float32) * np.ones_like(col),
                   np.array([0.58, 0.46, 0.32], np.float32) * np.ones_like(col), (rnd[..., 0] > 0.6).astype(np.float32))
        stone = tint * (0.7 + 0.5 * rnd[..., 2:3]) * (0.85 + 0.25 * sh[..., None])
        col = mix(col, stone, mask)
        h = np.where(mask > 0, np.maximum(h, 0.35 + 0.45 * sh), h)
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8))
    him = Image.fromarray((np.clip(h, 0, 1) * 255).astype(np.uint8))
    s = n / 1024
    # Dry tussock grass tufts in clumps.
    tuft = [(0.62, 0.56, 0.38), (0.52, 0.47, 0.32), (0.7, 0.64, 0.44)]
    clumps = 260
    d_img, d_h = img, him
    for _ in range(int(clumps * s * s)):
        cx, cy = k.rng.random() * n, k.rng.random() * n
        sub = Kit.__new__(Kit)
        sub.n, sub.rng = n, k.rng
        for _ in range(26):
            k2x = cx + (k.rng.random() - 0.5) * n * 0.03
            k2y = cy + (k.rng.random() - 0.5) * n * 0.03
            ang = np.arctan2(k2y - cy, k2x - cx)
            L = n * (0.01 + 0.015 * k.rng.random())
            col_t = np.asarray(tuft[k.rng.integers(3)]) * (0.85 + 0.3 * k.rng.random())
            pts = [(k2x, k2y), (k2x + np.cos(ang) * L, k2y + np.sin(ang) * L)]
            offs = [(ox, oy) for ox in (-n, 0, n) for oy in (-n, 0, n)] if (min(cx, cy) < n * 0.05 or max(cx, cy) > n * 0.95) else [(0, 0)]
            for ox, oy in offs:
                p = [(x + ox, y + oy) for x, y in pts]
                ImageDraw.Draw(d_img).line(p, fill=tuple(int(c * 255) for c in np.clip(col_t, 0, 1)), width=1)
                ImageDraw.Draw(d_h).line(p, fill=int(180 + 60 * k.rng.random()), width=1)
    col = to_arr(d_img)
    h = to_arr(d_h)
    rough = 0.9 - 0.08 * h
    return col, h, rough, 2.0


def lichen(k):
    n = k.n
    base_n = norm01(k.spectral(1.6, fmin=1))
    col = ramp(base_n, [(0, (0.3, 0.29, 0.26)), (1, (0.46, 0.44, 0.39))])
    h = base_n * 0.4
    # Crusty rosettes: rings of Voronoi cells, pale green-grey, with orange and yellow colonies.
    f1, f2, _, rnd = k.worley(34, jitter=1.0, warp=0.25)
    r = 0.3 + 0.35 * rnd[..., 1]
    ros = np.clip(1 - f1 / r, 0, 1)
    lobes = norm01(k.spectral(0.8, fmin=60)) * 0.6 + 0.4 * np.clip(1 - f1 / r, 0, 1)
    on = (rnd[..., 0] < 0.75).astype(np.float32)
    cover = np.clip(ros * 3, 0, 1) * on * (0.7 + 0.3 * lobes)
    patch = np.clip((norm01(k.spectral(1.7, fmin=1, fmax=12)) - 0.25) * 2.5, 0, 1)
    cover *= patch
    pick = rnd[..., 2]
    lich = np.where((pick < 0.7)[..., None], np.array([0.62, 0.66, 0.52], np.float32),
                    np.where((pick < 0.88)[..., None], np.array([0.72, 0.62, 0.3], np.float32),
                             np.array([0.7, 0.42, 0.18], np.float32)))
    lich = lich * (0.85 + 0.3 * lobes[..., None])
    col = mix(col, lich, cover * 0.9)
    h = np.maximum(h, 0.35 + cover * 0.35 * lobes)
    # Low moss cushions between.
    moss = np.clip((norm01(k.spectral(1.3, fmin=6, fmax=60)) - 0.7) * 4, 0, 1) * (1 - cover)
    col = mix(col, np.array([0.38, 0.44, 0.28], np.float32), moss * 0.7)
    h = np.maximum(h, moss * 0.55)
    rough = 0.9 - 0.04 * cover
    return col, norm01(h), rough, 2.4


def basalt(k):
    col, h, rough, strength = rock(k, dark=True)
    n = k.n
    # Columnar joints: a coarse hex-like Voronoi with deep cracks, and gas vesicles.
    f1, f2, _, rnd = k.worley(5, jitter=0.6, warp=0.08)
    joint = np.clip(1 - (f2 - f1) / 0.06, 0, 1)
    h = norm01(h * 0.6 + (rnd[..., 0] - 0.5) * 0.3 - joint * 0.5)
    col = col * (1 - 0.6 * joint[..., None]) * (0.9 + 0.2 * rnd[..., 1:2])
    ves_h, ves_m, _ = k.stones(90, 0.35, 0.25, flat=0.0)
    h = h - ves_h * 0.25
    col *= (1 - 0.5 * ves_m)[..., None]
    rust = np.clip((norm01(k.spectral(1.8, fmin=1, fmax=8)) - 0.7) * 3, 0, 1)
    col = mix(col, col * np.array([1.4, 1.0, 0.75], np.float32), rust * 0.5)
    rough = 0.75 + 0.2 * (1 - h)
    return col, norm01(h), rough, 3.0


BAKERS = {"grass": grass, "forest_floor": forest_floor, "dirt": dirt, "sand": sand, "rock": rock, "snow": snow,
          "mud": mud, "tundra": tundra, "lichen": lichen, "basalt": basalt}


# ------------------------------------------------------------------------------------------------------------ output

def normal_map(k, h, strength):
    """Tangent-space normal from a height field (periodic central differences), OpenGL convention (green = up in the
    image = -row). strength scales the slope (height 0..1 across ~1/256 of the tile)."""
    s = strength * k.n / 256.0
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * s
    drow = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * s
    nx, ny, nz = -dx, drow, np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx * inv, ny * inv, nz * inv], axis=-1)


def bake(kind, n, out, seed):
    k = Kit(n, seed)
    alb, h, rough, strength = BAKERS[kind](k)
    h = np.clip(h.astype(np.float32), 0, 1)
    # Cavity shading: darker in the hollows (a cheap baked AO).
    cav = np.clip((h - k.blur(h, n / 128)) * 3.0, -1, 1)
    alb = np.clip(alb * (0.92 + 0.16 * cav[..., None]), 0, 1)
    nrm = normal_map(k, k.blur(h, 0.6), strength)
    Image.fromarray((alb * 255 + 0.5).astype(np.uint8)).save(os.path.join(out, f"{kind}_albedo.jpg"), quality=86,
                                                              optimize=True)
    Image.fromarray(((nrm * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(os.path.join(out, f"{kind}_normal.jpg"),
                                                                           quality=90, optimize=True)
    Image.fromarray((np.clip(rough, 0, 1) * 255 + 0.5).astype(np.uint8), "L").save(os.path.join(out, f"{kind}_rough.jpg"),
                                                                                  quality=85, optimize=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "game", "textures", "terrain"))
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    kinds = [s for s in args.only.split(",") if s] or KINDS
    for i, kind in enumerate(KINDS):
        if kind in kinds:
            bake(kind, args.size, args.out, 1701 + i * 97)
            print("baked", kind)


if __name__ == "__main__":
    main()
