"""Survey rover finishes and damage (contract: rover_variants.py FINISHES / DAMAGE).

Two entry points, called after the rover is built (build_vehicles.survey_rover):
  weather_hull(obj, finish, damage, res)  drop-in for build_vehicles.weather_hull: smart-UV the hull, Cycles EMIT-bake a
                                          procedural (paint scuffs + dust / mud / snow + scratches, dents, scorch) into a
                                          packed image, swap in an image-texture Principled material f"{obj.name}Paint".
  apply_parts(root, finish, damage)       everything else, by part name (missing parts are skipped): snow caps (real
                                          geometry, material "Snow"), tinted copies of the shared materials (dusty /
                                          muddy / frosted), baked glass (wiper arcs, frost, cracks, smashed windscreen),
                                          dark broken lamps, bent antenna, missing cap / locker, hanging bumper, tilted
                                          dish, burnt-out rear.
Everything stays glTF-friendly: Principled BSDF materials, image textures only from bakes.

Object space is used throughout (the hull sits at the root's origin, +Y front, +Z up), so other tiers pass their own
`z_range` (hull bottom/top) and `arches` (wheel y, radius).
"""
import math

import bpy  # first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh
from mathutils import Matrix, Vector, noise as mnoise

FINISHES = ("clean", "dust", "mud", "snow")
MK2_ARCHES = [(1.5, 0.6), (-0.4, 0.6), (-1.75, 0.6)]

# ---------------------------------------------------------------- colours

CLEAN = (0.8, 0.81, 0.82)
GRIME = (0.42, 0.38, 0.33)
RUB = (0.92, 0.93, 0.94)
DUST_FILM = (0.68, 0.5, 0.28)
DUST_CAKE = (0.47, 0.32, 0.16)
MUD_DRY = (0.34, 0.25, 0.16)
MUD_WET = (0.075, 0.05, 0.03)
SNOW = (0.95, 0.96, 0.98)
FROST = (0.86, 0.9, 0.96)
SLUSH = (0.55, 0.57, 0.6)
SLUSH_DIRT = (0.22, 0.2, 0.18)
PRIMER = (0.13, 0.14, 0.15)
METAL = (0.8, 0.82, 0.85)
DENT = (0.2, 0.2, 0.21)
BURNT = (0.3, 0.17, 0.08)
SOOT = (0.025, 0.022, 0.02)
ASH = (0.16, 0.15, 0.14)
GLASS = (0.05, 0.085, 0.04)
CRACK = (0.72, 0.76, 0.78)

# Paint surface per finish: (roughness, coat).
PAINT = {"clean": (0.42, 0.2), "dust": (0.78, 0.0), "mud": (0.62, 0.08), "snow": (0.5, 0.15)}


# ---------------------------------------------------------------- small helpers


def _material(name, colour, metallic=0.0, roughness=0.5, coat=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes.get("Principled BSDF")
    p.inputs["Base Color"].default_value = (*colour, 1.0)
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = roughness
    p.inputs["Coat Weight"].default_value = coat
    return m


def _bsdf(mat):
    return mat.node_tree.nodes.get("Principled BSDF") if mat and mat.use_nodes else None


def _mix3(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


class _Graph:
    """Terse shader-node building: every helper takes sockets or plain numbers and returns an output socket."""

    def __init__(self, nt):
        self.nt = nt
        self.tc = nt.nodes.new("ShaderNodeTexCoord")
        self.P = self.tc.outputs["Object"]
        self.x, self.y, self.z = self.sep(self.P)
        self.nx, self.ny, self.nz = self.sep(self.tc.outputs["Normal"])

    def _set(self, sock, v):
        if isinstance(v, bpy.types.NodeSocket):
            self.nt.links.new(v, sock)
        elif isinstance(v, (tuple, list)):
            sock.default_value = (*v, 1.0) if len(v) == 3 and len(sock.default_value) == 4 else v
        else:
            sock.default_value = v

    def sep(self, vec):
        n = self.nt.nodes.new("ShaderNodeSeparateXYZ")
        self._set(n.inputs[0], vec)
        return n.outputs[0], n.outputs[1], n.outputs[2]

    def vec(self, x, y, z):
        n = self.nt.nodes.new("ShaderNodeCombineXYZ")
        for i, v in enumerate((x, y, z)):
            self._set(n.inputs[i], v)
        return n.outputs[0]

    def math(self, op, a, b=0.0, c=0.0, clamp=False):
        n = self.nt.nodes.new("ShaderNodeMath")
        n.operation = op
        n.use_clamp = clamp
        for i, v in enumerate((a, b, c)):
            self._set(n.inputs[i], v)
        return n.outputs[0]

    def add(self, a, b):
        return self.math("ADD", a, b)

    def sub(self, a, b):
        return self.math("SUBTRACT", a, b)

    def mul(self, a, b):
        return self.math("MULTIPLY", a, b)

    def mad(self, a, b, c):
        return self.math("MULTIPLY_ADD", a, b, c)

    def max(self, a, b):
        return self.math("MAXIMUM", a, b)

    def inv(self, a):
        return self.math("SUBTRACT", 1.0, a, clamp=True)

    def _range(self, x, lo, hi, to_lo, to_hi, kind):
        n = self.nt.nodes.new("ShaderNodeMapRange")
        n.interpolation_type = kind
        n.clamp = True
        for key, v in (("Value", x), ("From Min", lo), ("From Max", hi), ("To Min", to_lo), ("To Max", to_hi)):
            self._set(n.inputs[key], v)
        return n.outputs["Result"]

    def smooth(self, x, lo, hi):
        """0 below lo, 1 above hi (lo < hi)."""
        return self._range(x, lo, hi, 0.0, 1.0, "SMOOTHSTEP")

    def lin(self, x, lo, hi, to_lo=0.0, to_hi=1.0):
        return self._range(x, lo, hi, to_lo, to_hi, "LINEAR")

    def noise(self, vec, scale, detail=4.0, rough=0.5, distortion=0.0):
        n = self.nt.nodes.new("ShaderNodeTexNoise")
        self._set(n.inputs["Vector"], vec)
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough
        n.inputs["Distortion"].default_value = distortion
        return n.outputs["Fac"]

    def voronoi(self, vec, scale, feature="F1", randomness=1.0):
        n = self.nt.nodes.new("ShaderNodeTexVoronoi")
        n.feature = feature
        self._set(n.inputs["Vector"], vec)
        n.inputs["Scale"].default_value = scale
        n.inputs["Randomness"].default_value = randomness
        return n.outputs["Distance"]

    def mapping(self, vec, scale=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
        n = self.nt.nodes.new("ShaderNodeMapping")
        self._set(n.inputs["Vector"], vec)
        n.inputs["Scale"].default_value = scale
        n.inputs["Location"].default_value = loc
        n.inputs["Rotation"].default_value = rot
        return n.outputs["Vector"]

    def length2(self, a, b):
        return self.math("SQRT", self.add(self.mul(a, a), self.mul(b, b)))

    def mix(self, fac, a, b):
        n = self.nt.nodes.new("ShaderNodeMix")
        n.data_type = "RGBA"
        n.clamp_factor = True
        self._set(n.inputs["Factor"], fac)
        self._set(next(s for s in n.inputs if s.name == "A" and s.type == "RGBA"), a)
        self._set(next(s for s in n.inputs if s.name == "B" and s.type == "RGBA"), b)
        return next(s for s in n.outputs if s.type == "RGBA")


def _bake(obj, build, res, name):
    """Apply modifiers, smart-UV, EMIT-bake build(graph) -> colour socket into a packed image named `name`.
    Everything else is hidden while baking so nearby parts don't print into the texture."""
    scene = bpy.context.scene
    old_engine, old_samples = scene.render.engine, scene.cycles.samples
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 4
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01)
    bpy.ops.object.mode_set(mode="OBJECT")

    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    image = bpy.data.images.new(name, res, res)
    bake = bpy.data.materials.new(f"{name}Bake")
    bake.use_nodes = True
    nt = bake.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    g = _Graph(nt)
    emit = nt.nodes.new("ShaderNodeEmission")
    g._set(emit.inputs["Color"], build(g))
    nt.links.new(emit.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    target = nt.nodes.new("ShaderNodeTexImage")
    target.image = image
    nt.nodes.active = target

    obj.data.materials.clear()
    obj.data.materials.append(bake)
    hidden = [o for o in scene.objects if o is not obj and not o.hide_render]
    for o in hidden:
        o.hide_render = True
    try:
        scene.cycles.bake_type = "EMIT"
        bpy.ops.object.bake(type="EMIT", margin=4)
    finally:
        for o in hidden:
            o.hide_render = False
        scene.render.engine, scene.cycles.samples = old_engine, old_samples
    image.pack()
    bpy.data.materials.remove(bake)
    return image


def _image_material(name, image, metallic, roughness, coat):
    final = _material(name, (1, 1, 1), metallic, roughness, coat)
    nt = final.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    return final


# ---------------------------------------------------------------- hull paint


def _hull_colour(g, finish, damage, arches, z_range, y_range):
    P, x, y, z = g.P, g.x, g.y, g.z
    z0, z1 = z_range
    h = z1 - z0
    ya, yb = y_range
    hz = g.lin(z, z0, z1)  # 0 at the bottom of the hull, 1 on the roof

    # Base scuffed paint (the original weather_hull): dirt by height, rub marks, streaks.
    big = g.noise(P, 3.0, 8.0)
    dirt = g.mad(big, 0.8, g.lin(z, z0, z0 + h * 0.64))
    col = g.mix(g.lin(dirt, 0.75, 1.35, 0.75, 0.0), CLEAN, GRIME)
    col = g.mix(g.lin(g.noise(P, 26.0, 4.0), 0.55, 0.62), col, RUB)
    streak = g.noise(g.mapping(P, (14.0, 14.0, 1.2)), 2.0)
    col = g.mix(g.lin(streak, 0.55, 0.75, 0.0, 0.4), col, GRIME)

    # Spray zone thrown up behind and above each wheel: an ellipse centred a little behind and above the hub.
    arch = None
    for wy, r in arches:
        dy = g.mul(g.sub(y, wy - 0.6 * r / 0.6), 1.0 / (1.8 * r))
        dz = g.mul(g.sub(z, r + 0.35 * r / 0.6), 1.0 / (2.3 * r))
        a = g.inv(g.smooth(g.length2(dy, dz), 0.3, 1.0))
        arch = a if arch is None else g.max(arch, a)
    if arch is None:
        arch = 0.0
    low = g.inv(g.smooth(z, z0 + 0.05 * h, z0 + 0.7 * h))  # 1 at the sills, 0 two thirds up

    if finish == "dust":
        nd = g.noise(P, 2.2, 6.0, 0.6)
        nf = g.noise(P, 12.0, 3.0)
        # A film over everything, thinning upward; tops collect a little more.
        film = g.mad(g.inv(hz), 0.5, 0.38)
        film = g.add(film, g.mul(g.smooth(g.nz, 0.6, 0.9), 0.25))
        film = g.mul(film, g.lin(nd, 0.3, 0.7, 0.7, 1.15))
        col = g.mix(film, col, DUST_FILM)
        # Caked dust low down and in the wheel spray, with a ragged noise-broken top edge.
        cake = g.max(low, g.mul(arch, 0.85))
        cake = g.add(cake, g.add(g.mul(g.sub(nd, 0.5), 1.1), g.mul(g.sub(nf, 0.5), 0.35)))
        col = g.mix(g.smooth(cake, 0.3, 0.45), col, DUST_CAKE)
        # Run-down streaks from the roof edge.
        drip = g.noise(g.mapping(P, (18.0, 18.0, 0.8)), 2.0)
        col = g.mix(g.mul(g.lin(drip, 0.58, 0.72, 0.0, 0.5), hz), col, DUST_CAKE)

    elif finish == "mud":
        ns = g.noise(P, 6.0, 8.0, 0.65)
        m = g.max(arch, g.mul(low, 0.7))
        wet = g.add(m, g.mul(g.sub(ns, 0.5), 1.3))
        col = g.mix(g.mul(g.inv(hz), 0.3), col, MUD_DRY)  # general grime, worse low
        col = g.mix(g.mul(g.smooth(wet, 0.18, 0.3), 0.9), col, MUD_DRY)  # dried halo
        drops = g.mul(g.smooth(g.noise(P, 34.0, 2.0), 0.66, 0.69), g.smooth(m, 0.05, 0.45))
        col = g.mix(g.max(g.smooth(wet, 0.34, 0.4), drops), col, MUD_WET)
        # Flicked spots higher up.
        flick = g.mul(g.smooth(g.noise(P, 22.0, 2.0), 0.7, 0.72), g.smooth(arch, 0.0, 0.15))
        col = g.mix(flick, col, MUD_WET)

    elif finish == "snow":
        nsn = g.noise(P, 4.0, 6.0)
        frost = g.mad(g.smooth(g.noise(P, 14.0, 4.0), 0.45, 0.65), 0.3, 0.1)
        col = g.mix(frost, col, FROST)
        # Slush thrown up by the wheels: grey with dirty speckles.
        slush = g.add(g.max(g.mul(arch, 0.85), g.mul(low, 0.6)), g.mul(g.sub(g.noise(P, 9.0, 6.0, 0.6), 0.5), 1.1))
        col = g.mix(g.smooth(slush, 0.5, 0.56), col, SLUSH)
        dirt_spk = g.mul(g.smooth(g.noise(P, 30.0, 2.0), 0.64, 0.68), g.smooth(slush, 0.3, 0.5))
        col = g.mix(dirt_spk, col, SLUSH_DIRT)
        # Wind-driven snow plastered on the front and sides, streaked horizontally.
        windn = g.noise(g.mapping(P, (2.0, 2.0, 7.0)), 2.5, 4.0)
        front = g.smooth(g.ny, 0.3, 0.8)
        col = g.mix(g.mul(front, g.smooth(windn, 0.45, 0.55)), col, SNOW)
        # Snow lying on every upward face.
        up = g.smooth(g.nz, 0.45, 0.8)
        col = g.mix(g.mul(up, g.smooth(g.add(nsn, 0.25), 0.4, 0.52)), col, SNOW)

    if damage >= 1:
        # Dents: explicit positions on the nose corners and flanks, shaded darker at the top, with a creased rim.
        dents = [(1.12, 2.22, 1.2, 0.38)]
        if damage >= 2:
            dents += [(-1.2, 1.85, 1.2, 0.3), (1.22, -1.2, 1.75, 0.32)]
        if damage >= 3:
            dents += [(1.22, 0.55, 1.05, 0.28), (0.0, 2.32, 1.25, 0.35)]
        dent = None
        for cx, cy, cz, r in dents:
            d = g.math("SQRT", g.add(g.add(g.mul(g.sub(x, cx), g.sub(x, cx)), g.mul(g.sub(y, cy), g.sub(y, cy))),
                                     g.mul(g.sub(z, cz), g.sub(z, cz))))
            inside = g.inv(g.smooth(d, r * 0.3, r))
            top = g.lin(z, cz - r, cz + r, 0.35, 0.95)
            col = g.mix(g.mul(inside, top), col, DENT)
            rim = g.inv(g.smooth(g.math("ABSOLUTE", g.sub(d, r * 0.85)), 0.0, r * 0.1))
            col = g.mix(g.mul(rim, 0.5), col, PRIMER)
            dent = inside if dent is None else g.max(dent, inside)

        # Long gouges scraped along both flanks (between the stripe and the name), more with each level.
        gouges = [(1.05, 1.82, -0.55, 1.62), (0.9, 1.74, -0.35, 1.57), (0.7, 1.68, -0.2, 1.55)]
        if damage >= 2:
            gouges += [(-0.6, 2.12, -2.1, 1.75), (2.0, 1.25, 1.0, 1.05)]
        wob = g.mul(g.sub(g.noise(P, 6.0, 3.0), 0.5), 0.03)
        for s in (-1, 1):
            side = g.smooth(g.mul(x, s), 0.95, 1.1)
            for k, (ay, az, by, bz) in enumerate(gouges):
                if s < 0:
                    ay, by = ay - 0.3 * k, by - 0.2
                vy, vz = by - ay, bz - az
                t = g.math("DIVIDE", g.add(g.mul(g.sub(y, ay), vy), g.mul(g.sub(z, az), vz)), vy * vy + vz * vz)
                t = g.math("MINIMUM", g.max(t, 0.0), 1.0)
                d = g.length2(g.sub(y, g.mad(t, vy, ay)), g.sub(z, g.mad(t, vz, az)))
                d = g.add(d, wob)
                w = 0.016 if k else 0.022
                col = g.mix(g.mul(g.inv(g.smooth(d, w, w + 0.014)), side), col, PRIMER)
                col = g.mix(g.mul(g.inv(g.smooth(d, w * 0.25, w * 0.7)), side), col, METAL)

        # Scratches through the paint: contour lines of a noise stretched along the body (long gouges), in
        # clusters; dark primer edges with bright bare metal in the middle.
        thr = {1: 0.58, 2: 0.55, 3: 0.52}[min(damage, 3)]
        cluster = g.smooth(g.add(g.noise(P, 0.9, 2.0), g.mul(dent, 0.5)), thr, thr + 0.06)
        sets = [((7.0, 0.35, 7.0), (0, 0, 0))]
        if damage >= 3:
            sets.append(((6.0, 0.4, 6.0), (0.5, 0.0, 0.35)))
        for scale, rot in sets:
            sn = g.noise(g.mapping(P, scale, rot=rot), 1.2, 1.0)
            dist = g.math("ABSOLUTE", g.sub(sn, 0.5))
            col = g.mix(g.mul(g.inv(g.smooth(dist, 0.03, 0.05)), cluster), col, PRIMER)
            col = g.mix(g.mul(g.inv(g.smooth(dist, 0.012, 0.026)), cluster), col, METAL)

    if damage >= 2:
        nsc = g.noise(P, 1.4, 6.0, 0.6)
        rear = g.inv(g.smooth(y, ya, ya + (yb - ya) * 0.4))  # 1 at the tail, 0 by mid-body
        # A guaranteed patch on the right rear quarter (visible from the front three-quarter view).
        qx, qy, qz, qr = 1.22, -1.55, 1.55, 0.75 if damage == 2 else 1.1
        q = g.math("SQRT", g.add(g.add(g.mul(g.sub(x, qx), g.sub(x, qx)), g.mul(g.sub(y, qy), g.sub(y, qy))),
                                 g.mul(g.sub(z, qz), g.sub(z, qz))))
        patch = g.mul(g.inv(g.smooth(q, 0.0, qr)), 0.45)
        if damage == 2:
            s, thr = g.add(nsc, patch), 0.68
        else:
            s, thr = g.add(g.add(g.add(nsc, patch), g.mul(rear, 0.6)), 0.12), 0.62
        if damage >= 3:
            col = g.mix(0.2, col, GRIME)
        col = g.mix(g.mul(g.smooth(s, thr - 0.12, thr), 0.9), col, BURNT)
        col = g.mix(g.smooth(s, thr, thr + 0.07), col, SOOT)
        ash = g.mul(g.smooth(g.noise(P, 28.0, 2.0), 0.67, 0.7), g.smooth(s, thr + 0.05, thr + 0.15))
        col = g.mix(g.mul(ash, 0.7), col, ASH)
    return col


def weather_hull(obj, finish="clean", damage=0, res=1024, arches=None, z_range=(0.8, 2.36)):
    """Bake the hull's paint for a finish and damage level into a packed image, then swap in a plain image-texture
    material named f"{obj.name}Paint" (what glTF can carry)."""
    if finish not in FINISHES:
        raise ValueError(f"finish must be one of {FINISHES}")
    damage = max(0, min(3, int(damage)))
    arches = MK2_ARCHES if arches is None else arches
    ys = [v[1] for v in obj.bound_box]
    y_range = (min(ys), max(ys))
    image = _bake(obj, lambda g: _hull_colour(g, finish, damage, arches, z_range, y_range), res, f"{obj.name}Paint")
    rough, coat = PAINT[finish]
    if damage >= 3:
        rough, coat = max(rough, 0.7), 0.0
    final = _image_material(f"{obj.name}Paint", image, 0.15, rough, coat)
    obj.data.materials.clear()
    obj.data.materials.append(final)
    return final


# ---------------------------------------------------------------- glass


def _glass_colour(g, finish, crack, smash, wipers, centre, half, wide):
    """Normalised pane coords: u across (the wider of local x / y), v up (local z), both -1..1; metres = u*hu, v*hv."""
    cu = g.sub(g.x if wide == "x" else g.y, centre[0 if wide == "x" else 1])
    cv = g.sub(g.z, centre[2])
    hu, hv = half[0 if wide == "x" else 1], half[2]
    u, v = g.mul(cu, 1.0 / hu), g.mul(cv, 1.0 / hv)
    P = g.P
    col = GLASS
    n = g.noise(P, 5.0, 5.0)
    clear = 0.0
    if wipers:
        for px in (-0.5, 0.5):
            d = g.length2(g.sub(cu, px), g.sub(cv, -hv * 1.05))
            arc = g.inv(g.smooth(d, hv * 1.45, hv * 1.55))
            clear = arc if clear == 0.0 else g.max(clear, arc)
        clear = g.mul(clear, g.smooth(v, -0.9, -0.8))
    edge = g.max(g.math("ABSOLUTE", u), g.math("ABSOLUTE", v))
    bottom = g.inv(g.smooth(v, -1.0, -0.35))
    if finish == "dust":
        film = g.mad(g.inv(clear), 0.6, 0.12)
        col = g.mix(g.mul(film, g.lin(n, 0.3, 0.7, 0.75, 1.1)), col, DUST_FILM)
        col = g.mix(g.smooth(g.add(g.max(bottom, g.smooth(edge, 0.75, 1.0)), g.mul(g.sub(n, 0.5), 0.8)), 0.45, 0.6),
                    col, DUST_CAKE)
    elif finish == "mud":
        col = g.mix(g.mul(g.inv(clear), 0.35), col, MUD_DRY)
        splat = g.add(g.mul(bottom, 0.9), g.mul(g.sub(g.noise(P, 9.0, 6.0, 0.6), 0.5), 1.4))
        col = g.mix(g.smooth(splat, 0.42, 0.48), col, MUD_WET)
    elif finish == "snow":
        frost = g.add(g.smooth(edge, 0.45, 0.95), g.mul(g.inv(clear), 0.55))
        frost = g.add(frost, g.mul(g.sub(g.noise(P, 16.0, 4.0), 0.5), 0.6))
        col = g.mix(g.smooth(frost, 0.35, 0.75), col, FROST)
        col = g.mix(g.smooth(g.add(bottom, g.mul(g.sub(n, 0.5), 0.6)), 0.55, 0.65), col, SNOW)
    if crack or smash:
        # Impact point; spider-web cracks = Voronoi edges in polar coordinates around it, plus radial crazing.
        iu, iv = (0.25, 0.15) if smash else (0.3, 0.25)
        du, dv = g.sub(cu, iu * hu), g.sub(cv, iv * hv)
        d = g.length2(du, dv)
        ang = g.math("ARCTAN2", dv, du)
        polar = g.vec(g.mul(ang, 1.1), g.mul(g.math("LOGARITHM", g.add(d, 0.02), math.e), 1.5), 0.0)
        lw = 0.012 if smash else 0.03  # line half-width in Voronoi cells
        web = g.inv(g.smooth(g.voronoi(polar, 1.0, "DISTANCE_TO_EDGE"), lw, lw + 0.04))
        reach = 0.4 if crack and not smash else 3.0
        web = g.mul(web, g.inv(g.smooth(d, reach * 0.5, reach)))
        if smash:
            col = g.mix(0.45, col, (0.015, 0.02, 0.015))
            craze = g.inv(g.smooth(g.voronoi(P, 11.0, "DISTANCE_TO_EDGE"), 0.0, 0.03))
            col = g.mix(g.mul(craze, 0.45), col, CRACK)
        col = g.mix(g.mul(web, 0.7 if smash else 1.0), col, CRACK)
        col = g.mix(g.inv(g.smooth(d, 0.02, 0.08 if not smash else 0.2)), col, CRACK)
        if smash:
            col = g.mix(g.inv(g.smooth(d, 0.06, 0.1)), col, (0.005, 0.005, 0.005))  # the hole
    return col


def _bake_glass(obj, finish, crack=False, smash=False, res=512):
    wipers = obj.name == "Windscreen"
    for m in list(obj.modifiers):  # bake on the shape that exports
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=m.name)
    bb = [Vector(v) for v in obj.bound_box]
    lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    centre, half = (lo + hi) / 2, (hi - lo) / 2
    half = Vector((max(half.x, 1e-3), max(half.y, 1e-3), max(half.z, 1e-3)))
    wide = "x" if half.x >= half.y else "y"
    image = _bake(obj, lambda g: _glass_colour(g, finish, crack, smash, wipers, centre, half, wide), res, f"{obj.name}Glass")
    if smash:
        rough, metal, coat = 0.35, 0.1, 0.2
    elif finish == "clean":
        rough, metal, coat = 0.06, 0.3, 1.0
    else:
        rough, metal, coat = {"dust": 0.45, "mud": 0.3, "snow": 0.4}[finish], 0.1, 0.4
    final = _image_material(f"{obj.name}Glass", image, metal, rough, coat)
    obj.data.materials.clear()
    obj.data.materials.append(final)


# ---------------------------------------------------------------- part tints

# finish -> (colour, amount by height band low/mid/high, roughness floor or None)
TINT = {
    "dust": (DUST_CAKE, (0.7, 0.5, 0.35), 0.8),
    "mud": (MUD_WET, (0.8, 0.25, 0.08), None),
    "snow": (FROST, (0.3, 0.22, 0.4), None),
}
TINTED = ("Hull", "HullPanel", "Chassis", "Steel", "Hub", "Frame", "Number")
TYRE = {  # finish -> (tyre body colour, roughness), (tread colour, roughness)
    "dust": (((0.24, 0.17, 0.1), 0.95), ((0.42, 0.3, 0.17), 0.95)),
    "mud": (((0.07, 0.05, 0.03), 0.45), ((0.09, 0.06, 0.036), 0.5)),
    "snow": (((0.66, 0.69, 0.74), 0.7), ((0.17, 0.17, 0.19), 0.6)),
}


def _variant(base, suffix, colour=None, amount=1.0, roughness=None, metallic=None, kill_emission=False):
    name = f"{base.name}_{suffix}"
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = base.copy()
    m.name = name
    p = _bsdf(m)
    if p:
        if colour is not None:
            c = tuple(p.inputs["Base Color"].default_value)[:3]
            p.inputs["Base Color"].default_value = (*_mix3(c, colour, amount), 1.0)
        if roughness is not None:
            p.inputs["Roughness"].default_value = roughness
        if metallic is not None:
            p.inputs["Metallic"].default_value = metallic
        if kill_emission:
            p.inputs["Emission Strength"].default_value = 0.0
    return m


def _swap(obj, fn):
    if obj.type != "MESH":
        return
    mats = obj.data.materials
    for i, m in enumerate(mats):
        if m is not None:
            new = fn(m)
            if new is not None:
                mats[i] = new


def _centre(obj):
    if obj.type != "MESH":
        return obj.matrix_world.translation
    return obj.matrix_world @ (sum((Vector(v) for v in obj.bound_box), Vector()) / 8)


def _tint_parts(root, finish):
    colour, amounts, rough_floor = TINT[finish]
    (body, body_r), (tread, tread_r) = TYRE[finish]
    for o in root.children_recursive:
        if o.type != "MESH":
            continue
        z = _centre(o).z
        band = 0 if z < 1.0 else 1 if z < 1.9 else 2

        def fn(m, o=o, band=band):
            base = m.name.split("_")[0]
            if base == "Tyre":
                if "Tread" in o.name:
                    return _variant(m, f"{finish}Tread", tread, 1.0, tread_r)
                return _variant(m, finish, body, 1.0, body_r)
            if base not in TINTED:
                return None
            p = _bsdf(m)
            r = p.inputs["Roughness"].default_value if p else 0.5
            if rough_floor is not None:
                r = max(r, rough_floor)
            if finish == "mud" and band == 0:
                r = 0.4  # wet
            metal = p.inputs["Metallic"].default_value if p else 0.0
            if finish == "dust" or (finish == "mud" and band == 0):
                metal = min(0.25, metal)  # caked over: no bare-metal shine
            return _variant(m, f"{finish}{band}", colour, amounts[band], r, metal)

        _swap(o, fn)


# ---------------------------------------------------------------- snow caps


def _snow_cap(target, root, thickness, spacing=0.05, min_nz=0.7, mat=None):
    """Lumpy snow lying on `target`'s upward faces: a grid of rays from above; cells whose corners all land on the
    target (not on whatever is above it) become a pillow, thick in the middle, tapering to the edges, with a skirt."""
    scene = bpy.context.scene
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    bb = [target.matrix_world @ Vector(v) for v in target.bound_box]
    x0, x1 = min(v.x for v in bb), max(v.x for v in bb)
    y0, y1 = min(v.y for v in bb), max(v.y for v in bb)
    top = max(v.z for v in bb) + 3.0
    nx, ny = max(2, int((x1 - x0) / spacing) + 1), max(2, int((y1 - y0) / spacing) + 1)
    hits = {}
    for i in range(nx):
        for j in range(ny):
            p = Vector((x0 + (x1 - x0) * i / (nx - 1), y0 + (y1 - y0) * j / (ny - 1), top))
            ok, loc, nrm, _, obj, _ = scene.ray_cast(dg, p, Vector((0, 0, -1)))
            if ok and obj is not None and obj.original is target and nrm.z >= min_nz:
                hits[(i, j)] = (loc, nrm)
    if len(hits) < 4:
        return None
    # Grid distance from each hit to the nearest miss (multi-source BFS from the misses' neighbours).
    dist = {}
    frontier = [k for k in hits if any((k[0] + a, k[1] + b) not in hits for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for k in frontier:
        dist[k] = 1
    while frontier:
        nxt = []
        for (i, j) in frontier:
            for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                k = (i + a, j + b)
                if k in hits and k not in dist:
                    dist[k] = dist[(i, j)] + 1
                    nxt.append(k)
        frontier = nxt
    inv = root.matrix_world.inverted()
    bm = bmesh.new()
    topv, basev, zof = {}, {}, {}
    for k, (loc, nrm) in hits.items():
        f = min(1.0, max(0.0, (dist[k] - 0.6) / 3.0)) ** 0.5
        lump = 0.72 + 0.32 * mnoise.noise(loc * 3.2) + 0.18 * mnoise.noise(loc * 9.0 + Vector((3, 1, 7)))
        t = thickness * f * max(0.2, lump) + 0.004
        topv[k] = bm.verts.new(inv @ (loc + nrm * t))
        basev[k] = inv @ (loc + nrm * 0.002)
        zof[k] = loc.z
    # Overhang: one grid step past the edge the snow droops over as a thin lip (hidden where a part blocked the ray).
    ring = {(i + a, j + b) for (i, j) in hits for a in (-1, 0, 1) for b in (-1, 0, 1)} - set(hits)
    for (i, j) in ring:
        nb = [hits[(i + a, j + b)][0] for a in (-1, 0, 1) for b in (-1, 0, 1) if (i + a, j + b) in hits]
        z = sum(v.z for v in nb) / len(nb)
        p = Vector((x0 + (x1 - x0) * i / (nx - 1), y0 + (y1 - y0) * j / (ny - 1), z - thickness * 0.15))
        topv[(i, j)] = bm.verts.new(inv @ (p + Vector((0, 0, thickness * 0.2 + 0.006))))
        basev[(i, j)] = inv @ p
        zof[(i, j)] = z
    faces = 0
    for (i, j) in list(topv):
        quad = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
        if all(q in topv for q in quad) and any(q in hits for q in quad):
            zs = [zof[q] for q in quad]
            if max(zs) - min(zs) < spacing * 2.5:
                bm.faces.new([topv[q] for q in quad])
                faces += 1
    for k in [k for k, v in topv.items() if not v.link_faces]:
        bm.verts.remove(topv.pop(k))
    if not faces:
        bm.free()
        return None
    # Skirt: drop every boundary edge down to the surface so the pillow has a visible edge, not a floating sheet.
    lookup = {v: k for k, v in topv.items()}
    for e in [e for e in bm.edges if e.is_boundary]:
        a, b = e.verts
        f = e.link_faces[0]
        ka, kb = lookup[a], lookup[b]
        ba, bb_ = bm.verts.new(basev[ka]), bm.verts.new(basev[kb])
        # Keep the skirt's winding consistent with the top face (outward-facing).
        loop = next(l for l in f.loops if l.edge is e)
        if loop.vert is a:
            bm.faces.new((b, a, ba, bb_))
        else:
            bm.faces.new((a, b, bb_, ba))
    bmesh.ops.remove_doubles(bm, verts=[v for v in bm.verts if v not in lookup], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(f"Snow{target.name}")
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = True
    me.materials.append(mat)
    o = bpy.data.objects.new(f"Snow{target.name}", me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = root
    return o


SNOW_ON = [("Hull", 0.16), ("Locker", 0.11), ("LockerB", 0.11), ("LightBar", 0.07), ("Hatch", 0.07),
           ("FrontBumper", 0.08), ("RearBumper", 0.07), ("Step-1", 0.04), ("Step1", 0.04)]


# ---------------------------------------------------------------- damage


def _parts(root):
    return {o.name: o for o in [root] + list(root.children_recursive)}


def _dead_lamp():
    m = _material("LampDead", (0.06, 0.06, 0.065), 0.2, 0.3)
    return m


def _set_material(obj, mat):
    if obj is not None and obj.type == "MESH":
        obj.data.materials.clear()
        obj.data.materials.append(mat)


def _bend_antenna(obj):
    """Kink the antenna a third of the way up and fold the top over."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    zs = [v.co.z for v in bm.verts]
    zmin, zmax = min(zs), max(zs)
    pivot = zmin + (zmax - zmin) * 0.38
    for cut in (pivot, pivot + (zmax - zmin) * 0.12):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, cut), plane_no=(0, 0, 1))
    rot1 = Matrix.Rotation(math.radians(28), 4, Vector((0.8, -0.6, 0)).normalized())
    rot2 = Matrix.Rotation(math.radians(62), 4, Vector((0.8, -0.6, 0)).normalized())
    c1, c2 = Vector((0, 0, pivot)), Vector((0, 0, pivot + (zmax - zmin) * 0.12))
    c2r = c1 + rot1 @ (c2 - c1)
    for v in bm.verts:
        if v.co.z > c2.z + 1e-5:
            v.co = c2r + rot2 @ (v.co - c2)
        elif v.co.z > c1.z + 1e-5:
            v.co = c1 + rot1 @ (v.co - c1)
    bm.to_mesh(me)
    bm.free()


def _hang(obj, pivot_local, axis, degrees, drop=0.0):
    """Rotate obj about a point given in its own local frame (keeps any parent relationship)."""
    b = obj.matrix_basis.copy()
    pw = b @ Vector(pivot_local)
    r = Matrix.Rotation(math.radians(degrees), 4, axis)
    obj.matrix_basis = Matrix.Translation((0, 0, -drop)) @ Matrix.Translation(pw) @ r @ Matrix.Translation(-pw) @ b


def _scar(root, locker, mat):
    """The bare footprint where a locker was torn off: a dark plate flush on the roof plus bolt stubs."""
    bb = [locker.matrix_world @ Vector(v) for v in locker.bound_box]
    x0, x1 = min(v.x for v in bb), max(v.x for v in bb)
    y0, y1 = min(v.y for v in bb), max(v.y for v in bb)
    z = min(v.z for v in bb)
    inv = root.matrix_world.inverted()
    bm = bmesh.new()
    vs = [bm.verts.new(inv @ Vector(p)) for p in ((x0, y0, z + 0.004), (x1, y0, z + 0.004), (x1, y1, z + 0.004),
                                                   (x0, y1, z + 0.004))]
    bm.faces.new(vs)
    for bx, by in ((x0 + 0.08, y0 + 0.08), (x1 - 0.08, y0 + 0.08), (x1 - 0.08, y1 - 0.08), (x0 + 0.08, y1 - 0.08)):
        r = bmesh.ops.create_cube(bm, size=0.06)
        for v in r["verts"]:
            v.co = inv @ (Vector((bx, by, z + 0.03)) + v.co)
    me = bpy.data.meshes.new("LockerScar")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    o = bpy.data.objects.new("LockerScar", me)
    o.visible_shadow = False
    bpy.context.scene.collection.objects.link(o)
    o.parent = root
    return o


def _remove(obj):
    if obj is not None:
        for c in list(obj.children_recursive):
            bpy.data.objects.remove(c)
        bpy.data.objects.remove(obj)


def _damage(root, damage, finish):
    parts = _parts(root)
    get = parts.get
    dead = _dead_lamp()
    if damage >= 2:
        for name, o in parts.items():
            if name.startswith("Spot") and name[4:].isdigit():
                _set_material(o, dead)
        _set_material(get("Headlamp1"), dead)
        if get("Antenna"):
            _bend_antenna(get("Antenna"))
            _remove(get("AntennaTip"))  # the Mk I's whip has a separate tip cap that would stay floating where the top was
        _remove(get("WheelFRCap"))
        rb = get("RearBumper")
        if rb:
            half = max(abs(v[0]) for v in rb.bound_box)
            _hang(rb, (-half + 0.05, 0, 0.05), "Y", 9, drop=0.02)
            rb.rotation_euler.z += math.radians(4)
    if damage >= 3:
        for name in ("Tail-1", "Tail1", "Beacon"):
            _set_material(get(name), dead)
        dish = get("Dish")
        if dish:
            tilt = Matrix.Rotation(math.radians(21), 4, Vector((1, 0.4, 0)).normalized())
            for c in dish.children:
                c.matrix_basis = tilt @ c.matrix_basis
            bowl = _parts(root).get("DishBowl")
            if bowl:
                bowl.matrix_basis = (Matrix.Translation(bowl.matrix_basis.translation)
                                     @ Matrix.Rotation(math.radians(-38), 4, "X")
                                     @ Matrix.Translation(-bowl.matrix_basis.translation) @ bowl.matrix_basis)
        lk = get("Locker")
        if lk:
            _scar(root, lk, _material("Scar", (0.09, 0.085, 0.08), 0.3, 0.6))
            _remove(lk)
        _remove(get("Wiper"))
        # Burnt-out rear: everything behind the back axle goes sooty (not the recoloured Owner stripes).
        for o in _parts(root).values():
            if o.type == "MESH" and _centre(o).y < -1.5 and not o.name.startswith("Snow"):
                _swap(o, lambda m: None if m.name in ("Owner", "LampDead") or m.name.endswith("Paint")
                      else _variant(m, "soot", SOOT, 0.82, 0.8, 0.0, kill_emission=True))


def apply_parts(root, finish="clean", damage=0):
    """Everything outside the hull bake, by part name (parts that don't exist on this tier are skipped)."""
    if finish not in FINISHES:
        raise ValueError(f"finish must be one of {FINISHES}")
    damage = max(0, min(3, int(damage)))
    # Geometry changes first, so the snow lands on what is left.
    _damage(root, damage, finish)
    parts = _parts(root)
    # Glass: dusty / muddy / frosted panes with wiper arcs, a cracked side window, a smashed windscreen.
    for name in ("Windscreen", "SideGlass-1", "SideGlass1", "SideWindow-1", "SideWindow1"):
        o = parts.get(name)
        if o is None:
            continue
        crack = damage >= 1 and name == "SideWindow1"
        smash = damage >= 3 and name == "Windscreen"
        if finish != "clean" or crack or smash:
            _bake_glass(o, finish, crack=crack, smash=smash)
    if finish != "clean":
        _tint_parts(root, finish)
    if finish == "snow":
        snow = _material("Snow", SNOW, 0.0, 0.62)
        for name, t in SNOW_ON:
            o = _parts(root).get(name)
            if o is not None:
                _snow_cap(o, root, t, spacing=0.05 if name == "Hull" else 0.035, mat=snow)
    return root
