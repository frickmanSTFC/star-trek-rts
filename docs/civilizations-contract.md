# Civilizations phase 1 contract (2026-11-09)

Shared names for civilizations phase 1, so sim/data, the native town models and the view can run in parallel.
Design doc: "Civilizations & First Contact" (claude.ai/artifact/A8NZp9KCb7pft9mo7xLbYB). Mockup:
`docs/mockups/first-contact/index.html` (claude.ai/artifact/H6Y5rVyMDZYUTe2jREiVvY). All numbers are placeholders.

Owner's answers: natives **never on the colony's landing world** (other bodies of the system only); while a match
is one system, **about half of systems roll a living civilization and most have ruins** (back to 1 in 6 / 1 in 4
with a galaxy map); natives react only through awareness, unrest and events (no ground combat); **Federation only**
for now; phase 1 = **scan (a level range) + dossier + observe from orbit + a hidden observation post** placed from
orbit like a satellite (discovery risk, awareness, Starfleet Standing); first contact / membership are phase 2.
Study narrows the level range to the exact level in **~2 min from orbit, 1 min with a satellite, 30 s with a post**,
then temperament and unity are known. Prime Directive is **harsh** (Standing ladder below) but **only from ~2168**:
in eras before it (Dawn of Warp, Enterprise) there is no Standing meter. Towns are a **Blender kit by level**,
alien-flavoured per species; other bodies' towns are seen **from orbit** (lights/structures on the globe) and in a
look-only feed from a satellite or post. Standing is a **Federation-only top-bar chip** (+ a dossier row).

## Data (sim agent): new document `data/civilizations/` (manifest entry; a sim document)

- `rules.json`: chance per system of a living civ (0.5) and of ruins (0.8), body weights by class (M high, L/K/H/N
  lower; landable classes only; never `SurfaceBody`), at most one site per body; awareness bands (0–19 / 20–49 /
  50–79 / 80+); what raises awareness (satellite seen by L4+, post found, …); study seconds (orbit 120, satellite 60,
  post 30); observe research per minute while studying (by level) and per post; post cost (like the satellite's),
  discovery chance per 10 min (doubling above awareness 40), what discovery does (awareness +35, Standing −20, an
  incident); Standing: start 80, thresholds 70/50/30/10, recovery +1/min with no incident in the last 10 min, rung 5
  at 0 or a third incident on one world; `prime_directive_from_era` (eras before it have no Standing).
- `levels.json`: L1..L6 with name, population range, town count range and sizes, whether towns are lit, orbital signs.
- `names.json`: species name syllables (generated names), nation name parts.
- `ruins.json`: ruin kinds for phase 2 digs (list only for now: invented species + rare canon precursors).

## Sim API (`game/sim/Space/Civilizations.cs`; stubs now)

- Enums `CivLevel` (L1..L6), `Temperament`, `CivUnity`; records `CivSite`, `NativeTown(Tile, Size, Lit)`, `CivReport`.
- `StarSystemState`: `Civilizations`, `CivAt(body)`, `CivView(player, body)`, `AwarenessOf(body)`, `TownsOn(body)`,
  `StandingOf(player)` (null outside the Prime Directive or for non-Federation), `StandingRung(player)`,
  `IncidentsOn(player, body)`, `ObservationResearchPerMinute(player)`, `PostProblem(player, body)`.
- Command `ObservationPostCommand(player, bodyId)` (tag 30, routed with the star-system commands to
  `ApplyObservationPost`).

Rules: sites are rolled from the system seed (pure, SplitMix like `SystemGenerator`; no `sim.Rng`), in a
`GeneratedSystem`-level list (add to `SystemGenerator` output or roll in `StarSystemState` from `Seed`). Knowledge
follows the body's: Detected/Scanned → `Signals` only; Charted → species, level range (±1, clamped), population,
awareness; study accumulates while a ship of the player orbits (×1), a satellite is there (×2) or a post (×4)
until Study = 1 → exact level, temperament, unity (all kept). Observing adds research to the player's surface
research (`SurfacePlayer.ResearchProgress` via the active tech, or a pool — say which). Discovery rolls and
awareness changes use `sim.Rng` in `Step`; awareness, posts, study, standing, incidents are hashed only in system
games (planet-only and balance hashes unchanged). Events for the view: `Version` goes up on any change; add a small
event list (e.g. `CivEvents` with tick + text + body) the view can read for feed lines (post found, incident,
standing step reached, study complete).

## Models (`game/models/natives/towns.glb`, Blender agent)

One glb with a node per town type (each its own small mesh set, alien-flavoured, recoloured per species):
`town_l1_a/b` (huts, fire pits, fields), `town_l2_a/b` (walled stone town, towers), `town_l3_a/b` (industrial town:
smokestacks, rail, warehouses; smoke points `Smoke0..`), `town_l4_a/b` (atomic-age city: towers, antenna masts,
dish, lights), `town_l5_a` (city + spaceport/launch pad), `town_l6_a` (arcology towers, landing pads), plus `_far`
stand-ins for each level. Materials: "Species" (base, recoloured), "SpeciesTrim", "CityLight" (emissive, night),
"Glass", "Ground". Sizes: a town fits one hex (≈ 20 across), size 2–3 = scaled/clustered by the view. Godot:
`game/scripts/NativeTowns.cs` `Build(level, variant, speciesColour, size)` (glb or primitive stand-ins with the same
names), `SpeciesColour(seed)`.

## View (render-only)

- Body card (system plot / helm): a CIVILIZATION section by knowledge: Signals ("artificial signals — possible
  civilization"), Charted (species, level range or exact level, population, awareness, study bar), studied
  (temperament, unity, nations), ruins ("ruins of the <species>"); a HIDDEN POST action (`PostProblem`, sends
  `ObservationPostCommand`) next to DEPLOY SATELLITE while the flagship orbits it.
- Dossier: a larger card / panel for the civ (from the mockup's dossier screen, game HUD style).
- Standing chip on the top bar (Federation in a Prime Directive era only), ladder in its tooltip; event lines from
  the sim's civ events.
- Orbit globe of a civ body: city lights on the night side at `TownsOn` tiles (L3+ lit), small structures visible
  when zoomed in (towns from `NativeTowns`), smog tint for L3–L4. A look-only feed of the towns from a satellite or
  post is a stretch goal (report if not done).

## Sim side, filled in (2026-11-09, sim branch)

Names and signatures above are unchanged. Decisions and additions for the view:

- **Data** `data/civilizations/` (manifest document `civilizations`, optional like achievements: a data set without it has no
  natives; `GameData.Civilizations` → `CivilizationData` with `Sites`, `Awareness`, `Study`, `Post`, `Standing`, `Levels`,
  `Names`, `Ruins`; `GameData.CivLevels` = 6; read + `ValidateCivilizations` in `sim/Data/GameData.Civilizations.cs`, records
  in `CivilizationDefs.cs`). `DataChecks.CivilizationProblems` (in `DataChecks.All`): seeds 1/1701/99999 with every landable
  body as the landing world → no site on it, none twice on a body, every living site has a town map with towns; seeds 1..200
  → the share of systems with a living civ inside `sites.expected_living_share` [0.35, 0.65].
- **Body classes.** Only class M and L have surfaces, and ~85% of generated systems have just one landable body (the landing
  world), so "landable classes only" would have given ~15% of systems a civ. Living civs may also sit on K, H and N
  (`sites.living_weights`), whose towns go on a stand-in look-only map (`sites.town_maps`: K/H → class_l, N → class_m);
  ruins may also be on D (`ruins_weights`). `living_chance` is 0.58 so that about half of all systems end up with one
  (measured 42% at 0.5; ~49% at 0.58); ruins_chance 0.8 → ~78%. One living site and one ruins site per system at most.
- **Generation** `CivGenerator` (pure, SplitMix via `SystemGenerator.H`): `Roll(data, bodies, seed, excludeBody)`,
  `NationNames(data, site)`, `LevelRange(data, site)`, `Towns(data, site, map)`, `Root(names, h)`. `StarSystemState` rolls
  the sites once when the system joins the game (`Attach`), excluding `SurfaceBody`. `CivSite` gained init properties
  `Ruin` (ruins.json kind id, ruins only) and `Root` (the species' name root). Ruins: `Level` = how far they got,
  `Temperament`/`Unity` Unknown, no nations/population; canon precursors (Iconians, T'Kon, Preservers) are weight 1 of 90 each.
- **Level range** before study is `sites.level_range` (2) levels wide with the true level inside (where: by the site seed;
  owner's "L3–L4" example), not ±1 (which would give the level away as the middle).
- **Knowledge** (`CivView`): Detected/Scanned → `Signals` only for living L3+ (`levels.json signals`); L1–L2 and ruins → null
  until Charted. Charted → species, range, population, awareness (+ `AwarenessBand` name), `OrbitalSigns`, `Incidents`;
  ruins → `Living` false, species, `RuinName`. Study = 1 → exact level, temperament, unity, nations + `NationNames`.
  `Live` = `HasLiveData` or a working post there. `CivReport` gained init properties `RuinName`, `AwarenessBand`,
  `NationNames`, `OrbitalSigns`, `Incidents`.
- **Study** only once the body is Charted: per tick 1/(120 s) for any own ship in orbit + 1/(60 s) satellite + 1/(30 s)
  working post (they add up; ship alone ≈ 1801 ticks). `StudyOf(player, body)`.
- **Observation research** (chosen): `observe_per_min[level]` while a ship/satellite studies (until Study = 1) + `post_per_min
  [level]` per working post, always; goes into the player's **active surface tech** (`SurfacePlayer.ActiveTech` /
  `ResearchProgress`, completing it like a lab); with no tech active it waits in a per-player bank
  (`ObservationBank(player)`, hashed) and is paid into the next tech. Sanctions × 0.75; 0 once recalled.
- **Towns** `TownsOn(body)` / `TownMap(body)` (the `SurfaceMap` the tiles are on: own class or the stand-in;
  `SurfaceMap.Generate(data, data.Surface(cls), SystemGenerator.SurfaceSeed(seed, id), id, Generated.Climate)`), both cached,
  not hashed. Count `levels.towns`, size `town_size` (first town = largest, the capital), weighted land tiles
  (`town_terrain` plains 5 / forest 3 / hills 2 / desert 1, +3 coastal), ≥ `town_spacing` 10 tiles apart (lowered by 2 when
  the map has no room). The view converts tiles to lat/long with that map's grid (same as the look-only globes).
- **Awareness** (0..100, only rises; deterministic accumulation, no `sim.Rng`): start by level (`awareness.start`), + ship in
  orbit ×2/min if the civ is L5+, + satellite ×1/min if L4+, + 0.2/min per working post, + 35 when a post is found.
  `AwarenessOf`. Bands in `awareness.bands` (Unaware / Rumours / Suspicious / Aware).
- **Post** (`ObservationPostCommand`): `PostProblem` order: no such player → no civilizations data → "only the Federation can
  place hidden observation posts for now" (`post.factions`: federation, earth_uec) → "recalled to Starfleet" → no such body →
  "no ship in orbit" / "the ship is not in orbit of X" → "finish the sensor sweep of X first" → "no civilization on X to
  observe" / "only ruins on X: nobody to observe" → "a post is already hidden on X" → "all N observation posts are out" →
  "not enough resources". Cost 80 duranium + 40 tritanium from `sim.Players[p].Stock` (like the satellite). `Posts`
  (`ObservationPost`: Owner, BodyId, PlacedTick, NextRollTick, Found, FoundTick), `SecondsToNextRoll(post)`. Rolls on
  `sim.Rng` every 10 min of game time from placement, chance 0.1 (× 2 above awareness 40). Found → stops working, stays
  listed (`PostFound` in the report), awareness +35, incident, Standing −20; a new post may be placed there again.
- **Standing** (`StandingOf`, `StandingRung`, `RungName(rung)`, `PrimeDirective`): Federation players (`standing.factions`)
  only, in eras whose first year ≥ `standing.prime_directive_from_year` 2168 (a year, not an era id: `tos` starts 2256 so
  "from tos" would have cut out Kelvin 2233; ENT 2151 and a future Dawn of Warp have none; an era not in the data counts as
  having it). Fixed when the player joins. Start 80, recovery +1/min after 10 min with no incident, up to the start.
  Steps when newly reached: 1 warning (event), 2 inquiry (`InquirySecondsLeft`: `CourseProblem`/`ScanProblem` say "held for a
  Starfleet inquiry (N s)" for 300 s), 3 captain relieved (event only for now), 4 sanctions (`SanctionsSecondsLeft`:
  observation research × 0.75 for 900 s; lab research and reinforcements are not touched yet), 5 recalled for good at 0 or
  the 3rd incident on one world (`Recalled`: course, scan, satellite and post orders refused "recalled to Starfleet"; no
  recovery). Re-crossing a step after recovering applies it again.
- **Incidents** `IncidentsOn(player, body)`: post discoveries only in phase 1 (counted in every era).
- **Events** `CivEvents` (`CivEvent(Tick, Player, BodyId, Kind, Text)`, each addressed to one player): `post_placed`,
  `post_found`, `incident`, `standing` (Text names the step), `recalled` (BodyId = the colony's body), `study_complete`,
  `awareness` (a band reached; only to players who have Charted the body). `Version` goes up on every event, each 5% of
  study, each whole point of awareness or Standing and each step.
- **Hash**: sites, awareness, every player's study/Standing/steps/hold/sanctions/bank/incidents, posts and events, inside
  `StarSystemState.AddToHash` (system games only; planet-only and balance-runner hashes unchanged, tested).
- Tests `CivilizationTests` (18).
