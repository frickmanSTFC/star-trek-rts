// Selection / hover rings round vehicles and buildings on the planet surface (scripts/PlanetView.Selection.cs). The mesh
// is built on the CPU every frame in world space: one band per ring that follows the terrain, UV.x = 0..1 round the ring,
// UV.y = 0 (inner edge) .. 1 (outer edge), COLOR = colour + strength, UV2.x = 1 selected (pulses) / 0 hover,
// UV2.y = a per-ring phase. Included by selection_ring.gdshader (depth tested) and selection_ring_xray.gdshader (a faint
// copy drawn through whatever hides the ring: trees, a building, the vehicle's own hull).
//
// Never under the ground: every vertex is pulled toward the camera along its view ray by ring_push + ring_push_per_unit
// times its distance (the same screen position, nearer in depth), so the band wins against the terrain, its bumps, short
// grass and a repair pad's slab on any slope, at any zoom, without z-fighting, while the vehicle (metres tall) still hides
// the part of the ring behind it. Unshaded and fog_disabled: the same bright ring by day, at night and in fog.
//
// Profile across the band (in core widths from the centre line; the band is +-RING_BAND wide): a bright core
// (|t| < 0.5), a thin dark rim right outside it so it reads on snow, sand and bright concrete, and a soft glow beyond that
// breathes with the pulse.

uniform float ring_push = 0.6;            // world units toward the camera, always
uniform float ring_push_per_unit = 0.004; // plus this much per unit of distance (depth precision falls off with distance)
uniform float ring_pulse_speed = 3.2;     // radians per second
uniform float ring_sweep_speed = 0.35;    // turns per second of the bright arc running round a selected ring

const float RING_BAND = 1.6; // must match PlanetView.Selection.cs RingBand

varying vec4 ring_colour;
varying vec2 ring_kind;

void vertex() {
	ring_colour = COLOR;
	ring_kind = UV2;
	vec3 view_pos = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float dist = length(view_pos);
	float pulled = max(dist - (ring_push + ring_push_per_unit * dist), 0.05);
	VERTEX = view_pos * (pulled / max(dist, 0.001));
	NORMAL = normalize((MODELVIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
}

void fragment() {
	float t = abs((UV.y - 0.5) * 2.0 * RING_BAND); // core widths from the centre line
	float aa = max(fwidth(t), 0.02);
	float selected = ring_kind.x;
	float pulse = selected * (0.5 + 0.5 * sin(TIME * ring_pulse_speed + ring_kind.y * 6.2831));
	// A bright arc running round a selected ring (slowly), so it reads as "live" even when still.
	float sweep = fract(UV.x - TIME * ring_sweep_speed + ring_kind.y);
	float arc = selected * smoothstep(0.0, 0.08, sweep) * (1.0 - smoothstep(0.08, 0.3, sweep));

	float core = 1.0 - smoothstep(0.5 - aa, 0.5 + aa, t);
	float rim = 1.0 - smoothstep(0.62, 0.82 + aa, t);
	float glow = (1.0 - smoothstep(0.6, RING_BAND, t)) * (0.28 + 0.4 * pulse);

	vec3 bright = ring_colour.rgb * (1.15 + 0.35 * pulse + 0.5 * arc);
	vec3 col = mix(ring_colour.rgb * 0.9, vec3(0.01, 0.02, 0.02), smoothstep(0.5, 0.62, t)); // dark rim outside the core
	col = mix(col, ring_colour.rgb, smoothstep(0.82, 1.0, t));                                 // then the glow's colour
	col = mix(col, bright, core);
	float alpha = max(core, max(rim * 0.55, glow));
#ifdef RING_XRAY
	alpha *= core * 0.22; // through obstacles: only a faint core line
	col = ring_colour.rgb;
#endif
	ALBEDO = col;
	ALPHA = clamp(alpha * ring_colour.a, 0.0, 1.0);
}
