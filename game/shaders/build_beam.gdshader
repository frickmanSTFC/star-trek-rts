// Construction beam (ConstructionView): an open cylinder (local y -0.5..0.5) stretched from an exocomp's emitter to the
// piece it is printing. Additive: a white-hot core and a blue sheath (by how square the surface faces the camera),
// pulses running toward the target, a flicker, faded in at the emitter. phase is set once per pooled beam.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 beam_colour : source_color = vec3(0.35, 0.75, 1.0);
uniform float energy = 3.0;
uniform float phase = 0.0;

varying float along;
varying float beam_len;

void vertex() {
	along = VERTEX.y + 0.5;
	beam_len = length(MODEL_MATRIX[1].xyz);
}

void fragment() {
	float ndv = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	float core = pow(ndv, 7.0);
	float sheath = pow(ndv, 1.6);
	float pulses = 0.55 + 0.45 * sin(along * beam_len * 3.2 - TIME * 18.0 + phase * TAU);
	float flicker = 0.82 + 0.18 * sin(TIME * 47.0 + phase * 31.0) * sin(TIME * 13.0 + phase * 7.0);
	float ends = smoothstep(0.0, 0.06, along);
	float a = clamp((sheath * 0.5 * pulses + core * 0.9) * flicker * ends, 0.0, 1.0);
	ALBEDO = mix(beam_colour, vec3(1.0), core * 0.85) * energy;
	ALPHA = a;
}
