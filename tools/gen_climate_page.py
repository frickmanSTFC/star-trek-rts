"""Write climate.html: day and night, seasons and weather for every body of the system map.

Usage: python3 tools/gen_climate_page.py [OUT_HTML]
Reads data/climate.json, maps.json, planet_classes.json and surfaces.json (environments), and fills
tools/climate_page.html. The numbers mirror game/sim/Surface/Climate.cs (mean temperature from sunlight,
albedo and greenhouse; the equator's share of the polar drop; seasons and weather odds per climate template).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import data_loader  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "climate.html")


def load(name):
    return data_loader.load(name)


C = load("climate.json")
MAPS = load("maps.json")["maps"]
CLASSES = {c["id"]: c for c in load("planet_classes.json")["classes"]}
ENVS = {e["class"]: e for e in load("surfaces.json").get("environments", [])}
SURF = C["surface"]
KINDS = [w["id"] for w in C["weather"]]
WNAME = {w["id"]: w["name"] for w in C["weather"]}
# Eight weather groups for the odds bars (the eleven kinds folded; the exact kinds are listed under each bar).
GROUPS = [
    ("rain", "Rain", ["rain"]),
    ("dust", "Dust", ["dust", "dust_storm"]),
    ("fog", "Fog", ["fog"]),
    ("clear", "Clear", ["clear"]),
    ("wind", "High winds", ["high_wind"]),
    ("cloud", "Cloud", ["cloudy", "overcast"]),
    ("snow", "Snow", ["snow", "blizzard"]),
    ("storm", "Storm", ["storm"]),
]

checks = []
mp = MAPS[0]
bodies_on_map = {b["id"]: b for m in MAPS for b in m["bodies"]}
star = next((s for s in C["stars"] if s["class"] == mp["star"]["class"]), None)
if star is None:
    checks.append(f"The map's star class {mp['star']['class']} has no entry in climate.json stars.")
lum = star["luminosity"] if star else 1
home = next(s["home"] for s in sorted(mp["starts"], key=lambda s: s["slot"]))
home_year = C["bodies"][home]["year_days"] * C["bodies"][home]["spin"]["day_seconds"]


def orbit_of(bid):
    b = C["bodies"][bid]
    p = b["orbit"].get("parent")
    return C["bodies"][p]["orbit"] if p else b["orbit"]


def mean_temp(b, s):
    return 278.6 * ((1 - b["albedo"]) * s) ** 0.25 - 273.15 + b["greenhouse_c"]


def minutes(sec):
    m = sec / 60
    return f"{m:.0f} min" if m >= 10 else f"{m:.1f} min".replace(".0 min", " min")


def play_time(sec):
    if sec >= 7200:
        return f"{sec / 3600:.1f} h"
    return minutes(sec) if sec >= 60 else f"{sec:.0f} s"


def season_rows(template, seasons):
    rows = []
    for s in seasons:
        w = {k: s["weights"].get(k, 0) for k in KINDS}
        total = sum(w.values()) or 1
        groups = [{"id": g, "name": n, "pct": 100 * sum(w[k] for k in ks) / total,
                   "kinds": [f"{WNAME[k]} {100 * w[k] / total:.0f}%" for k in ks if w[k] > 0]} for g, n, ks in GROUPS]
        rows.append({"name": s["name"], "share": s["share"], "temp": s["temp_c"], "hum": s["humidity_pct"], "wind": s["wind"],
                     "spell": s["spell_s"], "groups": groups,
                     "top": ", ".join(f"{WNAME[k].lower()} {100 * v / total:.0f}%" for k, v in sorted(w.items(), key=lambda kv: -kv[1]) if v > 0)})
    return rows


def light_notes(bid, b):
    spin = b["spin"]
    lock = spin.get("locked", "none")
    day = spin["day_seconds"]
    notes = []
    if lock == "star":
        notes.append(f"Permanent light and dark sides: the star stands still over {spin.get('facing', 0) * 100:.0f}% of the map's width; the far side never sees daylight.")
    elif lock == "parent":
        parent = bodies_on_map[b["orbit"]["parent"]]["name"]
        notes.append(f"Locked to {parent}: one face always sees {parent} in its sky. Its day is one orbit around {parent}: {minutes(day)} of play ({spin['day_hours']:g} local hours), so each side spends about {minutes(day / 2)} in light, then as long in the dark.")
    else:
        rise = "west" if spin.get("direction") == "retrograde" else "east"
        notes.append(f"Spins {spin.get('direction', 'prograde')}: the sun rises in the {rise}, and the line between day and night crosses the whole map once every {minutes(day)} ({spin['day_hours']:g} local hours).")
    tilt = b["axial_tilt_deg"]
    span = SURF["latitude_span_deg"]
    if tilt >= 1:
        circle = 90 - tilt
        polar = (f"polar day and night beyond {circle:.0f}°, inside the map's ±{span:g}° rows" if circle < span
                 else f"no polar night on the map (its rows reach ±{span:g}°, the polar circles are at {circle:.0f}°)")
        notes.append(f"Tilted {tilt:g}°: the overhead sun swings between {tilt:g}° N and S over the year, so the hemispheres take turns at summer; {polar}.")
    else:
        notes.append("Barely tilted: the sun stays over the equator all year, so there are no real seasons from the sky.")
    return notes


distinct = []
twin_of = {}
for b in mp["bodies"]:
    if b["id"] in twin_of:
        continue
    mirror = next((o for o in mp["bodies"] if o["pos"] == [-b["pos"][0], -b["pos"][1]] and o["id"] != b["id"]), None)
    if mirror:
        twin_of[mirror["id"]] = b["id"]
        x = dict(C["bodies"].get(b["id"], {}))
        y = dict(C["bodies"].get(mirror["id"], {}))
        for k in ("notes",):
            x.pop(k, None)
            y.pop(k, None)
        if x.get("orbit", {}).get("parent"):
            x["orbit"] = dict(x["orbit"], parent=None)
            y["orbit"] = dict(y["orbit"], parent=None)
        if x != y:
            checks.append(f"{b['name']} and its mirror {mirror['name']} have different climate numbers (the duel map is point-symmetric, so they should match).")
    distinct.append((b, mirror))

table = []
cards = []
for b in mp["bodies"]:
    if b["id"] not in C["bodies"]:
        checks.append(f"{b['name']} ({b['id']}) has no entry in climate.json bodies.")
        continue
    cb = C["bodies"][b["id"]]
    o = orbit_of(b["id"])
    au = o["au"]
    e = o.get("eccentricity", 0)
    s_mean = lum / au ** 2
    dist = math.hypot(*b["pos"])
    parent = cb["orbit"].get("parent")
    env = ENVS.get(b["class"])
    tpl = C["climates"].get(cb["climate"])
    if tpl is None:
        checks.append(f"{b['name']}: unknown climate template '{cb['climate']}'.")
        continue
    polar = env["temperature_c"]["polar_drop"] if env else tpl["polar_drop_c"]
    swing = (env["temperature_c"]["day"] - env["temperature_c"]["night"]) / 2 if env else tpl["swing_c"]
    t_mean = mean_temp(cb, s_mean)
    t_eq = t_mean + polar * SURF["equator_share"]
    t_pole = t_eq - polar
    year_s = cb["year_days"] * cb["spin"]["day_seconds"]
    row = {
        "id": b["id"], "name": b["name"], "cls": CLASSES.get(b["class"], {}).get("name", b["class"]), "classId": b["class"],
        "parent": bodies_on_map[parent]["name"] if parent else "", "twin": twin_of.get(b["id"], ""),
        "mapDist": round(dist), "derivedAu": round(dist * SURF["au_per_map_unit"], 2), "au": au, "ecc": e,
        "sun": round(s_mean, 3), "sunMin": round(lum / (au * (1 + e)) ** 2, 3), "sunMax": round(lum / (au * (1 - e)) ** 2, 3),
        "tMean": round(t_mean), "tEqDay": round(t_eq + swing), "tEqNight": round(t_eq - swing), "tPole": round(t_pole),
        "day": cb["spin"]["day_seconds"], "dayText": play_time(cb["spin"]["day_seconds"]), "dayHours": cb["spin"]["day_hours"],
        "dir": cb["spin"].get("direction", "prograde"), "lock": cb["spin"].get("locked", "none"),
        "tilt": cb["axial_tilt_deg"], "yearDays": cb["year_days"], "yearText": play_time(year_s),
        "climate": tpl["name"], "climateId": cb["climate"], "air": tpl["air"], "landable": cb.get("landable", False),
    }
    table.append(row)

    # Year checks: planets roughly follow Kepler from the home world's year; a moon's year is its planet's.
    if parent:
        py = C["bodies"][parent]["year_days"] * C["bodies"][parent]["spin"]["day_seconds"]
        if abs(year_s - py) > py / 50:
            checks.append(f"{b['name']}: its year ({year_s:.0f} s) should match {bodies_on_map[parent]['name']}'s ({py:.0f} s).")
    else:
        kepler = home_year * au ** 1.5
        if abs(year_s - kepler) > kepler / 4:
            checks.append(f"{b['name']}: a year of {year_s:.0f} s is far from Kepler's {kepler:.0f} s for {au} AU (from {bodies_on_map[home]['name']}'s year).")
    if env and (env["pressure_kpa"] > 0) != tpl["air"]:
        checks.append(f"{b['name']}: climate '{cb['climate']}' {'has' if tpl['air'] else 'has no'} weather but the class environment {'has no' if tpl['air'] else 'has an'} atmosphere.")

for b, mirror in distinct:
    if b["id"] not in C["bodies"]:
        continue
    cb = C["bodies"][b["id"]]
    tpl = C["climates"][cb["climate"]]
    seasons = cb.get("seasons") or tpl["seasons"]
    cards.append({
        "id": b["id"], "name": b["name"], "twin": mirror["name"] if mirror else "",
        "notes": cb.get("notes", ""), "light": light_notes(b["id"], cb),
        "climate": tpl["name"], "air": tpl["air"], "windScale": tpl["wind_scale"], "drift": tpl["drift_tiles_per_min"],
        "seasons": season_rows(tpl, seasons),
        "bands": [{"name": x["name"], "to": x["to_deg"], "scale": x["season_scale"], "toward": x["wind_toward_deg"],
                   "mult": ", ".join(f"{WNAME[k].lower()} ×{v:g}" for k, v in x["weights"].items())} for x in tpl["bands"]],
    })

# Climate-level checks.
for tid, t in C["climates"].items():
    for s in t["seasons"]:
        if sum(s["weights"].values()) <= 0:
            checks.append(f"Climate {tid} season {s['name']}: the weather weights need a positive sum.")
        if any(v < 0 for v in s["weights"].values()):
            checks.append(f"Climate {tid} season {s['name']}: negative weather weights.")
        for k in s["weights"]:
            if k not in KINDS:
                checks.append(f"Climate {tid} season {s['name']}: unknown weather kind '{k}'.")
    total = sum(s["share"] for s in t["seasons"])
    if abs(total - 1) > 0.001:
        checks.append(f"Climate {tid}: season shares add up to {total:g}, not 1.")
for k in ["clear", "cloudy", "overcast", "fog", "rain", "storm", "snow", "blizzard", "dust", "dust_storm", "high_wind"]:
    if KINDS.count(k) != 1:
        checks.append(f"Weather '{k}' must be listed exactly once.")
for cid in {b["class"] for b in mp["bodies"]}:
    if cid not in C.get("classes", {}):
        checks.append(f"Planet class {cid} has no default climate template in climate.json classes.")

weather = [{
    "id": w["id"], "name": w["name"], "cloud": w["cloud"], "vis": w["visibility"], "precip": w["precip_mm_h"], "wind": w["wind_ms"],
    "fx": w["effects"],
} for w in C["weather"]]
terrain = [{"name": t["name"], "weather": [WNAME[k] for k in t["weather"]], "terrain": t["terrain"], "speed": t["rover_speed"], "damage": t["rover_damage"]} for t in C["terrain"]]
used = {}
for cid, tid in C.get("classes", {}).items():
    used.setdefault(tid, []).append(CLASSES.get(cid, {}).get("name", cid).split(" — ")[0])
templates = [{"id": tid, "name": t["name"], "air": t["air"], "seasons": len(t["seasons"]), "classes": used.get(tid, []),
              "bodies": [bodies_on_map[b]["name"] for b, cb in C["bodies"].items() if cb["climate"] == tid and b in bodies_on_map]}
             for tid, t in C["climates"].items()]

data = {
    "star": {"name": mp["star"]["name"], "cls": mp["star"]["class"], "lum": lum, "colour": star["colour"] if star else "#fff", "k": star["colour_k"] if star else 0},
    "map": mp["name"], "home": bodies_on_map[home]["name"],
    "bodies": [r for p in sorted((r for r in table if not r["parent"]), key=lambda r: (r["au"], r["name"]))
               for r in [p] + sorted((m for m in table if m["parent"] == p["name"]), key=lambda m: m["name"])], "cards": cards,
    "groups": [{"id": g, "name": n, "kinds": [WNAME[k] for k in ks]} for g, n, ks in GROUPS],
    "weather": weather, "terrain": terrain, "templates": templates, "surface": SURF,
    "checks": checks,
}
tpl = open(os.path.join(HERE, "climate_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
