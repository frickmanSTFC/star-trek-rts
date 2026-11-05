# Klingon ground kit (Blender mockups)

Built by `blender/factions/build_klingon.py` (helpers borrowed from `blender/build_vehicles.py` without running its
`main()`); renders in `blender/previews/factions/klingon/`, contact sheet `klingon-sheet.png` (with the Federation Mk I
Scout at the same camera for scale).

**Look.** Where the Federation kit is white paint, big glass and rounded boxes, the Klingon kit is dark gunmetal
armour faceted into wedges and beaks. Oxidised bronze-green plates cover the parts that take hits (fenders, fins,
roofs, crowns). Swept wing-like fins rise at the back of every vehicle and building. There are no big windows, only
narrow red-orange glowing slits (a V-shaped "brow" visor on the vehicles), plus red running lights, polished bronze
fittings and spikes, and the kit's own emblem: three swept daggers inside a broken ring, used on hulls, pads, banners
and gables. Buildings follow the concept boards (dark stone and metal halls, braziers, House banners, spiked keeps,
upswept wings), and the trefoil turns up in plan view as three buttress fins or landing fins around every core.
Everything is an original design in that style. The player colour (material `Owner`) is a deep red by default and is
used only as an accent: belt stripes, fin tips, wing stripes, banners and tower bands.

- **Survey rover** (`--model rover`, ~4.6 × 2.7 × 2.5, the Fed Mk I is 4.4 × 2.5 × 2.2): low six-wheeler. A pointed
  beak sits over a dark tub, with a faceted cab, bronze-green fender armour and skirt lips over chunky hex-rim wheels,
  two swept rear fins with red tips, a winged sensor head `Dish`, a `Beacon` and decal markers `NameL/NameR/NumberL/NumberR`
  (preview text KORVAX 31).
- **Mining vehicle** (`--model miner [--pose stowed|deployed]`, ~5.6 × 2.8 × 3.1 stowed): tracked digger. The wedge
  body overhangs armoured track skirts, with a toothed plough ram and an ore hopper (`Fill`). The drill `Boom` pivots
  in bronze cheek plates: stowed, it lies back over the hopper with the drill folded along it. Deployed, it swings
  forward and the `Drill` (`String`, `Bit`) hangs straight down ahead of the ram. Four stabiliser legs `Leg0-3` fold
  flat along the flanks and swing out to the ground.
- **Drop ship** (`--model dropship`, ~13.4 long × 11.6 span × 6.2 tall): an arrowhead lander with a raised spine. Its
  down-swept wings end in landing talons, and it has twin engine pods with red-orange exhausts, disruptor barrels at
  the wing roots and a swept dorsal fin carrying the `Beacon`. It carries one vehicle down in four cargo clamps
  (`Clamp0-3`), shown gripping the Klingon rover's cab under the belly.
- **Outpost L1 "House Encampment"** (`--model outpost --level 1`, inside one 25-unit hex): the landed command pod on
  three swept landing fins, with a glowing door and ramp, a sensor head and beacon mast. Around it are two A-frame
  shelters, a brazier, a House banner, an assault-shuttle pad, crates and the rover that came down with it.
- **Outpost L5 "Governor's Keep"** (`--level 5`, inside one hex, wall lined up with the tile): a spiked keep with
  upswept buttress fins inside a battered hex wall with corner towers and a glowing gatehouse with braziers and
  banners. Inside are two longhouses, a pad, crates and the rover.
- **Outpost L10 "Great Hall" capital** (`--level 10`, about three hexes across): the Great Hall stands on a stepped
  plinth, reached by a grand stair between braziers. It has huge upswept wings, a spired tower with sensor head and
  beacon, two flanking keeps, a 60-unit hex curtain wall with towers, banners and a gatehouse, four longhouses and two
  pads.
