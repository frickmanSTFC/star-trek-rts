// Selection rings, the faint pass drawn through whatever hides them (trees, buildings, the hull) so a selected vehicle
// in a forest still shows where it is (see selection_ring.gdshaderinc; scripts/PlanetView.Selection.cs).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled, fog_disabled, skip_vertex_transform;

#define RING_XRAY
#include "res://shaders/selection_ring.gdshaderinc"
