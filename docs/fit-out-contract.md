# Fit Out Your Ship: contract (phase 1, the two colony ships)

Owner 2026-11-17: "You should decide if you are an explorer, a builder, a combatant, etc. Based on this, you can set the
crewing/supplies of your ship before you leave home-base. You need to have some of each type of support, but you can go
more with one type over another." Design page `docs/mockups/loadout/index.html` (claude.ai/artifact/HiBpPTNV4PFkcGYjus1m6Q).
Owner on its eight questions: "go with your recommendations, build it for the colony ships".

## Decided

1. The step sits after Choose Your Ship and before the system pick.
2. The focus is the same pick as the officer's department (one pick; the officer screens are still only a mockup).
3. Departments are Exploration, Engineering, Science, Defense (Maintenance and Archaeology are renamed).
4. Five focuses: Explorer, Builder, Combatant, Scientist, All-rounder.
5. Combatant is shown now, small as it is, with a note that it grows with combat.
6. Any focus on any ship; each ship only suggests one.
7. Strength: a division's levers move 0.4% per point its crew share is above or below 25% (about +8% on a preset, +18%
   at most, -6% at the 10% minimum). Placeholder.
8. Cargo is chosen line by line.

Phase 1 is **Horizon and Cochrane only**. The four ships that never land skip the page and play as today.

## Rules (all numbers in data, all placeholders)

- Crew: the ship's crew (`ShipChoiceDef.Crew`'s number: Horizon 120, Cochrane 210) is split in heads over the four
  divisions. Each division holds at least 10%, the focus's own division at least 30%, and everyone must be assigned.
- A division's strength = 0.004 x (share in percent - 25). Each lever belongs to one division and is multiplied by
  (1 + strength) when more is better and (1 - strength) when less is better.
- A focus gives a crew preset, a cargo preset and one fixed perk.
- Hold: counted in containers, size per ship (Horizon 30, Cochrane 24). Each cargo line has a cost in containers, a
  minimum and a maximum; each cargo group (exploration, engineering, science, defense, ship) has a floor. The hold need not
  be full.
- **No fit-out = today's game exactly.** Tests, the sandbox, the balance runner and the four space ships pass none, and
  every existing hash stays the same. All-rounder with an even split has strength 0 everywhere, but its cargo is a real
  choice, so only "no fit-out" is promised to be identical.
- With a fit-out, the landing stock's duranium, tritanium and deuterium come from the cargo (Building materials: 50
  duranium + 30 tritanium a container; Fuel: 25 deuterium a container) instead of `landing.start_stock`; everything else in
  the start stock stays, and the ship's own `start_bonus` / `start_units` are added as today.

## Levers (colony ships)

| lever id | division | moves |
|---|---|---|
| `drive_speed` | exploration | ground vehicle speed (`PlanetSurface.SpeedPerTick`) |
| `satellite_sweep` | exploration | survey satellite sweep time |
| `drone_battery` | exploration | scout drone battery |
| `rover_hazard` | exploration | rover hazard and flat-tyre chance |
| `build_time` | engineering | build and upgrade time |
| `repair_speed` | engineering | repair and maintenance speed |
| `fabrication` | engineering | Machine Shop and hub workshop speed |
| `wear` | engineering | building wear |
| `research_rate` | science | Research Lab points a minute |
| `dig_time` | science | excavation time per layer |
| `sickness` | science | how long a pathogen keeps the crew sick |
| `wildlife` | defense | wildlife attack rate on vehicles |
| `ruin_defence` | defense | damage when a dig wakes a defence system |
| `crew_loss` | defense | crew lost a minute when life support fails |

Must be in phase 1: `drive_speed`, `satellite_sweep`, `build_time`, `repair_speed`, `research_rate`, and at least
`wildlife` and `ruin_defence` so Defense does something. The rest go in if each is a one-line multiplier where gravity,
weather or research already multiply; say which were left out.

Perks: Explorer = the landing reveals 5 hexes round the hub instead of 3. Builder = the hub workshop makes kits 30% faster.
Combatant = vehicles take half damage from wildlife and ruin defences. Scientist = research from mission rewards +20%.
All-rounder = none.

## Cargo lines (colony ships)

| id | group | containers | min | max | gives |
|---|---|---|---|---|---|
| `mat` Building materials | engineering | 1 | 6 | 20 | 50 duranium + 30 tritanium |
| `kits` Spare kit set | engineering | 1 | 0 | 6 | one of each of the four spare kits |
| `crawler` Extra mining crawler | engineering | 4 | 0 | 1 | a `mining_crawler` landing with the colony |
| `rover` Extra survey rover | exploration | 3 | 0 | 2 | a `survey_rover` landing with the colony |
| `drone` Scout drone | exploration | 1 | 0 | 4 | a `scout_drone` item in the landing stock |
| `sat` Survey satellite | exploration | 2 | 0 | 3 | that many satellites launch without paying |
| `lab` Lab equipment | science | 1 | 0 | 6 | 20 research points banked toward the first tech |
| `med` Medical stores | science | 1 | 0 | 4 | crew sickness 8% shorter each |
| `armour` Field armour | defense | 1 | 0 | 4 | vehicles take 5% less hazard and wildlife damage each |
| `fuel` Fuel | ship | 1 | 4 | 12 | 25 deuterium |
| `prov` Provisions | ship | 1 | 0 | 8 | 5 more crew (colonists awake) at landing |

Group floors: exploration 2, engineering 8, science 2, defense 2, ship 4 containers. Must be in phase 1: `mat`, `fuel`,
`kits`, `prov`, `rover`, `crawler`, `drone`, `lab`. `sat`, `med`, `armour` need a little new sim code: build them if
small, otherwise leave the line out of the data and lower that group's floor so the rules still hold (the UI shows only the
lines the data has). Extra vehicles arrive the way a ship's `start_units` do.

## Shared API (names fixed; stubs go on main before the two agents start)

- Data: `data/missions/fit_out.json` in the missions document (optional: absent = the feature is off). Rules, divisions,
  focuses (id, name, division or null, one-line text, crew percentages, perk), levers, cargo groups with floors, cargo lines,
  per ship: hold, suggested focus, the base load every preset starts from, and each focus's fill order.
- `game/sim/Data/FitOutDefs.cs`: the records and `GameData.FitOut` (null when absent), validated (ids unique and known,
  floors and budgets consistent, every preset legal on every listed ship, resources and units exist).
- `game/sim/Core/FitOut.cs`:
  - `record FitOut(string Focus, int[] Crew, IReadOnlyDictionary<string, int> Cargo)` (Crew in the data's division order).
  - `FitOutRules.Preset(data, shipId, focusId)` → a legal `FitOut`; `FitOutRules.Problem(data, shipId, fitOut)` → null or a
    plain sentence; `FitOutRules.Strength(data, shipId, fitOut, divisionId)`; `FitOutRules.Factor(data, shipId, fitOut,
    leverId)` (the multiplier, perks and cargo included; 1 with no fit-out); `FitOutRules.LandingStock(...)` /
    `LandingUnits(...)` for the UI's summary. Pure functions, used by both the sim and the menu so they agree.
  - `Simulation.FitOutOf(player)` (null = none), hashed only when set.
- `MatchSetup.CreateSystemGame(data, seed, faction, homeBody, shipId, fitOut)`; a fit-out that fails `Problem` is replaced
  by the ship's suggested preset. The 5-argument overload is unchanged.
- `GameSetup.FitOut` and a `StartCampaign(..., shipId, chain, fitOut)` overload (`game/scripts/WorldChooser.cs`).

## SIM agent

Data file, records, validation, `FitOutRules`, the multipliers at the lever sites, the perks, the landing stock and units,
hashing, tests (no fit-out = same hashes for the balance orders and ship chains; floors and budgets refuse bad fit-outs;
every lever moves the right way by the data's amount; same fit-out twice = same hashes; each focus preset on each colony
ship still lands and reaches its first base).

## UI agent

`game/scripts/MainMenu.FitOut.cs`: the page between Choose Your Ship and the system pick, for colony ships only, laid out
as the mock (focus + crew, cargo, summary) and fitting 1280x720 (cargo descriptions as hover text, the summary scrolls);
focus tiles load presets; sliders and steppers hold the floors and the budget; BACK / RESET TO PRESET / LAUNCH (off while
crew are unassigned); the summary is computed with `FitOutRules`. In game: the flagship's card and the pause card show the
focus and the division percentages. `PlanetDriver.StartGame` passes `GameSetup.FitOut` to the 6-argument
`CreateSystemGame`.

## Sim side, filled in (2026-11-17)

Data `data/missions/fit_out.json` (the missions document's `fit_out`; its `_notes_fit_out` explains every field), read
and checked in `game/sim/Data/GameData.FitOut.cs`, shapes in `FitOutDefs.cs`, rules in `game/sim/Core/FitOut.cs`, the
surface's helpers in `game/sim/Surface/PlanetSurface.FitOut.cs`, tests `FitOutTests`.

- **Everything in the two tables above is in**: all 14 crew levers, the four perks and all 11 cargo lines (`sat`, `med`
  and `armour` included), with the contract's floors. Nothing was left out of the data.
- **Five more lever ids, with no division** (only perks and cargo move them; `FitOutLever.Division` is `""`):
  `hazard_damage` and `wildlife_damage` (Field armour, -5% an item; the Combatant halves `wildlife_damage`),
  `ruin_vehicle_damage` (the Combatant: x 0.5), `hub_workshop` (the Builder: x 1.3, on top of `fabrication`),
  `mission_research` (the Scientist: x 1.2). `FitOutRules.KnownLevers` lists all 19; the data may only list those.
- **Added to the records** (init properties, the positional signatures are unchanged): `FitOutFocus.PerkLevers`,
  `.LandingReveal`; `FitOutCargoLine.Stock`, `.Unit`, `.Satellites`, `.Research`, `.Levers`; `FitOutShip.Base`,
  `.Fill`; `FitOutData.ReplacesStartStock`, `.Lever(id)`, `.Line(id)`, `.Group(id)`, `.DivisionIndex(id)`,
  `.EvenSharePct`.
- **Added to `FitOutRules`**: `CargoCount(fitOut, line)`, `ContainersUsed(data, fitOut, group)`, `LandingReveal`,
  `FreeSatellites`, `ResearchBanked`, `CargoUnits`, `CargoStock`, `KnownLevers`, the lever id constants.
  `Factor` = crew strength x the focus's perk multiplier x (1 + the cargo's per-item amount x count), never below 0.05.
- **Added to `Simulation`**: `FitOutFreeSatellites(player)` (ready satellites left), `FitOutLandingReveal(player)`,
  `FitOutUnits(player)`. `FitOutOf(player)` returns the game's own copy, whose `Cargo` holds only the lines above 0:
  read counts with `FitOutRules.CargoCount`.
- **Added to `PlanetSurface`**: `BuildTicksOf(buildingId, owner)` and `LevelTicksOf(level, owner)` (with the owner's
  build_time lever; the one-argument forms are the surface's own and unchanged, so a build-time readout for the player
  should call the two-argument ones). `SpeedPerTick`, `WearPerMinute`, `ResearchPerMinute`, `SecondsAt`, `DigProgress`
  already include the owner's fit-out. A fitted-out player's satellites lap their band in sweep_seconds x
  `FitOutFactor(satellite_sweep)` and their drones' batteries last battery_seconds x `FitOutFactor(drone_battery)`.
- **`CreateSystemGame(..., shipId, fitOut)`**: null, no ship, or a ship the data doesn't list (the four that never land)
  = the 5-argument game. Otherwise a fit-out `Problem` refuses is replaced by the ship's suggested preset; the landing
  stock's duranium, tritanium and deuterium are set from the cargo, the cargo's other stock is added, then the ship's
  own start bonus as always; lab equipment goes into the research bank (paid into the first tech started); extra
  vehicles land after the ship's start units, each in its own convoy drop ship.
- **No fit-out = the game as it was**: with none, `FitOutFactor` returns exactly 1 at every site, nothing is stored and
  nothing is added to the hash. Checked three ways: the 761 earlier tests pass unchanged; `FitOutTests` compares the
  5-argument and 6-argument games tick for tick (no ship, both colony ships, two space ships); and, once, the base
  commit and this branch were built side by side and gave the same final hashes for the balance runner (3 orders x 3
  seeds x 30 min, Veyra included) and for 42 scripted system games (2 data set-ups x 3 seeds x no ship and all six
  ships, 12 min each with landings, building, research, satellites, driving and flights).
- **Mission reach check**: `GameData.Validate()` now also walks each colony chain with the leanest stock a fit-out can
  land with (every cargo line at its minimum: 300 duranium, 180 tritanium, 100 deuterium; `LeanestFitOutStock()`),
  reported as "With the leanest fit-out …". It passes: those three can all be mined.
- **Open**: a hold full of building materials (15 or more on the Horizon) lands with more than the hub's 1500 vault
  holds; nothing is lost, but income waits until some is spent. The crew_loss lever only matters on Domes worlds.

## Phase 2: the four ships that never land (owner 2026-11-17: "do the four space ships next")

Tereshkova, Armstrong, Bastion and Meridian get the same page and rules. Everything below follows the design page's
"Space" section and its levers table (cargo on a ship that never lands); numbers are placeholders.

**Ships** (crew from `ShipChoiceDef.Crew`; hold; suggested focus): Tereshkova 40 / 12 / Explorer; Armstrong 6 / 6 /
Builder; Bastion 85 / 14 / Combatant; Meridian 350 / 20 / Scientist.

**Crew levers** (same 0.4% per point, same 10% / 30% floors):

| lever id | division | moves |
|---|---|---|
| `scan_cooldown` | exploration | long-range scan recharge (`system.scan.cooldown_seconds`, 90 s) |
| `sweep_time` | exploration | the orbital sweep of a new body (15 s) |
| `flight_time` | engineering | flight time between bodies (`TravelTicksFor`) |
| `study_time` | science | time to study a civilization (`StudyOf`) |
| `shock_chance` | science | culture-shock chance at first contact (`ShockChance`) |
| `alongside_time` | defense | time alongside a ship for `with_ship` objectives (`hold_seconds`) |

**Perks in space:** Explorer = long-range scans reach 400 units instead of 320; Builder = flights 8% shorter; Combatant =
`with_ship` holds need 20 s instead of 30; Scientist = study a quarter shorter; All-rounder = none.

**Cargo lines in space** (a ship that never lands shows only these; a colony ship only the phase 1 lines):

| id | group | containers | min | max | gives |
|---|---|---|---|---|---|
| `ssat` System satellite | exploration | 2 | 0 | 4 | that many `SystemSatelliteCommand`s cost nothing |
| `probe` Sensor probe | exploration | 1 | 0 | 4 | that many long-range scans with no recharge wait |
| `mass` Reaction mass | engineering | 1 | 0 | 6 | flights 2% shorter each |
| `post` Listening post | science | 2 | 0 | 3 | that many `ObservationPostCommand`s cost nothing |
| `gifts` Medicine and gifts | science | 1 | 0 | 4 | culture-shock chance 3 points lower each |

Group floors in space: exploration 1, engineering 1, science 1, defense 0, ship 0 (Defense cargo waits for combat; no
"boarding gear" line yet). Each ship has a base load and fill orders per focus as in the mock's `CARGO.space`.

**Rules that stay:** no fit-out = today's game exactly (every hash unchanged); a bad fit-out is replaced by the ship's
suggested preset; the page appears for every ship the data lists; the mission reach check's space part (satellites and
posts payable) should count free satellites/posts from the leanest legal load.

**UI:** the same `MainMenu.FitOut.cs` page, showing the ship's own cargo lines and, in the summary, "Aboard" (free
satellites, free scans, free posts, flights, culture shock) instead of "At landing", and the space levers under "During
play". The flagship card and pause card already show the focus and split.
