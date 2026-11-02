# Final Frontier: game prototype

Godot 4.7 (.NET edition, C#) over a deterministic simulation library. The game reads the same
`../data/*.json` files the design pages use, so balancing happens in one place.

## Layout

| Path | What it is |
|---|---|
| `sim/` | **FinalFrontier.Sim**: all game rules. Pure C#, no Godot types, fixed-point math. |
| `sim/Math/` | `Fix64` (Q32.32 fixed point), `FixVec2`, `DeterministicRng` (xoshiro256**), `StateHasher` |
| `sim/Data/` | `GameData`: loads factions, hulls and ship classes from `../data/` |
| `sim/Core/` | `Simulation` (fixed 15 Hz tick, command queue, state hash), `Ship`, `Commands`, `MatchSetup` |
| `sim/Ai/` | `ChaseAi`: placeholder opponent that only issues commands |
| `tests/` | xUnit tests: math, RNG, data checks, determinism |
| `project.godot`, `scenes/`, `scripts/` | The Godot shell: rendering, input, camera, HUD |
| `FinalFrontier.sln` | One solution for everything (Godot opens it too) |

## Run it

1. Install **Godot 4.7.x .NET edition** and the **.NET 8 SDK**.
2. In Godot: Import → pick `game/project.godot` → Run (F5).

Controls: left-click a ship to select (Shift adds), **A** selects all, right-click moves,
WASD/arrows pan, mouse wheel zooms. The HUD shows the tick and state hash.

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

Combat, economy, building, fog of war, the sector (lane) map, the network layer, exported builds
(the data folder is read from `../data` next to the project, which works in the editor only).
