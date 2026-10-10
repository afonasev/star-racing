# HUD concepts — 2026-10-09

Exploration requested by the user: improve readability, reduce occupied space and give the HUD a finished game appearance. These are editable HTML/CSS design studies, not an implemented Unity feature. Open `index.html` and choose 1–4 players, 8/16/32/64 entrants and either track background.

- Cloudline: light compact card, vertical nitro, grouped standing and speed. Closest to the established menu style.
- Apex: dark sports typography, strong standing/speed hierarchy, horizontal nitro. Recommended direction for visual clarity.
- Edge: selected for refinement by the user. The speed instrument is now 108×54 px instead of 171×95 (64% less area), speed digits are 28 instead of 46 px, and human progress markers are 14 instead of 21 px. Player labels and the timer are removed. Standing is separately placed in each viewport’s upper inner corner (upper right in left viewports, upper left in right viewports); solo uses upper right. Thin horizontal nitro and a fading local backdrop remain.

All keep the center of each viewport open, instruments at the outer corners, one common bottom progress rail, an individual marker for every entrant and numbered human markers. Edge has separate small standing indicators; the two earlier alternatives retain grouped standing and instruments. AI dots have lower contrast; individual AI colors remain available. No minimap, leaderboard or new gameplay data was added. The 3-player arrangement preserves the unused fourth camera cell.

## Proposed changes, awaiting choice

Canonical baseline: `unity-racing-prototype/spec.md`, requirement `Визуальное представление` in the shared planning repository. Cloudline and Apex propose grouping standing with instruments instead of displaying standing at the opposite vertical edge. The refined Edge follows the user’s request to move standing to the upper inner corner of each viewport, remove timer/name labels and shrink all HUD elements. Cloudline replaces the decorative speed arc with typography and a denser contrast backing. Apex and Edge additionally propose dark backing and horizontal nitro. These are design options, not approved changes to the specification. Implementation should follow the selected direction in a standard flow change and verify HUD lifecycle and actual movement readability.

## Background provenance and limits

`source-cloud.png` and `source-space.png` are copies of retained Unity QA captures from `star-racing-planning/evidence/refine-race-hud/integrated-final/race-{1,4}-64-fixture-progress.png`, produced before this review. They were read and visually inspected in this task. SVG viewBoxes crop the original HUD out for an unobstructed mockup; this changes framing and is not evidence of a new game camera configuration. Values and marker positions are illustrative. No Unity Editor, Player build or deployment was launched. Contrast observations are visual judgments from static mockups; movement, controller/TV and human acceptance remain unverified.

## Reproduction and verification

Run `node render.cjs` with Playwright available in `NODE_PATH`. The script renders the six retained PNGs and validates 288 combinations: three concepts × four local-seat layouts × four rosters × two track backgrounds × three browser widths (1280/1440/1920). Checks cover seat count, instrument bounds, exactly one common rail, total entrant markers, human marker count and absence of page errors. All passed on 2026-10-09. The six PNGs were visually inspected.

## Preservation and cleanup

Retain the HTML, rendering script, both source backgrounds and six previews together for design selection and reproducibility. They are saved on the concept-review branch; gameplay source is unchanged. No task-owned Unity instances, servers, builds or temporary exports remain. The attached worktree remains available for the user's choice; no implementation, merge or publication is claimed.

## Refinement verification

Re-rendered previews and repeated all 288 combinations after the requested Edge correction. Viewed the revised four-player space and solo cloud previews. The interactive source is updated; automatic refresh of the existing file:// browser tab was rejected by the browser URL policy, so the user can refresh it manually. No additional server or browser workaround was created. Changes are limited to the concept files; Unity gameplay is unchanged.

Right-view Edge instruments mirror the left-view treatment: the player-colored accent is on the right outer edge, with the backdrop gradient fading inward. Other element positions and the solo instrument are unchanged.
