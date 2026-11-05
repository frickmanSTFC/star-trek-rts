"""Cardassian ground kit mockups: survey rover, mining crawler, drop ship and the colony core (Garrison Post) at
levels 1, 5 and 10. Original designs in the Cardassian Union's general style (tan / ochre and brown-grey hulls,
layered ribbed segments, forward-swept fan shapes, amber lights, heavy and industrial); see cardassian.md.

Run with the PyPI bpy module (plain materials, no paint bake):
  /root/bpyenv/bin/python blender/factions/build_cardassian.py --model rover --preview blender/previews/factions/cardassian
  ... --model miner [--pose deploy|travel]
  ... --model dropship [--cargo miner|rover|none]
  ... --model outpost --level 1|5|10
  ... --model fed          the Federation Mk I Scout (build_vehicles tier 1) under the same lights and cameras
  --res 480 --samples 8    preview size / Cycles samples (finals: --res 800 --samples 24)
  --shots front34,game     which previews to render (also rear34, side, top)
  --blank                  no name/number text, only the decal empties (game export)
  --out file.glb           export the model (preview-only parts such as the hex guide are left out)
  python3 blender/factions/build_cardassian.py --sheet blender/previews/factions/cardassian
                           compose cardassian-sheet.png from the renders (needs PIL, not bpy)

Conventions (build_vehicles.py): Blender +Y = front, +Z up, units = Godot world units (one hex = 25 flat to flat).
Named parts: "Dish" (sensor head, spins about its local Z), "Beacon" (amber, flashes), "TailLamp" material, "Owner"
material (player colour), decal empties NameL / NameR / NameRear / NumberL / NumberR (L = -X side; local XY plane =
text plane, reading along +X, Blender scale (max_width, size, 1)). Miner: Leg0-3 (+ Leg{k}Ext telescopes), Boom,
Drill, String, Bit, Fill, Dust as in the game's crawler. Drop ship: Leg0-3, CargoClamp, Clamp0-3, Engine{L,R}.
Outposts: Door, Dish, Beacon, Screen.
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


# ---------------------------------------------------------------- contact sheet (PIL only, no bpy)

SHEET = [
    ("federation-mk1-front34.png", "Federation Mk I Scout (comparison)", "rover camera x1.0"),
    ("cardassian-rover-front34.png", "Survey rover TARVEK 31", "camera x1.0"),
    ("federation-mk1-game.png", "Federation Mk I Scout, game camera", "x1.0"),
    ("cardassian-rover-game.png", "Survey rover, game camera", "x1.0"),
    ("cardassian-miner-front34.png", "Mining crawler DRAVAN 09, deployed", "camera x1.15"),
    ("cardassian-miner-travel-front34.png", "Mining crawler, travel pose", "camera x1.15"),
    ("cardassian-miner-game.png", "Mining crawler, game camera", "x1.15"),
    ("cardassian-dropship-front34.png", "Drop ship VEKTAL 4 with the crawler clamped", "camera x2.1"),
    ("cardassian-dropship-game.png", "Drop ship, game camera", "x2.1"),
    ("cardassian-outpost-l1-front34.png", "Garrison Post L1 (just landed)", "camera x3.4, one hex"),
    ("cardassian-outpost-l1-game.png", "Garrison Post L1, game camera", "x1.9"),
    ("cardassian-outpost-l5-front34.png", "Prefecture L5", "camera x3.6, one hex"),
    ("cardassian-outpost-l5-game.png", "Prefecture L5, game camera", "x2.0"),
    ("cardassian-outpost-l10-front34.png", "Central Command L10 (capital)", "camera x11, seven hexes"),
    ("cardassian-outpost-l10-game.png", "Central Command L10, game camera", "x5.3"),
]


def make_sheet(folder):
    from PIL import Image, ImageDraw, ImageFont
    cw, ch, lab, cols, pad = 480, 320, 44, 4, 10
    rows = (len(SHEET) + 1 + cols - 1) // cols
    head = 74
    W = cols * cw + (cols + 1) * pad
    H = head + rows * (ch + lab) + (rows + 1) * pad
    sheet = Image.new("RGB", (W, H), (17, 15, 11))
    d = ImageDraw.Draw(sheet)
    bold = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    reg = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
    f_title, f_lab, f_small = ImageFont.truetype(bold, 30), ImageFont.truetype(bold, 16), ImageFont.truetype(reg, 13)
    d.text((pad + 4, 14), "CARDASSIAN GROUND KIT  -  Blender mockups", font=f_title, fill=(255, 180, 58))
    d.text((pad + 6, 50), "Rover, mining crawler, drop ship, Garrison Post L1 / L5 / L10. Camera xN = distance relative to "
           "the rover shots (same lens and angle).", font=f_small, fill=(168, 158, 140))
    for i, (fname, title, note) in enumerate(SHEET + [(None, "Palette and parts", "")]):
        r, c = divmod(i, cols)
        x = pad + c * (cw + pad)
        y = head + pad + r * (ch + lab + pad)
        if fname is None:
            d.rectangle((x, y, x + cw, y + ch + lab), fill=(42, 34, 22))
            sw = [("#b0905e", "tan hull"), ("#8a6a44", "ochre"), ("#5e4a32", "brown"), ("#8a8070", "brown-grey"),
                  ("#ffb43a", "amber light"), ("#8c1712", "Owner (player)")]
            for k, (hexc, nm) in enumerate(sw):
                yy = y + 18 + k * 34
                d.rectangle((x + 18, yy, x + 58, yy + 24), fill=hexc)
                d.text((x + 70, yy + 3), nm, font=f_lab, fill=(239, 232, 220))
            notes = ["Dish = spinning fan sensor head", "Beacon = amber flasher, TailLamp", "NameL/R, NumberL/R = decal empties",
                     "Miner: Leg0-3, Boom, Drill, String, Bit, Fill, Dust", "Drop ship: CargoClamp, Clamp0-3, Leg0-3"]
            for k, t in enumerate(notes):
                d.text((x + 18, y + 228 + k * 25), t, font=f_small, fill=(207, 198, 182))
            continue
        path = os.path.join(folder, fname)
        if os.path.exists(path):
            im = Image.open(path).convert("RGB")
            im = im.resize((cw, int(im.height * cw / im.width)), Image.LANCZOS)
            if im.height > ch:
                top = (im.height - ch) // 2
                im = im.crop((0, top, cw, top + ch))
            sheet.paste(im, (x, y))
        else:
            d.rectangle((x, y, x + cw, y + ch), fill=(60, 20, 20))
            d.text((x + 10, y + 10), "missing " + fname, font=f_small, fill=(255, 200, 200))
        fed = fname.startswith("federation")
        d.rectangle((x, y + ch, x + cw, y + ch + lab), fill=(30, 40, 60) if fed else (58, 44, 18))
        d.text((x + 10, y + ch + 5), title, font=f_lab, fill=(220, 230, 255) if fed else (255, 196, 106))
        d.text((x + 10, y + ch + 25), note, font=f_small, fill=(180, 190, 210) if fed else (207, 198, 182))
    out = os.path.join(folder, "cardassian-sheet.png")
    sheet.save(out)
    print("wrote", out)


if "--sheet" in argv:
    make_sheet(arg("--sheet"))
    sys.exit(0)

import bpy  # noqa: E402  (first: puts bmesh and mathutils on the path)
import bmesh  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

MODEL = arg("--model", "rover")
LEVEL = int(arg("--level", "1"))
POSE = arg("--pose", "deploy")
CARGO = arg("--cargo", "miner")
PREVIEW = arg("--preview")
OUT = arg("--out")
RES = int(arg("--res", "480"))
SAMPLES = int(arg("--samples", "8"))
SHOTS = [s.strip() for s in arg("--shots", "front34,game").split(",") if s.strip()]
BLANK = "--blank" in argv
if MODEL not in ("rover", "miner", "dropship", "outpost", "fed"):
    sys.exit(f"--model takes rover, miner, dropship, outpost or fed, not {MODEL}")
if MODEL == "outpost" and LEVEL not in (1, 5, 10):
    sys.exit(f"--level takes 1, 5 or 10, not {LEVEL}")


def load_build_vehicles(extra=()):
    """build_vehicles.py's helpers without running its main(): exec the source with the trailing main() stripped
    (as render_finish.py does). Its own flags see only `extra`."""
    path = os.path.join(BLENDER_DIR, "build_vehicles.py")
    with open(path) as f:
        src = re.sub(r"^main\(\)\s*$", "", f.read(), flags=re.M)
    saved = sys.argv
    sys.argv = [path, *extra]
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


BV = load_build_vehicles(("--tier", "1", "--name", "TALUS", "--number", "17") if MODEL == "fed" else ())
material, link, empty, mesh_object, bevel = BV["material"], BV["link"], BV["empty"], BV["mesh_object"], BV["bevel"]
box, cylinder, prism, ribbon, strut, ring, text_mesh, on_slope = (BV[k] for k in (
    "box", "cylinder", "prism", "ribbon", "strut", "ring", "text_mesh", "on_slope"))

# ---------------------------------------------------------------- geometry helpers


def lin(h):
    """sRGB hex -> linear rgb (Blender colour sockets are linear)."""
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def palette():
    return {
        "hull": material("CardHull", lin("#b0905e"), 0.25, 0.5),
        "ochre": material("CardOchre", lin("#8a6a44"), 0.25, 0.55),
        "brown": material("CardBrown", lin("#5e4a32"), 0.3, 0.6),
        "grey": material("CardGrey", lin("#8a8070"), 0.35, 0.5),
        "light": material("CardLight", lin("#c8a874"), 0.2, 0.5),
        "dark": material("CardChassis", lin("#2e2a24"), 0.4, 0.6),
        "steel": material("CardSteel", lin("#6e6658"), 0.8, 0.38),
        "tyre": material("CardTyre", lin("#161412"), 0.0, 0.9),
        "glass": material("CardGlass", lin("#1a1208"), 0.3, 0.06, emission=lin("#ff9a2a"), strength=0.35, coat=1.0),
        "amber": material("Amber", lin("#ffb43a"), 0.0, 0.3, emission=lin("#ffa020"), strength=6.0),
        "lamp": material("Lamp", lin("#ffd9a0"), 0.0, 0.2, emission=lin("#ffcf8a"), strength=8.0),
        "redlamp": material("TailLamp", lin("#ff4a14"), 0.0, 0.3, emission=lin("#ff3a0a"), strength=4.0),
        "owner": material("Owner", lin("#8c1712"), 0.1, 0.45),
        "number": material("Number", lin("#21180e"), 0.0, 0.5),
        "glow": material("EngineGlow", lin("#ff8a3a"), 0.0, 0.3, emission=lin("#ff7a20"), strength=12.0),
        "ore": material("Ore", lin("#55504a"), 0.05, 0.92),
        "glint": material("OreGlint", lin("#a08048"), 0.7, 0.3),
        "paving": material("CardPaving", lin("#7c705e"), 0.0, 0.85),
        "paving2": material("CardPavingDark", lin("#5a5044"), 0.0, 0.85),
        "screen": material("BroadcastScreen", lin("#ffb43a"), 0.0, 0.4, emission=lin("#ff9a20"), strength=2.5),
        "scorch": material("Scorch", lin("#211b15"), 0.0, 0.97),
        "guide": material("HexGuide", lin("#e8c36a"), 0.0, 0.5, emission=lin("#e8c36a"), strength=0.8),
    }


def loft(name, rings, mat, parent=None, loc=(0, 0, 0), cap=True, bev=0.0, smooth=False):
    """Skin consecutive rings of points (same count; a one-point ring is an apex) into a closed solid."""
    bm = bmesh.new()
    vr = [[bm.verts.new(Vector(p)) for p in r] for r in rings]
    for a, b in zip(vr, vr[1:]):
        if len(b) == 1 or len(a) == 1:
            ring_, apex = (a, b[0]) if len(b) == 1 else (b, a[0])
            for i in range(len(ring_)):
                bm.faces.new((ring_[i], ring_[(i + 1) % len(ring_)], apex))
            continue
        n = len(a)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap:
        for r in (vr[0], vr[-1]):
            if len(r) > 2:
                bm.faces.new(r)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_object(name, bm, mat, parent, loc, smooth)
    if bev > 0:
        bevel(o, bev, 2, 30)
    return o


def slab(name, pts, z0, z1, mat, parent=None, s=1.0, off=(0.0, 0.0), bev=0.03):
    """Plan polygon [(x, y)] extruded from z0 to z1; the top is scaled by s about the centroid and shifted by off."""
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    bottom = [(x, y, z0) for x, y in pts]
    top = [(cx + (x - cx) * s + off[0], cy + (y - cy) * s + off[1], z1) for x, y in pts]
    return loft(name, [bottom, top], mat, parent, bev=bev)


def rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def ngon(r, n, cx=0.0, cy=0.0, phase=0.0, sx=1.0, sy=1.0):
    return [(cx + sx * r * math.cos(phase + 2 * math.pi * k / n), cy + sy * r * math.sin(phase + 2 * math.pi * k / n)) for k in range(n)]


def hex_pts(r, cx=0.0, cy=0.0):
    """Pointy-top hex as in PlanetView (corners toward +-Y in Blender), circumradius r."""
    return ngon(r, 6, cx, cy, math.pi / 2)


def sect(y, hw, zb, zs, zt, thw):
    """Hull cross-section ring in XZ at y: flat bottom, vertical sides up to zs, tumblehome to a roof of half-width thw."""
    zs = min(zs, zt - 0.02)
    return [(-hw, y, zb), (hw, y, zb), (hw, y, zs), (thw, y, zt), (-thw, y, zt), (-hw, y, zs)]


def oct_sect(y, w, zb, zt):
    h = zt - zb
    pts = [(-0.34, 0), (0.34, 0), (0.5, 0.32), (0.5, 0.6), (0.3, 1.0), (-0.3, 1.0), (-0.5, 0.6), (-0.5, 0.32)]
    return [(px * w, y, zb + pz * h) for px, pz in pts]


def arch(w, h, n=14, e=0.7):
    """Half superellipse from (+w/2, 0) over the top to (-w/2, 0): [(x, z, nx, nz)] with outward normals."""
    q = 2 / e
    out = []
    for i in range(n + 1):
        t = math.pi * i / n
        c, s = math.cos(t), math.sin(t)
        x = w / 2 * math.copysign(abs(c) ** e, c)
        z = h * abs(s) ** e
        nx = math.copysign(abs(x / (w / 2)) ** (q - 1), x) / (w / 2)
        nz = abs(z / h) ** (q - 1) / h
        ln = math.hypot(nx, nz) or 1
        out.append((x, z, nx / ln, nz / ln))
    return out


def arch_ring(y, w, h, zb=0.0, n=14, e=0.7):
    return [(x, y, zb + z) for x, z, _, _ in arch(w, h, n, e)]


def ell_ring(w, d, z, n=24, e=0.5, cx=0.0, cy=0.0):
    out = []
    for k in range(n):
        t = 2 * math.pi * k / n
        c, s = math.cos(t), math.sin(t)
        out.append((cx + w / 2 * math.copysign(abs(c) ** e, c), cy + d / 2 * math.copysign(abs(s) ** e, s), z))
    return out


def band(name, pts, normals, sides, depth, thick, mat, parent=None, sink=0.03):
    """A rib of rectangular section (thick along `sides`, depth along `normals`) swept along a 3D polyline."""
    rings = []
    for p, n, s in zip(pts, normals, sides):
        p, n, s = Vector(p), Vector(n).normalized(), Vector(s).normalized()
        p = p - n * sink
        h = s * (thick / 2)
        rings.append([p - h, p + h, p + h + n * (depth + sink), p - h + n * (depth + sink)])
    return loft(name, rings, mat, parent)


def arch_rib(name, y, w, h, zb, depth, thick, mat, parent=None, n=14, e=0.7):
    a = arch(w, h, n, e)
    return band(name, [(x, y, zb + z) for x, z, _, _ in a], [(nx, 0, nz) for _, _, nx, nz in a],
                [(0, 1, 0)] * len(a), depth, thick, mat, parent)


def tube(name, pts, r0, r1, mat, parent=None, seg=8):
    pts = [Vector(p) for p in pts]
    n = len(pts)
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        ref = Vector((0, 1, 0)) if abs(t.y) < 0.9 else Vector((1, 0, 0))
        u = t.cross(ref).normalized()
        v = t.cross(u).normalized()
        r = r0 + (r1 - r0) * i / (n - 1)
        rings.append([p + (u * math.cos(2 * math.pi * k / seg) + v * math.sin(2 * math.pi * k / seg)) * r for k in range(seg)])
    return loft(name, rings, mat, parent, smooth=True)


def ball(name, r, loc, mat, parent=None, squash=1.0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=r)
    for v in bm.verts:
        v.co.z *= squash
    return mesh_object(name, bm, mat, parent, loc, smooth=True)


def fin(name, base, d, reach, z_top, z_foot, thick, mat, parent=None, inset=0.4, bev=0.04):
    """Buttress fin in the vertical plane through base (x, y) along plan direction d: from the wall (base) out to the
    foot `reach` away; top edge slopes from z_top at the wall to z_foot at the foot."""
    d = Vector((d[0], d[1], 0)).normalized()
    s = Vector((-d.y, d.x, 0)) * (thick / 2)
    b = Vector((base[0], base[1], 0)) - d * inset
    f = Vector((base[0], base[1], 0)) + d * reach
    pts = [b, b + Vector((0, 0, z_top)), f + Vector((0, 0, z_foot)), f]
    return loft(name, [[p - s for p in pts], [p + s for p in pts]], mat, parent, bev=bev)


def forked(name, base, mast, prong, spread, P, parent=None, r=0.08, yaw=0.0, mat=None):
    """Forked spire / antenna: a mast, then two prongs curving out and up (quadratic), amber lamps on the tips."""
    mat = mat or P["ochre"]
    base = Vector(base)
    F = base + Vector((0, 0, mast))
    tube(f"{name}Mast", [base, base + Vector((0, 0, mast * 0.5)), F], r * 1.4, r, mat, parent)
    cyl = cylinder(f"{name}Collar", r * 2.0, r * 2.5, F - Vector((0, 0, r)), P["dark"], parent, axis="Z", segments=12)
    del cyl
    c, sn = math.cos(yaw), math.sin(yaw)
    for s in (-1, 1):
        def off(dx, dz):
            return Vector((dx * c, dx * sn, dz))
        p0, p1, p2 = F, F + off(s * 0.7 * spread, 0.45 * prong), F + off(s * spread, prong)
        pts = [(1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t * t * p2 for t in [k / 7 for k in range(8)]]
        tube(f"{name}Prong{s}", pts, r, r * 0.55, mat, parent)
        ball(f"{name}Tip{s}", r * 1.3, p2, P["amber"], parent)


def crescent(w, depth, horn, n=10):
    """Forward-swept fan / crescent plan outline: convex side toward -Y, horns pointing forward (+Y).
    w = tip-to-tip width, depth = front-to-back thickness at the centre, horn = how far the tips reach forward."""
    out, inner = [], []
    for i in range(n + 1):
        t = -1 + 2 * i / n
        x = t * w / 2
        y_back = -depth * 0.5 * (1 - abs(t) ** 2) + horn * abs(t) ** 2.2 - depth * 0.15
        y_front = depth * 0.5 * (1 - abs(t) ** 2) * 0.3 + horn * abs(t) ** 1.6 + depth * 0.2
        out.append((x, y_back))
        inner.append((x * 0.98, y_front))
    return out + list(reversed(inner))


def fan_head(dish, scale, P, mast=0.3):
    """Cardassian sensor head under the empty `dish` (the game spins it): mast, hub, a tilted forward-swept fan vane
    with an amber emitter strip."""
    s = scale
    cylinder("DishMast", 0.05 * s, mast * s, (0, 0, mast * s / 2), P["steel"], dish, axis="Z", segments=10)
    cylinder("DishHub", 0.12 * s, 0.1 * s, (0, 0, mast * s + 0.03 * s), P["dark"], dish, axis="Z", segments=16, bev=0.01 * s)
    vane = slab("DishVane", [(x * s, y * s) for x, y in crescent(1.1, 0.32, 0.3)], 0, 0.05 * s, P["light"], dish, bev=0.012 * s)
    vane.location = (0, -0.05 * s, mast * s + 0.08 * s)
    vane.rotation_euler = (math.radians(16), 0, 0)
    strip = slab("DishEmitter", [(x * s, y * s) for x, y in crescent(0.9, 0.06, 0.25)], 0, 0.02 * s, P["amber"], dish, bev=0)
    strip.location = (0, 0.02 * s, mast * s + 0.12 * s)
    strip.rotation_euler = (math.radians(16), 0, 0)
    ball("DishEye", 0.07 * s, (0, 0, mast * s + 0.12 * s), P["amber"], dish)


def decal(marker, body, size, loc, rot, mat, parent, max_width):
    """Decal spot: an empty named `marker` (Blender scale (max_width, size, 1), text plane = local XY) plus, unless
    --blank, the text itself as geometry fitted to max_width."""
    m = Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4()
    e = bpy.data.objects.new(marker, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.5
    link(e, parent)
    e.matrix_basis = m @ Matrix.Diagonal((max_width, size, 1.0, 1.0))
    if BLANK:
        return e
    me = text_mesh(f"{marker}Text", body, size)
    xs = [v.co.x for v in me.vertices]
    if xs and max(xs) - min(xs) > max_width:
        f = max_width / (max(xs) - min(xs))
        me.transform(Matrix.Diagonal((f, f, 1.0, 1.0)))
    me.materials.clear()
    me.materials.append(mat)
    o = bpy.data.objects.new(f"{marker}Text", me)
    o.matrix_basis = m
    o.visible_shadow = False
    return link(o, parent)


def side_rot(s):
    """Rotation for text on the +X (s=1) or -X (s=-1) side, reading front-to-back as seen from that side."""
    return (math.radians(90), 0, math.radians(90 if s > 0 else -90))


def amber_lights(prefix, pts, r, P, parent):
    for k, p in enumerate(pts):
        ball(f"{prefix}{k}", r, p, P["amber"], parent, squash=0.6)


# ---------------------------------------------------------------- wheels and tracks


def card_wheel(name, x, y, r, w, P, parent):
    """Heavy wheel: block tread, dark bronze rim with radial ribs, hexagonal hub boss."""
    wl = empty(name, (x, y, r), parent)
    side = 1 if x > 0 else -1
    cylinder(f"{name}Tyre", r, w, (0, 0, 0), P["tyre"], wl, bev=0.05, segments=28)
    for k in range(22):
        a = 2 * math.pi * k / 22
        box(f"{name}Tread{k}", (w * 0.94, 0.1, 0.05), (0, math.cos(a) * r, math.sin(a) * r), P["tyre"], wl, bev=0.01,
            rot=(a + math.pi / 2, 0, 0))
    cylinder(f"{name}Rim", r * 0.62, w * 1.02, (0, 0, 0), P["brown"], wl, bev=0.02)
    for k in range(5):
        a = 2 * math.pi * k / 5
        box(f"{name}Rib{k}", (0.04, 0.05, r * 0.42), (side * w * 0.52, math.cos(a) * r * 0.36, math.sin(a) * r * 0.36),
            P["ochre"], wl, 0.008, rot=(a - math.pi / 2, 0, 0))
    cylinder(f"{name}Hub", r * 0.24, 0.1, (side * w * 0.53, 0, 0), P["steel"], wl, segments=6, bev=0.01)
    return wl


def track(name, xc, P, parent, L=4.6, R=0.5, w=0.62):
    """Stadium track unit at x = xc: belt, cleats, road wheels and end sprockets on the outer face."""
    g = empty(name, (xc, 0, 0), parent)
    side = 1 if xc > 0 else -1
    yF, yR = L / 2 - R, -(L / 2 - R)
    prof = []
    for i in range(9):
        a = -math.pi / 2 + math.pi * i / 8
        prof.append((yF + R * math.cos(a), R + R * math.sin(a)))
    for i in range(9):
        a = math.pi / 2 + math.pi * i / 8
        prof.append((yR + R * math.cos(a), R + R * math.sin(a)))
    prism(f"{name}Belt", prof, w, P["tyre"], g, bev=0.03)
    # cleats every ~0.24 along the perimeter
    per = 2 * (yF - yR) + 2 * math.pi * R
    n = int(per / 0.24)
    for k in range(n):
        dist = per * k / n
        if dist < yF - yR:  # bottom run, rear to front
            y, z, ang = yR + dist, 0.0, 0.0
        elif dist < yF - yR + math.pi * R:
            a = -math.pi / 2 + (dist - (yF - yR)) / R
            y, z, ang = yF + R * math.cos(a), R + R * math.sin(a), a + math.pi / 2
        elif dist < 2 * (yF - yR) + math.pi * R:
            y, z, ang = yF - (dist - (yF - yR) - math.pi * R), 2 * R, math.pi
        else:
            a = math.pi / 2 + (dist - 2 * (yF - yR) - math.pi * R) / R
            y, z, ang = yR + R * math.cos(a), R + R * math.sin(a), a + math.pi / 2
        box(f"{name}Cleat{k}", (w * 1.04, 0.1, 0.06), (0, y, z), P["dark"], g, 0.008, rot=(ang, 0, 0))
    xo = side * (w / 2 + 0.03)
    for k in range(5):
        y = yR + (yF - yR) * k / 4
        cylinder(f"{name}Road{k}", 0.3, 0.06, (xo, y, 0.36), P["brown"], g, segments=20, bev=0.01)
        cylinder(f"{name}RoadHub{k}", 0.1, 0.1, (xo + side * 0.02, y, 0.36), P["steel"], g, segments=6)
    for y in (yF, yR):
        cylinder(f"{name}Sprocket{y:.1f}", R * 0.82, 0.08, (xo, y, R), P["ochre"], g, segments=12, bev=0.01)
        cylinder(f"{name}SprocketHub{y:.1f}", 0.16, 0.12, (xo + side * 0.03, y, R), P["steel"], g, segments=6)
    return g


# ---------------------------------------------------------------- survey rover


def rover(P, name="TARVEK", number="31"):
    """Survey rover: six wheels under armoured, ribbed side skirts; a crescent ram prow with its horns swept forward;
    a narrow slit visor; four overlapping carapace plates stepping down the roof; a fan-vane sensor head ("Dish"),
    forked antenna, amber beacon."""
    root = empty("SurveyRover")
    r, ww = 0.48, 0.32
    axles = (1.5, 0.05, -1.4)
    # chassis tub between the wheels
    prism("Tub", [(-2.15, 0.42), (1.9, 0.42), (2.1, 0.7), (2.1, 1.12), (-2.25, 1.12), (-2.25, 0.6)], 1.56, P["dark"], root, bev=0.04)
    # upper hull overhanging the wheels, tumblehome sides, windscreen slope at the front
    loft("Hull", [sect(-2.32, 1.1, 1.08, 1.35, 1.62, 0.85), sect(-1.95, 1.15, 1.05, 1.38, 1.86, 0.92),
                  sect(1.35, 1.15, 1.05, 1.38, 1.86, 0.92), sect(2.2, 1.15, 1.05, 1.32, 1.45, 1.0),
                  sect(2.32, 1.12, 1.08, 1.2, 1.3, 1.0)], P["hull"], root, bev=0.04)
    # crescent ram prow: horns swept forward at the corners
    horn = [(-1.22, 1.9), (1.22, 1.9), (1.26, 2.25), (1.18, 2.62), (0.98, 2.6), (0.55, 2.42), (0.0, 2.36), (-0.55, 2.42),
            (-0.98, 2.6), (-1.18, 2.62), (-1.26, 2.25)]
    slab("Prow", horn, 0.74, 1.28, P["ochre"], root, s=0.97, off=(0, -0.03), bev=0.04)
    for k, x in enumerate((-0.75, -0.25, 0.25, 0.75)):
        box(f"ProwRib{k}", (0.12, 0.5, 0.07), (x, 2.12 + 0.2 * abs(x), 1.29), P["brown"], root, 0.02)
    for s in (-1, 1):
        box(f"HornLamp{s}", (0.1, 0.05, 0.16), (s * 1.0, 2.53, 1.0), P["amber"], root, 0.01, rot=(0, 0, s * 0.55))
        box(f"Headlamp{s}", (0.42, 0.04, 0.06), (s * 0.55, 2.325, 1.29), P["lamp"], root, 0.01)
        # slit visor on the windscreen slope, with a centre post, under a forward-swept fan brow
    mid, n, along, rx = on_slope((2.2, 1.45), (1.35, 1.86))
    box("VisorFrame", (1.85, 0.04, 0.34), mid + n * 0.01, P["dark"], root, 0.015, rot=(rx, 0, 0))
    box("Visor", (1.7, 0.04, 0.18), mid + n * 0.03, P["glass"], root, 0.01, rot=(rx, 0, 0))
    box("VisorPost", (0.08, 0.05, 0.3), mid + n * 0.04, P["hull"], root, 0.01, rot=(rx, 0, 0))
    # cheek fins: from the roof edge forward and down to the horn tips, framing the visor like a fan hood
    cheek = [(1.15, 1.9), (1.7, 1.93), (2.66, 1.36), (2.66, 1.2), (2.2, 1.24), (1.15, 1.55)]
    for s in (-1, 1):
        loft(f"Cheek{'L' if s < 0 else 'R'}", [[(s * x, y, z) for y, z in cheek] for x in (1.0, 1.2)], P["ochre"], root, bev=0.03)
        box(f"CheekRib{s}", (0.06, 1.2, 0.05), (s * 1.21, 2.1, 1.62), P["brown"], root, 0.01, rot=(math.radians(-25), 0, 0))
    # carapace: five overlapping plates stepping down toward the rear, a dark rib on each front lip
    for k in range(5):
        yf = 1.25 - 0.66 * k
        yb = max(yf - 0.8, -1.95)
        w = 1.84 - 0.06 * k
        h = 0.42 - 0.045 * k
        loft(f"Carapace{k}", [arch_ring(yf, w, h, 1.84, 12, 0.75), arch_ring(yb, w * 0.96, h * 0.45, 1.84, 12, 0.75)],
             P["hull"], root, bev=0.02)
        arch_rib(f"CarapaceLip{k}", yf - 0.03, w, h, 1.84, 0.035, 0.07, P["brown"], root, 12, 0.75)
        if -1.9 < yf < 1.3:  # continue the lip down the tumblehome as a side rib
            for s in (-1, 1):
                band(f"SideRib{k}{s}", [(s * 0.92, yf - 0.03, 1.86), (s * 1.15, yf - 0.03, 1.38), (s * 1.15, yf - 0.03, 1.17)],
                     [(s * 0.9, 0, 0.44), (s * 1, 0, 0.2), (s * 1, 0, 0)], [(0, 1, 0)] * 3, 0.05, 0.12, P["brown"], root)
    # side skirts over the wheels, ribbed at the ends, smooth panel in the middle for the name
    for s in (-1, 1):
        sk = "L" if s < 0 else "R"
        slab(f"Skirt{sk}", rect(s * 1.15, -2.1, s * 1.255, 1.92), 0.64, 1.16, P["grey"], root, bev=0.02)
        for k, y in enumerate((-1.95, -1.65, -1.35, -1.05, 0.8, 1.05, 1.85)):
            box(f"SkirtRib{sk}{k}", (0.05, 0.09, 0.5), (s * 1.275, y, 0.9), P["ochre"], root, 0.01)
        ribbon(f"OwnerStripe{sk}", [(-2.08, 1.115), (1.9, 1.115)], 0.07, P["owner"], root, x=s * 1.255, outward=s)
        decal(f"Name{sk}", name, 0.2, (s * 1.27, -0.1, 0.88), side_rot(s), P["number"], root, 1.3)
        decal(f"Number{sk}", number, 0.26, (s * 1.27, 1.45, 0.88), side_rot(s), P["number"], root, 0.5)
        # side slit window and a vent above the skirt
        box(f"SideSlit{sk}", (0.03, 0.9, 0.08), (s * 1.08, 0.7, 1.6), P["glass"], root, 0.01, rot=(0, s * 0.42, 0))
        for k in range(4):
            box(f"Vent{sk}{k}", (0.03, 0.06, 0.16), (s * 1.16, -1.0 - 0.15 * k, 1.27), P["dark"], root, 0)
    # rear: hatch, tail lamps, tow eye
    box("RearHatch", (1.0, 0.05, 0.42), (0, -2.33, 1.33), P["ochre"], root, 0.02)
    for s in (-1, 1):
        box(f"Tail{s}", (0.3, 0.04, 0.06), (s * 0.82, -2.33, 1.43), P["redlamp"], root, 0.01)
    decal("NameRear", name, 0.14, (0, -2.36, 1.33), (math.radians(90), 0, 0), P["number"], root, 0.8)
    box("TowEye", (0.2, 0.12, 0.12), (0, -2.3, 0.62), P["steel"], root, 0.02)
    # beacon, forked antenna, sensor head
    cylinder("BeaconBase", 0.1, 0.06, (-0.6, -2.05, 1.8), P["dark"], root, axis="Z", segments=12)
    cylinder("Beacon", 0.07, 0.13, (-0.6, -2.05, 1.89), P["amber"], root, axis="Z", segments=12)
    forked("Antenna", (0.62, -2.02, 1.78), 1.1, 0.42, 0.22, P, root, r=0.018, mat=P["steel"])
    dish = empty("Dish", (0, -0.95, 2.1), root)
    fan_head(dish, 1.25, P, mast=0.22)
    # wheels with short trailing arms
    for y in axles:
        for s in (-1, 1):
            box(f"Arm{y}{s}", (0.3, 0.16, 0.12), (s * 0.82, y, r + 0.05), P["steel"], root, 0.02)
            card_wheel(f"Wheel{'F' if y > 1 else ('M' if y > -1 else 'R')}{'L' if s < 0 else 'R'}", s * 0.98, y, r, ww, P, root)
    return root


# ---------------------------------------------------------------- mining crawler


def miner(P, pose="deploy", name="DRAVAN", number="09"):
    """Mining crawler: two armoured tracks, crescent dozer blade, offset ribbed cab, flared ribbed ore hopper (Fill),
    power unit with stacks, a drill boom (Boom > Drill > String > Bit) that folds back over the hopper for travel and
    swings forward to drill, four telescoping stabiliser legs (Leg0-3)."""
    root = empty("MiningCrawler")
    for s in (-1, 1):
        track(f"Track{'L' if s < 0 else 'R'}", s * 0.95, P, root)
        sk = "L" if s < 0 else "R"
        slab(f"TrackGuard{sk}", rect(s * 0.6, -2.05, s * 1.34, 2.0), 1.0, 1.1, P["ochre"], root, bev=0.02)
        slab(f"TrackSkirt{sk}", rect(s * 1.28, -1.95, s * 1.35, 1.9), 0.6, 1.08, P["ochre"], root, bev=0.015)
        for k, y in enumerate((-1.7, -1.2, -0.7, -0.2, 0.3, 0.8, 1.3, 1.7)):
            box(f"TrackSkirtRib{sk}{k}", (0.04, 0.08, 0.44), (s * 1.365, y, 0.84), P["brown"], root, 0.008)
    prism("Belly", [(-2.2, 0.42), (2.0, 0.42), (2.3, 0.7), (2.3, 1.05), (-2.3, 1.05)], 1.24, P["dark"], root, bev=0.03)
    loft("Hull", [sect(-2.45, 1.05, 1.05, 1.5, 1.85, 0.95), sect(-2.25, 1.12, 1.02, 1.6, 2.0, 1.02),
                  sect(1.6, 1.12, 1.02, 1.6, 2.0, 1.02), sect(2.2, 1.1, 1.05, 1.45, 1.7, 0.95),
                  sect(2.36, 1.0, 1.1, 1.35, 1.5, 0.9)], P["hull"], root, bev=0.04)
    # crescent dozer blade with push arms and lamps
    blade = [(-1.28, 2.28), (1.28, 2.28), (1.32, 2.55), (1.22, 2.82), (0.98, 2.78), (0.5, 2.56), (0.0, 2.5), (-0.5, 2.56),
             (-0.98, 2.78), (-1.22, 2.82), (-1.32, 2.55)]
    slab("Blade", blade, 0.25, 1.0, P["ochre"], root, s=0.97, off=(0, -0.05), bev=0.04)
    for k, x in enumerate((-0.75, -0.25, 0.25, 0.75)):
        box(f"BladeRib{k}", (0.1, 0.06, 0.6), (x, 2.53 + 0.06 * abs(x), 0.62), P["brown"], root, 0.01)
    for s in (-1, 1):
        box(f"Headlamp{s}", (0.36, 0.04, 0.08), (s * 0.62, 2.37, 1.3), P["lamp"], root, 0.01)
        box(f"HornLamp{s}", (0.1, 0.05, 0.14), (s * 1.08, 2.76, 0.82), P["amber"], root, 0.01, rot=(0, 0, s * 0.5))
    # offset cab (front right) with slit windows
    cab = prism("Cab", [(0.45, 1.98), (1.75, 1.98), (2.02, 2.22), (1.86, 2.8), (0.62, 2.84), (0.45, 2.62)], 0.92, P["light"], root, bev=0.04)
    cab.location.x = 0.62
    mid, n, along, rx = on_slope((2.02, 2.22), (1.86, 2.8))
    fin_prof = [(1.45, 2.86), (1.9, 2.9), (2.36, 2.24), (2.3, 2.02), (1.9, 1.99), (1.45, 2.4)]
    for k, (xa, xb) in enumerate(((1.04, 1.2), (0.1, 0.26))):
        loft(f"CabCheek{k}", [[(x, y, z) for y, z in fin_prof] for x in (xa, xb)], P["ochre"], root, bev=0.03)
    box("CabVisor", (0.74, 0.04, 0.16), Vector((0.62, 0, 0)) + mid + n * 0.02, P["glass"], root, 0.01, rot=(rx, 0, 0))
    box("CabSlitR", (0.03, 0.9, 0.09), (1.09, 1.2, 2.58), P["glass"], root, 0.01)
    box("CabSlitL", (0.03, 0.7, 0.09), (0.15, 1.3, 2.58), P["glass"], root, 0.01)
    for k in range(3):
        box(f"CabRib{k}", (0.95, 0.07, 0.05), (0.62, 0.75 + 0.35 * k, 2.86), P["ochre"], root, 0.01)
    cylinder("BeaconBase", 0.1, 0.06, (0.85, 0.65, 2.88), P["dark"], root, axis="Z", segments=12)
    cylinder("Beacon", 0.07, 0.13, (0.85, 0.65, 2.97), P["amber"], root, axis="Z", segments=12)
    decal("NumberR", number, 0.26, (1.085, 1.15, 2.33), side_rot(1), P["number"], root, 0.5)
    decal("NumberL", number, 0.26, (-1.13, 1.25, 1.27), side_rot(-1), P["number"], root, 0.5)
    # power unit and exhaust stacks between cab and hopper
    slab("PowerUnit", rect(0.15, -0.2, 1.05, 0.42), 1.98, 2.5, P["ochre"], root, s=0.92, bev=0.03)
    for k, y in enumerate((-0.05, 0.27)):
        tube(f"Stack{k}", [(0.85, y, 2.45), (0.85, y, 2.9)], 0.07, 0.07, P["steel"], root)
        slab(f"StackCap{k}", [(0.85 + x * 0.25, y + yy * 0.25) for x, yy in crescent(1.0, 0.4, 0.25)], 2.9, 2.94, P["dark"], root, bev=0)
    # ore hopper: flared ribbed bin, hollowed, ore heap (Fill) inside
    hop = slab("Hopper", rect(-0.92, -2.2, 0.98, -0.3), 1.85, 2.4, P["ochre"], root, s=1.06, bev=0.0)
    inner = slab("HopperCut", rect(-0.8, -2.08, 0.86, -0.42), 1.95, 2.7, P["dark"], None, s=1.06, bev=0.0)
    mod = hop.modifiers.new("Hollow", "BOOLEAN")
    mod.operation, mod.solver, mod.object = "DIFFERENCE", "EXACT", inner
    inner.hide_render = inner.hide_viewport = True
    bevel(hop, 0.02, 2, 30)
    for k, y in enumerate((-2.0, -1.6, -1.2, -0.8, -0.45)):
        for s in (-1, 1):
            x = 0.98 if s > 0 else -0.92
            box(f"HopperRib{k}{s}", (0.06, 0.08, 0.48), (x + s * 0.045, y, 2.1), P["brown"], root, 0.008, rot=(0, -s * 0.11, 0))
    fill = empty("Fill", (0.03, -1.25, 1.95), root)
    slab("FillOre", rect(-0.82, -0.82, 0.82, 0.82), 0, 0.3, P["ore"], fill, s=0.8, bev=0.05)
    rng = random.Random(9)
    for k in range(16):
        x, y = rng.uniform(-0.6, 0.6), rng.uniform(-0.65, 0.65)
        sz = rng.uniform(0.1, 0.2)
        box(f"Lump{k}", (sz, sz * 1.2, sz * 0.8), (x, y, 0.3 + rng.uniform(-0.02, 0.04)), P["glint"] if k % 4 == 0 else P["ore"],
            fill, 0.02, rot=(rng.uniform(0, 3), rng.uniform(0, 3), rng.uniform(0, 3)))
    # owner stripe and name along the hull sides
    for s in (-1, 1):
        sk = "L" if s < 0 else "R"
        ribbon(f"OwnerStripe{sk}", [(-2.2, 1.12), (1.6, 1.12)], 0.07, P["owner"], root, x=s * 1.12, outward=s)
        decal(f"Name{sk}", name, 0.2, (s * 1.13, 0.0, 1.3), side_rot(s), P["number"], root, 1.1)
    decal("NameRear", name, 0.16, (0, -2.465, 1.45), (math.radians(90), 0, 0), P["number"], root, 1.0)
    for s in (-1, 1):
        box(f"Tail{s}", (0.3, 0.04, 0.06), (s * 0.72, -2.465, 1.7), P["redlamp"], root, 0.01)
    # boom pedestal and the boom (pivot at its root on the front left of the deck)
    slab("BoomPedestal", rect(-0.82, 1.15, -0.18, 1.85), 1.98, 2.32, P["ochre"], root, s=0.85, bev=0.03)
    boom = empty("Boom", (-0.5, 1.5, 2.45), root)
    cylinder("BoomHinge", 0.16, 0.5, (0, 0, 0), P["steel"], boom, axis="X", segments=16)
    loft("BoomArm", [sect(0.0, 0.16, -0.14, 0.06, 0.13, 0.11), sect(2.6, 0.13, -0.11, 0.04, 0.1, 0.09),
                     sect(2.85, 0.1, -0.08, 0.02, 0.06, 0.07)], P["hull"], boom, bev=0.02)
    for k in range(5):
        box(f"BoomRib{k}", (0.36, 0.08, 0.3), (0, 0.35 + 0.5 * k, -0.01), P["brown"], boom, 0.01)
    tube("BoomRam", [(0, 0.2, -0.16), (0, 1.6, -0.14)], 0.05, 0.05, P["steel"], boom)
    drill = empty("Drill", (0, 2.72, -0.1), boom)
    for s in (-1, 1):
        box(f"DrillYoke{s}", (0.05, 0.22, 0.34), (s * 0.15, 0, -0.03), P["steel"], drill, 0.01)
    cylinder("DrillPin", 0.05, 0.4, (0, 0, 0), P["dark"], drill, axis="X", segments=10)
    cylinder("DrillHousing", 0.22, 0.5, (0, 0, -0.38), P["ochre"], drill, axis="Z", segments=20, bev=0.02)
    for k in range(3):
        f = slab(f"DrillFan{k}", [(x * 0.5, y * 0.5) for x, y in crescent(1.0, 0.25, 0.25)], 0, 0.04, P["brown"], drill, bev=0)
        f.location = (0, 0, -0.1 - 0.15 * k)
        f.rotation_euler = (0, 0, math.radians(120 * k))
    ball("DrillLamp", 0.05, (0, 0.22, -0.3), P["amber"], drill)
    string = empty("String", (0, 0, -0.63), drill)
    cylinder("StringShaft", 0.07, 1.2, (0, 0, -0.6), P["steel"], string, axis="Z", segments=12)
    for k in range(4):
        cylinder(f"StringCollar{k}", 0.1, 0.06, (0, 0, -0.15 - 0.3 * k), P["dark"], string, axis="Z", segments=12)
    bit = empty("Bit", (0, 0, -1.2), string)
    cylinder("BitCone", 0.22, 0.38, (0, 0, -0.19), P["dark"], bit, axis="Z", segments=8, radius2=0.03)
    for k in range(4):
        box(f"BitTooth{k}", (0.05, 0.3, 0.05), (0, 0, -0.1), P["steel"], bit, 0, rot=(0.5, 0, math.radians(45 * k)))
    empty("Dust", (0, 0, -0.38), bit)
    # stabiliser legs: pivots on brackets at the hull corners, telescoping lower leg + foot pad
    legs = []
    for k, (s, y) in enumerate(((-1, 1.95), (1, 1.95), (-1, -1.95), (1, -1.95))):
        box(f"Leg{k}Bracket", (0.24, 0.3, 0.3), (s * 1.18, y, 1.55), P["brown"], root, 0.02)
        leg = empty(f"Leg{k}", (s * 1.3, y, 1.55), root)
        cylinder(f"Leg{k}Hinge", 0.11, 0.3, (0, 0, 0), P["steel"], leg, axis="Y", segments=14)
        loft(f"Leg{k}Upper", [ell_ring(0.24, 0.24, 0.0, 12, 0.6), ell_ring(0.2, 0.2, -1.0, 12, 0.6)], P["ochre"], leg, bev=0.01)
        ext = empty(f"Leg{k}Ext", (0, 0, 0), leg)
        cylinder(f"Leg{k}Lower", 0.07, 1.0, (0, 0, -0.95), P["steel"], ext, axis="Z", segments=10)
        foot = empty(f"Leg{k}Foot", (0, 0, -1.47), ext)
        cylinder(f"Leg{k}Pad", 0.17, 0.08, (0, 0, -0.06), P["dark"], foot, axis="Z", segments=14, bev=0.01)
        ball(f"Leg{k}Ball", 0.08, (0, 0, 0), P["steel"], foot)
        legs.append((leg, ext, foot, s, y))
    pose_miner(boom, drill, string, legs, pose)
    return root


def pose_miner(boom, drill, string, legs, pose):
    if pose == "travel":
        boom.rotation_euler = (math.radians(176), 0, 0)
        drill.rotation_euler = (math.radians(94), 0, 0)
        string.scale = (1, 1, 0.25)
        for leg, ext, foot, s, y in legs:
            leg.rotation_euler = (math.radians(-90 if y > 0 else 90), 0, 0)
            ext.location = (0, 0, 0.75)
            foot.rotation_euler = (0, 0, 0)
    else:
        a = math.radians(-8)
        boom.rotation_euler = (a, 0, 0)
        drill.rotation_euler = (-a, 0, 0)
        string.scale = (1, 1, 1)
        spread = math.radians(38)
        for leg, ext, foot, s, y in legs:
            leg.rotation_euler = (0, -s * spread, 0)
            ext.location = (0, 0, 0.0)
            foot.rotation_euler = (0, s * spread, 0)


# ---------------------------------------------------------------- drop ship


BODY = [  # (y, width, belly z, top z)
    (6.6, 1.2, 4.2, 4.55), (5.0, 3.0, 3.7, 5.0), (3.0, 4.2, 3.38, 5.3), (-3.2, 4.2, 3.38, 5.3), (-5.4, 3.6, 3.5, 5.1),
    (-6.2, 3.0, 3.7, 4.9)]


def body_at(y):
    for (y0, w0, b0, t0), (y1, w1, b1, t1) in zip(BODY, BODY[1:]):
        if y1 <= y <= y0:
            f = (y0 - y) / (y0 - y1)
            return w0 + (w1 - w0) * f, b0 + (b1 - b0) * f, t0 + (t1 - t0) * f
    return BODY[-1][1:]


def dropship(P, cargo="miner", name="VEKTAL", number="4"):
    """Drop ship: lifting body with a ribbed dorsal spine, forward-swept fan wings drooping to wingtip landing legs,
    two rear engines, ventral lift glows and a belly cargo clamp (CargoClamp, Clamp0-3) that carries one vehicle."""
    root = empty("DropShip")
    loft("Hull", [oct_sect(y, w, zb, zt) for y, w, zb, zt in BODY], P["hull"], root, bev=0.06)
    # canopy hump with slit glass
    loft("Canopy", [oct_sect(4.9, 0.8, 4.92, 5.15), oct_sect(3.9, 1.4, 5.05, 5.6), oct_sect(2.8, 1.3, 5.2, 5.62)], P["light"], root, bev=0.04)
    for s in (-1, 1):
        box(f"CanopySlit{s}", (0.04, 0.95, 0.12), (s * 0.64, 4.0, 5.34), P["glass"], root, 0.01, rot=(0, s * 0.5, -s * 0.12))
    box("CanopyFront", (0.6, 0.04, 0.12), (0, 4.84, 5.08), P["glass"], root, 0.01, rot=(math.radians(-55), 0, 0))
    # nose: amber running lamps and a dark sensor chin
    for s in (-1, 1):
        ball(f"NoseLamp{s}", 0.1, (s * 0.45, 6.6, 4.38), P["amber"], root)
    loft("Chin", [oct_sect(5.8, 1.6, 3.75, 4.1), oct_sect(4.0, 2.6, 3.3, 3.75)], P["brown"], root, bev=0.03)
    # dorsal spine and ribs
    box("Spine", (0.5, 7.6, 0.36), (0, -1.6, 5.38), P["ochre"], root, 0.06)
    for k, y in enumerate([2.3 - 0.95 * i for i in range(8)]):
        w, zb, zt = body_at(y)
        h = zt - zb
        pts = []
        for px, pz in [(0.5, 0.45), (0.5, 0.6), (0.4, 0.8), (0.3, 1.0), (0.0, 1.03), (-0.3, 1.0), (-0.4, 0.8), (-0.5, 0.6), (-0.5, 0.45)]:
            pts.append((px * w, y, zb + pz * h))
        nrm = [(1, 0, 0), (1, 0, 0.3), (1, 0, 1), (0.4, 0, 1), (0, 0, 1), (-0.4, 0, 1), (-1, 0, 1), (-1, 0, 0.3), (-1, 0, 0)]
        band(f"Rib{k}", pts, nrm, [(0, 1, 0)] * len(pts), 0.14, 0.24, P["ochre"] if k != 3 else P["owner"], root)
    cylinder("BeaconBase", 0.14, 0.08, (0, -4.6, 5.6), P["dark"], root, axis="Z", segments=12)
    cylinder("Beacon", 0.1, 0.18, (0, -4.6, 5.72), P["amber"], root, axis="Z", segments=12)
    dish = empty("Dish", (0, 1.6, 5.55), root)
    fan_head(dish, 1.3, P, mast=0.25)
    # forward-swept wings (root at the body side, tips forward and drooping), fan ribs on top, wingtip pods
    for s in (-1, 1):
        sk = "L" if s < 0 else "R"
        xr, xt = s * 2.0, s * 6.2
        root_sec = [(xr, 1.6, 4.68), (xr, -5.2, 4.55), (xr, -5.2, 4.22), (xr, 1.6, 4.25)]
        tip_sec = [(xt, 4.4, 3.9), (xt, 2.5, 3.85), (xt, 2.5, 3.65), (xt, 4.4, 3.62)]
        loft(f"Wing{sk}", [root_sec, tip_sec], P["hull"], root, bev=0.04)
        for k, f in enumerate((0.12, 0.35, 0.58, 0.8)):
            a = Vector(root_sec[0]).lerp(Vector(root_sec[1]), f)
            b = Vector(tip_sec[0]).lerp(Vector(tip_sec[1]), f)
            pts = [a.lerp(b, t) for t in (0.0, 0.5, 1.0)]
            band(f"WingRib{sk}{k}", pts, [(0, 0, 1)] * 3, [(0, 1, 0)] * 3, 0.1, 0.16, P["owner"] if k == 0 else P["ochre"], root)
        # wingtip pod: a forward-raked blade
        pod = [[(xt + s * dx, y, z) for y, z in ((4.9, 3.85), (2.0, 4.2), (2.1, 3.15), (4.3, 3.25))] for dx in (-0.28, 0.28)]
        loft(f"TipPod{sk}", pod, P["ochre"], root, bev=0.05)
        box(f"TipLamp{sk}", (0.08, 0.3, 0.08), (xt + s * 0.25, 4.7, 3.85), P["amber"], root, 0.01)
        decal(f"Name{sk}", name, 0.34, (xt + s * 0.29, 3.2, 3.68), side_rot(s), P["number"], root, 1.8)
        decal(f"Number{sk}", number, 0.4, (s * 2.105, 2.5, 4.26), side_rot(s), P["number"], root, 0.6)
        # ventral lift glow under the wing root
        cylinder(f"LiftRing{sk}", 0.6, 0.1, (s * 3.3, -1.0, 4.12), P["dark"], root, axis="Z", segments=24)
        cylinder(f"LiftGlow{sk}", 0.48, 0.04, (s * 3.3, -1.0, 4.06), P["glow"], root, axis="Z", segments=24)
    # rear engines
    for s in (-1, 1):
        sk = "L" if s < 0 else "R"
        eng = empty(f"Engine{sk}", (s * 0.78, -6.25, 4.45), root)
        cylinder(f"Engine{sk}Bell", 0.62, 0.7, (0, 0, 0), P["dark"], eng, axis="Y", segments=24, radius2=0.5)
        cylinder(f"Engine{sk}Glow", 0.5, 0.05, (0, -0.36, 0), P["glow"], eng, axis="Y", segments=24)
        for k in range(6):
            a = 2 * math.pi * k / 6
            box(f"Engine{sk}Fin{k}", (0.06, 0.6, 0.2), (math.cos(a) * 0.62, 0, math.sin(a) * 0.62), P["ochre"], eng, 0.01,
                rot=(0, -a, 0))
        box(f"Tail{sk}", (0.3, 0.04, 0.08), (s * 1.2, -6.22, 5.0), P["redlamp"], root, 0.01)
    for k, y in enumerate((3.6, -4.6)):
        for s in (-1, 1):
            cylinder(f"BellyGlow{k}{s}", 0.32, 0.04, (s * 1.0, y, body_at(y)[1] - 0.01), P["glow"], root, axis="Z", segments=20)
    # landing legs: wingtips (Leg0 left, Leg1 right) and rear (Leg2, Leg3)
    for k, (x, y, z) in enumerate(((-6.2, 3.3, 3.2), (6.2, 3.3, 3.2), (-1.5, -5.0, 3.52), (1.5, -5.0, 3.52))):
        s = 1 if x > 0 else -1
        leg = empty(f"Leg{k}", (x, y, z), root)
        splay = math.radians(8 if k < 2 else 10)
        leg.rotation_euler = (0, -s * splay, 0)
        length = (z - 0.12) / math.cos(splay)
        loft(f"Leg{k}Strut", [ell_ring(0.36, 0.42, 0, 12, 0.6), ell_ring(0.22, 0.26, -length * 0.6, 12, 0.6)], P["ochre"], leg, bev=0.02)
        cylinder(f"Leg{k}Lower", 0.09, length * 0.5, (0, 0, -length * 0.72), P["steel"], leg, axis="Z", segments=12)
        foot = empty(f"Leg{k}Foot", (0, 0, -length), leg)
        foot.rotation_euler = (0, s * splay, 0)
        slab(f"Leg{k}Pad", [(px, py) for px, py in crescent(0.9, 0.35, 0.18)], 0.0, 0.12, P["dark"], foot, bev=0.02)
        ball(f"Leg{k}Knuckle", 0.12, (0, 0, 0.12), P["steel"], foot)
    # belly cargo clamp: two rails and four arms with pads
    clamp = empty("CargoClamp", (0, 0, 3.38), root)
    for s in (-1, 1):
        box(f"ClampRail{s}", (0.22, 5.2, 0.22), (s * 1.48, -0.1, -0.12), P["brown"], clamp, 0.03)
    for k, (s, y) in enumerate(((-1, 1.05), (1, 1.05), (-1, -1.05), (1, -1.05))):
        arm = empty(f"Clamp{k}", (s * 1.48, y, -0.15), clamp)
        box(f"Clamp{k}Arm", (0.14, 0.26, 1.5), (0, 0, -0.75), P["ochre"], arm, 0.02)
        box(f"Clamp{k}Pad", (0.3, 0.4, 0.22), (-s * 0.22, 0, -1.45), P["dark"], arm, 0.02)
        ball(f"Clamp{k}Lamp", 0.04, (s * 0.08, 0.14, -0.3), P["amber"], arm)
    if cargo == "miner":
        miner(P, "travel")
    elif cargo == "rover":
        rover(P)
    return root


# ---------------------------------------------------------------- outposts


def hex_guide(P, centres=((0.0, 0.0),), r=25 / math.sqrt(3)):
    """Preview only: the hex outline(s) on the ground (25 flat to flat)."""
    g = empty("HexGuide")
    for i, (cx, cy) in enumerate(centres):
        pts = hex_pts(r, cx, cy)
        for k in range(6):
            a, b = Vector((*pts[k], 0.03)), Vector((*pts[(k + 1) % 6], 0.03))
            d = (b - a)
            box(f"HexEdge{i}_{k}", (d.length, 0.18, 0.02), (a + b) / 2, P["guide"], g, 0, rot=(0, 0, math.atan2(d.y, d.x)))
    return g


def ribbed_block(name, x0, y0, x1, y1, z0, z1, P, parent, s=0.92, mat=None, rib_mat=None, front=(), sides=(), back=(),
                 depth=0.25, width=0.35):
    """Battered (sloped-wall) block with leaning ribs: `front` x positions on the +Y face, `back` on -Y, `sides` y
    positions on both X faces."""
    mat, rib_mat = mat or P["hull"], rib_mat or P["ochre"]
    slab(name, rect(x0, y0, x1, y1), z0, z1, mat, parent, s=s, bev=0.05)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2

    def top(x, y):
        return cx + (x - cx) * s, cy + (y - cy) * s

    def rib(nm, pb, out):
        pt = top(*pb)
        ox, oy = out
        tx, ty = -oy, ox
        quad = []
        for (px, py), z in ((pb, z0), (pt, z1 - 0.1)):
            quad.append([(px + tx * w + ox * dd, py + ty * w + oy * dd, z) for w, dd in
                         ((-width / 2, -0.05), (width / 2, -0.05), (width / 2, depth), (-width / 2, depth))])
        loft(nm, quad, rib_mat, parent, bev=0.02)

    for k, x in enumerate(front):
        rib(f"{name}RibF{k}", (x, y1), (0, 1))
    for k, x in enumerate(back):
        rib(f"{name}RibB{k}", (x, y0), (0, -1))
    for k, y in enumerate(sides):
        rib(f"{name}RibR{k}", (x1, y), (1, 0))
        rib(f"{name}RibL{k}", (x0, y), (-1, 0))


def portal(name, loc, rx, rz, P, parent, lean=0.0, door=True):
    """Oval portal: amber frame ring and a dark door ("Door"), on a wall facing +Y."""
    g = empty(name, loc, parent)
    g.rotation_euler = (lean, 0, 0)
    fr = ring(f"{name}Frame", 1.0, 0.3, 0.18 / max(rx, rz), (0, 0, 0), P["amber"], g, segments=32)
    fr.rotation_euler = (math.radians(90), 0, 0)
    fr.scale = (rx, rz, 1)
    if door:
        d = cylinder("Door" if name == "Portal" else f"{name}Door", 1.0, 0.12, (0, -0.05, 0), P["brown"], g, axis="Y", segments=32)
        d.scale = (rx * 0.92, rz * 0.92, 1)
        cylinder(f"{name}Inner", 0.55, 0.14, (0, -0.02, -0.15 * rz), P["dark"], g, axis="Y", segments=24).scale = (rx * 0.92, rz * 0.8, 1)
    return g


def floodlight(name, loc, yaw, h, P, parent):
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, yaw)
    slab(f"{name}Post", rect(-0.18, -0.18, 0.18, 0.18), 0, h, P["ochre"], g, s=0.6, bev=0.02)
    box(f"{name}Head", (0.6, 0.35, 0.3), (0, 0.12, h + 0.1), P["dark"], g, 0.03, rot=(-0.4, 0, 0))
    box(f"{name}Lamp", (0.5, 0.04, 0.2), (0, 0.3, h + 0.04), P["lamp"], g, 0.01, rot=(-0.4, 0, 0))
    ball(f"{name}Tip", 0.07, (0, 0, h + 0.3), P["amber"], g)
    return g


def crate(name, loc, yaw, P, parent, size=(2.4, 1.4, 1.3)):
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, yaw)
    sx, sy, sz = size
    box(f"{name}Body", size, (0, 0, sz / 2), P["grey"], g, 0.05)
    for k in range(5):
        box(f"{name}Rib{k}", (0.1, sy + 0.08, sz + 0.06), (-sx / 2 + 0.2 + (sx - 0.4) * k / 4, 0, sz / 2), P["ochre"], g, 0.02)
    box(f"{name}Mark", (0.5, 0.02, 0.12), (0, sy / 2 + 0.045, sz * 0.7), P["amber"], g, 0)
    return g


def pad(name, loc, r, P, parent, z=0.0, lights=8):
    g = empty(name, loc, parent)
    slab(f"{name}Deck", ngon(r, 8, phase=math.pi / 8), z, z + 0.16, P["paving"], g, s=0.97, bev=0.03)
    slab(f"{name}Mark", [(x * 0.5, y * 0.5) for x, y in crescent(r * 1.6, r * 0.4, r * 0.35)], z + 0.16, z + 0.18, P["brown"], g, bev=0)
    amber_lights(f"{name}Light", [(r * 0.9 * math.cos(2 * math.pi * k / lights), r * 0.9 * math.sin(2 * math.pi * k / lights), z + 0.18)
                                  for k in range(lights)], 0.1, P, g)
    return g


def silo(name, loc, r, h, P, parent):
    g = empty(name, loc, parent)
    cylinder(f"{name}Body", r, h, (0, 0, h / 2), P["grey"], g, axis="Z", segments=24, bev=0.04)
    for k in range(4):
        ring(f"{name}Band{k}", r + 0.06, 0.25, 0.1, (0, 0, 0.8 + (h - 1.4) * k / 3), P["ochre"], g, segments=24)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=10, radius=r)
    for v in bm.verts:
        v.co.z = max(v.co.z, 0) * 0.45
    mesh_object(f"{name}Dome", bm, P["ochre"], g, (0, 0, h), smooth=True)
    ball(f"{name}Lamp", 0.1, (0, 0, h + r * 0.45), P["amber"], g)
    return g


def screen(name, loc, yaw, w, h, z0, P, parent):
    """State broadcast screen on two posts ("Screen"): amber panel with an abstract dark emblem (not a real logo)."""
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, yaw)
    for s in (-1, 1):
        slab(f"{name}Post{s}", rect(s * w * 0.42 - 0.2, -0.2, s * w * 0.42 + 0.2, 0.2), 0, z0 + h * 0.5, P["ochre"], g, s=0.8)
    box(f"{name}Frame", (w + 0.4, 0.3, h + 0.4), (0, 0, z0 + h / 2), P["brown"], g, 0.06)
    box("Screen" if name == "Screen" else f"{name}Panel", (w, 0.05, h), (0, 0.16, z0 + h / 2), P["screen"], g, 0)
    e = cylinder(f"{name}Oval", 1.0, 0.04, (0, 0.2, z0 + h * 0.55), P["brown"], g, axis="Y", segments=28)
    e.scale = (h * 0.22, h * 0.3, 1)
    slab(f"{name}Chevron", [(0, 0), (h * 0.35, h * 0.0), (h * 0.2, h * 0.12)], 0, 0.04, P["brown"], g, bev=0)
    for s in (-1, 1):
        c = slab(f"{name}Wing{s}", [(x * h * 0.5, y * h * 0.5) for x, y in crescent(0.9, 0.18, 0.2)], 0, 0.04, P["brown"], g, bev=0)
        c.location = (s * w * 0.3, 0.22, z0 + h * 0.5)
        c.rotation_euler = (math.radians(90), math.radians(-90 * s), 0)
    bpy.data.objects[f"{name}Chevron"].location = (-h * 0.17, 0.22, z0 + h * 0.18)
    bpy.data.objects[f"{name}Chevron"].rotation_euler = (math.radians(90), 0, 0)
    return g


def wall(name, a, b, h, P, parent, thick=0.6, ribs=True):
    a, b = Vector((*a, 0)), Vector((*b, 0))
    d = b - a
    g = empty(name, (a + b) / 2, parent)
    g.rotation_euler = (0, 0, math.atan2(d.y, d.x))
    L = d.length
    slab(f"{name}Body", rect(-L / 2, -thick / 2, L / 2, thick / 2), 0, h, P["ochre"], g, s=1.0, bev=0.04)
    box(f"{name}Cap", (L, thick + 0.2, 0.2), (0, 0, h), P["brown"], g, 0.03)
    if ribs:
        n = max(2, int(L / 1.6))
        for k in range(n + 1):
            x = -L / 2 + L * k / n
            for s in (-1, 1):
                box(f"{name}Rib{k}{s}", (0.25, 0.2, h * 0.95), (x, s * (thick / 2 + 0.06), h * 0.47), P["brown"], g, 0.02)
    return g


def garrison_shell(name, P, parent, scale=1.0, owner_rib=4):
    """The landed bunker pod: a ribbed arch shell (flat floor), sloped front with the oval portal."""
    s = scale
    table = [(4.9, 5.8, 3.7), (4.3, 7.0, 4.5), (1.0, 7.4, 4.9), (-2.5, 7.0, 4.5), (-4.6, 6.0, 3.6), (-5.2, 4.8, 2.8)]
    loft(f"{name}Shell", [arch_ring(y * s, w * s, h * s, 0, 16, 0.7) for y, w, h in table], P["hull"], parent, bev=0.08)

    def wh(y):
        for (y0, w0, h0), (y1, w1, h1) in zip(table, table[1:]):
            if y1 <= y <= y0:
                f = (y0 - y) / (y0 - y1)
                return w0 + (w1 - w0) * f, h0 + (h1 - h0) * f
        return table[-1][1:]
    for k, y in enumerate((3.8, 2.6, 1.4, 0.2, -1.0, -2.2, -3.4, -4.4)):
        w, h = wh(y)
        arch_rib(f"{name}Rib{k}", y * s, w * s, h * s, 0, 0.26 * s, 0.34 * s, P["owner"] if k == owner_rib else P["ochre"], parent, 16, 0.7)
    return wh


def outpost_l1(P):
    """Garrison Post, level 1, just landed: the ribbed bunker pod on four fan buttresses inside a scorch ring, forked
    antenna, sensor head, floodlight pylons, supply crates, a small pad and the survey rover parked by the door."""
    root = empty("GarrisonPost")
    disc = cylinder("ScorchRing", 8.2, 0.02, (0, 0, 0.005), P["scorch"], root, axis="Z", segments=40)
    disc.visible_shadow = False
    garrison_shell("Garrison", P, root)
    portal("Portal", (0, 4.93, 1.65), 1.0, 1.5, P, root)
    hood = slab("PortalHood", [(x * 1.7, y * 1.7) for x, y in crescent(2.4, 0.5, 0.55)], 0, 0.28, P["ochre"], root, bev=0.04)
    hood.location = (0, 4.75, 3.3)
    amber_lights("HoodLight", [(x, 5.5 + 0.12 * abs(x), 3.28) for x in (-1.4, -0.7, 0, 0.7, 1.4)], 0.07, P, root)
    for s in (-1, 1):
        loft(f"PortalCheek{s}", [[(s * x, y, z) for y, z in ((4.6, 3.6), (6.0, 3.1), (6.1, 2.9), (5.5, 0.0), (4.6, 0.0))]
                                 for x in (1.75, 2.1)], P["ochre"], root, bev=0.04)
    box("SlitBand", (4.2, 0.05, 0.14), (0, 4.95, 3.0), P["glass"], root, 0.01)
    for k, (bx, by) in enumerate(((3.25, 2.4), (-3.25, 2.4), (3.15, -2.6), (-3.15, -2.6))):
        d = (math.copysign(1, bx), 0.55 * math.copysign(1, by))
        fin(f"Buttress{k}", (bx, by), d, 2.1, 2.9, 0.35, 0.42, P["ochre"], root, inset=0.6)
        cylinder(f"Thruster{k}", 0.38, 0.5, (bx + d[0] * 1.6, by + d[1] * 1.6, 0.7), P["dark"], root, axis="Z", segments=16, radius2=0.25)
    forked("Antenna", (0.0, -0.4, 4.75), 2.6, 1.6, 0.9, P, root, r=0.09)
    dish = empty("Dish", (0, -3.5, 3.9), root)
    fan_head(dish, 2.2, P, mast=0.35)
    cylinder("BeaconBase", 0.18, 0.12, (0, 2.9, 4.6), P["dark"], root, axis="Z", segments=12)
    cylinder("Beacon", 0.13, 0.26, (0, 2.9, 4.78), P["amber"], root, axis="Z", segments=12)
    for k, a in enumerate((35, 145, 215, 325)):
        t = math.radians(a)
        floodlight(f"Flood{k}", (9.5 * math.cos(t), 9.5 * math.sin(t), 0), t + math.pi / 2, 3.6, P, root)
    crate("Crate0", (5.6, -1.2, 0), 0.2, P, root)
    crate("Crate1", (6.1, 0.5, 0), 0.1, P, root)
    crate("Crate2", (5.85, -0.35, 1.3), 0.15, P, root, size=(2.0, 1.2, 1.1))
    crate("Crate3", (-5.6, 1.6, 0), -0.3, P, root)
    pad("Pad", (-6.6, -5.6, 0), 2.8, P, root)
    rv = rover(P)
    rv.location = (2.6, 7.6, 0)
    rv.rotation_euler = (0, 0, math.radians(-25))
    return root


def outpost_l5(P):
    """Prefecture, level 5: a hex platform, a battered two-tier ribbed seat (barrel-vault upper tier), oval portico
    under a crescent hood, owner banners, twin forked spires, a broadcast screen, guard tower, ore silos, a pad."""
    root = empty("Prefecture")
    z = 0.35
    slab("Platform", hex_pts(11.8), 0, z, P["paving"], root, s=0.985, bev=0.05)
    slab("PlatformInlay", [(x * 0.5, y * 0.5 + 4.5) for x, y in crescent(10, 3, 2.5)], z, z + 0.02, P["paving2"], root, bev=0)
    ribbed_block("LowerTier", -7, -5.5, 7, 2.5, z, 4.3, P, root, s=0.92,
                 front=(-6.4, -5.2, -4.0, -3.0, 3.0, 4.0, 5.2, 6.4), back=(-5.5, -3, 0, 3, 5.5), sides=(-4.4, -2.4, -0.4, 1.6))
    loft("UpperTier", [arch_ring(y, w, h, 4.3, 16, 0.75) for y, w, h in
                       ((1.4, 8.0, 2.8), (0.9, 9.0, 3.3), (-4.2, 9.0, 3.3), (-4.7, 8.0, 2.8))], P["light"], root, bev=0.06)
    for k, y in enumerate((0.0, -1.4, -2.8)):
        arch_rib(f"VaultRib{k}", y, 9.0, 3.3, 4.3, 0.24, 0.3, P["owner"] if k == 1 else P["ochre"], root, 16, 0.75)
    for k in range(7):
        box(f"VaultWindow{k}", (0.5, 0.05, 0.18), (-2.4 + 0.8 * k, 1.42, 5.6), P["amber"], root, 0)
    portal("Portal", (0, 2.5, 2.25), 1.3, 1.85, P, root, lean=math.radians(4))
    hood = slab("PortalHood", [(x * 2.4, y * 2.4) for x, y in crescent(2.4, 0.5, 0.55)], 0, 0.3, P["ochre"], root, bev=0.04)
    hood.location = (0, 2.6, 4.25)
    amber_lights("HoodLight", [(x, 3.5 + 0.1 * abs(x), 4.2) for x in (-2.0, -1.0, 0.0, 1.0, 2.0)], 0.09, P, root)
    for s in (-1, 1):
        box(f"Banner{s}", (0.7, 0.06, 2.8), (s * 2.4, 2.52, 2.2), P["owner"], root, 0.01, rot=(math.radians(4), 0, 0))
        box(f"BannerBar{s}", (0.9, 0.12, 0.1), (s * 2.4, 2.48, 3.65), P["steel"], root, 0.02)
        forked(f"Spire{s}", (s * 5.6, -1.6, 4.3), 8.5, 3.0, 1.6, P, root, r=0.16)
    dish = empty("Dish", (0, -3.4, 7.55), root)
    fan_head(dish, 2.4, P, mast=0.4)
    screen("Screen", (-7.0, 5.6, z), math.radians(10), 4.8, 2.9, 3.0, P, root)
    # guard tower
    tw = empty("GuardTower", (7.9, 4.2, z), root)
    slab("TowerShaft", rect(-1.1, -1.1, 1.1, 1.1), 0, 7.6, P["hull"], tw, s=0.62, bev=0.04)
    for s in (-1, 1):
        box(f"TowerRib{s}", (0.25, 0.3, 7.4), (s * 0.85, 0.9, 3.7), P["ochre"], tw, 0.02, rot=(math.radians(-4.5), 0, 0))
    slab("TowerCab", rect(-1.3, -1.3, 1.3, 1.3), 7.6, 9.2, P["ochre"], tw, s=1.12, bev=0.05)
    box("TowerSlits", (2.75, 2.75, 0.22), (0, 0, 8.55), P["glass"], tw, 0.02)
    box("TowerRoof", (3.2, 3.2, 0.25), (0, 0, 9.35), P["brown"], tw, 0.05)
    box("TowerLamp", (0.5, 0.3, 0.3), (-0.8, 1.5, 9.0), P["lamp"], tw, 0.03, rot=(-0.3, 0, 0.5))
    cylinder("BeaconBase", 0.2, 0.15, (0, 0, 9.55), P["dark"], tw, axis="Z", segments=12)
    cylinder("Beacon", 0.15, 0.3, (0, 0, 9.75), P["amber"], tw, axis="Z", segments=12)
    silo("Silo0", (7.6, -5.0, z), 1.5, 6.0, P, root)
    silo("Silo1", (4.9, -7.6, z), 1.3, 5.0, P, root)
    box("Conveyor", (0.6, 4.0, 0.5), (6.6, -2.9, 5.0), P["brown"], root, 0.04, rot=(0, 0, math.radians(15)))
    pad("Pad", (-6.4, -5.4, 0), 3.0, P, root, z=z)
    wall("WallE", (10.0, 1.5), (10.0, -4.5), 1.6, P, root)
    wall("WallW", (-10.0, 3.0), (-10.0, -2.0), 1.6, P, root)
    for k, (x, y) in enumerate(((10.2, 3.2), (-10.2, 1.0), (2.0, -10.5), (-3.0, 9.6))):
        floodlight(f"Flood{k}", (x, y, z), math.atan2(-y, -x) - math.pi / 2, 3.2, P, root)
    rv = rover(P)
    rv.location = (2.3, 7.2, z)
    rv.rotation_euler = (0, 0, math.radians(-20))
    return root


def tower_section(z, w, d):
    return ell_ring(w, d, z, 32, 0.35)


def outpost_l10(P):
    """Central Command, level 10 (capital): a towering ribbed HQ with flared shoulders and a forked crown on a stepped
    plaza, colonnade wings with broadcast screens, flanking spires, a fan monument, habitation towers, a monorail,
    silos and pads with a drop ship and the rover for scale. Spans the central hex and its neighbours."""
    root = empty("CentralCommand")
    slab("Plaza", hex_pts(27.5), 0, 0.5, P["paving"], root, s=0.99, bev=0.08)
    slab("Terrace", hex_pts(15.5), 0.5, 1.2, P["paving2"], root, s=0.98, bev=0.06)
    slab("Causeway", rect(-4.5, 9.0, 4.5, 24.5), 0.5, 0.62, P["paving2"], root, bev=0.03)
    tz = 1.2
    secs = [(tz, 18, 13), (7, 15.5, 11.5), (20, 8.5, 7), (27, 15, 8.5), (29, 14, 8), (33, 6.5, 5), (41, 4.2, 3.4), (43, 3.0, 2.4)]
    loft("Tower", [tower_section(z, w, d) for z, w, d in secs], P["hull"], root, bev=0.1)
    # vertical ribs following the front and back faces
    for k, f in enumerate((-0.62, -0.34, 0.34, 0.62)):
        for side in (1, -1):
            pts, nrm = [], []
            for z, w, d in secs[:-1]:
                x = f * w / 2
                y = side * d / 2 * (1 - abs(f) ** (1 / 0.175)) ** 0.175 if abs(f) < 1 else 0
                pts.append((x, y, z))
                nrm.append((0, side, 0))
            band(f"TowerRib{k}{'F' if side > 0 else 'B'}", pts, nrm, [(1, 0, 0)] * len(pts), 0.6, 0.7, P["ochre"], root, sink=0.25)
    # lit seam up the front, shoulder band, slit windows
    pts = [(0, d / 2, z) for z, w, d in secs[1:-1]]
    band("FrontSeam", pts, [(0, 1, 0)] * len(pts), [(1, 0, 0)] * len(pts), 0.08, 0.3, P["amber"], root, sink=0.25)
    loft("ShoulderBand", [tower_section(27.6, 15.3, 8.7), tower_section(28.3, 14.8, 8.5)], P["owner"], root)
    for k in range(6):
        z = 9 + 2.2 * k
        f = (z - 7) / 13
        d = 11.5 + (7 - 11.5) * f
        w = 15.5 + (8.5 - 15.5) * f
        for s in (-1, 1):
            box(f"TowerWindow{k}{s}", (w * 0.12, 0.1, 0.25), (s * w * 0.24, d / 2 + 0.02, z), P["amber"], root, 0)
    portal("Portal", (0, 6.6, 5.2), 2.6, 3.8, P, root, lean=math.radians(6))
    hood = slab("PortalHood", [(x * 6, y * 6) for x, y in crescent(2.0, 0.4, 0.5)], 0, 0.5, P["ochre"], root, bev=0.06)
    hood.location = (0, 6.0, 9.6)
    forked("Crown", (0, 0, 42.8), 0.8, 9.0, 4.8, P, root, r=0.55, mat=P["light"])
    cylinder("BeaconBase", 0.5, 0.3, (0, 0, 43.4), P["dark"], root, axis="Z", segments=16)
    cylinder("Beacon", 0.35, 0.6, (0, 0, 43.85), P["amber"], root, axis="Z", segments=16)
    dish = empty("Dish", (5.0, -1.0, 29.0), root)
    fan_head(dish, 3.5, P, mast=0.5)
    # colonnade wings
    for s in (-1, 1):
        sk = "L" if s < 0 else "R"
        x0, x1 = sorted((s * 9.5, s * 24.0))
        ribbed_block(f"Hall{sk}", x0, -8.0, x1, -1.0, 0.5, 7.0, P, root, s=0.95, back=[x0 + 2 + 2.5 * i for i in range(6)])
        box(f"PorticoRoof{sk}", (x1 - x0 + 1.0, 4.0, 0.6), ((x0 + x1) / 2, 0.4, 7.0), P["ochre"], root, 0.08)
        for k in range(10):
            x = x0 + 0.6 + (x1 - x0 - 1.2) * k / 9
            slab(f"Column{sk}{k}", rect(x - 0.35, 1.6, x + 0.35, 2.1), 0.5, 6.7, P["light"], root, s=0.85, bev=0.04)
            if k % 3 == 1:
                box(f"Banner{sk}{k}", (0.9, 0.06, 3.2), (x + (x1 - x0) / 18, -0.85, 4.6), P["owner"], root, 0.01)
        for k in range(8):
            box(f"HallWindow{sk}{k}", (1.0, 0.05, 0.2), (x0 + 1.5 + (x1 - x0 - 3) * k / 7, -0.98, 6.0), P["amber"], root, 0)
        forked(f"Spire{sk}", (s * 13.0, 7.5, 1.2), 23, 6, 3.0, P, root, r=0.35)
        screen(f"Screen{sk}", (s * 19.0, 8.5, 0.5), -s * math.radians(12), 6.5, 3.8, 4.0, P, root)
    # monument: a fan of five blades on a plinth
    slab("MonumentPlinth", ngon(3.0, 8, 0, 18, math.pi / 8), 0.5, 1.6, P["paving2"], root, s=0.85)
    for k in range(5):
        a = math.radians(-44 + 22 * k)
        g = empty(f"MonumentBlade{k}", (0, 18, 1.5), root)
        g.rotation_euler = (0, a, 0)
        loft(f"MonumentBladeMesh{k}", [[(-0.5, -0.3, 0), (0.5, -0.3, 0), (0.5, 0.3, 0), (-0.5, 0.3, 0)],
                                        [(-0.25, -0.15, 8.5 - 1.2 * abs(k - 2)), (0.25, -0.15, 8.5 - 1.2 * abs(k - 2)),
                                         (0.25, 0.15, 8.5 - 1.2 * abs(k - 2)), (-0.25, 0.15, 8.5 - 1.2 * abs(k - 2))]],
             P["light"] if k != 2 else P["ochre"], g, bev=0.04)
        ball(f"MonumentTip{k}", 0.22, (0, 0, 8.5 - 1.2 * abs(k - 2)), P["amber"], g)
    # habitation towers with forked crowns (rear)
    for k, (x, y, h) in enumerate(((-19, -15, 20), (19, -15, 20), (-9, -19, 16), (9, -19, 16))):
        hab = loft(f"Hab{k}", [tower_section(0.5, 6, 5), tower_section(h * 0.7, 4.4, 3.8), tower_section(h * 0.8, 5.6, 4.4),
                               tower_section(h, 3.2, 2.8)], P["hull"] if k < 2 else P["light"], root, bev=0.06)
        hab.location = (x, y, 0)
        for j in range(4):
            z = 3 + j * h * 0.14
            d = 5 + (3.8 - 5) * (z - 0.5) / (h * 0.7 - 0.5)
            box(f"Hab{k}Win{j}", (2.2, 0.06, 0.2), (x, y + d / 2 + 0.01, z), P["amber"], root, 0)
        forked(f"Hab{k}Crown", (x, y, h - 0.1), 0.6, 3.0, 1.6, P, root, r=0.2)
    # monorail along the back of the plaza
    for k in range(9):
        x = -32 + 8 * k
        slab(f"RailPylon{k}", rect(x - 0.4, -24.4, x + 0.4, -23.6), 0, 7.5, P["ochre"], root, s=0.7)
    box("RailBeam", (66, 1.2, 0.8), (0, -24, 7.9), P["brown"], root, 0.08)
    rc = loft("RailCar", [oct_sect(y, w, 8.3, 10.5) for y, w in ((-6.0, 1.6), (-5.2, 2.4), (5.2, 2.4), (6.0, 1.6))],
              P["hull"], root, bev=0.1)
    rc.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    rc.location = (-12, -24, 0)
    for k in range(6):
        box(f"RailCarWin{k}", (1.0, 0.05, 0.3), (-16 + 1.6 * k, -22.8, 9.7), P["amber"], root, 0)
    # industry and pads beyond the platform (neighbouring hexes)
    silo("Silo0", (28.5, -8.0, 0), 2.4, 10, P, root)
    silo("Silo1", (31.5, -13.0, 0), 2.0, 8, P, root)
    silo("Silo2", (27.0, -15.5, 0), 2.0, 9, P, root)
    pad("PadL", (-25.0, 15.0, 0), 7.5, P, root, z=0.0, lights=12)
    pad("PadR", (25.0, 15.0, 0), 5.0, P, root, z=0.0, lights=10)
    for k in range(10):
        t = math.radians(30 + 60 * (k // 2) + (8 if k % 2 else -8))
        a = (27.5 * math.cos(t - 0.12), 27.5 * math.sin(t - 0.12))
        b = (27.5 * math.cos(t + 0.12), 27.5 * math.sin(t + 0.12))
        if a[1] > 18 or b[1] > 18:
            continue
        wall(f"Wall{k}", a, b, 2.2, P, root)
    ds = dropship(P, cargo="none")
    ds.location = (-25.0, 15.0, 0.16)
    ds.rotation_euler = (0, 0, math.radians(30))
    rv = rover(P)
    rv.location = (6.0, 21.0, 0.62)
    rv.rotation_euler = (0, 0, math.radians(-30))
    return root


# ---------------------------------------------------------------- preview and export


SHOT_DIRS = {"front34": (7.5, 8.5, 1.9), "game": (0.0, 13.0, 14.2), "rear34": (-7.5, -8.0, 2.3), "side": (10.5, 0.4, 0.6),
             "top": (0.0, 0.01, 16.0)}
SHOT_LENS = {"front34": 40, "game": 30, "rear34": 40, "side": 38, "top": 30}
# Per model: camera distance factor (x the rover / build_vehicles shots), camera target height and an extra lift
# for the oblique shots (outposts read better from higher up).
FRAMING = {
    "rover": (1.0, 1.3, 0.0, 1.0), "fed": (1.0, 1.3, 0.0, 1.0), "miner": (1.15, 1.4, 0.0, 1.15), "dropship": (2.1, 3.2, 0.1, 2.1),
    "outpost1": (3.4, 2.4, 0.25, 1.9), "outpost5": (3.6, 3.6, 0.25, 2.0), "outpost10": (11.0, 17.0, 0.3, 5.3),
}


def preview_scene(key, folder, stem):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
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
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(55), 0, math.radians(150))
    link(sun)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=1500)
    bm.to_mesh(ground)
    bm.free()
    ground.materials.append(material("Basalt", lin("#615a50"), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))

    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.clip_end = 3000
    link(cam)
    scene.camera = cam
    k_oblique, tz, lift, k_game = FRAMING[key]
    target = Vector((0, 0, tz))
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        d = Vector(SHOT_DIRS[name])
        if name in ("front34", "rear34", "side"):
            d.z += lift * d.length
        k = k_game if name in ("game", "top") else k_oblique
        cam.location = target + d * k
        cam.data.lens = SHOT_LENS[name]
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{stem}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def main():
    BV["reset"]()
    P = palette() if MODEL != "fed" else None
    if MODEL == "rover":
        root, key, stem = rover(P), "rover", "cardassian-rover"
    elif MODEL == "miner":
        root, key = miner(P, POSE), "miner"
        stem = "cardassian-miner" + ("-travel" if POSE == "travel" else "")
    elif MODEL == "dropship":
        root, key = dropship(P, CARGO), "dropship"
        stem = "cardassian-dropship" + ("" if CARGO == "miner" else f"-{CARGO}")
    elif MODEL == "outpost":
        root = {1: outpost_l1, 5: outpost_l5, 10: outpost_l10}[LEVEL](P)
        centres = [(0.0, 0.0)] + ([(25 * math.cos(math.radians(60 * k)), 25 * math.sin(math.radians(60 * k))) for k in range(6)]
                                  if LEVEL == 10 else [])
        hex_guide(palette(), centres)
        key, stem = f"outpost{LEVEL}", f"cardassian-outpost-l{LEVEL}"
    else:
        root = BV["rover_mk1"](BV["palette"]())
        key, stem = "fed", "federation-mk1"
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in [root] + list(root.children_recursive)
           if o.type == "MESH" and not o.hide_render for c in o.bound_box]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    print(f"size {MODEL}: x {lo[0]:.2f}..{hi[0]:.2f}  y {lo[1]:.2f}..{hi[1]:.2f}  z {lo[2]:.2f}..{hi[2]:.2f}  "
          f"(W {hi[0] - lo[0]:.2f}  L {hi[1] - lo[1]:.2f}  H {hi[2] - lo[2]:.2f})")
    if OUT:
        guide = bpy.data.objects.get("HexGuide")
        if guide:
            for o in [guide] + list(guide.children_recursive):
                o.hide_render = True
        BV["export"](root, OUT)
        if guide:
            for o in [guide] + list(guide.children_recursive):
                o.hide_render = False
    if PREVIEW:
        preview_scene(key, PREVIEW, stem)


main()
