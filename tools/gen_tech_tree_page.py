"""Write tech-tree.html: a shareable page for the research tech tree and how each faction uses it.

Usage: python3 tools/gen_tech_tree_page.py [OUT_HTML]
Reads data/tech_tree.json, eras.json, ships.json, buildings.json, resources.json and the research
summaries in tools/faction_meta.py, and fills tools/tech_tree_page.html.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, NAMES, RESEARCH_MODEL  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "tech-tree.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


T = load("tech_tree.json")
ERAS = load("eras.json")["eras"]
S = load("ships.json")
B = load("buildings.json")
RES = {r["id"]: r["name"] for r in load("resources.json")["resources"]}

TREE_LABEL = {"propulsion": "Propulsion", "weapons": "Weapons", "defense": "Defense", "sensors_stealth": "Sensors & stealth",
              "engineering": "Engineering", "computing": "Computing", "biology_colonization": "Biology & colonization"}
TIER_ERA = {1: "ENT", 2: "TOS", 3: "TNG", 4: "DS9 / VOY", 5: "Picard", 6: "32nd c."}  # from tech_tree.json _notes

techs = {t["id"]: t for t in T["techs"]}
modules = {m for v in S["modules"].values() for m in v}
buildings = {b["id"] for b in B["buildings"]} | {b["id"] for b in B.get("faction_buildings", [])}


def unlock_kind(u):
    if u.startswith("mod_") and u[4:] in modules:
        return "module", u[4:].replace("_", " ")
    if u in buildings or u in ("recrystallizer", "subspace_relay", "transwarp_hub", "holodeck"):
        return "building", u.replace("_", " ")
    if u in RES:
        return "resource", RES[u]
    return "ability", u.replace("_", " ")


def reachable(f):
    """Techs a faction can research: allowed for it, with every prerequisite also reachable."""
    ok = {}

    def can(tid):
        if tid not in ok:
            t = techs[tid]
            ok[tid] = (not t.get("faction_only") or f in t["faction_only"]) and all(can(p) for p in t["prereqs"])
        return ok[tid]
    return {tid for tid in techs if can(tid)}


children = {tid: [] for tid in techs}
for t in T["techs"]:
    for p in t["prereqs"]:
        children[p].append(t["id"])

out_techs = []
for t in T["techs"]:
    out_techs.append({
        "id": t["id"], "name": t["name"], "tree": t["tree"], "tier": t["tier"], "prereqs": t["prereqs"], "leads": children[t["id"]],
        "unlocks": [list(unlock_kind(u)) for u in t.get("unlocks", [])], "effect": t.get("effect", ""),
        "only": t.get("faction_only", []), "forbidden": bool(t.get("forbidden")), "risky": bool(t.get("risky")),
        "victory": bool(t.get("victory_project")), "resource": RES.get(t.get("requires_resource"), t.get("requires_resource", "")),
    })

start = B["starting_sets"]["factions"]
factions = []
for f in FACTIONS:
    reach = reachable(f)
    label, text = RESEARCH_MODEL[f]
    lab = next(b for b in start[f]["buildings"] if b["role"] == "research")
    factions.append({"id": f, "name": NAMES[f], "model": label, "text": text, "lab": lab["name"], "labEffect": lab["effect"],
                     "reach": sorted(reach), "own": [t["name"] for t in T["techs"] if f in t.get("faction_only", [])],
                     "locked": len(techs) - len(reach)})

eras = []
for e in ERAS:
    cap = e["tech_cap_tier"]
    eras.append({"id": e["id"], "name": e["name"], "years": e["years"], "cap": cap,
                 "granted": sum(1 for t in T["techs"] if t["tier"] < cap - 1),
                 "research": sum(1 for t in T["techs"] if cap - 1 <= t["tier"] <= cap),
                 "locked": sum(1 for t in T["techs"] if t["tier"] > cap)})

# data checks shown at the bottom of the page
unlocked_mods = {u[4:] for t in T["techs"] for u in t.get("unlocks", []) if u.startswith("mod_")}
no_tech = {slot: sorted(m.replace("_", " ") for m in mods if m not in unlocked_mods and m not in techs)
           for slot, mods in S["modules"].items()}
checks = []
pc = techs.get("phase_cloak")
if pc and "Federation" in pc.get("effect", "") and "federation" not in reachable("federation") and "cloaking_device" in pc["prereqs"]:
    checks.append("Phasing Cloak's effect mentions Federation use, but the Federation can't research its prerequisite, Cloaking Device.")
for t in T["techs"]:
    if t.get("forbidden") and not t["prereqs"] and t["tier"] > 1:
        checks.append(f"{t['name']} (tier {t['tier']}) has no prerequisites, so it can be researched as soon as its tier opens.")

data = {
    "trees": [{"id": k, "label": TREE_LABEL.get(k, k)} for k in T["trees"]],
    "tiers": [{"n": n, "era": TIER_ERA[n]} for n in sorted(TIER_ERA)],
    "techs": out_techs, "factions": factions, "eras": eras,
    "noTech": no_tech, "checks": checks, "rule": T["_notes"],
}
tpl = open(os.path.join(HERE, "tech_tree_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
