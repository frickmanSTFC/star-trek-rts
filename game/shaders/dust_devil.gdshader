// A dust devil (class L, scripts/PlanetView.Veyra.cs): an open, tapered cylinder (narrow at the ground, wide on top)
// that snakes about, with dust streaks spiralling round it and rising. Denser in the middle of the column than at its
// silhouette, fading in at the foot and out toward the top. Lit by the scene (sun, sky) like real dust.
// `strength` fades the whole devil in and out (set from C#); `spin` turns it (sign = direction); `seed` offsets it.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_lambert, specular_disabled, shadows_disabled;

uniform vec3 dust_colour : source_color = vec3(0.66, 0.4, 0.24);
uniform float strength = 0.0;
uniform float spin = 2.5;
uniform float seed = 0.0;
uniform float column_height = 30.0;

varying float rise;   // 0 at the foot .. 1 at the top
varying float around; // 0..1 round the column

float dd_hash(vec2 p_p) {
	vec3 q = fract(vec3(p_p.x, p_p.y, p_p.x) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

// Value noise that repeats every p_period cells in x (seamless round the column).
float dd_noise(vec2 p_p, float p_period) {
	vec2 i = floor(p_p);
	vec2 f = fract(p_p);
	f = f * f * (3.0 - 2.0 * f);
	float x0 = mod(i.x, p_period);
	float x1 = mod(i.x + 1.0, p_period);
	float a = dd_hash(vec2(x0, i.y));
	float b = dd_hash(vec2(x1, i.y));
	float c = dd_hash(vec2(x0, i.y + 1.0));
	float d = dd_hash(vec2(x1, i.y + 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void vertex() {
	rise = clamp((VERTEX.y + column_height * 0.5) / column_height, 0.0, 1.0);
	around = UV.x;
	// The column snakes: the higher, the more it sways.
	float sway = rise * rise;
	VERTEX.x += sin(TIME * 0.9 + seed + rise * 3.0) * sway * 2.5;
	VERTEX.z += cos(TIME * 0.7 + seed * 1.7 + rise * 2.5) * sway * 2.0;
}

void fragment() {
	// Streaks: spiral (x shifts with height), turning with spin, rising with time.
	float x = around * 8.0 + TIME * spin - rise * 3.0 + seed;
	float n = dd_noise(vec2(x, rise * 6.0 - TIME * 1.2), 8.0) * 0.6
		+ dd_noise(vec2(around * 16.0 + TIME * spin * 1.6 + seed, rise * 14.0 - TIME * 2.0), 16.0) * 0.4;
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float core = pow(facing, 0.8);
	float foot = smoothstep(0.0, 0.06, rise);
	float top = 1.0 - smoothstep(0.45, 1.0, rise);
	float a = strength * core * foot * top * smoothstep(0.25, 0.8, n) * (0.55 - 0.25 * rise);
	ALBEDO = dust_colour * (0.85 + 0.3 * n);
	ALPHA = clamp(a, 0.0, 1.0);
}
