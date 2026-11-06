"""Federation Deep-Core Mine (`deep_core_mine`), game-ready: reopens a used-up deposit by drilling far below it
(slower, power-hungry, a seismic risk), so it reads as a heavier, taller, more industrial cousin of the Mining Complex:
  * an octagonal heavy pad over the old workings; a wide SHAFT COLLAR glowing with heat from the depths ("CoreHeat");
  * a tall tapered DERRICK (lattice, crown block, aviation beacons) over an elevated RIG FLOOR with stairs and the
    driller's cabin ("DEEP CORE nn" sign); the TOP DRIVE and drill string ("Drill") turn and stroke on the derrick's
    guide rails; four SEISMIC STABILISERS (hydraulic dampers) brace the derrick to anchor blocks;
  * two PRESSURE TANKS (heights vary), a COOLANT PUMP house with a flywheel ("Pump") and pipes into the collar, a
    POWER SUBSTATION (it eats power) with a cooler fan ("Fan0") and glowing capacitor coils, a pipe rack with drill
    pipe and a giant spare bit, a spoil conveyor ("Belt") to a skip, light masts, hazard striping.

Frame and conventions (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit; the pad is
an octagon of corner radius 6.7 (inside the 8.5 the scatter keeps clear); the crown tops out at 13.2.

BUILD PIECES (Build0..Build13): Pad, Collar, RigFloor, DerrickLower, DerrickUpper, Stabilisers, TopDrive, Tanks,
PumpHouse, Substation, PipeRack, Spoil, Cabin, Lights.

GAME CONTRACT (BuildingModels.Detailed.cs / PlanetView.Detailed.cs):
  Drill          top drive + string: spin about local Y in Godot, stroke along it (DrillLow = bottom of the stroke).
  Pump           pump flywheel (axle = local Y in Godot). Fan0 substation cooler fan (vertical axis).
  CoreHeat       glow material of the collar's heat ring (pulse; dim idle; off when broken). Belt (UV v every 0.6).
  Vent0          steam/dust out of the shaft; SparkPoint sparks when broken.
  Lamp, Window, Beacon, Coil   glow materials (Coil = the substation's capacitor coils).
  Derrick        not scaled (the stabilisers pin it); Tanks0/Tanks1 scale pivots (pressure tank heights), Masts.
  OptRack        optional pipe rack; SignFront, LabelTank, LabelSub decal markers; RoofSpot0..1, AddOnSpot0..4.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_deepcore.py --game     (export game/models/buildings/deep_core_mine.glb)
  /home/user/.bpyvenv/bin/python blender/build_deepcore.py --game --preview blender/previews/deepcore
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, lathe, strut, empty, cylinder, hoop, railing, ladder, pipe  # noqa: E402
from mathutils import Vector  # noqa: E402
import bmesh  # noqa: E402

CFG = K.setup("deep_core_mine")

SLAB = 0.4
PAD_R = 7.2
ANCHOR_R = 4.9
C = Vector((0.0, 0.4))           # shaft centre
FLOOR_Z = 2.3                    # rig floor
BASE_W, TOP_W, TOP_Z = 1.9, 0.75, 12.4
TANKS = [Vector((-5.2, -0.95)), Vector((-5.3, 0.55))]
PUMP = Vector((-4.4, 2.5))
SUB = Vector((4.6, 2.2))
CABIN = Vector((3.6, -1.7))
RACK = Vector((1.2, -4.6))
SPOIL_A, SPOIL_B = Vector((1.7, -0.3, 1.1)), Vector((4.6, 0.0, 2.0))
PIECES = ["Pad", "Collar", "RigFloor", "DerrickLower", "DerrickUpper", "Stabilisers", "TopDrive", "Tanks", "PumpHouse",
          "Substation", "PipeRack", "Spoil", "Cabin", "Lights"]
CORNERS = ((-1, -1), (1, -1), (1, 1), (-1, 1))


def materials():
    K.materials()
    K.belt_material()
    K.mat("heat", "CoreHeat", (1.0, 0.42, 0.12), 0.0, 0.3, emission=(1.0, 0.38, 0.08), strength=6.0)
    K.mat("void", "Void", (0.01, 0.01, 0.012), 0.0, 0.9)
    K.mat("coil", "Coil", (0.4, 0.75, 1.0), 0.0, 0.3, emission=(0.35, 0.7, 1.0), strength=3.0)
    K.mat("tank", "TankPaint", (0.36, 0.48, 0.64), 0.35, 0.42, textured=True)
    K.mat("rods", "Rods", (0.5, 0.52, 0.56), 0.85, 0.35, textured=True)
    K.mat("insulator", "Insulator", (0.55, 0.32, 0.2), 0.1, 0.35, textured=True)
    K.mat("label", "Label", (0.9, 0.9, 0.88), 0.0, 0.6, textured=True)
    K.mat("oreheap", "OreHeap", (0.24, 0.21, 0.19), 0.05, 0.95, textured=True)


def hw(z):
    return BASE_W + (TOP_W - BASE_W) * (z - FLOOR_Z) / (TOP_Z - FLOOR_Z)


def corner(sx, sy, z):
    w = hw(z)
    return P(C.x + sx * w, C.y + sy * w, z)


# ---------------------------------------------------------------- pieces


def pad(g):
    lathe("Pad", [(0.0, 0.0), (PAD_R, 0.0), (PAD_R, SLAB), (0.0, SLAB)], MT["concrete"], g, (0, 0, 0), 8, smooth=False)
    lathe("PadSkirt", [(0.0, 0.0), (PAD_R + 0.2, 0.0), (PAD_R + 0.2, 0.16), (0.0, 0.16)], MT["dark"], g, (0, 0, 0), 8, smooth=False)
    z = SLAB + 0.003
    K.ring_mesh("PadLine", PAD_R - 0.35, PAD_R - 0.28, SLAB, z + 0.001, MT["hazard"], g, segs=8)
    qs = []
    for k in range(8):
        a = math.radians(45 * k)
        d = Vector((math.cos(a), math.sin(a), 0))
        s = Vector((-d.y, d.x, 0)) * 0.02
        p0, p1 = d * 3.2, d * (PAD_R - 0.4)
        qs.append((p0 - s + P(0, 0, z), p1 - s + P(0, 0, z), p1 + s + P(0, 0, z), p0 + s + P(0, 0, z)))
    K.faces_obj("Joints", qs, MT["dark"], g)
    K.bollards("Bollard", [(-0.9, -6.2), (0.9, -6.2), (-6.2, -2.4), (6.3, -2.2)], SLAB, g)
    for k, (x, y, yaw) in enumerate([(-7.9, -2.6, 90), (8.0, -1.4, -90), (-2.2, 7.9, 0), (2.2, 7.9, 0), (-6.1, 5.0, 140)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)
    K.point("RoofSpot0", P(PUMP.x, PUMP.y + 0.3, SLAB + 2.45), g)
    K.point("RoofSpot1", P(CABIN.x - 0.2, CABIN.y + 0.1, FLOOR_Z + 2.35), g)


def collar(g):
    K.ring_mesh("Collar", 1.35, 2.5, SLAB, 1.1, MT["concrete"], g, xy=C, segs=24)
    zcyl("ShaftMouth", 1.37, SLAB + 0.005, SLAB + 0.02, MT["void"], g, xy=C, segs=24)
    K.ring_mesh("HeatRing", 1.36, 1.6, 1.1, 1.14, MT["heat"], g, xy=C, segs=24).visible_shadow = False
    K.band("HeatWall", 1.345, SLAB + 0.05, 1.0, MT["heat"], g, xy=C, segs=24).visible_shadow = False
    qs_y, qs_k = [], []
    for k in range(20):
        a0, a1 = 2 * math.pi * k / 20, 2 * math.pi * (k + 1) / 20
        q = tuple(P(C.x + r * math.cos(a), C.y + r * math.sin(a), 1.104) for r, a in ((1.8, a0), (2.45, a0 + 0.12), (2.45, a1 + 0.12), (1.8, a1)))
        (qs_y if k % 2 == 0 else qs_k).append(q)
    K.faces_obj("CollarStripeY", qs_y, MT["hazard"], g)
    K.faces_obj("CollarStripeK", qs_k, MT["dark"], g)
    # the casing coming up out of the shaft and the blowout preventer stack under the rig floor
    zcyl("Casing", 0.42, SLAB, FLOOR_Z - 0.3, MT["steel"], g, xy=C, segs=16)
    for k, z in enumerate((1.25, 1.6)):
        zcyl(f"Bop{k}", 0.75, z, z + 0.28, MT["valve"], g, xy=C, segs=16)
    K.point("Vent0", P(C.x + 0.9, C.y - 0.9, 1.3), g, 0.5)


def rig_floor(g):
    # the elevated drill floor on four heavy posts, with the derrick legs standing on it
    w = BASE_W + 0.8
    for k, (sx, sy) in enumerate(CORNERS):
        p = P(C.x + sx * (w - 0.3), C.y + sy * (w - 0.3), 0)
        bx(f"FloorPost{k}", (0.45, 0.45, FLOOR_Z - SLAB), p + P(0, 0, (FLOOR_Z + SLAB) / 2), MT["hull"], g, 0.02)
        for j in range(3):
            bx(f"PostBand{k}{j}", (0.47, 0.47, 0.2), p + P(0, 0, SLAB + 0.12 + j * 0.4), MT["hazard" if j % 2 == 0 else "dark"], g)
    bx("FloorBeams", (2 * w - 0.2, 2 * w - 0.2, 0.3), P(C.x, C.y, FLOOR_Z - 0.2), MT["trim"], g)
    deck = bmesh.new()
    # deck with a hole round the rotary
    pts_out = [P(C.x + sx * w, C.y + sy * w, FLOOR_Z) for sx, sy in CORNERS]
    for k in range(4):
        a, b = pts_out[k], pts_out[(k + 1) % 4]
        ia, ib = C.to_3d() + (a - C.to_3d()) * 0.22 + P(0, 0, FLOOR_Z * 0.78), C.to_3d() + (b - C.to_3d()) * 0.22 + P(0, 0, FLOOR_Z * 0.78)
        vs = [deck.verts.new(v) for v in (a, b, P(ib.x, ib.y, FLOOR_Z), P(ia.x, ia.y, FLOOR_Z))]
        deck.faces.new(vs)
    o = K.mesh_object("Deck", deck, MT["grate"], g)
    m = o.modifiers.new("Thick", "SOLIDIFY")
    m.thickness = 0.1
    for k in range(4):
        a, b = pts_out[k], pts_out[(k + 1) % 4]
        beam(f"DeckEdge{k}", a + P(0, 0, -0.05), b + P(0, 0, -0.05), 0.12, MT["owner"], g, 0.14)
    # the doghouse platform for the driller's cabin, cantilevered off the front-right
    bx("CabinDeck", (1.9, 1.7, 0.12), P(3.6, -1.7, FLOOR_Z - 0.06), MT["grate"], g)
    bx("CabinDeckBeam", (1.9, 1.5, 0.22), P(3.6, -1.7, FLOOR_Z - 0.23), MT["trim"], g)
    for k, (x, y) in enumerate(((4.4, -2.4), (4.4, -1.0))):
        bx(f"CabinPost{k}", (0.22, 0.22, FLOOR_Z - SLAB), P(x, y, (FLOOR_Z + SLAB) / 2), MT["hull"], g)
    zcyl("Rotary", 0.6, FLOOR_Z - 0.05, FLOOR_Z + 0.2, MT["dark"], g, xy=C, segs=16)
    hoop("RotaryRing", 0.62, FLOOR_Z + 0.21, 0.06, MT["hazard"], g, xy=C, segs=16)
    railing("FloorRail", [(C.x - w + 0.06, C.y - w + 1.3), (C.x - w + 0.06, C.y + w - 0.06), (C.x + w - 0.06, C.y + w - 0.06),
                          (C.x + w - 0.06, C.y - w + 0.06), (C.x - w + 1.4, C.y - w + 0.06)], FLOOR_Z, g)
    # stairs up from the front-left
    n = 8
    for k in range(n):
        t = (k + 0.5) / n
        x = C.x - w + 0.55
        y = C.y - w - 2.4 + 2.4 * t
        bx(f"Stair{k}", (0.9, 0.3, 0.06), P(x, y, SLAB + (FLOOR_Z - SLAB) * t), MT["grate"], g)
    for s in (-1, 1):
        a = P(C.x - w + 0.55 + s * 0.47, C.y - w - 2.4, SLAB)
        b = P(C.x - w + 0.55 + s * 0.47, C.y - w, FLOOR_Z)
        beam(f"Stringer{s}", a, b, 0.07, MT["trim"], g, 0.25)
        beam(f"StairRail{s}", a + P(0, 0, 0.85), b + P(0, 0, 0.85), 0.045, MT["rail"], g)
        for j in range(3):
            p = a.lerp(b, (j + 0.5) / 3)
            bx(f"StairPost{s}{j}", (0.045, 0.045, 0.85), p + P(0, 0, 0.42), MT["rail"], g)


def lattice(g, z0, z1, levels, name, w=0.3):
    for k, (sx, sy) in enumerate(CORNERS):
        beam(f"{name}Chord{k}", corner(sx, sy, z0), corner(sx, sy, z1), w, MT["hull"], g)
    for i, z in enumerate(levels):
        for k in range(4):
            a, b = CORNERS[k], CORNERS[(k + 1) % 4]
            beam(f"{name}Girt{i}{k}", corner(*a, z), corner(*b, z), 0.16, MT["hull"], g, 0.18)
    for i, (za, zb) in enumerate(zip(levels, levels[1:])):
        for k in range(4):
            a, b = CORNERS[k], CORNERS[(k + 1) % 4]
            strut(f"{name}BraceA{i}{k}", corner(*a, za), corner(*b, zb), 0.045, MT["trim"], g, 5)
            strut(f"{name}BraceB{i}{k}", corner(*b, za), corner(*a, zb), 0.045, MT["trim"], g, 5)


def derrick_lower(g):
    lattice(g, FLOOR_Z, 7.2, [FLOOR_Z + 0.05, 3.9, 5.5, 7.2], "DkL")
    for k in range(4):
        a, b = CORNERS[k], CORNERS[(k + 1) % 4]
        beam(f"OwnerGirt{k}", corner(*a, 5.5), corner(*b, 5.5), 0.17, MT["owner"], g, 0.08)
    # guide rails for the top drive on the front face, inside
    for s in (-1, 1):
        bx(f"DriveRail{s}", (0.08, 0.12, TOP_Z - FLOOR_Z - 1.2), P(C.x + s * 0.45, C.y - 0.62, (TOP_Z + FLOOR_Z) / 2 - 0.5), MT["steel"], g)
    # monkey board (racking platform) with its rail
    z = 7.2
    bx("MonkeyBoard", (1.4, 0.7, 0.07), P(C.x, C.y + hw(z) + 0.3, z), MT["grate"], g)
    railing("MonkeyRail", [(C.x - 0.7, C.y + hw(z) + 0.62), (C.x + 0.7, C.y + hw(z) + 0.62)], z + 0.04, g, h=0.8, post=0.7)
    c = corner(1, -1, FLOOR_Z)
    ladder("DkLadder", P(c.x + 0.42, c.y + 0.1, FLOOR_Z), 7.2, (1, 0), g)


def derrick_upper(g):
    lattice(g, 7.2, TOP_Z, [7.2, 8.9, 10.6, TOP_Z], "DkU", w=0.26)
    # crown block, its deck, sheaves, gin pole, aviation beacons
    bx("CrownDeck", (2 * TOP_W + 0.8, 2 * TOP_W + 0.8, 0.12), P(C.x, C.y, TOP_Z + 0.06), MT["grate"], g)
    railing("CrownRail", [(C.x - TOP_W - 0.35, C.y - TOP_W - 0.35), (C.x + TOP_W + 0.35, C.y - TOP_W - 0.35),
                          (C.x + TOP_W + 0.35, C.y + TOP_W + 0.35), (C.x - TOP_W - 0.35, C.y + TOP_W + 0.35)], TOP_Z + 0.12, g, h=0.7, post=0.6, closed=True)
    bx("CrownBlock", (1.0, 0.8, 0.5), P(C.x, C.y, TOP_Z + 0.4), MT["accent"], g, 0.03)
    for k in (-1, 1):
        cylinder(f"CrownSheave{k}", 0.32, 0.1, P(C.x + k * 0.22, C.y, TOP_Z + 0.62), MT["steel"], g, axis="X", segments=16)
    zcyl("GinPole", 0.05, TOP_Z + 0.12, TOP_Z + 0.8, MT["steel"], g, xy=(C.x + 0.9, C.y + 0.9), segs=6)
    for k, (sx, sy) in enumerate(CORNERS):
        sphere(f"CrownBeacon{k}", 0.08, P(C.x + sx * (TOP_W + 0.35), C.y + sy * (TOP_W + 0.35), TOP_Z + 0.86), MT["beacon"], g, 8)
    sphere("TopBeacon", 0.1, P(C.x + 0.9, C.y + 0.9, TOP_Z + 0.86), MT["beacon"], g, 8)
    # a painted owner board and lamps halfway up
    bx("DkBoard", (1.2, 0.06, 0.55), P(C.x, corner(0, -1, 9.6).y - 0.08, 9.6), MT["owner"], g)
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1))):
        p = corner(sx, sy, 8.9)
        bx(f"DkLamp{k}", (0.3, 0.2, 0.18), p + P(0, -0.2, -0.2), MT["dark"], g)
        bx(f"DkLens{k}", (0.24, 0.02, 0.12), p + P(0, -0.31, -0.22), MT["lamp"], g).visible_shadow = False


def stabilisers(g):
    for k, (sx, sy) in enumerate(CORNERS):
        top = corner(sx, sy, 6.4)
        d = Vector((sx, sy, 0)).normalized()
        foot = P(C.x, C.y, SLAB) + d * ANCHOR_R
        foot.z = SLAB
        bx(f"Anchor{k}", (1.0, 1.0, 0.55), foot + P(0, 0, 0.27), MT["concrete"], g, 0.04)
        bx(f"AnchorPlate{k}", (0.7, 0.7, 0.06), foot + P(0, 0, 0.58), MT["steel"], g)
        mid = foot.lerp(top, 0.5)
        # damper: a fat barrel at the bottom, a chrome rod into a sleeve at the top
        beam(f"Damper{k}", foot + P(0, 0, 0.6), mid, 0.34, MT["accent"], g)
        beam(f"DamperBand{k}", foot.lerp(mid, 0.12) + P(0, 0, 0.6 * 0.88), foot.lerp(mid, 0.22) + P(0, 0, 0.6 * 0.78), 0.36, MT["hazard"], g)
        strut(f"DamperRod{k}", mid, top, 0.09, MT["steel"], g, 10)
        bx(f"DamperClevis{k}", (0.3, 0.3, 0.3), top, MT["trim"], g, 0.02)
        pipe(f"DamperHose{k}", [foot + P(0, 0, 0.9), foot.lerp(mid, 0.6) + P(0, 0, 0.35)], 0.03, MT["cable"], g, segs=5, joints=False)


def top_drive(g):
    drv = empty("Drill", P(C.x, C.y - 0.3, 9.6), g)
    bx("DriveBody", (0.9, 0.55, 1.1), P(0, 0, 0), MT["hazard"], drv, 0.04)
    bx("DriveMotor", (0.6, 0.45, 0.6), P(0, 0.05, 0.85), MT["dark"], drv, 0.03)
    zcyl("DriveSwivel", 0.18, 1.15, 1.5, MT["steel"], drv, segs=10)
    bx("DriveStripe", (0.92, 0.57, 0.12), P(0, 0, -0.35), MT["dark"], drv)
    # the travelling block's ropes up to the crown (move with the drive; the crown is fixed)
    for s in (-1, 1):
        strut(f"DriveLine{s}", P(s * 0.2, 0, 1.5), P(s * 0.2, 0, TOP_Z + 0.6 - 9.6), 0.025, MT["cable"], drv, 5)
    # the drill string down through the rotary into the casing
    zcyl("Kelly", 0.13, -(9.6 - FLOOR_Z) - 1.2, -0.55, MT["rods"], drv, segs=8)
    for k in range(5):
        zcyl(f"ToolJoint{k}", 0.17, -1.4 - k * 1.45, -1.25 - k * 1.45, MT["steel"], drv, segs=8)
    K.point("DrillLow", P(C.x, C.y - 0.3, 4.4), g)


def tanks(g):
    for k, c in enumerate(TANKS):
        piv = empty(f"Tanks{k}", P(c.x, c.y, SLAB), g)
        h = 3.2 if k == 0 else 2.6
        r = 0.72
        lathe(f"Tank{k}", [(0.0, 0.5), (r * 0.7, 0.52), (r, 0.75), (r, h), (r * 0.7, h + 0.3), (0.0, h + 0.36)], MT["tank"], piv, (0, 0, 0), 20, smooth=True)
        K.band(f"Tank{k}Band", r + 0.012, h - 0.55, h - 0.38, MT["owner"], piv, segs=20)
        K.band(f"Tank{k}Band2", r + 0.012, 1.0, 1.15, MT["hazard"], piv, segs=20)
        for j in range(4):
            a = math.radians(45 + 90 * j)
            bx(f"Tank{k}Leg{j}", (0.14, 0.14, 0.75), P(0.52 * math.cos(a), 0.52 * math.sin(a), 0.38), MT["hull"], piv)
        zcyl(f"Tank{k}Valve", 0.09, h + 0.36, h + 0.6, MT["steel"], piv, segs=8)
        hoop(f"Tank{k}Wheel", 0.13, h + 0.62, 0.03, MT["valve"], piv, segs=10)
        ladder(f"Tank{k}Ladder", P(-r - 0.05, 0, 0.5), h, (-1, 0), piv, cage=False, width=0.34)
        pipe(f"Tank{k}Line", [P(r * 0.7, 0.4, 0.6), P(r * 0.7 + 0.5, 0.4, 0.6)], 0.06, MT["steel"], piv)
    bx("TankPlate", (0.9, 0.04, 0.28), TANKS[0].to_3d() + P(0, -0.73, SLAB + 2.0), MT["label"], g)
    K.marker("LabelTank", TANKS[0].to_3d() + P(0, -0.755, SLAB + 2.0), (math.radians(90), 0, 0), 0.8, 0.18, g)
    pipe("TankHeader", [TANKS[1].to_3d() + P(1.2, 0.4, SLAB + 0.6), P(C.x - 1.2, C.y - 1.6, SLAB + 0.6), P(C.x - 0.6, C.y - 0.9, 1.4)], 0.07, MT["steel"], g)


def pump_house(g):
    c = PUMP.to_3d()
    w, d, h = 2.2, 1.8, 2.0
    bx("PumpPlinth", (w + 0.12, d + 0.12, 0.25), c + P(0, 0, SLAB + 0.12), MT["trim"], g, 0.02)
    bx("PumpWalls", (w, d, h), c + P(0, 0, SLAB + 0.25 + h / 2), MT["panel"], g, 0.04)
    bx("PumpAccent", (w + 0.02, d + 0.02, 0.4), c + P(0, 0, SLAB + 0.6), MT["accent"], g)
    bx("PumpRoof", (w + 0.3, d + 0.3, 0.14), c + P(0, 0, SLAB + 0.32 + h), MT["hull"], g, 0.03)
    bx("PumpOwner", (w + 0.03, d + 0.03, 0.1), c + P(0, 0, SLAB + h - 0.15), MT["owner"], g)
    for k in range(2):
        zcyl(f"PumpVent{k}", 0.16, SLAB + 0.4 + h, SLAB + 0.75 + h, MT["trim"], g, xy=(c.x + 0.5 - k * 1.0, c.y + 0.4), segs=10)
        zcyl(f"PumpVentCap{k}", 0.24, SLAB + 0.75 + h, SLAB + 0.82 + h, MT["hull"], g, xy=(c.x + 0.5 - k * 1.0, c.y + 0.4), segs=10)
    K.pane_row("PumpWindows", (c.x + w / 2 + 0.012, c.y - d / 2 + 0.2, 0), (0, 1, 0), None, d - 0.4, SLAB + 1.4, SLAB + 1.75, 2, MT["window"], g)
    # the pump outside on the front: motor, casing, flywheel (Pump), pipes into the collar
    p = c + P(0.6, -d / 2 - 0.35, SLAB)
    bx("PumpSkid", (1.6, 0.6, 0.15), p + P(0, 0, 0.08), MT["dark"], g, 0.02)
    cylinder("PumpMotor", 0.3, 0.7, p + P(-0.35, 0, 0.5), MT["accent"], g, axis="X", segments=14)
    sphere("PumpCasing", 0.36, p + P(0.35, 0, 0.5), MT["hull"], g, 14)
    fw = empty("Pump", p + P(-0.8, 0, 0.5), g)
    fw.rotation_euler = (0, math.radians(90), 0)
    zcyl("PumpWheel", 0.42, -0.05, 0.05, MT["steel"], fw, segs=20)
    for k in range(4):
        bx(f"PumpSpoke{k}", (0.75, 0.08, 0.06), P(0, 0, 0.0), MT["trim"], fw, rot=(0, 0, math.radians(45 * k)))
    bx("PumpMark", (0.15, 0.1, 0.12), P(0.32, 0, 0), MT["hazard"], fw)
    pipe("Coolant0", [p + P(0.7, 0, 0.5), P(C.x - 1.9, p.y, 0.5), P(C.x - 1.9, C.y - 0.3, 0.9), P(C.x - 1.4, C.y - 0.3, 0.9)], 0.11, MT["steel"], g)
    pipe("Coolant1", [c + P(w / 2, 0.45, SLAB + 0.6), P(C.x - 1.6, c.y + 0.45, SLAB + 0.6), P(C.x - 1.6, C.y + 0.9, 0.9)], 0.11, MT["copper"], g)


def substation(g):
    c = SUB.to_3d()
    bx("SubPad", (2.4, 2.0, 0.2), c + P(0, 0, SLAB + 0.1), MT["concrete"], g, 0.02)
    bx("SubXfmr", (1.4, 1.0, 1.4), c + P(-0.5, 0.3, SLAB + 0.9), MT["trim"], g, 0.04)
    for j in range(7):
        bx(f"SubFin{j}", (0.05, 1.12, 1.1), c + P(-1.1 + j * 0.2, 0.3, SLAB + 0.85), MT["steel"], g)
    bx("SubOwner", (1.42, 1.02, 0.1), c + P(-0.5, 0.3, SLAB + 1.45), MT["owner"], g)
    for j in range(3):
        b = c + P(-0.9 + j * 0.4, 0.3, SLAB + 1.6)
        for i in range(4):
            zcyl(f"SubIns{j}_{i}", 0.11 - 0.01 * (i % 2), b.z + i * 0.14, b.z + i * 0.14 + 0.1, MT["insulator"], g, xy=(b.x, b.y), segs=10)
    # capacitor coils (glow when it draws) and the cooler with its fan
    for k in range(3):
        p = c + P(0.75, -0.65 + k * 0.6, SLAB)
        zcyl(f"CapBase{k}", 0.26, p.z + 0.2, p.z + 0.4, MT["dark"], g, xy=(p.x, p.y), segs=12)
        zcyl(f"CapCoil{k}", 0.2, p.z + 0.4, p.z + 1.1, MT["coil"], g, xy=(p.x, p.y), segs=12).visible_shadow = False
        for j in range(4):
            hoop(f"CapRing{k}{j}", 0.21, p.z + 0.5 + j * 0.17, 0.04, MT["copper"], g, xy=(p.x, p.y), segs=12)
        zcyl(f"CapTop{k}", 0.24, p.z + 1.1, p.z + 1.22, MT["trim"], g, xy=(p.x, p.y), segs=12)
    q = c + P(-0.5, -0.75, SLAB + 0.2)
    bx("Cooler", (1.2, 0.6, 0.6), q + P(0, 0, 0.3), MT["panel"], g, 0.03)
    fan = empty("Fan0", q + P(0, 0, 0.63), g)
    zcyl("Fan0Hub", 0.07, -0.03, 0.04, MT["dark"], fan, segs=10)
    for k in range(4):
        a = math.radians(90 * k)
        bx(f"Fan0Blade{k}", (0.42, 0.1, 0.02), P(0.2 * math.cos(a), 0.2 * math.sin(a), 0.01), MT["dark"], fan, rot=(math.radians(25), 0, a))
    hoop("FanRing", 0.28, 0.64 + q.z, 0.04, MT["steel"], g, xy=(q.x, q.y), segs=16)
    # the fat power feed to the rig floor (it eats power)
    pipe("PowerFeed", [c + P(-1.2, 0.3, SLAB + 0.5), P(C.x + 2.6, c.y + 0.3, SLAB + 0.5), P(C.x + 2.6, C.y + 1.6, FLOOR_Z - 0.4)], 0.1, MT["cable"], g)
    bx("SubPlate", (0.7, 0.04, 0.26), c + P(-0.5, -0.21, SLAB + 1.15), MT["hazard"], g)
    K.marker("LabelSub", c + P(-0.5, -0.235, SLAB + 1.15), (math.radians(90), 0, 0), 0.6, 0.14, g)
    # fence
    pts = [(c.x - 1.25, c.y - 1.05), (c.x + 1.25, c.y - 1.05), (c.x + 1.25, c.y + 1.05)]
    for k, (a, b) in enumerate(zip(pts, pts[1:])):
        a3, b3 = P(a[0], a[1], SLAB), P(b[0], b[1], SLAB)
        for i in range(5):
            bx(f"Fence{k}_{i}", (0.05, 0.05, 1.1), a3.lerp(b3, i / 4) + P(0, 0, 0.55), MT["steel"], g)
        for zz in (0.5, 1.05):
            beam(f"FenceRail{k}_{zz}", a3 + P(0, 0, zz), b3 + P(0, 0, zz), 0.03, MT["steel"], g)


def pipe_rack(g):
    opt = empty("OptRack", P(RACK.x, RACK.y, SLAB), g)
    for k in (-1, 1):
        bx(f"RackStand{k}", (0.12, 1.1, 0.65), P(k * 1.4, 0, 0.32), MT["trim"], opt)
        bx(f"RackFoot{k}", (0.4, 1.2, 0.06), P(k * 1.4, 0, 0.03), MT["steel"], opt)
    for k in range(9):
        cylinder(f"Pipe{k}", 0.08, 3.4, P(0, -0.4 + (k % 5) * 0.2, 0.72 + (k // 5) * 0.16), MT["rods"], opt, axis="X", segments=8)
    # the giant spare bit on its stand (a tri-cone)
    b = P(-2.4, -0.1, 0)
    bx("BitStand", (0.9, 0.9, 0.3), b + P(0, 0, 0.15), MT["dark"], opt, 0.02)
    zcyl("BitShank", 0.22, 0.3, 0.85, MT["steel"], opt, xy=(b.x, b.y), segs=12)
    for k in range(3):
        a = math.radians(120 * k)
        cone = cylinder(f"BitCone{k}", 0.32, 0.5, b + P(0.22 * math.cos(a), 0.22 * math.sin(a), 1.05), MT["copper"], opt, axis="Z", segments=10, radius2=0.08)
        cone.rotation_euler = (math.radians(35) * math.sin(a), -math.radians(35) * math.cos(a), 0)


def spoil(g):
    A, B = SPOIL_A, SPOIL_B
    u = (B - A).normalized()
    L = (B - A).length
    R = K.frame(u)
    s = R.col[0].to_3d()
    n = R.col[2].to_3d()
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    prev = None
    for i in range(9):
        p = A + u * (L * i / 8) + n * 0.02
        row = (bm.verts.new(p - s * 0.24), bm.verts.new(p + s * 0.24))
        if prev:
            f = bm.faces.new((prev[0], prev[1], row[1], row[0]))
            for loop in f.loops:
                v = Vector(loop.vert.co) - A
                loop[uv].uv = (0.5 + v.dot(s) / 0.48, v.dot(u) / 0.6)
        prev = row
    K.mesh_object("Belt", bm, MT["belt"], g)
    for side in (-1, 1):
        obox(f"SpoilStringer{side}", (0.07, L + 0.2, 0.24), A + u * (L / 2) + s * (side * 0.3) - n * 0.1, u, MT["trim"], g)
    t, k = 0.2, 0
    while t < L - 0.1:
        c = A + u * t - n * 0.06
        strut(f"SpoilIdler{k}", c - s * 0.26, c + s * 0.26, 0.045, MT["steel"], g, 6)
        t, k = t + 0.45, k + 1
    for j, tt in enumerate((L * 0.45, L * 0.95)):
        c = A + u * tt - n * 0.2
        for side in (-1, 1):
            top = c + s * (side * 0.32)
            bx(f"SpoilBent{j}{side}", (0.1, 0.1, top.z - SLAB), P(top.x, top.y, (top.z + SLAB) / 2), MT["hull"], g)
    # the spoil skip it drops into and a small heap round it
    sk = B + u * 0.7
    bx("Skip", (1.5, 1.0, 0.7), P(sk.x, sk.y, SLAB + 0.35), MT["hazard"], g, 0.03, rot=(0, 0, math.atan2(u.y, u.x)))
    K.heap("SkipLoad", 0.55, 0.35, P(sk.x, sk.y, SLAB + 0.6), MT["oreheap"], g, seed=5, segs=10)
    K.heap("SpoilHeap", 0.8, 0.5, P(6.0, -0.75, SLAB - 0.05), MT["oreheap"], g, seed=9, segs=12)


def cabin(g):
    c = CABIN.to_3d()
    w, d = 1.7, 1.3
    z0 = FLOOR_Z + 0.05
    bx("CabinBody", (w, d, 1.2), c + P(0, 0, z0 + 0.6), MT["hull"], g, 0.04)
    bx("CabinAccent", (w + 0.02, d + 0.02, 0.28), c + P(0, 0, z0 + 0.2), MT["accent"], g)
    bx("CabinGlass", (w - 0.06, d - 0.06, 0.6), c + P(0, 0, z0 + 1.5), MT["glass"], g, 0.02)
    K.pane_row("CabinLit", (c.x - w / 2 + 0.12, c.y - d / 2 + 0.025, 0), (1, 0, 0), None, w - 0.24, z0 + 1.3, z0 + 1.7, 2, MT["window"], g, frac=0.82)
    bx("CabinRoof", (w + 0.3, d + 0.3, 0.12), c + P(0, 0, z0 + 1.86), MT["panel"], g, 0.03)
    bx("CabinFascia", (w + 0.32, 0.05, 0.26), c + P(0, -d / 2 - 0.16, z0 + 1.83), MT["dark"], g)
    K.marker("SignFront", c + P(0, -d / 2 - 0.19, z0 + 1.83), (math.radians(90), 0, 0), 1.75, 0.17, g)
    bx("CabinOwner", (w + 0.02, d + 0.02, 0.1), c + P(0, 0, z0 + 1.15), MT["owner"], g)
    zcyl("CabinMast", 0.035, z0 + 1.92, z0 + 3.2, MT["steel"], g, xy=(c.x + 0.6, c.y + 0.4), segs=6)
    sphere("CabinMastLamp", 0.06, c + P(0.6, 0.4, z0 + 3.25), MT["beacon"], g, 8)


def lights(g):
    piv = empty("Masts", P(0, 0, SLAB), g)
    K.light_mast("MastA", -4.4, -4.5, 0.0, 6.6, 45, piv)
    K.light_mast("MastB", 5.0, -3.6, 0.0, 6.0, 140, piv)
    for k, (sx, sy) in enumerate(CORNERS):
        p = P(C.x + sx * (BASE_W + 0.6), C.y + sy * (BASE_W + 0.6), FLOOR_Z)
        bx(f"FloorLamp{k}", (0.24, 0.24, 0.14), p + P(0, 0, 0.2), MT["dark"], g)
        bx(f"FloorLens{k}", (0.18, 0.18, 0.02), p + P(0, 0, 0.27), MT["lamp"], g).visible_shadow = False
    K.point("SparkPoint", P(SUB.x + 0.75, SUB.y, SLAB + 1.2), g)


BUILDERS = [pad, collar, rig_floor, derrick_lower, derrick_upper, stabilisers, top_drive, tanks, pump_house, substation,
            pipe_rack, spoil, cabin, lights]

PIVOTS = {"Drill", "Pump", "Fan0", "Tanks0", "Tanks1", "Masts", "OptRack"}
SEPARATE = {"Belt"}
EMPTIES = {"DrillLow", "Vent0", "SparkPoint", "SignFront", "LabelTank", "LabelSub", "RoofSpot0", "RoofSpot1"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("DEEP CORE 01", (0.92, 0.93, 0.95)), "LabelTank": ("COOLANT", (0.08, 0.08, 0.1)),
         "LabelSub": ("DANGER", (0.08, 0.08, 0.1))}


def main():
    materials()
    root = K.assemble("deep_core_mine", PIECES, BUILDERS)
    night = [("NShaft", (1.0, 0.5, 0.2), 500, P(C.x, C.y - 1.2, 1.6)),
             ("NMastA", (1.0, 0.95, 0.85), 260, P(-4.1, -3.9, 6.6)),
             ("NMastB", (1.0, 0.95, 0.85), 260, P(4.5, -3.1, 6.0)),
             ("NFloor", (1.0, 0.95, 0.85), 120, P(C.x, C.y - 1.5, FLOOR_Z + 1.5)),
             ("NSub", (0.4, 0.7, 1.0), 60, P(SUB.x + 0.75, SUB.y - 0.6, 1.5))]
    shots = {"hero": ((16.5, -20.5, 13.0), (0.0, 0.3, 4.6), 32),
             "close": ((7.5, -8.0, 4.8), (0.6, -0.6, 2.8), 28),
             "night": ((-10.0, -20.0, 12.0), (0.0, 0.3, 4.6), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 17),
             "back": ((-15.0, 18.0, 11.5), (0.0, 0.5, 4.6), 32)}
    K.finish(root, KEEP, SEPARATE, "DeepCoreGrime", SLAB, soot=None, shots=shots, night_lights=night, signs=SIGNS,
             rover_at=(-9.2, -6.8, 160))


main()
