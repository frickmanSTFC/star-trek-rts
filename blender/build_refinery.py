"""Federation Ore Refinery, game-ready (owner 2026-11-06: "We need to work on the refinery full detail").

Process line, as a crawler's cargo pod sees it: the pod sets down on the PodPad of the raised receiving bay (front
right) and drops its ore through the grate into the hopper under the deck; the jaw crusher below it (flywheel =
"Crusher") breaks it up and loads the inclined belt conveyor ("Belt" material scrolls, "Ore0..7" lumps ride it under
"OreFlow") that climbs to the charging hood of the induction furnace (centre back: copper coils, three glowing tapping
ports = material "FurnaceGlow", ring catwalk, main stack with "Smoke0"). Molten metal runs down a glowing runner into
the casting hall (front left; radiator-fin roof, wall fan "Fan2", door glow) and leaves as ingot stacks (duranium blue
grey, tritanium bronze). Refined metal is blown into the two storage silos (back left: DURANIUM / TRITANIUM). Cooling
water loops through two fan-topped cooling towers (back right, "Fan0", "Fan1"); the scrubber's secondary stack carries
"Smoke1". A control booth at the front carries the "REFINERY nn" sign. Owner trim bands, hazard striping, caged
ladders, railings, light masts, beacons.

Frame and conventions (CLAUDE.md "Vehicle models"): Blender +Z up, front toward -Y (= Godot +Z, the buildings'
front); 1 unit = 1 world unit; the slab is 12.6 x 10.6 (corner radius ~8.2, inside the 8.5 the scatter keeps clear),
main stack top 11.3. Material "Owner" is recoloured per player; the game looks parts up by name (FindChild), so they
stay where they are in the hierarchy.

BUILD PIECES (for the construction view: optional child groups Build0..Build14 in build order, bottom-up):
  Build0 Foundation, Build1 Furnace, Build2 Crusher, Build3 ReceivingBay, Build4 Conveyor, Build5 CastingHall,
  Build6 Ingots, Build7 Silos, Build8 CoolingTowers, Build9 Pipes, Build10 Catwalks, Build11 Stacks,
  Build12 ControlBooth, Build13 RoofGear, Build14 Lights.
Every static mesh in a piece is joined into "Build<k>Body"; animated pivots keep their own mesh "<pivot>Body".

GAME CONTRACT (BuildingModels.Refinery.cs / PlanetView.Refinery.cs):
  Crusher        flywheel pivot; spin about its local Y in Godot (Blender local Z).
  Fan0, Fan1     cooling-tower fans (vertical axis); Fan2 the casting hall's wall fan (horizontal axis). All spin
                 about their own local Y in Godot.
  OreFlow        empty at the belt's foot; children Ore0..Ore7 (lumps) ride from it toward its child OreEnd.
  Belt           material: rubber belt with cleats, UV v runs along the belt every 0.6 units (scroll Uv1Offset.y).
  FurnaceGlow    material of the ports, spout and runner (pulse its emission); FurnaceLight empty = where the
                 orange OmniLight goes; SparkPoint empty = sparks when broken.
  Lamp, PadLight, Beacon, Window, Interior   glow materials (dim when idle).
  Smoke0, Smoke1 empties at the stack tips (steam / smoke emitters).
  PodPad         empty at the pad's surface: where cargo pods land.
  Stacks, Silos, Towers   inner pivots at their base, scaled in Y for per-instance variety (±10%).
  SignFront, SignHall, LabelSilo0, LabelSilo1, LabelPad   decal markers (Blender scale (width, height, 1); text along
                 local +X, facing local +Z) for Label3Ds: REFINERY nn, CASTING, DURANIUM, TRITANIUM, the pad number.
Textured materials (Hull, Panel, Trim, Dark, Steel, Concrete, Copper, SiloDur, SiloTri, Hazard, Rail) share one baked
grime atlas (Cycles EMIT bake of dirt-from-the-ground + ambient occlusion + streaks + soot) multiplied by their own
colour (glTF baseColorTexture x baseColorFactor), so the game can still tint them per instance.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_refinery.py --game          (export game/models/buildings/refinery.glb)
  /home/user/.bpyvenv/bin/python blender/build_refinery.py --game --preview blender/previews/refinery
Flags: --out <glb>, --no-join, --no-bake, --bake-res 1024, --res 960, --samples 24, --threads N,
       --shots hero,game,close,night,top,back,pieces
Previews land in blender/previews/refinery/refinery-<shot>.png ("close" also writes close2; "pieces" writes the build
order at pieces 3/7/11/15 as refinery-pieces03..15.png, kept in the repo as the 2 x 2 sheet refinery-pieces.png).
A full --game export plus every preview takes ~3 min.
"""
import math
import os
import re
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
OUT = arg("--out") or (os.path.join(REPO, "game", "models", "buildings", "refinery.glb") if GAME else None)
PREVIEW = arg("--preview")
ALL_SHOTS = ["hero", "game", "close", "night", "top", "back", "pieces"]
SHOTS = [s.strip() for s in arg("--shots", "hero,game,close,night,top,back").split(",") if s.strip()]
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "24"))
THREADS = int(arg("--threads", "0"))
JOIN = "--no-join" not in argv
BAKE = "--no-bake" not in argv
BAKE_RES = int(arg("--bake-res", "1024"))
if set(SHOTS) - set(ALL_SHOTS):
    sys.exit(f"--shots takes {', '.join(ALL_SHOTS)}")


def load(script, extra_argv=()):
    """Exec another builder without its trailing main() (as build_outpost_l1.py does) and return its namespace."""
    path = os.path.join(HERE, script)
    with open(path) as f:
        src = re.sub(r"^main\(\)\s*$", "", f.read(), flags=re.M)
    saved = sys.argv
    sys.argv = [path, *extra_argv]
    ns = {"__name__": script[:-3], "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


OP = load("build_outpost.py", ["--level", "1"])  # lathe, dome, zcyl, sphere, torus, flat_path, pane_row, stage ...
BV = OP["BV"]
box, cylinder, empty, mesh_object, strut, link = BV["box"], BV["cylinder"], BV["empty"], BV["mesh_object"], BV["strut"], BV["link"]
zcyl, sphere, lathe, flat_path, pane_row = OP["zcyl"], OP["sphere"], OP["lathe"], OP["flat_path"], OP["pane_row"]

# ---------------------------------------------------------------- layout (Blender frame, front = -Y)

SLAB = 0.35                                   # slab top
SLAB_X, SLAB_Y0, SLAB_Y1 = 6.3, -5.15, 5.45    # slab extents
F = Vector((-1.4, 1.8))                       # furnace centre
FR = 2.25                                     # furnace body radius
F_TOP = 4.6                                   # body top / ring catwalk level
F_APEX = 5.65                                 # roof cone top (main stack base)
D = Vector((4.0, -2.3))                       # receiving deck centre
DH = 2.0                                      # deck half size
DZ = 2.5                                      # deck top
CR = Vector((4.0, -2.2))                      # crusher centre (under the deck)
CR_TOP = SLAB + 1.25
BELT_A = Vector((3.9, -1.05, 1.0))            # belt foot (crusher outlet)
BELT_B = Vector((1.15, 1.8, 3.35))            # belt head (furnace charging hood)
SILOS = [Vector((-5.15, 0.6)), Vector((-5.15, 3.3))]
SILO_R = 1.0
TOWERS = [Vector((3.1, 4.05)), Vector((5.1, 2.2))]
HALL = (-6.0, -1.9, -4.4, -1.65)              # x0, x1, y0, y1
HALL_Z = 2.7
BOOTH = Vector((0.45, -3.85))
STACK_H = 5.6                                 # main stack height above the roof apex
PIECES = ["Foundation", "Furnace", "Crusher", "ReceivingBay", "Conveyor", "CastingHall", "Ingots", "Silos",
          "CoolingTowers", "Pipes", "Catwalks", "Stacks", "ControlBooth", "RoofGear", "Lights"]

# ---------------------------------------------------------------- materials

MT = {}
TEXTURED = []   # materials that get the baked grime atlas


def mat(key, name, colour, metallic=0.1, roughness=0.5, emission=None, strength=0.0, coat=0.0, textured=False, cull=True):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*colour, 1.0)
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = roughness
    if coat:
        p.inputs["Coat Weight"].default_value = coat
    if emission is not None:
        p.inputs["Emission Color"].default_value = (*emission, 1.0)
        p.inputs["Emission Strength"].default_value = strength
    m.use_backface_culling = cull
    m["colour"] = colour
    MT[key] = m
    if textured:
        TEXTURED.append(m)
    return m


def materials():
    mat("hull", "Hull", (0.76, 0.77, 0.79), 0.15, 0.42, coat=0.2, textured=True)
    mat("panel", "Panel", (0.56, 0.58, 0.62), 0.2, 0.5, textured=True)
    mat("accent", "Accent", (0.14, 0.3, 0.55), 0.3, 0.45, textured=True)
    mat("trim", "Trim", (0.26, 0.27, 0.3), 0.4, 0.5, textured=True)
    mat("dark", "Dark", (0.08, 0.085, 0.095), 0.4, 0.6, textured=True)
    mat("steel", "Steel", (0.55, 0.57, 0.6), 0.85, 0.32, textured=True)
    mat("concrete", "Concrete", (0.4, 0.39, 0.37), 0.0, 0.85, textured=True)
    mat("copper", "Copper", (0.78, 0.45, 0.26), 0.9, 0.3, textured=True)
    mat("silo_dur", "SiloDur", (0.36, 0.48, 0.64), 0.3, 0.45, textured=True)
    mat("silo_tri", "SiloTri", (0.66, 0.5, 0.32), 0.4, 0.42, textured=True)
    mat("hazard", "Hazard", (0.85, 0.62, 0.08), 0.0, 0.55, textured=True)
    mat("rail", "Rail", (0.86, 0.66, 0.12), 0.2, 0.5, textured=True, cull=False)
    mat("grate", "Grate", (0.17, 0.18, 0.2), 0.7, 0.55, cull=False)
    mat("owner", "Owner", (0.72, 0.08, 0.06), 0.1, 0.4)
    mat("window", "Window", (1.0, 0.82, 0.55), 0.0, 0.3, emission=(1.0, 0.78, 0.45), strength=3.0)
    mat("interior", "Interior", (1.0, 0.6, 0.3), 0.0, 0.4, emission=(1.0, 0.55, 0.25), strength=2.0)
    mat("glass", "Glass", (0.03, 0.06, 0.09), 0.3, 0.06, emission=(0.1, 0.25, 0.4), strength=0.25, coat=1.0)
    mat("glow", "FurnaceGlow", (1.0, 0.45, 0.12), 0.0, 0.3, emission=(1.0, 0.42, 0.1), strength=8.0)
    mat("lamp", "Lamp", (1.0, 0.95, 0.85), 0.0, 0.3, emission=(1.0, 0.95, 0.85), strength=6.0)
    mat("padlight", "PadLight", (0.3, 0.6, 1.0), 0.0, 0.3, emission=(0.3, 0.62, 1.0), strength=5.0)
    mat("beacon", "Beacon", (1.0, 0.1, 0.06), 0.0, 0.3, emission=(1.0, 0.08, 0.05), strength=8.0)
    mat("ingot_dur", "IngotDur", (0.58, 0.64, 0.74), 0.9, 0.25)
    mat("ingot_tri", "IngotTri", (0.72, 0.56, 0.4), 0.9, 0.28)
    mat("valve", "Valve", (0.7, 0.1, 0.08), 0.3, 0.5, textured=True)
    mat("ore", "Ore", (0.33, 0.29, 0.26), 0.1, 0.9)
    mat("pallet", "Pallet", (0.42, 0.34, 0.24), 0.0, 0.85)
    mat("sock", "Sock", (1.0, 0.4, 0.08), 0.0, 0.7, cull=False)
    belt = mat("belt", "Belt", (1, 1, 1), 0.0, 0.85)
    belt.node_tree.links.new(belt_texture(belt), belt.node_tree.nodes["Principled BSDF"].inputs["Base Color"])


def belt_texture(m):
    """A 32 x 64 rubber belt: dark with a raised cleat every 16 rows and worn edges (scrolls in the game)."""
    w, h = 32, 64
    img = bpy.data.images.new("BeltTex", w, h)
    px = []
    for y in range(h):
        for x in range(w):
            v = 0.05 + 0.015 * ((x * 7 + y * 13) % 5) / 4
            if y % 16 in (0, 1, 2):
                v = 0.22 if 3 < x < w - 4 else 0.12
            if x < 2 or x >= w - 2:
                v = 0.14
            px += [v, v, v * 0.95, 1.0]
    img.pixels = px
    img.pack()
    tex = m.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    return tex.outputs["Color"]


# ---------------------------------------------------------------- geometry helpers


def bx(name, size, loc, material, parent, bev=0.0, rot=(0, 0, 0)):
    """build_vehicles.box with a one-segment bevel (half the triangles)."""
    o = box(name, size, loc, material, parent, bev, rot)
    for m in o.modifiers:
        m.segments = 1
    return o


def frame(u, up=Vector((0, 0, 1))):
    """Rotation whose local +Y runs along u and local +Z is as close to `up` as possible."""
    u = Vector(u).normalized()
    s = u.cross(up)
    if s.length < 1e-6:
        s = Vector((1, 0, 0))
    s.normalize()
    n = s.cross(u)
    return Matrix((s, u, n)).transposed()


def obox(name, size, centre, u, material, parent, bev=0.0, up=Vector((0, 0, 1))):
    """A box whose size[1] runs along direction u, centred at `centre`."""
    o = bx(name, size, (0, 0, 0), material, parent, bev)
    o.matrix_basis = Matrix.Translation(Vector(centre)) @ frame(u, up).to_4x4()
    return o


def faces_obj(name, quads, material, parent, shadow=False):
    """Loose polygons [(p0, p1, p2[, p3]), ...] (decals, stripes) as one mesh."""
    bm = bmesh.new()
    for q in quads:
        vs = []
        for p in q:
            if all((Vector(p) - Vector(v.co)).length > 1e-5 for v in vs):
                vs.append(bm.verts.new(p))
        if len(vs) >= 3:
            bm.faces.new(vs)
    o = mesh_object(name, bm, material, parent)
    o.visible_shadow = shadow
    return o


def stripes(name, a, b, width, z, parent, pitch=0.36):
    """Diagonal hazard stripes on a flat strip from a to b (2D), `width` wide to the left of a->b, lying at height z."""
    a, b = Vector((a[0], a[1], z)), Vector((b[0], b[1], z))
    d = b - a
    L = d.length
    u = d / L
    s = Vector((-u.y, u.x, 0)) * width
    skew = width * 0.9
    yellow, black = [], []
    t, k = -skew, 0
    while t < L:
        t1 = t + pitch / 2
        c = lambda x: max(0.0, min(L, x))  # noqa: E731
        q = (a + u * c(t), a + u * c(t1), a + u * c(t1 + skew) + s, a + u * c(t + skew) + s)
        (yellow if k % 2 == 0 else black).append(q)
        t, k = t1, k + 1
    faces_obj(f"{name}Y", yellow, MT["hazard"], parent)
    faces_obj(f"{name}K", black, MT["dark"], parent)


def ring_mesh(name, r0, r1, z0, z1, material, parent, xy=(0, 0), segs=32):
    """An annulus (washer) between radii r0 < r1, z0..z1: catwalk decks, pad rings."""
    o = lathe(name, [(r0, z0), (r1, z0), (r1, z1), (r0, z1), (r0, z0)], material, parent, (xy[0], xy[1], 0), segs, smooth=False)
    return o


def band(name, r, z0, z1, material, parent, xy=(0, 0), segs=32):
    """An open painted band round a cylinder (no caps, so it can sit on hollow things like the cooling towers)."""
    bm = bmesh.new()
    lo = [bm.verts.new((xy[0] + r * math.cos(2 * math.pi * k / segs), xy[1] + r * math.sin(2 * math.pi * k / segs), z0)) for k in range(segs)]
    hi = [bm.verts.new((xy[0] + r * math.cos(2 * math.pi * k / segs), xy[1] + r * math.sin(2 * math.pi * k / segs), z1)) for k in range(segs)]
    for k in range(segs):
        j = (k + 1) % segs
        bm.faces.new((lo[k], lo[j], hi[j], hi[k]))
    o = mesh_object(name, bm, material, parent, smooth=True)
    return o


def hoop(name, r, z, thick, material, parent, xy=(0, 0), segs=32):
    """A thin round rail (handrail ring, cage hoop) as a square-section lathe (cheaper than a torus)."""
    h = thick / 2
    return lathe(name, [(r - h, z - h), (r + h, z - h), (r + h, z + h), (r - h, z + h), (r - h, z - h)], material, parent,
                 (xy[0], xy[1], 0), segs, smooth=False)


def railing(name, pts, z, parent, h=0.9, post=0.75, closed=False):
    """Yellow handrail along a polyline at deck height z: posts every ~`post`, a top rail and a mid rail."""
    pts = [Vector((p[0], p[1], z)) for p in pts]
    if closed:
        pts.append(pts[0])
    k = 0
    for a, b in zip(pts, pts[1:]):
        d = b - a
        n = max(1, round(d.length / post))
        for i in range(n + (1 if b is pts[-1] and not closed else 0)):
            p = a + d * (i / n)
            bx(f"{name}Post{k}", (0.05, 0.05, h), p + Vector((0, 0, h / 2)), MT["rail"], parent)
            k += 1
        for j, zz in enumerate((h, h * 0.5)):
            obox(f"{name}Rail{k}_{j}", (0.045, d.length, 0.045), a + d / 2 + Vector((0, 0, zz)), d, MT["rail"], parent)


def ladder(name, base, top_z, facing, parent, cage=True, width=0.42):
    """A caged ladder at `base` (x, y, z) climbing to top_z; `facing` = unit 2D vector the climber faces away from."""
    base = Vector(base)
    f = Vector((facing[0], facing[1], 0)).normalized()
    s = Vector((-f.y, f.x, 0))
    h = top_z - base.z
    for side in (-1, 1):
        p = base + s * (side * width / 2)
        bx(f"{name}Stile{side}", (0.04, 0.04, h + 0.9), p + Vector((0, 0, (h + 0.9) / 2)), MT["steel"], parent)
    n = int(h / 0.3)
    for i in range(1, n + 1):
        obox(f"{name}Rung{i}", (0.025, width, 0.025), base + Vector((0, 0, i * 0.3)), s, MT["steel"], parent)
    if cage and h > 2.2:
        c = base + f * 0.38
        for i, z in enumerate([base.z + 2.0 + k * 0.7 for k in range(int((h - 1.6) / 0.7) + 1)]):
            hoop(f"{name}Hoop{i}", 0.36, z, 0.035, MT["steel"], parent, xy=(c.x, c.y), segs=12)
        for k, a in enumerate((-60, 0, 60)):
            t = math.radians(a)
            p = c + (f * math.cos(t) + s * math.sin(t)) * 0.36
            bx(f"{name}CageBar{k}", (0.03, 0.03, h - 1.7), Vector((p.x, p.y, base.z + 2.0 + (h - 1.7) / 2)), MT["steel"], parent)


def pipe(name, pts, radius, material, parent, segs=10, joints=True):
    """A pipe along a polyline of 3D points, with ball joints at the bends."""
    pts = [Vector(p) for p in pts]
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        strut(f"{name}S{i}", a, b, radius, material, parent, segs)
    if joints:
        for i, p in enumerate(pts[1:-1]):
            sphere(f"{name}J{i}", radius * 1.18, p, material, parent, 8)


def rock(name, radius, loc, parent, seed):
    """A small lump of ore: a jittered icosphere."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=radius)
    for i, v in enumerate(bm.verts):
        k = math.sin(seed * 12.9898 + i * 78.233) * 43758.5453
        j = 0.72 + 0.5 * (k - math.floor(k))
        v.co *= j
        v.co.z *= 0.7
    o = mesh_object(name, bm, MT["ore"], parent, loc)
    return o


def marker(name, loc, rot, width, height, parent):
    """A decal marker (build_vehicles.text --blank convention): text along local +X facing local +Z, scale (w, h, 1)."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.4
    e.matrix_basis = Matrix.Translation(Vector(loc)) @ Matrix.Rotation(rot[2], 4, "Z") @ Matrix.Rotation(rot[0], 4, "X") @ Matrix.Diagonal((width, height, 1.0, 1.0))
    return link(e, parent)


def P(x, y, z=0.0):
    return Vector((x, y, z))


# ---------------------------------------------------------------- the pieces


def foundation(g):
    bx("Slab", (2 * SLAB_X, SLAB_Y1 - SLAB_Y0, SLAB), P(0, (SLAB_Y0 + SLAB_Y1) / 2, SLAB / 2), MT["concrete"], g, 0.06)
    bx("Skirt", (2 * SLAB_X + 0.3, SLAB_Y1 - SLAB_Y0 + 0.3, 0.16), P(0, (SLAB_Y0 + SLAB_Y1) / 2, 0.08), MT["dark"], g, 0.03)
    z = SLAB + 0.003
    # expansion joints every ~2.1 units
    qs = []
    for x in [-4.2, -2.1, 0.0, 2.1, 4.2]:
        qs.append((P(x - 0.02, SLAB_Y0 + 0.1, z), P(x + 0.02, SLAB_Y0 + 0.1, z), P(x + 0.02, SLAB_Y1 - 0.1, z), P(x - 0.02, SLAB_Y1 - 0.1, z)))
    for y in [-3.0, -0.6, 1.8, 4.2]:
        qs.append((P(-SLAB_X + 0.1, y - 0.02, z), P(SLAB_X - 0.1, y - 0.02, z), P(SLAB_X - 0.1, y + 0.02, z), P(-SLAB_X + 0.1, y + 0.02, z)))
    faces_obj("Joints", qs, MT["dark"], g)
    # painted walkway lines: booth -> furnace, round the deck, along the front
    flat_path("WalkLineA", [(-0.6, -4.9), (-0.6, -0.9), (0.9, 0.2)], 0.08, MT["hazard"], z + 0.001, g)
    flat_path("WalkLineC", [(-6.0, -4.95), (6.1, -4.95)], 0.06, MT["hazard"], z + 0.001, g)
    # hazard stripes round the receiving bay's footprint and in front of the casting door
    stripes("BayStripeF", (D.x + DH + 0.2, D.y - DH - 0.45), (D.x - DH - 0.2, D.y - DH - 0.45), 0.35, z + 0.002, g)
    stripes("BayStripeW", (D.x - DH - 0.45, -2.9), (D.x - DH - 0.45, D.y + DH + 0.2), 0.35, z + 0.002, g)
    stripes("HallStripe", (-3.0, HALL[2] - 0.15), (-4.8, HALL[2] - 0.15), 0.3, z + 0.002, g)
    # drain grates and bollards
    for k, (x, y) in enumerate([(0.9, -1.9), (-4.0, -0.9), (2.0, 3.0), (-2.8, 4.9)]):
        bx(f"Drain{k}", (0.7, 0.35, 0.02), P(x, y, SLAB + 0.008), MT["grate"], g)
    for k, (x, y) in enumerate([(1.85, -4.75), (6.0, -4.75), (1.85, -0.2), (-1.55, -4.75)]):
        zcyl(f"Bollard{k}", 0.11, SLAB, SLAB + 0.8, MT["hazard"], g, xy=(x, y), segs=10)
        zcyl(f"BollardBand{k}", 0.113, SLAB + 0.45, SLAB + 0.6, MT["dark"], g, xy=(x, y), segs=10)
        zcyl(f"BollardCap{k}", 0.08, SLAB + 0.8, SLAB + 0.86, MT["lamp"], g, xy=(x, y), segs=10)


def furnace(g):
    c = F
    zcyl("FurnacePlinth", FR + 0.4, SLAB, 0.75, MT["concrete"], g, xy=c, segs=8)
    prof = [(0.0, 0.75), (FR + 0.1, 0.75), (FR + 0.1, 1.0), (FR, 1.1), (FR, 1.9), (FR - 0.15, 2.0), (FR - 0.15, 3.3),
            (FR, 3.4), (FR, F_TOP - 0.05), (FR + 0.1, F_TOP), (FR + 0.1, F_TOP + 0.15), (1.05, F_APEX - 0.1), (1.05, F_APEX), (0.0, F_APEX)]
    lathe("FurnaceBody", prof, MT["hull"], g, (c.x, c.y, 0), 32, smooth=False)
    zcyl("FurnaceFootBand", FR + 0.12, 0.75, 1.0, MT["trim"], g, xy=c, segs=32)
    zcyl("FurnaceOwnerBand", FR + 0.02, 3.85, 4.1, MT["owner"], g, xy=c, segs=32)
    zcyl("FurnaceRim", FR + 0.12, F_TOP - 0.02, F_TOP + 0.17, MT["trim"], g, xy=c, segs=32)
    for k in range(10):
        a = math.radians(18 + 36 * k)
        d = Vector((math.cos(a), math.sin(a), 0))
        p0 = c.to_3d() + d * (FR + 0.05) + P(0, 0, F_TOP + 0.19)
        p1 = c.to_3d() + d * 1.08 + P(0, 0, F_APEX - 0.06)
        obox(f"ConeRib{k}", (0.1, (p1 - p0).length, 0.07), (p0 + p1) / 2, p1 - p0, MT["trim"], g)
    hatch = c.to_3d() + P(-0.95, -1.1, 0)
    bx("RoofHatch", (0.6, 0.5, 0.2), hatch + P(0, 0, 5.05), MT["panel"], g, 0.02, rot=(math.radians(-24), 0, math.radians(40)))
    # induction coils round the waist
    for k, z in enumerate((2.25, 2.62, 2.99)):
        hoop(f"Coil{k}", FR - 0.06, z, 0.17, MT["copper"], g, xy=c, segs=32)
    # the melt glowing through the gaps between the coils (seen from every side)
    for k, z in enumerate((2.435, 2.805)):
        band(f"CoilGlow{k}", FR - 0.145, z - 0.07, z + 0.07, MT["glow"], g, xy=c, segs=32).visible_shadow = False
    # ribs on the upper drum, and on the lower drum except round the ports at the front
    for k in range(12):
        a = math.radians(15 + 30 * k)
        d = Vector((math.cos(a), math.sin(a), 0))
        p = c.to_3d() + d * (FR + 0.04)
        bx(f"RibU{k}", (0.12, 0.16, F_TOP - 3.45), p + P(0, 0, (3.4 + F_TOP) / 2 - 0.02), MT["panel"], g, rot=(0, 0, a))
        deg = (15 + 30 * k + 180) % 360 - 180
        if not (-140 < deg < -40):
            bx(f"RibL{k}", (0.12, 0.16, 0.8), p + P(0, 0, 1.5), MT["panel"], g, rot=(0, 0, a))
    # three tapping ports at the front: recessed dark frames with glowing hearths, hoods above
    for k, deg in enumerate((-90, -63, -117)):
        a = math.radians(deg)
        d = Vector((math.cos(a), math.sin(a), 0))
        p = c.to_3d() + d * FR
        yaw = a - math.pi / 2
        bx(f"PortFrame{k}", (0.86, 0.3, 0.8), p + P(0, 0, 1.47), MT["dark"], g, 0.02, rot=(0, 0, yaw))
        bx(f"PortGlow{k}", (0.58, 0.05, 0.5), p + d * 0.155 + P(0, 0, 1.43), MT["glow"], g, rot=(0, 0, yaw)).visible_shadow = False
        bx(f"PortHood{k}", (1.0, 0.5, 0.12), p + d * 0.2 + P(0, 0, 1.98), MT["trim"], g, 0.02, rot=(math.radians(-18), 0, yaw))
        for j in (-1, 1):
            bx(f"PortJamb{k}{j}", (0.08, 0.36, 0.82), p + d * 0.02 + Vector((-d.y, d.x, 0)) * (j * 0.46) + P(0, 0, 1.47), MT["hazard"], g, rot=(0, 0, yaw))
    # tapping spout and the glowing runner down to the casting hall
    front = c.to_3d() + P(0, -FR, 0)
    obox("Spout", (0.42, 1.0, 0.22), front + P(0, -0.45, 1.05), P(0, -1, -0.25), MT["dark"], g, 0.02)
    obox("SpoutGlow", (0.22, 0.95, 0.03), front + P(0, -0.45, 1.17), P(0, -1, -0.25), MT["glow"], g).visible_shadow = False
    bx("Tundish", (0.75, 0.65, 0.45), P(c.x, -1.45, SLAB + 0.225), MT["dark"], g, 0.03)
    bx("TundishGlow", (0.55, 0.45, 0.02), P(c.x, -1.45, SLAB + 0.455), MT["glow"], g).visible_shadow = False
    ra, rb = P(c.x - 0.3, -1.5, SLAB), P(-2.35, HALL[3] + 0.05, SLAB)
    obox("Runner", (0.5, (rb - ra).length + 0.3, 0.26), (ra + rb) / 2 + P(0, 0, 0.13), rb - ra, MT["dark"], g, 0.02)
    obox("RunnerGlow", (0.26, (rb - ra).length + 0.2, 0.02), (ra + rb) / 2 + P(0, 0, 0.265), rb - ra, MT["glow"], g).visible_shadow = False
    for k in range(3):
        t = (k + 0.5) / 3
        obox(f"RunnerCover{k}", (0.6, 0.18, 0.05), ra.lerp(rb, t) + P(0, 0, 0.3), rb - ra, MT["steel"], g)
    # charging hood on the east side where the belt comes in
    hood = P(c.x + FR + 0.25, c.y, 3.4)
    bx("ChargeHood", (1.0, 1.3, 1.1), hood, MT["panel"], g, 0.04)
    bx("ChargeHoodRoof", (1.15, 1.45, 0.1), hood + P(0, 0, 0.6), MT["trim"], g, 0.02)
    bx("ChargeMouth", (0.06, 0.85, 0.55), hood + P(0.5, 0, -0.15), MT["dark"], g)
    bx("ChargeStripe", (1.02, 1.32, 0.12), hood + P(0, 0, -0.42), MT["owner"], g)
    # a pressure door on the west side and an inspection window strip
    a = math.radians(180)
    bx("FurnaceDoor", (0.08, 0.9, 1.2), c.to_3d() + P(-FR - 0.02, 0, 1.45), MT["trim"], g, 0.02)
    bx("FurnaceDoorLamp", (0.06, 0.5, 0.06), c.to_3d() + P(-FR - 0.05, 0, 2.15), MT["window"], g).visible_shadow = False
    e = empty("FurnaceLight", c.to_3d() + P(0, -FR - 1.4, 1.6), g)
    e.empty_display_size = 0.3
    empty("SparkPoint", c.to_3d() + P(0, -FR - 0.2, 1.45), g)


def crusher(g):
    c = CR.to_3d()
    bx("CrusherHousing", (2.2, 1.8, 1.25), c + P(0, 0, SLAB + 0.625), MT["accent"], g, 0.05)
    bx("CrusherBase", (2.4, 2.0, 0.2), c + P(0, 0, SLAB + 0.1), MT["trim"], g, 0.02)
    for k, z in enumerate((0.55, 1.15)):
        bx(f"CrusherFlange{k}", (2.24, 1.84, 0.07), c + P(0, 0, SLAB + z), MT["trim"], g)
    bx("CrusherDoor", (0.05, 0.8, 0.8), c + P(-1.11, 0.1, SLAB + 0.6), MT["trim"], g)
    for k in (-1, 1):
        bx(f"CrusherEdge{k}", (0.06, 1.82, 0.12), c + P(k * 1.1, 0, CR_TOP - 0.06), MT["hazard"], g)
    # motor and belt guard on the right, driving the flywheel
    bx("Motor", (0.7, 0.75, 0.6), c + P(1.5, 1.05, SLAB + 0.35), MT["trim"], g, 0.04)
    cylinder("MotorFan", 0.22, 0.12, c + P(1.9, 1.05, SLAB + 0.35), MT["dark"], g, axis="X", segments=12)
    obox("BeltGuard", (0.12, 1.45, 0.34), c + P(1.45, 0.4, SLAB + 0.54), P(0, -1.3, 0.37), MT["hazard"], g, 0.02)
    # outlet chute at the back onto the belt foot
    obox("OutletChute", (0.7, 0.75, 0.25), c + P(-0.05, 1.05, SLAB + 0.85), P(0, 1, -0.3), MT["steel"], g, 0.02)
    bx("LoadingHood", (0.95, 0.8, 0.45), BELT_A + P(0, 0.15, 0.32), MT["panel"], g, 0.03)
    # the flywheel: pivot at the shaft, local Z along +X (Godot local Y)
    fw = empty("Crusher", c + P(1.18, -0.25, SLAB + 0.72), g)
    fw.rotation_euler = (0, math.radians(90), 0)
    zcyl("FlywheelRim", 0.62, -0.08, 0.08, MT["steel"], fw, segs=24)
    zcyl("FlywheelHub", 0.17, -0.16, 0.16, MT["trim"], fw, segs=12)
    for k in range(3):
        bx(f"FlywheelSpoke{k}", (1.1, 0.12, 0.1), (0, 0, 0.06), MT["trim"], fw, rot=(0, 0, math.radians(60 * k)))
    bx("FlywheelMark", (0.2, 0.14, 0.18), (0.5, 0, 0.0), MT["hazard"], fw)
    strut("Shaft", c + P(1.05, -0.25, SLAB + 0.72), c + P(1.42, -0.25, SLAB + 0.72), 0.07, MT["steel"], g, 8)


def receiving_bay(g):
    c = D.to_3d()
    top = DZ
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        p = c + P(sx * (DH - 0.22), sy * (DH - 0.22), 0)
        bx(f"Column{k}", (0.3, 0.3, top - 0.22 - SLAB), p + P(0, 0, (top - 0.22 + SLAB) / 2), MT["hull"], g, 0.02)
        for j in range(3):
            bx(f"ColumnBand{k}{j}", (0.32, 0.32, 0.2), p + P(0, 0, SLAB + 0.1 + j * 0.4), MT["hazard" if j % 2 == 0 else "dark"], g)
        bx(f"ColumnFoot{k}", (0.5, 0.5, 0.06), p + P(0, 0, SLAB + 0.03), MT["steel"], g)
    # cross bracing on the back and right sides
    for k, (a, b) in enumerate((((-1, 1), (1, 1)), ((1, -1), (1, 1)))):
        pa = c + P(a[0] * (DH - 0.22), a[1] * (DH - 0.22), 0)
        pb = c + P(b[0] * (DH - 0.22), b[1] * (DH - 0.22), 0)
        strut(f"BraceA{k}", pa + P(0, 0, SLAB + 0.3), pb + P(0, 0, top - 0.35), 0.05, MT["trim"], g, 6)
        strut(f"BraceB{k}", pb + P(0, 0, SLAB + 0.3), pa + P(0, 0, top - 0.35), 0.05, MT["trim"], g, 6)
    bx("DeckBeams", (2 * DH - 0.2, 2 * DH - 0.2, 0.25), c + P(0, 0, top - 0.33), MT["trim"], g)
    bx("Deck", (2 * DH, 2 * DH, 0.14), c + P(0, 0, top - 0.07), MT["panel"], g, 0.02)
    for k, (sx, sy, w, d) in enumerate(((0, -1, 2 * DH + 0.04, 0.05), (0, 1, 2 * DH + 0.04, 0.05), (-1, 0, 0.05, 2 * DH + 0.04), (1, 0, 0.05, 2 * DH + 0.04))):
        bx(f"DeckEdge{k}", (w, d, 0.14), c + P(sx * DH, sy * DH, top - 0.07), MT["owner"], g)
    # hazard stripes round the deck, inside the rails
    z = top + 0.003
    corners = [c + P(-DH + 0.1, -DH + 0.1), c + P(DH - 0.1, -DH + 0.1), c + P(DH - 0.1, DH - 0.1), c + P(-DH + 0.1, DH - 0.1)]
    for k in range(4):
        a, b = corners[k], corners[(k + 1) % 4]
        stripes(f"DeckStripe{k}", (a.x, a.y), (b.x, b.y), 0.3, z, g)
    # the pad: dark disc, yellow ring, dashes, grate over the hopper, blue lights
    zcyl("Pad", 1.62, top, top + 0.025, MT["dark"], g, xy=D, segs=32)
    ring_mesh("PadRing", 1.38, 1.56, top + 0.025, top + 0.035, MT["hazard"], g, xy=D, segs=32)
    qs = []
    for k in range(4):
        a0 = math.radians(45 + 90 * k - 16)
        a1 = math.radians(45 + 90 * k + 16)
        r0, r1 = 0.95, 1.15
        for j in range(4):
            b0 = a0 + (a1 - a0) * j / 4
            b1 = a0 + (a1 - a0) * (j + 1) / 4
            qs.append((c + P(r0 * math.cos(b0), r0 * math.sin(b0), top + 0.03), c + P(r1 * math.cos(b0), r1 * math.sin(b0), top + 0.03),
                       c + P(r1 * math.cos(b1), r1 * math.sin(b1), top + 0.03), c + P(r0 * math.cos(b1), r0 * math.sin(b1), top + 0.03)))
    faces_obj("PadDashes", qs, MT["hazard"], g)
    zcyl("PadGrate", 0.62, top + 0.02, top + 0.04, MT["grate"], g, xy=D, segs=24)
    for k in range(5):
        bx(f"GrateBar{k}", (1.15 - abs(k - 2) * 0.28, 0.05, 0.03), c + P(0, (k - 2) * 0.22, top + 0.05), MT["steel"], g)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        p = c + P(1.75 * math.cos(a), 1.75 * math.sin(a), top)
        zcyl(f"PadLamp{k}", 0.07, top, top + 0.09, MT["padlight"], g, xy=(p.x, p.y), segs=8)
    pp = empty("PodPad", c + P(0, 0, top + 0.04), g)
    pp.empty_display_size = 1.0
    marker("LabelPad", c + P(0, -1.12, top + 0.036), (0, 0, 0), 0.7, 0.2, g)
    # hopper under the deck, down into the crusher
    hop = cylinder("Hopper", 1.1, top - 0.4 - CR_TOP, c + P(0, 0.05, (top - 0.4 + CR_TOP) / 2), MT["steel"], g, axis="Z", segments=4, radius2=2.3)
    hop.rotation_euler = (0, 0, math.radians(45))
    # railings (open on the west side where the ladder arrives) and the ladder
    railing("DeckRail", [(c.x - DH + 0.06, c.y - DH + 0.06), (c.x + DH - 0.06, c.y - DH + 0.06), (c.x + DH - 0.06, c.y + DH - 0.06),
                         (c.x - DH + 0.06, c.y + DH - 0.06), (c.x - DH + 0.06, c.y + 0.6)], top, g)
    ladder("DeckLadder", c + P(-DH - 0.05, -0.2, SLAB), top, (-1, 0), g)
    # flood lamps on two corners aiming at the pad, a windsock on the third
    for k, (sx, sy) in enumerate(((1, -1), (-1, 1))):
        p = c + P(sx * (DH - 0.12), sy * (DH - 0.12), top)
        zcyl(f"PadMast{k}", 0.05, top, top + 1.6, MT["steel"], g, xy=(p.x, p.y), segs=8)
        head = bx(f"PadFlood{k}", (0.36, 0.2, 0.22), p + P(0, 0, 1.65), MT["dark"], g, 0.02, rot=(math.radians(35), 0, math.atan2(-sy, -sx) + math.pi / 2))
        head.name = f"PadFlood{k}"
        bx(f"PadFloodLens{k}", (0.3, 0.03, 0.16), p + P(-sx * 0.09, -sy * 0.09, 1.6), MT["lamp"], g, rot=(math.radians(35), 0, math.atan2(-sy, -sx) + math.pi / 2)).visible_shadow = False
    p = c + P(DH - 0.12, DH - 0.12, top)
    zcyl("SockMast", 0.035, top, top + 2.0, MT["steel"], g, xy=(p.x, p.y), segs=8)
    sock = cylinder("Sock", 0.13, 0.75, p + P(0.38, -0.15, 1.88), MT["sock"], g, axis="X", segments=10, radius2=0.06)
    sock.rotation_euler = (0, math.radians(95), math.radians(-20))


def conveyor(g):
    A, B = BELT_A, BELT_B
    u = (B - A).normalized()
    L = (B - A).length
    R = frame(u)
    s = R.col[0].to_3d()
    n = R.col[2].to_3d()
    # the belt itself: its own mesh (keeps its UVs: u across, v along every 0.6) with the scrolling Belt material
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    steps = 12
    prev = None
    for i in range(steps + 1):
        t = L * i / steps
        p = A + u * t + n * 0.02
        row = (bm.verts.new(p - s * 0.31), bm.verts.new(p + s * 0.31))
        if prev:
            f = bm.faces.new((prev[0], prev[1], row[1], row[0]))
            for loop in f.loops:
                vi = loop.vert
                along = (Vector(vi.co) - A).dot(u)
                across = (Vector(vi.co) - A).dot(s)
                loop[uv].uv = (0.5 + across / 0.62, along / 0.6)
        prev = row
    belt = mesh_object("Belt", bm, MT["belt"], g)
    belt.visible_shadow = True
    for side in (-1, 1):
        obox(f"Stringer{side}", (0.08, L + 0.3, 0.3), A + u * (L / 2) + s * (side * 0.4) - n * 0.12, u, MT["trim"], g)
        obox(f"Skirt{side}", (0.03, L, 0.16), A + u * (L / 2) + s * (side * 0.335) + n * 0.08, u, MT["steel"], g)
    k = 0
    t = 0.2
    while t < L - 0.1:
        c = A + u * t - n * 0.07
        strut(f"Idler{k}", c - s * 0.33, c + s * 0.33, 0.055, MT["steel"], g, 8)
        t += 0.42
        k += 1
    for j, (t, r) in enumerate(((0.0, 0.2), (L, 0.22))):
        c = A + u * t - n * 0.05
        strut(f"Pulley{j}", c - s * 0.36, c + s * 0.36, r, MT["dark"], g, 12)
    # canopy over the upper two thirds: a half pipe with ribs and an owner stripe
    d0 = L * 0.34
    bm = bmesh.new()
    rings = []
    for i in range(9):
        t = d0 + (L - d0) * i / 8
        ring = []
        for k in range(9):
            a = math.pi * k / 8
            ring.append(bm.verts.new(A + u * t + s * (0.46 * math.cos(a)) + n * (0.08 + 0.42 * math.sin(a))))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(8):
            bm.faces.new((r0[k], r1[k], r1[k + 1], r0[k + 1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        cen = f.calc_center_median()
        axis = A + u * (cen - A).dot(u) + n * 0.08
        if f.normal.dot(cen - axis) < 0:
            f.normal_flip()
    o = mesh_object("Canopy", bm, MT["accent"], g)
    m = o.modifiers.new("Thick", "SOLIDIFY")
    m.thickness = 0.03
    for i in range(5):
        t = d0 + (L - d0) * (i + 0.5) / 5
        obox(f"CanopyRib{i}", (0.5, 0.07, 0.05), A + u * t + n * 0.52, u, MT["trim"], g)
        for side in (-1, 1):
            obox(f"CanopyStrap{i}{side}", (0.05, 0.07, 0.3), A + u * t + s * (side * 0.47) + n * 0.25, u, MT["trim"], g)
    # owner stripe along both lower edges of the canopy
    for side in (-1, 1):
        obox(f"CanopyStripe{side}", (0.035, L - d0, 0.07), A + u * ((d0 + L) / 2) + s * (side * 0.475) + n * 0.12, u, MT["owner"], g)
    # walkway on the west side with a handrail
    w = s * 0.72
    obox("Walkway", (0.5, L, 0.05), A + u * (L / 2) + w - n * 0.12, u, MT["grate"], g)
    for k in range(6):
        t = L * k / 5
        p = A + u * t + w + s * 0.24 - n * 0.1
        bx(f"WalkPost{k}", (0.045, 0.045, 0.9), p + P(0, 0, 0.45), MT["rail"], g)
    obox("WalkRail", (0.045, L, 0.045), A + u * (L / 2) + w + s * 0.24 + P(0, 0, 0.8) - n * 0.1, u, MT["rail"], g)
    obox("WalkRailMid", (0.045, L, 0.045), A + u * (L / 2) + w + s * 0.24 + P(0, 0, 0.4) - n * 0.1, u, MT["rail"], g)
    # support bents down to the slab
    for j, t in enumerate((L * 0.3, L * 0.68)):
        c = A + u * t - n * 0.25
        for side in (-1, 1):
            top = c + s * (side * 0.45)
            bx(f"Bent{j}{side}", (0.14, 0.14, top.z - SLAB), P(top.x, top.y, (top.z + SLAB) / 2), MT["hull"], g)
            bx(f"BentFoot{j}{side}", (0.3, 0.3, 0.05), P(top.x, top.y, SLAB + 0.025), MT["steel"], g)
        obox(f"BentBar{j}", (0.1, 0.9, 0.1), P(c.x, c.y, (c.z + SLAB) / 2 + 0.2), s, MT["trim"], g)
        strut(f"BentBrace{j}", P((c - s * 0.45).x, (c - s * 0.45).y, SLAB + 0.2), c + s * 0.45, 0.035, MT["trim"], g, 6)
    # ore lumps riding the belt: OreFlow at the foot, OreEnd at the head (local offset), lumps along the way
    flow = empty("OreFlow", A + n * 0.1, g)
    end = empty("OreEnd", u * (L - 0.15), flow)
    end.empty_display_size = 0.2
    for k in range(8):
        t = L * (k + 0.3) / 8
        across = s * (0.14 * math.sin(k * 2.3))
        rock(f"Ore{k}", 0.11 + 0.04 * ((k * 5) % 3), u * t + across, flow, k + 1)


def casting_hall(g):
    x0, x1, y0, y1 = HALL
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w, d = x1 - x0, y1 - y0
    bx("HallPlinth", (w + 0.1, d + 0.1, 0.32), P(cx, cy, SLAB + 0.16), MT["trim"], g, 0.02)
    bx("HallWalls", (w, d, HALL_Z - 0.3 - SLAB), P(cx, cy, (HALL_Z - 0.3 + SLAB) / 2 + 0.15), MT["panel"], g, 0.04)
    bx("HallAccent", (w + 0.02, d + 0.02, 0.5), P(cx, cy, SLAB + 0.6), MT["accent"], g)
    bx("HallRoof", (w + 0.3, d + 0.3, 0.16), P(cx, cy, HALL_Z), MT["panel"], g, 0.03)
    bx("HallFascia", (w + 0.32, 0.06, 0.34), P(cx, y0 - 0.15, HALL_Z - 0.1), MT["dark"], g)
    bx("HallOwnerBand", (w + 0.02, 0.04, 0.14), P(cx, y0 - 0.005, HALL_Z - 0.48), MT["owner"], g)
    marker("SignHall", P(cx - 1.2, y0 - 0.19, HALL_Z - 0.1), (math.radians(90), 0, 0), 1.8, 0.2, g)
    # the casting door: half-raised ribbed door over a glowing interior
    door_x = -3.95
    bx("DoorFrame", (2.0, 0.12, 1.9), P(door_x, y0 - 0.02, SLAB + 0.32 + 0.95), MT["trim"], g, 0.02)
    bx("DoorInterior", (1.7, 0.05, 0.8), P(door_x, y0 - 0.07, SLAB + 0.32 + 0.42), MT["interior"], g).visible_shadow = False
    bx("DoorPanel", (1.72, 0.08, 1.0), P(door_x, y0 - 0.09, SLAB + 0.32 + 1.32), MT["panel"], g)
    for k in range(5):
        bx(f"DoorRib{k}", (1.74, 0.1, 0.03), P(door_x, y0 - 0.1, SLAB + 0.32 + 0.9 + k * 0.2), MT["trim"], g)
    for j in (-1, 1):
        bx(f"DoorStripe{j}", (0.12, 0.13, 1.85), P(door_x + j * 0.93, y0 - 0.04, SLAB + 0.32 + 0.93), MT["hazard"], g)
    # window strip and wall lamps
    pane_row("HallWindows", (x0 + 0.2, y0 - 0.012, 0), (1, 0, 0), None, 1.7, SLAB + 1.65, SLAB + 1.95, 4, MT["window"], g)
    pane_row("HallWindowsE", (-2.95, y0 - 0.012, 0), (1, 0, 0), None, 0.9, SLAB + 1.65, SLAB + 1.95, 2, MT["window"], g)
    for k, x in enumerate((door_x - 1.2, door_x + 1.2)):
        bx(f"HallLamp{k}", (0.22, 0.12, 0.1), P(x, y0 - 0.07, SLAB + 2.0), MT["lamp"], g).visible_shadow = False
    # radiator fins on the roof with headers at the ends
    n_fins = 14
    for k in range(n_fins):
        x = x0 + 0.3 + (w - 0.6) * k / (n_fins - 1)
        bx(f"Fin{k}", (0.045, d - 0.7, 0.5), P(x, cy, HALL_Z + 0.33), MT["steel"], g)
    for j, y in enumerate((y0 + 0.3, y1 - 0.3)):
        strut(f"Header{j}", P(x0 + 0.2, y, HALL_Z + 0.25), P(x1 - 0.2, y, HALL_Z + 0.25), 0.09, MT["copper"], g, 10)
    # wall fan on the west wall (spins about local Z = facing -X)
    fan_at = P(x0 - 0.04, cy, SLAB + 1.25)
    bx("FanHousing", (0.12, 1.05, 1.05), fan_at, MT["trim"], g, 0.03)
    zcyl("FanShroud", 0.44, 0, 0.1, MT["dark"], None, segs=20).matrix_basis = Matrix.Translation(fan_at + P(-0.08, 0, 0)) @ Matrix.Rotation(math.radians(-90), 4, "Y")
    bpy.data.objects["FanShroud"].parent = g
    fan = empty("Fan2", fan_at + P(-0.14, 0, 0), g)
    fan.rotation_euler = (0, math.radians(-90), 0)
    zcyl("Fan2Hub", 0.1, -0.05, 0.05, MT["trim"], fan, segs=10)
    for k in range(5):
        bx(f"Fan2Blade{k}", (0.36, 0.11, 0.02), (0.2 * math.cos(math.radians(72 * k)), 0.2 * math.sin(math.radians(72 * k)), 0.0),
           MT["steel"], fan, rot=(math.radians(25), 0, math.radians(72 * k)))
    for k in range(3):
        bx(f"FanGuard{k}", (0.03, 0.03, 0.95), fan_at + P(-0.2, (k - 1) * 0.3, 0), MT["steel"], g)


def ingots(g):
    def stack(name, x, y, material, layers):
        bx(f"{name}Pallet", (1.0, 0.62, 0.1), P(x, y, SLAB + 0.05), MT["pallet"], g)
        for k in range(3):
            bx(f"{name}Skid{k}", (1.0, 0.08, 0.05), P(x, y - 0.25 + k * 0.25, SLAB + 0.02), MT["pallet"], g)
        for layer in range(layers):
            z = SLAB + 0.1 + 0.055 + layer * 0.11
            if layer % 2 == 0:
                for k in range(4):
                    bx(f"{name}Bar{layer}{k}", (0.92, 0.13, 0.1), P(x, y - 0.21 + k * 0.14, z), material, g, 0.012)
            else:
                for k in range(6):
                    bx(f"{name}Bar{layer}{k}", (0.13, 0.58, 0.1), P(x - 0.38 + k * 0.152, y, z), material, g, 0.012)
        bx(f"{name}Strap", (0.03, 0.64, layers * 0.11 + 0.02), P(x, y, SLAB + 0.1 + layers * 0.055), MT["steel"], g)
    stack("IngotA", -5.2, -4.78, MT["ingot_dur"], 4)
    stack("IngotB", -2.45, -4.78, MT["ingot_tri"], 3)
    stack("IngotC", -5.55, -0.95, MT["ingot_dur"], 2)
    stack("IngotD", -4.35, -1.0, MT["ingot_tri"], 5)


def silos(g):
    piv = empty("Silos", P(0, 0, SLAB), g)
    for k, c in enumerate(SILOS):
        paint = MT["silo_dur" if k == 0 else "silo_tri"]
        for j in range(4):
            a = math.radians(45 + 90 * j)
            p = c.to_3d() + P(0.72 * math.cos(a), 0.72 * math.sin(a), 0)
            bx(f"Silo{k}Leg{j}", (0.16, 0.16, 1.2), p + P(0, 0, 0.6), MT["hull"], piv)
            bx(f"Silo{k}LegFoot{j}", (0.32, 0.32, 0.05), p + P(0, 0, 0.025), MT["steel"], piv)
        prof = [(0.12, 0.55), (SILO_R, 1.25), (SILO_R, 5.55), (SILO_R + 0.04, 5.6), (0.3, 6.2), (0.0, 6.2)]
        lathe(f"Silo{k}Body", [(0.0, 0.55)] + prof, MT["hull"], piv, (c.x, c.y, 0), 24, smooth=True)
        zcyl(f"Silo{k}Outlet", 0.14, 0.2, 0.6, MT["steel"], piv, xy=c, segs=10)
        zcyl(f"Silo{k}BandLow", SILO_R + 0.012, 1.5, 1.8, paint, piv, xy=c, segs=24)
        zcyl(f"Silo{k}BandMid", SILO_R + 0.012, 3.0, 4.2, paint, piv, xy=c, segs=24)
        zcyl(f"Silo{k}BandTop", SILO_R + 0.012, 5.2, 5.45, MT["owner"], piv, xy=c, segs=24)
        for j, z in enumerate((2.4, 4.75)):
            hoop(f"Silo{k}Hoop{j}", SILO_R + 0.02, z, 0.05, MT["trim"], piv, xy=c, segs=24)
        zcyl(f"Silo{k}Hatch", 0.28, 6.1, 6.3, MT["trim"], piv, xy=c, segs=12)
        railing(f"Silo{k}Rail", [(c.x + 0.7 * math.cos(math.radians(a)), c.y + 0.7 * math.sin(math.radians(a))) for a in range(0, 360, 60)], 5.9, piv, h=0.5, post=0.8, closed=True)
        # label plate on the front, ladder on the outside
        bx(f"Silo{k}Plate", (1.15, 0.05, 0.42), c.to_3d() + P(0, -SILO_R - 0.02, 3.6), MT["panel"], piv)
        marker(f"LabelSilo{k}", c.to_3d() + P(0, -SILO_R - 0.05, 3.6), (math.radians(90), 0, 0), 1.0, 0.24, piv)
        ladder(f"Silo{k}Ladder", c.to_3d() + P(-SILO_R - 0.06, 0, 0.3), 5.6, (-1, 0), piv, cage=False)
        # a riser from the transfer line up to the roof
        pipe(f"Silo{k}Riser", [c.to_3d() + P(0.75, 0.62, 0.6), c.to_3d() + P(0.75, 0.62, 5.55), c.to_3d() + P(0.32, 0.25, 6.0)], 0.08, MT["steel"], piv)


def cooling_towers(g):
    piv = empty("Towers", P(0, 0, SLAB), g)
    for k, c in enumerate(TOWERS):
        def r_at(z):
            # hyperboloid-ish: 1.1 at the base, waist 0.8 at 2.6, 0.95 at the top 3.9
            return 0.8 + 0.3 * ((z - 2.6) / 2.1) ** 2 if z < 2.6 else 0.8 + 0.15 * ((z - 2.6) / 1.3) ** 2
        zs = [0.55 + (3.9 - 0.55) * i / 10 for i in range(11)]
        outer = [(r_at(z), z) for z in zs]
        inner = [(r_at(z) - 0.07, z) for z in reversed(zs)]
        lathe(f"Tower{k}Shell", outer + inner + [outer[0]], MT["hull"], piv, (c.x, c.y, 0), 28, smooth=True)
        zcyl(f"Tower{k}Basin", 1.22, 0.0, 0.3, MT["concrete"], piv, xy=c, segs=28)
        for j in range(8):
            a = math.radians(22.5 * (j % 2) + 45 * j)
            a2 = a + math.radians(22.5)
            pa = c.to_3d() + P(1.02 * math.cos(a), 1.02 * math.sin(a), 0.3)
            pb = c.to_3d() + P(1.06 * math.cos(a2), 1.06 * math.sin(a2), 0.6)
            strut(f"Tower{k}Col{j}a", pa, pb, 0.05, MT["panel"], piv, 6)
            strut(f"Tower{k}Col{j}b", c.to_3d() + P(1.02 * math.cos(a + math.radians(45)), 1.02 * math.sin(a + math.radians(45)), 0.3), pb, 0.05, MT["panel"], piv, 6)
        hoop(f"Tower{k}Lip", 0.96, 3.9, 0.08, MT["trim"], piv, xy=c, segs=28)
        band(f"Tower{k}Band", r_at(3.35) + 0.012, 3.25, 3.45, MT["owner"], piv, xy=c, segs=28)
        for j in range(2):
            obox(f"Tower{k}Guard{j}", (0.04, 1.85, 0.04), c.to_3d() + P(0, 0, 3.92), P(math.cos(math.radians(90 * j)), math.sin(math.radians(90 * j)), 0), MT["steel"], piv)
        sphere(f"Tower{k}Beacon", 0.07, c.to_3d() + P(0.92, 0, 3.98), MT["beacon"], piv, 8)
        fan = empty(f"Fan{k}", c.to_3d() + P(0, 0, 3.6), piv)
        zcyl(f"Fan{k}Hub", 0.14, -0.1, 0.12, MT["trim"], fan, segs=12)
        for j in range(6):
            a = math.radians(60 * j)
            bx(f"Fan{k}Blade{j}", (0.66, 0.15, 0.025), (0.38 * math.cos(a), 0.38 * math.sin(a), 0.03), MT["dark"], fan, rot=(math.radians(22), 0, a))
        zcyl(f"Fan{k}Motor", 0.12, -0.45, -0.1, MT["dark"], fan, segs=10)
    # a radiator bank between the towers
    rc = P(5.25, 4.55, 0)
    bx("RadBank", (1.6, 1.1, 0.25), rc + P(0, 0, 0.35), MT["trim"], piv, 0.02)
    for j in range(8):
        bx(f"RadFin{j}", (1.5, 0.05, 0.75), rc + P(0, -0.45 + j * 0.13, 0.85), MT["steel"], piv)


def pipes(g):
    st = MT["steel"]
    # coolant loop: supply (high, blue bands) from the furnace's east side to both towers' basins, return (low,
    # copper bands) from the second tower back
    sup_y, ret_y = F.y + 0.9, F.y + 0.58

    def wall_x(y):
        return F.x + math.sqrt(FR * FR - (y - F.y) ** 2) - 0.05

    a0 = P(wall_x(sup_y), sup_y, 1.55)
    pipe("CoolSupply", [a0, P(2.3, sup_y, 1.55), P(4.05, sup_y, 1.55), P(4.05, sup_y, SLAB + 0.45)], 0.13, st, g)
    pipe("CoolTee", [P(2.3, sup_y, 1.55), P(2.3, 3.3, 1.55), P(2.3, 3.3, SLAB + 0.45)], 0.12, st, g)
    a1 = P(wall_x(ret_y), ret_y, 1.15)
    pipe("CoolReturn", [a1, P(4.1, ret_y, 1.15), P(4.1, ret_y, SLAB + 0.45)], 0.11, st, g)
    for j, (a, y, rad, band) in enumerate(((a0, sup_y, 0.13, "silo_dur"), (a1, ret_y, 0.11, "copper"))):
        for t in (0.3, 0.75, 1.25):
            p = P(a.x + (2.3 - a.x) * t, y, a.z)
            o = zcyl(f"CoolBand{j}_{t}", rad + 0.014, -0.07, 0.07, MT[band], None, segs=10)
            o.matrix_basis = Matrix.Translation(p) @ Matrix.Rotation(math.radians(90), 4, "Y")
            o.parent = g
    # pipe rack under the coolant lines
    for k, x in enumerate((1.55, 3.25)):
        bx(f"RackPost{k}", (0.12, 0.12, 1.0 - SLAB), P(x, (sup_y + ret_y) / 2, (1.0 + SLAB) / 2), MT["trim"], g)
        bx(f"RackBeam{k}", (0.3, 0.75, 0.08), P(x, (sup_y + ret_y) / 2, 1.0), MT["trim"], g)
    # metal transfer line: furnace west side, low, into both silo risers; blower feeding it
    a = P(F.x - FR - 0.05, F.y + 0.2, 1.0)
    pipe("Transfer", [a, P(-4.0, F.y + 0.2, 1.0), P(-4.0, SILOS[0].y + 0.62, 1.0), P(SILOS[0].x + 0.75, SILOS[0].y + 0.62, 1.0)], 0.1, MT["panel"], g)
    pipe("Transfer2", [P(-4.0, F.y + 0.2, 1.0), P(-4.0, SILOS[1].y + 0.62, 1.0), P(SILOS[1].x + 0.75, SILOS[1].y + 0.62, 1.0)], 0.1, MT["panel"], g)
    bl = (-3.5, -0.45)
    zcyl("Blower", 0.32, SLAB, SLAB + 0.75, MT["trim"], g, xy=bl, segs=14)
    zcyl("BlowerBand", 0.33, SLAB + 0.5, SLAB + 0.62, MT["owner"], g, xy=bl, segs=14)
    pipe("BlowerPipe", [P(bl[0], bl[1], SLAB + 0.75), P(bl[0], bl[1], 1.0), P(-4.0, bl[1], 1.0), P(-4.0, SILOS[0].y + 0.62, 1.0)], 0.08, st, g)
    # scrubber duct: up off the furnace roof, over the ring catwalk's rail, down into the scrubber box
    pipe("Duct", [P(F.x - 0.85, F.y + 0.85, F_TOP + 0.7), P(F.x - 1.25, F.y + 1.7, 6.1), P(-3.25, 4.55, 6.1), P(-3.25, 4.55, SLAB + 2.6)], 0.22, MT["panel"], g)
    # gas bottles beside the booth and valves on the lines
    for k in range(3):
        zcyl(f"Bottle{k}", 0.14, SLAB, SLAB + 1.1, MT["silo_dur" if k != 1 else "hazard"], g, xy=(BOOTH.x - 1.55, BOOTH.y + 0.5 + k * 0.32), segs=10)
        sphere(f"BottleCap{k}", 0.1, P(BOOTH.x - 1.55, BOOTH.y + 0.5 + k * 0.32, SLAB + 1.13), MT["steel"], g, 8)
    for k, p in enumerate((P(1.9, sup_y, 1.55), P(-4.0, 1.6, 1.0), P(2.3, 3.0, 1.55))):
        w = hoop(f"Valve{k}", 0.15, 0.0, 0.035, MT["valve"], None, segs=12)
        w.matrix_basis = Matrix.Translation(p + P(0, 0, 0.32)) @ Matrix.Rotation(math.radians(90), 4, "X")
        w.parent = g
        zcyl(f"ValveStem{k}", 0.025, p.z, p.z + 0.32, st, g, xy=(p.x, p.y), segs=6)


def catwalks(g):
    c = F
    ring_mesh("RingDeck", FR + 0.15, FR + 0.75, F_TOP + 0.08, F_TOP + 0.14, MT["grate"], g, xy=c, segs=32)
    for k in range(16):
        a = math.radians(11.25 + 22.5 * k)
        p = c.to_3d() + P((FR + 0.72) * math.cos(a), (FR + 0.72) * math.sin(a), 0)
        bx(f"RingPost{k}", (0.045, 0.045, 0.9), p + P(0, 0, F_TOP + 0.59), MT["rail"], g, rot=(0, 0, a))
        if k % 2 == 0:
            strut(f"RingBracket{k}", c.to_3d() + P((FR + 0.05) * math.cos(a), (FR + 0.05) * math.sin(a), F_TOP - 0.55), p + P(0, 0, F_TOP + 0.06), 0.035, MT["trim"], g, 6)
    hoop("RingRailTop", FR + 0.72, F_TOP + 1.02, 0.045, MT["rail"], g, xy=c, segs=32)
    hoop("RingRailMid", FR + 0.72, F_TOP + 0.6, 0.04, MT["rail"], g, xy=c, segs=32)
    # caged ladder from the ground to the ring at the south-east, platform at the belt head
    a = math.radians(-35)
    d = Vector((math.cos(a), math.sin(a), 0))
    ladder("RingLadder", c.to_3d() + d * (FR + 0.82) + P(0, 0, SLAB), F_TOP + 0.14, (d.x, d.y), g)
    # a small landing at the belt head, cantilevered off the walkway side, with a rail
    u = (BELT_B - BELT_A).normalized()
    R = frame(u)
    s = R.col[0].to_3d()
    hp = BELT_B + s * 0.95 - P(0, 0, 0.12)
    hp.z = BELT_B.z - 0.15
    bx("HeadLanding", (0.8, 0.8, 0.06), hp, MT["grate"], g, rot=(0, 0, math.atan2(u.y, u.x)))
    strut("HeadStrut", hp + P(0, 0, -0.03), P(hp.x, hp.y, SLAB), 0.05, MT["trim"], g, 6)
    railing("HeadRail", [(hp.x + s.x * 0.38 - u.x * 0.38, hp.y + s.y * 0.38 - u.y * 0.38), (hp.x + s.x * 0.38 + u.x * 0.38, hp.y + s.y * 0.38 + u.y * 0.38)], hp.z + 0.03, g, h=0.9, post=0.4)


def stacks(g):
    base = P(F.x, F.y, F_APEX)
    piv = empty("Stacks", base, g)
    zcyl("StackPipe", 0.55, 0.0, STACK_H, MT["hull"], piv, segs=20, radius2=0.45)
    zcyl("StackFoot", 0.7, -0.05, 0.35, MT["trim"], piv, segs=20)
    for k, z in enumerate((1.4, 2.8, 4.2)):
        hoop(f"StackRing{k}", 0.55 - 0.1 * z / STACK_H + 0.03, z, 0.07, MT["trim"], piv, segs=20)
    zcyl("StackOwner", 0.5, 3.15, 3.5, MT["owner"], piv, segs=20, radius2=0.49)
    zcyl("StackSoot", 0.47, STACK_H - 0.7, STACK_H, MT["dark"], piv, segs=20, radius2=0.46)
    hoop("StackLip", 0.47, STACK_H, 0.08, MT["dark"], piv, segs=20)
    for k in range(3):
        a = math.radians(120 * k + 30)
        sphere(f"StackLamp{k}", 0.07, P(0.5 * math.cos(a), 0.5 * math.sin(a), STACK_H - 0.45), MT["beacon"], piv, 8)
    for side in (-1, 1):
        bx(f"StackLadder{side}", (0.035, 0.035, STACK_H - 0.6), P(-0.5 - 0.02 * side, side * 0.16, STACK_H / 2), MT["steel"], piv)
    s0 = empty("Smoke0", P(0, 0, STACK_H + 0.15), piv)
    s0.empty_display_size = 0.5
    # scrubber box and its secondary stack (fixed height)
    sc = P(-3.4, 4.75, 0)
    bx("Scrubber", (1.25, 1.15, 2.6), sc + P(0, 0, SLAB + 1.3), MT["panel"], g, 0.04)
    bx("ScrubberBand", (1.27, 1.17, 0.16), sc + P(0, 0, SLAB + 2.2), MT["owner"], g)
    for k in range(3):
        bx(f"ScrubberRib{k}", (1.27, 0.06, 2.4), sc + P(0, -0.35 + k * 0.35, SLAB + 1.3), MT["trim"], g)
    s2 = sc + P(-0.28, 0.25, 0)
    zcyl("Stack2", 0.3, SLAB + 2.6, 9.3, MT["hull"], g, xy=(s2.x, s2.y), segs=16, radius2=0.26)
    zcyl("Stack2Soot", 0.27, 8.7, 9.3, MT["dark"], g, xy=(s2.x, s2.y), segs=16)
    hoop("Stack2Ring", 0.29, 6.2, 0.06, MT["trim"], g, xy=(s2.x, s2.y), segs=16)
    sphere("Stack2Lamp", 0.06, s2 + P(0.28, 0, 8.9), MT["beacon"], g, 8)
    s1 = empty("Smoke1", s2 + P(0, 0, 9.45), g)
    s1.empty_display_size = 0.4


def control_booth(g):
    c = BOOTH.to_3d()
    w, d = 2.4, 1.6
    bx("BoothBase", (w + 0.1, d + 0.1, 0.25), c + P(0, 0, SLAB + 0.125), MT["trim"], g, 0.02)
    bx("BoothBody", (w, d, 1.15), c + P(0, 0, SLAB + 0.25 + 0.575), MT["hull"], g, 0.04)
    bx("BoothAccent", (w + 0.02, d + 0.02, 0.35), c + P(0, 0, SLAB + 0.5), MT["accent"], g)
    bx("BoothGlassBand", (w - 0.06, d - 0.06, 0.7), c + P(0, 0, SLAB + 1.75), MT["glass"], g, 0.02)
    for k in range(4):
        bx(f"BoothMullion{k}", (0.07, 0.07, 0.72), c + P((k - 1.5) * 0.78, -d / 2 + 0.03, SLAB + 1.75), MT["trim"], g)
    pane_row("BoothLit", (c.x - w / 2 + 0.15, c.y - d / 2 + 0.025, 0), (1, 0, 0), None, w - 0.3, SLAB + 1.5, SLAB + 1.95, 3, MT["window"], g, frac=0.82)
    bx("BoothRoof", (w + 0.35, d + 0.35, 0.14), c + P(0, 0, SLAB + 2.17), MT["panel"], g, 0.03)
    bx("BoothFascia", (w + 0.36, 0.05, 0.3), c + P(0, -d / 2 - 0.19, SLAB + 2.14), MT["dark"], g)
    marker("SignFront", c + P(0, -d / 2 - 0.22, SLAB + 2.14), (math.radians(90), 0, 0), 2.3, 0.2, g)
    bx("BoothOwner", (w + 0.02, d + 0.02, 0.12), c + P(0, 0, SLAB + 1.32), MT["owner"], g)
    bx("BoothDoor", (0.05, 0.75, 1.1), c + P(w / 2 + 0.01, 0.2, SLAB + 0.8), MT["trim"], g)
    bx("BoothDoorLamp", (0.06, 0.4, 0.06), c + P(w / 2 + 0.03, 0.2, SLAB + 1.42), MT["window"], g).visible_shadow = False
    bx("BoothStep", (0.45, 0.9, 0.2), c + P(w / 2 + 0.22, 0.2, SLAB + 0.1), MT["panel"], g, 0.02)


def roof_gear(g):
    c = BOOTH.to_3d()
    top = SLAB + 2.24
    bx("AcUnit", (0.9, 0.7, 0.4), c + P(-0.5, 0.2, top + 0.2), MT["panel"], g, 0.03)
    zcyl("AcFan", 0.25, top + 0.4, top + 0.43, MT["dark"], g, xy=(c.x - 0.5, c.y + 0.2), segs=14)
    zcyl("BoothMast", 0.05, top, top + 2.4, MT["steel"], g, xy=(c.x + 0.8, c.y + 0.4), segs=8)
    sphere("BoothMastLamp", 0.07, c + P(0.8, 0.4, top + 2.45), MT["beacon"], g, 8)
    r, dd, t = 0.38, 0.12, 0.025
    dish = lathe("BoothDish", [(0, 0), (r * 0.35, dd * 0.12), (r * 0.7, dd * 0.5), (r, dd), (r * 0.97, dd + t), (r * 0.68, dd * 0.5 + t),
                               (r * 0.34, dd * 0.12 + t), (0, t)], MT["hull"], g, (0, 0, 0), 20)
    dish.matrix_basis = Matrix.Translation(c + P(0.8, 0.4, top + 1.6)) @ Matrix.Rotation(math.radians(-30), 4, "Z") @ Matrix.Rotation(math.radians(60), 4, "X")
    strut("BoothDishArm", c + P(0.8, 0.4, top + 1.6), c + P(0.8, 0.4, top + 1.3), 0.03, MT["steel"], g, 6)
    # mushroom vents on the hall roof (between the fin rows' ends)
    x0, x1, y0, y1 = HALL
    for k, x in enumerate((x0 + 0.5, x1 - 0.5)):
        zcyl(f"Vent{k}", 0.16, HALL_Z, HALL_Z + 0.55, MT["trim"], g, xy=(x, y1 - 0.18), segs=10)
        zcyl(f"VentCap{k}", 0.27, HALL_Z + 0.55, HALL_Z + 0.65, MT["panel"], g, xy=(x, y1 - 0.18), segs=12)
    # crusher-side dust collector on the slab behind the deck
    dc = (5.75, 0.45)
    zcyl("Dust", 0.42, SLAB + 0.6, SLAB + 2.0, MT["panel"], g, xy=dc, segs=16)
    zcyl("DustCap", 0.45, SLAB + 2.0, SLAB + 2.1, MT["trim"], g, xy=dc, segs=16)
    cylinder("DustCone", 0.12, 0.3, P(dc[0], dc[1], SLAB + 0.45), MT["panel"], g, axis="Z", segments=16, radius2=0.42)
    for j in range(3):
        a = math.radians(120 * j)
        bx(f"DustLeg{j}", (0.06, 0.06, 0.65), P(dc[0] + 0.36 * math.cos(a), dc[1] + 0.36 * math.sin(a), SLAB + 0.32), MT["trim"], g)
    pipe("DustPipe", [P(5.4, 0.25, 1.9), P(5.45, -1.0, 1.9), P(4.9, -1.4, 1.75)], 0.07, MT["steel"], g)


def lights(g):
    for k, (x, y, yaw) in enumerate(((-6.0, 5.1, -40), (-6.05, -4.95, 40))):
        zcyl(f"LightMast{k}", 0.12, SLAB, SLAB + 6.4, MT["hull"], g, xy=(x, y), segs=10, radius2=0.08)
        zcyl(f"LightMastFoot{k}", 0.2, SLAB, SLAB + 0.5, MT["hazard"], g, xy=(x, y), segs=10)
        zcyl(f"LightMastBand{k}", 0.205, SLAB + 0.2, SLAB + 0.32, MT["dark"], g, xy=(x, y), segs=10)
        a = math.radians(yaw)
        d = Vector((math.cos(a), math.sin(a), 0))
        s = Vector((-d.y, d.x, 0))
        top = P(x, y, SLAB + 6.4)
        obox(f"LightArm{k}", (0.08, 1.2, 0.08), top, s, MT["trim"], g)
        for j in (-1, 1):
            p = top + s * (j * 0.5) + d * 0.1
            bx(f"LightHead{k}{j}", (0.42, 0.26, 0.2), p, MT["dark"], g, 0.02, rot=(math.radians(-30), 0, a - math.pi / 2))
            bx(f"LightLens{k}{j}", (0.36, 0.03, 0.15), p + d * 0.13 - P(0, 0, 0.05), MT["lamp"], g, rot=(math.radians(-30), 0, a - math.pi / 2)).visible_shadow = False
        sphere(f"LightBeacon{k}", 0.06, top + P(0, 0, 0.12), MT["beacon"], g, 8)


BUILDERS = [foundation, furnace, crusher, receiving_bay, conveyor, casting_hall, ingots, silos, cooling_towers, pipes,
            catwalks, stacks, control_booth, roof_gear, lights]


def refinery():
    root = empty("refinery", (0, 0, 0))
    for k, (piece, build) in enumerate(zip(PIECES, BUILDERS)):
        g = empty(f"Build{k}", (0, 0, 0), root)
        g["piece"] = piece
        build(g)
    return root


# ---------------------------------------------------------------- game export: join per piece

PIVOTS = {"Crusher", "Fan0", "Fan1", "Fan2", "OreFlow", "Stacks", "Silos", "Towers"}
SEPARATE = {"Belt"} | {f"Ore{k}" for k in range(8)}   # meshes kept as they are
EMPTIES = {"PodPad", "FurnaceLight", "SparkPoint", "Smoke0", "Smoke1", "OreEnd", "SignFront", "SignHall", "LabelSilo0",
           "LabelSilo1", "LabelPad"}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}


def join_pieces(root):
    """build_outpost_l1.join_for_game: modifiers applied, every mesh joined into "<group>Body" under its nearest kept
    ancestor (a Build group or an animated/scaled pivot), one material slot per material."""
    meshes = [o for o in root.children_recursive if o.type == "MESH" and not o.hide_render]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    applied = {o: bpy.data.meshes.new_from_object(o.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
               for o in meshes if o.modifiers}
    for o, me in applied.items():
        o.modifiers.clear()
        o.data = me

    def group_of(o):
        p = o.parent
        while p is not None and p is not root and p.name not in KEEP:
            p = p.parent
        return p or root

    groups = {}
    for o in meshes:
        if o.name in SEPARATE:
            continue
        groups.setdefault(group_of(o), []).append(o)
    for o in meshes:
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        o.data.uv_layers[0].name = "UVMap"
        while len(o.data.uv_layers) > 1:
            o.data.uv_layers.remove(o.data.uv_layers[1])
    for g, objs in groups.items():
        for o in objs:
            mw = o.matrix_world.copy()
            o.parent = g
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.matrix_world = mw
        bpy.context.view_layer.update()
        target = objs[0]
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = target
        if len(objs) > 1:
            bpy.ops.object.join()
        target.name = f"{g.name}Body"
        target.data.name = target.name
        target.data.transform(target.matrix_basis)
        target.matrix_basis = Matrix.Identity(4)
    while True:
        bare = [o for o in root.children_recursive if o.type == "EMPTY" and o.name not in KEEP and not o.children]
        if not bare:
            break
        for o in bare:
            bpy.data.objects.remove(o)
    names = sorted(o.name for o in root.children_recursive)
    missing = sorted(KEEP - set(names))
    if missing:
        sys.exit(f"join_pieces lost {missing}")
    print(f"joined for the game: {len(names) + 1} nodes:", ", ".join(names))


# ---------------------------------------------------------------- grime bake (one shared atlas)


def grime_material(image):
    """EMIT bake shader: white = clean, warm grey-brown where dirt rises from the ground, in crevices (AO), down rain
    streaks, mottled, and soot toward the stack tips."""
    m = bpy.data.materials.new("GrimeBake")
    m.use_nodes = True
    nt = m.node_tree
    N = nt.nodes.new
    L = nt.links.new
    geo = N("ShaderNodeNewGeometry")
    sep = N("ShaderNodeSeparateXYZ")
    L(geo.outputs["Position"], sep.inputs[0])

    def mrange(src, a, b, c=0.0, d=1.0):
        r = N("ShaderNodeMapRange")
        r.inputs["From Min"].default_value = a
        r.inputs["From Max"].default_value = b
        r.inputs["To Min"].default_value = c
        r.inputs["To Max"].default_value = d
        L(src, r.inputs["Value"])
        return r.outputs["Result"]

    def noise(vec, scale, detail=6.0):
        n = N("ShaderNodeTexNoise")
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        L(vec, n.inputs["Vector"])
        return n.outputs["Fac"]

    def math_(op, a, b):
        n = N("ShaderNodeMath")
        n.operation = op
        for i, v in enumerate((a, b)):
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L(v, n.inputs[i])
        return n.outputs[0]

    pos = geo.outputs["Position"]
    ground = mrange(sep.outputs["Z"], SLAB + 0.05, 1.9, 1.0, 0.0)
    dirt = math_("MULTIPLY", ground, mrange(noise(pos, 1.4), 0.3, 0.7, 0.35, 1.0))
    ao = N("ShaderNodeAmbientOcclusion")
    ao.samples = 16
    ao.inputs["Distance"].default_value = 0.8
    occ = mrange(ao.outputs["AO"], 0.2, 1.0, 1.0, 0.0)
    stretch = N("ShaderNodeMapping")
    stretch.inputs["Scale"].default_value = (7.0, 7.0, 0.35)
    L(pos, stretch.inputs["Vector"])
    streak = mrange(noise(stretch.outputs["Vector"], 2.2, 3.0), 0.56, 0.76, 0.0, 1.0)
    mott = mrange(noise(pos, 0.45, 4.0), 0.35, 0.65, 0.0, 1.0)
    soot = mrange(sep.outputs["Z"], 8.0, 11.3, 0.0, 1.0)
    amount = math_("ADD", math_("MULTIPLY", dirt, 0.85), math_("MULTIPLY", occ, 0.65))
    amount = math_("ADD", amount, math_("MULTIPLY", streak, 0.35))
    amount = math_("ADD", amount, math_("MULTIPLY", mott, 0.16))
    amount = math_("ADD", amount, math_("MULTIPLY", soot, 0.7))
    amount = math_("MINIMUM", amount, 0.85)
    mix = N("ShaderNodeMix")
    mix.data_type = "RGBA"
    L(amount, mix.inputs["Factor"])
    mix.inputs[6].default_value = (1.0, 1.0, 1.0, 1.0)
    mix.inputs[7].default_value = (0.4, 0.33, 0.25, 1.0)
    emit = N("ShaderNodeEmission")
    L(mix.outputs[2], emit.inputs["Color"])
    L(emit.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    tex = N("ShaderNodeTexImage")
    tex.image = image
    nt.nodes.active = tex
    return m


def bake_grime(root):
    """Unwrap every joined piece into one shared atlas (multi-object Smart UV Project), bake the grime into it, then
    give every textured material Base Color = atlas x its own colour (exported as baseColorTexture x factor)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 8
    if THREADS:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = THREADS
    targets = [o for o in root.children_recursive if o.type == "MESH" and o.name not in SEPARATE]
    bpy.ops.object.select_all(action="DESELECT")
    for o in targets:
        o.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0015, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.0015, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    image = bpy.data.images.new("RefineryGrime", BAKE_RES, BAKE_RES)
    bake = grime_material(image)
    saved = {}
    for o in targets:
        saved[o] = [s.material for s in o.material_slots]
        for s in o.material_slots:
            s.material = bake
    scene.cycles.bake_type = "EMIT"
    bpy.ops.object.bake(type="EMIT", margin=3, use_clear=True)
    for o, mats in saved.items():
        for s, m in zip(o.material_slots, mats):
            s.material = m
    image.pack()
    for m in TEXTURED:
        nt = m.node_tree
        p = nt.nodes["Principled BSDF"]
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(tex.outputs["Color"], mix.inputs[6])
        mix.inputs[7].default_value = (*m["colour"], 1.0)
        nt.links.new(mix.outputs[2], p.inputs["Base Color"])
    bpy.data.materials.remove(bake)
    print("baked grime atlas", BAKE_RES, "for", len(targets), "meshes")


def export(root, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        if not o.hide_render:
            o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_lights=False, export_cameras=False, export_image_format="JPEG",
                              export_jpeg_quality=88, export_image_quality=88)
    tris = sum(len(p.vertices) - 2 for o in root.children_recursive if o.type == "MESH" for p in o.data.polygons)
    print("exported", path, os.path.getsize(path), "bytes,", tris, "triangles")


# ---------------------------------------------------------------- previews

SIGNS = {"SignFront": ("REFINERY 01", (0.92, 0.93, 0.95)), "SignHall": ("CASTING", (0.92, 0.93, 0.95)),
         "LabelSilo0": ("DURANIUM", (0.08, 0.08, 0.1)), "LabelSilo1": ("TRITANIUM", (0.08, 0.08, 0.1)),
         "LabelPad": ("PAD 1", (0.85, 0.62, 0.08))}


def preview_decals():
    """What the game's Label3Ds will paint on the markers (preview only, built after the export)."""
    for name, (body, ink) in SIGNS.items():
        e = bpy.data.objects.get(name)
        if e is None:
            continue
        mw = e.matrix_world.copy()
        loc, rot, scale = mw.decompose()
        m = BV["material"](f"Ink{name}", ink, 0.0, 0.6)
        t = BV["text"](f"Text{name}", body, scale.y, (0, 0, 0), (0, 0, 0), m, None, max_width=scale.x)
        t.matrix_world = Matrix.Translation(loc) @ rot.to_matrix().to_4x4() @ Matrix.Translation((0, 0, 0.006))


def cargo_pod():
    """BuildingModels.CargoPod at the pad (Godot capsule r 0.55 x 2.2 along Z, lit band)."""
    pad = bpy.data.objects["PodPad"].matrix_world.translation
    hull = BV["material"]("PodHull", (0.88, 0.89, 0.92), 0.6, 0.3)
    band = BV["material"]("PodBand", (0.72, 0.08, 0.06), 0.0, 0.4, emission=(0.72, 0.08, 0.06), strength=2.0)
    holder = empty("PreviewPod", pad + P(0, 0, 0.6))
    cylinder("PodBody", 0.55, 1.1, (0, 0, 0), hull, holder, axis="Y", segments=16)
    for s in (-1, 1):
        sphere(f"PodCap{s}", 0.55, (0, s * 0.55, 0), hull, holder, 16)
    cylinder("PodBand", 0.58, 0.25, (0, 0, 0), band, holder, axis="Y", segments=16)
    holder.rotation_euler.z = math.radians(30)
    return holder


def preview(root, folder):
    scene = bpy.context.scene
    # the rover's palette reuses names like "Hull": rename ours first so it builds its own
    for m in list(MT.values()):
        m.name = m.name + "_R"
    OP["materials"]()
    OP["stage"]()
    preview_decals()
    pod = cargo_pod()
    rover = BV["rover_mk1"](BV["palette"]())
    rover.location = (9.2, -5.2, 0.0)
    rover.rotation_euler.z = math.radians(205)
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    if THREADS:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = THREADS
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    cam.data.clip_end = 2000
    os.makedirs(folder, exist_ok=True)
    sun = bpy.data.objects["Sun"]
    world = scene.world
    bg = world.node_tree.nodes["Background"]
    night_lights = []

    def shoot(name, pos, target, lens=None, angle_y=None, ortho=None, w=RES, h=None):
        scene.render.resolution_x = w
        scene.render.resolution_y = h or int(w * 9 / 16)
        if ortho:
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = ortho
        else:
            cam.data.type = "PERSP"
            if lens:
                cam.data.sensor_fit = "AUTO"
                cam.data.lens = lens
            if angle_y:
                cam.data.sensor_fit = "VERTICAL"
                cam.data.angle_y = math.radians(angle_y)
        cam.location = pos
        cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        path = os.path.join(folder, f"refinery-{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path, flush=True)

    def game_cam(dist, target=(0, 0, 0.5)):
        x = min(1.0, max(0.0, (dist - 15) / (160 - 15)))
        t = x * x * (3 - 2 * x)
        d = Vector((0, 0.34, 0.94)).lerp(Vector((0, 0.82, 0.57)), t).normalized()
        return Vector(target) + Vector((0, -d.z, d.y)) * dist, target

    def night(on):
        sun.hide_render = on
        bg.inputs["Strength"].default_value = 0.004 if on else 0.12
        scene.view_settings.exposure = 0.0 if on else -0.6
        if on and not night_lights:
            for name, colour, energy, loc in (("NFurnace", (1.0, 0.5, 0.2), 900, bpy.data.objects["FurnaceLight"].matrix_world.translation),
                                              ("NPad", (0.4, 0.6, 1.0), 120, D.to_3d() + P(0, 0, DZ + 1.5)),
                                              ("NMast0", (1.0, 0.95, 0.85), 260, P(-5.3, 4.3, 6.4)),
                                              ("NMast1", (1.0, 0.95, 0.85), 260, P(-5.4, -4.3, 6.4)),
                                              ("NHall", (1.0, 0.7, 0.4), 150, P(-3.95, HALL[2] - 1.0, 1.2))):
                lt = bpy.data.objects.new(name, bpy.data.lights.new(name, "POINT"))
                lt.data.energy = energy
                lt.data.color = colour
                lt.data.shadow_soft_size = 0.3
                lt.location = loc
                link(lt)
                night_lights.append(lt)
        for lt in night_lights:
            lt.hide_render = not on

    for name in SHOTS:
        night(name == "night")
        if name == "hero":
            shoot(name, (17.0, -21.0, 12.5), (0.6, 0.4, 3.0), lens=32, w=RES, h=int(RES * 2 / 3))
        elif name == "game":
            pos, target = game_cam(50.0)
            shoot(name, pos, target, angle_y=45)
        elif name == "close":
            shoot(name, (10.5, 2.0, 6.5), (2.4, 0.2, 2.0), lens=30, w=RES, h=int(RES * 2 / 3))
            shoot("close2", (-4.0, -11.0, 4.6), (-2.2, -0.8, 1.8), lens=30, w=RES, h=int(RES * 2 / 3))
        elif name == "night":
            shoot(name, (-9.0, -22.0, 11.5), (-0.5, 0.0, 3.0), lens=32, w=RES, h=int(RES * 2 / 3))
        elif name == "top":
            shoot(name, (0, -0.01, 80), (0, 0, 0), ortho=30, w=RES, h=RES)
        elif name == "back":
            shoot(name, (-15.0, 19.0, 11.0), (0.0, 0.5, 3.0), lens=32, w=RES, h=int(RES * 2 / 3))
        elif name == "pieces":
            # the build order: 4 frames showing pieces 0-3, 0-7, 0-11, all
            groups = [bpy.data.objects[f"Build{k}"] for k in range(len(PIECES))]
            for upto in (3, 7, 11, len(PIECES)):
                for k, g in enumerate(groups):
                    for o in [g] + list(g.children_recursive):
                        o.hide_render = k >= upto
                pod.hide_render = upto < len(PIECES)
                for o in pod.children:
                    o.hide_render = pod.hide_render
                shoot(f"pieces{upto:02d}", (17.0, -21.0, 12.5), (0.6, 0.4, 3.0), lens=32, w=RES // 2, h=int(RES / 3))
            for g in groups:
                for o in [g] + list(g.children_recursive):
                    o.hide_render = False


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    root = refinery()
    bpy.context.view_layer.update()
    if JOIN:
        join_pieces(root)
    if BAKE:
        bake_grime(root)
    if OUT:
        export(root, OUT)
    if PREVIEW:
        preview(root, PREVIEW)


main()
