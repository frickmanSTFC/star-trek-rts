"""Romulan ground kit, Blender mockups: survey rover, mining walker, talon drop ship and the colony core at three levels.

Look (blender/factions/romulan.md): deep green and pale jade hulls over dark grey, sweeping crescent "wing" shapes that
mantle down over each machine like a hawk over its prey, feather-row panelling (chevron seams on the backs, feather
blades along trailing edges), cyan-green glows, and hover pads instead of wheels. All shapes are original designs in
that general style.

Run (bpy 5.x module):
  /root/bpyenv/bin/python blender/factions/build_romulan.py --model rover --preview blender/previews/factions/romulan
  ... --model miner [--pose deployed|stowed]
  ... --model dropship
  ... --model outpost --level 1|5|10
  --res 480 --samples 8      quick look (finals: --res 800 --samples 24)
  --shots front34,game        any of front34, rear34, side, game (default front34,game)
  --blank                     decal spots as empties (NameL/NameR/NumberL/NumberR) instead of text, as for the game
  --out file.glb              also export the model
  python3 blender/factions/build_romulan.py --sheet blender/previews/factions/romulan    (PIL; no bpy)

Previews: <preview>/romulan-<model>[-l<level>]-<shot>.png. The rover and the miner use build_vehicles' preview cameras
unchanged (so they compare 1:1 with the Federation rover renders); bigger models scale the same camera rig up.

Conventions as in blender/build_vehicles.py (whose helpers this file loads without running its main()): +Y = front,
+Z up, units = world units (one hex is 25 across the flats), material "Owner" = player colour, "Dish" spins, "Beacon"
flashes, "TailLamp" lamps, decal markers NameL/NameR (L = -X side) and NumberL/NumberR, miner parts Leg0-3, Boom,
Drill, String, Bit, Fill, Dust.
"""
import math
import os
import re
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(HERE)

# ---------------------------------------------------------------- contact sheet (PIL only, no bpy)

SHEET_CELLS = [
    ("fed-mk1-front34.png", "Federation Mk I Scout (for scale)"),
    ("romulan-rover-front34.png", "Survey skimmer"),
    ("romulan-miner-front34.png", "Mining walker (boom deployed)"),
    ("romulan-dropship-front34.png", "Talon drop ship (carrying the skimmer)"),
    ("romulan-outpost-l1-front34.png", "Colony core L1: Colonial Garrison, just landed"),
    ("romulan-outpost-l5-front34.png", "Colony core L5: Proconsulate"),
    ("romulan-outpost-l10-front34.png", "Colony core L10: Imperial capital"),
    ("romulan-rover-game.png", "Survey skimmer, game camera"),
]


def make_sheet(folder):
    from PIL import Image, ImageDraw, ImageFont
    cols, cw, ch, bar = 4, 600, 400, 44
    rows = (len(SHEET_CELLS) + cols - 1) // cols
    head = 70
    sheet = Image.new("RGB", (cols * cw, head + rows * (ch + bar)), (10, 16, 13))
    draw = ImageDraw.Draw(sheet)

    def font(size, bold=False):
        for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",):
            if os.path.exists(path):
                return ImageFont.truetype(path, size)
        return ImageFont.load_default()

    draw.text((20, 14), "ROMULAN GROUND KIT  -  mockups", fill=(111, 232, 160), font=font(30, True))
    draw.text((cols * cw - 20, 24), "rover, miner, Fed Mk I: same camera and scale; others: same rig scaled up", fill=(147, 166, 155),
              font=font(16), anchor="ra")
    for k, (name, label) in enumerate(SHEET_CELLS):
        x, y = (k % cols) * cw, head + (k // cols) * (ch + bar)
        path = os.path.join(folder, name)
        if os.path.exists(path):
            im = Image.open(path).convert("RGB")
            im.thumbnail((cw - 8, ch - 8))
            sheet.paste(im, (x + (cw - im.width) // 2, y + 4))
        else:
            draw.rectangle((x + 4, y + 4, x + cw - 4, y + ch - 4), outline=(60, 80, 70))
            draw.text((x + cw // 2, y + ch // 2), f"missing {name}", fill=(200, 90, 90), font=font(16), anchor="mm")
        draw.rectangle((x + 4, y + ch, x + cw - 4, y + ch + bar - 6), fill=(19, 32, 28))
        draw.text((x + 14, y + ch + (bar - 6) // 2), label, fill=(184, 240, 204) if k else (243, 203, 91), font=font(19), anchor="lm")
    out = os.path.join(folder, "romulan-sheet.png")
    sheet.save(out)
    print("wrote", out)


if "--sheet" in argv:
    make_sheet(arg("--sheet"))
    sys.exit(0)

# ---------------------------------------------------------------- Blender side

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

MODEL = arg("--model", "rover")
LEVEL = int(arg("--level", "1"))
PREVIEW = arg("--preview")
OUT = arg("--out")
RES = int(arg("--res", "480"))
SAMPLES = int(arg("--samples", "8"))
SHOTS = [s.strip() for s in arg("--shots", "front34,game").split(",") if s.strip()]
POSE = arg("--pose", "deployed")
BLANK = "--blank" in argv
if MODEL not in ("rover", "miner", "dropship", "outpost"):
    sys.exit(f"--model takes rover, miner, dropship or outpost, not {MODEL}")
if MODEL == "outpost" and LEVEL not in (1, 5, 10):
    sys.exit(f"--level takes 1, 5 or 10, not {LEVEL}")


def load_build_vehicles():
    """Exec build_vehicles.py into a namespace without its trailing main() call (as render_finish.py does). Its own
    argument parsing sees an empty command line, so it keeps its defaults."""
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
BV["BLANK"] = BLANK  # text() reads it: decals become NameL/NameR/NumberL/NumberR empties
material, link, empty, mesh_object = BV["material"], BV["link"], BV["empty"], BV["mesh_object"]
box, cylinder, strut, text = BV["box"], BV["cylinder"], BV["strut"], BV["text"]

# ---------------------------------------------------------------- materials


def palette():
    return {
        "hull": material("RomHull", (0.02, 0.08, 0.045), 0.45, 0.34, coat=0.2),     # deep green
        "jade": material("RomJade", (0.3, 0.46, 0.37), 0.25, 0.38, coat=0.2),       # pale jade
        "seam": material("RomSeam", (0.012, 0.02, 0.016), 0.3, 0.6),                  # feather seams
        "dark": material("RomDark", (0.055, 0.06, 0.062), 0.55, 0.48),               # dark grey
        "grey": material("RomGrey", (0.2, 0.22, 0.22), 0.7, 0.38),                    # struts, joints
        "glass": material("RomGlass", (0.01, 0.035, 0.028), 0.3, 0.05, emission=(0.1, 0.55, 0.38), strength=0.25, coat=1.0),
        "glow": material("RomGlow", (0.3, 1.0, 0.72), 0.0, 0.3, emission=(0.25, 1.0, 0.7), strength=7.0),
        "glowsoft": material("RomGlowSoft", (0.2, 0.7, 0.5), 0.0, 0.3, emission=(0.2, 0.95, 0.65), strength=1.6),
        "owner": material("Owner", (0.78, 0.55, 0.12), 0.45, 0.32),                   # player colour (preview: gold)
        "number": material("Number", (0.62, 0.86, 0.72), 0.0, 0.45),
        "lamp": material("Lamp", (0.75, 1.0, 0.92), 0.0, 0.2, emission=(0.7, 1.0, 0.9), strength=6.0),
        "beacon": material("BeaconGlow", (0.3, 1.0, 0.75), 0.0, 0.25, emission=(0.3, 1.0, 0.75), strength=9.0),
        "tail": material("TailLamp", (0.1, 0.9, 0.5), 0.0, 0.3, emission=(0.1, 1.0, 0.5), strength=3.0),
        "stone": material("RomStone", (0.42, 0.45, 0.42), 0.0, 0.7),
        "stonedark": material("RomStoneDark", (0.18, 0.2, 0.19), 0.0, 0.75),
        "water": material("RomPool", (0.01, 0.03, 0.028), 0.0, 0.04, coat=1.0),
        "scorch": material("Scorch", (0.035, 0.028, 0.022), 0.0, 1.0),
        "hexline": material("HexLine", (0.75, 0.9, 0.8), 0.0, 0.5, emission=(0.4, 0.9, 0.6), strength=0.6),
    }


# Face material indices for the feathered meshes (loft/wing/tower): mats list order.
HULL, JADE, SEAM, DARK, OWNER, GLOW = range(6)


def mats6(P):
    return [P["hull"], P["jade"], P["seam"], P["dark"], P["owner"], P["glowsoft"]]


# ---------------------------------------------------------------- shape helpers


def pchip(table, t):
    """Smooth monotone interpolation through [(t, value), ...] (no overshoot between the knots)."""
    pts = sorted(table)
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    n = len(xs)
    if t <= xs[0]:
        return ys[0]
    if t >= xs[-1]:
        return ys[-1]
    d = [(ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]) for i in range(n - 1)]
    m = [d[0]] + [0.0 if d[i - 1] * d[i] <= 0 else 2.0 / (1.0 / d[i - 1] + 1.0 / d[i]) for i in range(1, n - 1)] + [d[-1]]
    i = max(k for k in range(n - 1) if xs[k] <= t)
    h = xs[i + 1] - xs[i]
    u = (t - xs[i]) / h
    return ((2 * u ** 3 - 3 * u ** 2 + 1) * ys[i] + (u ** 3 - 2 * u ** 2 + u) * h * m[i] + (-2 * u ** 3 + 3 * u ** 2) * ys[i + 1]
            + (u ** 3 - u ** 2) * h * m[i + 1])


def spow(v, e):
    return math.copysign(abs(v) ** e, v)


def se_loop(y, w, zb, zt, n=36, p_top=2.0, p_bot=2.0, x0=0.0):
    """Cross-section in the XZ plane at y: superellipse half-width w between zb and zt; p < 2 sharpens (dorsal ridge),
    p > 2 flattens (belly). Starts at +X mid height and runs over the top."""
    zc, h = (zb + zt) / 2, (zt - zb) / 2
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        c, s = math.cos(a), math.sin(a)
        p = p_top if s >= 0 else p_bot
        pts.append((x0 + w * spow(c, 2 / p), y, zc + h * spow(s, 2 / p)))
    return pts


def se_surface_x(y, z, W, ZB, ZT, p_top, p_bot):
    """Half-width of a lofted body (tables W/ZB/ZT) at height z, section y: where a side decal sits."""
    w, zb, zt = pchip(W, y), pchip(ZB, y), pchip(ZT, y)
    zc, h = (zb + zt) / 2, (zt - zb) / 2
    s = max(-1.0, min(1.0, (z - zc) / h))
    p = p_top if s >= 0 else p_bot
    return w * max(0.0, 1 - abs(s) ** p) ** (1 / p)


def loft(name, sections, mats, parent=None, paint=None, caps=(True, True), smooth=True, sharp=40):
    """Skin a list of equal-length point loops. paint(i, j, m, n, centre) -> material index for the quad between
    sections i, i+1 and loop points j, j+1 (m sections, n points per loop)."""
    bm = bmesh.new()
    rows = [[bm.verts.new(p) for p in sec] for sec in sections]
    m, n = len(sections), len(sections[0])
    for i in range(m - 1):
        for j in range(n):
            k = (j + 1) % n
            f = bm.faces.new((rows[i][j], rows[i][k], rows[i + 1][k], rows[i + 1][j]))
            if paint:
                f.material_index = paint(i, j, m, n, f.calc_center_median())
    for end, row in ((0, rows[0]), (1, rows[-1])):
        if caps[end]:
            f = bm.faces.new(row if end else list(reversed(row)))
            f.material_index = DARK if paint else 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for mt in mats:
        me.materials.append(mt)
    if smooth:
        for poly in me.polygons:
            poly.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(sharp))
    o = bpy.data.objects.new(name, me)
    return link(o, parent)


def slab(name, pts, thick, mat, parent=None):
    """A flat plate: polygon pts (3D, roughly planar) given thickness along the vector `thick` (centred)."""
    bm = bmesh.new()
    t = Vector(thick) / 2
    a = [bm.verts.new(Vector(p) + t) for p in pts]
    b = [bm.verts.new(Vector(p) - t) for p in pts]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm, mat, parent)


def ball(name, r, loc, mat, parent=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=r)
    return mesh_object(name, bm, mat, parent, loc, smooth=True)


def spike(name, a, b, r, mat, parent=None, tip=0.15, segments=12):
    """A tapered bar from a (radius r) to b (radius r * tip): talons, toes, antennae."""
    a, b = Vector(a), Vector(b)
    o = cylinder(name, r, (b - a).length, (a + b) / 2, mat, parent, axis="Z", segments=segments, radius2=r * tip)
    o.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    return o


def airfoil(c):
    return 2.6 * math.sqrt(max(c, 0.0)) * (1 - c)


def spine_frame(spine, s, flip=False):
    e = 1e-3
    x1, z1 = spine(max(0.0, s - e))
    x2, z2 = spine(min(1.0, s + e))
    tx, tz = x2 - x1, z2 - z1
    ln = math.hypot(tx, tz)
    tx, tz = tx / ln, tz / ln
    nx, nz = -tz, tx
    if flip:
        nx, nz = -nx, -nz
    return spine(s), (tx, tz), (nx, nz)


def wing(name, side, spine, le, chord, thick, mats, paint=None, parent=None, n=44, res=14, bottom=0.55, caps=(True, True),
         flip=False):
    """A wing (or roof, arch, armour vane) lofted along a curved spine in the XZ plane. spine(s) -> (x, z) for s 0..1
    on the +X side (side = -1 mirrors it), le(s) = leading-edge y, chord(s), thick(s). Sections stand perpendicular to
    the spine, so a wing can droop until it stands on the ground. The 'top' (feathered) skin faces the spine normal
    (rotated +90 deg from the tangent; flip turns it the other way). paint(s, c, top) -> material index."""
    secs = []
    for i in range(n):
        s = i / (n - 1)
        (x, z), _, (nx, nz) = spine_frame(spine, s, flip)
        y0, ch, th = le(s), chord(s), thick(s)
        loop = []
        for k in range(res + 1):
            c = k / res
            h = airfoil(c) * th / 2
            loop.append((side * (x + nx * h), y0 - c * ch, z + nz * h))
        for k in range(res - 1, 0, -1):
            c = k / res
            h = airfoil(c) * th / 2 * bottom
            loop.append((side * (x - nx * h), y0 - c * ch, z - nz * h))
        secs.append(loop)

    def p(i, j, m, nn, centre):
        if paint is None:
            return 0
        s = (i + 0.5) / (m - 1)
        top = j < res
        c = (j + 0.5) / res if top else 1 - (j - res + 0.5) / res
        return paint(s, c, top)
    return loft(name, secs, mats, parent, p, caps=caps)


def feather_paint(n=44, res=14, rows=5, feathers=3, lead=0.09, jade_from=0.7, owner=True, under=DARK):
    """Feather rows on a wing's top skin: rows across the span split by seams, each row cut into staggered
    chevron-tipped feathers, the trailing coverts in jade; an Owner band along the leading edge."""
    def p(s, c, top):
        if c < lead:
            return OWNER if owner else JADE
        if not top:
            return under
        if c > jade_from + 0.03:
            return JADE  # the trailing coverts: one clean jade band ahead of the feather blades
        r = s * rows
        row = int(r)
        t = r - row
        if t < rows / (n - 1) * 0.999:
            return SEAM
        q = (c - lead) / (1 - lead) * feathers + 0.5 * (row % 2) + 0.4 * abs(t - 0.5)
        if q - math.floor(q) < feathers / res * 1.05:
            return SEAM
        return HULL
    return p


def body_paint(period=0.45, slant=0.8, dy=0.1, waist=(-0.12, 0.06)):
    """Lofted bodies: chevron feather seams over the back (pointing forward), a jade waist line, dark grey belly."""
    def p(i, j, m, n, centre):
        s = math.sin(2 * math.pi * (j + 0.5) / n)
        if s < waist[0]:
            return DARK
        if s < waist[1]:
            return JADE
        v = (centre.y + slant * abs(centre.x)) / period
        if v - math.floor(v) < dy / period * 1.05:
            return SEAM
        return HULL
    return p


def feather_tips(name, side, spine, le, chord, s_list, length, width, mats, parent=None, inset=0.12, thick=0.025, flip=False):
    """Feather blades fanning back from a wing's trailing edge at the given span stations (alternating materials)."""
    out = []
    for k, s in enumerate(s_list):
        (x, z), (tx, tz), (nx, nz) = spine_frame(spine, s, flip)
        y_te = le(s) - chord(s)
        leaf = [(-0.5, inset), (0.5, inset), (0.34, -0.55), (0.06, -1.0), (-0.3, -0.62)]
        pts = []
        for a, b in leaf:
            yy = y_te + (b * length if b < 0 else b)
            sp = a * width * (1 - 0.2 * max(0, -b))
            pts.append((side * (x + tx * sp), yy, z + tz * sp))
        out.append(slab(f"{name}{k}", pts, (side * nx * thick, 0, nz * thick), mats[k % len(mats)], parent))
    return out


def hall(name, length, w, zb, zt, y0, P, parent, x0=0.0, rot_z=0.0, n=28, windows=True, point=0.55):
    """A pill-shaped building module along y (centred on y0): dark plinth band, jade walls with a band of glowing
    window slots, green crown with chevron seams."""
    L = length / 2
    W = [(-L, w * point), (-L + 0.5, w * (0.5 + 0.38 * point / 0.55)), (-L + 1.4, w), (L - 1.4, w), (L - 0.5, w * (0.5 + 0.38 * point / 0.55)),
         (L, w * point)]
    ZT = [(-L, zt - (zt - zb) * 0.3), (-L + 1.0, zt), (L - 1.0, zt), (L, zt - (zt - zb) * 0.3)]
    secs = [se_loop(y, pchip(W, y), zb, pchip(ZT, y), n=40, p_top=2.4, p_bot=8.0) for y in [(-L + 2 * L * i / (n - 1)) for i in range(n)]]
    seg = max(0.6, length / 14)

    def p(i, j, m, nn, c):
        s = math.sin(2 * math.pi * (j + 0.5) / nn)
        if s < -0.45:
            return DARK
        if windows and 0.08 < s < 0.2:
            return GLOW if ((c.y + L) / seg) % 1.0 > 0.3 else SEAM
        if s < 0.42:
            return JADE
        v = (c.y + 0.8 * abs(c.x)) / (seg * 1.2)
        return SEAM if v - math.floor(v) < 0.12 else HULL
    root = empty(name, (x0, y0, 0), parent)
    root.rotation_euler = (0, 0, rot_z)
    loft(f"{name}Body", secs, mats6(P), root, p)
    return root


def tower(name, h, w0, w1, t0, t1, P, parent=None, loc=(0, 0, 0), n=40, m=28, p=1.2, lean=0.0, band=8, rot_z=0.0):
    """A feather-blade spire: diamond-ish lens sections (sharp edges along +-X) tapering from (w0, t0) at the base to
    (w1, t1) at height h, leaning back by lean * h at the top. Jade edges, green faces, seams and glowing slits in
    bands of `band` sections."""
    secs = []
    for i in range(n):
        f = i / (n - 1)
        w = w0 + (w1 - w0) * f ** 0.7
        t = t0 + (t1 - t0) * f ** 0.7
        yo = -lean * h * f * f
        secs.append([(w * spow(math.cos(2 * math.pi * k / m), 2 / p), yo + t * spow(math.sin(2 * math.pi * k / m), 2 / p), f * h)
                     for k in range(m)])

    def paint(i, j, mm, nn, c):
        cc = abs(math.cos(2 * math.pi * (j + 0.5) / nn))
        if cc > 0.93:
            return JADE
        if i % band == 0:
            return SEAM
        if i % band == band // 2 and cc < 0.6:
            return GLOW
        return HULL
    root = empty(name, loc, parent)
    root.rotation_euler = (0, 0, rot_z)
    loft(f"{name}Blade", secs, mats6(P), root, paint, sharp=30)
    return root


def sensor_head(P, parent, loc, scale=1.0, name="Dish"):
    """The spinning sensor head ("Dish"): a short mast, a jade lens disc with a glowing rim and a swept crest blade."""
    d = empty(name, loc, parent)
    d.scale = (scale, scale, scale)
    cylinder(f"{name}Mast", 0.045, 0.26, (0, 0, 0.13), P["grey"], d, axis="Z", segments=12)
    cylinder(f"{name}Lens", 0.27, 0.07, (0, 0, 0.27), P["jade"], d, axis="Z", segments=32, radius2=0.17)
    cylinder(f"{name}Rim", 0.25, 0.025, (0, 0, 0.245), P["glowsoft"], d, axis="Z", segments=32)
    slab(f"{name}Crest", [(0, 0.2, 0.29), (0, -0.24, 0.29), (0, -0.34, 0.5), (0, -0.06, 0.39)], (0.03, 0, 0), P["hull"], d)
    for s in (-1, 1):
        slab(f"{name}Vane{s}", [(s * 0.05, 0.08, 0.3), (s * 0.05, -0.16, 0.3), (s * 0.24, -0.24, 0.4), (s * 0.18, -0.02, 0.34)],
             (0, 0, 0.02), P["dark"], d)
    return d


def side_text(name, body, size, y, z, x, s, mat, parent, max_width, yaw=0.0):
    """Text standing on the side plane x (facing s = +-1), reading front-to-back on +X like build_vehicles' rovers."""
    rot = Matrix.Rotation(yaw, 4, "Z") @ Euler((math.radians(90), 0, math.radians(90 if s > 0 else -90))).to_matrix().to_4x4()
    return text(name, body, size, None, None, mat, parent, max_width=max_width, matrix=Matrix.Translation((x, y, z)) @ rot)


def hex_outline(P, parent, centre=(0, 0), width=0.18, z=0.03, name="Hex"):
    """A hex tile outline on the ground (25 across the flats, corners on +-Y like the game's pointy-top grid)."""
    R = 25 / math.sqrt(3)
    cx, cy = centre
    corners = [(cx + R * math.sin(math.radians(60 * k)), cy + R * math.cos(math.radians(60 * k))) for k in range(6)]
    for k in range(6):
        a, b = Vector((*corners[k], z)), Vector((*corners[(k + 1) % 6], z))
        o = box(f"{name}{k}", ((b - a).length + width, width, 0.02), (a + b) / 2, P["hexline"], parent, 0)
        o.rotation_euler = (0, 0, math.atan2(b.y - a.y, b.x - a.x))
        o.visible_shadow = False


def hex_prism(name, R, z0, z1, mat, parent=None, loc=(0, 0)):
    pts = [(loc[0] + R * math.sin(math.radians(60 * k)), loc[1] + R * math.cos(math.radians(60 * k)), (z0 + z1) / 2) for k in range(6)]
    o = slab(name, pts, (0, 0, z1 - z0), mat, parent)
    return o


# ---------------------------------------------------------------- survey skimmer

ROVER_W = [(2.25, 0.03), (2.0, 0.16), (1.5, 0.36), (0.7, 0.52), (-0.4, 0.56), (-1.4, 0.48), (-2.0, 0.3)]
ROVER_ZT = [(2.25, 0.82), (2.0, 1.0), (1.5, 1.28), (0.7, 1.62), (0.0, 1.72), (-1.0, 1.66), (-2.0, 1.42)]
ROVER_ZB = [(2.25, 0.74), (2.0, 0.64), (1.5, 0.55), (0.7, 0.5), (-1.4, 0.52), (-2.0, 0.7)]


def rover(P, parent=None, name="VASHEN", number="27"):
    """Survey skimmer, the Romulan counterpart of the Federation survey rover: a hooked-beak body with a jade-tinted
    canopy, two feathered wings mantling down onto long claw-nosed hover pods (glowing emitters underneath, no
    wheels), feather blades fanning back past the tail, the spinning crest sensor ("Dish") on the back and a green
    beacon. Name and number ride on the pods' flat outer flanks."""
    root = empty("SurveyRover", (0, 0, 0), parent)
    M = mats6(P)
    ns = 110
    ys = [-2.0 + 4.25 * i / (ns - 1) for i in range(ns)]
    loft("Hull", [se_loop(y, pchip(ROVER_W, y), pchip(ROVER_ZB, y), pchip(ROVER_ZT, y), 48, 1.7, 3.0) for y in ys], M, root,
         body_paint(0.55, 0.9, 4.25 / (ns - 1)))
    # Canopy: a glass lens set into the front slope.
    CW = [(1.95, 0.03), (1.7, 0.19), (1.3, 0.29), (0.95, 0.26), (0.75, 0.03)]
    cys = [0.75 + 1.2 * i / 23 for i in range(24)]
    loft("Canopy", [se_loop(y, pchip(CW, y), pchip(ROVER_ZT, y) - 0.28, pchip(ROVER_ZT, y) + 0.05 * math.sin(math.pi * (y - 0.75) / 1.2),
                            28, 2.2, 2.0) for y in cys], [P["glass"]], root)
    # Wings: from the upper back, a short rise, then a long droop onto the pods; swept back, feathers past the tail.
    spine = lambda s: (0.25 + 0.85 * s, 1.45 + 2.4 * s - 3.35 * s * s)  # noqa: E731
    le = lambda s: 0.55 - 1.25 * s ** 1.3  # noqa: E731
    chord = lambda s: 1.6 - 0.55 * s  # noqa: E731
    thick = lambda s: 0.12 - 0.04 * s  # noqa: E731
    wn, wr = 80, 24
    for s in (-1, 1):
        wing(f"Wing{s}", s, spine, le, chord, thick, M, feather_paint(n=wn, res=wr, rows=4, feathers=3, lead=0.05), root, n=wn, res=wr)
        feather_tips(f"WingFeather{s}", s, spine, le, chord, [0.1, 0.28, 0.46, 0.64, 0.82], 0.6, 0.24, [P["jade"], P["hull"]], root)
        # Hover pod: long, flat-flanked nacelle with a claw nose; emitter strip underneath; lamp in the nose.
        x0 = s * 1.05
        PW = [(1.6, 0.03), (1.3, 0.12), (0.9, 0.19), (-1.35, 0.19), (-1.6, 0.13), (-1.7, 0.05)]
        PB = [(1.6, 0.4), (1.2, 0.25), (0.9, 0.22), (-1.4, 0.22), (-1.7, 0.34)]
        PT = [(1.6, 0.46), (1.2, 0.56), (0.9, 0.62), (-1.4, 0.62), (-1.7, 0.5)]
        loft(f"Pod{s}", [se_loop(y, pchip(PW, y), pchip(PB, y), pchip(PT, y), 32, 3.2, 3.2, x0) for y in [1.6 - 3.3 * i / 39 for i in range(40)]],
             M, root, body_paint(0.5, 0.0, 0.0, waist=(-0.35, 0.5)))
        box(f"PodEmitter{s}", (0.16, 2.1, 0.03), (x0, -0.25, 0.215), P["glow"], root, 0)
        box(f"PodLamp{s}", (0.1, 0.06, 0.05), (x0, 1.42, 0.45), P["lamp"], root, 0.01)
        box(f"PodGlowLine{s}", (0.012, 2.0, 0.022), (s * 1.243, -0.2, 0.555), P["glowsoft"], root, 0)
        spike(f"PodClaw{s}", (x0, 1.35, 0.3), (x0, 1.62, 0.12), 0.05, P["seam"], root, tip=0.2)
        # Decals on the pod's flat outer flank.
        side_text(f"Name{s}", name, 0.15, 0.3, 0.4, s * 1.247, s, P["number"], root, 0.9)
        side_text(f"Number{s}", number, 0.22, -0.85, 0.4, s * 1.247, s, P["number"], root, 0.45)
        # Beak "eyes" (headlamps) and tail lamps.
        box(f"Headlamp{s}", (0.04, 0.24, 0.045), (s * 0.24, 1.8, 0.84), P["lamp"], root, 0.01, rot=(0, 0, -s * 0.5))
        box(f"Tail{s}", (0.12, 0.04, 0.05), (s * 0.15, -1.97, 1.05), P["tail"], root, 0.01)
    box("BellyEmitter", (0.3, 2.2, 0.03), (0, -0.3, 0.5), P["glow"], root, 0)
    # Tail fan: three feather blades splaying back from the rump.
    for k, a in enumerate((-0.42, 0.0, 0.42)):
        d = Vector((math.sin(a), -math.cos(a), 0.14))
        base = Vector((0, -1.8, 1.25))
        side = Vector((math.cos(a), math.sin(a), 0)) * 0.1
        tip = base + d * 0.5
        slab(f"TailFeather{k}", [base + side, base - side, tip - side * 0.4, tip + d * 0.1, tip + side * 0.5], (0, 0, 0.025),
             P["jade"] if k == 1 else P["hull"], root)
    sensor_head(P, root, (0, -0.55, 1.69), 0.85)
    cylinder("Beacon", 0.06, 0.1, (0, -1.6, 1.53), P["beacon"], root, axis="Z", segments=14)
    return root


# ---------------------------------------------------------------- mining walker

MINER_W = [(2.35, 0.18), (2.0, 0.5), (1.3, 0.85), (0.0, 0.95), (-1.2, 0.92), (-2.1, 0.72), (-2.55, 0.4)]
MINER_ZT = [(2.35, 1.9), (2.0, 2.2), (1.3, 2.55), (0.0, 2.72), (-1.2, 2.7), (-2.1, 2.5), (-2.55, 2.2)]
MINER_ZB = [(2.35, 1.62), (2.0, 1.5), (1.3, 1.42), (0.0, 1.4), (-1.2, 1.42), (-2.1, 1.55), (-2.55, 1.75)]


def miner(P, name="DHEREK", number="41"):
    """Mining walker: a carapace body with folded feathered wing-armour down its flanks, four reverse-knee legs ending
    in three-toed talons (Leg0-3, pivots at the hips), an ore hopper with a glowing fill window ("Fill") and a drill
    boom ("Boom") hinged in the beak that stoops forward like a striking raptor; "Drill" holds the drill head, the
    "String" and the "Bit"; "Dust" marks the hole. --pose stowed raises the boom."""
    root = empty("MiningCrawler")
    M = mats6(P)
    ns = 120
    ys = [-2.55 + 4.9 * i / (ns - 1) for i in range(ns)]
    loft("Hull", [se_loop(y, pchip(MINER_W, y), pchip(MINER_ZB, y), pchip(MINER_ZT, y), 56, 1.8, 2.6) for y in ys], M, root,
         body_paint(0.6, 0.9, 4.9 / (ns - 1)))
    spine = lambda s: (0.35 + 0.85 * s, 2.45 + 0.2 * s - 1.05 * s * s)  # noqa: E731
    le = lambda s: 1.2 - 0.3 * s  # noqa: E731
    chord = lambda s: 3.0 - 0.4 * s  # noqa: E731
    thick = lambda s: 0.16  # noqa: E731
    for s in (-1, 1):
        wing(f"WingArmour{s}", s, spine, le, chord, thick, M, feather_paint(n=90, res=30, rows=4, feathers=4, jade_from=0.75, lead=0.05), root,
             n=90, res=30)
        feather_tips(f"ArmourFeather{s}", s, spine, le, chord, [0.3, 0.5, 0.7, 0.88], 0.42, 0.3, [P["jade"], P["hull"]], root)
        box(f"Headlamp{s}", (0.05, 0.26, 0.06), (s * 0.43, 2.02, 1.86), P["lamp"], root, 0.01, rot=(0, 0, -s * 0.55))
        box(f"Tail{s}", (0.14, 0.04, 0.06), (s * 0.22, -2.5, 2.02), P["tail"], root, 0.01)
    # Ore hopper on the back; "Fill" is its glowing window (the game can scale it with the load).
    HW = [(-0.2, 0.2), (-0.45, 0.5), (-0.7, 0.55), (-1.9, 0.55), (-2.15, 0.44), (-2.3, 0.2)]
    HT = [(-0.2, 2.75), (-0.5, 3.02), (-1.9, 3.04), (-2.3, 2.8)]

    def hopper_paint(i, j, m, n, c):
        sn = math.sin(2 * math.pi * (j + 0.5) / n)
        if -0.52 < c.y < -0.4 and sn > -0.3:
            return OWNER
        return JADE if sn > 0.86 else DARK
    loft("Hopper", [se_loop(y, pchip(HW, y), 2.35, pchip(HT, y), 40, 5.0, 5.0) for y in [-0.2 - 2.1 * i / 39 for i in range(40)]], M, root,
         hopper_paint)
    box("Fill", (0.78, 1.3, 0.05), (0, -1.25, 3.04), P["glowsoft"], root, 0.01)
    for k in range(4):
        box(f"HopperRib{k}", (0.84, 0.04, 0.04), (0, -0.72 - k * 0.35, 3.06), P["grey"], root, 0)
    for s in (-1, 1):
        side_text(f"Name{s}", name, 0.16, -0.85, 2.7, s * 0.556, s, P["number"], root, 0.85)
        side_text(f"Number{s}", number, 0.24, -1.78, 2.7, s * 0.556, s, P["number"], root, 0.42)
    cylinder("Beacon", 0.06, 0.1, (0.38, -2.05, 3.1), P["beacon"], root, axis="Z", segments=14)

    # Legs: hip -> backward-pointing ankle -> foot with three forward talons and a back spur.
    for k, (s, yh) in enumerate(((-1, 1.35), (1, 1.35), (-1, -1.45), (1, -1.45))):
        hip = Vector((s * 0.85, yh, 1.6))
        leg = empty(f"Leg{k}", hip, root)
        ankle = Vector((s * 1.2, yh - 0.42, 0.85)) - hip
        foot = Vector((s * 1.16, yh + 0.05, 0.15)) - hip
        ball(f"Leg{k}Hip", 0.17, (0, 0, 0), P["grey"], leg)
        spike(f"Leg{k}Thigh", (0, 0, 0), ankle, 0.15, P["jade"], leg, tip=0.6)
        # Feather-blade armour along the thigh's outer face.
        out = Vector((s * 0.1, 0, 0.04))
        mid = ankle * 0.5
        slab(f"Leg{k}Guard", [out + Vector((0, 0.16, 0.12)), out + Vector((0, -0.2, 0.1)), out + mid + Vector((0, -0.2, 0)),
                              out + ankle * 0.92 + Vector((0, -0.08, 0)), out + mid + Vector((0, 0.1, 0.02))], (s * 0.03, 0, 0), P["hull"], leg)
        strut(f"Leg{k}Piston", Vector((0, 0.14, 0.05)), ankle + Vector((0, 0.12, 0.12)), 0.035, P["grey"], leg)
        ball(f"Leg{k}Ankle", 0.11, ankle, P["dark"], leg)
        spike(f"Leg{k}Shin", ankle, foot, 0.095, P["dark"], leg, tip=0.7)
        ball(f"Leg{k}Foot", 0.1, foot, P["grey"], leg)
        g = Vector((0, 0, -foot.z - hip.z + 0.03))  # ground level in the leg's frame
        for t, (dx, dy) in enumerate(((-0.16, 0.36), (0.0, 0.44), (0.16, 0.36))):
            spike(f"Leg{k}Toe{t}", foot, Vector((foot.x + dx, foot.y + dy, 0)) + Vector((0, 0, g.z + foot.z)), 0.045, P["seam"], leg, tip=0.2)
        spike(f"Leg{k}Spur", foot, Vector((foot.x, foot.y - 0.28, g.z + foot.z)), 0.04, P["seam"], leg, tip=0.2)

    # Drill boom, hinged in the beak.
    angle = math.radians(-58 if POSE == "deployed" else 28)
    boom = empty("Boom", (0, 2.0, 2.05), root)
    boom.rotation_euler = (angle, 0, 0)
    L = 1.3
    for s in (-1, 1):
        box(f"BoomPlate{s}", (0.06, L + 0.3, 0.26), (s * 0.17, L / 2, 0), P["jade"], boom, 0.03)
    box("BoomSpine", (0.22, L, 0.14), (0, L / 2, 0.1), P["dark"], boom, 0.03)
    cylinder("BoomHinge", 0.13, 0.46, (0, 0, 0), P["grey"], boom, axis="X", segments=18)
    strut("BoomRam", (0, 0.1, -0.18), (0, L * 0.85, -0.1), 0.05, P["grey"], boom)
    drill = empty("Drill", (0, L, 0), boom)
    drill.rotation_euler = (-angle, 0, 0)  # the drill hangs plumb whatever the boom angle
    cylinder("DrillHead", 0.22, 0.42, (0, 0, 0), P["grey"], drill, axis="Z", segments=24, bev=0.02)
    cylinder("DrillRing", 0.235, 0.05, (0, 0, -0.1), P["glowsoft"], drill, axis="Z", segments=24)
    cylinder("DrillCap", 0.16, 0.12, (0, 0, 0.26), P["dark"], drill, axis="Z", segments=20)
    world_z = 2.05 + L * math.sin(angle)  # drill head centre height
    string_len = max(0.1, world_z - 0.21 - 0.28) if POSE == "deployed" else 0.12  # deployed: the bit reaches the ground
    cylinder("String", 0.06, string_len, (0, 0, -0.21 - string_len / 2), P["grey"], drill, axis="Z", segments=12)
    cylinder("Bit", 0.02, 0.32, (0, 0, -0.21 - string_len - 0.14), P["dark"], drill, axis="Z", segments=16, radius2=0.15)
    empty("Dust", (0, 0, -0.21 - string_len - 0.3), drill)
    if POSE == "deployed":
        hole = Vector((0, 2.0 + L * math.cos(angle), 0.012))
        cylinder("DigCrater", 0.55, 0.02, hole, P["scorch"], root, axis="Z", segments=28)
        cylinder("DigGlow", 0.26, 0.022, hole + Vector((0, 0, 0.004)), P["glowsoft"], root, axis="Z", segments=24)
    return root


# ---------------------------------------------------------------- talon drop ship

SHIP_W = [(6.4, 0.12), (5.8, 0.6), (4.6, 1.1), (2.5, 1.5), (0.0, 1.7), (-2.5, 1.6), (-4.5, 1.3), (-5.8, 0.9)]
SHIP_ZT = [(6.4, 4.5), (5.8, 4.85), (4.6, 5.3), (2.5, 5.9), (0.0, 6.1), (-2.5, 6.0), (-4.5, 5.6), (-5.8, 5.2)]
SHIP_ZB = [(6.4, 4.2), (5.8, 3.9), (4.6, 3.7), (2.5, 3.5), (0.0, 3.4), (-2.5, 3.5), (-4.5, 3.9), (-5.8, 4.4)]


def dropship(P, name="VERAKH", number="09"):
    """Talon drop ship: carries one vehicle down from orbit in a four-talon cargo clamp under its belly. The great
    crescent wings arch out and down until their tips stand on the ground as landing legs; a tail strut makes the
    third foot. Beaked nose with a canopy, feather-blade tail fan, twin glowing drive nozzles, a sensor head and a
    beacon on the back. Shown landed with the survey skimmer in the clamp."""
    root = empty("DropShip")
    M = mats6(P)
    ns = 150
    ys = [-5.8 + 12.2 * i / (ns - 1) for i in range(ns)]
    loft("Hull", [se_loop(y, pchip(SHIP_W, y), pchip(SHIP_ZB, y), pchip(SHIP_ZT, y), 72, 1.8, 2.4) for y in ys], M, root,
         body_paint(1.3, 0.9, 12.2 / (ns - 1)))
    CW = [(6.0, 0.05), (5.5, 0.45), (4.8, 0.6), (4.1, 0.48), (3.6, 0.05)]
    cys = [3.6 + 2.4 * i / 23 for i in range(24)]
    loft("Canopy", [se_loop(y, pchip(CW, y), pchip(SHIP_ZT, y) - 0.55, pchip(SHIP_ZT, y) + 0.14 * math.sin(math.pi * (y - 3.6) / 2.4),
                            28, 2.2, 2.0) for y in cys], [P["glass"]], root)
    spine = lambda s: (1.0 + 5.2 * s, 5.0 + 1.8 * s - 6.75 * s ** 4)  # noqa: E731
    le = lambda s: 3.0 - 3.6 * s ** 1.4  # noqa: E731
    chord = lambda s: 6.5 - 5.5 * s  # noqa: E731
    thick = lambda s: 0.55 - 0.3 * s  # noqa: E731
    for s in (-1, 1):
        wing(f"Wing{s}", s, spine, le, chord, thick, M, feather_paint(n=130, res=36, rows=8, feathers=4, lead=0.05), root, n=130, res=36)
        feather_tips(f"WingFeather{s}", s, spine, le, chord, [0.1, 0.22, 0.34, 0.46, 0.58, 0.7], 1.3, 0.75, [P["jade"], P["hull"]], root, inset=0.3)
        box(f"WingFoot{s}", (0.7, 1.5, 0.14), (s * 6.2, -1.1, 0.07), P["dark"], root, 0.04)
        # Drive nozzles and their glow.
        cylinder(f"Nozzle{s}", 0.4, 0.7, (s * 0.62, -5.55, 4.6), P["grey"], root, axis="Y", segments=24, bev=0.03)
        cylinder(f"NozzleGlow{s}", 0.31, 0.05, (s * 0.62, -5.92, 4.6), P["glow"], root, axis="Y", segments=24)
        box(f"Landing{s}", (0.3, 0.1, 0.1), (s * 0.45, 5.6, 4.05), P["lamp"], root, 0.02)
        # Decals on the lower flank, behind the wing root.
        for nm, body, size, y, z, mw in ((f"Name{s}", name, 0.3, 1.6, 4.3, 1.5), (f"Number{s}", number, 0.5, -4.1, 4.7, 0.8)):
            x = se_surface_x(y, z, SHIP_W, SHIP_ZB, SHIP_ZT, 1.8, 2.4) + 0.02
            slope = (pchip(SHIP_W, y + 0.1) - pchip(SHIP_W, y - 0.1)) / 0.2
            side_text(nm, body, size, y, z, s * x, s, P["number"], root, mw, yaw=-s * math.atan(slope))
    # Tail strut (third foot) and tail fan.
    strut("TailStrut", (0, -4.4, 4.0), (0, -5.2, 0.25), 0.13, P["grey"], root)
    box("TailFoot", (0.8, 0.9, 0.14), (0, -5.25, 0.07), P["dark"], root, 0.04)
    for k, a in enumerate((-0.6, -0.3, 0.0, 0.3, 0.6)):
        d = Vector((math.sin(a), -math.cos(a), 0.1))
        base = Vector((0, -5.0, 5.0))
        sd = Vector((math.cos(a), math.sin(a), 0)) * 0.32
        tip = base + d * (1.9 - 0.25 * abs(a))
        slab(f"TailFeather{k}", [base + sd, base - sd, tip - sd * 0.45, tip + d * 0.25, tip + sd * 0.55], (0, 0, 0.06),
             P["jade"] if k % 2 == 0 else P["hull"], root)
    # Cargo clamp: a frame under the belly and four talons gripping the vehicle's pods.
    box("ClampFrame", (1.5, 3.4, 0.32), (0, -0.2, 3.3), P["dark"], root, 0.06)
    box("ClampGlow", (1.1, 2.8, 0.03), (0, -0.2, 3.13), P["glowsoft"], root, 0)
    for k, (s, yk) in enumerate(((-1, 0.35), (1, 0.35), (-1, -1.2), (1, -1.2))):
        a, b, c, d = (Vector((s * 0.65, yk, 3.25)), Vector((s * 1.6, yk, 2.1)), Vector((s * 1.52, yk, 0.72)),
                      Vector((s * 1.08, yk, 0.13)))
        strut(f"Talon{k}Upper", a, b, 0.1, P["grey"], root)
        ball(f"Talon{k}Knuckle", 0.14, b, P["dark"], root)
        strut(f"Talon{k}Lower", b, c, 0.085, P["jade"], root)
        ball(f"Talon{k}Wrist", 0.1, c, P["dark"], root)
        spike(f"Talon{k}Claw", c, d, 0.08, P["seam"], root, tip=0.2)
    sensor_head(P, root, (0, 2.3, 5.86), 1.6)
    cylinder("Beacon", 0.12, 0.16, (0, -3.4, 5.86), P["beacon"], root, axis="Z", segments=16)
    rover(P, root)  # the cargo: the survey skimmer, held in the talons
    return root


# ---------------------------------------------------------------- colony core


def crescent_roof(name, P, parent, span, root_z, foot_z, le0, le1, chord0, chord1, thick, y0=0.0, x0=0.0, rot_z=0.0,
                  upswept=None, tips=6, tip_len=1.2, rows=6):
    """A crescent wing roof: two mirrored wing halves meeting on the ridge, drooping to feet at +-span (foot_z), or
    with upswept=(rise) sweeping up to their tips instead."""
    g = empty(name, (x0, y0, 0), parent)
    g.rotation_euler = (0, 0, rot_z)
    if upswept is None:
        spine = lambda s: (span * s, root_z + 0.08 * span * s - (root_z - foot_z + 0.08 * span) * s ** 3)  # noqa: E731
    else:
        spine = lambda s: (0.4 + (span - 0.4) * math.sin(s * math.pi / 2), root_z + upswept * (1 - math.cos(s * math.pi / 2)))  # noqa: E731
    le = lambda s: le0 + (le1 - le0) * s * s  # noqa: E731
    chord = lambda s: chord0 + (chord1 - chord0) * s  # noqa: E731
    th = lambda s: thick * (1 - 0.45 * s)  # noqa: E731
    for s in (-1, 1):
        wing(f"{name}Half{s}", s, spine, le, chord, th, mats6(P), feather_paint(n=48, res=16, rows=rows, feathers=4), g, n=48, res=16,
             caps=(upswept is not None, True))
        if tips:
            feather_tips(f"{name}Feather{s}", s, spine, le, chord, [0.1 + 0.8 * k / (tips - 1) for k in range(tips)], tip_len,
                         tip_len * 0.62, [P["jade"], P["hull"]], g, inset=thick * 0.6)
    return g, spine, le, chord


def cargo_pod(name, P, parent, loc, yaw, length=2.4, r=0.5):
    g = empty(name, loc, parent)
    g.rotation_euler = (0, 0, yaw)
    W = [(-length / 2, 0.08), (-length / 2 + 0.45, r), (length / 2 - 0.45, r), (length / 2, 0.08)]
    secs = [se_loop(y, pchip(W, y), r - pchip(W, y), r + pchip(W, y), 24, 2.0, 2.0) for y in [-length / 2 + length * i / 15 for i in range(16)]]

    def p(i, j, m, n, c):
        return OWNER if abs(c.y) < 0.12 else (JADE if abs(c.y) > length / 2 - 0.5 else DARK)
    loft(f"{name}Body", secs, mats6(P), g, p)
    return g


def outpost_l1(P, root):
    """Colonial Garrison, just landed: a low hall on four splayed landing legs under a crescent wing roof whose
    feathered tips touch the ground, a ramp down from the glowing door, a scorched landing ring, the beacon spire beside
    it, the sensor head on the roof ridge and three drop pods of stores."""
    cylinder("Scorch", 8.5, 0.02, (0, 0, 0.01), P["scorch"], root, axis="Z", segments=48)
    cylinder("ScorchHalo", 10.5, 0.016, (0, 0, 0.006), material("ScorchHalo", (0.16, 0.12, 0.085), 0.0, 1.0), root, axis="Z", segments=48)
    h = hall("Hall", 9.0, 2.8, 1.0, 3.6, -0.3, P, root)
    for k, (sx, sy) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        a, b = Vector((sx * 1.7, -0.3 + sy * 2.8, 1.2)), Vector((sx * 2.6, -0.3 + sy * 3.5, 0.12))
        strut(f"LandingLeg{k}", a, b, 0.16, P["grey"], root)
        cylinder(f"LandingPad{k}", 0.55, 0.14, (b.x, b.y, 0.07), P["dark"], root, axis="Z", segments=20, bev=0.03)
    for k, y in enumerate((1.7, -0.3, -2.3)):
        box(f"RoofPylon{k}", (0.5, 0.6, 0.8), (0, y, 3.9), P["dark"], root, 0.05)
    crescent_roof("Roof", P, root, 6.6, 4.25, 0.2, 3.0, 1.3, 6.8, 2.4, 0.45, y0=-0.3)
    for s in (-1, 1):
        box(f"RoofFoot{s}", (0.8, 2.6, 0.2), (s * 6.6, 0.2, 0.1), P["dark"], root, 0.05)
    # Door, ramp, banners.
    box("Door", (1.0, 0.12, 1.3), (0, 4.0, 1.75), P["glow"], root, 0.02)
    box("DoorFrame", (1.4, 0.2, 1.7), (0, 3.95, 1.85), P["dark"], root, 0.04)
    ramp = box("Ramp", (1.3, 2.4, 0.1), (0, 5.2, 0.62), P["grey"], root, 0.02)
    ramp.rotation_euler = (math.atan2(-1.1, 2.2), 0, 0)
    for s in (-1, 1):
        box(f"Banner{s}", (0.5, 0.05, 1.5), (s * 1.0, 3.92, 2.1), P["owner"], root, 0.01)
    # Beacon spire beside it.
    hex_prism("SpireBase", 1.3, 0, 0.4, P["dark"], root, (8.3, -2.8))
    tower("Spire", 10.5, 0.85, 0.08, 0.32, 0.05, P, root, (8.3, -2.8, 0.4), lean=0.06, band=7)
    cylinder("Beacon", 0.16, 0.24, (8.3, -2.8 - 0.06 * 10.5, 10.95), P["beacon"], root, axis="Z", segments=16)
    sensor_head(P, root, (0, -2.4, 4.4), 1.5)
    for k, (x, y, yaw) in enumerate(((-7.6, -5.4, 0.6), (-8.8, -3.0, 1.2), (-6.4, -7.2, 0.2))):
        cargo_pod(f"DropPod{k}", P, root, (x, y, 0), yaw)


def outpost_l5(P, root):
    """Proconsulate: the hall on a stone terrace, its roof split into two upswept wings over a reflecting pool, owner
    banners at the door, three crescent-roofed villas, a landing pad, a taller beacon spire behind and the sensor head
    on the ridge."""
    hex_prism("Terrace", 10.6, 0, 0.6, P["stone"], root)
    hex_prism("TerraceUpper", 6.4, 0.6, 1.1, P["stonedark"], root, (0, -1.6))
    hall("Hall", 11.0, 2.9, 1.1, 4.3, -1.4, P, root, point=0.2)
    crescent_roof("Wings", P, root, 8.2, 3.95, 0, 2.6, 4.4, 6.6, 1.8, 0.5, y0=-1.4, upswept=6.0, tips=7, tip_len=1.6, rows=7)
    # Low crescent canopy over the door, between the wings.
    crescent_roof("Porch", P, root, 2.6, 3.4, 1.15, 5.2, 4.9, 1.6, 1.2, 0.22, tips=0, rows=3)
    box("Door", (1.0, 0.3, 1.5), (0, 3.92, 1.85), P["glow"], root, 0.02)
    box("DoorFrame", (1.3, 0.3, 1.8), (0, 3.86, 1.95), P["dark"], root, 0.04)
    for s in (-1, 1):  # owner banners on two masts flanking the pool
        cylinder(f"BannerMast{s}", 0.07, 4.6, (s * 3.9, 4.3, 2.9), P["grey"], root, axis="Z", segments=10)
        box(f"Banner{s}", (0.05, 0.7, 2.6), (s * 3.9, 4.0, 3.5), P["owner"], root, 0.01)
        cylinder(f"BannerTip{s}", 0.1, 0.12, (s * 3.9, 4.3, 5.25), P["glowsoft"], root, axis="Z", segments=10)
    # Reflecting pool on the terrace, in front of the door.
    box("PoolRim", (6.8, 5.6, 0.3), (0, 6.8, 0.68), P["stonedark"], root, 0.04)
    box("Pool", (6.2, 5.0, 0.3), (0, 6.8, 0.7), P["water"], root, 0)
    box("PoolGlow", (0.3, 5.0, 0.02), (0, 6.8, 0.86), P["glowsoft"], root, 0)
    # Villas, landing pad, spire, sensor head.
    for k, (x, y, yaw) in enumerate(((-6.6, -3.4, 0.25), (6.6, -3.4, -0.25), (-6.4, 4.4, 0.0))):
        v = hall(f"Villa{k}", 4.2, 1.3, 0.6, 2.4, 0, P, root, windows=True)
        v.location = (x, y, 0)
        v.rotation_euler = (0, 0, yaw)
        r, *_ = crescent_roof(f"VillaRoof{k}", P, root, 2.4, 2.85, 0.65, 1.6, 0.8, 4.0, 1.6, 0.24, tips=4, tip_len=0.6, rows=3)
        r.location = (x, y, 0)
        r.rotation_euler = (0, 0, yaw)
    cylinder("Pad", 2.2, 0.08, (5.8, 4.1, 0.64), P["dark"], root, axis="Z", segments=36)
    cylinder("PadRing", 1.9, 0.09, (5.8, 4.1, 0.645), P["glowsoft"], root, axis="Z", segments=36)
    cylinder("PadCentre", 1.7, 0.1, (5.8, 4.1, 0.65), P["dark"], root, axis="Z", segments=36)
    cylinder("PadMark", 0.6, 0.11, (5.8, 4.1, 0.65), P["owner"], root, axis="Z", segments=3)
    hex_prism("SpireBase", 1.5, 1.1, 1.6, P["dark"], root, (0, -7.4))
    tower("Spire", 15.0, 1.05, 0.08, 0.42, 0.05, P, root, (0, -7.4, 1.6), lean=0.04, band=8)
    cylinder("Beacon", 0.2, 0.3, (0, -7.4 - 0.04 * 15, 16.7), P["beacon"], root, axis="Z", segments=16)
    sensor_head(P, root, (0, -4.2, 4.2), 1.6)


def outpost_l10(P, root):
    """Imperial capital: three stone terraces, a towering feather-blade spire framed by two great crescent arches
    (folded wings) joined to it by skybridges, a long reflecting pool, a ring of crescent-roofed halls, two secondary
    spires (one carries the sensor head) and the beacon on the summit."""
    hex_prism("Terrace0", 34, 0, 1.0, P["stone"], root)
    hex_prism("Terrace1", 26, 1.0, 2.2, P["stonedark"], root)
    hex_prism("Terrace2", 17, 2.2, 3.6, P["stone"], root)
    tower("Spire", 50.0, 4.4, 0.25, 2.4, 0.15, P, root, (0, -3.0, 3.6), lean=0.03, band=6, n=72, m=32)
    cylinder("Beacon", 0.45, 0.8, (0, -3.0 - 0.03 * 50, 54.0), P["beacon"], root, axis="Z", segments=16)
    # Senate drum around the spire's foot, with a crescent porch over the pool end.
    hex_prism("SenateDrum", 7.0, 3.6, 6.4, P["dark"], root, (0, -3.0))
    hex_prism("SenateBand", 7.08, 5.2, 5.6, P["glowsoft"], root, (0, -3.0))
    hex_prism("SenateCrown", 7.4, 6.4, 6.9, P["jade"], root, (0, -3.0))
    crescent_roof("SenatePorch", P, root, 6.5, 8.2, 3.65, 6.8, 6.0, 4.0, 2.0, 0.5, tips=5, tip_len=1.4, rows=5)
    # Great crescent arches: rise from the upper terrace and curve inward over the spire like folded wings.
    spine = lambda s: (13.6 - 10.2 * s * s, 3.4 + 31.0 * s ** 0.85)  # noqa: E731
    le = lambda s: 3.0 - 2.0 * s  # noqa: E731
    chord = lambda s: 11.0 - 8.0 * s  # noqa: E731
    th = lambda s: 1.8 - 1.1 * s  # noqa: E731
    for s in (-1, 1):
        wing(f"Arch{s}", s, spine, le, chord, th, mats6(P), feather_paint(n=110, res=30, rows=9, feathers=5, lead=0.05, under=HULL), root, n=110, res=30,
             flip=True)
        feather_tips(f"ArchFeather{s}", s, spine, le, chord, [0.25 + 0.1 * k for k in range(7)], 3.2, 2.0, [P["jade"], P["hull"]], root,
                     inset=0.9, thick=0.12, flip=True)
        # Skybridge from the spire to the arch, with a glowing underside.
        (ax, az), _, _ = spine_frame(spine, 0.62)
        box(f"Skybridge{s}", (ax - 2.0, 1.4, 0.55), (s * (ax + 2.0) / 2, -2.4, az), P["jade"], root, 0.08)
        box(f"SkybridgeGlow{s}", (ax - 2.2, 0.8, 0.04), (s * (ax + 2.0) / 2, -2.4, az - 0.29), P["glowsoft"], root, 0)
        hex_prism(f"SpireB{s}", 2.0, 2.2, 3.0, P["dark"], root, (s * 13.0, -12.0))
        tower(f"SpireB{s}", 22.0, 1.4, 0.1, 0.6, 0.06, P, root, (s * 13.0, -12.0, 3.0), lean=0.03, band=7)
    cylinder("BeaconB", 0.22, 0.3, (13.0, -12.0 - 0.03 * 22, 25.2), P["beacon"], root, axis="Z", segments=16)
    sensor_head(P, root, (-13.0, -12.0 - 0.03 * 22, 25.0), 2.6)
    # Reflecting pool down the front terraces.
    box("PoolRim", (9.6, 13.0, 0.3), (0, 9.5, 3.62), P["stonedark"], root, 0.05)
    box("Pool", (8.8, 12.2, 0.3), (0, 9.5, 3.64), P["water"], root, 0)
    box("CanalRim", (5.4, 11.0, 0.3), (0, 23.5, 1.02), P["stonedark"], root, 0.05)
    box("Canal", (4.6, 10.4, 0.3), (0, 23.5, 1.04), P["water"], root, 0)
    box("Steps", (6.0, 3.2, 2.6), (0, 16.4, 1.3), P["stone"], root, 0.05)
    for s in (-1, 1):
        box(f"Banner{s}", (1.2, 0.1, 8.0), (s * 3.4, 1.0, 7.6), P["owner"], root, 0.02)
    # Ring of halls on the middle terrace.
    for k, deg in enumerate((-10, -55, -90, -125, -170, 35, 145)):
        a = math.radians(deg)
        r = 21.5
        x, y = r * math.cos(a), r * math.sin(a)
        yaw = a  # hall length runs tangentially
        hall(f"RingHall{k}", 8.0, 2.4, 2.2, 6.0, 0, P, root).location = (x, y, 0)
        bpy.data.objects[f"RingHall{k}"].rotation_euler = (0, 0, yaw)
        rf, *_ = crescent_roof(f"RingRoof{k}", P, root, 4.6, 6.7, 2.25, 4.2, 2.4, 9.0, 3.0, 0.4, tips=4, tip_len=1.0, rows=4)
        rf.location = (x, y, 0)
        rf.rotation_euler = (0, 0, yaw)


# ---------------------------------------------------------------- preview

SHOT_CAMS = {  # build_vehicles.preview_scene's cameras (scaled for the bigger models)
    "front34": ((7.5, 8.5, 3.2), 40),
    "rear34": ((-7.5, -8.0, 3.6), 40),
    "side": ((10.5, 0.4, 1.9), 38),
    "game": ((0.0, 13.0, 15.5), 30),
}


def preview_scene(folder, stem, scale, game_zoom=1.0, target_z=None):
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
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(55), 0, math.radians(150))
    link(sun)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40 * max(1.0, scale))
    bm.to_mesh(ground)
    bm.free()
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    cam.data.clip_end = 400 * max(1.0, scale)
    scene.camera = cam
    target = Vector((0, 0, 1.3 * scale if target_z is None else target_z))
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        pos, lens = SHOT_CAMS[name]
        pos = Vector(pos) * scale * (game_zoom if name == "game" else 1.0)
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{stem}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def main():
    BV["reset"]()
    P = palette()
    if MODEL == "rover":
        root, stem, scale = rover(P), "romulan-rover", 1.0
    elif MODEL == "miner":
        root, stem, scale = miner(P), "romulan-miner" + ("" if POSE == "deployed" else "-stowed"), 1.0
    elif MODEL == "dropship":
        root, stem, scale = dropship(P), "romulan-dropship", 2.6
    else:
        root = empty(f"ColonyCore{LEVEL}")
        {1: outpost_l1, 5: outpost_l5, 10: outpost_l10}[LEVEL](P, root)
        g = empty("HexGrid")
        hex_outline(P, g, width=0.18 if LEVEL < 10 else 0.4)
        if LEVEL == 10:
            for k in range(6):
                a = math.radians(60 * k)
                hex_outline(P, g, (25 * math.cos(a), 25 * math.sin(a)), width=0.4, name=f"Hex{k}_")
        stem, scale = f"romulan-outpost-l{LEVEL}", {1: 4.3, 5: 4.6, 10: 11.5}[LEVEL]
    if OUT:
        if bpy.data.objects.get("HexGrid"):
            for o in [bpy.data.objects["HexGrid"]] + list(bpy.data.objects["HexGrid"].children_recursive):
                o.hide_render = True
        BV["export"](root, OUT)
        if bpy.data.objects.get("HexGrid"):
            for o in [bpy.data.objects["HexGrid"]] + list(bpy.data.objects["HexGrid"].children_recursive):
                o.hide_render = False
    if PREVIEW:
        preview_scene(PREVIEW, stem, scale, 0.62 if MODEL == "outpost" else 1.0, 19.0 if MODEL == "outpost" and LEVEL == 10 else None)


main()
