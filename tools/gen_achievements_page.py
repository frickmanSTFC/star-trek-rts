"""Write achievements.html: every achievement of the game prototype by category, its rule, reward and medal.

Usage: python3 tools/gen_achievements_page.py [OUT_HTML]
Reads data/achievements/ (plus buildings, surfaces, planet_classes and climate for the checks) and fills
tools/achievements_page.html. The line icons are read from the mockup (docs/mockups/achievements/index.html ICONS).
The game's own checks are GameData.ValidateAchievements (game/sim/Data/GameData.Achievements.cs).
"""
import html
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import data_loader  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "achievements.html")

A = data_loader.load("achievements")
BUILDINGS = {b["id"]: b for b in data_loader.load("buildings")["buildings"]}
SURF = data_loader.load("surfaces")
UNITS = {u["id"]: u for u in SURF.get("units", [])}
TECHS = SURF.get("research", [])
CLASSES = {c["id"]: c for c in data_loader.load("planet_classes")["classes"]}
WEATHER = {w["id"]: w["name"] for w in data_loader.load("climate")["weather"]}

CATS = sorted(A["categories"], key=lambda c: c["order"])
TIERS = {t["id"]: t for t in A["tiers"]}
COSMETICS = {c["id"]: c for c in A["cosmetics"]}
LIST = A["achievements"]
E = html.escape

# The same names the sim knows (GameData.Achievements.cs).
KIND_FIELDS = {
    "milestone": {"milestone", "before_seconds", "without"}, "built": {"building", "count"},
    "count": {"measure", "at_least", "all"}, "share": {"measure", "at_least"}, "unit_counter": {"counter", "at_least", "units"},
    "event": {"event"}, "streak": {"condition", "seconds", "from_start"}, "set": {"measure", "contains"},
    "view_count": {"measure", "at_least", "all"}, "all_of": {"rules"}, "any_of": {"rules"},
}
MILESTONES = {"uplink": "the outpost's uplink comes online", "landed": "the colony lands", "first_metal": "an Ore Refinery works",
              "first_parasteel": "the first parasteel is in stock", "second_base": "two finished colony cores",
              "research_all": "every surface tech is known"}
EVENTS = {
    "orbit_farthest": ([], "the flagship enters orbit of the body farthest from the colony"),
    "charted_class": (["class"], "a body of that class becomes Charted"),
    "deposit_found": (["amount"], "a deposit of at least that many units is surveyed"),
    "founded": ([], "a construction rover's outpost is finished"),
    "deposit_reopened": ([], "a Deep-Core Mine reopens a used-up deposit"),
    "tyre_changed_field": ([], "a tyre change finishes away from a repair pad"),
    "limped_home": ([], "a disabled rover parks on its own core's repair pad"),
    "crawler_explosion": ([], "a mining crawler's breakdown is an explosion"),
    "domed_crewed_working": ([], "a crewed building works inside a finished Pressure Dome's cover"),
    "life_support_saved": ([], "a base's life support comes back before the grace runs out"),
    "order_relayed": (["delay_seconds"], "a colony order arrives after that much signal delay"),
    "sos_answered": (["within_seconds"], "the flagship is back at the colony that soon after a breakdown reported while away"),
}
MEASURES = {"bodies_charted": ("count", "bodies charted"), "system_satellites": ("count", "satellites out in the system"),
            "units": ("count", "vehicles on the ground"), "techs_known": ("count", "surface techs known"),
            "surveyed": ("share", "of the map surveyed"), "weather_seen": ("set", "weather seen"),
            "ships_inspected": ("view_count", "neutral ship cards opened")}
CONDITIONS = {"no_power_short": "no base short of power", "flagship_home": "the flagship in orbit of the colony's body"}
ICON_NAMES = ["uplink", "ingot", "alloy", "crown", "capitol", "bolt", "chart", "satellite", "route", "planet", "hail", "hex", "gem",
              "base", "drill", "rover", "tyre", "wrench", "fleet", "blast", "dome", "lungs", "weight", "storm", "signal", "sos", "bay",
              "box", "flask", "anchor"]
KIND_TEXT = {"rover_name": "Rover name", "hull_number": "Hull number", "paint": "Paint", "finish": "Finish",
             "dropship_name": "Drop ship name", "beam_colour": "Exocomp beam colour", "dome_tint": "Dome glass tint"}


def icons():
    """The mockup's line icons by name (SVG path markup inside a 24x24 box)."""
    text = open(os.path.join(ROOT, "docs", "mockups", "achievements", "index.html"), encoding="utf-8").read()
    block = text[text.index("const ICONS = {"):]
    block = block[:block.index("};")]
    return dict(re.findall(r"^\s*(\w+): '(.*)',?\s*$", block, re.M))


ICONS = icons()


def shade(hex_colour, f):
    r, g, b = (int(hex_colour[i:i + 2], 16) for i in (1, 3, 5))
    if f >= 0:
        r, g, b = (round(v + (255 - v) * f) for v in (r, g, b))
    else:
        r, g, b = (round(v * (1 + f)) for v in (r, g, b))
    return f"#{r:02x}{g:02x}{b:02x}"


def medal(a, n):
    mid = TIERS.get(a["tier"], {"colour": "#888888"})["colour"]
    hi, lo = shade(mid, 0.45), shade(mid, -0.55)
    icon = ICONS.get(a["icon"], '<circle cx="12" cy="12" r="6"/>')
    gid = f"m{n}"
    return (f'<svg class="medal" viewBox="0 0 64 64" aria-hidden="true"><defs><linearGradient id="{gid}" x1="0" y1="0" x2="1" y2="1">'
            f'<stop offset="0" stop-color="{hi}"/><stop offset=".55" stop-color="{mid}"/><stop offset="1" stop-color="{lo}"/></linearGradient></defs>'
            f'<path d="M32 3l25 14.5v29L32 61 7 46.5v-29z" fill="url(#{gid})"/>'
            f'<path d="M32 9.5l19.4 11.2v22.6L32 54.5 12.6 43.3V20.7z" fill="{mid}" stroke="{hi}" stroke-width="1.2" opacity=".95"/>'
            f'<g transform="translate(18 18) scale(1.17)" fill="none" stroke="#0b1220" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">{icon}</g></svg>')


def name_of_building(b):
    return BUILDINGS.get(b, {}).get("name", b)


UNIT_NAMES = {"survey_rover": "Mk I Scout", "survey_rover_mk2": "Mk II Surveyor", "survey_rover_mk3": "Mk III Explorer",
              "mining_crawler": "Mining Crawler", "construction_rover": "Construction Rover"}  # the units have no name field


def name_of_unit(u):
    return UNITS.get(u, {}).get("name") or UNIT_NAMES.get(u, u.replace("_", " "))


def mmss(seconds):
    return f"{int(seconds) // 60}:{int(seconds) % 60:02d}"


def rule_text(t):
    k = t.get("kind")
    if k == "milestone":
        m = t.get("milestone", "")
        if m.startswith("built:"):
            s = f"a {name_of_building(m[6:])} is finished"
        elif m.startswith("unit:"):
            s = f"a {name_of_unit(m[5:])} rolls out of a Vehicle Bay"
        else:
            s = MILESTONES.get(m, m)
        if "before_seconds" in t:
            s += f", before {mmss(t['before_seconds'])} of game time"
        if "without" in t:
            s += f", with no {name_of_building(t['without'])} finished before it"
        return "Milestone: " + s
    if k == "built":
        n = t.get("count", 1)
        return f"Built: {n} × {name_of_building(t.get('building', ''))} finished at once"
    if k in ("count", "view_count"):
        what = MEASURES.get(t.get("measure", ""), ("", t.get("measure", "")))[1]
        target = "all" if t.get("all") else f"{t.get('at_least')}"
        return ("Counted by the view: " if k == "view_count" else "Count: ") + f"{target} {what}"
    if k == "share":
        return f"Share: {round(t.get('at_least', 0) * 100):g}% {MEASURES.get(t.get('measure', ''), ('', ''))[1]}"
    if k == "unit_counter":
        return f"One vehicle: {t.get('at_least')} hexes driven ({', '.join(name_of_unit(u) for u in t.get('units', []))})"
    if k == "event":
        e = t.get("event", "")
        fields, text = EVENTS.get(e, ([], e))
        extra = []
        for f in fields:
            if f in t:
                v = t[f]
                extra.append(CLASSES.get(v, {}).get("name", v).split(" — ")[0] if f == "class" else
                             f"≥ {v:,}" if f == "amount" else f"≥ {v} s" if f == "delay_seconds" else f"≤ {v} s")
        return f"Event: {text}" + (f" ({'; '.join(extra)})" if extra else "")
    if k == "streak":
        s = f"Streak: {t.get('seconds', 0) // 60} min in a row with {CONDITIONS.get(t.get('condition', ''), t.get('condition', ''))}"
        return s + (", from the start of the game" if t.get("from_start") else "")
    if k == "set":
        return "Set: " + ", ".join(WEATHER.get(w, w) for w in t.get("contains", [])) + " seen in one game"
    if k in ("all_of", "any_of"):
        joined = (" and " if k == "all_of" else " or ").join(rule_text(r) for r in t.get("rules", []))
        return ("All of: " if k == "all_of" else "Any of: ") + joined
    return k or "?"


def reward_html(c):
    if c is None:
        return '<span class="bad">unknown reward</span>'
    kind = KIND_TEXT.get(c["kind"], c["kind"])
    v = c["value"]
    if c["kind"] in ("paint", "beam_colour", "dome_tint"):
        look = f'<i class="swatch" style="background:{E(v)}"></i>'
        if c.get("colour2"):
            look += f'<i class="swatch" style="background:{E(c["colour2"])}"></i>'
    elif c["kind"] == "hull_number":
        look = f'<span class="plate">{int(v):02d}</span>'
    elif c["kind"] in ("rover_name", "dropship_name"):
        look = f'<span class="plate">{E(v)}</span>'
    else:
        look = f'<span class="tag">{E(v.replace("_", " "))}</span>'
    return f'Reward: {look} {E(c["name"])} <span class="tag">{E(kind)}</span>'


# ---------------------------------------------------------------- checks (a mirror of the game's main ones)
checks = []
ids = [a["id"] for a in LIST]
for i in sorted({x for x in ids if ids.count(x) > 1}):
    checks.append(f"Achievement {i} is listed more than once.")
rewards = {}
cat_ids = {c["id"] for c in CATS}
techs_known = {t["id"] for t in TECHS}


def check_trigger(t, where, depth):
    k = t.get("kind")
    if k not in KIND_FIELDS:
        checks.append(f"{where}: unknown trigger kind '{k}'.")
        return
    allowed = set(KIND_FIELDS[k]) | (set(EVENTS.get(t.get("event", ""), ([], ""))[0]) if k == "event" else set())
    for f in t:
        if f != "kind" and f not in allowed:
            checks.append(f"{where}: a {k} trigger has no field '{f}'.")
    if k == "milestone":
        m = t.get("milestone", "")
        if not (m in MILESTONES or (m.startswith("built:") and m[6:] in BUILDINGS) or (m.startswith("unit:") and m[5:] in UNITS)):
            checks.append(f"{where}: unknown milestone '{m}'.")
        if "without" in t and t["without"] not in BUILDINGS:
            checks.append(f"{where}: unknown building '{t['without']}'.")
    if k == "built" and t.get("building") not in BUILDINGS:
        checks.append(f"{where}: unknown building '{t.get('building')}'.")
    if k in ("count", "share", "set", "view_count") and MEASURES.get(t.get("measure", ""), ("",))[0] != k:
        checks.append(f"{where}: unknown {k} measure '{t.get('measure')}'.")
    if k == "unit_counter":
        for u in t.get("units", []):
            if u not in UNITS:
                checks.append(f"{where}: unknown unit '{u}'.")
    if k == "event" and t.get("event") not in EVENTS:
        checks.append(f"{where}: unknown event '{t.get('event')}'.")
    if k == "event" and "class" in t and t["class"] not in CLASSES:
        checks.append(f"{where}: unknown planet class '{t['class']}'.")
    if k == "streak":
        if t.get("condition") not in CONDITIONS:
            checks.append(f"{where}: unknown condition '{t.get('condition')}'.")
        if not 0 < t.get("seconds", 0) <= 14400:
            checks.append(f"{where}: seconds must be 1 to 14400.")
    if k == "set":
        for w in t.get("contains", []):
            if w not in WEATHER:
                checks.append(f"{where}: unknown weather kind '{w}'.")
    for f in ("at_least", "count", "before_seconds", "amount", "delay_seconds", "within_seconds"):
        if f in t and not (isinstance(t[f], (int, float)) and t[f] > 0):
            checks.append(f"{where}: {f} must be positive.")
    if k in ("all_of", "any_of"):
        if depth > 0:
            checks.append(f"{where}: {k} can't be nested.")
        for r in t.get("rules", []):
            check_trigger(r, where, depth + 1)


for a in LIST:
    w = f"Achievement {a['id']}"
    if not re.fullmatch(r"[a-z][a-z0-9_]*", a["id"]):
        checks.append(f"{w}: ids are lower_snake.")
    if a["category"] not in cat_ids:
        checks.append(f"{w}: unknown category '{a['category']}'.")
    if a["tier"] not in TIERS:
        checks.append(f"{w}: unknown tier '{a['tier']}'.")
    if a["icon"] not in ICON_NAMES:
        checks.append(f"{w}: unknown icon '{a['icon']}'.")
    if a["icon"] not in ICONS:
        checks.append(f"{w}: the mockup has no '{a['icon']}' icon.")
    r = a.get("reward")
    if r not in COSMETICS:
        checks.append(f"{w}: unknown reward '{r}'.")
    elif r in rewards:
        checks.append(f"{w}: reward {r} is already {rewards[r]}'s.")
    else:
        rewards[r] = a["id"]
    check_trigger(a["trigger"], w, 0)
    for c in a.get("worlds", {}).get("classes", []):
        if c not in CLASSES:
            checks.append(f"{w}: unknown planet class '{c}' in worlds.")
for c in A["cosmetics"]:
    if c["id"] not in rewards:
        checks.append(f"Cosmetic {c['id']} is no achievement's reward.")
scholar = next((a for a in LIST if a["id"] == "scholar"), None)
if scholar and f"all {len(TECHS)}" not in scholar["description"]:
    checks.append(f"Scholar's description should say all {len(TECHS)} surface techs.")

# ---------------------------------------------------------------- page
n = 0
sections = []
for i, cat in enumerate(CATS):
    cards = []
    for a in [x for x in LIST if x["category"] == cat["id"]]:
        n += 1
        tier = TIERS.get(a["tier"], {"name": a["tier"], "points": 0})
        tags = [f'<span class="tag" style="color:{tier.get("colour", "#888")};border-color:{tier.get("colour", "#888")}">{E(tier["name"])} · {tier["points"]}</span>']
        if a.get("hidden"):
            tags.append('<span class="tag hidden">hidden</span>')
        if a.get("progress"):
            tags.append('<span class="tag bar">progress bar</span>')
        if a["trigger"].get("kind") == "view_count":
            tags.append('<span class="tag view">counted by the view</span>')
        worlds = a.get("worlds")
        wtext = ""
        if worlds:
            parts = []
            if worlds.get("classes"):
                parts.append(", ".join(CLASSES.get(c, {}).get("name", c).split(" — ")[0] for c in worlds["classes"]))
            if "min_gravity_g" in worlds:
                parts.append(f"gravity ≥ {worlds['min_gravity_g']} g")
            if "max_gravity_g" in worlds:
                parts.append(f"gravity ≤ {worlds['max_gravity_g']} g")
            if worlds.get("tier"):
                parts.append(f"{worlds['tier']} tier")
            wtext = f'<p class="rule">Only on worlds with {E(", ".join(parts))}.</p>'
        cards.append(f'<article class="card">{medal(a, n)}<h3>{E(a["name"])} <small>{E(a["id"])}</small></h3>'
                     f'<div class="tags">{"".join(tags)}</div><p>{E(a["description"])}</p>'
                     f'<p class="rule">{E(rule_text(a["trigger"]))}</p>{wtext}<p class="reward">{reward_html(COSMETICS.get(a.get("reward")))}</p></article>')
    colour = ["var(--gold)", "var(--sky)", "var(--orange)", "var(--periwinkle)", "var(--lilac)", "var(--good)", "var(--peach)", "var(--bad)"][i % 8]
    count = len(cards)
    sections.append(f'  <section id="{E(cat["id"])}" style="--sec: {colour}">\n'
                    f'    <div class="sechead"><span class="cap"></span><div><span class="eyebrow">Category {cat["order"]}</span><h2>{E(cat["name"])}</h2>'
                    f'<p>{count} achievement{"s" if count != 1 else ""}.</p></div></div>\n'
                    f'    <div class="cards">{"".join(cards)}</div>\n  </section>')

points = sum(TIERS.get(a["tier"], {"points": 0})["points"] for a in LIST)
tiles = [f'<div class="tile" style="--t:var(--gold)"><b>{len(LIST)}</b><span>achievements</span></div>',
         f'<div class="tile" style="--t:var(--peach)"><b>{points}</b><span>points in all</span></div>']
for t in A["tiers"]:
    k = sum(1 for a in LIST if a["tier"] == t["id"])
    tiles.append(f'<div class="tile" style="--t:{t["colour"]}"><b>{k}</b><span>{E(t["name"])} · {t["points"]} points each</span></div>')
tiles.append(f'<div class="tile" style="--t:var(--lilac)"><b>{sum(1 for a in LIST if a.get("hidden"))}</b><span>hidden</span></div>')
tiles.append(f'<div class="tile" style="--t:var(--periwinkle)"><b>{len(A["cosmetics"])}</b><span>cosmetics</span></div>')

cos_rows = ['<thead><tr><th scope="col">Cosmetic</th><th scope="col">Kind</th><th scope="col">Value</th><th scope="col">Pattern</th><th scope="col">Earned by</th></tr></thead><tbody>']
by_id = {a["id"]: a for a in LIST}
for c in A["cosmetics"]:
    v = c["value"]
    look = f'<i class="swatch" style="background:{E(v)}"></i> {E(v)}' if v.startswith("#") else E(v)
    pat = E(c.get("pattern") or "")
    if c.get("colour2"):
        pat += f' <i class="swatch" style="background:{E(c["colour2"])}"></i> {E(c["colour2"])}'
    owner = rewards.get(c["id"])
    cos_rows.append(f'<tr><th scope="row">{E(c["name"])}<span class="tag" style="margin-left:6px">{E(c["id"])}</span></th>'
                    f'<td>{E(KIND_TEXT.get(c["kind"], c["kind"]))}</td><td>{look}</td><td>{pat}</td>'
                    f'<td>{E(by_id[owner]["name"]) if owner else "—"}</td></tr>')
cos_rows.append("</tbody>")

kinds = [
    ("milestone", "milestone, before_seconds, without", "A one-time moment: " + "; ".join(f"{k} ({v})" for k, v in MILESTONES.items()) + "; built:&lt;building&gt;; unit:&lt;unit&gt;."),
    ("built", "building, count", "That many finished buildings of one id at once."),
    ("count", "measure, at_least | all", "; ".join(f"{k} ({v})" for k, (kk, v) in MEASURES.items() if kk == "count") + "."),
    ("share", "measure, at_least (0–1)", "surveyed: the share of the map's tiles the player has surveyed."),
    ("unit_counter", "counter, at_least, units", "tiles_driven: hex edges one vehicle of those units has crossed."),
    ("event", "event + its fields", "; ".join(f"{k}" + (f" [{', '.join(f)}]" if f else "") for k, (f, _) in EVENTS.items()) + "."),
    ("streak", "condition, seconds, from_start", "; ".join(f"{k} ({v})" for k, v in CONDITIONS.items()) + "; from_start = must hold from the first check."),
    ("set", "measure, contains", "weather_seen: weather kinds over the player's vehicles and cores."),
    ("view_count", "measure, at_least | all", "ships_inspected: counted by the game's view (the sim never unlocks it)."),
    ("all_of / any_of", "rules", "Every / any of the rules (one level, no view_count inside)."),
]
kind_rows = ['<thead><tr><th scope="col">Kind</th><th scope="col">Fields</th><th scope="col">Names</th></tr></thead><tbody>']
for k, f, names in kinds:
    used = sum(1 for a in LIST if a["trigger"].get("kind") in k.split(" / "))
    kind_rows.append(f'<tr><th scope="row">{E(k)}</th><td><code>{E(f)}</code></td><td>{names} <span class="tag">{used} used</span></td></tr>')
kind_rows.append("</tbody>")

readout = (f"<span><b>{len(LIST)}</b> achievements</span><span><b>{len(CATS)}</b> categories</span>"
           f"<span><b>{points}</b> points</span><span><b>{len(A['cosmetics'])}</b> cosmetics</span>")
nav = '<a href="#summary">Summary</a>' + "".join(f'<a href="#{E(c["id"])}">{E(c["name"])}</a>' for c in CATS) + \
      '<a href="#cosmetics">Cosmetics</a><a href="#kinds">Trigger kinds</a><a href="#checks">Data checks</a>'
check_html = "".join(f'<li class="bad">{E(c)}</li>' for c in checks) or '<li class="ok">No problems found.</li>'

tpl = open(os.path.join(HERE, "achievements_page.html"), encoding="utf-8").read()
page = (tpl.replace("/*READOUT*/", readout).replace("/*NAV*/", nav).replace("/*TILES*/", "".join(tiles))
        .replace("/*CATEGORIES*/", "\n\n".join(sections)).replace("/*COSMETICS*/", "".join(cos_rows))
        .replace("/*KINDS*/", "".join(kind_rows)).replace("/*CHECKS*/", check_html))
open(OUT, "w", encoding="utf-8").write(page)
print("wrote", OUT, len(page), "bytes;", len(checks), "check problems")
for c in checks:
    print("  !", c)
