"""Federation Mining Complex (`mine`), game-ready (owner 2026-11-06: the same full-detail treatment the refinery got).

A shaft mine on the deposit, as the RTS camera sees it from the front:
  * a steel lattice HEADFRAME over the shaft collar (hazard-striped, a black shaft mouth), a sheave deck on top with the
    two winding sheaves ("Wheel", axle front-to-back so the spin shows from the camera), inclined BACKSTAYS down to the
    left and the WINDER HOUSE they lean toward (hoist ropes from the sheaves into its roof hood);
  * the ore SKIP ("Hoist") and the man CAGE ("Cage") ride guides inside the headframe on the ropes: the skip climbs to
    the dump at the right, tips its ore down a chute into the ORE BIN on legs, while the cage goes down;
  * under the bin a BELT CONVEYOR ("Belt" scrolls, "Ore0..5" ride it under "OreFlow") climbs to a stacker head over
    the ORE HEAP at the front right ("Dust" marker = where the dust plume rises);
  * a diesel-fusion GENERATOR set at the front left (radiator fan "Fan0", exhaust "Smoke0"), an exploration DRILL RIG
    at the back right (rotary head + drill string = "Drill", bobs and turns), an operator's OFFICE at the front with the
    "MINE nn" sign, cable trays, light masts, beacons, hazard striping.

Frame and conventions (building_kit.py, CLAUDE.md "Vehicle models"): Blender +Z up, front toward -Y (= Godot +Z);
1 unit = 1 world unit; the pad is 10.5 x 9.1 (x -5.6..4.9, y -4.5..4.6), heap to 6.1, corner radius < 7.5 (inside the
8.5 the scatter keeps clear); sheave tops at 10.2.

BUILD PIECES (Build0..Build13, bottom-up): Foundation, Collar, HeadframeLower, HeadframeUpper, Backstays, SheaveDeck,
WinderHouse, Hoist, OreBin, Conveyor, Generator, DrillRig, Office, Lights.

GAME CONTRACT (BuildingModels.Mine.cs / PlanetView.Mine.cs):
  Wheel          sheave pivot (both sheaves); spin about its local Y in Godot (Blender local Z = the axle, along -Y).
                 Rope speed = WheelRadius (0.95) x angular speed.
  Hoist, Cage    skip and cage pivots, rest pose = skip up at the dump, cage down; HoistLow / CageHigh empties (siblings)
                 mark the other end of their travel (move along the parent's Y in Godot).
  Drill          rotary head + drill string; spin about local Y, bob along it (DrillLow marks the bottom of the stroke).
  OreFlow        empty at the belt's foot; Ore0..Ore5 ride toward its child OreEnd.
  Belt           material: UV v along the belt every 0.6 units (scroll Uv1Offset.y).
  Fan0           generator radiator fan (vertical axis, spin about local Y).
  Dust, Smoke0, SparkPoint   effect points (dust plume at the stacker, exhaust, sparks when broken).
  Lamp, Window, Interior, Beacon, ShaftGlow   glow materials.
  Mast, Stack, Masts   scale pivots (drill mast, exhaust stack, light masts) for per-instance variety (±10% in Y).
  OptTank, OptRods     optional parts the variety code may hide (fuel tank, drill-rod rack).
  SignFront, SignWinder, LabelBin   decal markers (MINE nn, HOIST, ORE).
  RoofSpot0..1, AddOnSpot0..4       empties for the variety add-ons (roof / ground), local +Y in Godot = up, their
                 facing = Godot local +Z.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_mine.py --game                         (export game/models/buildings/mine.glb)
  /home/user/.bpyvenv/bin/python blender/build_mine.py --game --preview blender/previews/mine
Flags: --out <glb>, --no-join, --no-bake, --bake-res 1024, --res 960, --samples 24, --threads N,
       --shots hero,game,close,night,top,back,pieces
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, lathe, strut, empty, cylinder, hoop, railing, ladder, pipe  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
import bmesh  # noqa: E402

CFG = K.setup("mine")

# ---------------------------------------------------------------- layout (Blender frame, front = -Y)

SLAB = 0.3
PAD = (-5.6, 4.9, -4.5, 4.6)            # x0, x1, y0, y1
S = Vector((0.2, 0.6))                  # shaft centre
COLLAR = 0.9                            # collar top
HF_BASE, HF_TOP = 1.3, 0.75             # headframe half widths at the collar and at the top
HF_Z = 8.4                              # headframe top / sheave deck
R_WHEEL = 1.2
WHEEL = Vector((S.x - R_WHEEL, S.y, HF_Z + 1.35))   # sheave axle centre (ropes drop at x = S.x)
LANES = (-0.36, 0.36)                   # skip lane (front) and cage lane (back), y offsets from S
WINDER = (-5.5, -3.75, -0.7, 2.3, 2.7)  # x0, x1, y0, y1, roof
HOOD = Vector((-3.95, S.y, WINDER[4] + 0.15))
BIN = Vector((3.0, 0.6))
BIN_TOP, BIN_BOX, BIN_OUT = 4.6, 2.9, 1.95
BELT_A = Vector((3.0, 0.9, 1.05))
BELT_B = Vector((4.25, -2.45, 2.55))
HEAP = Vector((4.55, -3.35))
GEN = Vector((-3.45, -2.95))
DRILL = Vector((3.2, 3.35))
OFFICE = Vector((0.15, -3.35))
SKIP_UP, SKIP_DOWN = 5.35, 0.45         # skip bottom at the dump / in the shaft
PIECES = ["Foundation", "Collar", "HeadframeLower", "HeadframeUpper", "Backstays", "SheaveDeck", "WinderHouse", "Hoist",
          "OreBin", "Conveyor", "Generator", "DrillRig", "Office", "Lights"]


def materials():
    K.materials()
    K.belt_material()
    K.mat("void", "Void", (0.01, 0.01, 0.012), 0.0, 0.9)
    K.mat("shaftglow", "ShaftGlow", (1.0, 0.75, 0.4), 0.0, 0.4, emission=(1.0, 0.7, 0.35), strength=2.5)
    K.mat("skip", "Skip", (0.62, 0.42, 0.16), 0.5, 0.45, textured=True)
    K.mat("oreheap", "OreHeap", (0.36, 0.31, 0.27), 0.05, 0.95, textured=True)
    K.mat("rods", "Rods", (0.5, 0.52, 0.56), 0.85, 0.35, textured=True)


def hw(z):
    """Headframe half width at height z."""
    return HF_BASE + (HF_TOP - HF_BASE) * (z - COLLAR) / (HF_Z - COLLAR)


def corner(sx, sy, z):
    w = hw(z)
    return P(S.x + sx * w, S.y + sy * w, z)


CORNERS = ((-1, -1), (1, -1), (1, 1), (-1, 1))


# ---------------------------------------------------------------- pieces


def foundation(g):
    x0, x1, y0, y1 = PAD
    bx("Slab", (x1 - x0, y1 - y0, SLAB), P((x0 + x1) / 2, (y0 + y1) / 2, SLAB / 2), MT["concrete"], g, 0.06)
    bx("Skirt", (x1 - x0 + 0.3, y1 - y0 + 0.3, 0.14), P((x0 + x1) / 2, (y0 + y1) / 2, 0.07), MT["dark"], g, 0.03)
    z = SLAB + 0.003
    qs = []
    for x in (-3.4, -1.2, 1.9):
        qs.append((P(x - 0.02, y0 + 0.1, z), P(x + 0.02, y0 + 0.1, z), P(x + 0.02, y1 - 0.1, z), P(x - 0.02, y1 - 0.1, z)))
    for y in (-2.0, 2.6):
        qs.append((P(x0 + 0.1, y - 0.02, z), P(x1 - 0.1, y - 0.02, z), P(x1 - 0.1, y + 0.02, z), P(x0 + 0.1, y + 0.02, z)))
    K.faces_obj("Joints", qs, MT["dark"], g)
    K.flat_path("WalkLine", [(OFFICE.x, OFFICE.y + 0.75), (OFFICE.x, -1.6), (S.x - 1.9, -1.6), (S.x - 1.9, 2.6)], 0.08, MT["hazard"], z + 0.001, g)
    K.flat_path("EdgeLine", [(x0 + 0.15, y0 + 0.15), (x1 - 0.15, y0 + 0.15)], 0.06, MT["hazard"], z + 0.001, g)
    # truck lane to the heap
    K.stripes("HeapLaneA", (2.2, y0 + 0.2), (2.2, -1.0), 0.3, z + 0.002, g)
    for k, (x, y) in enumerate([(-2.0, -1.0), (2.2, 3.6), (-1.4, 3.9)]):
        bx(f"Drain{k}", (0.7, 0.35, 0.02), P(x, y, SLAB + 0.008), MT["grate"], g)
    K.bollards("Bollard", [(1.75, y0 + 0.25), (2.65, y0 + 0.25), (-1.0, y0 + 0.25), (1.3, -1.2)], SLAB, g)
    # variety spots: add-ons beside and behind, roof gear on the winder house and the office
    for k, (x, y, yaw) in enumerate([(-6.9, 3.4, 90), (-6.9, -2.6, 90), (6.4, 2.6, -90), (-2.6, 6.0, 0), (1.2, 6.1, 0)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)
    K.point("RoofSpot0", P((WINDER[0] + WINDER[1]) / 2, 1.5, WINDER[4] + 0.12), g)
    K.point("RoofSpot1", P(OFFICE.x - 0.5, OFFICE.y + 0.15, SLAB + 2.32), g)


def collar(g):
    K.ring_mesh("Collar", 1.05, 1.75, SLAB, COLLAR, MT["concrete"], g, xy=S, segs=8)
    zcyl("ShaftMouth", 1.07, SLAB + 0.005, SLAB + 0.02, MT["void"], g, xy=S, segs=8)
    # a faint lit ring down the shaft (lamps on the shaft wall)
    K.band("ShaftLights", 1.04, SLAB + 0.12, SLAB + 0.2, MT["shaftglow"], g, xy=S, segs=8).visible_shadow = False
    # hazard chevrons painted on the collar top
    qs_y, qs_k = [], []
    for k in range(16):
        a0, a1 = 2 * math.pi * k / 16, 2 * math.pi * (k + 1) / 16
        q = (P(S.x + 1.1 * math.cos(a0), S.y + 1.1 * math.sin(a0), COLLAR + 0.004), P(S.x + 1.7 * math.cos(a0 + 0.15), S.y + 1.7 * math.sin(a0 + 0.15), COLLAR + 0.004),
             P(S.x + 1.7 * math.cos(a1 + 0.15), S.y + 1.7 * math.sin(a1 + 0.15), COLLAR + 0.004), P(S.x + 1.1 * math.cos(a1), S.y + 1.1 * math.sin(a1), COLLAR + 0.004))
        (qs_y if k % 2 == 0 else qs_k).append(q)
    K.faces_obj("CollarStripeY", qs_y, MT["hazard"], g)
    K.faces_obj("CollarStripeK", qs_k, MT["dark"], g)
    # footings under the headframe legs and the backstays
    for k, (sx, sy) in enumerate(CORNERS):
        c = corner(sx, sy, COLLAR)
        bx(f"Footing{k}", (0.62, 0.62, COLLAR - SLAB + 0.12), P(c.x, c.y, (COLLAR + SLAB) / 2 + 0.06), MT["concrete"], g, 0.03)
        bx(f"BasePlate{k}", (0.44, 0.44, 0.05), P(c.x, c.y, COLLAR + 0.145), MT["steel"], g)
    for k, y in enumerate(BACKSTAY_Y):
        bx(f"StayFooting{k}", (0.8, 0.7, 0.4), P(STAY_FOOT_X, S.y + y, SLAB + 0.2), MT["concrete"], g, 0.03)
    # a guard rail round the collar, open where the cage comes out
    pts = [(S.x + 1.55 * math.cos(math.radians(a)), S.y + 1.55 * math.sin(math.radians(a))) for a in range(-60, 241, 30)]
    railing("CollarRail", pts, COLLAR, g, h=0.85, post=0.8)


def lattice(g, z0, z1, levels, name):
    """Corner chords from z0 to z1, girts at `levels`, X bracing on every face between consecutive levels."""
    for k, (sx, sy) in enumerate(CORNERS):
        beam(f"{name}Chord{k}", corner(sx, sy, z0), corner(sx, sy, z1), 0.26, MT["hull"], g)
    for i, z in enumerate(levels):
        for k in range(4):
            a, b = CORNERS[k], CORNERS[(k + 1) % 4]
            beam(f"{name}Girt{i}{k}", corner(*a, z), corner(*b, z), 0.14, MT["hull"], g, 0.16)
    for i, (za, zb) in enumerate(zip(levels, levels[1:])):
        for k in range(4):
            a, b = CORNERS[k], CORNERS[(k + 1) % 4]
            strut(f"{name}BraceA{i}{k}", corner(*a, za), corner(*b, zb), 0.04, MT["trim"], g, 5)
            strut(f"{name}BraceB{i}{k}", corner(*b, za), corner(*a, zb), 0.04, MT["trim"], g, 5)


def headframe_lower(g):
    lattice(g, COLLAR + 0.17, 4.8, [COLLAR + 0.17, 2.85, 4.8], "HfL")
    # owner band round the lower girt, hazard bands on the legs
    for k, (sx, sy) in enumerate(CORNERS):
        c = corner(sx, sy, 1.6)
        for j in range(3):
            bx(f"LegBand{k}{j}", (0.25, 0.25, 0.18), c + P(0, 0, j * 0.36), MT["hazard" if j % 2 == 0 else "dark"], g)
    for k in range(4):
        a, b = CORNERS[k], CORNERS[(k + 1) % 4]
        beam(f"OwnerGirt{k}", corner(*a, 2.85) + P(0, 0, 0.0), corner(*b, 2.85), 0.15, MT["owner"], g, 0.06)
    # skip / cage guides: two vertical rails per lane on the inner faces
    for j, ly in enumerate(LANES):
        for side in (-1, 1):
            x = S.x + side * 0.4
            bx(f"Guide{j}{side}", (0.06, 0.1, HF_Z - SLAB - 0.3), P(x, S.y + ly, (HF_Z + SLAB) / 2 - 0.15), MT["steel"], g)
    # a caged ladder up the front-left leg
    c = corner(-1, -1, COLLAR)
    ladder("HfLadder", P(c.x - 0.1, c.y - 0.45, COLLAR + 0.15), 5.6, (0, -1), g)


def headframe_upper(g):
    lattice(g, 4.8, HF_Z, [4.8, 6.6, HF_Z], "HfU")
    # the dump platform on the bin side with its chute
    dz = 5.55
    x0 = corner(1, -1, dz).x
    bx("DumpDeck", (1.0, 1.9, 0.08), P(x0 + 0.5, S.y, dz), MT["grate"], g)
    strut("DumpStrut0", P(x0 + 0.95, S.y - 0.85, dz - 0.04), P(x0, S.y - 0.85, dz - 0.9), 0.04, MT["trim"], g, 5)
    strut("DumpStrut1", P(x0 + 0.95, S.y + 0.85, dz - 0.04), P(x0, S.y + 0.85, dz - 0.9), 0.04, MT["trim"], g, 5)
    railing("DumpRail", [(x0 + 0.05, S.y - 0.92), (x0 + 0.98, S.y - 0.92), (x0 + 0.98, S.y + 0.92), (x0 + 0.05, S.y + 0.92)], dz + 0.04, g, h=0.8, post=0.6)
    # dump scroll (the guide that tips the skip) and the chute down to the bin
    bx("DumpScroll", (0.08, 0.7, 0.9), P(S.x + 0.42, S.y + LANES[0], SKIP_UP + 1.3), MT["hazard"], g)
    a, b = P(S.x + 0.55, S.y, SKIP_UP + 0.55), P(BIN.x - 0.55, BIN.y, BIN_TOP + 0.25)
    obox("Chute", (0.85, (b - a).length, 0.08), (a + b) / 2, b - a, MT["steel"], g)
    for side in (-1, 1):
        obox(f"ChuteSide{side}", (0.06, (b - a).length, 0.4), (a + b) / 2 + P(0, side * 0.42, 0.16), b - a, MT["trim"], g)
    # a second ladder flight to the top
    c = corner(-1, -1, 5.6)
    ladder("HfLadder2", P(c.x - 0.05, c.y - 0.4, 5.55), HF_Z, (0, -1), g)
    bx("LadderRest", (0.8, 0.7, 0.06), P(c.x - 0.05, c.y - 0.4, 5.55), MT["grate"], g)
    # an owner-colour panel on the front face: the company board
    f = corner(0, -1, 7.5)
    bx("FrontBoard", (1.25, 0.06, 0.55), P(S.x, f.y - 0.06, 7.5), MT["owner"], g)


BACKSTAY_Y = (-0.85, 0.85)
STAY_FOOT_X = -3.05
STAY_TOP = Vector((S.x - 1.75, 0, HF_Z - 0.15))


def backstays(g):
    for k, y in enumerate(BACKSTAY_Y):
        top = P(STAY_TOP.x, S.y + y * 0.7, STAY_TOP.z)
        foot = P(STAY_FOOT_X, S.y + y, SLAB + 0.4)
        K.truss(f"Stay{k}", foot, top, 0.36, 0.09, MT["hull"], g, bays=8, up=Vector((0, 1, 0)))
        bx(f"StayShoe{k}", (0.55, 0.5, 0.18), foot + P(0, 0, 0.0), MT["steel"], g)
    # ties between the two stays
    for i, t in enumerate((0.3, 0.55, 0.8)):
        a = P(STAY_FOOT_X, S.y + BACKSTAY_Y[0], SLAB + 0.4).lerp(P(STAY_TOP.x, S.y + BACKSTAY_Y[0] * 0.7, STAY_TOP.z), t)
        b = P(STAY_FOOT_X, S.y + BACKSTAY_Y[1], SLAB + 0.4).lerp(P(STAY_TOP.x, S.y + BACKSTAY_Y[1] * 0.7, STAY_TOP.z), t)
        beam(f"StayTie{i}", a, b, 0.1, MT["trim"], g)
        if i < 2:
            b2 = P(STAY_FOOT_X, S.y + BACKSTAY_Y[1], SLAB + 0.4).lerp(P(STAY_TOP.x, S.y + BACKSTAY_Y[1] * 0.7, STAY_TOP.z), t + 0.25)
            strut(f"StayX{i}", a, b2, 0.035, MT["trim"], g, 5)
    # a stair-like owner stripe on the stays (reads at distance)
    for k, y in enumerate(BACKSTAY_Y):
        a = P(STAY_FOOT_X, S.y + y, SLAB + 0.4).lerp(P(STAY_TOP.x, S.y + y * 0.7, STAY_TOP.z), 0.12)
        b = P(STAY_FOOT_X, S.y + y, SLAB + 0.4).lerp(P(STAY_TOP.x, S.y + y * 0.7, STAY_TOP.z), 0.2)
        beam(f"StayBand{k}", a, b, 0.42, MT["hazard"], g, 0.42)


def sheave(name, parent, y):
    """One sheave in the Wheel pivot's frame (axle = local Z): a grooved rim, six spokes, a hub."""
    r = R_WHEEL
    lathe(f"{name}Rim", [(r - 0.2, y - 0.1), (r + 0.04, y - 0.12), (r + 0.06, y - 0.06), (r - 0.03, y), (r + 0.06, y + 0.06),
                         (r + 0.04, y + 0.12), (r - 0.2, y + 0.1), (r - 0.2, y - 0.1)], MT["owner"], parent, segs=36, smooth=False)
    zcyl(f"{name}Hub", 0.2, y - 0.13, y + 0.13, MT["trim"], parent, segs=12)
    for k in range(8):
        a = math.radians(45 * k)
        bx(f"{name}Spoke{k}", (r - 0.2, 0.08, 0.06), P(math.cos(a) * (r - 0.2) / 2, math.sin(a) * (r - 0.2) / 2, y), MT["hull"], parent, rot=(0, 0, a))
    bx(f"{name}Mark", (0.2, 0.12, 0.22), P(r - 0.3, 0, y), MT["hazard"], parent)


def sheave_deck(g):
    z = HF_Z
    x0, x1 = STAY_TOP.x - 0.3, S.x + HF_TOP + 0.15
    bx("SheaveDeck", (x1 - x0, 2.1, 0.1), P((x0 + x1) / 2, S.y, z + 0.05), MT["grate"], g)
    bx("SheaveDeckBeams", (x1 - x0, 1.6, 0.22), P((x0 + x1) / 2, S.y, z - 0.08), MT["trim"], g)
    for side in (-1, 1):
        bx(f"DeckEdge{side}", (x1 - x0, 0.05, 0.14), P((x0 + x1) / 2, S.y + side * 1.05, z + 0.07), MT["owner"], g)
    railing("DeckRail", [(x0 + 0.05, S.y - 1.0), (x1 - 0.05, S.y - 1.0), (x1 - 0.05, S.y + 1.0), (x0 + 0.05, S.y + 1.0), (x0 + 0.05, S.y - 1.0)], z + 0.1, g, h=0.85, post=0.7)
    # bearing pedestals and the axle
    for k, y in enumerate((-0.72, 0.72)):
        bx(f"Pedestal{k}", (0.42, 0.2, WHEEL.z - z - 0.05), P(WHEEL.x, S.y + y, (WHEEL.z + z) / 2 + 0.05), MT["accent"], g, 0.02)
        bx(f"Bearing{k}", (0.36, 0.26, 0.26), P(WHEEL.x, S.y + y, WHEEL.z), MT["dark"], g, 0.02)
    cylinder("Axle", 0.07, 1.6, P(WHEEL.x, S.y, WHEEL.z), MT["steel"], g, axis="Y", segments=10)
    piv = empty("Wheel", WHEEL, g)
    piv.rotation_euler = (math.radians(90), 0, 0)    # local Z = -Y (the axle)
    for j, ly in enumerate(LANES):
        sheave(f"Sheave{j}", piv, -ly)                # local z = -world y offset
    # beacon on a short mast, lamps under the deck aimed down the shaft
    zcyl("TopMast", 0.05, z + 0.1, z + 2.2, MT["steel"], g, xy=(x1 - 0.2, S.y - 0.9), segs=8)
    sphere("TopBeacon", 0.09, P(x1 - 0.2, S.y - 0.9, z + 2.27), MT["beacon"], g, 8)
    for k, y in enumerate((-0.9, 0.9)):
        bx(f"DeckLamp{k}", (0.3, 0.16, 0.12), P(S.x + 0.5, S.y + y, z - 0.25), MT["dark"], g)
        bx(f"DeckLens{k}", (0.24, 0.12, 0.02), P(S.x + 0.5, S.y + y, z - 0.32), MT["lamp"], g).visible_shadow = False
    # rope guards (half rings) over the sheaves' backs
    for j, ly in enumerate(LANES):
        o = lathe(f"Guard{j}", [(R_WHEEL + 0.16, -0.08), (R_WHEEL + 0.2, -0.08), (R_WHEEL + 0.2, 0.08), (R_WHEEL + 0.16, 0.08), (R_WHEEL + 0.16, -0.08)],
                  MT["hazard"], None, segs=12, smooth=False)
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y < -0.05 or v.co.x > 0.2], context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
        o.matrix_basis = Matrix.Translation(P(WHEEL.x, S.y + ly, WHEEL.z)) @ Matrix.Rotation(math.radians(90), 4, "X")
        o.parent = g


def winder_house(g):
    x0, x1, y0, y1, top = WINDER
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w, d = x1 - x0, y1 - y0
    bx("WinderPlinth", (w + 0.12, d + 0.12, 0.3), P(cx, cy, SLAB + 0.15), MT["trim"], g, 0.02)
    bx("WinderWalls", (w, d, top - SLAB - 0.3), P(cx, cy, (top + SLAB + 0.3) / 2), MT["panel"], g, 0.04)
    bx("WinderAccent", (w + 0.02, d + 0.02, 0.45), P(cx, cy, SLAB + 0.75), MT["accent"], g)
    bx("WinderRoof", (w + 0.3, d + 0.3, 0.16), P(cx, cy, top + 0.04), MT["hull"], g, 0.03)
    bx("WinderOwner", (w + 0.03, d + 0.03, 0.12), P(cx, cy, top - 0.32), MT["owner"], g)
    for k in range(4):
        bx(f"WinderRib{k}", (0.06, d + 0.04, top - SLAB - 0.5), P(x0 + 0.25 + k * (w - 0.5) / 3, cy, (top + SLAB + 0.3) / 2), MT["trim"], g)
    # the rope hood at the front of the roof (facing the headframe) with the drum showing through
    bx("Hood", (0.75, 1.5, 0.6), HOOD + P(0, 0, 0.2), MT["trim"], g, 0.03)
    bx("HoodMouth", (0.05, 1.2, 0.35), HOOD + P(0.37, 0, 0.2), MT["void"], g)
    cylinder("Drum", 0.5, 1.3, P(HOOD.x - 0.55, S.y, HOOD.z - 0.1), MT["dark"], g, axis="Y", segments=16)
    for k in (-1, 1):
        cylinder(f"DrumFlange{k}", 0.58, 0.06, P(HOOD.x - 0.55, S.y + k * 0.62, HOOD.z - 0.1), MT["steel"], g, axis="Y", segments=16)
    # side windows (facing front), door, vents, sign on the front face
    K.pane_row("WinderWindows", (x0 + 0.2, y0 - 0.012, 0), (1, 0, 0), None, w - 0.4, SLAB + 1.5, SLAB + 1.9, 2, MT["window"], g)
    bx("WinderDoor", (0.06, 0.8, 1.35), P(x1 + 0.02, cy - 0.9, SLAB + 0.98), MT["trim"], g)
    bx("WinderDoorLamp", (0.06, 0.4, 0.06), P(x1 + 0.05, cy - 0.9, SLAB + 1.78), MT["window"], g).visible_shadow = False
    K.wall_stripes("WinderDoorStripe", (x1 + 0.03, cy - 1.5), (x1 + 0.03, cy - 0.3), SLAB + 0.3, SLAB + 0.45, g)
    for k in range(2):
        zcyl(f"WinderVent{k}", 0.17, top + 0.12, top + 0.5, MT["trim"], g, xy=(x0 + 0.5, y1 - 0.4 - k * 0.6), segs=10)
        zcyl(f"WinderVentCap{k}", 0.26, top + 0.5, top + 0.58, MT["hull"], g, xy=(x0 + 0.5, y1 - 0.4 - k * 0.6), segs=10)
    bx("WinderPlate", (1.4, 0.05, 0.34), P(cx, y0 - 0.03, top - 0.65), MT["dark"], g)
    K.marker("SignWinder", P(cx, y0 - 0.06, top - 0.65), (math.radians(90), 0, 0), 1.25, 0.22, g)


def rope(name, a, b, parent):
    strut(name, a, b, 0.028, MT["cable"], parent, 6)


def tangent_point(c, r, h, upper=True):
    """Where a rope from point h (x, z) leaves a circle (centre c, radius r) in the XZ plane, on its upper side."""
    dx, dz = h[0] - c[0], h[1] - c[1]
    d = math.hypot(dx, dz)
    th = math.atan2(dz, dx)
    be = math.acos(min(1.0, r / d))
    cands = [th + be, th - be]
    pts = [(c[0] + r * math.cos(a), c[1] + r * math.sin(a)) for a in cands]
    return max(pts, key=lambda p: p[1]) if upper else min(pts, key=lambda p: p[1])


def hoist(g):
    # ropes: from each sheave's top-back down into the winder hood, and down the shaft side into the lane
    t = tangent_point((WHEEL.x, WHEEL.z), R_WHEEL, (HOOD.x + 0.35, HOOD.z + 0.2))
    for j, ly in enumerate(LANES):
        y = S.y + ly
        rope(f"BackRope{j}", P(t[0], y, t[1]), P(HOOD.x + 0.35, S.y + ly * 0.8, HOOD.z + 0.2), g)
        rope(f"ShaftRope{j}", P(S.x, y, WHEEL.z), P(S.x, y, SLAB - 0.1), g)
    # the skip: a tipping bucket in a frame (owner stripe, bail at the top), rest pose up at the dump
    skip = empty("Hoist", P(S.x, S.y + LANES[0], SKIP_UP), g)
    bx("SkipFrame", (0.74, 0.66, 1.35), P(0, 0, 0.68), MT["trim"], skip, 0.02)
    bx("SkipBucket", (0.66, 0.6, 1.05), P(0.03, 0, 0.78), MT["skip"], skip, 0.03, rot=(0, math.radians(-6), 0))
    bx("SkipOwner", (0.68, 0.62, 0.12), P(0.03, 0, 1.1), MT["owner"], skip)
    for k in range(3):
        rock(f"SkipOre{k}", 0.13, P(-0.12 + k * 0.12, (k - 1) * 0.12, 1.3), skip, 20 + k)
    strut("SkipBail", P(0, 0, 1.35), P(0, 0, 1.75), 0.05, MT["steel"], skip, 6)
    bx("SkipCrosshead", (0.9, 0.12, 0.1), P(0, 0, 1.75), MT["hazard"], skip)
    low = K.point("HoistLow", P(S.x, S.y + LANES[0], SKIP_DOWN), g)
    # the man cage (counterweighted against the skip): rest pose down in the collar
    cage = empty("Cage", P(S.x, S.y + LANES[1], SKIP_DOWN), g)
    bx("CageFloor", (0.72, 0.62, 0.08), P(0, 0, 0.04), MT["trim"], cage)
    bx("CageRoof", (0.76, 0.66, 0.1), P(0, 0, 1.6), MT["accent"], cage, 0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bx(f"CagePost{sx}{sy}", (0.05, 0.05, 1.55), P(sx * 0.34, sy * 0.29, 0.82), MT["steel"], cage)
    for k in range(3):
        bx(f"CageMesh{k}", (0.7, 0.03, 0.03), P(0, 0.3, 0.4 + k * 0.4), MT["steel"], cage)
    bx("CageLamp", (0.3, 0.06, 0.06), P(0, -0.33, 1.5), MT["lamp"], cage).visible_shadow = False
    strut("CageBail", P(0, 0, 1.65), P(0, 0, 1.95), 0.04, MT["steel"], cage, 6)
    K.point("CageHigh", P(S.x, S.y + LANES[1], SKIP_UP), g)
    low.empty_display_size = 0.4


def rock(name, r, loc, parent, seed):
    return K.rock(name, r, loc, parent, seed)


def ore_bin(g):
    c = BIN.to_3d()
    for k, (sx, sy) in enumerate(CORNERS):
        p = c + P(sx * 0.8, sy * 0.8, 0)
        bx(f"BinLeg{k}", (0.2, 0.2, BIN_BOX - SLAB), p + P(0, 0, (BIN_BOX + SLAB) / 2), MT["hull"], g)
        bx(f"BinLegFoot{k}", (0.4, 0.4, 0.05), p + P(0, 0, SLAB + 0.025), MT["steel"], g)
        for j in range(2):
            bx(f"BinLegBand{k}{j}", (0.22, 0.22, 0.18), p + P(0, 0, SLAB + 0.15 + j * 0.36), MT["hazard"] if j == 0 else MT["dark"], g)
    for k in range(4):
        a, b = CORNERS[k], CORNERS[(k + 1) % 4]
        pa, pb = c + P(a[0] * 0.8, a[1] * 0.8, 0), c + P(b[0] * 0.8, b[1] * 0.8, 0)
        if k != 0:   # the front stays open for the belt
            strut(f"BinBraceA{k}", pa + P(0, 0, SLAB + 0.3), pb + P(0, 0, BIN_BOX - 0.2), 0.035, MT["trim"], g, 5)
            strut(f"BinBraceB{k}", pb + P(0, 0, SLAB + 0.3), pa + P(0, 0, BIN_BOX - 0.2), 0.035, MT["trim"], g, 5)
    bx("BinBox", (1.85, 1.85, BIN_TOP - BIN_BOX), c + P(0, 0, (BIN_TOP + BIN_BOX) / 2), MT["accent"], g, 0.03)
    for k, z in enumerate((BIN_BOX + 0.4, BIN_BOX + 1.1)):
        bx(f"BinHoop{k}", (1.9, 1.9, 0.08), c + P(0, 0, z), MT["trim"], g)
    bx("BinLip", (1.95, 1.95, 0.1), c + P(0, 0, BIN_TOP), MT["trim"], g)
    hop = cylinder("BinCone", 1.31, BIN_BOX - BIN_OUT, c + P(0, 0, (BIN_BOX + BIN_OUT) / 2), MT["steel"], g, axis="Z", segments=4, radius2=0.32)
    hop.rotation_euler = (0, 0, math.radians(45))
    for o in hop.data.polygons:
        o.use_smooth = False
    hop2 = cylinder("BinCone2", 0.32, 0.14, c + P(0, 0, BIN_OUT), MT["steel"], g, axis="Z", segments=4, radius2=0.32)
    hop2.rotation_euler = hop.rotation_euler
    bx("BinGate", (0.5, 0.3, 0.12), c + P(0, 0.05, BIN_OUT - 0.15), MT["hazard"], g)
    # ore heaped in the bin (seen from above) and a label plate
    K.heap("BinOre", 0.85, 0.35, c + P(0, 0, BIN_TOP - 0.1), MT["oreheap"], g, seed=3, segs=10)
    bx("BinPlate", (0.9, 0.04, 0.36), c + P(0, -0.95, BIN_BOX + 0.75), MT["panel"], g)
    K.marker("LabelBin", c + P(0, -0.98, BIN_BOX + 0.75), (math.radians(90), 0, 0), 0.75, 0.24, g)
    railing("BinRail", [(c.x - 0.9, c.y - 0.9), (c.x + 0.9, c.y - 0.9), (c.x + 0.9, c.y + 0.9), (c.x - 0.9, c.y + 0.9)], BIN_TOP + 0.05, g, h=0.6, post=0.9)


def conveyor(g):
    A, B = BELT_A, BELT_B
    u = (B - A).normalized()
    L = (B - A).length
    R = K.frame(u)
    s = R.col[0].to_3d()
    n = R.col[2].to_3d()
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    prev = None
    steps = 10
    for i in range(steps + 1):
        p = A + u * (L * i / steps) + n * 0.02
        row = (bm.verts.new(p - s * 0.27), bm.verts.new(p + s * 0.27))
        if prev:
            f = bm.faces.new((prev[0], prev[1], row[1], row[0]))
            for loop in f.loops:
                v = Vector(loop.vert.co) - A
                loop[uv].uv = (0.5 + v.dot(s) / 0.54, v.dot(u) / 0.6)
        prev = row
    K.mesh_object("Belt", bm, MT["belt"], g)
    for side in (-1, 1):
        obox(f"Stringer{side}", (0.07, L + 0.3, 0.26), A + u * (L / 2) + s * (side * 0.35) - n * 0.11, u, MT["trim"], g)
        obox(f"Skirt{side}", (0.03, L, 0.14), A + u * (L / 2) + s * (side * 0.29) + n * 0.07, u, MT["steel"], g)
    t, k = 0.2, 0
    while t < L - 0.1:
        c = A + u * t - n * 0.06
        strut(f"Idler{k}", c - s * 0.29, c + s * 0.29, 0.05, MT["steel"], g, 8)
        t, k = t + 0.42, k + 1
    for j, (tt, r) in enumerate(((0.0, 0.18), (L, 0.2))):
        c = A + u * tt - n * 0.05
        strut(f"Pulley{j}", c - s * 0.32, c + s * 0.32, r, MT["dark"], g, 12)
    # stacker head: a hood over the head pulley and a short spout down onto the heap
    bx("HeadHood", (0.8, 0.75, 0.55), B + u * 0.1 + P(0, 0, 0.18), MT["accent"], g, 0.03, rot=(0, 0, math.atan2(u.y, u.x) - math.pi / 2))
    obox("Spout", (0.5, 0.5, 0.6), B + u * 0.35 + P(0, 0, -0.35), P(0, 0, 1), MT["steel"], g, up=Vector((u.x, u.y, 0)))
    # support bents
    for j, tt in enumerate((L * 0.4, L * 0.92)):
        c = A + u * tt - n * 0.22
        for side in (-1, 1):
            top = c + s * (side * 0.38)
            bx(f"Bent{j}{side}", (0.12, 0.12, top.z - SLAB), P(top.x, top.y, (top.z + SLAB) / 2), MT["hull"], g)
            bx(f"BentFoot{j}{side}", (0.28, 0.28, 0.05), P(top.x, top.y, SLAB + 0.025), MT["steel"], g)
        obox(f"BentBar{j}", (0.09, 0.8, 0.09), P(c.x, c.y, (c.z + SLAB) / 2 + 0.15), s, MT["trim"], g)
    # walkway with a handrail on the west side
    w = s * -0.62
    obox("Walkway", (0.4, L, 0.05), A + u * (L / 2) + w - n * 0.1, u, MT["grate"], g)
    for k in range(5):
        p = A + u * (L * k / 4) + w - s * 0.18 - n * 0.08
        bx(f"WalkPost{k}", (0.045, 0.045, 0.85), p + P(0, 0, 0.42), MT["rail"], g)
    obox("WalkRail", (0.045, L, 0.045), A + u * (L / 2) + w - s * 0.18 + P(0, 0, 0.77) - n * 0.08, u, MT["rail"], g)
    # ore riding the belt
    flow = empty("OreFlow", A + n * 0.08, g)
    end = empty("OreEnd", u * (L - 0.1), flow)
    end.empty_display_size = 0.2
    for k in range(6):
        tt = L * (k + 0.3) / 6
        K.rock(f"Ore{k}", 0.1 + 0.035 * ((k * 5) % 3), u * tt + s * (0.1 * math.sin(k * 2.3)), flow, k + 1)
    # the heap and where the dust rises
    K.heap("OreHeap", 1.5, 1.45, HEAP.to_3d() + P(0, 0, SLAB - 0.05), MT["oreheap"], g, seed=7, segs=16)
    K.heap("OreHeapSpill", 0.7, 0.45, HEAP.to_3d() + P(-1.2, 0.6, SLAB - 0.05), MT["oreheap"], g, seed=11, segs=10)
    for k in range(5):
        a = 1.3 + k * 0.9
        K.rock(f"HeapLump{k}", 0.14, HEAP.to_3d() + P(1.5 * math.cos(a), 1.5 * math.sin(a), SLAB + 0.05), g, 40 + k)
    K.point("Dust", B + u * 0.4 + P(0, 0, -0.6), g, 0.5)


def generator(g):
    c = GEN.to_3d()
    L, W, H = 2.8, 1.35, 1.45
    bx("GenSkid", (L + 0.2, W + 0.2, 0.18), c + P(0, 0, SLAB + 0.09), MT["dark"], g, 0.02)
    bx("GenBody", (L, W, H), c + P(0, 0, SLAB + 0.18 + H / 2), MT["hull"], g, 0.05)
    bx("GenAccent", (L + 0.02, W + 0.02, 0.32), c + P(0, 0, SLAB + 0.45), MT["accent"], g)
    bx("GenOwner", (L + 0.02, W + 0.02, 0.1), c + P(0, 0, SLAB + 1.35), MT["owner"], g)
    for k in range(5):
        bx(f"GenLouvre{k}", (0.32, 0.03, 0.75), c + P(-0.6 + k * 0.38, -W / 2 - 0.015, SLAB + 0.95), MT["trim"], g)
    bx("GenRadiator", (0.08, W - 0.2, H - 0.3), c + P(-L / 2 - 0.04, 0, SLAB + 0.2 + H / 2), MT["grate"], g)
    bx("GenPanel", (0.5, 0.04, 0.6), c + P(1.0, -W / 2 - 0.02, SLAB + 1.0), MT["dark"], g)
    for k in range(3):
        bx(f"GenLed{k}", (0.07, 0.03, 0.05), c + P(0.85 + k * 0.13, -W / 2 - 0.045, SLAB + 1.2), MT["lamp" if k != 2 else "beacon"], g).visible_shadow = False
    # radiator fan on top (vertical axis), with a guard ring
    top = SLAB + 0.18 + H
    zcyl("FanShroud", 0.5, top, top + 0.16, MT["trim"], g, xy=(c.x - 0.75, c.y), segs=20)
    hoop("FanGuard", 0.5, top + 0.2, 0.04, MT["steel"], g, xy=(c.x - 0.75, c.y), segs=20)
    fan = empty("Fan0", c + P(-0.75, 0, top + 0.12), g)
    zcyl("Fan0Hub", 0.09, -0.04, 0.05, MT["dark"], fan, segs=10)
    for k in range(5):
        a = math.radians(72 * k)
        bx(f"Fan0Blade{k}", (0.4, 0.1, 0.02), P(0.22 * math.cos(a), 0.22 * math.sin(a), 0.02), MT["dark"], fan, rot=(math.radians(25), 0, a))
    # exhaust stack (scale pivot) with a rain cap
    st = empty("Stack", c + P(0.85, 0.3, top), g)
    zcyl("StackPipe", 0.11, 0, 1.4, MT["steel"], st, segs=10)
    zcyl("StackSoot", 0.115, 1.15, 1.4, MT["dark"], st, segs=10)
    bx("StackCap", (0.3, 0.3, 0.03), P(0, 0, 1.48), MT["dark"], st, rot=(math.radians(15), 0, 0))
    K.point("Smoke0", P(0, 0, 1.55), st)
    zcyl("Muffler", 0.2, top, top + 0.35, MT["dark"], g, xy=(c.x + 0.85, c.y + 0.3), segs=12)
    # power cable tray to the winder house and the headframe
    tray_z = SLAB + 0.55
    pipe("GenCable0", [c + P(-0.4, W / 2, tray_z), P(c.x - 0.4, WINDER[2] - 0.05, tray_z), P(c.x - 0.4, WINDER[2] - 0.05, SLAB + 1.4)], 0.06, MT["cable"], g)
    pipe("GenCable1", [c + P(L / 2, 0.2, tray_z), P(S.x - 1.3, c.y + 0.2, tray_z), P(S.x - 1.3, S.y - 1.2, tray_z), P(S.x - 1.3, S.y - 1.2, COLLAR + 0.8)], 0.06, MT["cable"], g)
    for k, (x, y) in enumerate([(c.x - 0.4, WINDER[2] - 0.8), (S.x - 1.3, c.y + 1.0), (-0.6, c.y + 0.2)]):
        bx(f"TrayStand{k}", (0.1, 0.1, tray_z - SLAB), P(x, y, (tray_z + SLAB) / 2), MT["trim"], g)
    # optional fuel tank behind the generator
    opt = empty("OptTank", P(-4.7, -1.45, SLAB), g)
    cylinder("TankBody", 0.45, 1.6, P(0, 0, 0.62), MT["hull"], opt, axis="X", segments=16)
    for k in (-1, 1):
        sphere(f"TankEnd{k}", 0.45, P(k * 0.8, 0, 0.62), MT["hull"], opt, 14).scale = (0.35, 1, 1)
        bx(f"TankSaddle{k}", (0.15, 0.8, 0.35), P(k * 0.5, 0, 0.17), MT["dark"], opt)
    cylinder("TankBand", 0.46, 0.2, P(0, 0, 0.62), MT["hazard"], opt, axis="X", segments=16)
    pipe("TankLine", [P(0.3, -0.35, 0.9), P(0.3, -0.75, 0.9), P(0.3, -0.75, 0.55)], 0.04, MT["steel"], opt)


def drill_rig(g):
    c = DRILL.to_3d()
    bx("RigSkid", (2.2, 1.8, 0.22), c + P(0, 0, SLAB + 0.11), MT["dark"], g, 0.02)
    bx("RigDeck", (2.0, 1.6, 0.12), c + P(0, 0, SLAB + 0.28), MT["grate"], g)
    bx("RigPowerPack", (0.9, 0.7, 0.75), c + P(0.5, 0.35, SLAB + 0.72), MT["accent"], g, 0.03)
    bx("RigPowerOwner", (0.92, 0.72, 0.08), c + P(0.5, 0.35, SLAB + 0.95), MT["owner"], g)
    bx("RigCabin", (0.7, 0.6, 0.9), c + P(-0.55, 0.45, SLAB + 0.8), MT["hull"], g, 0.03)
    bx("RigCabinGlass", (0.6, 0.02, 0.4), c + P(-0.55, 0.14, SLAB + 0.95), MT["glass"], g)
    # the mast (scale pivot at its foot): two channel legs, a crown block, the rotary head running on it
    mast = empty("Mast", c + P(-0.1, -0.35, SLAB + 0.34), g)
    H = 5.2
    for side in (-1, 1):
        bx(f"MastLeg{side}", (0.12, 0.2, H), P(side * 0.28, 0, H / 2), MT["hazard"], mast)
    for k in range(int(H / 0.6)):
        bx(f"MastRung{k}", (0.5, 0.05, 0.05), P(0, 0.08, 0.4 + k * 0.6), MT["trim"], mast)
        strut(f"MastX{k}", P(-0.26, 0.09, 0.4 + k * 0.6), P(0.26, 0.09, 1.0 + k * 0.6), 0.02, MT["trim"], mast, 4)
    bx("Crown", (0.75, 0.45, 0.3), P(0, 0, H + 0.12), MT["dark"], mast, 0.02)
    sphere("CrownBeacon", 0.07, P(0.3, 0, H + 0.34), MT["beacon"], mast, 8)
    strut("MastBrace0", P(-0.25, 0.1, H * 0.6), P(-0.4, 0.95, 0.0), 0.05, MT["trim"], mast, 6)
    strut("MastBrace1", P(0.25, 0.1, H * 0.6), P(0.4, 0.95, 0.0), 0.05, MT["trim"], mast, 6)
    # rotary head + drill string: spins and strokes; the string runs down into a casing in the ground
    head = empty("Drill", c + P(-0.1, -0.62, SLAB + 2.9), g)
    bx("DrillHead", (0.55, 0.4, 0.5), P(0, 0, 0), MT["accent"], head, 0.03)
    zcyl("DrillMotor", 0.18, 0.25, 0.55, MT["dark"], head, segs=10)
    zcyl("DrillString", 0.07, -2.75, -0.25, MT["rods"], head, segs=8)
    for k in range(3):
        zcyl(f"DrillJoint{k}", 0.095, -0.6 - k * 0.75, -0.48 - k * 0.75, MT["steel"], head, segs=8)
    bx("DrillStripe", (0.57, 0.42, 0.08), P(0, 0, -0.1), MT["hazard"], head)
    K.point("DrillLow", c + P(-0.1, -0.62, SLAB + 1.7), g)
    zcyl("Casing", 0.16, SLAB, SLAB + 0.45, MT["steel"], g, xy=(c.x - 0.1, c.y - 0.62), segs=10)
    zcyl("CasingFlange", 0.26, SLAB + 0.3, SLAB + 0.38, MT["trim"], g, xy=(c.x - 0.1, c.y - 0.62), segs=12)
    # optional rod rack
    opt = empty("OptRods", P(1.45, 3.9, SLAB), g)
    for k in (-1, 1):
        bx(f"RackStand{k}", (0.1, 0.9, 0.5), P(k * 0.9, 0, 0.25), MT["trim"], opt)
    for k in range(7):
        cylinder(f"Rod{k}", 0.06, 2.2, P(0, -0.32 + (k % 4) * 0.2, 0.56 + (k // 4) * 0.12), MT["rods"], opt, axis="X", segments=8)


def office(g):
    c = OFFICE.to_3d()
    w, d = 2.3, 1.4
    bx("OfficeBase", (w + 0.1, d + 0.1, 0.22), c + P(0, 0, SLAB + 0.11), MT["trim"], g, 0.02)
    bx("OfficeBody", (w, d, 1.1), c + P(0, 0, SLAB + 0.22 + 0.55), MT["hull"], g, 0.04)
    bx("OfficeAccent", (w + 0.02, d + 0.02, 0.3), c + P(0, 0, SLAB + 0.45), MT["accent"], g)
    bx("OfficeGlass", (w - 0.06, d - 0.06, 0.62), c + P(0, 0, SLAB + 1.63), MT["glass"], g, 0.02)
    for k in range(4):
        bx(f"OfficeMullion{k}", (0.07, 0.07, 0.64), c + P((k - 1.5) * 0.74, -d / 2 + 0.03, SLAB + 1.63), MT["trim"], g)
    K.pane_row("OfficeLit", (c.x - w / 2 + 0.15, c.y - d / 2 + 0.025, 0), (1, 0, 0), None, w - 0.3, SLAB + 1.42, SLAB + 1.82, 3, MT["window"], g, frac=0.82)
    bx("OfficeRoof", (w + 0.32, d + 0.32, 0.13), c + P(0, 0, SLAB + 2.0), MT["panel"], g, 0.03)
    bx("OfficeFascia", (w + 0.34, 0.05, 0.28), c + P(0, -d / 2 - 0.18, SLAB + 1.97), MT["dark"], g)
    K.marker("SignFront", c + P(0, -d / 2 - 0.21, SLAB + 1.97), (math.radians(90), 0, 0), 2.2, 0.19, g)
    bx("OfficeOwner", (w + 0.02, d + 0.02, 0.11), c + P(0, 0, SLAB + 1.25), MT["owner"], g)
    bx("OfficeDoor", (0.05, 0.7, 1.05), c + P(w / 2 + 0.01, 0.15, SLAB + 0.75), MT["trim"], g)
    bx("OfficeDoorLamp", (0.06, 0.36, 0.06), c + P(w / 2 + 0.03, 0.15, SLAB + 1.34), MT["window"], g).visible_shadow = False
    bx("OfficeStep", (0.42, 0.85, 0.18), c + P(w / 2 + 0.21, 0.15, SLAB + 0.09), MT["panel"], g, 0.02)
    top = SLAB + 2.07
    bx("OfficeAc", (0.8, 0.6, 0.36), c + P(0.55, 0.15, top + 0.18), MT["panel"], g, 0.03)
    zcyl("OfficeAcFan", 0.22, top + 0.36, top + 0.39, MT["dark"], g, xy=(c.x + 0.55, c.y + 0.15), segs=12)
    zcyl("OfficeMast", 0.04, top, top + 1.8, MT["steel"], g, xy=(c.x - 1.0, c.y + 0.5), segs=6)
    sphere("OfficeMastLamp", 0.06, c + P(-1.0, 0.5, top + 1.85), MT["beacon"], g, 8)
    # a lamp-room rack of helmet lamps (glowing dots by the door) and a first-aid sign
    for k in range(4):
        bx(f"HelmetLamp{k}", (0.05, 0.08, 0.05), c + P(w / 2 + 0.03, -0.5 + k * 0.12, SLAB + 1.0), MT["lamp"], g).visible_shadow = False


def lights(g):
    piv = empty("Masts", P(0, 0, SLAB), g)
    K.light_mast("MastA", 4.55, 4.2, 0.0, 6.2, -135, piv)
    K.light_mast("MastB", -5.3, -4.2, 0.0, 5.8, 45, piv)
    # wall lamps on the winder house and the bin
    for k, (p, yaw) in enumerate(((P(WINDER[1] + 0.06, 1.5, SLAB + 2.1), 0), (P(BIN.x + 0.95, BIN.y, BIN_BOX + 0.1), 0))):
        bx(f"WallLamp{k}", (0.1, 0.3, 0.16), p, MT["dark"], g)
        bx(f"WallLens{k}", (0.02, 0.24, 0.1), p + P(0.06, 0, -0.02), MT["lamp"], g).visible_shadow = False
    K.point("SparkPoint", P(GEN.x + 1.0, GEN.y - 0.8, SLAB + 1.0), g)


BUILDERS = [foundation, collar, headframe_lower, headframe_upper, backstays, sheave_deck, winder_house, hoist, ore_bin,
            conveyor, generator, drill_rig, office, lights]

PIVOTS = {"Wheel", "Hoist", "Cage", "Drill", "OreFlow", "Fan0", "Mast", "Stack", "Masts", "OptTank", "OptRods"}
SEPARATE = {"Belt"} | {f"Ore{k}" for k in range(6)}
EMPTIES = {"OreEnd", "HoistLow", "CageHigh", "DrillLow", "Dust", "Smoke0", "SparkPoint", "SignFront", "SignWinder",
           "LabelBin", "RoofSpot0", "RoofSpot1"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("MINE 01", (0.92, 0.93, 0.95)), "SignWinder": ("HOIST", (0.92, 0.93, 0.95)),
         "LabelBin": ("ORE", (0.08, 0.08, 0.1))}


def on_shot(name):
    """Preview pose: skip halfway up, drill mid-stroke, for every shot."""
    skip = K.bpy.data.objects["Hoist"]
    cage = K.bpy.data.objects["Cage"]
    f = 0.35 if name != "close" else 0.0
    skip.location.z = SKIP_UP - (SKIP_UP - SKIP_DOWN) * f
    cage.location.z = SKIP_DOWN + (SKIP_UP - SKIP_DOWN) * f


def main():
    materials()
    root = K.assemble("mine", PIECES, BUILDERS)
    night = [("NShaft", (1.0, 0.7, 0.4), 120, P(S.x, S.y, 2.0)),
             ("NMastA", (1.0, 0.95, 0.85), 260, P(4.0, 3.6, 6.4)),
             ("NMastB", (1.0, 0.95, 0.85), 260, P(-4.8, -3.6, 6.0)),
             ("NOffice", (1.0, 0.75, 0.45), 90, P(OFFICE.x, OFFICE.y - 1.5, 1.6)),
             ("NDeck", (1.0, 0.95, 0.85), 60, P(S.x + 0.5, S.y, HF_Z - 0.6))]
    shots = {"hero": ((15.5, -19.0, 11.5), (0.0, 0.2, 3.6), 32),
             "close": ((8.5, -7.5, 5.2), (1.6, 0.0, 3.2), 28),
             "night": ((-9.0, -19.0, 10.5), (0.0, 0.3, 3.6), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 18),
             "back": ((-14.0, 17.0, 10.0), (0.0, 0.5, 3.6), 32)}
    K.finish(root, KEEP, SEPARATE, "MineGrime", SLAB, soot=None, shots=shots, night_lights=night, signs=SIGNS,
             rover_at=(-9.4, -6.6, 160), on_shot=on_shot)


main()
