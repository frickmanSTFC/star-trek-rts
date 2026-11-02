# Project memory

Fan-made Star Trek RTS design package. `README.md` lists every page, data file and artifact link; `GDD.md` is the design document. Keep both in step with the data.

## Design rules decided so far

- Seven great powers (see `tools/faction_meta.py` FACTIONS); 28 factions in `data/factions.json`.
- **Borg never research.** `tech_tree.json` → `faction_rules.borg`: `research: false`; they assimilate techs and combine them, costing 20 nanoprobes × tier. `tools/tech_access.py` `reachable()` honours this.
- Terraforming runs K → L → M.
- Dominion War era is 2373–2379; `eras.json` and `factions.json` must agree.
- Galaxy map (`data/galaxy_map.json`): Sol-centred light-years, +x toward Beta, +y toward the core (core at 0, 26000). Routes use the stable Bajoran wormhole but skip the unstable Barzan wormhole (opt-in checkbox in the planner); transwarp is Borg only. Match drive speeds are placeholders: Warp 5 = 30, Warp 8 = 60, Warp 9.9 = 110, slipstream = 300 ly per minute of play.
- All numbers are placeholders for balancing.

## Game prototype (`game/`)

- Engine: **Godot 4.7 .NET (C#)**. `game/sim/` is the deterministic simulation (pure C#, `Fix64` fixed point, 15 Hz ticks, no Godot types); `game/scripts/` is the Godot side. Rules for sim code are in `game/README.md`; follow them.
- Build/test: `cd game && dotnet build FinalFrontier.sln && dotnet test`. Needs .NET 8 SDK (`apt-get install -y dotnet-sdk-8.0` in a fresh container). The Godot binary can't be downloaded here (downloads.godotengine.org and github.com releases blocked), so the editor run is the owner's; `dotnet build` still type-checks the Godot scripts against GodotSharp.
- The sim loads `../data/*.json` directly; `GameData.Validate()` (test `RealDataPassesChecks`) must stay empty.
- **MVP starts on a planet surface** (GDD §3b, decided by the owner): hex grid on 3D terrain, scan from orbit → land the colony pod → survey with rovers; solo first, then a Cardassian rival on the same planet; the star system comes later. Main scene `scenes/Planet.tscn` (`PlanetDriver`); surface rules in `game/sim/Surface/`, data in `data/surfaces.json` (terrain, generation per planet class, deposits, hints, landing, rover stats).
- Surface generation is seeded and deterministic; elevation and moisture are *ranked*, so thresholds in surfaces.json are shares of the map. Test `PlanetsArePlayable` checks a few seeds have enough landing sites and the basic deposits. Class M is 128×88 tiles (owner asked for much bigger ground); the camera's overview distance, zoom limit, haze and shadow distance are derived from the map size in `PlanetDriver`, so changing width/height needs no code changes. Hexes are `PlanetView.HexWidth` = 25 world units wide (owner: tiles much bigger than buildings, which keep their own size); terrain relief, scatter (trees, boulders) and camera distances are tuned to that.
- Star-system view (`scenes/System.tscn`, `SimDriver`) is kept for later: `data/maps.json` `mvp_duel` (Kalandra Expanse). Keep it point-symmetric (test `MvpMapIsPointSymmetric`); map deposits must be yields of the body's class.
- Building stats live on entries in `buildings.json` `buildings` (all optional, placeholder numbers): `build_seconds`, `output_per_min`, `upkeep_per_min`, `requires_deposit` + `extract_per_min` (mines), `build_radius` + `build_queues` (colony cores). `surfaces.json` `build_menu` lists what can be placed on a surface.
- **Power is a per-base budget on the surface** (owner decided): `power_supply` / `power_use` on buildings; energy (`surfaces.json` `power_resource`) is never stockpiled there and must not appear in a surface building's output/upkeep or the landing stock (validated). Over-use slows every power user at the base to supply ÷ use. Other buildings in buildings.json still list energy as a *cost*; fix those when they join the build menu.
- **Levels:** cores carry `level`; `upgrades_to` is the upgrade line (validated from the landing hub up: each step exists, has build_seconds, level +1). Structures belong to a base (`BaseId` = nearest core in range when placed).
- Planet HUD is `scripts/PlanetHud.cs` (layout from the mockup artifact claude.ai/artifact/2evrsmTGmagiQJhMsJuddS); `scripts/Hud.cs` is only the star-system view's. `surfaces.json` `hud_resources` sets the top bar. Fog is render-only (`PlanetView.FogAt`): visible = within sight of own buildings (3) / rovers (survey radius + 1); it is drawn through a one-texel-per-tile texture that `game/shaders/terrain|water|foliage.gdshader` sample by world position (soft edges). Terrain is one mesh: tile corners average the heights of the tiles sharing them; mountains peak at their centres. Shaders can't be compiled in this container: re-read them carefully and ask the owner for Godot's Output-panel errors.
- Owner's controls decisions: camera turns in 90° steps (Q / E); buildings rotate in 60° steps (R) to match hex sides; owner was told 90° for buildings is a small change if wanted.
- **Priority: building and economy come before combat.** It's an RTS: colonize, build, mine and produce first; combat is needed but secondary. Order: economy + construction → shipyard production → combat.

## How the pages are built

- Each page: `tools/gen_<x>_page.py` reads `data/*.json` + `tools/faction_meta.py`, replaces `/*DATA*/` in `tools/<x>_page.html`, writes `<x>.html` at the repo root.
- Shared dark "console frame" style; every page ends with a data-checks list, which should come back empty. When a page flags gaps, fix the data rather than the check.
- After any data change: rerun the affected `gen_*_page.py`, then `python3 tools/build_site.py` (builds `docs/` for GitHub Pages; new pages go in its PAGES list and in README).
- Data JSON is hand-formatted: edit with targeted unique string replacements, not full rewrites. One-object-per-line files (tech_tree, ships, vehicles, hazards) keep that layout.
- Test pages with Playwright at 1400 px and 400 px: no horizontal overflow, no console errors.
- Each root page is also published as a claude.ai Artifact (links in README); republish after regenerating. Artifacts are private until shared from the page's Share menu.

## Repo

- github.com/frickmanSTFC/star-trek-rts, public, branch `main`. Site: https://frickmanstfc.github.io/star-trek-rts/ once Pages is enabled (Settings → Pages → `main` / `docs`; still to do by the owner).
- Commit as `frickMan` with the owner's email; the session proxy blocks GitHub collaborator and Pages API calls, so those are done by the owner in the web UI.
- Blender scene: `blender/build_colony_pod.py` builds `colony_pod.blend` (see README for flags).
