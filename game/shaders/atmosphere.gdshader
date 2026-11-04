// Atmosphere halo for the orbital globe: a slightly larger sphere drawn additively, bright only where the
// surface turns away from the viewer (the limb). Blue on the day side, an orange band at the terminator
// (sunrise and sunset), and only a faint glow on the night side.
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never;

uniform vec4 glow : source_color = vec4(0.35, 0.62, 1.0, 1.0);
uniform vec4 sunset : source_color = vec4(1.0, 0.58, 0.25, 1.0);
uniform vec3 sun_dir = vec3(-0.6, 0.3, 0.75);
uniform float power = 3.0;
uniform float strength = 1.6;

void fragment() {
	float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	vec3 l = normalize((VIEW_MATRIX * vec4(sun_dir, 0.0)).xyz);
	float lit = clamp(dot(NORMAL, l) * 0.5 + 0.5, 0.0, 1.0);
	float day = smoothstep(0.3, 0.75, lit);
	// A thin orange band right at the terminator; on the night side almost nothing (blue + orange there looked pink).
	float dusk = smoothstep(0.35, 0.5, lit) * (1.0 - smoothstep(0.5, 0.65, lit));
	vec3 colour = glow.rgb * day * (1.0 - dusk) + sunset.rgb * dusk * 0.25; // orange replaces blue at the terminator
	ALBEDO = colour * pow(rim, power) * strength;
}
