"""Build the animated 'colony pod drop' prototype page from the v2 Colony Hub generator.

The level-1 scene is split into layers (static ground, scorch, petals, legs, pod,
ramp, capsules, beacons, drop ship) so the page script can animate each one.

Usage: python3 anim_build.py OUT_HTML
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_colony_hub as G  # noqa: E402

OUT = sys.argv[1]
X, Y = G.HUB2
POD_Z = 6.5
SHIP_Z = 64
PETALS = (100, 145, 190, 235, 280, 325, 10)
CAPSULES = [(76, 34), (30, 66), (80, 64)]
BEACONS = [(28, 26), (84, 26), (28, 84), (86, 84)]


def body(sc):
    return "".join(sc.ground) + "".join(sc.shadows) + "".join(s for _, s in sorted(sc.objs, key=lambda t: t[0])) + "".join(sc.sky)


def ang(px, py):
    return math.degrees(math.atan2(py - Y, px - X)) % 360


# ---- static plot (background, tile, trees, grass) ----
static = G.FedScene(11)
trees = [(10, 12, 1.1), (16, 52, 1.0), (8, 74, 1.2), (14, 98, 1.0), (30, 106, 1.1), (52, 104, 0.9), (60, 14, 1.0),
         (78, 10, 1.2), (98, 16, 1.0), (108, 40, 1.1), (112, 62, 0.9), (104, 112, 1.0), (40, 18, 0.9), (6, 34, 0.8),
         (24, 90, 0.9), (70, 104, 1.1), (96, 52, 0.9), (98, 92, 1.0), (86, 104, 0.8)]
G.wild(static, trees, [(28, 76, 26, 72)])
vig = f'<rect width="{G.W}" height="{G.H}" fill="url(#vig)"/>'
full = static.render()
head, _, _ = full.rpartition(vig)          # everything before the vignette
head = head.replace('<svg xmlns="http://www.w3.org/2000/svg"', '<svg xmlns="http://www.w3.org/2000/svg" id="stage" role="img" aria-label="Animated drop of a Federation colony pod onto a grassy plot"', 1)
head = head.replace(f'width="{G.W}" height="{G.H}">', 'preserveAspectRatio="xMidYMid meet">', 1)

layers = []


def layer(lid, sc, extra=""):
    layers.append(f'<g id="{lid}"{extra}>{body(sc)}</g>')


sc = G.FedScene(1)
sc.disc(X, Y, 17, "#3b4a2e", opacity=0.45)
sc.disc(X, Y, 12, "#2e3a24", opacity=0.45)
layer("scorch", sc, ' opacity="0"')

for i, deg in enumerate(PETALS):
    sc = G.FedScene(1)
    sc.petals(X, Y, 11.5, 18, (deg,))
    layer(f"petal{i}", sc, f' data-angle="{deg}"')

for i, (cx, cy) in enumerate(CAPSULES):
    sc = G.FedScene(1)
    sc.disc(cx, cy, 4.2, "#3b4a2e", opacity=0.55)
    layer(f"capscorch{i}", sc, ' opacity="0"')

# the pod is animated in parts: legs, saucer, command capsule
saucer = G.FedScene(1)
saucer.saucer(X, Y, 10, 3.2, POD_Z, depth=100)
layers.append(f'<g id="podshadow" opacity="0">{"".join(saucer.shadows)}</g>')
saucer.shadows = []
legs = G.FedScene(1)
legs.legs(X, Y, 10, POD_Z)
layer("legs", legs)
layer("saucer", saucer)
capsule = G.FedScene(1)
capsule.cylg(X, Y, 5, 4.6, z=POD_Z + 3.2, band=(3.6,), windows=(1.8,), cap="dome", shadow=False, depth=100.1)
layer("capsule", capsule)
ramp = G.FedScene(1)
ramp.ramp(X, Y, 8.5, 19, POD_Z, deg=0, w=2.4)
layer("ramp", ramp)

for i, (cx, cy) in enumerate(CAPSULES):
    sc = G.FedScene(1)
    sc.cylg(cx, cy, 2.2, 4.4, glowband=(1.2,), cap="hull")
    layer(f"cap{i}", sc, f' data-angle="{ang(cx, cy):.1f}"')

for i, (bx, by) in enumerate(BEACONS):
    sc = G.FedScene(1)
    sc.beacon(bx, by)
    layer(f"beacon{i}", sc, f' data-angle="{ang(bx, by):.1f}"')

layers.append('<g id="fx"></g>')
ship = G.FedScene(1)
ship.dropship(X, Y, SHIP_Z, s=1.9)
layer("ship", ship)

svg = head + "".join(layers) + vig + "</svg>"

geo = {
    "CX": G.CX, "CY": G.CY, "S": G.S, "hub": [X, Y], "podZ": POD_Z, "shipZ": SHIP_Z,
    "petals": list(PETALS),
    "capsules": [[cx, cy, round(ang(cx, cy), 1)] for cx, cy in CAPSULES],
    "beacons": [[bx, by, round(ang(bx, by), 1)] for bx, by in BEACONS],
    "rampFoot": [X + 19, Y], "rampTop": [X + 8.5, Y],
}

tpl = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "anim_page.html")).read()
html = tpl.replace("/*GEO*/", json.dumps(geo)).replace("<!--SVG-->", svg)
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
