// Ground rims of hex areas (scripts/PlanetView.Comms.cs: comms coverage per source; PlanetView.Missions.cs: mission
// areas and targets). The mesh is a flat ribbon along hex edges a little above the ground: UV.x = distance along the
// edge (world units), UV.y = 0 .. 1 across the ribbon. Additive and unlit: a soft-edged line with a brighter core, a
// glow travelling along it, optional dashes that crawl along the edge (dash > 0: the dash length in world units).
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

uniform vec3 rim_colour : source_color = vec3(0.45, 0.9, 1.0);
uniform float strength = 1.0;
uniform float dash = 0.0;
uniform float crawl = 0.6;
uniform float pulse = 1.0;

void fragment() {
	float across = 1.0 - abs(UV.y * 2.0 - 1.0);
	float soft = smoothstep(0.0, 0.7, across);
	float core = smoothstep(0.55, 0.95, across);
	float on = 1.0;
	if (dash > 0.0) {
		on = step(0.42, fract(UV.x / dash - TIME * crawl));
	}
	float wave = 0.65 + 0.35 * sin(UV.x * 0.35 - TIME * 2.2) * pulse;
	ALBEDO = mix(rim_colour, vec3(1.0), core * 0.35);
	ALPHA = clamp((soft * 0.55 + core * 0.6) * on * wave * strength, 0.0, 1.0);
}
