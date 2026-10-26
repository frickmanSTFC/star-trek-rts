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

## 4. Economy

### 4.1 Resource layers

- **Flow resources (always visible on HUD):** Tritanium · Duranium · Deuterium · Dilithium · Energy · Crew · Latinum
- **Strategic resources (gate specific techs/modules):** Verterium Cortenide, Kelbonite, Pergium, Topaline, Zenite,
  Rodinium, Uridium, Cormaline, Trellium-D, Boronite, Benamite, Sirillium
- **Exotic (late game / dangerous):** Protomatter, Neutronium, Trilithium, Omega Molecule, Red Matter (Kelvin-only)
- **Refined components (produced, not mined):** Antimatter, Parasteel, Isolinear Chips, Bio-neural Gel Packs,
  Transparent Aluminum, Warp Coils, Plasma Conduits, Photon Warheads
- **Faction-specific:** Ketracel-white (Dominion), Nanoprobes (Borg), Bloodwine (Klingon morale), Singularity Cores (Romulan)

Full table → `data/resources.json`.

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

| Era | Years | Stardate style | Tech cap | Playable factions | Era twist |
|---|---|---|---|---|---|
| **Enterprise** | 2151–2161 | Calendar (2151.04) | Warp 5, phase cannons, polarized hull, no true shields | Earth/UEC, Vulcan, Andorian, Tellarite, Klingon, Romulan, Xindi, Suliban | Coalition-building; Temporal Cold War events |
| **Discovery / TOS** | 2256–2293 | 4-digit (1312.4) | Warp 8, phasers, photon torps, early cloaks | Federation, Klingon, Romulan, Gorn, Tholian, Orion | Dilithium scarcity; Neutral Zone outposts |
| **TNG** | 2364–2373 | 5-digit (41153.7) | Warp 9.x, Galaxy-class, industrial replicators | Fed, Klingon, Romulan, Cardassian, Ferengi, Borg (AI/crisis) | First Borg contact; Cardassian border |
| **Dominion War** | 2373–2379 | 5-digit (50xxx) | Quantum torps, ablative armor, pulse phasers | + Dominion, Breen | Massive fleets; wormhole chokepoint; runs to 2379 for the Shinzon crisis |
| **Delta Quadrant** | 2371–2378 | 5-digit | Bio-neural, slipstream research | Fed (lost ship), Kazon, Vidiian, Hirogen, Borg, 8472 | Isolated start — no supply home; scavenge |
| **Picard era** | 2380s–2402 | 5-digit (78xxx) | Transwarp research, synths | Fed, Klingon, Romulan Free State, Fenris Rangers, Borg remnants | Romulan supernova refugees; synth ban politics |
| **32nd century** | 3189+ | 6-digit (865xxx) | Programmable matter, spore drive | Federation remnant, Emerald Chain, Klingon, Ni'Var | **The Burn** — dilithium nearly gone |

Era data → `data/eras.json`.

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
| *Minor / AI* | — | Breen, Tholian, Gorn, Bajoran, Orion Syndicate, Nausicaan, Kazon, Hirogen, Species 8472, Son'a |

Full data → `data/factions.json`.

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

Tree → `data/tech_tree.json`.

## 9. Ships

**Hull + modules** design system:

- Hull defines size class, slot counts, base HP, speed, crew requirement.
- Slots: Weapon (beam / torpedo), Defense (shield / armor), Engine, Utility (sensor, cloak, tractor, transporter,
  cargo, troop, science lab, mining laser, construction).
- Each faction has **class templates** (Galaxy, Vor'cha, D'deridex, Galor…) = a hull + default module loadout.
  Players can refit within faction rules (Borg can mix anything; Ferengi can buy foreign modules).

Roles: Scout · Construction · Mining · Freighter · Colony · Science · Escort · Frigate · Destroyer · Cruiser ·
Heavy Cruiser · Explorer · Carrier · Battleship · Dreadnought · Titan (faction super-unit).

Templates → `data/ships.json`.

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

Hazard data → `data/hazards.json`.

## 10b. Buildings, Stations & Vehicles

Data → `data/buildings.json`, `data/vehicles.json`.

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
2. **Engine:** Unity (DOTS for large unit counts), Godot (open source, lighter), or Unreal (visual fidelity, heavier).
3. **Map:** two-scale lanes (recommended) vs. single seamless galaxy.
4. **Real-time vs pausable** (pause for single-player only?).
5. **Ground combat:** abstracted invasions (recommended for v1) vs. tactical ground battles.
6. **MVP scope:** suggest 1 era (TNG/Dominion War) × 3 factions (Fed, Klingon, Cardassian) × 1–2 player first.
