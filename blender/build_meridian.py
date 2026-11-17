"""Meridian-class diplomatic and science flagship (560 m): high-detail model, path-traced stills and the game model.

Built on the Cochrane pipeline (blender/prewarp_ships.md). Layout, sizes and spin rates follow the class page:
x toward the bow, y to port, z dorsal; metres; the origin is amidships on the long axis.

  /home/user/.bpyvenv/bin/python blender/build_meridian.py --shot hero --out blender/previews/meridian --tmp <dir> --earth-tex <bmng.jpg>
  --shot hero|wheel|stern|bow|side   --drive (reactor at full power)   --only aft,tank,mid,fwd   --game game/models/ships/meridian.glb

What this rebuild changed against the first model (kept: every position, radius and spin rate):
  * the hydroponics drum is a round-cornered pressure torus four decks deep on lattice webs, not a slab with 28 m flat end walls;
  * the wheel's two reception pavilions are curved glazed tubes with domed ends, not rows of flat boxes;
  * the ventral shuttle bay is an unpressurised barrel hangar with clamshell doors, not a pressurised box;
  * both radiator sets are panels in frames on booms with manifolds and stay rods, not solid swept slabs;
  * the dome's Whipple shutters are tapered gores that fold in a Z beside the collar (a gore as long as the dome's
    meridian cannot lie flat on a 15 m collar in one piece);
  * the flywheel is a levitated rim rotor in a vacuum torus on tube struts; the sensor booms are lattice.
Left out: the wheel's floor windows (dark panes scattered over the rim read as missing tiles).
"""
import sys, os, math
import numpy as np
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_cochrane as C
import cochrane_kit as K
from build_cochrane import k, on, P, R, rotX, rotY, rotZ, move, orient, TAU, PI
from build_cochrane import (ycan, zcan, gbox, greeble, hrail, axial_pipe, hatch, rcs_quad, dock_port, dish, floodlight,
                            girder, rrect, arc_plate, arc_decal, flat_decal, GM)

NAME, REG = 'MERIDIAN', 'UE-01   UNITED EARTH'

D = dict(
    wheel=dict(x=80, r=102, rIn=94, w=26, g=1.0, dir=1, n=6),
    sci=dict(x=150, r=65, rIn=58, w=14, g=0.5, dir=-1, n=4),
    hydro=dict(x=10, r=45, rIn=16.5, w=30, g=0.4, dir=-1),
    fly=dict(x=50, r=30),
    shield=dict(x=-222, R=19.5),
    htr=dict(x0=-216, len=102, root=6.2, w=[8, 14, 19, 24, 28, 31, 33, 34, 33, 28, 1]),
    ltr=dict(x0=-92, len=66, root=6.0, w=[6, 12, 18, 24, 30, 35, 39, 41, 40, 30, 1]),
    tanks=dict(n=4, d=14.5, r=6.5, x0=-84, len=40),
    dome=dict(x=266, r=14),
)
HP = [(-36, 6.0), (-30, 10.5), (-14, 14.5), (34, 14.5), (46, 16.0), (116, 16.0), (130, 15.0), (168, 15.0), (178, 13.6), (252, 11.6)]


def hr(x):
    """Hull radius at x."""
    for (xa, ra), (xb, rb) in zip(HP, HP[1:]):
        if x <= xb:
            return ra + (rb - ra) * max(0.0, (x - xa) / (xb - xa))
    return HP[-1][1]


def lo():
    return k.lod < 1


# ------------------------------------------------------------------ materials of this ship
def _vglass():
    """Clear glazing you can see through: thin glass, no refraction, the sun and lamps pass as transparent shadows."""
    m = bpy.data.materials.new('vglass')
    m.use_nodes = True
    t = m.node_tree
    for n in list(t.nodes):
        t.nodes.remove(n)
    out = t.nodes.new('ShaderNodeOutputMaterial')
    tr = t.nodes.new('ShaderNodeBsdfTransparent')
    tr.inputs[0].default_value = (0.80, 0.88, 0.92, 1)
    gl = t.nodes.new('ShaderNodeBsdfGlossy')
    gl.inputs['Roughness'].default_value = 0.03
    fr = t.nodes.new('ShaderNodeFresnel')
    fr.inputs[0].default_value = 1.5
    fac = K._math(t, 'ADD', K._math(t, 'MULTIPLY', fr.outputs[0], 1.2), 0.03, clamp=True)
    mix = t.nodes.new('ShaderNodeMixShader')
    t.links.new(fac, mix.inputs[0]); t.links.new(tr.outputs[0], mix.inputs[1]); t.links.new(gl.outputs[0], mix.inputs[2])
    t.links.new(mix.outputs[0], out.inputs[0])
    return m


def _hydro():
    """Greenhouse glazing: panes over lit crop racks."""
    m = K.panel('hydro', (0.05, 0.13, 0.055), (0.02, 0.06, 0.03), size=(1.3, 2.6), seam=0.07, seam_mul=0.3, rough=0.1, wob=0.004,
                grime=0.05, coat=1.0)
    t = m.node_tree
    b = next(n for n in t.nodes if n.type == 'BSDF_PRINCIPLED')
    br = next(n for n in t.nodes if n.type == 'TEX_BRICK')
    t.links.new(br.outputs[0], b.inputs['Emission Color'])
    b.inputs['Emission Strength'].default_value = 0.9
    return m


C.EXTRA['vglass'] = (_vglass, ('sim', (0.02, 0.03, 0.05), 0.0, 0.08))
C.EXTRA['hydro'] = (_hydro, ('emit', (0.12, 0.75, 0.18), 0.35))
C.EXTRA['wglass'] = (lambda: K.emit('wglass', (1.0, 0.8, 0.55), 0.18, base=(0.015, 0.02, 0.03)), ('emit', (1.0, 0.82, 0.55), 1.0))
C.EXTRA['floor'] = (lambda: K.panel('floor', (0.42, 0.27, 0.15), (0.34, 0.21, 0.12), size=(2.4, 0.3), seam=0.01, rough=0.45, wob=0.002, grime=0.05),
                    ('sim', (0.40, 0.26, 0.14), 0.0, 0.5))
C.EXTRA['plant'] = (lambda: K.plain('plant', (0.03, 0.11, 0.03), rough=0.8, bump=0.08, grime=0.6), ('sim', (0.03, 0.11, 0.03), 0.0, 0.8))


# ------------------------------------------------------------------ helpers
def hon(x, a, lift=0.0):
    return on(x, a, hr(x) + lift)


def tgreeble(x0, x1, n, a0=0.0, span=TAU, hmax=0.7, lift=0.0):
    """Equipment boxes on the tapered hull; the livery stripes along both sides stay clear."""
    if lo():
        n //= 2
    for _ in range(n):
        x = x0 + R.random() * (x1 - x0)
        a = a0 + R.random() * span
        t = R.random()
        if abs(math.sin(a)) < 0.16:
            continue
        r = hr(x) + lift
        if t < 0.75:
            gbox(x, a, r, 0.6 + R.random() * 2.6, 0.5 + R.random() * 1.8, 0.15 + R.random() * hmax, R.choice(GM))
        elif t < 0.9:
            L, rad = 1.0 + R.random() * 2.2, 0.25 + R.random() * 0.4
            with k.xf(on(x, a, r + rad * 0.85)):
                k.can(-L / 2, L / 2, rad, R.choice(['silver', 'gold', 'white_s', 'grey']), ch=0.06, seg=12)
        else:
            rad = 0.45 + R.random() * 0.5
            with k.xf(on(x, a, r)):
                k.sphere((0, rad * 0.9, 0), rad, R.choice(['silver', 'gold']), seg=14, rings=8)
                k.box((0, 0.1, 0), (rad * 1.5, 0.2, rad * 1.5), 'grey')


def band(x0, x1, r, mat, h=0.12, seg=96):
    k.lathe(rrect(x0, x1, r - 0.03, r + h, 0.03, 0.03, 1), mat, seg=seg, closed=True)


def frame(x, r, w=0.15, h=0.16, mat='grey', seg=96):
    if lo():
        k.lathe([(x - w, r - 0.05), (x - w, r + h), (x + w, r + h), (x + w, r - 0.05)], mat, seg=seg)
        return
    k.lathe(rrect(x - w, x + w, r - 0.05, r + h, 0.06, 0.06, 1), mat, seg=seg, closed=True)


def bearing(x, r):
    """Fixed bearing race beside a rotor."""
    if lo():
        k.lathe([(x - 1.0, r - 0.1), (x - 0.9, r + 1.5), (x + 0.9, r + 1.5), (x + 1.0, r - 0.1)], 'grey', seg=96)
        return
    k.lathe(rrect(x - 1.0, x + 1.0, r - 0.1, r + 1.5, 0.3, 0.3, 2), 'grey', seg=96, closed=True)
    k.lathe(rrect(x - 0.35, x + 0.35, r + 1.4, r + 1.75, 0.08, 0.08, 1), 'dark', seg=96, closed=True)
    for j in range(24):
        gbox(x, j * TAU / 24, r + 1.5, 0.9, 1.0, 0.35, 'grey')


def win_row(x0, x1, a, n, lit=0.55, w=1.3, h=0.9, lift=0.0):
    for i in range(n):
        x = x0 + (x1 - x0) * (i + 0.5) / n
        with k.xf(hon(x, a, lift)):
            k.box((0, 0.02, 0), (w + 0.3, 0.08, h + 0.3), 'grey')
            k.box((0, 0.05, 0), (w, 0.06, h), 'glass' if R.random() < lit else 'glass_dark')


def clip(poly, xa, ya, xb, yb, under=True):
    """Clip a convex (x, y) polygon by the line through (xa, ya), (xb, yb): keep the part under (or over) it."""
    s = 1.0 if under else -1.0

    def d(p):
        return s * (p[1] - (ya + (yb - ya) * (p[0] - xa) / (xb - xa)))
    out = []
    for i in range(len(poly)):
        p, q = poly[i], poly[(i + 1) % len(poly)]
        dp, dq = d(p), d(q)
        if dp <= 1e-9:
            out.append(p)
        if (dp <= 1e-9) != (dq <= 1e-9):
            t = dp / (dp - dq)
            out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    return out


def plate(pts, t, mat, uvf=None):
    """Thin plate in the local plane z = 0 from a convex (x, y) outline."""
    n = len(pts)
    v = [(p[0], p[1], -t) for p in pts] + [(p[0], p[1], t) for p in pts]
    uv = uvf or (lambda p: (p[0], p[1]))
    f = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    uvs = [[uv(pts[i]) for i in range(n - 1, -1, -1)], [uv(p) for p in pts]]
    for i in range(n):
        j = (i + 1) % n
        f.append((i, j, n + j, n + i))
        uvs.append([uv(pts[i]), uv(pts[j]), uv(pts[j]), uv(pts[i])])
    k.add(mat, v, f, uvs)


def fin(F, mat, frame_mat, row, t, gap=0.16, floor=None):
    """Swept radiator fin in the local plane z = 0, growing toward +y: panels in a frame of ribs, stringers and an edge boom.
    floor(x): lowest y the panels may reach (the hull the fin runs into)."""
    w, n = F['w'], len(F['w']) - 1
    dx, root, x0 = F['len'] / n, F['root'], F['x0']

    def wid(x):
        u = min(max((x - x0) / dx, 0.0), n - 1e-9)
        i = int(u)
        return w[i] + (w[i + 1] - w[i]) * (u - i)
    hot = mat == 'hot'
    uvf = (lambda p: (p[0], min(1.0, max(0.0, (p[1] - root) / max(wid(p[0]), 1.0))))) if hot else None
    for i in range(n):
        xa, xb = x0 + i * dx, x0 + (i + 1) * dx
        ya, yb = root + w[i], root + w[i + 1]
        fa, fb = (floor(xa), floor(xb)) if floor else (root, root)
        j = 0
        while root + j * row < max(ya, yb):
            y0, y1 = root + j * row + gap, root + (j + 1) * row - gap
            poly = clip([(xa + gap, y0), (xb - gap, y0), (xb - gap, y1), (xa + gap, y1)], xa, ya - gap, xb, yb - gap)
            if floor and len(poly) >= 3:
                poly = clip(poly, xa, fa + gap, xb, fb + gap, under=False)
            if len(poly) >= 3:
                ar = 0.5 * abs(sum(poly[q][0] * poly[(q + 1) % len(poly)][1] - poly[(q + 1) % len(poly)][0] * poly[q][1] for q in range(len(poly))))
                if ar > 1.5:
                    plate(poly, t, mat, uvf)
            # stringer along this row line, as far as the fin reaches
            ys = root + j * row
            if j and ys < max(ya, yb) - 0.3 and ys > max(fa, fb):
                xs0, xs1 = xa, xb
                if ys > ya:
                    xs0 = xa + (ys - ya) / (yb - ya) * dx
                if ys > yb:
                    xs1 = xa + (ys - ya) / (yb - ya) * dx
                k.tube((xs0, ys, 0), (xs1, ys, 0), 0.11, frame_mat, seg=5)
            j += 1
        # rib at the joint, edge boom
        if max(fa, root) < ya - 0.5:
            k.box((xa, (max(fa, root) + ya) / 2, 0), (0.24, ya - max(fa, root), 0.5), frame_mat)
        k.tube((xa, ya + 0.15, 0), (xb, yb + 0.15, 0), 0.24, frame_mat, seg=6)
        k.sphere((xa, ya + 0.15, 0), 0.34, frame_mat, seg=8, rings=4)
    return wid


def gore(x0, x1, r, h0, h1, ac, mat, t=0.14, n=4):
    """Curved shutter panel on radius r: angular half-width h0 at x0 and h1 at x1, centred on angle ac."""
    va = []
    for (x, h) in ((x0, h0), (x1, h1)):
        for j in range(n + 1):
            a = ac - h + 2 * h * j / n
            va.append((x, r, a)); va.append((x, r + t, a))
    A = lambda i, j, l: (i * (n + 1) + j) * 2 + l
    f = []
    for j in range(n):
        f += [(A(0, j, 1), A(0, j + 1, 1), A(1, j + 1, 1), A(1, j, 1)), (A(0, j, 0), A(1, j, 0), A(1, j + 1, 0), A(0, j + 1, 0)),
              (A(0, j, 0), A(0, j + 1, 0), A(0, j + 1, 1), A(0, j, 1)), (A(1, j, 0), A(1, j, 1), A(1, j + 1, 1), A(1, j + 1, 0))]
    f += [(A(0, 0, 0), A(0, 0, 1), A(1, 0, 1), A(1, 0, 0)), (A(0, n, 0), A(1, n, 0), A(1, n, 1), A(0, n, 1))]
    v = [P(*q) for q in va]
    uvs = [[(va[i][0], va[i][2] * r + va[i][1]) for i in ff] for ff in f]
    k.add(mat, v, f, uvs)
    if mat == 'bumper':          # edge frame and two ribs
        for sg in (-1, 1):
            k.tube(P(x0, r + t, ac + sg * h0), P(x1, r + t, ac + sg * h1), 0.09, 'grey', seg=5)
        for u in (0.0, 0.5, 1.0):
            x, h = x0 + (x1 - x0) * u, h0 + (h1 - h0) * u
            for j in range(n):
                k.tube(P(x, r + t, ac - h + 2 * h * j / n), P(x, r + t, ac - h + 2 * h * (j + 1) / n), 0.08, 'grey', seg=4)


def side_decal(name, text, x0, x1, ac, hh, lift=0.1, r=None):
    """Lettering along the hull side: a flat quad tangent to the hull at angle ac (0 = port, pi = starboard), 2*hh tall."""
    port = math.cos(ac) > 0
    pts = []
    for x, s in ((x0, -1), (x1, -1), (x1, 1), (x0, 1)):
        rr = (r or hr(x)) + lift
        # tangent direction with +z upward
        ty, tz = -math.sin(ac), math.cos(ac)
        if tz < 0:
            ty, tz = -ty, -tz
        pts.append((x, rr * math.cos(ac) + s * hh * ty, rr * math.sin(ac) + s * hh * tz))
    if port:
        pts = [pts[1], pts[0], pts[3], pts[2]]
    flat_decal(name, text, *pts)


def roof_prof(xw, rIn, rim, sag, co, n=6):
    """Closed (x, r) outline of a ring hull: flat rim with rounded corners, straight side walls, roof arched toward the hub."""
    if lo():
        n = 2
    pts = []
    for cx, cr, t0 in ((xw - co, rim - co, 0), (-xw + co, rim - co, 90)):
        for i in range(n + 1):
            t = math.radians(t0 + 90 * i / n)
            pts.append((cx + co * math.cos(t), cr + co * math.sin(t)))
    for i in range(2 * n + 1):
        u = -1 + i / n
        pts.append((xw * u, rIn + sag * u * u))
    return pts


# =====================================================================================
# aft: magnetic nozzle, mirror reactor, converter, shadow shield, hot radiators
# =====================================================================================
def build_aft(drive):
    # --- magnetic nozzle: four superconducting coils in cryostats on a strut cage
    coils = [(-279, 18), (-275, 14.6), (-271, 11.8), (-267.5, 9.6)]
    for i, (x, r) in enumerate(coils):
        w = 1.25 - i * 0.12
        k.lathe(rrect(x - w, x + w, r - w, r + w, 0.35, 0.35, 3), 'steel', seg=72, closed=True, sharp=30)
        k.lathe(rrect(x - w * 0.5, x + w * 0.5, r - w - 0.12, r + w + 0.12, 0.2, 0.2, 2), 'gold', seg=72, closed=True, sharp=30)
        for j in range(12):
            with k.xf(on(x, (j + 0.5) * TAU / 12, r + w)):
                k.box((0, 0.2, 0), (w * 2.3, 0.5, 1.0), 'grey', bevel=0.06)
    for i in range(12):
        a, a2 = i * TAU / 12, (i + 1) * TAU / 12
        k.tube(P(-279, 19.6, a), P(-262.6, 9.6, a), 0.3, 'truss', seg=8)
        for (xa, ra), (xb, rb) in zip(coils, coils[1:]):
            p, q = (a, a2) if i % 2 else (a2, a)
            k.tube(P(xa, ra + 1.5, p), P(xb, rb + 1.4, q), 0.1, 'truss', seg=5)
    for a in (0.6, 2.7, 4.8):          # helium feed to the coils
        k.pipe([P(-278.5, 20.3, a), P(-267, 13.0, a), P(-262, 10.6, a), P(-237, 10.6, a)], 0.22, 'gold', seg=8)
    k.lathe([(-267.5, 3.0), (-265.5, 3.6), (-263.5, 5.0), (-262, 6.6)], 'bell', seg=48, sharp=60)
    k.lathe([(-267.4, 2.9), (-265.5, 3.5), (-262.2, 6.3)], 'black', seg=48, sharp=60)
    k.lathe([(-262.4, 6.3), (-262.4, 0)], 'plasma' if drive else 'black', seg=32)
    # --- mirror fusion reactor: vacuum vessel, solenoid coils, strong end mirrors, a service cage
    k.lathe([(-262, 0), (-262, 6.8), (-261.6, 7.2), (-236.4, 7.2), (-236, 6.8), (-236, 0)], 'vessel', seg=48)
    for i in range(6):
        x = -259.5 + i * 4.6 - (0.3 if i == 0 else 0) + (0.3 if i == 5 else 0)
        big = i in (0, 5)
        w, h = (1.5, 1.9) if big else (1.15, 1.3)
        k.lathe(rrect(x - w, x + w, 7.3, 7.3 + 2 * h, 0.3, 0.3, 3), 'white_s', seg=64, closed=True, sharp=30)
        k.lathe(rrect(x - w - 0.12, x - w + 0.25, 7.2, 7.3 + 2 * h + 0.15, 0.08, 0.08, 1), 'grey', seg=64, closed=True)
        k.lathe(rrect(x + w - 0.25, x + w + 0.12, 7.2, 7.3 + 2 * h + 0.15, 0.08, 0.08, 1), 'grey', seg=64, closed=True)
        for j in range(16):
            gbox(x, (j + 0.5) * TAU / 16, 7.3 + 2 * h, 0.5, 0.5, 0.2, 'dark')
    cage = 11.8
    cx = (-262.5, -253.7, -244.9, -236)
    for i in range(8):
        a = (i + 0.5) * TAU / 8
        k.tube(P(cx[0], cage, a), P(cx[-1], cage, a), 0.22, 'truss', seg=8)
        for x in cx:
            k.tube(P(x, cage, a), P(x, cage, a + TAU / 8), 0.14, 'truss', seg=5)
            k.tube(P(x, cage, a), P(x, 7.4, a), 0.12, 'truss', seg=5)
            k.sphere(P(x, cage, a), 0.34, 'grey', seg=8, rings=4)
        for j, x in enumerate(cx[:-1]):
            p, q = (a, a + TAU / 8) if (i + j) % 2 else (a + TAU / 8, a)
            k.tube(P(x, cage, p), P(cx[j + 1], cage, q), 0.07, 'truss', seg=4)
    for i in range(4):          # neutral-beam injectors, angled into the plasma
        a = i * TAU / 4 + 0.3
        with k.xf(rotX(a)):
            with k.xf(orient((-246.8, 7.2, 0), (-0.45, 1, 0))):
                k.can(0, 5.5, 1.0, 'steel', seg=20)
                k.can(1.0, 1.5, 1.25, 'grey', seg=20)
                k.box((6.6, 0, 0), (2.6, 2.6, 2.4), 'white_s', bevel=0.12)
                k.box((6.6, 0, 1.3), (1.8, 1.8, 0.3), 'gold')
            k.pipe([(-249.4, 14.2, 0.8), (-249.4, 12.6, 2.2), (-241, 12.2, 2.2), (-237, 10.4, 2.2)], 0.18, 'silver', seg=6)
    for a in (0.15, 1.2, 2.25, 3.3, 4.35, 5.4):
        axial_pipe(-261, -237, 11.8, a, 0.2, R.choice(['silver', 'gold', 'white_s']), clamp=4.3)
    for x in (-257, -248, -240):
        k.torus(x, 10.5, 0.2, 'silver', seg=48, tseg=6)
    # --- direct energy converter: drum of collector grids, power conditioning round it
    k.lathe([(-236, 0), (-236, 9.6), (-235.6, 10.0), (-228.4, 10.0), (-228, 9.6), (-228, 0)], 'white', seg=56)
    for x in (-235.2, -232.0, -228.8):
        frame(x, 10.0, 0.3, 0.5, seg=56)
    band(-234.2, -233.0, 10.0, 'blue', 0.08, seg=56)
    for i in range(10):
        a = i * TAU / 10
        gbox(-230.6, a, 10.0, 2.2, 3.2, 1.3, 'white_s', bevel=0.1)
        gbox(-230.6, a + TAU / 20, 10.0, 1.6, 1.4, 0.7, R.choice(['gold', 'grey', 'dark']))
        with k.xf(on(-230.6, a, 11.3)):
            k.box((0, 0.1, 0), (1.6, 0.12, 2.6), 'radw')
    # --- shadow shield: tungsten toward the reactor, lithium hydride behind, 39 m across
    SR = D['shield']['R']
    k.lathe([(-228.2, 0), (-228.2, 11.0), (-227.6, 12.0), (-226.2, 14.0), (-224.4, 17.6), (-223.6, 19.1), (-223.2, SR)], 'tungsten', seg=96, sharp=25)
    k.lathe([(-223.2, SR), (-222.4, SR), (-222.0, SR - 0.4), (-222.0, 0)], 'white', seg=96, sharp=40)
    k.torus(-222.8, SR, 0.45, 'grey', seg=96, tseg=8)
    for i in range(24):
        a = i * TAU / 24
        with k.xf(rotX(a)):
            k.box((-221.7, 12.6, 0), (0.6, 12.6, 0.35), 'grey')
        if i % 3 == 1:
            k.tube(P(-222, 18.0, a), P(-210, 5.6, a), 0.22, 'truss', seg=6)
    # --- thermal spine: a structural tube carrying the coolant mains to the fins
    k.lathe([(-222, 5.5), (-100, 5.5)], 'grey', seg=32)
    for x in np.arange(-218, -100, 8.5):
        k.lathe(rrect(x - 0.25, x + 0.25, 5.4, 6.0, 0.08, 0.08, 1), 'dark', seg=32, closed=True)
    for a in (0.0, PI / 2, PI, 3 * PI / 2):
        axial_pipe(-221, -101, 5.5, a, 0.36, 'silver' if a in (0.0, PI) else 'white_s', clamp=8.5)
        hrail(-213, -117, 5.5, a + 0.32)
    tgreeble_r(-212, -118, 5.5, 26)
    # --- high-temperature radiators: four swept fins, edge-on to the reactor, inside the shield's shadow
    H = D['htr']
    for q in range(4):
        with k.xf(rotX(TAU / 8 + q * TAU / 4)):
            for s in (1, -1):          # coolant headers out and back, glowing at the root
                k.tube((H['x0'] - 3, 6.1, s * 1.0), (H['x0'] + H['len'] - 6, 6.1, s * 1.0), 0.5, 'hotpipe', seg=10)
            wid = fin(H, 'hot', 'cc', 5.7, 0.14)
            for i in range(10):
                x = H['x0'] + i * 10.2
                k.tube((x, 5.9, 1.0), (x, 6.9, 0), 0.2, 'hotpipe', seg=6)
            for xs in (-195.6, -175.2, -154.8, -134.4, -124.2):       # stay rods keep the panels flat
                ws = wid(xs)
                for s in (1, -1):
                    k.tube((xs, 6.2 + ws, 0), (xs - 9 if xs > -200 else xs + 6, 5.7, s * 2.6), 0.07, 'cc', seg=4)
                    k.tube((xs, 6.2 + ws * 0.5, 0), (xs - 5, 5.7, s * 2.2), 0.06, 'cc', seg=4)
    # --- aft bulkhead and RCS
    k.lathe([(-100.4, 5.5), (-100, 7.1), (-99.6, 7.5), (-96.4, 7.5), (-96, 7.1), (-96, 0)], 'white', seg=48)
    band(-98.6, -97.4, 7.5, 'blue', 0.08, seg=48)
    for q in range(4):
        with k.xf(rotX(q * TAU / 4)):
            k.tube((-104, 5.5, 0), (-104, 11.0, 0), 0.35, 'truss', seg=8)
            k.tube((-108, 5.5, 0), (-104, 10.6, 0), 0.15, 'truss', seg=6)
            k.tube((-100.5, 5.8, 0), (-104, 10.6, 0), 0.15, 'truss', seg=6)
            with k.xf(move(-104, 12.3, 0)):
                rcs_quad()


def tgreeble_r(x0, x1, r, n):
    """Small boxes on a tube, kept off the fin roots at 45 degrees."""
    for _ in range(n if not lo() else n // 2):
        a = R.choice([0.0, PI / 2, PI, 3 * PI / 2]) + R.uniform(-0.42, 0.42)
        gbox(R.uniform(x0, x1), a, r, R.uniform(0.8, 2.6), R.uniform(0.6, 1.6), R.uniform(0.2, 0.7), R.choice(GM))


# =====================================================================================
# tank farm: fuel dewars, water tanks on an open truss, low-temperature radiators
# =====================================================================================
def build_tanks():
    T = D['tanks']
    # --- open truss with the utility core inside it
    with k.xf(Matrix.Scale(0.46, 4)):
        C.spine_truss(-96 / 0.46, -36 / 0.46, 10, 7)
    k.tube((-96, 0, 0), (-36, 0, 0), 2.2, 'grey', seg=20)
    for x in np.arange(-91.7, -36, 8.57):
        frame(x, 2.2, 0.2, 0.3, seg=20)
    k.tube((-96, 2.9, 2.9), (-34, 2.9, 2.9), 0.45, 'silver', seg=10)
    k.tube((-96, -2.9, -2.9), (-34, -2.9, -2.9), 0.32, 'gold', seg=8)
    k.tube((-96, -2.9, 2.9), (-34, -2.9, 2.9), 0.26, 'white_s', seg=8)
    nodes = [-96 + i * 60 / 7 for i in range(8)]
    # --- fusion fuel: four foil-wrapped dewars of deuterium and helium-3
    for q in range(4):
        a = TAU / 8 + q * TAU / 4
        c = P(-97, 10.5, a)
        k.sphere(c, 3.0, 'gold' if q % 2 == 0 else 'silver', seg=36, rings=18)
        with k.xf(move(*c)):
            k.torus(0, 3.04, 0.13, 'grey', seg=32, tseg=5)
            with k.xf(rotY(PI / 2)):
                k.torus(0, 3.04, 0.13, 'grey', seg=32, tseg=5)
        for da in (-0.28, 0.28):
            k.tube(P(-99, 7.4, a + da), P(-98.2, 8.6, a + da * 0.6), 0.14, 'truss', seg=6)
            k.tube(P(-95.6, 6.6, a + da), P(-96.2, 8.2, a + da * 0.6), 0.14, 'truss', seg=6)
        k.pipe([P(-97, 13.5, a), P(-94.5, 13.9, a), P(-93, 9.0, a), P(-93, 3.2, a)], 0.14, 'gold', seg=6)
    # --- water reaction mass: four tanks on cradle rings saddled to the truss longerons
    for q in range(T['n']):
        a = TAU / 8 + q * TAU / T['n']
        y, z = T['d'] * math.cos(a), T['d'] * math.sin(a)
        k.dome_tank(T['x0'] - 2, T['x0'] + T['len'] + 2, T['r'], 'tank', k=0.55, seg=44, rings=8, oy=y, oz=z)
        for x in (-80, -72, -66, -60, -48):
            k.lathe(rrect(x - 0.22, x + 0.22, T['r'] - 0.02, T['r'] + 0.1, 0.03, 0.03, 1), 'grey', seg=44, closed=True, oy=y, oz=z)
        for x in (nodes[2], nodes[5]):
            k.lathe(rrect(x - 0.6, x + 0.6, T['r'], T['r'] + 0.4, 0.1, 0.1, 1), 'grey', seg=44, closed=True, oy=y, oz=z)
            for da in (-0.75, 0.75):
                k.tube(P(x, 6.5, a), (x, y + (T['r'] + 0.3) * math.cos(a + PI + da), z + (T['r'] + 0.3) * math.sin(a + PI + da)), 0.26, 'truss', seg=6)
                k.tube(P(x, 6.5, a + (PI / 2 if da > 0 else -PI / 2)), (x, y + (T['r'] + 0.3) * math.cos(a + PI + da * 1.9), z + (T['r'] + 0.3) * math.sin(a + PI + da * 1.9)), 0.18, 'truss', seg=6)
        with k.xf(rotX(a)):
            k.pipe([(-42.8, T['d'], 0), (-39.5, T['d'], 0), (-38, 7.6, 0)], 0.36, 'silver', seg=8)      # feed to the drive mains
            k.pipe([(-85.2, T['d'], 0), (-88, T['d'], 0), (-90, 6.0, 0)], 0.2, 'silver', seg=6)
            k.box((-39.5, T['d'] + 0.6, 0), (1.4, 1.0, 1.4), 'grey', bevel=0.06)
            hrail(-80, -48, T['d'] + T['r'], 0.0)
            hatch(-64, 0.0, T['d'] + T['r'], 0.6)
            k.box((-70, T['d'] + T['r'] + 0.06, 1.2), (5.0, 0.1, 0.8), 'blue')
        with k.xf(move(0, y, z) @ rotX(a)):
            greeble(-82, -46, T['r'], 5, a0=-0.5, span=1.0, hmax=0.5)
    k.torus(-38, 7.6, 0.45, 'silver', seg=48, tseg=8)
    k.torus(-90, 6.0, 0.28, 'silver', seg=48, tseg=6)
    # --- low-temperature radiators: four swept fins between the tanks, white panels in frames on root booms
    L = D['ltr']
    for q in range(4):
        with k.xf(rotX(q * TAU / 4)):
            wid = fin(L, 'radw', 'grey', 5.1, 0.07, gap=0.14, floor=lambda x: max(L['root'], hr(x) + 0.5) if x > -36 else L['root'])
            k.tube((-93, 5.3, 0.5), (-36, 5.3, 0.5), 0.34, 'silver', seg=10)          # supply and return manifolds
            k.tube((-93, 5.3, -0.5), (-36, 5.3, -0.5), 0.34, 'white_s', seg=10)
            for x in (-88, -62, -42):
                with k.xf(orient((x, 4.4, 0), (0, 1, 0))):
                    k.can(0, 1.5, 1.0, 'grey', seg=16)
            for i in range(1, 10):          # loop pipes up each rib
                x = L['x0'] + i * 6.6
                k.tube((x + 0.25, 5.5, 0.2), (x + 0.25, 6.0 + L['w'][i] - 0.5, 0.2), 0.1, 'silver', seg=5)
            for xs in (-72.2, -52.4, -39.2):          # stay rods to the truss
                ws = wid(xs)
                for s in (1, -1):
                    k.tube((xs, 6.0 + ws, 0), (xs - 10, 4.6, s * 4.6), 0.07, 'truss', seg=4)
                    k.tube((xs, 6.0 + ws * 0.55, 0), (xs - 6, 4.6, s * 4.6), 0.06, 'truss', seg=4)
            if q % 2 == 0:
                x = L['x0'] + L['len'] * 0.7
                r = L['root'] + L['w'][7]
                k.tube((x, r, 0), (x, r + 1.2, 0), 0.1, 'grey', seg=5)
                k.box((x, r + 1.5, 0), (1.0, 0.8, 0.8), 'navR' if q == 0 else 'navG')
                C.LIGHTS.append(('POINT', (x, (r + 2.6) * (1 if q == 0 else -1), 0), None, 2500, (1, 0.05, 0.03) if q == 0 else (0.05, 1, 0.2), 0))


# =====================================================================================
# spin rings: one builder for the main wheel and the science ring
# =====================================================================================
def gring(S, phase, nmod, rh, sr, hubw, name=None, dname='name', pano=0, sag=1.6, lit=0.6, skip=None, reg=True):
    """Spin ring: one continuous pressure hull with an arched roof, bumper tiles on the rim, spokes and stays.
    pano: number of panoramic window banks on each side wall (0 = two rows of small ports).
    skip(side, angle) -> True where a side wall carries something else."""
    xc, rIn, rOut, w, nsp = S['x'], S['rIn'], S['r'], S['w'], S['n']
    rim = rOut - 0.5
    xw = w / 2 - 0.5
    seg = 288 if rOut > 80 else 192
    with k.xf(move(xc, 0, 0) @ rotX(phase)):
        k.lathe(roof_prof(xw, rIn, rim, sag, 1.0), 'white_m', seg=seg, closed=True, sharp=25)
        for i in range(nmod):          # ring frames
            a = i * TAU / nmod
            da = 0.32 / rOut
            k.lathe(roof_prof(xw + 0.16, rIn - 0.16, rim + 0.1, sag, 1.1, n=3), 'grey', seg=1, a0=a - da, a1=a + da, closed=True, caps=True)
        # --- rim bumper: spaced plates over the water jacket, livery band, name
        ntile = int(TAU * rOut / (2.5 if not lo() else 6.0))
        nrow = max(4, round((2 * xw - 1.4) / 2.7))
        if lo():
            nrow = max(3, (nrow + 1) // 2)
        rw = (2 * xw - 1.4) / nrow
        mid = (nrow // 2 - 1, nrow // 2) if nrow % 2 == 0 else (nrow // 2,)
        blue = set()
        cs = (0.0, TAU / 3, 2 * TAU / 3)
        ha = 0.0
        if name:
            hh = 2.9 if w > 20 else 2.0
            ha = (0.5 * len(name) + 0.3) * 2 * hh * 1.25 / 2 / rOut
            for c in cs:
                i0 = int((c + ha + 0.05) / TAU * ntile)
                blue.update(range(i0, i0 + int(ntile * 0.09)))
        for i in range(ntile):
            a0 = (i + 0.035) * TAU / ntile
            a1 = (i + 0.965) * TAU / ntile
            for j in range(nrow):
                if R.random() < 0.012:
                    continue
                xa = -xw + 0.7 + j * rw + 0.05
                mat = 'tile' if R.random() > 0.035 else 'tile2'
                if j in mid and i in blue:
                    mat = 'blue'
                arc_plate(xa, xa + rw - 0.1, rOut - 0.1, rOut, a0, a1, mat, vref=rOut)
        for j in range(nrow + 1 if not lo() else 0):
            k.torus(-xw + 0.7 + j * rw, rOut - 0.28, 0.1, 'grey', seg=seg, tseg=4)
        if name:
            for c in cs:
                arc_decal(dname, name, -hh, hh, rOut + 0.03, c - ha, c + ha, seg=40)
                if reg:
                    hr_ = (0.5 * len(REG) + 0.3) * 2.0 * 1.2 / 2 / rOut
                    arc_decal('reg', REG, -hh - 3.4, -hh - 1.4, rOut + 0.03, c - hr_, c + hr_, seg=30)
        # --- side walls
        wall0, wall1 = rIn + sag + 0.3, rim - 1.2
        for s in (1, -1):
            xs = s * (xw + 0.02)
            if pano:
                rc = (wall0 + wall1) / 2
                hgt = wall1 - wall0 - 0.3
                wd = TAU * rc / pano * 0.66
                for i in range(pano):
                    a = (i + 0.5) * TAU / pano
                    with k.xf(rotX(a)):
                        k.box((xs, rc, TAU * rc / pano / 2), (0.22, wall1 - wall0 + 1.6, 0.3), 'white_s')
                        if skip and skip(s, a):
                            continue
                        k.box((xs + s * 0.03, rc, 0), (0.14, hgt + 0.5, wd + 0.5), 'grey')
                        nc, nr = (4, 2) if not lo() else (2, 1)
                        room = R.random() < lit          # a whole room is lit or dark
                        for u in range(nc):
                            for v in range(nr):
                                k.box((xs + s * 0.08, rc - hgt / 2 + hgt * (v + 0.5) / nr, -wd / 2 + wd * (u + 0.5) / nc),
                                      (0.1, hgt / nr - 0.22, wd / nc - 0.22), 'wglass' if room and R.random() < 0.9 else 'glass_dark')
            else:
                for i in range(nmod * 4):
                    a = (i + 0.5) * TAU / (nmod * 4)
                    with k.xf(rotX(a)):
                        k.box((xs, (wall0 + wall1) / 2, 0), (0.16, wall1 - wall0 + 0.8, 0.22), 'white_s')
                for (rw_, lt) in ((rim - 2.2, lit), (rim - 4.6, lit * 0.8)):
                    nw = int(TAU * rw_ / 3.4)
                    for i in range(nw):
                        if (i % 6) in (4, 5) or R.random() < 0.06:
                            continue
                        with k.xf(rotX((i + 0.5) * TAU / nw)):
                            if not lo():
                                k.box((xs + s * 0.03, rw_, 0), (0.1, 1.2, 1.5), 'white_s')
                            k.box((xs + s * 0.06, rw_, 0), (0.08, 0.8, 1.1), 'glass' if R.random() < lt else 'glass_dark')
            for rr in (rim - 0.9, wall0 + 0.1):
                k.torus(xs + s * 0.45, rr, 0.045, 'yellow', seg=seg, tseg=4)
            for i in range(nmod * 2):          # wall-mounted gear between the windows
                a = (i + 0.25) * TAU / (nmod * 2)
                if skip and skip(s, a):
                    continue
                with k.xf(rotX(a)):
                    t = R.random()
                    if t < 0.45:
                        k.box((xs + s * 0.25, rim - 0.7 if pano else R.uniform(wall0 + 0.6, wall1 - 0.4), 0),
                              (0.5, R.uniform(0.5, 0.9) if pano else R.uniform(0.8, 2.0), R.uniform(0.8, 2.4)), R.choice(GM), bevel=0.05)
                    elif t < 0.55 and not pano:
                        with k.xf(move(xs, rim - 3.4, 0) @ orient((0, 0, 0), (s, 0, 0))):
                            k.lathe([(0, 1.2), (0.2, 1.2), (0.2, 0.9), (0.1, 0.9), (0.1, 0)], 'grey', seg=16, sharp=30)
        # --- hub-facing roof: skylights along the crown, conduits, equipment
        for i in range(nmod * 3):
            a0 = (i + 0.2) * TAU / (nmod * 3)
            a1 = (i + 0.8) * TAU / (nmod * 3)
            arc_plate(-1.5, 1.5, rIn - 0.1, rIn + 0.04, a0, a1, 'grey', seg=3)
            arc_plate(-1.25, 1.25, rIn - 0.14, rIn - 0.08, a0 + 0.004, a1 - 0.004, 'glass' if R.random() < (0.6 if pano else lit) else 'glass_dark', seg=3)
        for xx, mat in ((-0.34 * xw, 'silver'), (0.34 * xw, 'gold'), (-0.42 * xw, 'white_s'), (0.62 * xw, 'white_s')):
            k.torus(xx, rIn + sag * (xx / xw) ** 2 - 0.25, 0.2, mat, seg=seg, tseg=6)
        for i in range(nmod * 2):
            a = i * TAU / (nmod * 2) + 0.05
            xx = R.uniform(-0.75, 0.75) * xw
            with k.xf(rotX(a) @ move(xx, rIn + sag * (xx / xw) ** 2, 0) @ rotZ(PI)):
                k.box((0, 0.35, 0), (R.uniform(1, 2.6), 0.7, R.uniform(1, 2.2)), R.choice(GM), bevel=0.06)
        # --- elevator spokes: pressure tubes with round lobbies at both ends, and stays like a bicycle wheel
        for q in range(nsp):
            a = q * TAU / nsp + TAU / (2 * nsp)
            with k.xf(rotX(a)):
                k.tube((0, rh + 1.2, 0), (0, rIn + 0.4, 0), sr, 'white_m', seg=28)
                y = rh + 9
                while y < rIn - 8:
                    ycan(y, y + 0.5, sr + 0.22, 'grey', seg=28, ch=0.08)
                    y += 8.2
                ycan(rh + 1.0, rh + 4.6, sr + 1.2, 'grey', seg=28, ch=0.5)
                ycan(rIn - 5.2, rIn + 0.6, sr + 2.0, 'white_s', seg=32, ch=0.9)
                ycan(rIn - 5.7, rIn - 5.2, sr + 2.25, 'grey', seg=32, ch=0.1)
                for i in range(8):
                    with k.xf(move(0, rIn - 2.6, 0) @ rotY(i * TAU / 8 + 0.39)):
                        k.box((sr + 2.02, 0, 0), (0.1, 1.1, 1.3), 'glass' if R.random() < 0.6 else 'glass_dark')
                for zz in (sr + 0.2, -sr - 0.2):
                    k.tube((0.6, rh + 4.6, zz), (0.6, rIn - 5.6, zz), 0.18, 'gold' if zz > 0 else 'silver', seg=6)
                k.tube((sr + 0.1, rh + 4.6, 0), (sr + 0.1, rIn - 5.6, 0), 0.06, 'yellow', seg=4)
            for sgn in (1, -1):
                for dx in (1, -1):
                    k.tube(P(dx * hubw * 0.82, rh + 1.5, a), P(dx * (xw - 3.0), rIn + sag * 0.55, a + sgn * TAU / nsp / 2), 0.09, 'truss', seg=5)
                    k.tube(P(dx * hubw * 0.82, rh + 1.5, a + sgn * TAU / nsp / 2), P(dx * (xw - 3.0), rIn + sag * 0.55, a + sgn * TAU / nsp / 4), 0.07, 'truss', seg=4)
        # --- rotating collar on the hull, with the stay flanges
        k.lathe([(-hubw, rh + 0.2), (-hubw, rh + 1.2), (hubw, rh + 1.2), (hubw, rh + 0.2)], 'white_s', seg=96)
        for dx in (1, -1):
            k.lathe(rrect(dx * hubw * 0.82 - 0.3, dx * hubw * 0.82 + 0.3, rh + 1.1, rh + 1.9, 0.08, 0.08, 1), 'grey', seg=96, closed=True)
        greeble(-hubw + 0.5, hubw - 0.5, rh + 1.2, 22, hmax=0.6)
        return xw


def pavilion(xw, ac, half=0.194):
    """Reception pavilion on the wheel's forward wall: a curved pressure tube with domed ends, glazed forward and overhead.
    In section t = 0 looks forward, 90 is the ceiling (toward the hub), 270 the floor (toward the rim)."""
    rho, cr = 3.6, 97.9
    cx = xw + rho - 0.5
    a0, a1 = ac - half, ac + half
    seg = 30 if not lo() else 12

    def arc(t0, t1, n, dr=0.0):
        return [(cx + (rho + dr) * math.cos(math.radians(t0 + (t1 - t0) * i / n)), cr - (rho + dr) * math.sin(math.radians(t0 + (t1 - t0) * i / n))) for i in range(n + 1)]
    k.lathe(arc(112, 338, 12 if not lo() else 5), 'white_m', seg=seg, a0=a0, a1=a1, sharp=40)
    k.lathe(arc(-22, 112, 10 if not lo() else 4), 'vglass', seg=seg, a0=a0, a1=a1, sharp=60)
    for a in (a0, a1):
        k.sphere(P(cx, cr, a), rho, 'white_m', seg=24, rings=12)
    for t in (-22, 12, 46, 80, 112):          # mullions round the arc and down the section
        (px, pr), = arc(t, t, 1, 0.05)[:1]
        k.lathe(rrect(px - 0.09, px + 0.09, pr - 0.09, pr + 0.09, 0.02, 0.02, 1), 'grey', seg=seg, a0=a0, a1=a1, closed=True)
    nrib = 14 if not lo() else 6
    for i in range(nrib + 1):
        a = a0 + (a1 - a0) * i / nrib
        pts = arc(-22, 112, 6, 0.05)
        for (xa, ra), (xb, rb) in zip(pts, pts[1:]):
            k.tube(P(xa, ra, a), P(xb, rb, a), 0.08, 'grey', seg=4)
    k.lathe(arc(-44, -24, 2, 0.04), 'blue', seg=seg, a0=a0, a1=a1)
    arc_plate(xw - 0.1, xw + 0.5, cr - rho - 0.2, cr + rho + 0.1, a0 - 0.01, a1 + 0.01, 'white_s', seg=seg // 2)
    # the hall inside: deck, ceiling lamps, tables and planters
    k.lathe([(cx - 2.95, cr + 2.0), (cx + 2.95, cr + 2.0)], 'floor', seg=seg, a0=a0, a1=a1)
    if lo():
        return
    for i in range(12):
        a = a0 + (a1 - a0) * (i + 0.5) / 12
        with k.xf(rotX(a)):
            k.box((cx - 0.6, cr - rho + 0.5, 0), (0.3, 0.08, 0.16), 'lamp')
            if i % 3 == 1:
                ycan(cr + 1.2, cr + 2.0, 0.55, 'white_s', x=cx + R.uniform(-0.4, 1.4), seg=12)
            elif i % 3 == 2:
                k.box((cx - 2.0, cr + 1.7, 0), (1.0, 0.6, 2.2), 'grey')
                for dz in (-0.6, 0.6):
                    k.sphere((cx - 2.0, cr + 1.1, dz), 0.5, 'plant', seg=10, rings=6)
            k.tube((cx + 2.7, cr + 1.0, -1.5), (cx + 2.7, cr + 1.0, 1.5), 0.04, 'yellow', seg=4)
    C.LIGHTS.append(('POINT', tuple(k.M @ Vector(P(cx, cr - 1.5, ac))), None, 900, (1.0, 0.8, 0.55), 0))


def build_wheel():
    W = D['wheel']
    bearing(W['x'] - 9, 16.4)
    bearing(W['x'] + 9, 16.4)
    C.spin_group('RingW', W['x'], W['g'], W['r'], W['dir'])
    pav = (PI / 2, 3 * PI / 2)

    def skip(s, a):
        return s > 0 and any(abs(math.atan2(math.sin(a - p), math.cos(a - p))) < 0.235 for p in pav)
    xw = gring(W, 0.35, 24, 16.4, 2.25, 8.0, name=NAME, pano=48, sag=1.6, lit=0.4, skip=skip)
    with k.xf(move(W['x'], 0, 0) @ rotX(0.35)):
        for p in pav:
            pavilion(xw, p)
    k.group = 'Hull'


def build_sci():
    S = D['sci']
    bearing(S['x'] - 7, 15.3)
    bearing(S['x'] + 7, 15.3)
    C.spin_group('RingS', S['x'], S['g'], S['r'], S['dir'])
    gring(S, 0.6, 16, 15.2, 1.6, 6.0, name=REG, dname='reg', pano=0, sag=1.0, lit=0.4, reg=False)
    k.group = 'Hull'


# =====================================================================================
# hydroponics drum, flywheel, storm shelter, main hull (mid)
# =====================================================================================
def build_drum():
    H = D['hydro']
    xc, w = H['x'], H['w']
    bearing(xc - w / 2 - 1.0, 14.8)
    bearing(xc + w / 2 + 1.0, 14.8)
    C.spin_group('RingH', xc, H['g'], H['r'], H['dir'])
    xw, r0, rim = w / 2 - 0.5, 31.0, H['r'] - 0.5
    seg = 192
    nb = 12
    with k.xf(move(xc, 0, 0) @ rotX(0.2)):
        # pressure torus: four rack decks deep, corners rounded to carry the pressure
        k.lathe(rrect(-xw, xw, r0, rim, 3.6, 2.0, 6), 'white_m', seg=seg, closed=True, sharp=25)
        for i in range(nb):
            a = i * TAU / nb
            da = 0.3 / H['r']
            k.lathe(rrect(-xw - 0.16, xw + 0.16, r0 - 0.16, rim + 0.1, 3.7, 2.1, 3), 'grey', seg=1, a0=a - da, a1=a + da, closed=True, caps=True)
        # rim: bumper tiles with a glazed band over the lit racks
        ntile = int(TAU * H['r'] / (2.5 if not lo() else 6.0))
        nrow, glazed = (10, (2, 3, 6, 7)) if not lo() else (5, (1, 3))
        rw = (2 * xw - 3.0) / nrow
        for i in range(ntile):
            a0 = (i + 0.035) * TAU / ntile
            a1 = (i + 0.965) * TAU / ntile
            for j in range(nrow):
                if R.random() < 0.012:
                    continue
                xa = -xw + 1.5 + j * rw + 0.05
                mat = 'hydro' if j in glazed else ('tile' if R.random() > 0.035 else 'tile2')
                arc_plate(xa, xa + rw - 0.1, H['r'] - 0.1, H['r'], a0, a1, mat, vref=H['r'])
        for j in range(nrow + 1 if not lo() else 0):
            k.torus(-xw + 1.5 + j * rw, H['r'] - 0.28, 0.1, 'grey', seg=seg, tseg=4)
        # end walls: glazed bays between the frames, rails, gear
        for s in (1, -1):
            xs = s * (xw + 0.02)
            for i in range(nb):
                a = (i + 0.5) * TAU / nb
                with k.xf(rotX(a)):
                    k.box((xs + s * 0.03, 38.6, 0), (0.14, 6.4, 13.0), 'grey')
                    nc = 6 if not lo() else 2
                    for u in range(nc):
                        for v in range(2 if not lo() else 1):
                            hh = 5.8 / (2 if not lo() else 1)
                            k.box((xs + s * 0.08, 38.6 - 2.9 + hh * (v + 0.5), -6.2 + 12.4 * (u + 0.5) / nc), (0.1, hh - 0.22, 12.4 / nc - 0.22),
                                  'hydro' if R.random() < 0.9 else 'glass_dark')
                    k.box((xs + s * 0.3, 43.0, 4.0), (0.6, 0.9, R.uniform(1.5, 3.5)), R.choice(GM), bevel=0.05)
                with k.xf(rotX(a + TAU / nb / 2)):
                    k.box((xs + s * 0.25, R.uniform(35.5, 41.5), 0), (0.5, R.uniform(1.0, 2.4), R.uniform(0.8, 1.6)), R.choice(GM), bevel=0.05)
            for rr in (35.0, 42.3):
                k.torus(xs + s * 0.45, rr, 0.045, 'yellow', seg=seg, tseg=4)
        # roof: light pipes and services
        for xx, mat in ((-6.0, 'silver'), (6.0, 'gold'), (-8.5, 'white_s')):
            k.torus(xx, r0 - 0.25, 0.2, mat, seg=seg, tseg=6)
        for i in range(nb * 2):
            a = i * TAU / (nb * 2) + 0.05
            with k.xf(rotX(a) @ move(R.uniform(-9, 9), r0, 0) @ rotZ(PI)):
                k.box((0, 0.35, 0), (R.uniform(1, 2.6), 0.7, R.uniform(1, 2.2)), R.choice(GM), bevel=0.06)
        # webs: a bearing sleeve on the hull, lattice arms at both ends, six access tubes amidships
        k.lathe([(-xw - 0.5, 16.5), (-xw - 0.5, 17.6), (xw + 0.5, 17.6), (xw + 0.5, 16.5)], 'white_s', seg=96)
        for xx in (-xw + 1.5, 0.0, xw - 1.5):
            frame(xx, 17.6, 0.3, 0.6)
        greeble(-xw, xw, 17.6, 18, hmax=0.5)
        for i in range(nb):
            a = i * TAU / nb
            for s in (1, -1):
                girder(P(s * (xw - 2.2), 17.8, a), P(s * (xw - 2.2), r0 + 0.6, a), 1.8, bay=2.4, rc=0.14, rd=0.07)
                k.tube(P(s * (xw - 2.2), 18.2, a), P(s * (xw - 2.2), r0 + 0.2, a + TAU / nb), 0.07, 'truss', seg=4)
                k.tube(P(s * (xw - 2.2), 18.2, a + TAU / nb), P(s * (xw - 2.2), r0 + 0.2, a), 0.07, 'truss', seg=4)
            k.tube(P(-xw + 2.2, 18.0, a), P(0, r0 + 0.3, a + TAU / nb / 2), 0.08, 'truss', seg=4)
            k.tube(P(xw - 2.2, 18.0, a), P(0, r0 + 0.3, a + TAU / nb / 2), 0.08, 'truss', seg=4)
        for q in range(6):
            with k.xf(rotX(q * TAU / 6 + TAU / 24)):
                k.tube((0, 17.4, 0), (0, r0 + 0.5, 0), 1.3, 'white_m', seg=20)
                ycan(17.4, 19.6, 2.1, 'grey', seg=20, ch=0.4)
                ycan(r0 - 1.6, r0 + 0.4, 2.3, 'white_s', seg=20, ch=0.5)
                k.tube((0.5, 19.6, 1.45), (0.5, r0 - 1.6, 1.45), 0.14, 'gold', seg=6)
    k.group = 'Hull'


def build_flywheel():
    F = D['fly']
    x, r = F['x'], F['r']
    # vacuum torus round a levitated rim rotor: no spokes, the rotor never touches the casing
    k.lathe(rrect(x - 3, x + 3, r - 3, r + 3, 1.6, 1.6, 5), 'grey_p', seg=160, closed=True, sharp=30)
    k.lathe(rrect(x - 1.0, x + 1.0, r + 2.95, r + 3.12, 0.04, 0.04, 1), 'orange', seg=160, closed=True)
    for xx in (x - 3.05, x + 3.05):
        k.torus(xx, r, 0.16, 'silver', seg=160, tseg=6)
    for i in range(24):          # lift and drive magnet housings, cryocooler boxes
        a = i * TAU / 24
        with k.xf(on(x, a, r + 3.0)):
            k.box((0, 0.3, 0), (4.6, 0.7, 1.6), 'white_s' if i % 2 else 'dark', bevel=0.08)
        with k.xf(on(x, a + TAU / 48, r - 3.0) @ rotZ(PI)):
            k.box((0, 0.3, 0), (3.4, 0.6, 1.2), 'gold' if i % 3 == 0 else 'grey', bevel=0.06)
        da = 0.16 / r
        k.lathe(rrect(x - 3.14, x + 3.14, r - 3.14, r + 3.14, 1.7, 1.7, 3), 'grey', seg=1, a0=a - da, a1=a + da, closed=True, caps=True)
    for q in range(6):          # tube struts in A-frames from two hull frames
        a = q * TAU / 6 + TAU / 12
        for dx in (-5.0, 5.0):
            k.tube(P(x + dx, hr(x + dx) - 0.2, a), P(x + dx * 0.3, r - 2.6, a), 0.5, 'truss', seg=10)
            k.sphere(P(x + dx, hr(x + dx) + 0.2, a), 0.9, 'grey', seg=10, rings=5)
        for sg in (1, -1):
            k.tube(P(x, hr(x), a + sg * 0.2), P(x, r - 2.8, a + sg * TAU / 12), 0.12, 'truss', seg=5)
        k.pipe([P(x + 1.2, hr(x), a + 0.06), P(x + 1.2, r - 3.2, a + 0.06)], 0.16, 'gold', seg=6)
        hatch(x, a + TAU / 12, r + 3.0, 0.8)
    frame(x - 5, hr(x - 5), 0.5, 0.6)
    frame(x + 5, hr(x + 5), 0.5, 0.6)


def hull_shell(x0, x1):
    pts = [(x, r) for (x, r) in HP if x0 < x < x1]
    pts = [(x0, hr(x0))] + pts + [(x1, hr(x1))]
    k.lathe(pts, 'white', seg=96, sharp=30)


def stripes(spans):
    """Blue livery line along both sides of the hull."""
    for (xa, xb) in spans:
        xs = [xa] + [x for (x, _) in HP if xa < x < xb] + [xb]
        for ac in (0.0, PI):
            k.lathe([(x, hr(x) + 0.04) for x in xs], 'blue', seg=2, a0=ac - 0.06, a1=ac + 0.06)


def build_mid():
    # --- engineering cone and the hull back to the wheel
    k.lathe([(-36, 0), (-36, 6.0)], 'grey', seg=48)
    hull_shell(-36, 116)
    for x in (-35.6, -30, -22, -14, -9, 29, 34, 40, 46, 58, 64, 94, 98):
        frame(x, hr(x))
    tgreeble(-34, -15, 44, hmax=0.7)
    for a in (0.5, 2.1, 3.7, 5.3):
        with k.xf(rotX(a)):
            k.pipe([(-38, 7.6, 0), (-33, 9.2, 0), (-28, 12.4, 0), (-16, 15.4, 0), (-9, 15.4, 0)], 0.3, 'silver', seg=8)
    for a in (1.1, 4.3):
        hatch(-18, a, hr(-18) - 0.1, 1.0)
        with k.xf(hon(-24, a + 0.5)):
            k.box((0, 2.2, 0), (7, 0.2, 4.6), 'radw')
            k.tube((-2.5, 0, 0), (-2.5, 2.2, 0), 0.1, 'grey', seg=5)
            k.tube((2.5, 0, 0), (2.5, 2.2, 0), 0.1, 'grey', seg=5)
    hrail(-12.5, -8.5, 14.5, 1.2); hrail(-12.5, -8.5, 14.5, 4.4)
    tgreeble(28, 44, 26, hmax=0.6)
    win_row(29, 43, 0.5, 6); win_row(29, 43, PI - 0.5, 6); win_row(29, 43, PI + 0.5, 6, lit=0.4)
    for a in (0.7, 2.3, 3.9, 5.5):
        axial_pipe(56, 69.5, 16, a, 0.3, 'gold')
        axial_pipe(56, 69.5, 16, a + 0.1, 0.16, 'silver')
    tgreeble(56, 69, 18, hmax=0.5)
    for a in (1.3, 4.6):
        hrail(57, 69, 16, a)
    tgreeble(91, 98, 10, hmax=0.5)
    stripes([(-13, -8), (28, 69.5), (91, 98.5)])
    build_drum()
    build_flywheel()
    build_wheel()
    # --- storm shelter: a despun refuge inside a segmented water jacket
    k.lathe([(99.2, 16.0), (99.7, 16.7), (114.3, 16.7), (114.8, 16.0)], 'tank', seg=96, sharp=30)
    for x in (99.5, 104.3, 109.7, 114.5):
        frame(x, 16.7, 0.3, 0.32)
    for i in range(16):
        a = i * TAU / 16 + 0.1
        with k.xf(rotX(a)):
            k.box((107, 16.78, 0), (14.2, 0.16, 0.2), 'grey')
    for a in (0.9, 2.6, 4.2, 5.6):
        with k.xf(rotX(a)):
            k.pipe([(98.2, 16.3, 0), (98.2, 17.6, 0), (101, 17.4, 0)], 0.2, 'silver', seg=6)
            k.box((101.4, 17.1, 0), (1.2, 0.8, 1.0), 'grey', bevel=0.05)
    hatch(107, 1.7, 16.7, 1.0); hatch(107, 4.9, 16.7, 1.0)
    hrail(100.5, 113.5, 16.7, 1.2); hrail(100.5, 113.5, 16.7, 4.4)
    for ac in (0.0, PI):
        k.lathe([(100.2, 16.74), (113.8, 16.74)], 'blue', seg=2, a0=ac - 0.055, a1=ac + 0.055)
    for ac in (PI - 0.2, 0.2):
        side_decal('bay', 'STORM SHELTER', 102.8, 111.2, ac, 0.75, r=16.7)


# =====================================================================================
# forward: science ring, booms, telescope, shuttle bay, docking node, command deck, observatory
# =====================================================================================
def shuttle():
    """Twenty-metre lifting-body diplomatic shuttle: tail at the local origin, nose toward +x, +z up."""
    with k.xf(Matrix.Diagonal((1, 2.1, 1.6, 1))):
        k.lathe([(0, 0.0), (0, 0.78), (0.4, 0.93), (3, 1.0), (12, 1.0), (14.6, 0.9), (16.8, 0.68), (18.4, 0.38), (19.0, 0.16), (19.2, 0.0)], 'white', seg=28, sharp=50)
        k.lathe(rrect(6.0, 7.2, 1.0, 1.012, 0.004, 0.004, 1), 'blue', seg=28, closed=True)
        k.lathe([(0.02, 0.6), (-0.25, 0.5), (-0.25, 0)], 'grey', seg=20)
    for s in (1, -1):
        plate([(1.6, s * 1.9), (10.5, s * 1.9), (5.0, s * 6.4), (1.8, s * 6.4)], 0.13, 'white_s')
        with k.xf(move(0, 0, -0.75)):
            plate([(2.0, s * 1.9), (9.6, s * 1.9), (5.0, s * 6.0), (2.2, s * 6.0)], 0.03, 'tile2')
        k.box((3.3, s * 6.35, 0.75), (3.0, 0.16, 1.7), 'white_s')
        k.box((3.3, s * 6.38, 1.2), (2.4, 0.2, 0.5), 'blue')
        with k.xf(move(-0.2, s * 1.0, 0.1)):
            k.lathe([(0.6, 0.38), (0.0, 0.45), (-0.9, 0.7), (-1.4, 0.8)], 'bell', seg=16, sharp=60)
            k.lathe([(0.5, 0.36), (0.45, 0)], 'black', seg=12)
        k.box((15.6, s * 0.72, 1.12), (1.5, 0.9, 0.12), 'glass_dark')
        for i in range(4):
            k.box((6.2 + i * 1.9, s * 2.06, 0.5), (0.9, 0.1, 0.5), 'glass' if R.random() < 0.6 else 'glass_dark')
    k.box((1.6, 0, 2.2), (2.8, 0.2, 1.8), 'white_s')
    zcan(1.5, 1.85, 0.9, 'grey', x=9.0, seg=16)          # dorsal hatch
    k.box((12.5, 0, 1.62), (2.0, 1.2, 0.1), 'radw')


def build_fwd():
    hull_shell(116, 252)
    for x in (116, 123, 130, 138, 160.5, 168, 178, 186, 198, 210, 218, 233, 250):
        frame(x, hr(x))
    tgreeble(117, 140.5, 44, hmax=0.6)
    win_row(118, 129, 0.45, 5); win_row(118, 129, PI - 0.45, 5); win_row(118, 129, PI + 0.45, 5, lit=0.4)
    for a in (0.35, 2.0, 3.75, 5.1):
        axial_pipe(131, 141, 15, a, 0.3, 'gold')
        axial_pipe(159, 166.5, 15, a, 0.3, 'gold')
    for a in (1.2, 4.4):
        hrail(131, 141, 15, a)
    tgreeble(159, 176, 30, hmax=0.6)
    build_sci()
    stripes([(116.5, 141.5), (158.5, 219.5), (231.5, 251.5)])
    # --- sensor booms: lattice masts with instrument pallets, a radar dish and a magnetometer far out
    for s in (1, -1):
        girder((168, s * 14.6, 0), (168, s * 40, 0), 1.5, bay=1.6, rc=0.1, rd=0.05)
        with k.xf(orient((168, s * 14.4, 0), (0, s, 0))):
            k.can(0, 1.0, 1.5, 'grey', seg=20)
        k.box((168, s * 41.5, 0), (6, 3, 3), 'white_s', bevel=0.2)
        k.box((168, s * 41.5, 1.6), (4.4, 2.2, 0.25), 'gold')
        k.box((170.2, s * 41.5, -0.6), (1.8, 1.4, 1.4), 'dark', bevel=0.08)
        k.box((165.6, s * 41.5, 0.3), (1.2, 3.3, 0.4), 'radw')
        k.tube((168, s * 43, 0), (168, s * 49, 0), 0.09, 'grey', seg=5)
        k.box((168, s * 49.2, 0), (0.5, 0.5, 0.5), 'gold')
        k.box((168, s * 43.4, 0.9), (0.5, 0.5, 0.5), 'navW')
        C.LIGHTS.append(('POINT', (168, s * 44.2, 1.8), None, 1200, (1, 1, 1), 0))
        for yy in (22, 31):
            k.tube((168, s * yy, 0.8), (168 + 3, s * (yy + 1.5), 3.2), 0.07, 'grey', seg=4)
        with k.xf(orient((168, s * 30, 1.0), (0.3, 0, 1))):
            k.tube((0, 0, 0), (1.2, 0, 0), 0.16, 'grey', seg=6)
            with k.xf(move(1.4, 0, 0) @ Matrix.Scale(0.64, 4)):
                dish()
        with k.xf(orient((168, s * 24, -0.9), (0.2, 0, -1))):
            k.can(0, 1.6, 0.5, 'gold', seg=12)
            k.can(1.6, 1.8, 0.62, 'dark', seg=12)
    # --- survey telescope: a 6 m reflector in a fork on a turntable, dorsal, clear of the rotors
    tz = hr(190) + 7.5
    zcan(hr(190) - 0.3, hr(190) + 1.6, 4.6, 'grey', x=190, seg=36, ch=0.3)
    zcan(hr(190) + 1.6, hr(190) + 2.3, 4.2, 'white_s', x=190, seg=36, ch=0.15)
    k.box((190, 0, hr(190) + 2.8), (5.5, 9.4, 1.2), 'white_s', bevel=0.2)
    for s in (1, -1):
        k.extrudeX([(s * 3.5, hr(190) + 3.0), (s * 4.7, hr(190) + 3.0), (s * 4.5, tz + 1.0), (s * 3.7, tz + 1.0)], 188.2, 191.8, 'white_s', bevel=0.1)
        with k.xf(orient((190, s * 3.2, tz), (0, s, 0))):
            k.can(0, 1.9, 1.3, 'grey', seg=20)
            k.can(1.9, 2.2, 0.8, 'gold', seg=14)
    with k.xf(move(0, 0, tz)):
        k.lathe([(176.6, 0), (176.6, 2.6), (177.2, 3.0), (204, 3.0)], 'white_s', seg=48, sharp=40)
        k.lathe([(204, 3.0), (204.2, 3.3), (210, 3.3)], 'gold', seg=48, sharp=40)
        k.lathe([(210, 3.3), (210, 3.1), (182, 2.8), (182, 0)], 'black', seg=48, sharp=40)
        k.lathe(rrect(209.7, 210.1, 3.05, 3.42, 0.05, 0.05, 1), 'grey', seg=48, closed=True)
        for x in (180, 186.5, 193.5, 199):
            frame(x, 3.0, 0.16, 0.16, seg=48)
        band(195.2, 196.6, 3.0, 'blue', 0.05, seg=48)
        for i in range(4):          # secondary mirror on its spider
            a = i * TAU / 4 + TAU / 8
            k.tube(P(205.5, 3.2, a), P(205.5, 0.5, a), 0.06, 'dark', seg=4)
        k.can(204.8, 206.0, 0.7, 'dark', seg=16)
        k.can(173.6, 176.6, 1.9, 'gold', seg=24)          # instrument bay and its cooler
        k.box((175.2, 0, 2.4), (2.4, 2.8, 0.2), 'radw')
        k.box((175.2, 2.3, 0), (2.0, 0.6, 1.8), 'white_s', bevel=0.08)
        with k.xf(orient((210.0, 0, 3.45), (0.55, 0, 1))):          # aperture door, open
            k.can(0, 0.25, 3.3, 'white_s', seg=40, ch=0.08, oz=-3.3)
        for a in (0.9, 2.2):
            hrail(184, 200, 3.0, a)
    floodlight((178, 6, hr(178) + 1.2), (192, 0, tz), energy=5000, spot=1.1)
    # --- shuttle bay: an unpressurised barrel hangar under the hull, clamshell doors open, one shuttle lowered
    bz = -(hr(195) + 0.6)
    br = 9.0
    with k.xf(move(0, 0, bz)):
        open_a = 0.95          # half-angle of the door opening at the bottom
        a0, a1 = -PI / 2 + open_a, 3 * PI / 2 - open_a
        prof = [(176.0, br - 2.4), (176.8, br - 0.8), (178.2, br), (211.8, br), (213.2, br - 0.8), (214.0, br - 2.4)]
        k.lathe(prof, 'white', seg=40, a0=a0, a1=a1, sharp=40)
        k.lathe([(x, r - 0.25) for (x, r) in prof], 'grey_p', seg=40, a0=a0, a1=a1, sharp=40)
        k.lathe([(176.0, br - 2.4), (176.0, 0)], 'white_s', seg=40)
        k.lathe([(214.0, br - 2.4), (214.0, 0)], 'white_s', seg=40)
        k.lathe([(176.3, br - 2.6), (176.3, 0)], 'grey_p', seg=40)
        k.lathe([(213.7, br - 2.6), (213.7, 0)], 'grey_p', seg=40)
        for x in (182, 189, 195, 201, 208):
            k.lathe(rrect(x - 0.2, x + 0.2, br - 0.05, br + 0.3, 0.05, 0.05, 1), 'grey', seg=40, a0=a0, a1=a1, closed=True, caps=True)
            k.lathe(rrect(x - 0.15, x + 0.15, br - 0.9, br - 0.2, 0.05, 0.05, 1), 'truss', seg=40, a0=a0, a1=a1, closed=True, caps=True)
        for s in (1, -1):          # doors swung down and out on their hinges
            hy, hz = s * br * math.sin(open_a), -br * math.cos(open_a)
            with k.xf(move(0, hy, hz) @ rotX(s * 2.0) @ move(0, -hy, -hz)):
                aa = (-PI / 2, -PI / 2 + open_a) if s > 0 else (-PI / 2 - open_a, -PI / 2)
                k.lathe([(178.4, br + 0.02), (211.6, br + 0.02), (211.6, br - 0.22), (178.4, br - 0.22)], 'white', seg=10, a0=aa[0], a1=aa[1], closed=True, caps=True)
                for x in (184, 195, 206):
                    k.lathe(rrect(x - 0.2, x + 0.2, br - 0.6, br - 0.2, 0.05, 0.05, 1), 'hazard', seg=10, a0=aa[0], a1=aa[1], closed=True, caps=True)
            for x in (181, 195, 209):
                k.tube((x, hy * 1.12, hz + 1.6), (x, hy + s * 1.3, hz - 2.6), 0.14, 'steel', seg=6)
            for x in range(181, 212, 6):
                k.box((x, s * 8.2, -1.0), (1.4, 0.3, 0.4), 'lamp')
            k.boxX(178.2, 211.8, hy * 1.01, hz, 0.5, 0.5, 'hazard')
        # the second shuttle on its cradle inside, the first lowered on the cradle below the doors
        with k.xf(move(193.6, 0, -2.6)):
            shuttle()
        k.box((203.2, 0, -4.6), (15, 6.0, 0.4), 'grey_p', bevel=0.1)
        for dy in (-3.1, 3.1):
            k.box((203.2, dy, -4.6), (15.2, 0.3, 0.5), 'hazard')
        with k.xf(move(178.6, 0, -14.4)):
            shuttle()
            k.box((9.6, 0, 2.25), (11.5, 5.0, 0.4), 'grey_p', bevel=0.1)
            for dy in (-2.6, 2.6):
                k.box((9.6, dy, 2.25), (11.7, 0.3, 0.5), 'hazard')
            for dx in (5.0, 14.2):
                for dy in (-2.0, 2.0):
                    k.tube((dx, dy, 2.4), (dx, dy, 14.0), 0.2, 'steel', seg=8)
                    k.tube((dx, dy, 8.0), (dx, dy, 14.6), 0.32, 'grey', seg=8)
    for s in (1, -1):
        floodlight((196, s * 7.0, bz - 5.6), (188, 0, bz - 14), energy=9000, spot=1.3)
    # --- docking node: four radial ports (visiting craft to port, a shuttle to starboard, lifeboats above and below)
    nx = 225.5
    nr = hr(nx) + 1.0
    k.lathe([(219.6, hr(219.6)), (220.3, nr), (230.7, nr), (231.4, hr(231.4))], 'white', seg=96, sharp=30)
    band(222, 223, nr, 'blue', 0.08)
    frame(220.6, nr); frame(230.4, nr)
    greeble(223.6, 230, nr, 20, hmax=0.5)
    for q in range(4):
        with k.xf(rotX(q * TAU / 4) @ orient((nx, nr - 0.4, 0), (0, 1, 0))):
            dock_port(lifeboat=(q % 2 == 1))
            if q == 2:
                with k.xf(move(5.5 + 19.2, 0, 0) @ rotY(PI)):
                    shuttle()
            if q == 0:
                for i in range(4):
                    with k.xf(rotX(i * TAU / 4 + TAU / 8)):
                        k.box((5.4, 2.5, 0), (0.3, 0.3, 0.3), 'lamp')
    for a in (0.9, 2.2, 4.0, 5.4):
        hrail(221, 230, nr, a)
    hrail(213, 219, hr(216), 0.9); hrail(213, 219, hr(216), 2.2)
    tgreeble(214, 219, 14, hmax=0.4)
    floodlight((224, -9.5, 9.8), (232, -30, 0), energy=7000, spot=1.2)
    # --- lettering along the forward hull, both sides
    for ac in (PI - 0.27, 0.27):
        side_decal('name', NAME, 198.5, 211.5, ac, 1.35)
    for ac in (PI + 0.2, -0.2):
        side_decal('reg', REG, 199, 211, ac, 0.52)
    # --- command deck: two rows of bridge windows, the high-gain dish, laser terminal, forward RCS
    for (x, n_) in ((238.8, 26), (245.0, 26)):
        r = hr(x)
        k.lathe(rrect(x - 1.2, x + 1.2, r - 0.02, r + 0.1, 0.04, 0.04, 1), 'grey', seg=96, closed=True)
        for i in range(n_):
            with k.xf(on(x, i * TAU / n_, r + 0.1)):
                k.box((0, 0.04, 0), (1.5, 0.08, 2.0), 'glass' if R.random() < 0.6 else 'glass_dark')
    tgreeble(232, 237.4, 16, hmax=0.5); tgreeble(240.4, 243.6, 8, hmax=0.4); tgreeble(246.5, 251, 12, hmax=0.4)
    for a in (0.6, 2.5, 3.7, 5.4):
        hrail(232, 237, hr(234.5), a)
    girder((233, 0, hr(233) - 0.2), (233, 0, hr(233) + 12), 1.6, bay=1.6, rc=0.11, rd=0.05)
    k.box((233, 0, hr(233) + 12.5), (2.0, 2.0, 1.0), 'grey', bevel=0.1)
    with k.xf(orient((233, 0, hr(233) + 13.4), (1, 0, 0.5))):
        k.can(-0.6, 0.3, 1.1, 'gold', seg=16)
        with k.xf(move(1.2, 0, 0) @ Matrix.Scale(6.5 / 3.75, 4)):
            dish()
    for a in (2.2, 4.1):
        with k.xf(hon(241.8, a)):
            k.tube((0, 0, 0), (0, 3.2, 0), 0.07, 'grey', seg=5)
            with k.xf(orient((0, 3.2, 0), (0, 1, 0))):
                k.lathe([(0, 0.05), (0.9, 0.45)], 'white_s', seg=12)
    k.box((236, 0, -(hr(236) + 0.9)), (4, 2.4, 2.0), 'white_s', bevel=0.15)          # laser terminal on a gimbal
    with k.xf(orient((236, 0, -(hr(236) + 1.9)), (0.6, 0, -1))):
        k.can(0, 1.8, 1.1, 'gold', seg=18)
        k.can(1.8, 1.95, 0.95, 'glass_dark', seg=18, ch=0.03)
    for q in range(4):
        with k.xf(rotX(q * TAU / 4 + TAU / 8)):
            r = hr(248)
            k.tube((248, r - 0.2, 0), (248, r + 2.2, 0), 0.3, 'truss', seg=8)
            with k.xf(move(248, r + 3.5, 0)):
                rcs_quad()
    build_dome()


def build_dome():
    """Observatory at the bow: a glazed geodesic dome on a base ring, with the Whipple shutters folded beside the collar."""
    DM = D['dome']
    X, Rd = DM['x'], DM['r']
    k.lathe([(252, 11.6), (258, 13.4), (258.6, Rd), (X, Rd)], 'white', seg=96, sharp=30)
    band(261, 262, Rd, 'blue', 0.08)
    frame(258.6, Rd, 0.25, 0.3); frame(X - 0.3, Rd, 0.3, 0.45)
    n_ = 30
    for i in range(n_):          # gallery ports under the dome floor
        if i % 5 == 4:
            continue
        with k.xf(on(264.0, (i + 0.5) * TAU / n_, Rd)):
            k.box((0, 0.03, 0), (1.5, 0.08, 1.9), 'grey')
            k.box((0, 0.06, 0), (1.1, 0.08, 1.5), 'glass' if R.random() < 0.6 else 'glass_dark')
    # glazing: 24 meridians and four parallels of ribs carry the panes
    nb = 12 if not lo() else 5
    k.lathe([(X + Rd * math.sin(PI / 2 * j / nb), Rd * math.cos(PI / 2 * j / nb)) for j in range(nb + 1)], 'vglass', seg=96, sharp=80)
    nm = 24 if not lo() else 12
    rr = Rd + 0.06
    for i in range(nm):
        a = i * TAU / nm
        full = i % 2 == 0
        m_ = nb if full else nb - 3
        for j in range(m_):
            ta, tb = PI / 2 * j / nb, PI / 2 * (j + 1) / nb
            k.tube(P(X + rr * math.sin(ta), rr * math.cos(ta), a), P(X + rr * math.sin(tb), rr * math.cos(tb), a), 0.13 if full else 0.09, 'grey', seg=5)
    for j in (3, 6, 9):
        t = PI / 2 * j / 12
        k.torus(X + rr * math.sin(t), rr * math.cos(t), 0.11, 'grey', seg=96, tseg=5)
    k.can(X + Rd - 0.25, X + Rd + 0.35, 0.9, 'grey', seg=20, ch=0.1)
    k.box((X + Rd + 0.6, 0, 0), (0.4, 0.4, 0.4), 'navW')
    C.LIGHTS.append(('POINT', (X + Rd + 1.6, 0, 0), None, 1200, (1, 1, 1), 0))
    # inside: the deck faces forward; a visitors' telescope, seats, planters, lamps
    k.lathe([(X + 0.2, Rd - 0.1), (X + 0.2, 0)], 'floor', seg=64)
    k.lathe([(X + 0.2, 5.2), (X + 0.5, 5.2), (X + 0.5, 0)], 'white_s', seg=48)
    if not lo():
        k.can(X + 0.5, X + 2.2, 0.7, 'grey', seg=16)
        with k.xf(orient((X + 2.4, 0, 0), (1, 0.25, 0.35))):
            k.can(-1.6, 3.4, 0.55, 'white_s', seg=16)
            k.can(3.4, 3.7, 0.65, 'dark', seg=16)
        for i in range(12):
            a = i * TAU / 12
            with k.xf(rotX(a)):
                if i % 2 == 0:
                    k.box((X + 0.6, 10.4, 0), (0.8, 1.6, 3.6), 'grey', bevel=0.08)
                    for dz in (-1.1, 0, 1.1):
                        k.sphere((X + 1.25 + R.random() * 0.3, 10.4, dz), 0.62, 'plant', seg=10, rings=6)
                else:
                    k.box((X + 0.55, 8.0, 0), (0.7, 0.9, 2.6), 'white_s', bevel=0.08)
                k.tube((X + 0.2, 12.9, 0), (X + 3.0, 12.9, 0), 0.06, 'grey', seg=4)
                k.box((X + 3.1, 12.9, 0), (0.14, 0.16, 0.16), 'lamp')
        k.torus(X + 1.3, 12.2, 0.05, 'yellow', seg=64, tseg=4)
        C.LIGHTS.append(('POINT', (X + 5, 0, 0), None, 2500, (1.0, 0.82, 0.6), 0))
    # shutters: six tapered gores, each folded in a Z of four panels beside the collar
    Rp = Rd + 0.55
    hmax = TAU / 12 - 0.04
    lat = [0.0, 0.25, 0.5, 0.75, 1.0]
    wf = [math.cos(PI / 2 * t * 0.97) for t in lat]
    xh, pl = X - 0.3, 5.5
    for q in range(6):
        ac = q * TAU / 6
        gore(xh, xh - pl, Rp, hmax * wf[0], hmax * wf[1], ac, 'bumper')
        gore(xh - pl - 0.2, xh - 2 * pl, Rp, hmax * wf[1], hmax * wf[2], ac, 'bumper')
        gore(xh - 2 * pl + 0.1, xh - pl + 0.1, Rp + 0.5, hmax * wf[2], hmax * wf[3], ac, 'bumper')
        gore(xh - pl + 0.3, xh - 0.2, Rp + 0.5, hmax * wf[3], hmax * wf[4] + 0.01, ac, 'bumper')
        gore(xh - 2 * pl + 0.1, xh - 0.2, Rp + 0.36, hmax * wf[2] * 0.9, 0.02, ac, 'white_s', t=0.12)          # fabric layer between
        for da in (-hmax * 0.8, hmax * 0.8):          # hinge knuckles and the drive struts
            with k.xf(on(xh + 0.1, ac + da, Rd)):
                k.box((0, 0.5, 0), (0.7, 1.0, 0.5), 'hazard', bevel=0.05)
            k.tube(P(xh - 1.0, Rd + 0.2, ac + da * 0.6), P(xh - pl, Rp, ac + da * 0.6), 0.09, 'steel', seg=5)
        with k.xf(on(xh - 2 * pl, ac, Rp)):
            k.box((0, 0.35, 0), (0.6, 0.9, hmax * wf[2] * Rp * 1.6), 'grey')
        for x in (xh - 3.6, xh - 8.6):
            with k.xf(on(x, ac, 13.0)):
                k.tube((0, 0, -2.0), (0, Rp - 13.0, -2.0), 0.1, 'grey', seg=5)
                k.tube((0, 0, 2.0), (0, Rp - 13.0, 2.0), 0.1, 'grey', seg=5)


# =====================================================================================
def build(parts, drive, landers):
    if 'aft' in parts:
        build_aft(drive)
        if drive:
            C.PLUMES.append(dict(x=-262.0, length=300.0, r0=6.0, r1=44.0))
    if 'tank' in parts or 'mid' in parts:
        build_tanks()
    if 'mid' in parts:
        build_mid()
    if 'fwd' in parts:
        build_fwd()


SHOTS = {
    'hero':  dict(tgt=(10, 0, 0), cam=(0.64, -0.70, 0.32), dist=1010, lens=70, roll=-7, sun=(0.62, 0.55, 0.56), eoff=(-9, -20), alt=9500, elon=20),
    'wheel': dict(tgt=(92, -22, 62), cam=(0.60, -0.62, 0.50), dist=235, lens=50, roll=5, sun=(0.55, 0.60, 0.58), eoff=(-11, -21), alt=9500, elon=120),
    'stern': dict(tgt=(-150, 0, 0), cam=(-0.80, -0.52, 0.30), dist=640, lens=60, roll=4, sun=(-0.70, 0.45, 0.55), eoff=(12, -21), alt=9500, elon=200),
    'bow':   dict(tgt=(222, 0, 0), cam=(0.80, -0.50, 0.33), dist=215, lens=50, roll=-4, sun=(0.42, 0.55, 0.72), eoff=(-12, -18), alt=9500, elon=20),
    'side':  dict(tgt=(0, 0, 0), cam=(0.0, -1.0, 0.06), dist=1430, lens=100, roll=0, sun=(0.50, 0.62, 0.60), eoff=None, alt=0),
    # working views
    't_aft':  dict(tgt=(-200, 0, 0), cam=(-0.45, -0.75, 0.42), dist=250, lens=50, roll=0, sun=(-0.3, 0.6, 0.7), eoff=None, alt=0),
    't_tank': dict(tgt=(-70, 0, 0), cam=(-0.35, -0.8, 0.45), dist=190, lens=50, roll=0, sun=(0.3, 0.6, 0.7), eoff=None, alt=0),
    't_mid':  dict(tgt=(40, 0, 0), cam=(0.5, -0.75, 0.4), dist=330, lens=50, roll=0, sun=(0.4, 0.6, 0.65), eoff=None, alt=0),
    't_drum': dict(tgt=(20, 0, 10), cam=(-0.5, -0.75, 0.35), dist=210, lens=50, roll=0, sun=(0.4, 0.6, 0.65), eoff=None, alt=0),
    't_pav':  dict(tgt=(96, 0, 96), cam=(0.75, -0.55, 0.35), dist=90, lens=50, roll=0, sun=(0.4, 0.6, 0.65), eoff=None, alt=0),
    't_fwd':  dict(tgt=(200, 0, 0), cam=(0.35, -0.85, 0.35), dist=230, lens=50, roll=0, sun=(0.3, 0.6, 0.7), eoff=None, alt=0),
    't_bay':  dict(tgt=(196, 0, -22), cam=(0.3, -0.75, -0.55), dist=90, lens=50, roll=0, sun=(0.3, -0.5, 0.7), eoff=None, alt=0),
    't_dome': dict(tgt=(262, 0, 0), cam=(0.75, -0.55, 0.3), dist=80, lens=50, roll=0, sun=(0.4, 0.6, 0.65), eoff=None, alt=0),
    'top':   dict(tgt=(0, 0, 0), cam=(0.05, -0.2, 1.0), dist=1430, lens=100, roll=0, sun=(0.50, 0.62, 0.60), eoff=None, alt=0),
}


if __name__ == '__main__':
    C.main(build=build, shots=SHOTS, ship_id='meridian')
