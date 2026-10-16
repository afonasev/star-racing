import struct
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ApplicationIconContract(unittest.TestCase):
    def test_installer_png_matches_game_icon(self):
        game = ROOT / 'unity-prototype/Assets/StarRacing/Resources/Star-Racing-Icon.png'
        self.assertEqual((ROOT / 'distribution/icons/Star-Racing.png').read_bytes(),
                         game.read_bytes())

    def test_windows_icon_contains_complete_32_bit_bitmap_sizes(self):
        data = (ROOT / 'distribution/icons/Star-Racing.ico').read_bytes()
        reserved, kind, count = struct.unpack_from('<HHH', data)
        self.assertEqual((reserved, kind, count), (0, 1, 7))
        sizes = set()
        for index in range(count):
            width, height, _, _, planes, bits, length, offset = struct.unpack_from(
                '<BBBBHHII', data, 6 + index * 16)
            width, height = width or 256, height or 256
            sizes.add(width)
            self.assertEqual(width, height)
            # ICO directory planes may be unspecified; the DIB must declare one.
            self.assertIn(planes, (0, 1))
            self.assertEqual(bits, 32)
            self.assertGreaterEqual(offset, 6 + count * 16)
            self.assertLessEqual(offset + length, len(data))
            header, bitmap_width, bitmap_height = struct.unpack_from('<Iii', data, offset)
            self.assertEqual((header, bitmap_width, bitmap_height), (40, width, height * 2))
            self.assertEqual(struct.unpack_from('<HH', data, offset + 12), (1, 32))
        self.assertEqual(sizes, {16, 24, 32, 48, 64, 128, 256})
