# Музыка меню: эскизы

Два оригинальных генеративных эскиза для выбора музыкального направления Star Racing / Cloudline. Это материалы для прослушивания, ещё не игровые ассеты и не бесшовные петли.

1. `01-cloudline.mp3`: воздушная мелодичная электроника; prompt задаёт 108 BPM.
2. `02-gridline.mp3`: сдержанный мелодичный синтвейв; prompt задаёт 116 BPM.

Tempo — целевой параметр генерации, не измеренный BPM. Наличие/отсутствие вокала и музыкальную пригодность должен подтвердить слушатель. Первый вариант выбран и подключён к runtime меню; эти MP3 сохранены как исходные previews.

## Источники и воспроизводимость

Модель: локальная Stable Audio 3 `sm-music`, decoder `same-s`, fp16 DiT, fp32 decoder, 8 steps, CFG 1, 48 секунд, seeds 101001 / 101002. WAV — исходные экспорты модели; MP3 — previews после FFmpeg `loudnorm=I=-20:TP=-2:LRA=8`, 44.1 kHz stereo, 192 kbps. Точные prompts и хеши сохранены в `generation.json`.

Runner: `/Users/eaafonasev/.local/share/star-tournament-music/stable-audio-3/optimized/mlx/.venv/bin/python scripts/sa3_mlx.py --dit sm-music --decoder same-s --seconds 48 --steps 8 --seed <seed> --prompt <prompt> --out <absolute-wav-path>` из каталога локального runner.

## Проверки

Оба MP3 успешно декодированы FFmpeg: 48 секунд, stereo, 44.1 kHz. Измеренные integrated loudness / true peak: Cloudline −20.57 LUFS / −3.65 dBTP; Gridline −19.35 LUFS / −2.78 dBTP. Эти измерения не доказывают музыкальную или слуховую приёмку.

Пользователь выбрал Cloudline («1», 2026-10-10). Игровой ассет: `unity-prototype/Assets/StarRacing/Resources/Audio/menu-cloudline.wav`. Обработка сохраняет выбранный материал: двухсекундное перекрытие конца и начала с взаимодополняющими sine-squared весами, длина петли 46 секунд, постоянное усиление −1.81 dB. Динамическая нормализация для петли не применяется: она нарушает идентичность граничных PCM-сэмплов.

Воспроизведение меню использует отдельный 2D AudioSource без изменения темпа, fade-in 0.6s по unscaled time, gain 0.65 × music volume и существующий mute / `--muted`. Выбор экранов меню не перезапускает петлю; старт гонки останавливает её. Гоночный плейлист сохранён.

Для повторной подготовки исходного игрового WAV: `python make_loop.py 01-cloudline.wav <absolute-output.wav> --gain-db -1.81` (Python с NumPy). Измеренная громкость ассета: −20.00 LUFS, true peak −2.81 dBTP; seam delta до квантования 0.001561. Слуховую оценку стыка измерения не заменяют. Player build, full tests и выкатка требуют отдельных команд пользователя.
