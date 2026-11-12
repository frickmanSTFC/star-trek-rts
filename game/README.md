# Final Frontier: game prototype

Godot 4.7 (.NET edition, C#) over a deterministic simulation library. The game reads the same
`../data/` files the design pages use (small files per topic, listed in `../data/manifest.json`), so balancing
happens in one place. Release exports read one packed copy instead (`gamedata.pack`, see "Data" below).

## Layout

| Path | What it is |
|---|---|
| `sim/` | **FinalFrontier.Sim**: all game rules. Pure C#, no Godot types, fixed-point math. |
| `sim/Math/` | `Fix64` (Q32.32 fixed point), `FixVec2`, `DeterministicRng` (xoshiro256**), `StateHasher` |
| `sim/Data/` | `GameData`: loads factions, hulls, ship classes, resources, planet classes, buildings and maps from `../data/` (or the pack), and checks them (`Validate()`); `DataManifest` (joins a document's files), `DataDocument` (value positions in a file's text, for edits that keep the layout), `DataLayers` (era / faction / scenario patches), `DataStore` (draft, promote, backups, last-good fallback), `DataPack` (the release pack), `DataChecks` (every check Promote and the packer run), `DataTuning` (Test panel DATA tab) |
| `scripts/GameDataLoader.cs` | Debug builds: `../data/` + draft (refused data falls back to the last good copy); release exports: `res://gamedata.pack` |
| `sim/Core/` | `Simulation` (fixed 15 Hz tick, command queue, economy, state hash), `Player`, `ResourceBag`, `Body`, `Colony`, `Ship`, `Commands`, `MatchSetup` |
| `sim/Surface/` | Planet surface: `HexGrid`, `SurfaceMap` (seeded terrain and deposit generation: deposit fields of 3–7 hexes in `Fields.cs`, orbital hints, landing sites), `Pathfinder` (A*), `Lanes` (vehicles drive inside the hexes: round buildings, to access points and parking spots, keeping their distance), `PlanetSurface` (landing, rovers, surveying, placing and constructing buildings, upkeep, storage, mining, refining, mining crawlers and cargo pods, salvage) |
| `sim/Space/` | The star system of a system game: `SystemGenerator` (a system from the seed: star, planets, belts, moons, zones, each body's climate entry; Class M rare, at least one landable world) and `StarSystemState` (`Simulation.System`: ships, timed flight, the three fog levels Detected / Charted / Live, body satellites, long-range scans, neutral traffic) and `Civilizations` (natives and ruins rolled with the system, study, hidden observation posts, Starfleet Standing; `data/civilizations/`) and `Contact` (first contact, relations and membership, trade (halved by unrest after a culture shock; L6 worlds trade more), native trade freighters on each route, unique techs, ally ships, aggressive natives, L4 → L5). Planet-only games have none |
| `sim/Ai/` | `ChaseAi`: placeholder opponent that only issues commands |
| `tests/` | xUnit tests: math, RNG, data checks, determinism |
| `scenes/Planet.tscn`, `scripts/PlanetDriver.cs`, `PlanetView.cs` | **Main scene**: the planet surface (one continuous terrain mesh over the hex grid, animated water, trees, deposit crystals, scan hints, landing, buildings, rovers) |
| `scripts/DropShipView.cs`, `LandingView.cs`, `RocketPlume.cs` | Drop ships (rocket landers: suicide burn, flames, ground glow, smoke, fine dust) for orbit deliveries, and the first landing: three drop ships set the Level 1 outpost down in three modules, unload the rovers and the crawler, and the modules crawl together, lock and unfurl until the uplink comes online |
| `scripts/DropShipSpec.cs`, `DropShipHulls.cs`, `models/vehicles/dropship_prewarp_a/b/c.glb` | Each drop ship model's numbers (ramp, tip, bay, pads, footprint, leg fold, climb, lights); in a Dawn of Warp match every player flies one of three pre-warp ships (Lander, Hopper, Freighter from `blender/build_dropship_prewarp.py --game`), picked from the map seed, each ship named from a pre-warp list; other eras fly Halyard 07 |
| `shaders/` | `terrain`, `water`, `foliage`, `rock` (all read the fog-of-war texture: one texel per tile) and `crystal` (glowing deposits) |
| `scripts/ConstructionView.cs`, `BuildingModels.Exocomp.cs`, `models/drones/exocomp.glb`, `shaders/build_*` | Construction: exocomp drones fly out, circle the site and beam each piece of the building into place (`build_print`, `build_beam`, `build_flare`, `build_shimmer`) |
| `sim/Surface/Founding.cs`, `scripts/PlanetDriver.Founding.cs`, `PlanetView.Founding.cs`, `BuildingModels.Constructor.cs`, `models/vehicles/construction_rover.glb` | Founding a second outpost on the ground: the construction rover drives to a site, unfolds its crane and sets down the folded hub, and its exocomps build the new Colony Hub (`blender/build_constructor.py`) |
| `scripts/Scatter.cs`, `PlanetView.Scatter.cs`, `models/scatter/scatter.glb` | Landscape scatter: trees, bushes, grass, ferns, rocks, sandstone and ice per terrain (built by `blender/build_scatter.py`), chunked MultiMeshes with near/far LOD, wind and clearings under buildings |
| `scripts/OrbitView.cs`, `OrbitPainter.cs`, `shaders/planet`, `atmosphere`, `clouds` (+ `air`/`clouds` includes) | Orbit view (the helm viewscreen): the painted globe with satellite-photo ground, animated oceans, two lit cloud layers with ground shadows and a ray-marched (Rayleigh + Mie) atmosphere; displays Planet (optional lat/long grid), Orbit (tracks of the ship, satellites and moon), Atmosphere (full clouds, storm cells) and System |
| `scripts/OrbitView.Satellites.cs`, `SatelliteRig.cs`, `BuildingModels.Satellite.cs`, `SatelliteCard.cs`, `SatellitePhotos.cs`, `PlanetDriver.Satellites.cs` | Survey satellites in orbit (sim side `sim/Surface/Satellites.cs`): the era's model on its track (deploy, retask, swath, hover/select, FOLLOW), the helm's SATELLITES window (list, card, findings log, RETASK) and its pictures of the real surface (a small SubViewport on the feed layer). |
| `scripts/SurfaceFeed.cs`, `SurfaceTelemetry.cs`, `PlanetDriver.Uplink.cs`, `sim/Surface/Telemetry.cs` | Once the outpost's uplink is online: the helm's live video feed from the surface (its own camera and SubViewport; the surface moves to render layer 20 while it shows) and the environmental telemetry (temperature, humidity, wind, pressure, gases, radiation, hazards) computed from `surfaces.json` environments |
| `scripts/PlanetDriver.Sky.cs`, `SkyField.cs`, `SkyModel.cs`, `WeatherView.cs`, `PlanetView.Sky.cs`, `OrbitView.Sky.cs`, `shaders/sky.gdshaderinc`, `sky_dome` | Day/night, seasons and weather drawn from the sim's `Climate`: per-tile daylight and lying snow / wet ground / puddles in two textures the terrain, water, foliage and rock shaders read; the sun, moon, ambient, haze and sky dome (stars at night) for the view's centre; rain, snow, dust and lightning around the camera; lamps brighter at night; the orbit globe's sun over the same subsolar column; the top bar's time/season/weather readout |
| `scripts/VehicleLights.cs`, `PlanetView.Headlights.cs` | Vehicle headlights that light the world: spot lights at each live vehicle's lamps (drop ships: landing lights and a belly flood), glowing lenses, a faint beam cone in murky air and a ground glow far off; on by the dark and the weather at each vehicle (L: auto / on / off), off when disabled, flickering when damaged; at most 24 lit and 4 with shadows, nearest the camera first |
| `sim/Surface/Climate.cs`, `PlanetSurface.Climate.cs`, `sim/Data/ClimateDefs.cs`, `GameData.Climate.cs` | Planetary climate from `data/climate/`: the sun (day/night by spin, seasons by tilt, sunlight by orbit), temperature and humidity per tile, drifting weather cells with seasonal odds, and their mild gameplay effects (rover speed, survey radius, rover damage, grounded drop ships). Pure functions of data, map and tick |
| `sim/Data/EraDefs.cs`, `GameData.Eras.cs` | Eras (`data/eras/`) and the strategic tech tree (`data/tech_tree/`), loaded read-only and checked (era factions agree with `data/factions/`, the match era, tech ids and prerequisites, the Borg `research: false` rule); nothing plays them yet |
| `scripts/SystemPlot.cs`, `OrbitLines.cs` | The helm's top-down star-system plot from the map's bodies; thin line meshes for tracks, rings and the grid |
| `textures/planet/` | Tileable ground photos for the orbit globe, cut from NASA's Blue Marble Next Generation (public domain; credit: NASA Earth Observatory). Read at runtime, not imported (`.gdignore`) |
| `scenes/System.tscn`, `scripts/SimDriver.cs`, `SystemView.cs` | Star-system view, kept for later (star, planets, moons, asteroid fields, orbits, fleets) |
| `scripts/BuildingModels.cs`, `BuildingModels.Outpost.cs` | Primitive-built models (hub levels 2–3, mine, reactor, habitat, …) and the imported glb models: the Level 1 outpost (`models/buildings/outpost_l1.glb`), rovers, crawler, drop ship |
| `scripts/PlanetHud.cs`, `Minimap.cs`, `Icons.cs` | Planet HUD: icon resource bar, event feed, tooltip, bottom panel (minimap, selected base or object, command card); icons are SVG paths drawn at runtime |
| `scripts/BuildDrawer.cs`, `BuildingCard.cs`, `PlanetHud.BuildStrip.cs`, `PlanetDriver.BuildMenu.cs`, `ModelThumbnails.cs`, `HudKit.cs` | The build menu: BUILD [B] and the recent builds on the command card, the slide-up drawer with every building as a picture tile, the large centre card (live turning model and everything from the data) for buildings and Vehicle Bay orders, the placing strip; pictures rendered in the game from the real models in their own little studio (SubViewport with its own world) |
| `scripts/Hud.cs` | The star-system view's simpler HUD |
| `scripts/Sfx.cs`, `SfxHost.cs`, `PlanetDriver.Sounds.cs`, `PlanetView.Sounds.cs`, `WeatherAudio.cs`, `sounds/` | Game sounds (docs/sound-contract.md): UI clicks/hovers on every button, landing sounds per drop ship and convoy, selection hums, the nearest moving vehicles' electric drive + ground + flat-tyre loops and wiper strokes, the weather at the site (wind, gusts, rain, snowfall, dust storms, thunder), the exocomps' hums, beams, launches, docking, chirps and completion chime (`ConstructionView.Sounds.cs`); buses UI / Effects / Ambience with their volumes in the settings |
| `sim/Surface/DomeTransit.cs`, `scripts/PlanetView.DomeTransit.cs`, `shaders/vehicle_field.gdshader` | Dome transits: on dome worlds a vehicle stops 2.5 s at the edge of a field while its own field forms and meshes with the dome's (ripples, arcs, an iris opening); Shift+F10 fakes one on the selected vehicle in debug builds |
| `sim/Surface/Drones.cs`, `Comms.cs`, `scripts/PlanetView.Drones.cs`, `PlanetView.DronePad.cs`, `PlanetView.Comms.cs`, `OrbitView.Relays.cs`, `PlanetDriver.Drones.cs`, `PlanetDriver.Relays.cs`, `BuildingModels.Drone.cs` | Scout drones (made at the Machine Shop, launched from a Drone Pad, battery, fly over anything, survey), comms coverage (colony cores + relay satellites from the helm's RELAY pill) and its overlay (G) |
| `sim/Missions/`, `scripts/MissionTracker.cs`, `MissionComms.cs`, `MissionLog.cs`, `PlanetDriver.Missions.cs`, `PlanetView.Missions.cs` | Campaign missions from `data/missions/`: objective tracker, incoming transmissions (Y), mission log (N), sites and markers on the map, test panel MISSIONS tab |
| `scripts/GraphicsQuality.cs`, `PlanetDriver.Graphics.cs`, `PlanetView.TerrainDetail.cs`, `PlanetView.Decals.cs`, `PlanetView.Effects.cs`, `shaders/heat_haze.gdshader`, `textures/terrain/` | Visual pass (Settings › GRAPHICS: Classic / Enhanced / Custom; F7 compares live with frame time): SDFGI + SSIL, volumetric fog, AgX + per-world grading, softer shadows + SSAO, FSR 2 AA, detail terrain textures, tracks / scorch / worn ground / paths decals, heat shimmer, embers, dust motes |
| `scripts/DebugOptions.cs`, `DebugOverlay.cs`, `PlanetDriver.Debug.cs` | Debug overlay (F8, or the backquote key; Settings › DEBUG): performance (FPS, frame-time graph, GPU/CPU/sim ms), rendering, memory, simulation, cursor and selection cards; hex grid, vehicle paths and footprint draws; wireframe/overdraw/unshaded/lighting views (Shift+F8); FPS cap |
| `FinalFrontier.sln` | One solution for everything (Godot opens it too) |

## Run it

1. Install **Godot 4.7.x .NET edition** and the **.NET 8 SDK**.
2. In Godot: Import → pick `game/project.godot` → Run (F5).

You start aboard your ship in orbit over Tarsis, a generated Class M world (change `Seed` on the root node for another
planet). Drag (or WASD) to turn the globe, wheel to zoom, **Tab** to change scan (terrain, resources, lifeforms,
biologics, landing), and pick a card on the right to deploy: land the colony first (click a green site), later
outpost drops, shuttles, transporter beams and survey satellites. **O** switches between orbit and the surface.

1. **Scan:** the floating labels are the orbital scan's deposit hints per region. Hover any tile for its terrain.
2. **Land:** green tiles are valid landing sites (plains or desert, open land on every side). Left-click to land: three drop ships bring the outpost down in three modules, which crawl together and unfold into the Colony Hub (its uplink to the ship comes online when it is done).
3. **Survey:** at touchdown two survey rovers and a mining crawler roll out; the Colony Hub needs about 20 s to assemble
   (no power, storage, building or trickle until then), then it starts a small trickle of metal. The rovers and the mining
   crawler roll out (all selected). Right-click a tile to drive there; deposits appear as glowing crystals as tiles
   are surveyed.
4. **Mine:** select the crawler and right-click a surveyed deposit: it drives there, unfurls and starts digging.
   Each full hopper flies home as a cargo pod to the nearest storage building. **F** unfurls it where it stands or
   packs it up; a move order packs it up first. Storage (top bar) is small: the hub's vault fills fast, so build a
   **Storage Container** and an **Ore Refinery** early (the refinery turns 2 ore into 1 metal and frees space).
5. **Build:** press **B** (or the **BUILD** button on the command card) for the build drawer: every building you can build,
   with a picture rendered from its model, its cost (red when you're short) and its number key. A campaign starts with
   only the Storage Container, Ore Refinery, Mining Complex and Fusion Reactor (plus the domes kit on a Domes world):
   the First Light missions unlock the rest as you go (`Surface/Unlocks.cs`; DEBUG and the tests have everything). Pick one (tile, number key,
   or one of the three recent builds on the command card) and its **large card** opens over the map: a turning model,
   cost, build time, power, output, recipes, levels, research that changes it, where it can go, what feeds it and what it
   feeds, wear and tips. **PLACE** (or **Enter**, or the same pick again) arms placement and the card shrinks to a strip
   at the bottom right (**I** brings the card back); **Shift**+pick skips the card. A see-through ghost follows the mouse:
   green = OK, red = not (the info panel says why). **R** turns it 60° (Shift+R back), left-click places it
   (Shift+click to keep placing), right-click or Esc stops. The blue area is your build range. Exocomps fly out from
   the base and build each building piece by piece with their beams (a queued building is a faint ghost); one
   builds at a time and the rest queue. Click your building for its status;
   **X** cancels an unfinished one for a full refund, or salvages a finished one for half its cost. Put mines on
   deposits, and keep power and deuterium positive. A used-up deposit can be reopened once by a Deep-Core Mine
   (needs the Deep-Core Mining tech).
   The crawler can break down while mining (a minor fault, a major breakdown, or an explosion that loses its load).
   It then folds up and drives slower: press **F** (or right-click the Colony Hub) to send it to the repair pad
   beside the hub, where two crew members walk out and weld it back to 100% with spare kits (Mechanical Spares; the Colony Hub's small workshop makes them, a Machine Shop faster; short of kits, the repair waits or you jury-rig it to 60%).
   Rovers wear while they drive (hills, ice and forest most), can hit rock strikes, sinkholes or sharp ice, corrode on
   toxic ground and get attacked by predators. Below half integrity they drive slower, badly damaged their sensors see
   less, and at 0 they are disabled. A flat tyre slows a rover (two flats: a crawl): press **T** to have the crew change
   it with the spare, or **R** to send it to the repair pad, which fixes everything for Rover Spares.
   Day and night sweep across the map (a Tarsis day is 12 minutes, its year 8 days with four seasons), and weather
   drifts over it with the seasons: rain, snow, fog and storms slow vehicles, shorten their surveys and wear rovers
   faster, and storms keep drop ships and outpost drops (and the first landing) from coming down under them.
   Build a **Vehicle Bay** for more vehicles: select it and the command card shows its orders, each with a picture
   (survey rover Mk I Scout, mining crawler, Mk II Surveyor, Mk III Explorer, construction rover; keys 1–5). Orders are paid up front and queue up to 5; **Backspace** cancels
   the last one. Each vehicle rolls out of the bay's door when it's done.
   **A second base on the ground:** select a **construction rover** and press **FOUND OUTPOST** (the card, **F** or **1**),
   then click a site: surveyed, buildable, safe ground at least 8 tiles from your other bases (a ghost of the outpost
   shows green or red with the reason; the red ring marks where it's too close). It drives there, pays the founding
   cost, unfolds its crane and sets the folded hub down; its exocomps then build the new Colony Hub (90 s), which
   becomes a base of its own in the base tabs (**Tab**). **X** on the unfinished hub refunds it and gives the rover back;
   a move order while it unfolds folds it up again (refunded).
   A vehicle order opens the same large card for the vehicle (speed, survey radius, damage and tyres, mining or founding);
   a research tech opens a smaller card. Order or start research from the card's button, with **Enter**, or by picking
   the same card (or number key) again. **Esc** closes it. Research-gated buildings (the Deep-Core Mine) stay out of the
   drawer until their tech is done, and the number keys follow what the drawer shows.
7. **Research and upkeep:** build a **Research Lab**, select it and pick a tech (keys 1–7). Buildings wear
   while they work: below 50% they slow down and may break. Select one and press **M** to send the base's crew,
   or **U** to upgrade it (Mk II, Mk III: faster, wears less; Mk III and the Automated Maintenance tech add
   drones that keep it running). The building panel shows what it's doing, its stats and where bonuses come from.

6. **Run the base:** the bottom panel shows the selected base: level, upgrade (button or **U**), power used / supply,
   its buildings and build queue. Keep power supply above use (Fusion Reactors) or everything that uses power slows.
   Upgrade the Colony Hub to Colonial Administration for two build queues and a bigger radius.

Day and night sweep across the map (the planet turns; a local day is about 12 minutes), seasons change the light and
the weather: rain wets the ground and leaves puddles, snow settles in cold seasons and melts in warm ones, fog, dust
storms and thunderstorms come and go, and the wind bends the trees. At night lamps and windows light up. The top bar
shows the day, season, local time and weather at the centre of the view (hover it for wind, visibility and the ground).

The HUD: resources across the top (stock and rate per minute; power used / supply and storage used / space at the right), events top-left,
a tooltip for whatever is under the mouse, the survey map bottom-left (click or drag to move the camera), the
selected base or object in the middle, and the build card bottom-right. Fog: bright = in sight now, dim = explored,
near-black = unexplored (set `FogDarkness` on the root node).

Controls: left-click selects a vehicle, building, base or tile (Shift adds vehicles), **A** selects all vehicles, right-click
drives (a crawler right-clicked onto a deposit mines it), **F** unfurl / pack up the crawler (or send a broken one for repair; a construction rover: found an outpost), **R** send damaged vehicles to the repair pad, **T** change a rover's flat tyre, **B** build drawer, **1–8** a building's card (again or **Enter** to place, **Shift** places at once; or order vehicles when a Vehicle Bay is selected), **I** the card again while placing, **Backspace** cancel the bay's last order, **R** rotate,
**U** upgrade the selected building (or the base), **M** maintain the selected building, **P** pause / resume the selected building (a paused building uses no input and no power: e.g. stop the Alloy Foundry eating your duranium), **F1** all controls, **X** cancel a build or upgrade / salvage a finished building, **Tab** next base, **Esc** back to the base,
WASD/arrows or middle-mouse drag pan, **Q / E** turn the view 90°, mouse wheel zooms, **H** returns to the colony.

**Test panel (dev builds only, F9 on the surface):** change anything for testing — the selected vehicle's integrity,
breakdown, flat tyres and spares, dirt, position (teleport), clone or remove it; the selected building's condition, wear, level, work speed,
construction / upgrade / order progress ("finish now"), whole-base repairs; every resource, limits (no storage cap,
unlimited power, free and instant building), game speed ¼–8×, pause (**Space**), step a tick, skip a minute; research;
fog off, survey everything, deposit amounts, place any vehicle or building for any player; any number in the data
files (search, apply to this game, reload, save to the draft, promote the draft into the data files); and (SKY tab, look only) the time of day, season,
weather and wind. Every change is a sim command (in replays and the state
hash); the top bar shows TEST MODE once one is made. **Ctrl+Z** undoes the last edit.

Map size is `width` / `height` in `data/surfaces/planets.json` (Class M: 128×88 tiles); the camera adapts to it.

The older star-system view is `scenes/System.tscn` (open it and press F6).

## Build and test without the editor

```bash
cd game
dotnet build FinalFrontier.sln
dotnet test
```

## Data

- Edit the files under `../data/` by hand (keep their layout), or from the Test panel's DATA tab: **Save to draft**
  writes `../data/_draft/draft.json` (git-ignored), which debug runs load on top of the data; **Promote draft** runs every
  data check (`DataChecks`: `Validate()`, playable planets on three seeds, map symmetry, twin climates) and only then
  writes the edits into the data files, backing up the old ones in `../data/_backups/`. The same from a terminal:
  `dotnet run --project tools/DataPacker -- check [--draft]` and `-- promote` (from the repo root).
- Data that fails `Validate()` is refused at start: base + draft → base alone → the last good copy
  (`../data/_backups/last_good.pack`); the reasons show in the event feed and the Output panel.
- **Release export:** run `dotnet run --project tools/DataPacker -- --out game/gamedata.pack` first (it refuses data that
  fails a check), and in Project → Export → your preset → Resources set "Filters to export non-resource files/folders"
  to `gamedata.pack`. The JSON folders live outside `game/`, so they are never exported; release builds read only the pack.
The test project also builds and tests the headless balance runner (`../tools/BalanceRunner/`, see `../balance/README.md`),
which plays the sim with scripted build orders ~2,000× faster than real time and reports what a data change does.

## Rules for sim code (lockstep determinism, GDD §11)

- **No `float`/`double`** in `sim/`. Use `Fix64`/`FixVec2`. Convert floats only at the input boundary
  (`Fix64.FromDouble` when turning a click into a command) and back only for rendering (`ToFloat`).
- **No `System.Random`, clocks, threads or engine calls.** Use `Simulation.Rng`.
- **No iterating `Dictionary`/`HashSet`** for anything that affects state; keep ordered lists.
- **State changes only through commands**, scheduled for a future tick. The Godot side never edits ships directly.
- Every new piece of state goes into `StateHash()` (next-id counters and the queued commands included; tests
  `StateHashTests` find a forgotten `_next…Id`); every new system gets a determinism test.
- **Bad commands are refused, never thrown** (a peer can send anything): `CommandChecks.Malformed` turns away missing
  lists/strings, undefined enums and absurd sizes before any handler runs; each handler checks players, ids, tiles and
  values and, if it can't run, changes nothing and calls `sim.Refuse(command, reason)` (`RefusedCommands`,
  `LastRefusal`; not in the hash). A new command type needs a case in its handler, a shape check if it has lists or
  strings, and a tag + its fields in `CommandHash` (test `CommandHashCoversEveryCommandType`). `CommandFuzzTests`
  throws thousands of random commands at a two-player game; keep it passing.
- Numbers from JSON are parsed from raw text (`Fix64.Parse`), never through `double`.

## Not done yet

On the surface: shields and integrity, more buildings (research lab, garrison, refineries), population, the Cardassian rival landing. The star system's sim is in (`sim/Space/`: generated systems, flight, system fog, satellites, scans, neutral traffic; `MatchSetup.CreateSystemGame`); its SYSTEM view is being built. Later: shipyards, combat, orbits, the sector (lane) map, the network layer, exported builds
(the data now ships as `gamedata.pack`; the orbit globe's photo textures are still read from the project folder).
