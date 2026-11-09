"""Federation colony Machine Shop (`machine_shop`), game-ready (parts economy, 2026-11-09; contract
docs/parts-contract.md "Model"): the fabrication workshop that turns refined metal into parts, spare kits and
construction parts.

A fabrication shop as the RTS camera sees it from the front (access side = front = Blender -Y = Godot +Z):
  * a concrete pad 10.5 x 9 with markings, a hazard-striped apron in front of the big door, drains;
  * the FABRICATION HALL (7.2 x 5.7, walls 4.9) on the left: a big roll-up "Door" toward the access side, a glazed
    window band right of it (see-through "ShopGlass", the "FABRICATION" head sign) and a glazed roof lantern over the
    work cell, so the arms show from the game camera; Owner-coloured eaves band, accent plinth band, ribbed walls;
  * inside: the WORK CELL (a fixture table with a structural frame being welded, a safety fence) between two
    six-axis-style robot arms "Arm0"/"Arm1" (turret about local Y in Godot, elbow "Arm<k>Joint" about local X) with
    welding torches (SparkPoint0/1 at the tips, material "WeldGlow"); a hydraulic "Press" behind the window (strokes
    down to "PressLow"); a short belt "Conveyor" (material "Belt") from the press out through a port in the front wall
    to the OUTPUT RACK of kit crates (crates "Crate0..3" ride it under "CrateFlow" -> "CrateEnd"); parts shelving on the
    back wall, a kitting bench;
  * on the right: the output pallet rack (kit crates in the four kit colours), the control BOOTH with the emissive
    "Status" board, a materials HOPPER with a screw feeder into the hall, an outdoor bar-stock rack ("OptRack"), the
    transformer and compressor, a lattice mast with the "Beacon";
  * roof: lantern, two exhaust fans "Fan0"/"Fan1" (spin about local Y), vents, an AC unit.

Frame and conventions (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit.

BUILD PIECES (Build0..Build12): Pad, Frame, WorkCell, Press, Conveyor, Walls, Roof, OutputRack, PartsRacks, Hopper,
Booth, Utilities, Lights.

GAME CONTRACT (BuildingModels.MachineShop.cs, PlanetView.MachineShop.cs):
  Arm0, Arm1        robot arm turrets: rotate about local Y (Godot); rest pose = aimed at the work.
  Arm0Joint, ...    elbows (children of the arms): bend about local X; +angle raises the forearm.
  SparkPoint0/1     empties at the torch tips (children of the joints): weld sparks / broken sparks.
  Press             press ram + platen: moves down local -Y while working; sibling "PressLow" = the bottom of stroke.
  Door              roll-up door pivot at the top of the opening: scale Y 1 = shut, ~0.08 = open.
  Conveyor          the belt mesh (material "Belt", UV v every 0.6 units along the run, scroll Uv1Offset.y).
  CrateFlow         empty at the belt's start; child "CrateEnd" at its end; "Crate0..3" ride between.
  Status            material of the booth board + the door's status lamps (the view sets green/amber/red/grey).
  WeldGlow          material of the torch tips (the view lights it while welding).
  ShopGlass         see-through glazing (meshes "GlassFront", "GlassRoof"; the game swaps in its own material).
  Beacon            mesh on the mast (material "Beacon").
  Fan0, Fan1        roof fans. Masts = scale pivot. OptRack, OptCrates, OptAc = optional parts.
  ShopLight, CellLight  where the game hangs its OmniLights (hall, work cell).
  SignFront ("MACHINE SHOP nn"), SignHall ("FABRICATION"), LabelHopper, LabelRack  decal markers.
  AddOnSpot0..4, RoofSpot0..1  variety spots.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_machine_shop.py --game     (export game/models/buildings/machine_shop.glb)
  /home/user/.bpyvenv/bin/python blender/build_machine_shop.py --game --preview blender/previews/machine_shop --shots hero,game,close,night,pieces
  python3 blender/build_machine_shop.py --sheet blender/previews/machine_shop   (system python + PIL: pieces sheet)
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

if "--sheet" in sys.argv:
    # system python (PIL), no bpy: fold the pieces renders into one image
    from PIL import Image
    folder = sys.argv[sys.argv.index("--sheet") + 1]
    names = sorted(f for f in os.listdir(folder) if f.startswith("machine_shop-pieces") and f[19:21].isdigit())
    tiles = [Image.open(os.path.join(folder, n)).convert("RGB") for n in names]
    w, h = tiles[0].size
    img = Image.new("RGB", (2 * w, ((len(tiles) + 1) // 2) * h), (14, 16, 20))
    for i, t in enumerate(tiles):
        img.paste(t, ((i % 2) * w, (i // 2) * h))
    img.save(os.path.join(folder, "machine_shop-pieces.png"))
    print("wrote", os.path.join(folder, "machine_shop-pieces.png"), "from", names)
    sys.exit(0)

sys.path.insert(0, HERE)
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, lathe, empty, hoop, railing, ladder, pipe  # noqa: E402

CFG = K.setup("machine_shop")

SLAB = 0.3
PAD_X, PAD_Y = 5.25, 4.5
F = SLAB + 0.12                         # hall floor
HX0, HX1, HY0, HY1 = -5.0, 2.2, -1.6, 4.1
WALL = 0.2
WT = F + 4.9                            # wall top
RT = WT + 0.25                          # roof deck top
SKY = (-4.4, -0.7, -0.95, 3.45)         # roof lantern opening x0, x1, y0, y1
CURB = RT + 0.22
RIDGE = RT + 1.0
DOOR = (-4.3, -0.7)
DOOR_H = 3.9
DOOR_CX = (DOOR[0] + DOOR[1]) / 2
WIN = (-0.3, 1.95, F + 1.55, F + 3.55)  # window band x0, x1, z0, z1
PORT = (0.5, 1.4, F + 1.35)             # conveyor port x0, x1, top
CELL = (-2.5, 1.5)
REACH = 1.7
ARMS = [(CELL[0] - REACH, CELL[1], -90.0), (CELL[0] + REACH, CELL[1], 90.0)]
PED_Z = F + 0.6
ELBOW = (0.0, 0.5, 1.65)                # arm-local (local +Y = toward the work)
WRIST = (0.0, 1.15, -0.45)              # joint-local
TIP = (0.0, 1.2, -0.9)
TABLE_Z = F + 0.9
PRESS = (0.95, 2.55)
PRESS_Z = F + 2.35
PRESS_STROKE = 0.85
BELT_Z = F + 0.85
BELT_A = P(0.95, 1.75, BELT_Z)
BELT_B = P(0.95, -3.2, BELT_Z)
RACK = (1.55, 3.45, -4.1, -3.2)
BOOTH = (3.75, 5.05, -4.2, -2.45)
BOOTH_H = 2.6
HOPPER = (3.85, -1.1)
STOCK = (2.75, 4.95, 1.45)              # cantilever rack x0, x1, spine y
XFMR = (3.65, 3.45)
MAST = (4.8, 3.95)
KIT_KEYS = ["kitmech", "kitelec", "kitrover", "kitenv"]
PIECES = ["Pad", "Frame", "WorkCell", "Press", "Conveyor", "Walls", "Roof", "OutputRack", "PartsRacks", "Hopper",
          "Booth", "Utilities", "Lights"]


def materials():
    K.materials()
    K.belt_material()
    K.mat("status", "Status", (0.3, 1.0, 0.5), 0.0, 0.3, emission=(0.3, 1.0, 0.5), strength=3.0)
    K.mat("weld", "WeldGlow", (0.75, 0.88, 1.0), 0.0, 0.3, emission=(0.6, 0.8, 1.0), strength=0.6)
    K.mat("label", "Label", (0.9, 0.9, 0.88), 0.0, 0.6, textured=True)
    K.mat("floor", "Floor", (0.3, 0.34, 0.35), 0.1, 0.5, textured=True)
    K.mat("robot", "Robot", (0.88, 0.6, 0.13), 0.25, 0.4, textured=True)
    K.mat("kitmech", "KitMech", (0.78, 0.32, 0.12), 0.1, 0.55, textured=True)
    K.mat("kitelec", "KitElec", (0.18, 0.36, 0.7), 0.1, 0.5, textured=True)
    K.mat("kitrover", "KitRover", (0.56, 0.62, 0.2), 0.1, 0.55, textured=True)
    K.mat("kitenv", "KitEnv", (0.12, 0.52, 0.5), 0.1, 0.5, textured=True)
    K.mat("duranium", "Duranium", (0.5, 0.56, 0.64), 0.85, 0.35, textured=True)
    K.mat("tritanium", "Tritanium", (0.45, 0.36, 0.27), 0.85, 0.4, textured=True)
    K.mat("crate", "KitCrate", (0.8, 0.36, 0.12), 0.1, 0.55)      # the riding crates (own nodes, not baked)
    g = K.mat("shopglass", "ShopGlass", (0.45, 0.6, 0.7), 0.0, 0.04, emission=(0.5, 0.7, 0.9), strength=0.03, cull=False)
    g.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value = 0.16
    try:
        g.surface_render_method = "BLENDED"
    except AttributeError:
        g.blend_method = "BLEND"


def P2(xy, z=0.0):
    return P(xy[0], xy[1], z)


def box(name, x0, x1, y0, y1, z0, z1, m, g, bev=0.0):
    """An axis-aligned box by its extents."""
    return bx(name, (x1 - x0, y1 - y0, z1 - z0), P((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), m, g, bev)


def crate(name, c, size, m, g, lid=True):
    """A kit crate: body + a lighter lid band + corner straps (cheap)."""
    w, d, h = size
    bx(name, (w, d, h), c + P(0, 0, h / 2), m, g)
    if lid:
        bx(f"{name}Lid", (w + 0.02, d + 0.02, 0.05), c + P(0, 0, h - 0.04), MT["trim"], g)
        bx(f"{name}Tag", (w * 0.45, 0.012, h * 0.3), c + P(0, -d / 2 - 0.006, h * 0.5), MT["label"], g)


# ---------------------------------------------------------------- pieces


def pad(g):
    box("Pad", -PAD_X, PAD_X, -PAD_Y, PAD_Y, 0.0, SLAB, MT["concrete"], g)
    box("PadSkirt", -PAD_X - 0.15, PAD_X + 0.15, -PAD_Y - 0.15, PAD_Y + 0.15, 0.0, 0.12, MT["dark"], g)
    box("PadOwner", -PAD_X - 0.01, PAD_X + 0.01, -PAD_Y - 0.01, PAD_Y + 0.01, 0.13, 0.24, MT["owner"], g)
    # an inset hazard line round the pad
    z = SLAB + 0.004
    e, w = 0.32, 0.08
    box("PadLineF", -PAD_X + e, PAD_X - e, -PAD_Y + e, -PAD_Y + e + w, SLAB, z, MT["hazard"], g)
    box("PadLineB", -PAD_X + e, PAD_X - e, PAD_Y - e - w, PAD_Y - e, SLAB, z, MT["hazard"], g)
    box("PadLineL", -PAD_X + e, -PAD_X + e + w, -PAD_Y + e, PAD_Y - e, SLAB, z, MT["hazard"], g)
    box("PadLineR", PAD_X - e - w, PAD_X - e, -PAD_Y + e, PAD_Y - e, SLAB, z, MT["hazard"], g)
    # the apron: hazard sill at the door, a white vehicle box, the walk to the booth
    K.stripes("DoorSill", (DOOR[1] + 0.1, HY0 - 0.02), (DOOR[0] - 0.1, HY0 - 0.02), 0.5, z, g, pitch=0.42)
    qs = []
    x0, x1, y0, y1 = DOOR[0] + 0.15, DOOR[1] - 0.15, -PAD_Y + 0.55, HY0 - 0.7
    for a, b in (((x0, y0), (x0, y1)), ((x1, y0), (x1, y1)), ((x0, y0), (x1, y0))):
        if a[0] == b[0]:
            qs.append((P(a[0] - 0.05, a[1], z), P(a[0] + 0.05, a[1], z), P(b[0] + 0.05, b[1], z), P(b[0] - 0.05, b[1], z)))
        else:
            qs.append((P(a[0], a[1] - 0.05, z), P(b[0], b[1] - 0.05, z), P(b[0], b[1] + 0.05, z), P(a[0], a[1] + 0.05, z)))
    K.faces_obj("ApronBox", qs, MT["label"], g)
    K.flat_path("BoothWalk", [(1.45, -2.75), (3.3, -2.75)], 0.5, MT["panel"], z=SLAB + 0.003, parent=g)
    for k, (x, y) in enumerate([(-2.5, -3.9), (2.5, -2.5), (4.2, 2.8)]):
        bx(f"Drain{k}", (0.7, 0.3, 0.02), P(x, y, SLAB + 0.008), MT["grate"], g)
    for k, (x, y, yaw) in enumerate([(-6.15, 0.4, 90), (-6.15, 3.0, 90), (-2.4, 5.35, 0), (1.6, 5.35, 0), (6.15, -0.6, -90)]):
        e_ = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e_.rotation_euler.z = math.radians(yaw)


def frame(g):
    """Plinth, the hall floor with its lane lines, the steel frame and the roof beams (seen through the lantern),
    hanging lamp strips."""
    box("Plinth", HX0 - 0.12, HX1 + 0.12, HY0 - 0.12, HY1 + 0.12, SLAB, F - 0.02, MT["trim"], g)
    box("HallFloor", HX0 + 0.05, HX1 - 0.05, HY0 + 0.05, HY1 - 0.05, F - 0.03, F, MT["floor"], g)
    z = F + 0.004
    # yellow lane lines: round the work cell and a walk from the door
    qs = []

    def line(a, b, w=0.07):
        a, b = P(a[0], a[1], z), P(b[0], b[1], z)
        d = (b - a).normalized()
        n = P(-d.y, d.x, 0) * (w / 2)
        qs.append((a - n, b - n, b + n, a + n))

    cx0, cx1, cy0, cy1 = -4.7, -0.3, 0.1, 2.95
    for a, b in (((cx0, cy0), (cx1, cy0)), ((cx1, cy0), (cx1, cy1)), ((cx1, cy1), (cx0, cy1))):
        line(a, b)
    for x in (DOOR[0] + 0.5, DOOR[1] - 0.5):
        line((x, HY0 + 0.25), (x, cy0 - 0.15))
    line((0.35, HY0 + 0.3), (0.35, 3.1))
    line((1.6, HY0 + 0.3), (1.6, 3.1))
    K.faces_obj("LaneLines", qs, MT["hazard"], g)
    # columns at the corners and along the long walls, a portal beam on each, roof beams across
    for x in (HX0 + 0.18, -2.55, -0.55, HX1 - 0.18):
        for y in (HY0 + 0.32, HY1 - 0.18):
            if x in (-2.55,) and y < 0:
                continue  # over the door
            box(f"Column{x:.1f}_{y:.1f}", x - 0.11, x + 0.11, y - 0.11, y + 0.11, F, WT - 0.05, MT["trim"], g)
    for k, x in enumerate((HX0 + 0.18, -4.4, -2.55, -0.7, HX1 - 0.18)):
        box(f"RoofBeam{k}", x - 0.08, x + 0.08, HY0 + 0.2, HY1 - 0.2, WT - 0.42, WT - 0.02, MT["trim"], g)
        box(f"RoofBeamFlange{k}", x - 0.14, x + 0.14, HY0 + 0.2, HY1 - 0.2, WT - 0.46, WT - 0.42, MT["trim"], g)
    for k, y in enumerate((-0.95, 1.25, 3.45)):
        box(f"Purlin{k}", HX0 + 0.2, HX1 - 0.2, y - 0.07, y + 0.07, WT - 0.3, WT - 0.02, MT["steel"], g)
    # hanging lamp strips over the cell and the press (they glow up through the lantern at night)
    for k, (x0, x1, y) in enumerate([(-4.2, -0.9, 0.4), (-4.2, -0.9, 2.6), (0.2, 1.8, 1.4)]):
        z0 = F + 4.15
        box(f"LampHousing{k}", x0, x1, y - 0.09, y + 0.09, z0, z0 + 0.1, MT["dark"], g)
        box(f"LampStrip{k}", x0 + 0.05, x1 - 0.05, y - 0.06, y + 0.06, z0 - 0.02, z0, MT["lamp"], g).visible_shadow = False
        for x in (x0 + 0.2, x1 - 0.2):
            zcyl(f"LampRod{k}_{x:.1f}", 0.015, z0 + 0.1, WT - 0.3, MT["steel"], g, xy=(x, y), segs=4)
    K.point("ShopLight", P(-1.4, 1.3, F + 3.6), g, 0.4)


def robot(k, x, y, yaw, g):
    zcyl(f"PedFoot{k}", 0.46, F, F + 0.08, MT["dark"], g, xy=(x, y), segs=16)
    zcyl(f"Ped{k}", 0.3, F + 0.08, PED_Z - 0.04, MT["trim"], g, xy=(x, y), segs=16)
    zcyl(f"PedBand{k}", 0.305, F + 0.26, F + 0.34, MT["hazard"], g, xy=(x, y), segs=16)
    for j in range(4):
        a = math.radians(45 + 90 * j)
        zcyl(f"PedBolt{k}{j}", 0.035, F + 0.08, F + 0.13, MT["steel"], g, xy=(x + 0.38 * math.cos(a), y + 0.38 * math.sin(a)), segs=6)
    arm = empty(f"Arm{k}", P(x, y, PED_Z), g)
    arm.rotation_euler.z = math.radians(yaw)
    zcyl(f"Arm{k}Turret", 0.34, -0.04, 0.16, MT["trim"], arm, segs=18)
    bx(f"Arm{k}Base", (0.5, 0.6, 0.36), (0, -0.05, 0.34), MT["robot"], arm)
    K.cylinder(f"Arm{k}Shoulder", 0.21, 0.66, (0, 0, 0.5), MT["trim"], arm, axis="X", segments=16)
    for s in (-1, 1):
        K.cylinder(f"Arm{k}ShoulderCap{s}", 0.13, 0.04, (s * 0.35, 0, 0.5), MT["owner"], arm, axis="X", segments=12)
    bx(f"Arm{k}Motor", (0.2, 0.32, 0.3), (-0.42, -0.04, 0.42), MT["dark"], arm)
    sh = P(0, 0, 0.5)
    E = P(*ELBOW)
    obox(f"Arm{k}Upper", (0.28, (E - sh).length + 0.12, 0.34), (sh + E) / 2, E - sh, MT["robot"], arm)
    obox(f"Arm{k}UpperOwner", (0.29, 0.22, 0.35), sh.lerp(E, 0.6), E - sh, MT["owner"], arm)
    obox(f"Arm{k}UpperRib", (0.3, 0.05, 0.36), sh.lerp(E, 0.3), E - sh, MT["trim"], arm)
    K.sagging(f"Arm{k}Hose", P(0.22, -0.25, 0.4), E + P(0.2, -0.05, 0), 0.1, 0.028, MT["cable"], arm, n=5, segs=5)
    j = empty(f"Arm{k}Joint", E, arm)
    K.cylinder(f"Arm{k}Elbow", 0.18, 0.42, (0, 0, 0), MT["trim"], j, axis="X", segments=16)
    K.cylinder(f"Arm{k}ElbowMotor", 0.12, 0.2, (-0.3, 0, 0), MT["dark"], j, axis="X", segments=12)
    W = P(*WRIST)
    T = P(*TIP)
    obox(f"Arm{k}Fore", (0.2, W.length + 0.1, 0.24), W / 2, W, MT["robot"], j)
    obox(f"Arm{k}ForeBand", (0.21, 0.14, 0.25), W * 0.72, W, MT["dark"], j)
    K.cylinder(f"Arm{k}Wrist", 0.12, 0.26, W, MT["dark"], j, axis="X", segments=12)
    if k == 0:
        # a MIG torch: neck, cup, glowing tip
        K.strut(f"Arm{k}Torch", W, T + P(0, 0, 0.14), 0.055, MT["steel"], j, 8)
        K.strut(f"Arm{k}Cup", T + P(0, 0, 0.14), T + P(0, 0, 0.02), 0.045, MT["copper"], j, 8)
    else:
        # a spot-weld gun: body and two electrodes
        obox(f"Arm{k}Gun", (0.16, 0.22, 0.3), (W + T) / 2 + P(0, 0, 0.12), T - W, MT["trim"], j)
        K.strut(f"Arm{k}Electrode", W + (T - W) * 0.6, T + P(0, 0, 0.02), 0.03, MT["copper"], j, 6)
    sphere(f"Arm{k}Tip", 0.035, T, MT["weld"], j, 6)
    K.point(f"SparkPoint{k}", T, j, 0.2)


def work_cell(g):
    c = P2(CELL)
    # fixture table and the structural frame being welded on it
    bx("Table", (1.7, 1.15, 0.12), c + P(0, 0, TABLE_Z - 0.06), MT["steel"], g)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bx(f"TableLeg{sx}{sy}", (0.12, 0.12, TABLE_Z - F - 0.12), c + P(sx * 0.72, sy * 0.45, (TABLE_Z - 0.12 + F) / 2 - c.z), MT["trim"], g)
    bx("TableRail", (1.5, 0.95, 0.06), c + P(0, 0, F + 0.25), MT["trim"], g)
    z = TABLE_Z
    for k, (dx, dy, w, d) in enumerate([(0, -0.38, 1.3, 0.12), (0, 0.38, 1.3, 0.12), (-0.6, 0, 0.12, 0.88), (0.6, 0, 0.12, 0.88), (0, 0, 0.1, 0.88)]):
        bx(f"Work{k}", (w, d, 0.14), c + P(dx, dy, z + 0.07), MT["duranium"], g)
    for k, (dx, dy) in enumerate([(-0.75, -0.38), (0.75, -0.38), (-0.75, 0.38), (0.75, 0.38)]):
        bx(f"Clamp{k}", (0.12, 0.2, 0.22), c + P(dx * 1.06, dy, z + 0.11), MT["hazard"], g)
    # safety fence: back, right and the two front wings (the middle front open toward the door)
    rz = F
    railing("FenceBack", [(-4.75, 2.95), (-0.3, 2.95)], rz, g, h=1.05, post=1.1)
    railing("FenceSide", [(-0.3, 2.95), (-0.3, 0.1)], rz, g, h=1.05, post=1.0)
    railing("FenceFrontL", [(-4.75, 0.1), (-3.55, 0.1)], rz, g, h=1.05, post=1.2)
    railing("FenceFrontR", [(-1.45, 0.1), (-0.3, 0.1)], rz, g, h=1.05, post=1.15)
    for k, x in enumerate((-3.55, -1.45)):
        zcyl(f"LightCurtain{k}", 0.05, F, F + 1.6, MT["dark"], g, xy=(x, 0.1), segs=6)
        zcyl(f"LightCurtainLens{k}", 0.055, F + 1.45, F + 1.55, MT["status"], g, xy=(x, 0.1), segs=6)
    for k, (x, y, yaw) in enumerate(ARMS):
        robot(k, x, y, yaw, g)
    # cable trench covers from the arms to the fence
    for k, (x, y, _) in enumerate(ARMS):
        bx(f"Trench{k}", (0.32, 1.4, 0.03), P(x, y + 1.05, F + 0.015), MT["grate"], g)
    K.point("CellLight", c + P(0, 0, F + 1.7), g, 0.3)


def press(g):
    x, y = PRESS
    c = P(x, y, 0)
    # bed, lower die, the plate being formed
    box("PressBase", x - 0.75, x + 0.75, y - 0.55, y + 0.55, F, F + 0.18, MT["dark"], g)
    box("PressBed", x - 0.65, x + 0.65, y - 0.48, y + 0.48, F + 0.18, F + 0.9, MT["accent"], g)
    box("PressDie", x - 0.5, x + 0.5, y - 0.38, y + 0.38, F + 0.9, F + 1.08, MT["steel"], g)
    box("PressPlate", x - 0.42, x + 0.42, y - 0.33, y + 0.33, F + 1.08, F + 1.12, MT["copper"], g)
    # side frames, crown, hydraulic cylinder, hoses
    for s in (-1, 1):
        box(f"PressSide{s}", x + s * 0.62, x + s * 0.86, y - 0.5, y + 0.5, F + 0.18, F + 3.3, MT["accent"], g)
        box(f"PressSideRib{s}", x + s * 0.86, x + s * 0.92, y - 0.12, y + 0.12, F + 0.3, F + 3.2, MT["trim"], g)
        box(f"PressGuide{s}", x + s * 0.56, x + s * 0.62, y - 0.3, y + 0.3, F + 1.2, F + 3.1, MT["steel"], g)
    box("PressCrown", x - 0.95, x + 0.95, y - 0.58, y + 0.58, F + 3.3, F + 3.85, MT["accent"], g)
    box("PressCrownOwner", x - 0.96, x + 0.96, y - 0.59, y + 0.59, F + 3.5, F + 3.62, MT["owner"], g)
    zcyl("PressCylinder", 0.33, F + 3.85, F + 4.45, MT["hull"], g, xy=(x, y), segs=18)
    zcyl("PressCylinderCap", 0.36, F + 4.45, F + 4.55, MT["trim"], g, xy=(x, y), segs=18)
    for s in (-1, 1):
        K.sagging(f"PressHose{s}", P(x + s * 0.3, y + 0.2, F + 4.3), P(x + s * 0.9, y + 0.55, F + 2.2), 0.25, 0.04, MT["rubber"], g, n=5, segs=6)
    # control pendant and the light-curtain posts at the front
    zcyl("PendantPost", 0.04, F, F + 1.3, MT["trim"], g, xy=(x - 0.95, y - 0.75), segs=6)
    bx("Pendant", (0.22, 0.12, 0.32), P(x - 0.95, y - 0.75, F + 1.35), MT["hazard"], g)
    bx("PendantScreen", (0.16, 0.012, 0.12), P(x - 0.95, y - 0.815, F + 1.42), MT["status"], g).visible_shadow = False
    # the moving ram + upper die (rest = top of stroke)
    ram = empty("Press", P(x, y, PRESS_Z), g)
    box("PressPlaten", -0.55, 0.55, -0.4, 0.4, -0.3, 0.0, MT["steel"], ram)
    box("PressPlatenBand", -0.56, 0.56, -0.41, 0.41, -0.12, -0.04, MT["hazard"], ram)
    box("PressUpperDie", -0.45, 0.45, -0.32, 0.32, -0.42, -0.3, MT["trim"], ram)
    zcyl("PressRam", 0.15, 0.0, 0.98, MT["steel"], ram, segs=14)
    K.point("PressLow", P(x, y, PRESS_Z - PRESS_STROKE), g, 0.2)


def conveyor(g):
    A, B = BELT_A, BELT_B
    u = (B - A).normalized()
    L = (B - A).length
    s = P(-u.y, u.x, 0)
    bm = K.bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    prev = None
    for i in range(13):
        p = A + u * (L * i / 12) + P(0, 0, 0.02)
        row = (bm.verts.new(p - s * 0.3), bm.verts.new(p + s * 0.3))
        if prev:
            f = bm.faces.new((prev[1], prev[0], row[0], row[1]))  # wound so the belt faces up
            for loop in f.loops:
                v = K.Vector(loop.vert.co) - A
                loop[uv].uv = (0.5 + v.dot(s) / 0.6, v.dot(u) / 0.6)
        prev = row
    K.mesh_object("Conveyor", bm, MT["belt"], g)
    for side in (-1, 1):
        obox(f"Stringer{side}", (0.08, L + 0.3, 0.24), A + u * (L / 2) + s * (side * 0.37) - P(0, 0, 0.1), u, MT["trim"], g)
        obox(f"Guide{side}", (0.03, L, 0.12), A + u * (L / 2) + s * (side * 0.33) + P(0, 0, 0.09), u, MT["steel"], g)
    for end, p in (("A", A), ("B", B)):
        K.cylinder(f"Roller{end}", 0.11, 0.66, p - P(0, 0, 0.09), MT["steel"], g, axis="X", segments=12)
    for k, y in enumerate((1.45, 0.2, -1.05, -2.2, -3.0)):
        z0 = F if y > HY0 else SLAB
        for side in (-1, 1):
            bx(f"BeltLeg{k}{side}", (0.07, 0.07, BELT_Z - 0.2 - z0), P(A.x + side * 0.34, y, (BELT_Z - 0.2 + z0) / 2), MT["trim"], g)
        bx(f"BeltCross{k}", (0.7, 0.06, 0.06), P(A.x, y, z0 + 0.25), MT["trim"], g)
    bx("BeltMotor", (0.3, 0.36, 0.3), B + s * 0.55 - P(0, 0.05, 0.12), MT["dark"], g)
    K.cylinder("BeltMotorCan", 0.12, 0.28, B + s * 0.55 + P(0.0, 0.25, -0.12), MT["accent"], g, axis="Y", segments=10)
    # the strip curtain in the port
    for j in range(5):
        bx(f"Curtain{j}", (0.16, 0.02, 0.2), P(PORT[0] + 0.13 + j * 0.16, HY0 + 0.1, PORT[2] - 0.1), MT["rubber"], g)
    # kit crates riding the belt (own nodes, the view moves them)
    flow = empty("CrateFlow", A + P(0, 0, 0.02), g)
    K.point("CrateEnd", B - A, flow, 0.2)
    for k in range(4):
        o = bx(f"Crate{k}", (0.42, 0.42, 0.3), u * (L * (k + 0.5) / 4) + P(0, 0, 0.15), MT["crate"], flow)
        o.rotation_euler.z = math.radians((k * 37) % 9 - 4)


def walls(g):
    yf0, yf1 = HY0, HY0 + WALL
    z0, z1 = F, WT
    hull = MT["hull"]
    box("WallBack", HX0, HX1, HY1 - WALL, HY1, z0, z1, hull, g)
    box("WallLeft", HX0, HX0 + WALL, yf1, HY1 - WALL, z0, z1, hull, g)
    box("WallRight", HX1 - WALL, HX1, yf1, HY1 - WALL, z0, z1, hull, g)
    box("FrontL", HX0, DOOR[0], yf0, yf1, z0, z1, hull, g)
    box("FrontOverDoor", DOOR[0], DOOR[1], yf0, yf1, F + DOOR_H, z1, hull, g)
    box("FrontPier", DOOR[1], WIN[0], yf0, yf1, z0, z1, hull, g)
    box("FrontUnderL", WIN[0], PORT[0], yf0, yf1, z0, WIN[2], hull, g)
    box("FrontUnderR", PORT[1], WIN[1], yf0, yf1, z0, WIN[2], hull, g)
    box("FrontOverPort", PORT[0], PORT[1], yf0, yf1, PORT[2], WIN[2], hull, g)
    box("FrontOverWin", WIN[0], WIN[1], yf0, yf1, WIN[3], z1, hull, g)
    box("FrontR", WIN[1], HX1, yf0, yf1, z0, z1, hull, g)
    # plinth accent band and the Owner eaves band round the hall (proud of the walls)
    o = 0.025
    for k, (x0, x1) in enumerate([(HX0, DOOR[0]), (DOOR[1], PORT[0]), (PORT[1], HX1)]):
        box(f"AccentFront{k}", x0 - o, x1 + (o if k == 2 else 0), yf0 - o, yf0, z0, z0 + 0.5, MT["accent"], g)
    box("AccentBack", HX0 - o, HX1 + o, HY1, HY1 + o, z0, z0 + 0.5, MT["accent"], g)
    box("AccentLeft", HX0 - o, HX0, yf0, HY1, z0, z0 + 0.5, MT["accent"], g)
    box("AccentRight", HX1, HX1 + o, yf0, HY1, z0, z0 + 0.5, MT["accent"], g)
    ez0, ez1 = WT - 0.3, WT - 0.12
    box("OwnerEavesF", HX0 - o, HX1 + o, HY0 - o, HY0, ez0, ez1, MT["owner"], g)
    box("OwnerEavesB", HX0 - o, HX1 + o, HY1, HY1 + o, ez0, ez1, MT["owner"], g)
    box("OwnerEavesL", HX0 - o, HX0, HY0, HY1, ez0, ez1, MT["owner"], g)
    box("OwnerEavesR", HX1, HX1 + o, HY0, HY1, ez0, ez1, MT["owner"], g)
    # corner posts and wall ribs
    for k, (x, y) in enumerate([(HX0, HY0), (HX1, HY0), (HX0, HY1), (HX1, HY1)]):
        box(f"Corner{k}", x - 0.08, x + 0.08, y - 0.08, y + 0.08, SLAB, WT + 0.05, MT["trim"], g)
    for k in range(5):
        x = HX0 + 0.9 + k * 1.35
        box(f"RibBack{k}", x - 0.06, x + 0.06, HY1, HY1 + 0.07, z0 + 0.5, WT - 0.3, MT["panel"], g)
    for k in range(4):
        y = HY0 + 0.9 + k * 1.2
        box(f"RibRight{k}", HX1, HX1 + 0.07, y - 0.06, y + 0.06, z0 + 0.5, WT - 0.3, MT["panel"], g)
        box(f"RibLeft{k}", HX0 - 0.07, HX0, y - 0.06, y + 0.06, z0 + 0.5, WT - 0.3, MT["panel"], g)
    # the window band: frame, mullions, transom, see-through glass
    box("WinSill", WIN[0] - 0.05, WIN[1] + 0.05, yf0 - 0.08, yf1, WIN[2] - 0.08, WIN[2], MT["trim"], g)
    box("WinHead", WIN[0] - 0.05, WIN[1] + 0.05, yf0 - 0.05, yf1, WIN[3], WIN[3] + 0.08, MT["trim"], g)
    n = 4
    for k in range(n + 1):
        x = WIN[0] + (WIN[1] - WIN[0]) * k / n
        box(f"Mullion{k}", x - 0.035, x + 0.035, yf0 - 0.04, yf1, WIN[2], WIN[3], MT["trim"], g)
    box("Transom", WIN[0], WIN[1], yf0 - 0.03, yf1, (WIN[2] + WIN[3]) / 2 - 0.03, (WIN[2] + WIN[3]) / 2 + 0.03, MT["trim"], g)
    gy = HY0 + WALL / 2
    K.faces_obj("GlassFront", [(P(WIN[0], gy, WIN[2]), P(WIN[1], gy, WIN[2]), P(WIN[1], gy, WIN[3]), P(WIN[0], gy, WIN[3]))], MT["shopglass"], g)
    box("SignHallPlate", WIN[0] + 0.05, WIN[1] - 0.05, yf0 - 0.05, yf0, WIN[3] + 0.18, WIN[3] + 0.5, MT["dark"], g)
    K.marker("SignHall", P((WIN[0] + WIN[1]) / 2, yf0 - 0.06, WIN[3] + 0.34), (math.radians(90), 0, 0), 2.0, 0.22, g)
    # the conveyor port: a hazard frame round the opening
    for k, (x0, x1, za, zb) in enumerate([(PORT[0] - 0.08, PORT[0], z0, PORT[2] + 0.08), (PORT[1], PORT[1] + 0.08, z0, PORT[2] + 0.08),
                                          (PORT[0], PORT[1], PORT[2], PORT[2] + 0.08)]):
        box(f"PortFrame{k}", x0, x1, yf0 - 0.05, yf0, za, zb, MT["hazard"], g)
    # the roll-up door: guides, the drum hood, slats hanging from the pivot at the top of the opening
    for k, x in enumerate((DOOR[0] - 0.07, DOOR[1] + 0.07)):
        box(f"DoorGuide{k}", x - 0.07, x + 0.07, yf0 - 0.14, yf0, z0, F + DOOR_H + 0.05, MT["trim"], g)
    box("DoorHood", DOOR[0] - 0.2, DOOR[1] + 0.2, yf0 - 0.42, yf0, F + DOOR_H, F + DOOR_H + 0.3, MT["trim"], g)
    box("DoorHoodBand", DOOR[0] - 0.21, DOOR[1] + 0.21, yf0 - 0.43, yf0 - 0.4, F + DOOR_H + 0.08, F + DOOR_H + 0.2, MT["hazard"], g)
    door = empty("Door", P(DOOR_CX, yf0 - 0.07, F + DOOR_H), g)
    w = DOOR[1] - DOOR[0] - 0.02
    slats = 13
    sh = DOOR_H / slats
    for k in range(slats):
        zc = -(k + 0.5) * sh
        m = MT["hazard"] if k == slats - 1 else MT["panel"]
        bx(f"DoorSlat{k}", (w, 0.05, sh - 0.025), (0, 0, zc), m, door)
        bx(f"DoorGroove{k}", (w, 0.06, 0.025), (0, 0, zc - sh / 2), MT["trim"], door)
    bx("DoorWindows", (w * 0.8, 0.06, sh * 0.55), (0, -0.005, -(4.5) * sh), MT["glass"], door)
    bx("DoorBottom", (w, 0.08, 0.06), (0, 0, -DOOR_H + 0.03), MT["rubber"], door)
    # jamb hazard stripes, status lamp column, wall lamps
    K.wall_stripes("JambL", (DOOR[0] - 0.55, yf0 - 0.003), (DOOR[0] - 0.16, yf0 - 0.003), z0, z0 + 1.3, g, pitch=0.32)
    K.wall_stripes("JambR", (DOOR[1] + 0.16, yf0 - 0.003), (DOOR[1] + 0.55, yf0 - 0.003), z0, z0 + 1.3, g, pitch=0.32)
    bx("StatusColumn", (0.12, 0.06, 0.62), P(DOOR[1] + 0.22, yf0 - 0.03, F + 2.9), MT["dark"], g)
    for k in range(3):
        bx(f"StatusLamp{k}", (0.08, 0.03, 0.14), P(DOOR[1] + 0.22, yf0 - 0.065, F + 2.7 + k * 0.19), MT["status"], g).visible_shadow = False
    for k, x in enumerate((DOOR[0] + 0.4, DOOR[1] - 0.4)):
        bx(f"WallLamp{k}", (0.36, 0.2, 0.1), P(x, yf0 - 0.52, F + DOOR_H + 0.25), MT["dark"], g)
        bx(f"WallLampLens{k}", (0.3, 0.14, 0.02), P(x, yf0 - 0.52, F + DOOR_H + 0.195), MT["lamp"], g).visible_shadow = False
    # fascia + the main sign above the door
    box("Fascia", DOOR[0] - 0.1, DOOR[1] + 0.1, yf0 - 0.06, yf0, F + 4.24, F + 4.58, MT["dark"], g)
    K.marker("SignFront", P(DOOR_CX, yf0 - 0.07, F + 4.41), (math.radians(90), 0, 0), 3.4, 0.27, g)
    # lit windows on the side walls, a personnel door and a louvre on the right, a vent on the back
    K.pane_row("WinRight", (HX1 + 0.012, 1.0, 0), (0, 1, 0), None, 2.7, F + 2.5, F + 3.2, 3, MT["window"], g, frac=0.8)
    K.pane_row("WinLeft", (HX0 - 0.012, 3.6, 0), (0, -1, 0), None, 3.6, F + 2.5, F + 3.2, 4, MT["window"], g, frac=0.8)
    box("SideDoor", HX1, HX1 + 0.05, 0.05, 0.95, F, F + 2.05, MT["trim"], g)
    box("SideDoorLamp", HX1, HX1 + 0.07, 0.3, 0.7, F + 2.15, F + 2.22, MT["window"], g).visible_shadow = False
    box("BackLouvre", -3.6, -1.6, HY1, HY1 + 0.06, F + 2.6, F + 3.6, MT["dark"], g)
    for k in range(5):
        bx(f"BackLouvreSlat{k}", (2.0, 0.1, 0.04), P(-2.6, HY1 + 0.07, F + 2.7 + k * 0.2), MT["steel"], g, rot=(math.radians(30), 0, 0))


def roof(g):
    sx0, sx1, sy0, sy1 = SKY
    o = 0.1
    deck = MT["panel"]
    box("RoofFront", HX0 - o, HX1 + o, HY0 - o, sy0, WT, RT, deck, g)
    box("RoofBack", HX0 - o, HX1 + o, sy1, HY1 + o, WT, RT, deck, g)
    box("RoofLeft", HX0 - o, sx0, sy0, sy1, WT, RT, deck, g)
    box("RoofRight", sx1, HX1 + o, sy0, sy1, WT, RT, deck, g)
    pz = RT + 0.28
    box("ParapetF", HX0 - o, HX1 + o, HY0 - o, HY0 - o + 0.12, RT, pz, MT["trim"], g)
    box("ParapetB", HX0 - o, HX1 + o, HY1 + o - 0.12, HY1 + o, RT, pz, MT["trim"], g)
    box("ParapetL", HX0 - o, HX0 - o + 0.12, HY0 - o, HY1 + o, RT, pz, MT["trim"], g)
    box("ParapetR", HX1 + o - 0.12, HX1 + o, HY0 - o, HY1 + o, RT, pz, MT["trim"], g)
    # the lantern over the work cell: curb, ridge and eave beams, mullions, see-through glazing
    t = 0.14
    box("CurbF", sx0 - t, sx1 + t, sy0 - t, sy0, RT, CURB, MT["trim"], g)
    box("CurbB", sx0 - t, sx1 + t, sy1, sy1 + t, RT, CURB, MT["trim"], g)
    box("CurbL", sx0 - t, sx0, sy0, sy1, RT, CURB, MT["trim"], g)
    box("CurbR", sx1, sx1 + t, sy0, sy1, RT, CURB, MT["trim"], g)
    ym = (sy0 + sy1) / 2
    beam("Ridge", P(sx0 - 0.05, ym, RIDGE), P(sx1 + 0.05, ym, RIDGE), 0.12, MT["trim"], g)
    n = 6
    for k in range(n + 1):
        x = sx0 + (sx1 - sx0) * k / n
        beam(f"MullionF{k}", P(x, sy0, CURB), P(x, ym, RIDGE), 0.06, MT["trim"], g)
        beam(f"MullionB{k}", P(x, sy1, CURB), P(x, ym, RIDGE), 0.06, MT["trim"], g)
    for k in (-1, 1):
        a = P(sx0, ym + k * (sy1 - sy0) / 4, (CURB + RIDGE) / 2)
        b = P(sx1, a.y, a.z)
        beam(f"LanternRail{k}", a, b, 0.04, MT["trim"], g)
    quads = [(P(sx0, sy0, CURB), P(sx1, sy0, CURB), P(sx1, ym, RIDGE), P(sx0, ym, RIDGE)),
             (P(sx1, sy1, CURB), P(sx0, sy1, CURB), P(sx0, ym, RIDGE), P(sx1, ym, RIDGE)),
             (P(sx0, sy1, CURB), P(sx0, sy0, CURB), P(sx0, ym, RIDGE)),
             (P(sx1, sy0, CURB), P(sx1, sy1, CURB), P(sx1, ym, RIDGE))]
    K.faces_obj("GlassRoof", quads, MT["shopglass"], g)
    # exhaust fans on the right deck
    for k, (x, y) in enumerate([(0.55, 0.35), (0.55, 2.4)]):
        z = RT
        lathe(f"FanHousing{k}", [(0.66, z), (0.8, z), (0.8, z + 0.42), (0.72, z + 0.48), (0.66, z + 0.42), (0.66, z)], MT["hull"], g, (x, y, 0), 20, smooth=False)
        zcyl(f"FanWell{k}", 0.67, z - 0.01, z + 0.05, MT["dark"], g, xy=(x, y), segs=20)
        K.band(f"FanOwner{k}", 0.805, z + 0.12, z + 0.24, MT["owner"], g, xy=(x, y), segs=20)
        zcyl(f"FanMotor{k}", 0.16, z + 0.05, z + 0.2, MT["dark"], g, xy=(x, y), segs=10)
        for j in range(2):
            a = math.radians(45 + 90 * j)
            obox(f"FanGuard{k}{j}", (0.04, 1.42, 0.04), P(x, y, z + 0.47), P(math.cos(a), math.sin(a), 0), MT["steel"], g)
        fan = empty(f"Fan{k}", P(x, y, z + 0.3), g)
        zcyl(f"Fan{k}Hub", 0.13, -0.07, 0.08, MT["trim"], fan, segs=10)
        for j in range(6):
            a = math.radians(60 * j)
            bx(f"Fan{k}Blade{j}", (0.5, 0.16, 0.02), (0.35 * math.cos(a), 0.35 * math.sin(a), 0.0), MT["steel"], fan, rot=(math.radians(25), 0, a))
    # mushroom vents, an extract duct, the AC unit
    for k, (x, y) in enumerate([(1.65, 0.0), (1.65, 1.35), (-4.75, 0.2), (-4.75, 2.6)]):
        zcyl(f"Vent{k}", 0.12, RT, RT + 0.55, MT["steel"], g, xy=(x, y), segs=8)
        zcyl(f"VentCap{k}", 0.24, RT + 0.55, RT + 0.68, MT["trim"], g, xy=(x, y), segs=10, radius2=0.08)
    box("RoofAc", 1.25, 2.05, 3.0, 3.7, RT, RT + 0.5, MT["panel"], g)
    box("RoofAcGrille", 1.35, 1.95, 2.98, 3.0, RT + 0.08, RT + 0.42, MT["grate"], g)
    zcyl("RoofAcFan", 0.22, RT + 0.5, RT + 0.54, MT["dark"], g, xy=(1.65, 3.35), segs=12)
    box("RoofWalk", -0.5, 2.0, -0.75, -0.35, RT, RT + 0.03, MT["grate"], g)
    K.point("RoofSpot0", P(1.55, -0.55, RT), g)


def output_rack(g):
    x0, x1, y0, y1 = RACK
    xm = (x0 + x1) / 2
    levels = [SLAB, SLAB + 1.15, SLAB + 2.3]
    top = SLAB + 2.65
    for x in (x0, xm, x1):
        for y in (y0, y1):
            box(f"RackUpright{x:.2f}{y:.2f}", x - 0.05, x + 0.05, y - 0.05, y + 0.05, SLAB, top, MT["accent"], g)
            box(f"RackFoot{x:.2f}{y:.2f}", x - 0.09, x + 0.09, y - 0.09, y + 0.09, SLAB, SLAB + 0.04, MT["trim"], g)
        for z0 in (SLAB + 0.5, SLAB + 1.6):
            beam(f"RackBrace{x:.2f}{z0:.1f}", P(x, y0, z0), P(x, y1, z0 + 0.7), 0.04, MT["accent"], g)
    for k, z in enumerate(levels[1:]):
        for y in (y0, y1):
            box(f"RackBeam{k}{y:.1f}", x0, x1, y - 0.04, y + 0.04, z - 0.12, z, MT["hazard"], g)
    kits = [MT[kk] for kk in KIT_KEYS]
    fill = [[1, 1, 1, 1], [1, 1, 0, 1], [1, 0, 1, 1], [1, 1, 1, 0], [1, 0, 0, 1], [0, 1, 1, 0]]
    bay = 0
    for b, (bx0, bx1) in enumerate([(x0, xm), (xm, x1)]):
        for lv, z in enumerate(levels):
            c = P((bx0 + bx1) / 2, (y0 + y1) / 2, z)
            bx(f"Pallet{b}{lv}", (0.82, 0.8, 0.12), c + P(0, 0, 0.06), MT["pallet"], g)
            for q, (dx, dy) in enumerate([(-0.2, -0.19), (0.2, -0.19), (-0.2, 0.19), (0.2, 0.19)]):
                if not fill[bay % len(fill)][q]:
                    continue
                crate(f"RackCrate{b}{lv}{q}", c + P(dx, dy, 0.12), (0.37, 0.35, 0.3), kits[(b * 3 + lv + q // 2) % 4], g, lid=q < 2)
                if lv == 0 and q < 2:
                    crate(f"RackCrateUp{b}{q}", c + P(dx, dy, 0.42), (0.35, 0.33, 0.28), kits[(b + q + 2) % 4], g, lid=True)
            bay += 1
    # the sign over the rack
    for x in (x0 + 0.25, x1 - 0.25):
        box(f"RackSignPost{x:.1f}", x - 0.03, x + 0.03, y0 - 0.03, y0 + 0.03, top, top + 0.42, MT["trim"], g)
    box("RackSign", x0 + 0.1, x1 - 0.1, y0 - 0.05, y0 - 0.01, top + 0.15, top + 0.48, MT["dark"], g)
    K.marker("LabelRack", P(xm, y0 - 0.055, top + 0.315), (math.radians(90), 0, 0), 1.6, 0.22, g)
    # a roller table at the end of the belt with a crate waiting for the forklift
    ex = BELT_B.x
    box("RollerFrameL", ex - 0.38, ex - 0.32, -4.1, BELT_B.y, BELT_Z - 0.2, BELT_Z - 0.02, MT["trim"], g)
    box("RollerFrameR", ex + 0.32, ex + 0.38, -4.1, BELT_B.y, BELT_Z - 0.2, BELT_Z - 0.02, MT["trim"], g)
    for k in range(5):
        K.cylinder(f"Roller{k}", 0.05, 0.62, (ex, BELT_B.y - 0.12 - k * 0.17, BELT_Z - 0.04), MT["steel"], g, axis="X", segments=8)
    for sx in (-1, 1):
        bx(f"RollerLeg{sx}", (0.06, 0.06, BELT_Z - 0.2 - SLAB), P(ex + sx * 0.35, -3.95, (BELT_Z - 0.2 + SLAB) / 2), MT["trim"], g)
    box("RollerStop", ex - 0.38, ex + 0.38, -4.16, -4.1, BELT_Z - 0.1, BELT_Z + 0.18, MT["hazard"], g)
    crate("EndCrate", P(ex, -3.8, BELT_Z + 0.01), (0.42, 0.42, 0.3), MT["kitelec"], g)
    # optional: a pallet of crates on the apron's left
    opt = empty("OptCrates", P(-4.55, -3.25, SLAB), g)
    bx("OptPallet", (0.95, 0.85, 0.12), (0, 0, 0.06), MT["pallet"], opt)
    for q, (dx, dy, dz) in enumerate([(-0.22, -0.2, 0.12), (0.22, -0.2, 0.12), (-0.22, 0.2, 0.12), (0.22, 0.2, 0.12), (0.0, 0.0, 0.42)]):
        crate(f"OptCrate{q}", P(dx, dy, dz), (0.4, 0.37, 0.3), MT[KIT_KEYS[(q + 1) % 4]], opt, lid=q != 4)


def parts_racks(g):
    # shelving on the back wall inside, with bins
    y0, y1 = 3.35, 3.88
    xs = [-4.75, -3.55, -2.35, -1.15]
    for x in xs:
        for y in (y0, y1):
            box(f"ShelfUpright{x:.1f}{y:.1f}", x - 0.035, x + 0.035, y - 0.035, y + 0.035, F, F + 3.2, MT["accent"], g)
    kits = [MT[kk] for kk in KIT_KEYS] + [MT["panel"], MT["duranium"]]
    for lv in range(4):
        z = F + 0.15 + lv * 0.95
        box(f"Shelf{lv}", xs[0], xs[-1], y0, y1, z, z + 0.04, MT["grate"], g)
        for b in range(3):
            for q in range(3):
                if (lv * 7 + b * 3 + q) % 5 == 4:
                    continue
                x = xs[b] + 0.22 + q * 0.38
                h = 0.22 + 0.08 * ((lv + q) % 2)
                bx(f"Bin{lv}{b}{q}", (0.32, 0.42, h), P(x, (y0 + y1) / 2, z + 0.04 + h / 2), kits[(lv + b * 2 + q) % len(kits)], g)
    # the kitting bench inside the window, with a parts tote and a tool board
    box("Bench", 1.45, 2.0, -0.9, 0.9, F + 0.85, F + 0.92, MT["steel"], g)
    for y in (-0.8, 0.8):
        box(f"BenchLeg{y:.1f}", 1.5, 1.95, y - 0.04, y + 0.04, F, F + 0.85, MT["trim"], g)
    box("BenchTote", 1.55, 1.9, -0.6, -0.15, F + 0.92, F + 1.12, MT["kitmech"], g)
    box("BenchTote2", 1.55, 1.9, 0.1, 0.5, F + 0.92, F + 1.08, MT["kitelec"], g)
    box("ToolBoard", 1.96, 2.0, -0.9, 0.9, F + 1.2, F + 2.2, MT["dark"], g)
    for k in range(6):
        box(f"Tool{k}", 1.93, 1.96, -0.7 + k * 0.27, -0.62 + k * 0.27, F + 1.4 + (k % 2) * 0.25, F + 1.9, MT["steel"], g)
    # optional: an outdoor cantilever rack of bar stock (duranium blue-grey, tritanium bronze)
    x0, x1, sy = STOCK
    opt = empty("OptRack", P(0, 0, 0), g)
    box("StockBase", x0, x1, sy - 0.12, sy + 0.12, SLAB, SLAB + 0.12, MT["trim"], opt)
    for x in (x0 + 0.15, (x0 + x1) / 2, x1 - 0.15):
        box(f"StockColumn{x:.1f}", x - 0.08, x + 0.08, sy - 0.1, sy + 0.1, SLAB, SLAB + 2.4, MT["accent"], opt)
        for lv in range(3):
            z = SLAB + 0.55 + lv * 0.7
            for s in (-1, 1):
                obox(f"StockArm{x:.1f}{lv}{s}", (0.07, 0.85, 0.1), P(x, sy + s * 0.45, z), P(0, 1, 0), MT["trim"], opt)
    for lv in range(3):
        z = SLAB + 0.6 + lv * 0.7
        for s in (-1, 1):
            for b in range(3 - (lv + (s > 0)) % 2):
                m = MT["duranium"] if (lv + b) % 2 == 0 else MT["tritanium"]
                y = sy + s * (0.25 + b * 0.2)
                obox(f"Bar{lv}{s}{b}", (0.12, x1 - x0 - 0.1, 0.12), P((x0 + x1) / 2 + 0.05 * (b - 1), y, z + 0.06), P(1, 0, 0), m, opt)


def hopper(g):
    x, y = HOPPER
    c = P(x, y, 0)
    h0, h1, h2 = SLAB + 1.9, SLAB + 2.8, SLAB + 3.35
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"HopperLeg{sx}{sy}", x + sx * 0.55 - 0.06, x + sx * 0.55 + 0.06, y + sy * 0.55 - 0.06, y + sy * 0.55 + 0.06, SLAB, h1, MT["trim"], g)
    for k, z in enumerate((SLAB + 0.7, SLAB + 1.6)):
        box(f"HopperTie{k}", x - 0.55, x + 0.55, y - 0.58, y - 0.52, z, z + 0.07, MT["trim"], g)
        box(f"HopperTieB{k}", x - 0.55, x + 0.55, y + 0.52, y + 0.58, z, z + 0.07, MT["trim"], g)
    o = lathe("HopperCone", [(0.2, h0), (0.95, h1), (0.0, h1)], MT["panel"], g, (x, y, 0), 4, smooth=False)
    o.rotation_euler.z = math.radians(45)
    box("HopperBin", x - 0.68, x + 0.68, y - 0.68, y + 0.68, h1, h2, MT["hull"], g)
    box("HopperRim", x - 0.72, x + 0.72, y - 0.72, y + 0.72, h2, h2 + 0.06, MT["trim"], g)
    box("HopperBand", x - 0.69, x + 0.69, y - 0.69, y + 0.69, h1 + 0.08, h1 + 0.2, MT["owner"], g)
    K.heap("HopperStock", 0.62, 0.32, c + P(0, 0, h2 - 0.18), MT["duranium"], g, seed=5, segs=10)
    bx("HopperPlate", (0.9, 0.04, 0.3), c + P(0, -0.7, h1 + 0.36), MT["label"], g)
    K.marker("LabelHopper", c + P(0, -0.725, h1 + 0.36), (math.radians(90), 0, 0), 0.85, 0.17, g)
    # outlet, screw feeder up into the hall wall, its motor
    zcyl("HopperOutlet", 0.16, h0 - 0.3, h0, MT["steel"], g, xy=(x, y), segs=10)
    a = P(x, y, h0 - 0.3)
    b = P(HX1 + 0.05, -0.65, F + 2.8)
    K.strut("Feeder", a, b, 0.15, MT["steel"], g, 12)
    for k in range(3):
        K.strut(f"FeederFlange{k}", a.lerp(b, 0.2 + k * 0.3) - (b - a).normalized() * 0.03, a.lerp(b, 0.2 + k * 0.3) + (b - a).normalized() * 0.03, 0.19, MT["trim"], g, 12)
    bx("FeederMotor", (0.32, 0.32, 0.36), a - P(0, 0, 0.25), MT["accent"], g)
    bx("FeederSupport", (0.08, 0.08, (a.z + b.z) / 2 - SLAB - 0.12), P((a.x + b.x) / 2, (a.y + b.y) / 2, ((a.z + b.z) / 2 - 0.12 + SLAB) / 2), MT["trim"], g)
    ladder("HopperLadder", P(x + 0.62, y + 0.2, SLAB), h2, (1, 0), g, cage=False)
    railing("HopperRail", [(x - 0.72, y - 0.72), (x + 0.72, y - 0.72), (x + 0.72, y + 0.72), (x - 0.72, y + 0.72)], h2 + 0.06, g, h=0.6, post=0.75, closed=True)


def booth(g):
    x0, x1, y0, y1 = BOOTH
    c = P((x0 + x1) / 2, (y0 + y1) / 2, 0)
    z0 = SLAB + 0.15
    zt = z0 + BOOTH_H
    box("BoothBase", x0 - 0.05, x1 + 0.05, y0 - 0.05, y1 + 0.05, SLAB, z0, MT["trim"], g)
    box("BoothBody", x0, x1, y0, y1, z0, zt, MT["hull"], g)
    box("BoothAccent", x0 - 0.01, x1 + 0.01, y0 - 0.01, y1 + 0.01, z0 + 0.2, z0 + 0.45, MT["accent"], g)
    box("BoothOwner", x0 - 0.01, x1 + 0.01, y0 - 0.01, y1 + 0.01, zt - 0.22, zt - 0.1, MT["owner"], g)
    # the front: a wide window over the status board
    box("BoothGlass", x0 + 0.12, x1 - 0.12, y0 - 0.02, y0, z0 + 1.45, z0 + 2.15, MT["glass"], g)
    K.pane_row("BoothWin", (x0 + 0.15, y0 - 0.025, 0), (1, 0, 0), None, x1 - x0 - 0.3, z0 + 1.55, z0 + 2.05, 2, MT["window"], g, frac=0.86)
    box("StatusFrame", x0 + 0.1, x1 - 0.1, y0 - 0.06, y0, z0 + 0.75, z0 + 1.3, MT["dark"], g)
    box("StatusBoard", x0 + 0.18, x1 - 0.18, y0 - 0.085, y0 - 0.06, z0 + 1.0, z0 + 1.2, MT["status"], g).visible_shadow = False
    for j in range(5):
        xx = x0 + 0.28 + j * (x1 - x0 - 0.56) / 4
        box(f"StatusLed{j}", xx - 0.07, xx + 0.07, y0 - 0.085, y0 - 0.06, z0 + 0.82, z0 + 0.92, MT["status"], g).visible_shadow = False
    # side door toward the walk, a lit lamp over it, the step
    box("BoothDoor", x0 - 0.04, x0, y0 + 0.55, y0 + 1.35, z0, z0 + 2.0, MT["trim"], g)
    box("BoothDoorLamp", x0 - 0.06, x0, y0 + 0.75, y0 + 1.15, z0 + 2.1, z0 + 2.17, MT["window"], g).visible_shadow = False
    box("BoothStep", x0 - 0.45, x0 - 0.05, y0 + 0.5, y0 + 1.4, SLAB, z0, MT["grate"], g)
    box("BoothSideWin", x1, x1 + 0.02, y0 + 0.4, y1 - 0.4, z0 + 1.5, z0 + 2.1, MT["window"], g).visible_shadow = False
    box("BoothRoof", x0 - 0.15, x1 + 0.15, y0 - 0.25, y1 + 0.15, zt, zt + 0.14, MT["panel"], g)
    box("BoothFascia", x0 - 0.15, x1 + 0.15, y0 - 0.28, y0 - 0.22, zt - 0.12, zt + 0.16, MT["dark"], g)
    opt = empty("OptAc", P(c.x + 0.25, c.y + 0.3, zt + 0.14), g)
    bx("BoothAc", (0.6, 0.55, 0.35), (0, 0, 0.175), MT["panel"], opt)
    bx("BoothAcGrille", (0.5, 0.02, 0.25), (0, -0.285, 0.18), MT["grate"], opt)
    zcyl("BoothAntenna", 0.03, zt + 0.14, zt + 1.4, MT["steel"], g, xy=(x1 - 0.2, y1 - 0.2), segs=6)
    K.point("RoofSpot1", P(c.x - 0.35, c.y - 0.4, zt + 0.14), g)


def utilities(g):
    # transformer with cooling fins, a cable tray up the right wall into the hall
    x, y = XFMR
    box("XfmrPlinth", x - 0.7, x + 0.7, y - 0.55, y + 0.55, SLAB, SLAB + 0.15, MT["concrete"], g)
    box("XfmrBody", x - 0.5, x + 0.5, y - 0.38, y + 0.38, SLAB + 0.15, SLAB + 1.45, MT["accent"], g)
    for k in range(6):
        xx = x - 0.45 + k * 0.18
        box(f"XfmrFin{k}", xx - 0.02, xx + 0.02, y - 0.5, y - 0.38, SLAB + 0.3, SLAB + 1.3, MT["trim"], g)
    for k in range(3):
        zcyl(f"XfmrBushing{k}", 0.06, SLAB + 1.45, SLAB + 1.85, MT["label"], g, xy=(x - 0.3 + k * 0.3, y + 0.1), segs=8)
    bx("XfmrPlate", (0.5, 0.02, 0.3), P(x, y - 0.39, SLAB + 1.1), MT["hazard"], g)
    tray = [P(x - 0.5, y, SLAB + 1.2), P(HX1 + 0.35, y, SLAB + 1.2), P(HX1 + 0.35, y, WT - 0.6), P(HX1 + 0.12, y, WT - 0.6)]
    for k, (a, b) in enumerate(zip(tray, tray[1:])):
        beam(f"Tray{k}", a, b, 0.32, MT["grate"], g, h=0.08)
    # compressor skid with an air receiver, pipe into the hall
    box("CompSkid", 2.35, 3.0, 2.85, 4.0, SLAB, SLAB + 0.1, MT["trim"], g)
    box("CompBody", 2.4, 2.95, 3.3, 3.95, SLAB + 0.1, SLAB + 0.85, MT["hull"], g)
    box("CompGrille", 2.95, 2.97, 3.4, 3.85, SLAB + 0.2, SLAB + 0.75, MT["grate"], g)
    K.cylinder("Receiver", 0.24, 0.55, (2.67, 3.02, SLAB + 0.36), MT["valve"], g, axis="X", segments=12)
    pipe("AirLine", [P(2.67, 3.02, SLAB + 0.62), P(2.67, 3.02, F + 1.6), P(HX1, 3.02, F + 1.6)], 0.045, MT["steel"], g)
    # an extract duct up the right wall to a cowl above the roof
    box("Duct", HX1 + 0.02, HX1 + 0.42, 2.2, 2.6, F + 1.2, RT + 0.9, MT["panel"], g)
    box("DuctCowl", HX1 - 0.02, HX1 + 0.48, 2.12, 2.68, RT + 0.9, RT + 1.12, MT["trim"], g)
    for k in range(3):
        box(f"DuctBand{k}", HX1, HX1 + 0.44, 2.18, 2.62, F + 1.6 + k * 1.3, F + 1.68 + k * 1.3, MT["trim"], g)
    # gas bottles in a cage by the booth (welding gas)
    for k in range(3):
        zcyl(f"Bottle{k}", 0.11, SLAB, SLAB + 1.2, MT["kitenv"] if k != 1 else MT["valve"], g, xy=(2.6 + k * 0.26, -1.95), segs=10)
        sphere(f"BottleTop{k}", 0.1, P(2.6 + k * 0.26, -1.95, SLAB + 1.2), MT["steel"], g, 8)
    box("BottleCage", 2.42, 3.3, -2.14, -2.1, SLAB, SLAB + 1.0, MT["grate"], g)


def lights(g):
    piv = empty("Masts", P(0, 0, SLAB), g)
    K.light_mast("MastA", -4.95, -4.15, 0.0, 5.6, 50, piv)
    # the beacon mast: a lattice tower with an antenna and the beacon on top
    mx, my = MAST
    top = 8.0
    K.truss("Tower", P(mx, my, 0.0), P(mx, my, top), 0.36, 0.05, MT["trim"], piv, bays=12)
    box("TowerFoot", mx - 0.32, mx + 0.32, my - 0.32, my + 0.32, 0.0, 0.18, MT["concrete"], piv)
    box("TowerCap", mx - 0.28, mx + 0.28, my - 0.28, my + 0.28, top, top + 0.08, MT["trim"], piv)
    zcyl("TowerWhip", 0.03, top + 0.08, top + 1.1, MT["steel"], piv, xy=(mx, my), segs=6)
    for k, z in enumerate((top - 1.2, top - 3.5)):
        bx(f"TowerPanel{k}", (0.12, 0.42, 0.7), P(mx - 0.25, my, z), MT["label"], piv)
    sphere("Beacon", 0.11, P(mx, my, top + 0.2), MT["beacon"], piv, 10)
    K.bollards("Bollard", [(DOOR[0] - 0.3, -2.0), (DOOR[1] + 0.3, -2.0), (0.4, -3.6)], SLAB, g)


BUILDERS = [pad, frame, work_cell, press, conveyor, walls, roof, output_rack, parts_racks, hopper, booth, utilities, lights]

PIVOTS = {"Arm0", "Arm0Joint", "Arm1", "Arm1Joint", "Press", "Door", "Fan0", "Fan1", "Masts", "OptRack", "OptCrates", "OptAc", "CrateFlow"}
SEPARATE = {"Conveyor", "Beacon", "GlassFront", "GlassRoof"} | {f"Crate{k}" for k in range(4)}
EMPTIES = {"SparkPoint0", "SparkPoint1", "PressLow", "CrateEnd", "ShopLight", "CellLight", "SignFront", "SignHall",
           "LabelHopper", "LabelRack", "RoofSpot0", "RoofSpot1"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("MACHINE SHOP 01", (0.92, 0.93, 0.95)), "SignHall": ("FABRICATION", (0.92, 0.93, 0.95)),
         "LabelHopper": ("FEED STOCK", (0.08, 0.08, 0.1)), "LabelRack": ("KITS OUT", (0.92, 0.93, 0.95))}


def on_shot(name):
    """Preview pose (after the export): door up, arms at work, the press part way down."""
    D = K.bpy.data.objects
    D["Door"].scale.z = 0.08
    for k, (_, _, yaw) in enumerate(ARMS):
        D[f"Arm{k}"].rotation_euler.z = math.radians(yaw + (18 if k == 0 else -24))
        D[f"Arm{k}Joint"].rotation_euler.x = math.radians(6 if k == 0 else -4)
    D["Press"].location.z = PRESS_Z - PRESS_STROKE * 0.6


def main():
    materials()
    root = K.assemble("machine_shop", PIECES, BUILDERS)
    night = [("NHall", (1.0, 0.9, 0.75), 260, P(-1.6, 1.4, F + 3.6)),
             ("NCell", (0.55, 0.75, 1.0), 90, P(CELL[0], CELL[1], F + 1.9)),
             ("NMastA", (1.0, 0.95, 0.85), 240, P(-4.4, -3.6, 5.6)),
             ("NDoor", (1.0, 0.85, 0.6), 70, P(DOOR_CX, -2.6, F + 4.0)),
             ("NBooth", (0.5, 1.0, 0.6), 50, P(4.4, -4.9, 1.6))]
    shots = {"hero": ((15.5, -19.0, 12.5), (-0.4, 0.2, 2.6), 32),
             "close": ((-0.6, -10.8, 6.4), (-2.2, 1.2, 1.4), 26),
             "night": ((-9.0, -19.0, 11.5), (-0.4, 0.2, 2.6), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 15),
             "back": ((-14.0, 17.0, 10.0), (0.0, 0.5, 2.8), 32)}
    K.finish(root, KEEP, SEPARATE, "MachineShopGrime", SLAB, soot=None, shots=shots, night_lights=night,
             signs=SIGNS, rover_at=(-8.8, -6.6, 150), on_shot=on_shot)


main()
