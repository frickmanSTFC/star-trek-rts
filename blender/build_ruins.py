"""
Ruin props for an undug ruin hex on the colony's own world (civilizations phase 2, docs/civilizations-phase2-contract.md
"Models"): what's left of a dead civilization, and what stays around an Excavation Site once one is built there.

Builds ONE glb, game/models/natives/ruins.glb (scripts/RuinModels.cs reads it once and copies the meshes per ruin):

  ruin_a       broken walls + toppled pillars: a three-lobed enclosure wall in broken runs with tower stumps at the
               lobes, a ring colonnade (fluted lobed pillars, three standing at broken heights, three fallen in drums),
               a stepped plinth with a cracked basin, rubble
  ruin_b       half-buried dome + arch: a ribbed great dome sunk in its own mound with a section fallen in, a pointed
               gateway arch standing in front, the tops of buried columns poking out of the mound, a fallen spire
  ruin_c       obelisk ring: seven three-lobed twisted obelisks round a stepped dais (one fallen, one snapped, some
               leaning), a metal ring and a glowing core on the dais, a fallen lintel, a ring of paving
  ruin_<x>_rim the pieces of each ruin beyond r ~7.8 (outlying wall stubs, fallen stones): kept round an Excavation
               Site whose footprint (r ~7.2) hides the core
  ruin_far     cheap stand-in (<= 300 tris) for far views

Style: the native towns kit's (blender/build_native_towns.py, whose mesh builder MB, T, trefoil and shade are reused):
lobed, fluted, twisted, scalloped shapes, no human architecture; weathered stone (vertex colours carry the weathering,
moss and dirt and a fake AO by height). Each ruin is centred on its origin on the ground and fits a hex (r <= 9.8, a hex
is ~20 across), with a low sinking pad; transforms applied, every node at the origin in the file.
Materials (Godot makes its own copies; COLOR_0 multiplies the base colour):
  "RuinStone"  the stone (Godot may tint it a little toward the dead species' colour)
  "RuinMetal"  dark bronze-like alien metal bands, rings and caps
  "Glow"       inlays and the dais core: dark in the file; Godot lights it faintly for precursor sites

Run (PyPI bpy in the 3.11 venv; ~1 s for the glb):
  /home/user/.bpyvenv/bin/python blender/build_ruins.py                      # export the glb
  /home/user/.bpyvenv/bin/python blender/build_ruins.py --no-export --preview blender/previews/natives
Flags: --out PATH, --no-export, --preview DIR, --res W (640), --samples N (20)
"""

import math
import os
import random
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def arg(name, default=None):
    if name in sys.argv:
        i = sys.argv.index(name)
        return sys.argv[i + 1] if i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith("--") else True
    return default


OUT = arg("--out", os.path.join(ROOT, "game", "models", "natives", "ruins.glb"))
PREVIEW = arg("--preview")
RES = int(arg("--res", "640"))
SAMPLES = int(arg("--samples", "20"))


def load_towns():
    """The towns kit without its main(): MB (mesh builder with vertex colours + fake AO), T, trefoil, shade, export."""
    path = os.path.join(HERE, "build_native_towns.py")
    with open(path) as f:
        src = re.sub(r"^main\(\)\s*$", "", f.read(), flags=re.M)
    saved = sys.argv
    sys.argv = [path]
    ns = {"__name__": "build_native_towns", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


TK = load_towns()
bpy = TK["bpy"]
Matrix, Vector = TK["Matrix"], TK["Vector"]
MB, T, trefoil = TK["MB"], TK["T"], TK["trefoil"]

TAU = math.tau
STONE, METAL, GLOW = 0, 1, 2          # MB gives indices 0 and 1 its fake AO by height
MAT_NAMES = ["RuinStone", "RuinMetal", "Glow"]
RIM_R = 7.8                           # pieces beyond this go to ruin_<x>_rim

WHITE = (1.0, 1.0, 1.0)
DIRT = (0.64, 0.52, 0.4)
DIRT_DARK = (0.5, 0.41, 0.32)
MOSS = (0.7, 0.8, 0.6)


def stone(rng, base=1.0):
    """A weathered stone shade: grey with a little warm/cool jitter, sometimes mossy or stained."""
    v = base - rng.random() * 0.16
    if rng.random() < 0.18:
        return (v * MOSS[0], v * MOSS[1], v * MOSS[2])
    w = (rng.random() - 0.5) * 0.06
    return (min(1.0, v + w), min(1.0, v), min(1.0, v - w * 0.6))


class Split:
    """Two mesh builders: the core (inside RIM_R) and the rim; `at(x, y)` picks one by where a piece stands."""

    def __init__(self, far=False):
        self.core = MB(ao=not far)
        self.rim = MB(ao=not far)

    def at(self, x, y):
        return self.rim if math.hypot(x, y) > RIM_R else self.core


# ---------------------------------------------------------------- shared pieces


def pad(mb, rng, r=8.2, segs=16, col=DIRT, lift=0.08):
    """A low irregular mound of rubble and dirt with a skirt that sinks into the terrain."""
    radii = [r * (0.88 + 0.12 * rng.random()) for _ in range(segs)]
    I = Matrix()
    top = [mb.vert(I, (radii[i] * math.cos(TAU * i / segs), radii[i] * math.sin(TAU * i / segs), lift + 0.06 * rng.random())) for i in range(segs)]
    mid = [mb.vert(I, (radii[i] * 0.55 * math.cos(TAU * i / segs + 0.2), radii[i] * 0.55 * math.sin(TAU * i / segs + 0.2), lift + 0.12)) for i in range(segs)]
    bot = [mb.vert(I, (radii[i] * 1.08 * math.cos(TAU * i / segs), radii[i] * 1.08 * math.sin(TAU * i / segs), -0.45)) for i in range(segs)]
    c = mb.vert(I, (0, 0, lift + 0.1))
    for i in range(segs):
        j = (i + 1) % segs
        mb.face((bot[i], bot[j], top[j], top[i]), STONE, DIRT_DARK)
        mb.face((top[i], top[j], mid[j], mid[i]), STONE, col)
        mb.face((mid[i], mid[j], c), STONE, col)


def block(mb, rng, x, y, sx, sy, sz, rz=0.0, tilt=3.0, z=0.0, col=None, mat=STONE, taper=0.92):
    """A tilted stone block (rubble, wall course)."""
    M = T(x, y, z, rz, rx=(rng.random() - 0.5) * 2 * tilt, ry=(rng.random() - 0.5) * 2 * tilt)
    mb.box(M, sx, sy, sz, mat, col or stone(rng), z0=-0.1, taper=taper)


def rubble(sp, rng, n, r0, r1, avoid=(), big=1.0):
    for _ in range(n):
        for _try in range(30):
            a, r = rng.random() * TAU, r0 + (r1 - r0) * math.sqrt(rng.random())
            x, y = r * math.cos(a), r * math.sin(a)
            if all(math.hypot(x - ax, y - ay) > ar for (ax, ay), ar in avoid):
                break
        s = (0.3 + 0.6 * rng.random()) * big
        block(sp.at(x, y), rng, x, y, s * (1 + rng.random()), s, s * (0.5 + 0.5 * rng.random()), rz=rng.random() * 180, tilt=14)


def broken_run(sp, rng, a, b, heights, w=0.6, col=None):
    """A wall from a to b as courses of blocks, each with its own remaining height (0 = fallen, a gap)."""
    n = len(heights)
    ang = math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))
    L = math.hypot(b[0] - a[0], b[1] - a[1]) / n
    for k, h in enumerate(heights):
        if h <= 0:
            continue
        t = (k + 0.5) / n
        x, y = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
        mb = sp.at(x, y)
        M = T(x, y, 0, ang, rx=(rng.random() - 0.5) * 6)
        c = col or stone(rng)
        mb.box(M, L * 0.97, w, h, STONE, c, z0=-0.15, taper=(1.0, 0.86))
        if h > 1.2 and rng.random() < 0.6:  # a scalloped coping stone on the taller runs
            mb.lathe(M @ T(z=h - 0.12, s=(L * 0.55, w * 0.62, 1.0)), [(1.0, 0.0), (1.08, 0.1), (0.8, 0.2)], 8, STONE, stone(rng, 0.95),
                     smooth=False, lobes=4, amp=0.16)


def pillar(mb, rng, M, h, r=0.42, metal=True):
    """A fluted, lobed alien pillar: flared lobed base, twisted fluted shaft, a broken top chunk if short."""
    mb.lathe(M, [(r * 1.7, 0.0), (r * 1.6, 0.22), (r * 1.15, 0.45), (r * 1.02, 0.55)], 8, STONE, stone(rng), smooth=False, lobes=5, amp=0.12)
    mb.lathe(M @ T(z=0.55), [(r * 1.02, 0.0), (r, h * 0.55), (r * 0.94, h)], 8, STONE, stone(rng), smooth=True, lobes=5, amp=0.17, twist=6)
    if metal:
        mb.band(M @ T(z=0.55), r * 1.08, h * 0.42, 0.14, 8, col=(0.9, 0.85, 0.8), mat=METAL, lobes=5, amp=0.17, twist=6)
    if h > 2.6:  # an intact lobed capital
        mb.lathe(M @ T(z=0.55 + h), [(r * 0.94, 0.0), (r * 1.5, 0.3), (r * 1.6, 0.45), (r * 1.1, 0.6)], 8, STONE, stone(rng, 0.95),
                 smooth=False, lobes=5, amp=0.14)
    else:  # a jagged broken top
        mb.box(M @ T(r * 0.25, 0, 0.55 + h, rng.random() * 90, rx=18, ry=-12), r * 1.2, r * 1.1, r * 0.8, STONE, stone(rng), taper=0.5)


def fallen_pillar(sp, rng, x, y, yaw, length, r=0.42, pieces=3):
    """A toppled pillar lying in drums along `yaw` from (x, y), the drums a little apart and askew."""
    d = length / pieces
    for k in range(pieces):
        cx = x + math.cos(math.radians(yaw)) * (d * (k + 0.5) + 0.12 * k)
        cy = y + math.sin(math.radians(yaw)) * (d * (k + 0.5) + 0.12 * k)
        mb = sp.at(cx, cy)
        M = T(cx, cy, r * 0.9, yaw + (rng.random() - 0.5) * 16, ry=90)
        mb.lathe(M @ T(z=-d * 0.48), [(r, 0.0), (r * 0.97, d * 0.96)], 8, STONE, stone(rng), smooth=True, lobes=5, amp=0.17)


def obelisk(mb, rng, M, h, r=0.75, twist=22.0, broken=False):
    """A three-lobed twisted obelisk on a lobed foot; a RuinMetal cap (or a snapped top)."""
    mb.lathe(M, [(r * 1.6, 0.0), (r * 1.5, 0.3), (r * 1.15, 0.5)], 9, STONE, stone(rng), smooth=False, lobes=3, amp=0.2)
    top = h * (0.55 if broken else 0.86)
    mb.lathe(M @ T(z=0.5), [(r * 1.15, 0.0), (r * 0.95, top * 0.5), (r * 0.62, top)], 9, STONE, stone(rng), smooth=True, lobes=3, amp=0.3,
             twist=twist, cap_top=True)
    if broken:
        mb.box(M @ T(0.1, 0.05, 0.5 + top, 25, rx=20), r * 0.9, r * 0.8, r * 0.6, STONE, stone(rng), taper=0.4)
    else:
        mb.lathe(M @ T(z=0.5 + top), [(r * 0.62, 0.0), (r * 0.5, (h - top) * 0.55), (0.0, h - top)], 9, METAL, (0.85, 0.8, 0.75), smooth=False,
                 lobes=3, amp=0.3, twist=twist)
        # a glowing slit up one lobe (dark unless the game lights it)
        for k in range(3):
            a = TAU * k / 3 + math.radians(twist * 0.3 * top)
            mb.box(M @ T(r * 0.98 * math.cos(a), r * 0.98 * math.sin(a), 0.5 + top * 0.25, math.degrees(a)), 0.06, 0.08, top * 0.4, GLOW, WHITE)


def arch(mb, rng, M, span, rise, w=0.9, d=1.1, blocks=9, skip=()):
    """A pointed arch in the M frame's XZ plane over x in [-span/2, span/2], springing at z 0: voussoir blocks."""
    half = span / 2
    pts = []
    for k in range(blocks + 1):
        t = k / blocks
        a = math.pi * (1 - t)
        z = rise * math.sin(a) ** 0.65
        pts.append((half * math.cos(a), z))
    for k, ((x0, z0), (x1, z1)) in enumerate(zip(pts, pts[1:])):
        if k in skip:
            continue
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        L = math.hypot(x1 - x0, z1 - z0)
        ang = math.degrees(math.atan2(z1 - z0, x1 - x0))
        mb.box(M @ T(cx, 0, cz, ry=-ang), L * 1.04, d, w, STONE, stone(rng), z0=-w / 2)


# ---------------------------------------------------------------- the ruins


def ruin_a(sp, rng):
    """Broken walls + toppled pillars."""
    pad(sp.core, rng, 8.0)
    wall = trefoil(5.9, 0.13, 21, phase=0.3)
    for i in range(len(wall)):
        a, b = wall[i], wall[(i + 1) % len(wall)]
        if i in (10, 11):
            continue  # the gate gap, toward the front
        hs = [0.0 if rng.random() < 0.22 else 0.35 + rng.random() * (2.6 if i % 4 else 1.4) for _ in range(2)]
        broken_run(sp, rng, a, b, hs)
    for k in range(3):  # tower stumps at the lobe tips
        ang = TAU * k / 3 + 0.3
        R = 5.9 * 1.13
        x, y = R * math.cos(ang), R * math.sin(ang)
        h = [3.4, 1.6, 2.4][k]
        sp.core.lathe(T(x, y), [(1.05, -0.2), (1.0, h * 0.6), (0.92, h)], 9, STONE, stone(rng), smooth=False, lobes=6, amp=0.08, twist=4)
        sp.core.box(T(x + 0.3, y, h - 0.05, 30, rx=14), 1.0, 0.9, 0.7, STONE, stone(rng), taper=0.5)
        if k == 0:
            sp.core.band(T(x, y), 1.0, h * 0.7, 0.18, 9, col=(0.85, 0.8, 0.75), mat=METAL, lobes=6, amp=0.08, twist=4)
    # the colonnade: three standing (broken heights), three fallen outward from their bases
    for k in range(6):
        ang = TAU * k / 6 + 0.5
        x, y = 3.0 * math.cos(ang), 3.0 * math.sin(ang)
        if k % 2 == 0:
            pillar(sp.core, rng, T(x, y, rz=rng.random() * 60), [3.6, 2.1, 1.3][k // 2])
        else:
            sp.core.lathe(T(x, y), [(0.72, 0.0), (0.68, 0.22), (0.5, 0.45)], 8, STONE, stone(rng), smooth=False, lobes=5, amp=0.12)
            fallen_pillar(sp, rng, x + 0.5 * math.cos(ang), y + 0.5 * math.sin(ang), math.degrees(ang) + (rng.random() - 0.5) * 40, 3.2)
    # stepped plinth with a cracked basin and a glowing ring inlay
    for k, (r, h) in enumerate([(1.9, 0.3), (1.45, 0.3), (1.0, 0.25)]):
        sp.core.cyl(T(z=0.3 * k), r, h, 6, STONE, stone(rng, 0.96), smooth=False, phase=0.5 + 0.12 * k)
    sp.core.band(T(z=0.3), 1.47, 0.08, 0.1, 6, col=WHITE, mat=GLOW, phase=0.62)
    sp.core.lathe(T(z=0.85), [(0.8, 0.0), (0.95, 0.35), (0.85, 0.42), (0.55, 0.2), (0.0, 0.12)], 8, STONE, stone(rng), smooth=False,
                  lobes=4, amp=0.1, cap_top=False)
    sp.core.box(T(0.65, 0.35, 0.95, 40, rx=30), 0.5, 0.4, 0.35, STONE, stone(rng), taper=0.6)
    rubble(sp, rng, 12, 1.5, 7.4, avoid=[((0, 0), 2.2)])
    # outliers beyond the enclosure (kept round an excavation)
    broken_run(sp, rng, (-8.4, 2.6), (-7.8, 5.0), [1.1, 0.0, 0.7])
    broken_run(sp, rng, (7.4, -4.4), (8.6, -2.2), [0.8, 1.4])
    fallen_pillar(sp, rng, 3.6, 7.6, 15, 2.6, pieces=2)
    rubble(sp, rng, 8, 8.0, 9.0)


def partial_dome(mb, rng, M, r, h, segs, a0, a1, z0=0.0, rings=5, lobes=10, amp=0.05, col=None):
    """A ribbed dome over the angle range a0..a1 (degrees) only: the rest has fallen in."""
    rows = []
    n = int(segs * (a1 - a0) / 360.0) + 1
    for k in range(rings + 1):
        t = k / rings
        phi = t * math.pi / 2
        rr, z = r * math.cos(phi), z0 + h * math.sin(phi)
        row = []
        for i in range(n):
            a = math.radians(a0 + (a1 - a0) * i / (n - 1))
            jag = 0.0
            if i in (0, n - 1) and 0 < k < rings:
                jag = (rng.random() - 0.3) * 0.25  # ragged break edges
            rl = rr * (1.0 + amp * math.cos(lobes * a)) if k < rings else 0.0
            row.append(mb.vert(M, ((rl + jag) * math.cos(a), (rl + jag) * math.sin(a), z)))
        rows.append(row)
    c = col or stone(rng)
    for k in range(rings):
        for i in range(n - 1):
            if k == rings - 1:
                mb.face((rows[k][i], rows[k][i + 1], rows[k + 1][i]), STONE, c, True)
            else:
                mb.face((rows[k][i], rows[k][i + 1], rows[k + 1][i + 1], rows[k + 1][i]), STONE, c, True)


def ruin_b(sp, rng):
    """Half-buried dome + arch."""
    pad(sp.core, rng, 8.2)
    # the dome's own mound (dirt over the lower half), the dome sunk in it with a fallen-in section toward the front
    dx, dy = 0.8, 1.6
    sp.core.lathe(T(dx, dy), [(6.2, 0.05), (5.4, 0.7), (4.4, 1.35), (3.8, 1.6)], 12, STONE, DIRT, smooth=True, cap_top=False)
    partial_dome(sp.core, rng, T(dx, dy, rz=-90), 4.0, 3.6, 18, 70, 340, z0=0.4, col=stone(rng, 0.95))
    # ribs over the standing part (RuinMetal bands up the dome)
    for k in range(5):
        a = math.radians(-20 + 50 * k + 20)
        for j in range(3):
            p0 = j / 3 * math.pi / 2
            p1 = (j + 1) / 3 * math.pi / 2
            x0, z0 = 4.06 * math.cos(p0), 0.4 + 3.66 * math.sin(p0)
            x1, z1 = 4.06 * math.cos(p1), 0.4 + 3.66 * math.sin(p1)
            ang = math.degrees(math.atan2(z1 - z0, x1 - x0))
            L = math.hypot(x1 - x0, z1 - z0)
            sp.core.box(T(dx, dy, 0, math.degrees(a)) @ T((x0 + x1) / 2, 0, (z0 + z1) / 2, ry=-ang), L, 0.22, 0.14, METAL, (0.85, 0.8, 0.75), z0=-0.07)
    # inside the break: the dark dirt floor and a fallen crown ring
    sp.core.disc(T(dx, dy), 3.6, 1.0, 12, STONE, DIRT_DARK)
    sp.core.lathe(T(dx - 1.2, dy - 1.0, 1.05, 20, rx=70), [(0.9, 0.0), (1.1, 0.0), (1.1, 0.3), (0.9, 0.3), (0.9, 0.0)], 10, METAL, (0.8, 0.75, 0.7),
                  smooth=False, cap_top=False)
    # the gateway arch in front (piers + pointed arch, keystone with a glyph inlay)
    gx, gy, ga = -1.4, -4.6, 12.0
    G = T(gx, gy, 0, ga)
    for s in (-1, 1):
        sp.core.lathe(G @ T(s * 1.7, 0), [(0.62, 0.0), (0.58, 0.3), (0.48, 0.45), (0.46, 3.2), (0.6, 3.4)], 8, STONE, stone(rng), smooth=False, lobes=4, amp=0.1)
    arch(sp.core, rng, G @ T(z=3.4), 3.4, 2.5, w=0.75, d=0.9)
    sp.core.lathe(G @ T(0, 0, 5.85), [(0.36, 0.0), (0.45, 0.3), (0.0, 0.85)], 6, METAL, (0.85, 0.8, 0.75), smooth=False, lobes=3, amp=0.25)
    sp.core.box(G @ T(0, -0.47, 5.2), 0.5, 0.04, 0.5, GLOW, WHITE)
    # tops of buried columns poking out of the mound
    for k, ang in enumerate((150, 200, 245, 30)):
        x, y = dx + 5.0 * math.cos(math.radians(ang)), dy + 5.0 * math.sin(math.radians(ang))
        h = 0.8 + rng.random() * 0.9
        sp.core.lathe(T(x, y, 0.6), [(0.5, 0.0), (0.47, h)], 8, STONE, stone(rng), smooth=True, lobes=5, amp=0.16)
        sp.core.box(T(x, y, 0.6 + h, rng.random() * 90, rx=15), 0.55, 0.5, 0.3, STONE, stone(rng), taper=0.5)
    # the fallen spire lying off the dome's flank
    sp.core.lathe(T(4.6, -2.6, 0.45, 145, ry=84), [(0.5, 0.0), (0.36, 2.0), (0.0, 3.4)], 8, METAL, (0.8, 0.76, 0.72), smooth=False, lobes=3, amp=0.25, twist=30)
    rubble(sp, rng, 9, 2.0, 7.4, avoid=[((dx, dy), 4.4), ((gx, gy), 2.4)])
    # outliers: voussoirs of a second arch, a drum, rubble (kept round an excavation)
    for k in range(3):
        block(sp.rim, rng, -7.6 - 0.7 * k, -3.6 + 1.1 * k, 1.1, 0.8, 0.7, rz=30 * k, tilt=12)
    fallen_pillar(sp, rng, 6.2, 5.6, 40, 2.2, pieces=2)
    rubble(sp, rng, 7, 8.0, 9.0)


def ruin_c(sp, rng):
    """Obelisk ring."""
    pad(sp.core, rng, 8.0)
    # a ring of flat paving under the obelisks
    for k in range(18):
        ang = TAU * k / 18
        if k in (4, 11):
            continue
        r = 5.5 + (rng.random() - 0.5) * 0.3
        x, y = r * math.cos(ang), r * math.sin(ang)
        sp.core.box(T(x, y, 0.0, math.degrees(ang) + 90, rx=(rng.random() - 0.5) * 4), 1.6, 0.9, 0.16, STONE, stone(rng, 0.9), z0=-0.05)
    # seven obelisks: one fallen, one snapped, some leaning
    for k in range(7):
        ang = TAU * k / 7 + 0.2
        x, y = 5.5 * math.cos(ang), 5.5 * math.sin(ang)
        h = 5.0 + rng.random() * 2.2
        if k == 2:  # fallen outward in two pieces
            sp.core.lathe(T(x, y), [(1.2, 0.0), (1.12, 0.3), (0.86, 0.5)], 9, STONE, stone(rng), smooth=False, lobes=3, amp=0.2)
            fall = ang + math.pi * 0.62  # toppled along the ring, half inward
            for j, (d0, d1) in enumerate(((0.6, 3.2), (3.5, 5.4))):
                cx, cy = x + math.cos(fall) * (d0 + d1) / 2, y + math.sin(fall) * (d0 + d1) / 2
                mb = sp.at(cx, cy)
                M = T(cx, cy, 0.6, math.degrees(fall) + (rng.random() - 0.5) * 20 * j, ry=90)
                mb.lathe(M @ T(z=-(d1 - d0) / 2), [(0.7 - 0.12 * j, 0.0), (0.58 - 0.14 * j, d1 - d0)], 9, STONE, stone(rng), smooth=True,
                         lobes=3, amp=0.3, twist=10)
            continue
        lean = (rng.random() - 0.5) * 2 * (10 if k in (0, 5) else 3)
        obelisk(sp.core, rng, T(x, y, 0, math.degrees(ang), rx=lean, ry=(rng.random() - 0.5) * 4), h, broken=k == 4)
    # a fallen lintel between two of them
    a = TAU * 5.5 / 7 + 0.2
    sp.core.box(T(4.6 * math.cos(a), 4.6 * math.sin(a), 0.0, math.degrees(a) + 90 + 14, rx=6), 3.2, 0.8, 0.7, STONE, stone(rng), z0=-0.05)
    # the dais: two lobed steps, a metal ring, the core (glows for a precursor site)
    sp.core.lathe(T(), [(2.7, 0.0), (2.6, 0.35), (2.0, 0.4), (1.95, 0.75)], 9, STONE, stone(rng, 0.96), smooth=False, lobes=3, amp=0.08)
    sp.core.lathe(T(z=0.75), [(1.35, 0.0), (1.5, 0.08), (1.5, 0.28), (1.2, 0.3), (1.2, 0.05)], 12, METAL, (0.85, 0.8, 0.75), smooth=False, cap_top=False)
    sp.core.lathe(T(z=0.75), [(0.75, 0.0), (0.7, 0.12), (0.0, 0.2)], 9, GLOW, WHITE, smooth=True, lobes=3, amp=0.15)
    for k in range(3):
        ang = TAU * k / 3
        sp.core.box(T(1.7 * math.cos(ang), 1.7 * math.sin(ang), 0.4, math.degrees(ang)), 0.6, 0.08, 0.04, GLOW, WHITE)
    rubble(sp, rng, 8, 2.8, 7.4, avoid=[((0, 0), 2.9)], big=0.8)
    # outliers: two short leaning obelisks and rubble beyond the ring (kept round an excavation)
    for k, ang in enumerate((110, 290)):
        x, y = 8.3 * math.cos(math.radians(ang)), 8.3 * math.sin(math.radians(ang))
        obelisk(sp.rim, rng, T(x, y, 0, ang + 90, rx=-8), 3.2, r=0.55, broken=k == 1)
    rubble(sp, rng, 6, 8.0, 9.0, big=0.8)


def ruin_far(sp, rng):
    """Cheap stand-in: a mound, a few wall blocks, two obelisks."""
    mb = sp.core
    I = Matrix()
    segs = 9
    t = [mb.vert(I, (7.6 * math.cos(TAU * i / segs), 7.6 * math.sin(TAU * i / segs), 0.1)) for i in range(segs)]
    b = [mb.vert(I, (8.2 * math.cos(TAU * i / segs), 8.2 * math.sin(TAU * i / segs), -0.45)) for i in range(segs)]
    mb.face(t, STONE, DIRT)
    for i in range(segs):
        j = (i + 1) % segs
        mb.face((b[i], b[j], t[j], t[i]), STONE, DIRT_DARK)
    for k, (x, y, h, rz) in enumerate([(-4.0, 2.0, 1.8, 20), (-2.0, 4.4, 1.1, 50), (3.6, 3.4, 1.5, -30), (4.4, -2.6, 0.9, 80)]):
        mb.box(T(x, y, 0, rz), 2.6, 0.6, h, STONE, stone(rng), z0=-0.1, taper=(1.0, 0.85))
    for k, (x, y, h) in enumerate([(-1.0, -1.5, 5.0), (1.8, 0.6, 3.6)]):
        mb.lathe(T(x, y), [(0.8, 0.0), (0.5, h * 0.85), (0.0, h)], 5, STONE, stone(rng), smooth=False)
    mb.cyl(T(), 2.2, 0.5, 6, STONE, stone(rng), smooth=False)


BUILDERS = {"a": ruin_a, "b": ruin_b, "c": ruin_c, "far": ruin_far}


# ---------------------------------------------------------------- materials, objects, export


def make_materials():
    def pbsdf(name, colour, metallic=0.0, roughness=0.8, emission=None, strength=0.0):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*colour, 1.0)
        b.inputs["Metallic"].default_value = metallic
        b.inputs["Roughness"].default_value = roughness
        if emission:
            b.inputs["Emission Color"].default_value = (*emission, 1.0)
            b.inputs["Emission Strength"].default_value = strength
        return m

    return [
        pbsdf("RuinStone", (0.47, 0.45, 0.41), roughness=0.9),
        pbsdf("RuinMetal", (0.34, 0.29, 0.22), metallic=0.7, roughness=0.45),
        pbsdf("Glow", (0.1, 0.22, 0.2), roughness=0.5, emission=(0.35, 0.95, 0.85), strength=0.0),
    ]


def mesh_object(name, mb, mats):
    me = bpy.data.meshes.new(name)
    me.from_pydata(mb.v, [], mb.f)
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", mb.fm)
    me.polygons.foreach_set("use_smooth", mb.fs)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    flat = []
    for cs in mb.fc:
        for c in cs:
            flat.extend(c)
    attr.data.foreach_set("color", flat)
    me.color_attributes.active_color = attr
    me.validate()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build(key, mats):
    sp = Split(far=key == "far")
    rng = random.Random(f"ruins-{key}")
    BUILDERS[key](sp, rng)
    objs = [mesh_object(f"ruin_{key}", sp.core, mats)]
    if sp.rim.f:
        objs.append(mesh_object(f"ruin_{key}_rim", sp.rim, mats))
    for o in objs:
        r = max(math.hypot(v.co.x, v.co.y) for v in o.data.vertices)
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        print(f"{o.name}: {tris} tris, reach {r:.2f}")
    return objs


# ---------------------------------------------------------------- previews (Cycles)


def preview(objs, folder):
    os.makedirs(folder, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 4
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    scene.world = world
    bg = world.node_tree.nodes["Background"]
    gme = bpy.data.meshes.new("Plain")
    gme.from_pydata([(-400, -400, -0.02), (400, -400, -0.02), (400, 400, -0.02), (-400, 400, -0.02)], [], [(0, 1, 2, 3)])
    plain = bpy.data.objects.new("Plain", gme)
    gm = bpy.data.materials.new("PlainMat")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.2, 0.18, 0.12, 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1.0
    gme.materials.append(gm)
    scene.collection.objects.link(plain)
    # a hex outline for scale (HexWidth 25 across the flats? the game's hexes are ~20-25 across)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    sun.data.angle = math.radians(3)
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.clip_end = 3000
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle_y = math.radians(34)
    scene.collection.objects.link(cam)
    scene.camera = cam
    # vertex colour x base colour in the preview materials
    for m in bpy.data.materials:
        if m.name not in MAT_NAMES:
            continue
        nt = m.node_tree
        b = nt.nodes["Principled BSDF"]
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Col"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs[6].default_value = b.inputs["Base Color"].default_value
        nt.links.new(attr.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    glow = bpy.data.materials["Glow"].node_tree.nodes["Principled BSDF"]

    def lighting(dusk):
        sun.data.energy = 0.6 if dusk else 3.6
        sun.data.color = (1.0, 0.6, 0.4) if dusk else (1.0, 0.96, 0.9)
        bg.inputs["Color"].default_value = (0.05, 0.06, 0.1, 1) if dusk else (0.42, 0.55, 0.8, 1)
        bg.inputs["Strength"].default_value = 0.8 if dusk else 0.65
        glow.inputs["Emission Strength"].default_value = 4.0 if dusk else 0.0

    def shoot(path, target, dist, yaw=-38.0, elev=34.0, w=RES, h=None):
        scene.render.resolution_x = w
        scene.render.resolution_y = h or int(w * 3 / 4)
        d = Vector((math.sin(math.radians(yaw)) * math.cos(math.radians(elev)),
                    -math.cos(math.radians(yaw)) * math.cos(math.radians(elev)), math.sin(math.radians(elev))))
        cam.location = Vector(target) + d * dist
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path, flush=True)

    groups = {}
    for o in objs:
        key = o.name[5:].replace("_rim", "")
        groups.setdefault(key, []).append(o)
    lighting(False)
    for key, members in groups.items():
        for o in objs:
            o.hide_render = o not in members
        shoot(os.path.join(folder, f"ruins-{key}.png"), (0, 0, 1.2), 34.0)
    # precursor glow at dusk (ruin_c's dais and slits)
    lighting(True)
    for o in objs:
        o.hide_render = o not in groups["c"]
    shoot(os.path.join(folder, "ruins-c-glow.png"), (0, 0, 1.5), 30.0, yaw=150, elev=28)
    # line-up of the three ruins and the far stand-in, day
    lighting(False)
    for o in objs:
        o.hide_render = False
        key = o.name[5:].replace("_rim", "")
        o.location = ({"a": -1.5, "b": -0.5, "c": 0.5, "far": 1.5}[key] * 21.0, 0, 0)
    shoot(os.path.join(folder, "ruins-lineup.png"), (0, 0, 1.0), 88.0, yaw=-12, elev=36, w=RES * 2, h=int(RES * 0.7))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = make_materials()
    objs = []
    for key in ("a", "b", "c", "far"):
        objs += build(key, mats)
    if "--no-export" not in sys.argv:
        TK["export"](objs, OUT)
    if PREVIEW:
        preview(objs, PREVIEW)


main()
