"""Research Lab Mk I-V mockups (`science_lab`; owner 2026-11-06: full details and Mk I through Mk V).

One silhouette, five marks: a lab block under a glass dome, a sensor mast with a dish at the back, and the
containment experiment out front ("Spin": the glowing ring that turns while it researches).
  Mk I   Field lab: one prefab module with a small glass dome, a whip mast with a small dish, the containment ring on a
         plinth, a solar row, sample cases.
  Mk II  Survey lab: a second module at the back, a bigger dome, a taller lattice mast with a proper dish, a weather
         mast, sample tanks, field emitters round the ring.
  Mk III Research station: ribbed observation dome, a greenhouse bio-dome, a phased sensor array on the roof, four
         field coils round a bigger ring, Starfleet white + navy.
  Mk IV  Science complex: a second dish on its own pedestal, the isolinear data core (glowing stack), a glass
         walkway tube to the greenhouse, a subspace antenna spire.
  Mk V   Starfleet science station: a saucer on a column rising out of the lab (observation lounge), three domes,
         the big dish, a large containment ring with a warp-field column, light strips and nav lights.
Everything stays inside the lanes' footprint_radius 7 (corners ~6.8).

GAME PARTS (BuildingModels.ResearchLab / PlanetView): "Spin" = the containment ring group, turned about Godot Y while
it works (both dishes could take "Dish"/"Dish1" for a slow scan; needs code). Markers SignFront, SparkPoint.

Run: /home/user/.bpyvenv/bin/python blender/build_science_lab_marks.py --preview blender/previews/marks/science_lab
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
import marks_kit as MK  # noqa: E402
from building_kit import MT, P, bx, beam, zcyl, sphere, strut, empty, cylinder  # noqa: E402

SLAB = 0.3


def module(g, name, x, y, w, d, h, skin, accent, windows=3, z=SLAB):
    bx(f"{name}Skid", (w + 0.2, d + 0.2, 0.25), P(x, y, z + 0.12), MT["trim"], g, 0.03)
    bx(f"{name}Body", (w, d, h), P(x, y, z + 0.25 + h / 2), skin, g, 0.06)
    bx(f"{name}Band", (w + 0.02, d + 0.02, 0.35), P(x, y, z + 0.6), accent, g)
    bx(f"{name}Owner", (w + 0.02, d + 0.02, 0.1), P(x, y, z + 0.25 + h - 0.35), MT["owner"], g)
    bx(f"{name}Roof", (w + 0.16, d + 0.16, 0.12), P(x, y, z + 0.25 + h + 0.06), MT["panel"], g, 0.02)
    K.pane_row(f"{name}Win", (x - w / 2 + 0.2, y - d / 2 - 0.01, 0), (1, 0, 0), None, w - 0.4, z + 1.2, z + 1.85, windows, MT["window"], g, frac=0.72)
    return z + 0.25 + h + 0.12


def glass_dome(g, name, x, y, z, r, ribs=0, h=None, mat=None):
    h = h or r * 0.9
    zcyl(f"{name}Ring", r + 0.12, z, z + 0.28, MT["trim"], g, xy=(x, y), segs=32)
    K.OP["dome"](f"{name}Glass", r, h, mat or MT["glass"], g, (x, y, z + 0.28), segs=32)
    if ribs:
        for i in range(ribs):
            a = math.pi * i / ribs
            pts = []
            for j in range(11):
                t = math.pi * j / 10
                c, s = math.cos(t), math.sin(t)
                pts.append(P(x + math.cos(a) * r * 1.01 * c, y + math.sin(a) * r * 1.01 * c, z + 0.28 + h * 1.01 * s))
            K.pipe(f"{name}Rib{i}", pts, 0.05, MT["white"], g, segs=6, joints=False)
        zcyl(f"{name}Cap", 0.3, z + 0.28 + h - 0.02, z + 0.28 + h + 0.12, MT["white"], g, xy=(x, y), segs=16)
    # something lit inside
    zcyl(f"{name}Bench", r * 0.5, z, z + 0.7, MT["cyan"] if r > 1.4 else MT["window"], g, xy=(x, y), segs=16)


def containment(g, x, y, r, mark):
    """The experiment: a plinth, emitters and the glowing ring + core in "Spin"."""
    ph = 0.8 + 0.15 * mark
    zcyl("PlinthBase", r * 1.35, SLAB, SLAB + 0.25, MT["trim"], g, xy=(x, y), segs=24)
    zcyl("Plinth", r * 1.1, SLAB + 0.25, SLAB + ph, MT["accent"] if mark < 3 else MT["navy"], g, xy=(x, y), segs=24, radius2=r * 0.9)
    K.ring_mesh("PlinthLip", r * 0.95, r * 1.2, SLAB + ph, SLAB + ph + 0.08, MT["white"], g, xy=(x, y), segs=24)
    cz = SLAB + ph + r * 1.05
    spin = empty("Spin", P(x, y, cz), g)
    tor = K.OP["torus"]("SpinRing", r, r * 0.13, (0, 0, 0), MT["cyan"], spin, segs=40)
    tor.rotation_euler.x = math.radians(70)
    if mark >= 3:
        tor2 = K.OP["torus"]("SpinRing2", r * 0.78, r * 0.08, (0, 0, 0), MT["cyan"], spin, segs=32)
        tor2.rotation_euler.y = math.radians(70)
    sphere("SpinCore", r * 0.32, (0, 0, 0), MT["cyan"], spin, 16)
    posts = 0 if mark == 1 else 3 if mark == 2 else 4
    for i in range(posts):
        a = math.radians(90 * i + 45 if posts == 4 else 120 * i + 90)
        px, py = x + r * 1.45 * math.cos(a), y + r * 1.45 * math.sin(a)
        zcyl(f"Emitter{i}", 0.12, SLAB, cz + r * 0.6, MT["white"] if mark >= 3 else MT["steel"], g, xy=(px, py), segs=10)
        if mark >= 3:
            for j in range(3):
                zcyl(f"Coil{i}{j}", 0.2, cz - 0.6 + j * 0.35, cz - 0.45 + j * 0.35, MT["copper"], g, xy=(px, py), segs=12)
        sphere(f"EmitterTip{i}", 0.13, P(px, py, cz + r * 0.62), MT["cyan"], g, 10)
    if mark == 5:
        zcyl("FieldColumn", r * 0.16, cz, cz + 1.9, MT["cyan"], g, xy=(x, y), segs=12, radius2=0.02)
    return cz


def mast(g, x, y, h, dish_r, mark, lattice=False, tilt=35, yaw=200, name="Mast"):
    if lattice:
        K.truss(f"{name}Truss", P(x, y, SLAB), P(x, y, SLAB + h), 0.55, 0.06, MT["white"] if mark >= 3 else MT["steel"], g)
    else:
        zcyl(f"{name}Pole", 0.16, SLAB, SLAB + h, MT["hull"], g, xy=(x, y), segs=10, radius2=0.1)
    zcyl(f"{name}Foot", 0.35, SLAB, SLAB + 0.35, MT["hazard"], g, xy=(x, y), segs=12)
    MK.dish(f"{name}Dish", dish_r, g, P(x, y, SLAB + h + dish_r * 0.3), tilt=tilt, yaw=yaw)
    sphere(f"{name}Beacon", 0.08, P(x, y, SLAB + h + dish_r * 0.9 + 0.3), MT["beacon"], g, 8)


def solar_row(g, x, y, n, yaw=0):
    MK.solar_array(f"Solar{x}{y}", x, y, SLAB, g, n=n, w=1.0, d=0.8, tilt=30, yaw=yaw)


def cases(g, x, y, n):
    for i in range(n):
        bx(f"Case{x}{i}", (0.6, 0.4, 0.35), P(x + (i % 2) * 0.66, y + (i // 2) * 0.46, SLAB + 0.18 + (0.36 if i >= 4 else 0)),
           MT["hazard"] if i % 3 == 0 else MT["accent"], g, 0.03)


def tanks(g, x, y, n, h=1.6):
    for i in range(n):
        zcyl(f"Tank{x}{i}", 0.32, SLAB, SLAB + h, MT["tank"], g, xy=(x + i * 0.75, y), segs=16)
        sphere(f"TankTop{x}{i}", 0.32, P(x + i * 0.75, y, SLAB + h), MT["tank"], g, 12)
        zcyl(f"TankBand{x}{i}", 0.33, SLAB + h * 0.6, SLAB + h * 0.6 + 0.12, MT["owner"], g, xy=(x + i * 0.75, y), segs=16)


def weather_mast(g, x, y, h):
    zcyl("WxPole", 0.05, SLAB, SLAB + h, MT["steel"], g, xy=(x, y), segs=6)
    for i in range(3):
        a = math.radians(120 * i)
        strut(f"WxCup{i}", P(x, y, SLAB + h), P(x + 0.35 * math.cos(a), y + 0.35 * math.sin(a), SLAB + h), 0.02, MT["steel"], g, 4)
        sphere(f"WxCupB{i}", 0.08, P(x + 0.35 * math.cos(a), y + 0.35 * math.sin(a), SLAB + h), MT["white"], g, 8)
    bx("WxBox", (0.3, 0.2, 0.4), P(x, y - 0.1, SLAB + 1.2), MT["white"], g)


def data_core(g, x, y, h):
    zcyl("CoreBase", 0.9, SLAB, SLAB + 0.4, MT["trim"], g, xy=(x, y), segs=20)
    zcyl("CoreColumn", 0.62, SLAB + 0.4, SLAB + h, MT["white"], g, xy=(x, y), segs=20)
    for i in range(5):
        z = SLAB + 0.8 + i * (h - 1.2) / 5
        zcyl(f"CoreBand{i}", 0.64, z, z + 0.16, MT["cyan"], g, xy=(x, y), segs=20)
    zcyl("CoreCap", 0.75, SLAB + h, SLAB + h + 0.2, MT["navy"], g, xy=(x, y), segs=20)
    sphere("CoreLight", 0.1, P(x, y, SLAB + h + 0.3), MT["beacon"], g, 8)


def saucer(g, x, y, z, r):
    K.lathe("SaucerHull", [(0, -0.35 * r), (r * 0.35, -0.3 * r), (r * 0.8, -0.14 * r), (r, -0.02 * r), (r * 0.98, 0.04 * r),
                            (r * 0.7, 0.16 * r), (r * 0.35, 0.24 * r), (0, 0.27 * r)], MT["white"], g, (x, y, z), segs=40)
    K.window_ring("SaucerWin", r * 0.985, z - 0.03 * r, z + 0.02 * r, 24, MT["window"], g, frac=0.55, xy=(x, y))
    K.ring_mesh("SaucerBand", r * 0.55, r * 0.75, z + 0.19 * r - 0.04, z + 0.19 * r, MT["navy"], g, xy=(x, y), segs=40)
    zcyl("SaucerBridge", r * 0.25, z + 0.25 * r, z + 0.38 * r, MT["white"], g, xy=(x, y), segs=20)
    zcyl("SaucerBridgeGlass", r * 0.2, z + 0.38 * r, z + 0.44 * r, MT["glass"], g, xy=(x, y), segs=20)
    K.window_ring("SaucerLowerWin", r * 0.6, z - 0.24 * r, z - 0.19 * r, 16, MT["strip"], g, frac=0.5, xy=(x, y))
    for i in range(4):
        a = math.radians(90 * i + 45)
        sphere(f"SaucerNav{i}", 0.07, P(x + r * math.cos(a), y + r * math.sin(a), z), MT["beacon"] if i % 2 else MT["strip"], g, 8)


def sign(M, g, x, y, z, w, text):
    bx("SignBoard", (w + 0.1, 0.06, 0.32), P(x, y - 0.03, z), MT["dark"], g)
    e = K.marker("SignFront", P(x, y - 0.07, z), (math.radians(90), 0, 0), w, 0.22, g)
    M.sign(e, text)


# ---------------------------------------------------------------- the marks


def build(mark, M):
    skin = MT["prefab"] if mark == 1 else MT["hull"] if mark == 2 else MT["white"]
    accent = MT["accent"] if mark <= 2 else MT["navy"]

    g = M.piece("Foundation")
    if mark <= 2:
        MK.slab("Slab", [8.6, 10.4][mark - 1], [8.0, 9.6][mark - 1], SLAB, g, xy=(0, 0.2), mat=MT["concrete"])
    else:
        MK.slab("SlabEdge", 12.0, 11.0, SLAB - 0.12, g, xy=(0, 0.2), chamfer=2.6, mat=MT["dark"])
        MK.slab("Slab", 11.7, 10.7, SLAB, g, xy=(0, 0.2), chamfer=2.5, mat=MT["concrete"])
        K.flat_path("Walk", [(-1.4, -4.8), (-1.4, -1.5)], 1.1, MT["white"], SLAB + 0.004, g)
    K.point("SparkPoint", P(-1.2, 0.4, SLAB + 2.0), g)
    for k, (x, y, yaw) in enumerate([(-7.4, 0.5, 90), (7.4, 0.5, -90), (0.0, 7.2, 0), (-5.6, -5.0, 90), (5.6, -5.0, -90)]):
        e = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e.rotation_euler.z = math.radians(yaw)

    # --- 1 the lab block (modules)
    g = M.piece("Lab")
    if mark == 1:
        top = module(g, "Lab", -1.2, 0.4, 4.6, 3.4, 2.5, skin, accent, 3)
        dome_at, dome_r = (-1.2, 0.4, top), 1.2
    elif mark == 2:
        top = module(g, "Lab", -1.4, -0.2, 5.0, 3.6, 2.8, skin, accent, 3)
        module(g, "Wing", -1.0, 3.0, 4.0, 2.6, 2.3, skin, accent, 2)
        dome_at, dome_r = (-1.4, -0.2, top), 1.6
    else:
        top = module(g, "Lab", -1.4, -0.3, 5.4, 3.8, 3.0 if mark == 3 else 3.3, skin, accent, 4)
        module(g, "Wing", -0.8, 3.1, 4.6, 2.6, 2.6, skin, accent, 3)
        dome_at, dome_r = (-1.4, -0.3, top), 2.0 if mark < 5 else 1.8
        bx("Entry", (1.4, 1.0, 2.2), P(-1.4, -2.65, SLAB + 1.1), MT["white"], g, 0.05)
        bx("EntryGlass", (1.0, 0.04, 1.6), P(-1.4, -3.16, SLAB + 1.0), MT["glass"], g)
        bx("EntryLit", (0.9, 0.02, 1.4), P(-1.4, -3.18, SLAB + 1.0), MT["window"], g).visible_shadow = False
    sign(M, g, -1.4 if mark > 1 else -1.2, -2.05 if mark >= 3 else (-1.31 if mark == 1 else -1.98), top - 0.55, 2.6,
         ["", "FIELD LAB", "SURVEY LAB 01", "RESEARCH 01", "SCIENCE 01", "STARFLEET SCIENCE"][mark])

    # --- 2 the dome(s)
    g = M.piece("Domes")
    glass_dome(g, "Dome", *dome_at, dome_r, ribs=0 if mark < 3 else 8)
    if mark >= 3:
        glass_dome(g, "BioDome", -4.6, 3.4, SLAB, 1.3, ribs=6, h=1.3)
        for i in range(5):
            sphere(f"Plant{i}", 0.25 + 0.05 * (i % 2), P(-4.6 + 0.6 * math.cos(i * 1.3), 3.4 + 0.6 * math.sin(i * 1.3), SLAB + 0.6 + 0.1 * i),
                   K.mat(f"plant{mark}{i}", f"Plant{i}", (0.18, 0.42 + 0.05 * i, 0.16), 0.0, 0.7), g, 10)
    if mark == 5:
        glass_dome(g, "Dome3", 2.0, 4.2, SLAB + 2.5, 1.1, ribs=6)
        bx("Dome3Base", (2.4, 2.4, 2.5), P(2.0, 4.2, SLAB + 1.25), MT["white"], g, 0.08)
        bx("Dome3Navy", (2.42, 2.42, 0.25), P(2.0, 4.2, SLAB + 1.9), MT["navy"], g)

    # --- 3 sensors: mast(s) + dish(es) + arrays
    g = M.piece("Sensors")
    if mark == 1:
        mast(g, 2.8, 2.6, 4.2, 0.6, mark)
    elif mark == 2:
        mast(g, 3.0, 2.8, 6.0, 0.95, mark, lattice=True)
        weather_mast(g, -4.4, -1.6, 3.2)
    elif mark == 3:
        mast(g, 3.2, 3.0, 6.8, 1.15, mark, lattice=True)
        for i in range(4):   # phased sensor array on the wing roof
            bx(f"Array{i}", (0.7, 0.06, 1.0), P(-2.4 + i * 0.85, 3.1, SLAB + 3.6), MT["white"], g, 0.02, rot=(math.radians(-20), 0, 0))
            bx(f"ArrayFace{i}", (0.6, 0.02, 0.85), P(-2.4 + i * 0.85, 3.06, SLAB + 3.6), MT["navy"], g, rot=(math.radians(-20), 0, 0))
        weather_mast(g, -4.9, -1.8, 3.4)
    elif mark == 4:
        mast(g, 3.4, 3.2, 7.2, 1.25, mark, lattice=True)
        zcyl("Dish2Ped", 0.5, SLAB, SLAB + 1.6, MT["white"], g, xy=(-4.7, -2.4), segs=16)
        MK.dish("Dish2", 1.0, g, P(-4.7, -2.4, SLAB + 2.1), tilt=50, yaw=160)
        K.truss("Spire", P(1.4, 3.6, SLAB + 2.9), P(1.4, 3.6, SLAB + 9.0), 0.35, 0.05, MT["white"], g)
        for i in range(3):
            K.hoop(f"SpireRing{i}", 0.45 - 0.08 * i, SLAB + 6.0 + i * 1.0, 0.05, MT["cyan"], g, xy=(1.4, 3.6), segs=16)
        sphere("SpireTip", 0.1, P(1.4, 3.6, SLAB + 9.1), MT["beacon"], g, 8)
        for i in range(4):
            bx(f"Array{i}", (0.7, 0.06, 1.0), P(-2.0 + i * 0.85, 3.1, SLAB + 3.6), MT["white"], g, 0.02, rot=(math.radians(-20), 0, 0))
            bx(f"ArrayFace{i}", (0.6, 0.02, 0.85), P(-2.0 + i * 0.85, 3.06, SLAB + 3.6), MT["navy"], g, rot=(math.radians(-20), 0, 0))
    else:
        mast(g, 4.2, 2.6, 6.0, 1.45, mark, lattice=True)
        zcyl("Dish2Ped", 0.5, SLAB, SLAB + 1.6, MT["white"], g, xy=(-4.8, -2.5), segs=16)
        MK.dish("Dish2", 1.0, g, P(-4.8, -2.5, SLAB + 2.1), tilt=50, yaw=160)

    # --- 4 the experiment ("Spin")
    g = M.piece("Containment")
    r = [0, 0.65, 0.8, 0.95, 1.0, 1.2][mark]
    containment(g, 2.6, -2.2, r, mark)
    M.info["keep"] |= {"Spin"}

    # --- 5 support: power, tanks, data core, walkway, saucer tower
    g = M.piece("Support")
    if mark == 1:
        solar_row(g, -1.6, -3.0, 3)
        cases(g, 2.6, 0.2, 3)
    elif mark == 2:
        solar_row(g, -1.2, -3.6, 3)
        tanks(g, 3.0, 0.2, 2)
        cases(g, 4.2, -0.8, 3)
    elif mark == 3:
        tanks(g, 3.0, 0.4, 3, h=1.9)
        MK.solar_array("SolarRoof", -1.4, 1.0, SLAB + 3.6, g, n=3, w=0.9, d=0.7, tilt=10, yaw=180)
    elif mark == 4:
        data_core(g, 4.6, 0.2, 3.6)
        tanks(g, 2.6, 0.9, 2, h=1.9)
        # glass walkway tube from the wing to the greenhouse
        K.cylinder("Tube", 0.55, 1.5, P(-3.6, 3.3, SLAB + 1.0), MT["glass"], g, axis="X", segments=16)
        for i in range(3):
            K.cylinder(f"TubeRib{i}", 0.6, 0.1, P(-4.2 + i * 0.6, 3.3, SLAB + 1.0), MT["white"], g, axis="X", segments=16)
    else:
        data_core(g, 4.8, -0.2, 3.8)
        K.cylinder("Tube", 0.55, 1.5, P(-3.6, 3.3, SLAB + 1.0), MT["glass"], g, axis="X", segments=16)
        # the saucer tower: a column out of the lab, the observation saucer on top
        cx, cy = -1.4, 0.9
        zcyl("Column", 0.75, SLAB + 3.2, SLAB + 7.6, MT["white"], g, xy=(cx, cy), segs=24, radius2=0.55)
        zcyl("ColumnNavy", 0.72, SLAB + 4.6, SLAB + 4.9, MT["navy"], g, xy=(cx, cy), segs=24)
        K.window_ring("ColumnLit", 0.66, SLAB + 5.4, SLAB + 6.8, 6, MT["strip"], g, frac=0.25, xy=(cx, cy))
        saucer(g, cx, cy, SLAB + 8.2, 2.7)
        for s in (-1, 1):
            beam(f"Pylon{s}", P(cx + s * 0.5, cy, SLAB + 6.2), P(cx + s * 1.6, cy, SLAB + 7.7), 0.18, MT["white"], g)

    # --- 6 lights
    g = M.piece("Lights")
    if mark <= 2:
        K.light_mast("Flood", 4.3, 1.6 if mark == 1 else 2.0, SLAB, 4.5 + mark, 200, g, heads=1)
    else:
        K.light_mast("FloodL", -5.2, -3.6, SLAB, 6.5, 45, g)
        K.light_mast("FloodR", 4.9, 3.9, SLAB, 6.5, 225, g)
        K.bollards("PathLamp", [(-2.3, -4.6), (-0.5, -4.6), (-2.3, -3.4), (-0.5, -3.4)], SLAB, g)
    if mark >= 4:
        MK.strip_lights("RoofEdge", (-4.1, -2.25), (1.3, -2.25), SLAB + 3.85 if mark == 5 else SLAB + 3.55, 7, g)

    M.info["rover"] = (-6.4, 2.6, 100)
    M.lamp((0.5, 0.85, 1.0), 160, P(2.6, -2.2, 3.2))
    M.lamp((1.0, 0.85, 0.6), 120, P(-1.4, -3.2, 2.6))
    M.lamp((1.0, 0.9, 0.75), 220, P(4.0, 1.4, 6.0))
    if mark == 5:
        M.lamp((0.6, 0.85, 1.0), 200, P(-1.4, 0.9, 7.0))
        M.lamp((1.0, 0.9, 0.75), 220, P(-5.0, -3.4, 6.5))


MK.run("science_lab", build,
       hero=((15.0, -20.0, 12.0), (0.0, 0.0, 3.0), 35),
       close=((9.0, -11.0, 5.0), (0.8, -1.2, 4.0), 30))
