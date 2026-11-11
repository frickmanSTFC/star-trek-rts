// Heat shimmer (visual pass G2, PlanetView.Effects.cs, GraphicsQuality.Effects): a column that turns round the
// vertical axis to face the camera and shows the opaque picture behind it bent by rising ripples. Unshaded, so the
// screen colour (linear, before tonemapping) comes out as it went in. render_priority below the steam and flames
// (set by the C# side), so they draw over it rather than vanish inside it. Fades out with distance.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float strength = 0.01; // screen-UV shift at full heat, up close
uniform float speed = 1.0;
uniform float heat = 1.0;
uniform float seed = 0.0;
uniform float fade_near = 140.0;
uniform float fade_far = 320.0;

void vertex() {
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	right = normalize(vec3(right.x, 0.0, right.z) + vec3(0.0001, 0.0, 0.0));
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 back = cross(right, up);
	float sx = length(MODEL_MATRIX[0].xyz);
	float sy = length(MODEL_MATRIX[1].xyz);
	mat4 world = mat4(vec4(right * sx, 0.0), vec4(up * sy, 0.0), vec4(back, 0.0), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
}

void fragment() {
	// QuadMesh UV: y 0 at the top. Strongest low in the column, soft at the sides, top and bottom.
	float side = smoothstep(0.0, 0.35, UV.x) * (1.0 - smoothstep(0.65, 1.0, UV.x));
	float along = (1.0 - smoothstep(0.82, 1.0, UV.y)) * smoothstep(0.0, 0.5, UV.y);
	float t = TIME * speed + seed * 13.0;
	vec2 wob = vec2(sin(UV.y * 23.0 + t * 7.1 + UV.x * 4.0) + 0.5 * sin(UV.y * 41.0 + t * 11.3),
			cos(UV.y * 17.0 + t * 6.3 - UV.x * 6.0));
	float dist = length(VERTEX);
	float far_k = 1.0 - smoothstep(fade_near, fade_far, dist);
	float k = side * along * heat * far_k;
	vec2 off = wob * strength * k * clamp(40.0 / max(dist, 1.0), 0.2, 1.0) * (1.0 + 0.6 * UV.y);
	ALBEDO = textureLod(screen_tex, SCREEN_UV + off, 0.0).rgb;
	ALPHA = clamp(k * 1.6, 0.0, 1.0);
}
