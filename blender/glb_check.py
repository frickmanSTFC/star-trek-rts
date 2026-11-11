"""Small helpers shared by the drone / drone pad / relay satellite / mission props builders (2026-11-11):

  verify_glb(path, required, roots=None)   re-import an export into an empty scene and list its node tree
                                           (name, type, parent, Godot position, material names, triangles); exits
                                           non-zero when a required node name is missing or renamed (".001").
  contact_sheet(folder, rows, out, tw, th) system python + PIL: tiles [(file, label), ...] rows into one image
                                           (bpy's Python has no PIL, so the builders call this from their --sheet
                                           branch before importing bpy).

verify_glb needs bpy (import bpy first in the caller); contact_sheet needs only PIL.
"""
import os
import sys


def contact_sheet(folder, rows, out, tw=480, th=320):
    from PIL import Image, ImageDraw
    cols = max(len(r) for r in rows)
    sheet = Image.new("RGB", (tw * cols, th * len(rows)), (16, 18, 22))
    draw = ImageDraw.Draw(sheet)
    for r, row in enumerate(rows):
        for c, (name, label) in enumerate(row):
            path = os.path.join(folder, name)
            if not os.path.exists(path):
                continue
            im = Image.open(path).convert("RGB")
            # fit inside the tile keeping the aspect, centred
            s = min(tw / im.width, th / im.height)
            im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
            sheet.paste(im, (c * tw + (tw - im.width) // 2, r * th + (th - im.height) // 2))
            draw.rectangle((c * tw + 8, r * th + 8, c * tw + 16 + 7 * len(label), r * th + 26), fill=(8, 10, 14))
            draw.text((c * tw + 12, r * th + 11), label.upper(), fill=(235, 235, 240))
    sheet.save(out, optimize=True)
    print("wrote", out, sheet.size)


def godot(v):
    """Blender (x, y, z) -> Godot (x, z, -y)."""
    return (round(v[0], 3), round(v[2], 3), round(-v[1] + 0.0, 3))


def verify_glb(path, required, show=True):
    """Import `path` into an empty scene, print its nodes and fail if any `required` name is missing."""
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    objs = list(bpy.data.objects)
    names = {o.name for o in objs}
    tris = 0
    print(f"verify {os.path.basename(path)}: {os.path.getsize(path) / 1e6:.2f} MB, {len(objs)} nodes", flush=True)

    def walk(o, depth):
        nonlocal tris
        t = 0
        mats = ""
        if o.type == "MESH":
            t = sum(len(p.vertices) - 2 for p in o.data.polygons)
            tris += t
            mats = ", ".join(s.material.name for s in o.material_slots if s.material)
        if show:
            pos = godot(o.matrix_world.translation)
            extra = f"  {t} tris  [{mats}]" if o.type == "MESH" else ""
            print(f"  {'  ' * depth}{o.name} ({o.type.lower()}) at {pos}{extra}", flush=True)
        for c in sorted(o.children, key=lambda c: c.name):
            walk(c, depth + 1)

    for o in sorted((o for o in objs if o.parent is None), key=lambda o: o.name):
        walk(o, 0)
    print(f"  {tris} triangles in all", flush=True)
    bad = sorted(n for n in names if "." in n)
    missing = sorted(set(required) - names)
    if bad:
        print("  renamed on import (duplicate names?):", ", ".join(bad), flush=True)
    if missing:
        sys.exit(f"verify {path}: missing {', '.join(missing)}")
    print(f"  all {len(set(required))} required names present", flush=True)
    return {o.name: o for o in objs}, tris


def join_pivots(root, pivots, keep, body="Body"):
    """build_satellite.py's join_for_game, safe for nested pivots (Arm -> Tilt -> Rotor): modifiers applied, one mesh
    per pivot. The root's own parts -> `body` (origin at the root); each pivot empty in `pivots` is replaced by the
    joined mesh of its parts, named like the pivot, same transform and parent; `keep` meshes stay as they are
    (re-parented onto the joined mesh). Every part first gets one UV layer called "UVMap" (CLAUDE.md drop ship
    gotcha). Deepest pivots go first and the view layer is updated after each step, so no world matrix is stale.
    Empties that end up with no children and are not pivots/keep are left for the caller to prune."""
    import bpy
    from mathutils import Matrix
    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    dg = bpy.context.evaluated_depsgraph_get()
    for o in meshes:
        if o.modifiers:
            me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            o.modifiers.clear()
            o.data = me
    for o in meshes:
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        o.data.uv_layers[0].name = "UVMap"
        while len(o.data.uv_layers) > 1:
            o.data.uv_layers.remove(o.data.uv_layers[1])

    def pivot_of(o):
        p = o.parent
        while p is not None and p is not root and p.name not in pivots:
            p = p.parent
        return p or root

    def depth(o):
        d = 0
        while o is not None and o is not root:
            o, d = o.parent, d + 1
        return d

    groups = {}
    for o in meshes:
        if o.name not in keep:
            groups.setdefault(pivot_of(o), []).append(o)
    for pivot in sorted(groups, key=depth, reverse=True):
        objs = groups[pivot]
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
            o.select_set(True)
        target = objs[0]
        bpy.context.view_layer.objects.active = target
        if len(objs) > 1:
            bpy.ops.object.join()
        bpy.context.view_layer.update()
        frame = pivot.matrix_world.copy()
        target.data.transform(frame.inverted() @ target.matrix_world)
        if pivot is root:
            target.parent = root
            target.matrix_parent_inverse = Matrix.Identity(4)
            target.matrix_basis = Matrix.Identity(4)
            target.name = body
        else:
            name = pivot.name
            pivot.name = name + "_pivot"
            target.parent = pivot.parent
            target.matrix_parent_inverse = Matrix.Identity(4)
            target.matrix_basis = pivot.matrix_basis.copy()
            bpy.context.view_layer.update()
            for c in list(pivot.children):
                mw = c.matrix_world.copy()
                c.parent = target
                c.matrix_parent_inverse = Matrix.Identity(4)
                c.matrix_world = mw
                bpy.context.view_layer.update()
            bpy.data.objects.remove(pivot)
            target.name = name
        target.data.name = target.name
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    names = sorted(o.name for o in root.children_recursive)
    missing = sorted(set(list(keep) + list(pivots) + [body]) - set(names))
    if missing:
        sys.exit("missing parts: " + ", ".join(missing))


def prune_empties(root, keep):
    """Remove empties (helper frames) that have no children and are not in `keep`, repeatedly."""
    import bpy
    while True:
        bare = [o for o in root.children_recursive if o.type == "EMPTY" and o.name not in keep and not o.children]
        if not bare:
            break
        for o in bare:
            bpy.data.objects.remove(o)
