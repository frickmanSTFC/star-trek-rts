"""Copy the marks renders (blender/previews/marks/<id>/*.png) into this page's img/ as JPEGs (system python + PIL).

  python3 docs/mockups/building-marks/make_images.py
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
SIZES = {"lineup": 1920, "hero": 960, "game": 960, "close": 960, "night": 960}


def main():
    out = os.path.join(HERE, "img")
    os.makedirs(out, exist_ok=True)
    total = 0
    for b in sys.argv[1:] or ["vehicle_bay", "science_lab", "foundry"]:
        src = os.path.join(REPO, "blender", "previews", "marks", b)
        for f in sorted(os.listdir(src)):
            if not f.endswith(".png"):
                continue
            kind = f[:-4].split("-")[-1]
            im = Image.open(os.path.join(src, f)).convert("RGB")
            w = SIZES.get(kind, 900)
            if im.width > w:
                im = im.resize((w, round(im.height * w / im.width)), Image.LANCZOS)
            path = os.path.join(out, f[:-4] + ".jpg")
            im.save(path, quality=86, optimize=True, progressive=True)
            total += os.path.getsize(path)
            print(path, os.path.getsize(path))
    print("total", total)


main()
