// Plants scattered over the surface (MultiMesh, scripts/PlanetView.Scatter.cs): trees, bushes, ferns, grass.
// Colour = mesh vertex colour x instance colour (COLOR) x the foliage atlas (alpha-cut cards), darkened by the fog
// texture exactly like the terrain (one texel per tile: 1 = in sight, ~0.4 = explored, ~0.08 = unexplored).
// Wind: the whole plant bends downwind by height (world-position phase, so gusts roll across the map) and leaves /
// blade tips flutter by UV2.x (0 at the trunk, 1 at a branch tip). Leaves let some light through (BACKLIGHT).
// LOD: near and far meshes swap with a per-pixel dither between lod_band distances from eye_position (the game
// camera, set every frame: in the shadow pass CAMERA_POSITION_WORLD is the light's). Grass sinks into the ground
// between shrink_band distances instead.
// Weather (sky.gdshaderinc, off by default): day/night shading, snow on up-facing leaves, wet darkening; wind_strength
// scales the sway and flutter from the weather's wind (1 = the old look).
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx, alpha_to_coverage;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform bool use_atlas = true;
uniform float alpha_cut : hint_range(0.0, 1.0) = 0.45;
uniform float sway_height = 8.0;    // the mesh's height (model units): bending grows with (height / this)^2
uniform float sway = 0.03;          // bend at the top, as a share of the height
uniform float flutter = 0.05;       // leaf / blade tip flutter (model units)
uniform vec2 wind_dir = vec2(0.8, 0.6);
uniform float wind_speed = 1.0;
uniform float wind_strength = 1.0;
uniform float translucency = 0.3;
uniform float normal_up = 0.0;      // grass: tip the normals up so a clump lights like the ground under it
uniform float roughness = 0.85;
uniform vec3 eye_position = vec3(0.0, 1.0e6, 0.0);
uniform vec4 lod_band = vec4(-2.0, -1.0, 1.0e9, 2.0e9); // visible from smoothstep(x, y) until 1 - smoothstep(z, w)
uniform vec2 shrink_band = vec2(1.0e9, 2.0e9);

#include "res://shaders/sky.gdshaderinc"

varying vec3 world_pos;
varying vec3 view_normal;
varying float lod_vis;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float t = TIME * wind_speed;
	float roll = dot(origin.xz, wind_dir);
	float phase = INSTANCE_CUSTOM.x * 6.2831853 + roll * 0.045;
	float gust = 0.6 + 0.4 * sin(t * 0.37 - roll * 0.011);
	float h = clamp(VERTEX.y / max(sway_height, 0.01), 0.0, 1.5);
	float bend = sway * wind_strength * sway_height * h * h * gust * (0.65 + 0.35 * sin(t * 1.3 + phase));
	vec3 world_push = vec3(wind_dir.x, 0.0, wind_dir.y) * bend;
	// World push -> model space (instances are rotation x scale; no shear).
	mat3 m = mat3(MODEL_MATRIX);
	vec3 inv_scale2 = 1.0 / max(vec3(dot(m[0], m[0]), dot(m[1], m[1]), dot(m[2], m[2])), vec3(1.0e-6));
	vec3 local_push = (transpose(m) * world_push) * inv_scale2;
	float wobble = UV2.x * flutter * wind_strength * gust * sin(t * 4.3 + phase * 3.0 + UV2.y * 6.2831853);
	VERTEX += local_push + NORMAL * wobble;

	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float d = distance(wp, eye_position);
	float shrink = 1.0 - smoothstep(shrink_band.x, shrink_band.y, d);
	VERTEX.y *= shrink;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	lod_vis = smoothstep(lod_band.x, lod_band.y, d) * (1.0 - smoothstep(lod_band.z, lod_band.w, d));

	vec3 n = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), normal_up));
	// Our own normal (cards keep their outward crown normal on both sides; Godot would flip back faces).
	view_normal = normalize((MODELVIEW_MATRIX * vec4(n, 0.0)).xyz);
}

void fragment() {
	// Dithered LOD cross-fade (interleaved gradient noise).
	float dither = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	if (lod_vis <= dither) {
		discard;
	}
	vec4 tex = use_atlas ? texture(atlas, UV) : vec4(1.0);
	vec2 fog_uv = vec2((world_pos.x / hex_width + 0.5) / map_tiles.x, (world_pos.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
	float seen = texture(fog_tex, fog_uv).r;
	vec3 albedo = COLOR.rgb * tex.rgb;
	vec3 lit = vec3(1.0);
	if (sky_on > 0.5) {
		vec3 lying = sky_cover(world_pos);
		float up = (INV_VIEW_MATRIX * vec4(view_normal, 0.0)).y;
		albedo *= mix(1.0, 0.8, lying.y);
		albedo = mix(albedo, sky_snow_colour, sky_snow(world_pos, up, lying.x) * 0.85);
		lit = sky_light(world_pos);
	}
	ALBEDO = albedo * seen * lit;
	ALPHA = tex.a;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	// With MSAA on (project: 4x) this turns the cut edges into alpha-to-coverage: softer needles and blades.
	ALPHA_ANTIALIASING_EDGE = 0.25;
	ALPHA_TEXTURE_COORDINATE = UV * vec2(textureSize(atlas, 0));
	NORMAL = normalize(view_normal);
	ROUGHNESS = roughness;
	SPECULAR = 0.15 * seen * min(1.0, (lit.r + lit.g + lit.b) / 3.0);
	BACKLIGHT = albedo * translucency * seen * lit;
}
