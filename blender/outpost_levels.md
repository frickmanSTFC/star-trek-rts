# Outpost levels 1–10 (design proposal)

A proposal for growing the colony core (the Colony Hub, "the outpost") through ten levels, mocked up in Blender by
`blender/build_outpost.py`. Previews are in `blender/previews/outpost/`. **Nothing in `data/` has changed.**
`buildings.json` still has three core levels: Colony Hub 1 → Colonial Administration 2 → Planetary Capital 3.

Every level keeps what came before it and adds to it. The landed colony pod (saucer on four legs, command capsule,
glass dome, red beacon) stays at the centre the whole way, so the core is always recognisable. Its mast grows into the
control tower and then into the capital's spire.

| Lv | Name | What it adds |
|---|---|---|
| 1 | **Landing Site** | The colony pod has just landed and deployed: saucer on its legs, boarding ramp down, a red beacon on a short mast. Two inflatable hab modules, three supply capsules, the repair pad (striped slab, lamp mast, tool rack, gas bottles, jib crane) and amber beacons at the six hex corners. |
| 2 | **Outpost** | A walled ground floor under the saucer, so the legs become buttresses. It has a window band, an owner-colour band, a front porch with a canopy, and blue running lights. Also: a third hab, lit tubes joining the habs to the hub, the supply capsules unpacked into three hex cargo modules, a comms mast with a dish at the back corner, and a sensor dish on the beacon mast. |
| 3 | **Colony Hub** | The mast becomes a tapered pylon with a small saucer cap: windows, amber rim lights and an antenna. Also: the first terraced block on the left edge behind the habs (two steps, window strips), three floodlights, and worn vehicle tracks to the pad and the front. |
| 4 | **Settlement** | A striped shuttle landing pad with blue edge lights and a parked shuttle ("14"). Six cargo modules, two of them stacked, and an arched storage shed. |
| 5 | **Township** | A control tower: a taller pylon with a wide windowed saucer cap. The terraced block gets a third step and grows longer. Also: two radomes by the comms mast and paved walks with blue edge lights. |
| 6 | **Colonial Administration** | An administration wing (a second terraced block on the back-left edge), a squat saucer-topped cone tower at the back right, a skybridge from the pod to the administration wing, and the first deck ring on the pylon. |
| 7 | **Provincial Centre** | *Spills onto the neighbours' edges.* A large round pad with a red centre and amber ring lights, plus a second shuttle ("37"). Two solar halls with blue roofs on the right-hand neighbour's edge. Also: a needle spire with stacked rings above the cap, and a glass lift tube beside the pylon. |
| 8 | **Colony City** | A big ribbed assembly dome with a ring of windows and an antenna spire with a dish (on the back neighbour's edge). A fourth step on the terraced block, a second deck ring, and owner-colour fins up the pylon. |
| 9 | **Metropolitan Hub** | Two needle towers with pagoda rings (left neighbour), twin saucer platforms on column clusters with lift tubes (back-right neighbour), and a third step on the administration wing. |
| 10 | **Planetary Capital** | The main spire tops out at 40 units: five stacked rings, a third deck ring and a dish. Also: a comms array of three big dishes, a third landing pad, and a third terraced block on the back-left neighbour's edge. |

## Footprint and scale

- One hex is 25 units across the flats (corner radius 14.4). Buildings keep their own size (owner's decision).
- **Levels 1–6 stay inside one hex.** The only exceptions are the worn tracks, which are ground decals that lead in
  from outside.
- **Levels 7–10 spill onto the neighbouring hexes' edges**, about 6–9 units past the hex edge. On a real map that
  would claim or block those tiles. That still needs a rule: either the capital needs free neighbours, or its extras
  only draw where the neighbours are empty.
- Heights of the beacon or spire top, levels 1–10: 10.6, 11.8, 14, 16, 19, 22, 26, 30, 34, 40. The game's mountains peak
  at about 40–78.
- The pod is the colony-pod scene (`build_colony_pod.py`) at about 0.45 scale. Its saucer is 9.1 across, about the
  same size as today's primitive hub (radius 4.7).
- Repair pad: the same layout as `BuildingModels.RepairPad`, but at (8.6, 2) in Godot's frame instead of (9.4, 2), so
  the 7.4-wide pad stays inside the hex.
- For scale, a Mk I survey rover (from `build_vehicles.py`, without the paint bake) parks on the repair pad. There are
  2 + 2 × level colonists, each 1.9 units tall.

## Previews

- `blender/previews/outpost/outpost-L01.png` … `outpost-L10.png` all use the same camera, framed for level 10.
- `outpost-L01-game.png`, `outpost-L05-game.png` and `outpost-L10-game.png` show the game's camera at zoom distance 50
  (45° fov).
- `outpost-sheet.png` shows all ten levels side by side.

To rebuild them:
`/root/bpyenv/bin/python blender/build_outpost.py --level N --preview blender/previews/outpost --res 960 --samples 32 --shots front34,game`,
then `python3 blender/build_outpost.py --sheet blender/previews/outpost`.

## Questions for the owner

- **Mapping to the data.** Should the core get ten levels in `buildings.json`, or keep three? If it keeps three, the
  three names could sit on levels 3, 6 and 10 and the levels in between become visual steps. Those steps could come
  from the number of buildings in the base, the population, or Mk II/III-style sub-levels.
- **Is level 1 the landing?** Today the landing hub is already the "Colony Hub". This proposal makes the landing level
  1, "Landing Site", and has the hub earn the "Colony Hub" name at level 3.
- **Should levels 7–10 claim the neighbouring tiles** (see above)?
- **Are the later pieces separate buildings?** Several pieces could become their own buildings later instead of being
  part of the core: the shuttle pad, the comms array, the solar halls, and the terraced blocks as habitats.

## Level 1 in the game (2026-11-05)

The owner asked for the first landing to come down in three drop ships and unfurl/combine into the level 1 outpost.
`blender/build_outpost_l1.py` builds a **game-ready** Federation level 1 outpost, a different design from the mockup's
"Landing Site" above: a round drum-and-dome hub cut into three 120° wedges (ModuleA command + comms mast, ModuleB hab +
solar wings, ModuleC ops + vault, radiators and floodlight). `--game` exports `game/models/buildings/outpost_l1.glb`
(assembled rest pose); the game's `LandingView` flies each wedge down under a drop ship, crawls them together and
unfurls them. Previews: `previews/outpost/outpost-l1-assembled.png`, `-game`, `-top`, `-unfurl`, `-crawl`, `-convoy`.
Levels 2–10 are still only this mockup (levels 2 and 3 of the data are primitive models in `BuildingModels.Hub`).
