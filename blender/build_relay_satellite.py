"""Comms relay satellites (drones / comms / missions contract docs/missions-contract.md section B "Models"): the relays
whose coverage discs let drones fly. Two original designs, era-matched like the survey satellites (build_satellite.py,
whose helpers, palettes, bake and preview stage this script loads), one glb each in game/models/orbit/:

  prewarp  relay_satellite_prewarp.glb  Dawn of Warp, Earth-built: a hexagonal gold-foil bus with white decks and
                                        corner posts, a Shuttle-orange (#C8632A family) payload ring, a BIG white
                                        parabolic dish under the bus facing the planet (feed horn on a tripod at its
                                        focus, an orange rim marker), a crosslink terminal on top aiming at the ship
                                        (glowing "Link" aperture), two long two-panel solar wings on orange yokes, a
                                        whip, star tracker, red beacon, RCS quads. 3.32 across, ~1.9 tall, 0.9 deep.
  std      relay_satellite.glb          Federation, later eras: a flat lenticular drum (panelled light grey, Owner line
                                        round its rim), a deflector-style dish below (metal rings, glowing rim and core
                                        that read as "Link"), feed horn, a dorsal emitter spire, swept collector wings
                                        (dark blue cells, glowing leading edges), blue RCS glows and beacon. 3.0 across.

Units and axes as the survey satellites: 1 unit = 1 world unit (sized for the orbit view like them, no scale in Godot),
Blender +Y = front (Godot -Z, direction of travel), +Z = zenith (Godot +Y, toward the ship), -Z = nadir (Godot -Y,
the planet: the relay's big dish looks down at its station).

PARTS (same names in both glbs; every pivot unrotated, so Godot angles go on an identity rest):
  Body          everything static, joined, origin = the bus centre.
  PanelL/PanelR solar/collector wings, origin = hinge at the bus side. Rest = deployed. Fold exactly like the survey
                satellite: Godot PanelR.Basis = Basis(Vector3.Back, +a), PanelL = Basis(Vector3.Back, -a), a 0..90.
  Dish          the big dish, origin on its axis where it meets the bus/boom; opens toward nadir (Godot -Y). Turning
                it about local Y (Godot) is safe (tracking wobble); it is the parent of Horn.
  Horn          the feed horn at the dish focus (child of Dish), origin at the horn's mouth.
  Link          the feed's glowing aperture (child of Horn), material "Link" (emissive, exported at strength 1 so the
                importer keeps emission; the game pulses it). The crosslink terminal's lens on top uses the same
                material (inside Body), so pulsing the material lights both ends of the relay.
  Beacon        material "Beacon" (prewarp red, std blue-white), blinking.
  Thruster0..3  RCS glows, own materials Thruster0..3 (0 front-left, 1 front-right, 2 rear-left, 3 rear-right).
  Material "Owner" = the player-colour band (prewarp: round the top of the bus; std: round the drum's rim).

Run (bpy 5.x; CLAUDE.md "Blender here"):
  /home/user/.bpyvenv/bin/python blender/build_relay_satellite.py --game     both glbs (~15 s), re-imported + checked
  /home/user/.bpyvenv/bin/python blender/build_relay_satellite.py --preview blender/previews/relay_satellite
        relay-<design>-{hero,limb,folded}.png, relay-<design>-glb.png (export posed through its own pivots) and
        relay-small.png (both relays + the survey satellites at orbit-view size)
  python3 blender/build_relay_satellite.py --sheet blender/previews/relay_satellite      contact sheet (PIL)
Flags: --designs prewarp,std  --shots hero,limb,folded,glb,small  --res 1200  --samples 32  --bake-res 1024
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


if "--sheet" in argv:
    from glb_check import contact_sheet
    folder = arg("--sheet")
    rows = [[(f"relay-prewarp-{s}.png", f"pre-warp - {s}") for s in ("hero", "limb", "folded")] +
            [("relay-prewarp-glb.png", "pre-warp - glb pivots")],
            [(f"relay-std-{s}.png", f"standard - {s}") for s in ("hero", "limb", "folded")] +
            [("relay-std-glb.png", "standard - glb pivots")],
            [("relay-small.png", "orbit-view size: relays (front) + survey sats")]]
    contact_sheet(folder, rows, os.path.join(folder, "relay-sheet.png"), 480, 320)
    sys.exit(0)

import bpy  # noqa: E402  first: bpy puts bmesh and mathutils on the path
from mathutils import Vector  # noqa: E402

from glb_check import join_pivots, prune_empties, verify_glb  # noqa: E402


def load(script, extra):
    path = os.path.join(HERE, script)
    with open(path) as f:
        src = re.sub(r"^main\(\)\s*$", "", f.read(), flags=re.M)
    saved = sys.argv
    sys.argv = [path, *extra]
    ns = {"__name__": script[:-3], "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


S = load("build_satellite.py", ["--game"])
flat, painted, foil, tiles, cells, panelled = S["flat"], S["painted"], S["foil"], S["tiles"], S["cells"], S["panelled"]
box, cylinder, sphere, lathe, ring, prism, strut, empty, inset, loft, catmull = (
    S["box"], S["cylinder"], S["sphere"], S["lathe"], S["ring"], S["prism"], S["strut"], S["empty"], S["inset"],
    S["loft"], S["catmull"])

REPO = os.path.dirname(HERE)
GAME_DIR = os.path.join(REPO, "game", "models", "orbit")
GLB = {"prewarp": "relay_satellite_prewarp.glb", "std": "relay_satellite.glb"}
SURVEY = {"prewarp": "survey_satellite_prewarp.glb", "std": "survey_satellite.glb"}
DESIGNS = [s.strip() for s in arg("--designs", "prewarp,std").split(",") if s.strip()]
ALL_SHOTS = ["hero", "limb", "folded", "glb", "small"]
SHOTS = [s.strip() for s in arg("--shots", ",".join(ALL_SHOTS)).split(",") if s.strip()]
PREVIEW = arg("--preview")
GAME = "--game" in argv
RES = int(arg("--res", "1200"))
SAMPLES = int(arg("--samples", "32"))
BAKE_RES = int(arg("--bake-res", "1024"))
S["RES"], S["SAMPLES"] = RES, SAMPLES
if set(DESIGNS) - set(GLB):
    sys.exit("--designs takes prewarp and std")
if not GAME and not PREVIEW:
    sys.exit("nothing to do: pass --game and/or --preview <dir> (or --sheet <dir> with system python)")

PIVOTS = ["PanelL", "PanelR", "Dish", "Horn"]
KEEP = ["Link", "Beacon"] + [f"Thruster{k}" for k in range(4)]
REQUIRED = ["RelaySatellite", "Body"] + PIVOTS + KEEP
FOLD = 90.0
WHITE = (0.82, 0.82, 0.8)
ORANGE = (0.56, 0.13, 0.026)


# ---------------------------------------------------------------- palettes

def prewarp_palette():
    return {
        "white": painted("WhitePaint", WHITE, 0.05, 0.48, 0.05),
        "foil": foil("FoilGold", (1.0, 0.7, 0.28)),
        "silver": foil("FoilSilver", (0.82, 0.83, 0.86), 0.6, 0.25),
        "orange": painted("FoamOrange", ORANGE, 0.0, 0.8, 0.12, 9.0),
        "tiles": tiles("TileBlack"),
        "radiator": flat("Radiator", (0.88, 0.88, 0.87), 0.15, 0.3),
        "grey": flat("PaintGrey", (0.5, 0.51, 0.52), 0.3, 0.45),
        "dark": flat("PaintDark", (0.08, 0.08, 0.085), 0.3, 0.6),
        "steel": flat("Steel", (0.62, 0.63, 0.66), 0.9, 0.3),
        "owner": flat("Owner", (0.72, 0.1, 0.06), 0.1, 0.4),
        "cells": cells("SolarCell", (0.012, 0.02, 0.07), (0.55, 0.57, 0.62), 16.0, 0.4, 0.22),
        "back": flat("PanelBack", (0.7, 0.71, 0.72), 0.2, 0.45),
        "link": flat("Link", (0.4, 0.7, 1.0), 0.0, 0.3, emission=(0.45, 0.8, 1.0), strength=1.0),
        "beacon": flat("Beacon", (1.0, 0.2, 0.1), 0.0, 0.3, emission=(1.0, 0.18, 0.08), strength=6.0),
        "thr": S["thruster_mats"]((1.0, 0.62, 0.32), 1.0),
    }


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
        "plate": flat("DishPlate", (0.42, 0.45, 0.52), 0.75, 0.3),
        "ring": flat("DishRing", (0.78, 0.8, 0.84), 0.85, 0.22),
        "dishglow": flat("DishGlow", (0.5, 0.7, 1.0), 0.0, 0.3, emission=(0.6, 0.82, 1.0), strength=4.0),
        "link": flat("Link", (0.4, 0.7, 1.0), 0.0, 0.3, emission=(0.5, 0.8, 1.0), strength=1.0),
        "beacon": flat("Beacon", (0.6, 0.8, 1.0), 0.0, 0.3, emission=(0.65, 0.85, 1.0), strength=6.0),
        "thr": S["thruster_mats"]((0.5, 0.75, 1.0), 1.5),
    }


# ---------------------------------------------------------------- shared parts

def hex_prism(name, r, z0, z1, mat, parent, loc=(0, 0, 0)):
    poly = [(r * math.cos(math.radians(30 + 60 * k)), r * math.sin(math.radians(30 + 60 * k))) for k in range(6)]
    return prism(name, poly, z0, z1, mat, parent, loc, bev=0.006)


def big_dish(P, root, top, R, depth, focal, shell, rim, back, horn_mat, std=False):
    """A parabolic dish opening toward nadir: pivot `Dish` at `top` (bowl centre); child `Horn` at the focus with the
    `Link` aperture. z(r) = -(c + depth (r/R)^2)."""
    d = empty("Dish", top, root)
    c = 0.03

    def z(r):
        return -(c + depth * (r / R) ** 2)

    rs = [0.06, 0.15, 0.25, 0.35, 0.45, 0.55, 0.65, 0.75, 0.85, 0.93, 1.0]
    lathe("DishShell", [(R * f, z(R * f)) for f in rs], shell, d, seg=56, solid=0.014)
    ring("DishRim", R - 0.012, R + 0.012, z(R) - 0.014, z(R) + 0.012, rim, d, seg=64)
    lathe("DishBack", [(0.0, 0.0), (R * 0.22, -0.005), (R * 0.42, z(R * 0.42) + 0.04), (R * 0.12, z(R * 0.12) + 0.02)],
          back, d, seg=32)
    for k in range(8):  # back ribs
        a = math.radians(22.5 + 45 * k)
        cs, sn = math.cos(a), math.sin(a)
        strut(f"DishRib{k}", (0.1 * cs, 0.1 * sn, -0.01), (R * 0.95 * cs, R * 0.95 * sn, z(R * 0.95) + 0.016), 0.006,
              back, d, 6)
    fz = -focal
    h = empty("Horn", (0, 0, fz + 0.04), d)
    for k in range(3):  # feed tripod from the rim
        a = math.radians(90 + 120 * k)
        p = Vector((R * 0.9 * math.cos(a), R * 0.9 * math.sin(a), z(R * 0.9) - 0.004))
        strut(f"FeedLeg{k}", p, (0, 0, fz + 0.1), 0.008, rim, d, 6)
    # the horn: a flared cone opening up toward the dish, its mouth glowing (Link)
    lathe("HornBody", [(0.0, -0.1), (0.03, -0.1), (0.035, -0.06), (0.06, 0.0), (0.075, 0.035), (0.0, 0.035)],
          horn_mat, h, seg=20)
    lathe("Link", [(0.0, 0.04), (0.066, 0.04), (0.066, 0.033), (0.0, 0.033)], P["link"], h, seg=20)
    if std:
        ring("DishRimGlow", R + 0.01, R + 0.022, z(R) - 0.01, z(R) + 0.006, P["dishglow"], d, seg=64)
        for k, f in enumerate((0.38, 0.68)):
            ring(f"DishRing{k}", R * f - 0.008, R * f + 0.008, z(R * f) - 0.018, z(R * f) - 0.006, P["ring"], d, seg=56)
    else:
        box("RimMarker", (0.08, 0.04, 0.03), (0.0, R - 0.01, z(R) - 0.004), P["orange"], d, bev=0.003)
    return d


# ---------------------------------------------------------------- design: pre-warp relay

BUS_R, BUS_Z = 0.42, 0.56


def pw_wing(P, root, side):
    s = side
    hx = BUS_R * math.cos(math.radians(30)) + 0.03
    hinge = empty("PanelR" if s > 0 else "PanelL", (s * hx, 0.0, 0.08), root)
    cylinder("Knuckle", 0.026, 0.2, (s * 0.02, 0, 0), P["grey"], hinge, axis="Y", seg=14)
    box("HingeDrive", (0.04, 0.08, 0.06), (s * 0.02, 0, 0), P["dark"], hinge)
    strut("Mast", (s * 0.03, 0, 0), (s * 0.34, 0, 0), 0.014, P["white"], hinge)
    for y in (-0.24, 0.24):
        strut(f"Yoke{y}", (s * 0.2, 0.0, 0.0), (s * 0.34, y, 0.0), 0.011, P["orange"], hinge)
    strut("YokeBar", (s * 0.34, -0.27, 0.0), (s * 0.34, 0.27, 0.0), 0.012, P["white"], hinge)
    x0, w, gap, chord = 0.345, 0.4, 0.014, 0.7
    for k in range(2):
        a = x0 + k * (w + gap)
        poly = [(s * a, -chord / 2), (s * (a + w), -chord / 2), (s * (a + w), chord / 2), (s * a, chord / 2)]
        if s < 0:
            poly = list(reversed(poly))
        prism(f"Panel{k}", poly, -0.012, 0.009, P["back"], hinge, bev=0.003)
        prism(f"Cells{k}", inset(poly, 0.016), 0.009, 0.0125, P["cells"], hinge)
        box(f"Spar{k}", (w, 0.02, 0.016), (s * (a + w / 2), 0, -0.02), P["grey"], hinge, bev=0.0)
        if k == 0:
            for y in (-0.22, 0.22):
                box(f"PanelHinge{y}", (gap + 0.012, 0.04, 0.018), (s * (a + w + gap / 2), y, -0.002), P["grey"], hinge,
                    bev=0.0)
    tip = x0 + 2 * w + gap
    box("TipRail", (0.014, chord + 0.012, 0.026), (s * (tip + 0.005), 0, -0.002), P["orange"], hinge, bev=0.002)
    return hinge


def build_prewarp():
    P = prewarp_palette()
    root = empty("RelaySatellite")
    hz = BUS_Z / 2
    hex_prism("Bus", BUS_R - 0.01, -hz, hz, P["foil"], root)
    hex_prism("TopDeck", BUS_R + 0.012, hz - 0.01, hz + 0.025, P["white"], root)
    hex_prism("BottomDeck", BUS_R + 0.012, -hz - 0.02, -hz + 0.01, P["white"], root)
    for k in range(6):
        a = math.radians(30 + 60 * k)
        c = Vector((BUS_R * math.cos(a), BUS_R * math.sin(a), 0))
        box(f"Post{k}", (0.045, 0.045, BUS_Z), c, P["white"], root, bev=0.006, rot=(0, 0, a))
    # faces at 0 / 180 deg carry the wings; the other four get radiators
    for k in range(6):
        a = math.radians(60 * k)
        if k in (0, 3):  # +X / -X faces carry the wings: silver foil patch + hinge boss
            n = Vector((math.cos(a), math.sin(a), 0))
            fr = BUS_R * math.cos(math.radians(30))
            box(f"FoilPatch{k}", (0.01, 0.3, 0.24), n * (fr + 0.002) + Vector((0, 0, -0.1)), P["silver"], root, bev=0.0,
                rot=(0, 0, a))
            box(f"WingBoss{k}", (0.03, 0.14, 0.1), n * (fr + 0.012) + Vector((0, 0, 0.08)), P["grey"], root,
                rot=(0, 0, a))
            continue
        n = Vector((math.cos(a), math.sin(a), 0))
        fr = BUS_R * math.cos(math.radians(30))
        box(f"Radiator{k}", (0.012, 0.3, 0.28), n * (fr + 0.004) + Vector((0, 0, 0.02)), P["radiator"], root, bev=0.003,
            rot=(0, 0, a))
    # Owner band round the top of the bus (hex ring), orange payload ring under it
    ring("OwnerBand", BUS_R - 0.004, BUS_R + 0.016, hz - 0.075, hz - 0.045, P["owner"], root, seg=6).rotation_euler.z = \
        math.radians(30)
    ring("PayloadRing", 0.2, 0.3, -hz - 0.1, -hz - 0.02, P["orange"], root, seg=40)
    ring("PayloadLip", 0.18, 0.24, -hz - 0.13, -hz - 0.1, P["grey"], root, seg=40)
    hex_prism("TileDeck", BUS_R - 0.05, -hz - 0.03, -hz - 0.02, P["tiles"], root)
    # the dish boom and the big dish under the bus
    cylinder("DishBoom", 0.05, 0.14, (0, 0, -hz - 0.2), P["white"], root, seg=16)
    ring("BoomBand", 0.05, 0.056, -hz - 0.24, -hz - 0.2, P["orange"], root, seg=16)
    big_dish(P, root, (0, 0, -hz - 0.27), 0.68, 0.2, 0.62, P["white"], P["grey"], P["grey"], P["foil"])
    # zenith: crosslink terminal toward the ship, star tracker, sun sensor, whip, beacon
    cylinder("XlinkMast", 0.035, 0.18, (0, -0.12, hz + 0.11), P["white"], root, seg=14)
    cylinder("XlinkGimbal", 0.06, 0.05, (0, -0.12, hz + 0.22), P["grey"], root, seg=16)
    lathe("XlinkBarrel", [(0.0, 0.0), (0.075, 0.0), (0.085, 0.05), (0.085, 0.16), (0.0, 0.16)], P["white"], root,
          (0, -0.12, hz + 0.245), seg=24)
    ring("XlinkBand", 0.085, 0.09, hz + 0.3, hz + 0.33, P["orange"], root, (0, -0.12, 0), seg=24)
    cylinder("XlinkLens", 0.065, 0.006, (0, -0.12, hz + 0.408), P["link"], root, seg=24)
    box("Tracker", (0.07, 0.07, 0.06), (0.22, 0.2, hz + 0.055), P["grey"], root)
    cylinder("TrackerShade", 0.03, 0.06, (0.22, 0.22, hz + 0.11), P["dark"], root, seg=14, r2=0.04)
    box("SunSensor", (0.06, 0.04, 0.03), (-0.2, 0.22, hz + 0.04), P["dark"], root)
    strut("Whip0", (-0.25, -0.2, hz + 0.02), (-0.36, -0.36, hz + 0.45), 0.005, P["steel"], root, 6)
    strut("Whip1", (0.25, -0.2, -hz), (0.42, -0.36, -hz - 0.3), 0.005, P["steel"], root, 6)
    cylinder("BeaconBase", 0.026, 0.03, (0.18, -0.24, hz + 0.04), P["grey"], root, seg=12)
    beacon = sphere("Beacon", 0.024, (0.18, -0.24, hz + 0.07), P["beacon"], root, 16, 8)
    # RCS quads on the four non-wing lower corners, glowing nozzles facing out sideways
    thr = []
    for k, (sx, sy) in enumerate([(-1, 1), (1, 1), (-1, -1), (1, -1)]):
        a = math.atan2(sy * math.sin(math.radians(60)), sx * math.cos(math.radians(60)))
        n = Vector((math.cos(a), math.sin(a), 0))
        c = n * (BUS_R * math.cos(math.radians(30))) + Vector((0, 0, -hz + 0.07))
        box(f"RcsBlock{k}", (0.07, 0.07, 0.07), c + n * 0.035, P["grey"], root, rot=(0, 0, a))
        bell = cylinder(f"RcsBell{k}", 0.014, 0.03, c + n * 0.085, P["dark"], root, axis="X", seg=12, r2=0.022)
        bell.rotation_euler = (0, math.radians(90), a)
        t = cylinder(f"Thruster{k}", 0.017, 0.006, c + n * 0.103, P["thr"][k], root, axis="X", seg=12)
        t.rotation_euler = (0, math.radians(90), a)
        thr.append(t)
    wings = {s: pw_wing(P, root, s) for s in (-1, 1)}
    return {"root": root, "PanelL": wings[-1], "PanelR": wings[1], "P": P, "Beacon": beacon, "thrusters": thr}


# ---------------------------------------------------------------- design: standard (Federation) relay

def std_wing(P, root, side):
    s = side
    hinge = empty("PanelR" if s > 0 else "PanelL", (s * 0.5, 0.0, 0.0), root)
    plan = [(0.04, 0.24), (0.04, -0.22), (0.98, -0.42), (0.98, -0.2)]
    poly = [(s * x, y) for x, y in plan]
    if s < 0:
        poly = list(reversed(poly))
    prism("WingFrame", poly, -0.014, 0.014, P["frame"], hinge, bev=0.004)
    inner = inset(poly, 0.03)
    prism("CollectorTop", inner, 0.013, 0.018, P["collector"], hinge)
    prism("CollectorBot", inner, -0.018, -0.013, P["collector"], hinge)
    le0, le1 = (0.05, 0.24), (0.97, -0.2)
    strip = [(s * le0[0], le0[1] + 0.012), (s * le0[0], le0[1] - 0.012), (s * le1[0], le1[1] - 0.012),
             (s * le1[0], le1[1] + 0.012)]
    prism("LeadingEdge", strip, -0.009, 0.009, P["edge"], hinge)
    cylinder("Knuckle", 0.03, 0.48, (s * 0.02, 0.01, 0.0), P["dark"], hinge, axis="Y", seg=16)
    lathe("TipPod", [(0.0, -0.12), (0.03, -0.1), (0.034, 0.06), (0.0, 0.12)], P["trim"], hinge, (s * 1.0, -0.31, 0.0),
          seg=16).rotation_euler = (math.radians(-90), 0, 0)
    sphere("TipGlow", 0.017, (s * 1.0, -0.19, 0.0), P["edge"], hinge, 12, 6)
    return hinge


def build_std():
    P = std_palette()
    root = empty("RelaySatellite")
    # lenticular drum: lathe profile (r, z)
    prof = [(0.0, 0.2), (0.16, 0.19), (0.32, 0.15), (0.44, 0.09), (0.52, 0.03), (0.54, 0.0), (0.53, -0.04),
            (0.48, -0.09), (0.36, -0.13), (0.2, -0.15), (0.0, -0.155)]
    lathe("Drum", prof, P["hull"], root, seg=56)
    ring("RimBand", 0.535, 0.552, -0.018, 0.018, P["trim"], root, seg=56)
    ring("OwnerLine", 0.5, 0.53, 0.035, 0.05, P["owner"], root, seg=56)
    ring("RimGlowBand", 0.47, 0.5, -0.085, -0.07, P["edge"], root, seg=56)
    for k in range(12):  # dark radial panels on top
        a = math.radians(15 + 30 * k)
        box(f"TopPanel{k}", (0.012, 0.18, 0.012), (0.3 * math.cos(a), 0.3 * math.sin(a), 0.168), P["dark"], root,
            bev=0.0, rot=(math.radians(14), 0, a - math.pi / 2)).rotation_euler = (0, 0, a - math.pi / 2)
    # wing roots
    for s in (-1, 1):
        box(f"WingRoot{s}", (0.08, 0.44, 0.05), (s * 0.5, 0.0, 0.0), P["dark"], root, bev=0.01)
    # dorsal emitter spire toward the ship: tapered column, ring fins, glowing "Link" lens at the top
    lathe("Spire", [(0.0, 0.18), (0.1, 0.18), (0.07, 0.26), (0.045, 0.42), (0.05, 0.46), (0.0, 0.47)], P["trim"], root,
          seg=24)
    for k, z in enumerate((0.27, 0.34)):
        ring(f"SpireFin{k}", 0.05, 0.1 - 0.02 * k, z, z + 0.012, P["ring"], root, seg=24)
    cylinder("SpireLink", 0.042, 0.012, (0, 0, 0.468), P["link"], root, seg=20)
    # beacon aft on a short mast
    cylinder("BeaconMast", 0.012, 0.09, (0, -0.4, 0.13), P["trim"], root, seg=10)
    beacon = sphere("Beacon", 0.024, (0, -0.4, 0.185), P["beacon"], root, 16, 8)
    # under-drum neck and the dish
    cylinder("Neck", 0.09, 0.08, (0, 0, -0.18), P["dark"], root, seg=24)
    big_dish(P, root, (0, 0, -0.21), 0.6, 0.17, 0.52, P["plate"], P["ring"], P["dark"], P["trim"], std=True)
    # RCS: four glows on the rim at the diagonals, facing out
    thr = []
    for k, (sx, sy) in enumerate([(-1, 1), (1, 1), (-1, -1), (1, -1)]):
        a = math.atan2(sy, sx)
        n = Vector((math.cos(a), math.sin(a), 0))
        box(f"RcsHousing{k}", (0.05, 0.06, 0.04), n * 0.52, P["dark"], root, rot=(0, 0, a))
        t = cylinder(f"Thruster{k}", 0.018, 0.008, n * 0.552, P["thr"][k], root, axis="X", seg=16)
        t.rotation_euler = (0, math.radians(90), a)
        thr.append(t)
    wings = {s: std_wing(P, root, s) for s in (-1, 1)}
    return {"root": root, "PanelL": wings[-1], "PanelR": wings[1], "P": P, "Beacon": beacon, "thrusters": thr}


BUILDERS = {"prewarp": build_prewarp, "std": build_std}


def set_pose(parts, fold=0.0, dish=0.0):
    parts["PanelR"].rotation_euler = (0, math.radians(-fold), 0)
    parts["PanelL"].rotation_euler = (0, math.radians(fold), 0)
    d = bpy.data.objects.get("Dish")
    if d is not None:
        d.rotation_euler = (0, 0, math.radians(dish))
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- game export

def game_export(key):
    S["reset"]()
    parts = BUILDERS[key]()
    set_pose(parts)
    root = parts["root"]
    join_pivots(root, PIVOTS, KEEP)
    prune_empties(root, REQUIRED)
    S["KEEP"] = KEEP  # bake_colours skips these
    S["bake_colours"](root, BAKE_RES, f"Relay{key.capitalize()}Paint")
    path = os.path.join(GAME_DIR, GLB[key])
    S["export_glb"](root, path)
    verify_glb(path, REQUIRED)


# ---------------------------------------------------------------- previews

FRAMING = {  # target, (az, el, dist, lens, planet alpha, roll)
    "prewarp": {"target": (0, 0, -0.35), "hero": (36, 18, 5.6, 50, 6, 12), "limb": (100, -8, 5.4, 50, 3, -6),
                "folded": (-38, 16, 4.4, 50, 6, -10)},
    "std": {"target": (0, 0, -0.2), "hero": (36, 20, 5.2, 50, 6, 12), "limb": (100, -8, 5.0, 50, 3, -6),
            "folded": (-38, 16, 4.2, 50, 6, -10)},
}


def link_on(P, strength):
    P["link"].node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = strength


def preview_design(key):
    S["reset"]()
    cam, planet, air = S["setup_world"]()
    parts = BUILDERS[key]()
    link_on(parts["P"], 6.0)
    fr = FRAMING[key]
    for name in ("hero", "limb", "folded"):
        if name not in SHOTS:
            continue
        set_pose(parts, fold=FOLD if name == "folded" else 0.0)
        az, el, dist, lens, alpha, roll = fr[name]
        S["shot"](cam, fr["target"], az, el, dist, lens)
        S["place_planet"](cam, planet, air, alpha, roll)
        S["render"](os.path.join(PREVIEW, f"relay-{key}-{name}.png"))


def preview_glb(key):
    path = os.path.join(GAME_DIR, GLB[key])
    if not os.path.exists(path):
        print(f"no {path}: run --game first", flush=True)
        return
    S["reset"]()
    cam, planet, air = S["setup_world"]()
    for k, (fold, spin) in enumerate([(0, 0), (45, 0), (FOLD, 60)]):
        _, parts = S["import_glb"](path, ((1 - k) * 3.5, 0, 0))
        for name in ("PanelL", "PanelR", "Dish"):
            parts[name].rotation_mode = "XYZ"
        parts["PanelR"].rotation_euler = (0, math.radians(-fold), 0)
        parts["PanelL"].rotation_euler = (0, math.radians(fold), 0)
        parts["Dish"].rotation_euler = (0, 0, math.radians(spin))
    bpy.context.view_layer.update()
    S["shot"](cam, (0, 0, -0.3), 14, 22, 16.5, 50)
    S["place_planet"](cam, planet, air, 8, 6)
    S["render"](os.path.join(PREVIEW, f"relay-{key}-glb.png"))


def preview_small():
    S["reset"]()
    cam, planet, air = S["setup_world"]()
    placed = 0
    for key, x in (("std", 2.6), ("prewarp", -2.6)):
        for glb, y in ((GLB[key], 0.0), (SURVEY[key], -5.0)):
            path = os.path.join(GAME_DIR, glb)
            if not os.path.exists(path):
                continue
            holder, _ = S["import_glb"](path, (x, y, 0.0))
            for o in holder.children_recursive:
                o.visible_shadow = False
            placed += 1
    if not placed:
        print("small: run --game first", flush=True)
        return
    S["shot"](cam, (0, -2.5, 0), 12, 38, 62, 50)
    S["place_planet"](cam, planet, air, -11, 4, L=120)
    S["render"](os.path.join(PREVIEW, "relay-small.png"))


def main():
    if GAME:
        for key in DESIGNS:
            game_export(key)
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        S["PREVIEW"] = PREVIEW
        for key in DESIGNS:
            if {"hero", "limb", "folded"} & set(SHOTS):
                preview_design(key)
            if "glb" in SHOTS:
                preview_glb(key)
        if "small" in SHOTS:
            preview_small()


main()
