"""Horizon-class colony transport: high-detail model, path-traced stills and the game model.

Built on the Cochrane pipeline (see blender/prewarp_ships.md):
  /home/user/.bpyvenv/bin/python blender/build_horizon.py --shot hero --out blender/previews/horizon --tmp <scratch> --earth-tex <bmng.jpg>
  --shot hero|drums|stern|vault|berths|side   --drive   --no-earth   --game game/models/ships/horizon.glb
  --only takes aft,mid,fwd or the finer drive,htr,spinea,vault,spinef,ltr,hub,druma,drumf,berth,bow

Layout and sizes follow the class page (820 m; x toward the bow, y to port, z dorsal; metres; origin amidships on the axis):
three fusion chambers behind one shadow shield, six hot radiators, a truss spine carrying 28 drop pods, the cryo vault
ringed by six water tanks at the centre of mass, two contra-rotating 200 m drums, six drop ship berths, the command
section and a Whipple shield at the bow.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from mathutils import Matrix, Vector
import build_cochrane as C
import cochrane_kit as K
from build_cochrane import (k, on, P, R, rotX, rotY, rotZ, move, orient, TAU, PI, ycan, zcan, gbox, greeble, hrail,
                            axial_pipe, hatch, rcs_quad, dock_port, dish, floodlight, girder, rrect, arc_plate,
                            arc_decal, spine_truss)

NAME = 'HORIZON'
REG = 'UE-02   UNITED EARTH'

# design constants, as on the class page
D = dict(
    drumA=dict(x=175, r=100, rIn=88, w=60, g=0.9, dir=1),
    drumF=dict(x=245, r=100, rIn=88, w=60, g=0.9, dir=-1),
    hubR=16, sleeveR=18,
    drive=dict(n=3, off=15, r=6, x0=-396, x1=-358, exit=-410),
    shield=dict(x0=-344, x1=-334, r0=28, r1=38),
    htr=dict(x0=-330, len=118, w0=18, w1=46, n=6, rIn=8),
    ltr=dict(x0=4, len=92, w=52, rIn=30),
    vault=dict(x0=-96, x1=-36, r=12, jk=2.0),
    tanks=dict(n=6, r=8.5, len=40, R=24, xc=-66),
    pods=dict(r=7, len=30, R=21, rows=[-200, -166, -132, -30, 4, 38, 72], types=['mach', 'mach', 'pow', 'hab', 'hab', 'hab', 'store']),
    berth=dict(x=316, roof=24, n=6))

# the ship's own materials: (Cycles factory, game spec)
C.EXTRA['heat'] = (lambda: K.panel('heat', (0.30, 0.19, 0.11), (0.24, 0.15, 0.09), size=(1.4, 1.4), seam=0.03, seam_mul=0.45, rough=0.8, wob=0.01, grime=0.4),
                   ('pan', (0.30, 0.19, 0.11), (0.24, 0.15, 0.09), 0.45, (1.4, 1.4), 0, 0.8))
C.EXTRA['pgrey'] = (lambda: K.panel('pgrey', (0.55, 0.56, 0.57), (0.48, 0.49, 0.51), size=(2.4, 1.6), seam=0.022, seam_mul=0.5, rough=0.5, wob=0.015, grime=0.3, metal=0.15),
                    ('pan', (0.55, 0.56, 0.57), (0.48, 0.49, 0.51), 0.5, (2.4, 1.6), 0.15, 0.5))
C.EXTRA['green'] = (lambda: K.plain('green', (0.03, 0.2, 0.08), rough=0.5, grime=0.25), ('sim', (0.03, 0.2, 0.08), 0, 0.5))


def lo():
    """True while the lighter game model is being built."""
    return k.lod < 1


def axial_decal(name, text, x0, x1, r, ac, h, up=1, seg=6):
    """Lettering along a cylinder: it reads along the axis; h = letter height round the arc; up = +1 when the
    letters' tops point toward increasing angle."""
    da = h / r
    v, f, uv = [], [], []
    for j in range(seg + 1):
        a = ac - da / 2 + da * j / seg
        v.append(P(x0, r, a)); v.append(P(x1, r, a))
    for j in range(seg):
        f.append((2 * j, 2 * j + 1, 2 * j + 3, 2 * j + 2))
        v0, v1 = j / seg, (j + 1) / seg
        if up > 0:
            uv.append([(1, v0), (0, v0), (0, v1), (1, v1)])
        else:
            uv.append([(0, 1 - v0), (1, 1 - v0), (1, 1 - v1), (0, 1 - v1)])
    k.add('decal_' + name, v, f, uv)
    C.DECALS.append((name, text))


def ellipse_head(x_flat, r, depth, r_end, toward, n=8):
    """(x, r) points of an elliptical pressure head from the cylinder end at x_flat to a collar of radius r_end."""
    b = depth / math.sqrt(1 - (r_end / r) ** 2)
    pts = []
    for i in range(n + 1):
        d = depth * i / n
        pts.append((x_flat + toward * d, r * math.sqrt(max(0.0, 1 - (d / b) ** 2))))
    return pts


# off-axis plumes: the library's plume sits on the ship's axis, so move the finished object
_plume = C.build_plume


def _plume_at(coll, y=0.0, z=0.0, **kw):
    ob = _plume(coll, **kw)
    ob.location = (0, y, z)
    return ob


C.build_plume = _plume_at


# =====================================================================================
# drive: three mirror fusion chambers behind one shadow shield
# =====================================================================================
CAGE_X = (-396.5, -387.0, -377.5, -368.0, -358.5)
CAGE_R = 10.6


def chamber(drive, a_out):
    """One chamber on the local x axis: magnetic nozzle, mirror machine in its coil set and cage, direct converter.
    a_out is the direction away from the ship's axis."""
    # --- magnetic nozzle: four superconducting hoops in cryostats on a strut cone
    coils = [(-409.1, 9.5), (-405.6, 7.7), (-402.4, 6.1), (-399.7, 4.9)]
    for i, (x, r) in enumerate(coils):
        w = 0.9 - i * 0.07
        k.lathe(rrect(x - w, x + w, r - w, r + w, 0.28, 0.28, 3), 'steel', seg=64, closed=True, sharp=30)
        k.lathe(rrect(x - w * 0.5, x + w * 0.5, r - w - 0.1, r + w + 0.1, 0.15, 0.15, 2), 'gold', seg=64, closed=True, sharp=30)
        for j in range(10):
            with k.xf(on(x, (j + 0.5) * TAU / 10, r + w)):
                k.box((0, 0.16, 0), (w * 2.3, 0.4, 0.8), 'grey', bevel=0.05)
    n = 10
    for i in range(n):
        a, a2 = i * TAU / n, (i + 1) * TAU / n
        k.tube(P(-409.1, 10.9, a), P(-396.4, 6.7, a), 0.24, 'truss', seg=8)
        for (xa, ra), (xb, rb) in zip(coils, coils[1:]):
            p, q = (a, a2) if i % 2 else (a2, a)
            k.tube(P(xa, ra + 1.25, p), P(xb, rb + 1.2, q), 0.08, 'truss', seg=5)
        for j, (x, r) in enumerate(coils[1:]):
            rs = 10.9 - (x + 409.1) / 12.7 * 4.2
            k.tube(P(x, r + 0.8, a), P(x, rs, a), 0.1, 'truss', seg=5)
    for a in (a_out - 0.5, a_out + 0.5):
        k.pipe([P(-408.6, 11.3, a), P(-399, 8.0, a), P(-396.4, 7.6, a), P(-386, 7.6, a)], 0.18, 'gold', seg=8)
    # throat and plasma dump
    k.lathe([(-399.9, 2.2), (-398.6, 2.7), (-397.2, 3.9), (-396.0, 5.4)], 'bell', seg=40, sharp=60)
    k.lathe([(-399.8, 2.1), (-398.6, 2.6), (-396.2, 5.1)], 'black', seg=40, sharp=60)
    k.lathe([(-396.3, 5.1), (-396.3, 0)], 'plasma' if drive else 'black', seg=28)
    # --- mirror machine: vacuum vessel, solenoid coils, end mirror coils
    k.lathe([(-396, 0), (-396, 5.6), (-395.6, 6.0), (-358.4, 6.0), (-358, 5.6), (-358, 0)], 'vessel', seg=48)
    for i in range(8):
        x = -393.2 + i * 4.63
        big = i in (0, 7)
        w, h = (1.45, 1.6) if big else (1.1, 1.1)
        k.lathe(rrect(x - w, x + w, 6.1, 6.1 + 2 * h, 0.28, 0.28, 3), 'white_s', seg=56, closed=True, sharp=30)
        k.lathe(rrect(x - w - 0.12, x - w + 0.22, 6.0, 6.1 + 2 * h + 0.14, 0.07, 0.07, 1), 'grey', seg=56, closed=True)
        k.lathe(rrect(x + w - 0.22, x + w + 0.12, 6.0, 6.1 + 2 * h + 0.14, 0.07, 0.07, 1), 'grey', seg=56, closed=True)
        for j in range(12):
            gbox(x, (j + 0.5) * TAU / 12, 6.1 + 2 * h, 0.5, 0.5, 0.2, 'dark')
    # cage: twelve longerons, one pointing at the ship's axis, so the three cages can be tied together
    for i in range(12):
        a = a_out + PI + i * TAU / 12
        k.tube(P(CAGE_X[0], CAGE_R, a), P(CAGE_X[-1], CAGE_R, a), 0.2, 'truss', seg=8)
        for j, x in enumerate(CAGE_X):
            k.tube(P(x, CAGE_R, a), P(x, CAGE_R, a + TAU / 12), 0.13, 'truss', seg=5)
            if i % 2 == 0:
                k.tube(P(x, CAGE_R, a), P(x, 6.2, a), 0.11, 'truss', seg=5)
            if j < len(CAGE_X) - 1:
                p, q = (a, a + TAU / 12) if (i + j) % 2 else (a + TAU / 12, a)
                k.tube(P(x, CAGE_R, p), P(CAGE_X[j + 1], CAGE_R, q), 0.07, 'truss', seg=4)
    # neutral-beam injectors on the outboard side
    for da in (-0.8, 0.8):
        with k.xf(rotX(a_out + da)):
            with k.xf(orient((-376.9, 6.0, 0), (-0.16, 1, 0))):
                k.can(0, 4.9, 0.8, 'steel', seg=20)
                k.can(0.9, 1.3, 1.02, 'grey', seg=20)
                k.box((5.9, 0, 0), (2.2, 2.2, 2.0), 'white_s', bevel=0.1)
                k.box((5.9, 0, 1.1), (1.5, 1.5, 0.25), 'gold')
            k.pipe([(-378.6, 12.6, 0.7), (-378.6, 11.2, 1.9), (-366, 11.0, 1.9), (-359.5, 9.0, 1.9)], 0.15, 'silver', seg=6)
    for da in (-1.3, -0.35, 0.35, 1.3, 2.4, -2.4):
        axial_pipe(-395, -359, CAGE_R, a_out + da, 0.17, R.choice(['silver', 'gold', 'white_s']), clamp=4.75)
    for x in (-390, -381, -372, -363):
        k.torus(x, 9.6, 0.16, 'silver', seg=40, tseg=6)
    # --- direct energy converter: a drum of collector grids with its power conditioning outboard
    k.lathe([(-358, 0), (-358, 7.6), (-357.6, 8.0), (-350.4, 8.0), (-350, 7.6), (-350, 0)], 'white', seg=48)
    for x in (-357.3, -354.0, -350.7):
        k.lathe(rrect(x - 0.28, x + 0.28, 7.9, 8.45, 0.1, 0.1, 1), 'grey', seg=48, closed=True)
    k.lathe(rrect(-356.4, -355.2, 7.95, 8.08, 0.02, 0.02, 1), 'blue', seg=48, closed=True)
    for j in range(-2, 3):
        a = a_out + j * 0.5
        gbox(-352.4, a, 8.0, 2.0, 2.6, 1.2, 'white_s', bevel=0.1)
        gbox(-352.4, a + 0.25, 8.0, 1.4, 1.2, 0.6, R.choice(['gold', 'grey', 'dark']))
        with k.xf(on(-352.4, a, 9.2)):
            k.box((0, 0.1, 0), (1.5, 0.12, 2.2), 'radw')
    # --- thrust struts from the converter flange to the shield's backing ring
    for i in range(8):
        a = i * TAU / 8
        for s in (-1, 1):
            k.tube(P(-350.2, 7.8, a), P(-344.3, 10.6, a + s * TAU / 16), 0.2, 'truss', seg=6)
    k.torus(-344.5, 10.6, 0.3, 'grey', seg=40, tseg=6)


def build_drive(drive):
    V, SH = D['drive'], D['shield']
    cen = []
    for i in range(V['n']):
        a = TAU / 4 + i * TAU / 3
        cy, cz = V['off'] * math.cos(a), V['off'] * math.sin(a)
        cen.append((a, cy, cz))
        with k.xf(move(0, cy, cz)):
            chamber(drive, a)
        if drive:
            C.PLUMES.append(dict(x=-396.2, length=300.0, r0=4.6, r1=34.0, y=cy, z=cz))
    # --- cluster keel on the axis; the three cages are tied to it and to each other
    k.tube((-397, 0, 0), (-344.2, 0, 0), 1.1, 'truss', seg=16)
    for x in CAGE_X:
        k.can(x - 0.5, x + 0.5, 1.5, 'grey', seg=16, ch=0.1)
        for i in range(3):
            a, cy, cz = cen[i]
            b, dy, dz = cen[(i + 1) % 3]
            f = 1 - CAGE_R / V['off']
            k.tube((x, 0, 0), (x, cy * f, cz * f), 0.26, 'truss', seg=8)
            u = Vector((0, dy - cy, dz - cz)).normalized() * CAGE_R
            p0 = Vector((x, cy, cz)) + u
            p1 = Vector((x, dy, dz)) - u
            k.tube(p0, p1, 0.24, 'truss', seg=8)
            k.sphere(p0, 0.45, 'grey', seg=10, rings=6)
            k.sphere(p1, 0.45, 'grey', seg=10, rings=6)
    # --- shadow shield: a dished tungsten face toward the reactors, lithium hydride behind, on a ribbed backing frame
    x0, x1, r0, r1 = SH['x0'], SH['x1'], SH['r0'], SH['r1']
    k.lathe([(x0, 0), (x0, r0 - 2), (x0 + 0.6, r0 - 0.4), (x0 + 2.0, r0 + 1.6), (x1 - 3.4, r1 - 3.0), (x1 - 1.6, r1 - 0.8), (x1 - 1.0, r1)],
            'tungsten', seg=120, sharp=25)
    k.lathe([(x1 - 1.0, r1), (x1 - 0.3, r1), (x1, r1 - 0.4), (x1, 0)], 'white', seg=120, sharp=40)
    k.torus(x1 - 0.65, r1, 0.5, 'grey', seg=120, tseg=8)
    for i in range(24):
        a = i * TAU / 24
        k.pipe([P(x0 - 0.12, 4, a), P(x0 - 0.12, r0 - 2, a), P(x0 + 1.9, r0 + 1.5, a), P(x1 - 1.5, r1 - 0.7, a)], 0.11, 'steel', seg=5, elbow=False)
        with k.xf(rotX(a)):
            k.box((x1 + 0.35, 22.5, 0), (0.7, 29.0, 0.4), 'grey')
    for rr in (12.0, 24.0, 33.0):
        k.torus(x1 + 0.4, rr, 0.3, 'grey', seg=96, tseg=6)
    for i in range(6):          # shield support struts back to the thermal spine
        a = i * TAU / 6
        k.tube(P(x1 + 0.4, 33, a), P(x1 + 16, 5.2, a), 0.3, 'truss', seg=8)
        k.tube(P(x1 + 0.4, 24, a + TAU / 12), P(x1 + 9, 5.2, a + TAU / 12), 0.2, 'truss', seg=6)


# =====================================================================================
# thermal spine and the six high-temperature radiators
# =====================================================================================
def hot_slab(xa, xb, ya0, ya1, yb0, yb1, va, vb, t=0.14):
    """One radiator plate in the local x-y plane; UV v runs root to tip for the black-body material."""
    v = [(xa, ya0, -t), (xb, yb0, -t), (xb, yb1, -t), (xa, ya1, -t), (xa, ya0, t), (xb, yb0, t), (xb, yb1, t), (xa, ya1, t)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)]
    q = [(xa, va), (xb, va), (xb, vb), (xa, vb)]
    uvs = [[q[0], q[3], q[2], q[1]], q, [q[0], q[1], q[1], q[0]], [q[2], q[3], q[3], q[2]], [q[1], q[2], q[2], q[1]], [q[3], q[0], q[0], q[3]]]
    k.add('hot', v, f, uvs)


def build_htr():
    H, SH = D['htr'], D['shield']
    # the spine: a structural tube with the coolant headers outside it (the old model had one solid 14 m bar)
    k.lathe([(SH['x1'], 5.0), (-212, 5.0)], 'grey', seg=32)
    for x in np.arange(-331, -212, 9.83):
        k.lathe(rrect(x - 0.25, x + 0.25, 4.9, 5.45, 0.08, 0.08, 1), 'dark', seg=32, closed=True)
    k.lathe([(-212.6, 5.0), (-212.6, 0)], 'grey', seg=32)
    for s1 in (1, -1):          # the spine's end fitting into the square truss
        for s2 in (1, -1):
            a = math.atan2(s2, s1)
            k.tube(P(-222, 5.0, a), (-211.5, s1 * 9, s2 * 9), 0.4, 'truss', seg=8)
            k.tube(P(-214, 5.0, a + 0.5), (-211.5, s1 * 9, s2 * 9), 0.25, 'truss', seg=6)
            k.tube(P(-214, 5.0, a - 0.5), (-211.5, s1 * 9, s2 * 9), 0.25, 'truss', seg=6)
    x0, L = H['x0'], H['len']
    x1 = x0 + L
    nseg = 12 if not lo() else 6
    rt = H['rIn']
    wid = lambda x: H['w0'] + (H['w1'] - H['w0']) * (x - x0) / L
    for q in range(H['n']):
        with k.xf(rotX(TAU / 12 + q * TAU / H['n'])):
            # coolant out and back along the root, glowing under power
            k.tube((x0 - 3, 6.7, 1.0), (x1 + 1, 6.7, 1.0), 0.6, 'hotpipe', seg=10)
            k.tube((x0 - 3, 6.7, -1.0), (x1 + 1, 6.7, -1.0), 0.6, 'hotpipe', seg=10)
            for s in (1, -1):
                k.tube((x0 - 3, 6.7, s * 1.0), (x0 - 3.6, 5.0, s * 0.6), 0.6, 'hotpipe', seg=10)
            for i in range(nseg):
                xa = x0 + L * i / nseg + 0.25
                xb = x0 + L * (i + 1) / nseg - 0.25
                wa, wb = wid(xa), wid(xb)
                hot_slab(xa, xb, rt, rt + wa / 2 - 0.2, rt, rt + wb / 2 - 0.2, 0.0, 0.5)
                hot_slab(xa, xb, rt + wa / 2 + 0.2, rt + wa, rt + wb / 2 + 0.2, rt + wb, 0.5, 1.0)
                # frame: ribs at the joints, a mid stringer and the edge boom
                k.box((xa - 0.25, rt + wa / 2, 0), (0.24, wa, 0.6), 'cc')
                k.tube((xa, rt + wa + 0.3, 0), (xb, rt + wb + 0.3, 0), 0.24, 'cc', seg=6)
                k.tube((xa - 0.5, rt + wa / 2, 0), (xb + 0.5, rt + wb / 2, 0), 0.2, 'cc', seg=6)
                k.tube((xa, 7.0, 1.0), (xa, rt + 0.2, 0), 0.22, 'hotpipe', seg=6)
            k.box((x1 + 0.05, rt + H['w1'] / 2, 0), (0.24, H['w1'], 0.6), 'cc')
            for xs_ in (x0 + 19.7, x0 + 39.3, x0 + 59, x0 + 78.7, x0 + 98.3, x1):      # stay rods keep the panels flat
                ws = wid(xs_)
                for s in (1, -1):
                    k.tube((xs_, rt + ws, 0), (xs_ - 14, 5.2, s * 2.4), 0.07, 'cc', seg=4)
                    k.tube((xs_, rt + ws / 2, 0), (xs_ - 7, 5.2, s * 2.0), 0.07, 'cc', seg=4)


# =====================================================================================
# spine: truss, utility run, drop pods
# =====================================================================================
PODT = {'hab': ('white', 'blue', 'HABITAT'), 'mach': ('pgrey', 'hazard', 'MACHINERY'),
        'pow': ('white_s', 'orange', 'POWER'), 'store': ('tank', 'green', 'STORES')}


def pod(xs, ty, a_ship):
    """A drop pod on the local x axis, heat shield aft at xs. Local -y points at the ship's axis."""
    body, band, text = PODT[ty]
    r = 7.0
    seg = 44
    # heat shield: a blunt cap of ablator tiles; the pod flies this end first
    k.lathe([(xs, 0), (xs + 0.12, 2.4), (xs + 0.5, 4.7), (xs + 1.1, 6.5), (xs + 1.6, 7.25), (xs + 2.0, 7.42)], 'heat', seg=seg, sharp=50)
    k.lathe([(xs + 2.0, 7.42), (xs + 2.5, 7.42), (xs + 2.9, r)], 'grey', seg=seg, sharp=30)
    k.lathe([(xs + 2.9, r), (xs + 23.5, r)], body, seg=seg)
    # back shell and parachute canister
    k.lathe([(xs + 23.5, r), (xs + 24.2, 6.85), (xs + 28.3, 3.7), (xs + 28.6, 3.4)], body, seg=seg, sharp=30)
    k.lathe([(xs + 28.6, 3.4), (xs + 29.7, 3.4), (xs + 30.0, 3.1), (xs + 30.0, 0)], 'grey', seg=24, sharp=40)
    k.lathe(rrect(xs + 11.2, xs + 13.4, r - 0.02, r + 0.07, 0.03, 0.03, 1), band, seg=seg, closed=True)
    if not lo():
        k.lathe(rrect(xs + 29.0, xs + 29.5, 3.38, 3.5, 0.03, 0.03, 1), band, seg=24, closed=True)
        for x in (xs + 3.4, xs + 8.6, xs + 16.6, xs + 23.2):
            k.lathe(rrect(x - 0.18, x + 0.18, r - 0.02, r + 0.16, 0.04, 0.04, 1), 'grey', seg=seg, closed=True)
        for q in range(4):          # the pod's own thruster quads
            with k.xf(on(xs + 20.6, q * TAU / 4 + TAU / 8, r + 0.33) @ Matrix.Scale(0.5, 4)):
                rcs_quad()
        hatch(xs + 6.4, 0.0, r, 0.8)
        for a in (-0.3, 0.3):
            hrail(xs + 4, xs + 19, r, a)
        if ty == 'hab':
            for i in range(5):
                with k.xf(on(xs + 15 + i * 1.6, 0.0, r)):
                    k.box((0, 0.02, 0), (0.9, 0.08, 0.9), 'grey')
                    k.box((0, 0.05, 0), (0.6, 0.06, 0.6), 'glass_dark')
        elif ty == 'mach':          # cargo door outline
            for a in (-1.05, -0.55):
                with k.xf(on(xs + 17, a, r)):
                    k.box((0, 0.04, 0), (7.0, 0.1, 0.25), 'grey')
            for x in (xs + 13.6, xs + 20.4):
                arc_plate(x - 0.12, x + 0.12, r, r + 0.1, -1.05, -0.55, 'grey', seg=3)
        elif ty == 'pow':           # the reactor's own radiator strips, stowed
            for a in (-1.1, -0.8, 0.8, 1.1):
                with k.xf(on(xs + 18.4, a, r)):
                    k.box((0, 0.12, 0), (8.0, 0.14, 1.5), 'dark')
        greeble(xs + 3.5, xs + 23, r, 5, a0=-1.2, span=2.4, hmax=0.4)
        for ac in (0.62, -0.62):
            up = 1 if math.cos(a_ship + ac) > 0 else -1
            L = 1.9 * (0.62 * len(text) + 0.3)
            axial_decal('pod_' + ty, text, xs + 4.2, xs + 4.2 + L, r + 0.03, ac, 1.9, up=up, seg=3)
    else:
        for q in range(4):
            gbox(xs + 20.6, q * TAU / 4 + TAU / 8, r, 1.2, 1.2, 0.8, 'white_s', bevel=0)
    # clamps to the truss corner (8.27 m inboard), in hazard yellow because they let go
    for x in (xs + 6, xs + 19):
        with k.xf(on(x, PI, r)):
            k.box((0, 0.66, 0), (2.4, 1.32, 2.6), 'grey', bevel=0.08)
            k.box((0, 0.3, 0), (1.0, 0.6, 3.6), 'hazard', bevel=0.06)
        for dz in (1.5, -1.5):
            k.tube((x, -r + 0.2, dz), (x + 2.6, -8.27, 0), 0.14, 'truss', seg=5)
            k.tube((x, -r + 0.2, dz), (x - 2.6, -8.27, 0), 0.14, 'truss', seg=5)
    k.pipe([(xs + 12.3, -r, 0.9), (xs + 12.3, -8.0, 0.9)], 0.12, 'gold', seg=5)      # umbilical


def pod_rows(rows):
    PD = D['pods']
    for xs in rows:
        ty = PD['types'][PD['rows'].index(xs)]
        for q in range(4):
            a = TAU / 8 + q * TAU / 4
            with k.xf(rotX(a) @ move(0, PD['R'], 0)):
                pod(xs, ty, a)


def utilities(x0, x1):
    """Propellant main, coolant and power runs inside the truss, on hangers."""
    k.tube((x0, 3, 3), (x1, 3, 3), 0.9, 'silver', seg=14)
    k.tube((x0, -3, -3), (x1, -3, -3), 0.55, 'gold', seg=12)
    k.tube((x0, -3.2, 3), (x1, -3.2, 3), 0.42, 'white_s', seg=10)
    k.tube((x0, 3.2, -3), (x1, 3.2, -3), 0.42, 'white_s', seg=10)
    k.tube((x0, 0, -4.6), (x1, 0, -4.6), 0.3, 'gold', seg=8)
    k.boxX(x0, x1, 5.4, -7.7, 1.3, 0.3, 'dark')
    k.boxX(x0, x1, -5.4, 7.7, 1.3, 0.3, 'dark')
    n = max(2, round((x1 - x0) / (9.6 if not lo() else 20)))
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        k.box((x, 0, 0), (0.35, 9.5, 0.35), 'grey')
        k.box((x, 0, 0), (0.35, 0.35, 9.5), 'grey')
        for (y, z, r_) in ((3, 3, 1.05), (-3, -3, 0.7), (-3.2, 3, 0.55), (3.2, -3, 0.55)):
            with k.xf(move(x, y, z)):
                k.can(-0.2, 0.2, r_, 'grey', seg=12, ch=0.04)
        for (y, z) in ((5.4, -8.0), (-5.4, 7.4)):
            k.box((x, y, z), (0.4, 1.6, 0.5), 'grey')


def pallets(x0, x1, faces):
    """Equipment pallets on the free truss faces (the corners carry the pods)."""
    for (ay, az) in faces:
        for x in np.arange(x0, x1, 9.2):
            t = R.random()
            c = lambda u, w: (x, ay * (9.5 + u) + az * w, az * (9.5 + u) + ay * w)
            sz = lambda dx, du, dw: (dx, du if ay else dw, dw if ay else du)
            if t < 0.3:
                k.box(c(0.5, R.uniform(-4, 4)), sz(R.uniform(3, 6), 1.0, R.uniform(2, 4)), R.choice(['white_s', 'gold', 'grey']), bevel=0.1)
            elif t < 0.5:
                for dw in (-1.5, 1.5):
                    for dx in (-1.5, 1.5):
                        p = c(1.2, dw)
                        k.sphere((p[0] + dx, p[1], p[2]), 1.15, R.choice(['silver', 'gold']), seg=16, rings=8)
                k.box(c(0.2, 0), sz(5.6, 0.3, 5.6), 'grey')
            elif t < 0.66:
                k.box(c(0.35, 0), sz(4.5, 0.2, 6.5), 'radw')
                k.box(c(0.15, 0), sz(4.9, 0.25, 0.4), 'grey')
            elif t < 0.82:
                w0 = R.uniform(-4, 4)
                k.box(c(0.7, w0), sz(2.2, 1.4, 2.0), 'white_s', bevel=0.1)
                p = c(0.5, w0)
                k.box((p[0] + 2.4, p[1], p[2]), sz(1.6, 1.0, 1.4), 'gold', bevel=0.08)


def build_spine_aft():
    # aft thruster quads on booms that reach past the pods, so they do not fire onto them
    for q in range(4):
        with k.xf(rotX(TAU / 8 + q * TAU / 4)):
            girder((-207, 12.7, 0), (-207, 30.2, 0), 1.6, bay=1.9, rc=0.13, rd=0.06)
            k.tube((-212, 12.7, 0), (-207, 26, 0), 0.14, 'truss', seg=5)
            with k.xf(move(-207, 31.8, 0) @ Matrix.Scale(1.3, 4)):
                rcs_quad()
    # fusion fuel: deuterium and helium-3 dewars in foil, on the port and starboard truss faces
    for s in (1, -1):
        k.sphere((-206, s * 13.6, 0), 3.6, 'gold', seg=40, rings=20)
        with k.xf(move(-206, s * 13.6, 0)):
            k.torus(0, 3.65, 0.14, 'grey', seg=36, tseg=6)
            with k.xf(rotZ(PI / 2)):
                k.torus(0, 3.65, 0.14, 'grey', seg=36, tseg=6)
        for dx in (-2.2, 2.2):
            for dz in (-2.2, 2.2):
                k.tube((-206 + dx, s * 10.8, dz), (-206 + dx * 1.5, s * 9.2, dz * 1.6), 0.13, 'truss', seg=6)
        k.pipe([(-206, s * 10.1, 0.8), (-206, s * 8, 0.8), (-209, s * 3.4, 1.0), (-214, s * 3.4, 4.0)], 0.15, 'gold', seg=6)
    spine_truss(-212, -104, 9, 6)
    utilities(-210, -105)
    pallets(-196, -108, [(0, 1), (0, -1), (1, 0), (-1, 0)])
    k.boxX(-210, -105, 9.8, 9.2, 0.25, 0.25, 'yellow')
    pod_rows(D['pods']['rows'][:3])
    floodlight((-150, 0, 10.5), (-150, 18, 30), energy=9000, spot=1.3)


def build_vault():
    VA, T = D['vault'], D['tanks']
    RJ = VA['r'] + VA['jk']
    x0, x1 = VA['x0'], VA['x1']
    # the jacketed vault: a pressure hull with elliptical heads (the old model's ends were flat cones)
    prof = ellipse_head(x0, RJ, 5.0, 8.0, -1)[::-1] + ellipse_head(x1, RJ, 5.0, 8.0, 1)
    k.lathe(prof, 'white_m', seg=96, sharp=30)
    for x in (x0 + 2, x0 + 20, x1 - 20, x1 - 2):
        k.lathe(rrect(x - 0.45, x + 0.45, RJ - 0.05, RJ + 0.35, 0.08, 0.08, 1), 'grey', seg=96, closed=True)
    k.lathe(rrect(-68, -64, RJ - 0.02, RJ + 0.12, 0.03, 0.03, 1), 'blue', seg=96, closed=True)
    for q in range(6):
        a = q * TAU / 6
        axial_pipe(x0 + 1, x1 - 1, RJ, a, 0.32, 'gold', clamp=5.0)
        axial_pipe(x0 + 1, x1 - 1, RJ, a + 0.07, 0.16, 'silver', clamp=5.0)
        hrail(x0 + 4, x0 + 18, RJ, a - 0.12)
        hrail(x1 - 18, x1 - 4, RJ, a - 0.12)
        hatch(x0 + 11, a + 0.2, RJ, 0.85)
        greeble(x0 + 3, x1 - 3, RJ, 9, a0=a - 0.2, span=0.4, hmax=0.6)
    # thrust collars: the vault carries the spine's load between the two truss sections
    for xe, xt, d in ((x0 - 5, -104, -1), (x1 + 5, -29, 1)):
        k.lathe(rrect(xe - 0.7, xe + 0.7, 7.6, 8.7, 0.2, 0.2, 2), 'grey', seg=48, closed=True)
        if d < 0:
            k.lathe([(xe, 8.0), (xe, 0)], 'grey', seg=32)
        for s1 in (1, -1):
            for s2 in (1, -1):
                a = math.atan2(s2, s1)
                for da in (-0.55, 0.0, 0.55):
                    k.tube(P(xe, 8.4, a + da), (xt, s1 * 9, s2 * 9), 0.32 if da == 0 else 0.22, 'truss', seg=8)
    # revival bay and storm shelter on the vault's forward head, inside the truss; embryo and seed dewars ahead of it
    k.lathe([(x1 + 5, 0), (x1 + 5, 6.0), (x1 + 5.4, 6.4), (x1 + 13.0, 6.4), (x1 + 14.2, 5.6), (x1 + 15.0, 3.6), (x1 + 15.2, 0)], 'white', seg=48, sharp=30)
    for x in (x1 + 6.2, x1 + 12.4):
        k.lathe(rrect(x - 0.22, x + 0.22, 6.35, 6.7, 0.06, 0.06, 1), 'grey', seg=48, closed=True)
    k.lathe(rrect(x1 + 8.6, x1 + 9.8, 6.38, 6.5, 0.03, 0.03, 1), 'orange', seg=48, closed=True)
    for i in range(10):
        with k.xf(on(x1 + 10.9, i * TAU / 10 + 0.2, 6.4)):
            k.box((0, 0.02, 0), (1.3, 0.08, 1.0), 'grey')
            k.box((0, 0.05, 0), (0.95, 0.06, 0.7), 'glass' if R.random() < 0.7 else 'glass_dark')
    for i in range(4):
        a = i * TAU / 4
        k.sphere(P(x1 + 17.2, 2.6, a), 1.5, 'gold' if i % 2 else 'silver', seg=18, rings=10)
        k.tube(P(x1 + 15.0, 2.6, a), P(x1 + 19.6, 2.6, a), 0.12, 'truss', seg=5)
    k.tube((x1 + 15, 0, 0), (x1 + 20, 0, 0), 0.7, 'grey', seg=12)
    # --- six water propellant tanks round the vault: domed, banded, on saddle cradles
    tx0, tx1 = T['xc'] - T['len'] / 2 - 4, T['xc'] + T['len'] / 2 + 4
    for q in range(T['n']):
        a = TAU / 12 + q * TAU / T['n']
        y, z = T['R'] * math.cos(a), T['R'] * math.sin(a)
        k.dome_tank(tx0, tx1, T['r'], 'tank', k=0.6, seg=56, rings=8, oy=y, oz=z)
        for x in (tx0 + 6.5, tx0 + 15, T['xc'], tx1 - 15, tx1 - 6.5):
            k.lathe(rrect(x - 0.25, x + 0.25, T['r'] - 0.02, T['r'] + 0.12, 0.03, 0.03, 1), 'grey', seg=56, closed=True, oy=y, oz=z)
        for x in (tx0 + 11, tx1 - 11):
            k.lathe(rrect(x - 0.7, x + 0.7, T['r'], T['r'] + 0.45, 0.1, 0.1, 1), 'grey', seg=56, closed=True, oy=y, oz=z)
            with k.xf(on(x, a, RJ)):
                k.box((0, 0.7, 0), (1.8, 1.5, 6.5), 'grey', bevel=0.1)
            for da in (-1.0, 1.0):
                k.tube(P(x, RJ + 0.1, a + da * 0.36), (x, y + (T['r'] + 0.4) * math.cos(a + PI + da * 1.15), z + (T['r'] + 0.4) * math.sin(a + PI + da * 1.15)), 0.3, 'truss', seg=6)
        with k.xf(rotX(a)):
            k.pipe([(tx0 + 0.7, T['R'], 0), (tx0 - 2.0, T['R'], 0), (tx0 - 3.2, 16.8, 0)], 0.42, 'silver', seg=8)
            k.box((tx0 - 2.0, T['R'] + 0.7, 0), (1.5, 1.1, 1.5), 'grey', bevel=0.06)
            k.pipe([(tx1 - 0.7, T['R'], 0), (tx1 + 1.6, T['R'], 0), (tx1 + 2.6, 13.0, 0)], 0.2, 'silver', seg=6)
            k.sphere((tx1 + 1.2, T['R'] - 4.6, 0), 1.5, 'gold', seg=16, rings=8)      # pressurant bottle
            hrail(tx0 + 8, tx1 - 8, T['R'] + T['r'], 0.0)
            hatch(T['xc'] + 6, 0.0, T['R'] + T['r'], 0.7)
        with k.xf(move(0, y, z) @ rotX(a)):
            greeble(tx0 + 7, tx1 - 7, T['r'], 6, a0=-0.5, span=1.0, hmax=0.5)
    k.torus(tx0 - 3.2, 16.8, 0.5, 'silver', seg=64, tseg=8)
    k.pipe([(tx0 - 3.2, 3 + 13.6, 3.0), (tx0 - 9, 9.5, 3.4), (tx0 - 13, 3, 3)], 0.5, 'silver', seg=8)
    k.torus(tx1 + 2.6, 13.0, 0.25, 'silver', seg=64, tseg=6)
    floodlight((-66, 0, 15.2), (-50, 30, 40), energy=7000, spot=1.3)


def build_spine_fwd():
    spine_truss(-29, 129, 9, 9)
    utilities(-14, 128)
    pallets(-24, 0, [(0, 1), (0, -1), (1, 0), (-1, 0)])
    pallets(104, 126, [(0, 1), (0, -1), (1, 0), (-1, 0)])
    pallets(2, 100, [(0, 1), (0, -1)])
    k.boxX(-27, 128, 9.8, 9.2, 0.25, 0.25, 'yellow')
    k.box((60, 10.1, 9.5), (2.4, 1.2, 0.9), 'white_s', bevel=0.08)        # EVA cart on its rail
    pod_rows(D['pods']['rows'][3:])
    floodlight((30, 0, 10.5), (40, -18, 30), energy=9000, spot=1.3)


def build_ltr():
    """Low-temperature radiators: segmented white panels on booms, braced from king posts."""
    L = D['ltr']
    nx, ny = (8, 6) if not lo() else (4, 3)
    x0, Ln, w, ri = L['x0'], L['len'], L['w'], L['rIn']
    for s in (1, -1):
        for i in range(nx):
            xa = x0 + Ln * i / nx + 0.18
            xb = x0 + Ln * (i + 1) / nx - 0.18
            for j in range(ny):
                ya = ri + w * j / ny + 0.14
                yb = ri + w * (j + 1) / ny - 0.14
                k.box(((xa + xb) / 2, s * (ya + yb) / 2, 0), (xb - xa, yb - ya, 0.14), 'radw')
            x = x0 + Ln * i / nx
            if i:
                k.tube((x, s * ri, 0), (x, s * (ri + w), 0), 0.18, 'silver', seg=6)
        for j in range(ny + 1):
            y = s * (ri + w * j / ny)
            k.tube((x0, y, 0), (x0 + Ln, y, 0), 0.12, 'grey', seg=5)
        k.tube((x0 - 1, s * (ri - 0.9), 0.5), (x0 + Ln + 1, s * (ri - 0.9), 0.5), 0.45, 'silver', seg=10)
        k.tube((x0 - 1, s * (ri - 0.9), -0.5), (x0 + Ln + 1, s * (ri - 0.9), -0.5), 0.45, 'white_s', seg=10)
        for x in (x0 + 6, x0 + Ln / 2, x0 + Ln - 6):
            girder((x, s * 9.5, 0), (x, s * (ri - 1.5), 0), 1.8, bay=1.8, rc=0.13, rd=0.06)
            with k.xf(orient((x, s * 9.2, 0), (0, s, 0))):
                k.can(0, 1.2, 1.6, 'grey', seg=20)
            k.pipe([(x, s * (ri - 0.9), 0.5), (x, s * 9.6, 1.4), (x, s * 3.2, 3.0)], 0.2, 'silver', seg=6)
            # king post at the root and stay rods to the panel frame
            k.tube((x, s * (ri - 1.4), -13), (x, s * (ri - 1.4), 13), 0.22, 'truss', seg=8)
            for zz in (13, -13):
                k.sphere((x, s * (ri - 1.4), zz), 0.4, 'grey', seg=10, rings=6)
                for fr in (0.5, 1.0):
                    k.tube((x, s * (ri - 1.4), zz), (x, s * (ri + w * fr), 0), 0.07, 'truss', seg=4)
                for dx in (-Ln / 2 + 6, Ln / 2 - 6):
                    if x0 + 6 < x + dx * 0.5 < x0 + Ln - 6:
                        k.tube((x, s * (ri - 1.4), zz), (x + dx * 0.5, s * (ri + w), 0), 0.07, 'truss', seg=4)
        for x in (x0, x0 + Ln):
            k.tube((x, s * (ri - 0.5), 0), (x, s * (ri + w), 0), 0.22, 'grey', seg=6)
        xm = x0 + Ln / 2
        k.box((xm, s * (ri + w + 0.7), 0), (1.4, 1.0, 1.0), 'navR' if s > 0 else 'navG')
        C.LIGHTS.append(('POINT', (xm, s * (ri + w + 1.8), 0), None, 3000, (1, 0.05, 0.03) if s > 0 else (0.05, 1, 0.2), 0))


# =====================================================================================
# hub and the two contra-rotating drums
# =====================================================================================
def build_hub():
    hr = D['hubR']
    # fixed hub from the truss to the docking node
    k.lathe([(129.2, 0), (129.2, hr - 1.2), (130.2, hr), (333.0, hr), (336.0, 13.0), (337.0, 13.0)], 'white', seg=96, sharp=30)
    for s1 in (1, -1):
        for s2 in (1, -1):
            k.box((129.6, s1 * 9, s2 * 9), (1.4, 2.4, 2.4), 'grey', bevel=0.15)
    # bearing housings either side of each drum, and the one motor between them
    for xa, xb, motor in ((139.0, 144.6, False), (205.4, 214.6, True), (275.4, 281.0, False)):
        k.lathe(rrect(xa, xb, hr - 0.1, 22.0, 0.5, 0.5, 3), 'grey', seg=120, closed=True, sharp=30)
        for x in (xa + 0.7, xb - 0.7):
            k.lathe(rrect(x - 0.3, x + 0.3, 21.9, 22.3, 0.08, 0.08, 1), 'dark', seg=120, closed=True)
        n = 30
        for j in range(n):
            gbox((xa + xb) / 2, j * TAU / n, 22.0, min(3.2, xb - xa - 2.2), 1.3, 0.4, 'grey')
        if motor:
            k.lathe(rrect(209.2, 210.8, 21.95, 22.5, 0.08, 0.08, 1), 'orange', seg=120, closed=True)
            for j in range(12):
                a = j * TAU / 12 + 0.1
                gbox(207.6, a, 22.0, 2.4, 2.6, 1.3, 'white_s', bevel=0.1)
                gbox(212.4, a + 0.26, 22.0, 2.4, 2.2, 1.0, R.choice(['white_s', 'gold', 'grey']), bevel=0.1)
                with k.xf(on(207.6, a, 23.3)):
                    k.box((0, 0.1, 0), (1.8, 0.12, 2.2), 'radw')
    greeble(131, 138.5, hr, 26, hmax=0.6)
    for a in (0.4, 2.0, 3.6, 5.2):
        axial_pipe(130.6, 138.8, hr, a, 0.3, 'gold')
        axial_pipe(130.6, 138.8, hr, a + 0.12, 0.16, 'silver')
        hrail(131, 138, hr, a + 0.5)
    hatch(134.5, 1.2, hr, 0.9)
    hatch(134.5, 4.4, hr, 0.9)


def drum(xc, phase, lit=0.62):
    """One spin drum: three pressure hoops side by side (the old model was one flat-walled slab), six elevator
    spokes into the middle hoop, stay rods from the turning sleeve to the outer hoops."""
    rIn, rOut = 88.0, 100.0
    hr = rOut - 0.5
    hw, gap = 9.7, 0.9
    pitch = 2 * hw + gap
    rs = D['sleeveR']
    seg = 240
    nfr = 24
    low = lo()

    def label(a):
        for i in range(6):
            d = (a - i * TAU / 6 + PI) % TAU - PI
            if abs(d) < 0.215:
                return True
        return False

    with k.xf(move(xc, 0, 0) @ rotX(phase)):
        for h in (-1, 0, 1):
            x0, x1 = h * pitch - hw, h * pitch + hw
            k.lathe(rrect(x0, x1, rIn, hr, 4.5, 3.0, 6), 'white_m', seg=seg, closed=True, sharp=25)
            for i in range(nfr):          # ring frames
                a = (i + 0.5) * TAU / nfr
                da = 0.4 / rOut
                k.lathe(rrect(x0 - 0.16, x1 + 0.16, rIn - 0.16, hr + 0.1, 4.6, 3.1, 4), 'grey', seg=1, a0=a - da, a1=a + da, closed=True, caps=True)
            # rim bumper: spaced plates over the water jacket, on the flat of the rim
            xa0, xa1 = x0 + 3.1, x1 - 3.1
            nrow = 4
            rw = (xa1 - xa0) / nrow
            if low:
                for j in range(nrow):
                    xa = xa0 + j * rw + 0.06
                    if h == 0 and j in (1, 2):
                        for i in range(12):
                            a0, a1 = (i - 0.5) * TAU / 12, (i + 0.5) * TAU / 12
                            lab = i % 2 == 0
                            d = 0.215 if lab else TAU / 12 - 0.215 + TAU / 24
                            c = i * TAU / 12
                            # white window round each label, blue between
                            if lab:
                                k.lathe([(xa, rOut), (xa + rw - 0.12, rOut)], 'tile', seg=20, a0=c - 0.215, a1=c + 0.215, vref=rOut)
                            else:
                                k.lathe([(xa, rOut), (xa + rw - 0.12, rOut)], 'blue', seg=60, a0=c - TAU / 12 + 0.215, a1=c + TAU / 12 - 0.215, vref=rOut)
                    else:
                        k.lathe([(xa, rOut), (xa + rw - 0.12, rOut)], 'tile', seg=seg, vref=rOut)
            else:
                ntile = 156
                for i in range(ntile):
                    a0 = (i + 0.03) * TAU / ntile
                    a1 = (i + 0.97) * TAU / ntile
                    am = (i + 0.5) * TAU / ntile
                    for j in range(nrow):
                        if R.random() < 0.008:
                            continue
                        xa = xa0 + j * rw + 0.06
                        mat = 'tile' if R.random() > 0.03 else 'tile2'
                        if h == 0 and j in (1, 2) and not label(am):
                            mat = 'blue'
                        arc_plate(xa, xa + rw - 0.12, rOut - 0.12, rOut, a0, a1, mat, vref=rOut)
                for j in range(nrow + 1):
                    k.torus(xa0 + j * rw, rOut - 0.3, 0.1, 'grey', seg=seg, tseg=4)
            # hub-facing roof: skylight strips, conduits, equipment
            nsk = nfr * 3 if not low else nfr
            for xm in ((-2.9, 2.9) if h == 0 else (0.0,)):
                for i in range(nsk):
                    a0 = (i + 0.2) * TAU / nsk
                    a1 = (i + 0.8) * TAU / nsk
                    xmm = h * pitch + xm
                    if not low:
                        arc_plate(xmm - 1.4, xmm + 1.4, rIn - 0.1, rIn + 0.02, a0, a1, 'grey', seg=3)
                    arc_plate(xmm - 1.15, xmm + 1.15, rIn - 0.14, rIn - 0.08, a0 + 0.004, a1 - 0.004, 'glass' if R.random() < lit * 0.8 else 'glass_dark', seg=3)
            for xx, mat in ((-4.9, 'silver'), (4.9, 'gold'), (-5.5, 'white_s')):
                k.torus(h * pitch + xx, rIn - 0.25, 0.2, mat, seg=seg, tseg=6)
            for i in range(nfr * 2):
                a = i * TAU / (nfr * 2) + 0.05
                with k.xf(rotX(a) @ move(h * pitch + R.uniform(-5.2, 5.2), rIn, 0) @ rotZ(PI)):
                    k.box((0, 0.35, 0), (R.uniform(1, 2.6), 0.7, R.uniform(1, 2.2)), R.choice(C.GM), bevel=0.06)
        # the joints between hoops: tie plates at every frame, crew vestibules between
        for s in (-1, 1):
            xg = s * pitch / 2
            for i in range(nfr):
                a = (i + 0.5) * TAU / nfr
                with k.xf(rotX(a)):
                    k.box((xg, 94.2, 0), (1.5, 5.6, 0.9), 'grey', bevel=0.08)
                    k.box((xg, rIn + 0.4, 0), (5.0, 0.6, 0.9), 'grey', bevel=0.08)
                    k.box((xg, hr - 0.9, 0), (4.4, 0.5, 0.9), 'grey', bevel=0.08)
                if i % 2 == 0:
                    with k.xf(rotX(a + TAU / nfr / 2)):
                        k.can(xg - 0.75, xg + 0.75, 1.7, 'white_s', seg=16, oy=94.3, ch=0.1)
        # name and registry on the middle hoop's rim, three times round
        for i in range(3):
            c = i * TAU / 3
            arc_decal('name', NAME, -3.1, 3.1, rOut + 0.04, c - 0.16, c + 0.16, seg=30)
            arc_decal('reg', REG, -1.5, 1.5, rOut + 0.04, c + TAU / 6 - 0.19, c + TAU / 6 + 0.19, seg=30)
        # end walls of the outer hoops: two decks of small windows, rails, lockers
        for s in (1, -1):
            xs = s * (pitch + hw + 0.02)
            for (rw_, lit_) in ((96.0, lit), (93.1, lit * 0.8)):
                nw = int(TAU * rw_ / (3.6 if not low else 7.2))
                for i in range(nw):
                    if (i % 7) in (5, 6) or R.random() < 0.06:
                        continue
                    a = (i + 0.5) * TAU / nw
                    with k.xf(rotX(a)):
                        if not low:
                            k.box((xs + s * 0.03, rw_, 0), (0.1, 1.3, 1.6), 'white_s')
                        k.box((xs + s * 0.06, rw_, 0), (0.08, 0.85, 1.15), 'glass' if R.random() < lit_ else 'glass_dark')
            for rr in (97.4, 91.7):
                k.torus(xs + s * 0.45, rr, 0.045, 'yellow', seg=seg, tseg=4)
            for i in range(nfr * 2):
                a = (i + 0.25) * TAU / (nfr * 2)
                with k.xf(rotX(a)):
                    t = R.random()
                    if t < 0.4:
                        k.box((xs + s * 0.25, R.uniform(92.2, 96.8), 0), (0.5, R.uniform(0.8, 2.2), R.uniform(0.8, 2.4)), R.choice(C.GM), bevel=0.05)
                    elif t < 0.52:
                        with k.xf(move(xs, 94.4, 0) @ orient((0, 0, 0), (s, 0, 0))):
                            k.lathe([(0, 1.2), (0.2, 1.2), (0.2, 0.9), (0.1, 0.9), (0.1, 0)], 'grey', seg=16, sharp=30)
        # six pressurized elevator spokes (round tubes; the old ones were square beams) with drum-shaped lobbies
        for q in range(6):
            a = q * TAU / 6 + TAU / 12
            with k.xf(rotX(a)):
                k.tube((0, rs + 1.0, 0), (0, rIn + 0.6, 0), 2.5, 'white_m', seg=28)
                y = rs + 8.0
                while y < rIn - 7:
                    ycan(y, y + 0.5, 2.72, 'grey', seg=28, ch=0.08)
                    y += 8.6
                ycan(rs + 0.6, rs + 5.0, 3.9, 'grey', seg=28, ch=0.6)
                ycan(rIn - 5.6, rIn + 0.5, 4.7, 'white_s', seg=32, ch=0.9)
                ycan(rIn - 6.1, rIn - 5.6, 3.5, 'grey', seg=28, ch=0.1)
                ycan(51.0, 53.0, 2.95, 'grey', seg=28, ch=0.15)
                for zz in (2.7, -2.7):
                    k.tube((0.6, rs + 5.0, zz), (0.6, rIn - 5.8, zz), 0.18, 'gold' if zz > 0 else 'silver', seg=6)
                k.tube((2.65, rs + 5.0, 0), (2.65, rIn - 5.8, 0), 0.06, 'yellow', seg=4)
                for i in range(4):
                    k.box((4.72 if i % 2 else -4.72, rIn - 2.6, 1.6 if i < 2 else -1.6), (0.12, 1.1, 1.4), 'glass')
            for sgn in (1, -1):          # diagonal stays from the spoke roots to the outer hoops
                for dx in (1, -1):
                    k.tube(P(dx * 5.5, rs + 1.6, a), P(dx * pitch, rIn + 0.5, a + sgn * TAU / 12), 0.1, 'truss', seg=5)
        k.torus(0, 52.0, 0.3, 'truss', seg=120, tseg=6)
        # stay rods to the outer hoops, like a bicycle wheel, with a brace ring half way out
        for s in (1, -1):
            xh, xr = s * 27.5, s * pitch
            for i in range(18):
                a = i * TAU / 18 + TAU / 36
                k.tube(P(xh, rs + 1.4, a), P(xr, rIn + 0.3, a), 0.14, 'truss', seg=5)
                k.tube(P(xh, rs + 1.4, a), P(xr, rIn + 0.3, a + TAU / 18), 0.07, 'truss', seg=4)
                with k.xf(rotX(a)):
                    k.box((xr, rIn + 0.1, 0), (1.6, 0.8, 1.6), 'grey', bevel=0.08)
            k.torus(xh + (xr - xh) * (52 - rs - 1.4) / (rIn + 0.3 - rs - 1.4), 52.0, 0.22, 'truss', seg=120, tseg=5)
        # the turning sleeve on the hub
        k.lathe([(-29.7, rs - 1.5), (-29.7, rs), (29.7, rs), (29.7, rs - 1.5)], 'white_s', seg=96)
        for s in (1, -1):
            k.lathe(rrect(s * 27.5 - 0.9, s * 27.5 + 0.9, rs - 0.1, rs + 1.7, 0.3, 0.3, 2), 'grey', seg=96, closed=True)
        k.lathe(rrect(-4.6, 4.6, rs - 0.1, rs + 0.9, 0.3, 0.3, 2), 'grey', seg=96, closed=True)
        greeble(-26, -5, rs, 22, hmax=0.6)
        greeble(5, 26, rs, 22, hmax=0.6)


def build_drum(which):
    Dr = D['drumA' if which == 'A' else 'drumF']
    C.spin_group('RingA' if which == 'A' else 'RingF', Dr['x'], Dr['g'], Dr['r'], Dr['dir'])
    drum(Dr['x'], 0.0 if which == 'A' else TAU / 24)
    k.group = 'Hull'


# =====================================================================================
# forward hub: lander propellant plant, drop ship berths
# =====================================================================================
def build_berths(landers):
    hr = D['hubR']
    B = D['berth']
    BX, ROOF, H = B['x'], B['roof'], 6.6
    for x in (283, 304.5, 327.5):
        k.lathe(rrect(x - 0.7, x + 0.7, hr - 0.05, hr + 0.75, 0.15, 0.15, 2), 'grey', seg=96, closed=True)
    k.lathe(rrect(299.5, 301.5, hr - 0.02, hr + 0.1, 0.03, 0.03, 1), 'blue', seg=96, closed=True)
    greeble(285, 303, hr, 30, a0=0.2, span=PI - 0.4, hmax=0.6)
    greeble(328.5, 332.5, hr, 22, hmax=0.5)
    # --- lander propellant plant: a process drum slung under the hub (the old one was a flat-sided box)
    zc = -(hr + 6.4)
    with k.xf(move(0, 0, zc)):
        k.dome_tank(283.5, 298.5, 5.4, 'white', k=0.5, seg=48, rings=7)
        for x in (286.5, 291, 295.5):
            k.lathe(rrect(x - 0.22, x + 0.22, 5.38, 5.72, 0.06, 0.06, 1), 'grey', seg=48, closed=True)
        k.lathe(rrect(288.2, 289.4, 5.38, 5.5, 0.03, 0.03, 1), 'orange', seg=48, closed=True)
        greeble(285.5, 296.5, 5.4, 16, a0=PI + 0.3, span=PI - 0.6, hmax=0.5)
        hatch(293.2, -0.5, 5.4, 0.8)
        hrail(286, 296, 5.4, -0.2)
    for x in (286.5, 295.5):
        k.box((x, 0, -(hr + 0.6)), (1.4, 8.0, 1.6), 'grey', bevel=0.1)
        for s in (1, -1):
            k.tube((x, s * 9, -(hr - 3.5)), (x, s * 4.4, zc + 2.6), 0.24, 'truss', seg=6)
    k.box((291, 0, zc - 9.2), (11.0, 10.0, 0.25), 'radw')
    for dx in (-4.5, 4.5):
        for dy in (-4, 4):
            k.tube((291 + dx, dy, zc - 9.1), (291 + dx * 0.8, dy * 0.7, zc - 4.6), 0.1, 'truss', seg=5)
    for i in range(4):
        with k.xf(orient((285.5 + i * 3.6, 5.6, zc - 0.5), (0, 1, 0))):
            k.can(0, 2.2, 1.1, 'steel', seg=14)
    # dewars: liquid hydrogen in gold foil, liquid oxygen in silver, clear of the hub on cradles
    for (ang, rad, mat) in ((PI / 4, 4.6, 'gold'), (3 * PI / 4, 3.8, 'silver')):
        rc = hr + rad + 1.3
        c = P(291, rc, ang)
        k.sphere(c, rad, mat, seg=40, rings=20)
        with k.xf(move(*c)):
            k.torus(0, rad + 0.05, 0.14, 'grey', seg=36, tseg=5)
            with k.xf(rotZ(PI / 2)):
                k.torus(0, rad + 0.05, 0.14, 'grey', seg=36, tseg=5)
        for dx in (-2.4, 2.4):
            for da in (-0.14, 0.14):
                k.tube(P(291 + dx * 1.3, hr, ang + da), P(291 + dx * 0.6, rc - rad * 0.8, ang + da * 0.6), 0.16, 'truss', seg=6)
        k.pipe([P(291, rc + rad - 0.2, ang), P(296, rc + rad - 1.0, ang + 0.2), P(300, hr + 0.6, ang + 0.3), P(BX - 12, hr + 0.6, ang + 0.3)], 0.2, mat, seg=6)
    k.pipe([(297.5, 2.0, zc + 4.0), (300, 2.0, -(hr + 0.7)), (BX - 12, 2.0, -(hr + 0.5))], 0.22, 'silver', seg=6)
    k.torus(BX - 12, hr + 0.6, 0.22, 'silver', seg=64, tseg=6)
    # --- six radial berths; the landers hang roof-in on clamp arms with a crew tunnel and umbilicals
    for q in range(B['n']):
        a = (q + 0.5) * TAU / B['n']
        with k.xf(rotX(a)):
            for dx in (-4.0, 4.0):
                k.tube((BX + dx, hr, 2.2), (BX + dx * 0.8, ROOF - 0.3, 1.2), 0.3, 'grey', seg=8)
                k.tube((BX + dx, hr, -2.2), (BX + dx * 0.8, ROOF - 0.3, -1.2), 0.3, 'grey', seg=8)
                k.box((BX + dx * 0.8, ROOF - 0.15, 0), (1.2, 0.5, 3.4), 'hazard', bevel=0.08)
                k.tube((BX + dx, hr + 0.2, 2.2), (BX - dx, hr + 0.2, -2.2), 0.1, 'truss', seg=5)
            ycan(hr, ROOF - 0.6, 1.1, 'white_s', x=BX, seg=20)
            ycan(ROOF - 0.9, ROOF - 0.3, 1.45, 'grey', x=BX, seg=20)
            k.pipe([(BX + 2.6, hr, 0.6), (BX + 2.6, 21, 0.6), (BX + 1.6, ROOF - 0.5, 0.6)], 0.14, 'gold', seg=6)
            k.pipe([(BX - 2.6, hr, -0.6), (BX - 2.6, 21, -0.6), (BX - 1.6, ROOF - 0.5, -0.6)], 0.14, 'silver', seg=6)
            k.pipe([(BX - 12, hr + 0.6, 0.9), (BX - 2.6, hr + 0.5, -0.6)], 0.14, 'silver', seg=6)
            k.box((BX, hr + 0.4, 0), (10, 0.8, 6), 'grey', bevel=0.1)
            hrail(BX - 9, BX + 9, hr, 0.26)
        landers.append((BX, a, ROOF + H))
    floodlight((BX - 10.5, 0, hr + 2.0), (BX + 4, 14, 30), energy=9000, spot=1.3)
    floodlight((BX - 10.5, -3, -(hr + 2.0)), (BX + 4, -18, -30), energy=9000, spot=1.3)


# =====================================================================================
# bow: docking node, command section, antenna, Whipple shield
# =====================================================================================
def build_bow():
    # docking node: four radial ports, two with lifeboats
    k.lathe([(337, 13.0), (344, 13.0)], 'white', seg=80)
    for x in (337.4, 343.6):
        k.lathe(rrect(x - 0.3, x + 0.3, 12.95, 13.4, 0.08, 0.08, 1), 'grey', seg=80, closed=True)
    for q in range(4):
        with k.xf(rotX(q * TAU / 4) @ orient((340.5, 12.6, 0), (0, 1, 0))):
            dock_port(lifeboat=(q % 2 == 0))
    # command section
    k.lathe([(344, 13.0), (391.6, 13.0), (392.0, 12.7), (398.0, 9.3), (398.6, 8.5), (398.6, 0)], 'white_m', seg=96, sharp=30)
    k.lathe(rrect(350, 352, 12.98, 13.1, 0.03, 0.03, 1), 'blue', seg=96, closed=True)
    for x in (344.5, 356, 368, 380, 391.3):
        k.lathe(rrect(x - 0.22, x + 0.22, 12.95, 13.25, 0.05, 0.05, 1), 'grey', seg=96, closed=True)
    nw = 30
    for i in range(nw):          # bridge windows
        a = i * TAU / nw
        with k.xf(on(385, a, 13.0)):
            k.box((0, 0.03, 0), (2.0, 0.1, 2.1), 'grey')
            k.box((0, 0.07, 0), (1.5, 0.08, 1.65), 'glass' if R.random() < 0.55 else 'glass_dark')
    for i in range(36):          # crew deck ports
        if i % 6 == 5:
            continue
        with k.xf(on(374.5, i * TAU / 36, 13.0)):
            k.box((0, 0.03, 0), (1.2, 0.1, 1.2), 'grey')
            k.box((0, 0.06, 0), (0.8, 0.08, 0.8), 'glass' if R.random() < 0.45 else 'glass_dark')
    greeble(345, 349.5, 13, 16, hmax=0.6)
    greeble(352.6, 367.5, 13, 46, hmax=0.6)
    greeble(387, 391, 13, 16, hmax=0.6)
    for a in (0.9, 2.3, 4.0, 5.5):
        hrail(353, 367, 13, a)
        hrail(376.5, 383, 13, a + 0.3)
    for a in (1.3, 3.3, 5.0):
        hatch(364, a, 13, 0.9)
    for a in (0.25, 3.4):
        axial_pipe(344.6, 391, 13, a + 2.2, 0.28, 'gold')
    # the name along both flanks
    for ac in (0.42, PI - 0.42):
        axial_decal('name', NAME, 355.5, 372.5, 13.06, ac, 4.6, up=1 if math.cos(ac) > 0 else -1, seg=8)
    # high-gain antenna on a lattice mast with a two-axis gimbal
    girder((372, 0, 12.8), (372, 0, 26.6), 1.6, bay=1.6, rc=0.11, rd=0.05)
    k.box((372, 0, 27.2), (2.2, 2.2, 1.2), 'grey', bevel=0.1)
    with k.xf(orient((372, 0, 28.4), (1, 0, 0.55))):
        k.can(-0.8, 0.3, 1.1, 'gold', seg=16)
        with k.xf(move(1.3, 0, 0)):
            dish(8.0, 6.4)
    # laser comm terminal: a telescope in a yoke
    k.box((359, 0, 13.4), (4.4, 3.6, 1.0), 'grey', bevel=0.1)
    for s in (1, -1):
        k.box((359, s * 1.75, 15.2), (1.4, 0.4, 3.0), 'white_s', bevel=0.08)
    with k.xf(orient((356.6, 0, 15.9), (1, 0, 0.12))):
        k.can(0, 5.2, 1.3, 'white_s', seg=24, c1=False)
        k.lathe([(5.0, 1.18), (5.0, 0)], 'glass_dark', seg=24)
        k.lathe(rrect(4.7, 5.25, 1.28, 1.42, 0.03, 0.03, 1), 'grey', seg=24, closed=True)
        k.can(-0.6, 0.0, 1.0, 'gold', seg=16)
    # low-gain antennas and star trackers
    for a in (2.3, 4.2):
        with k.xf(on(378, a, 13)):
            k.tube((0, 0, 0), (0, 3.4, 0), 0.07, 'grey', seg=5)
            with k.xf(orient((0, 3.4, 0), (0, 1, 0))):
                k.lathe([(0, 0.05), (0.9, 0.45)], 'white_s', seg=12)
    for a in (0.3, 1.9, 3.5, 5.1):
        with k.xf(on(395, a, 11.0) @ orient((0, 0, 0), (0.5, 1, 0))):
            k.can(0, 0.9, 0.3, 'dark', seg=10)
    # forward RCS
    for q in range(4):
        with k.xf(rotX(q * TAU / 4 + TAU / 8)):
            k.tube((388.5, 12.8, 0), (388.5, 15.6, 0), 0.34, 'truss', seg=8)
            with k.xf(move(388.5, 17.2, 0) @ Matrix.Scale(1.3, 4)):
                rcs_quad()
    # Whipple shield: two layers of spaced bumper petals on a spider frame
    k.can(398.6, 402.6, 5.0, 'grey', seg=32, ch=0.3)
    for i in range(8):
        a = i * TAU / 8 + TAU / 16
        k.tube(P(398.0, 9.0, a), P(408.1, 15.6, a), 0.2, 'truss', seg=6)
        k.tube(P(402.4, 4.6, a), P(408.1, 8.0, a), 0.14, 'truss', seg=5)
        k.tube(P(408.1, 15.6, a), P(408.1, 15.6, a + TAU / 8), 0.14, 'truss', seg=5)
        k.tube(P(408.1, 8.0, a), P(408.1, 8.0, a + TAU / 8), 0.12, 'truss', seg=5)
    for (r0, r1, n) in [(0.8, 4.6, 8), (4.75, 8.7, 16), (8.85, 12.8, 24), (12.95, 17.0, 32)]:
        if lo():
            n //= 2
        for i in range(n):
            a0 = (i + 0.012) * TAU / n
            a1 = (i + 0.988) * TAU / n
            arc_plate(408.3, 408.46, r0, r1, a0, a1, 'bumper', seg=3)
            arc_plate(409.5, 409.64, r0 + 0.04, r1 - 0.04, a0 + 0.004, a1 - 0.004, 'bumper', seg=3)
    for i in range(24):
        a = i * TAU / 24
        for rr in (3.5, 8.0, 12.2, 16.3):
            k.tube(P(408.4, rr, a), P(409.5, rr, a), 0.06, 'grey', seg=4)
    k.can(407.6, 410.0, 0.6, 'grey', seg=12)
    k.box((409.85, 0, 0), (0.3, 0.5, 0.5), 'navW')
    C.LIGHTS.append(('POINT', (411.2, 0, 0), None, 1500, (1, 1, 1), 0))
    for s in (1, -1):
        k.box((408.2, s * 17.5, 0), (1.2, 0.9, 0.9), 'navR' if s > 0 else 'navG')
        C.LIGHTS.append(('POINT', (408.2, s * 18.6, 0), None, 2000, (1, 0.05, 0.03) if s > 0 else (0.05, 1, 0.2), 0))


# =====================================================================================
GROUPS = {'aft': ['drive', 'htr'], 'mid': ['spinea', 'vault', 'spinef', 'ltr'], 'fwd': ['hub', 'druma', 'drumf', 'berth', 'bow']}


def build(parts, drive, landers):
    want = set()
    for p in parts:
        want.update(GROUPS.get(p, [p]))
    if 'drive' in want: build_drive(drive)
    if 'htr' in want: build_htr()
    if 'spinea' in want: build_spine_aft()
    if 'vault' in want: build_vault()
    if 'spinef' in want: build_spine_fwd()
    if 'ltr' in want: build_ltr()
    if 'hub' in want: build_hub()
    if 'druma' in want: build_drum('A')
    if 'drumf' in want: build_drum('F')
    if 'berth' in want: build_berths(landers)
    if 'bow' in want: build_bow()


SHOTS = {
    'hero':   dict(tgt=(10, 0, -6), cam=(0.62, -0.72, 0.30), dist=1500, lens=70, roll=-7, sun=(0.62, 0.55, 0.56), eoff=(-9, -20), alt=9500),
    'drums':  dict(tgt=(222, 0, 6), cam=(0.72, -0.60, 0.34), dist=520, lens=50, roll=5, sun=(0.55, 0.60, 0.58), eoff=(-12, -27), alt=9500, elon=120),
    'stern':  dict(tgt=(-290, 0, 0), cam=(-0.80, -0.52, 0.30), dist=560, lens=55, roll=4, sun=(-0.70, 0.45, 0.55), eoff=(10, -25), alt=9500),
    'vault':  dict(tgt=(-70, 0, 4), cam=(0.30, -0.84, 0.45), dist=330, lens=45, roll=-3, sun=(0.50, 0.65, 0.57), eoff=(8, -25), alt=9500, elon=165),
    'berths': dict(tgt=(350, 0, 0), cam=(0.80, -0.52, 0.30), dist=260, lens=45, roll=6, sun=(-0.45, 0.62, 0.64), eoff=(-14, -24), alt=9500, elon=185),
    'side':   dict(tgt=(0, 0, 0), cam=(0.0, -1.0, 0.06), dist=2100, lens=100, roll=0, sun=(0.50, 0.62, 0.60), eoff=None, alt=0),
}


if __name__ == '__main__':
    C.main(build=build, shots=SHOTS, ship_id='horizon')
