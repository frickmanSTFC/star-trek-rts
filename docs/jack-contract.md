# Jack raid contract (owner 2026-11-11: a joke for Craig)

Owner: "make an alien that attacks bases that is Jack Skellington ... As soon as the base is put down, 1 minute
later it should show an incoming attack from this alien." A one-off Halloween joke: the giant skeleton from the
movie (pinstripe suit, bat bow tie) stomps through the new colony throwing pumpkins and setting bats loose, then
bows and vanishes. It never destroys anything (buildings and vehicles never drop below 1).

## Sim (`game/sim/Surface/Jack.cs`, partial PlanetSurface)

- Off unless `PlanetSurface.JackEnabled` is set (the game sets it for real matches; tests and the balance bot
  leave it off, so nothing else changes).
- Per landed player, timed from touchdown (`SurfacePlayer.LandsAtTick`), seconds (`JackRules`):
  `warn_after 60` → event `warning` (INCOMING ATTACK) with a `arrive_after 20` countdown → `arrived`: rises from
  green mist on a free passable tile ~`spawn_distance 5` hexes from the hub → rampage for `rampage_seconds 150`:
  walks one tile per `step_seconds 1.6` toward the player's structure nearest to him, stops next to it, attacks every
  `attack_seconds 5` (pumpkin, bats, pumpkin, shriek, …) → `leaving` (bows, `leave_seconds 4`) → `gone`.
- Attacks are `JackAttack(Tick, Player, Kind, FromTile, Tile, LandTick, Damage, Hits)`:
  `pumpkin` = thrown at a structure up to 4 hexes away, lands `pumpkin_flight 1.5` s later, hits buildings and
  vehicles within 1 hex; `bats` = everything within 2 hexes of Jack; `shriek` = no damage, an event line.
  Damage is applied at `LandTick`. Randomness comes from a hash of (tick, player), never `sim.Rng`.
- `Jacks` (one `JackRaid` per player once warned), `JackAttacks`, `JackEvents` (`JackEvent(Tick, Player, Kind,
  Text)`, kinds warning / arrived / attack / shriek / leaving / gone) are read by the view.
- Debug: `DebugJack` command (F9 test panel button "SUMMON JACK") starts the warning now for the local player.

## Models (`blender/build_jack.py --game` → `game/models/creatures/`)

Units: 1 Blender unit = 1 game world unit; hexes are 25 wide, buildings ~10–12 wide, so Jack is a **giant, 16.0
tall** (feet at the origin). Blender +Y = front (Godot -Z), +Z = up (Godot +Y); the exporter maps Blender (x, y, z)
to Godot (x, z, -y). Every pivot node is a mesh object whose origin is its joint and whose rest rotation is
identity, so the game animates by setting each node's Basis (identity = rest pose).

`jack.glb` node hierarchy (names exact; children keep their parent's transform chain):

```
Jack (empty, origin at the feet)
└─ Hips            origin at the hip joint (pelvis + coat tails)
   ├─ Torso        origin at the waist: chest, jacket, bat bow tie, shoulders
   │  ├─ Head      origin at the neck base: the big round skull (eye sockets, nose slits, stitched grin)
   │  ├─ ArmUpperL origin at the left shoulder (Jack's left = Blender -X); rest = hanging down, slightly out
   │  │  └─ ArmLowerL  origin at the elbow
   │  │     └─ HandL   origin at the wrist: long spindly fingers
   │  └─ ArmUpperR / ArmLowerR / HandR (mirror, +X)
   │                └─ Pumpkin   (child of HandR) a jack-o'-lantern held in the hand, hidden by the game except
   │                             when throwing; material "PumpkinGlow" for the glowing face
   ├─ LegUpperL    origin at the left hip; rest = straight down
   │  └─ LegLowerL origin at the knee
   │     └─ FootL  origin at the ankle (pointed shoes)
   └─ LegUpperR / LegLowerR / FootR
```

Look: very tall and thin, spider-like limbs, an oversized round white skull with big black eye sockets, two nose
slits and a wide stitched grin; black suit with thin white pinstripes, tailcoat, a black bat-shaped bow tie,
white shirt hint at the collar, pointed black shoes. Materials: "Bone" (skull, hands), "Suit" (pinstripes, baked or
striped geometry), "Black" (bow tie, sockets, mouth), "PumpkinGlow" (emissive orange, strength 1).

`pumpkin.glb`: a jack-o'-lantern ~1.6 across, origin at its centre, material "Pumpkin" + "PumpkinGlow" (emissive
face and stalk top dark green), the projectile.

`bat.glb`: a small bat ~1.4 wingspan: `Body` (origin at its centre) with children `WingL` / `WingR` (origins at the
shoulder; rest = wings spread flat; the game flaps them about the body's front-back axis).

Godot side (`scripts/PlanetView.Jack.cs`, `scripts/JackModel.cs`): loads the glbs with a primitive stand-in of the
same names if missing; walk = legs/arms swing, rise/sink through green mist, throw = ArmUpperR swing + Pumpkin shown
then flies on an arc, bats = a swarm circling out from Jack, bow at the end. HUD: a red INCOMING ATTACK banner with
the countdown, event-feed lines, an alarm sound.

**Built (2026-11-11, `blender/build_jack.py --game`; previews `blender/previews/jack/jack-{hero,game,glb,props}.png`,
sheet `jack-sheet.png`).** jack.glb 0.52 MB, 17 nodes, ~19.2k tris (Head 8.0k, Pumpkin 3.5k, each Hand 1.5k, Torso
1.4k); materials Bone, Suit (a 64×4 pinstripe PNG repeated by the UVs, glTF REPEAT wrap), Black, Pumpkin,
PumpkinGlow, Stalk. pumpkin.glb 0.09 MB, one node `Pumpkin`, 1.58 across, 1.44 tall (stalk included; Godot y
-0.57..0.87). bat.glb 0.04 MB, `Body` + `WingL`/`WingR`, wingspan 1.40; materials Black, BatWing, BatEye (red,
emissive 1). Turns keep their sign between Blender and Godot: +a about X swings a hanging limb forward
(Basis(Vector3.Right, +a)); a wing flap of +a about Blender Y is Basis(Vector3.Forward, +a) in Godot.

Pivot positions at rest, Godot world coordinates (node origin = joint; local position = this minus the parent's):

| node | Godot (x, y, z) | parent |
|---|---|---|
| Jack | (0, 0, 0) | — |
| Hips | (0, 7.3, 0) | Jack |
| Torso | (0, 8.05, 0) | Hips |
| Head | (0, 12.25, 0) | Torso (skull top at y 16.0, skull centre ~(0, 14.28, -0.04), ~3.0 wide) |
| ArmUpperL / R | (∓1.18, 11.42, 0.02) | Torso |
| ArmLowerL / R | (∓1.844, 8.546, 0.02) | ArmUpper |
| HandL / R | (∓2.485, 5.779, -0.222) | ArmLower (fingers hang ~1.5 below the wrist) |
| Pumpkin | (1.69, 5.01, -0.37) | HandR (centre; on the palm side, resting on the curled fingers) |
| LegUpperL / R | (∓0.46, 7.3, 0) | Hips |
| LegLowerL / R | (∓0.46, 3.95, 0) | LegUpper |
| FootL / R | (∓0.46, 0.62, 0) | LegLower (shoe toe tip at z -2.02, heel z 0.44, sole y 0) |

(L = Jack's left = -x.) bat.glb: Body (0, 0, 0); WingL (-0.07, 0.01, -0.04), WingR (0.07, 0.01, -0.04).
Stand-in sizes: torso ~2.6 across the shoulders tapering to ~1.1 at the waist; limbs ~0.4 thick (thigh 0.6);
coat tails hang behind the hips down to y ~4.2.
