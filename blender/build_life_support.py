"""Federation Life Support Plant (`life_support_plant`), game-ready (hostile worlds slice, 2026-11-08; contract
docs/hostile-worlds-contract.md): air and heat for a base on a world you can't breathe on (Veyra).

An air-processing plant as the RTS camera sees it from the front:
  * an octagonal pad (radius 7.0) with markings and a pipe vault at the front edge;
  * the COMPRESSOR HALL at the back left: intake louvres, a roll-up door, lit windows, owner/accent bands; on its roof
    two big intake FANS in housings ("Fan0", "Fan1"), an AC unit and the antenna mast with the beacon;
  * three SCRUBBER towers at the back right: pressure vessels with a stack each (scale pivot "Stacks"; steam at
    "Vent0", "Vent1"), a duct header from the hall, a catwalk with railings and a ladder;
  * HEAT EXCHANGERS on the right: a finned plate unit and a shell-and-tube exchanger with copper loops;
  * three gas TANKS at the front ("Tank0" O2, "Tank1" N2, "Tank2" O2; label markers LabelTank0..2), a manifold, and the
    supply pipes that run to the front edge and down into the ground (to the base);
  * the CONTROL kiosk at the front left with the "Status" board (green / amber / red) and the "LIFE SUPPORT nn" sign;
  * light masts, lamps, bollards, hazard striping, frost on the cold edges.

Frame and conventions (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit.

BUILD PIECES (Build0..Build10): Pad, Hall, HallRoof, Scrubbers, Ducts, HeatExchangers, Tanks, Pipes, Control,
Catwalks, Lights.

GAME CONTRACT (BuildingModels.Hostile.cs; names from docs/hostile-worlds-contract.md):
  Fan0, Fan1     roof intake fans: spin about local Y in Godot (Blender Z).
  Tank0..Tank2   gas tank pivots (Tank0 O2, Tank1 N2, Tank2 O2); LabelTank0..2 decal markers on them.
  Vent0, Vent1   empties at the scrubber stack tops = steam points (under the "Stacks" scale pivot).
  Status         material of the control board (emissive green; the view sets green / amber / red).
  Beacon         mesh on the roof mast (material "Beacon").
  Stacks, Masts  scale pivots (±10% in Y). SparkPoint = sparks when broken. PlantLight = where an OmniLight goes.
  SignFront      decal marker on the kiosk. RoofSpot0..1, AddOnSpot0..4 variety spots.
  Lamp, Window, Beacon, Status   glow materials.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_life_support.py --game     (export game/models/buildings/life_support_plant.glb)
  /home/user/.bpyvenv/bin/python blender/build_life_support.py --game --preview blender/previews/hostile --shots hero,game,close,night,pieces
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, lathe, empty, hoop, railing, ladder, pipe  # noqa: E402

CFG = K.setup("life_support_plant")

SLAB = 0.3
PAD_R = 7.0
HALL = (-2.3, 2.3)              # centre
HALL_W, HALL_D, HALL_H = 5.2, 3.6, 2.7
ROOF = SLAB + HALL_H + 0.2      # roof top
FANS = [(-3.55, 2.35), (-1.05, 2.35)]
SCRUB = [(1.5, 3.2), (3.2, 3.35), (4.9, 3.0)]
VESSEL_R, VESSEL_H = 0.78, 3.0
STACK_TOPS = (7.6, 8.4, 7.0)
TANKS = [(-0.45, -3.3, "O2"), (1.35, -3.65, "N2"), (3.15, -3.3, "O2")]
TANK_R, TANK_Z0, TANK_Z1 = 0.64, SLAB + 0.5, SLAB + 2.5
CONTROL = (-3.95, -2.3)
HX = (5.0, 0.0)
SHELL = (2.6, -0.85)
VAULT = (-1.6, -5.9)
PIECES = ["Pad", "Hall", "HallRoof", "Scrubbers", "Ducts", "HeatExchangers", "Tanks", "Pipes", "Control", "Catwalks", "Lights"]


def materials():
    K.materials()
    K.mat("status", "Status", (0.3, 1.0, 0.5), 0.0, 0.3, emission=(0.3, 1.0, 0.5), strength=3.0)
    K.mat("label", "Label", (0.9, 0.9, 0.88), 0.0, 0.6, textured=True)
    K.mat("o2", "TankO2", (0.18, 0.4, 0.78), 0.2, 0.45, textured=True)
    K.mat("n2", "TankN2", (0.13, 0.14, 0.16), 0.2, 0.5, textured=True)
    K.mat("frost", "Frost", (0.86, 0.9, 0.95), 0.0, 0.75, textured=True)
    K.mat("insul", "Insulation", (0.78, 0.76, 0.7), 0.0, 0.7, textured=True)


def P2(xy, z=0.0):
    return P(xy[0], xy[1], z)


# ---------------------------------------------------------------- pieces


def pad(g):
    o = lathe("Pad", [(0.0, 0.0), (PAD_R, 0.0), (PAD_R, SLAB), (0.0, SLAB)], MT["concrete"], g, (0, 0, 0), 8, smooth=False)
    o.rotation_euler.z = math.radians(22.5)
    o = lathe("PadSkirt", [(0.0, 0.0), (PAD_R + 0.15, 0.0), (PAD_R + 0.15, 0.12), (0.0, 0.12)], MT["dark"], g, (0, 0, 0), 8, smooth=False)
    o.rotation_euler.z = math.radians(22.5)
    o = K.ring_mesh("PadLine", PAD_R - 0.35, PAD_R - 0.27, SLAB, SLAB + 0.004, MT["hazard"], g, segs=8)
    o.rotation_euler.z = math.radians(22.5)
    o = K.band("PadOwner", PAD_R + 0.005, 0.08, 0.22, MT["owner"], g, segs=8)
    o.rotation_euler.z = math.radians(22.5)
    # frost on the pad's cold north (back) edge
    qs = []
    for k in range(6):
        x0, x1 = -4.2 + k * 1.4, -4.2 + (k + 1) * 1.4
        w0, w1 = 0.25 + 0.2 * abs(math.sin(k * 1.9)), 0.25 + 0.2 * abs(math.sin((k + 1) * 1.9))
        y = PAD_R * math.cos(math.radians(22.5)) - 0.02
        qs.append((P(x0, y - w0, SLAB + 0.003), P(x1, y - w1, SLAB + 0.003), P(x1, y, SLAB + 0.003), P(x0, y, SLAB + 0.003)))
    K.faces_obj("FrostEdge", qs, MT["frost"], g)
    # walkway markings to the hall door and the kiosk
    K.stripes("DoorStripe", (-1.4, 0.45), (-3.2, 0.45), 0.5, SLAB + 0.004, g, pitch=0.4)
    for k, (x, y) in enumerate([(-1.2, -1.2), (2.4, -5.4), (5.6, -1.9)]):
        bx(f"Drain{k}", (0.6, 0.3, 0.02), P(x, y, SLAB + 0.008), MT["grate"], g)
    # the pipe vault at the front edge (where the supply runs go underground to the base)
    v = P2(VAULT, SLAB)
    bx("Vault", (1.8, 1.1, 0.12), v + P(0, 0, -0.04), MT["dark"], g)
    bx("VaultGrate", (1.6, 0.9, 0.02), v + P(0, 0, 0.03), MT["grate"], g)
    for s in (-1, 1):
        bx(f"VaultKerb{s}", (1.9, 0.1, 0.12), v + P(0, s * 0.6, 0.02), MT["hazard"], g)
    for k, (x, y, yaw) in enumerate([(-7.5, 2.4, 90), (7.5, 1.2, -90), (-2.2, 7.4, 0), (2.6, 7.4, 0), (-6.3, -3.9, 60)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)


def hall(g):
    c = P2(HALL)
    x0, x1 = HALL[0] - HALL_W / 2, HALL[0] + HALL_W / 2
    y0, y1 = HALL[1] - HALL_D / 2, HALL[1] + HALL_D / 2
    bx("HallPlinth", (HALL_W + 0.3, HALL_D + 0.3, 0.3), c + P(0, 0, SLAB + 0.15), MT["trim"], g)
    bx("HallWalls", (HALL_W, HALL_D, HALL_H), c + P(0, 0, SLAB + HALL_H / 2), MT["hull"], g)
    bx("HallAccent", (HALL_W + 0.02, HALL_D + 0.02, 0.3), c + P(0, 0, SLAB + 0.55), MT["accent"], g)
    bx("HallOwner", (HALL_W + 0.02, HALL_D + 0.02, 0.12), c + P(0, 0, SLAB + HALL_H - 0.35), MT["owner"], g)
    # wall ribs (pilasters) along the front and back
    for k in range(6):
        x = x0 + 0.1 + k * (HALL_W - 0.2) / 5
        for y, s in ((y0, -1), (y1, 1)):
            bx(f"HallRib{k}{s}", (0.14, 0.1, HALL_H), P(x, y + s * 0.05, SLAB + HALL_H / 2), MT["panel"], g)
    # front: roll-up door (left), lit windows (right)
    bx("DoorFrame", (1.9, 0.12, 2.15), P(-2.3, y0 - 0.04, SLAB + 1.07), MT["trim"], g)
    bx("Door", (1.65, 0.06, 1.95), P(-2.3, y0 - 0.08, SLAB + 0.98), MT["panel"], g)
    for k in range(7):
        bx(f"DoorSlat{k}", (1.65, 0.03, 0.04), P(-2.3, y0 - 0.115, SLAB + 0.2 + k * 0.27), MT["trim"], g)
    bx("DoorLamp", (0.4, 0.12, 0.08), P(-2.3, y0 - 0.1, SLAB + 2.25), MT["lamp"], g).visible_shadow = False
    bx("HallGlass", (1.9, 0.04, 0.6), P(-0.45, y0 - 0.02, SLAB + 1.65), MT["glass"], g)
    K.pane_row("HallWin", (-1.35, y0 - 0.045, 0), (1, 0, 0), None, 1.8, SLAB + 1.4, SLAB + 1.9, 3, MT["window"], g, frac=0.8)
    # intake louvres on the left wall, a vent bank on the right
    for k in range(3):
        y = y0 + 0.6 + k * 1.2
        bx(f"Louvre{k}", (0.08, 0.95, 1.2), P(x0 - 0.04, y, SLAB + 1.3), MT["dark"], g)
        for j in range(5):
            bx(f"LouvreSlat{k}{j}", (0.12, 0.92, 0.04), P(x0 - 0.08, y, SLAB + 0.8 + j * 0.24), MT["steel"], g, rot=(math.radians(-30), 0, 0))
        bx(f"LouvreFrost{k}", (0.14, 1.0, 0.05), P(x0 - 0.06, y, SLAB + 1.92), MT["frost"], g)
    bx("SideDoor", (0.06, 0.9, 1.9), P(x1 + 0.03, y1 - 0.8, SLAB + 0.95), MT["trim"], g)
    bx("SideDoorLamp", (0.06, 0.3, 0.06), P(x1 + 0.05, y1 - 0.8, SLAB + 2.0), MT["window"], g).visible_shadow = False
    K.wall_stripes("HallFoot", (x0, y0 - 0.001), (-3.25, y0 - 0.001), SLAB + 0.3, SLAB + 0.42, g, pitch=0.3)
    K.point("PlantLight", P(-0.6, -1.0, 3.0), g, 0.4)


def hall_roof(g):
    c = P2(HALL)
    top = SLAB + HALL_H
    bx("Roof", (HALL_W + 0.3, HALL_D + 0.3, 0.2), c + P(0, 0, top + 0.1), MT["panel"], g)
    for s in (-1, 1):
        bx(f"Parapet{s}", (HALL_W + 0.3, 0.1, 0.25), c + P(0, s * (HALL_D / 2 + 0.1), top + 0.32), MT["trim"], g)
        bx(f"ParapetE{s}", (0.1, HALL_D + 0.3, 0.25), c + P(s * (HALL_W / 2 + 0.1), 0, top + 0.32), MT["trim"], g)
    bx("RoofFrost", (HALL_W + 0.32, 0.14, 0.05), c + P(0, HALL_D / 2 + 0.1, top + 0.47), MT["frost"], g)
    for k, f in enumerate(FANS):
        z = ROOF
        lathe(f"FanHousing{k}", [(0.85, z), (1.02, z), (1.02, z + 0.4), (0.92, z + 0.46), (0.85, z + 0.4), (0.85, z)], MT["hull"], g, (f[0], f[1], 0), 24, smooth=False)
        zcyl(f"FanWell{k}", 0.86, z - 0.02, z + 0.05, MT["dark"], g, xy=f, segs=24)
        K.band(f"FanOwner{k}", 1.025, z + 0.12, z + 0.24, MT["owner"], g, xy=f, segs=24)
        zcyl(f"FanMotor{k}", 0.2, z + 0.05, z + 0.2, MT["dark"], g, xy=f, segs=10)
        for j in range(2):
            a = math.radians(45 + 90 * j)
            d = P(math.cos(a), math.sin(a), 0)
            obox(f"FanGuard{k}{j}", (0.05, 1.8, 0.05), P2(f, z + 0.45), d, MT["steel"], g)
        fan = empty(f"Fan{k}", P2(f, z + 0.3), g)
        zcyl(f"Fan{k}Hub", 0.17, -0.08, 0.1, MT["trim"], fan, segs=12)
        for j in range(7):
            a = math.radians(360 * j / 7)
            bx(f"Fan{k}Blade{j}", (0.66, 0.2, 0.025), (0.45 * math.cos(a), 0.45 * math.sin(a), 0.0), MT["steel"], fan, rot=(math.radians(25), 0, a))
    # rooftop AC unit and the antenna mast
    bx("RoofAc", (1.0, 0.75, 0.42), P(-2.3, 0.95, ROOF + 0.21), MT["panel"], g)
    bx("RoofAcGrille", (0.8, 0.03, 0.28), P(-2.3, 0.565, ROOF + 0.22), MT["grate"], g)
    zcyl("Antenna", 0.05, ROOF, ROOF + 2.6, MT["steel"], g, xy=(0.0, 3.75), segs=6)
    zcyl("AntennaFoot", 0.14, ROOF, ROOF + 0.2, MT["trim"], g, xy=(0.0, 3.75), segs=8)
    for j in range(3):
        bx(f"AntennaArm{j}", (0.5, 0.03, 0.03), P(0.0, 3.75, ROOF + 1.4 + j * 0.4), MT["steel"], g)
    K.point("RoofSpot1", P(-3.9, 0.95, ROOF), g)


def scrubbers(g):
    piv = empty("Stacks", P(0, 0, SLAB), g)
    for k, (x, y) in enumerate(SCRUB):
        zcyl(f"Skirt{k}", VESSEL_R + 0.12, SLAB, SLAB + 0.35, MT["trim"], g, xy=(x, y), segs=16)
        lathe(f"Vessel{k}", [(VESSEL_R, SLAB + 0.3), (VESSEL_R, SLAB + VESSEL_H), (VESSEL_R * 0.75, SLAB + VESSEL_H + 0.35),
                             (0.36, SLAB + VESSEL_H + 0.6), (0.0, SLAB + VESSEL_H + 0.6)], MT["hull"], g, (x, y, 0), 20)
        K.band(f"VesselBand{k}", VESSEL_R + 0.01, SLAB + 1.1, SLAB + 1.35, MT["owner"] if k == 1 else MT["accent"], g, xy=(x, y), segs=20)
        hoop(f"VesselGirth{k}", VESSEL_R + 0.03, SLAB + 2.2, 0.07, MT["trim"], g, xy=(x, y), segs=20)
        bx(f"Manway{k}", (0.45, 0.12, 0.55), P(x, y - VESSEL_R - 0.02, SLAB + 0.85), MT["trim"], g)
        # the stack (in the Stacks pivot: z relative to SLAB)
        top = STACK_TOPS[k] - SLAB
        z0 = VESSEL_H + 0.55
        zcyl(f"Stack{k}", 0.34, z0, top, MT["panel"], piv, xy=(x, y), segs=14, radius2=0.3)
        for j, z in enumerate((z0 + 1.0, top - 0.9)):
            hoop(f"StackRing{k}{j}", 0.33, z, 0.06, MT["trim"], piv, xy=(x, y), segs=14)
        K.band(f"StackStripe{k}", 0.31, top - 0.55, top - 0.3, MT["hazard"], piv, xy=(x, y), segs=14)
        zcyl(f"StackLouvre{k}", 0.36, top, top + 0.22, MT["dark"], piv, xy=(x, y), segs=14)
        zcyl(f"StackCap{k}", 0.44, top + 0.22, top + 0.3, MT["trim"], piv, xy=(x, y), segs=14)
        hoop(f"StackFrost{k}", 0.43, top + 0.31, 0.05, MT["frost"], piv, xy=(x, y), segs=14)
        sphere(f"StackLamp{k}", 0.06, P(x + 0.4, y, top + 0.35), MT["beacon"], piv, 6)
        if k < 2:
            K.point(f"Vent{k}", P(x, y, top + 0.5), piv, 0.5)


def ducts(g):
    x1 = HALL[0] + HALL_W / 2
    z = SLAB + 1.9
    y = 2.05
    # a square duct header from the hall's right wall along the front of the vessels
    obox("DuctOut", (0.62, 0.9, 0.62), P(x1 + 0.45, y + 0.25, z), P(1, 0, 0), MT["panel"], g)
    obox("Duct", (0.55, SCRUB[-1][0] - x1 - 0.5, 0.55), P((x1 + 0.9 + SCRUB[-1][0]) / 2, y, z), P(1, 0, 0), MT["panel"], g)
    for k, (x, sy) in enumerate(SCRUB):
        obox(f"DuctDrop{k}", (0.42, sy - y - VESSEL_R + 0.2, 0.42), P(x, (y + sy - VESSEL_R) / 2 + 0.1, z), P(0, 1, 0), MT["panel"], g)
        bx(f"DuctFlange{k}", (0.6, 0.08, 0.6), P(x, y + 0.32, z), MT["trim"], g)
    for k in range(4):
        x = x1 + 0.9 + k * 1.2
        bx(f"DuctLeg{k}", (0.1, 0.1, z - SLAB - 0.27), P(x, y, (z - 0.27 + SLAB) / 2), MT["trim"], g)
    bx("DuctDamper", (0.62, 0.3, 0.62), P(x1 + 1.4, y, z), MT["owner"], g)


def heat_exchangers(g):
    # finned plate unit
    c = P2(HX)
    bx("HxBase", (1.5, 2.5, 0.25), c + P(0, 0, SLAB + 0.125), MT["trim"], g)
    bx("HxBody", (1.2, 2.3, 1.3), c + P(0, 0, SLAB + 0.25 + 0.65), MT["hull"], g)
    for j in range(9):
        bx(f"HxFin{j}", (1.32, 0.05, 1.15), c + P(0, -1.0 + j * 0.25, SLAB + 0.9), MT["steel"], g)
    bx("HxTop", (1.26, 2.36, 0.12), c + P(0, 0, SLAB + 1.61), MT["frost"], g)
    bx("HxOwner", (1.22, 2.32, 0.1), c + P(0, 0, SLAB + 0.42), MT["owner"], g)
    for s in (-1, 1):
        pipe(f"HxHeader{s}", [c + P(-0.75, s * 0.8, SLAB + 1.2), c + P(-1.35, s * 0.8, SLAB + 1.2), c + P(-1.35, s * 0.8, SLAB + 0.5)], 0.09, MT["copper"], g)
    # shell-and-tube exchanger on saddles with copper loops
    s0 = P2(SHELL, SLAB + 0.85)
    K.cylinder("Shell", 0.42, 2.4, s0, MT["insul"], g, axis="X", segments=16)
    for s in (-1, 1):
        K.cylinder(f"ShellHead{s}", 0.46, 0.18, s0 + P(s * 1.25, 0, 0), MT["trim"], g, axis="X", segments=16)
        bx(f"Saddle{s}", (0.2, 0.75, 0.55), P(SHELL[0] + s * 0.75, SHELL[1], SLAB + 0.28), MT["trim"], g)
    for j in range(3):
        K.cylinder(f"ShellBand{j}", 0.435, 0.08, s0 + P(-0.6 + j * 0.6, 0, 0), MT["steel"], g, axis="X", segments=16)
    pipe("ShellLoop", [s0 + P(1.34, 0, 0.15), s0 + P(1.75, 0, 0.15), P(HX[0] - 1.35, -0.8, SLAB + 1.0)], 0.08, MT["copper"], g)
    pipe("ShellLoop2", [s0 + P(-1.34, 0, 0.15), s0 + P(-1.7, 0, 0.15), s0 + P(-1.7, 0, -0.5)], 0.08, MT["copper"], g)


def tanks(g):
    for k, (x, y, gas) in enumerate(TANKS):
        piv = empty(f"Tank{k}", P(x, y, 0), g)
        band = MT["o2"] if gas == "O2" else MT["n2"]
        prof = [(0.0, TANK_Z0 - 0.25), (TANK_R * 0.6, TANK_Z0 - 0.2), (TANK_R, TANK_Z0)]
        prof += [(TANK_R, TANK_Z1)]
        for i in range(1, 5):
            t = i / 4 * math.pi / 2
            prof.append((TANK_R * math.cos(t) if i < 4 else 0.0, TANK_Z1 + TANK_R * 0.7 * math.sin(t)))
        lathe(f"Tank{k}Shell", prof, MT["hull"], piv, (0, 0, 0), 20)
        K.band(f"Tank{k}Band", TANK_R + 0.01, TANK_Z1 - 0.75, TANK_Z1 - 0.2, band, piv, segs=20)
        K.band(f"Tank{k}Owner", TANK_R + 0.012, TANK_Z0 + 0.2, TANK_Z0 + 0.32, MT["owner"], piv, segs=20)
        hoop(f"Tank{k}Girth", TANK_R + 0.03, TANK_Z0 + 0.55, 0.06, MT["trim"], piv, segs=20)
        for j in range(4):
            a = math.radians(45 + 90 * j)
            beam(f"Tank{k}Leg{j}", P(0.7 * math.cos(a), 0.7 * math.sin(a), SLAB), P(0.55 * math.cos(a), 0.55 * math.sin(a), TANK_Z0 + 0.1), 0.1, MT["trim"], piv)
        zcyl(f"Tank{k}Valve", 0.08, TANK_Z1 + 0.45, TANK_Z1 + 0.75, MT["steel"], piv, segs=8)
        hoop(f"Tank{k}Wheel", 0.13, TANK_Z1 + 0.76, 0.03, MT["valve"], piv, segs=10)
        hoop(f"Tank{k}Frost", TANK_R * 0.55, TANK_Z1 + 0.55, 0.06, MT["frost"], piv, segs=14)
        # label plate on the front (facing -Y) and the decal marker for the game's "O2"/"N2"
        bx(f"Tank{k}Plate", (0.75, 0.04, 0.42), P(0, -TANK_R - 0.03, TANK_Z0 + 1.05), MT["label"], piv)
        K.marker(f"LabelTank{k}", P(0, -TANK_R - 0.055, TANK_Z0 + 1.05), (math.radians(90), 0, 0), 0.7, 0.32, piv)
        pipe(f"Tank{k}Feed", [P(0, 0.2, TANK_Z0 - 0.2), P(0, 0.2, SLAB + 0.35), P(0, -1.7 - y - 0.1, SLAB + 0.35)], 0.07, MT["steel"], piv)


def pipes(g):
    # manifold behind the tanks, fed by the tanks and the hall, out to the vault at the front edge
    my = -1.7
    pipe("Manifold", [P(-1.6, my, SLAB + 0.35), P(3.6, my, SLAB + 0.35)], 0.1, MT["steel"], g)
    for k, x in enumerate((-1.2, 0.6, 2.4)):
        zcyl(f"ManifoldValve{k}", 0.09, SLAB + 0.45, SLAB + 0.7, MT["steel"], g, xy=(x, my), segs=8)
        hoop(f"ManifoldWheel{k}", 0.12, SLAB + 0.72, 0.03, MT["valve"], g, xy=(x, my), segs=10)
    for k, x in enumerate((-0.9, 0.4)):
        pipe(f"HallFeed{k}", [P(x, HALL[1] - HALL_D / 2, SLAB + 1.2 + 0.2 * k), P(x, -0.4, SLAB + 1.2 + 0.2 * k), P(x, -0.4, SLAB + 0.35), P(x, my, SLAB + 0.35)], 0.09, MT["steel"], g)
    for k, x in enumerate((-1.8, -1.4)):
        mat = MT["insul"]
        pipe(f"Supply{k}", [P(x, my, SLAB + 0.35 + 0.25 * k), P(x, -4.6, SLAB + 0.35 + 0.25 * k), P(x, VAULT[1], SLAB + 0.35 + 0.25 * k), P(x, VAULT[1], SLAB - 0.6)], 0.12, mat, g)
        K.cylinder(f"SupplyBand{k}", 0.135, 0.18, (x, -3.0, SLAB + 0.35 + 0.25 * k), MT["o2"] if k == 0 else MT["valve"], g, axis="Y", segments=10)
        zcyl(f"SupplyCollar{k}", 0.2, SLAB + 0.05, SLAB + 0.15, MT["trim"], g, xy=(x, VAULT[1]), segs=10)
        for j, yy in enumerate((-2.6, -4.0, -5.2)):
            bx(f"PipeShoe{k}{j}", (0.3, 0.2, 0.35 + 0.25 * k - 0.12), P(x, yy, SLAB + (0.35 + 0.25 * k - 0.12) / 2), MT["trim"], g)
    pipe("Manifold2", [P(-1.6, my, SLAB + 0.35), P(-1.8, my, SLAB + 0.35)], 0.1, MT["steel"], g, joints=False)
    pipe("Riser", [P(-1.4, my, SLAB + 0.35), P(-1.4, my, SLAB + 0.6)], 0.1, MT["steel"], g, joints=False)


def control(g):
    c = P2(CONTROL)
    w, d, h = 2.4, 1.7, 2.1
    bx("CtlBase", (w + 0.1, d + 0.1, 0.22), c + P(0, 0, SLAB + 0.11), MT["trim"], g)
    bx("CtlBody", (w, d, h), c + P(0, 0, SLAB + 0.22 + h / 2), MT["hull"], g)
    bx("CtlAccent", (w + 0.02, d + 0.02, 0.28), c + P(0, 0, SLAB + 0.5), MT["accent"], g)
    bx("CtlOwner", (w + 0.02, d + 0.02, 0.1), c + P(0, 0, SLAB + h - 0.1), MT["owner"], g)
    bx("CtlGlass", (w - 0.5, 0.04, 0.55), c + P(0.1, -d / 2 - 0.01, SLAB + 1.6), MT["glass"], g)
    K.pane_row("CtlWin", (c.x - 0.75, c.y - d / 2 - 0.035, 0), (1, 0, 0), None, w - 0.7, SLAB + 1.4, SLAB + 1.8, 2, MT["window"], g, frac=0.85)
    # the status board: a dark frame with the lit board and a row of LED tiles
    bx("StatusFrame", (1.6, 0.06, 0.55), c + P(0.1, -d / 2 - 0.02, SLAB + 0.95), MT["dark"], g)
    bx("StatusBoard", (1.4, 0.03, 0.18), c + P(0.1, -d / 2 - 0.055, SLAB + 1.08), MT["status"], g).visible_shadow = False
    for j in range(6):
        bx(f"StatusLed{j}", (0.14, 0.03, 0.1), c + P(-0.45 + j * 0.22, -d / 2 - 0.055, SLAB + 0.8), MT["status"], g).visible_shadow = False
    bx("CtlRoof", (w + 0.3, d + 0.3, 0.14), c + P(0, 0, SLAB + 0.22 + h + 0.07), MT["panel"], g)
    bx("CtlFascia", (w + 0.32, 0.05, 0.3), c + P(0, -d / 2 - 0.16, SLAB + 0.22 + h + 0.02), MT["dark"], g)
    K.marker("SignFront", c + P(0, -d / 2 - 0.19, SLAB + 0.22 + h + 0.02), (math.radians(90), 0, 0), 2.5, 0.2, g)
    bx("CtlRoofFrost", (w + 0.32, 0.12, 0.04), c + P(0, d / 2 + 0.09, SLAB + 0.22 + h + 0.15), MT["frost"], g)
    bx("CtlDoor", (0.05, 0.75, 1.8), c + P(w / 2 + 0.01, 0.25, SLAB + 0.22 + 0.9), MT["trim"], g)
    bx("CtlDoorLamp", (0.06, 0.4, 0.06), c + P(w / 2 + 0.03, 0.25, SLAB + 2.1), MT["window"], g).visible_shadow = False
    K.point("RoofSpot0", c + P(-0.4, 0.2, SLAB + 0.22 + h + 0.14), g)


def catwalks(g):
    z = SLAB + VESSEL_H + 0.05
    y = 2.05
    xa, xb = SCRUB[0][0] - 0.6, SCRUB[-1][0] + 0.7
    bx("Catwalk", (xb - xa, 0.8, 0.06), P((xa + xb) / 2, y - 0.62, z), MT["grate"], g)
    for k, x in enumerate((xa + 0.2, (xa + xb) / 2, xb - 0.2)):
        bx(f"CatwalkLeg{k}", (0.1, 0.1, z - SLAB), P(x, y - 0.95, (z + SLAB) / 2), MT["trim"], g)
    railing("CatRail", [(xa, y - 1.0), (xb, y - 1.0)], z + 0.03, g, h=0.9, post=0.8)
    ladder("CatLadder", P(xb + 0.15, y - 0.62, SLAB), z, (1, 0), g, cage=True)
    # a short walk from the catwalk onto the hall roof edge
    bx("RoofBridge", (0.9, 0.7, 0.06), P(xa - 0.45, y - 0.6, z), MT["grate"], g)


def lights(g):
    piv = empty("Masts", P(0, 0, SLAB), g)
    K.light_mast("MastA", -5.5, 3.6, 0.0, 6.0, -40, piv)
    K.light_mast("MastB", 5.7, -2.6, 0.0, 5.4, 140, piv)
    K.bollards("Bollard", [(-2.6, -4.6), (0.4, -5.4), (4.6, -4.2), (-5.6, -0.6)], SLAB, g)
    sphere("Beacon", 0.1, P(0.0, 3.75, ROOF + 2.7), MT["beacon"], g, 10)
    K.point("SparkPoint", P(HX[0] - 0.9, HX[1] - 1.4, SLAB + 0.9), g)


BUILDERS = [pad, hall, hall_roof, scrubbers, ducts, heat_exchangers, tanks, pipes, control, catwalks, lights]

PIVOTS = {"Fan0", "Fan1", "Tank0", "Tank1", "Tank2", "Stacks", "Masts"}
SEPARATE = {"Beacon"}
EMPTIES = {"Vent0", "Vent1", "SparkPoint", "PlantLight", "SignFront", "RoofSpot0", "RoofSpot1"} \
    | {f"AddOnSpot{k}" for k in range(5)} | {f"LabelTank{k}" for k in range(3)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("LIFE SUPPORT 01", (0.92, 0.93, 0.95))} | {f"LabelTank{k}": (gas, (0.08, 0.08, 0.1)) for k, (_, _, gas) in enumerate(TANKS)}


def main():
    materials()
    root = K.assemble("life_support_plant", PIECES, BUILDERS)
    night = [("NMastA", (1.0, 0.95, 0.85), 260, P(-5.0, 3.1, 6.0)),
             ("NMastB", (1.0, 0.95, 0.85), 260, P(5.2, -2.1, 5.4)),
             ("NDoor", (1.0, 0.85, 0.6), 80, P(-2.3, -0.6, 2.5)),
             ("NCtl", (0.5, 1.0, 0.6), 60, P(CONTROL[0], CONTROL[1] - 1.6, 1.4))]
    shots = {"hero": ((15.5, -19.0, 11.5), (0.0, 0.3, 2.8), 32),
             "close": ((5.5, -10.5, 4.6), (0.6, -1.5, 1.8), 26),
             "night": ((-9.0, -19.0, 10.5), (0.0, 0.3, 2.8), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 17),
             "back": ((-14.0, 17.0, 10.0), (0.0, 0.5, 2.8), 32)}
    K.finish(root, KEEP, SEPARATE, "LifeSupportGrime", SLAB, soot=(6.4, 8.8), shots=shots, night_lights=night,
             signs=SIGNS, rover_at=(-9.0, -6.8, 160))


main()
