# Exhaust readability concepts

Offline Blender previews of the existing Cloudline C05 model. These are shape/color studies, not Unity gameplay screenshots or runtime acceptance. Deterministic noise W=1.8, identical camera and silver paint; sustained gas and nitro states.

1. Dense: gas length 1.05 m, radius 0.19 m; nitro length 2.8 m, radius 0.2375 m.
2. Wide arcade: gas length 1.3 m, radius 0.28 m; nitro length 3.5 m, radius 0.35 m.

Gas is warm orange, nitro cyan/blue, both have pale hot cores. Existing runtime has gas up to ~0.55 m / width 0.15 m and nitro up to ~1.8 m / width 0.21 m; previews intentionally exaggerate the silhouette. Actual shader response and screen readability must be checked in Unity after selecting a direction, including split screen and release fade.

Reproduce from repository root: `blender -b --python art/exhaust-concepts/render.py`; run `compose.py` with a Python environment containing Pillow. `comparison.png` combines the four states.

No game code changed, no Unity Editor/Player started, no Player build or deployment. Human direction selection is pending.

## Selected implementation

User selected variant 2 on 2026-10-08. Applied as a small direct tuning adjustment: orange throttle/cyan active nitro, width 0.56/0.70 and length caps 1.30/3.50 in model-local coordinates (vehicle fitting scale still applies). Shader has a denser broad body, pale core and reduced brightness pulsation, with no bloom dependency. Release, reset, boost gating and propulsion rules are unchanged; canonical `cloudline-runtime-vehicle` dynamic-exhaust requirements still apply without a semantic change.

Feature QA: full Editor suite, fixture equivalence, existing countdown/pause/reset/resource regressions, revised length bounds and actual plume width checks. `CloudlineVehicleChecks.RenderReadability` saves actual offscreen Unity shader captures at 960x540 and 480x270 for gas and nitro; requires `STAR_RACING_EXHAUST_PROOF` and a graphics-enabled Editor through the exclusive Unity wrapper. The fixture is visual evidence, not race-camera/device/human acceptance.

No Player/release/installer build or deployment: user policy dated 2026-10-08 takes priority over historical native-build gates in tools/qa.py. Native Player, physical devices and human visual acceptance remain unverified. Evidence: configured planning root `evidence/strengthen-exhaust/`.

## Compact detailed revision

User feedback on 2026-10-08: reduce the effect and increase detail. Gas/nitro length caps now 1.0/2.6, widths 0.36/0.46 before fitting scale: about 25% shorter and 35% narrower than variant 2. The shader adds a narrow pale core, colored mantle, small animated edge filaments and stationary shock cells for active nitro only. Three existing crossed sheets are retained; no extra particle systems, textures or lights. Gas/nitro input gating and fade timing are unchanged. The Editor proof also saves 1280x720 close views; evidence in `evidence/refine-exhaust-detail/`. Player/device/human visual checks and builds/deployment remain deferred.
