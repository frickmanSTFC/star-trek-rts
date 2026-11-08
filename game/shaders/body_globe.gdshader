// Procedural globes for the bodies the flagship can orbit that have no surface map (OrbitView.Bodies.cs): one shader,
// a mode per kind of body, colours from C#. Lit by the orbit view's own sun (Godot's lighting), so the terminator
// and the night side come for free. Everything is computed from the unit direction in model space, so the pattern
// turns with the globe and never stretches at the poles.
//   globe_mode 0  gas giant: latitude bands warped by turbulence, a slow bounded swirl, a few oval storms
//   globe_mode 1  airless rock (Class D, rocky moons): grey/tan maria, two scales of craters with rims
//   globe_mode 2  ice moon: pale blue-white, reddish crack lines (lineae), faint craters
//   globe_mode 3  Class K: rust desert, dark plains, faint craters, small polar caps
//   globe_mode 4  Class H: ochre sand, dune streaks, dark rock outcrops
//   globe_mode 5  Class Y: dark basalt, glowing lava cracks
//   globe_mode 6  Class N: deep ocean, a few islands, drifting white cloud swirls
// Note: PI and TAU are Godot built-ins; never redefine them here.
shader_type spatial;
render_mode cull_disabled; // like planet.gdshader: OrbitView's own UV sphere is wound counter-clockwise

uniform int globe_mode = 1;
uniform vec3 colour_a : source_color = vec3(0.58, 0.55, 0.50);
uniform vec3 colour_b : source_color = vec3(0.36, 0.34, 0.31);
uniform vec3 colour_c : source_color = vec3(0.72, 0.68, 0.60);
uniform float seed = 0.0;
uniform float band_count = 9.0;
uniform float glow_strength = 0.0;
uniform vec3 rim_colour : source_color = vec3(0.0);
uniform float rim_strength = 0.0;
uniform float relief = 1.0;

varying vec3 dir_local;

void vertex() {
	dir_local = normalize(VERTEX);
}

float hash13(vec3 q) {
	q = fract(q * 0.1031);
	q += dot(q, q.zyx + 31.32);
	return fract((q.x + q.y) * q.z);
}

float value_noise(vec3 q) {
	vec3 i = floor(q);
	vec3 f = fract(q);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float n000 = hash13(i);
	float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
	float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
	float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
	float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
	float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
	float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
	float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
	float x00 = mix(n000, n100, u.x);
	float x10 = mix(n010, n110, u.x);
	float x01 = mix(n001, n101, u.x);
	float x11 = mix(n011, n111, u.x);
	return mix(mix(x00, x10, u.y), mix(x01, x11, u.y), u.z);
}

// Five octaves, roughly 0..1 around 0.5.
float fbm(vec3 q) {
	float sum = 0.0;
	float amp = 0.5;
	float total = 0.0;
	for (int k = 0; k < 5; k++) {
		sum += amp * value_noise(q);
		total += amp;
		q = q * 2.03 + vec3(1.7, 9.2, 3.1);
		amp *= 0.5;
	}
	return sum / total;
}

// Cellular noise: x = distance to the nearest feature point, y = to the second nearest, z = the nearest cell's random id.
vec3 cells(vec3 q) {
	vec3 i = floor(q);
	vec3 f = fract(q);
	float d1 = 8.0;
	float d2 = 8.0;
	float id = 0.0;
	for (int z = -1; z <= 1; z++) {
		for (int y = -1; y <= 1; y++) {
			for (int x = -1; x <= 1; x++) {
				vec3 o = vec3(float(x), float(y), float(z));
				vec3 c = i + o;
				vec3 r = vec3(hash13(c), hash13(c + 17.1), hash13(c + 41.7));
				vec3 d = o + r - f;
				float dd = dot(d, d);
				if (dd < d1) {
					d2 = d1;
					d1 = dd;
					id = hash13(c + 5.3);
				} else if (dd < d2) {
					d2 = dd;
				}
			}
		}
	}
	return vec3(sqrt(d1), sqrt(d2), id);
}

// Crater field at one scale: returns height (bowl negative, rim positive) for the craters that exist (about 60% of cells).
float craters(vec3 q, float keep) {
	vec3 c = cells(q);
	float has = step(1.0 - keep, c.z);
	float radius = 0.16 + 0.26 * fract(c.z * 7.31);
	float x = c.x / radius;
	float bowl = (1.0 - smoothstep(0.0, 1.0, x)) * -0.9;
	float rim = exp(-(x - 1.0) * (x - 1.0) * 18.0) * 0.6;
	return (bowl + rim) * has;
}

// Thin lines along the cell borders (cracks, lava channels): 1 on a line, 0 away from it.
float cracks(vec3 q, float width) {
	vec3 c = cells(q);
	return 1.0 - smoothstep(0.0, width, c.y - c.x);
}

// A unit direction turned about Y by an angle.
vec3 turn_y(vec3 d, float a) {
	float s = sin(a);
	float co = cos(a);
	return vec3(co * d.x + s * d.z, d.y, -s * d.x + co * d.z);
}

void fragment() {
	vec3 d = normalize(dir_local);
	vec3 sd = d + vec3(seed * 0.173, seed * 0.091, seed * 0.237);
	float lat = d.y;
	vec3 albedo = colour_a;
	float height = 0.0;
	float rough = 0.92;
	float spec = 0.3;
	vec3 glow = vec3(0.0);

	if (globe_mode == 0) {
		// Bands: latitude stripes, warped by turbulence; a gentle swirl that sways back and forth (bounded, so the
		// pattern never shears into threads however long the game runs).
		float sway = 0.35 * sin(TIME * 0.025 + lat * band_count * 1.7);
		vec3 w = turn_y(d, sway);
		float turb = fbm(w * 3.0 + vec3(seed));
		float b = lat * band_count + (turb - 0.5) * 2.2;
		float band = 0.5 + 0.5 * sin(b * PI);
		float streak = fbm(vec3(w.x * 2.5, lat * 38.0, w.z * 2.5) + vec3(seed * 1.3));
		albedo = mix(colour_b, colour_a, band);
		albedo = mix(albedo, colour_c, smoothstep(0.55, 0.85, streak) * 0.55);
		// Oval storms: big cells in a stretched space, a spiral inside the rare ones.
		vec3 sq = vec3(w.x * 2.2, lat * 6.5, w.z * 2.2) + vec3(seed * 2.1);
		vec3 st = cells(sq);
		float storm = step(0.86, st.z) * (1.0 - smoothstep(0.18, 0.42, st.x));
		float spiral = 0.5 + 0.5 * sin(st.x * 40.0 - TIME * 0.4 + st.z * 20.0);
		albedo = mix(albedo, mix(colour_c, colour_b * 1.25, spiral * 0.6), storm * 0.85);
		rough = 0.8;
		spec = 0.2;
	} else if (globe_mode == 1) {
		float maria = smoothstep(0.42, 0.6, fbm(sd * 2.2));
		albedo = mix(colour_a, colour_b, maria);
		albedo *= 0.85 + 0.3 * fbm(sd * 14.0);
		float big = craters(sd * 5.0, 0.6);
		float pits = craters(sd * 17.0 + vec3(3.1), 0.55);
		height = big + pits * 0.45;
		albedo = mix(albedo, colour_c, clamp(big, 0.0, 1.0) * 0.6 + clamp(pits, 0.0, 1.0) * 0.3);
		albedo *= 1.0 + clamp(height, -1.0, 0.0) * 0.25;
	} else if (globe_mode == 2) {
		float tone = fbm(sd * 3.0);
		albedo = mix(colour_b, colour_a, smoothstep(0.3, 0.7, tone));
		float lines = cracks(sd * 4.0, 0.035) * 0.8 + cracks(sd * 9.0 + vec3(7.7), 0.03) * 0.5;
		albedo = mix(albedo, colour_c, clamp(lines, 0.0, 1.0) * 0.75);
		height = -lines * 0.3 + craters(sd * 11.0, 0.25) * 0.4;
		rough = 0.55;
		spec = 0.55;
	} else if (globe_mode == 3) {
		float plains = smoothstep(0.45, 0.62, fbm(sd * 2.6));
		albedo = mix(colour_a, colour_b, plains);
		albedo *= 0.88 + 0.24 * fbm(sd * 16.0);
		height = craters(sd * 7.0, 0.35) * 0.6 + (fbm(sd * 9.0) - 0.5) * 0.8;
		float cap = smoothstep(0.86, 0.93, abs(lat) + (fbm(sd * 6.0) - 0.5) * 0.08);
		albedo = mix(albedo, colour_c, cap);
	} else if (globe_mode == 4) {
		vec3 w = sd + vec3(fbm(sd * 2.0), fbm(sd * 2.0 + vec3(5.2)), fbm(sd * 2.0 + vec3(9.1))) * 0.6;
		float dunes = 0.5 + 0.5 * sin((w.x * 0.6 + w.z * 0.8 + lat * 0.3) * 60.0);
		float rock = smoothstep(0.58, 0.7, fbm(sd * 3.5));
		albedo = mix(colour_a, colour_c, dunes * 0.35);
		albedo = mix(albedo, colour_b, rock);
		height = dunes * 0.25 * (1.0 - rock) + rock * (fbm(sd * 12.0) - 0.3);
	} else if (globe_mode == 5) {
		float tone = fbm(sd * 4.0);
		albedo = mix(colour_a, colour_b, smoothstep(0.35, 0.75, tone));
		float lava = cracks(sd * 3.5, 0.06) * 0.9 + cracks(sd * 8.0 + vec3(2.3), 0.04) * 0.6;
		lava = clamp(lava, 0.0, 1.0) * smoothstep(0.3, 0.6, fbm(sd * 2.0 + vec3(4.4)));
		float pulse = 0.85 + 0.15 * sin(TIME * 0.8 + tone * 12.0);
		glow = colour_c * lava * glow_strength * pulse;
		albedo = mix(albedo, colour_c * 0.6, lava * 0.5);
		height = (tone - 0.5) * 1.2 - lava * 0.5;
		rough = 0.85;
	} else {
		// Ocean world: depth tones, rare islands, two layers of drifting cloud swirls.
		float sea_depth = fbm(sd * 2.4);
		albedo = mix(colour_b, colour_a, smoothstep(0.35, 0.7, sea_depth));
		float island = smoothstep(0.7, 0.74, fbm(sd * 4.0 + vec3(1.9)));
		albedo = mix(albedo, vec3(0.42, 0.45, 0.3), island);
		vec3 cw = turn_y(d, TIME * 0.006);
		float cloud = fbm(cw * 3.2 + vec3(seed) + vec3(fbm(cw * 1.5) * 1.5));
		float cover = smoothstep(0.5, 0.68, cloud + 0.06 * cos(lat * 7.0));
		albedo = mix(albedo, colour_c, cover);
		rough = mix(0.28, 0.95, max(cover, island));
		spec = mix(0.6, 0.2, cover);
		height = island * 0.5;
	}

	// Relief: bump the normal by the screen-space slope of the height (the surface's own derivatives, so no tangents
	// are needed). Heights are in world units scaled by relief; a degenerate result keeps the mesh normal.
	float h = height * relief;
	vec3 dpdx = dFdx(VERTEX);
	vec3 dpdy = dFdy(VERTEX);
	float dhdx = dFdx(h);
	float dhdy = dFdy(h);
	vec3 r1 = cross(dpdy, NORMAL);
	vec3 r2 = cross(NORMAL, dpdx);
	float det = dot(dpdx, r1);
	vec3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
	vec3 bumped = abs(det) * NORMAL - grad;
	if (dot(bumped, bumped) > 0.00000001) {
		NORMAL = normalize(bumped);
	}

	float facing = clamp(dot(normalize(NORMAL), VIEW), 0.0, 1.0);
	float rim = pow(1.0 - facing, 3.0) * rim_strength;

	ALBEDO = clamp(albedo, vec3(0.0), vec3(1.0));
	ROUGHNESS = rough;
	SPECULAR = spec;
	EMISSION = glow + rim_colour * rim;
}
