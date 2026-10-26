# Vector V03 — detailed angular reference revision

F2 restores the art direction of the selected Vector concept. Straight body facets are intentional. This revision starts from V01, preserving its wedge silhouette while replacing simple exterior pieces with authored recessed front and rear fascias, broader segmented lamp units, body-fitted side intakes, a layered exhaust island and diffuser, lower planar canopy, framed roof inserts and forged aero wheels. Millimetre bevels provide highlights; there is no curved-body interpolation from V02.

V01 and V02 remain untouched. The original visual reference is `../vector-v01/design-reference.png`.

Files:
- `Vector_V03.blend`: editable car source.
- `Vector_V03.fbx` / `Vector_V03.glb`: car-only exchange assets.
- `Vector_CloudCity_Review.blend`: packed project textures, representative track decorations, named cameras and five colour rivals.
- Python generation, render and independent export checks; manifests with measured geometry and validation.

From the repository root, using Blender 5.2:

```sh
blender -b --python art/candidates/vector-v03/create_model.py -- .local/vector-v03/result
blender -b --python art/candidates/vector-v03/verify_exports.py -- .local/vector-v03/result
```

Review: `/Users/eaafonasev/Projects/star-racing/planning/evidence/create-vector-art-candidate/v03/index.html`. The eleven views include the previous nine cameras and two lower angles resembling the concept presentation. The source concept can be shown separately in the gallery. Original and revised car files remain independently reviewable.

These are actual Blender renders, not generated raster mockups. The representative scene uses project textures, not an exact Unity track export. No runtime installation, physics, LOD, performance, Player/device acceptance or game publication is included. Visual acceptance belongs to the user.

The Blender paint has a procedural microscopic flake/bump finish. FBX/GLB preserve the geometry and base materials; exact procedural shading is not claimed to transfer to another renderer. The final source includes fitted hood/fender and rear bumper joints.
