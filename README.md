# Star Racing — Unity prototype

<!-- desktop-downloads:start -->
**Скачать / Download**

| Windows | macOS |
| --- | --- |
| **[Download EXE](https://github.com/afonasev/star-racing/releases/download/v0.3.16/Star-Racing-0.3.16-Windows-Setup.exe)** | **[Download PKG](https://github.com/afonasev/star-racing/releases/download/v0.3.16/Star-Racing-0.3.16-macOS-Setup.pkg)** |

[Только установщики / Installer releases](https://github.com/afonasev/star-racing/releases?q=tag%3Av0%20prerelease%3Afalse)

Installer version: **0.3.16**.
<!-- desktop-downloads:end -->

Играбельная база магнитных гонок на Unity 6000.3.23f1 / URP.

## Реализовано

1–4 локальных игрока (два набора клавиатуры и до четырёх геймпадов), 8/16/32/64 участника с Rookie/Racer/Ace AI, случайная стартовая решётка и опциональная фора. Процедурные трассы, Cloud City / Space Station, настройки seed/рельсов/прыжков. Магнитная езда, нитро, занос, прыжки, безопасный recovery и ghost-защита. Split-screen, countdown, позиции, финишное окно, результаты/DNF, повтор, пауза и возврат в меню. Модели машин, окружение, музыка, синтез двигателя и эффекты. Главное меню и подготовка гонки оформлены в стиле Cloudline. Последняя выбранная конфигурация (включая имена и устройства) сохраняется автоматически; Start использует текущий выбор без Apply. При каждом входе в подготовку выбирается новый seed; кубик в поле seed меняет его случайно. Темы и ограждения выбираются из выпадающих списков. Сеть пока недоступна.

Новые конфигурации используют Native12: четвёртое семейство предлагает короткую техническую и длинную скоростную дороги с общим входом и схождением. Явно сохранённые revision10/11 и их повторы сохраняют прежнюю версию. Проверка на устройстве и человеческая приёмка остаются открытыми.

Открой `unity-prototype` в Unity. Сцена: `Assets/StarRacing/Generated/Prototype.unity`.

### Тихий запуск

Тихий Player: `./tools/unity.sh player 'unity-prototype/Builds/macOS/Star Racing.app/Contents/MacOS/Star Racing' -noaudio -muted`. Общий прикладной параметр `-muted` применяется до splash и загрузки сцены, пропускает инициализацию игрового аудио и действует всю сессию. Для прямого запуска: `"$PWD/unity-prototype/Builds/macOS/Star Racing.app/Contents/MacOS/Star Racing" -noaudio -muted`. Старый `--muted` остаётся совместимым. Режим не меняет сохранённые настройки звука или системную громкость.

## Проверки и сборка

```sh
./tools/unity.sh shared -batchmode -noaudio -muted -nographics -quit -executeMethod StarRacingPrototype.RaceHudChecks.Run -logFile /absolute/editor.log
# Сборка только по отдельной команде человека:
./tools/unity.sh shared -batchmode -noaudio -muted -quit -executeMethod StarRacingPrototype.PrototypeBuilder.BuildMac -logFile /absolute/build.log
./tools/unity.sh player 'unity-prototype/Builds/macOS/Star Racing.app/Contents/MacOS/Star Racing' -noaudio -muted
```

Тихий запуск Editor: `./tools/unity.sh shared -noaudio -muted`. Для Player используйте команду выше с `-noaudio -muted`: Unity отключает аудиовывод до инициализации, а игра не создаёт аудиокоординатор. Режим действует всю сессию и не меняет сохранённые настройки звука или системную громкость. Обёртка передаёт параметры без изменений и добавляет оба для автономных запусков с `-batchmode`, `-executeMethod` или `-runTests`. Общий прикладной параметр `-muted` обрабатывается до создания аудиокоординатора; он работает и отдельно, а Unity-проверки запускаются с обоими параметрами. Старый `--muted` также распознаётся до инициализации аудио.

Геймпад: RT/RB — газ, A — тормоз/назад, LT/LB — занос, B — нитро. Игрок 1: WASD/Space/Left Shift; игрок 2: стрелки/Right Alt/Right Shift. S и ↓ — тормоз с переходом в задний ход после остановки. ESC — пауза, F5 — повтор, Backspace — меню, F3 — FPS. Start на назначенном геймпаде — пауза. Y добавляет игрока с нажавшим геймпадом; имя сохраняется за ним в пределах сессии, включая переподключение. Карточки позволяют менять имя (до 10 символов и по ширине поля) и удалять игрока. Устройство каждого места выбирается в подготовке; отключение во время гонки ставит паузу. Для одного игрока используется весь экран, для двух — вертикальные половины, для трёх/четырёх — сетка 2×2.

Канонические спеки текущих игровых возможностей: `/Users/eaafonasev/Projects/star-racing/planning/openspec/specs`.

Физические геймпады, ощущения от езды и звук требуют человеческой приёмки. Windows build поддерживается builder при установленном модуле Unity; текущая локальная поставка — macOS. Производительность и другие платформы развиваются отдельными изменениями.

Проверенный базовый заезд — 8 участников. Состав 64 создаётся и едет, но пока заметно проседает по FPS; оптимизация больших составов остаётся отдельной работой.

### Анимированная загрузка Cloudline

Runtime меню открывается без создания трассы и автомобилей. После готовности меню постепенно загружаются45 общих импортированных ассетов: модель Cloudline, исходные материалы, текстуры дорог/обеих тем/неба, reflection cubemaps и shaders. Они удерживаются отдельным session cache между разными уровнями. Сгенерированные meshes, экземпляры материалов и коллайдеры в этот кеш не входят.

Хранится только последний опубликованный уровень для быстрого рестарта и один выбранный в настройках кандидат. При смене выбора или выходе из настройки кандидат освобождается; Start продолжает его подготовку, если ключ совпадает. После успешного перехода прошлый уровень удаляется — истории готовых уровней нет. Отмена сохраняет текущую гонку. Машины, баланс, AI и input готовятся заново; fresh seed при открытии настройки сохранён. Бюджет памяти/lowMemory отменяет только спекулятивный кандидат. Общие импортированные ресурсы освобождаются после завершения session, без принудительного Destroy/UnloadAsset.

Спекулятивная подготовка использует порции около 4 мс; после «Старт» порции увеличиваются до 12 мс для ускорения загрузки. Это пороги циклов, не гарантия длительности кадра: отдельное native cooking остаётся неделимым. Синхронный `RaceDirector` без `RaceStartup` сохраняет eager-путь для Editor tooling; `reuseWorld:false` у API подготовки принудительно строит мир заново для cold parity QA.

Проверка кеша, отмены, точного replay, реальных физических контактов и времени (для замеров — `exclusive`, для обычной функциональной QA — `shared`):

```sh
STAR_RACING_WORLD_QA_DIR=/absolute/owned-world-qa ./tools/unity.sh shared -batchmode -noaudio -muted -executeMethod StarRacingPrototype.WorldReusePlayModeChecks.RunLastAssets -logFile /absolute/owned-world-qa/editor.log
```

Загрузка меню, подготовка и повтор гонки используют двухстрочное название со статичной звездой и линиями скорости, плавное появление и редкий блик по буквам; ниже с большим отступом — явная полоса со скользящим световым сегментом без выдуманного процента. Анимация идёт по реальному времени и работает при `Time.timeScale=0`. Расчёт трассы и плана окружения выполняется вне основного потока, создание Unity-ресурсов отдаёт кадры между небольшими порциями работы. Отдельный native-вызов загрузки/запекания меша остаётся неделимой операцией; это не гарантия аппаратного FPS.

Точечная проверка реальных загрузок, mesh/replay parity и ошибок (Editor, без Player build и без полного набора):

```sh
STAR_RACING_LOADING_QA_DIR=/absolute/owned-loading-qa ./tools/unity.sh shared -noaudio -muted -executeMethod StarRacingPrototype.LoadingPlayModeChecks.Run -logFile /absolute/owned-loading-qa/editor.log
```

Проверка требует graphics-enabled Editor для PNG; создаёт изолированные QA-настройки, сохраняет и восстанавливает выбранную конфигурацию гонки. Прозрачный wordmark: `unity-prototype/Assets/StarRacing/Resources/UI/LoadingWordmark.png`, подготовлен из принятого визуального эскиза. Блик маскируется альфа-каналом букв и не затрагивает фон.
