# Cloudline C02 — softened body and fuller front

F1 revision requested by the user: reduce visible sharp body angles, make the front thicker and smoothly rounded without square corners. C01 remains unchanged in the adjacent directory.

C02 replaces the thin faceted hood/nose with a closed rounded volume, smooths all four fender shoulders, rounds small body bevels and uses curved headlight strips projected onto the new hood. The rear light signature, large wheels, wheelbase, canopy silhouette and material palette remain recognizable. The overall vehicle retains some designed creases; this is not a fully rounded redesign.

- `Cloudline_C02.blend`: editable model, smooth-surface modifiers, ten materials and studio.
- `Cloudline_C02.fbx`: geometry/material exchange asset for Unity; four separate wheel pivots.
- `Cloudline_C02.glb`: portable model with PBR materials.
- Four matching camera renders, with identical studio setup to C01.
- `comparison.html`: C01/C02 front and rear side-by-side.
- `create_model.py`: deterministic generator; run `blender -b --python create_model.py -- /absolute/output`.
- `verify_exports.py`: independent FBX/GLB reimport checks. Exact final counts/dimensions in model-stats.json and export-validation.json.

Source axis: X right, Y forward, Z up, metres. FBX carries the axis conversion to Y-up. When selected for integration, import in an isolated Unity folder and extract/remap the ten materials to URP/Lit; enable emission on Amber_Taillight and Ice_Headlight. Source nodes and GLB preserve colour/metallic/roughness/emission values. No texture dependencies. No collider, LOD, gameplay wiring or physics adjustments. Rendering studio is excluded from exports.

This is a visual candidate outside Unity Assets. Export validation and render inspection do not claim Unity import, URP, gameplay performance or human visual acceptance. Runtime still uses STRIDE. Original C01 delivery: commit 29e39f104d0d5277d55a72e5bd489d973d475082.
