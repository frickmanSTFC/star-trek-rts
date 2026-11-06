"""Construction Rover: the ground route to a second outpost (surfaces.json construction_rover), in the same family as
the survey rovers (build_vehicles.py) and the Mining Vehicle Mk I (build_miner.py): the Mk II Surveyor's wedge cab and
wrap-around windscreen on a long six-wheeled flatbed, the yellowed white hull with the owner-colour waist stripe, a
hazard-yellow slewing crane behind the cab, the folded Colony Hub (prefab wall panels in a cradle under straps) on the
deck, outrigger jacks at the corners and an exocomp dock in the cab roof that the exocomps launch from when it
deploys. Beacon, tail lamps, wipers and the decal markers work as on the rovers.

It loads build_vehicles.py's helpers (box, cylinder, prism, ribbon, text, wheel, wipers, palette, weather_hull) the way
build_miner.py does: the source is exec'd with its trailing main() call stripped.

  /home/user/.bpyvenv/bin/python blender/build_constructor.py --preview blender/previews/vehicles --res 960
  /home/user/.bpyvenv/bin/python blender/build_constructor.py --mode deployed --shots front34,game --preview blender/previews/vehicles
  /home/user/.bpyvenv/bin/python blender/build_constructor.py --game          (= --blank --out game/models/vehicles/construction_rover.glb)
  python3 blender/build_constructor.py --sheet blender/previews/vehicles     (contact sheet, PIL, no Blender)

Flags: --mode travel|deployed (deployed = the pose at the end of unfolding: jacks down, dock open, crane out to the
left and raised, the folded hub set down beside it), --preview <dir>, --res, --samples, --shots front34,rear34,side,game,
--out <glb>, --name / --number (painted decals), --blank (decal spots as empties, build_vehicles' GAME CONTRACT),
--game, --no-bake, --no-join. Previews: <dir>/construction_rover-<mode>-<shot>.png.

Game contract (BuildingModels.Constructor.cs, PlanetView.Founding.cs; Blender +Y front, +Z up, 1 unit = 1 world unit;
every pivot has no rest rotation, so Godot's local axes are Blender's: Godot Y = Blender Z, Godot Z = -Blender Y, and a
rotation about Blender Z / X is the same angle about Godot Y / X):
  ConstructionRover (root)
    Body            everything rigid: hull, cab, chassis, deck, jack housings, dock bay; holds Beacon, the tail lamps
                    (material TailLamp), Wiper0/1 and the decal markers NameL/NameR/NameRear/NumberL/NumberR
    WheelFL/FR/ML/MR/RL/RR   wheel pivots at the hubs (flat tyres squash them), axles y 2.3 / 0.85 / -2.2, radius 0.62
    Jack0..Jack3    outrigger feet (0 left middle, 1 right middle, 2 left rear, 3 right rear); slide down Z by JACK_DROP
    DockLid         the roof dock's hatch, hinged at its front edge, lid reaching back (-Y); opens about X to LID_OPEN
    Dock            empty at the dock's centre, where the exocomps leave and come back
    Crane           slew turret behind the cab; turns about Z to SLEW (-90 = the boom out over the left side)
      Boom          luff pivot on the mast top; the boom rests pointing back (-Y) over the deck; LUFF (-38) raises it
        Hook        at the boom tip, the cable and hook block hanging down; counter-rotated by -LUFF to hang straight
    Panels          the folded hub: pivot at the bottom centre of the load; the game lifts it off the deck and sets it
                    down beside the rover (to Godot (-4.6, 0.05, z - 0.6))
  Materials: "Owner" (recoloured per player), "Amber" on the Beacon, "TailLamp" on the tail lamps, "Lamp", "DockGlow".
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


SHEET_ORDER = [("travel", "front34"), ("travel", "rear34"), ("travel", "side"), ("travel", "game"), ("deployed", "front34"),
               ("deployed", "game")]


def contact_sheet(folder):
    """3 x 2 grid of the previews with a caption on each (PIL only, so it runs without Blender)."""
    from PIL import Image, ImageDraw
    tiles = []
    for mode, shot in SHEET_ORDER:
        path = os.path.join(folder, f"construction_rover-{mode}-{shot}.png")
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
    d.text((pad + 4, 14), "Construction Rover - blender/build_constructor.py", fill=(230, 230, 230))
    for k, (label, im) in enumerate(tiles):
        x = pad + (k % cols) * (w + pad)
        y = head + (k // cols) * (h + pad)
        sheet.paste(im.resize((w, h), Image.LANCZOS), (x, y))
        d.rectangle((x, y, x + 8 + 7 * len(label), y + 18), fill=(0, 0, 0))
        d.text((x + 4, y + 4), label, fill=(255, 210, 90))
    out = os.path.join(folder, "construction_rover-sheet.png")
    sheet.save(out)
    print("wrote", out)


if "--sheet" in argv:
    contact_sheet(arg("--sheet"))
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

GAME = "--game" in argv
if GAME and "--blank" not in argv:
    argv.append("--blank")
MODE = arg("--mode", "travel")
PREVIEW = arg("--preview")
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "40"))
SHOTS = [s.strip() for s in arg("--shots", "front34,rear34,side,game").split(",") if s.strip()]
OUT = arg("--out", os.path.join(REPO, "game", "models", "vehicles", "construction_rover.glb") if GAME else None)
NAME = arg("--name", "MORAINE").upper()
NUMBER = arg("--number", "31")
BAKE = "--no-bake" not in argv
if MODE not in ("travel", "deployed"):
    sys.exit(f"--mode is travel or deployed, not {MODE}")
if GAME and MODE != "travel":
    sys.exit("--game exports the travel pose (the game unfolds it)")


def load_build_vehicles():
    """build_vehicles.py's functions without running its main(). Only --blank is passed on (text() reads BLANK)."""
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

# ---------------------------------------------------------------- the game's numbers (BuildingModels.Constructor.cs)

JACK_DROP = 0.85
LID_OPEN = -110.0
SLEW = -90.0
LUFF = -38.0
PANELS_DOWN = Vector((-4.6, 0.6, 0.05))  # Godot (-4.6, 0.05, z - 0.6): x, y offset (Blender), ground height

# ---------------------------------------------------------------- layout (Blender, +Y front)

W = 2.5                        # hull / deck width
WHEEL_R, WHEEL_W = 0.62, 0.44
AXLES = {"F": 2.3, "M": 0.85, "R": -2.2}
WHEEL_X = 1.33
DECK_Z = 1.55                  # top of the flatbed
DECK_Y = (-3.45, 1.2)
CAB_Y0 = 1.25                  # back of the cab
ROOF_Z = 2.72
CRANE_AT = Vector((0.0, 0.72, DECK_Z))
MAST = 1.55                    # boom pivot above the turret base
BOOM_LEN = 4.3
PANELS_AT = Vector((0.0, -1.6, DECK_Z + 0.02))
PANELS_L, PANELS_W, PANELS_H = 3.4, 2.2, 1.0
JACKS = [(-1.42, -0.7), (1.42, -0.7), (-1.42, -3.05), (1.42, -3.05)]  # between the middle and rear wheels, and behind the rear
JACK_Z = 0.92                  # jack foot height when stowed
DOCK_Y = (1.42, 2.32)          # the dock bay in the cab roof (back, front)


def palette():
    P = bv["tier_palette"](bv["palette"]())
    P.update({
        "yellow": material("WorkYellow", (0.92, 0.5, 0.02), 0.1, 0.5),
        "black": material("HazardBlack", (0.02, 0.02, 0.022), 0.0, 0.6),
        "deck": material("DeckPlate", (0.3, 0.31, 0.32), 0.75, 0.45),
        "module": material("ModulePanel", (0.86, 0.86, 0.84), 0.1, 0.45, coat=0.15),
        "seam": material("ModuleSeam", (0.22, 0.23, 0.25), 0.4, 0.5),
        "window": material("ModuleWindow", (0.06, 0.09, 0.12), 0.3, 0.08, emission=(0.35, 0.6, 0.9), strength=0.25, coat=1.0),
        "strap": material("Strap", (0.85, 0.42, 0.04), 0.0, 0.8),
        "dockglow": material("DockGlow", (0.4, 0.8, 1.0), 0.0, 0.3, emission=(0.35, 0.75, 1.0), strength=3.0),
        "exo": material("ExocompShell", (0.86, 0.87, 0.9), 0.55, 0.32),
    })
    return P


def frame(loc, rot=(0, 0, 0)):
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


def hazard(name, w, h, matrix, P, parent, stripe=0.09):
    """Black-and-yellow chevron panel (build_miner's): a w x h rectangle in the local XY plane facing +Z. No shadow."""
    bm = bmesh.new()
    rect = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
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


# ---------------------------------------------------------------- the vehicle


def cab(P, body):
    """The Mk II Surveyor's face on a taller cab: wedge nose, wrap-around tinted windscreen, light bar, the dock bay in
    the roof, door with the hull number, name above the side window."""
    prof = [(CAB_Y0, 0.82), (3.38, 0.82), (3.6, 1.02), (3.62, 1.42), (3.4, 1.62), (2.68, 2.62), (2.4, ROOF_Z), (CAB_Y0, ROOF_Z)]
    c = bv["prism"]("Cab", prof, W, P["hull"], body, bev=0.06)
    c["dirt_band"] = (0.82, 1.8)
    c["paint"] = (0.77, 0.76, 0.7)
    for y in (AXLES["F"],):
        cutter = cylinder("ArchCut", WHEEL_R + 0.12, W + 0.4, (0, y, WHEEL_R), P["dark"], None, segments=32)
        mod = c.modifiers.new("Arch", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cutter
        cutter.hide_render = True
        cutter.hide_viewport = True
    c.modifiers.move(c.modifiers.find("Bevel"), len(c.modifiers) - 1)

    # Windscreen on the nose slope, side panes, wipers.
    lo, hi = (3.4, 1.62), (2.68, 2.62)
    mid, n, along, rx = bv["on_slope"](lo, hi)
    box("WindscreenFrame", (W * 0.9, 0.03, 1.2), mid + n * 0.008, P["frame"], body, 0.02, rot=(rx, 0, 0))
    box("Windscreen", (W * 0.85, 0.04, 1.1), mid + n * 0.02, P["glass"], body, 0.02, rot=(rx, 0, 0))
    bv["wipers"](P, body, mid + n * 0.02, n, along, 0.55, (-0.95, 0.12), 0.84, 0.04)
    for s in (-1, 1):
        x = s * (W / 2 + 0.004)
        bv["side_poly"](f"SideGlass{s}", [(3.22, 1.82), (2.74, 2.5), (2.4, 2.5), (2.4, 1.82)], x, s, P["glass"], body)
        box(f"SideWindowFrame{s}", (0.02, 0.6, 0.44), (s * (W / 2 + 0.002), 1.78, 2.2), P["panel"], body, 0.01)
        box(f"SideWindow{s}", (0.03, 0.52, 0.36), (s * (W / 2 + 0.009), 1.78, 2.2), P["glass"], body, 0.008)
        for yy in (1.48, 2.36):
            box(f"DoorSeam{s}{yy}", (0.02, 0.025, 1.3), (s * (W / 2 + 0.003), yy, 1.6), P["dark"], body, 0)
        box(f"Handle{s}", (0.04, 0.14, 0.04), (s * (W / 2 + 0.025), 2.2, 1.6), P["steel"], body, 0.01)
        box(f"Step{s}", (0.22, 0.6, 0.05), (s * (W / 2 + 0.07), 1.9, 0.78), P["steel"], body, 0.012)
        ribbon(f"CabFlash{s}", [(CAB_Y0 + 0.04, 1.66), (3.38, 1.66)], 0.08, P["owner"], body, x=s * W / 2, outward=s)
        text(f"Number{s}", NUMBER, 0.3, (s * (W / 2 + 0.014), 1.9, 1.28), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], body, max_width=0.62)
        text(f"Name{s}", NAME, 0.17, (s * (W / 2 + 0.004), 1.82, 2.55), (math.radians(90), 0, math.radians(90 if s > 0 else -90)),
             P["number"], body, max_width=0.9)
        box(f"Mirror{s}", (0.06, 0.12, 0.3), (s * (W / 2 + 0.24), 2.7, 2.0), P["dark"], body, 0.01)
        strut(f"MirrorArm{s}", (s * W / 2, 2.62, 2.05), (s * (W / 2 + 0.22), 2.7, 2.05), 0.02, P["steel"], body)

    # Face: grille, headlamps, bumper with tow hooks and a hazard strip.
    box("Grille", (1.1, 0.04, 0.32), (0, 3.6, 1.2), P["dark"], body, 0.01)
    for k in range(8):
        box(f"Slat{k}", (0.03, 0.03, 0.28), (-0.47 + k * 0.135, 3.625, 1.2), P["steel"], body, 0)
    for s in (-1, 1):
        box(f"HeadlampFrame{s}", (0.34, 0.04, 0.2), (s * 0.85, 3.6, 1.24), P["dark"], body, 0.02)
        box(f"Headlamp{s}", (0.28, 0.04, 0.14), (s * 0.85, 3.62, 1.24), P["lamp"], body, 0.01)
        box(f"Marker{s}", (0.1, 0.04, 0.07), (s * 1.08, 3.58, 1.42), P["amber"], body, 0.01)
    box("FrontBumper", (W + 0.06, 0.22, 0.26), (0, 3.7, 0.86), P["dark"], body, 0.04)
    hazard("FrontHazard", W - 0.3, 0.16, frame((0, 3.815, 0.86), (math.radians(-90), 0, 0)), P, body, stripe=0.11)
    for x in (-0.8, 0.8):
        box(f"Tow{x}", (0.1, 0.12, 0.1), (x, 3.84, 0.86), P["steel"], body, 0.02)

    # Roof: light bar at the front, beacon at the back corner, the exocomp dock bay in the middle.
    box("LightBar", (1.9, 0.14, 0.1), (0, 2.46, ROOF_Z + 0.09), P["dark"], body, 0.02)
    for k in range(5):
        cylinder(f"Spot{k}", 0.06, 0.03, (-0.72 + k * 0.36, 2.54, ROOF_Z + 0.09), P["lamp"], body, axis="Y", segments=14)
    cylinder("BeaconBase", 0.09, 0.05, (0.92, CAB_Y0 + 0.12, ROOF_Z + 0.025), P["dark"], body, axis="Z", segments=14)
    cylinder("Beacon", 0.07, 0.13, (0.92, CAB_Y0 + 0.12, ROOF_Z + 0.11), P["amber"], body, axis="Z", segments=14)
    y0, y1 = DOCK_Y
    dw = 1.7
    # The bay: a raised coaming round a dark well with three exocomp cradles, glowing guide lights.
    for nm, size, loc in (("DockCoamingL", (0.08, y1 - y0, 0.16), (-dw / 2, (y0 + y1) / 2, ROOF_Z + 0.08)),
                          ("DockCoamingR", (0.08, y1 - y0, 0.16), (dw / 2, (y0 + y1) / 2, ROOF_Z + 0.08)),
                          ("DockCoamingB", (dw + 0.08, 0.08, 0.16), (0, y0, ROOF_Z + 0.08)),
                          ("DockCoamingF", (dw + 0.08, 0.08, 0.16), (0, y1, ROOF_Z + 0.08))):
        box(nm, size, loc, P["yellow"], body, 0.012)
    box("DockWell", (dw - 0.08, y1 - y0 - 0.08, 0.02), (0, (y0 + y1) / 2, ROOF_Z + 0.015), P["black"], body, 0)
    for k, x in enumerate((-0.52, 0.0, 0.52)):
        cy = (y0 + y1) / 2
        cylinder(f"Cradle{k}", 0.2, 0.05, (x, cy, ROOF_Z + 0.05), P["dark"], body, axis="Z", segments=18)
        cylinder(f"CradleLight{k}", 0.22, 0.012, (x, cy, ROOF_Z + 0.08), P["dockglow"], body, axis="Z", segments=18)
        # A docked exocomp in each cradle (the game's drones leave from here; these stay as the dock's look).
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=7, radius=0.17)
        for v in bm.verts:
            v.co.y *= 1.35
            v.co.z = max(v.co.z, -0.04) * 0.45
        mesh_object(f"DockedExocomp{k}", bm, P["exo"], body, (x, cy, ROOF_Z + 0.07), smooth=True)
    hazard("DockHazard", dw - 0.1, 0.08, frame((0, y0 - 0.045, ROOF_Z + 0.12), (math.radians(90), 0, 0)), P, body, stripe=0.06)
    return c


def chassis(P, body):
    """Ladder frame, rear hull boxes over the axles, the flatbed with rails and chequer plate, rear lamps and name."""
    box("Frame", (W - 0.5, 7.0, 0.38), (0, 0.15, 0.72), P["dark"], body, 0.03)
    hull = bv["prism"]("Hull", [(DECK_Y[0], 0.86), (CAB_Y0 + 0.02, 0.86), (CAB_Y0 + 0.02, DECK_Z - 0.04), (DECK_Y[0], DECK_Z - 0.04)],
                       W, P["hull"], body, bev=0.04)
    hull["dirt_band"] = (0.86, DECK_Z)
    hull["paint"] = (0.77, 0.76, 0.7)
    for y in (AXLES["M"], AXLES["R"]):
        cutter = cylinder("ArchCut", WHEEL_R + 0.12, W + 0.4, (0, y, WHEEL_R), P["dark"], None, segments=32)
        mod = hull.modifiers.new("Arch", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cutter
        cutter.hide_render = True
        cutter.hide_viewport = True
    hull.modifiers.move(hull.modifiers.find("Bevel"), len(hull.modifiers) - 1)
    for s in (-1, 1):
        ribbon(f"Flash{s}", [(DECK_Y[0] + 0.05, 1.42), (CAB_Y0, 1.42)], 0.08, P["owner"], body, x=s * W / 2, outward=s)
        # Primer replacement panel on the right flank (the family's patched look).
        if s > 0:
            pr = box("PrimerPanel", (0.012, 0.5, 0.2), (W / 2 + 0.006, 0.25, 1.46), P["primer"], body, 0.004)
            pr.visible_shadow = False
        # Side rails along the deck edge with stake pockets.
        cylinder(f"DeckRail{s}", 0.03, DECK_Y[1] - DECK_Y[0] - 0.2, (s * (W / 2 - 0.05), (DECK_Y[0] + DECK_Y[1]) / 2, DECK_Z + 0.32),
                 P["steel"], body, axis="Y", segments=8)
        for y in (-3.2, -2.0, -0.8, 0.4):
            cylinder(f"DeckPost{s}{y}", 0.025, 0.32, (s * (W / 2 - 0.05), y, DECK_Z + 0.16), P["steel"], body, axis="Z", segments=8)
    box("Deck", (W, DECK_Y[1] - DECK_Y[0], 0.06), (0, (DECK_Y[0] + DECK_Y[1]) / 2, DECK_Z - 0.03), P["deck"], body, 0.01)
    for k in range(14):
        box(f"DeckRib{k}", (W - 0.2, 0.03, 0.015), (0, DECK_Y[0] + 0.2 + k * 0.32, DECK_Z + 0.005), P["steel"], body, 0)
    # Rear: bumper with chevrons, tail lamps (TailLamp), indicators, the name across the tailgate, grab handles.
    box("RearBumper", (W + 0.02, 0.18, 0.24), (0, DECK_Y[0] - 0.08, 0.88), P["dark"], body, 0.03)
    hazard("RearHazard", W - 0.3, 0.16, frame((0, DECK_Y[0] - 0.175, 0.88), (math.radians(90), 0, 0)), P, body, stripe=0.11)
    for s in (-1, 1):
        cylinder(f"Tail{s}", 0.08, 0.04, (s * 1.0, DECK_Y[0] - 0.04, 1.16), P["redlamp"], body, axis="Y", segments=16)
        box(f"TailFrame{s}", (0.22, 0.02, 0.22), (s * 1.0, DECK_Y[0] - 0.028, 1.16), P["dark"], body, 0.01)
        box(f"Indicator{s}", (0.12, 0.03, 0.07), (s * 1.0, DECK_Y[0] - 0.032, 1.36), P["amber"], body, 0.01)
    box("Tailgate", (W - 0.1, 0.03, 0.5), (0, DECK_Y[0] - 0.01, 1.28), P["hull"], body, 0.01)
    text("NameRear", NAME, 0.2, (0, DECK_Y[0] - 0.03, 1.4), (math.radians(90), 0, 0), P["number"], body, max_width=1.0)
    # Boom rest at the back of the deck.
    for s in (-1, 1):
        box(f"BoomRestPost{s}", (0.08, 0.08, 1.3), (s * 0.3, DECK_Y[0] + 0.12, DECK_Z + 0.65), P["yellow"], body, 0.01)
    box("BoomRestBar", (0.7, 0.12, 0.08), (0, DECK_Y[0] + 0.12, DECK_Z + 1.3), P["dark"], body, 0.01)
    # Jack housings: yellow boxes with chevrons on brackets at the corners.
    for k, (x, y) in enumerate(JACKS):
        s = 1 if x > 0 else -1
        box(f"JackHousing{k}", (0.28, 0.28, 0.62), (x, y, JACK_Z + 0.62), P["yellow"], body, 0.02)
        box(f"JackBracket{k}", (0.3, 0.22, 0.12), (x - s * 0.18, y, JACK_Z + 0.5), P["dark"], body, 0.01)
        hazard(f"JackHazard{k}", 0.22, 0.4, frame((x + s * 0.142, y, JACK_Z + 0.62), (0, math.radians(90 * s), 0)), P, body, stripe=0.06)
    return hull


def jacks(P, root):
    out = []
    for k, (x, y) in enumerate(JACKS):
        j = empty(f"Jack{k}", (x, y, JACK_Z), root)
        cylinder(f"Jack{k}Leg", 0.08, 0.9, (0, 0, 0.45), P["steel"], j, axis="Z", segments=10)
        box(f"Jack{k}Pad", (0.42, 0.42, 0.07), (0, 0, 0.035), P["dark"], j, 0.015)
        box(f"Jack{k}PadRib", (0.3, 0.06, 0.06), (0, 0, 0.1), P["yellow"], j, 0.01)
        out.append(j)
    return out


def dock_lid(P, root):
    """Two-leaf look on one hatch, hinged along the dock's front edge, lying flush over the bay when shut."""
    y0, y1 = DOCK_Y
    lid = empty("DockLid", (0, y1, ROOF_Z + 0.19), root)
    box("DockLidPlate", (1.66, y1 - y0 - 0.02, 0.05), (0, -(y1 - y0) / 2, 0.0), P["hull"], lid, 0.015)
    box("DockLidSeam", (0.02, y1 - y0 - 0.06, 0.012), (0, -(y1 - y0) / 2, 0.03), P["dark"], lid, 0)
    hazard("DockLidHazard", 1.5, 0.12, frame((0, -(y1 - y0) + 0.1, 0.027)), P, lid, stripe=0.06)
    for x in (-0.6, 0.6):
        cylinder(f"DockHinge{x}", 0.04, 0.22, (x, 0, 0), P["steel"], lid, axis="X", segments=10)
    empty("Dock", (0, (y0 + y1) / 2, ROOF_Z + 0.35), root)
    return lid


def crane(P, root):
    """Hazard-yellow slewing crane: turret on a slew ring behind the cab, a short mast, a box boom resting back over
    the load with its hook block."""
    c = empty("Crane", CRANE_AT, root)
    cylinder("SlewRing", 0.58, 0.12, (0, 0, 0.06), P["dark"], c, axis="Z", segments=28)
    cylinder("Turret", 0.5, 0.42, (0, 0, 0.33), P["yellow"], c, axis="Z", segments=24, bev=0.02)
    box("MastL", (0.1, 0.42, MAST - 0.5), (-0.24, 0, 0.5 + (MAST - 0.5) / 2), P["yellow"], c, 0.015)
    box("MastR", (0.1, 0.42, MAST - 0.5), (0.24, 0, 0.5 + (MAST - 0.5) / 2), P["yellow"], c, 0.015)
    box("Counterweight", (0.66, 0.3, 0.4), (0, 0.3, 0.62), P["dark"], c, 0.03)
    hazard("CounterHazard", 0.6, 0.3, frame((0, 0.455, 0.62), (math.radians(-90), 0, 0)), P, c, stripe=0.07)
    box("Winch", (0.36, 0.3, 0.3), (0, 0.12, 0.95), P["panel"], c, 0.02)
    boom = empty("Boom", (0, 0, MAST), c)
    cylinder("BoomPin", 0.07, 0.6, (0, 0, 0), P["steel"], boom, segments=12)
    # Box section, tapering a little toward the tip; chevrons near the tip; sheave at the end.
    bm = bmesh.new()
    for (y, hw, hh) in ((0.0, 0.17, 0.2), (-BOOM_LEN, 0.12, 0.14)):
        for sx in (-1, 1):
            for sz in (-1, 1):
                bm.verts.new((sx * hw, y, sz * hh))
    bm.verts.ensure_lookup_table()
    v = bm.verts
    for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    arm = mesh_object("BoomArm", bm, P["yellow"], boom)
    bv["bevel"](arm, 0.02, 2)
    for s in (-1, 1):
        hazard(f"BoomHazard{s}", 0.7, 0.2, frame((s * 0.125, -BOOM_LEN + 0.55, 0), (0, math.radians(90 * s), math.radians(90))), P, boom, stripe=0.07)
        cylinder(f"Sheave{s}", 0.13, 0.05, (s * 0.09, -BOOM_LEN - 0.05, 0), P["steel"], boom, segments=16)
    strut("BoomCable", (0, -0.2, 0.24), (0, -BOOM_LEN, 0.16), 0.015, P["dark"], boom)
    hook = empty("Hook", (0, -BOOM_LEN - 0.05, 0), boom)
    cylinder("HookCable", 0.015, 0.7, (0, 0, -0.35), P["dark"], hook, axis="Z", segments=6)
    box("HookBlock", (0.2, 0.16, 0.26), (0, 0, -0.82), P["yellow"], hook, 0.02)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.06, radius2=0.02, depth=0.2)
    mesh_object("HookTip", bm, P["steel"], hook, (0, 0, -1.03))
    return c, boom, hook


def panels(P, root):
    """The folded Colony Hub: six flat prefab wall panels standing across the deck (white, framed, owner stripe, a
    window slot each), the folded roof leaves stacked flat on top, a hazard-yellow cradle and orange straps."""
    pn = empty("Panels", PANELS_AT, root)
    L, Wd, H = PANELS_L, PANELS_W, PANELS_H
    # Cradle: base skid, corner posts and a top frame.
    box("CradleSkid", (Wd + 0.06, L + 0.06, 0.1), (0, 0, 0.05), P["yellow"], pn, 0.015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"CradlePost{sx}{sy}", (0.09, 0.09, H + 0.05), (sx * Wd / 2, sy * L / 2, (H + 0.05) / 2 + 0.05), P["yellow"], pn, 0.012)
        box(f"CradleTop{sx}", (0.08, L, 0.08), (sx * Wd / 2, 0, H + 0.1), P["yellow"], pn, 0.012)
    # The wall panels: thick slabs with a raised frame on the back face, standing on edge across the deck.
    n = 6
    gap = (L - 0.2) / n
    for k in range(n):
        y = -L / 2 + 0.1 + gap * (k + 0.5)
        box(f"Panel{k}", (Wd - 0.12, 0.15, H - 0.06), (0, y, 0.1 + (H - 0.06) / 2), P["module"], pn, 0.025)
        box(f"PanelFrameT{k}", (Wd - 0.14, 0.04, 0.06), (0, y - 0.09, H - 0.02), P["seam"], pn, 0.005)
        box(f"PanelFrameB{k}", (Wd - 0.14, 0.04, 0.06), (0, y - 0.09, 0.16), P["seam"], pn, 0.005)
        for x in (-Wd / 2 + 0.12, 0.0, Wd / 2 - 0.12):
            box(f"PanelFrameV{k}{x}", (0.05, 0.04, H - 0.14), (x, y - 0.09, 0.1 + (H - 0.06) / 2), P["seam"], pn, 0.005)
        box(f"PanelStripe{k}", (Wd - 0.14, 0.02, 0.08), (0, y + 0.08, H * 0.7), P["owner"], pn, 0)
        box(f"PanelWindow{k}", (Wd * 0.46, 0.02, 0.14), (-0.3 if k % 2 else 0.3, y + 0.082, H * 0.45), P["window"], pn, 0.005)
    # The folded roof leaves: three flat wedge plates stacked on the panels.
    for k in range(3):
        z = H + 0.03 + k * 0.06
        bm = bmesh.new()
        hw, hl = Wd / 2 - 0.06 - k * 0.12, L / 2 - 0.08 - k * 0.2
        pts = [(-hw, -hl), (hw, -hl), (hw * 0.82, hl), (-hw * 0.82, hl)] if k % 2 == 0 else [(-hw * 0.82, -hl), (hw * 0.82, -hl), (hw, hl), (-hw, hl)]
        bot = [bm.verts.new((x, y, 0.0)) for x, y in pts]
        top = [bm.verts.new((x, y, 0.05)) for x, y in pts]
        bm.faces.new(list(reversed(bot)))
        bm.faces.new(top)
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((bot[i], bot[j], top[j], top[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        mesh_object(f"RoofLeaf{k}", bm, P["module"] if k != 1 else P["panel"], pn, (0, 0, z))
    box("RoofHub", (0.5, 0.5, 0.08), (0, 0, H + 0.23), P["seam"], pn, 0.02)
    for y in (-1.1, 0.0, 1.1):
        box(f"Strap{y}", (Wd + 0.1, 0.07, 0.03), (0, y, H + 0.28), P["strap"], pn, 0)
        for s in (-1, 1):
            box(f"StrapSide{y}{s}", (0.03, 0.07, H + 0.2), (s * (Wd / 2 + 0.06), y, (H + 0.28) / 2 + 0.04), P["strap"], pn, 0)
    hazard("PanelsHazard", Wd - 0.2, 0.08, frame((0, -L / 2 - 0.035, 0.05), (math.radians(90), 0, 0)), P, pn, stripe=0.06)
    return pn


def construction_rover():
    P = palette()
    root = empty("ConstructionRover")
    body = empty("Body", (0, 0, 0), root)
    cab(P, body)
    chassis(P, body)
    for axle, y in AXLES.items():
        for s, side in ((-1, "L"), (1, "R")):
            bv["wheel"](f"Wheel{axle}{side}", s * WHEEL_X, y, WHEEL_R, WHEEL_W, P, root)
            box(f"Arm{axle}{s}", (0.36, 0.16, 0.14), (s * (WHEEL_X - 0.36), y, WHEEL_R + 0.05), P["steel"], body, 0.02)
    rig = {"jacks": jacks(P, root), "lid": dock_lid(P, root), "panels": panels(P, root)}
    rig["crane"], rig["boom"], rig["hook"] = crane(P, root)
    return root, rig


def pose(rig, mode):
    """The game's unfolded pose (PlanetView.PoseConstructor at f = 1, the panels set down beside it)."""
    if mode != "deployed":
        return
    for j in rig["jacks"]:
        j.location.z -= JACK_DROP
    rig["lid"].rotation_euler.x = math.radians(LID_OPEN)
    rig["crane"].rotation_euler.z = math.radians(SLEW)
    rig["boom"].rotation_euler.x = math.radians(LUFF)
    rig["hook"].rotation_euler.x = math.radians(-LUFF)
    p = rig["panels"]
    p.location = Vector((PANELS_DOWN.x, p.location.y + PANELS_DOWN.y, PANELS_DOWN.z))


# ---------------------------------------------------------------- preview and export

SHOT_CAMS = {
    "travel": {
        "front34": (Vector((10.0, 11.5, 4.4)), 40, Vector((0, 0.2, 1.6))),
        "rear34": (Vector((-10.0, -11.0, 5.0)), 40, Vector((0, -0.3, 1.6))),
        "side": (Vector((15.0, 0.2, 2.4)), 38, Vector((0, 0.1, 1.7))),
        "game": (Vector((0.0, 18.0, 21.0)), 55, Vector((0, 0.0, 1.4))),
    },
    "deployed": {
        "front34": (Vector((9.0, 15.0, 6.5)), 40, Vector((-2.0, 0.0, 2.2))),
        "rear34": (Vector((-12.0, -12.5, 6.8)), 40, Vector((-1.8, -0.4, 2.2))),
        "side": (Vector((-17.0, -0.5, 3.8)), 38, Vector((-1.5, 0.0, 2.4))),
        "game": (Vector((-2.0, 18.0, 22.0)), 50, Vector((-1.8, 0.0, 1.6))),
    },
}


def preview(folder):
    """build_vehicles.preview_scene's look (sky, sun, sand), with cameras framed for the longer vehicle."""
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
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    bv["link"](cam)
    scene.camera = cam
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        pos, lens, target = SHOT_CAMS[MODE][name]
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"construction_rover-{'glb-' if OUT else ''}{MODE}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


# Nodes the game moves or looks up by name: they stay separate objects; everything else is joined per rigid group.
KEEP = ({"Body", "Beacon", "DockLid", "Dock", "Crane", "Boom", "Hook", "Panels"}
        | set(bv["MARKERS"].values())
        | {f"Wheel{a}{s}" for a in "FMR" for s in "LR"} | {f"Jack{i}" for i in range(4)}
        | {f"Wiper{k}" for k in range(2)} | {f"Wiper{k}Blade" for k in range(2)})


def join_static(root):
    """build_miner.join_static: one mesh per rigid group ("<group>Mesh"), modifiers applied, bare empties removed.
    Every part gets one UV layer named "UVMap" first so the baked paint keeps its UVs through the join."""
    def kept(o):
        return o is root or o.name in KEEP

    groups = {}
    for o in root.children_recursive:
        if o.type == "MESH" and not kept(o):
            g = o.parent
            while not kept(g):
                g = g.parent
            groups.setdefault(g, []).append(o)
    parts = [o for os_ in groups.values() for o in os_]
    for o in parts:
        uv = o.data.uv_layers
        if len(uv) == 0:
            uv.new(name="UVMap")
        else:
            for extra in list(uv)[1:]:
                uv.remove(extra)
            uv[0].name = "UVMap"
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.convert(target="MESH")
    for g, members in groups.items():
        target = bv["link"](bpy.data.objects.new(f"{g.name}Mesh", bpy.data.meshes.new(f"{g.name}Mesh")), g)
        target.data.uv_layers.new(name="UVMap")
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        for o in members:
            o.select_set(True)
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.join()
    while True:
        bare = [o for o in root.children_recursive if o.type == "EMPTY" and not kept(o) and not o.children]
        if not bare:
            break
        for o in bare:
            bpy.data.objects.remove(o)
    left = [root] + list(root.children_recursive)
    missing = sorted(KEEP - {o.name for o in left})
    if missing:
        sys.exit(f"join_static: lost {missing}")
    print(f"join_static: {len(parts)} meshes -> {len(groups)} joined; {len(left)} objects, {sum(o.type == 'MESH' for o in left)} meshes")


def export_game(root, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        if not o.hide_render:
            o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_lights=False, export_cameras=False, export_image_format="JPEG",
                              export_jpeg_quality=90, export_image_quality=90)
    print("exported", path, os.path.getsize(path) // 1024, "KB")


def main():
    if set(SHOTS) - set(SHOT_CAMS["travel"]):
        sys.exit(f"--shots takes {', '.join(SHOT_CAMS['travel'])}")
    bv["reset"]()
    root, rig = construction_rover()
    pose(rig, MODE)
    bpy.context.view_layer.update()
    if BAKE:
        for name in ("Cab", "Hull"):
            bv["weather_hull"](bpy.data.objects[name])
    if OUT:
        if "--no-join" not in argv:
            join_static(root)
        export_game(root, OUT)
    if PREVIEW:
        preview(PREVIEW)


main()
