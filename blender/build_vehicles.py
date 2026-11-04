"""Federation ground vehicles, built in Blender and exported for the Godot prototype (game/models/vehicles/*.glb).

Look: the owner's reference art (art/reference/vehicles-and-outposts.md): boxy white-and-grey six-wheelers with a big
wrap-around tinted windscreen, roof light bars and spot lamps, owner-colour flash stripes, hull numbers and scuffed
paint.

Run (Blender 5.x, or the `bpy` module from PyPI):
  blender --background --python blender/build_vehicles.py -- --vehicle survey_rover --out game/models/vehicles/survey_rover.glb
  python3.11 blender/build_vehicles.py --vehicle survey_rover --preview blender/previews/vehicles --res 960

Conventions (so the game can animate the parts): Blender +Y is the vehicle's front (glTF/Godot then see it facing -Z,
like the old code-built models), +Z up, units = Godot world units. Named parts the game drives keep their names
("Dish" spins on the survey rover). The material named "Owner" is recoloured per player in the game. The hull's scuffed
paint is baked into an image texture so it survives the glTF export.
"""
import math
import os
import sys

import bpy  # first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh
from mathutils import Matrix, Vector

# ---------------------------------------------------------------- arguments

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


VEHICLE = arg("--vehicle", "survey_rover")
OUT = arg("--out")
PREVIEW = arg("--preview")
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "48"))
BAKE = "--no-bake" not in argv

# ---------------------------------------------------------------- scene helpers


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


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


def link(obj, parent=None):
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


def empty(name, loc=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    return link(e, parent)


def mesh_object(name, bm, mat, parent=None, loc=(0, 0, 0), smooth=False):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if smooth:
        for poly in me.polygons:
            poly.use_smooth = True
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    o.location = loc
    return link(o, parent)


def bevel(o, width=0.04, segments=2, angle=35.0):
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle)
    m.harden_normals = False
    return o


def box(name, size, loc, mat, parent=None, bev=0.03, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    o = mesh_object(name, bm, mat, parent, loc)
    o.rotation_euler = rot
    if bev > 0:
        bevel(o, bev, 2)
    return o


def cylinder(name, radius, depth, loc, mat, parent=None, axis="X", segments=24, bev=0.0, radius2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius if radius2 is None else radius2, depth=depth)
    o = mesh_object(name, bm, mat, parent, loc, smooth=True)
    if axis == "X":
        o.rotation_euler = (0, math.radians(90), 0)
    elif axis == "Y":
        o.rotation_euler = (math.radians(90), 0, 0)
    if bev > 0:
        bevel(o, bev, 2, 30)
    return o


def prism(name, profile, width, mat, parent=None, taper_above=None, taper=1.0, bev=0.06):
    """Extrude a side profile [(y, z), ...] (counter-clockwise seen from +X) across x = ±width/2.
    Vertices above taper_above are pulled in to taper × half-width (tumblehome)."""
    bm = bmesh.new()
    left = [bm.verts.new((-width / 2, y, z)) for y, z in profile]
    right = [bm.verts.new((width / 2, y, z)) for y, z in profile]
    bm.faces.new(list(reversed(left)))
    bm.faces.new(right)
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((left[i], left[j], right[j], right[i]))
    if taper_above is not None:
        for v in left + right:
            if v.co.z > taper_above:
                v.co.x *= taper
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_object(name, bm, mat, parent)
    if bev > 0:
        bevel(o, bev, 3, 30)
    return o


def ribbon(name, points, height, mat, parent=None, x=0.0, outward=1.0):
    """A thin stripe on a side of the hull: a polyline [(y, z)] at plane x, `height` tall, pushed out by outward*1cm."""
    bm = bmesh.new()
    top, bottom = [], []
    for y, z in points:
        top.append(bm.verts.new((x + 0.012 * outward, y, z + height / 2)))
        bottom.append(bm.verts.new((x + 0.012 * outward, y, z - height / 2)))
    for i in range(len(points) - 1):
        f = bm.faces.new((bottom[i], bottom[i + 1], top[i + 1], top[i]))
        if outward < 0:
            f.normal_flip()
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def text(name, body, size, loc, rot, mat, parent=None):
    """A text label as real geometry (glTF has no text): build the font curve, evaluate it to a mesh, drop the curve."""
    curve = bpy.data.curves.new(f"{name}Font", "FONT")
    curve.body = body
    curve.size = size
    curve.extrude = 0.006
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    tmp = bpy.data.objects.new(f"{name}Tmp", curve)
    bpy.context.scene.collection.objects.link(tmp)
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(tmp)
    me.materials.clear()
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    o.location = loc
    o.rotation_euler = rot
    o.visible_shadow = False  # a decal: its shadow on the paint just looks like a doubled number
    return link(o, parent)


# ---------------------------------------------------------------- shared materials


def palette():
    return {
        "hull": material("Hull", (0.78, 0.79, 0.8), 0.15, 0.42, coat=0.2),
        "panel": material("HullPanel", (0.62, 0.63, 0.65), 0.2, 0.5),
        "dark": material("Chassis", (0.13, 0.135, 0.15), 0.4, 0.55),
        "steel": material("Steel", (0.55, 0.57, 0.6), 0.85, 0.32),
        "tyre": material("Tyre", (0.045, 0.045, 0.05), 0.0, 0.88),
        "hub": material("Hub", (0.66, 0.67, 0.7), 0.9, 0.28),
        "glass": material("Glass", (0.05, 0.085, 0.04), 0.3, 0.04, emission=(0.12, 0.2, 0.07), strength=0.04, coat=1.0),
        "frame": material("Frame", (0.2, 0.21, 0.23), 0.5, 0.45),
        "owner": material("Owner", (0.72, 0.08, 0.06), 0.1, 0.4),
        "number": material("Number", (0.06, 0.08, 0.16), 0.0, 0.5),
        "lamp": material("Lamp", (1.0, 0.95, 0.82), 0.0, 0.2, emission=(1.0, 0.95, 0.82), strength=6.0),
        "amber": material("Amber", (1.0, 0.55, 0.12), 0.0, 0.3, emission=(1.0, 0.5, 0.1), strength=4.0),
        "redlamp": material("TailLamp", (0.9, 0.08, 0.05), 0.0, 0.3, emission=(1.0, 0.08, 0.05), strength=3.0),
    }


# ---------------------------------------------------------------- wheels


def wheel(name, x, y, radius, width, P, parent):
    """Tyre with tread blocks, recessed rim, hub cap and bolts. Pivot at the wheel centre."""
    w = empty(name, (x, y, radius), parent)
    side = 1 if x > 0 else -1
    cylinder(f"{name}Tyre", radius, width, (0, 0, 0), P["tyre"], w, bev=0.07)
    blocks = 26
    for k in range(blocks):
        a = 2 * math.pi * k / blocks
        b = box(f"{name}Tread{k}", (width * 0.92, 0.12, 0.06), (0, math.cos(a) * radius, math.sin(a) * radius), P["tyre"], w, bev=0.01,
                rot=(a + math.pi / 2, 0, 0))
    cylinder(f"{name}Rim", radius * 0.62, width * 1.02, (0, 0, 0), P["hub"], w, bev=0.03)
    cylinder(f"{name}Dish", radius * 0.5, 0.05, (side * width * 0.52, 0, 0), P["panel"], w, bev=0.015)
    cylinder(f"{name}Cap", radius * 0.2, 0.08, (side * width * 0.56, 0, 0), P["steel"], w, segments=16, bev=0.02)
    for k in range(6):
        a = 2 * math.pi * k / 6
        cylinder(f"{name}Bolt{k}", 0.03, 0.05, (side * width * 0.56, math.cos(a) * radius * 0.33, math.sin(a) * radius * 0.33), P["steel"], w, segments=8)
    return w


# ---------------------------------------------------------------- survey rover


def survey_rover():
    """Six-wheeled survey scout in the Grizzly Bear / G.A.V. style: wedge nose with a wrap-around windscreen, roof
    light bar, sensor dish ("Dish", spins in the game), owner-colour flash stripes, hull number."""
    P = palette()
    root = empty("SurveyRover")
    L, W = 4.6, 2.45            # body length (y) and width (x)
    wheel_r, wheel_w = 0.6, 0.42
    axles = (1.45, -0.6, -1.7)  # front, middle, rear (the middle and rear pair close together, like the art)

    # Main hull: side profile (y forward, z up), wedge nose and windscreen slope, slight tumblehome above z 1.7.
    profile = [(-2.3, 0.78), (2.08, 0.78), (2.3, 0.98), (2.32, 1.38), (2.1, 1.58), (1.38, 2.28), (1.1, 2.36), (-2.05, 2.36),
               (-2.3, 2.12)]
    hull = prism("Hull", profile, W, P["hull"], root, taper_above=1.7, taper=0.93, bev=0.07)
    # Wheel arches: cut half-cylinders out of the lower hull.
    for y in axles:
        cutter = cylinder("ArchCut", wheel_r + 0.12, W + 0.4, (0, y, wheel_r), P["dark"], None, segments=32)
        mod = hull.modifiers.new("Arch", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cutter
        cutter.hide_render = True
        cutter.hide_viewport = True
    hull.modifiers.move(hull.modifiers.find("Bevel"), len(hull.modifiers) - 1)

    # Chassis skirt and bumpers.
    box("Skirt", (W - 0.2, L - 0.3, 0.32), (0, 0, 0.62), P["dark"], root, 0.04)
    box("FrontBumper", (W + 0.05, 0.22, 0.26), (0, 2.38, 0.88), P["dark"], root, 0.05)
    box("RearBumper", (W, 0.18, 0.22), (0, -2.38, 0.9), P["dark"], root, 0.05)
    for x in (-0.8, -0.4, 0.4, 0.8):
        box(f"Tow{x}", (0.1, 0.12, 0.1), (x, 2.5, 0.88), P["steel"], root, 0.02)

    # Windscreen: one tinted pane on the slope plus two wrap-around side panes.
    slope = math.atan2(2.28 - 1.58, 1.38 - 2.1)  # angle of the nose slope in the y-z plane
    mid = Vector((0, (1.38 + 2.1) / 2 + 0.03, (2.28 + 1.58) / 2 + 0.03))
    box("WindscreenFrame", (W * 0.9, 0.03, 0.96), mid - Vector((0, 0.012, 0.012)), P["frame"], root, 0.02, rot=(slope - math.pi / 2, 0, 0))
    box("Windscreen", (W * 0.85, 0.04, 0.86), mid, P["glass"], root, 0.02, rot=(slope - math.pi / 2, 0, 0))
    box("Wiper", (0.9, 0.02, 0.02), mid + Vector((0.2, 0.02, -0.38)), P["dark"], root, 0, rot=(slope - math.pi / 2, 0, math.radians(8)))
    for s in (-1, 1):
        bm = bmesh.new()
        x = s * (W / 2 * 0.95 + 0.012)
        pts = [(1.42, 2.16), (2.02, 1.62), (1.62, 1.62), (1.3, 1.95), (1.3, 2.16)]
        vs = [bm.verts.new((x, y, z)) for y, z in pts]
        f = bm.faces.new(vs if s > 0 else list(reversed(vs)))
        mesh_object(f"SideGlass{s}", bm, P["glass"], root)
        # Rear side window.
        box(f"SideWindow{s}", (0.03, 0.55, 0.42), (s * (W / 2 * 0.93 + 0.01), -0.95, 1.95), P["glass"], root, 0.01)
        box(f"SideWindowFrame{s}", (0.02, 0.65, 0.52), (s * (W / 2 * 0.93), -0.95, 1.95), P["panel"], root, 0.01)
        # Door panel seams and a step.
        box(f"DoorSeam{s}", (0.02, 0.03, 0.62), (s * (W / 2 + 0.003), 0.35, 1.26), P["dark"], root, 0)
        box(f"DoorSeamB{s}", (0.02, 0.03, 0.62), (s * (W / 2 + 0.003), -0.45, 1.26), P["dark"], root, 0)
        box(f"DoorSeamT{s}", (0.02, 0.83, 0.03), (s * (W / 2 + 0.003), -0.05, 1.56), P["dark"], root, 0)
        box(f"Handle{s}", (0.04, 0.16, 0.04), (s * (W / 2 + 0.02), 0.15, 1.3), P["steel"], root, 0.01)
        # Equipment boxes between the wheels and a vent grille.
        box(f"SideBox{s}", (0.16, 0.7, 0.3), (s * (W / 2 + 0.06), -1.15, 1.02), P["panel"], root, 0.03)
        for k in range(4):
            box(f"Vent{s}{k}", (0.02, 0.36, 0.025), (s * (W / 2 + 0.004), -1.55, 1.25 + k * 0.06), P["dark"], root, 0)
        box(f"Step{s}", (0.22, 0.6, 0.06), (s * (W / 2 + 0.05), -0.05, 0.86), P["steel"], root, 0.015)
        # Owner-colour flash: low along the side, sweeping up over the cab like the reference art.
        ribbon(f"Flash{s}", [(-2.28, 1.42), (-0.2, 1.42), (0.6, 1.52), (1.7, 1.52)], 0.09, P["owner"], root, x=s * W / 2, outward=s)
        ribbon(f"FlashB{s}", [(-2.28, 1.27), (-0.35, 1.27)], 0.04, P["owner"], root, x=s * W / 2, outward=s)
        # Hull number.
        rot = (math.radians(90), 0, math.radians(90 if s > 0 else -90))
        # On the upper rear side, lying flat on the wall, which leans in by `lean` above z 1.58 (tumblehome).
        lean = math.atan2(W / 2 * 0.07, 2.36 - 1.58)
        z = 1.95
        num = text(f"Number{s}", "24", 0.36, (0, 0, 0), (math.radians(90), 0, math.radians(90 if s > 0 else -90)), P["number"], root)
        num.matrix_basis = (Matrix.Translation((s * (W / 2 - math.tan(lean) * (z - 1.58) + 0.004), -1.75, z))
                            @ Matrix.Rotation(-s * lean, 4, "Y") @ num.rotation_euler.to_matrix().to_4x4())
        # The rover's name, above the door.
        name = text(f"Name{s}", "KALTY", 0.2, (0, 0, 0), (math.radians(90), 0, math.radians(90 if s > 0 else -90)), P["number"], root)
        zn = 1.92
        name.matrix_basis = (Matrix.Translation((s * (W / 2 - math.tan(lean) * (zn - 1.58) + 0.004), 0.25, zn))
                             @ Matrix.Rotation(-s * lean, 4, "Y") @ name.rotation_euler.to_matrix().to_4x4())

    # Name across the back.
    text("NameRear", "KALTY", 0.22, (0, -2.31, 1.62), (math.radians(90), 0, math.radians(180)), P["number"], root)

    # Front: grille, headlights, amber markers.
    box("Grille", (1.1, 0.06, 0.26), (0, 2.33, 1.17), P["dark"], root, 0.02)
    for k in range(5):
        box(f"Slat{k}", (1.0, 0.03, 0.025), (0, 2.37, 1.07 + k * 0.05), P["steel"], root, 0)
    for s in (-1, 1):
        box(f"Headlamp{s}", (0.34, 0.05, 0.14), (s * 0.85, 2.34, 1.2), P["lamp"], root, 0.02)
        box(f"HeadlampFrame{s}", (0.42, 0.04, 0.22), (s * 0.85, 2.32, 1.2), P["dark"], root, 0.02)
        box(f"Marker{s}", (0.12, 0.05, 0.07), (s * 1.12, 2.31, 1.36), P["amber"], root, 0.01)
        box(f"Tail{s}", (0.3, 0.05, 0.12), (s * 0.9, -2.33, 1.9), P["redlamp"], root, 0.02)

    # Roof: light bar with spot lamps, equipment lockers, hatch, antenna.
    box("LightBar", (1.9, 0.22, 0.12), (0, 1.0, 2.45), P["dark"], root, 0.03)
    for k, x in enumerate((-0.75, -0.25, 0.25, 0.75)):
        cylinder(f"Spot{k}", 0.08, 0.06, (x, 1.12, 2.46), P["lamp"], root, axis="Y", segments=16)
    box("Locker", (0.7, 0.9, 0.24), (0.6, -1.25, 2.48), P["panel"], root, 0.04)
    box("LockerB", (0.55, 0.55, 0.2), (-0.65, -0.1, 2.46), P["panel"], root, 0.04)
    cylinder("Hatch", 0.32, 0.08, (-0.55, 0.65, 2.4), P["panel"], root, axis="Z", segments=24, bev=0.02)
    cylinder("Antenna", 0.018, 1.3, (-1.0, -1.9, 3.0), P["steel"], root, axis="Z", segments=8)
    cylinder("AntennaBase", 0.06, 0.1, (-1.0, -1.9, 2.4), P["dark"], root, axis="Z", segments=12)
    cylinder("Beacon", 0.07, 0.1, (1.0, 0.0, 2.43), P["amber"], root, axis="Z", segments=12)
    for s in (-1, 1):
        cylinder(f"RoofRail{s}", 0.025, 3.4, (s * 1.02, -0.4, 2.48), P["steel"], root, axis="Y", segments=8)
        for y in (-2.0, -0.4, 1.2):
            box(f"RailPost{s}{y}", (0.04, 0.04, 0.1), (s * 1.02, y, 2.42), P["steel"], root, 0)
    # Brush guard across the nose.
    cylinder("GuardBar", 0.035, W * 0.8, (0, 2.48, 1.05), P["steel"], root, axis="X", segments=10)
    for x in (-0.6, 0.6):
        cylinder(f"GuardPost{x}", 0.03, 0.4, (x, 2.46, 0.92), P["steel"], root, axis="Z", segments=8)

    # Sensor dish on a mast at the back of the roof (the game spins "Dish").
    dish = empty("Dish", (0, -0.6, 2.36), root)
    cylinder("DishMast", 0.06, 0.6, (0, 0, 0.3), P["steel"], dish, axis="Z", segments=12)
    cylinder("DishMount", 0.12, 0.12, (0, 0, 0.62), P["dark"], dish, axis="Z", segments=16)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=12, radius=0.55)
    for v in list(bm.verts):
        if v.co.z > 0.02:
            bm.verts.remove(v)
    for v in bm.verts:
        v.co.z *= 0.45
    bmesh.ops.contextual_create(bm, geom=[e for e in bm.edges if e.is_boundary])
    d = mesh_object("DishBowl", bm, P["hull"], dish, (0, 0, 0.95), smooth=True)
    d.rotation_euler = (math.radians(180 + 25), 0, 0)
    solid = d.modifiers.new("Thick", "SOLIDIFY")
    solid.thickness = 0.03
    cylinder("DishFeed", 0.02, 0.4, (0, 0.08, 1.12), P["steel"], dish, axis="Z", segments=8)

    # Suspension arms and wheels.
    for y in axles:
        for s in (-1, 1):
            box(f"Arm{y}{s}", (0.5, 0.18, 0.14), (s * (W / 2 - 0.05), y, wheel_r + 0.05), P["steel"], root, 0.03)
            wheel(f"Wheel{'F' if y > 0 else 'M' if y > -1 else 'R'}{'L' if s < 0 else 'R'}", s * (W / 2 + 0.12), y, wheel_r, wheel_w, P, root)
    return root


# ---------------------------------------------------------------- weathering


def weather_hull(obj, res=1024):
    """Bake scuffed paint into the hull: dirt toward the bottom, lighter rub marks, streaks. Smart-UV the hull, bake the
    procedural colour into an image, then swap in a plain image-texture material (what glTF can carry)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 4
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    # Apply modifiers so the UVs match what exports.
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01)
    bpy.ops.object.mode_set(mode="OBJECT")

    image = bpy.data.images.new(f"{obj.name}Paint", res, res)
    bake = material(f"{obj.name}Bake", (0.8, 0.8, 0.8))
    nt = bake.node_tree
    p = nt.nodes["Principled BSDF"]
    coord = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(coord.outputs["Object"], sep.inputs[0])
    # Dirt: darker toward the bottom (object z), broken up by noise.
    grad = nt.nodes.new("ShaderNodeMapRange")
    grad.inputs["From Min"].default_value = 0.8
    grad.inputs["From Max"].default_value = 1.8
    nt.links.new(sep.outputs["Z"], grad.inputs["Value"])
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 3.0
    noise.inputs["Detail"].default_value = 8.0
    nt.links.new(coord.outputs["Object"], noise.inputs["Vector"])
    dirt = nt.nodes.new("ShaderNodeMath")
    dirt.operation = "MULTIPLY_ADD"
    nt.links.new(noise.outputs["Fac"], dirt.inputs[0])
    dirt.inputs[1].default_value = 0.8
    nt.links.new(grad.outputs["Result"], dirt.inputs[2])
    # Rub marks: fine high-frequency noise thresholded.
    fine = nt.nodes.new("ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value = 26.0
    fine.inputs["Detail"].default_value = 4.0
    nt.links.new(coord.outputs["Object"], fine.inputs["Vector"])
    rub = nt.nodes.new("ShaderNodeMapRange")
    rub.inputs["From Min"].default_value = 0.55
    rub.inputs["From Max"].default_value = 0.62
    nt.links.new(fine.outputs["Fac"], rub.inputs["Value"])
    # Streaks: noise stretched along z.
    stretch = nt.nodes.new("ShaderNodeMapping")
    stretch.inputs["Scale"].default_value = (14.0, 14.0, 1.2)
    nt.links.new(coord.outputs["Object"], stretch.inputs["Vector"])
    streak = nt.nodes.new("ShaderNodeTexNoise")
    streak.inputs["Scale"].default_value = 2.0
    nt.links.new(stretch.outputs["Vector"], streak.inputs["Vector"])
    # Colour: clean white-grey → grime (warm dark grey) by dirt, lighter rubs, faint streaks.
    clean = nt.nodes.new("ShaderNodeRGB")
    clean.outputs[0].default_value = (0.8, 0.81, 0.82, 1)
    grime = nt.nodes.new("ShaderNodeRGB")
    grime.outputs[0].default_value = (0.42, 0.38, 0.33, 1)
    mix1 = nt.nodes.new("ShaderNodeMix")
    mix1.data_type = "RGBA"
    dirt_fac = nt.nodes.new("ShaderNodeMapRange")
    dirt_fac.inputs["From Min"].default_value = 0.75
    dirt_fac.inputs["From Max"].default_value = 1.35
    dirt_fac.inputs["To Min"].default_value = 0.75
    dirt_fac.inputs["To Max"].default_value = 0.0
    nt.links.new(dirt.outputs[0], dirt_fac.inputs["Value"])
    nt.links.new(dirt_fac.outputs["Result"], mix1.inputs["Factor"])
    nt.links.new(clean.outputs[0], mix1.inputs["A"])
    nt.links.new(grime.outputs[0], mix1.inputs["B"])
    light = nt.nodes.new("ShaderNodeRGB")
    light.outputs[0].default_value = (0.92, 0.93, 0.94, 1)
    mix2 = nt.nodes.new("ShaderNodeMix")
    mix2.data_type = "RGBA"
    nt.links.new(rub.outputs["Result"], mix2.inputs["Factor"])
    nt.links.new(mix1.outputs["Result"], mix2.inputs["A"])
    nt.links.new(light.outputs[0], mix2.inputs["B"])
    streak_fac = nt.nodes.new("ShaderNodeMapRange")
    streak_fac.inputs["From Min"].default_value = 0.55
    streak_fac.inputs["From Max"].default_value = 0.75
    streak_fac.inputs["To Max"].default_value = 0.4
    nt.links.new(streak.outputs["Fac"], streak_fac.inputs["Value"])
    mix3 = nt.nodes.new("ShaderNodeMix")
    mix3.data_type = "RGBA"
    nt.links.new(streak_fac.outputs["Result"], mix3.inputs["Factor"])
    nt.links.new(mix2.outputs["Result"], mix3.inputs["A"])
    nt.links.new(grime.outputs[0], mix3.inputs["B"])
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(mix3.outputs["Result"], emit.inputs["Color"])
    out = nt.nodes["Material Output"]
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    target = nt.nodes.new("ShaderNodeTexImage")
    target.image = image
    nt.nodes.active = target

    obj.data.materials.clear()
    obj.data.materials.append(bake)
    # Bake rays can hit parts sitting just off the hull (numbers, seams, stripes) and print them into the paint: hide
    # everything else while baking.
    hidden = [o for o in scene.objects if o is not obj and not o.hide_render]
    for o in hidden:
        o.hide_render = True
    scene.cycles.bake_type = "EMIT"
    bpy.ops.object.bake(type="EMIT", margin=4)
    for o in hidden:
        o.hide_render = False
    image.pack()

    # The exported material: the baked colour on a glossy paint.
    final = material(f"{obj.name}Paint", (1, 1, 1), 0.15, 0.42, coat=0.2)
    fnt = final.node_tree
    tex = fnt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    fnt.links.new(tex.outputs["Color"], fnt.nodes["Principled BSDF"].inputs["Base Color"])
    obj.data.materials.clear()
    obj.data.materials.append(final)


# ---------------------------------------------------------------- preview and export


def preview_scene(root, folder):
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
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40)
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))

    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    target = Vector((0, 0, 1.3))
    shots = {
        "front34": (Vector((7.5, 8.5, 3.2)), 40),
        "rear34": (Vector((-7.5, -8.0, 3.6)), 40),
        "side": (Vector((10.5, 0.4, 1.9)), 38),
        "game": (Vector((0.0, 13.0, 15.5)), 30),
    }
    os.makedirs(folder, exist_ok=True)
    for name, (pos, lens) in shots.items():
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{VEHICLE}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def export(root, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        if not o.hide_render:
            o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_lights=False, export_cameras=False)
    print("exported", path)


def main():
    reset()
    builders = {"survey_rover": survey_rover}
    root = builders[VEHICLE]()
    if BAKE:
        weather_hull(bpy.data.objects["Hull"])
    if OUT:
        export(root, OUT)
    if PREVIEW:
        preview_scene(root, PREVIEW)


main()
