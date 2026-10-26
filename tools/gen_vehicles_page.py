"""Write vehicles.html: a shareable page for small craft, probes, support ships and ground forces.

Usage: python3 tools/gen_vehicles_page.py [OUT_HTML]
Reads data/vehicles.json, factions.json, eras.json and ships.json, plus the vehicle text in
tools/faction_meta.py, and fills tools/vehicles_page.html.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, NAMES, VEHICLE_STANCE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "vehicles.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


V = load("vehicles.json")
FAC = {f["id"]: f for f in load("factions.json")["factions"]}
ERAS = {e["id"]: e for e in load("eras.json")["eras"]}
HULLS = {h["id"] for h in load("ships.json")["hulls"]}
CATS = [("small_craft", "Small craft", "Shuttles, fighters and boarding craft carried by ships and stations or built at drydocks."),
        ("probes_and_drones", "Probes and drones", "Uncrewed scouts, buoys, mines and automation."),
        ("civilian_and_support", "Civilian and support", "The working fleet every faction shares: builders, colony ships, miners, freighters, tugs."),
        ("ground_forces", "Ground forces", "Troops that land by shuttle or transporter. Combat resolves by strength times modifiers."),
        ("ground_vehicles", "Ground vehicles", "Surface units for recon, assault, occupation and economy."),
        ("heroes", "Heroes", "Captains and leaders: few in number, they gain XP and traits and can be captured, ransomed or killed.")]
ERA_SHORT = {"ent": "Enterprise", "tos": "TOS", "tng": "TNG", "dominion_war": "Dominion War", "voy": "Voyager",
             "picard": "Picard", "far_future": "32nd century", "kelvin": "Kelvin"}


def words(s):
    return s.replace("_", " ")


def fname(f):
    if f == "any":
        return "Any faction"
    return NAMES.get(f) or FAC.get(f, {}).get("name", words(f).title())


units = []
for cat, label, _ in CATS:
    for x in V[cat]:
        eras = [x["era"]] if "era" in x else x.get("eras", [])
        units.append({
            "id": x["id"], "name": x["name"], "cat": cat, "faction": x.get("faction", "any"), "factionName": fname(x.get("faction", "any")),
            "source": x["source"], "role": words(x.get("role", "")), "eras": [ERA_SHORT.get(e, e) for e in eras],
            "abilities": [words(a) for a in x.get("abilities", []) + x.get("traits", [])],
            "strength": x.get("strength"), "slots": x.get("carrier_slots"), "hull": words(x.get("hull", "")),
            "modules": [words(m) for m in x.get("modules", [])], "limit": x.get("limit"), "consumed": bool(x.get("consumed")),
            "notes": x.get("notes", ""),
        })

factions = []
for f in FACTIONS:
    mine = [u for u in units if u["faction"] == f]
    troops = [u for u in mine if u["strength"] is not None]
    best = max(troops, key=lambda u: u["strength"]) if troops else None
    factions.append({"id": f, "name": NAMES[f], "text": VEHICLE_STANCE[f], "count": len(mine),
                     "counts": {c: sum(1 for u in mine if u["cat"] == c) for c, _, _ in CATS},
                     "best": {"name": best["name"], "strength": best["strength"]} if best else None})

checks = []
for cat, label, _ in CATS:
    if cat == "civilian_and_support":
        continue
    missing = [NAMES[f] for f in FACTIONS if not any(u["faction"] == f and u["cat"] == cat for u in units)]
    if missing:
        checks.append(f"No faction-specific {label.lower()}: {', '.join(missing)}" + (" (they use the shared ones)." if any(u["faction"] == "any" and u["cat"] == cat for u in units) else "."))
for cat, label, _ in CATS:
    for x in V[cat]:
        fa = x.get("faction", "any")
        for e in ([x["era"]] if "era" in x else x.get("eras", [])):
            if fa != "any" and e in ERAS and fa not in ERAS[e]["playable"] + ERAS[e].get("ai_only", []):
                checks.append(f"{x['name']} is a {fname(fa)} unit in the {ERAS[e]['name']} era, which {fname(fa)} isn't part of.")
        if "hull" in x and x["hull"] not in HULLS:
            checks.append(f"{x['name']} uses hull {x['hull']}, which isn't in ships.json.")
        if fa != "any" and fa not in FAC:
            checks.append(f"{x['name']} belongs to {fa}, which isn't in factions.json.")
# flag a lopsided roster: one faction with more than twice the units of the next
top = sorted(factions, key=lambda f: -f["count"])
if top[0]["count"] > 2 * top[1]["count"]:
    checks.append(f"{top[0]['name']} has {top[0]['count']} units of its own; the next largest, {top[1]['name']}, has {top[1]['count']}.")

data = {"cats": [{"id": c, "label": l, "desc": d, "count": sum(1 for u in units if u["cat"] == c)} for c, l, d in CATS],
        "units": units, "factions": factions, "checks": checks, "rules": V["_notes"].split(". ", 1)[1].split(" source:")[0]}
tpl = open(os.path.join(HERE, "vehicles_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
