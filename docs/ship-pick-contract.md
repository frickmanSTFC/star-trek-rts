# Ship pick + ship-specific first missions: contract

Owner 2026-11-16: "When we start the first mission, the player will decide which ship that they are going to use. Based
on the ship, the missions is different. If they pick a ship that is meant to colonize the planet, then they will get the
colonize mission, if they choose an explorer, or any other ship that doesn't colonize, they will get a different mission
and won't goto a planet or body." Decisions: **Horizon and Cochrane colonize** (First Light); **one space chain per other
ship**; design page first (`docs/mockups/ship-pick/index.html`), then code.

Two agents build this in parallel: **SIM** (data + `game/sim/` + tests) and **UI** (`game/scripts/` + textures). The
stubs below are committed to `main` first so both compile; each agent replaces only its own stubs.

## The six ships (pick-screen order)

| id | name | class | role | colonizes | chain | speed |
|---|---|---|---|---|---|---|
| horizon | Horizon | Colony transport | colony | yes | first_light | 0.8 |
| cochrane | Cochrane | Deep-space tender | colony | yes | first_light | 0.9 |
| tereshkova | Tereshkova | Long-range survey vessel | survey | no | long_survey | 1.1 |
| armstrong | Armstrong | Fast courier | courier | no | priority_run | 2.0 |
| bastion | Bastion | System patrol cutter | patrol | no | watch_station | 1.2 |
| meridian | Meridian | Diplomatic and science flagship | diplomacy | no | envoy | 0.9 |

Speed divides the system's travel time for the flagship (placeholder numbers). Start bonus (colonizers only, added to the
landing stock / landed units): Horizon +30 crew, +200 duranium; Cochrane +4 of each spare-kit item the parts economy uses for
repairs and one `construction_rover` landing with the colony.

## A. Shared API (stubs on main; names are fixed)

- `game/sim/Data/ShipChoiceDefs.cs`: `ShipChoiceDef(Id, Name, ClassLabel, Role, Colonizes, Chain, Crew, LengthM, Speed,
  Card, Blurb, MissionTitle, MissionSummary, StartBonus, StartUnits)`; `GameData.ShipChoices` (pick order),
  `GameData.ShipChoice(id)`. **SIM** replaces the hard-coded stub list with `data/missions/ship_choice.json`
  (`{ "ships": [ { "id", "name", "class_label", "role", "colonizes", "chain", "crew", "length_m", "speed", "card",
  "blurb", "mission_title", "mission_summary", "start_bonus": {res: n}, "start_units": [ids] } ] }`, listed in
  `data/manifest.json` under missions; empty list when absent) and validates it (unique ids, chain exists, colonizers'
  chain is the era's colony chain, bonus resources/units exist, speed > 0).
- `game/sim/Core/ShipChoice.cs`: `Simulation.ShipChoiceOf(player)`, `CanColonize(player)`, `ColonizeProblem(player)`
  (text "the Tereshkova can't land a colony"), internal `SetShipChoice`. **SIM** hashes the pick in `StateHash()`
  (only when set, so sandbox hashes don't change).
- `MatchSetup.CreateSystemGame(data, seed, faction, homeBody, shipId)` (old 4-argument overload unchanged) and
  `MatchSetup.HasLivingCivilization(data, seed)`. **SIM**: for a non-colonizer `homeBody` is the start orbit (any body of
  the system, gas giants and belts included); the surface is still the generator's landable pick (never landed); the
  flagship's `ClassId`/`Name` come from the ship (add pre-warp classes to `data/ships/classes/` if `HasShipClass` needs
  them; registry "UE-" style); travel ticks ÷ speed for that player's flagship; colonizers get the start bonus.
- `MissionBook.SpaceMarkers(player)` → `SpaceMarker(Kind, BodyId, ShipId, Label)` (Kind "beacon" | "pickup" | "dropoff" |
  "target" | "ship") and `MissionBook.CargoAboard(player)` (labels). **SIM** fills them.
- `game/scripts/WorldChooser.cs`: `GameSetup.ShipId`, `GameSetup.StartCampaign(faction, era, seed, homeBody, shipId,
  chain)` (sets `MissionChain` = the ship's chain). The old 4-argument start clears `ShipId`.

## B. SIM agent (data, `game/sim/`, `game/tests/`, `data/`)

1. Ship data + checks as in A.
2. No landing for non-colonizers: `LandColonyCommand`, `OutpostDropCommand`, `ShuttleCommand`, `BeamCommand` and
   `FoundOutpostCommand` refused through `Simulation.Refuse` with `ColonizeProblem`; the existing `*Problem` helpers the UI
   asks (land/outpost/shuttle problems) return the same text.
3. Missions run for space chains with no surface uplink: chains get `"space": true` in `missions/rules.json`
   (`MissionChainDef` gains `Space`); `MissionBook.Step` skips the uplink gate (and surface-only work such as
   `UpdateSites`/`LockToStartMenu`) for a space chain. Colonizers keep First Light exactly as today.
4. New vocabulary (listed in `GameData.MissionConditionFields` / action fields, documented in `missions/notes.json`):
   conditions `ship_at` {body, hold_seconds}, `visited` {count}, `charted` {count, share}, `scanned` {count},
   `swept` {body, count}, `system_satellites` {count}, `cargo` {id, state: aboard|delivered, count}, `with_ship`
   {ship, hold_seconds, at_arrival}, `contact` {body, stage: post|studied|met}; actions `bind_body` {as, pick:
   farthest|outermost_moon|gas_or_ice|civ|random_other|nearest_other, exclude}, `bind_ship` {as, pick: freighter|departing},
   `place_beacon` {at, label}, `load_cargo` {id, label, from, to}. Bodies/ships bound with the existing `{binding}` scheme.
   Everything deterministic (map seed + player + mission hashes), hashed only while missions are on.
5. Four chains as in the design page (`data/missions/long_survey.json`, `priority_run.json`, `watch_station.json`,
   `envoy.json`): five main missions each, titles and objectives from the page; givers added to rules.json (courier desk
   "Dispatcher Lindqvist", patrol "Commodore Hale", envoy "Ambassador Osei"; placeholder names). Envoy's contact steps use
   the existing civilizations commands and must work in Dawn of Warp (no Prime Directive in this era).
6. Tests: each space chain played to the end through commands only (SetCourse, LongRangeScan, SystemSatellite,
   ObservationPost, FirstContact, ContactChoice, …), twice, same hash sequence; non-colonizer landing refusals; colonizer
   start bonus; data checks; `CommandHash`/`CommandChecks` untouched unless a new command is needed (prefer none).
7. Keep `cd game && dotnet build FinalFrontier.sln && dotnet test` green; update `README.md`/`GDD.md` notes for the data.

## C. UI agent (`game/scripts/`, `game/textures/ships/`)

1. `scripts/MainMenu.ShipPick.cs`: a Choose Your Ship page between the era and the system pick (layout from the design
   page: six cards with art, tag COLONIZE / SURVEY / COURIER / PATROL / DIPLOMACY, selected detail with art, stats from
   `ShipChoiceDef`, "Your mission" lines, "Never lands" for non-colonizers, BACK / CHOOSE <NAME>); card art
   `GD.Load<Texture2D>(def.Card)` with a plain fallback panel when missing.
2. `MainMenu.SystemPick.cs`: colonizer = as today (LAND HERE); non-colonizer = every body pickable, button
   BEGIN MISSION, text says it starts in orbit; Meridian rerolls until `MatchSetup.HasLivingCivilization`; start calls the
   6-argument `GameSetup.StartCampaign`.
3. `PlanetDriver` (StartGame): pass `GameSetup.ShipId` to the 5-argument `CreateSystemGame`; a non-colonizer opens on
   the SYSTEM display, its orbit cards for landing / outpost / drop ship / beam are hidden (or greyed with
   `ColonizeProblem`), and nothing sends it to the surface (SURFACE pill hidden, O key ignored).
4. System plot: draw `SpaceMarkers` (beacon pulse ring, pick-up/drop-off arrows, target ring, bound ship highlight) and
   list `CargoAboard` on the flagship card; the mission tracker and comms card already work in orbit.
5. Read every new script for Godot runtime traps before finishing (nodes not in the tree, names, API); `dotnet build`.
