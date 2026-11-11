# Drones, Drone Pad, relay satellites, mission props (models)

Contract: `docs/missions-contract.md` section B "Models (M, …)". Four builders (bpy 5.x, see CLAUDE.md "Blender here"),
all original designs (no canon). Shared helpers in `blender/glb_check.py` (`verify_glb` re-imports an export and lists
its node tree, failing on a missing or renamed part; `join_pivots` = the satellite join made safe for nested pivots;
`prune_empties`; `contact_sheet` for the `--sheet` branches, system `python3` + PIL).

Frame everywhere: Blender +Z up, +Y = front (Godot **−Z**), 1 unit = 1 world unit; Blender (x, y, z) → Godot
(x, z, −y). Every driven pivot is **unrotated** at rest, so Godot angles go straight on.

| glb | script | size | tris | file | nodes |
|---|---|---|---|---|---|
| `game/models/drones/scout_drone.glb` | `build_drone.py` | 2.56 across the rotor tips, 2.0 long, 0.95 tall | 8.4k | 0.52 MB | 23 |
| `game/models/buildings/drone_pad.glb` | `build_drone_pad.py` | pad r 6.5 (skirt 6.68), hangar 5.2 × 3.0, vault top 3.8, mast + whip 7.8 | 8.7k | 0.86 MB | 44 |
| `game/models/orbit/relay_satellite_prewarp.glb` | `build_relay_satellite.py` | 3.13 × 1.96 × 1.38 (Godot x × y × z) | 9.8k | 0.79 MB | 12 |
| `game/models/orbit/relay_satellite.glb` | `build_relay_satellite.py` | 3.07 × 1.26 × 1.24 | 10.2k | 0.70 MB | 12 |
| `game/models/missions/mission_props.glb` | `build_mission_props.py` | four props, each r ≤ 4.6 on the origin | 5.4k | 0.56 MB | 7 |

Run: `/home/user/.bpyvenv/bin/python blender/<script> --game` (5–15 s each, the export is re-imported and checked),
`--preview blender/previews/<drones|drone_pad|relay_satellite|missions>` (Cycles, 2–8 min each), then
`python3 blender/<script> --sheet <same folder>` for the contact sheet (`scout_drone-sheet.png`, `drone_pad-sheet.png`,
`relay-sheet.png`, `mission-sheet.png`).

## Scout drone (`scout_drone.glb`)

Dawn of Warp, Earth-built quad tilt-rotor in the pre-warp drop ships' family: white lifting fuselage with a black-tiled
nose cap and orange cheat lines, gold-foil avionics hatch, satcom dome, inverted V-tail with orange tips, four white
booms to tilting nacelles with three-blade carbon rotors (orange tips), a two-axis camera gimbal under the nose, tube
skids. **Origin = the ground under the skids**, so a drone placed on a pad's `Bay` empty is landed.

| node | parent | drive |
|---|---|---|
| `Body` | root | static |
| `Arm0..3` | root | 0 front-left, 1 front-right, 2 rear-left, 3 rear-right; rest = deployed. Optional fold about local Y: Arm0/Arm3 by −a, Arm1/Arm2 by +a, a 0..45° |
| `Tilt0..3` (extra) | Arm k | nacelles, about local X: −a tips the rotor forward (cruise), 0 = hover, to −80° |
| `Rotor0..3` | Tilt k | spin about local Y; spin them in place (lifting them out leaves them behind on a fold/tilt). Rotors 0/3 and 1/2 counter-rotate (blade pitch) |
| `Camera` | root | gimbal ball, looks along local −Y (down) at rest; `Lens` is its child (material "Lens") |
| `Beacon` | root | material "Beacon" (red, strength 6) |
| `NavL` / `NavR` | root | materials **"NavRed" / "NavGreen"** (strength 3) |
| `Strobe` | root | material "Strobe" (exported at 1; the game flashes it) |
| `Skid` | root | pivot at the body underside: scale Y 1 = down, ~0.35 = tucked |
| `NameL` / `NameR` | root | call-sign decal markers (text along local +X facing local +Z; Godot scale (width, 1, height)) |

Materials: "Owner" band round the aft fuselage, "WhitePaint" hull (achievement paint matches `*Paint`), procedural
paints baked into one 512² JPEG. At the game camera (zoom 50, cruise 14) it is a small speck (`scout_drone-game.png`):
the view may want to draw it ×1.3–1.5 like the exocomps (×1.25).

## Drone Pad (`drone_pad.glb`)

Building kit conventions (`building_kit.py`): round concrete pad r 6.5 with an Owner rim band and hazard ring, two raised
landing decks r 1.75 (top 0.55) left and right of centre in front, each with a touchdown ring + cross, a copper
induction plate, a hazard edge and eight flush ring lights; a charger arm on the outer side of each deck; at the back a
small hangar/ops box (roll-up door, ops window, status board, spare-drone rack and bench inside, barrel-vault roof with
vents and skylight, Owner eaves); comms lattice mast at the back right with a tracking dish and the beacon on a whip;
battery cabinets + transformer at the back left, cable trenches, windsock, floodlight poles, fire point, cart.

| node | Godot position | drive |
|---|---|---|
| `Bay0`, `Bay1` | (∓2.75, 0.55, 1.3) | empties at the deck centres on the deck top, local +Y up: put the drone's origin here |
| `Charger0`, `Charger1` (+ `Charger<k>Body`) | (∓4.85, 2.05, 1.3) | rest = stowed along Godot −Z (back). Over the docked drone: Charger0 `RotateY(−90°)`, Charger1 `RotateY(+90°)` (a 0..90). The head hangs over the bay centre, bottom at y 1.6 (~0.15 over the drone's top) |
| `ChargeLight0/1` | children of the arms | material "ChargeLight" (green-white, strength 3): on while charging |
| `Door` (+ `DoorBody`) | (0, 2.62, −2.29) | roll-up door, pivot at the top of the opening: scale Y 1 shut → ~0.1 open |
| `Dish` (+ `DishBody`) | (4.45, 6.55, −3.1) | rest aims front-up; `RotateY` = tracking |
| `Beacon` | (4.63, 7.72, −3.28) | material "Beacon" |
| `PadLight0`, `PadLight1` | under Build1 | separate meshes, material "PadLight" (white-amber, strength 4): set per bay state |
| `Status` | material | ops board + two per-bay lamps beside the door |
| `SignFront` | over the door | "DRONE PAD nn"; `LabelBay0/1` lie flat on the deck fronts ("1", "2") |
| `PadLamp`, `HangarLight` | | OmniLight spots (over the decks; inside the door) |
| `AddOnSpot0..4`, `RoofSpot0..1` | | variety spots |

Build pieces `Build0..7` = Pad, Decks, Chargers, Hangar, Roof, Utilities, Mast, Lights; one 1024² grime atlas over the
kit materials (+ "Deck", "Label", "SignalOrange"); other emissive names: "Window", "Lamp", "Beacon" (light poles too).

## Relay satellites (`relay_satellite_prewarp.glb`, `relay_satellite.glb`)

Sized like the survey satellites for the orbit view; same axes (zenith = Godot +Y toward the ship, nadir = −Y toward
the planet, where the big dish looks). Pre-warp: hexagonal gold-foil bus, white decks and corner posts, louvred
radiators, Shuttle-orange payload ring, a big white dish r 0.68 under the bus (orange rim marker), crosslink terminal on
top, two long two-panel solar wings, red beacon, RCS quads. Standard: lenticular drum (panelled grey, Owner line round
the rim), deflector-style dish r 0.6 (metal rings, glowing rim), dorsal emitter spire, swept collector wings, blue glows.

| node | parent | Godot pivot prewarp / std | drive |
|---|---|---|---|
| `Body` | root | (0, 0, 0) | static |
| `PanelL`, `PanelR` | root | (∓0.394, 0.08, 0) / (∓0.5, 0, 0) | fold like the survey satellite: `PanelR.Basis = Basis(Vector3.Back, +a)`, `PanelL = Basis(Vector3.Back, −a)`, a 0..90 |
| `Dish` | root | (0, −0.55, 0) / (0, −0.21, 0) | opens toward −Y; `RotateY` is safe (tracking wobble) |
| `Horn` | Dish | (0, −1.13, 0) / (0, −0.69, 0) | feed horn at the focus |
| `Link` | Horn | same | material **"Link"** (exported at 1, the game pulses it); the top crosslink lens / spire tip uses the same material inside `Body`, so one pulse lights both ends |
| `Beacon` | root | (0.18, 0.35, 0.24) / (0, 0.185, 0.4) | "Beacon" (red / blue-white) |
| `Thruster0..3` | root | bus corners | own materials Thruster0..3 (0 front-left, 1 front-right, 2 rear-left, 3 rear-right) |

Owner = the band round the top of the bus (prewarp) / the drum's rim line (std). Paint baked into one 1024² atlas each.

## Mission props (`mission_props.glb`)

Root empty `MissionProps`; one mesh node per prop, every node at the origin (like `ruins.glb`): `probe_wreck` (r 4.3:
an Earth entry capsule ×1.35 (≈2.3 across) tipped 28° and half buried in its impact mound, faded white with orange band
and torn gold foil, the cracked heat shield beside it, a scorched patch and skid trench, a torn orange/white chute with
its lines, a bent antenna, a solar panel and shards; child **`Beacon`**, material "Beacon" exported at strength 1 for a
weak blink), `signal_source` (r 3.2: a black faceted spindle, material "Obsidian", wedged into a dark boulder outcrop on
fused glassy ground; child **`Glow`**, material "Glow" teal, strength 2: three seam rings + the top aperture),
`photo_point` (r 1.9: survey camera tripod, white camera with black lens, solar panel, whip, green status LED
"Lamp", orange hard case + cable, ground target plate), `survey_marker` (r 2.2: steel flag pole with the flag in
material **"Owner"** (player colour), concrete benchmark post + brass disc in a cairn, four stakes with orange tape).
Each sits on a "Ground" disc (dark disturbed earth; the game may tint it) whose skirt sinks to −0.4. Glow children are
unrotated (rotation baked into the mesh). No far stand-ins (props are small; the contract made them optional).

## Differences from the contract / notes for the view

- Drone nav lights: materials are "NavRed" / "NavGreen" (two colours can't share one material); both start with "Nav".
- Drone has extra `Tilt0..3` nacelle pivots between `Arm` and `Rotor` (the contract's Rotor spin is unchanged).
- Drone Pad: charger head lamps are separate meshes `ChargeLight0/1` (material "ChargeLight") so each bay lights alone;
  joined pivots come as `<pivot>` empty + `<pivot>Body` mesh like the other buildings.
- Relay: `Horn` is a child of `Dish`, `Link` a child of `Horn`; aim/turn the `Dish` node only.
