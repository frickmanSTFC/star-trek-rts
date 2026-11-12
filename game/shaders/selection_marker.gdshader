// A small screen-space chevron over each selected vehicle when the camera is far out (scripts/PlanetView.Selection.cs),
// so a selection can be found on a big map. The mesh is built on the CPU: four vertices per marker, all at the marker's
// world anchor; UV = the quad corner (0..1, v down), UV2.x = half size in pixels, UV2.y = pixels above the anchor,
// COLOR = colour + strength. The vertex shader spreads the corners in screen space, so a marker keeps its pixel size at
// any zoom; drawn over everything (no depth test), never fogged, unshaded.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled, fog_disabled;

uniform float bob_pixels = 3.0;
uniform float bob_speed = 3.2;

varying vec4 marker_colour;

void vertex() {
	marker_colour = COLOR;
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec2 corner = vec2(UV.x * 2.0 - 1.0, 1.0 - UV.y * 2.0); // -1..1, y up
	vec2 pixels = corner * UV2.x + vec2(0.0, UV2.x + UV2.y + bob_pixels * sin(TIME * bob_speed));
	clip.xy += pixels * 2.0 / VIEWPORT_SIZE * clip.w;
	POSITION = clip;
}

void fragment() {
	// A downward-pointing triangle (apex at the bottom, toward the vehicle), as a signed distance in quad units.
	vec2 p = vec2(UV.x * 2.0 - 1.0, 1.0 - UV.y * 2.0); // -1..1, y up
	vec2 n = normalize(vec2(1.3, -0.75));               // outward normal of the right-hand side
	float side = dot(vec2(abs(p.x), p.y + 0.75), n);    // through the apex (0, -0.75) and (0.75, 0.55)
	float top = p.y - 0.55;
	float d = max(side, top);
	float aa = max(fwidth(d), 0.01);
	float fill = 1.0 - smoothstep(-aa, aa, d);
	float edge = 1.0 - smoothstep(0.14 - aa, 0.14 + aa, d);  // dark outline round it
	float glow = (1.0 - smoothstep(0.1, 0.45, d)) * 0.35;
	vec3 inner = marker_colour.rgb * mix(1.35, 0.9, smoothstep(-0.75, 0.55, p.y)); // brighter toward the tip
	vec3 col = mix(mix(marker_colour.rgb, vec3(0.01, 0.02, 0.02), edge), inner, fill);
	ALBEDO = col;
	ALPHA = clamp(max(fill, max(edge * 0.85, glow)) * marker_colour.a, 0.0, 1.0);
}
