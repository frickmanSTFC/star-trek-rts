# Parts economy contract (2026-11-09)

Shared names for parts-economy phases 1–3, so the sim/data, Machine Shop model and UI work can run in parallel.
Design doc: "Early Era, Parts & Maintenance" (claude.ai/artifact/UX7xT6yVWHZPingVsUErCH). Mockup:
`docs/mockups/parts/index.html` (claude.ai/artifact/5Uo4yRzX4b6eCZvXm5jHs9). All numbers are placeholders.

Owner's answers: repairs use spare kits first (construction parts in building costs come later, phase 4); a missing
kit = **wait for the shop or jury-rig**; parts and kits **take storage**; keep-in-stock targets **on by default**
(2 of each kit your buildings and vehicles use); the **Colony Hub has a small workshop** (the four spare kits only,
straight from materials, half speed, one at a time, 2× the summed shop time); the Machine Shop needs **crew + power**
and wears (it needs its own Mechanical + Electrical Spares); stored kits never decay; no landing crate.

## Data (sim agent)

Items are **resources** (they live in the player's `ResourceBag`, count toward storage, show in the test panel):
new file `data/resources/parts.json` (add it to the manifest), tier `part`, `spare` or `construction`, plus a storage
`volume` (1 per part, 2 per kit / construction part; other resources count 1 as now).

| id | name | tier | Machine Shop recipe | s |
| --- | --- | --- | --- | --- |
| structural_frame | Structural Frame | part | 4 duranium + 1 tritanium | 20 |
| hull_plating | Hull Plating | part | 2 parasteel | 25 |
| mechanism | Mechanism | part | 2 duranium + 1 tritanium | 15 |
| power_coupling | Power Coupling | part | 1 parasteel + 1 duranium | 20 |
| control_module | Control Module | part | 2 duranium + 1 tritanium (circuit boards; isolinear later) | 30 |
| seal_kit | Seal Kit | part | 1 parasteel + 1 tritanium | 20 |
| mechanical_spares | Mechanical Spares | spare | 2 mechanism + 1 structural_frame | 15 |
| electrical_spares | Electrical Spares | spare | 1 power_coupling + 1 control_module | 15 |
| rover_spares | Rover Spares | spare | 1 mechanism + 1 hull_plating | 12 |
| environmental_spares | Environmental Spares | spare | 2 seal_kit + 1 control_module | 15 |
| prefab_module | Prefab Module | construction | 2 structural_frame + 2 hull_plating | 30 |
| machinery_set | Machinery Set | construction | 2 mechanism + 1 power_coupling + 1 control_module | 35 |

The design doc's recipes use plasma conduits, isolinear chips and transparent aluminum; nothing makes those on the
surface yet, so these use duranium, tritanium and parasteel only (note it in the data's `_notes`).

New part file `data/surfaces/fabrication.json` (surfaces document): `fabrication` =
- `recipes`: one per item above (`id` = output id, `in`, `seconds`; optional `era_min` / `era_max` for later).
- `hub`: `recipes` [the four kits], `speed` 0.5, `time_factor` 2 (the hub's seconds = time_factor × the summed shop
  seconds of the whole tree, inputs = the tree flattened to materials).
- `kit_points` 25 (one kit per 25 condition/integrity points repaired, rounded up).
- `kits`: which kit each building / unit uses: `buildings` { fusion_reactor, science_lab: electrical_spares;
  pressure_dome, life_support_plant: environmental_spares }, `units` { survey rovers + construction_rover:
  rover_spares; mining_crawler: mechanical_spares }, `default` mechanical_spares.
- `breakdown_kits` per crawler severity: minor 1, major 2, explosion 3 (added to its damage kits).
- `pad_tyre_kits` 1 (Rover Spares per pad tyre, replacing `tyres.pad_cost`).
- `jury_rig`: `max_condition` 60, `speed` 0.333, `wear_multiplier` 2, `breakdown_multiplier` 2.
- `default_target` 2 (kits only, for each kit a finished building or vehicle of the player uses).

`cost_per_point` in `maintenance` and unit `repair`, and `tyres.pad_cost`, go (or become empty): repairs now cost kits
and crew time. Building `machine_shop` (`data/buildings/surface/machine_shop.json`, in `build_menu`, category Industry):
cost 140 duranium + 40 tritanium, 60 s, `crewed` true, `power_use` 12, `wear_per_min` like the foundry, footprint
radius 7 with an `access` point, `fabricator` { `lines` 1, `speed` 1 }; Mk II level (speed 1.5) optional.

## Sim API (`game/sim/Surface/Fabrication.cs`; stubs now, the sim branch fills them)

- `FabOrder(Id, RecipeId, Auto)` + `Progress`, `Started`; `SurfaceStructure.Fabrication` (queue), `JuryRigged`
- `SurfaceUnit.JuryRigged`, `JuryRigOrdered`
- `enum FabStatus { None, Idle, Working, LowPower, NoInput, Broken, Paused, Building }`
- `IsFabricator(s)`, `IsHubWorkshop(s)`, `RecipesAt(s)`, `InputsAt(s, recipe)`, `SecondsAt(s, recipe)`,
  `FabricateProblem(player, structureId, recipe)`, `FabricatorStatus(s)`, `OrderProblem(s, order)`, `QueueSecondsLeft(s)`
- `PartItems` (all item ids in display order), `StockTarget(player, item)`, `Queued(player, item)`
- `KitOf(structure|unit)`, `KitsNeeded(structure|unit)`, `RepairWaitReason(structure|unit)`, `KitEtaSeconds(player, kit)`
- `JuryRigProblem(player, structureId)`, `JuryRigProblem(unit)`
- Commands (tags 25–29, already in `CommandChecks`, routed to `ApplyFabrication`): `FabricateCommand(player,
  structureId, recipeId, count)`, `CancelFabricateCommand(player, structureId, orderId)`, `StockTargetCommand(player,
  itemId, target)`, `JuryRigCommand(player, structureId)`, `JuryRigUnitsCommand(player, unitIds)`.

Rules: one order runs at a time per fabricator (inputs taken when it starts; skipped orders with no input stay
queued); progress × power share × condition (`WorkRate`), hub × hub.speed; output goes through storage (waits if full).
Keep-in-stock: per player; below target (stock + queued) an Auto order goes to a Machine Shop of that player, else
(kits only) to a core's workshop, with the parts a kit is missing queued in front. A repair takes all its kits when the
crew start work (drones: when they start); short → waiting with `RepairWaitReason`, and the missing kits are queued
at the front as Auto orders. Jury-rig needs no kits. Fabrication state, targets and `JuryRigged` are hashed.
Achievement / balance-runner hashes for games that never touch parts must stay identical where possible; report it
if not (the balance targets may need a retune: say which).

## Model (`game/models/buildings/machine_shop.glb`, Blender agent)

Footprint radius ≈ 7. Parts `Build0..N` (exocomp pieces), `Arm0..1` (robot arms, rotate about local Y; `Arm<k>Joint`
child bends about local X), `Press` (moves down local −Y while working), `SparkPoint0..1`, `Door` (roll-up, scale Y),
`Conveyor` (material "Belt", UV scroll), `Status` (emissive), `Beacon`, `AddOnSpot0..4`, `RoofSpot0..1`, `SignFront`
(MACHINE SHOP nn). `BuildingModels.MachineShop.cs` (glb + primitive stand-in with the same names), dispatch in
`BuildingModels.BuildModel`, animation in `PlanetView.MachineShop.cs` (working = `FabricatorStatus` Working/LowPower).

## UI (render-only, UI agent)

- Machine Shop selected: the command card shows recipe tiles (like Vehicle Bay orders; click +1, Shift +5); the
  building panel shows status, crew/power/condition, the current order with progress, the next orders (× cancels),
  and short-input warnings. A selected colony core shows its workshop (the four kits) the same way.
- Inventory screen (key **I** when not placing; Esc closes): every `PartItems` item with stock, keep target (− / +),
  queued, storage space; filters parts / kits / construction; storage bar split metals vs parts.
- Repairs: a building or vehicle waiting for kits shows `RepairWaitReason` + ETA and a **JURY-RIG** button
  (`JuryRigProblem`); jury-rigged ones are labelled; event lines for "waiting for kits", "order done", "jury-rigged".
- Build drawer: Machine Shop tile under Industry (render, card, description from data).

## Sim branch notes (phases 1–2 built)

- Added to the API (nothing in the stubs changed): `HasFabricator(s)` (the kind of structure has a queue, finished or
  not), `FabStatus.StorageFull` (last: a done order waits for room), `PlanetSurface.MaxFabQueue` / `MaxFabCount` (50),
  `SurfaceStructure.JuryRigOrdered`, `RepairPaid`, `LastFabricated` + `FabricatedAtTick` (for "order done" events),
  `SurfaceUnit.RepairPaid`, `ResourceDef.Volume` / `IsPartItem`, `BuildingDef.Fabricator` (`FabricatorDef(Lines, Speed)`),
  `GameData.Fabrication` (`FabricationRules`: `Recipes`, `Recipe(id)`, `Hub` (Recipes, Speed, TimeFactor), `KitPoints`,
  `JuryRig` (MaxCondition, Speed, WearMultiplier, BreakdownMultiplier), `DefaultTarget`, `PadTyreKit(s)`,
  `KitsForBreakdown`), `GameData.KitOfBuilding` / `KitOfUnit` / `Flattened(id)` / `VolumeOf` / `MaxStockTarget` (999).
- Hub time: `SecondsAt` at a core = time_factor × the tree's summed shop seconds (Mechanical Spares 130 s, the owner's
  "2× the summed shop time"); hub.speed 0.5 is the same thing and is not applied a second time.
- `MaintenanceCost(s)` / `RepairCost(u)` now return the kits (kit × KitsNeeded); `MaintenanceWaiting` / `RepairWaiting`
  mean "waiting for kits" (the UI's "waiting for duranium" texts should use `RepairWaitReason`).
- `JuryRigProblem(player, id)` returns "a jury-rig is already ordered (again to cancel)" while one is ordered: the
  button can turn into a cancel there. A unit can only be jury-rigged while it is on its repair pad (`IsRepairing`).
