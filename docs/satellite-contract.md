# Survey satellites, refined: contract

Owner 2026-11-10: "The satellites in orbit need to be refined. we should also be able to review them in the UI. See what
information they are fetching, maybe see them taking pictures/etc."

Today: `SatelliteCommand(player, row)` puts a `Satellite` (Id, Owner, Row, LaunchTick, Swept) over a band of
`orbit.satellite.band_rows` 9 rows; it sweeps one map width east every `sweep_seconds` 60, surveying what it passes
(`PlanetSurface.SweepSatellites`). OrbitView draws a small marker on a sky-blue latitude ring (`UpdateSatellites`,
`SatelliteOrbit` = R + 14) and lists "SAT-n · Lat … · sweep %" in the Orbit display's tracked objects. System
satellites at other bodies (`SystemSatelliteCommand`) give "live" data there. Nothing shows what a satellite found.

## Model (agent M)

`blender/build_satellite.py` (bpy 5, like the other builders; `--game`, `--preview blender/previews/satellite`):
two original designs, era-matched, each a separate glb in `game/models/orbit/`:
- `survey_satellite.glb`: the standard (Federation, every era but Dawn of Warp) orbital survey probe.
- `survey_satellite_prewarp.glb`: Dawn of Warp (2063–2150): Earth-built, white/foil/Shuttle-orange like the pre-warp
  drop ships, boxy bus, big solar wings, a camera/telescope barrel, a dish.
Sized for the orbit view (the globe radius is ~100 world units; a satellite is drawn ~2–3 units long, so a strong
silhouette matters more than detail). Parts the game drives (same names in both):
`Body`, `PanelL`, `PanelR` (solar wings: hinge at the body, fold 0 → open; deploy unfolds them), `Dish` (spins about
its own axis), `Camera` (points at the planet = local −Y in Godot / nadir; the game aims it), `Lens` (material "Lens",
emissive glint), `Flash` (material "Flash", emissive, off at rest: the game pulses it when a picture is taken),
`Beacon` (material "Beacon", blinking), `Thruster0..3` (small RCS glows), material `Owner` (thin band, recoloured).
Previews: hero, against a planet limb, wings folded vs deployed, a contact sheet.

## Sim + view + UI (agent U)

Sim (deterministic, hashed, through commands only; see `game/README.md`):
- Satellite gets a call sign (deterministic per owner, e.g. "SAT-2 · KESTREL"), a deploy phase (`ReadyTick`: rises
  from the ship and unfolds over ~8 s before sweeping), `Passes` (completed laps) and a bounded findings log
  (`SatelliteFinding`: tick, kind (Deposit / Life / Natives / Toxic / Ruins / Weather / Terrain), tile, detail id,
  first-time flag): what each sweep newly surveyed or spotted. Coverage of its band (surveyed tiles in band ÷ band
  tiles). All in the state hash; a test that two runs log the same findings.
- Optional: `RetaskSatelliteCommand(player, satId, row)` to move a satellite's band (refused while deploying), only if
  it stays small.

View / UI (render-only, HUD look = HudKit + the helm's style):
- Orbit view: the satellite model on its track (fallback: primitive with the same part names), wings unfolding on
  deploy, the ground swath it is imaging shown on the globe (a soft strip on its band near the sub-satellite point),
  a flash + shutter tick each time it takes a picture, select/hover a satellite in the 3D view.
- Helm: a SATELLITES list (rail entry or Orbit-display panel) and a satellite card: call sign, model, orbit band
  (latitude range), status (deploying / sweeping / pass n), sweep progress, band coverage %, tiles surveyed, data
  downlinked, findings log (newest first, click a finding = show it on the globe / jump to the tile on the surface),
  FOLLOW (orbit camera tracks it), RETASK (if built).
- Photo feed: every few seconds of sweeping (and on each finding) the satellite "takes a picture" of the ground under
  it: a thumbnail rendered from the real surface (top-down/oblique camera over PlanetView via the SurfaceFeed
  layer-20 trick, or a crop of the orbit globe textures if cheaper; picture modes VISUAL / INFRARED / TERRAIN as
  shader tints), stamped with call sign, time, coordinates and what was found; a strip of the last ~8 in the card,
  click to enlarge. Keep render cost bounded (one capture at a time, low res, only while the card is open or a
  finding happens).
- System satellites at other bodies show in the same list (their live data rows).
- Uses the model contract above; missing glbs → primitive stand-in.
