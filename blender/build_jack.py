"""Jack, the Pumpkin King raider (owner 2026-11-11: a Halloween joke for Craig, "make an alien that attacks bases that
is Jack Skellington"; contract docs/jack-contract.md "Models"). Three glbs in game/models/creatures/:

  jack.glb     the giant skeleton, 16.0 tall, feet at the origin: very tall and thin, spidery limbs, an oversized
               smooth white egg skull with big black oval eye sockets (tops leaning out), two small nose slits and a
               long wide grin carved across the face with black stitches crossing it; black tailcoat suit with thin
               white vertical pinstripes (a 64 x 4 PNG stripe tile repeated by the UVs, no bake), long split coat
               tails, black lapels round a white shirt V, the black bat-shaped bow tie at the neck, bony white hands
               with long thin three-jointed fingers, pointed black shoes curling up at the toe, a jack-o'-lantern held
               in the right hand (hidden by the game except when throwing).
  pumpkin.glb  the projectile: a ribbed orange jack-o'-lantern 1.6 across, origin at its centre, carved triangle
               eyes and nose and a toothy grin (the cuts are "PumpkinGlow"), a curled dark green stalk.
  bat.glb      a small bat, 1.4 wingspan: Body (origin at its centre: body, head, ears, red eyes) with WingL / WingR
               (origins at the shoulders, rest = spread flat) of arm bone, four finger ribs and a scalloped membrane.

PARTS of jack.glb (names and hierarchy exact; every pivot is ONE mesh whose origin is its joint and whose rest
rotation is identity, so the game animates by setting each node's Basis, identity = this rest pose):
  Jack (empty at the feet) -> Hips (hip joint: pelvis/trousers + coat tails)
    Hips -> Torso (waist: jacket, lapels, shirt V, buttons, bow tie, shoulders)
      Torso -> Head (neck base: neck bone + skull)
      Torso -> ArmUpperL -> ArmLowerL -> HandL  (Jack's left = Blender -X; rest = hanging down, 13 deg out)
      Torso -> ArmUpperR -> ArmLowerR -> HandR -> Pumpkin (centre of the pumpkin, on the palm side of the hand)
    Hips -> LegUpperL -> LegLowerL -> FootL     (hip, knee, ankle; rest = straight down)
    Hips -> LegUpperR -> LegLowerR -> FootR
  Materials: Bone (skull, neck, hands, shirt), Suit (pinstripe texture), Black (sockets, mouth, stitches, bow tie,
  lapels, shoes, buttons), Pumpkin, PumpkinGlow (emissive orange, strength 1 so Godot keeps the emission on),
  Stalk. bat.glb: Black (body), BatWing (dark purple membrane), BatEye (red, emissive strength 1).

Units and axes: 1 Blender unit = 1 game world unit (hexes 25 wide, buildings 10-12, so Jack is a giant). Blender +Y =
front (Godot -Z), +Z = up (Godot +Y); the exporter maps Blender (x, y, z) to Godot (x, z, -y). Turning a pivot about
Blender +X by a positive angle swings a hanging limb forward (Godot: Basis(Vector3.Right, -a) ... note the sign flip
of the Y/Z swap: Blender X stays Godot X, so the same +a about X in Godot swings it backward toward +Z).

Run (bpy 5.x, see CLAUDE.md "Blender here"):
  /home/user/.bpyvenv/bin/python blender/build_jack.py --game
      the three glbs (~5 s), then re-imports each in a clean scene and checks every contract node, its parent,
      identity rest rotations and Jack's height/feet; prints node names, triangles, sizes and pivots in Godot coords.
  /home/user/.bpyvenv/bin/python blender/build_jack.py --preview blender/previews/jack --samples 40
      Cycles: jack-hero.png (3/4 front, holding a lit pumpkin), jack-game.png (game camera ~45 deg down beside a
      10-wide grey box standing in for a building, a pumpkin in flight, bats), jack-glb.png (the exported jack.glb
      re-imported and posed mid-stride through its own pivots; needs --game first), jack-props.png (pumpkin.glb +
      bat.glb). ~3-5 min on 4 cores.
  python3 blender/build_jack.py --sheet blender/previews/jack      contact sheet jack-sheet.png (system python + PIL)
Flags: --shots hero,game,glb,props  --res 1200  --samples 40
"""
import math
import os
import sys
import time

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
HERE = os.path.dirname(os.path.abspath(__file__))


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


# ---------------------------------------------------------------- contact sheet (plain python3 + PIL, no bpy)

if "--sheet" in argv:
    sys.path.insert(0, HERE)
    from glb_check import contact_sheet
    folder = arg("--sheet")
    contact_sheet(folder, [[("jack-hero.png", "hero - holding a lit pumpkin"), ("jack-game.png", "game camera, 10-wide box")],
                           [("jack-glb.png", "jack.glb posed through its pivots"), ("jack-props.png", "pumpkin.glb + bat.glb")]],
                  os.path.join(folder, "jack-sheet.png"), 720, 480)
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

REPO = os.path.dirname(HERE)
GAME_DIR = os.path.join(REPO, "game", "models", "creatures")
ALL_SHOTS = ["hero", "game", "glb", "props"]
SHOTS = [s.strip() for s in arg("--shots", ",".join(ALL_SHOTS)).split(",") if s.strip()]
PREVIEW = arg("--preview")
GAME = "--game" in argv
RES = int(arg("--res", "1200"))
SAMPLES = int(arg("--samples", "40"))
if set(SHOTS) - set(ALL_SHOTS):
    sys.exit(f"--shots takes {', '.join(ALL_SHOTS)}")
if not GAME and not PREVIEW:
    sys.exit("nothing to do: pass --game and/or --preview <dir> (or --sheet <dir> with system python)")

STRIPE = 0.24  # pinstripe spacing in world units

# joints (Blender world coordinates at rest)
HEIGHT = 16.0
ANKLE_Z, KNEE_Z, HIP_Z, HIP_X = 0.62, 3.95, 7.3, 0.46
WAIST_Z = 8.05
NECK_Z = 12.25
SHOULDER = (1.18, -0.02, 11.42)
ARM_OUT = math.radians(13.0)
UPPER_ARM, LOWER_ARM = 2.95, 2.85
HEAD_C = Vector((0.0, 0.04, 14.28))
HEAD_R = (1.5, 1.36, HEIGHT - 14.28)  # skull top exactly at HEIGHT

JACK_TREE = {  # node -> parent (None = scene root)
    "Jack": None, "Hips": "Jack", "Torso": "Hips", "Head": "Torso",
    "ArmUpperL": "Torso", "ArmLowerL": "ArmUpperL", "HandL": "ArmLowerL",
    "ArmUpperR": "Torso", "ArmLowerR": "ArmUpperR", "HandR": "ArmLowerR", "Pumpkin": "HandR",
    "LegUpperL": "Hips", "LegLowerL": "LegUpperL", "FootL": "LegLowerL",
    "LegUpperR": "Hips", "LegLowerR": "LegUpperR", "FootR": "LegLowerR",
}
PUMPKIN_TREE = {"Pumpkin": None}
BAT_TREE = {"Body": None, "WingL": "Body", "WingR": "Body"}
GLBS = {"jack.glb": JACK_TREE, "pumpkin.glb": PUMPKIN_TREE, "bat.glb": BAT_TREE}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def link(o, parent=None):
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
    return o


# ---------------------------------------------------------------- materials

def principled(name, colour, rough=0.5, metallic=0.0, emission=None, strength=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*colour, 1.0)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metallic
    if emission is not None:
        p.inputs["Emission Color"].default_value = (*emission, 1.0)
        p.inputs["Emission Strength"].default_value = strength
    return m


def pinstripe_material():
    """Black cloth with a thin off-white line every STRIPE: a 64 x 4 tile, the UVs count stripes (u = stripe index
    round each tube), so glTF's default REPEAT wrap draws them; vertical along every limb, converging on tapers."""
    w, h = 64, 4
    img = bpy.data.images.new("Pinstripe", w, h, alpha=False)
    px = []
    for _y in range(h):
        for x in range(w):
            line = x < 4
            soft = x in (4, 63)
            c = 0.74 if line else (0.3 if soft else 0.035)
            px += [c, c, c * 0.97, 1.0]
    img.pixels = px
    img.pack()
    m = bpy.data.materials.new("Suit")
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    p.inputs["Roughness"].default_value = 0.62
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Linear"
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    return m


def palette():
    return {
        "bone": principled("Bone", (0.93, 0.91, 0.86), 0.38),
        "suit": pinstripe_material(),
        "black": principled("Black", (0.012, 0.012, 0.014), 0.55),
        "pumpkin": principled("Pumpkin", (0.86, 0.3, 0.025), 0.42),
        "glow": principled("PumpkinGlow", (1.0, 0.5, 0.08), 0.5, emission=(1.0, 0.45, 0.06), strength=1.0),
        "stalk": principled("Stalk", (0.1, 0.17, 0.05), 0.75),
        "wing": principled("BatWing", (0.07, 0.03, 0.09), 0.62),
        "eye": principled("BatEye", (0.9, 0.08, 0.04), 0.4, emission=(1.0, 0.1, 0.05), strength=1.0),
    }


# ---------------------------------------------------------------- piece = one pivot's mesh, built in world coords

class Piece:
    def __init__(self, name, joint, parent=None):
        self.name = name
        self.joint = Vector(joint)
        self.parent = parent
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.mats = []

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)


def ellipse_perimeter(a, b):
    return math.pi * (3 * (a + b) - math.sqrt((3 * a + b) * (a + 3 * b)))


def tube(pc, pts, radii, mat, seg=16, side=(1, 0, 0), caps=(True, True), smooth=True, stripes=None):
    """Closed tube through pts; radii = r or (r_side, r_other) per point (r_side along `side` made perpendicular to the
    path). UV u = stripe index round the tube (a whole number of stripes, so the seam matches), v = length."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    radii = [(r, r) if isinstance(r, (int, float)) else r for r in radii]
    side = Vector(side)
    bm, uvl, mi = pc.bm, pc.uv, pc.mi(mat)
    if stripes is None:
        stripes = max(1, round(sum(ellipse_perimeter(*r) for r in radii) / n / STRIPE))
    rings, vs = [], [0.0]
    for i in range(n):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        u = side - t * side.dot(t)
        if u.length < 1e-6:
            u = t.orthogonal()
        u.normalize()
        w = t.cross(u)
        rx, ry = radii[i]
        rings.append([bm.verts.new(pts[i] + u * (rx * math.cos(2 * math.pi * k / seg)) +
                                   w * (ry * math.sin(2 * math.pi * k / seg))) for k in range(seg)])
        if i:
            vs.append(vs[-1] + (pts[i] - pts[i - 1]).length / STRIPE)
    faces = []
    for i in range(n - 1):
        for k in range(seg):
            k1 = (k + 1) % seg
            f = bm.faces.new((rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]))
            u0, u1 = stripes * k / seg, stripes * (k + 1) / seg
            for loop, uv in zip(f.loops, ((u0, vs[i]), (u1, vs[i]), (u1, vs[i + 1]), (u0, vs[i + 1]))):
                loop[uvl].uv = uv
            faces.append(f)
    for i, on in ((0, caps[0]), (n - 1, caps[1])):
        if not on:
            continue
        c = bm.verts.new(pts[i])
        for k in range(seg):
            f = bm.faces.new((c, rings[i][(k + 1) % seg], rings[i][k]))
            for loop in f.loops:
                loop[uvl].uv = (0.5, vs[i])
            faces.append(f)
    for f in faces:
        f.material_index = mi
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def ellipsoid_verts(c, radii, frame, seg, rings, fn=None):
    """Rows of points of an ellipsoid (poles first/last as single points); fn(lat, lon) -> radius factor."""
    rows = [[c - frame @ Vector((0, 0, radii[2]))]]
    for j in range(1, rings):
        lat = -math.pi / 2 + math.pi * j / rings
        row = []
        for k in range(seg):
            lon = 2 * math.pi * k / seg
            f = fn(lat, lon) if fn else 1.0
            row.append(c + frame @ Vector((radii[0] * f * math.cos(lat) * math.cos(lon),
                                           radii[1] * f * math.cos(lat) * math.sin(lon), radii[2] * math.sin(lat))))
        rows.append(row)
    rows.append([c + frame @ Vector((0, 0, radii[2]))])
    return rows


def grid_faces(bm, rows, seg):
    """Faces between ellipsoid_verts rows (already bm verts)."""
    faces = []
    for a, b in zip(rows, rows[1:]):
        for k in range(seg):
            k1 = (k + 1) % seg
            if len(a) == 1:
                faces.append(bm.faces.new((a[0], b[k1], b[k])))
            elif len(b) == 1:
                faces.append(bm.faces.new((a[k], a[k1], b[0])))
            else:
                faces.append(bm.faces.new((a[k], a[k1], b[k1], b[k])))
    return faces


def ellipsoid(pc, c, radii, mat, seg=16, rings=10, frame=None, smooth=True, fn=None):
    frame = frame or Matrix.Identity(3)
    bm, uvl = pc.bm, pc.uv
    pts = ellipsoid_verts(Vector(c), radii, frame, seg, rings, fn)
    rows = [[bm.verts.new(p) for p in row] for row in pts]
    faces = grid_faces(bm, rows, seg)
    stripes = max(1, round(ellipse_perimeter(radii[0], radii[1]) / STRIPE))
    mi = pc.mi(mat)
    for f in faces:
        f.material_index = mi
        f.smooth = smooth
        for loop in f.loops:
            p = loop.vert.co - Vector(c)
            lon = math.atan2(p.y, p.x) % (2 * math.pi)
            loop[uvl].uv = (stripes * lon / (2 * math.pi), p.z / STRIPE)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def prism(pc, poly, origin, ax_u, ax_v, thickness, mat, smooth=False):
    """Flat polygon [(u, v)] in the plane origin + u*ax_u + v*ax_v, extruded +-thickness/2 along ax_u x ax_v."""
    bm = pc.bm
    o, au, av = Vector(origin), Vector(ax_u), Vector(ax_v)
    an = au.cross(av).normalized() * (thickness / 2)
    front = [bm.verts.new(o + au * u + av * v + an) for u, v in poly]
    back = [bm.verts.new(o + au * u + av * v - an) for u, v in poly]
    faces = [bm.faces.new(front), bm.faces.new(list(reversed(back)))]
    n = len(poly)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((front[j], front[i], back[i], back[j])))
    tri = bmesh.ops.triangulate(bm, faces=faces[:2], quad_method="BEAUTY", ngon_method="EAR_CLIP")
    faces = tri["faces"] + faces[2:]
    mi = pc.mi(mat)
    for f in faces:
        f.material_index = mi
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def to_bm_ellipsoid(c, radii, frame=None, seg=24, rings=14):
    bm = bmesh.new()
    rows = [[bm.verts.new(p) for p in row] for row in ellipsoid_verts(Vector(c), radii, frame or Matrix.Identity(3), seg, rings)]
    faces = grid_faces(bm, rows, seg)
    for f in faces:
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return bm


def bm_merge(dst, src):
    vs = [dst.verts.new(v.co) for v in src.verts]
    src.verts.index_update()
    for f in src.faces:
        nf = dst.faces.new([vs[v.index] for v in f.verts])
        nf.smooth = f.smooth
    src.free()


def tube_bm(pts, radii, seg=12):
    """A tube as its own bmesh (for boolean cutters)."""
    tmp = Piece("tmp", (0, 0, 0))
    tube(tmp, pts, radii, None, seg=seg, side=(0, 0, 1))
    return tmp.bm


def prism_bm(poly, origin, ax_u, ax_v, thickness):
    tmp = Piece("tmp", (0, 0, 0))
    prism(tmp, poly, origin, ax_u, ax_v, thickness, None, smooth=False)
    return tmp.bm


def boolean_into(pc, base_bm, base_mat, cutter_bm, cutter_mat):
    """base minus cutter (exact solver, cutter faces keep cutter_mat), appended to the piece."""
    me = bpy.data.meshes.new("tmp_base")
    base_bm.to_mesh(me)
    base_bm.free()
    me.materials.append(base_mat)
    me.materials.append(cutter_mat)
    base = link(bpy.data.objects.new("tmp_base", me))
    cme = bpy.data.meshes.new("tmp_cut")
    cutter_bm.to_mesh(cme)
    cutter_bm.free()
    cme.materials.append(cutter_mat)
    cut = link(bpy.data.objects.new("tmp_cut", cme))
    mod = base.modifiers.new("Cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cut
    mod.material_mode = "TRANSFER"
    dg = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.meshes.new_from_object(base.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    for o in (base, cut):
        data = o.data
        bpy.data.objects.remove(o)
        bpy.data.meshes.remove(data)
    vs = [pc.bm.verts.new(v.co) for v in out.vertices]
    for p in out.polygons:
        try:
            f = pc.bm.faces.new([vs[i] for i in p.vertices])
        except ValueError:
            continue
        mat = out.materials[p.material_index] if p.material_index < len(out.materials) else base_mat
        f.material_index = pc.mi(mat or base_mat)
        f.smooth = p.use_smooth
    print(f"  boolean {pc.name}: {len(out.polygons)} faces", flush=True)
    bpy.data.meshes.remove(out)


def frame_from(n, up):
    """3x3 with columns (side, up', n): n = the depth axis, up projected perpendicular to n."""
    n = Vector(n).normalized()
    u = Vector(up) - n * Vector(up).dot(n)
    u.normalize()
    s = u.cross(n)
    return Matrix((s, u, n)).transposed()


def rotate_about(v, axis, a):
    return Matrix.Rotation(a, 3, Vector(axis).normalized()) @ Vector(v)


# ---------------------------------------------------------------- Jack

def skull_h_factor(h):
    """Horizontal radius factor of the egg skull at normalised height h (-1 chin .. 1 crown)."""
    return math.sqrt(max(0.0, 1 - h * h)) * (1 - 0.17 * max(0.0, -h) ** 1.5)


def skull_point(phi, z, inset=0.0):
    """Point on the skull at horizontal angle phi (0 = front +Y, + toward +X) and height z; plus its normal."""
    rx, ry, rz = HEAD_R
    h = (z - HEAD_C.z) / rz
    s = skull_h_factor(h)
    fy = 0.94 if math.cos(phi) > 0 else 1.0
    p = Vector((rx * s * math.sin(phi), ry * fy * s * math.cos(phi), z - HEAD_C.z)) + HEAD_C
    q = p - HEAD_C
    n = Vector((q.x / rx ** 2, q.y / (ry * fy) ** 2, q.z / rz ** 2)).normalized()
    return p - n * inset, n


def build_head(P, pc):
    rx, ry, rz = HEAD_R
    seg, rings = 64, 40
    bm = bmesh.new()
    rows = [[bm.verts.new(HEAD_C - Vector((0, 0, rz)))]]
    for j in range(1, rings):
        h = -math.cos(math.pi * j / rings)
        row = []
        for k in range(seg):
            phi = 2 * math.pi * k / seg
            row.append(bm.verts.new(skull_point(phi, HEAD_C.z + h * rz)[0]))
        rows.append(row)
    rows.append([bm.verts.new(HEAD_C + Vector((0, 0, rz)))])
    for f in grid_faces(bm, rows, seg):
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    cut = bmesh.new()
    # eye sockets: big ovals, tops leaning out ("\ /"), deep bowls
    for sgn in (-1, 1):
        p, n = skull_point(sgn * math.radians(21), 14.56)
        fr = frame_from(n, (0, 0, 1))
        tilt = Matrix.Rotation(sgn * math.radians(13), 3, n)
        bm_merge(cut, to_bm_ellipsoid(p - n * 0.14, (0.43, 0.57, 0.42), tilt @ fr, 28, 16))
        # nose slits: small, close together, tops leaning in ("/\")
        p, n = skull_point(sgn * math.radians(4.6), 13.93)
        fr = frame_from(n, (0, 0, 1))
        tilt = Matrix.Rotation(-sgn * math.radians(24), 3, n)
        bm_merge(cut, to_bm_ellipsoid(p - n * 0.08, (0.055, 0.16, 0.22), tilt @ fr, 12, 8))
    # the grin: a long groove from cheek to cheek, flat in the middle, curling up at the ends
    mouth = []
    for i in range(41):
        t = -1 + 2 * i / 40
        mouth.append(skull_point(t * math.radians(80), 13.36 + 0.07 * t * t + 0.4 * t ** 6, 0.012))
    radii = [0.034 + 0.024 * (1 - abs(-1 + 2 * i / 40) ** 3) for i in range(41)]
    bm_merge(cut, tube_bm([p for p, _ in mouth], radii, 10))
    boolean_into(pc, bm, P["bone"], cut, P["black"])

    # stitches across the grin: thin black stitches, half sunk into the bone
    for i in range(15):
        t = -0.86 + 1.72 * i / 14
        phi = t * math.radians(80)
        z = 13.36 + 0.07 * t * t + 0.4 * t ** 6
        p, n = skull_point(phi, z)
        p2, _ = skull_point(phi + 0.01, 13.36 + 0.07 * (t + 0.01) ** 2 + 0.4 * (t + 0.01) ** 6)
        along = (p2 - p).normalized()
        across = n.cross(along).normalized()
        if across.z < 0:
            across = -across
        L = 0.17 + 0.05 * (1 - abs(t))
        tube(pc, [p - across * L + n * 0.004, p + n * 0.012, p + across * L + n * 0.004], [0.022, 0.025, 0.022],
             P["black"], seg=6)
    # neck: a thin bone with vertebra rings, from inside the collar up into the skull
    tube(pc, [(0, 0.02, 11.95), (0, 0.02, 12.4), (0, 0.04, 12.9), (0, 0.06, 13.1)], [0.2, 0.21, 0.24, 0.3],
         P["bone"], seg=14)
    for z in (12.3, 12.62):
        ellipsoid(pc, (0, 0.03, z), (0.26, 0.24, 0.07), P["bone"], 14, 6)


TORSO = [  # z, rx, ry (jacket cross-sections, centred on x = 0)
    (7.75, 0.6, 0.42), (8.15, 0.55, 0.39), (8.8, 0.58, 0.4), (9.6, 0.7, 0.44), (10.3, 0.84, 0.48),
    (10.95, 1.0, 0.5), (11.38, 1.22, 0.5), (11.62, 1.3, 0.46), (11.84, 0.95, 0.4), (12.02, 0.5, 0.33),
    (12.14, 0.3, 0.27),
]


def torso_section(z):
    for (z0, a0, b0), (z1, a1, b1) in zip(TORSO, TORSO[1:]):
        if z0 <= z <= z1:
            f = (z - z0) / (z1 - z0)
            return a0 + (a1 - a0) * f, b0 + (b1 - b0) * f
    return TORSO[-1][1:] if z > TORSO[-1][0] else TORSO[0][1:]


def front_y(x, z):
    rx, ry = torso_section(z)
    return ry * math.sqrt(max(0.0, 1 - (x / rx) ** 2))


def front_slab(pc, rows, mat, off, cols=4, depth=0.06):
    """A thin slab lying on the jacket front: rows = [(z, x_left, x_right)], top to bottom."""
    bm = pc.bm
    F, B = [], []
    for z, xl, xr in rows:
        fr, br = [], []
        for j in range(cols + 1):
            x = xl + (xr - xl) * j / cols
            y = front_y(x, z)
            fr.append(bm.verts.new((x, y + off, z)))
            br.append(bm.verts.new((x, y - depth, z)))
        F.append(fr)
        B.append(br)
    faces = []
    n = len(rows)
    for i in range(n - 1):
        for j in range(cols):
            faces.append(bm.faces.new((F[i][j], F[i][j + 1], F[i + 1][j + 1], F[i + 1][j])))
            faces.append(bm.faces.new((B[i][j + 1], B[i][j], B[i + 1][j], B[i + 1][j + 1])))
    ring = [(0, j) for j in range(cols + 1)] + [(i, cols) for i in range(1, n)] + \
           [(n - 1, j) for j in range(cols - 1, -1, -1)] + [(i, 0) for i in range(n - 2, 0, -1)]
    for a, b in zip(ring, ring[1:] + ring[:1]):
        faces.append(bm.faces.new((F[a[0]][a[1]], F[b[0]][b[1]], B[b[0]][b[1]], B[a[0]][a[1]])))
    mi = pc.mi(mat)
    for f in faces:
        f.material_index = mi
        f.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def bat_outline(scale=1.0, ears=True):
    """Right half of a bat silhouette (x >= 0) from the top centre round the wing to the bottom centre; mirrored."""
    top = [(0.0, 0.085)]
    if ears:
        top += [(0.03, 0.09), (0.05, 0.17), (0.075, 0.075)]
    top += [(0.16, 0.1), (0.32, 0.15), (0.5, 0.2), (0.66, 0.26), (0.8, 0.34)]
    fingers = [(0.8, 0.34), (0.69, 0.03), (0.5, -0.06), (0.3, -0.1), (0.1, -0.07)]
    edge = []
    for (x0, z0), (x1, z1) in zip(fingers, fingers[1:]):
        for s in (0.25, 0.5, 0.75):
            edge.append((x0 + (x1 - x0) * s, z0 + (z1 - z0) * s + 0.075 * math.sin(math.pi * s)))
        edge.append((x1, z1))
    half = top + edge + [(0.05, -0.1), (0.0, -0.13)]
    full = half + [(-x, z) for x, z in reversed(half[1:-1])]
    return [(x * scale, z * scale) for x, z in full]


def build_torso(P, pc):
    tube(pc, [(0, 0, z) for z, _, _ in TORSO], [(a, b) for _, a, b in TORSO], P["suit"], seg=24)
    # waist seam of the tailcoat
    tube(pc, [(0, 0, 8.08), (0, 0, 8.2)], [(0.585, 0.41), (0.575, 0.405)], P["black"], seg=24)
    # shirt V and the black lapels round it
    shirt = [(12.12, -0.3, 0.3), (11.85, -0.25, 0.25), (11.55, -0.19, 0.19), (11.25, -0.12, 0.12),
             (10.95, -0.05, 0.05), (10.78, -0.01, 0.01)]
    front_slab(pc, shirt, P["bone"], 0.022)
    for sgn in (-1, 1):
        lap = [(12.06, 0.27, 0.42), (11.8, 0.23, 0.58), (11.5, 0.18, 0.62), (11.2, 0.11, 0.5), (10.95, 0.04, 0.3),
               (10.72, 0.0, 0.08)]
        rows = [(z, a, b) if sgn > 0 else (z, -b, -a) for z, a, b in lap]
        front_slab(pc, rows, P["black"], 0.034)
    for z in (10.45, 9.85):
        ellipsoid(pc, (0, front_y(0, z) + 0.01, z), (0.07, 0.04, 0.07), P["black"], 10, 5)
    # the bat bow tie, wings swept a touch forward
    y = front_y(0, 12.02) + 0.1
    prism(pc, bat_outline(1.0), (0, y, 12.02), (1, 0.12, 0), (0, 0, 1), 0.07, P["black"])
    ellipsoid(pc, (0, y + 0.03, 12.04), (0.09, 0.06, 0.09), P["black"], 10, 6)
    # shoulder pads
    for sgn in (-1, 1):
        ellipsoid(pc, (sgn * 1.08, -0.02, 11.52), (0.36, 0.42, 0.2), P["suit"], 16, 8)


def build_hips(P, pc):
    tube(pc, [(0, 0, 6.8), (0, 0, 7.1), (0, 0, 7.6), (0, 0, 8.1), (0, 0, 8.35)],
         [(0.42, 0.3), (0.72, 0.4), (0.78, 0.42), (0.62, 0.4), (0.55, 0.38)], P["suit"], seg=20)
    # long split coat tails hanging behind
    for sgn in (-1, 1):
        pts = [(sgn * 0.2, -0.33, 8.3), (sgn * 0.24, -0.43, 7.6), (sgn * 0.27, -0.5, 6.6), (sgn * 0.27, -0.56, 5.6),
               (sgn * 0.24, -0.6, 4.8), (sgn * 0.2, -0.62, 4.35), (sgn * 0.17, -0.63, 4.15)]
        radii = [(0.34, 0.07), (0.36, 0.065), (0.34, 0.06), (0.29, 0.055), (0.19, 0.05), (0.08, 0.035), (0.01, 0.01)]
        tube(pc, pts, radii, P["suit"], seg=12, side=(1, 0, 0))


def arm_dirs(sgn):
    d = Vector((sgn * math.sin(ARM_OUT), 0.0, -math.cos(ARM_OUT)))
    d2 = rotate_about(d, (1, 0, 0), math.radians(5))  # the elbow a touch bent: forearm slightly forward
    s = Vector(SHOULDER)
    s.x *= sgn
    e = s + d * UPPER_ARM
    w = e + d2 * LOWER_ARM
    return s, e, w, d, d2.normalized()


def build_arm_upper(P, pc, sgn):
    s, e, _, d, _ = arm_dirs(sgn)
    tube(pc, [s - d * 0.15, s + d * 0.6, e], [0.27, 0.24, 0.19], P["suit"], seg=14)
    ellipsoid(pc, s, (0.3, 0.3, 0.3), P["suit"], 14, 8)


def build_arm_lower(P, pc, sgn):
    _, e, w, _, d2 = arm_dirs(sgn)
    tube(pc, [e - d2 * 0.05, e + d2 * 1.4, w - d2 * 0.12, w + d2 * 0.02], [0.19, 0.165, 0.16, 0.18], P["suit"], seg=14)
    ellipsoid(pc, e, (0.2, 0.2, 0.2), P["suit"], 12, 7)
    tube(pc, [w - d2 * 0.1, w + d2 * 0.02], [0.185, 0.185], P["black"], seg=14, stripes=1)  # cuff band


def build_hand(P, pc, sgn):
    _, _, w, _, down = arm_dirs(sgn)
    palm_n = Vector((-sgn, 0, 0))  # palm faces the body
    palm_n = (palm_n - down * palm_n.dot(down)).normalized()
    fwd = palm_n.cross(down).normalized()
    if fwd.y < 0:
        fwd = -fwd
    tube(pc, [w - down * 0.25, w + down * 0.2], [0.085, 0.08], P["bone"], seg=8)  # wrist bones
    ellipsoid(pc, w + down * 0.06, (0.11, 0.1, 0.1), P["bone"], 10, 6)
    fr = Matrix((fwd, palm_n, down)).transposed()
    ellipsoid(pc, w + down * 0.4, (0.25, 0.085, 0.36), P["bone"], 12, 8, frame=fr)

    def finger(base, direction, lengths, curl, radius):
        pts = [base]
        knuckles = [base]
        dvec = Vector(direction).normalized()
        for L, c in zip(lengths, curl):
            n = palm_n - dvec * palm_n.dot(dvec)
            if n.length > 1e-6:
                n.normalize()
                dvec = (dvec * math.cos(math.radians(c)) + n * math.sin(math.radians(c))).normalized()
            pts.append(pts[-1] + dvec * L)
            knuckles.append(pts[-1])
        tip = pts[-1] + dvec * 0.08
        pts.append(tip)
        rs = [radius * (1 - 0.15 * i) for i in range(len(pts) - 1)] + [0.012]
        tube(pc, pts, rs, P["bone"], seg=7)
        for k in knuckles[:-1]:
            ellipsoid(pc, k, (radius * 1.22,) * 3, P["bone"], 8, 5)

    for off, L in ((-0.19, 0.84), (-0.065, 1.02), (0.065, 1.1), (0.19, 0.98)):
        base = w + down * 0.72 + fwd * off
        direction = down + fwd * off * 0.35
        finger(base, direction, [L * 0.42, L * 0.33, L * 0.22], [12, 22, 20], 0.06)
    finger(w + down * 0.28 + fwd * 0.2 + palm_n * 0.05, down * 0.55 + fwd * 0.75 + palm_n * 0.35, [0.38, 0.3],
           [18, 20], 0.065)


def pumpkin_centre_on_hand():
    _, _, w, _, down = arm_dirs(1)
    palm_n = Vector((-1, 0, 0))
    palm_n = (palm_n - down * palm_n.dot(down)).normalized()
    return w + down * 0.62 + palm_n * 0.9


def build_leg_upper(P, pc, sgn):
    x = sgn * HIP_X
    tube(pc, [(x, 0, HIP_Z + 0.35), (x, 0, HIP_Z - 0.6), (x, 0, KNEE_Z)], [0.31, 0.27, 0.205], P["suit"], seg=14)


def build_leg_lower(P, pc, sgn):
    x = sgn * HIP_X
    tube(pc, [(x, 0, KNEE_Z + 0.05), (x, 0, KNEE_Z - 1.6), (x, 0, 0.75), (x, 0.02, 0.42)], [0.205, 0.17, 0.165, 0.2],
         P["suit"], seg=14)
    ellipsoid(pc, (x, 0, KNEE_Z), (0.21, 0.21, 0.21), P["suit"], 12, 7)


def build_foot(P, pc, sgn):
    x = sgn * HIP_X
    st = [  # y, z_centre, half-width, half-height
        (-0.44, 0.27, 0.15, 0.25), (-0.38, 0.3, 0.2, 0.3), (-0.15, 0.32, 0.25, 0.32), (0.15, 0.3, 0.28, 0.3),
        (0.55, 0.22, 0.27, 0.22), (0.95, 0.165, 0.23, 0.165), (1.3, 0.13, 0.16, 0.13), (1.6, 0.15, 0.1, 0.085),
        (1.85, 0.25, 0.055, 0.05), (1.98, 0.38, 0.03, 0.03), (2.02, 0.47, 0.012, 0.012),
    ]
    tube(pc, [(x, y, z) for y, z, _, _ in st], [(w, h) for _, _, w, h in st], P["black"], seg=16)
    # heel block
    tube(pc, [(x, -0.38, 0.0), (x, -0.38, 0.16)], [(0.17, 0.11), (0.18, 0.12)], P["black"], seg=12, side=(1, 0, 0))


def build_pumpkin(P, pc, c, R=0.82):
    c = Vector(c)
    seg, rings = 60, 28
    rib = 10

    def r_at(lat, lon):
        g = (1 + math.cos(rib * (lon - math.pi / 2))) / 2  # a lobe straight at the front (+Y)
        return 0.9 + 0.1 * math.sqrt(g)

    bm = bmesh.new()
    rows = [[bm.verts.new(c + Vector((0, 0, -0.5 * R)))]]
    for j in range(1, rings):
        lat = -math.pi / 2 + math.pi * j / rings
        row = []
        for k in range(seg):
            lon = 2 * math.pi * k / seg
            f = r_at(lat, lon)
            cl = math.cos(lat)
            dimple = 1 - 0.3 * math.exp(-(cl / 0.3) ** 2)
            row.append(bm.verts.new(c + Vector((R * f * cl * math.cos(lon), R * f * cl * math.sin(lon),
                                                0.8 * R * math.sin(lat) * dimple))))
        rows.append(row)
    rows.append([bm.verts.new(c + Vector((0, 0, 0.5 * R)))])
    for f in grid_faces(bm, rows, seg):
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    s = R / 0.8
    cut = bmesh.new()
    y0, y1 = c.y + 0.4 * s, c.y + 1.2 * s
    depth = y1 - y0
    for sgn in (-1, 1):
        eye = [(0.1, 0.06), (0.43, 0.04), (0.29, 0.33)]
        eye = [(sgn * x, z) for x, z in eye]
        if sgn < 0:
            eye.reverse()
        bm_merge(cut, prism_bm([(x * s, z * s) for x, z in eye], (c.x, (y0 + y1) / 2, c.z), (1, 0, 0), (0, 0, 1), depth))
    nose = [(-0.08, -0.07), (0.08, -0.07), (0.0, 0.07)]
    bm_merge(cut, prism_bm([(x * s, z * s) for x, z in nose], (c.x, (y0 + y1) / 2, c.z), (1, 0, 0), (0, 0, 1), depth))
    mouth = [(-0.54, -0.1), (-0.36, -0.17), (-0.22, -0.18), (-0.22, -0.27), (-0.1, -0.27), (-0.1, -0.19),
             (0.12, -0.19), (0.12, -0.27), (0.24, -0.27), (0.24, -0.18), (0.38, -0.16), (0.54, -0.09),
             (0.44, -0.3), (0.26, -0.42), (0.06, -0.46), (0.06, -0.37), (-0.06, -0.37), (-0.06, -0.46),
             (-0.26, -0.42), (-0.44, -0.3)]
    bm_merge(cut, prism_bm([(x * s, z * s) for x, z in reversed(mouth)], (c.x, (y0 + y1) / 2, c.z), (1, 0, 0),
                           (0, 0, 1), depth))
    boolean_into(pc, bm, P["pumpkin"], cut, P["glow"])
    stalk = [c + Vector(v) * s for v in ((0, 0, 0.36), (0.01, 0, 0.52), (0.04, -0.01, 0.66), (0.1, -0.03, 0.76),
                                         (0.18, -0.04, 0.8), (0.24, -0.04, 0.78))]
    tube(pc, stalk, [0.11 * s, 0.09 * s, 0.075 * s, 0.065 * s, 0.055 * s, 0.045 * s], P["stalk"], seg=8)


def jack_pieces(P):
    pcs = {}

    def add(name, joint, parent):
        pcs[name] = Piece(name, joint, parent)
        return pcs[name]

    build_hips(P, add("Hips", (0, 0, HIP_Z), "Jack"))
    build_torso(P, add("Torso", (0, 0, WAIST_Z), "Hips"))
    build_head(P, add("Head", (0, 0, NECK_Z), "Torso"))
    for sgn, side in ((-1, "L"), (1, "R")):
        s, e, w, _, _ = arm_dirs(sgn)
        build_arm_upper(P, add(f"ArmUpper{side}", s, "Torso"), sgn)
        build_arm_lower(P, add(f"ArmLower{side}", e, f"ArmUpper{side}"), sgn)
        build_hand(P, add(f"Hand{side}", w, f"ArmLower{side}"), sgn)
    c = pumpkin_centre_on_hand()
    build_pumpkin(P, add("Pumpkin", c, "HandR"), c)
    for sgn, side in ((-1, "L"), (1, "R")):
        x = sgn * HIP_X
        build_leg_upper(P, add(f"LegUpper{side}", (x, 0, HIP_Z), "Hips"), sgn)
        build_leg_lower(P, add(f"LegLower{side}", (x, 0, KNEE_Z), f"LegUpper{side}"), sgn)
        build_foot(P, add(f"Foot{side}", (x, 0, ANKLE_Z), f"LegLower{side}"), sgn)
    return pcs


# ---------------------------------------------------------------- bat

def build_bat(P):
    body = Piece("Body", (0, 0, 0))
    ellipsoid(body, (0, -0.03, 0), (0.085, 0.16, 0.08), P["black"], 14, 8)
    ellipsoid(body, (0, 0.14, 0.02), (0.07, 0.075, 0.065), P["black"], 12, 7)
    ellipsoid(body, (0, 0.2, 0.01), (0.035, 0.04, 0.03), P["black"], 8, 5)  # snout
    for sgn in (-1, 1):
        tube(body, [(sgn * 0.035, 0.15, 0.06), (sgn * 0.05, 0.155, 0.11), (sgn * 0.06, 0.15, 0.15)],
             [0.028, 0.018, 0.003], P["black"], seg=6)
        ellipsoid(body, (sgn * 0.028, 0.205, 0.04), (0.013, 0.01, 0.013), P["eye"], 8, 5)
        tube(body, [(sgn * 0.03, -0.17, -0.02), (sgn * 0.04, -0.22, -0.03)], [0.012, 0.008], P["black"], seg=6)
    wings = {}
    for sgn, side in ((-1, "L"), (1, "R")):
        sh = Vector((sgn * 0.07, 0.04, 0.01))
        pc = Piece(f"Wing{side}", sh, "Body")
        wrist = Vector((sgn * 0.3, 0.13, 0.015))
        tips = [Vector((sgn * 0.7, 0.07, 0.01)), Vector((sgn * 0.63, -0.13, 0.01)), Vector((sgn * 0.46, -0.25, 0.01)),
                Vector((sgn * 0.25, -0.26, 0.01))]
        attach = Vector((sgn * 0.06, -0.15, 0.01))
        outline = [(sh.x, sh.y), (wrist.x, wrist.y + 0.012), (sgn * 0.33, 0.18), (wrist.x + sgn * 0.02, wrist.y)]
        prev = Vector((tips[0].x, tips[0].y))
        outline.append((prev.x, prev.y))
        for t in tips[1:] + [attach]:
            nxt = Vector((t.x, t.y))
            mid = (prev + nxt) / 2
            towards = Vector((-sgn * 0.0, 0)) + (Vector((wrist.x, wrist.y)) - mid).normalized() * 0.06
            for s in (0.3, 0.5, 0.7):
                q = prev.lerp(nxt, s) + towards * math.sin(math.pi * s)
                outline.append((q.x, q.y))
            outline.append((nxt.x, nxt.y))
            prev = nxt
        if sgn < 0:
            outline.reverse()
        prism(pc, outline, (0, 0, 0.008), (1, 0, 0), (0, 1, 0), 0.012, P["wing"])
        tube(pc, [sh, sh.lerp(wrist, 0.5) + Vector((0, 0, 0.01)), wrist], [0.022, 0.016, 0.014], P["black"], seg=6)
        tube(pc, [wrist, Vector((sgn * 0.33, 0.18, 0.015))], [0.01, 0.004], P["black"], seg=5)  # thumb claw
        for t in tips:
            mid = wrist.lerp(t, 0.5) + Vector((0, 0, 0.012))
            tube(pc, [wrist, mid, t], [0.011, 0.008, 0.004], P["black"], seg=5)
        wings[side] = pc
    return {"Body": body, "WingL": wings["L"], "WingR": wings["R"]}


# ---------------------------------------------------------------- pieces -> objects

def make_objects(pcs, root_name=None):
    """Each piece -> one mesh object, origin at its joint, identity rotation, parented by name. Edges between
    different materials are marked sharp so the carved sockets don't smear the bone's shading."""
    objs = {}
    root = None
    if root_name:
        root = link(bpy.data.objects.new(root_name, None))
        root.empty_display_size = 1.0
        objs[root_name] = root
    for name, pc in pcs.items():
        bm = pc.bm
        for e in bm.edges:
            fs = e.link_faces
            if len(fs) == 2 and fs[0].material_index != fs[1].material_index:
                e.smooth = False
        bmesh.ops.transform(bm, matrix=Matrix.Translation(-pc.joint), verts=bm.verts)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for m in pc.mats:
            me.materials.append(m)
        o = link(bpy.data.objects.new(name, me))
        objs[name] = o
    for name, pc in pcs.items():
        o = objs[name]
        par = objs.get(pc.parent) if pc.parent else None
        pj = pcs[pc.parent].joint if pc.parent in pcs else Vector((0, 0, 0))
        if par is not None:
            o.parent = par
            o.matrix_parent_inverse = Matrix.Identity(4)
        o.location = pc.joint - pj
    bpy.context.view_layer.update()
    return objs


def build_jack(P):
    return make_objects(jack_pieces(P), "Jack")


def build_pumpkin_alone(P):
    pc = Piece("Pumpkin", (0, 0, 0))
    build_pumpkin(P, pc, (0, 0, 0))
    return make_objects({"Pumpkin": pc})


# ---------------------------------------------------------------- export + checks

def godot(v):
    """Blender (x, y, z) -> Godot (x, z, -y)."""
    return (round(v[0], 3), round(v[2], 3), round(-v[1] + 0.0, 3))


def tris_of(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons) if o.type == "MESH" else 0


def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs if o.type == "MESH" for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def export_glb(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs.values():
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_lights=False, export_cameras=False, export_image_format="AUTO")
    allo = list(objs.values())
    lo, hi = bounds(allo)
    size = hi - lo
    tris = sum(tris_of(o) for o in allo)
    print(f"exported {path}: {os.path.getsize(path) / 1e6:.3f} MB, {len(allo)} nodes, {tris} triangles", flush=True)
    print(f"  size (Godot x across, y up, z along): {size.x:.3f} x {size.z:.3f} x {size.y:.3f}; "
          f"Blender z {lo.z:.3f}..{hi.z:.3f}", flush=True)
    for o in allo:
        par = o.parent.name if o.parent else "-"
        print(f"  {o.name:10s} parent {par:10s} pivot (Godot) {godot(o.matrix_world.translation)}  {tris_of(o)} tris  "
              f"[{', '.join(m.name for m in (o.data.materials if o.type == 'MESH' else []))}]", flush=True)


def verify(path, tree):
    reset()
    bpy.ops.import_scene.gltf(filepath=path)
    objs = {o.name: o for o in bpy.data.objects}
    bad = []
    for name, par in tree.items():
        o = objs.get(name)
        if o is None:
            bad.append(f"{name} missing")
            continue
        got = o.parent.name if o.parent else None
        if got != par:
            bad.append(f"{name} parent {got}, want {par}")
        q = o.matrix_basis.to_quaternion()
        if q.angle > 1e-4 or (o.matrix_basis.to_scale() - Vector((1, 1, 1))).length > 1e-4:
            bad.append(f"{name} rest rotation/scale not identity ({q.angle:.5f})")
    extra = sorted(set(objs) - set(tree))
    if extra:
        bad.append("extra nodes: " + ", ".join(extra))
    lo, hi = bounds(list(objs.values()))
    msg = f"verify {os.path.basename(path)}: {len(objs)} nodes, box {tuple(round(v, 3) for v in lo)}..{tuple(round(v, 3) for v in hi)}"
    if "Jack" in tree:
        h = hi.z - lo.z
        msg += f", height {h:.3f}, feet z {lo.z:.3f}"
        if abs(h - HEIGHT) > 0.3:
            bad.append(f"height {h:.3f} not 16 +- 0.3")
        if abs(lo.z) > 0.03:
            bad.append(f"feet at z {lo.z:.3f}, not 0")
    if "WingL" in tree:
        msg += f", wingspan {hi.x - lo.x:.3f}"
    if tree is PUMPKIN_TREE:
        msg += f", across {hi.x - lo.x:.3f}"
    print(msg, flush=True)
    print("  OK: every contract node with the right parent, identity rest" if not bad else "  PROBLEMS: " + "; ".join(bad),
          flush=True)
    return bad


def game_export():
    t = time.time()
    problems = []
    for fname, builder in (("jack.glb", build_jack), ("pumpkin.glb", build_pumpkin_alone), ("bat.glb", None)):
        reset()
        P = palette()
        objs = builder(P) if builder else make_objects(build_bat(P))
        path = os.path.join(GAME_DIR, fname)
        export_glb(objs, path)
        if fname == "jack.glb":
            print("pivots (Godot coordinates, world = rest pose; for the primitive stand-in):", flush=True)
            for name in JACK_TREE:
                if name != "Jack":
                    print(f"  {name:10s} {godot(objs[name].matrix_world.translation)}", flush=True)
    for fname, tree in GLBS.items():
        problems += verify(os.path.join(GAME_DIR, fname), tree)
    print(f"exported in {time.time() - t:.0f} s", flush=True)
    if problems:
        sys.exit("glb checks failed")


# ---------------------------------------------------------------- preview

def setup_world():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("Dusk")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.0
    cr.elements[0].color = (0.75, 0.32, 0.12, 1)
    cr.elements[1].position = 0.45
    cr.elements[1].color = (0.06, 0.05, 0.16, 1)
    e = cr.elements.new(0.12)
    e.color = (0.38, 0.14, 0.2, 1)
    nt.links.new(sep.outputs["Z"], ramp.inputs[0])
    bg = nt.nodes["Background"]
    nt.links.new(ramp.outputs[0], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 1.0
    sun = bpy.data.objects.new("Moon", bpy.data.lights.new("Moon", "SUN"))
    sun.data.energy = 3.2
    sun.data.color = (0.95, 0.93, 1.0)
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-145))
    link(sun)
    rim = bpy.data.objects.new("Rim", bpy.data.lights.new("Rim", "SUN"))
    rim.data.energy = 1.6
    rim.data.color = (0.55, 0.65, 1.0)
    rim.data.use_shadow = False
    rim.rotation_euler = (math.radians(70), 0, math.radians(20))
    link(rim)
    gm = bpy.data.materials.new("Ground")
    gm.use_nodes = True
    gnt = gm.node_tree
    p = gnt.nodes["Principled BSDF"]
    p.inputs["Roughness"].default_value = 0.95
    noise = gnt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.06
    noise.inputs["Detail"].default_value = 6
    gr = gnt.nodes.new("ShaderNodeValToRGB")
    gr.color_ramp.elements[0].color = (0.035, 0.05, 0.028, 1)
    gr.color_ramp.elements[1].color = (0.09, 0.075, 0.05, 1)
    gnt.links.new(noise.outputs["Fac"], gr.inputs[0])
    gnt.links.new(gr.outputs[0], p.inputs["Base Color"])
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400)
    me = bpy.data.meshes.new("Ground")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(gm)
    link(bpy.data.objects.new("Ground", me))
    cam = bpy.data.objects.new("RenderCam", bpy.data.cameras.new("RenderCam"))
    cam.data.clip_start = 0.05
    cam.data.clip_end = 3000
    link(cam)
    scene.camera = cam
    return cam


def shot(cam, target, az, el, dist, lens):
    """Camera at azimuth az (degrees from +Y toward +X), elevation el, distance dist from target."""
    a, e = math.radians(az), math.radians(el)
    pos = Vector(target) + dist * Vector((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = pos
    cam.data.lens = lens
    cam.rotation_euler = (Vector(target) - pos).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()


def render(path):
    bpy.context.scene.render.filepath = path
    t = time.time()
    bpy.ops.render.render(write_still=True)
    print(f"rendered {path} in {time.time() - t:.0f} s", flush=True)


POSES = {  # degrees about the pivot's own X, Y, Z (Blender; rest = all zero)
    "hero": {"ArmUpperR": (62, 0, -22), "ArmLowerR": (48, 0, 0), "HandR": (0, 0, -90),
             "ArmUpperL": (18, 38, 0), "ArmLowerL": (38, 0, 0), "HandL": (0, 0, 20),
             "Head": (-8, 4, -16), "Torso": (2, 0, -6), "LegUpperL": (6, 4, 0), "LegUpperR": (-4, -5, 0),
             "FootL": (-6, 0, 0)},
    "walk": {"LegUpperL": (27, 0, 0), "LegLowerL": (-10, 0, 0), "FootL": (-17, 0, 0),
             "LegUpperR": (-22, 0, 0), "LegLowerR": (-42, 0, 0), "FootR": (30, 0, 0),
             "ArmUpperL": (-28, 4, 0), "ArmLowerL": (14, 0, 0), "ArmUpperR": (30, -4, 0), "ArmLowerR": (28, 0, 0),
             "HandR": (10, 0, 0), "Torso": (-6, 0, -7), "Hips": (0, 0, 6), "Head": (4, 0, 9)},
    "menace": {"ArmUpperR": (-30, -70, 0), "ArmLowerR": (75, 0, 0), "HandR": (20, 0, 0),
               "ArmUpperL": (40, 60, 0), "ArmLowerL": (60, 0, 0), "HandL": (10, 0, 0),
               "LegUpperL": (14, 6, 0), "LegLowerL": (-6, 0, 0), "FootL": (-8, 0, 0),
               "LegUpperR": (-10, -6, 0), "LegLowerR": (-16, 0, 0), "FootR": (14, 0, 0),
               "Torso": (-8, 0, 12), "Head": (-4, 0, -14)},
}


def pose(objs, name, root):
    for o in objs.values():
        if o is not root and o.type in ("MESH", "EMPTY"):
            o.rotation_mode = "XYZ"
            o.rotation_euler = (0, 0, 0)
    for part, (x, y, z) in POSES.get(name, {}).items():
        o = objs[part]
        o.rotation_mode = "XYZ"
        o.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
    bpy.context.view_layer.update()
    meshes = [o for o in objs.values() if o.type == "MESH" and o.name != "Pumpkin"]
    lo = min((o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices)
    root.location.z -= lo  # keep the lowest foot on the ground
    bpy.context.view_layer.update()


def glow_preview(strength=10.0):
    m = bpy.data.materials.get("PumpkinGlow")
    if m:
        m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = strength


def pumpkin_light(obj, energy=260.0, name="PumpkinLight"):
    mw = obj.matrix_world
    face = (mw.to_3x3() @ Vector((0, 1, 0))).normalized()
    light = link(bpy.data.objects.new(name, bpy.data.lights.new(name, "POINT")))
    light.data.energy = energy
    light.data.color = (1.0, 0.5, 0.15)
    light.data.shadow_soft_size = 0.4
    light.location = mw.translation + face * 1.05
    return light


def import_glb(path, loc=(0, 0, 0), rot_z=0.0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    holder = link(bpy.data.objects.new(os.path.basename(path).split(".")[0] + "Holder", None))
    for o in new:
        if o.parent is None:
            o.parent = holder
    holder.location = loc
    holder.rotation_euler = (0, 0, math.radians(rot_z))
    bpy.context.view_layer.update()
    return holder, {o.name.split(".")[0]: o for o in new}


def add_bat(P, loc, rot, flap, scale=1.0):
    objs = make_objects(build_bat(P))
    holder = link(bpy.data.objects.new("BatHolder", None))
    objs["Body"].parent = holder
    holder.location = loc
    holder.rotation_euler = tuple(math.radians(a) for a in rot)
    holder.scale = (scale,) * 3
    objs["WingR"].rotation_euler = (0, math.radians(-flap), 0)
    objs["WingL"].rotation_euler = (0, math.radians(flap), 0)
    return holder


def preview_built():
    reset()
    cam = setup_world()
    P = palette()
    objs = build_jack(P)
    root = objs["Jack"]
    glow_preview()
    if "hero" in SHOTS:
        pose(objs, "hero", root)
        light = pumpkin_light(objs["Pumpkin"])
        bats = [add_bat(P, (-6.5, 1.5, 13.5), (10, -15, 40), 25), add_bat(P, (-9.0, -2.0, 10.5), (-5, 10, 70), -15),
                add_bat(P, (5.5, -3.0, 15.8), (0, 20, -30), 35)]
        shot(cam, (0.6, 0, 8.6), 32, 7, 41, 50)
        render(os.path.join(PREVIEW, "jack-hero.png"))
        for o in [light] + bats:
            for c in [o] + list(o.children_recursive):
                bpy.data.objects.remove(c)
    if "game" in SHOTS:
        root.rotation_euler = (0, 0, math.radians(118))
        pose(objs, "menace", root)
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=(10, 10, 6), verts=bm.verts)
        bmesh.ops.translate(bm, vec=(0, 0, 3), verts=bm.verts)
        me = bpy.data.meshes.new("Building")
        bm.to_mesh(me)
        bm.free()
        me.materials.append(principled("BuildingGrey", (0.45, 0.46, 0.48), 0.7))
        box = link(bpy.data.objects.new("Building", me))
        box.location = (-12.5, -7.5, 0)
        # a pumpkin in flight toward the building and a few bats
        p = make_objects({"Pumpkin": (lambda pc: (build_pumpkin(P, pc, (0, 0, 0)), pc)[1])(Piece("Pumpkin", (0, 0, 0)))})
        fly = p["Pumpkin"]
        fly.name = "FlyingPumpkin"
        fly.location = (-7.5, -4.6, 12.5)
        fly.rotation_euler = (math.radians(20), math.radians(-30), math.radians(130))
        bpy.context.view_layer.update()
        pumpkin_light(fly, 200, "FlyLight")
        for i, (x, y, z) in enumerate(((-3, 3, 14), (2.5, 2, 12.5), (-1, -4, 15.5), (4, -2, 10), (-5, 1, 9.5))):
            add_bat(P, (x, y, z), (0, 15 * (i % 3 - 1), 60 * i), 30 - 12 * i)
        shot(cam, (-5.5, -3.5, 4.5), 192, 45, 70, 50)
        render(os.path.join(PREVIEW, "jack-game.png"))


def preview_glb():
    path = os.path.join(GAME_DIR, "jack.glb")
    if not os.path.exists(path):
        print("glb: run --game first", flush=True)
        return
    reset()
    cam = setup_world()
    holder, parts = import_glb(path)
    glow_preview()
    parts["Pumpkin"].hide_render = True  # the game hides it except when throwing
    pose(parts, "walk", holder)
    shot(cam, (0, 0.5, 8.0), 62, 9, 40, 50)
    render(os.path.join(PREVIEW, "jack-glb.png"))


def preview_props():
    paths = [os.path.join(GAME_DIR, f) for f in ("pumpkin.glb", "bat.glb")]
    if not all(os.path.exists(p) for p in paths):
        print("props: run --game first", flush=True)
        return
    reset()
    cam = setup_world()
    _, pk = import_glb(paths[0], (-1.05, 0, 0.5))
    glow_preview(6.0)
    pumpkin_light(pk["Pumpkin"], 30)
    _, bat = import_glb(paths[1], (1.15, -0.2, 1.05), -20)
    for w, a in (("WingR", -24), ("WingL", 24)):
        bat[w].rotation_mode = "XYZ"
        bat[w].rotation_euler = (0, math.radians(a), 0)
    shot(cam, (0.05, 0, 0.7), 14, 14, 6.4, 50)
    render(os.path.join(PREVIEW, "jack-props.png"))


def main():
    if GAME:
        game_export()
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        if {"hero", "game"} & set(SHOTS):
            preview_built()
        if "glb" in SHOTS:
            preview_glb()
        if "props" in SHOTS:
            preview_props()


main()
