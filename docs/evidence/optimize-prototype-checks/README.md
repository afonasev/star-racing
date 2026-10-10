# Достаточные QA gates и повторные fixtures

Change `optimize-prototype-checks`, session `01a1056c-c341-7961-acd3-e7f4f4e78d4b`.
Baseline `fdeb00001dfc0c8d84e9720264bb5c3328dca751`; exact implementation revision записывается в `star-racing-planning/evidence/optimize-prototype-checks/candidate/identity.json` и каноническом delivery record.
Unity 6000.3.23f1, локальный Mac; balance version 8, SHA256 `f69bcce78035d2c42e3e0f406e09ea9d4cff7ba60081b4d0cfa7937d2ed6f8d9`. Никакие percentages/hotspots Star Tournament не перенесены.

## Выбранный gate

Изменены только Editor checks, QA tooling, policy, QA metadata профиля и этот отчёт. Shipping runtime, assets, build preparation/pipeline, packages и wrapper не изменяются. Достаточный gate: Unity compilation + все `PrototypeChecks` + шесть fixture-equivalence cases, tooling regressions, strict OpenSpec и review diff/links. Unchanged Player не пересобирается автоматически. Human review этого internal результата остаётся открытым; существующая product/gamepad/audio acceptance не закрывается.

Матрица и команды: `.agents/references/qa-scope.md`; `python3 tools/qa.py plan --base <reviewed SHA>`. Неизвестные пути, пустой diff, runtime/config/assets/build pipeline выбирают full. Profile diff проверяется по содержимому: локальны только новые QA metadata keys. UI локальность требует ручного доказательства зависимостей; автоматически runtime UI всё ещё full. `checks` сертифицирует только Editor suite и оставляет остальные gates в `remaining`.

## Измерения повторных fixtures

Один маршрут seed 77, normal, jumps=false, native=true генерировался отдельно для roster 2/8/64. Теперь один route на theme, только в области одного вызова. Все roster cases и assertions сохранены. Нет global/static mutable cache и reuse Unity scenes/physics.

Медианы трёх exclusive warm запусков с одной и той же instrumentation:

| Фаза | До | После |
| --- | ---: | ---: |
| Число roster route constructions | 6 | 2 |
| Generation + TrackRoute construction | 0,379425 с | 0,205531 с |
| Всё тело `PrototypeChecks.Run` с диагностикой | 0,816522 с | 0,585519 с |
| Admission | 0,051437 с | 0,064931 с |
| Startup/import | 4,602070 с | 5,040885 с |
| Teardown/exit | 0,515790 с | 0,507685 с |
| Wrapper wall | 5,989811 с | 6,188057 с |

Сокращена подтверждённая повторная работа внутри метода примерно на 0,17 с в generation и 0,23 с в полном method body. Method delta включает сокращение диагностических fingerprint emissions; ускорение shipping method без instrumentation этим A/B не измерено. Ускорение всего запуска не установлено: startup/import доминирует и варьируется. Это три samples на вариант, не статистическое/target-device доказательство. Фазы измерены напрямую; медианы отдельных фаз не обязаны суммироваться в медиану total. Cold startup после admission около 33,48 с, не входит в warm таблицу. XML test duration здесь отсутствует: проект использует executeMethod assertions, а не XML test runner.

`timings.json`, `run-probes.py`, `instrument.py`, `summarize.py` и instrumented patches находятся в durable planning evidence. Исходная preliminary серия `before` использовала JsonUtility wrapper для TrackFrame, который не Serializable; она исключена из сравнения. Финальная `before-v1` и `after` используют explicit binary serialization всех stored TrackFrame fields и full ordered Definition JSON. Fingerprints одинаковы по обеим темам; full ordered assertion/grid observation trace `1BA9396FAA22CC4DA72993D0B93F013FD2F2B47E62BB2DE1C460AB0D554D66D9`, 450 основных assertions и 25 ghost assertions.

## Эквивалентность и повторная инициализация

`RosterFixtureEquivalenceChecks` сравнивает fresh и reused route для всех шести случаев: full definition/TrackFrame fingerprints, ordered Id/seat/profile/seed/grid slot/distance/lateral observations, отсутствие mutation до/после reads. Это отдельная regression, не замена roster/gameplay assertions.

`RunWithFixtureEquivalence` запускает полный suite и equivalence в одном Editor процессе. Run уничтожает checkpoint/ghost fixtures через finally/Dispose; geometry regression не использует Unity objects или global physics. Нет cached scene/state и обхода ownership. Раздельные команды остаются diagnostic option. Изолированное сравнение отдельного и combined запуска не состоялось: ожидающий owned exclusive wrapper отменён штатно после 556,68 с, до запуска Editor, exit 130. Это admission evidence (`startup-timings.json`), не test duration или green gate. Для combined entry point проверяется эквивалентность и полнота в shared correctness gate; ускорение инициализации не заявлено. Два возможных diagnostic запуска сведены в один, но численное A/B для этого нового regression gate остаётся вне установленных результатов.

Receipt validation отвергает zero/missing/skipped/duplicate/foreign suite/equivalence, чужие token/profile, compilation errors и source changes во время проверки. 7 tooling regressions покрывают scope boundary, rename, profile semantics и отрицательные receipts. Source fingerprint исключает только regeneratable Prototype.unity, который `Prepare` пересоздаёт; generated diff сохраняется как evidence и восстанавливается перед commit. Это не allowlist для shipping scene изменений: такой diff в `plan` выбирает full.

## Границы и воспроизведение

Instrumentation полностью удалена из shipping Editor checks до финального gate. Для повторения generation A/B восстановить baseline checks из Git, добавить final equivalence helper для полного fingerprint, применить `instrument.py`, запустить `run-probes.py`; затем применить optimization patch и тот же probe. Это только owned disposable worktree, exclusive wrapper; leases отпущены. Не смешивать admission/import с method duration.

Сохраняются full compile/checks/build/affected Player gates для runtime/config/assets/shared changes и broad integration: menu → countdown → natural AI race finish → Results → menu, pause/repeat, обе темы и recovery. Focused filters, synthetic Results и roster 2/8/64 не заменяют этот gate, physical devices или human acceptance. Deploy запрещён профилем. Чужие процессы/кеши/locks не затронуты.
