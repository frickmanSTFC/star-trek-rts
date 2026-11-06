"""Concept renders of three non-Class-M surfaces for the hostile-worlds mockup (docs/mockups/hostile-worlds).

Mockup only: nothing here is a game asset. It builds a patch of the game's pointy-top hex terrain (hex width 25 world
units, odd rows shifted right, as PlanetView / HexGrid) with per-vertex colour and masks computed in numpy, places the
game's own glbs (outpost_l1, refinery, vehicles) for scale, and renders with Cycles from the game's camera:

  overview  zoom 260, the strategy pitch (0, 0.82, 0.57) of PlanetDriver.UpdateCamera, fov 45 vertical
  game      zoom 50, the eased pitch at that distance (smoothstep(15, 160, d) between (0,.34,.94) and (0,.82,.57))
  feed      the outpost's video-feed camera (SurfaceFeed: fov 52), low over the ground, sky in frame

Worlds: cinder (proposed volcanic Class F moon of Hesper), veyra (Class L), tarsis_a (Class D).
With the overview shot it also writes <world>-overview.json: every hex's corners and centre in screen space, so the
page can draw overlays (lava forecast, hazards, fog, dome rings) over the render.

Run:  /home/user/.bpyvenv/bin/python blender/build_hostile_worlds.py --world cinder --shot overview --out DIR
      [--samples 40] [--res 1280x720] [--quick]
"""
import argparse
import json
import math
import os
import sys

import bpy  # noqa: F401  (import bpy before bmesh)
import bmesh
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HEX = 25.0
ROW = HEX * 0.8660254
SIZE = HEX / math.sqrt(3)          # corner radius

ap = argparse.ArgumentParser()
ap.add_argument("--world", required=True)
ap.add_argument("--shot", default="overview")
ap.add_argument("--out", required=True)
ap.add_argument("--samples", type=int, default=40)
ap.add_argument("--res", default="1280x720")
ap.add_argument("--quick", action="store_true")
ap.add_argument("--blend", action="store_true", help="also save the .blend")
args = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:])
RES = tuple(int(v) for v in args.res.split("x"))
if args.quick:
    RES = (RES[0] // 3, RES[1] // 3)
    args.samples = min(args.samples, 8)

COLS, ROWS = 18, 14


# ---------------------------------------------------------------- noise

def _hash(ix, iy, seed):
    h = (ix.astype(np.uint64) * np.uint64(374761393) + iy.astype(np.uint64) * np.uint64(668265263)
         + np.uint64(seed * 982451653 + 1013904223))
    h = (h ^ (h >> np.uint64(13))) * np.uint64(1274126177)
    h = h ^ (h >> np.uint64(16))
    return (h & np.uint64(0xFFFFFF)).astype(np.float64) / float(0xFFFFFF)


def vnoise(x, y, seed=0):
    xi, yi = np.floor(x), np.floor(y)
    fx, fy = x - xi, y - yi
    xi, yi = xi.astype(np.int64), yi.astype(np.int64)
    u, v = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = _hash(xi, yi, seed), _hash(xi + 1, yi, seed)
    c, d = _hash(xi, yi + 1, seed), _hash(xi + 1, yi + 1, seed)
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v


def fbm(x, y, octaves=5, seed=0, gain=0.5):
    s, amp, norm, f = 0.0, 1.0, 0.0, 1.0
    for i in range(octaves):
        s = s + amp * vnoise(x * f, y * f, seed + i * 31)
        norm += amp
        amp *= gain
        f *= 2.03
    return s / norm


def ridged(x, y, octaves=4, seed=0):
    s, amp, norm, f = 0.0, 1.0, 0.0, 1.0
    for i in range(octaves):
        n = 1 - abs(vnoise(x * f, y * f, seed + i * 17) * 2 - 1)
        s = s + amp * n * n
        norm += amp
        amp *= 0.5
        f *= 2.1
    return s / norm


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def lin(hexcol):
    hexcol = hexcol.lstrip("#")
    c = [int(hexcol[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return np.array([x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c])


def mix(a, b, t):
    t = np.asarray(t, float)
    vec = any(np.ndim(v) >= 1 and np.shape(v)[-1] == 3 and np.ndim(v) != t.ndim for v in (a, b))
    if vec and t.ndim:
        t = t[..., None]
    return a * (1 - t) + b * t


# ---------------------------------------------------------------- hex grid

def centre(col, row):
    return np.array([(col + 0.5 * (row & 1)) * HEX, -row * ROW])


def hex_of(x, y):
    """Offset (col, row) of the hex holding each point (pointy top, odd-r; Blender y = -game z)."""
    yy = -y
    q = (math.sqrt(3) / 3 * x - yy / 3) / SIZE
    r = (2 / 3 * yy) / SIZE
    s = -q - r
    rq, rr, rs = np.round(q), np.round(r), np.round(s)
    dq, dr, ds = abs(rq - q), abs(rr - r), abs(rs - s)
    fix_q = (dq > dr) & (dq > ds)
    fix_r = ~fix_q & (dr > ds)
    rq = np.where(fix_q, -rr - rs, rq)
    rr = np.where(fix_r, -rq - rs, rr)
    rr = rr.astype(np.int64)
    rq = rq.astype(np.int64)
    col = rq + (rr - (rr & 1)) // 2
    return col, rr


def edge_dist(x, y, col, row):
    cx = (col + 0.5 * (row & 1)) * HEX
    cy = -row * ROW
    dx, dy = x - cx, y - cy
    m = np.maximum(abs(dx), np.maximum(abs(dx * 0.5 + dy * 0.8660254), abs(dx * 0.5 - dy * 0.8660254)))
    return HEX / 2 - m


def corners(col, row):
    c = centre(col, row)
    # pointy top: corners at 30 + 60k degrees, corner 0 toward north (+y)
    return [c + SIZE * np.array([math.cos(math.radians(90 - 60 * k)), math.sin(math.radians(90 - 60 * k))]) for k in range(6)]


def seg_dist(px, py, pts):
    """Distance from points to a polyline, and the arc-length fraction of the nearest point."""
    pts = np.asarray(pts, float)
    best = np.full(px.shape, 1e9)
    frac = np.zeros(px.shape)
    lens = np.linalg.norm(pts[1:] - pts[:-1], axis=1)
    total = lens.sum()
    acc = 0.0
    for (ax, ay), (bx, by), ln in zip(pts[:-1], pts[1:], lens):
        vx, vy = bx - ax, by - ay
        t = np.clip(((px - ax) * vx + (py - ay) * vy) / (ln * ln), 0, 1)
        d = np.hypot(px - (ax + t * vx), py - (ay + t * vy))
        m = d < best
        best = np.where(m, d, best)
        frac = np.where(m, (acc + t * ln) / total, frac)
        acc += ln
    return best, frac


def box_blur(a, k):
    """Separable box blur of a 2D array, radius k cells, edges clamped."""
    for axis in (0, 1):
        pad = [(0, 0), (0, 0)]
        pad[axis] = (k + 1, k)
        p = np.pad(a, pad, mode="edge")
        c = np.cumsum(p, axis=axis)
        if axis == 0:
            a = (c[2 * k + 1:] - c[:-2 * k - 1]) / (2 * k + 1)
        else:
            a = (c[:, 2 * k + 1:] - c[:, :-2 * k - 1]) / (2 * k + 1)
    return a


# ---------------------------------------------------------------- materials

def node(nt, kind, loc=(0, 0), **inputs):
    n = nt.nodes.new(kind)
    n.location = loc
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def link(nt, a, b):
    nt.links.new(a, b)


def math_node(nt, op, a, b=None, c=None, clamp=False):
    n = nt.nodes.new("ShaderNodeMath")
    n.operation = op
    n.use_clamp = clamp
    for i, v in enumerate((a, b, c)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            n.inputs[i].default_value = v
        else:
            nt.links.new(v, n.inputs[i])
    return n.outputs[0]


def ground_material(world):
    """Albedo from the 'albedo' colour attribute, masks from 'maskA' (R lava, G crust, B frost, A wet/lichen),
    detail noise + bump, and lava light from Voronoi cracks."""
    m = bpy.data.materials.new("Ground")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial", (900, 0))
    bsdf = node(nt, "ShaderNodeBsdfPrincipled", (600, 0))
    link(nt, bsdf.outputs[0], out.inputs[0])
    alb = nt.nodes.new("ShaderNodeAttribute")
    alb.attribute_name = "albedo"
    mask = nt.nodes.new("ShaderNodeAttribute")
    mask.attribute_name = "maskA"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    link(nt, mask.outputs["Color"], sep.inputs[0])
    lava, crust, frost = sep.outputs[0], sep.outputs[1], sep.outputs[2]
    rgh = nt.nodes.new("ShaderNodeAttribute")
    rgh.attribute_name = "rough"
    tc = nt.nodes.new("ShaderNodeTexCoord")

    # detail: two noise scales tint the albedo
    n1 = node(nt, "ShaderNodeTexNoise", Scale=0.35, Detail=6.0, Roughness=0.6)
    link(nt, tc.outputs["Object"], n1.inputs["Vector"])
    n2 = node(nt, "ShaderNodeTexNoise", Scale=2.6, Detail=4.0, Roughness=0.7)
    link(nt, tc.outputs["Object"], n2.inputs["Vector"])
    tint = math_node(nt, "MULTIPLY_ADD", n1.outputs["Fac"], 0.55, 0.72)
    tint = math_node(nt, "MULTIPLY_ADD", n2.outputs["Fac"], 0.35, math_node(nt, "SUBTRACT", tint, 0.17))
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    link(nt, alb.outputs["Color"], mul.inputs["A"])
    comb = nt.nodes.new("ShaderNodeCombineColor")
    for i in range(3):
        link(nt, tint, comb.inputs[i])
    link(nt, comb.outputs[0], mul.inputs["B"])
    link(nt, mul.outputs["Result"], bsdf.inputs["Base Color"])
    link(nt, rgh.outputs["Fac"], bsdf.inputs["Roughness"])

    # frost / ice sparkle: a bit of specular on frost
    spec = math_node(nt, "MULTIPLY_ADD", frost, 0.5, 0.3)
    link(nt, spec, bsdf.inputs["Specular IOR Level"])

    # bump
    bump = node(nt, "ShaderNodeBump", Strength=0.35, Distance=0.6)
    hb = math_node(nt, "ADD", n2.outputs["Fac"], math_node(nt, "MULTIPLY", n1.outputs["Fac"], 0.6))
    link(nt, hb, bump.inputs["Height"])
    link(nt, bump.outputs[0], bsdf.inputs["Normal"])

    if world == "cinder":
        vor = nt.nodes.new("ShaderNodeTexVoronoi")
        vor.feature = "DISTANCE_TO_EDGE"
        vor.inputs["Scale"].default_value = 0.42
        vor.inputs["Randomness"].default_value = 1.0
        warp = node(nt, "ShaderNodeTexNoise", Scale=0.12, Detail=3.0)
        link(nt, tc.outputs["Object"], warp.inputs["Vector"])
        vadd = nt.nodes.new("ShaderNodeVectorMath")
        vadd.operation = "MULTIPLY_ADD"
        link(nt, tc.outputs["Object"], vadd.inputs[0])
        vadd.inputs[1].default_value = (1, 1, 1)
        warpc = nt.nodes.new("ShaderNodeVectorMath")
        warpc.operation = "SCALE"
        link(nt, warp.outputs["Color"], warpc.inputs[0])
        warpc.inputs["Scale"].default_value = 3.0
        link(nt, warpc.outputs[0], vadd.inputs[2])
        link(nt, vadd.outputs[0], vor.inputs["Vector"])
        crack = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "DIVIDE", vor.outputs["Distance"], 0.07), clamp=True)
        crack = math_node(nt, "POWER", crack, 1.6)
        flow = node(nt, "ShaderNodeTexNoise", Scale=0.07, Detail=4.0)
        link(nt, tc.outputs["Object"], flow.inputs["Vector"])
        hot = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", flow.outputs["Fac"], 0.5), 4.0, clamp=True)
        plates = node(nt, "ShaderNodeTexNoise", Scale=0.05, Detail=2.0)
        link(nt, tc.outputs["Object"], plates.inputs["Vector"])
        plate = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", plates.outputs["Fac"], 0.5), 5.0, clamp=True)
        keep = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "MULTIPLY", plate, math_node(nt, "SUBTRACT", 0.85, crack)), clamp=True)
        lav = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", lava, keep), math_node(nt, "ADD", 0.12, math_node(nt, "MAXIMUM", crack, hot)))
        cru = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", crust, math_node(nt, "POWER", crack, 2.0)), 0.16)
        emit = math_node(nt, "ADD", lav, cru)
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        cr = ramp.color_ramp
        cr.elements[0].position = 0.0
        cr.elements[0].color = (0.25, 0.012, 0.002, 1)
        cr.elements[1].position = 1.0
        cr.elements[1].color = (1.0, 0.72, 0.22, 1)
        e = cr.elements.new(0.45)
        e.color = (0.95, 0.2, 0.02, 1)
        link(nt, emit, ramp.inputs[0])
        link(nt, ramp.outputs[0], bsdf.inputs["Emission Color"])
        link(nt, math_node(nt, "MULTIPLY", math_node(nt, "POWER", emit, 1.6), 16.0), bsdf.inputs["Emission Strength"])
    m.name = "Ground_" + world
    return m


def simple_mat(name, colour, rough=0.6, metal=0.0, emit=None, emit_strength=0.0, alpha=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*colour, 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    if emit is not None:
        p.inputs["Emission Color"].default_value = (*emit, 1)
        p.inputs["Emission Strength"].default_value = emit_strength
    if alpha is not None:
        p.inputs["Alpha"].default_value = alpha
    return m


def glass_mat(name, tint=(0.75, 0.85, 1.0), alpha=0.12, edge=0.5):
    """Cheap dome glass: transparent mixed with a glossy coat by fresnel (no refraction noise)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial", (600, 0))
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    tr.inputs["Color"].default_value = (*tint, 1)
    gl = nt.nodes.new("ShaderNodeBsdfGlossy")
    gl.inputs["Roughness"].default_value = 0.04
    gl.inputs["Color"].default_value = (*tint, 1)
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = edge
    fac = math_node(nt, "ADD", lw.outputs["Fresnel"], alpha, clamp=True)
    mx = nt.nodes.new("ShaderNodeMixShader")
    link(nt, fac, mx.inputs[0])
    link(nt, tr.outputs[0], mx.inputs[1])
    link(nt, gl.outputs[0], mx.inputs[2])
    link(nt, mx.outputs[0], out.inputs[0])
    return m


def field_mat(name, colour, strength=1.2, edge=0.25):
    """Energy-shield shell: emission strongest at grazing angles over a hex cell pattern, otherwise see-through."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial", (600, 0))
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*colour, 1)
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = edge
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.feature = "DISTANCE_TO_EDGE"
    vor.inputs["Scale"].default_value = 0.6
    vor.inputs["Randomness"].default_value = 0.0
    tc = nt.nodes.new("ShaderNodeTexCoord")
    link(nt, tc.outputs["Object"], vor.inputs["Vector"])
    cell = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "DIVIDE", vor.outputs["Distance"], 0.05), clamp=True)
    fac = math_node(nt, "ADD", math_node(nt, "MULTIPLY", math_node(nt, "POWER", lw.outputs["Facing"], 6.0), 0.6),
                    math_node(nt, "MULTIPLY", cell, 0.05), clamp=True)
    link(nt, math_node(nt, "MULTIPLY", fac, strength), em.inputs["Strength"])
    add = nt.nodes.new("ShaderNodeAddShader")       # additive glow: never darkens what is behind
    link(nt, tr.outputs[0], add.inputs[0])
    link(nt, em.outputs[0], add.inputs[1])
    link(nt, add.outputs[0], out.inputs[0])
    return m


def volume_mat(name, colour, density, noise_scale=0.0, falloff=None, emit=None, emit_strength=0.0, anis=0.3):
    """Principled volume. With noise_scale, density = noise; with falloff='plume' it fades with the distance from
    the object's Z axis (radius growing with height) and toward the top; 'column' for a thin dust devil."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial", (800, 0))
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Color"].default_value = (*colour, 1)
    vol.inputs["Anisotropy"].default_value = anis
    link(nt, vol.outputs[0], out.inputs["Volume"])
    if noise_scale <= 0:
        vol.inputs["Density"].default_value = density
        return m
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nz = node(nt, "ShaderNodeTexNoise", Scale=noise_scale, Detail=5.0, Roughness=0.62)
    link(nt, tc.outputs["Object"], nz.inputs["Vector"])
    d = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", nz.outputs["Fac"], 0.38), 2.2, clamp=True)
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    link(nt, tc.outputs["Object"], sep.inputs[0])
    z = math_node(nt, "MULTIPLY_ADD", sep.outputs["Z"], 0.5, 0.5)           # 0 at bottom, 1 at top (cube -1..1)
    rr = math_node(nt, "SQRT", math_node(nt, "ADD", math_node(nt, "MULTIPLY", sep.outputs["X"], sep.outputs["X"]),
                                           math_node(nt, "MULTIPLY", sep.outputs["Y"], sep.outputs["Y"])))
    if falloff == "plume":
        radius = math_node(nt, "MULTIPLY_ADD", z, 0.6, 0.12)
    else:  # column
        radius = math_node(nt, "MULTIPLY_ADD", z, 0.35, 0.18)
    radial = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "DIVIDE", rr, radius), clamp=True)
    top = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "POWER", z, 1.6), clamp=True)
    dens = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", d, radial), math_node(nt, "MULTIPLY", top, density))
    link(nt, dens, vol.inputs["Density"])
    if emit is not None:
        vol.inputs["Emission Color"].default_value = (*emit, 1)
        low = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "DIVIDE", z, 0.25), clamp=True)
        link(nt, math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", low, d), radial), emit_strength),
             vol.inputs["Emission Strength"])
    return m


# ---------------------------------------------------------------- scene helpers

def link_obj(o):
    bpy.context.scene.collection.objects.link(o)
    return o


def mesh_obj(name, verts, faces, mat=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    link_obj(o)
    if mat:
        o.data.materials.append(mat)
    return o


def import_glb(rel, loc, rot_deg=0.0, scale=1.0, owner=(0.18, 0.42, 0.85)):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(REPO, rel))
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None]
    for o in new:
        if o.type == "MESH":
            for mt in o.data.materials:
                if mt and mt.name.split(".")[0] == "Owner" and mt.use_nodes:
                    p = mt.node_tree.nodes.get("Principled BSDF")
                    if p and not p.inputs["Base Color"].is_linked:
                        p.inputs["Base Color"].default_value = (*owner, 1)
    for r in roots:
        r.location = Vector(loc)
        r.rotation_euler = (0, 0, math.radians(rot_deg))
        r.scale = (scale, scale, scale)
    return new


def sun(energy, colour, elev, azim, angle=0.6):
    ld = bpy.data.lights.new("Sun", "SUN")
    ld.energy = energy
    ld.color = colour
    ld.angle = math.radians(angle)
    o = link_obj(bpy.data.objects.new("Sun", ld))
    # light travels along -Z of the object; point it from (azim, elev)
    d = Vector((math.cos(math.radians(elev)) * math.sin(math.radians(azim)),
                math.cos(math.radians(elev)) * math.cos(math.radians(azim)), math.sin(math.radians(elev))))
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return o, d


def point(loc, colour, energy, radius=1.0):
    ld = bpy.data.lights.new("Pt", "POINT")
    ld.energy = energy
    ld.color = colour
    ld.shadow_soft_size = radius
    o = link_obj(bpy.data.objects.new("Pt", ld))
    o.location = loc
    return o


def world_sky(bottom, horizon, zenith, strength=1.0, stars=0.0, ground=None):
    w = bpy.data.worlds.new("Sky")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputWorld", (800, 0))
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = strength
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    link(nt, tc.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.45
    cr.elements[0].color = (*bottom, 1)
    cr.elements[1].position = 0.75
    cr.elements[1].color = (*zenith, 1)
    e = cr.elements.new(0.505)
    e.color = (*horizon, 1)
    zz = math_node(nt, "MULTIPLY_ADD", sep.outputs["Z"], 0.5, 0.5)
    link(nt, zz, ramp.inputs[0])
    col = ramp.outputs[0]
    if stars > 0:
        vor = nt.nodes.new("ShaderNodeTexVoronoi")
        vor.inputs["Scale"].default_value = 420.0
        link(nt, tc.outputs["Generated"], vor.inputs["Vector"])
        st = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "DIVIDE", vor.outputs["Distance"], 0.07), clamp=True)
        st = math_node(nt, "MULTIPLY", math_node(nt, "POWER", st, 6.0), stars)
        add = nt.nodes.new("ShaderNodeMix")
        add.data_type = "RGBA"
        add.blend_type = "ADD"
        add.inputs["Factor"].default_value = 1.0
        link(nt, col, add.inputs["A"])
        comb = nt.nodes.new("ShaderNodeCombineColor")
        for i in range(3):
            link(nt, st, comb.inputs[i])
        link(nt, comb.outputs[0], add.inputs["B"])
        col = add.outputs["Result"]
    link(nt, col, bg.inputs["Color"])
    link(nt, bg.outputs[0], out.inputs["Surface"])
    return w


def cube_volume(name, loc, size, mat, rot_z=0.0):
    bpy.ops.mesh.primitive_cube_add(size=2, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.rotation_euler = (0, 0, rot_z)
    o.data.materials.append(mat)
    return o


# ---------------------------------------------------------------- terrain build

class Terrain:
    def __init__(self, x0, x1, y0, y1, step):
        self.xs = np.arange(x0, x1 + 1e-6, step)
        self.ys = np.arange(y0, y1 + 1e-6, step)
        self.X, self.Y = np.meshgrid(self.xs, self.ys)
        self.step = step
        self.col, self.row = hex_of(self.X, self.Y)
        self.edge = edge_dist(self.X, self.Y, self.col, self.row)
        self.H = np.zeros_like(self.X)

    def tile_field(self, per_tile, default=0.0, blur=4.0):
        """Piecewise-constant field from a {(col, row): value} function, blurred so hexes read as soft plateaus."""
        keys = self.col * 1000 + self.row
        uk, inv = np.unique(keys, return_inverse=True)
        vals = np.array([per_tile(int(k // 1000), int(k % 1000)) for k in uk], float)
        f = vals[inv].reshape(self.X.shape)
        if blur > 0:
            k = max(1, int(round(blur / self.step)))
            f = box_blur(box_blur(f, k), k)
        return f

    def z_at(self, x, y):
        fx = (x - self.xs[0]) / self.step
        fy = (y - self.ys[0]) / self.step
        i0, j0 = int(np.clip(math.floor(fy), 0, len(self.ys) - 2)), int(np.clip(math.floor(fx), 0, len(self.xs) - 2))
        ty, tx = fy - i0, fx - j0
        H = self.H
        return float((H[i0, j0] * (1 - tx) + H[i0, j0 + 1] * tx) * (1 - ty) + (H[i0 + 1, j0] * (1 - tx) + H[i0 + 1, j0 + 1] * tx) * ty)

    def normals(self):
        gy, gx = np.gradient(self.H, self.step)
        n = np.stack([-gx, -gy, np.ones_like(gx)], -1)
        return n / np.linalg.norm(n, axis=-1, keepdims=True)

    def build(self, albedo, rough, maskA, mat):
        ny, nx = self.X.shape
        verts = np.stack([self.X, self.Y, self.H], -1).reshape(-1, 3)
        idx = np.arange(nx * ny).reshape(ny, nx)
        a, b, c, d = idx[:-1, :-1], idx[:-1, 1:], idx[1:, 1:], idx[1:, :-1]
        faces = np.stack([a, b, c, d], -1).reshape(-1, 4)
        me = bpy.data.meshes.new("Terrain")
        me.vertices.add(len(verts))
        me.vertices.foreach_set("co", verts.astype(np.float32).ravel())
        me.loops.add(len(faces) * 4)
        me.loops.foreach_set("vertex_index", faces.astype(np.int32).ravel())
        me.polygons.add(len(faces))
        me.polygons.foreach_set("loop_start", (np.arange(len(faces)) * 4).astype(np.int32))
        me.update(calc_edges=True)
        me.validate()
        me.shade_smooth()
        for name, arr in (("albedo", albedo), ("maskA", maskA)):
            at = me.color_attributes.new(name, "FLOAT_COLOR", "POINT")
            arr = arr.reshape(-1, arr.shape[-1])
            if arr.shape[1] == 3:
                arr = np.concatenate([arr, np.ones((len(arr), 1))], 1)
            at.data.foreach_set("color", arr.astype(np.float32).ravel())
        ra = me.attributes.new("rough", "FLOAT", "POINT")
        ra.data.foreach_set("value", rough.astype(np.float32).ravel())
        o = bpy.data.objects.new("Terrain", me)
        link_obj(o)
        o.data.materials.append(mat)
        return o


def rocks(terrain, n, weight, scale_range, colour, seed=1, name="Rocks", rough=0.85, sink=0.35, flatten=0.7):
    """Scatter n deformed icospheres where `weight` (same shape as the grid) is high."""
    rng = np.random.default_rng(seed)
    w = np.clip(weight, 0, None).ravel()
    if w.sum() <= 0:
        return None
    pick = rng.choice(len(w), size=n, p=w / w.sum())
    bm = bmesh.new()
    base = bmesh.new()
    bmesh.ops.create_icosphere(base, subdivisions=1, radius=1.0)
    bverts = np.array([v.co[:] for v in base.verts])
    bfaces = [[v.index for v in f.verts] for f in base.faces]
    base.free()
    allv, allf = [], []
    for k, i in enumerate(pick):
        x = terrain.X.ravel()[i] + rng.uniform(-terrain.step, terrain.step)
        y = terrain.Y.ravel()[i] + rng.uniform(-terrain.step, terrain.step)
        s = rng.uniform(*scale_range) * (rng.random() ** 1.8 * 0.8 + 0.4)
        jitter = 1 + 0.32 * (rng.random(bverts.shape) - 0.5)
        v = bverts * jitter * np.array([s * rng.uniform(0.8, 1.4), s * rng.uniform(0.7, 1.2), s * flatten])
        ang = rng.uniform(0, math.tau)
        ca, sa = math.cos(ang), math.sin(ang)
        v = v @ np.array([[ca, -sa, 0], [sa, ca, 0], [0, 0, 1]]).T
        v = v + np.array([x, y, terrain.z_at(x, y) + s * flatten * (1 - sink * 2) * 0.5])
        off = len(allv)
        allv.extend(v.tolist())
        allf.extend([[a + off for a in f] for f in bfaces])
    o = mesh_obj(name, allv, allf, simple_mat(name + "Mat", colour, rough))
    o.data.shade_smooth()
    return o


def dome(loc, radius, height, frame_col=(0.55, 0.58, 0.62), glass=None, ribs=3, field=None):
    """Geodesic pressure dome: an icosphere cut at the equator, flattened, wire frame + glass shell."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=ribs, radius=1.0)
    for v in list(bm.verts):
        if v.co.z < -0.02:
            bm.verts.remove(v)
    for v in bm.verts:
        v.co.x *= radius
        v.co.y *= radius
        v.co.z = max(v.co.z, 0) * height
    me = bpy.data.meshes.new("Dome")
    bm.to_mesh(me)
    bm.free()
    shell = bpy.data.objects.new("DomeGlass", me)
    link_obj(shell)
    shell.location = loc
    shell.data.materials.append(glass or glass_mat("DomeGlass"))
    fr = bpy.data.objects.new("DomeFrame", me.copy())
    link_obj(fr)
    fr.location = loc
    wf = fr.modifiers.new("wire", "WIREFRAME")
    wf.thickness = 0.32
    wf.use_replace = True
    fr.data.materials.append(simple_mat("DomeFrameMat", frame_col, 0.45, 0.8))
    # ring footing
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=radius * 1.02, depth=1.6, location=(loc[0], loc[1], loc[2] + 0.2), end_fill_type="NOTHING")
    ring = bpy.context.active_object
    ring.data.materials.append(simple_mat("DomeRing", (0.32, 0.34, 0.37), 0.7, 0.3))
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=radius * 1.03, depth=0.25, location=(loc[0], loc[1], loc[2] + 1.05), end_fill_type="NOTHING")
    strip = bpy.context.active_object
    strip.data.materials.append(simple_mat("DomeStrip", (0.1, 0.1, 0.1), 0.4, 0, emit=(0.5, 0.75, 1.0), emit_strength=1.5))
    return shell


def field_bubble(loc, radius, height, colour, strength=0.7):
    """The Pressure Dome's containment field: a faint shell over every hex it protects."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=96, ring_count=48, radius=1.0, location=loc)
    o = bpy.context.active_object
    bm = bmesh.new()
    bm.from_mesh(o.data)
    for v in list(bm.verts):
        if v.co.z < -0.01:
            bm.verts.remove(v)
    bm.to_mesh(o.data)
    bm.free()
    o.scale = (radius, radius, height)
    o.data.materials.append(field_mat("ContainmentField", colour, strength, 0.12))
    o.data.shade_smooth()
    o.visible_shadow = False
    return o


def airlock(loc, rot_deg, length=7.0):
    """A short tube from the dome wall: a sealed vehicle airlock."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=2.1, depth=length, location=loc,
                                        rotation=(math.radians(90), 0, math.radians(rot_deg)))
    o = bpy.context.active_object
    o.data.materials.append(simple_mat("Airlock", (0.62, 0.64, 0.68), 0.5, 0.5))
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    lamp = bpy.context.active_object
    lamp.scale = (0.4, 0.4, 0.4)
    lamp.location.z += 2.2
    lamp.data.materials.append(simple_mat("AirlockLamp", (0.1, 0.1, 0.1), 0.4, 0, emit=(1.0, 0.55, 0.12), emit_strength=25))
    return o


def barrier(points, z_of, height=4.2, colour=(0.35, 0.36, 0.38), glow=(1.0, 0.5, 0.15)):
    """Lava barrier / heat shield: angled ferrocrete slabs along a polyline with an emitter strip and a field."""
    objs = []
    slab = simple_mat("BarrierSlab", (0.16, 0.16, 0.17), 0.75, 0.2)
    lamp = simple_mat("BarrierEmit", (0.1, 0.1, 0.1), 0.4, 0, emit=(0.55, 0.8, 1.0), emit_strength=2.5)
    fieldm = field_mat("BarrierField", (0.5, 0.75, 1.0), strength=0.35, edge=0.5)
    pts = np.asarray(points, float)
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        ln = math.hypot(bx - ax, by - ay)
        nseg = max(1, int(ln / 5.5))
        for k in range(nseg):
            t = (k + 0.5) / nseg
            x, y = ax + (bx - ax) * t, ay + (by - ay) * t
            z = z_of(x, y)
            ang = math.atan2(by - ay, bx - ax)
            bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z + height / 2 - 0.6),
                                            rotation=(math.radians(12), 0, ang))
            o = bpy.context.active_object
            o.scale = (ln / nseg * 0.96, 1.6, height)
            o.data.materials.append(slab)
            bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z + height - 0.45), rotation=(math.radians(12), 0, ang))
            s = bpy.context.active_object
            s.scale = (ln / nseg * 0.9, 0.5, 0.3)
            s.data.materials.append(lamp)
            bpy.ops.mesh.primitive_plane_add(size=1, location=(x, y, z + height + 2.4), rotation=(math.radians(90), 0, ang))
            f = bpy.context.active_object
            f.scale = (ln / nseg, 5.0, 1)
            f.data.materials.append(fieldm)
            objs += [o, s, f]
    return objs


def stack(loc, radius=1.2, height=9, colour=(0.45, 0.46, 0.48)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=radius, depth=height, location=(loc[0], loc[1], loc[2] + height / 2))
    o = bpy.context.active_object
    o.data.materials.append(simple_mat("Stack", colour, 0.6, 0.4))
    return o


def flakes(n, centre, extent, size, colour, seed=7, name="Ash"):
    rng = np.random.default_rng(seed)
    verts, faces = [], []
    for i in range(n):
        p = np.array(centre) + (rng.random(3) - 0.5) * np.array(extent)
        a = rng.random(3) - 0.5
        b = rng.random(3) - 0.5
        s = size * rng.uniform(0.5, 1.4)
        off = len(verts)
        verts += [p.tolist(), (p + a * s).tolist(), (p + b * s).tolist()]
        faces.append([off, off + 1, off + 2])
    return mesh_obj(name, verts, faces, simple_mat(name + "Mat", colour, 0.9))


def planet_in_sky(direction, dist, radius, sun_dir):
    """Tarsis seen from its moon: a lit sphere with procedural seas, land, ice and a blue rim."""
    loc = Vector(direction).normalized() * dist
    bpy.ops.mesh.primitive_uv_sphere_add(segments=96, ring_count=48, radius=radius, location=loc)
    o = bpy.context.active_object
    o.rotation_euler = (math.radians(20), math.radians(-15), 0)
    m = bpy.data.materials.new("Tarsis")
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nz = node(nt, "ShaderNodeTexNoise", Scale=2.2, Detail=8.0, Roughness=0.62)
    link(nt, tc.outputs["Object"], nz.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.5
    cr.elements[0].color = (0.004, 0.025, 0.11, 1)
    cr.elements[1].position = 0.6
    cr.elements[1].color = (0.2, 0.16, 0.08, 1)
    e = cr.elements.new(0.515)
    e.color = (0.05, 0.13, 0.035, 1)
    link(nt, nz.outputs["Fac"], ramp.inputs[0])
    cl = node(nt, "ShaderNodeTexNoise", Scale=4.5, Detail=6.0, Roughness=0.55)
    link(nt, tc.outputs["Object"], cl.inputs["Vector"])
    cloud = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", cl.outputs["Fac"], 0.56), 3.0, clamp=True)
    mx = nt.nodes.new("ShaderNodeMix")
    mx.data_type = "RGBA"
    link(nt, cloud, mx.inputs["Factor"])
    link(nt, ramp.outputs[0], mx.inputs["A"])
    mx.inputs["B"].default_value = (0.85, 0.87, 0.9, 1)
    link(nt, mx.outputs["Result"], p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = 0.6
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.25
    p.inputs["Emission Color"].default_value = (0.3, 0.55, 1.0, 1)
    link(nt, math_node(nt, "MULTIPLY", math_node(nt, "POWER", lw.outputs["Fresnel"], 3.0), 0.9), p.inputs["Emission Strength"])
    o.data.materials.append(m)
    o.visible_shadow = False
    return o


# ---------------------------------------------------------------- the three worlds
# Each returns a dict describing the scene: terrain arrays + where things go + per-hex info for the overlay JSON.

def world_cinder(T):
    """Proposed Class F (geometallic, volcanic) moon of Hesper: thin CO2/SO2 air, basalt plains, lava channels from a
    vent field in the north, cooled crust, sulphur flats, ash, a domed outpost behind a lava barrier."""
    X, Y = T.X, T.Y
    vents = [centre(5, 1) + (3, 2), centre(11, 1) + (-2, 4), centre(15, 3)]
    # base relief: rising toward the vents (north), rolling basalt and hex plateaus
    tiles = T.tile_field(lambda c, r: (13 - r) * 1.3 + 4 * vnoise(np.array(c * 0.7), np.array(r * 0.7), 3), blur=5)
    H = tiles + 7 * fbm(X * 0.012, Y * 0.012, 5, 11) + 1.6 * fbm(X * 0.08, Y * 0.08, 3, 12)
    for (vx, vy) in vents:
        d = np.hypot(X - vx, Y - vy)
        H += 14 * np.exp(-(d / 22) ** 2) - 9 * np.exp(-(d / 5.5) ** 2)
    # flows: active (bright), and older cooled ones (crust)
    warpx = X + 9 * (fbm(X * 0.02, Y * 0.02, 3, 21) - 0.5)
    warpy = Y + 9 * (fbm(X * 0.02, Y * 0.02, 3, 22) - 0.5)
    base = centre(8, 10)
    flow_a = [vents[0], centre(5, 3), centre(6, 5) + (4, 0), centre(6, 7), centre(7, 8) + (-2, -2)]      # toward the base
    flow_a_forecast = [centre(7, 8) + (-2, -2), centre(7, 9), centre(7, 10) + (-3, -4)]
    flow_b = [vents[1], centre(11, 3), centre(12, 5), centre(12, 7) + (6, -3), centre(14, 9), centre(15, 12)]
    old_c = [vents[2], centre(15, 5), centre(16, 8), centre(16, 12)]
    old_d = [vents[0] + (-6, 0), centre(3, 3), centre(2, 6), centre(2, 10), centre(1, 13)]
    lava = np.zeros_like(X)
    crust = np.zeros_like(X)
    for pts, w, front in ((flow_a, 4.6, 1.0), (flow_b, 5.4, 1.0)):
        d, f = seg_dist(warpx, warpy, pts)
        width = w * (1.15 - 0.45 * f) * (0.8 + 0.5 * fbm(X * 0.05, Y * 0.05, 2, 5))
        lava = np.maximum(lava, smooth(width, width * 0.55, d) * (f <= front))
        crust = np.maximum(crust, smooth(width * 3.2, width * 1.1, d))
        H += -1.2 * smooth(width * 1.2, 0, d) + 1.4 * np.exp(-((d - width * 1.5) / (width * 0.6)) ** 2)   # channel + levees
    d, f = seg_dist(warpx, warpy, flow_a_forecast)
    toe = smooth(6.5, 3.0, d) * (f < 0.22)        # the advancing toe, just beyond the front
    lava = np.maximum(lava, toe * 0.75)
    crust = np.maximum(crust, smooth(10, 4, d) * (f < 0.35))
    for pts, w in ((old_c, 6.0), (old_d, 7.0)):
        d, f = seg_dist(warpx, warpy, pts)
        crust = np.maximum(crust, smooth(w, w * 0.4, d) * 0.85)
        H += 0.8 * smooth(w, w * 0.3, d)
    crust = np.clip(crust - lava, 0, 1)
    vent_d = np.min([np.hypot(X - vx, Y - vy) for vx, vy in vents], axis=0)
    sulphur = smooth(42, 10, vent_d) * smooth(0.3, 0.55, fbm(X * 0.05, Y * 0.05, 3, 31)) * (1 - lava)
    lava = np.maximum(lava, smooth(6.0, 3.0, vent_d))
    ash = smooth(0.48, 0.7, fbm(X * 0.015 + 4, Y * 0.015, 4, 41)) * (1 - crust) * (1 - lava)
    # base pad: flatten
    bd = np.hypot(X - base[0], Y - base[1])
    pad_h = float(H[np.unravel_index(np.argmin(bd), bd.shape)])
    H = mix(H, pad_h, smooth(30, 18, bd))
    T.H = H
    basalt, basalt2 = lin("#4b4540"), lin("#5e5249")
    ashc, sul, crustc = lin("#8e8983"), lin("#c9b23a"), lin("#1c1918")
    alb = mix(basalt, basalt2, fbm(X * 0.03, Y * 0.03, 3, 51))
    alb = mix(alb, ashc, ash * 0.8)
    alb = mix(alb, sul, sulphur * 0.85)
    alb = mix(alb, lin("#e4d77c"), sulphur * smooth(0.6, 0.8, fbm(X * 0.2, Y * 0.2, 2, 52)) * 0.6)
    alb = mix(alb, crustc, crust)
    alb = mix(alb, lin("#0d0b0a"), lava)
    pad = smooth(16, 12, bd)
    alb = mix(alb, lin("#4a4744"), pad * 0.8)
    rough = 0.82 - 0.3 * crust + 0.1 * ash
    maskA = np.stack([lava, crust, np.zeros_like(X), np.zeros_like(X)], -1)
    # hex classes for the overlay
    hexes = {}
    def cls(c, r):
        p = centre(c, r)
        dl = min(seg_dist(np.array([p[0]]), np.array([p[1]]), fl)[0][0] for fl in (flow_a, flow_b))
        df = seg_dist(np.array([p[0]]), np.array([p[1]]), flow_a_forecast + [centre(7, 11)])[0][0]
        dc = min(seg_dist(np.array([p[0]]), np.array([p[1]]), fl)[0][0] for fl in (old_c, old_d))
        dv = min(math.hypot(p[0] - vx, p[1] - vy) for vx, vy in vents)
        if dl < HEX * 0.42 or dv < HEX * 0.5:
            return "lava"
        if df < HEX * 0.45:
            return "forecast"
        if dc < HEX * 0.45:
            return "crust"
        if dv < HEX * 1.25:
            return "toxic"
        return "basalt"
    for r in range(ROWS):
        for c in range(COLS):
            hexes[(c, r)] = cls(c, r)
    hexes[(7, 9)] = "forecast"
    return dict(
        terrain=(alb, rough, maskA), hexes=hexes, vents=vents, base=base,
        flow=dict(active=[flow_a, flow_b], forecast=flow_a_forecast, cooled=[old_c, old_d]),
        rock_weight=(smooth(0.55, 0.75, fbm(X * 0.06, Y * 0.06, 3, 61)) * (1 - lava) * (1 - pad)) + crust * 0.5 * (1 - lava),
    )


def world_veyra(T, sun_dir):
    """Class L Veyra: rust/ochre regolith, dunes, mesas, frost in the shadows, a polar cap edge to the north,
    sparse lichen in the damp hollows, a CO2-heavy basin."""
    X, Y = T.X, T.Y
    tiles = T.tile_field(lambda c, r: 5 * vnoise(np.array(c * 0.55), np.array(r * 0.55), 7) + (2.5 if (c * 7 + r * 3) % 11 == 0 else 0), blur=5)
    H = tiles + 8 * fbm(X * 0.01, Y * 0.01, 5, 71) + 1.4 * fbm(X * 0.07, Y * 0.07, 3, 72)
    mesa = smooth(0.62, 0.66, fbm(X * 0.012 + 9, Y * 0.012, 4, 73))
    H += 13 * mesa
    dunes = smooth(0.45, 0.6, fbm(X * 0.008, Y * 0.008 + 3, 3, 74))
    dunes = dunes * smooth(0.4, 0.6, fbm(X * 0.02, Y * 0.02, 2, 70))
    H += dunes * 0.9 * np.sin((X * 0.85 + Y * 0.5) * 0.26 + 6 * fbm(X * 0.02, Y * 0.02, 3, 75))
    basin_c = centre(13, 8)
    bd = np.hypot(X - basin_c[0], Y - basin_c[1])
    H -= 9 * smooth(55, 15, bd)
    base = centre(6, 10)
    pd = np.hypot(X - base[0], Y - base[1])
    pad_h = float(H[np.unravel_index(np.argmin(pd), pd.shape)])
    H = mix(H, pad_h, smooth(34, 20, pd))
    T.H = H
    n = T.normals()
    lit = np.clip((n * np.array(sun_dir)).sum(-1), 0, 1) / max(0.05, float(sun_dir[2]))   # 1 = flat ground
    frost = smooth(0.6, 0.25, lit) * smooth(0.4, 0.7, fbm(X * 0.09, Y * 0.09, 3, 76)) * 0.7
    polar = smooth(-ROW * 1.1, ROW * 0.3, Y + 16 * (fbm(X * 0.03, Y * 0.03, 3, 77) - 0.5))  # north rows
    H = H - polar * (H - np.minimum(H, 6 + 3 * fbm(X * 0.02, Y * 0.02, 3, 84)))    # the cap is a flat ice sheet
    T.H = H
    lichen = smooth(0.55, 0.72, fbm(X * 0.05, Y * 0.05, 4, 78)) * smooth(-2, -6, H - pad_h) * (1 - polar)
    ochre, rust, dark = lin("#9a6a47"), lin("#70412c"), lin("#4a2e22")
    sand = lin("#a98260")
    alb = mix(rust, ochre, fbm(X * 0.02, Y * 0.02, 4, 79))
    alb = mix(alb, dark, mesa * 0.55)
    alb = mix(alb, lin("#3b302b"), smooth(0.6, 0.72, fbm(X * 0.03 + 5, Y * 0.03, 4, 82)) * 0.7)     # basalt outcrops
    alb = mix(alb, lin("#b39274"), smooth(0.55, 0.7, fbm(X * 0.012, Y * 0.012 + 7, 3, 83)) * 0.5)    # pale dust
    alb = mix(alb, sand, dunes * 0.5)
    alb = mix(alb, lin("#3d4a2e"), lichen * 0.75)
    alb = mix(alb, lin("#d9dde2"), frost * 0.75)
    alb = mix(alb, lin("#e9eef5"), polar)
    alb = mix(alb, lin("#c7d6e8"), polar * smooth(0.55, 0.75, fbm(X * 0.04, Y * 0.04, 3, 80)) * 0.6)  # CO2 ice
    alb = mix(alb, lin("#6a5848"), smooth(16, 12, pd) * 0.8)
    rough = 0.9 - 0.45 * np.maximum(frost, polar)
    maskA = np.stack([np.zeros_like(X), np.zeros_like(X), np.maximum(frost, polar), lichen], -1)
    hexes = {}
    for r in range(ROWS):
        for c in range(COLS):
            p = centre(c, r)
            if r <= 1:
                hexes[(c, r)] = "ice"
            elif math.hypot(p[0] - basin_c[0], p[1] - basin_c[1]) < HEX * 1.3:
                hexes[(c, r)] = "co2"
            else:
                hexes[(c, r)] = "regolith"
    return dict(terrain=(alb, rough, maskA), hexes=hexes, base=base, basin=basin_c,
                rock_weight=mesa * 0.6 + smooth(0.6, 0.8, fbm(X * 0.05, Y * 0.05, 2, 81)) * (1 - polar) * (1 - smooth(16, 12, pd)))


def world_tarsis_a(T):
    """Class D Tarsis A: grey regolith, craters of every size with rims and ejecta, a big crater with a central peak."""
    X, Y = T.X, T.Y
    rng = np.random.default_rng(1701)
    tiles = T.tile_field(lambda c, r: 3 * vnoise(np.array(c * 0.6), np.array(r * 0.6), 9), blur=6)
    H = tiles + 6 * fbm(X * 0.008, Y * 0.008, 5, 91) + 0.9 * fbm(X * 0.1, Y * 0.1, 3, 92)
    ray = np.zeros_like(X)
    big = (centre(13, 4)[0], centre(13, 4)[1], 52.0)
    craters = [big, (centre(3, 3)[0], centre(3, 3)[1], 26.0), (centre(10, 9)[0] + 8, centre(10, 9)[1], 14.0)]
    for _ in range(70):
        craters.append((rng.uniform(T.xs[0], T.xs[-1]), rng.uniform(T.ys[0], T.ys[-1]), float(rng.uniform(2, 10) ** 1.0)))
    base = centre(6, 10)
    for (cx, cy, rad) in craters:
        if math.hypot(cx - base[0], cy - base[1]) < 30 + rad:
            continue
        d = np.hypot(X - cx, Y - cy) / rad
        depth = rad * 0.22
        bowl = -depth * np.clip(1 - d * d, 0, 1)
        rim = depth * 0.55 * np.exp(-((d - 1.0) / 0.18) ** 2)
        ejecta = depth * 0.12 * np.exp(-(d - 1) * 2.2) * (d > 1)
        H += bowl + rim + ejecta
        if rad > 40:
            H += depth * 0.9 * np.exp(-(d / 0.18) ** 2)       # central peak
        ang = np.arctan2(Y - cy, X - cx)
        ray = np.maximum(ray, (d > 1) * np.exp(-(d - 1) * (0.6 if rad > 20 else 1.6)) * smooth(0.7, 0.9, vnoise(ang * 6 + rad, np.zeros_like(ang), int(rad)))
                         * (1 if rad > 12 else 0.5))
    pd = np.hypot(X - base[0], Y - base[1])
    pad_h = float(H[np.unravel_index(np.argmin(pd), pd.shape)])
    H = mix(H, pad_h, smooth(36, 22, pd))
    T.H = H
    grey, grey2 = lin("#77746f"), lin("#5a5854")
    alb = mix(grey2, grey, fbm(X * 0.02, Y * 0.02, 4, 93))
    alb = mix(alb, lin("#9a9893"), ray * 0.7)
    alb = mix(alb, lin("#4d4b48"), smooth(16, 12, pd) * 0.6)
    rough = np.full(X.shape, 0.92)
    maskA = np.zeros(X.shape + (4,))
    hexes = {}
    for r in range(ROWS):
        for c in range(COLS):
            p = centre(c, r)
            dc = math.hypot(p[0] - big[0], p[1] - big[1])
            hexes[(c, r)] = "crater" if dc < big[2] * 0.8 else ("ridge" if abs(dc - big[2]) < 12 else "regolith")
    return dict(terrain=(alb, rough, maskA), hexes=hexes, base=base, big=big,
                rock_weight=smooth(0.55, 0.85, 0.5 * ray + 0.6 * fbm(X * 0.05, Y * 0.05, 2, 94)) * (1 - smooth(18, 12, pd)))


# ---------------------------------------------------------------- main

def setup_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = args.samples
    sc.cycles.use_adaptive_sampling = True
    sc.cycles.adaptive_threshold = 0.03
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 4
    sc.cycles.diffuse_bounces = 2
    sc.cycles.glossy_bounces = 2
    sc.cycles.transmission_bounces = 4
    sc.cycles.transparent_max_bounces = 12
    sc.cycles.volume_bounces = 0
    sc.cycles.volume_step_rate = 4.0
    sc.cycles.volume_max_steps = 128
    sc.render.resolution_x, sc.render.resolution_y = RES
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "AgX - Medium High Contrast"
    sc.render.image_settings.file_format = "PNG"
    sc.render.threads_mode = "AUTO"
    return sc


def game_camera(look, distance, fov=45.0):
    t = float(smooth(15, 160, distance))
    d = np.array([0, 0.34, 0.94]) * (1 - t) + np.array([0, 0.82, 0.57]) * t
    d = d / np.linalg.norm(d)
    # game: eye = look + (0, dy, dz); +z (game) = south = -y (Blender)
    eye = Vector((look[0], look[1] - d[2] * distance, look[2] + d[1] * distance))
    return place_camera(eye, Vector(look), fov)


def place_camera(eye, target, fov):
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link_obj(cam)
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle_y = math.radians(fov)
    cam.data.clip_start = 0.3
    cam.data.clip_end = 9000
    cam.location = eye
    cam.rotation_euler = (target - eye).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def export_overlay(sc, cam, info, path, extra):
    bpy.context.view_layer.update()
    out = {"res": RES, "hex": [], "points": {}}
    T = info["T"]
    for (c, r), kind in info["hexes"].items():
        cc = centre(c, r)
        pts = []
        for p in corners(c, r):
            z = T.z_at(p[0], p[1]) if T.xs[0] <= p[0] <= T.xs[-1] and T.ys[0] <= p[1] <= T.ys[-1] else 0
            uv = world_to_camera_view(sc, cam, Vector((p[0], p[1], z + 0.3)))
            pts.append([round(uv.x * RES[0], 1), round((1 - uv.y) * RES[1], 1)])
        zc = T.z_at(cc[0], cc[1])
        uv = world_to_camera_view(sc, cam, Vector((cc[0], cc[1], zc)))
        out["hex"].append({"c": c, "r": r, "kind": kind, "pts": pts,
                           "cx": round(uv.x * RES[0], 1), "cy": round((1 - uv.y) * RES[1], 1)})
    for name, (x, y, z) in extra.items():
        uv = world_to_camera_view(sc, cam, Vector((x, y, z)))
        out["points"][name] = [round(uv.x * RES[0], 1), round((1 - uv.y) * RES[1], 1)]
    with open(path, "w") as f:
        json.dump(out, f, separators=(",", ":"))
    print("wrote", path, flush=True)


def main():
    sc = setup_scene()
    w = args.world
    margin = 60
    step = 0.9 if args.shot != "overview" else 1.1
    if args.quick:
        step *= 2
    x0, x1 = -HEX - margin, COLS * HEX + margin
    y0, y1 = -(ROWS) * ROW - margin, ROW + margin + (380 if args.shot != "overview" else 170)
    T = Terrain(x0, x1, y0, y1, step)
    extra = {}

    if w == "cinder":
        sun_o, sun_dir = sun(3.4, (1.0, 0.74, 0.52), 24, 250, angle=3)
        world_sky((0.02, 0.015, 0.012), (0.32, 0.16, 0.08), (0.05, 0.035, 0.03), 0.9)
        info = world_cinder(T)
    elif w == "veyra":
        sun_o, sun_dir = sun(3.4, (1.0, 0.9, 0.78), 22, 235, angle=1.2)
        world_sky((0.08, 0.06, 0.05), (0.5, 0.44, 0.34), (0.2, 0.25, 0.22), 1.0)
        info = world_veyra(T, sun_dir)
    else:
        sun_o, sun_dir = sun(5.0, (1.0, 0.98, 0.95), 18, 245, angle=0.53)
        world_sky((0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (0.0, 0.0, 0.0), 1.0, stars=1.2)
        info = world_tarsis_a(T)
    info["T"] = T
    alb, rough, maskA = info["terrain"]
    terrain = T.build(alb, rough, maskA, ground_material(w))
    base = info["base"]
    bz = T.z_at(*base)

    if w == "cinder":
        # outpost under a hardened pressure dome; refinery (automated, crewed: false) outside; lava barrier north
        import_glb("game/models/buildings/outpost_l1.glb", (base[0], base[1], bz), 200)
        dome((base[0], base[1], bz), 15.5, 10.5, frame_col=(0.45, 0.42, 0.4), glass=glass_mat("DomeGlassC", (1.0, 0.85, 0.7), 0.1), ribs=2)
        field_bubble((base[0], base[1], bz - 1), 38, 24, (0.45, 0.7, 1.0), 0.5)
        airlock((base[0] + 15.5, base[1] - 3, bz + 1.6), 90)
        ref = centre(10, 10)
        import_glb("game/models/buildings/refinery.glb", (ref[0], ref[1], T.z_at(*ref)), 30)
        rov = centre(9, 11) + (4, 3)
        import_glb("game/models/vehicles/survey_rover_mk2.glb", (rov[0], rov[1], T.z_at(*rov)), 60)
        geo = info["vents"][0] + (14, -10)
        stack((geo[0], geo[1], T.z_at(*geo)), 1.4, 8)
        bar = [centre(5, 9) + (6, 10), centre(6, 8) + (2, -2), centre(7, 8) + (8, -5), centre(8, 8) + (6, -1)]
        barrier(bar, T.z_at)
        for i, (vx, vy) in enumerate(info["vents"]):
            vz = T.z_at(vx, vy)
            cube_volume(f"Plume{i}", (vx + 10, vy + 6, vz + 38), (20, 20, 40),
                        volume_mat(f"PlumeMat{i}", (0.72, 0.7, 0.64), 0.3, 0.06, "plume", emit=(1.0, 0.35, 0.08), emit_strength=0.8), 0.4)
            point((vx, vy, vz + 4), (1.0, 0.4, 0.1), 2500, 4)
        cube_volume("Haze", ((x0 + x1) / 2, (y0 + y1) / 2, 60), ((x1 - x0) / 2, (y1 - y0) / 2, 62),
                    volume_mat("HazeMat", (0.62, 0.55, 0.48), 0.0017))
        flakes(4000 if args.shot != "overview" else 2500, (base[0], base[1] + 40, bz + 30), (260, 240, 60), 0.5, (0.12, 0.11, 0.1))
        extra = {"dome": (base[0], base[1], bz + 11), "refinery": (ref[0], ref[1], T.z_at(*ref) + 9),
                 "barrier": (bar[1][0], bar[1][1], T.z_at(*bar[1]) + 5), "vent0": (info["vents"][0][0], info["vents"][0][1], T.z_at(*info["vents"][0]) + 3),
                 "vent1": (info["vents"][1][0], info["vents"][1][1], T.z_at(*info["vents"][1]) + 3),
                 "rover": (rov[0], rov[1], T.z_at(*rov) + 3), "front": (centre(7, 8)[0] - 2, centre(7, 8)[1] - 2, T.z_at(*centre(7, 8)) + 1),
                 "geotap": (geo[0], geo[1], T.z_at(*geo) + 8)}
        rocks(T, 2200 if not args.quick else 300, info["rock_weight"], (0.4, 1.6), (0.05, 0.045, 0.04), seed=5, name="Basalt")
    elif w == "veyra":
        import_glb("game/models/buildings/outpost_l1.glb", (base[0], base[1], bz), 200)
        ref = centre(8, 10)
        import_glb("game/models/buildings/refinery.glb", (ref[0], ref[1], T.z_at(*ref)), 20)
        cr = centre(7, 8) + (2, 2)
        import_glb("game/models/vehicles/mining_crawler.glb", (cr[0], cr[1], T.z_at(*cr)), 140)
        rov = centre(5, 9) + (6, -2)
        import_glb("game/models/vehicles/survey_rover_mk2.glb", (rov[0], rov[1], T.z_at(*rov)), 35)
        for i, cc in enumerate((centre(11, 6) + (5, 0), centre(3, 5))):
            cz = T.z_at(*cc)
            cube_volume(f"Devil{i}", (cc[0], cc[1], cz + 40), (14, 14, 40),
                        volume_mat(f"DevilMat{i}", (0.75, 0.52, 0.32), 0.9, 0.18, "column"), 0.3 * i)
        bc = info["basin"]
        cube_volume("CO2", (bc[0], bc[1], T.z_at(*bc) + 4), (40, 40, 5), volume_mat("CO2Mat", (0.7, 0.78, 0.62), 0.03))
        cube_volume("Haze", ((x0 + x1) / 2, (y0 + y1) / 2, 60), ((x1 - x0) / 2, (y1 - y0) / 2, 62),
                    volume_mat("HazeMat", (0.85, 0.68, 0.52), 0.0010))
        extra = {"outpost": (base[0], base[1], bz + 10), "refinery": (ref[0], ref[1], T.z_at(*ref) + 9),
                 "crawler": (cr[0], cr[1], T.z_at(*cr) + 4), "rover": (rov[0], rov[1], T.z_at(*rov) + 3),
                 "devil0": (centre(11, 6)[0] + 5, centre(11, 6)[1], T.z_at(*centre(11, 6)) + 20),
                 "basin": (bc[0], bc[1], T.z_at(*bc) + 2), "cap": (centre(9, 1)[0], centre(9, 1)[1], T.z_at(*centre(9, 1)) + 2)}
        rocks(T, 1800 if not args.quick else 300, info["rock_weight"], (0.3, 1.4), (0.2, 0.09, 0.05), seed=6, name="Rubble")
    else:
        import_glb("game/models/buildings/outpost_l1.glb", (base[0], base[1], bz), 200)
        dome((base[0], base[1], bz), 16, 12, glass=glass_mat("DomeGlassT", (0.8, 0.9, 1.0), 0.08), ribs=3)
        field_bubble((base[0], base[1], bz - 1), 38, 24, (0.5, 0.75, 1.0), 0.45)
        airlock((base[0] + 16, base[1] - 3, bz + 1.6), 90)
        ref = centre(9, 10)
        import_glb("game/models/buildings/refinery.glb", (ref[0], ref[1], T.z_at(*ref)), 25)
        con = centre(7, 11) + (8, 6)
        import_glb("game/models/vehicles/construction_rover.glb", (con[0], con[1], T.z_at(*con)), 75)
        rov = centre(5, 9) + (4, 0)
        import_glb("game/models/vehicles/survey_rover_mk3.glb", (rov[0], rov[1], T.z_at(*rov)), 20)
        # Tarsis hangs in the sky in the feed shot; it is far off-frame for the game cameras
        planet_in_sky((0.25, 1.0, 0.13), 4000, 520, sun_dir)
        extra = {"dome": (base[0], base[1], bz + 12), "refinery": (ref[0], ref[1], T.z_at(*ref) + 9),
                 "rover": (rov[0], rov[1], T.z_at(*rov) + 3), "constructor": (con[0], con[1], T.z_at(*con) + 4),
                 "crater": (info["big"][0], info["big"][1], T.z_at(info["big"][0], info["big"][1]) + 6)}
        rocks(T, 2600 if not args.quick else 300, info["rock_weight"], (0.3, 1.8), (0.16, 0.155, 0.15), seed=7, name="Boulders", flatten=0.85)

    look = (base[0] + 20, base[1] + 52, bz)
    if args.shot == "overview":
        cam = game_camera((centre(8, 6)[0] + 10, centre(8, 6)[1] - 10, bz), 290)
    elif args.shot == "game":
        cam = game_camera((base[0] + 6, base[1] + 12, bz), 50 if w != "cinder" else 62)
    else:
        # the outpost's mast camera, low, looking across the base toward the hazards
        tgt = {"cinder": Vector((base[0] - 22, base[1] + 120, bz + 18)),
               "veyra": Vector((base[0] + 60, base[1] + 120, bz + 10)),
               "tarsis_a": Vector((base[0] + 70, base[1] + 220, bz + 75))}[w]
        eye = Vector((base[0] - 34, base[1] - 42, bz + 9))
        cam = place_camera(eye, tgt, 52)
    os.makedirs(args.out, exist_ok=True)
    stem = os.path.join(args.out, f"{w}-{args.shot}")
    if args.shot == "overview":
        export_overlay(sc, cam, info, stem + ".json", extra)
    if args.blend:
        bpy.ops.wm.save_as_mainfile(filepath=stem + ".blend")
    sc.render.filepath = stem + ".png"
    bpy.ops.render.render(write_still=True)
    print("wrote", stem + ".png", flush=True)


main()
