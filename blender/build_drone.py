"""Scout drone (drones / comms / missions contract, docs/missions-contract.md section B "Models"): the Dawn of Warp
Earth-built quad tilt-rotor survey scout the Machine Shop makes and the Drone Pad launches. Original design in the
pre-warp drop ships' family (build_dropship_prewarp.py): white paint, gold foil, Space Shuttle tank orange (#C8632A
family), black heat tiles, light grey details, the player colour a thin "Owner" band.

Look: a small lifting fuselage (white, black-tiled nose cap, orange cheat line), a gold-foil avionics hatch and a
white satcom dome on top, a red anti-collision beacon, a V-tail with orange tips and a white tail strobe, four white
booms from the upper sides to tilting motor nacelles (orange cowl bands, dark motor bells) carrying three-blade carbon
rotors with orange tips, a two-axis camera gimbal under the nose, red/green nav lights on the flanks, tube skids.
2.56 across the rotor tips (x), 2.0 long over the rotor discs (1.4 fuselage), 0.95 tall; a Mk I rover is ~5 long.

Frame (CLAUDE.md "Vehicle models"): Blender +Z up, +Y = front (Godot -Z), 1 unit = 1 world unit. ORIGIN = THE GROUND
UNDER THE SKIDS (the skid tubes' bottoms at z 0), so placing the drone on a pad's Bay empty = landed. Every pivot is
unrotated (identity rest), so Godot angles go straight on.

PARTS (the game drives these; names from the contract):
  Body          everything static, joined (fuselage, tail, beacon base, gimbal fork, nav fairings), origin = root.
  Arm0..Arm3    boom pivots at the shoulders (0 front-left -X, 1 front-right, 2 rear-left, 3 rear-right). Rest =
                deployed. Optional fold: rotate about local Y (Godot), Arm0/Arm3 by -a, Arm1/Arm2 by +a (swings each
                boom toward the fore-aft line), a = 0..45 deg.
  Tilt0..Tilt3  (extra, optional) the motor nacelles, children of the arms, pivot at the nacelle centre: rotate about
                local X, -a tips the rotor forward (cruise), 0 = hover, up to -80 deg.
  Rotor0..3     the rotors, children of the nacelles: spin about local Y (Godot; Blender Z). Spin the node where it is:
                lifting it out to a direct child would leave it behind when an arm folds or a nacelle tilts.
  Camera        gimbal ball under the nose, origin = gimbal centre; looks along local -Y (Godot, down) at rest.
  Lens          child of Camera, material "Lens" (emissive violet glint). Aim the Camera node only.
  Beacon        red anti-collision light on top, material "Beacon" (exported strength 6).
  NavL / NavR   flank nav lights, materials "NavRed" / "NavGreen" (strength 3).
  Strobe        white tail strobe, material "Strobe" (exported at strength 1 so the importer keeps emission; the game
                flashes it).
  Skid          the landing skids, one mesh, pivot at the body underside: scale Y (Godot) 1 = down, ~0.35 = tucked
                (optional; the skids reach the ground at origin height).
  NameL / NameR decal markers (empties) on the flanks for the call sign: text along local +X facing local +Z, Blender
                scale (width, height, 1) = Godot scale (width, 1, height) (build_vehicles.py --blank convention).
  Materials: "Owner" = the band round the aft fuselage (recoloured per player); "WhitePaint" = the hull (achievement
  paint matches *Paint); procedural paints EMIT-baked into one 512 JPEG atlas.

Run (bpy 5.x; CLAUDE.md "Blender here"):
  /home/user/.bpyvenv/bin/python blender/build_drone.py --game        game/models/drones/scout_drone.glb (~10 s),
        re-imported and listed afterwards (the check fails on a missing part)
  /home/user/.bpyvenv/bin/python blender/build_drone.py --preview blender/previews/drones --samples 32
        scout_drone-{hero,flight,game,top,glb}.png (glb = the export posed through its own pivots; needs --game first)
  python3 blender/build_drone.py --sheet blender/previews/drones      contact sheet scout_drone-sheet.png (PIL)
Flags: --shots hero,flight,game,top,glb  --res 1080  --samples 32  --bake-res 512
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


SHEET = [[("scout_drone-hero.png", "hero"), ("scout_drone-flight.png", "cruise: nacelles tilted, banking")],
         [("scout_drone-game.png", "game camera, cruise height"), ("scout_drone-top.png", "top")],
         [("scout_drone-glb.png", "glb pivots: rest / cruise / folded"), ("@", "")]]

if "--sheet" in argv:
    from glb_check import contact_sheet
    folder = arg("--sheet")
    contact_sheet(folder, [[c for c in r if c[0] != "@"] for r in SHEET], os.path.join(folder, "scout_drone-sheet.png"),
                  640, 427)
    sys.exit(0)

import bpy  # noqa: E402  first: bpy puts bmesh and mathutils on the path
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import building_kit as K  # noqa: E402  (stage, game camera, loader)
from glb_check import join_pivots, prune_empties, verify_glb  # noqa: E402

S = K.load("build_satellite.py", ["--game"])  # materials + geometry + join/bake/report helpers
flat, painted, foil, tiles = S["flat"], S["painted"], S["foil"], S["tiles"]
box, cylinder, sphere, lathe, ring, prism, loft, catmull, strut, empty, link = (
    S["box"], S["cylinder"], S["sphere"], S["lathe"], S["ring"], S["prism"], S["loft"], S["catmull"], S["strut"],
    S["empty"], S["link"])

REPO = os.path.dirname(HERE)
OUT = os.path.join(REPO, "game", "models", "drones", "scout_drone.glb")
GAME = "--game" in argv
PREVIEW = arg("--preview")
ALL_SHOTS = ["hero", "flight", "game", "top", "glb"]
SHOTS = [s.strip() for s in arg("--shots", ",".join(ALL_SHOTS)).split(",") if s.strip()]
RES = int(arg("--res", "1080"))
SAMPLES = int(arg("--samples", "32"))
BAKE_RES = int(arg("--bake-res", "512"))
if not GAME and not PREVIEW:
    sys.exit("nothing to do: pass --game and/or --preview <dir> (or --sheet <dir> with system python)")

ARMS = 4
PIVOTS = [f"Arm{k}" for k in range(ARMS)] + [f"Tilt{k}" for k in range(ARMS)] + [f"Rotor{k}" for k in range(ARMS)] + \
    ["Camera", "Skid"]
KEEP = ["Lens", "Beacon", "NavL", "NavR", "Strobe"]
REQUIRED = ["ScoutDrone", "Body", "NameL", "NameR"] + PIVOTS + KEEP
FOLD_SIGN = [-1, 1, 1, -1]

# ---------------------------------------------------------------- dimensions (Blender, origin on the ground)

ZC = 0.64                    # fuselage centre line height
BELLY = 0.47                 # body underside where the skids hang
SHOULDER = [(-0.17, 0.3), (0.17, 0.3), (-0.17, -0.26), (0.17, -0.26)]
NACELLE = [(-0.86, 0.62), (0.86, 0.62), (-0.86, -0.58), (0.86, -0.58)]
ARM_Z = 0.74
ROTOR_R = 0.42
ROTOR_UP = 0.15              # rotor hub above the nacelle centre
CAM = (0.0, 0.42, 0.34)      # gimbal centre

STATIONS = catmull([
    # y, half width, top, bottom, zc: a small lifting body, fullest just ahead of the middle, a tapered tail boom
    (-0.70, 0.07, 0.06, 0.05, ZC + 0.06),
    (-0.55, 0.12, 0.10, 0.08, ZC + 0.04),
    (-0.35, 0.21, 0.16, 0.13, ZC + 0.01),
    (-0.05, 0.255, 0.19, 0.16, ZC),
    (0.25, 0.25, 0.185, 0.16, ZC),
    (0.45, 0.2, 0.15, 0.14, ZC - 0.01),
    (0.58, 0.14, 0.11, 0.11, ZC - 0.02),
    (0.68, 0.07, 0.06, 0.06, ZC - 0.025),
], 3)
NOSE_Y = 0.52
TIP = (0.715, ZC - 0.03)


def hull_at(y):
    st = STATIONS
    if y <= st[0][0]:
        return st[0][1:]
    for a, b in zip(st, st[1:]):
        if a[0] <= y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return tuple(u + (v - u) * t for u, v in zip(a[1:], b[1:]))
    return st[-1][1:]


WHITE = (0.82, 0.82, 0.8)
ORANGE = (0.56, 0.13, 0.026)  # linear for sRGB ~#C8632A (the drop ships' and satellites' orange)


def palette():
    return {
        "white": painted("WhitePaint", WHITE, 0.05, 0.45, 0.05, 9.0),
        "orange": painted("FoamOrange", ORANGE, 0.0, 0.75, 0.1, 12.0),
        "foil": foil("FoilGold", (1.0, 0.7, 0.28)),
        "tiles": tiles("TileBlack", 70.0),
        "grey": flat("PaintGrey", (0.5, 0.51, 0.52), 0.3, 0.45),
        "dark": flat("PaintDark", (0.07, 0.07, 0.075), 0.3, 0.55),
        "carbon": flat("Carbon", (0.05, 0.052, 0.058), 0.2, 0.38),
        "steel": flat("Steel", (0.62, 0.63, 0.66), 0.9, 0.3),
        "owner": flat("Owner", (0.72, 0.1, 0.06), 0.1, 0.4),
        "lens": flat("Lens", (0.06, 0.04, 0.14), 0.3, 0.06, emission=(0.6, 0.45, 1.0), strength=1.6),
        "beacon": flat("Beacon", (1.0, 0.2, 0.1), 0.0, 0.3, emission=(1.0, 0.18, 0.08), strength=6.0),
        "navred": flat("NavRed", (1.0, 0.1, 0.06), 0.0, 0.3, emission=(1.0, 0.08, 0.04), strength=3.0),
        "navgreen": flat("NavGreen", (0.05, 0.9, 0.2), 0.0, 0.3, emission=(0.05, 1.0, 0.2), strength=3.0),
        "strobe": flat("Strobe", (0.9, 0.9, 0.92), 0.0, 0.2, emission=(1.0, 0.98, 0.95), strength=1.0),
    }


# ---------------------------------------------------------------- geometry


def band_loft(name, y0, y1, grow, mat, parent, seg=28):
    """A band round the fuselage between y0 and y1, `grow` proud of the skin (open, no caps)."""
    st = []
    for y in (y0, y1):
        hw, top, bot, zc = hull_at(y)
        st.append((y, hw + grow, top + grow, bot + grow, zc))
    return loft(name, st, mat, parent, seg=seg, cap_back=False, cap_front=False)


def side_strip(name, y0, y1, dz0, dz1, mat, parent, side, n=10, lift=0.004):
    """A strip painted along one flank of the fuselage between heights zc+dz0 and zc+dz1."""
    bm = bmesh.new()
    rows = []
    for i in range(n + 1):
        y = y0 + (y1 - y0) * i / n
        hw, top, bot, zc = hull_at(y)
        row = []
        for dz in (dz0, dz1):
            h = top if dz >= 0 else bot
            f = max(0.0, 1.0 - abs(dz / h) ** 2.6) ** (1 / 2.6)
            row.append(bm.verts.new((side * (hw * f + lift), y, zc + dz)))
        rows.append(row)
    for a, b in zip(rows, rows[1:]):
        bm.faces.new((a[0], b[0], b[1], a[1]) if side > 0 else (a[1], b[1], b[0], a[0]))
    o = S["mesh_object"](name, bm, mat, parent, recalc=False)
    o.visible_shadow = False
    return o


def fuselage(P, root):
    main = [s for s in STATIONS if s[0] <= NOSE_Y] + [(NOSE_Y, *hull_at(NOSE_Y))]
    nose = [(NOSE_Y, *hull_at(NOSE_Y))] + [s for s in STATIONS if s[0] > NOSE_Y]
    loft("Fuselage", main, P["white"], root, seg=28, cap_front=False)
    loft("NoseCap", nose, P["tiles"], root, seg=28, tip=TIP, cap_back=False)
    band_loft("NoseRing", NOSE_Y - 0.012, NOSE_Y + 0.004, 0.004, P["grey"], root)
    band_loft("OwnerBand", -0.43, -0.37, 0.004, P["owner"], root)
    band_loft("TailRing", -0.63, -0.6, 0.004, P["orange"], root)
    for s in (-1, 1):
        side_strip(f"CheatLine{s}", -0.5, 0.5, -0.045, -0.02, P["orange"], root, s)
        side_strip(f"CheatLineThin{s}", -0.46, 0.46, -0.07, -0.06, P["orange"], root, s)
    # black-tiled belly panel and a foil battery pack
    box("BellyTiles", (0.3, 0.62, 0.02), (0, 0.0, ZC - 0.16 + 0.004), P["tiles"], root, bev=0.004)
    box("Battery", (0.2, 0.36, 0.035), (0, -0.05, ZC - 0.17), P["foil"], root, bev=0.006)
    # top: gold-foil avionics hatch with a white frame, satcom dome, GPS puck, whip antenna, pitot tube
    top = hull_at(0.0)[1] + ZC
    box("HatchFrame", (0.26, 0.36, 0.02), (0, 0.02, top - 0.004), P["grey"], root, bev=0.004)
    box("Hatch", (0.22, 0.32, 0.024), (0, 0.02, top - 0.002), P["foil"], root, bev=0.006)
    for k, x in enumerate((-0.07, 0.0, 0.07)):
        box(f"HatchRib{k}", (0.012, 0.3, 0.008), (x, 0.02, top + 0.011), P["grey"], root, bev=0.0)
    zt = hull_at(-0.3)[1] + hull_at(-0.3)[3]
    lathe("Satcom", [(0.0, 0.07), (0.035, 0.065), (0.06, 0.045), (0.07, 0.015), (0.072, -0.02), (0.0, -0.02)],
          P["white"], root, (0, -0.3, zt - 0.01), seg=24)
    cylinder("SatcomBase", 0.078, 0.02, (0, -0.3, zt - 0.02), P["grey"], root, seg=24)
    cylinder("GpsPuck", 0.035, 0.02, (0.1, -0.12, top - 0.02), P["white"], root, seg=16)
    strut("Whip", (-0.1, -0.15, top - 0.01), (-0.12, -0.32, top + 0.2), 0.004, P["steel"], root, 6)
    zn = hull_at(0.6)[3]
    strut("Pitot", (0.06, 0.62, zn - 0.03), (0.07, 0.86, zn - 0.04), 0.006, P["steel"], root, 6)
    # forward sensor window on the nose cap
    box("NoseWindow", (0.07, 0.02, 0.035), (0, 0.686, ZC - 0.02), P["lens"], root, bev=0.005).rotation_euler.x = \
        math.radians(-12)
    # inverted V-tail (below the rotor discs, so the booms can fold over it)
    for s in (-1, 1):
        h = hull_at(-0.55)
        fin = empty(f"FinFrame{s}", (s * 0.03, -0.55, h[3] - h[2] + 0.025), root)
        fin.rotation_euler = (0, math.radians(-s * 38), 0)
        prism(f"Fin{s}", [(0.08, 0.0), (-0.12, 0.0), (-0.17, -0.18), (-0.05, -0.18)], -0.009, 0.009, P["white"], fin,
              plane="YZ", bev=0.003)
        prism(f"FinTip{s}", [(-0.05, -0.18), (-0.17, -0.18), (-0.18, -0.21), (-0.035, -0.21)], -0.009, 0.009,
              P["orange"], fin, plane="YZ", bev=0.003)
    # beacon on its base, the tail strobe, nav light fairings on the flanks
    by = 0.24
    btop = hull_at(by)[1] + hull_at(by)[3]
    cylinder("BeaconBase", 0.03, 0.025, (0, by, btop - 0.002), P["grey"], root, seg=14)
    beacon = sphere("Beacon", 0.024, (0, by, btop + 0.018), P["beacon"], root, 10, 5)
    tail = STATIONS[0]
    strobe = sphere("Strobe", 0.02, (0, tail[0] - 0.025, tail[4]), P["strobe"], root, 8, 4)
    nav = {}
    for s in (-1, 1):
        y = 0.1
        hw = hull_at(y)[0]
        box(f"NavFairing{s}", (0.04, 0.09, 0.035), (s * (hw - 0.002), y, ZC + 0.02), P["grey"], root, bev=0.008)
        nav[s] = sphere("NavL" if s < 0 else "NavR", 0.016, (s * (hw + 0.018), y + 0.02, ZC + 0.02),
                        P["navred"] if s < 0 else P["navgreen"], root, 8, 4)
    # shoulder fairings where the booms join
    for k, (x, y) in enumerate(SHOULDER):
        sphere(f"ShoulderFairing{k}", 0.055, (x, y, ARM_Z - 0.01), P["white"], root, 12, 6).scale = (1.0, 1.4, 0.75)
    # call-sign markers on the flanks (the game paints the name)
    for s in (-1, 1):
        hw = hull_at(0.0)[0]
        m = Matrix.Translation((s * (hw * 0.985 + 0.006), 0.0, ZC + 0.055)) @ \
            Matrix.Rotation(math.radians(90 if s > 0 else -90), 4, "Z") @ Matrix.Rotation(math.radians(90), 4, "X")
        e = bpy.data.objects.new("NameR" if s > 0 else "NameL", None)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 0.15
        e.matrix_basis = m @ Matrix.Diagonal((0.42, 0.075, 1.0, 1.0))
        link(e, root)
    return beacon, strobe, nav


def blade(name, mat, parent, angle, r0=0.05, r1=ROTOR_R, c0=0.06, c1=0.034, pitch=9.0):
    """One rotor blade along local +X turned by `angle` about the hub, pitched about its own axis."""
    frame = empty(name + "Frame", (0, 0, 0), parent)
    frame.rotation_euler = (math.radians(pitch), 0, angle)
    prism(name, [(r0, -c0 / 2), (r1 - 0.02, -c1 / 2), (r1, 0.0), (r1 - 0.02, c1 / 2), (r0, c0 / 2)], -0.005, 0.005,
          mat, frame, bev=0.0)
    return frame


def arm(P, root, k):
    sx, sy = SHOULDER[k]
    nx, ny = NACELLE[k]
    a = empty(f"Arm{k}", (sx, sy, ARM_Z), root)
    d = Vector((nx - sx, ny - sy, 0.0))
    L = d.length
    u = d.normalized()
    cylinder(f"Knuckle{k}", 0.034, 0.08, (0, 0, 0), P["grey"], a, seg=14)
    strut(f"Boom{k}", (0, 0, 0), u * (L - 0.06), 0.03, P["white"], a, 12)
    strut(f"BoomBand{k}", u * (L - 0.24), u * (L - 0.16), 0.032, P["orange"], a, 12)
    strut(f"BoomSpar{k}", u * 0.06 + Vector((0, 0, -0.02)), u * (L - 0.1) + Vector((0, 0, -0.02)), 0.012, P["grey"],
          a, 6)
    # nacelle: pivot at its centre on the boom tip, tilts about local X
    t = empty(f"Tilt{k}", (nx - sx, ny - sy, 0.0), a)
    cylinder(f"NacelleYoke{k}", 0.022, 0.2, (0, 0, 0), P["grey"], t, axis="X", seg=10)
    cylinder(f"Nacelle{k}", 0.07, 0.2, (0, 0, -0.02), P["white"], t, seg=20)
    cylinder(f"NacelleCowl{k}", 0.073, 0.045, (0, 0, 0.065), P["orange"], t, seg=20)
    lathe(f"NacelleTail{k}", [(0.07, -0.12), (0.05, -0.15), (0.0, -0.16)], P["white"], t, seg=20)
    cylinder(f"Motor{k}", 0.05, 0.05, (0, 0, 0.11), P["dark"], t, seg=18)
    for j in range(6):  # cooling fins round the motor bell
        ang = math.radians(30 + 60 * j)
        box(f"MotorFin{k}{j}", (0.012, 0.03, 0.04), (0.05 * math.cos(ang), 0.05 * math.sin(ang), 0.11), P["dark"], t,
            bev=0.0).rotation_euler.z = ang
    # rotor: pivot at the hub, spins about local Z (Godot Y)
    r = empty(f"Rotor{k}", (0, 0, ROTOR_UP), t)
    lathe(f"Spinner{k}", [(0.0, 0.055), (0.02, 0.05), (0.034, 0.03), (0.038, 0.0), (0.0, 0.0)], P["white"], r, seg=18)
    cylinder(f"Hub{k}", 0.03, 0.02, (0, 0, -0.012), P["grey"], r, seg=16)
    spin = 1 if k in (0, 3) else -1
    for j in range(3):
        f = blade(f"Blade{k}{j}", P["carbon"], r, math.radians(120 * j + 15 * k), pitch=9.0 * spin)
        prism(f"BladeTip{k}{j}", [(ROTOR_R - 0.075, -0.019), (ROTOR_R - 0.02, -0.017), (ROTOR_R, 0.0),
                                  (ROTOR_R - 0.02, 0.017), (ROTOR_R - 0.075, 0.019)], -0.0055, 0.0055, P["orange"], f)
    return a, t, r


def camera(P, root):
    # fork from the belly (static) and the gimbal ball (the Camera pivot)
    h = hull_at(CAM[1])
    zb = h[3] - h[2]
    cylinder("GimbalMount", 0.05, 0.03, (CAM[0], CAM[1], zb - 0.01), P["grey"], root, seg=16)
    cylinder("GimbalYaw", 0.025, 0.05, (CAM[0], CAM[1], zb - 0.045), P["dark"], root, seg=12)
    for s in (-1, 1):
        strut(f"Fork{s}", (CAM[0] + s * 0.03, CAM[1], zb - 0.06), (CAM[0] + s * 0.092, CAM[1], CAM[2] + 0.005), 0.012,
              P["grey"], root, 8)
    cam = empty("Camera", CAM, root)
    sphere("CamBall", 0.072, (0, 0, 0), P["white"], cam, 16, 8)
    for s in (-1, 1):
        cylinder(f"CamTrunnion{s}", 0.022, 0.03, (s * 0.078, 0, 0), P["grey"], cam, axis="X", seg=12)
    ring("CamHood", 0.034, 0.046, -0.085, -0.05, P["dark"], cam, seg=20)
    box("CamBand", (0.15, 0.03, 0.03), (0, 0.0, 0.0), P["orange"], cam, bev=0.006)
    lens = cylinder("Lens", 0.034, 0.006, (0, 0, -0.072), P["lens"], cam, seg=20)
    return cam, lens


def skids(P, root):
    sk = empty("Skid", (0, 0, BELLY), root)
    r = 0.022
    for s in (-1, 1):
        x = s * 0.3
        z = r - BELLY
        strut(f"SkidTube{s}", (x, -0.4, z), (x, 0.36, z), r, P["grey"], sk, 10)
        strut(f"SkidToe{s}", (x, 0.36, z), (x, 0.44, z + 0.05), r, P["grey"], sk, 10)
        strut(f"SkidHeel{s}", (x, -0.4, z), (x, -0.45, z + 0.03), r, P["grey"], sk, 10)
        for j, y in enumerate((-0.22, 0.22)):
            strut(f"SkidStrut{s}{j}", (s * 0.12, y, 0.0), (x, y + 0.02, z + 0.01), 0.016, P["white"], sk, 8)
            box(f"SkidFoot{s}{j}", (0.05, 0.06, 0.012), (x, y + 0.02, z - r + 0.006), P["dark"], sk, bev=0.0)
        strut(f"SkidBand{s}", (x, 0.05, z), (x, 0.13, z), r + 0.003, P["orange"], sk, 10)
    return sk


def build():
    P = palette()
    root = empty("ScoutDrone")
    beacon, strobe, nav = fuselage(P, root)
    arms = [arm(P, root, k) for k in range(ARMS)]
    cam, lens = camera(P, root)
    sk = skids(P, root)
    bpy.context.view_layer.update()
    return {"root": root, "P": P, "arms": arms, "Camera": cam, "Lens": lens, "Skid": sk, "Beacon": beacon,
            "Strobe": strobe, "nav": nav}


# ---------------------------------------------------------------- game export


def apply_scales(root):
    """Bake object scales (fairings) into their meshes so the join keeps them (join_for_game keeps transforms)."""
    for o in root.children_recursive:
        if o.type == "MESH" and tuple(o.scale) != (1.0, 1.0, 1.0):
            o.data.transform(Matrix.Diagonal((*o.scale, 1.0)))
            o.scale = (1, 1, 1)


def game_export():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = build()
    apply_scales(parts["root"])
    join_pivots(parts["root"], PIVOTS, KEEP)
    prune_empties(parts["root"], REQUIRED)  # the blade / fin frames
    S["KEEP"] = KEEP  # bake_colours skips these (flat emissive parts)
    S["bake_colours"](parts["root"], BAKE_RES, "ScoutDronePaint")
    S["export_glb"](parts["root"], OUT)
    verify_glb(OUT, REQUIRED)


# ---------------------------------------------------------------- previews


def setup_stage():
    K.OP["materials"]()
    K.OP["stage"]()
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    cam = bpy.data.objects.new("RenderCam", bpy.data.cameras.new("RenderCam"))
    cam.data.clip_end = 3000
    link(cam)
    scene.camera = cam
    return cam


def shoot(cam, name, pos, target, lens=50, ortho=None, w=None, h=None, angle_y=None):
    scene = bpy.context.scene
    scene.render.resolution_x = w or RES
    scene.render.resolution_y = h or int((w or RES) * 2 / 3)
    if ortho:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho
    else:
        cam.data.type = "PERSP"
        cam.data.sensor_fit = "AUTO"
        cam.data.lens = lens
        if angle_y:
            cam.data.sensor_fit = "VERTICAL"
            cam.data.angle_y = math.radians(angle_y)
    cam.location = pos
    cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
    path = os.path.join(PREVIEW, f"scout_drone-{name}.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("rendered", path, flush=True)


def pose(arms, cam, fold=0.0, tilt=0.0, spin=0.0, cam_tilt=0.0, skid=1.0, sk=None):
    for k, (a, t, r) in enumerate(arms):
        a.rotation_mode = t.rotation_mode = r.rotation_mode = "XYZ"
        a.rotation_euler = (0, 0, math.radians(FOLD_SIGN[k] * fold))
        t.rotation_euler = (math.radians(-tilt), 0, 0)
        r.rotation_euler = (0, 0, math.radians(spin + 37 * k))
    cam.rotation_mode = "XYZ"
    cam.rotation_euler = (math.radians(cam_tilt), 0, 0)
    if sk is not None:
        sk.scale = (1, 1, skid)
    bpy.context.view_layer.update()


def scale_rover(x, y, yaw):
    rover = K.BV["rover_mk1"](K.BV["palette"]())
    rover.location = (x, y, 0.0)
    rover.rotation_euler.z = math.radians(yaw)
    return rover


def preview_build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = build()
    root = parts["root"]
    cam = setup_stage()
    sk = parts["Skid"]
    arms = parts["arms"]
    if "hero" in SHOTS:
        root.location = (0, 0, 1.2)
        pose(arms, parts["Camera"], spin=0, cam_tilt=-20)
        shoot(cam, "hero", (3.1, 4.0, 2.9), (0, 0.05, 1.75), 50)
    if "flight" in SHOTS:
        root.location = (0, 0, 2.6)
        root.rotation_euler = (math.radians(-6), math.radians(18), math.radians(-25))
        pose(arms, parts["Camera"], tilt=62, spin=50, cam_tilt=-35, skid=0.4, sk=sk)
        shoot(cam, "flight", (-3.6, 3.4, 4.6), (0, 0, 3.0), 50)
        root.rotation_euler = (0, 0, 0)
    if "game" in SHOTS:
        # the game camera at zoom 50 on a drone at cruise height 14 over a Mk I rover (scale), its shadow below
        root.location = (2.0, 1.0, 14.0)
        root.rotation_euler = (0, math.radians(-10), math.radians(-30))
        pose(arms, parts["Camera"], tilt=55, spin=10, cam_tilt=-30, skid=0.4, sk=sk)
        scale_rover(-3.5, -2.0, 150)
        pos, target = K.game_cam(50.0, (0, 0, 6.0))
        shoot(cam, "game", pos, target, angle_y=45, w=RES, h=int(RES * 9 / 16))
        root.rotation_euler = (0, 0, 0)
    if "top" in SHOTS:
        root.location = (60, 0, 0.0)
        pose(arms, parts["Camera"], skid=1.0, sk=sk)
        shoot(cam, "top", (60, -0.001, 30), (60, 0, 0), ortho=3.0, w=int(RES * 0.75), h=int(RES * 0.75))


def import_glb(path, offset, yaw=0.0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    holder = bpy.data.objects.new("Holder", None)
    link(holder)
    for o in new:
        if o.parent is None:
            o.parent = holder
    holder.location = offset
    holder.rotation_euler.z = math.radians(yaw)
    bpy.context.view_layer.update()
    return holder, {o.name.split(".")[0]: o for o in new}


def preview_glb():
    if not os.path.exists(OUT):
        print(f"no {OUT}: run --game first", flush=True)
        return
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cam = setup_stage()
    # left to right (seen from the front-right): rest on the ground, cruise (nacelles -70, rotors turned, camera 30
    # forward, skids tucked, 0.9 up), folded (arms 45, skids tucked)
    for k, (fold, tilt, spin, ct, skid, z) in enumerate([(0, 0, 0, 0, 1, 0.0), (0, 70, 40, -30, 0.35, 0.9),
                                                           (45, 0, 0, 0, 0.35, 0.0)]):
        _, p = import_glb(OUT, ((k - 1) * 3.0, 0, z))
        arms = [(p[f"Arm{i}"], p[f"Tilt{i}"], p[f"Rotor{i}"]) for i in range(ARMS)]
        pose(arms, p["Camera"], fold, tilt, spin, ct, skid, p["Skid"])
    shoot(cam, "glb", (5.5, 7.4, 4.6), (0, 0, 0.55), 40)


def main():
    if GAME:
        game_export()
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        if set(SHOTS) - {"glb"}:
            preview_build()
        if "glb" in SHOTS:
            preview_glb()


main()
