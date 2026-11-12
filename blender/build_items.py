"""Machine Shop item renders (owner 2026-11-12: graphics for each build on the cards; the vector drawings were "too
cartoony, want it more realistic"). Each of the 13 fabrication.json items is built from bevelled primitives with PBR
materials (painted steel with worn edges from Cycles pointiness, bare metal, rubber, PCB, glass, emissive lights), lit
by a three-light studio and rendered with a shadow catcher on a transparent background.

    /home/user/.bpyvenv/bin/python blender/build_items.py [--only id,id] [--res 512] [--samples 48] [--out DIR]

Writes <out>/<id>.png (RGBA, default docs/mockups/machine-shop-panel/items/). The game can use the same PNGs as icons.
"""
import math
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))
OUT = arg("--out") or os.path.join(HERE, "..", "docs", "mockups", "machine-shop-panel", "items")
RES = int(arg("--res", "512"))
SAMPLES = int(arg("--samples", "48"))
ONLY = [s for s in (arg("--only") or "").split(",") if s]


# ---------------------------------------------------------------- scene + materials
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.cycles.device = "CPU"
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = RES
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "AgX - Medium High Contrast"
    w = bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.05, 0.06, 0.08, 1)
    bg.inputs[1].default_value = 0.35
    return sc


def mat(name, col, metal=0.0, rough=0.5, wear=0.0, bump=0.035, emit=None, emit_str=0.0, alpha=1.0, trans=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    N, L = nt.nodes, nt.links
    p = N["Principled BSDF"]
    p.inputs["Metallic"].default_value = metal
    p.inputs["Roughness"].default_value = rough
    if trans:
        p.inputs["Transmission Weight"].default_value = trans
        p.inputs["IOR"].default_value = 1.45
    if alpha < 1:
        p.inputs["Alpha"].default_value = alpha
    # colour variation + fine bump (grime, brushing)
    tc = N.new("ShaderNodeTexCoord")
    noise = N.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 18.0
    noise.inputs["Detail"].default_value = 8.0
    L.new(tc.outputs["Object"], noise.inputs["Vector"])
    var = N.new("ShaderNodeMixRGB")
    var.blend_type = "MULTIPLY"
    var.inputs["Fac"].default_value = 0.12
    var.inputs["Color1"].default_value = (*col, 1)
    L.new(noise.outputs["Color"], var.inputs["Color2"])
    base = var.outputs["Color"]
    if wear > 0:
        geo = N.new("ShaderNodeNewGeometry")
        ramp = N.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.585
        ramp.color_ramp.elements[1].position = 0.64
        wn = N.new("ShaderNodeTexNoise")
        wn.inputs["Scale"].default_value = 40.0
        L.new(tc.outputs["Object"], wn.inputs["Vector"])
        add = N.new("ShaderNodeMath")
        add.operation = "ADD"
        L.new(geo.outputs["Pointiness"], add.inputs[0])
        sub = N.new("ShaderNodeMath")
        sub.operation = "MULTIPLY"
        sub.inputs[1].default_value = 0.08 * wear
        cen = N.new("ShaderNodeMath")
        cen.operation = "SUBTRACT"
        cen.inputs[1].default_value = 0.5
        L.new(wn.outputs["Fac"], cen.inputs[0])
        L.new(cen.outputs[0], sub.inputs[0])
        L.new(sub.outputs[0], add.inputs[1])
        L.new(add.outputs[0], ramp.inputs["Fac"])
        mix = N.new("ShaderNodeMixRGB")
        mix.inputs["Color2"].default_value = (0.55, 0.56, 0.58, 1)
        L.new(ramp.outputs["Color"], mix.inputs["Fac"])
        L.new(base, mix.inputs["Color1"])
        base = mix.outputs["Color"]
        mr = N.new("ShaderNodeMapRange")
        mr.inputs["To Min"].default_value = metal
        mr.inputs["To Max"].default_value = 1.0
        L.new(ramp.outputs["Color"], mr.inputs["Value"])
        L.new(mr.outputs["Result"], p.inputs["Metallic"])
    L.new(base, p.inputs["Base Color"])
    bn = N.new("ShaderNodeBump")
    bn.inputs["Strength"].default_value = bump
    bn.inputs["Distance"].default_value = 0.004
    n2 = N.new("ShaderNodeTexNoise")
    n2.inputs["Scale"].default_value = 90.0
    L.new(tc.outputs["Object"], n2.inputs["Vector"])
    L.new(n2.outputs["Fac"], bn.inputs["Height"])
    L.new(bn.outputs["Normal"], p.inputs["Normal"])
    if emit:
        p.inputs["Emission Color"].default_value = (*emit, 1)
        p.inputs["Emission Strength"].default_value = emit_str
    if alpha < 1:
        m.blend_method = "BLEND"
    return m


def obj(o, m=None, bevel=0.0, segs=3, smooth=True):
    if m:
        o.data.materials.append(m)
    if bevel > 0:
        b = o.modifiers.new("Bevel", "BEVEL")
        b.width = bevel
        b.segments = segs
        b.limit_method = "ANGLE"
    if smooth and hasattr(o.data, "polygons"):
        for p in o.data.polygons:
            p.use_smooth = True
        try:
            o.data.set_sharp_from_angle(angle=math.radians(40))
        except Exception:
            pass
    return o


def cube(loc, size, m, bevel=0.01, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot)
    o = bpy.context.object
    o.scale = (size[0] / 2, size[1] / 2, size[2] / 2)
    bpy.ops.object.transform_apply(scale=True)
    return obj(o, m, bevel)


def cyl(loc, r, depth, m, rot=(0, 0, 0), verts=40, bevel=0.004):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    return obj(bpy.context.object, m, bevel)


def torus(loc, R, r, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(location=loc, rotation=rot, major_radius=R, minor_radius=r, major_segments=64, minor_segments=20)
    return obj(bpy.context.object, m)


def sphere(loc, r, m):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=16, ring_count=8)
    return obj(bpy.context.object, m)


def text(s, loc, rot, size, m, extrude=0.0008):
    bpy.ops.object.text_add(location=loc, rotation=rot)
    t = bpy.context.object
    t.data.body = s
    t.data.size = size
    t.data.extrude = extrude
    t.data.align_x = "CENTER"
    t.data.align_y = "CENTER"
    t.data.materials.append(m)
    return t


def gear(loc, r, thick, teeth, m, rot=(0, 0, 0)):
    """A spur gear: a hub disc plus tooth blocks, joined."""
    parts = [cyl((0, 0, 0), r, thick, m, verts=64, bevel=0.002)]
    for i in range(teeth):
        a = 2 * math.pi * i / teeth
        t = cube((math.cos(a) * (r + 0.012), math.sin(a) * (r + 0.012), 0), (0.03, 0.022, thick), m, bevel=0.003, rot=(0, 0, a))
        parts.append(t)
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    for p in parts:
        bpy.context.view_layer.objects.active = p
        for md in list(p.modifiers):
            bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    g = bpy.context.object
    g.location = loc
    g.rotation_euler = rot
    hole = cyl(loc, r * 0.28, thick * 1.2, MAT["dark"], rot=rot, verts=32)
    return g


# ---------------------------------------------------------------- items
MAT = {}


def mats():
    MAT.update(
        steel=mat("Steel", (0.55, 0.57, 0.6), metal=1.0, rough=0.32, bump=0.02),
        dark=mat("DarkSteel", (0.09, 0.1, 0.11), metal=0.8, rough=0.45, bump=0.03),
        brass=mat("Brass", (0.78, 0.58, 0.28), metal=1.0, rough=0.3),
        rubber=mat("Rubber", (0.03, 0.03, 0.035), metal=0.0, rough=0.8, bump=0.02),
        stencil=mat("Stencil", (0.92, 0.9, 0.84), rough=0.6),
        label=mat("Label", (0.06, 0.07, 0.08), rough=0.55),
        pcb=mat("PCB", (0.05, 0.26, 0.13), rough=0.35, bump=0.02),
        chip=mat("Chip", (0.04, 0.04, 0.05), rough=0.3),
        gold=mat("Gold", (0.95, 0.72, 0.3), metal=1.0, rough=0.25),
        glass=mat("Glass", (0.6, 0.8, 0.95), rough=0.05, trans=0.9),
        blue=mat("BlueGlow", (0.2, 0.6, 1.0), rough=0.2, emit=(0.25, 0.65, 1.0), emit_str=6.0),
        green=mat("GreenLed", (0.2, 1.0, 0.4), emit=(0.3, 1.0, 0.45), emit_str=8.0),
        amber=mat("AmberLed", (1.0, 0.6, 0.1), emit=(1.0, 0.55, 0.1), emit_str=8.0),
        white=mat("WhitePaint", (0.78, 0.8, 0.82), rough=0.45, wear=0.6),
        pallet=mat("Pallet", (0.2, 0.22, 0.25), metal=0.6, rough=0.5, wear=0.5),
        teal=mat("Seal", (0.12, 0.42, 0.5), rough=0.6, bump=0.03),
    )


def crate(col, label):
    m = mat("Paint" + label, col, metal=0.15, rough=0.42, wear=1.0)
    w, d, h = 0.62, 0.44, 0.40
    cube((0, 0, h / 2), (w, d, h), m, bevel=0.018)
    # lid seam + ribs
    cube((0, 0, h - 0.06), (w + 0.006, d + 0.006, 0.012), MAT["dark"], bevel=0.003)
    for x in (-w / 2 + 0.07, w / 2 - 0.07):
        cube((x, 0, h / 2), (0.03, d + 0.012, h - 0.02), m, bevel=0.006)
    # corner guards
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z in (0.035, h - 0.035):
                cube((sx * (w / 2 - 0.02), sy * (d / 2 - 0.02), z), (0.07, 0.07, 0.07), MAT["dark"], bevel=0.008)
    # latches + handles
    for x in (-0.13, 0.13):
        cube((x, -d / 2 - 0.006, h - 0.085), (0.06, 0.016, 0.05), MAT["steel"], bevel=0.004)
    for sx in (-1, 1):
        cyl((sx * (w / 2 + 0.01), 0, h * 0.62), 0.012, 0.16, MAT["dark"], rot=(math.radians(90), 0, 0), verts=16)
    # stencil label panel on the front
    cube((0.0, -d / 2 - 0.003, h * 0.42), (0.3, 0.004, 0.11), MAT["label"], bevel=0.002)
    text(label, (0.0, -d / 2 - 0.006, h * 0.42), (math.radians(90), 0, 0), 0.055, MAT["stencil"])
    text("DUNMORE · SPARES", (0.0, -d / 2 - 0.006, h * 0.27), (math.radians(90), 0, 0), 0.026, MAT["stencil"])
    return (w, d, h)


def truss():
    L = 0.9
    for z in (0.03, 0.27):
        for y in (-0.09, 0.09):
            cube((0, y, z), (L, 0.035, 0.035), MAT["steel"], bevel=0.005)
    n = 6
    for i in range(n + 1):
        x = -L / 2 + L * i / n
        for y in (-0.09, 0.09):
            cube((x, y, 0.15), (0.025, 0.025, 0.24), MAT["steel"], bevel=0.004)
        if i < n:
            for y in (-0.09, 0.09):
                a = math.atan2(0.24, L / n)
                cube((x + L / n / 2, y, 0.15), (math.hypot(L / n, 0.24), 0.02, 0.02), MAT["steel"], bevel=0.003, rot=(0, -a if i % 2 else a, 0))
        cube((x, 0, 0.03), (0.022, 0.18, 0.022), MAT["steel"], bevel=0.003)
    for x in (-L / 2 - 0.02, L / 2 + 0.02):
        cube((x, 0, 0.15), (0.03, 0.24, 0.3), MAT["brass"], bevel=0.006)
    return (L, 0.3, 0.3)


def plating():
    for i in range(4):
        z = 0.03 + i * 0.045
        p = cube((0.01 * (i % 2), 0.008 * i, z), (0.62, 0.44, 0.03), MAT["white"], bevel=0.006)
        p.rotation_euler = (0, 0, math.radians(3 * (i - 1.5)))
        for x in (-0.27, 0.0, 0.27):
            for y in (-0.18, 0.18):
                sphere((x + 0.01 * (i % 2), y + 0.008 * i, z + 0.016), 0.011, MAT["steel"])
    return (0.65, 0.48, 0.2)


def mechanism():
    cube((0, 0, 0.04), (0.55, 0.3, 0.08), MAT["pallet"], bevel=0.01)
    gear((-0.1, 0.0, 0.26), 0.15, 0.04, 22, MAT["steel"], rot=(math.radians(90), 0, 0))
    gear((0.15, -0.02, 0.2), 0.09, 0.04, 14, MAT["brass"], rot=(math.radians(90), 0, 0))
    cyl((-0.1, 0.06, 0.26), 0.02, 0.18, MAT["steel"], rot=(math.radians(90), 0, 0))
    cyl((0.15, 0.04, 0.2), 0.018, 0.16, MAT["steel"], rot=(math.radians(90), 0, 0))
    for x in (-0.1, 0.15):
        cube((x, 0.11, 0.14), (0.06, 0.03, 0.2 if x < 0 else 0.13), MAT["dark"], bevel=0.005)
    return (0.55, 0.3, 0.42)


def coupling():
    r = (0, math.radians(90), 0)
    cyl((0, 0, 0.12), 0.09, 0.42, MAT["steel"], rot=r)
    cyl((0, 0, 0.12), 0.097, 0.07, MAT["blue"], rot=r)
    for x in (-0.15, 0.15):
        cyl((x, 0, 0.12), 0.1, 0.04, MAT["dark"], rot=r)
        for k in range(6):
            a = 2 * math.pi * k / 6
            cyl((x * 1.05, math.cos(a) * 0.075, 0.12 + math.sin(a) * 0.075), 0.01, 0.05, MAT["steel"], rot=r, verts=6)
    for x in (-0.3, 0.3):
        cyl((x, 0, 0.12), 0.035, 0.18, MAT["rubber"], rot=r, verts=24)
    return (0.7, 0.2, 0.24)


def control_module():
    cube((0, 0, 0.05), (0.5, 0.36, 0.1), MAT["dark"], bevel=0.012)
    cube((0, 0, 0.105), (0.44, 0.3, 0.012), MAT["pcb"], bevel=0.002)
    cube((-0.08, 0.02, 0.12), (0.12, 0.12, 0.02), MAT["chip"], bevel=0.003)
    for x, y, sx, sy in ((0.1, 0.07, 0.08, 0.05), (0.1, -0.06, 0.08, 0.05), (-0.12, -0.1, 0.1, 0.04)):
        cube((x, y, 0.118), (sx, sy, 0.014), MAT["chip"], bevel=0.002)
    for i in range(10):
        cube((-0.17 + i * 0.035, 0.155, 0.112), (0.012, 0.03, 0.004), MAT["gold"], bevel=0.0)
    for i, m in enumerate(("green", "green", "amber")):
        sphere((0.15 + i * 0.03, -0.181, 0.06), 0.008, MAT[m])
    return (0.5, 0.36, 0.14)


def seal_kit():
    for i, (R, r, m) in enumerate(((0.16, 0.03, "rubber"), (0.13, 0.025, "teal"), (0.1, 0.02, "rubber"))):
        torus((-0.1 + i * 0.03, -0.02 + i * 0.03, 0.03 + i * 0.045), R, r, MAT[m])
    cyl((0.22, 0.05, 0.17), 0.045, 0.3, MAT["white"])
    cyl((0.22, 0.05, 0.34), 0.025, 0.05, MAT["teal"])
    cube((0.22, 0.004, 0.17), (0.07, 0.004, 0.1), MAT["label"], bevel=0.001)
    return (0.6, 0.4, 0.36)


def prefab():
    cube((0, 0, 0.22), (0.62, 0.42, 0.44), MAT["white"], bevel=0.02)
    cube((-0.12, -0.212, 0.27), (0.2, 0.01, 0.15), MAT["dark"], bevel=0.004)
    cube((-0.12, -0.217, 0.27), (0.17, 0.006, 0.12), MAT["glass"], bevel=0.002)
    cube((0.15, -0.212, 0.2), (0.12, 0.01, 0.3), MAT["dark"], bevel=0.004)
    for z in (0.03, 0.42):
        cube((0, 0, z), (0.64, 0.44, 0.03), MAT["pallet"], bevel=0.004)
    for i in range(4):
        cube((0.312, -0.15 + i * 0.1, 0.22), (0.006, 0.06, 0.36), MAT["dark"], bevel=0.002)
    return (0.64, 0.44, 0.44)


def machinery():
    cube((0, 0, 0.04), (0.62, 0.4, 0.08), MAT["pallet"], bevel=0.008)
    cyl((-0.12, 0, 0.22), 0.12, 0.26, MAT["white"], rot=(0, math.radians(90), 0))
    for i in range(7):
        cyl((-0.2 + i * 0.027, 0, 0.22), 0.13, 0.008, MAT["dark"], rot=(0, math.radians(90), 0))
    cyl((0.05, 0, 0.22), 0.025, 0.12, MAT["steel"], rot=(0, math.radians(90), 0))
    cyl((0.2, 0.05, 0.25), 0.06, 0.34, MAT["brass"])
    cyl((0.2, 0.05, 0.47), 0.025, 0.14, MAT["steel"])
    cube((0.2, -0.1, 0.16), (0.12, 0.08, 0.16), MAT["dark"], bevel=0.01)
    return (0.62, 0.4, 0.55)


def drone():
    cube((0, 0, 0.12), (0.22, 0.16, 0.08), MAT["white"], bevel=0.02)
    sphere((0, -0.085, 0.11), 0.03, MAT["glass"])
    for sx in (-1, 1):
        for sy in (-1, 1):
            a = math.atan2(sy, sx)
            cube((sx * 0.13, sy * 0.1, 0.13), (0.22, 0.025, 0.02), MAT["dark"], bevel=0.004, rot=(0, 0, a))
            cyl((sx * 0.21, sy * 0.16, 0.15), 0.025, 0.04, MAT["dark"])
            cyl((sx * 0.21, sy * 0.16, 0.175), 0.11, 0.004, MAT["glass"], verts=48)
            cube((sx * 0.21, sy * 0.16, 0.178), (0.2, 0.018, 0.004), MAT["rubber"], bevel=0.001, rot=(0, 0, a + 0.6))
    for sx in (-1, 1):
        cube((sx * 0.06, 0, 0.04), (0.02, 0.18, 0.012), MAT["dark"], bevel=0.003)
        for sy in (-1, 1):
            cube((sx * 0.06, sy * 0.06, 0.07), (0.012, 0.012, 0.06), MAT["dark"], bevel=0.002)
    sphere((0.09, 0.0, 0.165), 0.008, MAT["amber"])
    return (0.6, 0.5, 0.2)


ITEMS = {
    "mechanical_spares": lambda: crate((0.78, 0.5, 0.1), "MECH"),
    "electrical_spares": lambda: crate((0.8, 0.68, 0.12), "ELEC"),
    "rover_spares": lambda: crate((0.8, 0.33, 0.1), "ROVER"),
    "environmental_spares": lambda: crate((0.12, 0.5, 0.6), "ENVIRO"),
    "structural_frame": truss,
    "hull_plating": plating,
    "mechanism": mechanism,
    "power_coupling": coupling,
    "control_module": control_module,
    "seal_kit": seal_kit,
    "prefab_module": prefab,
    "machine_set_placeholder": None,
    "machinery_set": machinery,
    "scout_drone": drone,
}


def stage(size):
    w, d, h = size
    # shadow catcher floor
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
    bpy.context.object.is_shadow_catcher = True
    # lights
    def area(name, loc, power, size, col, target=(0, 0, h * 0.4)):
        bpy.ops.object.light_add(type="AREA", location=loc)
        L = bpy.context.object
        L.data.energy = power
        L.data.size = size
        L.data.color = col
        d_ = [target[i] - loc[i] for i in range(3)]
        L.rotation_euler = (math.atan2(math.hypot(d_[0], d_[1]), -d_[2]), 0, math.atan2(d_[1], d_[0]) - math.pi / 2)
    area("Key", (-1.2, -1.4, 1.6), 55, 1.4, (1.0, 0.95, 0.88))
    area("Rim", (1.3, 1.2, 1.1), 45, 1.0, (0.75, 0.85, 1.0))
    area("Fill", (1.6, -1.2, 0.5), 14, 2.0, (0.85, 0.9, 1.0))
    # camera: 3/4 from the front left, 28 deg down, fitted to the item
    bpy.ops.object.camera_add()
    cam = bpy.context.object
    bpy.context.scene.camera = cam
    cam.data.lens = 70
    span = max(w, d, h) * 1.25 + 0.08
    dist = span * 2.6
    az, el = math.radians(-35), math.radians(26)
    tgt = (0, 0, h * 0.42)
    cam.location = (tgt[0] + dist * math.cos(el) * math.sin(az), tgt[1] - dist * math.cos(el) * math.cos(az), tgt[2] + dist * math.sin(el))
    dx, dy, dz = (tgt[i] - cam.location[i] for i in range(3))
    cam.rotation_euler = (math.atan2(math.hypot(dx, dy), -dz), 0, math.atan2(dy, dx) - math.pi / 2)


def main():
    os.makedirs(OUT, exist_ok=True)
    for iid, build in ITEMS.items():
        if build is None or (ONLY and iid not in ONLY):
            continue
        sc = reset()
        mats()
        size = build()
        stage(size)
        path = os.path.join(OUT, iid + ".png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path, os.path.getsize(path))


if __name__ == "__main__":
    main()
