"""Federation Storage Container yard (`storage_container`), game-ready (owner 2026-11-06: full detail like the refinery).

A container yard as the RTS camera sees it from the front:
  * a concrete pad with crane rails down both sides; two columns of ribbed cargo containers, three rows deep, ground
    level ("Slot0".."Slot5") and an optional second tier ("Slot6".."Slot11"): the variety code shows a few of the upper
    ones and tints each container its own colour, so no two yards stack alike;
  * a rail-mounted GANTRY CRANE over the stacks ("Gantry" rolls along the rails, "Trolley" runs across the bridge,
    "Hook" = the container spreader on its ropes, "Cable" = the ropes, scaled to the hook's depth), operator cab,
    lamps under the bridge, beacons;
  * at the front: a cluster of hexagonal pressure modules (volatiles), the CARGO POD PAD where crawler pods set down
    ("PodPad"), the yard office with the "STORAGE nn" sign, and the FILL GAUGE tower ("FillLight0".."FillLight7",
    lit bottom-up by the player's storage share);
  * light masts, bollards, hazard striping, a pallet of crates by the pad.

Frame and conventions (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit; the pad
is 10.6 x 9.2 (corner radius 7.0, inside the 8.5 the scatter keeps clear); the gantry bridge tops out at 7.3.

BUILD PIECES (Build0..Build10): Pad, PodPad, ContainersLeft (Slot0-2), ContainersRight (Slot3-5), UpperTier
(Slot6-11), HexModules, Gantry, Office, FillGauge, Cargo, Lights.

GAME CONTRACT (BuildingModels.Storage.cs / PlanetView.Storage.cs):
  Gantry         pivot on the rails; moves along its parent's Godot Z (rest = middle row; GantryMin / GantryMax empties
                 mark the ends of its run).
  Trolley        child of Gantry, moves along Godot X (TrolleyMin / TrolleyMax empties, children of Gantry).
  Hook           child of Trolley, moves along Godot Y (rest = raised; HookLow, child of Trolley, = lowered).
  Cable          child of Trolley at the rope sheaves: ropes run from 0 to -1 along local Y; set Scale.Y = drop to the hook.
  Slot0..Slot11  container pivots (one mesh each, material "Container" = the paint to tint); 0-5 ground, 6-11 upper
                 (6+k sits on k).
  FillLight0..7  separate meshes, material "FillLight" (copy per light; lit bottom-up).
  PodPad         empty at the pad's surface (cargo pods land here). PadLight, Lamp, Window, Beacon glow materials.
  SignFront, LabelPad, LabelHex   decal markers. RoofSpot0, AddOnSpot0..4 variety spots.
  Masts          scale pivot (light masts).

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_storage.py --game          (export game/models/buildings/storage_container.glb)
  /home/user/.bpyvenv/bin/python blender/build_storage.py --game --preview blender/previews/storage
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, strut, empty, cylinder, hoop, railing, ladder, pipe  # noqa: E402
from mathutils import Vector  # noqa: E402

CFG = K.setup("storage_container")

SLAB = 0.3
PAD = (-5.3, 5.3, -4.6, 4.6)
RAIL_X = 4.85
COLS = (-1.95, 1.95)
ROWS = (-0.5, 1.5, 3.5)
CL, CW, CH = 3.6, 1.9, 2.0          # container length (x), width (y), height
GANTRY_Y = 1.5                       # rest: over the middle row
GANTRY_RUN = (-0.5, 3.5)
BRIDGE_Z = 6.5                       # bridge girder top
LEG_DY = 1.05
HOOK_UP = 4.75                       # spreader bottom, raised (clears the upper tier at 4.3)
HOOK_LOW = 4.38
POD = Vector((0.25, -3.15))
OFFICE = Vector((3.0, -3.45))
GAUGE = Vector((-4.6, -3.6))
HEXES = [Vector((-3.35, -3.7)), Vector((-1.9, -3.7)), Vector((-2.62, -2.5))]
RAIL_Y = (-2.4, 4.5)
PIECES = ["Pad", "PodPad", "ContainersLeft", "ContainersRight", "UpperTier", "HexModules", "Gantry", "Office",
          "FillGauge", "Cargo", "Lights"]


def materials():
    K.materials()
    K.mat("container", "Container", (0.8, 0.8, 0.8), 0.45, 0.5, textured=True)
    K.mat("padlight", "PadLight", (0.3, 0.6, 1.0), 0.0, 0.3, emission=(0.3, 0.62, 1.0), strength=5.0)
    K.mat("fill", "FillLight", (0.3, 1.0, 0.45), 0.0, 0.3, emission=(0.3, 1.0, 0.45), strength=4.0)
    K.mat("label", "Label", (0.9, 0.9, 0.88), 0.0, 0.6, textured=True)
    K.mat("crate", "Crate", (0.5, 0.42, 0.3), 0.1, 0.8, textured=True)


# ---------------------------------------------------------------- pieces


def pad(g):
    x0, x1, y0, y1 = PAD
    bx("Slab", (x1 - x0, y1 - y0, SLAB), P(0, 0, SLAB / 2), MT["concrete"], g, 0.06)
    bx("Skirt", (x1 - x0 + 0.3, y1 - y0 + 0.3, 0.14), P(0, 0, 0.07), MT["dark"], g, 0.03)
    z = SLAB + 0.003
    qs = []
    for x in (-3.9, 0.0, 3.9):
        qs.append((P(x - 0.02, y0 + 0.1, z), P(x + 0.02, y0 + 0.1, z), P(x + 0.02, y1 - 0.1, z), P(x - 0.02, y1 - 0.1, z)))
    for y in (-1.6, 0.5, 2.5):
        qs.append((P(x0 + 0.1, y - 0.02, z), P(x1 - 0.1, y - 0.02, z), P(x1 - 0.1, y + 0.02, z), P(x0 + 0.1, y + 0.02, z)))
    K.faces_obj("Joints", qs, MT["dark"], g)
    # painted bay outlines round each ground slot
    for c in COLS:
        for r in ROWS:
            hx, hy = CL / 2 + 0.08, CW / 2 + 0.05
            pts = [(c - hx, r - hy), (c + hx, r - hy), (c + hx, r + hy), (c - hx, r + hy), (c - hx, r - hy)]
            K.flat_path(f"Bay{c}{r}", pts, 0.05, MT["hazard"], z + 0.001, g)
    K.flat_path("FrontLine", [(x0 + 0.15, y0 + 0.15), (x1 - 0.15, y0 + 0.15)], 0.06, MT["hazard"], z + 0.001, g)
    # crane rails along both sides with end stops
    for k, x in enumerate((-RAIL_X, RAIL_X)):
        ry0, ry1 = RAIL_Y
        bx(f"RailBed{k}", (0.5, ry1 - ry0, 0.08), P(x, (ry0 + ry1) / 2, SLAB + 0.04), MT["steel"], g)
        bx(f"Rail{k}", (0.1, ry1 - ry0, 0.1), P(x, (ry0 + ry1) / 2, SLAB + 0.13), MT["steel"], g)
        for j, y in enumerate((ry0 + 0.15, ry1 - 0.15)):
            bx(f"RailStop{k}{j}", (0.5, 0.3, 0.45), P(x, y, SLAB + 0.22), MT["hazard"], g, 0.02)
        sx = 1 if k else -1
        a, b = ((sx * 5.0, ry0), (sx * 5.0, ry1)) if k == 0 else ((sx * 5.0, ry1), (sx * 5.0, ry0))
        K.stripes(f"RailStripe{k}", a, b, 0.22, z + 0.002, g, pitch=0.5)
    for k, (x, y) in enumerate([(0.0, -1.5), (-3.9, 2.5), (3.9, 2.5)]):
        bx(f"Drain{k}", (0.7, 0.35, 0.02), P(x, y, SLAB + 0.008), MT["grate"], g)
    K.bollards("Bollard", [(-1.0, y0 + 0.25), (1.75, y0 + 0.25), (4.3, y0 + 0.25), (-4.0, y0 + 0.25)], SLAB, g)
    for k, (x, y, yaw) in enumerate([(-6.7, 2.2, 90), (-6.7, -1.8, 90), (6.7, 2.2, -90), (6.7, -1.8, -90), (0.0, 6.0, 0)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)
    K.point("RoofSpot0", P(OFFICE.x - 0.6, OFFICE.y + 0.2, SLAB + 2.4), g)


def pod_pad(g):
    c = POD.to_3d()
    top = SLAB + 0.1
    zcyl("PodPadBase", 1.35, SLAB, top, MT["dark"], g, xy=POD, segs=32)
    K.ring_mesh("PodPadRing", 1.1, 1.26, top, top + 0.012, MT["hazard"], g, xy=POD, segs=32)
    qs = []
    for k in range(4):
        a0, a1 = math.radians(45 + 90 * k - 15), math.radians(45 + 90 * k + 15)
        for j in range(3):
            b0, b1 = a0 + (a1 - a0) * j / 3, a0 + (a1 - a0) * (j + 1) / 3
            qs.append((c + P(0.7 * math.cos(b0), 0.7 * math.sin(b0), top + 0.01), c + P(0.9 * math.cos(b0), 0.9 * math.sin(b0), top + 0.01),
                       c + P(0.9 * math.cos(b1), 0.9 * math.sin(b1), top + 0.01), c + P(0.7 * math.cos(b1), 0.7 * math.sin(b1), top + 0.01)))
    K.faces_obj("PodPadDashes", qs, MT["hazard"], g)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        zcyl(f"PodLamp{k}", 0.06, top, top + 0.07, MT["padlight"], g, xy=(POD.x + 1.3 * math.cos(a), POD.y + 1.3 * math.sin(a)), segs=8)
    K.point("PodPad", c + P(0, 0, top + 0.012), g, 1.0)
    K.marker("LabelPad", c + P(0, -0.42, top + 0.013), (0, 0, 0), 0.75, 0.2, g)


def container(name, parent, at):
    """One ribbed cargo container in its own pivot (origin at the floor centre): body in "Container" paint, corner posts
    and rails, ribs on the long sides, locking bars on the door end (+X), a label plate."""
    piv = empty(name, at, parent)
    hx, hy = CL / 2, CW / 2
    bx(f"{name}Shell", (CL - 0.1, CW - 0.06, CH - 0.12), P(0, 0, CH / 2), MT["container"], piv, 0.02)
    for sy in (-1, 1):
        for j in range(9):
            x = -hx + 0.3 + j * (CL - 0.6) / 8
            bx(f"{name}Rib{sy}{j}", (0.12, 0.05, CH - 0.32), P(x, sy * (hy - 0.01), CH / 2), MT["container"], piv)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bx(f"{name}Post{sx}{sy}", (0.14, 0.14, CH), P(sx * (hx - 0.07), sy * (hy - 0.07), CH / 2), MT["trim"], piv)
    for z in (0.06, CH - 0.06):
        for sy in (-1, 1):
            bx(f"{name}RailL{z}{sy}", (CL, 0.1, 0.12), P(0, sy * (hy - 0.05), z), MT["trim"], piv)
        for sx in (-1, 1):
            bx(f"{name}RailE{z}{sx}", (0.1, CW, 0.12), P(sx * (hx - 0.05), 0, z), MT["trim"], piv)
    for k in (-1, 1):
        for j in (0.35, 0.75):
            bx(f"{name}Bar{k}{j}", (0.04, 0.04, CH - 0.3), P(hx + 0.0, k * j * hy, CH / 2), MT["steel"], piv)
    bx(f"{name}Plate", (0.04, 0.5, 0.3), P(-hx + 0.04, 0.4, CH - 0.45), MT["label"], piv)
    bx(f"{name}Stripe", (CL - 0.3, 0.02, 0.1), P(0, -hy - 0.005, CH - 0.25), MT["owner"], piv)
    return piv


def containers_left(g):
    for r, y in enumerate(ROWS):
        container(f"Slot{r}", g, P(COLS[0], y, SLAB))


def containers_right(g):
    for r, y in enumerate(ROWS):
        container(f"Slot{3 + r}", g, P(COLS[1], y, SLAB))


def upper_tier(g):
    for c, x in enumerate(COLS):
        for r, y in enumerate(ROWS):
            container(f"Slot{6 + c * 3 + r}", g, P(x, y, SLAB + CH + 0.02))


def hex_modules(g):
    for k, c in enumerate(HEXES):
        h = 2.3 if k < 2 else 2.0
        K.hex_prism(f"HexBase{k}", 0.72, SLAB, SLAB + 0.18, MT["trim"], g, xy=c)
        K.hex_prism(f"HexBody{k}", 0.66, SLAB + 0.18, SLAB + h, MT["hull"], g, xy=c)
        K.hex_prism(f"HexBand{k}", 0.675, SLAB + h - 0.6, SLAB + h - 0.42, MT["owner"], g, xy=c)
        K.hex_prism(f"HexAccent{k}", 0.675, SLAB + 0.5, SLAB + 0.9, MT["accent"], g, xy=c)
        K.hex_prism(f"HexCap{k}", 0.6, SLAB + h, SLAB + h + 0.12, MT["panel"], g, xy=c)
        zcyl(f"HexValve{k}", 0.12, SLAB + h + 0.12, SLAB + h + 0.32, MT["steel"], g, xy=c, segs=8)
        K.hoop(f"HexWheel{k}", 0.14, SLAB + h + 0.36, 0.03, MT["valve"], g, xy=c, segs=10)
        # a pressure gauge window on the front face
        bx(f"HexGauge{k}", (0.3, 0.04, 0.22), c.to_3d() + P(0, -0.6, SLAB + 1.35), MT["glass"], g)
    bx("HexPlate", (0.9, 0.04, 0.3), HEXES[0].to_3d() + P(0, -0.605, SLAB + 1.0), MT["label"], g)
    K.marker("LabelHex", HEXES[0].to_3d() + P(0, -0.63, SLAB + 1.0), (math.radians(90), 0, 0), 0.8, 0.2, g)
    pipe("HexManifold", [HEXES[0].to_3d() + P(0.3, 0.45, SLAB + 0.35), HEXES[1].to_3d() + P(-0.3, 0.45, SLAB + 0.35)], 0.06, MT["steel"], g)
    pipe("HexFeed", [HEXES[2].to_3d() + P(0, -0.55, SLAB + 0.35), HEXES[2].to_3d() + P(0, -0.85, SLAB + 0.35)], 0.06, MT["steel"], g)


def gantry(g):
    piv = empty("Gantry", P(0, GANTRY_Y, 0), g)
    K.point("GantryMin", P(0, GANTRY_RUN[0], 0), g)
    K.point("GantryMax", P(0, GANTRY_RUN[1], 0), g)
    zb = BRIDGE_Z
    for k, x in enumerate((-RAIL_X, RAIL_X)):
        # bogie on the rail, sill beam, two legs up to the bridge end tie
        bx(f"Bogie{k}", (0.5, 2.7, 0.42), P(x, 0, SLAB + 0.38), MT["trim"], piv, 0.03)
        for j in (-1, 1):
            cylinder(f"BogieWheel{k}{j}", 0.2, 0.18, P(x, j * 0.95, SLAB + 0.36), MT["dark"], piv, axis="X", segments=12)
        K.wall_stripes(f"BogieStripe{k}", (x - 0.25, -1.36), (x + 0.25, -1.36), SLAB + 0.2, SLAB + 0.56, piv, pitch=0.25)
        for j in (-1, 1):
            beam(f"Leg{k}{j}", P(x, j * LEG_DY, SLAB + 0.55), P(x - (0.25 if k else -0.25), j * 0.62, zb - 0.6), 0.3, MT["hull"], piv)
        beam(f"LegTie{k}", P(x, -LEG_DY + 0.15, SLAB + 2.4), P(x, LEG_DY - 0.15, SLAB + 2.4), 0.16, MT["trim"], piv)
        strut(f"LegX{k}", P(x, -LEG_DY + 0.15, SLAB + 2.4), P(x - (0.18 if k else -0.18), 0.6, zb - 0.9), 0.05, MT["trim"], piv, 6)
        bx(f"EndTie{k}", (0.6, 1.6, 0.6), P(x - (0.15 if k else -0.15), 0, zb - 0.45), MT["hull"], piv, 0.03)
        bx(f"EndOwner{k}", (0.62, 1.62, 0.12), P(x - (0.15 if k else -0.15), 0, zb - 0.6), MT["owner"], piv)
    # the two box girders and the trolley rails on top
    for j in (-1, 1):
        bx(f"Girder{j}", (2 * RAIL_X + 0.4, 0.34, 0.6), P(0, j * 0.55, zb - 0.3), MT["hull"], piv, 0.02)
        bx(f"GirderRail{j}", (2 * RAIL_X, 0.08, 0.06), P(0, j * 0.55, zb + 0.03), MT["steel"], piv)
        bx(f"GirderStripe{j}", (2 * RAIL_X - 0.6, 0.02, 0.1), P(0, j * 0.72, zb - 0.32), MT["accent"], piv)
        for k in range(7):
            bx(f"GirderStiff{j}{k}", (0.05, 0.02, 0.5), P(-RAIL_X + 0.7 + k * (2 * RAIL_X - 1.4) / 6, j * 0.725, zb - 0.32), MT["trim"], piv)
        K.wall_stripes(f"GirderHaz{j}a", (-RAIL_X - 0.2, j * 0.725), (-RAIL_X + 0.5, j * 0.725), zb - 0.55, zb - 0.05, piv, out=0.0) if j < 0 else None
        K.wall_stripes(f"GirderHaz{j}b", (RAIL_X - 0.5, j * 0.725), (RAIL_X + 0.2, j * 0.725), zb - 0.55, zb - 0.05, piv) if j < 0 else None
    # walkway + rail along the back girder
    bx("BridgeWalk", (2 * RAIL_X, 0.45, 0.04), P(0, 0.98, zb - 0.55), MT["grate"], piv)
    railing("BridgeRail", [(-RAIL_X, 1.18), (RAIL_X, 1.18)], zb - 0.53, piv, h=0.8, post=1.1)
    # operator cab under the right end of the bridge
    cab = P(RAIL_X - 1.05, -0.9, zb - 1.15)
    bx("Cab", (1.0, 0.85, 1.0), cab, MT["hull"], piv, 0.04)
    bx("CabGlass", (0.92, 0.04, 0.5), cab + P(0, -0.43, 0.12), MT["glass"], piv)
    bx("CabLit", (0.8, 0.02, 0.35), cab + P(0, -0.455, 0.12), MT["window"], piv).visible_shadow = False
    bx("CabOwner", (1.02, 0.87, 0.1), cab + P(0, 0, -0.3), MT["owner"], piv)
    beam("CabHanger", cab + P(0, 0.2, 0.5), P(cab.x, -0.55, zb - 0.6), 0.1, MT["trim"], piv)
    # lamps under the girders, beacons on the end ties
    for k, x in enumerate((-2.6, 0.0, 2.6)):
        bx(f"BridgeLamp{k}", (0.36, 0.2, 0.14), P(x, -0.55, zb - 0.67), MT["dark"], piv)
        bx(f"BridgeLens{k}", (0.3, 0.14, 0.02), P(x, -0.55, zb - 0.75), MT["lamp"], piv).visible_shadow = False
    for k, x in enumerate((-RAIL_X, RAIL_X)):
        sphere(f"GantryBeacon{k}", 0.09, P(x - (0.15 if k else -0.15), 0, zb + 0.0), MT["beacon"], piv, 8)
    # the trolley: frame on wheels across both girders, hoist drum house, rope sheaves
    tro = empty("Trolley", P(0.8, 0, zb + 0.06), piv)
    K.point("TrolleyMin", P(COLS[0], 0, zb + 0.06), piv)
    K.point("TrolleyMax", P(COLS[1], 0, zb + 0.06), piv)
    bx("TrolleyFrame", (1.3, 1.5, 0.22), P(0, 0, 0.2), MT["trim"], tro, 0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cylinder(f"TrolleyWheel{sx}{sy}", 0.11, 0.08, P(sx * 0.45, sy * 0.55, 0.08), MT["dark"], tro, axis="Y", segments=10)
    bx("HoistHouse", (1.1, 0.9, 0.55), P(0, 0.15, 0.58), MT["accent"], tro, 0.03)
    bx("HoistHouseOwner", (1.12, 0.92, 0.08), P(0, 0.15, 0.62), MT["owner"], tro)
    cylinder("HoistDrum", 0.2, 0.9, P(0, -0.45, 0.48), MT["steel"], tro, axis="X", segments=12)
    sphere("TrolleyLamp", 0.07, P(0.5, -0.6, 0.38), MT["beacon"], tro, 8)
    # ropes (scaled to the hook) and the spreader (the hook block)
    cab_piv = empty("Cable", P(0, 0, 0.0), tro)
    for sx in (-1, 1):
        for sy in (-1, 1):
            strut(f"Rope{sx}{sy}", P(sx * 0.35, sy * 0.25, 0.0), P(sx * 0.35, sy * 0.25, -1.0), 0.022, MT["cable"], cab_piv, 5)
    rest = HOOK_UP - (zb + 0.06)
    cab_piv.scale = (1, 1, -rest - 0.3)      # rope length to the spreader top (+0.3 above its bottom)
    hook = empty("Hook", P(0, 0, rest), tro)
    K.point("HookLow", P(0, 0, HOOK_LOW - (zb + 0.06)), tro)
    bx("Spreader", (CL - 0.2, 0.5, 0.18), P(0, 0, 0.09), MT["hazard"], hook, 0.02)
    bx("SpreaderBeam", (0.9, 0.7, 0.14), P(0, 0, 0.24), MT["dark"], hook, 0.02)
    for sx in (-1, 1):
        bx(f"Twistlock{sx}", (0.15, CW - 0.2, 0.1), P(sx * (CL / 2 - 0.2), 0, 0.03), MT["trim"], hook)
    zcyl("HeadBlock", 0.18, 0.3, 0.42, MT["steel"], hook, segs=10)


def office(g):
    c = OFFICE.to_3d()
    w, d = 2.4, 1.5
    bx("OfficeBase", (w + 0.1, d + 0.1, 0.22), c + P(0, 0, SLAB + 0.11), MT["trim"], g, 0.02)
    bx("OfficeBody", (w, d, 1.2), c + P(0, 0, SLAB + 0.22 + 0.6), MT["hull"], g, 0.04)
    bx("OfficeAccent", (w + 0.02, d + 0.02, 0.32), c + P(0, 0, SLAB + 0.48), MT["accent"], g)
    bx("OfficeGlass", (w - 0.06, d - 0.06, 0.66), c + P(0, 0, SLAB + 1.75), MT["glass"], g, 0.02)
    for k in range(4):
        bx(f"OfficeMullion{k}", (0.07, 0.07, 0.68), c + P((k - 1.5) * 0.78, -d / 2 + 0.03, SLAB + 1.75), MT["trim"], g)
    K.pane_row("OfficeLit", (c.x - w / 2 + 0.15, c.y - d / 2 + 0.025, 0), (1, 0, 0), None, w - 0.3, SLAB + 1.52, SLAB + 1.95, 3, MT["window"], g, frac=0.82)
    bx("OfficeRoof", (w + 0.34, d + 0.34, 0.14), c + P(0, 0, SLAB + 2.15), MT["panel"], g, 0.03)
    bx("OfficeFascia", (w + 0.36, 0.05, 0.3), c + P(0, -d / 2 - 0.19, SLAB + 2.12), MT["dark"], g)
    K.marker("SignFront", c + P(0, -d / 2 - 0.22, SLAB + 2.12), (math.radians(90), 0, 0), 2.3, 0.2, g)
    bx("OfficeOwner", (w + 0.02, d + 0.02, 0.11), c + P(0, 0, SLAB + 1.33), MT["owner"], g)
    bx("OfficeDoor", (0.05, 0.75, 1.1), c + P(-w / 2 - 0.01, 0.1, SLAB + 0.77), MT["trim"], g)
    bx("OfficeDoorLamp", (0.06, 0.4, 0.06), c + P(-w / 2 - 0.03, 0.1, SLAB + 1.4), MT["window"], g).visible_shadow = False
    top = SLAB + 2.22
    bx("OfficeAc", (0.85, 0.65, 0.38), c + P(0.6, 0.2, top + 0.19), MT["panel"], g, 0.03)
    zcyl("OfficeAcFan", 0.23, top + 0.38, top + 0.41, MT["dark"], g, xy=(c.x + 0.6, c.y + 0.2), segs=12)
    zcyl("OfficeMast", 0.04, top, top + 1.6, MT["steel"], g, xy=(c.x + 1.0, c.y - 0.5), segs=6)
    sphere("OfficeMastLamp", 0.06, c + P(1.0, -0.5, top + 1.65), MT["beacon"], g, 8)


def fill_gauge(g):
    c = GAUGE.to_3d()
    H = 4.6
    bx("GaugeFoot", (0.9, 0.7, 0.3), c + P(0, 0, SLAB + 0.15), MT["concrete"], g, 0.03)
    bx("GaugeColumn", (0.55, 0.38, H), c + P(0, 0, SLAB + 0.3 + H / 2), MT["hull"], g, 0.03)
    bx("GaugeBack", (0.62, 0.06, H - 0.4), c + P(0, -0.2, SLAB + 0.3 + H / 2), MT["dark"], g)
    bx("GaugeCap", (0.7, 0.5, 0.14), c + P(0, 0, SLAB + 0.37 + H), MT["owner"], g, 0.02)
    sphere("GaugeBeacon", 0.09, c + P(0, 0, SLAB + 0.55 + H), MT["beacon"], g, 8)
    for k in range(8):
        z = SLAB + 0.6 + k * (H - 0.8) / 7.5
        bx(f"FillLight{k}", (0.44, 0.05, 0.32), c + P(0, -0.235, z), MT["fill"], g).visible_shadow = False
        bx(f"GaugeTick{k}", (0.52, 0.03, 0.04), c + P(0, -0.23, z + 0.22), MT["steel"], g)
    # side scale ticks and a strut
    strut("GaugeStay", c + P(0.25, 0.15, SLAB + 2.8), c + P(0.75, 0.6, SLAB + 0.05), 0.04, MT["trim"], g, 6)


def cargo(g):
    # a pallet of crates and a pallet of drums by the pod pad, a hose reel, a fork-lift style loader
    def crate(name, x, y, z, s, m, yaw=0.0):
        bx(name, (s, s, s * 0.9), P(x, y, z + s * 0.45), m, g, 0.02, rot=(0, 0, math.radians(yaw)))
        bx(f"{name}Band", (s + 0.02, s + 0.02, 0.06), P(x, y, z + s * 0.6), MT["trim"], g, rot=(0, 0, math.radians(yaw)))
    bx("PalletA", (1.0, 1.0, 0.12), P(3.95, -2.0, SLAB + 0.06), MT["pallet"], g)
    crate("CrateA0", 3.75, -2.2, SLAB + 0.12, 0.48, MT["crate"], 4)
    crate("CrateA1", 4.2, -1.85, SLAB + 0.12, 0.42, MT["accent"], -6)
    crate("CrateA2", 3.85, -2.05, SLAB + 0.55, 0.4, MT["crate"], 10)
    # loader: a small electric cargo mule parked by the pad
    m = P(2.25, -1.95, SLAB)
    bx("MuleBody", (1.1, 0.75, 0.45), m + P(0, 0, 0.35), MT["hazard"], g, 0.04)
    bx("MuleSeat", (0.35, 0.5, 0.35), m + P(-0.25, 0, 0.75), MT["dark"], g, 0.03)
    bx("MuleMast", (0.08, 0.6, 1.3), m + P(0.6, 0, 0.8), MT["trim"], g)
    bx("MuleForks", (0.6, 0.5, 0.05), m + P(0.92, 0, 0.18), MT["steel"], g)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cylinder(f"MuleWheel{sx}{sy}", 0.15, 0.1, m + P(sx * 0.36, sy * 0.4, 0.15), MT["rubber"], g, axis="Y", segments=10)


def lights(g):
    piv = empty("Masts", P(0, 0, SLAB), g)
    K.light_mast("MastA", -4.95, 4.35, 0.0, 6.6, -45, piv)
    K.light_mast("MastB", 5.05, -2.95, 0.0, 6.0, 160, piv)
    K.point("SparkPoint", P(RAIL_X - 1.05, GANTRY_Y - 0.9, BRIDGE_Z - 1.2), g)


BUILDERS = [pad, pod_pad, containers_left, containers_right, upper_tier, hex_modules, gantry, office, fill_gauge, cargo, lights]

SLOTS = {f"Slot{k}" for k in range(12)}
FILLS = {f"FillLight{k}" for k in range(8)}
PIVOTS = {"Gantry", "Trolley", "Hook", "Cable", "Masts"} | SLOTS
SEPARATE = FILLS
EMPTIES = {"GantryMin", "GantryMax", "TrolleyMin", "TrolleyMax", "HookLow", "PodPad", "SignFront", "LabelPad", "LabelHex",
           "RoofSpot0", "SparkPoint"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("STORAGE 01", (0.92, 0.93, 0.95)), "LabelPad": ("PAD 01", (0.85, 0.62, 0.08)),
         "LabelHex": ("VOLATILES", (0.08, 0.08, 0.1))}

PREVIEW_UPPER = (6, 8, 10)
PREVIEW_COLOURS = [(0.72, 0.36, 0.22), (0.3, 0.45, 0.62), (0.78, 0.7, 0.42), (0.42, 0.55, 0.4), (0.62, 0.62, 0.64),
                   (0.55, 0.24, 0.2)]


def extras():
    """Preview the way the game dresses it: three upper containers, each slot its own colour, a pod on the pad, the
    fill gauge 5/8 lit."""
    bpy = K.bpy
    for k in range(12):
        o = bpy.data.objects[f"Slot{k}"]
        hide = k >= 6 and k not in PREVIEW_UPPER
        for c in [o] + list(o.children_recursive):
            c.hide_render = hide
        colour = PREVIEW_COLOURS[(k * 5) % len(PREVIEW_COLOURS)]
        m = MT["container"].copy()
        mixes = [n for n in m.node_tree.nodes if n.type == "MIX"]
        if mixes:
            mixes[0].inputs[7].default_value = (*colour, 1.0)
        else:
            m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*colour, 1.0)
        for c in [o] + list(o.children_recursive):
            if c.type == "MESH":
                for s in c.material_slots:
                    if s.material and s.material.name.startswith("Container"):
                        s.material = m
    off = K.mat("filloff", "FillOff", (0.04, 0.06, 0.05), 0.0, 0.4)
    for k in range(5, 8):
        o = bpy.data.objects[f"FillLight{k}"]
        o.material_slots[0].material = off
    pad = bpy.data.objects["PodPad"].matrix_world.translation
    hull = K.BV["material"]("PodHull", (0.88, 0.89, 0.92), 0.6, 0.3)
    band = K.BV["material"]("PodBand", (0.72, 0.08, 0.06), 0.0, 0.4, emission=(0.72, 0.08, 0.06), strength=2.0)
    holder = empty("PreviewPod", pad + P(0, 0, 0.6))
    cylinder("PodBody", 0.55, 1.1, (0, 0, 0), hull, holder, axis="Y", segments=16)
    for s in (-1, 1):
        sphere(f"PodCap{s}", 0.55, (0, s * 0.55, 0), hull, holder, 16)
    cylinder("PodBandP", 0.58, 0.25, (0, 0, 0), band, holder, axis="Y", segments=16)
    holder.rotation_euler.z = math.radians(30)


def main():
    materials()
    root = K.assemble("storage_container", PIECES, BUILDERS)
    night = [("NPad", (0.4, 0.6, 1.0), 120, P(POD.x, POD.y, 1.6)),
             ("NMastA", (1.0, 0.95, 0.85), 300, P(-4.4, 3.8, 6.6)),
             ("NMastB", (1.0, 0.95, 0.85), 300, P(4.5, -2.8, 6.0)),
             ("NBridge", (1.0, 0.95, 0.85), 160, P(0.0, GANTRY_Y - 0.6, BRIDGE_Z - 1.2)),
             ("NOffice", (1.0, 0.75, 0.45), 90, P(OFFICE.x, OFFICE.y - 1.5, 1.6))]
    shots = {"hero": ((15.5, -19.0, 11.5), (0.0, 0.0, 2.6), 32),
             "close": ((6.0, -9.5, 4.0), (0.8, -2.4, 1.6), 28),
             "night": ((-9.0, -19.0, 10.5), (0.0, 0.0, 2.6), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 18),
             "back": ((-14.0, 17.0, 10.0), (0.0, 0.5, 2.6), 32)}
    K.finish(root, KEEP, SEPARATE, "StorageGrime", SLAB, soot=None, shots=shots, night_lights=night, signs=SIGNS,
             rover_at=(-8.6, -6.4, 160), extras=extras)


main()
