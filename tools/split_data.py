"""One-time migration (2026-11-06): split the flat data/<doc>.json files into small files per topic.

Reads data/<doc>.json, cuts each table (or run of records) out of the original *text* so the hand formatting stays,
wraps it in the same nesting ({ "buildings": [ ... ] }), writes data/<doc>/<part>.json and data/manifest.json, checks
that tools/data_loader.py assembles exactly the original documents, then deletes the flat files.

If main changed a flat data/*.json file before this branch was merged, restore the flat files, re-run this script and
re-run the page generators. Usage: python3 tools/split_data.py [--keep]
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.path.dirname(HERE), "data")


# ------------------------------------------------------------------ a JSON parser that keeps text positions

class Node:
    __slots__ = ("kind", "start", "end", "members", "items", "value")

    def __init__(self, kind, start):
        self.kind, self.start, self.end, self.members, self.items, self.value = kind, start, start, [], [], None


def parse(text):
    i = 0

    def ws(i):
        while i < len(text) and text[i] in " \t\r\n":
            i += 1
        return i

    def string(i):
        assert text[i] == '"'
        j = i + 1
        while text[j] != '"':
            j += 2 if text[j] == "\\" else 1
        return json.loads(text[i:j + 1]), j + 1

    def value(i):
        i = ws(i)
        c = text[i]
        if c == "{":
            n = Node("object", i)
            i = ws(i + 1)
            if text[i] == "}":
                n.end = i + 1
                return n, n.end
            while True:
                i = ws(i)
                ks = i
                key, i = string(i)
                i = ws(i)
                assert text[i] == ":"
                v, i = value(i + 1)
                n.members.append((key, ks, v))
                i = ws(i)
                if text[i] == ",":
                    i += 1
                    continue
                assert text[i] == "}", (i, text[i - 40:i + 40])
                n.end = i + 1
                return n, n.end
        if c == "[":
            n = Node("array", i)
            i = ws(i + 1)
            if text[i] == "]":
                n.end = i + 1
                return n, n.end
            while True:
                v, i = value(i)
                n.items.append(v)
                i = ws(i)
                if text[i] == ",":
                    i += 1
                    continue
                assert text[i] == "]"
                n.end = i + 1
                return n, n.end
        if c == '"':
            n = Node("string", i)
            n.value, n.end = string(i)
            return n, n.end
        n = Node("scalar", i)
        j = i
        while j < len(text) and text[j] not in ",}] \t\r\n":
            j += 1
        n.value, n.end = json.loads(text[i:j]), j
        return n, j

    root, _ = value(0)
    return root


def member(obj, key):
    for k, ks, v in obj.members:
        if k == key:
            return ks, v
    raise KeyError(key)


def ident(node):
    for k, _, v in node.members:
        if k in ("id", "class", "name") and v.kind == "string":
            return v.value
    return None


def line_indent(text, pos):
    start = text.rfind("\n", 0, pos) + 1
    j = start
    while text[j] == " ":
        j += 1
    return j - start, j == pos  # indentation of the line, whether pos starts the line


# ------------------------------------------------------------------ cutting parts out of the text

def render(text, obj, sel, pad):
    """Text of one selected member of obj: "key": value as written, or "key": { / [ the chosen sub-entries ] / }."""
    if isinstance(sel, str):
        ks, v = member(obj, sel)
        ind, starts = line_indent(text, ks)
        return " " * (ind if starts else pad) + text[ks:v.end]
    key, subs = sel
    ks, v = member(obj, key)
    ind, starts = line_indent(text, ks)
    ind = ind if starts else pad
    head = " " * ind + text[ks:v.start + 1]  # "key": { or "key": [
    if v.kind == "array":
        chosen = [it for it in v.items if ident(it) in subs]
        assert [ident(it) for it in chosen] == list(subs), (key, subs)
        idx = [v.items.index(it) for it in chosen]
        assert idx == list(range(idx[0], idx[0] + len(idx))), f"{key}: {subs} is not one run"
        body = []
        for it in chosen:
            ii, st = line_indent(text, it.start)
            assert st, f"{key}: {ident(it)} doesn't start a line"
            body.append(" " * ii + text[it.start:it.end])
        return head + "\n" + ",\n".join(body) + "\n" + " " * ind + "]"
    body = [render(text, v, s, ind + 2) for s in subs]
    return head + "\n" + ",\n".join(body) + "\n" + " " * ind + "}"


def ids(text_root, key, first, last):
    """Ids of a run of records in a top-level list, first..last inclusive."""
    _, v = member(text_root, key)
    names = [ident(it) for it in v.items]
    return names[names.index(first):names.index(last) + 1]


def keys(text_root, key, first, last):
    _, v = member(text_root, key)
    names = [k for k, _, _ in v.members]
    return names[names.index(first):names.index(last) + 1]


# ------------------------------------------------------------------ the plan: document -> parts (path, selectors)

def plan(doc, root):
    if doc == "buildings":
        b = lambda first, last=None: ("buildings", ids(root, "buildings", first, last or first))
        fb = lambda f, first, last: (f"faction_buildings/{f}.json", [("faction_buildings", ids(root, "faction_buildings", first, last))])
        powers = ["federation", "klingon", "cardassian", "romulan", "dominion", "ferengi", "borg"]
        return [
            ("notes.json", ["_notes"]),
            ("rules.json", ["layers", "shipyard_tiers"]),
            ("surface/cores.json", [b("colony_hub", "planetary_capital")]),
            ("surface/habitation.json", [b("prefab_habitat", "underground_shelter")]),
            ("surface/mine.json", [b("mine")]),
            ("surface/deep_core_mine.json", [b("deep_core_mine")]),
            ("surface/resource.json", [b("dilithium_mine", "recrystallizer")]),
            ("surface/storage_container.json", [b("storage_container")]),
            ("surface/vehicle_bay.json", [b("vehicle_bay")]),
            ("surface/refinery.json", [b("refinery")]),
            ("surface/foundry.json", [b("foundry")]),
            ("surface/industry.json", [b("component_fab", "industrial_replicator")]),
            ("surface/fusion_reactor.json", [b("fusion_reactor")]),
            ("surface/power.json", [b("geothermal_tap", "pergium_reactor")]),
            ("surface/science_lab.json", [b("science_lab")]),
            ("surface/research.json", [b("observatory", "xenobiology_center")]),
            ("surface/military.json", [b("garrison", "sensor_grid_ground")]),
            ("surface/civic.json", [b("hospital", "embassy")]),
            ("surface/terraforming.json", [b("weather_control", "terraforming_station")]),
            ("orbital.json", [b("orbital_drydock", "genesis_launcher")]),
            ("deep_space.json", [b("outpost", "salvage_yard")]),
            ("fleet_yards.json", [b("fleet_yards")]),
            fb("federation", "starfleet_academy", "federation_council"),
            fb("klingon", "great_hall", "asteroid_fortress"),
            fb("romulan", "tal_shiar_hq", "cloaked_outpost"),
            fb("cardassian", "nor_station", "labor_camp"),
            fb("dominion", "ketracel_facility", "great_link_pool"),
            fb("ferengi", "tower_of_commerce", "mercenary_guild"),
            fb("borg", "unicomplex", "assimilation_complex"),
            ("relics.json", ["relics"]),
            ("faction_lines/notes.json", ["_notes_faction_lines"]),
        ] + [(f"faction_lines/{f}.json", [("faction_lines", [f])]) for f in powers] + [
            ("starting_sets/notes.json", ["_notes_starting_sets", ("starting_sets", ["planet_class"])]),
        ] + [(f"starting_sets/{f}.json", [("starting_sets", [("factions", [f])])]) for f in powers]
    if doc == "surfaces":
        u = [ident(it) for it in member(root, "units")[1].items]
        return [
            ("notes.json", ["_notes"]),
            ("terrain.json", ["terrain"]),
            ("planets.json", ["planets", "hints"]),
            ("environments.json", ["environments", "telemetry"]),
            ("landing.json", ["landing"]),
            ("economy.json", ["build_menu", "power_resource", "hud_resources", "storage_exempt", "salvage_refund", "maintenance"]),
            ("research.json", ["research"]),
            ("orbit.json", ["orbit"]),
            ("lanes.json", ["lanes"]),
            ("unit_names.json", ["unit_names"]),
        ] + [(f"units/{x}.json", [("units", [x])]) for x in u]
    if doc == "climate":
        c = [k for k, _, _ in member(root, "climates")[1].members]
        return [
            ("notes.json", ["_notes"]),
            ("stars.json", ["stars"]),
            ("bodies.json", ["bodies"]),
            ("classes.json", ["classes"]),
            ("surface.json", ["surface"]),
        ] + [(f"climates/{x}.json", [("climates", [x])]) for x in c] + [
            ("weather.json", ["weather"]),
            ("terrain.json", ["terrain"]),
        ]
    if doc == "factions":
        f = lambda first, last=None: [("factions", ids(root, "factions", first, last or first))]
        return [(f"{x}.json", f(x)) for x in ["federation", "klingon", "romulan", "cardassian", "dominion", "ferengi", "borg"]] + [
            ("minor_powers.json", f("breen", "bajoran")),
            ("enterprise_era.json", f("earth_uec", "tellarite")),
            ("later_eras.json", f("federation_lost_ship", "ni_var")),
            ("raiders_and_crises.json", f("maquis", "species_8472")),
        ]
    if doc == "ships":
        c = lambda first, last: [("classes", ids(root, "classes", first, last))]
        return [
            ("notes.json", ["_notes"]),
            ("hulls.json", ["hulls", "modules"]),
            ("classes/federation.json", c("NX", "Constitution III")),
            ("classes/klingon.json", c("D5", "Negh'Var")),
            ("classes/romulan.json", c("Bird-of-Prey (22nd c.)", "Scimitar")),
            ("classes/cardassian.json", c("Hideki", "Keldon")),
            ("classes/dominion.json", c("Jem'Hadar Attack Ship", "Vorta Command Ship")),
            ("classes/ferengi.json", c("Ferengi Shuttle", "D'Kora Marauder")),
            ("classes/borg.json", c("Probe", "Queen's Diamond")),
            ("classes/minor_powers.json", c("Breen Warship", "Nausicaan Raider")),
            ("classes/kelvin.json", c("Kelvin", "Narada")),
            ("classes/far_future.json", c("Crossfield refit (Discovery-A)", "Bird-of-Prey (32nd c.)")),
            ("classes/enterprise_era.json", c("Suurok", "Tellarite Cruiser")),
            ("classes/picard_era.json", c("Free State Warbird", "Ni'Var Cruiser")),
        ]
    if doc == "resources":
        r = lambda first, last: [("resources", ids(root, "resources", first, last))]
        return [
            ("notes.json", ["_notes"]),
            ("raw.json", r("duranium_ore", "tritanium_ore")),
            ("flow.json", r("tritanium", "crew")),
            ("strategic.json", r("verterium_cortenide", "metreon_gas")),
            ("exotic.json", r("protomatter", "red_matter")),
            ("refined.json", r("antimatter", "photon_warheads")),
            ("faction.json", r("ketracel_white", "bloodwine")),
            ("trade_goods.json", r("self_sealing_stem_bolts", "romulan_ale")),
        ]
    if doc == "tech_tree":
        t = lambda first, last: [("techs", ids(root, "techs", first, last))]
        return [
            ("notes.json", ["_notes"]),
            ("rules.json", ["starting_modules", "assimilated_modules", "faction_rules", "trees"]),
            ("techs/propulsion.json", t("warp_5", "spore_drive")),
            ("techs/weapons.json", t("phase_cannon", "red_matter_weapons")),
            ("techs/defense.json", t("polarized_hull", "neutronium_armor")),
            ("techs/sensors_stealth.json", t("long_range_sensors", "subspace_relays")),
            ("techs/engineering.json", t("transporter", "programmable_matter")),
            ("techs/computing.json", t("duotronic", "positronic")),
            ("techs/biology_colonization.json", t("pressure_domes", "subspace_terraforming")),
            ("techs/engineering_exotic.json", t("omega_stabilization", "neutronium_harvesting")),
        ]
    if doc == "hazards":
        h = lambda first, last: [("hazards", ids(root, "hazards", first, last))]
        return [
            ("notes.json", ["_notes"]),
            ("anomalies.json", h("ion_storm", "cosmic_string")),
            ("habitat.json", h("horta_natives", "colonial_insurgency")),
            ("conditions.json", h("sandstorms", "stellar_radiation")),
            ("raiders.json", h("orion_syndicate", "breen_privateers")),
            ("crises.json", h("borg_incursion", "the_burn")),
            ("wildcards.json", h("q_event", "changeling_sabotage")),
        ]
    if doc == "vehicles":
        return [("notes.json", ["_notes"])] + [(f"{k}.json", [k]) for k, _, _ in root.members if k != "_notes"]
    if doc == "galaxy_map":
        return [
            ("notes.json", ["_notes"]),
            ("drives.json", ["core", "warp_scale", "drives"]),
            ("systems.json", ["systems"]),
            ("lanes.json", ["lanes"]),
        ]
    if doc == "eras":
        return [("eras.json", ["eras", "stardate_display"])]
    if doc == "maps":
        return [("notes.json", ["_notes"]), ("mvp_duel.json", ["maps"])]
    if doc == "planet_classes":
        return [("notes.json", ["_notes"]), ("classes.json", ["classes"])]
    raise KeyError(doc)


ORDER = ["factions", "ships", "resources", "planet_classes", "buildings", "maps", "surfaces", "vehicles", "climate",
         "eras", "tech_tree", "hazards", "galaxy_map"]


def main():
    manifest = {}
    written = []
    for doc in ORDER:
        path = os.path.join(DATA, doc + ".json")
        text = open(path, encoding="utf-8").read()
        root = parse(text)
        parts = []
        for rel, sels in plan(doc, root):
            out = "{\n" + ",\n".join(render(text, root, s, 2) for s in sels) + "\n}\n"
            json.loads(out)
            full = os.path.join(DATA, doc, rel)
            os.makedirs(os.path.dirname(full), exist_ok=True)
            open(full, "w", encoding="utf-8", newline="\n").write(out)
            parts.append(f"{doc}/{rel}")
            written.append(full)
        manifest[doc] = parts
    lines = ['{',
             '  "_notes": "Which files make up each data document, in order. The loaders (game/sim/Data/GameData.cs, tools/data_loader.py) merge a document\'s files top-down: a table that appears in several files is joined (lists one after the other, objects key by key); the same key twice is an error. A .json file under data/ that is not listed here is reported. layers/ holds era, faction and scenario overrides, _draft/ unpromoted edits and _backups/ copies (both git-ignored); see CLAUDE.md \\"Data files\\".",',
             '  "version": 1,',
             '  "documents": {']
    for i, doc in enumerate(ORDER):
        parts = manifest[doc]
        lines.append(f'    "{doc}": [')
        lines += [f'      "{p}"' + ("," if j < len(parts) - 1 else "") for j, p in enumerate(parts)]
        lines.append("    ]" + ("," if i < len(ORDER) - 1 else ""))
    lines += ["  }", "}", ""]
    open(os.path.join(DATA, "manifest.json"), "w", encoding="utf-8", newline="\n").write("\n".join(lines))

    sys.path.insert(0, HERE)
    import data_loader
    for doc in ORDER:
        original = json.load(open(os.path.join(DATA, doc + ".json"), encoding="utf-8"))
        assembled = data_loader.load(doc)
        assert sorted(original) == sorted(assembled), doc
        for k in original:
            assert json.dumps(original[k]) == json.dumps(assembled[k]), (doc, k)
    print(f"{len(written)} files written; every document assembles to the original")
    if "--keep" not in sys.argv:
        for doc in ORDER:
            os.remove(os.path.join(DATA, doc + ".json"))


if __name__ == "__main__":
    main()
