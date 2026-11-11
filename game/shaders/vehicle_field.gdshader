// A vehicle's own containment field while it cycles through a dome's shell (scripts/PlanetView.DomeTransit.cs; the
// timeline is described there). The mesh is a unit sphere scaled by the node to the vehicle's box + 0.6 (an ellipsoid
// round the hull, its bottom under the ground). Same family as shaders/containment_field.gdshader: additive and unlit,
// a fresnel rim, a fine hex lattice, a glow where it meets the ground; its own warm tint.
//   rise 0 -> 1        the field climbs from the ground (ground_y, unit space) to the roof behind a bright front
//   lock_flicker 0..1  stutters while it locks; flash 0..1 brightens it all (the lock, the merge)
//   seam 0..1          where it meets the big shell (seam_dir, unit space): it bulges that way (bulge), takes on the
//                      shell's blue (seam_colour) and ripples toward the seam
//   shell_*            the big shell (world centre + semi-axes, a vertical skirt below its equator): where the two
//                      surfaces cut through each other a bright crackling seam line runs round the bubble, and the part
//                      of the bubble already on the shell's far side turns the shell's colour (the colours exchange)
//   merge 0..1         entering: the whole bubble turns the dome's colour as it melts into it
//   dissolve 0..1      leaving: it burns away bottom-up behind a ragged bright edge
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

uniform vec3 field_tint : source_color = vec3(1.0, 0.84, 0.52);
uniform vec3 seam_colour : source_color = vec3(0.62, 0.8, 1.0);
uniform float strength = 1.0;
uniform float rise = 1.0;
uniform float ground_y = -0.6;
uniform float lock_flicker = 0.0;
uniform float flash = 0.0;
uniform float dissolve = 0.0;
uniform float seam = 0.0;
uniform vec3 seam_dir = vec3(0.0, 0.0, -1.0);
uniform float bulge = 0.0;
uniform float merge = 0.0;
uniform float hex_cells = 4.0;
uniform float seed = 0.0;
uniform float shell_on = 0.0;
uniform vec3 shell_centre = vec3(0.0);
uniform vec3 shell_scale = vec3(1.0);
uniform sampler2D field_depth : hint_depth_texture, repeat_disable, filter_nearest;

varying vec3 unit_pos;

float vf_hash(vec2 p_p) {
	vec3 q = fract(vec3(p_p.x, p_p.y, p_p.x) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

// Hex lattice: xy = offset from the nearest cell centre, zw = that centre (the cell's id).
vec4 vf_hex(vec2 p_p) {
	vec2 r = vec2(1.0, 1.7320508);
	vec2 h = r * 0.5;
	vec2 a = mod(p_p, r) - h;
	vec2 b = mod(p_p - h, r) - h;
	vec2 g = dot(a, a) < dot(b, b) ? a : b;
	return vec4(g, p_p - g);
}

// Signed distance (world units, roughly: measured along the ray from the shell's centre) from a world point to the big
// shell: a squashed hemisphere above its equator, a vertical skirt below it. < 0 = inside.
float vf_shell(vec3 p_world) {
	vec3 q = (p_world - shell_centre) / shell_scale;
	if (q.y < 0.0) {
		float r = length(q.xz);
		return (r - 1.0) * shell_scale.x;
	}
	float l = max(length(q), 1e-4);
	return (l - 1.0) * length(shell_scale * (q / l));
}

void vertex() {
	unit_pos = VERTEX;
	vec3 n = normalize(VERTEX);
	float toward = max(dot(n, seam_dir), 0.0);
	// the side facing the shell swells out to meet it; the whole bubble breathes a little
	float wobble = 0.014 * sin(TIME * 2.7 + VERTEX.y * 5.0 + seed) * (1.0 - dissolve);
	VERTEX += n * (bulge * toward * toward * toward + wobble);
}

void fragment() {
	float y = unit_pos.y;

	// Forming: shown below the rising front, with a bright band at it (gone once it reaches the roof).
	float front = mix(ground_y, 1.08, rise);
	float shown = 1.0 - smoothstep(front - 0.06, front, y);
	float front_band = (1.0 - smoothstep(0.0, 0.07, abs(y - front + 0.03))) * step(0.001, rise) * (1.0 - smoothstep(0.85, 1.0, rise));

	// Fresnel rim.
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float rim = pow(1.0 - facing, 2.4);

	// Hex lattice (a squashed stereographic layout from below, so the cells keep their shape over the roof).
	vec2 sp = unit_pos.xz / (1.35 + y);
	vec4 hx = vf_hex(sp * hex_cells);
	vec2 q = abs(hx.xy);
	float hd = max(q.x, dot(q, vec2(0.5, 0.8660254)));
	float line = smoothstep(0.42, 0.5, hd);
	float cell = vf_hash(hx.zw + vec2(seed, seed * 0.37));
	float shimmer = smoothstep(0.75, 1.0, sin(TIME * 1.7 + cell * 6.2831853 + y * 3.0));

	// Where it meets the ground (strong near the bottom) or the hull inside it (faint).
	float depth = texture(field_depth, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	behind.xyz /= behind.w;
	float gap = length(behind.xyz) - length(VERTEX);
	float touch = 1.0 - smoothstep(0.0, 0.6, gap);
	touch *= mix(0.25, 1.0, 1.0 - smoothstep(ground_y + 0.35, ground_y + 0.9, y));

	// The side toward the big shell: brighter, blue, rippling in toward the contact.
	float toward = dot(normalize(unit_pos), seam_dir);
	float seam_mask = smoothstep(0.35, 0.92, toward) * seam;
	float seam_wave = 0.5 + 0.5 * sin(toward * 26.0 - TIME * 9.0);

	// Where the two fields cut through each other: a crackling seam line; past it the bubble wears the shell's colour.
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float sd = vf_shell(world);
	float across = (1.0 - smoothstep(0.0, 0.32, abs(sd))) * shell_on * seam;
	float glow_band = (1.0 - smoothstep(0.0, 1.4, abs(sd))) * shell_on * seam;
	float ang = atan(unit_pos.z, unit_pos.x);
	float crackle = step(0.55, vf_hash(vec2(floor(ang * 9.0 + y * 4.0), floor(TIME * 19.0) + seed)));
	float own_side = vf_shell(NODE_POSITION_WORLD) < 0.0 ? -1.0 : 1.0; // the bubble's centre: inside (-1) or outside
	float far_side = (1.0 - smoothstep(-0.6, 0.6, sd * own_side)) * shell_on;

	// Locking: a hashed stutter (~15 Hz) with a fast buzz.
	float t = TIME * 15.0 + seed;
	float buzz = vf_hash(vec2(floor(t), seed));
	float stutter = mix(1.0, step(0.3, buzz) * (0.7 + 0.3 * sin(TIME * 70.0)), lock_flicker);

	// Dissolving bottom-up behind a ragged, bright edge.
	float dfront = mix(ground_y - 0.15, 1.15, dissolve);
	float ragged = (cell - 0.5) * 0.22 + 0.05 * sin(ang * 9.0 + TIME * 3.0);
	float kept = smoothstep(dfront - 0.03, dfront + 0.03, y - ragged);
	float dedge = (1.0 - smoothstep(0.0, 0.09, abs(y - ragged - dfront))) * step(0.001, dissolve) * (1.0 - smoothstep(0.92, 1.0, dissolve));

	float side = FRONT_FACING ? 1.0 : 0.5; // the far wall, seen through the near one, is fainter
	float a = rim * 0.75 + line * (0.05 + 0.3 * shimmer) * (0.35 + rim) + 0.03 + touch * 0.6;
	a += seam_mask * (0.25 + 0.55 * seam_wave) * (0.4 + line);
	a += glow_band * 0.35 + across * (0.9 + 1.4 * crackle);
	a = a * shown * kept + front_band * 0.9 + dedge * 1.2;
	a *= stutter * strength * side * (1.0 + flash * 2.5);

	// Colour: warm, turning the shell's blue toward the seam, past the cut and as it merges.
	float exchange = clamp(seam_mask * 0.9 + far_side * seam * 0.75 + merge, 0.0, 1.0);
	vec3 col = mix(field_tint, seam_colour, exchange);
	float hot = clamp(front_band + dedge + flash * 0.5 + across * 0.8, 0.0, 1.0);
	col = mix(col, vec3(0.95, 0.97, 1.0), hot * 0.6);
	ALBEDO = col * (1.0 + flash * 1.5 + (front_band + dedge + across) * 0.8);
	ALPHA = clamp(a, 0.0, 1.0);
}
