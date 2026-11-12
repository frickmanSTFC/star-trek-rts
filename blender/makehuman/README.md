# MakeHuman data subset (CC0)

Compact copies of the MakeHuman 1.x assets that `blender/build_portraits_mh.py` uses, so the portraits build in a
fresh container without a MakeHuman checkout.

Source: <https://github.com/makehumancommunity/makehuman> (branch master, commit
a8bc2d54ff0ac92e78ff71431b1023eda42bf482), folder `makehuman/data/`. The MakeHuman assets (base mesh, targets, rig,
weights) were released as **CC0 1.0** in September 2020 (see `LICENSE.ASSETS.md` in that repository and the header of
`3dobjs/base.obj`: "This asset was explicitly released as CC0 in september 2020"; copyright holders at the time Data
Collection AB, Joel Palmius, Jonas Hauquier; targets originally (C) 2014 Manuel Bastioni). No MakeHuman code is
included: the script has its own loader and macro weighting.

| file | what | from |
| --- | --- | --- |
| `base.npz` | `verts` (19158 x 3, decimetres, Y up, +Z forward), `f:<group>` quad faces of every non-joint face group (`body`, `helper-tights`, `helper-l/r-eye`, `helper-l/r-eyelashes-1/2`, `helper-upper/lower-teeth`, `helper-tongue`, ...), `j:<joint-group>` vertex indices of the joint helper cubes | `3dobjs/base.obj` (hm08) |
| `targets.npz` | 516 targets as `<name>:i` (uint16 vertex index) + `<name>:d` (int16 offset x 1000, decimetres; the .target files have three decimals, so this is lossless): `macro/<race>-<gender>-<young/old>`, `universal/<gender>-<age>-<muscle>-<weight>`, every target in `head`, `forehead`, `eyebrows`, `eyes`, `nose`, `mouth`, `cheek`, `chin`, `ears`, `neck`, `torso`, `asym`, and `expr/<race>/<unit>` (the 34 face expression units for african / asian / caucasian) | `targets/**.target` |
| `rig.json.gz` | the default skeleton: bones (head/tail joint names, parent), joints (vertex lists) and vertex weights | `rigs/default.mhskel`, `rigs/default_weights.mhw` |

Rebuild from a checkout: `python3 blender/build_portraits_mh.py --vendor --mh /path/to/makehuman` (no bpy needed).
Not vendored (not needed for the stills): child/baby macro targets, height/proportion/breast/measure targets, the
eye mesh (the script builds its own eyeballs on `helper-l/r-eye`), skins, hair and eyebrow assets (those live on a
separate asset host), and `poseunits/face-poseunits.*` (the 60 face pose units for the default rig: the plan for the
game's talking busts, see the script's docstring).
