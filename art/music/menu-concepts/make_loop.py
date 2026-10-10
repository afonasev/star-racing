"""Overlap the selected WAV's tail and head; retain the approved musical material."""
import argparse
import json
import wave
from pathlib import Path
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument('source', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--gain-db', type=float, default=0, help='Constant gain preserves the circular sample boundary')
args = parser.parse_args()
with wave.open(str(args.source), 'rb') as wav:
    rate, channels = wav.getframerate(), wav.getnchannels()
    if wav.getsampwidth() != 2:
        raise ValueError('Expected 16-bit PCM')
    pcm = np.frombuffer(wav.readframes(wav.getnframes()), dtype='<i2').reshape(-1, channels).astype(np.float64) / 32768
fade = round(2 * rate)
if len(pcm) <= fade * 2:
    raise ValueError('Source must exceed four seconds')
# Complementary sine-squared weights have zero slope at both joins.
weight = np.sin(np.linspace(0, np.pi / 2, fade)) ** 2
join = pcm[-fade:] * (1 - weight[:, None]) + pcm[:fade] * weight[:, None]
loop = np.concatenate((pcm[fade:-fade], join))
loop *= 10 ** (args.gain_db / 20)
peak = float(np.abs(loop).max())
if peak >= 1:
    raise ValueError('Crossfade clips')
args.output.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(args.output), 'wb') as wav:
    wav.setparams((channels, 2, rate, len(loop), 'NONE', 'not compressed'))
    wav.writeframes(np.round(loop * 32767).astype('<i2').tobytes())
print(json.dumps({'seconds': len(loop) / rate, 'sample_rate': rate, 'channels': channels,
                  'gain_db': args.gain_db, 'peak': peak, 'seam_delta': float(np.abs(loop[0] - loop[-1]).max()),
                  'adjacent_max_delta': float(np.abs(np.diff(loop, axis=0)).max())}, indent=2))
