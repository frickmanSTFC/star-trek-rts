# Pre-warp ships: high-detail models, stills and game models

One script per ship, all on the Cochrane's pipeline:

| ship | script | stills | game model | card |
|---|---|---|---|---|
| Cochrane | `blender/build_cochrane.py` | `blender/previews/cochrane/` | `game/models/ships/cochrane.glb` | `game/textures/ships/cochrane.jpg` |
| others | `blender/build_<id>.py` | `blender/previews/<id>/` | `game/models/ships/<id>.glb` | `game/textures/ships/<id>.jpg` |

Run with the bpy module: `/home/user/.bpyvenv/bin/python blender/build_<id>.py --shot hero --out blender/previews/<id> --tmp <scratch dir> --earth-tex <bmng.jpg>`.
Flags (parsed by `build_cochrane.main`): `--shot`, `--res 1920x1080`, `--spp 128`, `--drive`, `--no-earth`, `--only a,b`
(passed to your build as `parts`), `--name` (output suffix), `--elon/--elat` (turn the Earth), `--game <path.glb>` (write the
game model and stop). The Earth image is NASA Blue Marble `bmng.jpg` from the PyPI `basemap-data` wheel.

## A ship script

```python
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_cochrane as C
from build_cochrane import k, on, P, R, rotX, rotY, rotZ, move, orient, TAU, PI   # k is the shared Kit

def build(parts, drive, landers):
    ...                      # add geometry to k; x toward the bow, y to port, z dorsal, metres, origin amidships on the axis

SHOTS = {'hero': dict(tgt=(0, 0, 0), cam=(0.62, -0.72, 0.30), dist=900, lens=70, roll=-7, sun=(0.62, 0.55, 0.56), eoff=(-9, -20), alt=9500), ...}

if __name__ == '__main__':
    C.main(build=build, shots=SHOTS, ship_id='horizon')
```

Shot fields: `tgt` look-at point; `cam` direction from it to the camera; `dist`; `lens` (mm, 36 mm sensor); `roll` degrees;
`sun` direction to the sun in camera space (x right, y up, z toward the camera); `eoff` where the Earth's centre sits in
degrees right/up of the view axis (None = black sky); `alt` camera height above the Earth in km; optional `elon`, `elat`.

## What the pipeline gives you (read `build_cochrane.py` and `cochrane_kit.py` before writing)

- **Kit `k`** (`cochrane_kit.Kit`): `box`, `boxX`, `lathe` (revolve (x, r) points about local X; `closed`, partial arcs with
  `caps`), `cyl`, `can` (chamfered), `tube`, `pipe`, `sphere`, `dome_tank`, `torus`, `quad`, `extrudeX`, and
  `with k.xf(matrix):` to place things. Every primitive writes UVs in metres. Geometry is merged per material.
- **Parts in `build_cochrane`**: `on(x, a, r)` (frame on a hull: x axial, y radial out, z tangential), `P(x, r, a)`, `ycan`,
  `zcan`, `gbox`, `greeble`, `hrail`, `axial_pipe`, `hatch`, `rcs_quad`, `dock_port(lifeboat)`, `dish`, `floodlight` (adds a
  real spot light), `girder` (lattice), `spine_truss`, `rrect` (rounded pressure-hull outline), `arc_plate`, `arc_decal` /
  `flat_decal` (lettering), `hub`, `hab_ring` (a whole spin ring: hull, bumper tiles, windows, spokes, stays).
- **Materials** (use these names): `white`, `white_m` (blankets), `white_s` (small panels), `tank`, `grey`, `grey_p`,
  `vessel`, `blue` (livery), `tile`, `tile2`, `bumper`, `radw` (white radiator), `hazard`, `orange`, `yellow`, `dark`,
  `black`, `cc` (carbon), `truss`, `steel`, `tungsten`, `bell`, `gold` and `silver` (foil), `glass` (lit windows; choose
  `glass` or `glass_dark` per window), `glass_dark`, `lamp`, `navR`/`navG`/`navW`, `hot` (hot radiator: UV v must run 0 at
  the root to 1 at the tip, see the panels in `build_aft`), `hot2`, `hotpipe`, `plasma`, `ctr0..4` (containers).
  A ship's own material: `C.EXTRA['name'] = (lambda: K.panel(...), ('pan', c1, c2, seam, (w, h), metal, rough))`
  (or `('sim', colour, metal, rough)`, `('emit', colour, strength)`): the first is for Cycles, the second for the game export.
- **Turning sections**: `C.spin_group('RingF', x_centre, g, radius, direction)` before adding the parts, `k.group = 'Hull'`
  after. Names must start with `Ring`. `hab_ring` registers itself when `k.group` is set first. The game export makes each
  group its own node with its pivot on the axis and prints the spin rates.
- **Drop ships**: append `(x, angle, pad_radius)` to `landers` (pad radius = roof radius + 6.6); stills get the game's
  lander model there, the game export gets `Berth<n>` nodes.
- **Drive**: when `drive` is true, `C.PLUMES.append(dict(x=throat_x, length=..., r0=..., r1=...))`; the `hot` radiators
  glow at full power automatically.
- **Lights**: `C.LIGHTS.append(('POINT', loc, None, watts, colour, 0))` for navigation lights; `floodlight()` for work lamps.
- **Game export** (`--game`): thins the mesh (`k.lod`, small tubes and boxes dropped), one mesh per group and material,
  tiling textures, bow turned to Godot -Z. Contract: `docs/ship-model-contract.md`. Aim under 200k triangles and about 10 MB;
  if you are over, cut repeated small parts when `k.lod < 1`.

## What made the Cochrane read as real

- Shapes that could be built: pressure hulls are round or domed, never flat boxes; open structure is lattice or tube truss
  with node fittings; panels on stay rods; tanks with domed ends, bands and cradle struts; radiators as segmented panels on
  booms with manifolds.
- Layers: a vessel inside a cage inside pipework reads better than one detailed skin.
- Finishes with a reason: white paint and blankets on crew volumes, gold and silver foil on cryogenic parts, bare metal on
  structure, orange or hazard yellow on anything that moves, dark carbon on hot radiators.
- Avoid the tells: no regular dark checker of tiles or windows (keep variation small and rare), windows small and dim (they
  only show on the shadow side), hot radiators dark at idle, no depth-of-field blur, black sky with no stars, one sun plus
  earthshine.
- Scale cues: handrails, hatches, docking ports, floodlights, lettering, a docked craft.
- Look at every render before going on. Test at 960x540, 24 samples, `--no-earth` (seconds); finals 1920x1080 at 128.
