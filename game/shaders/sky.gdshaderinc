// Day/night and weather on the planet surface, shared by terrain, water, foliage and rock. Include it AFTER the
// shader's own map_tiles and hex_width uniforms (the fog texture's layout: one texel per tile).
// scripts/PlanetDriver.Sky.cs fills these every frame through PlanetView.SetSky. The defaults (sky_on 0: no darkening,
// no snow, dry ground) leave every material exactly as it looked before day/night existed, and so does a noon, clear,
// dry answer from Climate (daylight 1 on the tile and at the view's centre -> sky_light() = 1, cover 0).
//
// sky_day_tex  one texel per tile, R = the tile's daylight 0..1 (Climate.At), baked while the subsolar column was S0;
//              the star has moved sky_day_shift columns since, so it is read shifted (the map wraps east-west).
// sky_ref_shade  sky_shade() of the daylight the scene's own lights carry right now (the Sun, the moon and the ambient
//              are set for the view's centre): a tile lit less than that is darkened by the ratio, a brighter one is
//              brightened a little, so the night side reads dark from high above while the view's centre stays right.
// sky_wx_tex   one texel per tile: R = snow lying on the ground, G = wetness, B = puddles, A = night frost (render-only
//              accumulation in scripts/SkyField.cs). The frost shows only with sky_frost_on (class L, PlanetLook.Frost),
//              so worlds without it look exactly as before.
// Names are prefixed sky_ (uniforms, functions) and p_ (parameters) so nothing shadows the including shader's globals.

uniform float sky_on = 0.0;
uniform sampler2D sky_day_tex : hint_default_white, filter_linear, repeat_enable;
uniform float sky_day_shift = 0.0;
uniform vec3 sky_ref_shade = vec3(1.0);
uniform sampler2D sky_wx_tex : hint_default_black, filter_linear, repeat_disable;
uniform vec3 sky_snow_colour : source_color = vec3(0.86, 0.89, 0.95);
uniform float sky_rain = 0.0; // 0..1 falling at the view's centre (water roughens)
uniform float sky_frost_on = 0.0; // 1 on worlds with night frost (PlanetView.Veyra.cs)
uniform vec3 sky_frost_colour : source_color = vec3(0.8, 0.85, 0.9);

vec2 sky_tile_uv(vec3 p_world) {
	return vec2((p_world.x / hex_width + 0.5) / map_tiles.x, (p_world.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
}

float sky_daylight(vec3 p_world) {
	vec2 uv = sky_tile_uv(p_world);
	uv.x -= sky_day_shift / map_tiles.x;
	uv.y = clamp(uv.y, 0.5 / map_tiles.y, 1.0 - 0.5 / map_tiles.y);
	return texture(sky_day_tex, uv).r;
}

// Rough "how lit does it look" for a daylight value: warm direct sun growing with sqrt(daylight) plus sky light.
// scripts/SkyModel.cs Shade() is the same function; keep the two in step.
vec3 sky_shade(float p_day) {
	float d = clamp(p_day, 0.0, 1.0);
	float sun_on = smoothstep(0.0, 0.08, d);
	vec3 warm = mix(vec3(1.0, 0.56, 0.34), vec3(1.0), smoothstep(0.06, 0.45, d));
	vec3 ambient = mix(vec3(0.065, 0.0845, 0.156), vec3(0.22, 0.23, 0.25), smoothstep(0.0, 0.3, d)); // night floor +30% (owner 2026-11-10)
	return warm * sqrt(d) * sun_on + ambient;
}

// Multiplies a material's albedo: 1 where the tile is as lit as the view's centre.
vec3 sky_light(vec3 p_world) {
	if (sky_on < 0.5) {
		return vec3(1.0);
	}
	vec3 s = sky_shade(sky_daylight(p_world));
	return clamp(s / max(sky_ref_shade, vec3(0.004)), vec3(0.0), vec3(1.6));
}

// Snow, wetness and puddles lying at a world point (x, y, z).
vec3 sky_cover(vec3 p_world) {
	if (sky_on < 0.5) {
		return vec3(0.0);
	}
	return texture(sky_wx_tex, sky_tile_uv(p_world)).rgb;
}

float sky_hash(vec2 p_p) {
	vec3 q = fract(vec3(p_p.x, p_p.y, p_p.x) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float sky_noise(vec2 p_p) {
	vec2 i = floor(p_p);
	vec2 f = fract(p_p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(sky_hash(i), sky_hash(i + vec2(1.0, 0.0)), f.x);
	float b = mix(sky_hash(i + vec2(0.0, 1.0)), sky_hash(i + vec2(1.0, 1.0)), f.x);
	return mix(a, b, f.y);
}

// How much of a surface facing up by p_up (world normal's y) is under snow, for a cover of p_cover (0..1):
// patchy on flat ground first, then steeper faces as it deepens; none at all with no cover.
float sky_snow(vec3 p_world, float p_up, float p_cover) {
	float n = sky_noise(p_world.xz * 0.35) * 0.6 + sky_noise(p_world.xz * 1.7) * 0.4;
	float s = smoothstep(0.25, 0.45, p_cover + (n - 0.5) * 0.45 - (1.0 - p_up) * 1.3);
	return s * smoothstep(0.0, 0.08, p_cover);
}

// Puddles in hollows of flat ground, more of them as p_amount (0..1) rises; none at 0.
float sky_puddle(vec3 p_world, float p_up, float p_amount) {
	float n = sky_noise(p_world.xz * 0.06) * 0.7 + sky_noise(p_world.xz * 0.31) * 0.3;
	return smoothstep(0.0, 0.06, n - (1.0 - p_amount * 0.55)) * smoothstep(0.93, 0.985, p_up);
}

// Hoar frost at a world point (0..1) for a surface facing up by p_up: a fine, patchy white crust from the tile's frost
// (sky_wx_tex alpha), first in the open on flat ground and on up-facing rims, then thicker. 0 unless sky_frost_on.
float sky_frost(vec3 p_world, float p_up) {
	if (sky_on < 0.5 || sky_frost_on < 0.5) {
		return 0.0;
	}
	float f = texture(sky_wx_tex, sky_tile_uv(p_world)).a;
	float n = sky_noise(p_world.xz * 0.9) * 0.55 + sky_noise(p_world.xz * 4.3) * 0.45;
	float s = smoothstep(0.15, 0.5, f + (n - 0.5) * 0.55 - (1.0 - p_up) * 0.7);
	return s * smoothstep(0.0, 0.12, f) * 0.9;
}
