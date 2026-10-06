check:
	./tools/unity.sh shared -batchmode -nographics -quit -executeMethod StarRacingPrototype.PrototypeChecks.Run -logFile /tmp/star-racing-check.log
build:
	./tools/unity.sh shared -batchmode -quit -executeMethod StarRacingPrototype.PrototypeBuilder.BuildMac -logFile /tmp/star-racing-build.log
