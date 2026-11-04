// Atmosphere for the orbital globe: a shell around the planet whose fragments ray-march the air along the view ray
// (single scattering of the orbit sun: Rayleigh, which turns the limb blue and the terminator orange, and Mie, the
// bright forward haze around the sun). Each step also marches toward the sun, so light reaching low, grazing air is
// reddened and air in the planet's shadow stays dark. Output is premultiplied: the sky light is added and whatever is
// behind (ground, clouds, the moon) is dimmed by the air's transmittance. The same air numbers as planet.gdshader,
// which reddens the sunlight on the ground with them.
shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_back, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 sun_dir = vec3(-0.6, 0.3, 0.75);
uniform float planet_radius = 100.0;
uniform float atmosphere_height = 3.5;
// Vertical optical depth (Earth's, per colour) and scale height of the Rayleigh air, in world units.
uniform vec3 rayleigh_depth = vec3(0.046, 0.108, 0.265);
uniform float rayleigh_height = 0.8;
// Haze (aerosols): optical depth, scale height and forward-scattering anisotropy.
uniform float mie_depth = 0.03;
uniform float mie_height = 0.25;
uniform float mie_g = 0.76;
uniform float sun_intensity = 4.5;

const int VIEW_STEPS = 16;
const int LIGHT_STEPS = 6;
const float PI = 3.14159265;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Ray (origin o relative to the planet centre, unit direction d) against a sphere of radius r: (near, far), or (-1, -1) for a miss.
vec2 sphere(vec3 o, vec3 d, float r) {
	float b = dot(o, d);
	float disc = b * b - (dot(o, o) - r * r);
	if (disc < 0.0) return vec2(-1.0);
	float s = sqrt(disc);
	return vec2(-b - s, -b + s);
}

void fragment() {
	vec3 o = CAMERA_POSITION_WORLD - NODE_POSITION_WORLD;
	vec3 d = normalize(world_pos - CAMERA_POSITION_WORLD);
	vec3 s = normalize(sun_dir);
	float top = planet_radius + atmosphere_height;

	vec2 air = sphere(o, d, top);
	if (air.y <= 0.0) discard;
	float t0 = max(air.x, 0.0);
	float t1 = air.y;
	vec2 ground = sphere(o, d, planet_radius);
	if (ground.x > 0.0) t1 = ground.x;

	vec3 beta_r = rayleigh_depth / rayleigh_height;
	float beta_m = mie_depth / mie_height;
	float ds = (t1 - t0) / float(VIEW_STEPS);
	float od_r = 0.0, od_m = 0.0;
	vec3 sum_r = vec3(0.0), sum_m = vec3(0.0);
	for (int i = 0; i < VIEW_STEPS; i++) {
		vec3 p = o + d * (t0 + ds * (float(i) + 0.5));
		float h = length(p) - planet_radius;
		float dr = exp(-h / rayleigh_height) * ds;
		float dm = exp(-h / mie_height) * ds;
		od_r += dr;
		od_m += dm;
		// In the planet's shadow: no sunlight reaches this sample.
		vec2 blocker = sphere(p, s, planet_radius);
		if (blocker.x > 0.0) continue;
		float dl = sphere(p, s, top).y / float(LIGHT_STEPS);
		float l_r = 0.0, l_m = 0.0;
		for (int j = 0; j < LIGHT_STEPS; j++) {
			float hq = length(p + s * (dl * (float(j) + 0.5))) - planet_radius;
			l_r += exp(-hq / rayleigh_height) * dl;
			l_m += exp(-hq / mie_height) * dl;
		}
		vec3 att = exp(-(beta_r * (od_r + l_r) + 1.1 * beta_m * (od_m + l_m)));
		sum_r += att * dr;
		sum_m += att * dm;
	}

	float mu = dot(d, s);
	float phase_r = 3.0 / (16.0 * PI) * (1.0 + mu * mu);
	float g2 = mie_g * mie_g;
	float phase_m = 3.0 / (8.0 * PI) * ((1.0 - g2) * (1.0 + mu * mu)) / ((2.0 + g2) * pow(1.0 + g2 - 2.0 * mie_g * mu, 1.5));
	vec3 light = sun_intensity * (sum_r * beta_r * phase_r + sum_m * beta_m * phase_m);
	vec3 transmittance = exp(-(beta_r * od_r + 1.1 * beta_m * od_m));

	ALBEDO = light;
	ALPHA = clamp(1.0 - dot(transmittance, vec3(1.0 / 3.0)), 0.0, 1.0);
}
