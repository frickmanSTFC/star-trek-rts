"""Federation Colony Hub, level 1: the 20-second colony pod drop, built in Blender.

Builds the whole scene from scratch (plot, colony pod, drop ship, beams, transporter
effects, camera, lights) and keys the timeline used in animation/colony-pod-drop.html.

Run inside Blender 5.2:  Scripting tab -> open this file -> Run Script
Or from a terminal:      blender --background --python build_colony_pod.py -- --out colony_pod.blend
Preview frames:          ... -- --preview previews/ --frames 4.5,8.0,12.0 --res 640 [--zoom 4]
"""
import math
import random
import sys

import bpy

FPS = 30
DURATION = 20.0
POD_Z = 6.5          # saucer underside height once standing on its legs
POD_LOW = 2.0        # height while folded on its belly
SHIP_Z = 64.0
COMPACT = 0.45       # saucer scale while folded
T0, T1 = 11.3, 15.5  # construction beam laps
TX = 0.9             # transporter materialization length (s)


def F(t):
    return round(t * FPS) + 1


# 2D design coordinates (0-120 plot, hub at 52,48) -> Blender: X = y - 48, Y = x - 52
def W(x, y):
    return (y - 48.0, x - 52.0)


def bangle(deg2d):
    """Angle in the 2D design -> Blender angle (radians) around +Z from +X."""
    return math.radians(90.0 - deg2d)


def ang2d(x, y):
    return math.degrees(math.atan2(y - 48.0, x - 52.0)) % 360


PETALS = (100, 145, 190, 235, 280, 325, 10)
CAPSULES = [(76, 34), (30, 66), (80, 64)]
BEACONS = [(28, 26), (84, 26), (28, 84), (86, 84)]
WALKERS = [((66, 62)), ((69, 66)), ((73, 57)), ((78, 70)), ((62, 70))]


# ------------------------------------------------------------------ helpers
def reset_scene():
    if bpy.app.background:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    else:
        # inside the UI a factory reset would unload this running script, so empty the file instead
        if bpy.context.object and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.lights,
                     bpy.data.cameras, bpy.data.worlds, bpy.data.actions, bpy.data.curves):
            for block in list(coll):
                coll.remove(block)
        for c in list(bpy.context.scene.collection.children):
            bpy.data.collections.remove(c)
    sc = bpy.context.scene
    sc.cursor.location = (0, 0, 0)
    sc.render.fps = FPS
    sc.frame_start, sc.frame_end = 1, F(DURATION)
    sc.unit_settings.system = "METRIC"
    return sc


def link(obj, parent=None):
    if obj.name not in bpy.context.scene.collection.objects:
        bpy.context.scene.collection.objects.link(obj) if obj.users_collection == () else None
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse.identity()
    return obj


def mesh_obj(name, verts, faces, parent=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return link(ob, parent)


def prim(kind, name, parent=None, **kw):
    getattr(bpy.ops.mesh, "primitive_" + kind + "_add")(**kw)
    ob = bpy.context.active_object
    ob.name = name
    return link(ob, parent)


def empty(name, loc=(0, 0, 0), parent=None):
    ob = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    return link(ob, parent)


def smooth(ob, bevel=0.0, segments=2):
    for p in ob.data.polygons:
        p.use_smooth = True
    if bevel:
        m = ob.modifiers.new("Bevel", "BEVEL")
        m.width, m.segments = bevel, segments
        m.limit_method = "ANGLE"
    return ob


def lathe(name, profile, segs=96, parent=None, sharp=35):
    """Surface of revolution around Z from (r, z) points; r = 0 closes the end."""
    import bmesh
    verts, rings = [], []
    for r, z in profile:
        if r == 0:
            rings.append([len(verts)])
            verts.append((0, 0, z))
        else:
            rings.append(list(range(len(verts), len(verts) + segs)))
            verts += [(r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z) for k in range(segs)]
    faces = []
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            k1 = (k + 1) % segs
            if len(a) == 1:
                faces.append((a[0], b[k], b[k1]))
            elif len(b) == 1:
                faces.append((a[k], a[k1], b[0]))
            else:
                faces.append((a[k], a[k1], b[k1], b[k]))
    ob = mesh_obj(name, verts, faces, parent)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    smooth(ob)
    ob.data.set_sharp_from_angle(angle=math.radians(sharp))
    return ob


def profile_z(profile, r):
    """Height of a lathe profile's upper surface at radius r."""
    for (r0, z0), (r1, z1) in zip(profile, profile[1:]):
        if r0 <= r <= r1 and r1 > r0:
            return z0 + (z1 - z0) * (r - r0) / (r1 - r0)
    return profile[0][1]


def on_slope(name, parent, profile, shapes, lift=0.03):
    """Flat decals lying on a lathe's upper surface. shapes: list of (angle, [(r, t), ...]) polygons,
    r along the radius and t sideways (same units), fanned from the first point."""
    verts, faces = [], []
    for a, poly in shapes:
        c, s_ = math.cos(a), math.sin(a)
        base = len(verts)
        for r, t in poly:
            verts.append((r * c - t * s_, r * s_ + t * c, profile_z(profile, r) + lift))
        faces += [(base, base + i, base + i + 1) for i in range(1, len(poly) - 1)]
    return mesh_obj(name, verts, faces, parent)


def ring_quads(name, parent, n, r, z0, z1, hw, phase=0.0):
    """n small rectangles on a vertical cylinder wall (windows, doors, panels)."""
    verts, faces = [], []
    for k in range(n):
        a = 2 * math.pi * (k + phase) / n
        c, s_ = math.cos(a), math.sin(a)
        base = len(verts)
        for dz in (z0, z1):
            for dt in (-hw, hw):
                verts.append((r * c - dt * s_, r * s_ + dt * c, dz))
        faces.append((base, base + 1, base + 3, base + 2))
    return mesh_obj(name, verts, faces, parent)


def set_mat(ob, mat):
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    return ob


def key(ob, path, value, t, interp="BEZIER", index=-1):
    """Set ob.<path> (or a custom property '["name"]') to value and key it at time t."""
    prefs = bpy.context.preferences.edit
    old = prefs.keyframe_new_interpolation_type
    prefs.keyframe_new_interpolation_type = interp
    if path.startswith('["'):
        ob[path[2:-2]] = value
    elif index >= 0:
        getattr(ob, path)[index] = value
    else:
        setattr(ob, path, value)
    ob.keyframe_insert(data_path=path, frame=F(t), index=index)
    prefs.keyframe_new_interpolation_type = old


def prop(ob, name, value):
    ob[name] = float(value)
    return ob


# ------------------------------------------------------------------ materials
def nodes_of(mat):
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    return nt, nt.nodes, nt.links


def attr(nodes, name):
    a = nodes.new("ShaderNodeAttribute")
    a.attribute_type = "OBJECT"
    a.attribute_name = name
    return a.outputs["Fac"]


def math_node(nodes, links, op, a, b=None, clamp=False):
    m = nodes.new("ShaderNodeMath")
    m.operation = op
    m.use_clamp = clamp
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            m.inputs[i].default_value = v
        else:
            links.new(v, m.inputs[i])
    return m.outputs[0]


def mix(nodes, links, fac, s1, s2):
    m = nodes.new("ShaderNodeMixShader")
    if isinstance(fac, (int, float)):
        m.inputs[0].default_value = fac
    else:
        links.new(fac, m.inputs[0])
    links.new(s1, m.inputs[1])
    links.new(s2, m.inputs[2])
    return m.outputs[0]


def emission(nodes, color, strength):
    e = nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = (*color, 1)
    e.inputs["Strength"].default_value = strength
    return e


def frame_driver(socket, expr):
    fcs = socket.driver_add("default_value")
    for fc in fcs if isinstance(fcs, list) else [fcs]:
        fc.driver.type = "SCRIPTED"
        fc.driver.expression = expr


def hex_rgb(h):
    h = h.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)


CYAN = hex_rgb("#7fd4ff")
WHITE_CYAN = hex_rgb("#d8f2ff")


def mat_tx(name, color, rough=0.4, metal=0.0, emit=None, emit_strength=0.0, transmission=0.0, lit_prop=False, panels=0.0, cells=None):
    """Principled surface with a transporter dissolve driven by the object's ["tx"] (0 = gone, 1 = solid).

    If lit_prop, emission strength is multiplied by the object's ["lit"].
    panels > 0 adds hull plating: panel seams and slight per-plate tone changes at that scale.
    cells = (scale, line colour) draws a rectangular cell grid in object XY (solar panels).
    """
    mat = bpy.data.materials.new(name)
    nt, nodes, links = nodes_of(mat)
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (*hex_rgb(color), 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if panels:
        pc = nodes.new("ShaderNodeTexCoord")
        seams = nodes.new("ShaderNodeTexVoronoi")
        seams.feature = "DISTANCE_TO_EDGE"
        seams.inputs["Scale"].default_value = panels
        plates = nodes.new("ShaderNodeTexVoronoi")
        plates.inputs["Scale"].default_value = panels
        for v in (seams, plates):
            links.new(pc.outputs["Object"], v.inputs["Vector"])
        line = math_node(nodes, links, "LESS_THAN", seams.outputs["Distance"], 0.018)
        tone = math_node(nodes, links, "MULTIPLY_ADD", plates.outputs["Distance"], 0.08)
        nodes[-1].inputs[2].default_value = 0.95
        shade = math_node(nodes, links, "MULTIPLY", tone, math_node(nodes, links, "SUBTRACT", 1.0, math_node(nodes, links, "MULTIPLY", line, 0.16)))
        tint = nodes.new("ShaderNodeMix")
        tint.data_type, tint.blend_type = "RGBA", "MULTIPLY"
        tint.inputs["Factor"].default_value = 1.0
        tint.inputs["A"].default_value = (*hex_rgb(color), 1)
        links.new(shade, tint.inputs["B"])
        links.new(tint.outputs["Result"], bsdf.inputs["Base Color"])
        links.new(math_node(nodes, links, "MULTIPLY_ADD", line, 0.15), bsdf.inputs["Roughness"])
        nodes[-1].inputs[2].default_value = rough
    if cells:
        cc = nodes.new("ShaderNodeTexCoord")
        brick = nodes.new("ShaderNodeTexBrick")
        brick.offset, brick.squash = 0.0, 1.0
        brick.inputs["Scale"].default_value = cells[0]
        brick.inputs["Mortar Size"].default_value = 0.025
        brick.inputs["Brick Width"].default_value = 0.5
        brick.inputs["Row Height"].default_value = 0.5
        brick.inputs["Color1"].default_value = (*hex_rgb(color), 1)
        brick.inputs["Color2"].default_value = tuple(c * 0.85 for c in hex_rgb(color)) + (1,)
        brick.inputs["Mortar"].default_value = (*hex_rgb(cells[1]), 1)
        links.new(cc.outputs["Object"], brick.inputs["Vector"])
        links.new(brick.outputs["Color"], bsdf.inputs["Base Color"])
    if transmission:
        bsdf.inputs["Transmission Weight"].default_value = transmission
        bsdf.inputs["IOR"].default_value = 1.45
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*hex_rgb(emit), 1)
        if lit_prop:
            links.new(math_node(nodes, links, "MULTIPLY", attr(nodes, "lit"), emit_strength), bsdf.inputs["Emission Strength"])
        else:
            bsdf.inputs["Emission Strength"].default_value = emit_strength
    tx = attr(nodes, "tx")
    coord = nodes.new("ShaderNodeTexCoord")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.6
    noise.inputs["Detail"].default_value = 3.0
    links.new(coord.outputs["Object"], noise.inputs["Vector"])
    thr = math_node(nodes, links, "MULTIPLY_ADD", tx, 1.3)
    nodes[-1].inputs[2].default_value = -0.15
    solid = math_node(nodes, links, "LESS_THAN", noise.outputs["Fac"], thr)
    inner = math_node(nodes, links, "LESS_THAN", noise.outputs["Fac"], math_node(nodes, links, "SUBTRACT", thr, 0.07))
    edge = math_node(nodes, links, "SUBTRACT", solid, inner, clamp=True)
    transparent = nodes.new("ShaderNodeBsdfTransparent")
    glow = emission(nodes, WHITE_CYAN, 14.0)
    body = mix(nodes, links, solid, transparent.outputs[0], bsdf.outputs[0])
    # bright silhouette while materializing: fades as tx -> 1
    sil = math_node(nodes, links, "MULTIPLY", solid, math_node(nodes, links, "SUBTRACT", 1.0, tx, clamp=True))
    body = mix(nodes, links, math_node(nodes, links, "MULTIPLY", sil, 0.85), body, emission(nodes, CYAN, 6.0).outputs[0])
    final = mix(nodes, links, edge, body, glow.outputs[0])
    links.new(final, out.inputs["Surface"])
    return mat


def mat_glow(name, color, strength):
    mat = bpy.data.materials.new(name)
    nt, nodes, links = nodes_of(mat)
    out = nodes.new("ShaderNodeOutputMaterial")
    e = emission(nodes, hex_rgb(color), strength)
    links.new(e.outputs[0], out.inputs["Surface"])
    return mat


def mat_glow_prop(name, color, strength, propname):
    """Emission scaled by the object's [propname]; transparent where it is 0."""
    mat = bpy.data.materials.new(name)
    nt, nodes, links = nodes_of(mat)
    out = nodes.new("ShaderNodeOutputMaterial")
    a = attr(nodes, propname)
    e = emission(nodes, hex_rgb(color), 1.0)
    links.new(math_node(nodes, links, "MULTIPLY", a, strength), e.inputs["Strength"])
    tr = nodes.new("ShaderNodeBsdfTransparent")
    links.new(mix(nodes, links, math_node(nodes, links, "MINIMUM", a, 1.0), tr.outputs[0], e.outputs[0]), out.inputs["Surface"])
    return mat


def mat_fade(name, color, rough=0.9):
    """Diffuse surface whose opacity is the object's ["fade"]."""
    mat = bpy.data.materials.new(name)
    nt, nodes, links = nodes_of(mat)
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (*hex_rgb(color), 1)
    bsdf.inputs["Roughness"].default_value = rough
    tr = nodes.new("ShaderNodeBsdfTransparent")
    links.new(mix(nodes, links, attr(nodes, "fade"), tr.outputs[0], bsdf.outputs[0]), out.inputs["Surface"])
    return mat


def mat_column():
    """Transporter column: rising sparkles in a soft vertical glow, opacity from the object's ["glow"]."""
    mat = bpy.data.materials.new("TransporterColumn")
    nt, nodes, links = nodes_of(mat)
    out = nodes.new("ShaderNodeOutputMaterial")
    g = attr(nodes, "glow")
    coord = nodes.new("ShaderNodeTexCoord")
    sep = nodes.new("ShaderNodeSeparateXYZ")
    links.new(coord.outputs["Generated"], sep.inputs[0])
    z = sep.outputs["Z"]
    band = math_node(nodes, links, "MULTIPLY", math_node(nodes, links, "MULTIPLY", z, math_node(nodes, links, "SUBTRACT", 1.0, z)), 4.0)
    noise = nodes.new("ShaderNodeTexNoise")
    noise.noise_dimensions = "4D"
    noise.inputs["Scale"].default_value = 22.0
    noise.inputs["Detail"].default_value = 1.0
    frame_driver(noise.inputs["W"], "frame/7")
    mapping = nodes.new("ShaderNodeMapping")
    links.new(coord.outputs["Object"], mapping.inputs["Vector"])
    frame_driver(mapping.inputs["Location"], "-frame/40")  # all axes drift; reads as rising shimmer
    links.new(mapping.outputs[0], noise.inputs["Vector"])
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
    ramp.color_ramp.elements[1].position = 0.68
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    spark = ramp.outputs["Color"]
    alpha = math_node(nodes, links, "MULTIPLY", math_node(nodes, links, "MULTIPLY", band, g),
                      math_node(nodes, links, "ADD", 0.18, spark))
    e = emission(nodes, WHITE_CYAN, 1.0)
    links.new(math_node(nodes, links, "MULTIPLY", math_node(nodes, links, "ADD", 2.0, math_node(nodes, links, "MULTIPLY", spark, 14.0)), g), e.inputs["Strength"])
    tr = nodes.new("ShaderNodeBsdfTransparent")
    links.new(mix(nodes, links, math_node(nodes, links, "MINIMUM", alpha, 1.0), tr.outputs[0], e.outputs[0]), out.inputs["Surface"])
    return mat


def mat_tractor():
    """Tractor beam cone: rings sliding down a soft glow, opacity from the object's ["beam"]."""
    mat = bpy.data.materials.new("TractorBeam")
    nt, nodes, links = nodes_of(mat)
    out = nodes.new("ShaderNodeOutputMaterial")
    b = attr(nodes, "beam")
    coord = nodes.new("ShaderNodeTexCoord")
    wave = nodes.new("ShaderNodeTexWave")
    wave.wave_type = "BANDS"
    wave.bands_direction = "Z"
    wave.inputs["Scale"].default_value = 3.0
    frame_driver(wave.inputs["Phase Offset"], "frame/5")
    links.new(coord.outputs["Generated"], wave.inputs["Vector"])
    rings = math_node(nodes, links, "POWER", wave.outputs["Fac"], 6.0)
    alpha = math_node(nodes, links, "MULTIPLY", b, math_node(nodes, links, "ADD", 0.05, math_node(nodes, links, "MULTIPLY", rings, 0.28)))
    e = emission(nodes, WHITE_CYAN, 1.0)
    links.new(math_node(nodes, links, "MULTIPLY", b, 2.5), e.inputs["Strength"])
    tr = nodes.new("ShaderNodeBsdfTransparent")
    links.new(mix(nodes, links, math_node(nodes, links, "MINIMUM", alpha, 1.0), tr.outputs[0], e.outputs[0]), out.inputs["Surface"])
    return mat


def mat_plain(name, color, rough=0.6, metal=0.0):
    mat = bpy.data.materials.new(name)
    nt, nodes, links = nodes_of(mat)
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (*hex_rgb(color), 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    links.new(bsdf.outputs[0], out.inputs["Surface"])
    return mat


# ------------------------------------------------------------------ scene
def build():
    sc = reset_scene()
    random.seed(11)

    M = {
        "hull": mat_tx("Hull", "#e8ecf2", 0.32, 0.15, panels=0.55),
        "hull_dk": mat_tx("HullDark", "#9aa4b4", 0.45, 0.3),
        "under": mat_tx("Underside", "#7d889a", 0.5, 0.35, panels=0.7),
        "trim": mat_tx("TrimDark", "#4b5463", 0.5, 0.5),
        "green": mat_tx("GreenLight", "#4ae07a", 0.3, 0.0, emit="#4ae07a", emit_strength=10.0),
        "strobe": mat_tx("Strobe", "#ffffff", 0.3, 0.0, emit="#ffffff", emit_strength=40.0, lit_prop=True),
        "landing": mat_tx("LandingLights", "#fff2cc", 0.3, 0.0, emit="#fff2cc", emit_strength=16.0, lit_prop=True),
        "glass": mat_tx("DomeGlass", "#bfe4ff", 0.05, 0.0, emit="#9fd8ff", emit_strength=3.0, transmission=0.85, lit_prop=True),
        "windows": mat_tx("Windows", "#3a3a30", 0.4, 0.0, emit="#ffd98a", emit_strength=6.0, lit_prop=True),
        "stripe": mat_tx("StarfleetBlue", "#3f78e0", 0.35, 0.1, emit="#4a7fd6", emit_strength=2.5),
        "glowring": mat_tx("GlowRing", "#7fd4ff", 0.3, 0.0, emit="#7fd4ff", emit_strength=8.0),
        "red": mat_tx("RedLight", "#e0574a", 0.3, 0.0, emit="#e0574a", emit_strength=10.0),
        "solar": mat_tx("Solar", "#1f3a66", 0.25, 0.6, cells=(1.0, "#9fb2c8")),
        "column": mat_column(),
        "tractor": mat_tractor(),
        "beam": mat_glow_prop("ConstructionBeam", "#d8f2ff", 18.0, "beam"),
        "impact": mat_glow_prop("BeamImpact", "#d8f2ff", 30.0, "beam"),
        "dust": mat_fade("Dust", "#d8cfb8"),
        "scorch": mat_fade("Scorch", "#2e3a24"),
        "grass": mat_plain("Grass", "#5f8f56", 0.95),
        "earth": mat_plain("Earth", "#6b5440", 0.95),
        "tree": mat_plain("Foliage", "#2f5a34", 0.9),
        "trunk": mat_plain("Trunk", "#5b4632", 0.9),
        "engine": mat_glow("EngineGlow", "#7fd4ff", 12.0),
        "skin": mat_tx("Skin", "#f0d4b8", 0.6),
        "uni_red": mat_tx("UniformRed", "#c8463c", 0.6),
        "uni_gold": mat_tx("UniformGold", "#d9a43a", 0.6),
        "uni_blue": mat_tx("UniformBlue", "#4a7fd6", 0.6),
    }

    # ---- world, lights, camera ----
    world = bpy.data.worlds.new("Night")
    sc.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (*hex_rgb("#0b1322"), 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    sun_data = bpy.data.lights.new("Sun", "SUN")
    sun_data.energy, sun_data.angle = 3.2, math.radians(4)
    sun = bpy.data.objects.new("Sun", sun_data)
    sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(200))
    target = empty("CameraTarget", (4, 6, 22))
    cam_data = bpy.data.cameras.new("IsoCamera")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 162
    cam_data.clip_end = 2000
    cam = bpy.data.objects.new("IsoCamera", cam_data)
    sc.collection.objects.link(cam)
    cam.location = (4 + 300, 6 + 300, 22 + 300 * 0.8165)
    tr = cam.constraints.new("TRACK_TO")
    tr.target, tr.track_axis, tr.up_axis = target, "TRACK_NEGATIVE_Z", "UP_Y"
    sc.camera = cam

    # ---- plot ----
    gx, gy = 8.0, 12.0  # centre of the 120 x 120 plot in Blender coords
    ground = prim("plane", "Ground", size=120, location=(gx, gy, 0))
    set_mat(ground, M["grass"])
    slab = prim("cube", "PlotSlab", location=(gx, gy, -4.55))  # top just under the grass; coplanar faces render black
    slab.scale = (60, 60, 4.5)
    set_mat(slab, M["earth"])
    for i in range(22):
        while True:
            x, y = random.uniform(gx - 56, gx + 56), random.uniform(gy - 56, gy + 56)
            if math.hypot(x, y) > 38:
                break
        s = random.uniform(0.8, 1.2)
        trunk = prim("cylinder", f"TreeTrunk{i}", vertices=8, radius=0.35 * s, depth=2.2 * s, location=(x, y, 1.1 * s))
        set_mat(trunk, M["trunk"])
        crown = prim("ico_sphere", f"TreeCrown{i}", subdivisions=2, radius=2.6 * s, location=(x, y, 4.2 * s))
        crown.scale.z = 1.25
        set_mat(smooth(crown), M["tree"])
    scorch = prim("cylinder", "Scorch", vertices=48, radius=13, depth=0.05, location=(0, 0, 0.06))
    set_mat(prop(scorch, "fade", 0), M["scorch"])

    # ---- colony pod ----
    pod = empty("Pod", (0, 0, SHIP_Z - 10))
    # saucer cross-section (r, z about its centre): flat deck, sloped upper hull, window band, lower hull
    SP = [(0, 1.6), (5.6, 1.6), (6.0, 1.5), (9.2, 0.8), (9.95, 0.5), (10.15, 0.3),
          (10.15, -0.3), (9.95, -0.55), (9.0, -1.05), (7.8, -1.6), (0, -1.6)]
    saucer = lathe("Saucer", SP, 96, pod)
    saucer.location = (0, 0, 1.6)
    set_mat(saucer, M["hull"])
    under = prim("cone", "SaucerUnderside", saucer, vertices=48, radius1=3.0, radius2=7.8, depth=2.0, location=(0, 0, -1.0 - 1.6))
    set_mat(smooth(under, 0.2), M["under"])
    ring = prim("torus", "UndersideGlow", saucer, major_radius=4.8, minor_radius=0.22, location=(0, 0, -1.65))
    set_mat(ring, M["glowring"])
    # underside: deflector dish at the keel and a ring of landing lights
    deflector = prim("uv_sphere", "KeelDeflector", saucer, segments=32, ring_count=12, radius=2.5, location=(0, 0, -3.6))
    deflector.scale.z = 0.22
    set_mat(smooth(deflector), M["glowring"])
    keel_rim = prim("torus", "KeelRim", saucer, major_radius=2.85, minor_radius=0.2, location=(0, 0, -3.55))
    set_mat(keel_rim, M["trim"])
    landing = []
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        ll = prim("uv_sphere", f"LandingLight{k}", saucer, segments=10, ring_count=6, radius=0.26,
                  location=(6.4 * math.cos(a), 6.4 * math.sin(a), -2.2))
        set_mat(prop(ll, "lit", 0), M["landing"])
        landing.append(ll)
    top_ring = prim("torus", "PanelRing", saucer, major_radius=5.6, minor_radius=0.08, location=(0, 0, 1.62))
    set_mat(top_ring, M["hull_dk"])
    # Starfleet hull stripes and pennant delta on the sloped hull, turned toward the camera
    sa = math.radians(28)
    stripe = on_slope("BlueStripe", saucer, SP, [(sa, [(6.1, -1.0), (9.7, -1.0), (9.7, -0.5), (6.1, -0.5)]),
                                                 (sa, [(6.1, 0.5), (9.7, 0.5), (9.7, 1.0), (6.1, 1.0)])], lift=0.04)
    set_mat(stripe, M["stripe"])
    delta = on_slope("PennantDelta", saucer, SP, [(math.radians(62), [(8.6, 0), (6.6, -0.75), (7.1, 0), (6.6, 0.75)])], lift=0.04)
    set_mat(delta, M["uni_gold"])
    # plating seams: concentric and radial
    seams = []
    for r in (6.9, 8.1):
        t = prim("torus", f"HullSeam{r:.0f}", saucer, major_radius=r, minor_radius=0.05, major_segments=96, minor_segments=6,
                 location=(0, 0, profile_z(SP, r)))
        seams.append(t)
    radial = on_slope("HullSeamsRadial", saucer, SP, [(2 * math.pi * k / 24, [(6.05, -0.04), (9.15, -0.04), (9.15, 0.04), (6.05, 0.04)])
                                                      for k in range(24) if k % 6 != 1], lift=0.025)
    seams.append(radial)
    for t in seams:
        set_mat(t, M["trim"])
    # upper deck windows on the slope
    up_win = on_slope("DeckWindows", saucer, SP, [(2 * math.pi * (k + 0.5) / 40, [(8.35, -0.2), (8.85, -0.2), (8.85, 0.2), (8.35, 0.2)])
                                                  for k in range(40)], lift=0.035)
    set_mat(prop(up_win, "lit", 0.15), M["windows"])
    # rim: airlock door over the ramp, RCS blocks, navigation lights
    door = ring_quads("Airlock", saucer, 1, 10.18, -0.26, 0.26, 0.7, phase=0.25)
    set_mat(prop(door, "lit", 0.15), M["windows"])
    rcs = []
    for k in range(6):
        a = math.radians(15 + 60 * k)
        b = prim("cube", f"RCS{k}", saucer, location=(10.12 * math.cos(a), 10.12 * math.sin(a), 0))
        b.rotation_euler.z = a
        b.scale = (0.22, 0.55, 0.36)
        set_mat(b, M["trim"])
        rcs.append(b)
    navs = []
    for nm, a, m in (("NavPort", 180, "red"), ("NavStarboard", 0, "green"), ("NavStrobeAft", 270, "strobe"), ("NavStrobeFore", 135, "strobe")):
        a = math.radians(a)
        ln = prim("uv_sphere", nm, saucer, segments=12, ring_count=8, radius=0.3, location=(10.25 * math.cos(a), 10.25 * math.sin(a), 0.42))
        set_mat(ln, M[m])
        navs.append(ln)
    strobes = [ob for ob in navs if "Strobe" in ob.name]
    for ob in strobes:
        prop(ob, "lit", 0)
    # sensor dish on the upper hull
    dish_stub = prim("cylinder", "SensorStub", saucer, vertices=12, radius=0.22, depth=0.9, location=(0, 0, 0))
    da = math.radians(205)
    dish_stub.location = (7.0 * math.cos(da), 7.0 * math.sin(da), profile_z(SP, 7.0) + 0.4)
    set_mat(dish_stub, M["hull_dk"])
    dish = prim("cone", "SensorDish", dish_stub, vertices=24, radius1=0.1, radius2=0.95, depth=0.35, location=(0, 0, 0.55))
    dish.rotation_euler = (math.radians(35), 0, 0)
    set_mat(smooth(dish), M["hull"])
    # rim windows
    wverts, wfaces = [], []
    for k in range(44):
        a = 2 * math.pi * k / 44
        c, s_ = math.cos(a), math.sin(a)
        base = len(wverts)
        r, hw, z0, z1 = 10.17, 0.2, -0.18, 0.18
        for dz in (z0, z1):
            for dt in (-hw, hw):
                wverts.append((r * c - dt * s_, r * s_ + dt * c, dz))
        wfaces.append((base, base + 1, base + 3, base + 2))
    windows = mesh_obj("RimWindows", wverts, wfaces, saucer)
    set_mat(prop(windows, "lit", 0.15), M["windows"])
    saucer_detail = [stripe, delta, *seams, up_win, door, *rcs, *navs, dish_stub, dish, deflector, keel_rim, *landing]
    for ob in (saucer, under, ring, top_ring, windows, *saucer_detail):
        prop(ob, "tx", 0)

    capsule = prim("cylinder", "CommandCapsule", pod, vertices=48, radius=5, depth=4.6, location=(0, 0, 3.2 + 2.3))
    set_mat(smooth(capsule, 0.3, 2), M["hull"])
    band = prim("torus", "CapsuleBand", capsule, major_radius=5.02, minor_radius=0.18, location=(0, 0, 1.3))
    set_mat(band, M["stripe"])
    cw_v, cw_f = [], []
    for k in range(28):
        a = 2 * math.pi * k / 28
        c, s_ = math.cos(a), math.sin(a)
        base = len(cw_v)
        for dz in (-0.7, -0.2):
            for dt in (-0.25, 0.25):
                cw_v.append((5.02 * c - dt * s_, 5.02 * s_ + dt * c, dz))
        cw_f.append((base, base + 1, base + 3, base + 2))
    cwin = mesh_obj("CapsuleWindows", cw_v, cw_f, capsule)
    set_mat(prop(cwin, "lit", 0.15), M["windows"])
    dome = prim("uv_sphere", "CommandDome", capsule, segments=48, ring_count=24, radius=4.3, location=(0, 0, 2.3))
    dome.scale.z = 0.8
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for v in dome.data.vertices:
        v.select = v.co.z < -0.01
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.delete(type="VERT")
    bpy.ops.object.mode_set(mode="OBJECT")
    set_mat(smooth(dome), M["glass"])
    prop(dome, "lit", 0)
    mast = prim("cylinder", "CommMast", dome, vertices=12, radius=0.28, depth=1.0, location=(0, 0, 3.4))
    mast.data.transform(__import__("mathutils").Matrix.Translation((0, 0, 0.5)))
    mast.location = (0, 0, 3.3)
    mast.scale = (1, 1, 0.001)
    set_mat(smooth(mast), M["hull_dk"])
    mast_ring = prim("torus", "MastSensorRing", mast, major_radius=1.6, minor_radius=0.12, location=(0, 0, 0.66))
    mast_ring.scale = (1, 1, 1 / 9.0)
    set_mat(mast_ring, M["hull"])
    mast_light = prim("uv_sphere", "MastLight", mast, segments=12, ring_count=8, radius=0.45, location=(0, 0, 1.0))
    mast_light.scale = (1, 1, 1 / 9.0)
    set_mat(mast_light, M["red"])
    # capsule trim, ribs and a second window row
    cap_detail = []
    flare = prim("torus", "CapsuleFlare", capsule, major_radius=5.05, minor_radius=0.32, location=(0, 0, -2.15))
    crown = prim("torus", "DomeCollar", capsule, major_radius=4.5, minor_radius=0.24, location=(0, 0, 2.3))
    cap_detail += [flare, crown]
    for k in range(8):
        a = 2 * math.pi * k / 8 + math.pi / 28
        rib = prim("cube", f"CapsuleRib{k}", capsule, location=(5.03 * math.cos(a), 5.03 * math.sin(a), 0.15))
        rib.rotation_euler.z = a
        rib.scale = (0.08, 0.12, 2.0)
        cap_detail.append(rib)
    for ob in cap_detail:
        set_mat(ob, M["trim"])
    cwin2 = ring_quads("CapsuleWindowsUpper", capsule, 28, 5.02, 0.35, 0.8, 0.22, phase=0.5)
    set_mat(prop(cwin2, "lit", 0.15), M["windows"])
    cap_detail.append(cwin2)
    # dome lattice (meridians + latitudes) with a lit bridge console inside
    for k in range(4):
        m_ = prim("torus", f"DomeRib{k}", dome, major_radius=4.33, minor_radius=0.06, major_segments=64, minor_segments=6)
        m_.rotation_euler = (math.radians(90), 0, math.radians(45 * k))
        cap_detail.append(m_)
    for phi in (30, 58):
        lat = prim("torus", f"DomeLatitude{phi}", dome, major_radius=4.33 * math.cos(math.radians(phi)), minor_radius=0.06,
                   major_segments=64, minor_segments=6, location=(0, 0, 4.33 * math.sin(math.radians(phi))))
        cap_detail.append(lat)
    for ob in cap_detail[-6:]:
        set_mat(ob, M["trim"])
    console = prim("cylinder", "BridgeConsole", dome, vertices=32, radius=2.0, depth=0.7, location=(0, 0, 0.35))
    set_mat(console, M["hull_dk"])
    console_glow = prim("torus", "BridgeConsoleGlow", dome, major_radius=2.02, minor_radius=0.1, location=(0, 0, 0.72))
    set_mat(console_glow, M["glowring"])
    cap_detail += [console, console_glow]
    for ob in (capsule, band, cwin, dome, mast, mast_ring, mast_light, *cap_detail):
        prop(ob, "tx", 0)

    # landing legs: attach under the belly, telescope to the ground
    legs, leg_parts = [], []
    for k, deg in enumerate((45, 135, 225, 315)):
        a = math.radians(deg)
        pivot = empty(f"LegPivot{k}", (3.5 * math.cos(a), 3.5 * math.sin(a), -0.5), pod)
        reach, drop = 6.5, POD_Z - 0.5
        length = math.hypot(reach, drop)
        tilt = math.atan2(reach, drop)
        pivot.rotation_euler = (0, -tilt, a)
        strut = prim("cylinder", f"LegStrut{k}", pivot, vertices=12, radius=0.38, depth=length, location=(0, 0, -length / 2))
        set_mat(smooth(strut), M["hull_dk"])
        foot = prim("cylinder", f"LegFoot{k}", pivot, vertices=16, radius=1.0, depth=0.3, location=(0, 0, -length))
        foot.rotation_euler = (0, tilt, 0)
        set_mat(foot, M["under"])
        sleeve = prim("cylinder", f"LegSleeve{k}", pivot, vertices=16, radius=0.62, depth=length * 0.42, location=(0, 0, -length * 0.21))
        set_mat(smooth(sleeve, 0.08), M["hull"])
        knee = prim("torus", f"LegCollar{k}", pivot, major_radius=0.62, minor_radius=0.16, location=(0, 0, -length * 0.42))
        set_mat(knee, M["trim"])
        piston = prim("cylinder", f"LegPiston{k}", pivot, vertices=8, radius=0.14, depth=length * 0.55, location=(0.62, 0, -length * 0.4))
        set_mat(piston, M["hull_dk"])
        toe = prim("torus", f"LegFootRing{k}", foot, major_radius=1.0, minor_radius=0.12, location=(0, 0, 0.12))
        set_mat(toe, M["trim"])
        foot_light = prim("uv_sphere", f"LegLight{k}", pivot, segments=10, ring_count=6, radius=0.2, location=(0, 0.62, -length * 0.12))
        set_mat(foot_light, M["glowring"])
        for ob in (strut, foot, sleeve, knee, piston, toe, foot_light):
            prop(ob, "tx", 0)
            leg_parts.append(ob)
        pivot.scale = (1, 1, 0.01)
        legs.append(pivot)

    # boarding ramp, hinged at the saucer rim
    ramp_hinge = empty("RampHinge", (0, 8.5, 0.2), pod)
    rlen = math.hypot(19 - 8.5, POD_Z)
    ramp = prim("cube", "Ramp", ramp_hinge, location=(0, rlen / 2, 0))
    ramp.scale = (2.4, rlen / 2, 0.15)
    set_mat(prop(ramp, "tx", 1), M["hull_dk"])
    ramp_light = prim("cube", "RampLights", ramp_hinge, location=(0, rlen / 2, 0.17))
    ramp_light.scale = (0.12, rlen / 2 - 0.4, 0.02)
    set_mat(prop(ramp_light, "tx", 1), M["glowring"])
    ramp_hinge.scale = (0.001, 0.001, 0.001)

    # solar petals, hinged at their inner edge
    petals = []
    for i, d2 in enumerate(PETALS):
        a = bangle(d2)
        hinge = empty(f"PetalHinge{i}", (11.5 * math.cos(a), 11.5 * math.sin(a), 0.35))
        hinge.rotation_euler = (0, 0, a)
        r1, r2, half = 0.0, 6.5, math.radians(10)
        verts = [(r1, -11.5 * math.tan(half), 0), (r2, -18 * math.tan(half), 0), (r2, 18 * math.tan(half), 0), (r1, 11.5 * math.tan(half), 0)]
        pm = mesh_obj(f"SolarPetal{i}", verts, [(0, 1, 2, 3)], hinge)
        sol = pm.modifiers.new("Solidify", "SOLIDIFY")
        sol.thickness = 0.25
        set_mat(prop(pm, "tx", 1), M["solar"])
        hinge.scale = (0.001, 0.001, 0.001)
        petals.append((hinge, d2))

    # supply capsules and beacons (beamed in)
    caps, beacons = [], []
    for i, (x2, y2) in enumerate(CAPSULES):
        bx, by = W(x2, y2)
        c = prim("cylinder", f"SupplyCapsule{i}", vertices=32, radius=2.2, depth=4.4, location=(bx, by, 2.2))
        set_mat(smooth(c, 0.25), M["hull"])
        cap_top = prim("uv_sphere", f"SupplyCapsuleTop{i}", c, segments=32, ring_count=16, radius=2.2, location=(0, 0, 2.2))
        cap_top.scale.z = 0.45
        set_mat(smooth(cap_top), M["hull"])
        cb = prim("torus", f"SupplyCapsuleBand{i}", c, major_radius=2.22, minor_radius=0.12, location=(0, 0, -1.0))
        set_mat(cb, M["glowring"])
        for ob in (c, cap_top, cb):
            prop(ob, "tx", 0)
        caps.append((c, [c, cap_top, cb], ang2d(x2, y2)))
    for i, (x2, y2) in enumerate(BEACONS):
        bx, by = W(x2, y2)
        pole = prim("cylinder", f"BeaconPole{i}", vertices=8, radius=0.2, depth=4.2, location=(bx, by, 2.1))
        set_mat(pole, M["hull_dk"])
        lamp = prim("uv_sphere", f"BeaconLamp{i}", pole, segments=12, ring_count=8, radius=0.5, location=(0, 0, 2.3))
        set_mat(lamp, M["red"])
        for ob in (pole, lamp):
            prop(ob, "tx", 0)
        beacons.append((pole, [pole, lamp], ang2d(x2, y2)))

    # colonists
    uniforms = [M["uni_red"], M["uni_gold"], M["uni_blue"], M["hull"], M["uni_red"]]
    foot2d = (52 + 19, 48)
    walkers = []
    for i, to2d in enumerate(WALKERS):
        at2d = (foot2d[0] + 3 + (i % 3) * 2.5, foot2d[1] - 3 + (i // 3) * 4 + (i % 2) * 1.5)
        ax, ay = W(*at2d)
        body = prim("cylinder", f"Colonist{i}", vertices=10, radius=0.32, depth=1.3, location=(ax, ay, 0.65))
        set_mat(smooth(body), uniforms[i])
        head = prim("uv_sphere", f"ColonistHead{i}", body, segments=12, ring_count=8, radius=0.27, location=(0, 0, 0.95))
        set_mat(head, M["skin"])
        for ob in (body, head):
            prop(ob, "tx", 0)
        walkers.append((body, [body, head], W(*at2d), W(*to2d)))

    # ---- drop ship ----
    ship = empty("DropShip", (0, 0, SHIP_Z))
    hullb = prim("cube", "ShipHull", ship, location=(0, 0, 0))
    hullb.scale = (3.0, 7.0, 1.4)
    set_mat(prop(smooth(hullb, 0.6, 3), "tx", 1), M["hull"])
    nose = prim("cube", "ShipCockpit", ship, location=(0, 7.4, 0.5))
    nose.scale = (2.4, 1.4, 0.9)
    set_mat(prop(smooth(nose, 0.4, 3), "tx", 1), M["glass"])
    prop(nose, "lit", 0.6)
    for side in (-1, 1):
        nac = prim("cylinder", f"ShipNacelle{'L' if side < 0 else 'R'}", ship, vertices=24, radius=0.9, depth=11, location=(side * 4.6, -1.0, 0.6))
        nac.rotation_euler = (math.radians(90), 0, 0)
        set_mat(prop(smooth(nac, 0.3), "tx", 1), M["hull_dk"])
        strip = prim("cube", f"ShipNacelleGlow{'L' if side < 0 else 'R'}", nac, location=(side * 0.6, 0, 0))
        strip.scale = (0.12, 0.6, 5.0)
        set_mat(strip, M["engine"])
    emitter = prim("cylinder", "ShipEmitter", ship, vertices=24, radius=1.1, depth=0.3, location=(0, 0, -1.5))
    set_mat(emitter, M["engine"])

    # ---- beams and effects ----
    tractor = prim("cone", "TractorBeam", vertices=48, radius1=8.0, radius2=1.0, depth=SHIP_Z - 2, location=(0, 0, (SHIP_Z - 2) / 2))
    set_mat(prop(tractor, "beam", 0), M["tractor"])
    bt = empty("BeamTarget", (0, 0, 0))
    cbeam = mesh_obj("ConstructionBeam", *_beam_mesh(), parent=None)
    cl = cbeam.constraints.new("COPY_LOCATION")
    cl.target = emitter
    st = cbeam.constraints.new("STRETCH_TO")
    st.target, st.rest_length, st.volume = bt, 1.0, "NO_VOLUME"
    set_mat(prop(cbeam, "beam", 0), M["beam"])
    impact = prim("uv_sphere", "BeamImpact", bt, segments=16, ring_count=8, radius=1.4, location=(0, 0, 0.3))
    impact.scale.z = 0.4
    set_mat(prop(impact, "beam", 0), M["impact"])
    impact_light_data = bpy.data.lights.new("BeamImpactLight", "POINT")
    impact_light_data.color = CYAN
    impact_light = bpy.data.objects.new("BeamImpactLight", impact_light_data)
    sc.collection.objects.link(impact_light)
    link(impact_light, bt)
    impact_light.location = (0, 0, 1.5)
    dust1 = prim("torus", "DustRingTouchdown", major_radius=1.0, minor_radius=0.06, location=(0, 0, 0.2))
    dust2 = prim("torus", "DustRingUnfurl", major_radius=1.0, minor_radius=0.05, location=(0, 0, 0.2))
    for d in (dust1, dust2):
        set_mat(prop(d, "fade", 0), M["dust"])
    for fx in (tractor, cbeam, impact, dust1, dust2):
        fx.visible_shadow = False

    def column(name, loc, radius, height, parent=None):
        c = prim("cylinder", name, parent, vertices=32, radius=radius, depth=height, location=(loc[0], loc[1], loc[2] + height / 2))
        c.data.materials.append(M["column"])
        for p in c.data.polygons:  # open the caps so only the glowing wall shows
            p.select = abs(p.normal.z) > 0.5
        bpy.context.view_layer.objects.active = c
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.delete(type="FACE")
        bpy.ops.object.mode_set(mode="OBJECT")
        c.visible_shadow = False
        return prop(c, "glow", 0)

    # ---------------------------------------------------------------- timeline
    # drop ship: arrive, hover, depart
    key(ship, "location", (-60, 110, SHIP_Z + 70), 0.0)
    key(ship, "location", (0, 0, SHIP_Z), 2.5)
    t = 2.5
    while t < 16.4:
        key(ship, "location", (0, 0, SHIP_Z + (0.45 if int(t / 0.8) % 2 else -0.45)), t)
        t += 0.8
    key(ship, "location", (0, 0, SHIP_Z), 16.6)
    key(ship, "location", (90, -70, SHIP_Z + 90), 18.6)
    key(ship, "rotation_euler", (0, 0, 0), 16.6)
    key(ship, "rotation_euler", (math.radians(-12), 0, math.radians(-25)), 18.6)

    # tractor beam
    for tt, v in ((0, 0), (2.3, 0), (2.8, 1), (6.2, 1), (6.7, 0)):
        key(tractor, '["beam"]', v, tt)

    # pod: materializes high in the beam, lowered folded, lifted by its legs
    pod_parts = [saucer, under, ring, top_ring, windows, capsule, band, cwin, dome, mast, mast_ring, mast_light, *saucer_detail, *cap_detail, *leg_parts]
    for ob in pod_parts:
        key(ob, '["tx"]', 0.0, 2.8)
        key(ob, '["tx"]', 1.0, 2.8 + TX * 1.2)
    pcol = column("PodTransporterColumn", (0, 0, -3), 13, 16, pod)
    for tt, v in ((2.75, 0), (3.2, 1), (3.95, 0)):
        key(pcol, '["glow"]', v, tt)
    key(pod, "location", (0, 0, SHIP_Z - 10), 3.6)
    key(pod, "location", (0, 0, POD_LOW), 6.0)
    key(pod, "location", (0, 0, POD_LOW + 0.5), 6.2)
    key(pod, "location", (0, 0, POD_LOW), 6.4)
    key(pod, "location", (0, 0, POD_LOW), 6.7)
    key(pod, "location", (0, 0, POD_Z), 7.6)
    for lg in legs:
        key(lg, "scale", (1, 1, 0.01), 6.55)
        key(lg, "scale", (1, 1, (POD_LOW - 0.5) / (POD_Z - 0.5)), 6.75)
        key(lg, "scale", (1, 1, 1), 7.6)
    # saucer unfurls (scaled about its centre; the underside cone and rim ride along)
    for tt, s in ((0, COMPACT), (7.4, COMPACT), (8.6, 1.06), (9.0, 1.0)):
        key(saucer, "scale", (s, s, s), tt)
        key(saucer, "location", (0, 0, 1.6 * s + 2.0 * s), tt)
    # command capsule: nested low and small, then extends
    for tt, z, s in ((0, 3.2 * COMPACT + 2.0 * COMPACT + 0.6, 0.62), (8.6, 3.2 + 2.0 + 0.2, 0.62), (9.6, 3.2 + 2.0 + 2.3, 1.0)):
        key(capsule, "location", (0, 0, z), tt)
        key(capsule, "scale", (s, s, s), tt)
    for tt, v in ((0, 0), (8.9, 0), (9.5, 1)):
        key(dome, '["lit"]', v, tt)
    key(mast, "scale", (1, 1, 0.001), 9.4)
    key(mast, "scale", (1, 1, 9.0), 10.4)
    # scorch and dust
    for tt, v in ((0, 0), (6.0, 0), (6.6, 1)):
        key(scorch, '["fade"]', v, tt)
    for d, t0, t1, r0, r1 in ((dust1, 6.0, 6.9, 7, 24), (dust2, 7.6, 8.6, 12, 26)):
        # flat rings: z stays thin so the torus never cuts into the ground (that renders black)
        key(d, "scale", (r0, r0, 2), t0)
        key(d, "scale", (r1, r1, 2), t1)
        key(d, '["fade"]', 0.0, t0 - 0.1, "CONSTANT")
        key(d, '["fade"]', 0.75, t0)
        key(d, '["fade"]', 0.0, t1)
    # petals unfurl in sequence around the pod
    order = sorted(range(len(petals)), key=lambda i: petals[i][1] % 360)
    for k, i in enumerate(order):
        hinge, d2 = petals[i]
        ti = 9.8 + k * 0.16
        key(hinge, "scale", (0.001, 0.001, 0.001), ti - 0.1, "CONSTANT")  # keys must land on different frames
        key(hinge, "scale", (1, 1, 1), ti, "CONSTANT")
        key(hinge, "rotation_euler", (0, math.radians(-90), hinge.rotation_euler[2]), ti)
        key(hinge, "rotation_euler", (0, math.radians(8), hinge.rotation_euler[2]), ti + 0.35)
        key(hinge, "rotation_euler", (0, 0, hinge.rotation_euler[2]), ti + 0.45)

    # construction beam: two laps (lap 1 r=28, lap 2 spirals to r=40)
    for tt, v in ((0, 0), (11.0, 0), (11.3, 1), (15.5, 1), (15.8, 0)):
        key(cbeam, '["beam"]', v, tt)
        key(impact, '["beam"]', v, tt)
        key(impact_light.data, "energy", 4000.0 * v, tt)
    steps = int((T1 - T0) * FPS / 2)
    for n in range(steps + 1):
        lap = n / steps
        th2d = 720 * lap
        r = 28 if lap < 0.5 else 28 + 12 * min(1.0, (lap - 0.5) / 0.12)
        a = bangle(th2d)
        key(bt, "location", (r * math.cos(a), r * math.sin(a), 0), T0 + lap * (T1 - T0), "LINEAR")

    def lap_time(deg, lap):
        return T0 + ((lap * 360 + deg % 360) / 720) * (T1 - T0)

    def beam_in(parts, t_start, length, col_loc, col_r, col_h, name):
        for ob in parts:
            key(ob, '["tx"]', 0.0, t_start)
            key(ob, '["tx"]', 1.0, t_start + length)
        c = column(name, col_loc, col_r, col_h)
        key(c, '["glow"]', 0.0, t_start - 0.05)
        key(c, '["glow"]', 1.0, t_start + length * 0.45)
        key(c, '["glow"]', 0.0, t_start + length + 0.1)

    for i, (c, parts, a2) in enumerate(caps):
        ts = lap_time(a2, 0) - 0.15
        beam_in(parts, ts, TX, (c.location.x, c.location.y, 0), 3.4, 8, f"CapsuleTransporterColumn{i}")
    for i, (pole, parts, a2) in enumerate(beacons):
        ts = lap_time(a2, 1) - 0.1
        beam_in(parts, ts, TX * 0.8, (pole.location.x, pole.location.y, 0), 1.6, 7, f"BeaconTransporterColumn{i}")

    # ramp and pod lights
    key(ramp_hinge, "scale", (0.001, 0.001, 0.001), 15.9, "CONSTANT")
    key(ramp_hinge, "scale", (1, 1, 1), 16.0, "CONSTANT")
    down = math.atan2(POD_Z, 19 - 8.5)
    key(ramp_hinge, "rotation_euler", (math.radians(75), 0, 0), 16.0)
    key(ramp_hinge, "rotation_euler", (-down, 0, 0), 16.8)
    for ob in (windows, cwin, cwin2, up_win, door):
        key(ob, '["lit"]', 0.15, 16.2)
        key(ob, '["lit"]', 1.0, 16.8)
    # landing lights on while lowered, dim once standing; strobes blink after power-up
    for ll in landing:
        for tt, v in ((0, 0), (3.4, 0), (3.9, 1), (7.6, 1), (8.4, 0.25)):
            key(ll, '["lit"]', v, tt)
    for ob in strobes:
        key(ob, '["lit"]', 0.0, 0, "CONSTANT")
    t = 9.6
    while t < DURATION:
        for ob in strobes:
            key(ob, '["lit"]', 1.0, t, "CONSTANT")
            key(ob, '["lit"]', 0.0, t + 0.1, "CONSTANT")
        t += 1.2

    # colonists beam down beside the ramp, then walk to their posts
    for i, (body, parts, at, to) in enumerate(walkers):
        t0w = 16.8 + i * 0.25
        beam_in(parts, t0w, 0.7, (at[0], at[1], 0), 0.9, 2.6, f"ColonistTransporterColumn{i}")
        key(body, "location", (at[0], at[1], 0.65), t0w + 0.7)
        key(body, "location", (to[0], to[1], 0.65), t0w + 1.7)

    hide_while_gone(sc)

    # ---- render settings: EEVEE for your machine, 30 fps, 20 s ----
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1200
    sc.render.film_transparent = False
    sc.frame_set(F(DURATION))
    return sc


FADE_PROPS = ("tx", "fade", "glow", "beam")


def hide_while_gone(sc):
    """Key hide_render on every part whose fade property (tx, fade, glow, beam) is 0, so unseen
    parts cost nothing (and invisible shells can't leave dark specks in Cycles)."""
    parts = [ob for ob in bpy.data.objects if ob.type == "MESH" and ob.animation_data
             and any(k in ob.keys() for k in FADE_PROPS)]
    changes = {ob.name: [] for ob in parts}
    prev = {}
    for f in range(sc.frame_start, sc.frame_end + 1):
        sc.frame_set(f)
        for ob in parts:
            gone = next(ob[k] for k in FADE_PROPS if k in ob.keys()) <= 0.001
            if prev.get(ob.name) != gone:
                changes[ob.name].append((f, gone))
                prev[ob.name] = gone
    for ob in parts:
        for f, gone in changes[ob.name]:
            ob.hide_render = gone
            ob.keyframe_insert("hide_render", frame=f)


def _beam_mesh():
    """Unit-length glowing rod along +Y (Stretch To scales it to the target)."""
    verts, faces = [], []
    n, r = 12, 0.22
    for k in range(n):
        a = 2 * math.pi * k / n
        verts += [(r * math.cos(a), 0, r * math.sin(a)), (r * math.cos(a), 1, r * math.sin(a))]
    for k in range(n):
        a, b = 2 * k, 2 * ((k + 1) % n)
        faces.append((a, b, b + 1, a + 1))
    return verts, faces


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = dict(zip(argv[::2], argv[1::2]))
    sc = build()
    if "--preview" in opts:
        import os
        outdir = opts["--preview"]
        os.makedirs(outdir, exist_ok=True)
        sc.render.engine = "CYCLES"
        sc.cycles.device = "CPU"
        sc.cycles.samples = int(opts.get("--samples", 24))
        sc.cycles.transparent_max_bounces = 64  # stacked dissolving shells otherwise go black
        try:
            sc.cycles.use_denoising = True
        except Exception:
            pass
        if "--zoom" in opts:  # close-up on the pod for checking detail
            cam = sc.camera
            cam.data.ortho_scale /= float(opts["--zoom"])
            bpy.data.objects["CameraTarget"].location = (0, 0, 9)
            cam.location = (300, 300, 9 + 300 * 0.8165)
        res = int(opts.get("--res", 640))
        sc.render.resolution_x, sc.render.resolution_y = res, res * 3 // 4
        for ts in opts.get("--frames", "4.5,8.0,12.0").split(","):
            sc.frame_set(F(float(ts)))
            sc.render.filepath = os.path.join(outdir, f"t{float(ts):05.1f}.png")
            bpy.ops.render.render(write_still=True)
        sc.render.engine = "BLENDER_EEVEE"
        sc.render.resolution_x, sc.render.resolution_y = 1600, 1200
    if "--out" in opts:
        bpy.ops.wm.save_as_mainfile(filepath=opts["--out"])


main()
