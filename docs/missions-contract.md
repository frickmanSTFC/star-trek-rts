# Dome transits, drones, comms relays and missions: contract

Owner 2026-11-11: "when a vehicle leaves/enters a dome. it should pause and another field should appear over the
vehicle. When it comes in contact with the bigger shield, show some sort of interaction between the two fields.
i want to build upon our current scenarios. We need missions for us to do. Find things, setup communications, launch
drones, make drones, etc."

Owner's answers (2026-11-11): drones = **aerial scouts** (made in the Machine Shop from parts, launched from a Drone
Pad, fly over anything on a battery, survey, must stay inside comms coverage); communications = **satellite relays**
(coverage comes from relay satellites in orbit, not ground masts); missions = **story chain + side jobs** (Mission
Control sends one main mission at a time, plus up to 2 optional side jobs from finds and events); stakes = **only the
reward** (failing loses the reward; few timers).

Builds on the "Quests & Missions" design doc (2026-11-06, claude.ai/artifact/1QCXcqS5SHJPcAcyfVfz29: data-only quests
in the sim, conditions + actions vocabulary, stages, offers, rewards, COMMS card, objective tracker). Its open
questions take the doc's assumed answers unless this contract says otherwise. Every number is a placeholder.

Sim rules: `game/README.md` "Rules for sim code" (Fix64, no Dictionary iteration, commands only, everything hashed,
refuse never throw, determinism tests, `CommandHash` tags, fuzz test passing). `GameData.Validate()` and
`DataChecks.All` must stay empty. 574 tests pass at the start; keep them passing.

## Shared API (stubs committed with this contract)

Stubs return "nothing happens" values so every branch builds. The owning agent replaces the bodies; nobody changes a
signature without saying so in their report.

- `sim/Surface/DomeTransit.cs` (owner S1): `record struct DomeTransit(StructureId, Entering, Fraction, Contact,
  FromTile, ToTile, StartTick, EndTick)`; `PlanetSurface.DomeTransitOf(unit)`, `LastDomeTransitOf(unit)`.
- `sim/Surface/Drones.cs` (owner S1): `enum DroneState`, `record struct DroneInfo`; `IsAerial(unit)`, `DroneOf(unit)`,
  `DronesOf(padId)`, `DroneBays(structure)`, `LaunchProblem(sim, player, padId, tile)`.
- `sim/Surface/Comms.cs` (owner S1): `record struct CommsSource`, `enum CommsSourceKind`; `CommsCovered(player, tile)`,
  `CommsSources(player)`, `RelayProblem(sim, player, tile)`.
- `sim/Missions/MissionBook.cs` (owner S2): `Simulation.Missions` (`MissionBook`: `Enabled`, `For(player)`,
  `Messages(player)`, `Markers(player)`, `Sites(player)`) and the view records (`MissionView`, `ObjectiveView`,
  `MissionOption`, `MissionMarker`, `MissionMessage`, `MissionSite`, enums `MissionState`, `MissionKind`).
- Commands (`sim/Core/Commands.cs`, tags in `CommandChecks.cs`, shape checks done): `LaunchDroneCommand(Player,
  PadId, Tile)` 36, `RecallDroneCommand(Player, UnitIds)` 37, `RelaySatelliteCommand(Player, Tile)` 38 (owner S1);
  `MissionChoiceCommand(Player, MissionId, Option)` 39 (owner S2). Until a handler exists they are refused as unknown.
  Moving a flying drone = the existing `MoveUnitsCommand`.

## A. Dome transit (sim S1, view V1)

Only on worlds with the domes kit (`PlanetSurface.NeedsDomes`), only ground vehicles (not aerial, not drop ships).

Sim (S1, `sim/Surface/DomeTransit.cs` + the hook in `Lanes.cs MoveUnits`): when a vehicle is about to cross a hex edge
between a tile its owner has `Covered` and one it hasn't (either way), it stops at that edge's crossing point (where
the field shell stands: shells reach r + ½ hexes) and waits `domes.transit_seconds` (worlds.json domes block, 2.5)
before driving over. State on `SurfaceUnit` (hashed): ticks left, total, from/to tile, start tick, the structure
whose field it is (the covering dome/core nearest the edge), plus the last finished transit (for the after-glow). A
vehicle waiting behind it queues as for any vehicle in the way. A new order that still crosses the same edge keeps
the transit; one that doesn't cancels it; disabled / halted / teleported cancels it. Covered changing under a waiting
vehicle (dome finished or salvaged) re-checks at the end. On worlds without the kit nothing changes and nothing new is
hashed (class M hashes for the balance orders on seeds 1/1701/99999 stay as they are). Tests: enters and leaves a
core field and a Pressure Dome, pause length, queueing, cancel, determinism, hash, class M unchanged.

View (V1, render-only; `scripts/PlanetView.DomeTransit.cs` + `shaders/vehicle_field.gdshader` +
contact uniforms in `shaders/containment_field.gdshader` + small hooks in `PlanetView.Domes.cs`):
- The vehicle's own field: an ellipsoid bubble fitted to its model's box (+0.6), same visual family as the
  containment field (additive fresnel rim, fine hex lattice, soft ground contact) but its own tint (warm white-gold
  for ours, player colour hint), rising from the roof/ground over f 0–0.35 and flickering slightly as it locks.
- Contact (f 0.3–0.85): where the bubble meets the big shell, the shell shows ripples spreading from the contact point
  (rings travelling over the dome, fading with distance), an aperture "iris" glows open in the shell round the
  vehicle, short arcs crackle between the two fields, the colours exchange (bubble picks up the dome's blue at the
  seam). Up to 4 contacts per shell at once (uniform arrays).
- Through (f 0.85–1, then driving): the aperture holds while the vehicle passes; leaving, the bubble stays round it
  ~1.2 s outside then dissolves bottom-up; entering, it merges into the dome with a soft flash and the shell ripple
  closes. Uses `DomeTransitOf` / `LastDomeTransitOf`. Rivals' only while `ShowsLive`.
- Sounds if cheap with the existing kit (`docs/sound-contract.md`, `Sfx`): a rising field hum + crackle at contact.

## B. Drones, Drone Pad, relay satellites, comms coverage (sim S1, view V1, UI V2, models M)

Data (S1):
- Resource `scout_drone` (resources document, tier "drone", volume 3), made by the Machine Shop: recipe in
  `surfaces/fabrication.json` (1 structural_frame + 1 power_coupling + 2 control_module, 40 s).
- Unit `surfaces/units/scout_drone.json`: `aerial: true`, speed ~3× a Mk I rover, survey_radius 2, `drone` block:
  battery_seconds 180, reserve 0.2 (turns home when what's left only just covers the way back + margin),
  recharge_seconds 45, launch_seconds 3, land_seconds 3, loiter_seconds 12, `lost_seconds` 4; no damage/tyres/repair;
  not a Vehicle Bay product; description for the card.
- Building `drone_pad` (buildings document, `buildings/surface/drone_pad.json`, in build_menu after machine_shop): 120
  duranium + 40 tritanium, 40 s, power 4, `drone_bays` 2, not crewed, footprint_radius 6.5, access point, wear like a
  bay, description. Validate: bays > 0, a drone unit exists, the recipe item exists.
- Comms (`surfaces/orbit.json` or a new `surfaces/comms.json` part): core_radius 5 (each finished core's antenna),
  relay satellite: cost 120 duranium + 60 tritanium (from the orbit hold like satellites), max 3, radius 14 hexes,
  deploy_seconds 10, call signs (own list, e.g. RELAY-1 · BEACON…).

Sim (S1):
- Drones are `SurfaceUnit`s with DefId scout_drone. A finished pad loads a drone from stock into each empty bay
  (stock −1 → a docked unit on the pad tile; also when stock arrives later). `LaunchDroneCommand`: the pad's first
  charged (≥ reserve + a margin) docked drone launches to the tile; refused (LaunchProblem) when no pad / not yours /
  no charged drone / tile off map / tile not `CommsCovered` ("out of comms range: put a relay satellite over it") /
  battery can't reach it and back. Flight is a straight line (FixVec2), no lanes, ignores terrain, buildings, water,
  mountains and dome transits; every tile it enters is surveyed (Reveal + sight like a rover, survey_radius 2). At the
  target it loiters, then returns, lands in its bay and recharges. `MoveUnitsCommand` with flying drones retargets them
  (same checks, group = each its own target tile, no group spots); `RecallDroneCommand` sends them home. Battery
  drains while away; auto-return on reserve; a drone outside coverage (coverage changed) turns home; battery 0 = Lost
  (removed after lost_seconds; event; achievements may count it). Docked drones are skipped by lanes, rover wear,
  repair, breakdowns, people blockers, vehicle counts in the HUD; flying ones are never "in the way". Pad salvaged or
  destroyed: docked drones go back to stock, flying ones are lost when they get home to nothing (or land on another
  pad of the owner's with a free bay). Everything hashed; determinism test.
- Relay satellites: `RelaySatelliteCommand(player, tile)` puts a `Satellite` of a new kind Relay (survey ones keep
  sweeping; add `Kind` and `StationTile` to `Satellite`, hashed) that drifts to station over deploy_seconds then
  covers radius 14 round the tile (hex distance on the wrapped map). Survey and relay counts are separate maxima.
  `RelayProblem`: not landed / uplink offline, max reached, can't afford, off map. Retasking a relay moves its station
  (reuse `RetaskSatelliteCommand`, row/column from the tile, or a small extension: say which).
- `CommsCovered(player, tile)` = within core_radius of any finished core of the player's, or within radius of a ready
  relay. Cached per tick if needed (no Dictionary iteration in state logic). `CommsSources` for the view.
- Tests: fabricate → pad loads → launch → flies over a mountain/water → surveys → loiters → returns → recharges;
  every refusal; coverage refusal then a relay fixes it; battery lost; recall; retarget; salvage; determinism; hash;
  fuzz kinds for the three commands.

View (V1, render-only): drone models (`BuildingModels.Drone.cs`: `scout_drone.glb` or a primitive with the same part
names) flying at a cruise height above the ground (≈14, rising over terrain smoothly, never through mountains),
banking into turns, rotors spinning (faster on launch), nav lights + strobe, rising from / settling into its pad bay
(`Bay0..`), docked on the pad when charging (charger arm on, status light), lost = spirals down + small smoke puff;
Drone Pad model animation (`PlanetView.DronePad.cs`: door, chargers, pad lights by bay state, beacon); relay
satellite in the orbit view (`OrbitView.Relays.cs`: model `relay_satellite_prewarp.glb` / `relay_satellite.glb` by
era, parked over its station, drift on deploy, coverage disc on the globe, a faint link line to the ship marker);
comms coverage overlay on the surface (hex rim of the covered area per source, toggle from V2's key, and always while
a drone launch/move order is armed), drone target/loiter ring; mission sites (`MissionSite` Kind → models from
`mission_props.glb`) on found tiles and mission markers (rings of hexes: area / target / site; beacon on a unit).
Sounds if cheap: rotor buzz for the nearest flying drones (pooled like the exocomps).

UI (V2, commands only, each after the sim's own problem check, HudKit look):
- Drone Pad building panel: bays (drone call sign, battery bar, state), LAUNCH (arms a tile pick on the map with the
  coverage overlay and the LaunchProblem text in the tooltip), drones in stock / "make drones at a Machine Shop"
  hint. A selected drone uses the rover panel: battery, state, target, RETURN (key), right-click retargets.
- Machine Shop: the drone recipe shows like the kits (it already lists recipes from data).
- Helm: RELAY pill on the orbit action strip (with SATELLITE) → pick a tile on the globe, coverage preview, cost,
  RelayProblem; relay satellites in the SATELLITES window list with their station and coverage.
- A COMMS overlay toggle key (check free keys against the driver: L headlights, V wipers, P pause, R/T rover, F, M, U,
  X, B, I, O, J if missions take it…) and an F1 row.

Models (M, Blender, `blender/build_drone.py`, `build_drone_pad.py`, `build_relay_satellite.py`,
`build_mission_props.py`; `--game` exports to `game/models/...`, `--preview` renders to `blender/previews/...`):
- `models/drones/scout_drone.glb`: Dawn of Warp Earth-built quad tilt-rotor scout (~2.6 across, white + foil + Shuttle
  orange like the pre-warp drop ships), parts `Body`, `Arm0..3` (rest = deployed), `Rotor0..3` (spin about local Y),
  `Camera` (gimbal, looks along −Y = down), `Lens` ("Lens"), `Beacon` ("Beacon"), `NavL` red / `NavR` green ("Nav"),
  `Strobe` ("Strobe"), `Skid` legs, `Owner` band, decal markers `NameL/NameR` (call sign).
- `models/buildings/drone_pad.glb`: pad r ≈ 6.5 with two landing circles (`Bay0`, `Bay1` empties at the pad
  centres, local +Y up), charger arms `Charger0/1` (swing over a docked drone), small hangar/ops box with a roll door
  `Door` (scale Y like the Machine Shop), antenna mast with `Dish`, `Beacon`, "PadLight" ring lights per bay
  (separate meshes `PadLight0/1`), "Status" board, `Build0..N` pieces, `SignFront`, `AddOnSpot0..4`, `RoofSpot0..1`,
  one grime atlas, Owner bands (the building kit conventions).
- `models/orbit/relay_satellite_prewarp.glb` (+ `relay_satellite.glb` for later eras): big parabolic `Dish`, feed
  `Horn`, `Body`, `PanelL`/`PanelR` (same fold convention as the survey satellite), `Beacon`, `Thruster0..3`, `Link`
  (material "Link", emissive, the game pulses it), Owner band; sized like the survey satellite for the orbit view.
- `models/missions/mission_props.glb`: `probe_wreck` (a crashed Earth probe decades old: half-buried capsule, torn
  chute, scorched ground, `Beacon` blinking weakly), `signal_source` (an unknown small transmitter half sunk in rock,
  `Glow`), `photo_point` (a survey tripod), `survey_marker` (a planted flag + marker), each within r ≈ 5 on its
  origin, sinking skirt; far stand-ins optional.
- Previews + a contact sheet. Commit often.

## C. Missions (sim S2, UI V2)

Engine (S2, `sim/Missions/`, data `data/missions/` = a new optional document like achievements; manifest entries):
- The design doc's model, trimmed: per player, per mission: state, stage, stage start, objective progress (Fix64,
  only up unless hold_seconds), bindings (tiles, unit ids, site ids), timer end, plus flags; all hashed (only once
  enabled, like Achievements, so non-campaign hashes don't move). Stepped once a second (every 15 ticks) after
  `Surface.Step`, players in order, missions in file order. Randomness = hashes of map seed + player + mission (not
  `sim.Rng`). Messages stored as (tick, giver, text with bindings filled at the time). `MissionChoiceCommand` for
  accept / decline / branch (refused when not offered / unknown option / not yours). `DebugMissionCommand` for the
  test panel (start, goto stage, complete objective, complete, fail, reset; counted in DebugEdits): new tag 119.
- Enabled at set-up: `MatchSetup` / `Simulation` gets a way to turn missions on with a chain id before tick 0 (e.g.
  `sim.Missions.Enable("dawn_of_warp")` — say what you pick); the game calls it for a campaign start. Off by default.
- Conditions (enough for the content below; each a small class): resource, building (id, count, state, base),
  unit (id, count, state incl. drone states), surveyed (count, tile/area), deposits_found (distinct resources, rich),
  site (found / reached by a ground unit for hold_seconds / visited by a drone loitering), drone_launched,
  drone_at (tile/area, hold), relay (count ready, covers tile/site/base), covered (tile/site by comms), fabricated
  (item, count since start), outpost (count of finished cores), weather (forecast storm near a base), time, flag,
  mission state, all/any/not. Actions: message, grant (stock through Store; never power), research points, reveal
  (tile, radius), place_site (a site kind on a hashed tile matching rules: distance from the hub, terrain, passable or
  not, unsurveyed), marker, set_flag, start_mission, complete, fail.
- Sites: `sim/Surface/MissionSites.cs` (partial PlanetSurface, or inside the book): placed by actions, Found when
  surveyed or seen; hashed.
- Checks in `Validate()`: ids exist (resources, buildings, units, items, techs), stages reachable, bindings bound
  before use, rewards never energy, every mission can complete.
- Tests: each main mission completes along a scripted path on a test world (golden path), offers/decline/accept,
  side limits (2), timers, rewards paid once, determinism + hash, off = no hash change, refusals, fuzz kind.

Content (S2, `data/missions/`; English text inline; givers are original placeholders: **Flight Director Moreau**
(Earth Mission Control, giver "control"), **Chief Okafor** (outpost ops, "ops"), **Dr. Reyes** (science,
"science")). Dawn of Warp main chain "First Light", one at a time, each starting when the last completes:
1. **Ground Truth** (starts when a Research Lab is placed or 10 min after the uplink, so the tutorial runs first):
   survey 60 tiles; find 3 different resources. Reward: research 30.
2. **The Lost Pathfinder** (find things): an uncrewed Earth probe came down here decades ago; a search area marker
   (radius 4) round a hidden `probe_wreck` site 10–18 tiles from the hub on passable land. Find it (survey its tile),
   then park a rover beside it for 20 s (recover the data recorder). Reward: research 60, 2 control modules, reveal 6.
3. **Calling Home** (set up communications): the recorder's data has to go home: deploy a relay satellite whose
   coverage includes the base, then bring the wreck site under coverage (the same relay or another). Reward: research
   40 + flag `earth_link`.
4. **Wings** (make drones): build a Machine Shop (skip if built) and a Drone Pad; fabricate 2 scout drones. Reward:
   2 power couplings, research 20.
5. **First Flight** (launch drones): launch a drone; three `photo_point` sites beyond the nearest mountains or water
   (inside coverage or reachable once a relay covers them): a drone loiters at each. Reward: research 50, reveal.
6. **Beyond the Ridge** (find things with drones): a faint signal: drones visit 2 of 3 bearing points (markers), then
   the `signal_source` site appears in a small area; find it and reach it with a rover. Reward: research 100.
7. **Foothold**: found a second outpost (construction rover or outpost drop) inside comms coverage. Chapter debrief.
Side jobs (offered when triggered, accept / decline, at most 2 active, offers expire after 90 s, no fail costs):
**Rich Vein** (a rich deposit surveyed → mine or crawler on it, store 300 from it), **Storm Watch** (a storm forecast
near a base → a rover parked outside cover during it for 60 s, or any rover in the open on a world without domes),
**Lost Signal** (a ruins region hint exists → a drone loiters over the hint area → the ruin revealed), **Sky Survey**
(satellites have covered 50% of the map's tiles → research), **Rescue** (a rover disabled more than 6 tiles from a
pad → another vehicle reaches it or a drone loiters over it within 5 min). Rewards small (≈ a minute or two of income).

UI (V2, render-only + commands; HudKit look; the Quests doc's Presentation section, trimmed):
- Objective tracker top right under the resource bar: the main mission + active sides (title, stage, objectives with
  progress "34 / 60", timer, a hint line), click = camera to its marker; collapses to a chip.
- Incoming transmission: banner + event-feed line; a comms card (giver name + portrait placeholder initials, title,
  briefing, objectives, ACCEPT / DECLINE for side offers, choice buttons, CLOSE) that shows on the surface and over
  the helm in orbit. Key to reopen the last message (check free keys).
- Mission log (key J if free): active / completed / failed / declined, briefings and debriefs to re-read.
- Debrief card on completion (rewards paid, a short log line; the chapter end a larger one).
- Test panel MISSIONS tab (debug builds): every mission's state; start / goto stage / complete objective / complete /
  fail / reset via `DebugMissionCommand`; a switch that turns missions on in a sandbox game (via a debug command or
  set-up, say which).
- Tutorial: the tutorial card and the tracker must not overlap; the tracker stays hidden until the tutorial is done
  or skipped (or show compact below it).
- Main menu / campaign start: `GameSetup` campaign → missions on (one line in the set-up path; tell the merger).
- Drone + relay UI from B above.

## Agents and files (disjoint; the merger does the remaining one-line hooks)

- **S1 sim world**: `sim/Surface/DomeTransit.cs`, `Drones.cs`, `Comms.cs`, edits in `Lanes.cs`, `Satellites.cs`,
  `SurfaceUnit.cs`, `PlanetSurface.cs` (Step hooks, Apply cases for its commands), `RoverDamage.cs`/`Visibility.cs`
  skips, defs + loaders in `sim/Data/` (new `GameData.Drones.cs`), data files above, `manifest.json`, tests
  `DomeTransitTests`, `DroneTests`, `CommsTests`, fuzz kinds.
- **S2 missions**: `sim/Missions/*`, `sim/Surface/MissionSites.cs`, `sim/Data/MissionDefs.cs` + `GameData.Missions.cs`,
  `data/missions/*`, `manifest.json` (its own lines), `Simulation.cs` hooks (step, hash, Apply case, enable),
  `DebugCommands.cs` (DebugMissionCommand), tests `MissionTests`. Writes conditions against the S1 stubs' signatures
  (drone states, relays, coverage) so it builds before S1 merges.
- **V1 world view**: `scripts/PlanetView.DomeTransit.cs`, `PlanetView.Drones.cs`, `PlanetView.DronePad.cs`,
  `PlanetView.Comms.cs` (overlay), `PlanetView.Missions.cs` (sites + markers), `BuildingModels.Drone.cs` (drone,
  pad, mission props, relay satellite model factory), `OrbitView.Relays.cs`, `shaders/vehicle_field.gdshader`,
  `shaders/comms_overlay.gdshader` if needed, edits in `PlanetView.Domes.cs`, `containment_field.gdshader`,
  dispatch lines in `BuildingModels.BuildModel`, `PlanetView.UpdateRovers` (skip docked drones / aerial pose).
- **V2 UI**: `scripts/MissionTracker.cs`, `MissionComms.cs`, `MissionLog.cs`, `PlanetDriver.Missions.cs`,
  `PlanetDriver.Drones.cs`, `PlanetDriver.Relays.cs`, `TestPanel.Missions.cs`, edits in `HelmScreen.cs` (RELAY pill),
  `SatelliteCard.cs` (relay rows), `PlanetDriver.Tutorial.cs` (tracker vs tutorial), F1 rows.
- **M models**: `blender/build_drone.py`, `build_drone_pad.py`, `build_relay_satellite.py`, `build_mission_props.py`,
  their glbs and previews, `blender/drones.md` (notes).

Each agent: work in its worktree, build + test (`cd game && dotnet build FinalFrontier.sln && dotnet test`), commit
often (a container restart loses uncommitted work), and end with a report: what was built, files, API changes, data
numbers, what can't be checked here (Godot can't run here; shaders can't be compiled: re-read them), and the
CLAUDE.md paragraph text for the merger.
