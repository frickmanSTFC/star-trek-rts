// Starship hull plating: a brick grid of panels (seams darkened, anti-aliased with fwidth so they thin out with
// distance), each panel a slightly different shade and gloss (the "aztec" look), and optional rows of lit windows
// along bands of the mesh's v coordinate (lathed parts put v along their profile). Used by ShipModels.
shader_type spatial;

uniform vec3 hull_colour : source_color = vec3(0.8, 0.82, 0.86);
// Panels across the mesh's UV (u around, v along).
uniform vec2 panels = vec2(64.0, 16.0);
uniform float aztec = 0.09;
uniform float metal = 0.4;
// Window rows: (v centre, half height in v, windows around, share lit). z = 0 turns a row off.
uniform vec4 windows_a = vec4(0.0);
uniform vec4 windows_b = vec4(0.0);
uniform vec3 window_colour : source_color = vec3(1.0, 0.86, 0.62);
uniform float window_energy = 2.5;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float window_row(vec2 uv, vec4 row) {
	if (row.z <= 0.0) return 0.0;
	float across = 1.0 - smoothstep(row.y * 0.55, row.y, abs(uv.y - row.x));
	float cu = uv.x * row.z;
	float fu = fract(cu);
	float pane = smoothstep(0.2, 0.32, fu) * (1.0 - smoothstep(0.68, 0.8, fu));
	float lit = step(hash21(vec2(floor(cu), row.x * 97.0)), row.w);
	return across * pane * lit;
}

void fragment() {
	vec2 g = UV * panels;
	g.x += step(1.0, mod(floor(g.y), 2.0)) * 0.5; // brick offset on alternate rows
	vec2 cell = floor(g);
	vec2 f = fract(g);
	vec2 fw = max(fwidth(g), vec2(1e-4));
	vec2 e = smoothstep(vec2(0.0), fw * 1.2, f) * smoothstep(vec2(0.0), fw * 1.2, 1.0 - f);
	float seam = e.x * e.y;
	float t = hash21(cell);
	vec3 c = hull_colour * (1.0 + (t - 0.5) * aztec * 2.0);
	ALBEDO = mix(c * 0.7, c, seam);
	METALLIC = metal;
	ROUGHNESS = 0.3 + 0.22 * t;
	float w = max(window_row(UV, windows_a), window_row(UV, windows_b));
	EMISSION = window_colour * window_energy * w;
}
