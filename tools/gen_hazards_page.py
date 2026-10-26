"""Write hazards.html: a shareable page for the game's hazards and how each faction faces them.

Usage: python3 tools/gen_hazards_page.py [OUT_HTML]
Reads data/hazards.json, tech_tree.json, eras.json, resources.json and the hazard text in
tools/faction_meta.py, and fills tools/hazards_page.html.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, HAZARD_CATEGORIES, HAZARD_STANCE, NAMES  # noqa: E402
from tech_access import reachable  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "hazards.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


H = load("hazards.json")["hazards"]
TT = load("tech_tree.json")
techs = {t["id"]: t for t in TT["techs"]}
RULES = TT.get("faction_rules", {})
ERAS = load("eras.json")["eras"]
RES = {r["id"]: r["name"] for r in load("resources.json")["resources"]}


def words(s):
    return s.replace("_", " ")


def tech_ref(tid):
    return {"id": tid, "name": techs[tid]["name"] if tid in techs else words(tid).capitalize(), "exists": tid in techs}


hazards = []
for h in H:
    hazards.append({
        "id": h["id"], "name": h["name"], "cat": h["category"], "source": h["source"],
        "where": h.get("spawn") or h.get("trigger", ""), "whereLabel": "Trigger" if "trigger" in h else "Appears",
        "effect": h.get("effect") or h.get("behavior", ""), "counter": h.get("counter", ""),
        "choices": [[words(k), v] for k, v in h.get("choices", {}).items()],
        "counterTechs": [tech_ref(t) for t in h.get("counter_techs", [])],
        "reward": tech_ref(h["research_reward"]) if h.get("research_reward") else None,
        "harvest": RES.get(h.get("harvest"), words(h.get("harvest", ""))) if h.get("harvest") else "",
        "eras": h.get("eras", []),
    })

counter_ids = sorted({t for h in H for t in h.get("counter_techs", []) if t in techs})
factions = []
for f in FACTIONS:
    exposed, edge = HAZARD_STANCE[f]
    reach = reachable(techs, f, RULES)
    # how the faction gets each counter tech: Ferengi buy research; a faction that doesn't research assimilates or combines it
    how = "assimilate" if RULES.get(f, {}).get("research") is False else "buy" if f == "ferengi" else "research"
    access = {t: (how if t in reach else "none") for t in counter_ids}
    factions.append({"id": f, "name": NAMES[f], "exposed": exposed, "edge": edge, "access": access,
                     "reach": sum(1 for t in counter_ids if t in reach)})

checks = []
missing = {}
for h in H:
    for t in h.get("counter_techs", []):
        if t not in techs:
            missing.setdefault(t, []).append(h["name"])
for t, names in sorted(missing.items()):
    checks.append(f"{words(t).capitalize()} is named as a counter to {', '.join(names)} but isn't in the tech tree.")
for h in H:
    if h.get("research_reward") and h["research_reward"] not in techs:
        checks.append(f"{h['name']} rewards research in {h['research_reward']}, which isn't a tech id.")
    if h.get("harvest") and h["harvest"] not in RES:
        checks.append(f"{h['name']} can be harvested for {h['harvest']}, which isn't in resources.json.")
    if not h.get("counter") and not h.get("choices"):
        checks.append(f"{h['name']} has no counter or choices yet.")

data = {
    "categories": [{"id": k, "label": v[0], "desc": v[1], "count": sum(1 for h in H if h["category"] == k)} for k, v in HAZARD_CATEGORIES.items()],
    "hazards": hazards, "factions": factions,
    "counterTechs": [{"id": t, "name": techs[t]["name"], "tier": techs[t]["tier"],
                      "hazards": [h["name"] for h in H if t in h.get("counter_techs", [])]} for t in counter_ids],
    "eras": [{"id": e["id"], "name": e["name"]} for e in ERAS],
    "checks": checks, "missing": len(missing),
}
tpl = open(os.path.join(HERE, "hazards_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
