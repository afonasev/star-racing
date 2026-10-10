# C04 — unified cabin on preserved C03 body

F3 user feedback: main C03 contours are good, cabin looks fragmented and unlike the orbital reference. C04 preserves C01/C02/C03, rebuilds the cabin only, and remains outside Unity Assets.

Cabin glazing uses one continuous analytic surface. Silver roof and thin cross-frames follow the same surface; lower beltline sits flush. No inflated roof spine or tubular canopy border. Two thin rear cabin panels replace the tubular rear supports. Dark glass material adjusted for clean reflections. Primary body panels, lights, wheels, nose and diffuser are retained from C03.

- `Cloudline_C04.blend`: editable source including studio.
- `Cloudline_C04.fbx` / `.glb`: geometry and ten material slots/PBR materials; four wheel pivots.
- Four full views plus `cabin-detail.png`, all actual model renders.
- `comparison.html`: C03/C04 at matching cameras plus C04 cabin detail and original reference.
- `verify_preservation.py`: compares evaluated geometry/transform hashes of every unaffected mesh against C03; 152 meshes unchanged.
- `verify_exports.py`: independent FBX/GLB reimport, finite geometry, bounds, wheels, ten materials, no studio.
- `create_model.py`: reproduce with `blender -b --python create_model.py -- /absolute/output`.

Original user reference remains `../cloudline-c03/reference-orbital.png`. Earlier models are preserved in adjacent directories. Source axes X right / Y forward / Z up; metres. FBX includes axis conversion to Y-up. For Unity integration after selection, extract/remap materials to URP/Lit and enable Cyan_Energy / Thin_Red_Taillight emission; check orientation and scale. No Unity import, native Player, performance or human visual acceptance is claimed here. No colliders, LODs or gameplay wiring. Existing STRIDE runtime is unchanged by this art package.
