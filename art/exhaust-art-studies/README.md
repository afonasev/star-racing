# Detailed exhaust art alternatives

User feedback (2026-10-08): size and behavior are acceptable; current effects look simplistic and visually out of place. Rework only flame rendering. These are preview studies, not a production integration.

1. Natural combustion: layered curling wisps, feathered transparent borders, warm orange gas and icy blue nitro.
2. Plasma jet: cleaner central filament, braided flow, denser internal shock structure.

Both use the unchanged production envelope, anchors, model and local scale (gas/nitro caps 1.0/2.6, widths 0.36/0.46). The study renderer temporarily substitutes a material in an isolated Editor scene; it never writes the production shader, material, envelope or runtime. Render phase is fixed at 0.38 and the camera/paint/lighting are identical across the four samples. No acceptance of either art direction is inferred.

Assets: `1-natural.png`, `2-plasma.png`, transparent RGBA two-cell atlases generated with the built-in image_gen tool. Exact prompt set is `prompts.json`; alpha/dimension validation is `atlas-validation.json`. Left cell is gas, right cell nitro, root at UV Y=0. Source textures are kept unchanged. Shader UV warp is only a preview of motion, not final animation quality acceptance.

Reproduce with a graphics-enabled exclusive Editor via `tools/unity.sh exclusive -batchmode -quit -executeMethod StarRacingPrototype.ExhaustArtStudies.Render -logFile <absolute log>` and `STAR_RACING_EXHAUST_ART_PROOF=<absolute output directory>`. Editor-only source lives in `Assets/StarRacing/Editor/ExhaustStudies/`; final screenshots live in the configured planning root at `evidence/exhaust-art-studies/unity-render/`.

Pending: select art direction, integrate the chosen texture/rendering method, verify motion and race-camera readability. No Player/build/release/deployment; those need a separate explicit command under the 2026-10-08 user policy.

## Available offline previews / Unity admission blocker

Unity exclusive admission timed out after 600 seconds before any Editor process started. No Unity compilation or runtime render is claimed. Uncompiled Editor preview source is preserved under `unverified-unity-preview/`, outside the Unity import tree, for the selected direction's next check. The reproduction command above requires installing these files back into the Editor folder first.

For immediate art review, `render_blender.py` renders the original C05 source model with three crossed texture sheets using the current gas/nitro local widths and full-hold lengths (0.9881/2.5919). CPU Cycles, same camera and lighting, no geometry/model changes; `*-blender.png` are offline placement previews, not Unity screenshots. Motion, race camera, cross-plane artifacts and final shader performance remain open after art selection. Shader/material/envelope in production are byte-for-byte unchanged.

## Selected natural flame integration

User selected direction 1. The unchanged `1-natural.png` is copied into `Resources/Vehicle/CloudlineFlameAtlas.png`; the shared material references this atlas. Shipping shader uses soft alpha composition and blends its warm/blue cells from the existing nitro envelope. Small animated UV deformation is anchored at both ends, with different phases per nozzle; no new particles, lights or geometry. Envelope lengths, widths, input gating and fade durations are unchanged.

QA scope: local exhaust rendering and phase/reset contract. `ExhaustAppearanceChecks.Run` runs the complete affected vehicle/exhaust fixture, enters actual Editor Play Mode, renders gas/nitro at 960x540, 480x270 and 1280x720, compares frames to prove visible motion, checks width/length invariance and immediate suppression, and checks the shipping shader. No HUD/AI/long-match suite: no dependencies on those systems changed. Evidence is in the configured planning root `evidence/integrate-natural-exhaust/`. Player/build/device/human acceptance and deployment remain separate.
