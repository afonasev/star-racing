# Cloudline C05 — attached lamps and visual studies

Preserves the full C04 cabin, body and wheels. Two red light strips now follow the rear bridge face in embedded dark housings. Two hollow metal exhaust outlets connect into the rear bay.

- `Cloudline_C05.blend`: editable silver car and studio.
- `Cloudline_C05.fbx` / `.glb`: silver car only; no studio or flame VFX.
- `Cloudline_C05_Studies.blend`: six paint presets are recorded in studies.json; preview-only procedural gas/nitro volumes are in their own collection, hidden by default. Toggle a complete gas or nitro group for preview.
- `colors-sheet.png`: six actual Blender renders with identical camera and lighting.
- `exhaust-sheet.png`: same silver body and camera for gas versus nitro. Proposed flame lengths 0.55 m and 1.80 m; not an implemented game mechanic or calibrated engine simulation.
- `rear-light-detail.png`: embedded lamp detail.

Reproduce using Blender 5.2.1: `create_model.py -- <absolute-output-dir>`, then `render_studies.py -- <absolute-output-dir>`. Both scripts run via `blender -b --python <script> -- <absolute-output-dir>`. Run `verify_exports.py` and `verify_preservation.py` the same way; compose sheets with Pillow using `make_sheets.py`.

C01–C04 remain unchanged. Reference: ../cloudline-c03/reference-orbital.png. Front is extrapolated from the supplied rear view. Offline art only: no Unity Assets, playable car, physics, release or deployment modifications. Renders do not establish URP appearance or runtime VFX/performance acceptance.
