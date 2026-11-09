# Balance runner

"See what changing something would cause": the runner plays the planet-surface economy without the game window, as
fast as the simulation goes (about 30,000–40,000 ticks a second, so roughly ×2,000 real time: a 30-minute match takes
under a second), with a scripted player following a **build order**. It runs several planet seeds, measures everything
it can, and writes an HTML report. Two data versions (or two build orders, or two factions) side by side show what a
change does.

- Code: `tools/BalanceRunner/` (a .NET 8 console project that references only the sim, `game/sim/`; not part of the
  Godot build). Tests: `game/tests/FinalFrontier.Sim.Tests/BalanceRunnerTests.cs` (run with the other tests).
- Build orders: `balance/orders/*.json`.
- Target windows: `balance/targets.json` (see "Targets" below): the report marks each milestone in or out of its window.
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
| `--targets <file\|none>` | target windows per order (default `balance/targets.json`); `none` = no targets |
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
  "crawlers": { "prefer": ["duranium_ore", "tritanium_ore"], "max_distance": 12,
                "refuel": { "resource": "deuterium", "below": 60, "until": 200 } },
  "upkeep":   { "maintain_below": 55, "repair_below": 45 },
  "research": ["improved_smelting", "ore_conveyors", "preventive_maintenance"],
  "pause": [ { "building": "foundry", "when": "parasteel >= 40 or duranium < 50", "resume": "parasteel < 20 and duranium >= 100" } ],
  "steps": [
    { "build": "storage_container" },
    { "build": "refinery" },
    { "build": "mine", "deposit": ["duranium_ore", "tritanium_ore"], "timeout": 90 },
    { "build": "refinery", "when": "duranium_ore >= 100 or tritanium_ore >= 100", "timeout": 240 },
    { "upgrade": "refinery" },
    { "unit": "mining_crawler" },
    { "build": "foundry", "when": "duranium >= 200 and tritanium >= 80" },
    { "upgrade": "refinery", "after": "colonial_administration", "when": "parasteel >= 20" }
  ]
}
```

**Standing behaviour** (all optional, defaults shown above):

- `planet` / `world`: the planet class to generate, or a world of the duel map by name or id (`"world": "veyra"` =
  class L with Veyra's climate body; `MatchSetup.ResolveWorld`), which also sets gravity and the habitability tier. On a
  Domes world the bot keeps automated buildings off covered ground and puts a Pressure Dome past the core's own field.

- `landing.prefer`: ores the landing site should have, by the orbital scan's region hints (what a player sees). Every
  legal site (`PlanetSurface.CanLand` + `LandProblem`, the green tiles in the UI) is scored by those hints, buildable
  ground in the hub's build radius, open land around it and safety; the best one without a storm over it wins.
- `survey.max_distance`: survey rovers explore the nearest reachable unsurveyed tiles (spreading out, avoiding toxic,
  predator and native ground) up to this many tiles from the first base, then drive home to the repair pad.
- `crawlers.prefer` / `max_distance`: an idle crawler drives to the nearest surveyed deposit of its preferred ore near a
  finished base (crawler n starts at entry n of the list, so two crawlers split between the ores); once a minute a
  crawler on a less wanted ore moves if a more wanted deposit is now known. Used-up deposits: it finds the next one.
- `crawlers.refuel` (optional): while a building burns that resource (a Fusion Reactor: deuterium) and the stock is
  below `below`, the newest crawler puts it first and mines the nearest surveyed deposit of it, until the stock reaches
  `until`; then it goes back to its ores. (Deuterium deposits lie on coastal plains and desert: this is the surface's
  deuterium source.)
- `pause` (optional): standing pause rules. Every building of that type is switched off (`PauseCommand`) while `when`
  holds and back on when `resume` holds (without `resume`: as soon as `when` no longer does). E.g. keep the Alloy
  Foundry from eating the metal you are saving, or stop it once there is parasteel enough.
- `upkeep.maintain_below`: send the base crew (`MaintainCommand`) to any building below this condition or broken.
  `upkeep.repair_below`: send rovers below this integrity to the repair pad; a flat tyre is changed with the spare
  (`ChangeTyreCommand`), or the rover goes to the pad when there is none; broken crawlers and disabled rovers go to the pad.
  `upkeep.jury_rig_after` (seconds, default 60; 0 = never): a building or vehicle repair that has waited this long for
  spare kits is jury-rigged instead (`JuryRigCommand` / `JuryRigUnitsCommand`: no kits, up to 60%); a jury-rigged
  vehicle then goes back to work and comes in again when it next needs to.
- `research`: techs to research, in order, whenever a lab is finished and idle (a tech whose prerequisite isn't known yet
  is skipped until it is).

**Steps** (one key says what it is):

| Step | Does |
|---|---|
| `{ "build": "<building>", "count": 1, "base": "main", "deposit": "<ore>", "queue": false }` | Places the building on the best tile of the base: the sim's `CheckPlacement` must say it's fine; nearest the core first, ordinary buildings keep off deposits and off tiles crawlers are heading for; mines go on the `deposit` ore if one is in range, biggest deposit first. `deposit` can be a list (`["duranium_ore", "tritanium_ore"]`: only those, in that order); a single ore falls back to any other raw ore (never dilithium or other strategics); none = any deposit. Waits for a free build queue unless `"queue": true` (then it pays up front and waits in line). `base`: `"main"` (first base), `"newest"`, `"any"` (oldest with a free queue and a spot) or a number (1 = first). |
| `{ "upgrade": "<building>" }` | Next level (Mk II, Mk III) of the first one of that type that can take it, or the core's next building (`colony_hub` → Colonial Administration). Skipped when all are at the top level. |
| `{ "unit": "<unit>", "count": 1 }` | Orders it at a finished Vehicle Bay with room in its queue. |
| `{ "research": "<tech>" }` | Puts the tech at the front of the research list (done at once). |
| `{ "found_outpost": true }` | Founds an outpost with a construction rover: orders one at a bay if there is none, then sends it to the best surveyed site just past the spacing rule (`SiteProblem` must be None; deposits within 3 tiles and buildable ground score). Done when the new core exists. |
| `{ "drop_outpost": true }` | Outpost drop from orbit on the best site `OutpostProblem` allows. |
| `{ "beam_supplies": true }` | Beams the ship's hold down into the main base (transporter cargo per beam, cooldown); skipped when the faction's transporter can't. |
| `{ "wait": 60 }` | Waits that many seconds. |

Every step can also have:

- `"when": "<condition>"`: terms joined by `and` (and groups of those joined by `or`, which binds looser), each
  `<name> <op> <number>` with `>= <= > < ==`. Names: a resource id
  (stock, e.g. `duranium`, `duranium_ore`, `parasteel`), `minute`, `storage_free`, `storage_pct`, `power_free` (main
  base supply − use), `income:<resource>` (per minute, the HUD's number), `count:<building or unit id>`.
- `"after": "<id>"` (or a list): a finished building of that type, a known tech, a unit of that type, or `second_base`,
  `landed`, `hub_ready`.
- `"timeout": <seconds>`: give up on the step after waiting this long (logged as skipped). Default 0 = wait forever, so a
  stuck order shows in the report as "stopped at step N (…): <why>" (can't afford it, no valid tile in range, build queue
  busy, not enough resources, needs a level 2 base, …).

The orders shipped: `standard_opener` (storage, refinery, a duranium mine (else tritanium), lab, a second refinery
once ore piles up, storage, refinery Mk II, Vehicle Bay + second crawler, the reactor when the foundry needs the power,
foundry (paused while metal is short or parasteel piles up), Colonial Administration, refinery Mk III; the newest crawler
fetches deuterium when the reactor runs low; no beams), `storage_skip` (a naive opener: no Storage Container and one
refinery, otherwise like the standard one; it checks that storage fills fast), `fast_expand` (beams the hold down,
refinery + duranium mine, Vehicle Bay at 2 min, construction rover → second base, mine and storage there, second crawler,
second refinery, lab, reactor, an outpost drop), `tech_rush` (beams, two labs early, refinery Mk II, reactor, foundry,
mine Mk II; every tech). Before the 2026-11-07 retune the standard opener built the reactor at 2:30 (not needed until
the foundry) and put its mine on dilithium when no ore was in range.

## Targets

`balance/targets.json` holds target windows per build order (placeholders, like every number in the data): which
milestone should come when, and ranges for other measures. Keys are the measure keys of `summary.json` / the Key numbers
table; `ms:<milestone>` takes minutes, other measures their own unit; `min` and `max` are both optional; `"*"` applies to
every order (an order's own entry wins).

```json
{ "orders": {
    "*": { "end:deuterium": { "min": 20, "why": "the surface can fuel a reactor" } },
    "standard_opener": {
      "ms:built:refinery": { "min": 1, "max": 2 },
      "ms:built:vehicle_bay": { "min": 6, "max": 9 },
      "made:duranium": { "min": 35 } } } }
```

The console prints "N of M targets in their window" per variant with the ones that are out; the report has a Targets card
and a badge on every targeted row of Key numbers: `in`, `early` / `late` (times), `low` / `high` (other measures),
`missed` (no seed reached it), or `after run` (not reached, and the window starts after the run's end: not judged — run
longer); `2/3` = seeds whose own value is inside. The median of the seeds is what counts (a milestone only some seeds
reach: the median of those that did). Milestones a target names are listed even when no run reached them.

The windows set for the economy retune (2026-11-07), standard opener: refinery 1–2 min, Vehicle Bay 6–9, second crawler
9–12, first parasteel 12–18, Colonial Administration 20–30, power needed beyond the core (a reactor decision) 10–20, all
seven surface techs 35–45 with one lab, refinery idle ≤ 5 min, duranium income ≥ 35 a minute; storage_skip: storage first
full 10–15 min; fast_expand: bay ≤ 4, second base 4–10, second crawler ≤ 15; tech_rush: all techs 18–30 with two labs,
first parasteel ≤ 15; every order: no power shortage over a minute, deuterium ≥ 20 at the end, no crash.

**Factions.** Today the surface economy is Federation-first: every faction has the same build menu, buildings, units,
costs and techs. What does differ in the sim is the ship in orbit (name only) and the **transporter level** (surfaces.json
`orbit.transporter`: by era, with `factions` overrides — Ferengi level 2: 100 cargo a beam, 20 s cooldown, 12-tile range;
Borg 3; everyone else level 3 in the Dominion War era (the match era): 250 cargo, 10 s, any range). So `--factions` already shows the beaming
difference (see `reports/factions/`), and any other faction difference added to the sim's data will show up the same way.
The design data has much more per faction (buildings.json `starting_sets`: e.g. Klingon strip mines +25% yield, Cardassian
extraction +50%, Klingon/Cardassian/Dominion research −25–30%, Federation "fastest research"; `faction_buildings`), but
none of it is in the sim yet. When it is, write per-faction orders (e.g. `klingon_opener.json` with `"faction": "klingon"`)
and compare them with `--orders a.json,b.json`.

## What the bot can and can't do

Can: pick a landing site with the UI's rules, land, survey with every survey rover, put every crawler to work and move it
when its deposit runs out or a better one turns up, send a crawler for deuterium, pause and resume buildings by rule, place buildings at valid tiles near a chosen base, wait for queues and
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
    first Mk II / Mk III, a core upgrade, second base up, each tech, every surface tech known, each vehicle rolled out of
    a bay, storage first 80% full and first full, power needed beyond the core (a base using more than its own core
    supplies: the reactor decision), build order finished).
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

## What the current data does (economy retune 2026-11-07, 3 seeds × 30 and 60 min)

Reports: `reports/economy_retune/<order>/` (A/B: **before** = the data as it was (commit 3f59eb1), **after** = now; both
with the same new orders and bot, 3 seeds × 30 min; `standard_opener_60/` = 60 min), `reports/standard_opener/`,
`reports/orders/` (all four orders), `reports/factions/` (Federation vs Ferengi on fast_expand) for the current data.
`reports/ab_mockup_draft/` is from before the retune (the Data Lab mockup's draft on the old data).

**The changes** (data; every one a placeholder):

| What | Before → after | Why |
|---|---|---|
| Mining Crawler `extract_per_min` / `hopper` | 30 / 10 → 60 / 20 | one crawler keeps a refinery busy (a pod every 20 s, as before) |
| Mining Complex `extract_per_min` | 40 → 80 | a mine on top of the crawler piles ore up: storage fills fast unless you refine |
| Deep-Core Mine `extract_per_min` | 24 → 48 | keeps its 0.6 of a mine |
| Ore Refinery `cycles_per_min` / `storage` | 20 / 300 → 25 / 200 | 50 ore a minute: refining is the limit, so ore piles; its bins are a buffer, not a warehouse |
| Storage Container `storage` | 2000 → 1000 | storage is limited and fills fast (owner's rule); the hub vault stays 1500 |
| Duranium ore deposits | hills, 0.22, 600–1800 → hills + plains, 0.15, 1200–3000 | plains are landing ground, so a mine near the hub usually finds duranium; deposits last 15–40 min under a mine |
| Tritanium ore deposits | hills + plains, 0.07, 400–1200 → hills, 0.12, 800–2400 | tritanium (needed about a third as much) is a crawler drive away instead of filling the hub's radius |
| Research Lab `research_per_min` | 10 → 16 | all seven techs (630 points) in ~40 min with one lab |
| Colony Hub `power_supply` | 30 → 36 | two refineries, a mine, a lab and the bay run on the hub; the reactor becomes needed with the foundry (~15 min) |

Not changed: costs (Vehicle Bay, Colonial Administration, foundry, everything), the hub vault (1500) and its small
trickle (4 duranium, 2 tritanium), reactor upkeep, deuterium deposits, landing stock, one crawler at landing.

**Sim:** any building that works can be paused (`PauseCommand`, P / the building panel; status Paused: no input, no
output, no upkeep, no wear, no power). **Orders/bot:** pause rules, `crawlers.refuel`, deposit lists, `or` in conditions;
the standard opener now builds its reactor when the foundry needs it, a second refinery once ore piles up, and pauses the
foundry when metal is short or parasteel piles up; new order `storage_skip`.

**Standard opener, median of seeds 1, 1701, 99999** (original = old data, old orders and bot; before = old data with the
new orders and bot; after = now):

| Milestone | Target | Original | Before | After (30 min) | After (60 min) |
|---|---|---|---|---|---|
| Refinery done | 1–2 min | 1:13 | 1:13 | 1:13 | 1:13 |
| Vehicle Bay done | 6–9 | 19:52 | 10:45 | 8:19 | 8:19 |
| Second crawler rolled out | 9–12 | 22:46 (2/3) | 25:17 | 10:37 | 10:37 |
| Power needed beyond the core | 10–20 | (reactor at 2:28) | 13:11 (1/3) | 16:09 (2/3) | 16:09 (2/3) |
| First parasteel | 12–18 | 23:32 (1/3) | 20:43 (1/3) | 16:27 | 16:27 |
| Colonial Administration | 20–30 | never (75 min) | 28:42 (1/3); 43:38 in 60 min | 23:07 (2/3) | 23:30 |
| All seven techs, one lab | 35–45 | 63 min | not in 60 | – | 41:32 |
| Duranium income a minute | ≥ 35 | 20 | 19 | 52 | 54 |
| Every refinery idle (no ore) | ≤ 5 min | 12 | 0.3 (12 in 60 min) | 0 | 0 |
| Deuterium at the end | ≥ 20 | 47 | 128 | 74 | 402 (refuelled) |
| Targets in window | | | 7 of 11 (5 of 12 at 60) | 11 of 11 | 12 of 12 |

Other orders (30 min, after): storage_skip storage first full 14:43 (target 10–15; before: never) and then 9.6 min full;
fast_expand 6 of 6 (bay 2:05, second base 4:47, second crawler 5:54); tech_rush 5 of 5 (first parasteel 4:52, all techs
21:56). Seven seeds × 45 min (1, 1701, 99999, 42, 2024, 7, 314) keep the standard opener at 12 of 12.

- **Duranium is no longer the bottleneck:** ~50 a minute instead of ~20; Vehicle Bay at ~8 min, second crawler ~10:40,
  Colonial Administration ~23 min, Mk III refinery by ~25 min. The refinery is now the limit: with a crawler and a mine
  on duranium ore it can't keep up, ore piles up (1,600 duranium ore by 30 min on seeds 1 and 99999) — a second
  refinery or a Mk II is the answer, and a reason to build storage.
- **Storage fills fast now:** without containers (storage_skip) the vault fills at ~14:40 and mining stalls ~10 of 30
  min; tech_rush (one refinery, one container, beams) is full 11 min and its crawler digs only 57% of the time;
  fast_expand 4 min full. The standard opener (two containers, two refineries) stays below 80% for 40 min and reaches
  ~88% by 60.
- **Deuterium:** deuterium deposits (coastal plains and desert) were the surface source all along; the bot just never
  mined them. With `crawlers.refuel` a crawler fetches ~150 in 3–4 min when the reactor runs it down (around 35–45 min);
  402 at 60 min. The reactor also comes later now (~11 min, when needed), not at 2:30.
- **Mines last:** a mine on a 1200–3000 duranium deposit works 17–32 min (the old "5 minutes" was the bot putting its
  mine on a dilithium deposit of ~200). The bot still doesn't salvage used-up mines.
- **Pause:** the foundry spends most of its time paused once it has 40 parasteel (it only feeds Mk III upgrades so
  far); the rule pauses it below 50 duranium too. Generic: any converter, mine, bay, lab or reactor.
- **Seed variance is big:** on seed 1701 there is no duranium ore in the hub's radius, so the mine digs tritanium
  (1,200+ tritanium piles up) and Colonial Administration comes at ~37 min; one seed in three misses it in 30 min.
- **Tritanium** now mostly comes from the second crawler (hills); it piles up mid-game (500–1,400) because only
  Colonial Administration and the foundry need much of it.
- **Rovers, weather, breakdowns:** as before (little rover damage in 30 min, ~0.5 vehicle-min lost to weather).
- **Factions** still differ only by the transporter (Ferengi get their second crawler ~16 min instead of ~6 on
  fast_expand); the per-faction bonuses are text only — left for later.

Before the retune (2026-11-06, kept for the record): duranium ~20 a minute and under ~150 all game, no seed reached
Colonial Administration in 75 min, storage never filled (peak 63% in the first minute), one crawler (30/min) left a
refinery (40/min) idle 11.5 of 30 min, tritanium trickled, the foundry drained duranium with no way to stop it, the
reactor ran dry at ~45–50 min, one lab needed 63 min for seven techs.

## Veyra, a Domes world (2026-11-08, 3 seeds × 30 min, `reports/veyra_opener/`)

`orders/veyra_opener.json` plays the standard opener on Veyra (`"world": "veyra"`: class L on body west_l, 0.9 g,
Domes tier because O2 13.5% is below 16%) with the domes kit: a Life Support Plant after refinery Mk II (the hub's own
life support carries the hub and the lab), a Pressure Dome two tiles out (the hub's field only reaches its ring), the
Fusion Reactor before the bay (the dome and the plant draw power). The bot keeps automated buildings off covered ground
and puts a dome past the hub's field. New measure: `metal:<minute>` = refined metal made by then (the HUD's net
duranium + tritanium + parasteel income summed), the gravity doc's "refined metal at minute 10 and 20".

| median of seeds 1 / 1701 / 99999 | Tarsis (M) | Veyra (Domes) | Veyra ÷ M |
|---|---|---|---|
| Metal made by minute 10 / 20 / 30 | 550 / 1,362 / 2,262 | 534 / 1,376 / 2,186 | 97% / 101% / 97% |
| Duranium / tritanium income a minute | 52.2 / 23.0 | 57.2 / 14.9 | 110% / 65% |
| Life Support Plant, Pressure Dome | — | 8:11, 11:20 | |
| Vehicle Bay, second crawler | 8:19, 10:37 | 14:40, 17:02 | 1.76×, 1.60× |
| Fusion Reactor, first parasteel | 11:21, 16:27 | 12:06, 21:20 | 1.07×, 1.30× |
| Colonial Administration | 23:07 | 27:38 | 1.20× |
| A base short of power | 0 min | 3.9 min | |
| Crew at the end | 353 | 337 | 95% (none lost to failed life support) |

Tuning done (placeholders): Pressure Dome 200 duranium + 60 tritanium, 45 s, 8 power; Life Support Plant 120 + 30,
25 s, 6 power, supports 4 crewed buildings; on a Domes world every crewed building costs 15% more metal and draws 2 more
power (the doc's Domes-tier row; `habitability.domes` crewed_metal_cost / crewed_extra_power); the plant moved after the
second refinery in the order. **Findings:**

- **Update 2026-11-08 (owner: "yes, make refineries crewed on dome worlds"):** the Ore Refinery is `crewed: true`, so on a Domes world it needs cover, costs 15% more metal and draws 2 more power; the order now builds the Life Support Plant right after the first refinery and the mine. Seeds 1/1701/99999 × 30 min (median): refined metal **299 / 790 / 1,300** at minutes 10 / 20 / 30 vs Tarsis 550 / 1,362 / 2,262 = **54% / 58% / 57%**, a little under the doc's 60–80% band; Vehicle Bay 20:48 (2.5× Tarsis; it waits for the dome), no parasteel by 30 min, base short of power 1.4 min. Tried and reverted (no effect on these numbers): core life support 2/3/4 → 3/4/5, crewed_extra_power 2 → 1. The rest of this section is the earlier, automated-refinery result. Report: `reports/veyra_opener/`.
- **Refined metal stays at ~97–101% of class M, above the doc's 60–80% band.** The refineries set the pace of refined
  metal, and they are automated (crewed: false, as the doc lists the Ore Refinery), so the domes kit never slows them:
  it costs metal and time instead (bay 1.76×, second crawler 1.6×, parasteel 1.3×, Colonial Administration 1.2×).
  Veyra's map (more hills) even gives more duranium. Making the kit dearer only moved things when it pushed the second
  refinery past its 240 s window (one refinery all game: ~60%), which is a cliff, not a dial. Making Veyra's ore
  poorer (duranium/tritanium chance 0.09) changed nothing: one crawler and a mine already outrun two refineries.
- **What would bring it into the band:** with the refinery crewed (a smelter needs operators, so under a dome with life
  support) the same order made 45% / 59% / 72% at minutes 10 / 20 / 30. That is a design call for the owner (the doc
  says automated), so the data keeps the refinery automated for now.
- The Vehicle Bay comes later than the doc's 1.5× (14:40 vs 12:30): the bay waits for the dome (it needs the ring the
  dome opens). The base runs ~4 minutes short of power before the reactor (target ≤ 1): the reactor could come before
  the dome. Crew loss to failed life support: none with the plant (it is the grace clock: 30 s at zero, then 2 crew a
  minute per crewed building).
- Class M is untouched: the four shipped orders give the same final state hashes on all three seeds as before the
  gravity and hostile-worlds work. (Since the parts economy, 2026-11-09, every final hash is different: see below.)

## Parts economy (2026-11-09, 3 seeds × 30 min, `reports/orders/`, `reports/standard_opener/`, `reports/veyra_opener/`)

Repairs now take spare kits instead of duranium a point (`data/surfaces/fabrication.json`: one kit per 25 points, a
broken-down crawler 1/2/3 more by severity, a pad tyre 1 Rover Spares), and every player starts with keep-in-stock
targets of 2 for each kit their buildings and vehicles use, so from touchdown the Colony Hub's small workshop makes
Mechanical Spares (8 duranium + 3 tritanium, 130 s each) and queues Rover Spares (they need parasteel, so they wait for
the foundry). None of the orders builds a Machine Shop. The bot jury-rigs a repair that waits 60 s for kits
(`upkeep.jury_rig_after`). Before = the commit with the contract stubs (699fab4), after = now; same orders and seeds;
median of seeds 1 / 1701 / 99999:

| Measure | standard_opener | fast_expand | tech_rush | storage_skip |
|---|---|---|---|---|
| Targets in window | 11 of 11 → 11 of 11 | 6 of 6 → 6 of 6 | 5 of 5 → 5 of 5 | 4 of 4 → 4 of 4 |
| Vehicle Bay done | 8:19 → 8:31 | 2:05 → 2:05 | – | 4:52 → 5:18 |
| Second crawler | 10:37 → 10:49 | 5:54 → 5:54 | – | 8:52 → 9:10 |
| First parasteel | 16:27 → 16:02 | – | 4:52 → 5:15 | 19:59 → 19:14 |
| Colonial Administration (2 of 3 seeds) | 23:07 → 21:14 | – | – | 25:52 → 25:13 |
| Metal made by minute 30 | 2,262 → 2,282 | 1,780 → 1,776 | 1,324 → 1,314 | 1,337 → 1,344 |
| Crawler time digging | 92.7% → 85.5% | 78.2% → 75.1% | 57.1% → 58.2% | 56.3% → 59.0% |
| Crawler broken or in repair | 1.7% → 4.0% | 1.6% → 3.2% | 0.6% → 1.7% | 0.9% → 2.4% |
| Crew maintenance jobs | 3 → 5 | 4 → 6 | same | 3 → 4 |

- **Small moves, every target still in its window.** The kits cost about what the old per-point duranium did (a full
  repair of a building at 50%: 2 kits = 16 duranium + 6 tritanium at the hub, was 15 duranium), but the hub's 130 s per
  kit makes repairs wait: crawlers spend 1–2.5 points more of the match broken or on the pad (a breakdown can need up
  to 7 kits), and jury-rigged buildings (60%, wearing 2× faster) come back for maintenance sooner. Without the jury-rig
  rule the standard opener lost 20 points of crawler time (92.7% → 72.3%) and one seed never built its foundry.
- Veyra (`veyra_opener`) is where it was: 3 of 7 targets (bay 20:48 → 19:57, metal at 10 / 20 min 299 / 790 → 302 / 796).
- **Hashes:** every final state hash changed (the 12 new resources are in every stock, and repairs and the hub workshop
  change the game from touchdown), so golden hashes of landed games are new; nothing had to be re-recorded (the
  pre-split golden test stops applying once the data is edited).
- Next to try: a Machine Shop in the standard opener (65 s a Mechanical Spares kit instead of 130 s, at 12 power and
  its own wear), and whether default targets of 2 are enough for a crawler explosion (up to 7 kits).


## Writing new measures

`Recorder` (`tools/BalanceRunner/Metrics.cs`) samples series and counts per tick; `Aggregate.Measure` turns runs into
headline measures (label, group, unit, whether up is good); the HTML page renders whatever `summary` holds, so a new
measure shows up in the table without touching the page.
