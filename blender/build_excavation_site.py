"""Archaeological Excavation Site (`excavation_site`), game-ready (civilizations phase 2, 2026-11-09; contract
docs/civilizations-phase2-contract.md "Models"): the dig a colony builds on a ruin hex of its own world.

An excavation as the RTS camera sees it from the front (access side = front = Blender -Y = Godot +Z):
  * a raised spoil MESA (top at SURF 1.4, skirt sinking to the terrain at r ~7.2; the game's terrain can't be cut, so
    the dig is cut into a platform of its own spoil): timber steps up the front;
  * the DIG: a stepped trench grid cut into the mesa, two terraces and a bottom (strata = a darker soil per layer),
    survey pegs + string lines dividing it into a 4 x 3 grid, a baulk left standing, buckets, trowels, trays;
  * at the bottom the exposed ALIEN RUIN (the native towns kit's style: lobed, fluted, scalloped): a curved
    scalloped wall emerging from the back terrace, three fluted column stumps, a toppled column in drums, a pointed
    arch fragment coming out of the left wall, hexagonal paving, a glyph slab with a faint "Glow" inlay, and the
    three-lobed DEFENCE PYLON (RuinMetal bands, Glow slit) with the "SparkPoint" at its tip;
  * a tube SCAFFOLD tower in the front-left corner of the pit with a deck at the rim, ladders down the terraces;
  * a small GANTRY CRANE over the pit on rails ("Crane" travels front-back between CraneMin/CraneMax) with a hoist,
    "Cable" (unit ropes 0..-1, scaled to the drop) and "Hook" (a skip bucket; HookLow = in the pit);
  * two SIFTING SCREENS "Sieve0"/"Sieve1" hung on A-frames behind the pit (shake along local X) over spoil heaps
    (dust points Dust0/Dust1; Dust2 at the bottom of the dig);
  * the SURVEY HUT (prefab, Owner band, "Status" board, beacon mast with the "Beacon") with a canvas awning, the
    FINDS TABLE (trays, artifacts) with labelled crates, a generator + water tank, lamp masts ("Masts" pivot).

Frame and conventions (building_kit.py): Blender +Z up, front toward -Y (= Godot +Z); 1 unit = 1 world unit.

BUILD PIECES (Build0..Build10): Ground, Ruin, Grid, Scaffold, Crane, Sieves, Hut, Finds, Utilities, Sign, Lights.

GAME CONTRACT (BuildingModels.Excavation.cs, PlanetView.Excavation.cs):
  Crane              gantry pivot on the rails along the pit's sides: Position lerps between siblings CraneMin /
                     CraneMax (front-back over the dig; unrotated, so Godot = Blender axes).
  Hook               child of Crane, the skip: moves along local Y (rest = raised; sibling HookLow = in the pit).
  Cable              child of Crane at the hoist: ropes run 0..-1 along local Y; Scale.Y = drop to the hook.
  Sieve0, Sieve1     screen pivots: shake along local X (a few cm).
  Dust0..2           dust puff points (under the sieves, at the bottom of the dig).
  SparkPoint         tip of the defence pylon (sparks when the dig is locked).
  DigLight           where the game hangs its OmniLight over the pit.
  materials          Status (hut board), Glow (ruin inlay + pylon slit), Lamp, Window, Beacon, RuinStone, RuinMetal.
  Masts              scale pivot.  OptAwning, OptGenerator, OptBarrow = optional parts.
  SignFront ("EXCAVATION nn"), LabelFinds ("FINDS"), LabelHut ("SURVEY")  decal markers.
  AddOnSpot0..4, RoofSpot0..1  variety spots.

Run (bpy 5.x module venv):
  /home/user/.bpyvenv/bin/python blender/build_excavation_site.py --game     (export game/models/buildings/excavation_site.glb)
  /home/user/.bpyvenv/bin/python blender/build_excavation_site.py --game --preview blender/previews/excavation --shots hero,game,close,night,pieces
  python3 blender/build_excavation_site.py --sheet blender/previews/excavation   (system python + PIL: pieces sheet)
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

if "--sheet" in sys.argv:
    # system python (PIL), no bpy: fold the pieces renders into one image
    from PIL import Image
    folder = sys.argv[sys.argv.index("--sheet") + 1]
    pre = "excavation_site-pieces"
    names = sorted(f for f in os.listdir(folder) if f.startswith(pre) and f[len(pre):len(pre) + 2].isdigit())
    tiles = [Image.open(os.path.join(folder, n)).convert("RGB") for n in names]
    w, h = tiles[0].size
    img = Image.new("RGB", (2 * w, ((len(tiles) + 1) // 2) * h), (14, 16, 20))
    for i, t in enumerate(tiles):
        img.paste(t, ((i % 2) * w, (i // 2) * h))
    img.save(os.path.join(folder, f"{pre}.png"))
    print("wrote", os.path.join(folder, f"{pre}.png"), "from", names)
    sys.exit(0)

sys.path.insert(0, HERE)
import building_kit as K  # noqa: E402
from building_kit import MT, P, bx, obox, beam, zcyl, sphere, empty, ladder, pipe  # noqa: E402

CFG = K.setup("excavation_site")

SURF = 1.4                                  # top of the spoil mesa
Z1, Z2, Z3 = 0.95, 0.5, 0.06                # terrace 1, terrace 2, the bottom of the dig
R0 = (-4.4, 1.8, -2.8, 2.6)                 # pit opening x0, x1, y0, y1
R1 = (-3.9, 1.3, -2.3, 2.1)
R2 = (-3.45, 0.85, -1.85, 1.65)             # the bottom
PCX, PCY = (R0[0] + R0[1]) / 2, (R0[2] + R0[3]) / 2
TOP_R, BASE_R = 6.45, 7.25                  # mesa top edge / skirt foot radius
SPAN = 3.5                                  # rails at PCX -/+ SPAN, along Y (legs beside the pit, not in front)
RAIL_X = (PCX - SPAN, PCX + SPAN)
RAIL_Y = (-3.15, 2.95)
CRANE_Y = (-1.4, 1.2)                       # CraneMin / CraneMax (the hoist stays over the bottom)
CRANE_REST = PCY
BRIDGE_Z = SURF + 3.45
HOOK_UP = SURF + 1.75
HOOK_LOW = Z3 + 0.62
HUT = (2.45, 5.0, 1.65, 3.6)                # x0, x1, y0, y1
HUT_H = 2.45
SIEVES = [(-3.25, 3.95), (-1.05, 4.05)]
TABLE = (2.55, 4.65, -2.75, -1.95)
PIECES = ["Ground", "Ruin", "Grid", "Scaffold", "Crane", "Sieves", "Hut", "Finds", "Utilities", "Sign", "Lights"]


def materials():
    K.materials()
    K.mat("status", "Status", (0.3, 1.0, 0.5), 0.0, 0.3, emission=(0.3, 1.0, 0.5), strength=3.0)
    K.mat("label", "Label", (0.9, 0.9, 0.88), 0.0, 0.6, textured=True)
    K.mat("soil", "Soil", (0.44, 0.34, 0.24), 0.0, 0.95, textured=True, cull=False)
    K.mat("soil1", "SoilMid", (0.33, 0.23, 0.16), 0.0, 0.95, textured=True, cull=False)
    K.mat("soil2", "SoilDeep", (0.25, 0.18, 0.13), 0.0, 0.95, textured=True, cull=False)
    K.mat("soil3", "SoilBed", (0.22, 0.17, 0.13), 0.0, 0.95, textured=True, cull=False)
    K.mat("stone", "RuinStone", (0.66, 0.65, 0.62), 0.0, 0.85, textured=True, cull=False)
    K.mat("rmetal", "RuinMetal", (0.36, 0.31, 0.24), 0.75, 0.42, textured=True)
    K.mat("glow", "Glow", (0.2, 0.55, 0.5), 0.0, 0.4, emission=(0.35, 0.95, 0.85), strength=1.2)
    K.mat("canvas", "Canvas", (0.78, 0.72, 0.58), 0.0, 0.9, textured=True, cull=False)
    K.mat("tarp", "Tarp", (0.14, 0.3, 0.52), 0.0, 0.7, textured=True, cull=False)
    K.mat("wood", "Wood", (0.46, 0.36, 0.25), 0.0, 0.85, textured=True)
    K.mat("crate_a", "FindCrate", (0.36, 0.44, 0.3), 0.1, 0.6, textured=True)
    K.mat("crate_b", "FindCrateB", (0.74, 0.52, 0.2), 0.1, 0.55, textured=True)
    K.mat("bucket", "Bucket", (0.82, 0.32, 0.1), 0.1, 0.5, textured=True)


def box(name, x0, x1, y0, y1, z0, z1, m, g, bev=0.0):
    """An axis-aligned box by its extents."""
    return bx(name, (x1 - x0, y1 - y0, z1 - z0), P((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), m, g, bev)


def jit(k, a=0.0):
    """A stable pseudo-random 0..1 from an integer (no RNG state)."""
    v = math.sin(k * 12.9898 + a * 78.233) * 43758.5453
    return v - math.floor(v)


def lobed(name, prof, segs, m, g, loc=(0, 0, 0), lobes=0, amp=0.0, twist=0.0, cap=True, jag=0.0, seed=1, rot=None):
    """The towns kit's lathe: a surface of revolution through (r, z) with lobes (fluting / three-lobed sections),
    twist (degrees per unit of height) and a jagged broken top ring (jag = how much its heights vary)."""
    bm = K.bmesh.new()
    tw = math.radians(twist)
    rings = []
    last = len(prof) - 1
    for k, (r, z) in enumerate(prof):
        if r <= 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
            continue
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            rr = r * (1.0 + amp * math.cos(lobes * (a - tw * z))) if lobes else r
            zz = z - (jag * jit(seed * 31 + i) if k == last and jag else 0.0)
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), zz)))
        rings.append(ring)
    for A, B in zip(rings, rings[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            if len(A) == 1 and len(B) == 1:
                continue
            if len(B) == 1:
                bm.faces.new((A[i], A[j], B[0]))
            elif len(A) == 1:
                bm.faces.new((A[0], B[j], B[i]))
            else:
                bm.faces.new((A[i], A[j], B[j], B[i]))
    if cap and len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    o = K.mesh_object(name, bm, m, g, loc, smooth=False)
    if rot:
        o.rotation_euler = rot
    return o


def rect_at(rect, a):
    """Where a ray from the pit centre at angle a meets the rectangle."""
    x0, x1, y0, y1 = rect
    cx, cy = PCX, PCY
    dx, dy = math.cos(a), math.sin(a)
    t = min(((x1 - cx) / dx if dx > 1e-9 else (x0 - cx) / dx if dx < -1e-9 else 1e9),
            ((y1 - cy) / dy if dy > 1e-9 else (y0 - cy) / dy if dy < -1e-9 else 1e9))
    return cx + dx * t, cy + dy * t


def angles(n=24):
    """n even angles plus the pit's corners (so the rectangles keep sharp corners)."""
    out = {round(2 * math.pi * k / n, 6) for k in range(n)}
    for x in (R0[0], R0[1]):
        for y in (R0[2], R0[3]):
            out.add(round(math.atan2(y - PCY, x - PCX) % (2 * math.pi), 6))
    return sorted(out)


def corners(rect):
    x0, x1, y0, y1 = rect
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]  # CCW from above


def ring_faces(outer, inner):
    """Quads between two closed loops of 3D points (same count, CCW from above, outer first): faces up/out."""
    n = len(outer)
    return [(outer[k], outer[(k + 1) % n], inner[(k + 1) % n], inner[k]) for k in range(n)]


def mesa_r(a):
    """The mesa's irregular outline radius at angle a (spoil, not a neat circle)."""
    return TOP_R * (0.95 + 0.05 * math.sin(3 * a + 0.7) + 0.03 * math.sin(7 * a + 2.1))


# ---------------------------------------------------------------- pieces


def ground(g):
    """The spoil mesa, the stepped dig with a darker soil for every layer, timber steps up the front."""
    ang = angles()
    # mesa top: from the outline in to the pit's rim (sampled on the same rays)
    top = []
    for a in ang:
        # where the ray from the pit centre meets the mesa's outline (centred on the tile, not the pit)
        dx, dy = math.cos(a), math.sin(a)
        pd = PCX * dx + PCY * dy
        t = -pd + math.sqrt(max(0.0, pd * pd - (PCX * PCX + PCY * PCY) + mesa_r(a) ** 2))
        top.append(P(PCX + dx * t, PCY + dy * t, SURF + 0.04 * math.sin(5 * a)))
    rim = [P(*rect_at(R0, a), SURF) for a in ang]
    K.faces_obj("MesaTop", ring_faces(top, rim), MT["soil"], g, shadow=True)
    # the skirt down to (and under) the terrain
    foot = [P(v.x * BASE_R / TOP_R, v.y * BASE_R / TOP_R, -0.35) for v in top]
    mid = [P(v.x * 1.07, v.y * 1.07, SURF * 0.55 + 0.05 * math.sin(4 * a)) for v, a in zip(top, ang)]
    K.faces_obj("MesaSkirt", ring_faces(foot, mid) + ring_faces(mid, top), MT["soil"], g, shadow=True)
    # pit walls (each layer its own soil) and the terrace floors
    walls = [(R0, SURF, Z1, "soil1"), (R1, Z1, Z2, "soil2"), (R2, Z2, Z3, "soil3")]
    for k, (rect, za, zb, key) in enumerate(walls):
        cs = corners(rect)
        quads = []
        for i in range(4):
            a, b = cs[i], cs[(i + 1) % 4]
            quads.append((P(a[0], a[1], za), P(b[0], b[1], za), P(b[0], b[1], zb), P(a[0], a[1], zb)))
        K.faces_obj(f"PitWall{k}", quads, MT[key], g, shadow=True)
    for k, (ro, ri, z) in enumerate([(R0, R1, Z1), (R1, R2, Z2)]):
        outer = [P(*rect_at(ro, a), z) for a in ang]
        inner = [P(*rect_at(ri, a), z) for a in ang]
        K.faces_obj(f"Terrace{k}", ring_faces(outer, inner), MT["soil1" if k == 0 else "soil2"], g, shadow=True)
    cs = corners(R2)
    K.faces_obj("PitFloor", [tuple(P(x, y, Z3) for x, y in cs)], MT["soil3"], g, shadow=True)
    # darker seams along the strata (a thin band at the foot of every wall)
    for k, (rect, za, zb, _) in enumerate(walls):
        cs = corners(rect)
        quads = []
        for i in range(4):
            a, b = cs[i], cs[(i + 1) % 4]
            z = zb + 0.14
            quads.append((P(a[0], a[1], z + 0.06), P(b[0], b[1], z + 0.06), P(b[0], b[1], z), P(a[0], a[1], z)))
        K.faces_obj(f"Seam{k}", [tuple(v + (P(PCX, PCY, 0) - P(v.x, v.y, 0)).normalized() * 0.004 for v in q) for q in quads], MT["dark"], g)
    # timber steps up the front of the mesa (from the access side)
    x0, x1 = 0.55, 1.95
    steps = 5
    yb, yt = -7.15, -5.75
    for s in range(steps):
        y = yb + (yt - yb) * s / steps
        z = SURF * (s + 1) / steps
        box(f"Step{s}", x0, x1, y, yt + 0.25, -0.2, z, MT["soil"], g)
        box(f"StepBoard{s}", x0, x1, y - 0.04, y + 0.06, z - 0.16, z + 0.02, MT["wood"], g)
    for k, x in enumerate((x0 - 0.06, x1 + 0.06)):
        beam(f"StepRail{k}", P(x, yb, 1.0), P(x, yt, SURF + 0.95), 0.05, MT["rail"], g)
        for j, (y, z) in enumerate(((yb + 0.1, 0.0), (yt - 0.05, SURF))):
            zcyl(f"StepPost{k}{j}", 0.035, z - 0.1, z + 1.0, MT["rail"], g, xy=(x, y), segs=6)
    for k, (x, y, yaw) in enumerate([(-7.45, -0.8, 90), (-6.9, 3.25, 90), (-2.45, 7.2, 0), (2.7, 7.05, 0), (7.4, -1.25, -90)]):
        e_ = K.point(f"AddOnSpot{k}", P(x, y, 0), g)
        e_.rotation_euler.z = math.radians(yaw)


def scallop_wall(name, c, r, a0, a1, n, g, heights, thick=0.38, seed=3):
    """A curved alien wall (part of a ring) in blocks: scalloped caps, broken heights, each block slightly askew."""
    for k in range(n):
        t0 = math.radians(a0 + (a1 - a0) * k / n)
        t1 = math.radians(a0 + (a1 - a0) * (k + 1) / n)
        tm = (t0 + t1) / 2
        h = heights[k % len(heights)]
        if h <= 0:
            continue
        p = P(c[0] + r * math.cos(tm), c[1] + r * math.sin(tm), 0)
        tang = P(-math.sin(tm), math.cos(tm), 0)
        L = 2 * r * math.sin((t1 - t0) / 2) * 0.96
        o = obox(f"{name}{k}", (thick, L, h), p + P(0, 0, Z3 + h / 2 - 0.05), tang, MT["stone"], g)
        o.rotation_euler.x += math.radians((jit(seed + k) - 0.5) * 5)
        # a scalloped cap (a flattened lobed disc) on the intact blocks
        if h > 0.55:
            lobed(f"{name}Cap{k}", [(L * 0.5, 0.0), (L * 0.56, 0.1), (L * 0.42, 0.16)], 8, MT["stone"], g,
                  loc=p + P(0, 0, Z3 + h - 0.06), lobes=4, amp=0.18).scale = (thick / L * 2.2, 1.0, 1.0)
            bpy_rot(f"{name}Cap{k}", tm)


def bpy_rot(name, yaw):
    K.bpy.data.objects[name].rotation_euler.z = yaw


def column(name, x, y, h, g, seed, r=0.26):
    """A fluted alien column stump: flared lobed base, fluted shaft, broken jagged top."""
    lobed(f"{name}Base", [(r * 1.65, 0.0), (r * 1.6, 0.14), (r * 1.25, 0.3), (r * 1.05, 0.38)], 10, MT["stone"], g,
          loc=(x, y, Z3), lobes=5, amp=0.12)
    lobed(f"{name}Shaft", [(r * 1.05, 0.0), (r, h * 0.6), (r * 0.96, h)], 10, MT["stone"], g,
          loc=(x, y, Z3 + 0.36), lobes=5, amp=0.16, twist=8, jag=min(0.3, h * 0.3), seed=seed)
    K.band(f"{name}Ring", r * 1.13, Z3 + 0.36 + h * 0.42, Z3 + 0.36 + h * 0.48, MT["rmetal"], g, xy=(x, y), segs=10)


def ruin(g):
    """The exposed ruin at the bottom of the dig."""
    # hexagonal paving (alien flagstones), some sunk or tilted
    for k, (x, y) in enumerate([(-2.6, -0.9), (-1.9, -0.5), (-1.2, -0.9), (-2.25, 0.1), (-1.55, 0.25), (-0.85, -0.15),
                                (-0.15, -0.6), (-2.85, -0.1), (-0.45, 0.55), (-1.2, -1.45)]):
        o = K.hex_prism(f"Paving{k}", 0.4, Z3 - 0.02, Z3 + 0.05 + 0.03 * jit(k, 1), MT["stone"], g, xy=(x, y))
        o.rotation_euler = (math.radians((jit(k, 2) - 0.5) * 6), math.radians((jit(k, 3) - 0.5) * 6), math.radians(30 * (k % 2)))
    # the scalloped ring wall coming out of the back terrace
    scallop_wall("RuinWall", (-1.3, 3.1), 2.55, 205, 335, 7, g, [0.75, 1.15, 1.32, 0.42, 0.95, 1.28, 0.6])
    # three fluted column stumps and the toppled one in drums
    for k, (x, y, h) in enumerate([(-2.9, 0.55, 0.85), (-1.75, 1.05, 1.32), (-0.5, 1.0, 0.48)]):
        column(f"Column{k}", x, y, h, g, seed=7 + k)
    for k, (x, y, yaw, L) in enumerate([(-1.2, -0.85, 18, 0.62), (-0.48, -1.1, 24, 0.55), (0.2, -1.32, 33, 0.5)]):
        o = lobed(f"Drum{k}", [(0.27, -L / 2), (0.25, L / 2)], 10, MT["stone"], g, loc=(x, y, Z3 + 0.25), lobes=5, amp=0.16)
        o.rotation_euler = (0, math.radians(90), math.radians(yaw + 6 * k))
    lobed("DrumCapital", [(0.42, 0.0), (0.5, 0.16), (0.3, 0.3)], 10, MT["stone"], g, loc=(0.68, -1.45, Z3 + 0.36),
          lobes=5, amp=0.14, rot=(math.radians(72), 0, math.radians(40)))
    # a pointed arch coming out of the left terrace wall (voussoirs along two arcs)
    ax, cy = R2[0] + 0.12, 0.25
    half, rise, base = 0.62, 1.0, Z3 + 0.3
    pts = []
    for k in range(9):
        a = math.pi * (1 - k / 8)
        pts.append(P(ax, cy + half * math.cos(a), base + rise * math.sin(a) ** 0.7))
    for k, (a, b) in enumerate(zip(pts, pts[1:])):
        if k == 6:
            continue  # a missing stone
        obox(f"Voussoir{k}", (0.3, (b - a).length * 1.04, 0.42), (a + b) / 2, b - a, MT["stone"], g, up=P(1, 0, 0))
    for k, y in enumerate((cy - half, cy + half)):
        box(f"ArchPier{k}", ax - 0.22, ax + 0.22, y - 0.2, y + 0.2, Z3, base + 0.04, MT["stone"], g)
    lobed("Keystone", [(0.16, 0.0), (0.2, 0.12), (0.0, 0.32)], 6, MT["rmetal"], g,
          loc=(ax + 0.12, cy, base + rise + 0.08), lobes=3, amp=0.3)
    # a glyph slab leaning on the right of the bottom, glowing inlay lines
    slab = empty("GlyphSlab", P(R2[1] - 0.25, 0.5, Z3), g)
    slab.rotation_euler = (0, math.radians(-14), math.radians(-90))
    bx("Slab", (1.0, 0.16, 1.15), (0, 0, 0.57), MT["stone"], slab)
    for k in range(4):
        z = 0.3 + k * 0.22
        w = 0.7 - 0.12 * (k % 2)
        bx(f"Glyph{k}", (w, 0.02, 0.04), (0.04 * (k % 3) - 0.04, -0.085, z), MT["glow"], slab).visible_shadow = False
        bx(f"GlyphTick{k}", (0.05, 0.02, 0.12), (w / 2 - 0.05, -0.085, z + 0.05), MT["glow"], slab).visible_shadow = False
    # the defence pylon: a three-lobed twisted stub with metal bands and a glowing slit
    px, py = -0.35, 0.15
    lobed("PylonFoot", [(0.62, 0.0), (0.55, 0.18), (0.4, 0.26)], 12, MT["rmetal"], g, loc=(px, py, Z3), lobes=3, amp=0.22)
    lobed("Pylon", [(0.4, 0.0), (0.33, 0.7), (0.2, 1.35), (0.05, 1.62)], 12, MT["stone"], g, loc=(px, py, Z3 + 0.26),
          lobes=3, amp=0.3, twist=40)
    for k, z in enumerate((0.45, 0.95)):
        K.band(f"PylonBand{k}", 0.37 - 0.13 * k, Z3 + 0.26 + z, Z3 + 0.26 + z + 0.07, MT["rmetal"], g, xy=(px, py), segs=12)
    for k in range(3):
        a = math.radians(60 + 120 * k)
        obox(f"PylonSlit{k}", (0.03, 0.05, 0.9), P(px + 0.36 * math.cos(a), py + 0.36 * math.sin(a), Z3 + 0.26 + 0.6), P(math.cos(a), math.sin(a), 0),
             MT["glow"], g).visible_shadow = False
    sphere("PylonEye", 0.07, P(px, py, Z3 + 0.26 + 1.62), MT["glow"], g, 8)
    K.point("SparkPoint", P(px, py, Z3 + 0.26 + 1.65), g, 0.25)
    K.point("Dust2", P(-1.6, -0.3, Z3 + 0.1), g, 0.3)


def grid(g):
    """Survey pegs and string lines over the dig, a baulk left standing, grid tags, buckets, trays and tools."""
    x0, x1, y0, y1 = R0
    xs = [x0 + (x1 - x0) * k / 4 for k in range(5)]
    ys = [y0 + (y1 - y0) * k / 3 for k in range(4)]
    zs = SURF + 0.22
    pegs = [(x, y0 - 0.12) for x in xs] + [(x, y1 + 0.12) for x in xs] + [(x0 - 0.12, y) for y in ys[1:-1]] + [(x1 + 0.12, y) for y in ys[1:-1]]
    for k, (x, y) in enumerate(pegs):
        bx(f"Peg{k}", (0.05, 0.05, 0.4), P(x, y, SURF + 0.12), MT["wood"], g)
        bx(f"PegTop{k}", (0.07, 0.07, 0.05), P(x, y, SURF + 0.33), MT["hazard"], g)
    for k, x in enumerate(xs[1:-1]):
        beam(f"StringX{k}", P(x, y0 - 0.12, zs), P(x, y1 + 0.12, zs), 0.012, MT["label"], g)
    for k, y in enumerate(ys[1:-1]):
        beam(f"StringY{k}", P(x0 - 0.12, y, zs), P(x1 + 0.12, y, zs), 0.012, MT["label"], g)
    # grid tags on the front pegs
    for k, x in enumerate(xs[:-1]):
        bx(f"Tag{k}", (0.22, 0.015, 0.14), P(x + 0.2, y0 - 0.15, SURF + 0.25), MT["label"], g)
    # the baulk: an unexcavated soil rib across the front of the bottom
    box("Baulk", -2.02, -1.68, R2[2], -1.15, Z3, Z2 - 0.04, MT["soil2"], g)
    box("BaulkTop", -2.05, -1.65, R2[2] - 0.02, -1.12, Z2 - 0.06, Z2 - 0.02, MT["soil1"], g)
    # buckets, finds trays and trowels on the terraces and the bottom
    for k, (x, y, z) in enumerate([(-3.15, -1.5, Z3), (0.55, 1.3, Z3), (1.05, -2.0, Z2), (-3.7, 1.8, Z1), (1.55, 2.3, Z1)]):
        zcyl(f"Bucket{k}", 0.15, z, z + 0.3, MT["bucket"], g, xy=(x, y), segs=10, radius2=0.18)
        K.hoop(f"BucketRim{k}", 0.18, z + 0.3, 0.025, MT["trim"], g, xy=(x, y), segs=10)
    for k, (x, y, z, yaw) in enumerate([(-0.2, 1.6, Z2, 0), (-2.9, -2.1, Z2, 90), (1.0, 0.3, Z2, 90), (-3.65, -0.3, Z1, 90)]):
        t = empty(f"Tray{k}", P(x, y, z), g)
        t.rotation_euler.z = math.radians(yaw)
        bx(f"TrayBox{k}", (0.6, 0.4, 0.08), (0, 0, 0.04), MT["crate_b" if k % 2 else "crate_a"], t)
        for j in range(3):
            K.rock(f"TrayFind{k}{j}", 0.06, (-0.18 + 0.18 * j, 0.02 * (j - 1), 0.1), t, seed=k * 3 + j, material=MT["stone"])
    for k, (x, y, z, yaw) in enumerate([(-2.3, -1.3, Z3 + 0.02, 30), (-0.9, 0.85, Z3 + 0.02, -50), (1.15, -1.0, Z2 + 0.02, 10)]):
        bx(f"Trowel{k}", (0.24, 0.07, 0.02), P(x, y, z), MT["steel"], g, rot=(0, 0, math.radians(yaw)))
    # kneeling mats / a tarp on the back terrace
    box("TarpBack", -3.1, -0.2, 1.75, 2.05, Z2 + 0.005, Z2 + 0.02, MT["tarp"], g)
    box("KneelMat", -2.55, -2.15, -1.45, -1.05, Z3 + 0.005, Z3 + 0.03, MT["tarp"], g)


def scaffold(g):
    """A tube scaffold tower in the front-left corner of the pit up to a deck at the rim, ladders down the terraces,
    a rope barrier along the front edge."""
    x0, x1, y0, y1 = R2[0] + 0.08, R2[0] + 1.0, R2[2] + 0.08, R2[2] + 0.95
    top = SURF + 1.05
    for x in (x0, x1):
        for y in (y0, y1):
            zcyl(f"ScafTube{x:.1f}{y:.1f}", 0.035, Z3, top, MT["steel"], g, xy=(x, y), segs=6)
            box(f"ScafFoot{x:.1f}{y:.1f}", x - 0.1, x + 0.1, y - 0.1, y + 0.1, Z3, Z3 + 0.03, MT["wood"], g)
    for z in (Z3 + 0.5, SURF - 0.04, top):
        for (a, b) in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
            beam(f"ScafLedger{z:.2f}{a[0]:.1f}{a[1]:.1f}", P(a[0], a[1], z), P(b[0], b[1], z), 0.05, MT["steel"], g)
    for k, ((a, b), (za, zb)) in enumerate([(((x0, y0), (x1, y0)), (Z3 + 0.5, SURF - 0.04)), (((x1, y0), (x1, y1)), (SURF - 0.04, Z3 + 0.5)),
                                            (((x0, y1), (x0, y0)), (Z3 + 0.5, SURF - 0.04))]):
        beam(f"ScafBrace{k}", P(a[0], a[1], za), P(b[0], b[1], zb), 0.04, MT["hazard"], g)
    # the deck at the rim with a plank bridge to the mesa, toe boards and a hand rail
    box("ScafDeck", x0 - 0.05, x1 + 0.05, y0 - 0.05, y1 + 0.05, SURF - 0.02, SURF + 0.03, MT["wood"], g)
    box("ScafPlank", R0[0] - 0.15, x0 + 0.1, y0 + 0.25, y0 + 0.65, SURF + 0.0, SURF + 0.05, MT["wood"], g)
    K.railing("ScafRail", [(x0, y1), (x1, y1), (x1, y0)], SURF + 0.03, g, h=0.95, post=0.9)
    ladder("ScafLadder", P(x1 - 0.25, y1 + 0.05, Z3), SURF, (0, 1), g, cage=False)
    # ladders down the terraces (leaning a little off the wall)
    for k, (base, top_z, facing) in enumerate([(P(0.35, R1[3] - 0.06, Z2), SURF + 0.3, (0, -1)), (P(R2[1] + 0.02, -0.9, Z3), Z1 + 0.4, (-1, 0)),
                                                (P(-3.2, R2[3] + 0.02, Z3), Z2 + 0.45, (0, -1))]):
        ladder(f"PitLadder{k}", base, top_z, facing, g, cage=False, width=0.38)
    # rope barrier along the front rim
    x_a, x_b = R0[0] + 0.4, R0[1] - 0.3
    y = R0[2] - 0.35
    n = 6
    for k in range(n + 1):
        x = x_a + (x_b - x_a) * k / n
        zcyl(f"BarrierPost{k}", 0.03, SURF, SURF + 0.85, MT["hazard"], g, xy=(x, y), segs=6)
        if k:
            xp = x_a + (x_b - x_a) * (k - 1) / n
            K.sagging(f"BarrierRope{k}", P(xp, y, SURF + 0.8), P(x, y, SURF + 0.8), 0.07, 0.012, MT["hazard"], g, n=3, segs=4)


def crane(g):
    """The gantry crane over the pit: rails on sleepers along both long sides, the gantry travelling front-back."""
    for k, x in enumerate(RAIL_X):
        box(f"Rail{k}", x - 0.05, x + 0.05, RAIL_Y[0], RAIL_Y[1], SURF + 0.08, SURF + 0.16, MT["steel"], g)
        for j in range(8):
            y = RAIL_Y[0] + 0.2 + (RAIL_Y[1] - RAIL_Y[0] - 0.4) * j / 7
            box(f"Sleeper{k}{j}", x - 0.3, x + 0.3, y - 0.1, y + 0.1, SURF - 0.02, SURF + 0.08, MT["wood"], g)
        for j, y in enumerate(RAIL_Y):
            box(f"RailStop{k}{j}", x - 0.14, x + 0.14, y - 0.08, y + 0.08, SURF + 0.08, SURF + 0.34, MT["hazard"], g)
    K.point("CraneMin", P(PCX, CRANE_Y[0], SURF), g)
    K.point("CraneMax", P(PCX, CRANE_Y[1], SURF), g)
    cr = empty("Crane", P(PCX, CRANE_REST, SURF), g)
    zb = BRIDGE_Z - SURF
    for k, x in enumerate((-SPAN, SPAN)):
        s = -1 if k == 0 else 1
        bx(f"Bogie{k}", (0.26, 1.5, 0.22), (x, 0, 0.27), MT["accent"], cr)
        for j in (-1, 1):
            K.cylinder(f"Wheel{k}{j}", 0.11, 0.08, (x, j * 0.55, 0.27), MT["dark"], cr, axis="X", segments=10)
        for j in (-1, 1):
            beam(f"Leg{k}{j}", P(x, j * 0.62, 0.36), P(x - s * 0.05, j * 0.12, zb - 0.1), 0.13, MT["hull"], cr)
        beam(f"LegTie{k}", P(x, -0.42, 1.2), P(x, 0.42, 1.2), 0.07, MT["trim"], cr)
        bx(f"LegOwner{k}", (0.16, 0.42, 0.14), (x - s * 0.03, 0, zb - 0.32), MT["owner"], cr)
    # the bridge girder (yellow box girder with a top rail and stiffeners), the hoist under its middle
    x0, x1 = -SPAN - 0.1, SPAN + 0.1
    obox("Girder", (0.36, x1 - x0, 0.42), P(0, 0, zb + 0.12), P(1, 0, 0), MT["hazard"], cr)
    obox("GirderTop", (0.4, x1 - x0 + 0.04, 0.05), P(0, 0, zb + 0.35), P(1, 0, 0), MT["trim"], cr)
    for k in range(7):
        x = x0 + 0.4 + (x1 - x0 - 0.8) * k / 6
        bx(f"GirderStiff{k}", (0.05, 0.38, 0.36), (x, 0, zb + 0.12), MT["dark"], cr)
    bx("Hoist", (0.62, 0.55, 0.4), (0, 0, zb - 0.22), MT["accent"], cr)
    bx("HoistOwner", (0.64, 0.57, 0.08), (0, 0, zb - 0.1), MT["owner"], cr)
    K.cylinder("HoistDrum", 0.13, 0.5, (0.42, 0.0, zb - 0.22), MT["steel"], cr, axis="Y", segments=10)
    bx("HoistLamp", (0.16, 0.3, 0.06), (-0.36, 0.0, zb - 0.38), MT["dark"], cr)
    bx("HoistLens", (0.12, 0.24, 0.02), (-0.36, 0.0, zb - 0.42), MT["lamp"], cr).visible_shadow = False
    sphere("CraneBeacon", 0.07, P(SPAN - 0.05, 0, zb + 0.44), MT["beacon"], cr, 8)
    # ropes (scaled to the hook) and the skip bucket
    rope_top = zb - 0.42
    cab = empty("Cable", P(0, 0, rope_top), cr)
    for sx in (-1, 1):
        K.strut(f"Rope{sx}", P(sx * 0.1, 0, 0), P(sx * 0.1, 0, -1.0), 0.018, MT["cable"], cab, 5)
    rest = HOOK_UP - SURF
    cab.scale = (1, 1, rope_top - rest - 0.42)
    hook = empty("Hook", P(0, 0, rest), cr)
    K.point("HookLow", P(0, 0, HOOK_LOW - SURF), cr)
    bx("HookBlock", (0.3, 0.16, 0.22), (0, 0, 0.32), MT["hazard"], hook)
    beam("Bail", P(-0.32, 0, 0.0), P(0.0, 0, 0.26), 0.03, MT["steel"], hook)
    beam("Bail2", P(0.32, 0, 0.0), P(0.0, 0, 0.26), 0.03, MT["steel"], hook)
    lobed("Skip", [(0.22, -0.5), (0.3, -0.1), (0.32, 0.0)], 10, MT["bucket"], hook, cap=False)
    zcyl("SkipBottom", 0.22, -0.52, -0.5, MT["trim"], hook, segs=10)
    K.heap("SkipLoad", 0.27, 0.12, P(0, 0, -0.06), MT["soil"], hook, seed=4, segs=8)
    K.point("DigLight", P(PCX, PCY, SURF + 2.6), g, 0.4)


def sieves(g):
    """Two sifting screens on A-frames behind the pit, spoil heaps under them, a wheelbarrow."""
    for k, (x, y) in enumerate(SIEVES):
        zt = SURF + 1.75
        for s in (-1, 1):
            a = P(x + s * 0.85, y - 0.42, SURF)
            b = P(x + s * 0.85, y + 0.42, SURF)
            t = P(x + s * 0.85, y, zt)
            beam(f"Sieve{k}Leg{s}a", a, t, 0.07, MT["wood"], g)
            beam(f"Sieve{k}Leg{s}b", b, t, 0.07, MT["wood"], g)
        beam(f"Sieve{k}Bar", P(x - 0.9, y, zt - 0.02), P(x + 0.9, y, zt - 0.02), 0.08, MT["wood"], g)
        K.heap(f"Spoil{k}", 0.85, 0.55, P(x + 0.1, y + 0.15, SURF - 0.05), MT["soil"], g, seed=11 + k)
        piv = empty(f"Sieve{k}", P(x, y, SURF + 0.85), g)
        piv.rotation_euler.x = math.radians(-12)
        w, d = 1.25, 0.8
        for s in (-1, 1):
            bx(f"Sieve{k}Side{s}", (w, 0.06, 0.16), (0, s * d / 2, 0), MT["wood"], piv)
            bx(f"Sieve{k}End{s}", (0.06, d, 0.16), (s * w / 2, 0, 0), MT["wood"], piv)
        bx(f"Sieve{k}Screen", (w - 0.04, d - 0.04, 0.012), (0, 0, -0.04), MT["grate"], piv)
        K.rock(f"Sieve{k}Clod", 0.12, (0.15, 0.05, 0.02), piv, seed=k + 20, material=MT["soil"])
        for s in (-1, 1):
            K.strut(f"Sieve{k}Chain{s}", P(s * (w / 2 - 0.1), 0, 0.08), P(s * (w / 2 - 0.1), 0.0, zt - SURF - 0.85 - 0.05), 0.012, MT["steel"], piv, 4)
        K.point(f"Dust{k}", P(x, y + 0.05, SURF + 0.55), g, 0.3)
    opt = empty("OptBarrow", P(-3.3, -4.35, SURF), g)
    opt.rotation_euler.z = math.radians(-20)
    lobed("BarrowTub", [(0.28, 0.35), (0.45, 0.62)], 8, MT["accent"], opt, cap=False).scale = (1.4, 1.0, 1.0)
    zcyl("BarrowFloor", 0.28, 0.33, 0.35, MT["accent"], opt, segs=8).scale = (1.4, 1, 1)
    K.cylinder("BarrowWheel", 0.18, 0.08, (0.62, 0, 0.18), MT["rubber"], opt, axis="Y", segments=10)
    for s in (-1, 1):
        beam(f"BarrowHandle{s}", P(0.55, s * 0.2, 0.3), P(-0.85, s * 0.28, 0.6), 0.04, MT["trim"], opt)
        beam(f"BarrowLeg{s}", P(-0.3, s * 0.22, 0.35), P(-0.35, s * 0.24, 0.0), 0.04, MT["trim"], opt)
    K.heap("BarrowLoad", 0.32, 0.14, P(0, 0, 0.5), MT["soil"], opt, seed=9, segs=8).scale = (1.3, 1, 1)


def hut(g):
    """The survey hut: a prefab cabin with the Status board, windows, a door, an Owner band, a canvas awning, the
    beacon mast and a dish on the roof."""
    x0, x1, y0, y1 = HUT
    z0 = SURF + 0.15
    zt = z0 + HUT_H
    box("HutSkid", x0 - 0.05, x1 + 0.05, y0 - 0.05, y1 + 0.05, SURF - 0.05, z0, MT["trim"], g)
    box("HutBody", x0, x1, y0, y1, z0, zt, MT["hull"], g)
    box("HutAccent", x0 - 0.01, x1 + 0.01, y0 - 0.01, y1 + 0.01, z0, z0 + 0.32, MT["accent"], g)
    box("HutOwner", x0 - 0.01, x1 + 0.01, y0 - 0.01, y1 + 0.01, zt - 0.28, zt - 0.14, MT["owner"], g)
    for k in range(4):
        x = x0 + 0.35 + k * (x1 - x0 - 0.7) / 3
        box(f"HutRib{k}", x - 0.04, x + 0.04, y0 - 0.04, y0, z0 + 0.32, zt - 0.28, MT["panel"], g)
    # a low barrel roof (the field-prefab look), its ends closed
    segs = 6
    for k in range(segs):
        a0, a1 = math.pi * k / segs, math.pi * (k + 1) / segs
        ya, za = (y0 + y1) / 2 - math.cos(a0) * ((y1 - y0) / 2 + 0.12), zt + math.sin(a0) * 0.45
        yb, zb = (y0 + y1) / 2 - math.cos(a1) * ((y1 - y0) / 2 + 0.12), zt + math.sin(a1) * 0.45
        K.faces_obj(f"HutRoof{k}", [(P(x1 + 0.12, ya, za), P(x1 + 0.12, yb, zb), P(x0 - 0.12, yb, zb), P(x0 - 0.12, ya, za))],
                    MT["panel"], g, shadow=True)
    for k, x in enumerate((x0 - 0.12, x1 + 0.12)):
        pts = [P(x, (y0 + y1) / 2 - math.cos(math.pi * j / segs) * ((y1 - y0) / 2 + 0.12), zt + math.sin(math.pi * j / segs) * 0.45) for j in range(segs + 1)]
        K.faces_obj(f"HutGable{k}", [tuple(pts[::-1] if k else pts)], MT["trim"], g, shadow=True)
    # front: door, windows, the status board
    box("HutDoor", x0 + 0.3, x0 + 1.15, y0 - 0.03, y0, z0 + 0.05, z0 + 2.0, MT["trim"], g)
    box("HutDoorLamp", x0 + 0.5, x0 + 0.95, y0 - 0.06, y0, z0 + 2.08, z0 + 2.15, MT["window"], g).visible_shadow = False
    K.pane_row("HutWinF", (x0 + 1.4, y0 - 0.015, 0), (1, 0, 0), None, x1 - x0 - 1.55, z0 + 1.3, z0 + 1.95, 2, MT["window"], g, frac=0.8)
    box("StatusFrame", x0 + 1.45, x1 - 0.15, y0 - 0.06, y0, z0 + 0.55, z0 + 1.1, MT["dark"], g)
    box("StatusBoard", x0 + 1.53, x1 - 0.23, y0 - 0.085, y0 - 0.06, z0 + 0.78, z0 + 0.98, MT["status"], g).visible_shadow = False
    for j in range(4):
        xx = x0 + 1.65 + j * (x1 - x0 - 1.95) / 3
        box(f"StatusLed{j}", xx - 0.06, xx + 0.06, y0 - 0.085, y0 - 0.06, z0 + 0.62, z0 + 0.71, MT["status"], g).visible_shadow = False
    K.marker("LabelHut", P((x0 + 1.45 + x1 - 0.15) / 2, y0 - 0.065, z0 + 2.3), (math.radians(90), 0, 0), 1.0, 0.18, g)
    K.pane_row("HutWinR", (x1 + 0.012, y0 + 0.4, 0), (0, 1, 0), None, y1 - y0 - 0.8, z0 + 1.3, z0 + 1.95, 2, MT["window"], g, frac=0.8)
    K.pane_row("HutWinL", (x0 - 0.012, y1 - 0.4, 0), (0, -1, 0), None, y1 - y0 - 0.8, z0 + 1.3, z0 + 1.95, 2, MT["window"], g, frac=0.8)
    box("HutStep", x0 + 0.25, x0 + 1.2, y0 - 0.4, y0 - 0.05, SURF - 0.02, z0 - 0.02, MT["grate"], g)
    # roof: the beacon mast, a small dish, an antenna
    zcyl("HutMast", 0.05, zt + 0.3, zt + 2.1, MT["steel"], g, xy=(x1 - 0.35, y1 - 0.35), segs=6)
    sphere("Beacon", 0.1, P(x1 - 0.35, y1 - 0.35, zt + 2.18), MT["beacon"], g, 10)
    zcyl("HutDishPost", 0.04, zt + 0.35, zt + 0.85, MT["trim"], g, xy=(x0 + 0.6, y1 - 0.45), segs=6)
    lobed("HutDish", [(0.0, 0.0), (0.22, 0.04), (0.38, 0.14)], 12, MT["label"], g, loc=(x0 + 0.6, y1 - 0.45, zt + 0.85), cap=False,
          rot=(math.radians(-50), 0, math.radians(20)))
    zcyl("HutWhip", 0.015, zt + 0.4, zt + 1.6, MT["steel"], g, xy=(x0 + 0.25, y1 - 0.2), segs=4)
    K.point("RoofSpot0", P(x0 + 1.4, (y0 + y1) / 2 + 0.2, zt + 0.42), g)
    # the canvas awning in front (optional)
    opt = empty("OptAwning", P(0, 0, 0), g)
    ya, yb = y0 - 0.02, y0 - 1.25
    for k, x in enumerate((x0 + 1.3, x1 - 0.05)):
        zcyl(f"AwningPole{k}", 0.03, SURF, SURF + 1.95, MT["steel"], opt, xy=(x, yb + 0.05), segs=6)
        K.strut(f"AwningGuy{k}", P(x, yb + 0.05, SURF + 1.95), P(x, yb - 0.6, SURF), 0.008, MT["cable"], opt, 3)
    K.faces_obj("Awning", [(P(x0 + 1.25, yb, SURF + 2.0), P(x1, yb, SURF + 2.0), P(x1, ya, zt - 0.1), P(x0 + 1.25, ya, zt - 0.1))],
                MT["canvas"], opt, shadow=True)
    K.faces_obj("AwningValance", [(P(x0 + 1.25, yb, SURF + 1.82), P(x1, yb, SURF + 1.82), P(x1, yb, SURF + 2.0), P(x0 + 1.25, yb, SURF + 2.0))],
                MT["owner"], opt, shadow=True)
    # a camp table and stools under it
    box("CampTable", x0 + 1.6, x1 - 0.4, yb + 0.2, y0 - 0.3, SURF + 0.7, SURF + 0.75, MT["label"], opt)
    for sx in (x0 + 1.7, x1 - 0.5):
        beam(f"CampLeg{sx:.1f}", P(sx, yb + 0.25, SURF), P(sx, y0 - 0.35, SURF + 0.7), 0.03, MT["trim"], opt)
        beam(f"CampLegB{sx:.1f}", P(sx, y0 - 0.35, SURF), P(sx, yb + 0.25, SURF + 0.7), 0.03, MT["trim"], opt)
    bx("CampScreen", (0.4, 0.03, 0.28), P(x0 + 2.4, y0 - 0.45, SURF + 0.9), MT["dark"], opt, rot=(math.radians(-15), 0, 0))
    bx("CampScreenLit", (0.34, 0.01, 0.22), P(x0 + 2.4, y0 - 0.47, SURF + 0.9), MT["window"], opt, rot=(math.radians(-15), 0, 0)).visible_shadow = False


def finds(g):
    """The finds table (trestles, trays of finds, a glowing artifact) and a stack of labelled crates."""
    x0, x1, y0, y1 = TABLE
    zt = SURF + 0.85
    box("FindsTop", x0, x1, y0, y1, zt - 0.05, zt, MT["wood"], g)
    for x in (x0 + 0.2, x1 - 0.2):
        for s in (-1, 1):
            beam(f"Trestle{x:.1f}{s}", P(x - 0.25 * s, y0 + 0.1, SURF), P(x + 0.25 * s, y1 - 0.1, zt - 0.05), 0.05, MT["trim"], g)
    for k in range(3):
        cx = x0 + 0.38 + k * 0.68
        bx(f"FindsTray{k}", (0.55, 0.5, 0.06), P(cx, (y0 + y1) / 2, zt + 0.03), MT["label"], g)
        for j in range(3):
            K.rock(f"Find{k}{j}", 0.07, (cx - 0.15 + 0.15 * j, (y0 + y1) / 2 + 0.08 * (j % 2), zt + 0.08), g, seed=40 + k * 3 + j,
                   material=MT["stone"] if (k + j) % 3 else MT["rmetal"])
    lobed("Artifact", [(0.0, 0.0), (0.08, 0.06), (0.05, 0.22), (0.0, 0.28)], 6, MT["rmetal"], g, loc=(x1 - 0.25, y0 + 0.2, zt), lobes=3, amp=0.3, twist=60)
    sphere("ArtifactGlow", 0.035, P(x1 - 0.25, y0 + 0.2, zt + 0.17), MT["glow"], g, 6)
    bx("Scanner", (0.22, 0.14, 0.05), P(x0 + 0.3, y0 + 0.2, zt + 0.025), MT["dark"], g, rot=(0, 0, math.radians(15)))
    bx("ScannerLit", (0.16, 0.09, 0.01), P(x0 + 0.3, y0 + 0.2, zt + 0.055), MT["window"], g, rot=(0, 0, math.radians(15))).visible_shadow = False
    # crates beside it, the front one labelled
    cx, cy = 5.25, -2.25
    for k, (dx, dy, dz, m) in enumerate([(0, -0.35, 0, "crate_a"), (0, 0.35, 0, "crate_b"), (0, -0.35, 0.55, "crate_b"), (-0.05, 0.38, 0.55, "crate_a")]):
        o = bx(f"FindCrate{k}", (0.62, 0.62, 0.52), P(cx + dx, cy + dy, SURF + dz + 0.26), MT[m], g)
        o.rotation_euler.z = math.radians((k * 7) % 9 - 4)
        bx(f"FindCrateLid{k}", (0.64, 0.64, 0.05), P(cx + dx, cy + dy, SURF + dz + 0.5), MT["trim"], g)
    bx("FindsPlate", (0.5, 0.012, 0.18), P(cx, cy - 0.35 - 0.315, SURF + 0.28), MT["label"], g)
    K.marker("LabelFinds", P(cx, cy - 0.35 - 0.322, SURF + 0.28), (math.radians(90), 0, 0), 0.46, 0.13, g)


def utilities(g):
    """Generator skid with cables to the hut and the masts, a water tank, the dewatering pump and its hose."""
    opt = empty("OptGenerator", P(5.05, -0.4, SURF), g)
    bx("GenSkid", (1.1, 0.7, 0.1), (0, 0, 0.05), MT["trim"], opt)
    bx("GenBody", (0.95, 0.6, 0.62), (0, 0, 0.41), MT["accent"], opt)
    bx("GenOwner", (0.97, 0.62, 0.08), (0, 0, 0.62), MT["owner"], opt)
    bx("GenGrille", (0.02, 0.45, 0.36), (0.48, 0, 0.4), MT["grate"], opt)
    zcyl("GenStack", 0.04, 0.72, 1.0, MT["steel"], opt, xy=(-0.3, 0.15), segs=6)
    K.sagging("GenCableHut", P(4.6, -0.2, SURF + 0.3), P(4.2, HUT[2], SURF + 0.6), 0.05, 0.02, MT["cable"], g, n=4, segs=4)
    # water tank on a stand, a pump in the pit with its hose up over the rim
    zcyl("TankStand", 0.42, SURF, SURF + 0.35, MT["trim"], g, xy=(-5.55, -1.6), segs=10)
    zcyl("Tank", 0.4, SURF + 0.35, SURF + 1.25, MT["label"], g, xy=(-5.55, -1.6), segs=14)
    K.band("TankBand", 0.405, SURF + 0.75, SURF + 0.85, MT["accent"], g, xy=(-5.55, -1.6), segs=14)
    zcyl("Pump", 0.14, Z3, Z3 + 0.32, MT["hazard"], g, xy=(0.55, -1.55), segs=8)
    pipe("Hose", [P(0.55, -1.55, Z3 + 0.3), P(0.9, -2.1, Z2 + 0.15), P(1.5, -2.5, Z1 + 0.1), P(2.05, -3.0, SURF + 0.1), P(2.8, -3.6, SURF + 0.05)],
         0.04, MT["rubber"], g, segs=6)


def sign(g):
    """The entrance sign: a low board at the head of the steps (kept low so it doesn't hide the dig)."""
    x, y = -1.0, -5.35
    for k, dx in enumerate((-1.2, 1.2)):
        zcyl(f"SignPost{k}", 0.05, SURF - 0.1, SURF + 0.95, MT["trim"], g, xy=(x + dx, y), segs=8)
    box("SignBoard", x - 1.4, x + 1.4, y - 0.06, y, SURF + 0.35, SURF + 0.9, MT["dark"], g)
    box("SignOwner", x - 1.41, x + 1.41, y - 0.07, y + 0.01, SURF + 0.27, SURF + 0.35, MT["owner"], g)
    box("SignCap", x - 1.45, x + 1.45, y - 0.1, y + 0.04, SURF + 0.9, SURF + 0.96, MT["trim"], g)
    K.marker("SignFront", P(x, y - 0.065, SURF + 0.62), (math.radians(90), 0, 0), 2.5, 0.3, g)
    K.point("RoofSpot1", P(-5.55, -1.6, SURF + 1.25), g)


def lights(g):
    piv = empty("Masts", P(0, 0, SURF), g)
    K.light_mast("MastA", 5.6, 0.75, 0.0, 5.2, 170, piv)
    K.light_mast("MastB", -5.25, 2.6, 0.0, 5.0, -30, piv)
    # little lamp heads on the scaffold and the crane rails
    for k, (x, y, z) in enumerate([(R2[0] + 1.0, R2[2] + 0.95, SURF + 1.05), (RAIL_X[1] + 0.3, RAIL_Y[0], SURF + 0.4), (RAIL_X[0] - 0.3, RAIL_Y[1], SURF + 0.4)]):
        bx(f"WorkLamp{k}", (0.22, 0.18, 0.14), P(x, y, z + 0.07), MT["dark"], g)
        bx(f"WorkLampLens{k}", (0.18, 0.02, 0.1), P(x, y - 0.1, z + 0.07), MT["lamp"], g).visible_shadow = False


BUILDERS = [ground, ruin, grid, scaffold, crane, sieves, hut, finds, utilities, sign, lights]

PIVOTS = {"Crane", "Hook", "Cable", "Sieve0", "Sieve1", "Masts", "OptAwning", "OptGenerator", "OptBarrow", "GlyphSlab"}
SEPARATE = {"Beacon"}
EMPTIES = {"CraneMin", "CraneMax", "HookLow", "SparkPoint", "Dust0", "Dust1", "Dust2", "DigLight", "SignFront",
           "LabelFinds", "LabelHut", "RoofSpot0", "RoofSpot1"} | {f"AddOnSpot{k}" for k in range(5)}
KEEP = PIVOTS | SEPARATE | EMPTIES | {f"Build{k}" for k in range(len(PIECES))}

SIGNS = {"SignFront": ("EXCAVATION 01", (0.92, 0.93, 0.95)), "LabelFinds": ("FINDS", (0.08, 0.08, 0.1)),
         "LabelHut": ("SURVEY", (0.08, 0.08, 0.1))}


def on_shot(name):
    """Preview pose (after the export): the crane over the dig with the skip part way down, a sieve tipped."""
    D = K.bpy.data.objects
    D["Crane"].location.y = 0.55
    drop = 0.55
    D["Hook"].location.z = (HOOK_UP - SURF) + ((HOOK_LOW - SURF) - (HOOK_UP - SURF)) * drop
    rope_top = BRIDGE_Z - SURF - 0.42
    D["Cable"].scale.z = rope_top - D["Hook"].location.z - 0.42
    D["Sieve0"].location.x += 0.05


def main():
    materials()
    root = K.assemble("excavation_site", PIECES, BUILDERS)
    night = [("NPit", (1.0, 0.92, 0.78), 260, P(PCX, PCY, SURF + 3.0)),
             ("NMastA", (1.0, 0.95, 0.85), 220, P(5.0, 0.65, SURF + 5.0)),
             ("NMastB", (1.0, 0.95, 0.85), 200, P(-4.7, 2.2, SURF + 4.8)),
             ("NHut", (1.0, 0.85, 0.6), 60, P(3.7, 0.9, SURF + 2.2)),
             ("NGlow", (0.4, 1.0, 0.9), 12, P(-0.35, -0.2, Z3 + 1.4))]
    shots = {"hero": ((15.0, -18.5, 13.0), (-0.6, 0.3, 1.2), 32),
             "close": ((0.8, -9.6, 6.6), (-1.4, 0.2, 0.4), 26),
             "night": ((-9.5, -18.0, 12.0), (-0.6, 0.3, 1.2), 32),
             "top": ((0, -0.01, 80), (0, 0, 0), "ortho", 16),
             "back": ((-14.0, 17.0, 10.0), (0.0, 0.5, 1.5), 32)}
    K.finish(root, KEEP, SEPARATE, "ExcavationGrime", SURF - 0.25, soot=None, shots=shots, night_lights=night,
             signs=SIGNS, rover_at=(-9.4, -6.8, 150), on_shot=on_shot)


main()
