// Deposit crystals (MultiMesh): instance colour lights them up; alpha says how bright (dim when out of sight).
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

void fragment() {
	ALBEDO = COLOR.rgb * 0.6;
	EMISSION = COLOR.rgb * COLOR.a * 2.2;
	ROUGHNESS = 0.15;
	METALLIC = 0.2;
	SPECULAR = 0.8;
}
