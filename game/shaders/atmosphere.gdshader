// Atmosphere halo for the orbital globe: a slightly larger sphere drawn additively, bright only where the
// surface turns away from the viewer (the limb), so the planet gets a thin blue rim.
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never;

uniform vec4 glow : source_color = vec4(0.35, 0.62, 1.0, 1.0);
uniform float power = 3.0;
uniform float strength = 1.6;

void fragment() {
	float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	ALBEDO = glow.rgb * pow(rim, power) * strength;
}
