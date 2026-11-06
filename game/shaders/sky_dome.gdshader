// The surface sky (scripts/PlanetDriver.Sky.cs): a big inside-out sphere that follows the surface camera, drawn
// only above the horizon (below it the scene's own background shows, as before). Gradient from the horizon colour
// (blending into the haze colour right at the horizon, so distant fogged ground meets it) up to the zenith; the sun's disc and glow; stars at night;
// a lightning flash. Unshaded and out of the fog; it writes no depth, so everything else draws over it.
shader_type spatial;
render_mode unshaded, cull_front, fog_disabled, depth_draw_never, shadows_disabled;

uniform vec3 haze_colour : source_color = vec3(0.03, 0.04, 0.08); // the scene's fog colour: right at the horizon
uniform vec3 horizon_colour : source_color = vec3(0.6, 0.7, 0.85);
uniform vec3 zenith_colour : source_color = vec3(0.25, 0.4, 0.66);
uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);      // world, toward the sun
uniform vec3 sun_colour : source_color = vec3(1.0, 0.95, 0.85);
uniform float sun_strength = 1.0;                 // 0 below the horizon or behind cloud
uniform float stars = 0.0;                        // 0 day .. 1 clear night
uniform float flash = 0.0;                        // lightning
uniform float cloud = 0.0;                        // 0 clear .. 1 overcast (greys and hides the sun and stars)

varying vec3 sky_dir;

void vertex() {
	sky_dir = VERTEX; // the sphere is centred on the camera and never turned: model space = world directions
}

float star_hash(vec3 p_p) {
	vec3 q = fract(p_p * 0.1031);
	q += dot(q, q.zyx + 31.32);
	return fract((q.x + q.y) * q.z);
}

void fragment() {
	vec3 d = normalize(sky_dir);
	if (d.y < -0.02) {
		discard;
	}
	float h = clamp(d.y, 0.0, 1.0);
	vec3 grey = vec3(dot(horizon_colour, vec3(0.3, 0.45, 0.25)));
	vec3 col = mix(horizon_colour, mix(zenith_colour, grey * 0.85, cloud * 0.8), pow(h, 0.55));
	col = mix(haze_colour, col, smoothstep(0.0, 0.07, h)); // meet the fogged far ground at the horizon
	float mu = max(dot(d, normalize(sun_dir)), 0.0);
	float clear = 1.0 - cloud * 0.85;
	col += sun_colour * sun_strength * (smoothstep(0.9993, 0.9996, mu) * 4.0 * clear + pow(mu, 10.0) * 0.3 + pow(mu, 3.0) * 0.08);
	// Stars: one per lit cell of a fine grid of directions, twinkling a little.
	vec3 g = d * 260.0;
	vec3 cell = floor(g);
	float r = star_hash(cell);
	float dot_size = length(fract(g) - 0.5);
	float star = step(0.9965, r) * (1.0 - smoothstep(0.1, 0.42, dot_size)) * (0.55 + 0.45 * sin(TIME * (1.0 + r * 3.0) + r * 40.0));
	col += vec3(0.85, 0.9, 1.0) * star * stars * clear * smoothstep(0.0, 0.12, h) * (0.6 + 1.4 * fract(r * 977.0));
	col += vec3(0.75, 0.8, 1.0) * flash;
	ALBEDO = col;
}
