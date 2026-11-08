"""Contact sheets for the hostile-worlds building previews (system python with PIL; no bpy).

  python3 blender/hostile_sheet.py [blender/previews/hostile]

1. folds each building's pieces<NN>.png renders into one 2 x 2 <id>-pieces.png (and removes the singles);
2. writes hostile-sheet.png: per building hero | night, game | close, then the pieces sheet; labels on each tile.
"""
import glob
import os
import sys

from PIL import Image, ImageDraw

FOLDER = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "previews", "hostile")
IDS = ["pressure_dome", "life_support_plant", "seal_collar"]
BG = (14, 16, 20)


def label(img, text):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 8 * len(text) + 14, 22), fill=(0, 0, 0))
    d.text((7, 5), text, fill=(235, 235, 235))
    return img


def pieces_sheet(bid):
    singles = sorted(glob.glob(os.path.join(FOLDER, f"{bid}-pieces[0-9][0-9].png")))
    out = os.path.join(FOLDER, f"{bid}-pieces.png")
    if not singles:
        return out if os.path.exists(out) else None
    tiles = [label(Image.open(p).convert("RGB"), os.path.basename(p)[len(bid) + 1:-4]) for p in singles]
    w, h = tiles[0].size
    sheet = Image.new("RGB", (2 * w, ((len(tiles) + 1) // 2) * h), BG)
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % 2) * w, (i // 2) * h))
    sheet.save(out)
    for p in singles:
        os.remove(p)
    return out


def main():
    rows = []
    for bid in IDS:
        pieces = pieces_sheet(bid)
        shots = [os.path.join(FOLDER, f"{bid}-{s}.png") for s in ("hero", "night", "game", "close")]
        shots = [p for p in shots if os.path.exists(p)]
        if not shots:
            continue
        tiles = [label(Image.open(p).convert("RGB"), os.path.basename(p)[:-4]) for p in shots]
        if pieces:
            tiles.append(label(Image.open(pieces).convert("RGB"), os.path.basename(pieces)[:-4]))
        rows.append(tiles)
    W = 1920
    placed = []
    y = 0
    for tiles in rows:   # each building starts a new row
        x, row_h = 0, 0
        for t in tiles:
            c = t.resize((W // 2, round(t.size[1] * (W // 2) / t.size[0])))
            placed.append((c, x, y))
            row_h = max(row_h, c.size[1])
            x += W // 2
            if x >= W:
                x, y, row_h = 0, y + row_h, 0
        y += row_h
        row_h = 0
    H = y + row_h
    sheet = Image.new("RGB", (W, H), BG)
    for c, px, py in placed:
        sheet.paste(c, (px, py))
    out = os.path.join(FOLDER, "hostile-sheet.png")
    sheet.save(out)
    print("wrote", out, sheet.size)


main()
