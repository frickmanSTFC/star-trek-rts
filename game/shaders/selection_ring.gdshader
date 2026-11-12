// Selection / hover rings, depth-tested pass (see selection_ring.gdshaderinc; scripts/PlanetView.Selection.cs).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled, skip_vertex_transform;

#include "res://shaders/selection_ring.gdshaderinc"
