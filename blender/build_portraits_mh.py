"""Character portraits on the MakeHuman base mesh (owner 2026-11-12: the hand-built faces of build_portraits.py "look
like dolls", noses long and pointed, smiles don't read, faces stiff; wants "realistic cartoony": stylised realism like
modern stylised games / animation adults - believable anatomy and skin, gentle exaggeration; approved MakeHuman).

What it makes (docs/mockups/character/portraits/ by default; the same files build_portraits.py made, so
docs/mockups/character/index.html keeps working):
  <species>_<sex>_<dept>.jpg   18 Federation people (Human x6, Vulcan, Andorian, Tellarite, Bajoran, Trill, Betazoid,
                               one woman + one man each) in the ORIGINAL colony-service jumpsuit: charcoal, department
                               colour on the shoulder yoke and sleeve bands (Exploration #3E7FD6, Defense #C8423A,
                               Maintenance #D9A23A, Archaeology #4E9A5A), thin dark piping, black stand collar, gold
                               hexagon badge with a raised chevron (no canon insignia).
  <species>_<sex>.jpg          other powers: Klingon (leather, layered metal pauldrons, chest plates, red-trimmed collar),
                               Romulan (grey-green tunic; man: quilted padded shoulders; woman: tall collar, slim quilted
                               caps), Cardassian (ribbed armour, shoulder plates rising to a flared neck plate), Ferengi
                               (brocade jacket, cream shirt front, gold flared collar).
  portraits.json               {generator, note, size, departments, lineup, portraits: [{file, species, faction, sex,
                               department, department_colour, skin, skin_rgb, hair, hair_style, beard, eyes, age_band,
                               apparent_age, face_shape, nose, seed, expression, stylised_feature, makehuman, outfit}]}
  lineup.jpg                   the four Federation officers of build_portraits.LINEUP full length (MakeHuman full body in
                               the jumpsuit, boots and belt), cyclorama, 1600 x 900
  sheet.jpg                    contact sheet (system python3 + PIL: --sheet DIR)
Shot: 640 x 640, body turned 24 deg, head turned back toward the lens, eyes on the camera, 85 mm, soft warm key + cool
rims + weak fill + a small eye light, dark gradient backdrop with a faint faction element (build_portraits'), Cycles 64
samples denoised, JPEG 86.

Run (bpy 5.x venv; ~20 s a portrait at 64 samples on 4 cores, the whole set + lineup ~12-15 min):
  /home/user/.bpyvenv/bin/python blender/build_portraits_mh.py
  /home/user/.bpyvenv/bin/python blender/build_portraits_mh.py --only human_f_defense,vulcan_m_exploration --samples 24
  python3 blender/build_portraits_mh.py --sheet docs/mockups/character/portraits
Flags: --species <id>[,<id>]   only these species (human vulcan andorian tellarite bajoran trill betazoid klingon romulan
                               cardassian ferengi)
       --sex f|m               only this sex
       --only <stem>[,<stem>]  only these files (json is rewritten only on a full run)
       --seed N[,N...]         also render the generator's random person look(N) for each picked species
                               (<species>_<sex>_seedN.jpg, not in portraits.json)
       --samples 64 --res 640  Cycles samples, portrait size
       --out DIR               output folder
       --shots portraits,lineup
       --sheet DIR             contact sheet only (no bpy)
       --mh PATH               a MakeHuman 1.x checkout (default /home/user/makehumancommunity/makehuman); only read
                               when blender/makehuman/ is missing, or with --vendor (rebuild the vendored subset, exit)
  debug: --no-hair, --skip hair,brow,lash,beard, --orbit A[,B] (extra close views round the head; --orbit-dist/-x/-z),
         --closeup (only the orbit views), --expr unit:w,... (replace the expression), --tag S (file name suffix)

MakeHuman data: blender/makehuman/ holds the compact subset this script needs (base.npz: hm08 base mesh vertices, its
face groups (body + helpers: tights, eyes, eyelashes, teeth, tongue) and joint vertex groups; targets.npz: 516 targets
as uint16 vertex indices + int16 offsets in 1/1000 dm (lossless: the .target files have 3 decimals) - adult macro
targets (ethnicity x gender x young/old, universal gender x age x muscle x weight), head, forehead, eyebrows, eyes, nose,
mouth, cheek, chin, ears, neck, torso, asym and the face expression units for all three ethnicities; rig.json.gz: the
default skeleton's bones, joints and weights). ~4.5 MB, CC0, from github.com/makehumancommunity/makehuman (see
blender/makehuman/README.md). MakeHuman's own code is not used; the loader and the macro weighting are re-implemented
(apps/human.py _set*Vals, apps/humanmodifier.py getTargetWeights: a macro target's weight is the product of the macro
variable values in its file name).

How a person is generated from a seed (person()):
  * the cast entry (build_portraits.CAST, or build_portraits.look(seed) for --seed) gives species, sex, apparent age,
    skin, hair colour + style, iris, beard, build (heavy/slim), face shape, nose type and smile; the look seed is crc32 of
    the id (or the --seed number) and every number below is build_crew.h32(seed, k), so a person is stable.
  * macros: gender (0 / 1 +- a little), age (years -> MakeHuman 25..90 scale), ethnicity mix from the skin preset +-
    0.08 each, muscle and weight from the build.
  * ~60 face modifiers, each with its own triangular noise: head width/depth/height/fat/age, forehead height, slope and
    temples, brow ridge projection/height/angle, eye spacing/height/lid heights/corners (tilt)/fold/bags/epicanthus,
    nose type (straight, aquiline, roman, snub, button, broad, flat, hooked, greek -> a recipe of MakeHuman's nose
    modifiers, then noise on all 18), mouth width, lip volumes, cupid's bow, philtrum, dimples, laugh lines, cheekbones /
    volume / height, chin prominence / width / height, jaw width and drop, prognathism, cleft, ear size / wing / lobe /
    rotation, neck width/depth, a double chin with weight or age; face shape (oval, round, square, long, heart, diamond)
    from MakeHuman's head shape targets; 2-4 of MakeHuman's asym targets.
  * stylise pass: eyes ~+10% (eye-scale), nose a little shorter and shallower with a rounder tip, the head 5.5% bigger
    (scaled about the top of the neck), the person's strongest face feature x1.35; skin painted with soft occlusion in
    creases and a warm flush, lid margins darkened.
  * expression from MakeHuman's expression units (blended by the same ethnicity mix): smile = corner puller + upper lip
    lift (teeth once it is broad) + cheeks squinting the lower lids; small values a friendly resting face; Vulcans and
    Romulans calm; Ferengi grin with parted lips and pointed teeth.
  * species: Vulcan/Romulan pointed ears (MakeHuman's pointed ear + a sculpted tip), Romulan forehead V ridge, Andorian
    antennae + blue skin, Tellarite snout + heavy brow, Bajoran nose ridges + right-ear chain, Trill spot bands,
    Betazoid black irises, Klingon crest and chevron ridges, Cardassian spoon + temple ridges + cobra neck cords (warm
    grey), Ferengi ears (scaled up, concentric ridges, scalloped rim), nose-bridge folds and pointed teeth. Ridges are
    displacements on a twice-subdivided copy of the body.
  * hair, brows, lashes and beards are build_portraits.py's strand generators (guides + clumped children, comb fields,
    blunt cuts, buns, ponytails), fed by ray casts against the MakeHuman head (radial field, front height map, hairline
    by azimuth, ear ellipsoids, body clearance); skin, hair, eye, outfit and backdrop materials are build_portraits'.

Toward the game (plan only, nothing exported yet):
  1. One rigged bust glb per species from the MakeHuman default rig (163 bones, its face bones included), with the face
     modifiers above as blend shapes (or a few hundred baked variants) - the same person() numbers in C# from the crew
     id hash give every crew member, rival captain and contact a stable face.
  2. Talking and expressions from MakeHuman's face pose units (data/poseunits/face-poseunits.bvh: 60 poses for the
     default rig's face bones - visemes, blinks, brows, smiles), blended by the dialogue system.
  3. Hair as cards baked from these strands; outfits as swappable meshes fitted on the tights helper.
  4. Godot renders the bust into a SubViewport on the view screen and caches a still per character for lists; the
     framing and lighting here are the target look.
"""
import gzip
import json
import math
import os
import sys
import zlib
import warnings

import numpy as np

warnings.filterwarnings("ignore", category=DeprecationWarning)

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
OUT = arg("--out") or os.path.join(REPO, "docs", "mockups", "character", "portraits")
MH = arg("--mh", "/home/user/makehumancommunity/makehuman")
VENDOR = os.path.join(HERE, "makehuman")

# ================================================================ MakeHuman data (vendored subset)

MH_DATA = os.path.join(MH, "makehuman", "data") if os.path.isdir(os.path.join(MH, "makehuman", "data")) else os.path.join(MH, "data")
TARGET_DIRS = ["head", "forehead", "eyebrows", "eyes", "nose", "mouth", "cheek", "chin", "ears", "neck", "torso", "asym"]
RACES = ["african", "asian", "caucasian"]


def _read_obj(path):
    v, faces, g = [], {}, None
    for line in open(path):
        if line.startswith("v "):
            v.append([float(t) for t in line.split()[1:4]])
        elif line.startswith("g "):
            g = line.split()[1]
        elif line.startswith("f "):
            faces.setdefault(g, []).append([int(t.split("/")[0]) - 1 for t in line.split()[1:]])
    return np.array(v, np.float32), faces


def _read_target(path):
    rows = [ln.split() for ln in open(path) if ln.strip() and not ln.startswith("#")]
    idx = np.array([int(r[0]) for r in rows], np.uint16)
    d = np.array([[float(x) for x in r[1:4]] for r in rows])
    return idx, np.round(d * 1000).astype(np.int16)


def vendor(dst=VENDOR):
    """Copy the subset of MakeHuman 1.x data this script uses into compact numpy files (CC0 assets)."""
    os.makedirs(dst, exist_ok=True)
    v, faces = _read_obj(os.path.join(MH_DATA, "3dobjs", "base.obj"))
    keep = {k: np.array(f, np.int32) for k, f in faces.items() if not k.startswith("joint-")}
    joints = {k: np.unique(np.array(f).ravel()).astype(np.int32) for k, f in faces.items() if k.startswith("joint-")}
    np.savez_compressed(os.path.join(dst, "base.npz"), verts=v, **{"f:" + k: f for k, f in keep.items()},
                        **{"j:" + k: j for k, j in joints.items()})
    T = {}
    td = os.path.join(MH_DATA, "targets")
    for r in RACES:
        for g in ("male", "female"):
            for a in ("young", "old"):
                T[f"macro/{r}-{g}-{a}"] = _read_target(os.path.join(td, "macrodetails", f"{r}-{g}-{a}.target"))
    for g in ("male", "female"):
        for a in ("young", "old"):
            for mu in ("minmuscle", "averagemuscle", "maxmuscle"):
                for w in ("minweight", "averageweight", "maxweight"):
                    p = os.path.join(td, "macrodetails", f"universal-{g}-{a}-{mu}-{w}.target")
                    if os.path.exists(p):
                        T[f"universal/{g}-{a}-{mu}-{w}"] = _read_target(p)
    for d in TARGET_DIRS:
        for fn in sorted(os.listdir(os.path.join(td, d))):
            if fn.endswith(".target"):
                T[f"{d}/{fn[:-7]}"] = _read_target(os.path.join(td, d, fn))
    for r in RACES:
        ed = os.path.join(td, "expression", "units", r)
        for fn in sorted(os.listdir(ed)):
            if fn.endswith(".target"):
                T[f"expr/{r}/{fn[:-7]}"] = _read_target(os.path.join(ed, fn))
    arrs = {}
    for k, (i, d) in T.items():
        arrs[k + ":i"] = i
        arrs[k + ":d"] = d
    np.savez_compressed(os.path.join(dst, "targets.npz"), **arrs)
    sk = json.load(open(os.path.join(MH_DATA, "rigs", "default.mhskel")))
    wt = json.load(open(os.path.join(MH_DATA, "rigs", "default_weights.mhw")))
    rig = dict(bones={b: dict(head=d["head"], tail=d["tail"], parent=d["parent"]) for b, d in sk["bones"].items()},
               joints=sk["joints"], weights={b: [[int(a), round(float(w), 4)] for a, w in l] for b, l in wt["weights"].items()})
    with gzip.open(os.path.join(dst, "rig.json.gz"), "wt") as fh:
        json.dump(rig, fh, separators=(",", ":"))
    print("vendored", len(T), "targets ->", dst, flush=True)


class MHData:
    def __init__(self, src=VENDOR):
        if not os.path.exists(os.path.join(src, "targets.npz")):
            vendor(src)
        b = np.load(os.path.join(src, "base.npz"))
        self.base = b["verts"].astype(np.float64)
        self.faces = {k[2:]: b[k] for k in b.files if k.startswith("f:")}
        self.joints = {k[2:]: b[k] for k in b.files if k.startswith("j:")}
        t = np.load(os.path.join(src, "targets.npz"))
        self._t = t
        self.names = sorted({k.rsplit(":", 1)[0] for k in t.files})
        self._cache = {}
        with gzip.open(os.path.join(src, "rig.json.gz"), "rt") as fh:
            self.rig = json.load(fh)

    def target(self, name):
        if name not in self._cache:
            self._cache[name] = (self._t[name + ":i"].astype(np.int64), self._t[name + ":d"].astype(np.float64).reshape(-1, 3) / 1000.0)
        return self._cache[name]

    def has(self, name):
        return name in self.names

    def apply(self, weights):
        """Base mesh + sum of weighted targets (MakeHuman units: decimetres, Y up, +Z forward)."""
        co = self.base.copy()
        for name, w in weights.items():
            if abs(w) < 1e-4:
                continue
            if not self.has(name):
                print("WARNING: missing target", name, flush=True)
                continue
            i, d = self.target(name)
            co[i] += w * d
        return co


def macro_weights(gender, age, race, muscle=0.5, weight=0.5):
    """MakeHuman's macro modifier weighting (apps/human.py _set*Vals + humanmodifier.getTargetWeights): every macro
    target's weight is the product of the macro variable values named in its file name.
    gender 0 female .. 1 male; age 0.5 = 25 years, 1 = 90 years; race {african, asian, caucasian} summing to 1."""
    gv = {"male": gender, "female": 1 - gender}
    old = max(0.0, age * 2 - 1)
    av = {"young": 1 - old, "old": old}
    mv = {"maxmuscle": max(0.0, muscle * 2 - 1), "minmuscle": max(0.0, 1 - muscle * 2)}
    mv["averagemuscle"] = 1 - mv["maxmuscle"] - mv["minmuscle"]
    wv = {"maxweight": max(0.0, weight * 2 - 1), "minweight": max(0.0, 1 - weight * 2)}
    wv["averageweight"] = 1 - wv["maxweight"] - wv["minweight"]
    out = {}
    for r, rv in race.items():
        for g in gv:
            for a in av:
                out[f"macro/{r}-{g}-{a}"] = rv * gv[g] * av[a]
    for g in gv:
        for a in av:
            for mu in mv:
                for w in wv:
                    out[f"universal/{g}-{a}-{mu}-{w}"] = gv[g] * av[a] * mv[mu] * wv[w]
    return out


def age_value(years):
    """MakeHuman age slider from years (MIN 1, MID 25, MAX 90); adults only here."""
    years = max(25.0, min(90.0, years))
    return 0.5 + (years - 25.0) / (2 * 65.0)


def to_blender(co):
    """MakeHuman (dm, Y up, facing +Z) -> Blender (m, Z up, facing +Y, feet on z = 0)."""
    return np.stack([-co[..., 0], co[..., 2], co[..., 1] + 8.4488], -1) * 0.1


if "--vendor" in argv:
    vendor()
    sys.exit(0)


# ================================================================ contact sheet (system python3 + PIL, no bpy)

DEPTS = {"exploration": "#3E7FD6", "defense": "#C8423A", "maintenance": "#D9A23A", "archaeology": "#4E9A5A"}


def _hexrgb_int(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


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
    d.text((pad, 14), "Character portraits - blender/build_portraits_mh.py (MakeHuman base mesh, mockup)", fill=(225, 228, 235))
    for i, (f, e) in enumerate(tiles):
        im = Image.open(os.path.join(folder, f)).convert("RGB").resize((tw, tw), Image.LANCZOS)
        x = pad + (i % cols) * (tw + pad)
        y = 44 + (i // cols) * (tw + lab + pad)
        sheet.paste(im, (x, y))
        txt = f"{e['species']} {e['sex']}" + (f" - {e['department']}" if e["department"] else f" - {e['faction']}")
        d.text((x + 2, y + tw + 4), txt, fill=(200, 205, 215))
        if e["department"]:
            d.rectangle((x, y + tw, x + tw - 1, y + tw + 2), fill=_hexrgb_int(DEPTS[e["department"]]))
    if lh:
        im = Image.open(lineup).convert("RGB").resize((lw, lh), Image.LANCZOS)
        sheet.paste(im, (pad, 44 + rows * (tw + lab + pad)))
    out = os.path.join(folder, "sheet.jpg")
    sheet.save(out, quality=85, optimize=True)
    print("sheet", out, sheet.size, os.path.getsize(out))


if "--sheet" in argv:
    contact_sheet(arg("--sheet"))
    sys.exit(0)

# ================================================================ Blender side: build_portraits.py as a module

import importlib.util  # noqa: E402

_saved_argv = sys.argv
sys.argv = [os.path.join(HERE, "build_portraits.py")]
_spec = importlib.util.spec_from_file_location("build_portraits", os.path.join(HERE, "build_portraits.py"))
bp = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bp)
sys.argv = _saved_argv
bc = bp.bc

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

V = Vector
pi = math.pi
rad = math.radians
sm, ga, nrm, rng = bp.sm, bp.ga, bp.nrm, bp.rng
srgb_lin = bp.srgb_lin
SK, HC, IR = bp.SK, bp.HC, bp.IR
FACTION_OF, FACTION_COLOUR = bp.FACTION_OF, bp.FACTION_COLOUR
RES = int(arg("--res", "640"))
SAMPLES = int(arg("--samples", "64"))
bp.SAMPLES = SAMPLES
bp.OUT = OUT
SHOTS = [s for s in arg("--shots", "portraits,lineup").split(",") if s]
ONLY_SPECIES = [s for s in (arg("--species") or "").split(",") if s]
ONLY_SEX = arg("--sex")
ONLY = [s for s in (arg("--only") or "").split(",") if s]
SEEDS = [int(s) for s in (arg("--seed") or "").split(",") if s]
NO_HAIR = "--no-hair" in argv
SKIP = set((arg("--skip") or "").split(","))      # debug: skip strand groups (hair, brow, lash, beard)
ORBIT = [float(x) for x in (arg("--orbit") or "").split(",") if x]
SK.update(fair=(0.86, 0.66, 0.55), light=(0.80, 0.59, 0.47), tan=(0.70, 0.49, 0.36), olive=(0.66, 0.49, 0.35),
          eastasian=(0.82, 0.63, 0.49), romulan=(0.80, 0.63, 0.51), ferengi=(0.84, 0.62, 0.46),
          brown=(0.52, 0.35, 0.24), dark=(0.37, 0.24, 0.17), deep=(0.27, 0.175, 0.13))
bp.BROWS["upswept"] = dict(arch=0.15, ap=0.62, tail=0.0, up=0.55, thick=1.0)
bp.STYLES["swept_back"] = dict(bp.STYLES["swept_back"], hug=0.016, rise=0.3)
D = None          # MHData, loaded in main()
CUR = bp.CUR      # shared with build_portraits' hair / brow / lash code


def H(seed, k):
    return bc.h32(seed, k)


# ================================================================ person -> MakeHuman modifier values

# (african, asian, caucasian) mix behind each skin preset (aliens: whatever reads best under their make-up)
RACE_OF_SKIN = dict(fair=(0.02, 0.06, 0.92), light=(0.05, 0.10, 0.85), tan=(0.18, 0.17, 0.65), olive=(0.10, 0.22, 0.68),
                    brown=(0.55, 0.12, 0.33), dark=(0.80, 0.05, 0.15), deep=(0.90, 0.02, 0.08),
                    eastasian=(0.02, 0.88, 0.10), andorian=(0.08, 0.22, 0.70), cardassian=(0.15, 0.20, 0.65),
                    ferengi=(0.10, 0.30, 0.60), tellarite=(0.30, 0.10, 0.60), klingon=(0.45, 0.10, 0.45),
                    klingon2=(0.60, 0.05, 0.35), romulan=(0.05, 0.25, 0.70))
NOSE_TYPES = ["straight", "aquiline", "roman", "snub", "button", "broad", "flat", "hooked", "greek"]
FACE_SHAPES = ["oval", "round", "square", "long", "heart", "diamond"]
# nose type -> MakeHuman nose modifiers (value -1..1 on each decr|incr / down|up ... pair)
NOSE_MODS = {
    "straight": {"hump": -0.25, "point_width": 0.15, "curve": -0.05},
    "aquiline": {"hump": 0.45, "point": -0.25, "curve": 0.35, "septumangle": 0.15, "point_width": 0.15},
    "roman": {"hump": 0.6, "width1": 0.25, "point": -0.05, "point_width": 0.2},
    "snub": {"point": 0.75, "curve": -0.55, "scale_vert": -0.35, "septumangle": -0.3, "point_width": 0.25},
    "button": {"scale_vert": -0.55, "scale_depth": -0.35, "volume": -0.15, "point_width": 0.45, "point": 0.35,
               "nostrils_width": -0.2},
    "broad": {"scale_horiz": 0.45, "flaring": 0.65, "nostrils_width": 0.55, "width3": 0.45, "width2": 0.3,
              "point_width": 0.35},
    "flat": {"scale_depth": -0.6, "width1": 0.35, "flaring": 0.45, "compression": -0.3, "point_width": 0.3},
    "hooked": {"hump": 0.5, "point": -0.45, "septumangle": 0.3, "point_width": 0.25},
    "greek": {"greek": 0.85, "hump": -0.2, "point_width": 0.1},
}
NOSE_PAIRS = dict(hump=("nose-hump", "decr", "incr"), point=("nose-point", "down", "up"),
                  curve=("nose-curve", "concave", "convex"), septumangle=("nose-septumangle", "decr", "incr"),
                  width1=("nose-width1", "decr", "incr"), width2=("nose-width2", "decr", "incr"),
                  width3=("nose-width3", "decr", "incr"), scale_vert=("nose-scale-vert", "decr", "incr"),
                  scale_horiz=("nose-scale-horiz", "decr", "incr"), scale_depth=("nose-scale-depth", "decr", "incr"),
                  volume=("nose-volume", "decr", "incr"), point_width=("nose-point-width", "decr", "incr"),
                  nostrils_width=("nose-nostrils-width", "decr", "incr"), flaring=("nose-flaring", "decr", "incr"),
                  compression=("nose-compression", "compress", "uncompress"), greek=("nose-greek", "decr", "incr"),
                  base=("nose-base", "down", "up"), trans_vert=("nose-trans", "down", "up"))
FACE_MODS = {
    "oval": {"head/head-oval": 0.7},
    "round": {"head/head-round": 0.75, "head/head-fat-incr": 0.25},
    "square": {"head/head-square": 0.8},
    "long": {"head/head-rectangular": 0.55, "head/head-scale-vert-incr": 0.25},
    "heart": {"head/head-invertedtriangular": 0.7},
    "diamond": {"head/head-diamond": 0.75},
}


class Mods(dict):
    """target name -> weight, with helpers for MakeHuman's paired (decr|incr) and sided (l-/r-) modifiers."""

    def add(self, name, w):
        if abs(w) > 1e-4:
            self[name] = self.get(name, 0.0) + w

    def pair(self, folder, base, lo, hi, v, side=None, asym=0.0):
        sides = [None] if side is None else ["l", "r"]
        for s in sides:
            vv = v + (asym if s == "l" else -asym if s == "r" else 0.0)
            vv = max(-1.0, min(1.0, vv))
            nm = base if s is None else f"{s}-{base}"
            self.add(f"{folder}/{nm}-{hi if vv > 0 else lo}", abs(vv))


def person(c):
    """Everything that shapes one person's body and face, from the cast entry (or look(seed)) and a look seed:
    MakeHuman macro values (gender, age, ethnicity mix, muscle, weight), ~60 face modifiers, light asymmetry,
    the stylise pass and the expression. Returns dict(macro=..., mods=Mods, expr=Mods, info=...)."""
    seed = c.get("seed_n") or zlib.crc32(c["id"].encode())
    sp, sex = c["species"], c["sex"]
    fem = sex == "f"
    h = lambda k: H(seed, 700 + k)  # noqa: E731

    def n(k, s=1.0):        # triangular noise -s..s
        return (h(k) + h(k + 101) - 1.0) * s
    years = c.get("years", 35)
    race = np.array(RACE_OF_SKIN.get(c["skin"], (0.2, 0.2, 0.6))) + np.array([n(1, 0.08), n(2, 0.08), n(3, 0.08)])
    race = np.clip(race, 0.0, 1.0)
    race = race / race.sum()
    racd = dict(zip(RACES, (float(x) for x in race)))
    heavy, slim = c.get("heavy", 0.0), c.get("slim", 0.0)
    macro = dict(gender=(0.04 * h(4) if fem else 0.96 + 0.04 * h(4)), age=age_value(years), race=racd,
                 muscle=float(np.clip(0.5 + (0.12 if not fem else 0.0) + n(5, 0.15) + 0.15 * heavy - 0.2 * slim, 0, 1)),
                 weight=float(np.clip(0.5 + 0.5 * heavy - 0.35 * slim + n(6, 0.12), 0, 1)))
    M = Mods()
    face = c.get("face") or FACE_SHAPES[int(h(10) * 6) % 6]
    nose = c.get("nose") or NOSE_TYPES[int(h(11) * len(NOSE_TYPES)) % len(NOSE_TYPES)]
    if sp == "tellarite":
        nose = "snout"
    for k, v in FACE_MODS[face].items():
        M.add(k, v * (0.8 + 0.4 * h(12)))
    # every feature gets its own value; remember them to exaggerate the strongest one in the stylise pass
    feat = {}

    def F(key, folder, base, lo, hi, v, side=None, asym=0.0):
        feat[key] = (folder, base, lo, hi, v, side, asym)

    # head proportions
    F("head_w", "head", "head-scale-horiz", "decr", "incr", n(20, 0.35) - 0.08 * fem)
    F("head_d", "head", "head-scale-depth", "decr", "incr", n(21, 0.3))
    F("head_v", "head", "head-scale-vert", "decr", "incr", n(22, 0.25))
    F("head_fat", "head", "head-fat", "decr", "incr", n(23, 0.35) + 0.4 * heavy - 0.3 * slim)
    F("head_age", "head", "head-age", "decr", "incr", -0.25 + 0.6 * max(0.0, (years - 35) / 30.0))
    # forehead and brow
    F("fore_v", "forehead", "forehead-scale-vert", "decr", "incr", n(25, 0.45))
    F("fore_nub", "forehead", "forehead-nubian", "decr", "incr", n(26, 0.5))
    F("fore_tr", "forehead", "forehead-trans", "backward", "forward", n(27, 0.35))
    F("temple", "forehead", "forehead-temple", "decr", "incr", n(28, 0.4))
    F("brow_fw", "eyebrows", "eyebrows-trans", "backward", "forward", n(29, 0.4) + (0.25 if not fem else -0.1))
    F("brow_v", "eyebrows", "eyebrows-trans", "down", "up", n(30, 0.35) + 0.1 * fem)
    F("brow_ang", "eyebrows", "eyebrows-angle", "down", "up", n(31, 0.45))
    # eyes
    F("eye_sp", "eyes", "eye-trans", "in", "out", n(33, 0.45), "lr", n(34, 0.05))
    F("eye_v", "eyes", "eye-trans", "down", "up", n(35, 0.35), "lr", n(36, 0.06))
    F("eye_h1", "eyes", "eye-height1", "decr", "incr", n(37, 0.5), "lr")
    F("eye_h2", "eyes", "eye-height2", "decr", "incr", n(38, 0.5) + 0.1 * fem, "lr", n(39, 0.08))
    F("eye_h3", "eyes", "eye-height3", "decr", "incr", n(40, 0.4), "lr")
    F("eye_c1", "eyes", "eye-corner1", "down", "up", n(41, 0.45), "lr")
    F("eye_c2", "eyes", "eye-corner2", "down", "up", n(42, 0.5) + 0.15 * fem, "lr", n(43, 0.06))
    F("eye_fold", "eyes", "eye-eyefold", "down", "up", n(44, 0.5), "lr")
    F("eye_foldc", "eyes", "eye-eyefold", "concave", "convex", n(45, 0.4), "lr")
    F("eye_bag", "eyes", "eye-bag", "decr", "incr", -0.3 + 0.7 * max(0.0, (years - 35) / 30.0) + n(46, 0.2), "lr")
    F("epic", "eyes", "eye-epicanthus", "in", "out", (0.6 if c.get("monolid") else 0.0) + n(47, 0.25) +
      0.5 * racd["asian"], "lr")
    # nose
    base = NOSE_MODS.get(nose, {})
    for k, (b, lo, hi) in NOSE_PAIRS.items():
        v = base.get(k, 0.0) + n(50 + list(NOSE_PAIRS).index(k), 0.18)
        F("nose_" + k, "nose", b, lo, hi, v)
    # mouth and lips
    F("mouth_w", "mouth", "mouth-scale-horiz", "decr", "incr", n(70, 0.4) - 0.1 * fem)
    F("ulip", "mouth", "mouth-upperlip-volume", "decr", "incr", n(71, 0.45) + 0.25 * fem + 0.25 * racd["african"]
      - 0.35 * max(0.0, (years - 40) / 25.0))
    F("llip", "mouth", "mouth-lowerlip-volume", "decr", "incr", n(72, 0.45) + 0.3 * fem + 0.25 * racd["african"]
      - 0.3 * max(0.0, (years - 40) / 25.0))
    F("cupid", "mouth", "mouth-cupidsbow", "decr", "incr", n(73, 0.5) + 0.2 * fem)
    F("uh", "mouth", "mouth-upperlip-height", "decr", "incr", n(74, 0.4))
    F("mouth_v", "mouth", "mouth-trans", "down", "up", n(75, 0.3))
    F("mouth_fw", "mouth", "mouth-trans", "backward", "forward", n(76, 0.3))
    F("philtrum", "mouth", "mouth-philtrum-volume", "decr", "incr", n(77, 0.4))
    F("dimples", "mouth", "mouth-dimples", "in", "out", n(78, 0.6) if h(79) < 0.3 else 0.0)
    F("laugh", "mouth", "mouth-laugh-lines", "in", "out", 0.5 * max(0.0, (years - 35) / 25.0))
    # cheeks
    F("cheek_b", "cheek", "cheek-bones", "decr", "incr", n(80, 0.5) + 0.1 * fem, "lr", n(81, 0.05))
    F("cheek_vol", "cheek", "cheek-volume", "decr", "incr", n(82, 0.45) + 0.3 * heavy - 0.25 * slim, "lr")
    F("cheek_in", "cheek", "cheek-inner", "decr", "incr", n(83, 0.4), "lr")
    F("cheek_v", "cheek", "cheek-trans", "down", "up", n(84, 0.35), "lr")
    # chin and jaw
    F("chin_prom", "chin", "chin-prominent", "decr", "incr", n(85, 0.45) + (0.2 if not fem else 0.0))
    F("chin_w", "chin", "chin-width", "decr", "incr", n(86, 0.45) + (0.2 if not fem else -0.15))
    F("chin_h", "chin", "chin-height", "decr", "incr", n(87, 0.4))
    F("jaw", "chin", "chin-bones", "decr", "incr", n(88, 0.5) + (0.3 if not fem else -0.2))
    F("jaw_drop", "chin", "chin-jaw-drop", "decr", "incr", n(89, 0.35))
    F("progn", "chin", "chin-prognathism", "decr", "incr", n(90, 0.25))
    F("cleft", "chin", "chin-cleft", "decr", "incr", (0.7 if (not fem and h(91) < 0.2) else 0.0))
    # ears
    F("ear_s", "ears", "ear-scale", "decr", "incr", n(92, 0.35) + 0.25 * max(0.0, (years - 40) / 25.0), "lr")
    F("ear_wing", "ears", "ear-wing", "decr", "incr", n(93, 0.5), "lr", n(94, 0.1))
    F("ear_rot", "ears", "ear-rot", "backward", "forward", n(95, 0.3), "lr")
    F("ear_lobe", "ears", "ear-lobe", "decr", "incr", n(96, 0.5), "lr")
    F("ear_flap", "ears", "ear-flap", "decr", "incr", n(97, 0.4), "lr")
    # neck
    F("neck_w", "neck", "neck-scale-horiz", "decr", "incr", (0.35 if not fem else -0.15) + 0.4 * heavy + n(98, 0.15))
    F("neck_d", "neck", "neck-scale-depth", "decr", "incr", (0.2 if not fem else -0.1) + 0.3 * heavy)
    F("neck_dbl", "neck", "neck-double", "decr", "incr", max(0.0, 0.7 * heavy + 0.3 * max(0.0, (years - 45) / 20.0) - 0.2))
    # ---- stylise: slightly larger eyes and a softer, shorter nose for everyone; the strongest feature x1.35
    style_keys = [k for k in feat if not k.startswith(("head_age", "eye_bag", "laugh", "neck_", "epic"))]
    strongest = max(style_keys, key=lambda k: abs(feat[k][4]))
    for k, (folder, b, lo, hi, v, side, asym) in feat.items():
        if k == strongest:
            v *= 1.35
        M.pair(folder, b, lo, hi, v, side, asym)
    M.pair("eyes", "eye-scale", "decr", "incr", 0.5 + n(99, 0.12), "lr")
    M.pair("nose", "nose-scale-vert", "decr", "incr", -0.25)
    M.pair("nose", "nose-scale-depth", "decr", "incr", -0.18)
    M.pair("nose", "nose-point-width", "decr", "incr", 0.22)
    # asymmetry: two or three MakeHuman asym targets, small
    for j, part in enumerate(["brown-1", "cheek-1", "eye-2", "jaw-1", "nose-2", "ear-1", "temple-1"]):
        if h(110 + j) < 0.45:
            M.add(f"asym/asym-{part}-{'l' if h(120 + j) < 0.5 else 'r'}", 0.12 + 0.25 * h(130 + j))
    # species shaping on the base head (sculpted features come later as geometry)
    if sp in ("vulcan", "romulan"):
        M.pair("ears", "ear-shape", "pointed", "triangle", -0.9, "lr")
        M.pair("ears", "ear-scale-vert", "decr", "incr", 0.45, "lr")
    if sp == "klingon":
        M.pair("eyebrows", "eyebrows-trans", "backward", "forward", 0.6)
        M.pair("forehead", "forehead-nubian", "decr", "incr", 0.6)
        M.pair("chin", "chin-bones", "decr", "incr", 0.3)
    if sp == "tellarite":
        M.pair("eyebrows", "eyebrows-trans", "backward", "forward", 0.8)
        M.pair("nose", "nose-scale-horiz", "decr", "incr", 0.7)
        M.pair("nose", "nose-point", "down", "up", 0.9)
        M.pair("nose", "nose-curve", "concave", "convex", -0.8)
        M.pair("nose", "nose-flaring", "decr", "incr", 1.0)
        M.pair("head", "head-fat", "decr", "incr", 0.5)
    if sp == "ferengi":
        M.pair("nose", "nose-scale-vert", "decr", "incr", 0.25)
        M.pair("forehead", "forehead-scale-vert", "decr", "incr", 0.6)
        M.pair("head", "head-scale-vert", "decr", "incr", 0.3)
        M.pair("eyebrows", "eyebrows-trans", "backward", "forward", 0.4)
    if sp == "cardassian":
        M.pair("neck", "neck-scale-horiz", "decr", "incr", 0.6)
    # ---- expression (MakeHuman expression units, blended by ethnicity like the macro targets)
    E = Mods()
    smile = c.get("smile", 0.15)
    if sp in ("vulcan", "romulan"):
        smile = min(smile, 0.08)
    grin = sp == "ferengi"
    units = {}
    if grin:
        units.update({"mouth-corner-puller": 0.9, "mouth-parling": 0.55, "mouth-retraction": 0.3,
                      "mouth-upward-retraction": 0.35, "eye-left-slit": 0.25, "eye-right-slit": 0.25})
    elif smile > 0.0:
        # a real smile: corners up and back (zygomaticus), the upper lip lifting off the teeth once it is broad,
        # cheeks pushing the lower lids up; small values = a friendly resting face
        s = min(0.85, smile * 1.3)
        units.update({"mouth-corner-puller": 0.35 + 0.65 * s if s > 0.05 else 0.0,
                      "mouth-upward-retraction": 0.62 * float(sm(0.25, 0.9, s)),
                      "eye-left-slit": 0.32 * float(sm(0.15, 0.8, s)), "eye-right-slit": 0.32 * float(sm(0.15, 0.8, s)),
                      "mouth-parling": 0.15 * float(sm(0.5, 1.0, s))})
    if sp in ("vulcan", "romulan"):
        units["eyebrows-left-up"] = units.get("eyebrows-left-up", 0.0) + (0.12 if c.get("smirk") else 0.0)
    elif c.get("smirk"):
        units["eyebrows-left-extern-up"] = 0.2
    if arg("--expr") is not None:      # debug: --expr unit:weight,unit:weight replaces the expression
        units = {kv.split(":")[0]: float(kv.split(":")[1]) for kv in arg("--expr").split(",") if kv}
    for u, w in units.items():
        for r, rv in racd.items():
            E.add(f"expr/{r}/{u}", w * rv)
    info = dict(seed=seed, face=face, nose=nose, strongest=strongest, years=years, race=racd, smile=smile)
    return dict(macro=macro, mods=M, expr=E, info=info, seed=seed)


# ================================================================ the MakeHuman body in Blender

HEAD_SCALE = 1.055      # stylise: a slightly bigger head (scaled about the top of the neck)


def region(name):
    """0..1 weight per vertex (all 19158) from how far a target moves it: masks for the nose, lips, cheeks ..."""
    w = np.zeros(len(D.base))
    names = name if isinstance(name, (list, tuple)) else [name]
    for nm in names:
        i, d = D.target(nm)
        m = np.linalg.norm(d, axis=1)
        w[i] = np.maximum(w[i], m / max(m.max(), 1e-9))
    return w


class Human:
    """One person: MakeHuman coordinates with every target applied, stylised, in Blender space (metres, +Y forward)."""

    def __init__(self, c, P):
        self.c, self.P = c, P
        m = P["macro"]
        w = macro_weights(m["gender"], m["age"], m["race"], m["muscle"], m["weight"])
        w.update(P["mods"])
        w.update(P["expr"])
        co = to_blender(D.apply(w))
        # stylise: bigger head, scaled about the top of the neck (fades out down the neck)
        jh = co[D.joints["joint-head"]].mean(0)
        jn = co[D.joints["joint-neck"]].mean(0)
        piv = jh + (jn - jh) * 0.25
        k = sm(piv[2] - 0.035, piv[2] + 0.03, co[:, 2])
        co = co + (co - piv) * ((HEAD_SCALE - 1.0) * k)[:, None]
        self.co = co
        self.fem = c["sex"] == "f"
        ie = {s: np.unique(D.faces[f"helper-{s}-eye"].ravel()) for s in "lr"}
        self.eye_c = {s: co[ie[s]].mean(0) for s in "lr"}
        self.eye_r = {s: float(np.linalg.norm(co[ie[s]] - self.eye_c[s], axis=1).mean()) for s in "lr"}
        bi = np.unique(D.faces["body"].ravel())
        self.body_idx = bi
        ez = (self.eye_c["l"][2] + self.eye_c["r"][2]) / 2
        # head centre in build_portraits' head frame: 12 mm below the eyes, half way front-back
        bco = co[bi]
        sl = bco[(np.abs(bco[:, 0]) < 0.01) & (np.abs(bco[:, 2] - (ez + 0.03)) < 0.01)]
        self.head_c = np.array([0.0, (sl[:, 1].max() + sl[:, 1].min()) / 2, ez - 0.012])
        self.ez = ez

    def joint(self, name):
        return self.co[np.array(D.rig["joints"][name])].mean(0)


def mesh_from(name, co, faces, mats=None):
    """Mesh object from global vertex coordinates and faces (global indices); returns (object, used global indices)."""
    used = np.unique(faces.ravel())
    remap = np.full(len(co), -1, np.int64)
    remap[used] = np.arange(len(used))
    me = bpy.data.meshes.new(name)
    fl = remap[faces]
    me.from_pydata(co[used].tolist(), [], fl.tolist())
    me.validate(verbose=False)
    me.update()
    me.shade_smooth()
    for m in mats or []:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob, used


def build_rig(hm, name="Rig"):
    """MakeHuman default skeleton (163 bones) from its joint vertex groups."""
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bones = D.rig["bones"]
    for b, d in bones.items():
        eb = arm.edit_bones.new(b)
        eb.head = V(hm.joint(d["head"]).tolist())
        eb.tail = V(hm.joint(d["tail"]).tolist())
        if (eb.tail - eb.head).length < 1e-4:
            eb.tail = eb.head + V((0, 0, 0.01))
    for b, d in bones.items():
        if d["parent"]:
            arm.edit_bones[b].parent = arm.edit_bones[d["parent"]]
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def skin_to(ob, used, rig):
    """Vertex groups from MakeHuman's default weights (global indices -> this object's) + an armature modifier."""
    loc = np.full(len(D.base), -1, np.int64)
    loc[used] = np.arange(len(used))
    for b, lst in D.rig["weights"].items():
        a = np.array(lst, np.float64)
        li = loc[a[:, 0].astype(np.int64)]
        ok = li >= 0
        if not ok.any():
            continue
        vg = ob.vertex_groups.new(name=b)
        ws = np.round(a[ok, 1], 3)
        for wv in np.unique(ws):
            vg.add(li[ok][ws == wv].tolist(), float(wv), "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    mod.use_deform_preserve_volume = True
    ob.parent = rig


def rotate_bone(rig, name, axis, angle, pivot=None):
    """Rotate a pose bone in armature space about an axis through its head (or pivot)."""
    pb = rig.pose.bones[name]
    Mb = pb.matrix.copy()
    p = Mb.translation if pivot is None else V(pivot)
    R = Matrix.Translation(p) @ Matrix.Rotation(angle, 4, V(axis).normalized()) @ Matrix.Translation(-p)
    pb.matrix = R @ Mb
    bpy.context.view_layer.update()


def aim_bone(rig, name, direction, amount=1.0):
    """Turn a bone (about its head) so it points along direction (armature space)."""
    pb = rig.pose.bones[name]
    cur = (pb.tail - pb.head).normalized()
    q = cur.rotation_difference(V(direction).normalized())
    ax, ang = q.to_axis_angle()
    if ang > 1e-5:
        rotate_bone(rig, name, ax, ang * amount)


def pose_body(rig, hm, head_turn=0.0, arms="down", t=0.0):
    """Arms down from MakeHuman's A pose (shoulders relaxed), then the head turned by head_turn degrees (spread over
    the neck)."""
    for s, sx in (("L", -1.0), ("R", 1.0)):
        if arms == "down":
            aim_bone(rig, f"clavicle.{s}", (sx * 0.95, -0.05, -0.26), 0.35)
            aim_bone(rig, f"upperarm01.{s}", (sx * 0.17, -0.02 + 0.03 * t, -1.0))
            pb = rig.pose.bones[f"upperarm01.{s}"]
            d = (pb.tail - pb.head).normalized()
            aim_bone(rig, f"lowerarm01.{s}", (d * 0.93 + V((0, 0.36, 0))).normalized())
            aim_bone(rig, f"wrist.{s}", ((rig.pose.bones[f"lowerarm01.{s}"].tail - rig.pose.bones[f"lowerarm01.{s}"].head)
                                         .normalized() + V((-sx * 0.15, 0.05, 0))).normalized())
    for b, k in (("neck01", 0.3), ("neck02", 0.3), ("neck03", 0.2), ("head", 0.2)):
        rotate_bone(rig, b, (0, 0, 1), rad(head_turn) * k)


def head_matrix(rig):
    """Rest -> posed transform of the head (armature object space and world)."""
    pb = rig.pose.bones["head"]
    return rig.matrix_world @ pb.matrix @ pb.bone.matrix_local.inverted()


# ---------------------------------------------------------------- skin colour zones on the MakeHuman head

def skin_zones(hm, c):
    """Per-vertex tint (RGB multiplier) and skm (oil, freckle zone, age-spot zone) for all vertices (global)."""
    sp = c["species"]
    co = hm.co
    n = len(co)
    q = co - hm.head_c
    x, y, z = q[:, 0], q[:, 1], q[:, 2]
    fy = sm(0.0, 0.05, y)
    sk = np.array(SK[c["skin"]])
    alien = sp in ("andorian", "cardassian")
    tint = np.ones((n, 3))

    def toward(col, amt):
        nonlocal tint
        col = np.asarray(col, float)
        amt = np.clip(amt, 0, 1)
        tint = tint * (1 - amt[:, None]) + tint * col[None, :] * amt[:, None]
    flush = np.array({"andorian": (0.90, 0.84, 1.08), "cardassian": (1.10, 0.95, 0.80), "klingon": (1.07, 0.88, 0.84),
                      "tellarite": (1.08, 0.80, 0.78)}.get(sp, (1.05, 0.74, 0.72)))
    R = hm.regions
    smile = hm.P["info"]["smile"]
    warm = 0.8 * R["cheek"] * (0.7 + 0.5 * smile) + 0.75 * R["nose"] ** 1.5 + 0.35 * R["chin"] + 0.85 * R["ear"]
    toward(flush, warm)
    lipc = {"andorian": (0.78, 0.74, 0.98), "cardassian": (0.86, 0.72, 0.70), "ferengi": (0.92, 0.66, 0.62),
            "klingon": (0.8, 0.6, 0.6)}.get(sp, (0.88, 0.60, 0.60) if sk[0] > 0.6 else (0.76, 0.60, 0.62))
    if hm.fem and sp in ("human", "trill", "betazoid", "bajoran", "romulan", "cardassian", "ferengi"):
        lipc = tuple(v * w for v, w in zip(lipc, (0.97, 0.84, 0.87)))
    toward(lipc, sm(0.25, 0.6, R["lips"]))
    ex = abs(hm.eye_c["l"][0])
    ax = np.abs(x)
    ez = hm.ez - hm.head_c[2]
    lid = ga(ax - ex, 0.013) * ga(z - (ez + 0.008), 0.006) * fy
    under = ga(ax - (ex - 0.002), 0.012) * ga(z - (ez - 0.013), 0.005) * fy
    toward((0.93, 0.80, 0.84) if not alien else (0.85, 0.85, 0.95), 0.55 * lid)
    toward((0.86, 0.80, 0.86) if not alien else (0.85, 0.85, 0.92), (0.4 + 0.4 * hm.age01) * under)
    # lid margins: a soft darker, redder line where the lids meet the eye (reads as lash line and wet rim)
    ap = np.concatenate([hm.apertures["l"][0], hm.apertures["r"][0]])
    dmin = np.full(n, 9.0)
    for a_ in ap:
        dmin = np.minimum(dmin, np.linalg.norm(co - a_, axis=1))
    toward((0.62, 0.45, 0.45) if not alien else (0.6, 0.6, 0.75), 0.75 * sm(0.0035, 0.0008, dmin))
    toward((0.45, 0.28, 0.25), 0.7 * R["nostril"])
    toward((0.55, 0.30, 0.28), 0.9 * R["mouth_in"])
    bz_ = hm.beard
    if not hm.fem and sp not in ("andorian", "ferengi", "cardassian"):
        hcol = HC[c.get("hair") or "darkbrown"][0]
        toward((0.80, 0.80, 0.86), bz_ * (0.30 if c.get("beard") else 0.17) * hcol)
    if c.get("style") and not NO_HAIR:
        hm_ = HC[c.get("hair") or "darkbrown"]
        hrgb = np.array(bp.hair_rgb_guess(hm_, c.get("grey", 0.0)))
        rel = np.clip(hrgb / np.maximum(sk, 0.02), 0.05, 3.0)
        cov = hm.scalp[:, None] * 0.85
        tint = tint * (1 - cov) + rel[None, :] * cov
    # painted-in occlusion: creases (lid folds, nostrils, mouth corners, ear bowls, smile folds) a touch darker,
    # ridges a touch lighter, as in hand-painted stylised skin
    cv = hm.cavity
    face = sm(0.17, 0.12, np.linalg.norm(q, axis=1))
    toward(np.array((0.78, 0.66, 0.66)) if not alien else np.array((0.75, 0.75, 0.8)), 0.55 * sm(0.04, 0.35, cv) * face)
    tint = tint * (1 + 0.05 * sm(-0.04, -0.3, cv) * face)[:, None]
    bzr = hm.brow_z - hm.head_c[2]
    tz = hm.nose_tip[2] - hm.head_c[2]
    cz = hm.chin_z - hm.head_c[2]
    tzone = np.clip(ga(x, 0.024) * ga(z - (bzr + 0.025), 0.022) + ga(x, 0.01) * ga(z - (tz + 0.012), 0.02)
                    + 0.6 * ga(x, 0.015) * ga(z - cz, 0.01), 0, 1) * fy
    tzone = np.maximum(tzone, 0.9 * sm(0.25, 0.6, R["lips"]))
    frz = np.clip(ga(ax - 0.024, 0.026) * ga(z - (ez - 0.024), 0.018) + 0.6 * ga(x, 0.011) * ga(z - (tz + 0.01), 0.016)
                  + 0.25 * ga(z - (bzr + 0.025), 0.02), 0, 1) * fy
    agez = np.clip(0.6 * ga(z - (bzr + 0.03), 0.03) + 0.7 * ga(ax - 0.05, 0.02) * ga(z - (ez - 0.01), 0.025), 0, 1) * fy
    skm = np.stack([tzone, frz, agez, np.ones(n)], -1)
    return np.concatenate([tint, np.ones((n, 1))], -1), skm


def landmarks(hm):
    """Face landmarks and region masks on the (rest) MakeHuman head."""
    co = hm.co
    c = hm.c
    hm.age01 = float(np.clip((hm.P["info"]["years"] - 27.0) / 36.0, 0, 1))
    bco = co[hm.body_idx]
    q = bco - hm.head_c
    mid = np.abs(q[:, 0]) < 0.004
    ez = hm.ez - hm.head_c[2]
    sel = mid & (q[:, 2] < ez - 0.015) & (q[:, 2] > ez - 0.07)
    hm.nose_tip = bco[sel][np.argmax(q[sel, 1])]
    ch = mid & (q[:, 2] < ez - 0.06) & (q[:, 1] > 0.03)
    hm.chin_z = float(bco[ch][:, 2].min())
    hm.brow_z = hm.ez + 0.021
    R = {}
    R["cheek"] = region(["cheek/l-cheek-volume-incr", "cheek/r-cheek-volume-incr"])
    R["nose"] = region(["nose/nose-scale-horiz-incr", "nose/nose-scale-depth-incr"])
    R["chin"] = region("chin/chin-prominent-incr")
    R["ear"] = region(["ears/l-ear-trans-up", "ears/r-ear-trans-up"])
    lips = region(["mouth/mouth-upperlip-volume-incr", "mouth/mouth-lowerlip-volume-incr"])
    R["lips"] = lips
    R["nostril"] = np.zeros(len(co))
    R["mouth_in"] = np.zeros(len(co))
    hm.regions = R
    # mouth: the lip region's middle
    li = lips > 0.4
    hm.mouth = co[li].mean(0) if li.any() else hm.nose_tip + np.array([0, -0.01, -0.03])
    # ears: centre and extent of each ear
    hm.ears = {}
    for s, nm in (("l", "ears/l-ear-trans-up"), ("r", "ears/r-ear-trans-up")):
        i, d = D.target(nm)
        mag = np.linalg.norm(d, axis=1)
        pts = co[i[mag > 0.6 * mag.max()]]
        lo_, hi_ = np.percentile(pts, 3, 0), np.percentile(pts, 97, 0)
        hm.ears[s] = ((lo_ + hi_) / 2, (hi_ - lo_) / 2)
    hm.beard = beard_zone_mh(hm, co - hm.head_c)
    # curvature of the body surface (Laplacian along the normal, per mean edge length): + in creases
    fl = D.faces["body"]
    vn = vertex_normals(co, fl)
    e = np.concatenate([np.stack([fl[:, k], fl[:, (k + 1) % 4]], 1) for k in range(4)])
    e = np.unique(np.sort(e, 1), axis=0)
    acc = np.zeros_like(co)
    cnt = np.zeros(len(co))
    np.add.at(acc, e[:, 0], co[e[:, 1]])
    np.add.at(acc, e[:, 1], co[e[:, 0]])
    np.add.at(cnt, e[:, 0], 1)
    np.add.at(cnt, e[:, 1], 1)
    el = np.zeros(len(co))
    L = np.linalg.norm(co[e[:, 0]] - co[e[:, 1]], axis=1)
    np.add.at(el, e[:, 0], L)
    np.add.at(el, e[:, 1], L)
    lap = acc / np.maximum(cnt, 1)[:, None] - co
    curv = np.sum(lap * vn, 1) / np.maximum(el / np.maximum(cnt, 1), 1e-6)
    # smooth once so it reads as soft shading, not per-vertex noise
    sm_ = np.zeros(len(co))
    np.add.at(sm_, e[:, 0], curv[e[:, 1]])
    np.add.at(sm_, e[:, 1], curv[e[:, 0]])
    hm.cavity = 0.5 * curv + 0.5 * sm_ / np.maximum(cnt, 1)
    hm.scalp = np.zeros(len(co))
    hm.apertures = lid_apertures(hm)


def beard_zone_mh(hm, Q):
    """Where a man's beard grows (0..1), head-local positions (head_c frame)."""
    x, y, z = Q[:, 0], Q[:, 1], Q[:, 2]
    ax = np.abs(x)
    ez = hm.ez - hm.head_c[2]
    sz = hm.nose_tip[2] - hm.head_c[2] - 0.012        # under the nose
    mz = hm.mouth[2] - hm.head_c[2]
    cz = hm.chin_z - hm.head_c[2]
    a = np.degrees(np.abs(np.arctan2(x, np.maximum(y, -0.2))))
    ztop = sz + 0.004 * sm(10, 26, a) - 0.004 * ga(a - 34, 8) + 0.03 * sm(40, 76, a) + 0.022 * sm(72, 92, a)
    m = sm(ztop + 0.004, ztop - 0.004, z) * sm(100, 88, a) * sm(-0.04, -0.015, y)
    lipm = ga(ax / 0.026, 1.0) ** 2 * ga((z - mz) / 0.0075, 1.0)
    under = sm(-0.02, 0.0, z - (cz - 0.035))
    return np.clip(m * (1 - sm(0.3, 0.7, lipm)) * under, 0, 1) * (ez > -1)


# ---------------------------------------------------------------- eyes

def eyeball(ce, r, gaze_to, up, iris_la=57.5):
    """Sclera + recessed iris plane + pupil (one 'eye' material, attribute eyep) and a cornea dome, world space,
    looking at gaze_to."""
    cw = V(ce)
    g = (V(gaze_to) - cw).normalized()
    e1 = V(up).cross(g).normalized()
    e2 = g.cross(e1)
    zi = r * math.sin(rad(iris_la))
    ri = r * math.cos(rad(iris_la))
    nla, nlo = 60, 64
    verts, ep, faces = [], [], []
    las = [-89 + 179 * i / (nla - 1) for i in range(nla)]
    for la in las:
        for j in range(nlo):
            lo = 2 * pi * j / nlo
            cl, sl = math.cos(rad(la)), math.sin(rad(la))
            u, v = cl * math.cos(lo), cl * math.sin(lo)
            if la > iris_la:
                rr = (cl / math.cos(rad(iris_la)))
                p = (u * r, v * r, zi - 0.00035 * (1 - rr ** 2))
                w = 1.0
            else:
                p = (u * r, v * r, sl * r)
                w = 0.0
            verts.append(cw + e1 * p[0] + e2 * p[1] + g * p[2])
            ep.append((p[0] / ri, p[1] / ri, w, 1.0))
    for i in range(nla - 1):
        for j in range(nlo):
            k = (j + 1) % nlo
            faces.append((i * nlo + j, i * nlo + k, (i + 1) * nlo + k, (i + 1) * nlo + j))
    tip = len(verts)
    verts.append(cw + g * (zi - 0.00035))
    ep.append((0.0, 0.0, 1.0, 1.0))
    for j in range(nlo):
        faces.append(((nla - 1) * nlo + j, (nla - 1) * nlo + (j + 1) % nlo, tip))
    bot = len(verts)
    verts.append(cw - g * r)
    ep.append((0.0, 0.0, 0.0, 1.0))
    for j in range(nlo):
        faces.append(((j + 1) % nlo, j, bot))
    me = bpy.data.meshes.new("Eye")
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.shade_smooth()
    me.materials.append(bp.material("eye"))
    bp.set_attr(me, "eyep", np.array(ep), "FLOAT_COLOR")
    eo = bpy.data.objects.new("Eye", me)
    bpy.context.scene.collection.objects.link(eo)
    # cornea: a dome over the iris blending into a sphere a hair bigger than the eyeball
    cv, cf = [], []
    for i in range(40):
        la = -60 + 150 * i / 39
        for j in range(48):
            lo = 2 * pi * j / 48
            cl, sl = math.cos(rad(la)), math.sin(rad(la))
            bulge = r * 0.16 * bc.smooth(44, 88, la) ** 1.2
            cv.append(cw + (e1 * (cl * math.cos(lo)) + e2 * (cl * math.sin(lo)) + g * sl) * (r * 1.012) + g * bulge)
    for i in range(39):
        for j in range(48):
            k = (j + 1) % 48
            cf.append((i * 48 + j, i * 48 + k, (i + 1) * 48 + k, (i + 1) * 48 + j))
    t2 = len(cv)
    cv.append(cw + g * (r * 1.012 + r * 0.16))
    for j in range(48):
        cf.append((39 * 48 + j, 39 * 48 + (j + 1) % 48, t2))
    cm = bpy.data.meshes.new("Cornea")
    cm.from_pydata([tuple(v) for v in cv], [], cf)
    cm.shade_smooth()
    cm.materials.append(bp.material("Cornea"))
    co_ = bpy.data.objects.new("Cornea", cm)
    bpy.context.scene.collection.objects.link(co_)
    return [eo, co_]


# ================================================================ build_portraits' strand code on the MakeHuman head
# The hair / brow / lash / beard generators of build_portraits.py work in a head-local frame (origin 12 mm below the
# eyes, +y forward, z up, metres) and sample the scalp by (latitude, longitude) through head_xyz(); here those are
# answered by ray casts against the MakeHuman head instead of build_portraits' parametric head.

def body_bvh_rest(hm):
    fl = D.faces["body"]
    return BVHTree.FromPolygons([tuple(p) for p in (hm.co - hm.head_c)], fl.tolist(), all_triangles=False)


class MHField:
    """Radius of the head surface by direction from the head centre (0.5 deg grid), like build_portraits.HeadField."""

    def __init__(self, bvh, step=0.5, rmax=0.142):
        self.step = step
        els = np.arange(-90, 90 + step / 2, step)
        azs = np.arange(0, 360, step)
        g = np.zeros((len(els), len(azs)))
        o = V((0, 0, 0))
        for i, el in enumerate(els):
            ce, se = math.cos(rad(el)), math.sin(rad(el))
            for j, az in enumerate(azs):
                d = V((ce * math.cos(rad(az)), ce * math.sin(rad(az)), se))
                # the outermost surface within rmax (rays pass the mouth cavity and eye sockets first)
                p0, last = o, 0.0
                for _ in range(8):
                    hit = bvh.ray_cast(p0, d, 0.3)[0]
                    if hit is None or hit.length > rmax:
                        break
                    last = hit.length
                    p0 = hit + d * 1e-5
                g[i, j] = last
        for _ in range(60):
            empty = g <= 0
            if not empty.any():
                break
            nb = np.stack([np.roll(g, 1, 0), np.roll(g, -1, 0), np.roll(g, 1, 1), np.roll(g, -1, 1)])
            g = np.where(empty, nb.max(0), g)
        self.g = g
        self.ne, self.na = g.shape

    def R(self, p):
        r = np.linalg.norm(p, axis=-1)
        el = (np.degrees(np.arcsin(np.clip(p[..., 2] / np.maximum(r, 1e-9), -1, 1))) + 90) / self.step
        az = (np.degrees(np.arctan2(p[..., 1], p[..., 0])) % 360) / self.step
        i0 = np.clip(np.floor(el).astype(int), 0, self.ne - 2)
        j0 = np.floor(az).astype(int) % self.na
        j1 = (j0 + 1) % self.na
        ti, tj = np.clip(el - i0, 0, 1), az - np.floor(az)
        g = self.g
        return (g[i0, j0] * (1 - ti) * (1 - tj) + g[i0, j1] * (1 - ti) * tj + g[i0 + 1, j0] * ti * (1 - tj)
                + g[i0 + 1, j1] * ti * tj), r

    def xyz(self, la, lo):
        la, lo = np.asarray(la, float), np.asarray(lo, float)
        d = np.stack([np.cos(np.radians(la)) * np.cos(np.radians(lo)), np.cos(np.radians(la)) * np.sin(np.radians(lo)),
                      np.sin(np.radians(la))], -1)
        R, _ = self.R(d)
        return d * R[..., None]

    push = bp.HeadField.push
    hug = bp.HeadField.hug


class MHGrid(bp.FaceGrid):
    """Front height map y(x, z) of the head (head-local), 0.5 mm grid, by ray casts from the front."""

    def __init__(self, bvh):
        self.x0, self.z0, self.h = -0.095, -0.14, 0.0005
        nx, nz = int(0.19 / self.h) + 1, int(0.28 / self.h) + 1
        g = np.full((nz, nx), -1.0)
        d = V((0, -1, 0))
        for j in range(nz):
            z = self.z0 + j * self.h
            for i in range(nx):
                hit = bvh.ray_cast(V((self.x0 + i * self.h, 0.4, z)), d, 0.6)[0]
                if hit is not None:
                    g[j, i] = hit.y
        self.g = g


def hairline_mh(lo):
    """Hairline latitude (deg, true elevation from the head centre) by longitude: forehead, temples, sideburns, over
    the ears, nape; per person: forehead height, temple recession, widow's peak; ear clearance for short cuts."""
    hm = CUR["hm"]
    F = CUR["F"]
    lo = np.asarray(lo, float)
    a = np.abs((lo - 90.0 + 180.0) % 360.0 - 180.0)          # 0 front .. 180 back
    T = hm.hl_table                                              # (angle, z) knots
    z = np.interp(a, T[:, 0], T[:, 1])
    z = z + F["recede"] * 0.0016 * (ga(a - 32, 13) + 0.35 * ga(a, 30))
    z = z - F["peak"] * 0.003 * ga(a, 6)
    st = bp.STYLE.get("style")
    if st in ("bowl", "vulcan_f", "romulan_f", "bob", "pixie", "undercut", "fade", "crop", "slick", "slick_bun",
              "coils") or CUR.get("pointed"):
        ez = hm.ear_top_z
        z = np.maximum(z, ez * sm(118, 100, a) * sm(62, 80, a) + -1.0 * (1 - sm(118, 100, a) * sm(62, 80, a)))
    # z -> elevation along this longitude on the head surface
    out = np.empty_like(lo)
    las = hm.hl_las
    for k in range(len(lo)):
        j = int(round(lo[k] % 360)) % 360
        zz = hm.hl_z[:, j]
        out[k] = np.interp(z[k], zz, las)
    return out


def ear_push_mh(p, gap=0.004):
    hm = CUR["hm"]
    for s in "lr":
        c, ext = hm.ears_local[s]
        rad_ = np.array([max(ext[0], 0.008) + 0.004, ext[1] + 0.006, ext[2] + 0.006]) + gap
        tuck = CUR.get("species") == "bajoran" and c[0] > 0       # hair behind the ear with the chain
        if tuck:
            rad_ = rad_ + np.array([0.030, 0.016, 0.026])
        q = (p - c) / rad_
        d = np.linalg.norm(q, axis=-1)
        inside = d < 1.0
        if inside.any():
            if tuck:
                # out along (out, back): solve |q + s*dq| = 1
                dq = np.array([0.15, -1.0, 0.1]) / rad_
                qi = q[inside]
                a_ = dq @ dq
                b_ = 2 * qi @ dq
                c_ = np.sum(qi * qi, 1) - 1
                s_ = (-b_ + np.sqrt(np.maximum(b_ * b_ - 4 * a_ * c_, 0))) / (2 * a_)
                p[inside] = c + (qi + s_[:, None] * dq[None]) * rad_
            else:
                p[inside] = c + q[inside] / d[inside, None] * rad_
    return p


def face_clear_mh(p, F, fringe=False):
    """Keep hair off the face: points in front of the face below the hairline go out to the side."""
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    top = F["bz"] + (0.012 if fringe else 0.004)
    zone = (y > 0.03) & (z < top) & (z > F["cz"] - 0.03)
    half = CUR["hm"].face_half
    inside = zone & (np.abs(x) < half)
    if inside.any():
        sx = np.where(x[inside] >= 0, 1.0, -1.0)
        p[inside, 0] = sx * half
    return p


class BodyClearMH:
    """Distance from the body axis to the (rest) body by height and direction, for long hair (head-local in/out)."""

    def __init__(self, gap):
        hm = CUR["hm"]
        bvh = hm.bvh_body
        self.zs = np.arange(-0.62, 0.02, 0.01)            # head-local z
        self.ths = np.arange(0, 360, 6.0)
        self.cy = np.full(len(self.zs), -0.02)
        self.r = np.zeros((len(self.zs), len(self.ths)))
        for i, z in enumerate(self.zs):
            for j, th in enumerate(self.ths):
                d = V((math.cos(rad(th)), math.sin(rad(th)), 0))
                o = V((0, self.cy[i], z)) + d * 0.6
                hit = bvh.ray_cast(o, -d, 0.6)[0]
                self.r[i, j] = (hit - V((0, self.cy[i], z))).length if hit is not None else 0.05
        self.r += gap + hm.cloth_gap

    def push(self, p):
        sel = p[:, 2] < self.zs[-1]
        if not sel.any():
            return p
        q = p[sel]
        i = np.clip(((q[:, 2] - self.zs[0]) / 0.01).astype(int), 0, len(self.zs) - 1)
        cy = self.cy[i]
        th = np.degrees(np.arctan2(q[:, 1] - cy, q[:, 0])) % 360
        j = (np.round(th / 6.0).astype(int)) % len(self.ths)
        rmin = self.r[i, j]
        rr = np.hypot(q[:, 0], q[:, 1] - cy)
        k = np.where(rr < rmin, rmin / np.maximum(rr, 1e-6), 1.0)
        q[:, 0] *= k
        q[:, 1] = cy + (q[:, 1] - cy) * k
        p[sel] = q
        return p


def lid_apertures(hm):
    """The lid margins (palpebral fissure) of each eye on the MakeHuman body: in angular bins round the eye's forward
    axis, the body vertex in front of the eyeball's equator that is closest to the axis. Returns
    {side: (points (n, 3) ordered from the +x corner over the top and back under, angles)}."""
    bi = hm.body_idx
    co = hm.co[bi]
    out = {}
    for s in ("l", "r"):
        c = hm.eye_c[s]
        r = hm.eye_r[s]
        q = co - c
        sel = (q[:, 1] > 0.35 * r) & (np.linalg.norm(q, axis=1) < 2.2 * r)
        Q = q[sel]
        phi = np.degrees(np.arctan2(Q[:, 2], Q[:, 0])) % 360
        rho = np.hypot(Q[:, 0], Q[:, 2])
        pts, angs, rr = [], [], []
        for a0 in np.arange(0, 360, 10.0):
            m = np.abs((phi - a0 + 180) % 360 - 180) < 8.0
            if m.sum() < 1:
                continue
            k = np.argmin(np.where(m, rho, 9))
            pts.append(Q[k])
            angs.append(a0)
            rr.append(rho[k])
        pts, angs, rr = np.array(pts), np.array(angs), np.array(rr)
        med = np.array([np.median(np.take(rr, [i - 1, i, (i + 1) % len(rr)])) for i in range(len(rr))])
        ok = rr < med * 1.12
        pts, angs = pts[ok], angs[ok]
        # resample on a smooth (angle -> radius, depth) curve
        a2 = np.arange(0, 360, 4.0)
        ext = lambda v: np.concatenate([v, v, v])  # noqa: E731
        ae = np.concatenate([angs - 360, angs, angs + 360])
        rho2 = np.interp(a2, ae, ext(np.hypot(pts[:, 0], pts[:, 2])))
        y2 = np.interp(a2, ae, ext(pts[:, 1]))
        P2 = np.stack([rho2 * np.cos(np.radians(a2)), y2, rho2 * np.sin(np.radians(a2))], 1) + c
        out[s] = (P2, a2)
    return out


def lid_lines(hm, Dh=None):
    """Lid margins for build_portraits' lash generator: (eye centre, upper, lower) with (point, outward normal,
    lo 20..162 from the +x corner) per point, rest world coordinates."""
    out = []
    ap = hm.apertures
    for s in ("l", "r"):
        c = hm.eye_c[s]
        P, A = ap[s]
        # corners: the extreme x points of the aperture
        ia, ib = int(np.argmax(P[:, 0])), int(np.argmin(P[:, 0]))
        lines = []
        for upper in (True, False):
            m = (P[:, 2] >= c[2] - 0.0005) if upper else (P[:, 2] < c[2] + 0.0005)
            Q = P[m]
            Q = np.concatenate([P[[ia]], Q, P[[ib]]])
            Q = Q[np.argsort(-Q[:, 0])]
            seg = np.linalg.norm(np.diff(Q, axis=0), axis=1)
            sc = np.concatenate([[0], np.cumsum(seg)])
            npt = 70 if upper else 34
            ts = np.linspace(0, sc[-1], npt)
            Qs = np.stack([np.interp(ts, sc, Q[:, k]) for k in range(3)], 1)
            pts = []
            for k, q in enumerate(Qs):
                nv = q - c
                nv = nv / np.linalg.norm(nv)
                nv = nv * 0.3 + np.array([0, 1.0, -0.12 if upper else 0.1]) * 0.7
                nv = nv / np.linalg.norm(nv)
                pts.append((V((q + nv * 0.0004).tolist()), V(nv.tolist()), 20 + 142 * k / (npt - 1)))
            lines.append(pts)
        out.append((V(c.tolist()), lines[0], lines[1]))
    return out


def hair_setup(hm, c):
    """Ray-cast fields, hairline table and the face numbers build_portraits' strand code reads (CUR['F'])."""
    bvh = body_bvh_rest(hm)
    hm.bvh_body = bvh
    hm.cloth_gap = 0.012
    ear_v = hm.regions["ear"] > 0.6
    fl = D.faces["body"]
    noear = fl[~ear_v[fl].all(1)]
    bvh_ne = BVHTree.FromPolygons([tuple(p) for p in (hm.co - hm.head_c)], noear.tolist(), all_triangles=False)
    hm.field = MHField(bvh_ne)
    hm.grid = MHGrid(bvh)
    hc = hm.head_c
    ez = hm.ez - hc[2]
    ear = {s: (hm.ears[s][0] - hc, hm.ears[s][1]) for s in "lr"}
    hm.ears_local = ear
    ezc = ear["l"][0][2]
    hm.ear_top_z = ezc + ear["l"][1][2] + 0.006
    seed = hm.P["seed"]
    h = lambda k: H(seed, 900 + k)  # noqa: E731
    fem = hm.fem
    yrs = hm.P["info"]["years"]
    fore = 0.062 + (h(1) - 0.5) * 0.012 - 0.004 * fem
    zf = ez + fore
    hm.hl_table = np.array([(0, zf), (22, zf - 0.002), (40, zf - 0.010 - 0.004 * (1 - fem)), (58, ez + 0.040),
                            (72, ez + 0.020), (80, ezc + 0.004), (86, hm.ear_top_z + 0.002),
                            (100, hm.ear_top_z + 0.004), (114, ezc - 0.004), (130, ezc - 0.030),
                            (160, ezc - 0.048), (180, ezc - 0.052)])
    las = np.arange(-50, 89.5, 0.5)
    hm.hl_las = las
    lo = np.arange(0, 360, 1.0)
    LA, LO = np.meshgrid(las, lo, indexing="ij")
    hm.hl_z = hm.field.xyz(LA, LO)[..., 2]
    hm.hl_z = np.maximum.accumulate(hm.hl_z, axis=0)        # monotone in latitude for interp
    F = CUR["F"]
    F.update(dict(fem=1.0 if fem else 0.0, bz=hm.brow_z - hc[2], cz=hm.chin_z - hc[2], mz=hm.mouth[2] - hc[2],
                  ez=ez, eye_x=abs(hm.eye_c["l"][0]), ear=1.0, raise_=0.0, a_brow=(h(2) - 0.5) * 0.0016,
                  recede=(0.0 if fem else 1.0) * max(0.0, hm.age01 - 0.25 + (h(3) - 0.5) * 0.6) * 7.0,
                  peak=2.0 * h(4) if h(5) < 0.3 else 0.0, fore_h=0.0, years=yrs))
    F["raise"] = 0.0
    CUR["field"] = hm.field
    CUR["grid"] = hm.grid
    bp.HEAD_C = V(hc.tolist())
    bp.HC_NP = hc.copy()
    bp.head_xyz = hm.field.xyz
    bp.hairline = hairline_mh
    bp.ear_push = ear_push_mh
    bp.face_clear = lambda p, F, fringe=False: p      # strands are trimmed at the face afterwards (strands())
    q0 = hm.field.xyz(np.array([5.0, -15.0]), np.array([90 - 38.0, 90 - 38.0]))
    hm.face_half = float(max(0.062, q0[:, 0].max() + 0.004))
    bp.BodyClear = BodyClearMH
    bp.beard_zone = lambda Q: beard_zone_mh(hm, Q)
    bp.STYLE.clear()
    if c.get("style"):
        bp.STYLE.update(bp.STYLES[c["style"]])
        bp.STYLE["style"] = c["style"]
    # scalp tint under the hair (true elevation / azimuth of every vertex)
    q = hm.co - hc
    r = np.linalg.norm(q, axis=1)
    LAv = np.degrees(np.arcsin(np.clip(q[:, 2] / np.maximum(r, 1e-9), -1, 1)))
    LOv = np.degrees(np.arctan2(q[:, 1], q[:, 0])) % 360
    if c.get("style") and not NO_HAIR:
        hlv = hairline_mh(LOv)
        cov = sm(hlv - 1.0, hlv + 4.0, LAv) * (r < 0.16)
        if bp.STYLE.get("shaved_sides"):
            cov = cov * (0.35 + 0.65 * sm(bp.STYLE["shaved_sides"] - 4, bp.STYLE["shaved_sides"] + 2, LAv))
        hm.scalp = cov


def strands(hm, c, objs, body_ob):
    """Scalp hair, brows, lashes, beard (build_portraits' generators), posed with the head."""
    seed = hm.P["seed"]
    Dh = hm.Dh
    bvh = None
    if c.get("style") in ("long", "curly_long", "wild", "ponytail", "bob", "vulcan_f", "romulan_f"):
        dg = bpy.context.evaluated_depsgraph_get()
        vs, fs = [], []
        for o in objs:
            if o.type != "MESH" or o.name.startswith(("Eye", "Cornea", "helper")):
                continue
            me = o.evaluated_get(dg).to_mesh()
            M = o.matrix_world
            base = len(vs)
            vs += [tuple(M @ v.co) for v in me.vertices]
            fs += [tuple(base + i for i in pl.vertices) for pl in me.polygons]
            o.evaluated_get(dg).to_mesh_clear()
        bvh = BVHTree.FromPolygons(vs, fs)
    if c.get("style") and "hair" not in SKIP:
        # build_portraits' style anchors (ponytail tie, bun, whorl) sit on its own head: move them onto this one
        st0 = bp.STYLES[c["style"]]
        st1 = dict(st0)
        F = hm.field

        def onto(p, out):
            p = np.array(p, float)
            d = p / np.linalg.norm(p)
            return tuple(d * (F.R(d[None])[0][0] + out))
        if "tie" in st0:
            st1["tie"] = onto(st0["tie"], 0.007)
            if "bun" in st0:
                st1["bun"] = tuple(np.array(st1["tie"]) + (np.array(st0["bun"]) - np.array(st0["tie"])))
        if "whorl" in st0:
            st1["whorl"] = onto(st0["whorl"], -0.012)
        bp.STYLES[c["style"]] = st1
        try:
            C, w, st = bp.scalp_strands(c, seed)
        finally:
            bp.STYLES[c["style"]] = st0
        # strands end where they would enter the face (a fringe stops above the brows, side hair beside the cheeks)
        Fp = CUR["F"]
        top = Fp["bz"] + (0.010 if st.get("cut") in ("bowl", "vulcan_f", "romulan_f") else 0.006)
        xx, yy, zz = C[..., 0], C[..., 1], C[..., 2]
        bad = (yy > 0.035) & (zz < top) & (zz > Fp["cz"] - 0.03) & (np.abs(xx) < hm.face_half - 0.004)
        bad[:, 0] = False
        if bad.any():
            first = np.where(bad.any(1), np.argmax(bad, 1), C.shape[1])
            seg = np.linalg.norm(np.diff(C, axis=1), axis=-1)
            cum = np.concatenate([np.zeros((len(C), 1)), np.cumsum(seg, 1)], 1)
            tot = np.maximum(cum[:, -1], 1e-9)
            frac = np.where(first < C.shape[1], cum[np.arange(len(C)), np.maximum(first - 1, 0)] / tot, 1.0)
            C = bp.trim(C, np.maximum(frac, 0.02))
        hr = st.get("rad", 1.0) * 0.000085
        objs.append(bp.curves_object("Hair", C, hr, hr * 0.25, bp.CHAR_MATS["HairS"], Dh, bvh))
        if st.get("bun"):
            B = Dh @ (V(st["bun"]) + bp.HEAD_C)
            bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, location=B, segments=20, ring_count=10)
            o = bpy.context.active_object
            o.scale = (0.022, 0.016, 0.019)
            o.data.materials.append(bp.CHAR_MATS["hair_core"])
            objs.append(o)
    if "brow" not in SKIP:
        B_, kind = bp.brow_strands(c, seed)
        objs.append(bp.curves_object("Brow", B_, 0.00007, 0.00002, bp.CHAR_MATS["BrowS"], Dh))
    if "lash" not in SKIP:
        L_ = bp.lash_strands(lid_lines(hm), seed)
        # shorter than build_portraits' (its lids were further from the eye): upper x0.62, lower x0.42
        root = L_[:, :1]
        down = (L_[:, -1, 2] < L_[:, 0, 2])[:, None, None]
        L_ = root + (L_ - root) * np.where(down, 0.42, 0.62)
        objs.append(bp.curves_object("Lash", L_, 0.00006, 0.000015, bp.CHAR_MATS["LashS"], Dh))
    if c.get("beard") and "beard" not in SKIP:
        Bd, kind = bp.beard_strands(c, seed)
        r0 = 0.00007 if kind != "stubble" else 0.00005
        objs.append(bp.curves_object("Beard", Bd, r0, 0.00002, bp.CHAR_MATS["BeardS"], Dh))


# ================================================================ outfits on MakeHuman's tights helper
# Every garment starts from MakeHuman's "helper-tights" (a skin-tight suit shell that follows every target), split
# once (4x the faces) for cleaner panel edges. Vertex weights come from the default rig's weights of the parent
# vertices, so the clothes pose with the body. Colour zones (yoke, sleeve bands, belt, boots, seams) are crisp
# thresholds in the shader on smooth per-vertex fields; raised parts (collars, pauldrons, plates, padding) are shells
# offset from the suit.

def weight_matrix():
    bones = list(D.rig["weights"])
    W = np.zeros((len(D.base), len(bones)), np.float32)
    for j, b in enumerate(bones):
        a = np.array(D.rig["weights"][b], np.float64)
        W[a[:, 0].astype(np.int64), j] = a[:, 1]
    D.bones = bones
    D.W = W


def vertex_normals(co, faces):
    fn = np.cross(co[faces[:, 2]] - co[faces[:, 0]], co[faces[:, 3]] - co[faces[:, 1]])
    vn = np.zeros_like(co)
    for k in range(4):
        np.add.at(vn, faces[:, k], fn)
    return nrm(vn)


def split_quads(co, faces, W):
    """Linear 1->4 split of a quad mesh (positions and weight rows interpolated)."""
    n = len(co)
    e = np.concatenate([np.sort(np.stack([faces[:, k], faces[:, (k + 1) % 4]], 1), 1) for k in range(4)])
    eu, inv = np.unique(e, axis=0, return_inverse=True)
    inv = inv.reshape(4, -1).T
    ne = len(eu)
    eco = (co[eu[:, 0]] + co[eu[:, 1]]) / 2
    eW = (W[eu[:, 0]] + W[eu[:, 1]]) / 2
    fco = co[faces].mean(1)
    fW = W[faces].mean(1)
    co2 = np.concatenate([co, eco, fco])
    W2 = np.concatenate([W, eW, fW])
    E = n + inv
    Fc = n + ne + np.arange(len(faces))
    out = []
    for k in range(4):
        out.append(np.stack([faces[:, k], E[:, k], Fc, E[:, (k - 1) % 4]], 1))
    return co2, np.concatenate(out), W2


def skinned_object(name, co, faces, W, rig, mats, mat_index=None, subsurf=2, smooth=True):
    """Mesh object (rest coordinates) with vertex groups from weight rows W and an armature modifier."""
    me = bpy.data.meshes.new(name)
    me.from_pydata(np.asarray(co).tolist(), [], [list(map(int, f)) for f in faces])
    me.validate(verbose=False)
    for m in mats:
        me.materials.append(m)
    if mat_index is not None:
        me.polygons.foreach_set("material_index", np.asarray(mat_index, np.int32))
    if smooth:
        me.shade_smooth()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    Wr = np.round(W, 3)
    for j in np.nonzero(Wr.max(0) > 0)[0]:
        vg = ob.vertex_groups.new(name=D.bones[j])
        col = Wr[:, j]
        for wv in np.unique(col[col > 0]):
            vg.add(np.nonzero(col == wv)[0].tolist(), float(wv), "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    mod.use_deform_preserve_volume = True
    ob.parent = rig
    if subsurf:
        s = ob.modifiers.new("Subsurf", "SUBSURF")
        s.levels = 1
        s.render_levels = subsurf
    return ob


def nearest_rows(points, cand_idx, co):
    """Weight rows of the nearest candidate vertex (global indices) for each point."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(cand_idx))
    for k, i in enumerate(cand_idx):
        kd.insert(co[i], k)
    kd.balance()
    src = np.array([cand_idx[kd.find(p)[1]] for p in points])
    return D.W[src]


class Suit:
    """The rest-pose garment base for one person: split tights, normals, landmarks and colour fields."""

    def __init__(self, hm):
        f = D.faces["helper-tights"]
        used = np.unique(f.ravel())
        remap = np.full(len(D.base), -1, np.int64)
        remap[used] = np.arange(len(used))
        co, faces, W = hm.co[used], remap[f], D.W[used]
        # boundary loops of the tights: neck, wrists (ankles closed by the feet)
        e = np.concatenate([np.sort(np.stack([faces[:, k], faces[:, (k + 1) % 4]], 1), 1) for k in range(4)])
        eu, cnt = np.unique(e, axis=0, return_counts=True)
        bv = np.unique(eu[cnt == 1].ravel())
        co, faces, W = split_quads(co, faces, W)
        self.co, self.faces, self.W = co, faces, W
        self.n = vertex_normals(co, faces)
        self.hm = hm
        bco = co[bv]
        self.neck_ring = bco[bco[:, 2] > 1.2]
        self.neck_z = float(self.neck_ring[:, 2].mean())
        self.boundary = bco
        self.sh = {s: hm.joint(f"upperarm01.{s.upper()}____head") for s in "lr"}
        self.armpit_z = float(min(self.sh["l"][2], self.sh["r"][2]) - 0.035)
        self.waist_z = float(hm.joint("spine03____head")[2])
        self.chest_y = float(co[(np.abs(co[:, 0]) < 0.03) & (np.abs(co[:, 2] - self.armpit_z) < 0.02)][:, 1].max())

    def arm_t(self, p, with_dist=False):
        """Distance along each upper arm from the shoulder joint (negative on the torso), max of both sides; with
        with_dist also the distance from the arm's axis."""
        out = np.full(len(p), -1.0)
        dist = np.full(len(p), 9.0)
        for s in "lr":
            a = self.hm.joint(f"upperarm01.{s.upper()}____head")
            b = self.hm.joint(f"lowerarm01.{s.upper()}____head")
            ax = (b - a) / np.linalg.norm(b - a)
            t = (p - a) @ ax
            dd = np.linalg.norm((p - a) - t[:, None] * ax, axis=1)
            side = (p[:, 0] * np.sign(a[0])) > 0
            out = np.where(side, np.maximum(out, t), out)
            dist = np.where(side, dd, dist)
        return (out, dist) if with_dist else out


def suit_fields(S, kind):
    """Per-vertex fields for the suit shader (sign = inside): yoke, sleeve band, belt, boots, front seam."""
    p = S.co
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    t, da = S.arm_t(p, True)
    on_arm = sm(0.0, 0.05, t) * sm(0.085, 0.065, da)
    zl = S.neck_z - 0.085 - 0.03 * (np.abs(x) / 0.12) ** 2
    yk = (1 - on_arm) * (z - zl) + on_arm * (0.075 - t)
    band = 0.010 - np.abs(t - 0.165) + (on_arm < 0.5) * -1.0
    belt = 0.022 - np.abs(z - (S.waist_z - 0.03))
    boot = 0.30 - z
    seam = 0.0016 - np.abs(x) + (y < 0) * -1.0 + (z > zl + 0.002) * -1.0
    return np.stack([yk, band, belt, boot], 1), seam


def suit_material(name, base, yoke=None, trim=(0.012, 0.012, 0.014), boot=(0.022, 0.02, 0.018), belt=(0.10, 0.075, 0.045),
                  kind="cloth", rough=0.78, pattern=None):
    """Suit: base cloth + crisp zones from the 'zf' (yoke, band, belt, boot) and 'seam' attributes."""
    if pattern is not None:
        m = pattern
        nt = m.node_tree
        p = nt.nodes["Principled BSDF"]
        lk = p.inputs["Base Color"].links
        col = lk[0].from_socket if lk else base
    else:
        m, p = bp.principled(name, base, rough)
        nt = m.node_tree
        col = base
    za = bp.attr(nt, "zf")
    zf = list(bp.sep(nt, za.outputs["Color"])) + [za.outputs["Alpha"]]
    sem = bp.attr(nt, "seam").outputs["Fac"]

    def step(v, w=0.0006):
        return bp.math_node(nt, "MULTIPLY", bp.math_node(nt, "ADD", v, w), 0.5 / w, clamp_=True)
    if yoke is not None:
        yk = step(zf[0])
        col = bp.mix_rgb(nt, yk, col, yoke)
        edge = bp.math_node(nt, "SUBTRACT", 1.0, step(bp.math_node(nt, "SUBTRACT", bp.math_node(nt, "ABSOLUTE", zf[0]), 0.0022)))
        col = bp.mix_rgb(nt, edge, col, trim)
        col = bp.mix_rgb(nt, step(zf[1]), col, yoke)
    col = bp.mix_rgb(nt, step(zf[2]), col, belt)
    col = bp.mix_rgb(nt, step(zf[3]), col, boot)
    col = bp.mix_rgb(nt, step(sem, 0.0004), col, trim)
    nt.links.new(col, p.inputs["Base Color"])
    # cloth weave / leather grain
    if kind == "leather":
        bp.bump(m, p, 260, 0.25, 0.0004)
    else:
        bp.bump(m, p, 1500, 0.12, 0.0002)
    p.inputs["Sheen Weight"].default_value = 0.06 if kind == "cloth" else 0.0
    p.inputs["Sheen Roughness"].default_value = 0.4
    bt = step(zf[3])
    r = bp.math_node(nt, "SUBTRACT", rough, bp.math_node(nt, "MULTIPLY", bt, 0.35))
    nt.links.new(r, p.inputs["Roughness"])
    return m


def ring_band(hm, S, h0, h1, out0, out1, th0=0.0, th1=360.0, nth=96, rows=6, thick=0.003, rise_back=0.008, flare=0.0):
    """A collar: a band round the neck from the suit's neck line (height h0 above it) to h1, following the neck surface
    (out0 / out1 outside it), closed solid. Returns (co, faces) rest coordinates."""
    bvh = hm.bvh_body_world
    ring = S.neck_ring
    cy = float(ring[:, 1].mean())
    ang = np.degrees(np.arctan2(ring[:, 1] - cy, ring[:, 0])) % 360
    order = np.argsort(ang)
    ang_s, z_s = ang[order], ring[order, 2]
    ang_s = np.concatenate([ang_s - 360, ang_s, ang_s + 360])
    z_s = np.concatenate([z_s, z_s, z_s])
    full = abs((th1 - th0) - 360) < 1e-6
    ths = np.linspace(th0, th1, nth, endpoint=not full)
    # neck radius by row and angle (ray cast from the neck axis, only near the suit line), smoothed round the ring
    nr = rows + 1
    Rr = np.zeros((nr, len(ths)))
    Zz = np.zeros((nr, len(ths)))
    for r_ in range(nr):
        u = r_ / rows
        for j, th in enumerate(ths):
            zb = np.interp(th % 360, ang_s, z_s)
            back = 0.5 + 0.5 * math.sin(rad(th)) * -1.0      # 1 at the back (-y)
            zz = zb + h0 + (h1 - h0 + rise_back * back) * u
            d = V((math.cos(rad(th)), math.sin(rad(th)), 0))
            zcast = min(zz, zb + 0.016)
            hit = bvh.ray_cast(V((0, cy, zcast)), d, 0.2)[0]
            Rr[r_, j] = (hit - V((0, cy, zcast))).length if hit is not None else 0.06
            Zz[r_, j] = zz
    k = 7
    for _ in range(3):
        if full:
            Rr = sum(np.roll(Rr, i, 1) for i in range(-k // 2 + 1, k // 2 + 1)) / k
        else:
            Rp = np.pad(Rr, ((0, 0), (k // 2, k // 2)), mode="edge")
            Rr = np.stack([Rp[:, i:i + len(ths)] for i in range(k)]).mean(0)
    layers = []
    for shell in (0, 1):
        grid = []
        for r_ in range(nr):
            u = r_ / rows
            row = []
            for j, th in enumerate(ths):
                d = V((math.cos(rad(th)), math.sin(rad(th)), 0))
                off = out0 + (out1 - out0) * u + flare * u * u + shell * thick
                row.append(tuple(V((0, cy, Zz[r_, j])) + d * (Rr[r_, j] + off)))
            grid.append(row)
        layers.append(grid)
    co, faces = [], []
    ncol = len(ths)

    def vid(l, r_, j):
        return l * (rows + 1) * ncol + r_ * ncol + j
    for l in range(2):
        for r_ in range(rows + 1):
            co.extend(layers[l][r_])
    jr = range(ncol) if full else range(ncol - 1)
    for l, flip in ((0, True), (1, False)):
        for r_ in range(rows):
            for j in jr:
                k = (j + 1) % ncol
                f = (vid(l, r_, j), vid(l, r_, k), vid(l, r_ + 1, k), vid(l, r_ + 1, j))
                faces.append(f[::-1] if flip else f)
    for j in jr:                                   # top and bottom rims
        k = (j + 1) % ncol
        faces.append((vid(0, rows, j), vid(0, rows, k), vid(1, rows, k), vid(1, rows, j)))
        faces.append((vid(1, 0, j), vid(1, 0, k), vid(0, 0, k), vid(0, 0, j)))
    if not full:
        for j in (0, ncol - 1):
            for r_ in range(rows):
                faces.append((vid(0, r_, j), vid(0, r_ + 1, j), vid(1, r_ + 1, j), vid(1, r_, j)))
    return np.array(co), np.array(faces)


_CUT = {}


def cut_material(base):
    """A copy of base that is transparent where the 'pm' attribute is below zero: shells get crisp smooth outlines
    from the mask field instead of the suit's face edges."""
    key = "cut:" + base.name
    if key in bp.MATS:
        return bp.MATS[key]
    m = base.copy()
    m.name = base.name + "_cut"
    nt = m.node_tree
    out = nt.nodes["Material Output"]
    src = out.inputs["Surface"].links[0].from_socket
    tr = bp.node(nt, "ShaderNodeBsdfTransparent")
    mix = bp.node(nt, "ShaderNodeMixShader")
    fac = bp.math_node(nt, "GREATER_THAN", bp.attr(nt, "pm").outputs["Fac"], 0.0)
    nt.links.new(fac, mix.inputs["Fac"])
    nt.links.new(tr.outputs["BSDF"], mix.inputs[1])
    nt.links.new(src, mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], out.inputs["Surface"])
    bp.MATS[key] = m          # kept between characters (build_portraits.remove spares MATS)
    return m


def shell(S, mask, offset, name, mat, rig, thick=0.004, subsurf=2, feather=0.0):
    """A raised part: the suit faces where mask > 0 (all four corners), pushed out by offset (per vertex); with
    feather (mask units) the offset eases to 1.5 mm at the edge so the part blends into the suit."""
    sel = (mask[S.faces] > -0.012).any(1) & (mask[S.faces] > -0.03).all(1)
    if not (mask[S.faces] > 0).any():
        return None
    f = S.faces[sel]
    used = np.unique(f.ravel())
    remap = np.full(len(S.co), -1, np.int64)
    remap[used] = np.arange(len(used))
    off = offset if np.ndim(offset) else np.full(len(S.co), offset)
    if feather > 0:
        off = 0.0015 + (off - 0.0015) * sm(0.0, feather, mask)
    co = S.co[used] + S.n[used] * off[used][:, None]
    ob = skinned_object(name, co, remap[f], S.W[used], rig, [cut_material(mat)], subsurf=subsurf)
    a = ob.data.attributes.new("pm", "FLOAT", "POINT")
    a.data.foreach_set("value", mask[used].astype(np.float32))
    bp.set_attr(ob.data, "rest", co - S.hm.head_c, "FLOAT_VECTOR")
    so = ob.modifiers.new("Solid", "SOLIDIFY")
    so.thickness = thick
    so.offset = 1.0
    bpy.context.view_layer.objects.active = ob
    # solidify before the armature so the thickness poses too
    while ob.modifiers[0].name != "Solid":
        bpy.ops.object.modifier_move_up(modifier="Solid")
    return ob


def ribbed_material():
    """Cardassian chest plate: dull bronze ribs (rest z bands) with darker grooves and a bump."""
    if "car_ribbed" in bp.MATS:
        return bp.MATS["car_ribbed"]
    m, p = bp.principled("car_ribbed", srgb_lin((0.30, 0.24, 0.17)), 0.42, 0.45)
    nt = m.node_tree
    rest = bp.sep(nt, bp.attr(nt, "rest").outputs["Vector"])
    w = bp.math_node(nt, "SINE", bp.math_node(nt, "MULTIPLY", rest[2], 2 * pi / 0.022))
    band = bp.math_node(nt, "MULTIPLY", bp.math_node(nt, "ADD", w, 0.75), 3.0, clamp_=True)
    col = bp.mix_rgb(nt, band, srgb_lin((0.08, 0.07, 0.06)), srgb_lin((0.36, 0.28, 0.19)))
    nt.links.new(col, p.inputs["Base Color"])
    bm = bp.node(nt, "ShaderNodeBump")
    bm.inputs["Strength"].default_value = 0.8
    bm.inputs["Distance"].default_value = 0.003
    nt.links.new(band, bm.inputs["Height"])
    nt.links.new(bm.outputs["Normal"], p.inputs["Normal"])
    bp.MATS["car_ribbed"] = m
    return m


def badge(hm, S, rig, objs):
    """Gold hexagon badge with a raised chevron on the left chest (the colony-service badge, original)."""
    p = S.co
    want = np.array([-0.075, S.chest_y, S.neck_z - 0.085 - 0.03 * (0.075 / 0.12) ** 2 - 0.028])
    front = (p[:, 1] > S.chest_y - 0.05)
    k = np.argmin(np.where(front, np.linalg.norm(p - want, axis=1), 9))
    c = p[k] + S.n[k] * 0.0015
    n = V(S.n[k].tolist())
    e1 = V((0, 0, 1)).cross(n).normalized()
    e2 = n.cross(e1)
    co, faces = [], []
    R, T = 0.0125, 0.0022
    for layer, (r, dz) in enumerate(((R, 0.0), (R, T), (R * 0.84, T + 0.0006))):
        for j in range(6):
            a = rad(90 + 60 * j)
            q = V(c.tolist()) + (e1 * math.cos(a) + e2 * math.sin(a)) * r + n * dz
            co.append(tuple(q))
    for l0 in (0, 1):
        for j in range(6):
            k2 = (j + 1) % 6
            faces.append((l0 * 6 + j, l0 * 6 + k2, (l0 + 1) * 6 + k2, (l0 + 1) * 6 + j))
    faces.append(tuple(range(17, 11, -1)))
    faces.append(tuple(range(0, 6))[::-1])
    co = np.array(co)
    me = bpy.data.meshes.new("Badge")
    me.from_pydata(co.tolist(), [], faces)
    me.materials.append(bp.material("gold"))
    ob = bpy.data.objects.new("Badge", me)
    bpy.context.scene.collection.objects.link(ob)
    # chevron: two bars meeting at the bottom, raised
    cv, cf = [], []
    for sx in (-1, 1):
        a0 = V(c.tolist()) + e2 * (-0.0055) + n * (T + 0.0006)
        a1 = V(c.tolist()) + e1 * (sx * 0.0075) + e2 * 0.0045 + n * (T + 0.0006)
        d = (a1 - a0).normalized()
        side = n.cross(d) * 0.0017
        b = len(cv)
        for q in (a0 - side, a1 - side, a1 + side, a0 + side):
            cv.append(tuple(q))
            cv.append(tuple(q + n * 0.0011))
        cf += [(b, b + 2, b + 4, b + 6), (b + 1, b + 7, b + 5, b + 3), (b, b + 1, b + 3, b + 2), (b + 2, b + 3, b + 5, b + 4),
               (b + 4, b + 5, b + 7, b + 6), (b + 6, b + 7, b + 1, b)]
    me2 = bpy.data.meshes.new("Chevron")
    me2.from_pydata(cv, [], cf)
    me2.materials.append(bp.material("gold"))
    ob2 = bpy.data.objects.new("Chevron", me2)
    bpy.context.scene.collection.objects.link(ob2)
    for o, nv in ((ob, len(co)), (ob2, len(cv))):
        for j in np.nonzero(S.W[k] > 0)[0]:
            vg = o.vertex_groups.new(name=D.bones[j])
            vg.add(list(range(nv)), float(S.W[k][j]), "REPLACE")
        mod = o.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        o.parent = rig
        objs.append(o)


def hide_covered_body(hm, S, keep_band=0.015):
    """Body faces under the suit (not within keep_band of a suit opening) are removed from the body mesh."""
    from mathutils.kdtree import KDTree
    bvh = BVHTree.FromPolygons(S.co.tolist(), S.faces.tolist(), all_triangles=False)
    kd = KDTree(len(S.boundary))
    for k, q in enumerate(S.boundary):
        kd.insert(q, k)
    kd.balance()
    bi = hm.body_idx
    cov = np.zeros(len(D.base), bool)
    for i in bi:
        p = V(hm.co[i].tolist())
        loc, nn, _, dist = bvh.find_nearest(p, 0.04)
        if loc is None:
            continue
        if (p - loc).dot(nn) < 0.003 and kd.find(p)[2] > keep_band and p.z < S.neck_z - 0.012:
            cov[i] = True
    return cov


def outfit(hm, c, rig, objs):
    """The suit and its parts for this person's faction."""
    f = c["faction"]
    sex = c["sex"]
    S = Suit(hm)
    hm.suit = S
    fields, seam = suit_fields(S, f)
    hm.bvh_body_world = BVHTree.FromPolygons(hm.co.tolist(), D.faces["body"].tolist(), all_triangles=False)
    if f == "federation":
        dept = bp.CHAR_MATS["Owner"].node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value[:3]
        mat = suit_material("Suit_" + c["id"], (0.028, 0.028, 0.031), yoke=tuple(dept))
        coll_mat = bp.material("trim")
    elif f == "klingon":
        mat = suit_material("Suit_" + c["id"], srgb_lin((0.12, 0.085, 0.07)), kind="leather", rough=0.55,
                            belt=srgb_lin((0.24, 0.23, 0.22)))
        coll_mat = bp.material("kl_leather")
    elif f == "romulan":
        mat = suit_material("Suit_" + c["id"], srgb_lin((0.30, 0.35, 0.31)), rough=0.8, belt=srgb_lin((0.10, 0.17, 0.13)))
        coll_mat = bp.material("rom_trim")
    elif f == "cardassian":
        mat = suit_material("Suit_" + c["id"], srgb_lin((0.17, 0.15, 0.13)), rough=0.7, belt=srgb_lin((0.36, 0.27, 0.17)))
        coll_mat = bp.material("car_armour")
    else:  # ferengi
        pat = bp.pattern_material("fer_jacket_" + c["id"], (0.62, 0.36, 0.12), (0.30, 0.12, 0.26))
        for nd in pat.node_tree.nodes:
            if nd.type == "TEX_VORONOI":
                nd.inputs["Scale"].default_value = 75
            elif nd.type == "TEX_WAVE":
                nd.inputs["Scale"].default_value = 150
        mat = suit_material("Suit_" + c["id"], None, pattern=pat, belt=srgb_lin((0.85, 0.62, 0.22)))
        coll_mat = bp.material("fer_trim")
    suit = skinned_object("Suit", S.co, S.faces, S.W, rig, [mat])
    zf = np.concatenate([fields, np.ones((len(S.co), 0))], 1)
    bp.set_attr(suit.data, "zf", zf, "FLOAT_COLOR")
    a = suit.data.attributes.new("seam", "FLOAT", "POINT")
    a.data.foreach_set("value", seam.astype(np.float32))
    bp.set_attr(suit.data, "rest", S.co - hm.head_c, "FLOAT_VECTOR")
    objs.append(suit)
    body_idx = np.unique(D.faces["body"].ravel())

    def collar(h0, h1, out0, out1, mat_, name="Collar", **kw):
        co, fc = ring_band(hm, S, h0, h1, out0, out1, **kw)
        W = nearest_rows(co, body_idx, hm.co)
        o = skinned_object(name, co, fc, W, rig, [mat_], subsurf=2)
        objs.append(o)
        return o
    x, y, z = S.co[:, 0], S.co[:, 1], S.co[:, 2]
    t = S.arm_t(S.co)
    if f == "federation":
        collar(-0.006, 0.026, 0.004, 0.0035, coll_mat, thick=0.0035)
        badge(hm, S, rig, objs)
    elif f == "klingon":
        collar(-0.008, 0.026, 0.006, 0.012, coll_mat, thick=0.004, flare=0.004)
        collar(0.024, 0.030, 0.012, 0.016, bp.material("kl_red"), name="CollarTrim", rows=2, thick=0.003)
        for k in range(3):     # layered pauldrons over each shoulder, the top one first
            m_ = (t > -0.06 + 0.035 * k) & (t < 0.07 + 0.04 * k) & (z > S.armpit_z - 0.01 - 0.03 * k) & (np.abs(x) > 0.08)
            off = 0.016 + 0.006 * (2 - k) + 0.006 * sm(0.0, 0.06, t)
            o = shell(S, m_.astype(float) - 0.5, off, f"Pauldron{k}", bp.material("kl_metal"), rig, 0.004)
            if o:
                objs.append(o)
        for k in range(4):     # chest plates
            z0 = S.armpit_z - 0.21 + 0.045 * k
            m_ = (z > z0) & (z < z0 + 0.04) & (y > S.chest_y - 0.07) & (np.abs(x) < 0.12)
            o = shell(S, m_.astype(float) - 0.5, 0.008, f"Plate{k}", bp.material("kl_metal" if k % 2 == 0 else "kl_leather"),
                      rig, 0.004)
            if o:
                objs.append(o)
    elif f == "romulan":
        if sex == "f":
            collar(-0.006, 0.030, 0.004, 0.008, coll_mat, thick=0.004, flare=0.004)
            m_ = np.minimum(np.minimum(z - (S.armpit_z + 0.005), np.abs(x) - 0.075), 0.085 - t)
            off = 0.004 + 0.010 * sm(0.08, 0.15, np.abs(x))
            o = shell(S, m_, off, "Pads", bp.material("rom_pad"), rig, 0.003, feather=0.02)
        else:
            collar(-0.006, 0.016, 0.005, 0.005, coll_mat, thick=0.0035)
            m_ = np.minimum(z - (S.armpit_z - 0.05), 0.11 - t)
            off = 0.004 + 0.024 * sm(0.04, 0.16, np.abs(x))
            o = shell(S, m_, off, "Pads", bp.material("rom_pad"), rig, 0.004, feather=0.035)
        if o:
            objs.append(o)
    elif f == "cardassian":
        collar(-0.008, 0.008, 0.005, 0.008, coll_mat, thick=0.004)
        # shoulder plates over the outer shoulders (the neck and its cords stay free), rising toward the neck
        m_ = np.minimum(np.minimum(z - (S.armpit_z - 0.03), np.abs(x) - 0.095), 0.11 - t)
        off = 0.010 + 0.016 * sm(S.armpit_z, S.neck_z, z)
        o = shell(S, m_, off, "Shoulders", bp.material("car_armour"), rig, 0.005)
        if o:
            objs.append(o)
        # ribbed chest panel: a V-topped plate with horizontal ribs
        vtop = S.neck_z - 0.03 - 0.55 * np.abs(x)
        m_ = np.minimum(np.minimum(vtop - z, 0.085 - np.abs(x)), z - (S.armpit_z - 0.16))
        m_ = np.where(y > S.chest_y - 0.09, m_, -1)
        o = shell(S, m_, 0.007, "Ribs", ribbed_material(), rig, 0.004)
        if o:
            objs.append(o)
    else:  # ferengi
        collar(-0.006, 0.010, 0.005, 0.006, bp.material("fer_shirt"), thick=0.003)
        collar(0.004, 0.034, 0.012, 0.036, coll_mat, name="Flare", th0=118, th1=422, thick=0.003, flare=0.016, rows=8)
        m_ = (np.abs(x) < 0.035) & (y > 0) & (z > S.waist_z - 0.1) & (z < S.neck_z)
        o = shell(S, m_.astype(float) - 0.5, 0.003, "ShirtFront", bp.material("fer_shirt"), rig, 0.002)
        if o:
            objs.append(o)
    if hm.full:
        # boots: the suit's feet and shins a little thicker, with a sole
        o = shell(S, 0.30 - z, 0.0035, "Boots", bp.material("boot"), rig, 0.002, feather=0.01)
        if o:
            objs.append(o)
        o = shell(S, 0.022 - z, 0.007, "Soles", bp.material("sole"), rig, 0.004, feather=0.004)
        if o:
            objs.append(o)
    cov = hide_covered_body(hm, S)
    return cov


# ================================================================ species features on the MakeHuman head
# Base-mesh changes (ears) happen on the MakeHuman coordinates before the rig is built; ridges, spoons, cords and
# snouts are sculpted as displacements on a twice-subdivided copy of the body (fine enough for 2-3 mm ridges);
# antennae, spots, earrings and teeth are separate skinned meshes.

def species_base(hm, c):
    """Ear shapes on the base mesh: Vulcan / Romulan points, Ferengi shells (scaled up from the root, pushed out)."""
    sp = c["species"]
    co = hm.co
    if sp not in ("vulcan", "romulan", "ferengi"):
        return
    for s, nm in (("l", "ears/l-ear-trans-up"), ("r", "ears/r-ear-trans-up")):
        i, d = D.target(nm)
        w = np.zeros(len(co))
        w[i] = np.linalg.norm(d, axis=1)
        w /= w.max()
        ec, ext = hm.ears[s]
        sx = np.sign(ec[0])
        root = ec + np.array([-sx * ext[0] * 1.2, 0.0, 0.0])
        if sp == "ferengi":
            k = 1.0 + 1.0 * sm(0.15, 0.9, w)
            q = co - root
            q[:, 1] *= 1.0 + 0.25 * sm(0.15, 0.9, w)            # rounder: deeper front-back
            q[:, 2] *= 1.0 - 0.08 * sm(0.15, 0.9, w)
            new = root + q * k[:, None]
            new[:, 0] += sx * 0.016 * sm(0.3, 1.0, w)           # stand out from the head
            new[:, 1] -= 0.006 * sm(0.3, 1.0, w)
            new[:, 2] += 0.004 * sm(0.3, 1.0, w)
            co[:] = np.where((w > 0)[:, None], new, co)
        else:
            top = ec[2] + ext[2]
            h = sm(ec[2] + 0.2 * ext[2], top + 0.002, co[:, 2]) * sm(0.4, 0.95, w)
            co[:, 2] += 0.010 * h ** 1.6
            co[:, 1] -= 0.006 * h ** 1.6
            co[:, 0] += sx * 0.002 * h
    # refresh the ear landmarks
    for s, nm in (("l", "ears/l-ear-trans-up"), ("r", "ears/r-ear-trans-up")):
        i, d = D.target(nm)
        mag = np.linalg.norm(d, axis=1)
        pts = co[i[mag > 0.6 * mag.max()]]
        lo_, hi_ = np.percentile(pts, 3, 0), np.percentile(pts, 97, 0)
        hm.ears[s] = ((lo_ + hi_) / 2, (hi_ - lo_) / 2)


def seg_dist(p, a, b):
    """Distance of points p (n, 3) to the segment a-b, and the parameter along it."""
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1), t


def polyline_dist(p, pts):
    d = np.full(len(p), 9.0)
    tt = np.zeros(len(p))
    n = len(pts) - 1
    for k in range(n):
        dk, tk = seg_dist(p, pts[k], pts[k + 1])
        better = dk < d
        d = np.where(better, dk, d)
        tt = np.where(better, (k + tk) / n, tt)
    return d, tt


def species_sculpt(hm, c, q, nrm_):
    """Displacement (metres, along the normal) and a 'ridge' colour mask for head-local points q (rest)."""
    sp = c["species"]
    x, y, z = q[:, 0], q[:, 1], q[:, 2]
    ax = np.abs(x)
    hc = hm.head_c
    bz = hm.brow_z - hc[2]
    ez = hm.ez - hc[2]
    ex = abs(hm.eye_c["l"][0])
    front = sm(0.0, 0.04, y)
    disp = np.zeros(len(q))
    ridge = np.zeros(len(q))
    vec = np.zeros_like(q)
    top = hm.field.xyz(np.array([88.0]), np.array([90.0]))[0][2]
    if sp == "klingon":
        fore = sm(bz - 0.004, bz + 0.008, z) * front
        crest = 0.0042 * ga(x, 0.0055) * sm(bz + 0.0, bz + 0.012, z) * sm(top + 0.01, top - 0.03, z)
        ch = 0.0
        for k in range(6):
            zk = bz + 0.010 + 0.0135 * k
            d = z - (zk + 0.42 * ax - 2.2 * ax ** 2)
            ch = ch + ga(d, 0.0024) * (0.0034 - 0.0003 * k) * ga(x, 0.042) * sm(0.002, 0.008, ax)
        brow = 0.0045 * ga(z - (bz - 0.002), 0.0055) * ga(x, 0.045) * front
        disp = fore * (crest + ch) + brow
        ridge = np.clip(disp / 0.004, 0, 1)
    elif sp == "romulan":
        a = np.array([0.0, 0.0, bz - 0.003])
        for sx in (-1, 1):
            b = a + np.array([sx * 0.026, 0.0, 0.026])
            d, t = seg_dist(np.stack([x, np.zeros_like(x), z], 1), a, b)
            disp = disp + 0.0021 * ga(d, 0.0032) * sm(1.0, 0.75, t) * front
        disp = disp + 0.0012 * ga(z - (bz - 0.002), 0.004) * ga(x, 0.04) * front
        ridge = np.clip(disp / 0.002, 0, 1) * 0.5
    elif sp == "tellarite":
        # snout: the lower nose pushed forward and up and widened, nostrils turned to the front; heavy brow
        nt = hm.nose_tip - hc
        dn = np.linalg.norm((q - nt) * np.array([0.8, 1.0, 1.0]), axis=1)
        nb = nt + np.array([0, -0.012, -0.012])                     # nose base (nostrils)
        db = np.linalg.norm((q - nb) * np.array([0.7, 1.0, 1.0]), axis=1)
        w = ga(dn, 0.016)
        wb = ga(db, 0.011) * sm(-0.03, 0.0, q[:, 1] - nb[1])
        vec[:, 1] = 0.015 * w + 0.017 * wb
        vec[:, 2] = 0.005 * w - 0.001 * wb
        vec[:, 0] = x * (0.45 * ga(dn, 0.018) + 0.3 * wb)
        disp = 0.0065 * ga(z - (bz - 0.001), 0.007) * ga(x, 0.048) * front
        for k in range(3):     # brow-to-nose folds
            disp = disp + 0.0016 * ga(z - (ez - 0.004 - 0.006 * k), 0.0015) * ga(x, 0.006) * front
        ridge = np.clip(disp / 0.005, 0, 1) * 0.4
    elif sp == "bajoran":
        for k in range(4):
            zk = ez - 0.002 - 0.0045 * k
            disp = disp + (0.0019 - 0.0002 * k) * ga(z - zk, 0.0011) * ga(x, 0.0085 + 0.0008 * k) * front
        ridge = np.clip(disp / 0.001, 0, 1) * 0.35
    elif sp == "cardassian":
        # spoon: a teardrop raised rim on the forehead, slightly hollow inside
        zc, b = bz + 0.027, 0.020
        u = (z - zc) / b
        aw = 0.0105 * (1.0 + 0.35 * np.clip(u, -1, 1))
        e = np.sqrt((x / aw) ** 2 + u ** 2)
        disp = (0.0028 * ga(e - 1.0, 0.13) - 0.0009 * sm(1.0, 0.55, e)) * front
        # cord from the spoon's foot down the nose bridge
        disp = disp + 0.0016 * ga(x, 0.0035) * sm(zc - b - 0.002, zc - b + 0.004, z) * sm(ez - 0.012, ez, z) * front
        # brow ridges running out over the temples toward the ears
        for sx in (-1, 1):
            pts = np.array([[sx * 0.014, 0, bz + 0.006], [sx * 0.032, 0, bz + 0.010], [sx * 0.052, 0, bz + 0.004],
                            [sx * 0.066, 0, ez - 0.002]])
            P2 = q.copy()
            P2[:, 1] = 0
            d, t = polyline_dist(P2, pts)
            disp = disp + 0.0032 * ga(d, 0.0033) * (x * sx > 0) * sm(-0.02, 0.01, y)
        # cobra neck: two cords each side from behind the ear down and out to the shoulder
        # (lines in the side view, y-z, on each side of the neck)
        for sx in (-1, 1):
            ec, ext = hm.ears_local["l" if sx * hm.ears_local["l"][0][0] > 0 else "r"]
            side = sm(0.015, 0.035, x * sx)
            yz = np.stack([np.zeros_like(y), y, z], 1)
            for k, (a, bo, r_, h_) in enumerate((
                    (np.array([0.0, ec[1] - 0.016, ec[2] - 0.006]), np.array([0.0, -0.046, -0.15]), 0.0050, 0.0090),
                    (np.array([0.0, ec[1] + 0.008, ec[2] - 0.034]), np.array([0.0, 0.016, -0.15]), 0.0038, 0.0050))):
                d, t = seg_dist(yz, a, bo)
                off_ear = np.linalg.norm((q - ec) / (ext * 1.25 + 0.004), axis=1)
                disp = disp + h_ * ga(d, r_) * sm(0.0, 0.035, t) * side * sm(0.9, 1.15, off_ear)
        ridge = np.clip(disp / 0.003, 0, 1)
    elif sp == "ferengi":
        # nose-bridge folds climbing the forehead, a central lobe line, lumpy brow
        for k in range(5):
            zk = ez - 0.004 + 0.0075 * k
            disp = disp + (0.0022 - 0.0002 * k) * ga(z - zk, 0.0016) * ga(x, 0.010 + 0.002 * k) * front
        disp = disp + 0.0025 * ga(x, 0.007) * sm(ez + 0.03, ez + 0.05, z) * sm(top, top - 0.05, z) * front
        # ear shells: concentric ridges in the bowl, a scalloped rim
        for s in "lr":
            ec, ext = hm.ears_local[s]
            sx = np.sign(ec[0])
            rel = q - ec
            w = sm(0.0, 0.02, rel[:, 0] * sx + 0.012) * (np.linalg.norm(rel / (ext * 1.15 + 1e-4), axis=1) < 1.3)
            rr = np.hypot(rel[:, 1] / max(ext[1], 1e-3), rel[:, 2] / max(ext[2], 1e-3))
            ang = np.arctan2(rel[:, 2], rel[:, 1])
            disp = disp + w * (0.0014 * np.sin(rr * 2 * pi * 2.6) * sm(0.25, 0.4, rr) * sm(1.0, 0.8, rr)
                               + 0.0018 * np.sin(ang * 7) * sm(0.8, 1.0, rr))
        ridge = np.clip(np.abs(disp) / 0.002, 0, 1) * 0.5
    return disp, ridge, vec


def needs_sculpt(sp):
    return sp in ("klingon", "romulan", "tellarite", "bajoran", "cardassian", "ferengi", "trill")


def trill_spot_list(hm):
    """Two loose columns of spots each side: temple behind the eye corner, down the side of the face in front of the
    ear, down the side of the neck; getting smaller as they go. (head-local centre, radius)"""
    R = rng(hm.P["seed"] + 77)
    out = []
    ez = hm.ez - hm.head_c[2]
    for s in "lr":
        ec, ext = hm.ears_local[s]
        sx = np.sign(ec[0])
        line = np.array([[sx * 0.050, 0.080, ez + 0.030], [sx * 0.058, 0.066, ez - 0.004],
                         [sx * 0.062, 0.050, ez - 0.040], [sx * 0.056, 0.024, -0.085], [sx * 0.050, 0.010, -0.14]])
        for k in range(30):
            t = k / 29
            seg_ = min(int(t * 4), 3)
            u = t * 4 - seg_
            p = line[seg_] * (1 - u) + line[seg_ + 1] * u
            for col in (-1, 1):
                if R.uniform() < 0.3:
                    continue
                pp = p + np.array([0, col * 0.0048 * (1 - 0.4 * t), 0]) + R.normal(0, 0.0016, 3)
                loc, _ = surface_point(hm, pp + hm.head_c)
                out.append((loc - hm.head_c, (0.0036 - 0.0018 * t) * R.uniform(0.75, 1.25)))
    return out


def sculpt_body(body, hm, c):
    """Two Catmull-Clark levels applied (weights and attributes carried over), then the species displacement."""
    bpy.context.view_layer.objects.active = body
    m = body.modifiers.new("Pre", "SUBSURF")
    m.levels = 2
    while body.modifiers[0].name != "Pre":
        bpy.ops.object.modifier_move_up(modifier="Pre")
    bpy.ops.object.modifier_apply(modifier="Pre")
    me = body.data
    n = len(me.vertices)
    co = np.zeros(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nn = np.zeros(n * 3)
    me.vertices.foreach_get("normal", nn)
    nn = nn.reshape(-1, 3)
    disp, ridge, vec = species_sculpt(hm, c, co - hm.head_c, nn)
    co = co + nn * disp[:, None] + vec
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    # ridges a shade warmer / darker in their folds
    sk = {"cardassian": (1.06, 0.92, 0.74), "klingon": (0.9, 0.78, 0.72), "ferengi": (0.95, 0.85, 0.8)}.get(
        c["species"], (0.94, 0.86, 0.84))
    t = np.zeros(n * 4)
    me.color_attributes["tint"].data.foreach_get("color", t)
    t = t.reshape(-1, 4)
    t[:, :3] *= 1 + (np.array(sk) - 1)[None, :] * ridge[:, None]
    if c["species"] == "trill":
        q = co - hm.head_c
        spot = np.zeros(n)
        for cc, r in trill_spot_list(hm):
            d = np.linalg.norm((q - cc) * np.array([1.0, 0.8, 1.0]), axis=1)
            spot = np.maximum(spot, sm(r * 1.5, r * 0.8, d))
        sk_ = np.array(SK[c["skin"]])
        want = np.array((0.28, 0.14, 0.075)) / np.maximum(sk_, 0.05)
        t[:, :3] = t[:, :3] * (1 - spot[:, None]) + want[None, :] * spot[:, None]
    me.color_attributes["tint"].data.foreach_set("color", t.ravel())


def skin_extra(name, co, faces, rig, hm, objs, mat=None):
    """A skin-coloured extra (antennae ...): weights from the nearest body vertex, skin attributes."""
    W = nearest_rows(co, hm.body_idx, hm.co)
    ob = skinned_object(name, co, faces, W, rig, [mat or bp.material("Skin")], subsurf=1)
    me = ob.data
    bp.set_attr(me, "tint", np.ones((len(co), 4)), "FLOAT_COLOR")
    bp.set_attr(me, "skm", np.zeros((len(co), 4)), "FLOAT_COLOR")
    bp.set_attr(me, "rest", co - hm.head_c, "FLOAT_VECTOR")
    objs.append(ob)
    return ob


def tube(path, radii, nseg=14):
    """Tube mesh along a polyline with per-point radii, closed caps. Returns (co, faces)."""
    path = np.asarray(path, float)
    n = len(path)
    co, faces = [], []
    t = np.gradient(path, axis=0)
    t = nrm(t)
    ref = np.array([1.0, 0, 0]) if abs(t[0][0]) < 0.9 else np.array([0, 1.0, 0])
    e1 = nrm(np.cross(t[0], ref))
    for i in range(n):
        e1 = nrm(e1 - t[i] * (e1 @ t[i]))
        e2 = np.cross(t[i], e1)
        for j in range(nseg):
            a = 2 * pi * j / nseg
            co.append(path[i] + (e1 * math.cos(a) + e2 * math.sin(a)) * radii[i])
    for i in range(n - 1):
        for j in range(nseg):
            k = (j + 1) % nseg
            faces.append((i * nseg + j, i * nseg + k, (i + 1) * nseg + k, (i + 1) * nseg + j))
    co.append(path[0] - t[0] * radii[0] * 0.3)
    co.append(path[-1] + t[-1] * radii[-1] * 0.5)
    a0, a1 = len(co) - 2, len(co) - 1
    for j in range(nseg):
        k = (j + 1) % nseg
        faces.append((k, j, a0))
        faces.append(((n - 1) * nseg + j, (n - 1) * nseg + k, a1))
    return np.array(co), faces


def spheres(centres, radii, seg=10, rings=6):
    co, faces = [], []
    for c_, r in zip(centres, radii):
        b = len(co)
        co.append(c_ + np.array([0, 0, -r]))
        for i in range(1, rings):
            th = pi * i / rings - pi / 2
            for j in range(seg):
                a = 2 * pi * j / seg
                co.append(c_ + np.array([math.cos(th) * math.cos(a), math.cos(th) * math.sin(a), math.sin(th)]) * r)
        co.append(c_ + np.array([0, 0, r]))
        top = len(co) - 1
        for j in range(seg):
            k = (j + 1) % seg
            faces.append((b, b + 1 + k, b + 1 + j))
            for i in range(rings - 2):
                r0, r1 = b + 1 + i * seg, b + 1 + (i + 1) * seg
                faces.append((r0 + j, r0 + k, r1 + k, r1 + j))
            rl = b + 1 + (rings - 2) * seg
            faces.append((rl + j, rl + k, top))
    return np.array(co), faces


def surface_point(hm, p):
    """Nearest point on the rest body and its normal."""
    loc, nn, _, _ = hm.bvh_body_world.find_nearest(V(tuple(p)))
    return np.array(loc), np.array(nn)


def species_extras(hm, c, rig, objs):
    sp = c["species"]
    hc = hm.head_c
    if sp == "andorian":
        F = hm.field
        for sx in (-1, 1):
            d0 = np.array([sx * 0.24, 0.55, 0.8])
            d0 = d0 / np.linalg.norm(d0)
            root = d0 * (F.R(d0[None])[0][0] - 0.003) + hc
            ts = np.linspace(0, 1, 24)
            L = 0.088
            path = np.stack([root[0] + sx * (0.012 * ts + 0.01 * ts ** 2), root[1] + 0.012 * ts - 0.012 * ts ** 2,
                             root[2] + L * ts], 1)
            radii = 0.0040 * (1 - 0.5 * ts) + 0.0042 * ga(ts - 0.95, 0.045) * (ts > 0.8) + 0.0012 * ga(ts, 0.08)
            co, fc = tube(path, radii)
            skin_extra(f"Antenna{sx}", co, fc, rig, hm, objs)
    if sp == "bajoran":
        # the right ear (+x): a cuff at the top of the helix, beads down the rim to the lobe and a short drop
        s = "r" if hm.ears_local["r"][0][0] > 0 else "l"
        ec, ext = hm.ears_local[s]
        sx = np.sign(ec[0])
        topp = ec + np.array([sx * ext[0] * 0.6, -ext[1] * 0.2, ext[2] * 0.85])
        lobe = ec + np.array([sx * ext[0] * 0.5, ext[1] * 0.25, -ext[2] * 0.9])
        pts = []
        for k in range(18):
            t = k / 17
            p = topp * (1 - t) + lobe * t + np.array([sx * 0.006 * math.sin(pi * t), -0.010 * math.sin(pi * t), 0])
            pts.append(p + hc)
        drop = [lobe + hc + np.array([sx * 0.003, 0, -0.004 - 0.0035 * k]) for k in range(7)]
        cen = [np.array(p) for p in pts + drop]
        rr = [0.0011] * len(pts) + [0.0012] * 6 + [0.0026]
        co, fc = spheres(cen + [topp + hc + np.array([sx * 0.002, 0, 0])], rr + [0.0035])
        W = nearest_rows(co, hm.body_idx, hm.co)
        objs.append(skinned_object("Earring", co, fc, W, rig, [bp.material("chain_gold")], subsurf=1))
    if sp == "ferengi":
        # pointed teeth along the front of MakeHuman's teeth helpers
        for g, down in (("helper-upper-teeth", True), ("helper-lower-teeth", False)):
            u = np.unique(D.faces[g].ravel())
            P = hm.co[u]
            front = P[P[:, 1] > np.percentile(P[:, 1], 40)]
            zedge = front[:, 2].min() if down else front[:, 2].max()
            xs = np.linspace(front[:, 0].min() * 0.95, front[:, 0].max() * 0.95, 11)
            co, fc = [], []
            for xk in xs:
                near = front[np.argsort(np.abs(front[:, 0] - xk))[:4]]
                yk = near[:, 1].max() - 0.0005
                base = np.array([xk, yk, zedge + (0.0055 if down else -0.0055)])
                tip = np.array([xk, yk + 0.0004, zedge - (0.0016 if down else -0.0016)])
                b = len(co)
                for j in range(6):
                    a = 2 * pi * j / 6
                    co.append(base + np.array([math.cos(a) * 0.0021, math.sin(a) * 0.0013, 0]))
                co.append(tip)
                for j in range(6):
                    k = (j + 1) % 6
                    fc.append((b + j, b + k, b + 6) if down else (b + k, b + j, b + 6))
            co = np.array(co)
            W = D.W[u[np.argmin(np.linalg.norm(P[None, :, :] - co[:, None, :], axis=2), axis=1)]]
            objs.append(skinned_object("Fangs", co, fc, W, rig, [bp.material("teeth")], subsurf=0))


# ================================================================ one character

# changes to build_portraits' cast for this script (file, species, sex and department stay the same)
CAST_OVERRIDES = {
    "bajoran_f_archaeology": dict(style="ponytail"),      # the ear chain shows (a bob hid it)
}


def build_character(c, loc=(0, 0, 0), yaw=0.0, head_turn=0.0, cam=None, t=0.0, height=1.0, full=False):
    """Build one person (body, face, eyes, teeth, hair, brows, lashes, beard, species features, outfit), posed.
    Returns (objects, posed head centre)."""
    c.setdefault("faction", FACTION_OF[c["species"]])
    c.update(CAST_OVERRIDES.get(c["id"], {}))
    P = person(c)
    hm = Human(c, P)
    hm.full = full
    landmarks(hm)
    species_base(hm, c)
    CUR.clear()
    CUR.update(c)
    CUR["species"] = c["species"]
    CUR["F"] = dict(freckles=c.get("freckles", 0.0), age=hm.age01, fem=1.0 if hm.fem else 0.0, species=c["species"])
    CUR["hm"] = hm
    CUR["pointed"] = c["species"] in ("vulcan", "romulan")
    hair_setup(hm, c)
    bp.char_materials(c)
    objs = []
    rig = build_rig(hm)
    objs.append(rig)
    tint, skm = skin_zones(hm, c)
    cov = outfit(hm, c, rig, objs)
    bf = D.faces["body"]
    body, used = mesh_from("Body", hm.co, bf[~cov[bf].all(1)], [bp.material("Skin")])
    bp.set_attr(body.data, "tint", tint[used], "FLOAT_COLOR")
    bp.set_attr(body.data, "skm", skm[used], "FLOAT_COLOR")
    bp.set_attr(body.data, "rest", hm.co[used] - hm.head_c, "FLOAT_VECTOR")
    skin_to(body, used, rig)
    if needs_sculpt(c["species"]):
        sculpt_body(body, hm, c)
    else:
        sub = body.modifiers.new("Subsurf", "SUBSURF")
        sub.levels = 1
        sub.render_levels = 2
    objs.append(body)
    species_extras(hm, c, rig, objs)
    for g, mk in (("helper-upper-teeth", "teeth"), ("helper-lower-teeth", "teeth"), ("helper-tongue", "gum")):
        o, u = mesh_from(g, hm.co, D.faces[g], [bp.material(mk)])
        skin_to(o, u, rig)
        s2 = o.modifiers.new("Subsurf", "SUBSURF")
        s2.levels = s2.render_levels = 1
        objs.append(o)
    rig.location = loc
    rig.rotation_euler = (0, 0, rad(yaw))
    rig.scale = (height,) * 3
    bpy.context.view_layer.update()
    pose_body(rig, hm, head_turn, t=t)
    Dh = head_matrix(rig)
    hm.Dh = Dh
    head = Dh @ V(hm.head_c.tolist())
    cam = V(cam) if cam is not None else head + V((0, 1.2, 0))
    up = Dh.to_3x3() @ V((0, 0, 1))
    for s, k in (("l", 1), ("r", -1)):
        ce = Dh @ V(hm.eye_c[s].tolist())
        ce = ce - (cam - ce).normalized() * 0.0004
        objs += eyeball(ce, hm.eye_r[s] * 0.965, cam + V((0.012 * k, 0, 0)), up,
                        50.0 if c["species"] == "betazoid" else 57.5)
    if not NO_HAIR:
        strands(hm, c, objs, body)
    return objs, head


# ================================================================ shots

YAW = 24.0
CAM_DIST = 1.05


def portrait(cam, c, extras):
    camp = V((0.0, CAM_DIST, 1.58))
    objs, head = build_character(c, yaw=YAW, head_turn=-12.0, cam=camp)
    tall = 0.06 if c["species"] == "andorian" else 0.0
    target = head + V((0, -0.03, -0.05 + tall))
    camp = V((target.x, target.y + CAM_DIST + 2.2 * tall, target.z + 0.06))
    bp.aim(cam, camp, target, 85)
    cam.data.dof.use_dof = True
    cam.data.dof.focus_distance = (camp - head).length - 0.04
    cam.data.dof.aperture_fstop = 3.5
    off = head - V((0, 0, 1.64))
    for o, p0 in extras["portrait_lights"]:
        o.location = p0 + off
    eye_l = bp.light("EyeLight", "AREA", 1.6, 0.12, camp + V((-0.16, -0.05, 0.10)), head, (1.0, 0.97, 0.94))
    bd = bp.plane("Backdrop", target + V((0, -1.5, 0.08)), 2.2, 2.2, extras["bd"][c["faction"]], camp)
    path = os.path.join(OUT, c["id"] + (arg("--tag") or "") + ".jpg")
    if "--closeup" not in argv:
        bp.render(path, RES, RES)
    for a in ORBIT:
        hc = head + V((0, -0.01, 0))
        d = V((math.sin(rad(a)), math.cos(rad(a)), 0)) * float(arg("--orbit-dist", "0.55"))
        bp.aim(cam, hc + d + V((0, 0, 0.01)), hc + V((float(arg("--orbit-x", "0")), 0, float(arg("--orbit-z", "0")))), 85)
        cam.data.dof.use_dof = False
        bp.render(os.path.join(OUT, f"{c['id']}{arg('--tag') or ''}_orbit{int(a)}.jpg"), RES, RES)
    bp.remove(objs + [bd, eye_l])
    return path


def lineup(cam, chars, extras):
    """Four Federation officers full length on a curved cyclorama, 3/4 view, 1600 x 900."""
    objs_all = []
    xs = [-1.35, -0.45, 0.45, 1.35]
    heights = [1.03, 0.98, 1.0, 0.96]
    camp = V((2.2, 6.6, 1.55))
    for k, c in enumerate(chars):
        objs, _ = build_character(c, loc=(xs[k], 0.15 * (k % 2), 0), yaw=28 + 4 * k, head_turn=-6, cam=camp,
                                  t=0.1 + 0.2 * k, height=heights[k], full=True)
        objs_all += objs
    cy = bp.cyclorama("Cyc", 18, 9.0, 8.0, 2.2, bp.cyc_material(bp.backdrop_image("federation", 1600, 900, side=1)))
    cy.location = (-0.6, -3.4, 0)
    for o, _ in extras["portrait_lights"]:
        o.hide_render = True
    ls = [bp.light("LKey", "AREA", 1100, 2.4, (-3.2, 4.6, 4.2), (0, 0, 1.1), (1.0, 0.95, 0.88)),
          bp.light("LRim", "AREA", 800, 2.0, (3.4, -2.6, 3.4), (0, 0, 1.2), (0.82, 0.9, 1.0)),
          bp.light("LRim2", "AREA", 450, 2.0, (-3.6, -2.2, 3.0), (0, 0, 1.2), (0.9, 0.92, 1.0)),
          bp.light("LFill", "AREA", 160, 5.0, (4.0, 5.0, 2.0), (0, 0, 1.0), (0.88, 0.93, 1.0))]
    for x in xs:   # soft pools on the floor under each officer
        ls.append(bp.light("LPool", "SPOT", 90, 0.6, (x + 0.2, 0.6, 5.2), (x, 0.1, 0.0), (1.0, 0.96, 0.9)))
        ls[-1].data.spot_size = rad(38)
        ls[-1].data.spot_blend = 0.9
    bp.aim(cam, camp, V((0.0, 0.0, 0.93)), 50)
    cam.data.dof.use_dof = False
    bp.render(os.path.join(OUT, "lineup.jpg"), 1600, 900)
    bp.remove(objs_all + [cy] + ls)
    for o, _ in extras["portrait_lights"]:
        o.hide_render = False


OUTFIT_TEXT = {"klingon": "dark leather, layered metal pauldrons and chest plates, red-trimmed collar",
               "cardassian": "grey-brown ribbed armour, shoulder plates rising to a flared neck plate",
               "ferengi": "patterned jacket, cream shirt front, gold flared collar"}


def write_json(cast):
    entries = []
    for c in cast:
        f = c["id"] + ".jpg"
        if not os.path.exists(os.path.join(OUT, f)):
            continue
        c = dict(c)
        c.setdefault("faction", FACTION_OF[c["species"]])
        c.update(CAST_OVERRIDES.get(c["id"], {}))
        P = person(c)
        info = P["info"]
        fac = FACTION_OF[c["species"]]
        if fac == "federation":
            outfit_s = "colony-service jumpsuit, department yoke"
        elif fac == "romulan":
            outfit_s = ("grey-green tunic, high stand collar, slim quilted shoulder caps" if c["sex"] == "f"
                        else "grey-green tunic, quilted padded shoulders")
        else:
            outfit_s = OUTFIT_TEXT[fac]
        entries.append(dict(
            file=f, species=c["species"], faction=fac, sex=c["sex"], department=c.get("dept"),
            department_colour=DEPTS.get(c.get("dept")) if c.get("dept") else None,
            skin=c["skin"], skin_rgb=[round(x, 3) for x in SK[c["skin"]]], hair=c.get("hair") or "none (bald)",
            hair_style=c.get("style") or "bald", beard=c.get("beard"), eyes=c["iris"], age_band=c["age"],
            apparent_age=c.get("years"), face_shape=info["face"], nose=info["nose"], seed=info["seed"],
            expression=("grin" if c["species"] == "ferengi" else "smile" if info["smile"] >= 0.3 else
                        "friendly" if info["smile"] > 0.05 else "calm"),
            stylised_feature=info["strongest"],
            makehuman=dict(gender=round(P["macro"]["gender"], 3), age=round(P["macro"]["age"], 3),
                           muscle=round(P["macro"]["muscle"], 3), weight=round(P["macro"]["weight"], 3),
                           ethnicity={k: round(v, 3) for k, v in info["race"].items()}),
            outfit=outfit_s))
    doc = dict(generator="blender/build_portraits_mh.py",
               note="Mockup portraits on the MakeHuman 1.x base mesh (CC0); original designs, no canon uniforms or insignia.",
               size=[RES, RES], departments=DEPTS,
               lineup=dict(file="lineup.jpg", left_to_right=bp.LINEUP[::-1]) if os.path.exists(
                   os.path.join(OUT, "lineup.jpg")) else None, portraits=entries)
    with open(os.path.join(OUT, "portraits.json"), "w") as fh:
        json.dump(doc, fh, indent=1)
    print("json", len(entries), flush=True)


def main():
    global D
    D = MHData()
    weight_matrix()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bc.palette()
    os.makedirs(OUT, exist_ok=True)
    cam = bp.setup_scene()
    pl = []
    for args in (("Key", "AREA", 62, 0.8, (-1.2, 0.9, 2.1), (0, 0, 1.64), (1.0, 0.93, 0.84)),
                 ("Rim", "AREA", 95, 0.5, (0.9, -0.8, 1.95), (0, 0, 1.64), (0.78, 0.88, 1.0)),
                 ("Rim2", "AREA", 40, 0.5, (-0.9, -0.85, 1.9), (0, 0, 1.62), (0.9, 0.92, 1.0)),
                 ("Fill", "AREA", 6, 2.0, (1.3, 1.5, 1.4), (0, 0, 1.6), (0.85, 0.92, 1.0))):
        o = bp.light(*args)
        pl.append((o, o.location.copy()))
    extras = {"portrait_lights": pl}
    extras["bd"] = {f: bp.backdrop_material("BD_" + f, bp.backdrop_image(f)) for f in FACTION_COLOUR}
    cast = [c for c in bp.CAST if (not ONLY_SPECIES or c["species"] in ONLY_SPECIES)
            and (not ONLY_SEX or c["sex"] == ONLY_SEX) and (not ONLY or c["id"] in ONLY)]
    if "portraits" in SHOTS:
        for c in cast:
            portrait(cam, dict(c), extras)
        for sd in SEEDS:
            for sp in (ONLY_SPECIES or bp.SPECIES_ORDER):
                portrait(cam, bp.look(sd, sp, ONLY_SEX), extras)
    if not ONLY and not SEEDS and not ONLY_SPECIES and not ONLY_SEX:
        write_json(bp.CAST)
    if "lineup" in SHOTS:
        lineup(cam, [dict(next(c for c in bp.CAST if c["id"] == i)) for i in bp.LINEUP], extras)
        if not ONLY and not SEEDS and not ONLY_SPECIES and not ONLY_SEX:
            write_json(bp.CAST)


if __name__ == "__main__":
    main()
