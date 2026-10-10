# HUD design exploration — 2026-10-08

User selected the light Cloudline design and requested this mockup revision:
- One common progress rail floats over the race, centered at the bottom, 60% of
  the full screen width, with a transparent background and a 2 px line.
  The overlay is 26 px high. Viewports continue behind it to the screen bottom.
- Smaller speedometer with vertical nitro and a 40% opaque white background: 164 × 104 at a 1600 × 900 mock screen
  (previous instrument panel: 238 × 151).
- Position is separate, on the outer left/right side of each viewport, 12 px
  from the screen edge, vertically opposite the speedometer.
- Instruments retain their outer screen corners for 1–4 local players.
- Three-seat layout retains the existing empty fourth quadrant.

Open index.html to switch 1–4 local seats and 8/16/32/64 total entrants.
The road is a schematic illustration, not a Unity screenshot. Telemetry is synthetic;
no track seed applies. Mock positions determine ranking. Local-player circles are
larger, numbered and use the existing seat colors. Dense overlap remains an
implementation question; the rail must remain one, with accurate horizontal positions.

Previews: light-four.png; layouts.png (1, 2, 3, 4 seats, reading order).
Previous dark variant is preserved in Git history before this revision.
This is design exploration. Game code and canonical OpenSpec specs are unchanged.
