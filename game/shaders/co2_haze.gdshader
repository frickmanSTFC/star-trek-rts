// CO₂ ground haze (class L, scripts/PlanetView.Veyra.cs): heavier-than-air CO₂ pooling in hollows on calm nights.
// One sheet just above each hollow tile; how much shows is how deep the ground lies under the sheet along the view
// ray (scene depth minus the sheet's depth), so it fills the low ground and thins out on the slopes, like a pool.
// Only where the tile's own daylight says night (sky.gdshaderinc), scaled by `amount` (calm, still air; set from C#)
// and the pool edges' vertex alpha; fog of war hides it like the ground. Drifts slowly (world-space value noise).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_lambert, specular_disabled, shadows_disabled;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;

uniform sampler2D haze_depth : hint_depth_texture, repeat_disable, filter_nearest;
uniform vec3 haze_colour : source_color = vec3(0.66, 0.71, 0.78);
uniform float amount = 0.0;     // 0..1: calm air (PlanetView.UpdateVeyra)
uniform float thickness = 7.0;  // world units of view ray through the haze for full density
uniform float opacity = 0.42;   // densest it gets

#include "res://shaders/sky.gdshaderinc"

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float night = 1.0 - smoothstep(0.02, 0.18, sky_daylight(world_pos));
	if (amount * night <= 0.001) {
		discard;
	}
	// Scene depth behind this pixel, in view space (Forward+ reverse-Z: ndc z = the depth value).
	float depth = texture(haze_depth, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	behind.xyz /= behind.w;
	float gap = length(behind.xyz) - length(VERTEX);
	float thick = smoothstep(0.0, thickness, gap);
	float n = sky_noise(world_pos.xz * 0.045 + vec2(TIME * 0.012, TIME * 0.007)) * 0.6
		+ sky_noise(world_pos.xz * 0.16 - vec2(TIME * 0.02, -TIME * 0.015)) * 0.4;
	vec2 fog_uv = vec2((world_pos.x / hex_width + 0.5) / map_tiles.x, (world_pos.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
	float seen = texture(fog_tex, fog_uv).r;
	float a = amount * night * thick * COLOR.a * (0.5 + 0.7 * n) * opacity * smoothstep(0.1, 0.5, seen);
	ALBEDO = haze_colour;
	EMISSION = haze_colour * 0.035 * seen; // a faint glow so it still reads under a dim night sky
	ALPHA = clamp(a, 0.0, 1.0);
}
