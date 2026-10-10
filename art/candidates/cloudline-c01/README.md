# Cloudline C01 — visual candidate

An actual editable 3D interpretation of the white futuristic car in the Cloudline menu image. **Awaiting visual selection; not integrated into the playable vehicle.** The reference only establishes the rear three-quarter view; the front is an authored extrapolation.

- `Cloudline_C01.blend`: editable mesh objects, non-destructive bevels, PBR materials and review studio.
- `Cloudline_C01.fbx`: model only, four independent wheel pivots; standard Unity exchange asset.
- `Cloudline_C01.glb`: self-contained model and PBR materials for portable preview.
- Four PNGs: rear three-quarter, front three-quarter, side and rear. These are renders of the delivered geometry, not generated concept art.
- `create_model.py`: deterministic source; run `blender -b --python create_model.py -- /absolute/output`.
- `verify_exports.py`: independent FBX/GLB reimport and geometry/material/bounds/wheel-position checks.

## Unity handoff (after visual selection)

Import FBX into an isolated candidate folder, scale factor 1, metre units. Source coordinates are X right, Y forward, Z up; FBX includes axis conversion to Y-up. Confirm nose direction in Unity before placement. Extract the ten named materials and assign URP/Lit: Pearl_White, Satin_Titanium, Graphite_Aero, Midnight_Glass, Tire_Rubber, Forged_Aluminium, Brake_Ceramic, Amber_Taillight, Ice_Headlight, Copper_Caliper. Enable emission for the two light materials. Glass is opaque dark reflective glass, not a transparent interior. The `.blend` nodes preserve exact base colour, metallic, roughness and emission values; GLB also preserves these PBR properties.

The studio is excluded from both exports. Source dimensions are 2.414 m wide, 4.952 m long, 1.476 m high. Do not replace VehicleGeometry or rescale physics automatically. This package has no colliders, driving code, LODs or gameplay wiring. Wheel pivots are prepared for subsequent rigging. Meshes are intentionally separate for authoring; consolidate by material and preserve wheels during production optimization. No external textures or texture dependencies.

## Verification boundary

FBX and GLB are reimported in Blender and checked for finite nonempty geometry, dimensions, four correctly placed wheels, ten materials and no studio cameras/lights. PNGs are visually inspected. Unity import, URP appearance, native Player performance and human visual acceptance are not claimed by those checks. This offline package lives outside `unity-prototype/Assets`, so existing STRIDE rendering and race builds are unchanged.
