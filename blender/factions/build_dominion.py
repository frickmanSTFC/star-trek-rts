"""Dominion ground kit: Blender MOCKUPS of the Dominion's own counterparts to the Federation ground set (survey rover,
mining crawler, drop ship, colony core at levels 1 / 5 / 10). Original designs in the faction's general style, not
recreations of any existing ship, vehicle or set: violet carapace "wing-case" shells over dark gunmetal bodies with
ribbed undersides, lavender and blue-white glows, Ketracel-White canisters on everything.

Run (bpy 5.x module from PyPI, or blender --background --python ... -- <flags>):
  /root/bpyenv/bin/python blender/factions/build_dominion.py --model rover --preview blender/previews/factions/dominion
  ... --model miner [--pose deployed|stowed]
  ... --model dropship [--cargo rover|none]
  ... --model outpost --level 1|5|10
  ... --model fed        the Federation Mk I Scout (build_vehicles.py tier 1) under the same lights and cameras
  ... --model lineup     rover, crawler, drop ship and the Fed Mk I side by side, orthographic, same scale
  --res 480 --samples 8 (defaults; finals --res 800 --samples 24)   --shots front34,game[,rear34,side]
  --threads 2           Cycles threads (the CPU is shared)
  --out path.glb        also export the model (mockup only, untested in the game)
  --blank               no name/number text geometry, only the marker empties
Contact sheet (system python3 with PIL, no bpy needed):
  python3 blender/factions/build_dominion.py --sheet blender/previews/factions/dominion

Game conventions kept (see build_vehicles.py): Blender +Y = front, +Z up, units = world units; material "Owner" is the
player colour accent; the sensor head that spins is "Dish"; "Beacon" is the flashing lamp; "TailLamp" the rear lamps;
decal spots are empties NameL / NameR / NumberL / NumberR (L = -X side, R = +X side) with the text's matrix and Blender
scale (max_width, size, 1). The crawler keeps the crawler part names (Leg0-5, Boom, Drill, String, Bit, Fill); the drop
ship's cargo grabs are Clamp0-3 and its landing legs LandingLeg0-3.
build_vehicles.py is loaded WITHOUT running its main() (its source is exec'd with the trailing main() stripped), so
its helpers (box, cylinder, empty, text_mesh, material, ...) and its Mk I rover are shared, never edited.
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


# ---------------------------------------------------------------- contact sheet (PIL only, runs without bpy)

SHEET_CELLS = [
    ("Survey rover \"Vekta 07\" (hover skimmer)", "rover"),
    ("Mining crawler, drill boom unfolded", "miner"),
    ("Drop ship, survey rover in the clamps", "dropship"),
    ("Federation Mk I Scout (same cameras)", "fed-mk1"),
    ("Colony core L1 - Field Post, just landed", "outpost-l1"),
    ("Colony core L5 - Administration", "outpost-l5"),
    ("Colony core L10 - Capital", "outpost-l10"),
    ("Lineup, orthographic, same scale", "lineup"),
]


def make_sheet(folder):
    from PIL import Image, ImageDraw, ImageFont
    cw, ch, lab, pad, cols = 480, 320, 34, 10, 4
    rows = (len(SHEET_CELLS) + cols - 1) // cols
    title_h = 56
    W = cols * cw + (cols + 1) * pad
    H = title_h + rows * (2 * ch + lab) + (rows + 1) * pad
    sheet = Image.new("RGB", (W, H), (14, 12, 22))
    d = ImageDraw.Draw(sheet)

    def font(size, bold=False):
        for p in (f"/usr/share/fonts/truetype/dejavu/DejaVuSans{'-Bold' if bold else ''}.ttf",):
            if os.path.exists(p):
                return ImageFont.truetype(p, size)
        return ImageFont.load_default()

    d.text((pad + 4, 12), "DOMINION GROUND KIT - mockups (front 3/4 above, game camera below; hex = 25 units)", fill=(200, 180, 255),
           font=font(24, True))
    for i, (label, stem) in enumerate(SHEET_CELLS):
        c, r = i % cols, i // cols
        x = pad + c * (cw + pad)
        y = title_h + pad + r * (2 * ch + lab + pad)
        d.rectangle((x, y, x + cw - 1, y + lab - 1), fill=(36, 29, 52) if stem != "fed-mk1" else (40, 40, 46))
        d.text((x + 8, y + 7), label, fill=(235, 230, 244), font=font(17, True))
        for k, shot in enumerate(("front34", "game")):
            p = os.path.join(folder, f"{stem}-{shot}.png")
            box = (x, y + lab + k * ch)
            if os.path.exists(p):
                im = Image.open(p).convert("RGB")
                im.thumbnail((cw, ch))
                sheet.paste(im, (box[0] + (cw - im.width) // 2, box[1] + (ch - im.height) // 2))
            else:
                d.rectangle((box[0], box[1], box[0] + cw - 1, box[1] + ch - 1), outline=(90, 80, 110))
                d.text((box[0] + 10, box[1] + 10), f"missing {stem}-{shot}.png", fill=(160, 150, 180), font=font(14))
    out = os.path.join(folder, "dominion-sheet.png")
    sheet.save(out)
    print("wrote", out)


if "--sheet" in argv:
    make_sheet(arg("--sheet"))
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

MODEL = arg("--model", "rover")
LEVEL = int(arg("--level", "1"))
PREVIEW = arg("--preview")
RES = int(arg("--res", "480"))
SAMPLES = int(arg("--samples", "8"))
THREADS = int(arg("--threads", "2"))
SHOTS = [s.strip() for s in arg("--shots", "front34,game").split(",") if s.strip()]
POSE = arg("--pose", "deployed")
CARGO = arg("--cargo", "rover")
OUT = arg("--out")
BLANK = "--blank" in argv
if MODEL not in ("rover", "miner", "dropship", "outpost", "fed", "lineup"):
    sys.exit(f"--model takes rover, miner, dropship, outpost, fed or lineup, not {MODEL}")
if MODEL == "outpost" and LEVEL not in (1, 5, 10):
    sys.exit(f"--level must be 1, 5 or 10, not {LEVEL}")
if set(SHOTS) - {"front34", "game", "rear34", "side"}:
    sys.exit("--shots takes front34, game, rear34 and side")


def load_build_vehicles():
    """Exec build_vehicles.py without its trailing main() (as render_finish.py does). Its own flag parsing sees only
    --tier 1 --no-bake, so its survey_rover() builds the Mk I Scout and nothing is baked."""
    path = os.path.join(BLENDER_DIR, "build_vehicles.py")
    with open(path) as f:
        src = f.read()
    src = re.sub(r"^main\(\)\s*$", "", src, flags=re.M)
    saved = sys.argv
    sys.argv = [path, "--tier", "1", "--no-bake"]
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


NS = load_build_vehicles()
box, cylinder, empty, mesh_object = NS["box"], NS["cylinder"], NS["empty"], NS["mesh_object"]
material, link, text_mesh, ring = NS["material"], NS["link"], NS["text_mesh"], NS["ring"]

TAU = 2 * math.pi
HALF = math.pi / 2

# ---------------------------------------------------------------- palette


def dom_palette():
    """Violet carapace over gunmetal; lavender (DomGlow) and blue-white (KetracelWhite) light; Owner accent in gold."""
    return {
        "shell": material("DomCarapace", (0.085, 0.038, 0.18), 0.72, 0.26, coat=0.7),
        "shell2": material("DomCarapaceLight", (0.24, 0.15, 0.4), 0.6, 0.3, coat=0.5),
        "core": material("DomCore", (0.05, 0.042, 0.068), 0.75, 0.45),
        "gun": material("DomGunmetal", (0.17, 0.16, 0.19), 0.9, 0.3),
        "rib": material("DomRib", (0.1, 0.095, 0.115), 0.85, 0.38),
        "glass": material("DomGlass", (0.03, 0.012, 0.06), 0.3, 0.05, emission=(0.4, 0.22, 1.0), strength=0.15, coat=1.0),
        "glow": material("DomGlow", (0.55, 0.38, 1.0), 0.0, 0.3, emission=(0.5, 0.26, 1.0), strength=4.0),
        "seam": material("DomSeam", (0.5, 0.36, 0.9), 0.0, 0.3, emission=(0.5, 0.32, 1.0), strength=2.5),
        "white": material("KetracelWhite", (0.85, 0.93, 1.0), 0.0, 0.15, emission=(0.72, 0.88, 1.0), strength=6.0),
        "pod": material("ClonePod", (0.75, 0.8, 1.0), 0.0, 0.15, emission=(0.62, 0.7, 1.0), strength=2.6, coat=1.0),
        "window": material("DomWindow", (0.6, 0.8, 1.0), 0.0, 0.2, emission=(0.6, 0.82, 1.0), strength=2.2),
        "owner": material("Owner", (0.86, 0.52, 0.1), 0.35, 0.35),
        "glyph": material("DomGlyph", (0.86, 0.82, 1.0), 0.0, 0.4, emission=(0.6, 0.5, 1.0), strength=0.5),
        "beacon": material("BeaconGlow", (0.9, 0.85, 1.0), 0.0, 0.2, emission=(0.82, 0.7, 1.0), strength=10.0),
        "tail": material("TailLampDom", (0.7, 0.3, 1.0), 0.0, 0.3, emission=(0.65, 0.2, 1.0), strength=5.0),
        "ore": material("DomOre", (0.24, 0.18, 0.13), 0.1, 0.9),
        "gold": material("GreatLink", (1.0, 0.66, 0.2), 1.0, 0.16, emission=(1.0, 0.55, 0.12), strength=0.6),
        "rock": material("DomRock", (0.13, 0.11, 0.115), 0.0, 0.9),
        "scorch": material("Scorch", (0.07, 0.05, 0.045), 0.0, 0.97),
        "pad": material("DomPad", (0.08, 0.075, 0.09), 0.6, 0.5),
        "hex": material("HexGuide", (0.9, 0.9, 0.95), 0.0, 0.5, emission=(0.9, 0.9, 1.0), strength=0.5),
    }


# ---------------------------------------------------------------- shape helpers


def spow(c, e):
    return math.copysign(abs(c) ** (2.0 / e), c)


# A body section: (y, half width, mid z, top z, bottom z, upper exponent, lower exponent). The upper half of the
# cross-section is a superellipse of exponent e from the mid line up to `top`, the lower half one of exponent eb down to
# `bot` (eb > e: a flatter belly). t = 0 is the +X side, pi/2 the top, pi the -X side, -pi/2 the belly.


def sec_point(s, t, grow=0.0):
    y, w, zm, top, bot, e, eb = s
    c, sn = math.cos(t), math.sin(t)
    if sn >= 0:
        x, z = w * spow(c, e), zm + (top - zm) * spow(sn, e)
    else:
        x, z = w * spow(c, eb), zm + (zm - bot) * spow(sn, eb)
    if grow:
        d = Vector((x, z - zm))
        if d.length > 1e-6:
            d.normalize()
            x, z = x + d.x * grow, z + d.y * grow
    return Vector((x, y, z))


def resample(secs, steps=4):
    """Catmull-Rom through the key sections, `steps` samples per span, for smooth organic lofts."""
    out = []
    n = len(secs)
    for i in range(n - 1):
        p0, p1, p2, p3 = secs[max(i - 1, 0)], secs[i], secs[i + 1], secs[min(i + 2, n - 1)]
        for k in range(steps):
            u = k / steps
            v = [0.5 * ((2 * b) + (-a + c) * u + (2 * a - 5 * b + 4 * c - d) * u * u + (-a + 3 * b - 3 * c + d) * u ** 3)
                 for a, b, c, d in zip(p0, p1, p2, p3)]
            v[1] = max(v[1], 0.02)
            v[3] = max(v[3], v[2] + 0.01)
            v[4] = min(v[4], v[2] - 0.01)
            out.append(tuple(v))
    out.append(tuple(secs[-1]))
    return out


def section_at(secs, y):
    if y <= secs[0][0]:
        return (y,) + tuple(secs[0][1:])
    for a, b in zip(secs, secs[1:]):
        if a[0] <= y <= b[0]:
            u = (y - a[0]) / max(b[0] - a[0], 1e-9)
            return (y,) + tuple(p + (q - p) * u for p, q in zip(a[1:], b[1:]))
    return (y,) + tuple(secs[-1][1:])


def surf(secs, y, t, grow=0.0):
    return sec_point(section_at(secs, y), t, grow)


def surf_normal(secs, y, t):
    a, b = surf(secs, y, t - 0.01), surf(secs, y, t + 0.01)
    d = b - a
    n = Vector((d.z, 0, -d.x))
    return n.normalized() if n.length > 1e-9 else Vector((1, 0, 0))


def finish(o, sharp=40):
    o.data.set_sharp_from_angle(angle=math.radians(sharp))
    return o


def loft(name, secs, mat, parent=None, n=36, sharp=40):
    """Closed body through the sections (sorted by y), capped at both ends."""
    bm = bmesh.new()
    rings = [[bm.verts.new(sec_point(s, TAU * i / n)) for i in range(n)] for s in secs]
    for a, b in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    for rg, s in ((rings[0], secs[0]), (rings[-1], secs[-1])):
        c = bm.verts.new((0, s[0], s[2]))
        for i in range(n):
            bm.faces.new((rg[i], rg[(i + 1) % n], c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(mesh_object(name, bm, mat, parent, smooth=True), sharp)


def patch(name, secs, y0, y1, t0, t1, grow, thick, mat, parent=None, nt=None, dy=0.1, sharp=40, shadow=True):
    """A shell plate lying on the body: the section surface over y0..y1 and angles t0..t1, pushed out by `grow`,
    `thick` deep (a closed solid). Wing cases, ribs, stripes, visors. A full turn (t1 - t0 >= 2 pi) makes a band."""
    closed = t1 - t0 >= TAU - 1e-6
    nt = nt or max(4, int(abs(t1 - t0) / 0.07))
    ny = max(2, int((y1 - y0) / dy) + 2)
    ys = [y0 + (y1 - y0) * i / (ny - 1) for i in range(ny)]
    ts = [t0 + (t1 - t0) * j / nt for j in range(nt if closed else nt + 1)]
    bm = bmesh.new()
    O = [[bm.verts.new(surf(secs, y, t, grow)) for t in ts] for y in ys]
    I = [[bm.verts.new(surf(secs, y, t, grow - thick)) for t in ts] for y in ys]
    m = len(ts)
    jr = range(m) if closed else range(m - 1)
    for i in range(ny - 1):
        for j in jr:
            k = (j + 1) % m
            bm.faces.new((O[i][j], O[i + 1][j], O[i + 1][k], O[i][k]))
            bm.faces.new((I[i][k], I[i + 1][k], I[i + 1][j], I[i][j]))
    for j in jr:
        k = (j + 1) % m
        bm.faces.new((O[0][j], O[0][k], I[0][k], I[0][j]))
        bm.faces.new((I[-1][j], I[-1][k], O[-1][k], O[-1][j]))
    if not closed:
        for i in range(ny - 1):
            bm.faces.new((O[i][0], I[i][0], I[i + 1][0], O[i + 1][0]))
            bm.faces.new((O[i + 1][-1], I[i + 1][-1], I[i][-1], O[i][-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = finish(mesh_object(name, bm, mat, parent, smooth=True), sharp)
    o.visible_shadow = shadow
    return o


def ellipsoid(name, r, loc, mat, parent=None, rot=(0, 0, 0), seg=24, rings=12, cut_below=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * r[0], v.co.y * r[1], v.co.z * r[2]))
    if cut_below is not None:  # a dome: flatten everything under local z = cut_below
        for v in bm.verts:
            v.co.z = max(v.co.z, cut_below)
    o = mesh_object(name, bm, mat, parent, loc, smooth=True)
    o.rotation_euler = rot
    return o


def aim(o, direction, axis="Y", up="Z"):
    o.rotation_euler = Vector(direction).to_track_quat(axis, up).to_euler()
    return o


def catmull(pts, steps=6):
    pts = [Vector(p) for p in pts]
    if len(pts) < 3:
        return pts
    out = []
    n = len(pts)
    for i in range(n - 1):
        p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, n - 1)]
        for k in range(steps):
            u = k / steps
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3))
    out.append(pts[-1])
    return out


def tube(name, pts, radius, mat, parent=None, r1=None, seg=12, closed=False, loc=(0, 0, 0), sharp=50):
    """A round tube swept along a polyline (parallel-transport frames); radius tapers to r1 if given."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    T = []
    for i in range(n):
        d = (pts[(i + 1) % n] - pts[i - 1]) if closed else (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)])
        T.append(d.normalized())
    ref = Vector((0, 0, 1)) if abs(T[0].z) < 0.9 else Vector((1, 0, 0))
    N = T[0].cross(ref).normalized()
    bm = bmesh.new()
    rings = []
    for i in range(n):
        if i:
            N = T[i - 1].rotation_difference(T[i]) @ N
            N = (N - T[i] * N.dot(T[i])).normalized()
        B = T[i].cross(N)
        r = radius if r1 is None else radius + (r1 - radius) * i / (n - 1)
        r = max(r, 0.002)
        rings.append([bm.verts.new(pts[i] + r * (math.cos(TAU * k / seg) * N + math.sin(TAU * k / seg) * B)) for k in range(seg)])
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for a, b in pairs:
        for k in range(seg):
            q = (k + 1) % seg
            bm.faces.new((a[k], a[q], b[q], b[k]))
    if not closed:
        bm.faces.new(rings[0])
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(mesh_object(name, bm, mat, parent, loc, smooth=True), sharp)


def hoop(name, radius, z, r_tube, mat, parent=None, loc=(0, 0, 0), sy=1.0, seg=48):
    pts = [(radius * math.cos(TAU * k / seg), radius * sy * math.sin(TAU * k / seg), z) for k in range(seg)]
    return tube(name, pts, r_tube, mat, parent, seg=10, closed=True, loc=loc)


def revolve(name, prof, mat, parent=None, loc=(0, 0, 0), seg=40, sy=1.0, sharp=40):
    """Lathe a profile [(r, z)] listed bottom to top around local Z; r = 0 points close the ends."""
    bm = bmesh.new()
    rows = []
    for r, z in prof:
        if r < 1e-4:
            rows.append([bm.verts.new((0, 0, z))])
        else:
            rows.append([bm.verts.new((r * math.cos(TAU * i / seg), r * sy * math.sin(TAU * i / seg), z)) for i in range(seg)])
    for a, b in zip(rows, rows[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1:
                bm.faces.new((a[0], b[i], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    for rw in (rows[0], rows[-1]):
        if len(rw) > 1:
            bm.faces.new(rw)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(mesh_object(name, bm, mat, parent, loc, smooth=True), sharp)


def prof_r(prof, z):
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        if z0 <= z <= z1 and z1 > z0:
            return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    return prof[-1][0]


def meridian_ribs(name, prof, count, radius, mat, parent=None, loc=(0, 0, 0), off=0.0, phase=0.0, sy=1.0, zmin=-1e9, zmax=1e9,
                  rmin=0.15, skip=(), r1=None):
    """Ridges running up a revolved shell (profile bottom to top), pushed out `off` along the profile normal."""
    sm = [(p.x, p.y) for p in catmull([(r, z, 0) for r, z in prof], 4)]
    out = []
    for k in range(count):
        if k in skip:
            continue
        a = phase + TAU * k / count
        pts = []
        for i, (r, z) in enumerate(sm):
            if r < rmin or not (zmin <= z <= zmax):
                continue
            pa, pb = sm[max(i - 1, 0)], sm[min(i + 1, len(sm) - 1)]
            n = Vector((pb[1] - pa[1], -(pb[0] - pa[0])))
            n = n.normalized() if n.length > 1e-9 else Vector((1, 0))
            rr, zz = r + n.x * off, z + n.y * off
            pts.append((rr * math.cos(a), rr * sy * math.sin(a), zz))
        if len(pts) > 1:
            out.append(tube(f"{name}{k}", pts, radius, mat, parent, seg=8, loc=loc, r1=r1))
    return out


def extrude_xz(name, pts, y0, y1, mat, parent=None, loc=(0, 0, 0)):
    """Extrude an outline [(x, z)] (counter-clockwise seen from +Y) from y0 to y1: arched porches and door frames."""
    bm = bmesh.new()
    back = [bm.verts.new((x, y0, z)) for x, z in pts]
    front = [bm.verts.new((x, y1, z)) for x, z in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((back[i], back[j], front[j], front[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(mesh_object(name, bm, mat, parent, loc), 30)


def arch(w, h, spring=0.62, n=8):
    """A pointed (ogive) arch outline [(x, z)], w wide, h tall, the curve springing at spring*h."""
    zs = spring * h
    pts = [(-w / 2, 0), (w / 2, 0), (w / 2, zs)]
    for k in range(1, n):
        u = k / n
        pts.append((w / 2 * math.cos(u * HALF) ** 0.8, zs + (h - zs) * math.sin(u * HALF)))
    pts.append((0, h))
    for x, z in reversed(pts[3:-1]):
        pts.append((-x, z))
    pts.append((-w / 2, zs))
    return pts


def point_light(name, loc, colour, energy, parent=None, size=0.3):
    light = bpy.data.lights.new(name, "POINT")
    light.color = colour
    light.energy = energy
    light.shadow_soft_size = size
    o = bpy.data.objects.new(name, light)
    o.location = loc
    return link(o, parent)


def marker(name, matrix, width, size, parent):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.5
    e.matrix_basis = matrix @ Matrix.Diagonal((width, size, 1.0, 1.0))
    return link(e, parent)


def decal(name, body, size, matrix, mat, parent, max_width):
    """Text as geometry with `matrix` (text in local XY facing +Z, reading +X), shrunk to max_width, no shadow."""
    me = text_mesh(name, body, size)
    if me.vertices:
        xs = [v.co.x for v in me.vertices]
        if max(xs) - min(xs) > max_width:
            f = max_width / (max(xs) - min(xs))
            me.transform(Matrix.Diagonal((f, f, 1.0, 1.0)))
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    o.matrix_basis = matrix
    o.visible_shadow = False
    return link(o, parent)


def side_decals(secs, y, t, grow, kind, body, size, max_width, P, parent):
    """Name or number on both flanks at (y, t) of the body, lying on the surface tangent: a marker empty
    (NameL/NameR or NumberL/NumberR) plus, unless --blank, the text itself."""
    for s, side in ((1, "R"), (-1, "L")):
        tt = t if s > 0 else math.pi - t
        p = surf(secs, y, tt, grow + 0.012)
        # The body tapers along y: stand the text off the widest point under it so no letter sinks in.
        xs = [surf(secs, y + d * max_width / 2, tt, grow + 0.012).x for d in (-1, -0.5, 0.5, 1)]
        p.x = max([p.x] + xs) if s > 0 else min([p.x] + xs)
        n = surf_normal(secs, y, tt)
        lean = math.atan2(n.z, abs(n.x))
        face = Euler((HALF, 0, HALF if s > 0 else -HALF)).to_matrix().to_4x4()
        m = Matrix.Translation(p) @ Matrix.Rotation(-s * lean, 4, "Y") @ face
        marker(f"{kind}{side}", m, max_width, size, parent)
        if not BLANK:
            decal(f"{kind}{side}Text", body, size, m, P["glyph"], parent, max_width)


def canister(name, P, parent, loc, length=1.0, radius=0.15, rot=(0, 0, 0)):
    """A Ketracel-White canister: glowing blue-white core between gunmetal caps, three hoops."""
    c = empty(name, loc, parent)
    c.rotation_euler = rot
    cylinder(f"{name}Core", radius, length * 0.78, (0, 0, 0), P["white"], c, axis="Z", segments=16)
    for s in (-1, 1):
        cylinder(f"{name}Cap{s}", radius * 1.18, length * 0.12, (0, 0, s * length * 0.44), P["gun"], c, axis="Z", segments=16, bev=0.01)
        cylinder(f"{name}Tip{s}", radius * 0.5, length * 0.05, (0, 0, s * length * 0.52), P["core"], c, axis="Z", segments=12)
    for k, z in enumerate((-0.2, 0.0, 0.2)):
        hoop(f"{name}Hoop{k}", radius * 1.04, z * length, radius * 0.08, P["gun"], c)
    return c


def thorn(name, P, parent, loc, height, base_r, bulges=3, bulb=0.0, bulb_at=0.5, glow_at=0.82, beacon=False, seg=28):
    """A Jem'Hadar guard spire / Vorta tower: a ridged tapering thorn with banded bulges, a glow ring near the top."""
    root = empty(name, loc, parent)
    prof = []
    N = 40
    for i in range(N + 1):
        u = i / N
        r = base_r * (1 - u) ** 0.85 * (1 + 0.16 * math.sin(u * math.pi * bulges) ** 2)
        r += bulb * math.exp(-((u - bulb_at) / 0.07) ** 2)
        prof.append((r, u * height))
    prof[0] = (base_r * 1.25, 0)
    prof[-1] = (0, height)
    revolve(f"{name}Shell", prof, P["shell"], root, seg=seg)
    meridian_ribs(f"{name}Rib", prof, 6, base_r * 0.07, P["gun"], root, zmax=height * 0.9, rmin=base_r * 0.12)
    for k in range(bulges):
        u = (k + 0.5) / bulges
        if u < glow_at - 0.06:
            hoop(f"{name}Band{k}", prof_r(prof, u * height) + base_r * 0.04, u * height, base_r * 0.09, P["gun"], root)
    hoop(f"{name}Glow", prof_r(prof, glow_at * height) + base_r * 0.02, glow_at * height, base_r * 0.07, P["glow"], root)
    if bulb:
        hoop(f"{name}BulbGlow", prof_r(prof, bulb_at * height) + base_r * 0.02, bulb_at * height, base_r * 0.08, P["glow"], root)
    if beacon:
        ellipsoid("Beacon", (base_r * 0.12,) * 3, (0, 0, height * 0.985), P["beacon"], root)
    return root


def hex_guides(centres, P, parent=None, line=0.22):
    """Thin pale hexagons on the ground, 25 units across the flats (PlanetView.HexWidth), pointy ends on +-Y."""
    R = 25 / math.sqrt(3)
    for n, c in enumerate(centres):
        for k in range(6):
            a0, a1 = math.radians(30 + 60 * k), math.radians(90 + 60 * k)
            p0 = Vector((c[0] + R * math.cos(a0), c[1] + R * math.sin(a0), 0.03))
            p1 = Vector((c[0] + R * math.cos(a1), c[1] + R * math.sin(a1), 0.03))
            b = box(f"Hex{n}_{k}", ((p1 - p0).length, line, 0.03), (p0 + p1) / 2, P["hex"], parent, 0,
                    rot=(0, 0, math.atan2(p1.y - p0.y, p1.x - p0.x)))
            b.visible_shadow = False


# ---------------------------------------------------------------- survey rover

ROVER_SECS = [
    (-2.2, 0.3, 1.08, 1.3, 0.88, 2.2, 2.6),
    (-2.02, 0.82, 1.06, 1.6, 0.72, 2.3, 3.0),
    (-1.5, 1.04, 1.05, 1.78, 0.64, 2.4, 3.2),
    (-0.5, 1.1, 1.05, 1.86, 0.62, 2.4, 3.2),
    (0.6, 1.06, 1.04, 1.8, 0.62, 2.4, 3.2),
    (1.12, 0.94, 1.02, 1.62, 0.64, 2.3, 3.0),
    (1.38, 0.8, 1.0, 1.48, 0.68, 2.3, 3.0),   # neck
    (1.7, 0.84, 0.98, 1.5, 0.66, 2.3, 3.0),   # head
    (2.0, 0.7, 0.95, 1.36, 0.7, 2.2, 2.8),
    (2.22, 0.3, 0.92, 1.06, 0.8, 2.0, 2.4),
]


def beetle_shell(S, P, root, y0, y1, edge, seam, grow, thick, owner=True, seam_glow=True, prefix="Carapace", ridges=(0.55, 1.1),
                 ridge_r=0.03, grooves=3):
    """Two wing cases (elytra) over the top from y0 to y1, split by a glowing seam, gold Owner stripes on their lower
    edges. `edge` = how far below the mid line (radians) the cases reach; `seam` = half the gap at the top."""
    for side, (t0, t1) in (("R", (-edge, HALF - seam)), ("L", (HALF + seam, math.pi + edge))):
        patch(f"{prefix}{side}", S, y0, y1, t0, t1, grow, thick, P["shell"], root)
        if owner:
            st = (-edge, -edge + 0.09) if side == "R" else (math.pi + edge - 0.09, math.pi + edge)
            patch(f"OwnerStripe{prefix}{side}", S, y0 + 0.12, y1 - 0.06, st[0], st[1], grow + 0.018, 0.03, P["owner"], root, shadow=False)
    if seam_glow:
        patch(f"Seam{prefix}", S, y0 + 0.1, y1 - 0.05, HALF - seam - 0.01, HALF + seam + 0.01, grow * 0.4, 0.02, P["seam"], root)
    # Raised ridges along each case and dark grooves across them: a segmented, grown look.
    L = y1 - y0
    for side, ts in (("R", ridges), ("L", [math.pi - t for t in ridges])):
        for j, t in enumerate(ts):
            ys = [y0 + L * (0.06 + 0.88 * i / 12) for i in range(13)]
            tube(f"Ridge{prefix}{side}{j}", [surf(S, y, t, grow) for y in ys], ridge_r, P["shell2"], root, seg=8)
    for j in range(grooves):
        y = y0 + L * (j + 1) / (grooves + 1)
        for side, (t0, t1) in (("R", (-edge + 0.1, HALF - seam)), ("L", (HALF + seam, math.pi + edge - 0.1))):
            patch(f"Groove{prefix}{side}{j}", S, y - 0.02 * L / 3, y + 0.02 * L / 3, t0, t1, grow + 0.006, 0.02, P["rib"], root, shadow=False,
                  dy=0.02 * L)


def underside_ribs(S, P, root, y0, y1, step, width, grow, reach=0.12, prefix="Rib"):
    """Dark ridged bands across the belly and up the flanks, tucked under the wing cases."""
    y, k = y0, 0
    while y <= y1 + 1e-6:
        patch(f"{prefix}{k}", S, y - width / 2, y + width / 2, math.pi - reach, TAU + reach, grow, grow + 0.06, P["rib"], root, dy=width)
        y += step
        k += 1


def build_rover(P, parent=None, loc=(0, 0, 0), name="DominionRover", body_name="VEKTA", number="07"):
    """Survey skimmer: a beetle-shell hover scout. Violet wing cases over a dark ribbed body riding 0.45 above the
    ground on four lavender repulsor pods; bug-eye canopy; spinning sensor head ("Dish") with two curled prongs that
    maps deposits, a belly scanner lens, a White canister in a cradle on the back."""
    root = empty(name, loc, parent)
    S = resample(ROVER_SECS, 4)
    loft("Hull", S, P["core"], root)
    beetle_shell(S, P, root, -2.17, 1.2, 0.22, 0.035, 0.05, 0.05)
    underside_ribs(S, P, root, -1.8, 1.0, 0.4, 0.12, 0.035)
    patch("Collar", S, 1.26, 1.42, 0, TAU, 0.04, 0.1, P["gun"], root)
    # Front: dark bug-eye canopy over the head, violet cheek plates under it, blue-white lamp lenses.
    patch("Visor", S, 1.44, 2.17, 0.3, math.pi - 0.3, 0.03, 0.04, P["glass"], root)
    tube("Brow", [surf(S, y, HALF, 0.07) for y in (1.4, 1.6, 1.8, 1.98, 2.1)], 0.05, P["shell2"], root, r1=0.02, seg=8)
    for side, (t0, t1) in (("R", (-0.62, 0.3)), ("L", (math.pi - 0.3, math.pi + 0.62))):
        patch(f"Cheek{side}", S, 1.46, 2.12, t0, t1, 0.045, 0.05, P["shell"], root)
    for s in (-1, 1):
        t = -0.2 if s > 0 else math.pi + 0.2
        p = surf(S, 1.98, t, 0.06)
        lens = ellipsoid(f"Headlamp{s}", (0.07, 0.11, 0.05), p, P["white"], root)
        aim(lens, Vector((s * 0.35, 1.0, 0.0)))
        # Rear lamps on the tail.
        p = surf(S, -2.1, 0.15 if s > 0 else math.pi - 0.15, 0.03)
        ellipsoid(f"TailLamp{s}", (0.08, 0.05, 0.05), p, P["tail"], root)
    # Gunmetal chin guard under the cheeks and lavender thruster slits on the tail.
    patch("ChinGuard", S, 1.5, 2.2, -2.2, -0.62, 0.05, 0.07, P["gun"], root)
    patch("ChinGuardL", S, 1.5, 2.2, math.pi + 0.62, math.pi + 2.2, 0.05, 0.07, P["gun"], root)
    for s in (-1, 1):
        patch(f"TailVent{s}", S, -2.1, -1.98, (0.45 if s > 0 else math.pi - 0.75), (0.75 if s > 0 else math.pi - 0.45), 0.055, 0.02, P["glow"], root)
    # Ventral keel and the deposit scanner lens.
    tube("Keel", [surf(S, y, -HALF, 0.03) for y in (-1.95, -1.0, 0.0, 1.0, 1.95)], 0.06, P["gun"], root)
    ellipsoid("ScannerLens", (0.2, 0.2, 0.04), surf(S, 0.15, -HALF, 0.07), P["glow"], root)
    # Repulsor pods: gunmetal sleds tucked under the flanks, lavender emitters underneath.
    for y, tag in ((1.15, "F"), (-1.3, "R")):
        for s in (-1, 1):
            c = Vector((s * 0.95, y, 0.68))
            side = "L" if s < 0 else "R"
            ellipsoid(f"Pod{tag}{side}", (0.25, 0.58, 0.17), c, P["gun"], root)
            ellipsoid(f"PodShell{tag}{side}", (0.27, 0.5, 0.12), c + Vector((0, -0.02, 0.06)), P["shell"], root)
            box(f"PodPylon{tag}{side}", (0.14, 0.34, 0.24), (s * 0.84, y, 0.82), P["gun"], root, 0.03)
            cylinder(f"PodEmitter{tag}{side}", 0.16, 0.03, (c.x, c.y, 0.505), P["glow"], root, axis="Z", segments=24)
            cylinder(f"PodEmitterB{tag}{side}", 0.11, 0.03, (c.x, c.y + 0.3, 0.52), P["glow"], root, axis="Z", segments=20)
            cylinder(f"PodEmitterC{tag}{side}", 0.11, 0.03, (c.x, c.y - 0.3, 0.52), P["glow"], root, axis="Z", segments=20)
            point_light(f"PodLight{tag}{side}", (c.x, c.y, 0.3), (0.6, 0.42, 1.0), 18, root, 0.25)
    # Sensor head on a short mast through the top seam (the game spins "Dish").
    top = surf(S, -0.65, HALF, 0.05)
    dish = empty("Dish", top, root)
    cylinder("DishMast", 0.06, 0.2, (0, 0, 0.08), P["gun"], dish, axis="Z", segments=12)
    ellipsoid("DishHead", (0.36, 0.3, 0.07), (0, 0, 0.22), P["gun"], dish)
    ellipsoid("DishPlate", (0.31, 0.25, 0.06), (0, -0.02, 0.26), P["shell"], dish)
    hoop("DishRing", 0.3, 0.2, 0.025, P["glow"], dish, sy=0.84)
    ellipsoid("DishEye", (0.07, 0.07, 0.05), (0, 0.18, 0.28), P["glow"], dish)
    for s in (-1, 1):
        tube(f"DishProng{s}", catmull([(s * 0.2, 0.05, 0.25), (s * 0.3, 0.3, 0.3), (s * 0.24, 0.5, 0.36), (s * 0.12, 0.58, 0.32)], 5),
             0.035, P["shell"], dish, r1=0.008)
    # White canister in a cradle behind the sensor, beacon on the tail.
    zc = surf(S, -1.45, HALF, 0.05).z
    canister("WhiteCanister", P, root, (0, -1.45, zc + 0.13), 0.62, 0.1, rot=(HALF, 0, 0))
    for y in (-1.67, -1.23):
        box(f"Cradle{y}", (0.3, 0.06, 0.12), (0, y, zc + 0.04), P["gun"], root, 0.015)
    b = surf(S, -1.92, HALF, 0.05)
    cylinder("BeaconBase", 0.07, 0.06, b + Vector((0, 0, 0.02)), P["gun"], root, axis="Z", segments=14)
    ellipsoid("Beacon", (0.065, 0.065, 0.06), b + Vector((0, 0, 0.07)), P["beacon"], root)
    # Name and hull number on the wing cases.
    side_decals(S, -0.84, 0.12, 0.05, "Name", body_name, 0.2, 0.7, P, root)
    side_decals(S, -0.02, 0.12, 0.05, "Number", number, 0.26, 0.36, P, root)
    return root


# ---------------------------------------------------------------- mining crawler

MINER_SECS = [
    (-2.45, 0.3, 1.85, 2.12, 1.6, 2.2, 2.6),
    (-2.25, 0.8, 1.85, 2.5, 1.28, 2.3, 3.0),
    (-1.4, 0.96, 1.85, 2.7, 1.14, 2.4, 3.2),
    (-0.3, 0.94, 1.85, 2.66, 1.14, 2.4, 3.2),
    (0.5, 0.76, 1.83, 2.4, 1.24, 2.3, 3.0),
    (0.9, 0.7, 1.8, 2.28, 1.3, 2.3, 3.0),
    (1.5, 0.76, 1.78, 2.32, 1.26, 2.3, 3.0),
    (2.1, 0.6, 1.72, 2.14, 1.34, 2.2, 2.8),
    (2.45, 0.26, 1.68, 1.82, 1.48, 2.0, 2.4),
]


def leg_chain(name, P, root, hip, s, dy, knee_up=2.65, knee_x=1.34, foot_x=1.36, foot_r=0.12):
    """An insect leg from the hip: femur out and up to a high knee, tibia down to a clawed foot. The empty `name`
    sits at the hip so the game can swing it."""
    leg = empty(name, hip, root)
    hip = Vector(hip)
    knee = Vector((s * knee_x, hip.y + dy * 0.35, knee_up)) - hip
    ankle = Vector((s * foot_x, hip.y + dy * 0.7, 0.32)) - hip
    foot = Vector((s * (foot_x - 0.02), hip.y + dy * 0.78, 0.0)) - hip
    bend = Vector((s * 0.5, dy * 0.12, 0.45))
    tube(f"{name}Femur", catmull([(0, 0, 0), bend, knee], 6), 0.13, P["gun"], leg, r1=0.11)
    tube(f"{name}Armour", catmull([bend * 0.5, bend, bend + (knee - bend) * 0.8], 5), 0.17, P["shell"], leg, r1=0.1)
    ellipsoid(f"{name}Hip", (0.2, 0.2, 0.2), (0, 0, 0), P["rib"], leg)
    ellipsoid(f"{name}Knee", (0.16, 0.16, 0.16), knee, P["rib"], leg)
    tube(f"{name}Tibia", [knee, knee + (ankle - knee) * 0.5, ankle], 0.11, P["gun"], leg, r1=0.07)
    tube(f"{name}Greave", [knee + (ankle - knee) * 0.08 + Vector((s * 0.04, 0, 0)), knee + (ankle - knee) * 0.5 + Vector((s * 0.05, 0, 0))],
         0.15, P["shell"], leg, r1=0.09, seg=14)
    hoop(f"{name}GreaveBand", 0.12, 0, 0.03, P["owner"], leg, loc=knee + (ankle - knee) * 0.53)
    tube(f"{name}Spur", [knee + (ankle - knee) * 0.3, knee + (ankle - knee) * 0.3 + Vector((s * 0.12, -dy * 0.05, 0.08))], 0.04,
         P["shell"], leg, r1=0.005)
    ellipsoid(f"{name}Ankle", (0.08, 0.08, 0.08), ankle, P["rib"], leg)
    tube(f"{name}Claw", [ankle, foot + Vector((0, 0, 0.05))], 0.06, P["gun"], leg, r1=0.03)
    ellipsoid(f"{name}Foot", (foot_r * 1.4, foot_r * 1.8, 0.06), foot + Vector((0, 0, 0.05)), P["rib"], leg)
    for k, a in enumerate((-0.5, 0.0, 0.5)):
        d = Vector((math.sin(a) * s, math.cos(a), 0)) * 0.22
        tube(f"{name}Toe{k}", [foot + Vector((0, 0, 0.05)), foot + d + Vector((0, 0, 0.02))], 0.03, P["gun"], leg, r1=0.008)
    return leg


def build_miner(P, parent=None, loc=(0, 0, 0), name="DominionCrawler", pose="deployed", body_name="DURAK", number="12"):
    """Excavation crawler: a six-legged carapace walker. The rear wing cases part over an ore hopper ("Fill"); a
    mantis-like drill boom ("Boom" -> elbow "Drill" -> auger "String" -> "Bit") folds over the head for travel and
    unfolds forward to drive the auger into the deposit (--pose deployed, the default, or stowed)."""
    root = empty(name, loc, parent)
    S = resample(MINER_SECS, 4)
    loft("Hull", S, P["core"], root)
    # Wing cases over the abdomen, parted wide over the hopper; a head plate and a canopy on the head.
    beetle_shell(S, P, root, -2.28, 0.6, 0.24, 0.34, 0.06, 0.06, seam_glow=False)
    patch("HopperLiner", S, -2.15, 0.45, HALF - 0.36, HALF + 0.36, 0.02, 0.04, P["rib"], root)
    patch("HeadPlate", S, 0.95, 1.85, -0.22, math.pi + 0.22, 0.05, 0.05, P["shell"], root)
    patch("Visor", S, 1.78, 2.4, 0.25, math.pi - 0.25, 0.03, 0.04, P["glass"], root)
    patch("Collar", S, 0.66, 0.84, 0, TAU, 0.06, 0.12, P["gun"], root)
    underside_ribs(S, P, root, -2.05, 0.4, 0.35, 0.13, 0.04, reach=0.16)
    underside_ribs(S, P, root, 1.05, 1.65, 0.3, 0.12, 0.04, reach=0.24, prefix="HeadRib")
    for s in (-1, 1):
        p = surf(S, 2.2, -0.15 if s > 0 else math.pi + 0.15, 0.05)
        aim(ellipsoid(f"Headlamp{s}", (0.09, 0.13, 0.06), p, P["white"], root), Vector((s * 0.3, 1, 0)))
        p = surf(S, -2.38, 0.2 if s > 0 else math.pi - 0.2, 0.02)
        ellipsoid(f"TailLamp{s}", (0.07, 0.05, 0.05), p, P["tail"], root)
    # Ore in the hopper ("Fill" rises as it fills).
    import random
    rnd = random.Random(12)
    fill = empty("Fill", (0, 0, 0), root)
    for k in range(70):
        y = rnd.uniform(-2.0, 0.3)
        t = HALF + rnd.uniform(-0.28, 0.28)
        p = surf(S, y, t, 0.0) + Vector((0, 0, rnd.uniform(0.0, 0.08)))
        r = rnd.uniform(0.07, 0.15)
        o = ellipsoid(f"Ore{k}", (r, r * rnd.uniform(0.8, 1.3), r * 0.7), p, P["ore"], fill, seg=8, rings=5,
                      rot=(rnd.uniform(0, 3), rnd.uniform(0, 3), rnd.uniform(0, 3)))
    # Six legs, Leg0/1 front L/R, Leg2/3 middle, Leg4/5 rear.
    for k, (y, dy) in enumerate(((1.15, 1.0), (-0.25, 0.0), (-1.65, -1.0))):
        for s in (-1, 1):
            hip = surf(S, y, -0.95 if s > 0 else math.pi + 0.95, 0.0)
            leg_chain(f"Leg{2 * k + (0 if s < 0 else 1)}", P, root, hip, s, dy)
    # Drill boom: shoulder on the head, elbow, auger.
    P0 = surf(S, 1.3, HALF, 0.08)
    if pose == "stowed":
        P1 = P0 + Vector((0, -1.5, 0.62))
        P2 = P1 + Vector((0, 2.75, -1.25))
    else:
        P1 = P0 + Vector((0, 1.45, 0.75))
        P2 = Vector((0, 4.05, -0.05))
    boom = empty("Boom", P0, root)
    boom.matrix_basis = Matrix.Translation(P0) @ (P1 - P0).to_track_quat("Y", "Z").to_matrix().to_4x4()
    L1 = (P1 - P0).length
    cylinder("BoomHinge", 0.2, 0.5, (0, 0, 0), P["rib"], boom, axis="X", segments=20)
    tube("BoomArm", [(0, 0, 0), (0, L1 * 0.5, 0.06), (0, L1, 0)], 0.15, P["gun"], boom, r1=0.13)
    ellipsoid("BoomArmour", (0.2, L1 * 0.42, 0.16), (0, L1 * 0.48, 0.06), P["shell"], boom)
    for s in (-1, 1):
        tube(f"BoomNerve{s}", [(s * 0.17, 0.15, 0.0), (s * 0.19, L1 * 0.5, 0.04), (s * 0.15, L1 - 0.1, 0.0)], 0.022, P["glow"], boom)
    cylinder("ElbowHinge", 0.19, 0.46, (0, L1, 0), P["rib"], boom, axis="X", segments=20)
    drill = empty("Drill", (0, 0, 0), root)
    drill.matrix_basis = Matrix.Translation(P1) @ (P2 - P1).to_track_quat("-Z", "Y").to_matrix().to_4x4()
    L2 = (P2 - P1).length
    tube("DrillHousing", [(0, 0, 0), (0, 0, -0.9)], 0.2, P["gun"], drill, r1=0.18, seg=18)
    ellipsoid("DrillArmour", (0.24, 0.2, 0.42), (0, 0.04, -0.42), P["shell"], drill)
    hoop("DrillGlow", 0.19, -0.95, 0.045, P["glow"], drill)
    string = empty("String", (0, 0, -1.0), drill)
    shaft = L2 - 1.0 - 0.35
    cylinder("StringShaft", 0.08, shaft, (0, 0, -shaft / 2), P["gun"], string, axis="Z", segments=12)
    flights = [(0.17 * math.cos(a), 0.17 * math.sin(a), -a / TAU * 0.28) for a in [i * 0.35 for i in range(int(shaft / 0.28 * TAU / 0.35))]]
    tube("StringFlight", flights, 0.035, P["gun"], string, seg=6)
    bit = empty("Bit", (0, 0, -shaft), string)
    cylinder("BitCone", 0.02, 0.36, (0, 0, -0.18), P["rib"], bit, axis="Z", segments=16, radius2=0.19)
    if pose != "stowed":
        # Spoil heap and a lavender glow where the auger bites.
        for k in range(14):
            a = TAU * k / 14
            r = rnd.uniform(0.32, 0.6)
            ellipsoid(f"Spoil{k}", (0.12, 0.1, 0.06), (P2.x + r * math.cos(a), P2.y + r * math.sin(a), 0.03), P["ore"], root, seg=8, rings=5,
                      rot=(0, 0, a))
        point_light("BiteLight", (P2.x, P2.y, 0.4), (0.6, 0.42, 1.0), 25, root, 0.3)
    # White canisters on the tail, beacon on the right wing case.
    for k, x in enumerate((-0.2, 0.2)):
        canister(f"WhiteCanister{k}", P, root, (x, -2.5, 1.85), 0.55, 0.11)
    box("CanisterRack", (0.7, 0.18, 0.08), (0, -2.47, 1.54), P["gun"], root, 0.02)
    b = surf(S, -2.0, HALF - 0.5, 0.06)
    cylinder("BeaconBase", 0.07, 0.08, b + Vector((0, 0, 0.02)), P["gun"], root, axis="Z", segments=14)
    ellipsoid("Beacon", (0.07, 0.07, 0.065), b + Vector((0, 0, 0.09)), P["beacon"], root)
    side_decals(S, -0.95, 0.42, 0.06, "Name", body_name, 0.17, 0.9, P, root)
    side_decals(S, -0.95, -0.02, 0.06, "Number", number, 0.26, 0.42, P, root)
    return root


# ---------------------------------------------------------------- drop ship

DROP_SECS = [
    (-6.3, 0.25, 4.72, 4.95, 4.5, 2.0, 2.2),
    (-6.0, 0.9, 4.7, 5.3, 4.15, 2.2, 2.6),
    (-5.5, 2.1, 4.7, 5.95, 3.75, 2.3, 3.0),
    (-4.0, 2.65, 4.7, 6.3, 3.5, 2.4, 3.4),
    (-1.0, 2.8, 4.7, 6.4, 3.45, 2.4, 3.4),
    (2.0, 2.6, 4.65, 6.15, 3.5, 2.4, 3.4),
    (4.2, 2.0, 4.55, 5.65, 3.7, 2.3, 3.0),
    (5.6, 1.25, 4.45, 5.1, 3.95, 2.2, 2.6),
    (6.4, 0.35, 4.4, 4.6, 4.2, 2.0, 2.2),
]


def build_dropship(P, parent=None, loc=(0, 0, 0), name="DominionDropShip", cargo="rover", body_name="ISHAN", number="21"):
    """Drop carrier: a beetle hull that brings one ground vehicle down from orbit slung underneath in four jointed
    cargo clamps (Clamp0-3) - the "insect holding its egg" of the Dominion shipyards - and stands on four insect landing
    legs (LandingLeg0-3). Bug-eye canopy and two forward mandibles, thruster nacelles with lavender nozzles, ventral
    repulsors, three White canisters slung under the tail."""
    root = empty(name, loc, parent)
    S = resample(DROP_SECS, 4)
    loft("Hull", S, P["core"], root)
    beetle_shell(S, P, root, -5.75, 2.3, 0.26, 0.03, 0.1, 0.1, ridges=(0.5, 1.05), ridge_r=0.07, grooves=4)
    underside_ribs(S, P, root, -5.2, 3.3, 0.55, 0.2, 0.05, reach=0.18)
    # Pronotum: the shield plate between the wing cases and the head, with a keel ridge and two swept antennae.
    patch("Pronotum", S, 2.42, 4.4, -0.3, math.pi + 0.3, 0.13, 0.12, P["shell"], root)
    patch("PronotumLip", S, 2.42, 2.6, -0.3, math.pi + 0.3, 0.17, 0.1, P["gun"], root)
    tube("PronotumKeel", [surf(S, y, HALF, 0.15) for y in (2.5, 3.2, 3.9, 4.45, 4.9)], 0.12, P["shell2"], root, r1=0.04)
    for s in (-1, 1):
        a = surf(S, 4.0, HALF - s * 0.35, 0.14)
        tube(f"Antenna{s}", catmull([a, a + Vector((s * 0.4, -0.6, 0.55)), a + Vector((s * 0.75, -1.8, 0.8)), a + Vector((s * 0.9, -2.8, 0.7))], 5),
             0.07, P["gun"], root, r1=0.015)
    patch("Visor", S, 4.42, 6.25, 0.32, math.pi - 0.32, 0.04, 0.05, P["glass"], root)
    for side, (t0, t1) in (("R", (-0.7, 0.32)), ("L", (math.pi - 0.32, math.pi + 0.7))):
        patch(f"Cheek{side}", S, 4.42, 6.1, t0, t1, 0.07, 0.08, P["shell"], root)
    patch("ChinRibA", S, 4.0, 4.18, math.pi - 0.1, TAU + 0.1, 0.05, 0.11, P["rib"], root)
    patch("ChinRibB", S, 4.7, 4.88, math.pi - 0.1, TAU + 0.1, 0.05, 0.11, P["rib"], root)
    for s in (-1, 1):
        a = surf(S, 5.5, -0.45 if s > 0 else math.pi + 0.45, 0.02)
        tube(f"Mandible{s}", catmull([a, a + Vector((s * 0.35, 0.8, -0.25)), Vector((s * 0.9, 7.0, 3.95)), Vector((s * 0.45, 7.35, 4.05))], 5),
             0.2, P["shell"], root, r1=0.03)
        p = surf(S, 5.9, -0.15 if s > 0 else math.pi + 0.15, 0.08)
        aim(ellipsoid(f"Headlamp{s}", (0.14, 0.22, 0.1), p, P["white"], root), Vector((s * 0.3, 1, 0)))
        # Thruster nacelle on each flank at the back.
        c = Vector((s * 2.55, -3.95, 4.6))
        n = empty(f"Nacelle{s}", c, root)
        ellipsoid(f"NacelleBody{s}", (0.6, 1.7, 0.58), (0, 0, 0), P["gun"], n)
        ellipsoid(f"NacelleShell{s}", (0.64, 1.5, 0.46), (0, 0.1, 0.12), P["shell"], n)
        for k in range(4):
            hoop(f"NacelleRib{s}{k}", 0.6 * math.sqrt(max(0.0, 1 - ((-0.6 - 0.35 * k) / 1.7) ** 2)) + 0.02, 0, 0.05, P["rib"], n)
            bpy.data.objects[f"NacelleRib{s}{k}"].location = (0, -0.6 - 0.35 * k, 0)
            bpy.data.objects[f"NacelleRib{s}{k}"].rotation_euler = (HALF, 0, 0)
        cylinder(f"Nozzle{s}", 0.36, 0.2, (0, -1.68, 0), P["rib"], n, axis="Y", segments=24, radius2=0.42)
        cylinder(f"Engine{s}", 0.3, 0.05, (0, -1.79, 0), P["glow"], n, axis="Y", segments=24)
        p = surf(S, -6.0, 0.25 if s > 0 else math.pi - 0.25, 0.02)
        ellipsoid(f"TailLamp{s}", (0.12, 0.06, 0.08), p, P["tail"], root)
    # Ventral repulsors (lavender discs) at the belly corners.
    for k, (x, y) in enumerate(((1.6, 4.0), (-1.6, 4.0), (1.7, -4.9), (-1.7, -4.9))):
        p = surf(S, y, -HALF + (0.55 if x > 0 else -0.55), 0.02)
        cylinder(f"Repulsor{k}", 0.32, 0.05, (p.x, p.y, p.z - 0.02), P["glow"], root, axis="Z", segments=24)
        hoop(f"RepulsorRim{k}", 0.36, p.z - 0.02, 0.05, P["gun"], (root), loc=(p.x, p.y, 0))
    # White canisters slung under the tail on a gunmetal yoke.
    zb = surf(S, -4.3, -HALF).z
    box("SlingYoke", (2.0, 1.4, 0.12), (0, -4.3, zb - 0.02), P["gun"], root, 0.04)
    for k, y in enumerate((-3.85, -4.3, -4.75)):
        canister(f"WhiteCanister{k}", P, root, (0, y, zb - 0.3), 1.5, 0.17, rot=(0, HALF, 0))
        for x in (-0.55, 0.55):
            box(f"Sling{k}{x}", (0.06, 0.08, 0.3), (x, y, zb - 0.18), P["gun"], root, 0)
    # Landing legs: insect legs from the lower flanks, knees high, broad pads.
    for k, (y, dy) in enumerate(((2.6, 1.0), (-2.4, -1.0))):
        for s in (-1, 1):
            hip = surf(S, y, -0.45 if s > 0 else math.pi + 0.45, 0.0)
            leg = empty(f"LandingLeg{2 * k + (0 if s < 0 else 1)}", hip, root)
            knee = Vector((s * 3.75, y + dy * 0.5, 5.5)) - hip
            foot = Vector((s * 3.95, y + dy * 1.0, 0.12)) - hip
            tube(f"LegThigh{k}{s}", catmull([(0, 0, 0), Vector((s * 0.7, dy * 0.15, 0.9)), knee], 6), 0.24, P["gun"], leg, r1=0.18)
            tube(f"LegArmour{k}{s}", catmull([Vector((s * 0.25, 0, 0.3)), Vector((s * 0.7, dy * 0.15, 0.92)), knee * 0.9], 5), 0.3,
                 P["shell"], leg, r1=0.12)
            ellipsoid(f"LegKnee{k}{s}", (0.26, 0.26, 0.26), knee, P["rib"], leg)
            tube(f"LegShin{k}{s}", [knee, knee + (foot - knee) * 0.55, foot + Vector((0, 0, 0.15))], 0.2, P["gun"], leg, r1=0.12)
            tube(f"LegGreave{k}{s}", [knee + (foot - knee) * 0.06, knee + (foot - knee) * 0.45], 0.27, P["shell"], leg, r1=0.15, seg=14)
            tube(f"LegPiston{k}{s}", [knee + (foot - knee) * 0.1 + Vector((-s * 0.18, 0, 0)), knee + (foot - knee) * 0.6 + Vector((-s * 0.14, 0, 0))],
                 0.05, P["glow"], leg)
            cylinder(f"LegPad{k}{s}", 0.5, 0.14, foot + Vector((0, 0, -0.05)), P["rib"], leg, axis="Z", segments=20, bev=0.03)
            for j in range(3):
                a = TAU * j / 3 + 0.5
                tube(f"LegToe{k}{s}{j}", [foot + Vector((0, 0, 0.02)), foot + Vector((0.62 * math.cos(a), 0.62 * math.sin(a), -0.08))], 0.06,
                     P["gun"], leg, r1=0.02)
    # Cargo clamps: jointed grabs from the belly, gripping the vehicle's flanks.
    cargo_root = None
    if cargo == "rover":
        cargo_root = build_rover(P, root, (0, 0, 0), name="CargoRover")
        RS = resample(ROVER_SECS, 4)
    for k, (y, dy) in enumerate(((1.55, 1.0), (-1.6, -1.0))):
        for s in (-1, 1):
            hip = surf(S, y, -HALF + (0.5 if s > 0 else -0.5), 0.02)
            c = empty(f"Clamp{2 * k + (0 if s < 0 else 1)}", hip, root)
            if cargo == "rover":
                grip = surf(RS, y, 0.08 if s > 0 else math.pi - 0.08, 0.05 + 0.1)
            else:
                grip = Vector((s * 1.5, y, 1.6))
            elbow = Vector((s * 2.45, y + dy * 0.1, 2.55)) - hip
            g = grip - hip
            tube(f"ClampUpper{k}{s}", catmull([(0, 0, 0), Vector((s * 0.5, 0, -0.35)), elbow], 5), 0.13, P["gun"], c, r1=0.11)
            ellipsoid(f"ClampElbow{k}{s}", (0.15, 0.15, 0.15), elbow, P["rib"], c)
            tube(f"ClampFore{k}{s}", catmull([elbow, elbow + (g - elbow) * 0.5 + Vector((s * 0.15, 0, 0)), g + Vector((s * 0.12, 0, 0.08))], 5), 0.1,
                 P["shell"], c, r1=0.07)
            ellipsoid(f"ClampPad{k}{s}", (0.07, 0.32, 0.22), g + Vector((s * 0.04, 0, 0)), P["rib"], c)
            ellipsoid(f"ClampGlow{k}{s}", (0.03, 0.2, 0.08), g + Vector((s * 0.1, 0, 0.0)), P["glow"], c)
            ellipsoid(f"ClampHip{k}{s}", (0.18, 0.22, 0.14), (0, 0, 0), P["rib"], c)
    b = surf(S, -1.2, HALF, 0.09)
    cylinder("BeaconBase", 0.12, 0.1, b + Vector((0, 0, 0.03)), P["gun"], root, axis="Z", segments=16)
    ellipsoid("Beacon", (0.11, 0.11, 0.1), b + Vector((0, 0, 0.13)), P["beacon"], root)
    side_decals(S, 0.9, 0.0, 0.1, "Name", body_name, 0.4, 2.0, P, root)
    side_decals(S, -1.55, 0.0, 0.1, "Number", number, 0.55, 0.8, P, root)
    return root


# ---------------------------------------------------------------- colony core

HALL_SECS = [
    (-6.0, 1.7, 2.2, 3.7, 0.0, 2.2, 8.0),
    (-5.2, 3.0, 2.2, 5.6, 0.0, 2.3, 8.0),
    (-3.0, 3.8, 2.2, 7.0, 0.0, 2.4, 8.0),
    (0.5, 3.9, 2.2, 7.3, 0.0, 2.4, 8.0),
    (4.0, 3.5, 2.2, 6.6, 0.0, 2.3, 8.0),
    (6.2, 2.6, 2.2, 5.0, 0.0, 2.2, 8.0),
    (7.0, 2.1, 2.2, 4.3, 0.0, 2.2, 8.0),
]


def scaled(secs, k):
    return [(y * k, w * k, zm * k, top * k, bot * k, e, eb) for y, w, zm, top, bot, e, eb in secs]


def build_hall(P, parent, loc, k=1.0, name="Hall", spire_h=19.0):
    """Vorta Administration hall: a ribbed beetle hall under two sweeping wing-case canopies held up on jointed
    legs, a glowing window band, an arched blue-white door in the blunt front, a slender tower on the crown."""
    root = empty(name, loc, parent)
    S = resample(scaled(HALL_SECS, k), 4)
    loft(f"{name}Body", S, P["core"], root, n=48)
    y0, y1 = S[0][0], S[-1][0]
    # Window band and ribs.
    patch(f"{name}Windows", S, y0 + 1.0 * k, y1 - 0.6 * k, 0.12, 0.22, 0.02, 0.04, P["window"], root)
    patch(f"{name}WindowsL", S, y0 + 1.0 * k, y1 - 0.6 * k, math.pi - 0.22, math.pi - 0.12, 0.02, 0.04, P["window"], root)
    y, j = y0 + 0.8 * k, 0
    while y < y1 - 0.3 * k:
        patch(f"{name}Rib{j}", S, y - 0.13 * k, y + 0.13 * k, -0.95, math.pi + 0.95, 0.1 * k, 0.2 * k, P["rib"], root, dy=0.13 * k)
        y += 1.3 * k
        j += 1
    # Canopies: wing cases on a wider, taller copy of the hall sections.
    C = [(yy, w * 1.5, zm, top + 0.7 * k, bot, e, eb) for yy, w, zm, top, bot, e, eb in S]
    cy0, cy1 = y0 + 1.5 * k, y1 - 1.3 * k
    beetle_shell(C, P, root, cy0, cy1, -0.1, 0.09, 0.0, 0.2 * k, prefix=f"{name}Canopy", ridges=(0.55, 1.05), ridge_r=0.12 * k, grooves=3)
    for side in ("R", "L"):
        # Canopy legs.
        s = 1 if side == "R" else -1
        for yy in (cy0 + 1.0 * k, (cy0 + cy1) / 2, cy1 - 1.0 * k):
            e = surf(C, yy, 0.16 if s > 0 else math.pi - 0.16, -0.1 * k)
            pts = catmull([e, Vector((e.x + s * 0.55 * k, yy, e.z * 0.6)), Vector((e.x + s * 0.35 * k, yy, 0.0))], 6)
            tube(f"{name}CanopyLeg{side}{yy:.1f}", pts, 0.2 * k, P["gun"], root, r1=0.13 * k)
            cylinder(f"{name}CanopyFoot{side}{yy:.1f}", 0.32 * k, 0.12 * k, (e.x + s * 0.35 * k, yy, 0.06 * k), P["rib"], root, axis="Z", segments=16)
            ellipsoid(f"{name}CanopyKnee{side}{yy:.1f}", (0.24 * k,) * 3, (e.x + s * 0.55 * k * 0.9, yy, e.z * 0.62), P["rib"], root)
    # Crown spine joining the canopies, a glow seam along it.
    crown = [surf(S, yy, HALF, 0.22 * k) for yy in [cy0 + (cy1 - cy0) * i / 8 for i in range(9)]]
    tube(f"{name}Spine", crown, 0.32 * k, P["gun"], root, seg=14)
    tube(f"{name}SpineGlow", [p + Vector((0, 0, 0.3 * k)) for p in crown], 0.08 * k, P["seam"], root, seg=8)
    # Door in the blunt front, with a porch.
    yf = y1
    extrude_xz(f"{name}Porch", arch(2.6 * k, 3.6 * k), yf - 0.4 * k, yf + 0.35 * k, P["gun"], root)
    extrude_xz(f"{name}DoorFrame", arch(1.85 * k, 3.05 * k), yf + 0.3 * k, yf + 0.37 * k, P["white"], root)
    extrude_xz(f"{name}Door", arch(1.55 * k, 2.75 * k), yf + 0.33 * k, yf + 0.39 * k, P["glass"], root, loc=(0, 0, 0.02 * k))
    box(f"{name}Step", (2.8 * k, 1.2 * k, 0.16 * k), (0, yf + 0.9 * k, 0.08 * k), P["pad"], root, 0.03)
    point_light(f"{name}DoorLight", (0, yf + 1.5 * k, 1.4 * k), (0.7, 0.86, 1.0), 120 * k * k, root, 0.6 * k)
    # Tower on the crown.
    top = surf(S, 0.5 * k, HALF, 0.0)
    thorn(f"{name}Tower", P, root, (0, 0.5 * k, top.z - 0.2 * k), spire_h - top.z, 0.85 * k, bulges=4, bulb=0.55 * k, bulb_at=0.42,
          glow_at=0.84, beacon=True)
    return root, S


def birthing_chamber(P, parent, loc, k=1.0, name="Birthing", rot=0.0):
    """Clone line: a low ridged shell with arched pod windows - four glowing incubation pods."""
    root = empty(name, loc, parent)
    root.rotation_euler = (0, 0, rot)
    prof = [(2.8 * k, 0), (2.85 * k, 0.8 * k), (2.6 * k, 1.6 * k), (2.0 * k, 2.25 * k), (1.1 * k, 2.6 * k), (0, 2.7 * k)]
    revolve(f"{name}Shell", prof, P["shell"], root)
    meridian_ribs(f"{name}Rib", prof, 8, 0.09 * k, P["rib"], root, phase=math.pi / 8)
    hoop(f"{name}Base", 2.88 * k, 0.12 * k, 0.12 * k, P["gun"], root)
    for j in range(4):
        a = TAU * j / 4 + math.pi / 4 - math.pi / 8
        for da in (0.0,):
            p = Vector((math.cos(a + da) * 2.78 * k, math.sin(a + da) * 2.78 * k, 1.05 * k))
            frame = ellipsoid(f"{name}PodFrame{j}", (0.55 * k, 0.12 * k, 0.8 * k), p, P["gun"], root)
            frame.rotation_euler = (0, 0, a - HALF)
            pod = ellipsoid(f"{name}Pod{j}", (0.42 * k, 0.16 * k, 0.66 * k), p * 1.012, P["white"], root)
            pod.rotation_euler = (0, 0, a - HALF)
    ellipsoid(f"{name}Crown", (0.5 * k, 0.5 * k, 0.25 * k), (0, 0, 2.68 * k), P["gun"], root)
    hoop(f"{name}CrownGlow", 0.48 * k, 2.68 * k, 0.05 * k, P["glow"], root)
    return root


def white_facility(P, parent, loc, k=1.0, arms=4, name="WhiteFacility"):
    """Ketracel-White store: a hub dome with arms out to tall glowing canisters."""
    root = empty(name, loc, parent)
    prof = [(1.4 * k, 0), (1.45 * k, 0.5 * k), (1.2 * k, 1.2 * k), (0.6 * k, 1.65 * k), (0, 1.75 * k)]
    revolve(f"{name}Hub", prof, P["shell"], root)
    meridian_ribs(f"{name}HubRib", prof, arms * 2, 0.06 * k, P["rib"], root, phase=math.pi / (arms * 2))
    hoop(f"{name}HubGlow", 1.36 * k, 0.75 * k, 0.05 * k, P["glow"], root)
    cylinder(f"{name}Base", 2.9 * k, 0.12 * k, (0, 0, 0.06 * k), P["pad"], root, axis="Z", segments=arms * 6)
    for j in range(arms):
        a = TAU * j / arms
        d = Vector((math.cos(a), math.sin(a), 0))
        tube(f"{name}Arm{j}", [d * 1.2 * k + Vector((0, 0, 0.6 * k)), d * 1.9 * k + Vector((0, 0, 0.75 * k)), d * 2.35 * k + Vector((0, 0, 0.6 * k))],
             0.13 * k, P["gun"], root)
        canister(f"{name}Can{j}", P, root, d * 2.35 * k + Vector((0, 0, 1.05 * k)), 1.9 * k, 0.3 * k)
    return root


def landing_pad(P, parent, loc, r=3.0, name="Pad"):
    root = empty(name, loc, parent)
    cylinder(f"{name}Deck", r, 0.16, (0, 0, 0.08), P["pad"], root, axis="Z", segments=6, bev=0.04).rotation_euler = (0, 0, math.pi / 6)
    hoop(f"{name}Ring", r * 0.8, 0.17, 0.05, P["glow"], root, seg=60)
    hoop(f"{name}OwnerRing", r * 0.92, 0.17, 0.04, P["owner"], root, seg=60)
    for j in range(6):
        a = TAU * j / 6
        cylinder(f"{name}Light{j}", 0.12, 0.08, (r * 0.97 * math.cos(a), r * 0.97 * math.sin(a), 0.2), P["glow"], root, axis="Z", segments=10)
    return root


def cargo_capsule(P, parent, loc, rot=0.0, k=1.0, name="Capsule"):
    secs = [(-1.0 * k, 0.1 * k, 0.5 * k, 0.6 * k, 0.4 * k, 2.2, 2.2), (-0.8 * k, 0.5 * k, 0.5 * k, 0.98 * k, 0.04 * k, 2.4, 3.0),
            (0.8 * k, 0.5 * k, 0.5 * k, 0.98 * k, 0.04 * k, 2.4, 3.0), (1.0 * k, 0.1 * k, 0.5 * k, 0.6 * k, 0.4 * k, 2.2, 2.2)]
    root = empty(name, loc, parent)
    root.rotation_euler = (0, 0, rot)
    S = resample(secs, 3)
    loft(f"{name}Body", S, P["shell"], root, n=24)
    for j, y in enumerate((-0.55 * k, 0.0, 0.55 * k)):
        patch(f"{name}Band{j}", S, y - 0.06 * k, y + 0.06 * k, 0, TAU, 0.03 * k, 0.06 * k, P["rib"], root, dy=0.06 * k)
    patch(f"{name}Glow", S, -0.5 * k, 0.5 * k, HALF - 0.08, HALF + 0.08, 0.01, 0.02, P["seam"], root)
    return root


def build_outpost(P, level, parent=None, loc=(0, 0, 0)):
    root = empty(f"DominionColonyCoreL{level}", loc, parent)
    {1: outpost_l1, 5: outpost_l5, 10: outpost_l10}[level](P, root)
    return root


def outpost_l1(P, root):
    """Vorta Field Post, just landed: a ridged dome on a landing skirt and splayed feet, still-hot thruster vents, a
    scorched ring, ramp down from an arched blue-white door, crown with two curling prongs and the Beacon, two guard
    spires, a White cache and two cargo capsules."""
    # Scorch where it came down, and the landing skirt with feet.
    cylinder("Scorch", 7.6, 0.02, (0, 0, 0.01), P["scorch"], root, axis="Z", segments=48)
    revolve("Skirt", [(0, 0.05), (4.3, 0.05), (4.6, 0.12), (4.72, 0.36), (4.6, 0.62), (4.2, 0.72), (0, 0.72)], P["gun"], root, seg=48)
    hoop("OwnerBand", 4.62, 0.48, 0.07, P["owner"], root, seg=64)
    for j in range(6):
        a = TAU * j / 6 + math.pi / 6
        d = Vector((math.cos(a), math.sin(a), 0))
        tube(f"Foot{j}", catmull([d * 4.2 + Vector((0, 0, 0.55)), d * 5.0 + Vector((0, 0, 0.9)), d * 5.6 + Vector((0, 0, 0.12))], 5), 0.18, P["gun"], root, r1=0.12)
        cylinder(f"FootPad{j}", 0.45, 0.12, d * 5.6 + Vector((0, 0, 0.06)), P["rib"], root, axis="Z", segments=16)
        v = d.copy()
        v.rotate(Euler((0, 0, math.pi / 6)))
        box(f"Vent{j}", (0.9, 0.08, 0.16), v * 4.66 + Vector((0, 0, 0.3)), P["glow"], root, 0.02, rot=(0, 0, math.atan2(v.y, v.x) + HALF))
    # Ridged dome.
    prof = [(4.2, 0.6), (4.3, 1.0), (4.2, 1.9), (3.85, 3.0), (3.2, 4.05), (2.3, 4.8), (1.2, 5.2), (0, 5.3)]
    revolve("Dome", prof, P["shell"], root, seg=56)
    meridian_ribs("DomeRib", prof, 10, 0.26, P["rib"], root, off=-0.08, phase=TAU / 20, zmin=0.62, rmin=1.0, r1=0.07)
    meridian_ribs("DomeRidge", prof, 10, 0.1, P["shell2"], root, off=0.0, phase=TAU / 20 + TAU / 20, zmin=1.2, zmax=4.6, rmin=1.4, r1=0.03)
    hoop("DomeBand", prof_r(prof, 0.85) + 0.04, 0.85, 0.12, P["gun"], root, seg=64)
    # Door porch at the front (+Y), glowing door, ramp.
    extrude_xz("Porch", arch(2.4, 3.4), 3.6, 4.75, P["gun"], root, loc=(0, 0, 0.62))
    extrude_xz("DoorFrame", arch(1.75, 2.85), 4.74, 4.78, P["white"], root, loc=(0, 0, 0.62))
    extrude_xz("Door", arch(1.45, 2.55), 4.76, 4.81, P["glass"], root, loc=(0, 0, 0.64))
    ramp = box("Ramp", (1.7, 2.0, 0.12), (0, 5.65, 0.36), P["pad"], root, 0.03, rot=(-math.atan2(0.62, 1.9), 0, 0))
    point_light("DoorLight", (0, 5.8, 1.5), (0.7, 0.86, 1.0), 60, root, 0.4)
    # Crown: bulb with a glow ring, Beacon, two curling prongs.
    ellipsoid("Crown", (1.1, 1.1, 0.55), (0, 0, 5.25), P["gun"], root)
    hoop("CrownGlow", 1.08, 5.25, 0.08, P["glow"], root)
    ellipsoid("CrownCap", (0.75, 0.75, 0.4), (0, 0, 5.6), P["shell"], root)
    cylinder("BeaconBase", 0.16, 0.2, (0, 0, 6.02), P["gun"], root, axis="Z", segments=16)
    ellipsoid("Beacon", (0.17, 0.17, 0.15), (0, 0, 6.2), P["beacon"], root)
    for s in (-1, 1):
        tube(f"Prong{s}", catmull([(s * 0.75, 0, 5.4), (s * 1.35, 0.1, 6.3), (s * 1.15, 0.15, 7.15), (s * 0.55, 0.1, 7.5)], 6), 0.17, P["shell"], root, r1=0.02)
    # Guard spires flanking the door.
    for s in (-1, 1):
        thorn(f"GuardSpire{s}", P, root, (s * 6.3, 2.7, 0), 7.2, 0.62, bulges=3)
    # White cache: two rows of three canisters in a rack.
    rack = empty("WhiteCache", (-5.3, -3.0, 0), root)
    rack.rotation_euler = (0, 0, math.radians(30))
    box("CacheBase", (2.3, 1.5, 0.22), (0, 0, 0.11), P["gun"], rack, 0.05)
    for i in range(3):
        for j in range(2):
            canister(f"Cache{i}{j}", P, rack, ((i - 1) * 0.7, (j - 0.5) * 0.7, 0.8), 1.15, 0.22)
    box("CacheFrame", (2.3, 0.12, 0.12), (0, 0, 1.45), P["gun"], rack, 0.02)
    cargo_capsule(P, root, (4.9, -4.3, 0), rot=math.radians(-25), name="CapsuleA")
    cargo_capsule(P, root, (6.1, -2.4, 0), rot=math.radians(-70), name="CapsuleB")
    hex_guides([(0, 0)], P, root)


def outpost_l5(P, root):
    """Vorta Administration (level 5): the hall under its canopies with the tower, a birthing chamber, a White store
    piped to the hall, a landing pad with a parked survey rover, guard spires front, back and on the flanks."""
    hall, S = build_hall(P, root, (0, -0.4, 0))
    birthing_chamber(P, root, (-8.0, -6.4, 0), name="Birthing")
    white_facility(P, root, (7.6, -5.6, 0))
    landing_pad(P, root, (7.0, 6.0, 0), r=3.0)
    build_rover(P, root, (7.0, 6.0, 0.17), name="ParkedRover").rotation_euler = (0, 0, math.radians(-30))
    for j, (x, y) in enumerate(((0, 11.6), (0, -11.4), (-10.6, 1.5), (10.6, 1.5))):
        thorn(f"GuardSpire{j}", P, root, (x, y, 0), 7.5, 0.62, bulges=3)
    # White feed: pipe on short posts from the store to the hall flank.
    pts = [Vector((5.4, -5.0, 0.45)), Vector((4.6, -4.2, 0.45)), Vector((3.7, -3.5, 0.45))]
    tube("WhiteFeed", pts, 0.16, P["gun"], root)
    tube("WhiteFeedGlow", [p + Vector((0, 0, 0.17)) for p in pts], 0.05, P["white"], root)
    for p in pts[:2]:
        cylinder(f"FeedPost{p.x:.1f}", 0.08, 0.4, (p.x, p.y, 0.2), P["gun"], root, axis="Z", segments=8)
    pts = [Vector((-6.0, -4.9, 0.4)), Vector((-4.9, -4.2, 0.4)), Vector((-3.75, -3.5, 0.4))]
    tube("CloneFeed", pts, 0.14, P["gun"], root)
    tube("CloneFeedGlow", [p + Vector((0, 0, 0.15)) for p in pts], 0.045, P["white"], root)
    cargo_capsule(P, root, (-7.6, 4.8, 0), rot=math.radians(20), name="CapsuleA")
    cargo_capsule(P, root, (-6.2, 6.6, 0), rot=math.radians(80), name="CapsuleB")
    hex_guides([(0, 0)], P, root)


def outpost_l10(P, root):
    """Capital (level 10): the hall at double size with three towers, cloning towers, a big White store, a basin of
    the golden Great Link with a Vorta sanctuary on a crag, a landing field with drop ships, a ring of guard spires.
    Spans the centre hex and its six neighbours."""
    import random
    rnd = random.Random(10)
    hall, S = build_hall(P, root, (0, 2.0, 0), k=2.0, spire_h=44.0)
    for s in (-1, 1):
        thorn(f"SideTower{s}", P, root, (s * 11.5, 6.0, 0), 28.0, 1.5, bulges=5, bulb=0.9, bulb_at=0.6, beacon=False)
    # Cloning towers (west).
    for j, (x, y, h) in enumerate(((-25.0, 4.0, 20.0), (-30.0, 12.0, 17.0), (-21.0, 13.0, 15.0))):
        t = empty(f"CloneTower{j}", (x, y, 0), root)
        prof = [(2.5, 0), (2.7, 0.6), (2.3, 2.0), (2.35, h * 0.35), (2.1, h * 0.7), (2.25, h * 0.82), (1.3, h * 0.95), (0, h)]
        revolve(f"CloneTowerShell{j}", prof, P["shell"], t)
        meridian_ribs(f"CloneTowerRib{j}", prof, 8, 0.11, P["rib"], t, off=0.02)
        for c in range(4):
            a = TAU * (c + 0.5) / 4
            zs = [2.0 + 0.5 * i for i in range(int((h * 0.78 - 2.0) / 0.5) + 1)]
            tube(f"PodColumn{j}_{c}", [(prof_r(prof, z) * math.cos(a), prof_r(prof, z) * math.sin(a), z) for z in zs], 0.55, P["core"], t, seg=10)
            z = 2.6
            while z < h * 0.74:
                r = prof_r(prof, z) + 0.6
                pod = ellipsoid(f"ClonePod{j}_{c}_{z:.1f}", (0.32, 0.14, 0.5), (r * math.cos(a), r * math.sin(a), z), P["pod"], t, seg=12, rings=8)
                pod.rotation_euler = (0, 0, a - HALF)
                z += 1.4
        hoop(f"CloneTowerGlow{j}", prof_r(prof, h * 0.88) + 0.05, h * 0.88, 0.12, P["glow"], t)
    birthing_chamber(P, root, (-20.0, -10.0, 0), k=1.3, name="BirthingA")
    birthing_chamber(P, root, (-28.0, -3.0, 0), k=1.1, name="BirthingB")
    # White store (east).
    white_facility(P, root, (25.0, 8.0, 0), k=1.9, arms=6, name="WhiteStore")
    # Great Link basin (south) with crags and a Vorta sanctuary on a pillar.
    bx, by, br = 0.0, -27.0, 8.5
    revolve("BasinRim", [(br + 3.2, 0), (br + 2.2, 1.0), (br + 0.6, 1.5), (br, 1.0), (br - 0.3, 0.55), (br - 0.35, 0.3)], P["rock"], root,
            loc=(bx, by, 0), seg=64)
    cylinder("Link", br + 0.05, 0.3, (bx, by, 0.55), P["gold"], root, axis="Z", segments=64)
    for k in range(9):
        a, r = rnd.uniform(0, TAU), rnd.uniform(0, br - 2.0)
        ellipsoid(f"LinkSwell{k}", (rnd.uniform(0.8, 1.8), rnd.uniform(0.8, 1.8), rnd.uniform(0.25, 0.6)), (bx + r * math.cos(a), by + r * math.sin(a), 0.65),
                  P["gold"], root, seg=16, rings=8)
    for k in range(16):
        a = TAU * k / 16 + rnd.uniform(-0.1, 0.1)
        r = br + rnd.uniform(1.5, 3.0)
        h = rnd.uniform(2.0, 5.5)
        c = cylinder(f"Crag{k}", rnd.uniform(0.7, 1.4), h, (bx + r * math.cos(a), by + r * math.sin(a), h / 2 - 0.2), P["rock"], root, axis="Z",
                     segments=6, radius2=rnd.uniform(0.05, 0.3))
        c.rotation_euler = (rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), rnd.uniform(0, 1))
    pillar = cylinder("SanctuaryCrag", 2.6, 7.0, (bx + br + 1.5, by - 3.0, 3.5), P["rock"], root, axis="Z", segments=7, radius2=1.8)
    birthing_chamber(P, root, (bx + br + 1.5, by - 3.0, 7.0), k=0.75, name="Sanctuary")
    # Landing field (southeast) with two drop ships.
    for j, (x, y) in enumerate(((22.0, -14.0), (30.0, -24.0))):
        landing_pad(P, root, (x, y, 0), r=7.0, name=f"Field{j}")
    d0 = build_dropship(P, root, (22.0, -14.0, 0.16), name="DropShipA", cargo="rover")
    d0.rotation_euler = (0, 0, math.radians(35))
    d1 = build_dropship(P, root, (30.0, -24.0, 0.16), name="DropShipB", cargo="none")
    d1.rotation_euler = (0, 0, math.radians(35))
    # Feed lines.
    for name, pts in (("FeedEast", [(21.0, 7.5), (15.0, 6.5), (8.6, 5.5)]), ("FeedWest", [(-22.0, 5.0), (-15.0, 4.0), (-8.6, 3.5)])):
        p3 = [Vector((x, y, 0.5)) for x, y in pts]
        tube(name, p3, 0.32, P["gun"], root)
        tube(f"{name}Glow", [p + Vector((0, 0, 0.33)) for p in p3], 0.09, P["white"], root)
    # Ring of guard spires.
    for j in range(10):
        a = TAU * j / 10 + 0.12
        thorn(f"GuardSpire{j}", P, root, (37.0 * math.cos(a), 37.0 * math.sin(a) * 0.9, 0), 9.0, 0.75, bulges=3)
    R = 25.0
    hex_guides([(0, 0)] + [(R * math.cos(math.radians(60 * j)), R * math.sin(math.radians(60 * j))) for j in range(6)], P, root, line=0.35)


# ---------------------------------------------------------------- Federation reference


def build_fed(parent=None, loc=(0, 0, 0)):
    """The Federation Mk I Scout from build_vehicles.py (tier 1), plain materials (no paint bake)."""
    root = NS["survey_rover"]()
    root.name = "FederationMkI"
    root.location = loc
    if parent is not None:
        root.parent = parent
    for m in bpy.data.materials:
        if m.name == "Owner":
            m.name = "OwnerFed"
    return root


# ---------------------------------------------------------------- preview

FRAMES = {  # model: (camera scale k, target z[, game camera scale])
    "rover": (1.0, 1.3), "fed": (1.0, 1.3), "miner": (1.3, 1.7), "dropship": (2.25, 3.1),
    "outpost1": (2.3, 2.8, 2.4), "outpost5": (4.2, 5.5, 2.6), "outpost10": (10.5, 13.0, 6.5),
}
SHOT_CAMS = {
    "front34": (Vector((7.5, 8.5, 3.2)), 40),
    "rear34": (Vector((-7.5, -8.0, 3.6)), 40),
    "side": (Vector((10.5, 0.4, 1.9)), 38),
    "game": (Vector((0.0, 13.0, 15.5)), 30),
}


def preview(folder, stem, frame, ortho=None):
    """build_vehicles.preview_scene's look (sky, sun, sand, AgX, exposure) with a bigger ground and cameras scaled by k."""
    k, tz = frame[:2]
    kg = frame[2] if len(frame) > 2 else k
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = THREADS
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
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
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=max(200.0, 150.0 * k))
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    cam.data.clip_end = 2000
    target = Vector((0, 0, tz))
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        pos, lens = SHOT_CAMS[name]
        pos = pos * (kg if name == "game" else k)
        if ortho:
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = ortho[name] if isinstance(ortho, dict) else ortho
            pos = target + (pos - target).normalized() * 80
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{stem}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def export(root, path):
    NS["export"](root, path)


def main():
    NS["reset"]()
    if MODEL == "fed":
        root = build_fed()
        stem, frame = "fed-mk1", FRAMES["fed"]
    else:
        P = dom_palette()
        if MODEL == "rover":
            root, stem, frame = build_rover(P), "rover", FRAMES["rover"]
        elif MODEL == "miner":
            root = build_miner(P, pose=POSE)
            stem, frame = "miner" if POSE == "deployed" else f"miner-{POSE}", FRAMES["miner"]
        elif MODEL == "dropship":
            root = build_dropship(P, cargo=CARGO)
            stem, frame = "dropship" if CARGO == "rover" else f"dropship-{CARGO}", FRAMES["dropship"]
        elif MODEL == "outpost":
            root = build_outpost(P, LEVEL)
            stem, frame = f"outpost-l{LEVEL}", FRAMES[f"outpost{LEVEL}"]
        else:  # lineup: Fed first (its Owner material is renamed so the Dominion "Owner" stays gold)
            # Spread along the front34 camera's screen axis so nothing hides anything else.
            u = Vector((0.75, -0.66, 0))
            root = empty("Lineup")
            build_fed(root, u * 14.5)
            P = dom_palette()
            build_rover(P, root, u * 8.7)
            build_miner(P, root, u * 2.2, pose="stowed")
            build_dropship(P, root, u * -9.4, cargo="rover")
            if PREVIEW:
                preview(PREVIEW, "lineup", (2.2, 3.0), ortho=36.0)
            if OUT:
                export(root, OUT)
            return
    if "--bounds" in argv:
        print_bounds(root)
    if OUT:
        export(root, OUT)
    if PREVIEW:
        preview(PREVIEW, stem, frame)


def print_bounds(root):
    """Overall size of the model's meshes (hex guides and the scorch decal left out), for checking scale."""
    bpy.context.view_layer.update()
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in [root] + list(root.children_recursive):
        if o.type != "MESH" or o.name.startswith(("Hex", "Scorch")) or o.hide_render:
            continue
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    d = hi - lo
    print(f"bounds x {lo.x:.2f}..{hi.x:.2f} y {lo.y:.2f}..{hi.y:.2f} z {lo.z:.2f}..{hi.z:.2f}  ->  {d.y:.2f} long x {d.x:.2f} wide x {d.z:.2f} tall")


main()
