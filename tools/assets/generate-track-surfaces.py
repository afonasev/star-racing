#!/usr/bin/env python3
"""Author clean, seamless sci-fi patterns and their cyclic 20 m blend atlases.

No external dependencies. Source assets stay separate for future art replacement.
Road axes: 32 m lateral x 8 m longitudinal. Rail: 24 m longitudinal x 1.3 m high.
Atlases repeat every 288 m with three 96 m sections; alpha is not used.
"""
import math
import struct
import zlib
from pathlib import Path

OUT = Path(__file__).resolve().parents[2] / 'unity-prototype/Assets/StarRacing/Resources/Environment/Textures'
CYCLE, SECTION, BLEND = 288.0, 96.0, 20.0


def seam(value, period, width):
    return min(value % period, period - value % period) < width


def road(kind, x, distance):
    y = distance % 8
    grain = .008 * math.sin(x * 39.27) * math.sin(y * 12.56637)
    if kind == 0:  # Quiet asphalt-like ceramic: very fine, low contrast.
        color = (.31, .35, .40)
        detail = -.025 if seam(y, 8, .10) else 0
    elif kind == 1:  # Broad staggered tiles, almost white compared with ceramic.
        color = (.52, .55, .56)
        shifted = x + (2 if y >= 4 else 0)
        detail = -.075 if seam(y, 4, .13) or seam(shifted, 4, .09) else 0
    else:  # Dark longitudinal ribs and sparse cross joints.
        color = (.24, .29, .34)
        detail = -.055 if seam(x, 2, .14) else .02
        if seam(y, 8, .13): detail -= .045
    return tuple(c + detail + grain for c in color)


def rail(kind, distance, height):
    x, y = distance % 24, height / 1.3
    # Wall-specific fabricated metal: no road tiles, asphalt grain or longitudinal lanes.
    if y > .86: return (.65, .83, .85)
    if y < .13: return (.12, .18, .22)
    if kind == 0:  # Brushed extrusion: continuous horizontal channels and narrow end clamps.
        color = (.39, .45, .47)
        if seam(x, 6, .18): return (.20, .25, .28)
        if seam(y - .2, .2, .025): return (.23, .29, .32)
        return color
    if kind == 1:  # Recessed service cassettes, with dark ventilation slots.
        color = (.55, .58, .59)
        panel = x % 8
        if .8 < panel < 6.7 and .30 < y < .72:
            if seam(y - .35, .12, .028): return (.13, .19, .23)
            return (.25, .32, .35)
        if 7.2 < panel < 7.7 and .2 < y < .78: return (.72, .48, .20)
        return color
    # Dark reinforced housings with broad diagonal silver braces and amber fastening clips.
    color = (.22, .28, .31)
    panel = x % 6
    brace = .7 + (y - .15) * 5.8
    if abs(panel - brace) < .28: return (.56, .61, .62)
    if panel < .3 and .2 < y < .78: return (.72, .48, .20)
    return color


def blend(pattern, distance, other, is_rail=False):
    section = int(distance // SECTION) % 3
    local = distance % SECTION
    # Centred on boundaries, including the repeat seam at 0/288 m.
    if local < BLEND / 2:
        a, b, t = (section - 1) % 3, section, (local + BLEND / 2) / BLEND
    elif local > SECTION - BLEND / 2:
        a, b, t = section, (section + 1) % 3, (local - SECTION + BLEND / 2) / BLEND
    else:
        return pattern(section, distance, other) if is_rail else pattern(section, other, distance)
    t = t * t * (3 - 2 * t)
    ca = pattern(a, distance, other) if is_rail else pattern(a, other, distance)
    cb = pattern(b, distance, other) if is_rail else pattern(b, other, distance)
    return tuple(u * (1 - t) + v * t for u, v in zip(ca, cb))


def png(name, width, height, pixel):
    def chunk(tag, data):
        return struct.pack('!I', len(data)) + tag + data + struct.pack('!I', zlib.crc32(tag + data))
    raw = bytearray()
    for row in range(height):
        raw.append(0)
        for col in range(width):
            # PNG is top-down; Unity texture coordinates are bottom-up.
            rgb = pixel((col + .5) / width, 1 - (row + .5) / height)
            raw.extend(round(max(0, min(1, c)) * 255) for c in rgb)
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', width, height, 8, 2, 0, 0, 0))
    data += chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    (OUT / (name + '.png')).write_bytes(data)


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    for kind, name in enumerate(('Ceramic', 'Panels', 'Ribbed')):
        png('Road' + name, 512, 128, lambda u, v, k=kind: road(k, u * 32, v * 8))
        png('Rail' + name, 512, 128, lambda u, v, k=kind: rail(k, u * 24, v * 1.3))
    png('RoadBlend', 1024, 8192, lambda u, v: blend(road, v * CYCLE, u * 32))
    # A quarter-cycle offset and reversed variant order decouple wall changes from the floor.
    png('RailBlend', 8192, 256, lambda u, v: blend(
        lambda k, d, h: rail(2 - k, d, h), u * CYCLE + 72, v * 1.3, True))
    # The repeat boundary itself must match exactly and use both adjacent variants.
    for pattern, side, is_rail in ((road, 3.3, False), (rail, .7, True)):
        assert blend(pattern, 0, side, is_rail) == blend(pattern, CYCLE, side, is_rail)
    print('TRACK_SURFACE_ASSETS_OK variants=3 road=1024x8192 rail=8192x256 cycle=288m blend=20m')
