#!/usr/bin/env python3
"""Summarize measured Unity samples; no simulation or replacement physics."""
import argparse
import json
import math
from collections import defaultdict
from pathlib import Path


def stats(values):
    values = sorted(values)
    if not values:
        return None

    def percentile(q):
        at = (len(values) - 1) * q
        low = math.floor(at)
        high = math.ceil(at)
        return values[low] + (values[high] - values[low]) * (at - low)

    return dict(n=len(values), mean=sum(values) / len(values),
                median=percentile(.5), p95=percentile(.95),
                p99=percentile(.99), maximum=values[-1])


def summarize(rows):
    fields = ('projectionMs', 'vehicleMs', 'physicsMs',
              'renderSubmitMs', 'readbackMs', 'allocatedBytes')
    result = {field: stats([row[field] for row in rows if row[field] >= 0])
              for field in fields}
    result['cpuTickMs'] = stats([sum(row[f] for f in fields[:3]) for row in rows])
    result['falls'] = sum(row['falls'] for row in rows)
    result['respawns'] = sum(row['respawns'] for row in rows)
    result['contacts'] = sum(row['contacts'] for row in rows)
    result['supportContacts'] = sum(row['supportContacts'] for row in rows)
    result['maxFallingCars'] = max(row['fallingAfter'] for row in rows)
    if all('substepMs' in row for row in rows):
        result['substepsMs'] = [stats([row['substepMs'][i] for row in rows]) for i in range(8)]
    result['topCpuTicks'] = sorted(
        rows, key=lambda row: sum(row[f] for f in fields[:3]), reverse=True)[:5]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('samples', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    report = json.loads(args.samples.read_text())
    rows = report.pop('samples')
    if len(rows) != report['expectedTicks']:
        raise ValueError('Incomplete profile')
    groups = defaultdict(list)
    repeats = defaultdict(list)
    phases = defaultdict(list)
    for row in rows:
        for field in ('projectionMs', 'vehicleMs', 'physicsMs'):
            if not math.isfinite(row[field]) or row[field] < 0:
                raise ValueError('Invalid stage timing')
        groups[row['scenario']].append(row)
        repeats[f"{row['scenario']}/{row['repeat']}"].append(row)
        phase = ('respawn' if row['respawns'] else 'fall-entry' if row['falls']
                 else 'falling' if row['fallingBefore'] else 'on-road-or-airborne')
        phases[f"{row['scenario']}/{phase}"].append(row)
    result = dict(metadata=report, scenarios={k: summarize(v) for k, v in groups.items()},
                  repeats={k: summarize(v) for k, v in repeats.items()},
                  phases={k: summarize(v) for k, v in phases.items()})
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    for name, group in result['scenarios'].items():
        print(name, 'CPU tick p95:', round(group['cpuTickMs']['p95'], 3),
              'physics p95:', round(group['physicsMs']['p95'], 3),
              'falls:', group['falls'], 'respawns:', group['respawns'])


if __name__ == '__main__':
    main()
