"""Federation Fusion Reactor (`fusion_reactor`, power_supply), game-ready (owner 2026-11-06: refinery-level detail).

A compact tokamak under an open shield dome, as the RTS camera sees it from the front:
  * a round plinth with a raised reactor floor; the TOKAMAK vessel (torus) with twelve D-ish toroidal field coils and
    a glowing plasma slit round its equator; poloidal field hoops above and below;
  * a warp-core-like CORE COLUMN rising out of the torus hole: stacked glowing segments ("CoreGlow") between dark
    frames, a constrictor cap; three magnetic RINGS around it ("Ring0".."Ring2", spinning, alternate directions);
  * the SHIELD DOME: ribs all round, armour panels over the back so the core shows to the camera, a crown ring;
  * two fan-topped COOLING TOWERS at the back ("Fan0", "Fan1"; steam at "Vent0", "Vent1"), radiator fin banks at the
    sides, glowing PLASMA CONDUITS ("Plasma") from the vessel to the power converter, DEUTERIUM spheres (the upkeep),
    the CONTROL building at the front left with the "REACTOR nn" sign, the TRANSFORMER yard and a POWER PYLON with
    cables at the front right, light masts, beacons, hazard striping.

Frame and conventions (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit; the plinth
is a 16-gon of radius 6.4 (inside the 8.5 the scatter keeps clear); the core cap tops out at 9.3.

BUILD PIECES (Build0..Build13): Plinth, ReactorFloor, Vessel, FieldCoils, CoreColumn, DomeFrame, DomePanels,
CoolingTowers, Radiators, Conduits, FuelTanks, Control, Transformer, Lights.

GAME CONTRACT (BuildingModels.Reactor.cs / PlanetView.Reactor.cs):
  CoreGlow       material of the plasma slit and the column segments (pulse its emission by output; off when broken).
  Plasma         material of the conduits (flow pulse). CoreLight empty = where the cyan OmniLight goes.
  Ring0..Ring2   constrictor rings round the column: spin about local Y in Godot (Blender Z).
  Fan0, Fan1     cooling-tower fans (vertical axis). Vent0, Vent1 empties = steam. SparkPoint = sparks when broken.
  Lamp, Window, Beacon, Status   glow materials (Status = the control desk's green/amber/red board).
  Towers, Pylon  scale pivots (±10% in Y). OptTank (second deuterium sphere), OptRadiator   optional parts.
  SignFront, LabelTank, LabelPylon   decal markers. RoofSpot0, AddOnSpot0..4   variety spots.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_reactor.py --game          (export game/models/buildings/fusion_reactor.glb)
  /home/user/.bpyvenv/bin/python blender/build_reactor.py --game --preview blender/previews/reactor
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, lathe, strut, empty, cylinder, hoop, railing, ladder, pipe  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
import bmesh  # noqa: E402

CFG = K.setup("fusion_reactor")

SLAB = 0.35
PLINTH_R = 7.1
FLOOR_R, FLOOR_Z = 3.95, 0.8
C = Vector((0.0, 0.55))                  # reactor centre
TOR_R, TOR_r, TOR_Z = 2.3, 0.78, 2.05    # torus major/minor radius, centre height
COL_R, COL_TOP = 0.52, 8.3               # core column
DOME_R, DOME_H = 3.8, 6.1                # shield dome base radius, height above the floor
CROWN_R = 1.05
RINGS = (3.7, 4.95, 6.2)
TOWERS = [Vector((-4.3, 3.9)), Vector((4.3, 3.9))]
CONTROL = Vector((-3.45, -3.7))
XFMR = Vector((3.5, -4.4))
PYLON = Vector((5.7, -2.3))
CONV = Vector((3.75, -2.35))
TANKS = [Vector((-1.4, 5.1)), Vector((1.4, 5.1))]
PIECES = ["Plinth", "ReactorFloor", "Vessel", "FieldCoils", "CoreColumn", "DomeFrame", "DomePanels", "CoolingTowers",
          "Radiators", "Conduits", "FuelTanks", "Control", "Transformer", "Lights"]


def materials():
    K.materials()
    K.mat("core", "CoreGlow", (0.12, 0.55, 0.95), 0.0, 0.2, emission=(0.12, 0.62, 1.0), strength=5.0)
    K.mat("plasma", "Plasma", (0.3, 0.5, 1.0), 0.0, 0.25, emission=(0.3, 0.48, 1.0), strength=4.0)
    K.mat("coil", "Coil", (0.2, 0.22, 0.27), 0.6, 0.4, textured=True)
    K.mat("shield", "Shield", (0.66, 0.68, 0.72), 0.35, 0.45, textured=True, cull=False)
    K.mat("status", "Status", (0.3, 1.0, 0.5), 0.0, 0.3, emission=(0.3, 1.0, 0.5), strength=3.0)
    K.mat("insulator", "Insulator", (0.55, 0.32, 0.2), 0.1, 0.35, textured=True)
    K.mat("deut", "Deuterium", (0.36, 0.48, 0.64), 0.3, 0.45, textured=True)
    K.mat("label", "Label", (0.9, 0.9, 0.88), 0.0, 0.6, textured=True)


def at(a_deg, r, z=0.0):
    a = math.radians(a_deg)
    return P(C.x + r * math.cos(a), C.y + r * math.sin(a), z)


# ---------------------------------------------------------------- pieces


def plinth(g):
    lathe("Plinth", [(0.0, 0.0), (PLINTH_R, 0.0), (PLINTH_R, SLAB), (0.0, SLAB)], MT["concrete"], g, (0, 0, 0), 16, smooth=False)
    lathe("PlinthSkirt", [(0.0, 0.0), (PLINTH_R + 0.15, 0.0), (PLINTH_R + 0.15, 0.14), (0.0, 0.14)], MT["dark"], g, (0, 0, 0), 16, smooth=False)
    z = SLAB + 0.003
    K.ring_mesh("PlinthLine", PLINTH_R - 0.3, PLINTH_R - 0.24, SLAB, z + 0.001, MT["hazard"], g, segs=16)
    qs = []
    for k in range(8):
        a = math.radians(22.5 * 2 * k + 11.25)
        d = Vector((math.cos(a), math.sin(a), 0))
        s = Vector((-d.y, d.x, 0)) * 0.02
        p0, p1 = d * 4.5, d * (PLINTH_R - 0.35)
        qs.append((p0 - s + P(0, 0, z), p1 - s + P(0, 0, z), p1 + s + P(0, 0, z), p0 + s + P(0, 0, z)))
    K.faces_obj("Joints", qs, MT["dark"], g)
    for k, (x, y) in enumerate([(-1.6, -4.8), (1.6, -4.8), (-5.0, 0.6), (5.0, 1.8)]):
        bx(f"Drain{k}", (0.6, 0.3, 0.02), P(x, y, SLAB + 0.008), MT["grate"], g)
    K.bollards("Bollard", [(-1.0, -6.6), (1.0, -6.6), (-6.3, -2.6), (-2.1, -6.4)], SLAB, g)
    for k, (x, y, yaw) in enumerate([(-7.9, -2.2, 90), (7.9, 1.6, -90), (-3.2, 7.55, 0), (3.2, 7.55, 0), (-6.6, -4.3, 60)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)
    K.point("RoofSpot0", P(CONTROL.x - 0.4, CONTROL.y + 0.25, SLAB + 2.55), g)


def reactor_floor(g):
    lathe("Floor", [(0.0, SLAB), (FLOOR_R + 0.15, SLAB), (FLOOR_R + 0.15, FLOOR_Z - 0.12), (FLOOR_R, FLOOR_Z), (0.0, FLOOR_Z)], MT["panel"], g, (C.x, C.y, 0), 32, smooth=False)
    K.band("FloorOwner", FLOOR_R + 0.16, SLAB + 0.12, SLAB + 0.24, MT["owner"], g, xy=C, segs=32)
    # hazard ring round the vessel, painted rings
    qs_y, qs_k = [], []
    for k in range(32):
        a0, a1 = 2 * math.pi * k / 32, 2 * math.pi * (k + 1) / 32
        q = tuple(P(C.x + r * math.cos(a), C.y + r * math.sin(a), FLOOR_Z + 0.004) for r, a in ((3.35, a0), (3.65, a0 + 0.08), (3.65, a1 + 0.08), (3.35, a1)))
        (qs_y if k % 2 == 0 else qs_k).append(q)
    K.faces_obj("FloorStripeY", qs_y, MT["hazard"], g)
    K.faces_obj("FloorStripeK", qs_k, MT["dark"], g)
    # front steps up to the floor
    for k in range(3):
        bx(f"Step{k}", (2.0, 0.42, (FLOOR_Z - SLAB) * (3 - k) / 3), P(C.x, C.y - FLOOR_R - 0.21 - k * 0.42, SLAB + (FLOOR_Z - SLAB) * (3 - k) / 6),
           MT["concrete"], g)
    railing("StepRailL", [(C.x - 1.05, C.y - FLOOR_R - 1.3), (C.x - 1.05, C.y - FLOOR_R + 0.1)], SLAB + 0.25, g, h=0.85, post=0.7)
    railing("StepRailR", [(C.x + 1.05, C.y - FLOOR_R - 1.3), (C.x + 1.05, C.y - FLOOR_R + 0.1)], SLAB + 0.25, g, h=0.85, post=0.7)
    # vessel supports: a ring girder on short pillars
    for k in range(8):
        a = 22.5 + 45 * k
        p = at(a, TOR_R)
        bx(f"Pier{k}", (0.32, 0.32, TOR_Z - TOR_r - FLOOR_Z + 0.05), P(p.x, p.y, (TOR_Z - TOR_r + FLOOR_Z) / 2), MT["trim"], g, rot=(0, 0, math.radians(a)))
    hoop("SupportRing", TOR_R, TOR_Z - TOR_r - 0.02, 0.18, MT["trim"], g, xy=C, segs=32)


def vessel(g):
    # the torus: a lathed circle profile swept round (a D-ish section: a bit taller than wide)
    prof = []
    for i in range(13):
        t = 2 * math.pi * i / 12
        prof.append((TOR_R + TOR_r * math.cos(t), TOR_Z + TOR_r * 1.12 * math.sin(t)))
    lathe("Vessel", prof, MT["hull"], g, (C.x, C.y, 0), 32, smooth=True)
    # the glowing plasma slit round the outer equator (and the inner one, toward the column)
    K.band("PlasmaSlit", TOR_R + TOR_r + 0.012, TOR_Z - 0.09, TOR_Z + 0.09, MT["core"], g, xy=C, segs=32).visible_shadow = False
    K.band("PlasmaSlitIn", TOR_R - TOR_r - 0.012, TOR_Z - 0.07, TOR_Z + 0.07, MT["core"], g, xy=C, segs=32).visible_shadow = False
    for z in (TOR_Z - 0.13, TOR_Z + 0.13):
        hoop(f"SlitLip{z}", TOR_R + TOR_r + 0.02, z, 0.05, MT["dark"], g, xy=C, segs=32)
    # diagnostic ports on top
    for k in range(6):
        a = 30 + 60 * k
        p = at(a, TOR_R, TOR_Z + TOR_r * 1.1)
        zcyl(f"Port{k}", 0.16, p.z - 0.1, p.z + 0.25, MT["steel"], g, xy=(p.x, p.y), segs=10)
        zcyl(f"PortCap{k}", 0.2, p.z + 0.25, p.z + 0.32, MT["trim"], g, xy=(p.x, p.y), segs=10)


def field_coils(g):
    n = 12
    zi0, zi1 = TOR_Z - TOR_r * 1.25, TOR_Z + TOR_r * 1.25
    for k in range(n):
        a = 360 * k / n
        rot = (0, 0, math.radians(a))
        ri, ro = TOR_R - TOR_r - 0.12, TOR_R + TOR_r + 0.14
        # four legs of the coil frame in its radial plane
        def rp(r, z):
            return at(a, r, z)
        bx(f"CoilIn{k}", (0.2, 0.3, zi1 - zi0), rp(ri, TOR_Z), MT["coil"], g, rot=rot)
        bx(f"CoilOut{k}", (0.24, 0.34, zi1 - zi0 - 0.3), rp(ro, TOR_Z), MT["coil"], g, rot=rot)
        bx(f"CoilTop{k}", (ro - ri + 0.1, 0.3, 0.22), rp((ri + ro) / 2, zi1), MT["coil"], g, rot=rot)
        bx(f"CoilBot{k}", (ro - ri + 0.1, 0.3, 0.22), rp((ri + ro) / 2, zi0), MT["coil"], g, rot=rot)
        # the outer leg's corners rounded off with short diagonal pieces, a bright owner tag on every third
        for j, z in enumerate((zi1 - 0.15, zi0 + 0.15)):
            p0, p1 = rp(ro - 0.25, z + (0.08 if j == 0 else -0.08)), rp(ro, z - (0.22 if j == 0 else -0.22))
            obox(f"CoilCorner{k}{j}", (0.3, (p1 - p0).length + 0.1, 0.2), (p0 + p1) / 2, p1 - p0, MT["coil"], g)
        if k % 3 == 0:
            bx(f"CoilTag{k}", (0.26, 0.36, 0.35), rp(ro + 0.02, TOR_Z + 0.45), MT["owner"], g, rot=rot)
        else:
            bx(f"CoilTag{k}", (0.26, 0.36, 0.18), rp(ro + 0.02, TOR_Z + 0.45), MT["hazard"], g, rot=rot)
    # poloidal field hoops above and below
    for j, z in enumerate((zi1 + 0.28, zi0 - 0.05)):
        hoop(f"PfHoop{j}", TOR_R + 0.35, z, 0.16, MT["copper"], g, xy=C, segs=32)


def core_column(g):
    # stacked glowing segments between dark frames: the warp-core look
    zs = []
    z = FLOOR_Z
    zcyl("ColumnFoot", COL_R + 0.35, FLOOR_Z, FLOOR_Z + 0.35, MT["trim"], g, xy=C, segs=20)
    z = FLOOR_Z + 0.35
    k = 0
    while z < COL_TOP - 0.5:
        h = 0.42 if k % 3 else 0.6
        zcyl(f"ColumnGlow{k}", COL_R, z, z + h, MT["core"], g, xy=C, segs=20)
        zcyl(f"ColumnFrame{k}", COL_R + 0.1, z + h, z + h + 0.12, MT["dark"], g, xy=C, segs=20)
        zs.append(z)
        z += h + 0.12
        k += 1
    for j in range(4):
        a = 45 + 90 * j
        p0 = at(a, COL_R + 0.08, FLOOR_Z + 0.35)
        p1 = at(a, COL_R + 0.08, z)
        beam(f"ColumnSpine{j}", p0, p1, 0.08, MT["steel"], g)
    # constrictor cap on top
    lathe("CoreCap", [(0.0, z), (COL_R + 0.45, z), (COL_R + 0.55, z + 0.3), (COL_R + 0.3, z + 0.65), (0.25, z + 0.9), (0.0, z + 0.95)],
          MT["hull"], g, (C.x, C.y, 0), 24, smooth=False)
    K.band("CoreCapOwner", COL_R + 0.51, z + 0.12, z + 0.26, MT["owner"], g, xy=C, segs=24)
    sphere("CoreCapBeacon", 0.1, P(C.x, C.y, z + 1.0), MT["beacon"], g, 8)
    K.point("CoreLight", P(C.x, C.y - 1.6, 3.6), g, 0.4)
    # the three spinning constrictor rings, each with emitter blocks so the spin shows
    for i, rz in enumerate(RINGS):
        ring = empty(f"Ring{i}", P(C.x, C.y, rz), g)
        r = 1.05 + 0.08 * (1 - i)
        K.ring_mesh(f"Ring{i}Hoop", r - 0.09, r + 0.09, -0.07, 0.07, MT["steel"], ring, segs=28)
        K.band(f"Ring{i}Glow", r - 0.095, -0.03, 0.03, MT["core"], ring, segs=28).visible_shadow = False
        for j in range(6):
            a = math.radians(60 * j + 30 * i)
            bx(f"Ring{i}Emitter{j}", (0.3, 0.22, 0.26), P(r * math.cos(a), r * math.sin(a), 0), MT["accent"] if j % 2 else MT["trim"], ring, 0.02, rot=(0, 0, a))
            sphere(f"Ring{i}Node{j}", 0.07, P((r + 0.15) * math.cos(a), (r + 0.15) * math.sin(a), 0), MT["core"], ring, 8)
        # arms from the hoop in to the column bearing
        for j in range(3):
            a = math.radians(120 * j + 60 * i)
            beam(f"Ring{i}Arm{j}", P((COL_R + 0.13) * math.cos(a), (COL_R + 0.13) * math.sin(a), 0), P((r - 0.09) * math.cos(a), (r - 0.09) * math.sin(a), 0), 0.07, MT["trim"], ring)
        zcyl(f"Ring{i}Bearing", COL_R + 0.13, -0.08, 0.08, MT["dark"], ring, segs=20)


def dome_curve(t):
    """Shield dome meridian at t 0..1 (0 = base): (radius, z)."""
    a = t * math.pi / 2
    return CROWN_R + (DOME_R - CROWN_R) * math.cos(a), FLOOR_Z + DOME_H * math.sin(a)


def dome_frame(g):
    n = 12
    steps = 7
    for k in range(n):
        a = 360 * k / n + 15
        pts = []
        for i in range(steps + 1):
            r, z = dome_curve(i / steps)
            pts.append(at(a, r, z))
        for i, (p0, p1) in enumerate(zip(pts, pts[1:])):
            beam(f"Rib{k}_{i}", p0, p1, 0.17, MT["hull"], g, 0.2)
        bx(f"RibShoe{k}", (0.45, 0.4, 0.3), at(a, DOME_R, FLOOR_Z + 0.15), MT["trim"], g, 0.02, rot=(0, 0, math.radians(a)))
    for j, t in enumerate((0.36, 0.7)):
        r, z = dome_curve(t)
        hoop(f"DomeHoop{j}", r + 0.02, z, 0.13, MT["hull"], g, xy=C, segs=48)
    r, z = dome_curve(1.0)
    hoop("Crown", CROWN_R + 0.05, z, 0.26, MT["trim"], g, xy=C, segs=32)
    K.band("CrownOwner", CROWN_R + 0.19, z - 0.07, z + 0.07, MT["owner"], g, xy=C, segs=32)
    # a ring catwalk inside the crown round the column, reached by a ladder up the back rib
    K.ring_mesh("CrownWalk", COL_R + 0.15, CROWN_R - 0.08, z - 0.2, z - 0.14, MT["grate"], g, xy=C, segs=32)
    for j in range(4):
        a = 45 + 90 * j
        beam(f"CrownSpoke{j}", at(a, COL_R + 0.12, z - 0.22), at(a, CROWN_R, z - 0.22), 0.08, MT["trim"], g)
    for j in range(4):
        a = 90 * j
        p = at(a, dome_curve(1.0)[0] + 0.05, z + 0.15)
        sphere(f"CrownBeacon{j}", 0.07, p, MT["beacon"], g, 8)


def dome_panels(g):
    """Armour panels over the back of the dome (from -10 to 190 degrees round, i.e. not over the front), split into
    rows with a small gap so the seams read, and a lit window band on the lower row."""
    bm = bmesh.new()
    a0, a1 = math.radians(-12), math.radians(192)
    cols = 20
    for row, (t0, t1) in enumerate(((0.04, 0.34), (0.38, 0.68), (0.72, 0.97))):
        rr = 5 if row < 2 else 3
        grid = []
        for i in range(rr + 1):
            t = t0 + (t1 - t0) * i / rr
            r, z = dome_curve(t)
            grid.append([bm.verts.new(at(math.degrees(a0 + (a1 - a0) * j / cols), r - 0.03, z)) for j in range(cols + 1)])
        for i in range(rr):
            for j in range(cols):
                bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = K.mesh_object("DomeShell", bm, MT["shield"], g, smooth=True)
    m = o.modifiers.new("Thick", "SOLIDIFY")
    m.thickness = 0.06
    # side wings: two short panel strips running down the front sides (frames the opening)
    r, z = dome_curve(0.2)
    K.window_ring("DomeWindows", dome_curve(0.2)[0] + 0.02, z - 0.12, z + 0.12, 24, MT["window"], g, frac=0.55, xy=C,
                  skip=tuple(range(13, 24)))


def cooling_towers(g):
    piv = empty("Towers", P(0, 0, SLAB), g)
    for k, c in enumerate(TOWERS):
        def r_at(z):
            return 0.82 + 0.3 * ((z - 2.9) / 2.4) ** 2 if z < 2.9 else 0.82 + 0.16 * ((z - 2.9) / 1.5) ** 2
        zs = [0.55 + (4.4 - 0.55) * i / 10 for i in range(11)]
        outer = [(r_at(z), z) for z in zs]
        inner = [(r_at(z) - 0.07, z) for z in reversed(zs)]
        lathe(f"Tower{k}Shell", outer + inner + [outer[0]], MT["hull"], piv, (c.x, c.y, 0), 24, smooth=True)
        zcyl(f"Tower{k}Basin", 1.25, 0.0, 0.3, MT["concrete"], piv, xy=c, segs=28)
        for j in range(8):
            a = math.radians(45 * j)
            a2 = a + math.radians(22.5)
            pa = c.to_3d() + P(1.05 * math.cos(a), 1.05 * math.sin(a), 0.3)
            pb = c.to_3d() + P(1.08 * math.cos(a2), 1.08 * math.sin(a2), 0.6)
            strut(f"Tower{k}Col{j}a", pa, pb, 0.05, MT["panel"], piv, 6)
            strut(f"Tower{k}Col{j}b", c.to_3d() + P(1.05 * math.cos(a + math.radians(45)), 1.05 * math.sin(a + math.radians(45)), 0.3), pb, 0.05, MT["panel"], piv, 6)
        hoop(f"Tower{k}Lip", 0.98, 4.4, 0.08, MT["trim"], piv, xy=c, segs=28)
        K.band(f"Tower{k}Band", r_at(3.75) + 0.012, 3.65, 3.85, MT["owner"], piv, xy=c, segs=28)
        K.band(f"Tower{k}Band2", r_at(1.3) + 0.012, 1.2, 1.32, MT["accent"], piv, xy=c, segs=28)
        for j in range(2):
            obox(f"Tower{k}Guard{j}", (0.04, 1.9, 0.04), c.to_3d() + P(0, 0, 4.42), P(math.cos(math.radians(90 * j)), math.sin(math.radians(90 * j)), 0), MT["steel"], piv)
        sphere(f"Tower{k}Beacon", 0.07, c.to_3d() + P(0.94, 0, 4.48), MT["beacon"], piv, 8)
        fan = empty(f"Fan{k}", c.to_3d() + P(0, 0, 4.1), piv)
        zcyl(f"Fan{k}Hub", 0.14, -0.1, 0.12, MT["trim"], fan, segs=12)
        for j in range(6):
            a = math.radians(60 * j)
            bx(f"Fan{k}Blade{j}", (0.66, 0.15, 0.025), (0.38 * math.cos(a), 0.38 * math.sin(a), 0.03), MT["dark"], fan, rot=(math.radians(22), 0, a))
        zcyl(f"Fan{k}Motor", 0.12, -0.45, -0.1, MT["dark"], fan, segs=10)
        K.point(f"Vent{k}", c.to_3d() + P(0, 0, 4.6), piv, 0.5)
        ladder(f"Tower{k}Ladder", c.to_3d() + P(0, -1.18, 0.3), 4.4, (0, -1), piv, cage=True)
    # coolant pipes from the dome to each tower basin
    for k, c in enumerate(TOWERS):
        s = 1 if c.x > 0 else -1
        pipe(f"Coolant{k}", [at(90 - s * 55, DOME_R + 0.25, 1.2), P(c.x - s * 1.4, c.y - 0.9, 1.2), P(c.x - s * 1.4, c.y - 0.9, SLAB + 0.6),
                              P(c.x - s * 0.9, c.y - 0.6, SLAB + 0.6)], 0.14, MT["steel"], g)


def radiators(g):
    def bank(name, cx, cy, parent):
        bx(f"{name}Base", (1.0, 1.8, 0.3), P(cx, cy, SLAB + 0.15), MT["trim"], parent, 0.02)
        for j in range(7):
            bx(f"{name}Fin{j}", (0.85, 0.05, 1.5), P(cx, cy - 0.78 + j * 0.26, SLAB + 1.05), MT["steel"], parent)
        for j in (-1, 1):
            strut(f"{name}Header{j}", P(cx + j * 0.3, cy - 0.9, SLAB + 1.85), P(cx + j * 0.3, cy + 0.9, SLAB + 1.85), 0.08, MT["copper"], parent, 8)
        bx(f"{name}Owner", (1.02, 1.82, 0.1), P(cx, cy, SLAB + 0.32), MT["owner"], parent)
    bank("RadL", -5.3, -0.9, g)
    pipe("RadLFeed", [at(180, DOME_R + 0.2, 1.4), P(-4.8, C.y - 0.0, 1.4), P(-4.8, -0.9, 2.15)], 0.09, MT["steel"], g)
    opt = empty("OptRadiator", P(0, 0, 0), g)
    bank("RadL2", -5.3, 1.3, opt)


def conduits(g):
    """Glowing plasma conduits from the vessel's front-right out through the dome to the power converter."""
    pl = MT["plasma"]
    for k, (a, z) in enumerate(((-38, TOR_Z + 0.25), (-50, TOR_Z - 0.25))):
        p0 = at(a, TOR_R + TOR_r + 0.1, z)
        p1 = at(a, DOME_R + 0.6, z)
        p2 = P(CONV.x - 0.25 + 0.5 * k, CONV.y + 0.15, z)
        p3 = P(CONV.x - 0.25 + 0.5 * k, CONV.y + 0.15, SLAB + 0.95)
        pipe(f"Conduit{k}", [p0, p1, p2, p3], 0.13, pl, g, segs=10)
        # dark sleeves along the glowing tube, so the glow shows between them
        for i, (a3, b3) in enumerate(((p0, p1), (p1, p2), (p2, p3))):
            n = max(1, int((b3 - a3).length / 0.45))
            for j in range(n):
                t = (j + 0.5) / n
                cen = a3.lerp(b3, t)
                d = (b3 - a3).normalized()
                o = cylinder(f"Sleeve{k}_{i}_{j}", 0.165, 0.22, cen, MT["trim"], g, axis="Z", segments=8)
                o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    # the power converter box the conduits feed
    c = P(CONV.x, CONV.y, SLAB)
    bx("Converter", (1.3, 0.9, 1.0), c + P(0, 0, 0.5), MT["accent"], g, 0.04)
    bx("ConverterOwner", (1.32, 0.92, 0.1), c + P(0, 0, 0.82), MT["owner"], g)
    bx("ConverterVent", (0.9, 0.04, 0.4), c + P(0, -0.46, 0.45), MT["grate"], g)
    for k in range(3):
        bx(f"ConverterLed{k}", (0.08, 0.03, 0.06), c + P(-0.3 + k * 0.15, -0.47, 0.85), MT["status"], g).visible_shadow = False


def fuel_tanks(g):
    for k, c in enumerate(TANKS):
        parent = g if k == 0 else empty("OptTank", P(0, 0, 0), g)
        cz = SLAB + 1.55
        sphere(f"Tank{k}", 0.95, P(c.x, c.y, cz), MT["deut"], parent, 24)
        K.band(f"Tank{k}Band", 0.96, cz - 0.08, cz + 0.08, MT["owner"], parent, xy=c, segs=24)
        for j in range(4):
            a = math.radians(45 + 90 * j)
            p = c.to_3d() + P(0.62 * math.cos(a), 0.62 * math.sin(a), 0)
            beam(f"Tank{k}Leg{j}", P(p.x, p.y, SLAB), P(p.x * 0.9 + c.x * 0.1, p.y * 0.9 + c.y * 0.1, cz - 0.3), 0.12, MT["hull"], parent)
        hoop(f"Tank{k}Girth", 0.85, cz - 0.55, 0.08, MT["trim"], parent, xy=c, segs=24)
        zcyl(f"Tank{k}Valve", 0.12, cz + 0.85, cz + 1.15, MT["steel"], parent, xy=c, segs=8)
        hoop(f"Tank{k}Wheel", 0.15, cz + 1.18, 0.03, MT["valve"], parent, xy=c, segs=10)
        pipe(f"Tank{k}Feed", [c.to_3d() + P(0, -0.6, SLAB + 0.5), P(c.x, 4.45, SLAB + 0.5), P(c.x * 0.3, 4.45, SLAB + 0.5)], 0.07, MT["steel"], parent)
    bx("TankPlate", (1.1, 0.04, 0.3), TANKS[0].to_3d() + P(0, -0.93, SLAB + 1.0), MT["label"], g)
    K.marker("LabelTank", TANKS[0].to_3d() + P(0, -0.955, SLAB + 1.0), (math.radians(90), 0, 0), 1.0, 0.18, g)


def control(g):
    c = CONTROL.to_3d()
    w, d = 2.6, 1.7
    yaw = math.radians(-25)
    R = Matrix.Rotation(yaw, 4, "Z")
    holder = empty("ControlFrame", c, g)
    holder.rotation_euler.z = yaw
    h = holder
    bx("CtlBase", (w + 0.1, d + 0.1, 0.25), P(0, 0, SLAB + 0.125), MT["trim"], h, 0.02)
    bx("CtlBody", (w, d, 1.25), P(0, 0, SLAB + 0.25 + 0.625), MT["hull"], h, 0.04)
    bx("CtlAccent", (w + 0.02, d + 0.02, 0.34), P(0, 0, SLAB + 0.52), MT["accent"], h)
    bx("CtlGlass", (w - 0.06, d - 0.06, 0.72), P(0, 0, SLAB + 1.86), MT["glass"], h, 0.02)
    for k in range(4):
        bx(f"CtlMullion{k}", (0.07, 0.07, 0.74), P((k - 1.5) * 0.84, -d / 2 + 0.03, SLAB + 1.86), MT["trim"], h)
    K.pane_row("CtlLit", (-w / 2 + 0.15, -d / 2 + 0.025, 0), (1, 0, 0), None, w - 0.3, SLAB + 1.6, SLAB + 2.08, 3, MT["window"], h, frac=0.82)
    bx("CtlBoard", (w - 0.5, 0.03, 0.12), P(0, -d / 2 + 0.02, SLAB + 1.55), MT["status"], h).visible_shadow = False
    bx("CtlRoof", (w + 0.36, d + 0.36, 0.14), P(0, 0, SLAB + 2.3), MT["panel"], h, 0.03)
    bx("CtlFascia", (w + 0.38, 0.05, 0.3), P(0, -d / 2 - 0.2, SLAB + 2.27), MT["dark"], h)
    K.marker("SignFront", P(0, -d / 2 - 0.23, SLAB + 2.27), (math.radians(90), 0, 0), 2.5, 0.2, h)
    bx("CtlOwner", (w + 0.02, d + 0.02, 0.11), P(0, 0, SLAB + 1.42), MT["owner"], h)
    bx("CtlDoor", (0.05, 0.75, 1.15), P(w / 2 + 0.01, 0.2, SLAB + 0.83), MT["trim"], h)
    bx("CtlDoorLamp", (0.06, 0.4, 0.06), P(w / 2 + 0.03, 0.2, SLAB + 1.48), MT["window"], h).visible_shadow = False
    top = SLAB + 2.37
    bx("CtlAc", (0.85, 0.65, 0.38), P(-0.5, 0.25, top + 0.19), MT["panel"], h, 0.03)
    zcyl("CtlAcFan", 0.23, top + 0.38, top + 0.41, MT["dark"], h, xy=(-0.5, 0.25), segs=12)
    zcyl("CtlMast", 0.045, top, top + 2.2, MT["steel"], h, xy=(0.9, 0.45), segs=6)
    sphere("CtlMastLamp", 0.06, P(0.9, 0.45, top + 2.25), MT["beacon"], h, 8)
    dish = lathe("CtlDish", [(0, 0), (0.12, 0.02), (0.25, 0.06), (0.36, 0.13), (0.34, 0.16), (0.24, 0.09), (0.12, 0.05), (0, 0.03)], MT["hull"], h, (0, 0, 0), 20)
    dish.matrix_basis = Matrix.Translation(P(0.9, 0.45, top + 1.5)) @ Matrix.Rotation(math.radians(200), 4, "Z") @ Matrix.Rotation(math.radians(60), 4, "X")
    # a covered walkway from the control room to the reactor floor's steps
    pipe("CtlCable", [P(w / 2 - 0.2, d / 2, SLAB + 0.4), P(w / 2 + 1.2, d / 2 + 1.2, SLAB + 0.4)], 0.07, MT["cable"], h)


def transformer(g):
    c = XFMR.to_3d()
    bx("XfmrPad", (2.4, 1.8, 0.2), c + P(0, 0, SLAB + 0.1), MT["concrete"], g, 0.02)
    bx("Xfmr", (1.5, 1.0, 1.3), c + P(0, 0, SLAB + 0.2 + 0.65), MT["trim"], g, 0.04)
    for j in range(8):
        bx(f"XfmrFin{j}", (0.05, 1.12, 1.0), c + P(-0.7 + j * 0.2, 0, SLAB + 0.75), MT["steel"], g)
    bx("XfmrOwner", (1.52, 1.02, 0.1), c + P(0, 0, SLAB + 1.35), MT["owner"], g)
    bushings = []
    for j in range(3):
        b = c + P(-0.45 + j * 0.45, 0.1, SLAB + 1.5)
        for i in range(5):
            zcyl(f"Bushing{j}_{i}", 0.13 - 0.01 * (i % 2), b.z + i * 0.16, b.z + i * 0.16 + 0.12, MT["insulator"], g, xy=(b.x, b.y), segs=10)
        zcyl(f"BushingTop{j}", 0.05, b.z + 0.8, b.z + 0.95, MT["steel"], g, xy=(b.x, b.y), segs=6)
        bushings.append(b + P(0, 0, 0.95))
    # fence round the yard (posts + rails)
    pts = [(c.x - 1.35, c.y - 1.05), (c.x + 1.35, c.y - 1.05), (c.x + 1.35, c.y + 1.05)]
    for k, (a, b) in enumerate(zip(pts, pts[1:])):
        a3, b3 = P(a[0], a[1], SLAB), P(b[0], b[1], SLAB)
        n = 4
        for i in range(n + 1):
            p = a3.lerp(b3, i / n)
            bx(f"Fence{k}_{i}", (0.05, 0.05, 1.1), p + P(0, 0, 0.55), MT["steel"], g)
        for zz in (0.5, 1.05):
            beam(f"FenceRail{k}_{zz}", a3 + P(0, 0, zz), b3 + P(0, 0, zz), 0.03, MT["steel"], g)
    bx("FenceSign", (0.4, 0.03, 0.3), P(c.x, c.y - 1.07, SLAB + 0.8), MT["hazard"], g)
    # the pylon (scale pivot at its foot): a tapered lattice with two cross-arms and insulator strings
    piv = empty("Pylon", P(PYLON.x, PYLON.y, SLAB), g)
    H = 7.2
    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(f"PylonLeg{sx}{sy}", P(sx * 0.55, sy * 0.55, 0), P(sx * 0.18, sy * 0.18, H), 0.1, MT["hull"], piv)
    for i, z in enumerate((1.4, 2.8, 4.2, 5.6)):
        w = 0.55 + (0.18 - 0.55) * z / H
        for k, (a, b) in enumerate((((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1)))):
            beam(f"PylonGirt{i}{k}", P(a[0] * w, a[1] * w, z), P(b[0] * w, b[1] * w, z), 0.06, MT["hull"], piv)
            w2 = 0.55 + (0.18 - 0.55) * (z - 1.4) / H
            strut(f"PylonX{i}{k}", P(a[0] * w2, a[1] * w2, z - 1.4), P(b[0] * w, b[1] * w, z), 0.025, MT["trim"], piv, 4)
    for i, (z, span) in enumerate(((H - 0.2, 0.95), (H - 1.4, 1.1))):
        beam(f"PylonArm{i}", P(-span, 0, z), P(span, 0, z), 0.12, MT["hazard" if i == 0 else "hull"], piv)
        for s in (-1, 1):
            for j in range(3):
                zcyl(f"PylonIns{i}{s}{j}", 0.07, z - 0.5 + j * 0.15, z - 0.4 + j * 0.15, MT["insulator"], piv, xy=(s * span * 0.9, 0), segs=8)
    sphere("PylonBeacon", 0.08, P(0, 0, H + 0.1), MT["beacon"], piv, 8)
    bx("PylonPlate", (0.6, 0.04, 0.3), P(0, -0.47, 1.5), MT["label"], piv)
    K.marker("LabelPylon", P(0, -0.5, 1.5), (math.radians(90), 0, 0), 0.55, 0.14, piv)
    # cables: bushings up to the lower arm; the upper arm's lines run back to a cable head where they go underground
    pw = Vector((PYLON.x, PYLON.y, SLAB))
    for j, b in enumerate(bushings[1:]):
        s = -1 if j == 0 else 1
        K.sagging(f"Lead{j}", b, pw + P(s * 1.1 * 0.9, 0, H - 1.9), 0.3, 0.02, MT["cable"], g)
    term = P(PYLON.x, 0.3, SLAB)
    bx("Terminal", (1.1, 0.6, 1.1), term + P(0, 0, 0.55), MT["trim"], g, 0.03)
    bx("TerminalOwner", (1.12, 0.62, 0.1), term + P(0, 0, 0.9), MT["owner"], g)
    for s in (-1, 1):
        zcyl(f"TermIns{s}", 0.06, SLAB + 1.1, SLAB + 1.55, MT["insulator"], g, xy=(term.x + s * 0.35, term.y), segs=8)
        K.sagging(f"Line{s}", pw + P(s * 0.95 * 0.9, 0, H - 0.7), term + P(s * 0.35, 0, 1.55), 0.35, 0.02, MT["cable"], g)


def lights(g):
    piv = empty("Masts", P(0, 0, SLAB), g)
    K.light_mast("MastA", -5.3, 3.1, 0.0, 6.4, -30, piv)
    K.light_mast("MastB", 1.6, -5.5, 0.0, 5.6, 100, piv)
    # floor lamps aimed at the vessel
    for k in range(4):
        a = -135 + 90 * k
        p = at(a, FLOOR_R - 0.25, FLOOR_Z)
        bx(f"FloorLamp{k}", (0.3, 0.2, 0.2), p + P(0, 0, 0.12), MT["dark"], g, rot=(0, 0, math.radians(a)))
        bx(f"FloorLens{k}", (0.04, 0.16, 0.12), at(a, FLOOR_R - 0.4, FLOOR_Z + 0.13), MT["lamp"], g, rot=(0, 0, math.radians(a))).visible_shadow = False
    K.point("SparkPoint", P(XFMR.x - 1.45, XFMR.y + 1.2, SLAB + 0.9), g)


BUILDERS = [plinth, reactor_floor, vessel, field_coils, core_column, dome_frame, dome_panels, cooling_towers, radiators,
            conduits, fuel_tanks, control, transformer, lights]

PIVOTS = {"Ring0", "Ring1", "Ring2", "Fan0", "Fan1", "Towers", "Pylon", "Masts", "OptTank", "OptRadiator"}
SEPARATE = set()
EMPTIES = {"CoreLight", "Vent0", "Vent1", "SparkPoint", "SignFront", "LabelTank", "LabelPylon", "RoofSpot0"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("REACTOR 01", (0.92, 0.93, 0.95)), "LabelTank": ("DEUTERIUM", (0.08, 0.08, 0.1)),
         "LabelPylon": ("HIGH VOLTAGE", (0.08, 0.08, 0.1))}


def main():
    materials()
    root = K.assemble("fusion_reactor", PIECES, BUILDERS)
    night = [("NCore", (0.4, 0.8, 1.0), 900, P(C.x, C.y - 1.0, 3.6)),
             ("NMastA", (1.0, 0.95, 0.85), 260, P(-4.8, 2.6, 6.4)),
             ("NMastB", (1.0, 0.95, 0.85), 260, P(1.6, -4.9, 5.6)),
             ("NCtl", (1.0, 0.75, 0.45), 90, P(CONTROL.x + 0.5, CONTROL.y - 1.5, 1.6))]
    shots = {"hero": ((15.5, -19.0, 11.5), (0.0, 0.3, 3.4), 32),
             "close": ((5.5, -9.0, 5.4), (0.0, 0.4, 3.2), 28),
             "night": ((-9.0, -19.0, 10.5), (0.0, 0.3, 3.4), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 17),
             "back": ((-14.0, 17.0, 10.0), (0.0, 0.5, 3.4), 32)}
    K.finish(root, KEEP, SEPARATE, "ReactorGrime", SLAB, soot=None, shots=shots, night_lights=night, signs=SIGNS,
             rover_at=(-9.0, -6.6, 160))


main()
