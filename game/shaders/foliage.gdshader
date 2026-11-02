// Trees and other scattered props (MultiMesh): instance colour as albedo, fog from the same texture.
shader_type spatial;
render_mode diffuse_burley;

uniform sampler2D fog_tex : filter_linear, repeat_disable;
uniform vec2 map_tiles = vec2(44.0, 30.0);
uniform float hex_width = 10.0;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = vec2((world_pos.x / hex_width + 0.5) / map_tiles.x, (world_pos.z / (0.866 * hex_width) + 0.5) / map_tiles.y);
	float seen = texture(fog_tex, uv).r;
	ALBEDO = COLOR.rgb * seen;
	ROUGHNESS = 0.85;
	SPECULAR = 0.1;
}
