# Star Trek RTS — Game Design Document (Draft 0.1)

> Working title: **"Final Frontier"** (placeholder). Early planning — everything here is open for debate.
>
> **IP note:** Star Trek is owned by Paramount. Fan/personal project = fine to use lore. Commercial release needs a
> license *or* an IP-safe re-skin. Data files tag every item `canon` / `beta` (licensed books/games) / `original`
> so we can swap names later without redesigning systems.

---

## 1. Pillars

1. **Colonize everything** — planets, moons, asteroids, comets, gas giants, nebulae, and free-floating stations.
2. **Every era plays differently** — the starting stardate changes tech, factions, threats and the economy.
3. **Faction asymmetry** — Federation explores, Klingons raid, Romulans hide, Borg adapt, Ferengi buy.
4. **The galaxy fights back** — anomalies, hostile habitats, pirates and crisis entities pressure every player.

## 2. Modes

| Mode | Players | Notes |
|---|---|---|
| Skirmish | 1–4 (humans + AI) | Free-for-all or 2v2. Choose era, map size, hazard level, victory types. |
| Campaign (single-player) | 1 | Faction story arcs per era; scripted crises (e.g. Wolf 359, Dominion War). |
| "Ages" Campaign | 1 | One long game that advances era to era — your tech tree moves the timeline. |
| Co-op vs Crisis | 2–4 | Players ally against a scaling Borg / Species 8472 / Burn scenario. |
| Challenge Scenarios | 1 | Kobayashi Maru (unwinnable — score on survival time), Battle of the Binary Stars, etc. |

**Victory conditions** (toggle per match): Conquest · Economic (Latinum hoard / trade dominance) ·
Diplomatic (Federation-style council votes) · Scientific (complete a "Transcendence" project: Genesis, Transwarp
Network, Omega stabilization) · Survival (outlast a crisis).

## 3. Map Structure (recommendation)

**Two-scale map** (Sins of a Solar Empire–style):

- **Sector map** — 6–24 star systems connected by **warp lanes** (lanes = chokepoints, supply lines, borders).
  Wormholes are rare long-range links (e.g. a Bajoran-style stable wormhole to a "Gamma" pocket).
- **System view** — real-time play inside a system: orbits, planets, moons, asteroid belts, stations, anomalies.

Why: keeps 4-player battles readable, makes chokepoints (Neutral Zone, Badlands) meaningful, and lets the
colonization layer live inside each system.

### Celestial bodies & what they're for

| Body | Colonize? | Primary yield | Notes |
|---|---|---|---|
| Class M planet | Full colony | Population, food, crew | Rare, contested. Pre-warp natives possible. |
| Class K/L/H/N | Domed / adapted colony | Pop (reduced), minerals | Terraforming upgrades the class over time. |
| Class Y "Demon" | Shielded outpost only | Exotic ores, rare gases | High hazard, high reward. |
| Class J/T gas giant | Orbital refinery | **Deuterium**, gases | Moons around giants are prime real estate. |
| Class D moon/planetoid | Mining outpost / pressure dome | **Tritanium, Duranium**, Topaline | Genesis-capable (Regula was Class D). |
| Asteroid field | Mining habitat | Ores, Kelbonite, Pergium | Asteroid "towns" can be hollowed out later. |
| Comet | Harvest only | Deuterium, ice | Moves on an orbit — timing matters. |
| Nebula | Harvest only | Special gases (Metreon, Zenite, Sirillium) | Often hides cloaked ships; sensor penalties. |
| Collapsed star / neutron star | Late-tech harvest | **Neutronium** | Extreme hazard. |
| Derelict / Ruins | Salvage / research | Tech fragments, relics | Iconian ruins, ancient battlefields. |
| Deep space | Station | — | Starbases, relays, shipyards can float anywhere. |

## 3b. Planet Surface (where the MVP starts)

Decided 2026-11-02: the game opens on **one planet's surface**, not in space. The star system (§3) becomes the
layer above it once the surface game is fun.

- **Map:** a hex grid on 3D terrain, generated from a seed per planet class (`data/surfaces/`). Terrain:
  ocean, shallows, plains, forest, desert, hills, mountains, polar ice; each has a move cost and is or isn't buildable.
- **Orbit view** (decided 2026-11-04): the game opens aboard the player's ship (per faction: U.S.S. Endeavour, Galaxy
  class for the Federation; Borg cube, Klingon, Romulan shapes) over the planet as a globe you turn (drag, WASD) and
  zoom. The hex map wraps east–west around it; the poles beyond the map are ice. **O** goes back up at any time.
  - *Scans* (Tab): terrain; resources (surveyed deposits bright, region readings as a faint tint); lifeforms (grazing
    herds, predators, and on some worlds a native settlement); biologics (biomass, marine life, toxic ground);
    landing safety (safe / risky / unsafe / native land / too close to a base).
  - *Light rules:* no landing or building within 4 tiles of a native settlement (Prime Directive); toxic ground and
    predators make a tile unsafe to land on, and risky within 2 tiles.
  - *Ways down:* **land the colony** (first, free); **outpost drop** (another Colony Hub, a new base, at least 10 tiles
    from your others; the ground route is the construction rover, see Vehicle Bay); **shuttle** (flies a rover, crawler or survey team to any safe spot; away for the round trip);
    **transporter** (beams a survey team, or supplies from the ship's hold into a base; level set by faction and era:
    Enterprise era people only within 6 tiles, TOS cargo within 12, TNG and later anywhere; the Borg always level 3);
    **survey satellites** (up to 3; each sweeps a band of 9 rows around the planet every 60 s, surveying it, even before landing).
- **Scan → land → survey:**
  1. *Orbit:* the scan shows all terrain plus rough deposit hints per region ("Rich: Duranium · Likely: Deuterium").
  2. *Land:* pick a flat site (plains or desert with passable land on all six sides). The colony pod falls for a few
     seconds, then the Colony Hub stands there and income starts.
  3. *Survey:* survey rovers roll out; exact deposits are revealed around the landing site and wherever rovers drive.
     One **Mining Crawler** lands too (decided 2026-11-03: exactly one for now).
- **Deposits** are rolled per tile by terrain (duranium ore in hills and plains, tritanium ore in hills, deuterium on
  coasts, dilithium in deserts…).
- **Building** (from the build bar once landed): one building per hex, on surveyed, buildable tiles within the Colony
  Hub's build radius (4). The cost is paid on placement; the hub's single build queue builds one at a time, the rest
  wait in order; unfinished buildings can be cancelled for a full refund. Buildings turn in 60° steps (hex sides).
  - *Storage Container:* +1000 storage. *Ore Refinery:* 2 ore → 1 duranium or tritanium (25 loads a minute, so 50 ore;
    shares its work between the ores in stock), +200 storage, uses power. *Alloy Foundry:* 2 duranium + 1 tritanium → 1
    Parasteel, uses power.
  - *Mining Complex:* must sit on a deposit; digs it into storage (80/min) until it runs out. Uses power.
  - *Deep-Core Mine* (needs the Deep-Core Mining tech): goes on a used-up deposit and reopens it once at
    50% of its first size; slower (48/min) and power-hungry.
  - **Pause** (P or the building panel; decided 2026-11-07 as the fix for the foundry eating metal as soon as it's
    built): any building that works (not a colony core or plain storage) can be switched off and on. Paused it uses no
    input, makes nothing, pays no upkeep, doesn't wear and neither uses nor supplies power.
  - *Fusion Reactor:* +power; burns deuterium. *Prefab Habitat:* +crew; uses power.
  - A building that can't pay its upkeep, has no ore to refine, or has nowhere to store output idles and draws no power.
  - **Salvage** (X on a finished building, not a core): returns 50% of its cost and frees the tile.
- **Storage and ore** (decided 2026-11-03): hills and plains hold *raw ore* (Duranium Ore, Tritanium Ore) that must
  be refined; deuterium, dilithium and strategics are mined as they are. Everything stockpiled except crew and
  latinum shares one storage capacity: the Colony Hub's small vault (1500, about 1000 used at landing) plus Storage
  Containers and refineries. When it's full, mines and crawlers stop. The colony core makes only a small trickle of
  metal, so a new colony's first builds are storage and a refinery.
- **Mining Crawler:** right-click a surveyed deposit and it drives there (slower than a rover), unfurls (legs brace,
  boom lifts, drill lowers; 4 s) and mines 60/min anywhere on the map, outside build range too. Each full hopper (20)
  flies home as a cargo pod to the nearest storage building (vault, container or refinery); farther = longer flight
  (3 tiles/s). It waits when storage is full, and folds up when the deposit runs out. F unfurls / packs up; a move
  order packs it up first.
- **Breakdowns and repair** (decided 2026-11-03): while digging, a crawler has a slight chance of breaking down
  (about 5% per minute of mining). Weighted severities: minor fault (70%, 10–25 damage), major breakdown (22%, 30–55)
  or an explosion (8%, 60–90, loses the load in the hopper). Integrity runs 0–100. A broken crawler folds up, can't
  mine, and drives slower the worse the damage (down to 40% speed). Repair: drive it onto the Colony Hub's tile (F, or
  right-click the hub); it parks on the **repair pad** beside the hub, two crew walk out of the hub and weld it,
  restoring 5 integrity a second for 1 duranium a point; repair pauses if you can't pay.
- **Vehicle Bay** (built 2026-11-03): a surface building (180 duranium, 60 tritanium, uses power) that assembles
  survey rovers in three marks (Mk I Scout 60 duranium, 20 s; Mk II Surveyor 90 duranium + 20 tritanium, 30 s, faster
  with a wider survey; Mk III Explorer 120 duranium + 60 tritanium + 5 dilithium, 45 s, fastest and widest; which
  research unlocks Mk II and III is still to be decided), mining crawlers (150 duranium + 50 tritanium, 45 s) and
  **construction rovers** (120 duranium + 40 tritanium, 50 s; a slow six-wheeler carrying a folded Colony Hub, a crane
  and an exocomp dock: drive it to a surveyed, buildable, safe site at least 8 tiles from your other colony cores, press
  FOUND OUTPOST, and it pays 120 duranium + 40 tritanium + 30 crew, unfolds for 6 s and is used up as a new Colony Hub
  that its exocomps build in 90 s, a new base with its own queue, power and storage; cancelling the unfinished hub
  refunds it and the rover rolls back out; the ground route to a second base, cheaper and closer-spaced than an outpost
  drop but slower), one at a time with up
  to 5 orders queued, paid when ordered. Select it and the command card shows its orders; Backspace cancels the
  last order for a full refund. The door rolls up and the vehicle drives out beside the bay. Low power slows
  assembly; an idle bay draws no power; salvaging a bay refunds its unbuilt orders in full.
- **Unit names** (owner 2026-11-04): every vehicle gets its own name and hull number when it lands, is delivered or
  rolls out (e.g. KALTY 44), drawn in a random-looking order from a pool of geology and weather words
  (`surfaces.json` `unit_names`, numbers 10–99). A player never gets the same name or number twice in a game, even
  after the vehicle is gone; when the pool runs out names go round again as KALTY II, KALTY III…, and numbers go on
  with three digits.
- **Wear and maintenance** (decided 2026-11-03): working buildings lose condition (0–100) over time (refineries,
  mines and foundries 3 a minute, reactors and labs 2, habitats 1; cores and storage never wear). Below 50% their
  output falls, down to half at 0%, and they may break down outright (more likely the lower they get). A broken
  building stops until fixed. **Manual maintenance**: select it and press M; the base's crew of two walks out of the
  core (2 s plus 0.6 s per tile), fixes it at 10 points a second for 0.3 duranium a point, and walks back. One job
  at a time per base, oldest order first. **Automatic**: Mk III buildings and the Automated Maintenance tech add
  drones that fix a building whenever it drops below 70%, without the crew. Worn reactors supply less power.
- **Building levels**: refineries, mines and foundries upgrade Mk I → Mk II → Mk III (U). Mk II: +25–30% speed,
  25% less wear. Mk III (needs Colonial Administration and parasteel): +50–60% speed, +10% yield (refinery,
  foundry), 40% less wear and drones. An upgrade refits the building to 100% condition.
- **Research** (first version, 2026-11-03): a Research Lab makes 16 research points a minute (10 until the 2026-11-07 retune) toward the tech you
  pick (select the lab: its techs replace the build card). Techs: Improved Smelting (refinery yield +20%), Ore
  Conveyors (mines and refineries +20% speed), Preventive Maintenance (30% less wear), Automated Maintenance
  (drones everywhere), Alloy Metallurgy (foundry +25% speed, +10% yield), Reinforced Drills (crawler breakdowns
  halved), Deep-Core Mining (unlocks the Deep-Core Mine). Progress is kept when you switch.
- **Building panel**: what it's working on with live rates, its level, each stat with where the bonus comes from
  (level, research, wear, power), condition and what maintenance is doing, and Upgrade / Maintain / Salvage buttons.
- **Bases:** a colony core (the hub and its upgrades) plus the buildings in its radius. Each base has its own
  build queues and **power budget** (decided 2026-11-02): energy is not stockpiled on a surface; buildings supply or
  use power, and when use exceeds supply every power user at that base slows to supply ÷ use.
- **Levels:** Colony Hub (1) → Colonial Administration (2: 2 build queues, radius 6, more power) → Planetary
  Capital (3). Upgrading pays the next level's cost, takes time, keeps the base working, and can be cancelled for a
  refund.
- **HUD** (mockup: claude.ai/artifact/2evrsmTGmagiQJhMsJuddS): resource bar with icons; event feed; three-level fog
  (visible / explored / unexplored); minimap; bottom panel with the selected base (level, upgrade, power, buildings,
  build queue) or object; command card. Shields and integrity come later with defences.
- **Camera:** pan, zoom, and turn the view in 90° steps (Q / E).
- **Opponent:** solo first to get landing, surveying and building right; then a Cardassian landing on the same world.

## 4. Economy

### 4.1 Resource layers

- **Flow resources (always visible on HUD):** Tritanium · Duranium · Deuterium · Dilithium · Energy · Crew · Latinum
- **Strategic resources (gate specific techs/modules):** Verterium Cortenide, Kelbonite, Pergium, Topaline, Zenite,
  Rodinium, Uridium, Cormaline, Trellium-D, Boronite, Benamite, Sirillium
- **Exotic (late game / dangerous):** Protomatter, Neutronium, Trilithium, Omega Molecule, Red Matter (Kelvin-only)
- **Refined components (produced, not mined):** Antimatter, Parasteel, Isolinear Chips, Bio-neural Gel Packs,
  Transparent Aluminum, Warp Coils, Plasma Conduits, Photon Warheads
- **Faction-specific:** Ketracel-white (Dominion), Nanoprobes (Borg), Bloodwine (Klingon morale), Singularity Cores (Romulan)

Full table → `data/resources/`.

### 4.2 Production chain (example)

```
Asteroid ──▶ Tritanium Ore ──▶ [Refinery] ──▶ Tritanium ─┐
Moon     ──▶ Duranium Ore  ──▶ [Refinery] ──▶ Duranium  ─┴─▶ [Foundry] ──▶ Parasteel ──▶ Hull Sections
Gas Giant ─▶ Deuterium ─────────────────────────────────────▶ [Antimatter Plant + Energy] ──▶ Antimatter
Crystal deposit ─▶ Raw Dilithium ─▶ [Recrystallizer] ─▶ Dilithium ─┐
Verterium (synth) ──────────────────────────────────────────────────┴─▶ Warp Core + Warp Coils ──▶ Warp-capable ship
```

### 4.3 Key economic rules

- **Dilithium gates warp.** No dilithium = no new warp ships and degraded FTL travel for existing fleets.
  Crystals **degrade** with use; recrystallization tech (TOS era) extends them.
- **Replicators** (TNG era) let you convert Energy → basic materials at a bad rate. *Cannot* replicate Dilithium,
  Latinum, or most strategic/exotic resources — keeps mining relevant forever.
- **Supply range:** colonies/stations project supply; ships outside it lose repair and resupply. Subspace relays extend it.
- **Trade lanes:** freighters between your colonies (or other players') generate Latinum. Pirates target them.
- **Depletion:** deposits run out; deep-core mining and asteroid-cracking techs reopen them at a cost.
  On the planet surface (built 2026-11-03, see §3b): salvage the used-up mine, refine ore through the refinery chain,
  move the Mining Crawler between deposits, and reopen a deposit once with a Deep-Core Mine (Deep-Core Mining tech
  once research exists; a level 2 base until then). Deeper-scan research is still to come.

## 5. Colonization Loop

1. **Survey** — science ship scans system: reveals deposits, habitability, natives, hidden hazards.
2. **Claim** — construction ship drops an Outpost (any body) or a colony ship founds a Colony (habitable bodies).
3. **Adapt** — build domes, atmospheric processors, orbital habitats; terraform over time
   (K → L → M is a long project; Genesis device is instant-but-risky).
4. **Develop** — districts: Mining, Industry, Research, Habitation, Shipyard, Defense.
5. **Defend & keep happy** — morale, supplies, security. Neglected colonies breed **insurgents** (Maquis-style),
   defect, or fall to pirates.

**Habitat hazard examples:** native silicon life (Horta: kill = short-term ore, negotiate = permanent bonus),
plagues, seismic instability, stellar flares, toxic mineral exposure (raw Zenite → worker impairment),
tribble outbreak (food drain), indigenous pre-warp civilization (Prime Directive choice).

## 6. Eras ("Starting Stardate")

| Era | Years | Stardate style | Tech cap | Playable factions (AI only) | Era twist |
|---|---|---|---|---|---|
| **Enterprise** | 2151–2161 | Calendar (2151.04) | Warp 5, phase cannons, polarized hull, no true shields | Earth/UEC, Vulcan, Andorian, Tellarite, Klingon, Romulan (AI: Xindi, Suliban, Nausicaan, Orion, Tholian, Gorn) | Coalition-building; Temporal Cold War events |
| **Discovery / TOS** | 2256–2293 | 4-digit (1312.4) | Warp 8, phasers, photon torps, early cloaks | Federation, Klingon, Romulan (AI: Gorn, Tholian, Orion, Nausicaan; Planet Killer event) | Dilithium scarcity; Neutral Zone outposts |
| **TNG** | 2364–2373 | 5-digit (41153.7) | Warp 9.x, Galaxy-class, industrial replicators | Fed, Klingon, Romulan, Cardassian, Ferengi (AI: Borg as a crisis, Breen, Tholian, Bajor, Maquis, Orion, Nausicaan; Crystalline Entity event) | First Borg contact; Cardassian border |
| **Dominion War** | 2373–2379 | 5-digit (50xxx) | Quantum torps, ablative armor, pulse phasers | + Dominion (AI: Breen, Borg, Bajor, Maquis, Orion, Nausicaan) | Massive fleets; wormhole chokepoint; runs to 2379 for the Shinzon crisis |
| **Delta Quadrant** | 2371–2378 | 5-digit | Bio-neural, slipstream research | Fed (lost ship), Borg (AI: Kazon, Vidiian, Hirogen, Malon, Krenim, Species 8472) | Isolated start — no supply home; scavenge |
| **Picard era** | 2380s–2402 | 5-digit (78xxx) | Transwarp research, synths | Fed, Klingon, Romulan Free State (AI: Borg remnants, Fenris Rangers, Cardassian, Ferengi, Breen, Gorn, Orion, Nausicaan) | Romulan supernova refugees; synth ban politics |
| **32nd century** | 3189+ | 6-digit (865xxx) | Programmable matter, spore drive | Federation remnant, Emerald Chain, Klingon, Ni'Var | **The Burn** — dilithium nearly gone |

Era data → `data/eras/`. Only factions with `"playable": true` in `data/factions/` can be played; minor, pirate, insurgent and crisis factions are AI only, and each faction's `eras` list matches the eras that name it (checked by the game's data validation).

## 7. Factions (summary)

| Faction | Playstyle | Signature mechanic |
|---|---|---|
| United Federation of Planets | Explorer / generalist | **Member Worlds** — integrate species for unique bonuses; Prime Directive choices |
| Klingon Empire | Aggressive raider | **Honor** — gained in battle, spent on Great Houses' support; boarding parties |
| Romulan Star Empire | Stealth / espionage | **Cloak + Tal Shiar** ops; singularity cores (no antimatter needed) |
| Cardassian Union | Industrial / occupation | **Occupation** — conquered worlds produce more but breed resistance; Obsidian Order |
| Dominion | Clone army / infiltration | **Ketracel-white** controls Jem'Hadar loyalty; Changeling infiltrators; Vorta diplomacy |
| Ferengi Alliance | Economy / mercenary | **Rules of Acquisition** perks; buys fleets with Latinum; trade monopolies |
| Borg Collective | Assimilation | **Adaptation** — shields adapt to repeated weapon types; assimilate tech & population |
| *Minor / AI* | — | Breen, Tholian, Gorn, Bajoran, Xindi, Suliban, Orion Syndicate, Nausicaan, Maquis, Kazon, Vidiian, Hirogen, Malon, Krenim, Species 8472, Fenris Rangers, Son'a |

Full data → `data/factions/`.

## 8. Research

Seven trees, each with era-gated tiers, plus a faction-unique tree:
**Propulsion · Weapons · Defense · Sensors & Stealth · Engineering · Computing · Biology & Colonization**.

- Research generated by labs, science ships doing **surveys of anomalies** (anomalies are research mines!), and
  salvaging derelicts.
- **Eureka moments:** studying a specific anomaly unlocks a specific tech (e.g. surviving a subspace rift →
  rift-mapping sensors; scanning a Borg wreck → partial adaptive shielding).
- **Forbidden techs** (Genesis, Omega, Thalaron) give massive power with diplomatic/catastrophic risk.
- **The Borg never research.** They **assimilate** tech from ships, stations and colonies they take (including other
  factions' faction-only techs) and **combine** techs they already hold into the next one, paid in nanoprobes
  (20 × the tech's tier: a tier-3 tech costs 60, about 12 cycles of one Assimilation Spire's output).

Tree → `data/tech_tree/`.

## 9. Ships

**Hull + modules** design system:

- Hull defines size class, slot counts, base HP, speed, crew requirement.
- Slots: Weapon (beam / torpedo), Defense (shield / armor), Engine, Utility (sensor, cloak, tractor, transporter,
  cargo, troop, science lab, mining laser, construction).
- Each faction has **class templates** (Galaxy, Vor'cha, D'deridex, Galor…) = a hull + default module loadout.
  Players can refit within faction rules (Borg can mix anything; Ferengi can buy foreign modules).

Roles: Scout · Construction · Mining · Freighter · Colony · Science · Escort · Frigate · Destroyer · Cruiser ·
Heavy Cruiser · Explorer · Carrier · Battleship · Dreadnought · Titan (faction super-unit).

Templates → `data/ships/`.

**Captains (hero units):** limited count, gain XP, have traits (e.g. *Unorthodox* — chance to bluff with the
**Corbomite Maneuver*; *Diplomat* — first contact bonus; *Tactician* — flank bonus). Can be captured, ransomed, killed.

## 10. Threats

- **Anomalies:** ion storms, plasma storms (Badlands), Mutara-type nebulae (shields/sensors off), metreon gas
  (weapons ignite it), subspace rifts, graviton ellipse (roaming, surfaces randomly), temporal anomalies,
  stable wormholes, Tholian webs.
- **Pirates & raiders:** Orion Syndicate (smuggling, slaver raids), Nausicaans (freighter raids), Kazon (swarms),
  Breen privateers, Hirogen hunters (target your highest-XP units).
- **Crisis entities (late game):** Borg incursion, Species 8472 invasion from fluidic space, Crystalline Entity
  (strips a planet's biosphere), Doomsday Machine (planet killer — needs a sacrificial "jam the maw" tactic).
- **Wild cards:** Q (trickster event: tests, swaps, "you are on trial"), Mirror Universe incursion, Changeling sabotage.

Hazard data → `data/hazards/`.

## 10b. Buildings, Stations & Vehicles

Data → `data/buildings/`, `data/vehicles/`.

**Three build layers per system:**

| Layer | Where | Typical structures |
|---|---|---|
| Surface | Planets/moons (slots = size × habitability) | Colony Hub → Admin → Capital, mines, refineries, labs, phaser banks, shield generator, terraforming |
| Orbital | 3–8 ring slots around any body | Drydock / Shipyard, deuterium refinery, antimatter plant, habitat ring, weapons platforms, sensor array |
| Deep space | Free placement near lanes | Outpost → Deep Space Station → Starbase → Spacedock → Yorktown megastation; listening posts, relays, tachyon nodes, minefields |

**Shipyard tiers gate hull size:** Drydock (≤3) → Orbital Shipyard (≤5) → Spacedock (≤6) → Fleet Yards wonder (Titans).

**Signature structure mechanics:**
- **Chin'toka rule** — orbital weapons platforms share one surface power generator (usually on a moon): kill it, all go dark.
- **Antimatter plants are orbital-only** — destroying one damages everything in that orbital ring.
- **Kelbonite shelters** protect populations from transporter raids, thalaron and the Crystalline Entity.
- **Research stations next to anomalies** turn hazards into eureka techs (Regula I style).
- **Faction structures:** Great Hall (Honor → Great House fleets), Nor-type ore station (Cardassian ×2 refining),
  Ketracel-white facility (Dominion lifeline — top raid target), Vinculum (Borg link; kill it to disconnect drones),
  Tower of Commerce / Latinum Exchange (Ferengi market), Starfleet Academy (Captains).
- **Relics** (map-placed, capture-only): Guardian of Forever, Iconian Gateway, Dyson Sphere, Caretaker Array, Celestial Temple.

**Vehicles & small craft:** shuttles (ferry crew, land troops, evacuate), runabouts with swappable mission pods,
work bees (speed construction), fighters (carrier hulls), probes/buoys (vision), Captain's Yacht (Captain survives
ship loss), plus civilian support (freighters, tankers, tugs, hospital ships, troop transports).

**Ground combat (v1 = abstracted):** troops land by shuttle (always) or transporter (blocked by shields/kelbonite);
battle resolves on strength × traits × terrain × fortifications. Faction troops differ: Jem'Hadar need White,
Klingons never retreat, Borg drones assimilate defenders, Changelings replace officers.

## 11. Multiplayer (initial direction)

- Up to 4 players + AI fill; lobby → era/faction/map pick.
- **Deterministic lockstep** simulation (fixed-point math, fixed tick rate ~10–20 Hz) — standard for RTS with
  hundreds of units; only inputs go over the wire. Requires deterministic RNG and no floating-point in sim code.
- Relay server for NAT traversal; optional host migration. Replays come "free" from the input log.
- Single-player uses the same sim with AI players → one code path.

## 12. Open Decisions

1. **IP path:** fan project with real names, or IP-safe original universe "inspired by"?
2. ~~**Engine**~~ **Decided (2026-11-02): Godot 4.7 .NET (C#).** Game rules live in a separate pure-C# fixed-point
   simulation (`game/sim/`); Godot only renders and takes input. Chosen over Unity for a two-person team (owner + Claude):
   text scene files, headless builds and tests, no license terms. See `game/README.md`.
3. **Map:** two-scale lanes (recommended) vs. single seamless galaxy.
4. **Real-time vs pausable** (pause for single-player only?).
5. **Ground combat:** abstracted invasions (recommended for v1) vs. tactical ground battles.
6. **MVP scope:** 1 era (Dominion War) × 2 factions (Federation, Cardassian), **starting on one planet surface**
   (§3b): solo first, then a Cardassian rival on the same world; the star system comes after. Multiplayer later (the
   sim is lockstep-ready from day one). Klingons are the third faction.
   **Build order: building/economy first** (land, survey, mine, power, construction, shipyard), then combat.
