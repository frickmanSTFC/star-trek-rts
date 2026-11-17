"""Geometry and material kit for build_cochrane.py (the Cochrane-class tender, path-traced stills).

Axes as on the class page: x toward the bow, y to port, z dorsal; metres.
Geometry is gathered into one mesh per (material, bevel, sharp angle) so the scene stays light.
Every primitive writes UVs in metres, so the panel materials tile at real size on any part.
"""
import bpy, bmesh, math, random
from mathutils import Matrix, Vector

TAU = math.tau
PI = math.pi


def rotX(a): return Matrix.Rotation(a, 4, 'X')
def rotY(a): return Matrix.Rotation(a, 4, 'Y')
def rotZ(a): return Matrix.Rotation(a, 4, 'Z')
def move(x, y=0.0, z=0.0): return Matrix.Translation((x, y, z))


def orient(o, d, up=None):
    """Frame with local +X along d, origin at o."""
    d = Vector(d).normalized()
    ref = Vector(up) if up is not None else (Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0)))
    u = ref.cross(d).normalized()
    w = d.cross(u)
    return Matrix(((d.x, u.x, w.x, o[0]), (d.y, u.y, w.y, o[1]), (d.z, u.z, w.z, o[2]), (0, 0, 0, 1)))


class _XF:
    def __init__(self, k, M): self.k, self.M = k, M
    def __enter__(self):
        self.old = self.k.M
        self.k.M = self.old @ self.M
    def __exit__(self, *a): self.k.M = self.old


class Kit:
    def __init__(self, seed=1):
        self.B = {}
        self.M = Matrix.Identity(4)
        self.rnd = random.Random(seed)

    def xf(self, M): return _XF(self, M)

    # ---- raw
    def add(self, mat, verts, faces, uvs, bevel=0.0, sharp=35.0):
        key = (mat, bevel, sharp)
        b = self.B.get(key)
        if b is None:
            b = self.B[key] = {'v': [], 'f': [], 'uv': []}
        M = self.M
        V = b['v']
        base = len(V)
        for p in verts:
            q = M @ Vector(p)
            V.append((q.x, q.y, q.z))
        F, U = b['f'], b['uv']
        for f, uv in zip(faces, uvs):
            F.append(tuple(base + i for i in f))
            for p in uv:
                U.append(p[0]); U.append(p[1])

    # ---- primitives
    def box(self, c, size, mat, bevel=0.0, sharp=35.0):
        cx, cy, cz = c
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        v = [(cx - hx, cy - hy, cz - hz), (cx + hx, cy - hy, cz - hz), (cx + hx, cy + hy, cz - hz), (cx - hx, cy + hy, cz - hz),
             (cx - hx, cy - hy, cz + hz), (cx + hx, cy - hy, cz + hz), (cx + hx, cy + hy, cz + hz), (cx - hx, cy + hy, cz + hz)]
        f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)]
        ax = [(0, 1), (0, 1), (0, 2), (0, 2), (1, 2), (1, 2)]
        ou, ov = self.rnd.random() * 60, self.rnd.random() * 60
        uvs = [[(v[i][a] + ou, v[i][b] + ov) for i in ff] for ff, (a, b) in zip(f, ax)]
        self.add(mat, v, f, uvs, bevel, sharp)

    def boxX(self, x0, x1, y, z, w, h, mat, bevel=0.0):
        self.box(((x0 + x1) / 2, y, z), (abs(x1 - x0), w, h), mat, bevel)

    def lathe(self, prof, mat, seg=32, a0=0.0, a1=TAU, oy=0.0, oz=0.0, closed=False, caps=False,
              vref=None, sharp=35.0, bevel=0.0, uvn=False):
        """Revolve (x, r) points about the local X axis. uvn: v runs 0..1 over the profile instead of metres."""
        full = abs((a1 - a0) - TAU) < 1e-9
        n = len(prof)
        m = seg if full else seg + 1
        rr = vref or max(p[1] for p in prof) or 1.0
        ni = n if closed else n - 1
        sl = [math.hypot(prof[(i + 1) % n][0] - prof[i][0], prof[(i + 1) % n][1] - prof[i][1]) for i in range(ni)]
        tot = sum(sl) or 1.0
        verts = []
        for (x, r) in prof:
            r = max(r, 1e-4)
            for j in range(m):
                a = a0 + (a1 - a0) * j / seg
                verts.append((x, oy + r * math.cos(a), oz + r * math.sin(a)))
        faces, uvs = [], []
        ou, ov = self.rnd.random() * 60, self.rnd.random() * 60
        s = 0.0
        for i in range(ni):
            i2 = (i + 1) % n
            u0, u1 = s, s + sl[i]
            s = u1
            if uvn:
                u0, u1 = u0 / tot, u1 / tot
            else:
                u0, u1 = u0 + ou, u1 + ou
            for j in range(seg):
                j2 = (j + 1) % m if full else j + 1
                faces.append((i * m + j, i * m + j2, i2 * m + j2, i2 * m + j))
                if uvn:
                    va, vb = j / seg, (j + 1) / seg
                else:
                    va = (a0 + (a1 - a0) * j / seg) * rr + ov
                    vb = (a0 + (a1 - a0) * (j + 1) / seg) * rr + ov
                uvs.append([(u0, va), (u0, vb), (u1, vb), (u1, va)])
        if caps and closed and not full:
            for j in (0, seg):
                idx = [i * m + j for i in range(n)]
                faces.append(tuple(idx))
                uvs.append([(prof[i][0] + ou, prof[i][1] + ov) for i in range(n)])
        self.add(mat, verts, faces, uvs, bevel, sharp)

    def cyl(self, x0, x1, r0, r1, mat, seg=32, oy=0.0, oz=0.0, c0=True, c1=True, sharp=35.0, vref=None):
        p = []
        if c0: p.append((x0, 0))
        p += [(x0, r0), (x1, r1)]
        if c1: p.append((x1, 0))
        self.lathe(p, mat, seg=seg, oy=oy, oz=oz, sharp=sharp, vref=vref)

    def can(self, x0, x1, r, mat, ch=0.15, seg=32, oy=0.0, oz=0.0, c0=True, c1=True):
        """Cylinder with chamfered rims."""
        d = 1 if x1 > x0 else -1
        p = []
        if c0: p += [(x0, 0), (x0, r - ch)]
        p += [(x0 + d * ch, r), (x1 - d * ch, r)]
        if c1: p += [(x1, r - ch), (x1, 0)]
        self.lathe(p, mat, seg=seg, oy=oy, oz=oz, sharp=50.0)

    def tube(self, a, b, r, mat, seg=8, caps=False, r2=None, sharp=40.0):
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        if L < 1e-6:
            return
        p = [(0, r), (L, r if r2 is None else r2)]
        if caps:
            p = [(0, 0)] + p + [(L, 0)]
        with self.xf(orient(a, d)):
            self.lathe(p, mat, seg=seg, sharp=sharp)

    def pipe(self, pts, r, mat, seg=8, elbow=True):
        for i in range(len(pts) - 1):
            self.tube(pts[i], pts[i + 1], r, mat, seg=seg)
        if elbow:
            for p in pts[1:-1]:
                self.sphere(p, r * 1.05, mat, seg=seg, rings=4)

    def sphere(self, c, r, mat, seg=20, rings=10, sx=1.0, sharp=60.0):
        prof = [(c[0] - r * sx * math.cos(PI * i / rings), r * math.sin(PI * i / rings)) for i in range(rings + 1)]
        self.lathe(prof, mat, seg=seg, oy=c[1], oz=c[2], sharp=sharp)

    def dome_tank(self, x0, x1, r, mat, k=0.6, seg=32, rings=7, oy=0.0, oz=0.0):
        """Cylinder with elliptical end domes; x0..x1 is the overall length."""
        h = r * k
        prof = [(x0 + h - h * math.cos(PI / 2 * i / rings), r * math.sin(PI / 2 * i / rings)) for i in range(rings + 1)]
        prof += [(x1 - h + h * math.sin(PI / 2 * i / rings), r * math.cos(PI / 2 * i / rings)) for i in range(rings + 1)]
        self.lathe(prof, mat, seg=seg, oy=oy, oz=oz, sharp=60.0)

    def torus(self, x, R, r, mat, seg=48, tseg=10, sharp=60.0):
        prof = [(x + r * math.sin(TAU * j / tseg), R + r * math.cos(TAU * j / tseg)) for j in range(tseg)]
        self.lathe(prof, mat, seg=seg, closed=True, sharp=sharp)

    def quad(self, p, mat, uv=None):
        self.add(mat, p, [(0, 1, 2, 3)], [uv or [(0, 0), (1, 0), (1, 1), (0, 1)]])

    def extrudeX(self, poly, x0, x1, mat, bevel=0.0, sharp=35.0):
        """Prism: (y, z) outline swept from x0 to x1."""
        n = len(poly)
        v = [(x0, p[0], p[1]) for p in poly] + [(x1, p[0], p[1]) for p in poly]
        f, uv = [], []
        ou = self.rnd.random() * 60
        s = 0.0
        for i in range(n):
            j = (i + 1) % n
            d = math.hypot(poly[j][0] - poly[i][0], poly[j][1] - poly[i][1])
            f.append((i, j, n + j, n + i))
            uv.append([(x0 + ou, s), (x0 + ou, s + d), (x1 + ou, s + d), (x1 + ou, s)])
            s += d
        f.append(tuple(range(n - 1, -1, -1)))
        uv.append([(poly[i][0] + ou, poly[i][1]) for i in range(n - 1, -1, -1)])
        f.append(tuple(range(n, 2 * n)))
        uv.append([(poly[i][0] + ou, poly[i][1]) for i in range(n)])
        self.add(mat, v, f, uv, bevel, sharp)

    # ---- output
    def build(self, mats, coll, prefix='Coch'):
        obs = []
        for (mat, bevel, sharp), b in self.B.items():
            if not b['f']:
                continue
            me = bpy.data.meshes.new('%s_%s' % (prefix, mat))
            me.from_pydata(b['v'], [], b['f'])
            uvl = me.uv_layers.new(name='UVMap')
            uvl.data.foreach_set('uv', b['uv'])
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            bm.to_mesh(me)
            bm.free()
            me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
            me.set_sharp_from_angle(angle=math.radians(sharp))
            me.materials.append(mats[mat])
            ob = bpy.data.objects.new(me.name, me)
            coll.objects.link(ob)
            if bevel > 0:
                m = ob.modifiers.new('Bevel', 'BEVEL')
                m.width = bevel
                m.segments = 2
                m.limit_method = 'ANGLE'
                m.angle_limit = math.radians(40)
                w = ob.modifiers.new('WN', 'WEIGHTED_NORMAL')
                w.keep_sharp = True
            obs.append(ob)
        return obs

    def stats(self):
        return sum(len(b['f']) for b in self.B.values())


# =====================================================================================
# materials
# =====================================================================================
def _new(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    t = m.node_tree
    for n in list(t.nodes):
        t.nodes.remove(n)
    out = t.nodes.new('ShaderNodeOutputMaterial')
    b = t.nodes.new('ShaderNodeBsdfPrincipled')
    t.links.new(b.outputs[0], out.inputs[0])
    return m, t, b


def _set(node, name, val):
    node.inputs[name].default_value = val


def _c(c, a=1.0):
    if isinstance(c, (int, float)):
        return (c, c, c, a)
    return (c[0], c[1], c[2], a)


def _in(t, sock, v):
    """Link a socket or set a constant."""
    if hasattr(v, 'is_output'):
        t.links.new(v, sock)
    else:
        sock.default_value = v


def _math(t, op, a, b=None, clamp=False):
    n = t.nodes.new('ShaderNodeMath')
    n.operation = op
    n.use_clamp = clamp
    _in(t, n.inputs[0], a)
    if b is not None:
        _in(t, n.inputs[1], b)
    return n.outputs[0]


def _smooth(t, v, lo, hi):
    n = t.nodes.new('ShaderNodeMapRange')
    n.interpolation_type = 'SMOOTHSTEP'
    _in(t, n.inputs[0], v)
    n.inputs[1].default_value = lo; n.inputs[2].default_value = hi
    return n.outputs[0]


def _mix(t, blend, fac, a, b):
    n = t.nodes.new('ShaderNodeMix')
    n.data_type = 'RGBA'
    n.blend_type = blend
    _in(t, n.inputs[0], fac)
    _in(t, n.inputs[6], _c(a) if not hasattr(a, 'is_output') else a)
    _in(t, n.inputs[7], _c(b) if not hasattr(b, 'is_output') else b)
    return n.outputs[2]


def _noise(t, vec, scale, detail=4.0, rough=0.55, dist=0.0):
    n = t.nodes.new('ShaderNodeTexNoise')
    _set(n, 'Scale', scale); _set(n, 'Detail', detail); _set(n, 'Roughness', rough); _set(n, 'Distortion', dist)
    t.links.new(vec, n.inputs['Vector'])
    return n.outputs[0]


def _voro(t, vec, scale, feature='F1'):
    n = t.nodes.new('ShaderNodeTexVoronoi')
    n.feature = feature
    _set(n, 'Scale', scale)
    t.links.new(vec, n.inputs['Vector'])
    return n


def _bump(t, height, bsdf, dist=1.0, strength=1.0):
    n = t.nodes.new('ShaderNodeBump')
    _set(n, 'Strength', strength); _set(n, 'Distance', dist)
    t.links.new(height, n.inputs['Height'])
    t.links.new(n.outputs[0], bsdf.inputs['Normal'])
    return n


def _coords(t):
    n = t.nodes.new('ShaderNodeTexCoord')
    return n.outputs['UV'], n.outputs['Object']


def panel(name, c1, c2=None, size=(2.4, 1.6), seam=0.03, seam_mul=0.3, rough=0.55, rvar=0.15, metal=0.0,
          wob=0.012, grime=0.22, coat=0.0, streak=0.0, age=0.0, wrinkle=0.0):
    """Riveted or bonded skin panels: per-panel tint, dark seams, slight oil-canning, grime."""
    m, t, b = _new(name)
    uv, ob = _coords(t)
    br = t.nodes.new('ShaderNodeTexBrick')
    br.offset = 0.5
    c2 = c2 or tuple(x * 0.88 for x in c1)
    _set(br, 'Color1', _c(c1)); _set(br, 'Color2', _c(c2)); _set(br, 'Mortar', _c(tuple(x * seam_mul for x in c1)))
    _set(br, 'Scale', 1.0); _set(br, 'Mortar Size', seam); _set(br, 'Mortar Smooth', 0.3); _set(br, 'Bias', 0.0)
    _set(br, 'Brick Width', size[0]); _set(br, 'Row Height', size[1])
    t.links.new(uv, br.inputs['Vector'])
    g = _noise(t, ob, 0.11, 7.0, 0.62, 0.4)
    gfac = _math(t, 'MULTIPLY', _math(t, 'SUBTRACT', g, 0.42, clamp=True), grime * 3.0, clamp=True)
    col = _mix(t, 'MULTIPLY', gfac, br.outputs[0], (0.55, 0.52, 0.48))
    if age > 0:
        an = _noise(t, ob, 0.045, 5.0, 0.6, 0.8)
        col = _mix(t, 'MULTIPLY', _math(t, 'MULTIPLY', _smooth(t, an, 0.42, 0.68), age), col, (0.96, 0.88, 0.72))
    if streak > 0:
        s = _noise(t, uv, 6.0, 3.0, 0.5, 0.0)
        col = _mix(t, 'MULTIPLY', _math(t, 'MULTIPLY', s, streak), col, (0.6, 0.6, 0.6))
    t.links.new(col, b.inputs['Base Color'])
    _set(b, 'Metallic', metal)
    rn = _noise(t, ob, 1.7, 4.0, 0.6)
    r = _math(t, 'ADD', rough - rvar / 2, _math(t, 'MULTIPLY', rn, rvar))
    r = _math(t, 'ADD', r, _math(t, 'MULTIPLY', gfac, 0.2), clamp=True)
    t.links.new(r, b.inputs['Roughness'])
    if coat:
        _set(b, 'Coat Weight', coat)
    w = _noise(t, uv, 0.9 / max(size), 2.0, 0.5)
    h = _math(t, 'ADD', _math(t, 'MULTIPLY', w, wob), _math(t, 'MULTIPLY', br.outputs[1], -0.012))
    h = _math(t, 'ADD', h, _math(t, 'MULTIPLY', _noise(t, ob, 14.0, 2.0, 0.5), 0.0015))
    if wrinkle > 0:
        h = _math(t, 'ADD', h, _math(t, 'MULTIPLY', _noise(t, ob, 2.2, 5.0, 0.65, 1.2), wrinkle))
    _bump(t, h, b)
    return m


def plain(name, col, rough=0.5, metal=0.0, rvar=0.12, grime=0.15, bump=0.0, coat=0.0):
    m, t, b = _new(name)
    uv, ob = _coords(t)
    g = _noise(t, ob, 0.35, 6.0, 0.6, 0.3)
    gfac = _math(t, 'MULTIPLY', _math(t, 'SUBTRACT', g, 0.4, clamp=True), grime * 3.0, clamp=True)
    t.links.new(_mix(t, 'MULTIPLY', gfac, col, (0.5, 0.48, 0.45)), b.inputs['Base Color'])
    _set(b, 'Metallic', metal)
    rn = _noise(t, ob, 2.3, 4.0, 0.6)
    t.links.new(_math(t, 'ADD', rough - rvar / 2, _math(t, 'MULTIPLY', rn, rvar), clamp=True), b.inputs['Roughness'])
    if coat:
        _set(b, 'Coat Weight', coat)
    if bump:
        _bump(t, _math(t, 'MULTIPLY', _noise(t, ob, 5.0, 4.0, 0.6), bump), b)
    return m


def foil(name, col, rough=0.2, scale=1.3, depth=0.07):
    """Multi-layer insulation: crinkled metallised film."""
    m, t, b = _new(name)
    uv, ob = _coords(t)
    warp = t.nodes.new('ShaderNodeVectorMath')
    warp.operation = 'ADD'
    nz = t.nodes.new('ShaderNodeTexNoise')
    _set(nz, 'Scale', 0.6); _set(nz, 'Detail', 2.0)
    t.links.new(ob, nz.inputs['Vector'])
    t.links.new(ob, warp.inputs[0]); t.links.new(nz.outputs[1], warp.inputs[1])
    v1 = _voro(t, warp.outputs[0], scale)
    v2 = _voro(t, warp.outputs[0], scale * 3.1)
    h = _math(t, 'ADD', _math(t, 'MULTIPLY', v1.outputs[0], depth), _math(t, 'MULTIPLY', v2.outputs[0], depth * 0.3))
    h = _math(t, 'ADD', h, _math(t, 'MULTIPLY', _noise(t, ob, 0.5, 3.0, 0.5), depth * 1.5))
    _bump(t, h, b)
    tint = _noise(t, ob, 0.8, 3.0, 0.6)
    t.links.new(_mix(t, 'MULTIPLY', _math(t, 'MULTIPLY', tint, 0.35), col, (0.75, 0.7, 0.62)), b.inputs['Base Color'])
    _set(b, 'Metallic', 1.0)
    t.links.new(_math(t, 'ADD', rough, _math(t, 'MULTIPLY', v2.outputs[0], 0.25), clamp=True), b.inputs['Roughness'])
    return m


def emit(name, col, strength, base=(0.02, 0.02, 0.02)):
    m, t, b = _new(name)
    _set(b, 'Base Color', _c(base)); _set(b, 'Roughness', 0.3)
    _set(b, 'Emission Color', _c(col)); _set(b, 'Emission Strength', strength)
    return m


def windows(name, strength=6.0, lit=0.7):
    """Cabin windows: some lit warm, some dark, glossy glass in front. UVs carry a random offset per window."""
    m, t, b = _new(name)
    uv, ob = _coords(t)
    wn = t.nodes.new('ShaderNodeTexWhiteNoise')
    wn.noise_dimensions = '3D'
    sn = t.nodes.new('ShaderNodeVectorMath')
    sn.operation = 'SNAP'
    sn.inputs[1].default_value = (1.7, 1.7, 1.7)
    t.links.new(ob, sn.inputs[0]); t.links.new(sn.outputs[0], wn.inputs['Vector'])
    on = _math(t, 'LESS_THAN', wn.outputs[0], lit)
    warm = _mix(t, 'MIX', wn.outputs[0], (1.0, 0.78, 0.48), (0.85, 0.92, 1.0))
    _set(b, 'Base Color', _c((0.015, 0.02, 0.03))); _set(b, 'Roughness', 0.06); _set(b, 'Specular IOR Level', 0.8)
    t.links.new(warm, b.inputs['Emission Color'])
    t.links.new(_math(t, 'MULTIPLY', on, _math(t, 'ADD', strength * 0.5, _math(t, 'MULTIPLY', wn.outputs[0], strength))),
                b.inputs['Emission Strength'])
    return m


def hot_radiator(name, t_root=1250.0, t_tip=1020.0, gain=1.0):
    """Carbon-carbon panel glowing as a black body. UV: u in metres along the panel, v 0 at the root to 1 at the tip."""
    m, t, b = _new(name)
    uv, ob = _coords(t)
    sep = t.nodes.new('ShaderNodeSeparateXYZ')
    t.links.new(uv, sep.inputs[0])
    v = sep.outputs[1]
    pipes = t.nodes.new('ShaderNodeTexWave')
    pipes.wave_type = 'BANDS'; pipes.bands_direction = 'X'
    _set(pipes, 'Scale', 0.9); _set(pipes, 'Distortion', 0.0)
    t.links.new(uv, pipes.inputs['Vector'])
    blot = _noise(t, ob, 0.25, 4.0, 0.6)
    temp = _math(t, 'ADD', t_tip, _math(t, 'MULTIPLY', _math(t, 'SUBTRACT', 1.0, v, clamp=True), t_root - t_tip))
    temp = _math(t, 'ADD', temp, _math(t, 'MULTIPLY', _math(t, 'SUBTRACT', pipes.outputs[0], 0.5), 70.0))
    temp = _math(t, 'ADD', temp, _math(t, 'MULTIPLY', _math(t, 'SUBTRACT', blot, 0.5), 110.0))
    bb = t.nodes.new('ShaderNodeBlackbody')
    t.links.new(temp, bb.inputs[0])
    # Stefan-Boltzmann shape for brightness, normalised at 1200 K
    e = _math(t, 'POWER', _math(t, 'DIVIDE', temp, 1200.0), 7.0)
    _set(b, 'Base Color', _c((0.012, 0.012, 0.013))); _set(b, 'Roughness', 0.7)
    t.links.new(bb.outputs[0], b.inputs['Emission Color'])
    t.links.new(_math(t, 'MULTIPLY', e, gain), b.inputs['Emission Strength'])
    _bump(t, _math(t, 'MULTIPLY', pipes.outputs[0], 0.03), b)
    return m


def decal(name, image, col=(0.03, 0.03, 0.035)):
    m, t, b = _new(name)
    tex = t.nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.extension = 'CLIP'
    _set(b, 'Base Color', _c(col)); _set(b, 'Roughness', 0.6)
    t.links.new(tex.outputs['Alpha'], b.inputs['Alpha'])
    return m


def corrugated(name, col, pitch=0.3, rough=0.5):
    m, t, b = _new(name)
    uv, ob = _coords(t)
    w = t.nodes.new('ShaderNodeTexWave')
    w.wave_type = 'BANDS'; w.bands_direction = 'X'
    _set(w, 'Scale', 0.314 / pitch); _set(w, 'Distortion', 0.0)
    t.links.new(uv, w.inputs['Vector'])
    g = _noise(t, ob, 0.5, 6.0, 0.6, 0.3)
    gfac = _math(t, 'MULTIPLY', _math(t, 'SUBTRACT', g, 0.4, clamp=True), 0.6, clamp=True)
    t.links.new(_mix(t, 'MULTIPLY', gfac, col, (0.45, 0.42, 0.4)), b.inputs['Base Color'])
    _set(b, 'Roughness', rough)
    _bump(t, _math(t, 'MULTIPLY', w.outputs[0], 0.04), b)
    return m
