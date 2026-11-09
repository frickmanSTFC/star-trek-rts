"""
Native towns by development level (civilizations phase 1, docs/civilizations-contract.md "Models").

Builds ONE glb, game/models/natives/towns.glb, with a node per town type (scripts/NativeTowns.cs loads it once and
copies the meshes per town). Alien-flavoured generic settlements, original designs (no canon species architecture):
recognisable eras with non-human shapes — beehive/teardrop huts, lobed walls with onion towers, fluted trumpet
smokestacks and barrel-vault halls, saucer-ringed and twisted atomic-age towers, a spaceport with a tripod rocket,
spiral arcologies with landing pads on stalks.

  town_l1_a / town_l1_b   L1 Stone/Bronze: beehive huts round a fire, stilt pods, fields, standing stones
  town_l2_a / town_l2_b   L2 Iron/Medieval: trefoil walled town + ribbed spire / walled terraced mound citadel
  town_l3_a / town_l3_b   L3 Industrial: mill town with trumpet stacks, rail, vault warehouses, gas holders /
                          kiln town with a sawtooth works, water tower, curving rail; empties Smoke0.. at stack tops
  town_l4_a / town_l4_b   L4 Atomic: saucer-ringed tower, glass slabs, dish, masts, cooling tower /
                          twisted spire, dome stadium, crescent blocks; empties Smoke0.. on the stacks
  town_l5_a               L5 Early warp: city + spaceport (launch pad, gantry, tripod rocket, control dome)
  town_l6_a               L6 Spacefaring: three spiral arcologies, sky bridges, landing pads on stalks, park domes
  town_l1_far .. town_l6_far   cheap stand-ins (<= 300 tris) for the globe / far views

Conventions: Blender +Z up (Godot +Y), 1 unit = 1 world unit, each town centred on its origin on the ground and
inside a radius of ~9.6 (a hex is ~20 across), transforms applied, every node at the origin in the file.
Materials (recoloured in Godot; vertex colour COLOR_0 multiplies the base colour as a shade/AO term):
  "Species"      walls / bodies (Godot sets the species colour)
  "SpeciesTrim"  roofs, domes, trim (Godot: a colour derived from the species colour)
  "CityLight"    emissive windows, street lights, fires (Godot drives the strength by night)
  "Glass"        dark reflective glass
  "Ground"       the town pad, fields, roads (vertex colour carries the field/road tints)

Run (PyPI bpy in the 3.11 venv; ~1 s for the glb):
  /home/user/.bpyvenv/bin/python blender/build_native_towns.py                      # export the glb
  /home/user/.bpyvenv/bin/python blender/build_native_towns.py --no-export --preview blender/previews/natives
  python3 blender/build_native_towns.py --sheet blender/previews/natives          # contact sheet (system PIL)
Flags: --out PATH, --no-export, --preview DIR, --res W (480), --samples N (20), --only l3_a,l4_b
"""

import json
import math
import os
import random
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def arg(name, default=None):
    if name in sys.argv:
        i = sys.argv.index(name)
        return sys.argv[i + 1] if i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith("--") else True
    return default


TOWNS = ["l1_a", "l1_b", "l2_a", "l2_b", "l3_a", "l3_b", "l4_a", "l4_b", "l5_a", "l6_a"]
FARS = ["l1_far", "l2_far", "l3_far", "l4_far", "l5_far", "l6_far"]
LEVEL_NAMES = {1: "L1 Stone/Bronze", 2: "L2 Iron/Medieval", 3: "L3 Industrial", 4: "L4 Atomic", 5: "L5 Early warp",
               6: "L6 Spacefaring"}

# ---------------------------------------------------------------- contact sheet (system python + PIL, no bpy)

if arg("--sheet"):
    from PIL import Image, ImageDraw

    folder = arg("--sheet")

    def tile(name, w, h):
        p = os.path.join(folder, f"natives-{name}.png")
        if not os.path.exists(p):
            return None
        im = Image.open(p).convert("RGB")
        return im.resize((w, int(im.height * w / im.width)))

    w = 360
    cols = [("day", "a"), ("day", "b"), ("night", "a"), ("night", "b"), ("day", "far"), ("night", "far")]
    probe = tile("l1_a-day", w, 0)
    h = probe.height if probe else 270
    head = 22
    sheet = Image.new("RGB", (8 + 132 + len(cols) * (w + 6), 8 + head + 6 * (h + 6)), (14, 16, 22))
    d = ImageDraw.Draw(sheet)
    titles = ["variant a · day", "variant b · day", "variant a · night", "variant b · night", "far · day",
              "far · night"]
    for c, t in enumerate(titles):
        d.text((140 + c * (w + 6), 8), t, fill=(230, 200, 150))
    for lv in range(1, 7):
        y = 8 + head + (lv - 1) * (h + 6)
        d.text((10, y + h // 2 - 6), LEVEL_NAMES[lv], fill=(230, 200, 150))
        for c, (when, var) in enumerate(cols):
            name = f"l{lv}_far-{when}" if var == "far" else f"l{lv}_{var}-{when}"
            if var == "b" and lv >= 5:
                name = f"l{lv}_a-{when}-close"
            im = tile(name, w, h)
            x = 140 + c * (w + 6)
            if im:
                sheet.paste(im, (x, y))
            if var == "b" and lv >= 5:
                d.text((x + 6, y + 4), "a · close-up", fill=(200, 210, 230))
    out = os.path.join(folder, "natives-sheet.png")
    sheet.save(out)
    print("wrote", out)
    over = [tile(n, 900, 0) for n in ("lineup-day", "lineup-night")]
    if all(over):
        img = Image.new("RGB", (900 * 2 + 18, over[0].height + 12), (14, 16, 22))
        img.paste(over[0], (6, 6))
        img.paste(over[1], (912, 6))
        img.save(os.path.join(folder, "natives-lineup.png"))
        print("wrote", os.path.join(folder, "natives-lineup.png"))
    sys.exit(0)

import bpy  # noqa: E402  (bpy before bmesh / mathutils)
from mathutils import Matrix, Vector  # noqa: E402

OUT = arg("--out", os.path.join(ROOT, "game", "models", "natives", "towns.glb"))
PREVIEW = arg("--preview")
RES = int(arg("--res", "480"))
SAMPLES = int(arg("--samples", "20"))
ONLY = arg("--only")

TAU = math.tau
SPECIES, TRIM, LIGHT, GLASS, GROUND = range(5)
MAT_NAMES = ["Species", "SpeciesTrim", "CityLight", "Glass", "Ground"]

# Ground tints (vertex colour x the Ground material's tan)
DIRT = (0.86, 0.82, 0.78)
PLAZA = (1.0, 0.97, 0.9)
ROAD = (0.55, 0.55, 0.6)
FIELD_GREEN = (0.55, 0.85, 0.42)
FIELD_GOLD = (1.0, 0.88, 0.48)
FIELD_TEAL = (0.42, 0.8, 0.72)
FIELD_RUST = (0.95, 0.62, 0.42)
PARK = (0.42, 0.78, 0.38)
WHITE = (1.0, 1.0, 1.0)


def T(x=0.0, y=0.0, z=0.0, rz=0.0, s=1.0, rx=0.0, ry=0.0):
    """Translation @ RotZ @ RotY @ RotX @ Scale (s a number or (sx, sy, sz)); angles in degrees."""
    sc = s if isinstance(s, (tuple, list)) else (s, s, s)
    S = Matrix.Diagonal((sc[0], sc[1], sc[2], 1.0))
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, "Z")
            @ Matrix.Rotation(math.radians(ry), 4, "Y") @ Matrix.Rotation(math.radians(rx), 4, "X") @ S)


def shade(rng, base=1.0, spread=0.08):
    """A greyish multiplier with a little warm/cool jitter, so one species colour doesn't read flat."""
    v = base - rng.random() * spread
    w = (rng.random() - 0.5) * 0.05
    return (min(1.0, v + w), min(1.0, v), min(1.0, v - w))


class MB:
    """Mesh builder: faces with a material index, per-corner colours (x a fake AO from the height) and smooth flags."""

    def __init__(self, ao=True):
        self.v, self.f, self.fm, self.fs, self.fc = [], [], [], [], []
        self.ao = ao

    def vert(self, M, p):
        self.v.append(tuple(M @ Vector(p)))
        return len(self.v) - 1

    def face(self, idx, mat, col, smooth=False):
        self.f.append(tuple(idx))
        self.fm.append(mat)
        self.fs.append(smooth)
        cs = []
        for i in idx:
            if self.ao and mat in (SPECIES, TRIM):
                z = self.v[i][2]
                k = 0.7 + 0.3 * min(1.0, max(0.0, z / 1.6))
            else:
                k = 1.0
            cs.append((col[0] * k, col[1] * k, col[2] * k, 1.0))
        self.fc.append(cs)

    def tris(self):
        return sum(len(f) - 2 for f in self.f)

    # ------------------------------------------------------------ primitives

    def box(self, M, sx, sy, sz, mat, col, z0=0.0, taper=1.0, top_shift=(0.0, 0.0)):
        """Box from z0 to z0 + sz, centred in x/y; taper scales the top face (a number, or (x, y) for sloped wall
        tops that keep their length)."""
        hx, hy = sx / 2, sy / 2
        kx, ky = taper if isinstance(taper, tuple) else (taper, taper)
        tx, ty = hx * kx, hy * ky
        ox, oy = top_shift
        b = [self.vert(M, p) for p in ((-hx, -hy, z0), (hx, -hy, z0), (hx, hy, z0), (-hx, hy, z0))]
        t = [self.vert(M, p) for p in ((-tx + ox, -ty + oy, z0 + sz), (tx + ox, -ty + oy, z0 + sz),
                                       (tx + ox, ty + oy, z0 + sz), (-tx + ox, ty + oy, z0 + sz))]
        self.face((t[0], t[1], t[2], t[3]), mat, col)
        self.face((b[3], b[2], b[1], b[0]), mat, col)
        for i in range(4):
            j = (i + 1) % 4
            self.face((b[i], b[j], t[j], t[i]), mat, col)

    def quad(self, M, pts, mat, col, smooth=False):
        self.face([self.vert(M, p) for p in pts], mat, col, smooth)

    def lathe(self, M, prof, segs, mat, col, smooth=True, lobes=0, amp=0.0, twist=0.0, phase=0.0, cap_top=True,
              cap_bottom=False, flip=False, sx=1.0, sy=1.0):
        """Surface of revolution through (r, z) points (r = 0 makes an apex). lobes/amp make a fluted or lobed
        section, twist (degrees per unit of height) turns the lobes into a spiral; sx/sy squash it to an oval."""
        rings = []
        tw = math.radians(twist)
        if twist:  # enough rings that the spiral stays a surface (<= 12 degrees of turn per ring)
            fine = [prof[0]]
            for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
                n = max(1, int(math.ceil(abs(twist * (z1 - z0)) / 12.0)))
                fine += [(r0 + (r1 - r0) * k / n, z0 + (z1 - z0) * k / n) for k in range(1, n + 1)]
            prof = fine

        def pos(r, z, i):
            a = phase + TAU * i / segs
            rr = r * (1.0 + amp * math.cos(lobes * (a - tw * z))) if lobes else r
            return (rr * math.cos(a) * sx, rr * math.sin(a) * sy, z)

        for r, z in prof:
            if r <= 1e-6:
                rings.append([self.vert(M, (0, 0, z))])
            else:
                rings.append([self.vert(M, pos(r, z, i)) for i in range(segs)])
        for k in range(len(rings) - 1):
            A, B = rings[k], rings[k + 1]
            for i in range(segs):
                j = (i + 1) % segs
                if len(A) == 1 and len(B) == 1:
                    continue
                if len(B) == 1:
                    q = (A[i], A[j], B[0])
                elif len(A) == 1:
                    q = (A[0], B[j], B[i])
                else:
                    q = (A[i], A[j], B[j], B[i])
                self.face(q[::-1] if flip else q, mat, col, smooth)
        if cap_top and prof[-1][0] > 1e-6:
            r, z = prof[-1]
            idx = [self.vert(M, pos(r, z, i)) for i in range(segs)]
            self.face(idx[::-1] if flip else idx, mat, col)
        if cap_bottom and prof[0][0] > 1e-6:
            r, z = prof[0]
            idx = [self.vert(M, pos(r, z, i)) for i in range(segs)]
            self.face(idx if flip else idx[::-1], mat, col)

    def cyl(self, M, r, h, segs, mat, col, r2=None, z0=0.0, smooth=True, cap=True, **kw):
        self.lathe(M, [(r, z0), (r if r2 is None else r2, z0 + h)], segs, mat, col, smooth=smooth, cap_top=cap, **kw)

    def dome(self, M, r, h, segs, mat, col, z0=0.0, rings=3, smooth=True, **kw):
        prof = [(r * math.cos(a), z0 + h * math.sin(a)) for a in (k / rings * math.pi / 2 for k in range(rings))]
        self.lathe(M, prof + [(0.0, z0 + h)], segs, mat, col, smooth=smooth, **kw)

    def band(self, M, r, z, h, segs, col=WHITE, mat=LIGHT, **kw):
        """A thin open ring just outside radius r (window bands); pass the same lobes/amp/twist as the tower."""
        self.lathe(M, [(r + 0.04, z), (r + 0.04, z + h)], segs, mat, col, smooth=True, cap_top=False, **kw)

    def poly(self, M, pts, z, mat, col):
        """A flat polygon (fields, plazas, pads) at height z, CCW from above."""
        self.face([self.vert(M, (x, y, z)) for x, y in pts], mat, col)

    def disc(self, M, r, z, segs, mat, col, sx=1.0, sy=1.0, phase=0.0):
        self.poly(M, [(r * sx * math.cos(phase + TAU * i / segs), r * sy * math.sin(phase + TAU * i / segs))
                      for i in range(segs)], z, mat, col)

    def segment(self, M, a, b, w, h, mat, col, z0=0.0, taper=1.0):
        """A box from point a to point b (2-D), w wide, h tall (walls, rails, bridges)."""
        dx, dy = b[0] - a[0], b[1] - a[1]
        L = math.hypot(dx, dy)
        ang = math.degrees(math.atan2(dy, dx))
        self.box(M @ T((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, 0, ang), L, w, h, mat, col, z0=z0, taper=(1.0, taper))

    def vault(self, M, length, width, height, segs, mat, col, wall=0.0, wall_mat=SPECIES, wall_col=WHITE):
        """Barrel-vault hall along X: optional straight walls (wall tall) under a half-cylinder roof."""
        if wall > 0:
            self.box(M, length, width, wall, wall_mat, wall_col)
        hl = length / 2
        arc = [(width / 2 * math.cos(math.pi * k / segs), wall + height * math.sin(math.pi * k / segs))
               for k in range(segs + 1)]
        L = [self.vert(M, (-hl, y, z)) for y, z in arc]
        R = [self.vert(M, (hl, y, z)) for y, z in arc]
        for k in range(segs):
            self.face((L[k], R[k], R[k + 1], L[k + 1])[::-1], mat, col, True)
        self.face([self.vert(M, (-hl, y, z)) for y, z in arc], mat, col)
        self.face([self.vert(M, (hl, y, z)) for y, z in arc][::-1], mat, col)


# ---------------------------------------------------------------- shared pieces


def pad(mb, rng, r=9.5, segs=18, col=DIRT):
    """The town's ground: an irregular low pad with a skirt that sinks into the terrain."""
    radii = [r * (0.92 + 0.08 * rng.random()) for _ in range(segs)]
    top = [(radii[i] * math.cos(TAU * i / segs), radii[i] * math.sin(TAU * i / segs)) for i in range(segs)]
    I = Matrix()
    t = [mb.vert(I, (x, y, 0.05)) for x, y in top]
    b = [mb.vert(I, (x * 1.07, y * 1.07, -0.45)) for x, y in top]
    mb.face(t, GROUND, col)
    for i in range(segs):
        j = (i + 1) % segs
        mb.face((b[i], b[j], t[j], t[i]), GROUND, col)


def hut(mb, M, rng, s=1.0, col=None, door=True, horn=False):
    """Beehive hut: a bulging, slightly lobed teardrop with a dark door and sometimes a curled horn on top."""
    col = col or shade(rng)
    prof = [(0.95, 0.0), (1.12, 0.45), (1.02, 1.05), (0.68, 1.6), (0.25, 2.05), (0.0, 2.25)]
    mb.lathe(M @ T(s=s), prof, 9, SPECIES, col, lobes=3, amp=0.06, phase=rng.random() * TAU)
    mb.lathe(M @ T(z=2.05 * s, s=s), [(0.3, 0.0), (0.18, 0.12), (0.0, 0.2)], 6, TRIM, shade(rng, 0.9))
    if door:
        mb.box(M @ T(1.0 * s, 0, 0, 0, s), 0.3, 0.55, 0.85, TRIM, (0.32, 0.28, 0.26), taper=0.6)
    if horn:
        for k in range(3):
            mb.box(M @ T(0.12 * k * s, 0, (2.2 + 0.28 * k) * s, 0, s, ry=25 * k), 0.14, 0.14, 0.34, TRIM, shade(rng, 0.85))


def fire(mb, M, rng, r=0.55):
    for i in range(6):
        a = TAU * i / 6
        mb.box(M @ T(r * math.cos(a), r * math.sin(a), 0, math.degrees(a)), 0.28, 0.22, 0.2, TRIM, (0.55, 0.52, 0.5))
    mb.lathe(M, [(0.32, 0.05), (0.18, 0.45), (0.0, 0.85)], 5, LIGHT, WHITE, smooth=False)


def field(mb, M, w, l, col, rows=0, rng=None):
    """A flat crop patch with furrow stripes (only top quads: cheap)."""
    mb.poly(M, [(-w / 2, -l / 2), (w / 2, -l / 2), (w / 2, l / 2), (-w / 2, l / 2)], 0.07, GROUND, col)
    for k in range(rows):
        y = -l / 2 + (k + 0.5) * l / rows
        dk = 0.85 + 0.1 * ((k * 7) % 3)
        mb.poly(M, [(-w / 2 + 0.1, y - 0.09), (w / 2 - 0.1, y - 0.09), (w / 2 - 0.1, y + 0.09), (-w / 2 + 0.1, y + 0.09)],
                0.1, GROUND, (col[0] * dk, col[1] * dk, col[2] * dk))


def crescent_field(mb, M, r0, r1, a0, a1, col, segs=6):
    pts = [(r1 * math.cos(a0 + (a1 - a0) * k / segs), r1 * math.sin(a0 + (a1 - a0) * k / segs)) for k in range(segs + 1)]
    pts += [(r0 * math.cos(a0 + (a1 - a0) * k / segs), r0 * math.sin(a0 + (a1 - a0) * k / segs)) for k in range(segs, -1, -1)]
    # split into quads so the concave strip triangulates cleanly
    n = segs + 1
    for k in range(segs):
        mb.poly(M, [pts[k], pts[k + 1], pts[2 * n - 2 - k], pts[2 * n - 1 - k]][::-1], 0.07, GROUND, col)


def onion(mb, M, r, h, col, segs=8, mat=TRIM):
    mb.lathe(M, [(r * 0.85, 0.0), (r * 1.18, h * 0.3), (r * 0.95, h * 0.58), (r * 0.35, h * 0.82), (r * 0.1, h * 0.95),
                 (0.0, h)], segs, mat, col)


def lamp_post(mb, M, h=1.6):
    mb.cyl(M, 0.06, h, 4, TRIM, (0.4, 0.4, 0.42), smooth=False)
    mb.box(M @ T(z=h), 0.22, 0.22, 0.18, LIGHT, WHITE)


def window_strips(mb, M, sx, sy, z0, z1, rows, col=WHITE, faces=(0, 1, 2, 3), h=0.22):
    """Lit window strips on the faces of a box (sx x sy, centred) between z0 and z1: thin quads 3 cm out."""
    for r in range(rows):
        z = z0 + (r + 0.5) * (z1 - z0) / rows
        for fidx in faces:
            if fidx in (0, 2):
                y = (-1 if fidx == 0 else 1) * (sy / 2 + 0.03)
                xa, xb = -sx / 2 + 0.25, sx / 2 - 0.25
                pts = [(xa, y, z - h / 2), (xb, y, z - h / 2), (xb, y, z + h / 2), (xa, y, z + h / 2)]
                mb.quad(M, pts if fidx == 0 else pts[::-1], LIGHT, col)
            else:
                x = (1 if fidx == 1 else -1) * (sx / 2 + 0.03)
                ya, yb = -sy / 2 + 0.25, sy / 2 - 0.25
                pts = [(x, ya, z - h / 2), (x, yb, z - h / 2), (x, yb, z + h / 2), (x, ya, z + h / 2)]
                mb.quad(M, pts if fidx == 1 else pts[::-1], LIGHT, col)


def house(mb, M, rng, w=1.6, d=1.4, h=1.2, roof="dome", lit=False):
    """A small dwelling: rounded box body + dome / cone / shell roof."""
    mb.box(M, w, d, h, SPECIES, shade(rng), taper=0.92)
    rc = shade(rng, 0.95)
    if roof == "dome":
        mb.dome(M @ T(z=h, s=(w * 0.5, d * 0.5, 1)), 1.0, 0.8, 7, TRIM, rc, rings=2)
    elif roof == "cone":
        mb.lathe(M @ T(z=h, s=(w * 0.56, d * 0.56, 1)), [(1.0, 0), (0.5, 0.55), (0.0, 1.0)], 6, TRIM, rc, smooth=False)
    else:  # shell: a squashed half-vault
        mb.vault(M @ T(z=h), w * 1.04, d * 1.04, 0.6, 4, TRIM, rc)
    if lit:
        window_strips(mb, M, w * 0.92, d * 0.92, h * 0.35, h * 0.75, 1, faces=(0, 1), h=0.18)


def wall_loop(mb, M, pts, h, w, col, gaps=(), taper=0.7):
    n = len(pts)
    for i in range(n):
        if i in gaps:
            continue
        mb.segment(M, pts[i], pts[(i + 1) % n], w, h, SPECIES, col, taper=taper)


def fill_blocks(mb, rng, n, avoid, r0=2.5, r1=8.4, h=(1.2, 2.6), roof="shell", gap=1.9):
    """Low lit blocks between the landmarks (avoid = [((x, y), radius)])."""
    for (x, y) in scatter_points(rng, n, r0, r1, avoid=avoid, min_gap=gap):
        house(mb, T(x, y, 0, rng.choice((0, 90)) + rng.random() * 12), rng, 1.2 + 0.5 * rng.random(),
              1.1 + 0.4 * rng.random(), h[0] + (h[1] - h[0]) * rng.random(), roof=roof, lit=True)


def scatter_points(rng, n, r0, r1, avoid=(), min_gap=2.2, tries=400):
    pts = []
    for _ in range(tries):
        if len(pts) >= n:
            break
        a = rng.random() * TAU
        r = math.sqrt(r0 * r0 + (r1 * r1 - r0 * r0) * rng.random())
        p = (r * math.cos(a), r * math.sin(a))
        if all(math.dist(p, q) >= min_gap for q in pts) and all(math.dist(p, c) >= rad for c, rad in avoid):
            pts.append(p)
    return pts


# ---------------------------------------------------------------- L1 Stone/Bronze


def town_l1_a(mb, rng, smoke):
    """Ring village: beehive huts in a loose spiral round a fire and a circle of leaning stones; crescent fields."""
    pad(mb, rng)
    I = Matrix()
    mb.disc(I, 2.6, 0.08, 12, GROUND, PLAZA)
    fire(mb, I, rng)
    for i in range(7):  # leaning stone circle
        a = TAU * i / 7 + 0.3
        mb.box(T(2.0 * math.cos(a), 2.0 * math.sin(a), 0, math.degrees(a), ry=-8 + 16 * rng.random()),
               0.35, 0.5, 1.2 + 0.5 * rng.random(), TRIM, shade(rng, 0.8), taper=0.55)
    for i in range(8):
        a = 0.6 * i + 0.4
        r = 3.6 + 0.42 * i
        s = 0.95 - 0.03 * i + 0.1 * rng.random()
        hut(mb, T(r * math.cos(a), r * math.sin(a), 0, math.degrees(a) + 180, ), rng, s, horn=(i % 3 == 0))
    for k, (a0, col) in enumerate([(3.6, FIELD_GREEN), (4.5, FIELD_GOLD), (5.4, FIELD_TEAL)]):
        crescent_field(mb, I, 7.0, 9.0, a0, a0 + 0.8, col)
    for i in range(10):  # fence posts along the fields
        a = 3.5 + 2.8 * i / 9
        mb.cyl(T(6.75 * math.cos(a), 6.75 * math.sin(a)), 0.07, 0.7, 4, TRIM, (0.5, 0.42, 0.34), smooth=False)
    for p in [(-1.0, -6.6), (6.8, 2.6)]:  # drying racks
        M = T(p[0], p[1], 0, rng.random() * 180)
        for dx in (-0.7, 0.7):
            mb.cyl(M @ T(dx), 0.06, 1.1, 4, TRIM, (0.5, 0.42, 0.34), smooth=False)
        mb.box(M @ T(z=1.0), 1.6, 0.1, 0.1, TRIM, (0.5, 0.42, 0.34))


def stilt_pod(mb, M, rng, s=1.0, legs=3, h=1.2):
    for k in range(legs):
        a = TAU * k / legs + 0.4
        mb.cyl(M @ T(0.6 * s * math.cos(a), 0.6 * s * math.sin(a), 0, s=s, rx=0), 0.08, h + 0.3, 4, TRIM,
               (0.55, 0.46, 0.38), smooth=False)
    mb.lathe(M @ T(z=h * s, s=s), [(0.0, 0.0), (0.7, 0.2), (0.95, 0.75), (0.75, 1.35), (0.3, 1.75), (0.0, 1.9)], 8,
             SPECIES, shade(rng), lobes=4, amp=0.05)
    mb.box(M @ T(0.85 * s, 0, (h + 0.35) * s, 0, s), 0.2, 0.42, 0.6, TRIM, (0.32, 0.28, 0.26), taper=0.7)
    # ladder
    mb.segment(M @ T(s=s), (0.9, -0.15), (1.5, -0.15), 0.06, 0.06, TRIM, (0.5, 0.42, 0.34), z0=0.02)


def town_l1_b(mb, rng, smoke):
    """Stilt village: egg pods on stilts round a twisted shrine, terraced fields in a grid, two fires, a pen."""
    pad(mb, rng)
    I = Matrix()
    mb.disc(I @ T(-1.0, 0.5), 2.2, 0.08, 10, GROUND, PLAZA)
    # spiral shrine
    mb.lathe(T(-1.0, 0.5), [(0.75, 0), (0.6, 1.0), (0.45, 2.2), (0.3, 3.3), (0.12, 4.2), (0.0, 4.6)], 6, TRIM,
             shade(rng, 0.85), lobes=3, amp=0.35, twist=70, smooth=False)
    fire(mb, T(1.2, 1.6), rng, 0.45)
    fire(mb, T(-3.0, -1.6), rng, 0.45)
    pods = [(-4.2, 2.6), (-2.2, 3.9), (0.4, 3.8), (2.6, 3.0), (-4.6, -0.4), (-1.4, -2.6), (1.4, -1.2), (3.4, 0.6)]
    for i, (x, y) in enumerate(pods):
        stilt_pod(mb, T(x, y, 0, math.degrees(math.atan2(0.5 - y, -1 - x))), rng, 0.85 + 0.15 * rng.random(),
                  legs=3 + (i % 2))
    # fields east, in a slightly turned grid
    cols = [FIELD_GREEN, FIELD_GOLD, FIELD_RUST, FIELD_GREEN, FIELD_TEAL, FIELD_GOLD]
    for k in range(6):
        cx = 5.6 + (k % 2) * 2.3
        cy = -4.5 + (k // 2) * 2.6
        if math.hypot(cx, cy) < 8.8:
            field(mb, T(cx, cy, 0, 12), 2.1, 2.3, cols[k], rows=3)
    # pen: ring of posts with a rail
    pc = (-3.6, -5.4)
    for i in range(9):
        a = TAU * i / 9
        mb.cyl(T(pc[0] + 1.5 * math.cos(a), pc[1] + 1.5 * math.sin(a)), 0.07, 0.75, 4, TRIM, (0.5, 0.42, 0.34),
               smooth=False)
    mb.lathe(T(pc[0], pc[1], 0.55), [(1.5, 0), (1.5, 0.08)], 9, TRIM, (0.5, 0.42, 0.34), cap_top=False)
    mb.lathe(T(pc[0], pc[1], 0.55), [(1.5, 0.08), (1.5, 0)], 9, TRIM, (0.5, 0.42, 0.34), cap_top=False)
    for x, y in [(6.0, 3.4), (2.0, -6.5)]:  # standing stones
        mb.box(T(x, y, 0, rng.random() * 90, ry=6), 0.5, 0.4, 2.0, TRIM, shade(rng, 0.75), taper=0.5)


# ---------------------------------------------------------------- L2 Iron/Medieval


def trefoil(r, amp, n, lobes=3, phase=0.0):
    return [((r * (1 + amp * math.cos(lobes * (TAU * i / n + phase)))) * math.cos(TAU * i / n),
             (r * (1 + amp * math.cos(lobes * (TAU * i / n + phase)))) * math.sin(TAU * i / n)) for i in range(n)]


def round_tower(mb, M, rng, r, h, cap="onion", lit=False):
    mb.cyl(M, r, h, 8, SPECIES, shade(rng), r2=r * 0.86)
    mb.cyl(M @ T(z=h), r * 1.08, 0.25, 8, TRIM, shade(rng, 0.85), smooth=False)
    if cap == "onion":
        onion(mb, M @ T(z=h + 0.25), r * 0.95, r * 2.4, shade(rng, 0.95))
    else:
        mb.lathe(M @ T(z=h + 0.25), [(r * 1.0, 0), (r * 1.25, 0.35), (r * 0.6, 0.6), (0, 0.8)], 8, TRIM,
                 shade(rng, 0.95))
    if lit:
        mb.box(M @ T(r * 0.88, 0, h * 0.7), 0.06, 0.22, 0.4, LIGHT, WHITE)


def town_l2_a(mb, rng, smoke):
    """Trefoil walled town: a three-lobed wall with onion towers at the lobes, a gate, domed houses and a ribbed
    spire temple on a stepped plinth."""
    pad(mb, rng)
    I = Matrix()
    mb.poly(I, trefoil(7.4, 0.13, 24), 0.08, GROUND, PLAZA)
    wall = trefoil(7.6, 0.13, 24)
    wall_loop(mb, I, wall, 1.7, 0.55, shade(rng), gaps=(12,))
    for k in range(3):  # towers at the lobe tips
        a = TAU * k / 3
        R = 7.6 * 1.13
        round_tower(mb, T(R * math.cos(a), R * math.sin(a)), rng, 0.85, 3.6, lit=True)
    for k in range(3):  # small towers in the waists
        a = TAU * k / 3 + math.pi / 3
        R = 7.6 * 0.87
        round_tower(mb, T(R * math.cos(a), R * math.sin(a)), rng, 0.6, 2.4, cap="cap")
    # gate (in the gap at index 12): two posts + an arch
    g0, g1 = wall[12], wall[13]
    for p in (g0, g1):
        mb.cyl(T(p[0], p[1]), 0.45, 2.4, 6, SPECIES, shade(rng))
        mb.box(T(p[0], p[1], 2.4), 0.25, 0.25, 0.35, LIGHT, WHITE)
    mb.segment(I, g0, g1, 0.5, 0.45, TRIM, shade(rng, 0.85), z0=2.0)
    # stepped plinth + ribbed spire
    for k, (r, h) in enumerate([(2.3, 0.45), (1.8, 0.45), (1.3, 0.45)]):
        mb.cyl(T(z=0.45 * k), r, h, 6, SPECIES, shade(rng, 0.95), smooth=False, phase=0.5)
    mb.lathe(T(z=1.35), [(0.95, 0), (1.05, 1.0), (0.85, 2.6), (0.55, 4.6), (0.28, 6.3), (0.0, 7.6)], 8, TRIM,
             shade(rng, 0.92), lobes=6, amp=0.16)
    mb.band(T(z=1.35), 1.0, 0.6, 0.25, 8, lobes=6, amp=0.16)
    # houses inside the wall, facing the spire
    for (x, y) in scatter_points(rng, 17, 3.0, 6.3, avoid=[(wall[12], 2.2)], min_gap=1.85):
        house(mb, T(x, y, 0, math.degrees(math.atan2(-y, -x))), rng, 1.25 + 0.4 * rng.random(), 1.1 + 0.3 * rng.random(),
              0.9 + 0.5 * rng.random(), roof="dome" if rng.random() < 0.7 else "cone", lit=rng.random() < 0.3)


def town_l2_b(mb, rng, smoke):
    """Mound citadel: an oval wall with squat mushroom-capped towers round a terraced mound; a ribbed great dome
    with twin bulb towers on top, houses on the lower terraces, fields outside."""
    pad(mb, rng)
    I = Matrix()
    sx, sy = 1.0, 0.8
    wall = [(8.0 * sx * math.cos(TAU * i / 20), 8.0 * sy * math.sin(TAU * i / 20)) for i in range(20)]
    wall_loop(mb, I, wall, 1.5, 0.6, shade(rng), gaps=(15,), taper=0.6)
    for k in range(5):
        a = TAU * k / 5 + 0.3
        p = (8.0 * sx * math.cos(a), 8.0 * sy * math.sin(a))
        mb.cyl(T(*p), 0.85, 2.4, 7, SPECIES, shade(rng), r2=0.95)
        mb.lathe(T(p[0], p[1], 2.4), [(0.4, 0), (1.3, 0.35), (1.1, 0.7), (0.0, 0.95)], 7, TRIM, shade(rng, 0.9))
        mb.box(T(p[0], p[1], 1.6, math.degrees(a)), 1.8, 0.2, 0.25, LIGHT, WHITE)
    # terraced mound (lobed discs)
    for k, (r, h) in enumerate([(5.6, 0.8), (4.0, 0.8), (2.6, 0.8)]):
        mb.lathe(T(z=0.8 * k), [(r, 0), (r * 0.97, h)], 14, SPECIES, shade(rng, 0.9), lobes=5, amp=0.06, sx=1.0,
                 sy=0.85, smooth=False)
    mb.dome(T(z=2.4), 1.9, 2.2, 10, TRIM, shade(rng, 0.95), rings=4, sy=0.9)
    for k in range(5):  # dome ribs
        a = TAU * k / 5
        mb.box(T(1.4 * math.cos(a), 1.4 * math.sin(a), 2.4, math.degrees(a), ry=-38), 0.22, 0.22, 2.4, SPECIES,
               shade(rng, 0.9))
    for dx in (-2.0, 2.0):
        mb.cyl(T(dx, 0, 2.4), 0.32, 3.4, 6, SPECIES, shade(rng))
        mb.lathe(T(dx, 0, 5.8), [(0.2, 0), (0.62, 0.35), (0.4, 0.8), (0.0, 1.15)], 7, TRIM, shade(rng, 0.9))
        mb.box(T(dx, -0.3, 5.0), 0.2, 0.06, 0.3, LIGHT, WHITE)
    # houses on the terraces
    for k, (r, z, n) in enumerate([(4.75, 0.8, 9), (3.3, 1.6, 6)]):
        for i in range(n):
            a = TAU * i / n + 0.25 * k + 0.2
            if 4.3 < a < 5.1 and k == 0:  # leave the stair
                continue
            house(mb, T(r * math.cos(a), r * 0.85 * math.sin(a), z, math.degrees(a) + 180), rng, 1.0, 0.9, 0.75,
                  roof="shell" if (i + k) % 3 == 0 else "dome", lit=(i % 4 == 0))
    # stair up the mound toward the gate
    a = 4.7
    for k in range(5):
        r = 5.5 - 0.7 * k
        mb.box(T(r * math.cos(a), r * 0.85 * math.sin(a), 0, math.degrees(a)), 0.7, 1.4, 0.42 * (k + 1), TRIM,
               shade(rng, 0.85))
    for k in range(2):  # fields outside the wall
        a = 0.6 + k * 1.6
        crescent_field(mb, I, 8.55, 9.3, a, a + 0.9, FIELD_GREEN if k else FIELD_GOLD, segs=4)


# ---------------------------------------------------------------- L3 Industrial


def trumpet_stack(mb, M, rng, h, r=0.45, flutes=8):
    """A fluted smokestack that flares into a trumpet bell at the top (sooty rim). Returns the top point."""
    mb.lathe(M, [(r * 1.5, 0), (r * 1.1, 0.6), (r, h * 0.55), (r * 0.95, h * 0.85), (r * 1.6, h)], 8, SPECIES,
             shade(rng, 0.85), lobes=flutes, amp=0.08, cap_top=False)
    mb.lathe(M @ T(z=h), [(r * 1.6, 0), (r * 1.2, -0.25)], 8, TRIM, (0.25, 0.23, 0.22), cap_top=True, smooth=False)
    mb.band(M, r * 0.97, h * 0.8, 0.18, 8, col=(1.0, 0.5, 0.35))  # red aircraft band
    return M @ Vector((0, 0, h + 0.2))


def rail(mb, M, pts, ties=True):
    for a, b in zip(pts, pts[1:]):
        mb.segment(M, a, b, 1.3, 0.04, GROUND, ROAD, z0=0.04)
        dx, dy = b[0] - a[0], b[1] - a[1]
        L = math.hypot(dx, dy)
        nx, ny = -dy / L, dx / L
        for side in (-0.38, 0.38):
            mb.segment(M, (a[0] + nx * side, a[1] + ny * side), (b[0] + nx * side, b[1] + ny * side), 0.08, 0.1, TRIM,
                       (0.45, 0.45, 0.48), z0=0.08)


def gas_holder(mb, M, rng, r, h):
    mb.cyl(M, r, h, 10, SPECIES, shade(rng, 0.92), smooth=True, lobes=10, amp=0.03)
    mb.dome(M @ T(z=h), r, r * 0.3, 10, TRIM, shade(rng, 0.9), rings=2)
    for k in range(5):  # guide frame
        a = TAU * k / 5
        mb.cyl(M @ T((r + 0.12) * math.cos(a), (r + 0.12) * math.sin(a)), 0.07, h + 0.4, 4, TRIM, (0.4, 0.4, 0.42),
               smooth=False)


def town_l3_a(mb, rng, smoke):
    """Mill town: a works with three trumpet stacks over a vault hall, gas holders, warehouses along a rail line
    crossing the hex, terraced rows of shell-roofed houses with lit windows, street lamps."""
    pad(mb, rng)
    I = Matrix()
    rail(mb, I, [(-9.0, -2.6), (-3.0, -2.2), (3.0, -2.6), (9.0, -3.6)])
    mb.segment(I, (-8.6, 2.0), (8.6, 2.6), 1.6, 0.03, GROUND, ROAD, z0=0.04)  # main street
    mb.segment(I, (1.2, -1.5), (0.4, 8.6), 1.3, 0.03, GROUND, ROAD, z0=0.04)
    # the works (south of the rail)
    works = T(-1.5, -5.3, 0, -4)
    mb.vault(works, 7.0, 3.2, 1.3, 6, TRIM, shade(rng, 0.9), wall=2.0, wall_col=shade(rng))
    window_strips(mb, works, 7.0, 3.2, 0.7, 1.5, 1, faces=(2, 1, 3))
    for k, dx in enumerate((-2.2, 0.0, 2.2)):
        top = trumpet_stack(mb, works @ T(dx, -1.0), rng, 7.2 + 1.0 * (k == 1))
        smoke.append(top)
    # gas holders east
    gas_holder(mb, T(6.0, -6.0), rng, 1.5, 2.2)
    gas_holder(mb, T(7.2, -3.0 + 0.0), rng, 1.0, 1.6)
    # warehouses along the rail, north side
    for x in (-6.5, -2.9):
        M = T(x, -0.6, 0, 3)
        mb.vault(M, 3.2, 1.8, 0.8, 5, TRIM, shade(rng, 0.88), wall=1.1, wall_col=shade(rng))
    # a little train on the rail
    for k in range(3):
        mb.vault(T(4.0 + 1.7 * k, -2.75, 0.14, -9), 1.5, 0.9, 0.35, 3, TRIM if k == 0 else SPECIES,
                 shade(rng, 0.7), wall=0.5, wall_col=(0.35, 0.33, 0.33))
    # house rows north of the street
    for row, y in enumerate((4.2, 6.3)):
        for i in range(6 - row):
            x = -6.2 + i * 2.15 + row * 1.0
            if abs(x - 0.9) < 1.2:
                continue
            if math.hypot(x, y) > 8.6:
                continue
            house(mb, T(x, y, 0, 180 + 3 * rng.random()), rng, 1.8, 1.5, 1.3 + 0.4 * (i % 2), roof="shell", lit=True)
    # depot by the street
    dep = T(4.6, 4.4, 0, 0)
    mb.box(dep, 3.0, 2.2, 1.8, SPECIES, shade(rng))
    mb.dome(dep @ T(z=1.8, s=(1.5, 1.1, 1)), 1.0, 0.7, 8, TRIM, shade(rng, 0.9), rings=2)
    window_strips(mb, dep, 3.0, 2.2, 0.9, 1.4, 1)
    for x in (-5.0, -1.8, 2.8, 6.4):
        lamp_post(mb, T(x, 2.3 - 1.0 * (x > 0) + 1.0))


def town_l3_b(mb, rng, smoke):
    """Kiln town: a domed kiln with one great stack, a sawtooth works with a short stack, a bulb water tower,
    a curving rail into a yard with a crane, cone-roofed houses."""
    pad(mb, rng)
    I = Matrix()
    curve = [(-9.0, 4.0), (-5.0, 3.0), (-1.5, 1.2), (1.5, -1.5), (3.5, -5.0), (4.2, -9.0)]
    rail(mb, I, curve)
    mb.segment(I, (-8.8, -1.5), (0.5, -1.0), 1.4, 0.03, GROUND, ROAD, z0=0.04)
    mb.segment(I, (2.0, 0.5), (8.8, 2.0), 1.4, 0.03, GROUND, ROAD, z0=0.04)
    # kiln: a big lobed dome with a stack out of its crown
    kiln = T(-3.6, -5.0)
    mb.cyl(kiln, 2.6, 0.9, 12, SPECIES, shade(rng), lobes=6, amp=0.08)
    mb.dome(kiln @ T(z=0.9), 2.6, 2.4, 12, TRIM, shade(rng, 0.85), rings=4, lobes=6, amp=0.08)
    for k in range(4):
        a = TAU * k / 4 + 0.4
        mb.box(kiln @ T(2.55 * math.cos(a), 2.55 * math.sin(a), 0, math.degrees(a)), 0.1, 0.8, 0.6, LIGHT,
               (1.0, 0.75, 0.5))
    smoke.append(trumpet_stack(mb, kiln @ T(z=2.6), rng, 6.5, r=0.55, flutes=6))
    # sawtooth works
    works = T(4.6, -2.0, 0, 8)
    mb.box(works, 4.4, 3.2, 1.6, SPECIES, shade(rng))
    for k in range(4):
        mb.box(works @ T(-1.65 + 1.1 * k, 0, 1.6), 1.1, 3.2, 0.9, TRIM, shade(rng, 0.9), taper=1.0,
               top_shift=(0.4, 0.0))
        mb.quad(works @ T(-1.65 + 1.1 * k + 0.56, 0, 1.6), [(0, -1.5, 0.05), (0, 1.5, 0.05), (0, 1.5, 0.75),
                                                              (0, -1.5, 0.75)][::-1], GLASS, WHITE)
    window_strips(mb, works, 4.4, 3.2, 0.6, 1.2, 1, faces=(0, 2))
    smoke.append(trumpet_stack(mb, works @ T(2.6, 1.0), rng, 4.6, r=0.35))
    # water tower
    wt = T(1.0, 5.0)
    for k in range(3):
        a = TAU * k / 3
        mb.cyl(wt @ T(0.7 * math.cos(a), 0.7 * math.sin(a)), 0.1, 3.4, 4, TRIM, (0.45, 0.45, 0.48), smooth=False)
    mb.cyl(wt, 0.25, 3.4, 6, TRIM, (0.45, 0.45, 0.48))
    mb.lathe(wt @ T(z=3.2), [(0.2, 0), (1.2, 0.5), (1.35, 1.2), (0.9, 1.9), (0.0, 2.2)], 10, SPECIES, shade(rng))
    mb.band(wt @ T(z=3.2), 1.3, 1.0, 0.15, 10)
    # yard: crane + crates
    yard = T(-0.5, 0.5, 0, 35)
    mb.poly(yard, [(-2.0, -1.3), (2.0, -1.3), (2.0, 1.3), (-2.0, 1.3)], 0.06, GROUND, ROAD)
    for k in range(3):
        mb.box(yard @ T(-1.2 + 1.1 * k, 0.6), 0.9, 0.8, 0.7, SPECIES, shade(rng, 0.8))
    mb.cyl(yard @ T(1.4, -0.7), 0.15, 3.2, 4, TRIM, (0.85, 0.6, 0.2), smooth=False)
    mb.box(yard @ T(0.6, -0.7, 3.1), 3.0, 0.2, 0.2, TRIM, (0.85, 0.6, 0.2))
    # cone-roofed houses
    for (x, y) in scatter_points(rng, 13, 3.5, 8.6, avoid=[((-3.6, -5.0), 3.6), ((4.6, -2.0), 3.6), ((1.0, 5.0), 2.0),
                                                           ((-0.5, 0.5), 2.8)] + [(c, 1.6) for c in curve], min_gap=2.0):
        house(mb, T(x, y, 0, rng.random() * 360), rng, 1.5, 1.4, 1.2 + 0.5 * rng.random(), roof="cone", lit=True)
    for p in [(-6.0, -0.4), (-2.0, -0.2), (5.0, 2.6)]:
        lamp_post(mb, T(*p))


# ---------------------------------------------------------------- L4 Atomic


def saucer(mb, M, r, h, mat, col, segs=12):
    mb.lathe(M, [(0.0, -h / 2), (r * 0.7, -h * 0.35), (r, 0.0), (r * 0.7, h * 0.35), (0.0, h / 2)], segs, mat, col)


def glass_slab(mb, M, rng, sx, sy, h):
    mb.box(M, sx, sy, h, GLASS, WHITE)
    mb.box(M @ T(z=h), sx + 0.15, sy + 0.15, 0.25, TRIM, shade(rng, 0.9))
    mb.box(M, sx + 0.2, sy + 0.2, 0.6, SPECIES, shade(rng))
    window_strips(mb, M, sx, sy, 1.2, h - 0.4, max(2, int(h / 1.4)), h=0.16)


def dish(mb, M, r, depth, col):
    prof = [(0.0, 0.0), (r * 0.5, depth * 0.25), (r, depth)]
    mb.lathe(M, prof, 10, TRIM, col, cap_top=False)
    mb.lathe(M @ T(z=-0.03), prof, 10, TRIM, col, cap_top=False, flip=True)
    mb.cyl(M, 0.05, depth * 1.8, 4, TRIM, col, smooth=False)


def mast(mb, M, h, col=(0.55, 0.55, 0.58)):
    mb.lathe(M, [(0.35, 0), (0.08, h)], 3, TRIM, col, smooth=False)
    for z in (h * 0.45, h * 0.75):
        mb.box(M @ T(z=z), 1.0 - z / h * 0.6, 0.06, 0.06, TRIM, col)
    mb.box(M @ T(z=h), 0.2, 0.2, 0.2, LIGHT, (1.0, 0.35, 0.3))


def cooling_tower(mb, M, rng, r, h):
    prof = [(r, 0), (r * 0.82, h * 0.35), (r * 0.66, h * 0.7), (r * 0.72, h)]
    mb.lathe(M, prof, 12, SPECIES, shade(rng, 0.95), cap_top=False)
    mb.disc(M, r * 0.68, h - 0.5, 12, TRIM, (0.25, 0.25, 0.27))
    return M @ Vector((0, 0, h + 0.3))


def town_l4_a(mb, rng, smoke):
    """Atomic city: a saucer-ringed central tower with a bulb crown, glass slabs, a tilted dish, antenna masts,
    a mushroom water tower, a cooling tower (smoke), road grid with street lights."""
    pad(mb, rng)
    I = Matrix()
    for a, b in [((-9.0, 0.6), (9.0, 0.6)), ((0.6, -9.0), (0.6, 9.0)), ((-7.0, -5.4), (7.0, -5.4))]:
        mb.segment(I, a, b, 1.5, 0.03, GROUND, ROAD, z0=0.04)
    # central tower
    C = T(-2.4, 3.2)
    mb.cyl(C, 1.5, 0.8, 10, SPECIES, shade(rng), smooth=False)
    mb.lathe(C, [(1.0, 0.8), (0.85, 6.0), (0.7, 11.5), (0.55, 13.5)], 10, SPECIES, shade(rng), cap_top=True)
    for z, r in [(4.2, 2.4), (8.0, 2.0), (11.2, 1.6)]:
        saucer(mb, C @ T(z=z), r, 0.7, TRIM, shade(rng, 0.92))
        mb.band(C @ T(z=z - 0.06), r * 0.98 - 0.04, 0, 0.12, 12)
    mb.band(C, 0.9, 1.6, 2.0, 10, lobes=0)
    mb.band(C, 0.78, 9.0, 1.5, 10)
    mb.lathe(C @ T(z=13.5), [(0.0, 0), (0.9, 0.5), (1.0, 1.0), (0.5, 1.7), (0.0, 1.9)], 10, GLASS, WHITE)
    mb.box(C @ T(z=15.3), 0.12, 0.12, 1.6, TRIM, (0.6, 0.6, 0.62))
    mb.box(C @ T(z=16.9), 0.2, 0.2, 0.2, LIGHT, (1.0, 0.35, 0.3))
    # glass slabs
    glass_slab(mb, T(4.4, 4.0, 0, 8), rng, 2.6, 1.8, 7.5)
    glass_slab(mb, T(5.0, -2.5, 0, -6), rng, 2.2, 2.2, 5.5)
    glass_slab(mb, T(-4.6, -2.5, 0, 4), rng, 3.0, 1.6, 4.6)
    # dish on a turret
    mb.cyl(T(-6.8, 3.8), 0.9, 1.4, 8, SPECIES, shade(rng))
    dish(mb, T(-6.8, 3.8, 1.4, 30, ry=35), 1.7, 0.75, shade(rng, 0.95))
    mast(mb, T(2.4, 7.4), 8.5)
    mast(mb, T(-1.2, -7.4), 6.5)
    # mushroom water tower
    mb.cyl(T(-6.4, -6.0), 0.35, 3.2, 8, SPECIES, shade(rng))
    mb.lathe(T(-6.4, -6.0, 3.0), [(0.3, 0), (1.4, 0.45), (1.25, 0.9), (0.0, 1.05)], 10, TRIM, shade(rng, 0.92))
    # cooling tower + reactor block
    smoke.append(cooling_tower(mb, T(4.0, -7.0), rng, 1.7, 4.2))
    mb.box(T(1.4, -7.4), 1.6, 1.4, 1.6, SPECIES, shade(rng))
    mb.dome(T(1.4, -7.4, 1.6), 0.65, 0.65, 8, TRIM, shade(rng, 0.9), rings=2)
    # low blocks
    for (x, y, w, d, h) in [(-5.6, 0.3, 2.2, 1.6, 2.2), (6.8, 1.6, 1.6, 1.8, 2.6), (-2.6, -4.0, 1.6, 1.4, 1.8),
                            (3.0, 1.9, 1.4, 1.4, 2.0)]:
        house(mb, T(x, y, 0, 0), rng, w, d, h, roof="shell", lit=True)
    for x, y in [(-7.0, 1.6), (-3.5, 1.6), (2.0, 1.6), (6.0, -0.4), (1.6, 5.0), (-0.4, -3.0), (1.6, -5.0),
                 (-4.0, -4.4)]:
        lamp_post(mb, T(x, y), 1.4)
    roads = [((x, 0.6), 1.3) for x in range(-9, 10, 2)] + [((0.6, y), 1.3) for y in range(-9, 10, 2)] + \
            [((x, -5.4), 1.3) for x in range(-7, 8, 2)]
    fill_blocks(mb, rng, 9, roads + [((-2.4, 3.2), 2.6), ((4.4, 4.0), 2.2), ((5.0, -2.5), 2.0), ((-4.6, -2.5), 2.2),
                                     ((-6.8, 3.8), 1.8), ((2.4, 7.4), 1.0), ((-1.2, -7.4), 1.0), ((-6.4, -6.0), 1.8),
                                     ((4.0, -7.0), 2.2), ((1.4, -7.4), 1.4), ((-5.6, 0.3), 1.8), ((6.8, 1.6), 1.6),
                                     ((-2.6, -4.0), 1.5), ((3.0, 1.9), 1.5)])


def town_l4_b(mb, rng, smoke):
    """Spiral city: a twisted three-lobed tower with glass bands, crescent slab blocks round a glass-roofed dome
    stadium, a power house with a short stack and a cooling tower (smoke), masts, lamp-lit ring road."""
    pad(mb, rng)
    I = Matrix()
    ring = [(7.0 * math.cos(TAU * i / 16), 7.0 * math.sin(TAU * i / 16)) for i in range(16)]
    for i in range(16):
        mb.segment(I, ring[i], ring[(i + 1) % 16], 1.3, 0.03, GROUND, ROAD, z0=0.04)
    mb.segment(I, (-9.0, -0.4), (9.0, -0.4), 1.3, 0.03, GROUND, ROAD, z0=0.04)
    # spiral tower
    S = T(-2.0, 2.4)
    prof = [(1.5, 0), (1.35, 3.0), (1.2, 7.0), (1.0, 11.0), (0.7, 14.0), (0.0, 16.0)]
    mb.lathe(S, prof, 12, SPECIES, shade(rng), lobes=3, amp=0.32, twist=14)
    for z in (2.0, 4.2, 6.4, 8.6, 10.8):
        r = 1.5 - 0.5 * z / 14.0
        mb.band(S, r * 1.02, z, 0.5, 12, mat=GLASS, lobes=3, amp=0.32, twist=14)
        mb.band(S, r * 1.02 + 0.02, z + 0.15, 0.18, 12, lobes=3, amp=0.32, twist=14)
    mb.cyl(S, 2.2, 0.6, 12, TRIM, shade(rng, 0.9), smooth=False)
    # dome stadium
    D = T(3.8, 3.6)
    mb.cyl(D, 2.6, 0.9, 14, SPECIES, shade(rng))
    mb.dome(D @ T(z=0.9), 2.6, 1.6, 14, GLASS, WHITE, rings=3, sy=0.85)
    mb.band(D, 2.6, 0.4, 0.2, 14)
    # crescent blocks (arcs of boxes)
    for a0, n, r, h in [(3.6, 5, 5.0, 3.2), (5.0, 4, 8.3, 2.0)]:
        for k in range(n):
            a = a0 + k * 0.28
            M = T(r * math.cos(a), r * math.sin(a), 0, math.degrees(a) + 90)
            mb.box(M, 1.5, 1.2, h, SPECIES, shade(rng))
            mb.box(M @ T(z=h), 1.55, 1.25, 0.2, TRIM, shade(rng, 0.9))
            window_strips(mb, M, 1.5, 1.2, 0.6, h - 0.3, max(1, int(h / 1.1)), faces=(0, 2), h=0.15)
    # power house: box + stack + cooling tower
    P = T(4.6, -4.8, 0, -20)
    mb.box(P, 2.8, 1.8, 2.0, SPECIES, shade(rng))
    mb.vault(P @ T(z=2.0), 2.8, 1.8, 0.6, 4, TRIM, shade(rng, 0.9))
    smoke.append(trumpet_stack(mb, P @ T(-0.9, 0.4, 2.0), rng, 4.0, r=0.3, flutes=6))
    smoke.append(cooling_tower(mb, T(1.4, -6.8), rng, 1.5, 3.8))
    mast(mb, T(-6.4, -3.6), 7.5)
    mast(mb, T(7.6, 0.6), 5.5)
    dish(mb, T(-5.0, 5.6, 0.2, -140, ry=50), 1.2, 0.5, shade(rng, 0.95))
    for (x, y, w, d, h) in [(-4.4, -5.6, 1.8, 1.4, 1.6), (-1.6, -4.4, 1.5, 1.5, 2.4), (6.4, -2.2, 1.4, 1.4, 1.8)]:
        house(mb, T(x, y, 0, 15), rng, w, d, h, roof="dome", lit=True)
    for i in range(0, 16, 2):
        lamp_post(mb, T(7.9 * math.cos(TAU * i / 16 + 0.2), 7.9 * math.sin(TAU * i / 16 + 0.2)), 1.4)
    roads = [(p, 1.2) for p in ring] + [((x, -0.4), 1.2) for x in range(-9, 10, 2)]
    fill_blocks(mb, rng, 8, roads + [((-2.0, 2.4), 2.6), ((3.8, 3.6), 3.0), ((4.6, -4.8), 2.2), ((1.4, -6.8), 2.0),
                                     ((-6.4, -3.6), 1.0), ((7.6, 0.6), 1.0), ((-5.0, 5.6), 1.6), ((-4.4, -5.6), 1.4),
                                     ((-1.6, -4.4), 1.4), ((6.4, -2.2), 1.4)] +
                [((5.0 * math.cos(3.6 + k * 0.28), 5.0 * math.sin(3.6 + k * 0.28)), 1.3) for k in range(5)],
                r0=2.0, r1=6.2, h=(1.4, 3.0), roof="dome")


# ---------------------------------------------------------------- L5 Early warp


def rocket(mb, M, rng, h=12.0):
    """A tripod launcher: a slender spindle with three outrigger pods on struts and a lit ring at the base."""
    body = [(0.0, 0.0), (0.55, 0.3), (0.62, 1.5), (0.6, h * 0.65), (0.45, h * 0.85), (0.2, h * 0.97), (0.0, h)]
    mb.lathe(M @ T(z=1.0), body, 10, SPECIES, (1.0, 1.0, 1.0))
    mb.band(M @ T(z=1.0), 0.6, h * 0.5, 0.35, 10, mat=TRIM, col=shade(rng, 0.8))
    for k in range(3):
        a = TAU * k / 3
        P = M @ T(1.35 * math.cos(a), 1.35 * math.sin(a), 1.0)
        mb.lathe(P, [(0.0, 0.0), (0.35, 0.25), (0.38, h * 0.38), (0.0, h * 0.5)], 8, TRIM, shade(rng, 0.95))
        mb.segment(M @ T(z=1.0 + h * 0.3), (0.5 * math.cos(a), 0.5 * math.sin(a)), (1.2 * math.cos(a), 1.2 * math.sin(a)),
                   0.12, 0.25, TRIM, (0.6, 0.6, 0.62))
        mb.box(M @ T(1.6 * math.cos(a), 1.6 * math.sin(a), 0, math.degrees(a), ry=10), 0.9, 0.08, 1.4, TRIM,
               shade(rng, 0.85), taper=0.4)


def gantry(mb, M, h, col=(0.75, 0.55, 0.2)):
    for dx in (-0.6, 0.6):
        for dy in (-0.6, 0.6):
            mb.box(M @ T(dx, dy), 0.14, 0.14, h, TRIM, col)
    for z in range(1, int(h), 2):
        mb.box(M @ T(z=z), 1.4, 1.4, 0.12, TRIM, col)
    mb.box(M @ T(-1.1, 0, h * 0.7), 2.2, 0.2, 0.2, TRIM, col)
    mb.box(M @ T(z=h), 0.2, 0.2, 0.2, LIGHT, (1.0, 0.35, 0.3))


def town_l5_a(mb, rng, smoke):
    """Early-warp city + spaceport: a launch pad with a tripod rocket and gantry, a glass control dome, pad
    lights; a city of a flared tower, a twisted tower, slabs and a saucer hall across a boulevard."""
    pad(mb, rng)
    I = Matrix()
    mb.segment(I, (-9.0, 1.0), (9.0, -0.2), 1.6, 0.03, GROUND, ROAD, z0=0.04)
    mb.segment(I, (-1.0, 1.0), (-2.6, 9.0), 1.4, 0.03, GROUND, ROAD, z0=0.04)
    # spaceport (south-east)
    SP = T(3.4, -4.6)
    mb.cyl(SP, 3.6, 0.3, 16, TRIM, (0.62, 0.62, 0.65), smooth=False)
    mb.disc(SP, 2.9, 0.33, 16, GROUND, ROAD)
    for i in range(12):
        a = TAU * i / 12
        mb.box(SP @ T(3.4 * math.cos(a), 3.4 * math.sin(a), 0.3), 0.22, 0.22, 0.1, LIGHT, (0.6, 0.85, 1.0))
    rocket(mb, SP @ T(z=0.3), rng, 11.0)
    gantry(mb, SP @ T(2.2, 1.0, 0.3), 9.0)
    mb.box(SP @ T(-3.2, 2.6), 0.6, 0.6, 0.5, TRIM, (0.4, 0.4, 0.42))  # blast deflector
    # control dome + blockhouse
    CD = T(-1.6, -6.4)
    mb.cyl(CD, 1.6, 0.6, 12, SPECIES, shade(rng))
    mb.dome(CD @ T(z=0.6), 1.6, 1.3, 12, GLASS, WHITE, rings=3)
    mb.band(CD, 1.6, 0.2, 0.18, 12)
    dish(mb, T(-4.2, -5.2, 0.0, 120, ry=40), 1.3, 0.55, shade(rng, 0.95))
    # city (north)
    F = T(-3.6, 4.2)  # flared tower
    mb.lathe(F, [(1.6, 0), (0.9, 1.5), (0.8, 8.0), (1.1, 11.5), (1.4, 12.5), (0.0, 13.2)], 10, SPECIES, shade(rng),
             lobes=5, amp=0.1)
    for z in (2.5, 4.0, 5.5, 7.0, 8.5):
        mb.band(F, 0.85, z, 0.3, 10, lobes=5, amp=0.1)
    mb.band(F, 1.32, 12.0, 0.3, 10, mat=GLASS, lobes=5, amp=0.1)
    W = T(1.6, 4.6)  # twisted tower
    mb.lathe(W, [(1.2, 0), (1.1, 4.0), (0.9, 8.0), (0.55, 10.0), (0.0, 11.0)], 10, TRIM, shade(rng, 0.95),
             lobes=2, amp=0.35, twist=20)
    for z in (1.5, 3.3, 5.1, 6.9):
        mb.band(W, 1.15 - z * 0.03, z, 0.35, 10, mat=GLASS, lobes=2, amp=0.35, twist=20)
        mb.band(W, 1.15 - z * 0.03 + 0.02, z + 0.1, 0.15, 10, lobes=2, amp=0.35, twist=20)
    glass_slab(mb, T(5.4, 3.2, 0, -10), rng, 2.4, 1.6, 6.0)
    glass_slab(mb, T(-6.8, 0.0 + 3.0, 0, 15), rng, 1.6, 2.2, 4.2)
    saucer(mb, T(-0.6, 7.6, 2.2), 1.9, 0.9, TRIM, shade(rng, 0.92))
    mb.cyl(T(-0.6, 7.6), 0.4, 2.0, 8, SPECIES, shade(rng))
    mb.band(T(-0.6, 7.6, 2.2), 1.85, -0.05, 0.12, 12)
    for (x, y, w, d, h) in [(-6.2, -2.4, 1.8, 1.6, 2.2), (-6.6, 6.2, 1.5, 1.5, 1.8), (6.6, 6.2, 1.4, 1.4, 1.6)]:
        house(mb, T(x, y, 0, 10), rng, w, d, h, roof="dome", lit=True)
    for x in (-7.0, -4.0, -0.6, 2.6, 6.0):
        lamp_post(mb, T(x, 2.2 - 0.07 * x), 1.4)
    road = [((x, 1.0 - 0.067 * (x + 9)), 1.3) for x in range(-9, 10, 2)] + \
           [((-1.0 - 0.2 * (y - 1), y), 1.2) for y in range(1, 10, 2)]
    fill_blocks(mb, rng, 9, road + [((3.4, -4.6), 4.6), ((-1.6, -6.4), 2.2), ((-4.2, -5.2), 1.6), ((-3.6, 4.2), 2.0),
                                    ((1.6, 4.6), 1.8), ((5.4, 3.2), 1.9), ((-6.8, 3.0), 1.7), ((-0.6, 7.6), 2.2),
                                    ((-6.2, -2.4), 1.6), ((-6.6, 6.2), 1.4), ((6.6, 6.2), 1.4)],
                r0=1.5, r1=8.3, h=(1.4, 3.4))


# ---------------------------------------------------------------- L6 Spacefaring


def arcology(mb, M, rng, h, r, lobes=3, twist=18.0):
    """A spiral arcology: a lobed spire that twists as it narrows, ribbed with glass and light bands."""
    prof = [(r * 1.25, 0), (r, h * 0.12), (r * 0.82, h * 0.4), (r * 0.6, h * 0.7), (r * 0.32, h * 0.9), (0.0, h)]
    mb.lathe(M, prof, 12, SPECIES, shade(rng), lobes=lobes, amp=0.28, twist=twist)

    def radius(z):
        for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
            if z0 <= z <= z1:
                return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
        return 0.0

    z = h * 0.08
    while z < h * 0.82:
        rr = radius(z) * 1.01
        mb.band(M, rr, z, h * 0.035, 12, mat=GLASS, lobes=lobes, amp=0.28, twist=twist)
        mb.band(M, rr + 0.02, z + h * 0.01, h * 0.012, 12, lobes=lobes, amp=0.28, twist=twist)
        z += h * 0.085
    mb.box(M @ T(z=h - 0.2), 0.1, 0.1, 2.0, TRIM, (0.7, 0.7, 0.72))
    mb.box(M @ T(z=h + 1.8), 0.22, 0.22, 0.22, LIGHT, (1.0, 0.35, 0.3))


def stalk_pad(mb, M, rng, h, r):
    mb.lathe(M, [(0.9, 0), (0.45, h * 0.3), (0.4, h * 0.85), (r * 0.6, h)], 8, SPECIES, shade(rng))
    mb.cyl(M @ T(z=h), r, 0.3, 14, TRIM, (0.68, 0.68, 0.7), smooth=False)
    for i in range(8):
        a = TAU * i / 8
        mb.box(M @ T((r - 0.25) * math.cos(a), (r - 0.25) * math.sin(a), h + 0.3), 0.18, 0.18, 0.06, LIGHT,
               (0.6, 0.85, 1.0))


def lander(mb, M, rng):
    saucer(mb, M @ T(z=0.45, s=(1, 0.7, 1)), 1.3, 0.6, SPECIES, (0.95, 0.95, 0.97), segs=10)
    mb.box(M @ T(-1.1, 0, 0.35), 0.4, 0.5, 0.2, LIGHT, (0.6, 0.85, 1.0))


def town_l6_a(mb, rng, smoke):
    """Spacefaring: three spiral arcologies of different heights linked by sky bridges, landing pads on stalks
    with landers, glass park domes, a maglev loop."""
    pad(mb, rng)
    I = Matrix()
    loop = [(8.0 * math.cos(TAU * i / 20), 8.0 * math.sin(TAU * i / 20)) for i in range(20)]
    for i in range(20):
        mb.segment(I, loop[i], loop[(i + 1) % 20], 0.5, 0.25, TRIM, (0.75, 0.75, 0.78), z0=0.6)
        if i % 4 == 0:
            mb.cyl(T(*loop[i]), 0.15, 0.65, 4, TRIM, (0.6, 0.6, 0.62), smooth=False)
    for i in range(0, 20, 2):
        mb.box(T(loop[i][0], loop[i][1], 0.85), 0.3, 0.3, 0.06, LIGHT, (0.6, 0.85, 1.0))
    towers = [((-1.8, 1.4), 30.0, 2.6, 3, 7.0), ((3.4, 3.4), 21.0, 2.0, 4, -10.0), ((-1.0, -4.0), 16.0, 1.8, 3, 13.0)]
    for (x, y), h, r, lobes, tw in towers:
        arcology(mb, T(x, y, 0, rng.random() * 120), rng, h, r, lobes, tw)
    # sky bridges between the towers
    for (a, ha), (b, hb) in [((towers[0][0], 12.0), (towers[1][0], 12.0)), ((towers[0][0], 8.5), (towers[2][0], 8.5)),
                             ((towers[1][0], 6.0), (towers[2][0], 6.0))]:
        mb.segment(I, a, b, 0.55, 0.45, GLASS, WHITE, z0=ha)
        mb.segment(I, a, b, 0.6, 0.08, LIGHT, WHITE, z0=ha - 0.05)
    # landing pads on stalks
    for (x, y, h, r) in [(6.0, -3.0, 5.0, 1.9), (-5.8, 4.6, 4.0, 1.7), (-6.0, -3.0, 2.8, 1.6)]:
        stalk_pad(mb, T(x, y), rng, h, r)
        lander(mb, T(x + 0.2, y, h + 0.3, rng.random() * 360), rng)
    # park domes
    for (x, y, r) in [(2.8, -5.6, 1.9), (5.6, 1.0 - 0.6, 1.3), (-4.4, 0.0, 1.2)]:
        mb.disc(T(x, y), r, 0.09, 12, GROUND, PARK)
        for k in range(3):
            a = TAU * k / 3 + 0.5
            mb.lathe(T(x + r * 0.45 * math.cos(a), y + r * 0.45 * math.sin(a)), [(0.35, 0.3), (0.3, 0.75), (0.0, 1.0)],
                     5, GROUND, PARK)
        mb.dome(T(x, y), r, r * 0.75, 12, GLASS, WHITE, rings=3)
        mb.band(T(x, y), r, 0.05, 0.12, 12)


# ---------------------------------------------------------------- far stand-ins (<= 300 tris)


def far_pad(mb, r=9.3):
    mb.disc(Matrix(), r, 0.05, 8, GROUND, DIRT, phase=0.2)


def town_l1_far(mb, rng, smoke):
    far_pad(mb)
    for i in range(6):
        a = TAU * i / 6 + 0.4
        mb.lathe(T(4.2 * math.cos(a), 4.2 * math.sin(a)), [(1.0, 0), (0.9, 1.2), (0.0, 2.2)], 5, SPECIES, WHITE,
                 smooth=False)
    mb.lathe(Matrix(), [(0.3, 0.05), (0.0, 0.8)], 4, LIGHT, WHITE)
    crescent_field(mb, Matrix(), 7.0, 9.0, 3.6, 5.6, FIELD_GREEN, segs=3)


def town_l2_far(mb, rng, smoke):
    far_pad(mb)
    pts = trefoil(7.6, 0.13, 12)
    for i in range(12):
        mb.segment(Matrix(), pts[i], pts[(i + 1) % 12], 0.6, 1.7, SPECIES, WHITE)
    for k in range(3):
        a = TAU * k / 3
        mb.lathe(T(8.6 * math.cos(a), 8.6 * math.sin(a)), [(0.85, 0), (0.75, 3.6), (0.9, 4.0), (0.0, 5.6)], 5, TRIM,
                 WHITE, smooth=False)
    mb.lathe(Matrix(), [(1.6, 0), (1.0, 1.4), (0.5, 5.0), (0.0, 9.0)], 5, TRIM, WHITE, smooth=False)
    for k in range(4):
        a = TAU * k / 4 + 0.8
        mb.box(T(4.5 * math.cos(a), 4.5 * math.sin(a), 0, math.degrees(a)), 2.4, 1.6, 1.3, SPECIES, (0.9, 0.9, 0.9))


def town_l3_far(mb, rng, smoke):
    far_pad(mb)
    mb.box(T(-1.5, -5.3), 7.0, 3.2, 2.8, SPECIES, (0.9, 0.9, 0.9))
    for dx in (-2.2, 0.0, 2.2):
        mb.lathe(T(-1.5 + dx, -6.3), [(0.6, 0), (0.5, 8.0)], 5, SPECIES, (0.8, 0.8, 0.8))
    smoke.append(Vector((-1.5, -6.3, 8.2)))
    for (x, y) in [(-5.0, 4.6), (-1.8, 4.6), (4.6, 4.4), (-4.7, -0.6)]:
        mb.box(T(x, y), 2.8, 2.0, 1.8, TRIM, WHITE)
    mb.lathe(T(6.0, -6.0), [(1.5, 0), (1.5, 2.4), (0.0, 2.8)], 6, SPECIES, WHITE, smooth=False)
    mb.segment(Matrix(), (-9.0, -2.6), (9.0, -3.6), 1.3, 0.05, GROUND, ROAD)
    for (x, y) in [(-5.0, 4.6), (-1.8, 4.6), (4.6, 4.4), (-4.7, -0.6)]:
        mb.box(T(x, y), 2.86, 2.06, 0.25, LIGHT, WHITE, z0=0.9)


def town_l4_far(mb, rng, smoke):
    far_pad(mb)
    mb.lathe(T(-2.4, 3.2), [(1.2, 0), (0.6, 13.5), (0.0, 15.5)], 6, SPECIES, WHITE, smooth=False)
    mb.lathe(T(-2.4, 3.2, 8.0), [(0.0, -0.3), (2.0, 0.0), (0.0, 0.3)], 6, TRIM, WHITE, smooth=False)
    for (x, y, w, d, h) in [(4.4, 4.0, 2.6, 1.8, 7.5), (5.0, -2.5, 2.2, 2.2, 5.5), (-4.6, -2.5, 3.0, 1.6, 4.6)]:
        mb.box(T(x, y), w, d, h, GLASS, WHITE)
        for z in (0.35, 0.6):
            mb.box(T(x, y), w + 0.06, d + 0.06, h * 0.06, LIGHT, WHITE, z0=h * z)
    mb.lathe(T(4.0, -7.0), [(1.7, 0), (1.15, 2.9), (1.2, 4.2)], 6, SPECIES, WHITE, cap_top=True, smooth=False)
    smoke.append(Vector((4.0, -7.0, 4.5)))


def town_l5_far(mb, rng, smoke):
    far_pad(mb)
    mb.cyl(T(3.4, -4.6), 3.6, 0.3, 8, TRIM, (0.7, 0.7, 0.72), smooth=False)
    mb.lathe(T(3.4, -4.6, 0.3), [(0.7, 0), (0.6, 8.0), (0.0, 12.0)], 5, SPECIES, WHITE, smooth=False)
    mb.box(T(5.6, -3.6, 0.3), 1.4, 1.4, 9.0, TRIM, (0.75, 0.55, 0.2))
    mb.lathe(T(-3.6, 4.2), [(1.4, 0), (0.85, 11.5), (0.0, 13.2)], 5, SPECIES, WHITE, smooth=False)
    mb.lathe(T(1.6, 4.6), [(1.1, 0), (0.0, 11.0)], 5, TRIM, WHITE, smooth=False)
    mb.box(T(5.4, 3.2), 2.4, 1.6, 6.0, GLASS, WHITE)
    for z in (1.8, 3.4):
        mb.box(T(5.4, 3.2), 2.46, 1.66, 0.35, LIGHT, WHITE, z0=z)
    mb.dome(T(-1.6, -6.4), 1.6, 1.6, 6, GLASS, WHITE, rings=2, smooth=False)
    mb.lathe(T(3.4, -4.6), [(3.62, 0.3), (3.62, 0.36)], 8, LIGHT, (0.6, 0.85, 1.0), cap_top=False, smooth=False)


def town_l6_far(mb, rng, smoke):
    far_pad(mb)
    for (x, y), h, r in [((-1.8, 1.4), 30.0, 2.6), ((3.4, 3.4), 21.0, 2.0), ((-1.0, -4.0), 16.0, 1.8)]:
        mb.lathe(T(x, y), [(r * 1.25, 0), (r * 0.8, h * 0.4), (r * 0.35, h * 0.88), (0.0, h)], 6, SPECIES, WHITE,
                 smooth=False)
        for z0, z1 in ((0.25, 0.28), (0.45, 0.48)):
            mb.lathe(T(x, y), [(r * (1.18 - 0.9 * z0) + 0.04, h * z0), (r * (1.18 - 0.9 * z1) + 0.04, h * z1)], 6, LIGHT,
                     WHITE, cap_top=False, smooth=False)
    for (x, y, h, r) in [(6.0, -3.0, 5.0, 1.9), (-5.8, 4.6, 4.0, 1.7), (-6.0, -3.0, 2.8, 1.6)]:
        mb.cyl(T(x, y), 0.5, h, 4, SPECIES, WHITE, smooth=False)
        mb.cyl(T(x, y, h), r, 0.3, 6, TRIM, WHITE, smooth=False)
    mb.dome(T(2.8, -5.6), 1.9, 1.4, 6, GLASS, WHITE, rings=2, smooth=False)


BUILDERS = {k: globals()[f"town_{k}"] for k in TOWNS + FARS}


# ---------------------------------------------------------------- materials, objects, export


def make_materials():
    def pbsdf(name, colour, metallic=0.0, roughness=0.7, emission=None, strength=0.0):
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
        pbsdf("Species", (0.62, 0.42, 0.3), roughness=0.75),          # default terracotta (Godot sets the colour)
        pbsdf("SpeciesTrim", (0.33, 0.24, 0.2), roughness=0.6),
        pbsdf("CityLight", (0.3, 0.25, 0.18), roughness=0.4, emission=(1.0, 0.72, 0.4), strength=2.0),
        pbsdf("Glass", (0.08, 0.14, 0.18), metallic=0.4, roughness=0.12),
        pbsdf("Ground", (0.34, 0.28, 0.2), roughness=0.95),
    ]


def build_object(name, mats):
    mb = MB(ao=not name.endswith("_far"))
    smoke = []
    rng = random.Random(f"towns-{name}")
    BUILDERS[name](mb, rng, smoke)
    me = bpy.data.meshes.new(f"town_{name}")
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
    obj = bpy.data.objects.new(f"town_{name}", me)
    bpy.context.scene.collection.objects.link(obj)
    empties = []
    for i, p in enumerate(smoke):
        e = bpy.data.objects.new(f"Smoke{i}", None)
        e.empty_display_type = "SPHERE"
        e.empty_display_size = 0.3
        e.parent = obj
        e.location = p
        bpy.context.scene.collection.objects.link(e)
        empties.append(e)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    return obj, empties, tris


def fix_glb_names(path):
    """Blender keeps object names unique ("Smoke0.003"); the game wants plain Smoke0.. under each town."""
    import re
    with open(path, "rb") as f:
        data = f.read()
    magic, version, _ = struct.unpack_from("<III", data, 0)
    jlen, jtype = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + jlen].decode("utf-8"))
    rest = data[20 + jlen:]
    for n in doc.get("nodes", []):
        n["name"] = re.sub(r"^(Smoke\d+)\.\d+$", r"\1", n.get("name", ""))
    js = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)
    out = struct.pack("<III", magic, version, 12 + 8 + len(js) + len(rest)) + struct.pack("<II", len(js), jtype) + js + rest
    with open(path, "wb") as f:
        f.write(out)


def export(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_texcoords=False, export_normals=True, export_tangents=False,
                              export_materials="EXPORT", export_vertex_color="ACTIVE", export_animations=False,
                              export_skins=False, export_morph=False, export_cameras=False, export_lights=False)
    fix_glb_names(path)
    print(f"exported {path} ({os.path.getsize(path) / 1024:.0f} KB)")


# ---------------------------------------------------------------- previews (Cycles)

PREVIEW_COLOURS = {  # species colours for the previews (Godot picks them with NativeTowns.SpeciesColour)
    1: (0.62, 0.40, 0.26), 2: (0.55, 0.60, 0.42), 3: (0.50, 0.46, 0.60), 4: (0.36, 0.56, 0.58),
    5: (0.70, 0.62, 0.44), 6: (0.66, 0.44, 0.52),
}


def trim_of(c):
    import colorsys
    h, s, v = colorsys.rgb_to_hsv(*c)
    if v > 0.55:
        return colorsys.hsv_to_rgb((h + 0.06) % 1, min(1, s * 0.85), v * 0.55)
    return colorsys.hsv_to_rgb((h + 0.06) % 1, s * 0.6, min(1, v * 1.5 + 0.12))


def srgb_to_lin(c):
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def preview_materials(obj, level, night_strength):
    """Per-town copies with the level's preview species colour, and the vertex colour multiplied in."""
    species = srgb_to_lin(PREVIEW_COLOURS[level])
    trim = srgb_to_lin(trim_of(PREVIEW_COLOURS[level]))
    for i, src in enumerate(list(obj.data.materials)):
        m = src.copy()
        m.name = f"{src.name}_{obj.name}"
        nt = m.node_tree
        b = nt.nodes["Principled BSDF"]
        base = {"Species": species, "SpeciesTrim": trim}.get(src.name)
        if base:
            b.inputs["Base Color"].default_value = (*base, 1.0)
        if src.name == "CityLight":
            b.inputs["Emission Strength"].default_value = night_strength
            continue
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Col"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs[6].default_value = b.inputs["Base Color"].default_value
        nt.links.new(attr.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], b.inputs["Base Color"])
        obj.data.materials[i] = m


def preview(objs, folder):
    os.makedirs(folder, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 4
    scene.cycles.diffuse_bounces = 2
    scene.cycles.glossy_bounces = 2
    scene.cycles.transparent_max_bounces = 2
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    scene.world = world
    bg = world.node_tree.nodes["Background"]
    # ground
    gme = bpy.data.meshes.new("Plain")
    gme.from_pydata([(-400, -400, -0.02), (400, -400, -0.02), (400, 400, -0.02), (-400, 400, -0.02)], [], [(0, 1, 2, 3)])
    plain = bpy.data.objects.new("Plain", gme)
    gm = bpy.data.materials.new("PlainMat")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.16, 0.17, 0.1, 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1.0
    gme.materials.append(gm)
    scene.collection.objects.link(plain)
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

    def lighting(night):
        if night:
            sun.data.energy = 0.25
            sun.data.color = (0.55, 0.65, 1.0)
            bg.inputs["Color"].default_value = (0.01, 0.014, 0.03, 1)
            bg.inputs["Strength"].default_value = 1.0
        else:
            sun.data.energy = 3.6
            sun.data.color = (1.0, 0.96, 0.9)
            bg.inputs["Color"].default_value = (0.42, 0.55, 0.8, 1)
            bg.inputs["Strength"].default_value = 0.65
        for o in objs:
            for m in o.data.materials:
                if m.name.startswith("CityLight"):
                    m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 9.0 if night else 0.15

    def bounds(o):
        pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        return lo, hi

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

    # each town alone at the origin (the others hidden), day then night
    for night in (False, True):
        lighting(night)
        when = "night" if night else "day"
        for o in objs:
            name = o.name[5:]
            for other in objs:
                other.hide_render = other is not o
            lo, hi = bounds(o)
            height = hi.z
            dist = max(36.0, 26.0 + height * 1.1)
            tz = min(height * 0.32, 6.0)
            shoot(os.path.join(folder, f"natives-{name}-{when}.png"), (0, 0, tz), dist)
            if name in ("l5_a", "l6_a"):
                shoot(os.path.join(folder, f"natives-{name}-{when}-close.png"), (2.0, -2.0, 3.0), 26.0, yaw=145, elev=22)
    # line-up of every town (rows = levels, columns a / b / far)
    for o in objs:
        o.hide_render = False
        name = o.name[5:]
        lv = int(name[1])
        col = {"a": 0, "b": 1, "far": 2}[name.split("_")[1]]
        o.location = ((col - 1) * 24.0, (lv - 3.5) * 24.0, 0)
    for night in (False, True):
        lighting(night)
        shoot(os.path.join(folder, f"natives-lineup-{'night' if night else 'day'}.png"), (0, 4, 2), 175, yaw=-22,
              elev=38, w=RES * 2, h=int(RES * 2 * 0.75))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = make_materials()
    names = TOWNS + FARS
    if ONLY:
        names = [n for n in names if n in ONLY.split(",")]
    objs, everything = [], []
    for name in names:
        obj, empties, tris = build_object(name, mats)
        objs.append(obj)
        everything += [obj] + empties
        print(f"town_{name}: {tris} tris, {len(empties)} smoke points")
    if "--no-export" not in sys.argv:
        export(everything, OUT)
    if PREVIEW:
        for o in objs:
            name = o.name[5:]
            preview_materials(o, int(name[1]), 9.0)
        preview(objs, PREVIEW)


main()
