// Planet terrain: per-tile colour from the mesh, hex outlines near tile edges, and fog of war
// from a small texture (one texel per tile: 1 = in sight, ~0.4 = explored, ~0.08 = unexplored).
// Day/night, wet ground, puddles, lying snow and night frost come from sky.gdshaderinc (off by default: then it looks
// as before). Ground colours are per planet class, baked into the vertex colours (scripts/PlanetLook.cs).
//
// High-detail ground (visual pass, GraphicsQuality.Terrain; scripts/PlanetView.TerrainDetail.cs): det_on 0 (the
// default) = exactly the old shader path. With det_on 1, near the camera (full within det_near, gone by det_far, so the
// view from above is unchanged) each tile's ground kind (det_kind_tex: one texel per tile = a layer of the detail
// arrays) adds a tiling detail texture: albedo as variation around the tile's colour (texel / that layer's mean
// det_mean, so the palette and the average brightness stay), roughness, and a detail normal. Two samples per pixel at
// different scales and angles, mixed by a slow noise, plus a broad brightness mottle, so the repeat doesn't show; steep
// faces add two side projections (triplanar); snowy peaks swap rock for snow by the vertex colour; wet soil turns to mud
// and lying snow gets the snow layer. Kind edges are jittered by noise so they don't follow the hex lines exactly.
// Layers: 0 grass, 1 forest floor, 2 dirt, 3 sand, 4 rock, 5 snow, 6 mud, 7 tundra, 8 lichen, 9 basalt.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;
uniform float grid_strength : hint_range(0.0, 1.0) = 0.22;

uniform float det_on = 0.0;
uniform sampler2DArray det_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable; // rgb colour, a roughness
uniform sampler2DArray det_normal : filter_linear_mipmap_anisotropic, repeat_enable; // OpenGL-style tangent normal
uniform sampler2D det_kind_tex : filter_nearest, repeat_disable; // r * 255 = the tile's layer
uniform vec3 det_mean[10]; // each layer's mean colour (linear), from the C# loader
uniform float det_strength = 0.9; // how much of the albedo variation shows
uniform float det_normal_strength = 1.0;
uniform float det_rough_mix = 0.8;
uniform float det_near = 110.0; // full detail within this distance of the camera
uniform float det_far = 340.0; // none beyond
uniform float det_scale = 0.19; // repeats per world unit, soil kinds (one tile ~5.3 units)
uniform float det_rock_scale = 0.11; // rock, basalt, snow (~9 units)
uniform float det_macro = 0.53; // the second sample's scale relative to the first

#include "res://shaders/sky.gdshaderinc"

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float fog_at(vec3 p) {
	vec2 uv = vec2((p.x / hex_width + 0.5) / map_tiles.x, (p.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
	return texture(fog_tex, uv).r;
}

// The detail layer of the tile whose centre is nearest p_xz (offset rows, like PlanetView.TileAt).
int det_layer_at(vec2 p_xz) {
	int r0 = int(round(p_xz.y / (0.866 * hex_width)));
	ivec2 best = ivec2(0);
	float best_d = 1.0e20;
	for (int r = r0 - 1; r <= r0 + 1; r++) {
		float odd = float(r & 1);
		int c = int(round(p_xz.x / hex_width - 0.5 * odd));
		vec2 d = p_xz - vec2((float(c) + 0.5 * odd) * hex_width, float(r) * 0.866 * hex_width);
		float dd = dot(d, d);
		if (dd < best_d) {
			best_d = dd;
			best = ivec2(c, r);
		}
	}
	best = clamp(best, ivec2(0), ivec2(map_tiles) - ivec2(1));
	return int(texelFetch(det_kind_tex, best, 0).r * 255.0 + 0.5);
}

// Ground seen from above: layer p_layer at two scales/angles mixed by p_mix. p_col = albedo (rgb) + roughness (a);
// p_pert = the detail normal's tilt in world x/z. p_dx/p_dy = the world x/z derivatives (taken outside the branches).
void det_top(vec2 p_xz, float p_layer, float p_scale, vec2 p_dx, vec2 p_dy, float p_mix, out vec4 p_col, out vec3 p_pert) {
	vec2 uv1 = p_xz * p_scale;
	vec2 gx1 = p_dx * p_scale;
	vec2 gy1 = p_dy * p_scale;
	vec4 c1 = textureGrad(det_albedo, vec3(uv1, p_layer), gx1, gy1);
	vec2 t1 = textureGrad(det_normal, vec3(uv1, p_layer), gx1, gy1).rg * 2.0 - 1.0;
	// Second sample: turned ~37 degrees, larger, offset.
	float s2 = p_scale * det_macro;
	vec2 uv2 = vec2(0.799 * p_xz.x - 0.602 * p_xz.y, 0.602 * p_xz.x + 0.799 * p_xz.y) * s2 + vec2(0.31, 0.71);
	vec2 gx2 = vec2(0.799 * p_dx.x - 0.602 * p_dx.y, 0.602 * p_dx.x + 0.799 * p_dx.y) * s2;
	vec2 gy2 = vec2(0.799 * p_dy.x - 0.602 * p_dy.y, 0.602 * p_dy.x + 0.799 * p_dy.y) * s2;
	vec4 c2 = textureGrad(det_albedo, vec3(uv2, p_layer), gx2, gy2);
	vec2 t2 = textureGrad(det_normal, vec3(uv2, p_layer), gx2, gy2).rg * 2.0 - 1.0;
	p_col = mix(c1, c2, p_mix);
	// Tangent x runs along +u, tangent y (green, image up) along -v: back into world x/z for each sample's axes.
	vec2 w1 = vec2(t1.x, -t1.y);
	vec2 w2 = t2.x * vec2(0.799, -0.602) - t2.y * vec2(0.602, 0.799);
	vec2 w = mix(w1, w2, p_mix);
	p_pert = vec3(w.x, 0.0, w.y);
}

void fragment() {
	float seen = fog_at(world_pos);
	// UV.x is 0 at a tile's centre and 1 at its corners: darken a thin band at the edge.
	float rim = smoothstep(0.82, 1.0, UV.x) * grid_strength;
	vec3 albedo = COLOR.rgb * (1.0 - rim);
	float rough = 0.9;
	float spec = 0.25;

	// High-detail ground (det_on: uniform, so the derivatives below are taken in uniform control flow).
	float det_fade = 0.0;
	vec3 det_pert = vec3(0.0);
	vec3 det_base = albedo;
	vec2 det_dx = vec2(0.0);
	vec2 det_dy = vec2(0.0);
	float det_w = 0.5;
	float det_soil = 0.0;
	vec3 det_nw = vec3(0.0, 1.0, 0.0);
	if (det_on > 0.5) {
		vec3 dpx = dFdx(world_pos);
		vec3 dpy = dFdy(world_pos);
		det_dx = dpx.xz;
		det_dy = dpy.xz;
		det_nw = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
		det_fade = 1.0 - smoothstep(det_near, det_far, length(VERTEX));
		if (det_fade > 0.001) {
			vec2 jitter = vec2(sky_noise(world_pos.xz * 0.21), sky_noise(world_pos.xz * 0.21 + vec2(17.3, 5.1))) - 0.5;
			int layer = clamp(det_layer_at(world_pos.xz + jitter * 5.0), 0, 9);
			if (layer == 4 || layer == 9) {
				// Mountains: the mesh paints the peaks white above the snow line; follow it.
				float lum = dot(COLOR.rgb, vec3(0.2126, 0.7152, 0.0722));
				if (lum + (sky_noise(world_pos.xz * 0.9) - 0.5) * 0.2 > 0.5) {
					layer = 5;
				}
			}
			det_soil = (layer == 0 || layer == 1 || layer == 2 || layer == 7 || layer == 8) ? 1.0 : 0.0;
			float scale = (layer == 4 || layer == 5 || layer == 9) ? det_rock_scale : det_scale;
			det_w = mix(0.15, 0.85, smoothstep(0.25, 0.75, sky_noise(world_pos.xz * 0.013 + vec2(3.7, 8.1))));
			float fl = float(layer);
			vec4 col;
			vec3 pert;
			det_top(world_pos.xz, fl, scale, det_dx, det_dy, det_w, col, pert);
			// Steep faces: add the two side projections (triplanar), weighted by how much the face looks that way.
			vec3 an = abs(det_nw);
			vec3 tw = an * an * an * an;
			tw /= max(tw.x + tw.y + tw.z, 0.0001);
			if (tw.y < 0.97) {
				vec2 uvx = vec2(world_pos.z, -world_pos.y) * scale;
				vec2 gxx = vec2(dpx.z, -dpx.y) * scale;
				vec2 gyx = vec2(dpy.z, -dpy.y) * scale;
				vec4 cx = textureGrad(det_albedo, vec3(uvx, fl), gxx, gyx);
				vec2 tx = textureGrad(det_normal, vec3(uvx, fl), gxx, gyx).rg * 2.0 - 1.0;
				vec2 uvz = vec2(world_pos.x, -world_pos.y) * scale;
				vec2 gxz = vec2(dpx.x, -dpx.y) * scale;
				vec2 gyz = vec2(dpy.x, -dpy.y) * scale;
				vec4 cz = textureGrad(det_albedo, vec3(uvz, fl), gxz, gyz);
				vec2 tz = textureGrad(det_normal, vec3(uvz, fl), gxz, gyz).rg * 2.0 - 1.0;
				col = col * tw.y + cx * tw.x + cz * tw.z;
				pert = pert * tw.y + vec3(0.0, tx.y, tx.x) * tw.x + vec3(tz.x, tz.y, 0.0) * tw.z;
			}
			vec3 ratio = clamp(col.rgb / max(det_mean[layer], vec3(0.01)), vec3(0.0), vec3(3.0));
			// A broad mottle on top, so neighbouring repeats differ in brightness.
			float mottle = (sky_noise(world_pos.xz * 0.021) - 0.5) * 0.14 + (sky_noise(world_pos.xz * 0.073) - 0.5) * 0.08;
			albedo *= mix(vec3(1.0), ratio * (1.0 + mottle), det_strength * det_fade);
			rough = mix(rough, col.a, det_rough_mix * det_fade);
			det_pert = pert;
		}
	}

	if (sky_on > 0.5) {
		vec3 lying = sky_cover(world_pos);
		float up = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).y;
		float puddle = sky_puddle(world_pos, up, lying.z);
		float wet = max(lying.y, puddle);
		if (det_fade > 0.001 && det_soil > 0.5 && lying.y > 0.02) {
			// Wet soil turns to mud near the camera.
			vec4 mud;
			vec3 mud_pert;
			det_top(world_pos.xz, 6.0, det_scale, det_dx, det_dy, det_w, mud, mud_pert);
			float m = smoothstep(0.02, 0.7, lying.y) * 0.8 * det_fade;
			vec3 mud_ratio = clamp(mud.rgb / max(det_mean[6], vec3(0.01)), vec3(0.0), vec3(3.0));
			albedo = mix(albedo, mix(det_base, det_mean[6], 0.4) * mud_ratio, m);
			rough = mix(rough, mud.a, m);
			det_pert = mix(det_pert, mud_pert, m);
		}
		albedo *= mix(1.0, 0.6, wet);
		rough = mix(rough, 0.35, lying.y);
		rough = mix(rough, 0.04, puddle);
		spec = mix(spec, 0.55, max(lying.y * 0.5, puddle));
		float snow = sky_snow(world_pos, up, lying.x);
		vec3 snow_colour = sky_snow_colour * (0.93 + 0.07 * sky_noise(world_pos.xz * 2.3));
		if (det_fade > 0.001 && snow > 0.01) {
			vec4 sn;
			vec3 sn_pert;
			det_top(world_pos.xz, 5.0, det_rock_scale, det_dx, det_dy, det_w, sn, sn_pert);
			vec3 sn_ratio = clamp(sn.rgb / max(det_mean[5], vec3(0.01)), vec3(0.0), vec3(3.0));
			snow_colour *= mix(vec3(1.0), sn_ratio, det_strength * det_fade);
			det_pert = mix(det_pert, sn_pert * 0.6, snow);
		}
		albedo = mix(albedo, snow_colour, snow);
		rough = mix(rough, 0.7, snow);
		spec = mix(spec, 0.35, snow);
		// Night frost (class L only; 0 elsewhere): a thin, glittery white crust.
		float frost = sky_frost(world_pos, up) * (1.0 - snow);
		albedo = mix(albedo, sky_frost_colour * (0.9 + 0.1 * sky_noise(world_pos.xz * 7.1)), frost);
		rough = mix(rough, 0.55, frost);
		spec = mix(spec, 0.5, frost);
		vec3 lit = sky_light(world_pos);
		albedo *= lit;
		spec *= min(1.0, (lit.r + lit.g + lit.b) / 3.0);
	}
	if (det_fade > 0.001) {
		// Tilt the normal by the detail (in world space, on the side the face shows), back to view space.
		float side = det_nw.y < 0.0 ? -1.0 : 1.0;
		vec3 n = normalize(det_nw * side + det_pert * det_normal_strength * det_fade);
		NORMAL = normalize((VIEW_MATRIX * vec4(n * side, 0.0)).xyz);
	}
	ALBEDO = albedo * seen;
	ROUGHNESS = rough;
	SPECULAR = spec * seen;
}
