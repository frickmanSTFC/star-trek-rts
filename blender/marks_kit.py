"""Mk I-V mockup rig for building upgrade marks (owner 2026-11-06: "pick another 2 or 3 buildings to start mocking up
their full details and Mk I through Mk V"), on top of building_kit.py (same materials, conventions, Build pieces).

A marks script defines BUILD(mark, M) that builds one mark under M.root, using M.piece("Name") for each Build<k> group
(in build order: the exocomps print them bottom-up), and fills M.info:
    signs   [(marker object, text, ink rgb)]   decals the game would paint (preview only)
    night   [(colour, energy, local loc)]      point lights for the night shot
    rover   (x, y, yaw deg) local              a Mk I Scout parked for scale (None = none)
    keep    {pivot names}                      animated/scaled parts for a game export (Door, WorkGlow, Spin, ...)

Run (bpy 5.x venv):
  python blender/build_<id>_marks.py --preview blender/previews/marks/<id>            all marks, every shot
     --shots lineup,hero,game,close,night  --marks 1,2,3,4,5  --res 960  --samples 20
  python blender/build_<id>_marks.py --mark 4 --game        one mark, joined + baked + exported to
                                                            game/models/buildings/marks/<id>_mk4.glb (not wired in)

Shots (files <id>-<shot>.png in the preview folder):
  lineup        all five marks side by side, one per hex (25 apart along +X), same camera
  mk<N>-hero    each mark alone from the same front-right three-quarter camera
  mk<N>-game    each mark alone at the game camera, zoom 50 (PlanetDriver's eased tilt)
  mk5-close     close-up of the Mk V (camera from the script's CLOSE)
  mk5-night     the Mk V at night with its lamps (night lights + every emissive)
The preview materials get a live (unbaked) version of the grime the game export bakes, so the five marks don't need
five atlases.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_kit as K  # noqa: E402
from building_kit import MT, P  # noqa: E402
from mathutils import Vector  # noqa: E402

bpy = K.bpy
argv = K.argv
arg = K.arg
SPACING = 25.0          # one hex apart along +X (HexWidth)
ROMAN = ["", "I", "II", "III", "IV", "V"]


class Mark:
    def __init__(self, building_id, mark, at):
        self.mark = mark
        self.root = K.empty(f"{building_id}_mk{mark}", at)
        self.k = 0
        self.info = {"signs": [], "night": [], "rover": None, "keep": set()}

    def piece(self, name):
        g = K.empty(f"Build{self.k}", (0, 0, 0), self.root)
        g["piece"] = name
        self.k += 1
        return g

    def sign(self, marker, text, ink=(0.92, 0.93, 0.95)):
        self.info["signs"].append((marker, text, ink))

    def lamp(self, colour, energy, loc):
        self.info["night"].append((colour, energy, Vector(loc)))


def setup(building_id):
    shots = arg("--shots", "lineup,hero,game,close,night")
    if "--shots" in argv:      # building_kit validates its own shot names: hide ours from it
        i = argv.index("--shots")
        del argv[i:i + 2]
    cfg = K.setup(building_id)
    cfg["marks"] = [int(s) for s in arg("--marks", "1,2,3,4,5").split(",")]
    cfg["mark"] = int(arg("--mark", "0"))
    cfg["shots"] = [s.strip() for s in shots.split(",") if s.strip()]
    cfg["samples"] = int(arg("--samples", "20"))
    if cfg["game"] and not cfg["mark"]:
        sys.exit("--game needs --mark N")
    if cfg["game"]:
        cfg["out"] = arg("--out") or os.path.join(K.REPO, "game", "models", "buildings", "marks", f"{building_id}_mk{cfg['mark']}.glb")
    return cfg


def extra_materials():
    """Marks-specific palette on top of K.materials(): the Mk I prefab skin, the Mk V polished Starfleet hull, glows."""
    K.mat("prefab", "Prefab", (0.6, 0.59, 0.55), 0.1, 0.62, textured=True)
    K.mat("ribbed", "Ribbed", (0.5, 0.51, 0.52), 0.35, 0.5, textured=True)
    K.mat("white", "Polished", (0.88, 0.89, 0.9), 0.15, 0.28, coat=0.6, textured=True)
    K.mat("navy", "Navy", (0.1, 0.16, 0.3), 0.3, 0.4, textured=True)
    K.mat("solar", "Solar", (0.04, 0.08, 0.18), 0.4, 0.2, coat=0.8)
    K.mat("cyan", "ScienceGlow", (0.5, 0.9, 1.0), 0.0, 0.3, emission=(0.45, 0.85, 1.0), strength=5.0)
    K.mat("hot", "FurnaceGlow", (1.0, 0.55, 0.15), 0.0, 0.4, emission=(1.0, 0.45, 0.1), strength=7.0)
    K.mat("amber", "Amber", (1.0, 0.55, 0.12), 0.0, 0.3, emission=(1.0, 0.5, 0.1), strength=6.0)
    K.mat("work", "WorkLight", (0.8, 0.92, 1.0), 0.0, 0.3, emission=(0.75, 0.9, 1.0), strength=5.0)
    K.mat("strip", "LightStrip", (0.6, 0.85, 1.0), 0.0, 0.3, emission=(0.55, 0.82, 1.0), strength=4.0)
    K.mat("ownerglow", "OwnerGlow", (0.9, 0.12, 0.08), 0.0, 0.4, emission=(0.9, 0.1, 0.06), strength=2.5)
    K.mat("tank", "Tank", (0.84, 0.85, 0.86), 0.2, 0.35, textured=True)
    K.mat("ingot", "Ingot", (0.62, 0.66, 0.74), 0.9, 0.25)
    K.mat("talu", "TransparentAluminum", (0.05, 0.5, 0.9), 0.1, 0.05, emission=(0.02, 0.5, 1.0), strength=2.2)


def live_grime(ground_z=0.3):
    """Cycles-time version of building_kit.grime_material, multiplied into every textured material's colour."""
    for m in K.TEXTURED:
        nt = m.node_tree
        N, L = nt.nodes.new, nt.links.new
        geo = N("ShaderNodeNewGeometry")
        sep = N("ShaderNodeSeparateXYZ")
        L(geo.outputs["Position"], sep.inputs[0])

        def mrange(src, a, b, c=0.0, d=1.0):
            r = N("ShaderNodeMapRange")
            r.inputs["From Min"].default_value, r.inputs["From Max"].default_value = a, b
            r.inputs["To Min"].default_value, r.inputs["To Max"].default_value = c, d
            L(src, r.inputs["Value"])
            return r.outputs["Result"]

        def noise(vec, scale, detail=5.0):
            n = N("ShaderNodeTexNoise")
            n.inputs["Scale"].default_value = scale
            n.inputs["Detail"].default_value = detail
            L(vec, n.inputs["Vector"])
            return n.outputs["Fac"]

        def op(kind, a, b):
            n = N("ShaderNodeMath")
            n.operation = kind
            for i, v in enumerate((a, b)):
                if isinstance(v, (int, float)):
                    n.inputs[i].default_value = v
                else:
                    L(v, n.inputs[i])
            return n.outputs[0]

        pos = geo.outputs["Position"]
        ground = mrange(sep.outputs["Z"], ground_z + 0.05, ground_z + 1.6, 1.0, 0.0)
        dirt = op("MULTIPLY", ground, mrange(noise(pos, 1.4), 0.3, 0.7, 0.35, 1.0))
        ao = N("ShaderNodeAmbientOcclusion")
        ao.samples = 8
        ao.inputs["Distance"].default_value = 0.8
        occ = mrange(ao.outputs["AO"], 0.2, 1.0, 1.0, 0.0)
        st = N("ShaderNodeMapping")
        st.inputs["Scale"].default_value = (7.0, 7.0, 0.35)
        L(pos, st.inputs["Vector"])
        streak = mrange(noise(st.outputs["Vector"], 2.2, 3.0), 0.56, 0.76, 0.0, 1.0)
        amount = op("ADD", op("MULTIPLY", dirt, 0.8), op("MULTIPLY", occ, 0.55))
        amount = op("ADD", amount, op("MULTIPLY", streak, 0.3))
        amount = op("MINIMUM", amount, 0.8)
        mix = N("ShaderNodeMix")
        mix.data_type = "RGBA"
        L(amount, mix.inputs["Factor"])
        mix.inputs[6].default_value = (1, 1, 1, 1)
        mix.inputs[7].default_value = (0.42, 0.35, 0.27, 1)
        mul = N("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        L(mix.outputs[2], mul.inputs[6])
        mul.inputs[7].default_value = (*m["colour"], 1)
        L(mul.outputs[2], nt.nodes["Principled BSDF"].inputs["Base Color"])


# ---------------------------------------------------------------- helpers shared by the three buildings


def dish(name, r, parent, loc=(0, 0, 0), tilt=35.0, yaw=0.0, mat=None):
    """A concave dish on a yoke pivot: returns the pivot (origin at the dish centre, opening tilted up/forward)."""
    piv = K.empty(name, loc, parent)
    piv.rotation_euler = (math.radians(tilt), 0, math.radians(yaw))
    d = r * 0.32
    t = max(0.03, r * 0.035)
    K.lathe(f"{name}Bowl", [(0, 0), (r * 0.35, d * 0.12), (r * 0.7, d * 0.5), (r, d), (r * 0.97, d + t), (r * 0.68, d * 0.5 + t),
                            (r * 0.34, d * 0.12 + t), (0, t)], mat or MT["white"] if "white" in MT else MT["hull"], piv, segs=28)
    K.strut(f"{name}Feed", (0, 0, t), (0, 0, r * 0.62), max(0.02, r * 0.03), MT["trim"], piv, 6)
    K.sphere(f"{name}Horn", max(0.05, r * 0.07), (0, 0, r * 0.62), MT["dark"], piv, 8)
    K.cylinder(f"{name}Hub", r * 0.16, r * 0.25, (0, 0, -r * 0.1), MT["trim"], piv, axis="Z", segments=12)
    return piv


def slab(name, w, d, z, parent, xy=(0, 0), chamfer=0.0, mat=None):
    """A pad slab w x d (chamfered corners when chamfer > 0), top at z."""
    hx, hy, c = w / 2, d / 2, chamfer
    pts = [(-hx + c, -hy), (hx - c, -hy), (hx, -hy + c), (hx, hy - c), (hx - c, hy), (-hx + c, hy), (-hx, hy - c), (-hx, -hy + c)] if c else \
        [(-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy)]
    return prism(name, pts, 0.0, z, mat or MT["concrete"], parent, xy)


def prism(name, pts, z0, z1, mat, parent, xy=(0, 0)):
    """A vertical prism over a convex 2D outline (counter-clockwise)."""
    bm = K.bmesh.new()
    lo = [bm.verts.new((xy[0] + x, xy[1] + y, z0)) for x, y in pts]
    hi = [bm.verts.new((xy[0] + x, xy[1] + y, z1)) for x, y in pts]
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    n = len(pts)
    for k in range(n):
        j = (k + 1) % n
        bm.faces.new((lo[k], lo[j], hi[j], hi[k]))
    return K.mesh_object(name, bm, mat, parent)


def vault(name, w, length, h, centre, parent, mat, axis="Y", segs=12, ribs=0, rib_mat=None, z0=0.0):
    """A half-barrel (quonset) roof/hall: span w across, `length` along axis, rise h, base at z0, centred at centre."""
    bm = K.bmesh.new()
    rings = []
    for e in (-1, 1):
        ring = []
        for k in range(segs + 1):
            a = math.pi * k / segs
            u, z = -math.cos(a) * w / 2, math.sin(a) * h + z0
            ring.append(bm.verts.new((u, e * length / 2, z) if axis == "Y" else (e * length / 2, u, z)))
        rings.append(ring)
    for k in range(segs):
        bm.faces.new((rings[0][k], rings[0][k + 1], rings[1][k + 1], rings[1][k]))
    for ring in rings:
        bm.faces.new(ring)
    K.bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = K.mesh_object(name, bm, mat, parent, centre, smooth=True)
    c = Vector(centre)
    for k in range(ribs):
        t = -length / 2 + length * (k + 0.5) / ribs
        off = Vector((0, t, 0)) if axis == "Y" else Vector((t, 0, 0))
        vault(f"{name}Rib{k}", w + 0.12, 0.14, h + 0.06, c + off, parent, rib_mat or MT["trim"], axis, segs, z0=z0)
    return o


def solar_array(name, x, y, z, parent, n=3, w=1.2, d=0.8, tilt=28, yaw=0.0):
    piv = K.empty(name, P(x, y, z), parent)
    piv.rotation_euler.z = math.radians(yaw)
    for k in range(n):
        px = (k - (n - 1) / 2) * (w + 0.12)
        K.bx(f"{name}Leg{k}", (0.06, 0.06, 0.5), P(px, 0.1, 0.25), MT["steel"], piv)
        K.bx(f"{name}Panel{k}", (w, d, 0.05), P(px, 0, 0.55), MT["solar"], piv, rot=(math.radians(tilt), 0, 0))
        K.bx(f"{name}Frame{k}", (w + 0.04, 0.04, 0.07), P(px, -d / 2 * math.cos(math.radians(tilt)), 0.55 - d / 2 * math.sin(math.radians(tilt))), MT["trim"], piv)
    return piv


def strip_lights(name, a, b, z, n, parent, mat=None, size=(0.3, 0.06, 0.06)):
    a, b = Vector(a), Vector(b)
    for k in range(n):
        p = a.lerp(b, (k + 0.5) / n)
        o = K.bx(f"{name}{k}", size, P(p.x, p.y, z), mat or MT["strip"], parent)
        o.visible_shadow = False


# ---------------------------------------------------------------- scene + renders


def hex_lines(centres):
    OP = K.OP
    for o in [o for o in bpy.data.objects if o.name.startswith("HexLine")]:
        bpy.data.objects.remove(o)
    bright = set(centres)
    allc = set(bright)
    for cx, cy in centres:
        for k in range(6):
            allc.add((round(cx + SPACING * math.cos(math.radians(60 * k)), 3), round(cy + SPACING * math.sin(math.radians(60 * k)), 3)))
    for i, (cx, cy) in enumerate(sorted(allc)):
        cs = OP["hex_corners"](cx, cy)
        on = (cx, cy) in bright
        OP["flat_path"](f"HexLine{i}", cs + [cs[0]], 0.14 if on else 0.1, OP["M"]["hexline" if on else "hexline_dim"], 0.025)


def decals(marks):
    for M in marks:
        for e, body, ink in M.info["signs"]:
            loc, rot, scale = e.matrix_world.decompose()
            m = K.BV["material"](f"Ink{e.name}", ink, 0.0, 0.6)
            t = K.BV["text"](f"Text{e.name}", body, scale.y, (0, 0, 0), (0, 0, 0), m, None, max_width=scale.x)
            t.matrix_world = K.Matrix.Translation(loc) @ rot.to_matrix().to_4x4() @ K.Matrix.Translation((0, 0, 0.006))
            t.parent = M.root
            t.matrix_parent_inverse = M.root.matrix_world.inverted()


def show_only(marks, keep):
    for M in marks:
        hide = keep is not None and M is not keep
        for o in [M.root] + list(M.root.children_recursive):
            o.hide_render = hide


def run(building_id, build, hero, close, lineup_target_z=3.0, ground_z=0.3):
    """hero/close = (pos, target, lens) relative to a mark's origin."""
    cfg = setup(building_id)
    K.materials()
    extra_materials()
    if cfg["game"]:
        M = Mark(building_id, cfg["mark"], (0, 0, 0))
        build(cfg["mark"], M)
        bpy.context.view_layer.update()
        keep = set(M.info["keep"]) | {o.name for o in M.root.children if o.name.startswith("Build")}
        K.join_pieces(M.root, keep, set())
        K.bake_grime(M.root, set(), f"{building_id.title()}Mk{cfg['mark']}Grime", ground_z)
        K.export(M.root, cfg["out"])
        return
    marks = []
    for m in cfg["marks"]:
        M = Mark(building_id, m, ((m - 3) * SPACING, 0, 0))
        build(m, M)
        marks.append(M)
        print(f"Mk {ROMAN[m]}: {len([k for k in M.root.children if k.name.startswith('Build')])} pieces,",
              K.tri_count(M.root), "triangles", flush=True)
    bpy.context.view_layer.update()
    live_grime(ground_z)
    if not cfg["preview"]:
        return
    for m in list(MT.values()):
        m.name = m.name + "_B"
    K.OP["materials"]()
    K.OP["stage"]()
    hex_lines([(M.root.location.x, 0.0) for M in marks])
    decals(marks)
    for M in marks:
        if M.info["rover"]:
            x, y, yaw = M.info["rover"]
            rv = K.BV["rover_mk1"](K.BV["palette"]())
            rv.location = (M.root.location.x + x, y, 0.0)
            rv.rotation_euler.z = math.radians(yaw)
            rv.parent = M.root
            rv.matrix_parent_inverse = M.root.matrix_world.inverted()
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = cfg["samples"]
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    if cfg["threads"]:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = cfg["threads"]
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    K.link(cam)
    scene.camera = cam
    cam.data.clip_end = 3000
    folder = cfg["preview"]
    os.makedirs(folder, exist_ok=True)
    sun = bpy.data.objects["Sun"]
    bg = scene.world.node_tree.nodes["Background"]
    night_lights = []

    def shoot(name, pos, target, lens=None, angle_y=None, w=None, h=None):
        scene.render.resolution_x = w
        scene.render.resolution_y = h
        cam.data.type = "PERSP"
        if lens:
            cam.data.sensor_fit = "AUTO"
            cam.data.lens = lens
        if angle_y:
            cam.data.sensor_fit = "VERTICAL"
            cam.data.angle_y = math.radians(angle_y)
        cam.location = pos
        cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        path = os.path.join(folder, f"{building_id}-{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("rendered", path, flush=True)

    def night(on, M=None):
        sun.hide_render = on
        bg.inputs["Strength"].default_value = 0.004 if on else 0.12
        scene.view_settings.exposure = 0.3 if on else -0.6
        for lt in night_lights:
            bpy.data.objects.remove(lt)
        night_lights.clear()
        if on:
            for i, (colour, energy, loc) in enumerate(M.info["night"]):
                lt = bpy.data.objects.new(f"N{i}", bpy.data.lights.new(f"N{i}", "POINT"))
                lt.data.energy = energy
                lt.data.color = colour
                lt.data.shadow_soft_size = 0.3
                lt.location = M.root.location + loc
                K.link(lt)
                night_lights.append(lt)

    res = cfg["res"]
    shots = cfg["shots"]
    if "lineup" in shots:
        night(False)
        show_only(marks, None)
        n = len(marks)
        span = (n - 1) * SPACING + 20
        dist = span * 1.12
        shoot("lineup", (0, -dist * 0.84, dist * 0.5), (0, -2.0, lineup_target_z), lens=34, w=int(res * 2), h=int(res * 0.6))
    for M in marks:
        o = M.root.location
        if "hero" in shots:
            night(False)
            show_only(marks, M)
            pos, tgt, lens = hero
            shoot(f"mk{M.mark}-hero", o + Vector(pos), o + Vector(tgt), lens=lens, w=res, h=int(res * 2 / 3))
        if "game" in shots:
            night(False)
            show_only(marks, M)
            pos, tgt = K.game_cam(50.0)
            shoot(f"mk{M.mark}-game", o + Vector(pos), o + Vector(tgt), angle_y=45, w=res, h=int(res * 9 / 16))
        if M.mark == max(cfg["marks"]):
            if "close" in shots:
                night(False)
                show_only(marks, M)
                pos, tgt, lens = close
                shoot(f"mk{M.mark}-close", o + Vector(pos), o + Vector(tgt), lens=lens, w=res, h=int(res * 2 / 3))
            if "night" in shots:
                show_only(marks, M)
                night(True, M)
                pos, tgt, lens = hero
                p = Vector(pos)
                shoot(f"mk{M.mark}-night", o + Vector((-p.x * 0.7, p.y, p.z * 0.9)), o + Vector(tgt), lens=lens, w=res, h=int(res * 2 / 3))
                night(False)
    show_only(marks, None)
