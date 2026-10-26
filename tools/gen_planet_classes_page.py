"""Write planet-classes.html: a shareable page for the planet classes and other celestial bodies.

Usage: python3 tools/gen_planet_classes_page.py [OUT_HTML]
Reads data/planet_classes.json, resources.json, tech_tree.json, hazards.json and buildings.json, plus
the world text in tools/faction_meta.py, and fills tools/planet_classes_page.html.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, NAMES, WORLD_STANCE  # noqa: E402
from tech_access import reachable  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "planet-classes.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


P = load("planet_classes.json")["classes"]
RES = {r["id"]: r for r in load("resources.json")["resources"]}
techs = {t["id"]: t for t in load("tech_tree.json")["techs"]}
HAZ = {h["id"]: h["name"] for h in load("hazards.json")["hazards"]}
B = load("buildings.json")
pids = [p["id"] for p in P]
pname = {p["id"]: p["name"].split(" — ")[0] for p in P}

COLONY = {"full": ("Full colony", "settle"), "domed": ("Domed colony", "settle"), "adapted": ("Adapted colony", "settle"),
          "floating_or_submerged": ("Floating or submerged colony", "settle"), "outpost_or_dome": ("Outpost or dome", "outpost"),
          "shielded_outpost": ("Shielded outpost", "outpost"), "mining_habitat": ("Mining habitat", "outpost"),
          "orbital_refinery": ("Orbital refinery", "orbital"), "harvest_only": ("Harvest only", "harvest"), "salvage_only": ("Salvage only", "harvest"),
          "none": ("No colony", "harvest")}


def words(s):
    return s.replace("_", " ")


# where each resource is found, by either file
in_planets = {r: {p["id"] for p in P if r in p["yields"]} for r in RES}
in_resources = {r: {f for f in RES[r].get("found_on", []) if f in pids} for r in RES}

classes = []
for p in P:
    label, group = COLONY.get(p["colony"], (words(p["colony"]), "outpost"))
    classes.append({
        "id": p["id"], "name": p["name"], "short": pname[p["id"]], "subtitle": p["name"].split(" — ")[1] if " — " in p["name"] else "",
        "colony": label, "group": group, "hab": p["habitability"], "source": p["source"],
        "yields": [{"id": y, "name": RES[y]["name"], "tier": RES[y]["tier"]} for y in p["yields"]],
        "alsoFound": [{"id": r, "name": RES[r]["name"], "tier": RES[r]["tier"]} for r in RES if p["id"] in in_resources[r] and p["id"] not in in_planets[r]],
        "hazards": [HAZ.get(h, words(h)) for h in p.get("hazards", [])], "effects": [words(e) for e in p.get("effects", [])],
        "terraform": pname.get(p.get("terraform_to"), ""), "genesis": bool(p.get("genesis_target")),
        "natives": p.get("natives_chance"), "example": p.get("example", ""), "notes": p.get("notes", ""),
        "requiresTech": techs[p["requires_tech"]]["name"] if p.get("requires_tech") in techs else words(p.get("requires_tech", "")),
    })

# resource x class map: 2 = both files agree, 1 = only planet_classes.json, -1 = only resources.json
res_rows = []
for r, info in RES.items():
    a, b = in_planets[r], in_resources[r]
    if not a and not b:
        continue
    res_rows.append({"id": r, "name": info["name"], "tier": info["tier"],
                     "cells": {pid: (2 if pid in a and pid in b else 1 if pid in a else -1 if pid in b else 0) for pid in pids if pid in a | b},
                     "elsewhere": [words(f) for f in RES[r].get("found_on", []) if f not in pids]})

# terraforming paths: planet_classes terraform_to, the arrows in tech effects, and Genesis targets
paths = []
for p in P:
    if p.get("terraform_to"):
        paths.append({"from": pname[p["id"]], "to": pname[p["terraform_to"]], "how": "planet_classes.json terraform_to", "tech": "", "kind": "terraform"})
letter = {pid[-1].upper(): pid for pid in pids if pid.startswith("class_")}
for t in techs.values():
    for a, b in re.findall(r"\b([A-Z])→([A-Z])\b", t.get("effect", "")):
        paths.append({"from": pname[letter[a]], "to": pname[letter[b]], "how": t["effect"], "tech": t["name"], "kind": "terraform"})
for p in P:
    if p.get("genesis_target") or (p["id"] == "nebula" and "nebula" in techs["genesis"].get("effect", "")):
        paths.append({"from": pname[p["id"]], "to": pname["class_m"], "how": techs["genesis"]["effect"], "tech": techs["genesis"]["name"], "kind": "genesis"})
# one row per change of class, naming every source that lists it
merged = {}
for x in paths:
    m = merged.setdefault((x["from"], x["to"]), {"from": x["from"], "to": x["to"], "kind": x["kind"], "techs": [], "planetData": False})
    if x["tech"]:
        m["techs"].append({"name": x["tech"], "how": x["how"]})
    else:
        m["planetData"] = True
paths = list(merged.values())
colonize = [{"tech": t["name"], "effect": t["effect"]} for t in techs.values() if "Colonize" in t.get("effect", "")]

# faction worlds: the resources its buildings consume, and the bodies that yield them
cost_of = {}
for f in FACTIONS:
    tot = {}
    blds = list(B["starting_sets"]["factions"][f]["buildings"]) + [x for ln in B["faction_lines"][f].values() for x in ln]
    for x in blds:
        for k, v in (x.get("cost") or {}).items():
            tot[k] = tot.get(k, 0) + v
    cost_of[f] = tot
COLONY_TECHS = [t for t in ("pressure_domes", "atmospheric_processors", "subspace_terraforming", "genesis") if t in techs]
factions = []
for f in FACTIONS:
    prefers, rules = WORLD_STANCE[f]
    needs = []
    for r, amount in sorted(cost_of[f].items(), key=lambda kv: -kv[1]):
        where = sorted(in_planets.get(r, set()) | in_resources.get(r, set()), key=pids.index)
        if r in RES and where and RES[r]["tier"] != "flow":
            needs.append({"name": RES[r]["name"], "where": [pname[w] for w in where]})
    reach = reachable(techs, f)
    factions.append({"id": f, "name": NAMES[f], "prefers": prefers, "rules": rules, "needs": needs[:5],
                     "colonyTechs": [{"name": techs[t]["name"], "ok": t in reach and f != "borg"} for t in COLONY_TECHS]})

checks = []
for r in res_rows:
    only_p = [pname[c] for c, v in r["cells"].items() if v == 1]
    only_r = [pname[c] for c, v in r["cells"].items() if v == -1]
    if only_p or only_r:
        bits = []
        if only_r:
            bits.append(f"resources.json also lists {', '.join(only_r)}")
        if only_p:
            bits.append(f"planet_classes.json also lists {', '.join(only_p)}")
        checks.append(f"{r['name']}: {'; '.join(bits)}.")
odd = sorted({f for r in RES.values() for f in r.get("found_on", []) if f not in pids and f not in ("synthesized", "trade", "generated", "colonies", "warp_byproduct")})
if odd:
    checks.append(f"resources.json names bodies that aren't planet classes: {', '.join(words(o) for o in odd)}.")
k_m = [x for x in paths if x["from"] == "Class K" and x["to"] == "Class M" and x["planetData"]]
k_l = [x for x in paths if x["from"] == "Class K" and x["to"] == "Class L"]
if k_m and k_l:
    checks.append("Class K terraforms straight to Class M in planet_classes.json, but Atmospheric Processors take it to Class L first.")
for p in P:
    if p.get("requires_tech") and p["requires_tech"] not in techs:
        checks.append(f"{pname[p['id']]} needs the tech {p['requires_tech']}, which isn't in the tech tree.")
loose = sorted({h for p in P for h in p.get("hazards", []) if h not in HAZ})
if loose:
    checks.append(f"Planet hazards aren't entries in hazards.json, so they have no effect or counter yet: {', '.join(words(h) for h in loose)}.")

data = {"classes": classes, "resources": res_rows, "paths": paths, "colonize": colonize, "factions": factions,
        "pids": pids, "pname": pname, "checks": checks,
        "groups": [["settle", "Colonies"], ["outpost", "Outposts"], ["orbital", "Orbital refineries"], ["harvest", "Harvest and salvage"]]}
tpl = open(os.path.join(HERE, "planet_classes_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
