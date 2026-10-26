# Star Trek RTS: design package

Early planning material for a Star Trek–themed real-time strategy game (1–4 players, single-player and multiplayer).

## Start here

- `GDD.md`: the game design document (pillars, modes, map, economy, colonization, eras, factions, research, ships, threats, multiplayer, open decisions).
- `faction-buildings.html`: the same comparison as an interactive web page (open in any browser). Regenerate with `python3 tools/gen_faction_buildings_page.py`.
- `ship-designs.html`: interactive comparison of every faction's starships (doctrine, hulls, class roster by hull and era, specials, modules). Regenerate with `python3 tools/gen_ship_designs_page.py`.
- `tech-tree.html`: interactive research tree (faction research models, prerequisite tracing, starting-era grants, forbidden techs, resources, data checks). Regenerate with `python3 tools/gen_tech_tree_page.py`.
- `hazards.html`: interactive hazard catalog (categories, filterable cards, faction exposure, counter techs by faction, data checks). Regenerate with `python3 tools/gen_hazards_page.py`.
- `planet-classes.html`: interactive guide to planet classes and bodies (habitability, resource map, terraforming, faction world preferences, data checks). Regenerate with `python3 tools/gen_planet_classes_page.py`.
- `vehicles.html`: interactive page for small craft, probes, civilian ships, ground forces and vehicles (roster by faction, ground strength, catalog, data checks). Regenerate with `python3 tools/gen_vehicles_page.py`.
- `resources.html`: interactive resource guide (tiers, production chains, faction demand, catalog, data checks). Regenerate with `python3 tools/gen_resources_page.py`.
- `factions.html`: every faction: the seven great powers compared side by side with dossiers, an era map and the other powers. Regenerate with `python3 tools/gen_factions_page.py`.
- `galaxy-map.html`: map of known space (local view and whole galaxy) with territories by era, warp lanes and wormholes, a route planner with lore and match travel times, the warp scale and capital distances. Data in `data/galaxy_map.json`. Regenerate with `python3 tools/gen_galaxy_map_page.py`.
- `docs/faction-buildings.md`: grids comparing every faction's buildings (starting sets, upgrade lines, capabilities, wonders). Regenerate with `python3 tools/gen_faction_buildings_doc.py`.
- `federation-build-tree.html`: interactive Federation build tree. Open it in any browser.
- `art/`: detailed isometric artwork. `art/federation-colony-hub/` has the Colony Hub at all five levels (SVG, open in any browser).
- `tools/gen_colony_hub.py`: the script that draws that artwork (Starfleet style: saucers, capsule modules, glow strips), so the levels stay consistent: `python3 tools/gen_colony_hub.py art/federation-colony-hub`.
- `animation/colony-pod-drop.html`: animated prototype of the colony pod drop with a scrubbable timeline. Open it in any browser. Online: https://claude.ai/artifact/935ziqLQi7xFFeowTrP2sm. Rebuild with `python3 tools/anim_build.py animation/colony-pod-drop.html`.
- `animation/colony-pod-drop-blender.mp4`: the 20-second pod drop rendered from the Blender scene (960×720, 30 fps).
- `blender/`: the same 20-second pod drop in 3D for Blender 5.2. Open `colony_pod.blend` and press Play, or rebuild it from `build_colony_pod.py` (Scripting tab → Open → Run Script, or `blender --background --python blender/build_colony_pod.py -- --out blender/colony_pod.blend`). `previews/` has test renders; add `--preview DIR --frames 17.6 --zoom 3` to render close-ups.
- `concept-canvas/`: source for the structure concept boards (7 factions × 4 upgrade lines). These files render inside the online canvas, not in a plain browser.

## Website (GitHub Pages)

`docs/` is a ready-to-serve website: a landing page plus standalone copies of every design page, the Colony Hub art and the rendered video. Once Pages is on (Settings → Pages → Deploy from a branch → `main` / `docs`), it lives at https://frickmanstfc.github.io/star-trek-rts/.

After changing data, regenerate the pages (`python3 tools/gen_*_page.py`), then rebuild the site with `python3 tools/build_site.py` and push.

## Online versions

- Faction structure concepts (canvas): https://claude.ai/artifact/Q8fDXqjgVAQuyQWAyuEame
- Federation build tree: https://claude.ai/artifact/CtWfg7e9HB3neyC4Y1hmAa
- Faction buildings comparison: https://claude.ai/artifact/22KRN3X1S5rvqEdencW1XZ
- Starship designs comparison: https://claude.ai/artifact/3zju4soKLXq2t9akpNedXW
- Research tech tree: https://claude.ai/artifact/8kirbSLsBppJDPgT9EzoWA
- Galactic hazards: https://claude.ai/artifact/Vai46ETcjaNJG7ojmmKBH8
- Planet classes: https://claude.ai/artifact/A7rTNNkxcBGj8LKJvhSnnP
- Craft and ground forces: https://claude.ai/artifact/53hsd46T61zcfcxjFLd5nu
- Resources and materials: https://claude.ai/artifact/RvherhjUTzqwb1rWZcKZeS
- Factions of the galaxy: https://claude.ai/artifact/38uos1h4ucJyap5PYNiKDX
- Galaxy map: https://claude.ai/artifact/MaLME4auJSy9Q437HHN2mr

These links only open for people the owner has shared them with (Share menu on each page).

## Data files (`data/`)

| File | Contents |
|---|---|
| `resources.json` | Raw materials, refined components, faction resources, trade goods |
| `factions.json` | Playable, minor, pirate and crisis factions with signature mechanics |
| `eras.json` | Eight starting eras with available factions, events and stardate formats |
| `planet_classes.json` | Planet classes and other celestial bodies with yields and hazards |
| `tech_tree.json` | Research tree across seven branches |
| `ships.json` | Hull sizes, module slots and ship classes per faction |
| `vehicles.json` | Small craft, probes, civilian/support ships, ground forces and vehicles |
| `buildings.json` | All structures, faction-only buildings, relics, `faction_lines` (level-1-to-max upgrade chains for all seven factions) and `starting_sets` (each faction's Class M opening buildings) |
| `hazards.json` | Anomalies, habitat dangers, raiders, crisis entities and wildcards |

Each entry is tagged `canon` (on screen), `beta` (licensed books/games) or `original` (invented for this game). All costs and stats are placeholders.

Star Trek is owned by Paramount. This is a fan design project; a commercial release would need a license or an original setting.
