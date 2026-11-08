// Containment field shell over a dome's coverage on a Domes world (scripts/PlanetView.Domes.cs). The mesh is a unit
// dome (radius 1, height 1, plus a short vertical skirt below the equator) scaled by the node to the field's reach.
// Additive and unlit: a soft blue-white fresnel rim, a fine hex lattice that is nearly invisible face-on, a few cells
// glinting in slow turns, a faint band sweeping up, and a glow where the shell meets the ground or anything behind it
// (scene depth). `reveal` 0 -> 1 raises it from the ground when it first appears (bright front); `flicker` 0..1 makes
// it stutter and drop out (life support below half); `strength` scales it all (brighter while placing crewed buildings).
// The hex lattice is laid out on a stereographic projection of the dome (from its bottom pole), so there is no seam
// and the cells keep their shape; they grow a little toward the top.
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

uniform vec3 field_colour : source_color = vec3(0.62, 0.8, 1.0);
uniform float strength = 1.0;
uniform float flicker = 0.0;
uniform float reveal = 1.0;
uniform float rim_power = 3.0;
uniform float hex_cells = 9.0; // lattice cells across the dome's radius (at the equator)
uniform float seed = 0.0;
uniform sampler2D field_depth : hint_depth_texture, repeat_disable, filter_nearest;

varying vec3 unit_pos;

float cf_hash(vec2 p_p) {
	vec3 q = fract(vec3(p_p.x, p_p.y, p_p.x) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

// Hex lattice: xy = offset from the nearest cell centre, zw = that centre (the cell's id).
vec4 cf_hex(vec2 p_p) {
	vec2 r = vec2(1.0, 1.7320508);
	vec2 h = r * 0.5;
	vec2 a = mod(p_p, r) - h;
	vec2 b = mod(p_p - h, r) - h;
	vec2 g = dot(a, a) < dot(b, b) ? a : b;
	return vec4(g, p_p - g);
}

void vertex() {
	unit_pos = VERTEX;
}

void fragment() {
	float y = unit_pos.y;
	float up = clamp(y, 0.0, 1.0);

	// Powering up: shown below the rising front, with a bright band at it.
	float front = reveal * 1.1;
	float shown = 1.0 - smoothstep(front - 0.08, front, up);
	float front_glow = (1.0 - smoothstep(0.0, 0.06, abs(up - front + 0.04))) * (1.0 - reveal);

	// Fresnel rim: bright where the shell is seen edge-on, nearly nothing face-on.
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float rim = pow(1.0 - facing, rim_power);

	// Hex lattice with a few cells glinting at a time.
	vec2 sp = unit_pos.xz / (1.0 + up);
	vec4 hx = cf_hex(sp * hex_cells);
	vec2 q = abs(hx.xy);
	float d = max(q.x, dot(q, vec2(0.5, 0.8660254))); // 0 at the centre .. 0.5 at the edge
	float line = smoothstep(0.43, 0.5, d);
	float cell = cf_hash(hx.zw + vec2(seed, seed * 0.37));
	float glint = smoothstep(0.82, 1.0, sin(TIME * 0.6 + cell * 6.2831853 + up * 4.0));
	float sweep = smoothstep(0.9, 1.0, sin(TIME * 0.35 - up * 6.0 + seed));

	// Where the shell meets the ground (or anything just behind it).
	float depth = texture(field_depth, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	behind.xyz /= behind.w;
	float gap = length(behind.xyz) - length(VERTEX);
	float contact = 1.0 - smoothstep(0.0, 1.8, gap);

	float side = FRONT_FACING ? 1.0 : 0.45; // the far wall, seen through the near one, is fainter
	float a = rim * 0.8 + line * (0.03 + 0.22 * glint + 0.05 * sweep) * (0.3 + rim) + glint * 0.025 + contact * 0.5;

	// Failing life support: the field stutters (hashed per ~1/13 s) and hums.
	float t = TIME * 13.0 + seed;
	float buzz = cf_hash(vec2(floor(t), seed));
	float stutter = step(0.35, buzz) * (0.75 + 0.25 * sin(TIME * 60.0 + seed));
	float f = mix(1.0, 0.15 + 0.85 * stutter, flicker);

	a = (a * shown + front_glow * 0.8) * strength * f * side;
	ALBEDO = field_colour;
	ALPHA = clamp(a, 0.0, 1.0);
}
