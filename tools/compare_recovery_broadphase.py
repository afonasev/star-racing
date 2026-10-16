#!/usr/bin/env python3
"""Compare actual SAP/ABP traces without replacing the Unity simulation."""
import argparse
import json
from pathlib import Path
from analyze_recovery_profile import stats


def compare(a, b):
    checks = []
    results = {}
    for theme, (baseline, candidate) in (("cloud-city", (a[0], b[0])),
                                          ("space-station", (a[1], b[1]))):
        for field in ('theme', 'source', 'entrants', 'substeps', 'colliders', 'supportColliders', 'expectedTicks'):
            if baseline[field] != candidate[field]:
                raise ValueError('Mismatched ' + theme + '/' + field)
        if baseline['broadphase'] != 0 or candidate['broadphase'] != 2:
            raise ValueError('Expected SAP=0 and ABP=2')
        if candidate['broadphaseName'].replace(' ', '') != 'AutomaticBoxPruning':
            raise ValueError('ABP enum identity missing')
        if len(baseline['samples']) != 2700 or len(candidate['samples']) != 2700:
            raise ValueError('Incomplete measurement')
        ordinary = []
        for report in (baseline, candidate):
            ordinary.append(stats([r['physicsMs'] for r in report['samples']
                                   if r['scenario'] == 'drive' and not r['fallingBefore']
                                   and not r['falls'] and not r['respawns']]))
        ordinary_ratio = ordinary[1]['p95'] / ordinary[0]['p95']
        checks.append(dict(theme=theme, criterion='ordinary p95 <= 110%', ratio=ordinary_ratio,
                           passed=ordinary_ratio <= 1.1))
        cases = []
        for repeat in range(3):
            for scenario in ('edge', 'remote'):
                rows = [[r for r in report['samples']
                         if r['scenario'] == scenario and r['repeat'] == repeat]
                        for report in (baseline, candidate)]
                counts = [(sum(r['falls'] for r in rs), sum(r['respawns'] for r in rs)) for rs in rows]
                if counts != [(32, 32), (32, 32)]:
                    raise ValueError('Different mass recovery behavior: ' + str(counts))
                respawns = [[r for r in rs if r['respawns']] for rs in rows]
                if len(respawns[0]) != len(respawns[1]):
                    raise ValueError('Different respawn episode distribution')
                for event_a, event_b in zip(*respawns):
                    ratio = event_b['physicsMs'] / event_a['physicsMs']
                    checks.append(dict(theme=theme, scenario=scenario, repeat=repeat,
                                       criterion='each respawn <= 50%', ratio=ratio, passed=ratio <= .5))
                    windows = [[r['physicsMs'] for r in rs if abs(r['tick'] - event['tick']) <= 10]
                               for rs, event in zip(rows, (event_a, event_b))]
                    peak_ratio = max(windows[1]) / max(windows[0])
                    sum_ratio = sum(windows[1]) / sum(windows[0])
                    checks.append(dict(theme=theme, scenario=scenario, repeat=repeat,
                                       criterion='neighbor window peak/sum <= 110%',
                                       peakRatio=peak_ratio, sumRatio=sum_ratio,
                                       passed=peak_ratio <= 1.1 and sum_ratio <= 1.1))
                    cases.append(dict(scenario=scenario, repeat=repeat,
                                      baselineRespawnMs=event_a['physicsMs'], candidateRespawnMs=event_b['physicsMs'],
                                      ratio=ratio, windowPeakRatio=peak_ratio, windowSumRatio=sum_ratio))
        stages = {}
        for scenario in ('drive', 'edge', 'remote'):
            stages[scenario] = [stats([r['physicsMs'] for r in report['samples']
                                      if r['scenario'] == scenario]) for report in (baseline, candidate)]
        falling = [stats([r['physicsMs'] for r in report['samples'] if r['fallingBefore'] and not r['respawns']])
                   for report in (baseline, candidate)]
        results[theme] = dict(ordinaryPhysicsMs=ordinary, ordinaryP95Ratio=ordinary_ratio,
                              respawns=cases, scenarioPhysicsMs=stages, fallingPhysicsMs=falling,
                              setupMs=[baseline['setupMs'], candidate['setupMs']],
                              warmupMs=[baseline['warmupMs'], candidate['warmupMs']],
                              colliders=baseline['colliders'], supportColliders=baseline['supportColliders'])
    return dict(success=all(c['passed'] for c in checks), checks=checks, results=results,
                limits='Editor forced-departure CPU ticks, not Player FPS. Physics correctness requires affected fixtures.')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('directory', type=Path)
    p.add_argument('output', type=Path)
    args = p.parse_args()
    def read(name):
        return json.loads((args.directory / name / 'samples.json').read_text())
    report = compare([read('sap-cloud-complete'), read('sap-space')],
                     [read('abp-cloud'), read('abp-space')])
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    for theme, r in report['results'].items():
        print(theme, 'ordinary p95 ratio=', round(r['ordinaryP95Ratio'], 3),
              'respawn ratios=', [round(c['ratio'], 3) for c in r['respawns']])
    print('Performance criteria passed:', report['success'])


if __name__ == '__main__':
    main()
