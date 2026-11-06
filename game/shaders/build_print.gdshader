// One build piece being "printed" (ConstructionView). World-space Y: below the cut the piece is solid in its own colour
// (and texture), lit; a ragged glowing band runs along the cut while the exocomps' beams are on (scan); above it the
// piece is a faint blue hologram (rim, scanlines and a lattice). progress 0 = all hologram (the ghost of a queued or
// later piece), 1 = all solid; flash lights the whole piece up as it locks. Swapped for the piece's own materials after.
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx;

uniform float y_min = 0.0;
uniform float y_max = 1.0;
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float ghost : hint_range(0.0, 1.0) = 0.24;
uniform float scan : hint_range(0.0, 1.0) = 0.0;
uniform float flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4 base_colour : source_color = vec4(0.8, 0.82, 0.86, 1.0);
uniform sampler2D albedo_tex : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float metal : hint_range(0.0, 1.0) = 0.5;
uniform float rough : hint_range(0.0, 1.0) = 0.4;
uniform vec3 holo_colour : source_color = vec3(0.45, 0.8, 1.0);

varying vec3 w_pos;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash12(vec2 p_p) {
	vec3 q = fract(vec3(p_p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float noise2(vec2 p_p) {
	vec2 i = floor(p_p);
	vec2 f = fract(p_p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void fragment() {
	float edge = 0.2;
	float ragged = (noise2(w_pos.xz * 2.7 + vec2(w_pos.y * 0.9)) - 0.5) * 2.0 * edge;
	float cut = mix(y_min - edge * 1.5, y_max + edge * 1.5, progress) + ragged;
	float d = w_pos.y - cut;
	float band = (1.0 - smoothstep(0.0, 0.25, abs(d))) * scan;
	float ndv = clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0);
	float rim = pow(1.0 - ndv, 2.0);
	if (d <= 0.0) {
		vec4 tex = texture(albedo_tex, UV);
		ALBEDO = base_colour.rgb * tex.rgb;
		METALLIC = metal;
		ROUGHNESS = rough;
		EMISSION = holo_colour * (band * 3.0 + flash * (0.8 + rim * 2.0));
		ALPHA = 1.0;
	} else {
		float lines = smoothstep(0.82, 1.0, abs(sin(w_pos.y * 8.0 - TIME * 2.5)));
		vec3 g = abs(fract(w_pos * 0.9) - 0.5);
		float lattice = smoothstep(0.45, 0.49, max(g.x, g.z)) * 0.6 + smoothstep(0.46, 0.49, g.y) * 0.4;
		float shimmer = 0.85 + 0.15 * sin(TIME * 7.0 + w_pos.y * 3.0);
		ALBEDO = holo_colour * 0.15;
		ROUGHNESS = 0.6;
		EMISSION = holo_colour * ((0.25 + rim * 1.6 + lines * 0.5 + lattice) * ghost * 2.5 * shimmer + band * 4.0);
		ALPHA = clamp(ghost * (0.25 + rim * 1.2 + lines * 0.4 + lattice) * shimmer + band, 0.0, 1.0);
	}
}
