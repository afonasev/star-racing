# Sci-fi environment textures

Generated with the built-in imagegen tool on 2026-10-07 for the agreed clean sci-fi direction with moderate wear. Original outputs are preserved verbatim; Unity imports at 1024 with mipmaps, Repeat wrapping and anisotropy 8. No external texture library is required. STRIDE assets are unchanged.

## Prompts
Common prompt: "Use case: stylized-concept. Asset type: Unity game surface texture. Seamlessly tileable, orthographic flat albedo only, no words, logos, border, perspective, directional lighting or baked shadows. Square production clean science fiction texture, large clean shapes and restrained microdetail."

- Road: neutral medium gray anti-slip composite metal plates, broad mostly uninterrupted driving surface, restrained recessed panel joins, fine grain and subtle moderate edge wear. No lane lines or arrows.
- Hull: neutral light gray brushed metal rectangular panel modules, restrained recessed dark seams, a few small fastener details and subtle moderate edge wear. No glowing elements.
- Facade: neutral gray silver architectural frame with broad dark desaturated blue glass window modules in an even repeating rectangular grid, restrained seams and subtle moderate wear. No neon or reflections of identifiable scenery.

The former mesh planet texture was retired when planets moved to Environment/Sky.

## Restrained surface finish (2026-10-12)

`tools/assets/generate-track-surfaces.py --finish-only` deterministically authors four PBR maps without touching any existing albedo. Road maps are 512×4096; rail maps 4096×128. They share the existing 288 m cycle and 20 m blends; rails retain their 72 m phase offset and reversed district order. No micro-noise is added.

`*Finish`: linear RGBA, red metallic / alpha smoothness, transparency disabled. Matte road variants use smoothness 0.20–0.32 and metallic 0.04–0.22; satin housings use smoothness 0.22–0.42 and metallic 0.28–0.62, with rougher joints/recesses. `*Normal`: tangent-space normals from shallow rounded joints (road 12 mm, rail 10 mm), imported as NormalMap with bump scale 0.65. Both data maps use Repeat, trilinear mipmaps, anisotropy 16, compressed HQ and are non-readable.

URP Lit uses the same BaseMap UV/ST for all three maps. Tangents are generated only on render meshes; collider vertices/triangles remain unchanged. Four extra texture resources and two additional Lit texture samples are introduced; no extra object, geometry, submesh or render pass is introduced. FPS/device performance is not asserted by the Editor capture.
