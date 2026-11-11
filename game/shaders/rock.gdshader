// Rocks, scree, sandstone and ice scattered over the surface (MultiMesh, scripts/PlanetView.Scatter.cs).
// Colour = mesh vertex colour (strata, lichen, crevices) x instance colour, broken up by model-space value noise;
// INSTANCE_CUSTOM.y = snow on up-facing faces (polar ice, high mountains), .z = moss (forest floor).
// Fog darkens it like the terrain; near / far meshes cross-fade with a dither like foliage.gdshader.
// Weather (sky.gdshaderinc, off by default): day/night shading, lying snow (at least the instance's own), wet darkening,
// night frost (class L only).
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
// Visual pass (GraphicsQuality.Terrain, PlanetView.TerrainDetail.cs): close up, the terrain's rock (or basalt) detail
// layer projected triplanar in world space: albedo variation round the rock's own colour, roughness, detail normal.
// det_on 0 (the default, and ice) = exactly the old shader.
uniform float det_on = 0.0;
uniform sampler2DArray det_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray det_normal : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float det_layer = 4.0;
uniform vec3 det_mean_colour = vec3(0.2);
uniform float det_scale = 0.32;
uniform float det_near = 50.0;
uniform float det_far = 150.0;

#include "res://shaders/sky.gdshaderinc"

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
	float det_fade = 0.0;
	float det_rough = roughness;
	vec3 det_pert = vec3(0.0);
	vec3 det_nw = vec3(0.0, 1.0, 0.0);
	if (det_on > 0.5) {
		vec3 dpx = dFdx(world_pos) * det_scale;
		vec3 dpy = dFdy(world_pos) * det_scale;
		vec3 q = world_pos * det_scale;
		det_nw = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
		det_fade = 1.0 - smoothstep(det_near, det_far, length(VERTEX));
		if (det_fade > 0.001) {
			vec3 an = abs(det_nw);
			vec3 tw = an * an * an * an;
			tw /= max(tw.x + tw.y + tw.z, 0.0001);
			vec4 cy = textureGrad(det_albedo, vec3(q.xz, det_layer), dpx.xz, dpy.xz);
			vec2 ty = textureGrad(det_normal, vec3(q.xz, det_layer), dpx.xz, dpy.xz).rg * 2.0 - 1.0;
			vec2 uvx = vec2(q.z, -q.y);
			vec4 cx = textureGrad(det_albedo, vec3(uvx, det_layer), vec2(dpx.z, -dpx.y), vec2(dpy.z, -dpy.y));
			vec2 tx = textureGrad(det_normal, vec3(uvx, det_layer), vec2(dpx.z, -dpx.y), vec2(dpy.z, -dpy.y)).rg * 2.0 - 1.0;
			vec2 uvz = vec2(q.x, -q.y);
			vec4 cz = textureGrad(det_albedo, vec3(uvz, det_layer), vec2(dpx.x, -dpx.y), vec2(dpy.x, -dpy.y));
			vec2 tz = textureGrad(det_normal, vec3(uvz, det_layer), vec2(dpx.x, -dpx.y), vec2(dpy.x, -dpy.y)).rg * 2.0 - 1.0;
			vec4 c = cy * tw.y + cx * tw.x + cz * tw.z;
			det_pert = vec3(ty.x, 0.0, -ty.y) * tw.y + vec3(0.0, tx.y, tx.x) * tw.x + vec3(tz.x, tz.y, 0.0) * tw.z;
			vec3 ratio = clamp(c.rgb / max(det_mean_colour, vec3(0.01)), vec3(0.0), vec3(3.0));
			albedo *= mix(vec3(1.0), ratio, 0.8 * det_fade);
			det_rough = mix(roughness, c.a, 0.7 * det_fade);
		}
	}
	vec3 lying = sky_cover(world_pos);
	albedo *= mix(1.0, 0.7, lying.y);
	float snow = max(cover.x, lying.x) * smoothstep(0.3, 0.62, up + (n - 0.5) * 0.35);
	float moss = cover.y * smoothstep(0.15, 0.55, up + (n - 0.5) * 0.6) * (1.0 - snow);
	albedo = mix(albedo, moss_colour * (0.7 + 0.6 * n), moss);
	albedo = mix(albedo, snow_colour * (0.92 + 0.1 * n), snow);
	// Night frost (class L): rims and tops of the rocks first.
	float frost = sky_frost(world_pos, up + (n - 0.5) * 0.4) * (1.0 - snow);
	albedo = mix(albedo, sky_frost_colour * (0.9 + 0.12 * n), frost);
	vec3 lit = sky_light(world_pos);
	ALBEDO = albedo * seen * lit;
	ROUGHNESS = mix(mix(mix(det_rough, 0.4, lying.y), 0.55, snow), 0.5, frost);
	if (det_fade > 0.001) {
		NORMAL = normalize((VIEW_MATRIX * vec4(normalize(det_nw + det_pert * det_fade * (1.0 - snow)), 0.0)).xyz);
	}
	SPECULAR = mix(mix(specular, 0.5, lying.y * 0.6), 0.45, snow) * seen * min(1.0, (lit.r + lit.g + lit.b) / 3.0);
}
