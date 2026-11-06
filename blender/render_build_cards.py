"""Build-menu renders for the HUD mockup (docs/mockups/build-card/).

Renders every build-menu building (surfaces.json build_menu) the same way: a 3/4 front camera fitted to the model,
dark studio lights, a shadow catcher under it and a transparent film, so `--composite` (system python, PIL) can lay
them on the HUD's navy background and write JPEGs.

Models:
  refinery        game/models/buildings/refinery.glb (real model)
  colony_hub      game/models/buildings/outpost_l1.glb (real model, for the base panel)
  everything else stand-ins rebuilt from the primitive builders in game/scripts/BuildingModels.cs (Look.Plain:
                  no variety, no add-ons), same sizes, colours and materials (sRGB Godot colours -> linear).

Godot frame: Y up, front +Z. The primitives are built in that frame under an empty turned +90 deg about X, so Godot
(x, y, z) lands at Blender (x, -z, y) and the front faces -Y like the glbs.

Run:  /home/user/.bpyvenv/bin/python blender/render_build_cards.py --out <png dir> [--only id,id] [--samples 48]
      [--angles 4] [--hero 1024x768] [--thumb 512]
Then: python3 blender/render_build_cards.py --composite <png dir> docs/mockups/build-card
"""

import math
import os
import sys

ARGS = sys.argv[1:]


def arg(name, default=None):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else default


# ---------------------------------------------------------------- composite (system python + PIL, no bpy)

def composite(src, dst):
    from PIL import Image, ImageDraw, ImageFilter

    os.makedirs(dst, exist_ok=True)
    total = 0
    for name in sorted(os.listdir(src)):
        if not name.endswith(".png"):
            continue
        fg = Image.open(os.path.join(src, name)).convert("RGBA")
        w, h = fg.size
        # HUD ground (#05080f) with a soft lift behind the model and a faint floor glow
        bg = Image.new("RGB", (w, h), (5, 8, 16))
        glow = Image.new("L", (w, h), 0)
        d = ImageDraw.Draw(glow)
        d.ellipse((w * 0.08, h * 0.12, w * 0.92, h * 1.02), fill=255)
        glow = glow.filter(ImageFilter.GaussianBlur(w * 0.16))
        lift = Image.new("RGB", (w, h), (24, 34, 58))
        bg = Image.composite(lift, bg, glow)
        bg.paste(fg, (0, 0), fg)
        out = os.path.join(dst, name[:-4] + ".jpg")
        bg.save(out, "JPEG", quality=int(arg("--quality", "82")), optimize=True, progressive=True)
        total += os.path.getsize(out)
        print(out, os.path.getsize(out) // 1024, "KB")
    print("total", total // 1024, "KB")


if "--composite" in ARGS:
    i = ARGS.index("--composite")
    composite(ARGS[i + 1], ARGS[i + 2])
    sys.exit(0)

# ---------------------------------------------------------------- Blender side

import bpy  # noqa: E402  (bpy before bmesh)
import bmesh  # noqa: E402
from bpy_extras.object_utils import world_to_camera_view  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = arg("--out", os.path.join(REPO, "blender", "previews", "build_cards"))
ONLY = [s for s in arg("--only", "").split(",") if s]
SAMPLES = int(arg("--samples", "48"))
ANGLES = int(arg("--angles", "4"))
HERO = tuple(int(v) for v in arg("--hero", "1024x768").split("x"))
THUMB = int(arg("--thumb", "512"))

HULL = (0.84, 0.86, 0.9)
TRIM = (0.3, 0.32, 0.38)
WINDOW = (0.55, 0.8, 1.0)
OWNER = (0.45, 0.7, 1.0)  # PlanetDriver.PlayerColors[0]
ACCENT = (0.16, 0.32, 0.58)  # Look.Plain accent
EMIT = 1.4  # Godot emission energy -> Cycles strength


def lin(c):
    def ch(v):
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return tuple(ch(v) for v in c[:3])


_mats = {}


def principled(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    return m, m.node_tree.nodes["Principled BSDF"]


def metal(c, metallic=0.55, rough=0.35):
    key = ("m", c, metallic, rough)
    if key not in _mats:
        m, p = principled("Metal")
        p.inputs["Base Color"].default_value = (*lin(c), 1)
        p.inputs["Metallic"].default_value = metallic
        p.inputs["Roughness"].default_value = rough
        _mats[key] = m
    return _mats[key]


def glow(c, energy=2.4):
    key = ("g", c, energy)
    if key not in _mats:
        m, p = principled("Glow")
        p.inputs["Base Color"].default_value = (*lin(c), 1)
        p.inputs["Roughness"].default_value = 0.3
        p.inputs["Emission Color"].default_value = (*lin(c), 1)
        p.inputs["Emission Strength"].default_value = energy * EMIT
        _mats[key] = m
    return _mats[key]


def glass(c, alpha, emission, energy):
    m, p = principled("Glass")
    p.inputs["Base Color"].default_value = (*lin(c), 1)
    p.inputs["Alpha"].default_value = alpha
    p.inputs["Metallic"].default_value = 0.6
    p.inputs["Roughness"].default_value = 0.08
    p.inputs["Emission Color"].default_value = (*lin(emission), 1)
    p.inputs["Emission Strength"].default_value = energy * EMIT
    return m


# --- Godot primitive meshes, built in the Godot frame (Y up, centred like Godot's)

TO_Y = Matrix.Rotation(math.radians(-90), 4, "X")  # bmesh builds along Z; Godot meshes run along Y


def _finish(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    return me


def box(sx, sy, sz):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    return _finish(bm, "Box")


def cyl(top, bottom, height, seg=24, smooth=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=bottom, radius2=top, depth=height)
    bmesh.ops.transform(bm, matrix=TO_Y, verts=bm.verts)
    me = _finish(bm, "Cyl")
    if smooth:
        for p in me.polygons:
            p.use_smooth = abs(p.normal.y) < 0.9
    return me


def sphere(radius, height, hemi=False, seg=24):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(8, seg // 2), radius=1.0)
    bmesh.ops.transform(bm, matrix=TO_Y, verts=bm.verts)
    for v in bm.verts:
        if hemi:
            y = max(v.co.y, 0.0) * height
        else:
            y = v.co.y * height / 2
        v.co = Vector((v.co.x * radius, y, v.co.z * radius))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = _finish(bm, "Sphere")
    for p in me.polygons:
        p.use_smooth = True
    return me


def torus(inner, outer, rings=32, sides=12):
    major, minor = (inner + outer) / 2, (outer - inner) / 2
    bm = bmesh.new()
    grid = []
    for i in range(rings):
        a = 2 * math.pi * i / rings
        row = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            r = major + minor * math.cos(b)
            row.append(bm.verts.new((r * math.cos(a), minor * math.sin(b), r * math.sin(a))))
        grid.append(row)
    for i in range(rings):
        for j in range(sides):
            bm.faces.new((grid[i][j], grid[(i + 1) % rings][j], grid[(i + 1) % rings][(j + 1) % sides], grid[i][(j + 1) % sides]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = _finish(bm, "Torus")
    for p in me.polygons:
        p.use_smooth = True
    return me


def node(parent, pos=(0, 0, 0), rot=None, name="Node"):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    e.location = pos
    if rot:
        e.rotation_mode = "ZXY"  # Godot's YXZ order = Ry * Rx * Rz
        e.rotation_euler = tuple(math.radians(r) for r in rot)
    return e


def add(parent, mesh, pos, mat, rot=None):
    o = bpy.data.objects.new("Part", mesh)
    bpy.context.scene.collection.objects.link(o)
    o.parent = parent
    o.location = pos
    if rot:
        o.rotation_mode = "ZXY"
        o.rotation_euler = tuple(math.radians(r) for r in rot)
    o.data.materials.append(mat)
    return o


def omni(parent, colour, energy, rng, pos):
    ld = bpy.data.lights.new("Omni", "POINT")
    ld.color = lin(colour)
    ld.energy = energy * rng * 14
    ld.shadow_soft_size = 0.25
    o = bpy.data.objects.new("Omni", ld)
    bpy.context.scene.collection.objects.link(o)
    o.parent = parent
    o.location = pos
    return o


def godot_root():
    r = bpy.data.objects.new("GodotFrame", None)
    bpy.context.scene.collection.objects.link(r)
    r.rotation_euler = (math.radians(90), 0, 0)
    return r


# --- the primitive buildings (ports of BuildingModels.cs, Look.Plain)

def mine(n):
    hull, dark = metal(HULL), metal(TRIM, 0.3, 0.6)
    add(n, box(6.6, 0.7, 6.6), (0, 0.35, 0), dark)
    h = 1.0
    for x, z in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        add(n, cyl(0.1, 0.18, 7 * h, 6), (x * 1.1, 0.6 + 3.5 * h, z * 1.1), hull, (z * 8, 0, -x * 8))
    add(n, box(1.6, 0.7, 1.6), (0, 0.6 + 7 * h, 0), glow(OWNER, 2))
    spin = node(n, (0, 2.6, 0))
    add(spin, cyl(0.75, 0.1, 3.2, 6, False), (0, 0, 0), metal((0.7, 0.62, 0.45), 0.8, 0.25))
    add(n, cyl(1.4, 0.7, 2.2, 8, False), (2.3, 1.8, -1.9), metal(ACCENT, 0.45, 0.45))
    add(n, box(1, 0.35, 3.6), (0, 0.95, 2.6), dark)
    add(n, box(0.9, 0.08, 3.4), (0, 1.15, 2.6), glow((1, 0.75, 0.4), 1.2))
    omni(n, (1, 0.82, 0.6), 0.9, 12, (0, 5, 2))


def reactor(n):
    hull = metal(HULL)
    add(n, cyl(3.6, 4, 0.9, 20), (0, 0.45, 0), metal(TRIM, 0.4, 0.5))
    add(n, cyl(1.7, 2.2, 4.8, 20), (0, 3.3, 0), hull)
    add(n, sphere(1.7, 1.6, True, 20), (0, 5.7, 0), hull)
    core = (0.35, 0.85, 1)
    spin = node(n, (0, 3.4, 0))
    add(spin, torus(2.35, 2.75, 32), (0, 0, 0), glow(core, 3))
    add(spin, box(5.6, 0.25, 0.25), (0, 0, 0), hull)
    for x in (-2.7, 2.7):
        tower = 3.8
        add(n, cyl(0.85, 1.25, tower, 14), (x, 0.4 + tower / 2, -2.1), hull)
        add(n, cyl(0.86, 0.86, 0.2, 14), (x, 0.2 + tower, -2.1), glow(OWNER, 1.6))
    add(n, box(1.6, 1.2, 1), (0, 1.1, 3.4), metal(ACCENT, 0.4, 0.45))
    add(n, box(1, 0.6, 0.1), (0, 1.2, 3.92), glow(WINDOW, 1.2))
    omni(n, core, 1.6, 16, (0, 4, 0))


def habitat(n):
    hull = metal(HULL, 0.35, 0.4)
    warm = tuple(a + (b - a) * 0.5 for a, b in zip((1, 0.85, 0.6), (0.75, 0.9, 1)))
    g = glass((0.55, 0.75, 0.9), 0.55, warm, 0.35)
    for d in ((0, 0, -2), (-2.3, 0, 1.6), (2.3, 0, 1.6)):
        add(n, cyl(1.9, 2.05, 0.6, 16), (d[0], 0.3, d[2]), hull)
        add(n, sphere(1.8, 2.4, True, 16), (d[0], 0.6, d[2]), g)
        add(n, cyl(1.92, 1.92, 0.1, 16), (d[0], 0.62, d[2]), glow(OWNER, 1.4))
    tube = metal(ACCENT, 0.4, 0.45)
    add(n, cyl(0.32, 0.32, 4, 16), (0, 0.6, 1.6), tube, (0, 0, 90))
    add(n, cyl(0.32, 0.32, 3.2, 16), (0, 0.6, 0), tube, (90, 0, 0))
    add(n, box(1.4, 0.25, 1.4), (0, 0.13, 3.6), glow(WINDOW, 1))
    omni(n, warm, 0.8, 8, (0, 1.6, 0))


def lab(n):
    hull, dark = metal(HULL), metal(TRIM, 0.35, 0.55)
    g = glass((0.5, 0.75, 0.95), 0.5, (0.6, 0.85, 1), 0.5)
    add(n, box(7.2, 0.4, 7), (0, 0.2, 0), dark)
    add(n, box(5.2, 2.6, 4.2), (-0.8, 1.7, 0.6), hull)
    add(n, box(5.3, 0.16, 4.3), (-0.8, 2.6, 0.6), glow(OWNER, 1.6))
    add(n, sphere(2, 2.2, True, 16), (-0.8, 3, 0.6), g)
    for i in range(3):
        add(n, box(1, 0.6, 0.08), (-2.4 + i * 1.6, 1.8, 2.74), glow(WINDOW, 1.3))
    mh, tilt = 6.5, 35
    add(n, cyl(0.18, 0.3, mh, 8), (2.6, 0.35 + mh / 2, -2), hull)
    add(n, cyl(1.4, 0.2, 0.6, 14), (2.6, 0.3 + mh, -2), hull, (tilt, 0, 0))
    add(n, sphere(0.2, 0.4), (2.6, 0.9 + mh, -1.6), glow((1, 0.6, 0.15), 3))
    add(n, cyl(0.9, 1.1, 0.8, 12), (2.4, 0.8, 1.8), metal(ACCENT, 0.4, 0.45))
    spin = node(n, (2.4, 2.1, 1.8))
    add(spin, torus(0.75, 0.95, 24), (0, 0, 0), glow((0.55, 0.9, 1), 3), (70, 0, 0))
    add(spin, sphere(0.3, 0.6), (0, 0, 0), glow((0.85, 0.95, 1), 4))
    omni(n, (0.55, 0.85, 1), 1.3, 10, (2.4, 2.5, 2.2))


def vehicle_bay(n):
    hull, dark = metal(HULL), metal(TRIM, 0.35, 0.55)
    stripe = metal((0.9, 0.72, 0.18), 0.2, 0.6)
    door_h = 4.0
    add(n, box(8.2, 0.4, 8.4), (0, 0.2, 0.4), dark)
    add(n, box(7.4, 5, 0.4), (0, 2.9, -3), hull)
    for x in (-3.5, 3.5):
        add(n, box(0.4, 5, 5.2), (x, 2.9, -0.6), hull)
    add(n, box(7.8, 0.5, 5.8), (0, 5.6, -0.6), hull)
    for i in range(4):
        add(n, box(7.9, 0.25, 0.35), (0, 5.95, -2.8 + i * 1.45), dark)
    for x in (-2.95, 2.95):
        add(n, box(1.5, 5, 0.5), (x, 2.9, 2), hull)
    add(n, box(7.4, 1, 0.6), (0, 4.9, 2), hull)
    add(n, box(7.42, 0.18, 0.62), (0, 4.55, 2), glow(OWNER, 1.8))
    door = node(n, (0, 0.4 + door_h, 2.05))
    add(door, box(4.6, door_h, 0.15), (0, -door_h / 2, 0), metal((0.62, 0.64, 0.68), 0.6, 0.4))
    for i in range(6):
        add(door, box(4.62, 0.06, 0.18), (0, -0.4 - i * 0.62, 0), dark)
    add(n, box(4.2, 0.3, 3.6), (0, 0.55, -0.8), dark)
    for i in range(4):
        add(n, box(0.3, 0.04, 0.9), (-2.1, 0.42, 2.8 + i * 1.15), stripe)
        add(n, box(0.3, 0.04, 0.9), (2.1, 0.42, 2.8 + i * 1.15), stripe)
    tx, th = 3.6, 7.2
    add(n, box(1.6, th, 1.6), (tx, th / 2, -3.4), hull)
    add(n, box(1.64, 0.5, 1.64), (tx, th * 0.45, -3.4), metal(ACCENT, 0.4, 0.45))
    add(n, box(1.7, 0.6, 1.7), (tx, th - 0.8, -3.4), glow(WINDOW, 1.4))
    spin = node(n, (tx, th + 0.3, -3.4))
    add(spin, box(1.2, 0.25, 0.3), (0.4, 0, 0), glow((1, 0.6, 0.15), 3.5))
    add(spin, cyl(0.2, 0.2, 0.4, 12), (0, 0, 0), dark)


def deep_core_mine(n):
    hull, dark = metal(HULL), metal(TRIM, 0.3, 0.6)
    add(n, cyl(3.6, 4.1, 0.8, 8, False), (0, 0.4, 0), dark)
    add(n, torus(1.5, 1.9, 24), (0, 0.85, 0), glow((1, 0.5, 0.2), 2.6))
    h = 1.0
    for x, z in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        add(n, box(0.35, 10 * h, 0.35), (x * 1.6, 0.4 + 5 * h, z * 1.6), hull, (z * 7, 0, -x * 7))
    add(n, box(2.4, 1, 2.4), (0, 0.4 + 10 * h, 0), hull)
    add(n, box(2.5, 0.2, 2.5), (0, 0.9 + 10 * h, 0), glow(OWNER, 2))
    spin = node(n, (0, 4, 0))
    add(spin, cyl(0.25, 0.25, 6, 8), (0, 1.5, 0), metal((0.6, 0.6, 0.65), 0.85, 0.2))
    add(spin, cyl(1.1, 0.15, 3, 6, False), (0, -2.4, 0), metal((0.75, 0.62, 0.4), 0.8, 0.25))
    for x in (-2.9, 2.9):
        add(n, cyl(0.8, 0.8, 3, 12), (x, 0.8 + 1.5, 2.2), metal(ACCENT, 0.45, 0.4))
    omni(n, (1, 0.6, 0.3), 1.4, 14, (0, 2, 0))


def storage(n):
    add(n, box(7.4, 0.4, 7), (0, 0.2, 0), metal(TRIM, 0.3, 0.7))
    colours = [(0.72, 0.36, 0.22), (0.3, 0.45, 0.62), (0.78, 0.7, 0.42), (0.42, 0.55, 0.4), (0.62, 0.62, 0.64), (0.55, 0.24, 0.2)]
    ground = [(-1.8, -1.8), (1.8, -1.8), (-1.8, 1.2), (1.8, 1.2)]
    slots = [(x, z, 0) for x, z in ground] + [(ground[k][0], ground[k][1], 1) for k in (0, 3, 2)]
    for i, (x, z, level) in enumerate(slots):
        skew = (i % 2) * 2 - 1
        b = node(n, (x, 0.4 + 1.35 + level * 2.7, z), (0, skew, 0))
        c = colours[i % len(colours)]
        add(b, box(3.2, 2.6, 2.6), (0, 0, 0), metal(c, 0.45, 0.55))
        darker = tuple(v * 0.75 for v in c)
        for r in (-1, 0, 1):
            add(b, box(0.12, 2.4, 2.7), (r * 1.1, 0, 0), metal(darker, 0.45, 0.55))
    hull = metal(HULL)
    for x in (-3.5, 3.5):
        add(n, box(0.3, 7.4, 0.3), (x, 3.9, 3.1), hull)
    add(n, box(7.4, 0.45, 0.45), (0, 7.6, 3.1), hull)
    add(n, box(1, 0.6, 0.9), (0.8, 7.1, 3.1), glow(OWNER, 1.6))
    add(n, cyl(0.04, 0.04, 2.6, 8), (0.8, 5.6, 3.1), metal(TRIM))


def foundry(n):
    hull, dark = metal(HULL), metal(TRIM, 0.35, 0.55)
    hot = (1, 0.62, 0.2)
    add(n, box(7.2, 0.5, 7), (0, 0.25, 0), dark)
    add(n, box(6.2, 3.4, 3.2), (0, 2.2, -1.6), hull)
    add(n, box(6.3, 0.2, 3.3), (0, 3.95, -1.6), glow(OWNER, 1.5))
    sh = 5.0
    add(n, cyl(0.55, 0.7, sh, 10), (2.2, 3 + sh / 2, -2.2), hull)
    arch = metal(ACCENT, 0.4, 0.45)
    for x in (-1.6, 1.6):
        add(n, box(0.35, 3.6, 0.35), (x, 2.3, 1.2), arch)
    add(n, box(3.6, 0.4, 0.5), (0, 4.1, 1.2), arch)
    add(n, cyl(1.1, 0.8, 1.2, 14), (0, 1.1, 1.2), dark)
    add(n, cyl(0.95, 0.95, 0.1, 14), (0, 1.72, 1.2), glow(hot, 3.5))
    spin = node(n, (0, 2.6, 1.2))
    add(spin, box(1.4, 0.12, 0.12), (0, 0, 0), dark)
    add(spin, cyl(0.06, 0.06, 1.4, 8), (0.6, -0.6, 0), dark)
    ingot = metal((0.62, 0.66, 0.74), 0.85, 0.25)
    for i in range(6):
        add(n, box(1.1, 0.35, 0.45), (-2.6 + (i % 3) * 0.05, 0.7 + (i // 2) * 0.36, 2.2 + (i % 2) * 0.5), ingot)
    omni(n, hot, 1.6, 12, (0, 2.5, 2))


PRIMITIVES = {
    "storage_container": storage, "mine": mine, "fusion_reactor": reactor, "prefab_habitat": habitat,
    "foundry": foundry, "deep_core_mine": deep_core_mine, "vehicle_bay": vehicle_bay, "science_lab": lab,
}
GLBS = {"refinery": "game/models/buildings/refinery.glb", "colony_hub": "game/models/buildings/outpost_l1.glb"}


def import_glb(bid):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(REPO, GLBS[bid]))
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        if o.type != "MESH":
            continue
        for m in o.data.materials:
            if m and m.name.split(".")[0] == "Owner" and m.use_nodes:
                p = m.node_tree.nodes.get("Principled BSDF")
                if p and not p.inputs["Base Color"].is_linked:
                    p.inputs["Base Color"].default_value = (*lin(OWNER), 1)
    # the game adds these lights at runtime (BuildingModels.Refinery.cs)
    for o in new:
        if o.name == "FurnaceLight":
            omni(None, (1, 0.5, 0.2), 1.6, 10, o.matrix_world.translation)
        if o.name == "PodPad":
            omni(None, (0.45, 0.65, 1), 0.7, 5, o.matrix_world.translation + Vector((0, 0, 1.2)))
    return new


# ---------------------------------------------------------------- studio

def studio():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.065, 0.1, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9

    def light(kind, name, energy, colour, rot, size):
        ld = bpy.data.lights.new(name, kind)
        ld.energy = energy
        ld.color = colour
        if kind == "SUN":
            ld.angle = math.radians(size)
        else:
            ld.size = size
        o = bpy.data.objects.new(name, ld)
        scene.collection.objects.link(o)
        o.rotation_euler = tuple(math.radians(r) for r in rot)
        return o

    light("SUN", "Key", 4.2, (1.0, 0.95, 0.88), (52, 0, -38), 3)      # high, front-left
    light("SUN", "Rim", 2.2, (0.62, 0.75, 1.0), (60, 0, 160), 4)       # behind, cool
    light("SUN", "Fill", 0.7, (0.8, 0.86, 1.0), (75, 0, 95), 10)       # right, soft

    bpy.ops.mesh.primitive_plane_add(size=400)
    ground = bpy.context.active_object
    ground.is_shadow_catcher = True
    ground.name = "Ground"

    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 50
    cam.data.clip_end = 2000
    return scene, cam


def bounds(objs):
    pts = []
    for o in objs:
        if o.type == "MESH" and o.name != "Ground":
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    return pts


def frame(scene, cam, pts, az, el, w, h, fill):
    """Point the camera from azimuth az (deg from the front, toward +X) and elevation el, and pull it to the
    distance at which the model's box fills `fill` of the frame."""
    scene.render.resolution_x, scene.render.resolution_y = w, h
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    target = (lo + hi) / 2
    d = Vector((math.cos(math.radians(el)) * math.sin(math.radians(az)),
                -math.cos(math.radians(el)) * math.cos(math.radians(az)),
                math.sin(math.radians(el))))
    dist = (hi - lo).length * 1.6
    for _ in range(4):
        cam.location = target + d * dist
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        bpy.context.view_layer.update()
        uv = [world_to_camera_view(scene, cam, p) for p in pts]
        u0, u1 = min(p.x for p in uv), max(p.x for p in uv)
        v0, v1 = min(p.y for p in uv), max(p.y for p in uv)
        # shift the target so the box sits in the middle (a touch low so the top has room)
        aspect = w / h
        sx, sy = ((u0 + u1) / 2 - 0.5), ((v0 + v1) / 2 - 0.47)
        right = cam.matrix_world.to_3x3() @ Vector((1, 0, 0))
        up = cam.matrix_world.to_3x3() @ Vector((0, 1, 0))
        span = 2 * dist * math.tan(cam.data.angle / 2)
        target += right * sx * span * (1 if aspect >= 1 else aspect) + up * sy * span / (aspect if aspect >= 1 else 1)
        size = max(u1 - u0, v1 - v0)
        dist *= size / fill
    cam.location = target + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def render(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("wrote", path, flush=True)


def main():
    import json
    menu = json.load(open(os.path.join(REPO, "data", "surfaces.json")))["build_menu"]
    ids = (ONLY or menu + ["colony_hub"])
    os.makedirs(OUT, exist_ok=True)
    for bid in ids:
        scene, cam = studio()
        if bid in GLBS:
            objs = import_glb(bid)
        else:
            root = godot_root()
            PRIMITIVES[bid](root)
            bpy.context.view_layer.update()
            objs = list(bpy.data.objects)
        pts = bounds(objs)
        frame(scene, cam, pts, 38, 27, THUMB, THUMB, 0.86)
        render(scene, os.path.join(OUT, f"{bid}-thumb.png"))
        if bid == "colony_hub":
            continue
        for k in range(ANGLES):
            az = 38 + k * 360 / ANGLES
            frame(scene, cam, pts, az, 27 if k == 0 else 24, HERO[0], HERO[1], 0.84)
            render(scene, os.path.join(OUT, f"{bid}-{k}.png"))


main()
