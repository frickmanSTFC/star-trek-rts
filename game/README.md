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
| `sim/Ai/` | `ChaseAi`: placeholder opponent that only issues commands |
| `tests/` | xUnit tests: math, RNG, data checks, determinism |
| `project.godot`, `scenes/`, `scripts/` | The Godot shell: `SimDriver` (ticking, input, ships), `SystemView` (star, planets, moons, asteroid fields, orbits, colony rings), `Hud` (resource bar, info panel) |
| `FinalFrontier.sln` | One solution for everything (Godot opens it too) |

## Run it

1. Install **Godot 4.7.x .NET edition** and the **.NET 8 SDK**.
2. In Godot: Import → pick `game/project.godot` → Run (F5).

You play the Federation at Tarsis (west); the Cardassians hold Morrow (east). The top bar shows your
stock and income per minute; click any planet, moon or asteroid field to see its class, deposits and colony.

Controls: left-click a ship or body to select (Shift adds ships), **A** selects all ships, right-click moves,
WASD/arrows pan, mouse wheel zooms, **H** returns to your home world.

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

Placing buildings (step 2), mining deposits, construction ships and outposts (step 3), shipyards (step 4), combat (step 5), orbits, fog of war, the sector (lane) map, the network layer, exported builds
(the data folder is read from `../data` next to the project, which works in the editor only).
