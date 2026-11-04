// One cloud layer of the orbital globe (a sphere a little above the ground, spun slowly by OrbitView). Cover comes from
// clouds.gdshaderinc; lighting is done here: sunlit by day with a soft edge past the terminator, sunlight reddened by
// the air like the ground's, thick parts darker on the side away from the sun (one step toward the sun), and only the
// faint orbit ambient at night.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

#include "res://shaders/air.gdshaderinc"
#include "res://shaders/clouds.gdshaderinc"

uniform vec3 sun_dir = vec3(-0.6, 0.3, 0.75);
uniform vec3 sun_colour = vec3(1.0, 0.97, 0.9);
uniform float sun_energy = 1.7;
uniform float ambient = 0.07;
uniform vec3 layer_offset = vec3(0.0);
uniform vec3 layer_scale = vec3(1.2);
uniform float layer_cover = 0.58;
uniform float layer_softness = 0.11;
uniform float layer_opacity = 0.95;
// How much air lies between the sun and this layer, relative to the ground (higher layers: less, so less reddening).
uniform float air_thickness = 0.8;

varying vec3 local_dir;
varying vec3 world_dir;
varying vec3 sun_local;

void vertex() {
	local_dir = VERTEX;
	world_dir = (MODEL_MATRIX * vec4(VERTEX, 0.0)).xyz;
	sun_local = transpose(mat3(MODEL_MATRIX)) * sun_dir; // the layer only rotates, so the transpose is the inverse
}

void fragment() {
	vec3 d = normalize(local_dir);
	// Finer detail only when the camera is close (it would only shimmer from far off).
	float detail = 1.0 - smoothstep(170.0, 300.0, length(CAMERA_POSITION_WORLD - NODE_POSITION_WORLD));
	float field = cloud_field(d, layer_offset, layer_scale, detail);
	float a = cloud_alpha(field, d, layer_cover, layer_softness) * layer_opacity;
	if (a < 0.003) discard;
	float mu = dot(normalize(world_dir), normalize(sun_dir));
	float lit = smoothstep(-0.05, 0.2, mu);
	float toward = cloud_field(normalize(d + normalize(sun_local) * 0.012), layer_offset, layer_scale, 0.0);
	float shade = 1.0 - clamp((toward - field) * 4.0, 0.0, 0.45);
	ALBEDO = 0.85 * sun_energy * lit * shade * sun_colour * sunlight_at(mu, air_thickness) + vec3(ambient * 0.9);
	ALPHA = a;
}
