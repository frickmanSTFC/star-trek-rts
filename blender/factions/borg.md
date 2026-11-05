# Borg ground kit (mockups)

The Collective doesn't paint, name or decorate anything, and it doesn't research either: it assimilates. So every
Borg piece looks like it was put together from whatever was taken. Near-black and dark-grey machinery is built up in
layers: big shallow panels, then small blocks, vents and stacked plates, with exposed pipes on brackets, loose cables
and bare girder lattice. The geometry is plain cubes, slabs, stepped spires and sphere fragments, lit by hard green
slits, dashed grid lines and laser-bright sensor eyes. Drones ride in alcoves instead of sitting in cabs. Buildings
grow over the native world instead of replacing it: conduit roots creep across the ground and climb the pale native
dwellings, which get covered in plating level by level. The player's colour is a single thin glow line (material
`Owner`, blue in the mockups so it reads apart from the green). The decal spots `NameL/NameR/NameRear/NumberL/NumberR`
are kept as empties, but a Borg unit would carry a designation there ("3 of 7", "Tertiary Adjunct 12"), never a
name. All shapes are original designs in that general style, not copies of any existing ship or set.

Build and preview: `blender/factions/build_borg.py` (header lists the flags). Renders: `blender/previews/factions/borg/`.

- **Survey rover: sensor crawler** (`--model rover`, 4.4 × 2.5 × 2.8 to the head): a low slab chassis on four angular
  track pods, a greebled sensor housing with laser slits, a corner-standing cube sensor head (`Dish`, spins) that sweeps
  a green laser scan fan (`ScanFan`) over the ground, a caged power cell across the deck, and a drone riding in an
  alcove at the back (`Drone`). Also has a `Beacon` and `TailLamp` strips. Track pods are `Track0`–`Track3`.
- **Mining vehicle: extraction crawler** (`--model miner`, 5.6 × 2.9 × 3.0): a heavy hull on two long angular tracks
  with a green-lit intake maw, a power turret carrying a girder `Boom`, an extraction head `Drill` > `String` (cable
  bundle, scale Z to reach the ground) + `Bit` (four clawed tendrils round a green core, spins), an open lattice hopper
  with its `Fill` load, and four claw legs `Leg0`–`Leg3` folded up along the sides (the crawler's part names).
- **Drop ship: descent block** (`--model dropship`, 11.6 long, 8.8 × 10.6 over the feet, ~9.5 to the beacon): a stacked
  greebled cuboid with a sphere-fragment tractor emitter in the nose and one rear corner left as bare lattice around a
  glowing conduit. It stands on four piston-and-lattice legs (`Gear0`–`Gear3`) and has green lift emitters
  (`Thruster0`–`3`). The carried unit hangs underneath in four clamp arms (`Clamp0`–`3`, unit at the `Cargo` empty);
  the preview shows the miner (`--cargo miner|rover|none`).
- **Outpost L1: seeded node** (`--model outpost --level 1`, inside one 25-unit hex): the seed has just landed and split
  open (shell petals round a rubble crater). A tilted, stepped black spire (12.5 tall, `Beacon` on the tip, sensor
  ring `Spin`) stands in the crater, seven conduit roots spread toward the hex edge, there are two regeneration alcoves
  with drones and a power cell, and a root has started to plate over a native dwelling.
- **Outpost L5: assimilation node** (`--level 5`, inside one hex): a taller spire (18 to the tip) with four hive blocks under
  dashed green grids, conduits at height linking them to the spire, a six-alcove block, three caged power cells, a
  lattice mast with a laser eye, an assembly-matrix cube frame with a cube half grown inside, a regeneration pad (the
  Borg repair pad), roots out to the hex edge, and the native dwelling about two-thirds converted.
- **Outpost L10: assimilated complex** (`--level 10`, spreads over seven hexes): a plated ground field, a 14-unit
  central hub cube with a glowing core on its face, a spire on its back corner reaching 42, three rings of hive blocks
  (some stacked) joined to the hub by elevated conduits, three lattice masts, three alcove blocks, a power farm, two
  regeneration pads, fully plated native buildings, and a sphere moored overhead on a tractor beam from a docking mast.
- **Comparison:** `--model fed_mk1` renders build_vehicles' Mk I Scout (tier 1, no paint bake) under the Borg rover's
  exact camera. `--model lineup` puts the Fed Mk I, the Borg rover, the miner and the drop ship side by side.
  `--sheet <dir>` builds `borg-sheet.png`.
