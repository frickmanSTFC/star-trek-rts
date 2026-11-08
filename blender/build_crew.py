"""Federation colony crew figures, game-ready (owner 2026-11-07: "new high-detail crew figures (people)").

Five original roles in the spirit of a Federation colony crew (no canon costume or likeness copied):
  engineer   charcoal work jumpsuit, Owner shoulder yoke, utility harness, tool belt with pouches, knee pads, work gloves
  science    slate-blue jumpsuit with light side panels, Owner yoke, sample case on a cross strap, bare hands
  security   near-black jumpsuit under a grey armour vest, Owner shoulder pads, elbow/knee pads, holster, black gloves
  eva        white environment suit: bubble helmet with a gold Visor, life-support backpack, chest unit, HelmetLamp,
             bellows joints, gauntlets, heavy boots, Owner arm/leg bands (for hostile worlds later)
  colonist   ochre tunic with a wrap collar and rolled sleeves, sash, trousers, soft boots, satchel, Owner scarf

Frame (CLAUDE.md "Vehicle models"): Blender +Z up, +Y = front (Godot -Z), 1 unit = 1 world unit = 1 m; origin = between
the feet on the ground. Nominal figure 1.76 tall (variants 0.93-1.06): measured from the glbs, the L1 outpost's crew door
is 1.75 tall, the survey rover Mk I's door runs from a 0.85 sill to a 1.75 top (0.9 tall, step 0.8) and the Mk II's
from 0.99 to 1.57 (step 0.86), so a crew member's head is level with the rover's door top and the waist line of the
hull is at their chest: the vehicles are a little compact for real people, the outpost door fits.

Rig "Rig" (Godot SkeletonProfileHumanoid names, so retargeting needs no bone map): Root, Hips, Spine, Chest, UpperChest,
Neck, Head; per side (Left/Right) Shoulder, UpperArm, LowerArm, Hand, ThumbProximal, ThumbDistal, IndexProximal,
IndexIntermediate, MiddleProximal, MiddleIntermediate (the middle/ring/little finger group), UpperLeg, LowerLeg, Foot, Toes.
Rest pose = A-pose (arms 36 deg from vertical). Actions (30 fps, loop: last key = first, in place, Root never moves):
Idle 90, Walk 32, Run 20, Work 60, Carry 36, Scan 96, Wave 40 frames. See WALK_SPEED etc. for the ground speeds.

Meshes (one glb per role, game/models/crew/<role>.glb, all skinned to Rig):
  <role>        hero body (~7.9-10.3k tris) with shape keys Feminine, Heavy, Slim
  <role>_far    far LOD (~1.2k tris), same shape keys
  HairCrop, HairShort, HairSwept, HairBun, HairCurly, Beard   hair styles (Godot shows one; EVA has HairCrop only)
  Tool (Work), Scanner (Scan), Crate (Carry), SampleCase (science), HelmetLamp (eva, emissive "Lamp")
  empties ToolTip, ScannerTip (RightHand), LampPoint (Head) for effects
Materials: "Crew" = one shared 64x64 palette atlas (base colour + metal/rough) for every fixed colour; "Skin" and "Hair"
(tinted per variant), "Owner" (recoloured per player), "Visor", "Lamp", "Screen", "Glow" (emissive).

Variants (--variant N, the same hash as game/scripts/CrewModels.cs): gender presentation (Feminine 0/0.5/1), height,
build (Heavy/Slim), skin tone (8), hair colour (8), hair style, beard.

Run (bpy 5.x venv; import bpy before bmesh):
  /home/user/.bpyvenv/bin/python blender/build_crew.py --game                          (all roles -> game/models/crew/)
  /home/user/.bpyvenv/bin/python blender/build_crew.py --game --preview blender/previews/crew
  python3 blender/build_crew.py --sheet blender/previews/crew                          (contact sheet, system PIL)
Flags: --role <id>[,<id>], --variant N (previews), --res 900, --samples 24, --shots turn,lineup,poses,walk,run,variants,faces,lod,game
"""
import math
import os
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
ROLES = ["engineer", "science", "security", "eva", "colonist"]


# ---------------------------------------------------------------- contact sheet (system python3 + PIL, no bpy)

def contact_sheet(folder):
    from PIL import Image, ImageDraw
    order = ["crew-lineup", "crew-poses", "crew-variants", "crew-faces", "crew-walk", "crew-run", "crew-lod", "crew-game"]
    order += [f"{r}-turn" for r in ROLES]
    files = [os.path.join(folder, f"{n}.png") for n in order if os.path.exists(os.path.join(folder, f"{n}.png"))]
    w = 900
    tiles = []
    for f in files:
        im = Image.open(f).convert("RGB")
        im = im.resize((w, int(im.height * w / im.width)))
        tiles.append((os.path.basename(f)[:-4], im))
    cols = 2
    rows = [tiles[i:i + cols] for i in range(0, len(tiles), cols)]
    heights = [max(t[1].height for t in r) + 28 for r in rows]
    sheet = Image.new("RGB", (cols * w + (cols + 1) * 12, sum(heights) + 12 * (len(rows) + 1) + 40), (18, 20, 24))
    d = ImageDraw.Draw(sheet)
    d.text((12, 12), "Federation colony crew - blender/build_crew.py", fill=(230, 230, 230))
    y = 40
    for r, h in zip(rows, heights):
        x = 12
        for name, im in r:
            sheet.paste(im, (x, y + 22))
            d.text((x, y + 4), name, fill=(200, 205, 215))
            x += w + 12
        y += h + 12
    out = os.path.join(folder, "crew-sheet.png")
    sheet.save(out)
    print("sheet", out, sheet.size)


if "--sheet" in argv:
    contact_sheet(arg("--sheet"))
    sys.exit(0)

import bpy  # noqa: E402  first bpy, then bmesh/mathutils
from mathutils import Matrix, Vector, Quaternion  # noqa: E402

V = Vector
GAME = "--game" in argv
OUT_DIR = arg("--out-dir") or os.path.join(REPO, "game", "models", "crew")
PREVIEW = arg("--preview")
VARIANT = int(arg("--variant", "-1"))
RES = int(arg("--res", "900"))
SAMPLES = int(arg("--samples", "24"))
SHOTS = [s.strip() for s in arg("--shots", "turn,lineup,poses,walk,run,variants,faces,lod,game").split(",") if s.strip()]
ONLY = [r for r in (arg("--role") or ",".join(ROLES)).split(",") if r]
FPS = 30
FACE_LENS = float(arg("--face-lens", "70"))
LINEUP_ROVER_YAW = float(arg("--rover-yaw", "-90"))
SIG = 0.022  # weight falloff (distance beyond the nearest bone, in m)
TORSO_SEGS, HEAD_LONS, LIMB_SEGS = 28, 28, 12  # hero mesh resolution (budget ~6-10k tris)
BAND_T, BAND_L = 24, 12  # segments of closed bands round the torso / a limb

pi = math.pi
rad = math.radians


def clamp(x, a, b):
    return a if x < a else b if x > b else x


def smooth(e0, e1, x):
    t = clamp((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def gauss(x, s):
    return math.exp(-0.5 * (x / s) ** 2)


def adiff(a, b):
    """Signed difference of two angles in degrees, in (-180, 180]."""
    return (a - b + 180.0) % 360.0 - 180.0


def lerp(a, b, t):
    return a + (b - a) * t


def orth(v, axis):
    """v with its component along the unit vector axis removed, normalised."""
    w = v - axis * v.dot(axis)
    return w.normalized() if w.length > 1e-9 else w


# ---------------------------------------------------------------- variants (keep in step with CrewModels.cs)

SKIN_TONES = [(0.96, 0.80, 0.69), (0.92, 0.74, 0.60), (0.85, 0.64, 0.50), (0.76, 0.55, 0.40),
              (0.63, 0.44, 0.31), (0.52, 0.34, 0.23), (0.40, 0.26, 0.18), (0.28, 0.18, 0.13)]  # sRGB
HAIR_COLOURS = [(0.06, 0.05, 0.05), (0.17, 0.11, 0.07), (0.33, 0.21, 0.12), (0.48, 0.24, 0.12),
                (0.66, 0.50, 0.30), (0.85, 0.72, 0.48), (0.55, 0.53, 0.52), (0.86, 0.85, 0.83)]  # sRGB
HAIR_WEIGHTS = [5, 6, 5, 2, 2, 2, 1, 1]
HAIR_STYLES = ["HairCrop", "HairShort", "HairSwept", "HairBun", "HairCurly"]
STYLE_MASC = [30, 35, 15, 0, 15]  # + 5 bald
STYLE_FEM = [5, 15, 20, 40, 20]


def h32(seed, k):
    """lowbias32 of seed * golden + k: the same bits as CrewModels.Hash."""
    x = (seed * 0x9E3779B9 + k * 0x85EBCA6B + 0x632BE5AB) & 0xFFFFFFFF
    x ^= x >> 16
    x = (x * 0x7FEB352D) & 0xFFFFFFFF
    x ^= x >> 15
    x = (x * 0x846CA68B) & 0xFFFFFFFF
    x ^= x >> 16
    return x / 4294967296.0


def pick(weights, r):
    total = sum(weights)
    acc = 0.0
    for i, w in enumerate(weights):
        acc += w / total
        if r < acc:
            return i
    return len(weights) - 1


def variant(seed, role):
    r = [h32(seed, k) for k in range(10)]
    fem = 0.0 if r[0] < 0.45 else 1.0 if r[0] < 0.9 else 0.5
    height = (0.97 + 0.09 * r[1]) if fem < 0.5 else (0.93 + 0.08 * r[1])
    heavy = slim = 0.0
    if r[2] < 0.25:
        slim = 0.3 + 0.7 * r[3]
    elif r[2] > 0.72:
        heavy = 0.3 + 0.7 * r[3]
    skin = min(7, int(r[4] * 8))
    hair = pick(HAIR_WEIGHTS, r[5])
    styles = STYLE_FEM if fem >= 0.5 else STYLE_MASC
    bald = fem < 0.5 and r[6] > 0.95
    style = None if bald else HAIR_STYLES[pick(styles, r[6] / 0.95 if fem < 0.5 else r[6])]
    beard = fem < 0.5 and r[7] < 0.25
    if role == "eva":
        style, beard = "HairCrop", False
    return dict(fem=fem, height=height, heavy=heavy, slim=slim, skin=skin, hair=hair, style=style, beard=beard)


# ---------------------------------------------------------------- palette atlas

SW = {}


def swatch(name, rgb, rough=0.65, metal=0.0):
    SW[name] = (len(SW), rgb, rough, metal)


def palette():
    swatch("seam", (0.012, 0.012, 0.014), 0.85)
    swatch("trim", (0.025, 0.026, 0.03), 0.7)
    swatch("boot", (0.022, 0.02, 0.018), 0.5)
    swatch("sole", (0.008, 0.008, 0.008), 0.9)
    swatch("metal", (0.5, 0.52, 0.55), 0.32, 1.0)
    swatch("gold", (0.72, 0.48, 0.16), 0.28, 1.0)
    swatch("eyewhite", (0.34, 0.31, 0.28), 0.25)
    swatch("iris", (0.022, 0.014, 0.009), 0.1)
    swatch("glove_work", (0.25, 0.15, 0.07), 0.75)
    swatch("glove_black", (0.015, 0.015, 0.017), 0.55)
    swatch("strap", (0.07, 0.05, 0.025), 0.8)
    swatch("buckle", (0.4, 0.4, 0.42), 0.35, 1.0)
    # engineer
    swatch("eng_suit", (0.05, 0.05, 0.055), 0.8)
    swatch("eng_suit2", (0.11, 0.11, 0.115), 0.8)
    swatch("eng_pouch", (0.1, 0.085, 0.05), 0.85)
    swatch("eng_pad", (0.02, 0.02, 0.022), 0.6)
    # science
    swatch("sci_suit", (0.03, 0.065, 0.12), 0.75)
    swatch("sci_suit2", (0.3, 0.32, 0.34), 0.75)
    swatch("sci_case", (0.62, 0.63, 0.64), 0.4)
    swatch("sci_accent", (0.02, 0.25, 0.32), 0.6)
    # security
    swatch("sec_suit", (0.018, 0.018, 0.022), 0.75)
    swatch("sec_vest", (0.085, 0.09, 0.095), 0.5, 0.2)
    swatch("sec_plate", (0.14, 0.15, 0.16), 0.4, 0.4)
    swatch("sec_accent", (0.3, 0.02, 0.02), 0.6)
    # eva
    swatch("eva_suit", (0.68, 0.68, 0.66), 0.65)
    swatch("eva_suit2", (0.25, 0.26, 0.27), 0.7)
    swatch("eva_helmet", (0.78, 0.78, 0.77), 0.3)
    swatch("eva_pack", (0.55, 0.56, 0.57), 0.5)
    swatch("eva_bellows", (0.4, 0.4, 0.4), 0.8)
    swatch("eva_glove", (0.3, 0.3, 0.31), 0.7)
    # colonist
    swatch("col_tunic", (0.3, 0.16, 0.06), 0.85)
    swatch("col_trousers", (0.07, 0.065, 0.055), 0.85)
    swatch("col_sash", (0.16, 0.09, 0.04), 0.85)
    swatch("col_boot", (0.1, 0.05, 0.025), 0.7)
    swatch("col_bag", (0.2, 0.14, 0.07), 0.8)
    # props
    swatch("crate", (0.42, 0.43, 0.42), 0.55)
    swatch("crate_dark", (0.06, 0.065, 0.07), 0.6)
    swatch("tool", (0.55, 0.42, 0.06), 0.45, 0.2)
    swatch("scanner", (0.06, 0.06, 0.065), 0.45, 0.3)
    swatch("scanner_lid", (0.36, 0.37, 0.39), 0.35, 0.8)


ROLE_PAL = {
    "engineer": dict(suit="eng_suit", suit2="eng_suit2", boot="boot", glove="glove_work", accent="gold"),
    "science": dict(suit="sci_suit", suit2="sci_suit2", boot="boot", glove=None, accent="sci_accent"),
    "security": dict(suit="sec_suit", suit2="sec_vest", boot="boot", glove="glove_black", accent="sec_accent"),
    "eva": dict(suit="eva_suit", suit2="eva_suit2", boot="eva_suit2", glove="eva_glove", accent="eva_suit2"),
    "colonist": dict(suit="col_tunic", suit2="col_trousers", boot="col_boot", glove=None, accent="col_sash"),
}

SPECIAL = {"Skin", "Hair", "Owner", "Visor", "Lamp", "Screen", "Glow"}
ATLAS_N = 8  # 8 x 8 swatches of 8 px


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin_to_srgb(c):
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def swatch_uv(name):
    i = SW[name][0]
    return ((i % ATLAS_N) + 0.5) / ATLAS_N, ((i // ATLAS_N) + 0.5) / ATLAS_N


MT = {}


def plain(name, rgb, rough=0.5, metal=0.0, emit=None, strength=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes.get("Principled BSDF")
    p.inputs["Base Color"].default_value = (*rgb, 1.0)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    if emit:
        p.inputs["Emission Color"].default_value = (*emit, 1.0)
        p.inputs["Emission Strength"].default_value = strength
    return m


def materials(tmpdir):
    size = ATLAS_N * 8
    base = bpy.data.images.new("CrewAtlas", size, size, alpha=False)
    mr = bpy.data.images.new("CrewAtlasMR", size, size, alpha=False)
    px = [0.0] * (size * size * 4)
    mx = [0.0] * (size * size * 4)
    for i in range(0, len(px), 4):
        px[i:i + 4] = (0.5, 0.5, 0.5, 1.0)
        mx[i:i + 4] = (1.0, 0.6, 0.0, 1.0)
    for name, (i, rgb, rough, metal) in SW.items():
        cx, cy = i % ATLAS_N, i // ATLAS_N
        s = [lin_to_srgb(c) for c in rgb]
        for y in range(cy * 8, cy * 8 + 8):
            for x in range(cx * 8, cx * 8 + 8):
                k = (y * size + x) * 4
                px[k:k + 4] = (s[0], s[1], s[2], 1.0)
                mx[k:k + 4] = (1.0, rough, metal, 1.0)
    base.pixels = px
    mr.pixels = mx
    for img, fn in ((base, "crew_atlas.png"), (mr, "crew_atlas_mr.png")):
        img.filepath_raw = os.path.join(tmpdir, fn)
        img.file_format = "PNG"
        img.save()
    mr.colorspace_settings.name = "Non-Color"
    m = bpy.data.materials.new("Crew")
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes.get("Principled BSDF")
    t1 = nt.nodes.new("ShaderNodeTexImage")
    t1.image = base
    t1.interpolation = "Closest"
    t2 = nt.nodes.new("ShaderNodeTexImage")
    t2.image = mr
    t2.interpolation = "Closest"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(t1.outputs["Color"], p.inputs["Base Color"])
    nt.links.new(t2.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], p.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], p.inputs["Metallic"])
    MT["Crew"] = m
    lin = lambda c: tuple(srgb_to_lin(x) for x in c)  # noqa: E731
    MT["Skin"] = plain("Skin", lin(SKIN_TONES[3]), 0.55)
    p = MT["Skin"].node_tree.nodes.get("Principled BSDF")
    p.inputs["Subsurface Weight"].default_value = 0.15
    p.inputs["Subsurface Radius"].default_value = (0.012, 0.005, 0.003)
    MT["Hair"] = plain("Hair", lin(HAIR_COLOURS[1]), 0.6)
    MT["Owner"] = plain("Owner", lin((0.86, 0.64, 0.2)), 0.45)
    MT["Visor"] = plain("Visor", (0.32, 0.2, 0.05), 0.06, 1.0)
    MT["Lamp"] = plain("Lamp", (1.0, 0.95, 0.85), 0.2, emit=(1.0, 0.93, 0.8), strength=8.0)
    MT["Screen"] = plain("Screen", (0.1, 0.5, 0.55), 0.2, emit=(0.25, 0.85, 0.9), strength=3.0)
    MT["Glow"] = plain("Glow", (0.5, 0.85, 1.0), 0.2, emit=(0.45, 0.8, 1.0), strength=6.0)


# ---------------------------------------------------------------- skeleton (rest = A-pose)

SIDES = (("Right", 1), ("Left", -1))
JP = {}      # named rest points and frames used by the geometry
BONES = []   # (name, head, tail, parent, roll_z, connect, deform)


def skeleton():
    JP.clear()
    BONES.clear()

    def bone(name, head, tail, parent, rollz, connect=False, deform=True):
        BONES.append((name, V(head), V(tail), parent, V(rollz), connect, deform))

    trunk = [("Hips", (0, 0.0, 0.95)), ("Spine", (0, -0.008, 1.05)), ("Chest", (0, -0.016, 1.17)),
             ("UpperChest", (0, -0.02, 1.30)), ("Neck", (0, -0.024, 1.47)), ("Head", (0, -0.01, 1.578)),
             ("HeadTop", (0, -0.006, 1.775))]
    for k, p in trunk:
        JP[k] = V(p)
    bone("Root", (0, 0, 0), (0, 0.15, 0), None, (0, 0, 1), deform=False)
    for i in range(len(trunk) - 1):
        name = trunk[i][0]
        bone(name, JP[name], JP[trunk[i + 1][0]], "Root" if i == 0 else trunk[i - 1][0], (0, -1, 0), connect=i > 0)
    for side, s in SIDES:
        sh0 = V((s * 0.025, -0.006, 1.432))
        sh = V((s * 0.178, -0.028, 1.425))
        a = rad(36)
        u = V((s * math.sin(a), -0.02, -math.cos(a))).normalized()
        el = sh + u * 0.285
        f = (u * math.cos(rad(14)) + V((0, 1, 0)) * math.sin(rad(14))).normalized()
        wr = el + f * 0.255
        hf = f
        npalm = orth(V((-s, 0, 0)), hf)
        ath = hf.cross(npalm) * s
        kn = wr + hf * 0.088
        JP[side] = dict(clav=sh0, sh=sh, el=el, wr=wr, hf=hf, npalm=npalm, ath=ath, kn=kn)
        bone(side + "Shoulder", sh0, sh, "UpperChest", (0, 0, 1))
        # IK-friendly rolls: X = the elbow hinge (cross(chain dir, elbow offset)), as the pose solver builds them
        d = (wr - sh).normalized()
        n = d.cross(orth(el - sh, d)).normalized()
        bone(side + "UpperArm", sh, el, side + "Shoulder", n.cross((el - sh).normalized()), connect=True)
        bone(side + "LowerArm", el, wr, side + "UpperArm", n.cross((wr - el).normalized()), connect=True)
        bone(side + "Hand", wr, kn, side + "LowerArm", npalm, connect=True)
        JP[side]["arm_hinge"] = n
        fingers = []
        for k, (off, rec, ln, spread) in enumerate([(0.029, 0.004, 0.080, 0.06), (0.009, 0.0, 0.087, 0.0),
                                                     (-0.010, 0.003, 0.082, -0.05), (-0.027, 0.012, 0.066, -0.12)]):
            base = kn + ath * off - hf * rec
            dr = (hf + ath * spread).normalized()
            fingers.append((base, dr, ln))
        JP[side]["fingers"] = fingers
        b0, d0, l0 = fingers[0]
        bone(side + "IndexProximal", b0, b0 + d0 * 0.043, side + "Hand", npalm)
        bone(side + "IndexIntermediate", b0 + d0 * 0.043, b0 + d0 * l0, side + "IndexProximal", npalm, connect=True)
        b1, d1, l1 = fingers[1]
        bone(side + "MiddleProximal", b1, b1 + d1 * 0.046, side + "Hand", npalm)
        bone(side + "MiddleIntermediate", b1 + d1 * 0.046, b1 + d1 * l1, side + "MiddleProximal", npalm, connect=True)
        tb = wr + hf * 0.024 + ath * 0.022 + npalm * 0.014
        td = (hf * 0.62 + ath * 0.62 + npalm * 0.48).normalized()
        td2 = (td * 0.8 + hf * 0.35).normalized()
        JP[side]["thumb"] = (tb, td, td2)
        bone(side + "ThumbProximal", tb, tb + td * 0.046, side + "Hand", npalm)
        bone(side + "ThumbDistal", tb + td * 0.046, tb + td * 0.046 + td2 * 0.040, side + "ThumbProximal", npalm, connect=True)
    for side, s in SIDES:
        hip = V((s * 0.092, 0.005, 0.925))
        knee = V((s * 0.098, 0.018, 0.505))
        ankle = V((s * 0.104, -0.012, 0.085))
        ball = V((s * 0.112, 0.125, 0.022))
        toe = V((s * 0.114, 0.2, 0.02))
        JP[side].update(hip=hip, knee=knee, ankle=ankle, ball=ball, toe=toe)
        d = (ankle - hip).normalized()
        n = d.cross(orth(knee - hip, d)).normalized()
        JP[side]["leg_hinge"] = n
        bone(side + "UpperLeg", hip, knee, "Hips", n.cross((knee - hip).normalized()))
        bone(side + "LowerLeg", knee, ankle, side + "UpperLeg", n.cross((ankle - knee).normalized()), connect=True)
        bone(side + "Foot", ankle, ball, side + "LowerLeg", (0, 0, 1), connect=True)
        bone(side + "Toes", ball, toe, side + "Foot", (0, 0, 1), connect=True)


def bone_segments():
    return {b[0]: (b[1], b[2]) for b in BONES}


# ---------------------------------------------------------------- mesh builder


class MB:
    """Plain lists of vertices (with weight candidates or explicit weights) and faces (with a material key)."""

    def __init__(self):
        self.co, self.cand, self.wt, self.faces, self.mats = [], [], [], [], []

    def v(self, p, cand=None, w=None):
        self.co.append(V(p))
        self.cand.append(cand)
        self.wt.append(w)
        return len(self.co) - 1

    def f(self, idx, mat):
        self.faces.append(tuple(idx))
        self.mats.append(mat)

    def tris(self):
        return sum(len(f) - 2 for f in self.faces)


def qnormal(pts):
    if len(pts) == 3:
        return (pts[1] - pts[0]).cross(pts[2] - pts[0])
    return (pts[2] - pts[0]).cross(pts[3] - pts[1])


def sheet(mb, rows, mat, cand=None, closed=False, centres=None, outward=None, w=None, keep=None):
    """Quads between consecutive rows of points (equal lengths; wrap when closed), facing away from centres[i]
    (per row) or along outward(point). w(i, j) -> explicit weights, keep(i, j) -> bool drops quads."""
    idx = [[mb.v(p, cand, w(i, j) if w else None) for j, p in enumerate(r)] for i, r in enumerate(rows)]
    n = len(rows[0])
    quads = []
    for i in range(len(rows) - 1):
        for j in range(n if closed else n - 1):
            if keep and not keep(i, j):
                continue
            k = (j + 1) % n
            quads.append(((idx[i][j], idx[i][k], idx[i + 1][k], idx[i + 1][j]), i, j))
    flip = None
    for q, i, j in sorted(quads, key=lambda e: abs(e[1] - len(rows) / 2) + abs(e[2] - n / 2) * 0.1):
        pts = [mb.co[x] for x in q]
        nn = qnormal(pts)
        if nn.length < 1e-10:
            continue
        mid = sum(pts, V()) / 4
        out = outward(mid) if outward else (mid - centres[i])
        flip = nn.dot(out) < 0
        break
    for q, i, j in quads:
        m = mat(i, j) if callable(mat) else mat
        mb.f(q[::-1] if flip else q, m)
    return idx


def fan(mb, ring, centre, mat, cand=None, outward=None, w=None):
    """Triangle fan closing a ring of vertex indices onto a new centre vertex; faces along outward."""
    c = mb.v(centre, cand, w)
    n = len(ring)
    pts = [mb.co[ring[0]], mb.co[ring[1]], mb.co[c]]
    nn = qnormal(pts)
    flip = nn.dot(outward) < 0
    for j in range(n):
        tri = (ring[j], ring[(j + 1) % n], c)
        mb.f(tri[::-1] if flip else tri, mat)


def rim(mb, top, base, mat, cand, centroid):
    """Side wall between two equal-length index loops (a raised panel's edge), each quad facing out of the panel."""
    n = len(top)
    for j in range(n - 1):
        q = (top[j], top[j + 1], base[j + 1], base[j])
        pts = [mb.co[x] for x in q]
        nn = qnormal(pts)
        if nn.length < 1e-12:
            continue
        mid = sum(pts, V()) / 4
        mb.f(q[::-1] if nn.dot(mid - centroid) < 0 else q, mat)


def boundary(idx, closed):
    """Boundary loops of a sheet's index grid: one loop for an open patch, the first/last rows for a ring."""
    if closed:
        return [idx[0] + [idx[0][0]], idx[-1] + [idx[-1][0]]]
    loop = idx[0][:] + [r[-1] for r in idx[1:]] + idx[-1][::-1][1:] + [r[0] for r in idx[::-1][1:]]
    return [loop]


def obox(mb, c, ax, ay, az, h, mat, cand, w=None, chamfer=0.0):
    """Oriented box at c with unit axes ax/ay/az and half sizes h; chamfer > 0 bevels the edges around az."""
    c = V(c)
    hx, hy, hz = h
    if chamfer <= 0:
        corners = [c + ax * sx * hx + ay * sy * hy + az * sz * hz for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
        ids = [mb.v(p, cand, w) for p in corners]
        for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            pts = [mb.co[ids[i]] for i in q]
            mid = sum(pts, V()) / 4
            qq = [ids[i] for i in q]
            mb.f(qq[::-1] if qnormal(pts).dot(mid - c) < 0 else qq, mat)
        return
    ch = min(chamfer, hx * 0.9, hy * 0.9, hz * 0.9)

    def ring(z, inset):
        X, Y = hx - inset, hy - inset
        q = min(ch, X * 0.95, Y * 0.95)
        pts2 = [(X - q, Y), (-(X - q), Y), (-X, Y - q), (-X, -(Y - q)), (-(X - q), -Y), (X - q, -Y), (X, -(Y - q)), (X, Y - q)]
        return [c + ax * x + ay * y + az * z for x, y in pts2]
    rows = [ring(-hz, ch * 0.7), ring(-hz + ch, 0.0), ring(hz - ch, 0.0), ring(hz, ch * 0.7)]
    idx = sheet(mb, rows, mat, cand, closed=True, outward=lambda m: (m - c) - az * (m - c).dot(az) * 0.5,
                w=(lambda i, j: w) if w else None)
    fan(mb, idx[0], c - az * hz, mat, cand, outward=-az, w=w)
    fan(mb, idx[-1], c + az * hz, mat, cand, outward=az, w=w)


def tube(mb, a, b, r1, r2, segs, mat, cand, caps=(True, True), w=None, up=None):
    a, b = V(a), V(b)
    d = (b - a).normalized()
    up = V(up) if up else (V((0, 0, 1)) if abs(d.z) < 0.9 else V((1, 0, 0)))
    e1 = orth(up, d)
    e2 = d.cross(e1)
    rows = []
    for p, r in ((a, r1), (b, r2)):
        rows.append([p + (e1 * math.cos(2 * pi * j / segs) + e2 * math.sin(2 * pi * j / segs)) * r for j in range(segs)])
    idx = sheet(mb, rows, mat, cand, closed=True, centres=[a, b], w=(lambda i, j: w) if w else None)
    if caps[0]:
        fan(mb, idx[0], a, mat, cand, outward=-d, w=w)
    if caps[1]:
        fan(mb, idx[1], b, mat, cand, outward=d, w=w)


def ellipsoid(mb, c, axes, radii, segs, rings, mat, cand, w=None, deform=None, matf=None):
    """Ellipsoid on the frame axes (x, y, z) with radii; deform(lat, lon, p) -> p; matf(lat, lon) -> material."""
    c = V(c)
    ax, ay, az = axes
    rows = []
    lats = [-90 + 180 * (i + 1) / (rings + 1) for i in range(rings)]
    for la in lats:
        row = []
        for j in range(segs):
            lo = 360 * j / segs
            d = V((math.cos(rad(la)) * math.cos(rad(lo)), math.cos(rad(la)) * math.sin(rad(lo)), math.sin(rad(la))))
            p = c + ax * d.x * radii[0] + ay * d.y * radii[1] + az * d.z * radii[2]
            if deform:
                p = deform(la, lo, p)
            row.append(p)
        rows.append(row)
    mf = (lambda i, j: matf(lats[i], 360 * j / segs)) if matf else mat
    idx = sheet(mb, rows, mf, cand, closed=True, outward=lambda m: m - c, w=(lambda i, j: w) if w else None)
    bot = c - az * radii[2]
    top = c + az * radii[2]
    if deform:
        bot, top = deform(-90, 0, bot), deform(90, 0, top)
    fan(mb, idx[0], bot, matf(-90, 0) if matf else mat, cand, outward=-az, w=w)
    fan(mb, idx[-1], top, matf(90, 0) if matf else mat, cand, outward=az, w=w)


# ---------------------------------------------------------------- body surfaces

# torso sections: z, half width, front depth, back depth, y centre, squareness
TORSO = [
    (0.795, 0.085, 0.050, 0.060, -0.005, 2.2),
    (0.83, 0.138, 0.076, 0.090, -0.008, 2.4),
    (0.88, 0.168, 0.088, 0.108, -0.012, 2.5),
    (0.94, 0.178, 0.092, 0.112, -0.014, 2.5),
    (1.00, 0.168, 0.094, 0.098, -0.014, 2.5),
    (1.06, 0.152, 0.098, 0.088, -0.012, 2.4),
    (1.12, 0.150, 0.102, 0.088, -0.012, 2.4),
    (1.19, 0.157, 0.108, 0.094, -0.014, 2.5),
    (1.26, 0.166, 0.114, 0.100, -0.018, 2.6),
    (1.33, 0.174, 0.117, 0.104, -0.020, 2.7),
    (1.39, 0.186, 0.106, 0.104, -0.024, 2.9),
    (1.43, 0.198, 0.088, 0.096, -0.026, 3.0),
    (1.465, 0.180, 0.072, 0.082, -0.026, 2.8),
    (1.495, 0.120, 0.060, 0.068, -0.025, 2.4),
    (1.52, 0.072, 0.055, 0.058, -0.024, 2.1),
    (1.535, 0.060, 0.050, 0.054, -0.023, 2.0),
]
TORSO_Z = [0.795, 0.81, 0.83, 0.855, 0.88, 0.91, 0.94, 0.98, 1.02, 1.06, 1.1, 1.14, 1.19, 1.24,
           1.29, 1.33, 1.36, 1.39, 1.41, 1.43, 1.45, 1.465, 1.48, 1.495, 1.51, 1.52, 1.535]


def catmull(vals, zs, z):
    """Catmull-Rom through (zs[i], vals[i]) at z (clamped)."""
    z = clamp(z, zs[0], zs[-1])
    i = 0
    while i < len(zs) - 2 and z > zs[i + 1]:
        i += 1
    t = (z - zs[i]) / (zs[i + 1] - zs[i])
    p0 = vals[max(0, i - 1)]
    p1, p2 = vals[i], vals[i + 1]
    p3 = vals[min(len(vals) - 1, i + 2)]
    t2, t3 = t * t, t * t * t
    return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


_TZ = [s[0] for s in TORSO]
_TCOL = [[s[k] for s in TORSO] for k in range(6)]


class Body:
    """Morph + role parameters for every surface: fem/heavy/slim in 0..1, inflate (suit thickness)."""

    def __init__(self, fem=0.0, heavy=0.0, slim=0.0, inflate=0.0, limb_inflate=0.0):
        self.fem, self.heavy, self.slim = fem, heavy, slim
        self.inflate, self.limb_inflate = inflate, limb_inflate

    def section(self, z):
        w, f, b, c, n = (catmull(_TCOL[k], _TZ, z) for k in range(1, 6))
        fem, hv, sl = self.fem, self.heavy, self.slim
        w *= 1 - 0.07 * fem * smooth(1.25, 1.38, z) * (1 - smooth(1.47, 1.53, z))
        w *= 1 - 0.09 * fem * gauss(z - 1.09, 0.06)
        w *= 1 + 0.065 * fem * gauss(z - 0.92, 0.06)
        b += 0.012 * fem * gauss(z - 0.9, 0.05)
        w *= 1 + 0.10 * hv * (1 - smooth(1.45, 1.53, z)) - 0.08 * sl * (1 - smooth(1.45, 1.53, z))
        f += 0.012 * hv - 0.008 * sl + 0.035 * hv * gauss(z - 1.12, 0.09)
        b += 0.01 * hv - 0.008 * sl
        w *= 1 - 0.08 * fem * smooth(1.49, 1.53, z) + 0.12 * hv * smooth(1.49, 1.53, z)
        return w, f, b, c, n

    def bump(self, z, th):
        """Radial bumps: chest/bust, belly, seat, shoulder blades."""
        deg = math.degrees(th)
        r = 0.0
        for side in (65.0, 115.0):
            g = gauss(adiff(deg, side), 20.0)
            zc = 1.30 - 0.015 * self.fem
            below = gauss(z - zc, 0.035 + 0.01 * self.fem) if z < zc else gauss(z - zc, 0.05)
            r += g * below * (0.006 + 0.03 * self.fem - 0.004 * self.slim)
        r += self.heavy * 0.012 * gauss(adiff(deg, 90), 45) * gauss(z - 1.12, 0.08)
        for side in (245.0, 295.0):
            r += gauss(adiff(deg, side), 25) * gauss(z - 0.9, 0.05) * (0.012 + 0.012 * self.fem)
            r += gauss(adiff(deg, side), 25) * gauss(z - 1.36, 0.04) * 0.006
        return r

    def torso(self, z, th, extra=0.0):
        """Point on the jumpsuit at height z, angle th (0 = +X right, pi/2 = front); extra = outward offset."""
        w, f, b, c, n = self.section(z)
        cs, sn = math.cos(th), math.sin(th)
        e = 2.0 / n
        x = w * math.copysign(abs(cs) ** e, cs)
        d = f if sn > 0 else b
        y = d * math.copysign(abs(sn) ** e, sn)
        rad_ = V((x, y, 0))
        L = rad_.length
        if L > 1e-9:
            rad_ *= (L + self.bump(z, th) + self.inflate + extra) / L
        return V((rad_.x, c + rad_.y, z))

    def torso_n(self, z, th):
        p = self.torso(z, th)
        a = self.torso(z, th + 0.01) - self.torso(z, th - 0.01)
        b = self.torso(min(z + 0.003, 1.535), th) - self.torso(max(z - 0.003, 0.795), th)
        n = a.cross(b).normalized()
        axis = V((0, self.section(z)[3], z))
        if n.dot(p - axis) < 0:
            n = -n
        return n


class Limb:
    """A tube along a polyline of joints; profile rows (u, side radius, front radius, back radius) by arc length."""

    def __init__(self, joints, profile, s, front=(0, 1, 0), scale=1.0, add=0.0):
        self.j = [V(p) for p in joints]
        self.len = [(self.j[i + 1] - self.j[i]).length for i in range(len(self.j) - 1)]
        self.total = sum(self.len)
        self.prof = profile
        self.s = s
        self.front = V(front)
        self.scale, self.add = scale, add

    def centre(self, u):
        d = u * self.total
        if d <= 0:
            t = (self.j[1] - self.j[0]).normalized()
            return self.j[0] + t * d
        for i, L in enumerate(self.len):
            if d <= L or i == len(self.len) - 1:
                return self.j[i] + (self.j[i + 1] - self.j[i]) * (d / L)
            d -= L

    def tangent(self, u):
        ts = [(self.j[i + 1] - self.j[i]).normalized() for i in range(len(self.len))]
        d = u * self.total
        acc = 0.0
        for i, L in enumerate(self.len[:-1]):
            acc += L
            k = smooth(-0.05, 0.05, d - acc)
            if abs(d - acc) < 0.05:
                return (ts[i] * (1 - k) + ts[i + 1] * k).normalized()
            if d < acc:
                return ts[i]
        return ts[-1]

    def frame(self, u):
        t = self.tangent(u)
        ef = orth(self.front, t)
        es = ef.cross(t)
        if es.x * self.s < 0:
            es = -es
        return t, es, ef

    def radii(self, u):
        us = [p[0] for p in self.prof]
        uu = clamp(u, us[0], us[-1])
        r = [catmull([p[k] for p in self.prof], us, uu) for k in (1, 2, 3)]
        return [x * self.scale + self.add for x in r]

    def pt(self, u, th, extra=0.0):
        c = self.centre(u)
        t, es, ef = self.frame(u)
        rs, rf, rb = self.radii(u)
        rf_ = rf if math.sin(th) > 0 else rb
        return c + es * math.cos(th) * (rs + extra) + ef * math.sin(th) * (rf_ + extra)

    def n(self, u, th):
        p = self.pt(u, th)
        c = self.centre(u)
        a = self.pt(u, th + 0.01) - self.pt(u, th - 0.01)
        b = self.pt(u + 0.004, th) - self.pt(u - 0.004, th)
        nn = a.cross(b).normalized()
        return nn if nn.dot(p - c) > 0 else -nn


def arm_limb(side, s, body):
    J = JP[side]
    k = 1 + 0.12 * body.heavy - 0.1 * body.slim - 0.08 * body.fem
    prof = [(0.0, 0.050, 0.052, 0.050), (0.12, 0.048, 0.050, 0.047), (0.3, 0.044, 0.046, 0.046),
            (0.5, 0.037, 0.038, 0.040), (0.56, 0.040, 0.041, 0.040), (0.72, 0.037, 0.035, 0.036),
            (0.9, 0.029, 0.024, 0.025), (1.0, 0.028, 0.022, 0.023)]
    return Limb([J["sh"], J["el"], J["wr"]], prof, s, scale=k, add=body.limb_inflate)


def leg_limb(side, s, body):
    J = JP[side]
    kt = 1 + 0.1 * body.heavy - 0.1 * body.slim + 0.03 * body.fem
    prof = [(0.0, 0.070, 0.074, 0.080), (0.15, 0.072, 0.075, 0.078), (0.32, 0.064, 0.068, 0.068),
            (0.47, 0.050, 0.054, 0.050), (0.52, 0.048, 0.050, 0.050), (0.62, 0.050, 0.048, 0.062),
            (0.8, 0.040, 0.038, 0.045), (1.0, 0.033, 0.034, 0.036)]
    return Limb([J["hip"], J["knee"], J["ankle"]], prof, s, scale=kt, add=body.limb_inflate)


def u_at_z(limb, z):
    """Leg parameter whose centre is at height z (legs run downward)."""
    lo, hi = 0.0, 1.0
    for _ in range(30):
        m = (lo + hi) / 2
        if limb.centre(m).z > z:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


# ---------------------------------------------------------------- weights

T_CAND = [("Hips", 0), ("Spine", 0), ("Chest", 0), ("UpperChest", 0), ("Neck", 0.01), ("RightShoulder", 0.0),
          ("LeftShoulder", 0.0), ("RightUpperArm", 0.035), ("LeftUpperArm", 0.035), ("RightUpperLeg", 0.012),
          ("LeftUpperLeg", 0.012)]
LOWER_CAND = [("Hips", 0), ("Spine", 0), ("RightUpperLeg", 0.0), ("LeftUpperLeg", 0.0)]


def arm_cand(side):
    return [(side + "Shoulder", 0.025), (side + "UpperArm", 0), (side + "LowerArm", 0), (side + "Hand", 0.015),
            ("UpperChest", 0.04)]


def leg_cand(side):
    return [("Hips", 0.03), (side + "UpperLeg", 0), (side + "LowerLeg", 0), (side + "Foot", 0.01)]


def boot_cand(side):
    return [(side + "LowerLeg", 0), (side + "Foot", 0), (side + "Toes", 0.004)]


def rigid(bone):
    return [(bone, 0.0)]


def seg_dist(p, a, b):
    ab = b - a
    t = clamp((p - a).dot(ab) / ab.length_squared, 0.0, 1.0)
    return (p - (a + ab * t)).length


def solve_weights(mb):
    segs = bone_segments()
    out = []
    for i, p in enumerate(mb.co):
        if mb.wt[i] is not None:
            out.append(mb.wt[i])
            continue
        cand = mb.cand[i] or rigid("Hips")
        if len(cand) == 1:
            out.append({cand[0][0]: 1.0})
            continue
        ds = [(seg_dist(p, *segs[b]) + bias, b) for b, bias in cand]
        dmin = min(d for d, _ in ds)
        ws = sorted(((math.exp(-(d - dmin) / SIG), b) for d, b in ds), reverse=True)[:4]
        tot = sum(w for w, _ in ws)
        out.append({b: w / tot for w, b in ws if w / tot > 0.01})
    return out


# ---------------------------------------------------------------- figure pieces


def torso_mesh(mb, body, mat_at, segs, zs, cand=T_CAND):
    """The torso loft (jumpsuit), closed under the crotch. mat_at(z, theta) -> material key."""
    ths = [2 * pi * j / segs for j in range(segs)]
    rows = [[body.torso(z, th) for th in ths] for z in zs]
    cents = [V((0, body.section(z)[3], z)) for z in zs]
    zm = [(zs[i] + zs[i + 1]) / 2 for i in range(len(zs) - 1)]
    idx = sheet(mb, rows, lambda i, j: mat_at(zm[i], ths[j] + pi / segs), cand, closed=True, centres=cents)
    fan(mb, idx[0], V((0, -0.006, 0.79)), mat_at(0.79, pi / 2), cand, outward=V((0, 0, -1)))
    return idx


def limb_mesh(mb, limb, us, segs, mat_at, cand, dome=True, cap_end=True, extra=0.0):
    ths = [2 * pi * j / segs for j in range(segs)]
    rows, cents = [], []
    if dome:
        t0 = limb.tangent(0.0)
        c0 = limb.centre(0.0)
        r0 = limb.radii(0.0)
        for a in (70, 45, 20):
            ca, sa = math.cos(rad(a)), math.sin(rad(a))
            _, es, ef = limb.frame(0.0)
            c = c0 - t0 * (sa * max(r0) * 0.9)
            rows.append([c + es * math.cos(th) * (r0[0] + extra) * ca + ef * math.sin(th) * ((r0[1] if math.sin(th) > 0 else r0[2]) + extra) * ca for th in ths])
            cents.append(c)
    for u in us:
        rows.append([limb.pt(u, th, extra) for th in ths])
        cents.append(limb.centre(u))
    nd = 3 if dome else 0
    um = [None] * (len(rows) - 1)
    for i in range(len(rows) - 1):
        um[i] = us[0] if i < nd else (us[i - nd] + us[i - nd + 1]) / 2
    idx = sheet(mb, rows, lambda i, j: mat_at(um[i], ths[j] + pi / segs), cand, closed=True, centres=cents)
    if dome:
        t0 = limb.tangent(0.0)
        fan(mb, idx[0], limb.centre(0.0) - t0 * max(limb.radii(0.0)) * 0.9, mat_at(us[0], 0), cand, outward=-t0)
    if cap_end:
        t1 = limb.tangent(us[-1])
        fan(mb, idx[-1], limb.centre(us[-1]) + t1 * 0.004, mat_at(us[-1], 0), cand, outward=t1)
    return idx


def surface_panel(mb, fn, nfn, us, vs, off, mat, cand, edge=True, closed=False, keep=None, base_off=0.0):
    """A raised panel on a parametric surface: fn(u, v, extra) -> point, nfn(u, v) -> normal; off may be a function."""
    offf = off if callable(off) else (lambda u, v: off)
    rows = [[fn(u, v, offf(u, v)) for v in vs] for u in us]
    cen = sum((p for r in rows for p in r), V()) / (len(us) * len(vs))
    # orientation from the surface normal at the patch middle
    um, vm = us[len(us) // 2], vs[len(vs) // 2]
    nmid = nfn(um, vm)
    idx = sheet(mb, rows, mat, cand, closed=closed, outward=lambda m: nmid, keep=keep)
    if edge:
        brow = [[fn(u, v, base_off) for v in vs] for u in us]
        bidx = [[mb.v(p, cand) for p in r] for r in brow]
        for top, base in zip(boundary(idx, closed), boundary(bidx, closed)):
            rim(mb, top, base, mat, cand, cen)
    return idx


def ribbon(mb, fn, nfn, path, n, width, off, mat, cand, depth=0.0, closed=False):
    """A strip following path(t) -> (u, v) on a surface, width across, raised off; depth > 0 adds side walls."""
    pts, nrm = [], []
    for i in range(n + 1):
        t = i / n
        u, v = path(t)
        pts.append(fn(u, v, off))
        nrm.append(nfn(u, v))
    a_row, b_row, a0, b0 = [], [], [], []
    for i, (p, nn) in enumerate(zip(pts, nrm)):
        tg = (pts[min(i + 1, n)] - pts[max(i - 1, 0)])
        side = nn.cross(tg).normalized() * (width / 2)
        a_row.append(p + side)
        b_row.append(p - side)
        a0.append(p + side - nn * depth)
        b0.append(p - side - nn * depth)
    # per-quad orientation along a long strip: use the local normals
    ia = [mb.v(p, cand) for p in a_row]
    ib = [mb.v(p, cand) for p in b_row]
    for i in range(n):
        q = (ia[i], ia[i + 1], ib[i + 1], ib[i])
        pts4 = [mb.co[x] for x in q]
        mb.f(q[::-1] if qnormal(pts4).dot(nrm[i]) < 0 else q, mat)
    if depth > 0:
        ja = [mb.v(p, cand) for p in a0]
        jb = [mb.v(p, cand) for p in b0]
        for top, bot, sgn in ((ia, ja, 1), (ib, jb, -1)):
            for i in range(n):
                q = (top[i], top[i + 1], bot[i + 1], bot[i])
                pts4 = [mb.co[x] for x in q]
                sidev = (mb.co[top[i]] - mb.co[ib[i] if sgn > 0 else ia[i]])
                mb.f(q[::-1] if qnormal(pts4).dot(sidev) < 0 else q, mat)
    return ia, ib


# --- head


HEAD_C = V((0, 0.012, 1.668))
HEAD_LATS = [-78, -68, -58, -50, -43, -37, -32, -27, -22.5, -18, -14, -10, -6, -2, 2, 6, 10, 14, 18, 23, 29, 36, 44,
             53, 63, 74, 84]


def head_lons(n):
    out = []
    for j in range(n):
        x = 2 * j / n - 1  # -1..1, 0 = front
        g = 0.45 * x + 0.55 * x ** 3
        out.append((90 + 180 * g) % 360)
    return out


def head_local(la, lo, body):
    """Head surface point relative to HEAD_C (sculpted ellipsoid: skull, jaw, eye sockets, brow, cheeks, mouth, chin)."""
    cl, sl = math.cos(rad(la)), math.sin(rad(la))
    ch = cl ** 0.72 if sl > 0 else cl
    d = V((ch * math.cos(rad(lo)), ch * math.sin(rad(lo)), sl))
    fem = body.fem
    x = d.x * 0.076
    y = d.y * (0.096 if d.y > 0 else 0.104)
    z = d.z * (0.118 if d.z > 0 else 0.112)
    # flatter face plane
    if y > 0.055:
        y = 0.055 + (y - 0.055) * 0.72
    # jaw and lower face narrow toward the chin and the neck
    low = smooth(0.0, -0.1, z)
    x *= 1 - (0.26 + 0.06 * fem - 0.04 * body.heavy) * low * (0.6 + 0.4 * smooth(-0.02, 0.06, y))
    # underside of the jaw: a tilted plane from the chin back to the neck
    zmin = -0.104 + max(0.0, 0.07 - y) * 0.42
    if z < zmin:
        z = zmin + (z - zmin) * 0.25
    # chin forward, temples in, back of the skull rounder
    y += 0.008 * gauss(z + 0.088, 0.016) * gauss(x, 0.03) * (1 - 0.4 * fem)
    x *= 1 - 0.05 * gauss(z - 0.03, 0.03) * smooth(0.0, 0.05, y)
    if y > 0.03:
        fy = smooth(0.03, 0.07, y)
        for sx in (-1, 1):
            g = math.exp(-((x - sx * 0.033) ** 2 + (z - 0.012) ** 2 * 1.3) / (2 * 0.0135 ** 2))
            y -= 0.012 * g * fy
            y += 0.003 * gauss(x - sx * 0.052, 0.012) * gauss(z + 0.01, 0.014) * fy  # cheekbones
            x += sx * 0.004 * gauss(x - sx * 0.055, 0.012) * gauss(z + 0.012, 0.016)
        y += (0.0055 - 0.003 * fem) * gauss(z - 0.033, 0.0075) * smooth(0.058, 0.03, abs(x)) * fy  # brow ridge
        y += 0.005 * gauss(x, 0.007) * gauss(z - 0.012, 0.012) * fy  # bridge of the nose
        y += 0.009 * gauss(x, 0.026) * gauss(z + 0.05, 0.022) * fy  # muzzle
        y += (0.0028 + 0.001 * fem) * gauss(x, 0.015) * gauss(z + 0.0455, 0.0042) * fy  # upper lip
        y += (0.0036 + 0.0012 * fem) * gauss(x, 0.014) * gauss(z + 0.0595, 0.0048) * fy  # lower lip
        y -= 0.0028 * gauss(x, 0.02) * gauss(z + 0.0525, 0.0022) * fy  # mouth line
        y += 0.004 * gauss(x, 0.016) * gauss(z + 0.086, 0.012) * fy  # chin
    return V((x * (1 + 0.04 * body.heavy), y, z))


def head_pt(la, lo, body, extra=0.0):
    p = head_local(la, lo, body)
    if extra:
        p = p + head_n(la, lo, body) * extra
    return HEAD_C + p


def head_n(la, lo, body):
    a = head_local(la, lo + 0.6, body) - head_local(la, lo - 0.6, body)
    b = head_local(min(la + 0.6, 89.9), lo, body) - head_local(max(la - 0.6, -89.9), lo, body)
    n = a.cross(b)
    p = head_local(la, lo, body)
    if n.length < 1e-12:
        return p.normalized()
    n.normalize()
    return n if n.dot(p) > 0 else -n


_FACE_Y = {}


def face_y(x, z, body):
    """Front surface y (world) at head-local x, z, by searching the lon/lat grid (cached)."""
    key = (round(x, 5), round(z, 5), body.fem, body.heavy, body.slim)
    if key not in _FACE_Y:
        _FACE_Y[key] = _face_y(x, z, body)
    return _FACE_Y[key]


def _face_y(x, z, body):
    best = None
    for la10 in range(-600, 400, 15):
        la = la10 / 10
        for lo10 in range(500, 1300, 20):
            p = head_local(la, lo10 / 10, body)
            dd = (p.x - x) ** 2 + (p.z - z) ** 2
            if best is None or dd < best[0]:
                best = (dd, la, lo10 / 10)
    _, la, lo = best
    for step in (0.6, 0.2, 0.06):
        for _ in range(8):
            moved = False
            for dla, dlo in ((step, 0), (-step, 0), (0, step), (0, -step)):
                p = head_local(la + dla, lo + dlo, body)
                dd = (p.x - x) ** 2 + (p.z - z) ** 2
                q = head_local(la, lo, body)
                if dd < (q.x - x) ** 2 + (q.z - z) ** 2:
                    la, lo, moved = la + dla, lo + dlo, True
            if not moved:
                break
    return HEAD_C.y + head_local(la, lo, body).y


def head_mesh(mb, body, nlon, lats, hair_cap=None):
    lons = head_lons(nlon)
    cand = rigid("Head")
    rows = [[head_pt(la, lo, body) for lo in lons] for la in lats]
    if hair_cap:
        def mat(i, j):
            la = (lats[i] + lats[i + 1]) / 2
            return "Hair" if la > hair_cap(lons[j]) else "Skin"
    else:
        mat = "Skin"
    idx = sheet(mb, rows, mat, cand, closed=True, outward=lambda m: m - HEAD_C)
    fan(mb, idx[0], head_pt(-90, 90, body), "Skin", cand, outward=V((0, 0, -1)))
    fan(mb, idx[-1], head_pt(90, 90, body), "Hair" if hair_cap else "Skin", cand, outward=V((0, 0, 1)))


def face_features(mb, body):
    cand = rigid("Head")
    hl = lambda x, z: face_y(x, z, body)  # noqa: E731
    # nose: sections from the bridge to the base, each an arc from one cheek to the other
    secs = [(0.008, 0.0065, 0.002), (-0.004, 0.008, 0.007), (-0.016, 0.0105, 0.0135), (-0.026, 0.013, 0.0195),
            (-0.032, 0.0175, 0.0165), (-0.037, 0.012, 0.007), (-0.040, 0.007, 0.001)]
    rows = []
    cen = []
    for z, w, p in secs:
        yb = hl(0.0, z) - 0.003
        row = []
        for k in range(7):
            a = pi * k / 6
            x = -w * math.cos(a)
            prot = p * math.sin(a) ** 0.8
            row.append(V((x, yb + prot, HEAD_C.z + z)))
        rows.append(row)
        cen.append(V((0, yb - 0.01, HEAD_C.z + z)))
    sheet(mb, rows, "Skin", cand, centres=cen)
    # nostrils: two dark dents under the tip
    for sx in (-1, 1):
        c = V((sx * 0.0065, hl(0.0, -0.037) + 0.009, HEAD_C.z - 0.0385))
        ellipsoid(mb, c, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.0035, 0.004, 0.0016), 6, 2, "seam", cand)
    # eyes in the sockets, with upper lids
    for sx in (-1, 1):
        ex, ez = sx * 0.033, 0.012
        yf = hl(ex, ez)
        c = V((ex, yf - 0.0085, HEAD_C.z + ez))
        ellipsoid(mb, c, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.0118, 0.0118, 0.0118), 10, 5, "eyewhite", cand,
                  matf=lambda la, lo: "iris" if (la > -45 and la < 45 and abs(adiff(lo, 90 + sx * -8)) < 30 and math.cos(rad(la)) * math.cos(rad(adiff(lo, 90))) > 0.8) else "eyewhite")
        lid = []
        for la in (-2, 22, 50):
            lid.append([c + V((math.cos(rad(la)) * math.cos(rad(lo)), math.cos(rad(la)) * math.sin(rad(lo)), math.sin(rad(la)))) * 0.0132 for lo in range(20, 165, 18)])
        sheet(mb, lid, "Skin", cand, outward=lambda m, c=c: m - c)
        # eyebrow (hair-coloured)
        def brow(t, sx=sx):
            x = sx * (0.014 + 0.036 * t)
            z = 0.033 + 0.006 * math.sin(pi * (0.3 + 0.7 * t)) - 0.004 * t
            return x, z
        pts = []
        for i in range(7):
            x, z = brow(i / 6)
            pts.append(V((x, hl(x, z) + 0.0018, HEAD_C.z + z)))
        a_row = [p + V((0, 0, 0.003 - 0.0012 * abs(i - 2) / 4)) for i, p in enumerate(pts)]
        b_row = [p - V((0, 0, 0.0022)) for p in pts]
        sheet(mb, [a_row, b_row], "Hair", cand, outward=lambda m: V((0, 1, 0)))
    # ears
    for sx in (-1, 1):
        c = HEAD_C + V((sx * 0.074, -0.012, -0.006))
        ax = V((sx * math.cos(rad(18)), -math.sin(rad(18)), 0))  # out of the head, flaring back
        az = V((0, -math.sin(rad(10)), math.cos(rad(10))))
        ay = az.cross(ax) * sx
        ay = orth(ay, ax)

        def ear(la, lo, p, c=c, ax=ax):
            rel = p - c
            out = rel.dot(ax)
            # cup the middle toward the head (concha), keep the rim
            r = math.cos(rad(la))
            if out > 0:
                p = p - ax * out * 0.85 * smooth(0.55, 0.0, abs(la) / 90.0) * (1 if r > 0.4 else 0.4)
            return p
        ellipsoid(mb, c + ax * 0.004, (ax, ay, az), (0.009, 0.019, 0.031), 10, 6, "Skin", cand, deform=ear)


HAIRLINE = [(90, 44), (60, 34), (30, 22), (12, 15), (0, 16), (-30, -8), (-60, -28), (-90, -34)]  # round the ears


def hairline(lo):
    """Lowest latitude of the hair at longitude lo (degrees, 90 = front), symmetric left/right."""
    a = adiff(lo, 0)  # -180..180 from +X
    a = abs(a) if True else a
    # map: 90 front, 0 side, -90 back
    b = 90 - abs(adiff(lo, 90))  # 90 at front, -90 at back
    pts = HAIRLINE
    for (b0, l0), (b1, l1) in zip(pts, pts[1:]):
        if b1 <= b <= b0:
            t = (b - b0) / (b1 - b0)
            t = t * t * (3 - 2 * t)
            return l0 + (l1 - l0) * t
    del a
    return pts[-1][1]


def hair_mesh(mb, body, style, nlon=28, nrow=7):
    """A hair shell over the scalp from the hairline up, raised by the style's volume, with an edge back to the skin."""
    cand = rigid("Head")
    lons = [360 * j / nlon + 90 for j in range(nlon)]

    def low(lo):
        h = hairline(lo)
        if style == "HairSwept":
            h -= 9 * gauss(adiff(lo, 70), 22)  # fringe swept down on the right
        if style == "HairCurly":
            h -= 4
        return h

    def vol(lo, v):
        top = math.sin(v * pi / 2)
        front = gauss(adiff(lo, 90), 50)
        if style == "HairCrop":
            return 0.0035 + 0.002 * top
        if style == "HairShort":
            return 0.005 + 0.012 * top * (0.7 + 0.3 * front)
        if style == "HairSwept":
            return 0.006 + 0.018 * top * (0.6 + 0.6 * gauss(adiff(lo, 110), 40)) + 0.006 * front * (1 - v)
        if style == "HairBun":
            return 0.006 + 0.008 * top
        if style == "HairCurly":
            return 0.016 + 0.024 * top + 0.004 * math.sin(rad(lo) * 9) * math.sin(v * 11)
        return 0.005

    rows = []
    for i in range(nrow + 1):
        v = i / nrow
        row = []
        for lo in lons:
            la = low(lo) + (88 - low(lo)) * v
            row.append(head_pt(la, lo, body, vol(lo, v)))
        rows.append(row)
    idx = sheet(mb, rows, "Hair", cand, closed=True, outward=lambda m: m - HEAD_C)
    fan(mb, idx[-1], head_pt(90, 90, body, vol(90, 1.0) * 0.98), "Hair", cand, outward=V((0, 0, 1)))
    base = [mb.v(head_pt(low(lo) + 1.5, lo, body, 0.0005), cand) for lo in lons]
    rim(mb, idx[0] + [idx[0][0]], base + [base[0]], "Hair", cand, HEAD_C + V((0, 0, 0.12)))
    if style == "HairBun":
        bc = HEAD_C + V((0, -0.112, 0.045))
        ellipsoid(mb, bc, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.032, 0.028, 0.03), 10, 5, "Hair", cand,
                  deform=lambda la, lo, p: p + (p - bc) * 0.08 * math.sin(rad(lo) * 6) * math.cos(rad(la)))
        tube(mb, bc + V((0, 0.02, 0)), bc + V((0, 0.032, 0.0)), 0.022, 0.024, 10, "trim", cand)


def beard_mesh(mb, body):
    cand = rigid("Head")
    lons = [40 + 100 * j / 16 for j in range(17)]
    lats = [-86, -78, -68, -58, -48, -38, -30, -22, -14]

    def keep(i, j):
        la = (lats[i] + lats[i + 1]) / 2
        lo = (lons[j] + lons[j + 1]) / 2
        p = head_local(la, lo, body)
        if abs(p.x) < 0.024 and -0.07 < p.z < -0.038 and p.y > 0.03:  # mouth
            return False
        if la > -30 and abs(adiff(lo, 90)) < 24:  # upper lip/cheek under the nose: moustache below only
            return p.z < -0.04 and abs(p.x) < 0.03 and False
        return la < -12 + 0.0 or abs(adiff(lo, 90)) > 30
    rows = [[head_pt(la, lo, body, 0.0045) for lo in lons] for la in lats]
    sheet(mb, rows, "Hair", cand, outward=lambda m: m - HEAD_C, keep=keep)


# --- hands and boots


def hand_mesh(mb, side, s, mat, glove=False, lod="hero"):
    J = JP[side]
    wr, hf, npalm, ath = J["wr"], J["hf"], J["npalm"], J["ath"]
    g = 0.0016 if glove else 0.0
    hand = side + "Hand"
    if lod == "far":
        c = wr + hf * 0.07
        obox(mb, c, ath, hf, npalm, (0.042 + g, 0.07, 0.018 + g), mat, rigid(hand))
        return
    # palm: chamfered rounded box loft wrist -> knuckles
    rows = []
    cen = []
    for t, wa, wn in ((0.0, 0.031, 0.017), (0.35, 0.041, 0.017), (0.75, 0.044, 0.015), (1.0, 0.043, 0.012)):
        c = wr + hf * (0.088 * t) + npalm * 0.0
        ring = []
        for k in range(10):
            a = 2 * pi * k / 10
            ca, sa = math.cos(a), math.sin(a)
            ring.append(c + ath * (wa + g) * math.copysign(abs(ca) ** 0.7, ca) + npalm * (wn + g) * math.copysign(abs(sa) ** 0.8, sa))
        rows.append(ring)
        cen.append(c)
    idx = sheet(mb, rows, mat, rigid(hand), closed=True, centres=cen)
    fan(mb, idx[0], wr - hf * 0.004, mat, rigid(hand), outward=-hf)
    fan(mb, idx[-1], wr + hf * 0.092, mat, rigid(hand), outward=hf)
    fin = [(side + "IndexProximal", side + "IndexIntermediate")] + [(side + "MiddleProximal", side + "MiddleIntermediate")] * 3
    for k, (base, dr, ln) in enumerate(J["fingers"]):
        prox, inter = fin[k]
        r = (0.0088 if k < 3 else 0.0076) + g
        e1 = ath
        e2 = npalm
        rows, cen, ss = [], [], []
        for sfrac in (-0.012, 0.012, 0.043, 0.06, ln - 0.008):
            c = base + dr * sfrac
            rr = r * (0.92 if sfrac > 0.05 else 1.0)
            rows.append([c + e1 * math.cos(2 * pi * j / 6) * rr + e2 * math.sin(2 * pi * j / 6) * rr * 0.92 for j in range(6)])
            cen.append(c)
            ss.append(sfrac)

        def wf(i, j, ss=ss, prox=prox, inter=inter):
            sv = ss[i]
            wi = smooth(0.034, 0.052, sv)
            wh = smooth(0.012, -0.012, sv)
            wp = max(0.0, 1 - wi - wh)
            d = {hand: wh, prox: wp, inter: wi}
            return {b: w for b, w in d.items() if w > 0.01}
        idx = sheet(mb, rows, mat, None, closed=True, centres=cen, w=wf)
        tip = base + dr * ln
        fan(mb, idx[-1], tip, mat, None, outward=dr, w={inter: 1.0})
    tb, td, td2 = J["thumb"]
    pts = [(tb, 0.0, 0.012), (tb + td * 0.02, 0.02, 0.0115), (tb + td * 0.046, 0.046, 0.0105),
           (tb + td * 0.046 + td2 * 0.022, 0.068, 0.0095), (tb + td * 0.046 + td2 * 0.034, 0.08, 0.008)]
    rows, cen, ss = [], [], []
    for c, sv, r in pts:
        dd = td if sv < 0.046 else td2
        e1 = orth(npalm, dd)
        e2 = dd.cross(e1)
        rows.append([c + e1 * math.cos(2 * pi * j / 6) * (r + g) + e2 * math.sin(2 * pi * j / 6) * (r + g) for j in range(6)])
        cen.append(c)
        ss.append(sv)

    def wt(i, j):
        sv = ss[i]
        wd = smooth(0.036, 0.056, sv)
        wh = smooth(0.012, -0.004, sv)
        wp = max(0.0, 1 - wd - wh)
        d = {hand: wh, side + "ThumbProximal": wp, side + "ThumbDistal": wd}
        return {b: w for b, w in d.items() if w > 0.01}
    idx = sheet(mb, rows, mat, None, closed=True, centres=cen, w=wt)
    fan(mb, idx[-1], tb + td * 0.046 + td2 * 0.042, mat, None, outward=td2, w={side + "ThumbDistal": 1.0})
    fan(mb, idx[0], tb - td * 0.006, mat, None, outward=-td, w={hand: 1.0})
    if glove:
        # gauntlet cuff over the wrist
        c0 = wr - hf * 0.045
        c1 = wr + hf * 0.012
        e1 = ath
        e2 = npalm
        rows = []
        for c, wa, wn in ((c0, 0.036, 0.03), (c1, 0.04, 0.03)):
            rows.append([c + e1 * math.cos(2 * pi * j / 10) * wa + e2 * math.sin(2 * pi * j / 10) * wn for j in range(10)])
        cc = [(side + "LowerArm", 0), (hand, 0)]
        sheet(mb, rows, mat, cc, closed=True, centres=[c0, c1])
        rows2 = [[p for p in rows[1]], [c1 + (p - c1) * 0.85 for p in rows[1]]]
        sheet(mb, rows2, mat, cc, closed=True, outward=lambda m: hf)


def boot_mesh(mb, side, s, body, mat, top_z=0.30, big=0.0, lod="hero", lacing=True, toe_cap=None):
    J = JP[side]
    cand = boot_cand(side)
    ank, ball, toe = J["ankle"], J["ball"], J["toe"]
    fy = V((ball.x - ank.x, ball.y - ank.y, 0)).normalized()
    fx = fy.cross(V((0, 0, 1)))
    o = V((ank.x, ank.y, 0))
    leg = leg_limb(side, s, body)
    nseg = 12 if lod == "hero" else 6
    # shaft: around the shin from top_z down past the ankle
    u0 = u_at_z(leg, top_z)
    us = [u0 + (1.0 - u0) * k / 3 for k in range(4)]
    ths = [2 * pi * j / nseg for j in range(nseg)]
    rows, cen = [], []
    for u in us:
        rows.append([leg.pt(u, th, 0.009 + big) for th in ths])
        cen.append(leg.centre(u))
    for z, back in ((0.055, 0.004), (0.03, 0.008)):
        c = V((ank.x, ank.y - back, z)) - fy * 0.0
        rr = leg.radii(1.0)
        rows.append([c + leg.frame(1.0)[1] * math.cos(th) * (rr[0] + 0.013 + big) + V((0, 1, 0)) * math.sin(th) * ((rr[1] if math.sin(th) > 0 else rr[2]) + 0.03 + big) for th in ths])
        cen.append(c)
    idx = sheet(mb, rows, mat, cand, closed=True, centres=cen)
    top = idx[0]
    inner = [mb.v(leg.pt(us[0], th, 0.002), cand) for th in ths]
    rim(mb, top + [top[0]], inner + [inner[0]], mat, cand, leg.centre(us[0]) - V((0, 0, 0.05)))
    # foot: sections along the foot from the heel to the toe
    k = 1.0 + big * 6
    prof = [(-0.078, 0.034, 0.10), (-0.05, 0.038, 0.12), (0.0, 0.040, 0.12), (0.05, 0.043, 0.095), (0.10, 0.048, 0.072),
            (0.15, 0.048, 0.06), (0.19, 0.04, 0.05), (0.212, 0.022, 0.04)]
    if lod != "hero":
        prof = [prof[i] for i in (0, 2, 4, 6, 7)]
    nsec = 9 if lod == "hero" else 5
    rows, cen = [], []
    z0 = 0.024 + big
    for y, w, h in prof:
        w *= k
        h = h + big * 1.2
        c = o + fy * (y * (1 + big * 2)) + V((0, 0, z0))
        row = []
        for j in range(nsec):
            a = pi * j / (nsec - 1)
            row.append(c + fx * (-w * math.cos(a)) + V((0, 0, (h - z0) * math.sin(a) ** 0.7)))
        rows.append(row)
        cen.append(c + V((0, 0, 0.01)))
    idx = sheet(mb, rows, mat, cand, centres=cen)
    tipc = o + fy * (0.222 * (1 + big * 2)) + V((0, 0, z0 + 0.012))
    fan(mb, idx[-1], tipc, mat, cand, outward=fy)
    # sole: outline under the foot, a thicker heel
    outline = []
    for y, w, h in prof:
        outline.append((y, w * k + 0.005))
    loop = [(y, -w) for y, w in outline] + [(y, w) for y, w in outline[::-1]]
    top_pts = [o + fy * (y * (1 + big * 2)) + fx * x + V((0, 0, z0 + 0.002)) for y, x in loop]
    bot_pts = [o + fy * (y * (1 + big * 2)) + fx * x + V((0, 0, 0.0)) for y, x in loop]
    it = [mb.v(p, cand) for p in top_pts]
    ib = [mb.v(p, cand) for p in bot_pts]
    cen = o + fy * 0.07 + V((0, 0, z0 / 2))
    rim(mb, it + [it[0]], ib + [ib[0]], "sole", cand, cen)
    fan(mb, ib, o + fy * 0.07, "sole", cand, outward=V((0, 0, -1)))
    fan(mb, it, o + fy * 0.07 + V((0, 0, z0)), "sole", cand, outward=V((0, 0, 1)))
    if lod != "hero":
        return
    # heel block
    hc = o + fy * (-0.05) + V((0, 0, 0.004))
    obox(mb, hc, fx, fy, V((0, 0, 1)), (0.036 * k, 0.03, 0.009), "sole", cand)
    # welt line and lacing straps / toe cap
    if lacing:
        for i, y in enumerate((0.02, 0.05, 0.08)):
            c = o + fy * y + V((0, 0, 0.0))
            ang = 0.0
            # strap over the instep at height of the foot profile there
            h = catmull([p[2] for p in prof], [p[0] for p in prof], y) + big
            pts = []
            for j in range(7):
                a = pi * (0.15 + 0.7 * j / 6)
                w = catmull([p[1] for p in prof], [p[0] for p in prof], y) * k
                pts.append(c + fx * (-w * math.cos(a) * 1.04) + V((0, 0, z0 + (h - z0) * math.sin(a) ** 0.7 + 0.003)))
            a_row = [p + fy * 0.007 for p in pts]
            b_row = [p - fy * 0.007 for p in pts]
            sheet(mb, [a_row, b_row], "strap" if i != 1 else "buckle", cand, outward=lambda m: V((0, 0, 1)))
            del ang
    if toe_cap:
        rows = []
        for y in (0.15, 0.18, 0.205):
            w = catmull([p[1] for p in prof], [p[0] for p in prof], y) * k + 0.002
            h = catmull([p[2] for p in prof], [p[0] for p in prof], y) + big + 0.002
            c = o + fy * y + V((0, 0, z0))
            rows.append([c + fx * (-w * math.cos(pi * j / 8)) + V((0, 0, (h - z0) * math.sin(pi * j / 8) ** 0.7)) for j in range(9)])
        sheet(mb, rows, toe_cap, cand, outward=lambda m: m - (o + fy * 0.15 + V((0, 0, 0.03))))


# ---------------------------------------------------------------- per-role figure


def neck_limb(body, inflate=0.0):
    k = 1 + 0.12 * body.heavy - 0.08 * body.fem - 0.06 * body.slim
    prof = [(0.0, 0.056, 0.054, 0.058), (0.5, 0.052, 0.05, 0.056), (1.0, 0.05, 0.048, 0.054)]
    return Limb([JP["Neck"] + V((0, 0.0, 0.0)), V((0, 0.0, 1.615))], prof, 1, scale=k, add=inflate)


def badge(mb, body, z, th, cand):
    """Colony-service badge: a gold hexagon plate with a raised chevron (original design)."""
    p = body.torso(z, th, 0.004)
    n = body.torso_n(z, th)
    up = orth(V((0, 0, 1)), n)
    rt = up.cross(n)
    hexa = [p + (rt * math.cos(rad(30 + 60 * k)) * 0.0135 + up * math.sin(rad(30 + 60 * k)) * 0.016) for k in range(6)]
    top = [mb.v(q + n * 0.002, cand) for q in hexa]
    bot = [mb.v(q - n * 0.002, cand) for q in hexa]
    fan(mb, top, p + n * 0.002, "gold", cand, outward=n)
    rim(mb, top + [top[0]], bot + [bot[0]], "gold", cand, p)
    chev = [(-0.008, -0.006), (0.0, 0.007), (0.008, -0.006), (0.0, -0.001)]
    ct = [mb.v(p + n * 0.0042 + rt * x + up * y, cand) for x, y in chev]
    cb = [mb.v(p + n * 0.0021 + rt * x + up * y, cand) for x, y in chev]
    mb.f(ct[::-1] if qnormal([mb.co[i] for i in ct]).dot(n) < 0 else ct, "metal")
    rim(mb, ct + [ct[0]], cb + [cb[0]], "metal", cand, p)


def pocket_torso(mb, body, z0, z1, th0, th1, mat, flapmat, cand, button=True):
    fn = lambda z, th, e: body.torso(z, th, e)  # noqa: E731
    nf = body.torso_n
    zs = [z0 + (z1 - z0) * k / 3 for k in range(4)]
    ths = [th0 + (th1 - th0) * k / 3 for k in range(4)]
    surface_panel(mb, fn, nf, zs, ths, 0.005, mat, cand)
    fz = [z1 - 0.032, z1 - 0.012, z1 + 0.004]
    fth = [th0 - 0.03 + (th1 - th0 + 0.06) * k / 3 for k in range(4)]
    surface_panel(mb, fn, nf, fz, fth, lambda z, th: 0.008 + 0.002 * (z1 + 0.004 - z) / 0.036, flapmat, cand, base_off=0.005)
    if button:
        thm = (th0 + th1) / 2
        p = body.torso(z1 - 0.022, thm, 0.0105)
        n = body.torso_n(z1 - 0.022, thm)
        tube(mb, p - n * 0.002, p + n * 0.002, 0.0045, 0.004, 6, "metal", cand)


def pocket_limb(mb, limb, u0, u1, th0, th1, mat, flapmat, cand, off=0.006):
    fn = lambda u, th, e: limb.pt(u, th, e)  # noqa: E731
    nf = limb.n
    us = [u0 + (u1 - u0) * k / 3 for k in range(4)]
    ths = [th0 + (th1 - th0) * k / 3 for k in range(4)]
    surface_panel(mb, fn, nf, us, ths, off, mat, cand)
    fu = [u0 - 0.012, u0 + 0.008, u0 + 0.03]
    fth = [th0 - 0.04 + (th1 - th0 + 0.08) * k / 3 for k in range(4)]
    surface_panel(mb, fn, nf, fu, fth, lambda u, th: off + 0.004, flapmat, cand, base_off=off)


def figure(role, lod="hero", morph=None):
    """The whole skinned body of one role as one MB (deterministic vertex order for shape keys)."""
    morph = morph or {}
    pal = ROLE_PAL[role]
    eva = role == "eva"
    body = Body(morph.get("fem", 0.0), morph.get("heavy", 0.0), morph.get("slim", 0.0),
                inflate=0.026 if eva else 0.0, limb_inflate=0.022 if eva else 0.0)
    mb = MB()
    hero = lod == "hero"
    suit, suit2 = pal["suit"], pal["suit2"]

    # ---- torso
    def tmat(z, th):
        deg = math.degrees(th) % 360
        if role == "colonist":
            return suit if z > 1.0 else suit2
        if role == "science" and 0.95 < z < 1.37 and (abs(adiff(deg, 0)) < 18 or abs(adiff(deg, 180)) < 18):
            return suit2
        if role == "eva" and 0.9 < z < 1.4 and (abs(adiff(deg, 0)) < 22 or abs(adiff(deg, 180)) < 22):
            return suit2
        if not hero:
            if role in ("engineer", "science") and z > 1.39:
                return "Owner"
            if role in ("engineer", "science", "security") and 1.025 < z < 1.07:
                return "trim"
            if role == "security" and 1.0 < z < 1.45:
                return suit2
        return suit
    zs = TORSO_Z if hero else [0.795, 0.84, 0.9, 0.97, 1.03, 1.07, 1.14, 1.22, 1.3, 1.39, 1.43, 1.47, 1.5, 1.535]
    torso_mesh(mb, body, tmat, TORSO_SEGS if hero else 10, zs)

    # ---- neck + head
    nl = neck_limb(body)
    limb_mesh(mb, nl, [0.0, 0.35, 0.7, 1.0], LIMB_SEGS if hero else 6, lambda u, th: "Skin",
              [("Neck", 0), ("Head", 0.012), ("UpperChest", 0.01)], dome=False, cap_end=False)
    if hero and not eva:
        head_mesh(mb, body, HEAD_LONS, HEAD_LATS)
        face_features(mb, body)
    elif eva and hero:  # the head only shows as a shadow behind the gold visor
        head_mesh(mb, body, 16, [-70, -50, -30, -10, 10, 30, 50, 70])
    else:
        cap = None if eva else (lambda lo: hairline(lo) + 4)
        head_mesh(mb, body, 10, [-70, -45, -20, 5, 30, 55, 75], hair_cap=cap)

    # ---- arms, hands
    for side, s in SIDES:
        arm = arm_limb(side, s, body)
        us = [0.0, 0.08, 0.2, 0.32, 0.42, 0.48, 0.52, 0.56, 0.62, 0.72, 0.84, 0.93, 1.0] if hero else [0.0, 0.25, 0.5, 0.75, 1.0]

        def amat(u, th, side=side):
            if role == "colonist" and u > 0.6:
                return "Skin"
            if eva and 0.5 < u < 0.58:
                return "eva_bellows"
            if not hero and role == "security" and u < 0.15:
                return "Owner"
            if not hero and eva and 0.2 < u < 0.28:
                return "Owner"
            return suit
        limb_mesh(mb, arm, us, LIMB_SEGS if hero else 6, amat, arm_cand(side), dome=True, cap_end=True)
        glove = pal["glove"]
        hand_mesh(mb, side, s, glove or "Skin", glove=bool(glove), lod=lod)

    # ---- legs, boots
    leg_mat = suit2 if role == "colonist" else suit
    for side, s in SIDES:
        leg = leg_limb(side, s, body)
        us = [0.0, 0.1, 0.22, 0.34, 0.43, 0.47, 0.5, 0.53, 0.58, 0.68, 0.8, 0.91, 1.0] if hero else [0.0, 0.3, 0.5, 0.75, 1.0]

        def lmat(u, th):
            if eva and 0.48 < u < 0.54:
                return "eva_bellows"
            if not hero and eva and 0.2 < u < 0.27:
                return "Owner"
            if not hero and role in ("engineer", "security") and 0.46 < u < 0.55 and math.sin(th) > 0.3:
                return "eng_pad"
            return leg_mat
        limb_mesh(mb, leg, us, LIMB_SEGS if hero else 6, lmat, leg_cand(side), dome=True, cap_end=True)
        top = {"engineer": 0.30, "science": 0.26, "security": 0.33, "eva": 0.36, "colonist": 0.21}[role]
        boot_mesh(mb, side, s, body, pal["boot"], top_z=top, big=0.012 if eva else 0.0, lod=lod,
                  lacing=role in ("engineer", "security", "science"), toe_cap="trim" if role in ("engineer", "security") else None)

    if hero:
        details(mb, role, body)
    else:
        far_details(mb, role, body)
    return mb


def details(mb, role, body):
    pal = ROLE_PAL[role]
    suit, suit2, accent = pal["suit"], pal["suit2"], pal["accent"]
    fn = lambda z, th, e: body.torso(z, th, e)  # noqa: E731
    nf = body.torso_n
    eva = role == "eva"
    D = rad
    jumpsuit = role in ("engineer", "science", "security")

    # collar (all but eva: neck ring instead)
    if role != "eva":
        th = [D(100 + 340 * k / 22) for k in range(23)]
        nl = neck_limb(body)
        r0 = [body.torso(1.505, t, 0.004) for t in th]
        r1 = [nl.pt(0.62, t, 0.012) for t in th]
        r2 = [nl.pt(0.62, t, 0.004) for t in th]
        cm = suit if role != "colonist" else suit
        sheet(mb, [r0, r1], cm, T_CAND, outward=lambda m: (m - V((0, -0.02, m.z))) + V((0, 0, 0.3)))
        sheet(mb, [r1, r2], cm, T_CAND, outward=lambda m: V((0, 0, 1)))
    if jumpsuit:
        # Owner shoulder yoke with its seam edge, princess/side seams, zip placket
        zs = [1.385 + (1.505 - 1.385) * k / 4 for k in range(5)]
        ths = [2 * pi * j / BAND_T for j in range(BAND_T)]
        surface_panel(mb, fn, nf, zs, ths, 0.0025, "Owner", T_CAND, closed=True)
        for thd in (68, 112, 250, 290):
            ribbon(mb, fn, nf, lambda t, thd=thd: (1.385 - 0.335 * t, D(thd + (6 if thd < 90 or thd > 270 else -6) * t * (1 if thd < 180 else -1))), 10, 0.004, 0.0012, "seam", T_CAND)
        for thd in (0, 180):
            ribbon(mb, fn, nf, lambda t, thd=thd: (1.36 - 0.5 * t, D(thd)), 12, 0.004, 0.0012, "seam", T_CAND)
        ribbon(mb, fn, nf, lambda t: (1.5 - 0.47 * t, D(90)), 14, 0.011, 0.002, "trim", T_CAND, depth=0.001)
        zp = body.torso(1.47, D(90), 0.004)
        obox(mb, zp, V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1)), (0.005, 0.003, 0.011), "metal", T_CAND)
        # belt with buckle and loops
        zs = [1.025, 1.04, 1.055, 1.07]
        surface_panel(mb, fn, nf, zs, ths, 0.006, "strap" if role != "security" else "trim", LOWER_CAND, closed=True)
        bp = body.torso(1.0475, D(90), 0.009)
        obox(mb, bp, V((1, 0, 0)), V((0, 0, 1)), V((0, 1, 0)), (0.028, 0.02, 0.004), "buckle", LOWER_CAND, chamfer=0.004)
        obox(mb, bp + V((0, 0.004, 0)), V((1, 0, 0)), V((0, 0, 1)), V((0, 1, 0)), (0.012, 0.009, 0.002), accent if accent != "gold" else "gold", LOWER_CAND)
        # chest pockets, badge, name tape
        pocket_torso(mb, body, 1.235, 1.33, D(58), D(82), suit, suit, T_CAND)
        pocket_torso(mb, body, 1.235, 1.33, D(98), D(122), suit, suit, T_CAND)
        badge(mb, body, 1.37, D(110), T_CAND)
        surface_panel(mb, fn, nf, [1.352, 1.375], [D(58 + 24 * k / 3) for k in range(4)], 0.003, suit2 if role != "science" else accent, T_CAND)
        # trouser side seams and thigh cargo pockets
        for side, s in SIDES:
            leg = leg_limb(side, s, body)
            ribbon(mb, leg.pt, leg.n, lambda t: (0.05 + 0.85 * t, 0.0), 14, 0.004, 0.0012, "seam", leg_cand(side))
            if role in ("engineer", "security", "science"):
                pocket_limb(mb, leg, 0.27, 0.42, D(-30), D(25), suit, suit, leg_cand(side))
            # cuffs where the sleeves meet the gloves/wrist
            arm = arm_limb(side, s, body)
            surface_panel(mb, arm.pt, arm.n, [0.9, 0.95, 0.99], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.003, suit2 if role == "science" else "trim", arm_cand(side), closed=True)
            # Owner band round the upper sleeve under the yoke
            surface_panel(mb, arm.pt, arm.n, [0.1, 0.14, 0.18], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.0025, "Owner", arm_cand(side), closed=True)

    if role == "engineer":
        engineer(mb, body)
    elif role == "science":
        science(mb, body)
    elif role == "security":
        security(mb, body)
    elif role == "eva":
        eva_suit(mb, body)
    elif role == "colonist":
        colonist(mb, body)


def strap_path(mb, body, path, width, cand, mat="strap", n=24):
    ribbon(mb, lambda z, th, e: body.torso(z, th, e), body.torso_n, path, n, width, 0.007, mat, cand, depth=0.004)


def pouch(mb, body, z, thd, size, mat, cand, flap=None, out=0.012):
    th = rad(thd)
    p = body.torso(z, th, out + size[1])
    n = body.torso_n(z, th)
    up = V((0, 0, 1))
    rt = orth(up.cross(n) * -1, n)
    up2 = n.cross(rt) * -1
    if up2.z < 0:
        up2 = -up2
    obox(mb, p, rt, n, up2, size, mat, cand, chamfer=min(size) * 0.45)
    if flap:
        obox(mb, p + up2 * (size[2] * 0.62) + n * 0.003, rt, n, up2, (size[0] * 1.04, size[1] * 1.08, size[2] * 0.42), flap, cand)
    return p, n, rt, up2


def engineer(mb, body):
    D = rad
    H = T_CAND
    # utility harness: two straps up the front, over the shoulders, down the back; chest strap; D-rings
    for thd in (76, 104):
        back = 360 - thd

        def path(t, thd=thd, back=back):
            if t < 0.42:
                return 1.07 + (1.47 - 1.07) * t / 0.42, D(thd)
            if t < 0.58:
                k = (t - 0.42) / 0.16
                ang = thd + (back - 360 * (thd > 90) - thd) * k if thd < 90 else thd + (back - thd) * k
                return 1.47 + 0.035 * math.sin(pi * k), D(ang)
            return 1.47 - (1.47 - 1.07) * (t - 0.58) / 0.42, D(back)
        strap_path(mb, body, path, 0.032, H, n=20)
    strap_path(mb, body, lambda t: (1.30, D(76 + 28 * t)), 0.026, H, n=6)
    for thd in (76, 104):
        p = body.torso(1.36, D(thd), 0.013)
        n = body.torso_n(1.36, D(thd))
        tube(mb, p - n * 0.002, p + n * 0.003, 0.009, 0.009, 8, "buckle", H)
    bp = body.torso(1.30, D(90), 0.013)
    obox(mb, bp, V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1)), (0.022, 0.004, 0.016), "buckle", H, chamfer=0.003)
    # tool belt pouches and tools
    for z, thd, size in ((1.01, 228, (0.03, 0.018, 0.038)), (1.01, 312, (0.03, 0.018, 0.038)), (0.995, 52, (0.022, 0.016, 0.03))):
        p, n, rt, up = pouch(mb, body, z, thd, size, "eng_pouch", LOWER_CAND, flap="eng_pouch", out=0.012)
    p, n, rt, up = pouch(mb, body, 0.99, 22, (0.035, 0.02, 0.045), "eng_pouch", LOWER_CAND, out=0.012)
    tube(mb, p + up * 0.03 + rt * 0.012, p + up * 0.085 + rt * 0.018, 0.007, 0.007, 6, "metal", LOWER_CAND)
    obox(mb, p + up * 0.09 + rt * 0.018, rt, n, up, (0.014, 0.006, 0.01), "metal", LOWER_CAND)
    p, n, rt, up = pouch(mb, body, 0.99, 158, (0.035, 0.02, 0.045), "eng_pouch", LOWER_CAND, out=0.012)
    tube(mb, p + up * 0.03, p + up * 0.075, 0.011, 0.011, 8, "eng_pad", LOWER_CAND)
    # knee pads, elbow patch, sleeve pocket, tool loop
    for side, s in SIDES:
        leg = leg_limb(side, s, body)
        surface_panel(mb, leg.pt, leg.n, [0.45, 0.48, 0.51, 0.54, 0.57], [D(40 + 100 * k / 6) for k in range(7)], 0.012, "eng_pad", leg_cand(side))
        surface_panel(mb, leg.pt, leg.n, [0.43, 0.445], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.006, "trim", leg_cand(side), closed=True)
        surface_panel(mb, leg.pt, leg.n, [0.585, 0.6], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.006, "trim", leg_cand(side), closed=True)
        arm = arm_limb(side, s, body)
        surface_panel(mb, arm.pt, arm.n, [0.46, 0.5, 0.54], [D(-50 - 80 * k / 4) for k in range(5)], 0.004, "eng_suit2", arm_cand(side))
    arm = arm_limb("Left", -1, body)
    pocket_limb(mb, arm, 0.24, 0.36, D(-20), D(40), "eng_suit2", "eng_suit2", arm_cand("Left"), off=0.004)


def science(mb, body):
    D = rad
    # cross strap from the right shoulder to the left hip, case on the left hip
    def path(t):
        if t < 0.2:
            k = t / 0.2
            return 1.47 + 0.03 * math.sin(pi * k * 0.5), D(300 + 90 * k - 360 * 0)
        k = (t - 0.2) / 0.8
        return 1.47 - 0.43 * k, D(30 + 120 * k)
    strap_path(mb, body, lambda t: (1.47 - 0.42 * t, D(32 + 118 * t)), 0.03, T_CAND, mat="trim", n=24)
    strap_path(mb, body, lambda t: (1.47 - 0.42 * t, D(-32 - 118 * t)), 0.03, T_CAND, mat="trim", n=24)
    # scanner holster on the right hip
    pouch(mb, body, 0.99, 20, (0.03, 0.022, 0.05), "trim", LOWER_CAND, flap="sci_accent", out=0.012)
    # padd on the thigh
    leg = leg_limb("Right", 1, body)
    surface_panel(mb, leg.pt, leg.n, [0.2, 0.24], [D(60 + 30 * k / 3) for k in range(4)], 0.007, "scanner", leg_cand("Right"))
    del path


def security(mb, body):
    D = rad
    fn = lambda z, th, e: body.torso(z, th, e)  # noqa: E731
    nf = body.torso_n
    # armour vest: front and back panels, side straps, raised plates
    for c, span in ((90, 64), (270, 64)):
        ths = [D(c - span + 2 * span * k / 8) for k in range(9)]
        zs = [1.0 + (1.46 - 1.0) * k / 6 for k in range(7)]

        def off(z, th, c=c):
            d = abs(adiff(math.degrees(th), c))
            return 0.02 + 0.005 * smooth(1.42, 1.3, z) - 0.006 * smooth(40, 64, d)
        surface_panel(mb, fn, nf, zs, ths, off, "sec_vest", T_CAND)
        for z0, z1 in ((1.08, 1.2), (1.24, 1.38)):
            for d0, d1 in ((c - 44, c - 6), (c + 6, c + 44)):
                surface_panel(mb, fn, nf, [z0 + (z1 - z0) * k / 2 for k in range(3)], [D(d0 + (d1 - d0) * k / 3) for k in range(4)],
                              lambda z, th, off=off: off(z, th) + 0.007, "sec_plate", T_CAND, base_off=0.02)
    for z in (1.1, 1.26):
        for c in (0, 180):
            ribbon(mb, fn, nf, lambda t, c=c: (z, D(c - 28 + 56 * t)), 4, 0.03, 0.012, "trim", T_CAND, depth=0.005)
    ribbon(mb, fn, nf, lambda t: (1.17, D(54 + 72 * t)), 10, 0.008, 0.026, "sec_accent", T_CAND)
    # vest pouches and radio
    for thd in (66, 114):
        pouch(mb, body, 1.12, thd, (0.03, 0.016, 0.034), "trim", T_CAND, flap="trim", out=0.03)
    p, n, rt, up = pouch(mb, body, 1.42, 116, (0.016, 0.012, 0.03), "trim", T_CAND, out=0.026)
    tube(mb, p + up * 0.03, p + up * 0.075, 0.003, 0.003, 5, "metal", T_CAND)
    # Owner shoulder pads (two-layer), elbow and knee pads
    for side, s in SIDES:
        arm = arm_limb(side, s, body)
        for k, (u0, u1, off) in enumerate(((-0.04, 0.2, 0.016), (0.08, 0.26, 0.012))):
            surface_panel(mb, arm.pt, arm.n, [u0 + (u1 - u0) * i / 3 for i in range(4)], [D(-80 + 170 * i / 6) for i in range(7)],
                          lambda u, th, off=off: off + 0.006 * math.sin(math.pi * clamp((u - u0) / (u1 - u0), 0, 1)),
                          "Owner" if k == 0 else "sec_plate", arm_cand(side))
        surface_panel(mb, arm.pt, arm.n, [0.46, 0.52, 0.58], [D(-40 - 100 * i / 4) for i in range(5)], 0.01, "sec_plate", arm_cand(side))
        leg = leg_limb(side, s, body)
        surface_panel(mb, leg.pt, leg.n, [0.44, 0.5, 0.57], [D(35 + 110 * i / 5) for i in range(6)], 0.014, "sec_plate", leg_cand(side))
        surface_panel(mb, leg.pt, leg.n, [0.6, 0.62], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.006, "trim", leg_cand(side), closed=True)
    # holster with a compact sidearm on the right thigh
    leg = leg_limb("Right", 1, body)
    c = leg.pt(0.16, D(10), 0.03)
    t, es, ef = leg.frame(0.16)
    obox(mb, c, es, ef, -t, (0.012, 0.03, 0.055), "trim", leg_cand("Right"), chamfer=0.006)
    obox(mb, c - t * -0.07 + es * 0.004, es, ef, -t, (0.01, 0.022, 0.022), "metal", leg_cand("Right"), chamfer=0.005)
    obox(mb, c - t * -0.085 + ef * 0.03 + es * 0.004, es, ef, -t, (0.009, 0.035, 0.012), "trim", leg_cand("Right"), chamfer=0.004)
    ribbon(mb, leg.pt, leg.n, lambda t: (0.25, D(-60 + 120 * t)), 8, 0.012, 0.004, "trim", leg_cand("Right"))


def eva_suit(mb, body):
    D = rad
    fn = lambda z, th, e: body.torso(z, th, e)  # noqa: E731
    nf = body.torso_n
    # neck ring and helmet with the visor
    ring_c = V((0, -0.02, 1.53))
    rows = []
    for z, r in ((1.505, 0.118), (1.53, 0.124), (1.555, 0.124), (1.565, 0.112)):
        rows.append([ring_c + V((math.cos(2 * pi * j / 28) * r, math.sin(2 * pi * j / 28) * r * 1.02, z - 1.53)) for j in range(28)])
    sheet(mb, rows, "metal", T_CAND, closed=True, outward=lambda m: m - V((0, -0.02, m.z)))
    hc = HEAD_C + V((0, 0.012, 0.0))
    hrad = (0.142, 0.158, 0.152)

    def visor(la, lo):
        return -32 < la < 32 and abs(adiff(lo, 90)) < 58

    def hdeform(la, lo, p):
        if p.z < 1.555:
            p = V((p.x, p.y, 1.555 + (p.z - 1.555) * 0.2))
        return p
    ellipsoid(mb, hc, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), hrad, 32, 16, "eva_helmet", rigid("Head"), deform=hdeform,
              matf=lambda la, lo: "Visor" if visor(la, lo) else "eva_helmet")
    # visor frame and a sun-shield ridge
    for la in (-33, 33):
        pts = [hc + V((math.cos(rad(la)) * math.cos(rad(lo)) * hrad[0], math.cos(rad(la)) * math.sin(rad(lo)) * hrad[1], math.sin(rad(la)) * hrad[2])) * 1.0 for lo in range(30, 152, 8)]
        pts = [hc + (p - hc) * 1.035 for p in pts]
        a_row = [p + V((0, 0, 0.006)) for p in pts]
        b_row = [p - V((0, 0, 0.006)) for p in pts]
        sheet(mb, [a_row, b_row], "eva_suit2", rigid("Head"), outward=lambda m: m - hc)
    # lamp mount on the helmet's right side (the lamp itself is the HelmetLamp part)
    lm = hc + V((0.135, 0.03, 0.05))
    obox(mb, lm, V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1)), (0.012, 0.03, 0.016), "eva_suit2", rigid("Head"), chamfer=0.005)
    # life-support backpack: main box, top unit, tanks, vents, hoses to the chest unit
    up = V((0, 0, 1))
    pk = rigid("UpperChest")
    bc = V((0, -0.224, 1.24))
    obox(mb, bc, V((1, 0, 0)), V((0, 1, 0)), up, (0.16, 0.075, 0.22), "eva_pack", pk, chamfer=0.03)
    obox(mb, bc + V((0, -0.01, 0.255)), V((1, 0, 0)), V((0, 1, 0)), up, (0.14, 0.065, 0.04), "eva_suit2", pk, chamfer=0.02)
    for sx in (-1, 1):
        tube(mb, bc + V((sx * 0.17, -0.02, -0.17)), bc + V((sx * 0.17, -0.02, 0.17)), 0.04, 0.04, 12, "eva_helmet", pk)
        tube(mb, bc + V((sx * 0.17, -0.02, 0.17)), bc + V((sx * 0.17, -0.02, 0.2)), 0.022, 0.018, 10, "metal", pk)
        for k in range(4):
            obox(mb, bc + V((sx * 0.08, -0.077, -0.1 + k * 0.035)), V((1, 0, 0)), V((0, 1, 0)), up, (0.05, 0.004, 0.008), "seam", pk)
    obox(mb, bc + V((0, -0.077, 0.08)), V((1, 0, 0)), V((0, 1, 0)), up, (0.06, 0.004, 0.05), "Owner", pk)
    # chest control unit with status lights
    cu = body.torso(1.29, D(90), 0.03)
    obox(mb, cu, V((1, 0, 0)), V((0, 1, 0)), up, (0.08, 0.03, 0.05), "eva_suit2", T_CAND, chamfer=0.012)
    for k, m in enumerate(("Glow", "Lamp", "Glow")):
        obox(mb, cu + V((-0.04 + 0.04 * k, 0.031, 0.02)), V((1, 0, 0)), V((0, 1, 0)), up, (0.008, 0.003, 0.005), m, T_CAND)
    for k in range(3):
        obox(mb, cu + V((-0.03 + 0.03 * k, 0.031, -0.018)), V((1, 0, 0)), V((0, 1, 0)), up, (0.009, 0.004, 0.009), "metal", T_CAND)
    for sx in (-1, 1):
        a = bc + V((sx * 0.13, 0.05, 0.05))
        b = cu + V((sx * 0.075, -0.01, -0.01))
        pts = [a.lerp(b, k / 8) + V((sx * 0.08 * math.sin(pi * k / 8), 0, -0.06 * math.sin(pi * k / 8))) for k in range(9)]
        for k in range(8):
            tube(mb, pts[k], pts[k + 1], 0.012, 0.012, 6, "eva_bellows", T_CAND, caps=(k == 0, k == 7))
    # Owner bands on the arms and thighs, a chest stripe, bellows ribs at elbows and knees
    for side, s in SIDES:
        arm = arm_limb(side, s, body)
        surface_panel(mb, arm.pt, arm.n, [0.2, 0.24, 0.28], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.004, "Owner", arm_cand(side), closed=True)
        for u in (0.5, 0.54, 0.58):
            surface_panel(mb, arm.pt, arm.n, [u - 0.008, u + 0.008], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.008, "eva_bellows", arm_cand(side), closed=True, edge=False)
        leg = leg_limb(side, s, body)
        surface_panel(mb, leg.pt, leg.n, [0.2, 0.235, 0.27], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.004, "Owner", leg_cand(side), closed=True)
        for u in (0.47, 0.51, 0.55):
            surface_panel(mb, leg.pt, leg.n, [u - 0.008, u + 0.008], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.009, "eva_bellows", leg_cand(side), closed=True, edge=False)
        surface_panel(mb, leg.pt, leg.n, [0.42, 0.45, 0.48], [D(30 + 120 * k / 5) for k in range(6)], 0.012, "eva_suit2", leg_cand(side))
    surface_panel(mb, fn, nf, [1.36, 1.39], [D(55 + 70 * k / 6) for k in range(7)], 0.003, "Owner", T_CAND)
    # suit waist ring
    surface_panel(mb, fn, nf, [1.03, 1.05, 1.07], [2 * pi * j / BAND_T for j in range(BAND_T)], 0.008, "eva_suit2", LOWER_CAND, closed=True)
    badge(mb, body, 1.37, D(118), T_CAND)


def colonist(mb, body):
    D = rad
    fn = lambda z, th, e: body.torso(z, th, e)  # noqa: E731
    nf = body.torso_n
    # tunic hem flaring over the hips
    ths = [2 * pi * j / BAND_T for j in range(BAND_T)]
    rows = []
    for z, extra in ((1.04, 0.004), (0.98, 0.012), (0.92, 0.022), (0.86, 0.034), (0.845, 0.036)):
        rows.append([body.torso(z, th, extra) for th in ths])
    hem_c = [(("Hips", 0), ("Spine", 0.0)), ]
    cand = [("Hips", 0), ("Spine", 0.0), ("RightUpperLeg", 0.02), ("LeftUpperLeg", 0.02)]
    idx = sheet(mb, rows, "col_tunic", cand, closed=True, centres=[V((0, 0, r[0].z)) for r in rows])
    inner = [mb.v(body.torso(0.85, th, 0.012), cand) for th in ths]
    rim(mb, idx[-1] + [idx[-1][0]], inner + [inner[0]], "col_tunic", cand, V((0, 0, 0.95)))
    del hem_c
    # wrap collar: the overlapping front panel with a diagonal edge
    zs = [1.2 + 0.3 * k / 8 for k in range(9)]
    tth = [D(62 + 70 * k / 10) for k in range(11)]

    def keep(i, j):
        z = (zs[i] + zs[i + 1]) / 2
        thd = 62 + 70 * (j + 0.5) / 10
        return thd > 62 + (1.5 - z) * 150 * 0 + (1.5 - z) * 160
    surface_panel(mb, fn, nf, zs, tth, 0.004, "col_tunic", T_CAND, keep=keep, edge=False)
    ribbon(mb, fn, nf, lambda t: (1.5 - 0.29 * t, D(62 + 46 * t)), 12, 0.01, 0.006, "col_sash", T_CAND, depth=0.003)
    # sash with a knot at the left hip
    surface_panel(mb, fn, nf, [1.03, 1.05, 1.075], ths, 0.007, "col_sash", LOWER_CAND, closed=True)
    kp = body.torso(1.05, D(140), 0.02)
    kn = body.torso_n(1.05, D(140))
    ellipsoid(mb, kp, (orth(V((0, 0, 1)).cross(kn), kn), kn, V((0, 0, 1))), (0.02, 0.012, 0.018), 8, 4, "col_sash", LOWER_CAND)
    for k, dx in enumerate((-0.01, 0.012)):
        a = kp + V((0, 0, -0.012))
        tube(mb, a + V((dx, 0, 0)), a + V((dx * 2, 0.005, -0.14 + k * 0.03)), 0.012, 0.009, 6, "col_sash", LOWER_CAND)
    # rolled sleeve cuffs at the elbows
    for side, s in SIDES:
        arm = arm_limb(side, s, body)
        surface_panel(mb, arm.pt, arm.n, [0.56, 0.59, 0.62], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.01, "col_tunic", arm_cand(side), closed=True)
        leg = leg_limb(side, s, body)
        ribbon(mb, leg.pt, leg.n, lambda t: (0.32 + 0.6 * t, 0.0), 10, 0.004, 0.0012, "seam", leg_cand(side))
        surface_panel(mb, leg.pt, leg.n, [0.84, 0.87, 0.9], [2 * pi * j / BAND_L for j in range(BAND_L)], 0.008, "col_trousers", leg_cand(side), closed=True)
    # Owner scarf round the neck with a hanging tail
    nl = neck_limb(body)
    rows = []
    for u, e in ((0.0, 0.03), (0.15, 0.036), (0.32, 0.026)):
        rows.append([nl.pt(u, 2 * pi * j / 18, e) for j in range(18)])
    sheet(mb, rows, "Owner", [("Neck", 0), ("UpperChest", 0)], closed=True, centres=[nl.centre(u) for u in (0.0, 0.15, 0.32)])
    strap_path(mb, body, lambda t: (1.48 - 0.2 * t, D(102 + 6 * t)), 0.05, T_CAND, mat="Owner", n=8)
    # satchel on the right hip, strap over the left shoulder
    strap_path(mb, body, lambda t: (1.47 - 0.45 * t, D(-148 + 158 * t)), 0.025, T_CAND, mat="col_sash", n=24)
    strap_path(mb, body, lambda t: (1.47 - 0.45 * t, D(148 - 138 * t)), 0.025, T_CAND, mat="col_sash", n=24)
    p, n, rt, up = pouch(mb, body, 0.95, 10, (0.06, 0.03, 0.065), "col_bag", LOWER_CAND, flap="col_sash", out=0.03)


def far_details(mb, role, body):
    D = rad
    if role == "eva":
        hc = HEAD_C + V((0, 0.012, 0.0))
        ellipsoid(mb, hc, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.142, 0.158, 0.152), 10, 6, "eva_helmet", rigid("Head"),
                  matf=lambda la, lo: "Visor" if (-35 < la < 35 and abs(adiff(lo, 90)) < 60) else "eva_helmet")
        obox(mb, V((0, -0.224, 1.24)), V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1)), (0.17, 0.08, 0.23), "eva_pack", rigid("UpperChest"))
    elif role == "security":
        for side, s in SIDES:
            arm = arm_limb(side, s, body)
            c = arm.pt(0.05, D(0), 0.02)
            t, es, ef = arm.frame(0.05)
            obox(mb, c, es, ef, t, (0.03, 0.06, 0.06), "Owner", arm_cand(side))
        obox(mb, body.torso(1.2, D(90), 0.03), V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1)), (0.13, 0.012, 0.17), "sec_vest", T_CAND)
    elif role == "colonist":
        ths = [2 * pi * j / 10 for j in range(10)]
        rows = [[body.torso(z, th, e) for th in ths] for z, e in ((1.04, 0.004), (0.92, 0.022), (0.85, 0.034))]
        sheet(mb, rows, "col_tunic", [("Hips", 0), ("RightUpperLeg", 0.02), ("LeftUpperLeg", 0.02)], closed=True, centres=[V((0, 0, r[0].z)) for r in rows])
        nl = neck_limb(body)
        rows = [[nl.pt(u, 2 * pi * j / 8, e) for j in range(8)] for u, e in ((0.0, 0.03), (0.3, 0.026))]
        sheet(mb, rows, "Owner", [("Neck", 0), ("UpperChest", 0)], closed=True, centres=[nl.centre(u) for u in (0.0, 0.3)])
    elif role == "science":
        obox(mb, V((-0.2, 0.02, 0.98)), V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1)), (0.03, 0.08, 0.06), "sci_case", rigid("Hips"))
    elif role == "engineer":
        for thd in (76, 104):
            p = body.torso(1.25, D(thd), 0.006)
            obox(mb, p, V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1)), (0.016, 0.004, 0.2), "strap", T_CAND)


# ---------------------------------------------------------------- props (separate named parts)


def hand_frame(side):
    J = JP[side]
    return J["wr"], J["hf"], J["ath"], J["npalm"]


def prop_tool():
    """Plasma welder: pistol grip across the right fist, barrel along the fingers, emitter glow at the nozzle."""
    mb = MB()
    wr, f, a, n = hand_frame("Right")
    c = rigid("RightHand")
    g = wr + f * 0.05 + n * 0.024
    tube(mb, g - a * 0.05, g + a * 0.045, 0.015, 0.016, 10, "trim", c)
    body0 = g + a * 0.06 - f * 0.03
    obox(mb, body0 + f * 0.06, a, f, n, (0.022, 0.1, 0.026), "tool", c, chamfer=0.008)
    obox(mb, body0 + f * 0.02 + a * 0.028, a, f, n, (0.01, 0.05, 0.02), "trim", c, chamfer=0.004)
    tube(mb, body0 + f * 0.16, body0 + f * 0.22, 0.016, 0.011, 10, "metal", c)
    tube(mb, body0 + f * 0.22, body0 + f * 0.235, 0.008, 0.006, 8, "Glow", c)
    tube(mb, body0 - f * 0.04, body0 - f * 0.065, 0.02, 0.02, 10, "metal", c)
    for k in range(3):
        obox(mb, body0 + f * (0.03 + 0.03 * k) - a * 0.023, a, f, n, (0.002, 0.008, 0.02), "seam", c)
    return mb, body0 + f * 0.24


def prop_scanner():
    """Handheld field scanner (tricorder-like, original): a slab on the palm with a raised lid, screen, sensor head."""
    mb = MB()
    wr, f, a, n = hand_frame("Right")
    c = rigid("RightHand")
    base = wr + f * 0.075 + n * 0.032
    obox(mb, base, a, f, n, (0.036, 0.07, 0.012), "scanner", c, chamfer=0.006)
    obox(mb, base + n * 0.0125 + f * 0.012, a, f, n, (0.029, 0.045, 0.0015), "Screen", c)
    for k in range(3):
        obox(mb, base + n * 0.0125 - f * 0.05 + a * (-0.018 + 0.018 * k), a, f, n, (0.006, 0.006, 0.002), "Glow" if k == 1 else "metal", c)
    hinge = base + f * 0.07 + n * 0.012
    lid_dir = (f * math.cos(rad(70)) + n * math.sin(rad(70))).normalized()
    lid_n = lid_dir.cross(a) * -1
    if lid_n.dot(n) < 0:
        lid_n = -lid_n
    obox(mb, hinge + lid_dir * 0.032, a, lid_dir, lid_n, (0.034, 0.032, 0.005), "scanner_lid", c, chamfer=0.003)
    obox(mb, hinge + lid_dir * 0.032 + lid_n * -0.0055, a, lid_dir, lid_n, (0.026, 0.024, 0.001), "Screen", c)
    tube(mb, base + f * 0.07 - n * 0.002, base + f * 0.09 - n * 0.002, 0.012, 0.009, 10, "metal", c)
    tip = base + f * 0.092
    return mb, tip


CRATE_C = V((0, 0.29, 1.10))
CRATE_H = (0.24, 0.17, 0.15)


def prop_crate():
    mb = MB()
    c = rigid("Chest")
    X, Y, Z = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))
    hx, hy, hz = CRATE_H
    obox(mb, CRATE_C, X, Y, Z, (hx - 0.01, hy - 0.01, hz - 0.01), "crate", c, chamfer=0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            obox(mb, CRATE_C + V((sx * (hx - 0.012), sy * (hy - 0.012), 0)), X, Y, Z, (0.014, 0.014, hz), "crate_dark", c)
        obox(mb, CRATE_C + V((sx * hx, 0, 0.035)), Y, Z, X, (0.06, 0.012, 0.006), "crate_dark", c, chamfer=0.004)
    for sz in (-1, 1):
        obox(mb, CRATE_C + V((0, 0, sz * (hz - 0.006))), X, Y, Z, (hx + 0.002, hy + 0.002, 0.008), "crate_dark", c)
    obox(mb, CRATE_C + V((0, hy + 0.001, 0)), X, Z, Y, (hx * 0.7, 0.022, 0.002), "Owner", c)
    obox(mb, CRATE_C + V((-0.13, hy + 0.002, 0.07)), X, Z, Y, (0.03, 0.02, 0.002), "Glow", c)
    return mb


def prop_case():
    mb = MB()
    c = rigid("Hips")
    p = V((-0.205, 0.03, 0.97))
    X, Y, Z = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))
    obox(mb, p, X, Y, Z, (0.032, 0.09, 0.07), "sci_case", c, chamfer=0.012)
    obox(mb, p + V((-0.033, 0, 0.0)), X, Y, Z, (0.002, 0.07, 0.008), "sci_accent", c)
    for sy in (-1, 1):
        obox(mb, p + V((-0.033, sy * 0.05, 0.05)), X, Y, Z, (0.004, 0.01, 0.006), "metal", c)
    tube(mb, p + V((0, -0.04, 0.07)), p + V((0, -0.04, 0.09)), 0.005, 0.005, 6, "trim", c)
    tube(mb, p + V((0, 0.04, 0.07)), p + V((0, 0.04, 0.09)), 0.005, 0.005, 6, "trim", c)
    tube(mb, p + V((0, -0.045, 0.09)), p + V((0, 0.045, 0.09)), 0.006, 0.006, 6, "trim", c)
    return mb


def prop_lamp():
    mb = MB()
    c = rigid("Head")
    lm = HEAD_C + V((0, 0.012, 0.0)) + V((0.152, 0.04, 0.05))
    tube(mb, lm - V((0, 0.04, 0)), lm + V((0, 0.03, 0)), 0.02, 0.022, 12, "eva_suit2", c)
    tube(mb, lm + V((0, 0.03, 0)), lm + V((0, 0.034, 0)), 0.018, 0.018, 12, "Lamp", c)
    return mb, lm + V((0, 0.036, 0))


# ---------------------------------------------------------------- Blender objects


def make_armature(name="Rig"):
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for name_, head, tail, parent, rollz, connect, deform in BONES:
        b = arm.edit_bones.new(name_)
        b.head, b.tail = head, tail
        b.align_roll(rollz)
        if parent:
            b.parent = eb[parent]
            b.use_connect = connect
        b.use_deform = deform
        eb[name_] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in ob.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return ob


def make_mesh(name, mb, rig, morphs=None):
    """A skinned mesh object from an MB: atlas UVs per face, vertex groups from the weights, shape keys."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(c) for c in mb.co], [], list(mb.faces))
    slots = []
    for key in mb.mats:
        mname = key if key in SPECIAL else "Crew"
        if mname not in slots:
            slots.append(mname)
    for s in slots:
        me.materials.append(MT[s])
    mi = [slots.index(k if k in SPECIAL else "Crew") for k in mb.mats]
    me.polygons.foreach_set("material_index", mi)
    uv = me.uv_layers.new(name="UVMap")
    uvs = []
    for poly, key in zip(me.polygons, mb.mats):
        u, v = swatch_uv(key) if key not in SPECIAL else (0.0, 0.0)
        uvs += [u, v] * poly.loop_total
    uv.data.foreach_set("uv", uvs)
    if me.validate(verbose=False):
        print("WARNING: fixed invalid geometry in", name, flush=True)
    me.update()
    me.shade_smooth()
    try:
        me.set_sharp_from_angle(angle=rad(50))
    except Exception:  # noqa: BLE001
        pass
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = rig
    weights = solve_weights(mb)
    groups = {}
    for i, wd in enumerate(weights):
        for b, w in wd.items():
            if b not in groups:
                groups[b] = ob.vertex_groups.new(name=b)
            groups[b].add([i], w, "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    if morphs:
        ob.shape_key_add(name="Basis")
        for kname, mb2 in morphs:
            if len(mb2.co) != len(mb.co):
                sys.exit(f"{name}: shape key {kname} has {len(mb2.co)} verts, base {len(mb.co)}")
            k = ob.shape_key_add(name=kname, from_mix=False)
            flat = []
            for c in mb2.co:
                flat += [c.x, c.y, c.z]
            k.data.foreach_set("co", flat)
    return ob


def bone_empty(name, rig, bone, world_pos, fwd):
    """An empty parented to a bone at world_pos, local -Z... kept simple: identity rotation, +Y = fwd in rest."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.03
    bpy.context.scene.collection.objects.link(e)
    e.parent = rig
    e.parent_type = "BONE"
    e.parent_bone = bone
    q = V((0, 1, 0)).rotation_difference(fwd.normalized())
    e.matrix_world = Matrix.Translation(world_pos) @ q.to_matrix().to_4x4()
    return e


def build_role(role):
    """Every object of one role: Rig, body + far LOD (with shape keys), hair styles, props, effect empties."""
    rig = make_armature("Rig")
    bpy.context.view_layer.update()
    objs = [rig]
    for lod, name in (("hero", role), ("far", role + "_far")):
        base = figure(role, lod)
        morphs = [(k, figure(role, lod, {m: 1.0})) for k, m in (("Feminine", "fem"), ("Heavy", "heavy"), ("Slim", "slim"))]
        ob = make_mesh(name, base, rig, morphs)
        ob["tris"] = base.tris()
        objs.append(ob)
    body = Body()
    styles = ["HairCrop"] if role == "eva" else HAIR_STYLES
    for st in styles:
        mb = MB()
        hair_mesh(mb, body, st)
        objs.append(make_mesh(st, mb, rig))
    if role != "eva":
        mb = MB()
        beard_mesh(mb, body)
        objs.append(make_mesh("Beard", mb, rig))
    mb, tip = prop_tool()
    objs.append(make_mesh("Tool", mb, rig))
    objs.append(bone_empty("ToolTip", rig, "RightHand", tip, JP["Right"]["hf"]))
    mb, tip = prop_scanner()
    objs.append(make_mesh("Scanner", mb, rig))
    objs.append(bone_empty("ScannerTip", rig, "RightHand", tip, JP["Right"]["hf"]))
    objs.append(make_mesh("Crate", prop_crate(), rig))
    if role == "science":
        objs.append(make_mesh("SampleCase", prop_case(), rig))
    if role == "eva":
        mb, tip = prop_lamp()
        objs.append(make_mesh("HelmetLamp", mb, rig))
        objs.append(bone_empty("LampPoint", rig, "Head", tip, V((0, 1, 0))))
    return rig, objs


# ---------------------------------------------------------------- pose solver


class Solver:
    def __init__(self, rig):
        self.rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in rig.data.bones}
        self.length = {b.name: b.length for b in rig.data.bones}
        self.order = [b[0] for b in BONES]

    def solve(self, spec):
        """spec: off (hips offset), rot {bone: world-axis delta in the parent's frame}, leg {side: (ankle, foot delta,
        pole)}, arm {side: fn(P) -> (wrist, pole, fingers dir, palm normal)} -> {bone: matrix_basis}."""
        P, M, want = {}, {}, {}
        rot = spec.get("rot", {})
        for name in self.order:
            R = self.rest[name]
            par = self.parent[name]
            base = (P[par] @ self.rest[par].inverted() @ R) if par else R.copy()
            br = base.to_3x3()
            for side, _ in SIDES:
                if name == side + "UpperLeg" and side in spec.get("leg", {}):
                    ank, fdelta, pole = spec["leg"][side]
                    self._ik(name, side + "LowerLeg", base.translation, ank, pole, want)
                    want[side + "Foot"] = fdelta @ self.rest[side + "Foot"].to_3x3()
                if name == side + "UpperArm" and side in spec.get("arm", {}):
                    wr, pole, fy, fz = spec["arm"][side](P, self)
                    self._ik(name, side + "LowerArm", base.translation, wr, pole, want)
                    y = fy.normalized()
                    z = orth(fz, y)
                    x = y.cross(z)
                    want[side + "Hand"] = Matrix((x, y, z)).transposed()
            if name in want:
                mr = br.inverted() @ want[name]
                m = mr.to_4x4()
            else:
                d = rot.get(name)
                m = (R.to_3x3().inverted() @ d @ R.to_3x3()).to_4x4() if d is not None else Matrix.Identity(4)
                if name == "Hips" and "off" in spec:
                    m.translation = br.inverted() @ spec["off"]
            P[name] = base @ m
            M[name] = m
        return M, P

    def _ik(self, up, lo, a, target, pole, want):
        l1, l2 = self.length[up], self.length[lo]
        d = target - a
        dist = clamp(d.length, abs(l1 - l2) + 1e-4, l1 + l2 - 1e-4)
        dr = d.normalized()
        pp = orth(V(pole), dr)
        ca = (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)
        sa = math.sqrt(max(0.0, 1 - ca * ca))
        knee = a + dr * (l1 * ca) + pp * (l1 * sa)
        end = a + dr * dist
        n = dr.cross(pp).normalized()
        for b, p0, p1 in ((up, a, knee), (lo, knee, end)):
            y = (p1 - p0).normalized()
            x = orth(n, y)
            z = x.cross(y)
            want[b] = Matrix((x, y, z)).transposed()

    def tail(self, P, bone):
        return P[bone] @ V((0, self.length[bone], 0))


def Rx(d):
    return Matrix.Rotation(rad(d), 3, "X")


def Ry(d):
    return Matrix.Rotation(rad(d), 3, "Y")


def Rz(d):
    return Matrix.Rotation(rad(d), 3, "Z")


def chest_q(P, S):
    return P["UpperChest"].to_3x3() @ S.rest["UpperChest"].to_3x3().inverted()


def curl(side, idx=0.0, mid=0.0, thumb=0.0, idx2=None, mid2=None):
    J = JP[side]
    np_ = J["npalm"]
    out = {}
    (b0, d0, _), (b1, d1, _) = J["fingers"][0], J["fingers"][1]
    for bone, d, ang in ((side + "IndexProximal", d0, idx), (side + "IndexIntermediate", d0, idx2 if idx2 is not None else idx * 1.25),
                         (side + "MiddleProximal", d1, mid), (side + "MiddleIntermediate", d1, mid2 if mid2 is not None else mid * 1.25)):
        out[bone] = Matrix.Rotation(rad(ang), 3, d.cross(np_).normalized())
    tb, td, td2 = J["thumb"]
    toward = (np_ * 0.55 - J["ath"] * 0.85).normalized()
    out[side + "ThumbProximal"] = Matrix.Rotation(rad(thumb), 3, td.cross(toward).normalized())
    out[side + "ThumbDistal"] = Matrix.Rotation(rad(thumb * 0.8), 3, td2.cross(toward).normalized())
    return out


def hang_arm(side, s, swing=0.0, out=0.075, fwd=0.035, drop=0.505, bend=0.0):
    """A relaxed arm: the wrist below the shoulder (in the chest's frame) swung swing degrees forward."""
    def fn(P, S):
        q = chest_q(P, S)
        sh = S.tail(P, side + "Shoulder")
        v = Rx(swing) @ V((s * out, fwd, -drop + bend))
        wr = sh + q @ v
        fy = q @ (Rx(swing * 1.1) @ V((s * 0.06, 0.12, -1)))
        fz = q @ V((-s, 0.12, 0))
        return wr, q @ V((s * 0.35, -1, 0)), fy, fz
    return fn


# walk/run parameters: frames per loop, stance share, contact travel in stance, heel-strike heel y,
# strike/toe-off pitch, heel/ball share of stance, swing lift, hips height, bob, kick
WALK = dict(T=32, st=0.62, D=0.72, ys=0.24, b0=18, b1=42, q1=0.15, q2=0.6, lift=0.1, h=0.905, bob=0.016, kick=0.0)
CARRY = dict(T=36, st=0.64, D=0.6, ys=0.2, b0=14, b1=34, q1=0.15, q2=0.62, lift=0.08, h=0.9, bob=0.012, kick=0.0)
RUN = dict(T=20, st=0.36, D=0.7, ys=0.19, b0=6, b1=40, q1=0.06, q2=0.45, lift=0.24, h=0.875, bob=0.03, kick=0.2)


def speed(p):
    return p["D"] / (p["st"] * p["T"] / FPS)


HEEL = (-0.06, -0.085)
BALL = (0.135, -0.085)


def _rot2(rel, beta):
    c, s = math.cos(rad(beta)), math.sin(rad(beta))
    return rel[0] * c - rel[1] * s, rel[0] * s + rel[1] * c


def _ankle(contact_y, rel, beta):
    dy, dz = _rot2(rel, beta)
    return contact_y - dy, -dz


def foot_track(p, prm):
    """Ankle (y, z) relative to the hips' rest y and the foot pitch (deg, + = toes up) at phase p (0 = heel strike)."""
    st, D, ys = prm["st"], prm["D"], prm["ys"]

    def stance(q):
        heel = ys - D * q
        if q < prm["q1"]:
            b = prm["b0"] * (1 - smooth(0, 1, q / prm["q1"]))
            y, z = _ankle(heel, HEEL, b)
            return y, z, b
        if q < prm["q2"]:
            return heel - HEEL[0], -HEEL[1], 0.0
        r = (q - prm["q2"]) / (1 - prm["q2"])
        b = -prm["b1"] * r ** 1.4
        y, z = _ankle(heel + (BALL[0] - HEEL[0]), BALL, b)
        return y, z, b
    if p < st:
        return stance(p / st)
    r = (p - st) / (1 - st)
    y0, z0, b0 = stance(1.0)
    y1, z1, b1 = stance(0.0)
    k = smooth(0, 1, r)
    y = lerp(y0, y1, k) - prm["kick"] * math.sin(pi * r) * (1 - r) ** 1.5
    z = lerp(z0, z1, k) + prm["lift"] * math.sin(pi * r ** 0.85) ** 1.2
    b = lerp(b0, b1, smooth(0.1, 1.0, r)) - 12 * math.sin(pi * r) * (1 - r)
    return y, z, b


def gait(t, prm, arms=True, lean=0.0):
    rot, legs = {}, {}
    for side, s in SIDES:
        ph = (t + (0.0 if s > 0 else 0.5)) % 1.0
        y, z, b = foot_track(ph, prm)
        legs[side] = (V((s * 0.1, 0.005 + y, z)), Rz(-s * 4) @ Rx(b), V((s * 0.12, 1, 0)))
        rot[side + "Toes"] = Rx(max(0.0, -b) * 0.9 if ph < prm["st"] else 6.0)
    run = prm is RUN
    if run:
        zh = prm["h"] - prm["bob"] * math.cos(4 * pi * (t - prm["st"] / 2))
    else:
        zh = prm["h"] - prm["bob"] * math.cos(4 * pi * t)
    off = V((0.016 * math.sin(2 * pi * t) * (0.5 if run else 1.0), 0.0, zh - 0.925))
    yaw = (7 if run else 5) * math.cos(2 * pi * t)
    rot["Hips"] = Rz(yaw) @ Ry(-2 * math.sin(2 * pi * t)) @ Rx(-(6 if run else 1.5) + lean)
    rot["Spine"] = Rz(-0.4 * yaw) @ Rx(-(3 if run else 1.0))
    rot["Chest"] = Rz(-0.5 * yaw) @ Rx(0.5 * math.sin(4 * pi * t) + (1.0 if run else 0.0))
    rot["UpperChest"] = Rz(-0.6 * yaw)
    rot["Neck"] = Rz(0.5 * yaw) @ Rx(3 if run else 1)
    rot["Head"] = Rx((4 if run else 1.5) - 1.0 * math.sin(4 * pi * t))
    arm = {}
    if arms:
        for side, s in SIDES:
            sw = (-1 if s > 0 else 1) * math.cos(2 * pi * t)
            if run:
                def fn(P, S, side=side, s=s, sw=sw):
                    q = chest_q(P, S)
                    sh = S.tail(P, side + "Shoulder")
                    v = Rx(32 * sw) @ V((s * 0.05, 0.11, -0.3))
                    return sh + q @ v, q @ V((s * 0.3, -1, -0.3)), q @ (Rx(32 * sw) @ V((-s * 0.3, 1, 0.5))), q @ V((-s, 0, 0.2))
                arm[side] = fn
                rot.update(curl(side, 65, 75, 35))
            else:
                arm[side] = hang_arm(side, s, swing=17 * sw, bend=0.012 + 0.02 * max(0.0, sw))
                rot.update(curl(side, 14, 20, 12))
    return dict(off=off, rot=rot, leg=legs, arm=arm)


def stand_legs(rot, r=(0.105, -0.012), l=(-0.105, 0.0), yawr=-4, yawl=4, pole=0.12):
    legs = {}
    for side, s, (x, y), yw in (("Right", 1, r, yawr), ("Left", -1, l, yawl)):
        legs[side] = (V((x, y, 0.085)), Rz(yw), V((s * pole, 1, 0)))
        rot[side + "Toes"] = Matrix.Identity(3)
    return legs


def pose_idle(t):
    rot = {}
    sw = math.sin(2 * pi * t)
    legs = stand_legs(rot)
    rot["Hips"] = Ry(1.6 * sw) @ Rz(1.2 * math.sin(2 * pi * t + 0.7))
    rot["Spine"] = Ry(-1.1 * sw)
    rot["Chest"] = Rx(-0.6)
    rot["UpperChest"] = Rx(1.0 * math.sin(2 * pi * t + 1.3)) @ Ry(-0.4 * sw)
    rot["Neck"] = Rx(-2)
    rot["Head"] = Rz(6 * math.sin(2 * pi * t + 2.0)) @ Rx(1.5 * math.sin(4 * pi * t))
    arm = {"Right": hang_arm("Right", 1, swing=1.5 * sw), "Left": hang_arm("Left", -1, swing=-1.5 * sw)}
    rot.update(curl("Right", 14, 20, 10))
    rot.update(curl("Left", 14, 20, 10))
    return dict(off=V((0.012 * sw, 0.0, -0.014 + 0.003 * math.cos(2 * pi * t + 1.3))), rot=rot, leg=legs, arm=arm)


def pose_walk(t):
    return gait(t, WALK)


def pose_run(t):
    return gait(t, RUN)


def pose_carry(t):
    sp = gait(t, CARRY, arms=False, lean=2.0)
    rot = sp["rot"]
    rot["Spine"] = rot["Spine"] @ Rx(3)
    rot["Chest"] = rot["Chest"] @ Rx(2)
    rot["Head"] = Rx(-2)
    arm = {}
    for side, s in SIDES:
        def fn(P, S, side=side, s=s):
            q = P["Chest"].to_3x3() @ S.rest["Chest"].to_3x3().inverted()
            piv = P["Chest"].translation
            rest_piv = S.rest["Chest"].translation
            hx, hy, hz = CRATE_H
            side_pt = CRATE_C + V((s * (hx + 0.012), 0.02, 0.03))
            wr = piv + q @ (side_pt - rest_piv) + q @ V((s * 0.012, -0.05, -0.02))
            return wr, q @ V((s * 0.6, -1, -0.5)), q @ V((0, 0.85, -0.5)), q @ V((-s, 0, 0))
        arm[side] = fn
        rot.update(curl(side, 50, 60, 30))
    sp["arm"] = arm
    return sp


def pose_work(t):
    rot = {}
    legs = {}
    for side, s, (x, y), yw in (("Right", 1, (0.17, -0.17), -22), ("Left", -1, (-0.12, 0.2), 6)):
        legs[side] = (V((x, y, 0.085)), Rz(yw), V((s * 0.35, 1, 0)))
        rot[side + "Toes"] = Matrix.Identity(3)
    w = math.sin(2 * pi * t)
    jit = 0.004 * math.sin(2 * pi * 9 * t)
    rot["Hips"] = Rz(-6) @ Rx(-14)
    rot["Spine"] = Rx(-9) @ Rz(-3)
    rot["Chest"] = Rx(-8) @ Ry(1.5 * w)
    rot["UpperChest"] = Rx(-4) @ Rx(0.8 * math.sin(2 * pi * t + 1))
    rot["Neck"] = Rx(-10)
    rot["Head"] = Rx(-16) @ Rz(-6 * w)
    W = V((0.02 + 0.07 * w, 0.52, 0.93 + jit))

    def right(P, S):
        d = V((-0.2, 0.75, -0.62)).normalized()
        upv = V((0, 0, 1))
        wr = W - d * 0.21 - upv * 0.06 + V((0.06, 0, 0))
        return wr, V((1, -0.4, -0.6)), d, V((-1, 0.0, 0.1))

    def left(P, S):
        wr = W + V((-0.19, -0.02, 0.07)) + V((0, 0, 0.004 * math.sin(2 * pi * 2 * t)))
        return wr, V((-1, -0.3, -0.5)), V((0.35, 1, -0.25)), V((0, 0.1, -1))
    rot.update(curl("Right", 72, 85, 42, idx2=80, mid2=95))
    rot.update(curl("Left", 8, 10, 14))
    return dict(off=V((0.03, 0.03, -0.11)), rot=rot, leg=legs, arm={"Right": right, "Left": left})


def pose_scan(t):
    rot = {}
    legs = stand_legs(rot, r=(0.12, -0.03), l=(-0.11, 0.07), yawr=-10, yawl=6)
    sw = 20 * math.sin(2 * pi * t)
    rot["Hips"] = Rz(0.2 * sw)
    rot["Spine"] = Rz(0.25 * sw) @ Rx(-1)
    rot["Chest"] = Rz(0.25 * sw)
    rot["UpperChest"] = Rz(0.3 * sw) @ Rx(0.8 * math.sin(4 * pi * t))
    rot["Neck"] = Rx(-6) @ Rz(0.15 * sw)
    rot["Head"] = Rx(-12) @ Rz(-3)

    def right(P, S):
        q = chest_q(P, S)
        sh = S.tail(P, "RightShoulder")
        wr = sh + q @ V((-0.07, 0.33, -0.05))
        eyes = P["Head"] @ V((0, 0.09, 0.08))
        z = (eyes - wr).normalized()
        return wr, q @ V((1, -0.5, -1)), q @ V((-0.15, 1, 0.45)), z

    def left(P, S):
        q = chest_q(P, S)
        sh = S.tail(P, "LeftShoulder")
        tap = 0.012 * max(0.0, math.sin(2 * pi * 6 * t))
        wr = sh + q @ V((0.2, 0.3, -0.1 + tap))
        return wr, q @ V((-1, -0.6, -1)), q @ V((1, 0.5, 0.05)), q @ V((0, 0.3, -1))
    rot.update(curl("Right", 55, 60, 25))
    c = curl("Left", 5, 70, 30, idx2=8)
    rot.update(c)
    return dict(off=V((0.0, 0.0, -0.012)), rot=rot, leg=legs, arm={"Right": right, "Left": left})


def pose_wave(t):
    rot = {}
    legs = stand_legs(rot)
    w = math.sin(2 * pi * 2 * t)
    rot["Hips"] = Ry(-1.5) @ Rz(2)
    rot["Spine"] = Ry(-1.0)
    rot["UpperChest"] = Ry(-2.0) @ Rx(0.6 * w)
    rot["RightShoulder"] = Ry(-14)
    rot["Neck"] = Rx(-1)
    rot["Head"] = Ry(4) @ Rz(5)

    def right(P, S):
        q = chest_q(P, S)
        sh = S.tail(P, "RightShoulder")
        wr = sh + q @ V((0.24 + 0.07 * w, 0.07, 0.26 - 0.015 * abs(w)))
        return wr, q @ V((1, -0.3, -1)), q @ V((0.35 * w, 0.05, 1)), q @ V((-0.15 * w, 1, 0))
    rot.update(curl("Right", 4, 4, 6))
    rot.update(curl("Left", 14, 20, 10))
    return dict(off=V((-0.01, 0.0, -0.014)), rot=rot, leg=legs, arm={"Right": right, "Left": hang_arm("Left", -1)})


ACTIONS = [("Idle", 90, pose_idle), ("Walk", WALK["T"], pose_walk), ("Run", RUN["T"], pose_run),
           ("Work", 60, pose_work), ("Carry", CARRY["T"], pose_carry), ("Scan", 96, pose_scan), ("Wave", 40, pose_wave)]
PROPS = {"Work": "Tool", "Scan": "Scanner", "Carry": "Crate"}


def apply_pose(rig, S, spec):
    M, _ = S.solve(spec)
    for name, m in M.items():
        pb = rig.pose.bones[name]
        pb.rotation_quaternion = m.to_quaternion()
        if name == "Hips":
            pb.location = m.translation


def bake_actions(rig):
    S = Solver(rig)
    ad = rig.animation_data_create()
    for name, frames, fn in ACTIONS:
        act = bpy.data.actions.new(name)
        ad.action = act
        prev = {}
        for f in range(frames + 1):
            M, _ = S.solve(fn((f % frames) / frames))
            for bname, m in M.items():
                if bname == "Root":
                    continue
                pb = rig.pose.bones[bname]
                q = m.to_quaternion()
                if bname in prev and prev[bname].dot(q) < 0:
                    q.negate()
                prev[bname] = q
                pb.rotation_quaternion = q
                pb.keyframe_insert("rotation_quaternion", frame=f, group=bname)
                if bname == "Hips":
                    pb.location = m.translation
                    pb.keyframe_insert("location", frame=f, group=bname)
        tr = ad.nla_tracks.new()
        tr.name = name
        tr.strips.new(name, 0, act)
        ad.action = None
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = V()
    return S


# ---------------------------------------------------------------- export


def export(role, objs, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=False,
                              export_yup=True, export_lights=False, export_cameras=False, export_skins=True,
                              export_morph=True, export_morph_normal=False, export_animation_mode="NLA_TRACKS",
                              export_frame_step=1, export_optimize_animation_size=True, export_def_bones=False)
    before = os.path.getsize(path)
    slim_glb(path)
    print("exported", path, before, "->", os.path.getsize(path), "bytes", flush=True)


def slim_glb(path):
    """Shrink the exported glb without changing what it shows (pure json + struct):
    - drop the translation/scale channels of every bone but Hips (never animated, the exporter writes them anyway;
      rotation channels all stay, constant ones included, so switching actions never leaves a bone behind);
    - store skin weights as normalised unsigned shorts (glTF core allows it; Godot decodes normalised accessors);
    - drop what is no longer referenced and repack the binary chunk."""
    import json
    import struct
    data = open(path, "rb").read()
    jl = struct.unpack("<I", data[12:16])[0]
    j = json.loads(data[20:20 + jl])
    bl = struct.unpack("<I", data[20 + jl:24 + jl])[0]
    binc = data[28 + jl:28 + jl + bl]
    nodes, acc, bvs = j["nodes"], j["accessors"], j["bufferViews"]
    for an in j.get("animations", []):
        keep = [c for c in an["channels"]
                if c["target"]["path"] == "rotation" or nodes[c["target"]["node"]].get("name") == "Hips"
                or c["target"]["path"] == "weights"]
        used = sorted({c["sampler"] for c in keep})
        remap = {o: n for n, o in enumerate(used)}
        an["samplers"] = [an["samplers"][o] for o in used]
        for c in keep:
            c["sampler"] = remap[c["sampler"]]
        an["channels"] = keep
    new_views = {}  # accessor index -> replacement bytes for its data
    for m in j["meshes"]:
        for prim in m["primitives"]:
            for k, a in prim["attributes"].items():
                A = acc[a]
                if not k.startswith("WEIGHTS_") or A["componentType"] != 5126 or a in new_views:
                    continue
                v = bvs[A["bufferView"]]
                stride = v.get("byteStride", 16)
                base = v.get("byteOffset", 0) + A.get("byteOffset", 0)
                out = bytearray()
                for i in range(A["count"]):
                    w = struct.unpack_from("<4f", binc, base + i * stride)
                    tot = sum(w) or 1.0
                    q = [int(round(x / tot * 65535)) for x in w]
                    q[q.index(max(q))] += 65535 - sum(q)
                    out += struct.pack("<4H", *q)
                new_views[a] = bytes(out)
                A["componentType"] = 5123
                A["normalized"] = True
                A.pop("byteOffset", None)
                A.pop("min", None)
                A.pop("max", None)
    # bone rotations as normalised shorts (core glTF; the importers renormalise the quaternions)
    for an in j.get("animations", []):
        for c in an["channels"]:
            if c["target"]["path"] != "rotation":
                continue
            a = an["samplers"][c["sampler"]]["output"]
            A = acc[a]
            if A["componentType"] != 5126 or a in new_views:
                continue
            v = bvs[A["bufferView"]]
            base = v.get("byteOffset", 0) + A.get("byteOffset", 0)
            out = bytearray()
            for i in range(A["count"]):
                q = struct.unpack_from("<4f", binc, base + i * 16)
                out += struct.pack("<4h", *[max(-32767, min(32767, int(round(x * 32767)))) for x in q])
            new_views[a] = bytes(out)
            A["componentType"] = 5122
            A["normalized"] = True
            A.pop("byteOffset", None)
            A.pop("min", None)
            A.pop("max", None)
    # what is referenced now
    used_acc = set()
    for m in j["meshes"]:
        for prim in m["primitives"]:
            used_acc |= set(prim["attributes"].values())
            if "indices" in prim:
                used_acc.add(prim["indices"])
            for t in prim.get("targets", []):
                used_acc |= set(t.values())
    for sk in j.get("skins", []):
        if "inverseBindMatrices" in sk:
            used_acc.add(sk["inverseBindMatrices"])
    for an in j.get("animations", []):
        for sm in an["samplers"]:
            used_acc |= {sm["input"], sm["output"]}
    acc_order = sorted(used_acc)
    amap = {o: n for n, o in enumerate(acc_order)}
    out = bytearray()
    views = []

    def add_view(blob, src=None):
        while len(out) % 4:
            out.append(0)
        v = {"buffer": 0, "byteOffset": len(out), "byteLength": len(blob)}
        if src is not None:
            for key in ("target",):
                if key in src:
                    v[key] = src[key]
        out.extend(blob)
        views.append(v)
        return len(views) - 1

    vmap = {}

    def old_view(i):
        if i not in vmap:
            v = bvs[i]
            o = v.get("byteOffset", 0)
            nv = add_view(binc[o:o + v["byteLength"]], v)
            if "byteStride" in v:
                views[nv]["byteStride"] = v["byteStride"]
            vmap[i] = nv
        return vmap[i]
    new_acc = []
    for o in acc_order:
        A = acc[o]
        if o in new_views:
            A["bufferView"] = add_view(new_views[o], bvs[A["bufferView"]])
        elif "bufferView" in A:
            A["bufferView"] = old_view(A["bufferView"])
        if "sparse" in A:
            sp = A["sparse"]
            sp["indices"]["bufferView"] = old_view(sp["indices"]["bufferView"])
            sp["values"]["bufferView"] = old_view(sp["values"]["bufferView"])
        new_acc.append(A)
    for img in j.get("images", []):
        if "bufferView" in img:
            img["bufferView"] = old_view(img["bufferView"])
    for m in j["meshes"]:
        for prim in m["primitives"]:
            prim["attributes"] = {k: amap[a] for k, a in prim["attributes"].items()}
            if "indices" in prim:
                prim["indices"] = amap[prim["indices"]]
            if "targets" in prim:
                prim["targets"] = [{k: amap[a] for k, a in t.items()} for t in prim["targets"]]
    for sk in j.get("skins", []):
        if "inverseBindMatrices" in sk:
            sk["inverseBindMatrices"] = amap[sk["inverseBindMatrices"]]
    for an in j.get("animations", []):
        for sm in an["samplers"]:
            sm["input"], sm["output"] = amap[sm["input"]], amap[sm["output"]]
    j["accessors"], j["bufferViews"] = new_acc, views
    while len(out) % 4:
        out.append(0)
    j["buffers"] = [{"byteLength": len(out)}]
    js = json.dumps(j, separators=(",", ":")).encode()
    js += b" " * ((4 - len(js) % 4) % 4)
    blob = struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(js) + 8 + len(out))
    blob += struct.pack("<I4s", len(js), b"JSON") + js + struct.pack("<I4s", len(out), b"BIN\0") + bytes(out)
    open(path, "wb").write(blob)


def report(role, objs):
    rows = []
    for o in objs:
        if o.type == "MESH":
            rows.append((o.name, sum(len(p.vertices) - 2 for p in o.data.polygons)))
    return rows


# ---------------------------------------------------------------- previews


def link(o, coll=None):
    (coll or bpy.context.scene.collection).objects.link(o)
    return o


def stage():
    scene = bpy.context.scene
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.36, 0.42, 0.52, 1.0)
    bg.inputs["Strength"].default_value = 0.55
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = -0.1
    sun = link(bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")))
    sun.data.energy = 3.6
    sun.data.angle = rad(4)
    sun.rotation_euler = (rad(50), 0, rad(150))
    fill = link(bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "AREA")))
    fill.data.energy = 120
    fill.data.size = 4
    fill.location = (-3, 5, 3)
    fill.rotation_euler = (V((0, 0, 1)) - fill.location).to_track_quat("-Z", "Y").to_euler()
    me = bpy.data.meshes.new("Ground")
    me.from_pydata([(-200, -200, 0), (200, -200, 0), (200, 200, 0), (-200, 200, 0)], [], [(0, 1, 2, 3)])
    g = link(bpy.data.objects.new("Ground", me))
    g.data.materials.append(plain("GroundMat", (0.3, 0.25, 0.19), 0.95))
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.film_transparent = False
    cam = link(bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera")))
    scene.camera = cam
    cam.data.clip_end = 500
    return cam


TEMPLATES = {}
_MAT_CACHE = {}


def tinted(base, rgb_srgb):
    key = (base.name, rgb_srgb)
    if key not in _MAT_CACHE:
        m = base.copy()
        m.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (*[srgb_to_lin(c) for c in rgb_srgb], 1.0)
        _MAT_CACHE[key] = m
    return _MAT_CACHE[key]


def instance(role, seed, loc, yaw, action, t, far=False, props=None):
    """A posed copy of a role's template (own rig, own mesh data for shape keys and tinted skin/hair)."""
    rig0, objs0 = TEMPLATES[role]
    S = Solver(rig0)
    rig = rig0.copy()
    rig.animation_data_clear()
    link(rig)
    va = variant(seed, role)
    show_props = set(props or ([PROPS[action]] if action in PROPS else []))
    if role == "science":
        show_props.add("SampleCase")
    if role == "eva":
        show_props.add("HelmetLamp")
    out = [rig]
    for o in objs0:
        if o.type != "MESH":
            continue
        base = o.name.split(":", 1)[1]
        if base in ("Tool", "Scanner", "Crate", "SampleCase", "HelmetLamp") and base not in show_props:
            continue
        if base.startswith("Hair") and base != va["style"]:
            continue
        if base == "Beard" and not va["beard"]:
            continue
        if base == role + "_far" and not far:
            continue
        if base == role and far:
            continue
        c = o.copy()
        c.data = o.data.copy()
        link(c)
        c.parent = rig
        c.modifiers["Armature"].object = rig
        c.hide_render = False
        c.hide_viewport = False
        for i, m in enumerate(c.data.materials):
            if m.name == "Skin":
                c.data.materials[i] = tinted(m, SKIN_TONES[va["skin"]])
            elif m.name == "Hair":
                c.data.materials[i] = tinted(m, HAIR_COLOURS[va["hair"]])
        if c.data.shape_keys:
            kb = c.data.shape_keys.key_blocks
            kb["Feminine"].value = va["fem"]
            kb["Heavy"].value = va["heavy"]
            kb["Slim"].value = va["slim"]
        out.append(c)
    rig.location = loc
    rig.rotation_euler = (0, 0, rad(yaw))
    rig.scale = (va["height"],) * 3
    rig.hide_render = False
    fn = dict((a, f) for a, _, f in ACTIONS)[action]
    apply_pose(rig, S, fn(t))
    return out


def shoot(cam, path, pos, target, lens=50, ortho=None, w=None, h=None):
    scene = bpy.context.scene
    scene.render.resolution_x = w or RES
    scene.render.resolution_y = h or int((w or RES) * 2 / 3)
    if ortho:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho
    else:
        cam.data.type = "PERSP"
        cam.data.lens = lens
    cam.location = pos
    cam.rotation_euler = (V(target) - V(pos)).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.context.view_layer.update()
    bpy.ops.render.render(write_still=True)
    print("rendered", path, flush=True)


def clear(objs):
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


def import_rover(path, loc, yaw):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    holder = link(bpy.data.objects.new("RoverHolder", None))
    for r in [o for o in new if o.parent is None]:
        r.parent = holder  # keep each root's own transform from the importer
    holder.location = loc
    holder.rotation_euler = (0, 0, rad(yaw))
    return new + [holder]


def previews(folder):
    os.makedirs(folder, exist_ok=True)
    cam = stage()
    seeds = {"engineer": 3, "science": 8, "security": 1, "eva": 5, "colonist": 13}
    if VARIANT >= 0:
        seeds = {r: VARIANT for r in seeds}
    roles = [r for r in ROLES if r in TEMPLATES]
    if "turn" in SHOTS:
        for r in roles:
            objs = []
            for k, yaw in enumerate((0, -45, -90, 180)):
                objs += instance(r, seeds[r], V((-1.5 + k * 1.0, 0, 0)), yaw + 180, "Idle", 0.15)
            shoot(cam, os.path.join(folder, f"{r}-turn.png"), (0, -9, 2.2), (0, 0, 0.98), ortho=4.2, w=RES, h=int(RES * 0.52))
            clear(objs)
    if "lineup" in SHOTS:
        # the survey rover Mk I side-on (front to +X), its door (sill 0.85, top 1.75) toward the camera, the five roles
        # in a row beside it and one more colonist at the door step for scale
        rover = os.path.join(REPO, "game", "models", "vehicles", "survey_rover.glb")
        objs = import_rover(rover, V((3.1, 0.95, 0)), LINEUP_ROVER_YAW) if os.path.exists(rover) else []
        for k, r in enumerate(roles):
            objs += instance(r, seeds[r], V((-2.1 + k * 0.78, -0.1, 0)), 180 + 6 * (k - 2), "Idle", 0.1 + 0.15 * k)
        objs += instance("colonist" if "colonist" in TEMPLATES else roles[0], 77, V((3.15, -0.95, 0)), 160, "Wave", 0.2)
        shoot(cam, os.path.join(folder, "crew-lineup.png"), (1.2, -10.4, 1.9), (1.2, 0.3, 1.0), lens=38, w=RES, h=int(RES * 0.5))
        clear(objs)
    if "poses" in SHOTS:
        objs = []
        plan = [("colonist", "Idle", 0.2), ("science", "Walk", 0.1), ("security", "Run", 0.15), ("engineer", "Work", 0.3),
                ("colonist", "Carry", 0.6), ("science", "Scan", 0.2), ("eva", "Wave", 0.12)]
        for k, (r, a, t) in enumerate(plan):
            if r in TEMPLATES:
                objs += instance(r, seeds[r] + k * 7, V((-2.7 + k * 0.9, 0, 0)), 205, a, t)
        shoot(cam, os.path.join(folder, "crew-poses.png"), (1.0, -7.5, 1.6), (0, 0, 0.9), lens=40, w=RES + 300, h=int((RES + 300) * 0.42))
        clear(objs)
    for shot, prm, act, n in (("walk", WALK, "Walk", 8), ("run", RUN, "Run", 6)):
        if shot not in SHOTS:
            continue
        r = "science" if shot == "walk" else "security"
        if r not in TEMPLATES:
            continue
        objs = []
        for k in range(n):
            objs += instance(r, seeds[r], V((0, -2.6 + k * 0.75, 0)), 0, act, k / n)
        shoot(cam, os.path.join(folder, f"crew-{shot}.png"), (9, 0.0, 2.2), (0, 0, 1.0), ortho=6.4, w=RES + 300, h=int((RES + 300) * 0.36))
        clear(objs)
    if "variants" in SHOTS:
        objs = []
        for k in range(12):
            r = roles[k % len(roles)]
            x = -2.5 + (k % 6) * 1.0 + (0.5 if k >= 6 else 0)
            y = 0 if k < 6 else 1.2
            objs += instance(r, 100 + k * 13, V((x, y, 0)), 190 + (k * 17) % 30, "Idle", (k * 0.37) % 1)
        shoot(cam, os.path.join(folder, "crew-variants.png"), (0.6, -8.5, 2.2), (0.3, 0.6, 0.85), lens=42, w=RES + 200, h=int((RES + 200) * 0.5))
        clear(objs)
    if "faces" in SHOTS:
        objs = []
        face_roles = [x for x in ("engineer", "science", "security", "colonist") if x in TEMPLATES] or roles
        for k in range(5):
            r = face_roles[k % len(face_roles)]
            objs += instance(r, 200 + k * 11, V((-0.84 + k * 0.42, 0, 0)), 188 - 4 * k, "Idle", 0.0)
        shoot(cam, os.path.join(folder, "crew-faces.png"), (0.0, -4.2, 1.62), (0, 0, 1.56), lens=FACE_LENS, w=RES, h=int(RES * 0.36))
        clear(objs)
    if "lod" in SHOTS:
        objs = []
        for k, r in enumerate([x for x in ("engineer", "eva", "colonist") if x in TEMPLATES]):
            objs += instance(r, seeds[r], V((-2.0 + k * 1.6, 0, 0)), 200, "Idle", 0.2)
            objs += instance(r, seeds[r], V((-1.4 + k * 1.6, 0, 0)), 200, "Idle", 0.2, far=True)
        shoot(cam, os.path.join(folder, "crew-lod.png"), (0.2, -7, 1.3), (0.2, 0, 0.9), lens=50, w=RES, h=int(RES * 0.5))
        clear(objs)
    if "game" in SHOTS:
        rover = os.path.join(REPO, "game", "models", "vehicles", "survey_rover.glb")
        objs = import_rover(rover, V((0, 0, 0)), 30) if os.path.exists(rover) else []
        plan = [("engineer", "Work", (2.2, -1.0), 120), ("science", "Scan", (-2.4, 0.6), 250), ("colonist", "Carry", (1.0, -3.0), 160),
                ("security", "Walk", (-1.2, -2.6), 200), ("eva", "Idle", (3.2, 1.4), 220), ("colonist", "Wave", (-3.2, -1.4), 160)]
        for k, (r, a, (x, y), yaw) in enumerate(plan):
            if r in TEMPLATES:
                objs += instance(r, seeds[r] + 31 * k, V((x, y, 0)), yaw, a, 0.3)
        d = 18.0
        e = rad(28)
        shoot(cam, os.path.join(folder, "crew-game.png"), (0, -d * math.cos(e), d * math.sin(e)), (0, 0, 0.6), lens=35, w=RES, h=int(RES * 0.6))
        clear(objs)


# ---------------------------------------------------------------- main


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    palette()
    tmp = os.path.join(bpy.app.tempdir or "/tmp", "crew_atlas")
    os.makedirs(tmp, exist_ok=True)
    materials(tmp)
    skeleton()
    summary = []
    for role in [r for r in ROLES if r in ONLY]:
        rig, objs = build_role(role)
        bake_actions(rig)
        tri = report(role, objs)
        path = os.path.join(OUT_DIR, f"{role}.glb")
        if GAME or arg("--out-dir"):
            export(role, objs, path)
            summary.append((role, tri, os.path.getsize(path)))
        else:
            summary.append((role, tri, 0))
        # keep as a preview template under prefixed names
        for o in objs:
            o.name = f"{role}:{o.name}"
            if o.data is not None:
                o.data.name = o.name
            o.hide_render = True
        TEMPLATES[role] = (rig, objs)
    for role, tri, size in summary:
        print("CREW", role, "bytes", size, " ".join(f"{n}={t}" for n, t in tri), flush=True)
    print("CREW speeds walk %.2f carry %.2f run %.2f u/s" % (speed(WALK), speed(CARRY), speed(RUN)), flush=True)
    if PREVIEW:
        previews(PREVIEW)


if __name__ == "__main__":
    main()
