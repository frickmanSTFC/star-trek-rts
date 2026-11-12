"""Character portraits for the comm screen and character creation (owner 2026-11-11: mockups only for now;
"we will need many different models of people"; 2026-11-12: "make the faces more varied. They look like dolls, I
would prefer something more realistic cartoony").

Head-and-shoulders busts rendered in Cycles, built on the colony crew figures of build_crew.py (imported as a
module, never changed: its body, rig, jumpsuit and Idle pose are reused as they are; only this script's own process
patches a few of its module globals, see "How it is built" below).

Look: STYLISED REALISM (modern stylised 3D games / animation adults: believable anatomy, skin and hair with gentle
exaggeration), not smooth mannequins and not photoreal.

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
key + rim + weak fill (+ a faint eye light near the lens for the catchlight), dark gradient backdrop with a faint
element in the faction colour (Federation: two hexagon outlines; Klingon: angular slashes; Romulan: arcs; Cardassian:
stepped bars; Ferengi: a ring of discs), shallow depth of field, Cycles 64 samples, denoised, JPEG quality 86.

Run (bpy 5.x venv; ~25-35 min for everything on 4 cores, hair strands cost the most):
  /home/user/.bpyvenv/bin/python blender/build_portraits.py
  /home/user/.bpyvenv/bin/python blender/build_portraits.py --only human_f_defense,vulcan_m_exploration --samples 24
  python3 blender/build_portraits.py --sheet docs/mockups/character/portraits
Flags: --species <id>[,<id>] (human, vulcan, andorian, tellarite, bajoran, trill, betazoid, klingon, romulan,
cardassian, ferengi), --sex f|m, --res 640, --samples 64, --out DIR, --shots portraits,lineup, --only <file stem>
[,<stem>], --sheet DIR (contact sheet only, no bpy), --seed N[,N...] (also render look(N) for each picked species:
the generator's random person, <species>_<sex>_seedN.jpg, not listed in portraits.json), --orbit A[,B] (debug: extra
unframed views round the head from these angles, <id>_orbitA.jpg), --no-hair (debug: skip strands, fast).

BEFORE / AFTER (2026-11-12 pass, owner: "they look like dolls"):
  before  one shared base skull (build_crew's sculpted ellipsoid + small gaussian tweaks), so everyone looked related;
          smooth and symmetric; flat plastic skin (one colour, weak SSS); a stuck-on lofted nose; painted eyes with
          no lashes; eyebrows as painted ribbons; hair as one helmet shell with grooves; Vulcan/Romulan brows angry;
          Romulan man and woman the same; Cardassians pale and statue-like; Ferengi ears round flaps, no teeth.
  after   * Faces: a new parametric head (numpy, head_np) driven by ~70 numbers from face_params(seed, species, sex,
            age, overrides): skull width/length/height, crown, forehead height and slope, temples, brow ridge,
            cheekbone height/width/prominence, malar fat, buccal hollows, jaw width/angle/flare, chin height/width/
            projection/cleft, face length, eye spacing/height/size/tilt/hooding, nose built into the skin (types
            straight, aquiline, roman, snub, button, broad, flat, hooked + Tellarite snout: bridge height and hump,
            tip rotation/bulb, alar width with a real alar crease, columella and nostrils), lips (width, upper/lower
            fullness, cupid's bow, vermilion border, philtrum, mentolabial sulcus, mouth corners), ears (size, height,
            protrusion), neck thickness, face fat. Six face shapes (oval, round, square, long, heart, diamond) and
            sex-specific structure (brow, jaw, chin, nose size, lip fullness, cheekbones) before per-person noise;
            small asymmetry (eyes, brows, mouth corners, nose deviation, ears).
          * Age 25-65: nasolabial folds, tear troughs, under-eye bags, jowls, marionette lines, forehead and frown lines,
            crow's feet, thinner lips, longer nose/ears, receding hairline (men), greying strands, age spots.
          * Expressions: smile (corners up, cheeks up, lower lids squint, folds deepen), smirk (one side), brow raise /
            calm; Vulcans/Romulans calm and level (no frown), Ferengi grin with parted lips and pointed teeth.
          * Skin: random-walk (skin) subsurface scattering, per-vertex colour zones (cheek/nose/ear warmth, lips,
            darker lids and under-eyes, beard shadow on men, scalp tinted under the hair), procedural mottling,
            freckles on some, age spots on older people, T-zone shine (roughness map), pore bump at portrait scale,
            a little peach-fuzz sheen. Species skins keep their identity (Andorian blue with a violet flush, Cardassian
            warm grey with ochre undertone in the ridges, Klingon ridges with real depth, Tellarite ruddy).
          * Eyes: recessed iris under a clear cornea bulge (fresnel gloss: the key light makes the catchlight, plus a
            weak eye light), procedural iris (radial fibres, collarette, dark limbal ring, two-tone), sclera with warm
            corners, a pink caruncle, a wet line on the lower lid, real lid margins with an upper-lid fold; upper and
            lower lashes as hair strands; eyebrows as ~250-400 hair strands each with a growth pattern (medial hairs up,
            body along the brow, upper and lower rows converging), per-person shape (arch, thickness, tail).
          * Hair: Cycles hair curves (Principled Hair BSDF, melanin + per-strand variation, greying fraction) grown from
            the scalp with guides + clumped children, a soft feathered hairline, per-style comb fields (whorl, part,
            swept back, tied), volume layers, waves/curls/coils, frizz, blunt cuts, buns and ponytails, collision with
            head, ears and shoulders. Styles: crop, fade, textured, side_part, swept, swept_back, slick, bowl, vulcan_f,
            romulan_f, pixie, undercut, bob, long, curly_long, ponytail, bun, slick_bun, coils, shaggy, wild. Beards as
            strands too (stubble, short, full, Klingon, Tellarite).
          * Species: Ferengi ears are big scalloped shells with concentric ridges and a lobed rim, pointed teeth show in
            a grin; Cardassian spoon, brow-temple ridges and cobra neck ridges are deeper and warmer; Klingon crest
            ridges deeper; Romulan woman has her own cut (long sleek bob, angled fringe) and outfit (high collar, sash)
            vs the man's bowl cut and quilted shoulders.
          * Lineup: a curved cyclorama sweep (no hard horizon), darker floor with soft light pools.

How it is built:
  * build_crew.py is loaded with importlib (with a clean sys.argv, so its own flags stay at their defaults). Its
    palette, skeleton, Body, torso/limb/hand/boot meshes, jumpsuit details and Idle pose are used unchanged; the role
    used is "engineer" with its harness/tool belt switched off, bare hands, finer mesh resolution (TORSO_SEGS,
    LIMB_SEGS, BAND_T, BAND_L, TORSO_Z rows, a per-person neck_limb, a 17-row shoulder yoke; all patched in this
    process only, build_crew.py and its --game export are untouched). Other powers swap bc.details for their outfit.
  * bc.head_local / bc.face_y are replaced (this process only) by the numpy head (head_np) and a front height map of it,
    so everything that samples the head (antennae, Trill spots, neck ridges) follows the new shape. The head itself is
    one dense grid mesh (300 longitudes x ~330 rows, finest over the face) with vertex attributes tint/skm/rest.
  * Eyes are built after posing (turned to look into the lens); lids, ears, lashes, brows, beard and hair are built in
    the rest pose and carried by the Head bone (strands: transformed by the posed head matrix, then pushed out of the
    posed body with a BVH so long hair rests on the shoulders).

Toward the game's portrait / comm-screen generator (plan only, nothing exported yet):
  1. look(seed, species, sex) maps a seed to a whole person with build_crew's h32 hash (skin, hair colour and style,
     beard, iris, build, age) and face_params(seed, ...) to the face; the same hash in C# (CrewModels.Hash) would give
     every crew member, rival captain and contact a stable face from an id. --seed renders such people.
  2. Export one rigged bust glb per species with the face numbers as blend shapes (or bake a few hundred variants),
     visemes and blinks; hair as cards baked from these strands; outfits as swappable meshes.
  3. Godot renders the bust into a SubViewport on the view screen and caches a still per character for lists; the
     backdrop and framing here are the target look.
"""
import json
import math
import zlib
import os
import sys
import warnings

warnings.filterwarnings("ignore", category=DeprecationWarning)

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

# skin albedo presets (sRGB) and the subsurface radius colour for each
SK = dict(fair=(0.93, 0.76, 0.66), light=(0.88, 0.68, 0.56), tan=(0.78, 0.57, 0.43), olive=(0.73, 0.56, 0.41),
          brown=(0.55, 0.38, 0.27), dark=(0.39, 0.26, 0.19), deep=(0.28, 0.185, 0.14), eastasian=(0.89, 0.72, 0.57),
          andorian=(0.44, 0.60, 0.86), cardassian=(0.60, 0.55, 0.48), ferengi=(0.88, 0.67, 0.50),
          tellarite=(0.74, 0.53, 0.43), klingon=(0.52, 0.34, 0.24), klingon2=(0.40, 0.26, 0.19),
          romulan=(0.86, 0.71, 0.59))
SSS_RADIUS = {"andorian": (0.55, 0.65, 1.0), "cardassian": (0.9, 0.55, 0.35), "default": (1.0, 0.36, 0.2)}
# hair: (melanin, melanin redness, tint sRGB)
HC = dict(black=(1.0, 0.05, None), darkbrown=(0.93, 0.12, None), brown=(0.82, 0.22, None),
          chestnut=(0.76, 0.45, None), auburn=(0.62, 0.9, None), copper=(0.45, 1.0, None),
          blond=(0.30, 0.32, None), ash=(0.48, 0.06, (0.86, 0.86, 0.88)), grey=(0.50, 0.02, (0.9, 0.9, 0.92)),
          white=(0.0, 0.0, (0.92, 0.95, 1.0)), silver=(0.06, 0.0, (0.92, 0.93, 0.97)))
IR = dict(brown=((0.36, 0.20, 0.08), (0.20, 0.10, 0.04)), darkbrown=((0.20, 0.11, 0.05), (0.12, 0.07, 0.035)),
          hazel=((0.45, 0.38, 0.16), (0.42, 0.24, 0.08)), green=((0.30, 0.46, 0.27), (0.42, 0.36, 0.14)),
          blue=((0.30, 0.50, 0.72), (0.40, 0.55, 0.62)), grey=((0.50, 0.55, 0.60), (0.46, 0.44, 0.34)),
          black=((0.035, 0.03, 0.03), (0.05, 0.04, 0.04)), violet=((0.42, 0.33, 0.62), (0.55, 0.40, 0.55)),
          amber=((0.60, 0.40, 0.12), (0.45, 0.25, 0.06)))

# id, species, sex, dept, age band (json), years (apparent), skin, hair, style, iris, build (heavy/slim), beard and
# face overrides (face shape, nose type, smile, smirk, freckles, grey, part ...); everything else comes from the seed
CAST = [
    dict(id="human_f_exploration", species="human", sex="f", dept="exploration", age="30-40", years=33, skin="tan",
         hair="darkbrown", style="ponytail", iris="brown", face="heart", nose="straight", smile=0.35),
    dict(id="human_m_defense", species="human", sex="m", dept="defense", age="40-50", years=47, skin="light", hair="brown",
         grey=0.3, style="side_part", iris="blue", heavy=0.35, face="square", nose="roman", beard="stubble", smile=0.12),
    dict(id="vulcan_f_archaeology", species="vulcan", sex="f", dept="archaeology", age="40-60 (looks 30s)", years=33,
         skin="olive", hair="black", style="vulcan_f", iris="darkbrown", face="oval", nose="straight", smile=0.0),
    dict(id="vulcan_m_exploration", species="vulcan", sex="m", dept="exploration", age="60-90 (looks 40s)", years=46,
         skin="light", hair="darkbrown", style="bowl", iris="brown", slim=0.4, face="long", nose="aquiline", smile=0.0),
    dict(id="andorian_f_defense", species="andorian", sex="f", dept="defense", age="25-35", years=27, skin="andorian",
         hair="white", style="pixie", iris="violet", face="diamond", nose="button", smile=0.3),
    dict(id="andorian_m_maintenance", species="andorian", sex="m", dept="maintenance", age="30-40", years=37,
         skin="andorian", hair="white", style="swept_back", iris="grey", heavy=0.3, face="square", nose="broad",
         smile=0.1),
    dict(id="tellarite_f_maintenance", species="tellarite", sex="f", dept="maintenance", age="30-40", years=36,
         skin="tellarite", hair="auburn", style="shaggy", iris="darkbrown", heavy=0.5, face="round", smile=0.25),
    dict(id="tellarite_m_defense", species="tellarite", sex="m", dept="defense", age="40-50", years=47,
         skin="tellarite", hair="brown", grey=0.15, style="shaggy", iris="darkbrown", heavy=0.8, beard="tellarite",
         face="square", smile=0.0),
    dict(id="bajoran_f_archaeology", species="bajoran", sex="f", dept="archaeology", age="25-35", years=27, skin="light",
         hair="auburn", style="bob", iris="hazel", face="heart", nose="snub", freckles=0.8, smile=0.3),
    dict(id="bajoran_m_exploration", species="bajoran", sex="m", dept="exploration", age="35-45", years=41,
         skin="brown", hair="black", style="fade", iris="darkbrown", face="long", nose="straight", beard="short",
         smile=0.1),
    dict(id="trill_f_exploration", species="trill", sex="f", dept="exploration", age="25-35", years=26, skin="fair",
         hair="blond", style="long", iris="green", face="oval", nose="button", freckles=0.35, smile=0.4),
    dict(id="trill_m_archaeology", species="trill", sex="m", dept="archaeology", age="35-45", years=39, skin="light",
         hair="chestnut", style="swept", iris="blue", face="diamond", nose="hooked", smile=0.2),
    dict(id="betazoid_f_defense", species="betazoid", sex="f", dept="defense", age="25-35", years=29, skin="olive",
         hair="black", style="curly_long", iris="black", face="round", nose="straight", smile=0.25),
    dict(id="betazoid_m_maintenance", species="betazoid", sex="m", dept="maintenance", age="30-40", years=34, skin="tan",
         hair="black", style="textured", iris="black", face="oval", nose="straight", beard="short", smile=0.3),
    # four extra Humans, one per department
    dict(id="human_m_exploration", species="human", sex="m", dept="exploration", age="55-60", years=58, skin="deep",
         hair="black", grey=0.85, style="crop", iris="darkbrown", heavy=0.2, beard="full", face="round", nose="broad",
         smile=0.3),
    dict(id="human_f_defense", species="human", sex="f", dept="defense", age="25-30", years=26, skin="eastasian",
         hair="black", style="undercut", iris="darkbrown", slim=0.3, face="diamond", nose="flat", monolid=True,
         smile=0.05),
    dict(id="human_m_maintenance", species="human", sex="m", dept="maintenance", age="25-30", years=27, skin="dark",
         hair="black", style="coils", iris="brown", face="oval", nose="broad", smile=0.6),
    dict(id="human_f_archaeology", species="human", sex="f", dept="archaeology", age="50-60", years=57, skin="fair",
         hair="chestnut", grey=0.75, style="bun", iris="grey", heavy=0.25, face="long", nose="straight", freckles=0.5,
         smile=0.3),
    # other powers
    dict(id="klingon_f", species="klingon", sex="f", age="30-40", years=34, skin="klingon", hair="black", style="wild",
         iris="brown", face="diamond", nose="broad", smile=0.05),
    dict(id="klingon_m", species="klingon", sex="m", age="40-50", years=47, skin="klingon2", hair="black",
         style="wild", iris="darkbrown", beard="klingon", heavy=0.5, face="square", nose="broad", smile=0.0),
    dict(id="romulan_f", species="romulan", sex="f", age="40-60 (looks 30s)", years=35, skin="romulan", hair="black",
         style="romulan_f", iris="green", face="heart", nose="straight", smile=0.08, smirk=0.5),
    dict(id="romulan_m", species="romulan", sex="m", age="60-90 (looks 40s)", years=47, skin="tan", hair="darkbrown",
         style="bowl", iris="brown", face="long", nose="roman", smile=0.0),
    dict(id="cardassian_f", species="cardassian", sex="f", age="30-40", years=34, skin="cardassian", hair="black",
         style="slick_bun", iris="blue", face="oval", nose="straight", smile=0.15),
    dict(id="cardassian_m", species="cardassian", sex="m", age="40-50", years=48, skin="cardassian", hair="black",
         style="slick", iris="grey", heavy=0.3, face="square", nose="roman", smile=0.1, smirk=0.6),
    dict(id="ferengi_f", species="ferengi", sex="f", age="25-35", years=27, skin="ferengi", hair=None, style=None,
         iris="blue", face="heart", smile=0.65),
    dict(id="ferengi_m", species="ferengi", sex="m", age="40-50", years=47, skin="ferengi", hair=None, style=None,
         iris="grey", heavy=0.2, face="round", smile=0.9),
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
from mathutils.bvhtree import BVHTree  # noqa: E402

V = Vector
pi = math.pi
rad = math.radians
smooth, gauss, clamp, adiff, lerp, orth = bc.smooth, bc.gauss, bc.clamp, bc.adiff, bc.lerp, bc.orth
HEAD_C = bc.HEAD_C
HC_NP = np.array(HEAD_C)
RES = int(arg("--res", "640"))
SAMPLES = int(arg("--samples", "64"))
SHOTS = [s for s in arg("--shots", "portraits,lineup").split(",") if s]
ONLY_SPECIES = [s for s in (arg("--species") or "").split(",") if s]
ONLY_SEX = arg("--sex")
ONLY = [s for s in (arg("--only") or "").split(",") if s]
SEEDS = [int(s) for s in (arg("--seed") or "").split(",") if s]
NO_HAIR = "--no-hair" in argv

CUR = {}      # the character being built: spec + species + face params
CHAR_MATS = {}

_neck0 = bc.neck_limb


def neck_thick(body, inflate=0.0):
    F = CUR.get("F") or {}
    return _neck0(body, inflate + 0.0085 - 0.004 * body.fem + F.get("neck", 0.0)
                  + (0.008 if CUR.get("species") in ("cardassian", "klingon") else 0.0))


bc.neck_limb = neck_thick
# finer meshes than the game figure (this process only)
bc.TORSO_SEGS, bc.LIMB_SEGS, bc.BAND_T, bc.BAND_L = 72, 32, 64, 32
_tz = bc.TORSO_Z
bc.TORSO_Z = sorted(set(_tz + [(a + b) / 2 for a, b in zip(_tz, _tz[1:]) if a > 1.18]))


def srgb_lin(c):
    return tuple(bc.srgb_to_lin(x) for x in c)


# ---------------------------------------------------------------- numpy helpers

def sm(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def ga(x, s):
    return np.exp(-0.5 * (np.asarray(x, float) / s) ** 2)


def nrm(v):
    n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, 1e-12)


def smax(a, b, k):
    """max(a, b) with a soft blend of width k where both are positive (no offset where one of them is ~0)."""
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return np.maximum(a, b) + 0.25 * k * (h * (1 - h)) * 4 * sm(0.0, 2 * k, np.minimum(a, b)) * 0.5


def rng(seed):
    return np.random.default_rng(seed & 0xFFFFFFFF)


# ---------------------------------------------------------------- face parameters (seed -> face)

FACES = ["oval", "round", "square", "long", "heart", "diamond"]
NOSES = ["straight", "aquiline", "roman", "snub", "button", "broad", "flat", "hooked"]


def face_params(seed, species, sex, years, ov=None):
    """Every number that shapes one face, from a seed (+ species, sex, apparent age and optional overrides)."""
    ov = ov or {}
    H = lambda k: bc.h32(seed, 300 + k)  # noqa: E731

    def n(k, s):  # triangular noise in -s..s
        return (H(k) + H(k + 97) - 1.0) * s
    fem = 1.0 if sex == "f" else 0.0
    m = 1.0 - fem
    age = float(np.clip((years - 27.0) / 36.0, 0.0, 1.0))
    face = ov.get("face") or FACES[int(H(0) * len(FACES)) % len(FACES)]
    nose = ov.get("nose") or NOSES[int(H(1) * len(NOSES)) % len(NOSES)]
    if species == "tellarite":
        nose = "snout"
    F = dict(fem=fem, age=age, years=years, face=face, nose=nose, species=species, seed=seed)
    # skull
    F["skull_w"] = 0.0772 - 0.0030 * fem + n(2, 0.0024)
    F["skull_l"] = 0.104 - 0.003 * fem + n(3, 0.004)
    F["skull_h"] = 0.118 - 0.002 * fem + n(4, 0.004)
    F["face_d"] = 0.096 + n(5, 0.0025)
    F["crown"] = 0.72 + n(6, 0.07)
    F["slope"] = 0.0015 + 0.004 * m + n(7, 0.0025)        # forehead leaning back
    F["occ"] = n(8, 0.004)                                 # back of the skull
    F["temple"] = 0.04 + n(9, 0.025)                       # temples in
    F["fore_h"] = n(10, 1.8)                               # hairline / forehead height (deg)
    # landmarks (head-local z): eyes, nose, mouth, chin
    F["eye_r"] = 0.0130
    F["eye_x"] = 0.0322 + 0.0004 * m + n(11, 0.0016)
    F["eye_z"] = n(12, 0.0025)
    F["nose_len"] = 0.0395 + 0.003 * m + 0.002 * age + n(13, 0.003)
    F["col"] = 0.0062 + n(14, 0.0008)                     # tip to subnasale
    F["philtrum"] = 0.0122 + 0.0012 * m + n(15, 0.0016)
    F["chin_h"] = 0.0315 + 0.003 * m + n(16, 0.003)
    # jaw and cheeks
    F["jaw"] = 0.21 + 0.06 * fem + n(17, 0.045)            # narrowing toward the chin (more = narrower)
    F["gonion"] = 0.05 * m + 0.015 + n(18, 0.02)                 # jaw angle flaring out
    F["jaw_slope"] = 0.36 + n(19, 0.07)
    F["yg"] = -0.004 + n(63, 0.006)                        # jaw angle position (front-back)
    F["cheek"] = 0.0030 + 0.0006 * fem + n(20, 0.0014)     # cheekbone prominence
    F["cheek_z"] = n(21, 0.004)
    F["cheek_w"] = 0.050 + n(22, 0.003)
    F["fat"] = float(np.clip(0.45 + 0.15 * fem + n(23, 0.4), 0, 1))
    F["muzzle"] = 0.0062 + n(24, 0.0012)
    # brow
    F["brow"] = 0.0016 + 0.0042 * m + n(25, 0.0012)
    # eyes
    F["open"] = 1.06 + 0.06 * fem + n(26, 0.10)
    F["tilt"] = 1.5 + 2.5 * fem + n(27, 5.0)
    F["hood"] = H(28)
    F["sock"] = 0.0040 + n(29, 0.0010) + 0.0010 * age
    # nose by type, then noise
    nb = dict(straight=(0.0195, 0.0172, 0.0092, 0.0000, 0.000, 1.0, 1.0),
              aquiline=(0.0215, 0.0168, 0.0088, 0.0011, -0.0012, 0.9, 1.04),
              roman=(0.0215, 0.0180, 0.0100, 0.0016, -0.0004, 1.0, 1.04),
              snub=(0.0170, 0.0170, 0.0088, -0.0009, 0.0020, 1.15, 0.93),
              button=(0.0158, 0.0160, 0.0084, -0.0005, 0.0012, 1.3, 0.9),
              broad=(0.0180, 0.0212, 0.0112, 0.0000, 0.0004, 1.25, 1.0),
              flat=(0.0148, 0.0196, 0.0108, -0.0004, 0.0006, 1.15, 0.96),
              hooked=(0.0222, 0.0170, 0.0090, 0.0014, -0.0020, 0.95, 1.06),
              snout=(0.024, 0.0215, 0.0120, 0.0, 0.003, 1.0, 1.0))[nose]
    F["nose_proj"] = nb[0] * (0.92 + 0.06 * m) + n(30, 0.0013)
    F["nose_w"] = nb[1] * (1.0 + 0.05 * m) + n(31, 0.0012)
    F["bridge_w"] = nb[2] + n(32, 0.0006)
    F["hump"] = nb[3] + n(33, 0.0005)
    F["tip_up"] = nb[4] + n(34, 0.0006) - 0.0008 * age
    F["bulb"] = nb[5] + n(35, 0.2)
    F["nose_len"] *= nb[6]
    F["nasion_h"] = 0.0022 + n(36, 0.0010) + 0.0010 * m
    F["nose_dev"] = n(37, 0.0009)
    # mouth
    F["mouth_w"] = 0.0238 + 0.0006 * m + n(38, 0.0022)
    F["ul"] = (0.0064 + 0.0012 * fem + n(39, 0.0014)) * (1 - 0.3 * age)
    F["ll"] = (0.0090 + 0.0016 * fem + n(40, 0.0018)) * (1 - 0.22 * age)
    F["lip_proj"] = 0.0014 + 0.0005 * fem + n(41, 0.0004)
    F["corner"] = n(42, 0.0006) - 0.0008 * age
    # chin
    F["chin_proj"] = 0.0052 + 0.0020 * m + n(43, 0.0016)
    F["chin_w"] = 0.0150 + 0.0022 * m + n(44, 0.0025)
    F["cleft"] = 0.0007 if (H(45) < 0.15 and m) else 0.0
    # ears, neck
    F["ear"] = 1.0 + 0.08 * age + n(46, 0.09)
    F["ear_dz"] = n(47, 0.004)
    F["ear_out"] = 12 + n(48, 9.0)
    F["neck"] = 0.004 + 0.008 * m + n(49, 0.003) + 0.004 * ov.get("heavy", 0.0)
    # asymmetry (+x side value; the other side gets the opposite)
    F["a_eye"] = n(50, 0.0007)
    F["a_brow"] = n(51, 0.0012)
    F["a_mouth"] = n(52, 0.0006)
    F["a_ear"] = n(53, 0.003)
    # age
    F["wrinkle"] = age * (0.65 + 0.7 * H(54))
    F["bags"] = 0.0007 * age + 0.0006 * age * H(55)
    F["trough"] = 0.0005 + 0.0012 * age
    F["jowl"] = 0.003 * age * (0.6 + 0.8 * F["fat"])
    F["recede"] = (0.0 if fem else 1.0) * max(0.0, age - 0.25 + n(56, 0.3)) * 7.0
    F["peak"] = 2.0 * H(57) if H(58) < 0.3 else 0.0       # widow's peak (deg)
    # expression
    F["smile"] = ov.get("smile", 0.15 + n(59, 0.15))
    F["smirk"] = ov.get("smirk", n(60, 0.25)) * (1 if H(61) < 0.5 else -1)
    F["raise"] = ov.get("raise", n(62, 0.25))
    F["squint"] = 0.6 * F["smile"]
    F["open_mouth"] = ov.get("open_mouth", 0.0)
    F["freckles"] = ov.get("freckles", 0.0)
    F["monolid"] = bool(ov.get("monolid", False))
    # face shapes
    if face == "round":
        F["skull_w"] += 0.002
        F["jaw"] -= 0.02
        F["gonion"] -= 0.012
        F["fat"] = min(1.0, F["fat"] + 0.3)
        F["chin_h"] -= 0.003
        F["chin_w"] += 0.002
    elif face == "square":
        F["jaw"] -= 0.05
        F["gonion"] += 0.03
        F["chin_w"] += 0.004
        F["fat"] = max(0.0, F["fat"] - 0.15)
        F["jaw_slope"] -= 0.06
    elif face == "long":
        F["skull_w"] -= 0.0025
        F["chin_h"] += 0.004
        F["philtrum"] += 0.0012
        F["nose_len"] += 0.002
        F["jaw"] += 0.015
        F["skull_h"] += 0.003
    elif face == "heart":
        F["jaw"] += 0.05
        F["chin_w"] -= 0.003
        F["cheek_w"] += 0.003
        F["temple"] -= 0.02
        F["chin_proj"] += 0.0008
    elif face == "diamond":
        F["cheek"] += 0.0010
        F["cheek_w"] += 0.0022
        F["jaw"] += 0.035
        F["temple"] += 0.03
        F["chin_w"] -= 0.002
    # species structure
    if species in ("vulcan", "romulan"):
        F["tilt"] += 3.0
        F["raise"] = min(F["raise"], 0.0) * 0.3
        F["smile"] = min(F["smile"], 0.1)
    if species == "klingon":
        F["brow"] += 0.003
        F["nose_w"] += 0.002
        F["jaw"] -= 0.02
    if species == "tellarite":
        F["brow"] += 0.004
        F["jaw"] -= 0.04
        F["fat"] = min(1.0, F["fat"] + 0.3)
    if species == "ferengi":
        F["nose_proj"] += 0.002
        F["open_mouth"] = ov.get("open_mouth", 0.0058 if sex == "m" else 0.0045)
        F["ul"] *= 0.8
    # landmarks
    ez = 0.012 + F["eye_z"]
    F["ez"] = ez
    F["nz"] = ez + 0.004
    F["tz"] = ez - F["nose_len"]
    F["sz"] = F["tz"] - F["col"]
    F["mz"] = F["sz"] - F["philtrum"]
    F["cz"] = F["mz"] - F["chin_h"]
    F["face_len"] = (0.0165 - F["cz"]) / 0.104
    F["bz"] = ez + 0.0215
    return F


# ---------------------------------------------------------------- the head (numpy)

def lips_at(x, F):
    """Mouth line and lip heights at head-local x: (zm, zu, zl, ulh, llh, u)."""
    ax = np.abs(x)
    sx = np.where(x >= 0, 1.0, -1.0)
    smile = F["smile"]
    mw = F["mouth_w"] * (1 + 0.07 * smile)
    u = ax / mw
    corner = (F["corner"] + 0.0034 * smile + 0.0022 * F["smirk"] * sx + F["a_mouth"] * sx) * np.minimum(u, 1.3) ** 2.2
    zm = F["mz"] + corner
    o = F["open_mouth"] * np.clip(1 - u ** 2, 0, 1) ** 0.7
    zu = zm + 0.62 * o
    zl = zm - 0.38 * o
    ulh = F["ul"] * np.clip(1 - u ** 2.4, 0, 1) ** 0.6 * (1 + 0.20 * ga(ax - 0.0060, 0.0026) - 0.14 * ga(x, 0.0017)) \
        * (1 - 0.22 * smile)
    llh = F["ll"] * np.clip(1 - u ** 2.0, 0, 1) ** 0.65 * (1 - 0.12 * smile)
    return zm, zu, zl, ulh, llh, u


def nose_profile(F):
    """Nose projection and half width along z as fine lookup tables (np.interp)."""
    nz, tz, sz = F["nz"], F["tz"], F["sz"]
    P, Pn = F["nose_proj"], F["nasion_h"]
    L = nz - tz
    up = F["tip_up"]
    if F["nose"] == "snout":
        zs = [nz + 0.004, nz, nz - 0.3 * L, tz + 0.012, tz + 0.007, tz + 0.002, tz - 0.003, sz + 0.001, sz, sz - 0.004]
        ps = [0.0, Pn + 0.002, 0.010, 0.020, P, P, P - 0.002, 0.008, 0.0, 0.0]
        ws = [0.009, 0.010, 0.011, 0.015, 0.019, 0.020, 0.019, 0.010, 0.006, 0.005]
    else:
        zs = [nz + 0.005, nz, nz - 0.40 * L, nz - 0.62 * L, tz + 0.0075 + up * 0.6, tz + 0.0015 + up, tz - 0.0022 + up * 0.6,
              sz + 0.0016, sz, sz - 0.004]
        ps = [0.0, Pn, Pn + 0.36 * (P - Pn) + F["hump"], Pn + 0.58 * (P - Pn) + F["hump"] * 0.6, P - 0.0030 - max(0.0, up) * 0.4,
              P, P - 0.0022, 0.0068, 0.0, 0.0]
        bw = F["bridge_w"]
        nw = F["nose_w"] / 0.0172
        ws = [bw * 1.3, bw * 1.12, bw * 1.08, bw * 1.15, bw * 1.25, 0.0118 * F["bulb"] ** 0.5 * nw, 0.0128 * nw,
              0.0115 * nw, 0.0060, 0.0045]
    zs, ps, ws = zs[::-1], ps[::-1], ws[::-1]
    zz = np.linspace(zs[0], zs[-1], 600)
    pp = np.array([bc.catmull(ps, zs, z) for z in zz])
    ww = np.array([bc.catmull(ws, zs, z) for z in zz])
    return zz, np.maximum(pp, 0.0), np.maximum(ww, 0.003)


def nose_y(x, z, F):
    zz, pp, ww = F["_nprof"]
    tz, sz = F["tz"], F["sz"]
    xd = x - F["nose_dev"] * sm(F["nz"], tz, z)
    P = np.interp(z, zz, pp, left=0.0, right=0.0)
    W = np.interp(z, zz, ww, left=0.003, right=0.003)
    r = np.abs(xd) / W
    main = P * (0.75 * np.clip(1 - r * r, 0, 1) ** 1.5 + 0.25 * np.clip(1 - (1.5 * r) ** 2, 0, 1) ** 1.3)
    if F["nose"] == "snout":
        # flat front pad: a plateau with rounded edges, two forward nostrils
        e = np.sqrt((xd / 0.0175) ** 2 + ((z - (tz + 0.002)) / 0.0125) ** 2)
        pad = F["nose_proj"] * sm(1.08, 0.75, e)
        main = smax(main, pad, 0.0015)
        nos = 0.0045 * np.exp(-0.5 * (((np.abs(xd) - 0.0068) / 0.0026) ** 2 + ((z - (tz + 0.0005)) / 0.0035) ** 2))
        return main - nos * sm(0.9, 0.6, e)
    # tip bulb with the two domes
    tipz = tz + 0.0012 + F["tip_up"]
    main = main + 0.0016 * F["bulb"] * np.exp(-0.5 * ((xd / 0.0050) ** 2 + ((z - tipz) / 0.0046) ** 2)) \
        - 0.00035 * ga(xd, 0.0011) * ga(z - tipz, 0.003)
    # alae
    out = main
    cz = sz + 0.0042
    for s in (-1, 1):
        cx = s * F["nose_w"] * 0.74
        rx = 0.0072 * F["nose_w"] / 0.0172
        e = np.sqrt(((xd - cx) / rx) ** 2 + ((z - cz) / 0.0060) ** 2)
        a = 0.0068 * np.clip(1 - e * e, 0, 1) ** 0.55
        out = smax(out, a, 0.0016)
        # alar crease round the outer side and the back of the wing
        lateral = sm(0.0, 0.6, (np.abs(xd) - np.abs(cx)) / rx + 0.4 * (z - cz) / 0.006)
        out = out - 0.0010 * ga(e - 1.0, 0.12) * lateral
    nos = np.exp(-0.5 * (((np.abs(xd) - F["nose_w"] * 0.38) / 0.0030) ** 2 + ((z - (sz + 0.0026)) / 0.0018) ** 2))
    return out - 0.0042 * nos


def head_np(la, lo):
    """Head surface (head-local metres) for latitude/longitude arrays in degrees (lo 90 = straight ahead)."""
    F = CUR["F"]
    body = CUR["body"]
    sp = F["species"]
    la = np.asarray(la, float)
    lo = np.asarray(lo, float)
    fem, heavy = F["fem"], body.heavy
    cl, sl = np.cos(np.radians(la)), np.sin(np.radians(la))
    ch = np.where(sl > 0, np.abs(cl) ** F["crown"], cl)
    dx, dy, dz = ch * np.cos(np.radians(lo)), ch * np.sin(np.radians(lo)), sl
    x = dx * F["skull_w"]
    y = dy * np.where(dy > 0, F["face_d"], F["skull_l"])
    z = dz * np.where(dz > 0, F["skull_h"], 0.112 * F["face_len"])
    flat = 0.06 + 0.18 * sm(-0.06, -0.01, z)
    y = y - flat * 0.004 * np.logaddexp(0.0, (y - 0.055) / 0.004)
    y = y - F["slope"] * sm(0.02, 0.11, z) * sm(0.0, 0.07, y)
    y = y - F["occ"] * ga(z - 0.03, 0.035) * sm(0.0, -0.06, y)
    x = x * (1 - F["temple"] * ga(z - 0.035, 0.03) * sm(0.0, 0.06, y))
    fl = F["face_len"]
    low = sm(0.0, -0.1 * fl, z)
    x = x * (1 - (F["jaw"] - 0.04 * heavy) * low * (0.6 + 0.4 * sm(-0.02, 0.06, y)))
    x = x * (1 + F["gonion"] * ga(z + 0.074 * fl, 0.016) * sm(-0.035, 0.0, y) * sm(0.06, 0.02, y))
    # underside: jawline from the chin back to the jaw angle, the ramus up behind it, the skull base behind that
    k_ = 0.003
    zf = -0.104 * fl + k_ * np.logaddexp(0.0, (0.07 - y) / k_) * F["jaw_slope"]
    yg = F["yg"]
    zg = -0.104 * fl + (0.07 - yg) * F["jaw_slope"]
    zr = zg + 2.6 * k_ * np.logaddexp(0.0, (yg - y) / k_)
    zmin = 0.5 * (zf + zr) + 0.5 * np.sqrt((zf - zr) ** 2 + 4e-6)          # smooth max
    zb = -0.062
    zmin = 0.5 * (zmin + zb) - 0.5 * np.sqrt((zmin - zb) ** 2 + 4e-6)      # smooth min
    z = z + 0.8 * 0.0035 * np.logaddexp(0.0, (zmin - z) / 0.0035)
    x = x * (1 + 0.04 * heavy)
    ax = np.abs(x)
    sx = np.where(x >= 0, 1.0, -1.0)
    fy = sm(0.03, 0.07, y)
    # zygomatic arch back to the ear, temple hollow above it
    zy = F["ez"] - 0.016 + F["cheek_z"]
    side = sm(-0.012, 0.004, y) * sm(0.07, 0.045, y)
    x = x + sx * (0.0030 + 0.0012 * (1 - F["fem"])) * ga(z - zy, 0.0075) * side
    x = x - sx * (0.0028 + 0.002 * F["age"]) * ga(z - (F["ez"] + 0.024), 0.012) * sm(-0.015, 0.01, y) * sm(0.075, 0.05, y)
    ez = F["ez"] + F["a_eye"] * sx
    ex = F["eye_x"]
    bz = F["bz"] + F["a_brow"] * sx
    mz, sz, tz, cz = F["mz"], F["sz"], F["tz"], F["cz"]
    smile = F["smile"]
    age = F["age"]
    wr = F["wrinkle"]
    d = np.zeros_like(x)
    # eye sockets
    for s in (-1, 1):
        g = np.exp(-((x - s * ex) ** 2 / (2 * 0.0150 ** 2) + (z - ez) ** 2 / (2 * 0.0115 ** 2)))
        d -= 0.0095 * g
        d -= F["sock"] * np.exp(-((x - s * ex) ** 2 / (2 * 0.0150 ** 2) + (z - ez) ** 2 / (2 * 0.0098 ** 2)))
        d += 0.0025 * gauss_np2(x - s * (ex + 0.0175), z - (ez - 0.004), 0.006, 0.010)   # lateral orbital rim
    # brow ridge and glabella
    d += F["brow"] * ga(z - bz, 0.0072) * sm(0.058, 0.028, ax)
    d += 0.45 * F["brow"] * ga(x, 0.008) * ga(z - (bz - 0.004), 0.0075)
    # cheekbones, malar fat, buccal hollow
    cbz = ez - 0.022 + F["cheek_z"]
    d += F["cheek"] * ga(ax - F["cheek_w"] * 0.94, 0.012) * ga(z - cbz, 0.013)
    x = x + sx * 0.0045 * F["cheek"] / 0.003 * ga(ax - F["cheek_w"] * 1.05, 0.016) * ga(z - cbz, 0.019) * sm(0.0, 0.05, y)
    d += (0.0022 * F["fat"] + 0.0018 * smile) * ga(ax - 0.035, 0.0125) * ga(z - (ez - 0.034 + 0.005 * smile), 0.0135)
    d -= (0.0028 * (1 - F["fat"]) + 0.0012 * age) * ga(ax - 0.048, 0.011) * ga(z - (mz + 0.004), 0.013)
    # muzzle on a rounder dental arch
    d += F["muzzle"] * ga(x, 0.026) * ga(z - (mz + 0.0025), 0.022)
    d -= 2.2 * x * x * ga(z - (mz - 0.004), 0.03) * sm(0.05, 0.02, ax)
    # nose
    d += nose_y(x, z, F)
    # philtrum
    zm, zu, zl, ulh, llh, u = lips_at(x, F)
    ph = sm(sz + 0.001, sz - 0.002, z) * sm(zu + ulh - 0.0005, zu + ulh + 0.0025, z)
    d += (0.00075 * ga(ax - 0.0047, 0.0013) - 0.00035 * ga(x, 0.0018)) * ph
    # lips
    env = np.clip(1 - u ** 2, 0, 1) ** 0.5
    lp = F["lip_proj"]
    d += lp * (ulh / 0.0068) ** 0.7 * ga(z - (zu + 0.55 * ulh), 0.48 * ulh + 1e-4) * env
    d += 0.00045 * ga(z - (zu + ulh), 0.0006) * env                                 # vermilion border roll
    d += 1.15 * lp * (llh / 0.009) ** 0.7 * ga(z - (zl - 0.5 * llh), 0.5 * llh + 1e-4) * env
    d -= 0.0016 * ga(z - zm, 0.0007) * sm(1.15, 0.85, u) * (1 - sm(0.0005, 0.002, F["open_mouth"]))
    d -= (0.0007 + 0.0006 * smile) * ga(ax - (F["mouth_w"] + 0.0025), 0.0021) * ga(z - zm, 0.0032)   # corners
    d -= 0.0024 * ga(z - (zl - llh - 0.0055), 0.0034) * ga(x, 0.013)                                    # sulcus
    if F["open_mouth"] > 0:
        cav = sm(zl - 0.0004, zl + 0.0012, z) * sm(zu + 0.0004, zu - 0.0012, z) * sm(1.0, 0.82, u)
        d -= 0.012 * cav
    # nasolabial folds
    z0, z1 = sz + 0.0065, zm - 0.0105
    t = np.clip((z0 - z) / (z0 - z1), 0, 1)
    xc = F["nose_w"] + 0.0028 + (F["mouth_w"] + 0.0062 + 0.0025 * smile - F["nose_w"] - 0.0028) * t + 0.0035 * np.sin(pi * t)
    dd = ax - xc
    envf = sm(z0 + 0.003, z0 - 0.002, z) * sm(z1 - 0.006, z1 + 0.002, z)
    nlA = 0.0005 + 0.0016 * age + 0.0014 * smile + 0.0006 * F["fat"]
    nlC = 0.00025 + 0.0011 * age + 0.0007 * smile
    d += nlA * ga(dd - 0.0045, 0.0042) * envf - nlC * ga(dd, 0.0011) * envf
    # under the eyes
    d -= F["trough"] * ga(ax - (ex - 0.007), 0.008) * ga(z - (ez - 0.0145), 0.0028)
    d += F["bags"] * ga(ax - ex, 0.0095) * ga(z - (ez - 0.0108), 0.0032)
    # chin, cleft, jowls
    d += F["chin_proj"] * ga(x, F["chin_w"]) * ga(z - cz, 0.0095)
    d -= F["cleft"] * ga(x, 0.0018) * ga(z - cz, 0.0065)
    x = x + sx * F["jowl"] * ga(z - (cz + 0.008), 0.011) * ga(ax - 0.037, 0.012) * sm(0.0, 0.05, y)
    # wrinkles
    if wr > 0.02:
        for k in range(4):
            zc = bz + 0.0115 + 0.0068 * k + 0.0011 * np.sin(x * 95 + k * 1.7)
            d -= 0.00042 * wr * (1 - 0.2 * k) * ga(z - zc, 0.0007) * sm(0.05, 0.018, ax)
        d -= 0.00035 * wr * ga(ax - 0.0042, 0.0007) * ga(z - (bz - 0.0025), 0.0045)
        for s in (-1, 1):
            cx_, cz_ = s * (ex + 0.0135), ez + 0.0005
            for ang in (-30, -8, 14):
                ca, sa = math.cos(rad(ang)), math.sin(rad(ang))
                along = (x - cx_) * s * ca + (z - cz_) * sa
                perp = -(x - cx_) * s * sa + (z - cz_) * ca
                d -= 0.0004 * wr * (1 + 0.6 * smile) * ga(perp, 0.0008) * sm(0.002, 0.005, along) * sm(0.019, 0.010, along)
        d -= 0.0003 * wr * ga(z - (ez - 0.0122 - 8.0 * (ax - ex) ** 2), 0.0007) * ga(ax - ex, 0.0085)
        mar = ax - (F["mouth_w"] + 0.0035) - (zm - z) * 0.18
        d -= 0.0006 * wr * ga(mar, 0.0012) * sm(zm + 0.001, zm - 0.006, z) * sm(zm - 0.03, zm - 0.018, z)
    y = y + d * fy
    # species surfaces
    if sp in ("klingon", "romulan", "cardassian", "ferengi", "tellarite", "bajoran"):
        x, y, z = species_np(x, y, z, la, lo, F)
    return x, y, z


def gauss_np2(dx, dz, sx_, sz_):
    return np.exp(-0.5 * ((dx / sx_) ** 2 + (dz / sz_) ** 2))


def seg_np(x, z, a, b):
    ax_, az_ = b[0] - a[0], b[1] - a[1]
    t = np.clip(((x - a[0]) * ax_ + (z - a[1]) * az_) / (ax_ * ax_ + az_ * az_), 0.0, 1.0)
    return np.hypot(x - a[0] - ax_ * t, z - a[1] - az_ * t)


def species_np(x, y, z, la, lo, F):
    sp = F["species"]
    ax = np.abs(x)
    front = sm(-0.01, 0.045, y)
    fem = F["fem"]
    k = 1.0 - 0.3 * fem
    d = np.zeros_like(x)
    bz = F["bz"]
    if sp == "klingon":
        d += 0.0055 * k * ga(z - (bz + 0.002), 0.0075) * sm(0.068, 0.03, ax) * front           # heavy brow
        d += 0.0055 * k * ga(x, 0.0055) * sm(bz + 0.008, bz + 0.026, z) * sm(-0.035, 0.01, y)  # crest
        for i in range(5):
            zc = bz + 0.019 + 0.0145 * i + 9.0 * x * x
            prof = np.maximum(0.0, np.cos((z - zc) / 0.0052 * pi / 2)) ** 1.5 * (np.abs(z - zc) < 0.0052)
            lobe = 1 + 0.25 * np.cos(x * 160)
            d += 0.0048 * k * (1 - 0.12 * i) * prof * lobe * sm(0.058 - 0.005 * i, 0.02, ax) * sm(-0.035, 0.02, y)
        d -= 0.0012 * k * ga(x, 0.0025) * sm(bz + 0.01, bz + 0.03, z) * front                    # crest groove
        x = x * 1.03
    elif sp == "romulan":
        for s in (-1, 1):
            dd = seg_np(x, z, (s * 0.046, bz + 0.036), (s * 0.003, bz - 0.005))
            d += 0.0034 * ga(dd, 0.003) * front
        d += 0.0012 * ga(z - (bz + 0.001), 0.006) * sm(0.06, 0.03, ax) * front
    elif sp == "cardassian":
        r = np.sqrt((x / 0.0175) ** 2 + ((z - (bz + 0.0395)) / 0.026) ** 2)
        d += 0.0042 * ga(r - 1.0, 0.13) * front                                                # spoon rim
        d += 0.0016 * sm(1.0, 0.55, r) * front                                                 # raised bowl
        d -= 0.0012 * ga(r - 1.22, 0.1) * front                                                # groove outside the rim
        d += 0.0034 * ga(x, 0.0042) * sm(bz - 0.014, bz - 0.001, z) * sm(bz + 0.019, bz + 0.009, z) * front  # handle
        ang = np.abs((lo - 90.0 + 180.0) % 360.0 - 180.0)
        lc = 17 - 7 * sm(30, 90, ang)
        d += 0.0048 * ga(la - lc, 3.0) * sm(24, 36, ang) * sm(108, 94, ang)                   # brow-temple ridge
        d += 0.0030 * ga(la - lc + 7.5, 2.4) * sm(40, 55, ang) * sm(104, 92, ang)
        d -= 0.0012 * ga(la - lc + 3.8, 1.6) * sm(36, 50, ang) * sm(104, 92, ang)
        # jaw-line ridge from below the ear toward the chin
        d += 0.0020 * ga(la + 34 - 6 * sm(70, 30, ang), 3.0) * sm(25, 45, ang) * sm(85, 65, ang)
    elif sp == "ferengi":
        s = sm(-0.02, 0.10, z)
        x = x * (1 + 0.17 * s)
        y = np.where(y < 0, y * (1 + 0.16 * s), y)
        z = z * (1 + 0.07 * s)
        d -= 0.0045 * ga(x, 0.009) * sm(0.07, 0.105, z) * sm(-0.06, 0.0, y)                    # cranial furrow
        for i in range(5):
            zc = bz + 0.013 + 0.0125 * i + 0.0025 * np.sin(x * 140 + i)
            d += 0.0021 * ga(z - zc, 0.0026) * sm(0.06, 0.02, ax) * front
        d += 0.0034 * ga(z - (bz + 0.002), 0.007) * sm(0.062, 0.03, ax) * front
        for zr in (F["nz"] - 0.002, F["nz"] - 0.007, F["nz"] - 0.012, F["nz"] - 0.017):        # nose ridges
            d += 0.0011 * ga(z - zr, 0.0011) * sm(0.012, 0.004, ax) * front
    elif sp == "tellarite":
        d += 0.0065 * k * ga(z - (bz + 0.003), 0.0085) * sm(0.072, 0.03, ax) * front
        x = x * (1 + 0.10 * sm(0.02, -0.07, z))
    elif sp == "bajoran":
        for i, zr in enumerate((F["nz"] - 0.001, F["nz"] - 0.0045, F["nz"] - 0.008, F["nz"] - 0.0115)):
            d += 0.0010 * (1 - 0.1 * i) * ga(z - zr + 0.0009 * (x / 0.008) ** 2, 0.0009) * sm(0.0095 + 0.001 * i, 0.003, ax) * front
    r = np.sqrt(x * x + y * y + z * z)
    r = np.maximum(r, 1e-9)
    return x + x / r * d, y + y / r * d, z + z / r * d


def head_local_np(la, lo, body=None):
    x, y, z = head_np(np.array([la], float), np.array([lo], float))
    return V((float(x[0]), float(y[0]), float(z[0])))


bc.head_local = head_local_np


def head_xyz(la, lo):
    x, y, z = head_np(la, lo)
    return np.stack([x, y, z], -1)


def head_normals(la, lo, e=0.25):
    a = head_xyz(la, lo + e) - head_xyz(la, lo - e)
    b = head_xyz(np.minimum(la + e, 89.9), lo) - head_xyz(np.maximum(la - e, -89.9), lo)
    n = nrm(np.cross(a, b))
    p = head_xyz(la, lo)
    flip = (np.sum(n * p, -1) < 0)[..., None]
    return np.where(flip, -n, n)


class FaceGrid:
    """Front height map of the head: y(x, z) on a 0.2 mm grid (front-most surface), for brows, lids and teeth."""

    def __init__(self):
        la = np.arange(-72, 58, 0.14)
        lo = np.arange(22, 158, 0.14)
        LA, LO = np.meshgrid(la, lo, indexing="ij")
        x, y, z = head_np(LA.ravel(), LO.ravel())
        self.x0, self.z0, self.h = -0.095, -0.135, 0.0002
        nx, nz = int(0.19 / self.h) + 1, int(0.27 / self.h) + 1
        gx = np.clip(np.round((x - self.x0) / self.h).astype(int), 0, nx - 1)
        gz = np.clip(np.round((z - self.z0) / self.h).astype(int), 0, nz - 1)
        g = np.full((nz, nx), -1.0)
        np.maximum.at(g, (gz, gx), y)
        for _ in range(6):
            empty = g < -0.5
            if not empty.any():
                break
            nb = np.stack([np.roll(g, 1, 0), np.roll(g, -1, 0), np.roll(g, 1, 1), np.roll(g, -1, 1)])
            fill = nb.max(0)
            g = np.where(empty & (fill > -0.5), fill, g)
        self.g = g

    def y(self, x, z):
        x, z = np.asarray(x, float), np.asarray(z, float)
        fx = (x - self.x0) / self.h
        fz = (z - self.z0) / self.h
        i0 = np.clip(np.floor(fx).astype(int), 0, self.g.shape[1] - 2)
        j0 = np.clip(np.floor(fz).astype(int), 0, self.g.shape[0] - 2)
        tx, tz = np.clip(fx - i0, 0, 1), np.clip(fz - j0, 0, 1)
        g = self.g
        return (g[j0, i0] * (1 - tx) * (1 - tz) + g[j0, i0 + 1] * tx * (1 - tz) + g[j0 + 1, i0] * (1 - tx) * tz
                + g[j0 + 1, i0 + 1] * tx * tz)

    def n(self, x, z, e=0.0006):
        dydx = (self.y(x + e, z) - self.y(x - e, z)) / (2 * e)
        dydz = (self.y(x, z + e) - self.y(x, z - e)) / (2 * e)
        return nrm(np.stack([-dydx, np.ones_like(dydx), -dydz], -1))


def face_y_np(x, z, body=None):
    return HEAD_C.y + float(CUR["grid"].y(x, z))


bc.face_y = face_y_np


def hl(x, z, body=None):
    return HEAD_C.y + float(CUR["grid"].y(x, z))


class HeadField:
    """Radius of the head surface by direction (1 deg grid), for pushing hair strands out of the head."""

    def __init__(self):
        la = np.arange(-89.5, 89.6, 0.5)
        lo = np.arange(0, 360, 0.5)
        LA, LO = np.meshgrid(la, lo, indexing="ij")
        p = head_xyz(LA.ravel(), LO.ravel())
        r = np.linalg.norm(p, axis=-1)
        el = np.degrees(np.arcsin(np.clip(p[:, 2] / r, -1, 1)))
        az = np.degrees(np.arctan2(p[:, 1], p[:, 0])) % 360
        gi = np.clip(np.round(el + 90).astype(int), 0, 180)
        gj = np.round(az).astype(int) % 360
        g = np.zeros((181, 360))
        np.maximum.at(g, (gi, gj), r)
        for _ in range(8):
            empty = g <= 0
            if not empty.any():
                break
            nb = np.stack([np.roll(g, 1, 0), np.roll(g, -1, 0), np.roll(g, 1, 1), np.roll(g, -1, 1)])
            g = np.where(empty, nb.max(0), g)
        self.g = g

    def R(self, p):
        r = np.linalg.norm(p, axis=-1)
        el = np.degrees(np.arcsin(np.clip(p[..., 2] / np.maximum(r, 1e-9), -1, 1))) + 90
        az = np.degrees(np.arctan2(p[..., 1], p[..., 0])) % 360
        i0 = np.clip(np.floor(el).astype(int), 0, 179)
        j0 = np.floor(az).astype(int) % 360
        j1 = (j0 + 1) % 360
        ti, tj = np.clip(el - i0, 0, 1), az - np.floor(az)
        g = self.g
        return (g[i0, j0] * (1 - ti) * (1 - tj) + g[i0, j1] * (1 - ti) * tj + g[i0 + 1, j0] * ti * (1 - tj)
                + g[i0 + 1, j1] * ti * tj), r

    def push(self, p, h):
        R, r = self.R(p)
        need = R + h
        k = np.where(r < need, need / np.maximum(r, 1e-9), 1.0)
        return p * k[..., None]


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


def math_node(nt, op, a, b=None, clamp_=False):
    n = node(nt, "ShaderNodeMath")
    n.operation = op
    n.use_clamp = clamp_
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            n.inputs[i].default_value = v
        else:
            nt.links.new(v, n.inputs[i])
    return n.outputs[0]


def mix_rgb(nt, fac, a, b, blend="MIX"):
    n = node(nt, "ShaderNodeMix")
    n.data_type = "RGBA"
    n.blend_type = blend
    ins = {s.identifier: s for s in n.inputs}
    for sock, v in ((ins["Factor_Float"], fac), (ins["A_Color"], a), (ins["B_Color"], b)):
        if isinstance(v, (int, float)):
            sock.default_value = v
        elif isinstance(v, tuple):
            sock.default_value = (*v, 1.0) if len(v) == 3 else v
        else:
            nt.links.new(v, sock)
    return {s.identifier: s for s in n.outputs}["Result_Color"]


def attr(nt, name):
    a = node(nt, "ShaderNodeAttribute")
    a.attribute_name = name
    return a


def sep(nt, sock):
    s = node(nt, "ShaderNodeSeparateColor")
    nt.links.new(sock, s.inputs[0])
    return s.outputs


def skin_material(name, c):
    """Skin: SSS (random walk, skin), per-vertex colour zones (tint), mottling, freckles / age spots, T-zone shine,
    pores and fine bump from the rest position, faint sheen (peach fuzz)."""
    sk = SK[c["skin"]]
    F = CUR["F"]
    m, p = principled(name, srgb_lin(sk), 0.5)
    nt = m.node_tree
    try:
        p.subsurface_method = "RANDOM_WALK_SKIN"
    except Exception:  # noqa: BLE001
        pass
    tint = attr(nt, "tint")
    skm = sep(nt, attr(nt, "skm").outputs["Color"])  # R oil, G freckle zone, B age-spot zone
    rest = attr(nt, "rest").outputs["Vector"]
    base = mix_rgb(nt, 1.0, srgb_lin(sk), tint.outputs["Color"], "MULTIPLY")
    # mottling: large soft blotches + finer variation
    n1 = node(nt, "ShaderNodeTexNoise")
    n1.inputs["Scale"].default_value = 55
    n1.inputs["Detail"].default_value = 3
    nt.links.new(rest, n1.inputs["Vector"])
    mot = node(nt, "ShaderNodeMapRange")
    mot.inputs["To Min"].default_value = 0.93
    mot.inputs["To Max"].default_value = 1.06
    nt.links.new(n1.outputs["Fac"], mot.inputs["Value"])
    redn = node(nt, "ShaderNodeTexNoise")
    redn.inputs["Scale"].default_value = 140
    redn.inputs["Detail"].default_value = 2
    nt.links.new(rest, redn.inputs["Vector"])
    base = mix_rgb(nt, 1.0, base, mot.outputs["Result"], "MULTIPLY")
    blot = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", redn.outputs["Fac"], 0.5), 0.35)
    base = mix_rgb(nt, blot, base, tuple(x * y for x, y in zip(srgb_lin(sk), (1.05, 0.82, 0.80))))
    # freckles: small random cells in the freckle zone
    if F["freckles"] > 0 or F["age"] > 0.5:
        vo = node(nt, "ShaderNodeTexVoronoi")
        vo.inputs["Scale"].default_value = 520
        vo.inputs["Randomness"].default_value = 1.0
        nt.links.new(rest, vo.inputs["Vector"])
        cell = sep(nt, vo.outputs["Color"])[0]
        dot = math_node(nt, "LESS_THAN", vo.outputs["Distance"], 0.32)
        pick = math_node(nt, "GREATER_THAN", cell, 0.55)
        spot = math_node(nt, "MULTIPLY", dot, pick)
        zone = math_node(nt, "ADD", math_node(nt, "MULTIPLY", skm[1], F["freckles"]),
                         math_node(nt, "MULTIPLY", skm[2], 0.8 * max(0.0, F["age"] - 0.4)))
        amt = math_node(nt, "MULTIPLY", spot, math_node(nt, "MINIMUM", zone, 1.0))
        fcol = tuple(x * y for x, y in zip(srgb_lin(sk), (0.62, 0.45, 0.33)))
        base = mix_rgb(nt, math_node(nt, "MULTIPLY", amt, 0.75), base, fcol)
    nt.links.new(base, p.inputs["Base Color"])
    # roughness: T-zone shine, lips, noise
    rn = node(nt, "ShaderNodeTexNoise")
    rn.inputs["Scale"].default_value = 260
    nt.links.new(rest, rn.inputs["Vector"])
    r0 = 0.58 + 0.05 * F["age"]
    rough = math_node(nt, "SUBTRACT", r0, math_node(nt, "MULTIPLY", skm[0], 0.17))
    rough = math_node(nt, "ADD", rough, math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", rn.outputs["Fac"], 0.5), 0.12))
    nt.links.new(rough, p.inputs["Roughness"])
    # pores + fine skin texture
    pv = node(nt, "ShaderNodeTexVoronoi")
    pv.inputs["Scale"].default_value = 700
    nt.links.new(rest, pv.inputs["Vector"])
    fn = node(nt, "ShaderNodeTexNoise")
    fn.inputs["Scale"].default_value = 2400
    fn.inputs["Detail"].default_value = 2
    nt.links.new(rest, fn.inputs["Vector"])
    hgt = math_node(nt, "ADD", math_node(nt, "MULTIPLY", math_node(nt, "MINIMUM", pv.outputs["Distance"], 0.45), 1.0),
                    math_node(nt, "MULTIPLY", fn.outputs["Fac"], 0.5))
    hgt = math_node(nt, "ADD", hgt, math_node(nt, "MULTIPLY", n1.outputs["Fac"], 2.5))
    b = node(nt, "ShaderNodeBump")
    b.inputs["Strength"].default_value = 0.35 + 0.15 * F["age"]
    b.inputs["Distance"].default_value = 0.0003
    nt.links.new(hgt, b.inputs["Height"])
    nt.links.new(b.outputs["Normal"], p.inputs["Normal"])
    sp = c["species"]
    p.inputs["Subsurface Weight"].default_value = 1.0
    dark = 1.0 - min(1.0, sum(sk) / 1.6)
    rr = SSS_RADIUS.get(sp, SSS_RADIUS["default"])
    p.inputs["Subsurface Radius"].default_value = (rr[0] * (1 - 0.3 * dark), rr[1], rr[2])
    p.inputs["Subsurface Scale"].default_value = (0.0032 if sp != "cardassian" else 0.0022) * (1 - 0.35 * dark)
    p.inputs["Specular IOR Level"].default_value = 0.5
    p.inputs["Sheen Weight"].default_value = 0.12 * (1 - 0.75 * dark)
    p.inputs["Sheen Roughness"].default_value = 0.35
    return m


def hair_material(name, hc, grey=0.0, rough=0.38, darker=1.0):
    """Principled Hair BSDF (melanin), per-strand colour variation, a greying fraction of white strands, darker roots."""
    mel, red, tint = HC[hc] if isinstance(hc, str) else hc
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    out = nt.nodes["Material Output"]
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    hb = node(nt, "ShaderNodeBsdfHairPrincipled")
    hb.parametrization = "MELANIN"
    info = node(nt, "ShaderNodeHairInfo")
    mel = min(1.0, mel * darker + (1 - darker) * 0.3)
    # grey strands: a per-strand random number below the grey fraction -> almost no melanin
    r2 = math_node(nt, "FRACT", math_node(nt, "MULTIPLY", info.outputs["Random"], 13.37))
    isgrey = math_node(nt, "LESS_THAN", r2, grey)
    rootdark = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", 1.0, info.outputs["Intercept"]), 0.10)
    melv = math_node(nt, "ADD", mel, rootdark)
    melv = math_node(nt, "ADD", math_node(nt, "MULTIPLY", melv, math_node(nt, "SUBTRACT", 1.0, isgrey)),
                     math_node(nt, "MULTIPLY", isgrey, 0.02))
    nt.links.new(melv, hb.inputs["Melanin"])
    hb.inputs["Melanin Redness"].default_value = red
    hb.inputs["Tint"].default_value = (*(srgb_lin(tint) if tint else (1, 1, 1)), 1)
    hb.inputs["Random Color"].default_value = 0.12
    hb.inputs["Random Roughness"].default_value = 0.2
    hb.inputs["Roughness"].default_value = rough
    hb.inputs["Radial Roughness"].default_value = 0.38
    hb.inputs["Coat"].default_value = 0.0
    hb.inputs["IOR"].default_value = 1.55
    hb.inputs["Offset"].default_value = rad(2.0)
    nt.links.new(info.outputs["Random"], hb.inputs["Random"])
    nt.links.new(hb.outputs[0], out.inputs["Surface"])
    return m


def cornea_material():
    m = bpy.data.materials.new("Cornea")
    m.use_nodes = True
    nt = m.node_tree
    out = nt.nodes["Material Output"]
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    tr = node(nt, "ShaderNodeBsdfTransparent")
    gl = node(nt, "ShaderNodeBsdfGlossy")
    gl.inputs["Roughness"].default_value = 0.0
    fr = node(nt, "ShaderNodeFresnel")
    fr.inputs["IOR"].default_value = 1.42
    mx = math_node(nt, "ADD", fr.outputs["Fac"], 0.03)
    mul = node(nt, "ShaderNodeMixShader")
    nt.links.new(mx, mul.inputs["Fac"])
    nt.links.new(tr.outputs["BSDF"], mul.inputs[1])
    nt.links.new(gl.outputs["BSDF"], mul.inputs[2])
    nt.links.new(mul.outputs["Shader"], out.inputs["Surface"])
    return m


def wet_material():
    m = bpy.data.materials.new("Wet")
    m.use_nodes = True
    nt = m.node_tree
    out = nt.nodes["Material Output"]
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    tr = node(nt, "ShaderNodeBsdfTransparent")
    gl = node(nt, "ShaderNodeBsdfGlossy")
    gl.inputs["Roughness"].default_value = 0.05
    mul = node(nt, "ShaderNodeMixShader")
    mul.inputs["Fac"].default_value = 0.35
    nt.links.new(tr.outputs["BSDF"], mul.inputs[1])
    nt.links.new(gl.outputs["BSDF"], mul.inputs[2])
    nt.links.new(mul.outputs["Shader"], out.inputs["Surface"])
    return m


def eye_material(name, iris):
    """One material for sclera + iris + pupil, driven by the 'eyep' attribute (iris-plane coords / iris radius, and
    w = 1 on the iris plane): radial fibres, collarette, dark limbal ring, two-tone colour, warm sclera corners."""
    outer, inner = IR[iris]
    m, p = principled(name, (0.7, 0.66, 0.62), 0.32)
    nt = m.node_tree
    ep = sep(nt, attr(nt, "eyep").outputs["Color"])
    u, v, w = ep[0], ep[1], ep[2]
    r = math_node(nt, "SQRT", math_node(nt, "ADD", math_node(nt, "MULTIPLY", u, u), math_node(nt, "MULTIPLY", v, v)))
    # fibres: noise on (u/r, v/r)*k, r*k2
    inv = math_node(nt, "DIVIDE", 1.0, math_node(nt, "MAXIMUM", r, 0.02))
    cx = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", u, inv), 9.0)
    cy = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", v, inv), 9.0)
    cz = math_node(nt, "MULTIPLY", r, 2.2)
    cv = node(nt, "ShaderNodeCombineXYZ")
    nt.links.new(cx, cv.inputs[0])
    nt.links.new(cy, cv.inputs[1])
    nt.links.new(cz, cv.inputs[2])
    fib = node(nt, "ShaderNodeTexNoise")
    fib.inputs["Scale"].default_value = 1.6
    fib.inputs["Detail"].default_value = 6
    fib.inputs["Roughness"].default_value = 0.65
    nt.links.new(cv.outputs[0], fib.inputs["Vector"])
    fibv = node(nt, "ShaderNodeMapRange")
    fibv.inputs["From Min"].default_value = 0.3
    fibv.inputs["From Max"].default_value = 0.7
    fibv.inputs["To Min"].default_value = 0.45
    fibv.inputs["To Max"].default_value = 1.45
    nt.links.new(fib.outputs["Fac"], fibv.inputs["Value"])
    two = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "SMOOTH_MIN" if False else "MINIMUM",
                                                    math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", r, 0.32), 3.0), 1.0))
    two = math_node(nt, "MAXIMUM", math_node(nt, "MINIMUM", two, 1.0), 0.0)
    col = mix_rgb(nt, two, srgb_lin(outer), srgb_lin(inner))
    col = mix_rgb(nt, 1.0, col, fibv.outputs["Result"], "MULTIPLY")
    collar = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", 1.0, math_node(
        nt, "MINIMUM", math_node(nt, "ABSOLUTE", math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", r, 0.46), 14.0)), 1.0)), 0.25)
    col = mix_rgb(nt, collar, col, tuple(min(1.0, x * 1.6 + 0.02) for x in srgb_lin(inner)))
    limb = math_node(nt, "MAXIMUM", math_node(nt, "MINIMUM", math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", r, 0.80), 5.0), 1.0), 0.0)
    col = mix_rgb(nt, math_node(nt, "MULTIPLY", limb, 0.85), col, (0.012, 0.010, 0.009))
    pupil = math_node(nt, "LESS_THAN", r, 0.36 + (0.06 if iris == "black" else 0.0))
    col = mix_rgb(nt, pupil, col, (0.002, 0.002, 0.002))
    # sclera: warm/pink toward the corners (|u| large off the iris plane)
    au = math_node(nt, "ABSOLUTE", u)
    corner = math_node(nt, "MAXIMUM", math_node(nt, "MINIMUM", math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", au, 1.15), 1.1), 1.0), 0.0)
    scl = mix_rgb(nt, math_node(nt, "MULTIPLY", corner, 0.55), (0.70, 0.66, 0.62), (0.66, 0.42, 0.38))
    final = mix_rgb(nt, w, scl, col)
    nt.links.new(final, p.inputs["Base Color"])
    p.inputs["Subsurface Weight"].default_value = 0.25
    p.inputs["Subsurface Radius"].default_value = (1.0, 0.5, 0.4)
    p.inputs["Subsurface Scale"].default_value = 0.001
    rough = mix_rgb(nt, w, (0.28, 0.28, 0.28), (0.45, 0.45, 0.45))
    nt.links.new(rough, p.inputs["Roughness"])
    return m


def pattern_material(name, a_srgb, b_srgb, rough=0.6):
    """Ferengi brocade: a voronoi lattice of medallions over diagonal waves, two colours."""
    m, p = principled(name, srgb_lin(a_srgb), rough)
    nt = m.node_tree
    tc = node(nt, "ShaderNodeTexCoord")
    vo = node(nt, "ShaderNodeTexVoronoi")
    vo.inputs["Scale"].default_value = 30
    vo.inputs["Randomness"].default_value = 0.15
    wv = node(nt, "ShaderNodeTexWave")
    wv.inputs["Scale"].default_value = 60
    wv.inputs["Distortion"].default_value = 0.5
    nt.links.new(tc.outputs["Generated"], vo.inputs["Vector"])
    nt.links.new(tc.outputs["Generated"], wv.inputs["Vector"])
    mth = node(nt, "ShaderNodeMath")
    mth.operation = "MULTIPLY_ADD"
    nt.links.new(wv.outputs["Fac"], mth.inputs[0])
    mth.inputs[1].default_value = 0.05
    nt.links.new(vo.outputs["Distance"], mth.inputs[2])
    ramp = node(nt, "ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.17
    ramp.color_ramp.elements[1].position = 0.21
    ramp.color_ramp.elements[0].color = (*srgb_lin(b_srgb), 1)
    ramp.color_ramp.elements[1].color = (*srgb_lin(a_srgb), 1)
    nt.links.new(mth.outputs["Value"], ramp.inputs["Factor"])
    nt.links.new(ramp.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Sheen Weight"].default_value = 0.4
    return m


# outfit swatches (sRGB, roughness, metal, kind)
OUTFIT_MATS = {
    "kl_leather": ((0.12, 0.085, 0.07), 0.55, 0.0, "leather"),
    "kl_metal": ((0.24, 0.23, 0.22), 0.48, 1.0, "leather"),
    "kl_red": ((0.40, 0.07, 0.05), 0.5, 0.0, "leather"),
    "rom_cloth": ((0.30, 0.35, 0.31), 0.8, 0.0, "cloth"),
    "rom_pad": ((0.40, 0.46, 0.40), 0.75, 0.0, "quilt"),
    "rom_trim": ((0.10, 0.17, 0.13), 0.6, 0.0, "cloth"),
    "rom_sash": ((0.16, 0.30, 0.24), 0.5, 0.0, "cloth"),
    "rom_metal": ((0.62, 0.64, 0.62), 0.3, 1.0, "metal"),
    "car_under": ((0.17, 0.15, 0.13), 0.7, 0.0, "cloth"),
    "car_armour": ((0.38, 0.34, 0.29), 0.42, 0.25, "leather"),
    "car_rib": ((0.27, 0.24, 0.20), 0.45, 0.25, "leather"),
    "fer_shirt": ((0.80, 0.70, 0.48), 0.7, 0.0, "cloth"),
    "fer_trim": ((0.85, 0.62, 0.22), 0.3, 1.0, "metal"),
    "teeth": ((0.80, 0.72, 0.52), 0.35, 0.0, "plain"),
    "gum": ((0.45, 0.16, 0.15), 0.4, 0.0, "plain"),
    "mouth": ((0.10, 0.03, 0.03), 0.6, 0.0, "plain"),
    "caruncle": ((0.78, 0.42, 0.40), 0.25, 0.0, "plain"),
    "trill_spot": ((0.26, 0.14, 0.075), 0.6, 0.0, "plain"),
    "silver": ((0.80, 0.80, 0.82), 0.22, 1.0, "metal"),
    "chain_gold": ((0.85, 0.66, 0.30), 0.25, 1.0, "metal"),
    "hair_core": ((0.05, 0.04, 0.035), 0.6, 0.0, "plain"),
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
    elif key == "Wet":
        m = wet_material()
    elif key in OUTFIT_MATS:
        rgb, rough, metal, kind = OUTFIT_MATS[key]
        m, p = principled(key, srgb_lin(rgb), rough, metal)
        if kind == "leather":
            bump(m, p, 260, 0.25, 0.0004)
        elif kind == "quilt":
            nt = m.node_tree
            tc = node(nt, "ShaderNodeTexCoord")
            wv = node(nt, "ShaderNodeTexWave")
            wv.wave_type = "BANDS"
            wv.bands_direction = "Z"
            wv.inputs["Scale"].default_value = 9
            nt.links.new(tc.outputs["Object"], wv.inputs["Vector"])
            b = node(nt, "ShaderNodeBump")
            b.inputs["Strength"].default_value = 0.9
            b.inputs["Distance"].default_value = 0.004
            nt.links.new(wv.outputs["Fac"], b.inputs["Height"])
            nt.links.new(b.outputs["Normal"], p.inputs["Normal"])
        elif kind == "cloth":
            bump(m, p, 1400, 0.15, 0.0002)
        if key == "teeth":
            p.inputs["Subsurface Weight"].default_value = 0.4
            p.inputs["Subsurface Scale"].default_value = 0.001
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
    F = CUR["F"]
    CHAR_MATS["Skin"] = skin_material("Skin_" + c["id"], c)
    hc = c.get("hair") or "darkbrown"
    grey = c.get("grey", 0.0)
    CHAR_MATS["HairS"] = hair_material("Hair_" + c["id"], hc, grey)
    CHAR_MATS["BrowS"] = hair_material("Brow_" + c["id"], hc, grey * 0.6, 0.4, darker=1.12)
    CHAR_MATS["LashS"] = hair_material("Lash_" + c["id"], hc if c["species"] == "andorian" else "black", 0.0, 0.4)
    CHAR_MATS["BeardS"] = hair_material("Beard_" + c["id"], hc, min(1.0, grey * 1.1 + 0.05 * F["age"]), 0.38, darker=0.95)
    CHAR_MATS["eye"] = eye_material("Eye_" + c["id"], c["iris"])
    mel = HC[hc][0]
    core = tuple(0.5 * x for x in (0.30, 0.20, 0.13)) if mel > 0.5 else (0.35, 0.28, 0.2) if mel > 0.2 else (0.75, 0.75, 0.78)
    m, _ = principled("HairCore_" + c["id"], srgb_lin(core), 0.6)
    CHAR_MATS["hair_core"] = m
    if c.get("dept"):
        m, _ = principled("Dept_" + c["id"], srgb_lin(tuple(x * 0.9 for x in hexrgb(DEPTS[c["dept"]]))), 0.72)
        bump(m, m.node_tree.nodes["Principled BSDF"], 1500, 0.12, 0.0002)
        CHAR_MATS["Owner"] = m
    else:
        m, _ = principled("Owner_" + c["id"], srgb_lin(FACTION_COLOUR[c["faction"]]), 0.5)
        CHAR_MATS["Owner"] = m


# ---------------------------------------------------------------- skin colour zones

def skin_masks(Q, LA=None, LO=None):
    """Per-vertex tint (RGB multiplier of the skin colour) and skm (oil, freckle zone, age-spot zone) from head-local
    rest positions Q (N, 3); LA/LO (head grid only) for the scalp under the hair."""
    F = CUR["F"]
    sp = F["species"]
    c = CUR
    x, y, z = Q[:, 0], Q[:, 1], Q[:, 2]
    ax = np.abs(x)
    fy = sm(0.02, 0.06, y)
    ez, ex, mz, sz, tz, cz, bz = F["ez"], F["eye_x"], F["mz"], F["sz"], F["tz"], F["cz"], F["bz"]
    sk = np.array(SK[c["skin"]])
    alien = sp in ("andorian", "cardassian")
    tint = np.ones((len(Q), 3))

    def toward(col, amt):
        nonlocal tint
        col = np.asarray(col, float)
        tint = tint * (1 - amt[:, None]) + tint * col[None, :] * amt[:, None]
    # warmth: cheeks, nose, chin, ears (or species flush)
    flush = np.array({"andorian": (0.90, 0.84, 1.08), "cardassian": (1.07, 0.96, 0.84), "klingon": (1.07, 0.88, 0.84),
                      "tellarite": (1.08, 0.80, 0.78)}.get(sp, (1.05, 0.73, 0.71)))
    cheek = ga(ax - 0.040, 0.016) * ga(z - (ez - 0.028), 0.018) * fy
    nose = ga(x, 0.010) * ga(z - (tz + 0.002), 0.009) * fy
    chin = ga(x, 0.014) * ga(z - cz, 0.01) * fy
    ear = sm(0.068, 0.078, ax) * ga(z - 0.0, 0.03) * sm(0.02, -0.03, y)
    warm = np.clip(1.0 * cheek * (0.75 + 0.5 * F["smile"]) + 0.8 * nose + 0.4 * chin + 0.9 * ear, 0, 1)
    toward(flush, warm)
    # lips
    zm, zu, zl, ulh, llh, u = lips_at(x, F)
    up = sm(zu + ulh + 0.0006, zu + ulh - 0.0004, z) * sm(zu - 0.0003, zu + 0.0003, z)
    lo_ = sm(zl - llh - 0.0008, zl - llh + 0.0005, z) * sm(zl + 0.0003, zl - 0.0003, z)
    lip = np.clip(up + lo_, 0, 1) * sm(1.06, 0.9, u) * sm(0.03, 0.06, y)
    lipc = {"andorian": (0.78, 0.74, 0.98), "cardassian": (0.86, 0.72, 0.70), "ferengi": (0.92, 0.66, 0.62),
            "klingon": (0.8, 0.6, 0.6)}.get(sp, (0.90, 0.62, 0.60) if sk[0] > 0.6 else (0.78, 0.62, 0.62))
    if F["fem"] and sp in ("human", "trill", "betazoid", "bajoran", "romulan", "cardassian", "ferengi"):
        lipc = tuple(v * w for v, w in zip(lipc, (0.98, 0.86, 0.88)))
    toward(lipc, lip * 0.95)
    if F["open_mouth"] > 0:
        cav = sm(zl - 0.0002, zl + 0.0010, z) * sm(zu + 0.0002, zu - 0.0010, z) * sm(1.0, 0.85, u) * sm(0.02, 0.05, y)
        toward((0.25, 0.08, 0.08), cav)
    # eyelids, under-eyes, inner corners, nostrils
    lid = ga(ax - ex, 0.012) * ga(z - (ez + 0.006), 0.005) * fy
    under = ga(ax - (ex - 0.002), 0.011) * ga(z - (ez - 0.012), 0.0045) * fy
    toward((0.93, 0.80, 0.84) if not alien else (0.85, 0.85, 0.95), np.clip(0.6 * lid, 0, 1))
    toward((0.86, 0.80, 0.86) if not alien else (0.85, 0.85, 0.92), np.clip((0.45 + 0.4 * F["age"]) * under, 0, 1))
    nos = np.exp(-0.5 * (((ax - F["nose_w"] * 0.36) / 0.0028) ** 2 + ((z - (sz + 0.0024)) / 0.0019) ** 2)) * fy
    if F["nose"] == "snout":
        nos = np.exp(-0.5 * (((ax - 0.0068) / 0.0028) ** 2 + ((z - (tz + 0.0005)) / 0.0036) ** 2)) * fy
    toward((0.25, 0.14, 0.13), np.clip(nos * 1.2, 0, 1))
    # beard shadow (men) / stubble zone
    bz_ = beard_zone(Q)
    if F["fem"] == 0 and sp not in ("andorian", "ferengi", "cardassian"):
        hcol = HC[c.get("hair") or "darkbrown"][0]
        amt = bz_ * (0.30 if c.get("beard") else 0.18) * hcol
        toward((0.80, 0.80, 0.86), amt)
    # crease shading: deeper folds a touch darker
    if sp == "cardassian":
        ridge = sm(0.002, 0.0, np.abs(z - (bz + 0.0395)) - 0.024) * ga(x, 0.03) * fy
        toward((0.9, 0.82, 0.7), 0.3 * ridge)
    # scalp under the hair
    if LA is not None and c.get("style") and not NO_HAIR:
        cover = scalp_cover(LA, LO)
        hm = HC[c.get("hair") or "darkbrown"]
        hrgb = np.array(hair_rgb_guess(hm, c.get("grey", 0.0)))
        rel = np.clip(hrgb / np.maximum(sk, 0.02), 0.05, 3.0)
        n_ = len(LA)
        t0 = tint[:n_]
        t0 = t0 * (1 - 0.8 * cover[:, None]) + rel[None, :] * 0.8 * cover[:, None]
        tint[:n_] = t0
    # oil (T-zone), freckle zone, age-spot zone
    tzone = np.clip(ga(x, 0.022) * ga(z - (bz + 0.02), 0.022) + ga(x, 0.009) * ga(z - (tz + 0.012), 0.02)
                    + 0.6 * ga(x, 0.014) * ga(z - cz, 0.01), 0, 1) * fy
    tzone = np.maximum(tzone, 0.9 * lip)
    frz = np.clip(ga(ax - 0.022, 0.026) * ga(z - (ez - 0.022), 0.018) + 0.6 * ga(x, 0.01) * ga(z - (tz + 0.01), 0.016)
                  + 0.25 * ga(z - (bz + 0.02), 0.02), 0, 1) * fy
    agez = np.clip(0.6 * ga(z - (bz + 0.025), 0.03) + 0.7 * ga(ax - 0.05, 0.02) * ga(z - (ez - 0.01), 0.025), 0, 1) * fy
    skm = np.stack([tzone, frz, agez, np.ones_like(x)], -1)
    return np.concatenate([tint, np.ones((len(Q), 1))], -1), skm


def hair_rgb_guess(h, grey):
    mel, red, tint = h
    base = np.array([0.55, 0.36, 0.20]) * (1 - mel) ** 1.6 + np.array([0.06, 0.04, 0.03])
    base = base * (1 + 0.3 * red * np.array([0.3, -0.1, -0.3]))
    if tint is not None:
        base = np.array(tint) * (0.25 + 0.6 * (1 - mel))
    return base * (1 - grey) + np.array([0.55, 0.55, 0.56]) * grey


def beard_zone(Q):
    """Where a man's beard grows (0..1), head-local positions."""
    F = CUR["F"]
    x, y, z = Q[:, 0], Q[:, 1], Q[:, 2]
    ax = np.abs(x)
    zm, zu, zl, ulh, llh, u = lips_at(x, F)
    sz, ez = F["sz"], F["ez"]
    # angle round the head from the front
    a = np.degrees(np.abs(np.arctan2(x, np.maximum(y, -0.2))))
    ztop = (sz - 0.0025) + 0.004 * sm(10, 26, a) - 0.004 * ga(a - 34, 8) + 0.032 * sm(42, 76, a) + 0.016 * sm(72, 90, a)
    m = sm(ztop + 0.004, ztop - 0.004, z) * sm(98, 86, a) * sm(-0.035, -0.015, y + 0.0)
    lipm = sm(zu + ulh + 0.0012, zu + ulh - 0.0005, z) * sm(zl - llh - 0.0015, zl - llh + 0.0003, z) * sm(1.1, 0.9, u)
    nosec = sm(F["nose_w"] + 0.004, F["nose_w"] - 0.002, ax) * sm(sz - 0.003, sz + 0.001, z)
    under = sm(-0.02, 0.0, z - (F["cz"] - 0.03))   # stop on the throat
    return np.clip(m * (1 - lipm) * (1 - nosec) * under, 0, 1)


# ---------------------------------------------------------------- meshes

def make_obj(name, mb, rig, attrs=True, weights=True, tint_fn=None):
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
    co = np.array([tuple(c) for c in mb.co]) if mb.co else np.zeros((0, 3))
    if attrs and len(co):
        Q = co - HC_NP
        if tint_fn:
            tint, skm = tint_fn(Q)
        else:
            tint = np.ones((len(co), 4))
            skm = np.zeros((len(co), 4))
        set_attr(me, "tint", tint, "FLOAT_COLOR")
        set_attr(me, "skm", skm, "FLOAT_COLOR")
        set_attr(me, "rest", Q, "FLOAT_VECTOR")
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


def set_attr(me, name, arr, kind):
    if kind == "FLOAT_COLOR":
        a = me.color_attributes.new(name, "FLOAT_COLOR", "POINT")
        a.data.foreach_set("color", np.asarray(arr, np.float32).ravel())
    else:
        a = me.attributes.new(name, kind, "POINT")
        a.data.foreach_set("vector", np.asarray(arr, np.float32).ravel())


def hd_lats():
    out = [-87, -83, -79, -75, -71, -67, -63, -59, -56]
    out += list(np.round(np.arange(-54, -46, 0.6), 3))
    out += list(np.round(np.arange(-46, 36, 0.32), 3))
    out += list(np.round(np.arange(36, 60, 0.6), 3))
    out += [61.5, 63, 65, 67, 69, 71, 73.5, 76, 78.5, 81, 83.5, 86, 88]
    return sorted(set(float(v) for v in out))


HD_LONS = 300


def head_grid_obj(name, rig):
    """The head as one dense grid mesh (numpy), weighted to the Head bone, with tint/skm/rest attributes."""
    lats = np.array(hd_lats())
    lons = np.array(bc.head_lons(HD_LONS))
    LA, LO = np.meshgrid(lats, lons, indexing="ij")
    P = head_xyz(LA.ravel(), LO.ravel())
    nl, nj = len(lats), len(lons)
    bot = head_xyz(np.array([-90.0]), np.array([90.0]))
    top = head_xyz(np.array([90.0]), np.array([90.0]))
    verts = np.concatenate([P, bot, top]) + HC_NP
    ib, it = nl * nj, nl * nj + 1
    idx = np.arange(nl * nj).reshape(nl, nj)
    a = idx[:-1, :]
    b = np.roll(idx[:-1, :], -1, axis=1)
    c_ = np.roll(idx[1:, :], -1, axis=1)
    d = idx[1:, :]
    quads = np.stack([a, b, c_, d], -1).reshape(-1, 4)
    # orientation: outward normal; check one quad at the front
    tris_b = np.stack([idx[0], np.full(nj, ib), np.roll(idx[0], -1)], -1)
    tris_t = np.stack([idx[-1], np.roll(idx[-1], -1), np.full(nj, it)], -1)
    me = bpy.data.meshes.new(name)
    faces = [tuple(q) for q in quads] + [tuple(t) for t in tris_b] + [tuple(t) for t in tris_t]
    me.from_pydata(verts.tolist(), [], faces)
    me.materials.append(material("Skin"))
    me.validate(verbose=False)
    me.update()
    # flip if facing in
    me.calc_normals_split() if hasattr(me, "calc_normals_split") else None
    pv = me.polygons[len(quads) // 2]
    cen = V(pv.center) - HEAD_C
    if pv.normal.dot(cen) < 0:
        me.flip_normals()
    me.shade_smooth()
    Q = verts - HC_NP
    LAf = np.concatenate([LA.ravel(), [-90.0, 90.0]])
    LOf = np.concatenate([LO.ravel(), [90.0, 90.0]])
    tint, skm = skin_masks(Q, LAf, LOf)
    set_attr(me, "tint", tint, "FLOAT_COLOR")
    set_attr(me, "skm", skm, "FLOAT_COLOR")
    set_attr(me, "rest", Q, "FLOAT_VECTOR")
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = rig
    vg = ob.vertex_groups.new(name="Head")
    vg.add(list(range(len(verts))), 1.0, "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    return ob


RIG_HEAD = [("Head", 0.0)]


# ---------------------------------------------------------------- eyes and lids

def eye_centre(sx):
    F = CUR["F"]
    r = F["eye_r"]
    ex, ez = sx * F["eye_x"], F["ez"] + F["a_eye"] * sx
    return V((ex, hl(ex, ez) + F["sock"] * 0.98 - 0.0104 * r / 0.0118, HEAD_C.z + ez)), r


def lid_edges(sx):
    """Upper/lower lid opening (deg on the eyeball) as functions of lon (16 = +x corner, 166 = -x corner)."""
    F = CUR["F"]
    o = F["open"]
    sq = F["squint"]
    up = 23.0 * o * (1 - 0.12 * sq) - 2.5 * F["age"] - (3.0 if F["monolid"] else 0.0)
    lo_ = -27.0 * o * (1 - 0.30 * sq) + 1.0 * F["age"]
    tilt = F["tilt"]

    def outer(lo):
        t = clamp((lo - 16) / 150, 0, 1)
        return (1 - t) if sx > 0 else t

    def edge(lo, which):
        t = clamp((lo - 16) / 150, 0, 1)
        oo = outer(lo)
        if which > 0:
            # apex a little medial; the outer third flatter (hooding with age)
            shape = math.sin(pi * t) ** 0.55 * (1 + 0.12 * (0.5 - oo)) * (1 - 0.10 * F["hood"] * smooth(0.4, 0.9, oo))
            e = up * shape
        else:
            shape = math.sin(pi * t) ** 0.8 * (1 + 0.10 * (oo - 0.5))
            e = lo_ * shape
        return e + tilt * (oo - 0.5)
    return edge


def lids(mb, sx):
    """Lid shells round the eyeball with an almond opening, a rounded margin, an upper-lid fold; caruncle; wet line.
    Returns the upper / lower lash lines (rest positions) for the lash strands."""
    F = CUR["F"]
    c, r = eye_centre(sx)
    edge = lid_edges(sx)

    def pt(la, lo, rr):
        return c + V((math.cos(rad(la)) * math.cos(rad(lo)), math.cos(rad(la)) * math.sin(rad(lo)), math.sin(rad(la)))) * rr
    lons = [2 + 176 * j / 44 for j in range(45)]
    fold = 0.0 if F["monolid"] else 0.0016 * (0.6 + 0.6 * F["hood"])
    for which, top in ((1, 86), (-1, -86)):
        rows = []
        prof = ((0.0, 0.00035), (0.0, 0.0011), (0.03, 0.0016), (0.10, 0.0019), (0.22, 0.0018), (0.34, 0.0016),
                (0.46, 0.0018), (0.6, 0.0013), (0.8, 0.0009), (1.0, 0.0006))
        for f, dr in prof:
            if which > 0:
                dr = dr + fold * math.exp(-((f - 0.40) / 0.08) ** 2) - 0.0005 * math.exp(-((f - 0.30) / 0.05) ** 2)
            else:
                dr = dr + 0.0005 * math.exp(-((f - 0.12) / 0.06) ** 2) + F["bags"] * 0.6 * math.exp(-((f - 0.5) / 0.2) ** 2)
            row = [pt(lerp(edge(lo, which), top, f), lo, r + dr) for lo in lons]
            bw = smooth(0.22, 0.8, f)
            if bw > 0:
                xs = np.array([q.x for q in row])
                zs = np.array([q.z - HEAD_C.z for q in row])
                sy = CUR["grid"].y(xs, zs) + HEAD_C.y - 0.0004
                row = [V((q.x, max(q.y, lerp(q.y, float(yy), bw)) if yy > q.y else q.y, q.z)) for q, yy in zip(row, sy)]
            rows.append(row)
        bc.sheet(mb, rows, "Skin", RIG_HEAD, outward=lambda m: m - c)
    # caruncle at the inner corner
    inner_lo = 166 if sx > 0 else 16
    pc = pt(edge(inner_lo, 1) * 0.3 - 1.0, inner_lo + (-3 if sx > 0 else 3), r * 0.97)
    pc = pc + V((sx * -0.0012, 0.0006, 0))
    bc.ellipsoid(mb, pc, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.0016, 0.0014, 0.0019), 10, 6, "caruncle", RIG_HEAD)
    # wet line along the lower lid
    wl = [pt(edge(lo, -1) + 1.4, lo, r + 0.00045) for lo in lons[3:-3]]
    loft(mb, wl, lambda t: 0.00028, "Wet", RIG_HEAD, segs=6)
    up_line = [(pt(edge(lo, 1) - 0.4, lo, r + 0.0010), (pt(edge(lo, 1) - 0.4, lo, r + 0.0010) - c).normalized(), lo)
               for lo in np.linspace(20, 162, 60)]
    lo_line = [(pt(edge(lo, -1) + 0.6, lo, r + 0.0009), (pt(edge(lo, -1) + 0.6, lo, r + 0.0009) - c).normalized(), lo)
               for lo in np.linspace(28, 154, 40)]
    return c, up_line, lo_line


def eyeball(mb_list, sx, Dh, cam):
    """Sclera + recessed iris plane + pupil (one 'eye' material, attribute eyep) and a cornea dome; world space,
    aimed at the camera."""
    F = CUR["F"]
    ce, r = eye_centre(sx)
    r = r * HEAD_SCALE
    cw = Dh @ ce
    g = (cam - cw).normalized()
    # a slight convergence error makes the gaze less glassy
    up = Dh.to_3x3() @ V((0, 0, 1))
    e1 = orth(up, g).cross(g).normalized()
    e2 = g.cross(e1)
    iris_la = 57.5 if F["species"] != "betazoid" else 50.0
    zi = r * math.sin(rad(iris_la))
    ri = r * math.cos(rad(iris_la))
    nla, nlo = 60, 64
    verts, ep = [], []
    las = [-89 + 179 * i / (nla - 1) for i in range(nla)]
    for la in las:
        for j in range(nlo):
            lo = 2 * pi * j / nlo
            cl, sl = math.cos(rad(la)), math.sin(rad(la))
            u, v = cl * math.cos(lo), cl * math.sin(lo)
            if la > iris_la:
                rr = (cl / math.cos(rad(iris_la)))
                zz = zi - 0.00035 * (1 - rr ** 2)
                p = (u * r, v * r, zz)
                w = 1.0
            else:
                p = (u * r, v * r, sl * r)
                w = 0.0
            verts.append(cw + e1 * p[0] + e2 * p[1] + g * p[2])
            ep.append((p[0] / ri, p[1] / ri, w, 1.0))
    mb = bc.MB()
    rows = [[mb.v(verts[i * nlo + j]) for j in range(nlo)] for i in range(nla)]
    for i in range(nla - 1):
        for j in range(nlo):
            k = (j + 1) % nlo
            mb.f((rows[i][j], rows[i][k], rows[i + 1][k], rows[i + 1][j]), "eye")
    tip = mb.v(cw + g * (zi - 0.00035))
    ep.append((0.0, 0.0, 1.0, 1.0))
    for j in range(nlo):
        mb.f((rows[-1][j], rows[-1][(j + 1) % nlo], tip), "eye")
    bot = mb.v(cw - g * r)
    ep.append((0.0, 0.0, 0.0, 1.0))
    for j in range(nlo):
        mb.f((rows[0][(j + 1) % nlo], rows[0][j], bot), "eye")
    mb_list.append(("eye", mb, ep))
    # cornea: a dome over the iris blending into a sphere a hair bigger than the eyeball
    cm = bc.MB()
    rows = []
    for i in range(40):
        la = -60 + 150 * i / 39
        row = []
        for j in range(48):
            lo = 2 * pi * j / 48
            cl, sl = math.cos(rad(la)), math.sin(rad(la))
            rr = r * 1.012
            bulge = 0.0021 * smooth(44, 88, la) ** 1.2
            p = cw + (e1 * (cl * math.cos(lo)) + e2 * (cl * math.sin(lo)) + g * sl) * rr + g * bulge
            row.append(cm.v(p))
        rows.append(row)
    for i in range(39):
        for j in range(48):
            k = (j + 1) % 48
            cm.f((rows[i][j], rows[i][k], rows[i + 1][k], rows[i + 1][j]), "Cornea")
    tipc = cm.v(cw + g * (r * 1.012 + 0.0021))
    for j in range(48):
        cm.f((rows[-1][j], rows[-1][(j + 1) % 48], tipc), "Cornea")
    mb_list.append(("cornea", cm, None))


def mb_object(name, mb, ep=None):
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
    if ep is not None:
        set_attr(me, "eyep", np.array(ep), "FLOAT_COLOR")
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ---------------------------------------------------------------- ears

def ear_root(sx):
    F = CUR["F"]
    lo = -5.0 if sx > 0 else 185.0
    la = -3.0 + math.degrees(math.asin(clamp(F["ear_dz"] + F["a_ear"] * sx, -0.5, 0.5) / 0.118))
    p = head_xyz(np.array([la]), np.array([lo]))[0]
    return HEAD_C + V(p.tolist())


def ear_frame(sx, kind):
    F = CUR["F"]
    root = ear_root(sx)
    if kind == "ferengi":
        b = rad(50)
        ax = V((sx * math.cos(b), math.sin(b), 0.0))            # the shell faces forward and out
        ay = V((sx * math.sin(b), -math.cos(b), 0.0))           # its long axis runs outward and back
        az = orth(V((0, -0.18, 1)), ax)
        return root, ax, ay, az
    out = rad(18 + (F["ear_out"] - 12))
    ax = V((sx * math.cos(out), -math.sin(out), 0))
    az = V((0, -math.sin(rad(10)), math.cos(rad(10))))
    ay = orth(az.cross(ax) * sx, ax)
    return root, ax, ay, az


def ears(mb, kind="human"):
    F = CUR["F"]
    scale = F["ear"] * (0.9 if F["fem"] else 1.0)
    out = {}
    for sx in (-1, 1):
        root, ax, ay, az = ear_frame(sx, kind)
        if kind == "ferengi":
            out[sx] = ferengi_ear(mb, sx, root, ax, ay, az, 1.0 if not F["fem"] else 0.86)
            continue
        radii = (0.009 * scale, 0.019 * scale, 0.031 * scale)
        cc = root + ax * 0.002 - ay * 0.002

        def ear(la, lo, p, cc=cc, ax=ax, ay=ay, az=az, radii=radii):
            rel = p - cc
            out_ = rel.dot(ax)
            r = math.cos(rad(la))
            if out_ > 0:
                p = p - ax * out_ * 0.85 * smooth(0.55, 0.0, abs(la) / 90.0) * (1 if r > 0.4 else 0.4)
            up = rel.dot(az) / radii[2]
            fw = rel.dot(ay) / radii[1]
            if kind == "pointed":
                tip = smooth(0.15, 1.0, up)
                p = p + az * 0.019 * scale * tip ** 2 - ay * (0.009 * tip ** 2 + 0.006 * fw * tip) * scale
            # concha: the bowl in front of the canal
            p = p - ax * 0.0035 * scale * math.exp(-((up + 0.1) / 0.35) ** 2 - ((fw - 0.05) / 0.4) ** 2) * (1 if out_ > 0 else 0)
            # lobe a little fuller and lower with age
            p = p + az * (-0.003 * F["age"]) * smooth(-0.5, -1.0, up)
            return p
        bc.ellipsoid(mb, cc, (ax, ay, az), radii, 32, 18, "Skin", RIG_HEAD, deform=ear)
        out[sx] = e = (cc, ax, ay, az, radii, ear)
        rim_pts = [ear_pt(e, la, 90) for la in range(30, 90, 6)] + [ear_pt(e, la, -90) for la in range(89, -56, -6)]
        rim_pts = [p + ax * 0.0016 for p in rim_pts]
        loft(mb, rim_pts, lambda t: 0.0023 * scale * (0.6 + 0.4 * math.sin(pi * clamp(t * 1.1, 0, 1))), "Skin", RIG_HEAD, segs=10)
        inner = [ear_in(e, la, -90, 0.62) + ax * 0.0014 for la in range(60, -36, -8)]
        loft(mb, inner, lambda t: 0.0017 * scale * math.sin(pi * (0.1 + 0.8 * t)), "Skin", RIG_HEAD, segs=8)
        trag = ear_in(e, -20, 90, 0.55) + ax * 0.002
        bc.ellipsoid(mb, trag, (ax, ay, az), (0.0025 * scale, 0.003 * scale, 0.004 * scale), 10, 6, "Skin", RIG_HEAD)
    return out


def ferengi_ear(mb, sx, root, ax, ay, az, k):
    """Big Ferengi ear: a cupped shell opening forward, wider than tall, with concentric ridges, radial folds and a
    lobed, rolled rim; the inner edge sinks into the skull."""
    C = root + ay * 0.032 * k + az * 0.006 * k + ax * 0.006
    A, B = 0.046 * k, 0.050 * k      # half sizes along ay (out-back) and az (up)

    def outline(a):
        return 1.0 + 0.08 * abs(math.sin(3.5 * a + 0.6)) + 0.03 * math.sin(9 * a + 1.3) - 0.15 * smooth(0.3, 1.0, -math.cos(a))

    def surf(u, a, side):
        R = outline(a)
        pl = ay * (math.cos(a) * A * R * u) + az * (math.sin(a) * B * R * u)
        cup = -0.014 * k * (1 - u * u) + 0.004 * k * u ** 4
        ridges = 0.0055 * k * math.sin(pi * u * 4.0 + 0.4 * math.sin(3 * a)) ** 2 * smooth(0.12, 0.35, u) * smooth(0.97, 0.80, u)
        folds = 0.0024 * k * math.sin(a * 7 + 2.1 * u) * smooth(0.3, 0.6, u) * smooth(1.0, 0.85, u)
        rimroll = 0.0042 * k * math.exp(-((u - 0.93) / 0.06) ** 2)
        thick = (0.0034 * k * (1 - u ** 4) ** 0.5 + 0.0016 * k)
        canal = -0.010 * k * math.exp(-((u * math.cos(a) + 0.55) ** 2 + (u * math.sin(a) + 0.05) ** 2) / 0.05)
        d = cup + ridges + folds + rimroll + (canal if side > 0 else 0.0)
        return C + pl + ax * (d + side * thick)
    na = 72
    rows = []
    for i in range(18):                      # front: centre -> rim
        u = 0.02 + 0.98 * i / 17
        rows.append([surf(u, 2 * pi * j / na, 1) for j in range(na)])
    for i in range(1, 12):                   # back: rim -> centre
        u = 1.0 - 0.98 * i / 11
        rows.append([surf(u, 2 * pi * j / na, -1) for j in range(na)])
    idx = bc.sheet(mb, rows, "Skin", RIG_HEAD, closed=True, outward=lambda m: (m - C) + ax * (m - C).dot(ax) * 2.0)
    bc.fan(mb, idx[0], surf(0.0, 0, 1), "Skin", RIG_HEAD, outward=ax)
    bc.fan(mb, idx[-1], surf(0.0, 0, -1), "Skin", RIG_HEAD, outward=-ax)
    return (C, ax, ay, az, (A, B))


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


# ---------------------------------------------------------------- extras

def bead_chain(mb, pts, r, mat, cand):
    for i, p in enumerate(pts):
        rr = r * (1.0 if i % 2 == 0 else 0.75)
        bc.ellipsoid(mb, p, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (rr, rr, rr), 6, 3, mat, cand)


def bajoran_earring(mb, e):
    """Original ear chain: a cuff on the upper rim, a chain down the back of the ear to the lobe, a hanging strand."""
    cuff = [ear_pt(e, la, -70, 0.0012) for la in range(30, 80, 6)]
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


def ferengi_teeth(mb):
    """Jagged pointed teeth in the parted grin: an upper row hanging from the gum line, a lower row behind."""
    F = CUR["F"]
    gr = CUR["grid"]
    xs = np.linspace(-F["mouth_w"] * 0.82, F["mouth_w"] * 0.82, 13)
    zm, zu, zl, ulh, llh, u = lips_at(xs, F)
    for i, x in enumerate(xs):
        frontx = float(gr.y(x, zu[i] + 0.002)) - 0.0028 - 0.06 * x * x / 0.02
        w = 0.0016 + 0.0003 * ((i * 7) % 3)
        L = (zu[i] - zl[i]) * (0.75 + 0.2 * ((i * 5) % 3) / 2)
        base = V((x, HEAD_C.y + frontx, HEAD_C.z + zu[i] + 0.0015))
        tip = base + V((0.0002 * ((i % 3) - 1), 0.0006, -L))
        bc.tube(mb, base, tip, w, 0.00025, 7, "teeth", RIG_HEAD, caps=(True, True))
        if 2 <= i <= 10 and i % 2 == 0:
            lb = V((x + 0.0012, HEAD_C.y + frontx - 0.0012, HEAD_C.z + zl[i] - 0.0015))
            bc.tube(mb, lb, lb + V((0, 0.0004, (zu[i] - zl[i]) * 0.55)), w * 0.85, 0.0002, 7, "teeth", RIG_HEAD)
    # gums and a dark back wall
    row = []
    for x in np.linspace(-F["mouth_w"], F["mouth_w"], 21):
        zm1, zu1, zl1, *_ = lips_at(np.array([x]), F)
        y = float(gr.y(x, zm1[0] + 0.006)) - 0.0035 - 0.07 * x * x / 0.02
        row.append(V((x, HEAD_C.y + y, HEAD_C.z + zu1[0] + 0.0016)))
    up_ = [p + V((0, -0.0004, 0.004)) for p in row]
    bc.sheet(mb, [row, up_], "gum", RIG_HEAD, outward=lambda m: V((0, 1, 0)))
    back = [V((p.x * 0.9, p.y - 0.008, p.z - 0.012)) for p in row]
    back2 = [V((p.x * 0.9, p.y - 0.008, p.z + 0.006)) for p in row]
    bc.sheet(mb, [back, back2], "mouth", RIG_HEAD, outward=lambda m: V((0, 1, 0)))


def antennae(mb):
    body = CUR["body"]
    for sx in (-1, 1):
        lo = 90 - sx * 27
        la = 57
        base = bc.head_pt(la, lo, body, 0.002)
        n = bc.head_n(la, lo, body)
        up = V((0, 0, 1))
        pts = []
        for i in range(25):
            t = i / 24
            p = base + n * (0.022 * t) + up * (0.135 * t ** 1.05) + V((sx * 0.034 * t ** 1.5, 0.016 * math.sin(pi * t) - 0.016 * t ** 2, 0))
            pts.append(p)
        fk = 0.88 if CUR["sex"] == "f" else 1.0
        radius = lambda t: (0.0085 - 0.0042 * t + 0.0007 * math.sin(t * 40) ** 2) * fk  # noqa: E731
        loft(mb, pts, radius, "Skin", RIG_HEAD)
        tip = pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.003
        r = 0.0068 * fk
        bc.ellipsoid(mb, tip, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (r, r, r * 1.25), 14, 7, "Skin", RIG_HEAD)
        base_in = base - n * 0.01
        bc.tube(mb, base_in, base + n * 0.003, 0.0105, 0.0085, 16, "Skin", RIG_HEAD, caps=(False, False))


def trill_spots(mb):
    body = CUR["body"]
    nl = bc.neck_limb(body)
    ncand = [("Neck", 0), ("Head", 0.012), ("UpperChest", 0.01)]
    for sx in (-1, 1):
        def lon(ang):
            return 90 - sx * ang
        path = []
        for i in range(15):
            t = i / 14
            la = 40 - 85 * t
            ang = 60 + 26 * smooth(0.0, 1.0, t) + 8 * smooth(0.7, 1.0, t)
            path.append(("h", la, lon(ang)))
        for i in range(7):
            u = 0.85 - 0.75 * i / 6
            th = (0 if sx > 0 else pi) - sx * rad(14)
            path.append(("n", u, th))
        for i, (kind, a, b) in enumerate(path):
            for row in (0, 1):
                if (i + row) % 2:
                    continue
                size = 0.0050 - 0.0018 * i / len(path)
                if kind == "h":
                    off = 2.2 * (1 if row else -1)
                    la, lo = a, b + off * sx * 0.7
                    p = bc.head_pt(la, lo, body, 0.0003)
                    n = bc.head_n(la, lo, body)
                    cand = RIG_HEAD
                else:
                    th = b + rad(10) * (1 if row else -1) * sx
                    p = nl.pt(a, th, 0.0003)
                    n = nl.n(a, th)
                    cand = ncand
                t1 = orth(V((0, 0, 1)), n)
                t2 = n.cross(t1)
                jit = 0.8 + 0.4 * ((i * 37 + row * 11) % 7) / 6
                bc.ellipsoid(mb, p, (t1, t2, n), (size * jit, size * 0.85 * jit, 0.0002), 12, 3, "trill_spot", cand)


def neck_ridges(mb):
    """Cardassian neck ridges: from behind each ear down the side of the neck, flaring onto the shoulders like a
    cobra's hood, with a second smaller ridge in front."""
    body = CUR["body"]
    nl = bc.neck_limb(body)
    cand = [("Neck", 0), ("Head", 0.01), ("UpperChest", 0.0)]
    for sx in (-1, 1):
        th_side = 0 if sx > 0 else pi
        for k_, (lo_off, th_off, wk, hk) in enumerate(((112, 0, 1.0, 1.0), (96, 24, 0.55, 0.6))):
            pts, ns = [], []
            lo = 90 - sx * lo_off
            for la in (-4, -16, -28):
                pts.append(bc.head_pt(la, lo, body))
                ns.append(bc.head_n(la, lo, body))
            for u, thd in ((0.85, 26), (0.6, 16), (0.35, 6), (0.12, -4)):
                th = th_side - sx * rad(thd - th_off)
                pts.append(nl.pt(u, th))
                ns.append(nl.n(u, th))
            for z, thd in ((1.51, -10), (1.49, -14)):
                th = th_side - sx * rad(thd - th_off)
                pts.append(body.torso(z, th, 0.004))
                ns.append(body.torso_n(z, th))
            rows, cen = [], []
            n = len(pts)
            for i, (p, nn) in enumerate(zip(pts, ns)):
                t = i / (n - 1)
                d = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
                side = nn.cross(d).normalized()
                w = (0.008 + 0.022 * t ** 1.1) * wk
                h = (0.008 + 0.010 * t) * hk
                rows.append([p - nn * 0.003 + side * w * math.cos(pi * k / 16) + nn * h * math.sin(pi * k / 16) ** 1.2
                             + nn * 0.0035 * gauss(k / 16 - 0.5, 0.06) - nn * 0.0015 * gauss(k / 16 - 0.3, 0.05)
                             for k in range(17)])
                cen.append(p - nn * 0.01)
            bc.sheet(mb, rows, "Skin", cand, centres=cen)


# ---------------------------------------------------------------- hair strands (Cycles hair curves)

_HL = [(-90, -34), (-60, -28), (-30, -8), (0, 16), (12, 15), (30, 22), (60, 34), (90, 44)]
_HLZ = [p[0] for p in _HL]
_HLV = [p[1] for p in _HL]


def hairline(lo):
    """Hairline latitude by longitude (round the ears), per person: forehead height, temple recession, widow's peak."""
    F = CUR["F"]
    lo = np.asarray(lo, float)
    a = (lo - 90.0 + 180.0) % 360.0 - 180.0
    b = 90 - np.abs(a)
    h = np.interp(b, _HLZ, _HLV)
    h = h + F["fore_h"] * sm(20, 70, b)
    h = h + F["recede"] * (ga(np.abs(a) - 32, 13) + 0.35 * ga(a, 30))
    h = h - F["peak"] * ga(a, 5)
    st = STYLE.get("style")
    if st in ("bowl", "vulcan_f", "romulan_f", "bob", "pixie", "undercut", "fade", "crop", "slick", "slick_bun",
              "coils") or CUR.get("pointed"):
        # keep the hair edge above / behind the ears
        for e in (-9.0, 189.0):
            g = sm(28, 12, np.abs((lo - e + 180) % 360 - 180))
            h = np.maximum(h, 26.0 * g - 90 * (1 - g))
    if CUR["species"] == "klingon":
        h = h + 18 * sm(30, 80, b)
    return h


STYLE = {}


def scalp_cover(LA, LO):
    if not STYLE:
        return np.zeros_like(LA)
    h = hairline(LO)
    cov = sm(h - 1.0, h + 4.0, LA)
    if STYLE.get("shaved_sides"):
        b = 90 - np.abs((LO - 90.0 + 180.0) % 360.0 - 180.0)
        cov = cov * (0.35 + 0.65 * sm(STYLE["shaved_sides"] - 4, STYLE["shaved_sides"] + 2, LA + 0 * b))
    return cov


def sample_scalp(n, seed, hl_fn, extra=None):
    """n roots spread evenly by area over the scalp above hl_fn(lo) (soft, feathered hairline)."""
    R = rng(seed)
    out_la, out_lo, out_w = [], [], []
    need = n
    while need > 0:
        m = need * 4
        lo = R.uniform(0, 360, m)
        u = R.uniform(math.sin(rad(-60)), 1.0, m)
        la = np.degrees(np.arcsin(u))
        h = hl_fn(lo) + R.normal(0, 0.7, m)
        keep = R.uniform(0, 1, m) < sm(h - 1.0, h + 3.5, la)
        if extra is not None:
            keep &= extra(la, lo)
        la, lo = la[keep], lo[keep]
        w = sm(h[keep] - 1.0, h[keep] + 4.5, la)        # 0 at the very edge: fine, short baby hairs
        out_la.append(la[:need])
        out_lo.append(lo[:need])
        out_w.append(w[:need])
        need -= len(la[:need])
    return np.concatenate(out_la), np.concatenate(out_lo), np.concatenate(out_w)


def tangent(v, p):
    n = nrm(p)
    t = v - n * np.sum(v * n, -1, keepdims=True)
    return t


def comb_dir(p, mode, st):
    """Unnormalised comb direction at head-local points p (N, 3)."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    if mode == "whorl":
        W = np.array(st.get("whorl", (0.0, -0.045, 0.11)))
        v = p - W
        v[:, 2] -= 0.02
        return v
    if mode == "back":
        return np.stack([x * 0.15, -np.ones_like(x), -0.35 - 0.6 * sm(-0.02, -0.07, y)], -1)
    if mode == "part":
        phi = st["part"]
        lo = np.degrees(np.arctan2(y, x)) % 360
        s = np.sign((lo - phi + 180) % 360 - 180)
        s = np.where(s == 0, 1.0, s)
        elon = np.stack([-np.sin(np.radians(lo)), np.cos(np.radians(lo)), np.zeros_like(lo)], -1) * s[:, None]
        back = np.array([0.0, -0.28, -0.25])
        w = sm(-0.02, -0.06, y)[:, None]                 # behind the crown: falls down
        return elon * (1 - w) + back * (1 - w) + w * np.array([0.0, -0.3, -1.0])
    if mode == "up":
        return np.stack([x * 0.3, 0.1 + 0 * x, np.ones_like(x)], -1)
    if mode == "forward":
        return np.stack([x * 0.4, np.ones_like(x) * 0.8, -np.ones_like(x) * 0.5], -1)
    return np.tile([0.0, 0.0, -1.0], (len(p), 1))


def ear_push(p, gap=0.004):
    F = CUR["F"]
    for sx in (-1, 1):
        r = np.array(tuple(ear_root(sx) - HEAD_C))
        c = r + np.array([sx * 0.006, -0.008, 0.0])
        sc = F["ear"] * (1.25 if CUR.get("pointed") else 1.0)
        rad_ = np.array([0.016, 0.026, 0.036 if not CUR.get("pointed") else 0.05]) * sc + gap
        q = (p - c) / rad_
        d = np.linalg.norm(q, axis=-1)
        inside = d < 1.0
        if inside.any():
            p[inside] = c + q[inside] / d[inside, None] * rad_
    return p


class BodyClear:
    """Rest-pose distance from the body axis to the neck / jumpsuit by height and direction (long hair)."""

    def __init__(self, gap):
        body = CUR["body"]
        self.zs = np.arange(1.0, 1.66, 0.01)
        self.ths = np.arange(0, 360, 6.0)
        self.r = np.zeros((len(self.zs), len(self.ths)))
        self.cy = np.zeros(len(self.zs))
        for i, z in enumerate(self.zs):
            if z >= 1.535:
                self.cy[i] = -0.012
                self.r[i, :] = 0.06 + 0.004 + CUR["F"].get("neck", 0.0)
                continue
            cy = body.section(z)[3]
            self.cy[i] = cy
            for j, th in enumerate(self.ths):
                pp = body.torso(z, rad(th), 0.0)
                self.r[i, j] = math.hypot(pp.x, pp.y - cy)
        self.r += gap

    def push(self, p):
        zw = p[:, 2] + HEAD_C.z
        sel = zw < 1.62
        if not sel.any():
            return p
        q = p[sel]
        zz = np.clip(zw[sel], self.zs[0], self.zs[-1])
        i = np.clip(((zz - self.zs[0]) / 0.01).astype(int), 0, len(self.zs) - 1)
        cy = self.cy[i] - HEAD_C.y
        th = np.degrees(np.arctan2(q[:, 1] - cy, q[:, 0])) % 360
        j = (np.round(th / 6.0).astype(int)) % len(self.ths)
        rmin = self.r[i, j]
        rr = np.hypot(q[:, 0], q[:, 1] - cy)
        k = np.where(rr < rmin, rmin / np.maximum(rr, 1e-6), 1.0)
        q[:, 0] *= k
        q[:, 1] = cy + (q[:, 1] - cy) * k
        p[sel] = q
        return p


def grow_guides(roots, normals, lens, st, field, bodyc, seed):
    """Grow guide strands: follow the comb field near the scalp in a volume layer, then gravity; collide with the
    head (radial field), ears and the body."""
    R = rng(seed)
    G = len(roots)
    S = st.get("S", 16)
    P = np.zeros((G, S + 1, 3))
    p = roots + normals * 0.0003
    P[:, 0] = p
    lift = st.get("lift", 0.006)
    layer = 0.0005 + lift * R.uniform(0, 1, G) ** 0.7
    grav = st.get("grav", 0.3)
    stiff = st.get("stiff", 0.6)
    mode = st.get("comb", "whorl")
    rise = st.get("rise", 0.25)
    d = nrm(tangent(comb_dir(p, mode, st), p))
    d = nrm(d + normals * rise)
    step = (lens / S)[:, None]
    walked = np.zeros(G)
    tie = st.get("tie")
    tied = np.zeros(G, bool)
    tie_at = np.full(G, S)
    for i in range(1, S + 1):
        t = i / S
        if tie is not None:
            T = np.array(tie)
            want = T - p
            dist = np.linalg.norm(want, axis=-1)
            newly = (~tied) & (dist < 0.009)
            tie_at[newly] = i
            tied |= newly
            want = nrm(tangent(want, p))
            if st.get("after") == "pony":
                hang = np.tile(nrm(np.array([0.0, -0.25, -1.0])), (G, 1))
                want = np.where(tied[:, None], hang, want)
            elif st.get("after") == "bun":
                want = np.where(tied[:, None], d, want)
        else:
            want = nrm(tangent(comb_dir(p, mode, st), p) + 1e-6)
        gw = grav * sm(0.0, 0.35, walked / 0.03)[:, None] if tie is None else (0.9 * tied)[:, None] * (st.get("after") == "pony")
        want = nrm(want * (1 - gw) + np.array([0.0, 0.0, -1.0]) * gw)
        d = nrm(d * stiff + want * (1 - stiff))
        p = p + d * step
        walked += step[:, 0]
        h = layer * sm(0.0, 0.012, walked) + 0.0004
        if tie is not None:
            h = np.where(tied, h * 0.3, h * 0.35)
        p = field.push(p, h)
        if st.get("ears", True):
            p = ear_push(p)
        if bodyc is not None:
            p = bodyc.push(p)
        d = nrm(p - P[:, i - 1])
        P[:, i] = p
    return P, tie_at


def children(Pg, groots, croots, cidx, st, field, bodyc, seed, tie_at=None):
    """Child strands round each guide: the guide path + the root offset, clumping toward the tip, waves/curls, frizz."""
    R = rng(seed + 1)
    S = Pg.shape[1] - 1
    t = np.linspace(0, 1, S + 1)
    off = croots - groots[cidx]
    clump = st.get("clump", 0.6)
    k = 1 - clump * t ** st.get("clump_pow", 0.8)
    C = Pg[cidx] + off[:, None, :] * k[None, :, None]
    if tie_at is not None and st.get("tie") is not None:
        ta = tie_at[cidx]
        tt = np.arange(S + 1)[None, :]
        gather = sm(0, 1, (tt - (ta[:, None] - 3)) / 3.0)
        C = Pg[cidx] + off[:, None, :] * (1 - gather[..., None]) * k[None, :, None]
        spread = st.get("spread", 0.010)
        o2 = R.normal(0, 1, (len(cidx), 3)) * spread
        C = C + o2[:, None, :] * sm(0, 1, (tt - ta[:, None]) / 3.0)[..., None]
    # tangent frames
    D = np.gradient(C, axis=1)
    D = nrm(D)
    radial = nrm(C)
    e1 = nrm(np.cross(D, radial))
    e2 = nrm(np.cross(D, e1))
    seg = np.linalg.norm(np.diff(C, axis=1), axis=-1)
    s = np.concatenate([np.zeros((len(C), 1)), np.cumsum(seg, axis=1)], axis=1)
    ramp = sm(0.0, 0.01, s)[..., None]
    wave = st.get("wave")
    if wave:
        a, per = wave
        ph = R.uniform(0, 2 * pi, len(C))[:, None] * 0.3 + (cidx % 7)[:, None] * 0.9
        C = C + e1 * (a * np.sin(2 * pi * s / per + ph))[..., None] * ramp
    curl = st.get("curl")
    if curl:
        a, per = curl
        ph = R.uniform(0, 2 * pi, len(C))[:, None]
        av = a * R.uniform(0.7, 1.3, len(C))[:, None]
        C = C + (e1 * (av * np.cos(2 * pi * s / per + ph))[..., None] + e2 * (av * np.sin(2 * pi * s / per + ph))[..., None]) * ramp
    fr = st.get("frizz", 0.1)
    if fr:
        C = C + R.normal(0, 1, C.shape) * (0.0012 * fr * t[None, :, None] ** 1.2)
    # collide again (children sit a little off their guide)
    flat = C.reshape(-1, 3)
    flat = field.push(flat, 0.0004)
    if st.get("ears", True):
        flat = ear_push(flat, 0.003)
    if bodyc is not None:
        flat = bodyc.push(flat)
    return flat.reshape(C.shape)


def trim(paths, frac, zcut=None):
    """Shorten strands: end at frac of their length or where they first drop below zcut (blunt cut); resample."""
    N, P, _ = paths.shape
    seg = np.linalg.norm(np.diff(paths, axis=1), axis=-1)
    cum = np.concatenate([np.zeros((N, 1)), np.cumsum(seg, axis=1)], axis=1)
    end = cum[:, -1] * frac
    if zcut is not None:
        below = paths[:, :, 2] < zcut[:, None]
        below[:, 0] = False
        has = below.any(1)
        first = np.argmax(below, axis=1)
        zc = np.where(has, cum[np.arange(N), np.maximum(first, 1)], cum[:, -1])
        end = np.minimum(end, zc)
    end = np.maximum(end, 0.0015)
    target = end[:, None] * np.linspace(0, 1, P)[None, :]
    idx = (cum[:, None, :] <= target[:, :, None]).sum(-1) - 1
    idx = np.clip(idx, 0, P - 2)
    rows = np.arange(N)[:, None]
    c0 = cum[rows, idx]
    c1 = cum[rows, idx + 1]
    f = np.clip((target - c0) / np.maximum(c1 - c0, 1e-9), 0, 1)[..., None]
    return paths[rows, idx] * (1 - f) + paths[rows, idx + 1] * f


def length_map(la, lo, L):
    """Strand length by root position from a style's top/front/side/back lengths."""
    a = (lo - 90.0 + 180.0) % 360.0 - 180.0
    front = ga(a, 38)
    back = ga(np.abs(a) - 180, 55)
    side = np.clip(1 - front - back, 0, 1)
    top = sm(38, 66, la)
    low = (front * L["front"] + back * L["back"] + side * L["side"]) / np.maximum(front + back + side, 1e-6)
    return top * L["top"] + (1 - top) * low


STYLES = {
    "crop": dict(comb="whorl", L=dict(top=0.011, front=0.010, side=0.006, back=0.005), lift=0.0012, grav=0.0, stiff=0.75,
                 frizz=0.25, clump=0.25, S=6, n=26000, guides=900, rise=0.4, curl=(0.0007, 0.003)),
    "fade": dict(comb="whorl", L=dict(top=0.016, front=0.014, side=0.003, back=0.0025), lift=0.0018, grav=0.0, stiff=0.7,
                 frizz=0.2, clump=0.3, S=7, n=26000, guides=900, rise=0.35, curl=(0.0008, 0.0035)),
    "coils": dict(rad=1.2, comb="whorl", L=dict(top=0.030, front=0.026, side=0.014, back=0.012), lift=0.012, grav=0.0, stiff=0.35,
                  frizz=0.6, clump=0.15, S=18, n=24000, guides=1100, rise=0.9, curl=(0.0016, 0.0042)),
    "textured": dict(comb="whorl", whorl=(0.0, -0.06, 0.09), L=dict(top=0.045, front=0.042, side=0.012, back=0.010),
                     lift=0.010, grav=0.1, stiff=0.65, frizz=0.45, clump=0.75, S=12, n=26000, guides=900, rise=0.55,
                     wave=(0.003, 0.03)),
    "side_part": dict(comb="part", part=62, L=dict(top=0.065, front=0.07, side=0.018, back=0.014), lift=0.007, grav=0.2,
                      stiff=0.78, frizz=0.12, clump=0.6, S=14, n=28000, guides=1000, rise=0.3),
    "swept": dict(comb="part", part=118, L=dict(top=0.085, front=0.10, side=0.03, back=0.026), lift=0.010, grav=0.25,
                  stiff=0.8, frizz=0.18, clump=0.65, S=16, n=30000, guides=1000, rise=0.35, wave=(0.002, 0.05)),
    "swept_back": dict(comb="back", L=dict(top=0.09, front=0.10, side=0.035, back=0.04), lift=0.013, grav=0.25,
                       stiff=0.82, frizz=0.15, clump=0.65, S=16, n=30000, guides=1000, rise=0.5, wave=(0.002, 0.06)),
    "slick": dict(comb="back", L=dict(top=0.07, front=0.075, side=0.03, back=0.03), lift=0.0028, grav=0.3, stiff=0.88,
                  frizz=0.03, clump=0.85, S=14, n=26000, guides=900, rise=0.1),
    "bowl": dict(rad=1.15, comb="whorl", whorl=(0.0, -0.03, 0.118), L=dict(top=0.16, front=0.16, side=0.16, back=0.16),
                 lift=0.0045, grav=0.75, stiff=0.6, frizz=0.03, clump=0.85, S=18, n=32000, guides=1100, rise=0.12,
                 cut="bowl"),
    "vulcan_f": dict(rad=1.15, comb="whorl", whorl=(0.0, -0.03, 0.118), L=dict(top=0.22, front=0.22, side=0.22, back=0.22),
                     lift=0.005, grav=0.85, stiff=0.6, frizz=0.03, clump=0.85, S=22, n=34000, guides=1100, rise=0.12,
                     cut="vulcan_f", body=True),
    "romulan_f": dict(rad=1.15, comb="part", part=104, L=dict(top=0.30, front=0.30, side=0.30, back=0.30), lift=0.006, grav=0.85,
                      stiff=0.6, frizz=0.03, clump=0.85, S=24, n=36000, guides=1200, rise=0.15, cut="romulan_f",
                      body=True),
    "pixie": dict(comb="part", part=112, L=dict(top=0.055, front=0.075, side=0.02, back=0.014), lift=0.009, grav=0.3,
                  stiff=0.72, frizz=0.25, clump=0.7, S=14, n=26000, guides=900, rise=0.35, wave=(0.002, 0.03)),
    "undercut": dict(comb="part", part=58, L=dict(top=0.10, front=0.115, side=0.003, back=0.003), lift=0.010, grav=0.4,
                     stiff=0.75, frizz=0.08, clump=0.75, S=18, n=26000, guides=900, rise=0.3, shaved_sides=28),
    "bob": dict(rad=1.15, comb="part", part=70, L=dict(top=0.24, front=0.24, side=0.24, back=0.24), lift=0.008, grav=0.85,
                stiff=0.6, frizz=0.08, clump=0.7, S=22, n=36000, guides=1200, rise=0.25, cut="bob", body=True),
    "long": dict(rad=1.2, comb="part", part=90, L=dict(top=0.40, front=0.40, side=0.40, back=0.40), lift=0.006, grav=0.9,
                 stiff=0.6, frizz=0.07, clump=0.7, S=30, n=38000, guides=1200, rise=0.2, wave=(0.003, 0.09), body=True),
    "curly_long": dict(rad=1.35, comb="part", part=74, L=dict(top=0.32, front=0.30, side=0.32, back=0.32), lift=0.022, grav=0.75,
                       stiff=0.55, frizz=0.35, clump=0.7, S=34, n=36000, guides=1300, rise=0.5, curl=(0.0065, 0.032),
                       body=True),
    "ponytail": dict(rad=1.15, tie=(0.0, -0.098, 0.035), after="pony", L=dict(top=0.36, front=0.36, side=0.36, back=0.36),
                     lift=0.0018, stiff=0.55, frizz=0.08, clump=0.5, S=30, n=32000, guides=1100, rise=0.08,
                     wave=(0.004, 0.07), spread=0.008, body=True, tail=0.20),
    "bun": dict(tie=(0.0, -0.104, 0.052), after="bun", L=dict(top=0.30, front=0.30, side=0.30, back=0.30), lift=0.002,
                stiff=0.55, frizz=0.15, clump=0.5, S=26, n=30000, guides=1000, rise=0.08, bun=(0.0, -0.118, 0.058),
                wave=(0.002, 0.05)),
    "slick_bun": dict(tie=(0.0, -0.100, 0.072), after="bun", L=dict(top=0.30, front=0.30, side=0.30, back=0.30),
                      lift=0.0012, stiff=0.6, frizz=0.03, clump=0.7, S=26, n=30000, guides=1000, rise=0.05,
                      bun=(0.0, -0.112, 0.082)),
    "shaggy": dict(rad=1.25, comb="whorl", L=dict(top=0.08, front=0.07, side=0.075, back=0.085), lift=0.016, grav=0.6, stiff=0.6,
                   frizz=0.45, clump=0.75, S=16, n=32000, guides=1100, rise=0.45, wave=(0.005, 0.04)),
    "wild": dict(rad=1.6, comb="back", L=dict(top=0.42, front=0.40, side=0.42, back=0.44), lift=0.030, grav=0.82, stiff=0.55,
                 frizz=0.5, clump=0.8, S=34, n=40000, guides=1400, rise=0.6, curl=(0.009, 0.06), body=True),
}


def cut_z(style, la, lo, F):
    """Blunt cuts: the z (head-local) where each root's strand ends, by root longitude."""
    a = np.abs((lo - 90.0 + 180.0) % 360.0 - 180.0)
    bz = F["bz"]
    if style == "bowl":
        fringe = bz + 0.016
        side = 0.050 if CUR.get("pointed") else 0.040
        return np.where(a < 55, fringe + 0.002 * (a / 55) ** 2,
                        np.where(a < 122, lerp_np(fringe + 0.002, side, sm(55, 72, a)),
                                 lerp_np(side, -0.045, sm(120, 148, a))))
    if style == "vulcan_f":
        fringe = bz + 0.012
        return np.where(a < 50, fringe, lerp_np(fringe, -0.085, sm(50, 80, a)))
    if style == "romulan_f":
        fringe = bz + 0.006 + 0.010 * ((lo - 90) / 50)        # fringe cut at an angle
        side = -0.11 + 0.035 * sm(100, 170, a)                 # longer in front, shorter at the back
        return np.where(a < 48, fringe, lerp_np(fringe, side, sm(48, 70, a)))
    if style == "bob":
        return np.full_like(lo, F["cz"] - 0.006) + 0.012 * sm(110, 175, a)
    return None


def lerp_np(a, b, t):
    return a + (b - a) * t


def scalp_strands(c, seed):
    """All scalp hair of one person as (points (N, P, 3) head-local, root weights)."""
    style = c.get("style")
    st = dict(STYLES[style])
    st["style"] = style
    F = CUR["F"]
    if style in ("bowl", "vulcan_f", "romulan_f", "bob"):
        st["ears"] = True
    field = CUR["field"]
    bodyc = BodyClear(0.006) if st.get("body") else None
    n_all = int(st["n"] * (0.85 if F["fem"] == 0 and F["recede"] > 2 else 1.0))
    G = st["guides"]
    hl_fn = hairline
    extra = None
    if style == "crop" and F["recede"] > 4:
        n_all = int(n_all * 0.9)
    la, lo, w = sample_scalp(n_all, seed, hl_fn, extra)
    roots = head_xyz(la, lo)
    nrms = head_normals(la, lo)
    R = rng(seed + 7)
    gi = R.choice(len(roots), G, replace=False)
    # nearest guide for every root (chunked)
    cidx = np.zeros(len(roots), int)
    gr = roots[gi]
    for a in range(0, len(roots), 4000):
        dd = np.linalg.norm(roots[a:a + 4000, None, :] - gr[None, :, :], axis=-1)
        cidx[a:a + 4000] = np.argmin(dd, axis=1)
    lens = length_map(la[gi], lo[gi], st["L"])
    if st.get("shaved_sides"):
        lens = np.where(la[gi] < st["shaved_sides"], 0.003, lens)
    if st.get("tie") is not None:
        T = np.array(st["tie"])
        lens = np.linalg.norm(roots[gi] - T, axis=-1) * 1.25 + (st.get("tail", 0.07) if st.get("after") == "pony" else 0.0)
    Pg, tie_at = grow_guides(gr, nrms[gi], lens, st, field, bodyc, seed)
    if st.get("after") == "bun":
        Pg = bun_paths(Pg, tie_at, st, seed)
    C = children(Pg, gr, roots, cidx, st, field, bodyc, seed, tie_at)
    frac = R.uniform(0.82, 1.0, len(C)) * (0.35 + 0.65 * w)
    if st.get("shaved_sides"):
        short = la < st["shaved_sides"] + R.normal(0, 1.2, len(la))
        Ltot = np.linalg.norm(np.diff(C, axis=1), axis=-1).sum(1)
        frac = np.where(short, np.minimum(1.0, 0.0035 / np.maximum(Ltot, 1e-6)), frac)
    zc = cut_z(st.get("cut"), la, lo, F) if st.get("cut") else None
    C = trim(C, frac, zc)
    if st.get("cut") in ("bob", "vulcan_f", "romulan_f"):
        # tips turn under a little
        tt = np.linspace(0, 1, C.shape[1])
        inward = -nrm(C[:, -1, :] * np.array([1, 1, 0]))
        C = C + inward[:, None, :] * (0.004 * sm(0.85, 1.0, tt))[None, :, None]
    return C, w, st


def bun_paths(Pg, tie_at, st, seed):
    """After the tie, each guide coils round the bun centre (a ring of hair on the back of the head)."""
    R = rng(seed + 3)
    B = np.array(st["bun"])
    T = np.array(st["tie"])
    axis = nrm(B - T)
    e1 = nrm(np.cross(axis, np.array([0.0, 0.0, 1.0])) + 1e-9)
    e2 = np.cross(axis, e1)
    G, P1, _ = Pg.shape
    out = Pg.copy()
    rad0 = R.uniform(0.006, 0.026, G)
    th0 = R.uniform(0, 2 * pi, G)
    hgt = R.uniform(-1, 1, G)
    for g in range(G):
        k0 = int(min(tie_at[g], P1 - 2))
        n = P1 - k0
        th = th0[g] + np.linspace(0, 2 * pi * 1.4, n)
        rr = rad0[g]
        hh = hgt[g] * (0.022 - rr * 0.6) * np.cos(np.linspace(0, pi, n) * 0.5)
        pts = B + (e1[None] * np.cos(th)[:, None] + e2[None] * np.sin(th)[:, None]) * rr + axis[None] * hh[:, None] * 0.6
        pts[0] = Pg[g, k0]
        out[g, k0:] = pts
    return out


def curves_object(name, paths, r_root, r_tip, mat, Dh, body_bvh=None, gap=0.003):
    """Hair curves object from head-local paths (N, P, 3): radius tapers root -> tip; posed with the head matrix,
    then (optionally) pushed out of the posed body."""
    N, P, _ = paths.shape
    world = paths + HC_NP
    M = np.array(Dh)
    W = world @ M[:3, :3].T + M[:3, 3]
    if body_bvh is not None:
        flat = W.reshape(-1, 3)
        zlim = (Dh @ (HEAD_C + V((0, 0, -0.05)))).z
        for i in np.nonzero(flat[:, 2] < zlim)[0]:
            q = V(flat[i].tolist())
            hit, nn, _, dist = body_bvh.find_nearest(q, 0.05)
            if hit is None:
                continue
            side = (q - hit).dot(nn)
            if side < gap:
                flat[i] = tuple(hit + nn * gap)
        W = flat.reshape(N, P, 3)
        # smooth the pushed strands a little, keep roots
        for _ in range(2):
            W[:, 1:-1] = W[:, 1:-1] * 0.5 + (W[:, :-2] + W[:, 2:]) * 0.25
    cu = bpy.data.hair_curves.new(name)
    cu.add_curves([P] * N)
    cu.position_data.foreach_set("vector", W.astype(np.float32).ravel())
    t = np.linspace(0, 1, P)
    rr = (r_root + (r_tip - r_root) * t ** 1.3)[None, :].repeat(N, 0)
    if "radius" not in cu.attributes:
        cu.attributes.new("radius", "FLOAT", "POINT")
    cu.attributes["radius"].data.foreach_set("value", rr.astype(np.float32).ravel())
    cu.materials.append(mat)
    ob = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ---------------------------------------------------------------- brows, lashes, beard

BROWS = {"natural": dict(arch=0.6, ap=0.62, tail=0.5, up=0.0, thick=1.0),
         "straight": dict(arch=0.2, ap=0.6, tail=0.3, up=0.0, thick=1.1),
         "arched": dict(arch=1.0, ap=0.58, tail=0.75, up=0.0, thick=0.85),
         "soft": dict(arch=0.45, ap=0.7, tail=0.6, up=0.0, thick=0.9),
         "bushy": dict(arch=0.4, ap=0.55, tail=0.4, up=0.0, thick=1.5),
         "upswept": dict(arch=0.0, ap=0.6, tail=0.0, up=1.0, thick=1.0),
         "thin": dict(arch=0.6, ap=0.6, tail=0.6, up=0.0, thick=0.55)}


def brow_strands(c, seed):
    """Eyebrow hairs lying on the skin: medial hairs point up, the body runs along the brow, upper and lower rows
    angle toward the middle line."""
    F = CUR["F"]
    gr = CUR["grid"]
    sp = c["species"]
    kind = {"vulcan": "upswept", "romulan": "upswept", "klingon": "bushy", "tellarite": "bushy",
            "cardassian": "thin", "ferengi": "thin"}.get(sp)
    if kind is None:
        H = lambda k: bc.h32(seed, 600 + k)  # noqa: E731
        kind = ["natural", "straight", "arched", "soft", "natural"][int(H(0) * 5) % 5]
        if F["fem"] == 0 and kind == "arched":
            kind = "straight"
    B = dict(BROWS[kind])
    B["thick"] *= (1.15 if F["fem"] == 0 else 0.82) * (1 - 0.25 * F["age"] * F["fem"])
    R = rng(seed + 11)
    out = []
    ex, ez = F["eye_x"], F["ez"]
    for sx in (-1, 1):
        x0 = 0.0105 + 0.0012 * F["fem"]
        x1 = ex + 0.0215
        zb = ez + 0.0158 + 0.0022 * F["fem"] + 0.0022 * F["raise"] + F["a_brow"] * sx + 0.0025 * B["up"]
        n = int(800 * B["thick"])
        t = R.uniform(0, 1, n) ** 0.85
        v = R.uniform(-1, 1, n)
        keep = R.uniform(0, 1, n) < (1 - np.abs(v) ** 4) * sm(1.05, 0.85, t + 0.1 * np.abs(v))
        t, v = t[keep], v[keep]

        def centre(t):
            x = sx * (x0 + (x1 - x0) * t)
            z = zb + B["arch"] * 0.0052 * sm(0.0, B["ap"], t) - B["tail"] * 0.0062 * sm(B["ap"], 1.05, t) \
                + B["up"] * (0.0058 * sm(0.42, 1.0, t) ** 1.2 + 0.0008 * sm(0.25, 0.0, t)) + 0.0012 * F["raise"] * sm(0.4, 0.0, t)
            return x, z
        cx, cz = centre(t)
        w = 0.0082 * B["thick"] * (1 - 0.6 * t ** 1.5) * (0.8 + 0.2 * sm(0.0, 0.15, t))
        tx2, tz2 = centre(np.minimum(t + 0.02, 1.0))
        tdir = nrm(np.stack([tx2 - cx, np.zeros_like(cx), tz2 - cz], -1) + 1e-9)
        rx = cx + 0 * v
        rz = cz + v * w * 0.5
        # growth direction: medial up, then along (+x outward); rows converge
        along = tdir.copy()
        upv = np.array([0.0, 0.0, 1.0])
        med = sm(0.22, 0.0, t)[:, None]
        conv = (-0.45 * v)[:, None] * upv[None, :]
        dirv = nrm(along * (1 - med) + (upv + np.array([sx * 0.35, 0, 0])) * med + conv * (1 - med))
        L = (0.0055 + 0.0035 * R.uniform(0, 1, len(t))) * (0.75 + 0.25 * B["thick"]) * (1 - 0.35 * med[:, 0])
        Pn = 5
        pts = np.zeros((len(t), Pn, 3))
        for k in range(Pn):
            s = k / (Pn - 1)
            px = rx + dirv[:, 0] * L * s
            pz = rz + dirv[:, 2] * L * s
            py = gr.y(px, pz) + 0.00025 + 0.0006 * s + 0.0002 * R.uniform(0, 1, len(t))
            pts[:, k] = np.stack([px, py, pz], -1)
        out.append(pts)
    return np.concatenate(out), kind


def lash_strands(lines, seed):
    """Upper and lower lashes from the lid margins: out from the eyeball, curling up (upper) / down (lower)."""
    F = CUR["F"]
    R = rng(seed + 13)
    out = []
    for c, up_line, lo_line in lines:
        cc = np.array(tuple(c - HEAD_C))
        for line, sgn, Lb, rows in ((up_line, 1, 0.0062 + 0.0026 * F["fem"], 2), (lo_line, -1, 0.0036, 1)):
            P = np.array([tuple(p - HEAD_C) for p, n, lo in line])
            N = np.array([tuple(n) for p, n, lo in line])
            los = np.array([lo for p, n, lo in line])
            for rr in range(rows):
                jit = R.uniform(-0.4, 0.4, len(P))
                base = P + N * (0.0002 * rr) + R.normal(0, 0.00015, P.shape)
                outer = np.where(cc[0] > 0, 1 - (los - 20) / 142, (los - 20) / 142)
                L = Lb * (0.7 + 0.45 * np.sin(np.clip((los - 20) / 142, 0, 1) * pi) ** 0.5 + 0.15 * outer) * (0.85 + 0.15 * jit)
                out_d = nrm(N + np.array([0, 0, -0.25 * sgn]))
                lat = nrm(np.stack([np.sign(cc[0]) * (outer - 0.5) * 0.6, np.zeros_like(outer), np.zeros_like(outer)], -1) + 1e-9)
                Pn = 6
                pts = np.zeros((len(P), Pn, 3))
                for k in range(Pn):
                    s = k / (Pn - 1)
                    curlv = np.array([0, 0, 1.0]) * sgn * (s ** 1.8) * 0.9
                    d = out_d * s + curlv[None, :] * 1.0 + lat * 0.25 * s
                    pts[:, k] = base + d * L[:, None] * 0.75
                out.append(pts)
    return np.concatenate(out)


def beard_strands(c, seed):
    """Beard hairs from the beard zone on the head grid: stubble, short, full, Klingon (long, pointed), Tellarite."""
    kind = c["beard"]
    F = CUR["F"]
    R = rng(seed + 17)
    n = {"stubble": 30000, "short": 26000, "full": 26000, "klingon": 30000, "tellarite": 28000}[kind]
    lo = R.uniform(0, 180, n * 3) + 0.0
    la = R.uniform(-89, 5, n * 3)
    P = head_xyz(la, lo)
    z = beard_zone(P)
    keep = R.uniform(0, 1, len(P)) < z
    la, lo, P = la[keep][:n], lo[keep][:n], P[keep][:n]
    w = beard_zone(P)
    N = head_normals(la, lo)
    L = {"stubble": 0.0014, "short": 0.007, "full": 0.017, "klingon": 0.022, "tellarite": 0.026}[kind]
    x, z = P[:, 0], P[:, 2]
    chin = ga(x, 0.02) * sm(F["mz"] - 0.01, F["cz"] - 0.01, z)
    if kind == "klingon":
        L = L + 0.06 * chin ** 1.5
    if kind in ("full", "tellarite"):
        L = L + 0.012 * chin
    L = L * (0.6 + 0.4 * w) * R.uniform(0.75, 1.15, len(P))
    st = dict(comb="down", lift=0.0005 if kind == "stubble" else 0.003 + 0.004 * (kind != "short"), grav=0.5,
              stiff=0.55, frizz=0.35 if kind != "stubble" else 0.6, clump=0.35, S=4 if kind == "stubble" else 9,
              rise=0.9 if kind == "stubble" else 0.35, curl=(0.0012, 0.006) if kind in ("full", "klingon", "tellarite") else None,
              ears=False)
    # moustache hairs point down-out, cheek hairs down
    Pg, _ = grow_guides(P, N, L, st, CUR["field"], None, seed + 19)
    C = children(Pg, P, P, np.arange(len(P)), dict(st, clump=0.0), CUR["field"], None, seed + 23)
    return C, kind


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
        collar(mb, body, "kl_leather", h=0.022, out=0.008, flare=0.006)
        th = [D(100 + 340 * k / 40) for k in range(41)]
        nl = bc.neck_limb(body)
        edge = [nl.pt(0.62, t, 0.0285) + V((0, 0, 0.0176)) for t in th]
        edge2 = [nl.pt(0.62, t, 0.022) + V((0, 0, 0.0186)) for t in th]
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
    elif f == "romulan" and CUR["sex"] == "f":
        # her own cut: a tall stand collar, slim quilted shoulder caps, a V yoke and a wide sash with a clasp
        collar(mb, body, "rom_trim", h=0.030, out=0.004, flare=0.004)
        zs = [1.38 + (1.515 - 1.38) * i / 12 for i in range(13)]
        ths = [2 * pi * j / 72 for j in range(72)]
        bc.surface_panel(mb, fn, nf, zs, ths, lambda z, t: 0.006 + 0.013 * abs(math.cos(t)) ** 2.0 * smooth(1.38, 1.45, z),
                         "rom_pad", bc.T_CAND, closed=True)
        for sx in (1, -1):
            bc.ribbon(mb, fn, nf, lambda t, sx=sx: (1.49 - 0.17 * t, D(90 + sx * (34 - 30 * t))), 16, 0.010, 0.0075,
                      "rom_trim", bc.T_CAND, depth=0.002)
        bc.ribbon(mb, fn, nf, lambda t: (1.47 - 0.42 * t, D(32 + 116 * t)), 28, 0.05, 0.008, "rom_sash", bc.T_CAND, depth=0.004)
        p = body.torso(1.455, D(40), 0.014)
        n = body.torso_n(1.455, D(40))
        bc.tube(mb, p - n * 0.004, p + n * 0.004, 0.011, 0.010, 16, "rom_metal", bc.T_CAND)
        bc.tube(mb, p + n * 0.003, p + n * 0.006, 0.006, 0.004, 12, "rom_trim", bc.T_CAND)
    elif f == "romulan":
        collar(mb, body, "rom_trim", h=0.016, out=0.005)
        zs = [1.33 + (1.515 - 1.33) * i / 14 for i in range(15)]
        ths = [2 * pi * j / 72 for j in range(72)]
        bc.surface_panel(mb, fn, nf, zs, ths, lambda z, t: 0.008 + 0.030 * abs(math.cos(t)) ** 1.3 * smooth(1.33, 1.43, z),
                         "rom_pad", bc.T_CAND, closed=True)
        bc.ribbon(mb, fn, nf, lambda t: (1.40 - 0.33 * t, D(70 + 42 * t)), 20, 0.024, 0.006, "rom_trim", bc.T_CAND, depth=0.003)
        for k in range(3):
            t = 0.2 + 0.3 * k
            p = body.torso(1.40 - 0.33 * t, D(70 + 42 * t), 0.009)
            n = body.torso_n(1.40 - 0.33 * t, D(70 + 42 * t))
            bc.tube(mb, p - n * 0.004, p + n * 0.003, 0.004, 0.0035, 10, "rom_metal", bc.T_CAND)
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
        collar(mb, body, "fer_shirt", h=0.012, out=0.006, th0=110, th1=430)
        bc.surface_panel(mb, fn, nf, [1.43 + 0.1 * i / 8 for i in range(9)], [D(118 + 304 * j / 40) for j in range(41)],
                         lambda z, t: 0.010 + 0.006 * smooth(1.45, 1.52, z), "fer_trim", bc.T_CAND)
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

HEAD_SCALE = 1.09   # stylised: a slightly larger head on build_crew's body (scaled on the Head bone)
HEAD_DROP = 0.012   # and lower (the Head bone is disconnected from the Neck for this)

SPECIES = {
    "human": dict(ears="human"),
    "vulcan": dict(ears="pointed"),
    "andorian": dict(ears="human"),
    "tellarite": dict(ears="human"),
    "bajoran": dict(ears="human"),
    "trill": dict(ears="human"),
    "betazoid": dict(ears="human"),
    "klingon": dict(ears="human"),
    "romulan": dict(ears="pointed"),
    "cardassian": dict(ears="human"),
    "ferengi": dict(ears="ferengi"),
}


def build_character(c, loc=V((0, 0, 0)), yaw=0.0, head_turn=0.0, cam=None, t_idle=0.15, height=1.0):
    sp = c["species"]
    S = SPECIES[sp]
    CUR.clear()
    CUR.update(c)
    CUR["faction"] = FACTION_OF[sp]
    c["faction"] = CUR["faction"]
    CUR["pointed"] = S["ears"] == "pointed"
    seed = c.get("seed_n", zlib.crc32(c["id"].encode()))
    fem = 1.0 if c["sex"] == "f" else 0.0
    ov = {k: c[k] for k in ("face", "nose", "smile", "smirk", "raise", "freckles", "monolid", "open_mouth", "heavy")
          if k in c}
    body = bc.Body(fem, c.get("heavy", 0.0), c.get("slim", 0.0))
    CUR["body"] = body
    CUR["F"] = F = face_params(seed, sp, c["sex"], c.get("years", 35), ov)
    F["_nprof"] = nose_profile(F)
    STYLE.clear()
    if c.get("style"):
        STYLE.update(STYLES[c["style"]])
        STYLE["style"] = c["style"]
    CUR["grid"] = FaceGrid()
    CUR["field"] = HeadField()
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
    rig = bc.make_armature("Rig_" + c["id"])
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    rig.data.edit_bones["Head"].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    objs = [rig]
    objs.append(make_obj("Body_" + c["id"], mb, rig, tint_fn=lambda Q: (np.ones((len(Q), 4)), np.zeros((len(Q), 4)))))
    objs.append(head_grid_obj("Head_" + c["id"], rig))
    fm = bc.MB()
    lines = []
    for sx in (-1, 1):
        lines.append(lids(fm, sx))
    es = ears(fm, S["ears"])
    if sp == "cardassian":
        neck_ridges(fm)
    objs.append(make_obj("Face_" + c["id"], fm, rig, tint_fn=lambda Q: skin_masks(Q)))
    xm = bc.MB()
    if sp == "bajoran":
        bajoran_earring(xm, es[1])
    if sp == "ferengi":
        ferengi_teeth(xm)
    if sp == "andorian":
        antennae(xm)
    if sp == "trill":
        trill_spots(xm)
    if xm.co:
        objs.append(make_obj("Extra_" + c["id"], xm, rig, tint_fn=lambda Q: skin_masks(Q)))
    # rig + pose
    Sol = bc.Solver(rig)
    spec = bc.pose_idle(t_idle)
    spec["rot"]["Head"] = bc.Rz(head_turn) @ bc.Rx(-2.0)
    spec["rot"]["Neck"] = bc.Rz(head_turn * 0.5)
    bc.apply_pose(rig, Sol, spec)
    rig.pose.bones["Head"].scale = (HEAD_SCALE,) * 3
    rig.pose.bones["Head"].location = (0.0, -HEAD_DROP, 0.0)    # the bigger head sits a little lower on the neck
    rig.location = loc
    rig.rotation_euler = (0, 0, rad(yaw))
    rig.scale = (height,) * 3
    bpy.context.view_layer.update()
    pb = rig.pose.bones["Head"]
    Dh = rig.matrix_world @ pb.matrix @ rig.data.bones["Head"].matrix_local.inverted()
    camv = cam if cam is not None else V((0, 3, 1.6))
    # eyes, turned toward the camera
    eyes = []
    for sx in (-1, 1):
        eyeball(eyes, sx, Dh, camv)
    for k, (kind, emb, ep) in enumerate(eyes):
        objs.append(mb_object(f"Eye{k}_" + c["id"], emb, ep))
    # strands
    if not NO_HAIR:
        dg = bpy.context.evaluated_depsgraph_get()
        body_ob = objs[1]
        bvh = BVHTree.FromObject(body_ob.evaluated_get(dg), dg) if c.get("style") in (
            "long", "curly_long", "wild", "ponytail", "bob", "vulcan_f", "romulan_f") else None
        if c.get("style"):
            C, w, st = scalp_strands(c, seed)
            CUR["bun_core"] = st.get("bun")
            hr = st.get("rad", 1.0) * 0.000085
            objs.append(curves_object("Hair_" + c["id"], C, hr, hr * 0.25, CHAR_MATS["HairS"], Dh, bvh))
            if st.get("bun"):
                cm = bc.MB()
                B = V(st["bun"]) + HEAD_C
                bc.ellipsoid(cm, B, (V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))), (0.022, 0.016, 0.019), 20, 10, "hair_core", RIG_HEAD)
                objs.append(make_obj("BunCore_" + c["id"], cm, rig, attrs=False))
        if sp not in ("ferengi",) or True:
            B_, kind = brow_strands(c, seed)
            objs.append(curves_object("Brow_" + c["id"], B_, 0.00007, 0.00002, CHAR_MATS["BrowS"], Dh))
        L_ = lash_strands(lines, seed)
        objs.append(curves_object("Lash_" + c["id"], L_, 0.00006, 0.000015, CHAR_MATS["LashS"], Dh))
        if c.get("beard"):
            Bd, kind = beard_strands(c, seed)
            r0 = 0.00007 if kind != "stubble" else 0.00005
            objs.append(curves_object("Beard_" + c["id"], Bd, r0, 0.00002, CHAR_MATS["BeardS"], Dh))
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
            elif isinstance(data, bpy.types.Curves):
                bpy.data.hair_curves.remove(data)
    shared = list(MATS.values())
    for m in list(bpy.data.materials):
        if m.users == 0 and m not in shared:
            bpy.data.materials.remove(m)


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
    m.use_fake_user = True
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
    scene.cycles.max_bounces = 8
    scene.cycles.transparent_max_bounces = 24
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.02
    scene.cycles_curves.shape = "THICK"
    scene.cycles_curves.subdivisions = 2
    try:
        scene.cycles.denoiser = "OPENIMAGEDENOISE"
        scene.cycles.denoising_input_passes = "RGB_ALBEDO_NORMAL"
    except Exception:  # noqa: BLE001
        pass
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 86
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
CAM_DIST = 1.22
ORBIT = [float(x) for x in (arg("--orbit") or "").split(",") if x]  # debug: extra views round the head (deg)


def portrait(cam, c, extras):
    yaw = YAW
    camp = V((0.0, CAM_DIST, 1.62))
    objs, head = build_character(c, yaw=yaw, head_turn=-4.0, cam=camp)
    tall = 0.06 if c["species"] == "andorian" else 0.0
    target = head + V((0, -0.03, -0.062 + tall))
    camp = V((target.x + 0.0, target.y + CAM_DIST + 2.2 * tall, target.z + 0.06))
    # eyes were aimed at the first guess; the camera only moves by centimetres
    aim(cam, camp, target, 85)
    cam.data.dof.use_dof = True
    cam.data.dof.focus_distance = (camp - head).length - 0.04
    cam.data.dof.aperture_fstop = 3.5
    eye_l = light("EyeLight", "AREA", 1.6, 0.12, camp + V((-0.16, -0.05, 0.10)), head, (1.0, 0.97, 0.94))
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
    remove(objs + [bd, eye_l])
    return path


def cyclorama(name, width, depth, height, radius, mat):
    """Floor sweeping up into a back wall (no horizon line); UV u across, v = 0 where the wall starts."""
    prof = []
    n_floor, n_curve, n_wall = 8, 18, 10
    for i in range(n_floor + 1):
        prof.append((depth - (depth - radius) * i / n_floor, 0.0))
    for i in range(1, n_curve + 1):
        a = (pi / 2) * i / n_curve
        prof.append((radius - radius * math.sin(a) - 0.0, radius - radius * math.cos(a)))
    for i in range(1, n_wall + 1):
        prof.append((0.0, radius + (height - radius) * i / n_wall))
    s = [0.0]
    for (a, b), (c_, d) in zip(prof, prof[1:]):
        s.append(s[-1] + math.hypot(c_ - a, d - b))
    s_wall = s[n_floor] + (s[n_floor + n_curve] - s[n_floor]) * 0.5
    nx = 24
    verts, uvs, faces = [], [], []
    for i, (yy, zz) in enumerate(prof):
        for j in range(nx + 1):
            x = -width / 2 + width * j / nx
            verts.append((x, yy, zz))
    for i in range(len(prof) - 1):
        for j in range(nx):
            a = i * (nx + 1) + j
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            i, j = divmod(vi, nx + 1)
            uvl.data[li].uv = (j / nx, (s[i] - s_wall) / (height - radius * 0.5))
    me.materials.append(mat)
    me.shade_smooth()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def cyc_material(img):
    h, w, _ = img.shape
    im = bpy.data.images.new("CycImg", w, h, alpha=False, float_buffer=True)
    rgba = np.concatenate([img[::-1], np.ones((h, w, 1), np.float32)], axis=2)
    im.pixels.foreach_set(rgba.ravel())
    m, p = principled("Cyc", (0.010, 0.011, 0.014), 0.62)
    nt = m.node_tree
    uv = node(nt, "ShaderNodeUVMap")
    tx = node(nt, "ShaderNodeTexImage")
    tx.image = im
    tx.extension = "EXTEND"
    nt.links.new(uv.outputs["UV"], tx.inputs["Vector"])
    sp = sep(nt, uv.outputs["UV"])
    vmask = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", sp[1], -0.12), 4.0, clamp_=True)
    em = mix_rgb(nt, vmask, (0, 0, 0), tx.outputs["Color"])
    nt.links.new(em, p.inputs["Emission Color"])
    p.inputs["Emission Strength"].default_value = 1.0
    return m


def lineup(cam, chars, extras):
    objs_all = []
    xs = [-1.35, -0.45, 0.45, 1.35]
    heights = [1.03, 0.98, 1.0, 0.96]
    camp = V((2.2, 6.6, 1.55))
    for k, c in enumerate(chars):
        objs, _ = build_character(c, loc=V((xs[k], 0.15 * (k % 2), 0)), yaw=28 + 4 * k, head_turn=-6, cam=camp,
                                  t_idle=0.1 + 0.2 * k, height=heights[k])
        objs_all += objs
    cy = cyclorama("Cyc", 18, 9.0, 8.0, 2.2, cyc_material(backdrop_image("federation", 1600, 900, side=1)))
    cy.location = (-0.6, -3.4, 0)
    for o in extras["portrait_lights"]:
        o.hide_render = True
    ls = [light("LKey", "AREA", 1100, 2.4, (-3.2, 4.6, 4.2), (0, 0, 1.1), (1.0, 0.95, 0.88)),
          light("LRim", "AREA", 800, 2.0, (3.4, -2.6, 3.4), (0, 0, 1.2), (0.82, 0.9, 1.0)),
          light("LRim2", "AREA", 450, 2.0, (-3.6, -2.2, 3.0), (0, 0, 1.2), (0.9, 0.92, 1.0)),
          light("LFill", "AREA", 160, 5.0, (4.0, 5.0, 2.0), (0, 0, 1.0), (0.88, 0.93, 1.0))]
    for x in xs:   # soft pools on the floor under each officer
        ls.append(light("LPool", "SPOT", 260, 0.6, (x + 0.2, 0.6, 5.2), (x, 0.1, 0.0), (1.0, 0.96, 0.9)))
        ls[-1].data.spot_size = rad(38)
        ls[-1].data.spot_blend = 0.9
    aim(cam, camp, V((0.0, 0.0, 0.93)), 50)
    cam.data.dof.use_dof = False
    render(os.path.join(OUT, "lineup.jpg"), 1600, 900)
    remove(objs_all + [cy] + ls)
    for o in extras["portrait_lights"]:
        o.hide_render = False


def look(seed, species, sex=None):
    """Seed -> a whole person (the generator): sex, age, build, skin, hair colour/style, beard, iris, expression.
    The face itself comes from face_params(seed, ...) when the person is built."""
    h = lambda k: bc.h32(seed, k)  # noqa: E731
    sex = sex or ("f" if h(0) < 0.5 else "m")
    pal = {
        "andorian": (["andorian"], ["white", "silver"]), "cardassian": (["cardassian"], ["black", "darkbrown"]),
        "ferengi": (["ferengi"], [None]), "tellarite": (["tellarite"], ["brown", "auburn", "darkbrown", "chestnut"]),
        "klingon": (["klingon", "klingon2", "brown", "dark"], ["black", "darkbrown", "brown"]),
        "romulan": (["romulan", "tan", "light", "olive"], ["black", "darkbrown"]),
        "vulcan": (["light", "olive", "tan", "brown", "dark"], ["black", "darkbrown"]),
    }.get(species, (["fair", "light", "tan", "olive", "brown", "dark", "deep", "eastasian"],
                    ["black", "darkbrown", "brown", "chestnut", "auburn", "copper", "blond", "ash"]))
    skins, hairs = pal
    styles = {"vulcan": ["bowl"] if sex == "m" else ["vulcan_f", "bowl", "bun"],
              "romulan": ["bowl", "slick"] if sex == "m" else ["romulan_f", "bob"], "klingon": ["wild"],
              "cardassian": ["slick"] if sex == "m" else ["slick_bun", "slick"], "ferengi": [None],
              "tellarite": ["shaggy"], "andorian": ["swept_back", "crop", "textured"] if sex == "m" else ["pixie", "bob", "bun"]}.get(
        species, ["crop", "fade", "textured", "side_part", "swept", "swept_back", "coils", "shaggy"] if sex == "m"
        else ["bun", "bob", "long", "curly_long", "ponytail", "pixie", "undercut", "swept"])
    irises = ["black"] if species == "betazoid" else ["brown", "darkbrown", "hazel", "green", "blue", "grey", "amber"]
    years = int(24 + 40 * h(7) ** 1.3)
    hair = hairs[int(h(3) * len(hairs)) % len(hairs)]
    grey = float(np.clip((years - 38) / 25.0, 0, 1) * (0.4 + 0.6 * h(8))) if hair not in (None, "white", "silver") else 0.0
    beard_kind = {"klingon": "klingon", "tellarite": "tellarite"}.get(species)
    if beard_kind is None:
        beard_kind = ["stubble", "short", "full", "stubble"][int(h(12) * 4) % 4]
    beard_on = sex == "m" and species not in ("vulcan", "romulan", "ferengi", "cardassian", "andorian") and (
        h(5) < 0.4 or species in ("klingon", "tellarite"))
    band = f"{(years // 10) * 10}-{(years // 10) * 10 + 10}"
    return dict(id=f"{species}_{sex}_seed{seed}", seed_n=seed, species=species, sex=sex, dept=list(DEPTS)[int(h(9) * 4) % 4]
                if FACTION_OF[species] == "federation" else None, age=band, years=years,
                skin=skins[int(h(2) * len(skins)) % len(skins)], hair=hair, grey=grey,
                style=styles[int(h(4) * len(styles)) % len(styles)], iris=irises[int(h(6) * len(irises)) % len(irises)],
                heavy=0.6 * h(10) if h(11) > 0.6 else 0.0, slim=0.5 * h(10) if h(11) < 0.25 else 0.0,
                beard=beard_kind if beard_on else None, smile=0.5 * h(13) ** 1.5 if species not in ("vulcan", "romulan") else 0.0,
                freckles=0.7 * h(14) if h(15) < 0.2 else 0.0)


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
                            apparent_age=c.get("years"), face_shape=c.get("face"), nose=c.get("nose"),
                            seed=zlib.crc32(c["id"].encode()),
                            outfit="colony-service jumpsuit, department yoke" if FACTION_OF[c["species"]] == "federation"
                            else {"klingon": "dark leather, metal pauldrons and chest plates",
                                  "romulan": ("grey-green tunic, high stand collar, quilted shoulder caps, sash with a clasp"
                                              if c["sex"] == "f" else "grey-green tunic, quilted padded shoulders"),
                                  "cardassian": "grey-brown ribbed armour, raised shoulder plates",
                                  "ferengi": "patterned jacket, gold flared collar"}[FACTION_OF[c["species"]]]))
    doc = dict(generator="blender/build_portraits.py", note="Mockup portraits (original designs; no canon uniforms or insignia).",
               size=[RES, RES], departments=DEPTS, lineup=dict(file="lineup.jpg", left_to_right=LINEUP[::-1]) if os.path.exists(
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
    pl = [light("Key", "AREA", 80, 0.9, (-1.25, 0.95, 2.15), (0, 0, 1.64), (1.0, 0.95, 0.88)),
          light("Rim", "AREA", 90, 0.5, (0.9, -0.8, 1.95), (0, 0, 1.64), (0.82, 0.9, 1.0)),
          light("Rim2", "AREA", 40, 0.5, (-0.9, -0.85, 1.9), (0, 0, 1.62), (0.9, 0.92, 1.0)),
          light("Fill", "AREA", 9, 2.0, (1.3, 1.5, 1.4), (0, 0, 1.6), (0.88, 0.93, 1.0))]
    extras = {"portrait_lights": pl}
    extras["bd"] = {f: backdrop_material("BD_" + f, backdrop_image(f)) for f in FACTION_COLOUR}
    cast = [c for c in CAST if (not ONLY_SPECIES or c["species"] in ONLY_SPECIES)
            and (not ONLY_SEX or c["sex"] == ONLY_SEX) and (not ONLY or c["id"] in ONLY)]
    if "portraits" in SHOTS:
        for c in cast:
            portrait(cam, dict(c), extras)
        for sd in SEEDS:
            for sp in (ONLY_SPECIES or SPECIES_ORDER):
                portrait(cam, look(sd, sp, ONLY_SEX), extras)
    if not ONLY and not SEEDS:
        write_json(CAST)
    if "lineup" in SHOTS:
        lineup(cam, [dict(next(c for c in CAST if c["id"] == i)) for i in LINEUP], extras)
        if not ONLY and not SEEDS:
            write_json(CAST)


if __name__ == "__main__":
    main()
