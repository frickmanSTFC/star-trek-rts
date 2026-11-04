"""Survey rover variants: the shared contract between the rover model (build_vehicles.py) and its finishes
(rover_finish.py). Placeholder numbers, for the owner to pick from.

Three axes:
  tier    - upgrade path from early to later exploration (geometry: build_vehicles.py --tier 1|2|3)
  name    - each rover is an individual: name + hull number painted on (--name KALTY --number 44)
  finish  - what the planet has done to it (paint bake + snow geometry: --finish clean|dust|mud|snow)
  damage  - integrity bands, shown as scuffs -> broken lamps -> wrecked (--damage 0..3)

Part names every tier keeps (where the part exists) so the game and the damage pass can find them:
  Hull, Windscreen, SideGlass{-1,1}, SideWindow{-1,1}, Headlamp{-1,1}, Spot<k>, Beacon, Antenna, Locker, LockerB,
  FrontBumper, RearBumper, Wheel{F,M,R}{L,R} (each with ...Cap / ...Rim children), Dish (the game spins it),
  Number{-1,1}, Name{-1,1}, NameRear, Flash{-1,1}. Mk III adds Drone (sits on its roof pad).
"""

TIERS = {
    1: {"mark": "Mk I", "class": "Scout", "era": "early exploration",
        "look": "Four wheels, shorter body, split flat windscreen, whip antenna + small sensor puck instead of a dish, "
                "two roof spots, roof rack with equipment cases, plain steel wheels, primer-grey panels.",
        "stats": {"speed": 2.0, "survey_radius": 2, "cost": {"duranium": 60}, "build_seconds": 20}},
    2: {"mark": "Mk II", "class": "Surveyor", "era": "established colony",
        "look": "Today's Kalty: six-wheeler, wrap-around windscreen, light bar, spinning sensor dish, lockers.",
        "stats": {"speed": 2.2, "survey_radius": 3, "cost": {"duranium": 90, "tritanium": 20}, "build_seconds": 30,
                  "requires_tech": "improved_sensors"}},
    3: {"mark": "Mk III", "class": "Explorer", "era": "deep exploration",
        "look": "Longer six-wheeler on bigger tyres and raised suspension, flush glazing, rotating sensor turret, "
                "side sensor pods, roof pad with a scout drone, shield-emitter nubs, blue running-light strip.",
        "stats": {"speed": 2.6, "survey_radius": 4, "cost": {"duranium": 120, "tritanium": 60, "dilithium": 5},
                  "build_seconds": 45, "requires_tech": "long_range_survey", "abilities": ["launch_drone"]}},
}

# Each rover gets the next name from the roster (geology words; the owner's Kalty first) and a two-digit number.
NAMES = [("KALTY", "44"), ("TALUS", "17"), ("CAIRN", "08"), ("SCREE", "62"), ("MESA", "31"), ("BASALT", "26"),
         ("MORAINE", "53"), ("TUFF", "70")]

FINISHES = {
    "clean": "Fresh out of the Vehicle Bay: light scuffs only.",
    "dust": "Desert / barren worlds: ochre dust caked low and thinning upward, wiper arcs on the windscreen.",
    "mud": "Wet / jungle worlds: dark brown splatter thrown up behind every wheel, muddy tyres.",
    "snow": "Ice / tundra worlds: snow lying on roof and flat tops, frosted glass edges, slush on tyres and arches.",
}

# Game integrity (0-100) -> damage band.
DAMAGE = {
    0: {"integrity": "75-100", "look": "None beyond the finish."},
    1: {"integrity": "50-75", "look": "Deep scratches through the paint, a dent, one cracked side window."},
    2: {"integrity": "25-50", "look": "Plus: broken (dark) spot lamps and a headlamp, bent antenna, scorch marks, "
                                       "a missing hub cap, hanging rear bumper."},
    3: {"integrity": "0-25", "look": "Plus: smashed windscreen, dish knocked askew, burnt-out rear, missing locker, "
                                      "blackened scorching over most of the hull."},
}
