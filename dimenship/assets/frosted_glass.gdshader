shader_type canvas_item;

// Frosted glass for the shell's panels.
//
// The pane samples the backdrop texture directly instead of the screen. The backdrop is the only
// thing ever behind a panel, so a direct sample costs one fetch per pixel and needs neither a
// BackBufferCopy nor screen-texture mipmaps, which the Mobile renderer does not guarantee. The
// trade is that this shader must reproduce two things the shell does in C#: the backdrop
// TextureRect's KeepAspectCovered fit, and the scrim laid over it. Both arrive as uniforms so
// ShellPalette stays the single source of truth for colour.

uniform sampler2D backdrop : filter_linear_mipmap, source_color;
uniform vec4 scrim : source_color = vec4(0.04, 0.05, 0.06, 0.77);
uniform vec4 tint : source_color = vec4(0.07, 0.09, 0.11, 0.8);
uniform float blur_lod : hint_range(0.0, 10.0) = 3.5;

void fragment() {
	vec2 view = 1.0 / SCREEN_PIXEL_SIZE;
	vec2 source = vec2(textureSize(backdrop, 0));

	// KeepAspectCovered: scale until the image covers both axes, centre it, crop the overhang.
	float cover = max(view.x / source.x, view.y / source.y);
	vec2 drawn = source * cover;
	vec2 uv = (SCREEN_UV * view + (drawn - view) * 0.5) / drawn;

	// The mip chain is the blur. Mip 4.5 of a 2516px source is roughly a 100px-wide image, which
	// is all the structure a frosted pane should keep — and it is a single filtered fetch.
	vec3 pane = textureLod(backdrop, uv, blur_lod).rgb;

	// Alpha on these two is a mix weight, not an opacity: the pane draws opaque so no sharp copy
	// of the backdrop bleeds through from the layer underneath.
	pane = mix(pane, scrim.rgb, scrim.a);
	pane = mix(pane, tint.rgb, tint.a);

	COLOR = vec4(pane, 1.0);
}
