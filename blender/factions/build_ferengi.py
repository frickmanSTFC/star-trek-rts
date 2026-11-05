"""Ferengi ground kit mockups: survey rover, mining digger, drop ship and the colony core at levels 1, 5 and 10.

The Ferengi counterpart to the Federation set in blender/build_vehicles.py. Look (ferengi.md): orange-copper and
burnt-umber hulls with gold trim, crescent / horseshoe and bulbous shapes, lots of marquee bulbs and lime-lit signs,
and a price on everything. Every model here is an original design in that general style.

Run (the PyPI `bpy` module, Blender 5.x):
  /root/bpyenv/bin/python blender/factions/build_ferengi.py --model rover --preview blender/previews/factions/ferengi
  ... --model miner [--pose stowed|deployed]
  ... --model dropship
  ... --model outpost --level 1|5|10
  ... --model fedscout          the Federation Mk I Scout (build_vehicles tier 1, no paint bake) on the same camera
  ... --sheet                   compose ferengi-sheet.png from the renders already in --preview (builds each model
                                once without rendering to measure it for the labels)
Flags: --res 480 --samples 8 (iterate) / --res 800 --samples 24 (finals); --shots front34,game[,rear34,side];
--blank (no name/number text, only the decal empties); --out file.glb (export, as build_vehicles does).

Conventions kept from build_vehicles.py so the models could go into the game later: Blender +Y = front, +Z up, units =
world units (a hex is 25 across the flats); the material "Owner" is the player colour; "Beacon" (Amber material) and
"TailLamp" meshes; a spinning sensor head named "Dish"; decal spots as empties NameL / NameR / NumberL / NumberR
(L = -X side; the text lies in the empty's local XY plane facing local +Z, reading along +X; Blender scale
(max_width, size, 1)). Miner parts follow the game crawler: Leg0-3, Boom, Drill, String, Bit, Fill. Drop ship: Clamp0-3,
Gear0-3, Thruster0-1. Preview-only helpers (ground, hex outline) are named Preview*.

build_vehicles.py's helpers are loaded by exec'ing its source with the trailing main() stripped (as render_finish.py
does), so its own argument parsing sees only "--tier 1 --no-bake".
"""
import json
import math
import os
import re
import sys


def compose(spec_path):
    """The sheet itself (system python3 + PIL, no bpy): a labelled 3 x 3 grid of the renders."""
    from PIL import Image, ImageDraw, ImageFont
    with open(spec_path) as f:
        spec = json.load(f)
    cw, ch, lab, cols = 480, 320, 46, 3
    rows = (len(spec["cells"]) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cw, rows * (ch + lab) + 40), (18, 12, 8))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 17)
        small = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 13)
    except OSError:
        font = small = ImageFont.load_default()
    d.text((10, 10), "Ferengi ground kit mockups - front 3/4 at build_vehicles' rover camera x scale; last cell: game camera",
           fill=(232, 192, 74), font=font)
    for i, c in enumerate(spec["cells"]):
        x, y = (i % cols) * cw, 40 + (i // cols) * (ch + lab)
        if os.path.exists(c["path"]):
            sheet.paste(Image.open(c["path"]).convert("RGB").resize((cw, ch), Image.LANCZOS), (x, y))
        else:
            d.text((x + 20, y + 140), "missing " + os.path.basename(c["path"]), fill=(200, 80, 60), font=small)
        d.rectangle((x, y + ch, x + cw - 1, y + ch + lab - 1), fill=(40, 22, 12))
        d.text((x + 8, y + ch + 4), c["label"], fill=(190, 200, 215) if c["fed"] else (200, 232, 106), font=font)
        d.text((x + 8, y + ch + 26), c["caption"], fill=(240, 232, 220), font=small)
    sheet.save(spec["out"])
    print("sheet", spec["out"])


if "--compose" in sys.argv:
    compose(sys.argv[sys.argv.index("--compose") + 1])
    sys.exit(0)

import bpy  # first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
BV_PATH = os.path.join(HERE, "..", "build_vehicles.py")
DEFAULT_PREVIEW = os.path.join(HERE, "..", "previews", "factions", "ferengi")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


MODEL = arg("--model", "rover")
LEVEL = int(arg("--level", "1"))
POSE = arg("--pose", "stowed")
PREVIEW = arg("--preview")
RES = int(arg("--res", "480"))
SAMPLES = int(arg("--samples", "8"))
SHOTS = [s.strip() for s in arg("--shots", "front34,game").split(",") if s.strip()]
BLANK = "--blank" in argv
OUT = arg("--out")
NAME = arg("--name", "LOBE").upper()
NUMBER = arg("--number", "62")
if MODEL not in ("rover", "miner", "dropship", "outpost", "fedscout") and "--sheet" not in argv:
    sys.exit(f"--model takes rover, miner, dropship, outpost or fedscout, not {MODEL}")
if LEVEL not in (1, 5, 10):
    sys.exit(f"--level must be 1, 5 or 10, not {LEVEL}")
if POSE not in ("stowed", "deployed"):
    sys.exit(f"--pose must be stowed or deployed, not {POSE}")


def load_build_vehicles():
    with open(BV_PATH) as f:
        src = f.read()
    src = re.sub(r"^main\(\)\s*$", "", src, flags=re.M)
    saved = sys.argv
    sys.argv = [BV_PATH, "--tier", "1", "--no-bake"]
    ns = {"__name__": "build_vehicles", "__file__": BV_PATH}
    try:
        exec(compile(src, BV_PATH, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


NS = load_build_vehicles()
reset, material, link, empty = NS["reset"], NS["material"], NS["link"], NS["empty"]
mesh_object, box, cylinder, prism, text, strut = NS["mesh_object"], NS["box"], NS["cylinder"], NS["prism"], NS["text"], NS["strut"]

PI = math.pi
R90 = PI / 2

# ---------------------------------------------------------------- materials


def palette():
    """Ferengi materials. The Federation palette() runs first so the shared game names (Owner, Amber, TailLamp, Lamp,
    Tyre) exist; Owner is then given a teal preview colour (it is recoloured per player in the game)."""
    F = NS["palette"]()
    P = {
        "copper": material("FerCopper", (0.62, 0.25, 0.075), 0.6, 0.3, coat=0.35),
        "copperdark": material("FerCopperDark", (0.42, 0.16, 0.045), 0.55, 0.38),
        "umber": material("FerUmber", (0.15, 0.055, 0.022), 0.25, 0.5, coat=0.2),
        "gold": material("FerGold", (0.88, 0.6, 0.16), 1.0, 0.2),
        "latinum": material("Latinum", (1.0, 0.74, 0.25), 1.0, 0.12, emission=(1.0, 0.7, 0.2), strength=0.25),
        "lime": material("SignLime", (0.5, 0.85, 0.12), 0.0, 0.4, emission=(0.55, 0.95, 0.12), strength=4.0),
        "signback": material("SignBack", (0.03, 0.014, 0.008), 0.2, 0.35, coat=0.5),
        "cream": material("TagCream", (0.86, 0.78, 0.62), 0.0, 0.6),
        "ink": material("TagInk", (0.08, 0.03, 0.015), 0.0, 0.5),
        "glass": material("FerGlass", (0.012, 0.008, 0.006), 0.4, 0.04, emission=(1.0, 0.5, 0.15), strength=0.12, coat=1.0),
        "window": material("WindowLit", (0.05, 0.03, 0.015), 0.2, 0.1, emission=(1.0, 0.62, 0.28), strength=1.6, coat=1.0),
        "bulb": material("MarqueeBulb", (1.0, 0.85, 0.55), 0.0, 0.2, emission=(1.0, 0.8, 0.45), strength=9.0),
        "engine": material("EngineGlow", (1.0, 0.5, 0.15), 0.0, 0.3, emission=(1.0, 0.42, 0.1), strength=8.0),
        "ore": material("Ore", (0.17, 0.15, 0.14), 0.1, 0.9),
        "decal": material("FerDecal", (0.95, 0.72, 0.22), 0.5, 0.35),
        "scorch": material("Scorch", (0.13, 0.1, 0.08), 0.0, 0.95),
        "pad": material("PadUmber", (0.2, 0.1, 0.05), 0.0, 0.8),
        "padline": material("PadGold", (0.85, 0.6, 0.15), 0.0, 0.5),
        "steel": F["steel"],
        "dark": F["dark"],
        "tyre": F["tyre"],
        "lamp": F["lamp"],
        "amber": F["amber"],
        "redlamp": F["redlamp"],
        "owner": material("Owner", (0.03, 0.3, 0.34), 0.1, 0.4),
        "clear": material("VaultGlass", (0.95, 0.9, 0.8), 0.0, 0.04),
    }
    P["clear"].node_tree.nodes["Principled BSDF"].inputs["Transmission Weight"].default_value = 1.0
    return P


# ---------------------------------------------------------------- shape helpers


def smooth(o, angle=40):
    o.data.shade_smooth()
    o.data.set_sharp_from_angle(angle=math.radians(angle))
    return o


def blob(name, half, loc, mat, parent=None, p=2.0, segs=(40, 20), zmin=None, zmax=None, shape=None, open_top=False):
    """Superellipsoid |x/a|^p + |y/b|^p + |z/c|^p = 1 (p 2 = ellipsoid, higher = boxier). zmin/zmax (in units of the
    half height) flatten the bottom / top; open_top deletes everything above zmax instead (a tub). shape(x, y, z) ->
    (x, y, z) reshapes the scaled vertices (tapers)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs[0], v_segments=segs[1], radius=1.0)
    e = 2.0 / p
    for v in bm.verts:
        c = [math.copysign(abs(q) ** e, q) for q in v.co]
        v.co = Vector(c)
    if open_top and zmax is not None:
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z > zmax + 1e-6], context="VERTS")
    else:
        for v in bm.verts:
            if zmin is not None and v.co.z < zmin:
                v.co.z = zmin
            if zmax is not None and v.co.z > zmax:
                v.co.z = zmax
    for v in bm.verts:
        x, y, z = v.co.x * half[0], v.co.y * half[1], v.co.z * half[2]
        if shape:
            x, y, z = shape(x, y, z)
        v.co = Vector((x, y, z))
    if zmin is not None and not open_top:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    o = mesh_object(name, bm, mat, parent, loc)
    return smooth(o, 60)


def rim_loop(o):
    """World-space-free (object-local) ordered boundary loop of an open mesh, for a trim ring along it."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    pts = [v.co.copy() for v in bm.verts if v.is_boundary]
    bm.free()
    pts.sort(key=lambda q: math.atan2(q.y, q.x))
    return pts


def orient(o, xaxis, yaxis, loc):
    x = Vector(xaxis).normalized()
    y = Vector(yaxis).normalized()
    z = x.cross(y)
    o.matrix_basis = Matrix(((x.x, y.x, z.x, loc[0]), (x.y, y.y, z.y, loc[1]), (x.z, y.z, z.z, loc[2]), (0, 0, 0, 1)))
    return o


def crescent(name, R, w_mid, w_tip, arc, depth, mat, parent=None, segs=40, bev=0.03):
    """A crescent / horseshoe in the local XY plane around the origin: middle (thickest, w_mid) on +Y, horns sweeping
    round toward -Y to +-arc/2 degrees, thinning to w_tip. Outer edge on the circle R. Extruded +-depth/2 along Z."""
    bm = bmesh.new()
    half = math.radians(arc) / 2
    rows = []
    for i in range(segs + 1):
        t = -half + 2 * half * i / segs
        w = w_tip + (w_mid - w_tip) * max(0.0, math.cos(t / half * R90)) ** 0.8
        d = Vector((math.sin(t), math.cos(t), 0))
        o_, i_ = d * R, d * (R - w)
        rows.append([bm.verts.new((o_.x, o_.y, depth / 2)), bm.verts.new((i_.x, i_.y, depth / 2)),
                     bm.verts.new((i_.x, i_.y, -depth / 2)), bm.verts.new((o_.x, o_.y, -depth / 2))])
    for a, b in zip(rows, rows[1:]):
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(rows[0])
    bm.faces.new(list(reversed(rows[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_object(name, bm, mat, parent)
    if bev:
        NS["bevel"](o, bev, 2, 30)
    return smooth(o, 35)


def tube(name, pts, r, mat, parent=None, closed=False, segs=8):
    """A round bar through the points (trim, rails, ribs): one mesh of cylinders with a ball at every joint."""
    bm = bmesh.new()
    pts = [Vector(p) for p in pts]
    pairs = list(zip(pts, pts[1:])) + ([(pts[-1], pts[0])] if closed else [])
    for a, b in pairs:
        d = b - a
        if d.length < 1e-6:
            continue
        m = Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=d.length, matrix=m)
    for p in pts:
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r, matrix=Matrix.Translation(p))
    o = mesh_object(name, bm, mat, parent)
    return smooth(o, 50)


def bulbs(name, pts, r, mat, parent=None):
    """Marquee bulbs: one mesh of small spheres."""
    bm = bmesh.new()
    for p in pts:
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r, matrix=Matrix.Translation(p))
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return smooth(o, 80)


def cubes(name, items, mat, parent=None):
    """Many plain boxes in one mesh: items = [(size, loc, rot_euler)]."""
    bm = bmesh.new()
    for size, loc, rot in items:
        m = Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    return mesh_object(name, bm, mat, parent)


def se_points(ax, ay, z, n, p=2.0, skip=None, cy=0.0):
    """n points round the superellipse |x/ax|^p + |(y-cy)/ay|^p = 1 at height z."""
    out = []
    e = 2.0 / p
    for k in range(n):
        t = 2 * PI * k / n
        c, s = math.cos(t), math.sin(t)
        q = (math.copysign(abs(c) ** e, c) * ax, cy + math.copysign(abs(s) ** e, s) * ay, z)
        if skip and skip(q):
            continue
        out.append(q)
    return out


def circle_points(r, z, n, a0=0.0, a1=2 * PI, cx=0.0, cy=0.0):
    return [(cx + r * math.cos(a0 + (a1 - a0) * k / n), cy + r * math.sin(a0 + (a1 - a0) * k / n), z) for k in range(n)]


def sign(name, body, w, h, loc, yaw, P, parent=None, d=0.08, glow="lime", back="signback", frame=True, two_sided=False,
         marquee=0, size=None, legs=None):
    """A sign board facing local -Y (yaw turns it: 0 faces -Y, pi faces +Y, +pi/2 faces +X, -pi/2 faces -X) with
    lit text, a gold frame, optionally a ring of marquee bulbs and two gold legs reaching `legs` below the board's
    centre (free-standing signs). Returns its empty."""
    s = empty(name, loc, parent)
    s.rotation_euler = (0, 0, yaw)
    box(f"{name}Board", (w, d, h), (0, 0, 0), P[back], s, bev=min(0.02, d * 0.3))
    f = max(0.04, h * 0.08)
    if frame:
        box(f"{name}Frame", (w + 2 * f, d * 0.7, h + 2 * f), (0, 0, 0), P["gold"], s, bev=min(0.02, f * 0.4))
    tsize = size or h * 0.55
    text(f"{name}Text", body, tsize, (0, -d / 2 - 0.004, 0), (R90, 0, 0), P[glow], s, max_width=w * 0.88)
    if two_sided:
        text(f"{name}TextB", body, tsize, (0, d / 2 + 0.004, 0), (R90, 0, PI), P[glow], s, max_width=w * 0.88)
    if legs:
        for k in (-1, 1):
            box(f"{name}Leg{k}", (0.07, 0.07, legs), (k * w * 0.36, 0, -legs / 2), P["gold"], s, 0)
    if marquee:
        hw, hh = w / 2 + f, h / 2 + f
        per = 4 * (hw + hh)
        pts = []
        for k in range(marquee):
            u = per * k / marquee  # distance along the frame, clockwise from the top-left corner
            if u < 2 * hw:
                pts.append((-hw + u, -d * 0.4, hh))
            elif u < 2 * hw + 2 * hh:
                pts.append((hw, -d * 0.4, hh - (u - 2 * hw)))
            elif u < 4 * hw + 2 * hh:
                pts.append((hw - (u - 2 * hw - 2 * hh), -d * 0.4, -hh))
            else:
                pts.append((-hw, -d * 0.4, -hh + (u - 4 * hw - 2 * hh)))
        bulbs(f"{name}Bulbs", pts, max(0.025, f * 0.45), P["bulb"], s)
    return s


def tag(name, body, w, loc, yaw, P, parent=None, tilt=0.0):
    """A cream price tag with ink lettering and a gold grommet (flush, facing local -Y)."""
    s = empty(name, loc, parent)
    s.rotation_euler = (tilt, 0, yaw)
    h = w * 0.45
    box(f"{name}Card", (w, 0.025, h), (0, 0, 0), P["cream"], s, bev=0.006)
    cylinder(f"{name}Grommet", h * 0.1, 0.035, (-w / 2 + h * 0.2, 0, 0), P["gold"], s, axis="Y", segments=12)
    text(f"{name}Text", body, h * 0.42, (w * 0.06, -0.017, 0), (R90, 0, 0), P["ink"], s, max_width=w * 0.72)
    return s


def decal(marker, body, size, loc, rot, max_width, P, parent):
    """A decal spot (game contract: empty with the text's matrix and Blender scale (width, size, 1)); with text on it
    unless --blank."""
    width = max_width
    if marker.startswith("Number"):
        probe = NS["text_mesh"](f"{marker}Probe", "000", size)
        xs = [v.co.x for v in probe.vertices]
        bpy.data.meshes.remove(probe)
        width = min(max_width, math.ceil((max(xs) - min(xs)) * 100) / 100)
    e = bpy.data.objects.new(marker, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.4
    e.matrix_basis = Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((width, size, 1.0, 1.0))
    link(e, parent)
    if not BLANK:
        text(f"{marker}Text", body, size, loc, rot, P["decal"], parent, max_width=max_width)
    return e


def side_rot(s):
    """Euler for text lying on the +X (s=1) or -X (s=-1) side, reading front-to-back correctly from outside."""
    return (R90, 0, R90 if s > 0 else -R90)


def stadium(c, r, z0, n_end=10):
    """Side profile (y, z) of a track: two half circles of radius r centred at y=+-c, bottom at z0, CCW from +X."""
    pts = []
    for k in range(n_end + 1):
        a = -R90 + PI * k / n_end
        pts.append((c + r * math.cos(a), z0 + r + r * math.sin(a)))
    for k in range(n_end + 1):
        a = R90 + PI * k / n_end
        pts.append((-c + r * math.cos(a), z0 + r + r * math.sin(a)))
    return pts


def stadium_walk(c, r, z0, n):
    """n evenly spaced (y, z, normal angle) round the same stadium, for track cleats."""
    per = 4 * c + 2 * PI * r
    out = []
    for k in range(n):
        u = per * k / n
        if u < 2 * c:
            out.append((-c + u, z0 + 2 * r, R90))
        elif u < 2 * c + PI * r:
            a = R90 - (u - 2 * c) / r
            out.append((c + r * math.cos(a), z0 + r + r * math.sin(a), a))
        elif u < 4 * c + PI * r:
            out.append((c - (u - 2 * c - PI * r), z0, -R90))
        else:
            a = -R90 - (u - 4 * c - PI * r) / r
            out.append((-c + r * math.cos(a), z0 + r + r * math.sin(a), a))
    return out


def wheel_parts(P):
    return {"tyre": P["tyre"], "hub": P["gold"], "panel": P["copperdark"], "steel": P["gold"]}


def ears(name, R, w_mid, w_tip, arc, depth, P, parent, loc, mat=None):
    """The kit's signature sensor: a crescent standing upright facing +-Y, horns up (like spread ears)."""
    c = crescent(name, R, w_mid, w_tip, arc, depth, mat or P["gold"], parent)
    return orient(c, (1, 0, 0), (0, 0, -1), loc)


# ---------------------------------------------------------------- survey rover


def rover(P, parent=None, loc=(0, 0, 0)):
    """Prospector survey rover: a bulbous copper pod on four wheels under crescent fenders, bubble canopy, a roof
    billboard advertising the assay rate and a spinning crescent sensor ("Dish") at the back. ~4.3 x 2.5 x 2.2 (2.6
    to the sensor horns)."""
    root = empty("FerengiRover", loc, parent)
    hx, hy, hz, zc = 1.05, 2.0, 0.6, 1.3
    blob("Chassis", (0.95, 1.95, 0.28), (0, 0, 0.72), P["umber"], root, p=3)

    def nose(x, y, z):  # the top slopes down toward the nose
        if y > 0 and z > 0:
            z *= 1 - 0.18 * (y / hy) ** 2
        return x, y, z
    blob("Hull", (hx, hy, hz), (0, 0, zc), P["copper"], root, p=3, segs=(48, 24), shape=nose)
    # Gold waist band with marquee bulbs, owner stripe below it.
    blob("WaistTrim", (hx + 0.02, hy + 0.02, 0.03), (0, 0, zc), P["gold"], root, p=3, segs=(48, 6))
    blob("OwnerStripe", (hx + 0.012, hy + 0.022, 0.075), (0, 0, 1.13), P["owner"], root, p=3, segs=(48, 6))
    bulbs("WaistBulbs", se_points(hx + 0.035, hy + 0.035, zc, 40, p=3), 0.028, P["bulb"], root)

    # Canopy: amber-tinted bubble fused into the front of the roof, gold spine and collar.
    blob("Canopy", (0.72, 0.78, 0.42), (0, 0.95, 1.74), P["glass"], root, p=2, segs=(32, 16))
    tube("CanopySpine", [(0, 0.95 + 0.8 * math.sin(t), 1.74 + 0.44 * math.cos(t)) for t in [-1.2 + 0.25 * k for k in range(12)]],
         0.03, P["gold"], root)

    # Crescent fenders over each wheel (horns down), gold edge; wheels on short arms.
    axles = (1.35, -1.35)
    wr, ww, wx = 0.5, 0.36, 1.07
    WP = wheel_parts(P)
    for y in axles:
        for s in (-1, 1):
            f = crescent(f"Fender{y}{s}", 0.66, 0.13, 0.05, 150, 0.4, P["copper"], root)
            orient(f, (0, 1, 0), (0, 0, 1), (s * wx, y, wr))
            tube(f"FenderEdge{y}{s}", [(s * (wx + 0.2), y + 0.67 * math.sin(t), wr + 0.67 * math.cos(t))
                                      for t in [math.radians(-75 + 15 * k) for k in range(11)]], 0.022, P["gold"], root)
            box(f"Arm{y}{s}", (0.42, 0.16, 0.14), (s * 0.72, y, 0.52), P["umber"], root, 0.03)
            NS["wheel"](f"Wheel{'F' if y > 0 else 'R'}{'L' if s < 0 else 'R'}", s * wx, y, wr, ww, WP, root)

    # Nameplates (raised umber panels) between the wheels: name and number decals.
    for s in (-1, 1):
        box(f"NamePlate{s}", (0.17, 1.24, 0.34), (s * 0.98, 0, 1.55), P["umber"], root, 0.03)
        box(f"NamePlateTrim{s}", (0.15, 1.3, 0.05), (s * 0.99, 0, 1.38), P["owner"], root, 0.01)
        x = s * 1.07
        decal("NameR" if s > 0 else "NameL", NAME, 0.15, (x, 0, 1.62), side_rot(s), 0.95, P, root)
        decal("NumberR" if s > 0 else "NumberL", NUMBER, 0.12, (x, 0, 1.46), side_rot(s), 0.5, P, root)

    # Front: bumper with headlamps in gold bezels, amber markers, crescent badge.
    box("FrontBumper", (1.7, 0.22, 0.24), (0, 1.98, 0.8), P["umber"], root, 0.05)
    for s in (-1, 1):
        cylinder(f"HeadlampBezel{s}", 0.12, 0.04, (s * 0.52, 2.09, 0.8), P["gold"], root, axis="Y", segments=20)
        cylinder(f"Headlamp{s}", 0.09, 0.05, (s * 0.52, 2.1, 0.8), P["lamp"], root, axis="Y", segments=20)
        box(f"Marker{s}", (0.12, 0.04, 0.07), (s * 0.8, 2.09, 0.8), P["amber"], root, 0.01)
    badge = crescent("NoseBadge", 0.17, 0.07, 0.02, 200, 0.05, P["gold"], root, segs=24, bev=0.01)
    orient(badge, (1, 0, 0), (0, 0, -1), (0, 2.1, 0.87))
    # Rear: bumper, tail lamps, a price tag.
    box("RearBumper", (1.6, 0.2, 0.22), (0, -1.98, 0.8), P["umber"], root, 0.05)
    for s in (-1, 1):
        box(f"TailLamp{s}", (0.26, 0.04, 0.1), (s * 0.6, -2.09, 0.82), P["redlamp"], root, 0.01)
    tag("RearTag", "FOR LEASE", 0.6, (0, -2.095, 0.81), 0, P, root)

    # Roof billboard: two-sided lit sign on gold posts.
    sign("RoofSign", "ASSAYS 2 STRIPS", 1.05, 0.3, (0, -0.45, 2.1), R90, P, root, d=0.08, two_sided=True, marquee=24)
    for y in (-0.85, -0.05):
        cylinder(f"RoofSignPost{y}", 0.025, 0.2, (0, y, 1.9), P["gold"], root, axis="Z", segments=8)

    # Beacon.
    cylinder("BeaconBase", 0.08, 0.1, (0.55, -1.6, 1.74), P["gold"], root, axis="Z", segments=14)
    cylinder("Beacon", 0.065, 0.12, (0.55, -1.6, 1.84), P["amber"], root, axis="Z", segments=14)

    # Crescent sensor on a mast ("Dish" spins in the game), lime sensor eye in its cup.
    dish = empty("Dish", (0, -1.25, 1.8), root)
    cylinder("DishMast", 0.05, 0.26, (0, 0, 0.12), P["gold"], dish, axis="Z", segments=12)
    ears("DishEars", 0.55, 0.16, 0.05, 200, 0.08, P, dish, (0, 0, 0.8))
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.09)
    smooth(mesh_object("DishEye", bm, P["lime"], dish, (0, 0, 0.5)))
    return root


# ---------------------------------------------------------------- mining digger


def miner_pose(parts, deployed):
    boom, drill, string, bit, legs = parts
    if deployed:
        boom.rotation_euler = (math.radians(150), 0, 0)
        drill.rotation_euler = (math.radians(-150), 0, 0)
        string.scale = (1, 1, 0.8)
        bit.location = (0, 0, -0.7 - 0.8)
        for k, (leg, s, e) in enumerate(legs):
            leg.rotation_euler = (0, -s * math.radians(20), 0)
    else:
        boom.rotation_euler = (0, 0, 0)
        drill.rotation_euler = (0, 0, 0)
        string.scale = (1, 1, 0.02)
        bit.location = (0, 0, -0.72)
        for k, (leg, s, e) in enumerate(legs):
            leg.rotation_euler = (-e * R90, 0, 0)


def miner(P, deployed=False):
    """Concession digger: twin tracks under gold-wheeled guards, a bulbous copper cab, an open ore hopper with a lit
    price ticker, a boom that swings forward and down to plant the drill, four stabiliser legs. ~5.4 x 2.8 x 3.1."""
    root = empty("FerengiMiner")
    tx, tw, c, r = 0.92, 0.66, 2.15, 0.5
    for s in (-1, 1):
        t = prism(f"Track{s}", stadium(c, r, 0.0), tw, P["tyre"], root, bev=0.04)
        t.location.x = s * tx
        cubes(f"Cleats{s}", [((tw + 0.02, 0.1, 0.05), (s * tx, y + 0.02 * math.cos(a), z + 0.02 * math.sin(a)), (a + R90, 0, 0))
                             for y, z, a in stadium_walk(c, r, 0.0, 44)], P["tyre"], root)
        for k in range(5):
            cylinder(f"RoadWheel{s}{k}", 0.3, 0.06, (s * (tx + tw / 2 + 0.01), -1.8 + 0.9 * k, 0.5), P["gold"], root, segments=24)
            cylinder(f"RoadHub{s}{k}", 0.1, 0.08, (s * (tx + tw / 2 + 0.03), -1.8 + 0.9 * k, 0.5), P["umber"], root, segments=12)
        for y in (-c, c):
            cylinder(f"Sprocket{s}{y}", 0.38, 0.05, (s * (tx + tw / 2 + 0.005), y, 0.5), P["copperdark"], root, segments=24)
        # Guard over the track: copper, owner stripe on its edge, marquee bulbs along the top.
        box(f"TrackGuard{s}", (tw + 0.08, 4.9, 0.1), (s * tx, 0, 1.06), P["copper"], root, 0.03)
        box(f"GuardOwner{s}", (0.02, 4.7, 0.06), (s * (tx + tw / 2 + 0.045), 0, 1.06), P["owner"], root, 0)
        bulbs(f"GuardBulbs{s}", [(s * (tx + 0.26), -2.3 + 0.4 * k, 1.125) for k in range(12)], 0.03, P["bulb"], root)
    box("Deck", (1.3, 4.8, 0.45), (0, 0, 0.9), P["umber"], root, 0.04)

    # Cab: bulbous pod with an amber bubble screen, gold band, owner band, headlamps, nameplates.
    blob("Cab", (1.2, 1.05, 0.75), (0, 1.6, 1.75), P["copper"], root, p=3)
    blob("CabGlass", (0.95, 0.5, 0.42), (0, 2.2, 2.0), P["glass"], root, p=2, segs=(32, 16))
    blob("CabTrim", (1.22, 1.07, 0.03), (0, 1.6, 1.75), P["gold"], root, p=3, segs=(40, 6))
    blob("CabOwner", (1.205, 1.055, 0.04), (0, 1.6, 1.6), P["owner"], root, p=3, segs=(40, 6))
    bulbs("CabBulbs", se_points(1.235, 1.085, 1.75, 30, p=3, cy=1.6, skip=lambda q: q[1] > 2.4 and abs(q[0]) < 0.5), 0.028,
          P["bulb"], root)
    for s in (-1, 1):
        cylinder(f"HeadlampBezel{s}", 0.13, 0.05, (s * 0.6, 2.55, 1.38), P["gold"], root, axis="Y", segments=20)
        cylinder(f"Headlamp{s}", 0.1, 0.07, (s * 0.6, 2.57, 1.38), P["lamp"], root, axis="Y", segments=20)
        box(f"NamePlate{s}", (0.12, 1.0, 0.3), (s * 1.15, 1.55, 2.0), P["umber"], root, 0.025)
        x = s * 1.212
        decal("NameR" if s > 0 else "NameL", NAME, 0.13, (x, 1.55, 2.06), side_rot(s), 0.85, P, root)
        decal("NumberR" if s > 0 else "NumberL", NUMBER, 0.1, (x, 1.55, 1.93), side_rot(s), 0.4, P, root)
    cylinder("BeaconBase", 0.08, 0.06, (0.6, 1.3, 2.45), P["gold"], root, axis="Z", segments=14)
    cylinder("Beacon", 0.065, 0.12, (0.6, 1.3, 2.53), P["amber"], root, axis="Z", segments=14)

    # Ore hopper (open tub), gold rim, ore "Fill" inside, lit price tickers on its flanks, lease sign at the back.
    hop = blob("Hopper", (1.25, 1.2, 0.62), (0, -1.25, 1.62), P["copper"], root, p=4, zmax=0.75, open_top=True)
    sol = hop.modifiers.new("Thick", "SOLIDIFY")
    sol.thickness = 0.06
    rim = rim_loop(hop)
    tube("HopperRim", [Vector((q.x, q.y - 1.25, q.z + 1.62)) for q in rim], 0.05, P["gold"], root, closed=True)
    fill = empty("Fill", (0, -1.25, 1.62), root)
    blob("FillOre", (1.1, 1.05, 0.38), (0, 0, 0.1), P["ore"], fill, p=3, zmax=0.75, segs=(32, 12))
    import random
    rng = random.Random(7)
    lumps = []
    for k in range(26):
        lumps.append((rng.uniform(-0.85, 0.85), rng.uniform(-0.8, 0.8), 0.1 + 0.38 * 0.75 + rng.uniform(-0.02, 0.03)))
    bm = bmesh.new()
    for p in lumps:
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=rng.uniform(0.07, 0.14), matrix=Matrix.Translation(p))
    mesh_object("FillLumps", bm, P["ore"], fill)
    for s in (-1, 1):
        sign(f"OreTicker{s}", "ORE 12 STRIPS / T", 1.5, 0.36, (s * 1.25, -1.25, 1.6), s * R90, P, root, d=0.06, marquee=30)
    sign("LeaseSign", "MINING RIGHTS LEASED", 1.5, 0.28, (0, -2.46, 1.55), 0, P, root, d=0.06)
    for s in (-1, 1):
        box(f"TailLamp{s}", (0.3, 0.04, 0.07), (s * tx, -2.465, 1.06), P["redlamp"], root, 0.01)

    # Boom: gold hinge yoke on the cab roof; stowed, the boom lies back over the cab with the drill hanging in the gap
    # in front of the hopper; it swings forward 150 degrees to plant the drill ahead of the tracks.
    for s in (-1, 1):
        box(f"BoomYoke{s}", (0.1, 0.34, 0.4), (s * 0.25, 2.0, 2.62), P["gold"], root, 0.02)
    cylinder("BoomPin", 0.07, 0.62, (0, 2.0, 2.8), P["umber"], root, segments=14)
    boom = empty("Boom", (0, 2.0, 2.8), root)
    box("BoomArm", (0.3, 1.85, 0.22), (0, -0.83, 0), P["copper"], boom, 0.05)
    for s in (-1, 1):
        box(f"BoomStrip{s}", (0.02, 1.65, 0.06), (s * 0.155, -0.83, 0), P["gold"], boom, 0)
    bulbs("BoomBulbs", [(0, -0.25 - 0.3 * k, 0.12) for k in range(5)], 0.026, P["bulb"], boom)
    drill = empty("Drill", (0, -1.75, 0), boom)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=10, radius=0.28)
    for v in bm.verts:
        v.co.z = max(v.co.z, 0) * 0.7
    smooth(mesh_object("DrillMotor", bm, P["copper"], drill))
    cylinder("DrillHousing", 0.3, 0.65, (0, 0, -0.35), P["copperdark"], drill, axis="Z", segments=28)
    for z in (-0.06, -0.64):
        cylinder(f"DrillRing{z}", 0.32, 0.05, (0, 0, z), P["gold"], drill, axis="Z", segments=28)
    cylinder("DrillLight", 0.305, 0.04, (0, 0, -0.35), P["lime"], drill, axis="Z", segments=28)
    string = empty("String", (0, 0, -0.7), drill)
    cylinder("StringShaft", 0.07, 1.0, (0, 0, -0.5), P["steel"], string, axis="Z", segments=12)
    bit = empty("Bit", (0, 0, -0.72), drill)
    cylinder("BitCone", 0.03, 0.45, (0, 0, -0.225), P["gold"], bit, axis="Z", segments=20, radius2=0.24)
    for k in range(3):
        f = cylinder(f"BitFlight{k}", 0.25 - 0.06 * k, 0.025, (0, 0, -0.08 - 0.12 * k), P["steel"], bit, axis="Z", segments=20)
        f.rotation_euler = (math.radians(12), 0, math.radians(60 * k))

    # Stabiliser legs at the track corners: fold along the track side, swing down and out to plant.
    legs = []
    for k, (s, e) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        y = e * 2.0
        box(f"LegMount{k}", (0.12, 0.22, 0.3), (s * 1.31, y, 0.95), P["gold"], root, 0.02)
        leg = empty(f"Leg{k}", (s * 1.36, y, 0.85), root)
        cylinder(f"Leg{k}Strut", 0.06, 0.85, (0, 0, -0.42), P["umber"], leg, axis="Z", segments=12)
        cylinder(f"Leg{k}Ram", 0.04, 0.5, (0, 0, -0.62), P["steel"], leg, axis="Z", segments=10)
        cylinder(f"Leg{k}Pad", 0.11, 0.06, (0, 0, -0.87), P["gold"], leg, axis="Z", segments=16)
        legs.append((leg, s, e))
    miner_pose((boom, drill, string, bit, legs), deployed)
    return root


# ---------------------------------------------------------------- drop ship


def dropship(P):
    """Freight yoke: a crescent deck (horns aft) on two bulbous engine pods, a bulbous bridge at the bow, and a yoke
    beam across the open middle from which four clamps hold one vehicle (here the Ferengi rover). ~9.8 x 8.8 x 5.5."""
    root = empty("FerengiDropship")
    O = Vector((0, -0.8, 3.5))
    R, wm, wt, arc = 4.4, 2.8, 1.2, 230
    deck = crescent("Deck", R, wm, wt, arc, 0.8, P["copper"], root, segs=60, bev=0.12)
    deck.location = O
    inlay = crescent("DeckInlay", R - 0.25, wm - 0.55, wt - 0.4, arc - 8, 0.06, P["umber"], root, segs=60, bev=0.0)
    inlay.location = O + Vector((0, 0, 0.41))
    half = math.radians(arc) / 2
    edge = [O + Vector((R * math.sin(t), R * math.cos(t), 0.4)) for t in [-half + 2 * half * k / 48 for k in range(49)]]
    tube("DeckTrim", edge, 0.06, P["gold"], root)
    bulbs("DeckBulbs", [p + Vector((0, 0, 0.08)) for p in edge[1:-1:2]], 0.06, P["bulb"], root)

    def inner(t):
        w = wt + (wm - wt) * max(0.0, math.cos(t / half * R90)) ** 0.8
        return O + Vector(((R - w) * math.sin(t), (R - w) * math.cos(t), -0.42))
    tube("BayGlow", [inner(-half + 2 * half * k / 40) for k in range(41)], 0.05, P["lime"], root)

    # Bridge: bulbous bow pod with an amber window band, gold brow ribs, beacon, lit roof sign.
    blob("Bridge", (1.5, 1.3, 0.75), (0, 3.0, 4.0), P["copper"], root, p=2.5)
    blob("BridgeWindows", (1.36, 1.16, 0.16), (0, 3.12, 4.12), P["window"], root, p=2.5, segs=(40, 8))
    for x in (-0.55, 0.55):
        tube(f"BrowRib{x}", meridian((1.5, 1.3, 0.75), (0, 3.0, 4.0), 2.5, x, [-1.1 + 0.2 * k for k in range(12)], 0.03),
             0.04, P["gold"], root)
    cylinder("BeaconBase", 0.1, 0.1, (0.9, 2.4, 4.58), P["gold"], root, axis="Z", segments=14)
    cylinder("Beacon", 0.08, 0.16, (0.9, 2.4, 4.7), P["amber"], root, axis="Z", segments=14)
    sign("RoofSign", "FREIGHT - NO REFUNDS", 2.1, 0.42, (0, 2.3, 5.05), R90, P, root, d=0.1, two_sided=True, marquee=34)
    for y in (1.9, 2.9):
        cylinder(f"RoofSignPost{y}", 0.04, 0.6, (0, y, 4.62), P["gold"], root, axis="Z", segments=8)
    dish = empty("Dish", (-0.9, 2.2, 4.55), root)
    cylinder("DishMast", 0.05, 0.3, (0, 0, 0.15), P["gold"], dish, axis="Z", segments=12)
    ears("DishEars", 0.32, 0.1, 0.04, 200, 0.06, P, dish, (0, 0, 0.62))

    # Engine pods under the horns: bulbous nacelles, owner band, rear thrusters, lift rings, nameplates.
    def taper(x, y, z):  # pods narrow toward the stern (x only, so the bands can follow)
        if y < 0:
            x *= 1 - 0.25 * (-y / 2.9) ** 2
        return x, y, z

    def pod_x(y, z):  # half-width of a pod at (y, z) relative to its centre
        q = 1 - abs(y / 2.9) ** 2.6 - abs(z / 1.25) ** 2.6
        return 0 if q <= 0 else taper(0.9 * q ** (1 / 2.6), y, 0)[0]
    for k, s in enumerate((-1, 1)):
        px = s * 3.4
        blob(f"Pod{s}", (0.9, 2.9, 1.25), (px, -1.2, 2.7), P["copperdark"], root, p=2.6, shape=taper)
        blob(f"PodOwner{s}", (0.915, 2.86, 0.06), (px, -1.2, 2.25), P["owner"], root, p=2.6, segs=(40, 6), shape=taper)
        blob(f"PodTrim{s}", (0.92, 2.92, 0.035), (px, -1.2, 2.7), P["gold"], root, p=2.6, segs=(40, 6), shape=taper)
        cylinder(f"Nozzle{s}", 0.52, 0.5, (px, -4.0, 2.7), P["umber"], root, axis="Y", segments=28, bev=0.03)
        cylinder(f"NozzleRing{s}", 0.56, 0.08, (px, -4.2, 2.7), P["gold"], root, axis="Y", segments=28)
        cylinder(f"Thruster{k}", 0.42, 0.06, (px, -4.23, 2.7), P["engine"], root, axis="Y", segments=28)
        for y in (-0.2, -2.4):
            cylinder(f"LiftRing{s}{y}", 0.42, 0.2, (px, y, 1.52), P["umber"], root, axis="Z", segments=24)
            cylinder(f"LiftGlow{s}{y}", 0.34, 0.04, (px, y, 1.41), P["engine"], root, axis="Z", segments=24)
        box(f"NamePlate{s}", (0.12, 2.0, 0.4), (s * 4.25, -1.2, 2.8), P["umber"], root, 0.03)
        x = s * 4.315
        decal("NameR" if s > 0 else "NameL", "SWIFT PROFIT", 0.2, (x, -0.9, 2.86), side_rot(s), 1.4, P, root)
        decal("NumberR" if s > 0 else "NumberL", "118", 0.2, (x, -1.95, 2.86), side_rot(s), 0.5, P, root)
        bulbs(f"PodBulbs{s}", [(px + s * (pod_x(y + 1.2, -0.25) + 0.01), y, 2.45) for y in [-3.0 + 0.4 * j for j in range(10)]],
              0.035, P["bulb"], root)

    # Landing gear: one leg per pod corner.
    for k, (s, y) in enumerate(((-1, 0.6), (1, 0.6), (-1, -3.0), (1, -3.0))):
        g = empty(f"Gear{k}", (s * 4.15, y, 0.0), root)
        strut(f"Gear{k}Main", (-s * 0.7, 0, 1.8), (0, 0, 0.12), 0.09, P["umber"], g)
        strut(f"Gear{k}Brace", (-s * 1.1, 0, 1.8), (-s * 0.1, 0, 0.2), 0.06, P["steel"], g)
        cylinder(f"Gear{k}Pad", 0.35, 0.1, (0, 0, 0.05), P["gold"], g, axis="Z", segments=20)

    # Yoke beam across the open middle and the four cargo clamps holding the vehicle by its chassis tub.
    box("YokeBeam", (5.6, 1.2, 0.4), (0, -0.8, 3.2), P["umber"], root, 0.05)
    box("YokeTrim", (5.62, 0.06, 0.08), (0, -0.2, 3.2), P["gold"], root, 0.0)
    box("YokeTrimB", (5.62, 0.06, 0.08), (0, -1.4, 3.2), P["gold"], root, 0.0)
    sign("YokeSign", "CARGO 2 BARS / DROP", 2.2, 0.28, (0, -0.19, 3.2), PI, P, root, d=0.04, frame=False)
    for k, (s, e) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        cl = empty(f"Clamp{k}", (s * 1.35, -0.8 + e * 0.45, 3.0), root)
        box(f"Clamp{k}Strut", (0.12, 0.16, 2.0), (0, 0, -1.0), P["copperdark"], cl, 0.02)
        box(f"Clamp{k}Ram", (0.07, 0.1, 1.2), (0, 0, -0.75), P["steel"], cl, 0.0).location.x = s * 0.075
        box(f"Clamp{k}Claw", (0.6, 0.16, 0.12), (-s * 0.27, 0, -2.02), P["gold"], cl, 0.02)
        cylinder(f"Clamp{k}Collar", 0.1, 0.1, (0, 0, -0.1), P["gold"], cl, axis="Z", segments=12)
    rover(P, root, (0, -0.8, 0.3)).name = "Cargo"
    return root


# ---------------------------------------------------------------- colony core


def meridian(half, centre, p, x, ts, lift):
    """Points over a superellipsoid blob along the section at local x (front-to-back), pushed out by `lift`."""
    a, b, c = half
    k = max(0.0, 1 - abs(x / a) ** p) ** (1 / p)
    e = 2 / p
    out = []
    for t in ts:
        s_, c_ = math.sin(t), math.cos(t)
        y = k * b * math.copysign(abs(s_) ** e, s_)
        z = k * c * math.copysign(abs(c_) ** e, c_)
        n = Vector((0, y / max(b, 1e-6) ** 2, z / max(c, 1e-6) ** 2)).normalized()
        out.append(Vector((centre[0] + x, centre[1] + y, centre[2] + z)) + n * lift)
    return out


def ribs(name, rx, rz, z0, n, P, parent, t0=0.0, t1=1.32, phase=0.0, r=0.06, p=2.0):
    """Gold ribs up a round dome / tier (superellipsoid of exponent p, half sizes rx, rz, centre height z0)."""
    e = 2 / p
    for k in range(n):
        a = phase + 2 * PI * k / n
        pts = []
        for j in range(13):
            t = t0 + (t1 - t0) * j / 12
            h = rx * abs(math.cos(t)) ** e + r * 0.5
            z = z0 + rz * math.copysign(abs(math.sin(t)) ** e, math.sin(t)) + r * 0.5 * math.sin(t)
            pts.append((h * math.cos(a), h * math.sin(a), z))
        tube(f"{name}{k}", pts, r, P["gold"], parent)


def portal(name, P, parent, y, w, h, z0, sign_text, sign_w, depth=2.0):
    """Front entrance: rounded copper vestibule, gold door, crescent arch and the lit sign on top."""
    box(f"{name}Vestibule", (w, depth, h), (0, y - depth / 2, z0 + h / 2), P["copper"], parent, bev=min(0.35, w * 0.12))
    box(f"{name}Door", (w * 0.45, 0.16, h * 0.66), (0, y - 0.05, z0 + h * 0.33), P["gold"], parent, 0.02)
    box(f"{name}DoorSplit", (0.03, 0.18, h * 0.62), (0, y - 0.04, z0 + h * 0.33), P["umber"], parent, 0)
    a = crescent(f"{name}Arch", w * 0.36, 0.16, 0.06, 220, 0.14, P["gold"], parent, segs=30)
    orient(a, (-1, 0, 0), (0, 0, 1), (0, y + 0.02, z0 + h * 0.6))
    sign(f"{name}Sign", sign_text, sign_w, h * 0.22, (0, y - 0.35, z0 + h + h * 0.13), PI, P, parent, d=0.14, marquee=40)
    return a


def steps(name, P, parent, y, w, top, n=3, depth=0.5):
    for k in range(n):
        hgt = top * (n - k) / n
        box(f"{name}{k}", (w, depth, hgt), (0, y + depth * (k + 0.5), hgt / 2), P["umber"], parent, 0.02)


def cargo_pod(name, P, parent, loc, yaw, label):
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, yaw)
    blob(f"{name}Shell", (0.85, 1.35, 0.8), (0, 0, 0.8), P["copperdark"], g, p=2.4, zmin=-0.95)
    blob(f"{name}Band", (0.87, 1.37, 0.04), (0, 0, 0.8), P["gold"], g, p=2.4, segs=(32, 6))
    tag(f"{name}Tag", label, 0.9, (0.0, -1.28, 0.95), 0, P, g, tilt=math.radians(-20))
    return g


def latinum_stack(name, P, parent, loc, rows=3, cols=3, layers=3):
    items = []
    for l in range(layers):
        for i in range(rows - l):
            for j in range(cols):
                items.append(((0.16, 0.42, 0.09), (loc[0] + (i - (rows - l - 1) / 2) * 0.19, loc[1] + (j - (cols - 1) / 2) * 0.46,
                                                   loc[2] + 0.17 + 0.095 * l), (0, 0, 0)))
    box(f"{name}Pallet", (0.7, 1.5, 0.12), (loc[0], loc[1], loc[2] + 0.06), P["umber"], parent, 0.01)
    return cubes(name, items, P["latinum"], parent)


def hab_stack(name, P, parent, loc, yaw, rent):
    """Rental habitat: two copper pods stacked, each with a door and its rent posted."""
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, yaw)
    for k, z in enumerate((0.0, 1.55)):
        blob(f"{name}Pod{k}", (1.1, 1.0, 0.8), (0, 0, z + 0.8), P["copper"] if k == 0 else P["copperdark"], g, p=2.6,
             zmin=-0.95 if k == 0 else None)
        blob(f"{name}Band{k}", (1.11, 1.01, 0.035), (0, 0, z + 0.8), P["gold"], g, p=2.6, segs=(32, 6))
        box(f"{name}Door{k}", (0.5, 0.1, 0.8), (0, -0.95, z + 0.72), P["gold"], g, 0.02)
        sign(f"{name}Rate{k}", rent, 0.62, 0.17, (0, -0.95, z + 1.2), 0, P, g, d=0.04, frame=False)
    box(f"{name}Step", (0.6, 0.3, 0.14), (0, -1.15, 1.55), P["umber"], g, 0.01)
    return g


def landing_pad(name, P, parent, loc, r):
    g = empty(name, loc, parent)
    cylinder(f"{name}Disc", r, 0.14, (0, 0, 0.07), P["pad"], g, axis="Z", segments=48)
    tube(f"{name}Ring", circle_points(r * 0.86, 0.15, 48), 0.06, P["padline"], g, closed=True)
    mark = crescent(f"{name}Mark", r * 0.45, r * 0.12, r * 0.03, 200, 0.02, P["padline"], g, bev=0)
    mark.location = (0, -r * 0.12, 0.15)  # flat on the pad, horns toward -Y
    bulbs(f"{name}Lights", circle_points(r * 0.97, 0.16, 20), 0.07, P["bulb"], g)
    return g


def ticker_tower(name, P, parent, loc, height, boards, beacon=None):
    g = empty(name, loc, parent)
    cylinder(f"{name}Mast", 0.16, height, (0, 0, height / 2), P["umber"], g, axis="Z", segments=14)
    cylinder(f"{name}Foot", 0.5, 0.3, (0, 0, 0.15), P["gold"], g, axis="Z", segments=16)
    for k, (body, z) in enumerate(boards):
        sign(f"{name}Board{k}", body, 2.0, 0.5, (0, 0, z), PI / 6 + PI / 3 * k, P, g, d=0.38, two_sided=True, marquee=30)
    cylinder(f"{name}BeaconBase", 0.14, 0.1, (0, 0, height + 0.05), P["gold"], g, axis="Z", segments=14)
    cylinder(beacon or f"{name}Lamp", 0.12, 0.22, (0, 0, height + 0.2), P["amber"], g, axis="Z", segments=14)
    return g


def outpost_l1(P):
    """Trade Office, just landed: a copper dome on its landing plinth (legs still out, scorch ring round it), gold
    door under a crescent arch, the lit sign, a blade sign on a mast, two cargo pods for sale and a pallet of latinum."""
    root = empty("FerengiTradeOffice")
    cylinder("Scorch", 7.0, 0.01, (0, 0, 0.005), P["scorch"], root, axis="Z", segments=48)
    pl = cylinder("Plinth", 4.6, 0.8, (0, 0, 0.4), P["umber"], root, axis="Z", segments=8, bev=0.05)
    pl.rotation_euler.z = math.radians(22.5)
    o = cylinder("PlinthOwner", 4.62, 0.12, (0, 0, 0.6), P["owner"], root, axis="Z", segments=8)
    o.rotation_euler.z = math.radians(22.5)
    oct_pts = [(4.66 * math.cos(math.radians(22.5 + 45 * k)), 4.66 * math.sin(math.radians(22.5 + 45 * k)), 0.8) for k in range(8)]
    tube("PlinthTrim", oct_pts, 0.05, P["gold"], root, closed=True)
    bl = []
    for k in range(8):
        a, b = Vector(oct_pts[k]), Vector(oct_pts[(k + 1) % 8])
        bl += [a.lerp(b, j / 6) + Vector((0, 0, 0.08)) for j in range(6)]
    bulbs("PlinthBulbs", [p for p in bl if not (p.y > 3.5 and abs(p.x) < 1.6)], 0.06, P["bulb"], root)
    for k in range(6):
        a = math.radians(30 + 60 * k)
        if abs(math.degrees(a) - 90) < 1:
            continue
        g = empty(f"Leg{k}", (5.6 * math.cos(a), 5.6 * math.sin(a), 0), root)
        strut(f"Leg{k}Strut", (-1.4 * math.cos(a), -1.4 * math.sin(a), 0.65), (0, 0, 0.1), 0.12, P["copperdark"], g)
        cylinder(f"Leg{k}Pad", 0.5, 0.12, (0, 0, 0.06), P["gold"], g, axis="Z", segments=20)
    blob("Dome", (3.4, 3.4, 2.8), (0, 0, 0.8), P["copper"], root, p=2, zmin=0.0, segs=(48, 24))
    blob("DomeBand", (3.32, 3.32, 0.08), (0, 0, 1.55), P["window"], root, p=2, segs=(48, 6))
    ribs("DomeRib", 3.4, 2.8, 0.8, 8, P, root, t1=1.22, phase=PI / 8)
    cylinder("Collar", 1.2, 0.32, (0, 0, 3.45), P["gold"], root, axis="Z", segments=32)
    dish = empty("Dish", (0, 0, 3.6), root)
    cylinder("DishMast", 0.1, 0.5, (0, 0, 0.25), P["umber"], dish, axis="Z", segments=12)
    ears("DishEars", 0.9, 0.26, 0.08, 200, 0.14, P, dish, (0, 0, 1.4))
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.2)
    smooth(mesh_object("DishEye", bm, P["lime"], dish, (0, 0, 0.86)))
    portal("Entrance", P, root, 4.42, 2.6, 2.5, 0.8, "TRADE OFFICE", 3.6)
    steps("Step", P, root, 4.42, 2.4, 0.8, 3, 0.4)
    # Blade sign on a mast at the front-right corner, beacon on top.
    mx, my = -3.4, 2.9
    cylinder("SignMast", 0.12, 7.2, (mx, my, 3.6 + 0.8 - 0.8), P["umber"], root, axis="Z", segments=12)
    blade = sign("BladeSign", "O\nP\nE\nN", 0.8, 3.0, (mx, my, 4.6), PI * 0.75, P, root, d=0.3, two_sided=True,
                 marquee=36, size=0.55)
    cylinder("BeaconBase", 0.14, 0.1, (mx, my, 7.25), P["gold"], root, axis="Z", segments=14)
    cylinder("Beacon", 0.12, 0.24, (mx, my, 7.42), P["amber"], root, axis="Z", segments=14)
    sign("RateBoard", "LATINUM ONLY", 2.2, 0.36, (2.5, 3.4, 1.4), PI - math.radians(30), P, root, d=0.1, marquee=24, legs=0.6)
    cargo_pod("CargoPodA", P, root, (-6.0, 1.6, 0), math.radians(-70), "FOR SALE")
    cargo_pod("CargoPodB", P, root, (-5.4, -2.2, 0), math.radians(-110), "MAKE OFFER")
    latinum_stack("LatinumPallet", P, root, (2.4, 5.6, 0))
    for s in (-1, 1):
        decal("NameR" if s > 0 else "NameL", "PROFIT LANDING", 0.32, (s * 4.255, 0.0, 0.33), side_rot(s), 3.0, P, root)
    return root


def outpost_l5(P):
    """Commerce Authority: stacked domes with flared crescent fins and a price ticker ring, entrance and sign, a
    market of rental habitat stacks, a ticker tower, cargo pods and a landing pad, all inside one hex."""
    root = empty("FerengiCommerceAuthority")
    cylinder("Plinth", 6.2, 0.6, (0, 0, 0.3), P["umber"], root, axis="Z", segments=48, bev=0.05)
    cylinder("PlinthOwner", 6.22, 0.12, (0, 0, 0.42), P["owner"], root, axis="Z", segments=48)
    tube("PlinthTrim", circle_points(6.24, 0.6, 64), 0.06, P["gold"], root, closed=True)
    bulbs("PlinthBulbs", [p for p in circle_points(6.28, 0.68, 60) if not (p[1] > 5.4 and abs(p[0]) < 1.9)], 0.07, P["bulb"], root)
    blob("DomeMain", (5.0, 5.0, 3.6), (0, 0, 0.6), P["copper"], root, p=2, zmin=0.0, segs=(56, 28))
    blob("DomeMainBand", (4.88, 4.88, 0.11), (0, 0, 1.75), P["window"], root, p=2, segs=(56, 6))
    ribs("DomeMainRib", 5.0, 3.6, 0.6, 12, P, root, t1=1.15, phase=PI / 12, r=0.07)
    cylinder("Collar", 2.15, 0.5, (0, 0, 4.0), P["gold"], root, axis="Z", segments=40)
    blob("DomeUpper", (2.9, 2.9, 2.4), (0, 0, 4.2), P["copperdark"], root, p=2, zmin=0.0, segs=(48, 24))
    for k, body in enumerate(("DUR 12 +", "TRI 9 -", "ORE 3 +", "LAT 100", "PAR 41 +", "ISO 77 -")):
        a = 2 * PI * k / 6 + R90
        sign(f"Ticker{k}", body, 2.3, 0.42, (2.95 * math.cos(a), 2.95 * math.sin(a), 4.75), a + R90, P, root, d=0.3, frame=False)
    cylinder("CollarUpper", 1.1, 0.3, (0, 0, 6.45), P["gold"], root, axis="Z", segments=32)
    blob("DomeTop", (1.3, 1.3, 1.1), (0, 0, 6.5), P["gold"], root, p=2, zmin=0.0)
    # Four flared crescent fins round the main dome (like spread ears), lit along their spines.
    for k in range(4):
        phi = PI / 4 + R90 * k
        rh = Vector((math.cos(phi), math.sin(phi), 0))
        f = crescent(f"Fin{k}", 3.0, 0.9, 0.22, 140, 0.26, P["copper"], root, segs=36, bev=0.05)
        orient(f, (0, 0, 1), -rh, rh * 6.4 + Vector((0, 0, 3.0)))
        tube(f"FinEdge{k}", [rh * 6.4 + Vector((0, 0, 3.0)) - rh * 3.03 * math.cos(t) + Vector((0, 0, 3.03 * math.sin(t)))
                            for t in [math.radians(-66 + 11 * j) for j in range(13)]], 0.05, P["lime"], root)
    dish = empty("Dish", (0, 0, 7.55), root)
    cylinder("DishMast", 0.08, 0.5, (0, 0, 0.25), P["umber"], dish, axis="Z", segments=12)
    ears("DishEars", 0.8, 0.22, 0.07, 200, 0.12, P, dish, (0, 0, 1.3))
    portal("Entrance", P, root, 6.4, 3.0, 3.0, 0.6, "COMMERCE AUTHORITY", 5.0)
    steps("Step", P, root, 6.4, 2.8, 0.6, 2, 0.45)
    # Around it: rental habitat stacks (rear-left), cargo pods, ticker tower (front-right), landing pad (rear-right).
    for k, a in enumerate((150, 185, 220, 255)):
        r = 9.0
        ar = math.radians(a)
        hab_stack(f"Hab{k}", P, root, (r * math.cos(ar), r * math.sin(ar), 0), ar - R90, f"RENT {3 + k}")
    ticker_tower("TickerTower", P, root, (-5.6, 7.8, 0), 9.0, [("BUY LOW", 5.0), ("SELL HIGH", 6.4), ("ALL SALES FINAL", 7.8)],
                 beacon="Beacon")
    landing_pad("Pad", P, root, (7.3, -6.3, 0), 3.1)
    cargo_pod("CargoPodA", P, root, (9.6, 0.8, 0), math.radians(10), "FOR SALE")
    cargo_pod("CargoPodB", P, root, (0.5, 10.3, 0), math.radians(-160), "BID NOW")
    latinum_stack("LatinumPallet", P, root, (3.2, 7.6, 0))
    sign("RateBoard", "LICENSES 40 BARS", 2.6, 0.42, (5.2, 6.4, 1.3), PI - math.radians(35), P, root, d=0.12, marquee=26,
         legs=1.3)
    for s in (-1, 1):
        decal("NameR" if s > 0 else "NameL", "PROFIT LANDING", 0.2, (s * 6.235, 0.0, 0.22), side_rot(s), 2.2, P, root)
    return root


def outpost_l10(P):
    """Tower of Commerce, the capital: five stacked copper tiers on a stepped podium under a gold crown of spread
    ears, ticker rings, the grand entrance, a latinum vault, a prize-wheel sign, habitat stacks and two pads."""
    root = empty("FerengiTowerOfCommerce")
    cylinder("Podium0", 13.0, 0.8, (0, 0, 0.4), P["umber"], root, axis="Z", segments=64, bev=0.06)
    cylinder("Podium1", 9.5, 0.8, (0, 0, 1.2), P["copperdark"], root, axis="Z", segments=64, bev=0.06)
    cylinder("PodiumOwner", 13.02, 0.14, (0, 0, 0.55), P["owner"], root, axis="Z", segments=64)
    tube("PodiumTrim0", circle_points(13.04, 0.8, 96), 0.07, P["gold"], root, closed=True)
    tube("PodiumTrim1", circle_points(9.54, 1.6, 80), 0.07, P["gold"], root, closed=True)
    bulbs("PodiumBulbs0", [p for p in circle_points(13.1, 0.88, 110) if not (p[1] > 12 and abs(p[0]) < 2.6)], 0.08, P["bulb"], root)
    bulbs("PodiumBulbs1", [p for p in circle_points(9.6, 1.68, 80) if not (p[1] > 8.6 and abs(p[0]) < 2.6)], 0.08, P["bulb"], root)
    z = 1.6
    tiers = ((7.0, 4.6), (5.8, 4.2), (4.7, 3.8), (3.7, 3.4), (2.7, 3.0))
    for k, (rx, h) in enumerate(tiers):
        mat = P["copper"] if k % 2 == 0 else P["copperdark"]
        blob(f"Tier{k}", (rx, rx, h * 0.62), (0, 0, z + h * 0.5), mat, root, p=2.4, zmin=-0.8, zmax=0.8, segs=(56, 24))
        blob(f"TierWindows{k}", (rx * 1.004, rx * 1.004, h * 0.07), (0, 0, z + h * 0.42), P["window"], root, p=2.4, segs=(56, 6))
        tube(f"TierTicker{k}", circle_points(rx * 0.998, z + h * 0.62, 72), 0.06, P["lime"], root, closed=True)
        neck = tiers[k + 1][0] * 0.62 if k + 1 < len(tiers) else 1.4
        cylinder(f"Neck{k}", neck, 0.5, (0, 0, z + h), P["gold"], root, axis="Z", segments=40)
        ribs(f"TierRib{k}_", rx, h * 0.62, z + h * 0.5, 10 - k, P, root, t0=-0.85, t1=0.85, phase=0.3 * k, r=0.08, p=2.4)
        z += h
    # Tickers on the bottom tier.
    for k, body in enumerate(("DUR 12 +", "TRI 9 -", "LAT 100", "ORE 3 +", "PAR 41 +", "ISO 77 -", "WARP 900", "DIL 260 +")):
        a = 2 * PI * k / 8 + R90 + PI / 8
        sign(f"Ticker{k}", body, 2.6, 0.55, (7.0 * math.cos(a), 7.0 * math.sin(a), 1.6 + 4.6 * 0.5 + 0.9), a + R90, P, root,
             d=0.5, frame=False)
    # The crown: two great gold crescents spreading left and right like ears, a spire with a latinum orb.
    top = z
    # Each ear is a fat crescent lobe bulging outward, its horns meeting the spire above and the top neck below.
    for s in (-1, 1):
        e = crescent(f"Crown{s}", 4.0, 2.0, 0.3, 190, 0.6, P["gold"], root, segs=48, bev=0.08)
        rh = Vector((s, 0, 0))
        O = Vector((s * 0.3, 0, top + 3.95))
        orient(e, (0, 0, s), rh, O)
        bulbs(f"CrownBulbs{s}", [O + Vector((0, -0.33, 0)) + rh * 4.05 * math.cos(t) + Vector((0, 0, 4.05 * math.sin(t)))
                                 for t in [math.radians(-80 + 10 * j) for j in range(17)]], 0.1, P["bulb"], root)
    cylinder("Spire", 0.7, 8.0, (0, 0, top + 4.0), P["gold"], root, axis="Z", segments=24, radius2=0.12)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.1)
    smooth(mesh_object("LatinumOrb", bm, P["latinum"], root, (0, 0, top + 3.95)))
    cylinder("BeaconBase", 0.16, 0.12, (0, 0, top + 8.0), P["gold"], root, axis="Z", segments=14)
    cylinder("Beacon", 0.15, 0.3, (0, 0, top + 8.2), P["amber"], root, axis="Z", segments=14)
    dish = empty("Dish", (0, 0, top + 6.5), root)
    box("DishArm", (0.12, 0.8, 0.12), (0, -0.4, 0), P["gold"], dish, 0.02)
    ears("DishEars", 0.45, 0.13, 0.05, 200, 0.08, P, dish, (0, -0.8, 0.5))
    # Grand entrance on the podium front with a ramp of steps.
    portal("Entrance", P, root, 9.0, 4.0, 3.6, 1.6, "TOWER OF COMMERCE", 7.0, depth=3.0)
    steps("StepA", P, root, 9.5, 4.6, 1.6, 4, 0.9)
    # Latinum vault (clear case of bars) front-left on the lower terrace, prize-wheel sign front-right.
    vault = empty("Vault", (-7.9, 7.9, 0.8), root)
    vault.rotation_euler.z = math.radians(45)
    box("VaultBase", (2.8, 2.0, 0.4), (0, 0, 0.2), P["umber"], vault, 0.04)
    box("VaultGlass", (2.5, 1.7, 1.5), (0, 0, 1.15), P["clear"], vault, 0.03)
    latinum_stack("VaultBars", P, vault, (0, 0, 0.4), rows=4, cols=3, layers=4)
    box("VaultRoof", (2.9, 2.1, 0.16), (0, 0, 1.98), P["gold"], vault, 0.04)
    sign("VaultSign", "LOOK, DON'T TOUCH", 2.4, 0.3, (0, 0.9, 2.3), PI, P, vault, d=0.08)
    wheel = empty("PrizeWheel", (10.3, 2.0, 0.8), root)
    wheel.rotation_euler.z = math.radians(139)
    for s in (-1, 1):
        strut(f"WheelLeg{s}", (s * 1.6, 0, 0), (0, 0, 3.6), 0.12, P["umber"], wheel)
    tube("WheelRim", [(3.0 * math.cos(a), 0, 3.6 + 3.0 * math.sin(a)) for a in [2 * PI * k / 40 for k in range(40)]], 0.12,
         P["gold"], wheel, closed=True)
    for k in range(12):
        a = 2 * PI * k / 12
        box(f"WheelSpoke{k}", (0.1, 0.08, 2.9), (1.45 * math.cos(a), 0, 3.6 + 1.45 * math.sin(a)),
            P["lime"] if k % 2 else P["copper"], wheel, 0).rotation_euler.y = -a + R90
    cylinder("WheelHub", 0.45, 0.3, (0, 0, 3.6), P["gold"], wheel, axis="Y", segments=24)
    bulbs("WheelBulbs", [(3.2 * math.cos(a), -0.05, 3.6 + 3.2 * math.sin(a)) for a in [2 * PI * k / 30 for k in range(30)]], 0.09,
          P["bulb"], wheel)
    for k, a in enumerate((165, 190, 215, 325, 350)):
        ar = math.radians(a)
        hab_stack(f"Hab{k}", P, root, (11.2 * math.cos(ar), 11.2 * math.sin(ar), 0.8), ar - R90, f"RENT {4 + k}")
    landing_pad("PadA", P, root, (-7.5, -16.0, 0), 3.4)
    landing_pad("PadB", P, root, (7.5, -16.0, 0), 3.4)
    ticker_tower("TickerTower", P, root, (14.6, 2.0, 0), 11.0, [("BUY LOW", 6.0), ("SELL HIGH", 7.6), ("RULE 9", 9.2)])
    ticker_tower("TickerTowerB", P, root, (-14.6, 2.0, 0), 11.0, [("NO REFUNDS", 6.0), ("ALL SALES FINAL", 7.6), ("RULE 1", 9.2)])
    for s in (-1, 1):
        decal("NameR" if s > 0 else "NameL", "PROFIT LANDING", 0.34, (s * 13.04, 0.0, 0.28), side_rot(s), 3.0, P, root)
    return root


# ---------------------------------------------------------------- preview


# model -> (camera scale, ground colour) ; camera scale 1 = build_vehicles' rover cameras.
CAM_SCALE = {"rover": 1.0, "miner": 1.0, "fedscout": 1.0, "dropship": 2.0, "outpost1": 2.4, "outpost5": 3.4, "outpost10": 6.0}
CAM_LIFT = {"outpost10": 5.0}  # extra target height for the tall capital
BASE_SHOTS = {
    "front34": (Vector((7.5, 8.5, 3.2)), 40),
    "rear34": (Vector((-7.5, -8.0, 3.6)), 40),
    "side": (Vector((10.5, 0.4, 1.9)), 38),
    "game": (Vector((0.0, 13.0, 15.5)), 30),
}


def model_key():
    return f"outpost{LEVEL}" if MODEL == "outpost" else MODEL


def stem():
    if MODEL == "fedscout":
        return "fed-mk1-scout"
    if MODEL == "outpost":
        return f"ferengi-outpost-l{LEVEL}"
    if MODEL == "miner":
        return f"ferengi-miner-{POSE}"
    return f"ferengi-{MODEL}"


def hex_guide(P, z=0.02):
    """Preview only: the outline of one 25-unit hex (flat-to-flat), corners on +-Y like the game's HexCorner."""
    R = 25 / math.sqrt(3)
    pts = [(R * math.sin(math.radians(60 * k)), R * math.cos(math.radians(60 * k)), z) for k in range(6)]
    m = material("PreviewHex", (0.85, 0.85, 0.75), 0.0, 0.6, emission=(0.8, 0.9, 0.6), strength=0.4)
    tube("PreviewHex", pts, 0.08, m, None, closed=True, segs=6)


def measure(root):
    dg = bpy.context.evaluated_depsgraph_get()
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    for o in [root] + list(root.children_recursive):
        if o.type != "MESH" or o.hide_render or o.name.startswith("Preview") or o.name == "Scorch":
            continue
        ev = o.evaluated_get(dg)
        for c in ev.bound_box:
            w = ev.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def preview(root, folder):
    k = CAM_SCALE[model_key()]
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = math.radians(28)
    sky.sun_rotation = math.radians(150)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.12
    scene.view_settings.exposure = -0.6
    sun = bpy.data.objects.new("PreviewSun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(55), 0, math.radians(150))
    link(sun)
    ground = bpy.data.meshes.new("PreviewGround")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40 * k)
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("PreviewGround", ground))
    cam = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("Camera"))
    link(cam)
    cam.data.clip_end = 1000
    scene.camera = cam
    base_target = Vector((0, 0, 1.3))
    target = base_target * k + Vector((0, 0, CAM_LIFT.get(model_key(), 0.0)))
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        pos, lens = BASE_SHOTS[name]
        cam.location = target + (pos - base_target) * k
        cam.data.lens = lens
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{stem()}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath, flush=True)


def build(model, level=1, pose="stowed"):
    reset()
    if model == "fedscout":
        return NS["rover_mk1"](NS["palette"]())
    P = palette()
    if model == "rover":
        return rover(P)
    if model == "miner":
        return miner(P, deployed=pose == "deployed")
    if model == "dropship":
        return dropship(P)
    return {1: outpost_l1, 5: outpost_l5, 10: outpost_l10}[level](P)


# ---------------------------------------------------------------- sheet

SHEET = [
    ("fed-mk1-scout", "fedscout", 1, "stowed", "Federation Mk I Scout (comparison)"),
    ("ferengi-rover", "rover", 1, "stowed", "Prospector survey rover"),
    ("ferengi-miner-stowed", "miner", 1, "stowed", "Concession digger, stowed"),
    ("ferengi-miner-deployed", "miner", 1, "deployed", "Concession digger, drilling"),
    ("ferengi-dropship", "dropship", 1, "stowed", "Freight yoke drop ship + rover"),
    ("ferengi-outpost-l1", "outpost", 1, "stowed", "L1 Trade Office (just landed)"),
    ("ferengi-outpost-l5", "outpost", 5, "stowed", "L5 Commerce Authority"),
    ("ferengi-outpost-l10", "outpost", 10, "stowed", "L10 Tower of Commerce (capital)"),
    ("ferengi-rover", "rover", 1, "stowed", "Prospector, game camera"),
]


def make_sheet(folder):
    """Measure every sheet model here (bpy), then draw the sheet with the system python3 and PIL (the bpy venv has no
    PIL): this same file run with --compose <cells.json>."""
    import subprocess
    import tempfile
    dims = {}
    for st, model, level, pose, _ in SHEET:
        if st in dims:
            continue
        root = build(model, level, pose)
        bpy.context.view_layer.update()
        lo, hi = measure(root)
        dims[st] = hi - lo
    cells = []
    for i, (st, model, level, pose, label) in enumerate(SHEET):
        key = f"outpost{level}" if model == "outpost" else model
        v = dims[st]
        shot = "game" if i == len(SHEET) - 1 else "front34"
        cells.append({"path": os.path.join(folder, f"{st}-{shot}.png"), "label": label, "fed": model == "fedscout",
                      "caption": f"{v.y:.1f} long x {v.x:.1f} wide x {v.z:.1f} tall  |  camera x{CAM_SCALE[key]:g}"})
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"out": os.path.join(folder, "ferengi-sheet.png"), "cells": cells}, f)
    subprocess.run(["python3", os.path.abspath(__file__), "--compose", f.name], check=True)
    os.unlink(f.name)


def main():
    folder = PREVIEW or DEFAULT_PREVIEW
    if "--sheet" in argv:
        make_sheet(folder)
        return
    root = build(MODEL, LEVEL, POSE)
    bpy.context.view_layer.update()
    lo, hi = measure(root)
    size = hi - lo
    print(f"{stem()}: {size.y:.2f} long x {size.x:.2f} wide x {size.z:.2f} tall; x {lo.x:.2f}..{hi.x:.2f} "
          f"y {lo.y:.2f}..{hi.y:.2f} z {lo.z:.2f}..{hi.z:.2f}", flush=True)
    if OUT:
        NS["export"](root, OUT)
    if PREVIEW:
        if MODEL == "outpost" and LEVEL in (1, 5):
            hex_guide(None)
        preview(root, PREVIEW)


main()
