// Dirt on a vehicle, drawn as a material overlay over its own paint (RoverLook.Grime). Three layers, each spreading
// from where it collects first as its amount goes 0 -> 1: dust (caked low, a film thinning upward), mud (splashes low
// down, broken up) and snow (lying on upward faces, slush low down). Works in the vehicle's model space (to_model,
// set every frame) so the pattern rides along with the vehicle instead of sliding over it.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform mat4 to_model;
uniform float body_height = 2.4;
uniform vec3 dust_colour : source_color = vec3(0.74, 0.6, 0.41);
uniform vec3 mud_colour : source_color = vec3(0.22, 0.16, 0.1);
uniform vec3 snow_colour : source_color = vec3(0.92, 0.94, 0.97);
uniform float dust : hint_range(0.0, 1.0) = 0.0;
uniform float mud : hint_range(0.0, 1.0) = 0.0;
uniform float snow : hint_range(0.0, 1.0) = 0.0;
// The fan the wipers sweep on the windscreen (model space; see RoverLook.WiperArea): it stays clean while they run.
uniform vec3 wiper0;
uniform vec3 wiper1;
uniform vec3 wiper_x = vec3(1.0, 0.0, 0.0);
uniform vec3 wiper_y = vec3(0.0, 1.0, 0.0);
uniform float wiper_len = 0.0;
uniform float wiper_sweep = 1.75;
uniform float wiped : hint_range(0.0, 1.0) = 0.0;

varying vec3 m_pos;
varying vec3 m_normal;

void vertex() {
	vec4 world_pos = MODEL_MATRIX * vec4(VERTEX, 1.0);
	m_pos = (to_model * world_pos).xyz;
	vec3 world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	m_normal = normalize((to_model * vec4(world_normal, 0.0)).xyz);
}

float hash13(vec3 p_p) {
	vec3 q = fract(p_p * 0.1031);
	q += dot(q, q.zyx + 31.32);
	return fract((q.x + q.y) * q.z);
}

float value_noise(vec3 p_p) {
	vec3 i = floor(p_p);
	vec3 f = fract(p_p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float n000 = hash13(i);
	float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
	float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
	float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
	float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
	float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
	float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
	float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
	float x00 = mix(n000, n100, u.x);
	float x10 = mix(n010, n110, u.x);
	float x01 = mix(n001, n101, u.x);
	float x11 = mix(n011, n111, u.x);
	return mix(mix(x00, x10, u.y), mix(x01, x11, u.y), u.z);
}

float fbm(vec3 p_p) {
	float sum = 0.0;
	float amp = 0.5;
	vec3 q = p_p;
	for (int k = 0; k < 4; k++) {
		sum += amp * value_noise(q);
		q = q * 2.03 + vec3(1.7, 9.2, 3.1);
		amp *= 0.5;
	}
	return sum / 0.9375;
}

// How much of a layer shows where its "collects here first" field is p_field, at p_amount (0..1).
float spread(float p_field, float p_amount, float p_edge) {
	float threshold = 1.0 - p_amount;
	return smoothstep(threshold - p_edge, threshold + p_edge, p_field) * smoothstep(0.0, 0.04, p_amount);
}

// 1 inside the fan one wiper sweeps (on the glass plane, within the blade's reach, between parked and fully up).
float wiper_fan(vec3 p_pos, vec3 p_pivot) {
	vec3 d = p_pos - p_pivot;
	float u = dot(d, wiper_x);
	float v = dot(d, wiper_y);
	float off_glass = abs(dot(d, normalize(cross(wiper_x, wiper_y))));
	float r = length(vec2(u, v));
	float ang = atan(v, u);
	float reach = 1.0 - smoothstep(wiper_len * 0.94, wiper_len * 1.02, r);
	float inner = smoothstep(wiper_len * 0.08, wiper_len * 0.14, r); // the hub area near the pivot isn't wiped
	float arc = smoothstep(-0.06, 0.0, ang) * (1.0 - smoothstep(wiper_sweep, wiper_sweep + 0.06, ang));
	return reach * inner * arc * (1.0 - smoothstep(0.05, 0.09, off_glass));
}

void fragment() {
	float h = clamp(m_pos.y / body_height, 0.0, 1.0);
	float low = 1.0 - h;
	float broad = fbm(m_pos * 1.6);
	float fine = fbm(m_pos * 7.0 + vec3(5.0, 1.0, 3.0));

	// Dust: collects low first, then films the whole body (never quite opaque).
	float dust_a = spread(0.62 * low + 0.38 * broad, dust * 0.92, 0.1) * 0.88;
	// Mud: splashes low down, ragged.
	float mud_a = spread(0.6 * low * low + 0.4 * fine, mud * 0.85, 0.05);
	// Snow: on upward-facing surfaces first, plus slush low down.
	float up = clamp(m_normal.y, 0.0, 1.0);
	float snow_a = max(spread(0.72 * up + 0.28 * broad, snow, 0.06), spread(0.55 * low * low + 0.45 * fine, snow * 0.6, 0.05) * 0.8);

	// Composite the layers (premultiplied "over"): dust, then mud on top, then snow on top.
	vec3 col = dust_colour * (0.85 + 0.3 * fine) * dust_a;
	float a = dust_a;
	col = col * (1.0 - mud_a) + mud_colour * (0.8 + 0.4 * fine) * mud_a;
	a = a + mud_a * (1.0 - a);
	col = col * (1.0 - snow_a) + snow_colour * snow_a;
	a = a + snow_a * (1.0 - a);

	// Clear the wiped fans (premultiplied, so scale colour and coverage together).
	if (wiper_len > 0.0 && wiped > 0.0) {
		float fan = max(wiper_fan(m_pos, wiper0), wiper_fan(m_pos, wiper1));
		float keep = 1.0 - wiped * fan * 0.96;
		col *= keep;
		a *= keep;
	}

	ALBEDO = col / max(a, 0.001);
	ALPHA = a;
	ROUGHNESS = mix(0.95, 0.55, mud_a);
	SPECULAR = 0.3;
	METALLIC = 0.0;
}
