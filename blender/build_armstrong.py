"""Armstrong-class fast courier: high-detail model, path-traced stills and the game model.

On the Cochrane pipeline (see blender/prewarp_ships.md). Run with the bpy module:
  /home/user/.bpyvenv/bin/python blender/build_armstrong.py --shot hero --out blender/previews/armstrong --tmp <dir> --earth-tex <bmng.jpg>
  --shot hero|pod|stern|hub|side   --drive (reactor at full power)   --only aft,mid,fwd   --game game/models/ships/armstrong.glb
Layout and sizes follow the class page: x toward the bow, y to port, z dorsal; metres; origin amidships on the axis
(the ship runs x -70 .. +70). Cruise configuration: both crew pods reeled out to 45 m and turning.

What this rebuild changed against the first model (the reasons are in blender/previews/armstrong/changes.json):
  - the solid thermal spine is an open four-longeron truss with the hydrogen feed, lithium loop and power bus inside it;
  - radiators are thin segmented panels on headers, ribs, edge booms and stay rods, not slabs;
  - the tank is a foil-wrapped vessel under spaced bumper plates, carried by strut cones at both ends;
  - pods and crew module are domed pressure vessels with round ports, not flat-ended cans and a band of glass;
  - each tether is two strands from two reels that straddle the pod's docking neck, so the neck is free to meet the cradle
    collar when the pod is wound home (the old single apex on the axis was in the way);
  - the bow shield is spaced bumper petals on a spider frame, not a solid disc.
"""
import sys, os, math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_cochrane as C          # imports bpy first; mathutils only loads after it
import cochrane_kit as K
from mathutils import Matrix, Vector
from build_cochrane import k, on, P, R, rotX, rotY, rotZ, move, orient, TAU, PI

NAME, REG = 'ARMSTRONG', 'UE-19'
A = dict(
    noz=dict(coils=[(-69.55, 8.6), (-67.7, 7.1), (-65.9, 5.9), (-64.1, 4.9)], tube=0.45),
    reac=dict(x0=-60.5, x1=-44.0, r=3.4),
    conv=dict(x0=-44.0, x1=-40.0, r=4.8),
    cone=dict(x=-52.25, r=3.4, k=0.5),           # edge of the shield's shadow
    shield=dict(x0=-40.0, x1=-37.0),
    spine=dict(x0=-37.0, x1=0.0, r=3.2),
    htr=dict(x0=-35.5, x1=0.0, root=4.0, w0=6.4, w1=23.0, n=4),
    iface=dict(x0=0.0, x1=6.0, r=4.2),
    tank=dict(x0=6.0, x1=39.0, r=7.5, head=4.0, boss=3.0),
    ltr=dict(x0=11.0, x1=31.0, root=8.1, w=7.0),
    fuel=dict(x=4.6, r=5.8, rs=1.1),
    hub=dict(x=46.0, x0=41.4, x1=50.6, r=5.6, core=3.0, cradle=7.5, reel=3.6),
    pods=dict(r=45.0, g=0.4, dir=1, R=3.1, len=7.8, floor=6.4, deck2=3.8, fin=dict(u0=2.0, u1=7.0, w=1.8), thr=6.9),
    crew=dict(x0=52.2, bay=(53.2, 58.6), deck=(58.6, 65.0), nose=67.6, bow=70.0, r=4.6, port=56.0),
)
PHASE = float(os.environ.get('ARM_PHASE', 0.6))          # where the pods are in their turn (radians from dorsal/ventral)
RSH = A['cone']['r'] + A['cone']['k'] * (A['shield']['x1'] - A['cone']['x'])      # shield radius, 11.0 m

C.EXTRA['tether'] = (lambda: K.plain('tether', (0.82, 0.82, 0.78), rough=0.75, grime=0.1), ('sim', (0.82, 0.82, 0.78), 0.0, 0.75))


def S(n, g=None):
    """Segment count: n in the stills, about half in the game model (the kit would cut it to a third)."""
    if k.lod >= 1:
        return n
    return int(math.ceil((g or max(8, n // 2)) / k.lod))


def game():
    return k.lod < 1


# ------------------------------------------------------------------ small parts
GM = ['grey', 'grey', 'white_s', 'dark', 'gold', 'grey', 'silver', 'white_s', 'white_s']


def greeble(x0, x1, r, n, a0=0.0, span=TAU, hmax=0.4, sc=0.5):
    """Equipment boxes, bottles and whips on a hull, at this ship's size."""
    for _ in range(n):
        x = x0 + R.random() * (x1 - x0)
        a = a0 + R.random() * span
        t = R.random()
        w, d, h = (0.5 + R.random() * 2.2) * sc, (0.5 + R.random() * 1.6) * sc, 0.1 + R.random() * hmax
        mat = R.choice(GM)
        if t < 0.72:
            C.gbox(x, a, r, w, d, h, mat, bevel=0.03)
        elif t < 0.86:
            L, rad = (1.0 + R.random() * 1.6) * sc, (0.28 + R.random() * 0.3) * sc
            with k.xf(on(x, a, r + rad * 0.85)):
                k.can(-L / 2, L / 2, rad, R.choice(['silver', 'gold', 'white_s', 'grey']), ch=0.03, seg=10)
        elif t < 0.94:
            rad = (0.4 + R.random() * 0.4) * sc
            with k.xf(on(x, a, r)):
                k.sphere((0, rad * 0.9, 0), rad, R.choice(['silver', 'gold']), seg=12, rings=6)
        else:
            hh = 0.6 + R.random() * 1.0
            with k.xf(on(x, a, r)):
                k.tube((0, 0, 0), (0, hh, 0), 0.025, 'grey', seg=4)
                k.box((0, hh, 0), (0.12, 0.12, 0.12), 'dark')


def band(x, r, w=0.2, h=0.12, mat='grey', seg=64):
    k.lathe(C.rrect(x - w / 2, x + w / 2, r - 0.02, r + h, 0.03, 0.03, 1), mat, seg=S(seg), closed=True)


def cryostat(x, r, w, seg=72, clamps=12, case='steel'):
    """A superconducting coil in its vacuum case, with a foil belt and clamp blocks."""
    k.lathe(C.rrect(x - w, x + w, r - w, r + w, w * 0.45, w * 0.45, 3), case, seg=S(seg), closed=True, sharp=30)
    k.lathe(C.rrect(x - w * 0.42, x + w * 0.42, r - w - 0.035, r + w + 0.035, 0.05, 0.05, 1), 'gold', seg=S(seg), closed=True, sharp=30)
    for j in range(clamps):
        with k.xf(on(x, (j + 0.5) * TAU / clamps, r + w)):
            k.box((0, 0.07, 0), (w * 2.25, 0.2, 0.5), 'grey', bevel=0.03)


def truss4(x0, x1, h, nb, rl=0.16, rd=0.07, mat='truss'):
    """Square tube truss along x, longerons on the diagonals; returns the frame stations."""
    cs = [(-h, -h), (h, -h), (h, h), (-h, h)]
    for c in cs:
        k.tube((x0, c[0], c[1]), (x1, c[0], c[1]), rl, mat, seg=8)
    xs = [x0 + (x1 - x0) * i / nb for i in range(nb + 1)]
    for i, x in enumerate(xs):
        for j in range(4):
            c, c2 = cs[j], cs[(j + 1) % 4]
            k.tube((x, c[0], c[1]), (x, c2[0], c2[1]), rd * 1.3, mat, seg=6)
            with k.xf(move(x, c[0], c[1])):
                k.can(-0.3, 0.3, rl * 1.75, 'grey', seg=10, ch=0.04)
            if i < nb:
                p, q = (c, c2) if (i + j) % 2 else (c2, c)
                k.tube((x, p[0], p[1]), (xs[i + 1], q[0], q[1]), rd, mat, seg=5)
    return xs


def strut_cone(xa, ra, xb, rb, n, rad=0.12, mat='truss', a0=0.0):
    """Ring of V struts between two rings: the way a tank or a shield is carried without a solid skirt."""
    for i in range(n):
        a = a0 + i * TAU / n
        for da in (-0.5, 0.5):
            k.tube(P(xa, ra, a), P(xb, rb, a + da * TAU / n), rad, mat, seg=6)
        with k.xf(on(xa, a, ra)):
            k.box((0, 0, 0), (rad * 4, rad * 3, rad * 5), 'grey')
        with k.xf(on(xb, a + 0.5 * TAU / n, rb)):
            k.box((0, 0, 0), (rad * 4, rad * 3, rad * 5), 'grey')


def quad_on_stalk(x, a, r0, r1, sc=0.45):
    """RCS quad on a short tube tripod."""
    with k.xf(rotX(a)):
        k.tube((x, r0, 0), (x, r1 - 0.5, 0), 0.11, 'truss', seg=8)
        k.tube((x - 0.9, r0, 0), (x, r1 - 0.6, 0), 0.07, 'truss', seg=5)
        k.tube((x + 0.9, r0, 0), (x, r1 - 0.6, 0), 0.07, 'truss', seg=5)
        k.tube((x, r0, 0.8), (x, r1 - 0.6, 0), 0.07, 'truss', seg=5)
        k.tube((x, r0, -0.8), (x, r1 - 0.6, 0), 0.07, 'truss', seg=5)
        k.pipe([(x + 0.25, r0, 0.2), (x + 0.25, r1 - 0.7, 0.2)], 0.04, 'gold', seg=5)
        with k.xf(move(x, r1, 0) @ Matrix.Scale(sc, 4)):
            C.rcs_quad()


def porthole(x, a, r, rad=0.3, lit=0.6):
    """Round cabin port: a pressure-friendly window with a bolted frame."""
    with k.xf(on(x, a, r) @ orient((0, 0, 0), (0, 1, 0))):
        k.lathe([(-0.05, rad + 0.14), (0.07, rad + 0.14), (0.07, rad), (0.03, rad)], 'grey', seg=S(18, 10), sharp=30)
        k.lathe([(0.035, rad), (0.035, 0)], 'glass' if R.random() < lit else 'glass_dark', seg=S(14, 8))


def small_hatch(x, a, r, rad=0.55):
    with k.xf(on(x, a, r) @ orient((0, 0, 0), (0, 1, 0))):
        k.lathe([(-0.05, rad + 0.18), (0.1, rad + 0.18), (0.1, rad), (0.06, rad), (0.06, 0)], 'grey', seg=S(20, 10), sharp=30)
        k.lathe([(0.07, rad * 0.22), (0.085, rad * 0.22), (0.085, 0)], 'glass_dark', seg=10)
        k.box((0.1, rad * 0.55, 0), (0.06, 0.08, 0.34), 'yellow')
        k.box((0.1, -rad * 0.75, 0), (0.08, 0.14, 0.5), 'dark')


_FONT = None


def text_aspect(text):
    """Width over height of the lettering image the pipeline draws for a decal."""
    global _FONT
    from PIL import ImageFont
    if _FONT is None:
        for p in ('/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',
                  '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf'):
            if os.path.exists(p):
                _FONT = ImageFont.truetype(p, 200)
                break
        else:
            _FONT = False
    if not _FONT:
        return 0.6 * len(text)
    return (int(_FONT.getlength(text)) + 60) / 280.0


def hull_decal(name, text, xc, r, a, h, seg=6):
    """Lettering along a round hull at angle a, h metres tall, reading the right way up from outside."""
    w = h * text_aspect(text)
    up = 1 if math.cos(a) > 0 else -1          # port side: letters' tops toward +a; starboard: toward -a
    xa, xb = (xc + w / 2, xc - w / 2) if up > 0 else (xc - w / 2, xc + w / 2)
    da = h / r / 2
    v, f, uv = [], [], []
    for j in range(seg + 1):
        aj = a - up * da + up * 2 * da * j / seg
        v.append(P(xa, r, aj)); v.append(P(xb, r, aj))
    for j in range(seg):
        f.append((2 * j, 2 * j + 1, 2 * j + 3, 2 * j + 2))
        v0, v1 = j / seg, (j + 1) / seg
        uv.append([(0, v0), (1, v0), (1, v1), (0, v1)])
    k.add('decal_' + name, v, f, uv)
    C.DECALS.append((name, text))


# =====================================================================================
# aft: magnetic nozzle, mirror reactor, direct converter, shadow shield, spine, hot radiators
# =====================================================================================
def build_aft(drive):
    N, RE, CV = A['noz'], A['reac'], A['conv']
    # --- magnetic nozzle: four superconducting hoops in cryostats on a strut cage; no wall touches the plasma
    coils = N['coils']
    for i, (x, r) in enumerate(coils):
        cryostat(x, r, N['tube'] - i * 0.03, clamps=12 if i < 2 else 8)
    ns = 8
    for i in range(ns):
        a = (i + 0.5) * TAU / ns
        a2 = a + TAU / ns
        k.tube(P(-69.75, 9.35, a), P(-60.4, 4.75, a), 0.15, 'truss', seg=8)
        for j, (x, r) in enumerate(coils):          # saddles from the strut down to each coil case
            rs = 9.35 + (x + 69.75) * (4.75 - 9.35) / 9.35
            with k.xf(on(x, a, r + N['tube'])):
                k.box((0, (rs - r - N['tube']) / 2, 0), (0.5, rs - r - N['tube'] + 0.1, 0.3), 'grey')
        for (xa, ra), (xb, rb) in zip(coils, coils[1:]):
            p, q = (a, a2) if i % 2 else (a2, a)
            k.tube(P(xa, ra + 0.5, p), P(xb, rb + 0.5, q), 0.05, 'truss', seg=5)
    for a in (0.5, 2.6, 4.7):          # helium lines to the hoops
        k.pipe([P(-69.4, 9.3, a), P(-63.5, 6.3, a), P(-60.6, 5.5, a), P(-58.0, 5.75, a)], 0.09, 'gold', seg=6)
        k.pipe([P(-69.4, 9.3, a + 0.07), P(-63.5, 6.3, a + 0.07), P(-60.6, 5.5, a + 0.07)], 0.05, 'silver', seg=5)
    # mirror throat: the end-plug field squeezes the plasma down to 1.6 m before the hoops let it expand
    k.lathe([(-60.5, 3.25), (-61.6, 2.85), (-63.2, 2.1), (-64.3, 1.72), (-64.6, 1.72)], 'bell', seg=S(48), sharp=60)
    k.lathe([(-60.5, 3.1), (-63.2, 1.95), (-64.6, 1.6)], 'black', seg=S(48), sharp=60)
    k.lathe([(-64.6, 1.72), (-64.6, 1.6)], 'bell', seg=S(48))
    k.lathe([(-64.2, 1.62), (-64.2, 0)], 'plasma' if drive else 'black', seg=S(32))
    cryostat(-62.4, 3.25, 0.5, clamps=8, case='white_s')
    for i in range(8):
        a = i * TAU / 8
        k.tube(P(-63.9, 1.9, a), P(-60.6, 4.6, a), 0.06, 'truss', seg=5)
    # thrust ring: nozzle struts and the reactor cage meet here
    k.lathe(C.rrect(-60.75, -60.25, 4.45, 5.05, 0.08, 0.08, 1), 'grey', seg=S(64), closed=True)
    # --- mirror fusion reactor: vacuum vessel, end-plug coils, central solenoids, beam injectors
    x0, x1, r = RE['x0'], RE['x1'], RE['r']
    k.lathe([(x0, 0), (x0, r - 0.3), (x0 + 0.3, r), (x1 - 0.3, r), (x1, r - 0.3), (x1, 0)], 'vessel', seg=S(56))
    for x in (x0 + 1.25, x1 - 1.25):
        w, h = 0.85, 0.95
        k.lathe(C.rrect(x - w, x + w, r + 0.05, r + 0.05 + 2 * h, 0.25, 0.25, 3), 'white_s', seg=S(64), closed=True, sharp=30)
        for xx in (x - w, x + w):
            k.lathe(C.rrect(xx - 0.1, xx + 0.1, r, r + 2 * h + 0.14, 0.04, 0.04, 1), 'grey', seg=S(64), closed=True)
        for j in range(16):
            C.gbox(x, (j + 0.5) * TAU / 16, r + 2 * h + 0.05, 0.4, 0.3, 0.14, 'dark', bevel=0.0)
    for i in range(4):
        x = -56.0 + i * 2.8
        w, h = 0.5, 0.52
        k.lathe(C.rrect(x - w, x + w, r + 0.05, r + 0.05 + 2 * h, 0.16, 0.16, 3), 'white_s', seg=S(64), closed=True, sharp=30)
        for xx in (x - w, x + w):
            k.lathe(C.rrect(xx - 0.07, xx + 0.07, r, r + 2 * h + 0.1, 0.03, 0.03, 1), 'grey', seg=S(64), closed=True)
    for x in (-57.4, -54.6, -51.8, -49.0, -46.4):          # diagnostic ports and pump ducts between the coils
        for j in range(6):
            a = j * TAU / 6 + (0.26 if x != -51.8 else 0.78)
            with k.xf(on(x, a, r) @ orient((0, 0, 0), (0, 1, 0))):
                k.can(0, 0.5, 0.2, 'steel', seg=10, ch=0.03)
                k.can(0.5, 0.58, 0.27, 'grey', seg=10, ch=0.02)
    cage = 6.0
    xs = [x0, -55.0, -49.5, x1]
    for i in range(6):
        a = (i + 0.5) * TAU / 6
        a2 = a + TAU / 6
        k.tube(P(x0, cage, a), P(x1, cage, a), 0.13, 'truss', seg=8)
        k.tube(P(x0, cage, a), P(x0 - 0.1, 4.9, a), 0.1, 'truss', seg=6)
        k.tube(P(x1, cage, a), P(x1 + 0.3, 4.9, a), 0.1, 'truss', seg=6)
        for j, x in enumerate(xs):
            if i not in (2, 5) or j in (0, 3):          # the injectors come through the port and starboard faces
                k.tube(P(x, cage, a), P(x, cage, a2), 0.08, 'truss', seg=5)
            if j in (1, 2):
                k.tube(P(x, cage, a), P(x, r + 0.1, a), 0.06, 'truss', seg=5)
            with k.xf(on(x, a, cage)):
                k.can(-0.22, 0.22, 0.22, 'grey', seg=8, ch=0.03)
        for j in range(3):
            if i in (2, 5) and j == 1:
                continue
            p, q = (a, a2) if (i + j) % 2 else (a2, a)
            k.tube(P(xs[j], cage, p), P(xs[j + 1], cage, q), 0.045, 'truss', seg=4)
    for s in (0, 1):          # neutral-beam injectors, port and starboard, angled into the plasma
        with k.xf(rotX(s * PI)):
            with k.xf(orient((-51.5, r, 0), (-0.4, 1, 0))):
                k.can(0, 3.0, 0.52, 'steel', seg=S(20))
                k.can(0.55, 0.8, 0.68, 'grey', seg=S(20), ch=0.04)
                k.can(2.2, 2.4, 0.66, 'grey', seg=S(20), ch=0.04)
                k.box((3.9, 0, 0), (1.9, 1.6, 1.5), 'white_s', bevel=0.08)
                k.box((3.9, 0, 0.8), (1.4, 1.1, 0.14), 'gold')
                k.box((3.9, 0, -0.8), (1.5, 1.2, 0.1), 'radw')
                k.can(4.85, 5.5, 0.45, 'steel', seg=14, ch=0.06)
                with k.xf(orient((1.5, 0, 0.5), (0, 0, 1))):
                    k.can(0, 0.9, 0.3, 'grey', seg=12, ch=0.04)
            k.pipe([(-53.4, 7.55, 0.5), (-53.4, 6.4, 1.6), (-47.0, 6.2, 1.6), (-44.2, 5.4, 1.6)], 0.07, 'silver', seg=6)
            k.pipe([(-53.2, 7.4, -0.5), (-50.0, 6.25, -1.5), (-44.2, 5.4, -1.5)], 0.09, 'dark', seg=6)
    for a in (PI / 2 - 0.25, PI / 2 + 0.2, -PI / 2 - 0.25, -PI / 2 + 0.2, 0.75, 3.9):          # cryogen and coolant runs on the cage
        C.axial_pipe(x0 + 0.3, x1 + 0.2, cage - 0.1, a, R.choice([0.09, 0.12]), R.choice(['silver', 'gold', 'gold', 'white_s']), clamp=2.75)
    greeble(-58.5, -45.5, cage - 0.2, 14, a0=PI / 2 - 0.8, span=1.6, hmax=0.3, sc=0.4)
    greeble(-58.5, -45.5, cage - 0.2, 14, a0=-PI / 2 - 0.8, span=1.6, hmax=0.3, sc=0.4)
    # --- direct energy converter: drum of collector grids, power conditioning round it
    c0, c1, cr = CV['x0'], CV['x1'], CV['r']
    k.lathe([(c0, 0), (c0, cr - 0.25), (c0 + 0.25, cr), (c1 - 0.2, cr), (c1, cr - 0.2)], 'white', seg=S(64))
    for x in (c0 + 0.45, c0 + 2.0, c1 - 0.45):
        band(x, cr, 0.28, 0.22)
    k.lathe(C.rrect(-42.75, -41.75, cr - 0.02, cr + 0.06, 0.02, 0.02, 1), 'blue', seg=S(64), closed=True)
    for i in range(8):
        a = i * TAU / 8 + TAU / 16
        C.gbox(-41.3, a, cr, 1.1, 1.5, 0.6, 'white_s', bevel=0.05)
        with k.xf(on(-41.3, a, cr + 0.66)):
            k.box((0, 0.04, 0), (0.9, 0.06, 1.25), 'radw')
        C.gbox(-43.1, a + TAU / 16, cr, 0.7, 0.6, 0.3, R.choice(['gold', 'grey', 'dark']), bevel=0.0)
    # --- shadow shield: tungsten toward the reactor, lithium hydride in a white can behind it, carried on struts
    k.lathe([(-40.0, cr - 0.2), (-39.65, 5.6), (-38.95, 7.4), (-38.1, 9.7), (-37.75, 10.6), (-37.55, RSH)], 'tungsten', seg=S(96), sharp=25)
    k.lathe([(-37.55, RSH), (-37.15, RSH), (-36.95, RSH - 0.2), (-36.95, 2.2)], 'white', seg=S(96), sharp=40)
    k.torus(-37.35, RSH, 0.2, 'grey', seg=S(96), tseg=8)
    for (xx, rr) in ((-39.62, 5.68), (-38.93, 7.45), (-38.08, 9.75)):
        k.torus(xx - 0.03, rr, 0.05, 'steel', seg=S(96), tseg=4)
    for i in range(16):
        a = i * TAU / 16
        k.tube(P(-39.68, 5.62, a), P(-37.58, 10.95, a), 0.035, 'steel', seg=4)
    k.lathe([(-37.1, 2.3), (-37.1, 0.3)], 'dark', seg=S(32))
    for rr in (5.2, 8.2):
        k.lathe(C.rrect(-36.97, -36.8, rr - 0.12, rr + 0.12, 0.03, 0.03, 1), 'grey', seg=S(64), closed=True)
    for i in range(24):
        with k.xf(rotX(i * TAU / 24)):
            k.box((-36.85, 6.75, 0), (0.22, 7.6, 0.14), 'grey')
    # --- spine: an open truss carrying the feed line, the lithium loop and the power bus to the reactor
    h = 2.2
    xs = truss4(-36.95, 0.0, h, 8)
    for i in range(8):          # the shield hangs off the first frame on eight struts
        a = TAU / 8 + (i // 2) * TAU / 4
        k.tube(P(xs[1], h * math.sqrt(2), a), P(-36.9, 8.2, a + (0.3 if i % 2 else -0.3)), 0.11, 'truss', seg=6)
    k.tube((-37.0, 0, 0), (0.2, 0, 0), 0.42, 'gold', seg=S(14))          # vacuum-jacketed hydrogen feed
    k.tube((-37.0, 0.95, 0.95), (0.2, 0.95, 0.95), 0.17, 'silver', seg=8)          # chill-down return
    for s in (1, -1):
        k.tube((-37.0, s * 1.15, -s * 0.5), (-34.6, s * 1.15, -s * 0.5), 0.3, 'hotpipe', seg=10)          # lithium trunks from the reactor loop
        k.tube((-34.6, s * 1.15, -s * 0.5), (-35.9, s * 2.6, -s * 2.4), 0.24, 'hotpipe', seg=8)
        k.boxX(-36.9, 0.1, 0, s * 1.55, 0.7, 0.26, 'dark')          # bus ducts
        k.tube((-36.9, -s * 0.9, s * 0.2), (0.1, -s * 0.9, s * 0.2), 0.09, 'white_s', seg=6)
    for x in xs[1:-1]:          # cross frames holding the lines
        k.box((x, 0, 0), (0.12, 2 * h, 0.1), 'grey')
        k.box((x, 0, 0), (0.12, 0.1, 2 * h), 'grey')
        with k.xf(move(x, 0, 0)):
            k.can(-0.12, 0.12, 0.52, 'grey', seg=12, ch=0.03)
    for x in ((xs[i] + xs[i + 1]) / 2 for i in range(8)):
        with k.xf(move(x, 0, 0)):
            k.can(-0.08, 0.08, 0.46, 'silver', seg=12, ch=0.02)
    # power conditioning pallets on the port and starboard faces
    for s in (1, -1):
        k.box((-34.5, s * (h + 0.55), 0), (3.4, 1.0, 2.6), 'white_s', bevel=0.08)
        k.box((-34.5, s * (h + 1.1), 0), (3.0, 0.08, 2.2), 'radw')
        k.box((-31.4, s * (h + 0.35), 0.5), (1.4, 0.6, 1.2), 'gold', bevel=0.05)
        for x in (-29.0, -24.2, -19.7, -15.2, -10.5, -6.0):
            t = R.random()
            if t < 0.35:
                k.box((x, s * (h + 0.3), R.uniform(-1, 1)), (R.uniform(1, 2), 0.5, R.uniform(0.8, 1.6)), R.choice(['white_s', 'gold', 'grey']), bevel=0.05)
            elif t < 0.6:
                for dz in (-0.5, 0.5):
                    k.sphere((x, s * (h + 0.5), dz), 0.42, R.choice(['silver', 'gold']), seg=12, rings=6)
                k.box((x, s * (h + 0.08), 0), (1.2, 0.1, 2.2), 'grey')
            elif t < 0.8:
                k.box((x, s * (h + 0.12), 0), (2.2, 0.08, 2.8), 'radw')
    # --- high-temperature radiators: four wings in planes through the axis, edge-on to the reactor
    H = A['htr']
    hx0, hx1, root, nseg, t = H['x0'], H['x1'], H['root'], 8, 0.045
    L = hx1 - hx0
    wd = lambda x: H['w0'] + (H['w1'] - H['w0']) * (x - hx0) / L
    k.torus(hx0 - 0.4, 3.66, 0.22, 'hotpipe', seg=S(48), tseg=6)
    for q in range(H['n']):
        with k.xf(rotX(TAU / 8 + q * TAU / 4)):
            for s in (1, -1):          # lithium out and back along the root
                k.tube((hx0 - 0.4, 3.66, s * 0.3), (hx1 + 0.1, 3.66, s * 0.3), 0.2, 'hotpipe', seg=8)
            for i in range(nseg):
                xa = hx0 + L * i / nseg + 0.09
                xb = hx0 + L * (i + 1) / nseg - 0.09
                wa, wb = wd(xa), wd(xb)
                v = [(xa, root, -t), (xb, root, -t), (xb, root + wb, -t), (xa, root + wa, -t),
                     (xa, root, t), (xb, root, t), (xb, root + wb, t), (xa, root + wa, t)]
                f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)]
                uf = [(xa, 0), (xb, 0), (xb, 1), (xa, 1)]
                uvs = [[uf[0], uf[3], uf[2], uf[1]], uf, [uf[0], uf[1], uf[1], uf[0]], [uf[2], uf[3], uf[3], uf[2]],
                       [uf[1], uf[2], uf[2], uf[1]], [uf[3], uf[0], uf[0], uf[3]]]
                k.add('hot', v, f, uvs)
                k.box((xa - 0.09, root + wa / 2, 0), (0.09, wa, 0.24), 'cc')
                k.tube((xa, root + wa + 0.1, 0), (xb, root + wb + 0.1, 0), 0.085, 'cc', seg=6)
                k.tube((xa + 0.2, 3.66, 0.3), (xa + 0.2, root + 0.05, 0), 0.09, 'hotpipe', seg=5)
                k.tube((xb - 0.2, 3.66, -0.3), (xb - 0.2, root + 0.05, 0), 0.09, 'hotpipe', seg=5)
                k.box(((xa + xb) / 2, 3.38, 0), (0.3, 0.5, 0.9), 'grey')          # header saddle on the longeron
            k.box((hx1 + 0.02, root + H['w1'] / 2, 0), (0.09, H['w1'], 0.24), 'cc')
            for xs_ in (hx0 + L * 0.25, hx0 + L * 0.5, hx0 + L * 0.75, hx1):          # stay rods to the next longerons keep the wings flat
                ws = wd(xs_)
                for s in (1, -1):
                    k.tube((xs_, root + ws, 0), (xs_ - 4.6, 0, s * h * math.sqrt(2)), 0.028, 'cc', seg=4)


# =====================================================================================
# mid: interface (pumps, fuel, aft RCS), hydrogen tank, cold radiators, cryocoolers
# =====================================================================================
def build_mid():
    IF, T, F, L = A['iface'], A['tank'], A['fuel'], A['ltr']
    r = IF['r']
    # --- interface drum: turbopumps, fusion fuel dewars, aft thruster quads
    k.lathe([(0, 2.4), (0, r - 0.25), (0.25, r), (5.75, r), (6.0, r - 0.25), (6.0, T['boss'])], 'white', seg=S(64))
    for x in (0.45, 3.0, 5.55):
        band(x, r, 0.3, 0.14)
    for q in range(4):
        quad_on_stalk(1.5, q * TAU / 4, r, 6.7)
    for i, a in enumerate((TAU / 8, 5 * TAU / 8)):          # deuterium and helium-3
        with k.xf(rotX(a)):
            k.sphere((F['x'], F['r'], 0), F['rs'], 'gold' if i == 0 else 'silver', seg=S(32), rings=14)
            with k.xf(move(F['x'], F['r'], 0)):
                k.torus(0, F['rs'] + 0.02, 0.05, 'grey', seg=S(32), tseg=5)
                with k.xf(rotZ(PI / 2)):
                    k.torus(0, F['rs'] + 0.02, 0.05, 'grey', seg=S(32), tseg=5)
            for dx in (-0.7, 0.7):
                for dz in (-0.7, 0.7):
                    k.tube((F['x'] + dx, F['r'] - 0.75, dz), (F['x'] + dx * 1.5, r, dz * 1.5), 0.05, 'truss', seg=5)
            k.pipe([(F['x'], F['r'] + F['rs'], 0), (F['x'] - 1.0, F['r'] + F['rs'] + 0.15, 0), (F['x'] - 2.6, r + 0.2, 0.5), (F['x'] - 4.5, r - 0.6, 0.5)], 0.05, 'gold', seg=5)
            k.box((F['x'], F['r'] + F['rs'] + 0.08, 0), (0.4, 0.2, 0.4), 'grey')
    for a in (3 * TAU / 8, 7 * TAU / 8):          # hydrogen turbopumps: inducer, volute, motor
        with k.xf(on(3.3, a, r)):
            k.box((0, 0.08, 0), (3.6, 0.16, 1.9), 'grey')
            with k.xf(move(0, 0.75, 0)):
                k.can(-1.0, 0.2, 0.46, 'steel', seg=S(18), ch=0.06)
                k.can(-1.7, -1.0, 0.36, 'gold', seg=S(16), ch=0.05)
                k.can(-1.05, -0.9, 0.56, 'grey', seg=S(18), ch=0.03)
                k.sphere((0.75, 0, 0), 0.62, 'steel', seg=S(18), rings=8, sx=0.7)
                k.can(1.15, 1.6, 0.3, 'grey', seg=12, ch=0.04)
            k.pipe([(1.6, 0.75, 0), (2.6, 0.75, 0), (3.2, 1.4, 0.4)], 0.2, 'gold', seg=8)
            k.pipe([(0.75, 1.3, 0), (0.75, 1.5, 0), (-2.6, 0.9, -0.5), (-3.6, 0.2, -0.5)], 0.13, 'gold', seg=6)
            k.pipe([(-1.7, 0.75, 0), (-2.2, 0.75, 0.6), (-2.2, 0.1, 0.6)], 0.05, 'dark', seg=5)
    greeble(0.8, 5.4, r, 30, hmax=0.35, sc=0.42)
    for a in (0.3, 1.9, 3.45, 5.0):
        C.hrail(0.8, 5.2, r, a)
    # --- thrust cone: struts from the drum to the tank's aft ring frame, the foil head showing through
    strut_cone(5.5, r + 0.1, T['x0'] + T['head'], T['r'] + 0.1, 12, rad=0.13)
    k.lathe(C.rrect(5.3, 5.75, r, r + 0.3, 0.05, 0.05, 1), 'grey', seg=S(64), closed=True)
    # --- liquid hydrogen tank: vessel in foil, spaced white bumper plates over the barrel
    xa, xb, tr, hd = T['x0'] + T['head'], T['x1'] - T['head'], T['r'], T['head']
    sb = math.sqrt(1 - (T['boss'] / tr) ** 2)
    n = 9 if not game() else 5
    for xr, d in ((xa, -1), (xb, 1)):
        prof = [(xr + d * hd * sb * i / n, tr * math.sqrt(1 - (sb * i / n) ** 2)) for i in range(n + 1)]
        prof.append((xr + d * (hd * sb + 0.45), T['boss']))
        k.lathe(prof, 'gold', seg=S(72), sharp=60)
        k.lathe(C.rrect(xr + d * 0.05 - 0.3, xr + d * 0.05 + 0.3, tr - 0.05, tr + 0.26, 0.06, 0.06, 1), 'grey', seg=S(96), closed=True)
        k.torus(xr + d * hd * sb * 0.62, tr * math.sqrt(1 - (sb * 0.62) ** 2) + 0.03, 0.05, 'grey', seg=S(64), tseg=5)
    k.lathe([(xa, tr), (xb, tr)], 'gold', seg=S(72))
    nrow, ncol = 10, 20
    rw = (xb - xa - 0.6) / nrow
    for i in range(nrow):
        x0 = xa + 0.3 + i * rw
        for j in range(ncol):
            a0 = (j + 0.5 * (i % 2) + 0.012) * TAU / ncol
            a1 = (j + 0.5 * (i % 2) + 0.988) * TAU / ncol
            mat = 'white' if R.random() > 0.05 else 'white_s'
            C.arc_plate(x0 + 0.03, x0 + rw - 0.03, tr + 0.09, tr + 0.14, a0, a1, mat, seg=4, vref=tr)
    for i in range(1, nrow):
        if i % 2 == 0:
            band(xa + 0.3 + i * rw, tr + 0.08, 0.16, 0.1, seg=96)
    k.lathe(C.rrect(xb - 4.4, xb - 3.2, tr + 0.13, tr + 0.165, 0.01, 0.01, 1), 'blue', seg=S(96), closed=True)
    # lettering, port and starboard
    for a, sg in ((0.0, 1), (PI, -1)):          # 30 degrees above the waterline, where the sun and a camera can both see them
        hull_decal('arm_name', NAME, 19.0, tr + 0.16, a + sg * 0.52, 3.4)
        hull_decal('arm_reg', REG + '   UNITED EARTH', 19.0, tr + 0.16, a + sg * 0.17, 1.3)
    # services along the barrel: vent and fill lines, cable tray, rails, fill panel
    for a, rad, mat in ((1.05, 0.11, 'silver'), (1.12, 0.07, 'gold'), (PI + 0.62, 0.11, 'gold'), (PI + 0.71, 0.06, 'white_s')):
        C.axial_pipe(xa - 0.5, xb + 0.5, tr + 0.12, a, rad, mat, clamp=2.5)
    for a in (-0.62, PI - 1.08):
        with k.xf(on(0, a, tr + 0.14)):
            k.boxX(xa + 0.4, xb - 0.4, 0.05, 0, 0.1, 0.26, 'grey')
        C.hrail(xa + 0.6, xb - 0.6, tr + 0.14, a + 0.12)
    for a in (1.05, PI + 0.62):          # relief vent stacks
        with k.xf(on(xb - 1.5, a, tr + 0.14)):
            k.tube((0, 0.2, 0), (0, 1.3, 0), 0.09, 'steel', seg=8)
            k.tube((0, 1.3, 0), (0.35, 1.3, 0), 0.09, 'steel', seg=8)
            k.sphere((0, 1.3, 0), 0.1, 'steel', seg=8, rings=4)
    with k.xf(on(xa + 1.9, PI + 0.3, tr + 0.14)):          # fill and drain panel
        k.box((0, 0.12, 0), (2.0, 0.24, 1.4), 'grey', bevel=0.04)
        k.box((0, 0.26, 0), (1.7, 0.05, 1.1), 'hazard')
        for dx in (-0.45, 0.45):
            with k.xf(orient((dx, 0.24, 0), (0, 1, 0))):
                k.can(0, 0.3, 0.26, 'steel', seg=12, ch=0.04)
                k.can(0.3, 0.38, 0.3, 'orange', seg=12, ch=0.02)
    greeble(xa + 1, xb - 1, tr + 0.14, 10, a0=0.9, span=0.4, hmax=0.25, sc=0.4)
    greeble(xa + 1, xb - 1, tr + 0.14, 10, a0=PI + 0.45, span=0.5, hmax=0.25, sc=0.4)
    # --- low-temperature radiators: two panel wings, dorsal and ventral, on headers and stay rods
    lx0, lx1, w = L['x0'], L['x1'], L['w']
    nx, nz = 5, 2
    z0 = L['root'] + 0.3
    for s in (1, -1):
        for i in range(nx):
            pa = lx0 + (lx1 - lx0) * i / nx + 0.06
            pb = lx0 + (lx1 - lx0) * (i + 1) / nx - 0.06
            for j in range(nz):
                za = z0 + w * j / nz + 0.05
                zb = z0 + w * (j + 1) / nz - 0.05
                k.box(((pa + pb) / 2, 0, s * (za + zb) / 2), (pb - pa, 0.06, zb - za), 'radw')
            x = lx0 + (lx1 - lx0) * i / nx
            k.tube((x, 0, s * (z0 - 0.3)), (x, 0, s * (z0 + w)), 0.055 if i else 0.07, 'silver' if i else 'grey', seg=6)
        k.tube((lx1, 0, s * (z0 - 0.3)), (lx1, 0, s * (z0 + w)), 0.07, 'grey', seg=6)
        for j in range(nz + 1):
            z = s * (z0 + w * j / nz)
            k.tube((lx0, 0, z), (lx1, 0, z), 0.05, 'grey', seg=5)
        k.tube((lx0 - 0.4, 0.2, s * L['root']), (lx1 + 1.2, 0.2, s * L['root']), 0.14, 'silver', seg=8)
        k.tube((lx0 - 0.4, -0.2, s * L['root']), (lx1 + 1.2, -0.2, s * L['root']), 0.14, 'white_s', seg=8)
        for x in (lx0 + 1, (lx0 + lx1) / 2, lx1 - 1):
            k.tube((x, 0, s * (tr + 0.1)), (x, 0, s * (L['root'] - 0.1)), 0.16, 'grey', seg=8)
            k.box((x, 0, s * (tr + 0.2)), (0.7, 0.9, 0.3), 'grey', bevel=0.03)
            for sy in (1, -1):
                k.tube((x, 0, s * (z0 + w)), (x, sy * 3.4, s * 6.85), 0.03, 'truss', seg=4)
                k.tube((x, 0, s * (z0 + w / 2)), (x, sy * 2.0, s * 7.4), 0.03, 'truss', seg=4)
        # cryocoolers: compressor pair, cold head, their own little radiator
        with k.xf(move(lx1 + 2.0, 0, s * tr) @ (rotX(0) if s > 0 else rotX(PI))):
            k.box((0, 0, 0.22), (3.2, 2.5, 0.2), 'grey', bevel=0.04)
            for sy in (-0.65, 0.65):
                with k.xf(move(0, sy, 0.8)):
                    k.can(-1.2, 0.5, 0.42, 'white_s', seg=S(16), ch=0.06)
                    k.can(-1.4, -1.2, 0.3, 'grey', seg=12, ch=0.03)
                    k.can(0.5, 0.75, 0.3, 'gold', seg=12, ch=0.03)
            C.zcan(0.3, 1.5, 0.28, 'steel', x=1.15, y=0, seg=12)
            C.zcan(1.5, 1.62, 0.36, 'grey', x=1.15, y=0, seg=12)
            k.box((-0.3, 0, 1.75), (2.3, 1.9, 0.06), 'radw')
            for dx in (-1.3, 0.7):
                for dy in (-0.8, 0.8):
                    k.tube((dx, dy, 0.3), (dx, dy, 1.72), 0.04, 'grey', seg=4)
            k.pipe([(1.15, 0.35, 0.5), (1.9, 0.6, 0.35), (2.6, 0.6, -0.2)], 0.06, 'gold', seg=5)
            k.pipe([(-1.4, 0.2, 0.8), (-1.9, 0.2, 0.9), (-2.1, 0.2, 0.65)], 0.07, 'silver', seg=5)
    # --- forward strut cone: tank ring frame to the hub's fixed aft bearing
    strut_cone(xb, tr + 0.1, A['hub']['x0'] - 0.9, A['hub']['r'] + 0.35, 12, rad=0.12, a0=TAU / 24)


# =====================================================================================
# forward: despun hub, reels, tethers and pods (the turning group), crew module
# =====================================================================================
def pod_frame(a):
    """Local x radial out from the hub axis at angle a, y the direction of travel, z toward the bow."""
    return orient((A['hub']['x'], 0, 0), (0, math.cos(a), math.sin(a)), up=(1, 0, 0))


def build_cradle():
    """One pod's berth on the drum: docking collar, four latch claws, two reels with fairleads."""
    H = A['hub']
    cr = H['cradle']
    k.lathe([(H['r'] - 0.2, 1.25), (H['r'] + 0.25, 0.98), (cr - 0.25, 0.98), (cr - 0.25, 1.22), (cr, 1.22), (cr, 0.8)], 'white_s', seg=S(28), sharp=30)
    k.lathe(C.rrect(cr - 0.34, cr - 0.06, 1.2, 1.3, 0.02, 0.02, 1), 'blue', seg=S(28), closed=True)
    k.lathe([(cr - 0.4, 0.8), (cr - 0.4, 0)], 'dark', seg=S(20))
    for i in range(3):          # guide petals
        with k.xf(rotX(i * TAU / 3 + 0.5)):
            k.box((cr + 0.16, 0.95, 0), (0.34, 0.06, 0.45), 'grey')
    for (y, z) in ((2.2, 2.2), (2.2, -2.2), (-2.2, 2.2), (-2.2, -2.2)):
        top = cr + 1.75
        k.tube((H['r'] - 0.2, y * 0.75, z * 0.75), (top - 0.2, y * 1.04, z * 1.04), 0.13, 'truss', seg=8)
        k.tube((H['r'] + 0.3, y * 0.4, z * 0.4), (cr + 0.5, y * 0.92, z * 0.92), 0.07, 'truss', seg=5)
        k.tube((H['r'] - 0.1, y * 0.95, z * 0.3), (cr + 0.9, y * 0.97, z * 0.97), 0.05, 'truss', seg=5)
        with k.xf(orient((top, y * 1.04, z * 1.04), (0.25, -y, -z))):          # the claw: a hinged pad that closes on the pod's shoulder
            k.box((0.12, 0, 0), (0.5, 0.46, 0.4), 'hazard', bevel=0.04)
            k.box((0.42, 0, 0), (0.12, 0.6, 0.55), 'dark')
        with k.xf(orient((top - 0.25, y * 1.04, z * 1.04), (0, -z, y))):
            k.can(-0.3, 0.3, 0.2, 'grey', seg=10, ch=0.04)
        k.tube((cr - 0.2, y * 0.5, z * 0.5), (top - 0.5, y * 1.0, z * 1.0), 0.06, 'steel', seg=5)          # latch actuator
    for s in (1, -1):
        z = s * H['reel']
        rc = H['r'] + 0.85
        with k.xf(orient((rc, -1.5, z), (0, 1, 0))):          # reel: drum, flanges, motor and brake
            k.can(0, 3.0, 0.62, 'tether', seg=S(20), ch=0.02)
            for x in (-0.07, 3.07):
                k.can(x - 0.07, x + 0.07, 1.0, 'grey', seg=S(24), ch=0.03)
            k.can(3.14, 3.85, 0.42, 'white_s', seg=12, ch=0.05)
            k.can(3.85, 4.05, 0.3, 'gold', seg=10, ch=0.03)
            k.can(-0.6, -0.14, 0.36, 'dark', seg=12, ch=0.04)
        for y in (-1.72, 1.72):          # pillow-block frames
            k.tube((H['r'] - 0.1, y, z - 0.9), (rc, y, z), 0.09, 'truss', seg=6)
            k.tube((H['r'] - 0.1, y, z + 0.9), (rc, y, z), 0.09, 'truss', seg=6)
            with k.xf(orient((rc, y - 0.1, z), (0, 1, 0))):
                k.can(0, 0.2, 0.3, 'grey', seg=10, ch=0.03)
        k.box((H['r'] + 0.06, 0, z), (2.3, 3.7, 0.14), 'grey')
        # level-wind fairlead above the reel
        k.tube((rc + 1.25, -1.5, z), (rc + 1.25, 1.5, z), 0.05, 'steel', seg=6)
        for y in (-1.5, 1.5):
            k.tube((rc + 0.3, y * 1.14, z), (rc + 1.3, y, z), 0.05, 'truss', seg=5)
        k.box((rc + 1.25, 0, z), (0.3, 0.4, 0.34), 'hazard', bevel=0.03)


def build_pod(b, label):
    """A two-deck crew pod, hub-side end at radius b; floors face outward. Local frame from pod_frame()."""
    PD = A['pods']
    PR, F = PD['R'], PD['fin']
    u = lambda v: b + v
    # docking neck: the collar that meets the cradle, hatch inside
    k.lathe([(u(0), 0.8), (u(0), 1.12), (u(0.2), 1.12), (u(0.2), 0.95), (u(1.0), 0.95)], 'grey', seg=S(28), sharp=30)
    k.lathe(C.rrect(u(0.3), u(0.52), 0.94, 1.02, 0.02, 0.02, 1), 'blue', seg=S(28), closed=True)
    k.lathe([(u(0.25), 0.8), (u(0.25), 0)], 'dark', seg=S(20))
    # pressure hull: domed shoulder, barrel, domed floor head (water and stores under the lower deck)
    n = 7 if not game() else 4
    t0 = math.asin(0.95 / PR)
    top = [(u(2.3) - 1.4 * math.cos(t0 + (PI / 2 - t0) * i / n), PR * math.sin(t0 + (PI / 2 - t0) * i / n)) for i in range(n + 1)]
    k.lathe(top, 'white', seg=S(48), sharp=60)
    k.lathe([(u(2.3), PR), (u(6.5), PR)], 'white_m', seg=S(48))
    bot = [(u(6.5) + 1.3 * math.sin(PI / 2 * i / n), PR * math.cos(PI / 2 * i / n)) for i in range(n + 1)]
    k.lathe(bot, 'grey', seg=S(48), sharp=60)
    for uu, mat in ((2.3, 'grey'), (6.5, 'grey')):
        band(u(uu), PR, 0.16, 0.07, mat, seg=48)
    k.lathe(C.rrect(u(PD['deck2']) - 0.14, u(PD['deck2']) + 0.14, PR - 0.02, PR + 0.035, 0.01, 0.01, 1), 'blue', seg=S(48), closed=True)
    for i in range(12):          # stiffening stringers under the blankets
        with k.xf(on(u(4.4), i * TAU / 12 + 0.13, PR)):
            k.box((0, 0.015, 0), (4.0, 0.05, 0.07), 'white_s')
    for i in range(4):
        a = TAU / 8 + i * TAU / 4
        porthole(u(3.0), a, PR, 0.24, 0.7)
        porthole(u(5.3), a, PR, 0.24, 0.6)
    porthole(u(5.3), 0.0, PR, 0.2, 0.5)
    # radiator fins fore and aft, each on two struts with a coolant loop and stays to the shoulder
    for s in (1, -1):
        zc = s * (PR + 0.3 + F['w'] / 2)
        nf = 4
        for i in range(nf):
            fa = F['u0'] + (F['u1'] - F['u0']) * i / nf + 0.03
            fb = F['u0'] + (F['u1'] - F['u0']) * (i + 1) / nf - 0.03
            k.box((u((fa + fb) / 2), 0, zc), (fb - fa, 0.05, F['w']), 'radw')
            if i:
                k.tube((u(fa - 0.03), 0, s * (PR + 0.3)), (u(fa - 0.03), 0, s * (PR + 0.3 + F['w'])), 0.03, 'grey', seg=4)
        k.tube((u(F['u0']), 0, s * (PR + 0.28)), (u(F['u1']), 0, s * (PR + 0.28)), 0.07, 'silver', seg=6)
        k.tube((u(F['u0']), 0, s * (PR + 0.32 + F['w'])), (u(F['u1']), 0, s * (PR + 0.32 + F['w'])), 0.04, 'grey', seg=5)
        for x in (F['u0'] + 0.4, F['u1'] - 0.4):
            k.tube((u(x), 0, s * (PR - 0.05)), (u(x), 0, s * (PR + 0.3)), 0.09, 'grey', seg=6)
        for sy in (1, -1):
            k.tube((u(F['u0'] + 0.2), 0, s * (PR + 0.3 + F['w'])), (u(1.5), sy * 0.9, s * 2.2), 0.02, 'truss', seg=4)
            k.tube((u(F['u1'] - 0.2), 0, s * (PR + 0.3 + F['w'])), (u(6.4), sy * 1.2, s * 2.86), 0.02, 'truss', seg=4)
    # spin thrusters: a pair each side, firing along the direction of travel
    for s in (1, -1):
        with k.xf(move(u(PD['thr']), s * (PR + 0.12), 0)):
            k.box((0, 0, 0), (0.8, 0.5, 1.7), 'white_s', bevel=0.05)
            k.box((0, s * 0.1, 0), (0.5, 0.36, 1.3), 'gold')
            for z in (-0.5, 0.5):
                with k.xf(orient((0, s * 0.25, z), (0, s, 0))):
                    k.lathe([(0, 0.09), (0.12, 0.13), (0.3, 0.22), (0.5, 0.29)], 'bell', seg=S(14, 8), sharp=60)
                    k.lathe([(0.02, 0.085), (0.03, 0)], 'black', seg=8)
        with k.xf(move(u(7.45), s * 2.5, 0)):          # thruster propellant
            k.sphere((0, 0, 0), 0.45, 'gold', seg=S(16), rings=8)
            k.torus(0, 0.46, 0.035, 'grey', seg=S(16), tseg=4)
            k.tube((-0.3, -s * 0.3, 0), (-0.75, -s * 0.2, 0), 0.05, 'truss', seg=5)
    # bridle lugs on the shoulder, hatch and rails on the trailing side, whip antenna, running light
    for (y, z) in ((1.35, 1.35), (1.35, -1.35), (-1.35, 1.35), (-1.35, -1.35)):
        with k.xf(orient((u(1.2), y, z), (-1.0, y * 0.45, z * 0.45))):
            k.box((0.08, 0, 0), (0.34, 0.26, 0.26), 'grey', bevel=0.03)
            k.can(0.2, 0.3, 0.1, 'steel', seg=8, ch=0.02)
    small_hatch(u(5.2), PI, PR, 0.52)
    for a in (PI - 0.36, PI + 0.36):
        C.hrail(u(2.6), u(6.3), PR, a)
    with k.xf(on(u(2.75), PI, PR)):
        k.box((0, 0.2, 0), (0.5, 0.4, 0.6), 'white_s', bevel=0.03)          # tether power and data junction
    k.tube((u(7.6), 0.3, -1.3), (u(9.2), 0.3, -1.5), 0.02, 'grey', seg=4)
    k.box((u(7.84), 0, 0), (0.1, 0.16, 0.16), 'navW')
    w = 0.62 * text_aspect(label) / (PR + 0.03)
    C.arc_decal('pod_' + label[-1].lower(), label, u(3.35), u(2.73), PR + 0.03, w / 2, -w / 2, seg=10)
    C.arc_decal('pod_' + label[-1].lower(), label, u(3.35), u(2.73), PR + 0.03, PI + w / 2, PI - w / 2, seg=10)
    greeble(u(2.6), u(6.2), PR, 5, a0=0.25, span=0.4, hmax=0.2, sc=0.3)
    greeble(u(2.6), u(6.2), PR, 5, a0=-0.65, span=0.4, hmax=0.2, sc=0.3)


def build_tether(b, stowed=False):
    """Two strands from the two reels to yokes either side of the pod's neck, cross-ties, and the power line."""
    H = A['hub']
    rs = 0.035 if not game() else 0.07
    top = H['r'] + 0.85 + 1.25
    if stowed:          # wound home: only the last metre of each strand and its bridle are off the reel
        for s in (1, -1):
            p0, p1 = Vector((top, 0, s * H['reel'])), Vector((b + 0.75, 0, s * 2.5))
            k.tube(p0, p1, rs, 'tether', seg=5)
            k.box(tuple(p1), (0.3, 0.2, 0.3), 'grey', bevel=0.03)
            for y in (1.35, -1.35):
                k.tube(p1, (b + 1.12, y * 0.98, s * 1.32), rs * 0.8, 'tether', seg=4)
        k.pipe([Vector((H['r'] + 0.3, -0.5, 2.3)), Vector((b + 0.6, -1.6, 1.6)), Vector((b + 2.75, -3.3, 0))], 0.03, 'yellow', seg=4, elbow=False)
        return
    ya = b - 1.3
    for s in (1, -1):
        p0, p1 = Vector((top, 0, s * H['reel'])), Vector((ya, 0, s * 1.5))
        k.tube(p0, p1, rs, 'tether', seg=5)
        k.box(tuple(p1), (0.3, 0.2, 0.3), 'grey', bevel=0.03)          # yoke plate
        for y in (1.35, -1.35):
            k.tube(p1, (b + 1.12, y * 0.98, s * 1.32), rs * 0.8, 'tether', seg=4)
    n = 6
    for i in range(1, n):          # cross-ties with dampers keep the strands apart and kill sway
        f = i / n
        r = top + (ya - top) * f
        z = H['reel'] + (1.5 - H['reel']) * f
        k.tube((r, 0, -z), (r, 0, z), 0.018 if not game() else 0.0, 'tether', seg=4)
        k.box((r, 0, 0), (0.3, 0.24, 0.5), 'grey', bevel=0.03)
        for s in (1, -1):
            k.box((r, 0, s * z), (0.22, 0.14, 0.14), 'dark')
    # power and data line: from the slip-ring box on the drum, along the forward strand, to the junction on the pod
    pts = [Vector((H['r'] + 0.3, -0.5, 2.3)), Vector((top, -0.12, H['reel'])), Vector((ya, -0.12, 1.5)), Vector((b + 1.6, -1.9, 0.5)), Vector((b + 2.75, -3.3, 0))]
    k.pipe(pts, 0.03 if not game() else 0.066, 'yellow', seg=4, elbow=False)


def build_spin(stowed=False):
    """Everything that turns: the drum between the bearing races, both cradles, reels, tethers and pods.
    stowed: the burn configuration, pods wound home into their cradles and the drum stopped."""
    H, PD = A['hub'], A['pods']
    C.spin_group('RingPods', H['x'], PD['g'], PD['r'], PD['dir'])
    with k.xf(move(H['x'], 0, 0) @ rotX(0.0 if stowed else PHASE) @ move(-H['x'], 0, 0)):
        x0, x1, r = H['x0'], H['x1'], H['r']
        k.lathe([(x0 + 0.05, r - 0.5), (x0 + 0.05, r - 0.15), (x0 + 0.2, r), (x1 - 0.2, r), (x1 - 0.05, r - 0.15), (x1 - 0.05, r - 0.5)], 'white', seg=S(72))
        for x in (x0 + 0.55, x1 - 0.55):
            k.lathe(C.rrect(x - 0.22, x + 0.22, r - 0.02, r + 0.05, 0.01, 0.01, 1), 'blue', seg=S(72), closed=True)
        band(H['x'], r, 0.2, 0.08, seg=72)
        for s in (1, -1):          # the sides between the pods: slip-ring boxes, spin balance tanks, rails
            for da in (-0.5, 0.5):
                greeble(x0 + 1.0, x1 - 1.0, r, 10, a0=(0 if s > 0 else PI) + da - 0.3, span=0.6, hmax=0.3, sc=0.42)
            a = 0 if s > 0 else PI
            C.gbox(H['x'] + 2.3, a, r, 1.6, 1.3, 0.55, 'white_s', bevel=0.05)
            C.gbox(H['x'] - 2.2, a, r, 1.2, 1.6, 0.4, 'gold', bevel=0.04)
            for da in (-0.42, 0.42):
                C.hrail(x0 + 1.0, x1 - 1.0, r, a + da)
                with k.xf(on(H['x'], a + da * 1.9, r)):
                    k.sphere((-2.6, 0.5, 0), 0.52, 'silver', seg=S(14), rings=7)
                    k.box((-2.6, 0.05, 0), (0.8, 0.1, 0.8), 'grey')
        base = H['cradle'] if stowed else PD['r'] - PD['floor']
        for i, a in enumerate((PI / 2, 3 * PI / 2)):
            with k.xf(pod_frame(a)):
                build_cradle()
                build_tether(base, stowed)
                build_pod(base, 'POD ' + 'AB'[i])
    k.group = 'Hull'


def build_fwd(stowed=False):
    H, CM, T = A['hub'], A['crew'], A['tank']
    r = CM['r']
    # --- despun hub: the fixed core tube, a bearing race at each end, the turning drum between them
    k.lathe([(T['x1'] - 0.6, H['core']), (CM['x0'] + 0.2, H['core'])], 'grey', seg=S(40))
    for x in (H['x0'] - 0.5, H['x1'] + 0.5):
        d = 1 if x > H['x'] else -1
        k.lathe(C.rrect(x - 0.5, x + 0.5, H['r'] - 0.75, H['r'] + 0.7, 0.2, 0.3, 3), 'grey', seg=S(72), closed=True, sharp=30)
        k.lathe(C.rrect(x + d * 0.5 - 0.1, x + d * 0.5 + 0.1, H['core'] - 0.02, H['core'] + 0.3, 0.04, 0.04, 1), 'grey', seg=S(40), closed=True)
        k.lathe(C.rrect(x - 0.12, x + 0.12, H['r'] + 0.68, H['r'] + 0.8, 0.03, 0.03, 1), 'dark', seg=S(72), closed=True)
        for j in range(16):
            C.gbox(x, j * TAU / 16, H['r'] + 0.7, 0.5, 0.5, 0.16, 'grey', bevel=0.0)
        for j in range(12):          # V struts from the core to the race
            a = j * TAU / 12
            for da in (-0.5, 0.5):
                k.tube(P(x + d * 0.45, H['core'] + 0.1, a), P(x + d * 0.2, H['r'] - 0.7, a + da * TAU / 12), 0.09, 'truss', seg=6)
    build_spin(stowed)
    # --- crew module: aft dome, priority cargo bay, flight deck with storm shelter, nose
    n = 6 if not game() else 3
    dome = [(53.3 - 1.3 * math.cos(PI / 2 * i / n), H['core'] + (r - H['core']) * math.sin(PI / 2 * i / n)) for i in range(n + 1)]
    k.lathe(dome, 'white', seg=S(64), sharp=60)
    b0, b1 = 53.3, CM['bay'][1]
    k.lathe([(b0, r), (b1, r)], 'white', seg=S(64))
    k.lathe([(b1, r), (CM['deck'][1], r)], 'white_m', seg=S(64))
    for x in (b0, b1):
        band(x, r, 0.24, 0.1)
    # cargo hatches port and starboard: plug doors on outside rails
    for a in (0.0, PI):
        da = 1.8 / r
        C.arc_plate(53.9, 57.9, r + 0.0, r + 0.07, a - da, a + da, 'grey', seg=6)
        C.arc_plate(54.08, 57.72, r + 0.06, r + 0.12, a - da + 0.04, a + da - 0.04, 'white_s', seg=6)
        for x in (54.0, 57.8):
            C.arc_plate(x - 0.09, x + 0.09, r + 0.1, r + 0.14, a - da, a + da, 'hazard', seg=6)
        for sa in (-1, 1):
            with k.xf(on(55.9, a + sa * (da + 0.07), r)):
                k.box((1.1, 0.08, 0), (6.6, 0.1, 0.12), 'grey')
            with k.xf(on(54.6, a + sa * da * 0.82, r + 0.12)):
                k.box((0, 0.04, 0), (0.3, 0.1, 0.5), 'dark')
                k.box((2.6, 0.04, 0), (0.3, 0.1, 0.5), 'dark')
        hull_decal('arm_bay', 'PRIORITY CARGO', 55.9, r + 0.13, a + (0.17 if a == 0 else -0.17), 0.5, seg=3)
    # storm shelter: the flight deck's thick-walled refuge, a water jacket outside the hull
    k.lathe(C.rrect(59.9, 62.6, r - 0.02, r + 0.16, 0.06, 0.06, 2), 'white_s', seg=S(64), closed=True)
    k.lathe(C.rrect(59.05, 59.55, r - 0.02, r + 0.04, 0.01, 0.01, 1), 'blue', seg=S(64), closed=True)
    for x in (62.9, 64.9):
        band(x, r, 0.18, 0.08)
    # nose: a rounded cone with ten framed windows, then a small forward dome
    k.lathe([(CM['deck'][1], r), (65.25, r - 0.07), (65.5, r - 0.24), (67.3, 2.86), (67.6, 2.6), (67.75, 2.52)], 'white', seg=S(64), sharp=40)
    k.lathe([(67.75, 2.52), (68.2, 2.2), (68.5, 1.6), (68.62, 0.8), (68.65, 0)], 'white_s', seg=S(48), sharp=60)
    band(67.75, 2.5, 0.14, 0.07, seg=48)
    slope = math.atan2(4.36 - 2.86, 67.3 - 65.5)
    for i in range(12):
        a = (i + 0.5) * TAU / 12
        if i in (7, 10):          # the ventral pair is blanked off behind the antenna mast
            continue
        with k.xf(on(66.3, a, 3.7) @ rotZ(-slope)):
            k.box((0, 0.025, 0), (0.78, 0.05, 0.7), 'grey')
            k.box((0, 0.05, 0), (0.56, 0.04, 0.5), 'glass' if R.random() < 0.5 else 'glass_dark')
    for a in (PI / 2 + 0.5, PI / 2 - 0.5, -PI / 2 + 0.45, -PI / 2 - 0.45):
        porthole(63.8, a, r, 0.28, 0.6)
        porthole(61.2, a, r + 0.16, 0.22, 0.4)
    # --- Whipple shield at the bow: two layers of bumper petals on a spider
    for i in range(6):
        a = i * TAU / 6 + TAU / 12
        k.tube(P(68.2, 2.2, a), P(69.45, 4.7, a), 0.07, 'truss', seg=6)
        k.tube(P(68.55, 1.2, a), P(69.45, 2.4, a), 0.05, 'truss', seg=5)
        k.tube(P(69.45, 4.7, a), P(69.45, 4.7, a + TAU / 6), 0.05, 'truss', seg=5)
        k.tube(P(69.45, 2.4, a), P(69.45, 2.4, a + TAU / 6), 0.04, 'truss', seg=4)
    for (r0, r1, nn) in ((0.3, 1.7, 6), (1.76, 3.35, 12), (3.41, 5.0, 18)):
        for i in range(nn):
            a0 = (i + 0.02) * TAU / nn
            a1 = (i + 0.98) * TAU / nn
            C.arc_plate(69.55, 69.61, r0, r1, a0, a1, 'bumper', seg=3)
            C.arc_plate(69.93, 69.99, r0 + 0.03, r1 - 0.03, a0 + 0.006, a1 - 0.006, 'bumper', seg=3)
    for i in range(18):
        for rr in (1.4, 3.0, 4.7):
            k.tube(P(69.6, rr, i * TAU / 18), P(69.95, rr, i * TAU / 18), 0.025, 'grey', seg=4)
    k.can(69.3, 70.0, 0.22, 'grey', seg=10, ch=0.04)
    k.box((70.05, 0, 0), (0.12, 0.2, 0.2), 'navW')
    C.LIGHTS.append(('POINT', (70.6, 0, 0), None, 12, (1, 1, 1), 0))
    # --- dorsal docking port above the cargo bay
    with k.xf(orient((CM['port'], 0, r - 0.15), (0, 0, 1))):
        k.lathe([(0, 1.25), (0.5, 1.1), (1.25, 1.1), (1.25, 1.3), (1.6, 1.3), (1.6, 0.85)], 'white_s', seg=S(28), sharp=30)
        k.lathe(C.rrect(0.75, 1.0, 1.08, 1.16, 0.02, 0.02, 1), 'blue', seg=S(28), closed=True)
        k.lathe([(1.3, 0.85), (1.3, 0)], 'dark', seg=S(20))
        for i in range(3):
            with k.xf(rotX(i * TAU / 3 + 0.3)):
                k.box((1.8, 1.02, 0), (0.44, 0.06, 0.5), 'grey')
        for a in (0.5, 2.6, 4.7):
            with k.xf(rotX(a)):
                k.tube((0.35, 1.32, 0), (1.2, 1.32, 0), 0.03, 'yellow', seg=4)
    k.tube((CM['port'] + 1.5, 0, r), (CM['port'] + 1.5, 0, r + 1.7), 0.03, 'grey', seg=4)          # docking target and lamp
    k.box((CM['port'] + 1.5, 0, r + 1.75), (0.06, 0.5, 0.5), 'dark')
    C.floodlight((CM['port'] - 1.6, 0.8, r + 0.5), (CM['port'], 0, r + 4), energy=500, spot=1.2)
    # --- high-gain dish on a ventral mast, laser terminal on the dorsal side
    C.girder((61, 0, -r + 0.05), (61, 0, -7.4), 0.5, bay=0.6, rc=0.045, rd=0.022)
    k.tube((61, 0, -r + 0.05), (61, 0, -7.4), 0.07, 'grey', seg=6)
    k.box((61, 0, -7.55), (0.7, 0.7, 0.4), 'grey', bevel=0.04)
    with k.xf(orient((61, 0, -7.9), (0.6, 0, -0.8))):
        k.can(-0.3, 0.1, 0.4, 'gold', seg=12, ch=0.04)
        with k.xf(move(0.6, 0, 0) @ Matrix.Scale(0.56, 4)):
            C.dish()
    k.box((63.0, 0, r + 0.25), (1.2, 1.0, 0.5), 'white_s', bevel=0.05)
    C.zcan(r + 0.5, r + 0.75, 0.42, 'grey', x=63.0, seg=14)
    with k.xf(orient((63.0, 0, r + 1.15), (0.6, 0, 0.8))):
        k.can(-0.55, 0.75, 0.36, 'white_s', seg=S(16), ch=0.04)
        k.can(0.75, 0.82, 0.39, 'dark', seg=S(16), ch=0.02)
        k.lathe([(0.76, 0.3), (0.76, 0)], 'glass_dark', seg=12)
        k.box((0.0, 0.5, 0), (0.5, 0.28, 0.4), 'gold')
    for a in (0.5, 2.6):          # whip antennas
        with k.xf(on(66.6, a, 3.45)):
            k.tube((0, 0, 0), (0.6, 1.7, 0), 0.02, 'grey', seg=4)
    for a in (1.05, 2.1, 4.2, 5.25):          # star trackers
        with k.xf(on(64.2, a, r) @ orient((0, 0, 0), (0.4, 1, 0))):
            k.can(0, 0.5, 0.15, 'dark', seg=8, ch=0.02)
    # --- navigation lights, forward RCS, rails, hatches, equipment
    for s in (1, -1):
        k.tube((63.8, s * (r - 0.1), 0), (63.8, s * 5.8, 0), 0.06, 'grey', seg=6)
        k.box((63.8, s * 5.95, 0), (0.3, 0.3, 0.2), 'navR' if s > 0 else 'navG')
        C.LIGHTS.append(('POINT', (63.8, s * 6.6, 0), None, 14, (1, 0.05, 0.03) if s > 0 else (0.05, 1, 0.2), 0))
    for q in range(4):
        quad_on_stalk(61.25, TAU / 8 + q * TAU / 4, r + 0.14, 6.0, sc=0.4)
    small_hatch(63.75, 3.6, r, 0.5)
    for a in (0.62, 2.52, 3.76, 5.66):
        C.hrail(53.6, 58.3, r, a)
    for a in (1.2, 1.95, 4.3, 5.1):
        C.hrail(59.0, 64.8, r + 0.16, a)
    greeble(62.8, 64.7, r, 12, hmax=0.25, sc=0.36)
    greeble(53.6, 58.2, r, 10, a0=PI / 2 + 0.5, span=0.5, hmax=0.25, sc=0.36)
    greeble(53.6, 58.2, r, 12, a0=-PI / 2 - 0.5, span=1.0, hmax=0.25, sc=0.36)
    for a in (4.45, 4.95):
        C.axial_pipe(b0 + 0.2, CM['deck'][1] - 0.2, r + 0.16, a, 0.06, 'gold' if a < 4.6 else 'silver', clamp=1.6)
    # lamps that light the cradles for winding the pods home
    for s in (1, -1):
        C.floodlight((52.3, s * 3.6, 2.0), (46, s * 2.0, 9.0 * math.cos(PHASE)), energy=350, spot=1.3)


def build(parts, drive, landers):
    if 'aft' in parts:
        build_aft(drive)
        if drive:
            C.PLUMES.append(dict(x=-64.0, length=230.0, r0=2.2, r1=30.0))
    if 'mid' in parts:
        build_mid()
    if 'fwd' in parts:
        build_fwd(stowed=drive)          # pods are wound home before every burn


def shot(tgt, cam, dist, lens, roll, sun_world, eoff, **kw):
    """A SHOTS entry with the sun given in ship coordinates (the pipeline wants it in camera space)."""
    d = Vector(cam).normalized()
    CM = (-d).to_track_quat('-Z', 'Y').to_matrix() @ Matrix.Rotation(math.radians(roll), 3, 'Z')
    sun = tuple(CM.inverted() @ Vector(sun_world).normalized())
    return dict(tgt=tuple(tgt), cam=tuple(d), dist=dist, lens=lens, roll=roll, sun=sun, eoff=eoff, alt=kw.pop('alt', 9500), **kw)


def _pod(i=0):
    """Unit vectors at pod i: radial out, direction of travel; and the hub centre."""
    a = PI / 2 + i * PI + PHASE
    return Vector((0, math.cos(a), math.sin(a))), Vector((0, -math.sin(a), math.cos(a))), Vector((A['hub']['x'], 0, 0))


_r, _t, _h = _pod(0)
_bow = Vector((1, 0, 0))
_E = lambda key, d: float(os.environ.get('ARM_' + key, d))
SHOTS = {
    'hero':  shot((6, 0, 6), (0.62, -0.72, 0.30), _E('DIST', 288), 70, _E('ROLL', -35), (0.35, -0.25, 0.9), (-10, -19)),
    'pod':   shot(_h + _r * 31 + _bow * 2, _r * 0.86 + _bow * 0.30 - _t * 0.40, _E('DIST', 48), 28, _E('ROLL', -75), _r * 0.75 + _bow * 0.45 - _t * 0.5, (_E('EX', 25), _E('EY', -26)), elon=120),
    'stern': shot((-20, 0, 0), (-0.80, -0.52, 0.30), _E('DIST', 185), 60, _E('ROLL', 4), (-0.3, -0.45, 0.84), (_E('EX', 10), _E('EY', -25))),
    'hub':   shot((49, 0, 2), (0.42, -0.80, 0.42), _E('DIST', 52), 45, _E('ROLL', -3), (0.4, -0.2, 0.9), (_E('EX', 8), _E('EY', -25)), elon=165),
    # working views (no Earth)
    'side':  shot((0, 0, 0), (0.0, -1.0, 0.06), 420, 100, 0, (0.3, -0.6, 0.74), None),
    'aft':   shot((-52, 0, 0), (-0.25, -0.85, 0.45), 62, 50, 0, (-0.1, -0.6, 0.8), None),
    'spine': shot((-22, 0, 0), (0.15, -0.55, 0.82), 60, 50, 0, (0.3, -0.3, 0.9), None),
    'mid':   shot((18, 0, 0), (0.35, -0.85, 0.40), 95, 50, 0, (0.4, -0.5, 0.77), None),
    'cradle': shot(_h + _r * 7, _r * 0.5 + _bow * 0.35 - _t * 0.8, 32, 50, 0, _r * 0.7 + _bow * 0.4 - _t * 0.6, None),
    'bow':   shot((60, 0, 0), (0.70, -0.62, 0.35), 48, 50, 0, (0.5, -0.4, 0.77), None),
    'podc':  shot(_h + _r * 42, -_r * 0.25 + _bow * 0.55 - _t * 0.8, 24, 50, 0, -_r * 0.3 + _bow * 0.5 - _t * 0.8, None),
    'podd':  shot(_h + _r * 42, _r * 0.5 - _bow * 0.45 + _t * 0.75, 24, 50, 0, _r * 0.6 - _bow * 0.3 + _t * 0.75, None),
}

if __name__ == '__main__':
    C.main(build=build, shots=SHOTS, ship_id='armstrong')
