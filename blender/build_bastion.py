"""Bastion-class system patrol cutter (260 m): high-detail model, path-traced stills and the game model.

Built on the Cochrane pipeline (blender/prewarp_ships.md). Layout, sizes and systems follow the class page
(design constants BAS_D): x toward the bow, y to port, z dorsal; metres; x -130 (nozzle exits) to +130 (docking collar).

  /home/user/.bpyvenv/bin/python blender/build_bastion.py --shot hero --out blender/previews/bastion --tmp <dir> --earth-tex <bmng.jpg>
  --shot hero|drum|stern|guns|bay|side   --drive (torches lit, radiators at full heat)   --only aft,mid,fwd
  --game game/models/ships/bastion.glb

What this rebuild changed against the old low-detail model (see blender/previews/bastion/changes.json):
  * the pressure hull is a round cylinder with domed ends inside the octagonal armour box; the octagon is only
    spaced plates on standoffs (flat plates are fine for armour, not for a pressure wall);
  * the shadow shield is a thin tungsten + lithium-hydride plate on a rib frame, not a 3 m solid slab;
  * the drum is one pressure hull with rounded ends under spaced rim plates, turning between the fixed collars;
  * coilgun barrels are stacks of drive coils in a stringer cage, not square beams;
  * tanks have domed ends, cradles and armour shells; radiators are thin segmented strips on hinge booms with stays;
  * the boat is a round-hulled capsule, not a flat-sided box.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import build_cochrane as C          # imports bpy first; mathutils comes with it
import cochrane_kit as K
from mathutils import Matrix, Vector
from build_cochrane import k, on, P, R, rotX, rotY, rotZ, move, orient, TAU, PI

NAME, REG = 'BASTION', 'UE-04'

D = dict(hull=dict(w=24.4, h=18.4, c=5.0, wi=23.2, hi=17.2, ci=4.6), aft=(-96.0, -26.0), fwd=(26.0, 92.0),
         bow=dict(x0=92.0, x1=125.5, w=9.0, h=6.6, c=2.2), tip=130.0,
         drum=dict(r=20.0, rOut=20.8, rIn=9.0, w=33.0, g=0.5, dir=1), collar=dict(r=23.5, x0=17.0, x1=24.0), core=8.5,
         tor=dict(y=6.5, x0=-119.0, x1=-105.0, rc=2.9, rp=2.2, exit=-130.0, re=4.6),
         shield=dict(x0=-102.0, x1=-99.0, w=40.0, h=26.0, c=7.0),
         htr=dict(x0=-94.0, x1=-34.0, root=14.4, n=5, ws=7.0), ltr=dict(x0=30.0, x1=84.0, root=14.4, n=3, ws=6.6),
         tanks=dict(r=3.0, x0=-90.0, x1=-40.0), bay=dict(x0=-86.0, x1=-56.0, w=12.0, h=6.0), boat=dict(x0=-80.0, x1=-62.0),
         gun=dict(x=58.0, L=24.0), pd=dict(x=40.0, aft=-33.0), cells=dict(x0=-84.0, x1=-62.0), rcs=dict(bow=112.0, aft=-94.0))

# the ship's own materials: phased-array faces
C.EXTRA['array'] = (lambda: K.panel('array', (0.085, 0.095, 0.115), (0.06, 0.07, 0.09), size=(0.5, 0.5), seam=0.07, seam_mul=0.45,
                                    rough=0.35, metal=0.3, wob=0.001, grime=0.1),
                    ('pan', (0.085, 0.095, 0.115), (0.06, 0.07, 0.09), 0.45, (0.5, 0.5), 0.3, 0.35))


# ------------------------------------------------------------------ the octagonal armour box
def octp(w, h, c):
    """Octagon outline in (y, z). Face i runs from point i to i+1: 0 port side, 1 port-dorsal chamfer, 2 dorsal,
    3 starboard-dorsal chamfer, 4 starboard side, 5 starboard-ventral chamfer, 6 ventral, 7 port-ventral chamfer."""
    a, b = w / 2, h / 2
    return [(a, -b + c), (a, b - c), (a - c, b), (-a + c, b), (-a, b - c), (-a, -b + c), (-a + c, -b), (a - c, -b)]


H = D['hull']
OCT = octp(H['w'], H['h'], H['c'])
OCTI = octp(H['wi'], H['hi'], H['ci'])
HY, HZ = H['w'] / 2, H['h'] / 2
FACE = []          # (angle of the outward normal, distance from the axis, half width)
for _i in range(8):
    _p, _q = OCT[_i], OCT[(_i + 1) % 8]
    _a = _i * PI / 4
    FACE.append((_a, (_p[0] + _q[0]) / 2 * math.cos(_a) + (_p[1] + _q[1]) / 2 * math.sin(_a), math.hypot(_q[0] - _p[0], _q[1] - _p[1]) / 2))
P_, PT, T_, ST, S_, SB, B_, PB = range(8)
CHAMF = (PT, ST, SB, PB)


def F(i, x=0.0, t=0.0, h=0.0):
    """Frame on hull face i: local x axial, y out of the face, z along the face (toward the next face)."""
    a, d, _ = FACE[i]
    return rotX(a) @ move(x, d + h, t)


def lerp_sec(A, B, f):
    return [(a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f) for a, b in zip(A, B)]


def scale_sec(A, s):
    return [(p[0] * s, p[1] * s) for p in A]


def loft(x0, x1, A, B, mat, cap0=True, cap1=True):
    """Skin between two outlines (y, z) at x0 and x1."""
    n = len(A)
    v = [(x0, p[0], p[1]) for p in A] + [(x1, p[0], p[1]) for p in B]
    f, uv = [], []
    ou, s = R.random() * 60, 0.0
    for i in range(n):
        j = (i + 1) % n
        d = math.hypot(A[j][0] - A[i][0], A[j][1] - A[i][1])
        f.append((i, j, n + j, n + i))
        uv.append([(x0 + ou, s), (x0 + ou, s + d), (x1 + ou, s + d), (x1 + ou, s)])
        s += d
    if cap0:
        f.append(tuple(range(n - 1, -1, -1)))
        uv.append([(A[i][0] + ou, A[i][1]) for i in range(n - 1, -1, -1)])
    if cap1:
        f.append(tuple(range(n, 2 * n)))
        uv.append([(B[i][0] + ou, B[i][1]) for i in range(n)])
    k.add(mat, v, f, uv)


def slab(pts, th, mat):
    """A flat plate: four corner points of its outer face, th thick toward the axis."""
    p = [Vector(q) for q in pts]
    n = (p[1] - p[0]).cross(p[3] - p[0]).normalized()
    c = (p[0] + p[1] + p[2] + p[3]) / 4
    if n.dot(Vector((0, c.y, c.z))) < 0:
        n = -n
    q = [v - n * th for v in p]
    e1 = (p[1] - p[0]).normalized()
    e2 = n.cross(e1)
    ou, ov = R.random() * 60, R.random() * 60
    V = p + q

    def uv(v):
        return ((v - p[0]).dot(e1) + ou, (v - p[0]).dot(e2) + ov)
    fc = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    U = [[uv(V[i]) for i in f] for f in fc[:2]]
    for f in fc[2:]:
        L = (V[f[1]] - V[f[0]]).length
        U.append([(ou, ov), (ou + L, ov), (ou + L, ov + th), (ou, ov + th)])
    k.add(mat, [tuple(v) for v in V], fc, U)


def plates(x0, x1, A, B, nrow, split, mat, gap=0.3, sgap=0.22, th=0.12, posts=None, bolts=True):
    """Spaced armour: rows of flat plates over the outlines A (at x0) to B (at x1). split[i] plates across face i.
    mat(row, face, j) -> material name or None to leave the plate off."""
    for r in range(nrow):
        xa = x0 + (x1 - x0) * r / nrow + gap / 2
        xb = x0 + (x1 - x0) * (r + 1) / nrow - gap / 2
        SA = lerp_sec(A, B, (xa - x0) / (x1 - x0))
        SB_ = lerp_sec(A, B, (xb - x0) / (x1 - x0))
        for i in range(8):
            ns = split[i]
            for j in range(ns):
                m = mat(r, i, j)
                if not m:
                    continue
                pts = []
                for (xx, S, order) in ((xa, SA, (0, 1)), (xb, SB_, (1, 0))):
                    p, q = Vector(S[i]), Vector(S[(i + 1) % 8])
                    L = (q - p).length
                    e = sgap / 2 / L
                    u0, u1 = j / ns + e, (j + 1) / ns - e
                    for u in ((u0, u1) if order == (0, 1) else (u1, u0)):
                        w = p.lerp(q, u)
                        pts.append((xx, w.x, w.y))
                # pts: (xa,u0) (xa,u1) (xb,u1) (xb,u0); real plates never sit perfectly flush
                cq = [Vector(pts[q_]) for q_ in (0, 3, 2, 1)]
                nq = (cq[1] - cq[0]).cross(cq[3] - cq[0]).normalized()
                ctr = (cq[0] + cq[1] + cq[2] + cq[3]) / 4
                if nq.dot(Vector((0, ctr.y, ctr.z))) < 0:
                    nq = -nq
                jit = nq * (R.uniform(-0.025, 0.025) if k.lod >= 1 else 0.0)
                cq = [c_ + jit for c_ in cq]
                slab(cq, th, m)
                if bolts and k.lod >= 1:          # fastener bosses near the corners
                    for c_ in cq:
                        b_ = c_.lerp(ctr, 0.13) + nq * 0.02
                        with k.xf(orient(b_, nq)):
                            k.lathe([(0, 0.13), (0.03, 0.13), (0.045, 0.09), (0.045, 0)], 'grey', seg=6)
                if posts and k.lod >= 1:
                    for pt in (pts[0], pts[2]):
                        c = Vector(pt)
                        rad = Vector((0, c.y, c.z)).normalized()
                        k.tube(c - rad * 0.1, c - rad * posts, 0.05, 'grey', seg=4)


def armour_mat(stripe=None, blue=None):
    def m(r, i, j):
        if stripe is not None and r == stripe and i not in (P_, S_):
            return 'orange'
        if blue is not None and r == blue and i not in (P_, S_):
            return 'blue'
        return 'tile' if R.random() > 0.06 else 'tile2'
    return m


def frail(i, x0, x1, t=0.0):
    a, d, _ = FACE[i]
    with k.xf(rotX(a) @ move(0, 0, t)):
        C.hrail(x0, x1, d, 0.0)


def fpipe(i, x0, x1, t, rad, mat):
    a, d, _ = FACE[i]
    with k.xf(rotX(a) @ move(0, 0, t)):
        C.axial_pipe(x0, x1, d, 0.0, rad, mat)


def fhatch(i, x, t=0.0, rad=0.8):
    a, d, _ = FACE[i]
    with k.xf(rotX(a) @ move(0, 0, t)):
        C.hatch(x, 0.0, d, rad)


def port(i, x, t=0.0, lit=True, rad=0.42):
    """Small armoured viewport."""
    with k.xf(F(i, x, t) @ orient((0, 0, 0), (0, 1, 0))):
        k.lathe([(0, rad + 0.22), (0.14, rad + 0.22), (0.14, rad), (0.05, rad)], 'grey', seg=14, sharp=30)
        k.lathe([(0.06, rad), (0.06, 0)], 'glass' if lit else 'glass_dark', seg=12)


def locker(i, x, t=0.0):
    """Damage-control locker: orange, so a suited repair party finds it."""
    with k.xf(F(i, x, t)):
        k.box((0, 0.55, 0), (1.7, 1.1, 1.3), 'orange', bevel=0.06)
        k.box((0, 1.12, 0), (1.0, 0.05, 0.8), 'white_s')
        k.tube((-0.6, 1.1, 0.5), (0.6, 1.1, 0.5), 0.04, 'yellow', seg=4)


def fgreeble(i, x0, x1, n, span=0.8, hmax=0.6, mats=C.GM):
    _, _, hw = FACE[i]
    if k.lod < 1:
        n = n // 2
    for _ in range(n):
        x, t, u = R.uniform(x0, x1), R.uniform(-1, 1) * hw * span, R.random()
        with k.xf(F(i, x, t)):
            if u < 0.72:
                w, d, h = 0.6 + R.random() * 2.4, 0.5 + R.random() * 1.6, 0.15 + R.random() * hmax
                k.box((0, h / 2 - 0.03, 0), (w, h, d), R.choice(mats), bevel=0.05 if min(w, d, h) > 0.35 else 0.0)
            elif u < 0.88:
                L, rad = 1.0 + R.random() * 2.0, 0.22 + R.random() * 0.3
                k.can(-L / 2, L / 2, rad, R.choice(['silver', 'gold', 'white_s', 'grey']), ch=0.05, seg=12, oy=rad * 0.85)
                k.box((0, 0.05, 0), (L * 0.7, 0.1, rad * 2.2), 'grey')
            else:
                h = 1.0 + R.random() * 1.8
                k.tube((0, 0, 0), (0, h, 0), 0.05, 'grey', seg=5)
                k.box((0, h, 0), (0.25, 0.25, 0.25), 'dark')


def text_aspect(text):
    """Width over height of the decal image make_decals draws for this text."""
    try:
        from PIL import ImageFont
        for p in ('/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',
                  '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf'):
            if os.path.exists(p):
                return (int(ImageFont.truetype(p, 200).getlength(text)) + 60) / 280.0
    except Exception:
        pass
    return 0.45 * len(text) + 0.2


def face_decal(name, text, i, xc, tc, hgt):
    """Lettering on hull face i, upright and reading left to right from outside. hgt = height of the decal quad."""
    a, d, _ = FACE[i]
    s = 1 if math.cos(a) > 1e-6 else -1          # which way is up along the face
    L = hgt * text_aspect(text)
    xl, xr = (xc + L / 2, xc - L / 2) if s > 0 else (xc - L / 2, xc + L / 2)
    tb, tt = tc - s * hgt / 2, tc + s * hgt / 2
    M = rotX(a)
    pts = [tuple(M @ Vector((x, d + 0.04, t))) for (x, t) in ((xl, tb), (xr, tb), (xr, tt), (xl, tt))]
    C.flat_decal(name, text, *pts)


# =====================================================================================
# aft: twin torches, shadow shield, thrust frame
# =====================================================================================
def torch(s, drive):
    T = D['tor']
    y = s * T['y']
    with k.xf(move(0, y, 0)):
        # magnetic nozzle: four superconducting coils in cryostats on a strut cage
        coils = [(-129.4, 4.6), (-127.0, 3.77), (-124.6, 3.04), (-122.4, 2.48)]
        for i, (x, r) in enumerate(coils):
            w = 0.5 - i * 0.04
            k.lathe(C.rrect(x - w, x + w, r - w, r + w, 0.15, 0.15, 3), 'steel', seg=56, closed=True, sharp=30)
            k.lathe(C.rrect(x - w * 0.5, x + w * 0.5, r - w - 0.05, r + w + 0.05, 0.08, 0.08, 2), 'gold', seg=56, closed=True, sharp=30)
            for j in range(8):
                with k.xf(on(x, (j + 0.5) * TAU / 8, r + w)):
                    k.box((0, 0.1, 0), (w * 2.3, 0.26, 0.5), 'grey')
        for i in range(8):
            a, a2 = i * TAU / 8, (i + 1) * TAU / 8
            k.tube(P(-129.7, 5.36, a), P(-121.3, 2.9, a), 0.13, 'truss', seg=6)
            for (xa, ra), (xb, rb) in zip(coils, coils[1:]):
                p, q = (a, a2) if i % 2 else (a2, a)
                k.tube(P(xa, ra + 0.62, p), P(xb, rb + 0.58, q), 0.05, 'truss', seg=4)
        for a in (0.9, 3.0, 5.1):          # coil cryogen lines
            k.pipe([P(-129.2, 5.55, a), P(-122.6, 3.6, a), P(-119.4, 3.75, a), P(-105.4, 3.75, a)], 0.1, 'gold', seg=6)
        # throat shroud, plasma dump, water-injection ring for the boost mode
        k.lathe([(-122.3, 1.25), (-121.5, 1.5), (-120.2, 2.1), (-119.0, 2.75)], 'bell', seg=40, sharp=60)
        k.lathe([(-122.25, 1.2), (-121.5, 1.44), (-119.1, 2.6)], 'black', seg=40, sharp=60)
        k.lathe([(-119.15, 2.58), (-119.15, 0)], 'plasma' if drive else 'black', seg=28)
        k.torus(-121.3, 1.95, 0.15, 'silver', seg=32, tseg=6)
        for j in range(8):
            a = j * TAU / 8
            k.tube(P(-121.3, 1.95, a), P(-121.8, 1.45, a), 0.06, 'silver', seg=4)
        k.pipe([P(-121.3, 2.1, PI / 2 * s + PI / 2 + 0.5), P(-120.8, 4.0, PI / 2 * s + PI / 2 + 0.5), P(-103.0, 4.3, PI / 2 * s + PI / 2 + 0.5)], 0.16, 'silver', seg=6)
        # linear D-He3 reactor: vacuum vessel, solenoid coils with bigger mirror coils at the ends
        k.lathe([(-119, 0), (-119, 2.0), (-118.8, 2.2), (-105.2, 2.2), (-105, 2.0), (-105, 0)], 'vessel', seg=40)
        for i in range(6):
            x = -117.6 + i * 2.25
            big = i in (0, 5)
            w, h = (0.72, 0.62) if big else (0.55, 0.42)
            k.lathe(C.rrect(x - w, x + w, 2.25, 2.25 + 2 * h, 0.14, 0.14, 3), 'white_s', seg=48, closed=True, sharp=30)
            k.lathe(C.rrect(x - w - 0.06, x - w + 0.12, 2.2, 2.3 + 2 * h + 0.08, 0.04, 0.04, 1), 'grey', seg=48, closed=True)
            k.lathe(C.rrect(x + w - 0.12, x + w + 0.06, 2.2, 2.3 + 2 * h + 0.08, 0.04, 0.04, 1), 'grey', seg=48, closed=True)
            for j in range(10):
                C.gbox(x, (j + 0.5) * TAU / 10, 2.25 + 2 * h, 0.3, 0.3, 0.14, 'dark')
        cage = 4.15
        st = (-119.3, -114.6, -109.8, -105.0)
        for i in range(6):
            a, a2 = (i + 0.5) * TAU / 6, (i + 1.5) * TAU / 6
            k.tube(P(st[0], cage, a), P(st[-1], cage, a), 0.11, 'truss', seg=6)
            for j, x in enumerate(st):
                k.tube(P(x, cage, a), P(x, cage, a2), 0.07, 'truss', seg=5)
                k.tube(P(x, cage, a), P(x, 2.3, a), 0.06, 'truss', seg=4)
                if j < 3:
                    p, q = (a, a2) if (i + j) % 2 else (a2, a)
                    k.tube(P(x, cage, p), P(st[j + 1], cage, q), 0.04, 'truss', seg=4)
        for i in range(3):          # neutral-beam injectors, angled into the plasma
            a = i * TAU / 3 + 0.5 + (0.0 if s > 0 else PI)
            with k.xf(rotX(a)):
                with k.xf(orient((-112.2, 2.2, 0), (-0.45, 1, 0))):
                    k.can(0, 2.4, 0.42, 'steel', seg=16, ch=0.06)
                    k.can(0.5, 0.75, 0.55, 'grey', seg=16, ch=0.04)
                    k.box((2.95, 0, 0), (1.15, 1.1, 1.0), 'white_s', bevel=0.06)
                    k.box((2.95, 0, 0.56), (0.8, 0.8, 0.14), 'gold')
        for a in (0.2, 1.7, 3.3, 4.6):
            C.axial_pipe(-118.6, -105.4, cage, a, 0.09, R.choice(['silver', 'gold', 'white_s']), clamp=4.6)
        # direct energy converter: collector drum with power conditioning round it
        k.lathe([(-105, 0), (-105, 3.6), (-104.8, 3.8), (-102.2, 3.8)], 'white', seg=40)
        for x in (-104.6, -103.4, -102.5):
            k.lathe(C.rrect(x - 0.14, x + 0.14, 3.76, 4.0, 0.05, 0.05, 1), 'grey', seg=40, closed=True)
        k.lathe(C.rrect(-104.25, -103.75, 3.78, 3.86, 0.02, 0.02, 1), 'blue', seg=40, closed=True)
        for i in range(8):
            a = i * TAU / 8 + 0.2
            C.gbox(-103.0, a, 3.8, 1.0, 1.3, 0.55, 'white_s', bevel=0.06)
            with k.xf(on(-103.0, a, 4.38)):
                k.box((0, 0.05, 0), (0.8, 0.07, 1.1), 'radw')
        # thrust struts through sleeves in the shield to the thrust frame
        for dy in (1, -1):
            for dz in (1, -1):
                k.tube((-115.0, dy * 2.95, dz * 2.95), (-102.05, dy * 5.4, dz * 5.4), 0.32, 'truss', seg=10)
                k.tube((-102.0, dy * 5.4, dz * 5.4), (-98.9, dy * 4.6, dz * 5.0), 0.32, 'truss', seg=8)
                with k.xf(move(-102.25, dy * 5.4, dz * 5.4)):
                    k.can(-0.3, 0.35, 0.75, 'grey', seg=14, ch=0.08)
                k.sphere((-115.0, dy * 2.95, dz * 2.95), 0.45, 'grey', seg=10, rings=6)


def build_aft_end(drive):
    T, S = D['tor'], D['shield']
    for s in (1, -1):
        torch(s, drive)
    # cross tie between the two reactors
    k.tube((-104.0, -T['y'] + 3.8, 0), (-104.0, T['y'] - 3.8, 0), 0.4, 'truss', seg=10)
    for dz in (1, -1):
        k.tube((-116.5, -T['y'] + 3.3, dz * 2.6), (-116.5, T['y'] - 3.3, dz * 2.6), 0.14, 'truss', seg=6)
        k.tube((-116.5, -T['y'] + 3.3, dz * 2.6), (-104.0, T['y'] - 3.8, 0), 0.08, 'truss', seg=5)
    # --- shadow shield: 4 cm of tungsten toward the reactors, 30 cm of lithium hydride in cans behind, on a rib frame
    SH = octp(S['w'], S['h'], S['c'])
    k.extrudeX(SH, -102.0, -101.75, 'tungsten')
    k.extrudeX(octp(S['w'] - 0.5, S['h'] - 0.5, S['c'] - 0.15), -101.75, -101.25, 'white')
    for yy in np.arange(-17.5, 17.6, 5.0):          # retainer rails between the tungsten tiles
        k.box((-102.02, yy, 0), (0.06, 0.14, 2 * (S['h'] / 2 - max(0.0, abs(yy) - (S['w'] / 2 - S['c']))) - 0.6), 'grey')
    for zz in np.arange(-8.66, 8.7, 4.33):
        k.box((-102.02, 0, zz), (0.06, 2 * (S['w'] / 2 - max(0.0, abs(zz) - (S['h'] / 2 - S['c']))) - 0.6, 0.14), 'grey')
    for i in range(8):          # rim channel
        p, q = SH[i], SH[(i + 1) % 8]
        k.tube((-101.6, p[0], p[1]), (-101.6, q[0], q[1]), 0.28, 'grey', seg=6)
        k.sphere((-101.6, p[0], p[1]), 0.36, 'grey', seg=8, rings=5)

    def zext(yy):
        return S['h'] / 2 - max(0.0, abs(yy) - (S['w'] / 2 - S['c']))
    for yy in np.arange(-18, 18.1, 4.0):          # stiffening ribs on the cold face
        k.box((-100.95, yy, 0), (0.6, 0.22, 2 * zext(yy) - 0.5), 'grey')
    for zz in (-9.5, -4.75, 0, 4.75, 9.5):
        w = S['w'] / 2 - max(0.0, abs(zz) - (S['h'] / 2 - S['c']))
        k.box((-101.0, 0, zz), (0.5, 2 * w - 0.5, 0.22), 'grey')
    # frame from the shield ribs to the thrust ring
    RB = octp(20.0, 14.0, 4.0)
    SO = octp(34.0, 21.0, 6.0)
    for i in range(8):
        p, q, q2 = SO[i], RB[i], RB[(i + 1) % 8]
        k.tube((-100.8, p[0], p[1]), (-99.1, q[0], q[1]), 0.2, 'truss', seg=6)
        k.tube((-100.8, p[0], p[1]), (-99.1, q2[0], q2[1]), 0.12, 'truss', seg=5)
        k.tube((-99.1, q[0], q[1]), (-99.1, q2[0], q2[1]), 0.3, 'truss', seg=8)
        k.sphere((-99.1, q[0], q[1]), 0.45, 'grey', seg=8, rings=5)
    # --- thrust frame: a tapered adapter that spreads the thrust into the hull's ring frames; the aft dome of the
    #     round pressure hull shows inside it
    for i in range(8):
        q, h_ = RB[i], OCTI[i]
        k.tube((-99.1, q[0], q[1]), (-96.0, h_[0], h_[1]), 0.3, 'truss', seg=8)
        k.tube((-99.1, q[0], q[1]), (-96.0, OCTI[(i + 1) % 8][0], OCTI[(i + 1) % 8][1]), 0.14, 'truss', seg=5)
    k.lathe([(-98.6, 0), (-98.5, 2.5), (-98.1, 5.0), (-97.3, 7.0), (-96.4, 8.1), (-95.6, 8.4)], 'white', seg=48, sharp=30)
    k.lathe(C.rrect(-96.5, -96.0, 8.3, 8.75, 0.08, 0.08, 1), 'grey', seg=48, closed=True)
    loft(-96.3, -95.9, OCT, OCT, 'grey', cap0=False, cap1=False)
    for i in range(8):
        p, q = OCT[i], OCT[(i + 1) % 8]
        k.tube((-96.1, p[0], p[1]), (-96.1, q[0], q[1]), 0.22, 'grey', seg=6)
        k.tube((-96.1, (p[0] + q[0]) / 2, (p[1] + q[1]) / 2), P(-96.2, 8.6, i * PI / 4), 0.12, 'truss', seg=5)
    # --- fusion fuel cryotanks: deuterium (dorsal) and helium-3 (ventral) dewars in foil
    for s in (1, -1):
        zc = s * (HZ + 2.35)
        k.sphere((-93.0, 0, zc), 2.0, 'gold', seg=28, rings=14)
        with k.xf(move(-93.0, 0, zc)):
            k.torus(0, 2.03, 0.1, 'grey', seg=28, tseg=5)
            with k.xf(rotZ(PI / 2)):
                k.torus(0, 2.03, 0.1, 'grey', seg=28, tseg=5)
        for dx in (-1.3, 1.3):
            for dy in (-1.3, 1.3):
                k.tube((-93 + dx, dy, zc - s * 1.4), (-93 + dx * 1.5, dy * 1.5, s * HZ), 0.1, 'truss', seg=5)
        k.box((-93, 0, s * (HZ + 0.1)), (4.6, 4.6, 0.2), 'grey')
        k.pipe([(-94.6, 0.6, zc - s * 1.0), (-96.6, 0.6, s * 8.9), (-99.0, 2.5, s * 5.6), (-101.3, 4.0, s * 4.0)], 0.1, 'gold', seg=6)
        k.pipe([(-94.6, -0.6, zc - s * 1.0), (-96.6, -0.6, s * 8.9), (-99.0, -2.5, s * 5.6), (-101.3, -4.0, s * 4.0)], 0.1, 'gold', seg=6)


# =====================================================================================
# hull parts used fore and aft
# =====================================================================================
def tank(i):
    """Reaction-mass water tank in a cradle on chamfer i, under an armour shell."""
    TK = D['tanks']
    r = TK['r']
    with k.xf(F(i, 0, 0, r + 0.3)):          # origin on the tank's axis; y out of the hull
        k.dome_tank(TK['x0'], TK['x1'], r, 'tank', k=0.8, seg=36, rings=7)
        xs = [-84.0, -75.5, -67.0, -58.5, -50.0, -45.0]
        for x in xs:
            k.lathe(C.rrect(x - 0.4, x + 0.4, r - 0.02, r + 0.2, 0.05, 0.05, 1), 'grey', seg=36, closed=True)
            k.box((x, -r - 0.05, 0), (0.9, 1.0, 4.4), 'grey', bevel=0.05)           # saddle
            for sz in (1, -1):
                k.tube((x, -r - 0.3, sz * 2.2), (x, -0.8, sz * 3.2), 0.1, 'truss', seg=5)
        for x0, x1 in zip([-88.3] + xs[:-1], xs[:-1] + [-41.6]):          # armour shell, two plates round the outboard side
            for (a0, a1) in ((-1.25, -0.03), (0.03, 1.25)):
                C.arc_plate(x0 + 0.55, x1 - 0.55, r + 0.32, r + 0.44, a0, a1, 'tile', seg=5, vref=r + 0.44)
        for sz in (1, -1):
            C.hrail(-86, -44, r + 0.05, sz * 1.45)
        k.pipe([(-89.6, -0.5, 0), (-92.5, -1.6, 0), (-95.6, -2.6, 0)], 0.2, 'silver', seg=8)
        k.pipe([(-40.4, -0.5, 0), (-38.6, -1.8, 0), (-37.6, -3.2, 0)], 0.16, 'silver', seg=8)
        k.box((-37.6, -3.0, 0), (1.2, 0.5, 1.2), 'grey', bevel=0.05)
        k.box((-64, 0, 0), (0.9, 0.5, 0.9), 'grey')
        with k.xf(move(-62, r + 0.45, 0)):
            k.box((0, 0.2, 0), (1.3, 0.4, 0.9), 'white_s', bevel=0.05)


def panel(xa, xb, ya, yb, va, vb, mat='hot', t=0.06):
    """Radiator panel in the z = 0 plane. UV: u in metres along the ship, v from va at ya to vb at yb."""
    v = [(xa, ya, -t), (xb, ya, -t), (xb, yb, -t), (xa, yb, -t), (xa, ya, t), (xb, ya, t), (xb, yb, t), (xa, yb, t)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)]
    a, b, c, d = (xa, va), (xb, va), (xb, vb), (xa, vb)
    k.add(mat, v, f, [[a, d, c, b], [a, b, c, d], [a, b, b, a], [c, d, d, c], [b, c, c, b], [d, a, a, d]])


def rad_bay(x0, x1, s, hot):
    """Armoured bay along the hull side that the folded strips retract into; doors open."""
    i = P_ if s > 0 else S_
    n = max(1, round((x1 - x0) / 6.0))
    L = (x1 - x0) / n
    with k.xf(F(i)):          # x axial, y out of the hull side, z along the side
        k.box(((x0 + x1) / 2, 1.0, 0), (x1 - x0, 2.0, 5.2), 'grey')
        k.box(((x0 + x1) / 2, 1.95, 0), (x1 - x0 - 0.6, 0.2, 1.5), 'dark')          # the slot
        for j in range(n):
            xm = x0 + (j + 0.5) * L
            for sg in (1, -1):
                k.box((xm, 1.08, sg * 2.72), (L - 0.3, 2.3, 0.12), 'tile')
                k.box((xm, 2.2, sg * 1.78), (L - 0.3, 0.12, 1.78), 'tile')
                with k.xf(move(xm, 2.3, sg * 0.86) @ rotX(-sg * 0.75)):          # door leaf, swung clear
                    k.box((0, 0, sg * 0.42), (L - 0.4, 0.07, 0.84), 'tile')
                    k.box((0, -0.06, sg * 0.42), (L - 0.8, 0.06, 0.12), 'grey')
                with k.xf(move(xm, 2.3, sg * 0.86)):
                    k.can(-L / 2 + 0.4, L / 2 - 0.4, 0.09, 'grey', seg=6, ch=0.02)
            k.box((x0 + j * L, 1.1, 0), (0.3, 2.4, 5.5), 'grey')
        k.box((x1, 1.1, 0), (0.3, 2.4, 5.5), 'grey')
        for xe in (x0 - 0.3, x1 + 0.3):
            k.box((xe, 1.1, 0), (0.5, 2.3, 5.4), 'tile', bevel=0.08)
        for sg in (1, -1):          # supply and return headers along the root
            k.tube((x0 + 0.5, 2.5, sg * 0.4), (x1 - 0.5, 2.5, sg * 0.4), 0.2, 'hotpipe' if hot else 'silver', seg=8)


def radiator(Rd, hot, s):
    """Retractable radiator wing: folding strips in the plane of the two reactors, on hinge booms with stays."""
    x0, x1, root, n, ws = Rd['x0'], Rd['x1'], Rd['root'], Rd['n'], Rd['ws']
    Lr = x1 - x0
    step = Lr / (n + 0.5) if hot else 0.0          # each hot strip starts further forward, inside the shield's shadow
    rad_bay(x0 - 1, x1 + 1, s, hot)
    frame = 'cc' if hot else 'grey'
    for q in range(n):
        xs = x0 + q * step
        yi, yo = root + q * ws + 0.5, root + (q + 1) * ws - 0.12
        if q:
            yi = root + q * ws + 0.12
        npan = max(1, round((x1 - xs) / (5.45 if hot else 6.0)))
        for j in range(npan):
            xa = xs + (x1 - xs) * j / npan + 0.12
            xb = xs + (x1 - xs) * (j + 1) / npan - 0.12
            if hot:
                panel(xa, xb, s * yi, s * yo, q / n, (q + 1) / n)
            else:
                k.box(((xa + xb) / 2, s * (yi + yo) / 2, 0), (xb - xa, yo - yi, 0.1), 'radw')
            k.box((xa - 0.12, s * (yi + yo) / 2, 0), (0.14, yo - yi + 0.2, 0.34), frame)
        k.box((x1, s * (yi + yo) / 2, 0), (0.14, yo - yi + 0.2, 0.34), frame)
        # hinge boom along the inboard edge of the strip, with the knuckles of the fold
        yh = s * (root + q * ws + (0.25 if q == 0 else 0.0))
        k.tube((xs - 0.3, yh, 0), (x1 + 0.3, yh, 0), 0.16, frame, seg=6)
        for xx in np.arange(xs + 1.0, x1, 5.45 if hot else 6.0):
            with k.xf(move(xx, yh, 0)):
                k.can(-0.35, 0.35, 0.26, 'grey' if not hot else 'dark', seg=8, ch=0.04)
    yt = s * (root + n * ws)
    k.tube((x0 + (n - 1) * step - 0.3, yt, 0), (x1 + 0.3, yt, 0), 0.13, frame, seg=6)
    # feed and return across the forward ends of the strips
    for dz in (0.22, -0.22):
        k.tube((x1 + 0.55, s * root, dz), (x1 + 0.55, yt, dz), 0.17, 'hotpipe' if hot else 'silver', seg=6)
    # king posts on the bay and stay rods keep the strips flat
    xst = [x1 - 3.0 - j * (Lr - 6.0) / 5 for j in range(6)]
    for xx in xst:
        reach = n if not hot else min(n, int((xx - x0) / step) + 1)
        for sg in (1, -1):
            top = (xx, s * (HY + 1.4), sg * 6.6)
            k.tube((xx - 0.8, s * (HY + 1.2), sg * 2.8), top, 0.1, 'truss', seg=5)
            k.tube((xx + 0.8, s * (HY + 1.2), sg * 2.8), top, 0.1, 'truss', seg=5)
            k.tube((xx, s * HY, sg * 4.1), top, 0.07, 'truss', seg=4)
            for m in range(1, reach + 1):
                k.tube(top, (xx, s * (root + m * ws), sg * 0.12), 0.035, frame, seg=4)


def pd_turret():
    """Twin rapid-fire point-defence mount at the local origin, +y out of the hull, barrels toward +x."""
    C.ycan(0, 0.8, 1.75, 'grey', seg=24, ch=0.15)
    C.ycan(0.8, 1.0, 1.45, 'dark', seg=24, ch=0.04)
    with k.xf(orient((0, 1.0, 0), (0, 1, 0))):          # gunhouse: a short drum with a domed top
        k.lathe([(0, 1.3), (1.0, 1.3), (1.5, 1.05), (1.75, 0.6), (1.8, 0)], 'tile', seg=24, sharp=40)
    k.box((1.0, 1.85, 0), (1.3, 1.0, 1.7), 'grey', bevel=0.1)          # mantlet
    for sz in (0.45, -0.45):
        with k.xf(orient((1.4, 1.9, sz), (1, 0.07, 0))):
            k.can(0, 1.5, 0.2, 'dark', seg=10, ch=0.04)
            k.tube((1.5, 0, 0), (4.0, 0, 0), 0.085, 'steel', seg=8)
            k.can(3.9, 4.3, 0.14, 'dark', seg=8, ch=0.03)
        k.box((-0.2, 1.7, sz * 3.3), (1.5, 1.1, 0.7), 'white_s', bevel=0.06)          # ammunition drums
    with k.xf(orient((-0.5, 2.8, 0), (0.6, 1, 0))):          # tracking radar
        k.tube((-0.6, 0, 0), (0, 0, 0), 0.08, 'grey', seg=5)
        k.lathe([(0, 0.05), (0.12, 0.35), (0.3, 0.55)], 'white_s', seg=14, sharp=70)
    k.box((0.75, 2.75, 0), (0.5, 0.35, 0.5), 'glass_dark')


def coilgun():
    """Coilgun turret at the local origin: x toward the bow, y out of the hull, z along the face."""
    G = D['gun']
    C.ycan(0, 1.6, 5.2, 'grey', seg=48, ch=0.25)          # barbette
    C.ycan(1.6, 2.0, 4.6, 'dark', seg=48, ch=0.05)
    for j in range(24):
        with k.xf(rotY(j * TAU / 24)):
            k.box((5.2, 0.5, 0), (0.25, 1.0, 0.5), 'grey')
    # gunhouse: faceted armour over the breech, capacitor switchgear and the loader (not pressurized)
    a0 = [(p[0] + 3.8, p[1]) for p in octp(3.6, 9.0, 1.3)]
    a1 = [(p[0] + 3.8, p[1]) for p in octp(3.0, 7.4, 1.1)]
    loft(-6.0, 5.0, a0, a1, 'tile')
    for x in (-5.6, -2.0, 1.6, 4.7):
        f = (x + 6.0) / 11.0
        sec = [(p[0] * 1.012 + 3.8 * (1 - 1.012), p[1] * 1.012) for p in lerp_sec(a0, a1, f)]
        loft(x - 0.15, x + 0.15, sec, sec, 'grey', cap0=False, cap1=False)
    k.box((-1.5, 5.75, 1.8), (2.2, 0.35, 1.6), 'grey', bevel=0.08)          # roof hatch and sight hood
    k.box((2.4, 5.9, -2.4), (2.4, 0.7, 1.2), 'grey', bevel=0.1)
    k.box((3.65, 5.95, -2.4), (0.1, 0.4, 0.8), 'glass_dark')
    with k.xf(orient((0.6, 4.4, -5.3), (0, 0, 1))):          # rangefinder arms
        k.can(0, 10.6, 0.3, 'grey', seg=12, ch=0.06)
        k.can(-0.02, 0.5, 0.42, 'dark', seg=12, ch=0.05)
        k.can(10.1, 10.62, 0.42, 'dark', seg=12, ch=0.05)
    for sz in (1, -1):
        k.tube((-5.0, 5.75, sz * 3.2), (3.8, 5.45, sz * 2.6), 0.045, 'yellow', seg=5)
    # mantlet and trunnions
    k.box((5.6, 3.8, 0), (1.6, 2.7, 3.4), 'grey', bevel=0.2)
    with k.xf(orient((5.6, 3.8, -2.1), (0, 0, 1))):
        k.can(0, 4.2, 0.9, 'dark', seg=16, ch=0.1)
    # barrel: bore tube, drive coils in a stringer cage, bus bars on top
    with k.xf(orient((6.2, 3.8, 0), (1, 0.04, 0))):
        k.tube((0, 0, 0), (G['L'], 0, 0), 0.36, 'steel', seg=16)
        ncoil = 15 if k.lod >= 1 else 8
        for j in range(ncoil):
            xx = 1.3 + j * (G['L'] - 3.0) / (ncoil - 1)
            k.can(xx - 0.45, xx + 0.45, 0.78, 'bell', seg=20, ch=0.08)
            k.can(xx - 0.57, xx - 0.45, 0.9, 'grey', seg=20, ch=0.03)
            k.can(xx + 0.45, xx + 0.57, 0.9, 'grey', seg=20, ch=0.03)
            k.box((xx, 0.95, 0), (0.5, 0.3, 0.6), 'dark')
        for a in (PI / 4, 3 * PI / 4, 5 * PI / 4, 7 * PI / 4):
            k.tube(P(0.2, 0.98, a), P(G['L'] - 0.6, 0.98, a), 0.07, 'truss', seg=5)
        k.box((G['L'] / 2 - 0.4, 1.16, 0), (G['L'] - 1.6, 0.2, 0.42), 'gold')
        k.tube((0.3, -1.0, 0.25), (G['L'] - 1.2, -1.0, 0.25), 0.07, 'silver', seg=5)
        k.can(G['L'] - 0.95, G['L'], 1.05, 'grey', seg=20, ch=0.1)
        k.can(G['L'], G['L'] + 0.15, 0.7, 'dark', seg=16, ch=0.04)
        k.lathe([(G['L'] + 0.16, 0.36), (G['L'] + 0.1, 0)], 'black', seg=12)
    # recoil absorbers behind the gunhouse
    for sz in (1.4, -1.4):
        k.can(-9.2, -5.6, 0.55, 'steel', seg=16, ch=0.08, oy=3.4, oz=sz)
        k.can(-10.4, -9.2, 0.22, 'grey', seg=10, ch=0.03, oy=3.4, oz=sz)
        k.box((-10.8, 1.75, sz), (1.0, 3.5, 1.3), 'grey', bevel=0.1)
        k.box((-7.4, 2.9, sz), (0.5, 0.5, 1.3), 'dark')
    # capacitor banks in armoured boxes either side
    for sz in (3.5, -3.5):
        k.box((-11.0, 1.3, sz), (8.0, 2.6, 3.4), 'tile', bevel=0.12)
        for x in (-14.6, -11.0, -7.4):
            k.box((x, 1.33, sz), (0.3, 2.7, 3.52), 'grey')
        k.box((-11.0, 2.66, sz), (6.6, 0.08, 2.6), 'radw')
        k.pipe([(-6.9, 0.5, sz * 0.8), (-5.0, 0.5, sz * 0.72), (-4.0, 0.9, sz * 0.5)], 0.25, 'gold', seg=8)
        k.box((-15.3, 0.8, sz), (0.5, 1.2, 1.6), 'white_s', bevel=0.05)


def rcs_aft():
    """Three-nozzle cluster (out and both ways along the face) plus a braking nozzle forward; nothing fires aft at the shield."""
    k.box((0, 1.0, 0), (2.2, 2.0, 2.2), 'white_s', bevel=0.12)
    k.box((0, 2.02, 0), (1.5, 0.1, 1.5), 'gold')
    for d in ((0, 1, 0), (0, 0, 1), (0, 0, -1), (1, 0, 0)):
        o = (d[0] * 1.08, 1.0 + d[1] * 1.0, d[2] * 1.08)
        with k.xf(orient(o, d)):
            k.lathe([(0, 0.22), (0.2, 0.28), (0.5, 0.44), (0.9, 0.62), (1.25, 0.74)], 'bell', seg=14, sharp=60)
            k.lathe([(0.0, 0.21), (0.05, 0)], 'black', seg=8)


# =====================================================================================
# aft hull: armour, tanks, hot radiators, boat bay, missile racks
# =====================================================================================
def build_aft_hull():
    x0, x1 = D['aft']
    loft(x0, x1, OCTI, OCTI, 'grey', cap0=False, cap1=True)
    plates(x0, x1, OCT, OCT, 12, [2, 2, 3, 2, 2, 2, 3, 2], armour_mat(), posts=0.6)
    for i in CHAMF:
        tank(i)
        with k.xf(F(i, D['rcs']['aft'], 0)):
            rcs_aft()
    for s in (1, -1):
        radiator(D['htr'], True, s)
    build_bay()
    build_racks()
    # point defence, dorsal and ventral
    for i in (T_, B_):
        with k.xf(F(i, D['pd']['aft'], 0)):
            pd_turret()
    # lithium heat sink: fill and vent fittings on both sides, above the radiator bays
    for i in (P_, S_):
        for x in (-50, -44):
            with k.xf(F(i, x, 3.5)):
                k.box((0, 0.2, 0), (2.4, 0.4, 1.0), 'grey', bevel=0.05)
                C.ycan(0.2, 0.7, 0.3, 'steel', seg=10)
        fpipe(i, D['htr']['x0'] - 1, x1 - 1, 3.55, 0.2, 'gold')
        fpipe(i, D['htr']['x0'] - 1, x1 - 1, -3.55, 0.2, 'white_s')
    for i in CHAMF[:2]:          # a few small viewports through the armour (wardroom and sick bay)
        for x in (-33.5, -31.0, -28.6):
            port(i, x, 1.2 * (1 if i == PT else -1), R.random() < 0.7)
    # detail pass: equipment, lockers, hatches, rails
    fgreeble(T_, -54, -37, 18)
    fgreeble(T_, -95, -88, 6, span=0.5)
    fgreeble(B_, -95, -87, 8)
    fgreeble(B_, -54, -37, 16)
    for i, x, t in ((T_, -46, 4.4), (T_, -40, -4.2), (B_, -48, 0), (T_, -51, -5.8)):
        locker(i, x, t)
    for i, x, t in ((T_, -50, 2.5), (B_, -52, 3), (T_, -29.5, -4.0)):
        fhatch(i, x, t)
    for t in (-6.4, 6.4):
        frail(T_, -54, -28, t)
        frail(B_, -54, -28, t)
    frail(T_, -54, -38, 0)


def build_bay():
    """Dorsal boat bay: armoured walls, clamshell doors standing open, the rescue and boarding boat on its cradle."""
    B = D['bay']
    x0, x1, hw, h = B['x0'], B['x1'], B['w'] / 2, B['h']
    z0 = HZ
    k.box(((x0 + x1) / 2, 0, z0 + 0.15), (x1 - x0, B['w'], 0.3), 'grey')
    n = 5
    L = (x1 - x0) / n
    for s in (1, -1):
        k.box(((x0 + x1) / 2, s * (hw + 0.3), z0 + h / 2), (x1 - x0, 0.36, h), 'grey')
        for j in range(n):
            xm = x0 + (j + 0.5) * L
            k.box((xm, s * (hw + 0.62), z0 + h / 2 + 0.1), (L - 0.3, 0.12, h - 0.4), 'tile')
            k.box((x0 + j * L, s * (hw + 0.05), z0 + h / 2), (0.25, 0.3, h - 0.2), 'grey')
            k.box((xm, s * (hw + 0.1), z0 + 3.0), (L, 0.12, 0.2), 'grey')
        k.box(((x0 + x1) / 2, s * (hw + 0.3), z0 + h + 0.06), (x1 - x0, 0.5, 0.12), 'hazard')
        # door leaf, hinged on the wall top, opened 110 degrees: it stands up and shields the boat from fragments
        with k.xf(move(0, s * (hw + 0.3), z0 + h + 0.2) @ rotX(-s * 1.92)):
            for j in range(n):
                xm = x0 + (j + 0.5) * L
                k.box((xm, -s * hw / 2, 0.1), (L - 0.3, hw - 0.1, 0.12), 'tile')
                k.box((xm, -s * hw / 2, -0.05), (L - 0.5, hw - 0.4, 0.2), 'grey')
                for yy in (0.25, 0.75):
                    k.box((xm, -s * hw * yy, -0.25), (L - 0.5, 0.2, 0.3), 'grey')
                k.box((x0 + j * L + 0.2, -s * hw / 2, -0.25), (0.25, hw - 0.4, 0.3), 'grey')
            k.box(((x0 + x1) / 2, -s * (hw - 0.15), 0.18), (x1 - x0 - 0.6, 0.3, 0.06), 'hazard')
        for j in range(n + 1):          # hinges and door rams
            x = x0 + j * L * 0.98 + 0.3
            with k.xf(move(x, s * (hw + 0.3), z0 + h + 0.2)):
                k.can(-0.3, 0.3, 0.26, 'dark', seg=8, ch=0.04)
            dv = rotX(-s * 1.92) @ Vector((0, -s * 2.6, -0.3))
            k.tube((x, s * (hw + 0.9), z0 + h - 1.6), (x, s * (hw + 0.3) + dv.y, z0 + h + 0.2 + dv.z), 0.11, 'steel', seg=6)
    for xe, sg in ((x0 - 0.3, -1), (x1 + 0.3, 1)):
        k.box((xe, 0, z0 + h / 2), (0.36, B['w'] + 0.9, h), 'grey')
        for yy in (-4.2, 0, 4.2):
            k.box((xe + sg * 0.26, yy, z0 + h / 2 + 0.1), (0.12, 4.0, h - 0.4), 'tile')
        k.box((xe, 0, z0 + h + 0.06), (0.5, B['w'] + 0.9, 0.12), 'hazard')
    # floor: cradle rails, tie-downs, markings
    for yy in (-1.3, 1.3):
        k.box((-71, yy, z0 + 0.55), (20, 0.4, 0.5), 'grey', bevel=0.05)
    for x in (-78, -72, -66):
        k.box((x, 0, z0 + 0.95), (0.8, 4.6, 0.5), 'grey', bevel=0.05)
        for yy in (-2.5, 2.5):
            k.box((x, yy, z0 + 1.7), (0.7, 0.4, 1.9), 'hazard', bevel=0.05)
    for x in (x0 + 1.2, x1 - 1.2):
        k.box((x, 0, z0 + 0.32), (0.5, B['w'] - 1.0, 0.04), 'hazard')
    for s in (1, -1):
        for x in (-82, -60):
            k.box((x, s * 4.6, z0 + 0.9), (2.0, 1.4, 1.2), R.choice(['white_s', 'orange', 'grey']), bevel=0.06)
        C.floodlight((-58.5, s * 5.4, z0 + h - 0.5), (-68, 0, z0 + 2.5), energy=5200, spot=1.25)
        C.floodlight((-83.5, s * 5.4, z0 + h - 0.5), (-75, 0, z0 + 2.5), energy=4200, spot=1.25)
    # handling arm
    pts = [(x1 - 2.2, 4.4, z0 + 0.4), (x1 - 3.2, 3.6, z0 + 5.4), (x1 - 7.5, 1.9, z0 + 6.2), (x1 - 9.0, 0.6, z0 + 5.0)]
    k.box((pts[0][0], pts[0][1], z0 + 0.5), (1.8, 1.8, 0.6), 'grey', bevel=0.08)
    rad = [0.34, 0.27, 0.2]
    for j in range(3):
        k.tube(pts[j], pts[j + 1], rad[j], 'white_s', seg=12)
        ax = (Vector(pts[j + 1]) - Vector(pts[j])).cross(Vector((0, 0, 1))).normalized()
        with k.xf(orient(pts[j], ax)):
            k.can(-0.5, 0.5, 0.5 - j * 0.07, 'grey', seg=14, ch=0.06)
    with k.xf(orient(pts[3], Vector(pts[3]) - Vector(pts[2]))):
        k.can(0, 0.7, 0.3, 'dark', seg=12)
    build_boat(z0 + 0.3 + 1.0 + 1.8)


def build_boat(bz):
    """18 m rescue and boarding boat: 20 seats, medical bay, boarding collar at the nose."""
    with k.xf(move(0, 0, bz)):
        k.lathe([(-79.2, 0), (-79.2, 1.45), (-78.7, 1.8), (-67.6, 1.8), (-65.6, 1.55), (-63.6, 1.12), (-62.7, 0.98), (-62.7, 0)], 'white', seg=40, sharp=30)
        for xa, xb in ((-74.6, -73.2), (-72.8, -72.4)):          # rescue stripes
            k.lathe(C.rrect(xa, xb, 1.78, 1.83, 0.02, 0.02, 1), 'orange', seg=40, closed=True)
        for x in (-77.6, -70.0, -67.6):
            k.lathe(C.rrect(x - 0.08, x + 0.08, 1.78, 1.86, 0.02, 0.02, 1), 'grey', seg=40, closed=True)
        k.can(-62.7, -62.1, 0.95, 'grey', seg=20, ch=0.06)          # boarding collar
        k.can(-62.1, -61.95, 0.75, 'dark', seg=20, ch=0.03)
        for j in range(3):
            with k.xf(rotX(j * TAU / 3 + 0.5)):
                k.box((-62.2, 0.95, 0), (0.5, 0.1, 0.45), 'grey')
        for a in (PI / 2 - 0.5, PI / 2 - 0.17, PI / 2 + 0.17, PI / 2 + 0.5):          # flight deck windows
            with k.xf(on(-66.4, a, 1.63) @ rotZ(-0.125)):
                k.box((0, 0.02, 0), (1.3, 0.08, 0.52), 'grey')
                k.box((0, 0.05, 0), (1.05, 0.08, 0.38), 'glass_dark')
        for s in (0.0, PI):
            for x in (-76.0, -74.0, -71.2, -69.4):
                with k.xf(on(x, s, 1.8)):
                    k.box((0, 0.02, 0), (0.6, 0.06, 0.6), 'grey')
                    k.box((0, 0.04, 0), (0.42, 0.06, 0.42), 'glass' if R.random() < 0.5 else 'glass_dark')
            C.hatch(-68.6, s + (0.35 if s == 0 else -0.35), 1.8, 0.55)
        for s in (1, -1):
            k.dome_tank(-77.6, -69.0, 0.62, 'white_s', k=0.8, seg=16, rings=4, oy=s * 2.1, oz=-0.5)
            k.lathe(C.rrect(-73.6, -73.0, 0.6, 0.66, 0.02, 0.02, 1), 'gold', seg=16, closed=True, oy=s * 2.1, oz=-0.5)
            for x in (-76.2, -70.4):
                k.box((x, s * 1.75, -0.45), (0.5, 0.9, 0.4), 'grey')
            with k.xf(move(0, s * 0.85, 0)):          # main engines
                k.lathe([(-79.2, 0.3), (-79.7, 0.36), (-80.3, 0.56)], 'bell', seg=14, sharp=60)
                k.lathe([(-79.25, 0.28), (-79.2, 0)], 'black', seg=10)
            with k.xf(move(-64.6, s * 1.45, 0.2) @ Matrix.Scale(0.3, 4) @ rotZ(-s * PI / 2)):
                C.rcs_quad()
            with k.xf(move(-78.0, s * 1.75, 0.6) @ Matrix.Scale(0.3, 4) @ rotZ(-s * PI / 2)):
                C.rcs_quad()
        k.box((-75.8, 0, 1.84), (3.6, 1.5, 0.06), 'radw')
        with k.xf(on(-70.5, PI / 2, 1.8)):
            k.tube((0, 0, 0), (0, 0.9, 0), 0.04, 'grey', seg=4)
            k.box((0, 0.95, 0), (0.5, 0.08, 0.5), 'white_s')
        k.box((-71.0, 0, -1.92), (9.0, 1.6, 0.3), 'grey')          # keel fitting on the cradle


def build_racks():
    """Ventral missile racks (two by sixteen ready cells), magazine hatch and reloader rail."""
    CL = D['cells']
    x0, x1 = CL['x0'], CL['x1']
    with k.xf(F(B_)):          # y is down out of the hull; z runs to port
        for s in (1, -1):
            zc = s * 3.6
            k.box(((x0 + x1) / 2, 1.15, zc), (x1 - x0, 2.3, 6.0), 'grey', bevel=0.1)
            for j in range(4):
                xm = x0 + (j + 0.5) * (x1 - x0) / 4
                k.box((xm, 1.2, zc + s * 3.06), ((x1 - x0) / 4 - 0.25, 2.2, 0.12), 'tile')
            for xe in (x0 - 0.06, x1 + 0.06):
                k.box((xe, 1.2, zc), (0.12, 2.2, 5.8), 'tile')
            for r in (1.35, -1.35):
                for j in range(8):
                    cx = x0 + 1.6 + j * (x1 - x0 - 3.2) / 7
                    m = 'white_s' if R.random() > 0.1 else 'tile2'
                    k.box((cx, 2.36, zc + r), (2.1, 0.14, 2.1), m, bevel=0.03)
                    k.box((cx - 0.95, 2.44, zc + r), (0.2, 0.1, 1.6), 'grey')          # hinge
                    k.box((cx + 0.7, 2.45, zc + r), (0.3, 0.05, 0.3), 'hazard')
            k.box(((x0 + x1) / 2, 2.34, zc), (x1 - x0 - 0.4, 0.1, 0.35), 'dark')
        # magazine hatch and reloader
        k.box((x1 + 3.0, 0.28, 0), (4.4, 0.56, 4.6), 'grey', bevel=0.08)
        k.box((x1 + 3.0, 0.6, 0), (3.6, 0.08, 3.8), 'white_s')
        for zz in (-1.75, 1.75):
            k.box((x1 + 3.0, 0.64, zz), (3.6, 0.06, 0.3), 'hazard')
        for zz in (0.5, -0.5):
            k.box(((x0 + x1) / 2 + 2, 0.2, zz), (x1 - x0 + 6, 0.4, 0.22), 'grey')
        k.box((x1 - 6.0, 0.9, 0), (2.6, 1.0, 1.5), 'yellow', bevel=0.06)          # loader cart
        k.box((x1 - 6.0, 1.6, 0), (1.2, 0.5, 0.5), 'dark')


# =====================================================================================
# mid: collars, drum
# =====================================================================================
def build_mid():
    Cc = D['collar']
    rC = Cc['r']
    # despun core citadel inside the drum, with the bearing shafts
    k.lathe([(-17.0, D['core']), (17.0, D['core'])], 'dark', seg=32)
    for s in (-1, 1):
        xh, xo, xi = s * 26.0, s * Cc['x1'], s * Cc['x0']
        # end cone between the hull and the collar: backing structure, plates, gussets
        k.lathe([(xh, 13.0), (xo, rC - 1.35), (xo, rC - 0.3), (xi, rC - 0.3), (xi, 19.0), (xi - s * 0.3, 19.0)], 'grey', seg=96, sharp=30)
        dd = Vector((xo - xh, rC - 1.35 - 13.0)).normalized()
        nv = Vector((dd.y, -dd.x))
        nv = nv if nv.x * s > 0 else -nv          # outward: toward the hull end of the ship
        for r in range(2):
            Lc = math.hypot(xo - xh, rC - 1.35 - 13.0)
            s0, s1 = Lc * r / 2 + 0.12, Lc * (r + 1) / 2 - 0.12
            nseg = 16 if r == 0 else 24
            for j in range(nseg):
                rm = 13.0 + dd.y * (s0 + s1) / 2
                da = 0.1 / rm
                prof = [(xh + dd.x * sv + nv.x * o, 13.0 + dd.y * sv + nv.y * o) for (sv, o) in ((s0, 0.16), (s1, 0.16), (s1, 0.28), (s0, 0.28))]
                k.lathe(prof, 'tile' if R.random() > 0.06 else 'tile2', seg=3 if k.lod >= 1 else 1, a0=j * TAU / nseg + da, a1=(j + 1) * TAU / nseg - da,
                        closed=True, caps=True, sharp=30)
        for j in range(16):          # ribs between the plates carry the collar on the hull's end frame
            with k.xf(rotX(j * TAU / 16)):
                k.tube((xh + nv.x * 0.3, 13.0 + nv.y * 0.3, 0), (xo + nv.x * 0.3, rC - 1.35 + nv.y * 0.3, 0), 0.13, 'grey', seg=6)
        k.lathe(C.rrect(xo - 0.25, xo + 0.25, rC - 1.5, rC - 0.2, 0.06, 0.06, 1), 'grey', seg=96, closed=True)
        # collar rim: spaced plates, edge bands
        nt = 30
        for j in range(nt):
            a0, a1 = (j + 0.035) * TAU / nt, (j + 0.965) * TAU / nt
            for (xa, xb) in ((Cc['x0'] + 0.95, Cc['x0'] + 3.45), (Cc['x0'] + 3.6, Cc['x1'] - 0.95)):
                m = 'tile' if R.random() > 0.06 else 'tile2'
                C.arc_plate(min(s * xa, s * xb), max(s * xa, s * xb), rC - 0.12, rC, a0, a1, m, seg=3, vref=rC)
        for xe in (Cc['x0'] + 0.45, Cc['x1'] - 0.45):
            k.lathe(C.rrect(s * xe - 0.4, s * xe + 0.4, rC - 0.25, rC + 0.15, 0.06, 0.06, 1), 'grey', seg=96, closed=True)
        k.lathe(C.rrect(s * (Cc['x0'] + 0.1) - 0.12, s * (Cc['x0'] + 0.1) + 0.12, rC - 0.85, rC - 0.3, 0.04, 0.04, 1), 'hazard', seg=96, closed=True)
        # spin motor-generators: four housings on each collar, cooling panels on top
        xm = s * (Cc['x0'] + Cc['x1']) / 2
        for q in range(4):
            a = q * TAU / 4 + TAU / 8
            with k.xf(on(xm, a, rC)):
                k.box((0, 0.12, 0), (4.8, 0.24, 4.6), 'grey', bevel=0.05)
                k.box((0, 0.8, 0), (4.2, 1.2, 4.0), 'white_s', bevel=0.18)
                k.box((0, 1.44, 0), (3.2, 0.08, 3.0), 'radw')
                for zz in (-2.15, 2.15):
                    k.box((0, 0.7, zz), (2.6, 0.9, 0.3), 'grey')
                k.pipe([(-1.6, 0.4, 2.2), (-1.6, 0.25, 3.4), (-2.6, 0.2, 4.2)], 0.14, 'gold', seg=6)
                k.pipe([(1.6, 0.4, -2.2), (1.6, 0.25, -3.4), (2.6, 0.2, -4.2)], 0.14, 'silver', seg=6)
        C.greeble(min(s * 18.3, s * 22.7), max(s * 18.3, s * 22.7), rC, 20, hmax=0.45)
        for a in (0.25, 2.0, 3.6, 5.3):
            C.hrail(min(s * 18.2, s * 22.8), max(s * 18.2, s * 22.8), rC, a + (0.3 if s > 0 else 0))
        C.hatch(xm, 2.9 if s > 0 else 0.5, rC, 0.8)
    # lettering on the fixed collars
    asp = text_aspect(REG + '   UNITED EARTH PATROL')
    for c in (PI * 0.75, PI * 1.75 - 0.2, PI * 0.25 - 0.1):
        da = 2.6 * asp / rC / 2
        C.arc_decal('reg', REG + '   UNITED EARTH PATROL', 19.2, 21.8, rC + 0.03, c - da, c + da, seg=30)
    # high-gain antenna on the forward collar; whip antennas on the aft collar
    xc = (Cc['x0'] + Cc['x1']) / 2
    C.girder((xc, 0, rC - 0.1), (xc, 0, rC + 4.4), 1.1, bay=1.1, rc=0.08, rd=0.045)
    k.box((xc, 0, rC + 4.8), (1.5, 1.5, 0.8), 'grey', bevel=0.08)
    with k.xf(orient((xc, 0, rC + 5.5), (0.35, -0.3, 0.89))):
        k.can(-0.5, 0.15, 0.7, 'gold', seg=14)
        with k.xf(move(0.8, 0, 0) @ Matrix.Scale(0.85, 4)):
            C.dish()
    for a in (0.9, 2.4):
        with k.xf(on(-xc, a, rC)):
            k.tube((0, 0, 0), (0, 5.2, 0), 0.06, 'grey', seg=5)
            k.box((0, 0.3, 0), (0.6, 0.6, 0.6), 'grey')
    for a in (1.3, 4.5):
        with k.xf(on(-xc + 1.0, a, rC)):
            k.tube((0, 0, 0), (0, 1.6, 0), 0.06, 'grey', seg=5)
            with k.xf(orient((0, 1.6, 0), (0, 1, 0))):
                k.lathe([(0, 0.05), (0.7, 0.4)], 'white_s', seg=12)
    # navigation lights on the collar
    for s in (1, -1):
        with k.xf(on(xc, PI / 2 - s * PI / 2, rC)):
            k.box((0, 0.3, 0), (0.6, 0.6, 0.6), 'navR' if s > 0 else 'navG')
    build_drum()


def build_drum():
    """The armoured drum: one pressure hull with rounded ends, two decks, under spaced rim plates. It turns."""
    Dr = D['drum']
    rO, w = Dr['rOut'], Dr['w']
    C.spin_group('RingDrum', 0.0, Dr['g'], Dr['r'], Dr['dir'])
    xw = w / 2
    k.lathe(C.rrect(-xw + 0.05, xw - 0.05, Dr['rIn'], rO - 0.5, 2.5, 2.2, 6), 'white_m', seg=144, closed=True, sharp=25)
    # bearing and seal rings at both ends, hazard band on the moving edge
    for s in (1, -1):
        k.lathe(C.rrect(s * (xw + 0.2) - 0.25, s * (xw + 0.2) + 0.25, 17.4, 18.6, 0.06, 0.06, 1), 'dark', seg=96, closed=True)
        k.lathe(C.rrect(s * (xw - 0.45) - 0.3, s * (xw - 0.45) + 0.3, rO - 0.14, rO + 0.04, 0.03, 0.03, 1), 'hazard', seg=144, closed=True)
    # rim plates over the standoff gap
    ncol = 48 if k.lod >= 1 else 24
    nrow = 10
    x0, x1 = -xw + 0.85, xw - 0.85
    rw = (x1 - x0) / nrow
    for i in range(ncol):
        a0, a1 = (i + 0.03) * TAU / ncol, (i + 0.97) * TAU / ncol
        for j in range(nrow):
            if R.random() < 0.008:
                continue
            mat = 'tile' if R.random() > 0.05 else 'tile2'
            am = ((a0 + a1) / 2) % (TAU / 3)
            if j in (4, 5) and 1.66 < am < 2.0:
                mat = 'blue'          # livery band after the name
            xa = x0 + j * rw + 0.06
            C.arc_plate(xa, xa + rw - 0.12, rO - 0.12, rO, a0, a1, mat, seg=3, vref=rO)
    for j in range(nrow + 1):          # hoop frames the plates bolt to
        xx = x0 + j * rw
        big = j in (3, 7)
        k.lathe(C.rrect(xx - (0.28 if big else 0.1), xx + (0.28 if big else 0.1), rO - 0.42, rO + (0.14 if big else -0.16), 0.03, 0.03, 1), 'grey', seg=144, closed=True)
    # lettering, three times round
    asp = text_aspect(NAME)
    hgt = 7.6
    for c in (0.0, TAU / 3, 2 * TAU / 3):
        da = hgt * asp / rO / 2
        C.arc_decal('name', NAME, -hgt / 2, hgt / 2, rO + 0.03, c + 0.9 - da, c + 0.9 + da, seg=40)
    # a few small viewports through the armour, hatches, rails, low equipment
    for (x, a, lit) in ((-11.4, 0.3, 1), (-11.4, 0.45, 0), (9.6, 1.5, 1), (12.9, 2.6, 1), (-8.2, 3.3, 0), (-11.4, 4.2, 1), (9.6, 5.1, 0),
                        (12.9, 5.75, 1), (-14.6, 2.1, 1), (6.4, 3.9, 1)):
        with k.xf(on(x, a, rO) @ orient((0, 0, 0), (0, 1, 0))):
            k.lathe([(0, 0.62), (0.14, 0.62), (0.14, 0.42), (0.06, 0.42)], 'grey', seg=14, sharp=30)
            k.lathe([(0.07, 0.42), (0.07, 0)], 'glass' if lit else 'glass_dark', seg=12)
    for (x, a) in ((-13.0, 1.2), (13.0, 3.5), (-13.0, 5.4)):
        C.hatch(x, a, rO, 0.8)
        C.hrail(x - 2.6, x + 2.6, rO, a + 0.07)
    for a in (0.6, 2.7, 4.8):
        C.hrail(6.6, 15.0, rO, a)
        C.hrail(-15.0, -6.6, rO, a + 1.0)
    C.greeble(-15.4, -6.6, rO, 16, hmax=0.3)
    C.greeble(6.6, 15.4, rO, 16, hmax=0.3)
    k.group = 'Hull'


# =====================================================================================
# forward hull, guns, bow
# =====================================================================================
def build_fwd():
    x0, x1 = D['fwd']
    # counter-rotating flywheel housing just ahead of the collar
    k.lathe([(26.0, 13.1), (26.2, 13.5), (29.6, 13.5), (29.9, 13.1), (29.9, 9.0)], 'grey', seg=72, sharp=30)
    k.lathe(C.rrect(27.3, 28.5, 13.48, 13.56, 0.02, 0.02, 1), 'orange', seg=72, closed=True)
    for j in range(12):
        C.gbox(28.0 + (j % 2) * 0.9 - 0.45, j * TAU / 12 + 0.1, 13.5, 1.2, 1.0, 0.4, 'dark')
    loft(x0, x1, OCTI, OCTI, 'grey', cap0=True, cap1=False)
    plates(30.0, x1, OCT, OCT, 10, [2, 2, 3, 2, 2, 2, 3, 2], armour_mat(stripe=3), posts=0.6)
    for s in (1, -1):
        radiator(D['ltr'], False, s)
        LT = D['ltr']
        tip = LT['root'] + LT['n'] * LT['ws']
        k.box((LT['x1'] - 0.7, s * (tip + 0.5), 0), (1.2, 0.8, 0.8), 'navR' if s > 0 else 'navG')
        C.LIGHTS.append(('POINT', (LT['x1'] - 0.7, s * (tip + 1.6), 0), None, 1800, (1, 0.05, 0.03) if s > 0 else (0.05, 1, 0.2), 0))
    # coilgun turrets, dorsal and ventral
    for i in (T_, B_):
        with k.xf(F(i, D['gun']['x'], 0)):
            coilgun()
    # point defence on the four chamfers
    for i in CHAMF:
        with k.xf(F(i, D['pd']['x'], 0)):
            pd_turret()
    # phased-array radar faces on the bow chamfers
    for i in CHAMF:
        with k.xf(F(i, 87.3, 0)):
            k.box((0, 0.12, 0), (7.8, 0.24, 6.3), 'grey', bevel=0.05)
            k.box((0, 0.28, 0), (7.3, 0.1, 5.8), 'array')
            for xx in (-3.2, 3.2):
                k.box((xx, 0.1, 3.3), (0.6, 0.3, 0.4), 'dark')
    # lettering on the chamfers the sun and the camera see, both sides
    for i in (ST, PT):
        face_decal('hull', REG, i, 68.0, 0.0, 5.2)
    for i in (SB, PB):
        face_decal('hull', REG, i, 68.0, 0.0, 5.2)
    for i in CHAMF:          # viewports: duty stations forward of the flywheel bay
        sg = 1 if i in (PT, SB) else -1
        for x in (33.0, 35.4, 46.2, 48.4, 63.0, 80.8):
            port(i, x, sg * 2.3 if x < 60 else -sg * 2.6, R.random() < 0.65)
    # detail pass
    fgreeble(T_, 31, 42, 10, span=0.5)
    fgreeble(B_, 31, 42, 10, span=0.5)
    fgreeble(T_, 66, 91, 12, span=0.85)
    fgreeble(B_, 66, 91, 12, span=0.85)
    for i in CHAMF:
        fgreeble(i, 51, 60, 4, hmax=0.4, span=0.7)
        fgreeble(i, 76, 82, 3, hmax=0.4, span=0.7)
    for i in (P_, S_):
        fpipe(i, x0 + 5, x1 - 1, 3.55, 0.18, 'gold')
        fpipe(i, x0 + 5, x1 - 1, -3.55, 0.18, 'silver')
    for i, x, t in ((T_, 36, -5.2), (B_, 36, 5.2), (T_, 80, 5.0), (B_, 78, -5.0), (PT, 62.5, -2.6), (SB, 62.5, 2.6)):
        locker(i, x, t)
    for i, x, t in ((T_, 33, 3.6), (B_, 33, -3.6), (T_, 76, -3.4), (ST, 53, 0), (PB, 53, 0)):
        fhatch(i, x, t)
    for t in (-6.5, 6.5):
        frail(T_, 31, 44, t)
        frail(T_, 66, 91, t)
        frail(B_, 66, 91, t)
    for i in CHAMF:
        frail(i, 44, 62, 3.0)
    build_bow()


def build_bow():
    Bw = D['bow']
    x0, x1 = Bw['x0'], Bw['x1']
    BOW = octp(Bw['w'], Bw['h'], Bw['c'])
    BOWI = octp(Bw['w'] - 0.9, Bw['h'] - 0.9, Bw['c'] - 0.3)

    def bt(x):
        return (x - x0) / (x1 - x0)
    # forward pressure hull: a cone with a domed nose under the armour (seen through the plate gaps and at the collar)
    loft(x0, x1, OCTI, BOWI, 'grey', cap0=False, cap1=True)
    plates(x0, x1, OCT, BOW, 6, [1, 1, 2, 1, 1, 1, 2, 1], armour_mat(), gap=0.3)
    # Whipple bumper: thin sheets on posts, standing off the bow armour
    xa, xb = 96.0, 123.5
    WA = scale_sec(lerp_sec(OCT, BOW, bt(xa)), 1.055)
    WB = scale_sec(lerp_sec(OCT, BOW, bt(xb)), 1.14)
    plates(xa, xb, WA, WB, 5, [1, 1, 2, 1, 1, 1, 2, 1], lambda r, i, j: 'bumper', gap=0.22, sgap=0.18, th=0.05, bolts=False)
    for xx in (xa + 0.3, 101.6, 107.1, 112.6, 118.1, xb - 0.3):
        A0 = lerp_sec(OCT, BOW, bt(xx))
        A1 = scale_sec(A0, 1.055 + (1.14 - 1.055) * (xx - xa) / (xb - xa))
        for i in range(8):
            for u in (0.12, 0.88):
                p = Vector(A0[i]).lerp(Vector(A0[(i + 1) % 8]), u)
                q = Vector(A1[i]).lerp(Vector(A1[(i + 1) % 8]), u)
                k.tube((xx, p.x * 0.99, p.y * 0.99), (xx, q.x, q.y), 0.06, 'grey', seg=4)
        for i in range(8):
            k.tube((xx, A1[i][0], A1[i][1]), (xx, A1[(i + 1) % 8][0], A1[(i + 1) % 8][1]), 0.06, 'grey', seg=4)

    def ztop(x):
        return HZ + (Bw['h'] / 2 - HZ) * bt(x)

    def yside(x):
        return HY + (Bw['w'] / 2 - HY) * bt(x)
    # lidar turret (dorsal) and radome (ventral)
    zt = ztop(104.0)
    C.zcan(zt - 0.2, zt + 3.0, 0.75, 'grey', x=104.0, seg=16)
    C.zcan(zt + 0.75, zt + 1.05, 1.1, 'dark', x=104.0, seg=16, ch=0.05)
    k.box((104.0, 0, zt + 3.2), (1.0, 3.4, 0.5), 'grey', bevel=0.08)
    for s in (1, -1):
        k.box((104.0, s * 1.6, zt + 4.1), (0.9, 0.3, 2.0), 'grey', bevel=0.06)
    k.sphere((104.0, 0, zt + 4.3), 1.3, 'white_s', seg=24, rings=12)
    k.can(105.1, 105.45, 0.62, 'dark', seg=16, ch=0.04, oz=zt + 4.3)
    k.lathe([(105.46, 0.52), (105.46, 0)], 'glass_dark', seg=14, oz=zt + 4.3)
    k.sphere((104.0, 0, -zt - 1.5), 1.9, 'tank', seg=28, rings=14)
    C.zcan(-zt - 0.9, -zt + 0.2, 1.7, 'grey', x=104.0, seg=24)
    # optical turrets: paired telescopes each side
    ys = yside(110.0)
    for s in (1, -1):
        k.tube((110, s * (ys - 0.3), 0), (110, s * (ys + 1.5), 0), 0.4, 'grey', seg=10)
        k.sphere((110, s * (ys + 2.0), 0), 1.1, 'white_s', seg=20, rings=10)
        for dz in (0.5, -0.5):
            k.can(110.5, 112.1, 0.34, 'dark', seg=12, ch=0.04, oy=s * (ys + 2.0), oz=dz)
            k.lathe([(112.11, 0.28), (112.11, 0)], 'glass_dark', seg=10, oy=s * (ys + 2.0), oz=dz)
    # bow RCS quads on posts through the bumper
    xb_ = D['rcs']['bow']
    for q in range(4):
        with k.xf(rotX(q * TAU / 4 + TAU / 8)):
            k.tube((xb_, 6.6, 0), (xb_, 9.0, 0), 0.3, 'truss', seg=8)
            with k.xf(move(xb_, 9.9, 0) @ Matrix.Scale(0.8, 4)):
                C.rcs_quad()
    # androgynous docking collar at the bow
    k.lathe([(x1 - 0.1, 3.9), (x1 + 0.4, 3.9), (x1 + 0.9, 3.3)], 'grey', seg=40, sharp=40)
    with k.xf(move(x1 - 0.8, 0, 0)):
        C.dock_port()
        for q in range(3):
            with k.xf(rotX(q * TAU / 3 + 0.5) @ move(5.3, 2.5, 0) @ rotZ(0.5)):
                k.box((0.5, 0, 0), (1.3, 0.12, 1.3), 'hazard', bevel=0.04)
    k.tube((x1 + 0.6, 0, 3.6), (x1 + 0.6, 0, 5.0), 0.06, 'grey', seg=5)
    k.box((x1 + 0.6, 0, 5.1), (0.4, 0.4, 0.4), 'navW')
    C.LIGHTS.append(('POINT', (x1 + 0.6, 0, 5.9), None, 1200, (1, 1, 1), 0))


# =====================================================================================
def _plume(coll, x, length, r0, r1, y=0.0):
    """The library's plume sits on the ship's axis; the Bastion has two torches side by side."""
    ob = _plume0(coll, x=x, length=length, r0=r0, r1=r1)
    ob.location = (0.0, y, 0.0)
    return ob


_plume0 = C.build_plume
C.build_plume = _plume


def build(parts, drive, landers):
    k.group = 'Hull'
    if 'aft' in parts:
        build_aft_end(drive)
        build_aft_hull()
        if drive:
            for s in (1, -1):
                C.PLUMES.append(dict(x=-119.5, length=190.0, r0=2.0, r1=24.0, y=s * D['tor']['y']))
    if 'mid' in parts:
        build_mid()
    if 'fwd' in parts:
        build_fwd()


SHOTS = {
    'hero':  dict(tgt=(8, 0, -2), cam=(0.62, -0.72, 0.30), dist=495, lens=70, roll=-7, sun=(-0.45, 0.45, 0.77), eoff=(-9, -20), alt=9500, elon=20),
    'drum':  dict(tgt=(4, 0, 4), cam=(0.50, -0.78, 0.38), dist=150, lens=45, roll=4, sun=(-0.45, 0.45, 0.77), eoff=(-12, -27), alt=9500, elon=40),
    'stern': dict(tgt=(-82, 0, 0), cam=(-0.80, -0.52, 0.30), dist=300, lens=55, roll=4, sun=(0.5, 0.45, 0.74), eoff=(10, -25), alt=9500, elon=140),
    'guns':  dict(tgt=(66, 0, 11), cam=(0.58, -0.62, 0.53), dist=92, lens=40, roll=-4, sun=(-0.5, 0.45, 0.74), eoff=(13, -19), alt=9500, elon=165),
    'bay':   dict(tgt=(-70, 0, 12), cam=(-0.30, -0.45, 0.84), dist=110, lens=45, roll=3, sun=(-0.2, 0.3, 0.93), eoff=None, alt=0),
    'side':  dict(tgt=(0, 0, 0), cam=(0.0, -1.0, 0.06), dist=700, lens=100, roll=0, sun=(0.50, 0.62, 0.60), eoff=None, alt=0),
    'top':   dict(tgt=(0, 0, 0), cam=(0.02, -0.05, 1.0), dist=700, lens=100, roll=0, sun=(0.40, 0.50, 0.75), eoff=None, alt=0),
    'under': dict(tgt=(0, 0, 0), cam=(0.45, 0.55, -0.70), dist=480, lens=70, roll=0, sun=(0.2, 0.4, 0.9), eoff=None, alt=0),
}

if __name__ == '__main__':
    C.main(build=build, shots=SHOTS, ship_id='bastion')
