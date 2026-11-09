# Star Trek RTS: design package

Early planning material for a Star Trek–themed real-time strategy game (1–4 players, single-player and multiplayer).

## Start here

- `CLAUDE.md`: project memory for Claude sessions (design rules decided so far, how pages are built).
- `GDD.md`: the game design document (pillars, modes, map, economy, colonization, eras, factions, research, ships, threats, multiplayer, open decisions).
- `faction-buildings.html`: the same comparison as an interactive web page (open in any browser). Regenerate with `python3 tools/gen_faction_buildings_page.py`.
- `ship-designs.html`: interactive comparison of every faction's starships (doctrine, hulls, class roster by hull and era with the civilian fleet marked, specials, modules, build costs with the formula, data checks). Regenerate with `python3 tools/gen_ship_designs_page.py`. Every class's cost, build time, crew cost and tech gate come from one formula on hull size and role (`data/ships/rules.json`); after changing it or a class's hull/role, run `python3 tools/ship_costs.py --write` (without `--write` it lists the classes that differ).
- `tech-tree.html`: interactive research tree (faction research models, prerequisite tracing, starting-era grants, forbidden techs, resources, data checks). Regenerate with `python3 tools/gen_tech_tree_page.py`.
- `hazards.html`: interactive hazard catalog (categories, filterable cards, faction exposure, counter techs by faction, data checks). Regenerate with `python3 tools/gen_hazards_page.py`.
- `planet-classes.html`: interactive guide to planet classes and bodies (habitability, resource map, terraforming, faction world preferences, data checks). Regenerate with `python3 tools/gen_planet_classes_page.py`.
- `climate.html`: planetary climate for every body of the system map (orbit, sunlight, temperatures, day length and spin, tilt, light and dark sides, seasons with their weather odds, weather kinds and their gameplay effects, data checks). Data in `data/climate/`. Regenerate with `python3 tools/gen_climate_page.py`.
- `achievements.html`: every achievement of the game prototype by category (medal, rule, hidden or with a progress bar, cosmetic reward), the cosmetics catalogue, trigger kinds and data checks. Data in `data/achievements/`. Regenerate with `python3 tools/gen_achievements_page.py`.
- `vehicles.html`: interactive page for small craft, probes, civilian ships, ground forces and vehicles (roster by faction, ground strength, catalog, data checks). Regenerate with `python3 tools/gen_vehicles_page.py`.
- `resources.html`: interactive resource guide (tiers, production chains, faction demand, catalog, data checks). Regenerate with `python3 tools/gen_resources_page.py`.
- `factions.html`: every faction: the seven great powers compared side by side with dossiers, an era map and the other powers. Regenerate with `python3 tools/gen_factions_page.py`.
- `galaxy-map.html`: map of known space (local view and whole galaxy) with territories by era, warp lanes and wormholes, a route planner with lore and match travel times, the warp scale and capital distances. Data in `data/galaxy_map/`. Regenerate with `python3 tools/gen_galaxy_map_page.py`.
- `docs/faction-buildings.md`: grids comparing every faction's buildings (starting sets, upgrade lines, capabilities, wonders). Regenerate with `python3 tools/gen_faction_buildings_doc.py`.
- `federation-build-tree.html`: interactive Federation build tree. Open it in any browser.
- `art/`: detailed isometric artwork. `art/federation-colony-hub/` has the Colony Hub at all five levels (SVG, open in any browser).
- `tools/gen_colony_hub.py`: the script that draws that artwork (Starfleet style: saucers, capsule modules, glow strips), so the levels stay consistent: `python3 tools/gen_colony_hub.py art/federation-colony-hub`.
- `animation/colony-pod-drop.html`: animated prototype of the colony pod drop with a scrubbable timeline. Open it in any browser. Online: https://claude.ai/artifact/935ziqLQi7xFFeowTrP2sm. Rebuild with `python3 tools/anim_build.py animation/colony-pod-drop.html`.
- `animation/colony-pod-drop-blender.mp4`: the 20-second pod drop rendered from the Blender scene (960×720, 30 fps).
- `blender/build_vehicles.py`: the Federation ground vehicles, built in Blender and exported as `game/models/vehicles/*.glb` for the game (previews in `blender/previews/vehicles/`). Run with Blender (`blender --background --python blender/build_vehicles.py -- --vehicle survey_rover --out game/models/vehicles/survey_rover.glb`) or the `bpy` Python module. The survey rover has three tiers (`--tier 1|2|3`: Mk I Scout, Mk II Surveyor = Kalty, Mk III Explorer; see `blender/rover_variants.py`), a painted `--name` and `--number`, and `--shots front34,rear34,side,game` picks the previews. `blender/rover_finish.py` adds planet wear (`clean|dust|mud|snow`) and damage 0–3 to the paint bake and parts; preview them with `blender/render_finish.py --tier N --finish F --damage D` (renders in `blender/previews/vehicles/finish/`).
- `blender/build_miner.py`: the Mining Vehicle Mk I (the game's mining crawler; previews in `blender/previews/miner/`). `--mode travel|deployed --preview DIR` renders it; the game model is `/root/bpyenv/bin/python blender/build_miner.py --blank --out game/models/vehicles/mining_crawler.glb` (blank decal spots, baked paint, static parts joined).
- `blender/build_dropship.py`: the Federation drop ship (Halyard 07) that flies orbit shuttle deliveries down; `--game` exports the joined game model `game/models/vehicles/dropship.glb` (drawn by `game/scripts/DropShipView.cs`), `--preview` renders the mockup shots in `blender/previews/dropship/`.
- `blender/build_scatter.py`: the landscape scatter (conifers, pine, oak, birch, bushes, desert shrub, dead snag, grass clumps, ferns, boulders, rock clusters, scree, pebbles, layered sandstone, ice chunks, plus cheap far stand-ins), built procedurally with a painted foliage atlas and exported as one `game/models/scatter/scatter.glb` (drawn by `game/scripts/PlanetView.Scatter.cs`). `--preview blender/previews/scatter` renders the lineups and terrain dioramas; `python3 blender/build_scatter.py --sheet blender/previews/scatter` makes the contact sheet.
- `blender/`: the same 20-second pod drop in 3D for Blender 5.2. Open `colony_pod.blend` and press Play, or rebuild it from `build_colony_pod.py` (Scripting tab → Open → Run Script, or `blender --background --python blender/build_colony_pod.py -- --out blender/colony_pod.blend`). `previews/` has test renders; add `--preview DIR --frames 17.6 --zoom 3` to render close-ups.
- `concept-canvas/`: source for the structure concept boards (7 factions × 4 upgrade lines). These files render inside the online canvas, not in a plain browser.

## Game prototype

`game/`: the playable prototype in **Godot 4.7 (.NET / C#)**. Open `game/project.godot` in Godot 4.7 .NET and press Run: you start in orbit over a generated Class M world, pick a landing site, send survey rovers out to find deposits, then drive the mining crawler to a deposit, store and refine what it sends home, and build up the colony. Game rules live in `game/sim/` (deterministic, fixed-point, tested with `cd game && dotnet test`). See `game/README.md`.

**Balance runner** (`tools/BalanceRunner/`, `balance/`): plays the surface economy headless with scripted build orders (`balance/orders/*.json`) about 2,000× faster than real time and writes an HTML report with curves per seed, milestones, stalls and A/B deltas: `dotnet run -c Release --project tools/BalanceRunner -- --set "buildings.json:refinery.cycles_per_min=16"`. See `balance/README.md`; reports for the current data are in `balance/reports/`.

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
- Survey rover variants (marks, names, wear, damage): https://claude.ai/artifact/Rz3Hkh4cXu497PnWtUkwbP
- Test panel mockup: https://claude.ai/artifact/SCdjcY4gKUYcE1QRqMqp14
- Ground kit mockups (all factions; Federation miner, drop ship, outpost levels 1-10): https://claude.ai/artifact/StNcu8UNbSTbBLFvydqmew

These links only open for people the owner has shared them with (Share menu on each page).

## Data files (`data/`)

Each data document is a folder of small files, one per table or record family (one per record for the big ones: the main surface buildings, the surface units, climate templates); `data/manifest.json` lists the files of each document in order and the loaders (`game/sim/Data/GameData.cs`, `tools/data_loader.py`) join them back into one document. Override layers for eras, factions and scenarios are in `data/layers/`; edits from the game's Test panel wait in `data/_draft/` until promoted (see CLAUDE.md, "Data files"). Release builds read one packed file instead (`game/gamedata.pack`, built by `tools/DataPacker`).

| Document (folder) | Contents |
|---|---|
| `resources/` | Raw materials, refined components, faction resources, trade goods, and the parts economy's parts, spare kits and construction parts (with their storage volume). Files: notes, one file per tier, parts |
| `factions/` | Playable, minor, pirate and crisis factions with signature mechanics. Files: one file per great power, the rest by group |
| `eras/` | Eight starting eras with their playable and AI-only factions, events and stardate formats (the game loads and checks it against `factions/`, read-only for now). Files: one file |
| `planet_classes/` | Planet classes and other celestial bodies with yields and hazards. Files: notes + classes |
| `tech_tree/` | Research tree across seven branches (the game loads and checks it, read-only for now; the prototype's surface research is in `surfaces/`). Files: notes, rules, `techs/<branch>.json` |
| `ships/` | Hull sizes (canon crew complements), module slots and ship classes per faction, warships and civilian/support classes, each with a cost, build time, crew cost and tech gate. Files: notes, hulls + modules, rules (roles and the costing formula), `classes/<faction or era>.json` |
| `vehicles/` | Small craft, probes, civilian/support ships, ground forces and vehicles. Files: notes + one file per table |
| `buildings/` | All structures, faction-only buildings, relics, `faction_lines` (level-1-to-max upgrade chains for all seven factions) and `starting_sets` (each faction's Class M opening buildings). Files: notes, rules, `surface/<building or family>.json`, orbital, deep space, `faction_buildings/<faction>.json`, relics, `faction_lines/<faction>.json`, `starting_sets/<faction>.json` |
| `hazards/` | Anomalies, habitat dangers, raiders, crisis entities and wildcards. Files: notes + one file per category |
| `galaxy_map/` | Star systems, territories, warp lanes and wormholes, warp scale and drive speeds. Files: notes, drives, systems, lanes |
| `maps/` | Skirmish maps for the game prototype: one star system each (bodies, deposits, player starts, starting stock). Files: notes + `mvp_duel.json` (the Kalandra Expanse, on the galaxy map as `kalandra`: placeholder position near the Cardassian border), `generator.json` (how a system game's star system is generated from the seed: star, planet count, orbit zones with class weights (Class M rare), moons, sizes, names, the landable fallback, generated climates) and `system.json` (flight times, long-range scans, body satellites, neutral traffic) |
| `surfaces/` | Planet surfaces for the game prototype: terrain types, how each planet class's hex map is generated, deposits, orbital hints, landing rules, rover and mining crawler stats, storage and salvage rules, planet environments per class (temperature, humidity, wind, pressure, radiation, atmosphere and trace gases) and the outpost telemetry's hazard limits, gravity curves and habitability tiers (the domes kit on hostile worlds), and the parts economy (Machine Shop recipes, the Colony Hub workshop, which spare kits repairs take, jury-rigging, keep-in-stock defaults). Planets with a surface: Class M (Tarsis) and Class L (Veyra, a Domes world). Files: notes, terrain, planets, environments + telemetry, worlds (gravity + habitability), landing, economy (build menu, power, storage, salvage, maintenance), fabrication (parts economy), research, orbit, lanes, unit names, `units/<id>.json` |
| `climate/` | Planetary climate for the game prototype: star types, every map body's orbit, spin, tilt, year, albedo and greenhouse, climate templates (seasons with weather odds, latitude bands), weather kinds with their gameplay effects (rover speed, survey radius, rover damage, grounded drop ships, solar) and terrain modifiers. Files: notes, stars, bodies, classes, surface, `climates/<template>.json`, weather, terrain |
| `achievements/` | Achievements for the game prototype (30 in eight categories, bronze / silver / gold; the sim's tracker unlocks them, each earns one cosmetic). Files: notes, rules (categories, tiers, cosmetics), one file per category (colony, exploration, surface, vehicles, hostile, comms, challenge, research) |
| `civilizations/` | Natives and ruins for the game prototype's generated star systems (civilizations phase 1): the odds per system and the body classes they can be on, development levels L1–L6 (population, towns, lights, signals), awareness bands, study times, observation research, the hidden observation post and its discovery risk, Starfleet Standing (Federation, Prime Directive eras), generated species and nation names, ruin kinds; phase 2 (`contact.json`): the first-contact event (steps, choices, culture-shock odds), relations and membership, trade income, unique local techs, ally ships, aggressive natives, L4 → L5. Files: notes, rules, levels, names, ruins, contact |

Each entry is tagged `canon` (on screen), `beta` (licensed books/games) or `original` (invented for this game). All costs and stats are placeholders.

Star Trek is owned by Paramount. This is a fan design project; a commercial release would need a license or an original setting.
