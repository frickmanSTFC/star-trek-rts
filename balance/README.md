# Balance runner

"See what changing something would cause": the runner plays the planet-surface economy without the game window, as
fast as the simulation goes (about 30,000–40,000 ticks a second, so roughly ×2,000 real time: a 30-minute match takes
under a second), with a scripted player following a **build order**. It runs several planet seeds, measures everything
it can, and writes an HTML report. Two data versions (or two build orders, or two factions) side by side show what a
change does.

- Code: `tools/BalanceRunner/` (a .NET 8 console project that references only the sim, `game/sim/`; not part of the
  Godot build). Tests: `game/tests/FinalFrontier.Sim.Tests/BalanceRunnerTests.cs` (run with the other tests).
- Build orders: `balance/orders/*.json`.
- Reports for the current data: `balance/reports/` (see "What the current data does" below).

## Run it

From the repo root (needs the .NET 8 SDK; the same commands work in PowerShell):

```bash
# 3 seeds × 30 minutes of the standard opener on the repo's data → balance/reports/latest/report.html
dotnet run -c Release --project tools/BalanceRunner --

# the same, spelled out
dotnet run -c Release --project tools/BalanceRunner -- --data data --orders balance/orders/standard_opener.json \
    --seeds 1,1701,99999 --minutes 30 --out balance/reports/mine

# A/B: change one number (B = A with the edit) — the quickest way to try a tweak without touching data/
dotnet run -c Release --project tools/BalanceRunner -- --set "buildings.json:refinery.cycles_per_min=16"
dotnet run -c Release --project tools/BalanceRunner -- --set "buildings.json:storage_container.storage=1500" \
    --set "surfaces.json:mining_crawler.extract_per_min=40" --labels "now,draft"

# A/B: two data folders (e.g. a copy you edited, or another checkout / git worktree)
dotnet run -c Release --project tools/BalanceRunner -- --compare data ../draft/data

# compare build orders, or factions, on the same data
dotnet run -c Release --project tools/BalanceRunner -- --orders balance/orders/standard_opener.json,balance/orders/fast_expand.json
dotnet run -c Release --project tools/BalanceRunner -- --orders balance/orders/fast_expand.json --factions federation,ferengi
```

| Option | Meaning |
|---|---|
| `--data <dir>` | data folder (default: the repo's `data/`); loaded through `GameData.Load` only |
| `--orders a.json,b.json` | build order(s); several = one variant each |
| `--seeds 1,1701,99999` | planet seeds, one run each (the report shows min / median / max) |
| `--minutes 30` | game time per run |
| `--sample 10` | seconds between samples |
| `--out <dir>` | report folder (default `balance/reports/latest`, git-ignored; give committed reports their own name and `--lean`) |
| `--compare <dirA> <dirB>` | A/B two data folders |
| `--set file:label=value` | B = A with this number changed (repeatable). The label is the Test panel DATA tab's, dotted: `buildings.json:refinery.cycles_per_min`, `surfaces.json:units.mining_crawler.breakdown.chance_per_min`; the end of a label is enough if it is unique. Uses the Test panel's `DataTuning`, so the same checks apply (numbers only, whole numbers stay whole, the data must still validate). |
| `--factions a,b` | run each order as these factions |
| `--labels A,B` | names for the variants (in order) |
| `--title <text>` | report title |
| `--lean` | no `runs/` folder and a compact summary (for reports you commit) |
| `--quiet` | only print the report path |

The console prints one line per run (ticks, wall time, ticks/s, × real time, build-order steps done, the final state
hash, where the order got stuck and why) and a short table of headline numbers.

**Output folder:** `report.html` (self-contained page: no CDN, charts drawn as inline SVG by a small script, light and
dark), `summary.json` (every measure per variant: min / median / max across seeds), and unless `--lean`,
`runs/<variant>-seed<n>.json` (everything one run measured, including the bot's log) and `.csv` (the time series, one
column per series).

## Build orders

A build order is a JSON file: standing behaviour plus an ordered list of steps. The bot works on one step at a time, in
order; a step waits until it can be done (and its conditions hold), then the bot moves on. Example (`standard_opener.json`):

```json
{
  "name": "standard_opener",
  "description": "…",
  "faction": "federation",
  "planet": "class_m",
  "landing":  { "prefer": ["duranium_ore", "tritanium_ore"] },
  "survey":   { "max_distance": 22 },
  "crawlers": { "prefer": ["duranium_ore", "tritanium_ore"], "max_distance": 12 },
  "upkeep":   { "maintain_below": 55, "repair_below": 45 },
  "research": ["improved_smelting", "ore_conveyors", "preventive_maintenance"],
  "steps": [
    { "build": "storage_container" },
    { "build": "refinery" },
    { "build": "mine", "deposit": "tritanium_ore", "timeout": 300 },
    { "upgrade": "refinery" },
    { "unit": "mining_crawler" },
    { "build": "foundry", "when": "duranium >= 200 and tritanium >= 80" },
    { "upgrade": "refinery", "after": "colonial_administration", "when": "parasteel >= 20" }
  ]
}
```

**Standing behaviour** (all optional, defaults shown above):

- `landing.prefer`: ores the landing site should have, by the orbital scan's region hints (what a player sees). Every
  legal site (`PlanetSurface.CanLand` + `LandProblem`, the green tiles in the UI) is scored by those hints, buildable
  ground in the hub's build radius, open land around it and safety; the best one without a storm over it wins.
- `survey.max_distance`: survey rovers explore the nearest reachable unsurveyed tiles (spreading out, avoiding toxic,
  predator and native ground) up to this many tiles from the first base, then drive home to the repair pad.
- `crawlers.prefer` / `max_distance`: an idle crawler drives to the nearest surveyed deposit of its preferred ore near a
  finished base (crawler n starts at entry n of the list, so two crawlers split between the ores); once a minute a
  crawler on a less wanted ore moves if a more wanted deposit is now known. Used-up deposits: it finds the next one.
- `upkeep.maintain_below`: send the base crew (`MaintainCommand`) to any building below this condition or broken.
  `upkeep.repair_below`: send rovers below this integrity to the repair pad; a flat tyre is changed with the spare
  (`ChangeTyreCommand`), or the rover goes to the pad when there is none; broken crawlers and disabled rovers go to the pad.
- `research`: techs to research, in order, whenever a lab is finished and idle (a tech whose prerequisite isn't known yet
  is skipped until it is).

**Steps** (one key says what it is):

| Step | Does |
|---|---|
| `{ "build": "<building>", "count": 1, "base": "main", "deposit": "<ore>", "queue": false }` | Places the building on the best tile of the base: the sim's `CheckPlacement` must say it's fine; nearest the core first, ordinary buildings keep off deposits and off tiles crawlers are heading for; mines go on the `deposit` ore if one is in range (else any ore), biggest deposit first. Waits for a free build queue unless `"queue": true` (then it pays up front and waits in line). `base`: `"main"` (first base), `"newest"`, `"any"` (oldest with a free queue and a spot) or a number (1 = first). |
| `{ "upgrade": "<building>" }` | Next level (Mk II, Mk III) of the first one of that type that can take it, or the core's next building (`colony_hub` → Colonial Administration). Skipped when all are at the top level. |
| `{ "unit": "<unit>", "count": 1 }` | Orders it at a finished Vehicle Bay with room in its queue. |
| `{ "research": "<tech>" }` | Puts the tech at the front of the research list (done at once). |
| `{ "found_outpost": true }` | Founds an outpost with a construction rover: orders one at a bay if there is none, then sends it to the best surveyed site just past the spacing rule (`SiteProblem` must be None; deposits within 3 tiles and buildable ground score). Done when the new core exists. |
| `{ "drop_outpost": true }` | Outpost drop from orbit on the best site `OutpostProblem` allows. |
| `{ "beam_supplies": true }` | Beams the ship's hold down into the main base (transporter cargo per beam, cooldown); skipped when the faction's transporter can't. |
| `{ "wait": 60 }` | Waits that many seconds. |

Every step can also have:

- `"when": "<condition>"`: terms joined by `and`, each `<name> <op> <number>` with `>= <= > < ==`. Names: a resource id
  (stock, e.g. `duranium`, `duranium_ore`, `parasteel`), `minute`, `storage_free`, `storage_pct`, `power_free` (main
  base supply − use), `income:<resource>` (per minute, the HUD's number), `count:<building or unit id>`.
- `"after": "<id>"` (or a list): a finished building of that type, a known tech, a unit of that type, or `second_base`,
  `landed`, `hub_ready`.
- `"timeout": <seconds>`: give up on the step after waiting this long (logged as skipped). Default 0 = wait forever, so a
  stuck order shows in the report as "stopped at step N (…): <why>" (can't afford it, no valid tile in range, build queue
  busy, not enough resources, needs a level 2 base, …).

The orders shipped: `standard_opener` (the Data Lab mockup's opener played for real: storage, refinery, a tritanium
mine, lab, reactor, storage, refinery Mk II, Vehicle Bay + second crawler, foundry, Colonial Administration, refinery Mk
III; no beams), `fast_expand` (beams the hold down, refinery + duranium mine, Vehicle Bay at 2 min, construction rover →
second base, mine and storage there, second crawler, second refinery, lab, reactor, an outpost drop), `tech_rush`
(beams, two labs early, refinery Mk II, foundry, mine Mk II; every tech).

**Factions.** Today the surface economy is Federation-first: every faction has the same build menu, buildings, units,
costs and techs. What does differ in the sim is the ship in orbit (name only) and the **transporter level** (surfaces.json
`orbit.transporter`: by era, with `factions` overrides — Ferengi level 2: 100 cargo a beam, 20 s cooldown, 12-tile range;
Borg 3; everyone else level 3 in the TNG era: 250 cargo, 10 s, any range). So `--factions` already shows the beaming
difference (see `reports/factions/`), and any other faction difference added to the sim's data will show up the same way.
The design data has much more per faction (buildings.json `starting_sets`: e.g. Klingon strip mines +25% yield, Cardassian
extraction +50%, Klingon/Cardassian/Dominion research −25–30%, Federation "fastest research"; `faction_buildings`), but
none of it is in the sim yet. When it is, write per-faction orders (e.g. `klingon_opener.json` with `"faction": "klingon"`)
and compare them with `--orders a.json,b.json`.

## What the bot can and can't do

Can: pick a landing site with the UI's rules, land, survey with every survey rover, put every crawler to work and move it
when its deposit runs out or a better one turns up, place buildings at valid tiles near a chosen base, wait for queues and
resources, upgrade buildings and cores, order vehicles, research a list, keep buildings maintained, repair crawlers and
rovers and change flat tyres, found outposts with construction rovers, drop outposts from orbit, beam supplies. It acts
only through commands scheduled for the current tick (no test-panel commands: `DebugEdits` stays 0) and decides once a
second. A command the sim refuses is retried, and after five refusals the step is skipped.

Can't (yet): salvage depleted mines, rebuild mines elsewhere or build deep-core mines on its own (write those as steps),
pick facings (always 0), shuttle units down, launch satellites, react to the weather, fight, play more than one player,
or decide what to build by itself — it is a build-order player, not an AI. Survey and crawler choices are greedy
(nearest first), so a better human opening exists.

## Reading the report

- **Tiles** at the top: headline numbers (first metal, refinery, first parasteel, second base, duranium and tritanium at
  the end, peak storage fill, minutes with storage full, crawler uptime, idle building time, speed), the median of the
  seeds with the range under it; with several variants, the others' medians in their colours.
- **Over time**: pick a series (stocks, income per minute, storage fill and capacity, main-base power use vs supply,
  techs, crawlers digging, rover integrity, buildings, idle buildings, surveyed tiles). Thin lines are single seeds,
  the thick line the median; hover for the values at a time.
- **Key numbers**: every measure for every variant as median (min–max); `2/3` means only two of three seeds reached a
  milestone. With variants, the change against the first is green when better for the player, red when worse, and
  "same" under 2%; changed cells are highlighted.
  - *Milestones*: first time something happened (a building type finished, first refined metal, first parasteel,
    first Mk II / Mk III, a core upgrade, second base up, each tech, each vehicle rolled out of a bay, storage first full,
    build order finished).
  - *Stalls*: minutes with storage full (no room for 1 more unit), mining blocked (a crawler waiting on full storage or
    nowhere to send pods, or a mine stopped by full storage), every refinery idle for lack of ore, a base short of power,
    a building broken down.
  - *Vehicles*: crawler time digging (% of crawler time) and broken / in repair, ore dug, pods, flats, hazards, lowest
    rover integrity, driving time lost to weather (vehicle-minutes: 1 − the weather's speed share while driving), time
    drop ships are grounded over the hub.
- **Idle reasons**: building-minutes per building type in each status (Working, LowPower, NoInput, StorageFull,
  NoUpkeep, Depleted, Broken), and crawler-minutes per state.
- **Runs**: per seed: speed, the final state hash, landing tile, what stood at the end, the stalls, where the order
  stopped and why, and the bot's log (every order it gave).

**Determinism.** Same data + order + seed = the same metrics and the same final state hash, on any machine (the sim is
fixed-point; the bot only reads state). Speeds differ, nothing else. If a run throws (a sim bug), the run stops there,
keeps what it measured, and the report says where (`CRASHED: …`); that is how the converter crash fixed in this branch
was found.

## What the current data does (2026-11-06, 3 seeds × 30 min)

Reports: `reports/standard_opener/`, `reports/orders/` (the three orders), `reports/ab_mockup_draft/` (the Data Lab
mockup's draft: refinery 16 cycles/min and Storage Container 1500), `reports/factions/` (Federation vs Ferengi).

- **Duranium is the bottleneck.** Standard opener: duranium income averages ~20 a minute (one crawler's 30 ore/min
  refined at 2 : 1, plus the hub's 4); its stock stays under ~150 all game. The Vehicle Bay (180 duranium) only comes at
  13–23 min, the second crawler at 17–28 min (one seed never), the foundry in one seed of three, and no seed affords
  Colonial Administration (300 duranium + 150 tritanium) in 30 min — or in 75. Beaming the ship's hold down (700 metal
  and deuterium) changes the opening completely: fast_expand has its Vehicle Bay at 2:05 and a second base at 4:46.
- **Storage never fills.** Peak fill is 63% (standard) to 79% (beams), and it is reached in the first minute (landing
  stock in the 1,500 hub vault); after the first two minutes the fill stays at 17–52% for the rest of the 30 minutes
  (up to 72% by 75 min in fast_expand, where unrefined ore piles up). 0 minutes of full storage or blocked mining in
  every run, even over 75 minutes. Making containers 1,500 instead of 2,000 (mockup draft) changes nothing. This
  refutes the mockup stand-in's "storage fills fast" — it would only fill once ore outruns refining for an hour.
- **One crawler can't keep one refinery busy.** 30 ore/min vs the refinery's 40 (Mk II 52): the refinery is idle with
  no ore 11.5 min of 30 in the standard opener. A slower refinery (16 cycles, mockup draft) barely matters (duranium
  income −2 a minute). With several ore sources it flips: fast_expand (2 crawlers, 2 mines) ends with ~1,250 duranium ore
  waiting even after a second refinery. Mines dig ore, not metal, so every mine needs refining capacity.
- **Mines run dry fast.** The deposit in the hub's build radius lasts about 5 min under a mine (standard, seed 1701: 4.8
  min working, 69 min depleted); the bot doesn't salvage or move mines, a player would.
- **Tritanium only trickles** unless something mines tritanium ore (hub 2/min); the foundry (2 duranium + 1 tritanium →
  parasteel) then drains both: tech_rush makes 6 parasteel a minute but its duranium income falls to ~7.5/min. The
  Alloy Foundry pause toggle (open item) would matter.
- **Power:** the hub's 30 covers refinery + mine + lab + Vehicle Bay (28); no base was ever short of power, so the
  first Fusion Reactor is not needed until the foundry. The reactor eats 3 deuterium a minute and nothing on the surface
  makes deuterium: in 75 minutes the standard opener runs out (reactor NoUpkeep for the last ~30 min).
- **Research is slow:** one lab (10 points a minute) takes 63 minutes for all seven techs (630 points); tech_rush with two
  labs knows six techs by 30 min. Refinery Mk III needs a level 2 base (Colonial Administration), which no duranium-starved
  order reaches.
- **Rover damage barely shows in 30 minutes:** survey within 22 tiles is done in ~5 min; no flats, ≤ 2 hazard hits,
  lowest integrity ~92 (worst seed 74). Crawlers break down 1–4 times and spend 0.5–3 min broken or in repair.
- **Weather:** ~0.5 vehicle-minutes of driving lost per run, drop ships grounded over the hub ~4 min of 30.
- **Factions:** Federation and Klingon play identically (same data); Ferengi (transporter level 2) get the second base
  2.5 min later and end with 49 duranium instead of 334 on fast_expand. That is the only faction difference in the sim.

## Writing new measures

`Recorder` (`tools/BalanceRunner/Metrics.cs`) samples series and counts per tick; `Aggregate.Measure` turns runs into
headline measures (label, group, unit, whether up is good); the HTML page renders whatever `summary` holds, so a new
measure shows up in the table without touching the page.
