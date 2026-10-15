#!/bin/sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
project_dir="$repo_dir/unity-prototype"
runner=${UNITY_RUNNER:-$HOME/.local/bin/unity-run}
editor=${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity}
# Autonomous launches disable Unity audio before engine initialization.
# Interactive launches retain the caller's explicitly selected audio mode.
automated=false
silent=false
muted=false
legacy_muted=false
for argument in "$@"; do
  case "$argument" in -batchmode|-executeMethod|-runTests) automated=true ;; esac
  [ "$argument" != -noaudio ] || silent=true
  [ "$argument" != -muted ] || muted=true
  [ "$argument" != --muted ] || legacy_muted=true
done
if $automated || $silent || $muted || $legacy_muted; then
  if ! $silent; then set -- "$@" -noaudio; fi
  if ! $muted; then set -- "$@" -muted; fi
fi
# Aggregate suites must never become an implicit gate, including production routes.
previous=
for argument in "$@"; do
  if [ "$previous" = -executeMethod ]; then
    case "$argument" in
      StarRacingPrototype.PrototypeChecks.Run|StarRacingPrototype.PrototypeChecks.RunWithFixtureEquivalence)
        [ "${STAR_RACING_CONFIRM_FULL_TESTS:-}" = yes ] || {
          echo "Full tests require separate human confirmation (STAR_RACING_CONFIRM_FULL_TESTS=yes)" >&2
          exit 2
        } ;;
    esac
  fi
  previous=$argument
done
[ -x "$runner" ] || { echo "Install ~/.local/share/unity-run/install.sh first" >&2; exit 2; }
case "${1:-}" in
  shared|exclusive)
    mode=$1; shift
    [ -d "$project_dir" ] || { echo "Missing Unity project: $project_dir" >&2; exit 2; }
    exec "$runner" "--$mode" --timeout "${UNITY_ADMISSION_TIMEOUT:-120}" --project "$project_dir" -- "$editor" -projectPath "$project_dir" "$@" ;;
  player)
    shift
    mode=${UNITY_RUN_MODE:-shared}
    [ "$mode" = shared ] || [ "$mode" = exclusive ] || exit 2
    [ "$#" -gt 0 ] || { echo "Provide direct Player executable" >&2; exit 2; }
    executable=$1; shift
    exec "$runner" "--$mode" --timeout "${UNITY_ADMISSION_TIMEOUT:-120}" -- "$executable" "$@" ;;
  *) echo "Usage: tools/unity.sh shared|exclusive [Editor arguments] | player EXECUTABLE [arguments]" >&2; exit 2 ;;
esac
