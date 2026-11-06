"""Alloy Foundry Mk I-V mockups (`foundry`; owner 2026-11-06: full details and Mk I through Mk V).

One silhouette, five marks: a casting hall at the back, the melt out front under an arch (glowing crucible with the
stirrer "Spin"), a stack, parasteel ingots by the door.
  Mk I   Crucible shed: an open-fronted lean-to shed, one crucible on a stand under a steel arch, one stack, a pallet
         of ingots, gas bottles.
  Mk II  Induction works: a proper hall with a roof monitor, the crucible becomes an induction furnace with copper
         coils, a second stack, a roller cooling bed carrying ingots out, an argon tank.
  Mk III Twin melt: a second furnace under a portal gantry with a ladle hoist, sawtooth roof lights, a pouring robot
         arm, two horizontal argon tanks, bigger ingot stacks, Starfleet white + navy.
  Mk IV  Arc + glass line: an electric arc furnace (three electrodes rising from the lid) and the TRANSPARENT ALUMINUM
         kiln (cyan glass-walled kiln with a cooling tunnel) = the new recipe, scrubbers on the stacks, a covered
         conveyor.
  Mk V   Plasma conduit line: the hall clad white with a glass control gallery, a plasma-conduit extrusion line with
         a glowing test chamber (second new recipe), three stacks with scrubbers, two robot arms, light strips.
Everything stays inside the lanes' footprint_radius 7 (corners ~6.8).

GAME PARTS (BuildingModels.Foundry / PlanetView): "Spin" = the stirrer over the main melt (turns about Godot Y while
it works); glow materials "FurnaceGlow" (melt, pulse with work) and "TransparentAluminum" / "ScienceGlow" (kiln and
plasma line; would light only while that recipe runs: needs code). Markers SignFront, SparkPoint.

Run: /home/user/.bpyvenv/bin/python blender/build_foundry_marks.py --preview blender/previews/marks/foundry
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
import marks_kit as MK  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, strut, empty, cylinder  # noqa: E402

SLAB = 0.3


def stack(g, name, x, y, h, r, scrubber=False, mat=None):
    zcyl(f"{name}Foot", r * 1.5, SLAB, SLAB + 0.6, MT["concrete"], g, xy=(x, y), segs=16, radius2=r * 1.2)
    zcyl(f"{name}", r, SLAB + 0.6, SLAB + h, mat or MT["hull"], g, xy=(x, y), segs=16, radius2=r * 0.85)
    for i, f in enumerate((0.55, 0.8)):
        zcyl(f"{name}Band{i}", r * (1.0 - 0.15 * f) + 0.02, SLAB + h * f, SLAB + h * f + 0.25, MT["owner"] if i else MT["accent"], g, xy=(x, y), segs=16)
    zcyl(f"{name}Lip", r * 0.95, SLAB + h, SLAB + h + 0.2, MT["dark"], g, xy=(x, y), segs=16)
    sphere(f"{name}Beacon", 0.08, P(x + r * 0.8, y, SLAB + h + 0.25), MT["beacon"], g, 8)
    if scrubber:
        zcyl(f"{name}Scrub", r * 1.35, SLAB + h * 0.3, SLAB + h * 0.3 + 1.4, MT["white"], g, xy=(x, y), segs=16)
        zcyl(f"{name}ScrubNavy", r * 1.37, SLAB + h * 0.3 + 0.5, SLAB + h * 0.3 + 0.7, MT["navy"], g, xy=(x, y), segs=16)
        K.ladder(f"{name}Ladder", (x + r * 1.0, y - r * 0.6, SLAB + 0.6), SLAB + h * 0.3, (1, -0.6), g, cage=False)


def crucible(g, x, y, r, mark, name="Crucible", spin=True):
    """Open crucible on a stand (Mk I) or induction furnace with copper coils (Mk II+); melt glow on top."""
    if mark == 1:
        for i in range(3):
            a = math.radians(120 * i + 30)
            beam(f"{name}Leg{i}", P(x + r * 1.1 * math.cos(a), y + r * 1.1 * math.sin(a), SLAB), P(x + r * 0.7 * math.cos(a), y + r * 0.7 * math.sin(a), SLAB + 0.9), 0.12, MT["trim"], g)
        zcyl(f"{name}", r, SLAB + 0.9, SLAB + 1.9, MT["dark"], g, xy=(x, y), segs=20, radius2=r * 1.15)
        top = SLAB + 1.9
    else:
        h = 1.9 + 0.2 * mark
        zcyl(f"{name}Base", r * 1.25, SLAB, SLAB + 0.45, MT["trim"], g, xy=(x, y), segs=20)
        zcyl(f"{name}", r, SLAB + 0.45, SLAB + h, MT["navy"] if mark >= 3 else MT["dark"], g, xy=(x, y), segs=20)
        for i in range(4 + (mark >= 3)):
            z = SLAB + 0.7 + i * (h - 1.0) / (4 + (mark >= 3))
            K.OP["torus"](f"{name}Coil{i}", r + 0.05, 0.07, (x, y, z), MT["copper"], g, segs=24)
        top = SLAB + h
    K.ring_mesh(f"{name}Rim", r * 0.75, r * 1.18, top, top + 0.12, MT["steel"], g, xy=(x, y), segs=24)
    zcyl(f"{name}Melt", r * 0.78, top - 0.05, top + 0.08, MT["hot"], g, xy=(x, y), segs=24)
    bx(f"{name}Spout", (0.25, 0.6, 0.15), P(x, y - r * 1.15, top - 0.05), MT["steel"], g, rot=(math.radians(-15), 0, 0))
    if spin:
        piv = empty("Spin", P(x, y, top + 1.2), g)
        bx("StirArm", (r * 1.6, 0.14, 0.14), P(0, 0, 0), MT["trim"], piv)
        strut("StirRod", P(r * 0.55, 0, 0), P(r * 0.55, 0, -1.1), 0.06, MT["steel"], piv, 8)
        zcyl("StirHub", 0.18, -0.1, 0.4, MT["dark"], piv, segs=12)
    return top


def arch(g, x, y, w, h, mat):
    for s in (-1, 1):
        bx(f"Arch{s}", (0.35, 0.35, h), P(x + s * w / 2, y, SLAB + h / 2), mat, g, 0.03)
    bx("ArchBeam", (w + 0.6, 0.45, 0.45), P(x, y, SLAB + h + 0.22), mat, g, 0.03)
    K.wall_stripes("ArchStripe", (x - w / 2 - 0.18, y - 0.18), (x - w / 2 + 0.18, y - 0.18), SLAB, SLAB + 1.4, g, pitch=0.3)


def ingots(g, x, y, n, layers):
    bx(f"Pallet{x}{y}", (1.3, 1.0, 0.12), P(x, y, SLAB + 0.06), MT["pallet"], g)
    k = 0
    for L in range(layers):
        for i in range(n):
            if L % 2 == 0:
                bx(f"Ingot{x}{y}{k}", (1.1, 0.26, 0.2), P(x, y - 0.36 + i * 0.36, SLAB + 0.22 + L * 0.21), MT["ingot"], g)
            else:
                bx(f"Ingot{x}{y}{k}", (0.26, 0.9, 0.2), P(x - 0.42 + i * 0.42, y, SLAB + 0.22 + L * 0.21), MT["ingot"], g)
            k += 1


def bottles(g, x, y, n):
    bx(f"BottleRack{x}", (0.3, n * 0.32 + 0.1, 1.1), P(x + 0.18, y, SLAB + 0.55), MT["steel"], g)
    for i in range(n):
        zcyl(f"Bottle{x}{i}", 0.13, SLAB, SLAB + 1.3, MT["hazard"] if i % 2 else MT["accent"], g, xy=(x, y - (n - 1) * 0.16 + i * 0.32), segs=10)


def htank(g, x, y, L, r, name):
    cylinder(f"{name}", r, L, P(x, y, SLAB + r + 0.4), MT["tank"], g, axis="Y", segments=18)
    for s in (-1, 1):
        sphere(f"{name}End{s}", r, P(x, y + s * L / 2, SLAB + r + 0.4), MT["tank"], g, 14)
        bx(f"{name}Saddle{s}", (r * 1.6, 0.25, r + 0.4), P(x, y + s * L * 0.3, SLAB + (r + 0.4) / 2), MT["trim"], g)
    cylinder(f"{name}Band", r + 0.02, 0.2, P(x, y, SLAB + r + 0.4), MT["owner"], g, axis="Y", segments=18)


def roller_bed(g, x0, x1, y, z, n_ingots):
    bx("BedFrame", (x1 - x0, 1.0, 0.18), P((x0 + x1) / 2, y, SLAB + z - 0.15), MT["trim"], g)
    for i in range(int((x1 - x0) / 0.4)):
        cylinder(f"Roller{i}", 0.07, 0.9, P(x0 + 0.2 + i * 0.4, y, SLAB + z), MT["steel"], g, axis="Y", segments=8)
    for s in (-1, 1):
        for i in range(3):
            bx(f"BedLeg{s}{i}", (0.1, 0.1, z - 0.2), P(x0 + 0.2 + i * (x1 - x0 - 0.4) / 2, y + s * 0.42, SLAB + (z - 0.2) / 2), MT["trim"], g)
    for i in range(n_ingots):
        xx = x0 + 0.6 + i * (x1 - x0 - 1.2) / max(1, n_ingots - 1)
        bx(f"BedIngot{i}", (0.8, 0.28, 0.2), P(xx, y, SLAB + z + 0.17), MT["hot"] if i == 0 else MT["ingot"], g)


def robot_arm(g, x, y, name, yaw=0.0, reach=1.6):
    base = empty(name, P(x, y, SLAB), g)
    base.rotation_euler.z = math.radians(yaw)
    zcyl(f"{name}Base", 0.45, 0, 0.35, MT["trim"], base, segs=16)
    zcyl(f"{name}Turret", 0.32, 0.35, 0.85, MT["hazard"], base, segs=16)
    a, b, c = P(0, 0, 0.85), P(0, -reach * 0.55, 0.85 + reach * 0.75), P(0, -reach * 1.15, 0.85 + reach * 0.45)
    beam(f"{name}Upper", a, b, 0.26, MT["hazard"], base)
    beam(f"{name}Fore", b, c, 0.2, MT["hazard"], base)
    sphere(f"{name}Elbow", 0.2, b, MT["dark"], base, 10)
    zcyl(f"{name}Wrist", 0.12, c.z - 0.5, c.z, MT["dark"], base, xy=(c.x, c.y), segs=10)
    bx(f"{name}Grip", (0.4, 0.12, 0.25), c + P(0, 0, -0.6), MT["steel"], base)


def kiln(g, x, y, mark):
    """Transparent aluminum kiln: a glass-walled chamber with a cyan melt, a cooling tunnel toward the front (-Y) and
    a stack of finished sheets."""
    bx("KilnBase", (2.2, 1.9, 0.5), P(x, y, SLAB + 0.25), MT["trim"], g, 0.03)
    bx("KilnBody", (2.0, 1.7, 1.7), P(x, y, SLAB + 1.35), MT["white"], g, 0.05)
    bx("KilnPaneF", (1.6, 0.04, 1.1), P(x, y - 0.86, SLAB + 1.4), MT["talu"], g)
    bx("KilnPaneS", (0.04, 1.3, 1.1), P(x - 1.01, y, SLAB + 1.4), MT["talu"], g)
    bx("KilnHood", (2.2, 1.9, 0.3), P(x, y, SLAB + 2.35), MT["navy"], g, 0.04)
    zcyl("KilnFlue", 0.22, SLAB + 2.5, SLAB + 3.8, MT["steel"], g, xy=(x + 0.5, y + 0.4), segs=10)
    bx("Tunnel", (1.0, 1.9, 0.9), P(x - 0.4, y - 1.9, SLAB + 0.75), MT["white"], g, 0.05)
    bx("TunnelGlow", (0.02, 1.7, 0.3), P(x - 0.91, y - 1.9, SLAB + 0.75), MT["talu"], g).visible_shadow = False
    bx("TunnelNavy", (1.02, 1.92, 0.12), P(x - 0.4, y - 1.9, SLAB + 1.14), MT["navy"], g)
    bx("SheetPallet", (1.1, 1.0, 0.12), P(x - 1.9, y - 2.6, SLAB + 0.06), MT["pallet"], g)
    for i in range(5):
        bx(f"Sheet{i}", (1.0, 0.9, 0.05), P(x - 1.9, y - 2.6, SLAB + 0.15 + i * 0.07), MT["talu"], g)


def plasma_line(g, x0, x1, y):
    """Plasma-conduit extrusion: a tube line from a winder to a test chamber with glowing rings."""
    bx("LineBed", (x1 - x0, 1.2, 0.4), P((x0 + x1) / 2, y, SLAB + 0.2), MT["trim"], g, 0.03)
    cylinder("Winder", 0.6, 0.8, P(x0 + 0.6, y, SLAB + 1.0), MT["copper"], g, axis="Y", segments=20)
    bx("WinderFrame", (0.4, 1.0, 1.4), P(x0 + 0.6, y, SLAB + 0.9), MT["navy"], g, 0.03)
    cylinder("Conduit", 0.22, x1 - x0 - 2.6, P((x0 + x1) / 2 - 0.2, y, SLAB + 1.0), MT["steel"], g, axis="X", segments=12)
    for i in range(5):
        cylinder(f"ConduitClamp{i}", 0.28, 0.1, P(x0 + 1.6 + i * (x1 - x0 - 3.4) / 4, y, SLAB + 1.0), MT["navy"], g, axis="X", segments=12)
    cx = x1 - 0.9
    cylinder("TestChamber", 0.75, 1.4, P(cx, y, SLAB + 1.05), MT["white"], g, axis="X", segments=20)
    for i in range(3):
        cylinder(f"TestRing{i}", 0.78, 0.12, P(cx - 0.45 + i * 0.45, y, SLAB + 1.05), MT["cyan"], g, axis="X", segments=20)
    bx("TestWindow", (1.0, 0.04, 0.4), P(cx, y - 0.74, SLAB + 1.05), MT["cyan"], g)


def sign(M, g, x, y, z, w, text):
    bx("SignBoard", (w + 0.1, 0.06, 0.32), P(x, y - 0.03, z), MT["dark"], g)
    e = K.marker("SignFront", P(x, y - 0.07, z), (math.radians(90), 0, 0), w, 0.22, g)
    M.sign(e, text)


# ---------------------------------------------------------------- the marks


def build(mark, M):
    skin = MT["prefab"] if mark == 1 else MT["ribbed"] if mark == 2 else MT["hull"] if mark == 3 else MT["white"]
    accent = MT["accent"] if mark <= 2 else MT["navy"]

    g = M.piece("Foundation")
    if mark <= 2:
        MK.slab("Slab", [8.6, 10.6][mark - 1], [8.2, 9.6][mark - 1], SLAB, g, xy=(0, 0.2), mat=MT["concrete"])
    else:
        MK.slab("SlabEdge", 12.2, 11.0, SLAB - 0.12, g, xy=(0, 0.2), chamfer=2.6, mat=MT["dark"])
        MK.slab("Slab", 11.9, 10.7, SLAB, g, xy=(0, 0.2), chamfer=2.5, mat=MT["concrete"])
    K.stripes("PourZone", (-2.0, -1.0), (2.0, -1.0), 0.4, SLAB + 0.004, g, pitch=0.5)
    K.point("SparkPoint", P(0, -1.6, SLAB + 2.4), g)
    for k, (x, y, yaw) in enumerate([(-7.4, 0.5, 90), (7.4, 0.5, -90), (0.0, 7.2, 0), (-5.6, -5.0, 90), (5.6, -5.0, -90)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)

    # --- 1 casting hall
    g = M.piece("Hall")
    if mark == 1:
        hx0, hx1, hy0, hy1, hh = -3.2, 2.4, 0.6, 3.6, 3.2
        bx("ShedBack", (hx1 - hx0, 0.25, hh), P((hx0 + hx1) / 2, hy1, SLAB + hh / 2), skin, g, 0.03)
        for s, x in ((-1, hx0), (1, hx1)):
            bx(f"ShedSide{s}", (0.25, hy1 - hy0, hh * 0.8), P(x, (hy0 + hy1) / 2, SLAB + hh * 0.4), skin, g, 0.03)
        for x in (hx0 + 0.1, (hx0 + hx1) / 2, hx1 - 0.1):
            bx(f"ShedPost{x}", (0.18, 0.18, hh * 0.8), P(x, hy0, SLAB + hh * 0.4), MT["trim"], g)
        bx("ShedRoof", (hx1 - hx0 + 0.5, hy1 - hy0 + 0.8, 0.12), P((hx0 + hx1) / 2, (hy0 + hy1) / 2 - 0.1, SLAB + hh * 0.9), MT["ribbed"], g, 0.02,
           rot=(math.radians(-12), 0, 0))
        bx("ShedFloor", (hx1 - hx0, hy1 - hy0, 0.06), P((hx0 + hx1) / 2, (hy0 + hy1) / 2, SLAB + 0.03), MT["trim"], g)
        bx("Mould", (2.0, 0.9, 0.5), P(-1.0, 2.2, SLAB + 0.25), MT["dark"], g, 0.03)
        bx("MouldGlow", (1.8, 0.7, 0.04), P(-1.0, 2.2, SLAB + 0.51), MT["hot"], g)
        sign_at = ((hx0 + hx1) / 2, hy0 - 0.15, SLAB + hh * 0.86)
    else:
        hx0, hx1 = (-4.0, 3.2) if mark == 2 else (-4.4, 4.0)
        hy0, hy1 = (0.8, 4.0) if mark == 2 else (0.9, 4.3)
        hh = [0, 0, 3.8, 4.4, 4.6, 4.8][mark]
        bx("HallBody", (hx1 - hx0, hy1 - hy0, hh), P((hx0 + hx1) / 2, (hy0 + hy1) / 2, SLAB + hh / 2), skin, g, 0.05)
        bx("HallBand", (hx1 - hx0 + 0.02, hy1 - hy0 + 0.02, 0.4), P((hx0 + hx1) / 2, (hy0 + hy1) / 2, SLAB + 0.8), accent, g)
        bx("HallOwner", (hx1 - hx0 + 0.02, hy1 - hy0 + 0.02, 0.12), P((hx0 + hx1) / 2, (hy0 + hy1) / 2, SLAB + hh - 0.4), MT["owner"], g)
        bx("HallDoor", (2.6, 0.06, 2.8), P(0.0, hy0 - 0.03, SLAB + 1.4), MT["dark"], g)
        bx("HallDoorGlow", (2.4, 0.04, 1.2), P(0.0, hy0 - 0.06, SLAB + 0.75), MT["hot"], g).visible_shadow = False
        K.pane_row("HallWin", (hx0 + 0.4, hy0 - 0.01, 0), (1, 0, 0), None, 2.6, SLAB + hh - 1.3, SLAB + hh - 0.7, 3, MT["window"], g, frac=0.7)
        K.pane_row("HallWin2", (hx1 - 2.8, hy0 - 0.01, 0), (1, 0, 0), None, 2.4, SLAB + hh - 1.3, SLAB + hh - 0.7, 3, MT["window"], g, frac=0.7)
        top = SLAB + hh
        if mark == 2:
            bx("Monitor", (hx1 - hx0 - 1.6, 1.4, 0.9), P((hx0 + hx1) / 2, (hy0 + hy1) / 2, top + 0.45), skin, g, 0.03)
            K.pane_row("MonitorWin", ((hx0 + hx1) / 2 - (hx1 - hx0 - 1.8) / 2, (hy0 + hy1) / 2 - 0.71, 0), (1, 0, 0), None, hx1 - hx0 - 1.8, top + 0.2, top + 0.7, 6, MT["window"], g, frac=0.6)
            bx("MonitorRoof", (hx1 - hx0 - 1.3, 1.7, 0.12), P((hx0 + hx1) / 2, (hy0 + hy1) / 2, top + 0.96), MT["panel"], g, 0.02)
        elif mark == 5:
            MK.vault("Roof", hy1 - hy0 + 0.3, hx1 - hx0 + 0.2, 1.3, P((hx0 + hx1) / 2, (hy0 + hy1) / 2, top), g, MT["white"], axis="X", segs=14, ribs=5, rib_mat=MT["navy"])
            for i in range(9):
                bx(f"RoofGlass{i}", (0.7, 0.4, 0.06), P(hx0 + 0.6 + i * (hx1 - hx0 - 1.2) / 8, (hy0 + hy1) / 2, top + 1.31), MT["glass"], g)
        else:
            n = 4
            for i in range(n):   # sawtooth roof lights
                xx = hx0 + (hx1 - hx0) * (i + 0.5) / n
                obox(f"Saw{i}", ((hx1 - hx0) / n * 0.95, 0.1, 1.2), P(xx, (hy0 + hy1) / 2 + 0.3, top + 0.42), (0, 1, 0.9), MT["panel"] if mark < 5 else MT["white"], g)
                bx(f"SawGlass{i}", ((hx1 - hx0) / n * 0.9, 0.05, 0.75), P(xx, (hy0 + hy1) / 2 - 0.15, top + 0.4), MT["glass"], g)
        sign_at = ((hx0 + hx1) / 2 - 1.8, hy0 - 0.04, SLAB + hh - 0.55)
        if mark == 5:
            # glass control gallery along the front of the hall
            bx("Gallery", (4.0, 1.0, 1.0), P(1.6, hy0 - 0.5, SLAB + 3.3), MT["white"], g, 0.05)
            bx("GalleryGlass", (3.8, 0.04, 0.6), P(1.6, hy0 - 1.01, SLAB + 3.35), MT["glass"], g)
            bx("GalleryLit", (3.6, 0.02, 0.45), P(1.6, hy0 - 1.03, SLAB + 3.35), MT["window"], g).visible_shadow = False
            bx("GalleryNavy", (4.02, 1.02, 0.12), P(1.6, hy0 - 0.5, SLAB + 2.84), MT["navy"], g)
            for s in (-1, 1):
                beam(f"GalleryStrut{s}", P(1.6 + s * 1.7, hy0 - 0.9, SLAB), P(1.6 + s * 1.7, hy0 - 0.9, SLAB + 2.8), 0.16, MT["white"], g)
    sign(M, g, *sign_at, 2.4, ["", "FOUNDRY", "ALLOY WORKS 01", "ALLOY WORKS 01", "ALLOYS + GLASS 01", "STARFLEET ALLOYS"][mark])

    # --- 2 the melt: crucible / furnaces under the arch or gantry ("Spin")
    g = M.piece("Melt")
    if mark == 1:
        crucible(g, 0.0, -1.6, 0.7, mark)
        arch(g, 0.0, -1.6, 2.6, 3.4, MT["accent"])
    elif mark == 2:
        crucible(g, 0.0, -1.6, 0.85, mark)
        arch(g, 0.0, -1.6, 3.0, 4.0, MT["accent"])
    else:
        xs = (-1.4, 1.4)
        crucible(g, xs[0], -1.6, 0.85, mark)
        if mark == 3:
            crucible(g, xs[1], -1.6, 0.85, mark, name="Furnace2", spin=False)
        else:
            # electric arc furnace: squat shell, domed lid, three electrodes on a mast arm
            zcyl("ArcShell", 1.05, SLAB + 0.3, SLAB + 1.8, MT["navy"], g, xy=(xs[1], -1.6), segs=24)
            K.OP["dome"]("ArcLid", 1.1, 0.5, MT["steel"], g, (xs[1], -1.6, SLAB + 1.8), segs=24)
            for i in range(3):
                a = math.radians(120 * i + 90)
                ex, ey = xs[1] + 0.35 * math.cos(a), -1.6 + 0.35 * math.sin(a)
                zcyl(f"Electrode{i}", 0.13, SLAB + 2.0, SLAB + 4.2, MT["dark"], g, xy=(ex, ey), segs=10)
                zcyl(f"ElectrodeClamp{i}", 0.2, SLAB + 3.4, SLAB + 3.7, MT["copper"], g, xy=(ex, ey), segs=10)
            zcyl("ArcGlow", 0.5, SLAB + 2.25, SLAB + 2.32, MT["hot"], g, xy=(xs[1], -1.6), segs=16)
            bx("ElectrodeArm", (1.6, 0.4, 0.35), P(xs[1] + 0.6, -1.6, SLAB + 4.0), MT["hazard"], g, 0.03)
            zcyl("ElectrodeMast", 0.2, SLAB, SLAB + 4.3, MT["hazard"], g, xy=(xs[1] + 1.4, -1.6), segs=12)
        # portal gantry with a ladle hoist over both melts
        for s in (-1, 1):
            bx(f"Portal{s}", (0.4, 0.4, 4.6), P(s * 3.0, -1.6, SLAB + 2.3), MT["white"] if mark >= 4 else MT["accent"], g, 0.03)
        bx("PortalBeam", (6.6, 0.5, 0.5), P(0, -1.6, SLAB + 4.85), MT["white"] if mark >= 4 else MT["accent"], g, 0.03)
        K.wall_stripes("PortalStripe", (-3.2, -1.86), (-2.8, -1.86), SLAB, SLAB + 1.6, g, pitch=0.3)
        bx("Hoist", (0.7, 0.7, 0.5), P(0.0, -1.6, SLAB + 4.35), MT["hazard"], g, 0.03)
        strut("HoistRope", P(0, -1.6, SLAB + 4.1), P(0, -1.6, SLAB + 2.9), 0.03, MT["cable"], g, 5)
        zcyl("Ladle", 0.45, SLAB + 2.2, SLAB + 2.9, MT["dark"], g, xy=(0, -1.6), segs=16, radius2=0.55)
        zcyl("LadleMelt", 0.42, SLAB + 2.86, SLAB + 2.92, MT["hot"], g, xy=(0, -1.6), segs=16)
    M.info["keep"] |= {"Spin"}

    # --- 3 stacks
    g = M.piece("Stacks")
    if mark == 1:
        stack(g, "Stack0", 2.0, 3.0, 5.0, 0.42)
    elif mark == 2:
        stack(g, "Stack0", 2.6, 3.4, 6.2, 0.5)
        stack(g, "Stack1", -3.4, 3.4, 5.4, 0.42)
    elif mark == 3:
        stack(g, "Stack0", 3.4, 3.6, 7.0, 0.55)
        stack(g, "Stack1", -3.8, 3.6, 6.0, 0.45)
    else:
        stack(g, "Stack0", 3.5, 3.6, 7.6, 0.55, scrubber=True, mat=MT["white"])
        stack(g, "Stack1", -3.9, 3.6, 6.6, 0.48, scrubber=True, mat=MT["white"])
        if mark == 5:
            stack(g, "Stack2", -0.2, 4.6, 8.2, 0.5, scrubber=False, mat=MT["white"])

    # --- 4 product: ingots, cooling bed, conveyors
    g = M.piece("Product")
    if mark == 1:
        ingots(g, -3.0, -1.6, 3, 2)
    elif mark == 2:
        roller_bed(g, -4.6, -1.2, -0.2, 0.9, 3)
        ingots(g, -3.6, -2.8, 3, 3)
    else:
        roller_bed(g, -5.0, -2.4, -0.1, 0.9, 4)
        ingots(g, -4.2, -3.0, 3, 3 if mark == 3 else 4)
        ingots(g, -2.6, -3.5, 3, 2 + (mark >= 4))
        if mark >= 4:
            bx("ConveyorHood", (3.0, 1.1, 0.7), P(-3.7, -0.1, SLAB + 1.55), MT["white"], g, 0.04)
            bx("ConveyorHoodNavy", (3.02, 1.12, 0.12), P(-3.7, -0.1, SLAB + 1.25), MT["navy"], g)

    # --- 5 support: gas, tanks, robots
    g = M.piece("Support")
    if mark == 1:
        bottles(g, 3.0, -1.2, 3)
    elif mark == 2:
        bottles(g, 3.9, -2.2, 4)
        zcyl("Argon", 0.55, SLAB, SLAB + 2.4, MT["tank"], g, xy=(4.0, 0.0), segs=18)
        sphere("ArgonTop", 0.55, P(4.0, 0.0, SLAB + 2.4), MT["tank"], g, 14)
    elif mark == 3:
        htank(g, 4.6, -0.6, 2.6, 0.5, "ArgonA")
        robot_arm(g, 3.6, -3.4, "Robot0", yaw=-30, reach=1.5)
    elif mark == 4:
        kiln(g, 4.4, -0.4, mark)
        zcyl("Argon", 0.5, SLAB, SLAB + 2.6, MT["tank"], g, xy=(5.5, 1.6), segs=18)
        sphere("ArgonTop", 0.5, P(5.5, 1.6, SLAB + 2.6), MT["tank"], g, 14)
        robot_arm(g, 1.6, -3.9, "Robot0", yaw=-60, reach=1.3)
    else:
        kiln(g, 4.4, -0.4, mark)
        zcyl("Argon", 0.5, SLAB, SLAB + 2.6, MT["tank"], g, xy=(5.5, 1.6), segs=18)
        sphere("ArgonTop", 0.5, P(5.5, 1.6, SLAB + 2.6), MT["tank"], g, 14)
        plasma_line(g, -5.2, -0.6, -4.1)
        robot_arm(g, 1.6, -3.9, "Robot0", yaw=-60, reach=1.3)
        robot_arm(g, -3.0, -2.6, "Robot1", yaw=-120, reach=1.3)

    # --- 6 lights
    g = M.piece("Lights")
    if mark <= 2:
        K.light_mast("Flood", -3.8 if mark == 1 else -4.6, 0.6 if mark == 1 else 0.4, SLAB, 4.5 + mark, -30, g, heads=1)
    else:
        K.light_mast("FloodL", -5.4, 1.2, SLAB, 7.0, -30, g)
        K.light_mast("FloodR", 5.3, 2.2, SLAB, 7.0, 210, g)
    if mark >= 4:
        MK.strip_lights("PortalLight", (-2.8, -1.86), (2.8, -1.86), SLAB + 4.6, 6, g)
        MK.strip_lights("HallEdge", (-4.4, 0.86), (4.0, 0.86), SLAB + [0, 0, 0, 0, 4.4, 4.6][mark] - 0.15, 8, g)

    M.info["rover"] = (-6.8, -5.0, 140)
    M.lamp((1.0, 0.5, 0.15), 300, P(0.0, -1.6, 3.4))
    M.lamp((1.0, 0.9, 0.75), 220, P(-4.0, 0.5, 6.5))
    M.lamp((1.0, 0.85, 0.6), 120, P(0.0, 0.0, 2.0))
    if mark >= 4:
        M.lamp((0.45, 0.8, 1.0), 140, P(3.0, -2.2, 1.8))
        M.lamp((1.0, 0.9, 0.75), 220, P(5.0, 1.8, 6.5))
    if mark == 5:
        M.lamp((0.5, 0.85, 1.0), 120, P(-1.4, -5.2, 1.6))


MK.run("foundry", build,
       hero=((15.0, -20.0, 12.0), (0.0, 0.0, 2.8), 34),
       close=((8.0, -11.5, 5.0), (0.2, -2.2, 2.4), 30))
