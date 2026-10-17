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

## Детализация v2

Версия 2 сохраняет item transforms и варианты v1; меняется presentation identity. Корпуса имеют развёртку каждой плоскости и торцов, tangents, сервисные каналы, радиаторные рёбра, усиленные доки и вентиляцию кольца. Сетка панелей выступает над солнечной поверхностью. Основные цилиндры — 24 стороны. Астероиды сохраняют 12 общих сеток, получают плоские сколы и локальную triplanar-текстуру с правильно ориентированными normal-проекциями; mipmaps фильтруют детали вдали.

`SpaceHull[Normal]` и `SpaceRock[Normal]` — единственное исключение runtime-бюджета до2048px. Источник imagegen — **1254×1254**, сохраняется без искусственного увеличения (`NPOTScale.None`); бюджет2048 не означает source2K. Новые normal-источники повторяют исходные пиксели albedo; Unity importer преобразует их в слабый микрорельеф (`heightmapScale=.035`). Это не физически восстановленная карта высот. Существующие normal maps не переконвертируются. Новые карты: Repeat/Trilinear/mipmaps/aniso8/CompressedHQ, read-only. Generated originals хранятся в Codex; используемые PNG сохранены в Resources.

Ограниченная проверка детализации, rebuild/cleanup и PlayMode PNG за один запуск:

```sh
STAR_RACING_SPACE_QA_DIR=/absolute/evidence/detail \
  tools/unity.sh shared -batchmode -noaudio -muted \
  -executeMethod StarRacingPrototype.SpaceSceneryPreview.Detail \
  -logFile /absolute/evidence/detail/editor.log
```

`SpaceSceneryChecks.Detail` исполняет замороженный C# placement-baseline v1/5b54604a на шести Legacy seeds и шести Native12 комбинациях, сравнивает каждую позицию/вращение/размер/variant, проверяет прежнюю core identity, clearance и импорт. Для всех четырёх станций проверяет невырожденные UV и tangents; существующий Build fixture сохраняет два rebuild/cleanup на обоих маршрутах, все реальные вершины, коллайдеры, исходную физическую геометрию, лимиты 5 batches и <1млн треугольников. Полный прежний graph/tyre-mark chain не запускается: их код и размещение не менялись. Замер FPS четырёх камер, Player/device и человеческая визуальная приёмка остаются отдельными.

Проверенный инвентарь seed77: 228 объектов и 860660 треугольников космического слоя на обоих маршрутах (v1:741668). В Editor/Metal NPOT-карты фактически импортированы как RGBA32/1254×1254, несмотря на CompressedHQ; приблизительный объём четырёх карт с mipmaps —32MiB. Это не результат измерения FPS или формата будущего Player. Увеличенный промежуток между backing, solar faces и grid проверен реальными близкими/дальними кадрами; тонкие элементы на расстоянии остаются ограничены пиксельным разрешением.
