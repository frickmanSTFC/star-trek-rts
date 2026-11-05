"""Outpost (the colony core) growing through ten levels: a Blender MOCKUP, not a game model.

One architectural family in the Federation style of the rovers (white/grey panels, owner-colour accents, amber/blue
lights, striped pads, scuffed paint). Level 1 is the colony pod just landed; level 10 is a planetary capital. Each level
keeps everything below it and adds something. The level names and contents are a design proposal written up in
blender/outpost_levels.md (no data changes; data/buildings.json still has three core levels).

Scale: Godot world units. One hex = 25 units across the flats (PlanetView.HexWidth, pointy-top: corners toward ±Y here),
so the hex inradius is 12.5 and its corner radius 14.43. Levels 1-6 stay inside the hex; levels 7-10 spill onto the
neighbouring hexes' edges (outlines of the six neighbours are drawn too). A Mk I survey rover (build_vehicles.py's
rover_mk1, built without its paint bake) parks on the repair pad for scale; colonists are ~1.9 units tall.

Frame: Blender +Z up; the core's front (entrance, ramp, game camera side) faces -Y, which glTF/Godot sees as +Z, the
buildings' front in BuildingModels.cs. Repair pad at Blender (8.6, -2) = Godot (8.6, 0, 2) (the game has 9.4: moved in
0.8 so the 7.4-wide pad stays inside the hex).

Run (bpy 5.x module, e.g. /root/bpyenv/bin/python):
  /root/bpyenv/bin/python blender/build_outpost.py --level 3 --preview blender/previews/outpost --res 640 --samples 12
  ... --shots front34,game   front34 = one fixed camera for every level (framed for level 10); game = the game's
                             camera at zoom distance 50 (PlanetDriver's tilt curve, Planet.tscn's 45 degree fov);
                             close = a nearer look at the hex for checking details (outpost-LNN-close.png)
  ... --out /tmp/outpost-L03.glb   optional glTF of the outpost alone (not the rover/ground; NOT into game/models)
  ... --threads 3            Cycles threads (default: automatic)
  python3 blender/build_outpost.py --sheet blender/previews/outpost   contact sheet from outpost-L01..L10.png (PIL)

Previews: <dir>/outpost-L01.png (front34) and <dir>/outpost-L01-game.png.
Materials are plain node materials with a procedural scuff (noise grime + dirt toward the ground) instead of the rovers'
baked paint, so a --out glb carries only flat base colours.
"""
import math
import os
import random
import re
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))

# Proposal (see outpost_levels.md): name per level. Data levels today: Colony Hub 1, Colonial Administration 2,
# Planetary Capital 3; the proposal puts those names on levels 3, 6 and 10.
LEVEL_NAMES = ["Landing Site", "Outpost", "Colony Hub", "Settlement", "Township", "Colonial Administration",
               "Provincial Centre", "Colony City", "Metropolitan Hub", "Planetary Capital"]


def contact_sheet(folder):
    """2 rows x 5 of outpost-L01..L10.png, each labelled 'L1 Landing Site' etc."""
    from PIL import Image, ImageDraw, ImageFont
    tiles = [Image.open(os.path.join(folder, f"outpost-L{n:02d}.png")).convert("RGB") for n in range(1, 11)]
    w, h = 480, int(480 * tiles[0].height / tiles[0].width)
    bar = 34
    sheet = Image.new("RGB", (5 * w + 6 * 6, 2 * (h + bar) + 3 * 6), (14, 16, 20))
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 20)
    except OSError:
        font = ImageFont.load_default()
    d = ImageDraw.Draw(sheet)
    for i, t in enumerate(tiles):
        x = 6 + (i % 5) * (w + 6)
        y = 6 + (i // 5) * (h + bar + 6)
        sheet.paste(t.resize((w, h), Image.LANCZOS), (x, y + bar))
        d.rectangle((x, y, x + w - 1, y + bar - 1), fill=(28, 32, 40))
        d.text((x + 10, y + 6), f"L{i + 1}", fill=(255, 196, 92), font=font)
        d.text((x + 52, y + 6), LEVEL_NAMES[i], fill=(220, 226, 236), font=font)
    out = os.path.join(folder, "outpost-sheet.png")
    sheet.save(out)
    print("wrote", out)


if "--sheet" in argv:
    contact_sheet(arg("--sheet"))
    sys.exit(0)

import bpy  # noqa: E402  first bpy, then bmesh/mathutils
import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402

LEVEL = int(arg("--level", "1"))
PREVIEW = arg("--preview")
OUT = arg("--out")
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "32"))
THREADS = int(arg("--threads", "0"))
SHOTS = [s.strip() for s in arg("--shots", "front34,game").split(",") if s.strip()]
if not 1 <= LEVEL <= 10:
    sys.exit(f"--level must be 1..10, not {LEVEL}")
if set(SHOTS) - {"front34", "game", "close"}:
    sys.exit("--shots takes front34, game and close")

HEX = 25.0
HEX_R = HEX / math.sqrt(3)  # corner radius
PAD_AT = Vector((8.6, -2.0, 0.0))


def load_build_vehicles():
    """build_vehicles.py's helpers in a namespace, without running its main() (as render_finish.py does). Its own
    flag parsing sees only --tier 1 (Mk I Scout, the rover that lands with the colony)."""
    path = os.path.join(HERE, "build_vehicles.py")
    with open(path) as f:
        src = re.sub(r"^main\(\)\s*$", "", f.read(), flags=re.M)
    saved = sys.argv
    sys.argv = [path, "--tier", "1"]
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


BV = load_build_vehicles()
box, cylinder, link, empty, mesh_object, strut = BV["box"], BV["cylinder"], BV["link"], BV["empty"], BV["mesh_object"], BV["strut"]
prism = BV["prism"]

# ---------------------------------------------------------------- materials


def _sock(node, name, kind):
    return next(s for s in node.inputs if s.name == name and s.type == kind)


def paint(name, colour, rough=0.45, metal=0.15, scuff=0.35, dirt=0.55, emission=None, strength=0.0, coat=0.0):
    """Principled material with a procedural scuff: world-space noise grime plus dirt fading up from the ground."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    p.inputs["Metallic"].default_value = metal
    p.inputs["Roughness"].default_value = rough
    if coat:
        p.inputs["Coat Weight"].default_value = coat
    if emission is not None:
        p.inputs["Emission Color"].default_value = (*emission, 1.0)
        p.inputs["Emission Strength"].default_value = strength
    if scuff <= 0 and dirt <= 0:
        p.inputs["Base Color"].default_value = (*colour, 1.0)
        return m
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.45
    noise.inputs["Detail"].default_value = 8.0
    noise.inputs["Roughness"].default_value = 0.62
    nt.links.new(geo.outputs["Position"], noise.inputs["Vector"])
    grime = nt.nodes.new("ShaderNodeMapRange")
    grime.inputs["From Min"].default_value = 0.5
    grime.inputs["From Max"].default_value = 0.78
    grime.inputs["To Max"].default_value = scuff
    nt.links.new(noise.outputs["Fac"], grime.inputs["Value"])
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Position"], sep.inputs["Vector"])
    height = nt.nodes.new("ShaderNodeMapRange")
    height.inputs["From Min"].default_value = 0.0
    height.inputs["From Max"].default_value = 1.4
    height.inputs["To Min"].default_value = dirt
    height.inputs["To Max"].default_value = 0.0
    nt.links.new(sep.outputs["Z"], height.inputs["Value"])
    mx = nt.nodes.new("ShaderNodeMath")
    mx.operation = "MAXIMUM"
    nt.links.new(grime.outputs["Result"], mx.inputs[0])
    nt.links.new(height.outputs["Result"], mx.inputs[1])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    _sock(mix, "A", "RGBA").default_value = (*colour, 1.0)
    _sock(mix, "B", "RGBA").default_value = (0.36, 0.27, 0.18, 1.0)  # the preview ground's sand
    nt.links.new(mx.outputs["Value"], _sock(mix, "Factor", "VALUE"))
    nt.links.new(next(s for s in mix.outputs if s.type == "RGBA"), p.inputs["Base Color"])
    # rubs: a finer noise roughens the paint in patches
    fine = nt.nodes.new("ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value = 3.0
    nt.links.new(geo.outputs["Position"], fine.inputs["Vector"])
    rr = nt.nodes.new("ShaderNodeMapRange")
    rr.inputs["To Min"].default_value = rough * 0.8
    rr.inputs["To Max"].default_value = min(1.0, rough * 1.5)
    nt.links.new(fine.outputs["Fac"], rr.inputs["Value"])
    nt.links.new(rr.outputs["Result"], p.inputs["Roughness"])
    return m


def glow(name, colour, strength):
    return paint(name, colour, 0.3, 0.0, 0, 0, emission=colour, strength=strength)


M = {}


def materials():
    M.update({
        "hull": paint("O_Hull", (0.8, 0.81, 0.82), 0.42, 0.15, scuff=0.5, dirt=0.7, coat=0.2),
        "panel": paint("O_Panel", (0.6, 0.62, 0.64), 0.5, 0.2, scuff=0.45, dirt=0.7),
        "grey": paint("O_Grey", (0.42, 0.44, 0.47), 0.5, 0.3),
        "dark": paint("O_Dark", (0.13, 0.135, 0.15), 0.55, 0.4, scuff=0.15, dirt=0.3),
        "steel": paint("O_Steel", (0.55, 0.57, 0.6), 0.32, 0.85, scuff=0.1, dirt=0.2),
        "owner": paint("Owner", (0.72, 0.08, 0.06), 0.4, 0.1, scuff=0.25),
        "concrete": paint("O_Concrete", (0.46, 0.44, 0.41), 0.85, 0.0, scuff=0.3, dirt=0.4),
        "asphalt": paint("O_PadDark", (0.07, 0.075, 0.08), 0.8, 0.0, scuff=0.2, dirt=0.25),
        "stripe": paint("O_PadStripe", (0.85, 0.62, 0.08), 0.55, 0.0, scuff=0.35, dirt=0.3),
        "white_mark": paint("O_PadWhite", (0.82, 0.82, 0.8), 0.6, 0.0, scuff=0.3, dirt=0.2),
        "glass": paint("O_Glass", (0.03, 0.06, 0.09), 0.05, 0.3, 0, 0, emission=(0.1, 0.25, 0.4), strength=0.25, coat=1.0),
        "window": glow("O_Window", (1.0, 0.78, 0.45), 3.5),
        "amber": glow("O_Amber", (1.0, 0.5, 0.1), 6.0),
        "blue": glow("O_Blue", (0.1, 0.4, 1.0), 5.0),
        "cyan": glow("O_Cyan", (0.5, 0.85, 1.0), 4.0),
        "red": glow("O_Red", (1.0, 0.08, 0.05), 9.0),
        "lamp": glow("O_Lamp", (1.0, 0.95, 0.85), 8.0),
        "solar": paint("O_Solar", (0.04, 0.09, 0.2), 0.18, 0.6, scuff=0.1, dirt=0.2, coat=0.6),
        "skin": paint("O_Skin", (0.85, 0.66, 0.5), 0.6, 0.0, 0, 0),
        "uni_red": paint("O_UniRed", (0.7, 0.12, 0.1), 0.6, 0.0, 0, 0),
        "uni_gold": paint("O_UniGold", (0.8, 0.55, 0.12), 0.6, 0.0, 0, 0),
        "uni_blue": paint("O_UniBlue", (0.12, 0.3, 0.65), 0.6, 0.0, 0, 0),
        "track": paint("O_Track", (0.25, 0.19, 0.13), 0.95, 0.0, 0, 0),
        "hexline": paint("O_HexLine", (0.75, 0.85, 0.9), 0.6, 0.0, 0, 0, emission=(0.75, 0.9, 1.0), strength=0.35),
        "hexline_dim": paint("O_HexLineDim", (0.5, 0.44, 0.36), 0.8, 0.0, 0, 0),
    })


# ---------------------------------------------------------------- geometry helpers


def lathe(name, profile, mat, parent=None, loc=(0, 0, 0), segs=48, smooth=True):
    """Revolve a profile [(r, z), ...] around Z. Points with r == 0 become poles."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r < 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z)) for k in range(segs)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        for k in range(segs):
            j = (k + 1) % segs
            if len(a) == 1:
                bm.faces.new((a[0], b[k], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[k], a[j], b[0]))
            else:
                bm.faces.new((a[k], a[j], b[j], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm, mat, parent, loc, smooth=smooth)


def dome(name, radius, height, mat, parent=None, loc=(0, 0, 0), segs=40, base=0.0):
    prof = [(radius, -base)] if base else []
    for i in range(13):
        a = math.pi / 2 * i / 12
        prof.append((radius * math.cos(a), height * math.sin(a)))
    prof[-1] = (0, height)
    return lathe(name, prof, mat, parent, loc, segs)


def torus(name, major, minor, loc, mat, parent=None, segs=48):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=segs, minor_segments=8, location=(0, 0, 0))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    for poly in o.data.polygons:
        poly.use_smooth = True
    o.parent = parent
    o.location = loc
    return o


def sphere(name, radius, loc, mat, parent=None, segs=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(6, segs // 2), radius=radius)
    return mesh_object(name, bm, mat, parent, loc, smooth=True)


def zcyl(name, radius, z0, z1, mat, parent=None, xy=(0, 0), segs=32, radius2=None, bev=0.0):
    return cylinder(name, radius, z1 - z0, (xy[0], xy[1], (z0 + z1) / 2), mat, parent, axis="Z", segments=segs, bev=bev, radius2=radius2)


def window_ring(name, r, z0, z1, n, mat, parent=None, frac=0.6, phase=0.0, xy=(0, 0), skip=()):
    """n flat window panes round a cylinder of radius r (a hair outside it)."""
    bm = bmesh.new()
    for k in range(n):
        if k in skip:
            continue
        a0 = 2 * math.pi * (k + phase) / n
        a1 = a0 + 2 * math.pi * frac / n
        vs = [bm.verts.new((xy[0] + r * math.cos(a), xy[1] + r * math.sin(a), z)) for a, z in ((a0, z0), (a1, z0), (a1, z1), (a0, z1))]
        bm.faces.new(vs)
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def pane_row(name, start, along, up, length, z0, z1, n, mat, parent=None, frac=0.7):
    """n window panes along a flat wall: `start` is the wall-left point at z0 (local coords), `along` its unit
    direction, panes from z0 to z1. The wall's outward side must face the viewer; panes sit on it."""
    bm = bmesh.new()
    s, a = Vector(start), Vector(along)
    step = length / n
    for k in range(n):
        p0 = s + a * (k * step + step * (1 - frac) / 2)
        p1 = p0 + a * step * frac
        vs = [bm.verts.new(v) for v in ((p0.x, p0.y, z0), (p1.x, p1.y, z0), (p1.x, p1.y, z1), (p0.x, p0.y, z1))]
        bm.faces.new(vs)
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def flat_path(name, pts, width, mat, z=0.015, parent=None):
    """A flat strip on the ground along a polyline [(x, y)] (tracks, paths)."""
    bm = bmesh.new()
    left, right = [], []
    for i, (x, y) in enumerate(pts):
        a = Vector(pts[max(0, i - 1)]) if i else Vector(pts[0])
        b = Vector(pts[min(len(pts) - 1, i + 1)])
        d = (b - a).normalized() if (b - a).length > 1e-6 else Vector((1, 0))
        n = Vector((-d.y, d.x)) * width / 2
        left.append(bm.verts.new((x + n.x, y + n.y, z)))
        right.append(bm.verts.new((x - n.x, y - n.y, z)))
    for i in range(len(pts) - 1):
        bm.faces.new((left[i], right[i], right[i + 1], left[i + 1]))
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def edge_frame(name, theta, dist, parent):
    """An empty on the hex-edge normal at angle theta (degrees from +X) and distance dist, turned so local +Y points
    outward and local +X runs along that edge."""
    t = math.radians(theta)
    e = empty(name, (dist * math.cos(t), dist * math.sin(t), 0), parent)
    e.rotation_euler = (0, 0, t - math.pi / 2)
    return e


def hex_corners(cx=0.0, cy=0.0):
    # pointy-top: corners toward ±Y (Godot ±Z)
    return [(cx + HEX_R * math.sin(math.radians(60 * k)), cy + HEX_R * math.cos(math.radians(60 * k))) for k in range(6)]


# ---------------------------------------------------------------- parts


def colonist(name, loc, uniform, parent, yaw=0.0):
    c = empty(name, loc, parent)
    c.rotation_euler.z = yaw
    zcyl(f"{name}Legs", 0.2, 0.0, 0.9, M["dark"], c, segs=10)
    zcyl(f"{name}Body", 0.27, 0.85, 1.55, uniform, c, segs=10, radius2=0.24)
    sphere(f"{name}Head", 0.17, (0, 0, 1.75), M["skin"], c, 12)
    return c


def pod(root, L):
    """The colony pod (build_colony_pod.py's design at ~0.45 scale): saucer on four legs, command capsule, glass dome.
    It stays the recognisable heart of every level."""
    P = empty("Pod", (0, 0, 0), root)
    s = 0.45
    zc = 3.6  # saucer centre height
    SP = [(0, 1.6), (5.6, 1.6), (6.0, 1.5), (9.2, 0.8), (9.95, 0.5), (10.15, 0.3), (10.15, -0.3), (9.95, -0.55),
          (9.0, -1.05), (7.8, -1.6), (0, -1.6)]
    lathe("Saucer", [(r * s, z * s) for r, z in SP], M["hull"], P, (0, 0, zc), 64)
    zcyl("SaucerUnderside", 1.35, zc - 0.72 - 0.9, zc - 0.7, M["panel"], P, radius2=3.5, segs=40)
    torus("UndersideGlow", 2.2, 0.1, (0, 0, zc - 0.75), M["cyan"], P)
    window_ring("RimWindows", 4.58, zc - 0.08, zc + 0.1, 44, M["window"], P, frac=0.5)
    torus("PanelRing", 2.52, 0.04, (0, 0, zc + 0.73), M["grey"], P)
    # owner-colour hull stripes on the upper slope, facing the front
    for k, a in enumerate((-110, -70, 70, 110)):
        t = math.radians(a - 90)
        box(f"HullStripe{k}", (1.4, 0.22, 0.04), (3.4 * math.cos(t), 3.4 * math.sin(t), zc + 0.52), M["owner"], P, 0.0,
            rot=(0, -math.radians(13), t)).visible_shadow = False
    for nm, a, m in (("NavPort", 180, "red"), ("NavStarboard", 0, "cyan"), ("NavAft", 90, "amber"), ("NavFore", 270, "amber")):
        t = math.radians(a)
        sphere(nm, 0.14, (4.62 * math.cos(t), 4.62 * math.sin(t), zc + 0.2), M[m], P, 10)
    # command capsule and glass dome
    cz0, cz1 = zc + 0.72, zc + 0.72 + 2.1
    zcyl("Capsule", 2.25, cz0, cz1, M["hull"], P, segs=48, bev=0.06)
    torus("CapsuleBand", 2.26, 0.09, (0, 0, cz0 + 1.45), M["owner"], P)
    window_ring("CapsuleWindows", 2.27, cz0 + 0.55, cz0 + 1.05, 24, M["window"], P, frac=0.55)
    torus("CapsuleFlare", 2.28, 0.14, (0, 0, cz0 + 0.08), M["dark"], P)
    dome("CommandDome", 1.95, 1.4, M["glass"], P, (0, 0, cz1))
    torus("DomeCollar", 1.98, 0.1, (0, 0, cz1 + 0.02), M["dark"], P)
    for k in range(4):
        rib = torus(f"DomeRib{k}", 1.96, 0.035, (0, 0, cz1), M["dark"], P)
        rib.scale = (1, 1, 1.4 / 1.95)
        rib.rotation_euler = (math.radians(90), 0, math.radians(45 * k))
    # landing legs at the diagonals
    for k, deg in enumerate((45, 135, 225, 315)):
        a = math.radians(deg)
        c, sn = math.cos(a), math.sin(a)
        top = (1.7 * c, 1.7 * sn, zc - 0.8)
        foot = (4.3 * c, 4.3 * sn, 0.25)
        strut(f"LegStrut{k}", top, foot, 0.17, M["grey"], P)
        mid = [top[i] + (foot[i] - top[i]) * 0.45 for i in range(3)]
        strut(f"LegSleeve{k}", top, mid, 0.28, M["hull"], P)
        zcyl(f"LegFoot{k}", 0.5, 0.0, 0.2, M["dark"], P, xy=(foot[0], foot[1]), segs=16)
        sphere(f"LegLight{k}", 0.09, (mid[0], mid[1], mid[2] - 0.32), M["cyan"], P, 8)
    if L == 1:
        # boarding ramp from the front rim down to the ground
        a, b = Vector((0, -4.1, zc - 0.45)), Vector((0, -7.6, 0.06))
        d = b - a
        mid = (a + b) / 2
        rx = math.atan2(d.z, -d.y)
        box("Ramp", (1.5, d.length, 0.12), mid, M["panel"], P, 0.02, rot=(rx, 0, 0))
        box("RampLights", (0.08, d.length - 0.3, 0.02), mid + Vector((0, 0, 0.08)), M["cyan"], P, 0.0, rot=(rx, 0, 0))
        box("Airlock", (1.3, 0.1, 0.55), (0, -4.55, zc), M["window"], P, 0.0)
    return P, cz1


def drum(root, L):
    """Level 2+: a walled ground floor under the saucer (the legs become buttresses), entrance at the front."""
    D = empty("GroundFloor", (0, 0, 0), root)
    zcyl("Drum", 3.2, 0.0, 2.9, M["hull"], D, segs=48, bev=0.05)
    zcyl("DrumPlinth", 3.45, 0.0, 0.35, M["grey"], D, segs=48)
    window_ring("DrumWindows", 3.22, 1.55, 2.15, 28, M["window"], D, frac=0.5, skip=(20, 21, 22))
    torus("DrumBand", 3.22, 0.07, (0, 0, 1.1), M["owner"], D)
    # entrance: a porch box with a lit door and a canopy
    box("Porch", (2.4, 1.6, 2.4), (0, -3.3, 1.2), M["hull"], D, 0.05)
    box("Door", (1.3, 0.06, 1.8), (0, -4.12, 1.0), M["window"], D, 0.0)
    box("Canopy", (2.9, 2.1, 0.16), (0, -3.6, 2.5), M["panel"], D, 0.03)
    box("CanopyStripe", (2.92, 0.08, 0.1), (0, -4.62, 2.5), M["owner"], D, 0.0)
    for k in range(12):  # blue running lights round the base
        a = 2 * math.pi * (k + 0.5) / 12
        sphere(f"BaseLight{k}", 0.08, (3.47 * math.cos(a), 3.47 * math.sin(a), 0.4), M["blue"], D, 8)
    return D


# top of the central mast/tower (beacon height) per level
TOWER_TOP = [10.6, 11.8, 14.0, 16.0, 19.0, 22.0, 26.0, 30.0, 34.0, 40.0]


def tower(root, L, base_z):
    """The pod's mast grows into the control tower: mast (1-2), saucer-capped pylon (3-6), with decks, lift tube,
    stacked rings and finally a needle spire (7-10)."""
    T = empty("Tower", (0, 0, 0), root)
    top = TOWER_TOP[L - 1]
    if L <= 2:
        zcyl("Mast", 0.13, base_z, top - 0.25, M["grey"], T, segs=12)
        torus("MastSensorRing", 0.7, 0.06, (0, 0, base_z + 1.2), M["hull"], T)
        sphere("Beacon", 0.26, (0, 0, top), M["red"], T, 14)
        if L == 2:
            torus("MastSensorRing2", 0.45, 0.05, (0, 0, base_z + 2.4), M["hull"], T)
            box("DishArm", (0.9, 0.08, 0.08), (0.45, 0, base_z + 2.0), M["grey"], T, 0.0)
            d = zcyl("MastDish", 0.05, 0, 0.25, M["hull"], T, segs=20, radius2=0.55)
            d.location = (0.95, 0, base_z + 2.1)
            d.rotation_euler = (0, math.radians(60), 0)
        return T
    # saucer cap: radius and thickness per level
    cap_r = {3: 1.4, 4: 1.7, 5: 2.5, 6: 2.7, 7: 2.9, 8: 3.0, 9: 3.1, 10: 3.3}[L]
    cap_h = 0.7 + 0.08 * L
    spire = {3: 1.6, 4: 2.0, 5: 2.6, 6: 3.0, 7: 4.5, 8: 6.0, 9: 8.0, 10: 11.0}[L]
    cap_z = top - spire - cap_h  # cap underside
    r0, r1 = 0.75 + 0.07 * L, 0.42 + 0.03 * L
    zcyl("Pylon", r0, base_z - 0.6, cap_z + 0.1, M["hull"], T, segs=32, radius2=r1)
    zcyl("PylonCollar", r0 + 0.25, base_z - 0.1, base_z + 0.35, M["dark"], T, segs=32, radius2=r0 + 0.1)
    span = cap_z - base_z
    for k in range(1, int(span / 2.6) + 1):  # panel seams and a few rows of window slits up the pylon
        f = k * 2.6 / span
        if f > 0.95:
            break
        rr = r0 + (r1 - r0) * f
        torus(f"PylonSeam{k}", rr + 0.01, 0.035, (0, 0, base_z + span * f), M["grey"], T, 32)
        if k % 2:
            window_ring(f"PylonSlits{k}", rr + 0.02, base_z + span * f + 0.5, base_z + span * f + 1.1, 12, M["window"], T,
                        frac=0.18, phase=0.25 * k)
    if L >= 8:  # owner-colour fins up the pylon
        for k in range(4):
            a = math.radians(45 + 90 * k)
            h = cap_z - base_z
            zmid = base_z + h * 0.45
            rr = (r0 + r1) / 2 * 1.0
            fin = box(f"PylonFin{k}", (0.14, 0.12, h * 0.8), (rr * math.cos(a), rr * math.sin(a), zmid), M["owner"], T, 0.0)
            fin.rotation_euler = (0, 0, a)
            tilt = math.atan2(r0 - r1, h)
            fin.rotation_euler = (math.cos(a + math.pi / 2) * tilt, math.sin(a + math.pi / 2) * tilt, a)
    # cap: lower cone, windowed band, upper dome
    lathe("TowerCap", [(0, cap_z), (r1 + 0.1, cap_z), (cap_r, cap_z + cap_h * 0.45), (cap_r, cap_z + cap_h * 0.62),
                       (cap_r * 0.82, cap_z + cap_h), (0, cap_z + cap_h)], M["hull"], T, segs=48)
    window_ring("CapWindows", cap_r + 0.01, cap_z + cap_h * 0.46, cap_z + cap_h * 0.61, 32, M["window"], T, frac=0.6)
    torus("CapTrim", cap_r * 0.83, 0.05, (0, 0, cap_z + cap_h - 0.02), M["owner"], T)
    for k in range(8):
        a = 2 * math.pi * k / 8
        sphere(f"CapLight{k}", 0.09, ((cap_r + 0.05) * math.cos(a), (cap_r + 0.05) * math.sin(a), cap_z + cap_h * 0.3),
               M["amber"], T, 8)
    # decks on the pylon (pagoda rings) from level 6
    decks = {6: 1, 7: 1, 8: 2, 9: 2, 10: 3}.get(L, 0)
    for k in range(decks):
        f = 0.42 + 0.22 * k
        z = base_z + (cap_z - base_z) * f
        rr = r0 + (r1 - r0) * f
        dr = rr + 1.1 - 0.15 * k
        lathe(f"Deck{k}", [(0, z - 0.25), (dr * 0.7, z - 0.25), (dr, z), (dr, z + 0.22), (dr * 0.6, z + 0.45), (0, z + 0.45)],
              M["hull"], T, segs=40)
        window_ring(f"DeckWindows{k}", dr + 0.01, z + 0.01, z + 0.2, 24, M["window"], T, frac=0.55)
    if L >= 7:  # glass lift tube beside the pylon
        lx = r0 + 0.35
        zcyl("LiftTube", 0.3, base_z - 0.3, cap_z + 0.2, M["glass"], T, xy=(-lx * 0.7, -lx * 0.7), segs=16)
        zcyl("LiftCar", 0.26, base_z + (cap_z - base_z) * 0.3, base_z + (cap_z - base_z) * 0.3 + 0.6, M["window"], T,
             xy=(-lx * 0.7, -lx * 0.7), segs=16)
    # spire above the cap
    sz = cap_z + cap_h
    if L <= 6:
        zcyl("Antenna", 0.12, sz, top - 0.25, M["grey"], T, segs=12)
        torus("AntennaRing", 0.45 + 0.05 * L, 0.05, (0, 0, sz + spire * 0.4), M["hull"], T)
    else:
        # needle with stacked, shrinking rings
        zcyl("Spire", 0.32, sz, top - 0.3, M["hull"], T, segs=24, radius2=0.06)
        rings = {7: 2, 8: 3, 9: 4, 10: 5}[L]
        for k in range(rings):
            z = sz + spire * (0.15 + 0.16 * k)
            rr = 1.4 - 0.2 * k
            lathe(f"SpireRing{k}", [(0, z - 0.12), (rr, z), (rr * 0.5, z + 0.22), (0, z + 0.22)], M["hull"], T, segs=32)
            sphere(f"SpireLight{k}", 0.07, (rr, 0, z + 0.02), M["amber"], T, 8)
        if L >= 10:
            d = zcyl("SpireDish", 0.06, 0, 0.35, M["hull"], T, segs=24, radius2=1.0)
            d.location = (0.9, 0, sz + spire * 0.72)
            d.rotation_euler = (0, math.radians(70), 0)
            box("SpireDishArm", (0.8, 0.08, 0.08), (0.45, 0, sz + spire * 0.72), M["grey"], T, 0.0)
    sphere("Beacon", 0.28 + 0.02 * L, (0, 0, top), M["red"], T, 14)
    return T


def repair_pad(root):
    """BuildingModels.RepairPad in Blender coordinates (Godot (x, y, z) -> Blender (x, -z, y))."""
    R = empty("RepairPad", PAD_AT, root)

    def G(x, y, z):
        return (x, -z, y)

    box("PadSlab", (7.4, 8.6, 0.22), G(0, 0.11, 0), M["concrete"], R, 0.03)
    for i in range(9):
        m = M["stripe"] if i % 2 == 0 else M["asphalt"]
        z = -4 + i
        box(f"PadEdgeL{i}", (0.4, 1.0, 0.06), G(-3.5, 0.25, z), m, R, 0.0)
        box(f"PadEdgeR{i}", (0.4, 1.0, 0.06), G(3.5, 0.25, z), m, R, 0.0)
    for i in range(7):
        m = M["asphalt"] if i % 2 == 0 else M["stripe"]
        x = -3 + i
        box(f"PadEdgeB{i}", (1.0, 0.4, 0.06), G(x, 0.25, -4.1), m, R, 0.0)
        box(f"PadEdgeF{i}", (1.0, 0.4, 0.06), G(x, 0.25, 4.1), m, R, 0.0)
    box("PadWrench", (0.5, 3.2, 0.04), G(0, 0.24, 0), M["owner"], R, 0.0, rot=(0, 0, math.radians(45)))
    zcyl("PadWrenchHead", 0.9, 0.22, 0.26, M["owner"], R, xy=G(0.95, 0, -0.95)[:2], segs=16)
    zcyl("LampMast", 0.15, 0.2, 6.5, M["hull"], R, xy=G(3.2, 0, -3.8)[:2], segs=10, radius2=0.11)
    box("LampHead", (1.2, 0.6, 0.3), G(2.8, 6.5, -3.6), M["dark"], R, 0.03)
    box("LampLens", (1.0, 0.45, 0.04), G(2.8, 6.33, -3.6), M["lamp"], R, 0.0)
    box("ToolRack", (0.5, 2.2, 1.6), G(-3.4, 1.0, -2.5), paint("O_ToolRed", (0.7, 0.22, 0.2), 0.5, 0.4), R, 0.03)
    for z in (0.8, 1.5):
        zcyl(f"GasBottle{z}", 0.25, 0.2, 1.6, paint(f"O_Bottle{z}", (0.3, 0.55, 0.35), 0.4, 0.5), R, xy=G(-3.4, 0, z)[:2], segs=12)
    box("CraneMast", (0.3, 0.3, 5.0), G(-3.2, 2.6, 3.6), M["hull"], R, 0.02)
    box("CraneJib", (3.6, 0.3, 0.3), G(-1.5, 5.0, 3.6), M["hull"], R, 0.02)
    box("CraneJibStripe", (3.0, 0.32, 0.08), G(-1.5, 4.88, 3.6), M["stripe"], R, 0.0)
    zcyl("CraneCable", 0.03, 3.2, 5.0, M["dark"], R, xy=G(0.1, 0, 3.6)[:2], segs=6)
    box("CraneHook", (0.4, 0.4, 0.3), G(0.1, 3.1, 3.6), M["stripe"], R, 0.02)
    return R


def hab(name, loc, parent, r=1.45):
    """Inflatable hab module: a low dome on a ring base, owner band, lit door and two portholes."""
    H = empty(name, loc, parent)
    zcyl(f"{name}Base", r + 0.1, 0.0, 0.45, M["grey"], H, segs=32)
    dome(f"{name}Dome", r, 1.25, M["hull"], H, (0, 0, 0.42), segs=32)
    torus(f"{name}Band", r + 0.01, 0.06, (0, 0, 0.75), M["owner"], H, 32)
    window_ring(f"{name}Ports", r * 0.93, 1.0, 1.25, 8, M["window"], H, frac=0.35)
    return H


def supply_capsule(name, loc, parent):
    C = empty(name, loc, parent)
    zcyl(f"{name}Body", 0.9, 0.0, 1.7, M["hull"], C, segs=24, bev=0.04)
    dome(f"{name}Top", 0.9, 0.42, M["hull"], C, (0, 0, 1.7), segs=24)
    torus(f"{name}Band", 0.91, 0.05, (0, 0, 0.45), M["cyan"], C, 24)
    torus(f"{name}Band2", 0.91, 0.04, (0, 0, 1.25), M["owner"], C, 24)
    return C


def cargo_hex(name, loc, parent, h=1.5, rot=0.0):
    """Hexagonal cargo module (desert colony reference): a hex prism with an owner band and a hatch."""
    C = empty(name, loc, parent)
    C.rotation_euler.z = rot
    zcyl(f"{name}Body", 1.0, 0.0, h, M["panel"], C, segs=6, bev=0.05)
    zcyl(f"{name}Lid", 0.85, h, h + 0.12, M["hull"], C, segs=6)
    zcyl(f"{name}Band", 1.015, h * 0.62, h * 0.72, M["owner"], C, segs=6)
    box(f"{name}Hatch", (0.6, 0.04, 0.7), (0, -0.87, 0.5), M["dark"], C, 0.0)
    return C


def terraced(name, frame, length, tiers, L_lit=True):
    """Long, low stepped block (desert colony reference): tiers [(depth, height)] from the full-depth ground floor up,
    each set back toward the outer edge (local +Y), window strips on every step's inner face, owner trim on the roof."""
    total = tiers[0][0]
    y_out = 2.0
    z = 0.0
    for k, (depth, h) in enumerate(tiers):
        ln = length - 0.9 * k
        y_in = y_out - depth
        box(f"{name}Tier{k}", (ln, depth, h), (0, (y_in + y_out) / 2, z + h / 2), M["hull"] if k % 2 == 0 else M["panel"], frame, 0.05)
        pane_row(f"{name}Windows{k}", (-ln / 2 + 0.3, y_in - 0.012, 0), (1, 0, 0), None, ln - 0.6, z + h * 0.35, z + h * 0.72,
                 max(3, int(ln / 0.9)), M["window"], frame, frac=0.72)
        box(f"{name}Ledge{k}", (ln + 0.05, 0.25, 0.1), (0, y_in + 0.05, z + h), M["grey"], frame, 0.0)
        z += h
    top_depth = tiers[-1][0]
    ln = length - 0.9 * (len(tiers) - 1)
    box(f"{name}RoofTrim", (ln + 0.06, 0.14, 0.12), (0, y_out - top_depth, z + 0.02), M["owner"], frame, 0.0)
    # rooftop plant and a door at ground level on the inner face
    box(f"{name}RoofUnit", (1.2, 0.8, 0.5), (ln * 0.25, y_out - top_depth / 2, z + 0.25), M["grey"], frame, 0.04)
    zcyl(f"{name}Vent", 0.25, z, z + 0.7, M["dark"], frame, xy=(-ln * 0.28, y_out - top_depth / 2), segs=12)
    box(f"{name}Door", (1.0, 0.06, 1.7), (0, y_out - total - 0.02, 0.85), M["window"], frame, 0.0)
    return z


def landing_pad(name, loc, parent, r, style="stripes"):
    """Round landing pad: dark apron, yellow hazard ring and circle marks (desert colony) or a red centre with a ring of
    lights (valley colony)."""
    Pd = empty(name, loc, parent)
    zcyl(f"{name}Apron", r, 0.0, 0.16, M["asphalt"], Pd, segs=64)
    zcyl(f"{name}Kerb", r + 0.25, 0.0, 0.1, M["concrete"], Pd, segs=64)
    n = 24
    for k in range(n):  # hazard ring of alternating dashes
        if k % 2:
            continue
        a = 2 * math.pi * k / n
        b = box(f"{name}Dash{k}", (0.32, 2 * math.pi * (r - 0.4) / n * 0.95, 0.03), ((r - 0.4) * math.cos(a), (r - 0.4) * math.sin(a), 0.17),
                M["stripe"], Pd, 0.0, rot=(0, 0, a))
        b.visible_shadow = False
    if style == "stripes":
        torus(f"{name}Circle", r * 0.45, 0.06, (0, 0, 0.16), M["white_mark"], Pd, 48).scale = (1, 1, 0.3)
        box(f"{name}MarkA", (r * 0.7, 0.18, 0.03), (0, 0, 0.17), M["white_mark"], Pd, 0.0)
        box(f"{name}MarkB", (0.18, r * 0.7, 0.03), (0, 0, 0.17), M["white_mark"], Pd, 0.0)
    else:
        zcyl(f"{name}Centre", r * 0.28, 0.16, 0.19, paint(f"O_PadRed{name}", (0.6, 0.06, 0.04), 0.6, 0.0, 0.2, 0.1), Pd, segs=40)
        torus(f"{name}Circle", r * 0.6, 0.06, (0, 0, 0.16), M["white_mark"], Pd, 48).scale = (1, 1, 0.3)
    for k in range(16):  # ring of edge lights
        a = 2 * math.pi * (k + 0.5) / 16
        sphere(f"{name}Light{k}", 0.1, ((r + 0.12) * math.cos(a), (r + 0.12) * math.sin(a), 0.16), M["blue" if style == "stripes" else "amber"], Pd, 8)
    return Pd


def shuttle(name, loc, yaw, parent, number="14"):
    """Small angular lander in the Pigeon-class spirit: wedge hull, tinted canopy, owner accent panels, hull number."""
    S = empty(name, loc, parent)
    S.rotation_euler.z = yaw
    prof = [(-2.4, 0.5), (2.0, 0.5), (2.6, 0.85), (2.3, 1.25), (1.3, 1.75), (-2.2, 1.75), (-2.5, 1.4)]
    prism(f"{name}Hull", prof, 2.3, M["hull"], S, taper_above=1.3, taper=0.88, bev=0.06)
    prism(f"{name}Canopy", [(1.35, 1.6), (2.25, 1.22), (2.4, 1.0), (2.1, 1.0), (1.2, 1.55)], 1.7, M["glass"], S, bev=0.02)
    for sx in (-1, 1):
        box(f"{name}Pod{sx}", (0.55, 3.6, 0.55), (sx * 1.35, -0.6, 1.05), M["panel"], S, 0.08)
        box(f"{name}Accent{sx}", (0.04, 2.6, 0.22), (sx * 1.16, 0.0, 1.2), M["owner"], S, 0.0)
        box(f"{name}Glow{sx}", (0.4, 0.06, 0.35), (sx * 1.35, -2.42, 1.05), M["cyan"], S, 0.0)
        box(f"{name}Skid{sx}", (0.15, 3.6, 0.12), (sx * 0.85, -0.2, 0.06), M["dark"], S, 0.02)
        for y in (-1.4, 1.0):
            strut(f"{name}Strut{sx}{y}", (sx * 0.85, y, 0.08), (sx * 0.8, y, 0.55), 0.06, M["grey"], S)
        t = BV["text"](f"{name}Num{sx}", number, 0.55, (sx * 1.16, -1.3, 1.25), (math.radians(90), 0, math.radians(90 * sx)),
                       paint(f"O_Num{name}{sx}", (0.06, 0.08, 0.16), 0.5, 0.0, 0, 0), S)
    return S


def floodlight(name, loc, parent, h=5.0, yaw=0.0):
    F = empty(name, loc, parent)
    F.rotation_euler.z = yaw
    zcyl(f"{name}Pole", 0.12, 0.0, h, M["grey"], F, segs=8, radius2=0.08)
    box(f"{name}Head", (0.9, 0.4, 0.25), (0, -0.15, h), M["dark"], F, 0.02)
    box(f"{name}Lens", (0.75, 0.3, 0.03), (0, -0.15, h - 0.14), M["lamp"], F, 0.0)
    return F


def beacon_pole(name, loc, parent, colour="amber", h=2.6):
    zcyl(f"{name}Pole", 0.09, 0.0, h, M["grey"], parent, xy=loc, segs=8)
    zcyl(f"{name}Foot", 0.25, 0.0, 0.15, M["dark"], parent, xy=loc, segs=8)
    sphere(f"{name}Lamp", 0.17, (loc[0], loc[1], h + 0.1), M[colour], parent, 10)


def comms_mast(name, loc, parent, h=7.0, dish=1.1):
    C = empty(name, loc, parent)
    zcyl(f"{name}Foot", 0.6, 0.0, 0.35, M["concrete"], C, segs=12)
    for k in range(3):  # lattice: three legs
        a = 2 * math.pi * k / 3
        strut(f"{name}Leg{k}", (0.45 * math.cos(a), 0.45 * math.sin(a), 0.3), (0.08 * math.cos(a), 0.08 * math.sin(a), h), 0.05, M["grey"], C)
    for z in (h * 0.3, h * 0.6):
        torus(f"{name}Brace{z:.1f}", 0.45 - 0.37 * z / h, 0.03, (0, 0, z), M["grey"], C, 12)
    d = zcyl(f"{name}Dish", 0.05, 0, 0.35, M["hull"], C, segs=28, radius2=dish)
    d.location = (0.25, -0.1, h * 0.72)
    d.rotation_euler = (math.radians(55), 0, math.radians(200))
    sphere(f"{name}Light", 0.14, (0, 0, h + 0.1), M["red"], C, 10)
    return C


def radome(name, loc, parent, r=1.1):
    Rd = empty(name, loc, parent)
    zcyl(f"{name}Stalk", 0.35, 0.0, 1.3, M["panel"], Rd, segs=16, radius2=0.25)
    zcyl(f"{name}Base", 0.7, 0.0, 0.25, M["concrete"], Rd, segs=16)
    sphere(f"{name}Ball", r, (0, 0, 1.3 + r * 0.9), M["hull"], Rd, 28)
    torus(f"{name}Band", r * 0.98, 0.04, (0, 0, 1.3 + r * 0.7), M["owner"], Rd, 32)
    return Rd


def arched_shed(name, frame, loc, length, r):
    """Arched storage shed: a half-cylinder vault along local X with ribs and a lit end door."""
    S = empty(name, loc, frame)
    n = 16
    verts = []
    for i in range(n + 1):
        a = math.pi * i / n
        verts.append((r * math.cos(a), r * math.sin(a)))
    prof_bm = bmesh.new()
    left = [prof_bm.verts.new((-length / 2, y, z)) for y, z in verts]
    right = [prof_bm.verts.new((length / 2, y, z)) for y, z in verts]
    for i in range(n):
        prof_bm.faces.new((left[i], right[i], right[i + 1], left[i + 1]))
    prof_bm.faces.new(left)
    prof_bm.faces.new(list(reversed(right)))
    bmesh.ops.recalc_face_normals(prof_bm, faces=prof_bm.faces)
    mesh_object(f"{name}Vault", prof_bm, M["panel"], S, smooth=True)
    for k in range(5):
        x = -length / 2 + 0.15 + k * (length - 0.3) / 4
        rib = torus(f"{name}Rib{k}", r + 0.02, 0.06, (x, 0, 0), M["hull"], S, 24)
        rib.rotation_euler = (0, math.radians(90), 0)
    box(f"{name}Door", (0.05, r * 1.0, r * 0.75), (-length / 2 - 0.02, 0, r * 0.375), M["window"], S, 0.0)
    box(f"{name}Footing", (length + 0.3, 2 * r + 0.3, 0.15), (0, 0, 0.075), M["concrete"], S, 0.0)
    return S


def solar_hall(name, frame, loc, length, width):
    """Long flat hall with a blue solar roof (valley colony reference)."""
    S = empty(name, loc, frame)
    box(f"{name}Walls", (length, width, 1.7), (0, 0, 0.85), M["hull"], S, 0.04)
    box(f"{name}Roof", (length + 0.2, width + 0.3, 0.14), (0, 0, 1.78), M["solar"], S, 0.02, rot=(math.radians(4), 0, 0))
    for k in range(int(length / 1.6)):
        box(f"{name}RoofRib{k}", (0.06, width + 0.32, 0.05), (-length / 2 + 0.8 + k * 1.6, 0, 1.86), M["steel"], S, 0.0,
            rot=(math.radians(4), 0, 0))
    pane_row(f"{name}Windows", (-length / 2 + 0.3, -width / 2 - 0.012, 0), (1, 0, 0), None, length - 0.6, 0.7, 1.2,
             int(length / 1.1), M["window"], S, frac=0.6)
    box(f"{name}Stripe", (length + 0.02, width + 0.02, 0.12), (0, 0, 0.35), M["owner"], S, 0.0)
    return S


def ribbed_dome(name, loc, parent, r, h):
    """Big ribbed dome with a ring of windows and an antenna spire with a dish (spires reference)."""
    D = empty(name, loc, parent)
    zcyl(f"{name}Plinth", r + 0.8, 0.0, 0.6, M["concrete"], D, segs=64)
    zcyl(f"{name}Wall", r, 0.6, 1.9, M["panel"], D, segs=64)
    window_ring(f"{name}Windows", r + 0.01, 0.95, 1.6, 40, M["window"], D, frac=0.55)
    dome(f"{name}Shell", r, h, M["hull"], D, (0, 0, 1.9), segs=64)
    for k in range(16):
        rib = torus(f"{name}Rib{k}", r + 0.03, 0.09, (0, 0, 1.9), M["grey"], D, 64)
        rib.scale = (1, 1, h / r)
        rib.rotation_euler = (math.radians(90), 0, math.radians(180 / 16 * k))
    torus(f"{name}Band", r * 0.5, 0.12, (0, 0, 1.9 + h * math.sqrt(1 - 0.25) * 1.0), M["owner"], D, 48)
    zcyl(f"{name}Oculus", r * 0.2, 1.9 + h - 0.15, 1.9 + h + 0.35, M["glass"], D, segs=24)
    z0 = 1.9 + h + 0.3
    zcyl(f"{name}Spire", 0.22, z0, z0 + 7.5, M["hull"], D, segs=16, radius2=0.06)
    dd = zcyl(f"{name}Dish", 0.06, 0, 0.4, M["hull"], D, segs=28, radius2=1.3)
    dd.location = (0.9, 0.0, z0 + 3.0)
    dd.rotation_euler = (0, math.radians(65), math.radians(-30))
    sphere(f"{name}Light", 0.2, (0, 0, z0 + 7.6), M["red"], D, 12)
    return D


def needle_spire(name, loc, parent, h, rings=4):
    """Tall needle tower topped with stacked, pagoda-like rings (spires reference)."""
    N = empty(name, loc, parent)
    zcyl(f"{name}Base", 1.4, 0.0, 0.5, M["concrete"], N, segs=6)
    zcyl(f"{name}Shaft", 0.55, 0.5, h, M["hull"], N, segs=24, radius2=0.15)
    for k in range(rings):
        z = h * (0.55 + 0.1 * k)
        rr = 1.5 - 0.22 * k
        lathe(f"{name}Ring{k}", [(0, z - 0.15), (rr, z), (rr * 0.45, z + 0.3), (0, z + 0.3)], M["hull"], N, segs=32)
        window_ring(f"{name}RingWin{k}", rr * 0.72, z + 0.1, z + 0.2, 12, M["window"], N, frac=0.5)
    sphere(f"{name}Light", 0.16, (0, 0, h + 0.15), M["amber"], N, 10)
    return N


def saucer_platform(name, loc, parent, h, r):
    """Saucer platform on a cluster of columns with a glass lift tube (spires reference)."""
    S = empty(name, loc, parent)
    for k in range(5):
        a = 2 * math.pi * k / 5
        zcyl(f"{name}Col{k}", 0.25, 0.0, h, M["hull"], S, xy=(0.9 * math.cos(a), 0.9 * math.sin(a)), segs=12)
    zcyl(f"{name}Lift", 0.35, 0.0, h + 0.2, M["glass"], S, segs=16)
    zcyl(f"{name}LiftCar", 0.3, h * 0.4, h * 0.4 + 0.7, M["window"], S, segs=16)
    lathe(f"{name}Deck", [(0, h - 0.4), (r * 0.5, h - 0.4), (r, h), (r, h + 0.35), (r * 0.7, h + 0.7), (0, h + 0.7)], M["hull"], S, segs=48)
    window_ring(f"{name}Win", r + 0.01, h + 0.02, h + 0.32, 28, M["window"], S, frac=0.6)
    torus(f"{name}Trim", r * 0.72, 0.05, (0, 0, h + 0.7), M["owner"], S, 48)
    for k in range(6):
        a = 2 * math.pi * k / 6
        sphere(f"{name}Lamp{k}", 0.09, ((r + 0.05) * math.cos(a), (r + 0.05) * math.sin(a), h - 0.05), M["blue"], S, 8)
    return S


def dish_array(name, loc, parent, n=3):
    A = empty(name, loc, parent)
    zcyl(f"{name}Pad", 4.2, 0.0, 0.2, M["concrete"], A, segs=6)
    for k in range(n):
        a = 2 * math.pi * k / n + 0.3
        p = (2.4 * math.cos(a), 2.4 * math.sin(a))
        zcyl(f"{name}Ped{k}", 0.35, 0.2, 2.0, M["panel"], A, xy=p, segs=12)
        d = zcyl(f"{name}Dish{k}", 0.1, 0, 0.5, M["hull"], A, segs=32, radius2=1.5)
        d.location = (p[0], p[1], 2.5)
        d.rotation_euler = (math.radians(50), 0, math.radians(150 + 20 * k))
        sphere(f"{name}Feed{k}", 0.1, (p[0], p[1], 3.2), M["amber"], A, 8)
    zcyl(f"{name}Hut", 0.9, 0.2, 1.5, M["hull"], A, segs=6)
    return A


# ---------------------------------------------------------------- the outpost per level


def build(L):
    root = empty("Outpost")
    rng = random.Random(7)
    pod_obj, cap_top = pod(root, L)
    if L >= 2:
        drum(root, L)
    tower(root, L, cap_top + 1.0)
    repair_pad(root)

    # hex corner beacons (amber) from the start: they mark the claim
    for k, (x, y) in enumerate(hex_corners()):
        beacon_pole(f"CornerBeacon{k}", (x * 0.94, y * 0.94), root)

    # left: hab modules (1), joined to the ground floor by tubes (2)
    habs = [(-6.9, -1.9), (-6.9, 1.4)] + ([(-5.6, 4.1)] if L >= 2 else [])
    for k, p in enumerate(habs):
        hab(f"Hab{k}", (*p, 0), root, 1.2)
        if L >= 2:
            d = Vector(p).normalized()
            a = d * 3.25
            b = Vector(p) - d * 1.15
            strut(f"HabTube{k}", (a.x, a.y, 0.7), (b.x, b.y, 0.7), 0.42, M["hull"], root)
            strut(f"HabTubeLight{k}", (a.x, a.y, 1.13), (b.x, b.y, 1.13), 0.05, M["blue"], root)

    # back-right zone: supply capsules (1) unpacked into hex cargo modules (2), more and stacked (4), arched shed (4)
    BR = edge_frame("BackRight", 60, 8.0, root)
    if L == 1:
        for k, x in enumerate((1.2, 3.4)):
            supply_capsule(f"SupplyCapsule{k}", (x, 0.8, 0), BR)
        supply_capsule("SupplyCapsule2", (2.3, 2.6, 0), BR)
    else:
        slots = [(2.2, 0.4), (3.95, 0.4), (3.07, 1.9)]
        if L >= 4:
            slots += [(0.45, 0.4), (1.32, 1.9), (2.2, 3.4)]
        for k, (x, y) in enumerate(slots):
            cargo_hex(f"Cargo{k}", (x, y, 0), BR, rot=math.radians(30))
        if L >= 4:
            for k, (x, y) in enumerate([(2.2, 0.4), (3.07, 1.9)]):
                cargo_hex(f"CargoUp{k}", (x, y, 1.62), BR, h=1.3, rot=math.radians(30))
    if L >= 4:
        arched_shed("Shed", BR, (-2.6, 2.4, 0), 3.6, 1.25)

    # back corner: comms mast (2), radomes beside it (5)
    if L >= 2:
        comms_mast("CommsMast", (0.0, 11.2, 0), root, h=6.0 + 0.4 * L)
    if L >= 5:
        radome("RadomeA", (-2.3, 10.2, 0), root)
        radome("RadomeB", (2.2, 10.6, 0), root, 0.9)

    # left edge, behind the habs: terraced block (3), a tier more and longer (5), a fourth tier (8)
    LE = edge_frame("Left", 180, 10.4, root)
    if L >= 3:
        tiers = [(3.4, 1.9), (2.3, 1.6)]
        length = 6.4
        if L >= 5:
            tiers.append((1.3, 1.5))
            length = 8.4
        if L >= 8:
            tiers.append((0.6, 1.4))
            length = 9.0
        terraced("BlockA", LE, length, tiers)
    # back-left: administration wing (6), a third tier (9)
    BL = edge_frame("BackLeft", 120, 8.6, root)
    admin_tiers = [(2.6, 1.9), (1.5, 1.6)] + ([(0.7, 1.4)] if L >= 9 else [])
    if L >= 6:
        terraced("BlockB", BL, 7.6, admin_tiers)

    # front-left: shuttle landing pad with a parked shuttle (4)
    if L >= 4:
        landing_pad("ShuttlePad", (-5.2, -7.0, 0), root, 3.3, "stripes")
        shuttle("Shuttle", (-5.2, -7.0, 0.16), math.radians(-60), root, "14")

    # ground: worn tracks (3), paved walks with blue edge lights (5)
    if L >= 3:
        flat_path("TrackPad", [(9.2, -13.5), (8.9, -9.0), (8.6, -6.4)], 2.4, M["track"], 0.012, root)
        flat_path("TrackFront", [(0, -14.5), (0, -9.5), (0.0, -4.6)], 2.0, M["track"], 0.012, root)
        flat_path("TrackLink", [(0.6, -7.0), (3.5, -6.6), (5.6, -5.0)], 1.8, M["track"], 0.011, root)
        for k, (p, yaw) in enumerate((((-3.2, -11.0), math.radians(200)), ((4.0, -10.6), math.radians(160)), ((-11.0, 4.5), math.radians(110)))):
            floodlight(f"Flood{k}", (*p, 0), root, 4.5 + 0.25 * L, yaw)
    if L >= 5:
        walks = [[(0, -4.6), (0, -12.5)], [(-0.9, -6.0), (-2.6, -6.4)], [(-3.5, -0.25), (-8.8, -0.25)]]
        for k, w in enumerate(walks):
            flat_path(f"Walk{k}", w, 1.2, M["concrete"], 0.02, root)
            a, b = Vector(w[0]), Vector(w[-1])
            n = max(2, int((b - a).length / 1.6))
            side = Vector((-(b - a).y, (b - a).x)).normalized() * 0.7
            for i in range(n + 1):
                p = a + (b - a) * (i / n)
                for sgn in (-1, 1):
                    q = p + side * sgn
                    sphere(f"WalkLight{k}_{i}{sgn}", 0.07, (q.x, q.y, 0.1), M["blue"], root, 6)

    # level 6: skybridge from the saucer rim to block A, squat saucer-topped cone tower at the back-right
    if L >= 6:
        a = Vector((-2.2, 3.5, 4.0))
        bpy.context.view_layer.update()
        b = BL.matrix_world @ Vector((0.8, 2.0 - admin_tiers[1][0] + 0.05, 2.75))
        strut("Skybridge", a, b, 0.42, M["hull"], root)
        strut("SkybridgeWindows", a + Vector((0, 0, 0.2)), b + Vector((0, 0, 0.2)), 0.3, M["window"], root)
        for f in (0.33, 0.66):
            p = a + (b - a) * f
            zcyl(f"SkybridgePier{f}", 0.22, 0.0, p.z - 0.3, M["panel"], root, xy=(p.x, p.y), segs=12)
        T2 = empty("AdminTower", (6.2, 5.6, 0), root)
        zcyl("AdminTowerCone", 1.7, 0.0, 6.5, M["hull"], T2, segs=32, radius2=0.8)
        torus("AdminTowerBand", 1.35, 0.07, (0, 0, 3.0), M["owner"], T2)
        lathe("AdminTowerSaucer", [(0, 6.4), (0.9, 6.4), (2.4, 7.0), (2.4, 7.35), (1.6, 7.8), (0, 7.8)], M["hull"], T2, segs=40)
        window_ring("AdminTowerWin", 2.41, 7.02, 7.32, 28, M["window"], T2, frac=0.6)
        window_ring("AdminTowerSlits", 1.3, 4.0, 5.5, 10, M["window"], T2, frac=0.15)
        sphere("AdminTowerLight", 0.16, (0, 0, 8.0), M["amber"], T2, 10)

    # beyond the hex (7+): neighbours' edges
    if L >= 7:
        landing_pad("BigPad", (-10.5, -15.8, 0), root, 5.0, "red")
        shuttle("Shuttle2", (-10.5, -15.8, 0.16), math.radians(30), root, "37")
        RE = edge_frame("Right", 0, 15.8, root)
        solar_hall("SolarHallA", RE, (1.5, -1.0, 0), 10.0, 2.4)
        solar_hall("SolarHallB", RE, (1.5, 2.2, 0), 10.0, 2.4)
    if L >= 8:
        ribbed_dome("AssemblyDome", (-1.5, 18.8, 0), root, 5.6, 4.4)
    if L >= 9:
        needle_spire("NeedleA", (-15.5, 6.0, 0), root, 17.0, 4)
        needle_spire("NeedleB", (-13.2, 12.4, 0), root, 13.0, 3)
        saucer_platform("PlatformA", (12.6, 12.6, 0), root, 7.0, 2.6)
        saucer_platform("PlatformB", (16.2, 9.0, 0), root, 5.2, 2.1)
    if L >= 10:
        dish_array("CommsArray", (15.5, -12.8, 0), root, 3)
        landing_pad("Pad3", (-17.5, -6.0, 0), root, 3.6, "stripes")
        BL2 = edge_frame("BackLeftOut", 120, 15.5, root)
        terraced("BlockC", BL2, 9.0, [(3.4, 1.9), (2.2, 1.7), (1.1, 1.6)])

    # colonists, more per level, round the busy spots
    spots = [(1.3, -8.4), (-1.1, -9.2), (6.2, -4.0), (5.9, 0.5), (-4.9, -1.0), (-4.4, 2.4), (2.0, -9.6), (-2.6, -9.6),
             (4.0, 4.0), (-1.5, 6.2), (6.8, -7.6), (-8.8, -5.6), (1.0, 8.6), (-8.2, -4.6), (-3.0, -12.3), (3.4, -12.0),
             (-12.0, -11.0), (11.0, -10.0), (-7.0, -13.0), (12.0, 4.5)]
    uniforms = [M["uni_red"], M["uni_gold"], M["uni_blue"], M["hull"]]
    for k in range(min(len(spots), 2 + 2 * L)):
        x, y = spots[k]
        colonist(f"Colonist{k}", (x, y, 0), uniforms[k % 4], root, rng.uniform(0, 2 * math.pi))
    return root


# ---------------------------------------------------------------- stage, cameras, render


def stage():
    scene = bpy.context.scene
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = math.radians(24)
    sky.sun_rotation = math.radians(150)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.12
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = -0.6
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(58), 0, math.radians(150))
    link(sun)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=1500)
    mesh_object("Ground", bm, paint("O_Sand", (0.36, 0.27, 0.18), 0.95, 0.0, scuff=0.12, dirt=0.0))
    # hex outlines: this hex bright, its six neighbours dim
    centres = [(0.0, 0.0)] + [(HEX * math.cos(math.radians(60 * k)), HEX * math.sin(math.radians(60 * k))) for k in range(6)]
    for i, (cx, cy) in enumerate(centres):
        cs = hex_corners(cx, cy)
        flat_path(f"HexLine{i}", cs + [cs[0]], 0.14 if i == 0 else 0.1, M["hexline" if i == 0 else "hexline_dim"], 0.025)


def shoot(folder):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    if THREADS:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = THREADS
    scene.render.film_transparent = False
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    cam.data.clip_end = 1000
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        if name == "front34":
            # fixed for every level, framed for level 10 so smaller levels read as growth
            scene.render.resolution_x, scene.render.resolution_y = RES, int(RES * 2 / 3)
            target = Vector((0.5, 1.0, 6.5))
            pos = Vector((40.0, -56.0, 44.0))
            cam.data.sensor_fit = "AUTO"
            cam.data.lens = 34
            path = os.path.join(folder, f"outpost-L{LEVEL:02d}.png")
        elif name == "game":
            # the game camera: PlanetDriver eases the view direction from (0, 0.34, 0.94) to (0, 0.82, 0.57) between
            # zoom 15 and 160 (Godot +Z = Blender -Y); Planet.tscn fov 45 degrees vertical; looks at the ground under
            # the focus
            scene.render.resolution_x, scene.render.resolution_y = RES, int(RES * 9 / 16)
            dist = 50.0
            x = min(1.0, max(0.0, (dist - 15) / (160 - 15)))
            t = x * x * (3 - 2 * x)
            d = Vector((0, 0.34, 0.94)).lerp(Vector((0, 0.82, 0.57)), t).normalized()
            target = Vector((0, 0, 0.5))
            pos = target + Vector((0, -d.z, d.y)) * dist
            cam.data.sensor_fit = "VERTICAL"
            cam.data.angle_y = math.radians(45)
            path = os.path.join(folder, f"outpost-L{LEVEL:02d}-game.png")
        if name == "close":
            scene.render.resolution_x, scene.render.resolution_y = RES, int(RES * 2 / 3)
            target = Vector((0.0, 0.0, 3.5))
            pos = Vector((20.0, -28.0, 16.0))
            cam.data.sensor_fit = "AUTO"
            cam.data.lens = 30
            path = os.path.join(folder, f"outpost-L{LEVEL:02d}-close.png")
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    root = build(LEVEL)
    if OUT:
        BV["export"](root, OUT)
    if PREVIEW:
        stage()
        rover = BV["rover_mk1"](BV["palette"]())
        rover.location = (PAD_AT.x, PAD_AT.y - 0.3, 0.22)
        rover.rotation_euler.z = math.pi  # nose to the front (-Y), as vehicles park on the pad in the game
        shoot(PREVIEW)


main()
