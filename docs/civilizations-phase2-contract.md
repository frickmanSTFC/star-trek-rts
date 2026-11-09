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

## Phase 2 sim side, filled in (2026-11-09, space sim branch)

Stub signatures are unchanged (bodies filled in `game/sim/Space/Contact.cs`). Decisions and additions for the view:

- **Data** `data/civilizations/contact.json` (manifest: last file of the `civilizations` document; a data set without it
  has no first contact). `CivilizationData.Contact` → `ContactRules` (`CivilizationDefs.cs`: `ContactChoiceDef`,
  `ContactStepDef`, `UniqueTechDef`, `AllyClassDef`, `MembershipRules`, `AllyRules`, `AggressiveRules`, `AdvanceRules`),
  read + checked in the new partial `sim/Data/GameData.Contact.cs` (`ReadContact`, `ValidateContact`, called from
  `GameData.Civilizations.cs`). Owner's numbers: 3 steps × 80 s, real time (the game never pauses): **who goes** (captain /
  envoy team −8 / message only +6), **what to offer** (medicine −10 / cultural exchange −4 +20 research / nothing +4),
  **share technology** (none / limited +4, +25 research, −3 Standing if it ends in shock / warp help +12, +60 research, −10
  Standing if shock, 2 min shorter wait). Base shock 30%, temperament peaceful −10 / aggressive +15, +0.1 per awareness point,
  +30 below L5 where there is no rule, clamped 5–95. Trade income 6 duranium + 3 tritanium + 1 latinum a minute, allied ×2.
  Ally ships 1 as trade partner / candidate, 2 allied; never destroyed. Aggressive: from awareness 80, shoot down from L4 at
  25% a minute each, −5 Standing a shoot-down, refuse 10 min. Any incident → refuse 10 min. L4 → L5 0.35%/min (+0.01% per
  awareness point; ≈ 1 in 10 over 30 min), a 10-point awareness spike in that minute wrecks the attempt 75% of the time.
- **First contact** (`FirstContactProblem` order): no such player → no contact data → "only the Federation can make first
  contact for now" (`contact.factions`) → recalled → no such body → no ship / not in orbit → "finish the sensor sweep" →
  no civilization / "only ruins" → **"finish studying the X first"** (`needs_study`: Study = 1, so the level is known) →
  "under way" → "already in contact" → "the X refuse contact (N s)" → then, for a pre-warp world (below `min_level` 5) and a
  player with Standing (Federation, Prime Directive era), the General Order 1 text — the command still runs as a
  **violation**: Standing −10, an incident (counts toward recall), `ContactEvent.Violation`, ShockChance 100 (always culture
  shock). Before the Prime Directive (ENT, Dawn of Warp): no rule, only +30 shock.
- **The event**: `ContactEventOf` gives Step / Steps / the step's `Choices` / `Chosen` (-1 = not yet) / `ShockChance` (now) /
  `StepEndTick` / `EndTick` (predicted) + init `StepTitle`, `Violation`. A `ContactChoiceCommand` picks and moves on at once;
  a step that runs out takes choice 0. The flagship leaving orbit (or the site going away) calls it off (event, no penalty;
  it can start again). Outcome on `sim.Rng.NextInt(100) < ShockChance` when the last step ends.
- **Smooth** → `TradePartner` at once. **Shock** → `Contact`; trade partner after `shock_wait_minutes` 5 + the choices'
  `wait_minutes`; the natives' awareness +20, `CultureShockSecondsLeft(body)` 10 min (unrest: shown only, no effect yet);
  the choices' `standing_if_shock` on Standing.
- **Trade partner rewards** (once per player and world, on reaching Trade partner or better, also via the test panel):
  research boost 80 + the choices' research → `ObservationBank` (paid into the active surface tech like observation
  research); the **unique tech** = `unique_techs[H(site seed) % n]` (same for every player at that world) →
  `SurfacePlayer.SharedTechs` (new; hashed only when non-empty) whose modifiers count in `PlanetSurface.ResearchBonus`,
  `UnitResearchBonus` and `AutoMaintained` exactly like a researched surface tech (chosen over adding lab techs: no TechDef
  changes, never researchable in a lab, planet-only hashes untouched). `UniqueTechFrom` gives its name. Ally ships for the stage.
- **Trade income** each whole minute of game time (`tick % TicksPerMinute == 0`): `TradeIncomePerMinute(player)` = Σ income ×
  stage multiplier × (1 − walk-outs × 0.25). **Storage (chosen):** with a colony surface it goes through the surface's
  `Store` (new `PlanetSurface.StoreTrade`, `Surface/PlanetSurface.Contact.cs`): metal only as far as storage has room (none
  before landing), latinum always (storage-exempt); in a game without a surface straight into `Players[p].Stock`.
- **Membership** `MembershipProblem` order: player/data/recalled/body/no living civ → "under way" → refuse → None "make first
  contact first" / Contact "not ready to trade yet (N s)" / TradePartner "trade partners for N s more" (`partner_minutes` 3) /
  Candidate "membership talks take N s more" (`candidate_minutes` 5) / Allied "already an allied world" → "Starfleet Standing
  60 needed (now N)" (only players with Standing). Each step of a **fractured** world rolls `walkout_chance` 0.3 on sim.Rng:
  a nation walks out (event names it from `NationNames`), its trade cut 25% (`WalkedOut(player, body)`).
- **Ally ships**: `SystemShip` with `Owner` -1, `AllyOf` = player, `AllyHome` = their world, `ClassId` "native.<class id>"
  (not a ships.json class: `AllyClass(ship)` → `AllyClassDef` name/role; `Faction` "native", `Registry` = species), names
  from `ally.names`. `IsNeutral` is now false for allies (new `IsAlly`); neutral-traffic code skips them. **Escort**: when
  the flagship's body (`AtBody ?? ToBody`) differs, it sets off `escort_delay_seconds` 5 later (new `SystemShip.FollowTick`),
  flight = `TravelTicks` × 1.2. **Scout**: flies to `ScoutBody`, identifies it, sweeps `scout_sweep_seconds` 20 (uses
  `SweepBody`/`SweepStart/DoneTick`, so `SweepShare` works), charts it for the player (event `scout_charted`), then escorts
  again. Recalled players' allies go `Home`. `AllyOrderProblem`: no such ship / "not one of your allies" / recalled / escort:
  "no flagship to escort", "already escorting the flagship"; scout: no such body / "already scouting X" / "X is already charted".
- **Aggressive natives** (each whole minute): temperament Aggressive and awareness ≥ 80 → the first time, everyone who has
  Charted them is refused 10 min (`hostile` event, a running contact is called off); L4+ shoot down that body's satellites
  (removed from `Satellites`) and working posts (`ObservationPost.ShotDown` new + `Found`), each roll on sim.Rng, event
  `shot_down`, −5 Standing, refusal renewed. Any incident (post found, violation) also refuses 10 min.
- **L4 → L5** (each whole minute, sim.Rng): chance + awareness × per_awareness; a spike ≥ 10 awareness since the last
  minute's mark → wrecked with 75% (`warp_wrecked`), else level 5 (`warp`; towns re-rolled for the new level). Events only to
  players who have Charted the world.
- **Events** (`CivEvents`, new kinds): `contact_started`, `violation`, `contact_called_off`, `contact_smooth`, `culture_shock`,
  `trade_partner`, `unique_tech`, `ally_ship`, `membership`, `walkout`, `scout_charted`, `hostile`, `shot_down`, `warp`,
  `warp_wrecked` (plus phase 1's `incident`, `standing`).
- **Debug** `DebugCivProblem`/`ApplyDebugCiv` (every field): SpawnLiving (level 1..6, whole; not on the landing world; needs
  a class with a surface or a town-map stand-in) / SpawnRuins (also ruins_weights classes) make a site with a generated
  species (hash of the seed, tick, body, `DebugEdits`) replacing any there (study, relations and events there start over;
  sites stay in body order, study arrays follow); Remove; Level (towns re-rolled); Awareness 0..100 (may lower);
  Temperament 1..3; FinishStudy (also Charts the body for the target); Standing 0..max (> 0 undoes a recall — test panel
  only); FindPost (a working post of the target there → found, as a roll would); Relations 0..4 (cancels a running event,
  gives the stage's rewards/ships; lowering removes extra ally ships — the only way they go). "no star system" in
  planet-only games.
- **API additions** (beyond the stubs): `ContactEvent.StepTitle/Violation`; `StarSystemState.TradePartners(player)`,
  `CultureShockSecondsLeft(body)`, `AllyClass(ship)`, `WalkedOut(player, body)`; `SystemShip.IsAlly`, `FollowTick`;
  `ObservationPost.ShotDown`; `SurfacePlayer.SharedTechs`; `CivilizationData.Contact`.
- **Hash** (system games only): relations, running events, per-site culture shock / awareness mark / hostility, the sites'
  living/level/temperament/seed (they can change now), ally fields + `FollowTick`, posts' `ShotDown`, the ally counter; the
  surface's shared techs only once there is one (planet-only and balance hashes unchanged).
- Tests `ContactTests` (17). Not yet: culture shock/unrest has no gameplay effect beyond the wait; no trade-route object
  (the view draws one per `TradePartners` body); a small outpost on an allied world (no landing elsewhere yet); allies have
  no combat; other powers can't make contact.
