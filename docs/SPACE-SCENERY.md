# Космическое окружение

`SpaceSceneryPlan` задаёт отдельный детерминированный слой оформления для Legacy и Native12: исследовательские и модульные станции, верфи, кольцевые станции и астероидный пояс. Геометрия и физика трассы, AI, планета и город не изменяются.

Полный габарит каждой станции и астероида проверяется до размещения. Legacy использует оболочки контрольных точек кубических сегментов и полный размах paved-ветвей; Native12 — непрерывную all-road проверку. Отклонённые кандидаты не размещаются принудительно. Все модели помещаются в единичную сферу до масштабирования; коллайдеры не создаются.

`EnvironmentPlan.hash` — прежняя совместимая core identity; `presentationHash` дополнительно содержит версию и hash нового космического слоя. Изменение геометрии/материалов или генератора требует повышения `SpaceSceneryPlan.Version`. Город сохраняет прежнюю identity.

Лимиты: 210 астероидов и 18 станций; до 5 material batches на станцию; 12 разделяемых mesh-вариантов астероидов на builder. Все созданные Mesh/Material принадлежат окружению и освобождаются `Clear()`. Дальность камер включает полные bounds окружения. Это не результаты замера FPS или Player-производительности.

## Целевая проверка без звука

Из code worktree, с новым абсолютным каталогом доказательств:

```sh
STAR_RACING_SPACE_QA_DIR=/absolute/evidence/checks \
  tools/unity.sh shared -batchmode -noaudio -muted -quit \
  -executeMethod StarRacingPrototype.SpaceSceneryChecks.Run \
  -logFile /absolute/evidence/checks/editor.log
```

Рендер реальных моделей и ракурсов ChaseCamera в Unity Editor Play Mode, без сборки Player:

```sh
STAR_RACING_SPACE_QA_DIR=/absolute/evidence/preview \
  tools/unity.sh shared -batchmode -noaudio -muted \
  -executeMethod StarRacingPrototype.SpaceSceneryPreview.Run \
  -logFile /absolute/evidence/preview/editor.log
```

`SpaceSceneryPreview.Verify` сначала выполняет целевые contracts, затем preview. Preview завершает Editor самостоятельно; `-quit` ему не передаётся. PNG для Legacy/Native12, 1/4 экранов и model studies — визуальные доказательства, не человеческая приёмка. Сборка Player, полный прогон и выкатка отдельно по команде человека.
