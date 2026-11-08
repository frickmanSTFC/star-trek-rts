"""Federation Pressure Dome (`pressure_dome`), game-ready (hostile worlds slice, 2026-11-08; contract
docs/hostile-worlds-contract.md).

A sealed, heated geodesic glass dome for thin cold air (Veyra), as the RTS camera sees it from the front:
  * a concrete apron and a raised RING FOUNDATION (owner band, frost on the lip, a glowing field strip);
  * inside: a deck with two WALKWAYS from the airlocks to a round PLAZA, a ring path, raised PLANTER beds with crops,
    a few small trees, benches, an air handler, lamp posts and bollard lights;
  * the geodesic DOME FRAME (a frequency-3 icosphere cut at the equator: struts + hubs, a crown with the beacon mast);
  * the DOME GLASS (see-through, light blue tint; the lowest row of panels frosted);
  * two AIRLOCK vestibules (front and back-left) with lit doors; three FIELD EMITTER pylons round the rim (they throw the
    2-tile containment field, which the view draws as a shader shell); a heat exchanger outside feeding the ring.

Frame and conventions (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit. Glass radius
7.0 on the ring top (z 0.9), crown at 6.8, beacon at ~7.5; airlocks and emitters reach r 8.4 (footprint radius ~8).

BUILD PIECES (Build0..Build8): Foundation, Deck, Planters, Utilities, Frame, Airlocks, Emitters, Glass, Lights.

GAME CONTRACT (BuildingModels.Hostile.cs; names from docs/hostile-worlds-contract.md):
  DomeGlass        mesh (separate node): surfaces "Glass" (see-through, alpha ~0.16) and "GlassFrost" (base row).
  DomeFrame        pivot holding the joined frame (struts, hubs, crown, base hoop).
  Airlock0, Airlock1   vestibule pivots (front, back-left); their local -Y (Godot +Z) points out of the dome.
                   Airlock<k>Door = the outer door leaf (separate mesh; slides along its local X to open).
                   Material "DoorLight" = the lit door frames (amber-white; the view may cycle it).
  FieldEmitter0..2 pylon pivots round the rim (material "Field" = their glowing heads and the ring strip).
  Beacon           mesh on the crown mast (material "Beacon").
  DomeLight        empty inside the dome (for an interior OmniLight). SparkPoint = sparks when broken.
  SignFront        decal marker on Airlock0's fascia. RoofSpot0..1 (airlock roofs), AddOnSpot0..4 variety spots.
  Lamp, Window, Interior, GrowLight   other glow materials.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_pressure_dome.py --game          (export game/models/buildings/pressure_dome.glb)
  /home/user/.bpyvenv/bin/python blender/build_pressure_dome.py --game --preview blender/previews/hostile --shots hero,game,close,night,pieces
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, lathe, empty, hoop  # noqa: E402
from mathutils import Vector  # noqa: E402
import bmesh  # noqa: E402

CFG = K.setup("pressure_dome")

APRON_R = 8.0
RING_IN, RING_OUT, RING_Z = 6.75, 7.45, 0.9
DECK_Z = 0.32
DOME_R, DOME_H = 7.0, 5.9           # glass: radius at the base, height above the ring top
AIRLOCKS = (-90.0, 150.0)            # angles (deg, 0 = +X, 90 = back)
EMITTERS = (90.0, 210.0, -30.0)
HEATER = 30.0
LOCK_R = 7.15                        # airlock pivot radius; its outer face at LOCK_R + 1.25
WALK_W = 1.4
PLAZA_R = 1.55
FROST_Z = 0.9 + 0.55               # frosted glass below this height
PATH_R0, PATH_R1 = 3.7, 4.4
PIECES = ["Foundation", "Deck", "Planters", "Utilities", "Frame", "Airlocks", "Emitters", "Glass", "Lights"]


def materials():
    K.materials()
    # the dome glass: the kit's "Glass" made see-through (light blue tint, glossy, coat), exported with alpha (BLEND)
    g = MT["glass"]
    p = g.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (0.42, 0.55, 0.66, 1.0)
    p.inputs["Metallic"].default_value = 0.0
    p.inputs["Roughness"].default_value = 0.03
    p.inputs["Coat Weight"].default_value = 0.0
    p.inputs["Alpha"].default_value = 0.14
    p.inputs["Emission Color"].default_value = (0.55, 0.75, 1.0, 1.0)
    p.inputs["Emission Strength"].default_value = 0.04
    g["colour"] = (0.42, 0.55, 0.66)
    frost = K.mat("frostglass", "GlassFrost", (0.88, 0.94, 1.0), 0.0, 0.55, emission=(0.6, 0.75, 0.9), strength=0.05)
    frost.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value = 0.5
    for m in (g, frost):
        try:
            m.surface_render_method = "BLENDED"
        except AttributeError:
            m.blend_method = "BLEND"
        m.use_backface_culling = True
    K.mat("frost", "Frost", (0.86, 0.9, 0.95), 0.0, 0.75, textured=True)
    K.mat("frame", "Frame", (0.34, 0.36, 0.4), 0.3, 0.5, textured=True)
    K.mat("deck", "Deck", (0.5, 0.52, 0.55), 0.15, 0.6, textured=True)
    K.mat("walk", "Walkway", (0.72, 0.71, 0.68), 0.0, 0.7, textured=True)
    K.mat("planter", "Planter", (0.3, 0.33, 0.34), 0.2, 0.55, textured=True)
    K.mat("soil", "Soil", (0.16, 0.11, 0.07), 0.0, 0.95)
    K.mat("leaf", "Leaf", (0.18, 0.42, 0.15), 0.0, 0.7)
    K.mat("leaf2", "Leaf2", (0.32, 0.5, 0.16), 0.0, 0.7)
    K.mat("bloom", "Bloom", (0.85, 0.6, 0.2), 0.0, 0.6)
    K.mat("bark", "Bark", (0.25, 0.17, 0.11), 0.0, 0.85)
    K.mat("field", "Field", (0.3, 0.7, 1.0), 0.0, 0.25, emission=(0.35, 0.78, 1.0), strength=5.0)
    K.mat("doorlight", "DoorLight", (1.0, 0.85, 0.6), 0.0, 0.3, emission=(1.0, 0.8, 0.5), strength=4.0)
    K.mat("door", "DoorPanel", (0.62, 0.65, 0.7), 0.4, 0.4)
    K.mat("grow", "GrowLight", (1.0, 0.5, 0.85), 0.0, 0.3, emission=(1.0, 0.45, 0.85), strength=3.0)


def at(a_deg, r, z=0.0):
    a = math.radians(a_deg)
    return P(r * math.cos(a), r * math.sin(a), z)


def near(a, angles, tol):
    return any(abs((a - b + 180) % 360 - 180) < tol for b in angles)


# ---------------------------------------------------------------- pieces


def foundation(g):
    lathe("Apron", [(0.0, 0.0), (APRON_R, 0.0), (APRON_R, 0.1), (0.0, 0.1)], MT["concrete"], g, (0, 0, 0), 32, smooth=False)
    lathe("ApronSkirt", [(APRON_R - 0.05, 0.0), (APRON_R + 0.12, 0.0), (APRON_R + 0.05, 0.12), (APRON_R - 0.05, 0.12)], MT["dark"], g, (0, 0, 0), 32, smooth=False)
    # the ring wall the glass sits on (chamfered outside), the deck slab inside it
    lathe("Ring", [(RING_IN, 0.05), (RING_OUT + 0.1, 0.05), (RING_OUT + 0.1, 0.35), (RING_OUT, RING_Z - 0.12),
                   (RING_OUT - 0.12, RING_Z), (RING_IN, RING_Z), (RING_IN, 0.05)], MT["hull"], g, (0, 0, 0), 48, smooth=False)
    K.band("RingOwner", RING_OUT + 0.11, 0.14, 0.3, MT["owner"], g, segs=48)
    K.band("RingAccent", RING_OUT + 0.012, 0.42, 0.5, MT["accent"], g, segs=48)
    # frost on the lip and in a ragged rim round the foot (cold outside, heated inside)
    hoop("FrostLip", RING_OUT - 0.06, RING_Z - 0.04, 0.12, MT["frost"], g, segs=48)
    qs = []
    for k in range(48):
        a0, a1 = 2 * math.pi * k / 48, 2 * math.pi * (k + 1) / 48
        w0 = 0.25 + 0.2 * abs(math.sin(k * 2.7))
        w1 = 0.25 + 0.2 * abs(math.sin((k + 1) * 2.7))
        r = RING_OUT + 0.11
        qs.append((P(r * math.cos(a0), r * math.sin(a0), 0.105), P(r * math.cos(a1), r * math.sin(a1), 0.105),
                   P((r + w1) * math.cos(a1), (r + w1) * math.sin(a1), 0.105), P((r + w0) * math.cos(a0), (r + w0) * math.sin(a0), 0.105)))
    K.faces_obj("FrostRim", qs, MT["frost"], g)
    # hazard aprons in front of both airlocks
    for k, a in enumerate(AIRLOCKS):
        d = at(a, 1.0)
        s = Vector((-d.y, d.x, 0))
        p0 = at(a, LOCK_R + 1.25) - s * 1.2
        p1 = p0 + s * 2.4
        K.stripes(f"LockStripe{k}", (p1.x, p1.y), (p0.x, p0.y), 0.55, 0.11, g, pitch=0.4)
    for k, a in enumerate((0, 60, 120, 180, 240)):
        e = K.point(f"AddOnSpot{k}", at(a, 8.2), g)
        e.rotation_euler.z = math.radians(a - 90)


def deck(g):
    lathe("Deck", [(0.0, 0.05), (RING_IN + 0.02, 0.05), (RING_IN + 0.02, DECK_Z), (0.0, DECK_Z)], MT["deck"], g, (0, 0, 0), 32, smooth=False)
    z = DECK_Z + 0.02
    K.ring_mesh("RingPath", PATH_R0, PATH_R1, DECK_Z, z, MT["walk"], g, segs=40)
    zcyl("Plaza", PLAZA_R, DECK_Z, z, MT["walk"], g, segs=28)
    for k, a in enumerate(AIRLOCKS):
        a0, a1 = at(a, PLAZA_R - 0.2, z - 0.01), at(a, RING_IN + 0.4, z - 0.01)
        obox(f"Walk{k}", (WALK_W, (a1 - a0).length, 0.04), (a0 + a1) / 2, a1 - a0, MT["walk"], g)
        # kerbs
        d = (a1 - a0).normalized()
        s = Vector((-d.y, d.x, 0))
        for j in (-1, 1):
            obox(f"Kerb{k}{j}", (0.08, (a1 - a0).length - 0.2, 0.07), (a0 + a1) / 2 + s * (j * (WALK_W / 2 + 0.04)), d, MT["trim"], g)
    # drains / floor joints
    qs = []
    for k in range(12):
        a = 15 + 30 * k
        if near(a, AIRLOCKS, 10):
            continue
        d = at(a, 1.0)
        s = Vector((-d.y, d.x, 0)) * 0.02
        p0, p1 = d * (PATH_R1 + 0.05), d * (RING_IN - 0.1)
        qs.append((p0 - s + P(0, 0, DECK_Z + 0.003), p1 - s + P(0, 0, DECK_Z + 0.003), p1 + s + P(0, 0, DECK_Z + 0.003), p0 + s + P(0, 0, DECK_Z + 0.003)))
    K.faces_obj("DeckJoints", qs, MT["dark"], g)


def planter(name, a, r, length, g, seed):
    c = at(a, r)
    rot = (0, 0, math.radians(a + 90))
    h = 0.45
    bx(f"{name}Bed", (length, 0.8, h), c + P(0, 0, DECK_Z + h / 2), MT["planter"], g, rot=rot)
    bx(f"{name}Soil", (length - 0.12, 0.68, 0.02), c + P(0, 0, DECK_Z + h + 0.005), MT["soil"], g, rot=rot)
    t = at(a + 90, 1.0)
    n = max(2, int(length / 0.5))
    for i in range(n):
        off = (i - (n - 1) / 2) * (length - 0.3) / max(1, n - 1)
        p = c + t * off + P(0, 0, DECK_Z + h + 0.12)
        m = MT["leaf"] if (i + seed) % 3 else MT["leaf2"]
        K.rock(f"{name}Crop{i}", 0.27 + 0.05 * ((i * 7 + seed) % 3), p, g, seed * 13 + i, m)
        if (i + seed) % 4 == 0:
            K.rock(f"{name}Bloom{i}", 0.1, p + P(0.05, 0.03, 0.14), g, seed * 31 + i, MT["bloom"])


def tree(name, xy, h, g, seed):
    x, y = xy
    zcyl(f"{name}Trunk", 0.08, DECK_Z, DECK_Z + h * 0.55, MT["bark"], g, xy=(x, y), segs=6, radius2=0.05)
    K.rock(f"{name}Crown0", h * 0.32, P(x, y, DECK_Z + h * 0.62), g, seed, MT["leaf"]).scale = (1.0, 1.0, 1.5)
    K.rock(f"{name}Crown1", h * 0.24, P(x + 0.15, y - 0.1, DECK_Z + h * 0.85), g, seed + 5, MT["leaf2"]).scale = (1.0, 1.0, 1.4)


def planters(g):
    k = 0
    # outer ring of beds along the ring wall
    for i in range(16):
        a = 11.25 + 22.5 * i
        if near(a, AIRLOCKS, 14) or near(a, (HEATER,), 12):
            continue
        planter(f"Outer{k}", a, 5.45, 1.55, g, k)
        k += 1
    # inner ring of beds round the plaza
    for i in range(6):
        a = 60 * i
        if near(a, AIRLOCKS, 20):
            continue
        planter(f"Inner{k}", a, 2.65, 1.3, g, k)
        k += 1
    # trees: one in the plaza's round bed, a few in the outer zone
    zcyl("PlazaBed", 0.55, DECK_Z, DECK_Z + 0.4, MT["planter"], g, segs=16)
    zcyl("PlazaSoil", 0.48, DECK_Z + 0.4, DECK_Z + 0.42, MT["soil"], g, segs=16)
    tree("PlazaTree", (0.0, 0.0), 2.9, g, 3)
    for j, (a, r, h) in enumerate(((45, 6.1, 2.2), (195, 6.15, 2.0), (-45, 4.9, 1.8), (110, 4.95, 2.1))):
        p = at(a, r)
        tree(f"Tree{j}", (p.x, p.y), h, g, 11 + j)


def utilities(g):
    # benches round the plaza (between the walkways)
    for j, a in enumerate((-30, 30, 90, 210)):
        c = at(a, PLAZA_R + 0.45, DECK_Z)
        bx(f"Bench{j}", (1.0, 0.36, 0.08), c + P(0, 0, 0.42), MT["walk"], g, rot=(0, 0, math.radians(a + 90)))
        for s in (-1, 1):
            bx(f"BenchLeg{j}{s}", (0.08, 0.3, 0.4), c + at(a + 90, s * 0.4) + P(0, 0, 0.2), MT["trim"], g, rot=(0, 0, math.radians(a + 90)))
    # air handler against the ring at the heater's side
    c = at(HEATER, 5.6, DECK_Z)
    rot = (0, 0, math.radians(HEATER + 90))
    bx("Handler", (1.8, 1.0, 1.2), c + P(0, 0, 0.6), MT["panel"], g, rot=rot)
    bx("HandlerGrille", (1.5, 0.04, 0.7), c + at(HEATER + 180, 0.51) + P(0, 0, 0.62), MT["grate"], g, rot=rot)
    bx("HandlerOwner", (1.82, 1.02, 0.1), c + P(0, 0, 1.05), MT["owner"], g, rot=rot)
    K.pipe("HandlerDuct", [c + P(0, 0, 1.2), c + P(0, 0, 1.55), at(HEATER, RING_IN - 0.05, DECK_Z + 1.55)], 0.16, MT["steel"], g)
    # the heat exchanger outside (warm air into the ring)
    o = at(HEATER, 8.0)
    bx("Heater", (1.5, 0.9, 1.0), o + P(0, 0, 0.6), MT["trim"], g, rot=rot)
    for j in range(6):
        bx(f"HeaterFin{j}", (0.04, 0.95, 0.8), o + at(HEATER + 90, -0.6 + j * 0.24) + P(0, 0, 0.62), MT["steel"], g, rot=rot)
    bx("HeaterCap", (1.6, 1.0, 0.08), o + P(0, 0, 1.14), MT["frost"], g, rot=rot)
    bx("HeaterBand", (1.52, 0.92, 0.1), o + P(0, 0, 0.3), MT["hazard"], g, rot=rot)
    K.pipe("HeaterPipe", [o + at(HEATER + 180, 0.45) + P(0, 0, 0.7), at(HEATER, RING_OUT + 0.15, 0.7)], 0.12, MT["copper"], g)
    K.point("SparkPoint", o + P(0, 0, 1.3), g)


def struts():
    """The upper half of a frequency-3 icosphere: vertices (on the ellipsoid) and edges."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    verts = [v for v in bm.verts if v.co.z > -1e-4]
    idx = {v.index: i for i, v in enumerate(verts)}
    pts = [Vector((v.co.x * DOME_R, v.co.y * DOME_R, RING_Z + max(0.0, v.co.z) * DOME_H)) for v in verts]
    nrm = [Vector((v.co.x / DOME_R, v.co.y / DOME_R, max(0.0, v.co.z) / DOME_H)).normalized() for v in verts]
    edges = [(idx[e.verts[0].index], idx[e.verts[1].index]) for e in bm.edges if e.verts[0].index in idx and e.verts[1].index in idx]
    faces = [[idx[v.index] for v in f.verts] for f in bm.faces if all(v.index in idx for v in f.verts)]
    bm.free()
    return pts, nrm, edges, faces


def in_airlock(p):
    """Is p inside one of the airlock vestibules (with a little margin)?"""
    for a in AIRLOCKS:
        d = at(a, 1.0)
        r = p.x * d.x + p.y * d.y
        side = abs(-p.x * d.y + p.y * d.x)
        if LOCK_R - 1.6 < r < LOCK_R + 1.4 and side < 1.4 and p.z < 3.2:
            return True
    return False


def frame(g):
    piv = empty("DomeFrame", P(0, 0, 0), g)
    pts, nrm, edges, _ = struts()
    for k, (i, j) in enumerate(edges):
        a, b = pts[i], pts[j]
        if a.z < RING_Z + 0.01 and b.z < RING_Z + 0.01:
            continue
        if any(in_airlock(a.lerp(b, t / 6)) for t in range(7)):
            continue   # the vestibules break through the frame
        up = (nrm[i] + nrm[j]).normalized()
        obox(f"Strut{k}", (0.1, (b - a).length - 0.1, 0.17), (a + b) / 2, b - a, MT["frame"], piv, up=up)
    for i, (p, n) in enumerate(zip(pts, nrm)):
        if p.z < RING_Z + 0.01 or in_airlock(p):
            continue
        t = n.cross(Vector((0, 0, 1)))
        t = t.normalized() if t.length > 1e-4 else Vector((1, 0, 0))
        obox(f"Hub{i}", (0.22, 0.22, 0.22), p, t, MT["trim"], piv, up=n)
    hoop("BaseHoop", DOME_R, RING_Z + 0.08, 0.2, MT["trim"], piv, segs=40)
    top = RING_Z + DOME_H
    zcyl("Crown", 0.42, top - 0.12, top + 0.16, MT["trim"], piv, segs=12)
    K.band("CrownOwner", 0.43, top - 0.02, top + 0.08, MT["owner"], piv, segs=12)
    zcyl("BeaconMast", 0.05, top + 0.16, top + 0.75, MT["steel"], piv, segs=6)


def airlock(g, k, a):
    piv = empty(f"Airlock{k}", at(a, LOCK_R), g)
    piv.rotation_euler.z = math.radians(a + 90)
    L0, L1 = -1.25, 1.5            # local y: outer face .. inner end (inside the glass)
    L = L1 - L0
    cy = (L0 + L1) / 2
    W, WH, R = 2.5, 1.65, 1.25
    bx(f"Lock{k}Plinth", (W + 0.3, L, 0.2), P(0, cy, 0.1), MT["trim"], piv)
    bx(f"Lock{k}Walls", (W, L, WH), P(0, cy, 0.2 + WH / 2), MT["hull"], piv)
    K.cylinder(f"Lock{k}Roof", R, L, (0, cy, 0.2 + WH), MT["hull"], piv, axis="Y", segments=16)
    for j, y in enumerate((L0 + 0.35, L0 + 0.75)):
        bx(f"Lock{k}Owner{j}", (W + 0.03, 0.14, WH - 0.2), P(0, y, 0.2 + WH / 2), MT["owner"] if j == 0 else MT["accent"], piv)
    for j, y in enumerate((L0 + 0.1, cy, L1 - 0.15)):
        K.cylinder(f"Lock{k}Rib{j}", R + 0.06, 0.16, (0, y, 0.2 + WH), MT["trim"], piv, axis="Y", segments=16)
        bx(f"Lock{k}RibW{j}", (W + 0.12, 0.16, WH), P(0, y, 0.2 + WH / 2), MT["trim"], piv)
    # outer face: door frame, lit surround, door leaf (separate, slides), window, lamp, fascia + sign
    fz = 0.2
    bx(f"Lock{k}Frame", (1.6, 0.12, 2.25), P(0, L0 - 0.05, fz + 1.12), MT["trim"], piv)
    for s in (-1, 1):
        bx(f"Lock{k}LightS{s}", (0.07, 0.05, 2.0), P(s * 0.62, L0 - 0.12, fz + 1.05), MT["doorlight"], piv).visible_shadow = False
    bx(f"Lock{k}LightT", (1.3, 0.05, 0.07), P(0, L0 - 0.12, fz + 2.08), MT["doorlight"], piv).visible_shadow = False
    K.box(f"Airlock{k}Door", (1.12, 0.08, 1.95), (0, L0 - 0.13, fz + 1.0), MT["door"], piv, 0.0)
    bx(f"Lock{k}DoorWin", (0.42, 0.03, 0.3), P(0, L0 - 0.18, fz + 1.5), MT["window"], piv).visible_shadow = False
    bx(f"Lock{k}Fascia", (2.2, 0.12, 0.42), P(0, L0 - 0.06, fz + 2.5), MT["dark"], piv)
    if k == 0:
        K.marker("SignFront", P(0, L0 - 0.125, fz + 2.5), (math.radians(90), 0, 0), 2.0, 0.26, piv)
    else:
        K.marker("LabelLock1", P(0, L0 - 0.125, fz + 2.5), (math.radians(90), 0, 0), 2.0, 0.22, piv)
    bx(f"Lock{k}Lamp", (0.3, 0.16, 0.12), P(0, L0 - 0.14, fz + 2.84), MT["dark"], piv)
    bx(f"Lock{k}LampLens", (0.24, 0.03, 0.06), P(0, L0 - 0.22, fz + 2.81), MT["lamp"], piv).visible_shadow = False
    # frost along the roof's foot, a vent box and the roof spot
    for s in (-1, 1):
        bx(f"Lock{k}Frost{s}", (0.1, L - 0.4, 0.06), P(s * (W / 2 + 0.02), cy - 0.1, 0.2 + WH + 0.02), MT["frost"], piv)
    bx(f"Lock{k}Vent", (0.6, 0.5, 0.3), P(0.5, cy - 0.3, 0.2 + WH + R - 0.05), MT["panel"], piv)
    bx(f"Lock{k}VentGrille", (0.5, 0.02, 0.2), P(0.5, cy - 0.56, 0.2 + WH + R - 0.05), MT["grate"], piv)
    sp = K.point(f"RoofSpot{k}", P(-0.4, cy - 0.3, 0.2 + WH + R), piv)
    sp.rotation_euler.z = 0
    # side steps up to the door
    bx(f"Lock{k}Step", (1.5, 0.45, 0.14), P(0, L0 - 0.4, 0.07), MT["grate"], piv)


def airlocks(g):
    for k, a in enumerate(AIRLOCKS):
        airlock(g, k, a)


def emitters(g):
    for k, a in enumerate(EMITTERS):
        piv = empty(f"FieldEmitter{k}", at(a, 7.85), g)
        piv.rotation_euler.z = math.radians(a + 90)
        lathe(f"Em{k}Base", [(0.0, 0.0), (0.5, 0.0), (0.5, 0.3), (0.32, 0.45), (0.0, 0.45)], MT["trim"], piv, (0, 0, 0), 6, smooth=False)
        zcyl(f"Em{k}Column", 0.2, 0.45, 3.2, MT["hull"], piv, segs=8, radius2=0.13)
        K.band(f"Em{k}Owner", 0.18, 1.1, 1.35, MT["owner"], piv, segs=8)
        bx(f"Em{k}Strip", (0.06, 0.05, 2.0), P(0, -0.17, 1.6), MT["field"], piv).visible_shadow = False
        for j in range(3):
            t = math.radians(120 * j + 90)
            p0 = P(0.45 * math.cos(t), 0.45 * math.sin(t), 0.35)
            p1 = P(0.14 * math.cos(t), 0.14 * math.sin(t), 2.0)
            beam(f"Em{k}Fin{j}", p0, p1, 0.06, MT["trim"], piv, h=0.14)
        # emitter head: a collar, the glowing field node, three prongs and a cap
        zcyl(f"Em{k}Collar", 0.3, 3.15, 3.35, MT["trim"], piv, segs=10)
        zcyl(f"Em{k}Node", 0.17, 3.35, 3.95, MT["field"], piv, segs=10).visible_shadow = False
        hoop(f"Em{k}Halo", 0.34, 3.65, 0.07, MT["field"], piv, segs=14)
        for j in range(3):
            t = math.radians(120 * j + 30)
            beam(f"Em{k}Prong{j}", P(0.26 * math.cos(t), 0.26 * math.sin(t), 3.3), P(0.34 * math.cos(t), 0.34 * math.sin(t), 4.1), 0.05, MT["steel"], piv)
        zcyl(f"Em{k}Cap", 0.24, 3.95, 4.08, MT["dark"], piv, segs=10)
        bx(f"Em{k}Frost", (0.95, 0.95, 0.04), P(0, 0, 0.46), MT["frost"], piv, rot=(0, 0, math.radians(30)))
        # a cable conduit to the ring
        K.pipe(f"Em{k}Cable", [P(0, 0.3, 0.15), P(0, 0.75, 0.15)], 0.06, MT["cable"], piv, joints=False)


def glass(g):
    _, _, _, faces = struts()
    pts, _, _, _ = struts()
    bm = bmesh.new()
    vs = [bm.verts.new(Vector((p.x * 0.99, p.y * 0.99, RING_Z + 0.08 + (p.z - RING_Z) * 0.985))) for p in pts]
    for f in faces:
        bm.faces.new([vs[i] for i in f])
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, FROST_Z), plane_no=(0, 0, 1))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    frost_faces = [f for f in bm.faces if f.calc_center_median().z < FROST_Z]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y, c.z - RING_Z))) < 0:
            f.normal_flip()
    for f in frost_faces:
        f.material_index = 1
    o = K.mesh_object("DomeGlass", bm, MT["glass"], g)
    o.data.materials.append(MT["frostglass"])
    o.visible_shadow = False


def lights(g):
    # lamp posts along the walkways and the ring path
    k = 0
    for a in AIRLOCKS:
        for r in (2.4, 5.0):
            for s in (-1, 1):
                p = at(a, r) + at(a + 90, s * (WALK_W / 2 + 0.25))
                zcyl(f"Post{k}", 0.05, DECK_Z, DECK_Z + 1.9, MT["trim"], g, xy=(p.x, p.y), segs=6)
                bx(f"PostHead{k}", (0.26, 0.26, 0.1), p + P(0, 0, DECK_Z + 1.95), MT["dark"], g)
                bx(f"PostLens{k}", (0.2, 0.2, 0.03), p + P(0, 0, DECK_Z + 1.89), MT["lamp"], g).visible_shadow = False
                k += 1
    # bollard lights round the plaza and the path
    for j in range(8):
        a = 22.5 + 45 * j
        if near(a, AIRLOCKS, 15):
            continue
        p = at(a, PATH_R0 - 0.15)
        zcyl(f"Bollard{j}", 0.07, DECK_Z, DECK_Z + 0.5, MT["trim"], g, xy=(p.x, p.y), segs=6)
        zcyl(f"BollardLens{j}", 0.075, DECK_Z + 0.4, DECK_Z + 0.47, MT["window"], g, xy=(p.x, p.y), segs=6).visible_shadow = False
    # grow lights on short stands over some outer beds
    for j, a in enumerate((-56.25, 56.25, 101.25, 236.25)):
        p = at(a, 5.45)
        rot = (0, 0, math.radians(a + 90))
        for s in (-1, 1):
            q = p + at(a + 90, s * 0.72)
            zcyl(f"GrowPost{j}{s}", 0.03, DECK_Z + 0.45, DECK_Z + 1.5, MT["steel"], g, xy=(q.x, q.y), segs=4)
        bx(f"GrowBar{j}", (1.5, 0.18, 0.06), p + P(0, 0, DECK_Z + 1.52), MT["dark"], g, rot=rot)
        bx(f"GrowLens{j}", (1.4, 0.12, 0.02), p + P(0, 0, DECK_Z + 1.48), MT["grow"], g, rot=rot).visible_shadow = False
    # the field strip round the ring and floodlights on the apron aimed at the dome
    K.band("FieldStrip", RING_OUT + 0.08, 0.56, 0.64, MT["field"], g, segs=48).visible_shadow = False
    for j, a in enumerate((-120, -60, 60, 120, 180, 0)):
        if near(a, AIRLOCKS + EMITTERS + (HEATER,), 12):
            continue
        p = at(a, 7.75, 0.1)
        bx(f"Flood{j}", (0.36, 0.22, 0.2), p + P(0, 0, 0.14), MT["dark"], g, rot=(math.radians(20), 0, math.radians(a + 90)))
        bx(f"FloodLens{j}", (0.3, 0.03, 0.14), p + at(a, -0.12) + P(0, 0, 0.18), MT["lamp"], g, rot=(math.radians(20), 0, math.radians(a + 90))).visible_shadow = False
    sphere("Beacon", 0.13, P(0, 0, RING_Z + DOME_H + 0.85), MT["beacon"], g, 10)
    K.point("DomeLight", P(0, 0, 3.4), g, 0.5)


BUILDERS = [foundation, deck, planters, utilities, frame, airlocks, emitters, glass, lights]

PIVOTS = {"DomeFrame", "Airlock0", "Airlock1", "FieldEmitter0", "FieldEmitter1", "FieldEmitter2"}
SEPARATE = {"DomeGlass", "Beacon", "Airlock0Door", "Airlock1Door"}
EMPTIES = {"DomeLight", "SparkPoint", "SignFront", "LabelLock1", "RoofSpot0", "RoofSpot1"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("DOME 01", (0.92, 0.93, 0.95)), "LabelLock1": ("AIRLOCK 2", (0.92, 0.93, 0.95))}


def main():
    materials()
    root = K.assemble("pressure_dome", PIECES, BUILDERS)
    night = [("NDome", (1.0, 0.85, 0.65), 700, P(0, 0, 3.4)),
             ("NGrow", (1.0, 0.5, 0.85), 120, P(4.0, 2.0, 2.0)),
             ("NLock0", (1.0, 0.85, 0.6), 120, at(AIRLOCKS[0], LOCK_R + 2.0, 2.4)),
             ("NLock1", (1.0, 0.85, 0.6), 120, at(AIRLOCKS[1], LOCK_R + 2.0, 2.4))]
    shots = {"hero": ((15.5, -19.0, 11.5), (0.0, 0.0, 2.6), 32),
             "close": ((4.5, -10.5, 3.6), (0.0, 0.0, 1.6), 26),
             "night": ((-9.0, -19.0, 10.5), (0.0, 0.0, 2.6), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 18),
             "back": ((-14.0, 17.0, 10.0), (0.0, 0.5, 2.6), 32)}
    K.finish(root, KEEP, SEPARATE, "DomeGrime", 0.1, soot=None, shots=shots, night_lights=night, signs=SIGNS,
             rover_at=(-9.4, -7.0, 160))


main()
