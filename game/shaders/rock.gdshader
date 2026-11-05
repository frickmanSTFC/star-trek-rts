// Rocks, scree, sandstone and ice scattered over the surface (MultiMesh, scripts/PlanetView.Scatter.cs).
// Colour = mesh vertex colour (strata, lichen, crevices) x instance colour, broken up by model-space value noise;
// INSTANCE_CUSTOM.y = snow on up-facing faces (polar ice, high mountains), .z = moss (forest floor).
// Fog darkens it like the terrain; near / far meshes cross-fade with a dither like foliage.gdshader.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;

uniform float roughness = 0.85;
uniform float specular = 0.3;
uniform float detail_scale = 2.5;
uniform vec3 snow_colour : source_color = vec3(0.86, 0.9, 0.96);
uniform vec3 moss_colour : source_color = vec3(0.24, 0.31, 0.12);
uniform vec3 eye_position = vec3(0.0, 1.0e6, 0.0);
uniform vec4 lod_band = vec4(-2.0, -1.0, 1.0e9, 2.0e9);

varying vec3 world_pos;
varying vec3 model_pos;
varying vec2 cover;
varying float lod_vis;

float hash13(vec3 p) {
	vec3 q = fract(p * 0.1031);
	q += dot(q, q.zyx + 31.32);
	return fract((q.x + q.y) * q.z);
}

float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(hash13(i), hash13(i + vec3(1.0, 0.0, 0.0)), f.x), mix(hash13(i + vec3(0.0, 1.0, 0.0)), hash13(i + vec3(1.0, 1.0, 0.0)), f.x), f.y);
	float b = mix(mix(hash13(i + vec3(0.0, 0.0, 1.0)), hash13(i + vec3(1.0, 0.0, 1.0)), f.x), mix(hash13(i + vec3(0.0, 1.0, 1.0)), hash13(i + vec3(1.0, 1.0, 1.0)), f.x), f.y);
	return mix(a, b, f.z);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	model_pos = VERTEX + INSTANCE_CUSTOM.x * 37.0; // each instance gets its own noise
	cover = INSTANCE_CUSTOM.yz;
	float d = distance(world_pos, eye_position);
	lod_vis = smoothstep(lod_band.x, lod_band.y, d) * (1.0 - smoothstep(lod_band.z, lod_band.w, d));
}

void fragment() {
	float dither = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	if (lod_vis <= dither) {
		discard;
	}
	vec2 fog_uv = vec2((world_pos.x / hex_width + 0.5) / map_tiles.x, (world_pos.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
	float seen = texture(fog_tex, fog_uv).r;
	float n = value_noise(model_pos * detail_scale) * 0.65 + value_noise(model_pos * detail_scale * 3.7) * 0.35;
	vec3 albedo = COLOR.rgb * (0.72 + 0.4 * n);
	float up = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).y;
	float snow = cover.x * smoothstep(0.3, 0.62, up + (n - 0.5) * 0.35);
	float moss = cover.y * smoothstep(0.15, 0.55, up + (n - 0.5) * 0.6) * (1.0 - snow);
	albedo = mix(albedo, moss_colour * (0.7 + 0.6 * n), moss);
	albedo = mix(albedo, snow_colour * (0.92 + 0.1 * n), snow);
	ALBEDO = albedo * seen;
	ROUGHNESS = mix(roughness, 0.55, snow);
	SPECULAR = mix(specular, 0.45, snow) * seen;
}
