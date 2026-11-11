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
// Rover damage (RoverLook.Grime.Damage, from integrity: 0 at 75 and above, 1 at 0): scratches through the paint first,
// then sooty scorch marks spreading over the hull. Drawn under the dirt.
uniform float damage : hint_range(0.0, 1.0) = 0.0;
// Visual pass (GraphicsQuality.Terrain, RoverLook.Grime.Detail): close to the camera, a faint permanent weathering
// (rain streaks down the sides, a road film low on the body) and relief on caked dirt (a bump from the dirt's own
// noise), fading out between detail_near and detail_far. 0 = exactly the overlay as before.
uniform float detail_on = 0.0;
uniform float detail_near = 30.0;
uniform float detail_far = 80.0;

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

	// Close-zoom detail (detail_on is a uniform, so the derivatives here are taken in uniform control flow).
	float close = 0.0;
	float weather_a = 0.0;
	float bump_x = 0.0;
	float bump_y = 0.0;
	vec3 dpdx = vec3(0.0);
	vec3 dpdy = vec3(0.0);
	if (detail_on > 0.5) {
		close = 1.0 - smoothstep(detail_near, detail_far, length(VERTEX));
		float run = value_noise(vec3(m_pos.x * 7.0, m_pos.y * 0.6, m_pos.z * 7.0) + vec3(11.0, 0.0, 5.0));
		float streaks = smoothstep(0.6, 0.85, run) * (0.4 + 0.6 * broad) * (1.0 - up);
		float film = low * low * low * (0.55 + 0.45 * broad);
		weather_a = clamp(streaks * 0.16 + film * 0.2, 0.0, 0.3) * close;
		float relief_h = fbm(m_pos * 18.0 + vec3(2.0, 7.0, 1.0)) * 0.025;
		bump_x = dFdx(relief_h);
		bump_y = dFdy(relief_h);
		dpdx = dFdx(VERTEX);
		dpdy = dFdy(VERTEX);
	}

	// Weathering first (detail only), then damage under the dirt: bright scratches (thin streaks along the body, in
	// patches), then dark scorch.
	vec3 col = mix(dust_colour, vec3(0.16, 0.14, 0.12), 0.6) * weather_a;
	float a = weather_a;
	if (damage > 0.0) {
		float streak = smoothstep(0.86, 0.95, value_noise(vec3(m_pos.x * 1.3, m_pos.y * 34.0, m_pos.z * 1.3) + vec3(3.0)));
		float scratch_a = streak * spread(fbm(m_pos * 1.1 + vec3(17.0, 3.0, 9.0)), min(1.0, damage * 1.6), 0.08) * 0.85;
		float scorch_a = spread(0.7 * fbm(m_pos * 1.9 + vec3(41.0, 7.0, 23.0)) + 0.3 * h, max(0.0, damage * 1.25 - 0.3), 0.07) * 0.9;
		col = col * (1.0 - scratch_a) + vec3(0.62, 0.61, 0.58) * scratch_a;
		a = a + scratch_a * (1.0 - a);
		col = col * (1.0 - scorch_a) + vec3(0.05, 0.045, 0.04) * (0.7 + 0.6 * fine) * scorch_a;
		a = a + scorch_a * (1.0 - a);
	}

	// Composite the layers (premultiplied "over"): dust, then mud on top, then snow on top.
	col = col * (1.0 - dust_a) + dust_colour * (0.85 + 0.3 * fine) * dust_a;
	a = a + dust_a * (1.0 - a);
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

	// Caked dirt stands off the paint a little (surface-gradient bump in view space; detail only).
	float relief = (mud_a + dust_a * 0.35 + snow_a * 0.25 + weather_a * 0.5) * close;
	if (relief > 0.001) {
		vec3 r1 = cross(dpdy, NORMAL);
		vec3 r2 = cross(NORMAL, dpdx);
		float det = dot(dpdx, r1);
		vec3 grad = sign(det) * (bump_x * r1 + bump_y * r2);
		vec3 bumped = abs(det) * NORMAL - grad * relief;
		if (dot(bumped, bumped) > 0.0) {
			NORMAL = normalize(bumped);
		}
	}

	ALBEDO = col / max(a, 0.001);
	ALPHA = a;
	ROUGHNESS = mix(0.95, 0.55, mud_a) + (fine - 0.5) * 0.2 * close;
	SPECULAR = 0.3;
	METALLIC = 0.0;
}
