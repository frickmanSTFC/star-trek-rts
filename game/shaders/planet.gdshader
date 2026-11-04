// Orbital globe surface (textures painted by OrbitPainter). Land is satellite photography: six tileable ground layers
// (NASA Blue Marble patches, in a texture array) blended by the painter's splat masks, sampled at two scales so the
// detail holds up when zoomed in, and tinted part-way toward the painted map colour so biomass and elevation still
// show. Relief from a normal map of the painted height plus a tiling detail normal. Oceans: two scrolling wave normals
// and a broad, rough sun glint. Sunlight reddened by the air it crosses (air.gdshaderinc, the same air as
// atmosphere.gdshader), so the terminator glows warm; the low cloud layer's shadows (clouds.gdshaderinc, looked up
// toward the sun from each point); city lights at your bases only on the night side. Lit by the scene's lights.
shader_type spatial;
render_mode cull_disabled;

#include "res://shaders/air.gdshaderinc"
#include "res://shaders/clouds.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D water_tex : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2D lights_tex : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;
// Splats: a = plains, forest, desert, hills; b.rg = rock, ice. Layers of photo_layers in the same order.
uniform sampler2D splat_a : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2D splat_b : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2DArray photo_layers : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 sun_dir = vec3(-0.6, 0.3, 0.75);
uniform float relief = 1.0;
uniform float lights_energy = 3.0;
// How far land takes the photo's own colour (0 = painted colour with photo detail only, 1 = pure photo).
uniform float photo_mix = 0.5;
// Photo repeats around the globe (u, v): the main layer, and a finer one mixed in for close-ups.
uniform vec2 photo_scale = vec2(32.0, 16.0);
uniform vec2 photo_fine_scale = vec2(128.0, 64.0);
// Ocean waves: repeats of the detail normal around the globe, scroll speed (UV per second) and strength.
uniform vec2 wave_scale = vec2(48.0, 24.0);
uniform float wave_speed = 0.004;
uniform float wave_strength = 0.35;
uniform float water_roughness = 0.3;
// The low cloud layer (OrbitView sets these to match it, and cloud_from_world every frame as the layer turns).
uniform mat3 cloud_from_world = mat3(1.0);
uniform vec3 cloud_offset = vec3(0.0);
uniform vec3 cloud_scale = vec3(1.2);
uniform float cloud_cover = 0.58;
uniform float cloud_softness = 0.11;
uniform float cloud_opacity = 0.95;
uniform float cloud_height = 1.4;
uniform float cloud_shadow = 0.65;

varying vec3 world_dir;

void vertex() {
	world_dir = (MODEL_MATRIX * vec4(VERTEX, 0.0)).xyz;
}

void fragment() {
	float water = texture(water_tex, UV).r;
	vec3 base = texture(albedo_tex, UV).rgb;

	// Photo ground: weighted layers at two scales. Gradients are taken here, outside the per-layer branch.
	vec4 sa = texture(splat_a, UV);
	vec2 sb = texture(splat_b, UV).rg;
	float weights[6] = float[](sa.r, sa.g, sa.b, sa.a, sb.r, sb.g);
	vec2 uv1 = UV * photo_scale;
	vec2 uv2 = UV * photo_fine_scale;
	vec2 dx1 = dFdx(uv1), dy1 = dFdy(uv1), dx2 = dFdx(uv2), dy2 = dFdy(uv2);
	vec3 p1 = vec3(0.0), p2 = vec3(0.0), mean = vec3(0.0);
	float total = 0.0;
	for (int k = 0; k < 6; k++) {
		float w = weights[k];
		if (w < 0.004) continue;
		p1 += w * textureGrad(photo_layers, vec3(uv1, float(k)), dx1, dy1).rgb;
		p2 += w * textureGrad(photo_layers, vec3(uv2, float(k)), dx2, dy2).rgb;
		mean += w * textureLod(photo_layers, vec3(0.5, 0.5, float(k)), 12.0).rgb; // the 1x1 mip: the layer's average colour
		total += w;
	}
	vec3 colour = base;
	if (total > 0.01) {
		p1 /= total;
		p2 /= total;
		mean = max(mean / total, vec3(1e-4));
		vec3 photo = p1 * (1.0 + (p2 / mean - 1.0) * 0.5);
		vec3 land = mix(base, mean, photo_mix) * photo / mean;
		colour = mix(land, base, water);
	}

	// Relief: the elevation normal map, with tiling detail added on top (land only).
	vec3 n1 = texture(normal_tex, UV).rgb * 2.0 - 1.0;
	vec3 n2 = texture(detail_normal, UV * vec2(16.0, 8.0)).rgb * 2.0 - 1.0;
	vec3 n_land = vec3(n1.xy + n2.xy * 0.45, max(n1.z, 0.2));
	// Waves: two copies of the detail normal drifting different ways (water only).
	vec2 wuv = UV * wave_scale;
	vec3 w1 = texture(detail_normal, wuv + vec2(1.0, 0.6) * TIME * wave_speed).rgb * 2.0 - 1.0;
	vec3 w2 = texture(detail_normal, wuv * 1.7 + vec2(-0.8, 0.3) * TIME * wave_speed).rgb * 2.0 - 1.0;
	vec3 n_sea = vec3((w1.xy + w2.xy) * 0.5, 1.0);
	vec3 n = normalize(mix(n_land, n_sea, water));
	NORMAL_MAP = n * 0.5 + 0.5;
	NORMAL_MAP_DEPTH = mix(relief, wave_strength, water);

	// Sun height at this point (NORMAL and the light both in view space); sunlight through the air goes warm near the terminator.
	vec3 l = normalize((VIEW_MATRIX * vec4(sun_dir, 0.0)).xyz);
	float mu = dot(NORMAL, l);
	float day = smoothstep(-0.12, 0.2, mu);
	vec3 sunlight = sunlight_at(mu, 1.0);

	// Cloud shadow: the low layer where the ray toward the sun crosses it.
	float shadow = 0.0;
	if (mu > 0.0) {
		vec3 q = normalize(normalize(world_dir) + normalize(sun_dir) * (cloud_height / planet_radius) / max(mu, 0.15));
		vec3 cd = cloud_from_world * q;
		shadow = cloud_alpha(cloud_field(cd, cloud_offset, cloud_scale, 0.0), cd, cloud_cover, cloud_softness) * cloud_opacity * cloud_shadow;
	}
	// A crisper sunset line: Lambert light falls off slowly toward the terminator; lift the low-sun ground a little
	// (light ~ mu^0.75 instead of mu) so the day side stays bright closer to the edge and then drops away quickly.
	float crisp = clamp(pow(max(mu, 0.0) + 0.02, -0.25), 1.0, 1.8);
	ALBEDO = colour * sunlight * (1.0 - shadow) * crisp;
	ROUGHNESS = mix(0.9, water_roughness, water);
	SPECULAR = mix(0.25, 0.5, water) * dot(sunlight, vec3(0.333)) * (1.0 - shadow);
	METALLIC = 0.0;
	EMISSION = texture(lights_tex, UV).rgb * lights_energy * (1.0 - day);
}
