# Faction buildings: how each faction differs

Generated from `data/buildings.json` by `tools/gen_faction_buildings_doc.py`; re-run it after changing the data. All costs and stats are placeholders for balancing.

The Federation is the baseline. Every other faction fills the same roles but trades cost, output or rules to fit its identity.

## 1. Faction overview

| Faction | Economy hook | Identity | Strengths | Trade-offs |
|---|---|---|---|---|
| **Federation** | Baseline | Balanced baseline; fastest research. | Balanced costs; best research (+25% labs); widest habitation range (domes on any solid body); Yorktown and Utopia Planitia wonders. | No gimmick to lean on; Xenology Post risks a Prime Directive penalty. |
| **Klingon** | Honor | Cheap and aggressive; weak economy and research. | Cheap opening (only Ferengi leases cost less); guns and troops from turn one; strip mines and battle reactors front-load output; Honor buys Great House fleets. | Research limited to weapons at -25%; mines deplete faster; reactors explode on neighbours. |
| **Cardassian** | Occupation | Industry and control; slow research. | Strongest extraction (+50%); labour and occupation bonuses on populated worlds; Nor-type stations double refining. | -25% research; output bonuses raise Resistance that must be managed. |
| **Romulan** | Stealth | Hidden and paranoid; pricier. | Colonies, mines, guns and docks hidden from scans; spy slot from the start; singularity tech removes antimatter upkeep. | Most expensive opening (needs isolinear chips early); shrouded mines yield -20%. |
| **Dominion** | Ketracel-white | Clones and Ketracel-white; no native population growth. | No population needed: Jem'Hadar are grown in vats; squads arrive with the core; strong polaron defence. | Everything depends on White supply; -30% research; skips cruiser hulls. |
| **Ferengi** | Latinum | Everything costs latinum; everything earns it. | Pays to play: leases, contracts and markets earn latinum from every faction; can buy research instead of doing it. | Nothing works without latinum; no garrison on the core; defence is rented. |
| **Borg** | Assimilation | Can only take worlds that are already inhabited; no research. | Strongest harvesting (+60%); shared power grid; adapts to weapons; gains tech by assimilating hulls and crews. | Can only settle populated worlds; no research at all; harvesting degrades the planet. |

## 2. Starting buildings on a Class M colony (at a glance)

One building per role is available in the opening minutes. Names only; costs and effects follow in section 3.

| Role | Federation | Klingon | Cardassian | Romulan | Dominion | Ferengi | Borg |
|---|---|---|---|---|---|---|---|
| **Core** | Colony Hub | House Encampment | Garrison Post | Colonial Garrison | Vorta Field Post | Trade Office | Assimilation Spire |
| **Housing** | Prefab Habitat | Clan Longhouse | Labor Barracks | Colonial Villa | Birthing Chamber | Rental Habitat | Alcove Block |
| **Extraction** | Mining Complex | Strip Mine | Ore Extraction Site | Shrouded Mine | Jem'Hadar Work Site | Mining Concession | Strip Harvester |
| **Power** | Fusion Reactor | Battle Reactor | Fusion Reactor | Plasma Reactor | Fusion Reactor | Leased Reactor | Collective Power Node |
| **Research** | Research Lab | Weapons Forge | State Archive | Science Ministry Lab | Vorta Analysis Lab | Patent Exchange | None |
| **Defense** | Security Garrison | Disruptor Battery | Surveillance Grid | Cloaked Disruptor Emplacement | Polaron Battery | Mercenary Contract Board | Adaptive Shield Node |
| **Signature** | Xenology Observation Post | Warrior's Arena | Occupation Office | Tal Shiar Field Office | White Depot | Bar & Holosuites | Nanoprobe Beacon |
| *Opening cost (total units)* | *700* | *630* | *680* | *760* | *712* | *480* | *585* |

Total units add every resource together (duranium, crew, latinum, and so on). They're only a rough guide to how expensive each opening is.

## 3. Starting buildings by role

### Core

| Faction | Building | Cost | vs Federation cost | Effect | Requires |
|---|---|---|---|---|---|
| Federation | Colony Hub | 100 duranium, 50 crew | baseline | Supply range 1, 1 build queue. |  |
| Klingon | House Encampment | 90 duranium, 60 crew | same | Trains Klingon Warriors immediately. |  |
| Cardassian | Garrison Post | 110 duranium, 40 crew | same | Includes a garrison; no separate defense building needed. |  |
| Romulan | Colonial Garrison | 110 duranium, 40 crew | same | Enemy scans see no colony details. |  |
| Dominion | Vorta Field Post | 110 duranium, 2 White | -38 | Comes with a Jem'Hadar squad. |  |
| Ferengi | Trade Office | 90 duranium, 30 latinum | -30 | +2 Latinum per cycle; no garrison. |  |
| Borg | Assimilation Spire | 100 duranium, 20 nanoprobes | -30 | Converts natives or captured population into drones; +5 nanoprobes per cycle. | populated world |

### Housing

| Faction | Building | Cost | vs Federation cost | Effect | Requires |
|---|---|---|---|---|---|
| Federation | Prefab Habitat | 60 duranium | baseline | +1 population. |  |
| Klingon | Clan Longhouse | 70 duranium | +10 | +1 population, +1 militia, +1 Bloodwine per cycle. |  |
| Cardassian | Labor Barracks | 60 duranium | same | +1 population. |  |
| Romulan | Colonial Villa | 70 duranium | +10 | +1 population, +1 morale. |  |
| Dominion | Birthing Chamber | 90 duranium, 20 zenite | +50 | Grows Jem'Hadar squads instead of population. |  |
| Ferengi | Rental Habitat | 60 duranium | same | +1 population; adjustable rent. |  |
| Borg | Alcove Block | 80 duranium, 10 nanoprobes | +30 | Stores drones, not population. |  |

### Extraction

| Faction | Building | Cost | vs Federation cost | Effect | Requires |
|---|---|---|---|---|---|
| Federation | Mining Complex | 80 duranium | baseline | Normal yield. |  |
| Klingon | Strip Mine | 60 duranium | -20 | +25% yield; deposit depletes 25% faster. |  |
| Cardassian | Ore Extraction Site | 100 duranium | +20 | +50% yield; uridium bonus on Class M. |  |
| Romulan | Shrouded Mine | 100 duranium | +20 | -20% yield; invisible to scans and raids. |  |
| Dominion | Jem'Hadar Work Site | 80 duranium | same | No crew needed. |  |
| Ferengi | Mining Concession | free | -80 | Leased, no upfront cost; rights can be sold to another player. |  |
| Borg | Strip Harvester | 100 duranium | +20 | +60% yield; planet class degrades over time. |  |

### Power

| Faction | Building | Cost | vs Federation cost | Effect | Requires |
|---|---|---|---|---|---|
| Federation | Fusion Reactor | 80 duranium, 20 deuterium | baseline | +10 energy. |  |
| Klingon | Battle Reactor | 60 duranium, 20 deuterium | -20 | +10 energy; explodes and damages neighbours if destroyed. |  |
| Cardassian | Fusion Reactor | 80 duranium, 20 deuterium | same | +10 energy. |  |
| Romulan | Plasma Reactor | 90 duranium, 10 isolinear | same | +12 energy; no deuterium. |  |
| Dominion | Fusion Reactor | 80 duranium, 20 deuterium | same | +10 energy. |  |
| Ferengi | Leased Reactor | 30 latinum | -70 | +10 energy; paid in latinum. |  |
| Borg | Collective Power Node | 80 duranium, 5 nanoprobes | -15 | Energy shared across every Borg colony. |  |

### Research

| Faction | Building | Cost | vs Federation cost | Effect | Requires |
|---|---|---|---|---|---|
| Federation | Research Lab | 100 duranium | baseline | +25% research (best in game). |  |
| Klingon | Weapons Forge | 80 duranium | -20 | Weapons-tree research only, at -25%. |  |
| Cardassian | State Archive | 120 duranium | +20 | -25% research. |  |
| Romulan | Science Ministry Lab | 110 duranium, 10 isolinear | +20 | Normal research; +50% on stealth techs. |  |
| Dominion | Vorta Analysis Lab | 120 duranium | +20 | -30% research; double research from captured wrecks. |  |
| Ferengi | Patent Exchange | 80 duranium, 40 latinum | +20 | No own research; buy techs other players already have. |  |
| Borg | None | free | -100 | No research; tech comes from assimilation. |  |

### Defense

| Faction | Building | Cost | vs Federation cost | Effect | Requires |
|---|---|---|---|---|---|
| Federation | Security Garrison | 100 duranium, 50 crew | baseline | 1 security team. |  |
| Klingon | Disruptor Battery | 110 tritanium | -40 | Surface-to-orbit gun from turn one. |  |
| Cardassian | Surveillance Grid | 60 duranium, 10 isolinear | -80 | Reveals spies; -1 Resistance. |  |
| Romulan | Cloaked Disruptor Emplacement | 120 tritanium | -30 | Invisible until it fires. |  |
| Dominion | Polaron Battery | 110 tritanium | -40 | Partly bypasses shields. |  |
| Ferengi | Mercenary Contract Board | 50 latinum | -100 | Defenders hired on demand, paid per use. |  |
| Borg | Adaptive Shield Node | 120 duranium | -30 | Adapts to whatever weapon hits the colony. |  |

### Signature

| Faction | Building | Cost | vs Federation cost | Effect | Requires |
|---|---|---|---|---|---|
| Federation | Xenology Observation Post | 60 duranium | baseline | Hidden study of natives: research bonus; Prime Directive penalty if discovered. | natives present |
| Klingon | Warrior's Arena | 80 duranium | +20 | +1 Honor per cycle; ground troop XP. |  |
| Cardassian | Occupation Office | 80 duranium | +20 | Natives put to work: +output, rising Resistance. | natives present |
| Romulan | Tal Shiar Field Office | 80 duranium, 20 isolinear | +40 | 1 spy operation slot from the start. |  |
| Dominion | White Depot | 60 duranium, 20 zenite | +20 | Keeps troops supplied 2 cycles if White supply is cut. |  |
| Ferengi | Bar & Holosuites | 80 duranium, 20 latinum | +40 | +morale, +Latinum; gossip reveals nearby enemy plans. |  |
| Borg | Nanoprobe Beacon | 60 duranium, 10 nanoprobes | +10 | Speeds up turning natives into drones. | natives present |

## 4. Upgrade lines: level 1 to max

Each line upgrades in place: a new level replaces the one before it. Seven factions have four lines each, 94 levels in all. The fourth line is Habitation for most factions, Clones for the Dominion and Drones for the Borg.

### At a glance

| Faction | Deep space | Command | Shipyards | Habitation / Clones / Drones |
|---|---|---|---|---|
| **Federation** | Outpost → Deep Space Station → Starbase → Spacedock → Yorktown (5) | Colony Hub → Settlement Hub → Colonial Administration → Governor's Complex → Planetary Capital (5) | Orbital Drydock → Orbital Shipyard → Fleet Yards (3) | Prefab Habitat → Pressure Domes → Arcology (3) |
| **Klingon** | Border Outpost → Battle Station → Klingon Starbase → Asteroid Fortress (4) | House Encampment → Governor's Keep → Great Hall (3) | Cradle → Imperial Shipyard → Imperial Fleet Yards (3) | Clan Longhouse → Fortified Settlement → Warrior Citadel (3) |
| **Cardassian** | Outpost → Monitoring Station → Nor-type Station → Nor-type Bastion (4) | Garrison Post → Prefecture → Central Command (3) | Rib Dock → Fleet Shipyard → Assembly Yards (3) | Labor Barracks → Civic Arcade → Capital District (3) |
| **Romulan** | Cloaked Outpost → Listening Post → Warbird Aerie → Singularity Bastion (4) | Colonial Garrison → Proconsulate → Imperial Senate (3) | Aerie Dock → Imperial Shipyard → Singularity Yards (3) | Colonial Villa → Terraced Quarter → Spire City (3) |
| **Dominion** | Jem'Hadar Outpost → Ketracel-white Facility → Vorta Command Station → Wormhole Citadel (4) | Vorta Field Post → Vorta Administration → Founders' Sanctuary (3) | Attack Ship Cradle → Dominion Shipyard → Stellar Forge (3) | Birthing Chamber → Cloning Facility → Cloning Complex (3) |
| **Ferengi** | Trading Post → Bazaar Station → Marauder Station → Grand Exchange (4) | Trade Office → Commerce Authority Office → Tower of Commerce (3) | Shuttle Dock → Marauder Yard → Acquisition Yards (3) | Rental Habitat → Market Quarter → Resort Spire (3) |
| **Borg** | Probe Node → Interlink Node → Vinculum Station → Transwarp Hub (4) | Assimilation Spire → Planetary Hive → Unicomplex (3) | Assembly Matrix → Sphere Matrix → Cube Matrix (3) | Alcove Block → Maturation Chamber → Assimilation Complex (3) |

The number in brackets is how many levels the line has. The Federation command line has 5 levels; it's the one with detailed art in `art/federation-colony-hub/`.

### Deep space

| Faction | Level | Building | Cost | Stats | Effect |
|---|---|---|---|---|---|
| Federation | L1 | Outpost | 120 duranium, 10 rodinium | supply range 1; docking 0 | Claims the system. |
|  | L2 | Deep Space Station | 300 parasteel | supply range 2; docking 4 | K-7 style trade hub. |
|  | L3 | Starbase | 600 parasteel, 50 isolinear, 40 photon_warheads | supply range 3; docking 8 | Saucer command deck, fleet aura. |
|  | L4 | Spacedock | 1200 parasteel, 150 plasma conduits | hull cap 6; docking 16 | Interior bay; builds size-6 hulls. |
|  | L5 | Yorktown *(wonder)* | 4000 parasteel, 300 isolinear, 200 dilithium | docking 24 | Habitable rings inside a transparent shell. |
| Klingon | L1 | Border Outpost | 140 duranium, 10 rodinium | supply range 1; weapons 2 | Spear tower armed with 2 disruptors from day one. |
|  | L2 | Battle Station | 320 parasteel | supply range 2; docking 3 | Armored hub with wing arms; cloaked ships rearm without decloaking. |
|  | L3 | Klingon Starbase | 600 parasteel, 40 isolinear, 30 photon_warheads | supply range 3; docking 6 | War platform with spires; fleet aura, Honor for kills nearby. |
|  | L4 | Asteroid Fortress *(faction only)* | 1000 parasteel, 80 kelbonite | hidden hangars 8; requires asteroid_in_system | Ty'Gokor style hollowed asteroid; kelbonite hides hangars from scans. |
| Cardassian | L1 | Outpost | 120 duranium, 10 rodinium | supply range 1 | Ribbed spindle; +1 system sensor range. |
|  | L2 | Monitoring Station | 280 parasteel, 20 isolinear | supply range 2; docking 2 | Oval hub; reveals fleet movement in neighbouring systems. |
|  | L3 | Nor-type Station *(faction only)* | 700 parasteel, 60 uridium | docking 12 | Terok Nor layout; x2 refined output in system. |
|  | L4 | Nor-type Bastion *(faction only)* | 1100 parasteel, 80 antimatter, 40 photon_warheads | weapons 10 | War refit: 6 arrays + 4 weapons platforms; keeps refining bonus. |
| Romulan | L1 | Cloaked Outpost | 130 duranium, 20 isolinear | supply range 1 | Hidden from enemy maps until it fires. |
|  | L2 | Listening Post | 260 parasteel, 40 isolinear | supply range 2 | Neutral Zone sensor wings; intel 2 systems out. |
|  | L3 | Warbird Aerie | 620 parasteel, 60 isolinear | supply range 3; docking 6 | Hawk-shaped starbase; ships launch cloaked. |
|  | L4 | Singularity Bastion *(faction only)* | 1200 parasteel, 2 singularity cores |  | No antimatter upkeep in range; gravity well if destroyed. |
| Dominion | L1 | Jem'Hadar Outpost | 130 duranium | supply range 1 | No crew upkeep; costs 1 White per cycle. |
|  | L2 | Ketracel-white Facility *(faction only)* | 300 parasteel, 30 zenite | supply range 2 | +6 White/cycle; losing it makes nearby Jem'Hadar feral. |
|  | L3 | Vorta Command Station | 600 parasteel, 50 isolinear | supply range 3; docking 8 | Fleet aura; Vorta diplomacy with neighbours. |
|  | L4 | Wormhole Citadel *(faction only)* | 1400 parasteel, 100 isolinear | requires stable_wormhole | On a stable wormhole; free reinforcement wave every 10 min. |
| Ferengi | L1 | Trading Post | 100 duranium, 20 latinum | supply range 1 | +1 Latinum per passing freighter, any faction. |
|  | L2 | Bazaar Station | 280 parasteel, 60 latinum | docking 6 | 10% cut of all trades docked here; sells repairs. |
|  | L3 | Marauder Station | 560 parasteel, 150 latinum | supply range 3 | Horseshoe starbase; mercenary contracts. |
|  | L4 | Grand Exchange *(wonder)* | 2000 parasteel, 1000 latinum |  | Sets market prices for all players; attack = market crash. |
| Borg | L1 | Probe Node | 120 duranium, 10 nanoprobes | supply range 1 | Full system vision; records enemy weapons. |
|  | L2 | Interlink Node | 300 parasteel, 30 nanoprobes | supply range 2 | Adaptations shared with all Borg units in range. |
|  | L3 | Vinculum Station *(faction only)* | 650 parasteel, 60 nanoprobes | supply range 3 | Fast regeneration; destroyed = drones in range offline. |
|  | L4 | Transwarp Hub *(faction only)* | 1400 parasteel, 120 nanoprobes | apertures 3 | Instant hub-to-hub travel; capturable. |

### Command

| Faction | Level | Building | Cost | Stats | Effect |
|---|---|---|---|---|---|
| Federation | L1 | Colony Hub | 100 duranium, 50 crew | supply range 1; build queues 1; pop cap 2 | Colony pod set down by a drop ship: landing legs, boarding ramp, solar petals, drop capsules. |
|  | L2 | Settlement Hub | 160 duranium, 60 tritanium | supply range 1; build queues 1; pop cap 3 | Pod settles on a foundation; four capsule modules plug in on tubes; hydroponics dome; +25% storage, +10% build speed. |
|  | L3 | Colonial Administration | 300 duranium, 150 tritanium | supply range 2; build queues 2; pop cap 4 | Pod raised on a central column above a ring corridor of six modules; domed rotunda; comm tower. |
|  | L4 | Governor's Complex | 280 parasteel, 30 isolinear | supply range 2; build queues 3; pop cap 6 | Column carries a starbase-style saucer deck; three-storey ring modules; second saucer building; +1 morale in system. |
|  | L5 | Planetary Capital | 400 parasteel, 50 isolinear | supply range 3; build queues 3; pop cap 10; limit 1 per system | Spire of three saucer decks, ring of saucer-topped towers, Academy-style arches, pod monument; +2 morale in system. |
| Klingon | L1 | House Encampment | 90 duranium, 60 crew | supply range 1 | Shelters, brazier, House banner; trains warriors. |
|  | L2 | Governor's Keep | 280 duranium, 160 tritanium | supply range 2 | Walled keep; +50% garrison defense, 2nd build queue. |
|  | L3 | Great Hall *(faction only)* | 450 parasteel, 100 Honor, 50 bloodwine | supply range 3 | High Council seat; spend Honor for Great House fleets. |
| Cardassian | L1 | Garrison Post | 110 duranium, 40 crew | supply range 1 | Ribbed bunker; trains Cardassian soldiers. |
|  | L2 | Prefecture | 300 duranium, 140 tritanium | supply range 2 | Governs occupied worlds; -1 Resistance. |
|  | L3 | Central Command *(faction only)* | 450 parasteel, 60 isolinear | supply range 3 | Unlocks Obsidian Order and Union occupation policies. |
| Romulan | L1 | Colonial Garrison | 110 duranium, 40 crew | supply range 1 | Enemy scans see no colony details. |
|  | L2 | Proconsulate | 300 duranium, 140 tritanium | supply range 2; tal shiar slots 1 | 2nd build queue, 1 Tal Shiar op slot. |
|  | L3 | Imperial Senate *(faction only)* | 450 parasteel, 80 isolinear | supply range 3; tal shiar slots 3 | Unlocks Tal Shiar HQ and Senate politics (bribable senators). |
| Dominion | L1 | Vorta Field Post | 110 duranium, 2 White | supply range 1 | Vorta supervisor + Jem'Hadar squad; clone replaces a killed Vorta. |
|  | L2 | Vorta Administration | 300 duranium, 140 tritanium | supply range 2 | Client-world pacts with minor factions. |
|  | L3 | Founders' Sanctuary *(faction only)* | 500 parasteel, 60 isolinear | supply range 3 | The Great Link; trains Changeling infiltrators, loyalty aura. |
| Ferengi | L1 | Trade Office | 90 duranium, 30 latinum | supply range 1 | +2 Latinum; no garrison. |
|  | L2 | Commerce Authority Office | 260 duranium, 120 latinum | supply range 2 | Sells trade licences; 1 Rule of Acquisition perk. |
|  | L3 | Tower of Commerce *(faction only)* | 400 parasteel, 500 latinum | supply range 3 | All Rules perks; bribe AI factions and rival governors. |
| Borg | L1 | Assimilation Spire | 100 duranium, 20 nanoprobes | requires populated_world | Only on populated worlds; converts pop to drones; +5 nanoprobes per cycle. |
|  | L2 | Planetary Hive | 320 parasteel, 40 nanoprobes | supply range 2 | No morale/unrest; self-repairing. |
|  | L3 | Unicomplex *(faction only)* | 2000 parasteel, 200 nanoprobes |  | Match-long adaptation memory; Borg Queen; loss wipes adaptations. |

### Shipyards

| Faction | Level | Building | Cost | Stats | Effect |
|---|---|---|---|---|---|
| Federation | L1 | Orbital Drydock | 200 duranium, 100 tritanium | hull cap 3; build slots 1 | Open frame; escorts and frigates. |
|  | L2 | Orbital Shipyard | 300 parasteel, 40 plasma conduits | hull cap 5; build slots 2 | Double frame with cranes; cruisers. |
|  | L3 | Fleet Yards *(wonder)* | 3000 parasteel, 200 warp coils | hull cap titan | Utopia Planitia; titan berth, -20% build time. |
| Klingon | L1 | Cradle | 180 duranium, 100 tritanium | hull cap 3; build slots 1 | Claw cradle; -15% build time on escorts. |
|  | L2 | Imperial Shipyard | 300 parasteel, 40 plasma conduits | hull cap 5; build slots 2 | Three claw pairs; battlecruisers. |
|  | L3 | Imperial Fleet Yards *(wonder)* | 2800 parasteel, 200 warp coils | hull cap titan | Titan berth (Negh'Var); Great House sponsorship cuts cost 30%. |
| Cardassian | L1 | Rib Dock | 200 duranium, 100 tritanium | hull cap 3; build slots 1 | Hideki, Groumall; cheaper with a Nor-type in system. |
|  | L2 | Fleet Shipyard | 300 parasteel, 40 plasma conduits | hull cap 5; build slots 2 | Galor and Keldon. |
|  | L3 | Assembly Yards *(wonder)* | 2400 parasteel, 120 uridium | hull cap 5; build slots 3 | 3 parallel lines, cruisers -25%; no titan berth. |
| Romulan | L1 | Aerie Dock | 200 duranium, 30 isolinear | hull cap 3 | Half-cloaked arches; hidden build queue. |
|  | L2 | Imperial Shipyard | 320 parasteel, 40 plasma conduits | hull cap 5; build slots 2 | Valdore-class cruisers. |
|  | L3 | Singularity Yards *(wonder)* | 3000 parasteel, 4 singularity cores | hull cap titan | D'deridex and (late Dominion War era) Scimitar; all hulls launch cloaked. |
| Dominion | L1 | Attack Ship Cradle | 200 duranium, 100 tritanium | hull cap 3; build slots 2 | Attack ships built in pairs, crewed from vats. |
|  | L2 | Dominion Shipyard | 500 parasteel, 60 plasma conduits | hull cap 6; build slots 1 | Skips cruisers; Jem'Hadar battle cruiser. |
|  | L3 | Stellar Forge *(wonder)* | 2800 parasteel, 200 warp coils | hull cap titan | Star-powered; Jem'Hadar battleship; no energy upkeep. |
| Ferengi | L1 | Shuttle Dock | 180 duranium, 40 latinum | hull cap 3 | Shuttles, freighters (+25% cargo), small raiders. |
|  | L2 | Marauder Yard | 280 parasteel, 100 latinum | hull cap 4; build slots 2 | D'Kora Marauder. |
|  | L3 | Acquisition Yards *(wonder)* | 1800 parasteel, 800 latinum | hull cap any_acquired; build slots 3 | Refit any acquired foreign hull; no cap. |
| Borg | L1 | Assembly Matrix | 180 duranium, 20 nanoprobes | hull cap 2 | Probes; wrecks in system return resources. |
|  | L2 | Sphere Matrix | 360 parasteel, 40 nanoprobes | hull cap 5 | Spheres; can mount any assimilated tech. |
|  | L3 | Cube Matrix *(wonder)* | 3000 parasteel, 250 nanoprobes | hull cap titan | Cube, Tactical Cube, Queen's Diamond (with Unicomplex). |

### Habitation, Clones and Drones

| Faction | Line | Level | Building | Cost | Stats | Effect |
|---|---|---|---|---|---|---|
| Federation | habitation | L1 | Prefab Habitat | 60 duranium | pop cap 1 | Sealed modules on Class K/M/L/H. |
|  |  | L2 | Pressure Domes | 40 transp. aluminum, 10 topaline | pop cap 2 | Domes on any solid body; breach risk. |
|  |  | L3 | Arcology | 300 parasteel | pop cap 8 | Terraced megastructure; needs industrial replicators. |
| Klingon | habitation | L1 | Clan Longhouse | 70 duranium | pop cap 1 | Stone hall; +1 militia strength; +1 Bloodwine per cycle. |
|  |  | L2 | Fortified Settlement | 160 duranium, 60 tritanium | pop cap 3 | Walled compound; +2 vs invasion. |
|  |  | L3 | Warrior Citadel | 320 parasteel, 60 rodinium | pop cap 7 | Crag city; trains elite warriors, hardest to invade. |
| Cardassian | habitation | L1 | Labor Barracks | 60 duranium | pop cap 1 | On occupied worlds: +50% output, rising Resistance. |
|  |  | L2 | Civic Arcade | 150 duranium, 40 transp. aluminum | pop cap 3 | Broadcast screens; spies found faster. |
|  |  | L3 | Capital District | 320 parasteel, 40 uridium | pop cap 7 | Monument city; -1 Resistance in system. |
| Romulan | habitation | L1 | Colonial Villa | 70 duranium | pop cap 1 | Courtyard homes; +1 morale. |
|  |  | L2 | Terraced Quarter | 170 duranium, 40 tritanium | pop cap 3 | Informant network; spies and Changelings found faster. |
|  |  | L3 | Spire City | 320 parasteel, 50 isolinear | pop cap 7 | Ki Baratan style; +2 research, system-wide informants. |
| Dominion | clones | L1 | Birthing Chamber | 90 duranium, 20 zenite | squads per cycle 1 | 1 Jem'Hadar squad/cycle, no population needed. |
|  |  | L2 | Cloning Facility | 260 parasteel, 40 zenite | squads per cycle 3 | 3 squads/cycle; Vorta revival. |
|  |  | L3 | Cloning Complex *(faction only)* | 600 parasteel, 80 zenite | squads per cycle 8 | 8 squads/cycle, instant crews in system; huge White demand. |
| Ferengi | habitation | L1 | Rental Habitat | 60 duranium | pop cap 1 | Adjustable rent: latinum vs morale. |
|  |  | L2 | Market Quarter | 150 duranium, 60 latinum, 20 cormaline | pop cap 3 | Rent + 10% of sales; luxury goods. |
|  |  | L3 | Resort Spire | 300 parasteel, 150 latinum | pop cap 6 | Holosuites + dabo; +3 Latinum per trade partner. |
| Borg | drones | L1 | Alcove Block | 80 duranium, 10 nanoprobes | drone storage 10 | Stores and regenerates 10 drones. |
|  |  | L2 | Maturation Chamber | 240 parasteel, 40 nanoprobes | drones per cycle 3 | +3 drones/cycle, +25% adaptation speed. |
|  |  | L3 | Assimilation Complex *(faction only)* | 700 parasteel, 80 nanoprobes |  | Captured crews -> drones; each new hull type -> tech. |

## 5. Capability comparison

Highest value each faction reaches in its upgrade lines. A dash means the data doesn't list that stat for the faction.

| Faction | Max supply range (command) | Max docking (deep space) | Largest hull | Max pop per building | Research in the opening |
|---|---|---|---|---|---|
| **Federation** | 3 | 24 | titan | 8 | +25% research (best in game). |
| **Klingon** | 3 | 6 | titan | 7 | Weapons-tree research only, at -25%. |
| **Cardassian** | 3 | 12 | 5 | 7 | -25% research. |
| **Romulan** | 3 | 6 | titan | 7 | Normal research; +50% on stealth techs. |
| **Dominion** | 3 | 8 | titan | — (uses clones) | -30% research; double research from captured wrecks. |
| **Ferengi** | 3 | 6 | any acquired hull | 6 | No own research; buy techs other players already have. |
| **Borg** | 2 | — | titan | — (uses drones) | No research; tech comes from assimilation. |

## 6. Wonders and faction-only buildings

| Faction | Wonders | Faction-only |
|---|---|---|
| **Federation** | Yorktown, Fleet Yards | — |
| **Klingon** | Imperial Fleet Yards | Asteroid Fortress, Great Hall |
| **Cardassian** | Assembly Yards | Nor-type Station, Nor-type Bastion, Central Command |
| **Romulan** | Singularity Yards | Singularity Bastion, Imperial Senate |
| **Dominion** | Stellar Forge | Ketracel-white Facility, Wormhole Citadel, Founders' Sanctuary, Cloning Complex |
| **Ferengi** | Grand Exchange, Acquisition Yards | Tower of Commerce |
| **Borg** | Cube Matrix | Vinculum Station, Transwarp Hub, Unicomplex, Assimilation Complex |

Wonders are one-per-player megastructures. Faction-only buildings have no equivalent for other factions.
