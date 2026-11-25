# Space sites: models

For the space jobs (`docs/space-jobs-contract.md` "MODELS"). One file, `game/models/space/sites.glb`, built by
`blender/build_space_sites.py --game` (~10 s). Original designs; the Earth derelict, debris, cargo pod, lifepod and the
station use the pre-warp ships' white / grey / foil / orange.

```
/home/user/.bpyvenv/bin/python blender/build_space_sites.py --game                    # the glb
/home/user/.bpyvenv/bin/python blender/build_space_sites.py --preview blender/previews/space_sites [--only a,b]
python3 blender/build_space_sites.py --sheet blender/previews/space_sites            # contact sheet (system PIL)
```

Flags: `--out PATH`, `--only names`, `--res WxH` (800x480), `--spp N` (32), `--tex DIR` (where the generated textures
go; default the system temp dir). Previews are Cycles on black with one key sun and a dim blue fill (~1.5 min for all).

## Conventions

- Every model is a **root node at the scene root, on its own origin** (all roots sit at 0,0,0 in the file; place them
  yourself). The origin is roughly the centre of mass, so a model can tumble about it.
- Godot axes like the ship glbs: **forward/bow toward -Z, dorsal +Y, port -X**. Units are metres, the same scale as
  `game/models/ships/*.glb` (Armstrong ~127 m, Cochrane ~480 m).
- No transforms on the roots; child nodes carry their own translation (and rotation where noted). Names are plain
  (`Beacon`, `Light0` repeat under different roots; the export strips Blender's `.001` suffixes).
- No lights, cameras, animations or skins in the file: the view adds OmniLights at the `Light0` / `Beacon` nodes and
  drives the moving parts.
- Materials are double-sided (the broken hulls and the cavity are seen from inside). Textured materials carry a
  512² JPEG base colour (tiling, UVs in metres × 1/tile).

## Nodes

Sizes are the bounding box in Godot X × Y × Z (m).

| root | size (m) | tris | child nodes |
|---|---|---|---|
| `derelict_unknown` | 80 × 46 × 198 | 3.1k | `Light0` |
| `derelict_earth` | 50 × 38 × 125 | 6.7k | — |
| `derelict_trader` | 50 × 21 × 97 | 1.9k | — |
| `derelict_native` | 25 × 28 × 65 | 2.7k | — |
| `debris_piece_a` | 3 × 13 × 15 | 0.5k | — |
| `debris_piece_b` | 4 × 4 × 18 | 0.6k | — |
| `debris_piece_c` | 6 × 10 × 25 | 0.4k | — |
| `debris_piece_d` | 9 × 9 × 15 | 0.1k | — |
| `debris_piece_e` | 4 × 4 × 8 | 0.1k | `CargoPod` (holds all its meshes) |
| `debris_piece_f` | 1 × 2 × 1.5 | 56 | `BlackBox` (holds its meshes) → `Beacon` |
| `asteroid_common` | 145 × 132 × 221 | 20.5k | — |
| `asteroid_metal` | 130 × 124 × 144 | 20.5k | — |
| `asteroid_ice` | 111 × 114 × 154 | 20.5k | — |
| `asteroid_hollow` | 255 × 209 × 251 | 20.2k | `Mouth`, `Light0` |
| `lifepod` | 4.4 × 4.4 × 4.2 | 0.5k | `Beacon` |
| `comet_nucleus` | 251 × 256 × 482 | 20.5k | `Jet0`, `Jet1`, `Jet2` |
| `station` | 68 × 60 × 70 | 6.2k | `Ring` (extras `spin`), `Berth0`, `Berth1`, `Beacon` |
| `probe_alien` | 10 × 11 × 15 | 1.2k | `Core` |

Total ~126k triangles, 3.5 MB.

- **derelict_unknown**: a long flattened lobed hull (three keels) snapped in the middle; the forward half has drifted
  ~13 m and turned (11° yaw, 22° roll), torn edges with dark insides, bulkheads and ribs sticking out, a crescent of two
  swept blades round the head (one torn short), three aft vanes (one snapped), four cold drive bells, a few plates in the
  gap. `Light0` (aft dorsal, at about (0, 9.3, 74)) is the one faint light: a small `Lamp` bulb; add a weak OmniLight
  there and let it flicker.
- **derelict_earth**: pre-warp explorer/freighter, dark: square truss spine, domed crew module (dark windows), a torn hab
  ring (two arcs left, a snapped spoke), four foil tanks, radiators (one bent away), drive drum + bell, a loose cable.
- **derelict_trader**: boxy rust-brown trader, dorsal cargo doors swung open with two containers still in the hold, two
  engine pods on pylons, a torn hole in the port flank.
- **derelict_native**: native early-warp ship: trefoil body with bronze bands, three outrigger coil pods on struts,
  three bells, a dark cockpit band.
- **debris_piece_a..f**: a curved hull plate with ribs, a twisted triangular truss, a nacelle stub torn at the front, a
  hull chunk with frames, the cargo pod (`CargoPod`, orange end frames, a small `Lamp` status light) and the black box
  (`BlackBox`, orange with an aerial; `Beacon` = a 9 cm red bulb at the aerial tip, blink it).
- **asteroid_common / metal / ice**: displaced icospheres with craters, smooth-shaded. Metal has `Metal` patches over
  `Rock`; ice is mostly `Ice` with `Rock` showing through.
- **asteroid_hollow**: a funnel pressed into the bow side (-Z) opening into a rocky throat and a chamber ~85 m deep.
  `Mouth` = the centre of the opening (about (0, 0, -66)); its local -Z points out of the mouth. Inside: an old platform
  and `Light0` (a dim `Lamp` bulb deep in the cave, about (3, -2.8, -9.6)).
- **lifepod**: a conical capsule, heat shield aft (+Z), hatch forward; `Beacon` on the upper nose, blink it.
- **comet_nucleus**: a dark two-lobed nucleus (`CometRock`) with `Ice` patches. `Jet0..2` are vent points on the surface:
  local **+Y points out of the surface** (put gas/dust particles along it). Add the coma and tail in the view.
- **station**: small pre-warp station along the Z axis: forward axial port with `Beacon` (red bulb, -Z end), a lit hab
  can (`Lamp` windows), the ring bearing, foil tanks, a power truss with two `Solar` wings aft, radiators.
  `Ring` (origin on the axis, at 0,0,0) = the spinning crew ring (torus, spokes, collar, `Lamp` windows):
  `RotateZ(spin * dt)`, `extras.spin` = 0.24 rad/s. `Berth0` (ventral) and `Berth1` (dorsal) are the two radial docking
  ports at z -14, like the ship berths: local **+Y points at the station's axis**, local Z runs along it, so a lander
  (origin at its pads, +Y up) hangs roof-in as a child at (0, -height, 0).
- **probe_alien**: a three-lobed body with three long swept spines and three short forward antennae; `Core` holds the
  `Glow` ring round the waist and the glowing nose: pulse its energy.

## Materials

| name | use | notes for the view |
|---|---|---|
| `Hull` | alien hull (unknown derelict, probe) | dark green-grey panels, metallic 0.35 |
| `HullWhite`, `HullGrey` | pre-warp human panels | |
| `HullTrader`, `HullNative`, `NativeTrim` | trader / native hulls | |
| `Foil`, `FoilSilver`, `Orange`, `Dark`, `Glass`, `Solar` | details | |
| `Metal` | structure, ribs; metal veins on `asteroid_metal` | metallic 1 |
| `Rock`, `Ice`, `CometRock` | asteroids, comet | smooth-shaded |
| `Lamp` | windows, status lights, `Light0` bulbs | emissive warm, strength 4 |
| `Beacon` | red beacon bulbs | emissive red, strength 6: blink |
| `Glow` | the probe's core | emissive teal, strength 5: pulse |

The derelicts are dark: no `Lamp` on them except the unknown derelict's `Light0`.

## Previews

`blender/previews/space_sites/<root>.png` (800×480) and `space_sites-sheet.png`.
