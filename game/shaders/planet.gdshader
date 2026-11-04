// Orbital globe surface (textures painted by OrbitPainter). Terrain colour with fine detail on land, relief from a
// normal map of the painted height (plus a tiling detail normal), shiny oceans, a soft atmospheric haze toward the limb on the day side,
// and city lights at your bases that only show on the night side. Lit by the scene's lights (the orbit "sun").
shader_type spatial;
render_mode cull_disabled;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D water_tex : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2D lights_tex : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec3 sun_dir = vec3(-0.6, 0.3, 0.75);
uniform vec4 haze : source_color = vec4(0.45, 0.68, 1.0, 1.0);
uniform float relief = 1.0;
uniform float lights_energy = 3.0;

void fragment() {
	float water = texture(water_tex, UV).r;
	float detail = texture(detail_tex, UV * vec2(12.0, 6.0)).r;
	vec3 colour = texture(albedo_tex, UV).rgb * mix(0.82 + 0.36 * detail, 1.0, water);

	// Relief: the elevation normal map, with tiling detail added on top (land only).
	vec3 n1 = texture(normal_tex, UV).rgb * 2.0 - 1.0;
	vec3 n2 = texture(detail_normal, UV * vec2(16.0, 8.0)).rgb * 2.0 - 1.0;
	vec3 n = normalize(vec3(n1.xy + n2.xy * 0.45, max(n1.z, 0.2)));
	NORMAL_MAP = n * 0.5 + 0.5;
	NORMAL_MAP_DEPTH = relief * (1.0 - water);

	// Day side: how much the sun faces this point (NORMAL and the light both in view space).
	vec3 l = normalize((VIEW_MATRIX * vec4(sun_dir, 0.0)).xyz);
	float day = smoothstep(-0.12, 0.2, dot(NORMAL, l));
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(colour, haze.rgb, fresnel * 0.55 * day);
	ROUGHNESS = mix(0.9, 0.18, water);
	SPECULAR = mix(0.25, 0.9, water);
	METALLIC = 0.0;
	EMISSION = texture(lights_tex, UV).rgb * lights_energy * (1.0 - day);
}
