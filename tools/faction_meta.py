"""Faction names, roles, resource labels and play-style summaries shared by the faction building write-ups."""

FACTIONS = ["federation", "klingon", "cardassian", "romulan", "dominion", "ferengi", "borg"]
NAMES = {"federation": "Federation", "klingon": "Klingon", "cardassian": "Cardassian", "romulan": "Romulan",
         "dominion": "Dominion", "ferengi": "Ferengi", "borg": "Borg"}
ROLES = ["core", "housing", "extraction", "power", "research", "defense", "signature"]
LINE_NAMES = {"deep_space": "Deep space", "command": "Command", "shipyards": "Shipyards",
              "habitation": "Habitation", "clones": "Clones (Dominion)", "drones": "Drones (Borg)"}
RES = {"duranium": "duranium", "tritanium": "tritanium", "parasteel": "parasteel", "crew": "crew",
       "deuterium": "deuterium", "isolinear_chips": "isolinear", "plasma_conduits": "plasma conduits",
       "warp_coils": "warp coils", "dilithium": "dilithium", "rodinium": "rodinium", "kelbonite": "kelbonite",
       "uridium": "uridium", "antimatter": "antimatter", "singularity_core": "singularity cores",
       "zenite": "zenite", "ketracel_white": "White", "latinum": "latinum", "nanoprobes": "nanoprobes",
       "honor": "Honor", "transparent_aluminum": "transp. aluminum", "topaline": "topaline"}

# Hand-written summary per faction: how its buildings play differently from the Federation baseline.
PLAYSTYLE = {
    "federation": ("Baseline", "Balanced costs; best research (+25% labs); widest habitation range (domes on any solid body); Yorktown and Utopia Planitia wonders.",
                   "No gimmick to lean on; Xenology Post risks a Prime Directive penalty."),
    "klingon": ("Honor", "Cheap opening (only Ferengi leases cost less); guns and troops from turn one; strip mines and battle reactors front-load output; Honor buys Great House fleets.",
                "Research limited to weapons at -25%; mines deplete faster; reactors explode on neighbours."),
    "cardassian": ("Occupation", "Strongest extraction (+50%); labour and occupation bonuses on populated worlds; Nor-type stations double refining.",
                   "-25% research; output bonuses raise Resistance that must be managed."),
    "romulan": ("Stealth", "Colonies, mines, guns and docks hidden from scans; spy slot from the start; singularity tech removes antimatter upkeep.",
                "Most expensive opening (needs isolinear chips early); shrouded mines yield -20%."),
    "dominion": ("Ketracel-white", "No population needed: Jem'Hadar are grown in vats; squads arrive with the core; strong polaron defence.",
                 "Everything depends on White supply; -30% research; skips cruiser hulls."),
    "ferengi": ("Latinum", "Pays to play: leases, contracts and markets earn latinum from every faction; can buy research instead of doing it.",
                "Nothing works without latinum; no garrison on the core; defence is rented."),
    "borg": ("Assimilation", "Strongest harvesting (+60%); shared power grid; adapts to weapons; gains tech by assimilating hulls and crews.",
             "Can only settle populated worlds; no research at all; harvesting degrades the planet."),
}

# Hand-written fleet doctrine per faction for the ship designs page.
SHIP_DOCTRINE = {
    "federation": "Generalists with the widest roster. Saucer separation, multi-vector assault mode and the Intrepid's planetary landing give tactical options; a cloak only by treaty (Defiant).",
    "klingon": "Raiders and boarders. Cloaked Birds-of-Prey and K'Vorts strike first, backed by disruptor cruisers; a titan is available as early as the TOS era.",
    "romulan": "Ambush fleet: cloaks on almost every hull, plasma torpedoes and singularity cores. The Scimitar adds a thalaron weapon and a perfect cloak.",
    "cardassian": "Mid-weight industrial navy: Galor cruisers backed by Hideki fighters and Groumall freighters. No titan berth, so the Keldon heavy cruiser is the largest hull.",
    "dominion": "Few classes, built in numbers: attack ships in pairs with kamikaze rams, then a battle cruiser and a battleship. Skips the cruiser tier entirely.",
    "ferengi": "One native warship, the D'Kora Marauder with its energy whip and cargo bay. The rest of the fleet is bought, or refitted from acquired foreign hulls.",
    "borg": "Geometric hulls from probe to cube that can mount any assimilated module. Cubes adapt and regenerate; the Queen's Diamond gives a command aura.",
}

# Hand-written research model per faction for the tech tree page (the opening research building's effect comes from buildings.json).
RESEARCH_MODEL = {
    "federation": ("Fastest", "Best labs in the game; every shared tech is open, but no disruptors, and the only cloak is the forbidden Phasing Cloak."),
    "klingon": ("Weapons first", "The opening Weapons Forge researches only the Weapons tree, and slower than a lab; Honor and conquest make up for it."),
    "cardassian": ("Slow", "A quarter slower than baseline; the economy comes from occupation and refining instead."),
    "romulan": ("Stealth focus", "Normal speed with a big bonus on stealth techs; the only faction with plasma torpedoes and thalaron weapons."),
    "dominion": ("Salvage", "Slow labs, but captured wrecks give double research; phased polaron beams are theirs alone."),
    "ferengi": ("Buys tech", "No research of its own: it buys techs other players already have, so it is never first but rarely far behind."),
    "borg": ("Assimilates", "No research at all: techs come from assimilating ships and crews, and transwarp conduits are Borg-only."),
}

# Hazard categories and each faction's exposure and edge, for the hazards page.
HAZARD_CATEGORIES = {
    "anomaly": ("Anomalies", "Space itself: storms, nebulae, rifts and wormholes. Most double as research sites."),
    "habitat": ("Habitat dangers", "Trouble on the ground at your colonies: natives, plagues, disasters and unrest."),
    "condition": ("World conditions", "Permanent dangers of a world's environment that every colony or outpost there has to live with."),
    "raider": ("Raiders", "Pirates and hunters that prey on lanes, freighters and your best ships."),
    "crisis": ("Crises", "Late-game entities that threaten every player at once."),
    "wildcard": ("Wild cards", "Rare events that break the rules: Q, the Mirror Universe, hidden Changelings."),
}
HAZARD_STANCE = {
    "federation": ("Pre-warp dilemmas test the Prime Directive, and the largest research economy draws Borg attention.",
                   "The best labs reach counter techs first, such as transphasic torpedoes; diplomacy builds coalition fleets against crises."),
    "klingon": ("Weak research leaves anomalies without counters, and fleeing a crisis costs Honor.",
                "Tribbles can't survive a Klingon trade partner; warriors and boarding parties crush insurgents and raiders."),
    "cardassian": ("Occupation raises Resistance, which feeds Maquis-style insurgency on its own lanes.",
                   "Surveillance grids expose Changelings and spies; heavy defense platforms hold the Badlands border."),
    "romulan": ("Singularity-core wrecks add gravity wells to every battle, and paranoia makes Changeling infiltration costly.",
                "Cloaked fleets slip past raiders, and the Phasing Cloak escapes Tholian webs."),
    "dominion": ("Raiders or a closed wormhole that cut the Ketracel-white supply make Jem'Hadar go feral.",
                 "Changeling infiltration is their own weapon, and the stable wormhole is home ground."),
    "ferengi": ("Freighter raids hit the whole economy, and crises can't be bought off.",
                "Buys off the Orion Syndicate or hires raiders against rivals; buys counter techs other players have researched."),
    "borg": ("Species 8472 targets the Collective, and with no research the Borg can't develop counters to anomalies.",
             "Adaptation shrugs off repeated attacks, and Borg nanoprobes are the one weapon Species 8472 can't ignore."),
}

# Which worlds each faction wants and the colony rules that set it apart, for the planet classes page.
WORLD_STANCE = {
    "federation": ("Class M first, then domes on K, L and H worlds; moons of gas giants for deuterium.",
                   "Pressure Domes work on any solid body. Inhabited Class M worlds raise Prime Directive choices; a Xenology Post studies natives in secret."),
    "klingon": ("Class M and harsh Class H deserts; an asteroid in system for a hidden fortress.",
                "Strip Mines yield 25% more but exhaust deposits 25% faster. The Asteroid Fortress needs an asteroid field."),
    "cardassian": ("Inhabited Class M worlds to occupy, and uridium-rich Class K and M.",
                   "Ore Extraction Sites yield 50% more, with a uridium bonus on Class M. Labor Barracks on occupied worlds give +50% output but raise Resistance."),
    "romulan": ("Class M worlds, and nebulae where cloaked ships gain a bonus.",
                "Shrouded Mines yield 20% less but stay invisible to scans and raids."),
    "dominion": ("Almost anywhere: Jem'Hadar are grown in vats, so habitability barely matters.",
                 "No native population growth. Birthing chambers need zenite, so Class L worlds matter."),
    "ferengi": ("Whatever pays: trade crossroads and rich deposits it can lease out.",
                "Mining Concessions cost nothing up front, and the mining rights can be sold to another player."),
    "borg": ("Populated worlds only, which means Class M worlds with natives.",
             "The Assimilation Spire needs a populated world. Strip Harvesters yield 60% more but degrade the planet class over time."),
}
