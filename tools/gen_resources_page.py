"""Write resources.html: a shareable page for the game's resources and how each faction consumes them.

Usage: python3 tools/gen_resources_page.py [OUT_HTML]
Reads data/resources.json, buildings.json, tech_tree.json, hazards.json, planet_classes.json and GDD.md,
and fills tools/resources_page.html.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, NAMES  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "resources.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


RJ = load("resources.json")
R = {r["id"]: r for r in RJ["resources"]}
B = load("buildings.json")
T = load("tech_tree.json")["techs"]
H = load("hazards.json")["hazards"]
P = {p["id"]: p["name"].split(" — ")[0] for p in load("planet_classes.json")["classes"]}
TIERS = [("flow", "Flow", "Always on the HUD: mined or generated every cycle and spent on everything."),
         ("strategic", "Strategic", "Rarer materials that gate specific techs, modules and buildings."),
         ("exotic", "Exotic", "Late-game and dangerous: huge power with a real chance of disaster."),
         ("refined", "Refined", "Produced from other resources, never mined."),
         ("faction", "Faction", "Belong to one faction and drive its signature mechanics."),
         ("trade_good", "Trade goods", "Bought and sold for latinum and morale; contraband in some places.")]
# names a building's effect text uses for a resource it produces
ALIASES = {"ketracel_white": ["White"], "singularity_core": ["Singularity"], "nanoprobes": ["nanoprobe"], "honor": ["Honor"],
           "verterium_cortenide": ["Verterium"], "bloodwine": ["Bloodwine", "bloodwine"]}


def words(s):
    return s.replace("_", " ")


def all_buildings(f):
    return list(B["starting_sets"]["factions"][f]["buildings"]) + [x for ln in B["faction_lines"][f].values() for x in ln]


demand = {f: {} for f in FACTIONS}
for f in FACTIONS:
    for b in all_buildings(f):
        for k, v in (b.get("cost") or {}).items():
            demand[f][k] = demand[f].get(k, 0) + v

shared = list(B.get("buildings", [])) + list(B.get("faction_buildings", []))
producers = {}
for rid, r in R.items():
    keys = ALIASES.get(rid, [r["name"]])
    found = []
    for f in FACTIONS:
        for b in all_buildings(f):
            eff = b.get("effect", "")
            if any(k in eff for k in keys) and re.search(r"(\+\d|[Pp]roduc|[Ss]ynthesi|[Ee]arn|[Gg]enerat)", eff):
                found.append(f"{b['name']} ({NAMES[f]})")
    for b in shared:
        eff = b.get("effect", "")
        if any(k in eff for k in keys) and re.search(r"(\+\d|[Pp]roduc|[Ss]ynthesi|[Ee]arn|[Gg]enerat)", eff):
            found.append(b["name"])
    producers[rid] = sorted(set(found))[:4]

needed_by_tech = {}
for t in T:
    if t.get("requires_resource"):
        needed_by_tech.setdefault(t["requires_resource"], []).append(t["name"])
harvest = {}
for h in H:
    if h.get("harvest"):
        harvest.setdefault(h["harvest"], []).append(h["name"])
feeds = {}
for r in R.values():
    for i in r.get("inputs", {}):
        feeds.setdefault(i, []).append(R[r["id"]]["name"])

resources = []
for r in RJ["resources"]:
    rid = r["id"]
    resources.append({
        "id": rid, "name": r["name"], "tier": r["tier"], "source": r["source"], "lore": r.get("lore", ""),
        "uses": [words(u) for u in r.get("uses", [])],
        "found": [P.get(x, words(x)) for x in r.get("found_on", [])],
        "inputs": [[R[k]["name"], v] for k, v in r.get("inputs", {}).items()],
        "mechanics": [f"{words(k)}: {words(str(v)) if not isinstance(v, list) else ', '.join(words(x) for x in v)}" for k, v in r.get("mechanics", {}).items()],
        "faction": NAMES.get(r.get("faction"), r.get("faction", "")), "factionId": r.get("faction", ""),
        "eras": r.get("eras", []) or ([f"from {r['eras_from']}"] if r.get("eras_from") else []),
        "techs": needed_by_tech.get(rid, []), "harvest": harvest.get(rid, []), "feeds": feeds.get(rid, []),
        "producers": producers[rid],
        "demand": {f: demand[f].get(rid, 0) for f in FACTIONS},
    })

chains = [{"id": r["id"], "name": r["name"], "inputs": [{"id": k, "name": R[k]["name"], "tier": R[k]["tier"], "amount": v} for k, v in r["inputs"].items()]}
          for r in RJ["resources"] if r.get("inputs")]

factions = []
for f in FACTIONS:
    top = sorted(((k, v) for k, v in demand[f].items() if k in R), key=lambda kv: -kv[1])
    total = sum(v for _, v in top)
    factions.append({"id": f, "name": NAMES[f], "total": total,
                     "top": [{"name": R[k]["name"], "tier": R[k]["tier"], "amount": v} for k, v in top[:6]],
                     "own": [r["name"] for r in RJ["resources"] if r.get("faction") == f]})

# ---- data checks
checks = []
consumed = set(k for f in FACTIONS for k in demand[f]) | {k for b in shared for k in (b.get("cost") or {})}
consumed |= set(needed_by_tech) | set(feeds) | set(harvest)
unused = [R[k]["name"] for k in R if k not in consumed]
if unused:
    checks.append(f"Never spent by a building, needed by a tech or used as an input: {', '.join(unused)}.")
recipe = [r["name"] for r in RJ["resources"] if "synthesized" in r.get("found_on", []) and not r.get("inputs") and not producers[r["id"]]]
if recipe:
    checks.append(f"Listed as synthesized but with no recipe (inputs) or producing building: {', '.join(recipe)}.")
nosrc = [r["name"] for r in RJ["resources"] if not r.get("found_on") and not r.get("inputs") and not producers[r["id"]] and r["tier"] != "trade_good"]
if nosrc:
    checks.append(f"No source at all (not found anywhere, no recipe, no producing building): {', '.join(nosrc)}.")
tiers_in_notes = re.search(r"tier: ([^.]*)\.", RJ["_notes"])
listed = {t.strip() for t in tiers_in_notes.group(1).split("|")} if tiers_in_notes else set()
missing_tiers = sorted({r["tier"] for r in RJ["resources"]} - listed)
if missing_tiers:
    checks.append(f"resources.json _notes doesn't list the tier(s): {', '.join(missing_tiers)}.")
gdd = open(os.path.join(ROOT, "GDD.md")).read()
m = re.search(r"\*\*Refined components[^:]*:\*\* (.*?)\n- \*\*", gdd, re.S)
if m:
    names = {r["name"].split(" (")[0].lower() for r in RJ["resources"]}
    listed_gdd = [n.strip() for n in " ".join(m.group(1).split()).split(",")]
    gone = [n for n in listed_gdd if n and n.lower() not in names]
    if gone:
        checks.append(f"GDD.md lists refined components that aren't in resources.json: {', '.join(gone)}.")
for r in RJ["resources"]:
    if "verify" in r.get("lore", "").lower():
        checks.append(f"{r['name']}'s lore still carries a 'verify' note.")

data = {"tiers": [{"id": t, "label": l, "desc": d, "count": sum(1 for r in RJ["resources"] if r["tier"] == t)} for t, l, d in TIERS],
        "resources": resources, "chains": chains, "factions": factions, "checks": checks,
        "fnames": {f: NAMES[f] for f in FACTIONS}}
tpl = open(os.path.join(HERE, "resources_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
