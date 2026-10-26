# Vector V02 — curved surface revision

Offline editable vehicle art candidate responding to F1: V01 looked too angular. The Vector wedge, four wheels, twin central exhausts and chevron tail lamps remain. Longitudinal and transverse body rings now form a dense Catmull-Rom surface; wheel openings use 128 samples, with rounded cut edges. Windscreen, roof, rear and side glazing have explicit curved grids. Hood lines follow the new surface. Projector cells, hood vents, arch lips and a rear hex grille add geometry at close range.

V01 remains untouched in `../vector-v01`. V02 is an authored Blender model, not an image-generation edit. Curves and modelling modifiers remain editable. Main authoring axes: X right, Y forward, Z up, metres.

Files:
- `Vector_V02.blend`: editable car only.
- `Vector_V02.fbx`, `Vector_V02.glb`: independent exchange exports, car only.
- `Vector_CloudCity_Review.blend`: packed project textures, representative road/city set, nine named cameras, five rival colour variants.
- `create_model.py`, `render_views.py`, `verify_exports.py`: reproducible generation, render and independent import validation.
- `manifest.json`, `export-validation.json`: measured geometry and checks.

Run from the repository root with Blender 5.2:

```sh
blender -b --python art/candidates/vector-v02/create_model.py -- .local/vector-v02/final
blender -b --python art/candidates/vector-v02/verify_exports.py -- .local/vector-v02/final
```

Review: `/Users/eaafonasev/Projects/star-racing/planning/evidence/create-vector-art-candidate/v02/index.html`. Switch V01/V02 while keeping the same view. Final previews are 1920 pixels wide. The chase camera uses the existing unsplit offsets and 65° vertical FOV. The set is representative, not the exact Unity track or URP rendering.

This is a detailed offline model, not a runtime-optimized asset. No gameplay, collider, wheel animation, LOD, performance, Player or device acceptance is claimed. User visual acceptance is pending.
