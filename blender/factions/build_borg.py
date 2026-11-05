"""Borg ground kit MOCKUPS: the Collective's counterparts to the Federation survey rover, mining crawler, drop ship and
colony core. Look notes in blender/factions/borg.md.

Original designs in the Borg's general style (near-black greebled machinery, exposed pipes, cables and lattice, harsh
green glows and laser sensor lights, cubes and sphere fragments, no decoration, no names). Plain materials, no paint
bake. Uses build_vehicles.py's helpers (box, cylinder, prism, strut, empty, material, ...) by exec'ing its source with
the trailing main() stripped, like render_finish.py does.

  /root/bpyenv/bin/python blender/factions/build_borg.py --model rover --preview blender/previews/factions/borg --res 480 --samples 8
  ... --model miner | dropship [--cargo miner|rover|none] | outpost --level 1|5|10
  ... --model fed_mk1     the Federation Mk I Scout (build_vehicles tier 1) under the same camera, for comparison
  ... --model lineup      Fed Mk I, Borg rover, miner and drop ship side by side (scale check)
  python3 blender/factions/build_borg.py --sheet blender/previews/factions/borg     (PIL only; no bpy needed)
  --shots front34,game (default), also rear34, side, top; --res 480; --samples 8; --seed N (greeble layout)

Writes <preview>/borg-<model>[-l<level>]-<shot>.png (fed_mk1 -> fed-mk1-<shot>.png).

Game conventions kept (so the models could go in the game later): Blender +Y = front, +Z up, world units; material
"Owner" (here a thin emissive glow line) is recoloured per player; "Dish" = the rover's spinning sensor head;
"Beacon" mesh; "TailLamp" material (green on the Borg); decal empties NameL/NameR/NameRear/NumberL/NumberR with the
build_vehicles --blank matrix (text in local XY facing local +Z, Blender scale (max_width, size, 1)): Borg units carry
designations ("3 of 7", "Tertiary Adjunct 12") there, never names. The miner keeps the crawler's animated parts
(Leg0..Leg3, Boom, Drill/String/Bit, Fill); buildings get "Spin" and "Beacon".
"""
import math
import os
import random
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(HERE)

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


MODEL = arg("--model", "rover")
LEVEL = int(arg("--level", "1"))
PREVIEW = arg("--preview")
RES = int(arg("--res", "480"))
SAMPLES = int(arg("--samples", "8"))
SHOTS = [s.strip() for s in arg("--shots", "front34,game").split(",") if s.strip()]
CARGO = arg("--cargo", "miner")
SEED = int(arg("--seed", "3"))
SHEET = arg("--sheet")

MODELS = ("rover", "miner", "dropship", "outpost", "fed_mk1", "lineup")
if not SHEET and MODEL not in MODELS:
    sys.exit(f"--model takes {', '.join(MODELS)}, not {MODEL}")
if LEVEL not in (1, 5, 10):
    sys.exit(f"--level must be 1, 5 or 10, not {LEVEL}")


# ---------------------------------------------------------------- contact sheet (PIL only)


def make_sheet(folder):
    from PIL import Image, ImageDraw, ImageFont
    cells = [
        ("borg-rover-front34.png", "Survey rover: sensor crawler", "camera x1"),
        ("fed-mk1-front34.png", "Federation Mk I Scout (comparison)", "camera x1, same as the Borg rover"),
        ("borg-miner-front34.png", "Mining vehicle: extraction crawler", "camera x1"),
        ("borg-dropship-front34.png", "Drop ship: descent block (miner slung)", "camera x2"),
        ("borg-outpost-l1-front34.png", "Outpost L1: seeded node", "camera x3, one 25-unit hex"),
        ("borg-outpost-l5-front34.png", "Outpost L5: assimilation node", "camera x4.4, one 25-unit hex"),
        ("borg-outpost-l10-front34.png", "Outpost L10: assimilated complex", "camera x10.5, seven hexes"),
        ("borg-lineup-front34.png", "Scale lineup: Fed Mk I, rover, miner, drop ship", "one camera, left to right"),
    ]
    cw, ch, cols = 600, 400, 4
    head, label = 70, 54
    rows = (len(cells) + cols - 1) // cols
    sheet = Image.new("RGB", (cw * cols, head + rows * (ch + label)), (7, 9, 8))
    draw = ImageDraw.Draw(sheet)

    def font(size):
        for f in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
            if os.path.exists(f):
                return ImageFont.truetype(f, size)
        return ImageFont.load_default()

    big, mid, small = font(30), font(18), font(14)
    draw.text((20, 18), "BORG COLLECTIVE  -  ground kit mockups", fill=(95, 224, 122), font=big)
    for i, (fname, title, note) in enumerate(cells):
        x, y = (i % cols) * cw, head + (i // cols) * (ch + label)
        path = os.path.join(folder, fname)
        if os.path.exists(path):
            im = Image.open(path).convert("RGB")
            im.thumbnail((cw - 8, ch - 8))
            sheet.paste(im, (x + (cw - im.width) // 2, y + (ch - im.height) // 2))
        else:
            draw.rectangle((x + 4, y + 4, x + cw - 4, y + ch - 4), outline=(60, 70, 64))
            draw.text((x + 20, y + ch // 2), f"missing {fname}", fill=(200, 80, 80), font=small)
        colour = (240, 200, 90) if fname.startswith("fed") else (226, 235, 228)
        draw.text((x + 12, y + ch + 4), title, fill=colour, font=mid)
        draw.text((x + 12, y + ch + 28), note, fill=(143, 156, 147), font=small)
    out = os.path.join(folder, "borg-sheet.png")
    sheet.save(out)
    print("sheet", out)


if SHEET:
    make_sheet(SHEET)
    sys.exit(0)

import bpy  # noqa: E402  (after the sheet branch, which needs no Blender)
import bmesh  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402


def load_build_vehicles():
    """Exec build_vehicles.py into a namespace without its trailing main() call; its own flag parsing sees no flags."""
    path = os.path.join(BLENDER_DIR, "build_vehicles.py")
    with open(path) as f:
        src = f.read()
    src = re.sub(r"^main\(\)\s*$", "", src, flags=re.M)
    saved = sys.argv
    sys.argv = [path]
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


BV = load_build_vehicles()
material, link, empty, box, cylinder, prism, strut, bevel = (BV[k] for k in (
    "material", "link", "empty", "box", "cylinder", "prism", "strut", "bevel"))


# ---------------------------------------------------------------- materials


def borg_palette():
    def glass(name, colour, strength, alpha):
        m = material(name, colour, 0.0, 0.2, emission=colour, strength=strength)
        m.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value = alpha
        return m

    return {
        "hull": material("BorgHull", (0.028, 0.03, 0.031), 0.75, 0.42),
        "plate": material("BorgPlate", (0.07, 0.075, 0.075), 0.85, 0.36),
        "grey": material("BorgGrey", (0.16, 0.17, 0.17), 0.9, 0.3),
        "pipe": material("BorgPipe", (0.05, 0.055, 0.052), 0.95, 0.24),
        "tread": material("BorgTread", (0.02, 0.02, 0.022), 0.4, 0.65),
        "glow": material("BorgGlow", (0.2, 1.0, 0.3), 0.0, 0.3, emission=(0.25, 1.0, 0.32), strength=9.0),
        "glowdim": material("BorgGlowDim", (0.04, 0.25, 0.07), 0.0, 0.4, emission=(0.2, 1.0, 0.3), strength=2.2),
        "laser": material("SensorLaser", (0.6, 1.0, 0.6), 0.0, 0.2, emission=(0.55, 1.0, 0.5), strength=24.0),
        "fan": glass("ScanFan", (0.3, 1.0, 0.35), 1.2, 0.06),
        "beam": glass("TractorBeam", (0.3, 1.0, 0.4), 2.0, 0.08),
        # Player accent: a thin glow line (recoloured per player in the game; blue in the mockups so it reads apart
        # from the Collective's green).
        "owner": material("Owner", (0.1, 0.35, 1.0), 0.0, 0.3, emission=(0.12, 0.4, 1.0), strength=6.0),
        "beacon": material("BeaconGlow", (0.3, 1.0, 0.35), 0.0, 0.3, emission=(0.35, 1.0, 0.4), strength=14.0),
        "tail": material("TailLamp", (0.1, 0.6, 0.15), 0.0, 0.3, emission=(0.2, 1.0, 0.3), strength=4.0),
        "skin": material("DroneSkin", (0.52, 0.52, 0.5), 0.0, 0.55),
        "native": material("NativeStone", (0.62, 0.57, 0.48), 0.0, 0.85),
        "nativeroof": material("NativeRoof", (0.45, 0.3, 0.2), 0.0, 0.8),
        "window": material("NativeWindow", (0.9, 0.7, 0.4), 0.0, 0.4, emission=(1.0, 0.7, 0.35), strength=1.5),
        "ore": material("OreLoad", (0.3, 0.24, 0.18), 0.0, 0.9),
        "rubble": material("Rubble", (0.22, 0.17, 0.12), 0.0, 0.95),
        "guide": material("HexGuide", (0.85, 0.8, 0.6), 0.0, 0.6, emission=(0.9, 0.85, 0.6), strength=0.5),
    }


# ---------------------------------------------------------------- merged-geometry builder


class Acc:
    """Collects many small primitives into one bmesh per material (a few objects instead of thousands), in the local
    frame of the parent it is flushed to."""

    def __init__(self, P):
        self.P = P
        self.parts = {}  # material name -> (verts, faces, material)

    def _key(self, mat):
        if isinstance(mat, str):
            mat = self.P[mat]
        if mat.name not in self.parts:
            self.parts[mat.name] = ([], [], mat)
        return self.parts[mat.name]

    def add(self, mat, verts, faces, M=None, flip=False):
        """Append a primitive: verts (local), faces as index tuples; M transforms it (a mirroring M flips the winding)."""
        V, F, _ = self._key(mat)
        o = len(V)
        if M is not None:
            verts = [M @ Vector(v) for v in verts]
            flip = flip != (M.to_3x3().determinant() < 0)
        V.extend(tuple(v) for v in verts)
        F.extend(tuple(o + i for i in (reversed(f) if flip else f)) for f in faces)

    def box_m(self, mat, M):
        self.add(mat, CUBE_V, CUBE_F, M)

    def box(self, mat, size, loc, rot=(0, 0, 0)):
        self.box_m(mat, Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1)))

    def cyl_m(self, mat, r1, r2, depth, M, seg=12):
        """A capped cylinder/cone along local Z (centred), transformed by M."""
        ring = [(math.cos(2 * math.pi * k / seg), math.sin(2 * math.pi * k / seg)) for k in range(seg)]
        verts = [(r1 * c, r1 * s_, -depth / 2) for c, s_ in ring] + [(r2 * c, r2 * s_, depth / 2) for c, s_ in ring]
        faces = [(k, (k + 1) % seg, seg + (k + 1) % seg, seg + k) for k in range(seg)]
        faces.append(tuple(reversed(range(seg))))
        faces.append(tuple(range(seg, 2 * seg)))
        self.add(mat, verts, faces, M)

    def cyl(self, mat, r, depth, loc, direction=(0, 0, 1), seg=12, r2=None):
        q = Vector(direction).normalized().to_track_quat("Z", "Y")
        self.cyl_m(mat, r, r if r2 is None else r2, depth, Matrix.Translation(loc) @ q.to_matrix().to_4x4(), seg)

    def bar(self, mat, a, b, r, seg=8):
        a, b = Vector(a), Vector(b)
        if (b - a).length > 1e-6:
            self.cyl(mat, r, (b - a).length, (a + b) / 2, b - a, seg)

    def sphere(self, mat, r, loc, seg=16, squash=1.0, M=None, fn=None):
        """UV sphere; fn(v) (local, before M) can reshape vertices (flatten a side, ...)."""
        vs = max(6, seg // 2)
        verts = [(0, 0, -r)]
        for i in range(1, vs):
            ph = -math.pi / 2 + math.pi * i / vs
            for k in range(seg):
                th = 2 * math.pi * k / seg
                verts.append((r * math.cos(ph) * math.cos(th), r * math.cos(ph) * math.sin(th), r * math.sin(ph) * squash))
        verts.append((0, 0, r * squash))
        if fn:
            verts = [fn(Vector(v)) for v in verts]
        top = len(verts) - 1
        faces = [(0, 1 + (k + 1) % seg, 1 + k) for k in range(seg)]
        for i in range(vs - 2):
            a, b = 1 + i * seg, 1 + (i + 1) * seg
            faces += [(a + k, a + (k + 1) % seg, b + (k + 1) % seg, b + k) for k in range(seg)]
        last = 1 + (vs - 2) * seg
        faces += [(last + k, last + (k + 1) % seg, top) for k in range(seg)]
        self.add(mat, verts, faces, (M if M is not None else Matrix()) @ Matrix.Translation(loc))

    def prism(self, mat, profile, x0, x1):
        """Extrude a (y, z) profile (counter-clockwise seen from +X) between x0 and x1."""
        n = len(profile)
        verts = [(x0, y, z) for y, z in profile] + [(x1, y, z) for y, z in profile]
        faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        faces += [(i, n + i, n + (i + 1) % n, (i + 1) % n) for i in range(n)]
        area = sum(profile[i][0] * profile[(i + 1) % n][1] - profile[(i + 1) % n][0] * profile[i][1] for i in range(n))
        self.add(mat, verts, faces, flip=area < 0)

    def tri(self, mat, pts):
        self.add(mat, pts, [(0, 1, 2)])

    def tube(self, mat, pts, r, seg=10, sub=6, r_end=None, closed=False):
        """A pipe/cable along a Catmull-Rom curve through pts (radius r, tapering to r_end)."""
        pts = [Vector(p) for p in pts]
        if closed:
            pts = pts + pts[:3]
        path = []
        n = len(pts)
        for i in range(n - 1):
            p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, n - 1)]
            for k in range(sub):
                t = k / sub
                t2, t3 = t * t, t * t * t
                path.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
        path.append(pts[-1])
        if closed:
            path = path[sub:len(path) - sub]
        verts, faces = [], []
        normal = (path[1] - path[0]).normalized().orthogonal().normalized()
        for i, p in enumerate(path):
            t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            normal = (normal - t * normal.dot(t)).normalized()  # parallel transport
            binormal = t.cross(normal)
            rr = r if r_end is None else r + (r_end - r) * i / (len(path) - 1)
            verts += [p + (normal * math.cos(2 * math.pi * k / seg) + binormal * math.sin(2 * math.pi * k / seg)) * rr for k in range(seg)]
        for i in range(len(path) - 1):
            a, b = i * seg, (i + 1) * seg
            faces += [(a + k, a + (k + 1) % seg, b + (k + 1) % seg, b + k) for k in range(seg)]
        if not closed:
            faces.append(tuple(reversed(range(seg))))
            faces.append(tuple(range((len(path) - 1) * seg, len(path) * seg)))
        self.add(mat, verts, faces)
        return path

    def merge(self, other, M=Matrix()):
        """Move another Acc's geometry into this one, transformed by M."""
        for key, (V, F, mat) in other.parts.items():
            self.add(mat, V, F, M)
        other.parts = {}

    def flush(self, name, parent, bev=0.0):
        out = []
        for key, (V, F, mat) in self.parts.items():
            me = bpy.data.meshes.new(f"{name}_{key}")
            me.from_pydata(V, [], F)
            me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
            me.set_sharp_from_angle(angle=math.radians(40))
            me.materials.append(mat)
            o = bpy.data.objects.new(f"{name}_{key}", me)
            link(o, parent)
            if bev and key in ("BorgHull", "BorgPlate"):
                bevel(o, bev, 1, 30)
            if key in ("ScanFan", "TractorBeam", "HexGuide"):
                o.visible_shadow = False
            out.append(o)
        self.parts = {}
        return out


def _unit_cube():
    V = [(x, y, z) for x in (-0.5, 0.5) for y in (-0.5, 0.5) for z in (-0.5, 0.5)]
    F = []
    for f in [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]:
        a, b, c = (Vector(V[i]) for i in f[:3])
        centre = sum((Vector(V[i]) for i in f), Vector()) / 4
        F.append(f if (b - a).cross(c - a).dot(centre) > 0 else tuple(reversed(f)))
    return V, F


CUBE_V, CUBE_F = _unit_cube()


# ---------------------------------------------------------------- Borg surface language

AXES = {"x": (0, 1), "-x": (0, -1), "y": (1, 1), "-y": (1, -1), "z": (2, 1), "-z": (2, -1)}


def greeble_box(acc, c, s, rng, sc=1.0, faces=("x", "-x", "y", "-y", "z"), dens=1.0, glow=0.1, pipes=2, grid=False, core=True,
                core_mat="hull"):
    """An axis-aligned box (centre c, size s) covered in Borg machinery: layered plates, raised blocks, green slits,
    pipe runs on brackets and (grid=True) a dashed green grid. sc = the size of a typical greeble."""
    c, s = Vector(c), Vector(s)
    if core:
        acc.box(core_mat, s, c)
    for f in faces:
        a, sign = AXES[f]
        u, v = [i for i in range(3) if i != a]
        su, sv = s[u], s[v]
        fc = c.copy()
        fc[a] += sign * s[a] / 2

        def put(mat, w, h, d, pu, pv, lift=0.0):
            size = [0, 0, 0]
            size[a], size[u], size[v] = d, w, h
            loc = fc.copy()
            loc[a] += sign * (lift + d / 2)
            loc[u] += pu
            loc[v] += pv
            acc.box(mat, size, loc)

        # Big shallow panels first, then the small machinery on and around them.
        for _ in range(min(int(dens * su * sv / (2.6 * sc) ** 2 * 1.5), 60)):
            w = min(rng.uniform(1.0, 2.8) * sc, su * 0.95)
            h = min(rng.uniform(1.0, 2.8) * sc, sv * 0.95)
            put(rng.choice(("plate", "hull", "plate")), w, h, rng.uniform(0.03, 0.09) * sc,
                rng.uniform(-su / 2 + w / 2, su / 2 - w / 2), rng.uniform(-sv / 2 + h / 2, sv / 2 - h / 2))
        n = min(int(dens * su * sv / (sc * sc) * 1.1), 400)
        for _ in range(n):
            w = min(rng.uniform(0.12, 0.6) * sc, su * 0.9)
            h = min(rng.uniform(0.12, 0.6) * sc, sv * 0.9)
            pu = rng.uniform(-su / 2 + w / 2, su / 2 - w / 2)
            pv = rng.uniform(-sv / 2 + h / 2, sv / 2 - h / 2)
            roll = rng.random()
            if roll < glow:
                if rng.random() < 0.5:
                    w2, h2 = min(rng.uniform(0.3, 1.1) * sc, su * 0.9), 0.035 * sc
                else:
                    w2, h2 = 0.035 * sc, min(rng.uniform(0.3, 1.1) * sc, sv * 0.9)
                pu = rng.uniform(-su / 2 + w2 / 2, su / 2 - w2 / 2)
                pv = rng.uniform(-sv / 2 + h2 / 2, sv / 2 - h2 / 2)
                put("glow" if rng.random() < 0.35 else "glowdim", w2, h2, 0.012 * sc, pu, pv)
                continue
            d = rng.choice((0.03, 0.05, 0.08, 0.12, 0.2)) * sc
            put(rng.choice(("plate", "plate", "hull", "grey")), w, h, d, pu, pv)
            if rng.random() < 0.35:  # a smaller block stacked on top
                put(rng.choice(("plate", "grey", "hull")), w * 0.55, h * 0.55, d * 0.8, pu + rng.uniform(-w, w) * 0.2,
                    pv + rng.uniform(-h, h) * 0.2, lift=d)
        if grid:  # a sparse dashed grid of thin green lines, standing just proud of the machinery
            g = 3.0 * sc
            dash = 1.2 * sc
            for k in range(1, int(su / g) + 1):
                pu = -su / 2 + k * g
                if abs(pu) < su / 2 - 0.2 * sc:
                    for j in range(int(sv / dash)):
                        if rng.random() < 0.4:
                            put("glowdim", 0.022 * sc, dash * 0.85, 0.02 * sc, pu, -sv / 2 + (j + 0.5) * dash, lift=0.2 * sc)
            for k in range(1, int(sv / g) + 1):
                pv = -sv / 2 + k * g
                if abs(pv) < sv / 2 - 0.2 * sc:
                    for j in range(int(su / dash)):
                        if rng.random() < 0.4:
                            put("glowdim", dash * 0.85, 0.022 * sc, 0.02 * sc, -su / 2 + (j + 0.5) * dash, pv, lift=0.2 * sc)
        for _ in range(pipes):
            along = rng.choice((u, v))
            across = v if along == u else u
            length = s[along] * rng.uniform(0.5, 0.92)
            r = rng.uniform(0.025, 0.07) * sc * 2
            off = rng.uniform(0.12, 0.25) * sc + r
            pa = rng.uniform(-s[along] / 2 + length / 2, s[along] / 2 - length / 2)
            pc = rng.uniform(-s[across] / 2 + r * 2, s[across] / 2 - r * 2)
            p0, p1 = fc.copy(), fc.copy()
            for p in (p0, p1):
                p[a] += sign * off
                p[across] += pc
            p0[along] += pa - length / 2
            p1[along] += pa + length / 2
            acc.bar("pipe", p0, p1, r, 10)
            for k in range(max(2, int(length / sc))):
                q = p0.lerp(p1, (k + 0.5) / max(2, int(length / sc)))
                size = [0, 0, 0]
                size[a], size[along], size[across] = off, r * 1.6, r * 2.4
                q[a] -= sign * off / 2
                acc.box("grey", size, q)


def lattice(acc, c, w, d, h, rng, levels=4, r=0.05, mat="plate", nodes=True):
    """A square girder tower: corner posts, rings and alternating diagonals, glowing nodes at some joints."""
    c = Vector(c)
    xs, ys = (-w / 2, w / 2), (-d / 2, d / 2)
    corners = [Vector((x, y, 0)) for x, y in ((xs[0], ys[0]), (xs[1], ys[0]), (xs[1], ys[1]), (xs[0], ys[1]))]
    for p in corners:
        acc.bar(mat, c + p, c + p + Vector((0, 0, h)), r)
    for k in range(levels + 1):
        z = h * k / levels
        for i in range(4):
            a, b = corners[i] + Vector((0, 0, z)), corners[(i + 1) % 4] + Vector((0, 0, z))
            acc.bar(mat, c + a, c + b, r * 0.8)
            if k < levels:
                z2 = h * (k + 1) / levels
                if (i + k) % 2:
                    acc.bar(mat, c + a, c + corners[(i + 1) % 4] + Vector((0, 0, z2)), r * 0.6)
                else:
                    acc.bar(mat, c + b, c + corners[i] + Vector((0, 0, z2)), r * 0.6)
            if nodes and rng.random() < 0.3:
                acc.box("glow", (r * 2.2,) * 3, c + a)


def cube_frame(acc, c, s, r, rng, mat="plate"):
    """The edges of a cube as girders (assembly-matrix style), with glowing joints."""
    c = Vector(c)
    h = s / 2
    pts = [Vector((x, y, z)) for x in (-h, h) for y in (-h, h) for z in (-h, h)]
    for i, p in enumerate(pts):
        for q in pts[i + 1:]:
            if (p - q).length - s < 1e-6:
                acc.bar(mat, c + p, c + q, r)
        acc.box("glow" if rng.random() < 0.6 else "grey", (r * 2.6,) * 3, c + p)


def drone_figure(P, parent, loc, facing=0.0, name="Drone", height=1.78):
    """A drone standing upright, facing +Y rotated by `facing` (radians about Z): black armour, a tube or two into the
    head, pale skin, an eyepiece with a laser eye. Generic, not any particular character."""
    d = empty(name, loc, parent)
    d.rotation_euler = (0, 0, facing)
    k = height / 1.78
    d.scale = (k, k, k)
    a = Acc(P)
    for s in (-1, 1):
        a.bar("hull", (s * 0.1, 0, 0.08), (s * 0.11, 0, 0.9), 0.065)
        a.box("plate", (0.13, 0.11, 0.14), (s * 0.105, 0.03, 0.5))  # knee plates
        a.box("hull", (0.13, 0.24, 0.09), (s * 0.1, 0.03, 0.045))  # boots
        a.box("plate", (0.13, 0.16, 0.12), (s * 0.25, 0, 1.48))  # shoulders
        a.bar("hull", (s * 0.25, 0, 1.44), (s * 0.27, 0.04, 1.08), 0.05)
        a.bar("hull", (s * 0.27, 0.04, 1.08), (s * 0.25, 0.12, 0.86), 0.042)
    a.box("grey", (0.07, 0.16, 0.06), (0.25, 0.2, 0.84))  # tool on the right forearm
    a.box("hull", (0.32, 0.2, 0.2), (0, 0, 0.98))
    a.box("hull", (0.4, 0.22, 0.5), (0, 0, 1.28))
    a.box("plate", (0.3, 0.05, 0.3), (0, 0.12, 1.32))
    a.box("glowdim", (0.04, 0.02, 0.1), (0.08, 0.15, 1.36))
    a.bar("pipe", (-0.12, 0.09, 1.42), (-0.06, 0.06, 1.62), 0.018)
    a.bar("pipe", (0.1, -0.06, 1.48), (0.04, -0.08, 1.66), 0.018)
    a.cyl("plate", 0.06, 0.12, (0, 0, 1.56))
    a.sphere("skin", 0.11, (0, 0.01, 1.68), 14, squash=1.15)
    a.box("hull", (0.17, 0.2, 0.12), (0.03, -0.03, 1.74))  # skull plating
    a.box("grey", (0.06, 0.06, 0.05), (0.055, 0.105, 1.69))  # eyepiece
    a.sphere("laser", 0.016, (0.06, 0.14, 1.69), 8)
    a.flush(name, d)
    return d


def marker(name, loc, side, width, size, parent):
    """Decal spot (build_vehicles --blank contract): side +1/-1 = +X/-X face, 0 = rear (-Y). Borg units hold a
    designation here ("3 of 7"), not a name."""
    rot = (math.radians(90), 0, math.radians(90 if side > 0 else -90)) if side else (math.radians(90), 0, 0)
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.5
    e.matrix_basis = Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((width, size, 1.0, 1.0))
    return link(e, parent)


def track_unit(acc, profile, x0, x1, sc, outer):
    """An angular track pod: dark core, segmented tread plates around the profile, road wheels with green hubs on the
    outer face (outer = +1/-1)."""
    acc.prism("hull", profile, x0, x1)
    n = len(profile)
    w = x1 - x0
    xc = (x0 + x1) / 2
    for i in range(n):
        (y0, z0), (y1, z1) = profile[i], profile[(i + 1) % n]
        L = math.hypot(y1 - y0, z1 - z0)
        theta = math.atan2(z1 - z0, y1 - y0)
        ny, nz = (z1 - z0) / L, -(y1 - y0) / L
        k = max(1, int(L / (0.17 * sc)))
        for j in range(k):
            t = (j + 0.5) / k
            acc.box("tread", (w * 1.04, L / k * 0.78, 0.06 * sc), (xc, y0 + (y1 - y0) * t + ny * 0.03 * sc, z0 + (z1 - z0) * t + nz * 0.03 * sc),
                    (theta, 0, 0))
    ys = [p[0] for p in profile]
    zs = [p[1] for p in profile]
    ymin, ymax, zmax = min(ys), max(ys), max(zs)
    xo = x1 if outer > 0 else x0
    nw = max(2, int((ymax - ymin) / (0.55 * sc)))
    r = zmax * 0.26
    for j in range(nw):
        y = ymin + 0.25 * sc + (ymax - ymin - 0.5 * sc) * j / (nw - 1)
        acc.cyl("plate", r, 0.06, (xo + outer * 0.03, y, zmax * 0.42), (1, 0, 0), 14)
        acc.cyl("grey", r * 0.45, 0.05, (xo + outer * 0.07, y, zmax * 0.42), (1, 0, 0), 10)
        if j % 2 == 0:
            acc.cyl("glowdim", r * 0.16, 0.03, (xo + outer * 0.1, y, zmax * 0.42), (1, 0, 0), 8)
    acc.box("grey", (0.04, (ymax - ymin) * 0.7, 0.05 * sc), (xo + outer * 0.035, (ymin + ymax) / 2, zmax * 0.78))


# ---------------------------------------------------------------- (1) survey rover: sensor crawler


def borg_rover(P, seed=SEED, parent=None, loc=(0, 0, 0)):
    """Sensor crawler: a low slab chassis on four angular track pods, a greebled sensor housing with a corner-standing
    cube sensor head ("Dish") that sweeps a green laser fan over the ground, a caged power cell across the deck, and a
    drone riding in an alcove at the back. About 4.4 x 2.5 x 2.8 (head)."""
    rng = random.Random(seed)
    root = empty("SurveyRover", loc, parent)
    a = Acc(P)
    prof = [(-0.55, 0.0), (0.55, 0.0), (0.8, 0.3), (0.62, 0.72), (-0.62, 0.72), (-0.8, 0.3)]
    for i, (x, y) in enumerate([(-0.98, 1.3), (0.98, 1.3), (-0.98, -1.3), (0.98, -1.3)]):
        pod = empty(f"Track{i}", (x, y, 0), root)
        pa = Acc(P)
        track_unit(pa, prof, -0.25, 0.25, 1.0, 1 if x > 0 else -1)
        pa.flush(f"Track{i}", pod)
        s = 1 if x > 0 else -1
        # Knuckle between keel and pod, a pair of shock struts from the deck to the pod top.
        a.box("plate", (0.26, 0.42, 0.3), (s * 0.7, y, 0.46))
        a.box("grey", (0.1, 0.2, 0.2), (s * 0.84, y, 0.46))
        for e in (-0.3, 0.3):
            a.bar("grey", (s * 0.92, y + e, 0.73), (s * 0.95, y + e * 0.8, 0.97), 0.04)
            a.cyl("pipe", 0.055, 0.12, (s * 0.93, y + e * 0.9, 0.83), (0, 0, 1), 10)
    # Keel and deck.
    greeble_box(a, (0, -0.05, 0.7), (1.4, 3.8, 0.55), rng, 0.32, faces=("x", "-x", "y"), dens=0.6, glow=0.08, pipes=1)
    greeble_box(a, (0, -0.05, 1.15), (2.24, 3.8, 0.4), rng, 0.3, faces=("x", "-x", "y"), dens=0.8, glow=0.1, pipes=1)
    greeble_box(a, (0, -0.05, 1.15), (2.24, 3.8, 0.4), rng, 0.3, faces=("z",), dens=0.5, glow=0.12, pipes=0, core=False)
    # Front sensor prow with a laser slit and two emitter eyes.
    a.prism("hull", [(1.85, 0.5), (2.12, 0.6), (2.24, 0.95), (2.05, 1.3), (1.85, 1.35)], -0.75, 0.75)
    a.box("laser", (1.1, 0.04, 0.035), (0, 2.205, 0.86), (math.radians(-70), 0, 0))
    for s in (-1, 1):
        a.cyl("grey", 0.09, 0.06, (s * 0.5, 2.2, 1.06), (0, 1, 0.35), 14)
        a.cyl("laser", 0.04, 0.03, (s * 0.5, 2.235, 1.075), (0, 1, 0.35), 10)
        a.box("plate", (0.12, 0.3, 0.2), (s * 0.82, 1.98, 0.86))
    # Sensor housing, its front laser slit, mast lattice.
    greeble_box(a, (0, 1.1, 1.56), (1.5, 1.2, 0.42), rng, 0.22, faces=("x", "-x", "y", "-y", "z"), dens=0.8, glow=0.12, pipes=1)
    a.box("laser", (1.2, 0.02, 0.03), (0, 1.715, 1.62))
    for x in (-0.13, 0.13):
        for y in (0.87, 1.13):
            a.bar("plate", (x, y, 1.77), (x * 0.6, 1.0 + (y - 1.0) * 0.6, 2.06), 0.025)
    a.box("grey", (0.3, 0.3, 0.04), (0, 1.0, 1.8))
    # Power cell across the deck, in a cage, piped forward to the housing.
    a.cyl("glow", 0.15, 1.3, (0, -0.55, 1.56), (1, 0, 0), 16)
    for s in (-1, 1):
        a.cyl("plate", 0.2, 0.08, (s * 0.67, -0.55, 1.56), (1, 0, 0), 16)
        a.box("plate", (0.12, 0.3, 0.22), (s * 0.67, -0.55, 1.44))
        for y in (-0.73, -0.37):
            for z in (1.38, 1.74):
                a.bar("plate", (-0.7, y, z), (0.7, y, z), 0.022)
        a.tube("pipe", [(s * 0.72, -0.4, 1.56), (s * 0.85, -0.1, 1.45), (s * 0.85, 0.3, 1.43), (s * 0.7, 0.52, 1.5)], 0.045)
        a.tube("pipe", [(s * 0.55, -0.4, 1.4), (s * 0.6, 0.0, 1.38), (s * 0.45, 0.52, 1.42)], 0.03)
        # Loose cable loops from the deck edge down to the rear pods.
        a.tube("pipe", [(s * 1.05, -0.9, 1.0), (s * 1.1, -1.05, 0.84), (s * 1.08, -1.25, 0.78)], 0.025)
    # Rear alcove: casing on the deck, side posts, hood, step plate, the drone riding in it facing back.
    greeble_box(a, (0, -1.72, 1.9), (1.2, 0.46, 1.1), rng, 0.2, faces=("x", "-x", "y", "z"), dens=0.8, glow=0.12, pipes=1)
    a.box("hull", (0.86, 0.06, 1.9), (0, -1.98, 1.43))
    a.box("glowdim", (0.6, 0.02, 1.5), (0, -2.015, 1.45))
    for s in (-1, 1):
        a.box("plate", (0.12, 0.38, 2.0), (s * 0.5, -2.14, 1.45))
        a.box("glow", (0.02, 0.02, 1.6), (s * 0.44, -2.33, 1.45))
        a.tube("pipe", [(s * 0.3, -1.99, 2.1), (s * 0.24, -2.06, 1.95), (s * 0.13, -2.12, 1.86)], 0.02)
    a.box("plate", (1.12, 0.46, 0.16), (0, -2.12, 2.52))
    a.box("plate", (0.9, 0.42, 0.08), (0, -2.12, 0.5))
    drone_figure(P, root, (0, -2.12, 0.54), facing=math.pi, name="Drone", height=1.7)
    a.flush("Rover", root, bev=0.012)

    # Beacon on a post at the back-left; rear lamp strips ("TailLamp").
    post = Acc(P)
    post.bar("plate", (-0.88, -1.25, 1.35), (-0.88, -1.25, 1.72), 0.03)
    post.box("tail", (0.3, 0.025, 0.05), (-0.82, -1.965, 1.15))
    post.box("tail", (0.3, 0.025, 0.05), (0.82, -1.965, 1.15))
    post.flush("RoverLamps", root)
    box("Beacon", (0.12, 0.12, 0.12), (-0.88, -1.25, 1.8), P["beacon"], root, 0.0, rot=(math.radians(45), math.radians(35.26), 0))
    # Owner: a thin glow line along both deck edges.
    o = Acc(P)
    for s in (-1, 1):
        o.box("owner", (0.02, 3.5, 0.03), (s * 1.125, -0.05, 1.3))
    o.flush("OwnerLine", root)

    # Sensor head ("Dish", spins): a cube standing on its corner, two laser arms, a green scan fan to the ground.
    dish = empty("Dish", (0, 1.0, 2.06), root)
    h = Acc(P)
    h.cyl("plate", 0.16, 0.08, (0, 0, 0.04), (0, 0, 1), 16)
    h.cyl("glowdim", 0.1, 0.1, (0, 0, 0.1), (0, 0, 1), 12)
    cube = Matrix.Translation((0, 0, 0.42)) @ Euler((math.radians(45), math.radians(35.26), 0)).to_matrix().to_4x4()
    h.box_m("hull", cube @ Matrix.Diagonal((0.36, 0.36, 0.36, 1)))
    for axis in range(3):
        for sgn in (-1, 1):
            off = [0, 0, 0]
            off[axis] = sgn * 0.185
            size = [0.26, 0.26, 0.26]
            size[axis] = 0.02
            h.box_m(rng.choice(("plate", "grey")), cube @ Matrix.Translation(off) @ Matrix.Diagonal((*size, 1)))
            gl = [0.2, 0.2, 0.2]
            gl[axis] = 0.03
            gl[(axis + 1) % 3] = 0.02
            h.box_m("glowdim", cube @ Matrix.Translation(off) @ Matrix.Diagonal((*gl, 1)))
    for s in (-1, 1):
        h.bar("plate", (s * 0.14, 0, 0.42), (s * 0.36, 0.05, 0.42), 0.03)
        h.cyl("grey", 0.06, 0.1, (s * 0.38, 0.08, 0.42), (0, 1, -0.3), 12)
        h.cyl("laser", 0.028, 0.03, (s * 0.38, 0.13, 0.405), (0, 1, -0.3), 8)
    # Scan fan: from the head's eye to a line on the ground ahead (local z -2.06 = ground).
    eye = Vector((0, 0.24, 0.4))
    h.box("grey", (0.1, 0.1, 0.08), (0, 0.2, 0.4))
    h.sphere("laser", 0.035, eye, 8)
    left, right = Vector((-0.75, 2.7, -2.04)), Vector((0.75, 2.7, -2.04))
    h.tri("fan", (eye, right, left))
    h.bar("laser", eye, left, 0.006, 6)
    h.bar("laser", eye, right, 0.006, 6)
    h.bar("laser", left, right, 0.012, 6)
    h.flush("Dish", dish)

    # Designation decals (sides of the deck, sides of the housing, rear hood).
    for s, side in ((-1, "L"), (1, "R")):
        marker(f"Name{side}", (s * 1.135, -0.75, 1.12), s, 1.1, 0.16, root)
        marker(f"Number{side}", (s * 0.765, 1.1, 1.52), s, 0.45, 0.18, root)
    marker("NameRear", (0, -2.355, 2.52), 0, 0.9, 0.1, root)
    return root


# ---------------------------------------------------------------- (2) mining vehicle: extraction crawler


def borg_miner(P, seed=SEED + 1, parent=None, loc=(0, 0, 0), fill=0.6):
    """Extraction crawler: a heavy slab hull on two long angular tracks, a power turret carrying the boom ("Boom")
    with a hanging extraction head ("Drill" > "String" cable bundle + "Bit": four clawed tendrils round a green core),
    an open lattice hopper at the back ("Fill" = ore load), four claw legs folded up along the sides ("Leg0".."Leg3")
    and a green-lit intake maw at the front. About 5.6 x 2.8 x 3.1."""
    rng = random.Random(seed)
    root = empty("MiningCrawler", loc, parent)
    a = Acc(P)
    prof = [(-2.4, 0.0), (2.4, 0.0), (2.8, 0.45), (2.55, 1.05), (-2.55, 1.05), (-2.8, 0.45)]
    for s in (-1, 1):
        t = empty(f"Track{'L' if s < 0 else 'R'}", (s * 1.0, 0, 0), root)
        ta = Acc(P)
        track_unit(ta, prof, -0.34, 0.34, 1.15, s)
        ta.flush(t.name, t)
    greeble_box(a, (0, 0, 0.8), (1.32, 4.6, 0.7), rng, 0.3, faces=("y", "-y"), dens=0.6, glow=0.08, pipes=1)
    # Main deck, slightly overhanging the tracks.
    greeble_box(a, (0, -0.15, 1.33), (2.44, 4.7, 0.46), rng, 0.3, faces=("x", "-x", "-y"), dens=0.9, glow=0.1, pipes=1)
    greeble_box(a, (0, 0.9, 1.33), (2.44, 2.5, 0.46), rng, 0.25, faces=("z",), dens=0.6, glow=0.1, pipes=1, core=False)
    # Front intake maw: an angled plate block with a glowing grate.
    a.prism("hull", [(2.2, 1.1), (2.72, 1.18), (2.62, 1.5), (2.2, 1.62)], -1.15, 1.15)
    for k in range(6):
        z = 1.22 + k * 0.05
        a.box("glow" if k % 2 else "glowdim", (1.5, 0.03, 0.022), (0, 2.7 - k * 0.016, z), (math.radians(-72), 0, 0))
    for s in (-1, 1):
        a.box("plate", (0.3, 0.45, 0.34), (s * 0.95, 2.45, 1.32))
        a.cyl("laser", 0.035, 0.03, (s * 0.95, 2.69, 1.36), (0, 1, 0), 10)
    # Power turret under the boom pivot.
    greeble_box(a, (0, -0.15, 2.0), (1.7, 1.5, 0.9), rng, 0.24, faces=("x", "-x", "y", "-y", "z"), dens=0.8, glow=0.12, pipes=1)
    for s in (-1, 1):
        a.cyl("glow", 0.11, 0.9, (s * 0.6, 0.88, 1.78), (0, 0, 1), 14)  # power cells in front of the turret
        for z in (1.4, 2.16):
            a.cyl("plate", 0.15, 0.06, (s * 0.6, 0.88, z), (0, 0, 1), 14)
        for q in (0, 1, 2):
            ang = q * 2 * math.pi / 3
            a.bar("plate", (s * 0.6 + 0.14 * math.cos(ang), 0.88 + 0.14 * math.sin(ang), 1.4),
                  (s * 0.6 + 0.14 * math.cos(ang), 0.88 + 0.14 * math.sin(ang), 2.16), 0.018)
        a.tube("pipe", [(s * 0.6, 0.88, 2.2), (s * 0.55, 0.7, 2.4), (s * 0.4, 0.45, 2.5), (s * 0.3, 0.2, 2.5)], 0.04)
        a.tube("pipe", [(s * 0.86, -0.6, 1.9), (s * 1.12, -0.8, 1.62), (s * 1.12, -1.6, 1.6), (s * 0.95, -2.3, 1.6)], 0.05)
        a.tube("pipe", [(s * 0.86, 0.3, 1.65), (s * 1.05, 0.9, 1.6), (s * 1.0, 1.8, 1.6), (s * 0.85, 2.25, 1.55)], 0.04)
    a.cyl("plate", 0.32, 0.18, (0, -0.15, 2.54), (0, 0, 1), 18)
    for s in (-1, 1):
        a.box("plate", (0.12, 0.5, 0.36), (s * 0.3, -0.15, 2.66))
    # Hopper: an open lattice cage on a glowing rim.
    hy, hl = -1.72, 1.45
    a.box("hull", (2.2, hl, 0.08), (0, hy, 1.6))
    a.box("glowdim", (2.24, hl + 0.04, 0.03), (0, hy, 1.66))
    corners = [(-1.05, hy - hl / 2 + 0.05), (1.05, hy - hl / 2 + 0.05), (1.05, hy + hl / 2 - 0.05), (-1.05, hy + hl / 2 - 0.05)]
    for x, y in corners:
        a.box("plate", (0.09, 0.09, 1.1), (x, y, 2.15))
    for z in (2.1, 2.66):
        for i in range(4):
            (x0, y0), (x1, y1) = corners[i], corners[(i + 1) % 4]
            a.bar("plate", (x0, y0, z), (x1, y1, z), 0.035)
    for i in range(4):
        (x0, y0), (x1, y1) = corners[i], corners[(i + 1) % 4]
        a.bar("plate", (x0, y0, 1.64), (x1, y1, 2.66), 0.022)
        for k in range(1, 4):
            t = k / 4
            a.bar("plate", (x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, 1.64), (x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, 2.66), 0.018)
    a.flush("Miner", root, bev=0.012)

    fillo = empty("Fill", (0, hy, 1.64), root)
    fillo.scale = (1, 1, fill)
    f = Acc(P)
    f.box("ore", (2.0, hl - 0.15, 1.0), (0, 0, 0.5))
    for _ in range(26):
        sz = rng.uniform(0.12, 0.3)
        f.box("ore", (sz, sz * rng.uniform(0.7, 1.3), sz * 0.7), (rng.uniform(-0.85, 0.85), rng.uniform(-0.55, 0.55), 1.0),
              (rng.uniform(0, 1), rng.uniform(0, 1), rng.uniform(0, 3)))
    f.flush("Fill", fillo)

    # Lamps, beacon, owner line.
    lamps = Acc(P)
    for s in (-1, 1):
        lamps.box("tail", (0.35, 0.025, 0.06), (s * 0.85, -2.515, 1.36))
        lamps.box("owner", (0.02, 4.4, 0.03), (s * 1.225, -0.1, 1.52))
    lamps.bar("plate", (0.7, -0.75, 2.45), (0.7, -0.75, 2.78), 0.03)
    lamps.flush("MinerLamps", root)
    box("Beacon", (0.13, 0.13, 0.13), (0.7, -0.75, 2.86), P["beacon"], root, 0.0, rot=(math.radians(45), math.radians(35.26), 0))

    # Claw legs folded up along the sides (pivot at the deck edge, leg reaching along local +X, rotated up).
    for i, (x, y) in enumerate([(1.32, 1.75), (1.32, -1.0), (-1.32, 1.75), (-1.32, -1.0)]):
        leg = empty(f"Leg{i}", (x, y, 1.36), root)
        leg.rotation_euler = (0, math.radians(-86), 0 if x > 0 else math.pi)
        la = Acc(P)
        la.cyl("grey", 0.11, 0.2, (0, 0, 0), (0, 1, 0), 12)
        la.box("plate", (1.05, 0.16, 0.12), (0.55, 0, 0))
        la.box("hull", (0.8, 0.1, 0.16), (0.5, 0, 0.0))
        la.box("glowdim", (0.6, 0.02, 0.02), (0.5, 0.085, 0.0))
        for t in (-1, 0, 1):
            la.bar("grey", (1.05, 0, 0), (1.22, t * 0.12, -0.08 if t == 0 else 0.0), 0.03)
        la.flush(f"Leg{i}", leg)

    # Boom ("Boom") reaching forward over the deck, extraction head ("Drill") at its tip.
    pivot = Vector((0, -0.15, 2.72))
    boom = empty("Boom", pivot, root)
    reach = 2.75
    b = Acc(P)
    b.cyl("grey", 0.16, 0.46, (0, 0, 0), (1, 0, 0), 16)
    for x in (-0.16, 0.16):
        for z in (-0.12, 0.12):
            b.bar("plate", (x, 0, z), (x * 0.6, reach, z * 0.7), 0.035)
    for k in range(1, 7):
        y = reach * k / 7
        sh = 1 - 0.4 * y / reach
        b.bar("plate", (-0.16 * sh, y, 0.12 * sh), (0.16 * sh, y, -0.12 * sh), 0.02)
        b.bar("plate", (-0.16 * sh, y, -0.12 * sh), (0.16 * sh, y, 0.12 * sh), 0.02)
        if k % 2:
            b.box("glow", (0.05, 0.05, 0.05), (0.16 * sh, y, 0.12 * sh))
    b.tube("pipe", [(0, 0.2, 0.2), (0, 1.3, 0.24), (0, reach - 0.2, 0.16)], 0.04)
    b.box("owner", (0.02, reach - 0.6, 0.02), (0, reach / 2, 0.16))
    b.box("hull", (0.44, 0.44, 0.3), (0, reach, 0))
    b.flush("Boom", boom)
    drill = empty("Drill", (0, reach, -0.15), boom)
    string = empty("String", (0, 0, 0), drill)
    string.scale = (1, 1, 0.18)
    sa = Acc(P)
    for x, y in ((-0.06, 0), (0.06, 0), (0, 0.06), (0, -0.06)):
        sa.bar("pipe", (x, y, 0), (x, y, -1.0), 0.03)
    sa.flush("String", string)
    bit = empty("Bit", (0, 0, -0.18), drill)
    ba = Acc(P)
    ba.cyl("plate", 0.2, 0.1, (0, 0, 0), (0, 0, 1), 16)
    ba.sphere("glow", 0.12, (0, 0, -0.14), 14)
    for q in range(4):
        ang = q * math.pi / 2 + math.pi / 4
        c, s_ = math.cos(ang), math.sin(ang)
        ba.tube("hull", [(0.12 * c, 0.12 * s_, -0.02), (0.3 * c, 0.3 * s_, -0.12), (0.28 * c, 0.28 * s_, -0.32), (0.12 * c, 0.12 * s_, -0.46)],
                0.05, r_end=0.018)
        ba.box("grey", (0.08, 0.08, 0.05), (0.24 * c, 0.24 * s_, -0.08), (0, 0, ang))
    ba.flush("Bit", bit)

    for s, side in ((-1, "L"), (1, "R")):
        marker(f"Name{side}", (s * 1.225, 0.9, 1.3), s, 1.4, 0.18, root)
        marker(f"Number{side}", (s * 0.855, -0.15, 2.0), s, 0.6, 0.22, root)
    marker("NameRear", (0, -2.525, 1.1), 0, 1.0, 0.12, root)
    return root


# ---------------------------------------------------------------- (3) drop ship: descent block


def borg_dropship(P, seed=SEED + 2, cargo=CARGO, parent=None, loc=(0, 0, 0)):
    """Descent block: a stacked, greebled cuboid (11.6 long) on four lattice landing legs, the carried unit slung
    underneath in four clamp arms ("Clamp0".."Clamp3", unit at "Cargo"), green lift emitters ("Thruster0".."3") at the
    belly corners, a sphere-fragment tractor emitter set into the nose, an exposed lattice corner at the back."""
    rng = random.Random(seed)
    root = empty("DropShip", loc, parent)
    a = Acc(P)
    zb, zt = 3.5, 6.4
    L, W = 11.6, 7.4
    # Main block: built from slabs so a rear corner can be left open as bare lattice.
    greeble_box(a, (0, 0.9, (zb + zt) / 2), (W, L - 1.8, zt - zb), rng, 0.55, faces=("x", "-x", "y", "z"), dens=0.9, glow=0.1, pipes=3, grid=True)
    greeble_box(a, (-1.3, -L / 2 + 0.9, (zb + zt) / 2), (W - 2.6, 1.8, zt - zb), rng, 0.55, faces=("x", "-y", "-x", "z"), dens=0.9, glow=0.1,
                pipes=1, grid=True)
    # The open corner (+X, rear): girders inside, a glowing conduit through it.
    cx, cy = W / 2 - 1.3, -L / 2 + 0.9
    for x in (cx - 1.2, cx + 1.2):
        for y in (cy - 0.85, cy + 0.85):
            a.bar("plate", (x, y, zb), (x, y, zt), 0.08)
    for z in (zb + 0.05, zb + 1.45, zt - 0.05):
        for (x0, y0), (x1, y1) in (((cx - 1.2, cy - 0.85), (cx + 1.2, cy - 0.85)), ((cx + 1.2, cy - 0.85), (cx + 1.2, cy + 0.85)),
                                   ((cx - 1.2, cy - 0.85), (cx - 1.2, cy + 0.85))):
            a.bar("plate", (x0, y0, z), (x1, y1, z), 0.06)
    a.bar("plate", (cx + 1.2, cy - 0.85, zb), (cx + 1.2, cy + 0.85, zt), 0.05)
    a.bar("plate", (cx - 1.2, cy - 0.85, zb), (cx + 1.2, cy - 0.85, zt), 0.05)
    a.cyl("glow", 0.22, zt - zb - 0.2, (cx, cy, (zb + zt) / 2), (0, 0, 1), 16)
    for z in (zb + 0.6, zb + 1.5, zb + 2.4):
        a.cyl("plate", 0.34, 0.12, (cx, cy, z), (0, 0, 1), 16)
    a.tube("pipe", [(cx, cy, zt - 0.4), (cx - 0.7, cy + 0.3, zt - 0.5), (cx - 1.4, cy + 0.9, zt - 0.7)], 0.12)
    # Upper tier: a second, smaller block offset toward the front.
    greeble_box(a, (-0.4, 1.3, zt + 0.6), (4.6, 5.2, 1.2), rng, 0.4, faces=("x", "-x", "y", "-y", "z"), dens=0.9, glow=0.12, pipes=2, grid=True)
    greeble_box(a, (1.6, -2.0, zt + 0.35), (2.2, 2.4, 0.7), rng, 0.35, faces=("x", "-x", "y", "-y", "z"), dens=0.9, glow=0.1, pipes=1)
    for k in range(4):
        a.tube("pipe", [(-2.2 + k * 0.5, -1.0, zt + 0.02 + 0.12), (-2.2 + k * 0.5, -2.5, zt + 0.25), (-2.0 + k * 0.4, -4.2, zt + 0.12)], 0.09)
    # Nose: sphere-fragment tractor emitter set into the front face.
    a.sphere("plate", 1.8, (0, L / 2 - 1.2, (zb + zt) / 2), 28,
             fn=lambda v: Vector((v.x, max(v.y, 0.9), v.z)))  # the back of the ball flattened into the hull
    a.sphere("glow", 0.5, (0, L / 2 + 0.35, (zb + zt) / 2), 18, squash=1.0)
    for k in range(4):
        ang = math.pi / 4 + k * math.pi / 2
        a.bar("grey", (1.3 * math.cos(ang), L / 2 + 0.05, (zb + zt) / 2 + 1.3 * math.sin(ang)),
              (0.5 * math.cos(ang), L / 2 + 0.6, (zb + zt) / 2 + 0.5 * math.sin(ang)), 0.06)
    a.tube("glowdim", [(1.42 * math.cos(t * math.pi / 12), L / 2 + 0.06, (zb + zt) / 2 + 1.42 * math.sin(t * math.pi / 12)) for t in range(24)],
           0.04, seg=6, sub=3, closed=True)
    # Belly: cargo recess with green lights.
    a.box("hull", (3.8, 6.6, 0.1), (0, 0, zb - 0.05))
    for k in range(-3, 4):
        a.box("glowdim", (3.4, 0.05, 0.03), (0, k * 0.9, zb - 0.11))
    a.flush("Block", root, bev=0.03)

    # Landing legs: lattice struts from the belly corners out to pads.
    for i, (sx, sy) in enumerate([(1, 1), (-1, 1), (1, -1), (-1, -1)]):
        leg = empty(f"Gear{i}", (sx * (W / 2 - 0.6), sy * (L / 2 - 1.4), zb), root)
        g = Acc(P)
        foot = Vector((sx * 1.3, sy * 0.9, -zb + 0.18))
        knee = Vector((sx * 1.0, sy * 0.7, -zb * 0.45))
        for dx, dy in ((-0.4, -0.4), (0.4, 0.4), (-0.4, 0.4), (0.4, -0.4)):
            g.bar("plate", (dx, dy, 0), knee + Vector((dx * 0.6, dy * 0.6, 0)), 0.08)
            g.bar("plate", knee + Vector((dx * 0.6, dy * 0.6, 0)), foot + Vector((dx * 0.35, dy * 0.35, 0.15)), 0.07)
        g.bar("pipe", (0, 0, 0), knee, 0.16)  # the strut's piston core
        g.bar("grey", knee, foot, 0.12)
        g.box("hull", (0.75, 0.75, 0.3), knee)
        g.box("glow", (0.14, 0.14, 0.14), knee + Vector((sx * 0.38, sy * 0.38, 0)))
        for t in (0.3,):
            p = knee.lerp(foot, 0.5)
            g.box("grey", (0.5, 0.5, 0.1), p)
        g.box("hull", (0.9, 0.9, 0.18), foot - Vector((0, 0, 0.09)))
        g.box("plate", (0.6, 0.6, 0.12), foot + Vector((0, 0, 0.06)))
        g.box("hull", (1.0, 1.0, 0.25), (0, 0, -0.12))
        g.flush(leg.name, leg)
    # Lift emitters at the belly corners (between the legs), glowing down.
    for i, (sx, sy) in enumerate([(1, 1), (-1, 1), (1, -1), (-1, -1)]):
        t = empty(f"Thruster{i}", (sx * (W / 2 - 1.7), sy * (L / 2 - 2.9), zb), root)
        ta = Acc(P)
        ta.box("plate", (1.2, 1.2, 0.22), (0, 0, -0.11))
        for k in range(-2, 3):
            ta.box("glow", (1.0, 0.06, 0.04), (0, k * 0.2, -0.23))
        ta.flush(t.name, t)
    # Clamp arms reaching down to the cargo's deck, jaws turned in.
    for i, (sx, sy) in enumerate([(1, 1), (-1, 1), (1, -1), (-1, -1)]):
        c = empty(f"Clamp{i}", (sx * 1.6, 0.5 if sy > 0 else -2.1, zb - 0.1), root)
        ca = Acc(P)
        ca.box("grey", (0.4, 0.4, 0.3), (0, 0, -0.1))
        ca.bar("plate", (0, 0, -0.2), (0, 0, -1.55), 0.09)
        ca.bar("pipe", (sx * 0.15, 0, -0.2), (sx * 0.15, 0, -1.4), 0.04)
        ca.box("hull", (0.4, 0.5, 0.35), (sx * -0.05, 0, -1.7))
        ca.box("plate", (0.4, 0.4, 0.1), (sx * -0.3, 0, -1.85))
        ca.box("glow", (0.05, 0.3, 0.05), (sx * -0.2, 0, -1.55))
        ca.flush(c.name, c)
    cg = empty("Cargo", (0, 0, 0), root)
    if cargo == "miner":
        borg_miner(P, parent=cg, fill=0.0)
    elif cargo == "rover":
        borg_rover(P, parent=cg)

    lamps = Acc(P)
    for s in (-1, 1):
        lamps.box("owner", (0.03, L - 0.4, 0.05), (s * (W / 2 + 0.01), 0, zt - 0.08))
        lamps.box("tail", (0.8, 0.03, 0.08), (s * 2.6, -L / 2 - 0.01, zb + 0.4))
    lamps.bar("plate", (-1.8, 2.5, zt + 1.2), (-1.8, 2.5, zt + 2.0), 0.05)
    lamps.flush("DropLamps", root)
    box("Beacon", (0.22, 0.22, 0.22), (-1.8, 2.5, zt + 2.12), P["beacon"], root, 0.0, rot=(math.radians(45), math.radians(35.26), 0))
    for s, side in ((-1, "L"), (1, "R")):
        marker(f"Name{side}", (s * (W / 2 + 0.25), 2.0, zb + 0.6), s, 3.0, 0.4, root)
        marker(f"Number{side}", (s * (W / 2 + 0.25), -1.8, zb + 0.6), s, 1.4, 0.5, root)
    return root


# ---------------------------------------------------------------- (4) outpost: seeded node -> assimilated complex

HEX = 25.0  # PlanetView.HexWidth (taken here as flat-to-flat)


def hex_guide(acc, centre=(0, 0), width=HEX):
    """Preview-only: a thin pale outline of one hex on the ground."""
    R = width / math.sqrt(3)
    pts = [Vector((centre[0] + R * math.cos(math.radians(30 + 60 * k)), centre[1] + R * math.sin(math.radians(30 + 60 * k)), 0.03))
           for k in range(6)]
    for k in range(6):
        p, q = pts[k], pts[(k + 1) % 6]
        mid = (p + q) / 2
        acc.box("guide", ((q - p).length, 0.16, 0.02), mid, (0, 0, math.atan2(q.y - p.y, q.x - p.x)))


def root_conduit(acc, rng, start_angle, r0, r1, z, radius, nodes=True, wobble=0.35):
    """A conduit 'root' snaking out across the ground from radius r0 to r1, with junction boxes and green nodes."""
    pts = []
    n = max(3, int((r1 - r0) / 2.2))
    ang = start_angle
    for k in range(n + 1):
        r = r0 + (r1 - r0) * k / n
        ang += rng.uniform(-wobble, wobble) / max(r, 1) * 3
        pts.append(Vector((r * math.cos(ang), r * math.sin(ang), z + radius)))
    path = acc.tube("pipe", pts, radius, seg=10, sub=5, r_end=radius * 0.6)
    if nodes:
        for k in range(2, len(path) - 1, 6):
            p = path[k]
            acc.box("plate", (radius * 3, radius * 3, radius * 2.4), (p.x, p.y, radius * 1.2), (0, 0, rng.uniform(0, 3)))
            acc.box("glow", (radius * 0.8,) * 3, (p.x, p.y, radius * 2.5))
    return pts[-1]


def native_house(P, acc, c, rot, rng, assimilated=0.3, sc=1.0):
    """A pale native dwelling (the world the Borg took): stone box with a dome and warm windows; `assimilated` (0..1)
    of it, from its local +X end (turn that toward the spire), is covered in black plating and climbing tubes."""
    M = Matrix.Translation(c) @ Matrix.Rotation(rot, 4, "Z") @ Matrix.Scale(sc, 4)
    acc.box_m("native", M @ Matrix.Translation((0, 0, 1.4)) @ Matrix.Diagonal((4.0, 3.2, 2.8, 1)))
    acc.sphere("nativeroof", 1.5, (-0.4, 0, 2.8), 20, M=M, fn=lambda v: Vector((v.x, v.y, max(v.z, 0.0))))
    for k in range(3):
        acc.box_m("window" if rng.random() > assimilated else "hull", M @ Matrix.Translation((-1.2 + k * 1.2, -1.61, 1.5)) @ Matrix.Diagonal((0.5, 0.04, 0.7, 1)))
    acc.box_m("nativeroof", M @ Matrix.Translation((1.4, -1.61, 0.8)) @ Matrix.Diagonal((0.7, 0.04, 1.5, 1)))
    # Plating creeping over from the +X end: a greebled shell in three ragged bands, loose plates beyond it.
    cover = 4.0 * assimilated
    if cover > 0.6:
        t = Acc(acc.P)
        for band in range(3):
            length = min(4.0, cover * rng.uniform(0.55, 1.0))
            greeble_box(t, (2.0 - length / 2 + 0.06, 0, (band + 0.5) * 0.95), (length + 0.12, 3.32, 0.95), rng, 0.3,
                        faces=("x", "y", "-y") + (("z",) if band == 2 else ()), dens=0.6, glow=0.12, pipes=0)
        acc.merge(t, M)
    if cover > 0.05:
        for k in range(int(6 + 20 * assimilated)):
            w, h = rng.uniform(0.4, 1.2), rng.uniform(0.4, 1.4)
            x = 2.0 - rng.uniform(0, cover)
            face = rng.choice(("-y", "y", "z", "x"))
            if face == "x":
                loc, size = (2.04, rng.uniform(-1.3, 1.3), rng.uniform(0.4, 2.6)), (0.1, w, h)
            elif face == "z":
                loc, size = (x, rng.uniform(-1.3, 1.3), 2.84), (w, h, 0.1)
            else:
                loc, size = (x, (1.64 if face == "y" else -1.64), rng.uniform(0.4, 2.4)), (w, 0.1, h)
            acc.box_m(rng.choice(("hull", "plate")), M @ Matrix.Translation(loc) @ Matrix.Diagonal((*size, 1)))
            if rng.random() < 0.2:
                acc.box_m("glowdim", M @ Matrix.Translation(Vector(loc) + Vector((0, 0, 0.0))) @ Matrix.Diagonal(
                    (size[0] * 0.8 + 0.06, size[1] * 0.8 + 0.06, 0.04, 1)))
        p0 = M @ Vector((2.6, -1.9, 0.18))
        p1 = M @ Vector((1.2 - cover * 0.4, -1.72, 1.2))
        p2 = M @ Vector((1.0 - cover * 0.5, -1.0, 2.95))
        acc.tube("pipe", [p0, M @ Vector((2.1, -1.75, 0.5)), p1, p2], 0.13 * sc)


def spire(acc, rng, base, top, height, segs, sc, glow_lines=True):
    """A stepped, tapering square spire of greebled segments with a pointed cap and a lit slit up each face."""
    z = 0.0
    seg_h = height / segs
    for k in range(segs):
        w = base + (top - base) * k / (segs - 1)
        greeble_box(acc, (0, 0, z + seg_h / 2), (w, w, seg_h * 1.001), rng, sc, faces=("x", "-x", "y", "-y"), dens=0.9, glow=0.08,
                    pipes=1)
        acc.box("plate", (w + 0.12 * sc * 2, w + 0.12 * sc * 2, 0.18 * sc), (0, 0, z + seg_h))  # collar
        if glow_lines:
            for f in range(4):
                ang = f * math.pi / 2
                acc.box("glow", (0.06 * sc, 0.06 * sc, seg_h * 0.7),
                        ((w / 2 + 0.3 * sc) * math.cos(ang), (w / 2 + 0.3 * sc) * math.sin(ang), z + seg_h / 2))
        z += seg_h
    w = top
    acc.cyl_m("hull", w * 0.72, 0.02, w * 2.2, Matrix.Translation((0, 0, z + w * 1.1)) @ Matrix.Rotation(math.pi / 4, 4, "Z"), seg=4)
    return z + w * 2.2


def alcove_wall(P, acc, c, rot, n, rows, rng, drones=0.5, sc=1.0):
    """A block with a grid of regeneration alcoves on its +Y face (green-lit, some with a drone standing in them)."""
    M = Matrix.Translation(c) @ Matrix.Rotation(rot, 4, "Z")
    aw, ah, depth = 1.1, 2.3, 1.2
    W, H = n * (aw + 0.25) + 0.4, rows * (ah + 0.35) + 0.3
    body = Acc(P)
    greeble_box(body, (0, -depth / 2 - 0.4, H / 2), (W, depth + 0.8, H), rng, 0.4, faces=("x", "-x", "-y", "z"), dens=0.6, glow=0.08,
                pipes=1)
    merge_acc(acc, body, M)
    for r in range(rows):
        for k in range(n):
            x = -W / 2 + 0.4 + k * (aw + 0.25) + aw / 2 - 0.05
            z = 0.25 + r * (ah + 0.35)
            acc.box_m("glowdim", M @ Matrix.Translation((x, -0.12, z + ah / 2)) @ Matrix.Diagonal((aw * 0.8, 0.04, ah * 0.85, 1)))
            for s in (-1, 1):
                acc.box_m("plate", M @ Matrix.Translation((x + s * (aw / 2 + 0.05), -0.0, z + ah / 2)) @ Matrix.Diagonal((0.15, 0.3, ah, 1)))
                acc.box_m("glow", M @ Matrix.Translation((x + s * (aw / 2 - 0.04), 0.1, z + ah / 2)) @ Matrix.Diagonal((0.03, 0.03, ah * 0.8, 1)))
            acc.box_m("plate", M @ Matrix.Translation((x, 0.0, z + ah + 0.08)) @ Matrix.Diagonal((aw + 0.3, 0.35, 0.18, 1)))
            acc.box_m("plate", M @ Matrix.Translation((x, 0.05, z - 0.06)) @ Matrix.Diagonal((aw + 0.1, 0.4, 0.12, 1)))
            if rng.random() < drones:
                drone_global = M @ Vector((x, 0.05, z))
                drone_figure(P, None, drone_global, facing=rot, name=f"AlcoveDrone{len(bpy.data.objects)}", height=1.8)
    return M


def merge_acc(dst, src, M=Matrix()):
    dst.merge(src, M)


def power_cell(acc, c, h, r):
    c = Vector(c)
    acc.cyl("glow", r, h, c + Vector((0, 0, h / 2)), (0, 0, 1), 16)
    for z in (0.1, h * 0.5, h - 0.1):
        acc.cyl("plate", r * 1.35, 0.18, c + Vector((0, 0, z)), (0, 0, 1), 16)
    for q in range(4):
        ang = q * math.pi / 2 + math.pi / 4
        acc.bar("plate", c + Vector((r * 1.25 * math.cos(ang), r * 1.25 * math.sin(ang), 0)),
                c + Vector((r * 1.25 * math.cos(ang), r * 1.25 * math.sin(ang), h)), 0.06)
    acc.cyl("hull", r * 1.6, 0.3, c + Vector((0, 0, 0.15)), (0, 0, 1), 16)


def borg_block(acc, rng, c, s, sc=0.5, grid=True):
    """A ground-standing hive block (centre x, y; size s; bottom on the ground)."""
    greeble_box(acc, (c[0], c[1], s[2] / 2), s, rng, sc, faces=("x", "-x", "y", "-y", "z"), dens=0.8, glow=0.1, pipes=2, grid=grid)


def regen_pad(acc, c, size=7.0):
    """Vehicle regeneration pad (the Borg repair pad): a dark plate with a green grid."""
    acc.box("hull", (size, size, 0.2), (c[0], c[1], 0.1))
    for k in range(-3, 4):
        acc.box("glowdim", (size * 0.92, 0.05, 0.03), (c[0], c[1] + k * size / 7.5, 0.21))
        acc.box("glowdim", (0.05, size * 0.92, 0.03), (c[0] + k * size / 7.5, c[1], 0.21))
    for sx in (-1, 1):
        for sy in (-1, 1):
            acc.box("plate", (0.6, 0.6, 0.5), (c[0] + sx * size / 2, c[1] + sy * size / 2, 0.25))
            acc.box("glow", (0.2, 0.2, 0.2), (c[0] + sx * size / 2, c[1] + sy * size / 2, 0.55))


def spin_ring(P, parent, z, r, sc, name="Spin"):
    """A square sensor ring around a spire that turns while the building works ("Spin")."""
    spin = empty(name, (0, 0, z), parent)
    a = Acc(P)
    for k in range(4):
        ang = k * math.pi / 2
        p = Vector((r * math.cos(ang), r * math.sin(ang), 0))
        q = Vector((r * math.cos(ang + math.pi / 2), r * math.sin(ang + math.pi / 2), 0))
        a.bar("plate", p, q, 0.12 * sc)
        a.box("grey", (0.5 * sc, 0.5 * sc, 0.5 * sc), p)
        a.cyl("laser", 0.08 * sc, 0.1 * sc, p * (1 + 0.28 * sc / r), p, 10)
        a.bar("pipe", p, p * 0.3, 0.05 * sc)
    a.flush(name, spin)
    return spin


def outpost_l1(P, root, rng):
    """Seeded node: the seed has just landed and split open; a black stepped spire stands in the crater, conduit roots
    are spreading across the ground and one is already climbing a native dwelling."""
    a = Acc(P)
    hex_guide(a)
    # Crater: a low ring of churned rubble and shell petals of the seed.
    for k in range(40):
        ang = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(2.4, 4.0)
        sz = rng.uniform(0.3, 0.9)
        a.box("rubble", (sz, sz * 1.3, sz * 0.6), (r * math.cos(ang), r * math.sin(ang), sz * 0.2), (rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), ang))
    for k in range(4):
        ang = k * math.pi / 2 + 0.3
        M = Matrix.Translation((2.5 * math.cos(ang), 2.5 * math.sin(ang), 0.0)) @ Matrix.Rotation(ang, 4, "Z") @ Matrix.Rotation(
            math.radians(35), 4, "Y")
        a.box_m("hull", M @ Matrix.Translation((0.0, 0, 1.3)) @ Matrix.Diagonal((0.25, 2.6, 2.6, 1)))
        a.box_m("plate", M @ Matrix.Translation((0.16, 0, 1.3)) @ Matrix.Diagonal((0.1, 2.0, 2.0, 1)))
        a.box_m("glowdim", M @ Matrix.Translation((0.22, 0, 1.3)) @ Matrix.Diagonal((0.03, 1.6, 0.05, 1)))
        a.box_m("glowdim", M @ Matrix.Translation((0.22, 0, 1.3)) @ Matrix.Diagonal((0.03, 0.05, 1.6, 1)))
    # Spire, tilted a few degrees (driven in, not built).
    tilt = empty("Spire", (0, 0, -0.4), root)
    tilt.rotation_euler = (math.radians(4), math.radians(-5), 0)
    s = Acc(P)
    top = spire(s, rng, 2.6, 1.1, 10.0, 5, 0.32)
    s.flush("Spire", tilt, bev=0.02)
    box("Beacon", (0.35, 0.35, 0.35), (0, 0, top + 0.25), P["beacon"], tilt, 0.0, rot=(math.radians(45), math.radians(35.26), 0))
    spin_ring(P, tilt, 7.4, 1.6, 0.45)
    o = Acc(P)
    o.box("plate", (3.0, 3.0, 0.22), (0, 0, 0.9))
    o.box("owner", (3.05, 0.05, 0.08), (0, 1.53, 0.9))
    o.box("owner", (3.05, 0.05, 0.08), (0, -1.53, 0.9))
    o.box("owner", (0.05, 3.05, 0.08), (1.53, 0, 0.9))
    o.box("owner", (0.05, 3.05, 0.08), (-1.53, 0, 0.9))
    o.flush("OwnerLine", tilt)
    # Roots.
    for k in range(7):
        ang = k * 2 * math.pi / 7 + rng.uniform(-0.2, 0.2)
        root_conduit(a, rng, ang, 1.4, rng.uniform(7.5, 11.0), 0.0, rng.uniform(0.16, 0.3))
    # Two alcoves at the base facing out.
    alc = Acc(P)
    alcove_wall(P, alc, (0, 0, 0), 0, 2, 1, rng, drones=1.0)
    Mw = Matrix.Translation((5.0, 4.2, 0)) @ Matrix.Rotation(math.radians(-45), 4, "Z")
    merge_acc(a, alc, Mw)
    for ob in [ob for ob in bpy.data.objects if ob.name.startswith("AlcoveDrone") and ob.parent is None]:
        ob.matrix_world = Mw @ ob.matrix_world
        ob.parent = root
    power_cell(a, (-3.4, -3.2, 0), 2.2, 0.35)
    # A native dwelling on the edge of the hex, a root already climbing it.
    native_house(P, a, Vector((-6.5, 5.5, 0)), math.radians(-40), rng, assimilated=0.35)
    a.tube("pipe", [(-1.2, 1.0, 0.25), (-3.0, 2.6, 0.25), (-5.0, 3.6, 0.25), (-8.4, 3.2, 0.3)], 0.22)
    a.flush("Node", root, bev=0.0)


def place(rng, items, r_lo, r_hi, size, tries=200, gap=1.2):
    """Pick a centre on a ring that keeps a circle of `size` clear of everything placed so far."""
    for _ in range(tries):
        ang = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(r_lo, r_hi)
        c = Vector((r * math.cos(ang), r * math.sin(ang)))
        if all((c - q).length > size + qs + gap for q, qs in items):
            items.append((c, size))
            return c
    return None


def outpost_l5(P, root, rng):
    """Assimilation node: the spire has grown and the seed site is a small hive inside one hex: hive blocks with green
    grids, an alcove block, power cells, a lattice mast, an assembly-matrix cube frame, a regeneration pad, conduits to
    the hex edge, and a native dwelling half converted."""
    a = Acc(P)
    hex_guide(a)
    tilt = empty("Spire", (0, 0, -0.3), root)
    s = Acc(P)
    top = spire(s, rng, 3.6, 1.4, 15.0, 6, 0.4)
    s.flush("Spire", tilt, bev=0.02)
    box("Beacon", (0.45, 0.45, 0.45), (0, 0, top + 0.3), P["beacon"], tilt, 0.0, rot=(math.radians(45), math.radians(35.26), 0))
    spin_ring(P, tilt, 11.5, 2.0, 0.55)
    o = Acc(P)
    o.box("plate", (4.0, 4.0, 0.26), (0, 0, 1.1))
    for sx, sy, w, d in ((0, 2.05, 4.1, 0.06), (0, -2.05, 4.1, 0.06), (2.05, 0, 0.06, 4.1), (-2.05, 0, 0.06, 4.1)):
        o.box("owner", (w, d, 0.1), (sx, sy, 1.1))
    o.flush("OwnerLine", tilt)
    items = [(Vector((0, 0)), 2.6)]
    blocks = [((5.2, 1.5), (4.4, 4.0, 6.5)), ((-4.6, -3.6), (4.0, 3.6, 4.6)), ((-1.0, 5.8), (3.2, 3.2, 9.0)), ((1.8, -6.2), (3.6, 2.8, 3.4))]
    for (x, y), size in blocks:
        borg_block(a, rng, (x, y), size, 0.45)
        items.append((Vector((x, y)), max(size[0], size[1]) * 0.7))
        # Conduits from each block to the spire at height.
        h = size[2] * rng.uniform(0.55, 0.85)
        d = Vector((x, y)).normalized()
        a.tube("pipe", [(x - d.x * size[0] * 0.5, y - d.y * size[1] * 0.5, h), ((x - d.x * 2) * 0.6, (y - d.y * 2) * 0.6, h + 0.8),
                        (d.x * 2.2, d.y * 2.2, h + 1.6)], 0.28)
    # Alcove block facing out of the hex front.
    alc = Acc(P)
    alcove_wall(P, alc, (0, 0, 0), 0, 3, 2, rng, drones=0.6)
    Mw = Matrix.Translation((5.4, 6.8, 0)) @ Matrix.Rotation(math.radians(20), 4, "Z")
    merge_acc(a, alc, Mw)
    for ob in [ob for ob in bpy.data.objects if ob.name.startswith("AlcoveDrone") and ob.parent is None]:
        ob.matrix_world = Mw @ ob.matrix_world
        ob.parent = root
    for x, y in ((-6.0, 1.6), (-7.2, 3.2), (-5.4, 3.6)):
        power_cell(a, (x, y, 0), 3.2, 0.42)
    lattice(a, (7.6, -3.6, 0), 1.6, 1.6, 12.0, rng, levels=6, r=0.09)
    a.box("hull", (2.2, 2.2, 0.6), (7.6, -3.6, 12.3))
    a.cyl("laser", 0.12, 0.3, (7.6, -3.6, 12.75), (0, 0, 1), 10)
    cube_frame(a, (-2.2, -8.4, 2.1), 4.0, 0.12, rng)
    greeble_box(a, (-2.2, -8.4, 1.2), (2.4, 2.4, 2.4), rng, 0.35, dens=0.8, glow=0.12, pipes=1)  # a cube half grown in it
    regen_pad(a, (-9.2, -0.6), 4.6)
    for k in range(9):
        ang = k * 2 * math.pi / 9 + 0.3
        root_conduit(a, rng, ang, 3.0, 12.2, 0.0, rng.uniform(0.2, 0.32))
    native_house(P, a, Vector((-6.8, 6.4, 0)), math.radians(-43), rng, assimilated=0.7, sc=0.95)
    a.flush("Hive", root)


def borg_sphere(acc, rng, c, r):
    """A small greebled sphere (hovering transport) with glowing latitude/longitude seams."""
    c = Vector(c)
    acc.sphere("hull", r, c, 32)
    for _ in range(int(r * r * 26)):
        n = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        q = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
        w, h, d = rng.uniform(0.3, 1.1), rng.uniform(0.3, 1.1), rng.choice((0.1, 0.2, 0.35))
        acc.box_m(rng.choice(("plate", "plate", "hull", "grey")), Matrix.Translation(c + n * (r + d / 2 - 0.05)) @ q @ Matrix.Diagonal((w, h, d, 1)))
    for lat in (-50, -25, 0, 25, 50):
        rr = r * math.cos(math.radians(lat)) + 0.08
        z = r * math.sin(math.radians(lat))
        acc.tube("glowdim", [c + Vector((rr * math.cos(t * math.pi / 16), rr * math.sin(t * math.pi / 16), z)) for t in range(32)], 0.05, seg=6,
                 sub=2, closed=True)
    for lon in range(0, 180, 30):
        a = math.radians(lon)
        acc.tube("glowdim", [c + Vector(((r + 0.08) * math.cos(t * math.pi / 16) * math.cos(a), (r + 0.08) * math.cos(t * math.pi / 16) * math.sin(a),
                                         (r + 0.08) * math.sin(t * math.pi / 16))) for t in range(32)], 0.05, seg=6, sub=2, closed=True)
    acc.sphere("glow", r * 0.18, c + Vector((0, 0, -r)), 14)


def outpost_l10(P, root, rng):
    """Assimilated complex: the hive has spread over seven hexes. A plated ground field, a massive central hub cube with
    a glowing core face, a great spire, rings of hive blocks joined by elevated conduits, lattice masts, alcove blocks,
    power farms, two regeneration pads, converted native buildings, and a sphere moored overhead on a tractor beam."""
    a = Acc(P)
    for cx, cy in [(0, 0)] + [(HEX * math.cos(math.radians(60 * k)), HEX * math.sin(math.radians(60 * k))) for k in range(6)]:
        hex_guide(a, (cx, cy))
    # Plated ground: dark tiles with a green seam grid, ragged at the edge.
    t = 3.0
    for i in range(-12, 13):
        for j in range(-12, 13):
            x, y = i * t, j * t
            r = math.hypot(x, y)
            if r < 31 + rng.uniform(-5, 3):
                a.box(rng.choice(("hull", "plate", "hull")), (t - 0.12, t - 0.12, rng.uniform(0.12, 0.3)), (x, y, 0.06))
                if rng.random() < 0.18:
                    a.box("glowdim", (t * 0.8, 0.06, 0.04), (x, y + t / 2 - 0.06, 0.2))
    # Central hub cube with a recessed glowing core on its front face.
    hub = 14.0
    greeble_box(a, (0, 0, hub / 2), (hub, hub, hub), rng, 0.9, faces=("x", "-x", "-y", "z"), dens=0.8, glow=0.1, pipes=4, grid=True)
    greeble_box(a, (0, hub / 2 - 0.5, hub / 2), (hub, 1.0, hub), rng, 0.9, faces=("y",), dens=0.5, glow=0.1, pipes=2, grid=True, core=False)
    a.cyl("hull", 3.6, 0.8, (0, hub / 2 + 0.1, hub * 0.55), (0, 1, 0), 24)
    a.cyl("glow", 2.4, 0.6, (0, hub / 2 + 0.4, hub * 0.55), (0, 1, 0), 24)
    a.tube("glowdim", [(4.2 * math.cos(k * math.pi / 12), hub / 2 + 0.5, hub * 0.55 + 4.2 * math.sin(k * math.pi / 12)) for k in range(24)], 0.15,
           seg=8, sub=3, closed=True)
    # The great spire on the hub's back corner.
    sp = empty("Spire", (-3.5, -3.5, hub), root)
    s = Acc(P)
    top = spire(s, rng, 5.0, 1.8, 24.0, 7, 0.6)
    s.flush("Spire", sp, bev=0.03)
    box("Beacon", (0.7, 0.7, 0.7), (0, 0, top + 0.5), P["beacon"], sp, 0.0, rot=(math.radians(45), math.radians(35.26), 0))
    spin_ring(P, sp, 16.0, 3.0, 0.8)
    o = Acc(P)
    o.box("owner", (hub + 0.1, 0.08, 0.16), (0, hub / 2 + 0.03, 1.4))
    o.box("owner", (0.08, hub + 0.1, 0.16), (hub / 2 + 0.03, 0, 1.4))
    o.box("owner", (0.08, hub + 0.1, 0.16), (-hub / 2 - 0.03, 0, 1.4))
    o.flush("OwnerLine", root)
    # Rings of hive blocks with elevated conduits to the hub.
    items = [(Vector((0, 0)), hub * 0.75)]
    pads = [Vector((10.5, 12.5)), Vector((-13.5, 9.0))]
    for p in pads:
        regen_pad(a, p, 6.0)
        items.append((p, 4.0))
    for ring_lo, ring_hi, n, smin, smax, hmin, hmax in ((10, 15, 7, 3.0, 5.5, 4, 14), (16, 24, 12, 2.5, 5.0, 3, 10), (24, 31, 12, 2.0, 4.0, 2, 6)):
        for _ in range(n):
            w, d = rng.uniform(smin, smax), rng.uniform(smin, smax)
            c = place(rng, items, ring_lo, ring_hi, max(w, d) * 0.72, gap=1.0)
            if c is None:
                continue
            h = rng.uniform(hmin, hmax)
            borg_block(a, rng, c, (w, d, h), 0.5)
            if rng.random() < 0.4:  # a stacked smaller block
                w2, d2, h2 = w * rng.uniform(0.4, 0.7), d * rng.uniform(0.4, 0.7), h * rng.uniform(0.3, 0.7)
                greeble_box(a, (c.x + rng.uniform(-0.2, 0.2) * w, c.y + rng.uniform(-0.2, 0.2) * d, h + h2 / 2), (w2, d2, h2), rng, 0.45, dens=0.8,
                            glow=0.1, pipes=1, grid=True)
            if c.length < 25:
                hh = min(h * 0.8, hub * 0.8)
                u = c.normalized()
                t0 = min((w / 2) / abs(u.x) if abs(u.x) > 1e-3 else 1e9, (d / 2) / abs(u.y) if abs(u.y) > 1e-3 else 1e9)
                p0 = Vector((c.x - u.x * t0, c.y - u.y * t0, hh))
                f = (hub / 2) / max(abs(u.x), abs(u.y))
                p2 = Vector((u.x * f, u.y * f, hub * rng.uniform(0.3, 0.7)))
                mid = (p0 + p2) / 2 + Vector((0, 0, 1.5))
                a.tube("pipe", [p0, mid, p2], rng.uniform(0.3, 0.55))
                for k in (0.33, 0.66):  # supports under long runs
                    q = p0.lerp(p2, k)
                    if q.z > 3 and (Vector((q.x, q.y)).length > hub * 0.75):
                        a.bar("plate", (q.x, q.y, 0.2), (q.x, q.y, q.z), 0.2)
    # Masts, alcove blocks, power farm.
    for x, y, h in ((15.5, -8.0, 22.0), (-17.0, -6.5, 18.0), (4.0, 22.0, 16.0)):
        lattice(a, (x, y, 0), 2.0, 2.0, h, rng, levels=8, r=0.12)
        a.box("hull", (2.8, 2.8, 0.8), (x, y, h + 0.4))
        a.cyl("laser", 0.18, 0.4, (x, y, h + 1.0), (0, 0, 1), 10)
    for (x, y), rot in (((9.5, -16.0), math.radians(200)), ((-9.0, -17.5), math.radians(160)), ((-20.0, 15.0), math.radians(-40))):
        alc = Acc(P)
        alcove_wall(P, alc, (0, 0, 0), 0, 4, 3, rng, drones=0.35)
        Mw = Matrix.Translation((x, y, 0)) @ Matrix.Rotation(rot, 4, "Z")
        merge_acc(a, alc, Mw)
        for ob in [ob for ob in bpy.data.objects if ob.name.startswith("AlcoveDrone") and ob.parent is None]:
            ob.matrix_world = Mw @ ob.matrix_world
            ob.parent = root
    for k in range(6):
        power_cell(a, (-11.0 + (k % 3) * 2.2, 18.5 + (k // 3) * 2.4, 0.3), 5.0, 0.6)
    for (x, y), rot in (((27.0, 4.0), 0.4), ((-26.0, -10.0), -0.8), ((14.0, 26.0), 1.2)):
        native_house(P, a, Vector((x, y, 0)), rot, rng, assimilated=1.0, sc=1.3)
    for k in range(14):
        root_conduit(a, rng, k * 2 * math.pi / 14 + 0.1, 28.0, rng.uniform(34, 40), 0.0, rng.uniform(0.3, 0.5))
    # Sphere moored overhead on a tractor beam from a docking mast.
    dock = Vector((16.0, 6.0))
    lattice(a, (dock.x, dock.y, 0), 2.6, 2.6, 14.0, rng, levels=6, r=0.14)
    a.box("hull", (4.0, 4.0, 1.0), (dock.x, dock.y, 14.5))
    a.cyl("glow", 0.9, 0.3, (dock.x, dock.y, 15.1), (0, 0, 1), 18)
    sph = Vector((dock.x, dock.y, 34.0))
    borg_sphere(a, rng, sph, 7.0)
    a.flush("Complex", root)
    beam = Acc(P)
    beam.cyl("beam", 1.0, sph.z - 7.0 - 15.2, (dock.x, dock.y, (sph.z - 7.0 + 15.2) / 2), (0, 0, 1), 24, r2=2.2)
    beam.flush("TractorBeam", root)


def borg_outpost(P, level, seed=SEED + 10):
    rng = random.Random(seed + level)
    root = empty("ColonyCore")
    {1: outpost_l1, 5: outpost_l5, 10: outpost_l10}[level](P, root, rng)
    return root


# ---------------------------------------------------------------- the Federation Mk I (comparison)


def fed_mk1(parent=None, loc=(0, 0, 0)):
    """build_vehicles' tier-1 rover (no bake); its materials are renamed Fed_* so Borg ones of the same name (Owner,
    TailLamp) don't recolour it."""
    BV["TIER"] = 1
    before = set(bpy.data.materials.keys())
    root = BV["rover_mk1"](BV["palette"]())
    for m in bpy.data.materials:
        if m.name not in before:
            m.name = "Fed_" + m.name
    root.location = loc
    if parent:
        root.parent = parent
    return root


# ---------------------------------------------------------------- preview

BASE_CAMS = {
    "front34": (Vector((7.5, 8.5, 3.2)), 40),
    "rear34": (Vector((-7.5, -8.0, 3.6)), 40),
    "side": (Vector((10.5, 0.4, 1.9)), 38),
    "game": (Vector((0.0, 13.0, 15.5)), 30),
    "top": (Vector((0.01, 0.0, 16.0)), 30),
}
BASE_TARGET = Vector((0, 0, 1.3))


def preview(folder, stem, k=1.0, target=None, ground_size=40.0, shots=SHOTS, game_k=None, cams=None):
    """build_vehicles.preview_scene's look (sky, sun, exposure, sand ground) with the cameras scaled by k about the
    target, so the Borg rover and the Fed Mk I share the exact same camera."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 2
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = math.radians(28)
    sky.sun_rotation = math.radians(150)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.12
    scene.view_settings.exposure = -0.6
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(55), 0, math.radians(150))
    link(sun)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=ground_size)
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))

    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.clip_end = 2000
    link(cam)
    scene.camera = cam
    target = Vector(target) if target is not None else BASE_TARGET
    os.makedirs(folder, exist_ok=True)
    for name in shots:
        pos, lens = (cams or BASE_CAMS)[name]
        p = target + (pos - BASE_TARGET) * (game_k if name == "game" and game_k else k)
        cam.location = p
        cam.data.lens = lens
        cam.rotation_euler = (target - p).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{stem}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath, flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    P = borg_palette()
    if MODEL == "rover":
        borg_rover(P)
        stem, k, target, gs = "borg-rover", 1.0, None, 40
    elif MODEL == "miner":
        borg_miner(P)
        stem, k, target, gs = "borg-miner", 1.0, None, 40
    elif MODEL == "dropship":
        borg_dropship(P)
        stem, k, target, gs = "borg-dropship", 2.0, (0, 0, 3.4), 80
    elif MODEL == "outpost":
        borg_outpost(P, LEVEL)
        k, tz = {1: (3.0, 3.0), 5: (4.4, 5.0), 10: (10.5, 13.0)}[LEVEL]
        stem, target, gs = f"borg-outpost-l{LEVEL}", (0, 0, tz), 60 * k
    elif MODEL == "fed_mk1":
        fed_mk1()
        stem, k, target, gs = "fed-mk1", 1.0, None, 40
    else:  # lineup
        fed_mk1(loc=(8.0, 0, 0))
        borg_rover(P, loc=(4.2, 0, 0))
        borg_miner(P, loc=(0.0, 0, 0))
        borg_dropship(P, cargo="none", loc=(-8.0, 0, 0))
        stem, k, target, gs = "borg-lineup", 1.0, (-1.5, 0, 2.8), 120
        if PREVIEW:  # a near-frontal three-quarter view down the row (vehicles face +Y)
            lineup_cams = dict(BASE_CAMS, front34=(BASE_TARGET + Vector((3.5, 31.0, 6.5)), 35),
                               game=(BASE_TARGET + Vector((0.0, 20.0, 24.0)), 30))
            preview(PREVIEW, stem, k, target, gs, cams=lineup_cams)
        return
    if PREVIEW:
        # The game shot of the big pieces comes in closer than the scaled vehicle camera (the game zooms in to 15).
        preview(PREVIEW, stem, k, target, gs, game_k=k * 0.6 if k > 1.5 else None)


main()
