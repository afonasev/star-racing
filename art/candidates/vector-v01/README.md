# Vector V01 — detailed offline art candidate

Авторская 3D-интерпретация выбранного концепта №4: клиновидный кузов с настоящими колёсными арками, отдельная кабина, воздухозаборники, утопленная оптика, красные задние шевроны, парный полый выхлоп и диффузор. Колёса содержат направленные спицы, тормозные диски/суппорты и протектор. Материал `PlayerPaint` задаёт цвет участника.

## Files

- `Vector_V01.blend`: редактируемая модель с модификаторами и иерархией колёс.
- `Vector_V01.fbx`, `Vector_V01.glb`: только автомобиль, без декораций/камер/света.
- `Vector_CloudCity_Review.blend`: модель, именованные камеры, разноцветные соперники и окружение с упакованными проектными текстурами.
- `design-reference.png`: выбранный концепт из этого чата; рендеры создаются из реальной геометрии Blender, не генератором картинок.
- `manifest.json`, `export-validation.json`: состав, происхождение декораций и независимая проверка экспортов.

Исходные оси Blender: +Y вперёд, +Z вверх, метры. Экспортёры используют стандартные системы осей FBX/glTF. Four wheel pivots and `Exhaust_L` / `Exhaust_R` retained.

## Reproduce

```sh
blender -b --python art/candidates/vector-v01/create_model.py -- /absolute/output
blender -b --python art/candidates/vector-v01/verify_exports.py -- /absolute/output
blender -b /absolute/output/Vector_CloudCity_Review.blend --python art/candidates/vector-v01/render_views.py -- /absolute/renders
```

Blender 5.2.1, Cycles; генератор использует seed 404 для расстановки башен. Источники дороги, фасадов, облаков и HDRI — `unity-prototype/Assets/StarRacing/Resources/Environment/`. Расстановка декораций репрезентативная, не экспорт игрового уровня. Chase camera uses baseline `ChaseCamera.cs` offsets and the game's 65-degree vertical FOV.

## Review

Девять финальных PNG и страница переключения ракурсов: `/Users/eaafonasev/Projects/star-racing/planning/evidence/create-vector-art-candidate/index.html`. Рендеры сохраняются в общем planning-store; в Git кода — исходные ассеты и проверка экспортов.

## Scope

Это отдельный арт-кандидат для визуальной оценки. Игровые Assets, runtime, управление, коллайдеры и установленная игра не менялись. Показанные материалы — Cycles, не подтверждение внешнего вида в URP. LOD, 64-машинная производительность, движение колёс и интеграция потребуют отдельного этапа. Human visual acceptance pending.
