# Project memory

Fan-made Star Trek RTS design package plus a playable Godot prototype. `README.md` lists every page, data file and artifact link; `GDD.md` is the design document. Keep both in step with the data.

## Design rules decided so far

- Seven great powers (see `tools/faction_meta.py` FACTIONS); 28 factions in `data/factions.json`.
- **Borg never research.** `tech_tree.json` → `faction_rules.borg`: `research: false`; they assimilate techs and combine them, costing 20 nanoprobes × tier. `tools/tech_access.py` `reachable()` honours this.
- Terraforming runs K → L → M.
- Dominion War era is 2373–2379; `eras.json` and `factions.json` must agree.
- Galaxy map (`data/galaxy_map.json`): Sol-centred light-years, +x toward Beta, +y toward the core (core at 0, 26000). Routes use the stable Bajoran wormhole but skip the unstable Barzan wormhole (opt-in checkbox in the planner); transwarp is Borg only. Match drive speeds are placeholders: Warp 5 = 30, Warp 8 = 60, Warp 9.9 = 110, slipstream = 300 ly per minute of play.
- All numbers are placeholders for balancing.

## Game prototype (`game/`)

### Engine, build, workflow

- **Godot 4.7 .NET (C#)**, chosen for a two-person team (owner + Claude). `game/sim/` is the deterministic simulation (pure C#, `Fix64` fixed point, 15 Hz ticks, no Godot types, state changes only through commands); `game/scripts/` is the Godot side. Rules for sim code are in `game/README.md`; follow them.
- Build/test: `cd game && dotnet build FinalFrontier.sln && dotnet test` (96 tests at last count). Needs .NET 8 SDK (`apt-get install -y dotnet-sdk-8.0` in a fresh container). The Godot binary can't be downloaded here (downloads.godotengine.org and github.com releases blocked), so only the owner can run the game; `dotnet build` still type-checks the Godot scripts against GodotSharp. Shaders (`game/shaders/*.gdshader`) can't be compiled here at all: re-read them carefully and ask for Godot's Output-panel errors.
- The sim loads `../data/*.json` directly; `GameData.Validate()` (test `RealDataPassesChecks`) must stay empty.
- **Owner's loop:** they run it in Godot on Windows (`C:\dev\star-trek-rts\star-trek-rts`, PowerShell) and report "it worked" or what broke. After each step: build, test, commit, push to `main`, then give them the update commands. Godot rewrites `game/project.godot` when the project opens, so their update is: close Godot → `git checkout -- game/project.godot` → `git pull origin main`.
- Things can't be seen here, so after writing Godot code, re-read it for runtime traps (nodes not yet in the tree, sibling-name collisions, API names) before pushing.

### Owner's decisions

- **MVP starts on one planet surface** (GDD §3b): hex grid on 3D terrain; scan from orbit → land the colony pod → survey with rovers → build. Solo first, then a Cardassian rival on the same planet; the star system (`scenes/System.tscn`, `SimDriver`, `data/maps.json` `mvp_duel`, kept point-symmetric, test `MvpMapIsPointSymmetric`) comes later.
- **Priority: building and economy before combat.** Order: economy + construction → shipyard production → combat.
- **Power is a per-base budget on the surface**, not a stockpile.
- **Mining (2026-11-03):** ore must be refined; storage is limited and fills fast (hub vault 1500); early builds are Storage Container + Ore Refinery; the hub makes only a small metal trickle (owner: "small trickle"). Exactly **one Mining Crawler** lands with the colony; more come from the Vehicle Bay (owner picked it next, 2026-11-03). Keep the name "Colony Hub" (owner sometimes calls it the outpost).
- **Big world:** Class M is 128×88 tiles; hexes are `PlanetView.HexWidth` = 25 world units, much bigger than buildings (which keep their size). Mountains are impassable and drawn as towering massifs.
- **Controls:** camera turns in 90° steps (Q / E); buildings rotate in 60° steps (R) to match hex sides (owner was told 90° for buildings is a small change if wanted).
- **HUD** follows the mockup artifact claude.ai/artifact/2evrsmTGmagiQJhMsJuddS (icon resource bar, event feed, minimap, base panel, command card; darker three-level fog).

### How it works now

- **Surface** (`game/sim/Surface/`, `data/surfaces.json`): seeded, deterministic generation; elevation and moisture are *ranked*, so thresholds are shares of the map. Test `PlanetsArePlayable` checks a few seeds have landing sites and basic deposits. Landing rules, rover stats, `build_menu`, `hud_resources` and `power_resource` live in surfaces.json. Map size is just `width`/`height` there: the camera overview, zoom limit, haze and shadow distance are derived from it in `PlanetDriver`.
- **Buildings** (`buildings.json` `buildings`, all optional placeholder numbers): `build_seconds`, `output_per_min`, `upkeep_per_min`, `power_supply` / `power_use`, `requires_deposit` + `extract_per_min` (mines; they currently turn deposits straight into finished metal), `build_radius` + `build_queues` (colony cores), `level` + `upgrades_to` (Colony Hub 1 → Colonial Administration 2 → Planetary Capital 3; validated from the landing hub up). Planetary Capital costs Parasteel and Isolinear Chips, which nothing produces yet.
- **Bases:** a core plus the buildings in its radius (`BaseId` = nearest core in range when placed). Each base has its own build queues and power budget; over-use slows every power user there to supply ÷ use. Energy must not appear in a surface building's output/upkeep or the landing stock (validated). Other buildings in buildings.json still list energy as a *cost*; fix those when they join the build menu.
- **Storage:** `PlanetSurface.StorageCapacity` = sum of `storage` on finished buildings; `Stored` counts every resource except `storage_exempt` (crew, latinum) and power; `FreeStorage` also subtracts cargo pods in flight. Building output and mines go through `Store` (only what fits); converters spend input first and add output directly; refunds/salvage ignore the cap.
- **Ore and converters:** hills/plains deposits are `duranium_ore` / `tritanium_ore` (tier `raw`, amounts doubled). `recipes` + `cycles_per_min` (refinery 2 ore → 1 metal, foundry 2 dur + 1 tri → parasteel) share cycles between recipes that have input; no input → `NoInput`. Idle statuses (NoUpkeep, Depleted, StorageFull, NoInput) draw no power.
- **Mining crawler** (`surfaces.json` units: `extract_per_min`, `hopper`, `deploy_seconds`, `pod_speed`): `UnitMode` Mobile → Deploying → Deployed → Packing; `MineCommand` (drive + `DeployOnArrival`), `PackUpCommand`, moves pack up first. Full hopper → `CargoPod` to `NearestDepot` (nearest finished building with storage), flight = distance / pod_speed (min 1 s), space reserved at launch. Waits on `StorageFull` / `NoDestination`; packs up when `Depleted`. Can't deploy on a structure or another crawler's tile; buildings can't go under a deployed crawler. View: `BuildingModels.Crawler` parts (Leg0–3, Boom, Drill/String/Bit, Fill, Dust) animated in `PlanetView.AnimateCrawler`; pods fly an arc in `UpdatePods`. Used-up deposits draw as small dark stubs (markers redraw when the count of surveyed used-up deposits changes); the sim refuses to unfurl on them (`DeployProblem`).
- **Breakdowns** (owner 2026-11-03: "slight possibility", repair at the outpost with a small area and a couple of people): crawler `breakdown` (chance_per_min 0.05 while digging, rolled on `sim.Rng`; weighted severities minor/major/explosion with damage ranges; explosion `loses_load`; `slowest_drive`) and `repair` (per_second, crew_seconds, cost_per_point). `SurfaceUnit.Integrity/Broken/Breakdown/BrokeAtTick/RepairTicks/RepairWaiting`; broken → packs up, `MineProblem` refuses, `PlanetSurface.SpeedPerTick` slows it. Repair = parked (Mobile, no path) on own finished core tile (`IsRepairing`); `RepairCommand` drives to `NearestRepairPad`. Tests patch a copy of the data (`BreakdownTests.Patched`). View: `BuildingModels.RepairPad` beside every core (`RepairPadOffset`), crawlers on a core tile park there; `Crew` figures walk from the door (`UpdateCrew`); `Blast` plays once per `BrokeAtTick`; Smoke/Sparks/Fire while broken.
- **Vehicle Bay** (`vehicle_bay`: `produces` + `production_queue`; units' `cost` + `build_seconds` in surfaces.json): `BuildUnitCommand` pays up front and appends to `SurfaceStructure.Production`; `CancelUnitCommand` refunds the last order. Progress advances by the base's power efficiency in `Economy`; empty queue → `NoInput` (no power). `RollOut` puts the unit on the first free passable neighbour (sets `BuiltAtTick`/`BuiltAtTile`). Salvage refunds unbuilt orders in full. UI: the command card holds build menu + `unit:<id>` buttons and `ShowCommands` shows only the listed ones; a selected bay swaps in its orders (keys 1–n, Backspace cancel). View: `VehicleBay` model ("Door" rolls up, "WorkGlow", "Spin"), `AnimateBay`, `RollOutFrom` drives new units out of the door.
- **Depletion:** used-up mines go `Depleted`; `SalvageCommand` (X on a finished non-core building) refunds `salvage_refund` (50%) and frees the tile; `deep_core_mine` (`requires_depleted`, `reopen_share` 0.5, `requires_level` 2 standing in for the tech) reopens a deposit once (`SurfaceMap.DepositStart` / `DepositReopened`).
- **Rendering:** fog is render-only (`PlanetView.FogAt`: visible = within 3 tiles of own buildings / survey radius + 1 of rovers), drawn through a one-texel-per-tile texture the terrain, water and foliage shaders sample by world position. Terrain is one mesh (corners average neighbouring heights; hexes subdivided with bumps; mountains have a high base so ranges merge, peaks ~40–78, snow above ~34). Scatter (trees, boulders) is render-only randomness. Models are primitives in `scripts/BuildingModels.cs`. Planet HUD is `scripts/PlanetHud.cs` (+ `Minimap.cs`, `Icons.cs`); `scripts/Hud.cs` is only the star-system view's.

### Proposed next (not yet confirmed by the owner)

- Mining (storage, ore, refinery, crawler, pods, salvage, deep-core) and crawler breakdowns + repair pad: owner tested in Godot 2026-11-03, "it worked".
- Open: the Alloy Foundry eats duranium/tritanium as soon as it's built (no on/off or priority yet); offer a pause toggle. Deeper-scan research needs a research system. Crawler pods ignore terrain and fog.
- Vehicle Bay built 2026-11-03; waiting for the owner's Godot test (door, roll-out, order buttons).
- Other candidates offered: outposts + construction rovers (base tabs and Tab are ready for more bases), the Cardassian rival, shields and integrity, Research Lab / Security Garrison, faster rovers for the big map, an Alloy Foundry pause toggle.

## How the pages are built

- Each page: `tools/gen_<x>_page.py` reads `data/*.json` + `tools/faction_meta.py`, replaces `/*DATA*/` in `tools/<x>_page.html`, writes `<x>.html` at the repo root.
- Shared dark "console frame" style; every page ends with a data-checks list, which should come back empty. When a page flags gaps, fix the data rather than the check.
- After any data change: rerun the affected `gen_*_page.py`, then `python3 tools/build_site.py` (builds `docs/` for GitHub Pages; new pages go in its PAGES list and in README).
- Data JSON is hand-formatted: edit with targeted unique string replacements, not full rewrites. One-object-per-line files (tech_tree, ships, vehicles, hazards) keep that layout.
- Test pages with Playwright at 1400 px and 400 px: no horizontal overflow, no console errors.
- Each root page is also published as a claude.ai Artifact (links in README); republish after regenerating. Artifacts are private until shared from the page's Share menu.

## Repo

- github.com/frickmanSTFC/star-trek-rts, public, branch `main`. Site: https://frickmanstfc.github.io/star-trek-rts/ once Pages is enabled (Settings → Pages → `main` / `docs`; still to do by the owner).
- Commits: author `frickMan` with the owner's email; committer Claude `noreply@anthropic.com` (`GIT_COMMITTER_NAME=Claude GIT_COMMITTER_EMAIL=noreply@anthropic.com git -c user.name=frickMan -c user.email=<owner email> commit …`) so commits are signed and show as Verified. The owner has OK'd pushing each finished step to `main`.
- The session proxy blocks GitHub collaborator and Pages API calls, so those are done by the owner in the web UI.
- Blender scene: `blender/build_colony_pod.py` builds `colony_pod.blend` (see README for flags).
