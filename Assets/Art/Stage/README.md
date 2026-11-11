# Stage sprites (A5 living scene)

Rendered from the website's own drawings by `tools/render-stage-sprites.mjs` in the noit-platform repo
(scene paths from `RoleScene.astro`, colours from `lib/stage.ts`, glyphs from `lib/role.ts`).
Transparent PNGs, imported as Sprites (alphaIsTransparency, no mipmaps, bilinear). Re-run the script to regenerate.

## Shared frame
The 10 scenes and `trail.png` are all 1440x600 (the website's 480x200 viewBox x 3), so they overlay
pixel for pixel: put them in one RectTransform of the same size (aspect 480:200) and stretch the
children to fill. Pins sit in the same frame (frame px = viewBox units x 3):

| Overlay | Top-left in the 1440x600 frame | Size |
|---|---|---|
| pin 1 | 317.4, 297.3 | 81.2 x 106.3 |
| pin 2 | 612.9, 208.5 | 81.2 x 106.3 |

The pin's tip (the point) is bottom-centre. `trail.png` is `#0066cc`, stroke 2.95 (x3), round caps; it runs
left to right, so a Filled/Horizontal/Left fill reveals it.

## Files
| File | Size (px) | Notes |
|---|---|---|
| `scene-<key>-day.png` | 1440x600 | key = guide, teacher, broker, other, viewer (5 files) |
| `scene-<key>-night.png` | 1440x600 | same keys, night palette (5 files) |
| `trail.png` | 1440x600 | the phone route, `#0066cc`, same frame as the scenes |
| `pin.png` | 104x136 | 26x34 x 4, blue `#0066cc` with a white centre |
| `cloud.png` | 480x160 | 120x40 x 4, white; tint in Unity (day white 95%, night white 7%) |
| `sun.png` | 120x120 | `#fff6d6` disc, 80 px across, centred |
| `moon.png` | 120x120 | `#f3eccb` crescent from RoleStage (same 60-unit scale as the sun) |
| `star.png` | 16x16 | white dot |
| `icon-<name>.png` | 96x96 | white 24-unit line glyphs, stroke 2 (x4); tint in Unity |

Icon names: the 16 stage icons `book team chart route cols ar house camera map key sliders swap plus pin check arrow`
(the glyph names `role.ts` `stageFor` uses for signs and moments) and the 7 seal glyphs
`teacher guide broker other viewer pencil ask`. The moon's crescent sits left of its box centre, so
the moon box sits 4 CSS px (about 11 px at 1080 wide) to the left of where the sun box sits to match the website.
