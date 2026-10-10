.DEFAULT_GOAL := help
.PHONY: help check check-full check-local check-ui tooling unity-editor build check-player

help:
	@printf '%s\n' 'make unity-editor — open Editor; use Play for current changes' 'make check-local — HUD/UI contracts only; add affected UI checks' 'make check-full CONFIRM_FULL_TESTS=yes — human-authorized complete Editor checks; no Player build' 'make build — explicitly requested local Player build' 'make check-player — explicitly requested full checks and build'

check: check-local

check-full:
	@test "$(CONFIRM_FULL_TESTS)" = yes || { echo "Full tests require separate human confirmation; then use CONFIRM_FULL_TESTS=yes" >&2; exit 2; }
	STAR_RACING_CONFIRM_FULL_TESTS=yes ./tools/unity.sh shared -batchmode -nographics -quit -executeMethod StarRacingPrototype.PrototypeChecks.RunWithFixtureEquivalence -logFile /tmp/star-racing-check.log

check-local: check-ui

check-ui:
	./tools/unity.sh shared -batchmode -nographics -quit -executeMethod StarRacingPrototype.RaceHudChecks.Run -logFile /tmp/star-racing-ui-check.log

tooling:
	python3 -m unittest discover -s tools -p 'test_*.py'

unity-editor:
	./tools/unity.sh exclusive

build:
	./tools/unity.sh shared -batchmode -quit -executeMethod StarRacingPrototype.PrototypeBuilder.BuildMac -logFile /tmp/star-racing-build.log

check-player: check-full
	$(MAKE) build
