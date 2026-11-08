// Planet terrain: per-tile colour from the mesh, hex outlines near tile edges, and fog of war
// from a small texture (one texel per tile: 1 = in sight, ~0.4 = explored, ~0.08 = unexplored).
// Day/night, wet ground, puddles, lying snow and night frost come from sky.gdshaderinc (off by default: then it looks
// as before). Ground colours are per planet class, baked into the vertex colours (scripts/PlanetLook.cs).
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;
uniform float grid_strength : hint_range(0.0, 1.0) = 0.22;

#include "res://shaders/sky.gdshaderinc"

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
	vec3 albedo = COLOR.rgb * (1.0 - rim);
	float rough = 0.9;
	float spec = 0.25;
	if (sky_on > 0.5) {
		vec3 lying = sky_cover(world_pos);
		float up = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).y;
		float puddle = sky_puddle(world_pos, up, lying.z);
		float wet = max(lying.y, puddle);
		albedo *= mix(1.0, 0.6, wet);
		rough = mix(rough, 0.35, lying.y);
		rough = mix(rough, 0.04, puddle);
		spec = mix(spec, 0.55, max(lying.y * 0.5, puddle));
		float snow = sky_snow(world_pos, up, lying.x);
		albedo = mix(albedo, sky_snow_colour * (0.93 + 0.07 * sky_noise(world_pos.xz * 2.3)), snow);
		rough = mix(rough, 0.7, snow);
		spec = mix(spec, 0.35, snow);
		// Night frost (class L only; 0 elsewhere): a thin, glittery white crust.
		float frost = sky_frost(world_pos, up) * (1.0 - snow);
		albedo = mix(albedo, sky_frost_colour * (0.9 + 0.1 * sky_noise(world_pos.xz * 7.1)), frost);
		rough = mix(rough, 0.55, frost);
		spec = mix(spec, 0.5, frost);
		vec3 lit = sky_light(world_pos);
		albedo *= lit;
		spec *= min(1.0, (lit.r + lit.g + lit.b) / 3.0);
	}
	ALBEDO = albedo * seen;
	ROUGHNESS = rough;
	SPECULAR = spec * seen;
}
