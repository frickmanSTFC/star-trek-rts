# Crew figures

Federation colony crew (people) for the surface, built procedurally by `blender/build_crew.py` (owner 2026-11-07:
"new high-detail crew figures"). Original designs in the spirit of a Federation colony crew: no canon uniform,
insignia or likeness is copied. The badge is a gold hexagon with a raised chevron, made up for this colony service.

```
/home/user/.bpyvenv/bin/python blender/build_crew.py --game                                  # all roles -> game/models/crew/
/home/user/.bpyvenv/bin/python blender/build_crew.py --game --preview blender/previews/crew   # + Cycles previews (~9 min)
python3 blender/build_crew.py --sheet blender/previews/crew                                  # contact sheet (system PIL)
```

Flags: `--role engineer,eva`, `--variant N` (same seed for every preview figure), `--res 900`, `--samples 24`,
`--shots turn,lineup,poses,walk,run,variants,faces,lod,game`, `--out-dir DIR` (export somewhere else). The build alone
takes ~5 s for all five roles.

## Roles

| id | look | props / extras |
|---|---|---|
| `engineer` | charcoal work jumpsuit, Owner shoulder yoke + sleeve bands, utility harness with D-rings and chest buckle, tool belt with pouches (welder handle, torch), knee pads, elbow patches, sleeve pocket, work gloves, laced boots with toe caps | Tool (plasma welder) |
| `science` | slate-blue jumpsuit with light side panels, Owner yoke, crossed shoulder straps, scanner holster, PADD on the thigh, bare hands | SampleCase (left hip, always shown), Scanner |
| `security` | near-black jumpsuit under a grey armour vest (raised plates, side straps, red stripe, pouches, radio), Owner shoulder pads over grey ones, elbow and knee pads, thigh holster, black gloves | |
| `eva` | white environment suit: bubble helmet with a gold `Visor`, visor frame, life-support backpack with tanks and vents, chest control unit with status lights, hoses, bellows joints, Owner arm/leg bands and chest stripe, gauntlets, heavy boots | HelmetLamp (emissive, always shown) |
| `colonist` | ochre tunic with a wrap collar and hem, sash with a knot, rolled sleeves (bare forearms), trousers, soft boots, Owner scarf, satchel on a cross strap | |

Every role: seams (princess, side, trouser), chest pockets with flaps and buttons, cuffs, collar, badge, name tape,
zip placket, belt with buckle (jumpsuits), soled boots with heel blocks and welts, gloves or bare five-part hands
(index, middle group, thumb), a stylised-realistic head: eye sockets with eyeballs, irises and upper lids, brow
ridge, cheekbones, nose with nostrils, lips, chin, ears, eyebrows.

## Variants

`--variant N` in Blender and `CrewModels.Build(role, colour, variantSeed)` in Godot pick the same look (one hash,
`h32` / `CrewModels.Hash`, checked equal on 12 seeds): presentation (blend shape `Feminine` 0 / 0.5 / 1),
height ×0.93–1.06, build (`Heavy` or `Slim` 0.3–1), skin tone (8), hair colour (8, weighted), hair style
(`HairCrop`, `HairShort`, `HairSwept`, `HairBun`, `HairCurly` or bald), `Beard`. EVA always has HairCrop under the
helmet. Keep `SKIN_TONES`, `HAIR_COLOURS`, `HAIR_WEIGHTS`, `STYLE_MASC`, `STYLE_FEM` and `variant()` in step with
`game/scripts/CrewModels.cs`.

## Scale and frame

Blender +Z up, +Y = front (Godot sees -Z), 1 unit = 1 world unit = 1 m, origin between the feet on the ground.
Nominal height 1.76. Measured from the glbs: the L1 outpost crew door is 1.75 tall; the survey rover Mk I door runs
from a 0.85 sill to a 1.75 top (0.9 tall, step 0.8), the Mk II's from 0.99 to 1.57 (step 0.86). So a crew member
fits the outpost door, and next to the rover their head is level with its door top (see `crew-lineup.png`): the
rovers are a little compact for real people. The old primitive repair crew (`BuildingModels.Crew`) is about 2 tall.
A hex (`PlanetView.HexWidth`) is 25, so at the usual RTS zoom a person is a few pixels: that is what the far LOD is for.

## Rig and parts

Armature `Rig`, Godot `SkeletonProfileHumanoid` names (no bone map needed for retargeting): `Root` (not deforming,
never keyed), `Hips`, `Spine`, `Chest`, `UpperChest`, `Neck`, `Head`, and per side `Left`/`Right` + `Shoulder`,
`UpperArm`, `LowerArm`, `Hand`, `ThumbProximal`, `ThumbDistal`, `IndexProximal`, `IndexIntermediate`,
`MiddleProximal`, `MiddleIntermediate` (drives middle, ring and little fingers), `UpperLeg`, `LowerLeg`, `Foot`,
`Toes`. Rest pose = A-pose (arms 36° from vertical). Weights: distance to the nearest bones of a per-part candidate list.

Nodes in each glb: meshes `<role>` (hero) and `<role>_far` (far LOD), both with blend shapes `Feminine`, `Heavy`,
`Slim`; `HairCrop`, `HairShort`, `HairSwept`, `HairBun`, `HairCurly`, `Beard` (show one or none); props `Tool`,
`Scanner`, `Crate` (+ `SampleCase` on science, `HelmetLamp` on eva), all skinned to the hand / chest / hips / head
bones; marker nodes `ToolTip`, `ScannerTip` (right hand) and `LampPoint` (head) for effects.

Materials: `Crew` (one 64×64 palette atlas, base colour + metal/roughness, for every fixed colour), `Skin` and `Hair`
(tinted per variant), `Owner` (recoloured per player), `Visor` (gold metal), `Lamp`, `Screen`, `Glow` (emissive).

## Actions

30 fps, in place (the Hips sway and bob, Root never moves), last key = first so they loop; `CrewModels` sets loop
mode Linear on import.

| action | frames | notes |
|---|---|---|
| Idle | 90 | weight shift, breathing, head turns |
| Walk | 32 | feet planted at 1.09 u/s (`CrewModels.WalkSpeed`) |
| Run | 20 | 2.92 u/s, fists |
| Work | 60 | crouched over a job with the Tool (welder) |
| Carry | 36 | Crate held at the chest, 0.78 u/s |
| Scan | 96 | Scanner held up and swept, the other hand taps it |
| Wave | 40 | right hand waving overhead |

Props per action (`CrewModels.Play`): Work → Tool, Scan → Scanner, Carry → Crate; the others hidden.

## Budgets (2026-11-08 export)

| role | hero tris | far tris | glb |
|---|---|---|---|
| engineer | 10,058 | 1,176 | 0.99 MB |
| science | 8,854 | 1,164 | 0.90 MB |
| security | 10,340 | 1,188 | 1.02 MB |
| eva | 7,930 | 1,284 | 0.77 MB |
| colonist | 8,364 | 1,208 | 0.84 MB |

Hair styles 476–616 tris each, beard 208, props 96–316. To stay near 1 MB, `slim_glb()` rewrites the export: no
morph normals, only Hips translation + bone rotation channels (all rotations kept, constant ones too, so switching
actions never leaves a bone behind), skin weights and rotations stored as normalised shorts (core glTF; Godot's
importer decodes them), then repacks the buffer. Resolution knobs: `TORSO_SEGS`, `HEAD_LONS`, `LIMB_SEGS`, `BAND_T`,
`BAND_L`, `TORSO_Z`.

## Godot

`game/scripts/CrewModels.cs` (not wired into anything yet): `Build(role, ownerColor, variantSeed)` returns a Node3D
wrapper holding the glb (or a primitive capsule stand-in with the same part and action names when the glb is
missing/not imported); `Player(node)` = its AnimationPlayer; `Play(node, action, blend)`; `Variant(seed, role)`;
`SpeedOf(action)`. The hero mesh, hair and beard fade out at 60 units (`HeroRange`), the far LOD in.

## Previews (`blender/previews/crew/`)

`crew-sheet.png` (contact sheet of everything), `crew-lineup.png` (the five roles beside the survey rover Mk I, one
more at its door), `<role>-turn.png` (front, 3/4, side, back), `crew-poses.png` (one frame of every action),
`crew-walk.png` / `crew-run.png` (cycles), `crew-variants.png`, `crew-faces.png`, `crew-lod.png` (hero vs far),
`crew-game.png` (game camera around the rover).

## Open questions for the owner

1. **Where do crew appear?** Walking from the outpost door to repairs (replacing `BuildingModels.Crew`), around
   buildings at work, on the landing pad as drop ships unload, as the survey team, EVA on hostile worlds?
2. **Do figures map to the `crew` resource?** One figure per N crew in a base, or only decoration?
3. **Faction variants:** Klingon, Romulan, Cardassian etc. crews (same rig and actions, other suits and heads)?
4. **Scale:** keep people at 1.76 (fits the outpost door, a little big for the rover doors) or shrink them?
5. **Roles and actions:** more roles (medic, pilot), more actions (sit/drive, climb into a rover, repair kneeling)?
6. **Not seen in Godot yet:** check the glbs import (blend shape names, normalised weights/rotations, the
   KHR_materials_emissive_strength lamp), the LOD hand-over distance, and the action blend times.
