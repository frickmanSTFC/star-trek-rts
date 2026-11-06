"""Shared kit for the game-ready building models (mine, storage container, fusion reactor, deep core mine), lifted
from build_refinery.py (which stays self-contained) so every building follows the same conventions:

  * Blender +Z up, the building's front toward -Y (= Godot +Z), 1 unit = 1 world unit, origin at the tile centre.
  * Material "Owner" is recoloured per player; glow materials keep their names so the game can drive them.
  * Build pieces: child empties Build0..BuildN of the root, in build order (the exocomps print them bottom-up); every
    static mesh in a piece is joined into "Build<k>Body", animated/scaled pivots keep their own "<pivot>Body".
  * Textured materials share ONE baked grime atlas (Cycles EMIT bake of dirt from the ground + AO + streaks + soot),
    exported as glTF baseColorTexture x baseColorFactor, stored as JPEG, so the game can still tint them.
  * Decal markers: empties with Blender scale (width, height, 1), text along local +X facing local +Z (the game's
    PaintMarker reads them as Godot scale (width, 1, height), text normal local +Y).

A building script does:
    import building_kit as K
    K.setup("mine")                       # flags, output path
    K.materials()                         # the standard palette (+ K.mat(...) for its own)
    root = K.assemble("mine", PIECES, BUILDERS)
    K.finish(root, keep=..., separate=..., signs=..., preview=...)
"""
import math
import os
import re
import sys

import bpy  # noqa: E402  first bpy, then bmesh/mathutils
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


ALL_SHOTS = ["hero", "game", "close", "night", "top", "back", "pieces"]
CFG = {}


def setup(building_id):
    CFG.update(
        id=building_id,
        game="--game" in argv,
        preview=arg("--preview"),
        shots=[s.strip() for s in arg("--shots", "hero,game,close,night,top,back,pieces").split(",") if s.strip()],
        res=int(arg("--res", "960")),
        samples=int(arg("--samples", "24")),
        threads=int(arg("--threads", "0")),
        join="--no-join" not in argv,
        bake="--no-bake" not in argv,
        bake_res=int(arg("--bake-res", "1024")),
    )
    CFG["out"] = arg("--out") or (os.path.join(REPO, "game", "models", "buildings", f"{building_id}.glb") if CFG["game"] else None)
    if set(CFG["shots"]) - set(ALL_SHOTS):
        sys.exit(f"--shots takes {', '.join(ALL_SHOTS)}")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return CFG


def load(script, extra_argv=()):
    """Exec another builder without its trailing main() and return its namespace."""
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


OP = load("build_outpost.py", ["--level", "1"])
BV = OP["BV"]
box, cylinder, empty, mesh_object, strut, link = BV["box"], BV["cylinder"], BV["empty"], BV["mesh_object"], BV["strut"], BV["link"]
zcyl, sphere, lathe, flat_path, pane_row, window_ring = OP["zcyl"], OP["sphere"], OP["lathe"], OP["flat_path"], OP["pane_row"], OP["window_ring"]
torus = OP["torus"]

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
    """The refinery's palette (same names, so BuildingModels.Variety tints Hull/Panel/Accent the same way)."""
    mat("hull", "Hull", (0.76, 0.77, 0.79), 0.15, 0.42, coat=0.2, textured=True)
    mat("panel", "Panel", (0.56, 0.58, 0.62), 0.2, 0.5, textured=True)
    mat("accent", "Accent", (0.14, 0.3, 0.55), 0.3, 0.45, textured=True)
    mat("trim", "Trim", (0.26, 0.27, 0.3), 0.4, 0.5, textured=True)
    mat("dark", "Dark", (0.08, 0.085, 0.095), 0.4, 0.6, textured=True)
    mat("steel", "Steel", (0.55, 0.57, 0.6), 0.85, 0.32, textured=True)
    mat("concrete", "Concrete", (0.4, 0.39, 0.37), 0.0, 0.85, textured=True)
    mat("copper", "Copper", (0.78, 0.45, 0.26), 0.9, 0.3, textured=True)
    mat("hazard", "Hazard", (0.85, 0.62, 0.08), 0.0, 0.55, textured=True)
    mat("rail", "Rail", (0.86, 0.66, 0.12), 0.2, 0.5, textured=True, cull=False)
    mat("valve", "Valve", (0.7, 0.1, 0.08), 0.3, 0.5, textured=True)
    mat("grate", "Grate", (0.17, 0.18, 0.2), 0.7, 0.55, cull=False)
    mat("owner", "Owner", (0.72, 0.08, 0.06), 0.1, 0.4)
    mat("window", "Window", (1.0, 0.82, 0.55), 0.0, 0.3, emission=(1.0, 0.78, 0.45), strength=3.0)
    mat("interior", "Interior", (1.0, 0.6, 0.3), 0.0, 0.4, emission=(1.0, 0.55, 0.25), strength=2.0)
    mat("glass", "Glass", (0.03, 0.06, 0.09), 0.3, 0.06, emission=(0.1, 0.25, 0.4), strength=0.25, coat=1.0)
    mat("lamp", "Lamp", (1.0, 0.95, 0.85), 0.0, 0.3, emission=(1.0, 0.95, 0.85), strength=6.0)
    mat("beacon", "Beacon", (1.0, 0.1, 0.06), 0.0, 0.3, emission=(1.0, 0.08, 0.05), strength=8.0)
    mat("ore", "Ore", (0.33, 0.29, 0.26), 0.1, 0.9)
    mat("pallet", "Pallet", (0.42, 0.34, 0.24), 0.0, 0.85)
    mat("cable", "Cable", (0.05, 0.05, 0.055), 0.6, 0.5)
    mat("rubber", "Rubber", (0.05, 0.05, 0.055), 0.0, 0.85)


def belt_material():
    belt = mat("belt", "Belt", (1, 1, 1), 0.0, 0.85)
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
    tex = belt.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    belt.node_tree.links.new(tex.outputs["Color"], belt.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    return belt


# ---------------------------------------------------------------- geometry helpers


def P(x, y, z=0.0):
    return Vector((x, y, z))


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


def beam(name, a, b, w, material, parent, h=None):
    """A square bar from a to b (w wide, h deep)."""
    a, b = Vector(a), Vector(b)
    up = Vector((0, 0, 1)) if abs((b - a).normalized().z) < 0.95 else Vector((1, 0, 0))
    return obox(name, (w, (b - a).length, h or w), (a + b) / 2, b - a, material, parent, up=up)


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


def wall_stripes(name, a, b, z0, z1, parent, pitch=0.4, out=0.0):
    """Vertical-face hazard stripes on a wall from a to b (2D, the wall's outward side to the right of a->b)."""
    a3, b3 = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = b3 - a3
    L = d.length
    u = d / L
    nrm = Vector((u.y, -u.x, 0)) * out
    h = z1 - z0
    yellow, black = [], []
    t, k = -h, 0
    while t < L:
        t1 = t + pitch / 2
        c = lambda x: max(0.0, min(L, x))  # noqa: E731
        q = (a3 + u * c(t) + nrm + P(0, 0, z0), a3 + u * c(t1) + nrm + P(0, 0, z0),
             a3 + u * c(t1 + h) + nrm + P(0, 0, z1), a3 + u * c(t + h) + nrm + P(0, 0, z1))
        (yellow if k % 2 == 0 else black).append(q)
        t, k = t1, k + 1
    faces_obj(f"{name}Y", yellow, MT["hazard"], parent)
    faces_obj(f"{name}K", black, MT["dark"], parent)


def ring_mesh(name, r0, r1, z0, z1, material, parent, xy=(0, 0), segs=32):
    """An annulus (washer) between radii r0 < r1, z0..z1."""
    return lathe(name, [(r0, z0), (r1, z0), (r1, z1), (r0, z1), (r0, z0)], material, parent, (xy[0], xy[1], 0), segs, smooth=False)


def band(name, r, z0, z1, material, parent, xy=(0, 0), segs=32):
    """An open painted band round a cylinder (no caps)."""
    bm = bmesh.new()
    lo = [bm.verts.new((xy[0] + r * math.cos(2 * math.pi * k / segs), xy[1] + r * math.sin(2 * math.pi * k / segs), z0)) for k in range(segs)]
    hi = [bm.verts.new((xy[0] + r * math.cos(2 * math.pi * k / segs), xy[1] + r * math.sin(2 * math.pi * k / segs), z1)) for k in range(segs)]
    for k in range(segs):
        j = (k + 1) % segs
        bm.faces.new((lo[k], lo[j], hi[j], hi[k]))
    return mesh_object(name, bm, material, parent, smooth=True)


def hoop(name, r, z, thick, material, parent, xy=(0, 0), segs=32):
    """A thin round rail as a square-section lathe (cheaper than a torus)."""
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
    """A (caged) ladder at `base` (x, y, z) climbing to top_z; `facing` = 2D direction the cage bulges toward."""
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


def sagging(name, a, b, sag, radius, material, parent, n=6, segs=5):
    """A cable hanging from a to b (a parabola `sag` below the chord at the middle)."""
    a, b = Vector(a), Vector(b)
    pts = [a.lerp(b, i / n) - Vector((0, 0, 4 * sag * (i / n) * (1 - i / n))) for i in range(n + 1)]
    pipe(name, pts, radius, material, parent, segs, joints=False)


def truss(name, a, b, width, chord, material, parent, bays=None, up=Vector((0, 0, 1)), web=None):
    """A square lattice girder from a to b: four chords (`width` apart) and diagonal webs in every bay."""
    a, b = Vector(a), Vector(b)
    u = (b - a).normalized()
    R = frame(u, up if abs(u.dot(up)) < 0.95 else Vector((1, 0, 0)))
    s, n = R.col[0].to_3d(), R.col[2].to_3d()
    L = (b - a).length
    bays = bays or max(1, round(L / (width * 1.1)))
    h = width / 2
    corners = [s * h + n * h, -s * h + n * h, -s * h - n * h, s * h - n * h]
    for k, c in enumerate(corners):
        beam(f"{name}Chord{k}", a + c, b + c, chord, material, parent)
    web = web or chord * 0.6
    for i in range(bays):
        p0, p1 = a + u * (L * i / bays), a + u * (L * (i + 1) / bays)
        for k in range(4):
            c0, c1 = corners[k], corners[(k + 1) % 4]
            if i % 2:
                c0, c1 = c1, c0
            strut(f"{name}Web{i}_{k}", p0 + c0, p1 + c1, web / 2, material, parent, 4)


def rock(name, radius, loc, parent, seed, material=None):
    """A small lump of ore: a jittered icosphere."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=radius)
    for i, v in enumerate(bm.verts):
        k = math.sin(seed * 12.9898 + i * 78.233) * 43758.5453
        j = 0.72 + 0.5 * (k - math.floor(k))
        v.co *= j
        v.co.z *= 0.7
    return mesh_object(name, bm, material or MT["ore"], parent, loc)


def heap(name, radius, height, loc, material, parent, seed=1, segs=14):
    """A rough conical pile (ore heap, spoil): a lathed cone with jittered rims."""
    bm = bmesh.new()
    rings = []
    for i, (r, z) in enumerate(((radius, 0.0), (radius * 0.72, height * 0.42), (radius * 0.38, height * 0.82), (0.0, height))):
        if r == 0:
            rings.append([bm.verts.new((0, 0, z))])
            continue
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            j = math.sin(seed * 7.1 + k * 3.7 + i * 1.3) * 43758.5453
            j = 0.85 + 0.3 * (j - math.floor(j)) if i else 1.0
            ring.append(bm.verts.new((r * j * math.cos(a), r * j * math.sin(a), z * (1 if i == 0 else 0.92 + 0.16 * (j - 0.85) / 0.3))))
        rings.append(ring)
    for ra, rb in zip(rings, rings[1:]):
        for k in range(segs):
            j = (k + 1) % segs
            if len(rb) == 1:
                bm.faces.new((ra[k], ra[j], rb[0]))
            else:
                bm.faces.new((ra[k], ra[j], rb[j], rb[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm, material, parent, loc)


def hex_prism(name, r, z0, z1, material, parent, xy=(0, 0), rot=0.0):
    """A hexagonal prism (flat-topped hex in plan, corner radius r)."""
    return lathe(name, [(0.0, z0), (r, z0), (r, z1), (0.0, z1)], material, parent, (xy[0], xy[1], 0), 6, smooth=False)


def marker(name, loc, rot, width, height, parent):
    """A decal marker (build_vehicles.text --blank convention): text along local +X facing local +Z, scale (w, h, 1).
    rot = (x, 0, z) radians: Rz @ Rx; a sign on a wall facing -Y is (90 deg, 0, 0)."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.4
    e.matrix_basis = Matrix.Translation(Vector(loc)) @ Matrix.Rotation(rot[2], 4, "Z") @ Matrix.Rotation(rot[0], 4, "X") @ Matrix.Diagonal((width, height, 1.0, 1.0))
    return link(e, parent)


def point(name, loc, parent, size=0.3):
    e = empty(name, loc, parent)
    e.empty_display_size = size
    return e


def light_mast(name, x, y, z0, h, yaw_deg, parent, heads=2):
    """A tall floodlight mast: tapered pole, hazard foot, a cross arm with lamp heads aimed outward-down, a beacon."""
    zcyl(f"{name}Pole", 0.12, z0, z0 + h, MT["hull"], parent, xy=(x, y), segs=10, radius2=0.08)
    zcyl(f"{name}Foot", 0.2, z0, z0 + 0.5, MT["hazard"], parent, xy=(x, y), segs=10)
    zcyl(f"{name}FootBand", 0.205, z0 + 0.2, z0 + 0.32, MT["dark"], parent, xy=(x, y), segs=10)
    a = math.radians(yaw_deg)
    d = Vector((math.cos(a), math.sin(a), 0))
    s = Vector((-d.y, d.x, 0))
    top = P(x, y, z0 + h)
    obox(f"{name}Arm", (0.08, 0.5 * heads + 0.2, 0.08), top, s, MT["trim"], parent)
    for j in range(heads):
        off = (j - (heads - 1) / 2) * 0.5
        p = top + s * off + d * 0.1
        bx(f"{name}Head{j}", (0.42, 0.26, 0.2), p, MT["dark"], parent, 0.02, rot=(math.radians(-30), 0, a - math.pi / 2))
        bx(f"{name}Lens{j}", (0.36, 0.03, 0.15), p + d * 0.13 - P(0, 0, 0.05), MT["lamp"], parent, rot=(math.radians(-30), 0, a - math.pi / 2)).visible_shadow = False
    sphere(f"{name}Beacon", 0.06, top + P(0, 0, 0.12), MT["beacon"], parent, 8)


def bollards(name, pts, z, parent):
    for k, (x, y) in enumerate(pts):
        zcyl(f"{name}{k}", 0.11, z, z + 0.8, MT["hazard"], parent, xy=(x, y), segs=10)
        zcyl(f"{name}Band{k}", 0.113, z + 0.45, z + 0.6, MT["dark"], parent, xy=(x, y), segs=10)
        zcyl(f"{name}Cap{k}", 0.08, z + 0.8, z + 0.86, MT["lamp"], parent, xy=(x, y), segs=10)


def assemble(root_name, pieces, builders):
    root = empty(root_name, (0, 0, 0))
    for k, (piece, build) in enumerate(zip(pieces, builders)):
        g = empty(f"Build{k}", (0, 0, 0), root)
        g["piece"] = piece
        build(g)
    bpy.context.view_layer.update()
    return root


# ---------------------------------------------------------------- game export: join per piece


def join_pieces(root, keep, separate):
    """Modifiers applied, every mesh joined into "<group>Body" under its nearest kept ancestor (a Build group or an
    animated/scaled pivot), one material slot per material. `separate` meshes are left alone (own nodes)."""
    meshes = [o for o in root.children_recursive if o.type == "MESH" and not o.hide_render]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    applied = {o: bpy.data.meshes.new_from_object(o.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
               for o in meshes if o.modifiers}
    for o, me in applied.items():
        o.modifiers.clear()
        o.data = me

    def group_of(o):
        p = o.parent
        while p is not None and p is not root and p.name not in keep:
            p = p.parent
        return p or root

    groups = {}
    for o in meshes:
        if o.name in separate:
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
    # separate meshes: bake their own transform into the mesh? No: keep them (they may be animated by name).
    while True:
        bare = [o for o in root.children_recursive if o.type == "EMPTY" and o.name not in keep and not o.children]
        if not bare:
            break
        for o in bare:
            bpy.data.objects.remove(o)
    names = sorted(o.name for o in root.children_recursive)
    missing = sorted(set(keep) - set(names))
    if missing:
        sys.exit(f"join_pieces lost {missing}")
    print(f"joined for the game: {len(names) + 1} nodes:", ", ".join(names))


# ---------------------------------------------------------------- grime bake (one shared atlas)


def grime_material(image, ground_z, soot):
    """EMIT bake shader: white = clean, warm grey-brown where dirt rises from the ground, in crevices (AO), down rain
    streaks, mottled, and soot in the z range `soot` (None = none)."""
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
    ground = mrange(sep.outputs["Z"], ground_z + 0.05, ground_z + 1.6, 1.0, 0.0)
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
    amount = math_("ADD", math_("MULTIPLY", dirt, 0.85), math_("MULTIPLY", occ, 0.65))
    amount = math_("ADD", amount, math_("MULTIPLY", streak, 0.35))
    amount = math_("ADD", amount, math_("MULTIPLY", mott, 0.16))
    if soot:
        amount = math_("ADD", amount, math_("MULTIPLY", mrange(sep.outputs["Z"], soot[0], soot[1], 0.0, 1.0), 0.7))
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


def bake_grime(root, separate, image_name, ground_z=0.3, soot=None):
    """Unwrap every joined piece into one shared atlas, bake the grime into it, then give every textured material
    Base Color = atlas x its own colour (exported as baseColorTexture x factor)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 8
    if CFG["threads"]:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = CFG["threads"]
    targets = [o for o in root.children_recursive if o.type == "MESH" and o.name not in separate]
    bpy.ops.object.select_all(action="DESELECT")
    for o in targets:
        o.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0015, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.0015, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    image = bpy.data.images.new(image_name, CFG["bake_res"], CFG["bake_res"])
    bake = grime_material(image, ground_z, soot)
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
    print("baked grime atlas", CFG["bake_res"], "for", len(targets), "meshes")


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


def tri_count(root):
    return sum(len(p.vertices) - 2 for o in root.children_recursive if o.type == "MESH" for p in o.data.polygons)


# ---------------------------------------------------------------- previews


def preview_decals(signs):
    """What the game's Label3Ds will paint on the markers (preview only, built after the export)."""
    for name, (body, ink) in signs.items():
        e = bpy.data.objects.get(name)
        if e is None:
            continue
        loc, rot, scale = e.matrix_world.decompose()
        m = BV["material"](f"Ink{name}", ink, 0.0, 0.6)
        t = BV["text"](f"Text{name}", body, scale.y, (0, 0, 0), (0, 0, 0), m, None, max_width=scale.x)
        t.matrix_world = Matrix.Translation(loc) @ rot.to_matrix().to_4x4() @ Matrix.Translation((0, 0, 0.006))


def game_cam(dist, target=(0, 0, 0.5)):
    """PlanetDriver's camera: below TiltFrom 160 the direction eases from (0, 0.82, 0.57) to (0, 0.34, 0.94)."""
    x = min(1.0, max(0.0, (dist - 15) / (160 - 15)))
    t = x * x * (3 - 2 * x)
    d = Vector((0, 0.34, 0.94)).lerp(Vector((0, 0.82, 0.57)), t).normalized()
    return Vector(target) + Vector((0, -d.z, d.y)) * dist, target


def preview(root, shots, night_lights=(), signs=None, rover_at=None, extras=None, pieces_cam=None, on_shot=None):
    """Render `shots` = {name: (pos, target, lens)} (lens None + "ortho" key for the plan) plus "game" and "pieces".
    night_lights = [(name, colour, energy, loc)] shown only in the night shot; extras() builds preview-only props;
    on_shot(name) lets the building pose its animated parts per shot."""
    folder = CFG["preview"]
    prefix = CFG["id"]
    scene = bpy.context.scene
    for m in list(MT.values()):
        m.name = m.name + "_B"   # the outpost/rover palettes reuse names like "Hull"
    OP["materials"]()
    OP["stage"]()
    if signs:
        preview_decals(signs)
    if extras:
        extras()
    if rover_at:
        rover = BV["rover_mk1"](BV["palette"]())
        rover.location = (rover_at[0], rover_at[1], 0.0)
        rover.rotation_euler.z = math.radians(rover_at[2])
    scene.render.engine = "CYCLES"
    scene.cycles.samples = CFG["samples"]
    scene.cycles.use_denoising = True
    if CFG["threads"]:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = CFG["threads"]
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    cam.data.clip_end = 2000
    os.makedirs(folder, exist_ok=True)
    sun = bpy.data.objects["Sun"]
    bg = scene.world.node_tree.nodes["Background"]
    lights = []

    def shoot(name, pos, target, lens=None, angle_y=None, ortho=None, w=None, h=None):
        w = w or CFG["res"]
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
        path = os.path.join(folder, f"{prefix}-{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path, flush=True)

    def night(on):
        sun.hide_render = on
        bg.inputs["Strength"].default_value = 0.004 if on else 0.12
        scene.view_settings.exposure = 0.0 if on else -0.6
        if on and not lights:
            for name, colour, energy, loc in night_lights:
                lt = bpy.data.objects.new(name, bpy.data.lights.new(name, "POINT"))
                lt.data.energy = energy
                lt.data.color = colour
                lt.data.shadow_soft_size = 0.3
                lt.location = loc
                link(lt)
                lights.append(lt)
        for lt in lights:
            lt.hide_render = not on

    for name in CFG["shots"]:
        night(name == "night")
        if on_shot:
            on_shot(name)
        if name == "game":
            pos, target = game_cam(50.0)
            shoot(name, pos, target, angle_y=45)
        elif name == "pieces":
            groups = sorted((o for o in root.children if o.name.startswith("Build")), key=lambda o: int(o.name[5:]))
            n = len(groups)
            stops = sorted({max(1, round(n * f)) for f in (0.25, 0.5, 0.75)} | {n})
            for upto in stops:
                for k, g in enumerate(groups):
                    for o in [g] + list(g.children_recursive):
                        o.hide_render = k >= upto
                pos, target, lens = pieces_cam or shots["hero"]
                shoot(f"pieces{upto:02d}", pos, target, lens=lens, w=CFG["res"] // 2, h=int(CFG["res"] / 3))
            for g in groups:
                for o in [g] + list(g.children_recursive):
                    o.hide_render = False
        elif name in shots:
            spec = shots[name]
            if spec[2] == "ortho":
                shoot(name, spec[0], spec[1], ortho=spec[3], w=CFG["res"], h=CFG["res"])
            else:
                shoot(name, spec[0], spec[1], lens=spec[2], w=CFG["res"], h=int(CFG["res"] * 2 / 3))


def sheet(folder, prefix, names, out, cols=2):
    """Contact sheet of folder/prefix-<name>.png (system python with PIL; called separately)."""
    from PIL import Image
    tiles = [Image.open(os.path.join(folder, f"{prefix}-{n}.png")).convert("RGB") for n in names]
    w, h = tiles[0].size
    rows = (len(tiles) + cols - 1) // cols
    img = Image.new("RGB", (cols * w, rows * h), (14, 16, 20))
    for i, t in enumerate(tiles):
        img.paste(t, ((i % cols) * w, (i // cols) * h))
    img.save(out)


def finish(root, keep, separate, image_name, ground_z, soot=None, **preview_args):
    tris = tri_count(root)
    print("triangles before join:", tris)
    if CFG["join"]:
        join_pieces(root, keep, separate)
    if CFG["bake"]:
        bake_grime(root, separate, image_name, ground_z, soot)
    if CFG["out"]:
        export(root, CFG["out"])
    if CFG["preview"]:
        preview(root, **preview_args)
