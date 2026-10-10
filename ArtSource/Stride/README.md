# STRIDE v3 — исходник и Unity asset

Восстановлена готовая модель из commit `8951583` (спортивный клиновидный нос). `stride.glb` и `stride.blend` восстановлены без изменений. SHA-256 GLB: `d44cc1de6a7255bf4f4807f3177714518d65c2ec04924e65b234cd502c34bb62`.

GLB: +Y вверх, +Z вперёд, метры; 74 644 треугольника, восемь материалов. Editable Blender source и технические metadata/validation сохранены здесь. Исходный генератор и все review previews доступны в Git по указанному commit.

`export_unity.py` импортирует GLB в Blender, запекает transforms и объединяет статическую геометрию по материалам. Экспортирует `Resources/Vehicle/Stride.fbx` с восемью мешами и native URP материалы в `StrideMaterials`. Удаление детализации, LOD, новые коллайдеры, анимации колёс и нитро-пламя в этот экспорт не входят. Исходные pivot/socket nodes остаются в GLB; static FBX содержит только геометрию.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python ArtSource/Stride/export_unity.py
python3 ArtSource/Stride/validate_stride.py ArtSource/Stride/stride.glb --require-detail
```

`unity-export.json` фиксирует хеши и состав. Unity импорт настраивает `StrideAssetImporter`; runtime `StaticVehicleVisual` масштабирует модель равномерно под прежний collision envelope и сохраняет прежний нижний зазор. Геометрия и материалы общие для участников; цвет PlayerPaint задаётся MaterialPropertyBlock.

Техническая проверка не заменяет визуальную приёмку или performance при 64 машинах.
