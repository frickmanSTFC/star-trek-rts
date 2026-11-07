"""Derive every ship class's cost, build time, crew cost and tech gate from its hull and role.

    python3 tools/ship_costs.py            # list the classes whose numbers differ from the formula
    python3 tools/ship_costs.py --write    # rewrite them in data/ships/classes/*.json (one class per line kept)

The formula and its numbers are in data/ships/rules.json (_notes_costing, costing, roles). The derived fields sit at
the end of each class line in a fixed order: cost, build_seconds, crew_cost, requires_tech[, requires_assimilated].
A class with "tuned": true is hand-balanced and left alone. gen_ship_designs_page.py imports derive() for its checks.
"""
import json
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import data_loader  # noqa: E402

DERIVED = ["cost", "build_seconds", "crew_cost", "requires_tech", "requires_assimilated"]


def _round(x, rules):
    if x <= 0:
        return 0
    if x >= rules["round_from"]:
        step = rules["round_to"]
        return int(math.floor(x / step + 0.5) * step)
    return max(1, int(math.floor(x + 0.5)))


class Context:
    """The documents the formula reads (base data)."""

    def __init__(self, data_dir=data_loader.DATA):
        ships = data_loader.load("ships", data_dir)
        self.roles = ships["roles"]
        self.rules = ships["costing"]
        self.hulls = {h["id"]: h for h in ships["hulls"]}
        tree = data_loader.load("tech_tree", data_dir)
        self.techs = {t["id"]: t for t in tree["techs"]}
        self.faction_rules = tree.get("faction_rules", {})
        self.era_cap = {e["id"]: e["tech_cap_tier"] for e in data_loader.load("eras", data_dir)["eras"]}


def derive(c, ctx):
    """The formula's fields for class c: {cost, build_seconds, crew_cost, requires_tech[, requires_assimilated]}."""
    R = ctx.rules
    hull = ctx.hulls[c["hull"]]
    role = ctx.roles[c.get("role", "combat")]
    s = hull["size"]
    raw = {}
    for res, k in R["per_size2"].items():
        raw[res] = raw.get(res, 0) + k * s * s
    if s >= R["from_size"]:
        for res, k in R["per_size"].items():
            raw[res] = raw.get(res, 0) + k * s
    for res, p in R["per_size_over"].items():
        if s > p["over"]:
            raw[res] = raw.get(res, 0) + p["per"] * (s - p["over"])
    special = R["no_research"].get(c["faction"]) if ctx.faction_rules.get(c["faction"], {}).get("research") is False else None
    if special:
        for res, sw in special.get("swap", {}).items():
            if res in raw:
                raw[sw["to"]] = raw.get(sw["to"], 0) + raw.pop(res) * sw["times"]
    cost = {}
    for res, x in raw.items():
        v = _round(x * role["cost"], R)
        if v:
            cost[res] = v
    b = R["build_seconds"]
    build = _round((b["base"] + b["per_size_pow"] * s ** b["pow"]) * role["build"], R)
    crew = int(math.floor(R["crew_per_sqrt_complement"] * math.sqrt(hull["crew"]) * role["crew"] + 0.5))
    crew = max(1, min(hull["crew"], crew))

    cap = ctx.era_cap[c["era"]]
    band = R["tech_band_by_size"].get(str(s))
    ladder = special["ladder"] if special else R["tech_ladder"]
    tech = ladder[str(min(band, cap))] if band else None
    rt = role.get("tech")
    if rt and ctx.techs[rt]["tier"] <= cap and (tech is None or ctx.techs[rt]["tier"] > ctx.techs[tech]["tier"]):
        tech = rt
    out = {"cost": cost, "build_seconds": build, "crew_cost": crew}
    if special:
        out["requires_tech"] = None
        out[special["gate"]] = tech
    else:
        out["requires_tech"] = tech
    return out


def differs(c, ctx):
    """Fields of c that differ from the formula (empty for a tuned class)."""
    if c.get("tuned"):
        return []
    want = derive(c, ctx)
    return [k for k in DERIVED if (k in want or k in c) and c.get(k) != want.get(k)]


def _text(fields):
    parts = []
    for k in DERIVED:
        if k not in fields:
            continue
        v = fields[k]
        if k == "cost":
            parts.append('"cost": { ' + ", ".join(f'"{r}": {n}' for r, n in v.items()) + " }")
        else:
            parts.append(f'"{k}": {json.dumps(v)}')
    return ", ".join(parts)


LINE = re.compile(r'^(\s*)(\{ "faction".*\})(,?)\s*$')


def rewrite(path, ctx):
    """Rewrite the derived fields of every class line in one file; returns how many lines changed."""
    lines = open(path, encoding="utf-8").read().split("\n")
    changed = 0
    for i, line in enumerate(lines):
        m = LINE.match(line)
        if not m:
            continue
        c = json.loads(m.group(2))
        if c.get("tuned") or not differs(c, ctx):
            continue
        body = m.group(2)
        cut = body.find(', "cost": ')
        head = (body[:cut] if cut >= 0 else body[:-1].rstrip())
        lines[i] = f"{m.group(1)}{head}, {_text(derive(c, ctx))} }}{m.group(3)}"
        changed += 1
    if changed:
        open(path, "w", encoding="utf-8").write("\n".join(lines))
    return changed


def main():
    ctx = Context()
    files = [p for p in data_loader.manifest()["documents"]["ships"] if p.startswith("ships/classes/")]
    if "--write" in sys.argv:
        total = sum(rewrite(os.path.join(data_loader.DATA, p), ctx) for p in files)
        print(f"rewrote {total} class lines")
        return
    off = [(c["faction"], c["name"], differs(c, ctx)) for c in data_loader.load("ships")["classes"]]
    off = [o for o in off if o[2]]
    for f, n, d in off:
        print(f"{f}.{n}: {', '.join(d)}")
    print(f"{len(off)} classes differ from the formula")


if __name__ == "__main__":
    main()
