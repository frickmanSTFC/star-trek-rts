// Cloud field, shared by clouds.gdshader (the cloud layers) and planet.gdshader (their shadows on the ground).
// A small tiling 3D noise texture (one octave) summed at five scales after a gentle domain warp, sampled by the
// direction from the planet centre in the cloud layer's own space, so there are no seams or pinched poles.
uniform sampler3D cloud_noise : filter_linear, repeat_enable;

float cloud_field(vec3 dir, vec3 p_offset, vec3 p_scale) {
	vec3 q = dir * p_scale + p_offset;
	vec3 warp = vec3(
		textureLod(cloud_noise, q * 0.5, 0.0).r,
		textureLod(cloud_noise, q * 0.5 + vec3(0.37, 0.11, 0.71), 0.0).r,
		textureLod(cloud_noise, q * 0.5 + vec3(0.63, 0.89, 0.23), 0.0).r) - 0.5;
	q += warp * 0.6;
	float f = 0.0, a = 0.5, total = 0.0;
	for (int i = 0; i < 5; i++) {
		f += a * textureLod(cloud_noise, q, 0.0).r;
		total += a;
		q = q * 2.07 + vec3(0.13, 0.41, 0.29);
		a *= 0.5;
	}
	return f / total;
}

// Cover (0..1) from a field value: clear below p_cover, solid p_softness above it; a little cloudier at the equator and
// in the mid-latitude storm belts, clearer in the subtropics.
float cloud_alpha(float p_field, vec3 dir, float p_cover, float p_softness) {
	float lat = asin(clamp(dir.y, -1.0, 1.0));
	return smoothstep(p_cover, p_cover + p_softness, p_field + 0.015 * cos(lat * 7.2));
}
