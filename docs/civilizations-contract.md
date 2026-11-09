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
