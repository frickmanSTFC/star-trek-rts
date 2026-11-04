"""Preview driver for rover_finish.py: build the survey rover with build_vehicles.py's functions (its main() is not
run), apply a finish + damage level and render previews on a matching ground.

  /root/bpyenv/bin/python blender/render_finish.py --finish snow --damage 0 --res 640 --samples 24
  python3.11 blender/render_finish.py --finish dust --damage 2 --shots front34,rear34 --out blender/previews/vehicles/finish
  ... --glb /tmp/rover.glb      also export the result (checks the materials survive glTF)

Writes <out>/<finish>-d<damage>.png for the front34 shot and <finish>-d<damage>-<shot>.png for the others. Any extra
flags (e.g. --tier 3) are passed on to build_vehicles' own argument parsing.
"""
import inspect
import math
import os
import re
import sys

import bpy  # first: importing bpy puts bmesh and mathutils on the path when run as a module
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rover_finish  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


FINISH = arg("--finish", "clean")
DAMAGE = int(arg("--damage", "0"))
RES = int(arg("--res", "960"))
SAMPLES = int(arg("--samples", "48"))
BAKE_RES = int(arg("--bake-res", "1024"))
SHOTS = arg("--shots", "front34").split(",")
OUT = arg("--out", os.path.join(HERE, "previews", "vehicles", "finish"))
GLB = arg("--glb")

# Ground per finish: (colour, roughness).
GROUND = {
    "clean": ((0.36, 0.27, 0.18), 0.95),   # build_vehicles' sand
    "dust": ((0.5, 0.33, 0.15), 0.95),     # ochre
    "mud": ((0.07, 0.045, 0.028), 0.5),   # dark, wet
    "snow": ((0.76, 0.8, 0.86), 0.7),
}

SHOT_CAMS = {
    "front34": (Vector((7.5, 8.5, 3.2)), 40),
    "rear34": (Vector((-7.5, -8.0, 3.6)), 40),
    "side": (Vector((10.5, 0.4, 1.9)), 38),
    "game": (Vector((0.0, 13.0, 15.5)), 30),
    "rear34r": (Vector((7.5, -8.0, 3.6)), 40),
}


def load_build_vehicles():
    """Exec build_vehicles.py into a namespace without its trailing main() call (and with a module name that skips an
    `if __name__ == "__main__"` guard, should it gain one). Its own flags see only what we pass through."""
    path = os.path.join(HERE, "build_vehicles.py")
    with open(path) as f:
        src = f.read()
    src = re.sub(r"^main\(\)\s*$", "", src, flags=re.M)
    ours = {"--finish", "--damage", "--res", "--samples", "--bake-res", "--shots", "--out", "--glb"}
    passthrough, i = [], 0
    while i < len(argv):
        if argv[i] in ours:
            i += 2
            continue
        passthrough.append(argv[i])
        i += 1
    saved = sys.argv
    sys.argv = [path] + passthrough
    ns = {"__name__": "build_vehicles", "__file__": path}
    try:
        exec(compile(src, path, "exec"), ns)
    finally:
        sys.argv = saved
    return ns


def build(ns):
    ns["reset"]()
    fn = ns["survey_rover"]
    params = inspect.signature(fn).parameters
    kwargs = {}
    if "tier" in params and arg("--tier"):
        kwargs["tier"] = int(arg("--tier"))
    return fn(**kwargs)


def preview(ns, root, folder):
    """build_vehicles.preview_scene's look (sky, sun, cameras) with a ground per finish and a choice of shots."""
    material, link = ns["material"], ns["link"]
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RES
    scene.render.resolution_y = int(RES * 2 / 3)
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = math.radians(28)
    sky.sun_rotation = math.radians(150)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.12
    scene.view_settings.exposure = -0.6 if FINISH != "snow" else -0.85
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(55), 0, math.radians(150))
    link(sun)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40)
    bm.to_mesh(ground)
    colour, rough = GROUND[FINISH]
    ground.materials.append(material(f"Ground_{FINISH}", colour, 0.0, rough))
    link(bpy.data.objects.new("Ground", ground))

    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    link(cam)
    scene.camera = cam
    target = Vector((0, 0, 1.3))
    os.makedirs(folder, exist_ok=True)
    for name in SHOTS:
        pos, lens = SHOT_CAMS[name]
        cam.location = pos
        cam.data.lens = lens
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        suffix = "" if name == "front34" else f"-{name}"
        tier = f"t{arg('--tier')}-" if arg("--tier") else ""
        scene.render.filepath = os.path.join(folder, f"{tier}{FINISH}-d{DAMAGE}{suffix}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)


def main():
    ns = load_build_vehicles()
    root = build(ns)
    # Fit the bake to this tier: hull bottom/top from its bounds, one arch per left wheel (pivot y, hub height).
    hull = bpy.data.objects["Hull"]
    zs = [(hull.matrix_world @ Vector(c)).z for c in hull.bound_box]
    wheels = [o for o in root.children_recursive if o.type == "EMPTY" and re.fullmatch(r"Wheel[FMR]L", o.name)]
    arches = [(o.location.y, o.location.z) for o in wheels] or None
    rover_finish.weather_hull(hull, FINISH, DAMAGE, res=BAKE_RES, arches=arches, z_range=(min(zs), max(zs)))
    rover_finish.apply_parts(root, FINISH, DAMAGE)
    if GLB:
        ns["export"](root, GLB)
    preview(ns, root, OUT)


main()
