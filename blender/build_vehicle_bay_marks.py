"""Vehicle Bay Mk I-V mockups (`vehicle_bay`; owner 2026-11-06: full details and Mk I through Mk V).

One silhouette, five marks: a ribbed hall with a roll-up door facing the apron (front = -Y = Godot +Z), a control
cabin/tower with the turning beacon, work lights inside.
  Mk I   Prefab garage: bolted prefab panels, flat roof, one roll-up door, roof cabin with a beacon, one flood pole.
  Mk II  Assembly hall: taller and deeper, striped door portal, skylight + roof vents, parts annex with a wheel rack,
         two-storey control tower with a radar, light mast.
  Mk III Motor pool: clerestory roof with windows, Starfleet white + navy, three-storey tower with a dish, a charging
         canopy on the apron, solar roof on the annex, apron lamps.
  Mk IV  Twin line: the hall widens to two assembly lines under twin barrel roofs, two doors ("Door", "Door1"), roof
         tower with sensor array, HVAC and solar on the roof, lit lane stripes, two masts. Builds two at once.
  Mk V   Starfleet vehicle yard: one sweeping canopy over both lines, a glass control bridge above the doors, a
         spire with a dish and the beacon, an industrial transporter pad for parts, blue light strips. Unlocks the
         Argo.
Everything stays inside the lanes' footprint_radius 6.5 (chamfered corners at Mk IV-V) except the flat apron; the
access spot (Godot [0, 9] = Blender (0, -9)) stays in front of the door.

GAME PARTS (as BuildingModels.VehicleBay / PlanetView.AnimateBay use them): "Door" pivot at the top of the opening,
the panel hanging down (scale Godot Y 1 = shut, 0.08 = open); Mk IV-V also "Door1" (the second line; needs code).
"WorkGlow" (shown while working), "Spin" (turning beacon/radar, about Godot Y). Markers SignFront, SparkPoint.

Run: /home/user/.bpyvenv/bin/python blender/build_vehicle_bay_marks.py --preview blender/previews/marks/vehicle_bay
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
import marks_kit as MK  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, strut, empty, cylinder  # noqa: E402

SLAB = 0.3
DOOR_OPEN = 0.62   # preview pose: the door part-way up so the lit bay shows


def door(name, cx, y, w, h, parent, skin):
    """Roll-up door: pivot at the top of the opening, panel hanging down (local -Z), slats."""
    piv = empty(name, P(cx, y, SLAB + h), parent)
    bx(f"{name}Panel", (w, 0.14, h), P(0, 0, -h / 2), skin, piv)
    for i in range(int(h / 0.5)):
        bx(f"{name}Slat{i}", (w + 0.02, 0.17, 0.05), P(0, 0, -0.35 - i * 0.5), MT["trim"], piv)
    bx(f"{name}Bottom", (w + 0.02, 0.2, 0.12), P(0, 0, -h + 0.06), MT["hazard"], piv)
    piv.scale.z = 1.0 if K.CFG.get("game") else 1 - DOOR_OPEN     # the game export rests shut
    return piv


def hall(g, x0, x1, y0, y1, h, doors, skin, wall=0.3, ribs=True, roof=True):
    """Walls (and a flat roof) of a hall from x0..x1, y0 (front) .. y1 (back), height h above the slab; `doors` =
    [(centre x, width, height)] openings in the front wall."""
    z = SLAB
    bx("HallBack", (x1 - x0, wall, h), P((x0 + x1) / 2, y1 - wall / 2, z + h / 2), skin, g, 0.03)
    for k, x in enumerate((x0 + wall / 2, x1 - wall / 2)):
        sy0, sy1 = y0 + wall + 0.12, y1 - wall      # between the front piers and the back wall (no coplanar faces)
        bx(f"HallSide{k}", (wall, sy1 - sy0, h), P(x, (sy0 + sy1) / 2, z + h / 2), skin, g, 0.03)
        if ribs:
            n = int((y1 - y0) / 0.62)
            for i in range(n + 1):
                yy = y0 + 0.15 + i * (y1 - y0 - 0.3) / n
                bx(f"HallRib{k}_{i}", (0.07, 0.1, h - 0.1), P(x + (-wall / 2 - 0.03 if k == 0 else wall / 2 + 0.03), yy, z + h / 2), MT["trim"], g)
    # front wall pieces round the door openings
    edges = [x0]
    for cx, w, dh in sorted(doors):
        edges += [cx - w / 2, cx + w / 2]
    edges.append(x1)
    for i in range(0, len(edges), 2):
        a, b = edges[i], edges[i + 1]
        if b - a > 0.01:
            bx(f"HallPier{i}", (b - a, wall + 0.1, h), P((a + b) / 2, y0 + wall / 2, z + h / 2), skin, g, 0.03)
    dh = max(d[2] for d in doors)
    bx("HallLintel", (x1 - x0 - 0.06, wall + 0.12, h - dh - 0.02), P((x0 + x1) / 2, y0 + wall / 2, z + dh + (h - dh) / 2), skin, g, 0.03)
    if roof:
        bx("HallRoof", (x1 - x0 + 0.3, y1 - y0 + 0.3, 0.22), P((x0 + x1) / 2, (y0 + y1) / 2, z + h + 0.11), MT["panel"], g, 0.03)
    # the dark bay inside, its floor, an assembly cradle per door
    iw, iy0, iy1 = x1 - x0 - 2 * wall - 0.05, y0 + wall + 0.02, y1 - wall - 0.02
    bx("BayFloor", (iw, iy1 - iy0, 0.04), P((x0 + x1) / 2, (iy0 + iy1) / 2, z + 0.02), MT["trim"], g)
    bx("BayBackWall", (iw, 0.05, h - 0.1), P((x0 + x1) / 2, iy1 - 0.03, z + h / 2), MT["trim"], g)
    bx("BayCeiling", (iw, iy1 - iy0, 0.05), P((x0 + x1) / 2, (iy0 + iy1) / 2, z + h - 0.06), MT["dark"], g)
    for k, x in enumerate(((x0 + x1) / 2 - iw / 2 + 0.03, (x0 + x1) / 2 + iw / 2 - 0.03)):
        bx(f"BaySideWall{k}", (0.05, iy1 - iy0, h - 0.1), P(x, (iy0 + iy1) / 2, z + h / 2), MT["panel"], g)
    for k, (cx, w, dh) in enumerate(doors):
        if k:
            bx(f"BayDivider{k}", (0.3, iy1 - iy0, h - 0.1), P((doors[k - 1][0] + cx) / 2, (iy0 + iy1) / 2, z + h / 2), MT["panel"], g)
    for k, (cx, w, dh) in enumerate(doors):
        bx(f"Cradle{k}", (w * 0.8, 2.8, 0.3), P(cx, y0 + 2.2, z + 0.2), MT["trim"], g)
        bx(f"CradleHaz{k}", (w * 0.82, 0.12, 0.31), P(cx, y0 + 0.85, z + 0.2), MT["hazard"], g)
        chassis(g, cx, y0 + 2.2, z + 0.35, k)


def chassis(g, x, y, z, k):
    """A half-built rover on the cradle: frame rails, axles, three wheels on, the cab shell waiting."""
    for s in (-1, 1):
        bx(f"Rail{k}{s}", (0.14, 2.6, 0.18), P(x + s * 0.55, y, z + 0.45), MT["steel"], g)
    for j, yy in enumerate((-0.9, 0.1, 1.0)):
        bx(f"Axle{k}{j}", (1.6, 0.12, 0.12), P(x, y + yy, z + 0.4), MT["trim"], g)
        for s in (-1, 1):
            if (j, s) != (2, 1):
                cylinder(f"Wheel{k}{j}{s}", 0.42, 0.3, P(x + s * 0.85, y + yy, z + 0.42), MT["rubber"], g, axis="X", segments=14)
    bx(f"CabShell{k}", (1.5, 1.0, 0.9), P(x, y - 0.8, z + 1.05), MT["white"], g, 0.06)
    bx(f"CabGlass{k}", (1.3, 0.05, 0.4), P(x, y - 1.31, z + 1.2), MT["glass"], g)


def work_glow(g, doors, y0, h):
    glow = empty("WorkGlow", P(0, 0, 0), g)
    for k, (cx, w, dh) in enumerate(doors):
        for j in range(3):
            bx(f"Work{k}_{j}", (w * 0.85, 0.25, 0.08), P(cx, y0 + 0.9 + j * 1.3, SLAB + h - 0.35), MT["work"], glow).visible_shadow = False
        bx(f"WorkFloor{k}", (w * 0.7, 2.4, 0.02), P(cx, y0 + 2.0, SLAB + 0.36), MT["strip"], glow).visible_shadow = False
    return glow


def apron(g, doors, y0, y_front, lit=False):
    z = SLAB + 0.004
    for k, (cx, w, dh) in enumerate(doors):
        for s in (-1, 1):
            x = cx + s * (w / 2 - 0.25)
            K.flat_path(f"Lane{k}{s}", [(x, y0 - 0.3), (x, y_front + 0.3)], 0.14, MT["hazard"], z, g)
        K.stripes(f"Sill{k}", (cx - w / 2, y0 - 0.15), (cx + w / 2, y0 - 0.15), 0.45, z, g, pitch=0.5)
        for i in range(3):
            yy = y0 - 1.2 - i * 0.95
            K.flat_path(f"Chev{k}{i}", [(cx - 0.6, yy - 0.35), (cx, yy), (cx + 0.6, yy - 0.35)], 0.12, MT["owner"] if lit else MT["hazard"], z, g)
        if lit:
            for s in (-1, 1):
                for i in range(4):
                    zcyl(f"LaneLamp{k}{s}{i}", 0.07, SLAB, SLAB + 0.06, MT["strip"], g, xy=(cx + s * (w / 2 - 0.25), y0 - 0.8 - i * 0.9), segs=8)


def beacon(parent, at, radar=False, r=0.5):
    spin = empty("Spin", at, parent)
    if radar:
        bx("RadarBar", (r * 2.2, 0.14, 0.32), P(0, 0, 0.3), MT["trim"], spin, 0.02)
        bx("RadarFace", (r * 2.1, 0.03, 0.26), P(0, -0.08, 0.3), MT["amber"], spin).visible_shadow = False
        zcyl("RadarPost", 0.1, 0.0, 0.2, MT["dark"], spin, segs=10)
    else:
        zcyl("BeaconBase", 0.16, 0.0, 0.12, MT["dark"], spin, segs=12)
        bx("BeaconLens", (0.5, 0.12, 0.16), P(0.12, 0, 0.22), MT["amber"], spin)
        zcyl("BeaconCap", 0.11, 0.12, 0.32, MT["amber"], spin, segs=12)
    return spin


def tower(g, x, y, w, d, floors, skin, cap_dish=False, accent=None):
    fh = 1.5
    z = SLAB
    for f in range(floors):
        bx(f"Tower{f}", (w, d, fh), P(x, y, z + fh / 2 + f * fh), skin, g, 0.04)
        K.pane_row(f"TowerWin{f}", (x - w / 2 + 0.1, y - d / 2 - 0.01, 0), (1, 0, 0), None, w - 0.2, z + f * fh + 0.55, z + f * fh + 1.15, 2, MT["window"], g, frac=0.8)
        bx(f"TowerBand{f}", (w + 0.02, d + 0.02, 0.12), P(x, y, z + f * fh + 0.25), accent or MT["accent"], g)
    top = z + floors * fh
    bx("TowerCab", (w + 0.4, d + 0.4, 1.1), P(x, y, top + 0.55), skin, g, 0.05)
    bx("TowerGlass", (w + 0.42, d + 0.42, 0.6), P(x, y, top + 0.6), MT["glass"], g, 0.02)
    bx("TowerLit", (w + 0.3, 0.02, 0.4), P(x, y - (d + 0.42) / 2 - 0.01, top + 0.6), MT["window"], g).visible_shadow = False
    bx("TowerRoof", (w + 0.6, d + 0.6, 0.14), P(x, y, top + 1.17), MT["panel"], g, 0.03)
    bx("TowerOwner", (w + 0.62, d + 0.62, 0.08), P(x, y, top + 1.05), MT["owner"], g)
    return top + 1.24


def flood_pole(g, x, y, h, yaw, name="Flood"):
    K.light_mast(name, x, y, SLAB, h, yaw, g, heads=1)


def crates(g, x, y, n, seed=0):
    for i in range(n):
        s = 0.55 - 0.06 * ((i + seed) % 3)
        bx(f"Crate{x}{i}", (s, s, s * 0.9), P(x + (i % 2) * 0.62, y + (i // 2) * 0.6, SLAB + s * 0.45 + (0.5 if i >= 4 else 0)),
           MT["accent"] if (i + seed) % 3 == 0 else MT["pallet"], g, 0.02, rot=(0, 0, math.radians(7 * ((i * 5 + seed) % 5 - 2))))


def tyre_stack(g, x, y, n):
    for i in range(n):
        cylinder(f"Tyre{x}{i}", 0.42, 0.28, P(x, y, SLAB + 0.15 + i * 0.3), MT["rubber"], g, axis="Z", segments=14)


def wheel_rack(g, x, y, n, along_y=True):
    bx(f"Rack{x}", (0.3, n * 0.55 + 0.2, 1.6) if along_y else (n * 0.55 + 0.2, 0.3, 1.6), P(x, y, SLAB + 0.8), MT["steel"], g)
    for i in range(n):
        off = (i - (n - 1) / 2) * 0.55
        cylinder(f"RackWheel{x}{i}", 0.42, 0.26, P(x + (0.3 if along_y else off), y + (off if along_y else -0.3), SLAB + 0.75), MT["rubber"], g, axis="X" if along_y else "Y", segments=14)


def roof_vents(g, xs, y, z):
    for i, x in enumerate(xs):
        bx(f"Vent{i}{x}", (0.7, 0.7, 0.4), P(x, y, z + 0.2), MT["panel"], g, 0.03)
        zcyl(f"VentFan{i}{x}", 0.25, z + 0.4, z + 0.43, MT["dark"], g, xy=(x, y), segs=12)


def hvac(g, x, y, z):
    bx(f"Hvac{x}", (1.4, 1.0, 0.7), P(x, y, z + 0.35), MT["panel"], g, 0.04)
    for j in (-0.35, 0.35):
        zcyl(f"HvacFan{x}{j}", 0.28, z + 0.7, z + 0.74, MT["dark"], g, xy=(x + j, y), segs=14)


def sign(M, g, x, y, z, w, text="VEHICLE BAY 01"):
    bx("SignBoard", (w + 0.1, 0.06, 0.36), P(x, y - 0.03, z), MT["dark"], g)
    e = K.marker("SignFront", P(x, y - 0.07, z), (math.radians(90), 0, 0), w, 0.24, g)
    M.sign(e, text)


def charger(g, x, y, k):
    bx(f"Charger{k}", (0.5, 0.4, 1.3), P(x, y, SLAB + 0.65), MT["white"], g, 0.04)
    bx(f"ChargerFace{k}", (0.32, 0.02, 0.32), P(x, y - 0.21, SLAB + 0.95), MT["strip"], g).visible_shadow = False
    bx(f"ChargerBand{k}", (0.52, 0.42, 0.1), P(x, y, SLAB + 0.3), MT["navy"], g)
    K.sagging(f"ChargerCable{k}", P(x + 0.26, y, SLAB + 0.9), P(x + 0.7, y - 0.2, SLAB + 0.25), 0.15, 0.03, MT["cable"], g)


def transporter_pad(g, x, y, r=1.25):
    zcyl("TPadBase", r, SLAB, SLAB + 0.25, MT["trim"], g, xy=(x, y), segs=32)
    zcyl("TPadTop", r - 0.12, SLAB + 0.25, SLAB + 0.3, MT["white"], g, xy=(x, y), segs=32)
    for i in range(6):
        a = math.radians(60 * i + 30)
        zcyl(f"TPadDisc{i}", 0.26, SLAB + 0.3, SLAB + 0.34, MT["cyan"], g, xy=(x + 0.62 * math.cos(a), y + 0.62 * math.sin(a)), segs=12)
    zcyl("TPadDisc6", 0.26, SLAB + 0.3, SLAB + 0.34, MT["cyan"], g, xy=(x, y), segs=12)
    K.ring_mesh("TPadRing", r - 0.1, r - 0.02, SLAB + 0.3, SLAB + 0.32, MT["strip"], g, xy=(x, y), segs=32)
    # emitter canopy on two posts
    for s in (-1, 1):
        bx(f"TPadPost{s}", (0.18, 0.18, 3.2), P(x + s * (r + 0.05), y + 0.4, SLAB + 1.6), MT["white"], g, 0.03)
    bx("TPadEmitter", (2 * r + 0.5, 1.0, 0.25), P(x, y + 0.1, SLAB + 3.3), MT["white"], g, 0.04)
    bx("TPadEmitterGlow", (2 * r - 0.2, 0.8, 0.03), P(x, y + 0.1, SLAB + 3.17), MT["cyan"], g).visible_shadow = False
    # a crate being beamed in (preview: a ghost of light)
    bx("TPadCargo", (0.8, 0.8, 0.7), P(x, y, SLAB + 0.7), MT["accent"], g, 0.03)


# ---------------------------------------------------------------- the marks


def build(mark, M):
    two = mark >= 4
    skin = MT["prefab"] if mark == 1 else MT["ribbed"] if mark == 2 else MT["white"]
    accent = MT["accent"] if mark <= 2 else MT["navy"]
    if not two:
        H = [4.6, 5.8, 6.0][mark - 1]
        x0, x1 = [(-3.0, 3.0), (-3.5, 3.5), (-3.5, 3.5)][mark - 1]
        y0, y1 = -2.2, [2.8, 4.0, 4.0][mark - 1]
        doors = [(0.0, 4.6, 4.0)]
    else:
        H = 6.2 if mark == 4 else 6.4
        x0, x1, y0, y1 = -5.3, 5.3, -2.4, 3.5
        doors = [(-2.62, 4.4, 4.2), (2.62, 4.4, 4.2)]
    y_front = -5.4 if mark < 4 else -5.8

    # --- 0 foundation + apron
    g = M.piece("Foundation")
    if mark <= 3:
        MK.slab("Slab", [8.0, 11.6, 11.8][mark - 1], [8.8, 9.6, 9.6][mark - 1], SLAB, g, xy=(0.6 if mark > 1 else 0, [-1.0, -0.6, -0.6][mark - 1]), mat=MT["concrete"])
    else:
        MK.slab("Slab", 12.0, 10.0, SLAB, g, xy=(0, -0.9), chamfer=1.8, mat=MT["concrete"])
        MK.slab("SlabEdge", 12.3, 10.3, SLAB - 0.12, g, xy=(0, -0.9), chamfer=1.9, mat=MT["dark"])
    apron(g, doors, y0, y_front, lit=mark >= 4)
    K.bollards("Bollard", [(d[0] + s * (d[1] / 2 + 0.35), y0 - 0.4) for d in doors for s in (-1, 1)], SLAB, g)
    K.point("SparkPoint", P(0, y0 + 1.5, SLAB + 2.0), g)
    for k, (x, y, yaw) in enumerate([(-7.2, 1.0, 90), (7.2, 1.0, -90), (0.0, 6.6, 0), (-6.2, -4.0, 90), (6.2, -4.0, -90)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)

    # --- 1 hall walls (+ the bay inside)
    g = M.piece("Hall")
    hall(g, x0, x1, y0, y1, H, doors, skin, ribs=mark <= 2, roof=mark <= 2)
    if mark >= 3:
        for k, x in enumerate((x0, x1)):
            bx(f"HallBand{k}", (0.04, y1 - y0 - 0.2, 0.5), P(x - 0.02 if k == 0 else x + 0.02, (y0 + y1) / 2, SLAB + 1.0), accent, g)
            bx(f"HallOwner{k}", (0.04, y1 - y0 - 0.2, 0.14), P(x - 0.02 if k == 0 else x + 0.02, (y0 + y1) / 2, SLAB + 1.4), MT["owner"], g)
            K.pane_row(f"HallSideWin{k}", (x - 0.03 if k == 0 else x + 0.03, y1 - 0.4 if k == 0 else y0 + 0.4, 0), (0, -1 if k == 0 else 1, 0), None,
                       y1 - y0 - 0.8, SLAB + H - 1.6, SLAB + H - 1.0, 4, MT["window"], g, frac=0.7)
    if mark == 1:
        for k in range(5):   # prefab panel seams on the front
            x = x0 + 0.5 + k * (x1 - x0 - 1.0) / 4
            if abs(x) > 2.4:
                bx(f"Seam{k}", (0.05, 0.04, H), P(x, y0 - 0.06, SLAB + H / 2), MT["trim"], g)
        for k, x in enumerate((x0 + 0.2, x1 - 0.2)):
            bx(f"Bolt{k}", (0.12, 0.08, 0.12), P(x, y0 - 0.06, SLAB + H - 0.3), MT["steel"], g)

    # --- 2 roof
    g = M.piece("Roof")
    top = SLAB + H + 0.22
    if mark == 1:
        bx("RoofEdge", (x1 - x0 + 0.34, 0.12, 0.3), P(0, y0 - 0.17, top - 0.05), MT["trim"], g)
        roof_vents(g, (1.6,), 1.2, top)
    elif mark == 2:
        bx("Skylight", (1.4, y1 - y0 - 1.6, 0.25), P(0, (y0 + y1) / 2 + 0.3, top + 0.12), MT["glass"], g, 0.02)
        bx("SkylightFrame", (1.6, y1 - y0 - 1.4, 0.12), P(0, (y0 + y1) / 2 + 0.3, top + 0.04), MT["trim"], g)
        roof_vents(g, (-2.3, 2.3), 2.6, top)
        bx("RoofEdge", (x1 - x0 + 0.34, 0.14, 0.4), P(0, y0 - 0.17, top - 0.05), MT["trim"], g)
        bx("RoofOwner", (x1 - x0 + 0.36, 0.16, 0.12), P(0, y0 - 0.18, top - 0.2), MT["owner"], g)
    elif mark == 3:
        bx("RoofLow", (x1 - x0 + 0.3, y1 - y0 + 0.3, 0.22), P(0, (y0 + y1) / 2, top - 0.11), MT["panel"], g, 0.03)
        cw, ch = 3.6, 1.3     # clerestory
        bx("Clerestory", (cw, y1 - y0 - 0.6, ch), P(0, (y0 + y1) / 2 + 0.2, top + ch / 2), MT["white"], g, 0.03)
        for k, s in enumerate((-1, 1)):
            K.pane_row(f"ClereWin{k}", (s * (cw / 2 + 0.01), y1 - 0.5 if s < 0 else y0 + 0.5, 0), (0, -1 if s < 0 else 1, 0), None, y1 - y0 - 1.2,
                       top + 0.3, top + 1.0, 5, MT["window"], g, frac=0.75)
        bx("ClereRoof", (cw + 0.5, y1 - y0 - 0.3, 0.16), P(0, (y0 + y1) / 2 + 0.2, top + ch + 0.08), MT["panel"], g, 0.03)
        bx("ClereNavy", (cw + 0.52, y1 - y0 - 0.28, 0.08), P(0, (y0 + y1) / 2 + 0.2, top + ch - 0.04), MT["navy"], g)
        bx("RoofEdge", (x1 - x0 + 0.34, 0.14, 0.45), P(0, y0 - 0.17, top - 0.05), MT["navy"], g)
        MK.strip_lights("EdgeLight", (x0, y0 - 0.26), (x1, y0 - 0.26), top - 0.15, 6, g)
    elif mark == 4:
        for k, (cx, w, dh) in enumerate(doors):
            MK.vault(f"Barrel{k}", 5.3, y1 - y0 + 0.3, 1.5, P(cx, (y0 + y1) / 2, SLAB + H), g, MT["white"], segs=12, ribs=4, rib_mat=MT["navy"])
        bx("RoofValley", (0.6, y1 - y0 + 0.3, 0.3), P(0, (y0 + y1) / 2, SLAB + H + 0.15), MT["trim"], g)
        bx("RoofEdge", (x1 - x0 + 0.3, 0.16, 0.5), P(0, y0 - 0.18, SLAB + H - 0.1), MT["navy"], g)
        MK.strip_lights("EdgeLight", (x0, y0 - 0.27), (x1, y0 - 0.27), SLAB + H - 0.25, 10, g)
        MK.solar_array("SolarRoof", -2.62, 2.0, SLAB + H + 1.2, g, n=3, w=1.1, d=0.8, tilt=20, yaw=180)
    else:
        # one sweeping canopy over both lines, overhanging the doors as a porch
        MK.vault("Canopy", 11.4, y1 - y0 + 1.6, 2.4, P(0, (y0 + y1) / 2 - 0.65, SLAB + H - 0.2), g, MT["white"], segs=16, ribs=5, rib_mat=MT["navy"])
        for s in (-1, 1):
            beam(f"CanopyStrut{s}", P(s * 5.3, y0 - 1.3, SLAB), P(s * 5.0, y0 - 1.3, SLAB + H - 0.1), 0.25, MT["white"], g)
            bx(f"CanopyFoot{s}", (0.5, 0.5, 0.3), P(s * 5.3, y0 - 1.3, SLAB + 0.15), MT["navy"], g, 0.03)
        MK.strip_lights("CanopyLight", (-5.3, y0 - 1.42), (5.3, y0 - 1.42), SLAB + H - 0.28, 12, g)
        for k in range(16):    # skylight ribbon along the crown
            bx(f"CrownGlass{k}", (1.0, 0.35, 0.06), P(0, y0 - 1.2 + k * 0.48, SLAB + H + 2.2), MT["glass"], g)

    # --- 3 doors (+ the work lights inside)
    g = M.piece("Doors")
    for k, (cx, w, dh) in enumerate(doors):
        frame_mat = MT["hazard"] if mark >= 2 else MT["trim"]
        for s in (-1, 1):
            bx(f"Jamb{k}{s}", (0.22, 0.4, dh + 0.1), P(cx + s * (w / 2 + 0.11), y0 - 0.12, SLAB + dh / 2), frame_mat, g, 0.02)
        bx(f"Head{k}", (w + 0.44, 0.45, 0.35), P(cx, y0 - 0.12, SLAB + dh + 0.17), MT["trim"], g, 0.02)
        if mark >= 2:
            K.wall_stripes(f"JambStripe{k}", (cx - w / 2 - 0.22, y0 - 0.33), (cx - w / 2, y0 - 0.33), SLAB + 0.1, SLAB + dh, g, pitch=0.3)
        door("Door" if k == 0 else f"Door{k}", cx, y0 - 0.05, w, dh, g, MT["steel"] if mark >= 3 else MT["panel"])
        bx(f"HeadLamp{k}", (w * 0.6, 0.1, 0.08), P(cx, y0 - 0.38, SLAB + dh + 0.4), MT["lamp"], g).visible_shadow = False
        bx(f"Owner{k}", (w + 0.46, 0.04, 0.14), P(cx, y0 - 0.36, SLAB + dh + 0.05), MT["owner"], g)
    work_glow(g, doors, y0, H)
    M.info["keep"] |= {"Door", "Door1", "WorkGlow"}

    # --- 4 control: cabin / tower / spire with the beacon
    g = M.piece("Control")
    if mark == 1:
        c = P(-1.8, 1.6, SLAB + H + 0.22)
        bx("Cabin", (1.8, 1.6, 1.3), c + P(0, 0, 0.65), MT["prefab"], g, 0.04)
        bx("CabinGlass", (1.6, 0.04, 0.5), c + P(0, -0.81, 0.85), MT["glass"], g)
        bx("CabinLit", (1.4, 0.02, 0.35), c + P(0, -0.83, 0.85), MT["window"], g).visible_shadow = False
        bx("CabinRoof", (2.0, 1.8, 0.1), c + P(0, 0, 1.35), MT["trim"], g)
        zcyl("BeaconStub", 0.08, c.z + 1.4, c.z + 1.9, MT["steel"], g, xy=(c.x + 0.5, c.y), segs=8)
        beacon(g, c + P(0.5, 0, 1.9))
        K.ladder("Ladder", (x1 + 0.3, 2.6, SLAB), SLAB + H + 0.2, (1, 0), g, cage=False)
    elif mark in (2, 3):
        floors = 3 if mark == 2 else 4
        tx, ty = -4.6, 3.0
        top = tower(g, tx, ty, 1.7, 1.7, floors, MT["ribbed"] if mark == 2 else MT["white"], accent=accent)
        if mark == 3:
            zcyl("DishMast", 0.1, top, top + 0.8, MT["steel"], g, xy=(tx + 0.4, ty + 0.3), segs=8)
            MK.dish("TowerDish", 0.7, g, P(tx + 0.4, ty + 0.3, top + 1.0), tilt=40, yaw=200)
        beacon(g, P(tx - 0.3, ty - 0.3, top), radar=True, r=0.55)
        bx("Walkway", (1.2, 0.8, 0.12), P(-3.9, 2.0, SLAB + H - 0.3), MT["grate"], g)
    elif mark == 4:
        tx, ty = 3.6, 2.3
        base = SLAB + H + 0.15
        bx("RoofTower", (2.0, 1.8, 2.4), P(tx, ty, base + 1.2), MT["white"], g, 0.05)
        bx("RoofTowerGlass", (2.04, 1.84, 0.7), P(tx, ty, base + 1.8), MT["glass"], g, 0.02)
        bx("RoofTowerLit", (1.8, 0.02, 0.45), P(tx, ty - 0.93, base + 1.8), MT["window"], g).visible_shadow = False
        bx("RoofTowerNavy", (2.06, 1.86, 0.18), P(tx, ty, base + 0.9), MT["navy"], g)
        bx("RoofTowerCap", (2.4, 2.2, 0.16), P(tx, ty, base + 2.48), MT["panel"], g, 0.03)
        for i in range(3):   # sensor array
            bx(f"Array{i}", (0.08, 0.6, 1.2), P(tx - 0.6 + i * 0.6, ty + 0.4, base + 3.2), MT["white"], g, 0.02, rot=(math.radians(-15), 0, 0))
        beacon(g, P(tx + 0.6, ty - 0.5, base + 2.56), radar=True, r=0.5)
        hvac(g, -2.6, 2.3, SLAB + H + 1.4)
    else:
        # glass control bridge over the doors + a spire at the back with dish and beacon
        bz = SLAB + 4.7
        bx("Bridge", (3.6, 1.4, 1.0), P(0, y0 - 0.9, bz + 0.5), MT["white"], g, 0.06)
        bx("BridgeGlass", (3.4, 0.05, 0.6), P(0, y0 - 1.62, bz + 0.55), MT["glass"], g)
        bx("BridgeLit", (3.2, 0.02, 0.45), P(0, y0 - 1.65, bz + 0.55), MT["window"], g).visible_shadow = False
        bx("BridgeNavy", (3.62, 1.42, 0.14), P(0, y0 - 0.9, bz + 0.07), MT["navy"], g)
        bx("BridgeOwner", (3.64, 1.44, 0.08), P(0, y0 - 0.9, bz + 0.18), MT["owner"], g)
        sx, sy = 4.2, 2.6
        zcyl("Spire", 0.45, SLAB + H, SLAB + H + 5.2, MT["white"], g, xy=(sx, sy), segs=16, radius2=0.22)
        zcyl("SpireBand", 0.4, SLAB + H + 1.2, SLAB + H + 1.5, MT["navy"], g, xy=(sx, sy), segs=16)
        K.window_ring("SpireLit", 0.37, SLAB + H + 2.0, SLAB + H + 2.3, 8, MT["strip"], g, xy=(sx, sy))
        MK.dish("SpireDish", 0.9, g, P(sx - 0.5, sy, SLAB + H + 3.4), tilt=45, yaw=150)
        beacon(g, P(sx, sy, SLAB + H + 5.2), radar=False)
        hvac(g, -3.2, 2.4, SLAB + H + 1.2)
    M.info["keep"] |= {"Spin"}

    # --- 5 annex / side works
    g = M.piece("Annex")
    if mark == 1:
        crates(g, 3.3, -3.9, 3)
        tyre_stack(g, -3.5, -3.6, 3)
    elif mark in (2, 3):
        ax0, ax1, ay0, ay1 = x1, x1 + 1.7, -1.0, 3.4
        bx("Annex", (ax1 - ax0, ay1 - ay0, 3.0), P((ax0 + ax1) / 2, (ay0 + ay1) / 2, SLAB + 1.5), skin, g, 0.03)
        bx("AnnexRoof", (ax1 - ax0 + 0.3, ay1 - ay0 + 0.3, 0.14), P((ax0 + ax1) / 2, (ay0 + ay1) / 2, SLAB + 3.07), MT["panel"], g, 0.02, rot=(0, math.radians(-8), 0))
        bx("AnnexDoor", (0.04, 1.0, 2.0), P(ax1 + 0.01, 0.0, SLAB + 1.0), MT["trim"], g)
        bx("AnnexLamp", (0.06, 0.4, 0.08), P(ax1 + 0.03, 0.0, SLAB + 2.2), MT["window"], g).visible_shadow = False
        wheel_rack(g, ax1 + 0.4, 2.0, 3)
        crates(g, 3.4, -4.4, 3, seed=1)
        if mark == 3:
            MK.solar_array("AnnexSolar", (ax0 + ax1) / 2, 1.2, SLAB + 3.1, g, n=3, w=0.5, d=1.0, tilt=0, yaw=90)
            # charging canopy over two chargers at the apron's left
            for k, y in enumerate((-3.2, -4.6)):
                charger(g, -4.6, y, k)
            for s in (-1, 1):
                bx(f"ChargePost{s}", (0.14, 0.14, 2.8), P(-5.25, -3.9 + s * 1.1, SLAB + 1.4), MT["white"], g)
            bx("ChargeRoof", (1.4, 2.8, 0.12), P(-4.9, -3.9, SLAB + 2.85), MT["white"], g, 0.03)
            bx("ChargeRoofNavy", (1.42, 2.82, 0.06), P(-4.9, -3.9, SLAB + 2.76), MT["navy"], g)
            bx("ChargeLight", (1.0, 2.2, 0.03), P(-4.9, -3.9, SLAB + 2.72), MT["strip"], g).visible_shadow = False
        else:
            tyre_stack(g, -4.0, -4.2, 4)
    elif mark == 4:
        for k, y in enumerate((-3.6, -5.0)):
            charger(g, -5.2, y + 0.4, k)
        wheel_rack(g, 5.6, 1.0, 3)
        crates(g, 4.4, -4.6, 4, seed=2)
    else:
        transporter_pad(g, -4.25, -4.6, r=1.15)
        for k, y in enumerate((-3.4,)):
            charger(g, 5.3, y, k)
        crates(g, 4.6, -5.1, 3, seed=1)

    # --- 6 lights + sign
    g = M.piece("Lights")
    if mark == 1:
        flood_pole(g, 3.6, -2.8, 4.2, -120)
        sign(M, g, 0, y0 - 0.32, SLAB + 4.4, 2.4, "VEHICLE BAY")
    elif mark == 2:
        K.light_mast("Mast", 4.9, -3.4, SLAB, 6.5, 120, g)
        sign(M, g, 0, y0 - 0.32, SLAB + 5.1, 2.8, "VEHICLE BAY 01")
    elif mark == 3:
        K.light_mast("Mast", 5.0, -3.6, SLAB, 7.0, 120, g)
        K.bollards("ApronLamp", [(-2.8, -4.9), (2.8, -4.9)], SLAB, g)
        sign(M, g, 0, y0 - 0.32, SLAB + 5.2, 3.0, "MOTOR POOL 01")
    elif mark == 4:
        K.light_mast("MastL", -5.6, -2.6, SLAB, 7.5, 60, g)
        K.light_mast("MastR", 5.6, -2.6, SLAB, 7.5, 120, g)
        sign(M, g, 0, y0 - 0.36, SLAB + 5.4, 2.6, "ASSEMBLY 01")
    else:
        K.light_mast("MastL", -5.9, 1.0, SLAB, 7.8, 30, g)
        K.light_mast("MastR", 5.9, 1.0, SLAB, 7.8, 150, g)
        sign(M, g, 0, y0 - 1.66, SLAB + 4.45, 3.0, "STARFLEET MOTOR POOL")
    for k, s in enumerate((-1, 1)):
        sphere(f"Nav{k}", 0.07, P(s * (x1 + 0.05), y0, SLAB + H + 0.1), MT["beacon"], g, 8)

    # preview extras
    M.info["rover"] = (-3.0 if not two else -2.6, -7.6, 200) if mark != 5 else (2.4, -8.6, 195)
    for d in doors:
        M.lamp((0.75, 0.88, 1.0), 120, P(d[0], y0 + 1.5, SLAB + H - 0.8))
        M.lamp((1.0, 0.92, 0.8), 140, P(d[0], y0 - 2.5, SLAB + 4.5))
    M.lamp((1.0, 0.9, 0.75), 260, P(x1 + 0.5, -3.0, 7.5))
    M.lamp((1.0, 0.9, 0.75), 200, P(x0 - 0.5, -3.0, 7.0))
    if mark == 5:
        M.lamp((0.4, 0.8, 1.0), 120, P(-4.25, -4.6, 2.0))


MK.run("vehicle_bay", build,
       hero=((15.0, -20.0, 11.5), (0.0, -0.8, 2.8), 30),
       close=((-10.5, -15.0, 6.8), (-0.8, -2.6, 2.4), 32))
