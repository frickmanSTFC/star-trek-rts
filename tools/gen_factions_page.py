"""Write factions.html: a shareable page profiling every faction and comparing the seven great powers.

Usage: python3 tools/gen_factions_page.py [OUT_HTML]
Reads data/factions.json plus eras, buildings, ships, tech_tree, vehicles and resources, and the
summaries in tools/faction_meta.py, and fills tools/factions_page.html.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import (FACTIONS, HAZARD_STANCE, NAMES, PLAYSTYLE, RESEARCH_MODEL, SHIP_DOCTRINE,  # noqa: E402
                          VEHICLE_STANCE, WORLD_STANCE)
from tech_access import reachable  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "factions.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


FAC = load("factions.json")["factions"]
ERAS = load("eras.json")["eras"]
B = load("buildings.json")
S = load("ships.json")
TT = load("tech_tree.json")
V = load("vehicles.json")
R = {r["id"]: r for r in load("resources.json")["resources"]}
techs = {t["id"]: t for t in TT["techs"]}
RULES = TT.get("faction_rules", {})
HULL = {h["id"]: h for h in S["hulls"]}
ERA_SHORT = {"ent": "Enterprise", "tos": "TOS", "tng": "TNG", "dominion_war": "Dominion War", "voy": "Voyager",
             "picard": "Picard", "far_future": "32nd century", "kelvin": "Kelvin"}
ROLE = {"great": "Great power", "era": "Era power", "minor": "Minor power", "dlc_or_minor": "Minor power",
        "pirate": "Pirates", "insurgent": "Insurgents", "crisis": "Crisis"}
VCATS = ["small_craft", "probes_and_drones", "ground_forces", "ground_vehicles", "heroes"]
COMMON = {"duranium", "tritanium", "parasteel", "crew", "deuterium", "energy"}


def words(s):
    return s.replace("_", " ")


def role(f):
    if f["id"] in FACTIONS:
        return "great"
    p = f.get("playable")
    return "era" if p is True else p


def era_status(fid):
    out = {}
    for e in ERAS:
        out[e["id"]] = "playable" if fid in e["playable"] else "ai" if fid in e.get("ai_only", []) else ""
    return out


def all_buildings(f):
    return list(B["starting_sets"]["factions"][f]["buildings"]) + [x for ln in B["faction_lines"][f].values() for x in ln]


def units(c):
    return sum((c or {}).values())


great = []
for fid in FACTIONS:
    f = next(x for x in FAC if x["id"] == fid)
    start = B["starting_sets"]["factions"][fid]["buildings"]
    blds = all_buildings(fid)
    cost = {}
    for b in blds:
        for k, v in (b.get("cost") or {}).items():
            cost[k] = cost.get(k, 0) + v
    # what sets its economy apart: biggest costs other than the common building materials
    scarce = [R[k]["name"] for k, _ in sorted(cost.items(), key=lambda kv: -kv[1]) if k in R and k not in COMMON][:3]
    classes = [c for c in S["classes"] if c["faction"] == fid]
    flag = max(classes, key=lambda c: HULL[c["hull"]]["size"]) if classes else None
    reach = reachable(techs, fid, RULES)
    mine = [u for cat in VCATS for u in V[cat] if u.get("faction") == fid]
    troops = [u for u in V["ground_forces"] if u.get("faction") == fid]
    best = max(troops, key=lambda u: u["strength"]) if troops else None
    wonders = [x["name"] for ln in B["faction_lines"][fid].values() for x in ln if x.get("tag") == "wonder"]
    lab = next(b for b in start if b["role"] == "research")
    hook, plus, minus = PLAYSTYLE[fid]
    exposed, edge = HAZARD_STANCE[fid]
    prefers, rules = WORLD_STANCE[fid]
    great.append({
        "id": fid, "name": NAMES[fid], "full": f["name"], "playstyle": f.get("playstyle", ""), "hook": hook,
        "signature": {"name": words(f["signature"]["id"]).title(), "desc": f["signature"]["desc"]},
        "secondary": [words(s) for s in f.get("secondary", [])], "weakness": f.get("weakness", ""), "home": f.get("home", ""),
        "weapons": [words(w) for w in f.get("weapons", [])],
        "flavor": [[words(k), v] for k, v in f.get("flavor_units", {}).items()],
        "eras": era_status(fid),
        "facts": {
            "openingCost": sum(units(b.get("cost")) for b in start), "buildCost": sum(cost.values()),
            "research": RESEARCH_MODEL[fid][0], "lab": lab["effect"], "techs": len(reach), "ownTechs": [t["name"] for t in TT["techs"] if fid in t.get("faction_only", [])],
            "classes": len(classes), "flagship": f"{flag['name']} ({words(flag['hull'])})" if flag else "—",
            "units": len(mine), "troops": f"{best['name']} ({best['strength']})" if best else "—",
            "ownResources": [r["name"] for r in R.values() if r.get("faction") == fid], "scarce": scarce, "wonders": wonders,
            "playableEras": sum(1 for v in era_status(fid).values() if v == "playable"),
        },
        "notes": {"Strengths": plus, "Trade-offs": minus, "Fleet": SHIP_DOCTRINE[fid], "Research": RESEARCH_MODEL[fid][1],
                  "Craft and troops": VEHICLE_STANCE[fid], "Worlds": f"{prefers} {rules}", "Hazards": f"Exposed: {exposed} Edge: {edge}"},
    })

others = []
for f in FAC:
    if f["id"] in FACTIONS:
        continue
    others.append({"id": f["id"], "name": f["name"], "role": ROLE.get(role(f), words(str(role(f)))), "roleKey": role(f),
                   "signature": words(f["signature"]["id"]).title(), "desc": f["signature"]["desc"], "eras": era_status(f["id"]),
                   "playstyle": f.get("playstyle", ""), "weakness": f.get("weakness", ""), "hero": f.get("flavor_units", {}).get("hero", ""),
                   "ships": sum(1 for c in S["classes"] if c["faction"] == f["id"]),
                   "units": sum(1 for cat in VCATS for u in V[cat] if u.get("faction") == f["id"])})

checks = []
era_powers = [o["name"] for o in others if o["roleKey"] == "era"]
thin = [o["name"] for o in others if o["roleKey"] == "era" and not any(k in next(x for x in FAC if x["id"] == o["id"]) for k in ("playstyle", "weakness", "weapons"))]
if thin:
    checks.append(f"{len(thin)} era powers are playable but have only a signature mechanic (no playstyle, weakness, home, weapons or flavor units): {', '.join(thin)}.")
no_kit = [o["name"] for o in others if o["roleKey"] == "era" and o["ships"] == 0 and o["units"] == 0]
if no_kit:
    checks.append(f"Playable era powers with no ships or units of their own yet: {', '.join(no_kit)}.")
names = {u["name"].lower() for cat in VCATS + ["civilian_and_support"] for u in V[cat]} | {c["name"].lower() for c in S["classes"]}
heroes = [f"{v} ({NAMES[g['id']]})" for g in great for k, v in g["flavor"] if k == "hero" and v.lower() not in names]
if heroes:
    checks.append(f"Hero units named in factions.json aren't defined in any unit data yet: {', '.join(heroes)}.")
for g in great:
    if not g["facts"]["ownTechs"] and RULES.get(g["id"], {}).get("research", True):
        checks.append(f"{g['name']} has no faction-only techs.")

data = {"great": great, "others": others, "eras": [{"id": e["id"], "short": ERA_SHORT.get(e["id"], e["name"]), "name": e["name"], "years": e["years"]} for e in ERAS],
        "checks": checks, "counts": {"all": len(FAC), "great": len(great), "era": len(era_powers)}}
tpl = open(os.path.join(HERE, "factions_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
