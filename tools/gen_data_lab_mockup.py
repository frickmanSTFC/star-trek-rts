"""Data Lab mockups: builds docs/mockups/data-editor/index.html from tools/data_lab_mockup.html + a snapshot of
data/*.json (replaces /*DATA*/null). Mockup only; nothing in the game reads it. Usage: python3 tools/gen_data_lab_mockup.py"""
import json, os, subprocess, sys

here = os.path.dirname(os.path.abspath(__file__))
repo = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(here)
tpl = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, 'data_lab_mockup.html')
D = lambda f: json.load(open(os.path.join(repo, 'data', f)))
texts = {f: open(os.path.join(repo, 'data', f)).read() for f in sorted(os.listdir(os.path.join(repo, 'data'))) if f.endswith('.json')}

SIM = ["factions.json", "ships.json", "resources.json", "planet_classes.json", "buildings.json", "maps.json", "surfaces.json", "vehicles.json", "climate.json"]

def count(v, c):
    if isinstance(v, dict):
        c['obj'] += 1
        for x in v.values(): count(x, c)
    elif isinstance(v, list):
        c['list'] += 1
        for x in v: count(x, c)
    elif isinstance(v, bool): c['bool'] += 1
    elif isinstance(v, (int, float)): c['num'] += 1
    elif isinstance(v, str): c['str'] += 1
    else: c['null'] += 1

def depth(v):
    if isinstance(v, dict): return 1 + max([depth(x) for x in v.values()] + [0])
    if isinstance(v, list): return 1 + max([depth(x) for x in v] + [0])
    return 0

files = []
for f, t in texts.items():
    d = json.loads(t)
    c = dict(obj=0, list=0, bool=0, num=0, str=0, null=0)
    count(d, c)
    tables = []
    for k, v in d.items():
        if k.startswith('_'): continue
        n = len(v) if isinstance(v, (list, dict)) else 1
        kind = 'list' if isinstance(v, list) else ('map' if isinstance(v, dict) else type(v).__name__)
        tables.append([k, kind, n])
    files.append(dict(file=f, bytes=len(t.encode()), lines=t.count('\n') + 1, sim=f in SIM, counts=c, depth=depth(d), tables=tables,
                      notes=d.get('_notes', '')[:400]))

b = D('buildings.json')
s = D('surfaces.json')
cl = D('climate.json')
mp = D('maps.json')
res = D('resources.json')

menu = s['build_menu']
cores = ['colony_hub', 'colonial_administration', 'planetary_capital']
order = cores + menu
bmap = {x['id']: x for x in b['buildings']}
buildings = []
for i in order:
    x = dict(bmap[i])
    x.pop('description', None)
    buildings.append(x)

# References: where a building id appears as a string elsewhere (for the delete check).
def refs(idv):
    out = []
    def walk(v, path, f):
        if isinstance(v, dict):
            for k, x in v.items(): walk(x, path + [k], f)
        elif isinstance(v, list):
            for j, x in enumerate(v):
                name = x.get('id') if isinstance(x, dict) and 'id' in x else str(j)
                walk(x, path + [name], f)
        elif v == idv:
            out.append(f + ' › ' + ' › '.join(path))
    for f, t in texts.items():
        walk(json.loads(t), [], f)
    return [r for r in out if not r.endswith('› id') or 'buildings.json › buildings' not in r]

ref = {i: refs(i) for i in order}

units = []
for u in s['units']:
    x = {k: v for k, v in u.items() if k not in ('description',)}
    units.append(x)

def find(f, needle):
    ls = texts[f].split('\n')
    hits = [i for i, l in enumerate(ls) if needle in l]
    assert len(hits) == 1, (f, needle, hits)
    return [hits[0] + 1, ls[hits[0]]]

data = dict(
    commit=subprocess.run(['git', '-C', repo, 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True).stdout.strip(),
    files=files,
    buildings=buildings,
    refs=ref,
    buildingNotes=b['_notes'],
    surfacesNotes=s['_notes'],
    climateNotes=cl['_notes'],
    terrain=s['terrain'],
    planet=s['planets'][0],
    landing=s['landing'],
    research=[dict(id=r['id'], name=r['name'], cost=r['cost'], requires=r['requires']) for r in s['research']],
    units=units,
    climate=dict(bodies=cl['bodies'], climates=cl['climates'], weather=cl['weather'], surface=cl['surface'], terrain=cl['terrain'], stars=cl['stars'], classes=cl['classes']),
    mapBodies=[dict(id=x['id'], name=x['name'], cls=x['class'], parent=x['parent']) for x in mp['maps'][0]['bodies']],
    star=mp['maps'][0]['star'],
    resources={r['id']: dict(name=r['name'], tier=r['tier']) for r in res['resources']},
    environments={e['class']: dict(sky=e['sky'], day=e['temperature_c']['day'], night=e['temperature_c']['night'], gravity=e['gravity_g']) for e in s['environments']},
    maintenance=s['maintenance'],
    lines={k: find(*v) for k, v in {
        'refinery': ('buildings.json', '"build_seconds": 30, "power_use": 8, "storage": 300'),
        'storage': ('buildings.json', '"build_seconds": 15, "storage": 2000'),
        'summer': ('climate.json', '"name": "Summer", "share": 0.25, "temp_c": 7'),
        'tarsis': ('climate.json', '"tarsis":     {'),
    }.items()},
)
blob = json.dumps(data, ensure_ascii=False, separators=(',', ':'))
page = open(tpl).read().replace('/*DATA*/null', blob)
out = os.path.join(repo, 'docs', 'mockups', 'data-editor', 'index.html')
os.makedirs(os.path.dirname(out), exist_ok=True)
open(out, 'w').write(page)
print(out, len(page), 'numbers in sim files:', sum(f['counts']['num'] for f in files if f['sim']))
