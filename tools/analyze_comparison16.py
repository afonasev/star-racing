#!/usr/bin/env python3
"""Validate one bounded comparison16 capture before admitting the next arm."""
import argparse
import json
import math
import statistics
from pathlib import Path


def percentile(values, p):
    values = sorted(values)
    return values[max(0, math.ceil(len(values) * p) - 1)]


def analyze(directory):
    directory = Path(directory)
    meta = json.loads((directory / 'comparison.json').read_text())
    report = json.loads((directory / 'frames/frames.json').read_text())
    assert meta['completed'] and not meta.get('failure'), meta.get('failure')
    assert report['completed'] and not report.get('failure'), report.get('failure')
    assert meta['entrants'] == report['entrants'] == 16
    assert (meta['width'], meta['height']) == (report['width'], report['height']) == (1920, 1200)
    assert not report['editor'] and report['cpuEnabled']
    assert report['fixedDeltaTime'] == .01 or abs(report['fixedDeltaTime'] - .01) < 1e-8
    assert report['baseSubsteps'] == 8 and report['captureDeltaTime'] == 0
    assert report['droppedFrames'] == 0
    frames = [f for f in report['frames'] if f['steady']]
    assert len(frames) > 2 and report['measuredSimulatedSeconds'] >= 60
    assert all(f['focused'] and not f['paused'] and f['timeScale'] == 1 and f['captureDeltaTime'] == 0 for f in frames), 'Focus/pause/clock state changed'
    assert all(f['nativeDeltaAvailable'] and f['nativeDelta'] == f['cpu']['nativeCalls'] for f in frames), 'Native accounting mismatch'
    assert all(f['cpu']['baseSlices'] == 8 * f['cpu']['completedTicks'] for f in frames), 'Base slice contract'
    assert all(f['cpu']['nativeCalls'] == sum(f['cpu'][k] for k in ('baseSlices', 'angularSlices', 'pairSlices')) for f in frames), 'Adaptive accounting'
    assert all(f['cpu']['vehicleCalls'] == 16 * f['cpu']['completedTicks'] for f in frames), 'Vehicle tick accounting'
    freq = report['stopwatchFrequency']
    deltas = [f['deltaMs'] for f in frames]
    sim = sum(f['simulationDeltaSeconds'] for f in frames)
    wall = sum(deltas) / 1000
    unscaled = sum(f['unscaledDeltaMs'] for f in frames) / 1000
    assert abs(unscaled - wall) <= max(.1, wall * .05), 'Unscaled/real clock mismatch'
    # Compare the same interval: the observer Stopwatch and Unity realtime have
    # different epochs (cold startup can precede observer installation by seconds).
    # These files bracket Begin/Finish; JSON serialization is inside the OS interval.
    os_window = (directory / 'frames/frames.json').stat().st_mtime - (directory / 'frames/probe-started.json').stat().st_mtime
    unity_window = report['frames'][-1]['realSeconds'] - report['frames'][0]['realSeconds']
    assert abs(os_window - unity_window) <= max(.1, os_window * .05), 'OS/realtime elapsed interval mismatch'
    ticks = sum(f['cpu']['completedTicks'] for f in frames)
    calls = sum(f['cpu']['nativeCalls'] for f in frames)
    assert abs(ticks * .01 - sim) < .001, '100Hz simulation/tick accounting'
    def cpu_ms(section, selected=frames):
        return sum(f['cpu'][section] for f in selected) * 1000 / freq
    severe = [f for f in frames if f['deltaMs'] >= 80]
    result = {
        'directory': str(directory.resolve()), 'valid': True, 'version': meta['version'], 'theme': meta['theme'],
        'config': json.loads(meta['config']), 'settings': json.loads(meta['settings']),
        'geometryIdentity': meta['geometryIdentity'], 'replayIdentity': meta['replayIdentity'],
        'colliders': meta['colliders'], 'width': meta['width'], 'height': meta['height'],
        'vSync': meta['vSync'], 'frameLimit': meta['frameLimit'], 'basisEnabled': meta['basisEnabled'],
        'frames': len(frames), 'focused': len(frames), 'wallSeconds': wall, 'simSeconds': sim,
        'wallPerSim': wall / sim, 'meanMs': statistics.mean(deltas), 'medianMs': statistics.median(deltas),
        'p95Ms': percentile(deltas, .95), 'p99Ms': percentile(deltas, .99), 'maxMs': max(deltas),
        'aggregateFps': len(frames) / wall, 'unscaledToRealRatio': unscaled / wall,
        'osObservationSeconds': os_window, 'unityObservationSeconds': unity_window,
        'severeFrames': len(severe), 'severePerSimSecond': len(severe) / sim,
        'ticks': ticks, 'ticksPerFrame': ticks / len(frames), 'nativeCalls': calls, 'nativeCallsPerTick': calls / ticks,
        'baseSlices': sum(f['cpu']['baseSlices'] for f in frames),
        'angularSlices': sum(f['cpu']['angularSlices'] for f in frames),
        'pairSlices': sum(f['cpu']['pairSlices'] for f in frames),
        'nativeMsPerCall': cpu_ms('native') / calls,
        'cpuMsPerSimSecond': {k: cpu_ms(k) / sim for k in ('director', 'vehicle', 'stepper', 'setup', 'refine', 'snapshot', 'pairs', 'forces', 'native', 'presentation', 'cleanup')},
        'severeMeanMs': statistics.mean(f['deltaMs'] for f in severe) if severe else None,
        'severeCpuMsPerFrame': {k: cpu_ms(k, severe) / len(severe) for k in ('director', 'vehicle', 'stepper', 'native')} if severe else None,
        'gcFrames': sum(any(f[k] for k in ('gc0', 'gc1', 'gc2')) for f in frames),
        'gpuAvailable': report['gpuAvailable'],
        'gpuMsMedian': statistics.median(f['gpuMs'] for f in frames if f['gpuAvailable']) if any(f['gpuAvailable'] for f in frames) else None,
        'limitation': meta['limitation'] + ' CPU native is INCLUDED in stepper. Missing GPU is unavailable. Theme difference is not a pure rendering intervention.',
    }
    (directory / 'summary.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory')
    args = parser.parse_args()
    result = analyze(args.directory)
    print(json.dumps({k: result[k] for k in ('valid', 'theme', 'frames', 'meanMs', 'p95Ms', 'p99Ms', 'severeFrames', 'nativeCallsPerTick', 'cpuMsPerSimSecond', 'gpuAvailable')}, ensure_ascii=False))
