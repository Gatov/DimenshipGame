shader_type canvas_item;

// The loadout composer's frame art, drawn as a laser projection on dark glass.
//
// The artwork is white line work; this shader replaces its colour with `tint` and adds a soft halo
// sampled from the alpha around each pixel. The colour arrives as a uniform from
// ShellPalette.Projection, so the palette stays the single source of truth for colour. The glow
// belongs to the drawing only — no text is drawn through this material, so text stays sharp.
// A SubViewport with engine glow was rejected: its bloom would reach the text and the boxes too.

uniform vec4 tint : source_color = vec4(1.0);
uniform float halo_px : hint_range(0.0, 8.0) = 3.0;
uniform float halo_strength : hint_range(0.0, 1.0) = 0.35;

void fragment() {
	float line = texture(TEXTURE, UV).a;
	float halo = 0.0;

	// Twelve directions at two radii: enough for a halo with no visible spokes around 1-2px
	// strokes, and 24 fetches on a texture that is mostly transparent.
	for (int i = 0; i < 12; i++) {
		float angle = float(i) * 0.5235988;
		vec2 offset = vec2(cos(angle), sin(angle)) * halo_px * TEXTURE_PIXEL_SIZE;
		halo += texture(TEXTURE, UV + offset).a;
		halo += texture(TEXTURE, UV + offset * 0.5).a;
	}

	halo /= 24.0;
	COLOR = vec4(tint.rgb, max(line, halo * halo_strength) * tint.a);
}
