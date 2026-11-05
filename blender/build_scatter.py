"""
Landscape scatter for the planet surface: trees, bushes, grass, rocks, sandstone, ice.

Builds game-ready low-poly assets procedurally and exports them as ONE glb (one node per asset, named as in
ASSETS below) to game/models/scatter/scatter.glb. scripts/Scatter.cs loads the meshes from it by name and draws
them with MultiMeshes (shaders/foliage.gdshader for plants, shaders/rock.gdshader for stone).

Conventions (same as the vehicle scripts): Blender +Z up (Godot +Y), 1 unit = 1 world unit (a hex is 25 wide, a
rover ~2.5 long), every asset's origin sits on the ground at its foot, transforms applied.

  Material "Foliage": vertex colour x the 512x512 atlas (packed into the glb, alpha-cut): needle sprays, leaf
    clusters, grass blades, dry twigs, bark, an opaque needle speckle for cores and far meshes.
    UV2 ("Wind"): x = how much the vertex flutters (0 trunk .. 1 branch tip / blade tip), y = phase 0..1.
    Normals on cards are bent outward from the crown (custom normals), so a crown shades like one volume.
  Material "Rock": vertex colour only (strata, lichen, crevices); snow and moss are added in the game shader.
  "<name>_far": a much cheaper stand-in the game swaps to at 330-400 units (shared by similar models: conifer_a_far
  stands in for every conifer, boulder_a_far for every boulder, sandstone_a_far and ice_a_far for both of theirs).

Run (the PyPI bpy module, Python 3.11 venv; ~1 s for the assets, a few minutes for previews):
  /home/user/.bpyvenv/bin/python blender/build_scatter.py                       # export the glb
  /home/user/.bpyvenv/bin/python blender/build_scatter.py --preview blender/previews/scatter --samples 32
  python3 blender/build_scatter.py --sheet blender/previews/scatter            # contact sheet (system python, PIL)

Flags: --out PATH (glb, default game/models/scatter/scatter.glb), --no-export, --preview DIR, --res W,
--samples N, --shots a,b (lineup,rocks,temperate,arid,close,overview).
"""

import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def arg(name, default=None):
    if name in sys.argv:
        i = sys.argv.index(name)
        return sys.argv[i + 1] if i + 1 < len(sys.argv) else True
    return default


# ---------------------------------------------------------------- contact sheet (system python + PIL, no bpy)

if arg("--sheet"):
    from PIL import Image, ImageDraw

    folder = arg("--sheet")
    names = ["lineup", "rocks", "temperate", "arid", "close", "overview"]
    imgs = [(n, Image.open(os.path.join(folder, f"scatter-{n}.png"))) for n in names
            if os.path.exists(os.path.join(folder, f"scatter-{n}.png"))]
    w = 720
    tiles = [(n, im.resize((w, int(im.height * w / im.width)))) for n, im in imgs]
    h = max(t.height for _, t in tiles)
    cols = 2
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * w + (cols + 1) * 8, rows * (h + 26) + 8), (14, 16, 22))
    d = ImageDraw.Draw(sheet)
    for i, (n, t) in enumerate(tiles):
        x = 8 + (i % cols) * (w + 8)
        y = 8 + (i // cols) * (h + 26)
        sheet.paste(t, (x, y + 18))
        d.text((x, y + 2), n, fill=(230, 200, 150))
    out = os.path.join(folder, "scatter-sheet.png")
    sheet.save(out)
    print("wrote", out)
    sys.exit(0)

import bpy  # noqa: E402  (bpy before bmesh)
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

OUT = arg("--out", os.path.join(ROOT, "game", "models", "scatter", "scatter.glb"))
PREVIEW = arg("--preview")
RES = int(arg("--res", "1100"))
SAMPLES = int(arg("--samples", "32"))
SHOTS = (arg("--shots") or "lineup,rocks,temperate,arid,close,overview").split(",")


def srgb(c):
    """sRGB 0..1 triple -> linear (vertex colours are linear in glTF and in Godot)."""
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def mul(c, k):
    return tuple(x * k for x in c)


def lerp3(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# ---------------------------------------------------------------- atlas (512x512, painted at 1024 and halved)

S = 1024
REGIONS = {  # atlas UV rectangles (u0, v0, u1, v1), v up (Blender convention)
    "needles": (0.0, 0.0, 0.5, 0.5),
    "leaves": (0.5, 0.0, 1.0, 0.5),
    "grass": (0.0, 0.5, 0.5, 1.0),
    "twigs": (0.5, 0.5, 1.0, 0.75),
    "bark": (0.5, 0.75, 0.75, 1.0),
    "speckle": (0.75, 0.75, 1.0, 1.0),
}
INSET = 3.0 / 512.0


def ruv(region, u, v):
    u0, v0, u1, v1 = REGIONS[region]
    u0, v0, u1, v1 = u0 + INSET, v0 + INSET, u1 - INSET, v1 - INSET
    return (u0 + (u1 - u0) * u, v0 + (v1 - v0) * v)


class Canvas:
    def __init__(self):
        self.rgb = np.zeros((S, S, 3), np.float32)
        self.a = np.zeros((S, S), np.float32)
        self.clip = (0, 0, S, S)

    def region(self, name, bg):
        u0, v0, u1, v1 = REGIONS[name]
        x0, y0, x1, y1 = int(u0 * S), int(v0 * S), int(u1 * S), int(v1 * S)
        self.rgb[y0:y1, x0:x1] = bg
        self.clip = (x0 + 6, y0 + 6, x1 - 6, y1 - 6)
        return x0, y0, x1, y1

    def _box(self, xa, ya, xb, yb):
        c = self.clip
        x0, y0 = max(c[0], int(math.floor(xa))), max(c[1], int(math.floor(ya)))
        x1, y1 = min(c[2], int(math.ceil(xb)) + 1), min(c[3], int(math.ceil(yb)) + 1)
        if x1 <= x0 or y1 <= y0:
            return None
        ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32) + 0.5
        return (slice(y0, y1), slice(x0, x1)), xs, ys

    def _over(self, sl, cov, col):
        cov = np.clip(cov, 0, 1)
        self.rgb[sl] = self.rgb[sl] * (1 - cov[..., None]) + col * cov[..., None]
        self.a[sl] = self.a[sl] + cov * (1 - self.a[sl])

    def seg(self, p0, p1, w0, w1, c0, c1):
        """Anti-aliased tapered stroke from p0 to p1 (pixels), colour c0 -> c1."""
        m = max(w0, w1) / 2 + 2
        box = self._box(min(p0[0], p1[0]) - m, min(p0[1], p1[1]) - m, max(p0[0], p1[0]) + m, max(p0[1], p1[1]) + m)
        if box is None:
            return
        sl, xs, ys = box
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        l2 = max(dx * dx + dy * dy, 1e-6)
        t = np.clip(((xs - p0[0]) * dx + (ys - p0[1]) * dy) / l2, 0, 1)
        d = np.hypot(xs - (p0[0] + t * dx), ys - (p0[1] + t * dy))
        w = w0 + (w1 - w0) * t
        cov = w / 2 - d + 0.5
        col = np.array(c0, np.float32) + (np.array(c1, np.float32) - np.array(c0, np.float32)) * t[..., None]
        self._over(sl, cov, col)

    def curve(self, pts, w0, w1, c0, c1):
        n = len(pts) - 1
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            self.seg(pts[i], pts[i + 1], w0 + (w1 - w0) * t0, w0 + (w1 - w0) * t1,
                     lerp3(c0, c1, t0), lerp3(c0, c1, t1))

    def leaf(self, base, angle, length, width, col, vein=0.75):
        ca, sa = math.cos(angle), math.sin(angle)
        tip = (base[0] + ca * length, base[1] + sa * length)
        m = width / 2 + 2
        box = self._box(min(base[0], tip[0]) - m, min(base[1], tip[1]) - m, max(base[0], tip[0]) + m, max(base[1], tip[1]) + m)
        if box is None:
            return
        sl, xs, ys = box
        lx = (xs - base[0]) * ca + (ys - base[1]) * sa
        ly = -(xs - base[0]) * sa + (ys - base[1]) * ca
        f = np.clip(lx / length, 0, 1)
        half = width / 2 * np.power(np.maximum(np.sin(np.pi * f), 0.0), 0.7) * (1 - 0.3 * f) + 0.5
        cov = np.minimum(half - np.abs(ly) + 0.5, np.minimum(lx + 0.5, length - lx + 0.5))
        side = np.clip(ly / half, -1, 1)
        shade = 0.82 + 0.18 * side + 0.12 * f
        mid = np.clip(1.6 - np.abs(ly), 0, 1) * (f < 0.92)
        shade = shade * (1 - mid * (1 - vein))
        # side veins: faint diagonal lines
        vv = np.abs(np.sin((lx - np.abs(ly) * 0.9) * 0.28))
        shade = shade * (0.93 + 0.07 * vv)
        col = np.array(col, np.float32)[None, None, :] * shade[..., None]
        self._over(sl, cov, col)

    def blob(self, c, r, col):
        box = self._box(c[0] - r - 2, c[1] - r - 2, c[0] + r + 2, c[1] + r + 2)
        if box is None:
            return
        sl, xs, ys = box
        d = np.hypot(xs - c[0], ys - c[1])
        self._over(sl, r - d + 0.5, np.array(col, np.float32))


def paint_atlas(rnd):
    cv = Canvas()

    # Needle spray (conifer branch / fern frond): twig up the middle (v = base -> tip), side twiglets, dense needles.
    x0, y0, x1, y1 = cv.region("needles", (0.7, 0.76, 0.6))
    w, h = x1 - x0, y1 - y0
    cx = x0 + w / 2

    def envelope(v):
        return max(0.0, math.sin(math.pi * min(1.0, v * 1.05)) ** 0.75) * (1.0 - 0.25 * v)

    def needles_along(pts, length, density, spread, col_base, col_tip):
        for i in range(len(pts) - 1):
            p, q = pts[i], pts[i + 1]
            d = (q[0] - p[0], q[1] - p[1])
            ln = math.hypot(*d)
            if ln < 1e-3:
                continue
            ang = math.atan2(d[1], d[0])
            steps = max(1, int(ln / density))
            for s in range(steps):
                t = (s + rnd.random()) / steps
                b = (p[0] + d[0] * t, p[1] + d[1] * t)
                for side in (-1, 1):
                    a = ang + side * math.radians(rnd.uniform(*spread))
                    L = length * rnd.uniform(0.8, 1.15)
                    e = (b[0] + math.cos(a) * L, b[1] + math.sin(a) * L)
                    k = rnd.uniform(0.75, 1.0)
                    cv.seg(b, e, 3.2, 1.2, mul(col_base, k), mul(col_tip, k))

    main = [(cx + 6 * math.sin(v * 2.4), y0 + 8 + v * (h - 20)) for v in np.linspace(0, 1, 16)]
    twig = (0.6, 0.52, 0.42)
    nb, nt = (0.72, 0.8, 0.62), (1.0, 1.0, 0.85)
    for k in range(17):
        v = 0.06 + k * 0.053
        if v > 0.93:
            break
        by = y0 + 8 + v * (h - 20)
        bx = cx + 6 * math.sin(v * 2.4)
        for side in (-1, 1):
            L = (w / 2 - 34) * envelope(v + 0.05) * rnd.uniform(0.85, 1.05)
            if L < 20:
                continue
            a = math.radians(90 - side * rnd.uniform(52, 62))
            pts = [(bx + math.cos(a) * L * t + side * 6 * t * t * 0, by + math.sin(a) * L * t) for t in np.linspace(0, 1, 6)]
            cv.curve(pts, 4.0, 1.5, twig, twig)
            needles_along(pts, 22, 4.2, (40, 70), nb, nt)
    cv.curve(main, 7.0, 2.0, twig, twig)
    needles_along(main[1:], 24, 4.0, (35, 70), nb, nt)

    # Leaf cluster (broadleaf trees, bushes): stems from the bottom middle, ovate leaves pointing up and out.
    x0, y0, x1, y1 = cv.region("leaves", (0.7, 0.78, 0.55))
    w, h = x1 - x0, y1 - y0
    base = (x0 + w / 2, y0 + 10)
    stems = []
    for a_deg, L in ((90, 0.62), (62, 0.5), (118, 0.5), (40, 0.36), (140, 0.36)):
        a = math.radians(a_deg + rnd.uniform(-6, 6))
        L *= h
        pts = [(base[0] + math.cos(a) * L * t, base[1] + math.sin(a) * L * t + 18 * math.sin(math.pi * t) * 0.3) for t in np.linspace(0, 1, 5)]
        stems.append((pts, a))
    leaves = []
    for pts, a in stems:
        cv.curve(pts, 5.0, 2.0, (0.42, 0.36, 0.26), (0.5, 0.48, 0.3))
        n = len(pts)
        for i in range(1, n):
            p = pts[i]
            for side in ((-1, 1) if i < n - 1 else (0,)):
                la = a + side * math.radians(rnd.uniform(30, 55))
                L = rnd.uniform(78, 118) * (0.85 if i == 1 else 1.0)
                leaves.append((p, la, L, L * rnd.uniform(0.48, 0.58)))
    rnd.shuffle(leaves)
    for p, la, L, W in leaves:
        k = rnd.uniform(0.72, 1.0)
        tint = rnd.choice([(0.86, 0.98, 0.66), (0.8, 0.95, 0.62), (0.95, 0.98, 0.6), (0.78, 0.9, 0.7)])
        cv.leaf(p, la, L, W, mul(tint, k))

    # Grass blades: rooted along the bottom (v = 0.5 in the atlas), curving up and out.
    x0, y0, x1, y1 = cv.region("grass", (0.7, 0.76, 0.52))
    w, h = x1 - x0, y1 - y0
    blades = []
    for i in range(115):
        bx = x0 + 30 + rnd.random() * (w - 60)
        top = h * rnd.uniform(0.35, 0.96)
        lean = rnd.uniform(-0.35, 0.35) * top + (bx - (x0 + w / 2)) * 0.35
        lean = max(x0 + 12 - bx, min(x1 - 12 - bx, lean))
        bend = rnd.uniform(0.2, 0.6)
        pts = []
        for t in np.linspace(0, 1, 9):
            pts.append((bx + lean * (t ** (1 + bend)), y0 + 8 + top * t * (1 - 0.12 * bend * t)))
        dry = rnd.random() < 0.18
        k = rnd.uniform(0.75, 1.0)
        tip = (1.0, 0.92, 0.62) if dry else (0.88, 1.0, 0.7)
        blades.append((top, pts, rnd.uniform(7, 11), mul((0.55, 0.6, 0.42), k), mul(tip, k)))
    blades.sort(key=lambda b: -b[0])  # tall ones behind
    for _, pts, wd, c0, c1 in blades:
        cv.curve(pts, wd, 1.0, c0, c1)

    # Dry twigs (desert shrub, dead tree tips): forked twigs from the bottom middle, tiny grey-green leaves.
    x0, y0, x1, y1 = cv.region("twigs", (0.55, 0.55, 0.45))
    w, h = x1 - x0, y1 - y0

    def twig_branch(p, a, L, depth, width):
        q = (p[0] + math.cos(a) * L, p[1] + math.sin(a) * L)
        cv.seg(p, q, width, width * 0.6, (0.5, 0.42, 0.34), (0.58, 0.5, 0.4))
        if depth == 0:
            for _ in range(5):
                t = rnd.random()
                c = (p[0] + (q[0] - p[0]) * t + rnd.uniform(-6, 6), p[1] + (q[1] - p[1]) * t + rnd.uniform(-6, 6))
                cv.leaf(c, a + rnd.uniform(-1.2, 1.2), rnd.uniform(14, 22), rnd.uniform(6, 9), mul((0.8, 0.86, 0.7), rnd.uniform(0.75, 1.0)), 0.9)
            return
        for _ in range(2 if depth > 1 else 3):
            twig_branch(q, a + rnd.uniform(-0.65, 0.65), L * rnd.uniform(0.55, 0.75), depth - 1, width * 0.65)

    for a_deg in (70, 90, 110, 50, 130):
        twig_branch((x0 + w / 2 + rnd.uniform(-20, 20), y0 + 6), math.radians(a_deg + rnd.uniform(-8, 8)), h * 0.42, 3, 7.0)

    # Bark: vertical fibres and cracks, opaque.
    x0, y0, x1, y1 = cv.region("bark", (0.7, 0.7, 0.7))
    w, h = x1 - x0, y1 - y0
    xs = np.arange(w, dtype=np.float32)[None, :]
    ys = np.arange(h, dtype=np.float32)[:, None]
    fib = np.zeros((h, w), np.float32)
    for k in range(1, 7):
        ph = rnd.random() * 6.28
        fib += np.sin(xs * 0.09 * k * 2.2 + np.sin(ys * 0.02 * k + ph) * 1.2 + ph + rnd.random() * np.sin(ys * 0.11 * k)) / k
    fib = 0.72 + 0.16 * fib / 2.0
    cracks = np.abs(np.sin(xs * 0.11 + np.sin(ys * 0.02) * 3.0)) < 0.12
    fib = np.where(cracks, fib * 0.55, fib)
    cv.rgb[y0:y1, x0:x1] = np.clip(fib, 0.3, 1.0)[..., None] * np.array([1.0, 0.97, 0.93], np.float32)
    cv.a[y0:y1, x0:x1] = 1.0

    # Speckle: opaque dense foliage (crown cores, far stand-ins): a carpet of short random needles.
    x0, y0, x1, y1 = cv.region("speckle", (0.6, 0.66, 0.5))
    cv.a[y0:y1, x0:x1] = 1.0
    w, h = x1 - x0, y1 - y0
    old_clip = cv.clip
    cv.clip = (x0, y0, x1, y1)
    for _ in range(2600):
        p = (x0 + rnd.random() * w, y0 + rnd.random() * h)
        a = rnd.random() * 6.28
        L = rnd.uniform(6, 14)
        k = rnd.uniform(0.6, 1.05)
        cv.seg(p, (p[0] + math.cos(a) * L, p[1] + math.sin(a) * L), 3.0, 1.5, mul((0.75, 0.82, 0.62), k), mul((1.0, 1.0, 0.85), k))
    cv.clip = old_clip
    cv.a[y0:y1, x0:x1] = 1.0

    # Halve to 512 (box filter; transparent pixels keep their region's background colour, so mips don't fringe).
    rgb = cv.rgb.reshape(S // 2, 2, S // 2, 2, 3).mean(axis=(1, 3))
    a = cv.a.reshape(S // 2, 2, S // 2, 2).mean(axis=(1, 3))
    rgba = np.concatenate([np.clip(rgb, 0, 1), np.clip(a, 0, 1)[..., None]], axis=2)
    img = bpy.data.images.new("scatter_atlas", S // 2, S // 2, alpha=True)
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    img.file_format = "PNG"
    return img


# ---------------------------------------------------------------- mesh builder


class MB:
    """Collects vertices with per-vertex normal, atlas UV, wind UV2 and linear colour; one face list."""

    def __init__(self):
        self.co, self.no, self.uv, self.uv2, self.col, self.faces = [], [], [], [], [], []

    def v(self, co, no=(0, 0, 1), uv=(0, 0), uv2=(0, 0), col=(1, 1, 1)):
        self.co.append(tuple(co))
        n = Vector(no)
        self.no.append(tuple(n.normalized() if n.length > 1e-6 else Vector((0, 0, 1))))
        self.uv.append(tuple(uv))
        self.uv2.append(tuple(uv2))
        self.col.append(tuple(col))
        return len(self.co) - 1

    def f(self, *idx):
        self.faces.append(tuple(idx))

    def tris(self):
        return sum(len(f) - 2 for f in self.faces)

    # A strip along a polyline: side vectors give the half-width direction at each point.
    def strip(self, pts, sides, widths, normals, region, cols, flex, phase, u0=0.0, u1=1.0, v0=0.0, v1=1.0):
        n = len(pts)
        rows = []
        for i in range(n):
            t = i / (n - 1)
            v = v0 + (v1 - v0) * t
            p, s, w = Vector(pts[i]), Vector(sides[i]), widths[i]
            a = self.v(p - s * w / 2, normals[i], ruv(region, u0, v), (flex[i], phase), cols[i])
            b = self.v(p + s * w / 2, normals[i], ruv(region, u1, v), (flex[i], phase), cols[i])
            rows.append((a, b))
        for i in range(n - 1):
            a0, b0 = rows[i]
            a1, b1 = rows[i + 1]
            self.f(a0, b0, b1, a1)

    def tube(self, pts, radii, sides, region, cols, flex, phase=0.0, normal_up=0.0, cap=False):
        """Tapered tube along a polyline (bark or speckle), UV seam duplicated, outward normals."""
        n = len(pts)
        pts = [Vector(p) for p in pts]
        rings = []
        for i in range(n):
            d = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            ref = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
            x = d.cross(ref).normalized()
            y = d.cross(x).normalized()
            ring = []
            for j in range(sides + 1):
                a = 2 * math.pi * j / sides
                off = x * math.cos(a) + y * math.sin(a)
                nn = off + Vector((0, 0, normal_up))
                uv = ruv(region, j / sides, i / max(1, n - 1))
                ring.append(self.v(pts[i] + off * radii[i], nn, uv, (flex[i], phase), cols[i]))
            rings.append(ring)
        for i in range(n - 1):
            for j in range(sides):
                self.f(rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j])
        if cap:
            c = self.v(pts[-1], Vector((0, 0, 1)), ruv(region, 0.5, 1.0), (flex[-1], phase), cols[-1])
            for j in range(sides):
                self.f(rings[-1][j], rings[-1][j + 1], c)

    def card(self, centre, xaxis, yaxis, w, h, normal, region, col, flex, phase, segs=1, droop=0.0, uvr=(0, 1, 0, 1), cols=None):
        """A quad (optionally split along its height, drooping) centred at the bottom middle of its texture."""
        centre, xaxis, yaxis = Vector(centre), Vector(xaxis).normalized(), Vector(yaxis).normalized()
        pts, sides, widths, normals, cl, fl = [], [], [], [], [], []
        for i in range(segs + 1):
            t = i / segs
            pts.append(centre + yaxis * h * t - Vector((0, 0, droop * t * t)))
            sides.append(xaxis)
            widths.append(w)
            normals.append(normal if not callable(normal) else normal(t))
            cl.append(col if cols is None else cols(t))
            fl.append(flex(t) if callable(flex) else flex)
        self.strip(pts, sides, widths, normals, region, cl, fl, phase, uvr[0], uvr[1], uvr[2], uvr[3])

    def merge(self, other, matrix=Matrix()):
        base = len(self.co)
        rot = matrix.to_3x3()
        for i in range(len(other.co)):
            self.co.append(tuple(matrix @ Vector(other.co[i])))
            self.no.append(tuple((rot @ Vector(other.no[i])).normalized()))
            self.uv.append(other.uv[i])
            self.uv2.append(other.uv2[i])
            self.col.append(other.col[i])
        for f in other.faces:
            self.faces.append(tuple(base + i for i in f))

    def build(self, name, material):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.co, [], self.faces)
        me.validate(clean_customdata=False)
        uv = me.uv_layers.new(name="UVMap")
        uv2 = me.uv_layers.new(name="Wind")
        for loop in me.loops:
            uv.data[loop.index].uv = self.uv[loop.vertex_index]
            uv2.data[loop.index].uv = self.uv2[loop.vertex_index]
        me.uv_layers.active = uv
        attr = me.color_attributes.new("Color", "FLOAT_COLOR", "POINT")
        for i, c in enumerate(self.col):
            attr.data[i].color = (c[0], c[1], c[2], 1.0)
        me.color_attributes.active_color = attr
        me.shade_smooth()
        me.normals_split_custom_set_from_vertices(self.no)
        me.materials.append(material)
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ---------------------------------------------------------------- plants

TAU = 2 * math.pi
UP = Vector((0, 0, 1))


def perp_basis(axis, spin):
    """Two unit vectors perpendicular to axis, turned by spin."""
    axis = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    e1 = axis.cross(ref).normalized()
    e2 = axis.cross(e1).normalized()
    return e1 * math.cos(spin) + e2 * math.sin(spin), -e1 * math.sin(spin) + e2 * math.cos(spin)


def conifer(H, R, whorls, per, crown0, droop, needle, seed, profile=1.0, bark=(0.33, 0.25, 0.19)):
    """Spruce / fir: a tapered trunk, a dark needle core cone, and whorls of drooping branch sprays (two crossed
    cards each, normals bent outward so the crown shades as one volume)."""
    rnd = random.Random(seed)
    mb = MB()
    bark = srgb(bark)
    needle = srgb(needle)
    tr = 0.2 * H / 8.5 + 0.04
    mb.tube([(0, 0, -0.4), (0, 0, H * 0.3), (0, 0, H * 0.7), (0, 0, H * 0.98)], [tr, tr * 0.72, tr * 0.38, 0.02], 6, "bark",
            [mul(bark, 0.7), bark, bark, bark], [0, 0.05, 0.25, 0.5])
    z_lo, z_hi = max(0.45, crown0 * H), H * 0.93
    core = [(0, 0, z_lo + 0.2), (0, 0, z_lo + (H - z_lo) * 0.3), (0, 0, z_lo + (H - z_lo) * 0.62), (0, 0, H * 0.96)]
    core_r = [R * 0.32 * (1 - (c[2] - z_lo) / (H - z_lo)) ** profile + 0.05 for c in core]
    mb.tube(core, core_r, 7, "speckle", [mul(needle, 0.3), mul(needle, 0.36), mul(needle, 0.45), mul(needle, 0.6)],
            [0.05, 0.15, 0.3, 0.5], normal_up=0.8)
    for i in range(whorls):
        fz = i / (whorls - 1)
        z = z_lo + (z_hi - z_lo) * fz + rnd.uniform(-0.3, 0.3) * (z_hi - z_lo) / whorls
        f = max(0.05, (H - z) / (H - z_lo))
        r = R * f ** profile * rnd.uniform(0.86, 1.08) + 0.3
        nb = per if f > 0.4 else max(3, per - 2)
        a0 = rnd.random() * TAU
        for j in range(nb):
            if f > 0.3 and rnd.random() < 0.07:
                continue  # a gap here and there
            a = a0 + j * TAU / nb + rnd.uniform(-0.35, 0.35)
            L = r * rnd.uniform(0.85, 1.1)
            d = Vector((math.cos(a), math.sin(a), 0))
            side = Vector((-math.sin(a), math.cos(a), 0))
            up = rnd.uniform(0.0, 0.25)
            dr = droop * (0.45 + 0.75 * f)
            ts = (0.0, 0.5, 1.0)
            pts = [d * (0.08 + L * t) + Vector((0, 0, z + up * L * t - dr * L * t * t)) for t in ts]
            W = L * 0.8 + 0.3
            ph = rnd.random()
            k = rnd.uniform(0.85, 1.12)
            height_shade = 0.72 + 0.38 * (1 - f)
            cols = [mul(needle, (0.42 + 0.58 * t) * height_shade * k) for t in ts]
            normals = [(d * (0.35 + 0.55 * t) + UP * 0.9).normalized() for t in ts]
            flex = [0.15 + 0.85 * t for t in ts]
            for tilt in (0.42, -0.42):
                s = side * math.cos(tilt) + UP * math.sin(tilt)
                mb.strip(pts, [s] * 3, [W * (0.75 + 0.25 * t) for t in ts], normals, "needles", cols, flex, ph)
    # leader: two crossed upright sprays at the top
    for a in (0.0, math.pi / 2):
        x = Vector((math.cos(a), math.sin(a), 0))
        mb.card((0, 0, H * 0.84), x, UP, 0.75, H * 0.2, (UP * 1.0 + x * 0.2), "needles", mul(needle, 1.05),
                lambda t: 0.4 + 0.6 * t, rnd.random(), segs=1)
    return mb


def conifer_far(H, R, crown0, needle, profile=1.0, tiers=3, bark=(0.33, 0.25, 0.19)):
    """~50 triangles: three serrated skirts on a trunk stub, for distant forest."""
    mb = MB()
    needle = srgb(needle)
    bark = srgb(bark)
    z_lo = max(0.45, crown0 * H)
    mb.tube([(0, 0, -0.3), (0, 0, z_lo + 0.4)], [0.2 * H / 8.5 + 0.04] * 2, 4, "bark", [bark, bark], [0, 0])
    for k in range(tiers):
        zb = z_lo + (H - z_lo) * k / tiers * 0.82
        za = H if k == tiers - 1 else zb + (H - zb) * 0.62
        rb = R * ((H - zb) / (H - z_lo)) ** profile + 0.3
        apex = mb.v((0, 0, za), UP, ruv("speckle", 0.5, 1.0), (0.5, 0.0), mul(needle, 0.95))
        rim = []
        n = 12
        for j in range(n):
            a = TAU * j / n + k * 0.4
            r = rb * (1.0 if j % 2 == 0 else 0.78)
            dz = -0.22 * rb if j % 2 == 0 else -0.05 * rb
            d = Vector((math.cos(a), math.sin(a), 0))
            rim.append(mb.v(d * r + Vector((0, 0, zb + dz)), (d * 0.6 + UP * 0.8), ruv("speckle", j / n, 0.0), (0.3, 0.0), mul(needle, 0.62)))
        for j in range(n):
            mb.f(rim[j], rim[(j + 1) % n], apex)
    return mb


def blob(mb, c, radii, colour, seg=8, rings=4, region="speckle", dark=0.5, seed=0, lumpy=0.0, flex=0.4):
    """Low UV-sphere (opaque), darker at the bottom: crown cores and far stand-ins."""
    rnd = random.Random(seed)
    off = Vector((rnd.random() * 50, rnd.random() * 50, rnd.random() * 50))
    verts = []
    for i in range(rings + 1):
        th = math.pi * i / rings
        row = []
        for j in range(seg + 1):
            ph = TAU * j / seg
            d = Vector((math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)))
            r = 1 + lumpy * noise.noise(d * 1.6 + off)
            p = c + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * r
            shade = dark + (1 - dark) * (0.5 + 0.5 * d.z)
            row.append(mb.v(p, d, ruv(region, j / seg, 1 - i / rings), (flex, 0.0), mul(colour, shade)))
        verts.append(row)
    for i in range(rings):
        for j in range(seg):
            if i == 0:
                mb.f(verts[0][j], verts[1][j], verts[1][j + 1])
            elif i == rings - 1:
                mb.f(verts[i][j], verts[i + 1][j], verts[i][j + 1])
            else:
                mb.f(verts[i][j], verts[i + 1][j], verts[i + 1][j + 1], verts[i][j + 1])


def pine(H, seed, needle=(0.24, 0.34, 0.2)):
    """Scots-pine-like hardy tree for hills: tall bare trunk (grey-brown low, orange high), a few upswept limbs, each
    ending in a rounded, flattened clump of needle sprays around a dark core (an irregular, open crown)."""
    rnd = random.Random(seed)
    mb = MB()
    needle = srgb(needle)
    low, high = srgb((0.3, 0.26, 0.23)), srgb((0.62, 0.38, 0.22))
    lean = Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), 0))
    trunk = [Vector((0, 0, -0.4)), Vector((0, 0, H * 0.3)) + lean * 0.4, Vector((0, 0, H * 0.6)) + lean, Vector((0, 0, H * 0.86)) + lean * 1.2]
    mb.tube(trunk, [0.26, 0.21, 0.15, 0.07], 6, "bark", [low, lerp3(low, high, 0.5), high, high], [0, 0.05, 0.2, 0.45])
    clumps = [(trunk[-1] + Vector((0, 0, 0.35)), 1.55)]
    n = 6
    a0 = rnd.random() * TAU
    for i in range(n):
        f = i / (n - 1)
        z = H * (0.56 + 0.26 * f + rnd.uniform(-0.03, 0.03))
        a = a0 + i * TAU * 0.382 + rnd.uniform(-0.3, 0.3)  # golden-angle spiral
        L = rnd.uniform(1.5, 2.3) * (1.15 - 0.55 * f)
        start = Vector((0, 0, z)) + lean * (z / H / 0.6)
        end = start + Vector((math.cos(a) * L, math.sin(a) * L, L * rnd.uniform(0.35, 0.65)))
        mid = start.lerp(end, 0.5) + Vector((0, 0, 0.2))
        mb.tube([start, mid, end], [0.1, 0.065, 0.035], 4, "bark", [high] * 3, [0.2, 0.35, 0.55])
        clumps.append((end, rnd.uniform(1.15, 1.55) * (1.1 - 0.25 * f)))
    centre = sum((c for c, _ in clumps), Vector()) / len(clumps)
    for c, size in clumps:
        # flattened needle clump: a dark core plus sprays radiating from it, leaning up and out of the whole crown
        blob(mb, c, (size * 0.62, size * 0.62, size * 0.36), mul(needle, 0.55), 6, 3, seed=rnd.randint(0, 999), dark=0.45,
             lumpy=0.2, flex=0.45)
        out_c = (c - centre)
        out_c = (out_c.normalized() if out_c.length > 1e-3 else UP)
        for k in range(11):
            d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.35, 0.9)))
            if d.length < 1e-3:
                continue
            d.normalize()
            p = c + Vector((d.x * size * 0.55, d.y * size * 0.55, d.z * size * 0.3))
            out = (d * 0.65 + out_c * 0.35 + UP * 0.25).normalized()
            x, y = perp_basis(out, rnd.random() * TAU)
            if y.z < 0:
                y = -y
            sz = size * rnd.uniform(0.85, 1.15)
            shade = (0.7 + 0.42 * max(0.0, d.z)) * rnd.uniform(0.88, 1.1)
            nrm = (out * 0.8 + UP * 0.45).normalized()
            ph = rnd.random()
            mb.card(p - y * sz * 0.45, x, y, sz, sz, nrm, "needles", mul(needle, shade), lambda t: 0.55 + 0.45 * t, ph)
            mb.card(p - y * sz * 0.45, out, y, sz * 0.85, sz, nrm, "needles", mul(needle, shade * 0.88), lambda t: 0.55 + 0.45 * t, ph)
    return mb


def pine_far(H, needle=(0.24, 0.34, 0.2)):
    mb = MB()
    needle = srgb(needle)
    high = srgb((0.55, 0.36, 0.24))
    mb.tube([(0, 0, -0.3), (0, 0, H * 0.75)], [0.22, 0.1], 4, "bark", [high, high], [0, 0])
    blob(mb, Vector((0, 0, H * 0.82)), Vector((2.8, 2.8, 1.1)), needle, 6, 3, region="speckle", dark=0.55, lumpy=0.2)
    return mb


def leafy_crown(mb, rnd, C, crown, lobes, clusters, leaf, size, region="leaves", dark=0.5, blob_dark=0.4):
    """Leaf-cluster cards on the surface of a few overlapping lobes, normals pointing out from the crown centre."""
    lob = []
    for k in range(lobes):
        d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.3, 0.9)))
        if d.length > 1e-3:
            d.normalize()
        c = C + Vector((d.x * crown[0] * 0.42, d.y * crown[1] * 0.42, d.z * crown[2] * 0.32))
        r = (crown[0] + crown[1] + crown[2]) / 3 * rnd.uniform(0.5, 0.66)
        lob.append((c, r))
    for c, r in lob:
        blob(mb, c, (r * 0.7, r * 0.7, r * 0.62), mul(leaf, blob_dark + 0.25), 6, 3, seed=rnd.randint(0, 999), dark=0.6, flex=0.3)
    placed = 0
    tries = 0
    while placed < clusters and tries < clusters * 20:
        tries += 1
        c, r = rnd.choice(lob)
        d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.55, 1)))
        if d.length < 1e-3:
            continue
        d.normalize()
        p = c + d * r * rnd.uniform(0.82, 1.05)
        if any((p - c2).length < r2 * 0.78 for c2, r2 in lob if c2 is not c):
            continue
        placed += 1
        out = ((p - C).normalized() * 0.7 + d * 0.3).normalized()
        sz = size * rnd.uniform(0.8, 1.2)
        x, y = perp_basis(out, rnd.random() * TAU)
        if y.z < 0:
            y = -y
        rel = (p.z - (C.z - crown[2])) / (2 * crown[2])
        shade = (dark + (1 - dark) * max(0.0, min(1.0, rel))) * rnd.uniform(0.85, 1.12)
        tint = leaf if rnd.random() > 0.15 else lerp3(leaf, mul(leaf, 1.3), 0.6)
        n = (out * 0.85 + UP * 0.3).normalized()
        ph = rnd.random()
        mb.card(p - y * sz * 0.5, x, y, sz, sz, n, region, mul(tint, shade), lambda t: 0.55 + 0.45 * t, ph)
        mb.card(p - y * sz * 0.5, out, y, sz * 0.9, sz, n, region, mul(tint, shade * 0.9), lambda t: 0.55 + 0.45 * t, ph)
    return lob


def broadleaf(H, crown, lobes, clusters, leaf, bark, seed, trunk_frac=0.4, size=1.5, limbs=4):
    """Deciduous tree for plains: trunk splitting into limbs, a lumpy crown of leaf-cluster cards around dark cores."""
    rnd = random.Random(seed)
    mb = MB()
    leaf, bark = srgb(leaf), srgb(bark)
    k = H / 7.5
    top = Vector((rnd.uniform(-0.1, 0.1), rnd.uniform(-0.1, 0.1), H * trunk_frac))
    mb.tube([Vector((0, 0, -0.4)), Vector((0, 0, H * trunk_frac * 0.5)), top], [0.32 * k, 0.25 * k, 0.21 * k], 7, "bark",
            [mul(bark, 0.75), bark, bark], [0, 0.02, 0.1])
    C = Vector((0, 0, H - crown[2]))
    lob = leafy_crown(mb, rnd, C, crown, lobes, clusters, leaf, size)
    for c, r in lob[:limbs]:
        end = c.lerp(top, 0.25)
        mid = top.lerp(end, 0.5) + Vector((0, 0, 0.3))
        mb.tube([top, mid, end], [0.17 * k, 0.11 * k, 0.05 * k], 5, "bark", [bark] * 3, [0.1, 0.25, 0.45])
    return mb


def broadleaf_far(H, crown, leaf, bark, trunk_frac=0.4, seed=0):
    mb = MB()
    leaf, bark = srgb(leaf), srgb(bark)
    mb.tube([(0, 0, -0.3), (0, 0, H * trunk_frac + 0.5)], [0.3 * H / 7.5, 0.22 * H / 7.5], 4, "bark", [bark, bark], [0, 0])
    blob(mb, Vector((0, 0, H - crown[2])), crown, mul(leaf, 0.85), 8, 4, dark=0.5, seed=seed, lumpy=0.18)
    return mb


def bush(seed, leaf, width=1.8, height=1.2, clusters=20, size=0.85):
    rnd = random.Random(seed)
    mb = MB()
    leaf = srgb(leaf)
    leafy_crown(mb, rnd, Vector((0, 0, height * 0.5)), (width / 2, width / 2, height / 2), 3, clusters, leaf, size, dark=0.45)
    return mb


def juniper(seed, needle, width=1.6, height=1.0):
    rnd = random.Random(seed)
    mb = MB()
    needle = srgb(needle)
    blob(mb, Vector((0, 0, height * 0.3)), (width * 0.3, width * 0.3, height * 0.35), mul(needle, 0.55), 6, 3, dark=0.5, seed=seed, flex=0.3)
    for i in range(16):
        a = i * TAU / 16 + rnd.uniform(-0.2, 0.2)
        e = math.radians(rnd.uniform(18, 62))
        d = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
        side = Vector((-math.sin(a), math.cos(a), 0))
        L = width * 0.5 * rnd.uniform(0.75, 1.05)
        kk = rnd.uniform(0.85, 1.1)
        mb.card(Vector((0, 0, 0.05)), side, d, L * 0.75, L * 1.1, (d * 0.6 + UP).normalized(), "needles", None,
                lambda t: 0.2 + 0.8 * t, rnd.random(), segs=2, droop=0.12,
                cols=lambda t, kk=kk: mul(needle, (0.5 + 0.55 * t) * kk))
    return mb


def shrub(seed, colour, width=1.3, height=0.75, cards=12):
    """Twiggy desert shrub (sagebrush / dry bush): upright fans of the twig texture around the root."""
    rnd = random.Random(seed)
    mb = MB()
    colour = srgb(colour)
    for i in range(cards):
        a = i * TAU / cards * 1.7 + rnd.uniform(-0.3, 0.3)
        e = math.radians(rnd.uniform(0, 45))
        lean = Vector((math.cos(a) * math.sin(e), math.sin(a) * math.sin(e), math.cos(e)))
        side = Vector((-math.sin(a), math.cos(a), 0))
        kk = rnd.uniform(0.85, 1.1)
        mb.card(Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, -0.05)), side, lean, width * rnd.uniform(0.6, 0.9), height * rnd.uniform(0.8, 1.1),
                (Vector((math.cos(a), math.sin(a), 0)) * 0.4 + UP).normalized(), "twigs", None, lambda t: t, rnd.random(), segs=1,
                cols=lambda t, kk=kk: mul(colour, (0.55 + 0.5 * t) * kk))
    return mb


def snag(H, seed, bark=(0.45, 0.42, 0.38)):
    """Dead, bleached tree: bent trunk, bare forked limbs, a few dry twig sprays."""
    rnd = random.Random(seed)
    mb = MB()
    bark = srgb(bark)
    pts = [Vector((0, 0, -0.3))]
    lean = Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), 0))
    for i in range(1, 6):
        t = i / 5
        pts.append(Vector((0, 0, H * t)) + lean * t * t * H * 0.25 + Vector((rnd.uniform(-0.1, 0.1), rnd.uniform(-0.1, 0.1), 0)))
    radii = [0.26 * (1 - 0.82 * i / 5) for i in range(6)]
    mb.tube(pts, radii, 6, "bark", [mul(bark, 0.6 + 0.4 * i / 5) for i in range(6)], [0.02 * i for i in range(6)])
    for i in range(5):
        t = rnd.uniform(0.35, 0.85)
        idx = min(4, int(t * 5))
        start = pts[idx].lerp(pts[idx + 1], t * 5 - idx)
        a = i * TAU / 5 + rnd.uniform(-0.4, 0.4)
        L = rnd.uniform(0.9, 1.8) * (1.15 - t)
        e = rnd.uniform(0.4, 1.0)
        end = start + Vector((math.cos(a) * L, math.sin(a) * L, L * e))
        mid = start.lerp(end, 0.5) + Vector((rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), 0.1))
        mb.tube([start, mid, end], [0.1, 0.06, 0.02], 4, "bark", [bark] * 3, [0.1, 0.2, 0.3])
        sub_end = mid + Vector((math.cos(a + 0.8) * L * 0.5, math.sin(a + 0.8) * L * 0.5, L * 0.45))
        mb.tube([mid, sub_end], [0.04, 0.015], 4, "bark", [bark] * 2, [0.2, 0.35])
        if i % 2 == 0:
            side = Vector((-math.sin(a), math.cos(a), 0))
            mb.card(end - Vector((0, 0, 0.1)), side, (end - mid).normalized(), 0.9, 0.5, UP, "twigs", mul(srgb((0.5, 0.42, 0.33)), 0.8), 0.6, rnd.random())
    return mb


def grass(seed, cards, width, height, lean, colour, offset=0.0, segs=2):
    """Grass clump: crossed blade cards (or a ring of outward-leaning ones), normals tipped up like the ground's."""
    rnd = random.Random(seed)
    mb = MB()
    colour = srgb(colour)
    for i in range(cards):
        a = (i * math.pi / cards if offset == 0 else i * TAU / cards) + rnd.uniform(-0.25, 0.25)
        out = Vector((math.cos(a + math.pi / 2), math.sin(a + math.pi / 2), 0))
        x = Vector((math.cos(a), math.sin(a), 0))
        sign = 1 if offset else rnd.choice((-1, 1))
        y = (UP + out * math.tan(math.radians(lean * rnd.uniform(0.6, 1.2))) * sign).normalized()
        u0 = rnd.uniform(0.0, 0.35)
        kk = rnd.uniform(0.85, 1.12)
        mb.card(out * offset - UP * 0.04, x, y, width * rnd.uniform(0.85, 1.1), height * rnd.uniform(0.8, 1.15),
                (UP + out * 0.25 * sign).normalized(), "grass", None, lambda t: t ** 1.5, rnd.random(),
                segs=segs, uvr=(u0, u0 + 0.65, 0.0, 1.0),
                cols=lambda t, kk=kk: mul(colour, (0.5 + 0.55 * t) * kk))
    return mb


def fern(seed, colour):
    rnd = random.Random(seed)
    mb = MB()
    colour = srgb(colour)
    for i in range(9):
        a = i * TAU / 9 + rnd.uniform(-0.25, 0.25)
        inner = i % 3 == 0
        e = math.radians(rnd.uniform(55, 75) if inner else rnd.uniform(25, 45))
        d = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
        side = Vector((-math.sin(a), math.cos(a), 0))
        L = rnd.uniform(0.6, 0.8) if inner else rnd.uniform(0.85, 1.1)
        kk = rnd.uniform(0.85, 1.1)
        mb.card(Vector((0, 0, 0.0)), side, d, L * 0.42, L, (Vector((math.cos(a), math.sin(a), 0)) * 0.3 + UP).normalized(), "needles", None,
                lambda t: t, rnd.random(), segs=3, droop=0.0 if inner else 0.35 * L,
                cols=lambda t, kk=kk: mul(colour, (0.55 + 0.5 * t) * kk))
    return mb


# ---------------------------------------------------------------- stone


def to_mesh_obj(name, bm, material):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.append(material)
    return obj


def decimated(obj, ratio, name):
    """A copy of obj with a collapse Decimate applied (new mesh, attributes interpolated)."""
    tmp = bpy.data.objects.new(name + "_tmp", obj.data)
    bpy.context.scene.collection.objects.link(tmp)
    mod = tmp.modifiers.new("dec", "DECIMATE")
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    bpy.data.objects.remove(tmp)
    me.name = name
    out = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(out)
    return out


def finish_stone(obj, colour_fn, sharp_deg=38.0):
    """Sharp edges over sharp_deg (crisp facets, smooth elsewhere), per-vertex colour from colour_fn(p, n, cavity),
    a box-projected UV."""
    me = obj.data
    while len(me.color_attributes):
        me.color_attributes.remove(me.color_attributes[0])
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.normal_update()
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(sharp_deg))
    cols = []
    for v in bm.verts:
        nb = [e.other_vert(v).co for e in v.link_edges]
        if nb:
            avg = sum(nb, Vector()) / len(nb)
            el = sum((x - v.co).length for x in nb) / len(nb)
            cav = (avg - v.co).dot(v.normal) / max(el, 1e-4)
        else:
            cav = 0.0
        cols.append(colour_fn(v.co.copy(), v.normal.copy(), cav))
    for layer in list(bm.loops.layers.uv):
        bm.loops.layers.uv.remove(layer)
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        for loop in f.loops:
            p = loop.vert.co
            loop[uv].uv = ((p.x + p.z) * 0.25, (p.y + p.z) * 0.25)
    bm.to_mesh(me)
    bm.free()
    attr = me.color_attributes.new("Color", "FLOAT_COLOR", "POINT")
    for i, c in enumerate(cols):
        attr.data[i].color = (c[0], c[1], c[2], 1.0)
    me.color_attributes.active_color = attr
    return obj


def stone_colour(base, seed, lichen=True, strata=None, ice=False):
    rnd = random.Random(seed)
    off = Vector((rnd.random() * 90, rnd.random() * 90, rnd.random() * 90))
    off2 = Vector((rnd.random() * 90, rnd.random() * 90, rnd.random() * 90))
    lich = [srgb((0.62, 0.64, 0.42)), srgb((0.78, 0.6, 0.3)), srgb((0.5, 0.56, 0.36))]
    base = srgb(base)

    def fn(p, n, cav):
        if strata:
            band = p.z * strata[1] + 0.35 * noise.noise(p * 0.7 + off)
            c = srgb(strata[0][int(math.floor(band)) % len(strata[0])])
            c = mul(c, 0.9 + 0.1 * math.sin((band % 1.0) * math.pi))
        else:
            c = base
        h = noise.noise(p * 2.6 + off)
        fine = noise.noise(p * 9.0 + off2)
        k = 0.8 + 0.32 * h + 0.18 * fine
        k *= max(0.35, min(1.2, 1.0 - cav * 4.0))
        if ice:
            k = 0.92 + 0.1 * h
            c = lerp3(c, srgb((0.3, 0.52, 0.72)), max(0.0, min(1.0, cav * 4.0 + 0.25 - 0.35 * n.z + 0.3 * fine)))
        if p.z < 0.25:
            k *= 0.72 + 0.28 * max(0.0, p.z / 0.25)
        c = mul(c, k)
        if lichen and n.z > 0.15:
            m = noise.noise(p * 2.4 + off2) * 0.5 + 0.5
            m = max(0.0, min(1.0, (m - 0.52) / 0.15)) * max(0.0, min(1.0, (n.z - 0.15) / 0.4))
            lc = lich[int(abs(noise.noise(p * 0.9 + off)) * 3) % 3]
            c = lerp3(c, mul(lc, 0.8 + 0.3 * fine), m * 0.75)
        return c

    return fn


def boulder_bm(seed, scale=(1.0, 0.9, 0.7), planes=9, lumpy=0.07, crisp=0.02, sub=4, bury=0.22, plane_range=(0.62, 0.9), chips=12):
    """A fractured rock: a lumpy sphere flattened against random cut planes (flat faces, sharp ridges), its
    flattened base sunk below z = 0 by `bury` of its height."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    off = Vector((rnd.random() * 99, rnd.random() * 99, rnd.random() * 99))
    cuts = []
    for _ in range(planes):
        n = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.4, 1.0))).normalized()
        span = math.sqrt((n.x * scale[0]) ** 2 + (n.y * scale[1]) ** 2 + (n.z * scale[2]) ** 2)  # ellipsoid support
        cuts.append((n, rnd.uniform(*plane_range) * span))
    for v in bm.verts:
        d = v.co.normalized()
        p = d * (1 + lumpy * noise.noise(d * 1.3 + off))
        p = Vector((p.x * scale[0], p.y * scale[1], p.z * scale[2]))
        for n, dist in cuts:
            over = p.dot(n) - dist
            if over > 0:
                p -= n * over * 0.96
        p += noise.noise_vector(p * 3.5 + off) * crisp + noise.noise_vector(p * 9.0 + off) * crisp * 0.4
        v.co = p
    # chip the corners: planes just inside the outermost point in random directions (fractured, beveled edges)
    for _ in range(chips):
        n = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.3, 1.0))).normalized()
        top = max(v.co.dot(n) for v in bm.verts)
        cut = top - rnd.uniform(0.04, 0.16) * (scale[0] + scale[1] + scale[2]) / 3
        for v in bm.verts:
            over = v.co.dot(n) - cut
            if over > 0:
                v.co -= n * over
    zmin = min(v.co.z for v in bm.verts)
    zmax = max(v.co.z for v in bm.verts)
    floor = zmin + (zmax - zmin) * 0.12
    for v in bm.verts:
        v.co.z = max(v.co.z, floor) - floor - (zmax - floor) * bury
    return bm


def merge_bms(parts):
    """Join (bmesh, matrix) pairs into one bmesh."""
    out = bmesh.new()
    for bm, m in parts:
        me = bpy.data.meshes.new("tmp")
        bm.to_mesh(me)
        bm.free()
        me.transform(m)
        out.from_mesh(me)
        bpy.data.meshes.remove(me)
    return out


def slab_stack(seed, slabs, radius, heights, wobble=0.32):
    """Layered sandstone: a stack of beveled, irregular slabs (wind-cut narrower or wider per layer)."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    off = Vector((rnd.random() * 50, rnd.random() * 50, 0))
    z = -0.25
    n = 10
    for s in range(slabs):
        h = heights[s] if isinstance(heights, (list, tuple)) else heights * rnd.uniform(0.7, 1.25)
        r = radius[s] if isinstance(radius, (list, tuple)) else radius
        cx, cy = rnd.uniform(-0.1, 0.1) * r, rnd.uniform(-0.1, 0.1) * r
        outline = []
        for j in range(n):
            a = TAU * j / n
            rr = r * (1 + wobble * noise.noise(Vector((math.cos(a) * 1.3, math.sin(a) * 1.3, s * 0.7)) + off)) * rnd.uniform(0.82, 1.08)
            outline.append((math.cos(a) * rr + cx, math.sin(a) * rr + cy))
        rings = []
        for fz, fr in ((0.0, 0.9), (0.12, 0.99), (0.85, 1.0), (1.0, 0.9)):
            rings.append([bm.verts.new((x * fr + cx * (1 - fr), y * fr + cy * (1 - fr), z + h * fz)) for x, y in outline])
        for a, b in zip(rings, rings[1:]):
            for j in range(n):
                bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
        top = bm.verts.new((cx, cy, z + h + 0.02))
        for j in range(n):
            bm.faces.new((rings[-1][j], rings[-1][(j + 1) % n], top))
        z += h * 0.97
    for v in bm.verts:
        v.co += noise.noise_vector(v.co * 1.7 + off) * 0.08 + noise.noise_vector(v.co * 5.0 + off) * 0.025
    bm.normal_update()
    return bm


# ---------------------------------------------------------------- the asset set

# Base colours (sRGB; converted to linear vertex colours). The game multiplies a per-instance tint on top.
NEEDLE_SPRUCE = (0.22, 0.33, 0.2)
NEEDLE_FIR = (0.25, 0.37, 0.21)
NEEDLE_YOUNG = (0.29, 0.43, 0.22)
NEEDLE_JUNIPER = (0.24, 0.34, 0.24)
LEAF_OAK = (0.33, 0.46, 0.19)
LEAF_BIRCH = (0.43, 0.54, 0.2)
LEAF_BUSH = (0.3, 0.42, 0.19)
BARK_OAK = (0.32, 0.27, 0.22)
BARK_BIRCH = (0.78, 0.76, 0.7)
SAGE = (0.55, 0.58, 0.46)
GRASS = (0.45, 0.58, 0.27)
FERN = (0.33, 0.52, 0.2)
STONE = (0.4, 0.38, 0.35)
ICE = (0.66, 0.82, 0.93)
SANDSTONE = ([(0.7, 0.52, 0.38), (0.8, 0.64, 0.48), (0.64, 0.45, 0.33), (0.86, 0.74, 0.58), (0.58, 0.4, 0.3), (0.76, 0.6, 0.45)], 3.2)

FOLIAGE, ROCK = "Foliage", "Rock"


def export_materials(atlas):
    """Plain materials for the glb: the game replaces them with its own shaders, but takes the atlas from Foliage."""
    fol = bpy.data.materials.new(FOLIAGE)
    nt = fol.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = atlas
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.85
    rock = bpy.data.materials.new(ROCK)
    rock.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
    return fol, rock


def build_assets(fol, rock):
    """Every asset object by name, plus its kind for the previews."""
    out = {}

    def plant(name, mb):
        out[name] = mb.build(name, fol)

    plant("conifer_a", conifer(8.5, 2.3, 13, 7, 0.06, 0.45, NEEDLE_SPRUCE, 11))
    plant("conifer_a_far", conifer_far(8.5, 2.3, 0.06, NEEDLE_SPRUCE))
    plant("conifer_b", conifer(10.0, 2.3, 11, 6, 0.14, 0.25, NEEDLE_FIR, 23, profile=1.15))
    plant("conifer_c", conifer(4.6, 1.5, 8, 6, 0.04, 0.3, NEEDLE_YOUNG, 37))
    plant("pine_a", pine(9.0, 41))
    plant("pine_a_far", pine_far(9.0))
    plant("broadleaf_a", broadleaf(7.5, (3.1, 3.1, 2.5), 5, 85, LEAF_OAK, BARK_OAK, 51))
    plant("broadleaf_a_far", broadleaf_far(7.5, (3.1, 3.1, 2.5), LEAF_OAK, BARK_OAK, seed=51))
    plant("broadleaf_b", broadleaf(8.2, (1.9, 1.9, 3.0), 4, 70, LEAF_BIRCH, BARK_BIRCH, 61, trunk_frac=0.5, size=1.2, limbs=3))
    plant("bush_a", bush(71, LEAF_BUSH))
    plant("bush_b", juniper(73, NEEDLE_JUNIPER))
    plant("shrub_desert", shrub(79, SAGE))
    plant("snag", snag(5.0, 83))
    plant("grass_a", grass(91, 3, 1.2, 0.8, 8, GRASS))
    plant("grass_b", grass(93, 5, 0.9, 1.15, 18, GRASS, offset=0.12))
    plant("grass_c", grass(95, 4, 0.7, 0.45, 32, GRASS, offset=0.08, segs=1))
    plant("fern", fern(97, FERN))

    def stone(name, bm, colour, near=1.0, far=None, sharp=38.0):
        src = to_mesh_obj(name + "_src", bm, rock)
        obj = decimated(src, near, name) if near < 1.0 else src
        if obj is src:
            src.name = name
            src.data.name = name
        out[name] = finish_stone(obj, colour, sharp)
        if far:
            out[name + "_far"] = finish_stone(decimated(obj, far / near if near < 1.0 else far, name + "_far"), colour, sharp)
        if obj is not src:
            bpy.data.objects.remove(src)

    stone("boulder_a", boulder_bm(101, (1.1, 0.95, 0.75)), stone_colour(STONE, 1), 0.34, 0.045, sharp=24)
    stone("boulder_b", boulder_bm(103, (1.25, 0.85, 0.55), 13, lumpy=0.05), stone_colour(STONE, 2), 0.34, sharp=24)
    stone("boulder_c", boulder_bm(107, (0.85, 0.8, 1.05), 10, lumpy=0.1), stone_colour(STONE, 3), 0.34, sharp=24)

    rnd = random.Random(113)
    parts = []
    for i, (x, y, s) in enumerate(((0, 0, 1.0), (1.3, 0.5, 0.65), (-0.9, 0.9, 0.55), (0.6, -1.1, 0.5))):
        parts.append((boulder_bm(200 + i, (1.0, 0.9, 0.75), 6 + i, sub=3), Matrix.Translation((x, y, 0)) @ Matrix.Rotation(rnd.random() * TAU, 4, "Z") @ Matrix.Scale(s, 4)))
    for i in range(6):
        a, r = rnd.random() * TAU, rnd.uniform(1.2, 2.2)
        parts.append((boulder_bm(220 + i, (1.0, 0.85, 0.6), 4, sub=2, lumpy=0.2), Matrix.Translation((math.cos(a) * r, math.sin(a) * r, 0)) @ Matrix.Scale(rnd.uniform(0.15, 0.3), 4)))
    stone("rock_cluster", merge_bms(parts), stone_colour(STONE, 4), 0.42, 0.07)

    def pebbles(seed, count, spread, size):
        rnd = random.Random(seed)
        parts = []
        for i in range(count):
            a, r = rnd.random() * TAU, spread * math.sqrt(rnd.random())
            parts.append((boulder_bm(seed * 10 + i, (1.0, rnd.uniform(0.6, 0.95), rnd.uniform(0.4, 0.7)), 4, sub=2, lumpy=0.2, bury=0.3),
                          Matrix.Translation((math.cos(a) * r, math.sin(a) * r, 0)) @ Matrix.Rotation(rnd.random() * TAU, 4, "Z") @ Matrix.Scale(rnd.uniform(*size), 4)))
        return merge_bms(parts)

    stone("scree", pebbles(31, 16, 2.2, (0.12, 0.42)), stone_colour(STONE, 5, lichen=False), 0.32)
    stone("pebbles", pebbles(37, 7, 1.2, (0.08, 0.22)), stone_colour(STONE, 6, lichen=False), 0.35)
    stone("sandstone_a", slab_stack(131, 5, [1.45, 1.35, 1.15, 1.3, 1.1], 0.36), stone_colour(STONE, 7, lichen=False, strata=SANDSTONE), 1.0, 0.25, sharp=35)
    stone("sandstone_b", slab_stack(137, 7, [0.85, 0.78, 0.6, 0.55, 0.62, 0.5, 1.05], [0.45, 0.5, 0.45, 0.5, 0.45, 0.5, 0.32], wobble=0.2),
          stone_colour(STONE, 8, lichen=False, strata=SANDSTONE), 1.0, sharp=35)
    stone("ice_a", boulder_bm(141, (1.3, 0.95, 0.8), 13, lumpy=0.08, crisp=0.008, plane_range=(0.38, 0.7)), stone_colour(ICE, 9, lichen=False, ice=True), 0.22, 0.04, sharp=30)
    rnd = random.Random(149)
    shards = []
    for i in range(4):
        tilt = Matrix.Rotation(rnd.uniform(-0.5, 0.5), 4, "X") @ Matrix.Rotation(rnd.uniform(-0.5, 0.5), 4, "Y")
        shards.append((boulder_bm(150 + i, (0.55, 0.45, 1.5), 9, lumpy=0.06, crisp=0.008, sub=3, bury=0.3, plane_range=(0.35, 0.65)),
                       Matrix.Translation((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) @ tilt @ Matrix.Scale(rnd.uniform(0.6, 1.1), 4)))
    stone("ice_b", merge_bms(shards), stone_colour(ICE, 10, lichen=False, ice=True), 0.5, sharp=30)
    return out


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def export(assets, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in assets.values():
        o.location = (0, 0, 0)
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_texcoords=True, export_normals=True, export_tangents=False, export_materials="EXPORT",
                              export_vertex_color="ACTIVE", export_image_format="AUTO", export_animations=False,
                              export_skins=False, export_morph=False, export_cameras=False, export_lights=False)
    print(f"exported {path} ({os.path.getsize(path) / 1024:.0f} KB)")


# ---------------------------------------------------------------- previews (Cycles)


def preview_materials(atlas):
    def node_mat(name):
        m = bpy.data.materials.new(name)
        return m, m.node_tree, m.node_tree.nodes["Principled BSDF"]

    mats = {}
    m, nt, bsdf = node_mat("FoliagePreview")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = atlas
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Color"
    info = nt.nodes.new("ShaderNodeObjectInfo")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    nt.links.new(tex.outputs["Color"], mix.inputs[6])
    nt.links.new(vc.outputs["Color"], mix.inputs[7])
    mix2 = nt.nodes.new("ShaderNodeMix")
    mix2.data_type = "RGBA"
    mix2.blend_type = "MULTIPLY"
    mix2.inputs["Factor"].default_value = 1.0
    nt.links.new(mix.outputs[2], mix2.inputs[6])
    nt.links.new(info.outputs["Color"], mix2.inputs[7])
    nt.links.new(mix2.outputs[2], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.8
    trans = nt.nodes.new("ShaderNodeBsdfTranslucent")
    nt.links.new(mix2.outputs[2], trans.inputs["Color"])
    transp = nt.nodes.new("ShaderNodeBsdfTransparent")
    mixs = nt.nodes.new("ShaderNodeMixShader")
    mixs.inputs["Fac"].default_value = 0.25
    nt.links.new(bsdf.outputs[0], mixs.inputs[1])
    nt.links.new(trans.outputs[0], mixs.inputs[2])
    cut = nt.nodes.new("ShaderNodeMath")
    cut.operation = "GREATER_THAN"
    cut.inputs[1].default_value = 0.5
    nt.links.new(tex.outputs["Alpha"], cut.inputs[0])
    mixa = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(cut.outputs[0], mixa.inputs["Fac"])
    nt.links.new(transp.outputs[0], mixa.inputs[1])
    nt.links.new(mixs.outputs[0], mixa.inputs[2])
    nt.links.new(mixa.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    mats["foliage"] = m

    for key, rough, snow in (("rock", 0.85, False), ("snow", 0.85, True), ("ice", 0.22, False)):
        m, nt, bsdf = node_mat(key.capitalize() + "Preview")
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Color"
        info = nt.nodes.new("ShaderNodeObjectInfo")
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(vc.outputs["Color"], mix.inputs[6])
        nt.links.new(info.outputs["Color"], mix.inputs[7])
        col = mix.outputs[2]
        # the fine mottling rock.gdshader adds from model-space noise
        coord = nt.nodes.new("ShaderNodeTexCoord")
        nz = nt.nodes.new("ShaderNodeTexNoise")
        nz.inputs["Scale"].default_value = 2.5
        nz.inputs["Detail"].default_value = 4
        nt.links.new(coord.outputs["Object"], nz.inputs["Vector"])
        rr = nt.nodes.new("ShaderNodeMapRange")
        rr.inputs["From Min"].default_value = 0.3
        rr.inputs["From Max"].default_value = 0.7
        rr.inputs["To Min"].default_value = 0.72
        rr.inputs["To Max"].default_value = 1.12
        nt.links.new(nz.outputs["Fac"], rr.inputs["Value"])
        mixd = nt.nodes.new("ShaderNodeMix")
        mixd.data_type = "RGBA"
        mixd.blend_type = "MULTIPLY"
        mixd.inputs["Factor"].default_value = 1.0
        nt.links.new(col, mixd.inputs[6])
        nt.links.new(rr.outputs[0], mixd.inputs[7])
        col = mixd.outputs[2]
        if snow:  # what rock.gdshader does with INSTANCE_CUSTOM.y: snow on up-facing faces
            geo = nt.nodes.new("ShaderNodeNewGeometry")
            sep = nt.nodes.new("ShaderNodeSeparateXYZ")
            nt.links.new(geo.outputs["Normal"], sep.inputs[0])
            rng = nt.nodes.new("ShaderNodeMapRange")
            rng.inputs["From Min"].default_value = 0.45
            rng.inputs["From Max"].default_value = 0.7
            nt.links.new(sep.outputs["Z"], rng.inputs["Value"])
            mixs = nt.nodes.new("ShaderNodeMix")
            mixs.data_type = "RGBA"
            nt.links.new(rng.outputs[0], mixs.inputs["Factor"])
            nt.links.new(col, mixs.inputs[6])
            mixs.inputs[7].default_value = (0.82, 0.86, 0.92, 1)
            col = mixs.outputs[2]
        nt.links.new(col, bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = rough
        if key == "ice":
            bsdf.inputs["Specular IOR Level"].default_value = 0.7
        mats[key] = m
    return mats


def ground_material(name, hex_colour, seed):
    c = tuple(int(hex_colour[i:i + 2], 16) / 255 for i in (1, 3, 5))
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexNoise")
    tex.inputs["Scale"].default_value = 0.06
    tex.inputs["Detail"].default_value = 6
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*srgb(mul(c, 0.8)), 1)
    ramp.color_ramp.elements[1].color = (*srgb(mul(c, 1.08)), 1)
    nt.links.new(tex.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.95
    return m


def place(src, mat, loc, rot_z=0.0, scale=1.0, tint=(1, 1, 1), tilt=None):
    o = bpy.data.objects.new(src.name + "_i", src.data)
    bpy.context.scene.collection.objects.link(o)
    o.material_slots[0].link = "OBJECT"
    o.material_slots[0].material = mat
    o.location = loc
    m = Matrix.Rotation(rot_z, 4, "Z")
    if tilt:
        m = Matrix.Rotation(tilt[0], 4, "X") @ Matrix.Rotation(tilt[1], 4, "Y") @ m
    o.rotation_euler = m.to_euler()
    o.scale = (scale, scale, scale) if not isinstance(scale, tuple) else scale
    o.color = (*tint, 1)
    return o


def zone(assets, mats, rnd, x0, y0, x1, y1, rules):
    """rules: list of (asset names, count, scale range, tint list (sRGB), material key, kind) - like Scatter.cs."""
    for names, count, srange, tints, mkey, kind in rules:
        for _ in range(count):
            name = rnd.choice(names)
            x, y = rnd.uniform(x0, x1), rnd.uniform(y0, y1)
            s = rnd.uniform(*srange)
            tint = srgb(rnd.choice(tints)) if tints else (1, 1, 1)
            k = rnd.uniform(0.9, 1.1)
            tint = tuple(min(1.0, t * k) for t in tint)
            tilt = (rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25)) if kind == "rock" else None
            sink = -0.08 * s if kind == "rock" else 0.0
            place(assets[name], mats[mkey], (x, y, sink), rnd.random() * TAU, s if kind != "tree" else (s, s, s * rnd.uniform(0.9, 1.15)), tint, tilt)


def ground(x0, y0, x1, y1, mat):
    me = bpy.data.meshes.new("ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("ground", me)
    bpy.context.scene.collection.objects.link(o)
    o.location = ((x0 + x1) / 2, (y0 + y1) / 2, 0)
    o.scale = (x1 - x0, y1 - y0, 1)
    me.materials.append(mat)


TINT_GREEN = [(1.0, 1.0, 1.0), (0.92, 1.0, 0.9), (1.05, 1.0, 0.88), (0.88, 0.95, 0.95)]
TINT_GRASS = [(1.0, 1.0, 1.0), (1.05, 1.0, 0.8), (0.9, 1.0, 0.95), (1.15, 1.02, 0.7)]
TINT_DRY = [(1.25, 1.05, 0.62), (1.35, 1.12, 0.7), (1.15, 1.0, 0.65)]
TINT_STONE = [(1.0, 1.0, 1.0), (1.05, 0.98, 0.9), (0.92, 0.94, 0.98)]
TINT_DESERT_STONE = [(1.2, 1.0, 0.82), (1.15, 0.95, 0.8)]


def preview(assets, atlas, folder):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.transparent_max_bounces = 32
    scene.cycles.max_bounces = 4
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 0.6)
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = math.radians(40)
    sky.sun_rotation = math.radians(140)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.15
    scene.view_settings.exposure = -0.9
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.6
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = (math.radians(48), 0, math.radians(140))
    scene.collection.objects.link(sun)
    mats = preview_materials(atlas)
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle = math.radians(45)  # the game camera's fov
    cam.data.clip_end = 3000
    os.makedirs(folder, exist_ok=True)

    def shoot(name, target, elevation_deg, distance, yaw_deg=0.0):
        e, yw = math.radians(elevation_deg), math.radians(yaw_deg)
        d = Vector((math.sin(yw) * math.cos(e), -math.cos(yw) * math.cos(e), math.sin(e)))
        cam.location = Vector(target) + d * distance
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"scatter-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath, flush=True)

    def clear():
        for o in list(scene.collection.objects):
            if o.name.endswith("_i") or o.name.startswith("ground") or "_i." in o.name:
                bpy.data.objects.remove(o)

    for o in assets.values():
        o.hide_render = True

    def mkey(n):
        return "ice" if n.startswith("ice") else "rock" if n.startswith(("boulder", "rock", "scree", "pebble", "sand")) else "foliage"

    if "lineup" in SHOTS:
        ground(-60, -60, 60, 60, ground_material("g_plain", "#7d8f5a", 1))
        # back row: trees; front row: small plants (near meshes only)
        trees = [n for n in assets if mkey(n) == "foliage" and not n.endswith("_far") and assets[n].dimensions.z > 3]
        small = [n for n in assets if mkey(n) == "foliage" and not n.endswith("_far") and assets[n].dimensions.z <= 3]
        x = -20.0
        for n in trees:
            w = assets[n].dimensions.x * 0.8
            place(assets[n], mats["foliage"], (x + w / 2, 6, 0))
            x += w + 0.3
        x = -8.0
        for n in small:
            w = assets[n].dimensions.x
            place(assets[n], mats["foliage"], (x + w / 2, -3, 0))
            x += w + 0.5
        shoot("lineup", (0, 2, 3.0), 16, 38)
        clear()
    if "rocks" in SHOTS:
        ground(-60, -60, 60, 60, ground_material("g_hill", "#8a7a5a", 2))
        names = [n for n in assets if mkey(n) != "foliage" and not n.endswith("_far")]
        x, y = -9.0, 0.0
        for n in names:
            w = assets[n].dimensions.x
            if x + w > 10:
                x, y = -9.0, y + 4.5
            mk = mkey(n)
            if n in ("boulder_c",):
                place(assets[n], mats["snow"], (x + w / 2, y, 0))
            else:
                place(assets[n], mats[mk], (x + w / 2, y, 0))
            x += w + 0.6
        shoot("rocks", (0, 3, 0.6), 30, 15)
        clear()

    rnd = random.Random(7)
    conifers = ["conifer_a", "conifer_a", "conifer_b", "conifer_c"]
    if any(s in SHOTS for s in ("temperate", "close", "overview")):
        ground(-150, -110, -50, 110, ground_material("g_forest", "#3f6e35", 3))
        ground(-50, -110, 50, 110, ground_material("g_plains", "#8fae5a", 4))
        ground(50, -110, 150, 110, ground_material("g_hills", "#8a7a5a", 5))
        A = 541.0  # one hex, in square units: counts below are per hex as in Scatter.cs, times this zone's hexes
        hexes = 100 * 220 / A
        zone(assets, mats, rnd, -150, -110, -50, 110, [
            (conifers, int(18 * hexes), (0.75, 1.3), TINT_GREEN, "foliage", "tree"),
            (["broadleaf_b"], int(0.8 * hexes), (0.8, 1.1), TINT_GREEN, "foliage", "tree"),
            (["fern"], int(14 * hexes), (0.7, 1.3), TINT_GREEN, "foliage", "plant"),
            (["bush_a", "bush_b"], int(4 * hexes), (0.7, 1.2), TINT_GREEN, "foliage", "plant"),
            (["grass_a", "grass_c"], int(14 * hexes), (0.7, 1.2), TINT_GRASS, "foliage", "plant"),
            (["boulder_a", "boulder_b"], int(0.6 * hexes), (0.5, 1.2), TINT_STONE, "rock", "rock"),
        ])
        zone(assets, mats, rnd, -50, -110, 50, 110, [
            (["broadleaf_a", "broadleaf_a", "broadleaf_b"], int(1.6 * hexes), (0.75, 1.25), TINT_GREEN, "foliage", "tree"),
            (["bush_a", "bush_a", "bush_b"], int(4 * hexes), (0.6, 1.2), TINT_GREEN, "foliage", "plant"),
            (["grass_a", "grass_b", "grass_c", "grass_a"], int(90 * hexes), (0.7, 1.3), TINT_GRASS, "foliage", "plant"),
            (["pebbles"], int(0.6 * hexes), (0.8, 1.3), TINT_STONE, "rock", "rock"),
            (["boulder_c"], int(0.3 * hexes), (0.4, 0.8), TINT_STONE, "rock", "rock"),
        ])
        zone(assets, mats, rnd, 50, -110, 150, 110, [
            (["pine_a"], int(0.9 * hexes), (0.8, 1.15), TINT_GREEN, "foliage", "tree"),
            (["conifer_c"], int(0.5 * hexes), (0.8, 1.2), TINT_GREEN, "foliage", "tree"),
            (["bush_b", "bush_a"], int(2 * hexes), (0.6, 1.0), TINT_GREEN, "foliage", "plant"),
            (["grass_a", "grass_c"], int(30 * hexes), (0.7, 1.2), TINT_DRY + TINT_GRASS, "foliage", "plant"),
            (["boulder_a", "boulder_b", "boulder_c"], int(4 * hexes), (0.6, 2.0), TINT_STONE, "rock", "rock"),
            (["rock_cluster"], int(0.8 * hexes), (0.7, 1.3), TINT_STONE, "rock", "rock"),
            (["scree"], int(2 * hexes), (0.8, 1.4), TINT_STONE, "rock", "rock"),
        ])
        if "temperate" in SHOTS:
            shoot("temperate", (-10, 0, 0), 37, 95)
        if "close" in SHOTS:
            shoot("close", (-46, -40, 1.5), 20, 22, yaw_deg=-20)
        if "overview" in SHOTS:
            shoot("overview", (0, 10, 0), 52, 300)
        clear()
    if "arid" in SHOTS:
        ground(-150, -110, -50, 110, ground_material("g_desert", "#d2b77a", 6))
        ground(-50, -110, 50, 110, ground_material("g_mount", "#6e6a66", 7))
        ground(50, -110, 150, 110, ground_material("g_ice", "#e8f0f5", 8))
        A = 541.0
        hexes = 100 * 220 / A
        zone(assets, mats, rnd, -150, -110, -50, 110, [
            (["sandstone_a", "sandstone_b"], int(2 * hexes), (0.6, 1.6), TINT_DESERT_STONE, "rock", "rock"),
            (["shrub_desert"], int(6 * hexes), (0.6, 1.3), TINT_GREEN + TINT_DRY, "foliage", "plant"),
            (["snag"], int(0.3 * hexes), (0.7, 1.1), None, "foliage", "tree"),
            (["grass_c"], int(8 * hexes), (0.6, 1.1), TINT_DRY, "foliage", "plant"),
            (["pebbles"], int(2 * hexes), (0.8, 1.4), TINT_DESERT_STONE, "rock", "rock"),
        ])
        zone(assets, mats, rnd, -50, -110, 50, 110, [
            (["boulder_a", "boulder_b", "boulder_c"], int(7 * hexes), (0.8, 3.0), TINT_STONE, "rock", "rock"),
            (["rock_cluster"], int(1.5 * hexes), (0.8, 1.6), TINT_STONE, "rock", "rock"),
            (["scree"], int(4 * hexes), (1.0, 1.8), TINT_STONE, "rock", "rock"),
        ])
        zone(assets, mats, rnd, 50, -110, 150, 110, [
            (["ice_a", "ice_b"], int(4 * hexes), (0.6, 1.8), None, "ice", "rock"),
            (["boulder_a", "boulder_c"], int(1.5 * hexes), (0.6, 1.5), TINT_STONE, "snow", "rock"),
        ])
        shoot("arid", (0, 0, 0), 37, 95)
        clear()


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rnd = random.Random(1)
    atlas = paint_atlas(rnd)
    fol, rock = export_materials(atlas)
    assets = build_assets(fol, rock)
    total = 0
    for name, o in assets.items():
        t = tri_count(o)
        total += t
        print(f"  {name:18s} {t:5d} tris  {o.dimensions.x:5.2f} x {o.dimensions.y:5.2f} x {o.dimensions.z:5.2f}")
    print(f"  total {total} tris")
    if not arg("--no-export"):
        export(assets, OUT)
    if PREVIEW:
        atlas.filepath_raw = os.path.join(PREVIEW, "scatter-atlas.png")
        os.makedirs(PREVIEW, exist_ok=True)
        atlas.save()
        preview(assets, atlas, PREVIEW)


main()
