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
