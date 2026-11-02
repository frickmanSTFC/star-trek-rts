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
| `sim/Surface/` | Planet surface: `HexGrid`, `SurfaceMap` (seeded terrain and deposit generation, orbital hints, landing sites), `Pathfinder` (A*), `PlanetSurface` (landing, rovers, surveying, placing and constructing buildings, upkeep, mining) |
| `sim/Ai/` | `ChaseAi`: placeholder opponent that only issues commands |
| `tests/` | xUnit tests: math, RNG, data checks, determinism |
| `scenes/Planet.tscn`, `scripts/PlanetDriver.cs`, `PlanetView.cs` | **Main scene**: the planet surface (hex terrain, scan hints, landing, pod, hub, rovers, deposits) |
| `scenes/System.tscn`, `scripts/SimDriver.cs`, `SystemView.cs` | Star-system view, kept for later (star, planets, moons, asteroid fields, orbits, fleets) |
| `scripts/BuildingModels.cs` | Placeholder building models (hub, mine, reactor, habitat) |
| `scripts/Hud.cs` | Shared HUD: resource bar, prompt banner, build bar, info and help panels |
| `FinalFrontier.sln` | One solution for everything (Godot opens it too) |

## Run it

1. Install **Godot 4.7.x .NET edition** and the **.NET 8 SDK**.
2. In Godot: Import → pick `game/project.godot` → Run (F5).

You start in orbit over Tarsis, a generated Class M world (change `Seed` on the root node for another planet).

1. **Scan:** the floating labels are the orbital scan's deposit hints per region. Hover any tile for its terrain.
2. **Land:** green tiles are valid landing sites (plains or desert, open land on every side). Left-click to drop the pod.
3. **Survey:** after touchdown the Colony Hub starts producing and two survey rovers roll out (already selected).
   Right-click a tile to drive there; deposits appear as glowing crystals as tiles are surveyed.
4. **Build:** pick a building from the bar at the bottom (or keys **1–3**). A see-through ghost follows the mouse:
   green = OK, red = not (the info panel says why). **R** turns it 60° (Shift+R back), left-click places it
   (Shift+click to keep placing), right-click or Esc stops. The blue area is your build range. Buildings rise as a
   hologram while under construction; one builds at a time and the rest queue. Click your building for its status;
   **X** cancels an unfinished one for a full refund. Put mines on deposits, and keep energy and deuterium positive.

Controls: left-click selects a rover, building or tile (Shift adds rovers), **A** selects all rovers, right-click drives,
WASD/arrows pan, **Q / E** turn the view 90°, mouse wheel zooms, **H** returns to the colony.

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

On the surface: more buildings (research lab, garrison, refineries), upgrades, population, the Cardassian rival landing. Later: the star system above, shipyards, combat, orbits, the sector (lane) map, the network layer, exported builds
(the data folder is read from `../data` next to the project, which works in the editor only).
