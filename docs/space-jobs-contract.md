# Space jobs contract

Design page: `docs/mockups/space-jobs/index.html` (claude.ai/artifact/UNaUDgVTpJ8Jt64EczeYNc). Read it first: it has the
three screens, the site catalogue, "The Quiet Hulk" written out, the chain additions and the rules. Every number is a
placeholder and lives in data.

## Decided (owner 2026-11-24/25)

- Hull damage: **yes**. Crew loss: **yes**. Owner: "go with your suggestions, build it in the order you listed".
- Mined ore: sold for latinum (to a station or a passing freighter alongside) or handed in for jobs; a colony ship can
  drop it at its colony (into the colony's stock through storage).
- Colony ships (Horizon, Cochrane) get space jobs too, but only for sites within one flight (≤ 60 s) of the colony body;
  leaving orbit blocks drop ships and landings as today.
- Lost crew can be replaced: rescued survivors may join (by division) and a station hires 2 at a time. Otherwise losses
  stay for the match.
- Hull at 0 = crippled: the ship drifts (can't set course), every active space job fails, field repairs bring it back
  to 20% over 3 minutes. No game over.
- Four ship systems (sensors, engines, tractor, life support) on top of the hull bar.
- Towing: yes (flights 1.6× slower while towing).
- Build order: **phase 1** sites + scanning + hull damage + crew + the derelict and debris jobs with the away team →
  **phase 2** asteroids and mining (+ selling, towing) → **phase 3** events (rogue asteroid, comet, lifepods, stranded
  ship) → **phase 4** the eight chain missions (two per space chain).

## Rules (placeholders; all in data)

- **Sites:** each generated system rolls 4–7 at the start between the orbits (not on a body), plus **one Station**
  (always Identified, never spent: dockyard, buyer, hires crew), and more arrive during the match (~1 every 6 min; phase
  3). Knowledge per player: Detected (grey contact + size; found by the long-range scan like bodies) → Identified (fly
  within 120 map units, `SiteScanCommand`, 20–45 s by kind × the sweep_time lever; leaving range calls it off) → Spent
  (worked out; greyed; drifts off after 5 min).
- **Flight to a site:** `SiteCourseCommand`; same travel model as bodies (distance → 20–90 s, ÷ ship speed, × flight_time
  lever, × hull band). A ship at a site is not in orbit of anything (`AtBody` null) and not in flight: it holds there.
- **Hull 0–100** per flagship. Bands: 75–100 Scuffed (no effect), 50–75 Worn (flights +10%), 25–50 Damaged (flights
  +25%, scan reach −20%), 1–25 Critical (no hazardous jobs, flights +60%), 0 Crippled (above). Repairs: field repair by
  Engineering crew +1% / 10 s up to 60% (speed × repair_speed lever from the Engineering share); hull patch kit (hold) +15%
  in 30 s or fixes one system at once; dockyard (the Station; a colony ship's colony body) back to 100%.
- **Systems** (sensors, engines, tractor, life support): a hazard can knock one out for 60–180 s (sensors: scans 30%
  slower; engines: flights +30%; tractor: no mining/towing/pick-up; life support: injured crew don't recover). Back on
  their own, or a patch kit at once.
- **Crew** by division (fit-out; a ship with no fit-out uses its fit_out.json crew and suggested preset). Injured: out
  3 min (medical stores −1 min each, max 3), then back. Lost: gone for the match. The fit-out levers use the crew on duty
  now (0.4% a point of share, as the fit-out), so a lost pilot makes scans a little slower. Below a division's floor its
  effects stop and the card warns. Below half the starting crew: flights +25%.
- **Away team** (`AwayTeamCommand`): 2–6 crew (half the crew, at most 6; Armstrong at most 2). Three steps × 60 s,
  game never pauses, a step that runs out takes its first choice. Odds: injury / loss per person in percent, find chance;
  choices move them; each Defense member −4 loss points; a Science medic on the team turns half the losses into
  injuries. Outcome rolled once at the end on `sim.Rng`, then each member rolls. Leaving the site recalls the team: job
  failed, no reward. The Quiet Hulk's table (design page) is the model for every boarding.
- **Hold:** the fit-out hold (containers) minus cargo still aboard. Ore 30 per container; salvage items and survivors (3
  per container) take space. A full hold stops mining / pick-ups.
- **Side jobs for space:** `data/missions/space_jobs.json`, offered from sites (identified or event) to every ship's
  chain (all six chains; colony ships only for sites within one flight), at most 2 active, offers lapse after 90 s,
  failing costs only the reward (hull damage and crew loss stay).
- **Rewards** for ships that never land: latinum (stock), research (into the same place observation research goes:
  `ObservationBank` / the active tech), crew (survivors), patch kits, salvage parts. Colony ships: as the colony's
  rewards.

## Shared API (on main as stubs; names fixed)

Sim (`game/sim/Space/`): `SpaceSites.cs` (`SiteKind`, `SiteKnowledge`, `SpaceSite`, `SiteRow`, `SiteReport`,
`SiteAction`; `StarSystemState.Sites`, `Site(id)`, `SiteKnowledgeOf`, `SiteReportFor`, `ShipSite`, `SiteBound`,
`SiteCourseProblem`, `SiteScanProblem`, `SiteWorkProblem`, `SiteScanShare`, `SiteWorkProgress`, `ApplySpaceJobs`,
`DebugSpaceProblem`, `ApplyDebugSpace`), `ShipStatus.cs` (`ShipSystem`, `HullBand`, `SystemDown`, `CrewLine`, `HoldLine`,
`ShipLogLine`, `ShipStatusView`; `StatusOf`, `RepairProblem`, `HoldProblem`), `AwayTeam.cs` (`AwayChoice`, `AwayMember`,
`AwayTeamView`; `AwayTeamOf`, `AwayTeamProblem`, `AwayChoiceProblem`, `MaxAwayTeam`).

Commands (`Core/Commands.cs`, `CommandHash` tags): `SiteCourseCommand` 40, `SiteScanCommand` 41, `SiteWorkCommand` 42,
`AwayTeamCommand` 43, `AwayTeamChoiceCommand` 44, `ShipRepairCommand` 45, `ShipHoldCommand` 46, `DebugSpaceCommand`
122 (`DebugSpaceField`). Routed to `StarSystemState.ApplySpaceJobs` / `DebugSpaceProblem` / `ApplyDebugSpace`.

The sim agent may add members (list them under "Phase 1 sim side, filled in" below) but not rename these.

## Phase 1 agents and their files

**SIM** (`game/sim/`, `game/tests/`, `data/`, `docs/space-jobs-contract.md` "filled in" section; not `game/scripts/`):
- Data: `data/maps/sites.json` (`space_sites`: kinds with scan seconds, sizes, variants + weights, contents, hazards,
  work actions; generation counts and placement; station) and `data/maps/ship_status.json` (`ship_status`: hull bands,
  systems, repairs, crew injury/loss, away team rules and the step/choice tables per site kind) in the maps document;
  `data/missions/space_jobs.json` (the derelict + debris side jobs, incl. The Quiet Hulk, a known-origin derelict job and
  a debris sweep with a black box; vocabulary added to `missions/notes.json`); fit_out.json: space cargo `patch` (hull
  patch kit), `med_space` (medical stores), new space levers if needed (e.g. `site_scan`, `field_repair`, `away_loss`);
  manifest entries for new files. Loader + `Validate` checks for all of it.
- Sim: the stubs filled in; `SystemShip` gains `AtSite`/`FromSite`/`ToSite` (or equivalent) with `InFlight` still
  meaning "between places"; every existing caller of `AtBody`/`InFlight` checked (drop ships, landing, orbit gate,
  sweep, comms delay, missions' `ship_at`/`visited`, ally escort, neutral traffic must never be at a site unless meant).
  Long-range scan also Detects sites in range. Hull/systems/crew/hold state per flagship. Away team event. Field repair,
  patch kits, dockyard. Crew-aware fit-out factors (`Simulation.FitOutFactor` reads the crew on duty). Mission
  conditions/actions for sites (`site` found/identified/at/alongside/boarded/swept/done, `hull`, `crew`, `hold`; actions
  `place_space_site`, `bind_site`, `damage`, `lose_crew`, `give_crew`, …) and the side-job pool across chains.
  Debug commands. Randomness: hashes of the system seed (sites, contents) and `sim.Rng` only for outcome rolls.
- Hash: everything new hashed in system games; **planet-only and balance hashes must not change**; a system game with
  no player action may change (sites are new), but the same run twice must give the same hashes.
- Tests: `SpaceSiteTests`, `ShipStatusTests`, `AwayTeamTests`, `SpaceJobTests` (each new job played on the real rules,
  twice, same hashes; every space ship and both colony ships), fuzz kinds for the new commands, refusals change nothing,
  `CommandHashCoversEveryCommandType`. `dotnet build FinalFrontier.sln && dotnet test` green.

**VIEW** (`game/scripts/` only; commands only, after the sim's own problem check; render-only):
- System plot (`SystemPlot*.cs`): site glyphs + labels by kind and knowledge (the design page's shapes and colours:
  unknown grey diamond "?", wreck/debris rust, asteroid amber, anomaly lavender, people cyan, station sky blue), scan
  range ring round the flagship, course line to a site, the ship parked beside a site; click = site card.
- Helm cards (`PlanetDriver.System.cs` + a new `PlanetDriver.Sites.cs`): site card from `SiteReportFor` (rows, hazards,
  actions as buttons: SET COURSE / SCAN / work actions / AWAY TEAM; greyed with the problem as tooltip), scan and work
  progress bars; jobs list as today (missions tracker already shows space jobs).
- Away-team panel (new `AwayTeamPanel.cs`, like `ContactPanel.cs`): team picker before launch (per-division − / +, max,
  who gets picked by name), then the three timed steps with choice pills, the live injury/loss/find meters, result line.
- Ship status (new `ShipStatusScreen.cs` + helm rail entry, e.g. STATUS or under SHIP): hull bar with bands and effect,
  systems with timers, repairs with buttons (`ShipRepairCommand`), crew by division with the floor marks, injured/lost,
  hold lines with SELL / DROP / JETTISON where allowed (`ShipHoldCommand`), damage log. Hull + crew chip on the helm top
  bar (the design page's "HULL 78% · CREW 38/40").
- Orbit view at a site (`OrbitView.Sites.cs`): when the flagship holds at a site, the orbit display shows the site's
  model (from `SiteModels.cs`, loading `models/space/sites.glb`, primitive stand-ins if missing) with the flagship model
  beside it; no planet globe (stars + a far body or two). Event lines for the new things (identified, damage, crew
  injured/lost, repairs, away team results).
- Test panel: a SPACE tab (`TestPanel.Space.cs`) for every `DebugSpaceField`; note that the test panel opens only on
  the surface today: make F9 work in orbit for ships that never land (known gap from the ship pick).
- `dotnet build` green (scripts type-check against GodotSharp); re-read for Godot runtime traps.

**MODELS** (`blender/`, `game/models/space/`): `blender/build_space_sites.py --game` → `game/models/space/sites.glb`
with root nodes (each on its origin, -Z forward like the ships, metres-ish at the ship models' scale): `derelict_unknown`
(~180 m alien hull, broken in the middle, panel lines, one faint aft light `Light0`), `derelict_earth` (an Earth
freighter/explorer in the pre-warp style of the six ships, dark, tumbling-ready), `derelict_trader`, `derelict_native`,
`debris_piece_a..f` (hull plates, a nacelle stub, a cargo pod `CargoPod`, a black box `BlackBox` with a blinking
`Beacon`), `asteroid_common/metal/ice/hollow` (+ `hollow` cavity mouth), `lifepod` (with `Beacon`), `comet_nucleus`,
`station` (a small pre-warp station: docking ring `Ring` (spins), `Berth0..1`, lights), `probe_alien`. Materials named
for the view (`Hull`, `Lamp`, `Beacon`, `Glow`, `Ice`, `Rock`, `Metal`). Previews in `blender/previews/space_sites/` +
a contact sheet. Time-box: one finished preview of the derelict first, then the rest; whole job ≤ ~1.5 h.

## Phase 1 sim side, filled in

Built by the SIM agent (2026-11-25). Every number is a placeholder in data; `Validate()` checks all of it.

**Data.**
- `data/maps/sites.json` `space_sites` (maps document; `GameData.SpaceSites` = `SpaceSiteRules`, `sim/Data/SpaceJobDefs.cs`,
  read + checked in `GameData.SpaceJobs.cs`): generation count [4, 7] over orbit 130–480 map units, clear_of_body 40,
  spacing 70, 40 tries, weights derelict 3 / known_derelict 3 / debris 4; identify_range 120; spent_seconds 300;
  colony_reach_seconds 60; kinds station (S, scan 20, variant trade), derelict (C, scan 30, variant unknown, alongside 20 s
  + 40% debris roll for hull 2–6, away table derelict, research 80–150), known_derelict (K, scan 25, variants earth 2 /
  trader 2 / native 1, alongside 20 s + 30% for 1–4, restore_power 60 s or 1 patch kit, logs 20 s → latinum 20–40 +
  research 20–40, reactor fails 300 s after coming alongside: hull −15 if still alongside, then spent), debris (D, scan 20,
  variants freighter 2 / explorer 1, sweep 90 s, hull −1 every 10 s inside, 3–5 finds: the black box first, then
  cargo_pod 4 / lifepod 2 / hull_alloy 2, lifepods 1–3 survivors); salvage cargo_pod 25 latinum, hull_alloy 15,
  black_box 10 + 40 research, power_cell 60 + 30, data_core 30 + 60 (1 container each); station names, orbit 200–320,
  dock 30 s, hires 2 a visit at 15 latinum, survivor_latinum 10. Kind ids are `SpaceSiteRules.IdOf(SiteKind)`
  (snake case: `known_derelict`); `KindOf` also takes `knownderelict`.
- `data/maps/ship_status.json` `ship_status` (`GameData.ShipStatus` = `ShipStatusRules`): hull bands scuffed 75 /
  worn 50 (flight ×1.1) / damaged 25 (×1.25, scan reach ×0.8) / critical 1 (×1.6, no hazardous jobs) / crippled 0;
  crippled recovers to 20 over 180 s; systems sensors (scan ×1.3) / engines (flight ×1.3) / tractor / life_support,
  down 60–180 s; field repair 1% per 10 s up to 60 (× `field_repair` lever, needs Engineering at its floor); patch
  +15 in 30 s; injury 180 s − 60 s per medical store (max 3, at least 30); half crew → flights ×1.25; log 40 lines;
  ranks / roles / names for away-team members; default_ship crew [10, 10, 10, 10], hold 8, focus allround;
  default_patch_kits 1 (every flagship without a fit-out); hold survivors_per_container 3; away_team min 2, max 6,
  max_share 0.5, max_by_ship armstrong 2, step 60 s, return 20 s (+ choice seconds, at least 5), defense_loss 4,
  medic science (half the losses become injuries), quarantine 180 s, tables `derelict` (The Quiet Hulk's: how in /
  inside / what we bring back; outcomes clean 60 (+ site research) / surge 20 (hull 12, 1 injured, sensors down) /
  defences 12 (loss ×2, find ×0.5) / quarantine 8) and `known_derelict` (how we board / search pattern / bring back:
  survivors 1–3 at 70%, a cargo pod, or the computer's research; outcomes clean 70 / surge 15 / collapse 15; while dark:
  injury +4, loss +1, find −10).
- `data/missions/fit_out.json`: space levers `field_repair` (Engineering, more is better) and `away_loss` (Defense,
  less is better) (`FitOutRules.FieldRepair` / `AwayLoss`, in `KnownLevers` and `SpaceLevers`); space cargo `patch`
  (Hull patch kit, engineering, 1 container, max 4, `patch_kits: 1`) and `med_space` (Medical stores, science, 1
  container, max 3, `medical: 1`) → `FitOutCargoLine.PatchKits` / `Medical`. Not in any preset's base or fill, so
  every preset is as before. Site scans use the existing `sweep_time` lever; coming alongside `alongside_time`.
- `data/missions/rules.json`: chain `space_jobs` with `"pool": true` (`MissionChainDef.Pool`): a side-job pool, never a
  game's chain (`Enable("space_jobs")` is refused; it can't be the first chain), its missions offered to every chain's
  players in games with a star system. `data/missions/space_jobs.json` (manifest): `quiet_hulk` (science; unknown
  derelict: identify → alongside → board; 120 research), `dead_in_the_water` (ops; known derelict: identify → alongside +
  power → logs + search; 40 latinum + 40 research), `what_happened_here` (control; debris: identify → the black box in the
  hold (sweeping the whole field optional); 30 latinum + 40 research + a patch kit). Vocabulary in `missions/notes.json`.

**Sim API added (beyond the stubs).**
- `SystemShip`: `AtSite`, `FromSite`, `ToSite` (internal set); `InFlight` = `AtBody == null && AtSite == null`. Flights
  to and from sites set `DepartTick` / `ArriveTick` like body flights. Hashed only once a ship has used a site.
- `StarSystemState`: `TravelTicks(FixVec2, FixVec2)`, `TravelTicksFor(ship, FixVec2, FixVec2)`, `SiteTravelTicksFor(ship,
  siteId)`, `SiteFlightSeconds(player, siteId)`, `SiteScanRange(player)` = `IdentifyRangeFor(player)`, `SiteNameFor`,
  `UnknownContactName`, `SiteKindOf(site)` (data), `KnownSites(player)`, `SiteWork(player, siteId)` → `SiteWorkState(Alongside,
  Powered, Logs, Boarded, Swept, Finds, Recalled)`, `SiteResearch`, `SiteLogs`, `SiteFinds`, `WorkActions`, `IsCrippled`,
  `AwayTeamPreview(player, crew)` and `(player, siteId, crew)`, `AwayResultSeconds` (30: how long a finished event stays in
  `AwayTeamOf`), `AwayRecalledSince`, hold item ids `PatchKitItem` "patch_kit", `MedicalItem` "medical", `SalvagePrefix`
  "salvage:", `SurvivorsPrefix` "survivors:". `ShipStatusView` gained `Repair` ("patch" | "dock" | ""), `RepairShare`,
  `RepairSecondsLeft`, `CrippledSecondsLeft`, `LogCount` (lines ever written: tells the feed which lines are new),
  `InjuredBack` (division, back tick). `SpaceMarker.SiteId` (Kind "site").
- `SiteReport` actions: "course", "scan", the kind's work actions (`alongside`, `restore_power`, `restore_power_kit`,
  `recover_logs`, `sweep`, `dock` at the station) and "away_team", each with its problem. "stop" stops running work.
  `SiteWorkCommand` "dock" = `ShipRepairCommand` "dock".
- `ShipRepairCommand`: "patch", "fix:<system>" (`fix:sensors`, `fix:engines`, `fix:tractor`, `fix:lifesupport`; also
  `fix:life_support`), "dock" (a station; a colony ship also at its colony's body). `DebugSpaceCommand` SystemDown ids
  take the same names. `ShipHoldCommand`: "sell" salvage at a station; "drop" survivors ("survivors" = every division)
  or salvage at a station or the landed colony's body (survivors join while the crew is short, else latinum; salvage
  gives its research); "jettison" salvage, patch kits, medical, and the fit-out's used-up space cargo (`probe`, `ssat`,
  `post`); "hire" (Item = division id) at a station.
- `AwayTeamCommand.Crew` is in `GameData.FitOut.Divisions` order; `CrewLine.Division` is the division id.
- Missions: conditions `space_site`, `hull`, `crew`, `hold`; actions `place_space_site`, `bind_site`, `damage`,
  `lose_crew`, `give_crew`, `hold_add` (`Missions/MissionSpaceJobs.cs`, vocabulary in `GameData.Missions.cs`; the existing
  surface `site` condition keeps its name, so sites are `space_site`). `MissionBook.PoolStates(player)`; `States(player)`
  lists only the game's chain. A pool job fails when the flagship is crippled or its site's away team is recalled.
- `Simulation.FitOutFactor` asks `StarSystemState.CrewFactor` first: while the crew on duty differs from the start, a
  fitted-out ship's levers use the crew on duty (a division below its floor counts as empty); a ship without a fit-out
  uses its suggested preset's factor with the crew on duty ÷ with its start crew (exactly 1 until someone is lost or
  injured). `DataChecks.SpaceSiteProblems` (in `All`).

**Rules as built.** Long-range scans (also from a site) detect sites within the scan range; arriving at a site detects
it; the station is Identified for everyone. A ship holding at a site is not in orbit (`OrbitProblem`, drop ships,
landings, sweeps, contact, observation, comms delay by distance all treat it as away) and not in flight; allies stay
at their body while the flagship is at a site; neutral traffic never uses sites; `FlagshipArrived` (achievements) only
for bodies. Leaving a site stops its work and a dock repair, resets "alongside" and recalls an away team. A sweep that
can't fit its next find (or the tractor goes down) stops and keeps its progress. Spent sites drift off `spent_seconds`
later unless a ship is at, flying to or from them; per-player records (and so mission conditions) remain. Survivors
always come aboard (people, even into a full hold) and join while the crew is below where it started. A crippled ship
recovers by itself; patch kits and the dock still work. The ship log gets lines for damage, crippling, systems down /
back, crew injured / lost / back / joined / hired, repairs, sales, identifications, work and away-team results.

**Hash.** `StarSystemState.AddToHash` → `AddSpaceJobsToHash`: `_nextSiteId`, every site, every flagship's status
(hull, crippled, systems, crew arrays, injured, hold, repair, log, hires, teams sent, scan / work / away runs, per-site
records). System games only, so planet-only games keep their hashes (checked against the base commit: the golden
scripted game, a two-player First Light game and a Dawn of Warp landing hash the same); every system game's hash moved
(sites are rolled). Ship fields and mission bindings / markers add their site parts only when used. Randomness: sites,
contents, names and away-team members from the system seed's hash; debris rolls, away outcomes and each member's fate
on `sim.Rng`.

**Tests.** `SpaceSiteTests` (9), `ShipStatusTests` (8), `AwayTeamTests` (5), `SpaceJobTests` (each job on all six
ships, twice, same hashes; pool listing; crippled fails), `SpaceJobFuzzTests` (3 system games × 9000 ticks of random
space commands, invariants, determinism); `CommandFuzzTests` kind 51 sends them to a planet-only game (refused).
Changed expectations: `StateHashTests` (`_nextSiteId`), `FitOutSpaceTests` (the two new space lines and levers),
`MissionTests` (side jobs counted per chain).

**Not done (later phases or open).** Asteroids, mining, ore, towing, moving sites, events (phase 2–3); chain missions
(phase 4); sites arriving during the match; a black box opening a follow-up job; native ships returned for trust;
standing from owners; declined or lapsed pool offers never come back (MissionBook keeps Declined); away-team members
stay counted on duty while they are out; colony ships can't carry patch kits or medical stores (space cargo lines);
achievements don't listen to space jobs yet.

## Phase 2: asteroids, mining, selling, towing (one agent: sim + view)

- **Asteroids** (`SiteKind.Asteroid`, variants common / metal / ice / hollow; weights in data; most placed near the
  system's belts, a few loose). Scan as other sites. Ore content by variant (common: duranium ore, little tritanium;
  metal: tritanium ore + duranium ore; ice: water ice → deuterium (`deuterium` resource); hollow: rock + a cavity with a
  cache or an old base, worked by an away team with its own table `hollow`). Hazard: rock falls while mining (chance
  per minute, hull 2–5, now and then the tractor knocked out).
- **Mining** (`SiteWorkCommand` "mine" at an identified asteroid; needs the tractor): 1 container of ore (30) every 30 s
  into the hold, × an Engineering lever `mining` and × the new space cargo `cutting_gear` (+25% each, max 2); stops when
  the hold is full or the asteroid is mined out (then Spent). Ore lines in the hold (`ShipHoldCommand`).
- **Selling / handing in:** at the Station ("sell": latinum per ore by kind, data), to a neutral freighter or tanker the
  flagship is with at the same body ("sell" with the ship as buyer; lower price) — add what's needed so the view can
  offer it; a colony ship drops ore at its colony's body into the colony's stock through storage ("drop"); a space ship
  can also drop it at a civilization that trades with it (if that exists in the phase-2 civ code; else leave it).
- **Towing** (`SiteWorkCommand` "tow" at a derelict, known derelict or debris cargo pod; needs the tractor): the towed
  site moves with the flagship (its position follows the ship); flights ×1.6 while towing; "drop" a tow at the Station
  (salvage value in latinum) or a colony ship's colony body (research + duranium); "stop" releases it where it is.
- **Pool jobs** (`data/missions/space_jobs.json`): at least three new ones, e.g. "Pay Dirt" (mine and sell N ore),
  "Ice Run" (bring ice/deuterium to the station or colony), "Tow Job" (tow a derelict to the station). Fix the phase 1
  gap: declined or lapsed pool offers come back after a cooldown (data).
- **View** (`game/scripts/`): asteroid glyph by variant, a tow line from the flagship to the towed site and the towed
  site following it on the plot, mining progress on the card, ore lines with SELL / DROP, a SELL TO <ship> button on a
  neutral freighter's card when the flagship is with it, the orbit view at an asteroid with a mining beam from the
  flagship and ore chunks drifting to it, and a towed model trailing the flagship in the site view. F9 SPACE: hold items
  for the ores, spawn asteroid variants (generic already?).
- Same rules as phase 1: planet-only and balance hashes unchanged, system games deterministic, refusals change nothing,
  fuzz covers new actions, `Validate()` / `DataChecks.All` empty, 919+ tests green. Fill in "Phase 2, filled in" below.

## Phase 2, filled in

Built by the phase 2 agent (2026-11-25), sim + view. Every number is a placeholder in data; `Validate()` and
`DataChecks.All` check all of it.

**Data.**
- `data/maps/sites.json`: `generation.asteroids` (count [3, 6], near_belt 75 %, belt_spread 25, spacing 40; rolled after
  the other sites on their own salt, so the phase 1 sites of every seed are exactly where they were: test
  `AsteroidsAreRolledAfterTheOtherSitesMostlyNearTheBelts` compares against a data copy with no asteroids); kind `asteroid`
  (A, scan 20 s, size 1–4, variants common 5 / metal 3 / ice 3 / hollow 1, hazardous, `mining` { seconds 30, `ore` per
  variant: containers [min, max] + ores (resource: weight): common 6–10 duranium_ore 4 / tritanium_ore 1, metal 5–8
  tritanium_ore 3 / duranium_ore 2, ice 6–10 deuterium, hollow 2–4 duranium_ore; `rockfall` 15 % a minute (rolled each
  second on `sim.Rng`), hull 2–5, 20 % of them knock the tractor out }, `alongside` 15 s (offered only where there is
  something to board), `away_tables` { hollow: hollow }, research 60–120); `tow` on `derelict` (latinum 50–90, research
  30–60, duranium 80–140) and `known_derelict` (40–70 / 10–30 / 100–160); salvage `cache_crate` (Supply cache, 45 latinum,
  10 research); `ore` { per_container 30, kinds duranium_ore 0.5 / tritanium_ore 0.8 / deuterium 0.6 latinum a unit,
  `traders` roles freighter + tanker, share 0.6 }; `tow` { flight 1.6 }.
- `data/maps/ship_status.json`: away table `hollow` (into the cavity / what's down there / what we bring back: the cache,
  the old base's records, the power cell; outcomes clean 65 (+ site research) / rockslide 20 / gas 10 / the old base woke 5).
- `data/missions/fit_out.json`: space lever `mining` (Engineering, more is better; `FitOutRules.Mining`, in `KnownLevers`
  and `SpaceLevers`) and space cargo `cutting_gear` (engineering, 1 container, max 2, `levers` { mining: 0.25 }). In no
  preset's base or fill, so every preset is as before.
- `data/missions/rules.json`: the pool chain `space_jobs` has `return_seconds` 600 (`MissionChainDef.ReturnSeconds`;
  only a pool may have it).
- `data/missions/space_jobs.json`: `pay_dirt` (ops; asteroid common/metal detected: identify → 60 ore in the hold →
  sell 60 ore; 30 latinum + 20 research), `ice_run` (control; ice asteroid: identify → 60 deuterium aboard → 60 sold or
  dropped; 40 latinum + a patch kit), `tow_job` (ops; a derelict or known derelict, identified, worked out or not: take it
  in tow → deliver it; 50 latinum + a patch kit). Vocabulary in `missions/notes.json`.

**Sim API added.**
- `Space/Mining.cs`: `AsteroidContainers(site)`, `AsteroidOre(site, k)` (the k-th container's ore, by the site's hash),
  `AsteroidOreLeft(player, site)`, `OreItem(ore)` ("ore:duranium_ore"), `MiningSeconds(player, mining)`, `Tally(player,
  prefix)`, `OreBuyers(player)` (neutral ships of a trader role in orbit of the flagship's body), `BuysOre(player, shipId)`,
  `OreValue(item, count, trader)`. Site work "mine": a `WorkRun` per container (`mining.seconds` ÷ the mining lever); at
  its end 30 ore of that container's kind go into the hold and the next run starts, until the next container doesn't
  fit (stops, "the hold is full") or the rock is mined out (spent; a hollow one only once its cavity is boarded too).
  The tractor going down stops it; towing blocks it; an away team out blocks it.
- `Space/Towing.cs`: `TowerOf(siteId)` (player, -1), `TowOf(player)`. Site work "tow" (alongside, tractor up, nobody else
  at / flying to / from it, not delivered, its reactor not blown; a worked-out wreck can still be come alongside and
  towed), "release" (wherever the ship is: the wreck stays where it is). The towed site's `Position` = its ship's
  `ShipPosition` each tick (`StepTows`, first in `StepSpaceJobs`); `SystemShip.FromPos` (hashed only when set) is where
  a flight began when it left a site in tow (that site moves with it). Flights × `tow.flight` while towing
  (`StatusFlightFactor`). Ends: tractor knocked out, crippled ("the tow line parted"), the reactor failing in tow (the ship
  takes its hull damage), dropped, released. Course to a towed site is refused; a towed site never drifts off.
- Hold: items `ore:<resource>` (counted in units, `ore.per_container` to a container, per kind) and `tow` (a line of 0
  containers while something is in tow; `StarSystemState.OrePrefix`, `TowItem`). `ShipHoldCommand`: "sell" ore at a
  station (full price), or away from a station to the first trader in orbit with the flagship; "sell:<ship id>" to that
  ship (share price; only ore); "drop" ore at the landed colony's body (what storage takes; the rest stays aboard) or at a
  trade partner's world (station price in latinum); "drop" tow at a station (latinum) or a colony ship's colony body
  (research + duranium through storage); "jettison" ore, or tow = release. Salvage sales are tallied too.
- Tally (hashed): keys "sold:<item>", "drop:<item>", "mined:<item>", "tow:station", "tow:colony".
- `SiteWorkState` gained `Mined`, `MinedOut`, `Towing`, `Towed`; `SiteReport` rows for asteroids (ORE, MINED, RATE,
  CAVITY), wrecks (SALVAGE / TOW); actions "mine", "tow", "release"; labels MINE, TAKE IN TOW, RELEASE TOW.
- Missions: `space_site` states `mined`, `towing`, `towed`; a trigger may list `kinds` / `variants` and set `spent: true`;
  condition `space_tally` { key or keys, count } (since the objective's stage began; nested in all/any from the start);
  `hold` item `ore` (every ore) or `ore:<resource>`. Pool offers that were declined or lapsed come back (Locked again,
  re-triggered) `return_seconds` after they ended (`PoolOffersReturn`). A trigger now skips only sites held by another
  *offered or active* job (one that ended lets its site go, so a boarded wreck can be towed in).
- `DataChecks.SpaceSiteProblems` also wants generation.asteroids.count[0] asteroids on seeds 1/1701/99999.

**Hash.** Planet-only games and the balance runner keep their hashes (checked against the base commit 7e7f296: the
golden scripted game, a two-player First Light game sampled every 40 s for 400 s, a Dawn of Warp landing sampled for 300 s,
and the balance runner's standard_opener on seeds 1/1701 at 15 min all hash the same). New state is hashed in system games
only: site records' `Mined` / `Towed` and the flagship's `Tow` / `TowTick` / tally only once used; `FromPos` only when set.
Every system game's hash moved (asteroids are rolled). Randomness: asteroid placement, contents and ores from the system
seed's hash; rock falls on `sim.Rng`.

**Tests.** `AsteroidTests` (7: rolled after the others near the belts, mining a container every 30 s until full or mined
out, rock falls + tractor, selling at the station and to a freighter, a colony ship dropping ore, a hollow asteroid boarded
and spent only once mined out, refusals change nothing), `TowTests` (3), `SpaceJobPhase2Tests` (each phase 2 job on all six
ships, twice, same hashes; a declined / lapsed offer comes back), `SpaceJobFuzzTests` now sends mine / tow / release,
"sell:<id>", ore and tow items (+ a towed-site invariant). Changed expectations: `SpaceSiteTests` (the site count leaves the
asteroids out), `SpaceJobTests.ThePoolIs…` (the pool lists the three new jobs), `FitOutSpaceTests` (the new space line
`cutting_gear` and lever `mining`). 949 tests.

**View.** `SystemPlot.Sites.cs`: asteroid glyph colour by variant (`AsteroidColour`: common amber, metal brass, ice pale
blue), hollow = the rock glyph with a hole (`GlyphKind.HollowRock`), a towed site drawn just behind its ship's chevron
(`TowedPlot`, the label says IN TOW) and a solid tow line from the ship (ours sky blue) in the same ImmediateMesh as the
course lines. `OrbitView.Mining.cs` (from `UpdateSiteView`, nodes made once per site scene): an amber cutting beam from the
flagship to the rock and 10 ore chunks drifting to the ship while it mines; a wreck in tow (held at another site, e.g. the
station) trails behind the ship on a blue tractor line, slowly tumbling. `PlanetDriver.Mining.cs`: a neutral freighter's /
tanker's ship card shows BUYS and SELL ORE TO <NAME> (`sys:sellore:<id>`, greyed unless in orbit with it). Status screen
hold: ore lines with SELL (tooltip with the price) / DROP / JETTISON, the tow line with DROP / RELEASE; the hold note says
how ore is sold. Site card tips for MINE / TAKE IN TOW / RELEASE TOW; mining progress comes through the existing WORK row
(`SiteWorkProgress` "Mining"). F9 SPACE: +30 duranium / tritanium / ice buttons, the tow line kept out of the item rows,
the variant pick's tooltip names the asteroid variants (spawning any asteroid variant was already generic).

**Not done / open.** A debris field's cargo pod can't be towed (pods come aboard as salvage; only derelicts and known
derelicts tow). Ore can't be dropped at the station (sell it there) and the deuterium a colony gets is ore-kind
`deuterium` straight into stock. No "how full is the rock" model change (the asteroid doesn't shrink). Traders buy every
ore at one share (tankers aren't deuterium-only). Asteroids don't move; rogue asteroids are phase 3. Space ships carry
`cutting_gear` only if picked on the fit-out page (no preset loads it).

## Phase 3: events and anomalies (one agent: sim + view)

- **Sites that arrive during the match** (the phase 1 "not done" item): after a calm start (data, ~5 min), a new event
  about every 6 min (data; deterministic from the system seed and the tick, never `sim.Rng` for when/what), at most a
  few live at once; each announced with an event line / ship log line and detected for every player (an event is a
  distress call or a sensor alert, not something you have to scan to notice; identify still needs a scan).
- **Rogue asteroid** (`SiteKind.RogueAsteroid`): moves along a straight course toward a body, impact at a known tick
  (shown as a countdown on the card and the plot: course line + impact point). Push it off course: hold alongside with
  the tractor for 60–120 s (Engineering lever `mining`/tractor strength or a new one), or an away team plants charges
  (faster, riskier: its own table). Success: reward (latinum, research; a civilization's world saved: awareness down,
  trust with that civ). Failure: impact; on a colony's body the surface takes damage (a few buildings / vehicles near a
  random spot lose condition, a bounded, deterministic list; never destroyed outright), elsewhere a log line.
- **Comet** (`SiteKind.Comet`): crosses the system on a curve/line over several minutes, then leaves. Meet it in its
  window and "sample" the tail for 30 s while it is in range (the ship has to keep up: hold at the comet = follow it):
  research + ice → deuterium ore in the hold. Light hull wear while sampling.
- **Lifepods** (`SiteKind.Lifepods`): a cluster of 6–12 pods round a point (usually by a debris field), each with its own
  air deadline (minutes). "pickup" a pod within range takes 5 s (tractor); people go into the hold (3 per container) and
  are dropped at the Station or a colony body (crew join while short, else latinum/standing). Pods whose air runs out
  are lost (log). The site is Spent when no pods remain.
- **Stranded ship** (`SiteKind.StrandedShip`): a neutral ship adrift; bring power (60 s alongside or a patch kit), then
  escort it to a body (it follows the flagship like an ally escort). Sometimes (data, ~20%) the distress call is bait:
  on arrival an ambush burst (hull damage, a system down, no combat yet) and the site is Spent.
- **Anomaly** (`SiteKind.Anomaly`, ion storm etc.; not yet built): hold position at a ring (outer / middle / inner) and
  "record": research by time × ring; inner ring hull −2% / 10 s and a chance to knock out sensors or engines; the storm
  drifts. Some at the start (like asteroids), some as events.
- **Pool jobs** in `data/missions/space_jobs.json` for each event: e.g. "Rogue Rock" side version, "Catch the Comet",
  "Twelve Lifepods" side version (the chain one is phase 4), "Dead Stick" (stranded ship), "Into the Storm" (anomaly).
- **View:** moving sites on the plot (smooth from `ShipPosition`-style interpolation), rogue course line + impact
  countdown, comet path + window, pod dots with air bars on the card, escort line for a stranded ship, anomaly rings on
  the plot and in the site view (`OrbitView.Sites.cs`: comet coma + tail, ion storm volume, pods round the lifepod
  model, the rogue rock with a push beam), event lines and an alert sound (`Sfx.Ui("error")` or similar) for a distress
  call. F9 SPACE: spawn each event now.
- Same rules as before: planet-only and balance hashes unchanged (check against the base commit), system games
  deterministic, refusals change nothing, fuzz covers new actions, `Validate()` / `DataChecks.All` empty, 949+ tests
  green. Fill in "Phase 3, filled in" below.

## Phase 3, filled in

Built by the phase 3 agent (2026-11-25), sim + view. Every number is a placeholder in data; `Validate()` and
`DataChecks.All` check all of it. Decisions given with the brief: keep it simple and data-driven; a rogue strike on a
colony body is bounded and deterministic and never destroys anything; a failed escort or a lost pod costs only the
reward plus a log line.

**Data.** A new part of the maps document, `data/maps/events.json` (manifest after `ship_status.json`): it adds to the
`space_sites` table (its `kinds` list is joined after sites.json's, `generation` gains `anomalies`, plus `events`) and one
away table (`charges`) to `ship_status`; notes in its `_notes_space_events`.
- `events` (`SpaceSiteRules.Events` = `SpaceEventRules`): calm_seconds 300, every_seconds [300, 420], max_live 3,
  edge [500, 560] (where moving things come in), weights rogue_asteroid 2 / comet 2 / lifepods 2 / stranded_ship 2 /
  anomaly 1. `generation.anomalies` (`AnomalyGenDef`): count [0, 1], drift 40 map units over drift_seconds 3600.
- Kinds (`SiteKindDef` gained `Rogue`, `Comet`, `Pods`, `Stranded`, `Anomaly`; records in `SpaceJobDefs.cs`, read in
  `GameData.SpaceJobs.cs`, checked in the new `GameData.SpaceEvents.cs`; `GameData.EventKinds`):
  `rogue_asteroid` (R, scan 15, variants stony / iron, hazardous, alongside 15 s + 20% for hull 1-3, away table
  `charges`, research 30-60; `rogue`: flight_seconds [360, 480] to its body, colony_chance 50, push_seconds [60, 120]
  (by the rock's hash ÷ the Engineering lever `mining`), veer 600 units over 90 s, reward latinum [20, 40] + research
  [40, 80], civ_awareness −10 (saved quietly) / civ_strike_awareness +25, impact { spread 6, radius 3, damage [20, 40],
  max_buildings 6, max_vehicles 4 }); `comet` (M, scan 15; `comet`: cross_seconds [360, 540] passing [120, 260] from
  the star, window [0.25, 0.8] of the crossing, sample 30 s, hull −1 every 10 s, research [60, 120], ice [1, 2]
  containers of deuterium); `lifepods` (P, scan 10, variants freighter / liner; `pods`: count [6, 12], survivors [1, 3]
  each, air [240, 600] s from the call, pickup 5 s each, near_debris 60); `stranded_ship` (T, scan 20, variants
  freighter / trader / science, alongside 20 s, restore_power 60 s or 1 patch kit; `stranded`: air [600, 900] s unless
  powered, escort_flight 1.2, bait 20%, ambush hull [10, 20] + one of sensors / engines / tractor down, reward latinum
  [30, 60] + research [10, 30]); `anomaly` ("Ion storm", I, scan 20; `anomaly`: every 10 s, research_cap 240, systems
  sensors / engines, life_seconds [900, 1500] and drift 60 for event storms; rings outer (radius 30, +2) / middle (20,
  +4, hull −1) / inner (10, +8, hull −2, knock 10%, hazardous)).
- `ship_status` away table `charges` (where / how much / firing; outcomes turned 60 `deflect`, split 20 `deflect` + hull 6
  + 1 injured, fizzle 20). `AwayOutcomeDef.Deflect`.
- `data/missions/space_jobs.json`: `rogue_rock` (control: identify → turn it aside; 40 latinum + 40 research),
  `catch_the_comet` (science: identify → sample the tail; 80 research), `pods_in_the_dark` (ops: identify → pick up 3
  pods; 30 latinum + medical stores), `dead_stick` (ops: identify → alongside + power → escort to its destination;
  40 latinum + 20 research), `into_the_storm` (science: identify → record 60 research; 60 research). Vocabulary in
  `missions/notes.json`.

**Sim API added** (`Space/SpaceEvents.cs`, hooks in `SpaceSites.cs`, `ShipStatus.cs`, `Towing.cs`, `AwayTeam.cs`,
`StarSystem.cs`; `Surface/Impacts.cs`).
- `SpaceSite` gained (internal set): `Event`, a straight path `PathFrom` / `PathTo` / `PathStart` / `PathEnd`
  (PathStart < 0 = it doesn't move), `TargetBody` (a rogue's target, a stranded ship's destination), `Outcome` ("" while
  it runs: deflected, impact, gone, cleared, rescued, trap, lost) / `OutcomeBy` / `OutcomeTick`, `AirTick` (a stranded
  ship's air, -1 once powered), `PodCount` / `PodsTaken` / `PodsLost` (bitmasks). Hashed in a "p3" block only for event
  kinds or moving sites.
- `StarSystemState`: `SitePosition(site, tick)` (a moving site's place at any tick; `ShipPosition` of a ship at or flying
  to a site now uses it, so the view can lerp), `EventOf(siteId)` → `SpaceEventView` (path, target, impact tick, the
  comet's window, air, pods, outcome), `PodsOf(siteId)` → `PodView(Index, Survivors, AirTick, State)`, `PodAirTick`,
  `TailWindow`, `EscortOf(player)`, `EscorterOf(siteId)`, `RecordingRing(player)`, `WorkActionOf` / `WorkSiteOf` (no
  allocation), `LiveEvents()`, `NextEventTick`, `EventNotes` (newest 20 `SpaceEventNote(Tick, SiteId, Kind, Text,
  Distress)`) + `EventNoteCount`. Hold item `EscortItem` "escort" (a line of 0 containers while escorting; `ShipHoldCommand`
  jettison = stop the escort). `SiteWorkState` gained `Pushed`, `Deflected`, `Sampled`, `Picked`, `Recorded`, `Escorting`,
  `Rescued`. `PlanetSurface.LastImpactTile` / `LastImpactTick` (hashed once set).
- Work actions (`SiteWorkCommand`): `push` (rogue: alongside, tractor up, no tow, no away team; progress kept per player
  like a sweep), `sample` (comet: only if the whole sample fits in the window), `pickup` (lifepods: the pod with the least
  air, one after another until none is left; tractor), `escort` (stranded ship, powered, nobody else escorting, no tow),
  `record_outer` / `record_middle` / `record_inner` (anomaly; the inner ring refuses a critical hull); `release` stops an
  escort too (label STOP ESCORT). Course to a moving event the ship can't reach in time is refused ("will be gone before
  we get there" / "would strike before we got there"). `DebugSpaceField.SpawnEvent` (Id = event kind, Value = variant
  or -1); `SpawnSite` / `place_space_site` of an event kind goes through `SpawnEvent`.
- Missions: `space_site` states `deflected`, `sampled`, `picked` {count}, `escorting`, `escorted`, `recorded` {count}
  (`count` added to the field list). A pool job also fails when a required objective of its stage can no longer come
  true (`StarSystemState.SiteStateLost`: struck / turned by another ship, the comet left or its tail went quiet, too few
  pods left, bait / crew lost / brought in by another ship, the storm dispersed); that costs only the reward.

**Rules as built.** The clock: `_nextEventTick` = calm + a gap by the system's hash, each turn picks the kind by the hash
of the turn and spawns it unless max_live events are running; gaps by the hash; never sim.Rng. An event is announced on
every player's ship log (and `EventNotes`) and is Detected for every player with a flagship; identifying it needs a
scan. Event placement uses its own salts (`EventSalt`, `AnomalySalt`), and the start anomalies are rolled after the
asteroids, so the phase 1-2 sites of a seed are exactly where they were (test). A rogue comes in from the edge straight at
its body; pushed aside or turned by charges it veers outward and leaves; unturned it strikes at `PathEnd`: on the colony
body (someone landed) `PlanetSurface.RogueImpact` picks a core and a spot within `spread` by the rock's seed, every
building within `radius` loses damage × (1 − d ÷ (radius + 1)) condition and every vehicle that integrity, the nearest
max_buildings / max_vehicles only, never below 1 (as the ruin defences: nothing is destroyed or broken by it); anywhere
else a log line; a living civilization's world it was heading for: awareness −10 when saved, +25 when struck. A comet
crosses on its path and leaves at the end; sampling inside the window gives research and ice (deuterium ore, what fits;
tallied as mined), once per player (Spent for that player only). Lifepods: one site, pods by index with their own air;
pods are shared (taken by whoever picks them up); air running out loses a pod (log); no pods left → cleared for all.
Survivors join the crew while it is short, else ride in the hold. A stranded ship's air stops when anyone restores its
power; escorted, it follows the flagship (flights × 1.2, also tow-like on the plot) and is rescued in orbit of its
destination (reward, then it leaves); its air running out = lost; a bait call (20% by its hash) springs an ambush on the
first ship to arrive (hull, a system down on sim.Rng) and is spent. Ion storms: recording runs until stopped, every 10 s
the ring's research (up to the cap: then Spent for that player), hull wear and knock roll (sim.Rng); event storms
disperse at their path's end. An ended event is Spent for everyone who knew it, their work there stops, an escort ends,
an away team there is recalled; it drifts off spent_seconds later (comets and storms at once, a rescued ship at once, a
turned rock at the end of its veer) unless a ship holds there.

**Hash.** Planet-only games and the balance runner keep their hashes (checked against the base commit 36c7e5e: the golden
scripted game, a two-player First Light game sampled every 40 s for 400 s, a Dawn of Warp landing sampled for 300 s, and
standard_opener on seeds 1 / 1701 at 15 min hash the same). New state is hashed in system games only: the event clock and
notes (`AddEventsToHash`), the site "p3" block, the per-player record "p3" block and the flagship's escort only once used,
the surface's last impact once there was one. Every system game's hash moved (start anomalies, the event clock).

**Tests.** `SpaceEventTests` (44: the clock (twice, same notes and hashes; first event after calm + a gap; Detected and
logged for everyone), phase 1-2 sites unchanged without the phase 3 data, rogue pushed aside (moves, ship goes along,
reward, veers off), charges (fizzle / turned), a colony strike (bounded, never below 1, nothing removed, twice the same),
comet window and leaving, lifepods (least air first, cleared, air running out), stranded ship rescued, escort as a hold
line, bait ambush, crew lost, storm rings (outer safe, inner wears), refusals change nothing (twin games), a job failing
when its rock strikes, and each of the five jobs on all six ships twice with the same hashes, played on a data copy whose
events come fast and of one kind (calm 5 s, every 20 s, no bait)); `SpaceJobFuzzTests` sends the new actions, the escort
item and SpawnEvent (+ invariants: escort consistent, pods taken xor lost, ended events leave). Changed expectations:
`SpaceSiteTests.SitesAreRolledFromTheSeedBetweenTheBodies` and `AsteroidTests.AsteroidsAreRolledAfterTheOtherSites…`
(the start anomalies are left out of the phase 1 count / compared lists, they come after the asteroids),
`SpaceJobTests.ThePoolIsOffered…` (the pool lists the five new jobs). `DataChecks.SpaceSiteProblems` also wants
generation.anomalies.count[0] storms and every weighted event kind placeable on seeds 1/1701/99999. 993 tests.

**View.** `SystemPlot.Events.cs`: moving sites drawn between ticks (`SitePosition` at this tick and the next, lerped by the
frame's alpha; ships holding at them follow), a rogue's dashed red course to its body with a cross on the impact point
(glyph colour `SiteDanger`), a comet's dashed path with its tail window in cyan (`GlyphKind.Comet`), an ion storm's three
rings at the data radii (inner dashed), a dashed escort line to the stranded ship drawn just behind its escorting ship
(as a tow), and label countdowns (IMPACT 3:12, TAIL IN 1:05 / TAIL ACTIVE 0:40 / LEAVING, 5 PODS · AIR 1:20, AIR 6:00 /
POWERED, DISPERSES 12:00, RECORDING INNER, ESCORTING). `PlanetDriver.SpaceEvents.cs`: a distress call (lifepods, a
stranded ship) plays the alarm twice, a sensor alert the tutorial chime (the ship log already writes the event lines);
card bars: TO IMPACT, TAIL, each waiting pod's air (least first, six at most), a stranded ship's AIR, RECORDED; tips for
the new buttons. `OrbitView.Events.cs` (the site view): a comet's coma and jets (and tail) bright while its window is open
and a cyan sampling beam; a blue push beam on a rogue rock (plus a faint dust trail in `SiteModels`); lifepods built with
the sim's PodCount, picked-up / lost pods hidden, the next pod drawn in along a tractor beam; a stranded ship's distress
beacon (blinks) and a red pulsing light until its power is back, then a steady warm one; an ion storm's plasma cloud and
lightning (more the deeper the ship records), the ship holding at the ring it records at. Status screen: the escort line
with STOP ESCORT. F9 SPACE: EVENTS (the next turn's countdown, events running, a button per kind).

**Not done / open.** "Trust" with a saved civilization (only awareness moves). Rogue rocks are pushed by the existing
`mining` lever (no tractor lever of its own). Survivors from lifepods are not tallied for missions (no "rescued N people"
condition); the side job counts pods. A stranded ship has no model of its own (a derelict model + a distress beacon).
Achievements don't hear events. Events don't avoid a crowded system beyond max_live and the placement tries. The phase 4
chain versions (e.g. "Twelve Lifepods") are not built.

## Phase 4: two new main missions per space chain (one agent: sim/data + the small view bits)

Each space chain goes from five main missions to seven; place the two where they fit the chain's story (not both at the
end) and keep every chain finishable on every fit-out preset and with no fit-out (tests play each chain twice, same
hashes). Missions place their own sites with `place_space_site` (bound, so the pool doesn't offer them) and never depend
on the random event clock.

- **Tereshkova · The Long Survey:** *Wreck at the Edge* (a debris field past the last orbit: sweep it, recover the
  black box; the box's log points to the next mission) and *Hollow Rock* (survey asteroids in a belt until one reads
  hollow, then send an away team in: the `hollow` table).
- **Armstrong · Priority Run:** *Twelve Lifepods* (a liner broke up: lifepods with air timers; the Armstrong's small hold
  means several trips to the Station; the clock is the story) and *Tow Line* (drag a disabled freighter to a body before
  its reactor fails: tow + timer).
- **Bastion · Watch Station:** *Rogue Rock* (a rogue asteroid on course for an inhabited body: push it aside in time;
  uses the phase 3 rogue rules, placed by the mission) and *The Smuggler's Belt* (new `SiteKind.HiddenShip`: a ship hiding
  in a belt; timed scans from different spots narrow it down, then come alongside and inspect; it may run once found,
  no combat).
- **Meridian · Envoy:** *The Probe That Talks* (new `SiteKind.AlienProbe`: it follows the flagship and sends a pattern;
  hold still near it to decode (Science), then answer with a three-step choice event like the away team; a good answer
  gives the later first contact less culture shock — a flag the contact code reads) and *Their Lost Ship* (a native
  ship adrift: tow it home for trust/awareness down, or study it for research with an incident risk).
- New kinds need: data in `sites.json` (chain-only: never rolled at start or by the event clock), work actions, plot glyph
  + card + site view (`probe_alien` model exists; the hidden ship can reuse a derelict model, darkened, with running
  lights), F9 SPACE spawn buttons.
- Achievements: let the tracker hear space jobs where it is cheap (e.g. a few events: first site identified, first away
  team home, first ore sold, a rogue turned) only if it fits the existing achievement data without new categories;
  otherwise leave it and say so.
- Same rules as before (planet-only and balance hashes unchanged against the base commit; system games deterministic;
  refusals change nothing; fuzz; `Validate()` / `DataChecks.All` empty; 993+ tests green). Fill in "Phase 4, filled in".

## Phase 4, filled in

(the agent writes this when done)
