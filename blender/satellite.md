# Survey satellites (models)

Owner 2026-11-10: "The satellites in orbit need to be refined." Contract: `docs/satellite-contract.md` ("Model").
Script: `blender/build_satellite.py` (bpy 5.x). Two original designs, one glb each:

| glb | era | look | size (Godot x × y × z) |
|---|---|---|---|
| `game/models/orbit/survey_satellite.glb` | every era but Dawn of Warp | sleek flattened-torpedo sensor bus (light grey, panel lines), glowing blue nose sensor cap + aft ring, dorsal sensor blister with blue ports, beacon mast, swept collector wings (dark blue cells, glowing leading edges, tip sensor pods), deflector-style sensor dish under the belly (rings, glowing core + rim) facing the planet, ball-turret camera under the nose | 2.74 × 0.53 × 1.86 |
| `game/models/orbit/survey_satellite_prewarp.glb` | Dawn of Warp (2063–2150) | Earth-built: gold-foil box bus with white decks, corner posts and louvred radiators, Shuttle-tank orange (#C8632A family) payload adapter / wing yokes / barrel band / dish marker, black tiles on the nadir deck, two big three-panel solar wings, white telescope barrel hanging at nadir, high-gain dish on a mast at zenith, magnetometer boom, star trackers, whips, red beacon | 2.99 × 2.06 × 1.32 |

Both: 13 nodes, ~0.8–0.9 MB (one 1024² JPEG paint atlas each), 15.5k / 10k triangles.

## Scale and axes

- 1 Blender unit = 1 game world unit; no scale needed in Godot (the orbit globe is R ~100, a satellite ~2–3 long).
- Blender +Y = front = **Godot −Z** (direction of travel), Blender +Z = **Godot +Y** (zenith), so
  **nadir = Godot −Y** (the planet). The glTF exporter maps Blender (x, y, z) → Godot (x, z, −y).
- Origin = the bus centre. Every pivot is **unrotated** (identity rest), so Godot angles go straight on.

## Parts (same names in both)

| node | parent | what | pivot (Godot) std / prewarp |
|---|---|---|---|
| `Body` | root | everything static, joined | (0, 0, 0) |
| `PanelL`, `PanelR` | root | wings, one mesh each, origin on the hinge at the bus side; rest = deployed (flat) | (∓0.318, 0, 0.3) / (∓0.39, 0.06, 0) |
| `Dish` | root | one mesh; spins about its local **+Y** (vertical; `RotateY` works). std faces nadir, prewarp faces zenith | (0, −0.115, 0.2) / (0, 0.64, 0.12) |
| `Camera` | root | one mesh, origin = gimbal centre, lens looks along local **−Y** (nadir) at rest | (0, −0.124, −0.52) / (0, −0.445, −0.04) |
| `Lens` | Camera | material `Lens`, emissive glint | (0, −0.236, −0.52) / (0, −0.865, −0.04) |
| `Flash` | Camera | material `Flash` | (0, −0.222, −0.52) / (0.146, −1.005, −0.04) |
| `Beacon` | root | material `Beacon` (std blue-white, prewarp red), blinking | (0, 0.245, 0.56) / (0.3, 0.36, −0.33) |
| `Thruster0..3` | root | RCS glows, materials `Thruster0..3`; 0 front-left (−X), 1 front-right, 2 rear-left, 3 rear-right | std: front pair on the flanks facing ±X, rear pair on the aft face facing +Z; prewarp: four lower corners facing ±X |

- **Wing fold:** rotate about the hinge's front-back axis so the wing swings **up** beside the bus (cells
  facing in): Godot `PanelR.Basis = new Basis(Vector3.Back, +a)`, `PanelL.Basis = new Basis(Vector3.Back, -a)`,
  a = 0 (open) … 90° (folded, standing beside the bus; nothing clips at any angle in between). Blender:
  `rotation_euler.y = -a` (R) / `+a` (L).
- **Camera aim:** rotate the `Camera` node so its local −Y points at the target (e.g. `Basis` from the rotation
  taking `Vector3.Down` to the local direction). Keep it within ~±35° of nadir (prewarp barrel inside its
  adapter cone). Lift/aim only `Camera`: `Lens` and `Flash` are its children (if the loader lifts them out to
  direct children they stop following the camera).
- **Flash:** exported emissive white at strength 1 (an emissive factor of 0 would make Godot import it with
  emission off and the colour lost); the game sets its emission energy to 0 at rest and pulses it per picture.
- **Lens** emits at 2.0 (std, blue-white) / 1.6 (prewarp, violet coated optics); **Beacon** 6; **Thrusters**
  1.5 (std blue) / 1.0 (prewarp warm). Other glows baked into the Body/Dish/Panel meshes (not driven):
  `SensorGlow`, `DriveGlow`, `CollectorGlow`, `TipGlow`, `DishGlow`.
- **Owner** (recoloured per player): std = a thin line along both sides of the dorsal spine; prewarp = a thin
  band round the top of the bus.
- Hull materials for achievement paint: std `HullPaint`, prewarp `WhitePaint` (match "*Paint").

## Re-export / previews

```
/home/user/.bpyvenv/bin/python blender/build_satellite.py --game                       # both glbs, ~10 s
/home/user/.bpyvenv/bin/python blender/build_satellite.py --preview blender/previews/satellite --samples 48
python3 blender/build_satellite.py --sheet blender/previews/satellite                  # contact sheet (PIL)
```

`--designs std|prewarp` picks one, `--shots hero,limb,folded,flash,glb,small`, `--res`, `--samples`,
`--bake-res`. The `glb` shot imports the export three times and poses it through its own pivots (deployed,
45°, folded + camera tilted 30° + dish turned 60°); `small` shows both glbs at orbit-view size over the planet.
Previews in `blender/previews/satellite/` (`satellite-sheet.png` = all of them).

Not seen in Godot yet: check that the glbs import (baked atlas colours, emission strengths with the orbit
view's glow, Owner line), the fold direction (wings up, not down through the dish), and the size on the R+14
track.
