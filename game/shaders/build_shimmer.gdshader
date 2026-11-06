// Completion shimmer (ConstructionView): a material overlay on every mesh of a building that has just been finished.
// A bright band sweeps up its world-space height (sweep 0 -> 1), with sparkles in the band and a rim glow below it;
// fade takes the whole thing out at the end.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled;

uniform float y_min = 0.0;
uniform float y_max = 1.0;
uniform float sweep : hint_range(0.0, 1.2) = 0.0;
uniform float fade : hint_range(0.0, 1.0) = 1.0;
uniform vec3 shimmer_colour : source_color = vec3(0.55, 0.85, 1.0);

varying vec3 w_pos;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash13(vec3 p_p) {
	vec3 q = fract(p_p * 0.1031);
	q += dot(q, q.zyx + 31.32);
	return fract((q.x + q.y) * q.z);
}

void fragment() {
	float y = mix(y_min - 0.6, y_max + 0.6, sweep);
	float k = (w_pos.y - y) / 0.7;
	float band = exp(-k * k);
	float below = 1.0 - smoothstep(-0.2, 0.2, w_pos.y - y);
	float rim = pow(1.0 - clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 3.0);
	float sparkle = step(0.93, hash13(floor(w_pos * 5.0) + vec3(floor(TIME * 24.0))));
	float a = band * (0.7 + sparkle * 1.5) + rim * below * 0.35;
	ALBEDO = mix(shimmer_colour, vec3(1.0), band * 0.5) * 2.2;
	ALPHA = clamp(a * fade, 0.0, 1.0);
}
