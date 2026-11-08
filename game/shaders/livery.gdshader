// Our own vehicles' livery (CosmeticLook, achievements cosmetics; render-only): stands in for one hull paint material
// (HullPaint / CabPaint / Hull / HullPanel …) when a paint pattern or a brushed finish is picked. Reproduces the plain
// material (albedo colour × the baked scuff texture, metallic, roughness, specular, clearcoat, rim) and paints the
// pattern over it in the vehicle's own model space (mesh_to_model, set once: the mesh's rest transform under the model
// root, so a moving part carries its paint), normalised to the hull's box (box_min / box_size):
//   1 band      a broad paint band round the waist with paint2 pinlines (walls only)
//   2 chevrons  paint / paint2 hazard chevrons round the lower skirt, pointing forward (-Z)
//   3 stripes   twin paint racing stripes nose to tail over the roof and the ends, paint2 pinlines
// The livery keeps the bake's scuffs (its brightness relative to clean paint, tex_mean). brushed > 0 adds fine
// roughness streaks along the hull (brushed metal), faded out where they would alias. RoverLook's grime overlay still
// draws on top (it is a MaterialOverlay).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable, hint_default_white;
uniform float tex_mean = 1.0;
uniform float metallic = 0.15;
uniform float roughness = 0.42;
uniform float specular = 0.5;
uniform float clearcoat = 0.0;
uniform float clearcoat_roughness = 0.3;
uniform float rim = 0.0;
uniform float rim_tint = 0.5;
uniform float brushed = 0.0;

uniform int pattern = 0;
uniform vec3 paint : source_color = vec3(1.0, 1.0, 1.0);
uniform vec3 paint2 : source_color = vec3(0.1, 0.1, 0.1);
uniform float paint_metallic = 0.1;
uniform float paint_roughness = 0.4;
uniform mat4 mesh_to_model;
uniform vec3 box_min = vec3(-1.0, 0.0, -2.0);
uniform vec3 box_size = vec3(2.0, 2.0, 4.0);

varying vec3 m_pos;
varying vec3 m_normal;

void vertex() {
	m_pos = (mesh_to_model * vec4(VERTEX, 1.0)).xyz;
	m_normal = (mesh_to_model * vec4(NORMAL, 0.0)).xyz;
}

// 1 between p_a and p_b along p_x, soft over p_w at both edges.
float inside(float p_x, float p_a, float p_b, float p_w) {
	return smoothstep(p_a - p_w, p_a + p_w, p_x) * (1.0 - smoothstep(p_b - p_w, p_b + p_w, p_x));
}

float hash11(float p_x) {
	return fract(sin(p_x * 127.1 + 311.7) * 43758.5453);
}

void fragment() {
	vec4 tex = texture(albedo_texture, UV);
	vec3 base = albedo.rgb * tex.rgb;
	float rough = roughness;

	vec3 nrm = normalize(m_normal);
	vec3 n = (m_pos - box_min) / box_size;
	float walls = 1.0 - smoothstep(0.55, 0.75, abs(nrm.y));
	float paint_mask = 0.0;
	float paint2_mask = 0.0;

	if (pattern == 1) {
		float w = fwidth(n.y) + 0.0005;
		paint_mask = inside(n.y, 0.40, 0.56, w) * walls;
		paint2_mask = (inside(n.y, 0.37, 0.39, w) + inside(n.y, 0.57, 0.59, w)) * walls;
	} else if (pattern == 2) {
		float yc = box_min.y + box_size.y * 0.18;
		float along = abs(nrm.x) >= abs(nrm.z) ? m_pos.z : abs(m_pos.x);
		float s = (along - abs(m_pos.y - yc)) / 0.7; // apex toward -Z: pointing forward
		float t = abs(fract(s) - 0.5) * 2.0;
		float ws = fwidth(s) * 2.0 + 0.0005;
		float dark = smoothstep(0.5 - ws, 0.5 + ws, t);
		float skirt = inside(n.y, 0.06, 0.30, fwidth(n.y) + 0.0005) * walls;
		paint_mask = skirt * (1.0 - dark);
		paint2_mask = skirt * dark;
	} else if (pattern == 3) {
		float region = max(smoothstep(0.45, 0.65, abs(nrm.y)), smoothstep(0.55, 0.75, abs(nrm.z)));
		float dx = abs(n.x - 0.5);
		float w = fwidth(dx) + 0.0005;
		paint_mask = inside(dx, 0.05, 0.15, w) * region;
		paint2_mask = (inside(dx, 0.03, 0.045, w) + inside(dx, 0.155, 0.17, w)) * region;
	}

	if (brushed > 0.0) {
		// Streaks run along the hull (model Z): they vary across it, a little along it.
		float q = (m_pos.x + m_pos.y) * 140.0;
		float streak = hash11(floor(q) + floor(m_pos.z * 2.0) * 17.0) - 0.5;
		float fade = 1.0 - smoothstep(0.3, 1.0, fwidth(q));
		rough = clamp(rough + streak * 0.16 * brushed * fade, 0.04, 1.0);
		base *= 1.0 + streak * 0.06 * brushed * fade;
	}

	float livery = clamp(paint_mask + paint2_mask, 0.0, 1.0);
	vec3 livery_colour = (paint * paint_mask + paint2 * paint2_mask) / max(paint_mask + paint2_mask, 0.0001);
	float scuffs = clamp(dot(tex.rgb, vec3(0.3333)) / tex_mean, 0.0, 1.15);

	ALBEDO = mix(base, livery_colour * scuffs, livery);
	METALLIC = mix(metallic, paint_metallic, livery);
	ROUGHNESS = mix(rough, paint_roughness, livery);
	SPECULAR = specular;
	CLEARCOAT = clearcoat;
	CLEARCOAT_ROUGHNESS = clearcoat_roughness;
	RIM = rim;
	RIM_TINT = rim_tint;
}
