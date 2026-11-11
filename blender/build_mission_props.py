"""Mission site props (drones / comms / missions contract docs/missions-contract.md section B "Models"): what a mission
site looks like on its found tile. Original designs; ONE glb, game/models/missions/mission_props.glb, root empty
"MissionProps" with one mesh node per prop (each centred on the origin in the file, like ruins.glb / towns.glb):

  probe_wreck    a crashed Earth probe, decades old: a white entry capsule (faded paint, Shuttle-orange band, gold foil
                 torn back, black heat-shield tiles) tipped 28 deg and half buried in its own impact mound, the cracked
                 heat shield lying beside it, a skid trench and a scorched patch behind, a torn orange-and-white chute
                 draped over the ground with its lines, a bent antenna, a solar panel and panel shards strewn about.
                 Child mesh "Beacon" (material "Beacon", red, exported at strength 1; the game blinks it weakly) on a
                 stub on the capsule's top. r ~4.6.
  signal_source  an unknown small transmitter half sunk in rock: a black faceted spindle (material "Obsidian") wedged
                 into a cracked boulder outcrop on a ring of fused glassy ground. Child mesh "Glow" (material "Glow",
                 teal, exported at strength 2: three seam rings and the aperture on its top facet); the game pulses it.
                 r ~3.2.
  photo_point    a survey camera tripod: aluminium legs with orange tape, pan head, white camera body with a black
                 lens, a small solar panel and whip, a status LED (material "Lamp"), an orange hard case cabled to it,
                 a white ground target plate. r ~1.9.
  survey_marker  a planted flag on a steel pole (the flag is material "Owner": the game colours it per player), a
                 concrete benchmark post with a brass disc in a small cairn, four stakes with orange tape round it. r ~2.2.

Each prop sits on a trampled / disturbed "Ground" disc whose skirt sinks to -0.4 (slopes). Materials: "Ground" (dirt,
the game may tint it toward the terrain), "Scorch", "Rock", WhitePaint/FoamOrange/FoilGold/TileBlack/PaintGrey/
PaintDark/Steel (the pre-warp palette), ChuteOrange/ChuteWhite, Obsidian, Concrete, Brass, SolarCell, Owner, Beacon,
Glow, Lamp; procedural paints EMIT-baked into one 1024 JPEG atlas. Frame: Blender +Z up, +Y front (Godot -Z); the props
are not driven by rotation (the game may turn the whole node about Y).

Run (bpy 5.x; CLAUDE.md "Blender here"):
  /home/user/.bpyvenv/bin/python blender/build_mission_props.py --game     glb (~10 s), re-imported + checked
  /home/user/.bpyvenv/bin/python blender/build_mission_props.py --preview blender/previews/missions
        mission-{probe_wreck,signal_source,photo_point,survey_marker,lineup,game,night}.png (from the export)
  python3 blender/build_mission_props.py --sheet blender/previews/missions        contact sheet mission-sheet.png (PIL)
Flags: --res 1080  --samples 32  --bake-res 1024
"""
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


PROPS = ["probe_wreck", "signal_source", "photo_point", "survey_marker"]

if "--sheet" in argv:
    from glb_check import contact_sheet
    folder = arg("--sheet")
    rows = [[(f"mission-{p}.png", p.replace("_", " ")) for p in PROPS[:2]] + [("mission-lineup.png", "lineup, Mk I rover")],
            [(f"mission-{p}.png", p.replace("_", " ")) for p in PROPS[2:]] + [("mission-night.png", "night: beacon + glow")],
            [("mission-game.png", "game camera, zoom 50")]]
    contact_sheet(folder, rows, os.path.join(folder, "mission-sheet.png"), 560, 373)
    sys.exit(0)

import bpy  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import building_kit as K  # noqa: E402  (preview stage, game camera, rover for scale, loader)
from glb_check import join_pivots, verify_glb  # noqa: E402

S = K.load("build_satellite.py", ["--game"])
flat, painted, foil, tiles, cells = S["flat"], S["painted"], S["foil"], S["tiles"], S["cells"]
box, cylinder, sphere, lathe, ring, prism, strut, empty, link = (
    S["box"], S["cylinder"], S["sphere"], S["lathe"], S["ring"], S["prism"], S["strut"], S["empty"], S["link"])
mesh_object = S["mesh_object"]

REPO = os.path.dirname(HERE)
OUT = os.path.join(REPO, "game", "models", "missions", "mission_props.glb")
GAME = "--game" in argv
PREVIEW = arg("--preview")
RES = int(arg("--res", "1080"))
SAMPLES = int(arg("--samples", "32"))
BAKE_RES = int(arg("--bake-res", "1024"))
KEEP = ["Beacon", "Glow"]
REQUIRED = ["MissionProps"] + PROPS + KEEP
if not GAME and not PREVIEW:
    sys.exit("nothing to do: pass --game and/or --preview <dir> (or --sheet <dir> with system python)")

WHITE = (0.74, 0.74, 0.71)          # faded
ORANGE = (0.56, 0.13, 0.026)


def palette():
    return {
        "white": painted("WhitePaint", WHITE, 0.05, 0.6, 0.1, 7.0),
        "orange": painted("FoamOrange", ORANGE, 0.0, 0.8, 0.14, 9.0),
        "foil": foil("FoilGold", (0.85, 0.6, 0.26)),
        "tiles": tiles("TileBlack", 22.0),
        "grey": flat("PaintGrey", (0.5, 0.51, 0.52), 0.3, 0.45),
        "dark": flat("PaintDark", (0.07, 0.07, 0.075), 0.3, 0.6),
        "steel": flat("Steel", (0.62, 0.63, 0.66), 0.9, 0.3),
        "ground": painted("Ground", (0.3, 0.24, 0.17), 0.0, 0.95, 0.2, 3.0),
        "scorch": painted("Scorch", (0.035, 0.03, 0.028), 0.0, 0.95, 0.35, 2.5),
        "rock": painted("Rock", (0.32, 0.3, 0.28), 0.0, 0.85, 0.22, 4.0),
        "chute_o": flat("ChuteOrange", (0.62, 0.2, 0.05), 0.0, 0.85),
        "chute_w": flat("ChuteWhite", (0.72, 0.7, 0.66), 0.0, 0.85),
        "obsidian": flat("Obsidian", (0.02, 0.022, 0.03), 0.6, 0.15),
        "concrete": painted("Concrete", (0.45, 0.44, 0.41), 0.0, 0.85, 0.1, 6.0),
        "brass": flat("Brass", (0.7, 0.52, 0.22), 0.9, 0.3),
        "cells": cells("SolarCell", (0.012, 0.02, 0.07), (0.55, 0.57, 0.62), 16.0, 0.4, 0.22),
        "owner": flat("Owner", (0.72, 0.1, 0.06), 0.1, 0.6),
        "beacon": flat("Beacon", (1.0, 0.2, 0.1), 0.0, 0.3, emission=(1.0, 0.18, 0.08), strength=1.0),
        "glow": flat("Glow", (0.1, 0.6, 0.55), 0.0, 0.3, emission=(0.2, 1.0, 0.85), strength=2.0),
        "lamp": flat("Lamp", (0.3, 1.0, 0.4), 0.0, 0.3, emission=(0.3, 1.0, 0.4), strength=3.0),
    }


# ---------------------------------------------------------------- helpers


def ground_disc(name, rng, r, mat, parent, lift=0.04, segs=20, mound=0.0, rough=0.12):
    """An irregular low disc with a skirt sinking to -0.4 (sits on sloped tiles); `mound` raises the middle."""
    bm = bmesh.new()
    radii = [r * (1 - rough + rough * rng.random()) for _ in range(segs)]
    rings = []
    for f, z in ((1.08, -0.4), (1.0, lift), (0.6, lift + mound * 0.5 + 0.01), (0.25, lift + mound + 0.02)):
        rings.append([bm.verts.new((radii[i] * f * math.cos(2 * math.pi * i / segs),
                                    radii[i] * f * math.sin(2 * math.pi * i / segs), z + 0.015 * rng.random()))
                      for i in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_object(name, bm, mat, parent, smooth=True)
    return o


def blob(name, rng, r, loc, mat, parent, scale=(1, 1, 1), jitter=0.25, subdiv=2):
    """A rock: a jittered icosphere."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    for v in bm.verts:
        v.co *= 1 + jitter * (rng.random() - 0.5)
    for v in bm.verts:
        v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2]))
    o = mesh_object(name, bm, mat, parent, loc, smooth=False)
    o.rotation_euler.z = rng.random() * math.tau
    return o


def flat_patch(name, rng, r, z, mat, parent, segs=24, rough=0.35, xy=(0, 0), stretch=(1, 1), shadow=False):
    """An irregular flat decal patch (scorch, glassy ground) lying at height z."""
    bm = bmesh.new()
    c = bm.verts.new((xy[0], xy[1], z))
    ring_ = [bm.verts.new((xy[0] + stretch[0] * r * (1 - rough * rng.random()) * math.cos(2 * math.pi * i / segs),
                           xy[1] + stretch[1] * r * (1 - rough * rng.random()) * math.sin(2 * math.pi * i / segs), z))
             for i in range(segs)]
    for i in range(segs):
        bm.faces.new((c, ring_[i], ring_[(i + 1) % segs]))
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = shadow
    return o


def cloth(name, nx, ny, sx, sy, height, mats, parent, rng, loc=(0, 0, 0), rot=0.0, tear=0.0, gores=6):
    """A wrinkled sheet nx x ny over sx x sy lying on the ground (height(u, v) -> z), faces split into stripes of two
    materials along u (gores); `tear` drops random edge faces (ragged outline)."""
    bm = bmesh.new()
    vs = [[bm.verts.new((sx * (i / nx - 0.5), sy * (j / ny - 0.5), height(i / nx, j / ny))) for j in range(ny + 1)]
          for i in range(nx + 1)]
    me_faces = []
    for i in range(nx):
        for j in range(ny):
            edge = i in (0, nx - 1) or j in (0, ny - 1)
            if edge and rng.random() < tear:
                continue
            f = bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
            f.material_index = (i * gores // nx) % len(mats)
            me_faces.append(f)
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = True
    o = bpy.data.objects.new(name, me)
    o.location = loc
    o.rotation_euler.z = rot
    return link(o, parent)


# ---------------------------------------------------------------- props


def probe_wreck(P, root, rng):
    ground_disc("Mound", rng, 4.6, P["ground"], root, mound=0.35, rough=0.18)
    # skid trench (scorched) behind the capsule toward -Y, a scorch patch round the impact
    flat_patch("Scorch", rng, 2.3, 0.075, P["scorch"], root, xy=(0.2, -0.5), stretch=(1.0, 1.4))
    flat_patch("Trench", rng, 0.6, 0.08, P["scorch"], root, segs=16, xy=(0.0, -2.8), stretch=(1.0, 2.6), rough=0.2)
    for k in range(5):  # trench lips
        y = -1.6 - 0.6 * k
        for s in (-1, 1):
            blob(f"Lip{k}{s}", rng, 0.32, (s * (0.62 - 0.05 * k), y, 0.05), P["ground"], root, (1.0, 1.6, 0.4), 0.3, 1)
    # the capsule: tipped forward-left and half sunk; a frame carries it
    fr = empty("CapsuleFrame", (0.0, 0.25, 0.0), root)
    fr.rotation_euler = (math.radians(28), math.radians(-12), math.radians(20))
    lathe("Backshell", [(0.86, 0.0), (0.84, 0.08), (0.62, 0.55), (0.42, 0.95), (0.34, 1.05), (0.0, 1.06)],
          P["white"], fr, (0, 0, -0.45), seg=32)
    lathe("ShieldRim", [(0.88, -0.02), (0.9, 0.04), (0.86, 0.06), (0.8, 0.0)], P["dark"], fr, (0, 0, -0.45), seg=32)
    ring("Band", 0.6, 0.67, 0.08, 0.2, P["orange"], fr, (0, 0, 0), seg=32)
    ring("ChuteCan", 0.26, 0.33, 0.6, 0.8, P["grey"], fr, (0, 0, 0), seg=24)
    for k in range(3):  # torn gold foil patches round the shell, one peeled back
        a = math.radians(40 + 115 * k)
        n = Vector((math.cos(a), math.sin(a), 0))
        p = box(f"Foil{k}", (0.03, 0.34, 0.28), n * 0.56 + Vector((0, 0, -0.02)), P["foil"], fr, bev=0.0,
                rot=(0, math.radians(-26), a))
        if k == 2:
            p.rotation_euler = (math.radians(30), math.radians(-60), a)
    for k in range(3):  # hatch, RCS ports
        a = math.radians(-60 + 30 * k)
        box(f"Port{k}", (0.03, 0.08, 0.08), Vector((math.cos(a), math.sin(a), 0)) * 0.5 + Vector((0, 0, 0.3)),
            P["dark"], fr, bev=0.0, rot=(0, math.radians(-30), a))
    strut("BentAntenna", (0.18, 0.12, 0.6), (0.5, 0.35, 1.0), 0.012, P["steel"], fr, 6)
    strut("BentAntennaTip", (0.5, 0.35, 1.0), (0.95, 0.2, 0.92), 0.01, P["steel"], fr, 6)
    cylinder("BeaconStub", 0.04, 0.12, (-0.12, 0.0, 0.66), P["grey"], fr, seg=10)
    sphere("Beacon", 0.055, (-0.12, 0.0, 0.75), P["beacon"], fr, 12, 6)
    # the cracked heat shield beside it, half on edge
    sh = empty("ShieldFrame", (-1.75, 0.9, 0.12), root)
    sh.rotation_euler = (math.radians(-62), math.radians(8), math.radians(-35))
    lathe("Shield", [(0.0, 0.18), (0.4, 0.14), (0.7, 0.06), (0.8, 0.0), (0.0, 0.0)], P["tiles"], sh, seg=28)
    box("ShieldCrack", (0.04, 1.4, 0.24), (0.25, 0.0, 0.08), P["scorch"], sh, bev=0.0)
    # torn chute over the ground toward +X, its lines back to the capsule
    def lie(u, v):
        return 0.07 + 0.11 * math.sin(u * 9.0 + v * 2.0) ** 2 + 0.16 * max(0.0, 1 - 2.2 * u) * math.sin(v * math.pi)
    cloth("Chute", 14, 10, 3.0, 2.4, lie, [P["chute_o"], P["chute_w"]], root, rng, loc=(2.35, 0.95, 0.0),
          rot=math.radians(-14), tear=0.45, gores=7)
    cap = fr.matrix_basis @ Vector((0.0, 0.0, 0.72))
    bpy.context.view_layer.update()
    for k in range(6):
        y = -0.95 + 0.38 * k
        end = Vector((2.35 - 1.5 * math.cos(math.radians(14)) + 0.12, 0.95 + y + 0.36, 0.1))
        strut(f"Line{k}", cap + Vector((0.1, 0.0, 0.0)), end, 0.008, P["chute_w"], root, 4)
    # debris: a solar panel, panel shards, a strut
    sp = empty("PanelFrame", (-0.9, -2.6, 0.1), root)
    sp.rotation_euler = (math.radians(9), math.radians(-14), math.radians(35))
    box("PanelBack", (0.9, 0.55, 0.03), (0, 0, 0), P["grey"], sp, bev=0.0)
    box("PanelCells", (0.84, 0.49, 0.01), (0, 0, 0.02), P["cells"], sp, bev=0.0)
    for k in range(6):
        a = rng.random() * math.tau
        d = 1.6 + 2.2 * rng.random()
        box(f"Shard{k}", (0.2 + 0.25 * rng.random(), 0.12 + 0.2 * rng.random(), 0.025), (d * math.cos(a), d * math.sin(a), 0.07),
            [P["white"], P["foil"], P["grey"]][k % 3], root, bev=0.0,
            rot=(rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), rng.random() * math.tau))
    strut("Strut", (-2.6, -0.9, 0.08), (-1.7, -1.6, 0.16), 0.03, P["grey"], root, 8)
    for k in range(4):
        a = rng.random() * math.tau
        blob(f"Clod{k}", rng, 0.18 + 0.12 * rng.random(), (1.4 * math.cos(a), 1.4 * math.sin(a), 0.12), P["ground"], root,
             (1.2, 1.0, 0.6), 0.35, 1)


def signal_source(P, root, rng):
    ground_disc("Ground", rng, 3.2, P["ground"], root, mound=0.1)
    flat_patch("Glass", rng, 2.0, 0.065, P["obsidian"], root, segs=28, rough=0.3)
    for k in range(7):  # cracks of hot glass radiating out
        a = math.tau * k / 7 + rng.random() * 0.4
        flat_patch(f"Crack{k}", rng, 0.12, 0.07, P["scorch"], root, segs=6, rough=0.1,
                   xy=(1.6 * math.cos(a), 1.6 * math.sin(a)), stretch=(1.0, 1.0))
    # the outcrop: one big split boulder, smaller ones round it
    blob("Boulder", rng, 1.0, (0.15, 0.2, 0.35), P["rock"], root, (1.3, 1.05, 0.85), 0.3, 2)
    blob("BoulderB", rng, 0.6, (-0.95, 0.5, 0.2), P["rock"], root, (1.0, 1.2, 0.8), 0.35, 2)
    for k in range(5):
        a = math.radians(200 + 35 * k)
        blob(f"Stone{k}", rng, 0.18 + 0.15 * rng.random(), (1.7 * math.cos(a), 1.7 * math.sin(a), 0.1), P["rock"],
             root, (1, 1, 0.7), 0.35, 1)
    # the transmitter: a faceted spindle wedged into the boulder's front, leaning out
    t = empty("Spindle", (0.1, -0.35, 0.55), root)
    t.rotation_euler = (math.radians(-24), math.radians(10), math.radians(15))
    prof = [(0.0, -0.75), (0.12, -0.6), (0.24, -0.3), (0.28, 0.0), (0.24, 0.35), (0.14, 0.62), (0.06, 0.72), (0.0, 0.74)]
    lathe("Spindle", prof, P["obsidian"], t, seg=6, smooth=False)
    # glowing seams + aperture: one mesh "Glow"
    bm = bmesh.new()
    for z, r in ((-0.25, 0.258), (0.05, 0.283), (0.32, 0.25)):
        bmesh.ops.create_cone(bm, cap_ends=False, segments=6, radius1=r * 1.12, radius2=r * 1.12, depth=0.03,
                              matrix=Matrix.Translation((0, 0, z)))
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.06, radius2=0.05, depth=0.03,
                          matrix=Matrix.Translation((0, 0, 0.72)))
    g = mesh_object("Glow", bm, P["glow"], t, smooth=False)
    g.visible_shadow = False
    # a couple of thin metal fins out of the rock beside it (part of the thing, half buried)
    for k, (x, y, yaw) in enumerate([(0.75, -0.1, 30), (-0.45, -0.25, -40)]):
        box(f"Fin{k}", (0.03, 0.42, 0.3), (x, y, 0.28), P["obsidian"], root, bev=0.0,
            rot=(math.radians(-20), 0, math.radians(yaw)))


def photo_point(P, root, rng):
    ground_disc("Ground", rng, 1.9, P["ground"], root, lift=0.03, rough=0.1)
    # white target plate with an orange cross
    cylinder("Plate", 0.42, 0.02, (0, 0, 0.05), P["white"], root, seg=24)
    box("PlateCrossA", (0.7, 0.06, 0.006), (0, 0, 0.062), P["orange"], root, bev=0.0)
    box("PlateCrossB", (0.06, 0.7, 0.006), (0, 0, 0.062), P["orange"], root, bev=0.0)
    head = Vector((0.0, 0.0, 1.42))
    for k in range(3):
        a = math.radians(90 + 120 * k)
        foot = Vector((0.62 * math.cos(a), 0.62 * math.sin(a), 0.05))
        strut(f"Leg{k}", head - Vector((0, 0, 0.06)), foot, 0.022, P["steel"], root, 8)
        strut(f"LegTape{k}", head.lerp(foot, 0.45), head.lerp(foot, 0.52), 0.025, P["orange"], root, 8)
        strut(f"Foot{k}", foot, foot + Vector((0, 0, -0.08)), 0.018, P["dark"], root, 6)
    cylinder("Hub", 0.08, 0.08, head, P["dark"], root, seg=16)
    cylinder("Pan", 0.06, 0.1, head + Vector((0, 0, 0.09)), P["grey"], root, seg=16)
    box("Body", (0.22, 0.3, 0.2), head + Vector((0, 0.02, 0.25)), P["white"], root, bev=0.012)
    box("BodyBand", (0.225, 0.04, 0.205), head + Vector((0, -0.06, 0.25)), P["orange"], root, bev=0.0)
    cylinder("Lens", 0.07, 0.14, head + Vector((0, 0.23, 0.25)), P["dark"], root, axis="Y", seg=18)
    cylinder("LensGlass", 0.055, 0.01, head + Vector((0, 0.305, 0.25)), P["obsidian"], root, axis="Y", seg=18)
    sphere("Led", 0.016, head + Vector((0.08, -0.15, 0.32)), P["lamp"], root, 8, 4)
    strut("PanelMast", head + Vector((-0.12, -0.1, 0.1)), head + Vector((-0.25, -0.25, 0.55)), 0.012, P["steel"], root)
    pf = empty("SolarFrame", head + Vector((-0.25, -0.25, 0.58)), root)
    pf.rotation_euler = (math.radians(-35), 0, math.radians(30))
    box("Solar", (0.36, 0.26, 0.02), (0, 0, 0), P["grey"], pf, bev=0.0)
    box("SolarCells", (0.33, 0.23, 0.008), (0, 0, 0.012), P["cells"], pf, bev=0.0)
    strut("Whip", head + Vector((0.09, -0.12, 0.35)), head + Vector((0.12, -0.15, 1.05)), 0.005, P["steel"], root, 6)
    # hard case on the ground, cable up a leg
    box("Case", (0.5, 0.34, 0.24), (0.9, -0.55, 0.16), P["orange"], root, bev=0.02, rot=(0, 0, math.radians(20)))
    box("CaseLid", (0.52, 0.36, 0.04), (0.9, -0.55, 0.28), P["dark"], root, bev=0.0, rot=(0, 0, math.radians(20)))
    K.sagging("Cable", Vector((0.7, -0.45, 0.2)), Vector((0.28, -0.12, 0.6)), 0.15, 0.012, P["dark"], root, n=6, segs=5)


def survey_marker(P, root, rng):
    ground_disc("Ground", rng, 2.2, P["ground"], root, lift=0.03, rough=0.12)
    # cairn with the benchmark post and its brass disc
    for k in range(9):
        a = math.tau * k / 9
        blob(f"Cairn{k}", rng, 0.17, (0.32 * math.cos(a), 0.32 * math.sin(a), 0.1), P["rock"], root, (1, 1, 0.8), 0.3, 1)
    for k in range(4):
        a = math.tau * k / 4 + 0.4
        blob(f"CairnTop{k}", rng, 0.13, (0.18 * math.cos(a), 0.18 * math.sin(a), 0.28), P["rock"], root, (1, 1, 0.8), 0.3, 1)
    box("Post", (0.2, 0.2, 0.7), (0, 0, 0.35), P["concrete"], root, bev=0.015)
    cylinder("Disc", 0.07, 0.02, (0, 0, 0.71), P["brass"], root, seg=16)
    box("PostBand", (0.205, 0.205, 0.06), (0, 0, 0.55), P["orange"], root, bev=0.0)
    # the flag pole and flag (Owner colour)
    px, py = 0.45, 0.25
    cylinder("Pole", 0.025, 2.5, (px, py, 1.2), P["steel"], root, seg=10)
    sphere("Finial", 0.04, (px, py, 2.47), P["brass"], root, 10, 5)
    bm = bmesh.new()
    nx, ny, w, h = 10, 4, 0.95, 0.6
    vs = [[bm.verts.new((px + 0.03 + w * i / nx, py + 0.07 * math.sin(i / nx * 5.0) * (i / nx), 2.35 - h * j / ny - 0.04 * (i / nx) ** 2))
           for j in range(ny + 1)] for i in range(nx + 1)]
    for i in range(nx):
        for j in range(ny):
            bm.faces.new((vs[i][j], vs[i][j + 1], vs[i + 1][j + 1], vs[i + 1][j]))
    flag = mesh_object("Flag", bm, P["owner"], root, smooth=True)
    box("FlagHoist", (0.03, 0.03, h + 0.04), (px + 0.03, py, 2.35 - h / 2), P["chute_w"], root, bev=0.0)
    # four stakes with orange tape round the marker
    st = []
    for k in range(4):
        a = math.radians(45 + 90 * k)
        p = Vector((1.5 * math.cos(a), 1.5 * math.sin(a), 0))
        cylinder(f"Stake{k}", 0.025, 0.75, p + Vector((0, 0, 0.35)), P["white"], root, seg=6)
        cylinder(f"StakeTop{k}", 0.028, 0.08, p + Vector((0, 0, 0.68)), P["orange"], root, seg=6)
        st.append(p)
    for k in range(4):
        a, b = st[k] + Vector((0, 0, 0.62)), st[(k + 1) % 4] + Vector((0, 0, 0.62))
        K.sagging(f"Tape{k}", a, b, 0.1, 0.012, P["orange"], root, n=6, segs=4)
    box("Tag", (0.12, 0.01, 0.16), (0.0, -0.105, 0.42), P["chute_w"], root, bev=0.0)
    return flag


BUILD = {"probe_wreck": probe_wreck, "signal_source": signal_source, "photo_point": photo_point,
         "survey_marker": survey_marker}


def apply_scales(root):
    for o in root.children_recursive:
        if o.type == "MESH" and tuple(o.scale) != (1.0, 1.0, 1.0):
            o.data.transform(Matrix.Diagonal((*o.scale, 1.0)))
            o.scale = (1, 1, 1)


def build_all():
    S["reset"]()
    P = palette()
    master = empty("MissionProps")
    for k, name in enumerate(PROPS):
        rng = random.Random(1701 + 31 * k)
        pr = empty(name + "_root", (0, 0, 0), master)
        BUILD[name](P, pr, rng)
        bpy.context.view_layer.update()
        apply_scales(pr)
        keep = [o.name for o in pr.children_recursive if o.name in KEEP]
        join_pivots(pr, [], keep, body=name)
        body = bpy.data.objects[name]
        body.parent = master
        for kn in keep:  # the glowing child: identity rotation, origin where it was, parented to the prop
            o = bpy.data.objects[kn]
            mw = o.matrix_world.copy()
            o.parent = body
            o.matrix_parent_inverse = Matrix.Identity(4)
            loc, rot, _ = mw.decompose()
            o.data.transform(Matrix.Translation(-loc) @ mw)
            o.matrix_world = Matrix.Translation(loc)
        for e in [c for c in pr.children_recursive if c.type == "EMPTY"]:
            bpy.data.objects.remove(e)
        bpy.data.objects.remove(pr)
    bpy.context.view_layer.update()
    return master, P


def game_export():
    master, _ = build_all()
    S["KEEP"] = KEEP
    S["bake_colours"](master, BAKE_RES, "MissionPropsPaint")
    S["export_glb"](master, OUT)
    verify_glb(OUT, REQUIRED)


# ---------------------------------------------------------------- previews (from the exported glb)

SPACING = 11.0


def import_props(offset_each=True):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=OUT)
    new = {o.name.split(".")[0]: o for o in bpy.data.objects if o not in before}
    if offset_each:
        for k, name in enumerate(PROPS):
            new[name].location = ((k - 1.5) * SPACING, 0, 0)
    bpy.context.view_layer.update()
    return new


def preview():
    if not os.path.exists(OUT):
        print(f"no {OUT}: run --game first", flush=True)
        return
    os.makedirs(PREVIEW, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    K.OP["materials"]()
    K.OP["stage"]()
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.clip_end = 2000
    link(cam)
    scene.camera = cam
    parts = import_props()
    for name in PROPS:  # the glow bits read brighter in the previews
        for o in [parts[name]] + list(parts[name].children_recursive):
            for s in o.material_slots:
                p = s.material.node_tree.nodes.get("Principled BSDF") if s.material else None
                if p is not None and s.material.name.split(".")[0] in ("Beacon", "Glow"):
                    p.inputs["Emission Strength"].default_value = 8.0
    rover = K.BV["rover_mk1"](K.BV["palette"]())
    rover.location = (-0.5 * SPACING, -7.5, 0)
    rover.rotation_euler.z = math.radians(160)

    def shoot(name, pos, target, lens=40, w=RES, h=None, angle_y=None):
        scene.render.resolution_x = w
        scene.render.resolution_y = h or int(w * 2 / 3)
        cam.data.sensor_fit = "AUTO"
        cam.data.lens = lens
        if angle_y:
            cam.data.sensor_fit = "VERTICAL"
            cam.data.angle_y = math.radians(angle_y)
        cam.location = pos
        cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(PREVIEW, f"mission-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath, flush=True)

    views = {"probe_wreck": (7.5, -8.0, 5.6, 0.6, 35), "signal_source": (5.0, -6.0, 3.6, 0.6, 40),
             "photo_point": (3.0, -3.8, 2.4, 0.8, 40), "survey_marker": (3.4, -4.2, 2.6, 0.8, 40)}
    for k, name in enumerate(PROPS):
        x0 = (k - 1.5) * SPACING
        dx, dy, dz, tz, lens = views[name]
        shoot(name, (x0 + dx, dy, dz), (x0, 0, tz), lens)
    shoot("lineup", (4.0, -34.0, 15.0), (0, -1.5, 0.5), 38, h=int(RES * 0.45))
    from building_kit import game_cam
    pos, target = game_cam(50.0, (-0.5 * SPACING, -2.0, 0.5))
    shoot("game", tuple(pos), tuple(target), angle_y=45, h=int(RES * 9 / 16))
    sun = bpy.data.objects["Sun"]
    sun.hide_render = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.01
    scene.view_settings.exposure = 0.4
    for name, (x, y, z) in (("NightA", (-1.5 * SPACING - 3, -3, 4)), ("NightB", (-0.5 * SPACING - 3, -3, 3))):
        lt = bpy.data.objects.new(name, bpy.data.lights.new(name, "POINT"))
        lt.data.energy = 25
        lt.data.color = (0.6, 0.7, 1.0)
        lt.location = (x, y, z)
        link(lt)
    shoot("night", (-SPACING + 1.0, -11.0, 5.5), (-SPACING, 0, 0.6), 30, h=int(RES * 0.5))


def main():
    if GAME:
        game_export()
    if PREVIEW:
        preview()


main()
