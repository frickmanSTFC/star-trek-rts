"""Pre-warp drop ship MOCKUP (Dawn of Warp era, 2063-2150): three original cargo-lander designs for the human
expedition's surface deliveries, to replace the 24th-century look of Halyard 07 (build_dropship.py: nacelles,
impulse grilles) in pre-warp matches. Owner 2026-11-10: "on the pre-warp, the drop ships [shouldn't] have nacelles.
Make them look less enterprise / warp era. show me mockup".

Same role and roughly the same footprint as Halyard (a hex is 25 across): a bay that takes the mining crawler
(5.6 x 2.7 x 3.1) or a rover, a ramp, four landing legs with pads, four downward engine bells where the game's lift
thrusters are (the game already flies drop ships as rocket landers: RocketPlume.cs / DropShipView.cs) and small RCS
quads in place of the aft impulse exhausts. Earth-industrial look: white/grey panels, orange insulation, gold and black
foil, exposed piping, stencilled hull text, hazard stripes, the player colour as the "Owner" band.

  A  "Lander"     octagonal descent stage in gold/black foil, four exposed spherical tanks, a boxy pressurised cargo
                  module with a faceted crew cab on top, four splayed truss legs with dish pads, a bell under each
                  corner, a folding two-piece ramp. 8.4 across the stage, 11.8 over the pads, 8.0 to the cab roof.
  B  "Hopper"     upright reusable rocket lander, 7.2 wide and 13.3 tall: stainless cargo barrel over a dark tiled
                  skirt, white upper tank section, grid fins, four deployable legs, a cluster of four bells under the
                  base, a cargo door housing on one side whose door unfolds into a two-piece ramp.
  C  "Freighter"  horizontal lifting-body cargo hauler, 13.7 long: blunt nose, ceramic-tile belly, rear ramp like
                  Halyard's, four vertical engines hung exposed on outrigger beams, two external foam tanks on top,
                  utilitarian panels and stencils, no fins.

GAME CONTRACT KEPT (Blender +Y = front, +Z up, 1 unit = 1 world unit; names as in build_dropship.py):
  Ramp            empty on the hinge line at the bay floor's door edge (always on the -Y face = Godot +Z, "aft").
                  Rest = closed. Open = rotate about local +X (Godot: rest * Basis(Right, angle), same sign).
                  The game uses 109.6 degrees; each option's own open angle is printed (A and B are tuned to land
                  their toe within a few cm at 109.6; C's single slab is Halyard's 109.6 exactly).
  RampTip         (A, B only; NEW) empty at the far end of the main ramp slab; the ramp's second half is built folded
                  back onto the walking face and unfolds +180 degrees about local X once the ramp is down.
  Leg0..Leg3      hinge empties (0 front-left -X+Y, 1 front-right, 2 rear-left, 3 rear-right). Rest = deployed. Their
                  rest yaw points the leg so the game's fold (front -90, rear +90 about local X) swings it up the
                  hull (on Halyard the rest is identity; the game already composes rest * fold).
  Foot0..Foot3    ankle empties (children of LegK) carrying the pads.
  Thruster0..3    the four lift engines: each is the glowing INSIDE surface of an engine bell (own material ThrusterK),
                  so its bounding box bottom = the bell's exit plane and its width = the exit diameter (the game's
                  RocketPlume reads both from the mesh AABB).
  Thruster4/5     the glowing nozzle of an aft-facing RCS quad on each side (aft = -Y), same AABB rule.
  BayLight        the bay's ceiling strips. Beacon: amber roof lamp. Lamp / TailLamp materials as on the rovers.
  NameL/NameR/NameRear/NumberL/NumberR   decal markers with --blank (build_vehicles.py MARKERS).
  NacelleGlow0/1  gone (pre-warp). The game skips missing ones.
  Material "Owner" = the player-colour band.

Run (bpy 5.x; see CLAUDE.md "Blender here"):
  /home/user/.bpyvenv/bin/python blender/build_dropship_prewarp.py --preview blender/previews/dropship_prewarp
      every option x shot + both lineups (11 renders at 1080 x 720; with --samples 40 about 6 min on 4 cores)
  ... --options a --shots hero --res 640 --samples 8     (quick look at one option)
  ... --options a,b,c --shots lineup,lineup-game         (just the lineups)
  python3 blender/build_dropship_prewarp.py --sheet blender/previews/dropship_prewarp   (contact sheet; PIL, not bpy)

Flags:
  --options a,b,c     which designs to build (default all three)
  --shots ...         hero (landed 3/4, ramp open, mining crawler driving off), game (build_dropship.py's game-camera
                      preview: 55 degrees down from 46 away, 35 mm, ship turned 25 degrees, a Mk II rover and a hex
                      outline for scale), descent (legs out, engines firing), lineup (Halyard + A/B/C side by side,
                      each on its own hex), lineup-game (the same from the game's landing camera: 55 degrees down,
                      170 away, a 51 mm crop of it). Default: all of them.
  --preview <dir>     write <dir>/prewarp-<option>-<shot>.png and prewarp-<lineup shot>.png
  --res, --samples    render width (height 2/3) and Cycles samples
  --blank             decals as the game's marker empties (only with a single option)
  --blend <file>      also save the built scene (layout checks)
"""
import math
import os
import re
import sys
import time

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


# ---------------------------------------------------------------- contact sheet (plain python3 + PIL, no bpy)

SHEET_ROWS = [("a", "Lander"), ("b", "Hopper"), ("c", "Freighter")]
SHEET_COLS = ["hero", "game", "descent", "flight"]

if "--sheet" in argv:
    from PIL import Image, ImageDraw
    folder = arg("--sheet")
    tw = 480
    th = tw * 2 // 3
    lw = tw * len(SHEET_COLS) // 2  # the two lineups share the last row
    sheet = Image.new("RGB", (tw * len(SHEET_COLS), th * 3 + lw * 2 // 3), (20, 22, 26))
    draw = ImageDraw.Draw(sheet)

    def put(path, box, label):
        im = Image.open(path).convert("RGB").resize((box[2] - box[0], box[3] - box[1]), Image.LANCZOS)
        sheet.paste(im, box[:2])
        draw.rectangle((box[0] + 8, box[1] + 8, box[0] + 16 + 7 * len(label), box[1] + 26), fill=(10, 12, 16))
        draw.text((box[0] + 12, box[1] + 11), label.upper(), fill=(235, 235, 240))

    for r, (key, title) in enumerate(SHEET_ROWS):
        for c, shot in enumerate(SHEET_COLS):
            put(os.path.join(folder, f"prewarp-{key}-{shot}.png"), (c * tw, r * th, (c + 1) * tw, (r + 1) * th),
                f"{key.upper()} {title} - {shot}")
    y0 = th * 3
    put(os.path.join(folder, "prewarp-lineup.png"), (0, y0, lw, y0 + lw * 2 // 3), "lineup: Halyard 07 / A / B / C")
    put(os.path.join(folder, "prewarp-lineup-game.png"), (lw, y0, 2 * lw, y0 + lw * 2 // 3), "lineup: landing camera")
    out = os.path.join(folder, "prewarp-sheet.png")
    sheet.save(out, optimize=True)
    print("wrote", out, sheet.size)
    sys.exit(0)

import bpy  # noqa: E402  first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)

OPTIONS = [s.strip().lower() for s in arg("--options", "a,b,c").split(",") if s.strip()]
ALL_SHOTS = ["hero", "game", "descent", "flight", "lineup", "lineup-game", "glb"]
DEFAULT_SHOTS = ["hero", "game", "descent", "flight", "lineup", "lineup-game"]
SHOTS = [s.strip() for s in arg("--shots", ",".join(DEFAULT_SHOTS)).split(",") if s.strip()]
PREVIEW = arg("--preview")
RES = int(arg("--res", "1080"))
SAMPLES = int(arg("--samples", "24"))
GAME = "--game" in argv
BAKE_RES = int(arg("--bake-res", "2048"))
BLANK = "--blank" in argv or GAME
if set(OPTIONS) - {"a", "b", "c"}:
    sys.exit("--options takes a, b and c")
if set(SHOTS) - set(ALL_SHOTS):
    sys.exit(f"--shots takes {', '.join(ALL_SHOTS)}")
if GAME and PREVIEW:
    sys.exit("--game builds the game models only; render previews in a second run (--shots glb checks the exports)")
if BLANK and not GAME and len(OPTIONS) > 1:
    sys.exit("--blank needs a single --options value (object names must be unique)")


def load_build_vehicles():
    """build_dropship.py's loader: exec build_vehicles.py without its trailing main() and with none of our flags."""
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

RAMP_GAME = math.radians(109.6)  # DropShipView.RampOpenDegrees
SIN_GAME = math.sin(RAMP_GAME - math.pi / 2)

# ---------------------------------------------------------------- materials


def node_mat(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    return m, nt, nt.nodes["Principled BSDF"]


def _n(nt, kind, **inputs):
    n = nt.nodes.new(kind)
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def mix_col(nt, fac, a, b):
    """ShaderNodeMix (RGBA): fac socket or value, a/b sockets or colour tuples. Returns the result socket."""
    n = nt.nodes.new("ShaderNodeMix")
    n.data_type = "RGBA"
    if isinstance(fac, (int, float)):
        n.inputs[0].default_value = fac
    else:
        nt.links.new(fac, n.inputs[0])
    for idx, v in ((6, a), (7, b)):
        if isinstance(v, tuple):
            n.inputs[idx].default_value = (*v[:3], 1.0)
        else:
            nt.links.new(v, n.inputs[idx])
    return n.outputs[2]


def math_op(nt, op, a, b=None, clamp=False):
    n = nt.nodes.new("ShaderNodeMath")
    n.operation = op
    n.use_clamp = clamp
    for idx, v in ((0, a), (1, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            n.inputs[idx].default_value = v
        else:
            nt.links.new(v, n.inputs[idx])
    return n.outputs[0]


def paint(name, colour, metallic=0.1, rough=0.45, grime=0.35, grime_col=(0.3, 0.25, 0.18), height=2.6, mottle=0.08,
          streaks=0.12, coat=0.0):
    """Painted panels with procedural wear (no bake): soft mottling, grime rising from the ground (world z 0 ->
    height), faint vertical rain streaks."""
    m, nt, p = node_mat(name)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Position"], sep.inputs[0])
    hz = _n(nt, "ShaderNodeMapRange", **{"From Min": 0.0, "From Max": height, "To Min": 1.0, "To Max": 0.0})
    nt.links.new(sep.outputs["Z"], hz.inputs[0])
    n1 = _n(nt, "ShaderNodeTexNoise", Scale=0.7, Detail=6.0)
    nt.links.new(tc.outputs["Object"], n1.inputs["Vector"])
    n2 = _n(nt, "ShaderNodeTexNoise", Scale=4.0, Detail=3.0)
    nt.links.new(tc.outputs["Object"], n2.inputs["Vector"])
    base = mix_col(nt, math_op(nt, "MULTIPLY", n2.outputs["Factor"], mottle * 2), colour, tuple(c * 0.82 for c in colour))
    dirt = math_op(nt, "MULTIPLY", math_op(nt, "MULTIPLY", hz.outputs[0], n1.outputs["Factor"]), 1.6 * grime, clamp=True)
    col = mix_col(nt, dirt, base, grime_col)
    if streaks:
        mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (6.0, 6.0, 0.35)
        nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
        n3 = _n(nt, "ShaderNodeTexNoise", Scale=2.0, Detail=2.0)
        nt.links.new(mp.outputs[0], n3.inputs["Vector"])
        s = _n(nt, "ShaderNodeMapRange", **{"From Min": 0.56, "From Max": 0.78, "To Min": 0.0, "To Max": streaks})
        nt.links.new(n3.outputs["Factor"], s.inputs[0])
        col = mix_col(nt, s.outputs[0], col, grime_col)
    nt.links.new(col, p.inputs["Base Color"])
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = rough
    if coat:
        p.inputs["Coat Weight"].default_value = coat
    return m


def foil(name, colour, metallic=1.0, rough=0.26, crinkle=0.45):
    """Crinkled thermal blanket: Voronoi + noise bump, colour wandering toward a darker tone."""
    m, nt, p = node_mat(name)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    vo = _n(nt, "ShaderNodeTexVoronoi", Scale=7.0, Randomness=1.0)
    nt.links.new(tc.outputs["Object"], vo.inputs["Vector"])
    no = _n(nt, "ShaderNodeTexNoise", Scale=12.0, Detail=8.0, Roughness=0.6)
    nt.links.new(tc.outputs["Object"], no.inputs["Vector"])
    h = math_op(nt, "ADD", vo.outputs["Distance"], math_op(nt, "MULTIPLY", no.outputs["Factor"], 0.7))
    bump = _n(nt, "ShaderNodeBump", Strength=crinkle, Distance=0.03)
    nt.links.new(h, bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    nt.links.new(mix_col(nt, no.outputs["Factor"], tuple(c * 0.6 for c in colour), colour), p.inputs["Base Color"])
    p.inputs["Metallic"].default_value = metallic
    rr = _n(nt, "ShaderNodeMapRange", **{"To Min": rough * 0.6, "To Max": rough * 1.6})
    nt.links.new(vo.outputs["Distance"], rr.inputs[0])
    nt.links.new(rr.outputs[0], p.inputs["Roughness"])
    return m


def tiles(name, scale=2.6, wrap=False, front=False):
    """Ceramic heat-shield tiles: a brick grid of near-black tiles with a few lighter ones. wrap: map (x + y, z) for a
    vertical cylinder, front: (x, z) for a forward-facing nose cap, else (y, x) for a belly."""
    m, nt, p = node_mat(name)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    if wrap:
        nt.links.new(math_op(nt, "ADD", sep.outputs["X"], sep.outputs["Y"]), comb.inputs["X"])
        nt.links.new(sep.outputs["Z"], comb.inputs["Y"])
    elif front:
        nt.links.new(sep.outputs["X"], comb.inputs["X"])
        nt.links.new(sep.outputs["Z"], comb.inputs["Y"])
    else:
        nt.links.new(sep.outputs["Y"], comb.inputs["X"])
        nt.links.new(sep.outputs["X"], comb.inputs["Y"])
    br = _n(nt, "ShaderNodeTexBrick", Scale=scale, **{"Mortar Size": 0.035, "Brick Width": 0.5, "Row Height": 0.25,
                                                       "Color1": (0.05, 0.05, 0.055, 1), "Color2": (0.09, 0.088, 0.085, 1),
                                                       "Mortar": (0.32, 0.31, 0.3, 1), "Bias": -0.2})
    br.offset = 0.5
    nt.links.new(comb.outputs[0], br.inputs["Vector"])
    nt.links.new(br.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = 0.85
    bump = _n(nt, "ShaderNodeBump", Strength=0.25, Distance=0.02)
    nt.links.new(br.outputs["Factor"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    return m


def hazard(name, period=0.36):
    """Yellow/black diagonal warning stripes (object space)."""
    m, nt, p = node_mat(name)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wv = _n(nt, "ShaderNodeTexWave", Scale=2 * math.pi / (10 * period), Distortion=0.0)
    wv.wave_type = "BANDS"
    wv.bands_direction = "DIAGONAL"
    nt.links.new(tc.outputs["Object"], wv.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].color = (0.02, 0.02, 0.02, 1)
    ramp.color_ramp.elements[1].position = 0.5
    ramp.color_ramp.elements[1].color = (0.95, 0.68, 0.04, 1)
    nt.links.new(wv.outputs["Factor"], ramp.inputs[0])
    nt.links.new(ramp.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = 0.55
    return m


def plume_material(name, colour, strength, falloff=2.5):
    """Soft additive rocket flame: brightest near the nozzle (generated Z = 1 at the top of the cone) and where the
    cone faces the camera (build_dropship.py's plume, recoloured)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    coord = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(coord.outputs["Generated"], sep.inputs[0])
    lw = _n(nt, "ShaderNodeLayerWeight", Blend=0.5)
    face = math_op(nt, "POWER", math_op(nt, "SUBTRACT", 1.0, lw.outputs["Facing"]), 1.6)
    along = math_op(nt, "POWER", sep.outputs["Z"], falloff)
    amp = math_op(nt, "MULTIPLY", math_op(nt, "MULTIPLY", face, along), strength)
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (*colour, 1)
    nt.links.new(amp, emit.inputs["Strength"])
    add = nt.nodes.new("ShaderNodeAddShader")
    nt.links.new(nt.nodes.new("ShaderNodeBsdfTransparent").outputs[0], add.inputs[0])
    nt.links.new(emit.outputs[0], add.inputs[1])
    nt.links.new(add.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    return m


def dust_material():
    """Translucent dust for the descent shots: diffuse tan, alpha from noise."""
    m = bpy.data.materials.new("Dust")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    tc = nt.nodes.new("ShaderNodeTexCoord")
    no = _n(nt, "ShaderNodeTexNoise", Scale=1.6, Detail=6.0)
    nt.links.new(tc.outputs["Object"], no.inputs["Vector"])
    a = _n(nt, "ShaderNodeMapRange", **{"From Min": 0.38, "From Max": 0.7, "To Min": 0.0, "To Max": 0.55})
    nt.links.new(no.outputs["Factor"], a.inputs[0])
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(a.outputs[0], mix.inputs[0])
    nt.links.new(nt.nodes.new("ShaderNodeBsdfTransparent").outputs[0], mix.inputs[1])
    dif = nt.nodes.new("ShaderNodeBsdfDiffuse")
    dif.inputs["Color"].default_value = (0.55, 0.43, 0.3, 1)
    nt.links.new(dif.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    return m


def thruster_material(k, aft=False):
    """Each engine's inner surface gets its own material (ThrusterK) so the game can light them one by one. Chemical
    rockets: orange-white, a dull warm glow at idle."""
    colour = (1.0, 0.5, 0.16) if not aft else (1.0, 0.62, 0.3)
    return material(f"Thruster{k}", tuple(c * 0.25 for c in colour), 0.0, 0.35, emission=colour, strength=0.25)


# Owner 2026-11-10: "White, Space Shuttle red/orange, hull names random like before". White hull, the external tank's
# rust orange (~#C8632A) as the main accent (tanks, foam, stripes), black thermal tiles where the heat is, light grey
# details; gold foil only as a small accent on A; the player colour stays a thin "Owner" line.
WHITE = (0.82, 0.82, 0.8)
ORANGE = (0.56, 0.13, 0.026)   # linear for sRGB ~#C8632A
LIGHTGREY = (0.6, 0.61, 0.62)


def prewarp_palette():
    P = V["tier_palette"](V["palette"]())
    P.update({
        # "...Paint": the game's achievement paint/finish (CosmeticLook) dresses materials named Hull or *Paint.
        "white": paint("WhitePaint", WHITE, 0.05, 0.48, coat=0.1),
        "grey": paint("PaintGrey", LIGHTGREY, 0.12, 0.5),
        "darkgrey": paint("PaintDark", (0.16, 0.165, 0.17), 0.3, 0.55, grime=0.2),
        "stainless": paint("Stainless", (0.66, 0.67, 0.7), 0.95, 0.28, grime=0.25, mottle=0.12, streaks=0.18),
        "foam": paint("FoamOrange", ORANGE, 0.0, 0.82, grime=0.22, grime_col=(0.22, 0.08, 0.03), mottle=0.16, streaks=0.06),
        "goldfoil": foil("FoilGold", (1.0, 0.72, 0.3), 1.0, 0.22),
        "blackfoil": foil("FoilBlack", (0.035, 0.032, 0.03), 0.55, 0.32, 0.35),
        "silverfoil": foil("FoilSilver", (0.82, 0.83, 0.86), 1.0, 0.22, 0.4),
        "tiles": tiles("TileBelly"),
        "tilewrap": tiles("TileSkirt", 2.2, wrap=True),
        "tilenose": tiles("TileNose", 2.6, front=True),
        "hazard": hazard("Hazard"),
        "nozzle": material("Nozzle", (0.19, 0.17, 0.16), 0.85, 0.4),
        "nozzlehot": material("NozzleHot", (0.32, 0.2, 0.14), 0.9, 0.35),
        "copper": material("Copper", (0.78, 0.42, 0.26), 1.0, 0.3),
        "grating": material("Grating", (0.17, 0.17, 0.18), 0.7, 0.55),
        "stencil": material("Stencil", (0.035, 0.035, 0.04), 0.0, 0.6),
        "stencilred": material("StencilRed", (0.6, 0.06, 0.04), 0.0, 0.6),
        "stencilwhite": material("StencilWhite", (0.85, 0.85, 0.83), 0.0, 0.6),
        "radiator": material("Radiator", (0.86, 0.87, 0.88), 0.2, 0.3),
        "deck": material("Deck", (0.2, 0.205, 0.215), 0.7, 0.45),
        "liner": material("BayLiner", (0.55, 0.56, 0.58), 0.2, 0.6),
        "baylight": material("BayLight", (1.0, 0.95, 0.85), 0.0, 0.3, emission=(1.0, 0.94, 0.82), strength=3.0),
        "navgreen": material("NavGreen", (0.05, 0.9, 0.2), 0.0, 0.3, emission=(0.05, 1.0, 0.2), strength=3.0),
        "carbon": material("Carbon", (0.07, 0.072, 0.078), 0.2, 0.4),
    })
    return P


# ---------------------------------------------------------------- geometry helpers


def lathe(name, prof, mat, parent=None, loc=(0, 0, 0), seg=32, smooth=True, inward=False, closed=None):
    """Revolve a profile [(r, z)] about local Z. r == 0 makes a pole. Open shells face outward (inward=True: inward)."""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), z))
                          for i in range(seg)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1:
                bm.faces.new((a[0], b[j], b[i]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    if closed is None:
        closed = len(rings[0]) == 1 and len(rings[-1]) == 1
    if closed:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    else:
        bm.normal_update()
        f = max(bm.faces, key=lambda f: f.calc_area())
        c = f.calc_center_median()
        out = Vector((c.x, c.y, 0)).normalized()
        if (f.normal.dot(out) < 0) != inward:
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return mesh_object(name, bm, mat, parent, loc, smooth=smooth)


def sphere(name, r, loc, mat, parent=None, seg=28, rings=14, squash=1.0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
    for v in bm.verts:
        v.co.z *= squash
    return mesh_object(name, bm, mat, parent, loc, smooth=True)


def cone_dir(name, base, direction, r1, r2, length, mat, parent=None, segments=12):
    """A cone (r1 at base, r2 at the tip end) from `base` along `direction`."""
    d = Vector(direction).normalized()
    o = cylinder(name, r1, length, Vector(base) + d * length / 2, mat, parent, axis="Z", segments=segments, radius2=r2)
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()  # create_cone puts radius1 at local -Z = the base
    return o


def pipe(name, pts, radius, mat, parent=None, joints=True):
    """A pipe run through points: struts plus a ball at each bend."""
    out = []
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        out.append(strut(f"{name}{i}", a, b, radius, mat, parent, segments=10))
    if joints:
        for i, p in enumerate(pts[1:-1]):
            out.append(sphere(f"{name}J{i}", radius * 1.25, p, mat, parent, seg=10, rings=6))
    return out


def ring_band(name, r, z0, z1, mat, parent=None, seg=48, loc=(0, 0, 0)):
    """A thin cylindrical band (open tube) at radius r between z0 and z1."""
    return lathe(name, [(r, z1), (r, z0)], mat, parent, loc, seg=seg)


def cyl_patch(name, R, phi0, phi1, z0, z1, mat, parent=None, nu=10, lift=0.012):
    """A curved quad lying on a vertical cylinder of radius R (centred on the parent's Z axis): windows, decals."""
    bm = bmesh.new()
    grid = []
    for j in range(2):
        z = z0 + (z1 - z0) * j
        row = []
        for i in range(nu + 1):
            a = phi0 + (phi1 - phi0) * i / nu
            row.append(bm.verts.new(((R + lift) * math.cos(a), (R + lift) * math.sin(a), z)))
        grid.append(row)
    for i in range(nu):
        bm.faces.new((grid[0][i], grid[0][i + 1], grid[1][i + 1], grid[1][i]))
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    if bm.faces[0].normal.dot(Vector((math.cos(phi0), math.sin(phi0), 0))) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    o = mesh_object(name, bm, mat, parent)
    o.visible_shadow = False
    return o


def text_on_cylinder(name, body, size, R, phi, z, mat, parent=None, lift=0.012):
    """Stencil wrapped round a vertical cylinder: text centre at azimuth phi (facing outward), reading left to right."""
    me = V["text_mesh"](name, body, size)
    for v in me.vertices:
        x, y, w = v.co
        a = phi + x / R
        r = R + lift + w
        v.co = (r * math.cos(a), r * math.sin(a), z + y)
    me.materials.clear()
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    o.visible_shadow = False
    return link(o, parent)


def stencil(name, body, size, loc, facing, mat, parent=None, max_width=None, read=None):
    """Flat stencil text facing +X / -X / +Y / -Y / +Z ("up": reading along `read`, default +X)."""
    rots = {"+X": (math.radians(90), 0, math.radians(90)), "-X": (math.radians(90), 0, math.radians(-90)),
            "-Y": (math.radians(90), 0, 0), "+Y": (math.radians(90), 0, math.radians(180)),
            "+Z": (0, 0, read or 0.0)}
    return V["text"](name, body, size, loc, rots[facing], mat, parent, max_width=max_width)


def hz_box(name, size, loc, P, parent, rot=(0, 0, 0)):
    o = box(name, size, loc, P["hazard"], parent, 0, rot)
    o.visible_shadow = False
    return o


def seam(name, size, loc, P, parent, rot=(0, 0, 0)):
    o = box(name, size, loc, P["dark"], parent, 0, rot)
    o.visible_shadow = False
    return o


def octagon(name, apothem, z0, z1, mat, parent=None, bev=0.0):
    """Octagonal prism with flats facing +-X, +-Y and the diagonals."""
    R = apothem / math.cos(math.radians(22.5))
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=R, radius2=R, depth=z1 - z0)
    for v in bm.verts:
        a = math.atan2(v.co.y, v.co.x) + math.radians(22.5)
        rr = math.hypot(v.co.x, v.co.y)
        v.co.x, v.co.y = rr * math.cos(a), rr * math.sin(a)
    o = mesh_object(name, bm, mat, parent, (0, 0, (z0 + z1) / 2))
    if bev:
        bevel(o, bev, 2, 30)
    return o


def cut(host, name, size, loc, parent):
    """Boolean the box out of host (the cutter stays hidden, parented so the whole ship moves together)."""
    c = box(name, size, loc, host.data.materials[0], parent, 0)
    c.hide_render = c.hide_viewport = True
    c.display_type = "WIRE"
    mod = host.modifiers.new(name, "BOOLEAN")
    mod.operation, mod.solver, mod.object = "DIFFERENCE", "EXACT", c
    return c


def bell(k, xy, z_throat, r_t, r_e, length, P, parent, name=None, chamber=True):
    """Regeneratively cooled engine: injector dome + chamber + throat + bell shell (Nozzle), cooling bands, and the
    glowing inner surface ThrusterK (the game's lift flame anchor: AABB bottom = exit plane)."""
    name = name or f"Engine{k}"
    x, y = xy
    prof = []
    for i in range(11):
        t = i / 10
        prof.append((r_t + (r_e - r_t) * (1 - (1 - t) ** 1.9), z_throat - length * t))
    shell = lathe(f"{name}Bell", prof, P["nozzle"], parent, (x, y, 0), seg=32)
    m = shell.modifiers.new("Wall", "SOLIDIFY")
    m.thickness, m.offset = 0.035, -1
    inner = lathe(f"Thruster{k}", [(r - 0.045, z) for r, z in prof[1:]], thruster_material(k), parent, (x, y, 0), seg=32,
                  inward=True)
    inner.visible_shadow = False
    lathe(f"{name}Lip", [(r_e + 0.03, z_throat - length + 0.06), (r_e + 0.03, z_throat - length)], P["nozzlehot"], parent,
          (x, y, 0), seg=32)
    for i, t in enumerate((0.3, 0.62)):
        r = r_t + (r_e - r_t) * (1 - (1 - t) ** 1.9) + 0.015
        lathe(f"{name}Band{i}", [(r, z_throat - length * t + 0.035), (r, z_throat - length * t - 0.035)], P["dark"], parent,
              (x, y, 0), seg=32)
    if chamber:
        rc = r_t * 1.7
        lathe(f"{name}Chamber", [(0, z_throat + 0.62), (rc * 0.7, z_throat + 0.6), (rc, z_throat + 0.5), (rc, z_throat + 0.2),
                                 (r_t * 1.05, z_throat - 0.02), (0, z_throat - 0.02)], P["copper"], parent, (x, y, 0), seg=24)
        cylinder(f"{name}Pump", 0.11, 0.42, (x + rc + 0.14, y, z_throat + 0.42), P["steel"], parent, axis="Z", segments=14)
        pipe(f"{name}Feed", [(x + rc + 0.14, y, z_throat + 0.2), (x + rc * 0.6, y, z_throat + 0.12)], 0.035, P["steel"], parent)
        for s in (-1, 1):
            strut(f"{name}Gimbal{s}", (x + s * rc * 0.7, y + rc * 0.7, z_throat + 0.95), (x + s * rc * 0.6, y, z_throat + 0.45),
                  0.03, P["gunmetal"], parent, segments=8)
    return inner


def rcs_quad(name, loc, out, P, parent, glow_k=None, aft=Vector((0, -1, 0))):
    """Reaction-control quad: a block with small nozzles pointing out, up, down and both ways along the hull.
    glow_k: the nozzle nearest aft (-Y) points exactly aft and gets a glowing exit disc ThrusterK (the game's aft
    flame anchor)."""
    loc, out = Vector(loc), Vector(out).normalized()
    side = out.cross(Vector((0, 0, 1))).normalized()
    blk = box(f"{name}Block", (0.34, 0.34, 0.34), loc, P["gunmetal"], parent, 0.04)
    blk.rotation_euler = (0, 0, math.atan2(out.y, out.x))
    dirs = [out, Vector((0, 0, 1)), Vector((0, 0, -1)), side, -side]
    if glow_k is not None:
        i = 3 if side.dot(aft) > (-side).dot(aft) else 4
        dirs[i] = aft.normalized()
    for i, d in enumerate(dirs):
        cone_dir(f"{name}Noz{i}", loc + d * 0.16, d, 0.045, 0.085, 0.16, P["nozzle"], parent, 10)
    if glow_k is not None:
        d = aft.normalized()
        g = cylinder(f"Thruster{glow_k}", 0.07, 0.02, loc + d * 0.31, thruster_material(glow_k, aft=True), parent, axis="Z",
                     segments=12)
        g.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        g.visible_shadow = False
    return blk


def ladder(name, a, b, across, width, P, parent, rungs=0.32):
    """A two-rail ladder from a to b (points on its centreline), the rails `width` apart along `across`."""
    a, b = Vector(a), Vector(b)
    d = b - a
    side = Vector(across).normalized() * width / 2
    strut(f"{name}RailL", a - side, b - side, 0.025, P["steel"], parent, 8)
    strut(f"{name}RailR", a + side, b + side, 0.025, P["steel"], parent, 8)
    n = max(2, int(d.length / rungs))
    for i in range(1, n):
        p = a + d * (i / n)
        strut(f"{name}Rung{i}", p - side, p + side, 0.018, P["steel"], parent, 6)


def leg_frame(pos, out_xy, front):
    """Leg empty matrix: at pos, yawed so the game's fold (front -90 / rear +90 about local X) swings it up the hull:
    front legs extend toward local -Y, rear legs toward local +Y. Returns (yaw, matrix)."""
    dx, dy = out_xy
    yaw = math.atan2(dx, -dy) if front else math.atan2(-dx, dy)
    return yaw, Matrix.Translation(pos) @ Matrix.Rotation(yaw, 4, "Z")


def pad_dish(name, r, P, parent, loc=(0, 0, 0)):
    """Landing pad: a shallow dish with a rim, bottom at local z 0."""
    o = lathe(name, [(0, 0.17), (r * 0.82, 0.21), (r, 0.19), (r * 0.98, 0.09), (r * 0.7, 0.025), (0, 0.0)], P["dark"], parent,
              loc, seg=28)
    return o


def folding_ramp(P, root, hinge, hw, L1, L2, T, mat):
    """Two-piece ramp door on the -Y face: the main slab stands up in the door (inner/walking face = local +Y through
    the hinge), its second half RampTip lies folded back on the walking face and unfolds +180 degrees about local X.
    Returns (Ramp, RampTip, toe points in Ramp-local (y, z) once unfolded)."""
    r = empty("Ramp", hinge, root)
    prism("RampSlab", [(-T, 0), (0, 0), (0, L1), (-T, L1)], 2 * hw, mat, r, bev=0.03)
    tip = empty("RampTip", (0, 0, L1), r)
    g = 0.03  # folded clearance above the main slab's ribs
    prism("RampTipSlab", [(g, 0), (g + T, 0), (g + T, -L2 + 0.3), (g + 0.06, -L2), (g, -L2)], 2 * hw - 0.06, mat, tip, bev=0.03)
    cylinder("RampTipHinge", 0.07, 2 * hw - 0.5, (0, 0.02, 0), P["steel"], tip, axis="X", segments=12)
    # Walking faces: grip ribs, curbs, hazard edges. Main slab +Y face; tip's walking face = its y = g face (toward the
    # main slab while folded).
    for i in range(int(L1 / 0.34)):
        box(f"RampRib{i}", (2 * hw - 0.5, 0.02, 0.05), (0, 0.01, 0.22 + i * 0.34), P["steel"], r, 0)
    for i in range(int(L2 / 0.34)):
        box(f"RampTipRib{i}", (2 * hw - 0.56, 0.02, 0.05), (0, g - 0.008, -0.25 - i * 0.34), P["steel"], tip, 0)
    for x in (-1, 1):
        hz_box(f"RampHz{x}", (0.16, 0.012, L1 - 0.1), (x * (hw - 0.12), 0.006, L1 / 2), P, r)
        hz_box(f"RampTipHz{x}", (0.16, 0.012, L2 - 0.1), (x * (hw - 0.15), g - 0.006, -L2 / 2), P, tip)
    hz_box("RampToeHz", (2 * hw - 0.2, 0.012, 0.22), (0, g - 0.006, -L2 + 0.16), P, tip)
    cylinder("RampHinge", 0.1, 2 * hw - 0.4, (0, -0.1, 0.0), P["steel"], r, axis="X", segments=14)
    toe = [(-g, L1 + L2), (-g - 0.06, L1 + L2), (-g - T, L1 + L2 - 0.3)]
    return r, tip, toe


def single_ramp(P, root, hinge, hw, L, T, mat):
    """Halyard-style one-piece ramp door (build_dropship.py ramp())."""
    r = empty("Ramp", hinge, root)
    prism("RampSlab", [(-T, 0), (0, 0), (0, L), (-0.06, L), (-T, L - 0.32)], 2 * hw, mat, r, bev=0.03)
    for i in range(int(L / 0.32)):
        box(f"RampRib{i}", (2 * hw - 0.5, 0.025, 0.05), (0, 0.012, 0.25 + i * 0.32), P["steel"], r, 0)
    for x in (-1, 1):
        hz_box(f"RampHz{x}", (0.16, 0.012, L - 0.1), (x * (hw - 0.12), 0.006, L / 2), P, r)
    hz_box("RampToeHz", (2 * hw - 0.2, 0.012, 0.2), (0, 0.006, L - 0.14), P, r)
    cylinder("RampHinge", 0.1, 2 * hw - 0.4, (0, -0.1, 0.0), P["steel"], r, axis="X", segments=14)
    return r, None, [(0.0, L), (-0.06, L), (-T, L - 0.32)]


def ramp_angle(h, toe):
    """Rotation about X that brings the ramp's lowest toe point onto the ground (hinge at height h)."""
    def lowest(t):
        return min(h + y * math.sin(t) + z * math.cos(t) for y, z in toe)
    lo, hi = math.radians(90), math.radians(170)
    for _ in range(60):
        m = (lo + hi) / 2
        if lowest(m) > 0:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


def bay_interior(P, root, x_hw, y0, y1, floor, top, ribs=True):
    """Deck plate with tie-down rails, wall liners and ribs, ceiling light strips (BayLight)."""
    L = y1 - y0
    box("BayDeck", (2 * x_hw - 0.04, L, 0.04), (0, (y0 + y1) / 2, floor - 0.02), P["deck"], root, 0.005)
    for x in (-1.2, -0.4, 0.4, 1.2):
        box(f"TieRail{x}", (0.07, L - 0.5, 0.02), (x, (y0 + y1) / 2, floor + 0.01), P["steel"], root, 0)
    if ribs:
        for s in (-1, 1):
            for k in range(int(L / 1.1)):
                box(f"BayRib{s}{k}", (0.07, 0.12, top - floor - 0.04), (s * (x_hw - 0.03), y0 + 0.5 + k * 1.1, (top + floor) / 2),
                    P["frame"], root, 0)
            box(f"BayLiner{s}", (0.02, L - 0.3, 1.2), (s * (x_hw - 0.012), (y0 + y1) / 2, floor + 1.0), P["liner"], root, 0)
    bm = bmesh.new()
    for x in (-0.85, 0.85):
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((x, (y0 + y1) / 2, top - 0.02)) @ Matrix.Diagonal((0.18, L - 0.8, 0.03, 1)))
    mesh_object("BayLight", bm, P["baylight"], root).visible_shadow = False


def landing_lamps(P, root, spots):
    for i, (x, y, z) in enumerate(spots):
        box(f"LandingLampFrame{i}", (0.34, 0.34, 0.06), (x, y, z), P["dark"], root, 0.02)
        box(f"LandingLamp{i}", (0.26, 0.26, 0.05), (x, y, z - 0.02), P["lamp"], root, 0.01)


# ---------------------------------------------------------------- option A: "Lander"

A_APO = 4.2             # descent stage apothem (8.4 across the flats)
A_BOT, A_TOP = 1.35, 2.33
A_FLOOR = 2.43          # bay deck top = ramp hinge height
A_MHW = 2.1             # cargo module half width
A_MY = (-4.1, 4.1)      # module rear (ramp hinge) / front faces
A_MTOP = 6.55
A_BAYTOP = 6.2
A_BAYHW = 1.8
A_L1, A_L2, A_T = 3.75, 3.08, 0.22
A_LEG_R, A_PAD_R = 4.45, 7.15  # radial distance (along the diagonals) of the leg hinge and the pad centre
A_BELL = dict(r_t=0.17, r_e=0.55, L=0.86)
A_BELL_R = 3.55          # radial distance of the bells along the diagonals
A_TANK_R = 0.92
# Tanks on the side strips, clear of the diagonals so the legs can fold straight up past them (owner: "Legs should fold").
A_TANKS = [(-3.17, 1.55), (3.17, 1.55), (-3.17, -1.55), (3.17, -1.55)]
A_NAME, A_NUMBER = "TALLGRASS", "UE-02"
A_STRIPE_Z, A_OWNER_Z = 5.6, 5.95   # orange band and the thin player-colour line along the module


def option_a(P):
    root = empty("PrewarpLander")
    parts = {"root": root, "key": "a", "legs": [], "feet": [], "plumes": [], "bells": [], "hinge_z": A_FLOOR}

    # Descent stage: octagonal box, white panels on the four main bays and black thermal tiles on the diagonal bays
    # over the engines, steel rims and corner posts, tiles underneath, grating deck on top.
    core = octagon("Stage", A_APO - 0.05, A_BOT, A_TOP, P["frame"], root)
    side = 2 * A_APO * math.tan(math.radians(22.5))
    for i in range(8):
        a = math.radians(45 * i)
        mat = P["white"] if i % 2 == 0 else P["tilewrap"]
        o = box(f"StageBlanket{i}", (0.06, side * 0.93, (A_TOP - A_BOT) * 0.86), (0, 0, 0), mat, root, 0.03)
        o.location = (math.cos(a) * (A_APO - 0.02), math.sin(a) * (A_APO - 0.02), (A_BOT + A_TOP) / 2)
        o.rotation_euler = (0, 0, a)
    for z, nm in ((A_TOP - 0.04, "RimTop"), (A_BOT + 0.04, "RimBottom")):
        octagon(nm, A_APO + 0.03, z - 0.05, z + 0.05, P["steel"], root)
    R = A_APO / math.cos(math.radians(22.5))
    for i in range(8):
        a = math.radians(22.5 + 45 * i)
        cylinder(f"StagePost{i}", 0.07, A_TOP - A_BOT, (math.cos(a) * R, math.sin(a) * R, (A_TOP + A_BOT) / 2), P["steel"], root,
                 axis="Z", segments=8)
    octagon("StageUnderside", A_APO - 0.15, A_BOT - 0.06, A_BOT, P["tiles"], root)
    octagon("StageDeck", A_APO - 0.12, A_TOP, A_TOP + 0.04, P["grating"], root)
    parts["stage"] = core

    # Engines: a bell under each diagonal corner, gimballed on a thrust frame.
    for k, (sx, sy) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        x, y = sx * A_BELL_R / math.sqrt(2), sy * A_BELL_R / math.sqrt(2)
        b = bell(k, (x, y), A_BOT - 0.05, A_BELL["r_t"], A_BELL["r_e"], A_BELL["L"], P, root, chamber=False)
        cylinder(f"EngineMount{k}", 0.34, 0.1, (x, y, A_BOT - 0.04), P["goldfoil"], root, axis="Z", segments=16)  # gold: a small accent
        parts["bells"].append((b, Vector((x, y, A_BOT - 0.05 - A_BELL["L"])), A_BELL["r_e"]))

    # Cargo module: a box with stiffening ribs, the bay cut out of it, a door frame on the ramp face.
    ylen = A_MY[1] - A_MY[0]
    mod = box("Module", (2 * A_MHW, ylen, A_MTOP - A_TOP), (0, (A_MY[0] + A_MY[1]) / 2, (A_MTOP + A_TOP) / 2), P["white"], root, 0)
    cut(mod, "BayCut", (2 * A_BAYHW, ylen - 0.35 + 0.5, A_BAYTOP - A_FLOOR + 0.02),
        (0, A_MY[0] - 0.25 + (ylen - 0.35 + 0.5) / 2, (A_BAYTOP + A_FLOOR - 0.02) / 2), root)
    bevel(mod, 0.07, 2, 35)
    bay_interior(P, root, A_BAYHW, A_MY[0], A_MY[1] - 0.35, A_FLOOR, A_BAYTOP)
    for s in (-1, 1):
        for i in range(7):
            y = A_MY[0] + 0.35 + i * (ylen - 0.7) / 6
            box(f"ModuleRib{s}{i}", (0.1, 0.14, A_MTOP - A_TOP - 0.3), (s * (A_MHW + 0.04), y, (A_MTOP + A_TOP) / 2), P["grey"], root, 0.02)
        box(f"ModuleStripe{s}", (0.02, ylen - 0.3, 0.36), (s * (A_MHW + 0.011), 0, A_STRIPE_Z), P["foam"], root, 0).visible_shadow = False
        box(f"ModuleOwner{s}", (0.02, ylen - 0.3, 0.08), (s * (A_MHW + 0.011), 0, A_OWNER_Z), P["owner"], root, 0).visible_shadow = False
        for i, (y, z) in enumerate(((-2.6, 3.0), (2.4, 3.0))):
            seam(f"ModuleSeamV{s}{i}", (0.02, 0.03, 3.6), (s * (A_MHW + 0.004), y + 0.55, 4.35), P, root)
        seam(f"ModuleSeamH{s}", (0.02, ylen - 0.2, 0.03), (s * (A_MHW + 0.004), 0, 3.7), P, root)
    # Door frame (stands proud of the rear face so the closed ramp sits flush inside it) and hazard edging.
    for s in (-1, 1):
        box(f"DoorPost{s}", (0.26, 0.24, A_BAYTOP - A_FLOOR + 0.3), (s * (A_BAYHW + 0.12), A_MY[0] - 0.08, (A_BAYTOP + A_FLOOR) / 2 + 0.05),
            P["grey"], root, 0.03)
        hz_box(f"DoorPostHz{s}", (0.012, 0.2, A_BAYTOP - A_FLOOR - 0.2), (s * (A_BAYHW + 0.26), A_MY[0] - 0.08, (A_BAYTOP + A_FLOOR) / 2), P, root)
        box(f"TailLamp{s}", (0.14, 0.05, 0.4), (s * (A_MHW - 0.06), A_MY[0] - 0.02, 5.9), P["redlamp"], root, 0.01)
        cylinder(f"HingeBlock{s}", 0.11, 0.18, (s * (A_BAYHW - 0.02), A_MY[0], A_FLOOR - 0.11), P["steel"], root, axis="X", segments=12)
    box("DoorLintel", (2 * A_BAYHW + 0.5, 0.24, 0.26), (0, A_MY[0] - 0.08, A_BAYTOP + 0.13), P["grey"], root, 0.03)
    # Deck edge hazard band round the module foot, front ladder to the cab.
    for s in (-1, 1):
        hz_box(f"DeckEdgeHz{s}", (0.012, ylen - 0.2, 0.14), (s * (A_MHW + 0.011), 0, A_TOP + 0.14), P, root)
    ladder("CabLadder", (0.0, A_MY[1] + 0.14, A_TOP + 0.1), (0.0, A_MY[1] + 0.14, A_MTOP - 0.05), (1, 0, 0), 0.5, P, root)

    # Crew cab: faceted pressurised cabin on the module's front half, two forward windows, a docking hatch on top.
    cab_prof = [(0.5, A_MTOP - 0.02), (3.75, A_MTOP - 0.02), (4.15, 7.15), (3.75, 8.0), (1.0, 8.0), (0.5, 7.65)]
    cab = prism("Cab", cab_prof, 3.0, P["white"], root, bev=0.06)
    lo, hi = (4.15, 7.15), (3.75, 8.0)
    mid, n, along, rx = on_slope(lo, hi)
    for x in (-0.68, 0.68):
        box(f"CabWindowFrame{x}", (1.0, 0.04, 0.72), mid + n * 0.01 + Vector((x, 0, 0)), P["frame"], root, 0.01, rot=(rx, 0, 0))
        box(f"CabWindow{x}", (0.86, 0.04, 0.58), mid + n * 0.025 + Vector((x, 0, 0)), P["glass"], root, 0.01, rot=(rx, 0, 0))
    lo2, hi2 = (3.75, A_MTOP - 0.02), (4.15, 7.15)
    mid2, n2, _, rx2 = on_slope(lo2, hi2)
    for x in (-0.9, 0.9):
        box(f"CabLampFrame{x}", (0.4, 0.04, 0.22), mid2 + n2 * 0.01 + Vector((x, 0, 0)), P["dark"], root, 0.01, rot=(rx2, 0, 0))
        box(f"CabLamp{x}", (0.3, 0.05, 0.14), mid2 + n2 * 0.02 + Vector((x, 0, 0)), P["lamp"], root, 0.01, rot=(rx2, 0, 0))
    for s in (-1, 1):
        box(f"CabBlanket{s}", (0.04, 2.4, 0.9), (s * 1.52, 2.2, 7.25), P["goldfoil"], root, 0.02)
        box(f"CabSideWindow{s}", (0.04, 0.36, 0.36), (s * 1.52, 3.3, 7.45), P["glass"], root, 0.01)
    cylinder("CabHatch", 0.42, 0.18, (0, 2.2, 8.08), P["grey"], root, axis="Z", segments=24, bev=0.02)
    cylinder("CabHatchRing", 0.5, 0.06, (0, 2.2, 8.02), P["dark"], root, axis="Z", segments=24)
    # Dish antenna on a mast, whips, beacon.
    cylinder("DishMast", 0.05, 1.0, (-1.1, 1.2, 8.5), P["steel"], root, axis="Z", segments=10)
    dish = lathe("Dish", [(0, 0.0), (0.45, 0.14), (0.5, 0.18), (0.47, 0.19), (0, 0.04)], P["radiator"], root, (-1.1, 1.2, 9.0), seg=24)
    dish.rotation_euler = (math.radians(-35), math.radians(20), 0)
    for i, (x, y, h) in enumerate(((1.2, 1.0, 1.3), (0.9, 0.7, 0.8))):
        cylinder(f"Whip{i}", 0.02, h, (x, y, 8.0 + h / 2), P["steel"], root, axis="Z", segments=6)
    cylinder("BeaconBase", 0.13, 0.06, (0, 0.9, 8.03), P["dark"], root, axis="Z", segments=16)
    cylinder("Beacon", 0.1, 0.16, (0, 0.9, 8.14), P["amber"], root, axis="Z", segments=16)

    # Roof aft of the cab: two radiator panels on stand-offs, RCS quads on the corners (aft ones = Thruster4/5).
    for s in (-1, 1):
        rad = box(f"Radiator{s}", (1.5, 3.6, 0.05), (s * 1.0, -2.2, A_MTOP + 0.22), P["radiator"], root, 0.01)
        for i in range(9):
            seam(f"RadiatorRib{s}{i}", (1.4, 0.025, 0.02), (s * 1.0, -3.85 + i * 0.41, A_MTOP + 0.255), P, root)
        for y in (-3.6, -0.8):
            cylinder(f"RadiatorPost{s}{y}", 0.04, 0.2, (s * 1.0, y, A_MTOP + 0.1), P["steel"], root, axis="Z", segments=8)
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
        glow = (4 if sx < 0 else 5) if sy < 0 else None
        rcs_quad(f"Rcs{k}", (sx * (A_MHW + 0.2), sy * (abs(A_MY[0]) - 0.25), A_MTOP - 0.35), (sx, 0, 0), P, root, glow_k=glow)

    # Propellant: four exposed spherical tanks in cradles on the side strips (orange foam, front pair methane, rear
    # pair oxygen), a cross-feed manifold, a pressurant bottle per side, downcomers over the stage edge.
    for i, (x, y) in enumerate(A_TANKS):
        zc = A_TOP + 0.16 + A_TANK_R
        sphere(f"Tank{i}", A_TANK_R, (x, y, zc), P["foam"], root)
        lathe(f"TankGirth{i}", [(A_TANK_R + 0.04, zc + 0.06), (A_TANK_R + 0.04, zc - 0.06)], P["steel"], root, (x, y, 0), seg=28)
        for j in range(4):
            a = math.radians(45 + 90 * j)
            top = Vector((x + math.cos(a) * A_TANK_R * 0.98, y + math.sin(a) * A_TANK_R * 0.98, zc))
            foot = Vector((x + math.cos(a) * A_TANK_R * 0.7, y + math.sin(a) * A_TANK_R * 0.7, A_TOP + 0.04))
            strut(f"TankLeg{i}{j}", foot, top, 0.035, P["gunmetal"], root, 8)
        label = "CH4" if y > 0 else "LOX"
        sx = 1 if x > 0 else -1
        box(f"TankPlacard{i}", (0.03, 0.6, 0.3), (x + sx * (A_TANK_R + 0.08), y, zc - 0.5), P["white"], root, 0.01)
        stencil(f"TankLabel{i}", label, 0.2, (x + sx * (A_TANK_R + 0.1), y, zc - 0.5), "+X" if sx > 0 else "-X", P["stencil"], root)
    for s in (-1, 1):
        x = s * 3.17
        pipe(f"Manifold{s}", [(x, -1.5, A_TOP + 0.35), (x, 1.5, A_TOP + 0.35)], 0.07, P["foam"], root)
        sphere(f"Copv{s}", 0.3, (s * 2.62, 0, A_TOP + 0.4), P["carbon"], root)
        pipe(f"Downcomer{s}", [(x, 0, A_TOP + 0.35), (s * (A_APO - 0.15), 0, A_TOP + 0.3), (s * (A_APO + 0.12), 0, A_TOP - 0.05),
                                (s * (A_APO + 0.12), 0, A_BOT + 0.1), (s * (A_APO - 0.3), 0, A_BOT - 0.12)], 0.06, P["steel"], root)
        for y in (-1.2, 1.2):
            pipe(f"Feed{s}{y}", [(x, y, A_TOP + 0.35), (x, y * 1.6, A_TOP + 0.16)], 0.05, P["steel"], root, joints=False)

    # Legs on the diagonal corners: an A-frame (white primary strut with a shock absorber, two dark secondaries from
    # the ends of the hinge axle to the knee) on a hinge axle held by two brackets on the stage rim, a dish pad on a
    # ball joint. The whole frame swings about the axle, so it folds cleanly: straight up beside the tanks in flight
    # (A_FOLD; the game keeps the pads level), like a stowed Apollo-style leg.
    for k, (sx, sy) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        d = Vector((sx, sy, 0)).normalized()
        t_w = Vector((-d.y, d.x, 0))
        hinge = d * A_LEG_R + Vector((0, 0, A_TOP - 0.15))
        yaw, M = leg_frame(hinge, (d.x, d.y), front=sy > 0)
        lg = empty(f"Leg{k}", hinge, root)
        lg.rotation_euler.z = yaw
        ankle_w = d * (A_PAD_R - 0.05) + Vector((0, 0, 0.42))
        Minv = M.inverted()
        ankle = Minv @ ankle_w
        for j in (-1, 1):  # hinge brackets: body parts on the stage rim
            box(f"Leg{k}Bracket{j}", (0.14, 0.34, 0.36), hinge + t_w * j * 0.68 - d * 0.12, P["gunmetal"], root, 0.02).rotation_euler.z = \
                math.atan2(d.y, d.x)
        cylinder(f"Leg{k}Axle", 0.1, 1.5, (0, 0, 0), P["steel"], lg, axis="X", segments=14)
        knee = ankle * 0.58
        strut(f"Leg{k}Primary", Vector((0, 0, 0)), ankle * 0.66, 0.13, P["white"], lg, 14)
        strut(f"Leg{k}Piston", ankle * 0.64, ankle, 0.075, P["steel"], lg, 12)
        cylinder(f"Leg{k}Collar", 0.15, 0.1, ankle * 0.66, P["dark"], lg, axis="Z", segments=14).rotation_euler = \
            ankle.to_track_quat("Z", "Y").to_euler()
        for j, t in enumerate((-1, 1)):
            strut(f"Leg{k}Secondary{j}", Vector((t * 0.6, 0, 0)), knee, 0.055, P["darkgrey"], lg, 10)
        ft = empty(f"Foot{k}", ankle, lg)
        sphere(f"Foot{k}Ball", 0.12, (0, 0, 0), P["steel"], ft, seg=12, rings=8)
        pad_dish(f"Foot{k}Pad", 0.62, P, ft, (0, 0, -0.42))
        hz_box(f"Foot{k}Hz", (0.5, 0.5, 0.01), (0, 0, -0.42 + 0.215), P, ft, rot=(0, 0, math.radians(45)))
        parts["legs"].append(lg)
        parts["feet"].append(ft)

    # Ramp: two-piece door on the -Y face.
    ramp, tip, toe = folding_ramp(P, root, (0, A_MY[0], A_FLOOR), A_BAYHW - 0.03, A_L1, A_L2, A_T, P["white"])
    for z in (1.2, 2.5):
        seam(f"RampSeam{z}", (2 * A_BAYHW - 0.2, 0.02, 0.03), (0, -A_T - 0.004, z), P, ramp)
    box("RampStripe", (2 * A_BAYHW - 0.1, 0.012, 0.36), (0, -A_T - 0.006, A_STRIPE_Z - A_FLOOR), P["foam"], ramp, 0).visible_shadow = False
    box("RampOwner", (2 * A_BAYHW - 0.1, 0.012, 0.08), (0, -A_T - 0.006, A_OWNER_Z - A_FLOOR), P["owner"], ramp, 0).visible_shadow = False
    stencil("RampKeepClear", "KEEP CLEAR OF RAMP", 0.16, (0, -A_T - 0.01, 0.55), "-Y", P["stencil"], ramp)
    parts.update(ramp=ramp, tip=tip, toe=toe)

    # Stencils: name along the module (above the orange band), big hull number, name across the cab front, small
    # warnings on the stage.
    for s in (-1, 1):
        f = "+X" if s > 0 else "-X"
        stencil(f"Name{s}", A_NAME, 0.3, (s * (A_MHW + 0.11), 0.2, 6.25), f, P["stencil"], root, max_width=5.0)
        stencil(f"Number{s}", A_NUMBER, 0.9, (s * (A_MHW + 0.11), -0.4, 4.78), f, P["stencil"], root, max_width=4.4)
        stencil(f"StageWarn{s}", "DANGER  ENGINE EXHAUST", 0.13, (s * (A_APO + 0.03), 0, A_BOT + 0.22), f, P["stencilred"], root)
        stencil(f"NoStep{s}", "NO STEP", 0.18, (s * 1.0, -0.6, A_MTOP + 0.015), "+Z", P["stencil"], root, read=math.radians(90 if s > 0 else -90))
    stencil("NameFront", A_NAME, 0.22, (0, A_MY[1] + 0.02, 5.1), "+Y", P["stencil"], root, max_width=3.6)
    stencil("NameRear", A_NAME, 0.22, (0, A_MY[0] - 0.21, A_BAYTOP + 0.13), "-Y", P["stencil"], root, max_width=3.2)
    landing_lamps(P, root, [(0, A_APO - 0.6, A_BOT - 0.08), (0, -A_APO + 0.6, A_BOT - 0.08)])
    parts["descent_alt"] = 8.0
    # Legs fold straight up beside the tanks (the primary strut vertical, ~124 degrees); the pads turn a further
    # (fold - 90) so they stand flat, contact face out, like a stowed Apollo-style leg.
    leg = 90 + math.degrees(math.atan2((A_TOP - 0.15) - 0.42, (A_PAD_R - 0.05) - A_LEG_R))
    parts["fold"] = (leg, leg - 90)
    return parts


# ---------------------------------------------------------------- option B: "Hopper"

B_R = 3.6               # body radius
B_BASE = 2.05           # aft heat shield (bottom of the body)
B_FLOOR = 2.58          # bay floor = ramp hinge height
B_BAYTOP = 6.45
B_BAYHW = 1.8
B_BAY_Y = (-4.0, 2.75)
B_DOOR_Y = -3.98        # door housing face / hinge line
B_L1 = B_BAYTOP - B_FLOOR - 0.05
B_L2, B_T = 3.47, 0.22
B_LEG_R, B_LEG_Z, B_PAD_R = B_R + 0.35, 2.45, 6.35   # hinge stands off the skirt so the folded leg clears the barrel
B_BELL = dict(r_t=0.19, r_e=0.62, L=1.2)
B_NAME, B_NUMBER = "SOUTHWIND", "UE-05"
B_ORANGE = (7.35, 9.55)  # the orange tank section between the white cargo barrel and the white crew level


def option_b(P):
    root = empty("PrewarpHopper")
    parts = {"root": root, "key": "b", "legs": [], "feet": [], "plumes": [], "bells": [], "hinge_z": B_FLOOR}

    # Body: one lathe; materials by height band: black tiled skirt, white cargo barrel, the orange tank section, white
    # crew level and nose, a black tiled nose cap.
    prof = [(0, B_BASE), (B_R - 0.3, B_BASE), (B_R, B_BASE + 0.28), (B_R, 2.9), (B_R, 7.1), (B_R, B_ORANGE[0]),
            (B_R, 7.6), (B_R, B_ORANGE[1]), (B_R, 10.4), (B_R - 0.12, 11.1), (B_R - 0.55, 11.9), (B_R - 1.35, 12.6),
            (B_R - 2.4, 13.1), (0, 13.3)]
    body = lathe("Barrel", prof, P["white"], root, seg=64)
    body.data.materials.append(P["tilewrap"])
    body.data.materials.append(P["foam"])
    for poly in body.data.polygons:
        z = poly.center.z
        poly.material_index = 1 if (z < 2.9 or z > 12.62) else (2 if B_ORANGE[0] < z < B_ORANGE[1] else 0)
    cut(body, "BayCut", (2 * B_BAYHW, B_BAY_Y[1] - B_BAY_Y[0] + 0.6, B_BAYTOP - B_FLOOR + 0.02),
        (0, (B_BAY_Y[0] - 0.6 + B_BAY_Y[1]) / 2, (B_BAYTOP + B_FLOOR) / 2), root)
    bay_interior(P, root, B_BAYHW, B_DOOR_Y, B_BAY_Y[1], B_FLOOR, B_BAYTOP)
    box("BayEndWall", (2 * B_BAYHW, 0.06, B_BAYTOP - B_FLOOR), (0, B_BAY_Y[1] + 0.03, (B_BAYTOP + B_FLOOR) / 2), P["liner"], root, 0)
    # Base: tiled heat shield disc, thrust-section foil between the engines.
    lathe("HeatShield", [(0, B_BASE + 0.01), (B_R - 0.32, B_BASE + 0.01), (B_R - 0.1, B_BASE - 0.08), (0, B_BASE - 0.08)],
          P["tiles"], root, seg=48, closed=True)
    sphere("ThrustCone", 0.8, (0, 0, B_BASE - 0.08), P["goldfoil"], root, squash=0.45)
    # Weld seams on the barrel, the thin owner line under the orange section, panel lines; dark tile strips where
    # the legs lie folded.
    for z in (3.75, 4.95, 6.15, B_ORANGE[1]):
        ring_band(f"Weld{z}", B_R + 0.006, z - 0.02, z + 0.02, P["dark"], root)
    ring_band("OwnerBand", B_R + 0.01, 7.08, 7.2, P["owner"], root, seg=64)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        cyl_patch(f"LegWell{k}", B_R, a - 0.11, a + 0.11, 2.85, 6.4, P["tilewrap"], root, nu=4, lift=0.008)
    for i in range(12):
        a = math.radians(15 + 30 * i)
        cyl_patch(f"PanelLine{i}", B_R, a - 0.002, a + 0.002, 8.05, 10.35, P["dark"], root, nu=1, lift=0.006)

    # Door housing: a square frame standing proud of the barrel on -Y, the door = a two-piece ramp.
    zc = (B_BAYTOP + B_FLOOR) / 2
    for s in (-1, 1):
        box(f"DoorPost{s}", (0.4, 1.2, B_BAYTOP - B_FLOOR + 0.6), (s * (B_BAYHW + 0.2), B_DOOR_Y + 0.6, zc), P["white"], root, 0.04)
        hz_box(f"DoorPostHz{s}", (0.2, 0.012, B_BAYTOP - B_FLOOR + 0.3), (s * (B_BAYHW + 0.2), B_DOOR_Y - 0.006, zc), P, root)
        box(f"TailLamp{s}", (0.14, 0.05, 0.3), (s * (B_BAYHW + 0.2), B_DOOR_Y - 0.02, B_BAYTOP + 0.15), P["redlamp"], root, 0.01)
        cylinder(f"HingeBlock{s}", 0.11, 0.18, (s * (B_BAYHW - 0.02), B_DOOR_Y, B_FLOOR - 0.11), P["steel"], root, axis="X", segments=12)
    box("DoorLintel", (2 * B_BAYHW + 0.8, 1.2, 0.36), (0, B_DOOR_Y + 0.6, B_BAYTOP + 0.18), P["white"], root, 0.04)
    box("DoorSill", (2 * B_BAYHW + 0.8, 1.2, 0.4), (0, B_DOOR_Y + 0.6, B_FLOOR - 0.2), P["white"], root, 0.04)
    hz_box("DoorSillHz", (2 * B_BAYHW + 0.6, 0.012, 0.18), (0, B_DOOR_Y - 0.006, B_FLOOR - 0.22), P, root)
    ramp, tip, toe = folding_ramp(P, root, (0, B_DOOR_Y, B_FLOOR), B_BAYHW - 0.03, B_L1, B_L2, B_T, P["white"])
    for z in (1.0, 2.0, 3.0):
        seam(f"RampSeam{z}", (2 * B_BAYHW - 0.2, 0.02, 0.03), (0, -B_T - 0.004, z), P, ramp)
    stencil("RampCargo", "CARGO", 0.42, (0, -B_T - 0.01, 2.6), "-Y", P["stencil"], ramp)
    stencil("RampKeepClear", "KEEP CLEAR - RAMP DEPLOYS 8 M", 0.14, (0, -B_T - 0.01, 1.9), "-Y", P["stencilred"], ramp)
    parts.update(ramp=ramp, tip=tip, toe=toe)

    # Engines: a cluster of four under the base (Thruster0..3).
    for k, (sx, sy) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        x, y = sx * 1.08, sy * 1.08
        b = bell(k, (x, y), B_BASE - 0.12, B_BELL["r_t"], B_BELL["r_e"], B_BELL["L"], P, root, chamber=False)
        parts["bells"].append((b, Vector((x, y, B_BASE - 0.12 - B_BELL["L"])), B_BELL["r_e"]))
        cylinder(f"EngineMount{k}", 0.36, 0.12, (x, y, B_BASE - 0.1), P["gunmetal"], root, axis="Z", segments=16)

    # Legs on the diagonals: hinged low on a bracket off the tiled skirt, a black carbon main beam with a white tip and
    # two side braces from the ends of the hinge axle (an A-frame that swings as one), big dish pads. In flight they
    # fold B_FOLD (~130 degrees) straight up against the barrel over the dark leg wells, pads turned flat against it.
    for k, (sx, sy) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        d = Vector((sx, sy, 0)).normalized()
        hinge = d * B_LEG_R + Vector((0, 0, B_LEG_Z))
        yaw, M = leg_frame(hinge, (d.x, d.y), front=sy > 0)
        lg = empty(f"Leg{k}", hinge, root)
        lg.rotation_euler.z = yaw
        Minv = M.inverted()
        ankle = Minv @ (d * B_PAD_R + Vector((0, 0, 0.45)))
        mount = box(f"Leg{k}Mount", (0.42, 1.2, 0.5), d * (B_R + 0.13) + Vector((0, 0, B_LEG_Z + 0.02)), P["gunmetal"], root, 0.04)
        mount.rotation_euler.z = math.atan2(d.y, d.x)
        box(f"Leg{k}Clevis", (1.0, 0.3, 0.26), (0, 0, 0), P["gunmetal"], lg, 0.03)
        cylinder(f"Leg{k}Knuckle", 0.12, 1.1, (0, 0, 0), P["steel"], lg, axis="X", segments=14)
        strut(f"Leg{k}Beam", Vector((0, 0, 0)), ankle, 0.17, P["carbon"], lg, 6)
        strut(f"Leg{k}BeamTip", ankle * 0.82, ankle, 0.19, P["white"], lg, 6)
        for j in (-1, 1):
            strut(f"Leg{k}Brace{j}", Vector((j * 0.5, 0, 0)), ankle * 0.55, 0.06, P["grey"], lg, 10)
        ft = empty(f"Foot{k}", ankle, lg)
        sphere(f"Foot{k}Ball", 0.14, (0, 0, 0), P["steel"], ft, seg=12, rings=8)
        pad_dish(f"Foot{k}Pad", 0.72, P, ft, (0, 0, -0.45))
        parts["legs"].append(lg)
        parts["feet"].append(ft)

    # Grid fins on the diagonals near the top, RCS quads (aft pair = Thruster4/5 either side of the door line).
    for i in range(4):
        a = math.radians(45 + 90 * i)
        d = Vector((math.cos(a), math.sin(a), 0))
        fin = empty(f"GridFin{i}", d * (B_R - 0.12) + Vector((0, 0, 11.0)), root)
        fin.rotation_euler.z = a
        box(f"GridFin{i}Root", (0.3, 0.5, 0.5), (0.1, 0, 0), P["gunmetal"], fin, 0.03)
        for j in range(4):
            box(f"GridFin{i}Frame{j}", (1.26 if j < 2 else 0.07, 0.07, 0.07 if j < 2 else 1.0),
                ((0.88, 0, 0.47 * (1 if j == 0 else -1)) if j < 2 else (0.25 + 1.26 * (j - 2), 0, 0)), P["steel"], fin, 0)
        for j in range(1, 7):
            box(f"GridFin{i}LatV{j}", (0.025, 0.06, 0.94), (0.25 + 1.26 * j / 7, 0, 0), P["steel"], fin, 0)
        for j in range(1, 5):
            box(f"GridFin{i}LatH{j}", (1.26, 0.06, 0.025), (0.88, 0, -0.47 + 0.94 * j / 5), P["steel"], fin, 0)
    for k, (a, glow) in enumerate(((math.radians(-60), 5), (math.radians(-120), 4), (math.radians(60), None), (math.radians(120), None))):
        d = Vector((math.cos(a), math.sin(a), 0))
        rcs_quad(f"Rcs{k}", d * (B_R + 0.12) + Vector((0, 0, 10.75)), d, P, root, glow_k=glow)

    # Crew level: window row facing forward, a hatch under it with a ladder down the barrel (between the front legs),
    # a cable raceway with two insulated feed lines (clear of the folded legs), landing lamps under the base, beacon
    # and antennas on the nose.
    for i in range(5):
        a = math.radians(90 - 36 + 18 * i)
        cyl_patch(f"CrewWindowFrame{i}", B_R, a - 0.07, a + 0.07, 9.85, 10.35, P["frame"], root, nu=3, lift=0.01)
        cyl_patch(f"CrewWindow{i}", B_R, a - 0.055, a + 0.055, 9.9, 10.3, P["glass"], root, nu=3, lift=0.016)
    ha = math.radians(90)
    cyl_patch("CrewHatchFrame", B_R, ha - 0.2, ha + 0.2, 8.25, 9.65, P["grey"], root, nu=6, lift=0.012)
    cyl_patch("CrewHatchSeam", B_R, ha - 0.17, ha + 0.17, 8.3, 9.6, P["dark"], root, nu=6, lift=0.016)
    cyl_patch("CrewHatch", B_R, ha - 0.16, ha + 0.16, 8.32, 9.58, P["white"], root, nu=6, lift=0.02)
    dh = Vector((math.cos(ha), math.sin(ha), 0))
    ladder("HatchLadder", dh * (B_R + 0.22) + Vector((0, 0, 3.0)), dh * (B_R + 0.22) + Vector((0, 0, 8.25)),
           (-dh.y, dh.x, 0), 0.5, P, root)
    for z in (3.6, 5.6, 7.6):
        strut(f"LadderStandoff{z}", dh * B_R + Vector((0, 0, z)), dh * (B_R + 0.22) + Vector((0, 0, z)), 0.03, P["steel"], root, 6)
    a_r = math.radians(115)
    d_r = Vector((math.cos(a_r), math.sin(a_r), 0))
    t_r = Vector((-d_r.y, d_r.x, 0))
    rw = box("Raceway", (0.22, 0.38, 6.6), d_r * (B_R + 0.1) + Vector((0, 0, 2.9 + 3.3)), P["grey"], root, 0.03)
    rw.rotation_euler.z = a_r
    for s in (-1, 1):
        pipe(f"RacewayLine{s}", [d_r * (B_R + 0.3) + t_r * s * 0.32 + Vector((0, 0, 2.7)),
                                 d_r * (B_R + 0.3) + t_r * s * 0.32 + Vector((0, 0, 9.5))], 0.07, P["grey"], root)
    for z in (3.4, 5.0, 6.6, 8.2):
        box(f"RacewayClamp{z}", (0.12, 1.0, 0.1), d_r * (B_R + 0.28) + Vector((0, 0, z)), P["gunmetal"], root, 0.02).rotation_euler.z = a_r
    landing_lamps(P, root, [(0, 2.3, B_BASE - 0.1), (0, -2.3, B_BASE - 0.1), (2.3, 0, B_BASE - 0.1), (-2.3, 0, B_BASE - 0.1)])
    cylinder("BeaconBase", 0.13, 0.06, (0, 0, 13.3), P["dark"], root, axis="Z", segments=16)
    cylinder("Beacon", 0.1, 0.16, (0, 0, 13.41), P["amber"], root, axis="Z", segments=16)
    for i, (x, y, h) in enumerate(((0.6, 0.4, 0.9), (-0.5, 0.5, 0.6))):
        cylinder(f"Whip{i}", 0.02, h, (x, y, 13.1 + h / 2), P["steel"], root, axis="Z", segments=6)

    # Stencils.
    for s, phi in ((1, 0.0), (-1, math.pi)):
        if BLANK:
            # The game paints flat Label3D decals on these markers: kept narrow so the curve hides them (sagitta
            # w^2 / 8R ~0.15) and set a little proud of the barrel.
            f = "+X" if s > 0 else "-X"
            stencil(f"Number{s}", B_NUMBER, 0.9, (s * (B_R + 0.08), 0, 8.9), f, P["stencil"], root, max_width=2.2)
            stencil(f"Name{s}", B_NAME, 0.3, (s * (B_R + 0.06), 0, 6.75), f, P["stencil"], root, max_width=2.0)
        else:
            text_on_cylinder(f"Number{s}", B_NUMBER, 1.0, B_R, phi, 8.9, P["stencil"], root)
            text_on_cylinder(f"Name{s}", B_NAME, 0.3, B_R, phi, 6.75, P["stencil"], root)
    stencil("NameRear", B_NAME, 0.24, (0, B_DOOR_Y - 0.01, B_BAYTOP + 0.18), "-Y", P["stencil"], root, max_width=3.4)
    text_on_cylinder("SkirtWarn", "DANGER  ENGINE EXHAUST  KEEP 50 M CLEAR", 0.13, B_R, math.pi / 2, 2.55, P["stencilwhite"], root)
    text_on_cylinder("HatchSign", "CREW HATCH", 0.14, B_R, ha, 9.78, P["stencil"], root)
    parts["descent_alt"] = 9.0
    # Legs fold straight up the barrel (beam vertical, ~130 degrees); the pads turn a further (fold - 90) so they lie
    # flat against the barrel, contact face out.
    leg = 90 + math.degrees(math.atan2(B_LEG_Z - 0.45, B_PAD_R - B_LEG_R))
    parts["fold"] = (leg, leg - 90)
    return parts


# ---------------------------------------------------------------- option C: "Freighter"

C_REAR, C_NOSE = -6.4, 7.32
C_FLOOR = 1.25
C_BAYTOP = 4.6
C_BAYHW = 1.8
C_BAYFRONT = 1.6
C_RAMP_L = C_BAYTOP - C_FLOOR - 0.03
C_RAMP_T = 0.26
# loft sections: (y, half width, belly z, top z)
C_SECTIONS = [(C_REAR, 2.5, 0.85, 5.15), (2.0, 2.5, 0.85, 5.15), (4.2, 2.44, 0.9, 4.95), (5.5, 2.24, 1.04, 4.5),
              (6.45, 1.86, 1.28, 3.92), (6.98, 1.36, 1.6, 3.38), (7.24, 0.7, 1.95, 2.85)]
C_NTOP, C_NBOT, C_RING = 4.0, 9.0, 56
C_ENG = [(-4.15, 3.5), (4.15, 3.5), (-4.15, -4.55), (4.15, -4.55)]   # engine centres (x, y), Thruster0..3 order
C_ENG_Z = 3.05          # mount ring height
C_BELL = dict(r_t=0.22, r_e=0.66, L=1.25)
C_NAME, C_NUMBER = "IRONWOOD", "UE-07"


def c_section(y):
    S = C_SECTIONS
    if y <= S[0][0]:
        return S[0][1:]
    for (y0, *a), (y1, *b) in zip(S, S[1:]):
        if y <= y1:
            t = (y - y0) / (y1 - y0)
            return tuple(p + (q - p) * t for p, q in zip(a, b))
    return S[-1][1:]


def c_point(y, t):
    """Superellipse cross-section point at station y, angle t (0 = +X flank, pi/2 = top, 3pi/2 = belly)."""
    hw, zb, zt = c_section(y)
    zc, b = (zb + zt) / 2, (zt - zb) / 2
    c, s = math.cos(t), math.sin(t)
    n = C_NTOP if s >= 0 else C_NBOT
    x = hw * math.copysign(abs(c) ** (2 / n), c)
    z = zc + b * math.copysign(abs(s) ** (2 / n), s)
    return Vector((x, y, z))


def c_hull(P, root):
    bm = bmesh.new()
    rings = []
    for y, *_ in C_SECTIONS:
        rings.append([bm.verts.new(c_point(y, 2 * math.pi * i / C_RING)) for i in range(C_RING)])
    for a, b in zip(rings, rings[1:]):
        for i in range(C_RING):
            j = (i + 1) % C_RING
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0])))
    tipv = bm.verts.new((0, C_NOSE, 2.42))
    for i in range(C_RING):
        bm.faces.new((rings[-1][i], rings[-1][(i + 1) % C_RING], tipv))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    hull = mesh_object("Hull", bm, P["white"], root, smooth=False)
    hull.data.materials.append(P["tiles"])
    hull.data.materials.append(P["tilenose"])
    for poly in hull.data.polygons:
        c, nrm = poly.center, poly.normal
        if nrm.z < -0.55:
            poly.material_index = 1
        elif c.y > 6.98:  # the blunt nose cap: everything ahead of the last full section
            poly.material_index = 2
    for poly in hull.data.polygons:
        poly.use_smooth = True
    return hull


def c_angle(z):
    """Section angle t (upper flank) at which the constant mid-body section reaches height z."""
    hw, zb, zt = C_SECTIONS[0][1:]
    zc, b = (zb + zt) / 2, (zt - zb) / 2
    return math.asin(max(0.0, min(1.0, (z - zc) / b)) ** (C_NTOP / 2))


def c_patch(name, y0, y1, t0, t1, mat, parent, nu=8, nv=4, lift=0.014, shadow=False):
    """A patch lying on the hull loft between stations y0..y1 and section angles t0..t1 (decals, windows, stripes)."""
    bm = bmesh.new()
    grid = []
    for j in range(nv + 1):
        y = y0 + (y1 - y0) * j / nv
        row = []
        for i in range(nu + 1):
            t = t0 + (t1 - t0) * i / nu
            p = c_point(y, t)
            q = c_point(y, t + 1e-3)
            r = c_point(y + 1e-3, t)
            nrm = (q - p).cross(r - p).normalized()
            if nrm.dot(Vector((p.x, 0, p.z - 3.0))) < 0:
                nrm = -nrm
            row.append(bm.verts.new(p + nrm * lift))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    f = bm.faces[len(bm.faces) // 2]
    c = f.calc_center_median()
    if f.normal.dot(Vector((c.x, 0, c.z - 3.0))) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    o = mesh_object(name, bm, mat, parent, smooth=True)
    o.visible_shadow = shadow
    return o


def option_c(P):
    root = empty("PrewarpFreighter")
    parts = {"root": root, "key": "c", "legs": [], "feet": [], "plumes": [], "bells": [], "hinge_z": C_FLOOR}
    hull = c_hull(P, root)
    cut(hull, "BayCut", (2 * C_BAYHW, C_BAYFRONT - C_REAR + 1.0, C_BAYTOP - C_FLOOR + 0.02),
        (0, (C_BAYFRONT + C_REAR - 1.0) / 2, (C_BAYTOP + C_FLOOR - 0.02) / 2), root)
    hull.modifiers.new("Crease", "EDGE_SPLIT").split_angle = math.radians(40)  # after the boolean: keep it manifold
    bay_interior(P, root, C_BAYHW, C_REAR, C_BAYFRONT, C_FLOOR, C_BAYTOP)
    box("BulkheadDoor", (1.0, 0.04, 2.1), (0, C_BAYFRONT - 0.02, C_FLOOR + 1.05), P["grey"], root, 0.02)
    box("BulkheadDoorWindow", (0.4, 0.05, 0.3), (0, C_BAYFRONT - 0.03, C_FLOOR + 1.6), P["glass"], root, 0.01)

    # Flight deck windows: a row of six small panes across the upper nose, frames, nose lamps.
    for i in range(6):
        t0 = math.pi / 2 - 0.62 + i * 0.21
        c_patch(f"WindowFrame{i}", 5.62, 6.2, t0, t0 + 0.19, P["frame"], root, nu=3, nv=2, lift=0.012)
        c_patch(f"Window{i}", 5.66, 6.16, t0 + 0.015, t0 + 0.175, P["glass"], root, nu=3, nv=2, lift=0.02)
    for s in (-1, 1):
        t = math.radians(-12) if s > 0 else math.pi + math.radians(12)
        c_patch(f"SideWindow{s}", 4.9, 5.4, t - 0.1 * s, t + 0.1 * s, P["glass"], root, nu=2, nv=2, lift=0.02)
        c_patch(f"NoseLamp{s}", 7.0, 7.12, (math.radians(-25) if s > 0 else math.pi + math.radians(25)) - 0.08 * s,
                (math.radians(-25) if s > 0 else math.pi + math.radians(25)) + 0.08 * s, P["lamp"], root, nu=2, nv=1, lift=0.02)
    # Orange flash along the flanks, the thin player-colour line under it, panel seams, hazard edge at the door.
    for s in (-1, 1):
        for nm, z0, z1, y1, mat in (("Flash", 4.15, 4.42, 6.3, P["foam"]), ("FlashOwner", 3.96, 4.04, 6.1, P["owner"])):
            t0, t1 = c_angle(z0), c_angle(z1)
            if s < 0:
                t0, t1 = math.pi - t1, math.pi - t0
            c_patch(f"{nm}{s}", C_REAR + 0.05, y1, t0, t1, mat, root, nu=1, nv=28)
        for y in (-3.95, 0.55, 2.7):
            seam(f"SeamV{s}{y}", (0.02, 0.03, 2.6), (s * 2.506, y, 2.75), P, root)
        seam(f"SeamH{s}", (0.02, 8.4, 0.03), (s * 2.506, -2.1, 1.85), P, root)
        for k in range(6):
            seam(f"Vent{s}{k}", (0.02, 0.5, 0.04), (s * 2.506, -5.85, 3.0 + k * 0.1), P, root)
    # Rear door frame, tail lamps.
    for s in (-1, 1):
        box(f"DoorPost{s}", (0.24, C_RAMP_T + 0.04, C_BAYTOP - 0.85 + 0.1), (s * (C_BAYHW + 0.1), C_REAR - (C_RAMP_T + 0.04) / 2 + 0.02,
                                                                           (C_BAYTOP + 0.85 + 0.1) / 2), P["grey"], root, 0.03)
        hz_box(f"DoorPostHz{s}", (0.14, 0.012, C_BAYTOP - 1.0), (s * (C_BAYHW + 0.1), C_REAR - C_RAMP_T - 0.03, (C_BAYTOP + 0.85) / 2), P, root)
        box(f"TailLamp{s}", (0.12, 0.04, 0.5), (s * 2.25, C_REAR - 0.02, 4.0), P["redlamp"], root, 0.01)
        cylinder(f"HingeBlock{s}", 0.11, 0.18, (s * (C_BAYHW - 0.02), C_REAR, C_FLOOR - 0.11), P["steel"], root, axis="X", segments=12)
    box("DoorLintel", (2 * C_BAYHW + 0.44, C_RAMP_T + 0.04, 0.26), (0, C_REAR - (C_RAMP_T + 0.04) / 2 + 0.02, C_BAYTOP + 0.1), P["grey"], root, 0.03)
    ramp, tip, toe = single_ramp(P, root, (0, C_REAR, C_FLOOR), 1.75, C_RAMP_L, C_RAMP_T, P["white"])
    for z in (1.3, 2.4):
        seam(f"RampSeam{z}", (3.3, 0.02, 0.03), (0, -C_RAMP_T - 0.004, z), P, ramp)
    box("RampStripe", (3.42, 0.012, 0.27), (0, -C_RAMP_T - 0.006, 4.285 - C_FLOOR), P["foam"], ramp, 0).visible_shadow = False
    box("RampOwner", (3.42, 0.012, 0.08), (0, -C_RAMP_T - 0.006, 4.0 - C_FLOOR), P["owner"], ramp, 0).visible_shadow = False
    stencil("RampKeepClear", "KEEP CLEAR OF RAMP", 0.16, (0, -C_RAMP_T - 0.01, 0.5), "-Y", P["stencil"], ramp)
    parts.update(ramp=ramp, tip=tip, toe=toe)

    # Engine outriggers: a box beam and a diagonal brace from each flank to an exposed engine hung in a mount ring
    # (heat-shield cap on top, chamber, turbopump, gimbal struts, bell), feed lines in orange foam along the beams.
    for k, (x, y) in enumerate(C_ENG):
        s = 1 if x > 0 else -1
        bx0, bx1 = s * 2.35, x + s * 0.95
        box(f"Outrigger{k}", (abs(bx1 - bx0), 0.55, 0.42), ((bx0 + bx1) / 2, y, C_ENG_Z + 0.38), P["grey"], root, 0.04)
        hz_box(f"OutriggerHz{k}", (abs(bx1 - bx0) - 0.3, 0.012, 0.22), ((bx0 + bx1) / 2, y - 0.282, C_ENG_Z + 0.38), P, root)
        strut(f"OutriggerBrace{k}", (s * 2.45, y, 1.55), (x - s * 0.55, y, C_ENG_Z + 0.2), 0.09, P["gunmetal"], root, 10)
        V["ring"](f"MountRing{k}", 0.72, 0.16, 0.08, (x, y, C_ENG_Z), P["gunmetal"], root, 28)
        cap = lathe(f"EngineCap{k}", [(0, C_ENG_Z + 0.62), (0.7, C_ENG_Z + 0.58), (0.82, C_ENG_Z + 0.45), (0.82, C_ENG_Z + 0.3), (0, C_ENG_Z + 0.3)],
                    P["darkgrey"], root, (x, y, 0), seg=28)
        _ = cap
        zt = C_ENG_Z - 0.55
        b = bell(k, (x, y), zt, C_BELL["r_t"], C_BELL["r_e"], C_BELL["L"], P, root, chamber=True)
        parts["bells"].append((b, Vector((x, y, zt - C_BELL["L"])), C_BELL["r_e"]))
        for j in range(3):
            a = math.radians(30 + 120 * j)
            strut(f"MountStrut{k}{j}", (x + 0.7 * math.cos(a), y + 0.7 * math.sin(a), C_ENG_Z), (x + 0.3 * math.cos(a), y + 0.3 * math.sin(a), zt + 0.62),
                  0.035, P["steel"], root, 8)
        pipe(f"FeedLine{k}", [(s * 2.45, y + 0.4, C_ENG_Z + 0.75), (x - s * 0.4, y + 0.4, C_ENG_Z + 0.75), (x - s * 0.2, y + 0.3, zt + 0.5)], 0.075,
             P["foam"], root)
        stencil(f"EngWarn{k}", "DANGER", 0.16, (x, y - 0.29 if y < 0 else y + 0.29, C_ENG_Z + 0.38), "-Y" if y < 0 else "+Y", P["stencilred"], root)

    # Legs: outboard of each engine, hinged under the outrigger tip: telescoping strut + side brace, dish pad.
    for k, (x, y) in enumerate(C_ENG):
        s = 1 if x > 0 else -1
        front = y > 0
        hinge = Vector((x + s * 0.88, y, C_ENG_Z + 0.16))
        # These legs hang nearly straight down and splay outward in X, like Halyard's: identity rest, and the
        # game's fold about X swings them fore/aft under the outrigger.
        M = Matrix.Translation(hinge)
        lg = empty(f"Leg{k}", hinge, root)
        Minv = M.inverted()
        ankle = Minv @ Vector((x + s * 1.5, y, 0.42))
        box(f"Leg{k}Clevis", (0.36, 0.42, 0.22), (0, 0, 0.02), P["gunmetal"], lg, 0.03)
        cylinder(f"Leg{k}Knuckle", 0.12, 0.5, (0, 0, 0), P["steel"], lg, axis="X", segments=14)
        strut(f"Leg{k}Sleeve", Vector((0, 0, 0)), ankle * 0.62, 0.14, P["white"], lg, 14)
        strut(f"Leg{k}Piston", ankle * 0.6, ankle, 0.08, P["steel"], lg, 12)
        cylinder(f"Leg{k}Collar", 0.16, 0.1, ankle * 0.62, P["dark"], lg, axis="Z", segments=14).rotation_euler = ankle.to_track_quat("Z", "Y").to_euler()
        hz_box(f"Leg{k}Hz", (0.3, 0.3, 0.3), ankle * 0.45, P, lg)
        brace_root = Minv @ Vector((x - s * 0.2, y + (-0.4 if front else 0.4), C_ENG_Z + 0.2))
        strut(f"Leg{k}Brace", brace_root, ankle * 0.5, 0.05, P["dark"], lg, 8)
        ft = empty(f"Foot{k}", ankle, lg)
        sphere(f"Foot{k}Ball", 0.13, (0, 0, 0), P["steel"], ft, seg=12, rings=8)
        pad_dish(f"Foot{k}Pad", 0.62, P, ft, (0, 0, -0.42))
        parts["legs"].append(lg)
        parts["feet"].append(ft)

    # External tanks on the back: foam with white domes, straps, saddles; dorsal radiator, mast, beacon, hatch.
    for s in (-1, 1):
        x, z, r = s * 1.42, 5.62, 0.5
        y0, y1 = -5.7, 2.3
        cylinder(f"ExtTank{s}", r, y1 - y0, (x, (y0 + y1) / 2, z), P["foam"], root, axis="Y", segments=28)
        for yy, sgn in ((y1, 1), (y0, -1)):
            sphere(f"ExtTankDome{s}{sgn}", r, (x, yy, z), P["white"], root, seg=24, rings=12).scale = (1, 0.55, 1)
        for i in range(5):
            yy = y0 + 0.6 + i * (y1 - y0 - 1.2) / 4
            lathe(f"ExtTankStrap{s}{i}", [(r + 0.025, 0.06), (r + 0.025, -0.06)], P["dark"], root, (x, yy, z), seg=28).rotation_euler = (math.radians(90), 0, 0)
            box(f"ExtTankSaddle{s}{i}", (0.6, 0.18, 0.5), (x, yy, z - r - 0.05), P["gunmetal"], root, 0.03)
        stencil(f"ExtTankLabel{s}", "LOX" if s < 0 else "CH4", 0.3, (x + s * (r + 0.02), -1.7, z), "+X" if s > 0 else "-X", P["stencilwhite"], root)
        pipe(f"ExtTankFeed{s}", [(x, y0 - 0.2, z - 0.2), (x * 1.25, y0 - 0.1, 4.6), (x * 1.55, y0 + 0.3, 4.05)], 0.07, P["steel"], root)
    rad = box("DorsalRadiator", (1.3, 5.0, 0.05), (0, -2.6, 5.3), P["radiator"], root, 0.01)
    for i in range(11):
        seam(f"DorsalRadiatorRib{i}", (1.2, 0.025, 0.02), (0, -5.0 + i * 0.48, 5.335), P, root)
    _ = rad
    cylinder("HatchRing", 0.5, 0.06, (0, 2.9, 5.08), P["dark"], root, axis="Z", segments=24)
    cylinder("Hatch", 0.42, 0.12, (0, 2.9, 5.12), P["grey"], root, axis="Z", segments=24, bev=0.02)
    cylinder("Mast", 0.03, 1.1, (0.5, 0.2, 5.55 + 0.1), P["steel"], root, axis="Z", segments=8)
    cylinder("BeaconBase", 0.13, 0.06, (0, 1.2, 5.18), P["dark"], root, axis="Z", segments=16)
    cylinder("Beacon", 0.1, 0.16, (0, 1.2, 5.29), P["amber"], root, axis="Z", segments=16)
    for k, (x, y, glow) in enumerate(((-2.1, -6.15, 4), (2.1, -6.15, 5), (-1.55, 5.9, None), (1.55, 5.9, None))):
        z = 4.55 if y < 0 else 3.95
        rcs_quad(f"Rcs{k}", (x, y, z), (1 if x > 0 else -1, 0, 0), P, root, glow_k=glow)
    landing_lamps(P, root, [(0, 5.2, 1.0), (-1.4, -5.6, 0.82), (1.4, -5.6, 0.82)])

    # Stencils.
    for s in (-1, 1):
        f = "+X" if s > 0 else "-X"
        stencil(f"Name{s}", C_NAME, 0.36, (s * 2.512, -1.6, 2.25), f, P["stencil"], root, max_width=4.4)
        stencil(f"Number{s}", C_NUMBER, 1.0, (s * 2.512, -1.6, 3.3), f, P["stencil"], root, max_width=3.6)
        stencil(f"NoStep{s}", "NO STEP", 0.2, (s * 0.95, 3.6, 5.05), "+Z", P["stencil"], root, read=math.radians(90 if s > 0 else -90))
    stencil("NameRear", C_NUMBER, 0.24, (0, C_REAR - C_RAMP_T - 0.035, C_BAYTOP + 0.1), "-Y", P["stencilwhite"], root, max_width=2.6)
    stencil("NoseName", C_NAME, 0.2, (0, 7.02, 2.2), "+Y", P["stencilwhite"], root, max_width=1.8)
    parts["descent_alt"] = 7.5
    parts["fold"] = (90.0, 90.0)  # as Halyard: front legs fold aft, rear forward, under the outriggers; pads level
    return parts


BUILDERS = {"a": option_a, "b": option_b, "c": option_c}
TITLES = {"a": "Lander", "b": "Hopper", "c": "Freighter"}

# ---------------------------------------------------------------- poses, plumes


def glow(name, strength):
    m = bpy.data.materials.get(name)
    if m:
        m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = strength


def add_plumes(parts, P):
    """Descent previews: an orange flame and a white-hot core under each lift engine, a ground light, a dust ring."""
    root = parts["root"]
    outer = bpy.data.materials.get("PlumeOuter") or plume_material("PlumeOuter", (1.0, 0.48, 0.14), 2.2, 1.6)
    core = bpy.data.materials.get("PlumeCore") or plume_material("PlumeCore", (1.0, 0.88, 0.62), 6.0, 3.0)
    alt = parts["descent_alt"]
    objs = []
    for k, (b, exit_c, r_e) in enumerate(parts["bells"]):
        length = exit_c.z + alt - 0.05
        for nm, mat, r1, r2, ln in (("Outer", outer, r_e * 0.95, r_e * 2.3 + 0.12 * length, length),
                                    ("Core", core, r_e * 0.62, r_e * 0.25, min(length, 2.6))):
            bm = bmesh.new()
            bmesh.ops.create_cone(bm, cap_ends=False, segments=24, radius1=r2, radius2=r1, depth=ln)
            o = mesh_object(f"Plume{nm}{k}", bm, mat, root, (exit_c.x, exit_c.y, exit_c.z - ln / 2), smooth=True)
            o.visible_shadow = False
            objs.append(o)
        light = bpy.data.lights.new(f"Burn{k}", "POINT")
        light.energy = 1400
        light.color = (1.0, 0.55, 0.22)
        light.shadow_soft_size = 0.6
        lo = bpy.data.objects.new(f"Burn{k}", light)
        lo.location = (exit_c.x, exit_c.y, exit_c.z - 1.0)
        link(lo, root)
        objs.append(lo)
    # Dust ring on the ground (in world space under the ship, parented so it travels with the lineup offset).
    dm = bpy.data.materials.get("Dust") or dust_material()
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=40, radius1=9.5, radius2=3.0, depth=1.6)
    for v in bm.verts:
        v.co.z += 0.8 - 0.3 * (v.co.x ** 2 + v.co.y ** 2) ** 0.5 / 9.5
    dust = mesh_object("DustRing", bm, dm, root, (0, 0, -alt), smooth=True)
    dust.visible_shadow = False
    objs.append(dust)
    for o in objs:
        o.hide_render = True
    parts["plumes"] = objs


def fold_legs(legs, feet, fold, f):
    """The game's leg fold at share f (0 = down, 1 = folded): front legs (0, 1) turn -leg about local X, rear legs
    +leg; their feet turn the other way by the foot angle (= leg keeps the pads level). DropShipSpec.LegFold/FootFold."""
    leg, foot = fold
    for k, (lg, ft) in enumerate(zip(legs, feet)):
        sgn = -1.0 if k < 2 else 1.0
        lg.rotation_euler.x = sgn * math.radians(leg) * f
        ft.rotation_euler.x = -sgn * math.radians(foot) * f


def set_pose(parts, pose):
    """landed: legs down, ramp closed, idle. open: landed with the ramp (and tip) down. descent: alt up, burning.
    flight: as descent with the legs folded (the climb-out)."""
    root = parts["root"]
    root.location.z = parts["descent_alt"] if pose in ("descent", "flight") else 0.0
    fold_legs(parts["legs"], parts["feet"], parts["fold"], 1.0 if pose == "flight" else 0.0)
    if pose == "flight":
        pose = "descent"
    if pose == "open":
        parts["ramp"].rotation_euler.x = parts["ramp_open"]
        if parts["tip"] is not None:
            parts["tip"].rotation_euler.x = math.pi
    else:
        parts["ramp"].rotation_euler.x = 0.0
        if parts["tip"] is not None:
            parts["tip"].rotation_euler.x = 0.0
    for o in parts["plumes"]:
        o.hide_render = pose != "descent"
    for k in range(4):
        glow(f"Thruster{k}", 40.0 if pose == "descent" else 0.25)
    for k in (4, 5):
        glow(f"Thruster{k}", 6.0 if pose == "descent" else 0.25)
    glow("BayLight", 6.0 if pose == "open" else 2.0)


# ---------------------------------------------------------------- game export (--game)

GAME_DIR = os.path.join(REPO, "game", "models", "vehicles")
PIVOTS = ["Ramp", "RampTip"] + [f"Leg{k}" for k in range(4)] + [f"Foot{k}" for k in range(4)]  # animated empties
KEEP = [f"Thruster{k}" for k in range(6)] + ["BayLight", "Beacon"]  # driven meshes, exported as their own nodes


def game_path(key):
    return os.path.join(GAME_DIR, f"dropship_prewarp_{key}.glb")


def join_for_game(root):
    """build_dropship.py's join_for_game for the pre-warp ships: every modifier applied, the boolean cutters dropped,
    then one mesh per pivot (root -> "Body", Ramp -> "RampBody", RampTip -> "RampTipBody", LegK -> "LegKBody",
    FootK -> "FootKBody"), one material slot per material. KEEP meshes and the decal marker empties stay as they are;
    empties left with nothing under them (grid fin frames) go. Every part first gets one UV layer called "UVMap" (the
    join keeps only the active object's layers by name)."""
    meshes = [o for o in root.children_recursive if o.type == "MESH" and not o.hide_render]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    applied = {o: bpy.data.meshes.new_from_object(o.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
               for o in meshes if o.modifiers}
    for o, me in applied.items():
        o.modifiers.clear()
        o.data = me
    for o in [o for o in root.children_recursive if o.hide_render]:
        bpy.data.objects.remove(o)

    def pivot_of(o):
        p = o.parent
        while p is not None and p is not root and p.name not in PIVOTS:
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
    for pivot, objs in groups.items():
        for o in objs:  # straight under the pivot first (some parts hang under helper empties)
            mw = o.matrix_world.copy()
            o.parent = pivot
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.matrix_world = mw
        bpy.context.view_layer.update()
        target = objs[0]
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = target
        if len(objs) > 1:
            bpy.ops.object.join()
        target.name = "Body" if pivot is root else f"{pivot.name}Body"
        target.data.name = target.name
        target.data.transform(target.matrix_basis)
        target.matrix_basis = Matrix.Identity(4)
    markers = set(V["MARKERS"].values())
    while True:
        bare = [o for o in root.children_recursive if o.type == "EMPTY" and not o.children and o.name not in PIVOTS
                and o.name not in markers]
        if not bare:
            break
        for o in bare:
            bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action="DESELECT")
    names = sorted(o.name for o in root.children_recursive)
    missing = sorted(set(KEEP + PIVOTS) - set(names))
    print(f"joined for the game: {len(names) + 1} nodes:", ", ".join(names), flush=True)
    if missing:
        print("  (no", ", ".join(missing) + ")", flush=True)


def _principled(m):
    return m.node_tree.nodes.get("Principled BSDF") if m and m.use_nodes else None


def bake_colours(root, res, image_name):
    """The procedural paints (scuffed white, foam, tiles, foil, hazard stripes) don't survive glTF: unwrap every
    joined body into one shared atlas (multi-object smart UV, like building_kit.bake_grime), EMIT-bake each material's
    own Base Color input into it, then give every procedural material Base Color = the atlas (roughness = its
    middle value, the bump dropped). Flat materials (Owner, lamps, glass, steel...) stay as they are."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 4
    targets = [o for o in root.children_recursive if o.type == "MESH" and o.name not in KEEP]
    mats = {s.material for o in targets for s in o.material_slots if s.material}
    procedural = [m for m in mats if (p := _principled(m)) is not None and p.inputs["Base Color"].is_linked]
    bpy.ops.object.select_all(action="DESELECT")
    for o in targets:
        o.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.002, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    image = bpy.data.images.new(image_name, res, res)
    copies = {}
    for m in mats:
        c = m.copy()
        nt = c.node_tree
        p = nt.nodes.get("Principled BSDF")
        emit = nt.nodes.new("ShaderNodeEmission")
        if p is not None and p.inputs["Base Color"].is_linked:
            nt.links.new(p.inputs["Base Color"].links[0].from_socket, emit.inputs["Color"])
        elif p is not None:
            emit.inputs["Color"].default_value = p.inputs["Base Color"].default_value
        out = nt.nodes.get("Material Output") or nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(emit.outputs[0], out.inputs["Surface"])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        nt.nodes.active = tex
        copies[m] = c
    saved = {}
    for o in targets:
        saved[o] = [s.material for s in o.material_slots]
        for s in o.material_slots:
            if s.material:
                s.material = copies[s.material]
    t = time.time()
    scene.cycles.bake_type = "EMIT"
    bpy.ops.object.bake(type="EMIT", margin=4, use_clear=True)
    print(f"baked the paint atlas {res} for {len(targets)} bodies, {len(procedural)} procedural materials "
          f"in {time.time() - t:.0f} s", flush=True)
    for o, ms in saved.items():
        for s, m in zip(o.material_slots, ms):
            s.material = m
    for c in copies.values():
        bpy.data.materials.remove(c)
    image.pack()
    for m in procedural:
        nt = m.node_tree
        p = _principled(m)
        for name in ("Base Color", "Normal", "Roughness"):
            inp = p.inputs[name]
            if not inp.is_linked:
                continue
            src = inp.links[0].from_node
            if name == "Roughness":
                lo = src.inputs.get("To Min")
                hi = src.inputs.get("To Max")
                inp.default_value = (lo.default_value + hi.default_value) / 2 if lo and hi else 0.45
            nt.links.remove(inp.links[0])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])


def export_glb(root, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        if not o.hide_render:
            o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_lights=False, export_cameras=False, export_image_format="JPEG",
                              export_jpeg_quality=86, export_image_quality=86)
    tris = sum(len(p.vertices) - 2 for o in root.children_recursive if o.type == "MESH" for p in o.data.polygons)
    nodes = 1 + len(root.children_recursive)
    print(f"exported {path}: {os.path.getsize(path) / 1e6:.2f} MB, {nodes} nodes, {tris} triangles", flush=True)


def godot(v):
    """Blender (x, y, z) -> Godot (x, z, -y)."""
    return (round(v[0], 3), round(v[2], 3), round(-v[1], 3))


def print_spec(parts):
    """The numbers DropShipSpec.cs needs, in Godot model space (front -Z, up +Y, aft +Z)."""
    ramp, tip = parts["ramp"], parts["tip"]
    bpy.context.view_layer.update()
    hinge = ramp.matrix_world.translation
    L1 = tip.location.z if tip is not None else max(z for _, z in parts["toe"])
    L = max(z for _, z in parts["toe"])
    a = RAMP_GAME
    toe_y = hinge.y - (L * math.sin(a))  # Blender: the ramp swings aft (-Y) and down
    toe_z = hinge.z + L * math.cos(a)
    print(f"SPEC {parts['key'].upper()}: hinge {godot(hinge)}  ramp {L1:.3f} + tip {L - L1:.3f}  "
          f"toe at 109.6: ({0:.2f}, {toe_z:.3f}, {-toe_y:.3f})  fold leg {parts['fold'][0]:.1f} foot {parts['fold'][1]:.1f}", flush=True)
    for k, ft in enumerate(parts["feet"]):
        w = ft.matrix_world.translation
        print(f"  Foot{k} pad {godot((w.x, w.y, 0.0))}", flush=True)
    for b, ex, r in parts["bells"]:
        print(f"  {b.name} exit {godot(ex)} r {r}", flush=True)


def game_export(key, res):
    """One option as the game model: --blank markers, landed rest pose (legs down, ramp closed, tip folded),
    joined, paint baked, written to game/models/vehicles/dropship_prewarp_<key>.glb."""
    t = time.time()
    V["reset"]()
    V["BLANK"] = True
    V["NUMBER_DIGITS"] = 5  # number markers sized for "UE-07"
    P = prewarp_palette()
    parts = BUILDERS[key](P)
    parts["ramp_open"] = ramp_angle(parts["hinge_z"], parts["toe"])
    set_pose(parts, "landed")
    print_spec(parts)
    join_for_game(parts["root"])
    bake_colours(parts["root"], res, f"Prewarp{key.upper()}Paint")
    export_glb(parts["root"], game_path(key))
    print(f"option {key.upper()} exported in {time.time() - t:.0f} s", flush=True)


# ---------------------------------------------------------------- scene, props, render


def make_collection(name):
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    return c


def move_to(objs, coll):
    for o in objs:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        coll.objects.link(o)


def import_glb(path, coll):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    move_to(new, coll)
    roots = [o for o in new if o.parent is None]
    holder = bpy.data.objects.new(os.path.basename(path).split(".")[0] + "Holder", None)
    coll.objects.link(holder)
    for r in roots:
        r.parent = holder
    return holder


def hex_outline(name="HexOutline", radius=25 / math.sqrt(3)):
    bm = bmesh.new()
    outer = [bm.verts.new((math.sin(math.radians(60 * k)) * radius, math.cos(math.radians(60 * k)) * radius, 0.01)) for k in range(6)]
    inner = [bm.verts.new((math.sin(math.radians(60 * k)) * (radius - 0.25), math.cos(math.radians(60 * k)) * (radius - 0.25), 0.01)) for k in range(6)]
    for k in range(6):
        j = (k + 1) % 6
        bm.faces.new((outer[k], outer[j], inner[j], inner[k]))
    o = mesh_object(name, bm, material("HexLine", (0.9, 0.85, 0.5), 0, 0.5, emission=(0.9, 0.85, 0.5), strength=0.6), None)
    o.visible_shadow = False
    return o


def setup_world():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.cycles.transparent_max_bounces = 12
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.render.film_transparent = False
    scene.render.image_settings.compression = 90
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
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400)
    bm.to_mesh(ground)
    ground.materials.append(material("Sand", (0.36, 0.27, 0.18), 0.0, 0.95))
    link(bpy.data.objects.new("Ground", ground))
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    return cam


def aim(cam, pos, target, lens):
    cam.location = pos
    cam.data.lens = lens
    cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()


def render(path):
    bpy.context.scene.render.filepath = path
    t = time.time()
    bpy.ops.render.render(write_still=True)
    print(f"rendered {path} in {time.time() - t:.0f} s", flush=True)


# Per-option shot framing: hero (from the ramp side, rear-left 3/4) and descent (front-right, low).
FRAMING = {
    "a": {"hero": ((-20.0, -27.0, 9.0), (0.3, -5.0, 3.4), 35), "descent": ((16.0, 21.0, 3.0), (0, 0, 7.6), 30)},
    "b": {"hero": ((-24.0, -30.0, 11.0), (0.3, -5.5, 5.2), 34), "descent": ((18.0, 24.0, 3.4), (0, 0, 10.2), 30)},
    "c": {"hero": ((-17.0, -25.0, 7.5), (0.3, -4.2, 2.4), 35), "descent": ((15.0, 20.0, 3.0), (0, 0, 6.6), 30)},
}


def crawler_spot(parts):
    """Where the hero shot's mining crawler sits: on the ground just past the ramp toe, heading away (-Y)."""
    a = parts["ramp_open"]
    toe_y = parts["ramp"].matrix_world.translation.y + min(y * math.cos(a) - z * math.sin(a) for y, z in parts["toe"])
    return Vector((0.35, toe_y - 3.4, 0.0))


def main():
    t0 = time.time()
    if GAME:
        for key in OPTIONS:
            game_export(key, BAKE_RES)
        print(f"done in {time.time() - t0:.0f} s", flush=True)
        return
    V["reset"]()
    V["BLANK"] = BLANK
    P = prewarp_palette()
    cam = setup_world() if PREVIEW else None
    builds = {}
    for key in OPTIONS:
        parts = BUILDERS[key](P)
        parts["ramp_open"] = ramp_angle(parts["hinge_z"], parts["toe"])
        add_plumes(parts, P)
        coll = make_collection(f"Option{key.upper()}")
        move_to([parts["root"]] + list(parts["root"].children_recursive), coll)
        parts["coll"] = coll
        builds[key] = parts
        toe_game = min(parts["hinge_z"] + y * math.sin(RAMP_GAME) + z * math.cos(RAMP_GAME) for y, z in parts["toe"])
        print(f"option {key.upper()} {TITLES[key]}: ramp opens {math.degrees(parts['ramp_open']):.1f} deg "
              f"(at the game's 109.6 the toe is {toe_game:+.2f} above the ground); "
              f"{len(parts['root'].children_recursive)} objects", flush=True)
        bpy.context.view_layer.update()
        pts = [o.matrix_world @ Vector(c) for o in parts["root"].children_recursive if o.type == "MESH" and not o.hide_render
               and not o.name.startswith(("Plume", "DustRing")) for c in o.bound_box]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        print(f"  extent x {lo.x:.2f}..{hi.x:.2f}  y {lo.y:.2f}..{hi.y:.2f}  z {lo.z:.2f}..{hi.z:.2f}", flush=True)
        for b, ex, r in parts["bells"]:
            print(f"  {b.name}: exit ({ex.x:.2f}, {ex.y:.2f}, {ex.z:.2f}) r {r}", flush=True)
    print(f"built in {time.time() - t0:.0f} s", flush=True)
    if arg("--blend"):
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(arg("--blend")))
    if not PREVIEW:
        return
    os.makedirs(PREVIEW, exist_ok=True)

    # Props: mining crawler (hero), Mk II rover + hex (game), today's Halyard (lineups).
    props = make_collection("Props")
    vehicles = os.path.join(REPO, "game", "models", "vehicles")
    crawler = import_glb(os.path.join(vehicles, "mining_crawler.glb"), props)
    rover = import_glb(os.path.join(vehicles, "survey_rover_mk2.glb"), props)
    halyard = import_glb(os.path.join(vehicles, "dropship.glb"), props)
    hexes = [hex_outline(f"Hex{i}") for i in range(4)]
    move_to(hexes, props)
    prop_objs = {"crawler": [crawler] + list(crawler.children_recursive), "rover": [rover] + list(rover.children_recursive),
                 "halyard": [halyard] + list(halyard.children_recursive)}
    for i, h in enumerate(hexes):
        prop_objs[f"hex{i}"] = [h]
    for objs in prop_objs.values():
        for o in objs:
            o["was_hidden"] = o.hide_render

    def only(keys, props_on):
        for k, p in builds.items():
            p["coll"].hide_render = k not in keys
        for name, objs in prop_objs.items():
            for o in objs:
                o.hide_render = bool(o["was_hidden"]) or name not in props_on

    for key, parts in builds.items():
        root = parts["root"]
        if "hero" in SHOTS:
            set_pose(parts, "open")
            root.location = (0, 0, 0)
            root.rotation_euler.z = 0
            bpy.context.view_layer.update()
            spot = crawler_spot(parts)
            crawler.location = spot
            crawler.rotation_euler = (0, 0, math.pi)
            only([key], ["crawler"])
            pos, target, lens = FRAMING[key]["hero"]
            aim(cam, pos, target, lens)
            render(os.path.join(PREVIEW, f"prewarp-{key}-hero.png"))
        if "game" in SHOTS:
            set_pose(parts, "landed")
            root.location = (0, 0, 0)
            root.rotation_euler.z = math.radians(25)
            rover.location = (9.6, -4.4, 0)
            rover.rotation_euler = (0, 0, math.radians(-20))
            hexes[0].location = (0, 0, 0)
            only([key], ["rover", "hex0"])
            target = Vector((3.5, -1.5, 0))
            aim(cam, target + Vector((0, -0.57, 0.82)) * 46, target, 35)
            render(os.path.join(PREVIEW, f"prewarp-{key}-game.png"))
            root.rotation_euler.z = 0
        if "descent" in SHOTS:
            set_pose(parts, "descent")
            only([key], [])
            pos, target, lens = FRAMING[key]["descent"]
            aim(cam, pos, target, lens)
            render(os.path.join(PREVIEW, f"prewarp-{key}-descent.png"))
            set_pose(parts, "landed")
        if "flight" in SHOTS:  # the climb-out: legs folded as the game folds them (DropShipSpec.LegFold / FootFold)
            set_pose(parts, "flight")
            only([key], [])
            pos, target, lens = FRAMING[key]["descent"]
            aim(cam, pos, target, lens)
            render(os.path.join(PREVIEW, f"prewarp-{key}-flight.png"))
            set_pose(parts, "landed")
        if "glb" in SHOTS and os.path.exists(game_path(key)):
            # The exported game model, posed through its own pivots the way DropShipView does: left landed with the
            # ramp at 109.6 and the tip unfolded, right in flight with the legs folded.
            copies = [import_glb(game_path(key), props), import_glb(game_path(key), props)]
            for c, (x, z, pose) in zip(copies, ((-8.5, 0.0, "open"), (8.5, 6.0, "flight"))):
                c.location = (x, 0, z)
                nodes = {o.name.split(".")[0]: o for o in c.children_recursive}
                if pose == "open":
                    nodes["Ramp"].matrix_basis = nodes["Ramp"].matrix_basis @ Matrix.Rotation(RAMP_GAME, 4, "X")
                    if "RampTip" in nodes:
                        nodes["RampTip"].matrix_basis = nodes["RampTip"].matrix_basis @ Matrix.Rotation(math.pi, 4, "X")
                else:
                    leg, foot = parts["fold"]
                    for k in range(4):
                        sgn = -1.0 if k < 2 else 1.0
                        nodes[f"Leg{k}"].matrix_basis = nodes[f"Leg{k}"].matrix_basis @ Matrix.Rotation(sgn * math.radians(leg), 4, "X")
                        nodes[f"Foot{k}"].matrix_basis = nodes[f"Foot{k}"].matrix_basis @ Matrix.Rotation(-sgn * math.radians(foot), 4, "X")
            only([], [])
            pos, target, lens = {"a": ((-9, -46, 13), (0, 0, 5.0), 32), "b": ((-9, -50, 15), (0, 0, 7.5), 30),
                                 "c": ((-9, -48, 12), (0, 0, 4.5), 30)}[key]
            aim(cam, pos, target, lens)
            render(os.path.join(PREVIEW, f"prewarp-{key}-glb.png"))
            for c in copies:
                for o in [c] + list(c.children_recursive):
                    o.hide_render = True

    def place_lineup(xs):
        """Halyard, A, B, C at xs (so they read left to right from the shot's camera)."""
        halyard.location = (xs[0], 0, 0)
        for i, key in enumerate(["a", "b", "c"]):
            if key in builds:
                set_pose(builds[key], "landed")
                builds[key]["root"].location = (xs[i + 1], 0, 0)
                builds[key]["root"].rotation_euler.z = 0
        for i, h in enumerate(hexes):
            h.location = (xs[i], 0, 0)

    if "lineup" in SHOTS:  # front 3/4 from +Y: +X is on the left
        place_lineup([28.5, 9.5, -9.5, -28.5])
        crawler.location = (0.0, 10.0, 0)
        crawler.rotation_euler = (0, 0, math.radians(-150))
        rover.location = (19.5, 9.0, 0)
        rover.rotation_euler = (0, 0, math.radians(200))
        only(list(builds), ["halyard", "crawler", "rover"])
        aim(cam, (9.0, 74.0, 10.5), (-0.5, 0, 4.4), 34)
        render(os.path.join(PREVIEW, "prewarp-lineup.png"))
    if "lineup-game" in SHOTS:  # the landing camera from -Y: each ship on its own hex, 25 apart like the map
        place_lineup([-37.5, -12.5, 12.5, 37.5])
        crawler.location = (0.0, 12.0, 0)
        crawler.rotation_euler = (0, 0, math.radians(-30))
        rover.location = (-25.0, 12.0, 0)
        rover.rotation_euler = (0, 0, math.radians(15))
        only(list(builds), ["halyard", "crawler", "rover", "hex0", "hex1", "hex2", "hex3"])
        target = Vector((0, 0, 0))
        aim(cam, target + Vector((0, -0.57, 0.82)).normalized() * 170, target, 51)
        render(os.path.join(PREVIEW, "prewarp-lineup-game.png"))
    print(f"done in {time.time() - t0:.0f} s", flush=True)


main()
