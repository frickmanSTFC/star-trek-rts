"""Character portraits for the comm screen and character creation (owner 2026-11-11: mockups only for now;
"we will need many different models of people").

Head-and-shoulders busts rendered in Cycles, built on the colony crew figures of build_crew.py (imported as a
module, never changed: its body, head surface, hair shells, rig and Idle pose are reused as they are; only this
script's own process patches a few of its module globals, see "How it is built" below).

What it makes (docs/mockups/character/portraits/ by default):
  <species>_<sex>_<dept>.jpg   Federation people in the ORIGINAL colony-service jumpsuit (charcoal, gold hexagon
                               badge with a chevron, see blender/crew.md) with the department colour on the shoulder
                               yoke and sleeve bands: Exploration #3E7FD6, Defense #C8423A, Maintenance #D9A23A,
                               Archaeology #4E9A5A. Human, Vulcan, Andorian, Tellarite, Bajoran, Trill, Betazoid,
                               one woman and one man each, plus four extra Humans (one per department) = 18.
  <species>_<sex>.jpg          other powers for later comms: Klingon, Romulan, Cardassian, Ferengi, in original
                               outfits in their faction colours (no canon insignia).
  portraits.json               every file with species, faction, sex, department (or null), skin, hair, eyes, age band
  lineup.jpg                   four Federation officers (one per department) full length, 3/4 view, 1600 x 900
  sheet.jpg                    contact sheet (system python3 + PIL: --sheet)

Shot: 640 x 640, slight 3/4 (body turned ~22 deg, head back toward the lens, eyes on the camera), 85 mm, soft
key + rim + weak fill, dark gradient backdrop with a faint element in the faction colour (Federation: two hexagon
outlines; Klingon: angular slashes; Romulan: arcs; Cardassian: stepped bars; Ferengi: a ring of discs), shallow depth
of field, Cycles 40 samples, denoised, JPEG quality 88.

Run (bpy 5.x venv; ~10-15 min for everything on 4 cores):
  /home/user/.bpyvenv/bin/python blender/build_portraits.py
  /home/user/.bpyvenv/bin/python blender/build_portraits.py --species vulcan,klingon --sex f --samples 16
  python3 blender/build_portraits.py --sheet docs/mockups/character/portraits
Flags: --species <id>[,<id>] (human, vulcan, andorian, tellarite, bajoran, trill, betazoid, klingon, romulan,
cardassian, ferengi), --sex f|m, --res 640, --samples 40, --out DIR, --shots portraits,lineup, --only <file stem>
[,<stem>], --sheet DIR (contact sheet only, no bpy), --seed N (also render look(N) for each picked species: the
future generator's random person, <species>_<sex>_seedN.jpg, not listed in portraits.json).

How it is built:
  * build_crew.py is loaded with importlib (with a clean sys.argv, so its own flags stay at their defaults). Its
    palette, skeleton, Body, torso/limb/hand/boot meshes, jumpsuit details (collar, Owner yoke, seams, pockets,
    badge, name tape, belt, cuffs, sleeve bands) and Idle pose are used unchanged; the role used is "engineer" with
    its harness/tool belt switched off (bc.engineer -> no-op), bare hands, and finer mesh resolution (TORSO_SEGS,
    LIMB_SEGS, BAND_T, BAND_L raised in this process only). Other powers swap bc.details for their outfit.
  * Heads: bc.head_local (the sculpted ellipsoid) is wrapped: the species adds its forehead/brow shapes as smooth
    displacements of that surface (Klingon crest + five transverse ridges, Romulan V ridge, Cardassian spoon with a
    raised rim and brow-to-temple ridges, Ferengi enlarged cranium with a central furrow and wavy forehead ridges,
    a grin crease, Tellarite heavy brow and jowls), so everything that samples the head (hair shells, brows, nose,
    eyes) follows it. The head is meshed much finer than the game's (128 longitudes, 0.6 deg rows over the face).
  * Face parts are this script's own (higher resolution than crew's face_features): a lofted nose (wider/longer per
    species, Bajoran and Ferengi ridges across the bridge, a Tellarite snout with two nostrils instead), eyeballs
    with sclera, iris, pupil and a clear cornea (built after posing, turned to look into the lens; Betazoid irises
    all black), upper and lower lids, a lash line, eyebrows (natural, bushy, thin or upswept), ears (human, pointed,
    Ferengi), teeth for the Ferengi grin, lip/cheek tint as a vertex colour on the skin.
  * Hair: one generalised shell per style (crop, short, swept, bun, curly, bowl with a straight fringe, slick,
    shaggy, bob, long, wild) running from the hairline over the crown; long styles hang straight below the widest
    part of the skull and keep clear of the neck, collar and shoulders (body surface + the outfit's gap); hairlines
    notch round pointed ears so the tips never go through hair. Beards are a shell on the jaw (full, Klingon with
    a chin point, Tellarite shaggy with sideburns).
  * Extras: Andorian antennae (tapered, segmented, bulb tips, rooted in the hair), Trill spots (two staggered rows
    from each temple down past the ear and the side of the neck), Bajoran ear chain on the right ear (cuff, chain
    to the lobe, dangling strand), Cardassian neck ridges (ears to shoulders, flaring).
  * Clothing for other powers is built on the same torso: Klingon dark leather with layered metal pauldrons,
    chest plates and a high collar with dark-red edging; Romulan grey-green tunic with big quilted padded shoulders
    and a diagonal closure; Cardassian grey-brown ribbed chest armour with raised shoulder plates and an open neck;
    Ferengi patterned jacket (procedural brocade) with a tall flared gold collar and lapels.

Toward the game's portrait / comm-screen generator (plan only, nothing exported yet):
  1. look(seed, species, sex) below already maps a seed to a look with build_crew's h32 hash (skin tone, hair
     colour and style, beard, iris, build, age band, per-species palettes); the same hash in C# (CrewModels.Hash)
     would give every crew member, rival captain and contact a stable face from an id.
  2. Export one rigged bust glb per species (head + neck + shoulders, skinned to Neck/Head/Jaw bones) with blend
     shapes: build (Feminine/Heavy/Slim, as the crew figures), face (brow raise, frown, smile, blink, eyes left/
     right/up/down) and mouth visemes for talking (closed, A, E, O, F/V, M/B/P), the hair styles, beards and
     species extras as separate nodes (show one), and outfits per faction as swappable meshes with the Owner/
     department colour as a material parameter.
  3. Godot renders the bust into a SubViewport on the view screen (like ModelThumbnails' hero), drives the visemes
     from the dialogue text (or audio amplitude), blinks and glances at random, and caches a still per character
     for lists; the backdrop and framing here are the target look.
"""
import json
import math
import os
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
OUT = arg("--out") or os.path.join(REPO, "docs", "mockups", "character", "portraits")

DEPTS = {"exploration": "#3E7FD6", "defense": "#C8423A", "maintenance": "#D9A23A", "archaeology": "#4E9A5A"}
FACTION_COLOUR = {"federation": (0.24, 0.50, 0.84), "klingon": (0.78, 0.16, 0.12), "romulan": (0.22, 0.64, 0.44),
                  "cardassian": (0.82, 0.54, 0.22), "ferengi": (0.88, 0.68, 0.22)}
FACTION_OF = {"human": "federation", "vulcan": "federation", "andorian": "federation", "tellarite": "federation",
              "bajoran": "federation", "trill": "federation", "betazoid": "federation", "klingon": "klingon",
              "romulan": "romulan", "cardassian": "cardassian", "ferengi": "ferengi"}
SPECIES_ORDER = list(FACTION_OF)


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


# ---------------------------------------------------------------- the cast (sRGB colours)

# skin / hair / iris presets (sRGB)
SK = dict(fair=(0.95, 0.79, 0.69), light=(0.90, 0.72, 0.60), tan=(0.80, 0.60, 0.45), olive=(0.74, 0.56, 0.40),
          brown=(0.58, 0.40, 0.28), dark=(0.40, 0.26, 0.18), deep=(0.29, 0.19, 0.13), eastasian=(0.88, 0.71, 0.56),
          andorian=(0.42, 0.60, 0.86), cardassian=(0.62, 0.62, 0.60), ferengi=(0.86, 0.66, 0.50),
          tellarite=(0.70, 0.52, 0.42), klingon=(0.50, 0.33, 0.24), romulan=(0.86, 0.72, 0.60))
HC = dict(black=(0.05, 0.045, 0.045), darkbrown=(0.16, 0.10, 0.07), brown=(0.32, 0.20, 0.11), auburn=(0.45, 0.20, 0.10),
          blond=(0.72, 0.58, 0.36), ash=(0.55, 0.50, 0.44), grey=(0.62, 0.61, 0.60), white=(0.92, 0.93, 0.95),
          silver=(0.78, 0.78, 0.80))
IR = dict(brown=(0.30, 0.17, 0.08), darkbrown=(0.16, 0.09, 0.05), hazel=(0.42, 0.33, 0.14), green=(0.25, 0.42, 0.25),
          blue=(0.25, 0.45, 0.68), grey=(0.45, 0.50, 0.55), black=(0.02, 0.02, 0.025), violet=(0.36, 0.28, 0.55),
          amber=(0.55, 0.36, 0.10))

CAST = [
    # id, species, sex, dept, age band, skin, hair colour, hair style, iris, build {fem, heavy, slim}, beard, notes
    dict(id="human_f_exploration", species="human", sex="f", dept="exploration", age="30-40", skin="tan", hair="darkbrown",
         style="bun", iris="brown"),
    dict(id="human_m_defense", species="human", sex="m", dept="defense", age="40-50", skin="light", hair="brown",
         style="short", iris="blue", heavy=0.35),
    dict(id="vulcan_f_archaeology", species="vulcan", sex="f", dept="archaeology", age="40-60 (looks 30s)", skin="olive",
         hair="black", style="vulcan_f", iris="darkbrown"),
    dict(id="vulcan_m_exploration", species="vulcan", sex="m", dept="exploration", age="60-90 (looks 40s)", skin="light",
         hair="darkbrown", style="bowl", iris="brown", slim=0.4),
    dict(id="andorian_f_defense", species="andorian", sex="f", dept="defense", age="25-35", skin="andorian", hair="white",
         style="bob", iris="violet"),
    dict(id="andorian_m_maintenance", species="andorian", sex="m", dept="maintenance", age="30-40", skin="andorian",
         hair="white", style="swept", iris="grey", heavy=0.3),
    dict(id="tellarite_f_maintenance", species="tellarite", sex="f", dept="maintenance", age="30-40", skin="tellarite",
         hair="auburn", style="shaggy", iris="darkbrown", heavy=0.5),
    dict(id="tellarite_m_defense", species="tellarite", sex="m", dept="defense", age="40-50", skin="tellarite",
         hair="brown", style="shaggy", iris="darkbrown", heavy=0.8, beard="tellarite"),
    dict(id="bajoran_f_archaeology", species="bajoran", sex="f", dept="archaeology", age="25-35", skin="light",
         hair="auburn", style="bob", iris="hazel"),
    dict(id="bajoran_m_exploration", species="bajoran", sex="m", dept="exploration", age="35-45", skin="brown",
         hair="black", style="crop", iris="darkbrown"),
    dict(id="trill_f_exploration", species="trill", sex="f", dept="exploration", age="25-35", skin="fair", hair="blond",
         style="long", iris="green"),
    dict(id="trill_m_archaeology", species="trill", sex="m", dept="archaeology", age="35-45", skin="light", hair="brown",
         style="swept", iris="blue"),
    dict(id="betazoid_f_defense", species="betazoid", sex="f", dept="defense", age="25-35", skin="olive", hair="black",
         style="curly", iris="black"),
    dict(id="betazoid_m_maintenance", species="betazoid", sex="m", dept="maintenance", age="30-40", skin="tan",
         hair="black", style="short", iris="black"),
    # four extra Humans, one per department
    dict(id="human_m_exploration", species="human", sex="m", dept="exploration", age="55-60", skin="deep", hair="grey",
         style="crop", iris="darkbrown", beard="full", heavy=0.2),
    dict(id="human_f_defense", species="human", sex="f", dept="defense", age="25-30", skin="eastasian", hair="black",
         style="short", iris="darkbrown", slim=0.3),
    dict(id="human_m_maintenance", species="human", sex="m", dept="maintenance", age="25-30", skin="dark", hair="black",
         style="curly", iris="brown"),
    dict(id="human_f_archaeology", species="human", sex="f", dept="archaeology", age="50-60", skin="fair", hair="ash",
         style="bun", iris="grey", heavy=0.25),
    # other powers
    dict(id="klingon_f", species="klingon", sex="f", age="30-40", skin="klingon", hair="black", style="wild",
         iris="brown"),
    dict(id="klingon_m", species="klingon", sex="m", age="40-50", skin="klingon", hair="darkbrown", style="wild",
         iris="darkbrown", beard="klingon", heavy=0.5),
    dict(id="romulan_f", species="romulan", sex="f", age="40-60 (looks 30s)", skin="romulan", hair="black", style="bowl",
         iris="green"),
    dict(id="romulan_m", species="romulan", sex="m", age="60-90 (looks 40s)", skin="tan", hair="darkbrown", style="bowl",
         iris="brown"),
    dict(id="cardassian_f", species="cardassian", sex="f", age="30-40", skin="cardassian", hair="black",
         style="slick_bun", iris="blue"),
    dict(id="cardassian_m", species="cardassian", sex="m", age="40-50", skin="cardassian", hair="black", style="slick",
         iris="grey", heavy=0.3),
    dict(id="ferengi_f", species="ferengi", sex="f", age="25-35", skin="ferengi", hair=None, style=None, iris="blue"),
    dict(id="ferengi_m", species="ferengi", sex="m", age="40-50", skin="ferengi", hair=None, style=None, iris="grey",
         heavy=0.2),
]
LINEUP = ["vulcan_m_exploration", "andorian_f_defense", "human_m_maintenance", "bajoran_f_archaeology"]


# ---------------------------------------------------------------- contact sheet (system python3 + PIL, no bpy)

def contact_sheet(folder):
    from PIL import Image, ImageDraw
    meta = json.load(open(os.path.join(folder, "portraits.json")))
    tiles = [(e["file"], e) for e in meta["portraits"] if os.path.exists(os.path.join(folder, e["file"]))]
    tw, cols, pad, lab = 200, 6, 10, 30
    rows = (len(tiles) + cols - 1) // cols
    lw = cols * (tw + pad) - pad
    lineup = os.path.join(folder, "lineup.jpg")
    lh = int(lw * 9 / 16) if os.path.exists(lineup) else 0
    sheet = Image.new("RGB", (lw + 2 * pad, 44 + rows * (tw + lab + pad) + lh + pad * 2), (14, 16, 20))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 14), "Character portraits - blender/build_portraits.py (mockup)", fill=(225, 228, 235))
    for i, (f, e) in enumerate(tiles):
        im = Image.open(os.path.join(folder, f)).convert("RGB").resize((tw, tw), Image.LANCZOS)
        x = pad + (i % cols) * (tw + pad)
        y = 44 + (i // cols) * (tw + lab + pad)
        sheet.paste(im, (x, y))
        txt = f"{e['species']} {e['sex']}" + (f" - {e['department']}" if e["department"] else f" - {e['faction']}")
        d.text((x + 2, y + tw + 4), txt, fill=(200, 205, 215))
        if e["department"]:
            d.rectangle((x, y + tw, x + tw - 1, y + tw + 2), fill=hexrgb_int(DEPTS[e["department"]]))
    if lh:
        im = Image.open(lineup).convert("RGB").resize((lw, lh), Image.LANCZOS)
        sheet.paste(im, (pad, 44 + rows * (tw + lab + pad)))
    out = os.path.join(folder, "sheet.jpg")
    sheet.save(out, quality=85, optimize=True)
    print("sheet", out, sheet.size, os.path.getsize(out))


def hexrgb_int(h):
    return tuple(int(c * 255) for c in hexrgb(h))


if "--sheet" in argv:
    contact_sheet(arg("--sheet"))
    sys.exit(0)

# ---------------------------------------------------------------- build_crew as a module

import importlib.util  # noqa: E402

_saved_argv = sys.argv
sys.argv = [os.path.join(HERE, "build_crew.py")]
_spec = importlib.util.spec_from_file_location("build_crew", os.path.join(HERE, "build_crew.py"))
bc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bc)
sys.argv = _saved_argv

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

V = Vector
pi = math.pi
rad = math.radians
smooth, gauss, clamp, adiff, lerp, orth = bc.smooth, bc.gauss, bc.clamp, bc.adiff, bc.lerp, bc.orth
HEAD_C = bc.HEAD_C
RES = int(arg("--res", "640"))
SAMPLES = int(arg("--samples", "40"))
SHOTS = [s for s in arg("--shots", "portraits,lineup").split(",") if s]
ONLY_SPECIES = [s for s in (arg("--species") or "").split(",") if s]
ONLY_SEX = arg("--sex")
ONLY = [s for s in (arg("--only") or "").split(",") if s]
SEED = arg("--seed")

_neck0 = bc.neck_limb


def neck_thick(body, inflate=0.0):
    return _neck0(body, inflate + 0.0085 - 0.004 * body.fem)


bc.neck_limb = neck_thick
# finer meshes than the game figure (this process only)
bc.TORSO_SEGS, bc.LIMB_SEGS, bc.BAND_T, bc.BAND_L = 72, 32, 64, 32
_tz = bc.TORSO_Z
bc.TORSO_Z = sorted(set(_tz + [(a + b) / 2 for a, b in zip(_tz, _tz[1:]) if a > 1.18]))

CUR = {}      # the character being built: spec + species
CHAR_MATS = {}


def srgb_lin(c):
    return tuple(bc.srgb_to_lin(x) for x in c)


# ---------------------------------------------------------------- species head shapes (wrapping bc.head_local)

_head_local0 = bc.head_local


def seg_d2(x, z, a, b):
    ax_, az_ = b[0] - a[0], b[1] - a[1]
    t = clamp(((x - a[0]) * ax_ + (z - a[1]) * az_) / (ax_ * ax_ + az_ * az_), 0.0, 1.0)
    return math.hypot(x - a[0] - ax_ * t, z - a[1] - az_ * t)


SOCKET = 0.0055


def head_local_species(la, lo, body):
    p = _head_local0(la, lo, body)
    sp = CUR.get("species")
    if sp is None:
        return p
    x, y, z = p.x, p.y, p.z
    ax = abs(x)
    fem = body.fem
    # portrait refinements for everyone: fuller jaw and chin, shaped lips, philtrum, deeper brow
    fy = smooth(0.02, 0.06, y)
    x *= 1 + (0.13 - 0.07 * fem) * smooth(-0.025, -0.075, z) * smooth(-0.04, 0.03, y)
    x *= 1 + 0.05 * gauss(z + 0.004, 0.016) * smooth(-0.02, 0.03, y)                          # cheekbones out
    y += 0.0032 * gauss(ax - 0.047, 0.011) * gauss(z + 0.008, 0.011) * fy                      # cheekbones forward
    y -= 0.0018 * gauss(ax - 0.045, 0.012) * gauss(z + 0.045, 0.012) * fy                      # hollow under them
    y -= 0.0022 * gauss(x, 0.009) * gauss(z - 0.019, 0.006) * fy                              # nasion
    y += (0.0035 - 0.001 * fem) * gauss(x, 0.017) * gauss(z + 0.089, 0.010) * fy
    bow = 0.0016 * gauss(ax - 0.0065, 0.0035)
    y += (0.0016 + 0.0006 * fem) * smooth(0.023, 0.010, ax) * gauss(z + 0.0468 - bow, 0.0026) * fy
    y += (0.0020 + 0.0008 * fem) * smooth(0.022, 0.008, ax) * gauss(z + 0.0590, 0.0032) * fy
    y -= 0.0008 * gauss(x, 0.0026) * gauss(z + 0.0405, 0.0038) * fy
    y -= 0.0014 * gauss(x, 0.013) * gauss(z + 0.0685, 0.0038) * fy
    y += (0.0022 - 0.0008 * fem) * gauss(z - 0.031, 0.0065) * smooth(0.056, 0.024, ax) * fy
    for sx in (-1, 1):  # deeper sockets: the lids, not the skin, shape the eye opening
        y -= SOCKET * math.exp(-((x - sx * 0.033) ** 2 / (2 * 0.0145 ** 2) + (z - 0.010) ** 2 / (2 * 0.0105 ** 2))) * fy
    if sp in ("human", "vulcan", "andorian", "bajoran", "trill", "betazoid"):
        return V((x, y, z))
    front = smooth(-0.01, 0.045, y)
    k = 1.0 - 0.35 * fem
    d = 0.0
    if sp == "klingon":
        d += 0.0048 * k * gauss(z - 0.034, 0.0075) * smooth(0.068, 0.03, ax) * front           # heavy brow
        d += 0.004 * k * gauss(x, 0.0058) * smooth(0.04, 0.056, z) * smooth(-0.03, 0.01, y)   # crest
        for i in range(4):
            zc = 0.052 + 0.0155 * i + 7.0 * x * x
            d += 0.0029 * k * (1 - 0.1 * i) * gauss(z - zc, 0.0034) * smooth(0.054 - 0.005 * i, 0.022, ax) * smooth(-0.03, 0.02, y)
        x *= 1.03
    elif sp == "romulan":
        for sx in (-1, 1):
            dd = seg_d2(x, z, (sx * 0.046, 0.066), (sx * 0.003, 0.027))
            d += 0.0026 * gauss(dd, 0.0028) * front
        d += 0.0015 * gauss(z - 0.033, 0.006) * smooth(0.06, 0.03, ax) * front
    elif sp == "cardassian":
        r = math.sqrt((x / 0.0175) ** 2 + ((z - 0.071) / 0.026) ** 2)
        d += 0.0032 * gauss(r - 1.0, 0.15) * front                                              # spoon rim
        d += 0.0014 * smooth(1.0, 0.55, r) * front                                              # raised bowl
        d += 0.0030 * gauss(x, 0.0042) * smooth(0.017, 0.03, z) * smooth(0.05, 0.04, z) * front  # handle
        ang = abs(adiff(lo, 90))
        lc = 17 - 7 * smooth(30, 90, ang)
        d += 0.0034 * gauss(la - lc, 3.2) * smooth(24, 36, ang) * smooth(104, 92, ang)           # brow-temple ridge
        d += 0.0022 * gauss(la - lc + 7, 2.5) * smooth(40, 55, ang) * smooth(100, 90, ang)
        y -= 0.0025 * gauss(z + 0.088, 0.01) * gauss(x, 0.012) * -1                              # chin knob
    elif sp == "ferengi":
        s = smooth(-0.02, 0.10, z)
        x *= 1 + 0.17 * s
        if y < 0:
            y *= 1 + 0.16 * s
        z *= 1 + 0.07 * s
        d -= 0.0045 * gauss(x, 0.009) * smooth(0.07, 0.105, z) * smooth(-0.06, 0.0, y)           # cranial furrow
        for i in range(4):
            zc = 0.046 + 0.0135 * i + 0.0025 * math.sin(x * 140 + i)
            d += 0.0019 * gauss(z - zc, 0.0027) * smooth(0.06, 0.02, ax) * front
        d += 0.0034 * gauss(z - 0.034, 0.007) * smooth(0.062, 0.03, ax) * front
        m = smooth(0.026, 0.012, ax) * gauss(z + 0.0525, 0.0024) * smooth(0.02, 0.05, y)
        y -= 0.0052 * m                                                                          # parted grin
        z += 0.0045 * smooth(0.006, 0.024, ax) * gauss(z + 0.052, 0.01) * smooth(0.02, 0.05, y)  # mouth corners up
    elif sp == "tellarite":
        d += 0.0085 * k * gauss(z - 0.036, 0.0085) * smooth(0.072, 0.03, ax) * front
        x *= 1 + 0.10 * smooth(0.02, -0.07, z)
        y += 0.004 * smooth(0.0, -0.06, z) * front
    q = V((x, y, z))
    if d:
        q = q + q.normalized() * d
    return q


bc.head_local = head_local_species


def hl(x, z, body):
    return bc.face_y(x, z, body)


# ---------------------------------------------------------------- materials

MATS = {}


def node(nt, kind, loc=(0, 0)):
    n = nt.nodes.new(kind)
    n.location = loc
    return n


def principled(name, rgb_lin, rough=0.5, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*rgb_lin, 1.0)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    return m, p


def bump(m, p, scale, strength, distance=0.0005, stretch=(1, 1, 1), kind="noise"):
    nt = m.node_tree
    tc = node(nt, "ShaderNodeTexCoord")
    mp = node(nt, "ShaderNodeMapping")
    mp.inputs["Scale"].default_value = stretch
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    if kind == "noise":
        tx = node(nt, "ShaderNodeTexNoise")
        tx.inputs["Detail"].default_value = 3
    else:
        tx = node(nt, "ShaderNodeTexVoronoi")
    tx.inputs["Scale"].default_value = scale
    nt.links.new(mp.outputs["Vector"], tx.inputs["Vector"])
    b = node(nt, "ShaderNodeBump")
    b.inputs["Strength"].default_value = strength
    b.inputs["Distance"].default_value = distance
    nt.links.new(tx.outputs["Fac"] if kind == "noise" else tx.outputs["Distance"], b.inputs["Height"])
    nt.links.new(b.outputs["Normal"], p.inputs["Normal"])
    return tx


def skin_material(name, rgb_srgb):
    m, p = principled(name, srgb_lin(rgb_srgb), 0.47)
    nt = m.node_tree
    at = node(nt, "ShaderNodeAttribute")
    at.attribute_name = "tint"
    mul = node(nt, "ShaderNodeVectorMath")
    mul.operation = "MULTIPLY"
    mul.inputs[0].default_value = srgb_lin(rgb_srgb)
    nt.links.new(at.outputs["Color"], mul.inputs[1])
    nt.links.new(mul.outputs["Vector"], p.inputs["Base Color"])
    p.inputs["Subsurface Weight"].default_value = 0.18
    nz = node(nt, "ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 40
    nz.inputs["Detail"].default_value = 4
    tcn = node(nt, "ShaderNodeTexCoord")
    nt.links.new(tcn.outputs["Object"], nz.inputs["Vector"])
    rr = node(nt, "ShaderNodeMapRange")
    rr.inputs["To Min"].default_value = 0.40
    rr.inputs["To Max"].default_value = 0.62
    nt.links.new(nz.outputs["Fac"], rr.inputs["Value"])
    nt.links.new(rr.outputs["Result"], p.inputs["Roughness"])
    mv = node(nt, "ShaderNodeMapRange")
    mv.inputs["To Min"].default_value = 0.9
    mv.inputs["To Max"].default_value = 1.08
    nt.links.new(nz.outputs["Fac"], mv.inputs["Value"])
    mul2 = node(nt, "ShaderNodeVectorMath")
    mul2.operation = "SCALE"
    nt.links.new(mul.outputs["Vector"], mul2.inputs[0])
    nt.links.new(mv.outputs["Result"], mul2.inputs["Scale"])
    nt.links.new(mul2.outputs["Vector"], p.inputs["Base Color"])
    p.inputs["Subsurface Radius"].default_value = (1.0, 0.42, 0.25)
    p.inputs["Subsurface Scale"].default_value = 0.006
    p.inputs["Specular IOR Level"].default_value = 0.45
    bump(m, p, 900, 0.06, 0.0003)
    return m


def hair_material(name, rgb_srgb):
    m, p = principled(name, srgb_lin(rgb_srgb), 0.5)
    nt = m.node_tree
    tx = bump(m, p, 420, 0.35, 0.0006, stretch=(1.0, 1.0, 0.08))
    ramp = node(nt, "ShaderNodeValToRGB")
    c = srgb_lin(rgb_srgb)
    ramp.color_ramp.elements[0].color = (*[x * 0.6 for x in c], 1)
    ramp.color_ramp.elements[1].color = (*[min(1.0, x * 1.3 + 0.004) for x in c], 1)
    nt.links.new(tx.outputs["Fac"], ramp.inputs["Factor"])
    nt.links.new(ramp.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Anisotropic"].default_value = 0.35
    p.inputs["Specular IOR Level"].default_value = 0.22
    return m


def cornea_material():
    m = bpy.data.materials.new("Cornea")
    m.use_nodes = True
    nt = m.node_tree
    out = nt.nodes["Material Output"]
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    tr = node(nt, "ShaderNodeBsdfTransparent")
    gl = node(nt, "ShaderNodeBsdfGlossy")
    gl.inputs["Roughness"].default_value = 0.02
    fr = node(nt, "ShaderNodeFresnel")
    fr.inputs["IOR"].default_value = 1.5
    mx = node(nt, "ShaderNodeMath")
    mx.operation = "ADD"
    mx.inputs[1].default_value = 0.05
    nt.links.new(fr.outputs["Fac"], mx.inputs[0])
    mul = node(nt, "ShaderNodeMixShader")
    nt.links.new(mx.outputs["Value"], mul.inputs["Fac"])
    nt.links.new(tr.outputs["BSDF"], mul.inputs[1])
    nt.links.new(gl.outputs["BSDF"], mul.inputs[2])
    nt.links.new(mul.outputs["Shader"], out.inputs["Surface"])
    m.blend_method = "HASHED" if hasattr(m, "blend_method") else None
    return m


def iris_material(name, rgb_srgb):
    m, p = principled(name, srgb_lin(rgb_srgb), 0.35)
    nt = m.node_tree
    tc = node(nt, "ShaderNodeTexCoord")
    vo = node(nt, "ShaderNodeTexNoise")
    vo.inputs["Scale"].default_value = 900
    vo.inputs["Detail"].default_value = 4
    nt.links.new(tc.outputs["Object"], vo.inputs["Vector"])
    ramp = node(nt, "ShaderNodeValToRGB")
    c = srgb_lin(rgb_srgb)
    ramp.color_ramp.elements[0].color = (*[x * 0.45 for x in c], 1)
    ramp.color_ramp.elements[1].color = (*[min(1.0, x * 1.6 + 0.01) for x in c], 1)
    nt.links.new(vo.outputs["Fac"], ramp.inputs["Factor"])
    nt.links.new(ramp.outputs["Color"], p.inputs["Base Color"])
    return m


def pattern_material(name, a_srgb, b_srgb, rough=0.6):
    """Ferengi brocade: a voronoi lattice of medallions over diagonal waves, two colours."""
    m, p = principled(name, srgb_lin(a_srgb), rough)
    nt = m.node_tree
    tc = node(nt, "ShaderNodeTexCoord")
    vo = node(nt, "ShaderNodeTexVoronoi")
    vo.inputs["Scale"].default_value = 38
    wv = node(nt, "ShaderNodeTexWave")
    wv.inputs["Scale"].default_value = 9
    wv.inputs["Distortion"].default_value = 3
    nt.links.new(tc.outputs["Generated"], vo.inputs["Vector"])
    nt.links.new(tc.outputs["Generated"], wv.inputs["Vector"])
    mth = node(nt, "ShaderNodeMath")
    mth.operation = "MULTIPLY"
    nt.links.new(vo.outputs["Distance"], mth.inputs[0])
    nt.links.new(wv.outputs["Fac"], mth.inputs[1])
    ramp = node(nt, "ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.12
    ramp.color_ramp.elements[1].position = 0.2
    ramp.color_ramp.elements[0].color = (*srgb_lin(b_srgb), 1)
    ramp.color_ramp.elements[1].color = (*srgb_lin(a_srgb), 1)
    nt.links.new(mth.outputs["Value"], ramp.inputs["Factor"])
    nt.links.new(ramp.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Sheen Weight"].default_value = 0.4
    return m


# outfit swatches (sRGB, roughness, metal, kind)
OUTFIT_MATS = {
    "kl_leather": ((0.12, 0.085, 0.07), 0.55, 0.0, "leather"),
    "kl_metal": ((0.36, 0.34, 0.32), 0.36, 1.0, "metal"),
    "kl_red": ((0.40, 0.07, 0.05), 0.5, 0.0, "leather"),
    "rom_cloth": ((0.30, 0.35, 0.31), 0.8, 0.0, "cloth"),
    "rom_pad": ((0.42, 0.48, 0.42), 0.75, 0.0, "cloth"),
    "rom_trim": ((0.10, 0.17, 0.13), 0.6, 0.0, "cloth"),
    "rom_metal": ((0.62, 0.64, 0.62), 0.3, 1.0, "metal"),
    "car_under": ((0.17, 0.15, 0.13), 0.7, 0.0, "cloth"),
    "car_armour": ((0.38, 0.34, 0.29), 0.42, 0.25, "leather"),
    "car_rib": ((0.27, 0.24, 0.20), 0.45, 0.25, "leather"),
    "fer_shirt": ((0.80, 0.70, 0.48), 0.7, 0.0, "cloth"),
    "fer_trim": ((0.85, 0.62, 0.22), 0.3, 1.0, "metal"),
    "teeth": ((0.86, 0.80, 0.62), 0.3, 0.0, "plain"),
    "mouth": ((0.18, 0.05, 0.05), 0.6, 0.0, "plain"),
    "lash": ((0.025, 0.02, 0.02), 0.6, 0.0, "plain"),
    "eyewhite_hd": ((0.86, 0.82, 0.79), 0.25, 0.0, "plain"),
    "pupil": ((0.004, 0.004, 0.004), 0.2, 0.0, "plain"),
    "limbus": ((0.03, 0.025, 0.02), 0.3, 0.0, "plain"),
    "trill_spot": ((0.36, 0.22, 0.13), 0.55, 0.0, "plain"),
    "silver": ((0.80, 0.80, 0.82), 0.22, 1.0, "metal"),
    "chain_gold": ((0.85, 0.66, 0.30), 0.25, 1.0, "metal"),
    "snout": ((1.0, 1.0, 1.0), 0.5, 0.0, "plain"),
}


def material(key):
    if key in CHAR_MATS:
        return CHAR_MATS[key]
    if key in MATS:
        return MATS[key]
    if key == "fer_jacket":
        m = pattern_material("fer_jacket", (0.62, 0.36, 0.12), (0.30, 0.12, 0.26))
    elif key == "Cornea":
        m = cornea_material()
    elif key in OUTFIT_MATS:
        rgb, rough, metal, kind = OUTFIT_MATS[key]
        m, p = principled(key, srgb_lin(rgb), rough, metal)
        if kind == "leather":
            bump(m, p, 260, 0.25, 0.0004)
        elif kind == "cloth":
            bump(m, p, 1400, 0.15, 0.0002)
    elif key in bc.SW:
        _, rgb, rough, metal = bc.SW[key]
        m, p = principled(key, rgb, rough, metal)
        if key in ("eng_suit", "eng_suit2", "trim", "seam"):
            bump(m, p, 1500, 0.12, 0.0002)
    else:
        m, _ = principled(key, (0.5, 0.0, 0.5), 0.5)
        print("WARNING: unknown material key", key, flush=True)
    MATS[key] = m
    return m


def char_materials(c):
    CHAR_MATS.clear()
    sk = SK[c["skin"]]
    hc = HC[c["hair"]] if c.get("hair") else (0.1, 0.08, 0.07)
    CHAR_MATS["Skin"] = skin_material("Skin_" + c["id"], sk)
    CHAR_MATS["Hair"] = hair_material("Hair_" + c["id"], hc)
    CHAR_MATS["brow"] = hair_material("Brow_" + c["id"], tuple(x * 0.85 for x in hc))
    CHAR_MATS["iris"] = iris_material("Iris_" + c["id"], IR[c["iris"]])
    if c.get("dept"):
        m, _ = principled("Dept_" + c["id"], srgb_lin(tuple(x * 0.9 for x in hexrgb(DEPTS[c["dept"]]))), 0.72)
        bump(m, m.node_tree.nodes["Principled BSDF"], 1500, 0.12, 0.0002)
        CHAR_MATS["Owner"] = m
    else:
        m, _ = principled("Owner_" + c["id"], srgb_lin(FACTION_COLOUR[c["faction"]]), 0.5)
        CHAR_MATS["Owner"] = m
    CHAR_MATS["nostril"] = skin_material("Nostril_" + c["id"], tuple(x * 0.3 for x in sk))
    sn = tuple(x * y for x, y in zip(sk, (0.93, 0.78, 0.78)))
    CHAR_MATS["snout"] = skin_material("Snout_" + c["id"], sn)


# ---------------------------------------------------------------- meshes from MB lists

def skin_tint(p):
    """Vertex colour multiplier for the skin: lips, a little warmth on cheeks and nose, darker round the eyes."""
    sp = CUR.get("species")
    q = p - HEAD_C
    x, y, z = q.x, q.y, q.z
    if z < -0.13 or y < -0.02:
        return (1.0, 1.0, 1.0)
    ax = abs(x)
    lip = smooth(0.027, 0.016, ax) * smooth(0.014, 0.006, abs(z + 0.0525)) * smooth(0.03, 0.055, y)
    cheek = gauss(ax - 0.045, 0.014) * gauss(z + 0.02, 0.016) * smooth(0.02, 0.05, y)
    eye = gauss(ax - 0.034, 0.014) * gauss(z - 0.006, 0.012) * smooth(0.02, 0.05, y)
    lipc = CUR.get("lip", (0.82, 0.60, 0.60))
    r, g, b = 1.0, 1.0, 1.0
    r, g, b = lerp(r, lipc[0], lip), lerp(g, lipc[1], lip), lerp(b, lipc[2], lip)
    w = 0.10 if sp not in ("andorian", "cardassian") else 0.03
    r, g, b = r * (1 - 0.0 * cheek), g * (1 - w * cheek), b * (1 - w * 1.2 * cheek)
    e = 0.12 * eye
    return (r * (1 - e), g * (1 - e), b * (1 - e * 0.8))


def make_obj(name, mb, rig, tint=False, weights=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(c) for c in mb.co], [], list(mb.faces))
    keys = []
    for k in mb.mats:
        if k not in keys:
            keys.append(k)
    for k in keys:
        me.materials.append(material(k))
    me.polygons.foreach_set("material_index", [keys.index(k) for k in mb.mats])
    me.validate(verbose=False)
    me.update()
    me.shade_smooth()
    try:
        me.set_sharp_from_angle(angle=rad(55))
    except Exception:  # noqa: BLE001
        pass
    ca = me.color_attributes.new("tint", "FLOAT_COLOR", "POINT")
    cols = []
    for v in me.vertices:
        t = skin_tint(V(v.co)) if tint else (1.0, 1.0, 1.0)
        cols += [t[0], t[1], t[2], 1.0]
    ca.data.foreach_set("color", cols)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if rig is not None and weights:
        ob.parent = rig
        groups = {}
        for i, wd in enumerate(bc.solve_weights(mb)):
            for b, w in wd.items():
                if b not in groups:
                    groups[b] = ob.vertex_groups.new(name=b)
                groups[b].add([i], w, "REPLACE")
        mod = ob.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
    return ob


# ---------------------------------------------------------------- head, face, ears

HD_LATS = sorted(set([-86, -80, -74, -68, -62, -57] + [round(-55 + 0.6 * i, 2) for i in range(int(95 / 0.6) + 1)]
                     + [42, 45, 48, 52, 56, 60, 64, 68, 72, 76, 80, 84, 87]))
HD_LONS = 128
RIG_HEAD = [("Head", 0.0)]


def head_mesh_hd(mb, body):
    lons = bc.head_lons(HD_LONS)
    rows = [[bc.head_pt(la, lo, body) for lo in lons] for la in HD_LATS]
    idx = bc.sheet(mb, rows, "Skin", RIG_HEAD, closed=True, outward=lambda m: m - HEAD_C)
    bc.fan(mb, idx[0], bc.head_pt(-90, 90, body), "Skin", RIG_HEAD, outward=V((0, 0, -1)))
    bc.fan(mb, idx[-1], bc.head_pt(90, 90, body), "Skin", RIG_HEAD, outward=V((0, 0, 1)))


NOSE = [(0.017, 0.0062, 0.0002), (0.008, 0.0068, 0.0016), (-0.004, 0.0082, 0.0062), (-0.016, 0.0108, 0.0122),
        (-0.025, 0.0142, 0.0178), (-0.0315, 0.0188, 0.0158), (-0.0365, 0.0135, 0.0075), (-0.040, 0.0075, 0.001)]


def nose(mb, body, ws=1.0, ps=1.0, ridges=(), ridge_amp=0.0012, top=None):
    tab = NOSE[::-1]
    zs = [t[0] for t in tab]
    wv = [t[1] for t in tab]
    pv = [t[2] for t in tab]
    z0, z1 = (top if top is not None else NOSE[0][0]), NOSE[-1][0]
    n = 64
    rows, cen = [], []
    for i in range(n + 1):
        z = z0 + (z1 - z0) * i / n
        w = bc.catmull(wv, zs, z) * ws
        pr = bc.catmull(pv, zs, z) * ps
        b0 = hl(0.0, z, body)
        bw = hl(w, z, body)
        row = []
        for k in range(17):
            a = pi * k / 16
            x = -w * math.cos(a)
            s = math.sin(a)
            prot = pr * s ** 0.8
            for zr in ridges:
                prot += ridge_amp * gauss(z - zr, 0.0011) * s ** 0.6
            base = b0 + (bw - b0) * (x / w) ** 2 - 0.0022
            row.append(V((x, base + prot, HEAD_C.z + z)))
        rows.append(row)
        cen.append(V((0, b0 - 0.012, HEAD_C.z + z)))
    bc.sheet(mb, rows, "Skin", RIG_HEAD, centres=cen)
    # nostrils: dark ovals tucked under the tip, mostly inside the nose
    ri = min(range(len(rows)), key=lambda i: abs(rows[i][0].z - (HEAD_C.z - 0.0368)))
    for sx in (-1, 1):
        q = min(rows[ri], key=lambda v: abs(v.x - sx * 0.0066 * ws))
        c = q + V((0, -0.0032, -0.0007))
        bc.ellipsoid(mb, c, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.0030 * ws, 0.0036, 0.0015), 12, 5, "nostril",
                     RIG_HEAD)


def mouth_line(mb, body, width=0.0225, zc=-0.0527, droop=0.0004):
    a_row, m_row, b_row = [], [], []
    for i in range(25):
        t = i / 24
        x = -width + 2 * width * t
        e = 1 - abs(2 * t - 1)
        z = zc - droop * (1 - e) + 0.0006 * gauss(x, 0.004)
        h = 0.00075 * smooth(0.0, 0.35, e)
        y = hl(x, z, body) + 0.0002
        a_row.append(V((x, y, HEAD_C.z + z + h)))
        m_row.append(V((x, y + 0.0001, HEAD_C.z + z)))
        b_row.append(V((x, y, HEAD_C.z + z - h)))
    bc.sheet(mb, [a_row, m_row, b_row], "mouth", RIG_HEAD, outward=lambda m: V((0, 1, 0)))


def snout(mb, body):
    """Tellarite snout: a broad, slightly upturned barrel with a flat front pad and two big nostrils."""
    zc = -0.028
    yb = hl(0.0, zc, body)
    rows, cen = [], []
    for t, sc, dy in ((0.0, 1.15, -0.010), (0.25, 1.05, 0.004), (0.55, 1.0, 0.013), (0.8, 0.96, 0.019), (1.0, 0.9, 0.022)):
        c = V((0, yb + dy, HEAD_C.z + zc + 0.003 * t))
        rows.append([c + V((math.cos(2 * pi * j / 24) * 0.0185 * sc, 0, math.sin(2 * pi * j / 24) * 0.0135 * sc))
                     for j in range(24)])
        cen.append(c)
    idx = bc.sheet(mb, rows, "snout", RIG_HEAD, closed=True, centres=cen)
    front = cen[-1] + V((0, 0.002, 0))
    bc.fan(mb, idx[-1], front, "snout", RIG_HEAD, outward=V((0, 1, 0.15)))
    for sx in (-1, 1):
        c = front + V((sx * 0.0068, 0.0002, -0.0005))
        bc.ellipsoid(mb, c, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.0042, 0.0012, 0.0052), 10, 4, "mouth",
                     RIG_HEAD)


def eye_centre(sx, body, r):
    ex, ez = sx * 0.033, 0.012
    return V((ex, hl(ex, ez, body) + SOCKET * 0.98 - 0.0104 * r / 0.0118, HEAD_C.z + ez)), r


def lids(mb, body, sx, r, upper_edge=23, lower_edge=-27, lash=True):
    """A lid shell round the eyeball with an almond opening (corners at lon 16 / 166, 90 = straight ahead)."""
    c, _ = eye_centre(sx, body, r)
    inner_lo = 90 + sx * 0  # (symmetric almond; the tear duct side is a little rounder)

    def pt(la, lo, rr):
        return c + V((math.cos(rad(la)) * math.cos(rad(lo)), math.cos(rad(la)) * math.sin(rad(lo)), math.sin(rad(la)))) * rr
    lons = [2 + 176 * j / 40 for j in range(41)]
    del inner_lo

    def opening(lo, edge, pw):
        t = clamp((lo - 16) / 150, 0, 1)
        skew = 1 + 0.12 * (t - 0.5) * (-sx) * (1 if edge > 0 else -1)
        return edge * math.sin(pi * t) ** pw * skew
    for edge, pw, top in ((upper_edge, 0.55, 86), (lower_edge, 0.8, -86)):
        rows = []
        for f, dr in ((0.0, 0.0003), (0.0, 0.0013), (0.06, 0.0015), (0.18, 0.0019), (0.32, 0.0016), (0.55, 0.0012), (0.8, 0.0008), (1.0, 0.0006)):
            rows.append([pt(lerp(opening(lo, edge, pw), top, f), lo, r + dr) for lo in lons])
        bc.sheet(mb, rows, "Skin", RIG_HEAD, outward=lambda m: m - c)
    if lash:
        ls = [lo for lo in lons if 20 < lo < 162]
        a = [pt(opening(lo, upper_edge, 0.55) + 0.6, lo, r + 0.0014) for lo in ls]
        b = [pt(opening(lo, upper_edge, 0.55) + 4.5, lo, r + 0.0026) for lo in ls]
        bc.sheet(mb, [a, b], "lash", RIG_HEAD, outward=lambda m: m - c)


def brows(mb, body, kind="natural", thick=1.0):
    for sx in (-1, 1):
        def path(t):
            x = sx * (0.012 + 0.039 * t)
            if kind == "upswept":
                z = 0.030 + 0.003 * t + 0.017 * t ** 2.2
            elif kind == "bushy":
                z = 0.037 + 0.004 * math.sin(pi * (0.25 + 0.75 * t)) - 0.003 * t
            else:
                z = 0.032 + 0.006 * math.sin(pi * (0.3 + 0.7 * t)) - 0.004 * t
            return x, z
        n = 14
        a_row, m_row, b_row = [], [], []
        for i in range(n + 1):
            t = i / n
            x, z = path(t)
            wid = (0.0046 if kind != "bushy" else 0.0068) * thick * (1.0 - 0.55 * t ** 1.5) * (0.75 + 0.25 * smooth(0, 0.2, t))
            if kind == "thin":
                wid *= 0.6
            y = hl(x, z, body)
            a_row.append(V((x, y + 0.0009, HEAD_C.z + z + wid * 0.55)))
            m_row.append(V((x, y + 0.0009 + 0.0011 * thick, HEAD_C.z + z)))
            b_row.append(V((x, y + 0.0009, HEAD_C.z + z - wid * 0.45)))
        bc.sheet(mb, [a_row, m_row, b_row], "brow", RIG_HEAD, outward=lambda m: V((0, 1, 0)))


def ear_frame(sx, kind):
    if kind == "ferengi":
        c = HEAD_C + V((sx * 0.084, -0.022, -0.008))
        ax = V((sx * math.cos(rad(42)), math.sin(rad(42)), 0.0)).normalized()
        az = V((0, -0.12, 1)).normalized()
    else:
        c = HEAD_C + V((sx * 0.074, -0.012, -0.006))
        ax = V((sx * math.cos(rad(18)), -math.sin(rad(18)), 0))
        az = V((0, -math.sin(rad(10)), math.cos(rad(10))))
    ay = orth(az.cross(ax) * sx, ax)
    return c, ax, ay, az


def ears(mb, body, kind="human", scale=1.0):
    out = {}
    for sx in (-1, 1):
        c, ax, ay, az = ear_frame(sx, kind)
        if kind == "ferengi":
            radii = (0.011 * scale, 0.040 * scale, 0.056 * scale)
            cc = c + ax * 0.004 - ay * 0.010 * scale
        else:
            radii = (0.009, 0.019, 0.031)
            cc = c + ax * 0.004

        def ear(la, lo, p, cc=cc, ax=ax, ay=ay, az=az):
            rel = p - cc
            out_ = rel.dot(ax)
            r = math.cos(rad(la))
            if out_ > 0:
                p = p - ax * out_ * (0.85 if kind != "ferengi" else 0.7) * smooth(0.55, 0.0, abs(la) / 90.0) * (1 if r > 0.4 else 0.4)
            up = rel.dot(az) / radii[2]
            fw = rel.dot(ay) / radii[1]
            if kind == "pointed":
                tip = smooth(0.15, 1.0, up)
                p = p + az * 0.019 * tip ** 2 - ay * (0.009 * tip ** 2 + 0.006 * fw * tip)
            if kind == "ferengi":
                p = p + ax * 0.006 * scale * math.sin(fw * 4.0) * smooth(0.2, 0.9, abs(up))  # rippled rim
                p = p - ay * 0.010 * scale * smooth(-0.2, 1.0, up) ** 2 + az * 0.006 * scale * smooth(0.6, 1, up)
                p = p - ax * 0.010 * scale * smooth(0.0, 1.0, -fw)  # front edge tucks toward the head
            return p
        bc.ellipsoid(mb, cc, (ax, ay, az), radii, 28, 16, "Skin", RIG_HEAD, deform=ear)
        out[sx] = e = (cc, ax, ay, az, radii, ear)
        # helix rim over the top and down the back to the lobe, and the antihelix inside it
        rim_pts = [ear_pt(e, la, 90) for la in range(30, 90, 6)] + [ear_pt(e, la, -90) for la in range(89, -56, -6)]
        rim_pts = [p + ax * 0.0016 for p in rim_pts]
        k = 1.0 if kind != "ferengi" else 1.6 * scale
        loft(mb, rim_pts, lambda t: 0.0023 * k * (0.6 + 0.4 * math.sin(pi * clamp(t * 1.1, 0, 1))), "Skin", RIG_HEAD, segs=10)
        inner = [ear_in(e, la, -90, 0.62) + ax * 0.0014 for la in range(60, -36, -8)]
        loft(mb, inner, lambda t: 0.0017 * k * math.sin(pi * (0.1 + 0.8 * t)), "Skin", RIG_HEAD, segs=8)
    return out


def ear_in(e, la, lo, f):
    cc, ax, ay, az, radii, deform = e
    d = (math.cos(rad(la)) * math.cos(rad(lo)), math.cos(rad(la)) * math.sin(rad(lo)), math.sin(rad(la)))
    p = cc + ax * radii[0] * 0.3 + ay * d[1] * radii[1] * f + az * d[2] * radii[2] * f
    return deform(la, 0, p)


def ear_pt(e, la, lo, extra=0.0):
    cc, ax, ay, az, radii, deform = e
    d = (math.cos(rad(la)) * math.cos(rad(lo)), math.cos(rad(la)) * math.sin(rad(lo)), math.sin(rad(la)))
    p = cc + ax * d[0] * radii[0] + ay * d[1] * radii[1] + az * d[2] * radii[2]
    p = deform(la, lo, p)
    return p + (p - cc).normalized() * extra


def bead_chain(mb, pts, r, mat, cand):
    for i, p in enumerate(pts):
        rr = r * (1.0 if i % 2 == 0 else 0.75)
        bc.ellipsoid(mb, p, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (rr, rr, rr), 6, 3, mat, cand)


def bajoran_earring(mb, e):
    """Original ear chain: a cuff on the upper rim, a chain down the back of the ear to the lobe, a hanging strand."""
    cuff = [ear_pt(e, la, -70 + 0.0 * la, 0.0012) for la in range(30, 80, 6)]
    bc.tube(mb, cuff[0], cuff[-1], 0.0016, 0.0016, 8, "silver", RIG_HEAD)
    for p in cuff:
        bc.ellipsoid(mb, p, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.0017,) * 3, 8, 4, "silver", RIG_HEAD)
    path = [ear_pt(e, la, -95, 0.003) for la in range(60, -75, -9)]
    pts = []
    for a, b in zip(path, path[1:]):
        for k in range(3):
            pts.append(a.lerp(b, k / 3))
    bead_chain(mb, pts, 0.0011, "chain_gold", RIG_HEAD)
    lobe = ear_pt(e, -78, 0, 0.001)
    drop = [lobe + V((0, 0.0, -0.0028 * i)) for i in range(1, 12)]
    bead_chain(mb, drop, 0.0011, "chain_gold", RIG_HEAD)
    end = drop[-1] + V((0, 0, -0.004))
    bc.ellipsoid(mb, end, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.0026, 0.0026, 0.0042), 10, 5, "silver", RIG_HEAD)


def ferengi_teeth(mb, body):
    zc = -0.0525
    row = []
    for i in range(23):
        x = -0.024 + 0.048 * i / 22
        row.append(V((x, hl(x, zc, body) - 0.0035, HEAD_C.z + zc + 0.0035 * smooth(0.006, 0.024, abs(x)))))
    a = [p + V((0, 0, 0.0045)) for p in row]
    b = [p - V((0, 0, 0.0045)) for p in row]
    bc.sheet(mb, [a, row, b], "mouth", RIG_HEAD, outward=lambda m: V((0, 1, 0)))
    for k in range(10):
        x = -0.0165 + 0.033 * k / 9
        lift = 0.0035 * smooth(0.006, 0.024, abs(x))
        y = hl(x, zc, body) - 0.0018 * (1 + abs(x) * 30)
        for up, dz, off in ((1, 0.0024, 0.0), (-1, -0.0026, 0.0018)):
            base = V((x + off, y, HEAD_C.z + zc + lift + dz))
            tip = base + V((0, 0.0004, -up * 0.0042 * (0.8 + 0.4 * ((k * 7) % 3) / 2)))
            bc.tube(mb, base, tip, 0.0017, 0.0002, 6, "teeth", RIG_HEAD, caps=(True, False))


def antennae(mb, body):
    for sx in (-1, 1):
        lo = 90 - sx * 24
        la = 60
        base = bc.head_pt(la, lo, body, 0.002)
        n = bc.head_n(la, lo, body)
        up = V((0, 0, 1))
        pts = []
        for i in range(25):
            t = i / 24
            p = base + n * (0.018 * t) + up * (0.10 * t ** 1.05) + V((sx * 0.026 * t ** 1.6, 0.012 * math.sin(pi * t) - 0.012 * t ** 2, 0))
            pts.append(p)
        radius = lambda t: (0.0075 - 0.0038 * t + 0.0006 * math.sin(t * 40) ** 2) * (0.85 if CUR["sex"] == "f" else 1.0)  # noqa: E731
        loft(mb, pts, radius, "Skin", RIG_HEAD)
        tip = pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.003
        r = 0.0062 * (0.85 if CUR["sex"] == "f" else 1.0)
        bc.ellipsoid(mb, tip, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (r, r, r * 1.25), 14, 7, "Skin", RIG_HEAD)
        base_in = base - n * 0.01
        bc.tube(mb, base_in, base + n * 0.003, 0.0105, 0.0085, 16, "Skin", RIG_HEAD, caps=(False, False))


def loft(mb, pts, radius, mat, cand, segs=16):
    rows, cen = [], []
    n = len(pts)
    prev = None
    for i, p in enumerate(pts):
        t = i / (n - 1)
        d = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        e1 = orth(prev if prev is not None else V((1, 0, 0)), d)
        prev = e1
        e2 = d.cross(e1)
        r = radius(t)
        rows.append([p + (e1 * math.cos(2 * pi * j / segs) + e2 * math.sin(2 * pi * j / segs)) * r for j in range(segs)])
        cen.append(p)
    return bc.sheet(mb, rows, mat, cand, closed=True, centres=cen)


def trill_spots(mb, body):
    nl = bc.neck_limb(body)
    ncand = [("Neck", 0), ("Head", 0.012), ("UpperChest", 0.01)]
    for sx in (-1, 1):
        def lon(ang):
            return 90 - sx * ang
        path = []
        for i in range(15):
            t = i / 14
            la = 40 - 85 * t
            ang = 66 + 24 * smooth(0.0, 1.0, t) + 8 * smooth(0.7, 1.0, t)
            path.append(("h", la, lon(ang)))
        for i in range(7):
            u = 0.85 - 0.75 * i / 6
            th = (0 if sx > 0 else pi) - sx * rad(14)
            path.append(("n", u, th))
        for i, (kind, a, b) in enumerate(path):
            for row in (0, 1):
                if (i + row) % 2:
                    continue
                size = 0.0032 - 0.0011 * i / len(path)
                if kind == "h":
                    off = 2.2 * (1 if row else -1)
                    la, lo = a, b + off * sx * 0.7
                    p = bc.head_pt(la, lo, body, 0.0004)
                    n = bc.head_n(la, lo, body)
                    cand = RIG_HEAD
                else:
                    th = b + rad(10) * (1 if row else -1) * sx
                    p = nl.pt(a, th, 0.0004)
                    n = nl.n(a, th)
                    cand = ncand
                t1 = orth(V((0, 0, 1)), n)
                t2 = n.cross(t1)
                jit = 0.8 + 0.4 * ((i * 37 + row * 11) % 7) / 6
                bc.ellipsoid(mb, p, (t1, t2, n), (size * jit, size * 0.85 * jit, 0.0004), 10, 3, "trill_spot", cand)


def neck_ridges(mb, body):
    """Cardassian neck ridges: from behind each ear down the side of the neck, flaring onto the shoulders."""
    nl = bc.neck_limb(body)
    cand = [("Neck", 0), ("Head", 0.01), ("UpperChest", 0.0)]
    for sx in (-1, 1):
        th_side = 0 if sx > 0 else pi
        pts, ns = [], []
        lo = 90 - sx * 112
        for la in (-6, -18, -30):
            pts.append(bc.head_pt(la, lo, body))
            ns.append(bc.head_n(la, lo, body))
        for u in (0.85, 0.6, 0.35, 0.12):
            th = th_side - sx * rad(28)
            pts.append(nl.pt(u, th))
            ns.append(nl.n(u, th))
        for z, thd in ((1.51, 22), (1.49, 18)):
            th = th_side - sx * rad(thd)
            pts.append(body.torso(z, th, 0.004))
            ns.append(body.torso_n(z, th))
        rows, cen = [], []
        n = len(pts)
        for i, (p, nn) in enumerate(zip(pts, ns)):
            t = i / (n - 1)
            d = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            side = nn.cross(d).normalized()
            w = 0.006 + 0.012 * t ** 1.4
            h = 0.004 + 0.004 * t
            rows.append([p - nn * 0.003 + side * w * math.cos(pi * k / 10) + nn * h * math.sin(pi * k / 10) ** 0.7
                         + nn * 0.0015 * math.sin(pi * k / 10) * math.sin(pi * k / 5) ** 2 for k in range(11)])
            cen.append(p - nn * 0.01)
        bc.sheet(mb, rows, "Skin", cand, centres=cen)


# ---------------------------------------------------------------- hair

HAIRLINE_F = None


_HL = [(-90, -34), (-60, -28), (-30, -8), (0, 16), (12, 15), (30, 22), (60, 34), (90, 44)]


def base_hairline(lo):
    """crew's HAIRLINE (round the ears) through a smooth spline instead of per-segment smoothsteps."""
    b = 90 - abs(adiff(lo, 90))
    return bc.catmull([p[1] for p in _HL], [p[0] for p in _HL], b)


def la_at_z(z):
    return math.degrees(math.asin(clamp(z / 0.118, -1, 1))) if z >= 0 else math.degrees(math.asin(clamp(z / 0.112, -1, 1)))


def ang_front(lo):
    return abs(adiff(lo, 90))  # 0 front .. 180 back


def ear_notch(lo, z_top):
    """Raise the hairline round each ear (the ears stay in front of / under the hair edge)."""
    out = -90.0
    for ear_lo in (-9, 189):
        g = smooth(26, 12, abs(adiff(lo, ear_lo)))
        out = max(out, la_at_z(z_top) * g - 90 * (1 - g))
    return out


def clearance(z, lo, gap):
    """Distance from the body axis to the neck / torso surface at height z, direction lo, plus gap."""
    th = rad(lo)
    if z >= 1.535:
        r = 0.058 + 0.004
        cy = -0.012
    else:
        p = CUR["body"].torso(max(z, 0.80), th, 0.0)
        cy = CUR["body"].section(max(z, 0.80))[3]
        r = math.hypot(p.x, p.y - cy)
    return r + gap, cy


def hair(mb, body, style):
    """A hair shell from the hairline over the crown; long styles hang straight below the widest part of the skull."""
    if not style:
        return
    sex = CUR["sex"]
    gap = CUR.get("hair_gap", 0.02)
    notch = CUR.get("pointed", False)

    def low(lo):
        h = base_hairline(lo)
        a = ang_front(lo)
        if style == "swept":
            h -= 9 * gauss(adiff(lo, 70), 22)
        if style in ("curly", "shaggy", "wild"):
            h -= 4
        if style in ("bowl", "vulcan_f"):
            zc = 0.052 if style == "bowl" else 0.058
            zs = zc - 0.004 * smooth(40, 80, a)
            if style == "bowl":
                zs = lerp(zs, -0.030, smooth(118, 150, a))
            else:
                zs = lerp(zs, -0.03, smooth(118, 150, a))
            h = la_at_z(zs)
        if style in ("slick", "slick_bun"):
            h = base_hairline(lo) + 4 - 6 * gauss(adiff(lo, 90), 8)
        if style == "wild":
            h = lerp(64, h, smooth(22, 80, a))
        if notch or style in ("bowl", "vulcan_f"):
            h = max(h, ear_notch(lo, 0.060 if notch else 0.034))
        return h

    def vol(lo, v):
        extra = 0.0
        if style in ("bob", "long", "wild"):  # puffed out over the ears
            l0 = low(lo)
            la = l0 + v * (88 - l0)
            g = max(smooth(32, 14, abs(adiff(lo, e))) for e in (-9, 189))
            extra = 0.019 * g * smooth(36, 16, la)
        taper = 1.0 if style in ("bowl", "vulcan_f", "bob", "wild", "curly", "shaggy") else 0.2 + 0.8 * smooth(0.0, 0.16, v)
        return (extra + vol0(lo, v)) * taper + 0.0012 * (1 - taper)

    def vol0(lo, v):
        top = math.sin(v * pi / 2)
        front = gauss(adiff(lo, 90), 50)
        a = ang_front(lo)
        if style == "crop":
            return 0.0035 + 0.002 * top
        if style == "short":
            return 0.005 + 0.012 * top * (0.7 + 0.3 * front)
        if style == "swept":
            return 0.006 + 0.018 * top * (0.6 + 0.6 * gauss(adiff(lo, 110), 40)) + 0.006 * front * (1 - v)
        if style in ("bun", "slick_bun"):
            return 0.005 + 0.007 * top
        if style == "slick":
            return 0.004 + 0.006 * top
        if style == "curly":
            return 0.016 + 0.024 * top + 0.004 * math.sin(rad(lo) * 9) * math.sin(v * 11)
        if style == "shaggy":
            return 0.012 + 0.014 * top + 0.003 * math.sin(rad(lo) * 13) * math.sin(v * 9)
        if style in ("bowl", "vulcan_f"):
            return 0.0055 + 0.006 * top + 0.002 * (1 - v)
        if style == "bob":
            return 0.007 + 0.012 * top + 0.004 * smooth(60, 120, a)
        if style == "long":
            return 0.006 + 0.012 * top
        if style == "wild":
            return 0.016 + 0.022 * top + 0.005 * math.sin(rad(lo) * 7 + v * 5) + 0.012 * smooth(40, 100, a)
        return 0.005

    z_full = {"long": HEAD_C.z - 0.23, "bob": HEAD_C.z - 0.078, "wild": HEAD_C.z - (0.27 if sex == "m" else 0.30)}.get(style)

    def hang_w(lo):
        a = ang_front(lo)
        if style == "bob":
            return smooth(50, 92, a)
        if style == "long":
            return smooth(78, 112, a)
        if style == "wild":
            return smooth(62, 105, a)
        return 0.0

    def ear_r(z, lo):
        """Hanging hair must pass outside the (round) ears."""
        g = max(smooth(30, 14, abs(adiff(lo, e))) for e in (-9, 189))
        return 0.099 * g * smooth(HEAD_C.z - 0.06, HEAD_C.z - 0.04, z) * smooth(HEAD_C.z + 0.04, HEAD_C.z + 0.025, z)

    nlon, nrow = 128, 46
    lons = [90 + 360 * j / nlon for j in range(nlon)]
    cols, inner = [], []
    for lo in lons:
        la0 = low(lo)
        las = [la0 + (88 - la0) * k / 80 for k in range(81)]
        hp = [bc.head_pt(la, lo, body, vol(lo, (la - la0) / (88 - la0))) for la in las]
        w = hang_w(lo) if z_full is not None else 0.0
        th = rad(lo)
        dirv = V((math.cos(th), math.sin(th), 0))
        skin_in = bc.head_pt(la0 + 1.5, lo, body, 0.0005)
        zb = lerp(hp[0].z, z_full, w) if w > 0 else hp[0].z
        if w > 0.001 and zb < hp[0].z - 0.001:
            # widest point (from the head axis) on the lower half; below it the hair falls straight
            axis = V((0, HEAD_C.y, 0))
            rr = [((p - axis) * V((1, 1, 0))).length for p in hp]
            cand_i = [i for i in range(len(hp)) if las[i] < 25] or [0]
            iw = max(cand_i, key=lambda i: rr[i])
            rw, zt = rr[iw], hp[iw].z
            col = []
            nz = 30
            for k in range(nz + 1):
                z = zb + (zt - zb) * k / nz
                cr, cy = clearance(z, lo, gap)
                fall = (zt - z) / max(0.01, zt - zb)
                wave = 0.004 * math.sin(z * 90 + lo * 0.2) * fall if style == "wild" else 0.0
                r = max(rw * (1 - 0.05 * fall), cr, ear_r(z, lo)) + wave + (0.012 * fall if style == "wild" else 0.0)
                cy_ = lerp(cy, HEAD_C.y, smooth(1.52, 1.62, z))
                col.append(V((dirv.x * r, cy_ + dirv.y * r, z)))
            col += hp[iw + 1:]
            thick = col[0] - dirv * (0.010 if style != "wild" else 0.018)
            inner_pt = skin_in.lerp(thick, smooth(0.0, 0.3, w))
        else:
            col = hp
            inner_pt = skin_in
        # resample by arc length, bottom -> top
        L = [0.0]
        for a, b in zip(col, col[1:]):
            L.append(L[-1] + (b - a).length)
        out = []
        j = 0
        for i in range(nrow + 1):
            s = L[-1] * (i / nrow) ** 0.85
            while j < len(L) - 2 and L[j + 1] < s:
                j += 1
            t = (s - L[j]) / max(1e-9, L[j + 1] - L[j])
            out.append(col[j].lerp(col[j + 1], clamp(t, 0, 1)))
        cols.append(out)
        inner.append(inner_pt)
    rows = [[cols[j][i] for j in range(nlon)] for i in range(nrow + 1)]
    # per-vertex weights: hanging hair follows the neck/chest, the rest the head
    def wts(z):
        h = smooth(1.55, 1.62, z)
        n = (1 - h) * smooth(1.44, 1.54, z)
        return {k: v for k, v in (("Head", h), ("Neck", n), ("UpperChest", 1 - h - n)) if v > 0.005}

    def wfun(i, j):
        return wts(rows[i][j].z)
    idx = bc.sheet(mb, rows, "Hair", None, closed=True, outward=lambda m: (m - V((0, HEAD_C.y, m.z))) + V((0, 0, 0.02)), w=wfun)
    bc.fan(mb, idx[-1], bc.head_pt(90, 90, body, vol(90, 1.0) * 0.98), "Hair", RIG_HEAD, outward=V((0, 0, 1)), w={"Head": 1.0})
    base = []
    for p in inner:
        base.append(mb.v(p, None, wts(p.z)))
    bc.rim(mb, idx[0] + [idx[0][0]], base + [base[0]], "Hair", None, HEAD_C + V((0, 0, 0.12)))
    if style in ("bun", "slick_bun", "vulcan_f"):
        bcn = HEAD_C + V((0, -0.112, 0.045 if style == "bun" else -0.005))
        sz = (0.032, 0.028, 0.03) if style == "bun" else (0.026, 0.024, 0.024)
        bc.ellipsoid(mb, bcn, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), sz, 20, 10, "Hair", RIG_HEAD,
                     deform=lambda la, lo, p: p + (p - bcn) * 0.08 * math.sin(rad(lo) * 6) * math.cos(rad(la)))


def beard(mb, body, kind):
    lons = [32 + 116 * j / 48 for j in range(49)]
    lats = [-88 + 2.0 * i for i in range(40)]

    def keep(i, j):
        la = (lats[i] + lats[i + 1]) / 2
        lo = (lons[j] + lons[j + 1]) / 2
        p = bc.head_local(la, lo, body)
        a = abs(adiff(lo, 90))
        if abs(p.x) < 0.025 and -0.068 < p.z < -0.038 and p.y > 0.03:      # mouth
            return kind == "full" and -0.046 < p.z < -0.040 and abs(p.x) < 0.022  # moustache over the lip
        if p.z > -0.03 and a < 28:                                           # cheeks under the eyes / nose
            return False
        if kind == "tellarite":
            return p.z < 0.012 + 0.03 * smooth(40, 80, a) and a < 92
        return p.z < -0.012 + 0.02 * smooth(45, 80, a) and a < 88

    def vol(la, lo):
        p = bc.head_local(la, lo, body)
        a = abs(adiff(lo, 90))
        v = {"full": 0.0045, "klingon": 0.008, "tellarite": 0.009}[kind]
        if kind == "klingon":
            v += 0.02 * gauss(a, 18) * smooth(-0.07, -0.11, p.z)
        if kind == "tellarite":
            v += 0.004 * math.sin(lo * 0.5) * math.sin(la * 0.4)
        return v
    rows = [[bc.head_pt(la, lo, body, vol(la, lo)) for lo in lons] for la in lats]
    if kind == "klingon":
        for r_, la in zip(rows, lats):
            for j, lo in enumerate(lons):
                a = abs(adiff(lo, 90))
                r_[j] = r_[j] + V((0, 0, -0.022 * gauss(a, 14) * smooth(-60, -84, la)))
    bc.sheet(mb, rows, "Hair", RIG_HEAD, outward=lambda m: m - HEAD_C, keep=keep)


# ---------------------------------------------------------------- outfits (other powers)

_details0 = bc.details
_engineer0 = bc.engineer


def collar(mb, body, mat, h=0.012, out=0.004, flare=0.0, th0=100, th1=440, z=1.505):
    th = [rad(th0 + (th1 - th0) * k / 40) for k in range(41)]
    nl = bc.neck_limb(body)
    r0 = [body.torso(z, t, out) for t in th]
    r1 = [nl.pt(0.62, t, h + flare) + V((0, 0, h * 0.8)) for t in th]
    r2 = [nl.pt(0.62, t, 0.004 + flare * 0.5) + V((0, 0, h * 0.8)) for t in th]
    bc.sheet(mb, [r0, r1], mat, bc.T_CAND, outward=lambda m: (m - V((0, -0.02, m.z))) + V((0, 0, 0.3)))
    bc.sheet(mb, [r1, r2], mat, bc.T_CAND, outward=lambda m: V((0, 0, 1)))


_surface_panel0 = bc.surface_panel


def surface_panel_fine(mb, fn, nfn, us, vs, off, mat, cand, **kw):
    """crew's 5-row shoulder yoke cuts into the (finer) torso here: give it 17 rows."""
    if mat == "Owner" and len(us) == 5 and abs(us[0] - 1.385) < 1e-6:
        us = [1.385 + 0.12 * k / 16 for k in range(17)]
        off = 0.003
    return _surface_panel0(mb, fn, nfn, us, vs, off, mat, cand, **kw)


def details_portrait(mb, role, body):
    f = CUR["faction"]
    if f == "federation":
        bc.surface_panel = surface_panel_fine
        try:
            _details0(mb, role, body)
        finally:
            bc.surface_panel = _surface_panel0
        return
    fn = lambda z, th, e: body.torso(z, th, e)  # noqa: E731
    nf = body.torso_n
    D = rad
    if f == "klingon":
        collar(mb, body, "kl_leather", h=0.03, out=0.008, flare=0.012)
        th = [D(100 + 340 * k / 40) for k in range(41)]
        nl = bc.neck_limb(body)
        edge = [nl.pt(0.62, t, 0.042) + V((0, 0, 0.024)) for t in th]
        edge2 = [nl.pt(0.62, t, 0.034) + V((0, 0, 0.026)) for t in th]
        bc.sheet(mb, [edge, edge2], "kl_red", bc.T_CAND, outward=lambda m: V((0, 0, 1)))
        for sx, c0 in ((1, 0), (-1, 180)):
            for k in range(3):  # layered pauldron plates, top one first
                zs = [1.44 - 0.035 * k + 0.07 * i / 5 for i in range(6)]
                ths = [D(c0 - 48 + 96 * j / 12) for j in range(13)]
                bc.surface_panel(mb, fn, nf, zs, ths, lambda z, t, k=k: 0.016 + 0.006 * (2 - k) + 0.006 * math.cos(t) ** 2,
                                 "kl_metal", bc.T_CAND)
            arm = bc.arm_limb("Right" if sx > 0 else "Left", sx, body)
            for k in range(3):
                u0 = 0.03 + 0.07 * k
                bc.surface_panel(mb, arm.pt, arm.n, [u0, u0 + 0.05, u0 + 0.09], [2 * pi * j / 24 for j in range(25)],
                                 0.012 + 0.003 * (2 - k), "kl_metal", bc.arm_cand("Right" if sx > 0 else "Left"))
        for k in range(4):  # chest plates
            z0 = 1.22 + 0.045 * k
            bc.surface_panel(mb, fn, nf, [z0, z0 + 0.02, z0 + 0.04], [D(62 + 56 * j / 10) for j in range(11)],
                             lambda z, t: 0.008 + 0.003 * math.sin(t) ** 4, "kl_metal" if k % 2 == 0 else "kl_leather", bc.T_CAND)
        bc.ribbon(mb, fn, nf, lambda t: (1.48 - 0.4 * t, D(90)), 14, 0.008, 0.012, "kl_red", bc.T_CAND, depth=0.003)
    elif f == "romulan":
        collar(mb, body, "rom_trim", h=0.016, out=0.005)
        zs = [1.36 + (1.515 - 1.36) * i / 8 for i in range(9)]
        ths = [2 * pi * j / 64 for j in range(64)]
        bc.surface_panel(mb, fn, nf, zs, ths, lambda z, t: 0.006 + 0.022 * abs(math.cos(t)) ** 1.5 * smooth(1.36, 1.44, z)
                         * smooth(1.53, 1.47, z), "rom_pad", bc.T_CAND, closed=True)
        for k in range(6):  # quilting lines over the pads
            zq = 1.385 + 0.022 * k
            for c0 in (0, 180):
                bc.ribbon(mb, fn, nf, lambda t, c0=c0, zq=zq: (zq, D(c0 - 55 + 110 * t)), 16, 0.0025,
                          0.007 + 0.022 * 0.85 * smooth(1.36, 1.44, zq) * smooth(1.53, 1.47, zq), "rom_trim", bc.T_CAND)
        bc.ribbon(mb, fn, nf, lambda t: (1.49 - 0.42 * t, D(62 + 50 * t)), 20, 0.012, 0.004, "rom_trim", bc.T_CAND, depth=0.002)
        for k in range(4):
            t = 0.15 + 0.2 * k
            p = body.torso(1.49 - 0.42 * t, D(62 + 50 * t), 0.008)
            n = body.torso_n(1.49 - 0.42 * t, D(62 + 50 * t))
            bc.tube(mb, p - n * 0.003, p + n * 0.003, 0.004, 0.0035, 10, "rom_metal", bc.T_CAND)
    elif f == "cardassian":
        th = [D(110 + 320 * k / 40) for k in range(41)]
        r0 = [body.torso(1.50, t, 0.006) for t in th]
        r1 = [body.torso(1.535, t, 0.026) for t in th]
        bc.sheet(mb, [r0, r1], "car_armour", bc.T_CAND, outward=lambda m: V((0, 0, 1)))
        for k in range(9):  # chest ribs
            z0 = 1.20 + 0.026 * k
            bc.surface_panel(mb, fn, nf, [z0, z0 + 0.008, z0 + 0.016], [D(45 + 90 * j / 16) for j in range(17)],
                             lambda z, t: 0.008 + 0.004 * math.sin(t) ** 2, "car_armour" if k % 2 else "car_rib", bc.T_CAND)
        for c0 in (0, 180):  # shoulder plates rising toward the neck
            zs = [1.40 + 0.13 * i / 8 for i in range(9)]
            ths = [D(c0 - 60 + 120 * j / 16) for j in range(17)]
            bc.surface_panel(mb, fn, nf, zs, ths, lambda z, t: 0.012 + 0.018 * smooth(1.44, 1.52, z) + 0.006 * math.cos(t) ** 2,
                             "car_armour", bc.T_CAND)
            for k in range(4):
                zr = 1.41 + 0.028 * k
                bc.ribbon(mb, fn, nf, lambda t, c0=c0, zr=zr: (zr, D(c0 - 55 + 110 * t)), 16, 0.006,
                          0.014 + 0.018 * smooth(1.44, 1.52, zr) + 0.004, "car_rib", bc.T_CAND, depth=0.003)
    elif f == "ferengi":
        collar(mb, body, "fer_trim", h=0.05, out=0.012, flare=0.03, th0=120, th1=420)
        bc.surface_panel(mb, fn, nf, [1.08 + 0.42 * i / 12 for i in range(13)], [D(80 + 20 * j / 4) for j in range(5)],
                         0.003, "fer_shirt", bc.T_CAND)
        for sx in (1, -1):  # lapels
            bc.surface_panel(mb, fn, nf, [1.30 + 0.2 * i / 8 for i in range(9)],
                             [D(90 + sx * (10 + 18 * j / 6)) for j in range(7)],
                             lambda z, t: 0.006 + 0.004 * smooth(1.30, 1.48, z), "fer_trim" if sx > 0 else "fer_trim", bc.T_CAND)
        for k in range(3):
            p = body.torso(1.12 + 0.07 * k, D(90), 0.008)
            n = body.torso_n(1.12 + 0.07 * k, D(90))
            bc.tube(mb, p - n * 0.003, p + n * 0.004, 0.006, 0.005, 12, "fer_trim", bc.T_CAND)


OUTFIT_PAL = {
    "federation": dict(suit="eng_suit", suit2="eng_suit2", boot="boot", glove=None, accent="gold"),
    "klingon": dict(suit="kl_leather", suit2="kl_leather", boot="boot", glove=None, accent="kl_red"),
    "romulan": dict(suit="rom_cloth", suit2="rom_cloth", boot="boot", glove=None, accent="rom_trim"),
    "cardassian": dict(suit="car_under", suit2="car_under", boot="boot", glove=None, accent="car_rib"),
    "ferengi": dict(suit="fer_jacket", suit2="fer_shirt", boot="boot", glove=None, accent="fer_trim"),
}


# ---------------------------------------------------------------- one character

SPECIES = {
    "human": dict(ears="human", brow="natural"),
    "vulcan": dict(ears="pointed", brow="upswept", nose=(0.92, 1.08)),
    "andorian": dict(ears="human", brow="natural", lip=(0.82, 0.72, 0.92)),
    "tellarite": dict(ears="human", brow="bushy", eye_r=0.0098, lids=(14, -24)),
    "bajoran": dict(ears="human", brow="natural", ridges=(0.0125, 0.0080, 0.0035, -0.001)),
    "trill": dict(ears="human", brow="natural"),
    "betazoid": dict(ears="human", brow="natural"),
    "klingon": dict(ears="human", brow="bushy", nose=(1.35, 1.12), lids=(22, -28)),
    "romulan": dict(ears="pointed", brow="upswept", nose=(0.95, 1.05)),
    "cardassian": dict(ears="human", brow="thin", nose=(0.95, 1.0), lip=(0.78, 0.70, 0.72)),
    "ferengi": dict(ears="ferengi", brow="thin", nose=(1.25, 1.3), lids=(22, -26),
                    ridges=(0.012, 0.007, 0.002, -0.003, -0.008, -0.013, -0.018)),
}


def build_character(c, loc=V((0, 0, 0)), yaw=0.0, head_turn=0.0, cam=None, t_idle=0.15, height=1.0):
    sp = c["species"]
    S = SPECIES[sp]
    CUR.clear()
    CUR.update(c)
    CUR["faction"] = FACTION_OF[sp]
    c["faction"] = CUR["faction"]
    CUR["pointed"] = S["ears"] == "pointed"
    CUR["lip"] = S.get("lip", (0.80, 0.58, 0.58) if SK[c["skin"]][0] > 0.6 else (0.78, 0.66, 0.66))
    CUR["hair_gap"] = {"klingon": 0.04, "romulan": 0.045, "cardassian": 0.045, "ferengi": 0.07}.get(CUR["faction"], 0.022)
    fem = 1.0 if c["sex"] == "f" else 0.0
    body = bc.Body(fem, c.get("heavy", 0.0), c.get("slim", 0.0))
    CUR["body"] = body
    bc._FACE_Y.clear()
    char_materials(c)
    # the body: build_crew's engineer figure, harness off, bare hands, this faction's outfit
    pal0 = bc.ROLE_PAL["engineer"]
    bc.ROLE_PAL["engineer"] = OUTFIT_PAL[CUR["faction"]]
    bc.engineer = lambda mb, body: None
    bc.details = details_portrait
    bc.head_mesh = lambda *a, **k: None
    bc.face_features = lambda *a, **k: None
    try:
        mb = bc.figure("engineer", "hero", {"fem": fem, "heavy": c.get("heavy", 0.0), "slim": c.get("slim", 0.0)})
    finally:
        bc.ROLE_PAL["engineer"] = pal0
        bc.engineer = _engineer0
        bc.details = _details0
    hm = bc.MB()
    head_mesh_hd(hm, body)
    if sp == "tellarite":
        nose(hm, body, 1.6, 0.55, top=0.017)
        snout(hm, body)
    else:
        ws, ps = S.get("nose", (1.0, 1.0))
        nose(hm, body, ws, ps, ridges=S.get("ridges", ()), ridge_amp=0.0013 if sp == "bajoran" else 0.0011,
             top=0.017)
    if sp != "ferengi":
        mouth_line(hm, body)
    eye_r = S.get("eye_r", 0.0118)
    ue, le = S.get("lids", (23, -27))
    for sx in (-1, 1):
        lids(hm, body, sx, eye_r, ue, le)
    if S["brow"]:
        brows(hm, body, S["brow"], 1.25 if c["sex"] == "m" else 0.95)
    ear_kind = S["ears"]
    es = ears(hm, body, ear_kind, 1.0 if c["sex"] == "m" else 0.82)
    xm = bc.MB()
    if sp == "bajoran":
        bajoran_earring(xm, es[1])
    if sp == "ferengi":
        ferengi_teeth(xm, body)
    if sp == "andorian":
        antennae(xm, body)
    if sp == "trill":
        trill_spots(xm, body)
    if sp == "cardassian":
        neck_ridges(hm, body)
    hairm = bc.MB()
    hair(hairm, body, c.get("style"))
    if c.get("beard"):
        beard(hairm, body, c["beard"])
    # rig + pose
    rig = bc.make_armature("Rig_" + c["id"])
    objs = [rig]
    objs.append(make_obj("Body_" + c["id"], mb, rig, tint=False))
    objs.append(make_obj("Head_" + c["id"], hm, rig, tint=True))
    if xm.co:
        objs.append(make_obj("Extra_" + c["id"], xm, rig))
    if hairm.co:
        objs.append(make_obj("Hair_" + c["id"], hairm, rig))
    Sol = bc.Solver(rig)
    spec = bc.pose_idle(t_idle)
    spec["rot"]["Head"] = bc.Rz(head_turn) @ bc.Rx(-2.0)
    spec["rot"]["Neck"] = bc.Rz(head_turn * 0.5)
    bc.apply_pose(rig, Sol, spec)
    rig.location = loc
    rig.rotation_euler = (0, 0, rad(yaw))
    rig.scale = (height,) * 3
    bpy.context.view_layer.update()
    # eyes, turned toward the camera
    pb = rig.pose.bones["Head"]
    Dh = rig.matrix_world @ pb.matrix @ rig.data.bones["Head"].matrix_local.inverted()
    em = bc.MB()
    for sx in (-1, 1):
        ce, r = eye_centre(sx, body, eye_r)
        g = (Dh.to_3x3().inverted() @ ((cam if cam is not None else V((0, 3, 1.6))) - Dh @ ce)).normalized()
        e1 = orth(V((0, 0, 1)), g)
        e2 = g.cross(e1)
        iris_la = 48 if sp == "betazoid" else 58
        pupil_la = 80

        def matf(la, lo, iris_la=iris_la):
            if la > pupil_la:
                return "pupil"
            if la > iris_la + 2.5:
                return "iris"
            if la > iris_la:
                return "limbus"
            return "eyewhite_hd"
        bc.ellipsoid(em, ce, (e1, e2, g), (r, r, r), 48, 30, "eyewhite_hd", RIG_HEAD, matf=matf)
        bc.ellipsoid(em, ce + g * 0.0004, (e1, e2, g), (r * 1.02,) * 3, 32, 16, "Cornea", RIG_HEAD,
                     deform=lambda la, lo, p, ce=ce, g=g, r=r: p + g * 0.0012 * smooth(45, 85, la))
    objs.append(make_obj("Eyes_" + c["id"], em, rig))
    head_pos = Dh @ (HEAD_C + V((0, 0.03, 0.0)))
    return objs, head_pos


def remove(objs):
    for o in objs:
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Armature):
                bpy.data.armatures.remove(data)


# ---------------------------------------------------------------- backdrop, lights, camera

def backdrop_image(faction, w=768, h=768, element=True, side=1):
    """Dark gradient with a soft glow and a faint faction element, as a float RGB image (linear)."""
    col = np.array(srgb_lin(FACTION_COLOUR[faction]))
    v, u = np.mgrid[1:-1:h * 1j, -1:1:w * 1j]
    base = 0.010 + 0.016 * (v + 1) / 2
    glow = np.exp(-((u + 0.1 * side) ** 2 + (v - 0.2) ** 2) / 0.6)
    img = base[..., None] * np.array([0.9, 0.95, 1.1]) + glow[..., None] * col * 0.07
    m = np.zeros_like(u)
    cx, cy = 0.5 * side, 0.05
    if element:
        if faction == "federation":
            for R, wdt in ((0.78, 0.012), (0.58, 0.007)):
                x, y = (u - cx), (v - cy)
                a = np.arctan2(y, x)
                rr = np.hypot(x, y)
                sector = np.mod(a + pi / 6, pi / 3) - pi / 6
                d = rr * np.cos(sector) - R * math.cos(pi / 6)
                m += np.exp(-(d / wdt) ** 2)
        elif faction == "klingon":
            for k in range(3):
                x0 = cx - 0.35 + 0.28 * k
                d = np.abs((u - x0) * 0.8 + (v - cy) * 0.6) - 0.0
                along = (u - x0) * -0.6 + (v - cy) * 0.8
                taper = np.clip(1 - np.abs(along) / (0.7 - 0.12 * k), 0, 1)
                m += np.exp(-(d / (0.02 * taper + 1e-4)) ** 2) * taper
        elif faction == "romulan":
            x, y = (u - cx - 0.2), (v - cy + 0.6)
            rr = np.hypot(x, y)
            for R in (0.6, 0.8, 1.0):
                m += np.exp(-((rr - R) / 0.008) ** 2) * (y > -0.1)
        elif faction == "cardassian":
            for k in range(6):
                y0 = cy + 0.45 - 0.17 * k
                inb = (np.abs(v - y0) < 0.035) & (u * side > 0.15 + 0.06 * abs(k - 2.5)) & (u * side < 0.95)
                m += inb * 0.8
        elif faction == "ferengi":
            for k in range(12):
                a = 2 * pi * k / 12
                x0, y0 = cx + 0.55 * math.cos(a), cy + 0.55 * math.sin(a)
                rr = np.hypot(u - x0, v - y0)
                m += np.exp(-((rr - 0.07) / 0.008) ** 2)
            rr = np.hypot(u - cx, v - cy)
            m += np.exp(-((rr - 0.36) / 0.01) ** 2)
        m = np.clip(m, 0, 1)
        # soften
        for _ in range(3):
            m = (m + np.roll(m, 1, 0) + np.roll(m, -1, 0) + np.roll(m, 1, 1) + np.roll(m, -1, 1)) / 5
        img = img + m[..., None] * col * 0.05
    vig = 1 - 0.45 * np.clip(np.hypot(u, v) - 0.6, 0, 1)
    img = img * vig[..., None]
    return img.astype(np.float32)


def backdrop_material(name, img):
    h, w, _ = img.shape
    im = bpy.data.images.new(name, w, h, alpha=False, float_buffer=True)
    rgba = np.concatenate([img[::-1], np.ones((h, w, 1), np.float32)], axis=2)
    im.pixels.foreach_set(rgba.ravel())
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    out = nt.nodes["Material Output"]
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    tx = node(nt, "ShaderNodeTexImage")
    tx.image = im
    em = node(nt, "ShaderNodeEmission")
    em.inputs["Strength"].default_value = 1.0
    nt.links.new(tx.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return m


def setup_scene():
    scene = bpy.context.scene
    world = bpy.data.worlds.new("Dark")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.02, 0.024, 0.03, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast" if "AgX - Medium High Contrast" in [
        i.identifier for i in scene.view_settings.bl_rna.properties["look"].enum_items] else "None"
    scene.view_settings.exposure = 0.0
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.cycles.transparent_max_bounces = 12
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.clip_start = 0.05
    cam.data.clip_end = 100
    return cam


def light(name, kind, energy, size, loc, target, colour=(1, 1, 1)):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = energy
    ld.color = colour
    if kind == "AREA":
        ld.size = size
        ld.shape = "DISK"
    ob = bpy.data.objects.new(name, ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (V(target) - V(loc)).to_track_quat("-Z", "Y").to_euler()
    return ob


def plane(name, c, w, h, mat, normal_to):
    me = bpy.data.meshes.new(name)
    me.from_pydata([(-w / 2, -h / 2, 0), (w / 2, -h / 2, 0), (w / 2, h / 2, 0), (-w / 2, h / 2, 0)], [], [(0, 1, 2, 3)])
    me.uv_layers.new(name="UVMap")
    uv = me.uv_layers[0].data
    for i, (a, b) in enumerate(((0, 0), (1, 0), (1, 1), (0, 1))):
        uv[i].uv = (a, b)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = c
    ob.rotation_euler = (V(normal_to) - V(c)).to_track_quat("Z", "Y").to_euler()
    return ob


def aim(cam, pos, target, lens):
    cam.location = pos
    cam.rotation_euler = (V(target) - V(pos)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens


def render(path, w, h):
    scene = bpy.context.scene
    scene.render.resolution_x = w
    scene.render.resolution_y = h
    scene.render.resolution_percentage = 100
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("rendered", path, os.path.getsize(path), flush=True)


# ---------------------------------------------------------------- shots

YAW = 24.0       # body turned to the camera's right (the figure's left shoulder toward the lens)
CAM_DIST = 1.18
ORBIT = [float(x) for x in (arg("--orbit") or "").split(",") if x]  # debug: extra views round the head (deg)


def portrait(cam, c, extras):
    yaw = YAW
    camp = V((0.0, CAM_DIST, 1.62))
    objs, head = build_character(c, yaw=yaw, head_turn=-4.0, cam=camp)
    target = head + V((0, -0.03, -0.10))
    camp = V((target.x + 0.0, target.y + CAM_DIST, target.z + 0.06))
    # eyes were aimed at the first guess; the camera only moves by centimetres
    aim(cam, camp, target, 85)
    cam.data.dof.use_dof = True
    cam.data.dof.focus_distance = (camp - head).length - 0.04
    cam.data.dof.aperture_fstop = 3.5
    f = c["faction"]
    bd = plane("Backdrop", target + V((0, -1.5, 0.08)), 2.2, 2.2, extras["bd"][f], camp)
    path = os.path.join(OUT, c["id"] + ".jpg")
    render(path, RES, RES)
    for a in ORBIT:
        hc = head + V((0, -0.03, 0))
        d = V((math.sin(rad(a)), math.cos(rad(a)), 0)) * 0.55
        aim(cam, hc + d + V((0, 0, 0.01)), hc, 85)
        cam.data.dof.use_dof = False
        render(os.path.join(OUT, f"{c['id']}_orbit{int(a)}.jpg"), RES, RES)
    remove(objs + [bd])
    return path


def lineup(cam, chars, extras):
    objs_all = []
    xs = [-1.35, -0.45, 0.45, 1.35]
    heights = [1.03, 0.98, 1.0, 0.96]
    camp = V((2.2, 6.6, 1.55))
    for k, c in enumerate(chars):
        objs, _ = build_character(c, loc=V((xs[k], 0.15 * (k % 2), 0)), yaw=28 + 4 * k, head_turn=-6, cam=camp,
                                  t_idle=0.1 + 0.2 * k, height=heights[k])
        objs_all += objs
    floor_mat = bpy.data.materials.new("Floor")
    floor_mat.use_nodes = True
    p = floor_mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (0.03, 0.035, 0.045, 1)
    p.inputs["Roughness"].default_value = 0.35
    me = bpy.data.meshes.new("Floor")
    me.from_pydata([(-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0)], [], [(0, 1, 2, 3)])
    me.materials.append(floor_mat)
    fl = bpy.data.objects.new("Floor", me)
    bpy.context.scene.collection.objects.link(fl)
    bd = plane("BackdropL", V((-1.2, -3.2, 2.6)), 14, 7, extras["bd_wide"], camp)
    aim(cam, camp, V((0.0, 0.0, 0.95)), 50)
    cam.data.dof.use_dof = False
    render(os.path.join(OUT, "lineup.jpg"), 1600, 900)
    remove(objs_all + [fl, bd])


def look(seed, species, sex=None):
    """Seed -> look (the future generator): sex, build, skin, hair colour/style, beard, iris, age band."""
    h = lambda k: bc.h32(seed, k)  # noqa: E731
    sex = sex or ("f" if h(0) < 0.5 else "m")
    pal = {
        "andorian": (["andorian"], ["white", "silver"]), "cardassian": (["cardassian"], ["black", "darkbrown"]),
        "ferengi": (["ferengi"], [None]), "tellarite": (["tellarite"], ["brown", "auburn", "darkbrown", "grey"]),
        "klingon": (["klingon", "brown", "dark", "tan"], ["black", "darkbrown", "brown", "grey"]),
        "romulan": (["romulan", "tan", "light"], ["black", "darkbrown"]),
        "vulcan": (["light", "olive", "tan", "brown"], ["black", "darkbrown"]),
    }.get(species, (list(SK)[:8], ["black", "darkbrown", "brown", "auburn", "blond", "ash", "grey"]))
    skins, hairs = pal
    styles = {"vulcan": ["bowl"] if sex == "m" else ["vulcan_f", "bowl"], "romulan": ["bowl"], "klingon": ["wild"],
              "cardassian": ["slick"] if sex == "m" else ["slick_bun"], "ferengi": [None], "tellarite": ["shaggy"],
              "andorian": ["swept", "short"] if sex == "m" else ["bob", "bun"]}.get(
        species, ["crop", "short", "swept", "curly"] if sex == "m" else ["bun", "bob", "long", "curly", "short"])
    irises = ["black"] if species == "betazoid" else ["brown", "darkbrown", "hazel", "green", "blue", "grey"]
    age = ["25-35", "35-45", "45-55", "55-65"][int(h(7) * 4) % 4]
    hair = hairs[int(h(3) * len(hairs)) % len(hairs)]
    if age == "55-65" and hair not in (None, "white", "silver") and h(8) < 0.6:
        hair = "grey"
    beard_kind = {"klingon": "klingon", "tellarite": "tellarite"}.get(species, "full")
    beard_on = sex == "m" and species not in ("vulcan", "romulan", "ferengi", "cardassian", "andorian") and (
        h(5) < 0.3 or species in ("klingon", "tellarite"))
    return dict(id=f"{species}_{sex}_seed{seed}", species=species, sex=sex, dept=list(DEPTS)[int(h(9) * 4) % 4]
                if FACTION_OF[species] == "federation" else None, age=age,
                skin=skins[int(h(2) * len(skins)) % len(skins)], hair=hair,
                style=styles[int(h(4) * len(styles)) % len(styles)], iris=irises[int(h(6) * len(irises)) % len(irises)],
                heavy=0.6 * h(10) if h(11) > 0.6 else 0.0, slim=0.5 * h(10) if h(11) < 0.25 else 0.0,
                beard=beard_kind if beard_on else None)


def write_json(cast):
    entries = []
    for c in cast:
        f = c["id"] + ".jpg"
        if not os.path.exists(os.path.join(OUT, f)):
            continue
        entries.append(dict(file=f, species=c["species"], faction=FACTION_OF[c["species"]], sex=c["sex"],
                            department=c.get("dept"), department_colour=DEPTS.get(c.get("dept")) if c.get("dept") else None,
                            skin=c["skin"], skin_rgb=[round(x, 3) for x in SK[c["skin"]]], hair=c.get("hair") or "none (bald)",
                            hair_style=c.get("style") or "bald", beard=c.get("beard"), eyes=c["iris"], age_band=c["age"],
                            outfit="colony-service jumpsuit, department yoke" if FACTION_OF[c["species"]] == "federation"
                            else {"klingon": "dark leather, metal pauldrons and chest plates",
                                  "romulan": "grey-green tunic, quilted padded shoulders",
                                  "cardassian": "grey-brown ribbed armour, raised shoulder plates",
                                  "ferengi": "patterned jacket, gold flared collar"}[FACTION_OF[c["species"]]]))
    doc = dict(generator="blender/build_portraits.py", note="Mockup portraits (original designs; no canon uniforms or insignia).",
               size=[RES, RES], departments=DEPTS, lineup=dict(file="lineup.jpg", characters=LINEUP) if os.path.exists(
                   os.path.join(OUT, "lineup.jpg")) else None, portraits=entries)
    with open(os.path.join(OUT, "portraits.json"), "w") as fh:
        json.dump(doc, fh, indent=1)
    print("json", len(entries), flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bc.palette()
    bc.skeleton()
    os.makedirs(OUT, exist_ok=True)
    cam = setup_scene()
    # lights: soft key from camera-left, rim from behind-right, weak fill
    light("Key", "AREA", 80, 0.9, (-1.25, 0.95, 2.15), (0, 0, 1.64), (1.0, 0.95, 0.88))
    light("Rim", "AREA", 90, 0.5, (0.9, -0.8, 1.95), (0, 0, 1.64), (0.82, 0.9, 1.0))
    light("Rim2", "AREA", 40, 0.5, (-0.9, -0.85, 1.9), (0, 0, 1.62), (0.9, 0.92, 1.0))
    light("Fill", "AREA", 9, 2.0, (1.3, 1.5, 1.4), (0, 0, 1.6), (0.88, 0.93, 1.0))
    extras = {"bd": {f: backdrop_material("BD_" + f, backdrop_image(f)) for f in FACTION_COLOUR}}
    extras["bd_wide"] = backdrop_material("BD_wide", backdrop_image("federation", 1600, 800, side=1))
    cast = [c for c in CAST if (not ONLY_SPECIES or c["species"] in ONLY_SPECIES)
            and (not ONLY_SEX or c["sex"] == ONLY_SEX) and (not ONLY or c["id"] in ONLY)]
    if "portraits" in SHOTS:
        for c in cast:
            portrait(cam, c, extras)
        if SEED is not None:
            for sp in (ONLY_SPECIES or SPECIES_ORDER):
                portrait(cam, look(int(SEED), sp, ONLY_SEX), extras)
    write_json(CAST)
    if "lineup" in SHOTS:
        lineup(cam, [next(c for c in CAST if c["id"] == i) for i in LINEUP], extras)
        write_json(CAST)


if __name__ == "__main__":
    main()
