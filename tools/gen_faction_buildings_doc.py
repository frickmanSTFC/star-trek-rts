"""Write docs/faction-buildings.md: grids comparing each faction's buildings, built from data/buildings.json.

Usage: python3 tools/gen_faction_buildings_doc.py [OUT_MD]
Re-run after editing buildings.json so the write-up stays in sync.
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "docs", "faction-buildings.md")
B = json.load(open(os.path.join(ROOT, "data", "buildings.json")))

FACTIONS = ["federation", "klingon", "cardassian", "romulan", "dominion", "ferengi", "borg"]
NAMES = {"federation": "Federation", "klingon": "Klingon", "cardassian": "Cardassian", "romulan": "Romulan",
         "dominion": "Dominion", "ferengi": "Ferengi", "borg": "Borg"}
ROLES = ["core", "housing", "extraction", "power", "research", "defense", "signature"]
LINE_NAMES = {"deep_space": "Deep space", "command": "Command", "shipyards": "Shipyards",
              "habitation": "Habitation", "clones": "Clones (Dominion)", "drones": "Drones (Borg)"}
RES = {"duranium": "duranium", "tritanium": "tritanium", "parasteel": "parasteel", "crew": "crew",
       "deuterium": "deuterium", "isolinear_chips": "isolinear", "plasma_conduits": "plasma conduits",
       "warp_coils": "warp coils", "dilithium": "dilithium", "rodinium": "rodinium", "kelbonite": "kelbonite",
       "uridium": "uridium", "antimatter": "antimatter", "singularity_core": "singularity cores",
       "zenite": "zenite", "ketracel_white": "White", "latinum": "latinum", "nanoprobes": "nanoprobes",
       "honor": "Honor", "transparent_aluminum": "transp. aluminum", "topaline": "topaline"}

# Hand-written summary per faction: how its buildings play differently from the Federation baseline.
PLAYSTYLE = {
    "federation": ("Baseline", "Balanced costs; best research (+25% labs); widest habitation range (domes on any solid body); Yorktown and Utopia Planitia wonders.",
                   "No gimmick to lean on; Xenology Post risks a Prime Directive penalty."),
    "klingon": ("Honor", "Cheap opening (only Ferengi leases cost less); guns and troops from turn one; strip mines and battle reactors front-load output; Honor buys Great House fleets.",
                "Research limited to weapons at -25%; mines deplete faster; reactors explode on neighbours."),
    "cardassian": ("Occupation", "Strongest extraction (+50%); labour and occupation bonuses on populated worlds; Nor-type stations double refining.",
                   "-25% research; output bonuses raise Resistance that must be managed."),
    "romulan": ("Stealth", "Colonies, mines, guns and docks hidden from scans; spy slot from the start; singularity tech removes antimatter upkeep.",
                "Most expensive opening (needs isolinear chips early); shrouded mines yield -20%."),
    "dominion": ("Ketracel-white", "No population needed: Jem'Hadar are grown in vats; squads arrive with the core; strong polaron defence.",
                 "Everything depends on White supply; -30% research; skips cruiser hulls."),
    "ferengi": ("Latinum", "Pays to play: leases, contracts and markets earn latinum from every faction; can buy research instead of doing it.",
                "Nothing works without latinum; no garrison on the core; defence is rented."),
    "borg": ("Assimilation", "Strongest harvesting (+60%); shared power grid; adapts to weapons; gains tech by assimilating hulls and crews.",
             "Can only settle populated worlds; no research at all; harvesting degrades the planet."),
}


def cost(c):
    if not c:
        return "free"
    return ", ".join(f"{v} {RES.get(k, k)}" for k, v in c.items())


def units(c):
    return sum(v for v in (c or {}).values())


def esc(s):
    return str(s).replace("|", "\\|")


def stats(s):
    if not s:
        return ""
    out = []
    for k, v in s.items():
        out.append(f"{k.replace('_', ' ')} {v}".replace(" 1_per_system", " 1 per system"))
    return "; ".join(out)


lines = []
w = lines.append

w("# Faction buildings: how each faction differs")
w("")
w("Generated from `data/buildings.json` by `tools/gen_faction_buildings_doc.py`; re-run it after changing the data. "
  "All costs and stats are placeholders for balancing.")
w("")
w("The Federation is the baseline. Every other faction fills the same roles but trades cost, output or rules to fit its identity.")
w("")

# ---------------------------------------------------------------- 1. overview
w("## 1. Faction overview")
w("")
w("| Faction | Economy hook | Identity | Strengths | Trade-offs |")
w("|---|---|---|---|---|")
for f in FACTIONS:
    hook, plus, minus = PLAYSTYLE[f]
    ident = B["starting_sets"]["factions"][f]["identity"]
    w(f"| **{NAMES[f]}** | {hook} | {esc(ident)} | {esc(plus)} | {esc(minus)} |")
w("")

# ---------------------------------------------------------------- 2. starting grid
ss = B["starting_sets"]["factions"]
by_role = {f: {b["role"]: b for b in ss[f]["buildings"]} for f in FACTIONS}
w("## 2. Starting buildings on a Class M colony (at a glance)")
w("")
w("One building per role is available in the opening minutes. Names only; costs and effects follow in section 3.")
w("")
w("| Role | " + " | ".join(NAMES[f] for f in FACTIONS) + " |")
w("|---|" + "---|" * len(FACTIONS))
for r in ROLES:
    w(f"| **{r.capitalize()}** | " + " | ".join(esc(by_role[f][r]["name"]) if r in by_role[f] else "—" for f in FACTIONS) + " |")
totals = {f: sum(units(b.get("cost")) for b in ss[f]["buildings"]) for f in FACTIONS}
w("| *Opening cost (total units)* | " + " | ".join(f"*{totals[f]}*" for f in FACTIONS) + " |")
w("")
w("Total units add every resource together (duranium, crew, latinum, and so on). They're only a rough guide to how expensive each opening is.")
w("")

# ---------------------------------------------------------------- 3. per-role detail
w("## 3. Starting buildings by role")
w("")
for r in ROLES:
    base = by_role["federation"].get(r)
    base_units = units(base.get("cost")) if base else 0
    w(f"### {r.capitalize()}")
    w("")
    w("| Faction | Building | Cost | vs Federation cost | Effect | Requires |")
    w("|---|---|---|---|---|---|")
    for f in FACTIONS:
        b = by_role[f].get(r)
        if not b:
            w(f"| {NAMES[f]} | — | — | — | — | — |")
            continue
        d = units(b.get("cost")) - base_units
        delta = "baseline" if f == "federation" else ("same" if d == 0 else f"{d:+d}")
        req = b.get("requires", "").replace("_", " ")
        w(f"| {NAMES[f]} | {esc(b['name'])} | {cost(b.get('cost'))} | {delta} | {esc(b['effect'])} | {req} |")
    w("")

# ---------------------------------------------------------------- 4. upgrade lines grid
fl = B["faction_lines"]
w("## 4. Upgrade lines: level 1 to max")
w("")
n_levels = sum(len(v) for f in FACTIONS for v in fl[f].values())
w(f"Each line upgrades in place: a new level replaces the one before it. Seven factions have four lines each, {n_levels} levels in all. "
  "The fourth line is Habitation for most factions, Clones for the Dominion and Drones for the Borg.")
w("")
w("### At a glance")
w("")
w("| Faction | Deep space | Command | Shipyards | Habitation / Clones / Drones |")
w("|---|---|---|---|---|")
for f in FACTIONS:
    keys = list(fl[f])
    fourth = [k for k in keys if k not in ("deep_space", "command", "shipyards")][0]
    cells = []
    for k in ("deep_space", "command", "shipyards", fourth):
        cells.append(" → ".join(esc(x["name"]) for x in fl[f][k]) + f" ({len(fl[f][k])})")
    w(f"| **{NAMES[f]}** | " + " | ".join(cells) + " |")
w("")
w("The number in brackets is how many levels the line has. The Federation command line has 5 levels; it's the one with detailed art in `art/federation-colony-hub/`.")
w("")

for line in ("deep_space", "command", "shipyards"):
    w(f"### {LINE_NAMES[line]}")
    w("")
    w("| Faction | Level | Building | Cost | Stats | Effect |")
    w("|---|---|---|---|---|---|")
    for f in FACTIONS:
        for i, x in enumerate(fl[f][line], 1):
            tag = f" *({x['tag'].replace('_', ' ')})*" if x.get("tag") else ""
            w(f"| {NAMES[f] if i == 1 else ''} | L{i} | {esc(x['name'])}{tag} | {cost(x.get('cost'))} | {esc(stats(x.get('stats')))} | {esc(x['effect'])} |")
    w("")

w("### Habitation, Clones and Drones")
w("")
w("| Faction | Line | Level | Building | Cost | Stats | Effect |")
w("|---|---|---|---|---|---|---|")
for f in FACTIONS:
    fourth = [k for k in fl[f] if k not in ("deep_space", "command", "shipyards")][0]
    for i, x in enumerate(fl[f][fourth], 1):
        tag = f" *({x['tag'].replace('_', ' ')})*" if x.get("tag") else ""
        w(f"| {NAMES[f] if i == 1 else ''} | {fourth if i == 1 else ''} | L{i} | {esc(x['name'])}{tag} | {cost(x.get('cost'))} | {esc(stats(x.get('stats')))} | {esc(x['effect'])} |")
w("")

# ---------------------------------------------------------------- 5. capability comparison
w("## 5. Capability comparison")
w("")


def top(f, line, key):
    vals = [x.get("stats", {}) or {} for x in fl[f].get(line, [])]
    vals = [v.get(key) for v in vals if key in v]
    return vals[-1] if vals else "—"


def top_shipyard(f):
    return str(top(f, "shipyards", "hull_cap")).replace("any_acquired", "any acquired hull")


def top_pop(f):
    keys = [k for k in fl[f] if k not in ("deep_space", "command", "shipyards")]
    v = top(f, keys[0], "pop_cap")
    return v if v != "—" else f"— (uses {keys[0]})"


def queues(f):
    v = top(f, "command", "build_queues")
    return v if v != "—" else "see effects"


def research(f):
    return by_role[f]["research"]["effect"]


w("Highest value each faction reaches in its upgrade lines. A dash means the data doesn't list that stat for the faction.")
w("")
w("| Faction | Max supply range (command) | Max docking (deep space) | Largest hull | Max pop per building | Research in the opening |")
w("|---|---|---|---|---|---|")
for f in FACTIONS:
    w(f"| **{NAMES[f]}** | {top(f, 'command', 'supply_range')} | {top(f, 'deep_space', 'docking')} | {top_shipyard(f)} | {top_pop(f)} | {esc(research(f))} |")
w("")

# ---------------------------------------------------------------- 6. unique buildings
w("## 6. Wonders and faction-only buildings")
w("")
w("| Faction | Wonders | Faction-only |")
w("|---|---|---|")
for f in FACTIONS:
    wonders = [x["name"] for ln in fl[f].values() for x in ln if x.get("tag") == "wonder"]
    only = [x["name"] for ln in fl[f].values() for x in ln if x.get("tag") == "faction_only"]
    w(f"| **{NAMES[f]}** | {', '.join(wonders) or '—'} | {', '.join(only) or '—'} |")
w("")
w("Wonders are one-per-player megastructures. Faction-only buildings have no equivalent for other factions.")
w("")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, "w").write("\n".join(lines))
print("wrote", OUT)
