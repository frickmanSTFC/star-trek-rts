# Civilizations phase 2 + CIV test tab contract (2026-11-09)

Shared names so the space sim, the surface (ruins) sim, the view and the models can run in parallel. Phase 1 is in
`docs/civilizations-contract.md` (read its "Sim side, filled in" section first). Design doc: claude.ai/artifact/A8NZp9KCb7pft9mo7xLbYB.
All numbers are placeholders.

## Owner's answers

- **First contact** (Federation, L5/L6 worlds; below L5 it breaks General Order 1 where the Prime Directive applies):
  a **timed event of ~4 minutes with 2–3 dialogue-style choices** (e.g. envoy style, gifts, tech sharing) that shift the
  odds of **culture shock** vs a **smooth start as a trade partner**.
- First contact gives **all four**: a trade partner (steady resource/latinum trickle + a trade route), research (a
  one-off boost + a unique local tech), **ally ships** and a membership path. Stages **Contact → Trade partner →
  Candidate → Allied world**; an allied world **stays theirs** (a small outpost later, its trade and ships, no free mining).
- **Ally ships: escort + scout** — they follow the flagship or fly to a body and chart it; weak, never land anything.
- **Aggressive natives** past awareness 80: L4+ can **shoot down your satellites/posts** over their world, and they
  **refuse contact** for a while. No ground combat.
- An **L4 world can reach L5 (warp) during a match** (small chance a minute; contamination can speed it up or wreck it).
- **Ruins on your own landing world**: about **1 in 4** colony worlds has a ruin site. Dig with an **Excavation Site**
  building on the ruin hex; crew dig **layers**, each layer rolls a find: **a mix of research, resources and dangers**.
  A woken **defence system damages nearby buildings/vehicles and locks the dig** a few minutes (research can disarm
  later); a released **pathogen makes the base's crew sick** (slower work) ~5 min (a medical tech shortens it).
  Mostly invented species; canon precursors (Iconians, T'Kon, Preservers) are very rare finds.
- **CIV test tab** in the F9 test panel: spawn a civ on a body, set level/awareness/standing, finish study, force a
  post discovery (+ relations stage, temperament, ruins on the colony map).

## Commands (tags in `CommandChecks`, routing done)

- Space (routed to `StarSystemState.ApplyContact`, `Space/Contact.cs`): `FirstContactCommand(player, bodyId)` 31,
  `ContactChoiceCommand(player, bodyId, choice)` 32, `MembershipCommand(player, bodyId)` 33,
  `AllyShipOrderCommand(player, shipId, bodyId?)` 34 (null = escort the flagship).
- Debug: `DebugCivCommand(player, target, bodyId, DebugCivField, value)` 117 → `StarSystemState.DebugCivProblem` /
  `ApplyDebugCiv` (fields in `Core/DebugCommands.cs`); `DebugRuinCommand(player, tile, add)` 118 →
  `PlanetSurface.RuinDebugProblem` / `ApplyRuinDebug` (`Surface/Excavation.cs`).
- No new surface command: the Excavation Site is placed with `PlaceBuildingCommand` (only on a ruin tile).

## Space sim API (`game/sim/Space/Contact.cs`; stubs)

`ContactStage`, `AllyTask`, `ContactChoice(Text, Hint, ShockShift)`, `ContactEvent(...)`; `SystemShip.AllyOf / AllyHome /
AllyTask / ScoutBody`; `StarSystemState.RelationsWith`, `ContactEventOf`, `FirstContactProblem`, `MembershipProblem`,
`RefusesContactSeconds`, `TradeIncomePerMinute`, `UniqueTechFrom`, `AlliesOf`, `AllyOrderProblem`, `DebugCivProblem`,
`ApplyDebugCiv`. Rules: the event needs the flagship in orbit (leaving calls it off); each step has a time limit (the
first choice is taken when it runs out); the outcome is rolled on `sim.Rng` at the end. Smooth → Trade partner at once;
shock → Contact (talks continue, trade after a wait), culture shock on the natives, Standing −10 only if forced below L5.
Trade income goes to the colony's stock each minute (resources + latinum, from data); the unique tech is a surface
research modifier given at once (pick from a small data list); 1–2 ally ships (`SystemShip` with `AllyOf`, a civ class
from data, weak). Membership: Trade partner → Candidate (needs Standing ≥ 60; fractured worlds may have a nation walk
out) → Allied (more income, ally ships stay). Aggressive natives at awareness ≥ 80: L4+ shoot down that player's
satellites/posts at that body (events) and refuse contact for N minutes. L4 → L5 chance a minute (data), raised by
awareness from contamination, can also be wrecked (awareness spike → level stays). Events go into `CivEvents`. New state
hashed only in system games.

## Surface sim API (`game/sim/Surface/Excavation.cs`; stubs)

`SurfaceRuin(Tile, Species, Layers)` + `LayersDug`, `LockedUntilTick`, `Exhausted`; `DigFind(...)`;
`PlanetSurface.Ruins`, `RuinAt(tile)`, `DigProgress(site)`, `DigProblem(site)`, `Finds`, `CrewSickSeconds(baseId)`.
Rules: ruins are rolled from the map seed AFTER map generation, without changing terrain (pick a buildable land tile
away from the landing area; never the same as a deposit or native tile); ~25% of maps (data). Data
`data/surfaces/excavation.json` (surfaces document): chance, layers range, seconds per layer, crew, find table
(weights: artifact → research, data core → research, tech fragment → a research boost or a one-off modifier, resources →
metal/dilithium/latinum, defence, pathogen), defence damage range + radius + lock seconds, sickness seconds + work
multiplier, species names, rare precursors. Building `excavation_site` (`data/buildings/surface/excavation_site.json`,
build_menu, `requires_ruin`, crewed, small power, wears like others; placement refused off a ruin tile). Techs
`artifact_analysis` (defences found disarmed more often / lower damage) and `xenomedicine` (shorter sickness) in the
surface research list. Sickness = the base's crew work × multiplier (repairs, construction, crewed buildings). Hash new
state (ruins only when the map has one, so other maps' hashes stay; say what moved).

## Models (Blender agent)

- `game/models/buildings/excavation_site.glb`: footprint radius ≈ 7; dig grid with layered trench steps, scaffolding,
  a small gantry crane `Crane` → `Hook`, sifting screens `Sieve0..1` (shake), lamp masts, finds table with crates,
  survey tent/hut, `Status` board, `Beacon`, `SparkPoint`, `Build0..N`, `AddOnSpot0..4`, `RoofSpot0..1`, `SignFront`.
- `game/models/natives/ruins.glb`: ruin props for an undug ruin hex (and what's left around an excavation):
  `ruin_a` (broken walls + toppled pillars), `ruin_b` (half-buried dome + arch), `ruin_c` (obelisk ring), `ruin_far`.
  Weathered stone, alien shapes matching the towns kit's style; materials "RuinStone", "RuinMetal", "Glow" (faint, for
  precursor sites).
- Godot: `BuildingModels.Excavation.cs` (glb + primitive stand-in, dispatch), `PlanetView.Excavation.cs` (crane/sieve
  animation while digging, locked = red status + sparks, exhausted = idle), `PlanetView.Ruins.cs` (ruin props on every
  ruin tile the player has seen, hidden under a finished Excavation Site's footprint).

## View (UI agent)

- First contact: a FIRST CONTACT button on the body card / dossier (`FirstContactProblem`), the event panel (timer, step,
  2–3 choice buttons with hints, shock chance meter, result line), relations stage track in the dossier with the
  MEMBERSHIP button (`MembershipProblem`), refusal countdown, trade income on the dossier and as a top-bar tooltip line,
  unique tech line; ally ships on the system plot (ally colour) with a ship card: ESCORT / SCOUT <body> buttons.
- Surface: ruin marker on the minimap and the hex tooltip ("Ruins of the <species> · N layers"), Excavation Site panel
  (layer progress, `DigProblem`, finds list, lock countdown), sick-crew chip on the base panel, find event lines.
- Test panel CIV tab (`TestPanel.Civ.cs`): body picker (system bodies), spawn living (level) / ruins / remove, level,
  awareness, temperament, finish study, Standing, force post discovery, relations stage (target = local player), and a
  ruins row (add/remove ruins at a picked tile) — all through `DebugCivCommand` / `DebugRuinCommand` with
  `Simulation.DebugProblem` first, like the other tabs; marks TEST MODE.

## Surface sim side, filled in (2026-11-09)

Branch of the surface sim agent; placeholder numbers, owner's answers of 2026-11-09 folded in (3–5 layers of 120 s,
salvage an exhausted site, moderate defences that never wreck, precursor rewards, pathogens spread by vehicles, an
orbital region hint).

- **Data:** `data/surfaces/excavation.json` (a part of the surfaces document, `_notes_excavation` explains every field;
  manifest after `fabrication.json`): `chance` 0.25, `sites` [1, 1], `terrain` ["hills"] (must be buildable and not landing
  ground, so a colony never lands on a ruin), `edge_rows` 3, `min_distance` 6, `building` excavation_site, `layers` [3, 5],
  `seconds_per_layer` 120, `finds` weights (artifact 26, data_core 14, tech_fragment 10, resources 26, defence 14,
  pathogen 10), `artifact`/`data_core` research ranges + names, `tech_fragments` (smelter schematics: refinery yield +10%;
  bearing alloy: wear −10%; field theory: 120 research), `caches` (metal store, dilithium cache, latinum vault),
  `defence` (damage [20, 40], radius 1, lock 180 s, disarmed 0.15; `artifact_analysis` → disarmed 0.6, damage × 0.5),
  `pathogen` (300 s, work × 0.6; `xenomedicine` → × 0.4; `spread_share` 0.5, `immunity_seconds` 120), `species` (weight 60,
  ten invented names), `precursors` iconian / tkon / preserver (weight 1 each, layers [5, 7], danger 2 / 2 / 1.5 × the
  defence and pathogen weights, `reward`: Iconian gateway fragment = drop ship turnaround × 0.5 + 60 research; T'Kon power
  lattice = mine + deep-core mine speed +30% + 60 research; Preserver archive = 400 research). Building
  `data/buildings/surface/excavation_site.json` (90 dur + 30 tri, 40 s, crewed, power 4, wear 2, footprint 7, access [0, 9],
  `requires_ruin`; last in `build_menu`; default kit mechanical_spares). Techs `artifact_analysis` (90) and `xenomedicine`
  (80) at the end of `surfaces/research.json` (no modifiers: their effects are the excavation numbers).
- **Loader/checks:** `sim/Data/GameData.Excavation.cs` (`GameData.Excavation` = `ExcavationRules` + `DigFindWeight`,
  `DigResearchFind`, `TechFragmentDef`, `ResourceCacheDef`, `DigDefenceRules`, `DigPathogenRules`, `PrecursorDef`,
  `PrecursorReward`; tolerant reader, `ValidateExcavation`: ranges, every kind once, terrain buildable and not landing
  ground, the building has `requires_ruin` and is in the build menu, techs exist, resources known, modifiers name known
  buildings/units, rewards give something). `BuildingDef.RequiresRuin`.
- **Ruins** (`sim/Surface/Excavation.cs`): rolled in the `PlanetSurface` constructor (after map generation; own SplitMix
  stream from the map seed, never `sim.Rng`; terrain untouched, so `PlanetsArePlayable` / `DataChecks.PlanetProblems` are
  unchanged): hills only, no deposit, not toxic, no predators, outside the natives' land, 3 rows from the poles. 100 seeds:
  12–40 maps with ruins (test); of the balance seeds only 99999 has one. Precursor ~5% of sites.
- **Placement:** `PlacementProblem.NeedsRuin` (an Excavation Site off a ruin or on a dug-out one) and `RuinInTheWay`
  (anything else on a ruin that isn't dug out); text `PlanetSurface.RuinPlacementText(p)`. Also: `CanLand` false, outpost
  drop "ruins are there", founding `FoundProblem.Unbuildable`, test-panel placement refused, on a ruin that isn't dug out.
- **Dig:** in `Economy`: an Excavation Site with no ruin / a dug-out one is `Depleted`, a locked one `NoInput` (no power);
  working, progress += WorkRate (power × condition × speed) × crew share (Domes life support × crew health); each
  `seconds_per_layer` × 15 a layer is dug and one find rolled on `sim.Rng` (`Finds`). Research goes into the active surface
  tech, the rest (or all with none) into `DigResearchBank(player)`, paid when a tech starts. Resources go through `Store`
  (what doesn't fit is lost, said in the line). Defence: maybe "found disarmed"; else every building (condition) and
  vehicle (integrity) within the radius, any owner, the site too, loses the damage but stays ≥ 1 and is never broken by
  it; the ruin's `LockedUntilTick` = now + 180 s. Pathogen: the site's base sick (`CrewSickSeconds`), a second one restarts
  the clock. Precursor reward: an extra find of kind `precursor` on the last layer. An exhausted site sits `Depleted`
  ("site exhausted: every layer is dug"), salvage refunds 50%, the ruin stays (Exhausted) and other buildings may then use
  the tile.
- **Sickness:** `CrewHealth(baseId)` = work_multiplier while sick: × crewed buildings' work and output (`CrewShare`, every
  world), × building repairs (crew and drones, `RepairTick`), × vehicle repairs on that base's pad (`RepairWork`), and
  construction at that base advances on a carried share of ticks (`BuildsThisTick`). **Carriers:** a vehicle on the core
  tile of its owner's sick base carries it until that base is well (`Carrier(u)`, `CarrierSeconds(u)`); on the core tile
  of another of its owner's bases that is neither sick nor immune it makes it sick for spread_share × the carried time
  left; a base can't catch it again until immunity_seconds after its sickness (`ImmuneSeconds(baseId)`).
- **Orbit hint:** `RuinRegionHint(tile)` (any ruin in that tile's `SurfaceMap.Region`), `RuinHintRegions`,
  `PlanetSurface.RuinHintText` = "artificial structures detected".
- **API added beyond the stubs** (stub signatures unchanged): `SurfaceRuin.Precursor`; `DigFind` kind `precursor`;
  `Carrier`, `CarrierSeconds`, `ImmuneSeconds`, `CrewHealth`, `RuinRegionHint`, `RuinHintRegions`, `RuinHintText`,
  `DigResearchBank`, `DigModifiers(player)`, `ShuttleTurnaround(player)`, `RuinPlacementText`; `PlacementProblem.NeedsRuin`
  / `RuinInTheWay`; `BuildingDef.RequiresRuin`; `GameData.Excavation`. Dig modifiers count in `ResearchBonus`,
  `UnitResearchBonus` and `AutoMaintained`; the turnaround in `ShuttleCommand`'s `ShuttleBackTick`.
- **Test panel:** `DebugRuinCommand(player, tile, add)`: add refused off the map, on unbuildable ground, on a building, a
  deposit, natives' land, a landing pod's site, an unfolded vehicle, or existing ruins (species and layers from a SplitMix of
  seed + tile); remove refused with no ruins there or a building on them.
- **Hash:** `AddExcavationToHash` adds nothing while there are no ruins, finds, sickness, immunity, carriers, build carry,
  bank, dig modifiers or turnaround factors (test: add + remove a ruin = the same hash as an untouched game with as many
  test-panel edits). Hash moves: the two new techs grow every player's `Known`/`ResearchProgress` arrays, so **every**
  state hash moved (all 12 balance finals; on seeds 1 and 1701 that is the only change); seed 99999 has a rolled ruin, so its
  three finals moved again, with identical bot play (no measure changed; the bot never placed on that hill). The balance
  measure `ms:research_all` is gone from the reports (nobody researches the two ruin techs). The golden `presplit` test no
  longer applies to the edited data and still passes.
- Tests `ExcavationTests` (21). Not yet: the view (Excavation Site panel, find lines, sick chip, carrier marker, orbit
  findings line), the bot never digs, sickness doesn't spread between players, a find of a dug-out ruin can't be re-dug.
