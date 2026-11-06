"""Federation construction exocomp, game-ready (owner 2026-11-06: "i would like the building to be done with
exocomps/drones with beams that go around the building"). A small hovering work drone in the spirit of TNG's exocomps:
a rounded, slightly wedge-shaped white hull with a dark visor and a glowing sensor strip for a face, an owner-coloured
band, two little tool arms folded forward, a beam emitter under the chin and four lift pads underneath.

Frame (CLAUDE.md "Vehicle models"): Blender +Z up, +Y = front (Godot -Z), 1 unit = 1 world unit; origin = hover
centre. About 1.9 long, 1.35 wide, 0.95 tall; a building is ~7 across and ~8 tall, a hex 25.

Parts the game uses (ConstructionView / BuildingModels.Exocomp; keep the names):
  Body        empty, parent of everything that bobs
  Emitter     empty at the beam's origin (tip of the chin emitter); EmitterMesh = its barrel + crystal
  Light       the sensor strip on the visor (material "Sensor", emissive)
  Thruster0-3 the lift pads' glowing discs (material "Thruster", emissive)
  ArmL, ArmR  tool-arm pivots at the shoulders
Materials: Hull, Panel, Dark, Steel, Glass, Owner (recoloured per player), Sensor, Thruster, EmitterGlow, Lamp.
The export joins every other mesh into "<pivot>Mesh" under its nearest kept pivot (BodyMesh, ArmLMesh, ...).

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_exocomp.py --game                          (game/models/drones/exocomp.glb)
  /home/user/.bpyvenv/bin/python blender/build_exocomp.py --game --preview blender/previews/drones
Flags: --out <glb>, --res 960, --samples 32, --no-join, --shots hero,work,game,top
"""
import math
import os
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


import bpy  # noqa: E402  first bpy, then bmesh/mathutils
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
GAME = "--game" in argv
OUT = arg("--out") or (os.path.join(REPO, "game", "models", "drones", "exocomp.glb") if GAME else None)
PREVIEW = arg("--preview")
SHOTS = [s.strip() for s in arg("--shots", "hero,work,game,top").split(",") if s.strip()]
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "32"))
JOIN = "--no-join" not in argv

KEEP = {"Body", "Emitter", "ArmL", "ArmR", "Light", "Thruster0", "Thruster1", "Thruster2", "Thruster3"}

# ---------------------------------------------------------------- basics

MT = {}


def material(name, colour, metallic=0.0, roughness=0.5, emission=None, strength=0.0, coat=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes.get("Principled BSDF")
    p.inputs["Base Color"].default_value = (*colour, 1.0)
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = roughness
    if coat:
        p.inputs["Coat Weight"].default_value = coat
    if emission is not None:
        p.inputs["Emission Color"].default_value = (*emission, 1.0)
        p.inputs["Emission Strength"].default_value = strength
    return m


def materials():
    MT.update({
        "hull": material("Hull", (0.83, 0.84, 0.86), 0.15, 0.36, coat=0.35),
        "panel": material("Panel", (0.62, 0.64, 0.67), 0.25, 0.45),
        "dark": material("Dark", (0.07, 0.075, 0.085), 0.4, 0.55),
        "steel": material("Steel", (0.58, 0.6, 0.63), 0.9, 0.28),
        "glass": material("Glass", (0.02, 0.035, 0.05), 0.2, 0.08, emission=(0.05, 0.15, 0.25), strength=0.3, coat=1.0),
        "owner": material("Owner", (0.72, 0.08, 0.06), 0.1, 0.4),
        "sensor": material("Sensor", (0.55, 0.9, 1.0), 0.0, 0.3, emission=(0.45, 0.85, 1.0), strength=6.0),
        "thruster": material("Thruster", (0.3, 0.6, 1.0), 0.0, 0.3, emission=(0.3, 0.62, 1.0), strength=5.0),
        "emit": material("EmitterGlow", (0.7, 0.92, 1.0), 0.0, 0.2, emission=(0.6, 0.9, 1.0), strength=8.0),
        "lamp": material("Lamp", (1.0, 0.6, 0.15), 0.0, 0.3, emission=(1.0, 0.55, 0.12), strength=5.0),
    })


def link(obj, parent=None):
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


def empty(name, loc=(0, 0, 0), parent=None):
    o = bpy.data.objects.new(name, None)
    o.empty_display_size = 0.2
    o.location = loc
    return link(o, parent)


def finish(bm, sharp=35.0):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
    lim = math.radians(sharp)
    for e in bm.edges:
        if len(e.link_faces) != 2 or e.calc_face_angle(0.0) > lim:
            e.smooth = False


def mesh_object(name, bm, mat, parent=None, loc=(0, 0, 0), sharp=35.0):
    finish(bm, sharp)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    o.location = loc
    return link(o, parent)


def box(name, size, loc, mat, parent=None, bevel=0.0, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES", profile=0.5)
    o = mesh_object(name, bm, mat, parent, loc)
    if rot:
        o.rotation_euler = [math.radians(a) for a in rot]
    return o


def cylinder(name, r1, r2, depth, loc, mat, parent=None, segs=16, axis="Z", cap=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=segs, radius1=r1, radius2=r2, depth=depth)
    if axis == "Y":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, "X"))
    elif axis == "X":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    return mesh_object(name, bm, mat, parent, loc, sharp=50.0)


def sphere(name, r, loc, mat, parent=None, segs=16, rings=8, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=r)
    bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
    return mesh_object(name, bm, mat, parent, loc, sharp=80.0)


def strut(name, a, b, r, mat, parent=None, segs=8):
    a, b = Vector(a), Vector(b)
    d = b - a
    o = cylinder(name, r, r, d.length, (a + b) / 2, mat, parent, segs)
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return o


# ---------------------------------------------------------------- hull


def ring(z, half_w, half_l, n, front_taper, segs=40):
    """A superellipse ring at height z: |x/w|^n + |y/l|^n = 1, the front (+Y) half narrowed by front_taper."""
    pts = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        c, s = math.cos(a), math.sin(a)
        x = half_w * math.copysign(abs(c) ** (2 / n), c)
        y = half_l * math.copysign(abs(s) ** (2 / n), s)
        if y > 0:
            x *= 1.0 - front_taper * (y / half_l) ** 2
        pts.append(Vector((x, y, z)))
    return pts


def loft(name, rings_, mat, parent=None, cap_bottom=True, cap_top=True, sharp=35.0):
    bm = bmesh.new()
    layers = [[bm.verts.new(p) for p in r] for r in rings_]
    n = len(rings_[0])
    for k in range(len(layers) - 1):
        lo, hi = layers[k], layers[k + 1]
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    if cap_bottom:
        c = bm.verts.new(sum((v.co for v in layers[0]), Vector()) / n)
        for i in range(n):
            bm.faces.new((layers[0][(i + 1) % n], layers[0][i], c))
    if cap_top:
        c = bm.verts.new(sum((v.co for v in layers[-1]), Vector()) / n + Vector((0, 0, 0.0)))
        for i in range(n):
            bm.faces.new((layers[-1][i], layers[-1][(i + 1) % n], c))
    return mesh_object(name, bm, mat, parent, sharp=sharp)


# (z, half width, half length, squareness, front taper): a flat belly, sides bulging out to the band, a crowned top.
HULL = [
    (-0.30, 0.48, 0.70, 3.2, 0.30),
    (-0.22, 0.60, 0.86, 3.4, 0.32),
    (-0.06, 0.66, 0.94, 3.6, 0.34),
    (0.10, 0.65, 0.93, 3.6, 0.36),
    (0.26, 0.58, 0.84, 3.3, 0.38),
    (0.38, 0.46, 0.68, 3.0, 0.40),
    (0.46, 0.30, 0.46, 2.6, 0.40),
    (0.50, 0.12, 0.20, 2.2, 0.40),
]


def hull_at(z, grow=1.0):
    """The hull's (half width, half length, squareness, taper) at height z, interpolated between the HULL rings."""
    for a, b in zip(HULL, HULL[1:]):
        if a[0] <= z <= b[0]:
            f = (z - a[0]) / (b[0] - a[0])
            w, l, n, t = (a[k] + (b[k] - a[k]) * f for k in range(1, 5))
            return w * grow, l * grow, n, t
    h = HULL[0] if z < HULL[0][0] else HULL[-1]
    return h[1] * grow, h[2] * grow, h[3], h[4]


def hull(body):
    loft("Shell", [ring(*h) for h in HULL], MT["hull"], body)
    # owner band round the widest part, standing a hair proud of the shell
    band = [ring(z, *hull_at(z, 1.014)) for z in (-0.12, -0.06, 0.02)]
    loft("Band", band, MT["owner"], body, cap_bottom=False, cap_top=False)
    # dark belly plate and skirt
    loft("Belly", [ring(-0.36, 0.40, 0.60, 3.0, 0.3), ring(-0.29, 0.50, 0.72, 3.2, 0.3)], MT["dark"], body)
    # face: a curved visor across the front, sensor strip in it
    visor_pts = []
    for z in (0.04, 0.1, 0.17, 0.24):
        w, l, n, t = hull_at(z, 1.012)
        row = []
        for i in range(25):  # the same angular span on every row so the quads line up
            a = math.radians(55 + 70 * i / 24)
            c, sn = math.cos(a), math.sin(a)
            x = w * math.copysign(abs(c) ** (2 / n), c)
            y = l * math.copysign(abs(sn) ** (2 / n), sn)
            x *= 1.0 - t * (y / l) ** 2
            row.append(Vector((x, y, z)))
        visor_pts.append(row)
    bm = bmesh.new()
    rows = [[bm.verts.new(p) for p in row] for row in visor_pts]
    for k in range(len(rows) - 1):
        m = min(len(rows[k]), len(rows[k + 1]))
        for i in range(m - 1):
            bm.faces.new((rows[k][i], rows[k][i + 1], rows[k + 1][i + 1], rows[k + 1][i]))
    mesh_object("Visor", bm, MT["glass"], body)
    face_y = hull_at(0.13, 1.012)[1]
    light = box("Light", (0.46, 0.05, 0.045), (0, face_y + 0.012, 0.13), MT["sensor"], body, bevel=0.012)
    light.data.name = "Light"
    # two small "pupils" brighter in the strip
    for x in (-0.12, 0.12):
        sphere(f"Pupil{'L' if x < 0 else 'R'}", 0.04, (x, face_y + 0.03, 0.13), MT["sensor"], body, segs=10, rings=5, scale=(1, 0.6, 1))
    # brow over the visor
    box("Brow", (0.6, 0.12, 0.05), (0, hull_at(0.28)[1], 0.28), MT["panel"], body, bevel=0.015, rot=(-24, 0, 0))
    return light


def dorsal(body):
    # equipment pod on the back, a small antenna and two amber status lamps at the rear
    box("Pod", (0.36, 0.5, 0.12), (0, -0.18, 0.47), MT["panel"], body, bevel=0.04)
    for i, x in enumerate((-0.11, 0.0, 0.11)):
        box(f"PodVent{i}", (0.05, 0.38, 0.02), (x, -0.18, 0.535), MT["dark"], body)
    strut("Antenna", (0.12, -0.42, 0.44), (0.16, -0.6, 0.95), 0.012, MT["steel"], body, 6)
    sphere("AntennaTip", 0.03, (0.16, -0.6, 0.96), MT["lamp"], body, 8, 4)
    for x in (-0.3, 0.3):
        box(f"RearLamp{'L' if x < 0 else 'R'}", (0.1, 0.03, 0.05), (x, -0.95, 0.1), MT["lamp"], body, bevel=0.01)
    box("RearPlate", (0.5, 0.04, 0.16), (0, -0.955, -0.04), MT["dark"], body, bevel=0.015)
    # side grilles
    for side in (-1, 1):
        for i in range(4):
            box(f"Grille{'L' if side < 0 else 'R'}{i}", (0.03, 0.05, 0.16), (side * hull_at(0.12)[0], -0.32 + i * 0.1, 0.12), MT["dark"], body)


def emitter(body):
    e = empty("Emitter", (0, 1.08, -0.24), body)
    # barrel under the chin, pointing forward; the empty sits at the crystal so the beam starts there
    cylinder("EmitterBarrel", 0.085, 0.06, 0.34, (0, -0.2, 0), MT["steel"], e, 14, axis="Y")
    cylinder("EmitterCollar", 0.1, 0.1, 0.05, (0, -0.32, 0), MT["dark"], e, 14, axis="Y")
    sphere("EmitterCrystal", 0.055, (0, 0.0, 0), MT["emit"], e, 12, 6, scale=(1, 1.4, 1))
    box("EmitterMount", (0.12, 0.2, 0.1), (0, -0.36, 0.08), MT["dark"], e, bevel=0.02)
    return e


def arm(body, side):
    name = "ArmL" if side < 0 else "ArmR"
    a = empty(name, (side * 0.56, 0.56, -0.17), body)
    sphere(f"{name}Shoulder", 0.085, (0, 0, 0), MT["steel"], a, 12, 6)
    elbow = Vector((side * 0.07, 0.2, -0.1))
    wrist = Vector((side * -0.03, 0.44, -0.03))
    strut(f"{name}Upper", (0, 0, 0), elbow, 0.045, MT["panel"], a)
    sphere(f"{name}Elbow", 0.055, elbow, MT["dark"], a, 10, 5)
    strut(f"{name}Fore", elbow, wrist, 0.036, MT["panel"], a)
    # two-finger claw
    for k, dz in enumerate((0.035, -0.035)):
        strut(f"{name}Finger{k}", wrist, wrist + Vector((side * -0.02, 0.1, dz * 1.6)), 0.014, MT["steel"], a, 6)
    sphere(f"{name}Tool", 0.022, wrist + Vector((0, 0.06, 0)), MT["emit"], a, 8, 4)
    return a


def thrusters(body):
    pads = [(-0.36, 0.46), (0.36, 0.46), (-0.36, -0.46), (0.36, -0.46)]
    out = []
    for i, (x, y) in enumerate(pads):
        cylinder(f"Pad{i}", 0.15, 0.13, 0.08, (x, y, -0.36), MT["dark"], body, 18)
        cylinder(f"PadRing{i}", 0.155, 0.155, 0.025, (x, y, -0.33), MT["steel"], body, 18)
        t = cylinder(f"Thruster{i}", 0.11, 0.11, 0.012, (x, y, -0.405), MT["thruster"], body, 18)
        t.data.name = f"Thruster{i}"
        out.append(t)
    return out


def exocomp():
    root = empty("Exocomp")
    body = empty("Body", (0, 0, 0), root)
    hull(body)
    dorsal(body)
    emitter(body)
    arm(body, -1)
    arm(body, 1)
    thrusters(body)
    return root


# ---------------------------------------------------------------- game export


def join_for_game(root):
    meshes = [o for o in root.children_recursive if o.type == "MESH"]

    def pivot_of(o):
        p = o.parent
        while p is not None and p is not root and p.name not in KEEP:
            p = p.parent
        return p or root

    groups = {}
    for o in meshes:
        if o.name in KEEP:
            continue
        groups.setdefault(pivot_of(o), []).append(o)
    for o in meshes:
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
    for piv, objs in groups.items():
        for o in objs:
            mw = o.matrix_world.copy()
            o.parent = piv
            o.matrix_world = mw
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        if len(objs) > 1:
            bpy.ops.object.join()
        target = objs[0]
        target.name = f"{piv.name}Mesh"
        target.data.name = target.name
        target.data.transform(target.matrix_basis)
        target.matrix_basis = Matrix.Identity(4)
    names = sorted(o.name for o in root.children_recursive)
    missing = sorted(KEEP - set(names))
    if missing:
        sys.exit(f"join_for_game lost {missing}")
    print(f"joined for the game: {len(names) + 1} nodes:", ", ".join(names))


def export(root, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_lights=False, export_cameras=False)
    print("exported", path, os.path.getsize(path), "bytes")


# ---------------------------------------------------------------- previews


def stage():
    scene = bpy.context.scene
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.32, 0.38, 0.48, 1.0)
    bg.inputs["Strength"].default_value = 0.35
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = -0.2
    sun = link(bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")))
    sun.data.energy = 3.4
    sun.data.angle = math.radians(3)
    sun.rotation_euler = (math.radians(52), 0, math.radians(140))
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400)
    mesh_object("Ground", bm, material("Ground", (0.34, 0.27, 0.19), 0.0, 0.95), None, (0, 0, -1.6))


def work_set():
    """A half-printed building block (solid below, hologram above) and a glowing beam from the emitter to it."""
    solid = material("BlockSolid", (0.8, 0.81, 0.84), 0.3, 0.4)
    holo = material("BlockHolo", (0.4, 0.75, 1.0), 0.0, 0.3, emission=(0.4, 0.75, 1.0), strength=0.15)
    p = holo.node_tree.nodes.get("Principled BSDF")
    p.inputs["Alpha"].default_value = 0.12
    band = material("BlockBand", (0.6, 0.9, 1.0), 0.0, 0.3, emission=(0.5, 0.85, 1.0), strength=10.0)
    beam = material("BeamGlow", (0.6, 0.9, 1.0), 0.0, 0.3, emission=(0.45, 0.82, 1.0), strength=14.0)
    objs = [
        box("BlockBase", (4.0, 3.0, 1.8), (0, 4.2, -0.7), solid),
        box("BlockBand", (4.04, 3.04, 0.08), (0, 4.2, 0.22), band),
        box("BlockTop", (4.0, 3.0, 1.6), (0, 4.2, 1.06), holo),
    ]
    tip = Vector((0, 1.08, -0.24))
    hit = Vector((0.3, 2.68, 0.22))
    objs.append(strut("Beam", tip, hit, 0.05, beam, None, 10))
    objs.append(sphere("Hit", 0.16, hit, beam, None, 10, 5))
    return objs


def preview(root, folder):
    scene = bpy.context.scene
    stage()
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    cam = link(bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera")))
    scene.camera = cam
    cam.data.clip_end = 2000
    os.makedirs(folder, exist_ok=True)
    work = []
    big = []

    def shoot(name, pos, target, lens=50, ortho=None, w=RES, h=None):
        scene.render.resolution_x = w
        scene.render.resolution_y = h or int(w * 2 / 3)
        if ortho:
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = ortho
        else:
            cam.data.type = "PERSP"
            cam.data.lens = lens
        cam.location = pos
        cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        path = os.path.join(folder, f"exocomp-{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path, flush=True)

    for name in SHOTS:
        for o in work + big:
            o.hide_render = True
        if name == "hero":
            shoot(name, (2.6, 3.4, 1.4), (0, 0.1, 0.0), lens=55)
        elif name == "work":
            if not work:
                work.extend(work_set())
            for o in work:
                o.hide_render = False
            shoot(name, (6.5, -3.0, 3.4), (0.0, 2.4, 0.2), lens=35)
        elif name == "game":
            # the game camera at zoom 50 over a building-sized block, with the drone beside it
            if not big:
                big.append(box("Building", (7.0, 7.0, 8.0), (-6, 2, 2.4), material("Bldg", (0.78, 0.79, 0.82), 0.3, 0.4)))
            for o in big:
                o.hide_render = False
            shoot(name, (0, -26, 42), (-3, 1, 0), lens=35)
        elif name == "top":
            shoot(name, (0, 0.001, 8), (0, 0, 0), ortho=2.6, w=RES, h=RES)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    root = exocomp()
    bpy.context.view_layer.update()
    if OUT:
        if JOIN:
            join_for_game(root)
        export(root, OUT)
    if PREVIEW:
        preview(root, PREVIEW)


main()
