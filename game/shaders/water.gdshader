// Animated water over ocean and shallow tiles, darkened by the same fog texture as the terrain.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;
uniform vec4 deep_colour : source_color = vec4(0.02, 0.09, 0.2, 0.92);
uniform vec4 shallow_colour : source_color = vec4(0.08, 0.3, 0.42, 0.8);
uniform float wave_height = 0.22;
uniform float wave_speed = 0.7;

varying vec3 world_pos;
varying float wave;

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float t = TIME * wave_speed;
	wave = sin(w.x * 0.11 + t) * 0.6 + cos(w.z * 0.09 - t * 1.3) * 0.4;
	VERTEX.y += wave * wave_height;
	world_pos = w;
}

float fog_at(vec3 p) {
	vec2 uv = vec2((p.x / hex_width + 0.5) / map_tiles.x, (p.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
	return texture(fog_tex, uv).r;
}

void fragment() {
	float seen = fog_at(world_pos);
	// COLOR.r is 1 over deep ocean and 0 over shallows (set per tile in the mesh).
	vec4 base = mix(shallow_colour, deep_colour, COLOR.r);
	// Small ripples bend the normal so highlights move.
	float t = TIME * wave_speed * 2.0;
	vec3 ripple = vec3(sin(world_pos.x * 0.9 + t) * 0.06, 1.0, cos(world_pos.z * 0.8 - t * 1.1) * 0.06);
	NORMAL = normalize((VIEW_MATRIX * vec4(normalize(ripple), 0.0)).xyz);
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float fresnel = pow(1.0 - facing, 3.0);
	ALBEDO = (base.rgb + vec3(0.04, 0.06, 0.08) * (0.5 + 0.5 * wave)) * seen;
	ALPHA = mix(base.a, 1.0, fresnel * 0.6);
	ROUGHNESS = 0.06;
	METALLIC = 0.0;
	SPECULAR = 0.7 * seen;
}
