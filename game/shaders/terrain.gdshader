// Planet terrain: per-tile colour from the mesh, hex outlines near tile edges, and fog of war
// from a small texture (one texel per tile: 1 = in sight, ~0.4 = explored, ~0.08 = unexplored).
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;
uniform float grid_strength : hint_range(0.0, 1.0) = 0.22;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float fog_at(vec3 p) {
	vec2 uv = vec2((p.x / hex_width + 0.5) / map_tiles.x, (p.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
	return texture(fog_tex, uv).r;
}

void fragment() {
	float seen = fog_at(world_pos);
	// UV.x is 0 at a tile's centre and 1 at its corners: darken a thin band at the edge.
	float rim = smoothstep(0.82, 1.0, UV.x) * grid_strength;
	ALBEDO = COLOR.rgb * (1.0 - rim) * seen;
	ROUGHNESS = 0.9;
	SPECULAR = 0.25 * seen;
}
