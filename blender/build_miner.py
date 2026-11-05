"""Mining Vehicle Mk I: a Blender mockup of a new look for the game's primitive "Mining Crawler"
(game/scripts/BuildingModels.cs Crawler), the early-exploration member of the same family as the Mk I Scout rover
(build_vehicles.py --tier 1): boxy and crude, split flat windscreen, primer replacement panels, the yellowed white hull
with the owner-colour waist stripe, hazard-yellow work gear (legs, boom, drill head) with black chevrons, tracks.

It loads build_vehicles.py's helpers (box, cylinder, prism, ribbon, text, wipers, palette, weather_hull, export) the way
render_finish.py does: the source is exec'd with its trailing main() call stripped, so nothing of the rover is built.

  /root/bpyenv/bin/python blender/build_miner.py --mode travel --preview blender/previews/miner --res 640 --samples 16
  /root/bpyenv/bin/python blender/build_miner.py --mode deployed --shots front34,side --preview blender/previews/miner
  /root/bpyenv/bin/python blender/build_miner.py --blank --out /tmp/miner.glb      (glb; NOT into game/models yet)
  python3 blender/build_miner.py --sheet blender/previews/miner                    (contact sheet, PIL, no Blender)

Flags: --mode travel|deployed (deployed = legs braced, boom raised, drill string down to the ground), --preview <dir>,
--res, --samples, --shots front34,rear34,side,game, --out <glb>, --name / --number (painted decals), --blank (decal
spots as empties NameL/NameR/NameRear/NumberL/NumberR, build_vehicles' GAME CONTRACT), --fill 0..1 (ore in the hopper;
default 0.6 travel / 0.85 deployed), --no-bake. Previews: <dir>/miner-<mode>-<shot>.png.

Game contract (kept from the primitive so this can replace it; Blender +Y front, +Z up, 1 unit = 1 world unit):
  MiningCrawler (root)
    Body            everything rigid (the game shakes it while digging); holds Fill, Beacon, tail lamps, Wiper0/1,
                    decal markers, the rams' barrels RamL/RamR
      Fill          ore in the hopper: origin on the hopper floor, scale Blender Z (Godot Y) 0.01..1
    Leg0..Leg3      stabiliser pivots (0 right front, 1 right rear, 2 left front, 3 left rear); the leg reaches along
                    local +X; Godot sets RotationDegrees (0, 0 | 180 for 2 and 3, angle), angle FoldedLeg 78 ->
                    DeployedLeg -38 (BuildingModels' constants; the pads sit flat on the ground at -38)
    Boom            pivot at (0, -1.1, 3.45) (Godot (0, 3.45, 1.1)), BoomLength 5.4, rotates about X up to 32
                    (DeployedBoom); BoomReach / BoomTipHeight in the game stay valid
      Drill         at the boom tip (boom-local y 5.4), counter-rotated to hang straight down
        String      drill rod, 1 unit long down local -Z: scale Z = reach (0.3 folded, BoomTipHeight - 1 deployed)
        Bit         at z = -reach; 1.0 long, so the tip just touches the ground when deployed; the game spins it
      RamRodL/R     lift-ram rods (pivot at the boom's cross pin); RamL/R in Body hold the barrels. Not animated by the
                    game yet: each needs aiming at the other's pivot (see pose()).
  Materials: "Owner" (recoloured per player), "Amber" on the Beacon, "TailLamp" on the tail lamps.
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


SHEET_ORDER = [("travel", "front34"), ("travel", "rear34"), ("travel", "side"), ("travel", "game"), ("deployed", "front34"),
               ("deployed", "side")]


def contact_sheet(folder):
    """3 x 2 grid of the previews with a caption on each (PIL only, so it runs without Blender)."""
    from PIL import Image, ImageDraw
    tiles = []
    for mode, shot in SHEET_ORDER:
        path = os.path.join(folder, f"miner-{mode}-{shot}.png")
        if os.path.exists(path):
            tiles.append((f"{mode} / {shot}", Image.open(path).convert("RGB")))
    if not tiles:
        sys.exit(f"no previews in {folder}")
    w = 640
    h = int(w * tiles[0][1].height / tiles[0][1].width)
    cols = 3
    rows = (len(tiles) + cols - 1) // cols
    pad, head = 8, 40
    sheet = Image.new("RGB", (cols * w + (cols + 1) * pad, head + rows * (h + pad) + pad), (24, 26, 30))
    d = ImageDraw.Draw(sheet)
    d.text((pad + 4, 14), "Mining Vehicle Mk I - mockup (blender/build_miner.py)", fill=(230, 230, 230))
    for k, (label, im) in enumerate(tiles):
        x = pad + (k % cols) * (w + pad)
        y = head + (k // cols) * (h + pad)
        sheet.paste(im.resize((w, h), Image.LANCZOS), (x, y))
        d.rectangle((x, y, x + 8 + 7 * len(label), y + 18), fill=(0, 0, 0))
        d.text((x + 4, y + 4), label, fill=(255, 210, 90))
    out = os.path.join(folder, "miner-sheet.png")
    sheet.save(out)
    print("wrote", out)


if "--sheet" in argv:
    contact_sheet(arg("--sheet"))
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MODE = arg("--mode", "travel")
PREVIEW = arg("--preview")
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "40"))
SHOTS = [s.strip() for s in arg("--shots", "front34,rear34,side,game").split(",") if s.strip()]
OUT = arg("--out")
NAME = arg("--name", "BASALT").upper()
NUMBER = arg("--number", "26")
FILL = float(arg("--fill", "0.6" if MODE == "travel" else "0.85"))
BAKE = "--no-bake" not in argv
if MODE not in ("travel", "deployed"):
    sys.exit(f"--mode is travel or deployed, not {MODE}")


def load_build_vehicles():
    """build_vehicles.py's functions without running its main() (render_finish.py's loader). Only --blank is passed
    on to its own flag parsing (text() reads BLANK)."""
    path = os.path.join(HERE, "build_vehicles.py")
    with open(path) as f:
        src = f.read()
    src = re.sub(r"^main\(\)\s*$", "", src, flags=re.M)
    saved = sys.argv
    sys.argv = [path] + (["--blank"] if "--blank" in argv else [])
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


bv = load_build_vehicles()
box, cylinder, prism, ribbon, text, empty, mesh_object, material, strut = (
    bv[k] for k in ("box", "cylinder", "prism", "ribbon", "text", "empty", "mesh_object", "material", "strut"))

# ---------------------------------------------------------------- the game's numbers (BuildingModels.cs)

FOLDED_LEG, DEPLOYED_LEG = 78.0, -38.0
DEPLOYED_BOOM = 32.0
BOOM_LENGTH = 5.4
BOOM_PIVOT = Vector((0.0, -1.1, 3.45))
TIP_HEIGHT = BOOM_PIVOT.z + BOOM_LENGTH * math.sin(math.radians(DEPLOYED_BOOM))
LEG_Z = 1.3                    # leg pivot height; the leg + jack + pad reach the ground at DEPLOYED_LEG
LEG_PIVOTS = [(1.55, 2.68), (1.55, -2.4), (-1.55, 2.68), (-1.55, -2.4)]  # Leg0..3: right front, right rear, left front, left rear
BEAM = 1.6                     # leg beam length to the jack
PAD_T = 0.08
JACK = LEG_Z - BEAM * math.sin(math.radians(-DEPLOYED_LEG)) - PAD_T  # vertical jack from the beam end to the pad top
RAM_X, RAM_PIN = 0.95, 2.5     # lift rams: x of each ram, boom-local y of the cross pin
RAM_BASE = Vector((0.0, 0.35, 1.93))  # hull lug (x = ±RAM_X)
RAM_PART = 1.6                 # barrel and rod lengths

# ---------------------------------------------------------------- layout (Blender, +Y front)

W = 2.9                        # hull width (over the track fenders)
HULL_Z = (1.1, 1.85)           # hull bottom / deck
TRACK_X, TRACK_W = 1.13, 0.6
TRACK_YF, TRACK_YR, TRACK_ZC, TRACK_R = 1.72, -1.72, 0.5, 0.44  # idler / sprocket centres, path radius (plate centres)
CAB_W = 2.1


def palette():
    P = bv["tier_palette"](bv["palette"]())
    P.update({
        "yellow": material("WorkYellow", (0.86, 0.6, 0.08), 0.1, 0.5),
        "black": material("HazardBlack", (0.02, 0.02, 0.022), 0.0, 0.6),
        "hopper": material("HopperSteel", (0.36, 0.36, 0.35), 0.6, 0.6),
        "ore": material("Ore", (0.3, 0.22, 0.15), 0.0, 0.92),
        "orelight": material("OreLight", (0.5, 0.36, 0.22), 0.0, 0.9),
        "bit": material("BitSteel", (0.62, 0.5, 0.32), 0.85, 0.3),
        "chrome": material("Chrome", (0.8, 0.82, 0.85), 1.0, 0.12),
        "rubber": material("TrackSteel", (0.09, 0.09, 0.095), 0.55, 0.7),
    })
    return P


def frame(loc, rot=(0, 0, 0)):
    from mathutils import Euler
    return Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4()


def clip(poly, a, c):
    """Sutherland-Hodgman: keep the part of convex polygon `poly` [(x, y)] where a . p <= c."""
    out = []
    for i, p in enumerate(poly):
        q = poly[(i + 1) % len(poly)]
        dp, dq = a[0] * p[0] + a[1] * p[1] - c, a[0] * q[0] + a[1] * q[1] - c
        if dp <= 0:
            out.append(p)
        if (dp < 0 < dq) or (dq < 0 < dp):
            t = dp / (dp - dq)
            out.append((p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])))
    return out


def hazard(name, w, h, matrix, P, parent, stripe=0.09, base=True):
    """Black-and-yellow chevron panel: a w x h rectangle in the local XY plane facing +Z (yellow base + black 45 degree
    bands, 1.5 mm proud). A decal: no shadow."""
    bm = bmesh.new()
    rect = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
    if base:
        f = bm.faces.new([bm.verts.new((x, y, 0.0)) for x, y in rect])
        f.material_index = 0
    c = -(w + h) / 2
    while c < (w + h) / 2:
        band = clip(clip(rect, (-1, -1), -c), (1, 1), c + stripe)
        if len(band) >= 3:
            f = bm.faces.new([bm.verts.new((x, y, 0.0015)) for x, y in band])
            f.material_index = 1
        c += 2 * stripe
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(P["yellow"])
    me.materials.append(P["black"])
    o = bpy.data.objects.new(name, me)
    o.matrix_basis = matrix
    o.visible_shadow = False
    return bv["link"](o, parent)


# ---------------------------------------------------------------- tracks


def track_path(n):
    """n evenly spaced points round the track loop: (y, z, tangent angle); travelling backwards along the bottom run so
    that a box's local +Z (after rotating about X by the angle) points out of the loop."""
    straight = TRACK_YF - TRACK_YR
    arc = math.pi * TRACK_R
    total = 2 * straight + 2 * arc
    pts = []
    for k in range(n):
        s = total * k / n
        if s < straight:                       # bottom run, front -> rear
            y, z, t = TRACK_YF - s, TRACK_ZC - TRACK_R, (-1.0, 0.0)
        elif s < straight + arc:               # rear wheel, bottom -> top
            phi = math.radians(270) - (s - straight) / TRACK_R
            y, z, t = TRACK_YR + TRACK_R * math.cos(phi), TRACK_ZC + TRACK_R * math.sin(phi), (math.sin(phi), -math.cos(phi))
        elif s < 2 * straight + arc:           # top run, rear -> front
            y, z, t = TRACK_YR + (s - straight - arc), TRACK_ZC + TRACK_R, (1.0, 0.0)
        else:                                  # front wheel, top -> bottom
            phi = math.radians(90) - (s - 2 * straight - arc) / TRACK_R
            y, z, t = TRACK_YF + TRACK_R * math.cos(phi), TRACK_ZC + TRACK_R * math.sin(phi), (math.sin(phi), -math.cos(phi))
        pts.append((y, z, math.atan2(t[1], t[0])))
    return pts


def track(side, P, parent):
    """One track unit: a loop of steel plates with grousers (open, so the wheels show), front idler, rear drive
    sprocket, five dual road wheels, two return rollers and the track frame."""
    x = side * TRACK_X
    tag = "L" if side < 0 else "R"
    bm = bmesh.new()
    for y, z, a in track_path(52):
        m = Matrix.Translation((x, y, z)) @ Matrix.Rotation(a, 4, "X")
        bmesh.ops.create_cube(bm, size=1.0, matrix=m @ Matrix.Diagonal((TRACK_W, 0.16, 0.05, 1)))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m @ Matrix.Translation((0, -0.03, 0.04)) @ Matrix.Diagonal((TRACK_W * 0.96, 0.04, 0.04, 1)))
    mesh_object(f"Track{tag}", bm, P["rubber"], parent)
    # Guide horns down the middle of the inside of the loop, between the road wheel discs.
    bm = bmesh.new()
    for y, z, a in track_path(52)[::2]:
        m = Matrix.Translation((x, y, z)) @ Matrix.Rotation(a, 4, "X")
        bmesh.ops.create_cube(bm, size=1.0, matrix=m @ Matrix.Translation((0, 0, -0.06)) @ Matrix.Diagonal((0.06, 0.08, 0.08, 1)))
    mesh_object(f"TrackGuides{tag}", bm, P["dark"], parent)

    inner = TRACK_R - 0.03                     # inside face of the plates
    for name, yc, r in (("Idler", TRACK_YF, inner - 0.04), ("Sprocket", TRACK_YR, inner - 0.06)):
        for dx in (-0.17, 0.17):
            cylinder(f"{name}{tag}{dx}", r, 0.12, (x + dx, yc, TRACK_ZC), P["dark"], parent, segments=28, bev=0.015)
            cylinder(f"{name}Hub{tag}{dx}", r * 0.45, 0.14, (x + dx, yc, TRACK_ZC), P["steel"], parent, segments=16)
        cylinder(f"{name}Axle{tag}", 0.08, 0.5, (x, yc, TRACK_ZC), P["steel"], parent, segments=12)
    for k in range(10):                        # sprocket teeth between the plates
        a = 2 * math.pi * k / 10
        for dx in (-0.17, 0.17):
            box(f"Tooth{tag}{dx}{k}", (0.1, 0.07, 0.1), (x + dx, TRACK_YR + math.cos(a) * (inner - 0.02), TRACK_ZC + math.sin(a) * (inner - 0.02)),
                P["steel"], parent, 0, rot=(a, 0, 0))
    rw = 0.2
    zw = TRACK_ZC - TRACK_R + 0.03 + rw
    for k, y in enumerate((-1.08, -0.54, 0.0, 0.54, 1.08)):
        for dx in (-0.17, 0.17):
            cylinder(f"RoadWheel{tag}{k}{dx}", rw, 0.11, (x + dx, y, zw), P["dark"], parent, segments=22, bev=0.012)
            cylinder(f"RoadHub{tag}{k}{dx}", 0.08, 0.13, (x + dx, y, zw), P["steel"], parent, segments=12)
        box(f"RoadArm{tag}{k}", (0.1, 0.34, 0.08), (x, y + 0.12, zw + 0.1), P["dark"], parent, 0.01, rot=(math.radians(-25), 0, 0))
    for k, y in enumerate((-0.7, 0.7)):
        cylinder(f"ReturnRoller{tag}{k}", 0.075, 0.3, (x, y, TRACK_ZC + TRACK_R - 0.03 - 0.075), P["dark"], parent, segments=14)
    box(f"TrackFrame{tag}", (0.18, TRACK_YF - TRACK_YR - 0.3, 0.22), (x, 0, TRACK_ZC + 0.08), P["dark"], parent, 0.02)
    # Crude Mk I touch: a bolted mud scraper behind each end wheel.
    for y, s in ((TRACK_YF + TRACK_R + 0.12, 1), (TRACK_YR - TRACK_R - 0.12, -1)):
        box(f"Scraper{tag}{s}", (TRACK_W * 0.9, 0.05, 0.3), (x, y, TRACK_ZC + 0.45), P["dark"], parent, 0.01, rot=(math.radians(-20 * s), 0, 0))


# ---------------------------------------------------------------- legs


def leg(i, P, root):
    """Stabiliser Leg{i}: pivot knuckle in a clevis on the hull, box beam along local +X with a small ram on top, a
    jack that stands vertical when the leg is at DEPLOYED_LEG, and a ribbed foot pad flat on the ground there."""
    x, y = LEG_PIVOTS[i]
    p = empty(f"Leg{i}", (x, y, LEG_Z), root)
    cylinder(f"Leg{i}Knuckle", 0.15, 0.34, (0, 0, 0), P["yellow"], p, axis="Y", segments=20, bev=0.01)
    box(f"Leg{i}Beam", (BEAM, 0.28, 0.26), (BEAM / 2 + 0.04, 0, 0), P["yellow"], p, 0.025)
    box(f"Leg{i}Plate", (BEAM * 0.7, 0.3, 0.03), (BEAM * 0.55, 0, -0.14), P["dark"], p, 0.008)
    # Leg ram along the top (it would drive the swing).
    strut(f"Leg{i}Ram", (0.12, 0, 0.2), (BEAM * 0.62, 0, 0.17), 0.045, P["dark"], p)
    strut(f"Leg{i}RamRod", (BEAM * 0.62, 0, 0.17), (BEAM * 0.85, 0, 0.14), 0.026, P["chrome"], p)
    # The jack hangs from the beam end, vertical in the deployed pose.
    a = math.radians(-DEPLOYED_LEG)
    down = Vector((math.sin(a), 0, -math.cos(a)))      # world down, in leg-local coordinates at the deployed angle
    tilt = (0, math.radians(DEPLOYED_LEG), 0)          # local rotation that makes local Z world-up when deployed
    end = Vector((BEAM, 0, 0))
    box(f"Leg{i}JackHead", (0.3, 0.3, 0.2), end + down * 0.02, P["yellow"], p, 0.02, rot=tilt)
    cylinder(f"Leg{i}Jack", 0.075, JACK, end + down * (JACK / 2), P["chrome"], p, axis="Z", segments=14).rotation_euler = tilt
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=0.1)
    mesh_object(f"Leg{i}Ball", bm, P["steel"], p, end + down * JACK, smooth=True)
    pad = empty(f"Leg{i}Pad", end + down * (JACK + PAD_T / 2), p)
    pad.rotation_euler = tilt
    # Square ribbed pad (backhoe style) on a hinge block.
    box(f"Leg{i}PadPlate", (0.56, 0.46, PAD_T), (0, 0, 0), P["dark"], pad, 0.015)
    for k in (-1, 0, 1):
        box(f"Leg{i}Rib{k}", (0.5, 0.035, 0.07), (0, k * 0.16, PAD_T / 2 + 0.03), P["dark"], pad, 0.005)
    for e in (-1, 1):
        box(f"Leg{i}PadLug{e}", (0.16, 0.04, 0.14), (0, e * 0.11, PAD_T / 2 + 0.07), P["steel"], pad, 0.008)
    # Chevrons on both faces near the foot.
    for s in (-1, 1):
        hazard(f"Leg{i}Hazard{s}", 0.7, 0.22, frame((BEAM - 0.4, s * 0.142, 0), (math.radians(-90 * s), 0, 0)), P, p, stripe=0.08)
    return p


def leg_bracket(i, P, parent):
    x, y = LEG_PIVOTS[i]
    s = 1 if x > 0 else -1
    box(f"LegMount{i}", (0.42, 0.52, 0.42), (s * 1.38, y, LEG_Z + 0.02), P["dark"], parent, 0.03)
    for e in (-1, 1):
        box(f"LegClevis{i}{e}", (0.34, 0.04, 0.36), (x, y + e * 0.195, LEG_Z), P["dark"], parent, 0.012)
    cylinder(f"LegPin{i}", 0.06, 0.46, (x, y, LEG_Z), P["steel"], parent, axis="Y", segments=12)


# ---------------------------------------------------------------- boom, drill, rams


def tapered_beam(name, y0, y1, hw0, hh0, hw1, hh1, mat, parent):
    bm = bmesh.new()
    vs = []
    for y, hw, hh in ((y0, hw0, hh0), (y1, hw1, hh1)):
        vs.append([bm.verts.new((sx * hw, y, sz * hh)) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    a, b = vs
    bm.faces.new(list(reversed(a)))
    bm.faces.new(b)
    for k in range(4):
        bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_object(name, bm, mat, parent)
    bv["bevel"](o, 0.025, 2, 30)
    return o


def boom(P, root):
    """Box-section boom (tapered, stiffener bands, owner stripe on top, chevrons at the tip), heel knuckle on the
    A-frame pin, a cross pin for the two lift rams, work lamp under the tip and a clevis carrying the Drill."""
    b = empty("Boom", BOOM_PIVOT, root)
    y0, y1 = -0.2, 4.95
    hw0, hh0, hw1, hh1 = 0.23, 0.27, 0.18, 0.2

    def half(y):
        t = (y - y0) / (y1 - y0)
        return hw0 + (hw1 - hw0) * t, hh0 + (hh1 - hh0) * t

    tapered_beam("BoomBeam", y0, y1, hw0, hh0, hw1, hh1, P["yellow"], b)
    cylinder("BoomHeel", 0.27, 0.46, (0, 0, 0), P["yellow"], b, segments=24, bev=0.02)
    for k, y in enumerate((0.9, 1.8, 3.3, 4.2)):
        hw, hh = half(y)
        box(f"BoomBand{k}", (2 * hw + 0.04, 0.06, 2 * hh + 0.04), (0, y, 0), P["yellow"], b, 0.01)
    # Owner stripe along the top (reads from the game camera), a decal following the taper.
    bm = bmesh.new()
    ya, yb = 0.35, 4.6
    (wa, ha), (wb, hb) = half(ya), half(yb)
    bm.faces.new([bm.verts.new(v) for v in ((-0.11, ya, ha + 0.004), (0.11, ya, ha + 0.004), (0.11, yb, hb + 0.004), (-0.11, yb, hb + 0.004))])
    o = mesh_object("BoomStripe", bm, P["owner"], b)
    o.visible_shadow = False
    for s in (-1, 1):
        hw, hh = half(4.35)
        hazard(f"BoomHazard{s}", 0.62, 2 * hh - 0.06, frame((s * (hw + 0.003), 4.35, 0), (math.radians(90), 0, math.radians(90 * s))), P, b, stripe=0.08)
        # Clevis plates either side of the drill head (it swings between them, clear of the tip box).
        box(f"BoomClevis{s}", (0.06, 0.7, 0.36), (s * 0.225, 5.2, 0), P["yellow"], b, 0.012)
    box("BoomTipBox", (0.5, 0.26, 0.42), (0, 4.85, 0), P["yellow"], b, 0.02)
    cylinder("BoomTipPin", 0.07, 0.6, (0, 5.4, 0), P["steel"], b, segments=12)
    # Cross pin for the lift rams.
    cylinder("RamPin", 0.07, 2 * RAM_X + 0.16, (0, RAM_PIN, 0), P["steel"], b, segments=12)
    for s in (-1, 1):
        box(f"RamLug{s}", (0.08, 0.26, 0.22), (s * (RAM_X - 0.12), RAM_PIN, 0), P["dark"], b, 0.01)
        cylinder(f"RamPinBoss{s}", 0.1, 0.06, (s * (half(RAM_PIN)[0] + 0.03), RAM_PIN, 0), P["dark"], b, segments=14)
    # Work lamp under the tip.
    hl = half(4.3)[1]
    box("BoomLampBracket", (0.3, 0.12, 0.08), (0, 4.3, -hl - 0.04), P["dark"], b, 0.01)
    cylinder("BoomLampHousing", 0.1, 0.12, (0, 4.3, -hl - 0.14), P["dark"], b, axis="Z", segments=16)
    cylinder("BoomLamp", 0.08, 0.02, (0, 4.3, -hl - 0.205), P["lamp"], b, axis="Z", segments=16)
    # Hose run along the side.
    for s in (-1, 1):
        cylinder(f"BoomHose{s}", 0.025, 4.0, (s * (0.22 + 0.03), 2.6, 0.08), P["black"], b, axis="Y", segments=8)

    drill = empty("Drill", (0, BOOM_LENGTH, 0), b)
    box("DrillHead", (0.38, 0.5, 0.52), (0, 0, -0.06), P["yellow"], drill, 0.03)
    box("DrillGear", (0.46, 0.3, 0.14), (0, 0, -0.28), P["dark"], drill, 0.02)
    cylinder("DrillMotor", 0.12, 0.18, (0, 0, 0.29), P["dark"], drill, axis="Z", segments=18, bev=0.015)
    cylinder("DrillMotorCap", 0.07, 0.04, (0, 0, 0.39), P["steel"], drill, axis="Z", segments=12)
    for s in (-1, 1):
        hazard(f"DrillHazard{s}", 0.32, 0.3, frame((0, s * 0.252, -0.04), (math.radians(-90 * s), 0, 0)), P, drill, stripe=0.07)
    string = empty("String", (0, 0, 0), drill)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.075, radius2=0.075, depth=1.0)
    mesh_object("StringRod", bm, P["steel"], string, (0, 0, -0.5))
    bit = empty("Bit", (0, 0, -0.3), drill)
    cylinder("BitCollar", 0.12, 0.15, (0, 0, -0.075), P["dark"], bit, axis="Z", segments=16, bev=0.01)
    cylinder("BitShaft", 0.065, 0.72, (0, 0, -0.5), P["bit"], bit, axis="Z", segments=14)
    # Auger flight: a tapering helicoid, 2.5 turns from z -0.17 to -0.85.
    bm = bmesh.new()
    steps, turns = 60, 2.5
    rows = []
    for k in range(steps + 1):
        t = k / steps
        a = 2 * math.pi * turns * t
        z = -0.17 - 0.68 * t
        r = 0.3 - 0.13 * t
        rows.append((bm.verts.new((0.06 * math.cos(a), 0.06 * math.sin(a), z)), bm.verts.new((r * math.cos(a), r * math.sin(a), z))))
    for k in range(steps):
        bm.faces.new((rows[k][0], rows[k][1], rows[k + 1][1], rows[k + 1][0]))
    o = mesh_object("BitFlight", bm, P["bit"], bit, smooth=True)
    o.modifiers.new("Thick", "SOLIDIFY").thickness = 0.03
    cylinder("BitTip", 0.12, 0.17, (0, 0, -0.915), P["gunmetal"], bit, axis="Z", segments=12, radius2=0.0)
    for k in range(3):
        a = 2 * math.pi * k / 3
        box(f"BitTooth{k}", (0.07, 0.05, 0.06), (0.16 * math.cos(a), 0.16 * math.sin(a), -0.86), P["gunmetal"], bit, 0.005, rot=(0, 0, a))

    rods = []
    for s in (-1, 1):
        r = empty(f"RamRod{'L' if s < 0 else 'R'}", (s * RAM_X, RAM_PIN, 0), b)
        cylinder(f"RamRodBar{s}", 0.045, RAM_PART, (0, RAM_PART / 2, 0), P["chrome"], r, axis="Y", segments=12)
        cylinder(f"RamRodEye{s}", 0.08, 0.1, (0, 0, 0), P["dark"], r, segments=14)
        rods.append(r)
    return b, drill, string, bit, rods


def rams(P, parent):
    out = []
    for s in (-1, 1):
        a = RAM_BASE + Vector((s * RAM_X, 0, 0))
        box(f"RamBase{s}", (0.2, 0.3, 0.12), a - Vector((0, 0, 0.02)), P["dark"], parent, 0.015)
        r = empty(f"Ram{'L' if s < 0 else 'R'}", a, parent)
        cylinder(f"RamBarrel{s}", 0.085, RAM_PART, (0, RAM_PART / 2 + 0.05, 0), P["yellow"], r, axis="Y", segments=16, bev=0.012)
        cylinder(f"RamGland{s}", 0.095, 0.08, (0, RAM_PART + 0.03, 0), P["dark"], r, axis="Y", segments=16)
        cylinder(f"RamEye{s}", 0.09, 0.12, (0, 0, 0), P["dark"], r, segments=14)
        out.append(r)
    return out


# ---------------------------------------------------------------- the vehicle


def mining_vehicle():
    P = palette()
    root = empty("MiningCrawler")
    body = empty("Body", (0, 0, 0), root)

    # Chassis tub between the tracks, the tracks, the hull (deck) over them.
    box("Chassis", (2 * (TRACK_X - TRACK_W / 2) - 0.02, 4.7, 0.62), (0, 0, 0.8), P["dark"], body, 0.04)
    box("BellyPlate", (1.3, 3.6, 0.05), (0, 0, 0.47), P["steel"], body, 0.01)
    for s in (-1, 1):
        track(s, P, body)
    z0, z1 = HULL_Z
    profile = [(-2.75, z0), (2.62, z0), (2.8, z0 + 0.18), (2.8, 1.72), (2.72, z1), (-2.7, z1), (-2.75, z1 - 0.05)]
    hull = prism("Hull", profile, W, P["hull"], body, bev=0.035)
    hull["dirt_band"] = (z0, z1 + 0.1)
    hull["paint"] = (0.77, 0.76, 0.7)  # the Mk I Scout's older, yellowed white

    # Bumpers with chevrons, tow hooks.
    for y, s in ((2.87, 1), (-2.82, -1)):
        box(f"Bumper{s}", (W - 0.2, 0.14, 0.26), (0, y, z0 + 0.02), P["dark"], body, 0.02)
        hazard(f"BumperHazard{s}", W - 0.4, 0.18, frame((0, y + s * 0.072, z0 + 0.02), (math.radians(-90 * s), 0, 0)), P, body, stripe=0.12)
        for x in (-0.75, 0.75):
            box(f"Tow{s}{x}", (0.09, 0.12, 0.13), (x, y + s * 0.12, z0 + 0.02), P["steel"], body, 0.015)

    # Nose: slatted grille, round headlamps in square bezels, amber markers (the Mk I Scout's face).
    box("Grille", (1.0, 0.04, 0.34), (0, 2.81, 1.5), P["dark"], body, 0.01)
    for k in range(7):
        box(f"Slat{k}", (0.03, 0.03, 0.3), (-0.39 + k * 0.13, 2.835, 1.5), P["steel"], body, 0)
    for s in (-1, 1):
        box(f"HeadlampFrame{s}", (0.3, 0.04, 0.3), (s * 0.95, 2.81, 1.5), P["dark"], body, 0.02)
        cylinder(f"HeadlampRim{s}", 0.12, 0.04, (s * 0.95, 2.83, 1.5), P["steel"], body, axis="Y", segments=20)
        cylinder(f"Headlamp{s}", 0.1, 0.04, (s * 0.95, 2.845, 1.5), P["lamp"], body, axis="Y", segments=20)
        box(f"Marker{s}", (0.1, 0.04, 0.07), (s * 1.28, 2.81, 1.66), P["amber"], body, 0.01)

    # Rear: round tail lamps ("TailLamp") in square frames, grab handles.
    for s in (-1, 1):
        cylinder(f"Tail{s}", 0.08, 0.04, (s * 1.05, -2.765, 1.52), P["redlamp"], body, axis="Y", segments=16)
        box(f"TailFrame{s}", (0.22, 0.02, 0.22), (s * 1.05, -2.752, 1.52), P["dark"], body, 0.01)
        box(f"Indicator{s}", (0.12, 0.03, 0.07), (s * 1.05, -2.76, 1.35), P["amber"], body, 0.01)
    for x in (-0.45, 0.45):
        cylinder(f"Grab{x}", 0.02, 0.3, (x, -2.785, 1.6), P["steel"], body, axis="X", segments=8)

    # Hull sides: owner stripe at the waist (broken by a primer replacement panel on the right), riveted seam, name.
    for s in (-1, 1):
        xs = s * W / 2
        if s > 0:
            pts = [(-2.7, 1.68), (0.35, 1.68), None, (1.25, 1.68), (2.76, 1.68)]
            pr = box("PrimerPanel", (0.012, 0.9, 0.66), (s * (W / 2 + 0.006), 0.8, 1.46), P["primer"], body, 0.004)
            pr.visible_shadow = False
        else:
            pts = [(-2.7, 1.68), (2.76, 1.68)]
        ribbon(f"Flash{s}", pts, 0.08, P["owner"], body, x=xs, outward=s)
        box(f"Seam{s}", (0.015, 5.3, 0.02), (s * (W / 2 + 0.003), 0.0, 1.22), P["dark"], body, 0)
        for k in range(22):
            yk = -2.6 + k * 0.245
            if s > 0 and 0.3 < yk < 1.3:
                continue
            cylinder(f"Rivet{s}{k}", 0.014, 0.02, (s * (W / 2 + 0.006), yk, 1.26), P["steel"], body, axis="X", segments=6)
        text(f"Name{s}", NAME, 0.2, (s * (W / 2 + 0.004), -0.75, 1.45), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], body, max_width=1.2)
        leg_bracket(0 if s > 0 else 2, P, body)
        leg_bracket(1 if s > 0 else 3, P, body)
    # Boarding steps under the left cab door.
    for k, z in enumerate((1.28, 1.62)):
        box(f"BoardStep{k}", (0.2, 0.42, 0.04), (-(W / 2 + 0.09), 1.75, z), P["steel"], body, 0.01)
        for e in (-1, 1):
            box(f"BoardStepHanger{k}{e}", (0.18, 0.03, 0.08), (-(W / 2 + 0.08), 1.75 + e * 0.2, z + 0.03), P["dark"], body, 0)

    # Cab at the front: boxy, split flat windscreen with a centre post, wipers, door windows.
    cab_prof = [(1.3, z1), (2.62, z1), (2.62, 2.22), (2.44, 2.95), (1.3, 2.95)]
    cab = prism("Cab", cab_prof, CAB_W, P["hull"], body, bev=0.035)
    cab["dirt_band"] = (z1, z1 + 0.6)
    cab["paint"] = (0.77, 0.76, 0.7)
    mid, n, along, rx = bv["on_slope"]((2.62, 2.22), (2.44, 2.95))
    box("WindscreenFrame", (CAB_W * 0.92, 0.03, 0.68), mid + n * 0.008, P["frame"], body, 0.015, rot=(rx, 0, 0))
    bm = bmesh.new()
    for x in (-0.48, 0.48):
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((x, 0, 0)) @ Matrix.Diagonal((0.86, 0.03, 0.56, 1)))
    ws = mesh_object("Windscreen", bm, P["glass"], body, mid + n * 0.02)
    ws.rotation_euler = (rx, 0, 0)
    box("WindscreenPost", (0.09, 0.05, 0.68), mid + n * 0.025, P["frame"], body, 0.01, rot=(rx, 0, 0))
    bv["wipers"](P, body, mid, n, along, 0.28, (-0.84, 0.12), 0.48, 0.05)
    for s in (-1, 1):
        cx = s * CAB_W / 2
        box(f"CabWindowFrame{s}", (0.02, 0.78, 0.52), (s * (CAB_W / 2 + 0.002), 1.98, 2.6), P["panel"], body, 0.01)
        box(f"CabWindow{s}", (0.03, 0.7, 0.44), (s * (CAB_W / 2 + 0.009), 1.98, 2.6), P["glass"], body, 0.008)
        for yy in (1.5, 2.46):
            box(f"DoorSeam{s}{yy}", (0.02, 0.025, 0.98), (s * (CAB_W / 2 + 0.003), yy, 2.38), P["dark"], body, 0)
        box(f"Handle{s}", (0.04, 0.14, 0.04), (s * (CAB_W / 2 + 0.025), 1.62, 2.12), P["steel"], body, 0.01)
        ribbon(f"CabFlash{s}", [(1.32, 2.24), (2.6, 2.24)], 0.06, P["owner"], body, x=cx, outward=s)
        text(f"Number{s}", NUMBER, 0.24, (s * (CAB_W / 2 + 0.014), 1.98, 2.03), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], body, max_width=0.6)
        # Cab grab rail by the door.
        cylinder(f"CabGrab{s}", 0.02, 0.6, (s * (CAB_W / 2 + 0.06), 1.4, 2.4), P["steel"], body, axis="Z", segments=8)
    # Cab roof: owner band across the back edge, boom rest, spots, beacon, whip antenna.
    box("RoofBand", (CAB_W - 0.1, 0.22, 0.012), (0, 1.48, 2.956), P["owner"], body, 0).visible_shadow = False
    box("BoomRestBase", (0.7, 0.24, 0.06), (0, 2.15, 2.98), P["dark"], body, 0.01)
    for s in (-1, 1):
        box(f"BoomRestCheek{s}", (0.06, 0.2, 0.26), (s * 0.27, 2.15, 3.1), P["yellow"], body, 0.01)
    box("BoomRestPad", (0.46, 0.18, 0.05), (0, 2.15, 3.2), P["black"], body, 0.01)
    for k, x in enumerate((-0.78, 0.78)):
        box(f"SpotBracket{k}", (0.05, 0.05, 0.14), (x, 2.3, 3.0), P["dark"], body, 0)
        cylinder(f"SpotHousing{k}", 0.1, 0.13, (x, 2.3, 3.12), P["dark"], body, axis="Y", segments=18, bev=0.02)
        cylinder(f"Spot{k}", 0.08, 0.03, (x, 2.37, 3.12), P["lamp"], body, axis="Y", segments=18)
    cylinder("BeaconBase", 0.09, 0.05, (0.8, 1.5, 2.975), P["dark"], body, axis="Z", segments=14)
    cylinder("Beacon", 0.07, 0.13, (0.8, 1.5, 3.06), P["amber"], body, axis="Z", segments=14)
    cylinder("AntennaBase", 0.05, 0.12, (-0.82, 1.45, 3.0), P["dark"], body, axis="Z", segments=12)
    cylinder("Antenna", 0.012, 1.6, (-0.82, 1.45, 3.86), P["steel"], body, axis="Z", segments=6, radius2=0.005)
    cylinder("AntennaTip", 0.02, 0.03, (-0.82, 1.45, 4.67), P["dark"], body, axis="Z", segments=8)

    # Power pack housing behind the cab: grey panels, louvres, a radiator grille on top, a primer access door.
    box("PowerPack", (1.44, 1.72, 0.72), (0, 0.36, z1 + 0.36), P["panel"], body, 0.03)
    box("Radiator", (1.0, 1.1, 0.06), (0, 0.4, z1 + 0.74), P["dark"], body, 0.01)
    for k in range(9):
        box(f"RadiatorSlat{k}", (0.96, 0.03, 0.03), (0, -0.08 + k * 0.12, z1 + 0.78), P["steel"], body, 0)
    for s in (-1, 1):
        for k in range(5):
            box(f"Louvre{s}{k}", (0.02, 0.7, 0.03), (s * 0.725, 0.75, z1 + 0.2 + k * 0.08), P["dark"], body, 0)
    door = box("PowerPackDoor", (0.012, 0.6, 0.5), (-0.726, -0.15, z1 + 0.36), P["primer"], body, 0.004)
    door.visible_shadow = False
    box("PowerPackHandle", (0.03, 0.12, 0.03), (-0.74, 0.05, z1 + 0.36), P["steel"], body, 0)
    # Deck hand rails (yellow) along both sides.
    for s in (-1, 1):
        cylinder(f"Rail{s}", 0.025, 2.6, (s * 1.36, -0.15, z1 + 0.5), P["yellow"], body, axis="Y", segments=8)
        for y in (-1.42, -0.15, 1.12):
            cylinder(f"RailPost{s}{y}", 0.022, 0.5, (s * 1.36, y, z1 + 0.25), P["yellow"], body, axis="Z", segments=8)

    # A-frame carrying the boom pin, lift rams' hull lugs.
    for s in (-1, 1):
        plate = prism(f"AFrame{s}", [(-1.5, z1), (-0.66, z1), (-0.98, BOOM_PIVOT.z + 0.05), (-1.22, BOOM_PIVOT.z + 0.05)], 0.07, P["yellow"], body, bev=0.012)
        plate.location.x = s * 0.31
        cylinder(f"AFrameBoss{s}", 0.17, 0.09, (s * 0.31, BOOM_PIVOT.y, BOOM_PIVOT.z), P["yellow"], body, segments=20, bev=0.01)
    cylinder("BoomPin", 0.08, 0.86, BOOM_PIVOT, P["steel"], body, segments=12)
    box("AFrameTie", (0.62, 0.12, 0.5), (0, -1.1, z1 + 0.25), P["dark"], body, 0.02)

    # Hopper at the back: steel bin with ribs and a rim, the ore load ("Fill") inside, name across the back.
    hz0, hz1 = z1, 3.0
    hy0, hy1 = -2.72, -1.56
    hx = 1.3
    for nm, size, loc in (("HopperBack", (2 * hx, 0.08, hz1 - hz0), (0, hy0 + 0.04, (hz0 + hz1) / 2)),
                          ("HopperFront", (2 * hx, 0.08, hz1 - hz0), (0, hy1 - 0.04, (hz0 + hz1) / 2)),
                          ("HopperSideL", (0.08, hy1 - hy0, hz1 - hz0), (-hx + 0.04, (hy0 + hy1) / 2, (hz0 + hz1) / 2)),
                          ("HopperSideR", (0.08, hy1 - hy0, hz1 - hz0), (hx - 0.04, (hy0 + hy1) / 2, (hz0 + hz1) / 2)),
                          ("HopperFloor", (2 * hx, hy1 - hy0, 0.08), (0, (hy0 + hy1) / 2, hz0 + 0.04))):
        box(nm, size, loc, P["hopper"], body, 0.015)
    for s in (-1, 1):
        cylinder(f"HopperRimSide{s}", 0.04, hy1 - hy0 + 0.08, (s * hx, (hy0 + hy1) / 2, hz1), P["steel"], body, axis="Y", segments=8)
        for y in (-2.45, -1.85):
            box(f"HopperRib{s}{y}", (0.05, 0.08, hz1 - hz0 - 0.1), (s * (hx + 0.02), y, (hz0 + hz1) / 2), P["hopper"], body, 0.01)
        box(f"HopperRibBack{s}", (0.08, 0.05, hz1 - hz0 - 0.1), (s * 0.95, hy0 - 0.02, (hz0 + hz1) / 2), P["hopper"], body, 0.01)
    for y in (hy0, hy1):
        cylinder(f"HopperRim{y}", 0.04, 2 * hx + 0.08, (0, y, hz1), P["steel"], body, axis="X", segments=8)
    pr = box("HopperPrimer", (0.012, 0.5, 0.5), (hx + 0.008, -2.15, 2.35), P["primer"], body, 0.004)
    pr.visible_shadow = False
    hazard("HopperHazard", 2 * hx - 0.1, 0.12, frame((0, hy0 - 0.05, hz1 - 0.12), (math.radians(90), 0, 0)), P, body, stripe=0.1)
    text("NameRear", NAME, 0.24, (0, hy0 - 0.008, 2.45), (math.radians(90), 0, 0), P["number"], body, max_width=1.6)
    fill = empty("Fill", (0, (hy0 + hy1) / 2, hz0 + 0.08), body)
    ore_mound(P, fill, hx - 0.09, (hy1 - hy0) / 2 - 0.09, hz1 - hz0 - 0.1)

    legs = [leg(i, P, root) for i in range(4)]
    bm_, drill, string, bit, rods = boom(P, root)
    barrels = rams(P, body)
    return root, {"legs": legs, "boom": bm_, "drill": drill, "string": string, "bit": bit, "rods": rods, "barrels": barrels,
                  "fill": fill}


def ore_mound(P, parent, hx, hy, h):
    """The ore load: a block whose top is a lumpy heap peaking in the middle, 0..h above the Fill origin (the game
    scales Fill's Z from 0.01 to 1), plus a few loose lumps on top."""
    bm = bmesh.new()
    nx, ny = 10, 6
    grid = {}
    for i in range(nx + 1):
        for j in range(ny + 1):
            u, v = i / nx * 2 - 1, j / ny * 2 - 1
            heap = 1.0 - 0.32 * (u * u + v * v)
            bump = 0.05 * math.sin(7.1 * u + 1.3) * math.cos(5.3 * v + 0.4) + 0.03 * math.sin(13 * u * v + 2)
            grid[i, j] = bm.verts.new((u * hx, v * hy, h * (heap + bump)))
    for i in range(nx):
        for j in range(ny):
            bm.faces.new((grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
    ring = [grid[i, 0] for i in range(nx + 1)] + [grid[nx, j] for j in range(1, ny + 1)] + \
           [grid[i, ny] for i in range(nx - 1, -1, -1)] + [grid[0, j] for j in range(ny - 1, 0, -1)]
    low = [bm.verts.new((v.co.x, v.co.y, 0.0)) for v in ring]
    for k in range(len(ring)):
        a, b = ring[k], ring[(k + 1) % len(ring)]
        bm.faces.new((b, a, low[k], low[(k + 1) % len(ring)]))
    bm.faces.new(list(reversed(low)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh_object("FillOre", bm, P["ore"], parent, smooth=True)
    for k, (u, v, r) in enumerate(((-0.4, 0.2, 0.11), (0.3, -0.3, 0.13), (0.55, 0.35, 0.09), (-0.1, -0.5, 0.1), (0.05, 0.1, 0.12))):
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
        z = h * (1.0 - 0.32 * (u * u + v * v)) - r * 0.3
        mesh_object(f"FillLump{k}", bm, P["orelight"] if k % 2 else P["ore"], parent, (u * hx, v * hy, z))


# ---------------------------------------------------------------- poses


def pose(rig, mode, fill):
    """Travel: legs folded (FOLDED_LEG), boom down on its rest, drill rod 0.3. Deployed: legs at DEPLOYED_LEG, boom
    at DEPLOYED_BOOM, Drill hanging straight, rod down to the ground. The ram barrels (Body) and rods (Boom) are aimed
    at each other's pivot - the game would do the same each frame:
      barrel angle about X = atan2(dz, dy) of (pin - base);  rod angle = atan2 of (base - pin) - boom angle."""
    deployed = mode == "deployed"
    a = DEPLOYED_LEG if deployed else FOLDED_LEG
    for i, p in enumerate(rig["legs"]):
        p.rotation_euler = (0, -math.radians(a), math.pi if i >= 2 else 0)  # = Godot (0, 0|180, a)
    phi = math.radians(DEPLOYED_BOOM if deployed else 0.0)
    rig["boom"].rotation_euler = (phi, 0, 0)
    rig["drill"].rotation_euler = (-phi, 0, 0)
    reach = TIP_HEIGHT - 1.0 if deployed else 0.3  # PlanetView.AnimateCrawler's reach
    rig["string"].scale = (1, 1, reach)
    rig["bit"].location = (0, 0, -reach)
    rig["fill"].scale = (1, 1, max(0.01, min(1.0, fill)))
    pin = Vector((0, BOOM_PIVOT.y + RAM_PIN * math.cos(phi), BOOM_PIVOT.z + RAM_PIN * math.sin(phi)))
    base = RAM_BASE
    d = pin - base
    for r in rig["barrels"]:
        r.rotation_euler = (math.atan2(d.z, d.y), 0, 0)
    for r in rig["rods"]:
        r.rotation_euler = (math.atan2(-d.z, -d.y) - phi, 0, 0)
    stroke = d.length
    print(f"pose {mode}: ram length {stroke:.2f} (barrel + rod {2 * RAM_PART:.2f}, overlap {2 * RAM_PART - stroke:.2f})")


# ---------------------------------------------------------------- preview

SHOT_CAMS = {
    # name: (camera, lens, target) per mode
    "travel": {
        "front34": (Vector((9.2, 10.8, 4.2)), 40, Vector((0, 0.5, 1.7))),
        "rear34": (Vector((-9.2, -10.2, 4.8)), 40, Vector((0, 0.3, 1.7))),
        "side": (Vector((14.0, 0.7, 2.4)), 38, Vector((0, 0.7, 1.75))),
        "game": (Vector((0.0, 16.5, 19.5)), 55, Vector((0, 0.5, 1.5))),
    },
    "deployed": {
        "front34": (Vector((11.5, 13.0, 5.6)), 40, Vector((0, 0.9, 2.6))),
        "rear34": (Vector((-11.5, -12.0, 6.0)), 40, Vector((0, 0.6, 2.6))),
        "side": (Vector((17.5, 0.9, 3.4)), 38, Vector((0, 0.9, 3.0))),
        "game": (Vector((0.0, 18.5, 22.0)), 50, Vector((0, 0.7, 2.0))),
    },
}


def spoil_heap(at):
    """Preview only (not part of the model; the game has its own Dust): a low ring of cuttings round the drill hole."""
    mat = material("Spoil", (0.3, 0.22, 0.15), 0.0, 0.95)
    bm = bmesh.new()
    rings, segs = 6, 28
    rows = []
    for i in range(rings + 1):
        r = 0.22 + 0.9 * i / rings
        row = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            h = 0.16 * math.sin(math.pi * min(1.0, (r - 0.22) / 0.55)) if r < 0.77 else 0.16 * max(0.0, 1 - (r - 0.77) / 0.35)
            h *= 1 + 0.25 * math.sin(3 * a + i)
            row.append(bm.verts.new((at.x + r * math.cos(a), at.y + r * math.sin(a), max(h, 0.0) + 0.002)))
        rows.append(row)
    for i in range(rings):
        for k in range(segs):
            bm.faces.new((rows[i][k], rows[i][(k + 1) % segs], rows[i + 1][(k + 1) % segs], rows[i + 1][k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Spoil")
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    me.materials.append(mat)
    bv["link"](bpy.data.objects.new("Spoil", me))
    for k in range(9):
        a = 2.4 * k
        r = 0.45 + 0.08 * (k % 4)
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.05 + 0.02 * (k % 3))
        mesh_object(f"SpoilLump{k}", bm, bpy.data.materials["OreLight"], None, (at.x + r * math.cos(a), at.y + r * math.sin(a), 0.12))


def preview(folder):
    """build_vehicles.preview_scene's look (sky, sun, sand), with cameras framed for the bigger machine."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = math.radians(28)
    sky.sun_rotation = math.radians(150)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.12
    scene.view_settings.exposure = -0.6
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(55), 0, math.radians(150))
    bv["link"](sun)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40)
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    bv["link"](bpy.data.objects.new("Ground", ground))
    if MODE == "deployed":
        spoil_heap(Vector((0, BOOM_PIVOT.y + BOOM_LENGTH * math.cos(math.radians(DEPLOYED_BOOM)), 0)))
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    bv["link"](cam)
    scene.camera = cam
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        pos, lens, target = SHOT_CAMS[MODE][name]
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"miner-{MODE}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def main():
    if set(SHOTS) - set(SHOT_CAMS["travel"]):
        sys.exit(f"--shots takes {', '.join(SHOT_CAMS['travel'])}")
    bv["reset"]()
    root, rig = mining_vehicle()
    pose(rig, MODE, FILL)
    bpy.context.view_layer.update()
    if BAKE:
        for name in ("Hull", "Cab"):
            bv["weather_hull"](bpy.data.objects[name])
    if OUT:
        bv["export"](root, OUT)
    if PREVIEW:
        preview(PREVIEW)


main()
