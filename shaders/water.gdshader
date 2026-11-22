shader_type spatial;
// Animated block water v2: geometric swell on top faces only, a shoreline
// depth-blended transparency using the screen depth buffer, rippling
// specular normals and fresnel-boosted alpha at grazing angles.
render_mode blend_mix, cull_disabled, depth_draw_opaque;

uniform sampler2D atlas_texture : filter_nearest, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture;
uniform float wave_strength : hint_range(0.0, 0.15) = 0.045;
uniform float wave_speed : hint_range(0.0, 4.0) = 1.3;
uniform float alpha_base : hint_range(0.0, 1.0) = 0.82;
uniform float shore_fade : hint_range(0.1, 8.0) = 2.5;
uniform float dim : hint_range(0.0, 1.0) = 1.0;
uniform float sky_light_energy : hint_range(0.0, 1.25) = 1.0;
uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);
uniform vec3 sun_color = vec3(1.0, 0.96, 0.89);
uniform vec3 sky_light_color = vec3(0.88, 0.94, 1.0);
uniform float sunlight_energy : hint_range(0.0, 1.25) = 1.0;
uniform vec3 moon_direction = vec3(0.0, -1.0, 0.0);
uniform vec3 moon_color = vec3(0.62, 0.74, 1.0);
uniform float moonlight_energy : hint_range(0.0, 0.35) = 0.0;
uniform float caustic_strength : hint_range(0.0, 2.0) = 0.0;

varying vec3 world_pos;
varying vec3 world_normal;
varying float view_depth;

vec3 directional_sky_light(vec3 normal, float sky, float sky_exposure) {
    float sun_facing = max(dot(normalize(normal), sun_direction), 0.0);
    float direct_sun = clamp(sun_facing * sunlight_energy, 0.0, 1.0);
    float directional_gain = 0.88 + direct_sun * 0.12;
    float moon_facing = max(dot(normalize(normal), moon_direction), 0.0);
    float moon_fill = clamp(sky_exposure, 0.0, 1.0)
            * moonlight_energy * (0.28 + moon_facing * 0.72);
    vec3 sky_tint = mix(sky_light_color, sun_color, direct_sun * 0.32);
    return sky * sky_tint * directional_gain + moon_color * moon_fill;
}

void vertex() {
	vec3 base_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float swell = sin(base_world_pos.x * 1.1 + TIME * wave_speed)
		+ cos(base_world_pos.z * 1.3 + TIME * wave_speed * 0.8)
		+ sin((base_world_pos.x + base_world_pos.z) * 0.6 + TIME * wave_speed * 1.2);
	// Only the surface (upward faces) moves, so shorelines never tear.
	VERTEX.y += swell * wave_strength * max(NORMAL.y, 0.0);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// VERTEX is chunk-local. Reconstruct positive camera-space depth from the
	// translated world position so shoreline fading and distant ripple damping
	// behave identically in every streamed chunk.
	view_depth = -(VIEW_MATRIX * vec4(world_pos, 1.0)).z;
	// Damp ripples with distance: far sparkle is temporal AA's worst enemy.
	float ripple_fade = 1.0 - smoothstep(40.0, 120.0, view_depth);
	// Ripple the surface normal with two scrolling waves (model space; the
	// water mesh is axis-aligned so this stays stable).
	float rx = sin(world_pos.x * 2.1 + TIME * 1.7) * 0.04
		+ sin(world_pos.z * 1.7 + TIME * 1.1) * 0.03;
	float rz = cos(world_pos.z * 2.3 + TIME * 1.5) * 0.04
		+ cos(world_pos.x * 1.9 + TIME * 0.9) * 0.03;
	NORMAL = normalize(NORMAL + vec3(rx, 0.0, rz) * max(NORMAL.y, 0.0) * ripple_fade);
    world_normal = normalize(mat3(MODEL_MATRIX) * NORMAL);
}

void fragment() {
	vec4 tex = texture(atlas_texture, UV);
	float sky = clamp(COLOR.r * sky_light_energy, 0.0, 1.0);
	float block = clamp(COLOR.g, 0.0, 1.0);
	float shadowed_block = block * (1.0 - sky);
	float shade = COLOR.b;
	vec3 warm = vec3(1.0, 0.68, 0.38);
	vec3 illumination = vec3(0.04) + directional_sky_light(world_normal, sky, COLOR.r) + warm * shadowed_block * 0.35;
	vec3 base = tex.rgb * illumination * shade;
	// Shoreline fade: how much water sits between this fragment and the
	// opaque scene behind it.
	float raw_depth = texture(depth_texture, SCREEN_UV).x;
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
	vec4 behind = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	behind.xyz /= behind.w;
	float scene_depth = -behind.z;
	float thickness = max(scene_depth - view_depth, 0.0);
	float depth_alpha = clamp(thickness / shore_fade, 0.18, 1.0);
	float fresnel = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 2.0);
	// Restrained animated foam gives shallow river/lake edges a readable
	// transition without adding another material or particle system.
	float shoreline = 1.0 - smoothstep(0.15, max(shore_fade, 0.16), thickness);
	float foam_wave = 0.5 + 0.5 * sin(world_pos.x * 1.9 + world_pos.z * 1.4 + TIME * 0.55);
	float foam = shoreline * smoothstep(0.58, 0.88, foam_wave) * 0.20;
	ALBEDO = mix(base * dim, vec3(0.78, 0.89, 0.94), foam);
	EMISSION += tex.rgb * warm * shadowed_block * 0.08;
	ALPHA = clamp(tex.a * alpha_base * depth_alpha + fresnel * 0.2, 0.0, 1.0);
	ROUGHNESS = mix(0.12, 0.5, smoothstep(40.0, 120.0, view_depth) + foam * 0.4);
	// Caustic light webs on the underside of the surface (diver's view).
	if (!FRONT_FACING && caustic_strength > 0.0) {
		float w1 = sin(world_pos.x * 1.9 + TIME * 1.1) * sin(world_pos.z * 1.5 - TIME * 0.8);
		float w2 = sin((world_pos.x + world_pos.z) * 1.1 + TIME * 1.6);
		float web = pow(clamp(abs(w1 * w2) * 2.4, 0.0, 1.0), 3.0);
		ALBEDO += vec3(0.20, 0.23, 0.26) * web * caustic_strength;
		EMISSION += vec3(0.10, 0.12, 0.14) * web * caustic_strength;
	}
	METALLIC = 0.0;
	SPECULAR = 0.5;
}
