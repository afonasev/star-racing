"""Reviewed existing Editor entrypoints; never infer complete coverage from a name."""
import json
from pathlib import Path
import re

NAMESPACE = 'StarRacingPrototype.'
# Direct calls of PrototypeChecks.RunWithFixtureEquivalence, in execution order.
# Nested checks keep their own assertions; this is not the entire Editor directory.
AGGREGATE_ENTRYPOINTS = [NAMESPACE + name for name in (
    'CheckpointAdmissionChecks.Run', 'AirSteeringChecks.Run',
    'TrackProjectionParityChecks.Run', 'RoadVolumeChecks.Run',
    'RoadSmoothnessChecks.Run', 'GravityChecks.Run',
    'PrototypeChecks.RunIdleRecovery', 'PrototypeChecks.Run',
    'RosterFixtureEquivalenceChecks.Run', 'CloudlineChecks.Run',
    'CloudlineVehicleChecks.Run', 'Sn30ProChecks.Run', 'VehicleAudioChecks.Run',
    'VehiclePresentationChecks.Run', 'NativeRunwayChecks.Run',
)]


def fixture(method, marker, output_env=None, result=None, **options):
    return dict(method=NAMESPACE + method, marker=marker, output_env=output_env,
                result=result, mode='shared', quit=True, **options)


# These routes preserve the existing methods, seeds, assertions and internal deadlines.
# An explicit selection plus dependency review is still required. No build/measure methods.
AFFECTED = {
    'authored-geometry': fixture('AuthoredCourseChecks.Run', 'AUTHORED_GEOMETRY_OK',
                                 'STAR_RACING_AUTHORED_QA_DIR', 'geometry-result.txt'),
    'orbital-geometry': fixture('OrbitalRiftChecks.Run', 'ORBITAL_GEOMETRY_OK',
                                'STAR_RACING_AUTHORED_QA_DIR', 'orbital-result.txt'),
    'basis-selection': fixture('BasisCompatibilityChecks.RunDefaultSelection',
                               'BASIS_DEFAULT_SELECTION_OK', exact_count=8),
    'basis-compatibility': fixture('BasisCompatibilityChecks.Run', 'BASIS_COMPAT_OK',
                                    'STAR_RACING_BASIS_COMPAT_DIR', 'structural-pass.txt'),
    'vector-runtime': fixture('VectorRuntimeChecks.Run', 'VECTOR_RUNTIME_CHECKS_OK',
                              'STAR_RACING_VEHICLE_RENDER_DIR', 'contracts.json',
                              count_field='assertions'),
    'visual-chunks': fixture('VisualMeshChunkChecks.Run', 'VISUAL_CHUNKS_EXACT_OK',
                             'STAR_RACING_CHUNK_QA', 'exact.json', count_field='checks'),
    'visual-chunks-shipping': fixture('VisualMeshChunkChecks.RunShipping',
                                      'VISUAL_CHUNKS_SHIPPING_OK', 'STAR_RACING_CHUNK_QA',
                                      'shipping.json', count_field='checks'),
    'world-reuse': dict(method=NAMESPACE + 'WorldReusePlayModeChecks.RunLastAssets',
                        marker='LAST_WORLD_ASSETS_OK', output_env='STAR_RACING_WORLD_QA_DIR',
                        result='success.json', count_field='assertions', quit=False,
                        # This fixture writes shared LocalRaceConfig PlayerPrefs with backup/restore.
                        mode='exclusive'),
}


def selected_fixtures(names):
    if not names or len(names) != len(set(names)):
        raise ValueError('affected requires a nonempty, unique --fixture selection')
    unknown = set(names) - AFFECTED.keys()
    if unknown:
        raise ValueError('Unknown affected fixture(s): ' + ', '.join(sorted(unknown)))
    return [dict(id=name, **AFFECTED[name]) for name in names]


def read_owned(path):
    path = Path(path)
    if path.is_symlink() or not path.is_file():
        raise ValueError('Missing or foreign fixture output: ' + str(path))
    return path.read_text()


def validate_affected(log, spec, output):
    if re.search(r'error CS\d+|Exception:|PROTOTYPE_QA_FAILED', log):
        raise ValueError('Compilation or check failure')
    if (output/'failure.txt').exists():
        raise ValueError('Fixture failure output exists')
    pattern = r'^' + re.escape(spec['marker']) + r' (?:assertions|checks)=(\d+)(?: .*)?$'
    matches = re.findall(pattern, log, re.M)
    if len(matches) != 1 or int(matches[0]) <= 0:
        raise ValueError('Missing, duplicated, foreign or zero fixture result')
    count = int(matches[0])
    if 'exact_count' in spec and count != spec['exact_count']:
        raise ValueError('Incomplete fixture count')
    result = spec.get('result')
    if result:
        raw = read_owned(output/result)
        if result.endswith('.json'):
            payload = json.loads(raw)
            if payload.get('success') is not True or payload.get(spec['count_field']) != count:
                raise ValueError('Fixture result disagrees with log')
        elif not re.match(pattern, raw.strip()):
            raise ValueError('Missing fixture result marker')
        elif raw.strip() not in log.splitlines():
            raise ValueError('Fixture result disagrees with log')
    return {'method': spec['method'], 'marker': spec['marker'], 'assertions_or_checks': count,
            # Existing methods do not emit a token. Do not claim token authentication.
            'fixture_token_validated': False,
            'provenance': 'fresh-owned-output-and-process-exit'}
