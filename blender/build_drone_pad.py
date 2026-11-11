"""Drone Pad (`drone_pad`, drones / comms / missions contract docs/missions-contract.md section B "Models"): the
building that keeps two scout drones charged and launches them. Original design, building kit conventions
(building_kit.py, like build_machine_shop.py).

As the RTS camera sees it from the front (access side = front = Blender -Y = Godot +Z):
  * a round concrete pad r 6.5 (24-gon, Owner band round the rim, hazard ring line), a walk from the front edge;
  * two raised LANDING DECKS r 1.75 left and right of centre (dark non-slip grating, a white touchdown ring and cross,
    hazard edge, eight flush ring lights per deck = the meshes "PadLight0"/"PadLight1", material "PadLight"); the
    bay empties "Bay0"/"Bay1" sit at each deck's centre on its top surface (a drone placed there = landed);
  * a CHARGER ARM beside each deck on its outer side: a post with a slewing head and a boom carrying a hanging
    inductive charge head ("Charger0"/"Charger1"; the head's lamp is the separate mesh "ChargeLight0"/"1", material
    "ChargeLight"); rest = stowed, pointing back (+Y Blender / Godot -Z) clear of the deck;
  * at the back a small HANGAR / OPS box (5.2 x 3.0, walls 2.9, barrel-vault roof to ~3.8) with a roll-up "Door"
    toward the pad, an ops window, the "Status" board beside the door, the "SignFront" fascia, a spare-drone rack and
    workbench inside the door, roof vents, the Owner eaves band;
  * a comms MAST at the back right (lattice, 7.2 tall) with a tracking "Dish" and the "Beacon" on its whip;
  * battery cabinets + transformer at the back left, cable trenches from the chargers, a windsock, bollards.

PARTS (the game drives these; names from the contract, all pivots unrotated):
  Bay0, Bay1           empties at the deck centres (top surface, z 0.55 Blender = Godot y 0.55), local +Y up (Godot).
                       Bay0 = left (Godot x -2.75), Bay1 = right (x +2.75), both at Godot z +1.3 (front of centre).
  Charger0, Charger1   arm pivots (posts at Godot x -4.85 / +4.85, z +1.3, pivot height 2.05). Rest = stowed along
                       Godot -Z (back). Over the docked drone: Charger0 RotateY(-90 deg), Charger1 RotateY(+90 deg),
                       i.e. Godot Basis(Vector3.Up, -/+a), a = 0 .. 90; the head then hangs over the bay centre
                       ~0.15 above the docked drone's top (head bottom at Godot y 1.6).
  ChargeLight0/1       child meshes of the arms (material "ChargeLight", green-white, strength 3): on while charging.
  Door                 roll-up door pivot at the top of the opening (Godot y 2.62): scale Y 1 = shut, ~0.1 = open.
  Dish                 mast dish pivot (Godot (4.45, 6.55, -3.1)); rest aims front-up; RotateY turns it (tracking).
  Beacon               red beacon mesh on the mast whip (material "Beacon").
  PadLight0, PadLight1 the ring lights of each deck, separate meshes, material "PadLight" (white-amber, strength 4):
                       the view sets them by bay state (e.g. amber blinking on launch, green when charged).
  Status               material of the ops board beside the door (green by default; the view recolours it).
  SignFront ("DRONE PAD nn"), LabelBay0 ("1"), LabelBay1 ("2")  decal markers (LabelBay lie flat, text up).
  PadLamp              where the game hangs its OmniLight (over the decks); HangarLight inside the door.
  AddOnSpot0..4, RoofSpot0..1  variety spots. Build pieces Build0..Build7: Pad, Decks, Chargers, Hangar, Roof,
  Utilities, Mast, Lights. One grime atlas over the textured kit materials; Owner = pad rim, eaves, charger posts.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_drone_pad.py --game        game/models/buildings/drone_pad.glb (+ check)
  /home/user/.bpyvenv/bin/python blender/build_drone_pad.py --preview blender/previews/drone_pad
        drone_pad-{hero,game,close,night,top,back,pieces..}.png (a scout drone docked under Charger0 on Bay0, one
        lifting off Bay1; uses game/models/drones/scout_drone.glb when it exists)
  python3 blender/build_drone_pad.py --sheet blender/previews/drone_pad    contact sheet drone_pad-sheet.png (PIL)
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
_argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]

if "--sheet" in _argv:
    from glb_check import contact_sheet
    folder = _argv[_argv.index("--sheet") + 1]
    pieces = sorted(f for f in os.listdir(folder) if f.startswith("drone_pad-pieces"))
    rows = [[("drone_pad-hero.png", "hero"), ("drone_pad-game.png", "game camera, zoom 50"),
             ("drone_pad-close.png", "close: docked + charging")],
            [("drone_pad-night.png", "night"), ("drone_pad-top.png", "plan"), ("drone_pad-back.png", "back")],
            [(p, "build piece " + p[16:18].lstrip("0")) for p in pieces[:3]]]
    contact_sheet(folder, rows, os.path.join(folder, "drone_pad-sheet.png"), 560, 373)
    sys.exit(0)

import building_kit as K  # noqa: E402  (imports bpy first)
from building_kit import MT, P, bx, obox, zcyl, sphere, lathe, empty, hoop, railing, ladder, pipe  # noqa: E402
from glb_check import verify_glb  # noqa: E402

CFG = K.setup("drone_pad")
bpy = K.bpy
bmesh = K.bmesh
Vector = K.Vector

SLAB = 0.3
R = 6.5
DECK_R = 1.75
DECK_TOP = 0.55
BAYS = [(-2.75, -1.3), (2.75, -1.3)]
POST_X = 4.85                      # charger posts at (+-POST_X, bay y)
ARM_Z = 2.05                       # arm pivot height (top of the post)
ARM_L = POST_X - 2.75              # post to bay centre
HEAD_BOTTOM = 1.6
HX0, HX1, HY0, HY1 = -2.6, 2.6, 2.35, 5.35
WT = SLAB + 2.9                    # hangar wall top
VAULT = 0.6                        # roof rise
DOOR = (-1.35, 1.35)
DOOR_H = 2.32
MAST = (4.45, 3.1)
MAST_H = 6.3
PIECES = ["Pad", "Decks", "Chargers", "Hangar", "Roof", "Utilities", "Mast", "Lights"]


def materials():
    K.materials()
    K.mat("status", "Status", (0.3, 1.0, 0.5), 0.0, 0.3, emission=(0.3, 1.0, 0.5), strength=3.0)
    K.mat("padlight", "PadLight", (1.0, 0.9, 0.7), 0.0, 0.3, emission=(1.0, 0.85, 0.6), strength=4.0)
    K.mat("charge", "ChargeLight", (0.5, 1.0, 0.75), 0.0, 0.3, emission=(0.45, 1.0, 0.7), strength=3.0)
    K.mat("deck", "Deck", (0.2, 0.21, 0.22), 0.3, 0.75, textured=True)
    K.mat("label", "Label", (0.9, 0.9, 0.88), 0.0, 0.6, textured=True)
    K.mat("orange", "SignalOrange", (0.56, 0.13, 0.026), 0.0, 0.6, textured=True)
    K.mat("sock", "Windsock", (0.85, 0.25, 0.05), 0.0, 0.8, cull=False)


def box(name, x0, x1, y0, y1, z0, z1, m, g, bev=0.0):
    return bx(name, (x1 - x0, y1 - y0, z1 - z0), P((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), m, g, bev)


def ngon(name, r, z0, z1, m, g, xy=(0, 0), segs=24, rot=0.0):
    return lathe(name, [(0.0, z0), (r, z0), (r, z1), (0.0, z1)], m, g, (xy[0], xy[1], 0), segs, smooth=False)


def flat_ring(name, r0, r1, z, m, g, xy=(0, 0), segs=48):
    """A flat painted annulus lying at height z."""
    bm = bmesh.new()
    a = [bm.verts.new((xy[0] + r0 * math.cos(2 * math.pi * k / segs), xy[1] + r0 * math.sin(2 * math.pi * k / segs), z)) for k in range(segs)]
    b = [bm.verts.new((xy[0] + r1 * math.cos(2 * math.pi * k / segs), xy[1] + r1 * math.sin(2 * math.pi * k / segs), z)) for k in range(segs)]
    for k in range(segs):
        j = (k + 1) % segs
        bm.faces.new((a[k], b[k], b[j], a[j]))
    o = K.mesh_object(name, bm, m, g)
    o.visible_shadow = False
    return o


def vault(name, x0, x1, y0, y1, z0, rise, m, g, n=10, thick=0.0):
    """A barrel vault along X (arc in the YZ plane), closed ends; thick > 0 = a shell (eaves overhang)."""
    bm = bmesh.new()
    cy, hw = (y0 + y1) / 2, (y1 - y0) / 2
    prof = [(cy + hw * math.cos(math.pi * (1 - k / n)), z0 + rise * math.sin(math.pi * k / n)) for k in range(n + 1)]
    lo = [bm.verts.new((x0, y, z)) for y, z in prof]
    hi = [bm.verts.new((x1, y, z)) for y, z in prof]
    for k in range(n):
        bm.faces.new((lo[k], lo[k + 1], hi[k + 1], hi[k]))
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = K.mesh_object(name, bm, m, g, smooth=False)
    for poly in o.data.polygons:
        poly.use_smooth = abs(poly.normal.x) < 0.5
    return o


# ---------------------------------------------------------------- pieces


def pad(g):
    ngon("Pad", R, 0.0, SLAB, MT["concrete"], g)
    ngon("PadSkirt", R + 0.18, -0.4, 0.12, MT["dark"], g)  # sinks into sloped ground
    K.band("PadOwner", R + 0.005, 0.14, 0.25, MT["owner"], g, segs=24)
    z = SLAB + 0.004
    flat_ring("PadLine", R - 0.42, R - 0.34, z, MT["hazard"], g, segs=48)
    # the walk from the front edge to the hangar door, between the decks
    K.flat_path("Walk", [(0.0, -R + 0.25), (0.0, HY0 - 0.1)], 1.3, MT["panel"], z=SLAB + 0.003, parent=g)
    for k, y in enumerate((-4.6, -3.2, -1.8, -0.4, 1.0)):
        box(f"WalkChevron{k}", -0.08, 0.08, y - 0.25, y + 0.25, SLAB, SLAB + 0.006, MT["label"], g)
    K.stripes("DoorSill", (DOOR[1] + 0.05, HY0 - 0.05), (DOOR[0] - 0.05, HY0 - 0.05), 0.45, z, g, pitch=0.4)
    for k, (x, y) in enumerate([(-4.4, 1.6), (4.6, 0.8), (0.0, -5.5)]):
        bx(f"Drain{k}", (0.6, 0.26, 0.02), P(x, y, SLAB + 0.008), MT["grate"], g)
    for k, (x, y, yaw) in enumerate([(-6.9, -2.2, 90), (-6.4, 3.4, 60), (6.9, -2.4, -90), (-2.2, 6.6, 0), (2.2, 6.6, 0)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)


def decks(g):
    for k, (x, y) in enumerate(BAYS):
        ngon(f"DeckBase{k}", DECK_R + 0.12, SLAB, DECK_TOP - 0.1, MT["trim"], g, (x, y), 32)
        ngon(f"Deck{k}", DECK_R, DECK_TOP - 0.1, DECK_TOP, MT["deck"], g, (x, y), 32)
        K.band(f"DeckEdge{k}", DECK_R + 0.125, SLAB + 0.02, DECK_TOP - 0.12, MT["hazard"], g, xy=(x, y), segs=32)
        z = DECK_TOP + 0.004
        flat_ring(f"TouchRing{k}", 1.05, 1.17, z, MT["label"], g, (x, y), 40)
        flat_ring(f"OuterRing{k}", DECK_R - 0.16, DECK_R - 0.08, z, MT["orange"], g, (x, y), 40)
        # a touchdown cross (skids land along Y)
        box(f"CrossA{k}", x - 0.06, x + 0.06, y - 0.7, y + 0.7, DECK_TOP, z, MT["label"], g)
        box(f"CrossB{k}", x - 0.7, x + 0.7, y - 0.06, y + 0.06, DECK_TOP, z, MT["label"], g)
        # induction plate under the drone, a step at the front, a junction box at the back
        ngon(f"Coil{k}", 0.42, DECK_TOP, DECK_TOP + 0.012, MT["copper"], g, (x, y), 24)
        box(f"Step{k}", x - 0.5, x + 0.5, y - DECK_R - 0.45, y - DECK_R - 0.05, SLAB, SLAB + 0.12, MT["trim"], g)
        box(f"Junction{k}", x - 0.25, x + 0.25, y + DECK_R + 0.12, y + DECK_R + 0.42, SLAB, SLAB + 0.42, MT["panel"], g)
        box(f"JunctionLid{k}", x - 0.27, x + 0.27, y + DECK_R + 0.1, y + DECK_R + 0.44, SLAB + 0.42, SLAB + 0.46, MT["trim"], g)
        # ring lights: eight flush lamps round the deck edge, one mesh per bay
        bm = bmesh.new()
        for j in range(8):
            a = 2 * math.pi * (j + 0.5) / 8
            c = Vector((x + (DECK_R - 0.03) * math.cos(a), y + (DECK_R - 0.03) * math.sin(a), DECK_TOP))
            bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.07, radius2=0.06, depth=0.05,
                                  matrix=K.Matrix.Translation(c))
        o = K.mesh_object(f"PadLight{k}", bm, MT["padlight"], g)
        o.visible_shadow = False
        K.point(f"Bay{k}", P(x, y, DECK_TOP), g, 0.6)
        K.marker(f"LabelBay{k}", P(x, y - 1.36, z + 0.002), (0, 0, 0), 0.34, 0.26, g)


def charger(g, k):
    x, y = BAYS[k]
    s = -1 if k == 0 else 1
    px = s * POST_X
    # post, foot, owner band, cable conduit down to the trench
    zcyl(f"PostFoot{k}", 0.32, SLAB, SLAB + 0.16, MT["concrete"], g, xy=(px, y), segs=16)
    zcyl(f"Post{k}", 0.13, SLAB + 0.16, ARM_Z - 0.12, MT["hull"], g, xy=(px, y), segs=16)
    zcyl(f"PostOwner{k}", 0.135, ARM_Z - 0.75, ARM_Z - 0.55, MT["owner"], g, xy=(px, y), segs=16)
    zcyl(f"PostBand{k}", 0.135, SLAB + 0.3, SLAB + 0.5, MT["hazard"], g, xy=(px, y), segs=16)
    bx(f"PostCabinet{k}", (0.32, 0.42, 0.62), P(px + s * 0.27, y + 0.32, SLAB + 0.46), MT["panel"], g)
    bx(f"PostCabinetDoor{k}", (0.02, 0.32, 0.5), P(px + s * 0.44, y + 0.32, SLAB + 0.46), MT["trim"], g)
    # the arm: slewing head at the pivot, a boom along local +Y (rest = back), the charge head hanging at its tip
    arm = empty(f"Charger{k}", P(px, y, ARM_Z), g)
    zcyl(f"Slew{k}", 0.2, -0.12, 0.1, MT["trim"], arm, segs=18)
    bx(f"SlewMotor{k}", (0.2, 0.26, 0.2), (s * 0.2, -0.08, 0.0), MT["dark"], arm)
    L = ARM_L
    obox(f"Boom{k}", (0.16, L + 0.15, 0.2), P(0, L / 2, 0.05), P(0, 1, 0), MT["orange"], arm)
    obox(f"BoomTop{k}", (0.17, L * 0.6, 0.04), P(0, L * 0.45, 0.17), P(0, 1, 0), MT["hull"], arm)
    K.sagging(f"BoomCable{k}", P(s * 0.12, 0.1, -0.05), P(s * 0.1, L - 0.1, -0.02), 0.08, 0.025, MT["cable"], arm, n=6, segs=5)
    zcyl(f"HeadNeck{k}", 0.05, HEAD_BOTTOM - ARM_Z + 0.16, 0.0, MT["steel"], arm, xy=(0, L), segs=10)
    zcyl(f"HeadSleeve{k}", 0.08, -0.32, -0.05, MT["dark"], arm, xy=(0, L), segs=12)
    zcyl(f"Head{k}", 0.3, HEAD_BOTTOM - ARM_Z + 0.04, HEAD_BOTTOM - ARM_Z + 0.16, MT["hull"], arm, xy=(0, L), segs=24, radius2=0.22)
    lt = zcyl(f"ChargeLight{k}", 0.24, HEAD_BOTTOM - ARM_Z, HEAD_BOTTOM - ARM_Z + 0.04, MT["charge"], arm, xy=(0, L), segs=24)
    lt.visible_shadow = False
    # cable trench from the post to the hangar
    tx = px - s * 0.0
    K.flat_path(f"Trench{k}", [(tx, y + 0.65), (tx, 2.0), (s * 2.9, 2.0)], 0.34, MT["grate"], z=SLAB + 0.006, parent=g)


def chargers(g):
    for k in range(2):
        charger(g, k)


def hangar(g):
    z0 = SLAB
    yf, w = HY0, 0.18
    hull = MT["hull"]
    box("Plinth", HX0 - 0.1, HX1 + 0.1, HY0 - 0.1, HY1 + 0.1, SLAB, SLAB + 0.08, MT["trim"], g)
    box("Floor", HX0 + 0.1, HX1 - 0.1, HY0 + 0.1, HY1 - 0.1, SLAB + 0.08, SLAB + 0.1, MT["deck"], g)
    box("WallBack", HX0, HX1, HY1 - w, HY1, z0, WT, hull, g)
    box("WallLeft", HX0, HX0 + w, HY0 + w, HY1 - w, z0, WT, hull, g)
    box("WallRight", HX1 - w, HX1, HY0 + w, HY1 - w, z0, WT, hull, g)
    box("FrontL", HX0, DOOR[0], yf, yf + w, z0, WT, hull, g)
    box("FrontR", DOOR[1], HX1, yf, yf + w, z0, WT, hull, g)
    box("FrontOverDoor", DOOR[0], DOOR[1], yf, yf + w, z0 + DOOR_H, WT, hull, g)
    o = 0.025
    box("AccentF", HX0 - o, HX1 + o, yf - o, yf, z0, z0 + 0.4, MT["accent"], g)
    box("AccentB", HX0 - o, HX1 + o, HY1, HY1 + o, z0, z0 + 0.4, MT["accent"], g)
    box("AccentL", HX0 - o, HX0, yf, HY1, z0, z0 + 0.4, MT["accent"], g)
    box("AccentR", HX1, HX1 + o, yf, HY1, z0, z0 + 0.4, MT["accent"], g)
    ez0, ez1 = WT - 0.26, WT - 0.1
    box("OwnerEavesF", HX0 - o, HX1 + o, yf - o, yf, ez0, ez1, MT["owner"], g)
    box("OwnerEavesB", HX0 - o, HX1 + o, HY1, HY1 + o, ez0, ez1, MT["owner"], g)
    box("OwnerEavesL", HX0 - o, HX0, yf, HY1, ez0, ez1, MT["owner"], g)
    box("OwnerEavesR", HX1, HX1 + o, yf, HY1, ez0, ez1, MT["owner"], g)
    for k, (x, y) in enumerate([(HX0, HY0), (HX1, HY0), (HX0, HY1), (HX1, HY1)]):
        box(f"Corner{k}", x - 0.07, x + 0.07, y - 0.07, y + 0.07, SLAB, WT + 0.04, MT["trim"], g)
    for k in range(3):
        y = HY0 + 0.75 + k * 0.75
        box(f"RibL{k}", HX0 - 0.06, HX0, y - 0.05, y + 0.05, z0 + 0.4, WT - 0.26, MT["panel"], g)
        box(f"RibR{k}", HX1, HX1 + 0.06, y - 0.05, y + 0.05, z0 + 0.4, WT - 0.26, MT["panel"], g)
    # ops window left of the door, side windows
    box("OpsWinFrame", -2.35, -1.6, yf - 0.04, yf, z0 + 1.2, z0 + 2.15, MT["trim"], g)
    box("OpsWin", -2.29, -1.66, yf - 0.05, yf - 0.03, z0 + 1.26, z0 + 2.09, MT["window"], g).visible_shadow = False
    K.pane_row("WinRight", (HX1 + 0.012, HY0 + 0.6, 0), (0, 1, 0), None, 1.8, z0 + 1.5, z0 + 2.1, 2, MT["window"], g, frac=0.75)
    K.pane_row("WinLeft", (HX0 - 0.012, HY1 - 0.6, 0), (0, -1, 0), None, 1.8, z0 + 1.5, z0 + 2.1, 2, MT["window"], g, frac=0.75)
    # roll-up door: guides, hood, slats from the pivot at the top of the opening
    for k, x in enumerate((DOOR[0] - 0.06, DOOR[1] + 0.06)):
        box(f"DoorGuide{k}", x - 0.06, x + 0.06, yf - 0.12, yf, z0, z0 + DOOR_H + 0.04, MT["trim"], g)
    box("DoorHood", DOOR[0] - 0.16, DOOR[1] + 0.16, yf - 0.34, yf, z0 + DOOR_H, z0 + DOOR_H + 0.26, MT["trim"], g)
    box("DoorHoodBand", DOOR[0] - 0.17, DOOR[1] + 0.17, yf - 0.35, yf - 0.32, z0 + DOOR_H + 0.07, z0 + DOOR_H + 0.18, MT["hazard"], g)
    door = empty("Door", P(0, yf - 0.06, z0 + DOOR_H), g)
    dw = DOOR[1] - DOOR[0] - 0.02
    slats = 9
    sh = DOOR_H / slats
    for k in range(slats):
        zc = -(k + 0.5) * sh
        bx(f"DoorSlat{k}", (dw, 0.05, sh - 0.022), (0, 0, zc), MT["hazard"] if k == slats - 1 else MT["panel"], door)
        bx(f"DoorGroove{k}", (dw, 0.06, 0.022), (0, 0, zc - sh / 2), MT["trim"], door)
    bx("DoorWindows", (dw * 0.75, 0.06, sh * 0.5), (0, -0.005, -3.5 * sh), MT["glass"], door)
    # status board right of the door, fascia + sign above it, wall lamps
    box("StatusFrame", 1.6, 2.4, yf - 0.06, yf, z0 + 1.15, z0 + 1.75, MT["dark"], g)
    box("StatusBoard", 1.66, 2.34, yf - 0.08, yf - 0.06, z0 + 1.38, z0 + 1.68, MT["status"], g).visible_shadow = False
    for j in range(2):
        xx = 1.8 + j * 0.4
        box(f"StatusBay{j}", xx - 0.12, xx + 0.12, yf - 0.08, yf - 0.06, z0 + 1.2, z0 + 1.32, MT["status"], g).visible_shadow = False
    box("Fascia", DOOR[0] - 0.05, DOOR[1] + 0.05, yf - 0.05, yf, WT - 0.24 - 0.0, WT - 0.0, MT["dark"], g)
    K.marker("SignFront", P(0, yf - 0.06, WT - 0.12), (math.radians(90), 0, 0), 2.3, 0.18, g)
    for k, x in enumerate((DOOR[0] - 0.55, DOOR[1] + 0.55)):
        bx(f"WallLamp{k}", (0.3, 0.18, 0.09), P(x, yf - 0.1, z0 + 2.45), MT["dark"], g)
        bx(f"WallLampLens{k}", (0.26, 0.13, 0.02), P(x, yf - 0.1, z0 + 2.4), MT["lamp"], g).visible_shadow = False
    # inside: a spare-drone rack on the back wall and a workbench (seen through the open door)
    box("Rack", -1.5, 1.5, HY1 - 0.7, HY1 - w, z0 + 0.1, z0 + 0.14, MT["steel"], g)
    for j, zz in enumerate((0.75, 1.45)):
        box(f"RackShelf{j}", -1.5, 1.5, HY1 - 0.7, HY1 - w, z0 + zz, z0 + zz + 0.04, MT["steel"], g)
    for j, xx in enumerate((-1.5, 0.0, 1.5)):
        box(f"RackPost{j}", xx - 0.03, xx + 0.03, HY1 - 0.7, HY1 - 0.64, z0 + 0.1, z0 + 2.0, MT["hazard"], g)
    for j, (xx, zz) in enumerate([(-0.75, 0.79), (0.75, 0.79), (-0.75, 1.49)]):
        bx(f"DroneCase{j}", (1.2, 0.45, 0.32), P(xx, HY1 - 0.44, z0 + zz + 0.16), MT["orange"] if j != 1 else MT["hull"], g)
    box("Bench", -2.3, -1.3, HY0 + 0.45, HY1 - 0.9, z0 + 0.82, z0 + 0.88, MT["steel"], g)
    box("BenchBody", -2.25, -1.35, HY0 + 0.5, HY1 - 0.95, z0 + 0.1, z0 + 0.82, MT["accent"], g)
    K.point("HangarLight", P(0, (HY0 + HY1) / 2, z0 + 2.3), g, 0.3)


def roof(g):
    box("RoofDeck", HX0 - 0.08, HX1 + 0.08, HY0 - 0.08, HY1 + 0.08, WT, WT + 0.08, MT["trim"], g)
    vault("Vault", HX0 - 0.12, HX1 + 0.12, HY0 - 0.14, HY1 + 0.14, WT + 0.08, VAULT, MT["hull"], g, n=12)
    for k, x in enumerate((HX0 - 0.13, -0.87, 0.87, HX1 + 0.13)):
        prof = [((HY0 + HY1) / 2 + ((HY1 - HY0) / 2 + 0.16) * math.cos(math.pi * (1 - j / 12)),
                 WT + 0.1 + (VAULT + 0.02) * math.sin(math.pi * j / 12)) for j in range(13)]
        for j in range(12):
            a, b = P(x, prof[j][0], prof[j][1]), P(x, prof[j + 1][0], prof[j + 1][1])
            K.beam(f"Rib{k}_{j}", a, b, 0.07, MT["trim"], g, h=0.05)
    # vents on the crown, a skylight, roof spots
    for k, x in enumerate((-1.6, 1.0)):
        zcyl(f"Vent{k}", 0.2, WT + VAULT - 0.05, WT + VAULT + 0.35, MT["panel"], g, xy=(x, (HY0 + HY1) / 2), segs=14)
        zcyl(f"VentCap{k}", 0.27, WT + VAULT + 0.35, WT + VAULT + 0.42, MT["trim"], g, xy=(x, (HY0 + HY1) / 2), segs=14)
    bx("Skylight", (0.9, 0.7, 0.06), P(-0.3, (HY0 + HY1) / 2, WT + VAULT + 0.05), MT["glass"], g)
    K.point("RoofSpot0", P(1.9, HY0 + 0.6, WT + 0.08 + VAULT * 0.55), g)
    K.point("RoofSpot1", P(-1.9, HY1 - 0.6, WT + 0.08 + VAULT * 0.55), g)


def utilities(g):
    z = SLAB
    # battery cabinets at the back left (behind the left deck), a transformer, conduit into the hangar
    for k in range(3):
        x = -5.15 + k * 0.62
        box(f"Battery{k}", x - 0.27, x + 0.27, 2.55, 3.15, z, z + 1.35, MT["hull"], g)
        box(f"BatteryDoor{k}", x - 0.23, x + 0.23, 2.53, 2.56, z + 0.1, z + 1.25, MT["panel"], g)
        box(f"BatteryVent{k}", x - 0.18, x + 0.18, 2.52, 2.54, z + 1.0, z + 1.18, MT["grate"], g)
        box(f"BatteryStripe{k}", x - 0.27, x + 0.27, 2.54, 2.56, z + 0.15, z + 0.25, MT["hazard"], g)
    box("BatteryRoof", -5.5, -3.5, 2.45, 3.25, z + 1.35, z + 1.42, MT["trim"], g)
    box("XfmrBase", -4.2, -3.0, 3.55, 4.45, z, z + 0.12, MT["concrete"], g)
    box("Xfmr", -4.05, -3.15, 3.7, 4.3, z + 0.12, z + 1.1, MT["accent"], g)
    for k in range(4):
        box(f"XfmrFin{k}", -4.13, -4.05, 3.78 + k * 0.13, 3.83 + k * 0.13, z + 0.2, z + 1.0, MT["trim"], g)
    for k in range(3):
        zcyl(f"Bushing{k}", 0.05, z + 1.1, z + 1.42, MT["label"], g, xy=(-3.9 + k * 0.3, 4.0), segs=8)
    pipe("Conduit", [P(-3.6, 2.85, z + 1.0), P(HX0 - 0.02, 2.85, z + 1.0)], 0.06, MT["steel"], g)
    pipe("XfmrLine", [P(-3.6, 3.7, z + 0.9), P(-3.6, 3.15, z + 0.9)], 0.05, MT["cable"], g, joints=False)
    # windsock on a pole at the front left
    wx, wy = -4.9, -3.4
    zcyl("SockPole", 0.05, z, z + 3.4, MT["hull"], g, xy=(wx, wy), segs=8)
    zcyl("SockPoleFoot", 0.14, z, z + 0.25, MT["hazard"], g, xy=(wx, wy), segs=10)
    sock = hoop("SockHoop", 0.15, 0.0, 0.03, MT["steel"], g, xy=(0, 0), segs=12)
    sock.rotation_euler = (0, math.radians(90), 0)
    sock.location = P(wx + 0.12, wy, z + 3.2)
    cone = lathe("Sock", [(0.15, 0.0), (0.12, 0.35), (0.095, 0.7), (0.07, 1.05)], MT["sock"], g, (wx + 0.12, wy, z + 3.2), 12)
    cone.rotation_euler = (0, math.radians(100), 0)
    band = lathe("SockBand", [(0.122, 0.33), (0.108, 0.52)], MT["label"], g, (wx + 0.12, wy, z + 3.2), 12)
    band.rotation_euler = (0, math.radians(100), 0)
    # fire point and an equipment cart by the right deck
    box("FireBox", 5.3, 5.7, 1.6, 1.9, z, z + 1.0, MT["valve"], g)
    zcyl("Extinguisher", 0.09, z + 0.05, z + 0.62, MT["valve"], g, xy=(5.5, 1.45), segs=10)
    box("Cart", 3.0, 3.8, 1.25, 1.75, z + 0.25, z + 0.62, MT["panel"], g)
    for sx in (3.1, 3.7):
        for sy in (1.3, 1.7):
            zcyl(f"CartWheel{sx}{sy}", 0.09, z, z + 0.25, MT["rubber"], g, xy=(sx, sy), segs=8)
    bx("CartCase", (0.6, 0.38, 0.22), P(3.4, 1.5, z + 0.73), MT["orange"], g)
    K.bollards("Bollard", [(-1.2, -5.2), (1.2, -5.2)], SLAB, g)


def mast(g):
    mx, my = MAST
    z = SLAB
    box("MastFoot", mx - 0.36, mx + 0.36, my - 0.36, my + 0.36, z, z + 0.16, MT["concrete"], g)
    K.truss("MastTruss", P(mx, my, z + 0.16), P(mx, my, MAST_H), 0.3, 0.045, MT["trim"], g, bays=10)
    box("MastCap", mx - 0.24, mx + 0.24, my - 0.24, my + 0.24, MAST_H, MAST_H + 0.08, MT["trim"], g)
    bx("MastBox", (0.22, 0.3, 0.45), P(mx - 0.26, my, z + 1.3), MT["panel"], g)
    ladder("MastLadder", (mx + 0.2, my - 0.25, z + 0.16), MAST_H - 0.3, (0, -1), g, cage=False, width=0.3)
    # dish on a yoke: pivot over the cap (turns about Z / Godot Y); the reflector tilted 35 deg up toward the front
    d = empty("Dish", P(mx, my, MAST_H + 0.25), g)
    zcyl("DishTurntable", 0.18, -0.17, -0.02, MT["dark"], d, segs=16)
    for s in (-1, 1):
        obox(f"DishYoke{s}", (0.06, 0.06, 0.5), P(s * 0.32, 0, 0.22), P(0, 1, 0), MT["hull"], d)
    tilt = K.Matrix.Translation(P(0, 0, 0.42)) @ K.Matrix.Rotation(math.radians(-55), 4, "X")
    holder = empty("DishFrame", (0, 0, 0), d)
    holder.matrix_basis = tilt
    r = 0.62
    lathe("Reflector", [(0.0, 0.0), (r * 0.35, 0.025), (r * 0.7, 0.09), (r, 0.19), (r * 0.98, 0.22), (r * 0.68, 0.12),
                        (r * 0.34, 0.055), (0.0, 0.03)], MT["label"], holder, segs=32)
    K.hoop("DishRim", r, 0.2, 0.025, MT["trim"], holder, segs=32)
    K.strut("DishFeed", P(0, 0, 0.03), P(0, 0, 0.5), 0.02, MT["steel"], holder, 6)
    sphere("DishHorn", 0.05, P(0, 0, 0.52), MT["dark"], holder, 8)
    bx("DishBack", (0.26, 0.26, 0.14), P(0, 0, -0.06), MT["dark"], holder)
    # whip + beacon above the dish
    zcyl("MastWhipBase", 0.05, MAST_H + 0.08, MAST_H + 0.2, MT["dark"], g, xy=(mx + 0.18, my + 0.18), segs=8)
    zcyl("MastWhip", 0.018, MAST_H + 0.2, MAST_H + 1.35, MT["steel"], g, xy=(mx + 0.18, my + 0.18), segs=6)
    sphere("Beacon", 0.09, P(mx + 0.18, my + 0.18, MAST_H + 1.42), MT["beacon"], g, 10)


def lights(g):
    # two short floodlight poles aimed at the decks, the light where the game hangs its OmniLight
    K.light_mast("FloodL", -6.0, 0.6, SLAB, 4.2, -20, g, heads=1)
    K.light_mast("FloodR", 6.0, 0.6, SLAB, 4.2, 200, g, heads=1)
    K.point("PadLamp", P(0, -1.3, 4.0), g, 0.4)


BUILDERS = [pad, decks, chargers, hangar, roof, utilities, mast, lights]

PIVOTS = {"Door", "Charger0", "Charger1", "Dish"}
SEPARATE = {"Beacon", "PadLight0", "PadLight1", "ChargeLight0", "ChargeLight1"}
EMPTIES = {"Bay0", "Bay1", "SignFront", "LabelBay0", "LabelBay1", "PadLamp", "HangarLight", "RoofSpot0", "RoofSpot1"} | \
    {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}
REQUIRED = sorted(KEEP | {"DronePad"})
SIGNS = {"SignFront": ("DRONE PAD 01", (0.92, 0.93, 0.95)), "LabelBay0": ("1", (0.92, 0.93, 0.95)),
         "LabelBay1": ("2", (0.92, 0.93, 0.95))}
DRONE_GLB = os.path.join(K.REPO, "game", "models", "drones", "scout_drone.glb")


def import_drone(loc, yaw=0.0, lift=0.0):
    if not os.path.exists(DRONE_GLB):
        return None
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=DRONE_GLB)
    new = [o for o in bpy.data.objects if o not in before]
    holder = bpy.data.objects.new("DroneHolder", None)
    K.link(holder)
    for o in new:
        if o.parent is None:
            o.parent = holder
    holder.location = Vector(loc) + Vector((0, 0, lift))
    holder.rotation_euler.z = math.radians(yaw)
    parts = {o.name.split(".")[0]: o for o in new}
    return holder, parts


def extras():
    """A drone docked on Bay0 (charging), one lifting off Bay1: nacelles up, rotors turned, skids tucked."""
    b0 = bpy.data.objects["Bay0"].matrix_world.translation
    b1 = bpy.data.objects["Bay1"].matrix_world.translation
    import_drone(b0, 0.0)
    got = import_drone(b1, -20.0, 1.6)
    if got:
        _, p = got
        for k in range(4):
            p[f"Rotor{k}"].rotation_mode = "XYZ"
            p[f"Rotor{k}"].rotation_euler.z = math.radians(25 * k + 10)
        p["Skid"].scale = (1, 1, 0.4)


def on_shot(name):
    D = bpy.data.objects
    D["Door"].scale.z = 0.12 if name != "back" else 1.0
    D["Charger0"].rotation_euler.z = math.radians(-90)
    D["Charger1"].rotation_euler.z = 0.0


def check_export():
    if CFG["out"]:
        verify_glb(CFG["out"], REQUIRED)


def main():
    materials()
    root = K.assemble("DronePad", PIECES, BUILDERS)
    night = [("NPad", (1.0, 0.92, 0.8), 160, P(0, -1.3, 4.0)), ("NHangar", (1.0, 0.85, 0.6), 60, P(0, 1.6, 2.6)),
             ("NFloodL", (1.0, 0.95, 0.85), 120, P(-5.6, 0.0, 4.2)), ("NFloodR", (1.0, 0.95, 0.85), 120, P(5.6, 0.0, 4.2))]
    shots = {"hero": ((11.5, -15.0, 9.5), (0.0, 0.2, 1.4), 32),
             "close": ((-1.2, -7.6, 4.2), (-2.6, -0.8, 1.0), 30),
             "night": ((-9.0, -15.0, 9.0), (0.0, 0.2, 1.4), 32),
             "top": ((0, -0.01, 60), (0, 0, 0), "ortho", 14.5),
             "back": ((-10.0, 13.5, 8.0), (0.0, 1.0, 1.8), 32)}
    K.finish(root, KEEP, SEPARATE, "DronePadGrime", SLAB, soot=None, shots=shots, night_lights=night, signs=SIGNS,
             rover_at=(-9.0, -7.5, 150), on_shot=on_shot, extras=extras)


main()
check_export()
