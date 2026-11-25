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

(the agent writes this when done)
