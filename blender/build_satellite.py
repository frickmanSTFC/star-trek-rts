"""Survey satellites, refined (owner 2026-11-10: "The satellites in orbit need to be refined"; contract
docs/satellite-contract.md "Model"). Two original designs, era-matched, one glb each in game/models/orbit/:

  std      survey_satellite.glb          the standard orbital survey probe (Federation, every era but Dawn of Warp):
                                         a sleek lofted sensor bus, light grey hull with panel lines and a thin Owner
                                         line along the dorsal spine, a nose sensor band and an aft drive slit glowing
                                         blue-white, swept collector wings (dark blue cells, glowing leading edge, tip
                                         sensor pods), a deflector-style sensor dish under the belly (concentric rings,
                                         glowing core) facing the planet, a ball-turret camera under the nose, a dorsal
                                         fin with the beacon on top, four RCS glows. 1.85 long, 2.67 across the wings.
  prewarp  survey_satellite_prewarp.glb  Dawn of Warp (2063-2150), Earth-built: a boxy gold-foil bus with white decks,
                                         corner posts and radiators, a Shuttle-tank orange (#C8632A family) payload
                                         adapter, wing yokes and barrel band, black tiles on the nadir deck, two big
                                         rectangular three-panel solar wings, a white telescope/camera barrel hanging
                                         at nadir, a high-gain dish on a mast at zenith, magnetometer boom, star
                                         trackers, red beacon, RCS quads at the corners. 0.78 long, 2.98 across.

Units and axes: 1 Blender unit = 1 game world unit (the orbit globe is R ~100; the contract draws a satellite 2-3
units long, so the glbs are made at that size and need no scale in Godot). Blender +Y = front (Godot -Z, the
direction of travel), +Z = zenith (Godot +Y), -Z = nadir (Godot -Y, the planet). The exporter's +Y up maps Blender
(x, y, z) to Godot (x, z, -y).

PARTS (same names in both glbs; every pivot is unrotated, so Godot angles are applied to an identity rest):
  Body          everything static, joined, origin = the bus centre.
  PanelL/PanelR the solar/collector wings, each one mesh whose origin is its hinge at the bus side. Rest = deployed
                (flat, spanning -X / +X). Fold = rotate about the hinge's front-back axis so the wing swings up to
                stand beside the bus (cells facing inward): Godot PanelR.Basis = Basis(Vector3.Back, +a),
                PanelL.Basis = Basis(Vector3.Back, -a), a = 0 (open) .. 90 deg (folded). Blender: rotation_euler.y =
                -a (R) / +a (L).
  Dish          one mesh, origin on its spin axis; spins about its local +Y in Godot (vertical: RotateY works, the
                rest is identity). std: the belly sensor dish facing nadir. prewarp: the high-gain dish facing zenith.
  Camera        one mesh, origin = the gimbal centre; its lens looks along local -Y in Godot (nadir) at rest. Lens and
                Flash are its children, so aim the Camera node only (lifting Lens/Flash out would leave them behind).
  Lens          child of Camera, material "Lens" (emissive glint).
  Flash         child of Camera, material "Flash" (white). Exported emissive at strength 1 so the importer keeps
                emission on with the right colour: the game sets its energy to 0 at rest and pulses it per picture.
  Beacon        material "Beacon" (std blue-white, prewarp red), blinking.
  Thruster0..3  small RCS glows, own materials Thruster0..3 (0 front-left -X+Y, 1 front-right, 2 rear-left,
                3 rear-right, like the drop ships' legs).
  Material "Owner" = the thin player-colour line (std: both sides of the dorsal spine; prewarp: a band round the top
  of the bus).

Run (bpy 5.x, see CLAUDE.md "Blender here"):
  /home/user/.bpyvenv/bin/python blender/build_satellite.py --game
      both glbs (~15 s): joined per pivot, procedural paints EMIT-baked into one atlas per satellite (--bake-res,
      default 1024, JPEG); prints node count, size, dimensions and pivots in Godot coordinates.
  /home/user/.bpyvenv/bin/python blender/build_satellite.py --preview blender/previews/satellite --samples 48
      Cycles previews over a planet limb: satellite-<design>-{hero,limb,folded,flash}.png, satellite-<design>-glb.png
      (the exported glb posed through its own pivots: deployed + folded, camera tilted, dish turned; needs --game
      first) and satellite-small.png (both glbs at orbit-view size over the planet). ~6 min on 4 cores.
  python3 blender/build_satellite.py --sheet blender/previews/satellite      contact sheet (system python + PIL)
Flags: --designs std,prewarp  --shots hero,limb,folded,flash,glb,small  --res 1200  --samples 32
"""
import math
import os
import sys
import time

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


# ---------------------------------------------------------------- contact sheet (plain python3 + PIL, no bpy)

if "--sheet" in argv:
    from PIL import Image, ImageDraw
    folder = arg("--sheet")
    tw, th = 480, 320
    rows = [[(f"satellite-std-{s}.png", f"standard - {s}") for s in ("hero", "limb", "folded", "flash")],
            [(f"satellite-prewarp-{s}.png", f"pre-warp - {s}") for s in ("hero", "limb", "folded", "flash")],
            [("satellite-std-glb.png", "standard - glb pivots"), ("satellite-prewarp-glb.png", "pre-warp - glb pivots"),
             ("satellite-small.png", "orbit-view size"), ("@zoom", "orbit-view size, 3x pixels")]]
    sheet = Image.new("RGB", (tw * 4, th * len(rows)), (16, 18, 22))
    draw = ImageDraw.Draw(sheet)
    for r, row in enumerate(rows):
        for c, (name, label) in enumerate(row):
            if name == "@zoom":
                src = Image.open(os.path.join(folder, "satellite-small.png")).convert("RGB")
                w, h = src.size
                cw, ch = tw // 3, th // 3
                im = src.crop((w // 2 - cw // 2, h // 2 - ch // 2, w // 2 - cw // 2 + cw, h // 2 - ch // 2 + ch))
                im = im.resize((tw, th), Image.NEAREST)
            else:
                path = os.path.join(folder, name)
                if not os.path.exists(path):
                    continue
                im = Image.open(path).convert("RGB").resize((tw, th), Image.LANCZOS)
            sheet.paste(im, (c * tw, r * th))
            draw.rectangle((c * tw + 8, r * th + 8, c * tw + 16 + 7 * len(label), r * th + 26), fill=(8, 10, 14))
            draw.text((c * tw + 12, r * th + 11), label.upper(), fill=(235, 235, 240))
    out = os.path.join(folder, "satellite-sheet.png")
    sheet.save(out, optimize=True)
    print("wrote", out, sheet.size)
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
GAME_DIR = os.path.join(REPO, "game", "models", "orbit")
GLB = {"std": "survey_satellite.glb", "prewarp": "survey_satellite_prewarp.glb"}

DESIGNS = [s.strip() for s in arg("--designs", "std,prewarp").split(",") if s.strip()]
ALL_SHOTS = ["hero", "limb", "folded", "flash", "glb", "small"]
SHOTS = [s.strip() for s in arg("--shots", ",".join(ALL_SHOTS)).split(",") if s.strip()]
PREVIEW = arg("--preview")
GAME = "--game" in argv
RES = int(arg("--res", "1200"))
SAMPLES = int(arg("--samples", "32"))
BAKE_RES = int(arg("--bake-res", "1024"))
if set(DESIGNS) - set(GLB):
    sys.exit("--designs takes std and prewarp")
if set(SHOTS) - set(ALL_SHOTS):
    sys.exit(f"--shots takes {', '.join(ALL_SHOTS)}")
if not GAME and not PREVIEW:
    sys.exit("nothing to do: pass --game and/or --preview <dir> (or --sheet <dir> with system python)")

PIVOTS = ["PanelL", "PanelR", "Dish", "Camera"]
KEEP = ["Lens", "Flash", "Beacon"] + [f"Thruster{k}" for k in range(4)]
FOLD = 90.0  # degrees: fully folded


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def link(o, parent=None):
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
    return o


def empty(name, loc=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    e.empty_display_size = 0.1
    return link(e, parent)


# ---------------------------------------------------------------- materials

def _n(nt, kind, **inputs):
    n = nt.nodes.new(kind)
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def flat(name, colour, metallic=0.0, rough=0.5, emission=None, strength=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*colour, 1.0)
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = rough
    if emission is not None:
        p.inputs["Emission Color"].default_value = (*emission, 1.0)
        p.inputs["Emission Strength"].default_value = strength
    return m


def node_mat(name, metallic, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = rough
    return m, nt, p, nt.nodes.new("ShaderNodeTexCoord")


def mix(nt, fac, a, b):
    n = nt.nodes.new("ShaderNodeMix")
    n.data_type = "RGBA"
    if isinstance(fac, (int, float)):
        n.inputs[0].default_value = fac
    else:
        nt.links.new(fac, n.inputs[0])
    for idx, v in ((6, a), (7, b)):
        if isinstance(v, tuple):
            n.inputs[idx].default_value = (*v[:3], 1.0)
        else:
            nt.links.new(v, n.inputs[idx])
    return n.outputs[2]


def swizzle(nt, tc, a, b, scale=1.0):
    """Object coords -> (a, b, 0) * scale, a/b = 'X', 'Y', 'Z' or 'XZ' (x + z)."""
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    for key, slot in ((a, "X"), (b, "Y")):
        if len(key) == 1:
            nt.links.new(sep.outputs[key], comb.inputs[slot])
        else:
            add = nt.nodes.new("ShaderNodeMath")
            add.operation = "ADD"
            nt.links.new(sep.outputs[key[0]], add.inputs[0])
            nt.links.new(sep.outputs[key[1]], add.inputs[1])
            nt.links.new(add.outputs[0], comb.inputs[slot])
    vm = nt.nodes.new("ShaderNodeVectorMath")
    vm.operation = "SCALE"
    vm.inputs["Scale"].default_value = scale
    nt.links.new(comb.outputs[0], vm.inputs[0])
    return vm.outputs[0]


def panelled(name, colour, line, metallic=0.15, rough=0.42, scale=4.0, mottle=0.06):
    """Painted hull with recessed panel lines (a brick grid on (y, x + z) object coords) and soft tone mottling."""
    m, nt, p, tc = node_mat(name, metallic, rough)
    br = _n(nt, "ShaderNodeTexBrick", Scale=scale, **{"Mortar Size": 0.012, "Mortar Smooth": 0.2, "Brick Width": 0.9,
                                                       "Row Height": 0.45, "Color1": (*colour, 1),
                                                       "Color2": (*(c * 0.93 for c in colour), 1), "Mortar": (*line, 1),
                                                       "Bias": 0.0})
    br.offset = 0.5
    nt.links.new(swizzle(nt, tc, "Y", "XZ"), br.inputs["Vector"])
    no = _n(nt, "ShaderNodeTexNoise", Scale=9.0, Detail=4.0)
    nt.links.new(tc.outputs["Object"], no.inputs["Vector"])
    col = mix(nt, no.outputs[0], br.outputs["Color"],
              tuple(c * (1 - mottle * 2) for c in colour))
    col2 = mix(nt, br.outputs["Fac"], col, line)
    nt.links.new(col2, p.inputs["Base Color"])
    bump = _n(nt, "ShaderNodeBump", Strength=0.35, Distance=0.004)
    nt.links.new(br.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    return m


def painted(name, colour, metallic=0.05, rough=0.5, mottle=0.08, scale=6.0):
    """Plain paint/foam with gentle mottling (procedural, so it gets baked)."""
    m, nt, p, tc = node_mat(name, metallic, rough)
    no = _n(nt, "ShaderNodeTexNoise", Scale=scale, Detail=5.0, Roughness=0.6)
    nt.links.new(tc.outputs["Object"], no.inputs["Vector"])
    nt.links.new(mix(nt, no.outputs[0], tuple(c * (1 - mottle * 2) for c in colour), colour), p.inputs["Base Color"])
    return m


def foil(name, colour, metallic=0.55, rough=0.32):
    """Crinkled thermal blanket: Voronoi + noise, colour wandering darker; bump only in the previews."""
    m, nt, p, tc = node_mat(name, metallic, rough)
    vo = _n(nt, "ShaderNodeTexVoronoi", Scale=26.0, Randomness=1.0)
    nt.links.new(tc.outputs["Object"], vo.inputs["Vector"])
    no = _n(nt, "ShaderNodeTexNoise", Scale=40.0, Detail=6.0, Roughness=0.6)
    nt.links.new(tc.outputs["Object"], no.inputs["Vector"])
    h = nt.nodes.new("ShaderNodeMath")
    h.operation = "ADD"
    nt.links.new(vo.outputs["Distance"], h.inputs[0])
    nt.links.new(no.outputs[0], h.inputs[1])
    bump = _n(nt, "ShaderNodeBump", Strength=0.4, Distance=0.006)
    nt.links.new(h.outputs[0], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    nt.links.new(mix(nt, no.outputs[0], tuple(c * 0.62 for c in colour), colour), p.inputs["Base Color"])
    return m


def cells(name, cell, line, scale=18.0, metallic=0.35, rough=0.28, axes=("X", "Y")):
    """Solar / collector cells: a fine grid of dark cells with bright bus lines (object coords)."""
    m, nt, p, tc = node_mat(name, metallic, rough)
    br = _n(nt, "ShaderNodeTexBrick", Scale=scale, **{"Mortar Size": 0.04, "Mortar Smooth": 0.0, "Brick Width": 0.5,
                                                       "Row Height": 0.5, "Color1": (*cell, 1),
                                                       "Color2": (*(c * 1.25 for c in cell), 1), "Mortar": (*line, 1),
                                                       "Bias": -0.3})
    br.offset = 0.0
    br.squash = 1.0
    nt.links.new(swizzle(nt, tc, *axes), br.inputs["Vector"])
    nt.links.new(br.outputs["Color"], p.inputs["Base Color"])
    return m


def tiles(name, scale=14.0):
    """Black thermal tiles with light mortar."""
    m, nt, p, tc = node_mat(name, 0.0, 0.85)
    br = _n(nt, "ShaderNodeTexBrick", Scale=scale, **{"Mortar Size": 0.035, "Brick Width": 0.5, "Row Height": 0.5,
                                                       "Color1": (0.04, 0.04, 0.045, 1), "Color2": (0.075, 0.073, 0.07, 1),
                                                       "Mortar": (0.3, 0.3, 0.29, 1), "Bias": -0.2})
    br.offset = 0.5
    nt.links.new(swizzle(nt, tc, "X", "Y"), br.inputs["Vector"])
    nt.links.new(br.outputs["Color"], p.inputs["Base Color"])
    return m


def thruster_mats(colour, strength):
    return [flat(f"Thruster{k}", tuple(c * 0.2 for c in colour), 0.0, 0.35, emission=colour, strength=strength)
            for k in range(4)]


def std_palette():
    blue = (0.55, 0.78, 1.0)
    return {
        "hull": panelled("HullPaint", (0.6, 0.62, 0.66), (0.36, 0.37, 0.4)),
        "dark": flat("HullDark", (0.12, 0.125, 0.14), 0.5, 0.45),
        "trim": flat("Trim", (0.82, 0.83, 0.85), 0.3, 0.35),
        "owner": flat("Owner", (0.72, 0.1, 0.06), 0.1, 0.4),
        "frame": flat("WingFrame", (0.5, 0.52, 0.56), 0.5, 0.38),
        "collector": cells("Collector", (0.02, 0.035, 0.1), (0.2, 0.28, 0.45), 22.0),
        "edge": flat("CollectorGlow", (0.4, 0.6, 0.9), 0.0, 0.3, emission=blue, strength=2.0),
        "tip": flat("TipGlow", (0.5, 0.7, 1.0), 0.0, 0.3, emission=(0.75, 0.88, 1.0), strength=4.0),
        "sensor": flat("SensorGlow", (0.4, 0.6, 0.9), 0.0, 0.3, emission=blue, strength=3.0),
        "drive": flat("DriveGlow", (0.4, 0.6, 0.9), 0.0, 0.3, emission=(0.45, 0.7, 1.0), strength=3.0),
        "plate": flat("DishPlate", (0.42, 0.45, 0.52), 0.75, 0.3),
        "ring": flat("DishRing", (0.78, 0.8, 0.84), 0.85, 0.22),
        "dishglow": flat("DishGlow", (0.5, 0.7, 1.0), 0.0, 0.3, emission=(0.6, 0.82, 1.0), strength=5.0),
        "lens": flat("Lens", (0.05, 0.1, 0.2), 0.2, 0.08, emission=(0.55, 0.8, 1.0), strength=2.0),
        "flash": flat("Flash", (0.85, 0.85, 0.88), 0.0, 0.2, emission=(1.0, 0.97, 0.92), strength=1.0),
        "beacon": flat("Beacon", (0.6, 0.8, 1.0), 0.0, 0.3, emission=(0.65, 0.85, 1.0), strength=6.0),
        "thr": thruster_mats((0.5, 0.75, 1.0), 1.5),
    }


WHITE = (0.82, 0.82, 0.8)
ORANGE = (0.56, 0.13, 0.026)   # linear for sRGB ~#C8632A (Space Shuttle external tank)


def prewarp_palette():
    return {
        "white": painted("WhitePaint", WHITE, 0.05, 0.48, 0.05),
        "foil": foil("FoilGold", (1.0, 0.7, 0.28)),
        "silver": foil("FoilSilver", (0.82, 0.83, 0.86), 0.6, 0.25),
        "orange": painted("FoamOrange", ORANGE, 0.0, 0.8, 0.12, 9.0),
        "tiles": tiles("TileBlack"),
        "radiator": flat("Radiator", (0.88, 0.88, 0.87), 0.15, 0.3),
        "louvre": flat("Louvre", (0.55, 0.56, 0.58), 0.9, 0.25),
        "grey": flat("PaintGrey", (0.5, 0.51, 0.52), 0.3, 0.45),
        "dark": flat("PaintDark", (0.08, 0.08, 0.085), 0.3, 0.6),
        "baffle": flat("Baffle", (0.015, 0.015, 0.016), 0.0, 0.9),
        "steel": flat("Steel", (0.62, 0.63, 0.66), 0.9, 0.3),
        "owner": flat("Owner", (0.72, 0.1, 0.06), 0.1, 0.4),
        "cells": cells("SolarCell", (0.012, 0.02, 0.07), (0.55, 0.57, 0.62), 16.0, 0.4, 0.22),
        "back": flat("PanelBack", (0.7, 0.71, 0.72), 0.2, 0.45),
        "lens": flat("Lens", (0.06, 0.04, 0.14), 0.3, 0.06, emission=(0.6, 0.45, 1.0), strength=1.6),
        "flash": flat("Flash", (0.85, 0.85, 0.88), 0.0, 0.2, emission=(1.0, 0.97, 0.92), strength=1.0),
        "beacon": flat("Beacon", (1.0, 0.2, 0.1), 0.0, 0.3, emission=(1.0, 0.18, 0.08), strength=6.0),
        "thr": thruster_mats((1.0, 0.62, 0.32), 1.0),
    }


# ---------------------------------------------------------------- geometry helpers

def mesh_object(name, bm, mat, parent=None, loc=(0, 0, 0), smooth=False, rot=(0, 0, 0), recalc=True):
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = smooth
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    o.location = loc
    o.rotation_euler = rot
    return link(o, parent)


def bevel(o, width=0.006, segments=2, angle=35.0):
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle)
    return o


def box(name, size, loc, mat, parent=None, bev=0.006, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    o = mesh_object(name, bm, mat, parent, loc, rot=rot)
    if bev > 0:
        bevel(o, bev)
    return o


def cylinder(name, r, depth, loc, mat, parent=None, axis="Z", seg=24, r2=None, bev=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=depth)
    rot = {"X": (0, math.radians(90), 0), "Y": (math.radians(-90), 0, 0), "Z": (0, 0, 0)}[axis]
    o = mesh_object(name, bm, mat, parent, loc, smooth=True, rot=rot)
    if bev > 0:
        bevel(o, bev, 2, 30)
    return o


def sphere(name, r, loc, mat, parent=None, seg=20, rings=10):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
    return mesh_object(name, bm, mat, parent, loc, smooth=True)


def lathe(name, prof, mat, parent=None, loc=(0, 0, 0), seg=32, solid=0.0, smooth=True):
    """Revolve [(r, z)] about local Z (r == 0 = a pole). solid > 0 adds a Solidify modifier (open shells)."""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), z))
                          for i in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1 and len(b) == 1:
                continue
            if len(a) == 1:
                bm.faces.new((a[0], b[j], b[i]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    closed = len(rings[0]) == 1 and len(rings[-1]) == 1
    o = mesh_object(name, bm, mat, parent, loc, smooth=smooth, recalc=closed)
    if solid:
        s = o.modifiers.new("Solid", "SOLIDIFY")
        s.thickness = solid
        s.offset = 0.0
    return o


def ring(name, r_in, r_out, z0, z1, mat, parent=None, loc=(0, 0, 0), seg=32):
    """A closed annulus (washer / tube / band) about local Z, smooth round, sharp at its rims."""
    bm = bmesh.new()
    prof = [(r_in, z0), (r_out, z0), (r_out, z1), (r_in, z1)]
    rings = [[bm.verts.new((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), z))
              for i in range(seg)] for r, z in prof]
    for k in range(4):
        a, b = rings[k], rings[(k + 1) % 4]
        for i in range(seg):
            j = (i + 1) % seg
            f = bm.faces.new((a[i], a[j], b[j], b[i]))
            f.smooth = k in (1, 3)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    o.location = loc
    return link(o, parent)


def prism(name, poly, z0, z1, mat, parent=None, loc=(0, 0, 0), plane="XY", rot=(0, 0, 0), bev=0.0):
    """Extrude a 2D polygon [(u, v)] between z0 and z1. plane 'XY': (u, v) = (x, y), extruded along z;
    'YZ': (u, v) = (y, z), extruded along x; 'XZ': (u, v) = (x, z), extruded along y."""
    def co(u, v, w):
        return {"XY": (u, v, w), "YZ": (w, u, v), "XZ": (u, w, v)}[plane]
    bm = bmesh.new()
    lo = [bm.verts.new(co(u, v, z0)) for u, v in poly]
    hi = [bm.verts.new(co(u, v, z1)) for u, v in poly]
    bm.faces.new(lo)
    bm.faces.new(hi)
    n = len(poly)
    for k in range(n):
        j = (k + 1) % n
        bm.faces.new((lo[k], lo[j], hi[j], hi[k]))
    o = mesh_object(name, bm, mat, parent, loc, rot=rot)
    if bev:
        bevel(o, bev)
    return o


def inset(poly, d):
    """Offset a convex polygon inward by d (edge lines moved in, consecutive lines intersected)."""
    area = sum(poly[k][0] * poly[(k + 1) % len(poly)][1] - poly[(k + 1) % len(poly)][0] * poly[k][1]
               for k in range(len(poly)))
    sgn = 1 if area > 0 else -1
    lines = []
    for k in range(len(poly)):
        a, b = Vector(poly[k]), Vector(poly[(k + 1) % len(poly)])
        e = (b - a).normalized()
        nrm = Vector((-e.y, e.x)) * sgn  # inward for a CCW polygon
        lines.append((a + nrm * d, e))
    out = []
    for k in range(len(lines)):
        p1, d1 = lines[k - 1]
        p2, d2 = lines[k]
        den = d1.x * d2.y - d1.y * d2.x
        t = ((p2.x - p1.x) * d2.y - (p2.y - p1.y) * d2.x) / den
        q = p1 + d1 * t
        out.append((q.x, q.y))
    return out


def catmull(stations, sub=4):
    """Smooth a list of tuples (Catmull-Rom on every component, by index)."""
    pts = [stations[0]] + list(stations) + [stations[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for s in range(sub):
            t = s / sub
            out.append(tuple(0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t
                                    + (-a + 3 * b - 3 * c + d) * t ** 3) for a, b, c, d in zip(p0, p1, p2, p3)))
    out.append(stations[-1])
    return out


def loft(name, stations, mat, parent=None, seg=40, p=2.6, tip=None, smooth=True, cap_back=True, cap_front=True):
    """Superellipse sections [(y, half_width, top, bottom, zc)] in increasing y, joined into a skin. tip = (y, z):
    close the front with a point there, else a flat cap. Cap edges are marked sharp."""
    bm = bmesh.new()
    rings = []
    for y, hw, top, bot, zc in stations:
        rr = []
        for k in range(seg):
            t = 2 * math.pi * k / seg
            c, s = math.cos(t), math.sin(t)
            x = hw * math.copysign(abs(c) ** (2 / p), c)
            z = zc + (top if s >= 0 else bot) * math.copysign(abs(s) ** (2 / p), s)
            rr.append(bm.verts.new((x, y, z)))
        rings.append(rr)
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            j = (k + 1) % seg
            bm.faces.new((a[k], a[j], b[j], b[k]))
    caps = []
    if cap_back:
        caps.append(bm.faces.new(list(reversed(rings[0]))))
    if tip is not None:
        tv = bm.verts.new((0, tip[0], tip[1]))
        for k in range(seg):
            bm.faces.new((rings[-1][k], rings[-1][(k + 1) % seg], tv))
    elif cap_front:
        caps.append(bm.faces.new(rings[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in caps:
        for e in f.edges:
            e.smooth = False
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = smooth and poly.loop_total <= 4
    me.materials.append(mat)
    return link(bpy.data.objects.new(name, me), parent)


def strut(name, a, b, r, mat, parent=None, seg=8):
    a, b = Vector(a), Vector(b)
    d = b - a
    o = cylinder(name, r, d.length, (a + b) / 2, mat, parent, axis="Z", seg=seg)
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return o


# ---------------------------------------------------------------- design: standard survey probe

STD_STATIONS = catmull([
    # y, half width, top, bottom, zc
    (-0.85, 0.205, 0.118, 0.098, 0.0),
    (-0.75, 0.232, 0.132, 0.108, 0.0),
    (-0.50, 0.272, 0.156, 0.12, 0.0),
    (-0.20, 0.29, 0.17, 0.125, 0.0),
    (0.10, 0.284, 0.168, 0.12, 0.0),
    (0.35, 0.258, 0.155, 0.108, 0.0),
    (0.60, 0.205, 0.128, 0.088, -0.004),
    (0.80, 0.13, 0.085, 0.06, -0.01),
    (0.92, 0.062, 0.042, 0.03, -0.014),
], 5)
STD_TIP = (1.0, -0.018)


def hull_at(y):
    st = STD_STATIONS
    if y <= st[0][0]:
        return st[0][1:]
    for a, b in zip(st, st[1:]):
        if a[0] <= y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return tuple(u + (v - u) * t for u, v in zip(a[1:], b[1:]))
    return st[-1][1:]


def std_wing(P, root, side):
    """One swept collector wing; its mesh pieces hang under the hinge empty (PanelR for side +1, PanelL for -1)."""
    s = side
    hinge = empty("PanelR" if s > 0 else "PanelL", (s * 0.318, -0.1, 0.0), root)
    plan = [(0.05, 0.22), (0.05, -0.2), (1.0, -0.52), (1.0, -0.32)]  # root LE, root TE, tip TE, tip LE (x out, y)
    poly = [(s * x, y) for x, y in plan]
    if s < 0:
        poly = list(reversed(poly))
    prism("WingFrame", poly, -0.014, 0.014, P["frame"], hinge, bev=0.004)
    inner = inset(poly, 0.03)
    prism("CollectorTop", inner, 0.013, 0.018, P["collector"], hinge)
    prism("CollectorBot", inner, -0.018, -0.013, P["collector"], hinge)
    # glowing leading edge: a thin strip just ahead of the frame
    le0, le1 = (0.06, 0.22), (0.99, -0.32)
    strip = [(s * le0[0], le0[1] + 0.012), (s * le0[0], le0[1] - 0.012), (s * le1[0], le1[1] - 0.012),
             (s * le1[0], le1[1] + 0.012)]
    prism("LeadingEdge", strip, -0.009, 0.009, P["edge"], hinge)
    # trailing-edge Owner tick near the root (thin)
    cylinder("Knuckle", 0.03, 0.46, (s * 0.022, 0.01, 0.0), P["dark"], hinge, axis="Y", seg=16)
    cylinder("KnuckleCapF", 0.034, 0.03, (s * 0.022, 0.25, 0.0), P["trim"], hinge, axis="Y", seg=16)
    cylinder("KnuckleCapB", 0.034, 0.03, (s * 0.022, -0.23, 0.0), P["trim"], hinge, axis="Y", seg=16)
    # tip sensor pod with a glowing front
    pod = lathe("TipPod", [(0.0, -0.2), (0.016, -0.19), (0.03, -0.15), (0.034, -0.05), (0.034, 0.08), (0.026, 0.16),
                           (0.0, 0.19)], P["trim"], hinge, (s * 1.02, -0.42, 0.0), seg=16)
    pod.rotation_euler = (math.radians(-90), 0, 0)
    sphere("TipGlow", 0.017, (s * 1.02, -0.233, 0.0), P["tip"], hinge, 12, 6)
    return hinge


def std_dish(P, root):
    """Deflector-style sensor dish under the belly, facing the planet; spins about its vertical axis."""
    zb = -hull_at(-0.18)[2]
    d = empty("Dish", (0, -0.18, zb + 0.01), root)
    R = 0.3

    def sag(r):
        return -0.035 - 0.05 * (r / R) ** 2

    lathe("DishShell", [(0.05, sag(0.05)), (0.1, sag(0.1)), (0.16, sag(0.16)), (0.22, sag(0.22)), (0.27, sag(0.27)),
                        (R, sag(R))], P["plate"], d, seg=48, solid=0.012)
    cylinder("DishNeck", 0.07, 0.05, (0, 0, -0.02), P["dark"], d, seg=20)
    for k, r in enumerate((0.11, 0.19, 0.26)):
        ring(f"DishRing{k}", r - 0.008, r + 0.008, sag(r) - 0.016, sag(r) - 0.004, P["ring"], d, seg=48)
    ring("DishRim", R - 0.012, R + 0.008, sag(R) - 0.012, sag(R) + 0.01, P["ring"], d, seg=48)
    ring("DishGlowRing", 0.135, 0.155, sag(0.145) - 0.012, sag(0.145) - 0.006, P["dishglow"], d, seg=48)
    lathe("DishCore", [(0.0, -0.12), (0.03, -0.11), (0.05, -0.075), (0.055, -0.04), (0.0, -0.035)], P["dishglow"], d,
          seg=24)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        c, sn = math.cos(a), math.sin(a)
        strut(f"DishSpoke{k}", (0.055 * c, 0.055 * sn, -0.09), (0.29 * c, 0.29 * sn, sag(0.29) - 0.012), 0.008,
              P["ring"], d)
    # asymmetric emitter block on one spoke so the spin reads
    box("DishEmitter", (0.04, 0.05, 0.02), (0.0, 0.2, sag(0.2) - 0.022), P["dishglow"], d, bev=0.004)
    return d


def std_camera(P, root):
    y = 0.52
    zb = -hull_at(y)[2] + hull_at(y)[3]
    cam = empty("Camera", (0, y, zb - 0.02), root)
    sphere("CamBall", 0.058, (0, 0, 0), P["trim"], cam, 24, 12)
    ring("CamBand", 0.0585, 0.062, -0.008, 0.008, P["dark"], cam, seg=24)
    cylinder("CamBarrel", 0.032, 0.08, (0, 0, -0.07), P["dark"], cam, seg=20)
    lens = cylinder("Lens", 0.025, 0.006, (0, 0, -0.112), P["lens"], cam, seg=20)
    flash = ring("Flash", 0.034, 0.046, -0.104, -0.092, P["flash"], cam, seg=24)
    return cam, lens, flash


def build_std():
    P = std_palette()
    root = empty("SurveySatellite")
    loft("Hull", STD_STATIONS, P["hull"], root, tip=STD_TIP)
    # nose sensor band and aft face
    band = [(y, hw * 1.025, t * 1.03, b * 1.03, zc) for y, hw, t, b, zc in
            [(0.78,) + tuple(hull_at(0.78)), (0.81,) + tuple(hull_at(0.81))]]
    loft("NoseBand", band, P["sensor"], root)
    aft = [(-0.862,) + tuple(c * 0.95 if i < 3 else c for i, c in enumerate(hull_at(-0.85))),
           (-0.846,) + tuple(c * 0.95 if i < 3 else c for i, c in enumerate(hull_at(-0.85)))]
    loft("AftPlate", aft, P["dark"], root)
    box("DriveSlit", (0.16, 0.012, 0.03), (0, -0.866, -0.02), P["drive"], root, bev=0.003)
    # dorsal spine with the thin Owner line along both sides
    def top(y):
        return hull_at(y)[3] + hull_at(y)[1]

    spine = [(y, 0.03, 0.026, 0.03, top(y) - 0.006) for y in [-0.62 + 0.1 * k for k in range(12)]]
    owner = [(y, 0.048, 0.01, 0.02, top(y) - 0.004) for y, *_ in spine]
    loft("Spine", spine, P["trim"], root, seg=24)
    loft("OwnerLine", owner, P["owner"], root, seg=24)
    # dorsal fin with the beacon
    fy0, fy1 = -0.8, -0.42
    fin = [(fy0, top(fy0) - 0.02), (fy1, top(fy1) - 0.02), (-0.62, 0.33), (-0.73, 0.345)]
    prism("Fin", fin, -0.012, 0.012, P["hull"], root, plane="YZ", bev=0.003)
    beacon = sphere("Beacon", 0.022, (0, -0.7, 0.36), P["beacon"], root, 16, 8)
    # wing root fairings (static, on the hull) and the hinge blisters
    for s in (-1, 1):
        cylinder(f"Fairing{s}", 0.045, 0.52, (s * 0.27, -0.09, 0.0), P["hull"], root, axis="Y", seg=20, bev=0.004)
        for y in (-0.3, 0.12):
            box(f"HingeBlock{s}{y}", (0.03, 0.03, 0.05), (s * 0.3, y, 0.0), P["dark"], root)
    # sensor windows (dark strips) on the flanks and a ventral keel
    for s in (-1, 1):
        for k in range(4):
            y = 0.18 + 0.08 * k
            hw, top, bot, zc = hull_at(y)
            box(f"Port{s}{k}", (0.012, 0.05, 0.022), (s * (hw - 0.004), y, 0.03), P["sensor"], root, bev=0.0)
    # RCS thrusters: housings in the hull, the glowing nozzles are the driven parts
    thr = []
    for k, (x, y, z, axis) in enumerate([(-1, 0.42, 0.0, "X"), (1, 0.42, 0.0, "X"), (-1, -0.86, 0.04, "Y"),
                                         (1, -0.86, 0.04, "Y")]):
        if axis == "X":
            hw = hull_at(y)[0]
            box(f"RcsHousing{k}", (0.03, 0.06, 0.05), (x * (hw - 0.006), y, z), P["dark"], root)
            thr.append(cylinder(f"Thruster{k}", 0.018, 0.008, (x * (hw + 0.012), y, z), P["thr"][k], root, axis="X",
                                seg=16))
        else:
            box(f"RcsHousing{k}", (0.05, 0.03, 0.04), (x * 0.13, y + 0.005, z), P["dark"], root)
            thr.append(cylinder(f"Thruster{k}", 0.016, 0.008, (x * 0.13, y - 0.014, z), P["thr"][k], root, axis="Y",
                                seg=16))
    wings = {s: std_wing(P, root, s) for s in (-1, 1)}
    dish = std_dish(P, root)
    cam, lens, flash = std_camera(P, root)
    # camera socket ring on the belly
    y = 0.52
    ring("CamSocket", 0.06, 0.082, -0.006, 0.008, P["dark"], root, (0, y, -hull_at(y)[2] + hull_at(y)[3] + 0.004))
    return {"root": root, "PanelL": wings[-1], "PanelR": wings[1], "Dish": dish, "Camera": cam, "Lens": lens,
            "Flash": flash, "Beacon": beacon, "thrusters": thr, "P": P}


# ---------------------------------------------------------------- design: pre-warp (Dawn of Warp) survey satellite

BUS = (0.72, 0.78, 0.62)  # x, y, z


def pw_wing(P, root, side):
    s = side
    hx = BUS[0] / 2 + 0.03
    hinge = empty("PanelR" if s > 0 else "PanelL", (s * hx, 0.0, 0.06), root)
    cylinder("Knuckle", 0.026, 0.2, (s * 0.02, 0, 0), P["grey"], hinge, axis="Y", seg=14)
    box("HingeDrive", (0.04, 0.08, 0.06), (s * 0.02, 0, 0), P["dark"], hinge)
    # yoke: two orange struts from the knuckle to the first panel, one white cross bar
    for y in (-0.22, 0.22):
        strut(f"Yoke{y}", (s * 0.03, y * 0.25, 0.0), (s * 0.21, y, 0.0), 0.011, P["orange"], hinge)
    strut("YokeBar", (s * 0.21, -0.25, 0.0), (s * 0.21, 0.25, 0.0), 0.012, P["white"], hinge)
    x0, w, gap, chord = 0.215, 0.285, 0.012, 0.62
    for k in range(3):
        a = x0 + k * (w + gap)
        poly = [(s * a, -chord / 2), (s * (a + w), -chord / 2), (s * (a + w), chord / 2), (s * a, chord / 2)]
        if s < 0:
            poly = list(reversed(poly))
        prism(f"Panel{k}", poly, -0.012, 0.009, P["back"], hinge, bev=0.003)
        prism(f"Cells{k}", inset(poly, 0.014), 0.009, 0.0125, P["cells"], hinge)
        if k < 2:
            for y in (-0.2, 0.2):
                box(f"PanelHinge{k}{y}", (gap + 0.012, 0.04, 0.018), (s * (a + w + gap / 2), y, -0.002), P["grey"], hinge,
                    bev=0.0)
    # thin white edge frame along the outer tip
    tip = x0 + 3 * w + 2 * gap
    box("TipRail", (0.012, chord + 0.01, 0.026), (s * (tip + 0.004), 0, -0.002), P["white"], hinge, bev=0.002)
    return hinge


def pw_dish(P, root):
    d = empty("Dish", (0, -0.12, BUS[2] / 2 + 0.33), root)
    R = 0.27

    def para(r):
        return 0.03 + 0.11 * (r / R) ** 2

    lathe("DishShell", [(0.035, para(0.035)), (0.08, para(0.08)), (0.13, para(0.13)), (0.18, para(0.18)),
                        (0.23, para(0.23)), (R, para(R))], P["white"], d, seg=48, solid=0.012)
    ring("DishRim", R - 0.01, R + 0.008, para(R) - 0.01, para(R) + 0.008, P["grey"], d, seg=48)
    lathe("DishBack", [(0.0, -0.01), (0.05, -0.005), (0.09, 0.02), (0.035, 0.03)], P["grey"], d, seg=24)
    for k in range(6):  # back ribs
        a = math.radians(60 * k)
        c, sn = math.cos(a), math.sin(a)
        strut(f"Rib{k}", (0.07 * c, 0.07 * sn, 0.02), (0.25 * c, 0.25 * sn, para(0.25) - 0.012), 0.006, P["grey"], d, 6)
    feed = Vector((0, 0, 0.27))
    for k in range(3):  # feed tripod
        a = math.radians(90 + 120 * k)
        strut(f"FeedLeg{k}", (0.24 * math.cos(a), 0.24 * math.sin(a), para(0.24) + 0.004), feed, 0.006, P["grey"], d, 6)
    lathe("Feed", [(0.0, 0.31), (0.03, 0.305), (0.035, 0.27), (0.022, 0.245), (0.0, 0.24)], P["foil"], d, seg=16)
    box("RimMarker", (0.06, 0.035, 0.022), (0.0, R - 0.005, para(R) + 0.004), P["orange"], d, bev=0.003)
    return d


def pw_camera(P, root):
    """Telescope / camera barrel hanging at nadir from a gimbal inside the orange payload adapter."""
    zb = -BUS[2] / 2
    cam = empty("Camera", (0, 0.04, zb - 0.135), root)
    r = 0.12
    ring("Barrel", r - 0.012, r, -0.62, 0.0, P["white"], cam, seg=32)  # open tube
    lathe("BarrelTop", [(0.0, 0.01), (r, 0.01), (r, -0.01), (0.0, -0.01)], P["white"], cam, seg=32, smooth=False)
    ring("BarrelBand", r - 0.002, r + 0.008, -0.38, -0.32, P["orange"], cam, seg=32)
    ring("Sunshade", r - 0.006, r + 0.016, -0.66, -0.6, P["white"], cam, seg=32)
    ring("Baffle", r - 0.03, r - 0.011, -0.64, -0.1, P["baffle"], cam, seg=32)
    ring("BaffleRing", r - 0.04, r - 0.011, -0.5, -0.49, P["baffle"], cam, seg=32)
    for s in (-1, 1):  # trunnions on the gimbal axis (X)
        cylinder(f"Trunnion{s}", 0.03, 0.04, (s * (r + 0.018), 0, -0.02), P["steel"], cam, axis="X", seg=14)
    box("CamElectronics", (0.07, 0.09, 0.14), (0, -r - 0.03, -0.16), P["foil"], cam, bev=0.004)
    lens = cylinder("Lens", r - 0.032, 0.008, (0, 0, -0.42), P["lens"], cam, seg=32)
    flash = box("Flash", (0.035, 0.07, 0.05), (r + 0.026, 0.0, -0.56), P["flash"], cam, bev=0.006)
    box("FlashHousing", (0.03, 0.084, 0.064), (r + 0.006, 0.0, -0.56), P["dark"], cam, bev=0.004)
    return cam, lens, flash


def build_prewarp():
    P = prewarp_palette()
    root = empty("SurveySatellite")
    bx, by, bz = BUS
    box("Bus", (bx - 0.02, by - 0.02, bz - 0.02), (0, 0, 0), P["foil"], root, bev=0.01)
    box("TopDeck", (bx + 0.02, by + 0.02, 0.035), (0, 0, bz / 2 + 0.0), P["white"], root, bev=0.006)
    box("BottomDeck", (bx + 0.02, by + 0.02, 0.03), (0, 0, -bz / 2), P["white"], root, bev=0.006)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"Post{sx}{sy}", (0.05, 0.05, bz), (sx * bx / 2, sy * by / 2, 0), P["white"], root, bev=0.006)
    # radiators with louvres on the front and back faces
    for sy in (-1, 1):
        box(f"Radiator{sy}", (bx - 0.14, 0.012, 0.36), (0, sy * (by / 2 + 0.002), -0.03), P["radiator"], root, bev=0.003)
        for k in range(6):
            box(f"Louvre{sy}{k}", (0.07, 0.01, 0.3), (-0.21 + 0.084 * k, sy * (by / 2 + 0.01), -0.03), P["louvre"], root,
                bev=0.0)
    # silver foil patch + hinge drive bosses on the wing faces (the gold shows round them)
    for sx in (-1, 1):
        box(f"FoilPatch{sx}", (0.01, by - 0.2, 0.2), (sx * (bx / 2 + 0.0), 0, -0.16), P["silver"], root, bev=0.0)
        box(f"WingBoss{sx}", (0.03, 0.14, 0.1), (sx * (bx / 2 + 0.01), 0, 0.06), P["grey"], root)
    # thin Owner band round the top of the bus
    for sy in (-1, 1):
        box(f"OwnerY{sy}", (bx + 0.012, 0.008, 0.026), (0, sy * (by / 2 + 0.002), bz / 2 - 0.045), P["owner"], root,
            bev=0.0)
    for sx in (-1, 1):
        box(f"OwnerX{sx}", (0.008, by + 0.012, 0.026), (sx * (bx / 2 + 0.002), 0, bz / 2 - 0.045), P["owner"], root,
            bev=0.0)
    # nadir deck: black tiles, orange payload adapter cone, gimbal bearings
    box("TileDeck", (bx - 0.06, by - 0.06, 0.012), (0, 0, -bz / 2 - 0.02), P["tiles"], root, bev=0.0)
    lathe("Adapter", [(0.27, -bz / 2 - 0.02), (0.25, -bz / 2 - 0.08), (0.2, -bz / 2 - 0.13)], P["orange"],
          root, (0, 0.04, 0), seg=40, solid=0.012)
    ring("AdapterLip", 0.19, 0.21, -bz / 2 - 0.14, -bz / 2 - 0.12, P["grey"], root, (0, 0.04, 0), seg=40)
    for s in (-1, 1):
        box(f"Bearing{s}", (0.03, 0.06, 0.07), (s * 0.168, 0.04, -bz / 2 - 0.135), P["steel"], root)
    # zenith deck: dish mast, star trackers, magnetometer boom, whip antennas, sun sensor, beacon
    cylinder("Mast", 0.03, 0.33, (0, -0.12, bz / 2 + 0.165), P["white"], root, seg=16)
    ring("MastBand", 0.03, 0.036, 0.12, 0.17, P["orange"], root, (0, -0.12, bz / 2), seg=16)
    cylinder("MastFoot", 0.07, 0.04, (0, -0.12, bz / 2 + 0.03), P["grey"], root, seg=20)
    for k, (x, y) in enumerate([(0.22, 0.24), (-0.22, 0.24)]):
        box(f"Tracker{k}", (0.07, 0.07, 0.06), (x, y, bz / 2 + 0.05), P["grey"], root)
        cylinder(f"TrackerShade{k}", 0.03, 0.06, (x, y + 0.02, bz / 2 + 0.11), P["baffle"], root, seg=14, r2=0.04)
    strut("MagBoom", (-0.3, -0.33, bz / 2 + 0.02), (-0.58, -0.66, bz / 2 + 0.28), 0.008, P["grey"], root)
    box("Magnetometer", (0.05, 0.05, 0.05), (-0.58, -0.66, bz / 2 + 0.28), P["white"], root, bev=0.006)
    for k, (x, y) in enumerate([(0.3, -0.33), (-0.3, 0.33)]):
        strut(f"Whip{k}", (x, y, -bz / 2), (x * 1.25, y * 1.25, -bz / 2 - 0.38), 0.005, P["steel"], root, 6)
    box("SunSensor", (0.06, 0.04, 0.03), (0.2, -0.3, bz / 2 + 0.03), P["dark"], root)
    beacon = sphere("Beacon", 0.024, (0.3, 0.33, bz / 2 + 0.05), P["beacon"], root, 16, 8)
    cylinder("BeaconBase", 0.026, 0.03, (0.3, 0.33, bz / 2 + 0.025), P["grey"], root, seg=12)
    # RCS quads at the four lower corners; the glowing nozzles are the driven parts (facing out sideways)
    thr = []
    for k, (sx, sy) in enumerate([(-1, 1), (1, 1), (-1, -1), (1, -1)]):
        x, y, z = sx * (bx / 2 + 0.025), sy * (by / 2 - 0.07), -bz / 2 + 0.07
        box(f"RcsBlock{k}", (0.05, 0.08, 0.07), (x, y, z), P["grey"], root)
        cylinder(f"RcsBell{k}", 0.014, 0.03, (x + sx * 0.035, y, z), P["dark"], root, axis="X", seg=12, r2=0.022)
        thr.append(cylinder(f"Thruster{k}", 0.017, 0.006, (x + sx * 0.052, y, z), P["thr"][k], root, axis="X", seg=12))
        cylinder(f"RcsAft{k}", 0.01, 0.024, (x, y + sy * 0.05, z), P["dark"], root, axis="Y", seg=10, r2=0.015)
    wings = {s: pw_wing(P, root, s) for s in (-1, 1)}
    dish = pw_dish(P, root)
    cam, lens, flash = pw_camera(P, root)
    return {"root": root, "PanelL": wings[-1], "PanelR": wings[1], "Dish": dish, "Camera": cam, "Lens": lens,
            "Flash": flash, "Beacon": beacon, "thrusters": thr, "P": P}


BUILDERS = {"std": build_std, "prewarp": build_prewarp}


def set_pose(parts, fold=0.0, cam_tilt=(0.0, 0.0), dish=0.0):
    """fold in degrees (0 open .. 90 folded); cam_tilt = (about X, about Y) degrees; dish spin degrees."""
    parts["PanelR"].rotation_euler = (0, math.radians(-fold), 0)
    parts["PanelL"].rotation_euler = (0, math.radians(fold), 0)
    parts["Camera"].rotation_euler = (math.radians(cam_tilt[0]), math.radians(cam_tilt[1]), 0)
    parts["Dish"].rotation_euler = (0, 0, math.radians(dish))
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- game export

def join_for_game(root):
    """Modifiers applied; one mesh per pivot. The root's own parts -> "Body" (origin at the root); each pivot empty in
    PIVOTS is replaced by the joined mesh of its parts, named like the pivot, same transform; KEEP meshes stay as
    they are (re-parented onto the joined mesh). Every part first gets one UV layer called "UVMap" (the join keeps
    only the active object's layers by name, CLAUDE.md drop ship gotcha)."""
    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    dg = bpy.context.evaluated_depsgraph_get()
    for o in meshes:
        if o.modifiers:
            me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            o.modifiers.clear()
            o.data = me
    for o in meshes:
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        o.data.uv_layers[0].name = "UVMap"
        while len(o.data.uv_layers) > 1:
            o.data.uv_layers.remove(o.data.uv_layers[1])

    def pivot_of(o):
        p = o.parent
        while p is not None and p is not root and p.name not in PIVOTS:
            p = p.parent
        return p or root

    groups = {}
    for o in meshes:
        if o.name not in KEEP:
            groups.setdefault(pivot_of(o), []).append(o)
    for pivot, objs in groups.items():
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
            o.select_set(True)
        target = objs[0]
        bpy.context.view_layer.objects.active = target
        if len(objs) > 1:
            bpy.ops.object.join()
        bpy.context.view_layer.update()
        frame = pivot.matrix_world.copy()
        target.data.transform(frame.inverted() @ target.matrix_world)
        if pivot is root:
            target.parent = root
            target.matrix_parent_inverse = Matrix.Identity(4)
            target.matrix_basis = Matrix.Identity(4)
            target.name = "Body"
        else:
            name = pivot.name
            pivot.name = name + "_pivot"
            target.parent = pivot.parent
            target.matrix_parent_inverse = Matrix.Identity(4)
            target.matrix_basis = pivot.matrix_basis.copy()
            for c in list(pivot.children):
                mw = c.matrix_world.copy()
                c.parent = target
                c.matrix_parent_inverse = Matrix.Identity(4)
                c.matrix_world = mw
            bpy.data.objects.remove(pivot)
            target.name = name
        target.data.name = target.name
    bpy.ops.object.select_all(action="DESELECT")
    for o in root.children_recursive:
        if o.type == "MESH":
            o.data.name = o.name
    names = sorted(o.name for o in root.children_recursive)
    print(f"joined for the game: {len(names) + 1} nodes:", ", ".join(names), flush=True)
    missing = sorted(set(KEEP + PIVOTS + ["Body"]) - set(names))
    if missing:
        sys.exit("missing parts: " + ", ".join(missing))


def _principled(m):
    return m.node_tree.nodes.get("Principled BSDF") if m and m.use_nodes else None


def bake_colours(root, res, image_name):
    """build_dropship_prewarp.py's bake: one shared smart-UV atlas over every joined body, each procedural material's
    own Base Color EMIT-baked into it, then those materials read the atlas (bump/roughness links dropped). Flat
    materials (Owner, lamps, lens, steel...) stay as they are."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 4
    targets = [o for o in root.children_recursive if o.type == "MESH" and o.name not in KEEP]
    mats = {s.material for o in targets for s in o.material_slots if s.material}
    procedural = [m for m in mats if (p := _principled(m)) is not None and p.inputs["Base Color"].is_linked]
    bpy.ops.object.select_all(action="DESELECT")
    for o in targets:
        o.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.003, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    image = bpy.data.images.new(image_name, res, res)
    copies = {}
    for m in mats:
        c = m.copy()
        nt = c.node_tree
        p = nt.nodes.get("Principled BSDF")
        emit = nt.nodes.new("ShaderNodeEmission")
        if p is not None and p.inputs["Base Color"].is_linked:
            nt.links.new(p.inputs["Base Color"].links[0].from_socket, emit.inputs["Color"])
        elif p is not None:
            emit.inputs["Color"].default_value = p.inputs["Base Color"].default_value
        out = nt.nodes.get("Material Output") or nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(emit.outputs[0], out.inputs["Surface"])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        nt.nodes.active = tex
        copies[m] = c
    saved = {}
    for o in targets:
        saved[o] = [s.material for s in o.material_slots]
        for s in o.material_slots:
            if s.material:
                s.material = copies[s.material]
    t = time.time()
    scene.cycles.bake_type = "EMIT"
    bpy.ops.object.bake(type="EMIT", margin=4, use_clear=True)
    print(f"baked the atlas {res} for {len(targets)} bodies, {len(procedural)} procedural materials "
          f"in {time.time() - t:.0f} s", flush=True)
    for o, ms in saved.items():
        for s, m in zip(o.material_slots, ms):
            s.material = m
    for c in copies.values():
        bpy.data.materials.remove(c)
    image.pack()
    for m in procedural:
        nt = m.node_tree
        p = _principled(m)
        for name in ("Base Color", "Normal", "Roughness"):
            inp = p.inputs[name]
            if inp.is_linked:
                nt.links.remove(inp.links[0])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])


def godot(v):
    """Blender (x, y, z) -> Godot (x, z, -y)."""
    return (round(v[0], 3), round(v[2], 3), round(-v[1] + 0.0, 3))


def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs if o.type == "MESH" for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def report(root, path):
    objs = [root] + list(root.children_recursive)
    lo, hi = bounds(objs)
    g_lo, g_hi = godot(lo), godot(hi)
    size = hi - lo
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == "MESH" for p in o.data.polygons)
    print(f"exported {path}: {os.path.getsize(path) / 1e6:.2f} MB, {len(objs)} nodes, {tris} triangles", flush=True)
    print(f"  size (Godot x across, y up, z along): {size.x:.3f} x {size.z:.3f} x {size.y:.3f}; "
          f"Godot box x {g_lo[0]}..{g_hi[0]}, y {g_lo[1]}..{g_hi[1]}, z {g_hi[2]}..{g_lo[2]}", flush=True)
    for o in sorted(objs[1:], key=lambda o: o.name):
        w = o.matrix_world.translation
        par = o.parent.name if o.parent else "-"
        print(f"  {o.name:10s} parent {par:15s} pivot (Godot) {godot(w)}", flush=True)


def export_glb(root, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_lights=False, export_cameras=False, export_image_format="JPEG",
                              export_jpeg_quality=88, export_image_quality=88)
    report(root, path)


def game_export(key):
    t = time.time()
    reset()
    parts = BUILDERS[key]()
    set_pose(parts)
    join_for_game(parts["root"])
    bake_colours(parts["root"], BAKE_RES, f"Satellite{key.capitalize()}Paint")
    export_glb(parts["root"], os.path.join(GAME_DIR, GLB[key]))
    print(f"{key} exported in {time.time() - t:.0f} s", flush=True)


# ---------------------------------------------------------------- preview scene

def planet_material():
    m = bpy.data.materials.new("PlanetSurface")
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    vm = nt.nodes.new("ShaderNodeVectorMath")
    vm.operation = "NORMALIZE"
    nt.links.new(tc.outputs["Object"], vm.inputs[0])
    land = _n(nt, "ShaderNodeTexNoise", Scale=1.6, Detail=10.0, Roughness=0.62)
    nt.links.new(vm.outputs[0], land.inputs["Vector"])
    mask = _n(nt, "ShaderNodeMapRange", **{"From Min": 0.5, "From Max": 0.53})
    nt.links.new(land.outputs[0], mask.inputs[0])
    tone = _n(nt, "ShaderNodeTexNoise", Scale=6.0, Detail=6.0)
    nt.links.new(vm.outputs[0], tone.inputs["Vector"])
    ground = mix(nt, tone.outputs[0], (0.05, 0.09, 0.03), (0.22, 0.16, 0.08))
    sea = mix(nt, land.outputs[0], (0.004, 0.02, 0.07), (0.01, 0.06, 0.12))
    col = mix(nt, mask.outputs[0], sea, ground)
    cl = _n(nt, "ShaderNodeTexNoise", Scale=4.5, Detail=12.0, Roughness=0.6, Distortion=0.6)
    nt.links.new(vm.outputs[0], cl.inputs["Vector"])
    clm = _n(nt, "ShaderNodeMapRange", **{"From Min": 0.52, "From Max": 0.68})
    nt.links.new(cl.outputs[0], clm.inputs[0])
    col = mix(nt, clm.outputs[0], col, (0.8, 0.82, 0.85))
    nt.links.new(col, p.inputs["Base Color"])
    rough = _n(nt, "ShaderNodeMapRange", **{"To Min": 0.35, "To Max": 0.95})
    nt.links.new(mask.outputs[0], rough.inputs[0])
    nt.links.new(rough.outputs[0], p.inputs["Roughness"])
    return m


def atmosphere_material(sun_dir):
    m = bpy.data.materials.new("Atmosphere")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    lw = _n(nt, "ShaderNodeLayerWeight", Blend=0.5)
    pw = nt.nodes.new("ShaderNodeMath")
    pw.operation = "POWER"
    nt.links.new(lw.outputs["Facing"], pw.inputs[0])
    pw.inputs[1].default_value = 3.0
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    dot = nt.nodes.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    nt.links.new(geo.outputs["Normal"], dot.inputs[0])
    dot.inputs[1].default_value = tuple(sun_dir)
    lit = _n(nt, "ShaderNodeMapRange", **{"From Min": -0.35, "From Max": 0.5, "To Min": 0.03, "To Max": 1.0})
    nt.links.new(dot.outputs["Value"], lit.inputs[0])
    amt = nt.nodes.new("ShaderNodeMath")
    amt.operation = "MULTIPLY"
    nt.links.new(pw.outputs[0], amt.inputs[0])
    nt.links.new(lit.outputs[0], amt.inputs[1])
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (0.3, 0.55, 1.0, 1)
    emit.inputs["Strength"].default_value = 2.2
    mixs = nt.nodes.new("ShaderNodeMixShader")
    clamp = nt.nodes.new("ShaderNodeMath")
    clamp.operation = "MINIMUM"
    nt.links.new(amt.outputs[0], clamp.inputs[0])
    clamp.inputs[1].default_value = 1.0
    nt.links.new(clamp.outputs[0], mixs.inputs[0])
    nt.links.new(nt.nodes.new("ShaderNodeBsdfTransparent").outputs[0], mixs.inputs[1])
    nt.links.new(emit.outputs[0], mixs.inputs[2])
    nt.links.new(mixs.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    return m


SUN_DIR = Vector((0.55, 0.75, 0.38)).normalized()  # toward the sun (from the front-right, above)


def setup_world():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = 0.0
    world = bpy.data.worlds.new("Space")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    tc = nt.nodes.new("ShaderNodeTexCoord")
    vo = _n(nt, "ShaderNodeTexVoronoi", Scale=220.0)
    nt.links.new(tc.outputs["Generated"], vo.inputs["Vector"])
    st = _n(nt, "ShaderNodeMapRange", **{"From Min": 0.05, "From Max": 0.0, "To Min": 0.0, "To Max": 1.6})
    nt.links.new(vo.outputs["Distance"], st.inputs[0])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(vo.outputs["Color"], sep.inputs[0])
    keep = nt.nodes.new("ShaderNodeMath")  # one cell in ~7 has a star
    keep.operation = "GREATER_THAN"
    nt.links.new(sep.outputs[0], keep.inputs[0])
    keep.inputs[1].default_value = 0.86
    amt = nt.nodes.new("ShaderNodeMath")
    amt.operation = "MULTIPLY"
    nt.links.new(st.outputs[0], amt.inputs[0])
    nt.links.new(keep.outputs[0], amt.inputs[1])
    bg = nt.nodes["Background"]
    nt.links.new(amt.outputs[0], bg.inputs["Strength"])
    bg.inputs["Color"].default_value = (0.8, 0.85, 1.0, 1)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4.5
    sun.data.angle = math.radians(0.6)
    sun.rotation_euler = (-SUN_DIR).to_track_quat("-Z", "Y").to_euler()
    link(sun)
    fill = bpy.data.objects.new("Earthshine", bpy.data.lights.new("Earthshine", "SUN"))
    fill.data.energy = 0.35
    fill.data.color = (0.55, 0.7, 1.0)
    fill.data.use_shadow = False
    fill.rotation_euler = Vector((0, 0, 1)).to_track_quat("-Z", "Y").to_euler()  # light travelling up = from below
    link(fill)
    cam = bpy.data.objects.new("RenderCam", bpy.data.cameras.new("RenderCam"))
    cam.data.clip_start = 0.05
    cam.data.clip_end = 2000
    link(cam)
    scene.camera = cam
    R = 40.0
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=192, v_segments=96, radius=R)
    planet = mesh_object("Planet", bm, planet_material(), None, smooth=True, recalc=False)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=192, v_segments=96, radius=R * 1.025)
    air = mesh_object("Air", bm, atmosphere_material(SUN_DIR), None, smooth=True, recalc=False)
    air.visible_shadow = False
    return cam, planet, air


def aim(cam, pos, target, lens):
    cam.location = pos
    cam.data.lens = lens
    cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()


def shot(cam, target, az, el, dist, lens):
    """Camera at azimuth az (degrees from +Y toward +X), elevation el, distance dist from target."""
    a, e = math.radians(az), math.radians(el)
    pos = Vector(target) + dist * Vector((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)))
    aim(cam, pos, target, lens)


def place_planet(cam, planet, air, alpha=4.0, roll=12.0, L=95.0):
    """Put the planet behind the subject so its limb crosses the frame alpha degrees below the view centre, tilted by
    roll degrees."""
    R = planet.dimensions.x / 2
    rho = math.asin(R / L)
    m = cam.matrix_world.to_3x3()
    v = -(m @ Vector((0, 0, 1)))
    up = m @ Vector((0, 1, 0))
    right = m @ Vector((1, 0, 0))
    th = rho + math.radians(alpha)
    b = math.radians(roll)
    d = v * math.cos(th) - (up * math.cos(b) + right * math.sin(b)) * math.sin(th)
    planet.location = cam.location + d * L
    air.location = planet.location
    bpy.context.view_layer.update()


def render(path):
    bpy.context.scene.render.filepath = path
    t = time.time()
    bpy.ops.render.render(write_still=True)
    print(f"rendered {path} in {time.time() - t:.0f} s", flush=True)


def flash_on(parts, on):
    p = parts["P"]["flash"].node_tree.nodes["Principled BSDF"]
    p.inputs["Emission Strength"].default_value = 60.0 if on else 0.0
    light = bpy.data.objects.get("FlashLight")
    if on and light is None:
        light = bpy.data.objects.new("FlashLight", bpy.data.lights.new("FlashLight", "POINT"))
        light.data.energy = 6.0
        light.data.shadow_soft_size = 0.02
        light.data.color = (1.0, 0.97, 0.92)
        link(light)
    if light is not None:
        light.hide_render = not on
        light.location = parts["Flash"].matrix_world.translation + Vector((0.0, 0.0, -0.08))


FRAMING = {  # per design: target, (az, el, dist, lens) per shot, planet (alpha, roll)
    "std": {"target": (0, -0.05, 0.0), "hero": (38, 24, 4.6, 50, 5, 12), "limb": (100, -6, 4.6, 50, 3, -6),
            "folded": (-35, 20, 3.6, 50, 6, -10), "flash": (55, -14, 2.0, 50, -9, 8)},
    "prewarp": {"target": (0, 0, -0.08), "hero": (36, 22, 5.0, 50, 6, 12), "limb": (100, -5, 5.0, 50, 3, -6),
                "folded": (-38, 18, 3.8, 50, 6, -10), "flash": (55, -12, 2.6, 50, -9, 8)},
}


def preview_design(key):
    reset()
    cam, planet, air = setup_world()
    parts = BUILDERS[key]()
    fr = FRAMING[key]
    for name in ("hero", "limb", "folded", "flash"):
        if name not in SHOTS:
            continue
        set_pose(parts, fold=FOLD if name == "folded" else 0.0, cam_tilt=(0, 0), dish=0)
        flash_on(parts, name == "flash")
        az, el, dist, lens, alpha, roll = fr[name]
        target = fr["target"] if name != "flash" else tuple(parts["Camera"].matrix_world.translation + Vector((0, 0, -0.1)))
        shot(cam, target, az, el, dist, lens)
        place_planet(cam, planet, air, alpha, roll)
        render(os.path.join(PREVIEW, f"satellite-{key}-{name}.png"))


def import_glb(path, offset):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    holder = bpy.data.objects.new(os.path.basename(path).split(".")[0] + "Holder", None)
    link(holder)
    for o in new:
        if o.parent is None:
            o.parent = holder
    holder.location = offset
    bpy.context.view_layer.update()
    return holder, {o.name.split(".")[0]: o for o in new}


def preview_glb(key):
    path = os.path.join(GAME_DIR, GLB[key])
    if not os.path.exists(path):
        print(f"no {path}: run --game first", flush=True)
        return
    reset()
    cam, planet, air = setup_world()
    gap = 3.4 if key == "prewarp" else 3.0
    a, pa = import_glb(path, (-gap / 2, 0, 0))
    b, pb = import_glb(path, (gap / 2, 0, 0))
    pb["PanelR"].rotation_euler = (0, math.radians(-FOLD), 0)
    pb["PanelL"].rotation_euler = (0, math.radians(FOLD), 0)
    pb["Camera"].rotation_euler = (math.radians(25), 0, 0)
    pb["Dish"].rotation_euler = (0, 0, math.radians(60))
    pa["PanelR"].rotation_euler = (0, math.radians(-40), 0)
    pa["PanelL"].rotation_euler = (0, math.radians(40), 0)
    bpy.context.view_layer.update()
    shot(cam, (0, 0, 0.1), 30, 20, 8.5 if key == "prewarp" else 7.4, 50)
    place_planet(cam, planet, air, 6, 10)
    render(os.path.join(PREVIEW, f"satellite-{key}-glb.png"))


def preview_small():
    paths = [os.path.join(GAME_DIR, GLB[k]) for k in ("std", "prewarp")]
    if not all(os.path.exists(p) for p in paths):
        print("small: run --game first", flush=True)
        return
    reset()
    cam, planet, air = setup_world()
    import_glb(paths[0], (-2.2, 0.0, 0.0))
    import_glb(paths[1], (2.2, 0.0, 0.0))
    # the orbit view: a camera well outside the satellites' track, looking down past them at the day side
    shot(cam, (0, 0, 0), 15, 40, 90, 50)
    place_planet(cam, planet, air, -18, 0, L=150)
    render(os.path.join(PREVIEW, "satellite-small.png"))


def main():
    if GAME:
        for key in DESIGNS:
            game_export(key)
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        for key in DESIGNS:
            if {"hero", "limb", "folded", "flash"} & set(SHOTS):
                preview_design(key)
            if "glb" in SHOTS:
                preview_glb(key)
        if "small" in SHOTS:
            preview_small()


main()
