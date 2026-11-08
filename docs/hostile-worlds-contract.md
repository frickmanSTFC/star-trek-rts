# Hostile worlds contract (2026-11-08)

Shared names for the gravity + Veyra slice, so the sim, view and model work can run in parallel.
Owner's answers: Veyra needs **domes** (tighten the habitability thresholds so class L lands in Domes),
gravity effects are **smooth curves** (class M unchanged), Veyra's polar caps are **water ice**,
drop ships cost **5 deuterium a trip at 1 g** (× the lift-cost curve).

## Sim API (`game/sim/Surface/Habitability.cs`, stubs until the sim branch lands)

- `enum HabitabilityTier { Open, Masks, Suits, Domes, Tech, Impossible }`
- `PlanetSurface.Tier`, `PlanetSurface.GravityG`
- `PlanetSurface.Covered(player, tile)`: under a dome or containment field of that player
- `PlanetSurface.DomeRadiusOf(structure)`: tiles a finished structure covers (cores 1 on a Domes world, `pressure_dome` 2)
- `PlanetSurface.LifeSupportOf(baseId)`: 0..1

## Data ids

- Buildings: `pressure_dome` (glass dome over its own hex + a containment field 2 tiles out),
  `life_support_plant` (air + heat for a base; crewed buildings draw from it).
- World: Veyra = climate body `veyra`, planet class `class_l` (a new `planets` entry in surfaces.json).
- Starting a game there: `PlanetDriver.World` export (`"tarsis"` default, `"veyra"`).

## Models (`game/models/buildings/<id>.glb`, loaded in `scripts/BuildingModels.Hostile.cs`)

- `pressure_dome`: parts `Build0..N` (exocomp pieces), `DomeGlass` (material "Glass", see-through),
  `DomeFrame`, `Airlock0..1`, `FieldEmitter0..2` (emissive "Field"), `Beacon`. Footprint ≈ the core's (radius 8).
  The 2-tile containment field itself is drawn by the view (shader shell), not in the glb.
- `life_support_plant`: parts `Build0..N`, `Fan0..1` (spin about local Y), `Tank0..2`, `Vent0..1` (steam points),
  `Status` (emissive "Status": green / amber / red), `Beacon`. Footprint radius ≈ 7.
- Primitive stand-ins with the same part names when the glb is missing.

## View (render-only)

- The core's own dome on a Domes world: a glass dome over the core hex + field shell 1 tile out;
  a Pressure Dome's field shell 2 tiles out. Shader `shaders/containment_field.gdshader`.
- Veyra look: class L palette, scatter set (lichen mats, wind-carved rocks, frost-rimmed boulders, low shrubs),
  night frost, dust devils, crews outside domes in EVA suits (`CrewModels` role `eva`).
