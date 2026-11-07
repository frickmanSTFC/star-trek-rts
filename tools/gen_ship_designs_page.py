"""Write ship-designs.html: a shareable page comparing each faction's starships, built from the data files.

Usage: python3 tools/gen_ship_designs_page.py [OUT_HTML]
Reads data/ships.json, data/factions.json, data/eras.json, data/buildings.json (shipyard caps), tech_tree.json and
resources.json (build costs and tech gates), plus the doctrine text in tools/faction_meta.py, and fills
tools/ship_designs_page.html. Its data checks cover the ship classes' cost, build time, crew cost, tech gate and role
(ids resolve, resources exist, every class has the fields, the numbers match tools/ship_costs.py's formula).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import data_loader  # noqa: E402
import ship_costs  # noqa: E402
from faction_meta import FACTIONS, NAMES, SHIP_DOCTRINE  # noqa: E402
from tech_access import reachable  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "ship-designs.html")


def load(name):
    return data_loader.load(name)


S = load("ships.json")
FAC = {f["id"]: f for f in load("factions.json")["factions"]}
ERAS = {e["id"]: e for e in load("eras.json")["eras"]}
B = load("buildings.json")
T = load("tech_tree.json")
TECHS = {t["id"]: t for t in T["techs"]}
RESOURCES = {r["id"]: r for r in load("resources.json")["resources"]}
ROLES = S["roles"]
CTX = ship_costs.Context()
NO_RESEARCH = {f for f, r in T.get("faction_rules", {}).items() if r.get("research") is False}

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
    combat = ship_costs.derive({"faction": "", "hull": h["id"], "era": "far_future"}, CTX)  # the hull's own numbers, role combat
    hulls.append({"id": h["id"], "label": label(h["id"]), "size": h["size"], "hp": h["hp"], "speed": h["speed"], "crew": h["crew"],
                  "crewCost": combat["crew_cost"], "cost": combat["cost"], "build": combat["build_seconds"],
                  "slots": [h["slots"]["weapon"], h["slots"]["defense"], h["slots"]["engine"], h["slots"]["utility"]],
                  "notes": notes, "yard": next(n for cap, n in YARDS if h["size"] <= cap)})

module_ids = {m for v in S["modules"].values() for m in v}


def weapon_modules(w):
    return [m for m in module_ids if m == w or m.startswith(w + "_")]


def gate(c):
    """{id, name, tier, kind} of the class's tech gate, or None."""
    tid = c.get("requires_tech") or c.get("requires_assimilated")
    if not tid or tid not in TECHS:
        return None
    t = TECHS[tid]
    return {"id": tid, "name": t["name"], "tier": t["tier"], "kind": "assimilate" if c.get("requires_assimilated") else "research"}


classes = []
for c in S["classes"]:
    role = c.get("role", "combat")
    classes.append({"name": c["name"], "faction": c["faction"], "hull": c["hull"], "era": c["era"], "source": c["source"],
                    "special": [words(x.strip()) for x in c.get("special", "").split(",") if x.strip()],
                    "role": words(c.get("role", "")), "roleId": role, "civilian": bool(ROLES.get(role, {}).get("civilian")),
                    "cost": c.get("cost", {}), "build": c.get("build_seconds"), "crewCost": c.get("crew_cost"),
                    "gate": gate(c), "tuned": bool(c.get("tuned"))})

# ---- data checks: the classes' cost, build time, crew cost, tech gate and role
checks = []
hull_by_id = {h["id"]: h for h in S["hulls"]}
reach = {}
for c in S["classes"]:
    key = f"{c['faction']}.{c['name']}"
    for field in ["cost", "build_seconds", "crew_cost", "requires_tech"]:
        if field not in c:
            checks.append(f"{key} has no {field} (run python3 tools/ship_costs.py --write).")
    for r, n in c.get("cost", {}).items():
        if r not in RESOURCES:
            checks.append(f"{key} costs '{r}', which isn't in resources.json.")
        elif r in ("crew", "energy"):
            checks.append(f"{key} lists {r} in its cost ({'use crew_cost' if r == 'crew' else 'energy is a power budget'}).")
        if not isinstance(n, (int, float)) or n <= 0:
            checks.append(f"{key}: cost of {r} must be above 0.")
    if not c.get("cost") and "cost" in c:
        checks.append(f"{key} has an empty cost.")
    if "build_seconds" in c and not (isinstance(c["build_seconds"], (int, float)) and c["build_seconds"] > 0):
        checks.append(f"{key}: build_seconds must be above 0.")
    if "crew_cost" in c and not (isinstance(c["crew_cost"], int) and c["crew_cost"] >= 1):
        checks.append(f"{key}: crew_cost must be a whole number of at least 1.")
    role = c.get("role", "combat")
    if role not in ROLES:
        checks.append(f"{key} has role '{role}', which isn't in ships.json roles.")
    hull = hull_by_id.get(c["hull"])
    if hull and hull.get("roles") and role not in hull["roles"]:
        checks.append(f"{key} is on the {c['hull']} hull, which takes the roles {', '.join(hull['roles'])}, not '{role}'.")
    borg = c["faction"] in NO_RESEARCH
    tid = c.get("requires_assimilated") if borg else c.get("requires_tech")
    if borg and c.get("requires_tech"):
        checks.append(f"{key}: the {NAMES.get(c['faction'], c['faction'])} never research, so requires_tech must be null (use requires_assimilated).")
    if not borg and c.get("requires_assimilated"):
        checks.append(f"{key}: requires_assimilated is only for factions that don't research.")
    if tid:
        if tid not in TECHS:
            checks.append(f"{key} needs tech '{tid}', which isn't in tech_tree.json.")
        else:
            if c["faction"] not in reach:
                reach[c["faction"]] = reachable(TECHS, c["faction"], T.get("faction_rules"))
            if tid not in reach[c["faction"]]:
                checks.append(f"{key} needs {tid}, which {c['faction']} can't research.")
            cap = ERAS[c["era"]]["tech_cap_tier"]
            if TECHS[tid]["tier"] > cap:
                checks.append(f"{key} enters service in {c['era']} (tech cap tier {cap}) but needs {tid} (tier {TECHS[tid]['tier']}).")
    if role in ROLES and hull and not c.get("tuned"):
        off = ship_costs.differs(c, CTX)
        if off:
            checks.append(f"{key}: {', '.join(off)} {'differs' if len(off) == 1 else 'differ'} from the formula in ships/rules.json (run python3 tools/ship_costs.py --write, or mark the class \"tuned\": true).")
for f in FACTIONS:
    if f in NO_RESEARCH:
        continue
    civ = {c.get("role") for c in S["classes"] if c["faction"] == f and ROLES.get(c.get("role", "combat"), {}).get("civilian")}
    if "construction" not in civ:
        checks.append(f"{NAMES[f]} has no construction ship class.")
    if len(civ) < 2:
        checks.append(f"{NAMES[f]} has fewer than two civilian roles ({', '.join(sorted(civ)) or 'none'}).")

era_ids = [e for e in ERA_SHORT if any(c["era"] == e for c in classes)]
# kelvin is a separate timeline; the rest run in order, and a class stays available after the era it enters service
eras = [{"id": e, "short": ERA_SHORT[e], "name": ERAS[e]["name"], "years": ERAS[e]["years"],
         "timeline": "kelvin" if e == "kelvin" else "prime", "order": i} for i, e in enumerate(era_ids)]


def status(f, e):
    if f in ERAS[e]["playable"]:
        return "playable"
    return "ai" if f in ERAS[e].get("ai_only", []) else "absent"


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
        weapons.append({"label": words(w), "modules": mods})
    factions.append({
        "id": f, "name": NAMES[f], "playstyle": meta["playstyle"], "doctrine": SHIP_DOCTRINE[f], "weakness": meta["weakness"],
        "weapons": weapons, "status": {e: status(f, e) for e in era_ids}, "count": len(mine), "flagship": flag["name"],
        "flagship_hull": label(flag["hull"]), "yard": yard_cap(f),
        "hulls_used": len({c["hull"] for c in mine}), "civilian": sum(1 for c in mine if c["civilian"]),
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
               "modules": len(module_ids), "civilian": sum(1 for c in classes if c["civilian"])},
    "hullLabels": {h["id"]: h["label"] for h in hulls},
    "unusedEras": [ERA_SHORT[e] for e in ERA_SHORT if e not in era_ids],
    "roles": [{"id": k, "label": v["label"], "civilian": v["civilian"], "cost": v["cost"], "build": v["build"], "crew": v["crew"],
               "tech": v.get("tech")} for k, v in ROLES.items()],
    "resNames": {r: RESOURCES[r]["name"] for c in classes for r in c["cost"] if r in RESOURCES},
    "startCrew": load("surfaces.json")["landing"]["start_stock"].get("crew"),
    "noResearch": [NAMES.get(f, words(f).title()) for f in sorted(NO_RESEARCH)],
    "ladder": [{"tier": int(k), "id": v, "name": TECHS[v]["name"]} for k, v in CTX.rules["tech_ladder"].items() if v in TECHS],
    "borgLadder": [{"tier": int(k), "id": v, "name": TECHS[v]["name"]}
                   for k, v in CTX.rules["no_research"].get("borg", {}).get("ladder", {}).items() if v in TECHS],
    "costing": {k: v for k, v in CTX.rules.items() if k not in ("tech_ladder",)},
    "checks": checks,
}
tpl = open(os.path.join(HERE, "ship_designs_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
