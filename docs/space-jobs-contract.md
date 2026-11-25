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

(the SIM agent writes this when done: every added member, data file and number, the hash rules, what isn't done)
