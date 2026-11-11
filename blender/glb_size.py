"""Print what takes the room in a .glb (images vs geometry buffers), plain python3:  python3 blender/glb_size.py X.glb"""
import json
import struct
import sys

for path in sys.argv[1:]:
    with open(path, "rb") as f:
        data = f.read()
    n = struct.unpack_from("<I", data, 12)[0]
    gltf = json.loads(data[20:20 + n])
    views = gltf.get("bufferViews", [])
    img = sum(views[i["bufferView"]]["byteLength"] for i in gltf.get("images", []) if "bufferView" in i)
    total = len(data)
    print(f"{path}: {total / 1e6:.2f} MB, images {img / 1e6:.2f} MB ({len(gltf.get('images', []))}), "
          f"geometry + json {(total - img) / 1e6:.2f} MB, {len(gltf.get('nodes', []))} nodes, "
          f"{len(gltf.get('materials', []))} materials")
