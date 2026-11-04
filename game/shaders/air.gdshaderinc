// The planet's air, shared by planet.gdshader and clouds.gdshader (atmosphere.gdshader declares the same numbers; keep them in step).
uniform float planet_radius = 100.0;
uniform float atmosphere_height = 3.5;
// Vertical optical depth (Earth's, per colour) and scale height of the Rayleigh air, in world units; then the haze (Mie).
uniform vec3 rayleigh_depth = vec3(0.046, 0.108, 0.265);
uniform float rayleigh_height = 0.8;
uniform float mie_depth = 0.03;
uniform float mie_height = 0.25;

// Relative optical depth of air toward a light at cosine mu: 1 overhead, about sqrt(pi R / 2H) at the horizon.
float airmass(float mu, float scale_height) {
	float c = 1.0 / sqrt(PI * planet_radius / (2.0 * scale_height));
	float m = max(mu, 0.0);
	return 1.0 / (m + c * exp(-m / c * 0.7));
}

// Colour of sunlight after crossing the air to a point where the sun is at cosine mu (warm near the terminator).
// thickness scales the air crossed (1 = from the ground; less for something higher up, like clouds).
vec3 sunlight_at(float mu, float thickness) {
	return exp(-(rayleigh_depth * airmass(mu, rayleigh_height) + 1.1 * mie_depth * airmass(mu, mie_height)) * thickness);
}
