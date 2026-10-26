"""Write faction-buildings.html: the shareable web page version of docs/faction-buildings.md.

Usage: python3 tools/gen_faction_buildings_page.py [OUT_HTML]
Reads data/buildings.json and tools/faction_meta.py, fills tools/faction_buildings_page.html.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, LINE_NAMES, NAMES, PLAYSTYLE, RES, ROLES  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "faction-buildings.html")
B = json.load(open(os.path.join(ROOT, "data", "buildings.json")))
ss = B["starting_sets"]["factions"]
fl = B["faction_lines"]
FIXED = ("deep_space", "command", "shipyards")


def cost(c):
    return [[RES.get(k, k), v] for k, v in (c or {}).items()]


def units(c):
    return sum((c or {}).values())


def stats(s):
    out = []
    for k, v in (s or {}).items():
        v = str(v).replace("_", " ")
        out.append(f"{k.replace('_', ' ')} {v}")
    return out


def peak(f, line, key):
    vals = [x["stats"][key] for x in fl[f].get(line, []) if x.get("stats") and key in x["stats"]]
    return str(vals[-1]).replace("any_acquired", "any acquired hull") if vals else "—"


base = {b["role"]: units(b.get("cost")) for b in ss["federation"]["buildings"]}
factions = []
for f in FACTIONS:
    hook, plus, minus = PLAYSTYLE[f]
    start = {}
    for b in ss[f]["buildings"]:
        start[b["role"]] = {"name": b["name"], "cost": cost(b.get("cost")), "delta": units(b.get("cost")) - base.get(b["role"], 0),
                            "effect": b["effect"], "requires": b.get("requires", "").replace("_", " ")}
    fourth = [k for k in fl[f] if k not in FIXED][0]
    lines = []
    for key in (*FIXED, fourth):
        lines.append({"slot": key if key in FIXED else "fourth", "label": LINE_NAMES[key].split(" (")[0],
                      "levels": [{"name": x["name"], "cost": cost(x.get("cost")), "stats": stats(x.get("stats")),
                                  "effect": x["effect"], "tag": x.get("tag", "")} for x in fl[f][key]]})
    pop = peak(f, fourth, "pop_cap")
    factions.append({
        "id": f, "name": NAMES[f], "hook": hook, "identity": ss[f]["identity"], "plus": plus, "minus": minus,
        "start": start, "opening": sum(units(b.get("cost")) for b in ss[f]["buildings"]), "lines": lines,
        "caps": [peak(f, "command", "supply_range"), peak(f, "deep_space", "docking"), peak(f, "shipyards", "hull_cap"),
                 pop if pop != "—" else f"— (uses {fourth})", start["research"]["effect"]],
        "wonders": [{"name": x["name"], "effect": x["effect"]} for ln in fl[f].values() for x in ln if x.get("tag") == "wonder"],
        "only": [{"name": x["name"], "effect": x["effect"]} for ln in fl[f].values() for x in ln if x.get("tag") == "faction_only"],
    })

data = {
    "factions": factions,
    "roles": [r.capitalize() for r in ROLES],
    "levels": sum(len(v) for f in FACTIONS for v in fl[f].values()),
    "lineKeys": [*FIXED, "fourth"],
    "lineLabels": {"deep_space": "Deep space", "command": "Command", "shipyards": "Shipyards", "fourth": "Habitation / Clones / Drones"},
    "capCols": ["Max supply range", "Max docking", "Largest hull", "Max pop per building", "Research in the opening"],
}
# roles are capitalised for display; key the opening buildings the same way
for fa in factions:
    fa["start"] = {k.capitalize(): v for k, v in fa["start"].items()}

tpl = open(os.path.join(HERE, "faction_buildings_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
