# Final Frontier: game prototype

Godot 4.7 (.NET edition, C#) over a deterministic simulation library. The game reads the same
`../data/*.json` files the design pages use, so balancing happens in one place.

## Layout

| Path | What it is |
|---|---|
| `sim/` | **FinalFrontier.Sim**: all game rules. Pure C#, no Godot types, fixed-point math. |
| `sim/Math/` | `Fix64` (Q32.32 fixed point), `FixVec2`, `DeterministicRng` (xoshiro256**), `StateHasher` |
| `sim/Data/` | `GameData`: loads factions, hulls, ship classes, resources, planet classes, buildings and maps from `../data/`, and checks them (`Validate()`) |
| `sim/Core/` | `Simulation` (fixed 15 Hz tick, command queue, economy, state hash), `Player`, `ResourceBag`, `Body`, `Colony`, `Ship`, `Commands`, `MatchSetup` |
| `sim/Surface/` | Planet surface: `HexGrid`, `SurfaceMap` (seeded terrain and deposit generation, orbital hints, landing sites), `Pathfinder` (A*), `PlanetSurface` (landing, rovers, surveying, placing and constructing buildings, upkeep, storage, mining, refining, mining crawlers and cargo pods, salvage) |
| `sim/Ai/` | `ChaseAi`: placeholder opponent that only issues commands |
| `tests/` | xUnit tests: math, RNG, data checks, determinism |
| `scenes/Planet.tscn`, `scripts/PlanetDriver.cs`, `PlanetView.cs` | **Main scene**: the planet surface (one continuous terrain mesh over the hex grid, animated water, trees, deposit crystals, scan hints, landing, pod with touchdown dust, buildings, rovers) |
| `shaders/` | `terrain`, `water`, `foliage` (all read the fog-of-war texture: one texel per tile) and `crystal` (glowing deposits) |
| `scripts/OrbitView.cs`, `OrbitPainter.cs`, `shaders/planet`, `shaders/atmosphere` | Orbit view: the painted globe with satellite-photo ground and a ray-marched (Rayleigh + Mie) atmosphere |
| `textures/planet/` | Tileable ground photos for the orbit globe, cut from NASA's Blue Marble Next Generation (public domain; credit: NASA Earth Observatory). Read at runtime, not imported (`.gdignore`) |
| `scenes/System.tscn`, `scripts/SimDriver.cs`, `SystemView.cs` | Star-system view, kept for later (star, planets, moons, asteroid fields, orbits, fleets) |
| `scripts/BuildingModels.cs` | Primitive-built models: hub (grows with level), mine, reactor, habitat, colony pod, survey rover |
| `scripts/PlanetHud.cs`, `Minimap.cs`, `Icons.cs` | Planet HUD: icon resource bar, event feed, tooltip, bottom panel (minimap, selected base or object, command card); icons are SVG paths drawn at runtime |
| `scripts/Hud.cs` | The star-system view's simpler HUD |
| `FinalFrontier.sln` | One solution for everything (Godot opens it too) |

## Run it

1. Install **Godot 4.7.x .NET edition** and the **.NET 8 SDK**.
2. In Godot: Import → pick `game/project.godot` → Run (F5).

You start aboard your ship in orbit over Tarsis, a generated Class M world (change `Seed` on the root node for another
planet). Drag (or WASD) to turn the globe, wheel to zoom, **Tab** to change scan (terrain, resources, lifeforms,
biologics, landing), and pick a card on the right to deploy: land the colony first (click a green site), later
outpost drops, shuttles, transporter beams and survey satellites. **O** switches between orbit and the surface.

1. **Scan:** the floating labels are the orbital scan's deposit hints per region. Hover any tile for its terrain.
2. **Land:** green tiles are valid landing sites (plains or desert, open land on every side). Left-click to drop the pod.
3. **Survey:** after touchdown the Colony Hub starts a small trickle of metal, and two survey rovers and a mining
   crawler roll out (all selected). Right-click a tile to drive there; deposits appear as glowing crystals as tiles
   are surveyed.
4. **Mine:** select the crawler and right-click a surveyed deposit: it drives there, unfurls and starts digging.
   Each full hopper flies home as a cargo pod to the nearest storage building. **F** unfurls it where it stands or
   packs it up; a move order packs it up first. Storage (top bar) is small: the hub's vault fills fast, so build a
   **Storage Container** and an **Ore Refinery** early (the refinery turns 2 ore into 1 metal and frees space).
5. **Build:** pick a building from the bar at the bottom (or keys **1–7**). A see-through ghost follows the mouse:
   green = OK, red = not (the info panel says why). **R** turns it 60° (Shift+R back), left-click places it
   (Shift+click to keep placing), right-click or Esc stops. The blue area is your build range. Buildings rise as a
   hologram while under construction; one builds at a time and the rest queue. Click your building for its status;
   **X** cancels an unfinished one for a full refund, or salvages a finished one for half its cost. Put mines on
   deposits, and keep power and deuterium positive. A used-up deposit can be reopened once by a Deep-Core Mine
   (needs the Deep-Core Mining tech).
   The crawler can break down while mining (a minor fault, a major breakdown, or an explosion that loses its load).
   It then folds up and drives slower: press **F** (or right-click the Colony Hub) to send it to the repair pad
   beside the hub, where two crew members walk out and weld it back to 100% for a little duranium.
   Build a **Vehicle Bay** (key 8) for more vehicles: select it and the command card shows its orders
   (survey rover, mining crawler; keys 1–2). Orders are paid up front and queue up to 5; **Backspace** cancels
   the last one. Each vehicle rolls out of the bay's door when it's done.
   Clicking a card on the build card opens a bigger **info card** above it: description, cost, stats, upgrades
   and requirements. A building card also starts placing it. A research or vehicle card only opens its info:
   start or order from the card's button, with **Enter**, or by picking the same card (or number key) again. **Esc** closes it.
7. **Research and upkeep:** build a **Research Lab** (key 9), select it and pick a tech (keys 1–7). Buildings wear
   while they work: below 50% they slow down and may break. Select one and press **M** to send the base's crew,
   or **U** to upgrade it (Mk II, Mk III: faster, wears less; Mk III and the Automated Maintenance tech add
   drones that keep it running). The building panel shows what it's doing, its stats and where bonuses come from.

6. **Run the base:** the bottom panel shows the selected base: level, upgrade (button or **U**), power used / supply,
   its buildings and build queue. Keep power supply above use (Fusion Reactors) or everything that uses power slows.
   Upgrade the Colony Hub to Colonial Administration for two build queues and a bigger radius.

The HUD: resources across the top (stock and rate per minute; power used / supply and storage used / space at the right), events top-left,
a tooltip for whatever is under the mouse, the survey map bottom-left (click or drag to move the camera), the
selected base or object in the middle, and the build card bottom-right. Fog: bright = in sight now, dim = explored,
near-black = unexplored (set `FogDarkness` on the root node).

Controls: left-click selects a vehicle, building, base or tile (Shift adds vehicles), **A** selects all vehicles, right-click
drives (a crawler right-clicked onto a deposit mines it), **F** unfurl / pack up the crawler (or send a broken one for repair), **1–8** build (or order vehicles when a Vehicle Bay is selected), **Backspace** cancel the bay's last order, **R** rotate,
**U** upgrade the selected building (or the base), **M** maintain the selected building, **F1** all controls, **X** cancel a build or upgrade / salvage a finished building, **Tab** next base, **Esc** back to the base,
WASD/arrows or middle-mouse drag pan, **Q / E** turn the view 90°, mouse wheel zooms, **H** returns to the colony.

Map size is `width` / `height` in `data/surfaces.json` (Class M: 128×88 tiles); the camera adapts to it.

The older star-system view is `scenes/System.tscn` (open it and press F6).

## Build and test without the editor

```bash
cd game
dotnet build FinalFrontier.sln
dotnet test
```

## Rules for sim code (lockstep determinism, GDD §11)

- **No `float`/`double`** in `sim/`. Use `Fix64`/`FixVec2`. Convert floats only at the input boundary
  (`Fix64.FromDouble` when turning a click into a command) and back only for rendering (`ToFloat`).
- **No `System.Random`, clocks, threads or engine calls.** Use `Simulation.Rng`.
- **No iterating `Dictionary`/`HashSet`** for anything that affects state; keep ordered lists.
- **State changes only through commands**, scheduled for a future tick. The Godot side never edits ships directly.
- Every new piece of state goes into `StateHash()`; every new system gets a determinism test.
- Numbers from JSON are parsed from raw text (`Fix64.Parse`), never through `double`.

## Not done yet

On the surface: outposts (more bases), shields and integrity, more buildings (research lab, garrison, refineries), population, the Cardassian rival landing. Later: the star system above, shipyards, combat, orbits, the sector (lane) map, the network layer, exported builds
(the data folder is read from `../data` next to the project, which works in the editor only).
