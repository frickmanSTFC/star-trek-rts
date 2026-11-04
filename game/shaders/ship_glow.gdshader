// Glowing ship parts (deflector, warp grilles, bussard collectors, impulse engines, running lights): unshaded HDR
// colour so the scene's glow blooms it, with an optional slow pulse and an optional blink (a short flash each period).
shader_type spatial;
render_mode unshaded, shadows_disabled;

uniform vec3 colour : source_color = vec3(0.4, 0.7, 1.0);
uniform float energy = 3.0;
uniform float pulse = 0.0;
uniform float pulse_speed = 2.0;
// Flashes per second; 0 = steady.
uniform float blink = 0.0;

void fragment() {
	float k = 1.0 + pulse * sin(TIME * pulse_speed);
	if (blink > 0.0) k *= 0.08 + 0.92 * step(0.88, fract(TIME * blink));
	// A touch brighter facing the camera, softer at the edges.
	k *= 0.75 + 0.25 * clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	ALBEDO = colour * energy * k;
}
