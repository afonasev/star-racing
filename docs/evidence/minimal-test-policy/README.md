# Минимальная QA и расследование длительности, 2026-10-10

Scope: решение пользователя для всех проектов; защиты маршрутов и QA policy в Star Racing; read-only анализ последних сессий Star Racing, Spacewars и Star Tournament. Игровые runtime/tests других проектов не менялись. Baseline Star Racing: `7ff317b`. Владелец: `01a12483-f4ec-7893-9dec-659246ce6f02`, ветка `codex/minimal-test-policy`, исходный worktree `/Users/eaafonasev/.codex/worktrees/3630/star-racing`.

Глобальное правило установлено в `/Users/eaafonasev/.codex/AGENTS.md`; его Git-копия — [global-policy.md](global-policy.md), поскольку ~/.codex не Git-репозиторий. Полный набор требует отдельной явной команды человека, включая production. Неизвестный путь/пустой diff ведут к review, не full. Обязательные assertions, equality и длительности не сокращались. Процессная правка не меняет канонические игровые требования в planning root.

## Уже изменено

- QA planner выдаёт `review-required` для неизвестных/shared путей и изменённых Editor fixtures. `qa.py` выбирает tooling для собственных Python-контрактов; Unity не нужен для проверки command routing.
- `checks --scope full` требует `--confirm-full`. `make check-full` и составной `check-player` требуют `CONFIRM_FULL_TESTS=yes`. Aggregate методы в Unity wrapper требуют `STAR_RACING_CONFIRM_FULL_TESTS=yes`. Это защита от случайного запуска; CLI-флаг не заменяет человеческое разрешение и не защищает прямой запуск Unity в обход wrapper.
- Обновлены AGENTS, QA/local-development/delivery/game-qa references и workflow profile. Обычный `make check` сохраняет HUD subset; для каждого изменения выбирай фактически нужный fixture, а не этот alias по привычке.
- Документация/tooling больше не требуют автоматически OpenSpec all-validation: planning проверяется при его изменении. Shared runtime требует разбора и affected QA; изменение планировщика не объявляется Unity/runtime acceptance.

## Подтверждённые расходы

Источники, SHA256 и извлечённые результаты сохранены в [measurements.json](measurements.json). XML считался по листовым test-case, без двойного учёта suite. Это анализ сохранённых результатов, не новый benchmark и не гарантия ускорения.

| Запуск | Измерение | Вывод |
| --- | --- | --- |
| Star Racing production desktop: committed-final | wrapper 493,82 с; PrototypeChecks.Run 20,83 с | methodSeconds относится только к одному подметоду большого aggregate, а не всему набору |
| Тот же маршрут frozen-final / editor-full-stable | 457,11 / 473,02 с | Три успешных broad запуска суммарно 23,7 мин; snapshots различались, поэтому нельзя все повторы объявить лишними |
| Spacewars replay gate на cac959cd | EditMode 2537,44 с, 456/458 pass; PlayMode 81,76 с, 21/21 pass | Два timeout остаются реальными непрошедшими проверками; длинный wall нельзя считать pass |
| Spacewars replay-cost attribution, 240 тиков | baseline: step 3,01 с, capture 5,94 с, compare 26,49 с; optimized: 2,76 / 2,12 / 22,60 с | capture улучшен на 64,34%; comparison занимает около 77–82% измеренной работы |
| Star Tournament async-lab-selection | 52 EditMode cases за 391,12 с; DesignLabHistory 390,68 с | Практически вся длительность в history fixture; XML включает setup/teardown, это не только тело теста |
| Star Tournament update-button menu regression | 12 PlayMode cases за 195,97 с; IndependentPauseMenu 148,43 с | Даже menu-only subset дорог; повторная scene/match подготовка заслуживает отдельного профиля |

Последняя Spacewars сессия `01a12435-dad3-7c70-b505-e872bbaf8d0b` отдельно сообщила около 46 минут ожидания Unity при 82 секундах PlayMode. Очередь — не вычисления теста. В её 42-минутном EditMode на WorldRestore пришлось около 23 минут, на Army около 9 минут. Эти fixture totals взяты из сообщения сессии; сохранившаяся qualification подтверждает общий duration и два timeout. Удалённый raw worktree не восстанавливался.

Для Star Racing прочитан `committed-final/editor.log`: Editor стартовал 05:45:18Z, центральный PrototypeChecks.Run начался 05:51:39Z; до него уже выполнялись geometry/road/physics/idle fixtures. Initial asset refresh — 4,487 с. Поэтому разницу 494−21 с нельзя называть startup или queue overhead. Агрегат включает 120 simulated seconds idle на каждой теме и другие physics checks. Сокращать simulated duration нельзя; нужны timers отдельных fixture/setup/body для дальнейшего точного профиля.

## Ускорения помимо сокращения набора

1. **Spacewars: дешёвое точное сравнение bytes.** Вместо NUnit CollectionAssert над большим byte[] использовать сравнение длины и SequenceEqual/Span.SequenceEqual; при несовпадении найти первый различающийся byte и вызвать Assert.Fail с tick/index/length. Все тики, оба варианта и exact equality сохранить; hashing/sampling не заменяют этот контракт. Проверить helper на равных массивах, разной длине и различиях в начале/середине/конце, затем original affected replay cases в неизменных timeout. Из одной старой пары sample нельзя вывести точный будущий speedup. Эта оптимизация уже имеет отдельного владельца: `01a1247f-18c5-73b3-8b1a-71a14329b73c`, worktree `o1-test-speed/spacewars`, resources в `.../o1/test-speed-20261010/resources.json`; его исходники/процессы здесь не менялись.
2. **Star Tournament: отделить immutable catalog/migration подготовку от per-test mutable state.** Read-only parsed descriptors и fixtures можно подготавливать один раз, но snapshots/history/profile/player prefs должны оставаться независимыми. В XML самый долгий MigrationCache case — 32,11 с; ряд migration cases — 22–25 с. Это основание для profiling, ещё не доказательство конкретного cache miss. Menu tests заново загружают ProvingGround и начинают матч через реальные loading/gamepad пути. Чистый layout/pause-state unit слой может быть лёгким; несколько end-to-end lifecycle cases нужно сохранить в Unity. Простой OneTimeSetUp общей сцены без reset опасен из-за утечки prefs/devices/state.
3. **Star Racing: timers и разделение preparation/проверок.** Большой aggregate скрывает расход до/после PrototypeChecks.Run; добавить per-fixture timers при следующей относящейся правке. Проверки scene/build preparation запускать при изменении подготовки; чистая policy/tooling проверка не должна вызывать PrototypeBuilder.Prepare. Не менять physics step, число seeds или длительность ради скорости.
4. **Все Unity проекты: один startup для выбранных независимых fixtures.** Использовать поддерживаемый объединённый selector/runner с исходной изоляцией; переиспользовать принадлежащий задаче worktree/Library между итерациями. Ordinary fixtures — shared; exclusive только для performance или общих desktop/input/audio ресурсов. Не увеличивать параллелизм вслепую: CPU contention может удлинить тесты и вызвать timeout. Чужие jobs не останавливать.

## Что можно без Unity

- Python/shell command contracts, QA selection, receipts, manifest/format/signature checks, documentation и asset-file metadata — existing Python/CLI routes.
- `StarRacing.Runtime.ExhaustEnvelope` использует только System; чистые envelope cases могут исполнять исходный C# в независимом .NET runner. Проверки реального exhaust/render/material/lifecycle останутся Unity.
- `Spacewars/Simulation` не содержит прямых UnityEngine imports; есть `tools/ai-sim` на net8.0. Но это CLI-контракт headless пути, а не готовый runner всех PlayableWorldRestoreTests: последние зависят от authority/presentation/reflection. Нельзя объявить их перенесёнными без link исходников и dependency review. System SDK `dotnet` сейчас не найден в PATH; ничего не устанавливалось.
- Scene loading, GameObject lifecycle, Physics, importing, UI rendering, InputSystem и AudioSource integration остаются Unity. Мок или копия алгоритма на Python не доказывает исполнение C#.

## Проверка и границы результата

Выполнены affected Python contracts: test_qa (11), test_ui_route (5), test_local_workflow (5); всего 21 pass. Fake runner проверил отказ full/check-player/direct aggregate без подтверждения и правильный разрешённый маршрут, не запускал Unity. `sh -n tools/unity.sh` и `git diff --check` прошли. Полный project test run, Unity Editor/Player, builds и deployment не запускались. Дальнейшие runtime/test-body ускорения описаны выше и не объявляются внедрёнными или измеренными.

Исходный worktree используется этим чатом; новых worktree, Unity cache, build и долгоживущих процессов задача не создала. TemporaryDirectory fake-runner tests автоматически удалены. Measurements и глобальная policy-копия сохраняются в Git для review и восстановления. Unrelated main Unity import/settings edits сохраняются без изменений. Human acceptance отчёта остаётся за пользователем.
