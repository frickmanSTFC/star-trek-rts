// A soft glow sprite (ConstructionView): where a construction beam lands and at the exocomp's emitter. Camera-facing
// quad (the scale of the node's transform is kept), additive radial falloff plus a faint four-point star.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 flare_colour : source_color = vec3(0.55, 0.85, 1.0);
uniform float energy = 3.0;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(
		vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
		vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	float glow = pow(max(1.0 - r, 0.0), 2.4);
	float star = max(1.0 - abs(c.x) * 14.0, 0.0) + max(1.0 - abs(c.y) * 14.0, 0.0);
	star *= max(1.0 - r, 0.0) * 0.5;
	float hot = pow(max(1.0 - r * 2.2, 0.0), 2.0);
	ALBEDO = mix(flare_colour, vec3(1.0), hot) * energy;
	ALPHA = clamp(glow + star + hot, 0.0, 1.0);
}
