"""Cochrane-class deep-space tender: high-detail model and path-traced stills (Cycles).

Run with the bpy module:  /home/user/.bpyvenv/bin/python blender/build_cochrane.py --shot hero --out blender/previews/cochrane
Layout and sizes follow the class page (docs: Cochrane Class artifact): x toward the bow, y to port, z dorsal; metres.
  --shot hero|ring|stern|yard|side|bow   --res 1920x1080   --spp 128   --drive (reactor at full power, plume)
  --only aft,mid,fwd  builds part of the ship (quick looks)   --no-earth  black sky
The Earth backdrop needs bmng.jpg (NASA Blue Marble, from the PyPI basemap-data wheel): --earth-tex path.
"""
import bpy, sys, os, math, argparse, random, time
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cochrane_kit as K
from cochrane_kit import Kit, rotX, rotY, rotZ, move, orient, TAU, PI

G0 = 9.81
D = dict(ringF=dict(r=78, rIn=70, w=18, g=0.8, n=6, dir=-1), ringA=dict(r=58, rIn=50, w=20, g=0.3, n=4, dir=1),
         htr=dict(len=68, w0=13, w1=31), ltr=dict(len=66, w=64), tanks=dict(n=6, r=5, len=40))

k = Kit(seed=2063)
k.group = 'Hull'
R = k.rnd
LIGHTS = []          # (kind, loc, dir, energy, colour, spot angle)
PIVOTS = {}          # turning group -> its centre on the axis (ship coordinates)
SPIN = {}            # turning group -> rad/s for Godot's RotateZ
PLUMES = []          # dict(x=throat, length, r0, r1) for build_plume
EXTRA = {}           # more materials for other ships: name -> (cycles factory, game spec); see export_game
DECALS = []          # (name, text, pts, flip)


def on(x, a, r):
    """Frame on a hull of radius r at angle a: local x axial, y radial out, z tangential."""
    return rotX(a) @ move(x, r, 0)


def P(x, r, a):
    return (x, r * math.cos(a), r * math.sin(a))


def ycan(y0, y1, r, mat, x=0.0, z=0.0, seg=16, ch=None):
    with k.xf(orient((x, y0, z), (0, 1, 0))):
        k.can(0, y1 - y0, r, mat, ch=min(0.12, r * 0.25) if ch is None else ch, seg=seg)


def zcan(z0, z1, r, mat, x=0.0, y=0.0, seg=16, ch=None):
    with k.xf(orient((x, y, z0), (0, 0, 1))):
        k.can(0, z1 - z0, r, mat, ch=min(0.12, r * 0.25) if ch is None else ch, seg=seg)


# ------------------------------------------------------------------ small parts
def gbox(x, a, r, w, d, h, mat, bevel=0.05):
    with k.xf(on(x, a, r)):
        k.box((0, h / 2 - 0.03, 0), (w, h, d), mat, bevel=bevel if min(w, d, h) > 0.35 else 0.0)


GM = ['grey', 'grey', 'white_s', 'dark', 'gold', 'grey', 'silver', 'white_s', 'white_s']


def greeble(x0, x1, r, n, a0=0.0, span=TAU, hmax=0.9, mats=GM):
    if k.lod < 1:
        n = n // 2
    for _ in range(n):
        x = x0 + R.random() * (x1 - x0)
        a = a0 + R.random() * span
        t = R.random()
        if t < 0.72:
            gbox(x, a, r, 0.6 + R.random() * 2.6, 0.5 + R.random() * 1.8, 0.15 + R.random() * hmax, R.choice(mats))
        elif t < 0.86:
            L, rad = 1.0 + R.random() * 2.2, 0.25 + R.random() * 0.4
            with k.xf(on(x, a, r + rad * 0.85)):
                k.can(-L / 2, L / 2, rad, R.choice(['silver', 'gold', 'white_s', 'grey']), ch=0.06, seg=12)
        elif t < 0.94:
            rad = 0.45 + R.random() * 0.5
            with k.xf(on(x, a, r)):
                k.sphere((0, rad * 0.9, 0), rad, R.choice(['silver', 'gold']), seg=14, rings=8)
                k.box((0, 0.1, 0), (rad * 1.5, 0.2, rad * 1.5), 'grey')
        else:
            h = 1.2 + R.random() * 2.0
            with k.xf(on(x, a, r)):
                k.tube((0, 0, 0), (0, h, 0), 0.05, 'grey', seg=5)
                k.box((0, h, 0), (0.25, 0.25, 0.25), 'dark')


def hrail(x0, x1, r, a, mat='yellow'):
    with k.xf(on(0, a, r)):
        k.tube((x0, 0.45, 0), (x1, 0.45, 0), 0.045, mat, seg=5)
        n = max(1, round((x1 - x0) / 1.6))
        for j in range(n + 1):
            x = x0 + (x1 - x0) * j / n
            k.tube((x, 0, 0), (x, 0.45, 0), 0.035, mat, seg=4)


def axial_pipe(x0, x1, r, a, rad, mat, clamp=4.0):
    with k.xf(on(0, a, r)):
        k.tube((x0, rad + 0.12, 0), (x1, rad + 0.12, 0), rad, mat, seg=10)
        x = x0 + 1.0
        while x < x1 - 0.5:
            k.box((x, rad * 0.6 + 0.06, 0), (0.3, rad * 1.2 + 0.2, rad * 2 + 0.3), 'grey')
            x += clamp


def hatch(x, a, r, rad=0.9):
    with k.xf(on(x, a, r) @ orient((0, 0, 0), (0, 1, 0))):
        k.lathe([(0, rad + 0.3), (0.16, rad + 0.3), (0.16, rad), (0.08, rad), (0.08, 0)], 'grey', seg=20, sharp=30)
        k.lathe([(0.09, rad * 0.25), (0.11, rad * 0.25), (0.11, 0)], 'glass_dark', seg=12)
        k.box((0.14, rad * 0.55, 0), (0.08, 0.12, 0.5), 'yellow')


def rcs_quad():
    """Thruster quad at the local origin, radial out = +y."""
    k.box((0, 0.3, 0), (2.2, 1.7, 2.2), 'white_s', bevel=0.14)
    k.box((0, 1.2, 0), (1.5, 0.15, 1.5), 'gold')
    for d in [(1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)]:
        with k.xf(orient((d[0] * 1.08, 0.35, d[2] * 1.08), d)):
            k.lathe([(0, 0.24), (0.22, 0.3), (0.5, 0.46), (0.9, 0.66), (1.3, 0.8)], 'bell', seg=16, sharp=60)
            k.lathe([(0.0, 0.23), (0.05, 0)], 'black', seg=10)
    for s in (1, -1):
        k.tube((s * 0.8, -1.4, 0), (s * 0.5, -0.5, 0), 0.12, 'truss', seg=6)
        k.tube((0, -1.4, s * 0.8), (0, -0.5, s * 0.5), 0.12, 'truss', seg=6)


def dock_port(lifeboat=False):
    """Radial port at the local origin on the hull, +x out of the hull."""
    k.lathe([(-0.5, 3.0), (4.2, 3.0), (4.6, 2.6)], 'white_s', seg=24, sharp=30)
    k.lathe([(4.6, 2.9), (5.3, 2.9), (5.3, 2.1), (4.6, 2.1)], 'grey', seg=24, closed=True, sharp=30)
    k.lathe([(1.2, 3.25), (1.9, 3.25), (1.9, 3.0), (1.2, 3.0)], 'blue', seg=24, closed=True, sharp=30)
    for i in range(3):
        a = i * TAU / 3 + 0.5
        with k.xf(rotX(a)):
            k.box((5.55, 2.35, 0), (0.6, 0.12, 0.8), 'grey')
    for i in range(6):
        a = i * TAU / 6
        with k.xf(rotX(a)):
            k.box((2.9, 3.12, 0), (1.6, 0.25, 0.5), 'grey')
    hr = [0.4, 2.5, 4.4]
    for a in hr:
        with k.xf(rotX(a)):
            k.tube((0.3, 3.4, 0), (4.0, 3.4, 0), 0.045, 'yellow', seg=5)
    if not lifeboat:
        k.lathe([(5.0, 2.1), (5.0, 0)], 'dark', seg=20)
        return
    # eight-seat lifeboat capsule, nose out, heat shield toward the port
    k.lathe([(5.35, 2.0), (5.5, 3.3), (5.9, 3.7), (6.6, 3.75)], 'dark', seg=28, sharp=50)
    k.lathe([(6.6, 3.75), (7.6, 3.6), (9.6, 2.6), (11.2, 1.55), (11.9, 1.35), (12.0, 0)], 'white_s', seg=28, sharp=40)
    k.lathe([(11.4, 1.5), (11.7, 1.5), (11.7, 1.3), (11.4, 1.3)], 'grey', seg=20, closed=True)
    for i in range(4):
        with k.xf(rotX(i * TAU / 4 + 0.4)):
            k.box((8.6, 3.12, 0), (0.7, 0.08, 0.6), 'glass_dark')
            with k.xf(move(7.2, 3.6, 0)):
                k.lathe([(0, 0.1), (0.1, 0.14), (0.3, 0.22)], 'bell', seg=8)


def dish(Rd=3.75, f=3.0):
    """High-gain dish, boresight +x, vertex at the origin."""
    prof = [((Rd * i / 12) ** 2 / (4 * f), Rd * i / 12) for i in range(13)]
    k.lathe(prof, 'white_s', seg=40, sharp=70)
    k.lathe([(p[0] - 0.06, p[1]) for p in prof], 'grey', seg=40, sharp=70)
    rim = prof[-1][0]
    k.torus(rim, Rd, 0.06, 'grey', seg=40, tseg=5)
    for i in range(12):
        a = i * TAU / 12
        k.tube(P(-0.5, 0.5, a), P(rim - 0.1, Rd * 0.97, a), 0.05, 'grey', seg=4)
    for i in range(3):
        a = i * TAU / 3 + 0.3
        k.tube(P(prof[9][0], prof[9][1], a), P(f, 0.2, a), 0.05, 'grey', seg=5)
    k.can(f - 0.35, f + 0.2, 0.35, 'white_s', seg=12, ch=0.05)
    k.lathe([(f - 0.35, 0.2), (f - 0.7, 0.42)], 'gold', seg=12)
    k.can(-1.0, 0.0, 0.7, 'grey', seg=16, ch=0.08)


def floodlight(loc, aim, energy=9000.0, spot=1.1, col=(1.0, 0.96, 0.9)):
    d = (Vector(aim) - Vector(loc)).normalized()
    with k.xf(orient(loc, d)):
        k.box((-0.2, 0, 0), (0.5, 0.7, 0.7), 'grey', bevel=0.04)
        k.quad([(0.06, -0.3, -0.3), (0.06, 0.3, -0.3), (0.06, 0.3, 0.3), (0.06, -0.3, 0.3)], 'lamp')
    w = k.M @ (Vector(loc) + d * 0.35)
    wd = (k.M.to_3x3() @ d).normalized()
    LIGHTS.append(('SPOT', tuple(w), tuple(wd), energy, col, spot))


def girder(a, b, size=2.0, bay=2.2, rc=0.13, rd=0.07, mat='truss'):
    """Square lattice girder between two points."""
    if k.lod < 1:
        bay *= 2
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    h = size / 2
    cs = [(-h, -h), (h, -h), (h, h), (-h, h)]
    n = max(1, round(L / bay))
    with k.xf(orient(a, b - a)):
        for c in cs:
            k.tube((0, c[0], c[1]), (L, c[0], c[1]), rc, mat, seg=6)
        for i in range(n + 1):
            x = L * i / n
            for j in range(4):
                c, c2 = cs[j], cs[(j + 1) % 4]
                k.tube((x, c[0], c[1]), (x, c2[0], c2[1]), rd, mat, seg=4)
                if i < n:
                    x2 = L * (i + 1) / n
                    p, q = (c, c2) if (i + j) % 2 else (c2, c)
                    k.tube((x, p[0], p[1]), (x2, q[0], q[1]), rd, mat, seg=4)


def rrect(x0, x1, r0, r1, ci, co, n=6):
    """Closed (x, r) outline: r0 toward the hub (corner radius ci), r1 at the rim (corner radius co)."""
    if k.lod < 1:
        n = max(1, n // 3)
    pts = []
    for cx, cr, rad, t0 in [(x1 - co, r1 - co, co, 0), (x0 + co, r1 - co, co, 90), (x0 + ci, r0 + ci, ci, 180), (x1 - ci, r0 + ci, ci, 270)]:
        for i in range(n + 1):
            t = math.radians(t0 + 90 * i / n)
            pts.append((cx + rad * math.cos(t), cr + rad * math.sin(t)))
    return pts


def arc_plate(x0, x1, r0, r1, a0, a1, mat, seg=2, vref=None):
    if k.lod < 1 and seg <= 3:
        seg = 1
    k.lathe([(x0, r0), (x1, r0), (x1, r1), (x0, r1)], mat, seg=seg, a0=a0, a1=a1, closed=True, caps=True, vref=vref, sharp=30)


def arc_decal(name, text, x0, x1, r, a0, a1, seg=24, flip=False):
    """Lettering wrapped round a rim: u along the arc, v across."""
    v, f, uv = [], [], []
    for j in range(seg + 1):
        a = a0 + (a1 - a0) * j / seg
        v.append(P(x0, r, a)); v.append(P(x1, r, a))
    for j in range(seg):
        f.append((2 * j, 2 * j + 2, 2 * j + 3, 2 * j + 1))
        u0, u1 = j / seg, (j + 1) / seg
        if flip:
            uv.append([(1 - u0, 1), (1 - u1, 1), (1 - u1, 0), (1 - u0, 0)])
        else:
            uv.append([(u0, 0), (u1, 0), (u1, 1), (u0, 1)])
    k.add('decal_' + name, v, f, uv)
    DECALS.append((name, text))


def flat_decal(name, text, p0, p1, p2, p3):
    k.quad([p0, p1, p2, p3], 'decal_' + name)
    DECALS.append((name, text))


# =====================================================================================
# aft: drive, shield, hot radiators
# =====================================================================================
def build_aft(drive):
    # --- magnetic nozzle: four superconducting coils in cryostats on a strut cage
    coils = [(-238, 17), (-233.5, 13.5), (-229.5, 10.8), (-226, 8.8)]
    for i, (x, r) in enumerate(coils):
        w = 1.25 - i * 0.12
        k.lathe(rrect(x - w, x + w, r - w, r + w, 0.35, 0.35, 3), 'steel', seg=72, closed=True, sharp=30)
        k.lathe(rrect(x - w * 0.5, x + w * 0.5, r - w - 0.12, r + w + 0.12, 0.2, 0.2, 2), 'gold', seg=72, closed=True, sharp=30)
        for j in range(12):
            a = (j + 0.5) * TAU / 12
            with k.xf(on(x, a, r + w)):
                k.box((0, 0.2, 0), (w * 2.3, 0.5, 1.0), 'grey', bevel=0.06)
    for i in range(12):
        a = i * TAU / 12
        k.tube(P(-238, 18.6, a), P(-221.5, 8.9, a), 0.3, 'truss', seg=8)
        a2 = (i + 1) * TAU / 12
        for (xa, ra), (xb, rb) in zip(coils, coils[1:]):
            p, q = (a, a2) if i % 2 else (a2, a)
            k.tube(P(xa, ra + 1.5, p), P(xb, rb + 1.4, q), 0.1, 'truss', seg=5)
    for a in (0.6, 2.7, 4.8):
        k.pipe([P(-237.5, 19.3, a), P(-226, 12.3, a), P(-221, 10.2, a), P(-196, 10.2, a)], 0.22, 'gold', seg=8)
    # throat and plasma dump
    k.lathe([(-226.5, 3.0), (-224.5, 3.6), (-222.5, 5.0), (-221, 6.6)], 'bell', seg=48, sharp=60)
    k.lathe([(-226.4, 2.9), (-224.5, 3.5), (-221.2, 6.3)], 'black', seg=48, sharp=60)
    k.lathe([(-221.4, 6.3), (-221.4, 0)], 'plasma' if drive else 'black', seg=32)
    # --- mirror fusion reactor: vacuum vessel, solenoid coils, end mirror coils, injectors
    k.lathe([(-221, 0), (-221, 6.8), (-220.6, 7.2), (-195.4, 7.2), (-195, 6.8), (-195, 0)], 'vessel', seg=48)
    for i in range(6):
        x = -218.2 + i * 4.3
        big = i in (0, 5)
        w, h = (1.5, 1.9) if big else (1.15, 1.3)
        rr = 7.3 + h
        k.lathe(rrect(x - w, x + w, 7.3, 7.3 + 2 * h, 0.3, 0.3, 3), 'white_s', seg=64, closed=True, sharp=30)
        k.lathe(rrect(x - w - 0.12, x - w + 0.25, 7.2, 7.3 + 2 * h + 0.15, 0.08, 0.08, 1), 'grey', seg=64, closed=True)
        k.lathe(rrect(x + w - 0.25, x + w + 0.12, 7.2, 7.3 + 2 * h + 0.15, 0.08, 0.08, 1), 'grey', seg=64, closed=True)
        for j in range(16):
            gbox(x, (j + 0.5) * TAU / 16, 7.3 + 2 * h, 0.5, 0.5, 0.2, 'dark')
    cage = 11.6
    for i in range(8):
        a = (i + 0.5) * TAU / 8
        k.tube(P(-221.5, cage, a), P(-195, cage, a), 0.22, 'truss', seg=8)
        for x in (-221.5, -212.5, -203.5, -195):
            k.tube(P(x, cage, a), P(x, cage, a + TAU / 8), 0.14, 'truss', seg=5)
            k.tube(P(x, cage, a), P(x, 7.4, a), 0.12, 'truss', seg=5)
        for j, x in enumerate((-221.5, -212.5, -203.5)):
            p, q = (a, a + TAU / 8) if (i + j) % 2 else (a + TAU / 8, a)
            k.tube(P(x, cage, p), P(x + 9, cage, q), 0.07, 'truss', seg=4)
    for i in range(4):          # neutral-beam injectors, angled into the plasma
        a = i * TAU / 4 + 0.3
        with k.xf(rotX(a)):
            with k.xf(orient((-205.8, 7.2, 0), (-0.45, 1, 0))):
                k.can(0, 5.5, 1.0, 'steel', seg=20)
                k.can(1.0, 1.5, 1.25, 'grey', seg=20)
                k.box((6.6, 0, 0), (2.6, 2.6, 2.4), 'white_s', bevel=0.12)
                k.box((6.6, 0, 1.3), (1.8, 1.8, 0.3), 'gold')
            k.pipe([(-208.4, 14.0, 0.8), (-208.4, 12.4, 2.2), (-200, 12.0, 2.2), (-196, 10.4, 2.2)], 0.18, 'silver', seg=6)
    for a in (0.15, 1.2, 2.25, 3.3, 4.35, 5.4):
        axial_pipe(-220, -196, 11.6, a, 0.2, R.choice(['silver', 'gold', 'white_s']), clamp=4.3)
    for x in (-216, -207, -199):
        k.torus(x, 10.4, 0.2, 'silver', seg=48, tseg=6)
    # --- direct energy converter: drum of collector grids, power conditioning round it
    k.lathe([(-195, 0), (-195, 9.6), (-194.6, 10.0), (-187.4, 10.0), (-187, 9.6), (-187, 0)], 'white', seg=56)
    for x in (-194.2, -191.0, -187.8):
        k.lathe(rrect(x - 0.3, x + 0.3, 9.9, 10.5, 0.1, 0.1, 1), 'grey', seg=56, closed=True)
    k.lathe(rrect(-193.2, -192.0, 9.95, 10.08, 0.02, 0.02, 1), 'blue', seg=56, closed=True)
    for i in range(10):
        a = i * TAU / 10
        gbox(-189.6, a, 10.0, 2.2, 3.2, 1.3, 'white_s', bevel=0.1)
        gbox(-189.6, a + TAU / 20, 10.0, 1.6, 1.4, 0.7, R.choice(['gold', 'grey', 'dark']))
        with k.xf(on(-189.6, a, 11.3)):
            k.box((0, 0.1, 0), (1.6, 0.12, 2.6), 'radw')
    # --- shadow shield: tungsten face toward the reactor, lithium hydride behind, on a strut ring
    k.lathe([(-187.2, 0), (-187.2, 12.0), (-186.6, 13.0), (-185.2, 15.2), (-183.4, 19.0), (-182.6, 20.6), (-182.2, 21.0)], 'tungsten', seg=96, sharp=25)
    k.lathe([(-182.2, 21.0), (-181.4, 21.0), (-181.0, 20.6), (-181.0, 0)], 'white', seg=96, sharp=40)
    k.torus(-181.8, 21.0, 0.45, 'grey', seg=96, tseg=8)
    for i in range(24):
        a = i * TAU / 24
        with k.xf(rotX(a)):
            k.box((-180.7, 13.2, 0), (0.6, 14.0, 0.35), 'grey')
        if i % 3 == 0:
            k.tube(P(-181, 19.5, a), P(-170, 4.0, a), 0.22, 'truss', seg=6)
    # --- thermal spine and high-temperature radiators
    k.lathe([(-181, 3.6), (-106, 3.6)], 'grey', seg=24)
    for x in np.arange(-178, -106, 8.5):
        k.lathe(rrect(x - 0.25, x + 0.25, 3.5, 4.1, 0.08, 0.08, 1), 'dark', seg=24, closed=True)
    H = D['htr']
    x0, x1 = -178.0, -178.0 + H['len']
    nseg = 8
    for q in range(4):
        with k.xf(rotX(TAU / 8 + q * TAU / 4)):
            # coolant headers out and back, glowing at the root
            k.tube((x0 - 2, 5.6, 0.9), (x1 + 1, 5.6, 0.9), 0.55, 'hotpipe', seg=10)
            k.tube((x0 - 2, 5.6, -0.9), (x1 + 1, 5.6, -0.9), 0.55, 'hotpipe', seg=10)
            for i in range(nseg):
                xa = x0 + H['len'] * i / nseg + 0.25
                xb = x0 + H['len'] * (i + 1) / nseg - 0.25
                wa = H['w0'] + (H['w1'] - H['w0']) * (xa - x0) / H['len']
                wb = H['w0'] + (H['w1'] - H['w0']) * (xb - x0) / H['len']
                t = 0.14
                v = [(xa, 6.6, -t), (xb, 6.6, -t), (xb, 6.6 + wb, -t), (xa, 6.6 + wa, -t),
                     (xa, 6.6, t), (xb, 6.6, t), (xb, 6.6 + wb, t), (xa, 6.6 + wa, t)]
                f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)]
                uvf = [(xa, 0), (xb, 0), (xb, 1), (xa, 1)]
                uvs = [[uvf[0], uvf[3], uvf[2], uvf[1]], uvf, [uvf[0], uvf[1], uvf[1], uvf[0]], [uvf[2], uvf[3], uvf[3], uvf[2]],
                       [uvf[1], uvf[2], uvf[2], uvf[1]], [uvf[3], uvf[0], uvf[0], uvf[3]]]
                k.add('hot', v, f, uvs)
                # frame: ribs at the joints and the edge boom
                k.box((xa - 0.25, 6.6 + wa / 2, 0), (0.22, wa, 0.5), 'cc')
                k.tube((xa, 6.6 + wa + 0.25, 0), (xb, 6.6 + wb + 0.25, 0), 0.2, 'cc', seg=6)
                k.tube((xa, 6.0, 0.9), (xa, 6.8, 0), 0.2, 'hotpipe', seg=6)
            k.box((x1 + 0.05, 6.6 + H['w1'] / 2, 0), (0.22, H['w1'], 0.5), 'cc')
            for xs_ in (x0 + 17, x0 + 34, x0 + 51, x1):       # stay rods to the spine keep the panels flat
                ws = H['w0'] + (H['w1'] - H['w0']) * (xs_ - x0) / H['len']
                for s in (1, -1):
                    k.tube((xs_, 6.6 + ws, 0), (xs_ - 9, 3.8, s * 2.2), 0.06, 'cc', seg=4)
    # --- aft RCS
    for q in range(4):
        with k.xf(rotX(q * TAU / 4)):
            k.tube((-114, 3.6, 0), (-114, 12.0, 0), 0.35, 'truss', seg=8)
            k.tube((-118, 3.6, 0), (-114, 11.5, 0), 0.15, 'truss', seg=6)
            k.tube((-110, 3.6, 0), (-114, 11.5, 0), 0.15, 'truss', seg=6)
            with k.xf(move(-114, 13.4, 0)):
                rcs_quad()


# =====================================================================================
# mid: tanks, spine truss, aft ring, yard
# =====================================================================================
def spine_truss(x0, x1, h, nb):
    cs = [(-h, -h), (h, -h), (h, h), (-h, h)]
    for c in cs:
        k.tube((x0, c[0], c[1]), (x1, c[0], c[1]), 0.5, 'truss', seg=12)
    for i in range(nb + 1):
        x = x0 + (x1 - x0) * i / nb
        for j in range(4):
            c, c2 = cs[j], cs[(j + 1) % 4]
            k.tube((x, c[0], c[1]), (x, c2[0], c2[1]), 0.34, 'truss', seg=8)
            k.sphere((x, c[0], c[1]), 0.85, 'grey', seg=12, rings=6)
            with k.xf(move(x, c[0], c[1])):
                k.can(-1.3, 1.3, 0.62, 'grey', seg=12, ch=0.08)
        a, b = (cs[0], cs[2]) if i % 2 else (cs[1], cs[3])
        k.tube((x, a[0], a[1]), (x, b[0], b[1]), 0.16, 'truss', seg=6)
        if i < nb:
            x2 = x0 + (x1 - x0) * (i + 1) / nb
            for j in range(4):
                c, c2 = cs[j], cs[(j + 1) % 4]
                p, q = (c, c2) if (i + j) % 2 else (c2, c)
                k.tube((x, p[0], p[1]), (x2, q[0], q[1]), 0.24, 'truss', seg=8)
                k.tube((x, q[0], q[1]), (x2, p[0], p[1]), 0.07, 'truss', seg=4)
            xm = (x + x2) / 2      # mid-bay batten keeps the longerons from buckling
            for j in range(4):
                c, c2 = cs[j], cs[(j + 1) % 4]
                k.tube((xm, c[0], c[1]), (xm, c2[0], c2[1]), 0.12, 'truss', seg=5)


def build_mid():
    T = D['tanks']
    # --- fusion fuel cryotanks: D2 and He-3 dewars in foil
    for s in (1, -1):
        k.sphere((-103, 0, s * 15.5), 4.0, 'gold', seg=40, rings=20)
        with k.xf(move(-103, 0, s * 15.5)):
            with k.xf(rotY(PI / 2)):
                k.torus(0, 4.05, 0.16, 'grey', seg=40, tseg=6)
            k.torus(0, 4.05, 0.16, 'grey', seg=40, tseg=6)
        for dx in (-2.4, 2.4):
            for dy in (-2.4, 2.4):
                k.tube((-103 + dx, dy, s * 12.6), (-103 + dx * 1.6, dy * 1.6, s * 10), 0.14, 'truss', seg=6)
        k.pipe([(-103, 1.0, s * 11.4), (-103, 1.0, s * 9.0), (-106, 1.0, s * 3.4)], 0.16, 'gold', seg=6)
        gbox(-103, PI / 2 * s, 19.5, 1.2, 1.2, 0.6, 'grey')
    # --- water tank farm: six tanks on cradle rings, feed manifold
    for q in range(T['n']):
        a = q * TAU / T['n']
        y, z = 19 * math.cos(a), 19 * math.sin(a)
        k.dome_tank(-98, -54, T['r'], 'tank', k=0.55, seg=40, rings=8, oy=y, oz=z)
        for x in (-92.5, -86, -76, -66, -59.5):
            k.lathe(rrect(x - 0.22, x + 0.22, T['r'] - 0.02, T['r'] + 0.1, 0.03, 0.03, 1), 'grey', seg=40, closed=True, oy=y, oz=z)
        for x in (-88, -64):
            k.lathe(rrect(x - 0.6, x + 0.6, T['r'], T['r'] + 0.4, 0.1, 0.1, 1), 'grey', seg=40, closed=True, oy=y, oz=z)
            for da in (-0.5, 0.5):
                k.tube(P(x, 11.2, a + da * 0.6), (x, y + (T['r'] + 0.3) * math.cos(a + PI + da * 1.3), z + (T['r'] + 0.3) * math.sin(a + PI + da * 1.3)), 0.28, 'truss', seg=6)
        with k.xf(rotX(a)):
            k.pipe([(-55.2, 19, 0), (-52.5, 19, 0), (-50, 9.0, 0)], 0.38, 'silver', seg=8)
            k.pipe([(-96.8, 19, 0), (-99.5, 19, 0), (-101, 10.5, 0)], 0.22, 'silver', seg=6)
            k.box((-52.5, 19.6, 0), (1.4, 1.0, 1.4), 'grey', bevel=0.06)
            hrail(-90, -62, 24.0, 0.0)
            hatch(-76, 0.0, 24.0, 0.6)
        with k.xf(move(0, y, z)):
            with k.xf(rotX(a)):
                greeble(-94, -58, T['r'], 5, a0=-0.5, span=1.0, hmax=0.5)
    k.torus(-50, 9.0, 0.5, 'silver', seg=48, tseg=8)
    k.torus(-101, 10.5, 0.3, 'silver', seg=48, tseg=6)
    # --- refuelling booms: dorsal deployed, ventral stowed
    for s, pts in ((1, [(-62, 0, 13), (-74, 0, 30), (-84, 0, 44)]), (-1, [(-62, 0, -13.5), (-76, 0, -14.5), (-90, 0, -15)])):
        k.box((-62, 0, s * 12.3), (6, 5, 3), 'white_s', bevel=0.2)
        with k.xf(move(-62, 0, s * 13.4)):
            with k.xf(rotZ(PI / 2)):
                k.can(-1.4, 1.4, 1.5, 'grey', seg=20)
        k.tube(pts[0], pts[1], 0.85, 'white_s', seg=14)
        k.tube(pts[1], pts[2], 0.6, 'white_s', seg=14)
        k.sphere(pts[1], 1.1, 'grey', seg=14, rings=8)
        d = Vector(pts[2]) - Vector(pts[1])
        with k.xf(orient(pts[2], d)):
            k.lathe([(0, 0.6), (1.0, 0.7), (3.0, 2.3)], 'orange', seg=20, sharp=60)
            k.lathe([(0.4, 0.5), (3.0, 0.5), (3.3, 0.3), (3.3, 0)], 'grey', seg=12)
        k.pipe([Vector(pts[0]) + Vector((0, 0.9, 0)), Vector(pts[1]) + Vector((0, 0.9, 0)), Vector(pts[2]) + Vector((0, 0.7, 0))], 0.2, 'silver', seg=6)
    # --- spine truss and the utility run inside it
    spine_truss(-106, 168, 10, 14)
    k.tube((-104, 3, 3), (166, 3, 3), 0.9, 'silver', seg=14)
    k.tube((-104, -3, -3), (166, -3, -3), 0.55, 'gold', seg=12)
    k.tube((-104, -3.2, 3), (166, -3.2, 3), 0.42, 'white_s', seg=10)
    k.tube((-104, 3.2, -3), (166, 3.2, -3), 0.42, 'white_s', seg=10)
    k.tube((-104, 0, -4.6), (166, 0, -4.6), 0.3, 'gold', seg=8)
    k.boxX(-104, 166, 6.2, -8.6, 1.3, 0.3, 'dark')
    k.boxX(-104, 166, -6.2, 8.6, 1.3, 0.3, 'dark')
    for x in np.arange(-102, 166, 274 / 28):
        k.box((x, 0, 0), (0.35, 9.5, 0.35), 'grey')
        k.box((x, 0, 0), (0.35, 0.35, 9.5), 'grey')
        for (y, z, r_) in ((3, 3, 1.05), (-3, -3, 0.7), (-3.2, 3, 0.55), (3.2, -3, 0.55)):
            with k.xf(move(x, y, z)):
                k.can(-0.2, 0.2, r_, 'grey', seg=12, ch=0.04)
        for (y, z) in ((6.2, -8.9), (-6.2, 8.3)):
            k.box((x, y, z), (0.4, 1.6, 0.5), 'grey')
    # equipment pallets on the port and starboard truss faces
    for s in (1, -1):
        for x in np.arange(-98, 96, 9.8):
            t = R.random()
            y = s * 10.5
            if t < 0.3:
                k.box((x, y + s * 0.5, R.uniform(-5, 5)), (R.uniform(3, 6), 1.0, R.uniform(2, 4)), R.choice(['white_s', 'gold', 'grey']), bevel=0.1)
            elif t < 0.5:
                for dz in (-1.5, 1.5):
                    for dx in (-1.5, 1.5):
                        k.sphere((x + dx, y + s * 1.2, dz), 1.15, R.choice(['silver', 'gold']), seg=16, rings=8)
                k.box((x, y + s * 0.2, 0), (5.6, 0.3, 5.6), 'grey')
            elif t < 0.7:
                k.box((x, y + s * 0.35, 0), (4.5, 0.2, 6.5), 'radw')
                k.box((x, y + s * 0.15, 0), (4.9, 0.25, 0.4), 'grey')
            elif t < 0.85:
                z0 = R.uniform(-4, 4)
                k.box((x, y + s * 0.7, z0), (2.2, 1.4, 2.0), 'white_s', bevel=0.1)
                k.box((x + 2.4, y + s * 0.5, z0), (1.6, 1.0, 1.4), 'gold', bevel=0.08)
                k.tube((x, y + s * 1.4, z0), (x, y + s * 3.4, z0), 0.06, 'grey', seg=5)
    # EVA rail and cart along the dorsal port longeron
    k.boxX(-104, 166, 10.9, 10.2, 0.25, 0.25, 'yellow')
    k.box((-70, 11.3, 10.6), (2.4, 1.2, 0.9), 'white_s', bevel=0.08)
    # --- flywheel housing, aft hub and aft workshop ring
    k.lathe([(-48, 0), (-48, 15.0), (-47.5, 15.5), (-36.5, 15.5), (-36, 15.0)], 'grey', seg=64)
    k.lathe(rrect(-43.2, -40.8, 15.45, 15.95, 0.08, 0.08, 1), 'orange', seg=64, closed=True)
    for i in range(16):
        gbox(-44.5 + (i % 2) * 5, i * TAU / 16, 15.5, 1.6, 1.2, 0.5, 'dark')
    hub(-36, -8, 15, -22, D['ringA'])
    k.group = 'RingA'
    hab_ring(-22, D['ringA'], phase=0.33, nmod=16, name=None, workshop=True)
    k.group = 'Hull'
    build_yard()


def hub(x0, x1, r, xr, ring):
    """Despun hub: skin, bearing races either side of the ring, pipes, rails, hatches."""
    w = ring['w']
    k.lathe([(x0, 0), (x0, r - 0.4), (x0 + 0.4, r), (x1 - 0.4, r), (x1, r - 0.4), (x1, 0)], 'white', seg=96)
    for x in (xr - w / 2 - 1.6, xr + w / 2 + 1.6):
        k.lathe(rrect(x - 1.0, x + 1.0, r - 0.1, r + 1.5, 0.3, 0.3, 2), 'grey', seg=96, closed=True)
        k.lathe(rrect(x - 0.35, x + 0.35, r + 1.4, r + 1.75, 0.08, 0.08, 1), 'dark', seg=96, closed=True)
        for j in range(24):
            gbox(x, j * TAU / 24, r + 1.5, 0.9, 1.0, 0.35, 'grey')
    for a in (0.35, 1.95, 3.5, 5.1):
        axial_pipe(x0 + 0.6, xr - w / 2 - 2.7, r, a, 0.3, 'gold')
        axial_pipe(xr + w / 2 + 2.7, x1 - 0.6, r, a, 0.3, 'gold')
        axial_pipe(x0 + 0.6, xr - w / 2 - 2.7, r, a + 0.12, 0.16, 'silver')
        axial_pipe(xr + w / 2 + 2.7, x1 - 0.6, r, a + 0.12, 0.16, 'silver')


def spin_group(name, xc, g, r, direction):
    """Start a turning group: parts added until k.group changes go under node `name`, pivot on the axis at xc.
    direction is the spin about the bow axis (+x), as on the class pages."""
    k.group = name
    PIVOTS[name] = (xc, 0, 0)
    SPIN[name] = -direction * math.sqrt(g * G0 / r)


def hab_ring(xc, ring, phase, nmod, name, workshop=False, reg='FT-05   UNITED EARTH', rh=15.0):
    """Spin-gravity ring: one continuous pressure hull, bumper tiles on the rim, spokes and stay rods."""
    rIn, rOut, w, nsp = ring['rIn'], ring['r'], ring['w'], ring['n']
    if k.group != 'Hull':
        PIVOTS[k.group] = (xc, 0, 0)
        SPIN[k.group] = -ring.get('dir', 1) * math.sqrt(ring['g'] * G0 / rOut)
    hr = rOut - 0.5
    xw = w / 2 - 0.5
    with k.xf(move(xc, 0, 0) @ rotX(phase)):
        seg = 288
        k.lathe(rrect(-xw, xw, rIn, hr, 2.8, 1.0, 6), 'white_m', seg=seg, closed=True, sharp=25)
        for i in range(nmod):
            a = i * TAU / nmod
            da = 0.32 / rOut
            k.lathe(rrect(-xw - 0.16, xw + 0.16, rIn - 0.16, hr + 0.1, 2.9, 1.1, 4), 'grey', seg=1, a0=a - da, a1=a + da, closed=True, caps=True)
        # rim bumper: spaced plates over the water jacket
        ntile = int(TAU * rOut / (2.5 if k.lod >= 1 else 5.0))
        nrow = 6
        rw = (2 * xw - 1.4) / nrow
        band = set()
        if name:
            for c in (0.0, TAU / 3, 2 * TAU / 3):
                i0 = int((c + 0.36) / TAU * ntile)
                band.update(range(i0, i0 + int(ntile * 0.11)))
        for i in range(ntile):
            a0 = (i + 0.035) * TAU / ntile
            a1 = (i + 0.965) * TAU / ntile
            for j in range(nrow):
                if R.random() < 0.012:
                    continue
                xa = -xw + 0.7 + j * rw + 0.05
                mat = 'tile' if R.random() > 0.035 else 'tile2'
                if j in (2, 3) and i % ntile in band:
                    mat = 'blue'
                arc_plate(xa, xa + rw - 0.1, rOut - 0.1, rOut, a0, a1, mat, vref=rOut)
        for j in range(nrow + 1):
            k.torus(-xw + 0.7 + j * rw, rOut - 0.28, 0.1, 'grey', seg=seg, tseg=4)
        if name:
            for c in (0.0, TAU / 3, 2 * TAU / 3):
                arc_decal('name', name, -2.6, 2.6, rOut + 0.03, c - 0.30, c + 0.30, seg=40)
                arc_decal('reg', reg, -1.1, 1.1, rOut + 0.03, c + 0.37, c + 0.37 + 0.4, seg=30)
        # side walls: frames, windows on two decks, rails
        for s in (1, -1):
            xs = s * (xw + 0.02)
            for i in range(nmod * 4):
                a = (i + 0.5) * TAU / (nmod * 4)
                with k.xf(rotX(a)):
                    k.box((xs, (rIn + hr) / 2 + 0.9, 0), (0.16, hr - rIn - 4.4, 0.22), 'white_s')
            rows = [(rOut - 2.6, 0.72), (rOut - 5.9, 0.6)] if not workshop else [(rOut - 3.2, 0.35)]
            for (rw_, lit) in rows:
                nw = int(TAU * rw_ / 3.4)
                for i in range(nw):
                    if (i % 6) in (4, 5) or R.random() < 0.06:
                        continue
                    a = (i + 0.5) * TAU / nw
                    with k.xf(rotX(a)):
                        k.box((xs + s * 0.03, rw_, 0), (0.1, 1.2, 1.5), 'white_s', bevel=0.0)
                        k.box((xs + s * 0.06, rw_, 0), (0.08, 0.8, 1.1), 'glass' if R.random() < lit else 'glass_dark')
            for rr in (rOut - 1.3, rIn + 3.4):
                k.torus(xs + s * 0.45, rr, 0.045, 'yellow', seg=seg, tseg=4)
            for i in range(nmod * 2):
                a = (i + 0.25) * TAU / (nmod * 2)
                with k.xf(rotX(a)):
                    t = R.random()
                    if t < 0.4:
                        k.box((xs + s * 0.25, R.uniform(rIn + 3.5, hr - 4.2), 0), (0.5, R.uniform(0.8, 2.2), R.uniform(0.8, 2.4)), R.choice(GM), bevel=0.05)
                    elif t < 0.5:
                        with k.xf(move(xs, rOut - 4.2, 0) @ orient((0, 0, 0), (s, 0, 0))):
                            k.lathe([(0, 1.2), (0.2, 1.2), (0.2, 0.9), (0.1, 0.9), (0.1, 0)], 'grey', seg=16, sharp=30)
        # hub-facing roof: skylights, conduits, antenna farm
        for i in range(nmod * 3):
            a0 = (i + 0.2) * TAU / (nmod * 3)
            a1 = (i + 0.8) * TAU / (nmod * 3)
            arc_plate(-1.5, 1.5, rIn - 0.1, rIn + 0.02, a0, a1, 'grey', seg=3)
            arc_plate(-1.25, 1.25, rIn - 0.14, rIn - 0.08, a0 + 0.004, a1 - 0.004, 'glass' if R.random() < (0.25 if workshop else 0.6) else 'glass_dark', seg=3)
        for xx, mat in ((-4.2, 'silver'), (4.2, 'gold'), (-5.0, 'white_s')):
            k.torus(xx, rIn - 0.25, 0.2, mat, seg=seg, tseg=6)
        for i in range(nmod * 2):
            a = i * TAU / (nmod * 2) + 0.05
            with k.xf(rotX(a) @ move(R.uniform(-5.5, 5.5), rIn, 0) @ rotZ(PI)):
                k.box((0, 0.35, 0), (R.uniform(1, 2.6), 0.7, R.uniform(1, 2.2)), R.choice(GM), bevel=0.06)
        # elevator spokes with landing lobbies, and stay rods like a bicycle wheel
        for q in range(nsp):
            a = q * TAU / nsp + TAU / (2 * nsp)
            with k.xf(rotX(a)):
                k.tube((0, rh + 1.2, 0), (0, rIn + 0.6, 0), 2.1, 'white_m', seg=28)
                y = rh + 6
                while y < rIn - 3:
                    ycan(y, y + 0.5, 2.32, 'grey', seg=28, ch=0.08)
                    y += 8.2
                ycan(rh + 1.0, rh + 4.5, 3.3, 'grey', seg=28, ch=0.5)
                k.box((0, rIn - 2.2, 0), (8.5, 5.0, 7.5), 'white_s', bevel=0.5)
                k.box((0, rIn - 4.9, 0), (6.0, 0.5, 5.2), 'grey', bevel=0.1)
                for zz in (2.25, -2.25):
                    k.tube((0.6, rh + 4.5, zz), (0.6, rIn - 4.5, zz), 0.18, 'gold' if zz > 0 else 'silver', seg=6)
                k.tube((2.2, rh + 4.5, 0), (2.2, rIn - 4.5, 0), 0.06, 'yellow', seg=4)
                for i in range(4):
                    k.box((4.3 if i % 2 else -4.3, rIn - 2.2, 1.8 if i < 2 else -1.8), (0.12, 1.1, 1.5), 'glass')
            for sgn in (1, -1):
                for dx in (1, -1):
                    k.tube(P(dx * 6.5, rh + 1.6, a), P(dx * (xw - 2.5), rIn + 0.5, a + sgn * TAU / nsp / 2), 0.09, 'truss', seg=5)
        # rotating collar on the hub
        k.lathe([(-w / 2 - 0.5, rh + 0.2), (-w / 2 - 0.5, rh + 1.2), (w / 2 + 0.5, rh + 1.2), (w / 2 + 0.5, rh + 0.2)], 'white_s', seg=96)
        greeble(-w / 2, w / 2, rh + 1.2, 26, hmax=0.6)
        if workshop:      # freight doors on the rim-side walls
            for i in range(4):
                a = i * TAU / 4 + 0.2
                with k.xf(rotX(a)):
                    k.box((xw + 0.06, rOut - 4.2, 0), (0.14, 4.6, 6.0), 'grey', bevel=0.05)
                    k.box((xw + 0.12, rOut - 4.2, 0), (0.1, 4.0, 5.4), 'white_s')
                    for zz in (-2.9, 2.9):
                        k.box((xw + 0.2, rOut - 4.2, zz), (0.1, 4.6, 0.3), 'hazard')


def build_yard():
    # --- repair cradle (dorsal), x -6..96: lattice frames, crane rails on top
    xs = [-6, 14.4, 34.8, 55.2, 75.6, 96]
    for x in xs:
        girder((x, -30, 12), (x, 30, 12), 2.2)
        for y in (-30, 30):
            girder((x, y, 12), (x, y, 58), 2.2)
            k.box((x, y, 12), (2.8, 2.8, 2.8), 'grey', bevel=0.15)
            k.box((x, y, 58), (2.8, 2.8, 2.8), 'grey', bevel=0.15)
        for y in (-10, 10):
            k.tube((x, y, 10.5), (x, y, 11.2), 0.6, 'grey', seg=10)
    for y in (-30, 30):
        girder((-6, y, 12), (96, y, 12), 2.2)
        girder((-6, y, 58), (96, y, 58), 2.6, rc=0.16)
        k.boxX(-7.5, 97.5, y, 59.55, 0.7, 0.35, 'orange')
        for i in range(5):
            k.tube((xs[i], y, 13), (xs[i + 1], y, 57), 0.1, 'truss', seg=5)
            k.tube((xs[i], y, 57), (xs[i + 1], y, 13), 0.1, 'truss', seg=5)
        k.box((-7.5, y, 59.5), (0.5, 1.6, 1.4), 'hazard')
        k.box((97.5, y, 59.5), (0.5, 1.6, 1.4), 'hazard')
    # gantry cranes
    for x, ty in ((18, -6), (62, 8)):
        girder((x - 1.6, -31.5, 61.6), (x - 1.6, 31.5, 61.6), 2.4, rc=0.15, mat='orange')
        girder((x + 1.6, -31.5, 61.6), (x + 1.6, 31.5, 61.6), 2.4, rc=0.15, mat='orange')
        for y in (-30, 30):
            k.box((x, y, 60.4), (7.5, 2.4, 1.3), 'orange', bevel=0.12)
            for dx in (-2.8, 2.8):
                with k.xf(move(x + dx, y, 59.9) @ rotZ(PI / 2)):
                    k.can(-0.5, 0.5, 0.42, 'dark', seg=12)
        k.box((x, ty, 60.2), (5.6, 4.6, 1.6), 'grey', bevel=0.15)
        with k.xf(move(x, ty, 59.2) @ rotZ(PI / 2)):
            k.can(-1.6, 1.6, 0.8, 'dark', seg=16)
        k.box((x + 1.9, ty, 61.3), (1.2, 2.0, 1.2), 'white_s', bevel=0.08)
        for dy in (-0.5, 0.5):
            k.tube((x, ty + dy, 58.6), (x, ty + dy, 42.4), 0.05, 'dark', seg=4)
        k.box((x, ty, 41.6), (1.4, 1.8, 1.6), 'hazard', bevel=0.1)
        k.tube((x, ty, 40.8), (x, ty, 39.6), 0.16, 'grey', seg=6)
        floodlight((x, ty + 2.6, 59.2), (x + 6, ty, 36), energy=14000, spot=1.2)
    # --- docked tug under refit
    build_tug(32)
    for x in (14, 36, 58):
        for s in (1, -1):
            k.tube((x, s * 29, 20), (x, s * 6.6, 31), 0.5, 'grey', seg=10)
            with k.xf(orient((x, s * 6.6, 31), (-s, 0, 0.45))):
                k.box((0.2, 0, 0), (0.5, 3.2, 2.6), 'hazard', bevel=0.1)
            with k.xf(orient((x, s * 29, 20), (0, s, 0))):
                k.can(-0.4, 1.0, 1.0, 'dark', seg=14)
        k.tube((x, 0, 13.2), (x, 0, 25.6), 0.7, 'grey', seg=12)
        k.box((x, 0, 25.9), (2.6, 4.4, 0.6), 'hazard', bevel=0.1)
    # manipulator arms on rail carriages
    for pts, sy in (([(30, -30, 60.4), (34, -24, 70), (42, -12, 62), (46, -6, 44.5)], -1), ([(70, 30, 60.4), (66, 24, 70), (58, 13, 63), (52, 6, 44.5)], 1)):
        k.box((pts[0][0], pts[0][1], 60.3), (4.2, 2.6, 1.5), 'grey', bevel=0.12)
        rad = [0.62, 0.5, 0.38]
        for i in range(3):
            k.tube(pts[i], pts[i + 1], rad[i], 'white_s', seg=14)
            d = (Vector(pts[i + 1]) - Vector(pts[i])).normalized()
            for f in (0.3, 0.7):
                p = Vector(pts[i]).lerp(Vector(pts[i + 1]), f)
                with k.xf(orient(p, d)):
                    k.can(-0.15, 0.15, rad[i] + 0.06, 'grey', seg=14, ch=0.03)
        for i, p in enumerate(pts[:3]):
            ax = (Vector(pts[i + 1]) - Vector(p)).cross(Vector((0, 0, 1))).normalized()
            with k.xf(orient(p, ax)):
                k.can(-1.0, 1.0, 0.95 - i * 0.12, 'grey', seg=18, ch=0.1)
                k.can(-1.15, 1.15, 0.5, 'gold', seg=12)
        d = (Vector(pts[3]) - Vector(pts[2])).normalized()
        with k.xf(orient(pts[3], d)):
            k.can(-0.2, 1.4, 0.55, 'grey', seg=16)
            k.can(1.4, 1.7, 0.7, 'dark', seg=16)
            k.box((0.5, 0.8, 0), (0.6, 0.5, 0.5), 'dark')
        floodlight(Vector(pts[2]) + Vector((0, 0, -1.2)), (pts[3][0], pts[3][1], 36), energy=6000, spot=1.0)
    # floodlight masts on the frame tops
    for x in (14.4, 55.2, 96):
        for y in (-30, 30):
            k.tube((x, y, 59.4), (x, y, 64.5), 0.12, 'grey', seg=6)
            floodlight((x, y * 0.97, 64.3), (x - 6, 0, 32), energy=20000, spot=0.9)
    for x in (34.8, 75.6):
        for y in (-30, 30):
            floodlight((x, y * 0.95, 30), (x, 0, 30), energy=7000, spot=1.3)
    # work platforms round the tug's engine section
    for s in (1, -1):
        k.box((9, s * 9.5, 26.2), (10, 3.2, 0.2), 'grey')
        for x in (4.2, 9, 13.8):
            k.tube((x, s * 11, 26.2), (x, s * 11, 27.4), 0.04, 'yellow', seg=4)
            k.tube((x, s * 11, 12), (x, s * 11, 26), 0.1, 'truss', seg=5)
        k.tube((4.2, s * 11, 27.4), (13.8, s * 11, 27.4), 0.04, 'yellow', seg=4)
        k.box((R.uniform(6, 12), s * 9.2, 26.8), (1.6, 1.2, 1.0), R.choice(['white_s', 'orange']), bevel=0.06)
    # --- pressurized repair shop: a drum, because flat walls do not hold pressure
    with k.xf(move(0, 0, 22)):
        prof = [(80.4, 0), (80.5, 4.0), (80.9, 7.6), (81.8, 9.2), (83.0, 9.8), (92.0, 9.8), (93.2, 9.2), (94.1, 7.6), (94.5, 4.0), (94.6, 0)]
        k.lathe(prof, 'white', seg=72, sharp=30)
        for x in (83.0, 87.5, 92.0):
            k.lathe(rrect(x - 0.25, x + 0.25, 9.7, 10.15, 0.06, 0.06, 1), 'grey', seg=72, closed=True)
        for i in range(24):
            a = i * TAU / 24
            with k.xf(rotX(a)):
                k.box((87.5, 9.92, 0), (9.0, 0.2, 0.16), 'white_s')
        greeble(83.5, 91.5, 9.8, 18, a0=-0.3, span=PI + 0.6, hmax=0.6)
        for a in (0.6, 1.57, 2.5):
            hrail(83.5, 91.5, 9.8, a)
        # cupola on top
        zcan(9.6, 11.2, 1.6, 'grey', x=87.5, seg=20)
        zcan(11.2, 11.5, 1.3, 'glass_dark', x=87.5, seg=20, ch=0.05)
        for i in range(6):
            a = i * TAU / 6
            k.box((87.5 + 1.55 * math.cos(a), 1.55 * math.sin(a), 10.5), (0.5, 0.5, 0.9), 'glass')
    # airlock facing the cradle
    with k.xf(move(0, 0, 24)):
        k.lathe([(75.6, 0), (75.6, 2.7), (75.9, 3.0), (80.8, 3.0)], 'white_s', seg=32)
        k.lathe(rrect(75.5, 76.0, 2.9, 3.35, 0.06, 0.06, 1), 'hazard', seg=32, closed=True)
        k.lathe([(75.55, 2.0), (75.5, 0)], 'grey', seg=24)
        for a in (0.5, 2.0, 3.6, 5.2):
            hrail(76.4, 80, 3.0, a)
    floodlight((79.5, 0, 28.2), (60, 0, 32), energy=9000, spot=1.2)
    k.box((87.5, -9.8, 23.8), (7.8, 0.1, 2.2), 'white_s')
    flat_decal('bay', 'REPAIR BAY 1', (84.0, -9.9, 23.0), (91.0, -9.9, 23.0), (91.0, -9.9, 24.6), (84.0, -9.9, 24.6))
    # --- stores racks (ventral): 36 containers in a tube frame
    for x in xs[:5]:
        for y in (-14, 0, 14):
            k.tube((x - 4.3, y, -11), (x - 4.3, y, -34.6), 0.22, 'truss', seg=6)
        for z in (-12.6, -20.0, -27.0, -34.4):
            k.tube((x - 4.3, -14, z), (x - 4.3, 14, z), 0.18, 'truss', seg=6)
    for y in (-14, 0, 14):
        for z in (-12.6, -34.4):
            k.tube((-10.3, y, z), (79, y, z), 0.2, 'truss', seg=6)
    cols = ['ctr0', 'ctr1', 'ctr2', 'ctr3', 'ctr4', 'ctr1', 'ctr0', 'ctr2']
    n = 0
    for x in range(-3, 68, 14):
        for y in (-7, 7):
            for z in (-16.5, -23.5, -30.5):
                n += 1
                if R.random() < 0.07:
                    continue
                c = (x + 6.5, y, z)
                k.box(c, (12.0, 12.2, 6.3), R.choice(cols), bevel=0.1)
                for dx in (-6.0, 6.0):
                    k.box((c[0] + dx, y, z), (0.25, 12.4, 6.5), 'grey')
                for dy in (-6.1, 6.1):
                    for dz in (-3.15, 3.15):
                        k.box((c[0], y + dy, z + dz), (12.2, 0.22, 0.22), 'grey')
                sy = 1 if y > 0 else -1
                k.box((c[0] - 3.5, y + sy * 6.14, z + 1.2), (2.4, 0.04, 1.4), 'white_s')
                k.box((c[0] + 3.0, y + sy * 6.14, z - 0.4), (0.3, 0.1, 4.6), 'grey')
    # --- lander propellant plant: electrolysis, liquefaction, LH2 and LOX dewars
    k.box((88, -6.5, -23), (12, 13, 19), 'white', bevel=0.25)
    for z in (-28, -23, -18):
        k.box((88, -6.5, z), (12.3, 13.3, 0.35), 'grey')
    for i in range(4):
        zcan(-34.5, -32.5, 1.3, 'steel', x=84.5 + i * 2.4, y=-9.5, seg=14)
    k.box((88, -6.5, -13.2), (8.6, 8.6, 0.35), 'radw')
    k.box((88, -13.2, -22), (9, 0.3, 12), 'radw')
    k.sphere((88, 7, -19), 4.6, 'gold', seg=40, rings=20)
    k.sphere((88, 7, -29), 3.6, 'silver', seg=36, rings=18)
    for zc, rr in ((-19, 4.65), (-29, 3.65)):
        with k.xf(move(88, 7, zc)):
            k.torus(0, rr, 0.14, 'grey', seg=36, tseg=5)
        for dx in (-2.5, 2.5):
            k.tube((88 + dx, 2.6, zc), (88 + dx, 0.2, zc + 1.0), 0.16, 'truss', seg=6)
    k.pipe([(88, 7, -14.4), (88, 4, -11.6), (88, 4, -10.4)], 0.22, 'gold', seg=6)
    k.pipe([(86, 0, -13.5), (86, 0, -10.5)], 0.22, 'silver', seg=6)
    k.pipe([(88, 7, -25.4), (86.5, 7, -24.4), (86.5, 0.5, -24.4)], 0.18, 'silver', seg=6)
    # --- fabrication modules (dorsal pair) and foundry (ventral)
    for y in (-8, 8):
        with k.xf(move(0, y, 17)):
            k.dome_tank(100.5, 164.5, 6.5, 'white_m', k=0.45, seg=56, rings=8)
            for x in (108, 114, 123, 132, 141, 150, 156):
                k.lathe(rrect(x - 0.3, x + 0.3, 6.45, 6.85, 0.08, 0.08, 1), 'grey', seg=56, closed=True)
            sgn = 1 if y > 0 else -1
            greeble(104, 160, 6.5, 46, a0=PI / 2 - sgn * 1.9, span=2.6, hmax=0.7)
            for a in (PI / 2 - sgn * 0.6, PI / 2 - sgn * 1.2, PI / 2 + sgn * 0.3):
                hrail(105, 159, 6.5, a)
            for x in (118, 136, 146):
                hatch(x, PI / 2 - sgn * 0.9, 6.5, 0.85)
            for i in range(11):
                x = 106 + i * 5
                with k.xf(on(x, PI / 2 - sgn * 0.35, 6.5)):
                    k.box((0, 0.02, 0), (1.5, 0.08, 1.0), 'grey')
                    k.box((0, 0.05, 0), (1.2, 0.06, 0.75), 'glass' if R.random() < 0.6 else 'glass_dark')
            with k.xf(on(126, PI / 2 - sgn * 1.45, 6.5)):
                k.box((0, 3.0, 0), (9, 0.25, 6), 'radw')
                k.tube((-3, 0, 0), (-3, 3, 0), 0.12, 'grey', seg=5)
                k.tube((3, 0, 0), (3, 3, 0), 0.12, 'grey', seg=5)
        for x in (108, 140, 158):
            k.tube((x, y * 0.6, 10.2), (x, y, 11.2), 0.5, 'grey', seg=10)
    for x in (120, 148):
        with k.xf(orient((x, -3, 17), (0, 1, 0))):
            k.can(0, 6, 1.7, 'white_s', seg=20)
    k.tube((164, 8, 17), (167, 6, 14.6), 1.4, 'white_s', seg=16)
    k.tube((164, -8, 17), (167, -6, 14.6), 1.4, 'white_s', seg=16)
    # foundry: an open-frame process plant, furnaces in a cold frame
    k.box((131, 0, -21), (54, 24, 18), 'grey_p', bevel=0.3)
    for x in np.arange(106, 158, 6.5):
        k.box((x, 0, -21), (0.5, 24.5, 18.5), 'grey')
    for z in (-13, -21, -29):
        k.box((131, 0, z), (54.6, 24.6, 0.4), 'grey')
    for i, x in enumerate((114, 127, 140)):
        zcan(-35.5, -29.5, 4.2, 'steel', x=x, y=-4, seg=28, ch=0.5)
        zcan(-36.2, -35.5, 3.0, 'hotpipe', x=x, y=-4, seg=24)
        k.pipe([(x, -4, -36), (x, 3, -36.6), (x + 5, 3, -36.6)], 0.5, 'hotpipe', seg=8)
    k.box((131, 3, -40.5), (38, 0.3, 14.5), 'hot2')
    for x in np.arange(112, 151, 4.75):
        k.box((x, 3, -40.5), (0.3, 0.6, 15), 'cc')
    k.box((131, 3, -33.0), (39, 0.7, 0.7), 'cc')
    k.box((131, 3, -47.9), (39, 0.5, 0.5), 'cc')
    for x in (116, 131, 146):
        k.box((x, 12.2, -21), (8, 0.3, 10), 'radw')
        k.box((x, -12.2, -18), (6, 0.25, 6), 'dark')
    # --- low-temperature radiators: folding panel wings on booms
    L = D['ltr']
    nx, ny = 6, 8
    for s in (1, -1):
        for i in range(nx):
            xa = 100 + L['len'] * i / nx + 0.18
            xb = 100 + L['len'] * (i + 1) / nx - 0.18
            for j in range(ny):
                ya = 16.6 + L['w'] * j / ny + 0.14
                yb = 16.6 + L['w'] * (j + 1) / ny - 0.14
                k.box(((xa + xb) / 2, s * (ya + yb) / 2, 0), (xb - xa, yb - ya, 0.12), 'radw')
            x = 100 + L['len'] * i / nx
            if i:
                k.tube((x, s * 16.6, 0), (x, s * (16.6 + L['w']), 0), 0.16, 'silver', seg=6)
        for j in range(ny + 1):
            y = s * (16.6 + L['w'] * j / ny)
            k.tube((100, y, 0), (100 + L['len'], y, 0), 0.11, 'grey', seg=5)
        k.tube((99, s * 15.8, 0.4), (167, s * 15.8, 0.4), 0.42, 'silver', seg=10)
        k.tube((99, s * 15.8, -0.4), (167, s * 15.8, -0.4), 0.42, 'white_s', seg=10)
        for x in (104, 133, 162):
            girder((x, s * 10.5, 0), (x, s * 15.4, 0), 1.6, bay=1.6, rc=0.12, rd=0.06)
            with k.xf(orient((x, s * 10.2, 0), (0, s, 0))):
                k.can(0, 1.2, 1.5, 'grey', seg=20)
        for x in (100, 166):
            k.tube((x, s * 16, 0), (x, s * (16.6 + L['w']), 0), 0.2, 'grey', seg=6)
        k.box((133, s * (17.2 + L['w']), 0), (1.2, 0.9, 0.9), 'navR' if s > 0 else 'navG')
        LIGHTS.append(('POINT', (133, s * (18.2 + L['w']), 0), None, 2500, (1, 0.05, 0.03) if s > 0 else (0.05, 1, 0.2), 0))


def build_tug(tz):
    """A 76 m orbital tug in the cradle for an engine overhaul."""
    with k.xf(move(0, 0, tz)):
        # engine bell and thrust structure
        bell = [(8.2, 1.3), (7.0, 2.2), (5.2, 3.8), (3.0, 5.3), (0.0, 6.4)]
        k.lathe(bell, 'bell', seg=56, sharp=70)
        k.lathe([(p[0] + 0.02, p[1] - 0.12) for p in bell], 'black', seg=56, sharp=70)
        for x, r in ((1.2, 6.05), (3.6, 5.05), (5.8, 3.45)):
            k.torus(x, r, 0.14, 'steel', seg=56, tseg=6)
        k.can(8.0, 11.0, 2.4, 'steel', seg=28)
        for i in range(8):
            a = i * TAU / 8
            k.tube(P(8.6, 2.4, a), P(19.5, 5.6, a), 0.16, 'truss', seg=6)
            k.tube(P(8.6, 2.4, a), P(19.5, 5.6, a + TAU / 8), 0.1, 'truss', seg=5)
        k.can(11.0, 15.5, 3.4, 'grey', seg=32)
        for i in range(4):
            a = i * TAU / 4 + 0.4
            with k.xf(on(13.2, a, 3.4)):
                k.sphere((0, 1.2, 0), 1.25, 'gold', seg=16, rings=8)
            with k.xf(on(16.5, a + 0.7, 2.0)):
                k.can(-1.6, 1.6, 0.9, 'silver', seg=14)
        k.can(15.5, 19.5, 2.2, 'dark', seg=24)
        # engine cowl panels: two taken off for the overhaul
        for i in range(8):
            if i in (1, 2):
                continue
            a0 = (i + 0.03) * TAU / 8
            a1 = (i + 0.97) * TAU / 8
            arc_plate(11.2, 19.4, 4.55, 4.7, a0, a1, 'white_s', seg=5)
        # propellant tank section and equipment bay
        k.lathe([(19.5, 0), (19.5, 5.6), (20.0, 6.0), (61.5, 6.0), (62.0, 5.7), (64.0, 4.9), (67.0, 3.6), (70.5, 2.5), (72.0, 2.2), (72.0, 0)], 'white', seg=64, sharp=30)
        for x in (26, 33, 47, 54):
            k.lathe(rrect(x - 0.2, x + 0.2, 5.98, 6.16, 0.04, 0.04, 1), 'grey', seg=64, closed=True)
        k.lathe(rrect(40.0, 43.0, 5.98, 6.22, 0.05, 0.05, 1), 'blue', seg=64, closed=True)
        k.lathe(rrect(20.6, 24.4, 5.98, 6.12, 0.05, 0.05, 1), 'gold', seg=64, closed=True)
        greeble(21, 61, 6.0, 30, hmax=0.5)
        for a in (1.2, 1.9, 4.3):
            hrail(24, 58, 6.0, a)
        for a in (0.4, 2.7, 3.6, 5.6):
            axial_pipe(20.5, 61, 6.0, a, 0.14, R.choice(['silver', 'gold']))
        # radiators
        for s in (1, -1):
            for j in range(3):
                k.box((16, s * (5.4 + 1.6 + j * 3.1), 0), (11.5, 2.9, 0.1), 'radw')
            k.tube((10.5, s * 5.2, 0), (10.5, s * 14.6, 0), 0.12, 'grey', seg=5)
            k.tube((21.5, s * 5.2, 0), (21.5, s * 14.6, 0), 0.12, 'grey', seg=5)
        # crew cab, docking probe, thrusters, antenna
        for i in range(8):
            with k.xf(on(65.2, i * TAU / 8 + 0.2, 4.35) @ rotZ(-0.42)):
                k.box((0, 0.05, 0), (1.5, 0.08, 1.1), 'glass_dark')
                k.box((0, 0.02, 0), (1.8, 0.06, 1.4), 'grey')
        k.can(72.0, 73.2, 1.5, 'grey', seg=20)
        k.can(73.2, 73.5, 1.75, 'dark', seg=20)
        for q in range(4):
            with k.xf(rotX(q * TAU / 4 + 0.78) @ move(58, 6.9, 0)):
                with k.xf(Matrix.Scale(0.6, 4)):
                    rcs_quad()
        with k.xf(orient((30, 0, 6.0), (0.2, 0, 1))):
            k.tube((0, 0, 0), (2.0, 0, 0), 0.1, 'grey', seg=5)
            with k.xf(move(2.2, 0, 0) @ Matrix.Scale(0.42, 4)):
                dish()
    flat_decal('tug', 'UE-T 114', (30.0, -6.04, tz - 0.9), (37.0, -6.04, tz - 0.9), (37.0, -6.04, tz + 0.9), (30.0, -6.04, tz + 0.9))


# =====================================================================================
# forward: hub, crew ring, berths, command section
# =====================================================================================
def build_fwd(landers):
    hub(166, 214, 15, 190, D['ringF'])
    greeble(203, 212.5, 15, 34, hmax=0.6)
    greeble(167, 177, 15, 22)
    for a in (0.9, 2.7, 4.3, 5.8):
        hrail(203, 212, 15, a)
        hrail(167, 177, 15, a + 0.2)
    for a in (1.3, 3.0, 5.0):
        hatch(175.5, a, 15, 0.9)
    # docking node: four radial ports, two with lifeboats
    for q in range(4):
        with k.xf(rotX(q * TAU / 4) @ orient((171, 14.6, 0), (0, 1, 0))):
            dock_port(lifeboat=(q % 2 == 0))
    k.group = 'RingF'
    hab_ring(190, D['ringF'], phase=0.21, nmod=24, name='COCHRANE')
    k.group = 'Hull'
    # drop ship berths: four on the fixed hub ahead of the ring, landers held roof-in
    BX, ROOF, H = 207.4, 24.0, 6.6
    for q in (1, 3, 5, 7):
        a = q * PI / 4
        with k.xf(rotX(a)):
            for dx in (-4.0, 4.0):
                k.tube((BX + dx, 15, 2.2), (BX + dx * 0.8, ROOF - 0.3, 1.2), 0.3, 'grey', seg=8)
                k.tube((BX + dx, 15, -2.2), (BX + dx * 0.8, ROOF - 0.3, -1.2), 0.3, 'grey', seg=8)
                k.box((BX + dx * 0.8, ROOF - 0.15, 0), (1.2, 0.5, 3.4), 'hazard', bevel=0.08)
                k.tube((BX + dx, 15.2, 2.2), (BX - dx, 15.2, -2.2), 0.1, 'truss', seg=5)
            ycan(15, ROOF - 0.6, 1.1, 'white_s', x=BX, seg=20)          # crew tunnel to the roof hatch
            ycan(ROOF - 0.9, ROOF - 0.3, 1.45, 'grey', x=BX, seg=20)
            k.pipe([(BX + 2.6, 15, 0.6), (BX + 2.6, 21, 0.6), (BX + 1.6, ROOF - 0.5, 0.6)], 0.14, 'gold', seg=6)
            k.pipe([(BX - 2.6, 15, -0.6), (BX - 2.6, 21, -0.6), (BX - 1.6, ROOF - 0.5, -0.6)], 0.14, 'silver', seg=6)
            k.box((BX, 15.4, 0), (10, 0.8, 6), 'grey', bevel=0.1)
        landers.append((BX, a, ROOF + H))
    # forward cone and command section
    k.lathe([(214, 15), (215.0, 14.4), (217.2, 10.9), (218, 10.0)], 'white', seg=96, sharp=30)
    k.lathe([(218, 10), (233.6, 10), (234, 9.7), (236, 7.0), (236, 0)], 'white_m', seg=80, sharp=30)
    k.lathe(rrect(220.0, 221.5, 9.98, 10.1, 0.03, 0.03, 1), 'blue', seg=80, closed=True)
    for x in (218.4, 224.6, 229.4, 233.4):
        k.lathe(rrect(x - 0.2, x + 0.2, 9.95, 10.22, 0.05, 0.05, 1), 'grey', seg=80, closed=True)
    nw = 22
    for i in range(nw):          # bridge windows
        a = i * TAU / nw
        with k.xf(on(227, a, 10.0)):
            k.box((0, 0.03, 0), (2.0, 0.1, 2.2), 'grey')
            k.box((0, 0.07, 0), (1.5, 0.08, 1.75), 'glass' if R.random() < 0.55 else 'glass_dark')
    greeble(218.6, 224.2, 10, 16, hmax=0.6)
    greeble(229.6, 233.2, 10, 12, hmax=0.6)
    for a in (0.6, 2.5, 3.7, 5.4):
        hrail(219, 225, 10, a)
    # storm shelter: the thick-walled bay just behind the bridge
    k.lathe(rrect(229.8, 233.2, 9.95, 10.5, 0.15, 0.15, 2), 'white_s', seg=80, closed=True)
    # high-gain antenna on a mast with a two-axis gimbal
    girder((224, 0, 9.8), (224, 0, 23.2), 1.4, bay=1.4, rc=0.1, rd=0.05)
    k.box((224, 0, 23.7), (1.8, 1.8, 1.0), 'grey', bevel=0.1)
    with k.xf(orient((224, 0, 24.6), (1, 0, 0.55))):
        k.can(-0.6, 0.2, 0.9, 'gold', seg=16)
        with k.xf(move(1.0, 0, 0)):
            dish()
    # low-gain antennas, star trackers
    for a in (2.2, 4.1):
        with k.xf(on(222.5, a, 10)):
            k.tube((0, 0, 0), (0, 3.2, 0), 0.07, 'grey', seg=5)
            with k.xf(orient((0, 3.2, 0), (0, 1, 0))):
                k.lathe([(0, 0.05), (0.9, 0.45)], 'white_s', seg=12)
    for a in (0.3, 1.9, 3.5, 5.1):
        with k.xf(on(231.5, a, 10.5) @ orient((0, 0, 0), (0.5, 1, 0))):
            k.can(0, 0.9, 0.28, 'dark', seg=10)
    # forward RCS
    for q in range(4):
        with k.xf(rotX(q * TAU / 4 + TAU / 8)):
            k.tube((230, 9.8, 0), (230, 12.2, 0), 0.3, 'truss', seg=8)
            with k.xf(move(230, 13.5, 0)):
                rcs_quad()
    # Whipple shield: spaced bumper petals on a spider frame
    for i in range(8):
        a = i * TAU / 8 + TAU / 16
        k.tube(P(235.6, 6.4, a), P(238.2, 12.4, a), 0.16, 'truss', seg=6)
        k.tube(P(236, 2.0, a), P(238.2, 6.2, a), 0.12, 'truss', seg=5)
        k.tube(P(238.2, 12.4, a), P(238.2, 12.4, a + TAU / 8), 0.12, 'truss', seg=5)
    rings = [(0.6, 3.6, 8), (3.72, 6.8, 16), (6.92, 10.0, 24), (10.12, 13.2, 32)]
    for (r0, r1, n) in rings:
        for i in range(n):
            a0 = (i + 0.012) * TAU / n
            a1 = (i + 0.988) * TAU / n
            arc_plate(238.4, 238.55, r0, r1, a0, a1, 'bumper', seg=3)
            arc_plate(239.3, 239.42, r0 + 0.04, r1 - 0.04, a0 + 0.004, a1 - 0.004, 'bumper', seg=3)
    for i in range(24):
        a = i * TAU / 24
        for rr in (3.0, 7.0, 11.4):
            k.tube(P(238.5, rr, a), P(239.3, rr, a), 0.05, 'grey', seg=4)
    k.can(238.0, 240.0, 0.5, 'grey', seg=12)
    k.tube((240, 0, 0), (242.4, 0, 0), 0.08, 'grey', seg=5)
    k.box((242.4, 0, 0), (0.4, 0.4, 0.4), 'navW')
    LIGHTS.append(('POINT', (243.2, 0, 0), None, 1500, (1, 1, 1), 0))


# =====================================================================================
# materials, decals, scene
# =====================================================================================
def make_materials(drive):
    M = {}
    M['white'] = K.panel('white', (0.80, 0.80, 0.78), (0.72, 0.72, 0.71), size=(2.6, 1.7), wob=0.02, seam=0.022, seam_mul=0.55, age=0.3, wrinkle=0.004)
    M['white_m'] = K.panel('white_m', (0.83, 0.83, 0.81), (0.74, 0.74, 0.73), size=(3.4, 2.1), wob=0.05, rough=0.7, seam=0.03, seam_mul=0.62, age=0.35, wrinkle=0.012)
    M['white_s'] = K.panel('white_s', (0.78, 0.78, 0.77), (0.71, 0.71, 0.7), size=(1.3, 0.9), wob=0.008, seam=0.015, seam_mul=0.6, rough=0.5)
    M['tank'] = K.panel('tank', (0.84, 0.84, 0.83), (0.8, 0.8, 0.79), size=(5.0, 2.6), wob=0.02, seam=0.03, rough=0.42, streak=0.25)
    M['grey'] = K.panel('grey', (0.36, 0.37, 0.39), (0.29, 0.3, 0.32), size=(1.6, 1.0), metal=0.5, rough=0.45, wob=0.004, seam=0.02, grime=0.3)
    M['grey_p'] = K.panel('grey_p', (0.36, 0.37, 0.38), (0.3, 0.31, 0.33), size=(3.2, 3.0), metal=0.6, rough=0.5, wob=0.02, seam=0.05, grime=0.4)
    M['vessel'] = K.panel('vessel', (0.5, 0.5, 0.52), (0.42, 0.42, 0.45), size=(3.0, 2.0), metal=1.0, rough=0.3, wob=0.004, grime=0.3)
    M['blue'] = K.panel('blue', (0.025, 0.07, 0.27), (0.02, 0.055, 0.22), size=(2.4, 1.6), rough=0.45, wob=0.008)
    M['tile'] = K.panel('tile', (0.74, 0.74, 0.73), (0.66, 0.66, 0.66), size=(1.25, 1.25), metal=0.0, rough=0.5, wob=0.014, seam=0.015, seam_mul=0.6, grime=0.35, age=0.3)
    M['bumper'] = K.panel('bumper', (0.5, 0.51, 0.52), (0.42, 0.43, 0.45), size=(1.1, 1.1), metal=0.45, rough=0.42, wob=0.02, seam=0.015, seam_mul=0.6, grime=0.5)
    M['tile2'] = K.panel('tile2', (0.6, 0.6, 0.6), (0.52, 0.52, 0.53), size=(1.25, 1.25), metal=0.2, rough=0.5, wob=0.014, seam=0.015, seam_mul=0.6, grime=0.4)
    M['radw'] = K.panel('radw', (0.86, 0.87, 0.88), (0.83, 0.84, 0.86), size=(3.6, 0.36), seam=0.03, seam_mul=0.55, rough=0.5, wob=0.002, grime=0.12)
    M['hazard'] = K.plain('hazard', (0.85, 0.5, 0.02), rough=0.5, grime=0.3)
    M['orange'] = K.plain('orange', (0.78, 0.16, 0.02), rough=0.5, grime=0.3)
    M['yellow'] = K.plain('yellow', (0.8, 0.48, 0.03), rough=0.38, metal=0.5)
    M['dark'] = K.plain('dark', (0.045, 0.045, 0.05), rough=0.5, metal=0.4)
    M['black'] = K.plain('black', (0.008, 0.008, 0.009), rough=0.8)
    M['cc'] = K.plain('cc', (0.018, 0.018, 0.02), rough=0.7)
    M['truss'] = K.plain('truss', (0.6, 0.61, 0.62), rough=0.48, metal=0.35, rvar=0.2, grime=0.25)
    M['steel'] = K.plain('steel', (0.72, 0.72, 0.74), rough=0.22, metal=1.0, rvar=0.15, grime=0.2)
    M['tungsten'] = K.plain('tungsten', (0.07, 0.07, 0.08), rough=0.55, metal=0.9, bump=0.01, grime=0.4)
    M['bell'] = K.plain('bell', (0.16, 0.12, 0.1), rough=0.36, metal=1.0, rvar=0.2, grime=0.5)
    M['gold'] = K.foil('gold', (1.0, 0.56, 0.16))
    M['silver'] = K.foil('silver', (0.9, 0.9, 0.92), rough=0.16)
    M['glass'] = K.windows('glass', strength=0.32)
    M['glass_dark'] = K.plain('glass_dark', (0.008, 0.01, 0.016), rough=0.05, grime=0.0, coat=1.0)
    M['lamp'] = K.emit('lamp', (1.0, 0.95, 0.88), 60.0)
    M['navR'] = K.emit('navR', (1.0, 0.04, 0.02), 40.0)
    M['navG'] = K.emit('navG', (0.03, 1.0, 0.15), 40.0)
    M['navW'] = K.emit('navW', (1.0, 1.0, 1.0), 40.0)
    g = 1.0 if drive else 0.12
    M['hot'] = K.hot_radiator('hot', 1260.0 if drive else 900.0, 1040.0 if drive else 760.0, gain=0.95 if drive else 0.05)
    M['hot2'] = K.hot_radiator('hot2', 900.0, 780.0, gain=0.08)
    M['hotpipe'] = K.emit('hotpipe', (1.0, 0.28, 0.05), 2.4 * g + 0.15, base=(0.02, 0.018, 0.016))
    M['plasma'] = K.emit('plasma', (0.55, 0.6, 1.0), 30.0)
    for i, c in enumerate([(0.62, 0.62, 0.6), (0.5, 0.42, 0.3), (0.5, 0.2, 0.08), (0.12, 0.2, 0.33), (0.3, 0.33, 0.24)]):
        M['ctr%d' % i] = K.corrugated('ctr%d' % i, c)
    for name, (factory, _) in EXTRA.items():
        M[name] = factory()
    return M


def make_decals(M, tmp):
    from PIL import Image, ImageDraw, ImageFont
    font = None
    for p in ('/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',
              '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf'):
        if os.path.exists(p):
            font = p
            break
    done = set()
    for name, text in DECALS:
        if name in done:
            continue
        done.add(name)
        f = ImageFont.truetype(font, 200) if font else ImageFont.load_default()
        bb = f.getbbox(text)
        tw = int(f.getlength(text))
        W, Hh = tw + 60, 280
        im = Image.new('RGBA', (W, Hh), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        d.text((30, (Hh - (bb[3] + bb[1])) // 2), text, font=f, fill=(255, 255, 255, 255))
        path = os.path.join(tmp, 'decal_%s.png' % name)
        im.save(path)
        img = bpy.data.images.load(path)
        M['decal_' + name] = K.decal('decal_' + name, img, col=(0.02, 0.02, 0.025) if name != 'bay' else (0.6, 0.1, 0.02))


def import_landers(landers, coll):
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'game', 'models', 'vehicles', 'dropship_prewarp_a.glb')
    if not os.path.exists(path):
        return
    for (bx, a, rb) in landers:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        new = [o for o in bpy.data.objects if o not in before]
        root = bpy.data.objects.new('Lander', None)
        coll.objects.link(root)
        for o in new:
            for c in list(o.users_collection):
                c.objects.unlink(o)
            coll.objects.link(o)
            if o.parent is None:
                o.parent = root
        rad = Vector((0, math.cos(a), math.sin(a)))
        tan = Vector((0, -math.sin(a), math.cos(a)))
        X, Y, Z = -tan, Vector((1, 0, 0)), -rad
        o = rad * rb + Vector((bx, 0, 0))
        root.matrix_world = Matrix(((X.x, Y.x, Z.x, o.x), (X.y, Y.y, Z.y, o.y), (X.z, Y.z, Z.z, o.z), (0, 0, 0, 1)))


def build_plume(coll, x=-221.0, length=260.0, r0=6.0, r1=40.0):
    """Fusion exhaust: thin and faint, a cool violet glow that fades fast."""
    me = bpy.data.meshes.new('Plume')
    ob = bpy.data.objects.new('Plume', me)
    coll.objects.link(ob)
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=48, radius1=r1, radius2=r0, depth=length,
                          matrix=Matrix.Translation((x - length / 2, 0, 0)) @ Matrix.Rotation(PI / 2, 4, 'Y'))
    bm.to_mesh(me)
    bm.free()
    m = bpy.data.materials.new('plume')
    m.use_nodes = True
    t = m.node_tree
    for n in list(t.nodes):
        t.nodes.remove(n)
    out = t.nodes.new('ShaderNodeOutputMaterial')
    tc = t.nodes.new('ShaderNodeTexCoord')
    sep = t.nodes.new('ShaderNodeSeparateXYZ')
    t.links.new(tc.outputs['Object'], sep.inputs[0])
    # distance behind the throat (0 at x=-221), radial distance from the axis
    dist = K._math(t, 'MULTIPLY', K._math(t, 'ADD', sep.outputs[0], -x), -1.0)
    rad = K._math(t, 'SQRT', K._math(t, 'ADD', K._math(t, 'POWER', sep.outputs[1], 2.0), K._math(t, 'POWER', sep.outputs[2], 2.0)))
    width = K._math(t, 'ADD', r0 * 0.85, K._math(t, 'MULTIPLY', dist, (r1 - r0) / length * 0.92))
    core = K._math(t, 'POWER', K._math(t, 'SUBTRACT', 1.0, K._math(t, 'DIVIDE', rad, width), clamp=True), 2.0)
    fall = K._math(t, 'POWER', 2.718, K._math(t, 'MULTIPLY', dist, -0.016))
    dens = K._math(t, 'MULTIPLY', K._math(t, 'MULTIPLY', core, fall), 0.016)
    vol = t.nodes.new('ShaderNodeEmission')
    vol.inputs[0].default_value = (0.42, 0.48, 1.0, 1)
    t.links.new(dens, vol.inputs[1])
    t.links.new(vol.outputs[0], out.inputs['Volume'])
    me.materials.append(m)
    return ob


# ---- Earth backdrop (its own render pass, composited behind the ship)
def build_earth(tex):
    coll = bpy.data.collections.new('Earth')
    bpy.context.scene.collection.children.link(coll)

    def ball(name, r):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=256, ring_count=128, radius=1.0)
        ob = bpy.context.active_object
        ob.name = name
        for c in list(ob.users_collection):
            c.objects.unlink(ob)
        coll.objects.link(ob)
        bpy.ops.object.shade_smooth()
        ob.scale = (r, r, r)
        return ob
    e = ball('Earth', 6371.0)
    m, t, b = K._new('earth')
    img = t.nodes.new('ShaderNodeTexImage')
    img.image = bpy.data.images.load(tex)
    uv, obc = K._coords(t)
    t.links.new(uv, img.inputs['Vector'])
    sep = t.nodes.new('ShaderNodeSeparateColor')
    t.links.new(img.outputs[0], sep.inputs[0])
    mx = K._math(t, 'MAXIMUM', sep.outputs[0], sep.outputs[1])
    ocean = K._math(t, 'MULTIPLY', K._math(t, 'SUBTRACT', sep.outputs[2], mx, clamp=True), 14.0, clamp=True)
    col = K._mix(t, 'MIX', ocean, img.outputs[0], (0.012, 0.035, 0.085))
    t.links.new(col, b.inputs['Base Color'])
    t.links.new(K._math(t, 'SUBTRACT', 0.85, K._math(t, 'MULTIPLY', ocean, 0.6)), b.inputs['Roughness'])
    e.data.materials.append(m)
    # clouds: big weather systems broken into cells and streets
    c = ball('Clouds', 6371.0 * 1.0016)
    m, t, b = K._new('clouds')
    uv, obc = K._coords(t)
    big = K._noise(t, obc, 2.6, 12.0, 0.62, 1.6)
    mid = K._noise(t, obc, 11.0, 10.0, 0.7, 0.8)
    cov = K._smooth(t, K._math(t, 'ADD', K._math(t, 'MULTIPLY', big, 0.75), K._math(t, 'MULTIPLY', mid, 0.25)), 0.45, 0.62)
    wv = t.nodes.new('ShaderNodeVectorMath'); wv.operation = 'ADD'
    wn = t.nodes.new('ShaderNodeTexNoise'); wn.inputs['Scale'].default_value = 40.0; wn.inputs['Detail'].default_value = 3.0
    t.links.new(obc, wn.inputs['Vector'])
    ws = t.nodes.new('ShaderNodeVectorMath'); ws.operation = 'SCALE'; ws.inputs[3].default_value = 0.012
    t.links.new(wn.outputs[1], ws.inputs[0]); t.links.new(obc, wv.inputs[0]); t.links.new(ws.outputs[0], wv.inputs[1])
    cells = K._voro(t, wv.outputs[0], 380.0, 'DISTANCE_TO_EDGE')
    c2 = K._smooth(t, cells.outputs[0], 0.0, 0.5)
    fine = K._noise(t, obc, 90.0, 12.0, 0.78, 0.6)
    c3 = K._smooth(t, fine, 0.38, 0.68)
    brk = K._math(t, 'MULTIPLY', K._math(t, 'ADD', 0.72, K._math(t, 'MULTIPLY', c2, 0.28)), K._math(t, 'ADD', 0.15, K._math(t, 'MULTIPLY', c3, 1.0)), clamp=True)
    dens = K._math(t, 'MULTIPLY', cov, brk, clamp=True)
    thin = K._math(t, 'MULTIPLY', K._math(t, 'SUBTRACT', mid, 0.5, clamp=True), 0.5)
    alpha = K._math(t, 'ADD', dens, thin, clamp=True)
    b.inputs['Base Color'].default_value = (0.9, 0.9, 0.9, 1)
    b.inputs['Roughness'].default_value = 1.0
    t.links.new(alpha, b.inputs['Alpha'])
    K._bump(t, K._math(t, 'MULTIPLY', dens, 6.0), b)
    c.data.materials.append(m)
    # air: a shell that is nearly clear face-on and blue at the limb, lit by the sun
    a = ball('Air', 6371.0 * 1.012)
    m = bpy.data.materials.new('air')
    m.use_nodes = True
    t = m.node_tree
    for nn in list(t.nodes):
        t.nodes.remove(nn)
    out = t.nodes.new('ShaderNodeOutputMaterial')
    lw = t.nodes.new('ShaderNodeLayerWeight'); lw.inputs[0].default_value = 0.5
    f = K._math(t, 'ADD', 0.06, K._math(t, 'MULTIPLY', K._math(t, 'POWER', lw.outputs[1], 3.0), 0.9), clamp=True)
    df = t.nodes.new('ShaderNodeBsdfDiffuse'); df.inputs[0].default_value = (0.2, 0.42, 1.0, 1)
    tr = t.nodes.new('ShaderNodeBsdfTransparent')
    mix = t.nodes.new('ShaderNodeMixShader')
    t.links.new(f, mix.inputs[0]); t.links.new(tr.outputs[0], mix.inputs[1]); t.links.new(df.outputs[0], mix.inputs[2])
    t.links.new(mix.outputs[0], out.inputs[0])
    a.data.materials.append(m)
    return coll, (e, c, a)



# =====================================================================================
# game export: a lighter mesh with small tiling textures, rings as their own nodes
# =====================================================================================
def _srgb(a):
    a = np.clip(a, 0, 1)
    return np.where(a < 0.0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - 0.055)


def _tile_noise(n, cells, rng):
    from scipy.ndimage import zoom
    g = rng.random((cells, cells)).astype(np.float32)
    return zoom(g, n / cells, order=3, mode='grid-wrap')[:n, :n]


def _normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nz = np.ones_like(h)
    l = np.sqrt(dx * dx + dy * dy + 1)
    return np.stack([-dx / l * 0.5 + 0.5, -dy / l * 0.5 + 0.5, nz / l * 0.5 + 0.5], 2)


def _save(arr, path):
    from PIL import Image
    Image.fromarray((np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8)).save(path, quality=88)
    return bpy.data.images.load(path)


def panel_textures(name, c1, c2, seam_mul, tmp, n=512, nx=4, ny=4, seam_px=2.2, wob=1.0):
    """Albedo and normal for nx by ny skin panels, half-offset rows, tiling."""
    rng = np.random.default_rng(abs(hash(name)) % (2 ** 31))
    v, u = np.mgrid[0:n, 0:n].astype(np.float32) / n
    row = np.floor(v * ny).astype(int)
    uu = (u * nx + 0.5 * (row % 2)) % nx
    col = np.floor(uu).astype(int)
    fu, fv = uu - col, v * ny - row
    tint = rng.random((ny, nx)).astype(np.float32)[row, col]
    du = np.minimum(fu, 1 - fu) * n / nx
    dv = np.minimum(fv, 1 - fv) * n / ny
    seam = np.clip(1 - np.minimum(du, dv) / seam_px, 0, 1)
    c1, c2 = np.array(c1, np.float32), np.array(c2, np.float32)
    alb = c1 + (c2 - c1) * tint[..., None]
    alb *= (1 - seam * (1 - seam_mul))[..., None]
    alb *= (0.94 + 0.12 * _tile_noise(n, 8, rng))[..., None]
    pillow = np.sin(PI * fu) * np.sin(PI * fv) * (0.4 + 0.6 * tint)
    h = pillow * 1.2 * wob + _tile_noise(n, 6, rng) * 2.0 * wob - seam * 1.5
    return _save(_srgb(alb), os.path.join(tmp, 'g_%s_a.jpg' % name)), _save(_normal_from_height(h, 1.6), os.path.join(tmp, 'g_%s_n.jpg' % name))


def foil_normal(name, tmp, n=512):
    rng = np.random.default_rng(abs(hash(name)) % (2 ** 31))
    pts = rng.random((90, 2))
    v, u = np.mgrid[0:n, 0:n].astype(np.float32) / n
    d = np.full((n, n), 9.0, np.float32)
    for (px, py) in pts:
        ddx = np.abs(u - px); ddx = np.minimum(ddx, 1 - ddx)
        ddy = np.abs(v - py); ddy = np.minimum(ddy, 1 - ddy)
        d = np.minimum(d, np.sqrt(ddx * ddx + ddy * ddy))
    h = d * 60 + _tile_noise(n, 5, rng) * 9 + _tile_noise(n, 24, rng) * 2.5
    return _save(_normal_from_height(h, 1.3), os.path.join(tmp, 'g_%s_n.jpg' % name))


def game_material(name, col, metal=0.0, rough=0.5, emis=None, estr=0.0, alb=None, nrm=None, alpha=None):
    m, t, b = K._new(name)
    b.inputs['Base Color'].default_value = K._c(col)
    b.inputs['Metallic'].default_value = metal
    b.inputs['Roughness'].default_value = rough
    if emis:
        b.inputs['Emission Color'].default_value = K._c(emis)
        b.inputs['Emission Strength'].default_value = estr
    if alb:
        n = t.nodes.new('ShaderNodeTexImage'); n.image = alb
        t.links.new(n.outputs[0], b.inputs['Base Color'])
    if nrm:
        n = t.nodes.new('ShaderNodeTexImage'); n.image = nrm
        n.image.colorspace_settings.name = 'Non-Color'
        nm = t.nodes.new('ShaderNodeNormalMap')
        t.links.new(n.outputs[0], nm.inputs['Color']); t.links.new(nm.outputs[0], b.inputs['Normal'])
    if alpha:
        n = t.nodes.new('ShaderNodeTexImage'); n.image = alpha
        t.links.new(n.outputs['Alpha'], b.inputs['Alpha'])
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            m.blend_method = 'BLEND'
    return m


def default_build(parts, drive, landers):
    if 'aft' in parts:
        build_aft(drive)
        if drive:
            PLUMES.append(dict(x=-221.0, length=260.0, r0=6.0, r1=40.0))
    if 'mid' in parts: build_mid()
    if 'fwd' in parts: build_fwd(landers)


def export_game(path, tmp, build=None):
    """Write the game model (see docs/ship-model-contract.md)."""
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    coll = bpy.data.collections.new('Ship')
    scene.collection.children.link(coll)
    k.lod, k.min_r, k.min_box, k.one_key = 0.3, 0.065, 0.5, True
    k.M = rotZ(PI / 2)                  # bow to Blender +Y, which the exporter turns into Godot -Z
    landers = []
    (build or default_build)(['aft', 'mid', 'fwd'], False, landers)
    # panel materials: (c1, c2, seam darkening, panel size in metres, metal, rough)
    PAN = {'white': ((0.80, 0.80, 0.78), (0.72, 0.72, 0.71), 0.55, (2.6, 1.7), 0, 0.55),
           'white_m': ((0.83, 0.83, 0.81), (0.74, 0.74, 0.73), 0.62, (3.4, 2.1), 0, 0.7),
           'white_s': ((0.78, 0.78, 0.77), (0.71, 0.71, 0.70), 0.6, (1.3, 0.9), 0, 0.5),
           'tank': ((0.84, 0.84, 0.83), (0.80, 0.80, 0.79), 0.6, (5.0, 2.6), 0, 0.42),
           'grey': ((0.36, 0.37, 0.39), (0.29, 0.30, 0.32), 0.4, (1.6, 1.0), 0.5, 0.45),
           'grey_p': ((0.36, 0.37, 0.38), (0.30, 0.31, 0.33), 0.4, (3.2, 3.0), 0.6, 0.5),
           'vessel': ((0.50, 0.50, 0.52), (0.42, 0.42, 0.45), 0.4, (3.0, 2.0), 1.0, 0.3),
           'blue': ((0.025, 0.07, 0.27), (0.02, 0.055, 0.22), 0.5, (2.4, 1.6), 0, 0.45),
           'tile': ((0.74, 0.74, 0.73), (0.66, 0.66, 0.66), 0.6, (1.25, 1.25), 0, 0.5),
           'tile2': ((0.60, 0.60, 0.60), (0.52, 0.52, 0.53), 0.6, (1.25, 1.25), 0.2, 0.5),
           'bumper': ((0.50, 0.51, 0.52), (0.42, 0.43, 0.45), 0.6, (1.1, 1.1), 0.45, 0.42),
           'radw': ((0.86, 0.87, 0.88), (0.83, 0.84, 0.86), 0.55, (3.6, 0.36), 0, 0.5)}
    M, uvs = {}, {}
    for name, (c1, c2, sm, size, metal, rough) in PAN.items():
        a_, n_ = panel_textures(name, c1, c2, sm, tmp)
        M[name] = game_material(name, (1, 1, 1), metal, rough, alb=a_, nrm=n_)
        uvs[name] = (1 / (4 * size[0]), 1 / (4 * size[1]))
    fn = foil_normal('foil', tmp)
    M['gold'] = game_material('gold', (1.0, 0.56, 0.16), 1.0, 0.28, nrm=fn)
    M['silver'] = game_material('silver', (0.9, 0.9, 0.92), 1.0, 0.24, nrm=fn)
    uvs['gold'] = uvs['silver'] = (1 / 3.0, 1 / 3.0)
    SIM = {'hazard': ((0.85, 0.5, 0.02), 0, 0.5), 'orange': ((0.78, 0.16, 0.02), 0, 0.5), 'yellow': ((0.8, 0.48, 0.03), 0.5, 0.38),
           'dark': ((0.045, 0.045, 0.05), 0.4, 0.5), 'black': ((0.008, 0.008, 0.009), 0, 0.8), 'cc': ((0.018, 0.018, 0.02), 0, 0.7),
           'truss': ((0.6, 0.61, 0.62), 0.35, 0.48), 'steel': ((0.72, 0.72, 0.74), 1.0, 0.25), 'tungsten': ((0.07, 0.07, 0.08), 0.9, 0.55),
           'bell': ((0.16, 0.12, 0.1), 1.0, 0.36), 'glass_dark': ((0.008, 0.01, 0.016), 0, 0.06), 'plasma': ((0.008, 0.008, 0.009), 0, 0.8)}
    for name, (c, metal, rough) in SIM.items():
        M[name] = game_material(name, c, metal, rough)
    for i, c in enumerate([(0.62, 0.62, 0.6), (0.5, 0.42, 0.3), (0.5, 0.2, 0.08), (0.12, 0.2, 0.33), (0.3, 0.33, 0.24)]):
        M['ctr%d' % i] = game_material('ctr%d' % i, c, 0, 0.55)
    M['glass'] = game_material('glass', (0.02, 0.02, 0.03), 0, 0.08, emis=(1.0, 0.82, 0.55), estr=1.0)
    M['lamp'] = game_material('lamp', (0.02, 0.02, 0.02), 0, 0.3, emis=(1.0, 0.95, 0.88), estr=1.0)
    M['navR'] = game_material('navR', (0.02, 0.02, 0.02), 0, 0.3, emis=(1.0, 0.04, 0.02), estr=1.0)
    M['navG'] = game_material('navG', (0.02, 0.02, 0.02), 0, 0.3, emis=(0.03, 1.0, 0.15), estr=1.0)
    M['navW'] = game_material('navW', (0.02, 0.02, 0.02), 0, 0.3, emis=(1.0, 1.0, 1.0), estr=1.0)
    M['hot'] = game_material('hot', (0.012, 0.012, 0.013), 0, 0.7, emis=(1.0, 0.16, 0.03), estr=0.12)
    M['hot2'] = game_material('hot2', (0.012, 0.012, 0.013), 0, 0.7, emis=(1.0, 0.16, 0.03), estr=0.12)
    M['hotpipe'] = game_material('hotpipe', (0.02, 0.018, 0.016), 0, 0.5, emis=(1.0, 0.28, 0.05), estr=0.3)
    # a ship's own materials: game spec ('pan', c1, c2, seam, (w, h), metal, rough) | ('sim', col, metal, rough) | ('emit', col, strength)
    for name, (_, spec) in EXTRA.items():
        if spec[0] == 'pan':
            a_, n_ = panel_textures(name, spec[1], spec[2], spec[3], tmp)
            M[name] = game_material(name, (1, 1, 1), spec[5], spec[6], alb=a_, nrm=n_)
            uvs[name] = (1 / (4 * spec[4][0]), 1 / (4 * spec[4][1]))
        elif spec[0] == 'sim':
            M[name] = game_material(name, spec[1], spec[2], spec[3])
        else:
            M[name] = game_material(name, (0.02, 0.02, 0.02), 0, 0.3, emis=spec[1], estr=spec[2])
    # decals
    make_decals({}, tmp)
    for name, text in DECALS:
        img = bpy.data.images.get('decal_%s.png' % name) or bpy.data.images.load(os.path.join(tmp, 'decal_%s.png' % name))
        M['decal_' + name] = game_material('decal_' + name, (0.02, 0.02, 0.025) if name != 'bay' else (0.6, 0.1, 0.02), 0, 0.6, alpha=img)
    root = bpy.data.objects.new('Ship', None); coll.objects.link(root)
    R90 = rotZ(PI / 2)
    piv = {'Hull': (0, 0, 0)}
    for g, c in PIVOTS.items():
        piv[g] = tuple(R90 @ Vector(c))
    par = {}
    for g, pv in piv.items():
        e = bpy.data.objects.new(g, None); coll.objects.link(e); e.parent = root; e.location = pv
        if g in SPIN:
            e['spin'] = SPIN[g]
        par[g] = e
    obs = k.build(M, coll, bevels=False, uvscale=uvs, pivots=piv, parents=par)
    for i, (bx, a, rb) in enumerate(landers):
        rad = Vector((0, math.cos(a), math.sin(a))); tan = Vector((0, -math.sin(a), math.cos(a)))
        X, Y, Z = -tan, Vector((1, 0, 0)), -rad
        o = rad * (rb - 6.6) + Vector((bx, 0, 0))          # the roof plane: landers are listed by their pad radius
        e = bpy.data.objects.new('Berth%d' % i, None); coll.objects.link(e); e.parent = root
        e.matrix_world = R90 @ Matrix(((X.x, Y.x, Z.x, o.x), (X.y, Y.y, Z.y, o.y), (X.z, Y.z, Z.z, o.z), (0, 0, 0, 1)))
    bpy.context.view_layer.update()
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in obs)
    for o in sorted(obs, key=lambda o: -len(o.data.polygons))[:14]:
        print('  %-28s %7d' % (o.name, sum(len(p.vertices) - 2 for p in o.data.polygons)))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_yup=True, export_apply=True, export_image_format='AUTO',
                              export_jpeg_quality=85, export_cameras=False, export_lights=False, export_animations=False, export_extras=True)
    print('game model: %d tris, %d meshes, %.1f MB, %.0fs' % (tris, len(obs), os.path.getsize(path) / 1e6, time.time() - t0))
    print('spin (Godot RotateZ, rad/s):', {g: round(v, 4) for g, v in SPIN.items()})


SHOTS = {
    # tgt: look-at point; cam: direction from it to the camera; sun: direction to the sun in camera space (x right, y up, z toward the camera)
    # eoff: where the Earth's centre sits, degrees right and up of the view axis; alt: camera height above the surface, km
    'hero':  dict(tgt=(40, 0, 0), cam=(0.62, -0.72, 0.30), dist=860, lens=70, roll=-7, sun=(0.62, 0.55, 0.56), eoff=(-9, -20), alt=9500),
    'ring':  dict(tgt=(196, 0, 6), cam=(0.72, -0.60, 0.34), dist=330, lens=50, roll=5, sun=(0.55, 0.60, 0.58), eoff=(-12, -27), alt=9500, elon=120),
    'stern': dict(tgt=(-150, 0, 0), cam=(-0.80, -0.52, 0.30), dist=560, lens=60, roll=4, sun=(-0.70, 0.45, 0.55), eoff=(10, -25), alt=9500),
    'yard':  dict(tgt=(46, 0, 30), cam=(0.25, -0.86, 0.45), dist=300, lens=45, roll=-3, sun=(0.50, 0.65, 0.57), eoff=(8, -25), alt=9500, elon=165),
    'side':  dict(tgt=(2, 0, 4), cam=(0.0, -1.0, 0.06), dist=1440, lens=100, roll=0, sun=(0.50, 0.62, 0.60), eoff=None, alt=0),
    'bow':   dict(tgt=(150, 0, -4), cam=(0.93, -0.30, 0.20), dist=540, lens=60, roll=8, sun=(-0.55, 0.60, 0.58), eoff=(-14, -22), alt=9500, elon=185),
}


def setup_camera(sh):
    cam = bpy.data.cameras.new('Cam')
    ob = bpy.data.objects.new('Cam', cam)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene.camera = ob
    cam.lens = sh['lens']
    cam.sensor_width = 36
    cam.clip_start = 5
    cam.clip_end = 1e6
    d = Vector(sh['cam']).normalized()
    ob.location = Vector(sh['tgt']) + d * sh['dist']
    q = (-d).to_track_quat('-Z', 'Y')
    ob.rotation_euler = (q.to_matrix().to_4x4() @ Matrix.Rotation(math.radians(sh['roll']), 4, 'Z')).to_euler()
    return ob


def sun_light(name, d, energy, col, angle, coll):
    L = bpy.data.lights.new(name, 'SUN')
    L.energy = energy
    L.color = col
    L.angle = angle
    ob = bpy.data.objects.new(name, L)
    coll.objects.link(ob)
    ob.rotation_euler = Vector(d).normalized().to_track_quat('Z', 'Y').to_euler()
    return ob


def load_exr(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(h, w, 4)


def finish(ship, earth, out, scene, grain=0.012):
    """Composite ship over Earth, bloom, view transform, grain and vignette."""
    from scipy.ndimage import gaussian_filter
    from PIL import Image
    rgb = ship[..., :3].copy()
    if earth is not None:
        rgb += earth[..., :3] * (1 - ship[..., 3:4])
    h, w = rgb.shape[:2]
    lum = rgb.max(axis=2)
    hi = np.clip(lum - 1.6, 0, None)[..., None] * (rgb / np.maximum(lum, 1e-4)[..., None])
    s = w / 1920.0
    glow = sum(gaussian_filter(hi, sigma=(sg * s, sg * s, 0)) * wt for sg, wt in ((3, 0.10), (12, 0.06), (45, 0.035)))
    veil = gaussian_filter(rgb, sigma=(90 * s, 90 * s, 0)) * 0.012          # lens veiling glare
    rgb = rgb + glow + veil
    img = bpy.data.images.new('final', w, h, alpha=False, float_buffer=True)
    px = np.concatenate([rgb, np.ones((h, w, 1), np.float32)], axis=2).astype(np.float32)
    img.pixels.foreach_set(px.ravel())
    tmp = out + '.tmp.png'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '16'
    img.save_render(tmp, scene=scene)
    bpy.data.images.remove(img)
    im = np.asarray(Image.open(tmp).convert('RGB'), dtype=np.float32) / 255.0
    os.remove(tmp)
    yy, xx = np.mgrid[0:h, 0:w]
    r2 = ((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (w / 2)) ** 2
    im *= (1 - 0.16 * r2 ** 1.2)[..., None]
    rng = np.random.default_rng(7)
    g = rng.normal(0, grain, (h, w, 1)).astype(np.float32) + rng.normal(0, grain * 0.5, (h, w, 3)).astype(np.float32)
    im = np.clip(im + g * (0.35 + 0.65 * (1 - im)), 0, 1)
    Image.fromarray((im * 255 + 0.5).astype(np.uint8)).save(out, quality=93, subsampling=0)


def main(build=None, shots=None, ship_id='cochrane'):
    """build(parts, drive, landers) adds the ship to k; other ships' scripts pass their own (see blender/prewarp_ships.md)."""
    ap = argparse.ArgumentParser()
    ap.add_argument('--shot', default='hero')
    ap.add_argument('--res', default='1920x1080')
    ap.add_argument('--spp', type=int, default=128)
    ap.add_argument('--out', default='blender/previews/cochrane')
    ap.add_argument('--tmp', default='/tmp/cochrane')
    ap.add_argument('--only', default='aft,mid,fwd')
    ap.add_argument('--drive', action='store_true')
    ap.add_argument('--no-earth', action='store_true')
    ap.add_argument('--earth-tex', default='')
    ap.add_argument('--earth-only', action='store_true')
    ap.add_argument('--name', default='')
    ap.add_argument('--elon', type=float, default=None)
    ap.add_argument('--elat', type=float, default=None)
    ap.add_argument('--exposure', type=float, default=0.0)
    ap.add_argument('--save-blend', default='')
    ap.add_argument('--game', default='', help='write the game glb to this path and stop')
    a = ap.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:])
    os.makedirs(a.tmp, exist_ok=True)
    if a.game:
        export_game(a.game, a.tmp, build)
        return
    os.makedirs(a.out, exist_ok=True)
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    sh = (shots or SHOTS)[a.shot]
    W, Hh = [int(v) for v in a.res.split('x')]
    # ---- ship
    ship = bpy.data.collections.new('Ship')
    scene.collection.children.link(ship)
    parts = a.only.split(',')
    landers = []
    if not a.earth_only:
        (build or default_build)(parts, a.drive, landers)
    M = make_materials(a.drive)
    make_decals(M, a.tmp)
    k.build(M, ship)
    import_landers(landers, ship)
    for pl in PLUMES:
        build_plume(ship, **pl)
    print('geometry: %d faces, %.1fs' % (k.stats(), time.time() - t0)); sys.stdout.flush()
    # ---- lights
    cam = setup_camera(sh)
    bpy.context.view_layer.update()
    CM = cam.matrix_world.to_3x3()
    sun_d = (CM @ Vector(sh['sun'])).normalized()
    sun_light('Sun', sun_d, 5.2, (1.0, 0.97, 0.93), math.radians(0.53), ship)
    for kind, loc, d, e, col, spot in LIGHTS:
        if kind == 'SPOT':
            L = bpy.data.lights.new('Flood', 'SPOT'); L.energy = e; L.color = col; L.spot_size = spot; L.spot_blend = 0.5; L.shadow_soft_size = 0.3
            ob = bpy.data.objects.new('Flood', L); ship.objects.link(ob)
            ob.location = loc
            ob.rotation_euler = (-Vector(d)).to_track_quat('Z', 'Y').to_euler()
        elif kind == 'POINT':
            L = bpy.data.lights.new('Nav', 'POINT'); L.energy = e; L.color = col; L.shadow_soft_size = 0.4
            ob = bpy.data.objects.new('Nav', L); ship.objects.link(ob); ob.location = loc
    earth_coll = None
    if sh['eoff'] and not a.no_earth and a.earth_tex:
        ax, ay = math.radians(sh['eoff'][0]), math.radians(sh['eoff'][1])
        ed = (CM @ Vector((math.sin(ax) * math.cos(ay), math.sin(ay), -math.cos(ax) * math.cos(ay)))).normalized()
        earth_coll, (e_, c_, a_) = build_earth(a.earth_tex)
        centre = cam.location + ed * (6371.0 + sh['alt'])
        for o in (e_, c_, a_):
            o.location = centre
            o.rotation_euler = (math.radians(a.elat if a.elat is not None else sh.get('elat', 24)), 0, math.radians(a.elon if a.elon is not None else sh.get('elon', 140)))
        # earthshine on the ship: soft, blue-white, from the planet's direction
        sun_light('Earthshine', ed, 5.2 * 0.055, (0.62, 0.76, 1.0), math.radians(50), ship)
        sun_light('SunE', sun_d, 5.2, (1.0, 0.97, 0.93), math.radians(0.53), earth_coll)
    world = bpy.data.worlds.new('W')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0, 0, 0, 1)
    scene.world = world
    # ---- render settings
    scene.render.engine = 'CYCLES'
    cy = scene.cycles
    cy.device = 'CPU'
    cy.samples = a.spp
    cy.use_adaptive_sampling = True
    cy.adaptive_threshold = 0.02
    cy.use_denoising = True
    cy.denoiser = 'OPENIMAGEDENOISE'
    cy.max_bounces = 6; cy.diffuse_bounces = 3; cy.glossy_bounces = 4; cy.transparent_max_bounces = 8; cy.volume_bounces = 0
    cy.sample_clamp_indirect = 8.0
    cy.caustics_reflective = False; cy.caustics_refractive = False
    scene.render.resolution_x, scene.render.resolution_y = W, Hh
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.threads_mode = 'AUTO'
    scene.view_settings.view_transform = 'AgX'
    try:
        scene.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        try:
            scene.view_settings.look = 'Medium High Contrast'
        except Exception:
            pass
    scene.view_settings.exposure = a.exposure
    if a.save_blend:
        bpy.ops.wm.save_as_mainfile(filepath=a.save_blend)

    def render_pass(name, hide_ship, hide_earth, spp):
        ship.hide_render = hide_ship
        if earth_coll:
            earth_coll.hide_render = hide_earth
        cy.samples = spp
        scene.render.image_settings.file_format = 'OPEN_EXR'
        scene.render.image_settings.color_mode = 'RGBA'
        scene.render.image_settings.color_depth = '32'
        path = os.path.join(a.tmp, '%s_%s.exr' % (a.shot, name))
        scene.render.filepath = path
        t1 = time.time()
        bpy.ops.render.render(write_still=True)
        print('%s pass: %.0fs' % (name, time.time() - t1)); sys.stdout.flush()
        return load_exr(path)

    ship_px = None
    if not a.earth_only:
        ship_px = render_pass('ship', False, True, a.spp)
    earth_px = render_pass('earth', True, False, max(24, a.spp // 4)) if earth_coll else None
    if ship_px is None:
        ship_px = np.zeros_like(earth_px)
    out = os.path.join(a.out, '%s-%s.jpg' % (ship_id, a.name or a.shot))
    finish(ship_px, earth_px, out, scene)
    print('wrote %s  total %.0fs' % (out, time.time() - t0))


if __name__ == '__main__':
    main()
