# Terrain detail textures

Tileable 1024² ground detail for the surface terrain shader (`game/shaders/terrain.gdshader`, the "High-detail terrain"
graphics toggle; visual pass 2026-11-11). Ten ground kinds, three maps each:

| kind | used for |
|---|---|
| `grass` | class M plains |
| `forest_floor` | class M forest (leaf litter, needles, twigs, moss) |
| `dirt` | hills (both classes), anything unknown |
| `sand` | desert / dust flats, shallows seabed |
| `rock` | class M mountains, cliffs |
| `snow` | ice tiles, snowy peaks, lying snow (sky.gdshaderinc) |
| `mud` | wet ground (sky.gdshaderinc wetness) |
| `tundra` | class L plains (frost-shattered gravel, dry tussocks) |
| `lichen` | class L lowlands (lichen rosettes, moss cushions) |
| `basalt` | class L mountains (columnar joints, vesicles) |

- `<kind>_albedo.jpg`: sRGB colour. The shader uses it only as variation around the mesh's per-tile colour
  (texel ÷ the texture's own mean), so every world keeps its palette (PlanetLook) and the far look is unchanged.
- `<kind>_normal.jpg`: tangent-space normal, OpenGL / Godot convention (green = up in the image).
- `<kind>_rough.jpg`: roughness 0..1 (grey).

**Source:** baked procedurally by `blender/bake_terrain_textures.py` (numpy + Pillow, deterministic; periodic FFT
noise, wrapped Voronoi cells, strokes and leaves drawn at the wrapped offsets). Original work for this project, no
third-party content, so no licence terms beyond the repository's. Poly Haven and ambientCG (CC0) were the first choice
but the network refused both when these were made; CC0 sets of the same names can be dropped in later (keep the size
square and the normal convention).

Loaded at run time by `scripts/PlanetView.TerrainDetail.cs` with `Image.LoadFromFile("res://textures/terrain/…")`
into two `Texture2DArray`s (albedo + roughness in alpha, normals), like `textures/planet/`. The `.gdignore` keeps
Godot's importer away, so no import step is needed; **exported builds do not include them unless the export preset's
"Filters to export non-resource files/folders" lists `textures/terrain/*`** (without them the toggle simply leaves the
terrain as before).
