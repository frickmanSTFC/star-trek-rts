"""Federation ground vehicles, built in Blender and exported for the Godot prototype (game/models/vehicles/*.glb).

Look: the owner's reference art (art/reference/vehicles-and-outposts.md): boxy white-and-grey six-wheelers with a big
wrap-around tinted windscreen, roof light bars and spot lamps, owner-colour flash stripes, hull numbers and scuffed
paint.

Run (Blender 5.x, or the `bpy` module from PyPI):
  blender --background --python blender/build_vehicles.py -- --vehicle survey_rover --out game/models/vehicles/survey_rover.glb
  python3.11 blender/build_vehicles.py --vehicle survey_rover --preview blender/previews/vehicles --res 960

Survey rover variants (contract in blender/rover_variants.py):
  --tier 1|2|3      Mk I Scout / Mk II Surveyor (default, the original Kalty) / Mk III Explorer
  --name KALTY      painted on both sides and across the back (scaled down to fit when long)
  --number 44       hull number on both sides
  --shots front34,rear34,side,game   which previews to render (default all four)
With --tier or --name the previews are named <vehicle>-t<tier>-<name lower>-<shot>.png, otherwise <vehicle>-<shot>.png.

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
TIER = int(arg("--tier", "2"))
NAME = arg("--name", "KALTY").upper()
NUMBER = arg("--number", "44")
SHOTS = [s.strip() for s in arg("--shots", "front34,rear34,side,game").split(",") if s.strip()]
NAMED = "--tier" in argv or "--name" in argv  # variant previews get the tier and name in their file names
if TIER not in (1, 2, 3):
    sys.exit(f"--tier must be 1, 2 or 3, not {TIER}")
if set(SHOTS) - {"front34", "rear34", "side", "game"}:
    sys.exit(f"--shots takes front34, rear34, side and game, not {', '.join(sorted(set(SHOTS) - {'front34', 'rear34', 'side', 'game'}))}")

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
    """A thin stripe on a side of the hull: a polyline [(y, z)] at plane x, `height` tall, pushed out by outward*1cm.
    A None in the points breaks the stripe (one object, several runs)."""
    bm = bmesh.new()
    runs, run = [], []
    for p in list(points) + [None]:
        if p is None:
            if len(run) > 1:
                runs.append(run)
            run = []
        else:
            run.append(p)
    for run in runs:
        top, bottom = [], []
        for y, z in run:
            top.append(bm.verts.new((x + 0.012 * outward, y, z + height / 2)))
            bottom.append(bm.verts.new((x + 0.012 * outward, y, z - height / 2)))
        for i in range(len(run) - 1):
            f = bm.faces.new((bottom[i], bottom[i + 1], top[i + 1], top[i]))
            if outward < 0:
                f.normal_flip()
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def side_poly(name, pts, x, s, mat, parent=None, shadow=True):
    """A flat polygon [(y, z)] lying on the side plane x (facing s = ±1): flush glass, black surrounds, decals."""
    bm = bmesh.new()
    vs = [bm.verts.new((x, y, z)) for y, z in pts]
    f = bm.faces.new(vs if s > 0 else list(reversed(vs)))
    if f.normal.x * s < 0:
        f.normal_flip()
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = shadow
    return o


def strut(name, a, b, radius, mat, parent=None, segments=10):
    """A round bar from point a to point b (suspension arms, shocks, pylons)."""
    a, b = Vector(a), Vector(b)
    o = cylinder(name, radius, (b - a).length, (a + b) / 2, mat, parent, axis="Z", segments=segments)
    o.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    return o


def ring(name, radius, depth, thickness, loc, mat, parent=None, segments=24):
    """An open tube (rotor guards, pad markings): a cylinder wall with a solidify."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=segments, radius1=radius, radius2=radius, depth=depth)
    o = mesh_object(name, bm, mat, parent, loc, smooth=True)
    m = o.modifiers.new("Thick", "SOLIDIFY")
    m.thickness = thickness
    return o


def text(name, body, size, loc, rot, mat, parent=None, max_width=None):
    """A text label as real geometry (glTF has no text): build the font curve, evaluate it to a mesh, drop the curve.
    Longer than max_width (world units, along the text) → scaled down evenly until it fits."""
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
    if max_width is not None and me.vertices:
        xs = [v.co.x for v in me.vertices]
        width = max(xs) - min(xs)
        if width > max_width:
            f = max_width / width
            me.transform(Matrix.Diagonal((f, f, 1.0, 1.0)))
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


def tier_palette(P):
    """Extra materials for the Mk I and Mk III (kept out of palette() so the Mk II scene is unchanged)."""
    P = dict(P)
    P.update({
        "primer": material("Primer", (0.33, 0.34, 0.34), 0.0, 0.85),
        "wheelsteel": material("WheelSteel", (0.42, 0.43, 0.44), 0.55, 0.5),
        "olive": material("CaseOlive", (0.2, 0.22, 0.14), 0.0, 0.7),
        "gunmetal": material("Gunmetal", (0.16, 0.17, 0.19), 0.8, 0.33),
        "glassblack": material("GlassBlack", (0.015, 0.018, 0.022), 0.2, 0.12, coat=1.0),
        "flushglass": material("FlushGlass", (0.03, 0.06, 0.08), 0.3, 0.03, emission=(0.05, 0.12, 0.16), strength=0.05, coat=1.0),
        "blue": material("RunLight", (0.02, 0.2, 1.0), 0.0, 0.25, emission=(0.0, 0.18, 1.0), strength=2.5),
        "array": material("SensorArray", (0.04, 0.07, 0.13), 0.6, 0.25, emission=(0.1, 0.35, 0.9), strength=0.8),
        "padmark": material("PadMark", (0.85, 0.62, 0.08), 0.0, 0.5),
    })
    return P


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


def steel_wheel(name, x, y, radius, width, P, parent):
    """Mk I: plain pressed-steel wheel (painted disc with round holes, small cap, five nuts) on a ribbed tyre."""
    w = empty(name, (x, y, radius), parent)
    side = 1 if x > 0 else -1
    cylinder(f"{name}Tyre", radius, width, (0, 0, 0), P["tyre"], w, bev=0.05)
    ribs = 20
    for k in range(ribs):
        a = 2 * math.pi * k / ribs
        box(f"{name}Tread{k}", (width * 0.9, 0.07, 0.04), (0, math.cos(a) * radius, math.sin(a) * radius), P["tyre"], w,
            bev=0.008, rot=(a + math.pi / 2, 0, 0))
    cylinder(f"{name}Rim", radius * 0.64, width * 1.02, (0, 0, 0), P["wheelsteel"], w, bev=0.02)
    cylinder(f"{name}Disc", radius * 0.56, 0.04, (side * width * 0.51, 0, 0), P["wheelsteel"], w, segments=28, bev=0.012)
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.26
        cylinder(f"{name}Hole{k}", radius * 0.075, 0.02, (side * width * 0.535, math.cos(a) * radius * 0.4, math.sin(a) * radius * 0.4),
                 P["dark"], w, segments=12)
    cylinder(f"{name}Cap", radius * 0.15, 0.07, (side * width * 0.55, 0, 0), P["steel"], w, segments=14, bev=0.015)
    for k in range(5):
        a = 2 * math.pi * k / 5
        cylinder(f"{name}Nut{k}", 0.022, 0.04, (side * width * 0.55, math.cos(a) * radius * 0.22, math.sin(a) * radius * 0.22),
                 P["steel"], w, segments=6)
    return w


def explorer_wheel(name, x, y, radius, width, P, parent):
    """Mk III: big all-terrain tyre (two staggered rows of chunky blocks) on a six-spoke gunmetal rim."""
    w = empty(name, (x, y, radius), parent)
    side = 1 if x > 0 else -1
    cylinder(f"{name}Tyre", radius, width, (0, 0, 0), P["tyre"], w, segments=32, bev=0.09)
    blocks = 18
    for row in (-1, 1):
        for k in range(blocks):
            a = 2 * math.pi * (k + (0.5 if row > 0 else 0.0)) / blocks
            box(f"{name}Tread{row}{k}", (width * 0.44, 0.16, 0.085), (row * width * 0.24, math.cos(a) * radius, math.sin(a) * radius),
                P["tyre"], w, bev=0.015, rot=(a + math.pi / 2, 0, 0))
    cylinder(f"{name}Rim", radius * 0.6, width * 1.02, (0, 0, 0), P["gunmetal"], w, segments=32, bev=0.03)
    cylinder(f"{name}Disc", radius * 0.52, 0.04, (side * width * 0.5, 0, 0), P["dark"], w, segments=32)
    for k in range(6):
        a = 2 * math.pi * k / 6
        b = box(f"{name}Spoke{k}", (0.05, 0.075, radius * 0.36), (side * width * 0.53, math.cos(a) * radius * 0.33,
                                                                      math.sin(a) * radius * 0.33), P["hub"], w, bev=0.012)
        b.rotation_euler = (a - math.pi / 2, 0, 0)
    cylinder(f"{name}Cap", radius * 0.17, 0.09, (side * width * 0.56, 0, 0), P["steel"], w, segments=18, bev=0.02)
    for k in range(8):
        a = 2 * math.pi * k / 8
        cylinder(f"{name}Bolt{k}", 0.022, 0.04, (side * width * 0.585, math.cos(a) * radius * 0.11, math.sin(a) * radius * 0.11),
                 P["dark"], w, segments=6)
    return w


# ---------------------------------------------------------------- survey rover


def rover_mk2(P):
    """Mk II Surveyor, the original Kalty. Six-wheeled survey scout in the Grizzly Bear / G.A.V. style: wedge nose with a wrap-around windscreen, roof
    light bar, sensor dish ("Dish", spins in the game), owner-colour flash stripes, hull number."""
    root = empty("SurveyRover")
    L, W = 4.6, 2.45            # body length (y) and width (x)
    wheel_r, wheel_w = 0.6, 0.42
    axles = (1.5, -0.4, -1.75)  # front, middle, rear: the back pair close together like the art, 0.15 clear between tyres

    # Main hull: side profile (y forward, z up), wedge nose and windscreen slope, slight tumblehome above z 1.7.
    profile = [(-2.3, 0.78), (2.08, 0.78), (2.3, 0.98), (2.32, 1.38), (2.1, 1.58), (1.38, 2.28), (1.1, 2.36), (-2.05, 2.36),
               (-2.3, 2.12)]
    hull = prism("Hull", profile, W, P["hull"], root, taper_above=1.7, taper=0.93, bev=0.07)
    hull["dirt_band"] = (0.8, 1.8)  # object z where the weathering's dirt fades out (see weather_hull)
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
        # Door between the front and middle wheel arches, with its step; a vent grille above the back pair.
        box(f"DoorSeam{s}", (0.02, 0.03, 0.58), (s * (W / 2 + 0.003), 0.86, 1.28), P["dark"], root, 0)
        box(f"DoorSeamB{s}", (0.02, 0.03, 0.58), (s * (W / 2 + 0.003), 0.22, 1.28), P["dark"], root, 0)
        box(f"DoorSeamT{s}", (0.02, 0.67, 0.03), (s * (W / 2 + 0.003), 0.54, 1.56), P["dark"], root, 0)
        box(f"Handle{s}", (0.04, 0.16, 0.04), (s * (W / 2 + 0.02), 0.72, 1.3), P["steel"], root, 0.01)
        for k in range(4):
            box(f"Vent{s}{k}", (0.02, 0.4, 0.025), (s * (W / 2 + 0.004), -1.08, 1.38 + k * 0.05), P["dark"], root, 0)
        box(f"Step{s}", (0.22, 0.5, 0.06), (s * (W / 2 + 0.05), 0.54, 0.86), P["steel"], root, 0.015)
        # Owner-colour flash: low along the side, sweeping up over the cab like the reference art.
        ribbon(f"Flash{s}", [(-2.28, 1.42), (-0.2, 1.42), (0.6, 1.52), (1.7, 1.52)], 0.09, P["owner"], root, x=s * W / 2, outward=s)
        ribbon(f"FlashB{s}", [(-2.28, 1.27), (-0.35, 1.27)], 0.04, P["owner"], root, x=s * W / 2, outward=s)
        # Hull number.
        rot = (math.radians(90), 0, math.radians(90 if s > 0 else -90))
        # On the upper rear side, lying flat on the wall, which leans in by `lean` above z 1.58 (tumblehome).
        lean = math.atan2(W / 2 * 0.07, 2.36 - 1.58)
        z = 1.95
        num = text(f"Number{s}", NUMBER, 0.36, (0, 0, 0), (math.radians(90), 0, math.radians(90 if s > 0 else -90)), P["number"], root, max_width=0.55)
        num.matrix_basis = (Matrix.Translation((s * (W / 2 - math.tan(lean) * (z - 1.58) + 0.004), -1.75, z))
                            @ Matrix.Rotation(-s * lean, 4, "Y") @ num.rotation_euler.to_matrix().to_4x4())
        # The rover's name, above the door.
        name = text(f"Name{s}", NAME, 0.2, (0, 0, 0), (math.radians(90), 0, math.radians(90 if s > 0 else -90)), P["number"], root, max_width=0.95)
        zn = 1.92
        name.matrix_basis = (Matrix.Translation((s * (W / 2 - math.tan(lean) * (zn - 1.58) + 0.004), 0.25, zn))
                             @ Matrix.Rotation(-s * lean, 4, "Y") @ name.rotation_euler.to_matrix().to_4x4())

    # Name across the back.
    text("NameRear", NAME, 0.22, (0, -2.31, 1.62), (math.radians(90), 0, 0), P["number"], root, max_width=1.4)  # faces -Y, reads left to right from behind

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


def on_slope(lo, hi):
    """Frame for parts lying on a sloped hull face from lo (y, z) up to hi (y, z): midpoint, outward normal, the unit
    vector up the slope and the X rotation that turns a box's thin Y axis onto the normal."""
    d = Vector((0, hi[0] - lo[0], hi[1] - lo[1]))
    along = d.normalized()
    n = Vector((0, along.z, -along.y))  # outward: forward and up for a windscreen leaning back
    if n.y < 0:
        n = -n
    mid = Vector((0, (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2))
    return mid, n, along, math.atan2(d.z, d.y) - math.pi / 2


def cut_arches(hull, axles, radius, centre_z, width, side_only=None):
    """Boolean the wheel arches out of the hull: full-width cylinders, or (side_only = depth) one short cylinder per
    side so the hull keeps a floor between the wheel wells. The bevel stays last so the arch edges get rounded too."""
    for y in axles:
        cuts = [(0, width + 0.4)] if side_only is None else [(s * width / 2, side_only * 2) for s in (-1, 1)]
        for x, depth in cuts:
            cutter = cylinder("ArchCut", radius, depth, (x, y, centre_z), bpy.data.materials["Chassis"], None, segments=36)
            mod = hull.modifiers.new("Arch", "BOOLEAN")
            mod.operation = "DIFFERENCE"
            mod.solver = "EXACT"
            mod.object = cutter
            cutter.hide_render = True
            cutter.hide_viewport = True
    hull.modifiers.move(hull.modifiers.find("Bevel"), len(hull.modifiers) - 1)


def rover_mk1(P):
    """Mk I Scout, early exploration: the same family a generation older. Four wheels on a short boxy body with square
    arches, split flat windscreen with a centre post, round headlamps, roof rack with cases and a spare, two roof spots,
    whip antenna and a small sensor puck (as "Dish"), pressed-steel wheels and a few primer-grey replacement panels."""
    P = tier_palette(P)
    root = empty("SurveyRover")
    L, W = 4.0, 2.3
    wheel_r, wheel_w = 0.52, 0.36
    axles = (1.25, -1.2)
    profile = [(-2.0, 0.72), (1.82, 0.72), (2.0, 0.88), (2.0, 1.36), (1.86, 1.48), (1.5, 2.16), (-1.92, 2.16), (-2.0, 2.08)]
    hull = prism("Hull", profile, W, P["hull"], root, bev=0.035)
    hull["dirt_band"] = (0.74, 1.74)
    hull["paint"] = (0.77, 0.76, 0.7)  # older, yellowed white
    # Square-cut arches (a box per axle) with bolted-on flares: cruder than the Mk II's round ones.
    for y in axles:
        cutter = box("ArchCut", (W + 0.4, 2 * (wheel_r + 0.12), 2 * (wheel_r + 0.1)), (0, y, wheel_r), P["dark"], None, bev=0)
        mod = hull.modifiers.new("Arch", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cutter
        cutter.hide_render = True
        cutter.hide_viewport = True
        top = 2 * wheel_r + 0.1
        for s in (-1, 1):
            box(f"Flare{y}{s}", (0.08, 2 * (wheel_r + 0.12) + 0.16, 0.07), (s * (W / 2 + 0.03), y, top + 0.02), P["dark"], root, 0.015)
            for e in (-1, 1):
                box(f"FlareLeg{y}{s}{e}", (0.08, 0.07, top - 0.72 + 0.02), (s * (W / 2 + 0.03), y + e * (wheel_r + 0.155), (top + 0.72) / 2),
                    P["dark"], root, 0.015)
    hull.modifiers.move(hull.modifiers.find("Bevel"), len(hull.modifiers) - 1)

    # Chassis and plain box bumpers with tow hooks.
    box("Skirt", (W - 0.24, L - 0.5, 0.26), (0, 0, 0.6), P["dark"], root, 0.03)
    box("FrontBumper", (W + 0.08, 0.16, 0.2), (0, 2.08, 0.84), P["dark"], root, 0.025)
    box("RearBumper", (W + 0.04, 0.16, 0.2), (0, -2.08, 0.84), P["dark"], root, 0.025)
    for x in (-0.7, 0.7):
        box(f"Tow{x}", (0.08, 0.1, 0.12), (x, 2.19, 0.84), P["steel"], root, 0.015)

    # Split flat windscreen: two panes in a frame with a centre post, wipers hanging from the top.
    mid, n, along, rx = on_slope((1.86, 1.48), (1.5, 2.16))
    box("WindscreenFrame", (W * 0.88, 0.03, 0.68), mid + n * 0.008, P["frame"], root, 0.015, rot=(rx, 0, 0))
    bm = bmesh.new()
    for x in (-0.49, 0.49):
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((x, 0, 0)) @ Matrix.Diagonal((0.9, 0.03, 0.56, 1)))
    ws = mesh_object("Windscreen", bm, P["glass"], root, mid + n * 0.02)
    ws.rotation_euler = (rx, 0, 0)
    box("WindscreenPost", (0.08, 0.05, 0.68), mid + n * 0.025, P["frame"], root, 0.01, rot=(rx, 0, 0))
    for x in (-0.5, 0.48):
        box(f"Wiper{x}", (0.018, 0.018, 0.42), mid + n * 0.045 + along * 0.06 + Vector((x, 0, 0)), P["dark"], root, 0, rot=(rx, 0, 0))

    # Front: flat slatted grille, round headlamps in square bezels, amber markers.
    box("Grille", (1.0, 0.04, 0.34), (0, 2.02, 1.12), P["dark"], root, 0.01)
    for k in range(7):
        box(f"Slat{k}", (0.03, 0.03, 0.3), (-0.39 + k * 0.13, 2.045, 1.12), P["steel"], root, 0)
    for s in (-1, 1):
        box(f"HeadlampFrame{s}", (0.3, 0.04, 0.3), (s * 0.78, 2.02, 1.12), P["dark"], root, 0.02)
        cylinder(f"HeadlampRim{s}", 0.12, 0.04, (s * 0.78, 2.04, 1.12), P["steel"], root, axis="Y", segments=20)
        cylinder(f"Headlamp{s}", 0.1, 0.04, (s * 0.78, 2.055, 1.12), P["lamp"], root, axis="Y", segments=20)
        box(f"Marker{s}", (0.1, 0.04, 0.07), (s * 1.0, 2.02, 1.31), P["amber"], root, 0.01)
        cylinder(f"Tail{s}", 0.07, 0.04, (s * 0.95, -2.015, 1.2), P["redlamp"], root, axis="Y", segments=16)
        box(f"TailFrame{s}", (0.2, 0.02, 0.2), (s * 0.95, -2.004, 1.2), P["dark"], root, 0.01)

    for s in (-1, 1):
        x = s * (W / 2 + 0.005)
        # Small flat cab window and a rear side window, each in a plain frame.
        box(f"SideGlassFrame{s}", (0.02, 0.58, 0.42), (s * (W / 2 + 0.002), 1.2, 1.85), P["panel"], root, 0.01)
        box(f"SideGlass{s}", (0.03, 0.5, 0.34), (x + s * 0.004, 1.2, 1.85), P["glass"], root, 0.008)
        box(f"SideWindowFrame{s}", (0.02, 0.53, 0.4), (s * (W / 2 + 0.002), -1.3, 1.85), P["panel"], root, 0.01)
        box(f"SideWindow{s}", (0.03, 0.45, 0.32), (x + s * 0.004, -1.3, 1.85), P["glass"], root, 0.008)
        # Replacement door in primer grey (the stripe stops at it), hinges, handle and a step.
        door = box(f"DoorPanel{s}", (0.012, 0.8, 0.9), (s * (W / 2 + 0.006), 0.05, 1.3), P["primer"], root, 0.004)
        door.visible_shadow = False
        for z in (1.0, 1.6):
            box(f"Hinge{s}{z}", (0.04, 0.07, 0.1), (s * (W / 2 + 0.02), -0.38, z), P["steel"], root, 0.01)
        box(f"Handle{s}", (0.04, 0.14, 0.04), (s * (W / 2 + 0.025), 0.36, 1.45), P["steel"], root, 0.01)
        box(f"Step{s}", (0.2, 0.5, 0.05), (s * (W / 2 + 0.06), 0.05, 0.8), P["steel"], root, 0.012)
        for e in (-1, 1):
            box(f"StepBracket{s}{e}", (0.04, 0.04, 0.12), (s * (W / 2 + 0.02), 0.05 + e * 0.2, 0.86), P["dark"], root, 0)
        # Plain owner stripe along the waist, broken by the primer door.
        ribbon(f"Flash{s}", [(-1.95, 1.48), (-0.37, 1.48), None, (0.47, 1.48), (1.84, 1.48)], 0.08, P["owner"], root, x=s * W / 2, outward=s)
        # Hull number stencilled on the door; the name above it.
        text(f"Number{s}", NUMBER, 0.3, (s * (W / 2 + 0.014), 0.05, 1.15), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], root, max_width=0.6)
        text(f"Name{s}", NAME, 0.17, (s * (W / 2 + 0.004), 0.05, 1.95), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], root, max_width=1.0)
        # Riveted panel seam under the windows.
        box(f"Seam{s}", (0.015, 3.6, 0.02), (s * (W / 2 + 0.003), -0.1, 1.64), P["dark"], root, 0)
        for k in range(16):
            yk = -1.85 + k * 0.24
            if -0.4 < yk < 0.5:
                continue
            cylinder(f"Rivet{s}{k}", 0.014, 0.02, (s * (W / 2 + 0.006), yk, 1.6), P["steel"], root, axis="X", segments=6)
    # Odd primer patches: left front fender, the right half of the back.
    p = box("PrimerFender", (0.012, 0.5, 0.24), (-(W / 2 + 0.006), 1.55, 1.3), P["primer"], root, 0.004)
    p.visible_shadow = False
    p = box("PrimerRear", (0.8, 0.012, 0.6), (0.4, -2.006, 1.22), P["primer"], root, 0.004)
    p.visible_shadow = False
    text("NameRear", NAME, 0.2, (0, -2.008, 1.82), (math.radians(90), 0, 0), P["number"], root, max_width=1.5)
    for x in (-0.55, 0.55):
        cylinder(f"Grab{x}", 0.02, 0.3, (x, -2.035, 1.55), P["steel"], root, axis="X", segments=8)

    # Roof rack: tube rails on posts, cross bars, cases, a spare wheel lying flat.
    zr = 2.32
    for s in (-1, 1):
        cylinder(f"RoofRail{s}", 0.025, 3.1, (s * 0.95, -0.2, zr), P["steel"], root, axis="Y", segments=8)
        for y in (1.3, 0.0, -1.7):
            box(f"RailPost{s}{y}", (0.05, 0.05, 0.16), (s * 0.95, y, 2.24), P["steel"], root, 0)
    for y in (1.3, 0.55, -0.2, -0.95, -1.7):
        cylinder(f"RackBar{y}", 0.022, 1.9, (0, y, zr), P["steel"], root, axis="X", segments=8)
    box("Locker", (0.8, 0.55, 0.3), (0.45, -1.25, zr + 0.17), P["primer"], root, 0.03)
    box("LockerB", (0.6, 0.45, 0.26), (-0.45, -1.35, zr + 0.15), P["panel"], root, 0.03)
    box("CaseC", (0.5, 0.4, 0.22), (0.5, -0.5, zr + 0.13), P["olive"], root, 0.025)
    for x, y, sx in ((0.45, -1.25, 0.8), (-0.45, -1.35, 0.6)):
        box(f"Strap{x}", (sx + 0.02, 0.05, 0.02), (x, y, zr + 0.31 if x > 0 else zr + 0.27), P["dark"], root, 0)
    spare = steel_wheel("SpareTyre", 0, 0, wheel_r * 0.95, wheel_w * 0.9, P, root)
    spare.location = (-0.4, -0.35, zr + 0.03 + wheel_w * 0.45)
    spare.rotation_euler = (0, math.radians(90), 0)

    # Two spot lamps on brackets at the front corners of the roof; beacon; whip antenna.
    for k, x in enumerate((-0.8, 0.8)):
        box(f"SpotBracket{k}", (0.05, 0.05, 0.2), (x, 1.42, 2.26), P["dark"], root, 0)
        cylinder(f"SpotHousing{k}", 0.11, 0.14, (x, 1.42, 2.42), P["dark"], root, axis="Y", segments=18, bev=0.02)
        cylinder(f"Spot{k}", 0.09, 0.03, (x, 1.495, 2.42), P["lamp"], root, axis="Y", segments=18)
    cylinder("Beacon", 0.07, 0.12, (-0.75, -1.85, 2.22), P["amber"], root, axis="Z", segments=12)
    cylinder("AntennaBase", 0.05, 0.12, (0.95, -1.85, 2.22), P["dark"], root, axis="Z", segments=12)
    cylinder("AntennaSpring", 0.022, 0.12, (0.95, -1.85, 2.34), P["steel"], root, axis="Z", segments=8)
    cylinder("Antenna", 0.012, 2.0, (0.95, -1.85, 3.4), P["steel"], root, axis="Z", segments=6, radius2=0.005)
    cylinder("AntennaTip", 0.02, 0.03, (0.95, -1.85, 4.41), P["dark"], root, axis="Z", segments=8)

    # Sensor puck on a short mast in the middle of the rack ("Dish" so the game still spins it).
    dish = empty("Dish", (0, 0.45, 2.16), root)
    cylinder("DishMast", 0.04, 0.5, (0, 0, 0.25), P["steel"], dish, axis="Z", segments=10)
    cylinder("DishMount", 0.08, 0.06, (0, 0, 0.5), P["dark"], dish, axis="Z", segments=14)
    cylinder("DishPuck", 0.2, 0.1, (0, 0, 0.58), P["hull"], dish, axis="Z", segments=28, bev=0.025)
    cylinder("DishBand", 0.205, 0.03, (0, 0, 0.58), P["dark"], dish, axis="Z", segments=28)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=10, radius=0.1)
    for v in bm.verts:
        v.co.z = max(v.co.z, 0) * 0.6
    mesh_object("DishDome", bm, P["panel"], dish, (0, 0, 0.63), smooth=True)
    box("DishSensor", (0.08, 0.04, 0.05), (0, 0.19, 0.58), P["glass"], dish, 0.01)

    # Simple trailing arms and the four wheels.
    for y in axles:
        for s in (-1, 1):
            box(f"Arm{y}{s}", (0.4, 0.14, 0.12), (s * (W / 2 - 0.05), y, wheel_r + 0.03), P["steel"], root, 0.02)
            steel_wheel(f"Wheel{'F' if y > 0 else 'R'}{'L' if s < 0 else 'R'}", s * (W / 2 + 0.1), y, wheel_r, wheel_w, P, root)
    return root


def rover_mk3(P):
    """Mk III Explorer, deep exploration: longer and sleeker six-wheeler on big tyres and raised double-wishbone
    suspension, flush black-surround glazing, a phased-array sensor turret (as "Dish"), side sensor pods, a roof pad with
    a quad scout drone ("Drone"), shield-emitter nubs at the corners and a blue running-light strip."""
    P = tier_palette(P)
    root = empty("SurveyRover")
    L, W = 5.2, 2.55
    wheel_r, wheel_w = 0.7, 0.5
    axles = (1.8, -0.05, -1.75)  # tyres 1.4 across: 0.45 and 0.3 clear between neighbours
    profile = [(-2.6, 1.05), (2.35, 1.05), (2.6, 1.3), (2.62, 1.62), (2.42, 1.86), (1.55, 2.62), (1.2, 2.72), (-2.25, 2.72),
               (-2.6, 2.42)]
    hull = prism("Hull", profile, W, P["hull"], root, bev=0.09)
    hull["dirt_band"] = (1.07, 2.07)
    # Wheel wells cut from the sides only, set high above the tyres (raised suspension).
    cut_arches(hull, axles, wheel_r + 0.16, 0.8, W, side_only=0.45)

    # Chassis tub, sculpted bumpers, skid plate.
    box("Skirt", (W - 0.6, L - 0.6, 0.32), (0, 0, 0.98), P["dark"], root, 0.05)
    box("FrontBumper", (W * 0.92, 0.26, 0.2), (0, 2.55, 1.12), P["gunmetal"], root, 0.06)
    box("SkidPlate", (1.6, 0.5, 0.05), (0, 2.25, 0.9), P["steel"], root, 0.02, rot=(math.radians(18), 0, 0))
    box("RearBumper", (W * 0.9, 0.2, 0.18), (0, -2.66, 1.12), P["gunmetal"], root, 0.05)

    # Flush glazing: tinted glass on a piano-black surround, wrapping from the windscreen down both sides.
    mid, n, along, rx = on_slope((2.42, 1.86), (1.55, 2.62))
    box("WindscreenSurround", (W - 0.12, 0.01, 1.0), mid + n * 0.006, P["glassblack"], root, 0, rot=(rx, 0, 0))
    box("Windscreen", (W - 0.24, 0.012, 0.9), mid + n * 0.012, P["flushglass"], root, 0.004, rot=(rx, 0, 0))
    for s in (-1, 1):
        side_poly(f"SideGlassSurround{s}", [(1.52, 2.58), (2.26, 1.94), (0.04, 1.94), (0.04, 2.58)], s * (W / 2 + 0.003), s, P["glassblack"], root)
        side_poly(f"SideGlass{s}", [(1.48, 2.545), (2.18, 1.975), (0.08, 1.975), (0.08, 2.545)], s * (W / 2 + 0.007), s, P["flushglass"], root)
        side_poly(f"SideWindowSurround{s}", [(-1.11, 1.98), (-1.69, 1.98), (-1.69, 2.54), (-1.11, 2.54)], s * (W / 2 + 0.003), s,
                  P["glassblack"], root)
        side_poly(f"SideWindow{s}", [(-1.15, 2.015), (-1.65, 2.015), (-1.65, 2.505), (-1.15, 2.505)], s * (W / 2 + 0.007), s,
                  P["flushglass"], root)
        # High door between the front and middle wells (the hull rides high), glazed flush under the long glass band,
        # with a two-rung step below it.
        for y in (0.12, 0.98):
            box(f"DoorSeam{s}{y}", (0.02, 0.025, 0.24), (s * (W / 2 + 0.004), y, 1.82), P["dark"], root, 0)
        box(f"DoorSeamT{s}", (0.02, 0.86, 0.025), (s * (W / 2 + 0.004), 0.55, 1.71), P["dark"], root, 0)
        box(f"Handle{s}", (0.03, 0.16, 0.03), (s * (W / 2 + 0.016), 0.84, 1.84), P["steel"], root, 0.008)
        for z in (1.0, 1.35):
            box(f"Step{s}{z}", (0.18, 0.16, 0.04), (s * (W / 2 + 0.08), 0.88, z), P["steel"], root, 0.01)
        for y in (0.8, 0.96):
            strut(f"StepRail{s}{y}", (s * (W / 2 + 0.08), y, 0.96), (s * (W / 2 + 0.08), y, 1.6), 0.014, P["steel"], root)
        box(f"StepHanger{s}", (0.1, 0.2, 0.04), (s * (W / 2 + 0.04), 0.88, 1.6), P["dark"], root, 0.01)
        # Blue running-light strip along the waist, the owner flash just below it.
        ribbon(f"RunLight{s}", [(-2.48, 1.9), (2.3, 1.9)], 0.035, P["blue"], root, x=s * W / 2, outward=s)
        ribbon(f"Flash{s}", [(-2.48, 1.76), (0.3, 1.76), (1.3, 1.8), (2.36, 1.8)], 0.07, P["owner"], root, x=s * W / 2, outward=s)
        text(f"Name{s}", NAME, 0.2, (s * (W / 2 + 0.004), -0.55, 2.26), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], root, max_width=0.8)
        text(f"Number{s}", NUMBER, 0.32, (s * (W / 2 + 0.004), -2.12, 2.26), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], root, max_width=0.5)
        # Side sensor pod on a pylon at the roof edge: a capsule with a glowing lens up front.
        pod = empty(f"SensorPod{s}", (s * (W / 2 + 0.06), -0.3, 2.84), root)
        box(f"SensorPylon{s}", (0.2, 0.3, 0.12), (-s * 0.08, 0, -0.07), P["gunmetal"], pod, 0.03)
        cylinder(f"SensorPodBody{s}", 0.1, 0.62, (0, 0, 0), P["hull"], pod, axis="Y", segments=20, bev=0.03)
        cylinder(f"SensorPodNose{s}", 0.1, 0.16, (0, 0.39, 0), P["gunmetal"], pod, axis="Y", segments=20, radius2=0.06)
        cylinder(f"SensorPodLens{s}", 0.055, 0.02, (0, 0.475, 0), P["blue"], pod, axis="Y", segments=16)
        cylinder(f"SensorPodTail{s}", 0.1, 0.1, (0, -0.36, 0), P["gunmetal"], pod, axis="Y", segments=20, radius2=0.07)

    # Front: slim LED headlamps, intake slot, a blue running-light bar; rear: slim tail lamps, name, lockers.
    for s in (-1, 1):
        box(f"HeadlampFrame{s}", (0.52, 0.03, 0.11), (s * 0.78, 2.622, 1.55), P["glassblack"], root, 0.01)
        box(f"Headlamp{s}", (0.46, 0.03, 0.06), (s * 0.78, 2.632, 1.55), P["lamp"], root, 0.008)
        box(f"Marker{s}", (0.1, 0.03, 0.05), (s * 1.12, 2.625, 1.55), P["amber"], root, 0.008)
        box(f"Tail{s}", (0.08, 0.03, 0.6), (s * 1.1, -2.612, 1.95), P["redlamp"], root, 0.01)
    box("Grille", (0.9, 0.03, 0.12), (0, 2.625, 1.55), P["dark"], root, 0.01)
    box("RunLightFront", (1.9, 0.02, 0.035), (0, 2.615, 1.42), P["blue"], root, 0)
    text("NameRear", NAME, 0.22, (0, -2.612, 2.12), (math.radians(90), 0, 0), P["number"], root, max_width=1.5)
    box("Locker", (0.85, 0.16, 0.42), (-0.55, -2.68, 1.45), P["panel"], root, 0.03)
    box("LockerB", (0.85, 0.16, 0.42), (0.55, -2.68, 1.45), P["panel"], root, 0.03)
    for x in (-0.55, 0.55):
        box(f"LockerLatch{x}", (0.12, 0.03, 0.04), (x, -2.765, 1.55), P["steel"], root, 0.005)

    # Roof: slim light bar, the sensor turret ("Dish"), drone pad with the drone, antenna, beacon, shield emitters.
    box("LightBar", (1.7, 0.24, 0.1), (0, 0.95, 2.77), P["gunmetal"], root, 0.03)
    for k, x in enumerate((-0.6, -0.2, 0.2, 0.6)):
        cylinder(f"Spot{k}", 0.06, 0.04, (x, 1.06, 2.77), P["lamp"], root, axis="Y", segments=16)
    cylinder("Beacon", 0.06, 0.08, (0.9, -2.12, 2.76), P["amber"], root, axis="Z", segments=12)
    box("AntennaBase", (0.06, 0.16, 0.06), (-0.9, -2.12, 2.75), P["dark"], root, 0.01)
    cylinder("Antenna", 0.012, 0.9, (-0.9, -2.12, 3.2), P["steel"], root, axis="Z", segments=6, radius2=0.006)
    k = 0
    for x in (-1, 1):
        for y in (1.05, -2.2):
            emitter(f"Emitter{k}", (x * (W / 2 - 0.12), y, 2.72), (0, 0, 1), P, root)
            k += 1
        emitter(f"Emitter{k}", (x * (W / 2 - 0.18), 2.608, 1.4), (0, 1, 0), P, root)
        emitter(f"Emitter{k + 1}", (x * (W / 2 - 0.18), -2.6, 1.3), (0, -1, 0), P, root)
        k += 2

    dish = empty("Dish", (0, -0.15, 2.72), root)
    cylinder("TurretRing", 0.45, 0.06, (0, 0, 0.03), P["gunmetal"], dish, axis="Z", segments=32, bev=0.015)
    cylinder("TurretBody", 0.37, 0.22, (0, 0, 0.17), P["hull"], dish, axis="Z", segments=8, radius2=0.3, bev=0.03)
    box("TurretYoke", (1.06, 0.16, 0.06), (0, 0, 0.31), P["gunmetal"], dish, 0.02)
    for x in (-0.5, 0.5):
        box(f"TurretArm{x}", (0.06, 0.12, 0.34), (x, 0, 0.47), P["gunmetal"], dish, 0.015)
    head = box("ArrayHead", (0.9, 0.08, 0.5), (0, 0, 0.58), P["hull"], dish, 0.02, rot=(math.radians(20), 0, 0))
    box("ArrayBack", (0.56, 0.16, 0.32), (0, -0.1, 0), P["gunmetal"], head, 0.03)
    for i in range(6):
        for j in range(3):
            box(f"ArrayTile{i}{j}", (0.12, 0.02, 0.12), (-0.35 + i * 0.14, 0.045, -0.15 + j * 0.15), P["array"], head, 0.005)
    cylinder("TurretStub", 0.025, 0.2, (0, -0.1, 0.9), P["steel"], dish, axis="Z", segments=8)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=0.07)
    mesh_object("TurretDome", bm, P["glassblack"], dish, (0, -0.1, 1.04), smooth=True)

    box("DronePad", (1.25, 1.25, 0.05), (0, -1.55, 2.745), P["gunmetal"], root, 0.015)
    ring("PadRing", 0.5, 0.006, 0.05, (0, -1.55, 2.773), P["padmark"], root, segments=40)
    for x in (-0.55, 0.55):
        for y in (-1.0, -2.1):
            cylinder(f"PadLight{x}{y}", 0.025, 0.02, (x, y, 2.775), P["amber"], root, axis="Z", segments=8)
    drone(P, root, (0, -1.55, 2.77))

    # Raised double-wishbone suspension with owner-colour coil-overs, and the big wheels.
    for y in axles:
        for s in (-1, 1):
            xo = W / 2 + 0.15
            strut(f"ArmLow{y}{s}", (s * (W / 2 - 0.42), y, 0.88), (s * (xo - 0.3), y, 0.62), 0.045, P["steel"], root)
            strut(f"ArmUp{y}{s}", (s * (W / 2 - 0.42), y, 1.1), (s * (xo - 0.3), y, 0.86), 0.035, P["steel"], root)
            box(f"Upright{y}{s}", (0.08, 0.14, 0.34), (s * (xo - 0.3), y, 0.74), P["dark"], root, 0.02)
            strut(f"Shock{y}{s}", (s * (xo - 0.34), y - 0.12, 0.78), (s * (W / 2 - 0.36), y - 0.12, 1.62), 0.04, P["steel"], root)
            strut(f"Coil{y}{s}", (s * (xo - 0.345), y - 0.12, 0.9), (s * (W / 2 - 0.37), y - 0.12, 1.4), 0.065, P["owner"], root)
            explorer_wheel(f"Wheel{'F' if y > 1 else 'M' if y > -1 else 'R'}{'L' if s < 0 else 'R'}", s * xo, y, wheel_r, wheel_w, P, root)
    return root


def emitter(name, loc, facing, P, parent):
    """Shield-emitter nub: a low dark dome with a glowing blue core, sitting on the face it points out of."""
    e = empty(name, loc, parent)
    e.rotation_euler = Vector(facing).to_track_quat("Z", "Y").to_euler()
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=0.085)
    for v in bm.verts:
        v.co.z = max(v.co.z, 0) * 0.6
    mesh_object(f"{name}Dome", bm, P["gunmetal"], e, smooth=True)
    cylinder(f"{name}Core", 0.04, 0.02, (0, 0, 0.047), P["blue"], e, axis="Z", segments=12)
    return e


def drone(P, parent, loc):
    """Small quad scout drone ("Drone") sitting on the Mk III's roof pad: body, crossed arms, four guarded rotors, skids."""
    d = empty("Drone", loc, parent)
    d.scale = (1.25, 1.25, 1.25)
    for x in (-0.15, 0.15):
        cylinder(f"DroneSkid{x}", 0.015, 0.42, (x, 0, 0.015), P["steel"], d, axis="Y", segments=8)
        for y in (-0.12, 0.12):
            strut(f"DroneLeg{x}{y}", (x, y, 0.015), (x * 0.6, y * 0.8, 0.08), 0.01, P["steel"], d, segments=6)
    box("DroneBody", (0.3, 0.38, 0.11), (0, 0, 0.12), P["hull"], d, 0.035)
    box("DroneStripe", (0.31, 0.06, 0.02), (0, 0.08, 0.17), P["owner"], d, 0)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=6, radius=0.05)
    mesh_object("DroneEye", bm, P["glassblack"], d, (0, 0.18, 0.08), smooth=True)
    cylinder("DroneLamp", 0.02, 0.012, (0, -0.05, 0.18), P["blue"], d, axis="Z", segments=8)
    for a in (45, 135):
        box(f"DroneArm{a}", (0.86, 0.04, 0.03), (0, 0, 0.14), P["gunmetal"], d, 0.01, rot=(0, 0, math.radians(a)))
    for x in (-1, 1):
        for y in (-1, 1):
            c = Vector((x * 0.3, y * 0.3, 0.16))
            cylinder(f"DroneMotor{x}{y}", 0.03, 0.06, c, P["dark"], d, axis="Z", segments=10)
            ring(f"DroneGuard{x}{y}", 0.15, 0.035, 0.012, c + Vector((0, 0, 0.01)), P["gunmetal"], d, segments=24)
            for b in (0, 90):
                box(f"DroneBlade{x}{y}{b}", (0.26, 0.025, 0.005), c + Vector((0, 0, 0.035)), P["dark"], d, 0,
                    rot=(0, 0, math.radians(b + 20 * x * y)))
    return d


def survey_rover():
    P = palette()
    return {1: rover_mk1, 2: rover_mk2, 3: rover_mk3}[TIER](P)


# ---------------------------------------------------------------- weathering


def weather_hull(obj, res=1024):
    """Bake scuffed paint into the hull: dirt toward the bottom, lighter rub marks, streaks. Smart-UV the hull, bake the
    procedural colour into an image, then swap in a plain image-texture material (what glTF can carry).
    The dirt fades out over the object-z band obj["dirt_band"] = (lo, hi) that each tier's builder sets (Mk II 0.8-1.8,
    tuned first); without it the band is the bottom metre of the hull's bounding box. obj["paint"] (rgb), if set,
    replaces the clean paint colour."""
    zs = [c[2] for c in obj.bound_box]
    band = tuple(obj["dirt_band"]) if "dirt_band" in obj else (min(zs) + 0.02, min(zs) + 1.02)
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
    grad.inputs["From Min"].default_value = band[0]
    grad.inputs["From Max"].default_value = band[1]
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
    clean.outputs[0].default_value = (*(tuple(obj["paint"]) if "paint" in obj else (0.8, 0.81, 0.82)), 1)
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
    for name in SHOTS:
        pos, lens = shots[name]
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        stem = f"{VEHICLE}-t{TIER}-{NAME.lower()}" if NAMED else VEHICLE
        scene.render.filepath = os.path.join(folder, f"{stem}-{name}.png")
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
