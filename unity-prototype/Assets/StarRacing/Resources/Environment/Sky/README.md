# Track sky probe assets

Source PNGs generated with built-in imagegen on 2026-10-08 for add-track-skyboxes.
The original two source files are 1254×1254 and retained verbatim. PlanetSky has alpha;
CloudSea is opaque. SkyTextureImporter owns the import policy separately from
existing environment surface textures. Planet clamps; cloud sampling mirrors at
edges to keep value continuity. Shader mip sampling and distance haze suppress
far-detail aliasing. Imported output is capped at 2048, with mipmaps/compression.

Planet prompt: isolated photoreal terrestrial globe, pale mineral continents,
navy oceans, cloud bands, fine atmospheric rim, upper-left lighting, dark right
hemisphere, transparent exterior. No rings or stars painted into the asset.

Cloud prompt: overhead dense white stratocumulus, cool valleys, sunny tops,
no horizon/land/objects. Repeated at two scales in shared world-XZ coordinates.

The sky ray/plane intersection and local render-only cloud deck share
CloudSampling.cginc. Cloud height is 215 m below the minimum route elevation.
No sky resource modifies route geometry or colliders. Build creates resources;
activation is deferred until RaceDirector publishes the prepared race.

Initial city/ocean-world Player appearance approved 2026-10-08.
No performance improvement claimed.

## Planet variants (2026-10-08)

Built-in imagegen produced PlanetGas.png, PlanetRock.png and PlanetIce.png using
PlanetSky.png as a lighting/framing reference. Sources copied verbatim with alpha.
The common prompt requested a square photorealistic astronomical globe occupying
80% of the frame, light from upper left, opaque dark right hemisphere, transparent
exterior with a thin atmospheric rim; no stars, rings, text or extra moons.

- Gas: amber/cream gas giant, fine turbulent cloud bands, immense oval storm and wisps.
- Rock: rusty copper world, impact basins, branching canyons, basalt plains, dusty rim.
- Ice: pale cyan world, fractured ice plates, frost fields, deep blue fissures, cool rim.

EnvironmentPlan selects `(seed % 4 + 3) % 4` from the requested UI seed,
independent of geometry-generator retries: seeds 77/78/79/80 map to
ocean/gas/rock/ice. No mutable session counter: restarting a seed preserves its sky.
All planets use the same render-only sky shader and import policy.

### Exact prompt set

Gas: Use case: stylized-concept. Production photorealistic space racing skybox planet decal. Reference image supplies framing, realistic detail and lighting only. Create an amber and cream gas giant with fine turbulent horizontal cloud bands, one immense oval storm, intricate wisps, no solid continents. Square composition, one complete centered spherical globe occupying 80% of frame. Match reference light from upper left with shadowed right hemisphere. Preserve opaque dark night hemisphere, fully transparent exterior with real alpha, delicate atmospheric rim only. Detailed realistic astronomical photography aesthetic. No stars, background, rings, text, extra moons or labels.

Rock: Use case: stylized-concept. Production photorealistic space racing skybox planet decal. Reference image supplies framing, realistic detail and lighting only. Create a rusty copper rocky planet with vast ancient impact basins, deeply branching canyons, darker basalt plains and subtle dusty atmosphere, no oceans. Square composition, one complete centered spherical globe occupying 80% of frame. Match reference light from upper left with shadowed right hemisphere. Preserve opaque dark night hemisphere, fully transparent exterior with real alpha, delicate atmospheric rim only. Detailed realistic astronomical photography aesthetic. No stars, background, rings, text, extra moons or labels.

Ice: Use case: stylized-concept. Production photorealistic space racing skybox planet decal. Reference image supplies framing, realistic detail and lighting only. Create a pale cyan icy planet with fractured ice plates, sweeping frost fields, subtle deep blue fissures and a thin cool atmospheric rim, no earthlike continents. Square composition, one complete centered spherical globe occupying 80% of frame. Match reference light from upper left with shadowed right hemisphere. Preserve opaque dark night hemisphere, fully transparent exterior with real alpha, delicate atmospheric rim only. Detailed realistic astronomical photography aesthetic. No stars, background, rings, text, extra moons or labels.
