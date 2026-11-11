# Visual pass: contract

Owner 2026-11-11 (after asking about Unity / Unreal; Claude advised staying on Godot and doing a visual pass): "do the
visual pass after the merge. Is this something we can turn on or off ingame to see what the difference is?" → yes:
a GRAPHICS section in Settings (preset Classic / Enhanced / Custom + one toggle per feature, saved) and a live compare
key that flips Classic ↔ the player's settings with a frame-time readout.

Render-only: no sim changes, no data changes. Classic must look exactly like the game before this pass (every new
thing is gated on its flag; the old values stay as the Classic branch). Godot can't run here and shaders can't be
compiled: write Godot 4.7 shading language carefully and re-read it; `dotnet build` type-checks the C#. Keep frame cost
in mind (Craig's PC may be modest): each feature should be cheap enough alone, and the FPS readout lets testers see.

## Shared API (stub committed): `game/scripts/GraphicsQuality.cs`

`GraphicsPreset`, `GraphicsQuality.Preset`, `CompareClassic`, the `*Setting` toggles, the effective flags `Gi`, `Fog`,
`Grading`, `Shadows`, `Terrain`, `Decals`, `Effects`, `AntiAlias`, `ShowFps`, event `Changed`, `NotifyChanged()`.
G1 owns the file (may add members, keep these); G2 only reads the effective flags and listens to `Changed`.

## G1: lighting, atmosphere, post, settings, compare key

Files: `scripts/GraphicsQuality.cs`, the `[graphics]` section in `scripts/GameSettings.cs` (load/save/defaults),
the GRAPHICS section in `scripts/SettingsPanel.cs` (preset + toggles + "Show frame time"), new
`scripts/PlanetDriver.Graphics.cs` (apply to the surface `WorldEnvironment`, Sun/Moon lights and camera; compare key
**F7** (free) on the surface and in orbit: flips `CompareClassic`, banner "CLASSIC" / "ENHANCED" for 1.5 s; FPS +
frame-ms readout top-left while comparing or ShowFps), small hooks in `PlanetDriver.Sky.cs` (it writes fog/ambient/
glow every frame: make those the Classic branch and add the Enhanced values), `scripts/PlanetLook.cs` (per world class
grade: colour temperature, saturation, contrast, fog tint), `scripts/MainMenu.cs` (its own environment follows the
grading flag), `scenes/Planet.tscn` environment defaults only if needed.
- Gi: SDFGI on the surface (cascades sized to the map/zoom, y_scale, energy, bounce) + SSIL; off in orbit.
- Fog: volumetric fog (density from the existing weather fog, albedo from the sky/fog colour, sun/moon scattering =
  light shafts at dawn/dusk and through dust; headlight cones scatter); keep the height fog look.
- Grading: tonemap (AgX or Filmic, exposure/white tuned so day isn't washed out and night stays readable — owner
  asked twice for brighter nights), adjustment (contrast/saturation) + per-world-class colour grade, glow tuned
  (threshold/bloom so lamps, beams and flames bloom without blowing out).
- Shadows: directional shadow quality (split distances to the zoom, soft PCF, blend splits), SSAO tuned; lights'
  shadow budget unchanged.
- AntiAlias: TAA + FSR 2 (scale 1, sharpness) or MSAA fallback; off = today's setting.
- Orbit view: grading + glow apply; Gi/Fog not.

## G2: terrain, decals, effects, hero detail

Files: `shaders/terrain.gdshader` (+ `rock.gdshader` / `foliage.gdshader` / `water.gdshader` only for detail),
new texture sets under `game/textures/terrain/` (tileable 1024² albedo + normal + roughness per ground kind: grass/
plains, forest floor, dirt, sand, rock, snow, mud, tundra/lichen for class L; CC0 sources such as Poly Haven /
ambientCG if the network allows (credit them in a README there), else baked procedurally in Blender like the other
assets; keep the set ≲ 20 MB), new `scripts/PlanetView.Decals.cs` (pooled `Decal` nodes: tyre/track marks behind
moving ground vehicles that fade over ~60 s, scorch + blast ring where drop ships land and lift off, dust/oil stains
and worn ground round buildings, worn footpaths along crew walker routes; caps on counts), new
`scripts/PlanetView.Effects.cs` (heat shimmer over reactor/refinery/foundry stacks and rocket plumes, embers at
furnaces, drifting dust motes in sunbeams, light spray of particles from drone rotors near the ground) — all gated on
`GraphicsQuality.Terrain` / `Decals` / `Effects` and rebuilt on `Changed`; hero detail: close-zoom material polish
for vehicles/buildings where cheap (detail normal / roughness variation on the shared grime overlay, `grime.gdshader`).
Terrain shader: when Terrain is on, triplanar detail albedo/normal/roughness by ground kind blended with the existing
colours (keep the fog-of-war, sky, snow/wet/puddle and frost logic intact), macro variation to kill tiling, and the
far look unchanged from above (detail fades with distance); off = exactly today's shader path.

Both: commit often (the container can restart), `cd game && dotnet build FinalFrontier.sln && dotnet test` (634),
don't edit CLAUDE.md/README.md, don't push; end with a report (what, files, frame-cost notes, what to check in Godot,
a ready CLAUDE.md paragraph).
