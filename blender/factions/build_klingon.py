"""Klingon ground kit, Blender mockups: the Klingon counterpart to the Federation vehicle set (blender/build_vehicles.py).

Original designs in the Klingon style: dark gunmetal hulls with oxidised bronze-green armour plates, angular facets,
swept wing-like fins, red running lights, glowing red-orange window slits and a three-bladed emblem (three swept
daggers in a broken ring). Plain materials only (no paint bake): these are look mockups, not game exports yet.

Run with the PyPI bpy module (one render at a time, the CPU is shared):
  /root/bpyenv/bin/python blender/factions/build_klingon.py --model rover --preview blender/previews/factions/klingon --res 480 --samples 8
  ... --model miner [--pose stowed|deployed]
  ... --model dropship            (carries the Klingon rover in its cargo clamps)
  ... --model outpost --level 1|5|10
  ... --model fedref              (the Federation Mk I Scout from build_vehicles.py, same camera as the Klingon rover)
  ... --shots front34,game        (also side, rear34)   --blank (decal markers only, no painted name/number)
  ... --out file.glb              (optional glTF export of the model)
  python3 blender/factions/build_klingon.py --sheet blender/previews/factions/klingon     (PIL contact sheet, no bpy)

Game conventions kept so the models could go in later: Blender +Y is the front, +Z up, units = world units (Godot);
material "Owner" is the player colour; "Dish" is the sensor head the game spins; "Beacon" is the roof beacon; decal
markers NameL / NameR / NumberL / NumberR are empties with build_vehicles' text matrix (text in the local XY plane facing
local +Z, reading along +X; Blender scale (max_width, size, 1)). The miner keeps the crawler's part names (Leg0-3, Boom,
Drill, String, Bit, Fill); the drop ship's cargo clamps are Clamp0-3.

Helpers (box, cylinder, strut, text, material, empty, mesh_object, palette, survey_rover) come from build_vehicles.py,
exec'd with its trailing main() stripped (see render_finish.py), so its own flags never run.
"""
import math
import os
import re
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.path.dirname(HERE)

# ---------------------------------------------------------------- contact sheet (PIL only, no bpy)

SHEET = [
    ("fed-mk1-front34.png", "Federation Mk I Scout (reference, same camera)"),
    ("klingon-rover-front34.png", "Klingon survey rover"),
    ("klingon-rover-game.png", "Survey rover - game camera"),
    ("klingon-miner-front34.png", "Mining vehicle - stowed"),
    ("klingon-miner-deployed-front34.png", "Mining vehicle - drill deployed"),
    ("klingon-miner-game.png", "Mining vehicle - game camera"),
    ("klingon-dropship-front34.png", "Drop ship with rover clamped"),
    ("klingon-dropship-game.png", "Drop ship - game camera"),
    ("klingon-outpost-l1-front34.png", "Outpost L1 - just landed"),
    ("klingon-outpost-l1-game.png", "Outpost L1 - game camera"),
    ("klingon-outpost-l5-front34.png", "Outpost L5 - Governor's Keep"),
    ("klingon-outpost-l5-game.png", "Outpost L5 - game camera"),
    ("klingon-outpost-l10-front34.png", "Outpost L10 - Great Hall capital"),
    ("klingon-outpost-l10-game.png", "Outpost L10 - game camera"),
    ("fed-mk1-game.png", "Federation Mk I - game camera"),
]


def make_sheet(folder):
    from PIL import Image, ImageDraw, ImageFont
    cw, ch, lab, cols = 480, 320, 34, 4
    rows = math.ceil((len(SHEET) + 1) / cols)
    top = 70
    sheet = Image.new("RGB", (cols * cw, top + rows * (ch + lab)), (14, 10, 11))
    d = ImageDraw.Draw(sheet)
    fonts = "/usr/share/fonts/truetype/dejavu/"
    big = ImageFont.truetype(fonts + "DejaVuSans-Bold.ttf", 30)
    small = ImageFont.truetype(fonts + "DejaVuSans.ttf", 17)
    d.text((18, 16), "KLINGON GROUND KIT  -  Blender mockups", fill=(255, 122, 74), font=big)
    d.text((cols * cw - 640, 26), "rover / miner / drop ship / outpost L1 L5 L10   (2026-11-05)", fill=(205, 191, 186), font=small)
    for i, (name, label) in enumerate(SHEET):
        c, r = i % cols, i // cols
        x, y = c * cw, top + r * (ch + lab)
        path = os.path.join(folder, name)
        if os.path.exists(path):
            im = Image.open(path).convert("RGB")
            im.thumbnail((cw - 8, ch - 8))
            sheet.paste(im, (x + (cw - im.width) // 2, y + (ch - im.height) // 2))
        else:
            d.rectangle((x + 4, y + 4, x + cw - 4, y + ch - 4), outline=(90, 40, 40))
            d.text((x + 20, y + ch // 2), f"missing: {name}", fill=(200, 80, 60), font=small)
        fed = name.startswith("fed-")
        d.rectangle((x + 4, y + ch, x + cw - 4, y + ch + lab - 6), fill=(36, 40, 56) if fed else (58, 22, 18))
        d.text((x + 12, y + ch + 5), label, fill=(220, 228, 255) if fed else (255, 184, 160), font=small)
    out = os.path.join(folder, "klingon-sheet.png")
    sheet.save(out)
    print("sheet", out)


if "--sheet" in argv:
    make_sheet(arg("--sheet"))
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MODEL = arg("--model", "rover")
LEVEL = int(arg("--level", "1"))
POSE = arg("--pose", "stowed")
PREVIEW = arg("--preview")
OUT = arg("--out")
RES = int(arg("--res", "480"))
SAMPLES = int(arg("--samples", "8"))
SHOTS = [s.strip() for s in arg("--shots", "front34,game").split(",") if s.strip()]
BLANK = "--blank" in argv
if MODEL not in ("rover", "miner", "dropship", "outpost", "fedref"):
    sys.exit(f"--model must be rover, miner, dropship, outpost or fedref, not {MODEL}")
if MODEL == "outpost" and LEVEL not in (1, 5, 10):
    sys.exit(f"--level must be 1, 5 or 10, not {LEVEL}")
if POSE not in ("stowed", "deployed"):
    sys.exit(f"--pose must be stowed or deployed, not {POSE}")


def load_build_vehicles():
    """build_vehicles.py's functions without running its main(), and with its own flags at their defaults."""
    path = os.path.join(BLENDER, "build_vehicles.py")
    with open(path) as f:
        src = re.sub(r"^main\(\)\s*$", "", f.read(), flags=re.M)
    saved = sys.argv
    sys.argv = [path]
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


BV = load_build_vehicles()
box, cylinder, strut, empty, mesh_object, bevel = (BV[k] for k in ("box", "cylinder", "strut", "empty", "mesh_object", "bevel"))
material, link = BV["material"], BV["link"]
pi, cos, sin, rad = math.pi, math.cos, math.sin, math.radians

# ---------------------------------------------------------------- materials


def kpalette():
    m = material
    return {
        "hull": m("Hull", (0.05, 0.053, 0.058), 0.3, 0.58),                    # dark gunmetal
        "plate": m("Verdigris", (0.1, 0.14, 0.09), 0.35, 0.55),              # oxidised bronze-green armour
        "bronze": m("Bronze", (0.36, 0.22, 0.1), 0.9, 0.36),
        "dark": m("Chassis", (0.03, 0.03, 0.035), 0.4, 0.6),
        "steel": m("Steel", (0.3, 0.29, 0.28), 0.85, 0.35),
        "tyre": m("Tyre", (0.04, 0.04, 0.045), 0.0, 0.9),
        "window": m("WindowGlow", (0.25, 0.04, 0.01), 0.0, 0.4, emission=(1.0, 0.2, 0.03), strength=3.0),
        "gate": m("GateGlow", (0.2, 0.04, 0.01), 0.0, 0.5, emission=(1.0, 0.24, 0.04), strength=2.2),
        "run": m("RunLight", (1.0, 0.05, 0.03), 0.0, 0.3, emission=(1.0, 0.04, 0.02), strength=10.0),
        "tail": m("TailLamp", (0.9, 0.06, 0.04), 0.0, 0.3, emission=(1.0, 0.06, 0.03), strength=4.0),
        "beacon": m("BeaconLamp", (1.0, 0.12, 0.05), 0.0, 0.3, emission=(1.0, 0.1, 0.04), strength=6.0),
        "lamp": m("Lamp", (1.0, 0.86, 0.7), 0.0, 0.2, emission=(1.0, 0.82, 0.62), strength=5.0),
        "owner": m("Owner", (0.55, 0.05, 0.04), 0.2, 0.45),
        "emblem": m("Emblem", (0.62, 0.42, 0.14), 1.0, 0.28),
        "number": m("Number", (0.72, 0.62, 0.44), 0.2, 0.55),
        "fire": m("Fire", (0.3, 0.08, 0.01), 0.0, 0.5, emission=(1.0, 0.36, 0.05), strength=5.0),
        "engine": m("EngineGlow", (1.0, 0.3, 0.1), 0.0, 0.4, emission=(1.0, 0.26, 0.05), strength=14.0),
        "disruptor": m("DisruptorGlow", (0.3, 1.0, 0.3), 0.0, 0.3, emission=(0.25, 1.0, 0.2), strength=6.0),
        "stone": m("Stone", (0.085, 0.075, 0.07), 0.0, 0.92),
        "ore": m("Ore", (0.23, 0.17, 0.12), 0.1, 0.9),
        "pad": m("PadPaint", (0.45, 0.36, 0.25), 0.0, 0.75),
        "hexline": m("HexLine", (0.9, 0.84, 0.72), 0.0, 0.8),
    }


# ---------------------------------------------------------------- geometry helpers


def sym(pts):
    """Points plus their mirror image across x = 0 (points on the centre line once)."""
    out = list(pts)
    out += [(-x, y, z) for x, y, z in pts if abs(x) > 1e-6]
    return out


def chull(name, pts, mat, parent=None, bev=0.0, shadow=True):
    """Convex hull of the points: the workhorse for angular armour, plates and fins. Coplanar triangles are merged so
    the bevel only rounds real edges."""
    bm = bmesh.new()
    vs = [bm.verts.new(Vector(p)) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    junk = {g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
    if junk:
        bmesh.ops.delete(bm, geom=list(junk), context="VERTS")
    bmesh.ops.dissolve_limit(bm, angle_limit=rad(0.5), verts=bm.verts[:], edges=bm.edges[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = shadow
    if bev > 0:
        bevel(o, bev, 2, 30)
    return o


def newell(pts):
    n = Vector((0, 0, 0))
    for i, a in enumerate(pts):
        b = pts[(i + 1) % len(pts)]
        n.x += (a[1] - b[1]) * (a[2] + b[2])
        n.y += (a[2] - b[2]) * (a[0] + b[0])
        n.z += (a[0] - b[0]) * (a[1] + b[1])
    return n.normalized()


def slab(name, outline, thick, mat, parent=None, bev=0.0, shadow=True):
    """A flat plate: the (convex) outline thickened by `thick` along its normal (fins, wings, roof plates)."""
    n = newell([Vector(p) for p in outline])
    pts = [Vector(p) + n * d for p in outline for d in (-thick / 2, thick / 2)]
    return chull(name, pts, mat, parent, bev, shadow)


def frame_at(obj, origin, direction, right):
    """Ray-cast onto obj (world space, built at the origin) and return a frame at the hit: X = `right` projected into
    the surface (a decal's reading direction), Z = outward normal, Y = Z x X (up the surface)."""
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    inv = obj.matrix_world.inverted()
    o = inv @ Vector(origin)
    d = (inv.to_3x3() @ Vector(direction)).normalized()
    ok, loc, nrm, _ = obj.evaluated_get(dg).ray_cast(o, d)
    if not ok:
        raise RuntimeError(f"ray from {origin} missed {obj.name}")
    loc = obj.matrix_world @ loc
    nrm = (obj.matrix_world.to_3x3() @ nrm).normalized()
    r = Vector(right)
    x = (r - r.dot(nrm) * nrm).normalized()
    y = nrm.cross(x)
    return Matrix(((x.x, y.x, nrm.x, loc.x), (x.y, y.y, nrm.y, loc.y), (x.z, y.z, nrm.z, loc.z), (0, 0, 0, 1)))


def plate_at(name, M, uv, mat, parent=None, lift=0.008, thick=0.014):
    """A thin flat plate on a surface frame M (from frame_at): window slits, stripes, panels. uv is a convex outline."""
    pts = [M @ Vector((u, v, lift + d)) for u, v in uv for d in (-thick / 2, thick / 2)]
    return chull(name, pts, mat, parent, 0.0, shadow=False)


def mirror_uv(uv):
    return [(-u, v) for u, v in reversed(uv)]


def frame(origin, x, n):
    """A frame from an origin, a reading direction x and a normal n (made orthogonal)."""
    n = Vector(n).normalized()
    x = Vector(x)
    x = (x - x.dot(n) * n).normalized()
    y = n.cross(x)
    o = Vector(origin)
    return Matrix(((x.x, y.x, n.x, o.x), (x.y, y.y, n.y, o.y), (x.z, y.z, n.z, o.z), (0, 0, 0, 1)))


# ---------------------------------------------------------------- emblem and decals

BLADE = [(-0.11, 0.2), (0.11, 0.2), (0.2, 0.55), (0.07, 1.0), (-0.02, 0.62), (-0.15, 0.42)]


def emblem(name, M, size, mat, parent=None, lift=0.006, thick=0.012, boss=None):
    """The kit's emblem: three swept daggers inside a broken ring, with a raised centre boss. Lies in M's XY plane
    (one blade pointing along +Y) facing +Z, `size` = outer radius."""
    polys = []
    for k in range(3):
        a = 2 * pi * k / 3
        polys.append([(cos(a) * u - sin(a) * v, sin(a) * u + cos(a) * v) for u, v in BLADE])
        a0, a1 = pi / 2 + a + rad(30), pi / 2 + a + rad(90)
        steps = [a0 + (a1 - a0) * i / 8 for i in range(9)]
        polys.append([(cos(t) * 0.88, sin(t) * 0.88) for t in steps] + [(cos(t) * 0.76, sin(t) * 0.76) for t in reversed(steps)])
    bm = bmesh.new()

    def extrude(poly, z0, z1):
        lo = [bm.verts.new((u, v, z0)) for u, v in poly]
        hi = [bm.verts.new((u, v, z1)) for u, v in poly]
        bm.faces.new(hi)
        bm.faces.new(list(reversed(lo)))
        for i in range(len(poly)):
            j = (i + 1) % len(poly)
            bm.faces.new((lo[i], lo[j], hi[j], hi[i]))

    t = thick / size
    for p in polys:
        extrude(p, 0, t)
    extrude([(cos(pi / 2 + pi / 3 + 2 * pi * k / 3) * 0.24, sin(pi / 2 + pi / 3 + 2 * pi * k / 3) * 0.24) for k in range(3)], 0, t * 1.8)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.transform(M @ Matrix.Translation((0, 0, lift)) @ Matrix.Diagonal((size, size, size, 1)))
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def decal(kind, s, body, size, M, max_width, P, parent):
    """Game marker (empty NameL/NameR/NumberL/NumberR, build_vehicles' convention) plus, unless --blank, the painted
    text itself for the preview. M: frame with X = reading direction, Z = outward."""
    key = f"{kind}{-1 if s < 0 else 1}"
    BV["BLANK"] = True
    BV["text"](key, body, size, None, None, P["number"], parent, max_width=max_width, matrix=M)
    BV["BLANK"] = False
    if not BLANK:
        BV["text"](f"{kind}Text{'L' if s < 0 else 'R'}", body, size, None, None, P["number"], parent, max_width=max_width,
                   matrix=M @ Matrix.Translation((0, 0, 0.004)))


def side_right(s):
    """Reading direction for text on the +X (s = 1) or -X (s = -1) side: toward the front on the right, toward the back
    on the left, so both read left to right."""
    return (0, s, 0)


# ---------------------------------------------------------------- wheel


def k_wheel(name, x, y, r, w, P, parent):
    """Chunky tyre with staggered lugs on a hexagonal gunmetal rim, bronze hub with three blades and a spike."""
    wh = empty(name, (x, y, r), parent)
    s = 1 if x > 0 else -1
    cylinder(f"{name}Tyre", r, w, (0, 0, 0), P["tyre"], wh, segments=28, bev=0.05)
    n = 16
    for row in (-1, 1):
        for k in range(n):
            a = 2 * pi * (k + (0.5 if row > 0 else 0)) / n
            box(f"{name}Lug{row}{k}", (w * 0.46, 0.14, 0.07), (row * w * 0.25, cos(a) * r, sin(a) * r), P["tyre"], wh, bev=0.012,
                rot=(a + pi / 2, 0, 0))
    cylinder(f"{name}Rim", r * 0.62, w * 1.02, (0, 0, 0), P["hull"], wh, segments=6, bev=0.02)
    cylinder(f"{name}Hub", r * 0.36, 0.05, (s * w * 0.52, 0, 0), P["bronze"], wh, segments=6, bev=0.01)
    for k in range(3):
        a = 2 * pi * k / 3 + pi / 2
        b = box(f"{name}Blade{k}", (0.04, 0.07, r * 0.34), (s * w * 0.54, cos(a) * r * 0.3, sin(a) * r * 0.3), P["bronze"], wh, 0.01)
        b.rotation_euler = (a - pi / 2, 0, 0)
    rb = r * 0.13
    cylinder(f"{name}Spike", 0.012 if s < 0 else rb, 0.18, (s * (w * 0.55 + 0.09), 0, 0), P["steel"], wh, segments=6,
             radius2=rb if s < 0 else 0.012)
    return wh


# ---------------------------------------------------------------- survey rover


def k_rover(P, name="KORVAX", number="31"):
    """Survey rover: a low six-wheeled raider. Pointed beak over a dark tub, faceted cab with a V of glowing visor
    slits, bronze-green fender armour with skirt lips over the wheels, two swept fins at the back with red tip lights,
    a winged sensor head ("Dish", spins) and a red Beacon. ~4.6 x 2.6 x 2.4."""
    root = empty("KlingonRover")
    axles = (1.45, 0.0, -1.45)
    r, w, xw = 0.48, 0.36, 1.07
    chull("Tub", sym([(0.75, -2.05, 0.42), (0.75, -2.05, 1.1), (0.75, 1.65, 0.42), (0.75, 2.05, 0.72), (0.75, 2.05, 1.1)]),
          P["dark"], root, bev=0.03)
    chull("Belt", sym([(0.99, -2.15, 1.05), (0.99, -2.15, 1.32), (0.99, 1.75, 1.05), (0.99, 1.62, 1.32), (0.28, 2.3, 1.12),
                       (0.28, 2.22, 1.05), (0.3, 2.16, 1.32)]), P["hull"], root, bev=0.025)
    cab = chull("Hull", sym([(0.99, -2.1, 1.32), (0.99, 1.6, 1.32), (0.35, 2.12, 1.32), (0.62, 0.8, 1.95), (0.62, -1.85, 1.95)]),
                P["hull"], root, bev=0.03)
    # Ram plate under the beak, white driving-lamp slits, red fang lights on the beak.
    chull("Ram", sym([(0.7, 2.04, 0.45), (0.7, 2.06, 0.66), (0.0, 2.34, 0.5), (0.0, 2.34, 0.7), (0.7, 1.96, 0.45), (0.0, 2.24, 0.48)]),
          P["plate"], root, bev=0.015)
    for s in (-1, 1):
        box(f"Headlamp{s}", (0.26, 0.03, 0.06), (s * 0.42, 2.06, 0.95), P["lamp"], root, 0.008)
        nx, ny = 0.62 * s, 0.79
        box(f"Fang{s}", (0.3, 0.03, 0.05), (s * 0.64, 1.99, 1.2), P["run"], root, 0.008, rot=(0, 0, math.atan2(-nx, ny)))
        box(f"Tail{s}", (0.26, 0.03, 0.08), (s * 0.7, -2.165, 1.19), P["tail"], root, 0.008)
    # Visor: a dark band and a V of glowing slits on the front facet.
    M = frame_at(cab, (0, 1.4, 3.0), (0, 0, -1), (-1, 0, 0))
    plate_at("VisorBand", M, [(-0.44, -0.12), (0.44, -0.12), (0.4, 0.2), (-0.4, 0.2)], P["dark"], root, lift=0.004)
    slit = [(-0.38, 0.1), (-0.06, -0.02), (-0.06, 0.08), (-0.35, 0.17)]
    plate_at("Visor0", M, slit, P["window"], root, lift=0.01)
    plate_at("Visor1", M, mirror_uv(slit), P["window"], root, lift=0.01)
    emblem("EmblemFront", frame_at(cab, (0, 1.85, 3.0), (0, 0, -1), (-1, 0, 0)), 0.13, P["emblem"], root)
    emblem("EmblemRear", frame_at(cab, (0, -2.6, 1.62), (0, 1, 0), (1, 0, 0)), 0.2, P["emblem"], root)
    for s in (-1, 1):
        side = lambda y, z: frame_at(cab, (s * 2.0, y, z), (-s, 0, 0), side_right(s))  # noqa: E731
        # Long raked window slit and a short one behind it (in the side frame: u along the reading direction).
        Mw = side(0.35, 1.68)
        long_slit = [(-0.65, -0.08), (0.62, -0.08), (0.45, 0.1), (-0.55, 0.1)]
        plate_at(f"SideWindow{s}", Mw, long_slit if s > 0 else mirror_uv(long_slit), P["window"], root, lift=0.008)
        decal("Name", s, name, 0.16, side(0.25, 1.46) @ Matrix.Translation((0, 0, 0.004)), 1.1, P, root)
        decal("Number", s, number, 0.28, side(-1.3, 1.62) @ Matrix.Translation((0, 0, 0.004)), 0.55, P, root)
        # Owner stripe along the belt, above the fenders.
        BV["ribbon"](f"Flash{s}", [(-2.12, 1.25), (1.58, 1.25)], 0.07, P["owner"], root, x=s * 0.99, outward=s)
        # Fender armour over each wheel, sloping down outward, with a skirt lip.
        for k, yc in enumerate(axles):
            fr, xi = 0.0, (0.64 if k == 0 else 0.97)  # the front fender's inner front corner follows the beak
            chull(f"Fender{s}{k}", [(s * 0.97, yc - 0.62, 1.16), (s * xi, yc + 0.62, 1.16), (s * 1.36, yc + 0.56, 1.04),
                                    (s * 1.36, yc - 0.56, 1.04), (s * 0.97, yc - 0.62, 1.09), (s * xi, yc + 0.62, 1.09),
                                    (s * 1.36, yc + 0.56, 0.98), (s * 1.36, yc - 0.56, 0.98)], P["plate"], root, bev=0.012)
            chull(f"Lip{s}{k}", [(s * 1.33, yc - 0.56, 1.0), (s * 1.33, yc + 0.56 + fr, 1.0), (s * 1.33, yc + 0.42 + fr, 0.78),
                                 (s * 1.33, yc - 0.46, 0.78), (s * 1.37, yc - 0.56, 1.0), (s * 1.37, yc + 0.56 + fr, 1.0),
                                 (s * 1.37, yc + 0.42 + fr, 0.78), (s * 1.37, yc - 0.46, 0.78)], P["hull"], root, bev=0.008)
            box(f"Arm{s}{k}", (0.2, 0.22, 0.16), (s * 0.83, yc, 0.5), P["steel"], root, 0.02)
            k_wheel(f"Wheel{'FMR'[k]}{'L' if s < 0 else 'R'}", s * xw, yc, r, w, P, root)
        # Swept fin at the back of the roof, leaning out, red light at the tip, owner-colour tip.
        a, b, c, d = Vector((s * 0.6, -0.6, 1.93)), Vector((s * 0.6, -1.75, 1.93)), Vector((s * 0.92, -2.45, 2.62)), Vector((s * 0.8, -2.05, 2.66))
        slab(f"Fin{s}", [a, b, c, d], 0.05, P["plate"], root)
        slab(f"FinTip{s}", [d.lerp(a, 0.3), c.lerp(b, 0.3), c, d], 0.062, P["owner"], root)
        box(f"FinLight{s}", (0.06, 0.12, 0.05), c.lerp(d, 0.5) + Vector((0, 0, 0.03)), P["run"], root, 0.01)
    # Roof: hex hatch, grille, blade antenna, beacon, sensor head.
    chull("Ridge", sym([(0.13, -1.85, 1.94), (0.13, 0.42, 1.94), (0.05, -1.7, 2.1), (0.05, 0.3, 2.1)]), P["plate"], root, bev=0.01)
    cylinder("Hatch", 0.2, 0.06, (0.36, -0.75, 1.97), P["bronze"], root, axis="Z", segments=6, bev=0.01)
    for k in range(4):
        box(f"Grille{k}", (0.36, 0.04, 0.03), (-0.34, -1.0 - k * 0.09, 1.965), P["dark"], root, 0)
    slab("Antenna", [(-0.45, -1.55, 1.94), (-0.45, -1.75, 1.94), (-0.5, -1.95, 2.45), (-0.48, -1.86, 2.46)], 0.025, P["steel"], root)
    cylinder("BeaconBase", 0.07, 0.04, (0.42, -1.6, 1.97), P["dark"], root, axis="Z", segments=8)
    cylinder("Beacon", 0.055, 0.09, (0.42, -1.6, 2.03), P["beacon"], root, axis="Z", segments=8)
    dish = empty("Dish", (0, 0.15, 1.95), root)
    cylinder("DishMast", 0.05, 0.24, (0, 0, 0.12), P["steel"], dish, axis="Z", segments=8)
    chull("DishHead", sym([(0.14, -0.15, 0.24), (0.14, 0.17, 0.24), (0.1, -0.12, 0.36), (0.1, 0.12, 0.36), (0.5, -0.1, 0.22),
                           (0.5, -0.04, 0.18), (0.42, 0.06, 0.25)]), P["plate"], dish, bev=0.01)
    box("DishEye", (0.16, 0.02, 0.04), (0, 0.17, 0.29), P["window"], dish, 0.005)
    return root


# ---------------------------------------------------------------- mining vehicle


def track(name, s, P, parent):
    """Angular track unit at x = s*1.07: rubber belt with pads, an armoured skirt and small road-wheel hubs."""
    outline = [(-2.05, 0.05), (2.05, 0.05), (2.62, 0.62), (2.45, 0.95), (-2.45, 0.95), (-2.62, 0.62)]
    chull(f"{name}Belt", [(s * x, y, z) for x in (0.78, 1.36) for y, z in outline], P["tyre"], parent, bev=0.04)
    n = len(outline)
    for i in range(n):
        (y0, z0), (y1, z1) = outline[i], outline[(i + 1) % n]
        seg = Vector((0, y1 - y0, z1 - z0))
        steps = max(1, int(seg.length / 0.24))
        ang = math.atan2(seg.z, seg.y)
        for k in range(steps):
            t = (k + 0.5) / steps
            p = Vector((s * 1.07, y0, z0)) + seg * t
            nrm = Vector((0, seg.z, -seg.y)).normalized() * 0.02  # outward: the outline runs counter-clockwise seen from +X
            box(f"{name}Pad{i}_{k}", (0.6, 0.12, 0.05), p + nrm, P["dark"], parent, 0.008, rot=(ang, 0, 0))
    chull(f"{name}Skirt", [(s * x, y, z) for x in (1.37, 1.41) for y, z in
                           [(-2.3, 0.42), (2.3, 0.42), (2.6, 0.75), (2.5, 1.0), (-2.5, 1.0), (-2.6, 0.75)]], P["hull"], parent, bev=0.01)
    for k in range(5):
        cylinder(f"{name}Hub{k}", 0.16, 0.05, (s * 1.375, -1.8 + k * 0.9, 0.24), P["bronze"], parent, segments=6)


def k_miner(P, pose="stowed", name="DUQGHOR", number="08"):
    """Mining vehicle: a tracked armoured digger. Wedge body overhanging the tracks, plough ram at the nose, glowing
    visor, ore hopper ("Fill") at the back, two exhaust stacks with swept fins. The drill "Boom" pivots in bronze
    cheek plates at the front: stowed it lies back over the hopper with the drill folded along it; deployed it swings
    forward and the drill hangs straight down ahead of the ram. Four stabiliser legs (Leg0-3) fold flat along the
    flanks and swing out to the ground. ~5.6 x 2.8 x 3.1 stowed."""
    root = empty("KlingonMiner")
    for s in (-1, 1):
        track(f"Track{'L' if s < 0 else 'R'}", s, P, root)
    chull("Belly", sym([(0.78, -2.4, 0.4), (0.78, 2.3, 0.4), (0.78, -2.4, 1.05), (0.78, 2.3, 1.05)]), P["dark"], root, bev=0.03)
    body = chull("Hull", sym([(1.42, -2.6, 1.0), (1.42, 2.2, 1.0), (0.7, 2.78, 1.0), (0.7, 2.78, 1.25), (1.42, -2.62, 1.5),
                              (0.95, -2.35, 2.0), (0.95, 1.3, 2.0)]), P["hull"], root, bev=0.04)
    chull("Ram", sym([(1.3, 2.68, 0.25), (1.3, 2.72, 0.85), (0.0, 2.95, 0.28), (0.0, 2.98, 0.82), (1.3, 2.6, 0.25), (1.3, 2.62, 0.85),
                      (0.0, 2.87, 0.28), (0.0, 2.9, 0.82)]), P["plate"], root, bev=0.02)
    for k in range(7):
        x = -1.05 + k * 0.35
        y = 2.95 - abs(x) / 1.3 * 0.27
        chull(f"Tooth{k}", [(x - 0.08, y - 0.06, 0.28), (x + 0.08, y - 0.06, 0.28), (x, y + 0.02, 0.06), (x - 0.08, y - 0.12, 0.3),
                            (x + 0.08, y - 0.12, 0.3)], P["steel"], root)
    for s in (-1, 1):
        box(f"Headlamp{s}", (0.3, 0.03, 0.07), (s * 0.38, 2.795, 1.12), P["lamp"], root, 0.008)
        box(f"Fang{s}", (0.03, 0.3, 0.05), (s * 1.42, 2.0, 1.05), P["run"], root, 0.008)
        box(f"Tail{s}", (0.3, 0.03, 0.09), (s * 1.0, -2.625, 1.3), P["tail"], root, 0.008)
    M = frame_at(body, (0, 2.2, 4.0), (0, 0, -1), (-1, 0, 0))
    plate_at("VisorBand", M, [(-0.78, -0.22), (0.78, -0.22), (0.74, 0.1), (-0.74, 0.1)], P["dark"], root, lift=0.004)
    slit = [(-0.7, 0.0), (-0.1, -0.14), (-0.1, -0.04), (-0.66, 0.07)]
    plate_at("Visor0", M, slit, P["window"], root, lift=0.01)
    plate_at("Visor1", M, mirror_uv(slit), P["window"], root, lift=0.01)
    emblem("EmblemRear", frame_at(body, (0, -3.5, 1.25), (0, 1, 0), (1, 0, 0)), 0.2, P["emblem"], root)
    for s in (-1, 1):
        side = lambda y, z: frame_at(body, (s * 3, y, z), (-s, 0, 0), side_right(s))  # noqa: E731
        plate_at(f"SideWindow{s}", side(1.55, 1.85), [(-0.3, -0.05), (0.32, -0.05), (0.22, 0.06), (-0.26, 0.06)], P["window"], root)
        emblem(f"EmblemSide{s}", side(-1.4, 1.88), 0.13, P["emblem"], root)
        BV["ribbon"](f"Flash{s}", [(-2.4, 0.94), (2.42, 0.94)], 0.05, P["owner"], root, x=s * 1.41, outward=s)
        decal("Name", s, name, 0.22, frame((s * 1.415, -0.85, 0.7), side_right(s), (s, 0, 0)), 1.7, P, root)
        decal("Number", s, number, 0.34, frame((s * 1.415, 1.6, 0.7), side_right(s), (s, 0, 0)), 0.7, P, root)
        # Stabiliser legs (Leg0 front-left, Leg1 front-right, Leg2 rear-left, Leg3 rear-right): pivot at the top, leg
        # along local -Z. Stowed they lie flat along the flank (front pair pointing back, rear pair forward); deployed
        # they swing out 25 degrees to stand on their pads.
        for e, y in ((1, 1.8), (-1, -2.0)):
            idx = (0 if e > 0 else 2) + (0 if s < 0 else 1)
            leg = empty(f"Leg{idx}", (s * 1.36, y, 1.6), root)
            leg.rotation_euler = (rad(-90 * e), 0, 0) if pose == "stowed" else (0, rad(-25 * s), 0)  # Ry(-25s) swings local -Z outward
            chull(f"Leg{idx}Strut", [(x, y_, z) for x in (-0.09, 0.09) for y_ in (-0.08, 0.08) for z in (0.05,)] +
                  [(x, y_, -1.6) for x in (-0.06, 0.06) for y_ in (-0.06, 0.06)], P["plate"], leg, bev=0.01)
            box(f"Leg{idx}Ram", (0.06, 0.06, 1.0), (0, 0.11, -0.65), P["steel"], leg, 0)
            cylinder(f"Leg{idx}Pad", 0.2, 0.12, (0, 0, -1.64), P["dark"], leg, axis="Z", segments=6)
            cylinder(f"Leg{idx}Hinge", 0.11, 0.26, (0, 0, 0), P["bronze"], leg, axis="X", segments=6)
    # Hopper with its ore load.
    for k, (sx, sy, x, y) in enumerate(((1.24, 0.07, 0, -0.68), (1.24, 0.07, 0, -2.3), (0.07, 1.62, 0.6, -1.49), (0.07, 1.62, -0.6, -1.49))):
        box(f"Hopper{k}", (sx, sy, 0.4), (x, y, 2.17), P["plate"], root, 0.015)
    chull("Fill", [(x, y, 2.0) for x in (-0.55, 0.55) for y in (-2.25, -0.75)] + [(0.3, -1.2, 2.3), (-0.2, -1.7, 2.32), (0.1, -2.0, 2.2),
                                                                                (-0.4, -1.0, 2.18), (0.45, -1.9, 2.15)], P["ore"], root)
    # Exhaust stacks with swept fins.
    for s in (-1, 1):
        base, top = Vector((s * 0.8, -2.05, 1.95)), Vector((s * 0.86, -2.3, 2.92))
        chull(f"Stack{s}", [base + Vector((cos(a) * 0.14, sin(a) * 0.14, 0)) for a in [k * pi / 3 for k in range(6)]] +
              [top + Vector((cos(a) * 0.1, sin(a) * 0.1, 0)) for a in [k * pi / 3 for k in range(6)]], P["hull"], root)
        cylinder(f"StackGlow{s}", 0.08, 0.02, top + Vector((0, 0, 0.005)), P["engine"], root, axis="Z", segments=6)
        slab(f"StackFin{s}", [(s * 0.92, -1.95, 2.0), (s * 0.94, -2.3, 2.5), (s * 1.32, -2.75, 3.05), (s * 1.2, -2.35, 2.6)], 0.04, P["plate"], root)
        box(f"StackLight{s}", (0.05, 0.1, 0.05), (s * 1.3, -2.7, 3.06), P["run"], root, 0.008)
    # Boom: bronze cheek plates, rest cradle, the boom itself with its drill.
    for s in (-1, 1):
        chull(f"Cheek{s}", [(s * x, y, z) for x in (0.29, 0.4) for y, z in
                            [(1.4, 1.95), (2.08, 1.55), (2.0, 2.62), (1.8, 2.85), (1.55, 2.8)]], P["bronze"], root, bev=0.015)
    chull("BoomRest", sym([(0.3, -0.35, 1.98), (0.3, -0.15, 1.98), (0.12, -0.35, 2.4), (0.12, -0.15, 2.4)]), P["hull"], root)
    for s in (-1, 1):
        slab(f"RestFork{s}", [(s * 0.12, -0.35, 2.38), (s * 0.12, -0.15, 2.38), (s * 0.32, -0.15, 2.62), (s * 0.32, -0.35, 2.62)], 0.05, P["bronze"], root)
    cylinder("BeaconBase", 0.07, 0.04, (-0.345, 1.62, 2.84), P["dark"], root, axis="Z", segments=8)
    cylinder("Beacon", 0.055, 0.09, (-0.345, 1.62, 2.9), P["beacon"], root, axis="Z", segments=8)
    pivot = Vector((0, 1.75, 2.55))
    boom = empty("Boom", pivot, root)
    a = pi - 0.05 if pose == "stowed" else 0.15
    boom.rotation_euler = (a, 0, 0)
    Lb = 2.9
    oct0 = [(0.24, 0.12), (0.12, 0.24), (-0.12, 0.24), (-0.24, 0.12), (-0.24, -0.12), (-0.12, -0.24), (0.12, -0.24), (0.24, -0.12)]
    chull("BoomBeam", [(x, 0.0, z) for x, z in oct0] + [(x * 0.66, Lb, z * 0.66) for x, z in oct0], P["hull"], boom, bev=0.02)
    cylinder("BoomAxle", 0.17, 0.62, (0, 0, 0), P["steel"], boom, axis="X", segments=12)
    cylinder("BoomKnuckle", 0.17, 0.42, (0, Lb, 0), P["bronze"], boom, axis="X", segments=6)
    for s in (-1, 1):
        hw = lambda y: 0.24 - 0.08 * y / Lb  # noqa: E731
        slab(f"BoomPlate{s}", [(s * (hw(y) + 0.012), y, z * (hw(y) / 0.24)) for y, z in ((0.4, 0.13), (2.4, 0.11), (2.4, -0.11), (0.4, -0.13))],
             0.02, P["plate"], boom)
        slab(f"BoomStripe{s}", [(s * (hw(y) + 0.026), y, z * (hw(y) / 0.24)) for y, z in ((0.6, 0.035), (2.2, 0.03), (2.2, -0.03), (0.6, -0.035))],
             0.008, P["owner"], boom, shadow=False)
    box("BoomLight", (0.06, 0.06, 0.06), (0, Lb + 0.12, 0.13), P["run"], boom, 0.01)
    drill = empty("Drill", (0, Lb, 0), boom)
    world = Matrix.Rotation(-pi / 2, 3, "X") if pose == "stowed" else Matrix.Identity(3)
    drill.matrix_basis = Matrix.Translation((0, Lb, 0)) @ (Matrix.Rotation(-a, 3, "X") @ world).to_4x4()
    cylinder("DrillHousing", 0.22, 1.05, (0, 0, -0.5), P["hull"], drill, axis="Z", segments=6, bev=0.02)
    for z in (-0.2, -0.8):
        cylinder(f"DrillBand{z}", 0.235, 0.08, (0, 0, z), P["bronze"], drill, axis="Z", segments=6)
    cylinder("DrillGlow", 0.1, 0.02, (0, 0, -1.035), P["engine"], drill, axis="Z", segments=6)
    string_len = 0.12 if pose == "stowed" else 1.38
    st = empty("String", (0, 0, -1.03), drill)
    cylinder("StringRod", 0.07, 1.0, (0, 0, -0.5), P["steel"], st, axis="Z", segments=6)
    st.scale = (1, 1, string_len)
    bit = empty("Bit", (0, 0, -1.03 - string_len), drill)
    cylinder("BitCone", 0.012, 0.55, (0, 0, -0.275), P["steel"], bit, axis="Z", segments=6, radius2=0.2)
    for k in range(3):
        chull(f"BitFlute{k}", [(cos(t) * 0.21, sin(t) * 0.21, z) for t in (2 * pi * k / 3, 2 * pi * k / 3 + 0.5) for z in (0, -0.2)] +
              [(0, 0, -0.62)], P["bronze"], bit)
    return root


# ---------------------------------------------------------------- drop ship


def k_dropship(P, cargo=True, name="VETH'KAR", number="12"):
    """Drop ship: an arrowhead lander that carries one vehicle down clamped under its belly. Faceted hull with a
    raised spine, down-swept wings that end in landing talons, two engine pods with red-orange exhaust, a swept
    dorsal fin with the Beacon, glowing V visor, disruptor barrels at the wing roots and four cargo clamps
    (Clamp0-3) gripping the rover's cab. ~13 long x 11.6 span x 6.2 tall."""
    root = empty("KlingonDropship")
    hull = chull("Hull", sym([(1.9, -5.3, 2.75), (2.15, 0.6, 2.7), (1.3, 4.2, 2.8), (0.35, 6.7, 3.25), (0.3, 6.55, 3.65),
                              (2.45, -4.9, 3.75), (2.6, 0.4, 3.8), (1.6, 4.5, 3.85), (1.25, -5.0, 4.45), (1.35, 1.6, 4.5),
                              (0.75, 4.7, 4.3), (1.85, -5.7, 3.0), (1.2, -5.75, 4.2)]), P["hull"], root, bev=0.06)
    chull("Spine", sym([(0.55, -5.3, 4.35), (0.55, 1.8, 4.4), (0.2, 2.8, 4.42), (0.28, -4.3, 5.15), (0.28, -0.8, 5.05)]), P["plate"], root, bev=0.04)
    chull("Keel", sym([(1.2, -4.6, 2.6), (1.2, -2.9, 2.6), (0.9, -4.8, 2.74), (1.3, 2.9, 2.6), (0.9, 4.0, 2.7), (1.3, 2.75, 2.75)]), P["dark"], root)
    slab("DorsalFin", [(0, -2.4, 5.0), (0, -4.6, 5.05), (0, -5.85, 6.1), (0, -5.05, 6.15)], 0.12, P["plate"], root)
    slab("DorsalFinTip", [(0, -5.3, 5.7), (0, -5.68, 5.88), (0, -5.85, 6.1), (0, -5.05, 6.15), (0, -4.95, 5.9)], 0.14, P["owner"], root)
    cylinder("Beacon", 0.09, 0.14, (0, -5.45, 6.2), P["beacon"], root, axis="Z", segments=8)
    # Visor and side ports.
    M = frame_at(hull, (0, 5.25, 8), (0, 0, -1), (-1, 0, 0))
    plate_at("VisorBand", M, [(-0.6, -0.3), (0.6, -0.3), (0.55, 0.25), (-0.55, 0.25)], P["dark"], root, lift=0.004)
    slit = [(-0.52, 0.12), (-0.1, -0.12), (-0.1, 0.02), (-0.48, 0.22)]
    plate_at("Visor0", M, slit, P["window"], root, lift=0.012)
    plate_at("Visor1", M, mirror_uv(slit), P["window"], root, lift=0.012)
    emblem("EmblemNose", frame_at(hull, (0, 4.1, 8), (0, 0, -1), (-1, 0, 0)), 0.5, P["emblem"], root)
    for s in (-1, 1):
        side = lambda y, z: frame_at(hull, (s * 6, y, z), (-s, 0, 0), side_right(s))  # noqa: E731
        for k, y in enumerate((3.2, 2.4, 1.6)):
            plate_at(f"Port{s}{k}", side(y, 3.95), [(-0.3, -0.06), (0.3, -0.06), (0.22, 0.06), (-0.26, 0.06)], P["window"], root)
        decal("Name", s, name, 0.32, side(0.9, 3.08), 2.5, P, root)
        decal("Number", s, number, 0.46, side(4.05, 3.3), 0.8, P, root)
        # Wing: root along the hull shoulder, drooping 40 degrees to a talon foot.
        lr, tr, lt, tt = (Vector((s * 2.35, 0.3, 3.75)), Vector((s * 2.25, -4.6, 3.7)), Vector((s * 5.4, -3.0, 1.1)),
                          Vector((s * 5.6, -4.7, 1.0)))
        wing = slab(f"Wing{s}", [lr, tr, tt, lt], 0.28, P["hull"], root, bev=0.03)
        nw = newell([lr, tr, tt, lt])
        if nw.z < 0:
            nw = -nw
        up = nw * 0.15  # armour plate centre; its top is at 0.18 above the wing's mid-plane
        slab(f"WingArmour{s}", [lr.lerp(lt, 0.12) + up - Vector((0, 0.3, 0)), tr.lerp(tt, 0.12) + up + Vector((0, 0.3, 0)),
                                tr.lerp(tt, 0.85) + up + Vector((0, 0.2, 0)), lr.lerp(lt, 0.85) + up - Vector((0, 0.25, 0))], 0.06, P["plate"], root)
        top = nw * 0.19
        slab(f"WingStripe{s}", [lr.lerp(lt, 0.2) + top - Vector((0, 0.4, 0)), lr.lerp(lt, 0.8) + top - Vector((0, 0.4, 0)),
                                lr.lerp(lt, 0.8) + top - Vector((0, 0.72, 0)), lr.lerp(lt, 0.2) + top - Vector((0, 0.78, 0))], 0.02, P["owner"], root,
             shadow=False)
        mid = (lr + tr + lt + tt) / 4
        emblem(f"EmblemWing{s}", frame(mid + nw * 0.18 - Vector((0, 0.45, 0)) + (lt - lr).normalized() * 0.25, (-1, 0, 0), nw), 0.72, P["emblem"], root)
        box(f"WingLight{s}", (0.08, 0.3, 0.08), lt + Vector((s * 0.02, 0.0, 0.18)), P["run"], root, 0.01)
        box(f"WingTail{s}", (0.08, 0.2, 0.08), tt + Vector((s * 0.02, -0.1, 0.16)), P["tail"], root, 0.01)
        # Talon foot under the wing tip.
        foot = lt.lerp(tt, 0.5)
        chull(f"Talon{s}", [lt + Vector((0, -0.2, -0.05)), tt + Vector((0, 0.3, -0.05)), lt + Vector((-s * 0.3, -0.3, 0.05)),
                            tt + Vector((-s * 0.3, 0.3, 0.05)), Vector((foot.x + s * 0.15, foot.y, 0.1)), Vector((foot.x - s * 0.1, foot.y + 0.3, 0.12))],
              P["plate"], root, bev=0.02)
        cylinder(f"FootPad{s}", 0.45, 0.14, (foot.x + s * 0.05, foot.y + 0.05, 0.07), P["dark"], root, axis="Z", segments=6)
        # Engine pod with its exhaust.
        chull(f"Engine{s}", [(s * x, y, z) for y in (-3.0, -6.3) for x, z in ((1.2, 3.2), (2.3, 3.1), (2.3, 4.05), (1.3, 4.2))] +
              [(s * 1.75, -2.4, 3.65)], P["hull"], root, bev=0.04)
        chull(f"Exhaust{s}", [(s * x, -6.31, z) for x, z in ((1.35, 3.3), (2.15, 3.25), (2.15, 3.95), (1.42, 4.05))] +
              [(s * x, -6.25, z) for x, z in ((1.35, 3.3), (2.15, 3.25), (2.15, 3.95), (1.42, 4.05))], P["engine"], root)
        chull(f"EngineArmour{s}", [(s * x, y, z) for y in (-3.3, -5.9) for x, z in ((1.3, 4.25), (2.1, 4.12), (2.36, 3.8))] +
              [(s * 1.75, -2.9, 4.1)], P["plate"], root)
        # Disruptor at the wing root, nose gear, cargo clamps.
        cylinder(f"Disruptor{s}", 0.12, 1.5, (s * 2.55, 1.0, 3.5), P["hull"], root, axis="Y", segments=6)
        cylinder(f"DisruptorTip{s}", 0.08, 0.06, (s * 2.55, 1.77, 3.5), P["disruptor"], root, axis="Y", segments=6)
        strut(f"NoseGear{s}", (s * 0.9, 4.6, 2.85), (s * 1.1, 4.9, 0.12), 0.09, P["steel"], root)
        strut(f"NoseGearRam{s}", (s * 0.6, 4.1, 2.8), (s * 1.05, 4.85, 0.6), 0.05, P["dark"], root)
        cylinder(f"NosePad{s}", 0.32, 0.12, (s * 1.1, 4.9, 0.06), P["dark"], root, axis="Z", segments=6)
        for e, y in ((1, 1.35), (-1, -1.25)):
            idx = (0 if e > 0 else 2) + (0 if s < 0 else 1)
            c = empty(f"Clamp{idx}", (s * 1.25, y, 2.68), root)
            box(f"Clamp{idx}Hinge", (0.4, 0.34, 0.16), (0, 0, 0), P["bronze"], c, 0.02)
            chull(f"Clamp{idx}Arm", [(s * dx, dy, dz) for dx, dz in ((-0.13, 0.0), (0.11, 0.0), (0.06, -1.18), (-0.09, -1.18))
                                     for dy in (-0.12, 0.12)], P["hull"], c, bev=0.01)
            chull(f"Clamp{idx}Claw", [(s * dx, dy, dz) for dx, dz in ((0.06, -1.18), (-0.09, -1.18), (-0.26, -1.3), (0.02, -1.36))
                                      for dy in (-0.1, 0.1)], P["plate"], c, bev=0.01)
    dish = empty("Dish", (0, 3.1, 4.42), root)
    cylinder("DishMast", 0.08, 0.3, (0, 0, 0.15), P["steel"], dish, axis="Z", segments=8)
    chull("DishHead", sym([(0.25, -0.2, 0.3), (0.25, 0.25, 0.3), (0.16, -0.15, 0.48), (0.16, 0.18, 0.48), (0.8, -0.15, 0.28), (0.7, 0.08, 0.34)]),
          P["plate"], dish, bev=0.015)
    box("DishEye", (0.3, 0.02, 0.06), (0, 0.255, 0.38), P["window"], dish, 0.005)
    if cargo:
        rover = k_rover(P)
        rover.parent = root
        rover.name = "Cargo"
    return root


# ---------------------------------------------------------------- outpost pieces


def hex_ring(r, z, phase=0.0, sides=6):
    return [(cos(phase + 2 * pi * k / sides) * r, sin(phase + 2 * pi * k / sides) * r, z) for k in range(sides)]


def hex_outline(name, centre, R, width, mat, parent=None):
    """Pointy-top (corners at +-Y) hex outline lying on the ground: one game tile (25 across the flats = R 14.43)."""
    bm = bmesh.new()
    outer = [bm.verts.new((centre[0] + cos(pi / 2 + k * pi / 3) * R, centre[1] + sin(pi / 2 + k * pi / 3) * R, 0.02)) for k in range(6)]
    inner = [bm.verts.new((centre[0] + cos(pi / 2 + k * pi / 3) * (R - width), centre[1] + sin(pi / 2 + k * pi / 3) * (R - width), 0.02))
             for k in range(6)]
    for k in range(6):
        j = (k + 1) % 6
        bm.faces.new((inner[k], outer[k], outer[j], inner[j]))
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def face_frame(phi, a0, a1, z0, z1, t, u=0.0, lift=0.0):
    """Frame on a face of a tapered polygonal tower: the face centred at angle phi, apothem a0 at z0 to a1 at z1, at
    height fraction t; X = the viewer's right along the face, Z = outward."""
    d = Vector((cos(phi), sin(phi), 0))
    tan = Vector((-sin(phi), cos(phi), 0))
    a = a0 + (a1 - a0) * t
    n = (d * (z1 - z0) + Vector((0, 0, a0 - a1))).normalized()
    p = d * a + Vector((0, 0, z0 + (z1 - z0) * t)) + tan * u + n * lift
    return frame(p, tan, n)


AP = math.cos(pi / 6)  # apothem of a hexagon of radius 1


def tower(name, mat, r0, r1, z0, z1, parent, phase=0.0, bev=0.04):
    """Tapered hexagonal block. phase 0: corners at 60k, faces centred at 30 + 60k; phase 30: faces at 60k."""
    return chull(name, hex_ring(r0, z0, phase) + hex_ring(r1, z1, phase), mat, parent, bev)


def brazier(name, P, loc, parent, scale=1.0, light=True):
    g = empty(name, loc, parent)
    g.scale = (scale, scale, scale)
    for k in range(3):
        a = 2 * pi * k / 3 + 0.3
        strut(f"{name}Leg{k}", (cos(a) * 0.5, sin(a) * 0.5, 0), (cos(a) * 0.18, sin(a) * 0.18, 0.95), 0.04, P["dark"], g, segments=6)
    cylinder(f"{name}Bowl", 0.26, 0.32, (0, 0, 1.05), P["bronze"], g, axis="Z", segments=8, radius2=0.46)
    cylinder(f"{name}Coals", 0.4, 0.04, (0, 0, 1.2), P["fire"], g, axis="Z", segments=8)
    for k, (dx, dy, h, rr) in enumerate(((0, 0, 0.8, 0.28), (0.14, 0.08, 0.5, 0.16), (-0.12, -0.1, 0.55, 0.15))):
        cylinder(f"{name}Flame{k}", rr, h, (dx, dy, 1.2 + h / 2), P["fire"], g, axis="Z", segments=7, radius2=0.01)
    if light:
        lt = bpy.data.objects.new(f"{name}Light", bpy.data.lights.new(f"{name}Light", "POINT"))
        lt.data.energy = 120 * scale * scale
        lt.data.color = (1.0, 0.45, 0.15)
        lt.data.shadow_soft_size = 0.3
        lt.location = (0, 0, 1.9)
        link(lt, g)
    return g


def spike_x(name, length, r, loc, s, mat, parent):
    """A cone along X pointing +X (s = 1) or -X (s = -1)."""
    return cylinder(name, r if s > 0 else 0.005, length, loc, mat, parent, axis="X", segments=6, radius2=0.005 if s > 0 else r)


def banner(name, P, loc, parent, h=7.0, w=1.2, bh=2.6, rotz=0.0):
    """House banner: dark pole, bronze crossbar with spiked ends, a pointed Owner-colour cloth with the emblem on
    both faces. The cloth faces local +Y."""
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, rotz)
    k = h / 7
    cylinder(f"{name}Pole", 0.07 * k, h, (0, 0, h / 2), P["dark"], g, axis="Z", segments=8)
    cylinder(f"{name}Finial", 0.1 * k, 0.6 * k, (0, 0, h + 0.3 * k), P["bronze"], g, axis="Z", segments=6, radius2=0.01)
    top = h - 0.3 * k
    box(f"{name}Bar", (w + 0.4, 0.08 * k, 0.08 * k), (0, 0.12 * k, top), P["bronze"], g, 0.01)
    for s in (-1, 1):
        spike_x(f"{name}BarSpike{s}", 0.35 * k, 0.05 * k, (s * (w / 2 + 0.37), 0.12 * k, top), s, P["bronze"], g)
    bot = top - bh
    y = 0.12 * k
    slab(f"{name}Cloth", [(-w / 2, y, top - 0.05), (w / 2, y, top - 0.05), (w / 2, y, bot + 0.5 * k), (0, y, bot), (-w / 2, y, bot + 0.5 * k)],
         0.03, P["owner"], g)
    for sgn in (1, -1):
        emblem(f"{name}Emblem{sgn}", frame((0, y + sgn * 0.016, top - bh * 0.42), (-sgn, 0, 0), (0, sgn, 0)), w * 0.36, P["emblem"], g)
    return g


def landing_pad(name, P, loc, R, parent):
    g = empty(name, loc, parent)
    cylinder(f"{name}Rim", R + 0.25, 0.22, (0, 0, 0.11), P["bronze"], g, axis="Z", segments=8)
    cylinder(f"{name}Deck", R, 0.3, (0, 0, 0.15), P["dark"], g, axis="Z", segments=8)
    emblem(f"{name}Mark", frame((0, 0, 0.3), (-1, 0, 0), (0, 0, 1)), R * 0.62, P["pad"], g, lift=0.002, thick=0.01)
    for k in range(8):
        a = 2 * pi * (k + 0.5) / 8
        box(f"{name}Light{k}", (0.18, 0.18, 0.08), (cos(a) * (R - 0.15), sin(a) * (R - 0.15), 0.31), P["run"], g, 0.01)
    return g


def aframe(name, P, loc, rotz, parent, L=3.4, W=2.8, H=2.6):
    """A-frame shelter: two bronze-green roof plates on a ridge, dark end walls, a glowing door, ridge spikes."""
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, rotz)
    for s in (-1, 1):
        slab(f"{name}Roof{s}", [(0, -L / 2 - 0.25, H), (0, L / 2 + 0.25, H), (s * W / 2, L / 2, 0.05), (s * W / 2, -L / 2, 0.05)], 0.1, P["plate"], g)
    for e in (-1, 1):
        y = e * (L / 2 - 0.15)
        slab(f"{name}End{e}", [(-W / 2 + 0.2, y, 0.05), (W / 2 - 0.2, y, 0.05), (0, y, H - 0.2)], 0.08, P["hull"], g)
    slab(f"{name}Door", [(-0.4, L / 2 - 0.1, 0.05), (0.4, L / 2 - 0.1, 0.05), (0.22, L / 2 - 0.1, 1.45), (-0.22, L / 2 - 0.1, 1.45)], 0.02, P["window"], g)
    box(f"{name}Ridge", (0.16, L + 0.6, 0.12), (0, 0, H + 0.04), P["bronze"], g, 0.02)
    for e in (-1, 1):
        strut(f"{name}Spike{e}", (0, e * (L / 2 + 0.25), H), (0, e * (L / 2 + 0.75), H + 0.55), 0.05, P["bronze"], g, segments=6)
    return g


def longhouse(name, P, loc, rotz, parent, L=5.6, W=3.4, H=4.4):
    """Clan longhouse: dark stone base, steep metal roof, gable ends in gunmetal, a glowing door, slit windows, a
    hearth vent and a weapons rack by the door. Local +Y = the door end."""
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, rotz)
    box(f"{name}Base", (W, L, 1.3), (0, 0, 0.65), P["stone"], g, 0.06)
    for s in (-1, 1):
        slab(f"{name}Roof{s}", [(0, -L / 2 - 0.35, H), (0, L / 2 + 0.35, H), (s * (W / 2 + 0.35), L / 2 + 0.05, 1.15),
                                (s * (W / 2 + 0.35), -L / 2 - 0.05, 1.15)], 0.12, P["plate"], g)
        for k in range(3):
            box(f"{name}Slit{s}{k}", (0.03, 0.5, 0.14), (s * (W / 2 + 0.005), -L / 3 + k * L / 3, 0.85), P["window"], g, 0)
    for e in (-1, 1):
        y = e * (L / 2 - 0.1)
        slab(f"{name}Gable{e}", [(-W / 2 + 0.1, y, 1.25), (W / 2 - 0.1, y, 1.25), (0, y, H - 0.2)], 0.1, P["hull"], g)
    slab(f"{name}Door", [(-0.55, L / 2 + 0.01, 0.0), (0.55, L / 2 + 0.01, 0.0), (0.4, L / 2 + 0.01, 1.7), (-0.4, L / 2 + 0.01, 1.7)], 0.02, P["window"], g)
    emblem(f"{name}Emblem", frame((0, L / 2 - 0.04, 2.6), (-1, 0, 0), (0, 1, 0)), 0.55, P["emblem"], g)
    box(f"{name}Ridge", (0.2, L + 0.8, 0.16), (0, 0, H + 0.05), P["bronze"], g, 0.02)
    for e in (-1, 1):
        strut(f"{name}Horn{e}", (0, e * (L / 2 + 0.35), H), (0, e * (L / 2 + 1.0), H + 0.9), 0.07, P["bronze"], g, segments=6)
    vz = 1.15 + (H - 1.15) * (1 - (W / 4) / (W / 2 + 0.35))  # roof height above the vent
    cylinder(f"{name}Vent", 0.22, H + 0.6 - vz + 0.3, (W / 4, -L / 4, (vz - 0.3 + H + 0.6) / 2), P["hull"], g, axis="Z", segments=6)
    cylinder(f"{name}VentGlow", 0.15, 0.03, (W / 4, -L / 4, H + 0.61), P["fire"], g, axis="Z", segments=6)
    for k in range(3):
        strut(f"{name}Rack{k}", (W / 2 - 0.2 - k * 0.25, L / 2 + 0.45, 0.0), (W / 2 - 0.3 - k * 0.25, L / 2 + 0.25, 1.4), 0.025, P["steel"], g, segments=6)
    box(f"{name}RackBar", (0.9, 0.06, 0.06), (W / 2 - 0.45, L / 2 + 0.35, 1.0), P["dark"], g, 0)
    return g


def wall(name, P, a, b, parent, h=2.8, t=0.9, spikes=True):
    """Battered stone wall from a to b (2D) with a gunmetal cap and leaning spikes."""
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = (b - a).normalized()
    n = Vector((-d.y, d.x, 0))
    pts = [p + n * o + Vector((0, 0, z)) for p in (a, b) for o, z in ((t / 2, 0), (-t / 2, 0), (t * 0.3, h), (-t * 0.3, h))]
    chull(name, pts, P["stone"], parent, bev=0.05)
    cap = [p + n * o + Vector((0, 0, z)) for p in (a + d * 0.05, b - d * 0.05)
           for o, z in ((t * 0.36, h - 0.05), (-t * 0.36, h - 0.05), (t * 0.36, h + 0.25), (-t * 0.36, h + 0.25))]
    chull(f"{name}Cap", cap, P["hull"], parent, bev=0.02)
    if spikes:
        L = (b - a).length
        k = max(1, int(L / 1.3))
        for i in range(k):
            p = a + d * (L * (i + 0.5) / k) + Vector((0, 0, h + 0.2))
            lean = n * (0.25 if i % 2 else -0.25)
            strut(f"{name}Spike{i}", p, p + lean + Vector((0, 0, 0.75)), 0.06 * t / 0.9, P["bronze"], parent, segments=5)


def fin_blade(name, phi, pts_rz, thick, mat, parent):
    """A plate in the radial plane at angle phi, outline given as (radius, z)."""
    d = Vector((cos(phi), sin(phi), 0))
    return slab(name, [d * r + Vector((0, 0, z)) for r, z in pts_rz], thick, mat, parent, bev=0.02)


def sensor_head(P, loc, parent, span=1.6):
    """Rotating sensor vane head ("Dish") on a mast."""
    dish = empty("Dish", loc, parent)
    k = span / 1.6
    cylinder("DishMast", 0.12 * k, 0.6 * k, (0, 0, 0.3 * k), P["steel"], dish, axis="Z", segments=8)
    chull("DishHead", sym([(0.3 * k, -0.3 * k, 0.6 * k), (0.3 * k, 0.32 * k, 0.6 * k), (0.2 * k, -0.24 * k, 0.88 * k), (0.2 * k, 0.24 * k, 0.88 * k),
                           (span * 0.5, -0.2 * k, 0.5 * k), (span * 0.5, -0.06 * k, 0.44 * k), (span * 0.44, 0.14 * k, 0.6 * k)]), P["plate"], dish, bev=0.02)
    box("DishEye", (0.4 * k, 0.03, 0.08 * k), (0, 0.32 * k + 0.01, 0.7 * k), P["window"], dish, 0.01)
    return dish


def core_pod(P, parent, name="ColonyCore"):
    """L1 core: the landed command pod. Hexagonal armoured hull on three swept landing fins (the emblem's trefoil in
    plan), a ring of window slits, owner band, glowing door with a ramp, crown with sensor head and beacon mast."""
    g = empty(name, (0, 0, 0), parent)
    # Corners at 60k, so the faces are centred at 30 + 60k: the door face is 90 (front, +Y).
    tower(f"{name}Skirt", P["plate"], 3.0, 2.75, 0.0, 1.1, g)
    tower(f"{name}Body", P["hull"], 2.75, 2.05, 1.1, 4.1, g)
    tower(f"{name}Crown", P["plate"], 2.05, 1.05, 4.1, 5.0, g)
    for k in range(6):
        phi = pi / 6 + k * pi / 3
        if abs(phi - pi / 2) < 0.01:
            continue
        plate_at(f"{name}Slit{k}", face_frame(phi, 2.75 * AP, 2.05 * AP, 1.1, 4.1, 0.62), [(-0.75, -0.12), (0.75, -0.12), (0.6, 0.1), (-0.6, 0.1)],
                 P["window"], g)
        plate_at(f"{name}Band{k}", face_frame(phi, 2.75 * AP, 2.05 * AP, 1.1, 4.1, 0.16), [(-1.15, -0.1), (1.15, -0.1), (1.12, 0.1), (-1.12, 0.1)],
                 P["owner"], g)
    door = face_frame(pi / 2, 2.75 * AP, 2.05 * AP, 1.1, 4.1, 0.0)
    plate_at(f"{name}Door", door, [(-0.7, 0.0), (0.7, 0.0), (0.45, 1.7), (-0.45, 1.7)], P["window"], g, lift=0.012)
    plate_at(f"{name}DoorFrame", door, [(-0.9, -0.05), (0.9, -0.05), (0.6, 1.95), (-0.6, 1.95)], P["dark"], g, lift=0.004)
    emblem(f"{name}Emblem", face_frame(pi / 2, 2.75 * AP, 2.05 * AP, 1.1, 4.1, 0.85), 0.45, P["emblem"], g)
    y0 = 2.75 * AP + 0.05
    slab(f"{name}Ramp", [(-0.75, y0, 1.12), (0.75, y0, 1.12), (0.85, y0 + 2.2, 0.04), (-0.85, y0 + 2.2, 0.04)], 0.1, P["hull"], g)
    for k in range(3):
        phi = pi / 6 + k * 2 * pi / 3  # fins at 30, 150, 270 leave the door face clear
        fin_blade(f"{name}Fin{k}", phi, [(1.7, 3.7), (2.2, 1.0), (4.6, 0.0), (5.0, 0.28), (3.3, 3.2)], 0.32, P["hull"], g)
        fin_blade(f"{name}FinArmour{k}", phi, [(2.2, 3.2), (2.45, 1.5), (4.2, 0.45), (3.2, 2.8)], 0.42, P["plate"], g)
        d = Vector((cos(phi), sin(phi), 0))
        box(f"{name}Foot{k}", (0.9, 0.9, 0.16), d * 4.75 + Vector((0, 0, 0.08)), P["dark"], g, 0.02, rot=(0, 0, phi))
        box(f"{name}FinLight{k}", (0.2, 0.2, 0.12), d * 3.3 + Vector((0, 0, 3.25)), P["run"], g, 0.01)
    cylinder(f"{name}Mast", 0.1, 2.2, (0, -0.8, 6.0), P["steel"], g, axis="Z", segments=6)
    cylinder("Beacon", 0.13, 0.2, (0, -0.8, 7.2), P["beacon"], g, axis="Z", segments=8)
    sensor_head(P, (0, 0.25, 5.0), g, span=1.4)
    return g


def keep_tower(P, parent, name="Keep", scale=1.0, gate_phi=pi / 2, beacon=True, dish=True):
    """Spiked keep: stone podium, two tapering gunmetal tiers with window slits, a spiked crown and short spire, three
    upswept buttress fins (the emblem's trefoil in plan) and a gate glowing in the face at gate_phi (30 + 60k)."""
    g = empty(name, (0, 0, 0), parent)
    g.scale = (scale, scale, scale)
    tower(f"{name}Podium", P["stone"], 3.6, 3.2, 0.0, 1.3, g)
    tower(f"{name}Tier1", P["hull"], 2.7, 2.1, 1.3, 7.0, g)
    tower(f"{name}Ledge", P["plate"], 2.45, 2.25, 7.0, 7.5, g)
    tower(f"{name}Tier2", P["hull"], 1.95, 1.35, 7.5, 11.4, g)
    tower(f"{name}Crown", P["plate"], 1.6, 1.9, 11.4, 12.4, g)
    cylinder(f"{name}Spire", 0.7, 2.0, (0, 0, 13.4), P["hull"], g, axis="Z", segments=6, radius2=0.28)
    for k in range(6):
        phi = pi / 6 + k * pi / 3
        gate = abs((phi - gate_phi + pi) % (2 * pi) - pi) < 0.01
        for t in ((0.8,) if gate else (0.55, 0.8)):
            plate_at(f"{name}Slit{k}{t}", face_frame(phi, 2.7 * AP, 2.1 * AP, 1.3, 7.0, t), [(-0.35, -0.3), (0.35, -0.3), (0.2, 0.3), (-0.2, 0.3)],
                     P["window"], g)
        plate_at(f"{name}Slit2{k}", face_frame(phi, 1.95 * AP, 1.35 * AP, 7.5, 11.4, 0.6), [(-0.25, -0.35), (0.25, -0.35), (0.12, 0.35), (-0.12, 0.35)],
                 P["window"], g)
        plate_at(f"{name}Band{k}", face_frame(phi, 2.45 * AP, 2.25 * AP, 7.0, 7.5, 0.5), [(-1.1, -0.12), (1.1, -0.12), (1.1, 0.12), (-1.1, 0.12)],
                 P["owner"], g)
        c = Vector((cos(phi - pi / 6), sin(phi - pi / 6), 0))  # spikes from the crown's corners
        base = c * 1.75 + Vector((0, 0, 12.3))
        strut(f"{name}CrownSpike{k}", base, base + c * 0.7 + Vector((0, 0, 1.6)), 0.12, P["bronze"], g, segments=5)
        if k == 0 and beacon:
            cylinder("Beacon", 0.16, 0.26, base + c * 0.74 + Vector((0, 0, 1.68)), P["beacon"], g, axis="Z", segments=8)
    gate = face_frame(gate_phi, 2.7 * AP, 2.1 * AP, 1.3, 7.0, 0.0)
    plate_at(f"{name}Gate", gate, [(-0.8, 0.0), (0.8, 0.0), (0.45, 2.4), (-0.45, 2.4)], P["gate"], g, lift=0.012)
    plate_at(f"{name}GateFrame", gate, [(-1.05, -0.05), (1.05, -0.05), (0.62, 2.75), (-0.62, 2.75)], P["dark"], g, lift=0.005)
    emblem(f"{name}Emblem", face_frame(gate_phi, 2.7 * AP, 2.1 * AP, 1.3, 7.0, 0.6), 0.62, P["emblem"], g)
    for k in range(3):
        phi = gate_phi + pi / 3 + k * 2 * pi / 3
        fin_blade(f"{name}FinLow{k}", phi, [(1.9, 1.0), (4.6, 0.0), (3.8, 3.8), (1.9, 4.4)], 0.42, P["stone"], g)
        fin_blade(f"{name}FinHigh{k}", phi, [(1.9, 4.4), (3.8, 3.8), (5.3, 10.8), (1.75, 7.0)], 0.32, P["plate"], g)
        fin_blade(f"{name}FinTip{k}", phi, [(4.6, 7.8), (5.3, 10.8), (4.4, 9.2)], 0.36, P["owner"], g)
        d = Vector((cos(phi), sin(phi), 0))
        box(f"{name}FinLight{k}", (0.22, 0.22, 0.22), d * 5.3 + Vector((0, 0, 10.9)), P["run"], g, 0.02)
    if dish:
        sensor_head(P, (0, 0, 14.4), g, span=2.0)
    return g


def gatehouse(name, P, centre, phi, parent, gap=3.2, h=5.5, banners=True):
    """Two angular pylons either side of a glowing gate, facing outward along phi; braziers outside, banners inside."""
    g = empty(name, centre, parent)
    g.rotation_euler = (0, 0, phi - pi / 2)  # local +Y = outward
    k = h / 5.5
    for s in (-1, 1):
        x = s * (gap / 2 + 0.8 * k)
        chull(f"{name}Pylon{s}", [(x + dx * k, dy * k, 0) for dx in (-0.8, 0.8) for dy in (-0.8, 0.8)] +
              [(x + dx * k, dy * k, h) for dx in (-0.45, 0.45) for dy in (-0.45, 0.45)] + [(x, 0.0, h + 1.4 * k)], P["hull"], g, bev=0.04)
        box(f"{name}PylonLight{s}", (0.2 * k, 0.2 * k, 0.2 * k), (x, 0.0, h + 1.45 * k), P["run"], g, 0.02)
        plate_at(f"{name}PylonSlit{s}", frame((x, 0.62 * k, h * 0.55), (-1, 0, 0), (0, 1, 0.13)), [(-0.2, -0.6), (0.2, -0.6), (0.1, 0.6), (-0.1, 0.6)],
                 P["window"], g)
        brazier(f"{name}Brazier{s}", P, (s * (gap / 2 + 0.4), 2.2 * k, 0), g, 0.8 * k)
        if banners:
            banner(f"{name}Banner{s}", P, (x, -1.6 * k, 0), g, h=8.0 * k, w=1.2 * k, bh=2.8 * k)
    box(f"{name}Lintel", (gap + 1.2 * k, 1.2 * k, 0.7 * k), (0, 0, h * 0.71), P["plate"], g, 0.04)
    slab(f"{name}Gate", [(-gap / 2, 0.0, 0.0), (gap / 2, 0.0, 0.0), (gap / 2 - 0.3, 0.0, h * 0.65), (-gap / 2 + 0.3, 0.0, h * 0.65)], 0.06, P["gate"], g)
    emblem(f"{name}Emblem", frame((0, 0.61 * k, h * 0.71), (-1, 0, 0), (0, 1, 0)), 0.5 * k, P["emblem"], g)
    return g


def crates(name, P, loc, parent, rotz=0.0):
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, rotz)
    for k, (x, y, z, mat) in enumerate(((0, 0, 0.45, "plate"), (1.0, 0.1, 0.45, "hull"), (0.5, 0.05, 1.35, "plate"), (0.2, 1.0, 0.45, "hull"))):
        box(f"{name}{k}", (0.9, 0.9, 0.9), (x, y, z), P[mat], g, 0.05)
        box(f"{name}{k}Band", (0.92, 0.92, 0.1), (x, y, z), P["bronze"], g, 0.01)


def curtain(P, root, R, gate_k, h, t, tower_r, tower_h, gate_gap, gate_h, banners_on_towers=False, phase=0.0):
    """Hex curtain wall with corners at phase + 60k (face k centred at phase + 30 + 60k), a gatehouse in face gate_k
    and a tower at every corner (one tower face looking straight out)."""
    corners = [Vector((cos(phase + k * pi / 3) * R, sin(phase + k * pi / 3) * R, 0)) for k in range(6)]
    for k in range(6):
        a, b = corners[k], corners[(k + 1) % 6]
        phi = phase + pi / 6 + k * pi / 3
        if k == gate_k:
            mid = (a + b) / 2
            d = (b - a).normalized()
            half = gate_gap / 2 + 1.6 * gate_h / 5.5
            wall(f"Wall{k}a", P, a, mid - d * half, root, h=h, t=t)
            wall(f"Wall{k}b", P, mid + d * half, b, root, h=h, t=t)
            gatehouse("Gatehouse", P, mid, phi, root, gap=gate_gap, h=gate_h)
        else:
            wall(f"Wall{k}", P, a, b, root, h=h, t=t)
        c = corners[k]
        tc = phase + k * pi / 3  # the corner's direction: a tower face looks this way
        tower(f"Tower{k}", P["hull"], tower_r, tower_r * 0.72, 0.0, tower_h, root, phase=tc - pi / 6).location = c
        cylinder(f"TowerCap{k}", tower_r * 0.72, tower_h * 0.35, c + Vector((0, 0, tower_h * 1.175)), P["plate"], root, axis="Z", segments=6,
                 radius2=0.03)
        box(f"TowerLight{k}", (0.2, 0.2, 0.2) if tower_r < 2 else (0.35, 0.35, 0.35), c + Vector((0, 0, tower_h * 1.36)), P["run"], root, 0.02)
        out = Vector((cos(tc), sin(tc), 0))
        M = Matrix.Translation(c) @ face_frame(tc, tower_r * AP, tower_r * 0.72 * AP, 0.0, tower_h, 0.62)
        plate_at(f"TowerSlit{k}", M, [(-0.25, -0.4), (0.25, -0.4), (0.12, 0.4), (-0.12, 0.4)] if tower_r < 2 else
                 [(-0.4, -0.8), (0.4, -0.8), (0.2, 0.8), (-0.2, 0.8)], P["window"], root)
        if banners_on_towers:
            banner(f"TowerBanner{k}", P, c - out * (tower_r + 1.2), root, h=tower_h * 1.1, w=1.3, bh=3.2, rotz=tc + pi / 2)


# ---------------------------------------------------------------- outposts


def k_outpost(P, level):
    root = empty(f"KlingonOutpostL{level}")
    if level == 1:
        # House Encampment, just landed: command pod on its fins, two A-frames, brazier, House banner, assault-shuttle
        # pad, supply crates, and the survey rover that came down with it.
        core_pod(P, root)
        landing_pad("ShuttlePad", P, (-6.2, -4.6, 0), 3.0, root)
        aframe("Shelter0", P, (5.6, -3.6, 0), rad(28), root)
        aframe("Shelter1", P, (6.6, 1.6, 0), rad(-8), root)
        brazier("Brazier0", P, (3.4, 4.2, 0), root)
        banner("Banner0", P, (-3.4, 4.3, 0), root, h=7.0)
        crates("Crates0", P, (-6.8, 2.2, 0), root, rad(15))
        place_rover(P, root, (2.6, -8.2), -160)
    elif level == 5:
        # Governor's Keep: spiked keep with upswept buttresses inside a battered hex wall lined up with the tile
        # (corners at +-Y like the hex, R 12 so the corner towers stay inside it), a glowing gatehouse in the face at
        # 60 degrees (toward the camera), two longhouses, a pad, braziers, banners, crates and the rover heading out.
        keep_tower(P, root)  # gate face 90, buttress fins at 30, 150, 270
        curtain(P, root, R=12.0, gate_k=5, h=2.8, t=0.9, tower_r=1.3, tower_h=4.6, gate_gap=3.2, gate_h=5.5, phase=pi / 2)
        longhouse("Longhouse0", P, (-5.89, -3.4, 0), rad(30), root)
        longhouse("Longhouse1", P, (5.89, -3.4, 0), rad(-30), root)
        landing_pad("ShuttlePad", P, (-3.0, 6.6, 0), 2.1, root)
        for k, s in enumerate((-1, 1)):
            brazier(f"KeepBrazier{k}", P, (s * 1.4, 4.0, 0), root, 0.9)
        banner("Banner0", P, (6.8, 0.6, 0), root, h=6.5, rotz=rad(-37))
        crates("Crates0", P, (-7.0, 2.0, 0), root, rad(15))
        place_rover(P, root, (2.2, 6.6), -30)
    else:
        great_hall(P, root)
    return root


def place_rover(P, root, xy, heading):
    rover = k_rover(P)  # built at the origin (its decals ray-cast in place), then moved
    rover.location, rover.rotation_euler = (xy[0], xy[1], 0), (0, 0, rad(heading))
    rover.parent = root
    return rover


def great_hall(P, root):
    """L10 capital: the Great Hall on a stepped plinth reached by a grand stair between braziers, with huge upswept
    wings, a central spire, flanking keeps, a hex curtain wall with corner towers and a gatehouse, longhouses, pads.
    About three hexes across."""
    for k, (hx, y0, y1) in enumerate(((14, -17, 14), (12, -16, 12), (10.5, -15, 10))):
        box(f"Plinth{k}", (2 * hx, y1 - y0, 1.0), (0, (y0 + y1) / 2, k + 0.5), P["stone"], root, 0.05)
    for k in range(12):  # grand stair from the ground at y 17.2 up to the top tier (z 3) at y 10
        yf = 17.2 - 0.6 * k
        h = 0.25 * (k + 1)
        box(f"Stair{k}", (7.0, yf - 10.0, h), (0, (yf + 10.0) / 2, h / 2), P["stone"], root, 0.02)
    for s in (-1, 1):
        chull(f"StairCheek{s}", [(s * x, y, z) for x in (3.5, 4.3) for y, z in ((17.6, 0.0), (10.0, 0.0), (10.0, 3.6), (17.6, 0.6))], P["hull"], root, bev=0.04)
        for k, (y, z) in enumerate(((18.3, 0.0), (13.3, 1.0), (11.2, 2.0), (9.7, 3.0))):
            brazier(f"StairBrazier{s}{k}", P, (s * 5.0, y, z), root, 1.3, light=k in (0, 3))
        banner(f"StairBanner{s}", P, (s * 6.2, 9.3, 3.0), root, h=11.0, w=1.8, bh=4.8)
    z0 = 3.0
    hall = chull("Hall", sym([(7.0, -14.0, z0), (7.0, 8.5, z0), (5.8, -13.2, z0 + 8.5), (5.8, 7.8, z0 + 8.5)]), P["hull"], root, bev=0.08)
    chull("HallRoof", sym([(6.3, -13.6, z0 + 8.3), (6.3, 8.2, z0 + 8.3), (1.2, -12.5, z0 + 14.5), (1.2, 6.0, z0 + 14.5)]), P["plate"], root, bev=0.06)
    chull("HallRidge", sym([(1.3, -12.6, z0 + 14.3), (1.3, 6.1, z0 + 14.3), (0.4, -11.0, z0 + 15.4), (0.4, 5.0, z0 + 15.4)]), P["bronze"], root, bev=0.04)
    chull("PortalFrame", sym([(3.2, 8.45, z0), (3.2, 9.6, z0), (1.6, 8.45, z0 + 7.0), (1.6, 9.6, z0 + 7.0), (0.0, 9.6, z0 + 8.4), (0, 8.45, z0 + 8.4)]),
          P["plate"], root, bev=0.04)
    slab("Portal", [(-2.3, 9.63, z0), (2.3, 9.63, z0), (1.1, 9.63, z0 + 6.2), (0, 9.63, z0 + 7.2), (-1.1, 9.63, z0 + 6.2)], 0.04, P["gate"], root)
    n = Vector((0, 6.2, 2.2)).normalized()  # front face of the roof
    emblem("HallEmblem", frame(Vector((0, 8.2 - 2.2 * 0.45, z0 + 8.3 + 6.2 * 0.45)) + n * 0.03, (-1, 0, 0), n), 2.0, P["emblem"], root)
    for s in (-1, 1):
        for k in range(6):
            y = -11.0 + k * 3.4
            plate_at(f"HallSlit{s}{k}", frame_at(hall, (s * 12, y, z0 + 3.5), (-s, 0, 0), side_right(s)),
                     [(-0.45, -1.5), (0.45, -1.5), (0.25, 1.5), (-0.25, 1.5)], P["window"], root)
            chull(f"Buttress{s}{k}", [(s * 6.8, y + 1.5 + dy, z0) for dy in (-0.4, 0.4)] + [(s * 9.0, y + 1.5 + dy, z0) for dy in (-0.4, 0.4)] +
                  [(s * 5.8, y + 1.5 + dy, z0 + 7.5) for dy in (-0.3, 0.3)], P["stone"], root, bev=0.04)
        # Upswept wings from the hall's shoulders, in two plates, with owner-colour tips and red lights.
        slab(f"WingInner{s}", [(s * 6.0, 7.0, z0 + 6.0), (s * 6.0, -6.0, z0 + 5.0), (s * 12.5, -7.5, z0 + 9.5), (s * 12.0, 4.0, z0 + 11.5)], 0.7,
             P["plate"], root, bev=0.05)
        slab(f"WingOuter{s}", [(s * 12.0, 4.0, z0 + 11.5), (s * 12.5, -7.5, z0 + 9.5), (s * 17.0, -10.0, z0 + 20.0), (s * 16.4, -3.5, z0 + 23.5)], 0.6,
             P["hull"], root, bev=0.05)
        slab(f"WingTip{s}", [(s * 15.6, -4.2, z0 + 19.5), (s * 16.6, -8.4, z0 + 18.0), (s * 17.0, -10.0, z0 + 20.0), (s * 16.4, -3.5, z0 + 23.5)], 0.7,
             P["owner"], root)
        box(f"WingLight{s}", (0.5, 0.5, 0.5), (s * 16.4, -3.5, z0 + 23.7), P["run"], root, 0.04)
        strut(f"WingStrut{s}", (s * 9.0, -5.0, z0), (s * 12.3, -6.0, z0 + 9.3), 0.5, P["bronze"], root, segments=6)
        keep = keep_tower(P, root, name=f"Keep{'L' if s < 0 else 'R'}", scale=0.85, beacon=False, dish=False)
        keep.location = (s * 12.0, -19.0, 0.0)
    # Central spire through the roof, with the beacon and the sensor head.
    sp = empty("Spire", (0, -3.0, 0), root)
    tower("SpireShaft", P["hull"], 4.0, 2.8, z0 + 8.0, z0 + 24.0, sp, phase=pi / 6)
    tower("SpireCrown", P["plate"], 2.6, 3.2, z0 + 24.0, z0 + 25.6, sp, phase=pi / 6)
    cylinder("SpireNeedle", 1.1, 9.0, (0, 0, z0 + 30.1), P["hull"], sp, axis="Z", segments=6, radius2=0.05)
    for k in range(6):
        phi = k * pi / 3
        plate_at(f"SpireSlit{k}", face_frame(phi, 4.0 * AP, 2.8 * AP, z0 + 8.0, z0 + 24.0, 0.72), [(-0.5, -1.4), (0.5, -1.4), (0.25, 1.4), (-0.25, 1.4)],
                 P["window"], sp)
        c = Vector((cos(phi + pi / 6), sin(phi + pi / 6), 0))
        base = c * 3.0 + Vector((0, 0, z0 + 25.5))
        strut(f"SpireSpike{k}", base, base + c * 1.2 + Vector((0, 0, 3.0)), 0.25, P["bronze"], sp, segments=5)
    cylinder("Beacon", 0.35, 0.6, (0, 0, z0 + 34.85), P["beacon"], sp, axis="Z", segments=8)
    sensor_head(P, (0, 2.3, z0 + 25.6), sp, span=2.2)
    curtain(P, root, R=30.0, gate_k=1, h=4.2, t=1.4, tower_r=2.2, tower_h=8.0, gate_gap=4.6, gate_h=7.5, banners_on_towers=True)
    for k, (x, y, r) in enumerate(((19.5, 6.0, 30), (-19.5, 6.0, -30), (19.0, -8.0, -30), (-19.0, -8.0, 30))):
        longhouse(f"Longhouse{k}", P, (x, y, 0), rad(r), root, L=7.5, W=3.8, H=5.0)
    landing_pad("ShuttlePad0", P, (-11.0, 19.0, 0), 3.4, root)
    landing_pad("ShuttlePad1", P, (11.0, 19.0, 0), 3.4, root)
    place_rover(P, root, (11.0, 19.0), -120).location.z = 0.3


# ---------------------------------------------------------------- preview and export


def preview(target, k, stem, folder, hexes=(), game_k=None):
    """build_vehicles.preview_scene's look (same sky, sun, sand, exposure), cameras scaled by k about target so the
    rover (k = 1, target z 1.3) matches the Federation previews exactly. game_k (default k) sets the game camera's
    distance on its own: the big models get a closer game shot than their front34 scale would give."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = rad(28)
    sky.sun_rotation = rad(150)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.12
    scene.view_settings.exposure = -0.6
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = rad(2)
    sun.rotation_euler = (rad(55), 0, rad(150))
    link(sun)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40 * max(1.0, k))
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))
    P = kpalette()
    for i, c in enumerate(hexes):
        hex_outline(f"PreviewHex{i}", c, 25 / math.sqrt(3), 0.25 if i == 0 else 0.18, P["hexline"])
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.clip_end = 5000
    link(cam)
    scene.camera = cam
    target = Vector(target)
    shots = {"front34": ((7.5, 8.5, 1.9), 40), "game": ((0.0, 13.0, 14.2), 30), "rear34": ((-7.5, -8.0, 2.3), 40), "side": ((10.5, 0.4, 0.6), 38)}
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        off, lens = shots[name]
        cam.location = target + Vector(off) * (game_k or k if name == "game" else k)
        cam.data.lens = lens
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{stem}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def main():
    BV["reset"]()
    hexes, game_k = (), None
    if MODEL == "fedref":
        BV["TIER"] = 1
        root = BV["survey_rover"]()
        stem, target, k = "fed-mk1", (0, 0, 1.3), 1.0
    else:
        P = kpalette()
        if MODEL == "rover":
            root, stem, target, k = k_rover(P), "klingon-rover", (0, 0, 1.3), 1.0
        elif MODEL == "miner":
            root = k_miner(P, POSE)
            stem, target, k = "klingon-miner" + ("-deployed" if POSE == "deployed" else ""), (0, 0.4, 1.5), 1.25
        elif MODEL == "dropship":
            root, stem, target, k = k_dropship(P), "klingon-dropship", (0, 0, 2.8), 2.2
            game_k = 1.7
        else:
            root = k_outpost(P, LEVEL)
            stem = f"klingon-outpost-l{LEVEL}"
            target, k = {1: ((0, 0, 1.5), 3.0), 5: ((0, 0, 3.5), 3.7), 10: ((0, -1, 8.0), 8.5)}[LEVEL]
            game_k = {1: 2.0, 5: 2.3, 10: 5.2}[LEVEL]
            hexes = [(0, 0)] + ([(25 * cos(pi / 3 * j), 25 * sin(pi / 3 * j)) for j in range(6)] if LEVEL == 10 else [])
    if OUT:
        BV["export"](root, OUT)
    if PREVIEW:
        preview(target, k, stem, PREVIEW, hexes, game_k)


main()
