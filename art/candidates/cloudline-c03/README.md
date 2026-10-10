# C03 — orbital silver/cyan car

New model interpretation of the actual user-supplied orbital-station reference, preserved verbatim as `reference-orbital.png`. This replaces the *reference for C03*, not either previous candidate. C01 and C02 remain untouched and recoverable in Git and their adjacent directories.

## Design

Lower inset canopy, wider track, continuous silver shoulder panels with real wheel openings, broad swept rear bridge, dark open diffuser, cyan rear-edge signatures and central cyan panel, fine red tail accents. These are authored meshes with materials, not an image-generation output. The reference only shows rear three-quarter; the front is a same-style reconstruction and requires separate visual judgement. No blue speed-trail effects are baked into the car geometry.

## Files

- `Cloudline_C03.blend`: editable geometry, modifiers, PBR materials and review studio.
- `Cloudline_C03.fbx`: Unity-compatible exchange geometry and material slots, four separate wheel pivots.
- `Cloudline_C03.glb`: self-contained PBR asset for portable inspection.
- Four PNG views of the exported source; deterministic authoring, no random seed.
- `comparison.html`: supplied reference and C03 rear/front previews.
- `create_model.py`: reproduce with `blender -b --python create_model.py -- /absolute/output`.
- `verify_exports.py`: independent FBX/GLB reimport with finite-geometry, bounds, wheel-centre and material-count assertions; no exported studio.

Source units: metres, X right / Y forward / Z up. FBX includes Y-up conversion. Source modifiers are editable; exchange exports bake them. Exact mesh/triangle counts and bounds are in model-stats.json and export-validation.json.

## Unity handoff boundary

After visual selection, import FBX into an isolated Unity candidate folder at scale 1, verify orientation and remap material slots to URP/Lit. `Cyan_Energy` and `Thin_Red_Taillight` require emission. The Blender nodes and GLB preserve all PBR values. Glass is opaque dark reflective glass. No texture dependencies, colliders, LODs or gameplay wiring. Production optimization and Unity/URP import are not yet tested; export reimport and Blender renders do not substitute for those gates. The delivered package is outside Unity Assets, and the STRIDE playable vehicle remains unchanged.

## Preserved alternatives

C01: ../cloudline-c01, commit 29e39f104d0d5277d55a72e5bd489d973d475082.
C02: ../cloudline-c02, commit b0d215fdcd929623343101f6801c2cbb76cc4506.
Both file manifests are checked unchanged during C03 delivery.
