"""
Space sites for the space jobs (docs/space-jobs-contract.md "MODELS", notes in blender/space_sites.md).

Builds ONE glb, game/models/space/sites.glb, with one root node per site model, each on its own origin (centre of
mass, so the game can tumble it), -Z forward like the ship glbs, metres at the ship models' scale (the Armstrong is
~127 m long, the Cochrane ~480 m). Original designs; pre-warp human parts in the white/grey/foil/orange of the six
pre-warp ships.

  derelict_unknown   ~180 m alien hull broken in two; faint aft light Light0
  derelict_earth     ~120 m dark Earth explorer/freighter (truss spine, crew module, torn ring arc, tanks)
  derelict_trader    ~95 m boxy trader, cargo doors open, two engine pods
  derelict_native    ~62 m native early-warp ship (trefoil body, three outrigger coils)
  debris_piece_a..f  curved hull plate, twisted truss, nacelle stub, hull chunk, cargo pod (CargoPod),
                     black box (BlackBox -> Beacon)
  asteroid_common / _metal / _ice / _hollow (Mouth at the cavity entrance, Light0 deep inside)
  lifepod            ~4.6 m capsule, Beacon on top
  comet_nucleus      ~420 m dark peanut nucleus with ice patches, Jet0..2 vent points
  station            small pre-warp station: hub, spinning Ring (extras.spin), Berth0..1, Beacon, lamps
  probe_alien        ~13 m three-spined probe with a glowing Core

Run (PyPI bpy in the 3.11 venv):
  /home/user/.bpyvenv/bin/python blender/build_space_sites.py --game            # -> game/models/space/sites.glb
  /home/user/.bpyvenv/bin/python blender/build_space_sites.py --preview blender/previews/space_sites [--only a,b]
  python3 blender/build_space_sites.py --sheet blender/previews/space_sites     # contact sheet (system PIL)
Flags: --out PATH, --only names, --res WxH (800x480), --spp N (32), --tex DIR (texture scratch dir)
"""

import json
import math
import os
import random
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

ORDER = ["derelict_unknown", "derelict_earth", "derelict_trader", "derelict_native",
         "debris_piece_a", "debris_piece_b", "debris_piece_c", "debris_piece_d", "debris_piece_e", "debris_piece_f",
         "asteroid_common", "asteroid_metal", "asteroid_ice", "asteroid_hollow",
         "lifepod", "comet_nucleus", "station", "probe_alien"]


def arg(name, default=None):
    if name in sys.argv:
        i = sys.argv.index(name)
        return sys.argv[i + 1] if i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith("--") else True
    return default


# ---------------------------------------------------------------- contact sheet (system python + PIL, no bpy)

if arg("--sheet"):
    from PIL import Image, ImageDraw
    folder = arg("--sheet")
    tiles = [(n, os.path.join(folder, f"{n}.png")) for n in ORDER]
    tiles = [(n, p) for n, p in tiles if os.path.exists(p)]
    w, cols = 400, 4
    h = 240
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (w + 6) + 6, rows * (h + 26) + 6), (8, 9, 12))
    d = ImageDraw.Draw(sheet)
    for i, (n, p) in enumerate(tiles):
        im = Image.open(p).convert("RGB")
        im = im.resize((w, int(im.height * w / im.width)))
        x, y = 6 + (i % cols) * (w + 6), 6 + (i // cols) * (h + 26)
        sheet.paste(im.crop((0, 0, w, h)), (x, y))
        d.text((x + 4, y + h + 6), n, fill=(220, 200, 160))
    out = os.path.join(folder, "space_sites-sheet.png")
    sheet.save(out)
    print("wrote", out)
    sys.exit(0)

import bpy  # noqa: E402  (bpy before mathutils/bmesh)
import bmesh  # noqa: E402,F401
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

sys.path.insert(0, HERE)
from cochrane_kit import Kit, rotX, rotY, rotZ, move, orient, TAU, PI  # noqa: E402

import tempfile  # noqa: E402
TEX = arg("--tex", os.path.join(tempfile.gettempdir(), "space_sites_tex"))

# ---------------------------------------------------------------- textures (numpy, tileable 512²)


def periodic_noise(n, beta, seed):
    rng = np.random.default_rng(seed)
    f = np.fft.fftfreq(n)
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx * fx + fy * fy)
    r[0, 0] = 1
    spec = (rng.normal(size=(n, n)) + 1j * rng.normal(size=(n, n))) / r ** beta
    spec[0, 0] = 0
    a = np.real(np.fft.ifft2(spec))
    return (a - a.mean()) / (a.std() + 1e-9)


def panel_tex(name, base, var=0.06, line=0.45, seed=1, rows=(4, 9), cols=(2, 6), grime=0.08, n=512):
    rng = np.random.default_rng(seed)
    img = np.zeros((n, n, 3)) + np.array(base)
    shade = np.ones((n, n))
    lines = np.ones((n, n))
    # rows of varied height, each split into columns of varied width (lines span the tile, so it wraps)
    nr = rng.integers(rows[0], rows[1] + 1)
    cuts = np.sort(rng.choice(np.arange(8, n - 8), nr - 1, replace=False))
    ys = [0] + list(cuts) + [n]
    for i in range(len(ys) - 1):
        nc = rng.integers(cols[0], cols[1] + 1)
        cx = np.sort(rng.choice(np.arange(8, n - 8), nc - 1, replace=False))
        xs = [0] + list(cx) + [n]
        for j in range(len(xs) - 1):
            shade[ys[i]:ys[i + 1], xs[j]:xs[j + 1]] = 1 + rng.normal() * var
            lines[ys[i]:ys[i] + 2, xs[j]:xs[j + 1]] = line
            lines[ys[i]:ys[i + 1], xs[j]:xs[j] + 2] = line
    g = periodic_noise(n, 1.4, seed + 7)
    img = img * shade[..., None] * lines[..., None] * (1 + grime * g)[..., None]
    return save_tex(name, img)


def rock_tex(name, c1, c2, seed=3, n=512, speck=0.1):
    a = periodic_noise(n, 1.3, seed)
    b = periodic_noise(n, 0.6, seed + 1)
    t = np.clip(0.5 + 0.22 * a, 0, 1)[..., None]
    img = np.array(c1) * (1 - t) + np.array(c2) * t
    img = img * (1 + speck * b)[..., None]
    return save_tex(name, img)


def save_tex(name, img):
    from PIL import Image
    os.makedirs(TEX, exist_ok=True)
    srgb = np.where(img <= 0.0031308, img * 12.92, 1.055 * np.power(np.clip(img, 0, None), 1 / 2.4) - 0.055)
    p = os.path.join(TEX, name + ".jpg")
    Image.fromarray((np.clip(srgb, 0, 1) * 255).astype(np.uint8)).save(p, quality=88)
    return p


# ---------------------------------------------------------------- materials


MATS = {}
UVSCALE = {}


def material(name, col=(0.5, 0.5, 0.5), metal=0.0, rough=0.5, emit=None, estr=0.0, tex=None, tile=8.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = estr
    if tex:
        im = nt.nodes.new("ShaderNodeTexImage")
        im.image = bpy.data.images.load(tex)
        nt.links.new(im.outputs["Color"], b.inputs["Base Color"])
        UVSCALE[name] = (1.0 / tile, 1.0 / tile)
    MATS[name] = m
    return m


def make_materials():
    material("Hull", tex=panel_tex("hull", (0.075, 0.085, 0.07), var=0.12, line=0.3, seed=11, rows=(3, 7), cols=(1, 4)),
             metal=0.35, rough=0.5, tile=14)
    material("HullWhite", tex=panel_tex("hull_white", (0.62, 0.62, 0.60), var=0.05, line=0.55, seed=21), rough=0.5,
             tile=6)
    material("HullGrey", tex=panel_tex("hull_grey", (0.30, 0.31, 0.32), var=0.07, line=0.5, seed=23), rough=0.55,
             metal=0.2, tile=6)
    material("HullTrader", tex=panel_tex("hull_trader", (0.32, 0.20, 0.10), var=0.12, line=0.45, seed=31,
                                         rows=(5, 10), cols=(3, 7), grime=0.18), metal=0.45, rough=0.5, tile=10)
    material("HullNative", tex=panel_tex("hull_native", (0.20, 0.30, 0.30), var=0.06, line=0.6, seed=41, rows=(6, 12),
                                         cols=(1, 2)), metal=0.2, rough=0.45, tile=7)
    material("NativeTrim", (0.55, 0.38, 0.12), metal=0.6, rough=0.35)
    material("Foil", (0.75, 0.55, 0.18), metal=1.0, rough=0.25)
    material("FoilSilver", (0.7, 0.7, 0.72), metal=1.0, rough=0.3)
    material("Orange", (0.80, 0.22, 0.03), rough=0.5)
    material("Dark", (0.025, 0.025, 0.028), rough=0.7)
    material("Metal", (0.45, 0.44, 0.42), metal=1.0, rough=0.38)
    material("Solar", tex=panel_tex("solar", (0.03, 0.05, 0.12), var=0.03, line=0.4, seed=51, rows=(12, 12),
                                    cols=(6, 6), grime=0.02), metal=0.4, rough=0.25, tile=6)
    material("Glass", (0.02, 0.03, 0.04), metal=0.0, rough=0.08)
    material("Lamp", (0.9, 0.85, 0.7), emit=(1.0, 0.78, 0.45), estr=4.0)
    material("Beacon", (0.9, 0.1, 0.05), emit=(1.0, 0.12, 0.04), estr=6.0)
    material("Glow", (0.1, 0.4, 0.35), emit=(0.2, 1.0, 0.8), estr=5.0)
    material("Rock", tex=rock_tex("rock", (0.16, 0.14, 0.12), (0.34, 0.31, 0.27), seed=61), rough=0.85, tile=40)
    material("Ice", tex=rock_tex("ice", (0.48, 0.56, 0.62), (0.82, 0.88, 0.92), seed=71, speck=0.05), rough=0.25,
             tile=40)
    material("CometRock", tex=rock_tex("comet", (0.035, 0.033, 0.03), (0.09, 0.085, 0.08), seed=81), rough=0.9, tile=60)


# ---------------------------------------------------------------- the build state

k = Kit(seed=7)
k.one_key = True
EMPTIES = {}       # name -> (parent name or None, location (kit frame), rotation matrix 3x3 (kit frame) or None, props)
SPIN = {}


def node(name, parent, loc=(0, 0, 0), rot=None, **props):
    EMPTIES[name] = (parent, Vector(loc), rot, props)


def group(name):
    k.group = name


# kit frame: x toward the bow, y to port, z dorsal; the export turns x to Blender +Y (= glTF/Godot -Z)
KIT_TO_BL = Matrix(((0, -1, 0, 0), (1, 0, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))


# ---------------------------------------------------------------- shape helpers


def loft(mat, rings, closed0=False, closed1=False, vscale=1.0):
    """rings: list of lists of (x, y, z), equal counts, each a closed loop; quads between consecutive rings."""
    n = len(rings[0])
    verts, faces, uvs = [], [], []
    for r in rings:
        verts.extend(r)
    # u along the length, v round the loop (metres)
    acc = [0.0]
    for i in range(1, len(rings)):
        acc.append(acc[-1] + (Vector(rings[i][0]) - Vector(rings[i - 1][0])).length)
    for i in range(len(rings) - 1):
        vv = 0.0
        for j in range(n):
            j2 = (j + 1) % n
            d = (Vector(rings[i][j2]) - Vector(rings[i][j])).length * vscale
            faces.append((i * n + j, i * n + j2, (i + 1) * n + j2, (i + 1) * n + j))
            uvs.append([(acc[i], vv), (acc[i], vv + d), (acc[i + 1], vv + d), (acc[i + 1], vv)])
            vv += d
    for idx, flag in ((0, closed0), (len(rings) - 1, closed1)):
        if flag:
            c = sum((Vector(p) for p in rings[idx]), Vector()) / n
            ci = len(verts)
            verts.append(tuple(c))
            for j in range(n):
                j2 = (j + 1) % n
                faces.append((ci, idx * n + j, idx * n + j2))
                p, q = rings[idx][j], rings[idx][j2]
                uvs.append([(c.y, c.z), (p[1], p[2]), (q[1], q[2])])
    k.add(mat, verts, faces, uvs)


def section(x, R, shape, n, rz=1.0, ry=1.0, cy=0.0, cz=0.0, twist=0.0):
    out = []
    for j in range(n):
        a = TAU * j / n + twist
        r = R * shape(a)
        out.append((x, cy + ry * r * math.cos(a), cz + rz * r * math.sin(a)))
    return out


def jag(ring, axis_sign, amp, seed, rcut=0.12):
    """Torn edge: push points along x by smooth random amounts, nibble the radius."""
    rnd = random.Random(seed)
    n = len(ring)
    ph = [rnd.random() * TAU for _ in range(4)]
    cx = sum(p[0] for p in ring) / n
    cy = sum(p[1] for p in ring) / n
    cz = sum(p[2] for p in ring) / n
    out = []
    for j, (x, y, z) in enumerate(ring):
        a = TAU * j / n
        s = 0.5 + 0.25 * math.sin(3 * a + ph[0]) + 0.15 * math.sin(7 * a + ph[1]) + 0.1 * math.sin(13 * a + ph[2])
        s += 0.35 * (rnd.random() - 0.5)
        f = 1 - rcut * rnd.random()
        out.append((x + axis_sign * amp * s, cy + (y - cy) * f, cz + (z - cz) * f))
    return out


def plate(poly, thick, mat):
    """Prism: polygon in the local (x, z) plane, thickness along y."""
    n = len(poly)
    v = [(p[0], -thick / 2, p[1]) for p in poly] + [(p[0], thick / 2, p[1]) for p in poly]
    f, uv = [], []
    for i in range(n):
        j = (i + 1) % n
        f.append((i, j, n + j, n + i))
        uv.append([(poly[i][0], 0), (poly[j][0], 0), (poly[j][0], thick), (poly[i][0], thick)])
    f.append(tuple(range(n - 1, -1, -1)))
    uv.append([(poly[i][0], poly[i][1]) for i in range(n - 1, -1, -1)])
    f.append(tuple(range(n, 2 * n)))
    uv.append([(p[0], p[1]) for p in poly])
    k.add(mat, v, f, uv)


def rock_mesh(R, seed, sub=4, scale=(1, 1, 1), craters=12, rough=0.35, waist=0.0, mats=("Rock",), split=None,
              hole=None):
    """Displaced icosphere. split(p, n) -> material index per face; hole=(dir, cos) drops faces in that cone."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    rnd = random.Random(seed)
    off = Vector((rnd.uniform(-50, 50), rnd.uniform(-50, 50), rnd.uniform(-50, 50)))
    cr = [(Vector((rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, 1))).normalized(), rnd.uniform(0.12, 0.45),
           rnd.uniform(0.05, 0.12)) for _ in range(craters)]
    for v in bm.verts:
        d = v.co.normalized()
        h = 1.0 + rough * noise.fractal(d * 1.3 + off, 0.55, 2.1, 5) * 0.6
        h += 0.05 * noise.noise(d * 6 + off)
        for c, rad, depth in cr:
            t = math.acos(max(-1, min(1, d.dot(c)))) / rad
            if t < 1.3:
                h -= depth * (1 - t * t) if t < 1 else -depth * 0.25 * (1.3 - t) / 0.3 * 1.0
        p = d * h
        p.x *= scale[0]
        p.y *= scale[1]
        p.z *= scale[2]
        if waist:
            p.y *= 1 - waist * math.exp(-(p.x / 0.35) ** 2)
            p.z *= 1 - waist * math.exp(-(p.x / 0.35) ** 2)
        v.co = p * R
    bm.verts.ensure_lookup_table()
    groups = {}
    for f in bm.faces:
        c = f.calc_center_median()
        if hole and c.normalized().dot(hole[0]) > hole[1]:
            continue
        mi = split(c / R, f.normal) if split else 0
        groups.setdefault(mi, []).append(f)
    for mi, fl in groups.items():
        verts, faces, uvs = [], [], []
        index = {}
        for f in fl:
            nn = f.normal
            ax = max(range(3), key=lambda i: abs(nn[i]))
            a, b = [(1, 2), (0, 2), (0, 1)][ax]
            ids = []
            for v in f.verts:
                if v.index not in index:
                    index[v.index] = len(verts)
                    verts.append(tuple(v.co))
                ids.append(index[v.index])
            faces.append(tuple(ids))
            uvs.append([(v.co[a], v.co[b]) for v in f.verts])
        k.add(mats[mi], verts, faces, uvs)
    bm.free()


# ---------------------------------------------------------------- the models


def derelict_unknown():
    group("derelict_unknown")
    rnd = random.Random(5)

    def shape(a):  # lobed lens: three soft keels
        return 1 + 0.16 * math.cos(3 * a - 3 * PI / 2) + 0.05 * math.cos(6 * a)

    def R(x):  # long body: pointed bow lobe, waist, swollen aft drive bulb
        t = min(1.0, max(0.0, (x + 90) / 180))
        r = 7.5 + 5 * math.sin(PI * t) ** 0.7
        r += 5.5 * math.exp(-((x + 66) / 13) ** 2)      # aft bulb
        r += 2.5 * math.exp(-((x - 52) / 10) ** 2)      # forward swell
        r *= max(0.02, min(1, (92 - x) / 22)) ** 0.55 if x > 70 else 1
        return max(r, 0.4)

    n = 30
    # aft half: x -90 .. -4, torn at the front
    xs = [-90, -88, -84, -78, -70, -62, -54, -44, -34, -24, -14, -7]
    rings = [section(x, R(x) * (0.55 if x == -90 else 1), shape, n, ry=1.25, rz=0.6) for x in xs]
    rings.append(jag(section(-3, R(-3), shape, n, ry=1.25, rz=0.6), +1, 5, 3))
    loft("Hull", rings, closed0=True)
    # inner face of the torn shell (dark), a bulkhead and ribs sticking out
    inner = [section(x, R(x) * 0.93, shape, n, ry=1.25, rz=0.6) for x in (-14, -7)]
    inner.append(jag(section(-4, R(-4) * 0.93, shape, n, ry=1.25, rz=0.6), +1, 4, 3))
    loft("Dark", inner)
    loft("Dark", [section(-16, R(-16) * 0.94, shape, n, ry=1.25, rz=0.6)], closed0=True)
    for j in range(9):
        a = TAU * j / 9 + 0.2
        r = R(-6) * 0.86 * shape(a)
        p0 = Vector((-10, 1.25 * r * math.cos(a), 0.6 * r * math.sin(a)))
        L = rnd.uniform(3, 9)
        k.tube(p0, p0 + Vector((L, rnd.uniform(-1, 1), rnd.uniform(-1, 1))), rnd.uniform(0.25, 0.5), "Metal", seg=5)
    # forward half: x +5 .. +92, drifted and turned
    with k.xf(move(13, 4, 3) @ rotZ(math.radians(11)) @ rotY(math.radians(-7)) @ rotX(math.radians(22))):
        xs = [8, 14, 22, 32, 42, 52, 60, 68, 76, 82, 87, 90, 92]
        rings = [jag(section(3, R(3), shape, n, ry=1.25, rz=0.6), -1, 5, 9)]
        rings += [section(x, R(x), shape, n, ry=1.25, rz=0.6) for x in xs]
        loft("Hull", rings, closed1=True)
        inner = [jag(section(4, R(4) * 0.93, shape, n, ry=1.25, rz=0.6), -1, 4, 9)]
        inner += [section(x, R(x) * 0.93, shape, n, ry=1.25, rz=0.6) for x in (10, 16)]
        loft("Dark", inner)
        loft("Dark", [section(18, R(18) * 0.94, shape, n, ry=1.25, rz=0.6)], closed0=True)
        for j in range(7):
            a = TAU * j / 7 + 0.5
            r = R(8) * 0.86 * shape(a)
            p0 = Vector((12, 1.25 * r * math.cos(a), 0.6 * r * math.sin(a)))
            k.tube(p0, p0 - Vector((rnd.uniform(3, 8), rnd.uniform(-1, 1), rnd.uniform(-1, 1))),
                   rnd.uniform(0.25, 0.5), "Metal", seg=5)
        # sensor blisters along the forward keels (dark glass slits)
        for x in (30, 44, 58, 72):
            for a in (PI / 2, PI / 2 + TAU / 3, PI / 2 - TAU / 3):
                r = R(x) * shape(a)
                k.box((x, r * math.cos(a) * 0.99, 0.6 * r * math.sin(a) * 0.99), (5, 1.2, 1.2), "Glass")
        # the crescent: two swept blades curving forward round the head, one torn short
        for side in (1, -1):
            with k.xf(rotX(-side * PI / 2)):
                rb = R(40) * 1.1
                if side > 0:
                    poly = [(26, rb), (50, rb), (64, rb + 14), (84, rb + 18), (96, rb + 10), (90, rb + 22),
                            (70, rb + 30), (48, rb + 26), (34, rb + 12)]
                else:
                    poly = [(26, rb), (50, rb), (62, rb + 13), (68, rb + 17), (63, rb + 24), (56, rb + 21),
                            (48, rb + 26), (34, rb + 12)]
                plate(poly, 1.4, "Hull")
                plate([(30, rb - 1), (46, rb - 1), (44, rb + 8), (34, rb + 8)], 2.4, "Hull")
                k.box((60, 0, rb + 12), (14, 1.6, 0.8), "Glass")
        # forward fins: three short swept blades on the keels
        for a in (PI / 2, PI / 2 + TAU / 3, PI / 2 - TAU / 3):
            with k.xf(rotX(a - PI / 2)):
                r = R(50) * shape(PI / 2) * 0.6
                plate([(38, r - 1), (62, r - 1), (54, r + 5), (44, r + 6)], 0.8, "Hull")
    # aft vanes: three long swept fins off the drive bulb
    for i, a in enumerate((PI / 2, PI / 2 + TAU / 3, PI / 2 - TAU / 3)):
        with k.xf(rotX(a - PI / 2)):
            r = R(-66) * shape(PI / 2) * 0.6
            if i == 1:  # one snapped off short
                plate([(-82, r - 2), (-50, r - 2), (-58, r + 6), (-70, r + 9), (-80, r + 5)], 1.0, "Hull")
            else:
                plate([(-86, r - 2), (-48, r - 2), (-64, r + 12), (-92, r + 20), (-94, r + 16)], 1.0, "Hull")
    # drive: a ring of four cold emitter bells in the aft cap
    for j in range(4):
        a = TAU * j / 4 + PI / 4
        k.lathe([(-89, 0), (-89, 2.4), (-94, 3.4), (-95.5, 3.1), (-90.5, 1.8), (-90.5, 0)], "Dark", seg=16,
                oy=5 * math.cos(a), oz=3.6 * math.sin(a))
    # a scatter of small torn plates drifting in the gap
    for i in range(6):
        c = Vector((rnd.uniform(-2, 4), rnd.uniform(-10, 10), rnd.uniform(-7, 7)))
        with k.xf(move(*c) @ rotX(rnd.uniform(0, TAU)) @ rotY(rnd.uniform(0, TAU))):
            s = rnd.uniform(1.2, 2.6)
            plate([(-s, -s * 0.6), (s, -s * 0.8), (s * 0.7, s * 0.7), (-s * 0.9, s)], 0.25, "Hull")
    # the one faint light, aft dorsal
    node("Light0", "derelict_unknown", (-74, 0, R(-74) * 0.6 * shape(PI / 2) + 0.4))
    group("Light0")
    k.sphere((0, 0, 0), 0.45, "Lamp", seg=8, rings=5)
    k.box((0, 0, -0.35), (1.4, 0.9, 0.5), "Metal")


def derelict_earth():
    g = "derelict_earth"
    group(g)
    rnd = random.Random(12)
    # spine: square truss from the drive section to the crew module
    s = 2.2
    corners = [(s, s), (s, -s), (-s, -s), (-s, s)]
    x0, x1 = -46, 30
    for (y, z) in corners:
        k.tube((x0, y, z), (x1, y, z), 0.35, "Metal", seg=6)
    nseg = 12
    for i in range(nseg + 1):
        x = x0 + (x1 - x0) * i / nseg
        for c in range(4):
            a, b = corners[c], corners[(c + 1) % 4]
            k.tube((x, *a), (x, *b), 0.18, "Metal", seg=4)
            if i < nseg:
                xn = x0 + (x1 - x0) * (i + 1) / nseg
                k.tube((x, *a), (xn, *b), 0.12, "Metal", seg=4)
    # crew module: domed pressure hull, orange bands, dark windows
    k.dome_tank(30, 56, 5.2, "HullWhite", k=0.7, seg=28)
    for x in (34, 47):
        k.cyl(x, x + 0.8, 5.35, 5.35, "Orange", seg=28, c0=False, c1=False)
    for j in range(8):
        a = TAU * j / 8
        k.box((51, 5.0 * math.cos(a), 5.0 * math.sin(a)), (1.6, 0.6, 0.6), "Glass")
    k.can(56, 58.5, 1.6, "Metal", seg=12)            # docking port in the nose
    k.cyl(28, 30, 2.6, 4.2, "HullGrey", seg=20, c0=True, c1=False)
    # torn hab ring: two arcs left of a full ring, on three spokes
    with k.xf(move(20, 0, 0)):
        R, r = 17, 2.2
        prof = [(r * math.sin(TAU * j / 10), R + r * math.cos(TAU * j / 10)) for j in range(10)]
        k.lathe(prof, "HullWhite", seg=40, a0=0.3, a1=3.6, closed=True, caps=True)
        k.lathe(prof, "HullWhite", seg=16, a0=4.3, a1=5.4, closed=True, caps=True)
        for a in (0.9, 3.0, 4.9):
            k.tube((0, 3 * math.cos(a), 3 * math.sin(a)), (0, (R - 2) * math.cos(a), (R - 2) * math.sin(a)), 0.6,
                   "HullGrey", seg=8)
        k.tube((0, 3 * math.cos(2.0), 3 * math.sin(2.0)), (0, 8 * math.cos(2.0), 8 * math.sin(2.0)), 0.6, "HullGrey",
               seg=8)                                # a spoke snapped half way
        k.cyl(-2, 2, 3.4, 3.4, "HullGrey", seg=20)
        for a in (0.3, 3.6, 4.3, 5.4):               # torn ring ends (dark)
            k.box((0, (R) * math.cos(a), R * math.sin(a)), (4.6, 0.9, 4.6), "Dark")
    # tank farm: four foil tanks round the spine
    for j in range(4):
        a = TAU * j / 4 + PI / 4
        oy, oz = 6.2 * math.cos(a), 6.2 * math.sin(a)
        k.dome_tank(-22, -2, 3.0, "Foil" if j % 2 == 0 else "FoilSilver", seg=18, oy=oy, oz=oz)
        k.cyl(-12.3, -11.7, 3.1, 3.1, "Metal", seg=18, oy=oy, oz=oz, c0=False, c1=False)
    # radiators on booms: one bent away
    for side, bend in ((1, 0.0), (-1, 0.45)):
        with k.xf(move(-30, side * 2.2, 0) @ rotX(side * bend)):
            k.tube((0, 0, 0), (0, side * 6, 0), 0.4, "Metal", seg=6)
            k.box((0, side * 15, 0), (12, 18, 0.25), "HullGrey")
            for i in range(5):
                k.box((-6 + i * 3, side * 15, 0.15), (0.3, 18, 0.12), "Dark")
    # drive section: reactor drum, aft bell, hazard ring
    k.can(-58, -46, 4.6, "HullGrey", seg=24)
    k.cyl(-50, -49.2, 4.75, 4.75, "Orange", seg=24, c0=False, c1=False)
    k.lathe([(-58, 0), (-58, 2.0), (-63, 3.8), (-66, 4.6), (-65.6, 4.8), (-60, 2.4)], "Dark", seg=24)
    for j in range(4):
        a = TAU * j / 4
        k.box((-54, 5.0 * math.cos(a), 5.0 * math.sin(a)), (3, 1.2, 1.2), "Metal")
    # dead nav lamps (dark lenses)
    k.box((54, 0, 5.4), (0.6, 0.6, 0.4), "Dark")
    # loose cable trailing from the ring
    pts = [(20, 17 * math.cos(3.6), 17 * math.sin(3.6))]
    for i in range(5):
        pts.append((pts[-1][0] + rnd.uniform(-2, 1), pts[-1][1] + rnd.uniform(-2.5, 0.5), pts[-1][2] - 2))
    k.pipe(pts, 0.25, "Dark", seg=4)


def derelict_trader():
    group("derelict_trader")

    def shape(a):  # rounded rectangle-ish: squarish loop
        c, s = abs(math.cos(a)), abs(math.sin(a))
        return 1.0 / max(c, s) ** 0.85 / 1.08

    n = 32
    prof = [(-34, 9, 0.7), (-30, 11, 0.8), (-10, 12, 0.85), (10, 12, 0.85), (24, 11, 0.8), (36, 8.5, 0.7),
            (44, 5.5, 0.6), (48, 2.4, 0.5)]
    rings = [section(x, r, shape, n, ry=1.35, rz=rz) for x, r, rz in prof]
    loft("HullTrader", rings, closed0=True, closed1=True)
    # dorsal cargo bay: doors swung open, dark hold
    k.box((0, 0, 9.0), (34, 16, 1.0), "Dark")
    for side in (1, -1):
        with k.xf(move(0, side * 8.2, 9.6) @ rotX(side * math.radians(75))):
            k.box((0, side * 0, 4.0), (32, 0.5, 8.0), "HullTrader")
    # bridge blister forward, windows dark
    k.sphere((30, 0, 7.5), 3.6, "HullTrader", seg=18, rings=8, sx=1.5)
    k.box((34.5, 0, 8.6), (1.2, 4.2, 0.9), "Glass")
    # engine pods on pylons
    for side in (1, -1):
        y = side * 21
        k.can(-44, -8, 4.0, "HullTrader", seg=20, oy=y, oz=-1)
        k.cyl(-8, -2, 4.0, 2.0, "HullTrader", seg=20, oy=y, oz=-1, c0=False)
        k.lathe([(-44, 0), (-44, 3.0), (-49, 3.8), (-49.3, 3.6), (-45.5, 2.2)], "Dark", seg=20, oy=y, oz=-1)
        k.cyl(-30, -28.5, 4.15, 4.15, "Metal", seg=20, oy=y, oz=-1, c0=False, c1=False)
        with k.xf(move(-26, side * 13, -1)):
            k.box((0, side * 2, 0), (12, 5, 1.4), "HullTrader")
    # a hole torn in the port flank: dark patch with bent plates
    k.box((-4, 16.2, -2), (9, 0.6, 5), "Dark")
    rnd = random.Random(33)
    for i in range(4):
        with k.xf(move(-4 + rnd.uniform(-4, 4), 16.6, -2 + rnd.uniform(-2, 2)) @ rotZ(rnd.uniform(-0.6, 0.6))):
            plate([(-1.5, -1), (1.5, -1.2), (1.2, 1.4), (-1.2, 1)], 0.2, "HullTrader")
    # cargo container still clamped in the hold
    k.box((-6, 0, 10.4), (9, 4, 3.2), "Orange")
    k.box((5, 2, 10.4), (9, 4, 3.2), "HullGrey")


def derelict_native():
    group("derelict_native")

    def shape(a):
        return 1 + 0.22 * math.cos(3 * a + PI / 2)

    n = 27
    prof = [(-26, 3.5), (-24, 6.0), (-18, 7.0), (-6, 7.4), (8, 6.8), (18, 6.0), (26, 4.4), (31, 2.6), (34, 0.6)]
    rings = [section(x, r, shape, n) for x, r in prof]
    loft("HullNative", rings, closed0=True, closed1=True)
    for x in (-14, 2, 16):
        k.cyl(x, x + 1.2, 1, 1, "NativeTrim", seg=n, c0=False, c1=False)
        rings = [section(x + d, 7.7 - abs(x) * 0.03, shape, n) for d in (0, 1.2)]
        loft("NativeTrim", rings)
    # three outrigger coils on swept struts
    for j in range(3):
        a = TAU * j / 3 + PI / 6 + PI / 2
        oy, oz = 13 * math.cos(a), 13 * math.sin(a)
        k.tube((-6, 6 * math.cos(a), 6 * math.sin(a)), (-12, oy, oz), 0.9, "NativeTrim", seg=8)
        k.tube((8, 5.5 * math.cos(a), 5.5 * math.sin(a)), (2, oy, oz), 0.7, "NativeTrim", seg=8)
        k.can(-22, 8, 2.2, "HullNative", seg=16, oy=oy, oz=oz)
        for x in (-18, -12, -6, 0):
            k.cyl(x, x + 2, 2.5, 2.5, "NativeTrim", seg=16, oy=oy, oz=oz, c0=False, c1=False)
        k.sphere((8, oy, oz), 2.2, "HullNative", seg=14, rings=6, sx=1.4)
    # aft: three bells
    for j in range(3):
        a = TAU * j / 3 + PI / 2
        k.lathe([(-26, 0), (-26, 1.8), (-31, 3.0), (-31.3, 2.8), (-27, 1.2)], "Dark", seg=14,
                oy=3 * math.cos(a), oz=3 * math.sin(a))
    # cockpit band (dark glass)
    rings = [section(x, r * 1.005, shape, n) for x, r in ((24, 4.85), (27.5, 3.9))]
    loft("Glass", rings)


def debris_a():  # curved hull plate with ribs, torn edges
    group("debris_piece_a")
    R, L, n = 12, 14, 12
    rnd = random.Random(41)
    for (rr, mat) in ((R, "HullWhite"), (R - 0.4, "Dark")):
        rings = []
        for i in range(6):
            x = -L / 2 + L * i / 5
            ring = []
            for j in range(n):
                a = -0.55 + 1.1 * j / (n - 1)
                e = 1.0 if 0 < i < 5 else 1.0 + 0.0
                ring.append((x + (rnd.uniform(-0.8, 0.8) if i in (0, 5) else 0), rr * math.cos(a) * e - R,
                             rr * math.sin(a)))
            rings.append(ring)
        # open strip (not closed round): build quads manually
        verts, faces, uvs = [], [], []
        for r in rings:
            verts.extend(r)
        for i in range(5):
            for j in range(n - 1):
                faces.append((i * n + j, i * n + j + 1, (i + 1) * n + j + 1, (i + 1) * n + j))
                uvs.append([(i * 2.8, j), (i * 2.8, j + 1), ((i + 1) * 2.8, j + 1), ((i + 1) * 2.8, j)])
        k.add(mat, verts, faces, uvs)
    for x in (-4.5, 0, 4.5):
        k.lathe([(x - 0.3, R - 1.2), (x + 0.3, R - 1.2), (x + 0.3, R - 0.35), (x - 0.3, R - 0.35)], "Metal", seg=10,
                a0=-0.55, a1=0.55, oy=-R, closed=True, caps=True)
    k.box((0, 0.3, 0), (5, 0.3, 0.6), "Orange")


def debris_b():  # twisted truss section
    group("debris_piece_b")
    s, L, nseg = 1.6, 18, 8
    pts = []
    for i in range(nseg + 1):
        x = -L / 2 + L * i / nseg
        tw = 0.9 * i / nseg
        bend = 0.04 * (x ** 2) / 10
        pts.append([(x, bend + s * math.cos(tw + TAU * c / 3), s * math.sin(tw + TAU * c / 3)) for c in range(3)])
    for c in range(3):
        k.pipe([p[c] for p in pts], 0.28, "Metal", seg=5, elbow=False)
    for i in range(nseg):
        for c in range(3):
            k.tube(pts[i][c], pts[i][(c + 1) % 3], 0.13, "Metal", seg=4)
            k.tube(pts[i][c], pts[i + 1][(c + 1) % 3], 0.1, "Metal", seg=4)
    k.box((2, pts[5][0][1], 0), (3, 1.2, 1.2), "Orange")


def debris_c():  # nacelle stub, torn at the front
    group("debris_piece_c")
    n = 20

    def circ(a):
        return 1.0
    rings = [section(-12, 1.6, circ, n), section(-10, 3.0, circ, n), section(4, 3.0, circ, n)]
    rings.append(jag(section(9, 3.0, circ, n), +1, 3, 77, rcut=0.2))
    loft("HullWhite", rings, closed0=True)
    loft("Dark", [section(-6, 2.8, circ, n), section(4, 2.8, circ, n), jag(section(8, 2.8, circ, n), +1, 2.5, 77)])
    loft("Dark", [section(-6, 2.85, circ, n)], closed0=True)
    for x in (-6, -1, 3):
        k.cyl(x, x + 0.7, 3.08, 3.08, "Orange" if x == -1 else "Metal", seg=n, c0=False, c1=False)
    k.box((-4, 0, -3.6), (8, 1.2, 1.4), "HullGrey")                # pylon root
    k.tube((-8, 0, -3.6), (-11, 0.5, -6.5), 0.5, "Metal", seg=6)   # snapped strut
    rnd = random.Random(78)
    for j in range(6):
        a = rnd.uniform(0, TAU)
        k.tube((7, 2.6 * math.cos(a), 2.6 * math.sin(a)),
               (11 + rnd.uniform(0, 2), 3.2 * math.cos(a + 0.2), 3.2 * math.sin(a + 0.2)), 0.15, "Metal", seg=4)


def debris_d():  # hull chunk with frame ribs
    group("debris_piece_d")
    n = 8

    def sq(a):
        c, s = abs(math.cos(a)), abs(math.sin(a))
        return 1.0 / max(c, s)
    rings = [jag(section(-5, 4, sq, n, twist=PI / 8), -1, 3, 91, rcut=0.3), section(0, 4.3, sq, n, twist=PI / 8),
             jag(section(5, 4, sq, n, twist=PI / 8), +1, 3, 92, rcut=0.3)]
    loft("HullGrey", rings)
    loft("Dark", [[(x, y * 0.92, z * 0.92) for (x, y, z) in r] for r in rings])
    for x in (-3, 1.5):
        k.box((x, 0, 0), (0.5, 7, 7), "Metal")
    k.box((0, 0, 0), (0.5, 3, 3), "Dark")
    rnd = random.Random(93)
    for j in range(5):
        k.tube((rnd.uniform(-3, 3), rnd.uniform(-3, 3), rnd.uniform(-3, 3)),
               (rnd.uniform(-8, 8), rnd.uniform(-5, 5), rnd.uniform(-5, 5)), 0.18, "Metal", seg=4)


def debris_e():  # cargo pod (CargoPod)
    group("debris_piece_e")
    node("CargoPod", "debris_piece_e")
    group("CargoPod")
    k.box((0, 0, 0), (8, 3.2, 3.2), "HullWhite")
    for x in (-3.6, 3.6):
        k.box((x, 0, 0), (0.8, 3.4, 3.4), "Orange")
    for x in (-1.2, 1.2):
        k.box((x, 0, 0), (0.2, 3.3, 3.3), "Metal")
    for (y, z) in ((1.7, 1.7), (-1.7, 1.7), (1.7, -1.7), (-1.7, -1.7)):
        k.box((0, y, z), (6.4, 0.3, 0.3), "Metal")
    k.box((0, 0, 1.7), (2.4, 1.2, 0.2), "Dark")          # data plate
    k.box((-4.1, 0.9, 1.0), (0.3, 0.5, 0.5), "Lamp")      # status lamp


def debris_f():  # black box (BlackBox -> Beacon)
    group("debris_piece_f")
    node("BlackBox", "debris_piece_f")
    group("BlackBox")
    k.box((0, 0, 0), (1.5, 0.9, 0.7), "Orange")
    for x in (-0.5, 0.5):
        k.box((x, 0, 0), (0.12, 0.95, 0.75), "Dark")
    k.box((0, 0, 0.36), (0.8, 0.5, 0.04), "HullWhite")
    k.tube((0.55, 0.3, 0.35), (0.55, 0.3, 1.3), 0.03, "Metal", seg=4)
    node("Beacon", "BlackBox", (0.55, 0.3, 1.36))
    group("Beacon")
    k.sphere((0, 0, 0), 0.09, "Beacon", seg=8, rings=4)


def asteroid_common():
    group("asteroid_common")
    rock_mesh(70, seed=101, sub=5, scale=(1.4, 0.95, 0.8), craters=16)


def asteroid_metal():
    group("asteroid_metal")

    def split(p, nn):
        return 1 if noise.noise(p * 3.2 + Vector((7, 3, 1))) > 0.25 else 0
    rock_mesh(58, seed=202, sub=5, scale=(1.15, 1.0, 0.9), craters=10, rough=0.45, mats=("Rock", "Metal"), split=split)


def asteroid_ice():
    group("asteroid_ice")

    def split(p, nn):
        return 1 if noise.noise(p * 2.5 + Vector((1, 9, 4))) > 0.35 else 0
    rock_mesh(55, seed=303, sub=5, scale=(1.3, 0.9, 1.0), craters=8, rough=0.28, mats=("Ice", "Rock"), split=split)


def asteroid_hollow():
    g = "asteroid_hollow"
    group(g)
    R = 110
    rock_mesh(R, seed=404, sub=5, scale=(1.1, 1.0, 0.85), craters=14, rough=0.3,
              hole=(Vector((1, 0, 0)), math.cos(0.16)))
    # the cavity: a rocky throat into the asteroid, ending in a chamber
    mx = R * 1.1 * 0.80
    prof = [(mx + 18, 30), (mx + 6, 22), (mx - 10, 17), (mx - 40, 14), (mx - 70, 16), (mx - 85, 10), (mx - 90, 0)]
    k.lathe(prof, "Rock", seg=28)
    # rim: a lumpy collar to blend the throat into the surface
    k.torus(mx + 12, 26, 8, "Rock", seg=28, tseg=8)
    node("Mouth", g, (mx + 18, 0, 0))
    # something built inside: an old platform and one dim lamp deep in
    k.box((mx - 60, 0, -10), (16, 10, 1.0), "HullGrey")
    k.box((mx - 62, 2, -7), (5, 4, 5), "HullGrey")
    k.tube((mx - 56, -3, -9.5), (mx - 56, -3, 2), 0.3, "Metal", seg=5)
    node("Light0", g, (mx - 56, -3, 2.4))
    group("Light0")
    k.sphere((0, 0, 0), 0.5, "Lamp", seg=8, rings=4)


def lifepod():
    group("lifepod")
    # capsule axis along x (nose forward): heat shield aft, conical white body, hatch on the nose
    k.lathe([(-1.6, 0), (-1.6, 1.9), (-1.45, 2.15), (-1.25, 2.2)], "Dark", seg=24)
    k.lathe([(-1.25, 2.2), (1.6, 1.15), (1.9, 0.9)], "HullWhite", seg=24)
    k.lathe([(-1.0, 2.13), (-0.6, 1.97)], "Orange", seg=24)
    k.can(1.9, 2.6, 0.7, "Metal", seg=16)
    for j in range(4):
        a = TAU * j / 4 + PI / 4
        k.box((0.4, 1.68 * math.cos(a), 1.68 * math.sin(a)), (0.3, 0.3, 0.3), "Dark")   # thrusters
    k.box((0.6, 0, 1.6), (0.7, 0.5, 0.1), "Glass")   # window
    node("Beacon", "lifepod", (2.0, 0, 0.85))
    group("Beacon")
    k.box((0, 0, -0.08), (0.2, 0.2, 0.16), "Metal")
    k.sphere((0, 0, 0.06), 0.11, "Beacon", seg=8, rings=4)


def comet_nucleus():
    g = "comet_nucleus"
    group(g)

    def split(p, nn):
        return 1 if noise.noise(p * 2.2 + Vector((3, 3, 8))) > 0.42 else 0
    rock_mesh(140, seed=505, sub=5, scale=(1.5, 0.9, 0.85), craters=18, rough=0.4, waist=0.28,
              mats=("CometRock", "Ice"), split=split)
    # vent points: local +Z (Blender) = Godot +Y points out of the surface
    for i, d in enumerate([(0.4, 0.3, 0.86), (-0.6, -0.5, 0.6), (0.9, -0.2, -0.4)]):
        d = Vector(d).normalized()
        p = Vector((d.x * 1.5 * 140 * 0.92, d.y * 0.9 * 140 * 0.92, d.z * 0.85 * 140 * 0.92))
        node("Jet%d" % i, g, p, rot=d.to_track_quat("Z", "Y").to_matrix())


def station():
    g = "station"
    group(g)
    # hub along the axis: docking node forward, hab can, power truss aft
    k.can(6, 22, 4.2, "HullWhite", seg=24)
    k.dome_tank(20, 30, 3.4, "HullWhite", seg=24)
    k.can(30, 32.5, 1.4, "Metal", seg=12)                         # forward axial port
    k.can(-6, 6, 3.0, "HullGrey", seg=20)                          # ring bearing
    k.can(-24, -6, 3.6, "HullWhite", seg=24)
    for x in (10, 17, -15):
        k.cyl(x, x + 0.7, (4.35 if x > 0 else 3.75), (4.35 if x > 0 else 3.75), "Orange", seg=24, c0=False, c1=False)
    for x in (8, 12, 16, 20):
        for j in range(6):
            a = TAU * j / 6
            k.box((x, 4.15 * math.cos(a), 4.15 * math.sin(a)), (1.0, 0.5, 0.5), "Lamp")
    # foil tanks and the power truss with two solar wings
    for j in range(3):
        a = TAU * j / 3 + 0.3
        k.dome_tank(-22, -10, 1.6, "Foil", seg=14, oy=5.2 * math.cos(a), oz=5.2 * math.sin(a))
    for side in (1, -1):
        k.tube((-30, 0, 0), (-30, side * 34, 0), 0.6, "Metal", seg=6)
        for i in range(3):
            k.box((-30, side * (10 + i * 8.5), 0), (14, 7.6, 0.2), "Solar")
    k.can(-34, -24, 2.0, "HullGrey", seg=16)
    # radiators
    for side in (1, -1):
        k.box((-12, 0, side * 10), (8, 0.2, 12), "HullGrey")
        k.tube((-12, 0, side * 3.6), (-12, 0, side * 4.2), 0.3, "Metal", seg=5)
    # berths: two radial docking ports, ventral (Berth0) and dorsal (Berth1)
    for i, side in enumerate((-1, 1)):
        with k.xf(move(14, 0, side * 4.0)):
            k.tube((0, 0, 0), (0, 0, side * 2.6), 1.5, "HullGrey", seg=12, caps=True)
        k.box((14 + 1.8, 0, side * 6.4), (0.4, 0.4, 0.4), "Lamp")
        rot = None if side < 0 else Matrix.Rotation(PI, 3, "X")
        node("Berth%d" % i, g, (14, 0, side * 6.6), rot=rot)
    node("Beacon", g, (32.8, 0, 0))
    group("Beacon")
    k.sphere((0, 0, 0), 0.35, "Beacon", seg=8, rings=4)
    # the spinning ring: pressure torus, spokes, collar
    node("Ring", g, (0, 0, 0), spin=0.24)
    group("Ring")
    R, r = 27, 2.8
    k.torus(0, R, r, "HullWhite", seg=56, tseg=12)
    k.torus(0, R + r * 0.98, 0.35, "Orange", seg=56, tseg=4)
    for j in range(4):
        a = TAU * j / 4
        k.tube((0, 3.1 * math.cos(a), 3.1 * math.sin(a)), (0, (R - r) * math.cos(a), (R - r) * math.sin(a)), 0.9,
               "HullGrey", seg=8)
    k.cyl(-2.5, 2.5, 3.6, 3.6, "HullGrey", seg=24)
    for j in range(24):
        a = TAU * (j + 0.5) / 24
        k.box((r * 0.45, (R + r * 0.86) * math.cos(a), (R + r * 0.86) * math.sin(a)), (0.5, 1.4, 1.4), "Lamp")


def probe_alien():
    g = "probe_alien"
    group(g)

    def shape(a):
        return 1 + 0.25 * math.cos(3 * a)
    n = 24
    prof = [(-4, 0.4), (-3.4, 1.6), (-1.8, 2.5), (0, 2.8), (2, 2.4), (4, 1.4), (5.5, 0.3)]
    rings = [section(x, r, shape, n) for x, r in prof]
    loft("Hull", rings, closed0=True, closed1=True)
    for j in range(3):
        a = TAU * j / 3
        c, s = math.cos(a), math.sin(a)
        k.tube((-1, 2.8 * c, 2.8 * s), (-7.5, 6.5 * c, 6.5 * s), 0.45, "Hull", seg=6, r2=0.05)
        k.tube((3, 2.2 * c, 2.2 * s), (7.5, 1.2 * c, 1.2 * s), 0.25, "Metal", seg=5, r2=0.03)
        k.sphere((-1.0, 2.9 * c, 2.9 * s), 0.6, "Hull", seg=10, rings=5)
    node("Core", g, (0, 0, 0))
    group("Core")
    k.torus(0, 2.95, 0.32, "Glow", seg=36, tseg=6)
    k.sphere((5.4, 0, 0), 0.4, "Glow", seg=10, rings=5)


BUILDERS = {
    "derelict_unknown": derelict_unknown, "derelict_earth": derelict_earth, "derelict_trader": derelict_trader,
    "derelict_native": derelict_native, "debris_piece_a": debris_a, "debris_piece_b": debris_b,
    "debris_piece_c": debris_c, "debris_piece_d": debris_d, "debris_piece_e": debris_e, "debris_piece_f": debris_f,
    "asteroid_common": asteroid_common, "asteroid_metal": asteroid_metal, "asteroid_ice": asteroid_ice,
    "asteroid_hollow": asteroid_hollow, "lifepod": lifepod, "comet_nucleus": comet_nucleus, "station": station,
    "probe_alien": probe_alien,
}


# ---------------------------------------------------------------- assembly


def world_of(name):
    """Kit-frame world location of a node (empties nest)."""
    par, loc, rot, _ = EMPTIES[name]
    if par in EMPTIES:
        return world_of(par) + loc
    return loc


def assemble(names):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    coll = bpy.context.scene.collection
    for nm in names:
        node(nm, None)
        group(nm)
        BUILDERS[nm]()
        group("")
    # bake the kit frame into Blender's (x fwd -> +Y)
    for b in k.B.values():
        b["v"] = [(-y, x, z) for (x, y, z) in b["v"]]
    R3 = KIT_TO_BL.to_3x3()
    objs = {}
    for name, (par, loc, rot, props) in EMPTIES.items():
        e = bpy.data.objects.new(name, None)
        e.empty_display_size = 1.0
        coll.objects.link(e)
        objs[name] = e
        for kk, vv in props.items():
            e[kk] = vv
    for name, (par, loc, rot, props) in EMPTIES.items():
        e = objs[name]
        wl = R3 @ world_of(name)
        M = Matrix.Translation(wl)
        if rot is not None:
            M = M @ (R3 @ rot @ R3.inverted()).to_4x4()
        e.matrix_world = M
        if par:
            e.parent = objs[par]
            e.matrix_parent_inverse = objs[par].matrix_world.inverted()
    # a node's own parts are built in its local frame (child groups) and hang under it unmoved
    meshes = k.build(MATS, coll, prefix="", bevels=False, uvscale=UVSCALE, parents=objs)
    return objs, meshes


def tri_count(root):
    t = 0
    for o in bpy.data.objects:
        if o.type == "MESH" and is_under(o, root):
            t += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return t


def is_under(o, root):
    while o is not None:
        if o is root:
            return True
        o = o.parent
    return False


def bounds(root):
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in bpy.data.objects:
        if o.type == "MESH" and is_under(o, root):
            for c in o.bound_box:
                w = o.matrix_world @ Vector(c)
                lo = Vector(map(min, lo, w))
                hi = Vector(map(max, hi, w))
    return lo, hi


def fix_glb_names(path):
    """Blender keeps names unique ("Beacon.001"); the game wants plain Beacon, Light0, ... under each root."""
    with open(path, "rb") as f:
        data = f.read()
    magic, version, _ = struct.unpack_from("<III", data, 0)
    jlen, jtype = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + jlen].decode("utf-8"))
    rest = data[20 + jlen:]
    for n in doc.get("nodes", []):
        n["name"] = re.sub(r"\.\d{3}$", "", n.get("name", ""))
    js = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)
    out = struct.pack("<III", magic, version, 12 + 8 + len(js) + len(rest)) + struct.pack("<II", len(js), jtype) + js + rest
    with open(path, "wb") as f:
        f.write(out)


def export(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=False, export_apply=True,
                              export_yup=True, export_texcoords=True, export_normals=True, export_tangents=False,
                              export_materials="EXPORT", export_image_format="AUTO", export_animations=False,
                              export_skins=False, export_morph=False, export_cameras=False, export_lights=False,
                              export_extras=True)
    fix_glb_names(path)
    print(f"exported {path} ({os.path.getsize(path) / 1024 / 1024:.2f} MB)")


# ---------------------------------------------------------------- previews (Cycles)


def preview(objs, names, folder):
    scn = bpy.context.scene
    scn.render.engine = "CYCLES"
    scn.cycles.samples = int(arg("--spp", 32))
    scn.cycles.use_denoising = True
    try:
        scn.cycles.denoiser = "OPENIMAGEDENOISE"
    except Exception:
        pass
    scn.cycles.max_bounces = 4
    w, h = (int(v) for v in str(arg("--res", "800x480")).split("x"))
    scn.render.resolution_x, scn.render.resolution_y = w, h
    scn.render.film_transparent = False
    scn.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("Space")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0, 0, 0, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0
    scn.world = world
    sun = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "SUN"))
    sun.data.energy = 4.5
    sun.data.angle = math.radians(0.6)
    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
    fill.data.energy = 0.25
    fill.data.color = (0.55, 0.65, 1.0)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = 60
    cam.data.clip_start = 0.05
    cam.data.clip_end = 20000
    for o in (sun, fill, cam):
        scn.collection.objects.link(o)
    scn.camera = cam
    os.makedirs(folder, exist_ok=True)
    for nm in names:
        root = objs[nm]
        for o in bpy.data.objects:
            if o.type == "MESH":
                o.hide_render = not is_under(o, root)
        lo, hi = bounds(root)
        c = (lo + hi) / 2
        rad = (hi - lo).length / 2
        # view from forward-right-above (bow is Blender +Y)
        d = Vector((0.9, 0.3, 0.42)).normalized()
        q = (-d).to_track_quat("-Z", "Y")
        right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
        th = 18 / cam.data.lens          # half the 36 mm sensor width over the lens
        tv = th * h / w
        dist = 0.0
        for cx in (lo.x, hi.x):
            for cy in (lo.y, hi.y):
                for cz in (lo.z, hi.z):
                    p = Vector((cx, cy, cz)) - c
                    z = p.dot(d)
                    dist = max(dist, abs(p.dot(right)) / th + z, abs(p.dot(up)) / tv + z)
        dist *= 1.1
        cam.location = c + d * dist
        cam.rotation_euler = q.to_euler()
        cam.data.clip_start = max(0.01, dist - rad * 2)
        sdir = Vector((0.9, 0.2, 0.55)).normalized()
        sun.rotation_euler = (-sdir).to_track_quat("-Z", "Y").to_euler()
        fdir = Vector((-0.7, -0.3, -0.4)).normalized()
        fill.rotation_euler = (-fdir).to_track_quat("-Z", "Y").to_euler()
        scn.render.filepath = os.path.join(folder, nm + ".png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scn.render.filepath)


# ---------------------------------------------------------------- main


def main():
    only = arg("--only")
    names = [n for n in ORDER if not only or n in only.split(",")]
    make_materials()
    objs, _ = assemble(names)
    report = {}
    for nm in names:
        lo, hi = bounds(objs[nm])
        size = hi - lo
        report[nm] = (round(size.x, 1), round(size.z, 1), round(size.y, 1), tri_count(objs[nm]))
    total = 0
    for nm, (sx, sy, sz, t) in report.items():
        total += t
        print(f"{nm:18s} {sx:7.1f} x {sy:7.1f} x {sz:7.1f} m (Godot X x Y x Z)  {t:7d} tris")
    print("total tris", total)
    if arg("--game"):
        out = arg("--out", os.path.join(ROOT, "game", "models", "space", "sites.glb"))
        export(out)
    if arg("--preview"):
        preview(objs, names, arg("--preview"))


if __name__ == "__main__":
    main()
