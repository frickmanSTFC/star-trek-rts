"""Write galaxy-map.html: a shareable map of known space with territories, warp lanes and travel times.

Usage: python3 tools/gen_galaxy_map_page.py [OUT_HTML]
Reads data/galaxy_map.json, factions.json, eras.json and tech_tree.json, and fills tools/galaxy_map_page.html.
"""
import heapq
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from faction_meta import FACTIONS, NAMES  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "galaxy-map.html")


def load(name):
    return json.load(open(os.path.join(ROOT, "data", name)))


G = load("galaxy_map.json")
FAC = {f["id"]: f for f in load("factions.json")["factions"]}
ERAS = load("eras.json")["eras"]
TECH = {t["id"]: t for t in load("tech_tree.json")["techs"]}
SYS = {s["id"]: s for s in G["systems"]}
CORE = G["core"]
LOCAL_R = 1000  # systems this close to Sol make up the local-space view


def dist(a, b):
    return math.hypot(SYS[a]["x"] - SYS[b]["x"], SYS[a]["y"] - SYS[b]["y"])


def quadrant(s):
    x, y = s["x"], s["y"]
    if math.hypot(x - CORE["x"], y - CORE["y"]) < 3000:
        return "Galactic core"
    if y < CORE["y"]:
        return "Alpha/Beta border" if abs(x) < 3 else ("Alpha" if x < 0 else "Beta")
    return "Gamma" if x < 0 else "Delta"


lanes = []
for l in G["lanes"]:
    lanes.append({**l, "ly": round(dist(l["a"], l["b"]), 1)})

# lane graph for routes by warp (wormholes count as near-instant, transwarp is Borg only,
# and the unstable Barzan wormhole is too unreliable to plan around)
adj = {s: [] for s in SYS}
for l in lanes:
    if l["kind"] in ("transwarp", "unstable_wormhole"):
        continue
    w = 0.1 if "wormhole" in l["kind"] else l["ly"]
    adj[l["a"]].append((l["b"], w))
    adj[l["b"]].append((l["a"], w))


def route(a, b):
    best, prev, todo = {a: 0}, {}, [(0, a)]
    while todo:
        d0, u = heapq.heappop(todo)
        if u == b:
            break
        if d0 > best[u]:
            continue
        for v, w in adj[u]:
            if d0 + w < best.get(v, 1e18):
                best[v], prev[v] = d0 + w, u
                heapq.heappush(todo, (d0 + w, v))
    if b not in best:
        return None
    path, u = [b], b
    while u != a:
        u = prev[u]
        path.append(u)
    return {"ly": round(best[b], 1), "path": path[::-1]}


def eras_of(fid):
    return [e["id"] for e in ERAS if fid in e["playable"] + e.get("ai_only", [])]


factions = {}
for fid in sorted({s["faction"] for s in G["systems"] if s["faction"]} | {a for s in G["systems"] for a in s.get("also", [])}):
    f = FAC.get(fid, {})
    factions[fid] = {"id": fid, "name": NAMES.get(fid) or f.get("name", fid), "full": f.get("name", fid), "eras": eras_of(fid),
                     "great": fid in FACTIONS}

territories = []
for fid, f in factions.items():
    mine = [s for s in G["systems"] if s["faction"] == fid]
    if not mine:
        continue
    cap = next((s for s in mine if s["kind"] == "capital"), mine[0])
    territories.append({"id": fid, "name": f["full"], "capital": cap["name"], "systems": len(mine), "quadrant": quadrant(cap),
                        "fromSol": round(math.hypot(cap["x"], cap["y"])), "eras": f["eras"], "great": f["great"]})
territories.sort(key=lambda t: (not t["great"], t["fromSol"]))

CAPS = ["sol", "qonos", "romulus", "cardassia", "ferenginar", "omarion", "unicomplex"]
matrix = [[{"straight": round(dist(a, b)), "route": (route(a, b) or {}).get("ly")} for b in CAPS] for a in CAPS]

drives = []
for d in G["drives"]:
    t = TECH.get(d["tech"], {})
    drives.append({**d, "techName": t.get("name", d["tech"]), "tier": t.get("tier")})

checks = []
deg = {s: 0 for s in SYS}
for l in lanes:
    deg[l["a"]] += 1
    deg[l["b"]] += 1
    for end in (l["a"], l["b"]):
        if end not in SYS:
            checks.append(f"Lane {l['a']} to {l['b']} names an unknown system.")
loose = [SYS[s]["name"] for s, n in deg.items() if n == 0 and SYS[s]["kind"] != "landmark"]
if loose:
    checks.append(f"Systems with no lanes: {', '.join(loose)}.")
nohome = sorted(set(FAC) - set(factions))
if nohome:
    checks.append(f"Factions with no system on the map: {', '.join(FAC[f]['name'] for f in nohome)}.")
for fid, f in factions.items():
    mine = [s for s in G["systems"] if s["faction"] == fid]
    if any(s["kind"] == "colony" for s in mine) and not any(s["kind"] == "capital" for s in mine) and not any(fid in s.get("also", []) for s in G["systems"]):
        checks.append(f"{f['full']} has systems but no capital.")
far = [s for s in G["systems"] if math.hypot(s["x"], s["y"]) > LOCAL_R]
unreach = [s["name"] for s in far if s["kind"] != "landmark" and route("sol", s["id"]) is None]
if unreach:
    checks.append(f"Not reachable from Sol by lanes or wormholes (Borg transwarp aside): {', '.join(unreach)}.")

data = {
    "systems": [{**s, "quadrant": quadrant(s), "local": math.hypot(s["x"], s["y"]) < LOCAL_R, "fromSol": round(math.hypot(s["x"], s["y"]), 1)} for s in G["systems"]],
    "lanes": lanes, "factions": factions, "territories": territories, "core": CORE,
    "eras": [{"id": e["id"], "name": e["name"], "years": e["years"]} for e in ERAS],
    "warp": G["warp_scale"]["factors"], "warpNote": G["warp_scale"]["_notes"], "drives": drives,
    "caps": CAPS, "matrix": matrix, "checks": checks, "notes": G["_notes"],
}
tpl = open(os.path.join(HERE, "galaxy_map_page.html")).read()
html = tpl.replace("/*DATA*/", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(OUT, "w").write(html)
print("wrote", OUT, len(html), "bytes")
