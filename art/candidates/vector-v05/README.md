# Vector V05 — slightly raised cabin

F4 raises the roof and upper glazing approximately 10 cm relative to V04, following user feedback that its cabin was too flat. Body, wheels, materials and cameras are retained. V04 remains available for comparison.

The selected visual reference remains `../vector-v01/design-reference.png`. This is an authored interpretation of a two-view concept image, not a measured CAD reconstruction. Similarity must be judged visually. Camera angles are chosen to resemble the concept presentation; no claim of exact camera calibration is made.

Files: editable `Vector_V05.blend`, car-only FBX/GLB, packed `Vector_CloudCity_Review.blend`, generator and rendering/verification scripts, geometry manifest and measured export validation.

```sh
blender -b --python art/candidates/vector-v05/create_model.py -- .local/vector-v05/result --no-render
blender -b --python art/candidates/vector-v05/verify_exports.py -- .local/vector-v05/result
blender -b .local/vector-v05/result/Vector_CloudCity_Review.blend --python art/candidates/vector-v05/render_views.py -- .local/vector-v05/result
```

`--draft --clay` produces three neutral material review views to inspect primary form. `--draft` produces the same three views with materials. The full render pass contains eleven views, including chase with five differently coloured opponents. This Blender scene uses project textures and a representative track layout; it is not a Unity capture. Procedural paint finish is Blender-specific, with exchange files carrying geometry and base materials.

Design dimensions: 0.48 m tyre radius; wheel pivot positions x ±0.985 m, y ±1.50 m, z 0.48 m; roof approximately 1.28 m; body shoulder width up to 2.36 m. These are modelling choices for this candidate, not game physics contracts. No runtime install, LOD, performance, Player/device acceptance, build or deployment is included.

Review: `/Users/eaafonasev/Projects/star-racing/planning/evidence/create-vector-art-candidate/v05/index.html`.
