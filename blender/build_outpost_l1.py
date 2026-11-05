"""Federation Level 1 outpost (the landed Colony Hub), game-ready: three modules that come down slung under three drop
ships, crawl together onto the tile centre, lock into one round hub and unfurl (owner 2026-11-05: "when the original
outpost comes down it should come down in 3 drop ships and then unfurl/combine into the level1 outpost").

The hub is a low round drum with a dome, cut into three 120-degree wedges, one per drop ship:
  ModuleA  Command (front, Godot +Z): entrance vestibule with the crew door (Godot (0, 0, 5.8), PlanetView's crew walk
           start), the comms mast (Mast -> MastTop -> Dish, Beacon) stowed lying over the dome on a cradle.
  ModuleB  Hab (back left): two folding solar wings on the outer wall (SolarB0/1, each with an outer leaf
           SolarB0Outer/SolarB1Outer folded back onto it), a small radome.
  ModuleC  Ops (back right): the storage vault, two roof radiators (RadiatorC0/1) and a floodlight arm (Flood).
Every wedge has an emissive "Seam" strip along its cut edges (flashes when they lock), a blue "Skid" glow line round its
dark under-frame (hover skids, lit while it crawls), a window band ("Window") and an "Owner" stripe.

Frame and conventions (CLAUDE.md "Vehicle models"): Blender +Z up, the hub's front (door) toward -Y = Godot +Z (the
buildings' front in BuildingModels.cs); 1 unit = 1 world unit; a hex is 25 across the flats, so the hub (radius 4.6,
door step to 6.3, solar wings to 8.8, mast tip 11.0) stays well inside it; the repair pad is added by the game
(BuildingModels.RepairPad at Godot (9.4, 0, 2)) and only drawn here in previews. The export is the REST POSE = fully
assembled and unfurled (the permanent Colony Hub). Static meshes are joined per rigid group (build_dropship.py's
join_for_game): ModuleA -> ModuleABody, Mast -> MastBody, ... ; Beacon stays its own mesh. No paint bake (flat
materials, so the colours survive glTF).

STOWED POSES (what the game's LandingView animates from; f = 0 stowed .. 1 rest). Every pivot below has its hinge on
its own local X axis; a Blender turn about local X is Godot's rest * Basis(Vector3.Right, angle), same sign (the glTF
Y-up conversion is itself a turn about X); a slide along Blender local Z is Godot local +Y; a Blender scale (x, y, z) is
Godot (x, z, y). Keep STOWED here and LandingView.Stowed in step.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_outpost_l1.py --game                 (export game/models/buildings/outpost_l1.glb)
  /home/user/.bpyvenv/bin/python blender/build_outpost_l1.py --preview blender/previews/outpost --shots assembled,game,unfurl,crawl,convoy
Flags: --out <glb>, --res 960, --samples 24, --no-join (export every part separately), --threads N
Shots (blender/previews/outpost/outpost-l1-<shot>.png):
  assembled  front 3/4 of the finished hub with the repair pad and a Mk I rover for scale, hex outline
  game       the game's camera at zoom distance 50
  top        orthographic plan (footprint check)
  unfurl     modules locked, mast half up, wings half out (unfurl 0.45)
  crawl      the three modules set down 8.5 out, stowed, crawling in
  convoy     the three drop ships (game/models/vehicles/dropship.glb) carrying the modules on cables
"""
import math
import os
import re
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


import bpy  # noqa: E402  first bpy, then bmesh/mathutils
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
GAME = "--game" in argv
OUT = arg("--out") or (os.path.join(REPO, "game", "models", "buildings", "outpost_l1.glb") if GAME else None)
PREVIEW = arg("--preview")
SHOTS = [s.strip() for s in arg("--shots", "assembled,game").split(",") if s.strip()]
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "24"))
THREADS = int(arg("--threads", "0"))
JOIN = "--no-join" not in argv
ALL_SHOTS = {"assembled", "game", "top", "unfurl", "crawl", "convoy"}
if set(SHOTS) - ALL_SHOTS:
    sys.exit(f"--shots takes {', '.join(sorted(ALL_SHOTS))}")


def load(script, extra_argv=()):
    """Exec another builder without its trailing main() (as render_finish.py does) and return its namespace."""
    path = os.path.join(HERE, script)
    with open(path) as f:
        src = re.sub(r"^main\(\)\s*$", "", f.read(), flags=re.M)
    saved = sys.argv
    sys.argv = [path, *extra_argv]
    ns = {"__name__": script[:-3], "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


OP = load("build_outpost.py", ["--level", "1"])  # helpers: lathe, dome, dish, sphere, zcyl, stage, repair_pad, M ...
BV = OP["BV"]
box, cylinder, empty, mesh_object, strut, link = BV["box"], BV["cylinder"], BV["empty"], BV["mesh_object"], BV["strut"], BV["link"]
zcyl, sphere = OP["zcyl"], OP["sphere"]

# ---------------------------------------------------------------- dimensions (Blender frame)

R = 4.6             # drum radius at the base
R_TOP = 4.35        # drum radius at the roof edge (the wall leans in)
Z_BASE = 0.32       # drum bottom (the dark under-frame / skids are below)
Z_WALL = 0.9        # the wall starts leaning in from here
Z_ROOF = 2.75       # roof deck
R_DOME = 3.35       # dome base radius
Z_DOME = 4.45       # dome apex
GAP = 0.05          # each wedge sits this far out along its centre line: a 0.09 seam between wedges
MAST_Z = 4.58       # mast hinge height (above the collar)
MAST_LEN = 3.4      # lower mast tube
TOP_LEN = 2.8       # telescoping upper section's travel

# Wedge centre angles (degrees from +X, Blender): A front (-Y), B back-left, C back-right.
WEDGES = {"A": -90.0, "B": 150.0, "C": 30.0}
HALF = 60.0

# Stowed offsets per pivot: (kind, value). rot = degrees about local X; slide = local Z; scale = (x, y, z).
STOWED = {
    "Mast": [("rot", 90.0)],
    "MastTop": [("slide", -TOP_LEN)],
    "Dish": [("rot", -40.0), ("scale", (0.28, 0.28, 1.0))],
    "SolarB0": [("rot", -100.0)],
    "SolarB1": [("rot", -100.0)],
    "SolarB0Outer": [("rot", 180.0)],
    "SolarB1Outer": [("rot", 180.0)],
    "RadiatorC0": [("rot", -170.0)],
    "RadiatorC1": [("rot", -170.0)],
    "Flood": [("rot", 90.0)],
}

# ---------------------------------------------------------------- materials (flat, game-safe)


def mat(name, colour, metallic=0.1, roughness=0.5, emission=None, strength=0.0, coat=0.0):
    return BV["material"](name, colour, metallic, roughness, emission, strength, coat)


MT = {}


def materials():
    MT.update({
        "hull": mat("Hull", (0.80, 0.81, 0.83), 0.15, 0.42, coat=0.2),
        "panel": mat("Panel", (0.6, 0.62, 0.65), 0.2, 0.5),
        "trim": mat("Trim", (0.26, 0.27, 0.3), 0.4, 0.5),
        "dark": mat("Dark", (0.08, 0.085, 0.095), 0.4, 0.6),
        "steel": mat("Steel", (0.55, 0.57, 0.6), 0.85, 0.32),
        "owner": mat("Owner", (0.72, 0.08, 0.06), 0.1, 0.4),
        "window": mat("Window", (1.0, 0.82, 0.55), 0.0, 0.3, emission=(1.0, 0.78, 0.45), strength=3.0),
        "glass": mat("Glass", (0.03, 0.06, 0.09), 0.3, 0.06, emission=(0.1, 0.25, 0.4), strength=0.25, coat=1.0),
        "seam": mat("Seam", (0.3, 0.6, 1.0), 0.0, 0.3, emission=(0.35, 0.7, 1.0), strength=1.5),
        "skid": mat("Skid", (0.2, 0.45, 1.0), 0.0, 0.3, emission=(0.25, 0.55, 1.0), strength=1.0),
        "solar": mat("Solar", (0.04, 0.09, 0.2), 0.6, 0.18, coat=0.6),
        "beacon": mat("Beacon", (1.0, 0.1, 0.06), 0.0, 0.3, emission=(1.0, 0.08, 0.05), strength=8.0),
        "lamp": mat("Lamp", (1.0, 0.95, 0.85), 0.0, 0.3, emission=(1.0, 0.95, 0.85), strength=6.0),
        "comms": mat("Comms", (0.5, 0.85, 1.0), 0.0, 0.3, emission=(0.5, 0.85, 1.0), strength=3.0),
        "vault": mat("Vault", (0.55, 0.48, 0.3), 0.7, 0.35),
        "stripe": mat("Hazard", (0.85, 0.62, 0.08), 0.0, 0.55),
    })


# ---------------------------------------------------------------- geometry helpers


def finish(bm, sharp=32.0):
    """Outward normals, smooth faces, and sharp edges wherever faces meet at more than `sharp` degrees (glTF keeps the
    split normals), so one mesh can have soft curves and crisp corners."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
    lim = math.radians(sharp)
    for e in bm.edges:
        if len(e.link_faces) != 2 or e.calc_face_angle(0.0) > lim:
            e.smooth = False


def polar(r, a, z):
    return Vector((r * math.cos(a), r * math.sin(a), z))


def revolve(name, profile, a0, a1, segs, material, parent=None, loc=(0, 0, 0), sharp=32.0):
    """A closed profile [(r, z), ...] (counter-clockwise in the r-z plane) swept from angle a0 to a1 (radians) about
    Z, capped at both ends: a solid wedge. Points on the axis (r == 0) become single vertices."""
    bm = bmesh.new()
    angles = [a0 + (a1 - a0) * i / segs for i in range(segs + 1)]
    rings = []
    for r, z in profile:
        if r < 1e-6:
            v = bm.verts.new((0, 0, z))
            rings.append([v] * len(angles))
        else:
            rings.append([bm.verts.new(polar(r, a, z)) for a in angles])
    n = len(profile)
    for i in range(n):
        p, q = rings[i], rings[(i + 1) % n]
        for k in range(segs):
            vs = [p[k], p[k + 1], q[k + 1], q[k]]
            uniq = []
            for v in vs:
                if v not in uniq:
                    uniq.append(v)
            if len(uniq) >= 3:
                bm.faces.new(uniq)
    for end in (0, segs):
        cap = []
        for ring in rings:
            if ring[end] not in cap:
                cap.append(ring[end])
        bm.faces.new(cap)
    finish(bm, sharp)
    return mesh_object(name, bm, material, parent, loc)


def quads(name, quads_, material, parent=None, loc=(0, 0, 0), shadow=False):
    """Loose quads [(p0, p1, p2, p3), ...] (windows, stripes, seams)."""
    bm = bmesh.new()
    for q in quads_:
        bm.faces.new([bm.verts.new(p) for p in q])
    o = mesh_object(name, bm, material, parent, loc)
    o.visible_shadow = shadow
    return o


def wall_r(z):
    """Drum wall radius at height z."""
    if z <= Z_WALL:
        return R
    return R + (R_TOP - R) * (z - Z_WALL) / (Z_ROOF - 0.12 - Z_WALL)


def dome_z(r):
    return Z_ROOF + (Z_DOME - Z_ROOF) * math.sqrt(max(0.0, 1 - (r / R_DOME) ** 2))


def wall_band(name, a0, a1, z0, z1, material, parent, loc, lift=0.012, segs=16):
    qs = []
    for k in range(segs):
        b0 = a0 + (a1 - a0) * k / segs
        b1 = a0 + (a1 - a0) * (k + 1) / segs
        qs.append((polar(wall_r(z0) + lift, b0, z0), polar(wall_r(z0) + lift, b1, z0), polar(wall_r(z1) + lift, b1, z1), polar(wall_r(z1) + lift, b0, z1)))
    return quads(name, qs, material, parent, loc)


def wall_panes(name, a0, a1, z0, z1, n, frac, material, parent, loc, lift=0.02):
    qs = []
    step = (a1 - a0) / n
    for k in range(n):
        b0 = a0 + step * (k + (1 - frac) / 2)
        b1 = b0 + step * frac
        qs.append((polar(wall_r(z0) + lift, b0, z0), polar(wall_r(z0) + lift, b1, z0), polar(wall_r(z1) + lift, b1, z1), polar(wall_r(z1) + lift, b0, z1)))
    return quads(name, qs, material, parent, loc)


def dome_panes(name, a0, a1, t0, t1, n, frac, material, parent, loc):
    """Panes on the dome between elevation angles t0..t1 (degrees on the dome's ellipse)."""
    qs = []
    step = (a1 - a0) / n

    def p(a, t):
        r = R_DOME * math.cos(math.radians(t)) + 0.015
        z = Z_ROOF + (Z_DOME - Z_ROOF) * math.sin(math.radians(t)) + 0.015
        return polar(r, a, z)

    for k in range(n):
        b0 = a0 + step * (k + (1 - frac) / 2)
        b1 = b0 + step * frac
        qs.append((p(b0, t0), p(b1, t0), p(b1, t1), p(b0, t1)))
    return quads(name, qs, material, parent, loc)


def outer_profile():
    """The outside of a wedge from the wall foot to the apex: [(r, z)] (the shell's visible surface)."""
    pts = [(R, Z_BASE), (R, Z_WALL), (R_TOP, Z_ROOF - 0.12), (R_TOP - 0.12, Z_ROOF), (R_DOME, Z_ROOF)]
    for i in range(1, 11):
        t = math.pi / 2 * i / 10
        pts.append((R_DOME * math.cos(t), Z_ROOF + (Z_DOME - Z_ROOF) * math.sin(t)))
    pts[-1] = (0.0, Z_DOME)
    return pts


def seam_strip(name, a, into, material, parent, loc, width=0.07, lift=0.014):
    """A glowing strip along a wedge's cut edge at angle a, `into` = +1/-1 toward the wedge's inside."""
    prof = outer_profile()
    t = Vector((-math.sin(a), math.cos(a), 0)) * into
    qs = []
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        d = Vector((r1 - r0, z1 - z0))
        if d.length < 1e-6:
            continue
        nrm = Vector((d.y, -d.x)).normalized()  # outward in r-z for a counter-clockwise outer surface walk
        if nrm.x < -0.01 or (abs(nrm.x) < 0.01 and nrm.y < 0):
            nrm = -nrm
        p0 = polar(r0 + nrm.x * lift, a, z0 + nrm.y * lift)
        p1 = polar(r1 + nrm.x * lift, a, z1 + nrm.y * lift)
        o0, o1 = t * 0.012, t * (0.012 + width)
        qs.append((p0 + o0, p1 + o0, p1 + o1, p0 + o1))
    return quads(name, qs, material, parent, loc)


def pivot(name, loc, parent, yaw_deg=0.0, tilt_deg=0.0):
    """An empty whose local X is the hinge axis: yawed about Z, then tilted about its own X (Blender XYZ euler)."""
    e = empty(name, loc, parent)
    e.rotation_euler = (math.radians(tilt_deg), 0, math.radians(yaw_deg))
    return e


# ---------------------------------------------------------------- the wedges


def shell(key, root):
    """One wedge's drum + roof + dome, plus the bands, panes, seams and under-frame every wedge has."""
    centre = math.radians(WEDGES[key])
    a0, a1 = centre - math.radians(HALF), centre + math.radians(HALF)
    off = Vector((math.cos(centre), math.sin(centre), 0)) * GAP
    M = empty(f"Module{key}", (0, 0, 0), root)
    prof = [(0.0, Z_BASE)] + outer_profile()[:-1] + [(0.0, Z_DOME)]
    revolve(f"{key}Shell", prof, a0, a1, 24, MT["hull"], M, off)
    # dark under-frame (skids) and its glow line
    revolve(f"{key}Skirt", [(0.0, 0.0), (R - 0.4, 0.0), (R - 0.4, Z_BASE + 0.02), (0.0, Z_BASE + 0.02)], a0 + 0.02, a1 - 0.02, 16, MT["dark"], M, off)
    qs = []
    for k in range(16):
        b0 = a0 + 0.03 + (a1 - a0 - 0.06) * k / 16
        b1 = a0 + 0.03 + (a1 - a0 - 0.06) * (k + 1) / 16
        rr = R - 0.4 + 0.012
        qs.append((polar(rr, b0, 0.09), polar(rr, b1, 0.09), polar(rr, b1, 0.2), polar(rr, b0, 0.2)))
    quads(f"{key}SkidGlow", qs, MT["skid"], M, off)
    # roof deck ring in panel grey
    revolve(f"{key}Deck", [(R_DOME - 0.02, Z_ROOF), (R_TOP - 0.14, Z_ROOF), (R_TOP - 0.14, Z_ROOF + 0.05), (R_DOME - 0.02, Z_ROOF + 0.05)],
            a0 + 0.01, a1 - 0.01, 24, MT["panel"], M, off)
    # owner stripe low on the wall, a dark band with the window row above it
    wall_band(f"{key}Stripe", a0 + 0.02, a1 - 0.02, 0.95, 1.12, MT["owner"], M, off)
    wall_band(f"{key}WindowBand", a0 + 0.02, a1 - 0.02, 1.42, 2.12, MT["trim"], M, off, lift=0.01)
    wall_band(f"{key}Rim", a0 + 0.02, a1 - 0.02, Z_ROOF - 0.32, Z_ROOF - 0.2, MT["trim"], M, off, lift=0.01)
    dome_panes(f"{key}DomeGlass", a0 + 0.04, a1 - 0.04, 14, 34, 6, 0.78, MT["glass"], M, off)
    seam_strip(f"{key}Seam0", a0, +1, MT["seam"], M, off)
    seam_strip(f"{key}Seam1", a1, -1, MT["seam"], M, off)
    return M, a0, a1, off


def module_a(root):
    """Command: vestibule with the crew door at the front, comms mast stowed over the dome on a cradle."""
    M, a0, a1, off = shell("A", root)
    wall_panes("AWindows", a0 + 0.05, a1 - 0.05, 1.5, 2.04, 7, 0.62, MT["window"], M, off)
    # vestibule (Blender -Y = Godot +Z front), door at y -5.76
    V = empty("Vestibule", (0, 0, 0), M)
    box("VestBody", (2.4, 1.9, 2.2), (0, -5.0, Z_BASE + 1.1), MT["hull"], V, 0.06)
    box("VestBase", (2.2, 1.7, Z_BASE), (0, -4.95, Z_BASE / 2), MT["dark"], V, 0.0)
    box("Canopy", (2.9, 2.3, 0.12), (0, -5.15, Z_BASE + 2.3), MT["owner"], V, 0.03)
    box("DoorFrame", (1.5, 0.06, 2.0), (0, -5.96, Z_BASE + 1.0), MT["trim"], V, 0.02)
    box("Door", (1.2, 0.06, 1.75), (0, -5.98, Z_BASE + 0.92), MT["glass"], V, 0.0)
    box("DoorLight", (1.0, 0.08, 0.1), (0, -5.98, Z_BASE + 2.0), MT["window"], V, 0.0).visible_shadow = False
    box("Step", (2.0, 0.7, 0.18), (0, -6.25, 0.09), MT["panel"], V, 0.03)
    for s in (-1, 1):
        box(f"VestWindow{s}", (0.06, 0.8, 0.5), (s * 1.21, -5.0, Z_BASE + 1.5), MT["window"], V, 0.0).visible_shadow = False
        box(f"VestStripe{s}", (0.05, 1.9, 0.14), (s * 1.215, -5.0, 1.03), MT["owner"], V, 0.0)
    # mast collar on the apex and the cradle the stowed mast rests on
    zcyl("MastCollar", 0.42, Z_DOME - 0.08, MAST_Z, MT["trim"], M, segs=20)
    zcyl("MastCollarRing", 0.46, MAST_Z - 0.18, MAST_Z - 0.08, MT["seam"], M, segs=20)
    for s in (-1, 1):
        box(f"CradleArm{s}", (0.1, 0.12, MAST_Z - 0.22 - dome_z(3.0) + 0.1), (s * 0.3, -3.0, (MAST_Z - 0.22 + dome_z(3.0)) / 2), MT["trim"], M, 0.0)
    box("CradleSaddle", (0.75, 0.25, 0.08), (0, -3.0, MAST_Z - 0.26), MT["dark"], M, 0.0)
    # roof gear: two vents
    for k, a in enumerate((-115, -65)):
        p = polar(3.85, math.radians(a), Z_ROOF + 0.05)
        box(f"AVent{k}", (0.7, 0.5, 0.28), p + Vector((0, 0, 0.14)) + off, MT["panel"], M, 0.04, rot=(0, 0, math.radians(a + 90)))

    # mast: hinge at the apex, tube up local Z; upper section slides out; dish and beacon on top
    mast = pivot("Mast", (0, 0, MAST_Z), M)
    zcyl("MastTube", 0.22, 0.0, MAST_LEN, MT["hull"], mast, segs=16)
    zcyl("MastHinge", 0.3, -0.12, 0.25, MT["trim"], mast, segs=16)
    zcyl("MastBand", 0.235, MAST_LEN - 0.35, MAST_LEN - 0.2, MT["owner"], mast, segs=16)
    zcyl("MastLip", 0.27, MAST_LEN - 0.1, MAST_LEN, MT["trim"], mast, segs=16)
    top = empty("MastTop", (0, 0, MAST_LEN), mast)
    zcyl("TopTube", 0.15, -0.3, TOP_LEN, MT["steel"], top, segs=12)
    zcyl("TopRing", 0.34, TOP_LEN - 0.65, TOP_LEN - 0.5, MT["trim"], top, segs=16)
    zcyl("TopRingGlow", 0.35, TOP_LEN - 0.5, TOP_LEN - 0.45, MT["comms"], top, segs=16)
    zcyl("TopCap", 0.2, TOP_LEN, TOP_LEN + 0.12, MT["trim"], top, segs=12)
    strut("Whip", (0.12, 0.0, TOP_LEN), (0.12, 0.0, TOP_LEN + 1.1), 0.025, MT["dark"], top, 6)
    beacon = sphere("Beacon", 0.17, (0, 0, TOP_LEN + 0.28), MT["beacon"], top, 14)
    beacon.visible_shadow = False
    # dish on an arm off the right side of the upper section (+X stays sideways while the mast is stowed): bowl opens
    # along the pivot's local +Z; the pivot is yawed 90 degrees so its X hinge runs fore-aft; rest tilt 40 = facing up
    # and out to the right; stowed it faces straight along the mast, folded like a closed umbrella
    strut("DishArm", (0.12, 0.0, TOP_LEN - 1.25), (1.0, 0.0, TOP_LEN - 1.25), 0.06, MT["trim"], top, 6)
    dish = pivot("Dish", (1.0, 0.0, TOP_LEN - 1.25), top, 90.0, 40.0)
    r, d, t = 0.95, 0.3, 0.035
    OP["lathe"]("DishBowl", [(0, 0), (r * 0.35, d * 0.12), (r * 0.7, d * 0.5), (r, d), (r * 0.97, d + t), (r * 0.68, d * 0.5 + t),
                             (r * 0.34, d * 0.12 + t), (0, t)], MT["hull"], dish, (0, 0, 0.0), 32)
    zcyl("DishHub", 0.14, -0.12, 0.06, MT["trim"], dish, segs=12)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        strut(f"DishFeedLeg{k}", (0.8 * r * math.cos(a), 0.8 * r * math.sin(a), d * 0.75), (0, 0, 0.62), 0.018, MT["steel"], dish, 5)
    sphere("DishHorn", 0.08, (0, 0, 0.64), MT["comms"], dish, 10)
    return M


def solar_wing(name, a_deg, M, off):
    """A two-leaf solar wing hinged at the roof edge: rest = lying outward, tilted up 10 degrees."""
    a = math.radians(a_deg)
    hinge = polar(4.74, a, Z_ROOF - 0.08) + off
    box(f"{name}Bracket", (0.5, 0.42, 0.22), polar(4.5, a, Z_ROOF - 0.08) + off, MT["trim"], M, 0.02, rot=(0, 0, a - math.pi / 2))
    P = pivot(name, hinge, M, a_deg - 90.0, 10.0)
    for leaf, parent, y0 in ((0, P, 0.06), (1, None, 0.04)):
        if leaf == 1:
            parent = empty(f"{name}Outer", (0, 2.1, 0.07), P)
        zo = 0.0 if leaf == 0 else -0.07
        box(f"{name}Frame{leaf}", (2.2, 2.0, 0.05), (0, y0 + 1.0, zo), MT["trim"], parent, 0.01)
        for i in range(2):
            for j in range(3):
                c = box(f"{name}Cell{leaf}{i}{j}", (1.0, 0.6, 0.02), (-0.53 + i * 1.06, y0 + 0.36 + j * 0.64, zo + 0.03), MT["solar"], parent, 0.0)
                c.visible_shadow = False
    box(f"{name}Hinge", (2.2, 0.1, 0.1), (0, 2.08, 0.035), MT["steel"], P, 0.0)
    return P


def module_b(root):
    """Hab: two folding solar wings, airlock, radome."""
    M, a0, a1, off = shell("B", root)
    wall_panes("BWindows", a0 + 0.05, a1 - 0.05, 1.5, 2.04, 7, 0.62, MT["window"], M, off)
    solar_wing("SolarB0", 128.0, M, off)
    solar_wing("SolarB1", 172.0, M, off)
    # airlock on the outer wall between the wings' reach and module A
    a = math.radians(196)
    box("Airlock", (1.2, 1.1, 1.7), polar(4.95, a, Z_BASE + 0.85) + off, MT["panel"], M, 0.05, rot=(0, 0, a - math.pi / 2))
    box("AirlockDoor", (0.7, 0.05, 1.3), polar(5.52, a, Z_BASE + 0.8) + off, MT["trim"], M, 0.0, rot=(0, 0, a - math.pi / 2))
    box("AirlockLamp", (0.4, 0.06, 0.08), polar(5.53, a, Z_BASE + 1.55) + off, MT["window"], M, 0.0, rot=(0, 0, a - math.pi / 2)).visible_shadow = False
    p = polar(3.85, math.radians(150), Z_ROOF + 0.05) + off
    OP["dome"]("Radome", 0.42, 0.5, MT["hull"], M, tuple(p), 20, base=0.0)
    zcyl("RadomeBase", 0.46, Z_ROOF + 0.02, Z_ROOF + 0.1, MT["trim"], M, xy=(p.x, p.y), segs=20)
    return M


def module_c(root):
    """Ops: storage vault, roof radiators, floodlight arm."""
    M, a0, a1, off = shell("C", root)
    wall_panes("CWindows", a0 + 0.05, a1 - 0.05, 1.5, 2.04, 6, 0.55, MT["window"], M, off)
    a = math.radians(62)
    yaw = a - math.pi / 2
    box("Vault", (2.5, 1.5, 2.0), polar(5.15, a, Z_BASE + 1.0) + off, MT["trim"], M, 0.06, rot=(0, 0, yaw))
    box("VaultBase", (2.3, 1.3, Z_BASE), polar(5.1, a, Z_BASE / 2) + off, MT["dark"], M, 0.0, rot=(0, 0, yaw))
    box("VaultBand", (2.54, 1.54, 0.16), polar(5.15, a, Z_BASE + 1.45) + off, MT["owner"], M, 0.0, rot=(0, 0, yaw))
    box("VaultDoor", (1.3, 0.05, 1.25), polar(5.92, a, Z_BASE + 0.75) + off, MT["vault"], M, 0.0, rot=(0, 0, yaw))
    for k, z in enumerate((0.55, 0.95)):
        box(f"VaultBolt{k}", (1.0, 0.07, 0.06), polar(5.95, a, Z_BASE + z) + off, MT["steel"], M, 0.0, rot=(0, 0, yaw))
    # radiators: hinged on brackets at the roof edge, standing up (leaning out 10 degrees) at rest; stowed they hang
    # down outside the wall like the solar wings
    for k, a_deg in enumerate((2.0, 38.0)):
        b = math.radians(a_deg)
        box(f"RadiatorC{k}Bracket", (1.2, 0.4, 0.18), polar(4.45, b, Z_ROOF - 0.05) + off, MT["trim"], M, 0.02, rot=(0, 0, b - math.pi / 2))
        P = pivot(f"RadiatorC{k}", polar(4.66, b, Z_ROOF - 0.05) + off, M, a_deg - 90.0, 80.0)
        box(f"RadiatorC{k}Panel", (1.5, 1.4, 0.06), (0, 0.75, 0.0), MT["panel"], P, 0.01)
        for i in range(5):
            box(f"RadiatorC{k}Fin{i}", (0.05, 1.3, 0.12), (-0.6 + i * 0.3, 0.75, 0.08), MT["steel"], P, 0.0)
        box(f"RadiatorC{k}Edge", (1.5, 0.06, 0.09), (0, 1.43, 0.0), MT["owner"], P, 0.0)
        box(f"RadiatorC{k}Knuckle", (1.4, 0.12, 0.12), (0, 0.0, 0.0), MT["trim"], P, 0.0)
    # floodlight arm at the back of the roof: its hinge runs radially (local X), so stowed it lies along the roof deck;
    # at rest it stands up with the lamp head facing outward (local +X) and down
    b = math.radians(80)
    F = pivot("Flood", polar(3.8, b, Z_ROOF + 0.05) + off, M, 80.0, 0.0)
    box("FloodFoot", (0.4, 0.4, 0.16), (0, 0, 0.0), MT["trim"], F, 0.02)
    zcyl("FloodPole", 0.08, 0.0, 2.2, MT["steel"], F, segs=10)
    box("FloodHead", (0.35, 0.9, 0.3), (0.12, 0, 2.3), MT["dark"], F, 0.03, rot=(0, math.radians(35), 0))
    lens = box("FloodLens", (0.04, 0.78, 0.22), (0.27, 0, 2.2), MT["lamp"], F, 0.0, rot=(0, math.radians(35), 0))
    lens.visible_shadow = False
    return M


def outpost():
    root = empty("outpost_l1", (0, 0, 0))
    module_a(root)
    module_b(root)
    module_c(root)
    return root


# ---------------------------------------------------------------- poses (same table as LandingView.Stowed)

REST = {}


def remember_rest(root):
    for o in [root] + list(root.children_recursive):
        if o.name in STOWED:
            REST[o.name] = o.matrix_basis.copy()


def pose(unfurl=1.0, crawl=0.0, lowered=None):
    """unfurl 0 = stowed .. 1 = rest (per part the same easing windows as the game would use are not needed here);
    crawl = distance each module sits out along its wedge's centre line."""
    for name, steps in STOWED.items():
        o = bpy.data.objects.get(name)
        if o is None:
            continue
        m = REST[name].copy()
        k = 1.0 - unfurl
        for kind, v in steps:
            if kind == "rot":
                m = m @ Matrix.Rotation(math.radians(v * k), 4, "X")
            elif kind == "slide":
                m = m @ Matrix.Translation((0, 0, v * k))
            elif kind == "scale":
                s = [1 + (c - 1) * k for c in v]
                m = m @ Matrix.Diagonal((*s, 1.0))
        o.matrix_basis = m
    for key, a in WEDGES.items():
        mod = bpy.data.objects[f"Module{key}"]
        d = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        mod.location = d * crawl


# ---------------------------------------------------------------- game export


KEEP = set(STOWED) | {"ModuleA", "ModuleB", "ModuleC", "Beacon"}


def join_for_game(root):
    """build_dropship.py's join: modifiers applied, every mesh joined into "<pivot>Body" under its nearest kept
    ancestor (ModuleA/B/C or an animated pivot), one material slot per material; Beacon stays its own mesh."""
    meshes = [o for o in root.children_recursive if o.type == "MESH" and not o.hide_render]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    applied = {o: bpy.data.meshes.new_from_object(o.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
               for o in meshes if o.modifiers}
    for o, me in applied.items():
        o.modifiers.clear()
        o.data = me

    def pivot_of(o):
        p = o.parent
        while p is not None and p is not root and p.name not in KEEP:
            p = p.parent
        return p or root

    groups = {}
    for o in meshes:
        if o.name in KEEP:
            continue
        groups.setdefault(pivot_of(o), []).append(o)
    for o in meshes:
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        o.data.uv_layers[0].name = "UVMap"
        while len(o.data.uv_layers) > 1:
            o.data.uv_layers.remove(o.data.uv_layers[1])
    for piv, objs in groups.items():
        # bring every member into the pivot's space first (they may hang under intermediate empties)
        for o in objs:
            mw = o.matrix_world.copy()
            o.parent = piv
            o.matrix_world = mw
        bpy.context.view_layer.update()
        target = objs[0]
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = target
        if len(objs) > 1:
            bpy.ops.object.join()
        target.name = f"{piv.name}Body"
        target.data.name = target.name
        target.data.transform(target.matrix_basis)
        target.matrix_basis = Matrix.Identity(4)
    while True:
        bare = [o for o in root.children_recursive if o.type == "EMPTY" and o.name not in KEEP and not o.children]
        if not bare:
            break
        for o in bare:
            bpy.data.objects.remove(o)
    names = sorted(o.name for o in root.children_recursive)
    missing = sorted(KEEP - set(names))
    if missing:
        sys.exit(f"join_for_game lost {missing}")
    print(f"joined for the game: {len(names) + 1} nodes:", ", ".join(names))


def export(root, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        if not o.hide_render:
            o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_lights=False, export_cameras=False)
    print("exported", path, os.path.getsize(path), "bytes")


# ---------------------------------------------------------------- previews


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None or o.parent not in new]
    holder = empty(os.path.basename(path)[:-4] + "_holder", (0, 0, 0))
    for o in roots:
        o.parent = holder
    return holder, new


def find(objs, name):
    for o in objs:
        if o.name == name or o.name.split(".")[0] == name:
            return o
    return None


def cable(name, a, b):
    o = strut(name, a, b, 0.035, MT["dark"], None, 6)
    return o


def preview_pad():
    pad = OP["repair_pad"](None)
    pad.location = (9.4, -2.0, 0.0)  # BuildingModels.RepairPadOffset (Godot (9.4, 0, 2))
    return pad


def preview(root, folder):
    scene = bpy.context.scene
    OP["materials"]()
    OP["stage"]()
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    if THREADS:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = THREADS
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    cam.data.clip_end = 2000
    os.makedirs(folder, exist_ok=True)
    pad = preview_pad()
    rover = BV["rover_mk1"](BV["palette"]())
    rover.location = (9.4, -2.3, 0.22)
    rover.rotation_euler.z = math.pi
    ships = []

    def shoot(name, pos, target, lens=None, angle_y=None, ortho=None, w=RES, h=None):
        scene.render.resolution_x = w
        scene.render.resolution_y = h or int(w * 9 / 16)
        if ortho:
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = ortho
        else:
            cam.data.type = "PERSP"
            if lens:
                cam.data.sensor_fit = "AUTO"
                cam.data.lens = lens
            if angle_y:
                cam.data.sensor_fit = "VERTICAL"
                cam.data.angle_y = math.radians(angle_y)
        cam.location = pos
        cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        path = os.path.join(folder, f"outpost-l1-{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path, flush=True)

    def game_cam(dist, target=(0, 0, 0.5)):
        x = min(1.0, max(0.0, (dist - 15) / (160 - 15)))
        t = x * x * (3 - 2 * x)
        d = Vector((0, 0.34, 0.94)).lerp(Vector((0, 0.82, 0.57)), t).normalized()
        return Vector(target) + Vector((0, -d.z, d.y)) * dist, target

    for name in SHOTS:
        pad.hide_render = rover.hide_render = name in ("crawl", "convoy")
        for o in [pad] + list(pad.children_recursive) + [rover] + list(rover.children_recursive):
            o.hide_render = name in ("crawl", "convoy")
        for s in ships:
            for o in [s] + list(s.children_recursive):
                o.hide_render = name != "convoy"
        if name == "assembled":
            pose(1.0, 0.0)
            shoot(name, (17.0, -24.0, 13.0), (1.5, 0.0, 3.0), lens=32, w=RES, h=int(RES * 2 / 3))
        elif name == "game":
            pose(1.0, 0.0)
            pos, target = game_cam(50.0)
            shoot(name, pos, target, angle_y=45)
        elif name == "top":
            pose(1.0, 0.0)
            shoot(name, (0, -0.01, 80), (0, 0, 0), ortho=34, w=RES, h=RES)
        elif name == "unfurl":
            pose(0.45, 0.0)
            shoot(name, (17.0, -24.0, 13.0), (0.0, 0.0, 3.0), lens=32, w=RES, h=int(RES * 2 / 3))
        elif name == "crawl":
            pose(0.0, 6.0)
            shoot(name, (24.0, -34.0, 20.0), (0.0, 0.0, 2.0), lens=32, w=RES, h=int(RES * 2 / 3))
        elif name == "convoy":
            pose(0.0, 0.0)
            convoy(ships)
            shoot(name, (34.0, -50.0, 24.0), (0.0, 1.0, 12.0), lens=30, w=RES, h=int(RES * 2 / 3))


def convoy(ships):
    """Three drop ships (the game glb) in flight, legs folded, each with its module hanging on four cables, at
    staggered heights as in LandingView: module origins 8.5 out, ships nose-out, module dir -> ship forward."""
    path = os.path.join(REPO, "game", "models", "vehicles", "dropship.glb")
    heights = {"A": 10.0, "B": 13.5, "C": 17.0}
    for key, a_deg in WEDGES.items():
        a = math.radians(a_deg)
        d = Vector((math.cos(a), math.sin(a), 0))
        lift = heights[key]
        mod = bpy.data.objects[f"Module{key}"]
        mod.location = d * 8.5 + Vector((0, 0, lift))
        holder, objs = import_glb(path)
        ships.append(holder)
        # Godot ship frame: nose -Z, the module's centroid 2.6 out sits under the ship at ship z 0.3, top 4.0 below.
        # In Blender the glb's nose is +Y; turn the ship so its nose points along d.
        yaw = math.atan2(d.y, d.x) - math.pi / 2
        centroid = d * (8.5 + 2.6)
        holder.rotation_euler = (0, 0, yaw)
        # ship-local Godot (0, -4.0, 0.3) = Blender (0, -0.3, -4.0) from the ship origin to the module's centroid foot
        fwd = d
        holder.location = centroid + Vector((0, 0, lift + 4.0)) + fwd * 0.3
        for k in range(4):
            leg = find(objs, f"Leg{k}")
            if leg is not None:  # glTF imports use quaternions: fold the legs up under the pods for flight
                leg.rotation_quaternion = leg.rotation_quaternion @ Matrix.Rotation(math.radians(-90 if k < 2 else 90), 4, "X").to_quaternion()
        bpy.context.view_layer.update()
        side = Vector((-d.y, d.x, 0))
        for i, (r, s, z_top) in enumerate(((3.9, 1.3, Z_ROOF), (3.9, -1.3, Z_ROOF), (1.8, 0.9, dome_z(1.97)), (1.8, -0.9, dome_z(1.97)))):
            bottom = d * (8.5 + r) + side * s + Vector((0, 0, lift + z_top))
            top = Vector((bottom.x, bottom.y, holder.location.z + 0.85))
            cable(f"Cable{key}{i}", top, bottom)
        for k in range(6):
            th = find(objs, f"Thruster{k}")
            if th is not None and th.active_material is not None:
                p = th.active_material.node_tree.nodes.get("Principled BSDF")
                if p is not None:
                    p.inputs["Emission Strength"].default_value = 30.0 if k < 4 else 6.0


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    root = outpost()
    bpy.context.view_layer.update()
    remember_rest(root)
    if OUT:
        if JOIN:
            join_for_game(root)
            remember_rest(root)
        export(root, OUT)
    if PREVIEW:
        preview(root, PREVIEW)


main()
