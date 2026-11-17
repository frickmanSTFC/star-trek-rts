# Ship models in the game: contract

Owner 2026-11-17: "put the 3d model on the ship pick page. Allow the "orbit" view to see the ship too." Only the Cochrane
has a game model so far (`blender/build_cochrane.py --game`); the other five ships keep their flat card art until they get
one. Everything here is render-only: no sim or data changes.

## The file

`game/models/ships/<ship id>.glb` (ship id = `ShipChoiceDef.Id`; today only `cochrane.glb`). A ship without a file has no
3D model: the pick page shows its card picture as before and the orbit view shows nothing new. Load it the way the other
glbs are loaded (missing or not yet imported = fall back quietly, one warning at most).

- Units are metres: the Cochrane is about 480 long and its forward ring is 156 across. Scale it down for whatever view it is in.
- Godot axes: **bow toward -Z, dorsal +Y, port -X**. The origin is on the ship's long axis, about amidships.
- Root node `Ship`. Children:
  - `Hull`: everything that does not turn (one or more MeshInstance3D below it).
  - `RingF`: the forward crew ring with its spokes and collar. Origin on the axis at the ring's centre. Spin it about its
    local Z: `RotateZ(+0.3172 * dt)` (0.8 g at 78 m, about 3 rpm).
  - `RingA`: the aft workshop ring. `RotateZ(-0.2253 * dt)` (0.3 g at 58 m; it turns the other way).
  - `Berth0`..`Berth3`: empty nodes at the four drop ship berths on the forward hub. Local **+Y points at the ship's axis**
    (toward the hub), local Z runs along the ship. A lander model (origin at its pads, +Y up, as the game's drop ship glbs
    are) hangs roof-in when it is a child at `(0, -height, 0)`, height = the top of its bounding box. Optional: nothing
    breaks if the berths stay empty.
- Materials keep their names. Emissive ones: `glass` (cabin windows), `lamp`, `navR`, `navG`, `navW`, `hot` and `hot2`
  (radiators, dull at idle), `hotpipe`. No lights are in the file.
- Budget: under 200k triangles, under 10 MB. No animations, no skins.

## Ship pick page (`game/scripts/MainMenu.ShipPick.cs`)

The selected ship's detail panel shows a live, slowly turning 3D model in place of its still picture when the ship has a
glb: its own SubViewport with its own World3D, camera, key light and a dim fill, black or transparent background, rings
turning, drag to turn it by hand. Fit the camera to the model's bounds. Render only while the page is open. Ships without
a glb keep the picture. The six cards keep their flat pictures.

## Orbit view (`game/scripts/OrbitView*.cs`, `PlanetDriver*.cs`, `HelmScreen.cs`)

The player's flagship is visible in orbit when its picked ship has a glb (`GameSetup.ShipId` / the sim's ship choice):

- It rides where the ship's track marker already is (the peach ship track at R+32 on the Orbit display), bow along the
  direction of travel, dorsal side away from the planet, lit by the orbit view's sun. Size it like the satellites are sized
  (far bigger than life so it can be seen): placeholder about 14 units long.
- Shown on the Planet and Orbit displays while the flagship is in orbit of the body on screen; hidden on System and
  Atmosphere, and hidden while the flagship is away or in flight.
- A way to look at it: a SHIP entry (helm rail or the tracked-objects row for the ship) that turns the view to the ship and
  follows it, zoomed in close enough to see it well; any manual turn or Esc lets go. Follow the satellite FOLLOW pattern.
- Rings turn. It must respect the existing layer rules (the surface feed's layer 20, satellite photos) and must not block
  tile picking or the satellite pick.
