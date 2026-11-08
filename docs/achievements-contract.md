# Achievements contract (2026-11-08)

Design doc: claude.ai/artifact/PCswr9SQMHrgBsYsYUptAk. Mockup: `docs/mockups/achievements/index.html`
(claude.ai/artifact/7XvGdwiVJmHT9JAZyTbKhX) — the UI follows it.

Owner's decisions: **cosmetic rewards** (one per achievement, picked in a HANGAR tab; never gameplay), hidden =
Unlucky, Holding Breath, SOS Answered, Never Left; gold/challenge times **fixed in the data**; **one profile per
machine, no reset button**; ids stay stable (Steam later).

Stubs (compile, do nothing yet): `game/sim/Core/Achievements.cs` (`AchievementUnlock`, `AchievementProgress`,
`PlayerCosmetics`, `AchievementTracker`, `Simulation.Achievements`, `Simulation.AchievementsEnabled`,
`Simulation.SetCosmetics`), `game/sim/Data/AchievementDefs.cs` (`AchievementData` and records,
`GameData.Achievements`), `game/scripts/Cosmetics.cs` (static picks the render code reads). Names and signatures are
fixed; add members, don't rename.

## Data (`data/achievements/`, a new sim document; manifest + `GameData.Documents`)

- `notes.json` `_notes`; `rules.json` `categories` (colony, exploration, surface, vehicles, hostile, comms, challenge,
  research: id, name, order, icon), `tiers` (bronze 10 / silver 25 / gold 50 points, colour "#cf8a52" / "#c3cedd" /
  "#f5a624"), `cosmetics` (id, kind, name, value, colour2, pattern); `<category>.json` = `{ "achievements": [ … ] }`.
- Achievement fields: `id`, `name`, `description`, `category`, `tier`, `icon` (line icon name: the mockup's set —
  uplink, ingot, alloy, crown, capitol, bolt, chart, satellite, route, planet, hail, hex, gem, base, drill, rover,
  tyre, wrench, fleet, blast, dome, lungs, weight, storm, signal, sos, bay, box, flask, anchor), `hidden`, `progress`,
  `reward` (cosmetic id), `trigger`, optional `worlds` (`classes`, `min_gravity_g`, `max_gravity_g`, `tier`).
- Trigger kinds: `milestone` (`milestone`, `before_seconds`, `without`), `built` (`building`, `count`), `count`
  (`measure`, `at_least` | `all`), `share` (`measure`, `at_least` 0..1), `unit_counter` (`counter`, `at_least`,
  `units`), `event` (`event` + fields), `streak` (`condition`, `seconds`, `from_start`), `set` (`measure`,
  `contains`), `view_count` (`measure`, `at_least` | `all`; checked by the view only), `all_of` / `any_of` (`rules`).
- `Validate()`: ids unique and lower_snake; category/tier/reward known; every building, unit, class, milestone,
  event, measure, condition name known; thresholds positive; seconds ≤ 14400; one nesting level; every cosmetic used
  by at most one achievement; hidden ⇒ any tier. A page `achievements.html` (`tools/gen_achievements_page.py`,
  README entry) like the other data pages.

## The 30 achievements (ids fixed) and their rewards

| id | tier | trigger | reward id → kind: value |
| --- | --- | --- | --- |
| first_light | bronze | milestone uplink (uplink online) | name_beacon → rover_name: BEACON |
| refined_taste | bronze | milestone first_metal | hull_10 → hull_number: 10 |
| parasteel_age | silver | milestone first_parasteel | paint_steel_blue → paint: #4f7fb8 solid |
| seat_of_government | silver | built colonial_administration | beam_violet → beam_colour: #b48cff |
| capital_idea | gold | built planetary_capital | finish_chrome → finish: chrome |
| lights_stay_on | silver | streak no_power_short 1800 s | paint_amber_band → paint: #f5a624 band, #1b2233 |
| uncharted_no_more | silver | count bodies_charted all | paint_survey_orange → paint: #ff8c3a solid |
| eyes_everywhere | silver | count system_satellites ≥ 4 | beam_cyan → beam_colour: #5ff2e6 |
| long_way_round | bronze | event orbit_farthest | name_long_haul → rover_name: LONG HAUL |
| rare_find | gold | event charted_class class_m | finish_blue_white → finish: blue_white |
| hailing_frequencies | bronze | view_count ships_inspected all | ship_hailer → dropship_name: HAILER 01 |
| ground_truth | silver | share surveyed ≥ 0.5 | paint_survey_green → paint: #5fbf7a stripes, #e6ebf5 |
| motherlode | bronze | event deposit_found amount ≥ 2500 | name_motherlode → rover_name: MOTHERLODE |
| second_base | silver | event founded | beam_green → beam_colour: #7dff9a |
| deep_roots | silver | event deposit_reopened | ship_taproot → dropship_name: TAPROOT 05 |
| kaltys_mileage | bronze | unit_counter tiles_driven ≥ 100 (survey rovers) | hull_44 → hull_number: 44 |
| pit_crew | bronze | event tyre_changed_field | paint_hazard → paint: #ffd23a chevrons, #111111 |
| limp_home | silver | event limped_home | name_limpet → rover_name: LIMPET |
| fleet | silver | count units ≥ 10 | hull_01 → hull_number: 1 |
| unlucky (hidden) | bronze | event crawler_explosion | name_scorch → rover_name: SCORCH |
| under_glass | silver | event domed_crewed_working | dome_amber → dome_tint: #ffb85c |
| holding_breath (hidden) | gold | event life_support_saved | finish_pearl → finish: pearl |
| heavy_world | gold | built colonial_administration, worlds min_gravity_g 1.3 | finish_gunmetal → finish: gunmetal |
| every_kind_of_weather | silver | set weather_seen ⊇ storm, blizzard, dust_storm | paint_storm_grey → paint: #8a93a6 band, #e6ebf5 |
| long_distance | bronze | event order_relayed delay_seconds ≥ 8 | ship_farcall → dropship_name: FARCALL 08 |
| sos_answered (hidden) | gold | event sos_answered within_seconds 60 | finish_rescue_white → finish: rescue_white |
| bay_by_eight | gold | milestone built:vehicle_bay before_seconds 510 | finish_brushed_gold → finish: brushed_gold |
| no_storage | silver | milestone first_metal without storage_container | paint_graphite → paint: #2b2f38 solid |
| never_left (hidden) | bronze | streak flagship_home 1800 s from_start | name_homebody → rover_name: HOMEBODY |
| scholar | silver | count techs_known all | beam_gold → beam_colour: #ffd27a |

Names/descriptions as in the design doc's table. Events: `orbit_farthest` (local flagship enters orbit of the body
farthest from the colony's body), `charted_class` (a body of that class becomes Charted), `deposit_found` (a deposit of
≥ amount becomes surveyed by the player), `founded` (a construction-rover outpost finishes founding), `deposit_reopened`,
`tyre_changed_field` (a ChangeTyre finishes off a repair pad), `limped_home` (a disabled rover arrives on its own core's
pad), `crawler_explosion` (breakdown severity explosion), `domed_crewed_working` (a crewed building working inside a
finished Pressure Dome's cover), `life_support_saved` (a base's life support went to 0 and recovered before the grace ran
out), `order_relayed` (a colony order arrives with ≥ delay), `sos_answered` (flagship back in orbit of the colony body
within N s of a breakdown/disabled report that happened while it was away). `uplink` and the other milestones mean what
the balance runner's milestones mean (move the shared detection into the sim if needed; the runner may keep its own).
Measures: `bodies_charted` (all = every body of the system; no system ⇒ never), `system_satellites`, `units` (own
units on the surface), `techs_known` (all = every surface tech), `surveyed` (share of map tiles). Conditions:
`no_power_short` (no own base with power efficiency < 1), `flagship_home` (flagship in orbit of the colony body).

## Sim (`game/sim/Core/Achievements.cs`, sim agent)

- Tracker checks every 15 ticks after `Surface.Step` (events are buffered as they happen and read there). Unlocks
  in `StateHash()` (per unlock: player, id, tick) **only when there is at least one** (planet-only games with none keep
  their hashes; the four balance orders' 12 final hashes on seeds 1/1701/99999 must not change, and the balance
  runner sets `AchievementsEnabled = false`). Per-player counters/streak state hashed too, under the same rule.
- Gate: `!AchievementsEnabled` → "turned off"; `DebugEdits > 0` → "test mode". (The game sets
  `AchievementsEnabled = false` itself when draft data is loaded.)
- Only real players (slots in `Players`); every player is tracked (each machine shows only its local player's).
- `SetCosmetics(player, c)`: names/numbers handed out first by `UnitNaming` (then the shuffled pool, skipping a pool
  entry equal to a used extra); hashed; refused (ignored) after tick 0. Tests: names, numbers, determinism, hash.
- Tests `AchievementTests`: each trigger kind, the gate, determinism, no unlocks in the balance runner, data checks.

## View (two agents)

- **UI agent** (`scripts/AchievementProfile.cs`, `AchievementToast.cs`, `AchievementScreen.cs`,
  `PlanetDriver.Achievements.cs`, `PlanetHud.Trophy.cs`; small hooks in PlanetDriver/PlanetHud/WorldChooser):
  profile `user://achievements.cfg` (earned id → date, world, seed, game seconds; unlocked/picked cosmetics; view
  counters), new unlocks of `LocalPlayer` not yet in the profile → record + toast + event line "Achievement: <name>";
  view-only rules (`ships_inspected`: system ship cards opened); toast per the mockup (4.5 s, gold 6 s, stack 3, hover
  holds, click opens screen, waits while F1 help / a large card / the chooser is open); trophy chip in the top bar
  ("9 / 30"), F3, Esc closes; screen per the mockup (summary, categories, filters, cards with progress from
  `Achievements.Progress`, hidden cards) + **HANGAR** tab (pick one per kind from unlocked cosmetics, or "standard";
  rover names/hull numbers: pick several, applied to the next new game via `SetCosmetics` in `StartGame`); sets
  `Cosmetics.*` from the picks at start and on change; `AchievementsEnabled = false` when draft data is loaded; a
  gate line on the screen when `Gate` is non-null. Medal icons drawn in code (`Icons.cs` style).
- **Cosmetics agent** (render-only; `RoverLook`, `BuildingModels` vehicle factories, `DropShipView`,
  `ConstructionView`, `PlanetView.Domes.cs`, `ModelThumbnails` if needed): local player's vehicles take
  `Cosmetics.Paint` (+ pattern via a small shader or second material on the Owner surfaces), `Finish` (material
  overrides: brushed gold, chrome, blue-white livery, gunmetal, pearl, rescue white), drop ships `DropshipName`,
  exocomp beams `BeamColour`, dome glass `DomeTint`; refresh when `Cosmetics.Version` changes; never other players'.
