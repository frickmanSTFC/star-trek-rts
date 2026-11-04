// Cloud field, shared by clouds.gdshader (the cloud layers) and planet.gdshader (their shadows on the ground).
// A small tiling 3D gradient noise (OrbitView.CloudNoise, one octave, 4 cells across) sampled by the direction from
// the planet centre in the cloud layer's own space, so there are no seams or pinched poles: a very large-scale
// "weather" octave decides where it is cloudy at all (systems with clear skies between), a light domain warp, then
// five octaves of fbm, plus two finer ones scaled by p_detail (1 when the camera is close, 0 far off or for shadows).
uniform sampler3D cloud_noise : filter_linear, repeat_enable;

float cloud_field(vec3 dir, vec3 p_offset, vec3 p_scale, float p_detail) {
	vec3 q = dir * p_scale + p_offset;
	float weather = textureLod(cloud_noise, q * 0.3 + vec3(0.21, 0.47, 0.83), 0.0).r;
	vec3 warp = vec3(
		textureLod(cloud_noise, q * 0.5, 0.0).r,
		textureLod(cloud_noise, q * 0.5 + vec3(0.37, 0.11, 0.71), 0.0).r,
		textureLod(cloud_noise, q * 0.5 + vec3(0.63, 0.89, 0.23), 0.0).r) - 0.5;
	q += warp * 0.15;
	float f = 0.0, a = 0.5, total = 0.0;
	for (int i = 0; i < 5; i++) {
		f += a * textureLod(cloud_noise, q, 0.0).r;
		total += a;
		q = q * 2.07 + vec3(0.13, 0.41, 0.29);
		a *= 0.5;
	}
	if (p_detail > 0.0) {
		for (int j = 0; j < 2; j++) {
			f += a * p_detail * (textureLod(cloud_noise, q, 0.0).r - 0.5);
			q = q * 2.07 + vec3(0.13, 0.41, 0.29);
			a *= 0.5;
		}
	}
	return f / total + (weather - 0.5) * 0.45;
}

// Cover (0..1) from a field value: clear below p_cover, solid p_softness above it; a little cloudier at the equator and
// in the mid-latitude storm belts, clearer in the subtropics.
float cloud_alpha(float p_field, vec3 dir, float p_cover, float p_softness) {
	float lat = asin(clamp(dir.y, -1.0, 1.0));
	return smoothstep(p_cover, p_cover + p_softness, p_field + 0.015 * cos(lat * 7.2));
}
