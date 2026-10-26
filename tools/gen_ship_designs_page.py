"""Write ship-designs.html: a shareable page comparing each faction's starships, built from the data files.

Usage: python3 tools/gen_ship_designs_page.py [OUT_HTML]
Reads data/ships.json, data/factions.json, data/eras.json and data/buildings.json (shipyard caps),
plus the doctrine text in tools/faction_meta.py, and fills tools/ship_designs_page.html.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, NAMES, SHIP_DOCTRINE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "ship-designs.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


S = load("ships.json")
FAC = {f["id"]: f for f in load("factions.json")["factions"]}
ERAS = {e["id"]: e for e in load("eras.json")["eras"]}
B = load("buildings.json")

ERA_SHORT = {"ent": "Enterprise", "tos": "TOS", "tng": "TNG", "dominion_war": "Dominion War", "voy": "Voyager",
             "picard": "Picard", "far_future": "32nd century", "kelvin": "Kelvin"}
HULL_LABEL = {"heavy_cruiser": "Heavy cruiser"}
YARDS = [(3, "Drydock"), (5, "Shipyard"), (6, "Spacedock"), (8, "Fleet yards")]  # buildings.json shipyard_tiers


def label(s):
    return HULL_LABEL.get(s, s.replace("_", " ").capitalize())


def words(s):
    return s.replace("_", " ")


hull_size = {h["id"]: h["size"] for h in S["hulls"]}
hulls = []
for h in sorted(S["hulls"], key=lambda h: (h["size"], h["hp"])):
    notes = []
    if h.get("hangar"):
        notes.append(f"hangar {h['hangar']}")
    if h.get("roles"):
        notes.append("roles: " + ", ".join(h["roles"]))
    if h.get("limit_per_player"):
        notes.append(f"{h['limit_per_player']} per player")
    hulls.append({"id": h["id"], "label": label(h["id"]), "size": h["size"], "hp": h["hp"], "speed": h["speed"], "crew": h["crew"],
                  "slots": [h["slots"]["weapon"], h["slots"]["defense"], h["slots"]["engine"], h["slots"]["utility"]],
                  "notes": notes, "yard": next(n for cap, n in YARDS if h["size"] <= cap)})

module_ids = {m for v in S["modules"].values() for m in v}


def weapon_modules(w):
    return [m for m in module_ids if m == w or m.startswith(w + "_")]


classes = []
for c in S["classes"]:
    classes.append({"name": c["name"], "faction": c["faction"], "hull": c["hull"], "era": c["era"], "source": c["source"],
                    "special": [words(x.strip()) for x in c.get("special", "").split(",") if x.strip()],
                    "role": words(c.get("role", ""))})

era_ids = [e for e in ERA_SHORT if any(c["era"] == e for c in classes)]
eras = [{"id": e, "short": ERA_SHORT[e], "name": ERAS[e]["name"], "years": ERAS[e]["years"]} for e in era_ids]


def yard_cap(f):
    top = B["faction_lines"][f]["shipyards"][-1]["stats"]["hull_cap"]
    return {"titan": "8 (titan)", "any_acquired": "any acquired hull"}.get(top, str(top))


factions = []
for f in FACTIONS:
    meta = FAC[f]
    mine = [c for c in classes if c["faction"] == f]
    flag = max(mine, key=lambda c: (hull_size[c["hull"]], era_ids.index(c["era"])))
    weapons = []
    for w in meta["weapons"]:
        mods = weapon_modules(w)
        weapons.append({"label": words(w).replace(" forbidden", " (forbidden)"), "modules": mods})
    factions.append({
        "id": f, "name": NAMES[f], "playstyle": meta["playstyle"], "doctrine": SHIP_DOCTRINE[f], "weakness": meta["weakness"],
        "weapons": weapons, "eras": [e for e in meta["eras"] if e in era_ids], "count": len(mine), "flagship": flag["name"],
        "flagship_hull": label(flag["hull"]), "yard": yard_cap(f),
        "hulls_used": len({c["hull"] for c in mine}),
    })

others = []
for c in classes:
    if c["faction"] not in FACTIONS:
        others.append({**c, "faction_name": FAC.get(c["faction"], {}).get("name", words(c["faction"]).title())})

users = {}
for f in FACTIONS:
    for w in FAC[f]["weapons"]:
        for m in weapon_modules(w):
            users.setdefault(m, []).append(f)
modules = [{"slot": slot.capitalize(), "items": [{"id": m, "label": words(m), "users": users.get(m, [])} for m in mods]}
           for slot, mods in S["modules"].items()]

data = {
    "factions": factions, "hulls": hulls, "classes": [c for c in classes if c["faction"] in FACTIONS], "others": others,
    "eras": eras, "modules": modules,
    "counts": {"classes": len(classes), "playable_classes": sum(f["count"] for f in factions), "hulls": len(hulls),
               "modules": len(module_ids)},
    "hullLabels": {h["id"]: h["label"] for h in hulls},
    "unusedEras": [ERA_SHORT[e] for e in ERA_SHORT if e not in era_ids],
}
tpl = open(os.path.join(HERE, "ship_designs_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
