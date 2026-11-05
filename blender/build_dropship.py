"""Federation drop ship MOCKUP: the heavy-lift lander that brings a ground vehicle (survey rover or mining crawler) down
from the orbiting ship and flies back up (the game's orbit "shuttle" delivery, surfaces.json orbit.shuttle).

An original design in the rover family's paint language (white/grey scuffed hull, "Owner" flash stripes, hull number
and name decals, amber beacon, green-tinted glass): a box-section cargo fuselage with a wedge flight deck on the nose,
two VTOL pods on short pylons, four folding landing legs under the pods and a rear ramp door that drops to the ground.
The bay (3.6 wide x 7.2 long x 3.35 high inside) takes one mining crawler (5.6 x 2.7 x 3.1) or a survey rover.
Overall 12.6 long, 9.4 wide over the landing pads, 5.6 high to the beacon, 6.3 to the mast tip (a hex is 25 across).

Run (bpy 5.x, see CLAUDE.md "Vehicle models"):
  /root/bpyenv/bin/python blender/build_dropship.py --pose landed --preview blender/previews/dropship --res 640 --samples 16
  /root/bpyenv/bin/python blender/build_dropship.py --pose unloading --shots rear34 --preview blender/previews/dropship
  /root/bpyenv/bin/python blender/build_dropship.py --pose landed --blank --out /tmp/dropship.glb   (no preview)
  python3 blender/build_dropship.py --sheet blender/previews/dropship      (contact sheet; needs PIL, not bpy)

Flags:
  --pose landed|flying|unloading   landed: legs down, ramp closed, engines idle. flying: 6 units up, legs folded,
                                   thrusters lit. unloading: ramp down with a Mk I Scout rover half way down it.
  --preview <dir>   render the shots into <dir>/dropship-<pose>-<shot>.png
  --shots a,b       front34, rear34, side, game (game camera tilt, next to a Mk II rover and a hex outline for scale)
  --res, --samples  render size (width; height is 2/3) and Cycles samples
  --out <file.glb>  export the ship alone (never the preview rover, plumes or lights). Not into game/models yet.
  --blank           decals as empties NameL, NameR, NameRear, NumberL, NumberR (build_vehicles.py --blank convention:
                    text in the empty's local XY plane facing local +Z, Blender scale (max_width, size, 1))
  --name, --number  painted name and hull number (default HALYARD 07)
  --no-bake         skip the scuffed-paint bakes (faster layout checks)

PARTS THE GAME CAN DRIVE (Blender +Y front, +Z up, 1 unit = 1 world unit; the export is in the landed rest pose):
  Ramp        empty on the hinge line (0, -6.0, 1.25) at the bay floor's rear edge. Rest = closed (slab standing up
              in the door). Open = rotate about local +X by RAMP_OPEN (109.6 degrees, a 19.6 degree slope; the toe rests on the
              ground). Godot (glTF): rest * Basis(Vector3.Right, angle), same sign.
  Leg0..Leg3  empties at each leg's hinge under a pod (0 front-left -X, 1 front-right, 2 rear-left, 3 rear-right).
              Rest = down (landed). Retract = rotate about local +X: front legs -90 degrees (fold aft), rear legs +90
              (fold forward); folded they lie under the pod.
  Foot0..Foot3 empties (children of LegK) at the ankle; rotate about local X by minus the leg's angle to keep the pad
              level while folding (optional).
  Thruster0..Thruster5  glowing meshes, each with its own material (ThrusterK): 0/1 pod VTOL grilles (left/right),
              2/3 chin VTOL grilles, 4/5 aft impulse exhausts on the pods. Drive their emission (idle ~0.5, lift ~30).
  NacelleGlow0/1  blue grille strips along the pods' tops. BayLight: the bay's ceiling strips (own material).
  Beacon      amber lamp on the dorsal spine (like the rovers' Beacon).
  Material "Owner" = flash stripes, recoloured per player.
"""
import math
import os
import re
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


# ---------------------------------------------------------------- contact sheet (plain python3 + PIL, no bpy)

SHEET = [("landed", "front34"), ("landed", "rear34"), ("landed", "side"), ("flying", "front34"), ("unloading", "rear34"),
         ("landed", "game")]

if "--sheet" in argv:
    from PIL import Image, ImageDraw
    folder = arg("--sheet")
    tiles = [(f"{p} {s}", Image.open(os.path.join(folder, f"dropship-{p}-{s}.png")).convert("RGB")) for p, s in SHEET]
    w, h = 640, 640 * tiles[0][1].size[1] // tiles[0][1].size[0]
    sheet = Image.new("RGB", (w * 3, h * 2), (20, 22, 26))
    draw = ImageDraw.Draw(sheet)
    for k, (label, im) in enumerate(tiles):
        x, y = (k % 3) * w, (k // 3) * h
        sheet.paste(im.resize((w, h)), (x, y))
        draw.rectangle((x + 8, y + 8, x + 16 + 7 * len(label), y + 26), fill=(10, 12, 16))
        draw.text((x + 12, y + 11), label.upper(), fill=(235, 235, 240))
    sheet.save(os.path.join(folder, "dropship-sheet.png"))
    print("wrote", os.path.join(folder, "dropship-sheet.png"))
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rover_finish  # noqa: E402  (its bake helpers paint the ship's hull)

POSE = arg("--pose", "landed")
PREVIEW = arg("--preview")
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "40"))
SHOTS = [s.strip() for s in arg("--shots", "front34,rear34,side").split(",") if s.strip()]
OUT = arg("--out")
BLANK = "--blank" in argv
BAKE = "--no-bake" not in argv
NAME = arg("--name", "HALYARD").upper()
NUMBER = arg("--number", "07")
if POSE not in ("landed", "flying", "unloading"):
    sys.exit(f"--pose takes landed, flying or unloading, not {POSE}")
if set(SHOTS) - {"front34", "rear34", "side", "game"}:
    sys.exit("--shots takes front34, rear34, side and game")


def load_build_vehicles():
    """render_finish.py's loader: exec build_vehicles.py without its trailing main() and with none of our flags, so
    its helpers (box, cylinder, prism, text, palette, weather_hull, rover_mk1/2, export...) land in a namespace."""
    path = os.path.join(HERE, "build_vehicles.py")
    with open(path) as f:
        src = f.read()
    src = re.sub(r"^main\(\)\s*$", "", src, flags=re.M)
    saved = sys.argv
    sys.argv = [path]
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


V = load_build_vehicles()
box, cylinder, prism, strut, empty, link = V["box"], V["cylinder"], V["prism"], V["strut"], V["empty"], V["link"]
material, mesh_object, bevel, on_slope = V["material"], V["mesh_object"], V["bevel"], V["on_slope"]

# ---------------------------------------------------------------- dimensions

W = 4.6                 # fuselage width (x) aft of the nose taper, below the shoulder
HW = W / 2
TAPER_FROM, NOSE_Y, NOSE_HW = 3.0, 6.6, 0.7 * HW   # plan-view taper of the nose: half width shrinks linearly
BELLY, ROOF = 0.85, 4.98
SHOULDER, ROOF_K = 4.0, 0.86   # upper flanks lean in above the shoulder to ROOF_K x half width at the roof
FLOOR = 1.25            # bay deck top (= ramp hinge height)
BAY_TOP = 4.6
BAY_HW = 1.8            # bay half width inside
BAY_FRONT = 1.2         # bay front bulkhead (y)
REAR = -6.0             # hull rear face / ramp hinge line (y)
RAMP_T = 0.26           # ramp slab thickness
RAMP_LEN = BAY_TOP - FLOOR - 0.03
RAMP_HW = 1.75
POD_X, POD_HW = 3.75, 0.65
POD_BOTTOM, POD_TOP = 2.7, 4.0
LEG_Z = 2.55            # leg hinge height (just under the pod)
LEG_Y = (2.4, -5.5)     # front, rear hinge (y)
SPLAY = math.radians(12)
PAD_DROP = 0.18         # ankle to pad bottom
FLY_Z = 6.0


def hw(y, z=0.0):
    """Fuselage half width at (y, z): the nose narrows in plan from TAPER_FROM to the tip, the upper flanks lean in
    above SHOULDER."""
    plan = HW if y <= TAPER_FROM else HW + (NOSE_HW - HW) * (y - TAPER_FROM) / (NOSE_Y - TAPER_FROM)
    lean = 1.0 if z <= SHOULDER else 1.0 - (1.0 - ROOF_K) * (z - SHOULDER) / (ROOF - SHOULDER)
    return plan * lean


# ---------------------------------------------------------------- materials


def ship_palette():
    P = V["tier_palette"](V["palette"]())
    P.update({
        "navgreen": material("NavGreen", (0.05, 0.9, 0.2), 0.0, 0.3, emission=(0.05, 1.0, 0.2), strength=3.0),
        "nacelle": material("NacelleGlow", (0.1, 0.4, 1.0), 0.0, 0.25, emission=(0.12, 0.45, 1.0), strength=2.0),
        "baylight": material("BayLight", (1.0, 0.95, 0.85), 0.0, 0.3, emission=(1.0, 0.94, 0.82), strength=4.0),
        "deck": material("Deck", (0.2, 0.205, 0.215), 0.7, 0.45),
        "liner": material("BayLiner", (0.5, 0.51, 0.53), 0.2, 0.6),
    })
    return P


def thruster_material(k, impulse):
    """Each thruster gets its own material (ThrusterK) so the game can light them one by one."""
    colour = (1.0, 0.32, 0.08) if impulse else (0.55, 0.8, 1.0)
    return material(f"Thruster{k}", tuple(c * 0.3 for c in colour), 0.0, 0.3, emission=colour, strength=0.5)


# ---------------------------------------------------------------- geometry helpers


def tapered_prism(name, profile, mat, parent):
    """prism() across the fuselage width, cut at TAPER_FROM and SHOULDER, then narrowed by hw(y, z): side faces stay
    planar where x is linear in y or in z alone; the upper nose (both at once) is triangulated. Bevel added later."""
    o = prism(name, profile, W, mat, parent, bev=0)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, TAPER_FROM, 0), plane_no=(0, 1, 0))
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, SHOULDER), plane_no=(0, 0, 1))
    for v in bm.verts:
        v.co.x = math.copysign(hw(v.co.y, v.co.z), v.co.x)
    both = [f for f in bm.faces if abs(f.normal.x) > 0.5 and all(v.co.y > TAPER_FROM - 1e-4 and v.co.z > SHOULDER - 1e-4 for v in f.verts)]
    bmesh.ops.triangulate(bm, faces=both)
    bm.to_mesh(o.data)
    bm.free()
    return o


def flank_poly(name, pts, s, mat, parent, lift=0.012, shadow=False):
    """Flat polygon [(y, z)] on the fuselage flank (follows the nose taper), facing s = +-1."""
    bm = bmesh.new()
    vs = [bm.verts.new((s * (hw(y, z) + lift), y, z)) for y, z in pts]
    f = bm.faces.new(vs)
    if f.normal.x * s < 0:
        f.normal_flip()
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = shadow
    return o


def flank_ribbon(name, points, height, mat, parent, s):
    """build_vehicles.ribbon on the flank, following the nose taper (None breaks the stripe)."""
    bm = bmesh.new()
    runs, run = [], []
    for p in list(points) + [None]:
        if p is None:
            if len(run) > 1:
                runs.append(run)
            run = []
        else:
            run.append(p)
    for run in runs:
        top = [bm.verts.new((s * (hw(y, z + height / 2) + 0.012), y, z + height / 2)) for y, z in run]
        bot = [bm.verts.new((s * (hw(y, z - height / 2) + 0.012), y, z - height / 2)) for y, z in run]
        for i in range(len(run) - 1):
            f = bm.faces.new((bot[i], bot[i + 1], top[i + 1], top[i]))
            if f.normal.x * s < 0:
                f.normal_flip()
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def quad_on_slope(name, lo, hi, frac_lo, frac_hi, inset, lift, mat, parent, shadow=False):
    """A flat quad on the sloped nose face between profile points lo -> hi (y, z), from fraction frac_lo to frac_hi of
    the way up, inset from the tapered edges, lifted along the outward normal."""
    _, n, _, _ = on_slope(lo, hi)
    pts = []
    for f in (frac_lo, frac_hi):
        y = lo[0] + (hi[0] - lo[0]) * f
        z = lo[1] + (hi[1] - lo[1]) * f
        pts.append((y, z, hw(y, z) - inset))
    bm = bmesh.new()
    (y0, z0, w0), (y1, z1, w1) = pts
    vs = [bm.verts.new(Vector((x, y, z)) + n * lift) for x, y, z in ((-w0, y0, z0), (w0, y0, z0), (w1, y1, z1), (-w1, y1, z1))]
    f = bm.faces.new(vs)
    if f.normal.dot(n) < 0:
        f.normal_flip()
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = shadow
    return o


def seam(name, size, loc, P, parent):
    o = box(name, size, loc, P["dark"], parent, 0)
    o.visible_shadow = False
    return o


def grille(name, k, centre, size, P, parent, impulse=False, facing="down"):
    """Recessed thruster: dark frame, glowing panel ThrusterK (own material), slats across it."""
    sx, sy = size
    x, y, z = centre
    if facing == "down":
        box(f"{name}Frame", (sx + 0.14, sy + 0.14, 0.08), (x, y, z), P["dark"], parent, 0.02)
        glow = box(f"Thruster{k}", (sx, sy, 0.04), (x, y, z - 0.05), thruster_material(k, impulse), parent, 0.01)
        n = max(2, int(sy / 0.22))
        for i in range(n):
            yy = y - sy / 2 + (i + 0.5) * sy / n
            box(f"{name}Slat{i}", (sx + 0.04, 0.035, 0.05), (x, yy, z - 0.085), P["gunmetal"], parent, 0)
    else:  # facing aft (-Y)
        box(f"{name}Frame", (sx + 0.14, 0.08, sy + 0.14), (x, y, z), P["dark"], parent, 0.02)
        glow = box(f"Thruster{k}", (sx, 0.04, sy), (x, y - 0.05, z), thruster_material(k, impulse), parent, 0.01)
        for i in range(3):
            zz = z - sy / 2 + (i + 0.5) * sy / 3
            box(f"{name}Slat{i}", (sx + 0.04, 0.05, 0.035), (x, y - 0.085, zz), P["gunmetal"], parent, 0)
    glow.visible_shadow = False
    return glow


# ---------------------------------------------------------------- the ship


def fuselage(P, root):
    profile = [(REAR, BELLY), (4.0, BELLY), (5.5, 1.3), (6.55, 2.05), (NOSE_Y, 2.45), (6.25, 2.85), (4.45, 4.2), (3.7, 4.6),
               (2.3, ROOF), (REAR + 0.15, ROOF), (REAR, ROOF - 0.15)]
    hull = tapered_prism("Hull", profile, P["hull"], root)
    hull["dirt_band"] = (BELLY, 2.6)
    # The bay: open at the back, floor at FLOOR (cut a touch lower; the deck plate sits on top).
    cut = box("BayCut", (2 * BAY_HW, BAY_FRONT - REAR + 1.0, BAY_TOP - FLOOR + 0.02),
              (0, (BAY_FRONT + REAR - 1.0) / 2, (BAY_TOP + FLOOR - 0.02) / 2), P["dark"], None, bev=0)
    cut.hide_render = cut.hide_viewport = True
    mod = hull.modifiers.new("Bay", "BOOLEAN")
    mod.operation, mod.solver, mod.object = "DIFFERENCE", "EXACT", cut
    bevel(hull, 0.12, 3, 35)

    # Wrap-around windscreen on the nose slope: frame, glass, two mullions; side cockpit windows.
    lo, hi = (6.25, 2.85), (4.45, 4.2)
    quad_on_slope("WindscreenFrame", lo, hi, 0.08, 0.86, 0.16, 0.008, P["frame"], root)
    quad_on_slope("Windscreen", lo, hi, 0.12, 0.82, 0.24, 0.018, P["glass"], root)
    mid, n, along, rx = on_slope(lo, hi)
    for x in (-0.55, 0.55):
        p = box(f"Mullion{x}", (0.07, 0.04, 1.75), mid + n * 0.025 - along * 0.06 + Vector((x, 0, 0)), P["frame"], root, 0.01, rot=(rx, 0, 0))
        p.visible_shadow = False
    for s in (-1, 1):
        flank_poly(f"SideGlassFrame{s}", [(5.68, 3.02), (4.5, 3.91), (3.38, 3.91), (3.38, 3.02)], s, P["frame"], root, 0.006)
        flank_poly(f"SideGlass{s}", [(5.5, 3.08), (4.47, 3.85), (3.45, 3.85), (3.45, 3.08)], s, P["glass"], root, 0.014)
        box(f"Post{s}", (0.03, 0.06, 0.8), (s * (hw(4.3) + 0.02), 4.3, 3.46), P["frame"], root, 0).visible_shadow = False

    # Nose: landing lamps and a dark sensor band; chin VTOL grilles.
    for s in (-1, 1):
        box(f"NoseLampFrame{s}", (0.36, 0.05, 0.2), (s * 0.85, NOSE_Y + 0.0, 2.25), P["dark"], root, 0.02)
        box(f"NoseLamp{s}", (0.28, 0.05, 0.13), (s * 0.85, NOSE_Y + 0.02, 2.25), P["lamp"], root, 0.015)
    box("NoseBand", (0.9, 0.05, 0.12), (0, NOSE_Y + 0.01, 2.25), P["array"], root, 0.02)
    for k, s in ((2, -1), (3, 1)):
        grille(f"ChinGrille{s}", k, (s * 0.95, 3.1, BELLY - 0.02), (0.9, 1.2), P, root)

    # Bay: deck plate with tie-down rails and a hazard edge, wall ribs, ceiling light strips, front bulkhead door.
    deck_len = BAY_FRONT - REAR
    box("BayDeck", (2 * BAY_HW - 0.04, deck_len, 0.04), (0, (BAY_FRONT + REAR) / 2, FLOOR - 0.02), P["deck"], root, 0.005)
    for x in (-1.25, -0.45, 0.45, 1.25):
        box(f"TieRail{x}", (0.07, deck_len - 0.6, 0.02), (x, (BAY_FRONT + REAR) / 2 + 0.1, FLOOR + 0.01), P["steel"], root, 0)
    for k in range(8):
        hz = box(f"DeckHazard{k}", (0.22, 0.2, 0.012), (-BAY_HW + 0.2 + k * 0.48, REAR + 0.13, FLOOR + 0.006), P["padmark"], root, 0)
        hz.visible_shadow = False
    for s in (-1, 1):
        for k in range(7):
            y = REAR + 0.5 + k * 1.0
            box(f"Rib{s}{k}", (0.07, 0.12, BAY_TOP - FLOOR - 0.04), (s * (BAY_HW - 0.03), y, (BAY_TOP + FLOOR) / 2), P["frame"], root, 0)
        box(f"Liner{s}", (0.02, deck_len - 0.4, 1.2), (s * (BAY_HW - 0.012), (BAY_FRONT + REAR) / 2 + 0.2, FLOOR + 1.0), P["liner"], root, 0)
    bm = bmesh.new()
    for x in (-0.9, 0.9):
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((x, (BAY_FRONT + REAR) / 2 + 0.2, BAY_TOP - 0.02)) @ Matrix.Diagonal((0.2, deck_len - 0.8, 0.03, 1)))
    mesh_object("BayLight", bm, P["baylight"], root).visible_shadow = False
    for k in range(5):
        box(f"CeilingRib{k}", (2 * BAY_HW - 0.06, 0.1, 0.08), (0, REAR + 0.9 + k * 1.5, BAY_TOP - 0.04), P["frame"], root, 0)
    box("BulkheadDoor", (1.0, 0.04, 2.1), (0, BAY_FRONT - 0.02, FLOOR + 1.05), P["panel"], root, 0.02)
    box("BulkheadDoorWindow", (0.4, 0.05, 0.3), (0, BAY_FRONT - 0.03, FLOOR + 1.6), P["glass"], root, 0.01)
    for x in (-1.3, 1.3):
        box(f"BulkheadLocker{x}", (0.6, 0.3, 1.6), (x, BAY_FRONT - 0.15, FLOOR + 0.8), P["liner"], root, 0.03)

    # Rear door frame standing proud of the back so the closed ramp sits flush inside it; tail lamps.
    for s in (-1, 1):
        box(f"DoorPost{s}", (0.24, RAMP_T + 0.04, BAY_TOP - BELLY + 0.1), (s * (BAY_HW + 0.1), REAR - (RAMP_T + 0.04) / 2 + 0.02,
                                                                          (BAY_TOP + BELLY + 0.1) / 2), P["frame"], root, 0.03)
        box(f"TailLamp{s}", (0.12, 0.04, 0.5), (s * (BAY_HW + 0.1), REAR - RAMP_T - 0.04, 3.9), P["redlamp"], root, 0.01)
        box(f"RearLamp{s}", (0.12, 0.04, 0.3), (s * (BAY_HW + 0.1), REAR - RAMP_T - 0.04, 1.5), P["amber"], root, 0.01)
        cylinder(f"HingeBlock{s}", 0.11, 0.18, (s * (BAY_HW - 0.02), REAR, FLOOR - 0.11), P["steel"], root, axis="X", segments=12)
    box("DoorLintel", (2 * BAY_HW + 0.44, RAMP_T + 0.04, 0.26), (0, REAR - (RAMP_T + 0.04) / 2 + 0.02, BAY_TOP + 0.1), P["frame"], root, 0.03)

    # Flank details: crew door (left), panel seams, vents, RCS quads at the corners.
    s = -1
    x = s * (HW + 0.004)
    seam("CrewDoorFwd", (0.02, 0.03, 2.1), (x, 2.25, 1.95), P, root)
    seam("CrewDoorAft", (0.02, 0.03, 2.1), (x, 1.3, 1.95), P, root)
    seam("CrewDoorTop", (0.02, 0.98, 0.03), (x, 1.775, 3.0), P, root)
    box("CrewDoorHandle", (0.05, 0.18, 0.05), (s * (HW + 0.025), 2.05, 1.9), P["steel"], root, 0.01)
    box("CrewDoorWindow", (0.03, 0.4, 0.3), (s * (HW + 0.01), 1.775, 2.6), P["glass"], root, 0.01)
    box("CrewStep", (0.3, 0.9, 0.06), (s * (HW + 0.1), 1.775, BELLY + 0.08), P["steel"], root, 0.015)
    for s in (-1, 1):
        x = s * (HW + 0.004)
        for y in (-3.95, 0.95):
            seam(f"SeamV{s}{y}", (0.02, 0.03, 2.9), (x, y, 2.45), P, root)
        seam(f"SeamH{s}", (0.02, 6.8, 0.03), (x, -2.5, 2.95), P, root)
        for k in range(5):
            seam(f"Vent{s}{k}", (0.02, 0.7, 0.04), (x, -5.1, 2.1 + k * 0.1), P, root)
        for y, z in ((REAR + 0.35, 3.7), (2.6, 3.5)):
            box(f"Rcs{s}{y}", (0.12, 0.34, 0.3), (s * (HW + 0.05), y, z), P["gunmetal"], root, 0.03)
            for dz in (-0.07, 0.07):
                cylinder(f"RcsNozzle{s}{y}{dz}", 0.04, 0.06, (s * (HW + 0.13), y, z + dz), P["dark"], root, axis="X", segments=8)
    return hull


def spine(P, root):
    """Dorsal spine on the roof: beacon, sensor dome, comm masts, strobe."""
    profile = [(REAR + 0.5, ROOF - 0.05), (2.0, ROOF - 0.05), (1.2, ROOF + 0.38), (REAR + 0.85, ROOF + 0.38), (REAR + 0.5, ROOF + 0.2)]
    sp = prism("Spine", profile, 1.6, P["hull"], root, bev=0.08)
    sp["dirt_band"] = (ROOF - 0.4, ROOF + 0.6)
    top = ROOF + 0.38
    cylinder("BeaconBase", 0.13, 0.06, (0, -0.4, top + 0.03), P["dark"], root, axis="Z", segments=16)
    cylinder("Beacon", 0.1, 0.16, (0, -0.4, top + 0.14), P["amber"], root, axis="Z", segments=16)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=10, radius=0.32)
    for v in bm.verts:
        v.co.z = max(v.co.z, 0) * 0.7
    mesh_object("SensorDome", bm, P["panel"], root, (0, -3.2, top), smooth=True)
    for k, (x, y, h) in enumerate(((0.45, -4.6, 0.9), (-0.45, -4.6, 0.6))):
        cylinder(f"CommMast{k}", 0.025, h, (x, y, top + h / 2), P["steel"], root, axis="Z", segments=8)
        cylinder(f"CommMastBase{k}", 0.06, 0.08, (x, y, top + 0.04), P["dark"], root, axis="Z", segments=10)
    cylinder("Strobe", 0.05, 0.06, (0, REAR + 0.75, top + 0.03), P["lamp"], root, axis="Z", segments=12)
    for x in (-0.55, 0.55):
        box(f"SpineVent{x}", (0.3, 1.2, 0.02), (x, -1.9, top + 0.005), P["dark"], root, 0).visible_shadow = False
    return sp


def pod(P, root, s):
    """VTOL pod on a pylon: down-facing lift grille, aft impulse exhaust, blue grille strip, nav light, leg hinges."""
    k = 0 if s < 0 else 1
    profile = [(-6.0, POD_BOTTOM), (2.6, POD_BOTTOM), (3.55, 3.05), (3.95, 3.45), (3.5, POD_TOP), (-5.6, POD_TOP), (-6.0, 3.7)]
    pd = prism(f"Pod{k}", profile, 2 * POD_HW, P["hull"], root, bev=0.14)
    pd.location.x = s * POD_X
    pd["dirt_band"] = (POD_BOTTOM, POD_TOP + 0.4)
    # Pylon: a flat aerofoil from the hull flank down to the pod's inner face.
    py = prism(f"Pylon{k}", [(-3.8, -0.16), (0.0, -0.16), (0.8, 0.0), (0.0, 0.17), (-3.6, 0.17)], 1.5, P["panel"], root, bev=0.06)
    py.location = (s * (HW + POD_X - POD_HW) / 2, 0, 3.45)
    py.rotation_euler = (0, s * math.radians(12), 0)
    # Blue grille strip along the pod's top in a dark surround (seen from the game camera), ribbed.
    xo = s * (POD_X + POD_HW)
    box(f"NacelleSurround{k}", (0.5, 6.4, 0.03), (s * POD_X, -1.4, POD_TOP + 0.006), P["dark"], root, 0.01)
    glow = box(f"NacelleGlow{k}", (0.32, 6.2, 0.03), (s * POD_X, -1.4, POD_TOP + 0.016), P["nacelle"], root, 0.01)
    glow.visible_shadow = False
    for i in range(14):
        box(f"NacelleRib{k}{i}", (0.36, 0.04, 0.03), (s * POD_X, -4.3 + i * 0.445, POD_TOP + 0.03), P["gunmetal"], root, 0)
    # Front intake cap, nav light (red port, green starboard).
    _, n, _, rx = on_slope((3.95, 3.45), (3.5, POD_TOP))
    box(f"Intake{k}", (2 * POD_HW - 0.34, 0.05, 0.42), (s * POD_X, 3.725, 3.725), P["array"], root, 0.02, rot=(rx, 0, 0))
    cylinder(f"NavLight{k}", 0.07, 0.08, (s * (POD_X + POD_HW - 0.08), 3.0, POD_TOP + 0.02), P["navgreen" if s > 0 else "redlamp"], root,
             axis="Z", segments=12)
    # Lift grille underneath (between the folded legs) and the aft impulse exhaust.
    grille(f"PodGrille{k}", k, (s * POD_X, -1.55, POD_BOTTOM - 0.02), (0.9, 1.9), P, root)
    grille(f"Impulse{k}", 4 + k, (s * POD_X, -6.0, 3.2), (0.9, 0.66), P, root, impulse=True, facing="aft")
    # Owner stripe along the pod's outboard flank under the hull number, sweeping up at the nose.
    V["ribbon"](f"PodFlash{k}", [(-5.8, 2.98), (2.5, 2.98), (3.45, 3.3)], 0.12, P["owner"], root, x=xo, outward=s)
    # Hinge brackets for the legs.
    for y in LEG_Y:
        box(f"LegBracket{k}{y}", (0.7, 0.42, 0.16), (s * POD_X, y, POD_BOTTOM - 0.06), P["dark"], root, 0.02)
    return pd


def leg(P, root, k, s, y):
    """Landing leg on a hinge under a pod: knuckle, A-frame, oleo, ankle (FootK) with a level pad. The splay is built
    into the geometry so LegK's rest frame is the ship's (fold = rotation about local X)."""
    lg = empty(f"Leg{k}", (s * POD_X, y, LEG_Z), root)
    J = (LEG_Z - PAD_DROP) / math.cos(SPLAY)
    tip = Vector((s * J * math.sin(SPLAY), 0, -J * math.cos(SPLAY)))
    cylinder(f"Leg{k}Knuckle", 0.13, 0.62, (0, 0, 0), P["steel"], lg, axis="X", segments=16)
    knee = tip * 0.55
    for x in (-0.24, 0.24):
        strut(f"Leg{k}Arm{x}", (x, 0, -0.04), knee, 0.065, P["dark"], lg)
    strut(f"Leg{k}Sleeve", knee + (knee - tip) * 0.15, tip * 0.82, 0.11, P["panel"], lg, segments=14)
    strut(f"Leg{k}Piston", tip * 0.8, tip, 0.07, P["steel"], lg, segments=12)
    cylinder(f"Leg{k}Collar", 0.13, 0.08, knee, P["dark"], lg, axis="Z", segments=14).rotation_euler = (tip.to_track_quat("Z", "Y")).to_euler()
    foot = empty(f"Foot{k}", tip, lg)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.1)
    mesh_object(f"Foot{k}Ball", bm, P["steel"], foot, smooth=True)
    cylinder(f"Foot{k}Pad", 0.42, 0.1, (0, 0, -PAD_DROP + 0.05), P["dark"], foot, axis="Z", segments=28, bev=0.02)
    cylinder(f"Foot{k}Boss", 0.2, 0.1, (0, 0, -0.1), P["gunmetal"], foot, axis="Z", segments=18, bev=0.015)
    return lg


def ramp(P, root):
    """Rear ramp door on its hinge. Built closed (standing up in the door, inner face at local y = 0 through the hinge so
    the open ramp's top surface meets the deck), toe chamfered underneath so it lies on the ground."""
    r = empty("Ramp", (0, REAR, FLOOR), root)
    L, T = RAMP_LEN, RAMP_T
    slab = prism("RampSlab", [(-T, 0), (0, 0), (0, L), (-0.06, L), (-T, L - 0.32)], 2 * RAMP_HW, P["hull"], r, bev=0.03)
    slab.location = (0, 0, 0)
    slab["dirt_band"] = (L, L - 1.2)  # dirt toward the toe (inverted band), where it meets the ground
    # Walking surface (local +Y face): grip ribs, side curbs, hazard edges.
    for i in range(10):
        box(f"RampRib{i}", (2 * RAMP_HW - 0.5, 0.025, 0.05), (0, 0.012, 0.25 + i * 0.3), P["steel"], r, 0)
    for x in (-1, 1):
        box(f"RampCurb{x}", (0.1, 0.1, L - 0.1), (x * (RAMP_HW - 0.06), 0.05, L / 2), P["frame"], r, 0.02)
        for i in range(6):
            hz = box(f"RampHazard{x}{i}", (0.1, 0.012, 0.22), (x * (RAMP_HW - 0.2), 0.006, 0.3 + i * 0.55), P["padmark"], r, 0)
            hz.visible_shadow = False
    # Outer face (what you see when closed): seams and the flank's owner stripes carried across; hazard-striped toe
    # on the walking face.
    for z in (1.3, 2.4):
        seam(f"RampSeam{z}", (2 * RAMP_HW - 0.2, 0.02, 0.03), (0, -T - 0.004, z), P, r)
    for name, z, h in (("RampFlash", 1.8 - FLOOR, 0.16), ("RampFlashB", 1.56 - FLOOR, 0.06)):
        box(name, (2 * RAMP_HW - 0.08, 0.012, h), (0, -T - 0.006, z), P["owner"], r, 0).visible_shadow = False
    for i in range(7):
        hz = box(f"RampToeHazard{i}", (0.22, 0.012, 0.12), (-RAMP_HW + 0.3 + i * 0.5, 0.008, L - 0.12), P["padmark"], r, 0)
        hz.visible_shadow = False
    cylinder("RampHinge", 0.1, 2 * RAMP_HW - 0.4, (0, -0.1, 0.0), P["steel"], r, axis="X", segments=14)
    return r


def ramp_open_angle():
    """Rotation about X (radians) that brings the closed ramp's toe down onto the ground (z = 0 in the landed pose)."""
    corners = [(0.0, RAMP_LEN), (-0.06, RAMP_LEN), (-RAMP_T, RAMP_LEN - 0.32)]

    def lowest(t):
        return min(FLOOR + y * math.sin(t) + z * math.cos(t) for y, z in corners)

    lo, hi = math.radians(90), math.radians(170)
    for _ in range(60):
        m = (lo + hi) / 2
        if lowest(m) > 0:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


RAMP_OPEN = ramp_open_angle()


def decals(P, root):
    """Hull number on the rear flanks, name above the pylons, name across the door lintel (decals cast no shadow).
    With --blank these are the game's marker empties (build_vehicles' MARKERS names)."""
    V["BLANK"] = BLANK
    try:
        for s in (-1, 1):
            rot = (math.radians(90), 0, math.radians(90 if s > 0 else -90))
            V["text"](f"Number{s}", NUMBER, 0.8, (s * (POD_X + POD_HW + 0.006), -2.2, 3.42), rot, P["number"], root, max_width=1.4)
            V["text"](f"Name{s}", NAME, 0.36, (s * (HW + 0.006), -2.0, 2.3), rot, P["number"], root, max_width=3.0)
            # Owner flash: low along the flank, up over the chin; a thin second line. The crew door breaks the left one.
            main = [(REAR + 0.05, 1.8), (1.15, 1.8), None, (2.4, 1.8), (3.2, 1.8), (4.6, 2.55)] if s < 0 else \
                [(REAR + 0.05, 1.8), (3.2, 1.8), (4.6, 2.55)]
            flank_ribbon(f"Flash{s}", main, 0.16, P["owner"], root, s)
            thin = [(REAR + 0.05, 1.56), (1.15, 1.56), None, (2.4, 1.56), (3.4, 1.56), (4.5, 2.08)] if s < 0 else \
                [(REAR + 0.05, 1.56), (3.4, 1.56), (4.5, 2.08)]
            flank_ribbon(f"FlashB{s}", thin, 0.06, P["owner"], root, s)
        V["text"]("NameRear", NAME, 0.18, (0, REAR - RAMP_T - 0.035, BAY_TOP + 0.1), (math.radians(90), 0, 0), P["number"], root, max_width=2.6)
    finally:
        V["BLANK"] = False


def dropship():
    P = ship_palette()
    root = empty("DropShip")
    parts = {"root": root, "bake": []}
    parts["bake"].append((fuselage(P, root), 2048))
    parts["bake"].append((spine(P, root), 512))
    for s in (-1, 1):
        parts["bake"].append((pod(P, root, s), 1024))
    parts["legs"] = [leg(P, root, k, s, y) for k, (s, y) in enumerate(((-1, LEG_Y[0]), (1, LEG_Y[0]), (-1, LEG_Y[1]), (1, LEG_Y[1])))]
    parts["ramp"] = ramp(P, root)
    parts["bake"].append((bpy.data.objects["RampSlab"], 512))
    decals(P, root)
    return parts


def ship_paint(g, band):
    """The rovers' scuffed white paint at ship scale (build_vehicles.weather_hull's noise is tuned for a 4.6 m rover and
    reads as sandpaper on a 12 m hull): soft grey clouding, grime rising from the belly (object z band lo -> hi,
    inverted when lo > hi), rain streaks running down, a few dark scuffs."""
    F = rover_finish
    P, z = g.P, g.z
    col = g.mix(g.lin(g.noise(P, 0.5, 4.0), 0.35, 0.7, 0.0, 0.14), F.CLEAN, (0.6, 0.6, 0.58))
    dirt = g.mad(g.noise(P, 1.1, 6.0), 0.7, g.lin(z, band[0], band[1]))
    col = g.mix(g.lin(dirt, 0.5, 1.25, 0.8, 0.0), col, F.GRIME)
    streak = g.noise(g.mapping(P, (5.0, 5.0, 0.3)), 2.0, 3.0)
    col = g.mix(g.lin(streak, 0.58, 0.76, 0.0, 0.28), col, F.GRIME)
    scuff = g.mul(g.smooth(g.noise(P, 6.0, 3.0), 0.69, 0.73), 0.4)
    col = g.mix(scuff, col, (0.45, 0.45, 0.44))
    rub = g.mul(g.smooth(g.noise(P, 11.0, 3.0), 0.7, 0.73), 0.6)
    return g.mix(rub, col, F.RUB)


def bake_paint(obj, res):
    """Bake ship_paint into a packed image (rover_finish's bake: modifiers applied, smart UV, everything else hidden)
    and swap in the glTF-friendly image material f"{obj.name}Paint"."""
    zs = [c[2] for c in obj.bound_box]
    band = tuple(obj["dirt_band"]) if "dirt_band" in obj else (min(zs), min(zs) + 1.5)
    image = rover_finish._bake(obj, lambda g: ship_paint(g, band), res, f"{obj.name}Paint")
    final = rover_finish._image_material(f"{obj.name}Paint", image, 0.15, 0.42, 0.2)
    obj.data.materials.clear()
    obj.data.materials.append(final)


# ---------------------------------------------------------------- poses


def glow(name, strength):
    m = bpy.data.materials.get(name)
    if m:
        m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = strength


def set_pose(parts, pose):
    root, legs, rmp = parts["root"], parts["legs"], parts["ramp"]
    root.location.z = FLY_Z if pose == "flying" else 0.0
    rmp.rotation_euler.x = RAMP_OPEN if pose == "unloading" else 0.0
    for k, lg in enumerate(legs):
        fold = 0.0 if pose != "flying" else math.radians(-90 if k < 2 else 90)
        lg.rotation_euler.x = fold
        bpy.data.objects[f"Foot{k}"].rotation_euler.x = -fold
    lift = pose == "flying"
    for k in range(6):
        glow(f"Thruster{k}", (32.0 if k < 4 else 14.0) if lift else 0.5)
    glow("NacelleGlow", 5.0 if lift else 2.0)
    glow("BayLight", 6.0 if pose == "unloading" else 2.0)


# ---------------------------------------------------------------- preview props (never exported)


def rover_on_ramp():
    """A Mk I Scout driving out: rear wheels on the deck just inside the door, front wheels on the open ramp."""
    V["NAME"], V["NUMBER"] = "TALUS", "17"
    rover = V["rover_mk1"](V["palette"]())
    r, wheelbase, rear_local = 0.52, 2.45, -1.2  # rover_mk1: wheel radius, axles 1.25 / -1.2
    a = RAMP_OPEN - math.pi / 2  # ramp slope below horizontal
    d = Vector((-math.cos(a), -math.sin(a)))  # (y, z) down the ramp
    nrm = Vector((-math.sin(a), math.cos(a)))
    hinge = Vector((REAR, FLOOR))
    c_rear = Vector((REAR + 0.6, FLOOR + r))
    lo, hi = 0.0, RAMP_LEN
    for _ in range(60):
        m = (lo + hi) / 2
        if ((hinge + d * m + nrm * r) - c_rear).length < wheelbase:
            lo = m
        else:
            hi = m
    c_front = hinge + d * lo + nrm * r
    u = (c_front - c_rear).normalized()
    v = Vector((u.y, -u.x))  # rover up: perpendicular to u, z > 0
    if v.y < 0:
        v = -v
    o = c_rear - u * rear_local - v * r
    rover.matrix_world = Matrix((
        (-1, 0, 0, 0),
        (0, u.x, v.x, o.x),
        (0, u.y, v.y, o.y),
        (0, 0, 0, 1)))
    tie_down_antenna(rover)
    return rover


def tie_down_antenna(rover):
    """The Mk I's 2 m whip would poke through the bay roof: lay it forward along the rack, as tied down for the flight."""
    pivot = Vector((0.95, -1.85, 2.34))
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(-78), 4, "X") @ Matrix.Translation(-pivot)
    for o in rover.children:
        if o.name.split(".")[0] in ("Antenna", "AntennaTip"):
            o.matrix_basis = m @ o.matrix_basis


def scale_rover():
    """A Mk II (Kalty 44) parked beside the ship for the game-camera scale shot."""
    V["NAME"], V["NUMBER"] = "KALTY", "44"
    rover = V["rover_mk2"](V["palette"]())
    rover.location = (8.2, -3.5, 0)
    rover.rotation_euler.z = math.radians(-20)
    return rover


def hex_outline(radius=25 / math.sqrt(3)):
    """One planet hex (pointy-top, corner toward +Y, 25 across the flats) drawn on the ground for scale."""
    bm = bmesh.new()
    outer = [bm.verts.new((math.sin(math.radians(60 * k)) * radius, math.cos(math.radians(60 * k)) * radius, 0.01)) for k in range(6)]
    inner = [bm.verts.new((math.sin(math.radians(60 * k)) * (radius - 0.25), math.cos(math.radians(60 * k)) * (radius - 0.25), 0.01)) for k in range(6)]
    for k in range(6):
        j = (k + 1) % 6
        bm.faces.new((outer[k], outer[j], inner[j], inner[k]))
    o = mesh_object("HexOutline", bm, material("HexLine", (0.9, 0.85, 0.5), 0, 0.5, emission=(0.9, 0.85, 0.5), strength=0.6), None)
    o.visible_shadow = False
    return o


def plume_material():
    """Soft additive glow for the lift plumes: brightest near the grille and where the cone faces the camera."""
    m = bpy.data.materials.new("Plume")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    coord = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(coord.outputs["Generated"], sep.inputs[0])
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.5
    face = nt.nodes.new("ShaderNodeMath")
    face.operation = "SUBTRACT"
    face.inputs[0].default_value = 1.0
    nt.links.new(lw.outputs["Facing"], face.inputs[1])
    face2 = nt.nodes.new("ShaderNodeMath")
    face2.operation = "POWER"
    nt.links.new(face.outputs[0], face2.inputs[0])
    face2.inputs[1].default_value = 2.0
    along = nt.nodes.new("ShaderNodeMath")
    along.operation = "POWER"
    nt.links.new(sep.outputs["Z"], along.inputs[0])
    along.inputs[1].default_value = 2.5
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    nt.links.new(face2.outputs[0], mul.inputs[0])
    nt.links.new(along.outputs[0], mul.inputs[1])
    amp = nt.nodes.new("ShaderNodeMath")
    amp.operation = "MULTIPLY"
    nt.links.new(mul.outputs[0], amp.inputs[0])
    amp.inputs[1].default_value = 0.9
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (0.35, 0.62, 1.0, 1)
    nt.links.new(amp.outputs[0], emit.inputs["Strength"])
    clear = nt.nodes.new("ShaderNodeBsdfTransparent")
    add = nt.nodes.new("ShaderNodeAddShader")
    nt.links.new(clear.outputs[0], add.inputs[0])
    nt.links.new(emit.outputs[0], add.inputs[1])
    nt.links.new(add.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    return m


def plumes(root):
    """Flying previews: a faint plume and a downward light under each lift grille."""
    mat = plume_material()
    spots = [(-POD_X, -1.55, POD_BOTTOM - 0.12, (0.9, 1.9)), (POD_X, -1.55, POD_BOTTOM - 0.12, (0.9, 1.9)),
             (-0.95, 3.1, BELLY - 0.12, (0.9, 1.2)), (0.95, 3.1, BELLY - 0.12, (0.9, 1.2))]
    for k, (x, y, z, (sx, sy)) in enumerate(spots):
        bm = bmesh.new()
        depth = 1.8
        bmesh.ops.create_cone(bm, cap_ends=False, segments=24, radius1=0.62, radius2=0.45, depth=depth)
        for v in bm.verts:
            v.co.y *= sy / sx
        o = mesh_object(f"Plume{k}", bm, mat, root, (x, y, z - depth / 2), smooth=True)
        o.visible_shadow = False
        light = bpy.data.lights.new(f"Lift{k}", "AREA")
        light.shape = "RECTANGLE"
        light.size, light.size_y = sx, sy
        light.energy = 250
        light.color = (0.6, 0.8, 1.0)
        lo = bpy.data.objects.new(f"Lift{k}", light)
        lo.location = (x, y, z - 0.05)
        link(lo, root)


def bay_lamp(root):
    """Unloading previews: light the bay interior like the ceiling strips would."""
    light = bpy.data.lights.new("BayFill", "AREA")
    light.shape = "RECTANGLE"
    light.size, light.size_y = 2.6, 6.0
    light.energy = 450
    light.color = (1.0, 0.94, 0.84)
    lo = bpy.data.objects.new("BayFill", light)
    lo.location = (0, (BAY_FRONT + REAR) / 2 + 0.2, BAY_TOP - 0.1)
    link(lo, root)


# ---------------------------------------------------------------- render


def preview(parts, folder, extras):
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
    link(sun)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=300)
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))

    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    lift = FLY_Z if POSE == "flying" else 0.0
    shots = {  # camera position, look-at target, lens
        "front34": (Vector((15.5, 18.5, 7.0 + lift * 0.75)), Vector((0, 0.3, 2.7 + lift * 0.85)), 40),
        "rear34": (Vector((-15.0, -19.0, 7.0)) if POSE != "unloading" else Vector((-10.5, -18.5, 5.2)),
                   Vector((0, -0.6, 2.6)) if POSE != "unloading" else Vector((0, -5.6, 2.3)), 40),
        "side": (Vector((25.0, 0.6, 3.8 + lift)), Vector((0, 0.0, 2.6 + lift)), 38),
        "game": (Vector((3.5, -1.5, 0)) + Vector((0, -0.57, 0.82)) * 46, Vector((3.5, -1.5, 0)), 35),
    }
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        for o in extras.get("game", []):
            o.hide_render = name != "game"
        parts["root"].rotation_euler.z = math.radians(25) if name == "game" else 0.0
        pos, target, lens = shots[name]
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"dropship-{POSE}-{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def main():
    V["reset"]()
    parts = dropship()
    set_pose(parts, "landed")
    if BAKE:
        for obj, res in parts["bake"]:
            bake_paint(obj, res)
    if OUT:
        V["export"](parts["root"], OUT)
    if not PREVIEW:
        return
    set_pose(parts, POSE)
    extras = {}
    if POSE == "unloading":
        rover = rover_on_ramp()
        bay_lamp(parts["root"])
        if BAKE:
            V["weather_hull"](next(o for o in rover.children if o.name.split(".")[0] == "Hull"))
    if POSE == "flying":
        plumes(parts["root"])
    if "game" in SHOTS:
        rover = scale_rover()
        if BAKE:
            V["weather_hull"](next(o for o in rover.children if o.name.split(".")[0] == "Hull"))
        extras["game"] = [rover] + list(rover.children_recursive) + [hex_outline()]
        extras["game"] = [o for o in extras["game"] if not o.hide_render]
    preview(parts, PREVIEW, extras)


main()
