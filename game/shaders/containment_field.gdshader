// Containment field shell over a dome's coverage on a Domes world (scripts/PlanetView.Domes.cs). The mesh is a unit
// dome (radius 1, height 1, plus a short vertical skirt below the equator) scaled by the node to the field's reach.
// Additive and unlit: a soft blue-white fresnel rim, a fine hex lattice that is nearly invisible face-on, a few cells
// glinting in slow turns, a faint band sweeping up, and a glow where the shell meets the ground or anything behind it
// (scene depth). `reveal` 0 -> 1 raises it from the ground when it first appears (bright front); `flicker` 0..1 makes
// it stutter and drop out (life support below half); `strength` scales it all (brighter while placing crewed buildings).
// The hex lattice is laid out on a stereographic projection of the dome (from its bottom pole), so there is no seam
// and the cells keep their shape; they grow a little toward the top.
// Vehicle transits (scripts/PlanetView.DomeTransit.cs): up to 4 contacts at once, where a vehicle's own field meets the
// shell. Each sags the shell in toward the vehicle, sends rings travelling out over the dome (fading with distance),
// opens a glowing aperture ("iris") round the vehicle with crackling sparks on its rim and short streaks out of it, and
// warms the shell's colour toward the vehicle field's (the colours exchange); `closing` flashes the rim as it shuts.
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
// Contacts. The node's scale (world units of the unit dome: radius, height, radius) for distances:
uniform vec3 shell_scale = vec3(37.5, 22.5, 37.5);
uniform int contact_count = 0;
// xyz = the contact point on the shell (unit space), w = intensity 0..1
uniform vec4 contact_at[4];
// xyz = the vehicle field's centre (unit space), w = aperture 0..1 (open share)
uniform vec4 contact_to[4];
// x = seconds since the contact began, y = closing flash 0..1, z = colour exchange 0..1, w = aperture radius (world)
uniform vec4 contact_fx[4];
uniform vec3 contact_tint : source_color = vec3(1.0, 0.84, 0.52);

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
	vec3 moved = vec3(0.0);
	for (int i = 0; i < 4; i++) {
		if (i >= contact_count) {
			break;
		}
		vec4 c = contact_at[i];
		if (c.w <= 0.0) {
			continue;
		}
		vec4 fx = contact_fx[i];
		float dist = length((VERTEX - c.xyz) * shell_scale);
		// the shell sags in to meet the vehicle's field, easing back as the aperture opens
		float reach = max(fx.w * 2.4, 1.0);
		float k = 1.0 - smoothstep(0.0, reach, dist);
		moved += (contact_to[i].xyz - VERTEX) * k * k * c.w * 0.16 * (1.0 - 0.55 * contact_to[i].w);
		// the travelling rings lift the shell a little as they pass
		float lead = fx.x * 9.0;
		float wave = sin(dist * 1.1 - fx.x * 10.0) * exp(-dist * 0.08) * (1.0 - smoothstep(lead - 3.0, lead, dist));
		moved += normalize(NORMAL) * (wave * 0.12 * c.w / shell_scale.x);
	}
	VERTEX += moved;
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

	// Vehicle transits: rings, the sag's halo, the aperture and its crackling rim, the colours exchanging.
	float cut = 1.0;
	float fx_a = 0.0;
	float warm = 0.0;
	float hot = 0.0;
	for (int i = 0; i < 4; i++) {
		if (i >= contact_count) {
			break;
		}
		vec4 c = contact_at[i];
		if (c.w <= 0.0) {
			continue;
		}
		vec4 fx = contact_fx[i];
		float open = contact_to[i].w;
		vec3 dv = (unit_pos - c.xyz) * shell_scale;
		float dist = length(dv);

		// rings travelling out from the contact (a leading front, fading with distance)
		float lead = fx.x * 9.0;
		float ring = pow(0.5 + 0.5 * sin(dist * 1.1 - fx.x * 10.0), 6.0);
		ring *= (1.0 - smoothstep(lead - 3.0, lead, dist)) * exp(-dist * 0.07) * c.w;

		// a halo round the contact (where the shell sags in)
		float halo = (1.0 - smoothstep(0.0, max(fx.w * 2.6, 1.0), dist)) * c.w;

		// the aperture round the vehicle: nearly clear inside, a bright rim with sparks
		float ap = open * fx.w;
		float inside = (1.0 - smoothstep(ap - 0.5, ap + 0.15, dist)) * step(0.02, open);
		cut = min(cut, 1.0 - inside * 0.9 * c.w);
		float edge = (1.0 - smoothstep(0.0, 0.5 + 0.4 * fx.y, abs(dist - ap))) * step(0.02, open) * c.w;

		// angle round the contact in the shell's tangent plane (normal of the ellipsoid there)
		vec3 nrm = normalize(c.xyz / shell_scale);
		vec3 tan_a = abs(nrm.y) > 0.98 ? vec3(1.0, 0.0, 0.0) : normalize(cross(vec3(0.0, 1.0, 0.0), nrm));
		vec3 tan_b = cross(nrm, tan_a);
		float ang = atan(dot(dv, tan_b), dot(dv, tan_a));
		float frame = floor(TIME * 18.0) + float(i) * 17.0;
		float spark = step(0.62, cf_hash(vec2(floor(ang * 7.0 + seed), frame)));
		// short streaks out of the rim (jagged: the angular cell wanders with the distance)
		float cell_ang = ang * 1.75 + sin(dist * 2.3 + frame) * 0.05;
		float lane = abs(fract(cell_ang) - 0.5);
		float streak_on = step(0.8, cf_hash(vec2(floor(cell_ang), frame * 1.3)));
		float streak_len = 0.8 + 2.4 * cf_hash(vec2(floor(cell_ang) + 3.0, frame));
		float streak = (1.0 - smoothstep(0.0, 0.035, lane)) * streak_on * step(ap, dist) * (1.0 - smoothstep(ap, ap + streak_len, dist))
			* step(0.1, open) * c.w;

		fx_a += ring * 0.55 + halo * (0.22 + 0.18 * sin(TIME * 7.0 + dist)) + edge * (0.9 + 0.8 * spark + 1.2 * fx.y) + streak * 0.9;
		warm = max(warm, halo * fx.z);
		hot = max(hot, edge * (0.4 + 0.6 * spark) + streak);
	}

	a = (a * shown * cut + front_glow * 0.8) * strength * f * side;
	float extra = fx_a * strength * f * mix(1.0, side, 0.5);
	vec3 col = mix(field_colour, contact_tint, clamp(warm * 0.8, 0.0, 1.0));
	col = mix(col, vec3(0.95, 0.97, 1.0), clamp(hot * 0.55, 0.0, 1.0));
	float total = a + extra;
	ALBEDO = mix(field_colour, col, total > 0.0001 ? extra / total : 0.0) * (1.0 + hot * 0.8);
	ALPHA = clamp(total, 0.0, 1.0);
}
