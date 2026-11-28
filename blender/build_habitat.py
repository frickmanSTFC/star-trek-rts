"""Prefab Habitat (`prefab_habitat`), Dawn of Warp era. MOCKUP stage: only the basic version (--mark 0) exists.

A pre-warp expedition's first home, as the RTS camera sees it from the front: one pressurised cylinder shipped down in a
drop ship and set on jack legs over a landing mat; a boxy airlock porch with steps; portholes; an air handler with
exposed ducts; a water tank; two solar wings on a frame; an antenna mast with a beacon; crates. White paint and
Shuttle orange, like the era's drop ships.

Frame (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit. Footprint radius 7.

Run:
  /home/user/.bpyvenv/bin/python blender/build_habitat.py --mark 0 --preview blender/previews/habitat --shots hero,game
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, zcyl, sphere, empty, railing, pipe, cylinder, torus, beam  # noqa: E402

MARK = int(K.arg("--mark", "0"))
CFG = K.setup("prefab_habitat")
CFG["id"] = f"prefab_habitat-mk{MARK}"

MAT_Z = 0.12                 # top of the landing mat
R = 1.5                      # module radius
LEN = 6.6                    # module length (along X)
CZ = MAT_Z + 0.75 + R        # module axis height
MOD_Y = 0.6                  # module axis y
PIECES = ["Mat", "Cradles", "Module", "Airlock", "Utilities", "Solar", "Mast"]


def materials():
    K.materials()
    for key, colour in (("hull", (0.9, 0.9, 0.87)), ("panel", (0.72, 0.73, 0.74)), ("accent", (0.86, 0.33, 0.07))):
        MT[key].node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*colour, 1.0)
        MT[key]["colour"] = colour
    K.mat("solar", "Solar", (0.03, 0.05, 0.14), 0.6, 0.18, coat=1.0)
    K.mat("foil", "Foil", (0.8, 0.62, 0.25), 0.9, 0.35, textured=True)
    K.mat("mat", "Mat", (0.33, 0.32, 0.3), 0.3, 0.7, textured=True)
    K.mat("water", "TankBlue", (0.16, 0.36, 0.7), 0.2, 0.45, textured=True)


def ring_x(name, r, minor, x, mat, g, y=MOD_Y, z=CZ):
    o = torus(name, r, minor, (x, y, z), mat, g, segs=28)
    o.rotation_euler = (0, math.pi / 2, 0)
    return o


def mat_(g):
    bx("LandingMat", (11.0, 8.6, MAT_Z), P(0, -0.2, MAT_Z / 2), MT["mat"], g, 0.03)
    for k in range(9):   # plank seams
        bx(f"MatSeam{k}", (11.0, 0.03, 0.01), P(0, -4.1 + k * 0.98, MAT_Z + 0.004), MT["dark"], g).visible_shadow = False
    K.stripes("MatEdge", P(-2.2, -4.35), P(2.2, -4.35), 0.3, MAT_Z + 0.006, g)
    for k, (x, y) in enumerate([(-6.2, -3.6), (6.2, 2.6), (-5.6, 4.6), (4.2, -5.6), (0.0, 5.6)]):
        K.point(f"AddOnSpot{k}", P(x, y, 0), g)


def cradles(g):
    for k, x in enumerate((-2.3, 0.0, 2.3)):
        bx(f"Saddle{k}", (0.32, 2.5, 0.5), P(x, MOD_Y, CZ - R - 0.05), MT["trim"], g, 0.03)
        bx(f"Skid{k}", (0.5, 3.4, 0.14), P(x, MOD_Y, MAT_Z + 0.07), MT["steel"], g, 0.02)
        for s in (-1, 1):
            zcyl(f"Jack{k}{s}", 0.09, MAT_Z + 0.1, CZ - R - 0.1, MT["steel"], g, xy=(x, MOD_Y + s * 1.35), segs=8)
            beam(f"Brace{k}{s}", P(x, MOD_Y + s * 1.6, MAT_Z + 0.14), P(x, MOD_Y + s * 0.75, CZ - R + 0.2), 0.07, MT["trim"], g)
            zcyl(f"JackPad{k}{s}", 0.2, MAT_Z, MAT_Z + 0.08, MT["hazard"], g, xy=(x, MOD_Y + s * 1.35), segs=10)


def module(g):
    cylinder("Shell", R, LEN, (0, MOD_Y, CZ), MT["hull"], g, axis="X", segments=32)
    for s in (-1, 1):
        cap = sphere(f"EndCap{s}", R * 0.985, (s * LEN / 2, MOD_Y, CZ), MT["hull"], g, 24)
        cap.scale = (0.42, 1, 1)
        ring_x(f"EndRing{s}", R + 0.03, 0.09, s * LEN / 2, MT["accent"], g)
        cylinder(f"Hatch{s}", 0.55, 0.12, (s * (LEN / 2 + R * 0.4), MOD_Y, CZ), MT["trim"], g, axis="X", segments=16)
        cylinder(f"HatchPort{s}", 0.22, 0.14, (s * (LEN / 2 + R * 0.4 + 0.01), MOD_Y, CZ + 0.05), MT["glass"], g, axis="X", segments=12)
    for k, x in enumerate((-1.65, 0.0, 1.65)):
        ring_x(f"Rib{k}", R + 0.02, 0.055, x, MT["panel"], g)
    # the orange cheat line along the front, portholes over it
    front = MOD_Y - R
    for k, (x0, x1) in enumerate(((-3.2, -1.0), (1.0, 3.2))):
        bx(f"CheatLine{k}", (x1 - x0, 0.04, 0.2), P((x0 + x1) / 2, front + 0.07, CZ - 0.55), MT["accent"], g).visible_shadow = False
    for k, x in enumerate((-2.55, -1.55, 1.55, 2.55)):
        a = math.radians(14)   # a little above the equator, so the camera sees them
        y, z = MOD_Y - R * math.cos(a), CZ + R * math.sin(a)
        for nm, r, d, m in (("PortRim", 0.3, 0.1, MT["trim"]), ("Port", 0.22, 0.13, MT["window"])):
            o = cylinder(f"{nm}{k}", r, d, (x, y, z), m, g, axis="Y", segments=14)
            o.rotation_euler.x += a
            o.visible_shadow = False
    # roof: walkway, hand rail, radiator, hatch, a foil-wrapped equipment box
    top = CZ + R
    bx("RoofWalk", (LEN - 1.0, 0.7, 0.05), P(0, MOD_Y, top + 0.02), MT["grate"], g)
    railing("RoofRail", [(-LEN / 2 + 0.5, MOD_Y + 0.38), (LEN / 2 - 0.5, MOD_Y + 0.38)], top + 0.04, g, h=0.6, post=1.4)
    bx("Radiator", (2.2, 0.06, 0.9), P(-1.3, MOD_Y + 0.75, top + 0.42), MT["panel"], g, rot=(math.radians(-18), 0, 0))
    for j in range(6):
        bx(f"RadFin{j}", (0.03, 0.09, 0.86), P(-2.3 + j * 0.4, MOD_Y + 0.75, top + 0.42), MT["steel"], g, rot=(math.radians(-18), 0, 0))
    bx("RoofBox", (0.8, 0.6, 0.45), P(2.0, MOD_Y - 0.05, top + 0.2), MT["foil"], g, 0.04)
    zcyl("RoofVent", 0.14, top - 0.1, top + 0.55, MT["steel"], g, xy=(0.9, MOD_Y + 0.05), segs=10)
    zcyl("RoofVentCap", 0.2, top + 0.55, top + 0.62, MT["trim"], g, xy=(0.9, MOD_Y + 0.05), segs=10)
    K.point("RoofSpot0", P(-0.4, MOD_Y, top + 0.05), g)
    K.point("RoofSpot1", P(2.6, MOD_Y, top + 0.05), g)
    K.point("HabLight", P(0, MOD_Y - R - 1.6, 2.6), g)
    K.point("SparkPoint", P(2.0, MOD_Y - 0.4, top + 0.45), g)


def airlock(g):
    front = MOD_Y - R
    w, d, h = 1.7, 1.5, 2.15
    z0 = CZ - 1.05                   # porch floor = module floor
    y = front - d / 2 + 0.35
    bx("LockBody", (w, d, h), P(0, y, z0 + h / 2), MT["hull"], g, 0.06)
    bx("LockRoof", (w + 0.16, d + 0.1, 0.1), P(0, y, z0 + h + 0.05), MT["accent"], g, 0.02)
    bx("LockBand", (w + 0.03, d + 0.03, 0.22), P(0, y, z0 + 0.3), MT["owner"], g)
    yf = y - d / 2
    bx("DoorFrame", (1.0, 0.06, 1.85), P(0, yf - 0.02, z0 + 0.98), MT["trim"], g)
    bx("Door", (0.82, 0.05, 1.7), P(0, yf - 0.05, z0 + 0.95), MT["panel"], g)
    bx("DoorWindow", (0.34, 0.03, 0.4), P(0, yf - 0.08, z0 + 1.45), MT["window"], g).visible_shadow = False
    bx("DoorLamp", (0.7, 0.07, 0.07), P(0, yf - 0.05, z0 + 1.98), MT["lamp"], g).visible_shadow = False
    bx("DoorPad", (0.1, 0.03, 0.16), P(0.6, yf - 0.03, z0 + 1.05), MT["window"], g).visible_shadow = False
    K.marker("SignFront", P(0, yf - 0.04, z0 + h - 0.02), (math.radians(90), 0, 0), 1.5, 0.16, g)
    # porch deck, steps, rails
    bx("Porch", (2.1, 1.0, 0.07), P(0, yf - 0.5, z0 - 0.03), MT["grate"], g)
    n = 4
    for j in range(n):
        zt = z0 - 0.03 - (j + 1) * (z0 - MAT_Z) / (n + 1)
        bx(f"Step{j}", (1.2, 0.3, 0.05), P(0, yf - 1.12 - j * 0.3, zt), MT["grate"], g)
    for s in (-1, 1):
        beam(f"Stringer{s}", P(s * 0.62, yf - 1.0, z0 - 0.08), P(s * 0.62, yf - 1.0 - n * 0.3, MAT_Z + 0.1), 0.06, MT["trim"], g)
        beam(f"StepRail{s}", P(s * 0.62, yf - 1.0, z0 + 0.85), P(s * 0.62, yf - 1.0 - n * 0.3, MAT_Z + 0.95), 0.04, MT["rail"], g)
        zcyl(f"RailPostTop{s}", 0.03, z0, z0 + 0.87, MT["rail"], g, xy=(s * 0.62, yf - 1.0), segs=6)
        zcyl(f"RailPostLow{s}", 0.03, MAT_Z, MAT_Z + 0.97, MT["rail"], g, xy=(s * 0.62, yf - 1.0 - n * 0.3), segs=6)
        zcyl(f"PorchLeg{s}", 0.05, MAT_Z, z0 - 0.05, MT["steel"], g, xy=(s * 0.95, yf - 0.9), segs=6)
    railing("PorchRailL", [(-1.03, yf - 0.02), (-1.03, yf - 0.98)], z0, g, h=0.85, post=0.9)
    railing("PorchRailR", [(1.03, yf - 0.02), (1.03, yf - 0.98)], z0, g, h=0.85, post=0.9)


def utilities(g):
    # water tank, back left
    tx, ty = -4.1, 2.9
    zcyl("WaterTank", 0.75, MAT_Z + 0.35, MAT_Z + 2.45, MT["hull"], g, xy=(tx, ty), segs=20, bev=0.1)
    K.band("WaterBand", 0.76, MAT_Z + 1.2, MAT_Z + 1.6, MT["water"], g, xy=(tx, ty), segs=20)
    for j in range(3):
        a = math.radians(90 + j * 120)
        zcyl(f"TankLeg{j}", 0.06, MAT_Z, MAT_Z + 0.5, MT["steel"], g, xy=(tx + 0.6 * math.cos(a), ty + 0.6 * math.sin(a)), segs=6)
    pipe("WaterPipe", [P(tx + 0.75, ty - 0.2, MAT_Z + 0.7), P(tx + 1.5, ty - 0.2, MAT_Z + 0.7), P(tx + 1.5, MOD_Y + 1.2, MAT_Z + 0.7), P(tx + 1.5, MOD_Y + 1.2, CZ - 0.6)],
         0.07, MT["copper"], g)
    # air handler with ducts into the shell, right of the airlock
    ax, ay = 3.9, -2.3
    bx("AirUnit", (1.5, 1.1, 1.1), P(ax, ay, MAT_Z + 0.65), MT["panel"], g, 0.05)
    bx("AirGrille", (1.1, 0.04, 0.6), P(ax, ay - 0.56, MAT_Z + 0.7), MT["dark"], g)
    zcyl("AirFan", 0.36, MAT_Z + 1.2, MAT_Z + 1.3, MT["dark"], g, xy=(ax, ay), segs=14)
    bx("AirSkid", (1.7, 1.3, 0.1), P(ax, ay, MAT_Z + 0.05), MT["trim"], g)
    pipe("Duct0", [P(ax - 0.4, ay + 0.55, MAT_Z + 0.85), P(ax - 0.4, MOD_Y - R - 0.5, MAT_Z + 0.85), P(ax - 0.4, MOD_Y - R - 0.5, CZ - 0.2), P(ax - 0.4, MOD_Y - R + 0.25, CZ - 0.2)],
         0.17, MT["foil"], g, segs=10)
    pipe("Duct1", [P(ax + 0.3, ay + 0.55, MAT_Z + 0.6), P(ax + 0.3, MOD_Y - 0.9, MAT_Z + 0.6), P(ax - 0.9, MOD_Y - 0.9, MAT_Z + 0.6)], 0.12, MT["steel"], g, segs=8)
    # power box + cable to the module, gas bottles, crates
    bx("PowerBox", (0.9, 0.7, 1.3), P(4.6, 2.6, MAT_Z + 0.65), MT["trim"], g, 0.04)
    bx("PowerDoor", (0.7, 0.03, 1.0), P(4.6, 2.24, MAT_Z + 0.7), MT["hazard"], g)
    K.sagging("PowerCable", P(4.2, 2.6, MAT_Z + 1.3), P(3.1, MOD_Y + 0.6, CZ + 0.9), 0.35, 0.035, MT["cable"], g)
    for j in range(3):
        zcyl(f"Bottle{j}", 0.17, MAT_Z, MAT_Z + 1.25, MT["valve"] if j == 1 else MT["hull"], g, xy=(-4.9 + j * 0.4, -1.2), segs=10, bev=0.06)
    bx("BottleRack", (1.35, 0.5, 0.08), P(-4.5, -1.2, MAT_Z + 0.85), MT["trim"], g)
    for j, (x, y, s, yaw, m) in enumerate([(-3.6, -3.0, 0.9, 12, "accent"), (-4.6, -2.9, 0.7, -8, "panel"), (-3.7, -3.05, 0.55, 30, "hull")]):
        z = MAT_Z + s / 2 + (0.9 if j == 2 else 0)
        bx(f"Crate{j}", (s, s, s), P(x, y, z), MT[m], g, 0.04, rot=(0, 0, math.radians(yaw)))


def solar(g):
    # two wings on a tube frame behind the module, tilted to the front
    for k, x in enumerate((-0.6, 3.5)):
        y, z = 3.9, MAT_Z + 1.9
        tilt = math.radians(32)
        bx(f"Panel{k}", (3.3, 1.9, 0.06), P(x, y, z), MT["solar"], g, rot=(tilt, 0, 0))
        bx(f"PanelFrame{k}", (3.4, 2.0, 0.04), P(x, y + 0.02, z - 0.03), MT["steel"], g, rot=(tilt, 0, 0))
        for j in range(1, 6):
            bx(f"Cell{k}{j}", (0.02, 1.9, 0.065), P(x - 1.65 + j * 0.55, y, z), MT["steel"], g, rot=(tilt, 0, 0)).visible_shadow = False
        for s in (-1, 1):
            beam(f"PanelLegF{k}{s}", P(x + s * 1.3, y - 0.75, MAT_Z), P(x + s * 1.3, y - 0.75, z - 0.48), 0.06, MT["steel"], g)
            beam(f"PanelLegB{k}{s}", P(x + s * 1.3, y + 0.8, MAT_Z), P(x + s * 1.3, y + 0.8, z + 0.45), 0.06, MT["steel"], g)
            beam(f"PanelBrace{k}{s}", P(x + s * 1.3, y - 0.75, MAT_Z + 0.1), P(x + s * 1.3, y + 0.8, z + 0.35), 0.04, MT["trim"], g)


def mast(g):
    piv = empty("Masts", P(0, 0, MAT_Z), g)
    mx, my = -3.3, MOD_Y + 1.75
    zcyl("MastPole", 0.07, 0, 6.6, MT["steel"], piv, xy=(mx, my), segs=8, radius2=0.04)
    zcyl("MastFoot", 0.22, 0, 0.3, MT["hazard"], piv, xy=(mx, my), segs=10)
    for j, (dx, dy) in enumerate(((-1.8, 1.4), (1.6, 1.6), (0.1, -1.2))):
        beam(f"Guy{j}", P(mx, my, 4.6), P(mx + dx, my + dy, 0.02), 0.02, MT["cable"], piv)
    bx("MastArm", (1.1, 0.05, 0.05), P(mx, my, 5.6), MT["trim"], piv)
    d = K.lathe("Dish", [(0, 0.0), (0.2, 0.02), (0.4, 0.09), (0.55, 0.2), (0.56, 0.2), (0.4, 0.07), (0.2, 0.0), (0, -0.02)], MT["hull"], piv,
                (mx + 0.55, my - 0.1, 5.75), segs=20)
    d.rotation_euler = (math.radians(55), 0, math.radians(-25))
    zcyl("Whip", 0.015, 5.6, 7.3, MT["cable"], piv, xy=(mx - 0.5, my), segs=5)
    sphere("Beacon", 0.09, P(mx, my, 6.66 + MAT_Z), MT["beacon"], g, 10)
    K.light_mast("LightMast", -5.2, -3.9, MAT_Z, 4.2, 40, piv, heads=1)


BUILDERS = [mat_, cradles, module, airlock, utilities, solar, mast]
PIVOTS = {"Masts"}
SEPARATE = {"Beacon"}
EMPTIES = {"SparkPoint", "HabLight", "SignFront", "RoofSpot0", "RoofSpot1"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}
SIGNS = {"SignFront": ("HABITAT 01", (0.1, 0.1, 0.12))}


def main():
    if MARK != 0:
        sys.exit("only --mark 0 (the basic version) is built so far")
    materials()
    root = K.assemble("prefab_habitat", PIECES, BUILDERS)
    night = [("NDoor", (1.0, 0.85, 0.6), 90, P(0, -3.4, 2.6)), ("NMast", (1.0, 0.95, 0.85), 200, P(-4.8, -3.5, 4.2))]
    shots = {"hero": ((12.5, -15.5, 8.5), (0.0, 0.2, 2.0), 32),
             "close": ((4.5, -9.0, 3.4), (0.3, -0.8, 1.9), 26),
             "night": ((-8.0, -15.5, 8.0), (0.0, 0.2, 2.0), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 17),
             "back": ((-11.0, 14.0, 8.0), (0.0, 0.5, 2.0), 32)}
    K.finish(root, KEEP, SEPARATE, "HabitatGrime", MAT_Z, shots=shots, night_lights=night, signs=SIGNS, rover_at=(-8.5, -5.5, 160))


main()
