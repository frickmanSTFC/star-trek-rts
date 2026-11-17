"""Tereshkova-class long-range survey vessel (310 m): high-detail model, path-traced stills and the game model.

Built on the Cochrane pipeline (blender/prewarp_ships.md). Layout and sizes follow the class page:
x toward the bow, y to port, z dorsal; metres; the hull runs from x -155 (nozzle) to x +155 (bow antenna).

  /home/user/.bpyvenv/bin/python blender/build_tereshkova.py --shot hero --out blender/previews/tereshkova \
      --tmp <scratch> --earth-tex <bmng.jpg>
  --shot hero|ring|stern|science|side   --drive (reactor at full power, plume)   --only aft,mid,fwd
  --game game/models/ships/tereshkova.glb   writes the game model (node Ship: Hull + RingH)

What the rebuild changed against the old low-detail model is listed in blender/previews/tereshkova/changes.json.
"""
import sys, os, math
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_cochrane as C          # imports bpy, which must come before mathutils
from mathutils import Matrix, Vector
import cochrane_kit as K
from build_cochrane import k, on, P, R, rotX, rotY, rotZ, move, orient, TAU, PI

NAME, REG = 'TERESHKOVA', 'UE-16'

# design constants of the class page (TER_D)
T = dict(
    ring=dict(r=60, rIn=52, w=16, g=0.6, n=4, dir=1), hub=dict(x0=-22, x1=22, r=9.0), collar=11.2,
    fly=dict(x0=-30, x1=-22, r=9.6),
    coils=[(-154.2, 9.0, 0.8), (-151.2, 7.2, 0.7), (-148.4, 5.6, 0.62), (-146.0, 4.4, 0.55)],
    reactor=dict(x0=-143, x1=-126, r=4.5), conv=dict(x0=-126, x1=-122, r=6.0), fission=dict(x0=-122, x1=-119, r=3.4),
    shield=dict(x0=-119, x1=-115, r0=7.0, r1=12.0),
    htr=dict(x0=-113.0, len=44.0, r0=3.5, w0=8.0, w1=20.0, n=4),
    truss=dict(x0=-69, x1=118, h=4.0),
    tanks=dict(n=6, r=3.0, x0=-60.0, x1=-43.0, rc=9.5),
    dewar=dict(x=-37.0, rHe=1.6, rD=1.0, z=7.6),
    cmd=dict(x0=22, x1=38, r=7.0),
    ltr=dict(x0=42.0, len=56.0, w=38.0, y0=5.5),
    hga=dict(D=12.0, o=(62, 0, 21), dir=(-0.5, 0, 0.866)),
    xband=dict(D=3.5, o=(50, 0, -8.6), dir=(-0.5, 0, -0.866)),
    scope=dict(D=3.0, len=11.0, o=(124.5, 0, 10.6), dir=(1, 0, 0.1)),
    sample=dict(x0=80, x1=96, r=3.2, z=9.5),
    probes=dict(x0=100, x1=118, xs=(103, 109, 115), rail=(80, 118), railZ=-8.4),
    bow=dict(x0=118, x1=146, r=5.5), mag=dict(x=139, y0=5.5, len=40.0), whipple=dict(x=150.6, r=6.5),
)

# the ship's own materials: (Cycles factory, game spec)
C.EXTRA['ablator'] = (lambda: K.plain('ablator', (0.30, 0.20, 0.12), rough=0.85, grime=0.3, bump=0.01),
                      ('sim', (0.30, 0.20, 0.12), 0.0, 0.85))
C.EXTRA['grow'] = (lambda: K.emit('grow', (1.0, 0.45, 0.82), 0.3, base=(0.02, 0.02, 0.03)),
                   ('emit', (1.0, 0.45, 0.82), 0.6))
C.EXTRA['shade'] = (lambda: K.panel('shade', (0.80, 0.78, 0.74), (0.74, 0.72, 0.69), size=(2.4, 9.5), seam=0.02, seam_mul=0.7,
                                    rough=0.38, metal=0.3, wob=0.03, wrinkle=0.02),
                    ('pan', (0.80, 0.78, 0.74), (0.74, 0.72, 0.69), 0.7, (2.4, 9.5), 0.3, 0.4))


# ------------------------------------------------------------------ helpers
def greeb(x0, x1, r, n, a0=0.0, span=TAU, hmax=0.5, s=0.7, mats=C.GM):
    """Equipment on a hull, sized for this ship's slimmer modules."""
    if k.lod < 1:
        n = n // 2
    for _ in range(n):
        x = x0 + R.random() * (x1 - x0)
        a = a0 + R.random() * span
        t = R.random()
        if t < 0.74:
            C.gbox(x, a, r, s * (0.6 + R.random() * 2.6), s * (0.5 + R.random() * 1.8), 0.12 + R.random() * hmax, R.choice(mats))
        elif t < 0.88:
            L, rad = s * (1.0 + R.random() * 2.2), s * (0.25 + R.random() * 0.4)
            with k.xf(on(x, a, r + rad * 0.85)):
                k.can(-L / 2, L / 2, rad, R.choice(['silver', 'gold', 'white_s', 'grey']), ch=0.05, seg=12)
        elif t < 0.95:
            rad = s * (0.45 + R.random() * 0.5)
            with k.xf(on(x, a, r)):
                k.sphere((0, rad * 0.9, 0), rad, R.choice(['silver', 'gold']), seg=14, rings=8)
                k.box((0, 0.08, 0), (rad * 1.5, 0.16, rad * 1.5), 'grey')
        else:
            h = 1.0 + R.random() * 1.6
            with k.xf(on(x, a, r)):
                k.tube((0, 0, 0), (0, h, 0), 0.04, 'grey', seg=5)
                k.box((0, h, 0), (0.2, 0.2, 0.2), 'dark')


def band(x0, x1, r, mat='grey', h=0.2, seg=64):
    k.lathe(C.rrect(x0, x1, r - 0.03, r + h, 0.04, 0.04, 1), mat, seg=seg, closed=True)


def window(x, a, r, w=0.9, h=0.9, lit=0.55):
    with k.xf(on(x, a, r)):
        k.box((0, 0.02, 0), (w + 0.3, 0.08, h + 0.3), 'grey')
        k.box((0, 0.05, 0), (w, 0.07, h), 'glass' if R.random() < lit else 'glass_dark')


def side_decal(name, text, xc, length, height, r, side, zc=0.0):
    """Lettering along the hull, upright and readable from outside: side -1 starboard, +1 port."""
    n = 6
    ac = math.asin(zc / r)
    xa, xb = (xc - length / 2, xc + length / 2) if side < 0 else (xc + length / 2, xc - length / 2)
    v, f, uv = [], [], []
    for j in range(n + 1):
        e = ac + (j / n - 0.5) * height / r
        y, z = side * r * math.cos(e), r * math.sin(e)
        v.append((xa, y, z)); v.append((xb, y, z))
    for j in range(n):
        f.append((2 * j, 2 * j + 1, 2 * j + 3, 2 * j + 2))
        uv.append([(0, j / n), (1, j / n), (1, (j + 1) / n), (0, (j + 1) / n)])
    k.add('decal_' + name, v, f, uv)
    C.DECALS.append((name, text))


def astronaut(M_, reach=True):
    """Suited figure for scale: boots at the local origin, +y up, facing +x. Stills only."""
    if k.lod < 1:
        return
    with k.xf(M_):
        for z, kx in ((0.12, 0.06), (-0.12, 0.15)):
            k.tube((0, 0.06, z), (kx, 0.48, z), 0.085, 'white_s', seg=8)
            k.tube((kx, 0.48, z), (0, 0.9, z * 0.85), 0.1, 'white_s', seg=8)
            k.box((0.05, 0.05, z), (0.3, 0.12, 0.15), 'grey')
        k.box((0, 1.2, 0), (0.3, 0.62, 0.46), 'white_s', bevel=0.06)
        k.box((-0.27, 1.25, 0), (0.24, 0.72, 0.5), 'white_s', bevel=0.04)
        k.box((0.17, 1.2, 0), (0.06, 0.24, 0.26), 'grey')
        k.sphere((0.02, 1.69, 0), 0.18, 'white_s', seg=14, rings=8)
        k.sphere((0.09, 1.69, 0), 0.135, 'gold', seg=12, rings=6)
        k.pipe([(0, 1.43, 0.28), (0.24, 1.2, 0.35), (0.5, 1.3, 0.3)], 0.07, 'white_s', seg=6)
        if reach:
            k.pipe([(0, 1.43, -0.28), (0.2, 1.58, -0.36), (0.44, 1.85, -0.3)], 0.07, 'white_s', seg=6)
        else:
            k.pipe([(0, 1.43, -0.28), (0.2, 1.15, -0.36), (0.46, 1.2, -0.28)], 0.07, 'white_s', seg=6)
        k.box((-0.05, 1.32, 0.26), (0.2, 0.08, 0.04), 'orange')


def truss(x0, x1, h, nb, rl=0.22, rf=0.13, rd=0.10):
    """Square tube truss with node fittings: four longerons, frames, one diagonal a face, a thin counter-rod."""
    cs = [(-h, -h), (h, -h), (h, h), (-h, h)]
    for c in cs:
        k.tube((x0, c[0], c[1]), (x1, c[0], c[1]), rl, 'truss', seg=8)
    for i in range(nb + 1):
        x = x0 + (x1 - x0) * i / nb
        for j in range(4):
            c, c2 = cs[j], cs[(j + 1) % 4]
            k.tube((x, c[0], c[1]), (x, c2[0], c2[1]), rf, 'truss', seg=6)
            k.sphere((x, c[0], c[1]), 0.42, 'grey', seg=10, rings=5)
            with k.xf(move(x, c[0], c[1])):
                k.can(-0.65, 0.65, 0.31, 'grey', seg=10, ch=0.05)
        a, b = (cs[0], cs[2]) if i % 2 else (cs[1], cs[3])
        k.tube((x, a[0], a[1]), (x, b[0], b[1]), 0.08, 'truss', seg=5)
        if i < nb:
            x2 = x0 + (x1 - x0) * (i + 1) / nb
            for j in range(4):
                c, c2 = cs[j], cs[(j + 1) % 4]
                p, q = (c, c2) if (i + j) % 2 else (c2, c)
                k.tube((x, p[0], p[1]), (x2, q[0], q[1]), rd, 'truss', seg=6)
                k.tube((x, q[0], q[1]), (x2, p[0], p[1]), 0.04, 'truss', seg=4)


def utilities(x0, x1, step):
    """Pipe and cable run inside the truss, on hangers."""
    k.tube((x0, 1.6, 1.6), (x1, 1.6, 1.6), 0.42, 'silver', seg=12)
    k.tube((x0, -1.6, -1.6), (x1, -1.6, -1.6), 0.3, 'gold', seg=10)
    k.tube((x0, -1.7, 1.6), (x1, -1.7, 1.6), 0.2, 'white_s', seg=8)
    k.tube((x0, 1.7, -1.6), (x1, 1.7, -1.6), 0.2, 'white_s', seg=8)
    k.boxX(x0, x1, 2.5, -3.55, 0.9, 0.22, 'dark')
    k.boxX(x0, x1, -2.5, 3.55, 0.9, 0.22, 'dark')
    x = x0 + step / 2
    while x < x1:
        k.box((x, 0, 0), (0.2, 7.6, 0.2), 'grey')
        k.box((x, 0, 0), (0.2, 0.2, 7.6), 'grey')
        for (y, z, r_) in ((1.6, 1.6, 0.52), (-1.6, -1.6, 0.4)):
            with k.xf(move(x, y, z)):
                k.can(-0.12, 0.12, r_, 'grey', seg=10, ch=0.03)
        x += step


# =====================================================================================
# aft: magnetic nozzle, mirror reactor, converter, backup fission core, shadow shield, hot radiators
# =====================================================================================
def build_aft(drive):
    coils = T['coils']
    # --- magnetic nozzle: four superconducting coils in cryostats, hung inside a strut cone
    for (x, r, w) in coils:
        k.lathe(C.rrect(x - w, x + w, r - w, r + w, 0.22, 0.22, 3), 'steel', seg=64, closed=True, sharp=30)
        k.lathe(C.rrect(x - w * 0.5, x + w * 0.5, r - w - 0.07, r + w + 0.07, 0.1, 0.1, 2), 'gold', seg=64, closed=True, sharp=30)
        for j in range(12):
            with k.xf(on(x, (j + 0.5) * TAU / 12, r + w)):
                k.box((0, 0.1, 0), (w * 2.2, 0.28, 0.6), 'grey', bevel=0.04)

    def cone_r(x):
        return 10.0 - (x + 154.2) * (5.0 / 11.0)
    for i in range(12):
        a, a2 = i * TAU / 12, (i + 1) * TAU / 12
        k.tube(P(-154.2, 10.0, a), P(-143.2, 5.0, a), 0.16, 'truss', seg=6)
        for (x, r, w) in coils[1:]:
            k.tube(P(x, r + w, a), P(x, cone_r(x), a), 0.07, 'truss', seg=4)
        for (xa, ra, wa), (xb, rb, wb) in zip(coils, coils[1:]):
            p, q = (a, a2) if i % 2 else (a2, a)
            k.tube(P(xa, cone_r(xa), p), P(xb, cone_r(xb), q), 0.07, 'truss', seg=4)
    k.torus(-154.2, 10.0, 0.14, 'truss', seg=48, tseg=6)
    for a in (0.6, 2.7, 4.8):          # helium feed lines to the cryostats
        k.pipe([P(-153.6, 10.4, a), P(-146, 7.0, a), P(-143.4, 7.6, a), P(-127, 7.6, a)], 0.12, 'gold', seg=6)
    # throat and plasma dump
    k.lathe([(-146.4, 2.2), (-145.4, 2.5), (-144.2, 3.1), (-143.0, 3.9)], 'bell', seg=40, sharp=60)
    k.lathe([(-146.3, 2.12), (-145.4, 2.4), (-143.1, 3.7)], 'black', seg=40, sharp=60)
    k.lathe([(-143.3, 3.7), (-143.3, 0)], 'plasma' if drive else 'black', seg=28)
    # --- D-He3 mirror reactor: vacuum vessel, solenoid coils, bigger mirror coils at the ends, cage, injectors
    Rx = T['reactor']
    k.lathe([(-143, 0), (-143, 4.2), (-142.7, 4.5), (-126.3, 4.5), (-126, 4.2), (-126, 0)], 'vessel', seg=40)
    k.lathe(C.rrect(-143.7, -142.9, 4.4, 5.4, 0.1, 0.1, 1), 'grey', seg=48, closed=True)
    for i in range(6):
        x = -141.4 + i * 2.75
        w, h = (0.95, 1.15) if i in (0, 5) else (0.72, 0.8)
        k.lathe(C.rrect(x - w, x + w, 4.55, 4.55 + 2 * h, 0.2, 0.2, 3), 'white_s', seg=48, closed=True, sharp=30)
        k.lathe(C.rrect(x - w - 0.08, x - w + 0.16, 4.5, 4.65 + 2 * h, 0.05, 0.05, 1), 'grey', seg=48, closed=True)
        k.lathe(C.rrect(x + w - 0.16, x + w + 0.08, 4.5, 4.65 + 2 * h, 0.05, 0.05, 1), 'grey', seg=48, closed=True)
        for j in range(12):
            C.gbox(x, (j + 0.5) * TAU / 12, 4.55 + 2 * h, 0.35, 0.35, 0.15, 'dark')
    cage = 7.5
    xs = (-143.3, -137.5, -131.8, -126.0)
    for i in range(8):
        a = (i + 0.5) * TAU / 8
        a2 = a + TAU / 8
        k.tube(P(xs[0], cage, a), P(xs[-1], cage, a), 0.14, 'truss', seg=6)
        for x in xs:
            k.tube(P(x, cage, a), P(x, cage, a2), 0.09, 'truss', seg=5)
            k.tube(P(x, cage, a), P(x, 4.6, a), 0.08, 'truss', seg=5)
        for j, x in enumerate(xs[:-1]):
            p, q = (a, a2) if (i + j) % 2 else (a2, a)
            k.tube(P(x, cage, p), P(xs[j + 1], cage, q), 0.07, 'truss', seg=4)
    for i in range(4):          # neutral-beam injectors between the coils
        a = i * TAU / 4 + 0.3
        with k.xf(rotX(a)):
            with k.xf(orient((-131.78, 4.4, 0), (0, 1, 0))):
                k.can(0, 3.5, 0.5, 'steel', seg=16)
                k.can(1.9, 2.2, 0.68, 'grey', seg=16)
                k.box((4.3, 0, 0), (1.7, 1.7, 1.6), 'white_s', bevel=0.08)
                k.box((4.3, 0, 0.85), (1.2, 1.2, 0.2), 'gold')
                k.box((5.25, 0, 0), (0.2, 1.3, 1.3), 'radw')
            k.pipe([(-133.4, 8.7, 0.5), (-134.6, 7.8, 1.4), (-139, 7.7, 1.4), (-142.6, 6.3, 1.4)], 0.1, 'silver', seg=6)
    for a in (0.15, 1.2, 2.25, 3.3, 4.35, 5.4):
        C.axial_pipe(-142.5, -126.6, cage, a, 0.13, R.choice(['silver', 'gold', 'white_s']), clamp=3.9)
    for s in (1, -1):           # superconducting bus ducts from the converter to the magnets
        k.pipe([(-142.6, s * 2.2, 7.9), (-127.4, s * 2.2, 7.9), (-125.6, s * 2.2, 6.2)], 0.22, 'gold', seg=8)
    # --- direct energy converter: collector drum with power conditioning round it
    k.lathe([(-126, 0), (-126, 5.7), (-125.7, 6.0), (-122.3, 6.0), (-122, 5.7), (-122, 0)], 'white', seg=48)
    for x in (-125.6, -124.0, -122.4):
        k.lathe(C.rrect(x - 0.16, x + 0.16, 5.95, 6.3, 0.06, 0.06, 1), 'grey', seg=48, closed=True)
    k.lathe(C.rrect(-125.2, -124.4, 5.97, 6.07, 0.02, 0.02, 1), 'blue', seg=48, closed=True)
    for i in range(8):
        a = i * TAU / 8
        C.gbox(-123.2, a, 6.0, 1.3, 1.9, 0.8, 'white_s', bevel=0.06)
        C.gbox(-123.2, a + TAU / 16, 6.0, 1.0, 0.9, 0.45, R.choice(['gold', 'grey', 'dark']))
        with k.xf(on(-123.2, a, 6.8)):
            k.box((0, 0.08, 0), (1.0, 0.1, 1.6), 'radw')
    # --- backup fission core on the axis: small vessel, control-drum drives, thrust struts round it
    k.lathe([(-122, 3.2), (-121.8, 3.4), (-119.3, 3.4)], 'vessel', seg=32)
    k.lathe(C.rrect(-121.0, -120.2, 3.38, 3.52, 0.03, 0.03, 1), 'orange', seg=32, closed=True)
    for i in range(6):
        a = i * TAU / 6 + 0.2
        with k.xf(on(-120.6, a, 3.4)):
            k.can(-1.0, 0.9, 0.3, 'steel', seg=10, ch=0.05, oy=0.32)
        k.tube(P(-122, 5.6, a), P(-119.1, 6.6, a), 0.16, 'truss', seg=6)
        k.tube(P(-122, 5.6, a), P(-119.1, 6.6, a + TAU / 6), 0.08, 'truss', seg=5)
    # --- shadow shield: tungsten toward the reactor, lithium hydride behind it, white forward face
    k.lathe([(-119.3, 0), (-119.3, 6.5), (-119.0, 7.0), (-118.0, 7.6), (-117.2, 8.7), (-116.2, 10.4), (-115.5, 11.7), (-115.2, 12.0)],
            'tungsten', seg=80, sharp=25)
    k.lathe([(-115.2, 12.0), (-114.7, 12.0), (-114.5, 11.75), (-114.5, 0)], 'white', seg=80, sharp=40)
    k.torus(-114.95, 12.0, 0.28, 'grey', seg=80, tseg=8)
    k.torus(-117.75, 8.05, 0.12, 'grey', seg=64, tseg=6)
    for i in range(16):
        a = i * TAU / 16
        with k.xf(rotX(a)):
            k.box((-114.3, 7.4, 0), (0.4, 8.4, 0.22), 'grey')
        if i % 2 == 0:
            k.tube(P(-114.5, 10.6, a), P(-107.5, 2.6, a), 0.14, 'truss', seg=6)
    # --- thermal spine: a load tube with the coolant headers and four carbon radiator wings
    H = T['htr']
    k.lathe([(-114.5, 2.6), (-71.0, 2.6), (-70.2, 2.2), (-70.2, 0)], 'grey', seg=24)
    for x in np.arange(-112, -71, 5.5):
        k.lathe(C.rrect(x - 0.18, x + 0.18, 2.55, 2.95, 0.05, 0.05, 1), 'dark', seg=24, closed=True)
    for (y, z) in ((4, 4), (-4, 4), (-4, -4), (4, -4)):       # round-to-square adapter onto the truss
        k.tube((-69, y, z), (-74.5, y * 0.46, z * 0.46), 0.2, 'truss', seg=8)
    x0, x1 = H['x0'], H['x0'] + H['len']
    root = 3.9

    def tip(x):
        return H['r0'] + H['w0'] + (H['w1'] - H['w0']) * (x - x0) / H['len']
    nseg = 8
    t = 0.1
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)]
    for q in range(H['n']):
        with k.xf(rotX(TAU / 8 + q * TAU / 4)):
            k.tube((x0 - 1.4, 3.25, 0.5), (x1 + 0.6, 3.25, 0.5), 0.32, 'hotpipe', seg=10)
            k.tube((x0 - 1.4, 3.25, -0.5), (x1 + 0.6, 3.25, -0.5), 0.32, 'hotpipe', seg=10)
            for i in range(nseg):
                xa = x0 + H['len'] * i / nseg + 0.15
                xb = x0 + H['len'] * (i + 1) / nseg - 0.15
                ta, tb = tip(xa), tip(xb)
                v = [(xa, root, -t), (xb, root, -t), (xb, tb, -t), (xa, ta, -t),
                     (xa, root, t), (xb, root, t), (xb, tb, t), (xa, ta, t)]
                uvf = [(xa, 0), (xb, 0), (xb, 1), (xa, 1)]
                uvs = [[uvf[0], uvf[3], uvf[2], uvf[1]], uvf, [uvf[0], uvf[1], uvf[1], uvf[0]], [uvf[2], uvf[3], uvf[3], uvf[2]],
                       [uvf[1], uvf[2], uvf[2], uvf[1]], [uvf[3], uvf[0], uvf[0], uvf[3]]]
                k.add('hot', v, f, uvs)
                k.box((xa - 0.15, (root + ta) / 2, 0), (0.16, ta - root, 0.36), 'cc')
                k.tube((xa, ta + 0.16, 0), (xb, tb + 0.16, 0), 0.13, 'cc', seg=6)
                k.tube((xa, 3.5, 0.5), (xa, root + 0.15, 0), 0.13, 'hotpipe', seg=6)
            k.box((x1 + 0.05, (root + tip(x1)) / 2, 0), (0.16, tip(x1) - root, 0.36), 'cc')
            for xs_ in (x0 + 11, x0 + 22, x0 + 33, x1):       # stay rods to the spine keep the wings flat
                for s in (1, -1):
                    k.tube((xs_, tip(xs_), 0), (xs_ - 5.5, 2.75, s * 1.5), 0.05, 'cc', seg=4)
    # --- aft RCS quads on booms off the truss's first frame
    for (ya, yb) in ((4, -4), (-4, 4)):
        k.tube((-66, ya, ya), (-66, yb, ya), 0.13, 'truss', seg=6)
        k.tube((-66, ya, ya), (-66, ya, yb), 0.13, 'truss', seg=6)
    for q in range(4):
        with k.xf(rotX(q * TAU / 4)):
            k.tube((-66, 4.0, 0), (-66, 9.3, 0), 0.2, 'truss', seg=8)
            k.tube((-68.6, 4.0, 0), (-66, 8.9, 0), 0.09, 'truss', seg=5)
            k.tube((-63.4, 4.0, 0), (-66, 8.9, 0), 0.09, 'truss', seg=5)
            with k.xf(move(-66, 10.2, 0) @ Matrix.Scale(0.7, 4)):
                C.rcs_quad()


# =====================================================================================
# mid: water tanks, fuel dewars, flywheel, despun hub, habitat ring, command module
# =====================================================================================
def build_mid():
    Tk, Dw = T['tanks'], T['dewar']
    truss(-69, -32, 4.0, 4)
    utilities(-68.5, -32.5, 9.25)
    k.tube((-69, 0, 1.6), (-41, 0, 1.6), 0.26, 'silver', seg=8)          # reaction-mass main to the drive
    k.tube((-114.4, 0, 2.95), (-70, 0, 2.95), 0.22, 'silver', seg=8)
    k.tube((-70, 0, 2.95), (-69, 0, 1.6), 0.22, 'silver', seg=8)
    # --- water reaction-mass tanks: six domed tanks on cradle rings, strutted to the longerons
    corners = [(4, 4), (-4, 4), (-4, -4), (4, -4)]
    for q in range(Tk['n']):
        a = q * TAU / Tk['n']
        y, z = Tk['rc'] * math.cos(a), Tk['rc'] * math.sin(a)
        k.dome_tank(Tk['x0'], Tk['x1'], Tk['r'], 'tank', k=0.62, seg=32, rings=7, oy=y, oz=z)
        for x in (-57.6, -53.0, -51.5, -47.5, -45.4):
            k.lathe(C.rrect(x - 0.12, x + 0.12, Tk['r'] - 0.02, Tk['r'] + 0.07, 0.02, 0.02, 1), 'grey', seg=32, closed=True, oy=y, oz=z)
        near = sorted(corners, key=lambda c: (c[0] - y) ** 2 + (c[1] - z) ** 2)[:2]
        for x in (-56.0, -47.0):
            k.lathe(C.rrect(x - 0.35, x + 0.35, Tk['r'], Tk['r'] + 0.26, 0.06, 0.06, 1), 'grey', seg=32, closed=True, oy=y, oz=z)
            for c in near:
                d = Vector((c[0] - y, c[1] - z)).normalized() * (Tk['r'] + 0.2)
                k.tube((x, c[0], c[1]), (x, y + d.x, z + d.y), 0.15, 'truss', seg=6)
                k.tube((x + (4.5 if x < -50 else -4.5), c[0], c[1]), (x, y + d.x, z + d.y), 0.08, 'truss', seg=5)
        with k.xf(rotX(a)):
            k.pipe([(Tk['x1'] - 0.1, Tk['rc'], 0), (-41.6, Tk['rc'], 0), (-41.0, 6.4, 0)], 0.17, 'silver', seg=6)
            k.box((-41.7, Tk['rc'] + 0.3, 0), (0.8, 0.6, 0.8), 'grey', bevel=0.04)
            k.pipe([(Tk['x0'] + 0.1, Tk['rc'], 0), (-61.2, Tk['rc'], 0), (-62.0, 6.0, 0)], 0.09, 'silver', seg=5)
            if q % 2 == 0:
                C.hrail(-57, -46, Tk['rc'] + Tk['r'], 0.0)
                C.hatch(-51.5, 0.0, Tk['rc'] + Tk['r'], 0.45)
        with k.xf(move(0, y, z) @ rotX(a)):
            greeb(-58, -45, Tk['r'], 4, a0=-0.6, span=1.2, hmax=0.35, s=0.55)
    k.torus(-41.0, 6.2, 0.22, 'silver', seg=40, tseg=8)
    k.torus(-62.0, 5.9, 0.11, 'silver', seg=40, tseg=6)
    k.pipe([(-41.0, 0.3, 6.1), (-41.0, 0, 1.9)], 0.2, 'silver', seg=6)
    # --- fusion fuel dewars: helium-3 above the spine, deuterium below, in foil on strut tripods
    for (zc, rad) in ((Dw['z'], Dw['rHe']), (-(4.6 + Dw['rD'] + 0.6), Dw['rD'])):
        s = 1 if zc > 0 else -1
        k.sphere((Dw['x'], 0, zc), rad, 'gold', seg=32, rings=16)
        with k.xf(move(Dw['x'], 0, zc)):
            k.torus(0, rad + 0.03, 0.07, 'grey', seg=32, tseg=5)
            with k.xf(rotY(PI / 2)):
                k.torus(0, rad + 0.03, 0.07, 'grey', seg=32, tseg=5)
        for dx in (-1, 1):
            for dy in (-1, 1):
                k.tube((Dw['x'] + dx * rad * 0.7, dy * rad * 0.7, zc), (Dw['x'] + dx * 2.6, dy * 4, s * 4), 0.08, 'truss', seg=5)
        k.pipe([(Dw['x'], 0.3, zc - s * rad), (Dw['x'], 0.3, s * 4.4), (Dw['x'] - 2, -1.6, s * 1.9)], 0.08, 'gold', seg=6)
        C.gbox(Dw['x'], PI / 2 * s, abs(zc) + rad, 0.6, 0.6, 0.35, 'grey')
    # --- momentum flywheel housing: carbon rotor inside, turning against the ring
    k.lathe([(-32.6, 0), (-32.6, 5.9), (-30.4, 9.2), (-30.0, 9.6), (-22.0, 9.6), (-22.0, 9.0)], 'grey', seg=72)
    k.lathe(C.rrect(-27.0, -25.6, 9.57, 9.85, 0.05, 0.05, 1), 'orange', seg=72, closed=True)
    for x in (-29.6, -22.4):
        k.lathe(C.rrect(x - 0.25, x + 0.25, 9.55, 9.95, 0.06, 0.06, 1), 'dark', seg=72, closed=True)
    for i in range(16):
        C.gbox(-28.5 + (i % 2) * 4.7, i * TAU / 16, 9.6, 1.2, 0.9, 0.4, 'dark')
    for i in range(8):
        with k.xf(rotX(i * TAU / 8 + TAU / 16)):
            k.tube((-32.5, 6.0, 0), (-30.3, 9.3, 0), 0.12, 'truss', seg=5)
            k.box((-31.4, 7.9, 0.5), (1.0, 0.5, 0.7), 'white_s', bevel=0.04)
    # --- despun hub: zero-g labs, bearing races either side of the turning collar
    Hb = T['hub']
    k.lathe([(-22, 9.0), (21.2, 9.0), (21.8, 8.75), (22.3, 7.7), (22.6, 7.0)], 'white', seg=80, sharp=30)
    for x in (-9.7, 9.7):
        k.lathe(C.rrect(x - 0.8, x + 0.8, 8.9, 10.3, 0.25, 0.25, 2), 'grey', seg=80, closed=True)
        k.lathe(C.rrect(x - 0.3, x + 0.3, 10.25, 10.55, 0.06, 0.06, 1), 'dark', seg=80, closed=True)
        for j in range(20):
            C.gbox(x, j * TAU / 20, 10.3, 0.7, 0.8, 0.3, 'grey')
    greeb(-21.2, -11.2, 9.0, 13, a0=0.5, span=2.1)
    greeb(-21.2, -11.2, 9.0, 13, a0=3.7, span=2.1)
    greeb(14.5, 20.6, 9.0, 14)
    for a in (0.35, 1.95, 3.5, 5.1):
        C.axial_pipe(-21.6, -10.9, 9.0, a, 0.2, 'gold')
        C.axial_pipe(-21.6, -10.9, 9.0, a + 0.1, 0.11, 'silver')
        C.axial_pipe(10.9, 21.0, 9.0, a + 0.5, 0.2, 'gold')
        C.axial_pipe(10.9, 21.0, 9.0, a + 0.6, 0.11, 'silver')
    for a in (1.2, 3.2, 5.0):
        C.hrail(-21, -11.5, 9.0, a)
        C.hrail(14, 20.8, 9.0, a + 0.3)
    for a in (0.8, 2.6, 4.4):
        C.hatch(-18.5, a, 9.0, 0.7)
    band(-12.3, -11.2, 9.0, 'blue', h=0.06, seg=80)
    for i in range(20):                 # the zero-g labs' ports
        a = (i + 0.5) * TAU / 20
        if abs(a - PI / 2) > 0.35 and all(abs(a - pa - 0.55) > 0.22 for pa in (0.35, 1.95, 3.5, 5.1)):
            window(12.6, a, 9.0, 0.8, 0.8, 0.5)
    for s in (1, -1):
        side_decal('name', NAME, -17.0, 8.2, 1.5, 9.04, s, zc=1.2)
    # observation cupola on the hub
    C.zcan(8.8, 10.0, 1.7, 'grey', x=16.5, seg=20)
    C.zcan(10.0, 10.25, 1.35, 'glass_dark', x=16.5, seg=20, ch=0.05)
    for i in range(6):
        with k.xf(move(16.5, 0, 9.45) @ rotZ(i * TAU / 6)):
            k.box((1.68, 0, 0), (0.12, 0.95, 0.75), 'glass')
    # --- habitat ring: one continuous pressure hull, bumper tiles over the water jacket, four elevator spokes
    Rg = T['ring']
    k.group = 'RingH'
    C.hab_ring(0.0, Rg, phase=0.2, nmod=20, name=NAME, reg=REG + '   UNITED EARTH', rh=T['collar'] - 1.2)
    with k.xf(rotX(0.2)):
        # hydroponics decks under the roof: two glazed sectors lit by the grow lamps
        for c in (0.0, PI):
            n = 14
            for i in range(n):
                a0 = c - 0.55 + (i + 0.08) * 1.1 / n
                a1 = c - 0.55 + (i + 0.92) * 1.1 / n
                for (xa, xb) in ((2.1, 3.7), (-3.7, -2.1)):
                    C.arc_plate(xa - 0.12, xb + 0.12, Rg['rIn'] - 0.1, Rg['rIn'] + 0.02, a0 - 0.002, a1 + 0.002, 'grey', seg=3)
                    C.arc_plate(xa, xb, Rg['rIn'] - 0.15, Rg['rIn'] - 0.08, a0, a1, 'grow' if R.random() < 0.8 else 'glass_dark', seg=3)
        # spoke details: a mid-span tension ring and the docking lights at each landing
        k.torus(0.0, 31.0, 0.09, 'truss', seg=96, tseg=5)
        for q in range(Rg['n']):
            a = q * TAU / Rg['n'] + TAU / (2 * Rg['n'])
            with k.xf(rotX(a)):
                C.ycan(30.2, 31.8, 2.45, 'blue', seg=28, ch=0.1)
                for dx in (1, -1):
                    k.tube((dx * 2.2, 31.0, 0), (dx * 6.9, Rg['rIn'] + 0.4, 0), 0.07, 'truss', seg=5)
    k.group = 'Hull'
    # --- command module: bridge and operations round the storm shelter, docking port on top
    Cm = T['cmd']
    k.lathe([(22.6, 7.0), (37.0, 7.0), (37.6, 6.75), (38.8, 5.6), (39.6, 4.9), (40.0, 4.6), (40.0, 0)], 'white_m', seg=64, sharp=30)
    for x in (23.0, 27.4, 33.0, 36.9):
        band(x - 0.15, x + 0.15, 7.0, 'grey', h=0.18)
    band(25.0, 26.2, 7.0, 'blue', h=0.06)
    band(28.2, 32.2, 7.0, 'white_s', h=0.32)          # the storm shelter's water wall shows as a thicker belt
    for i in range(16):
        a = (i + 0.5) * TAU / 16
        if abs(a - PI / 2) > 0.3:
            window(34.4, a, 7.0, 1.0, 0.9, 0.6)
    greeb(23.3, 27.2, 7.0, 10, hmax=0.4)
    greeb(35.2, 36.8, 7.0, 8, hmax=0.4)
    for a in (0.6, 2.5, 3.7, 5.4):
        C.hrail(23.4, 28.0, 7.0, a)
    C.hatch(26.2, 3.9, 7.0, 0.7)
    with k.xf(orient((34.5, 0, 6.8), (0, 0, 1)) @ Matrix.Scale(0.62, 4)):
        C.dock_port(False)
    for a in (2.2, 4.1):                # low-gain antennas
        with k.xf(on(24.5, a, 7.0)):
            k.tube((0, 0, 0), (0, 2.4, 0), 0.07, 'grey', seg=5)
            with k.xf(orient((0, 2.4, 0), (0, 1, 0))):
                k.lathe([(0, 0.05), (0.7, 0.35)], 'white_s', seg=12)
    for a in (0.3, 1.2, 3.5, 5.1):      # star trackers
        with k.xf(on(38.2, a, 6.2) @ orient((0, 0, 0), (0.5, 1, 0))):
            k.can(0, 0.8, 0.25, 'dark', seg=10)
    C.floodlight((30, -2.8, 7.25), (34.5, 0, 9.5), energy=2500, spot=1.2)


# =====================================================================================
# forward: truss, cold radiators, antennas, sample lab, probe racks and launcher, bow science section
# =====================================================================================
def build_fwd():
    truss(40, 116.6, 4.0, 9)
    utilities(40.5, 116.0, 8.51)
    k.boxX(40, 116, 4.3, 4.3, 0.16, 0.16, 'yellow')          # EVA rail along the dorsal port longeron
    k.box((74, 4.6, 4.7), (1.6, 0.9, 0.7), 'white_s', bevel=0.06)
    for (y, z) in ((4, 4), (-4, 4), (-4, -4), (4, -4)):
        k.tube((40.0, y, z), (38.6, y * 1.02, z * 1.02), 0.3, 'grey', seg=8)
    astronaut(move(74.3, 4.6, 5.05) @ rotX(PI / 2), reach=False)          # riding the EVA cart
    astronaut(move(106.0, 1.6, 4.72) @ rotX(PI / 2) @ rotY(PI))          # checking an orbiter on the dorsal rack
    radiators()
    antennas()
    sample_lab()
    probe_racks()
    launcher()
    bow()


def radiators():
    """Low-temperature radiators: panel wings on a root manifold, hinge booms and stay rods from king posts."""
    L = T['ltr']
    x0, x1 = L['x0'], L['x0'] + L['len']
    nx, ny = 7, 6
    y0, w = L['y0'] + 0.5, L['w'] - 0.5
    for s in (1, -1):
        for i in range(nx):
            xa = x0 + L['len'] * i / nx + 0.12
            xb = x0 + L['len'] * (i + 1) / nx - 0.12
            for j in range(ny):
                ya = y0 + w * j / ny + 0.1
                yb = y0 + w * (j + 1) / ny - 0.1
                k.box(((xa + xb) / 2, s * (ya + yb) / 2, 0), (xb - xa, yb - ya, 0.08), 'radw')
            if i:
                x = x0 + L['len'] * i / nx
                k.tube((x, s * (y0 - 0.4), 0), (x, s * (y0 + w), 0), 0.1, 'silver', seg=6)
        for j in range(ny + 1):
            y = s * (y0 + w * j / ny)
            k.tube((x0, y, 0), (x1, y, 0), 0.07, 'grey', seg=5)
        k.tube((x0 - 0.8, s * (y0 - 0.5), 0.26), (x1 + 0.8, s * (y0 - 0.5), 0.26), 0.24, 'silver', seg=10)
        k.tube((x0 - 0.8, s * (y0 - 0.5), -0.26), (x1 + 0.8, s * (y0 - 0.5), -0.26), 0.24, 'white_s', seg=10)
        for x in (x0, x1):
            k.tube((x, s * (y0 - 0.5), 0), (x, s * (y0 + w), 0), 0.12, 'grey', seg=6)
        for x in (x0 + 3, x0 + L['len'] / 2, x1 - 3):
            with k.xf(orient((x, s * 3.9, 0), (0, s, 0))):
                k.can(0, 1.7, 0.42, 'grey', seg=14)
                k.can(0.5, 0.9, 0.62, 'dark', seg=14)
            k.tube((x, s * 4, 4), (x, s * 4, -4), 0.11, 'truss', seg=6)
            k.pipe([(x + 0.6, s * 5.0, 0.26), (x + 0.6, s * 3.0, 0.9), (x + 0.6, 1.6 * s, 1.6)], 0.1, 'silver', seg=5)
        for x in (x0 + 1, x0 + L['len'] / 2, x1 - 1):          # king posts and stays
            for sz in (1, -1):
                top = (x, s * 4.5, sz * 10.0)
                k.tube((x, s * 4, sz * 4), top, 0.12, 'truss', seg=6)
                k.sphere(top, 0.22, 'grey', seg=8, rings=4)
                for f_ in (0.5, 1.0):
                    k.tube(top, (x, s * (y0 + w * f_), 0), 0.045, 'truss', seg=4)
                k.tube(top, (x + (9 if x < x0 + 30 else -9), s * 4, sz * 4), 0.045, 'truss', seg=4)
        tipy = s * (y0 + w + 0.5)
        k.box((x0 + L['len'] / 2, tipy, 0), (1.2, 0.7, 0.7), 'navR' if s > 0 else 'navG')
        C.LIGHTS.append(('POINT', (x0 + L['len'] / 2, tipy + s * 1.0, 0), None, 500, (1, 0.05, 0.03) if s > 0 else (0.05, 1, 0.2), 0))


def antennas():
    Hg, Xb = T['hga'], T['xband']
    # deep-space high-gain dish on a lattice mast with a two-axis gimbal
    C.girder((Hg['o'][0], 0, 4.0), (Hg['o'][0], 0, 19.2), 1.3, bay=1.3, rc=0.09, rd=0.045)
    k.tube((Hg['o'][0], -4, 4), (Hg['o'][0], 4, 4), 0.13, 'truss', seg=6)
    for (y, z) in ((4, 4), (-4, 4)):
        k.tube((Hg['o'][0] - 8.5, y, z), (Hg['o'][0], y * 0.16, 12.5), 0.07, 'truss', seg=5)
        k.tube((Hg['o'][0] + 8.5, y, z), (Hg['o'][0], y * 0.16, 12.5), 0.07, 'truss', seg=5)
    k.box((Hg['o'][0], 0, 19.5), (1.9, 1.9, 0.8), 'grey', bevel=0.08)
    C.zcan(19.9, 20.4, 0.75, 'dark', x=Hg['o'][0], seg=16)
    d = Vector(Hg['dir']).normalized()
    with k.xf(orient(Vector(Hg['o']) - d * 0.2, d)):
        k.can(-1.3, -0.2, 0.8, 'gold', seg=16)
        k.box((-0.9, 0, 0), (0.7, 2.6, 0.5), 'grey', bevel=0.05)
        with k.xf(move(0.2, 0, 0)):
            C.dish(Rd=Hg['D'] / 2, f=4.6)
    k.pipe([(Hg['o'][0] + 0.75, 0.75, 4.0), (Hg['o'][0] + 0.75, 0.75, 19.2)], 0.07, 'gold', seg=5)
    C.floodlight((Hg['o'][0] + 1.2, 0, 18.4), (104, 0, 6), energy=9000, spot=0.8)
    # backup X-band dish under the spine
    k.tube((Xb['o'][0], 0, -4.0), (Xb['o'][0], 0, -8.0), 0.18, 'truss', seg=8)
    k.tube((Xb['o'][0] + 3, 2.5, -4.0), (Xb['o'][0], 0, -7.6), 0.07, 'truss', seg=5)
    k.tube((Xb['o'][0] + 3, -2.5, -4.0), (Xb['o'][0], 0, -7.6), 0.07, 'truss', seg=5)
    k.box((Xb['o'][0], 0, -8.1), (0.8, 0.8, 0.6), 'grey', bevel=0.05)
    d = Vector(Xb['dir']).normalized()
    with k.xf(orient(Vector(Xb['o']), d) @ Matrix.Scale(Xb['D'] / 7.5, 4)):
        C.dish()


def sample_lab():
    """Sample receiving lab: its own small pressure vessel on saddles above the spine, away from the crew spaces."""
    Sl = T['sample']
    with k.xf(move(0, 0, Sl['z'])):
        k.dome_tank(Sl['x0'] - 1.0, Sl['x1'] + 1.2, Sl['r'], 'white_m', k=0.5, seg=40, rings=7)
        for x in (82.0, 86.6, 91.0, 94.6):
            band(x - 0.13, x + 0.13, Sl['r'], 'grey', h=0.16, seg=40)
        band(88.2, 89.0, Sl['r'], 'hazard', h=0.05, seg=40)
        for a in (0.5, 1.1, 2.05, 2.65):
            window(85.4, a, Sl['r'], 0.6, 0.6, 0.6)
        greeb(89.5, 94.2, Sl['r'], 12, a0=-0.4, span=PI + 0.8, hmax=0.35, s=0.5)
        for a in (0.75, 2.4):
            C.hrail(82.5, 94, Sl['r'], a)
        C.hatch(92.6, PI, Sl['r'], 0.6)
        with k.xf(on(87.5, PI / 2, Sl['r'])):         # cold plate for the sample freezers
            k.box((0, 1.0, 0), (5.0, 0.1, 2.6), 'radw')
            for dx in (-2, 2):
                k.tube((dx, 0, 0.8), (dx, 1.0, 0.8), 0.06, 'grey', seg=5)
                k.tube((dx, 0, -0.8), (dx, 1.0, -0.8), 0.06, 'grey', seg=5)
        # sample port at the forward end, a return capsule docked nose-out
        k.lathe([(96.6, 1.45), (98.0, 1.45), (98.2, 1.25)], 'white_s', seg=24, sharp=30)
        k.lathe(C.rrect(98.0, 98.45, 1.15, 1.75, 0.06, 0.06, 1), 'grey', seg=24, closed=True)
        k.lathe(C.rrect(97.0, 97.4, 1.43, 1.56, 0.03, 0.03, 1), 'hazard', seg=24, closed=True)
        k.lathe([(98.45, 0.9), (98.6, 1.45), (98.85, 1.6)], 'dark', seg=24, sharp=50)
        k.lathe([(98.85, 1.6), (99.3, 1.55), (100.2, 1.05), (100.75, 0.55), (100.85, 0)], 'white_s', seg=24, sharp=40)
        k.lathe(C.rrect(99.25, 99.4, 1.5, 1.58, 0.02, 0.02, 1), 'orange', seg=24, closed=True)
        for i in range(3):
            with k.xf(rotX(i * TAU / 3 + 0.4)):
                k.box((99.75, 1.32, 0), (0.4, 0.06, 0.4), 'glass_dark')
        for x in (83.0, 93.0):              # saddles and struts to the longerons
            k.lathe(C.rrect(x - 0.3, x + 0.3, Sl['r'], Sl['r'] + 0.24, 0.05, 0.05, 1), 'grey', seg=40, closed=True)
    for x in (83.0, 93.0):
        for s in (1, -1):
            k.tube((x, s * 4, 4), (x, s * 2.5, Sl['z'] - 2.1), 0.14, 'truss', seg=6)
            k.tube((x + (4 if x < 88 else -4), s * 4, 4), (x, s * 2.5, Sl['z'] - 2.1), 0.08, 'truss', seg=5)
    k.pipe([(86, 0.6, Sl['z'] - Sl['r']), (86, 0.6, 4.4), (86, 1.6, 1.9)], 0.12, 'silver', seg=6)


def probe(kind):
    """One probe at the local origin, +x out of its rack."""
    if kind == 'entry':             # atmosphere entry probe: blunt heat shield outward, white backshell
        k.can(0, 0.4, 1.0, 'grey', seg=16)
        k.lathe([(0.4, 0.9), (0.6, 1.1), (1.9, 2.2), (2.05, 2.3)], 'white_s', seg=28, sharp=40)
        k.lathe([(2.05, 2.3), (2.3, 2.12), (2.55, 1.55), (2.72, 0.8), (2.78, 0)], 'ablator', seg=28, sharp=60)
        k.lathe(C.rrect(1.98, 2.1, 2.27, 2.37, 0.02, 0.02, 1), 'grey', seg=28, closed=True)
        k.lathe([(2.70, 0.62), (2.80, 0.55), (2.84, 0)], 'grey', seg=16)
        k.torus(2.52, 1.62, 0.035, 'dark', seg=28, tseg=4)
        for i in range(3):
            with k.xf(rotX(i * TAU / 3)):
                k.box((1.2, 1.7, 0), (0.5, 0.12, 0.5), 'dark')
    elif kind == 'orbiter':         # small orbiter: foil bus, folded arrays, dish
        k.can(0, 0.35, 0.9, 'grey', seg=14)
        k.box((1.45, 0, 0), (2.2, 2.2, 2.2), 'gold', bevel=0.08)
        for s in (1, -1):
            k.box((1.45, s * 1.16, 0), (1.9, 0.07, 1.9), 'dark')
            k.box((1.45, s * 1.26, 0), (1.9, 0.07, 1.9), 'dark')
            k.box((1.45, s * 1.21, 0.9), (0.3, 0.2, 0.2), 'grey')
        with k.xf(move(2.6, 0, 0) @ Matrix.Scale(0.3, 4)):
            C.dish()
        k.box((0.8, 0, 1.2), (0.5, 0.5, 0.3), 'white_s')
        k.box((2.0, 0, -1.2), (0.6, 0.6, 0.25), 'silver')
        with k.xf(orient((1.0, 0, -1.1), (0, 0, -1))):
            k.lathe([(0, 0.1), (0.15, 0.14), (0.5, 0.3)], 'bell', seg=10)
    else:                           # surface penetrator: a steel-nosed dart in two ring clamps
        k.can(0, 0.3, 0.75, 'grey', seg=14)
        k.can(0.3, 3.4, 0.5, 'white_s', seg=14)
        k.lathe([(3.4, 0.5), (3.9, 0.36), (4.4, 0.06), (4.45, 0)], 'steel', seg=14)
        k.lathe(C.rrect(1.1, 1.3, 0.5, 0.64, 0.03, 0.03, 1), 'hazard', seg=14, closed=True)
        k.lathe(C.rrect(2.7, 2.9, 0.5, 0.64, 0.03, 0.03, 1), 'hazard', seg=14, closed=True)
        for f in range(4):
            with k.xf(rotX(f * TAU / 4 + 0.78)):
                k.box((0.85, 0.82, 0), (0.9, 0.64, 0.06), 'grey')


def probe_racks():
    """Nine probes outside the spine: entry probes to port, orbiters on top, penetrators to starboard."""
    Pb = T['probes']
    for a, kind in ((0.0, 'entry'), (PI / 2, 'orbiter'), (PI, 'pen')):
        with k.xf(rotX(a)):
            for z in (-3.0, 3.0):
                k.tube((99.6, 4.55, z), (118.0, 4.55, z), 0.12, 'truss', seg=6)
            for x in (100, 106, 112, 117.6):
                k.tube((x, 4.55, -3.0), (x, 4.55, 3.0), 0.09, 'truss', seg=5)
                for z in (-3.0, 3.0):
                    k.box((x, 4.3, z), (0.4, 0.5, 0.4), 'grey')
            k.boxX(100, 118, 4.5, 3.55, 0.25, 0.5, 'dark')
            for xc in Pb['xs']:
                k.box((xc, 4.72, 0), (3.0, 0.2, 3.0), 'grey', bevel=0.05)
                k.box((xc - 1.7, 4.85, 0), (0.5, 0.6, 1.0), 'white_s', bevel=0.04)       # umbilical box
                for dz in (-1, 1):
                    k.box((xc, 5.05, dz * 1.25), (0.55, 0.6, 0.3), 'hazard')
                with k.xf(orient((xc, 4.85, 0), (0, 1, 0))):
                    probe(kind)


def launcher():
    """Electromagnetic rail under the spine: two rails through drive coils, a sled, a loader arm."""
    Pb = T['probes']
    ra, rb = Pb['rail']
    rz = Pb['railZ']
    for s in (1, -1):
        k.tube((ra, s * 1.1, rz), (rb, s * 1.1, rz), 0.2, 'grey', seg=8)
        k.tube((ra, s * 2.3, -5.6), (rb, s * 2.3, -5.6), 0.1, 'gold', seg=6)
    with k.xf(move(0, 0, rz)):
        x, i = ra + 1.0, 0
        while x < rb:
            if i % 4 == 0:
                k.lathe(C.rrect(x - 0.3, x + 0.3, 1.42, 1.85, 0.06, 0.06, 1), 'grey', seg=20, closed=True)
            else:
                k.torus(x, 1.5, 0.17, 'gold', seg=20, tseg=6)
            x += 2.5
            i += 1
        k.lathe(C.rrect(rb - 0.4, rb + 0.4, 1.35, 1.95, 0.08, 0.08, 1), 'hazard', seg=20, closed=True)
    for x in (ra + 1, ra + 11, ra + 21, ra + 31, rb - 1):          # A-frame pylons off the lower longerons
        for s in (1, -1):
            k.tube((x, s * 4, -4), (x, s * 1.75, rz + 0.75), 0.13, 'truss', seg=6)
        k.tube((x, -1.75, rz + 0.75), (x, 1.75, rz + 0.75), 0.1, 'truss', seg=5)
    k.box((ra - 0.7, 0, rz), (1.6, 3.0, 2.6), 'grey', bevel=0.1)           # breech and switchgear
    k.box((111.5, 0, rz), (3.0, 1.5, 1.1), 'white_s', bevel=0.08)           # sled
    for dx in (-1.6, 1.6):
        k.box((111.5 + dx, 0, rz), (0.2, 1.7, 1.3), 'hazard')
    for i in range(6):          # capacitor bank
        for s in (1, -1):
            with k.xf(move(82 + i * 2.3, s * 2.3, -4.9)):
                k.can(-0.95, 0.95, 0.62, 'silver', seg=14, ch=0.08)
    k.boxX(81, 94.6, 0, -4.35, 6.2, 0.2, 'grey')
    C.floodlight((104, 0, -4.4), (111, 0, -9), energy=2500, spot=1.3)
    # loader arm: lifts a probe off its rack and sets it on the sled
    pts = [(119.8, -3.9, -3.9), (113.0, -7.5, -7.6), (109.0, -3.2, -9.8)]
    with k.xf(orient(pts[0], (0, -1, -1))):
        k.can(-0.3, 0.5, 0.75, 'grey', seg=16)
    rad = [0.3, 0.24]
    for i in range(2):
        k.tube(pts[i], pts[i + 1], rad[i], 'white_s', seg=12)
        dv = (Vector(pts[i + 1]) - Vector(pts[i])).normalized()
        for f_ in (0.3, 0.7):
            with k.xf(orient(Vector(pts[i]).lerp(Vector(pts[i + 1]), f_), dv)):
                k.can(-0.1, 0.1, rad[i] + 0.05, 'grey', seg=12, ch=0.02)
    for i, p in enumerate(pts[:2]):
        ax = (Vector(pts[i + 1]) - Vector(p)).cross(Vector((0, 0, 1))).normalized()
        with k.xf(orient(p, ax)):
            k.can(-0.55, 0.55, 0.5 - i * 0.08, 'grey', seg=14, ch=0.06)
            k.can(-0.65, 0.65, 0.26, 'gold', seg=10)
    dv = (Vector(pts[2]) - Vector(pts[1])).normalized()
    with k.xf(orient(pts[2], dv)):
        k.can(-0.15, 0.7, 0.34, 'grey', seg=14)
        k.can(0.7, 0.9, 0.46, 'dark', seg=14)


def bow():
    Bw, Sc, Mg, Wp = T['bow'], T['scope'], T['mag'], T['whipple']
    r = Bw['r']
    # --- science section: a round pressure hull with dished ends
    k.lathe([(115.6, 0), (115.7, 2.4), (116.2, 3.9), (117.0, 4.95), (118.0, 5.5), (145.2, 5.5), (146.4, 5.2), (148.0, 4.05), (149.0, 3.2), (149.0, 0)],
            'white', seg=64, sharp=30)
    for x in (118.4, 121.6, 126.6, 134.2, 141.0, 145.0):
        band(x - 0.14, x + 0.14, r, 'grey', h=0.18)
    band(120.0, 121.2, r, 'blue', h=0.06)
    greeb(121.9, 126.2, r, 14, a0=-1.2, span=3.4, hmax=0.4)
    greeb(134.6, 144.6, r, 22, a0=3.6, span=2.4, hmax=0.4)
    greeb(134.6, 140.4, r, 8, a0=1.0, span=1.0, hmax=0.4)
    for a in (0.9, 4.0, 5.3):
        C.hrail(122, 144.6, r, a)
    C.hrail(134.6, 144.6, r, 2.0)
    C.hatch(124, 5.6, r, 0.7)
    C.hatch(140.5, 5.75, r, 0.6)
    for a in (0.45, 0.75, 2.4, 2.7, 3.45, 3.75):
        window(142.6, a, r, 0.7, 0.7, 0.6)
    astronaut(on(136.2, 1.15, r) @ rotY(PI))             # on the bow hull by the telescope mount
    astronaut(on(138.0, 1.0, r) @ rotY(2.4), reach=False)
    for s in (1, -1):
        side_decal('breg', REG, 137.2, 5.6, 1.5, r + 0.04, s, zc=2.3)
    # --- survey telescope: fork mount on a turntable, baffled tube, scarfed hood, cryoradiator
    sr = Sc['D'] / 2 + 0.4
    d = Vector(Sc['dir']).normalized()
    piv = Vector(Sc['o']) + d * 5.5
    k.box((130, 0, r + 0.2), (6.0, 4.6, 1.4), 'grey', bevel=0.12)
    C.zcan(r + 0.9, r + 1.55, 2.5, 'grey', x=piv.x, seg=32)
    C.zcan(r + 1.55, r + 1.75, 2.75, 'dark', x=piv.x, seg=32, ch=0.05)
    for s in (1, -1):
        k.box((piv.x, s * (sr + 0.55), (r + 1.7 + piv.z + 0.5) / 2), (1.5, 0.5, piv.z + 0.5 - r - 1.7), 'white_s', bevel=0.1)
        k.tube((piv.x - 1.6, s * (sr + 0.55), r + 1.8), (piv.x, s * (sr + 0.55), piv.z - 0.4), 0.11, 'grey', seg=6)
        C.ycan(s * (sr + 0.05) if s > 0 else s * (sr + 1.0), s * (sr + 1.0) if s > 0 else s * (sr + 0.05), 0.62, 'grey', x=piv.x, z=piv.z, seg=16)
        C.ycan(s * (sr + 1.0) if s > 0 else s * (sr + 1.25), s * (sr + 1.25) if s > 0 else s * (sr + 1.0), 0.38, 'gold', x=piv.x, z=piv.z, seg=12)
    with k.xf(orient(Sc['o'], d)):
        L = Sc['len']
        k.lathe([(0, 0), (0, sr + 0.05), (0.25, sr + 0.2), (2.6, sr + 0.2), (2.8, sr)], 'gold', seg=40, sharp=40)      # instrument bay
        k.lathe([(2.8, sr), (L, sr)], 'white_s', seg=40)
        k.lathe([(L, sr), (L, sr - 0.14), (2.9, sr - 0.14), (2.9, 0)], 'black', seg=40, sharp=40)
        for x in (2.8, 5.2, 6.4, 8.6, L - 0.1):
            k.lathe(C.rrect(x - 0.1, x + 0.1, sr - 0.02, sr + 0.12, 0.03, 0.03, 1), 'grey', seg=40, closed=True)
        k.can(L - 1.6, L - 1.2, 0.42, 'dark', seg=12)             # secondary mirror on its spider
        for i in range(3):
            with k.xf(rotX(i * TAU / 3 + 0.5)):
                k.box((L - 1.4, sr / 2, 0), (0.25, sr - 0.2, 0.04), 'dark')
        # scarfed sunshade hood on the sunward (starboard) half
        for (da, ext) in ((0.42, 3.6), (0.95, 2.5), (1.5, 1.3)):
            k.lathe([(L, sr + 0.06), (L + ext, sr + 0.06), (L + ext, sr + 0.1), (L, sr + 0.1)], 'white_s', seg=10, a0=PI - da, a1=PI + da,
                    closed=True, caps=True)
            k.lathe([(L, sr + 0.02), (L + ext, sr + 0.02), (L + ext, sr + 0.06), (L, sr + 0.06)], 'black', seg=10, a0=PI - da, a1=PI + da,
                    closed=True, caps=True)
        # cryoradiator on the cold (port-dorsal) side, on standoffs
        with k.xf(rotX(1.0)):
            k.box((4.3, sr + 0.5, 0), (5.6, 0.08, 3.0), 'radw')
            for (x, z) in ((2.0, 1.0), (2.0, -1.0), (6.6, 1.0), (6.6, -1.0)):
                k.tube((x, sr, z), (x, sr + 0.5, z), 0.05, 'grey', seg=5)
        k.box((1.2, 0, sr + 0.5), (1.4, 1.0, 0.6), 'white_s', bevel=0.05)
    # --- sunshade wall on the starboard side: four foil membranes spread on a tube frame
    xa, xb, zc, hh = 121.5, 141.0, 10.6, 9.0
    for i in range(4):
        y = -(4.0 + i * 0.45)
        k.box(((xa + xb) / 2, y, zc), (xb - xa - (3 - i) * 0.7, 0.04, hh - (3 - i) * 0.5), 'shade' if i == 3 else 'gold')
    for x in (xa - 0.2, (xa + xb) / 2, xb + 0.2):
        k.tube((x, -3.55, 4.25), (x, -4.7, zc - hh / 2 - 0.3), 0.13, 'truss', seg=6)
        k.tube((x, -4.7, zc - hh / 2 - 0.3), (x, -4.7, zc + hh / 2 + 0.3), 0.11, 'truss', seg=6)
        k.tube((x, -1.6, 5.3), (x, -4.7, zc + 1.0), 0.07, 'truss', seg=5)
        for z in (zc - hh / 2 - 0.25, zc + hh / 2 + 0.25):         # spreader bars carry the layer edges
            k.tube((x, -3.8, z), (x, -5.55, z), 0.07, 'grey', seg=5)
    for z in (zc - hh / 2 - 0.3, zc + hh / 2 + 0.3):
        k.tube((xa - 0.2, -4.7, z), (xb + 0.2, -4.7, z), 0.09, 'truss', seg=6)
    k.tube((xa - 0.2, -4.7, zc - hh / 2 - 0.3), ((xa + xb) / 2, -4.7, zc + hh / 2 + 0.3), 0.04, 'truss', seg=4)
    k.tube((xb + 0.2, -4.7, zc - hh / 2 - 0.3), ((xa + xb) / 2, -4.7, zc + hh / 2 + 0.3), 0.04, 'truss', seg=4)
    # --- mapping radar: phased-array tiles on a back frame under the hull
    rz = -(r + 1.5)
    for i in range(5):
        for j in range(3):
            k.box((123 + i * 4, -4 + j * 4, rz), (3.86, 3.86, 0.14), 'tile')
            k.box((123 + i * 4, -4 + j * 4, rz + 0.2), (1.6, 2.2, 0.26), R.choice(['grey', 'gold', 'white_s']), bevel=0.0)
    for y in (-6, -2, 2, 6):
        k.tube((121, y, rz + 0.28), (141, y, rz + 0.28), 0.09, 'truss', seg=5)
    for x in (121, 125, 129, 133, 137, 141):
        k.tube((x, -6, rz + 0.28), (x, 6, rz + 0.28), 0.08, 'truss', seg=5)
    for x in (124, 131, 138):
        for s in (1, -1):
            k.tube((x, s * 2.4, -4.9), (x, s * 2.0, rz + 0.28), 0.12, 'truss', seg=6)
            k.tube((x, s * 2.4, -4.9), (x, s * 6, rz + 0.28), 0.06, 'truss', seg=5)
    # --- spectrometer scan platform to port, lidar on the nose
    C.ycan(r - 0.1, r + 0.95, 0.8, 'grey', x=131, seg=20)
    k.box((131, r + 1.1, 0), (6.0, 0.3, 3.2), 'grey', bevel=0.06)
    for (x, z, r_, L_, mat) in ((129.3, -0.8, 0.38, 1.3, 'white_s'), (129.3, 0.8, 0.32, 1.0, 'gold'), (132.6, 0.25, 0.48, 1.7, 'white_s')):
        C.ycan(r + 1.25, r + 1.25 + L_, r_, mat, x=x, z=z, seg=14)
        C.ycan(r + 1.25 + L_ - 0.04, r + 1.25 + L_ + 0.03, r_ * 0.8, 'glass_dark', x=x, z=z, seg=12, ch=0.01)
    k.box((131.0, r + 1.65, -1.0), (1.2, 0.8, 0.9), 'gold', bevel=0.05)
    k.box((133.3, r + 1.5, 1.15), (0.9, 0.5, 0.7), 'white_s', bevel=0.04)
    k.box((130.6, r + 1.7, 1.56), (2.6, 0.9, 0.06), 'radw')
    k.sphere((147.6, 0, -3.9), 0.9, 'grey', seg=16, rings=8)
    with k.xf(move(147.6, 0, -3.9)):
        k.can(0.4, 1.3, 0.42, 'white_s', seg=12, ch=0.04)
        k.lathe([(1.31, 0.34), (1.31, 0)], 'glass_dark', seg=12)
    # --- magnetometer boom to starboard: a triangular lattice, sensors inboard and at the tip
    with k.xf(orient((Mg['x'], -(r - 0.2), 0), (0, -1, 0))):
        Lb = Mg['len'] + 0.2
        k.can(0, 1.4, 0.6, 'grey', seg=14)
        tri = [(0.3, 0.26), (-0.3, 0.26), (0.0, -0.3)]
        for (u, w) in tri:
            k.tube((1.2, u, w), (Lb, u, w), 0.07, 'truss', seg=5)
        x = 1.4
        j = 0
        while x < Lb:
            for q in range(3):
                p, p2 = tri[q], tri[(q + 1) % 3]
                k.tube((x, p[0], p[1]), (x, p2[0], p2[1]), 0.035, 'truss', seg=4)
                if x + 2.0 < Lb:
                    k.tube((x, p[0], p[1]), (x + 2.0, p2[0], p2[1]), 0.03, 'truss', seg=3)
            x += 2.0
            j += 1
        k.box((Lb * 0.55, 0, 0), (0.8, 0.8, 0.8), 'white_s', bevel=0.05)
        k.box((Lb + 0.5, 0, 0), (1.0, 1.0, 1.0), 'white_s', bevel=0.06)
        k.can(Lb + 1.0, Lb + 1.6, 0.25, 'gold', seg=10)
    # --- bow RCS, Whipple shield, forward low-gain antenna
    for q in range(4):
        with k.xf(rotX(TAU / 8 + q * TAU / 4 + 0.0)):
            k.tube((144, r - 0.1, 0), (144, 7.3, 0), 0.2, 'truss', seg=8)
            k.tube((142.2, r, 0), (144, 7.1, 0), 0.08, 'truss', seg=5)
            with k.xf(move(144, 8.2, 0) @ Matrix.Scale(0.7, 4)):
                C.rcs_quad()
    for i in range(8):
        a = i * TAU / 8 + TAU / 16
        k.tube(P(148.6, 3.5, a), P(Wp['x'] - 0.1, 6.0, a), 0.11, 'truss', seg=6)
        k.tube(P(149.0, 1.2, a), P(Wp['x'] - 0.1, 3.0, a), 0.08, 'truss', seg=5)
        k.tube(P(Wp['x'] - 0.1, 6.0, a), P(Wp['x'] - 0.1, 6.0, a + TAU / 8), 0.08, 'truss', seg=5)
    for (r0, r1, n) in ((0.45, 2.4, 8), (2.5, 4.45, 12), (4.55, Wp['r'], 18)):
        for i in range(n):
            a0 = (i + 0.015) * TAU / n
            a1 = (i + 0.985) * TAU / n
            C.arc_plate(Wp['x'], Wp['x'] + 0.12, r0, r1, a0, a1, 'bumper', seg=3)
            C.arc_plate(Wp['x'] + 0.7, Wp['x'] + 0.8, r0 + 0.03, r1 - 0.03, a0 + 0.004, a1 - 0.004, 'bumper', seg=3)
    for i in range(18):
        a = i * TAU / 18
        for rr in (1.6, 3.6, 5.8):
            k.tube(P(Wp['x'] + 0.1, rr, a), P(Wp['x'] + 0.7, rr, a), 0.04, 'grey', seg=4)
    k.can(Wp['x'] - 0.4, Wp['x'] + 1.2, 0.4, 'grey', seg=12)
    k.tube((Wp['x'] + 1.2, 0, 0), (153.6, 0, 0), 0.07, 'grey', seg=5)
    k.box((154.0, 0, 0), (0.8, 0.8, 0.8), 'white_s', bevel=0.05)
    k.box((154.55, 0, 0), (0.3, 0.24, 0.24), 'navW')
    C.LIGHTS.append(('POINT', (155.6, 0, 0), None, 120, (1, 1, 1), 0))


def build(parts, drive, landers):
    if 'aft' in parts:
        build_aft(drive)
        if drive:
            C.PLUMES.append(dict(x=-143.2, length=190.0, r0=3.7, r1=30.0))
    if 'mid' in parts:
        build_mid()
    if 'fwd' in parts:
        build_fwd()


SHOTS = {
    # tgt: look-at point; cam: direction from it to the camera; sun: direction to the sun in camera space
    'hero':    dict(tgt=(18, 0, -4), cam=(0.62, -0.66, 0.42), dist=590, lens=70, roll=-7, sun=(0.25, 0.62, 0.74), eoff=(-9, -20), alt=9500),
    'ring':    dict(tgt=(5, 0, 0), cam=(0.70, -0.60, 0.38), dist=300, lens=50, roll=5, sun=(0.55, 0.60, 0.58), eoff=(-12, -25.5), alt=9500, elon=120),
    'stern':   dict(tgt=(-90, 0, -2), cam=(-0.80, -0.52, 0.30), dist=350, lens=60, roll=4, sun=(-0.70, 0.45, 0.55), eoff=(10, -25), alt=9500),
    'science': dict(tgt=(121, 0, 2), cam=(0.55, 0.70, 0.46), dist=108, lens=50, roll=-4, sun=(-0.40, 0.66, 0.60), eoff=(15, -27), alt=9500, elon=165),
    'side':    dict(tgt=(0, 0, 0), cam=(0.0, -1.0, 0.06), dist=960, lens=100, roll=0, sun=(0.50, 0.62, 0.60), eoff=None, alt=0),
    'top':     dict(tgt=(0, 0, 0), cam=(0.0, -0.05, 1.0), dist=960, lens=100, roll=0, sun=(0.40, 0.50, 0.75), eoff=None, alt=0),
}

if __name__ == '__main__':
    C.main(build=build, shots=SHOTS, ship_id='tereshkova')
