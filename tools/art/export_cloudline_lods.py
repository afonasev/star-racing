"""Export visual-only C05 LODs; canonical FBX/blend/contact profile stay untouched.
Blender --factory-startup -b --python tools/art/export_cloudline_lods.py -- REPO OUTPUT
"""
import hashlib
import json
import sys
from pathlib import Path
import bpy

args = sys.argv[sys.argv.index('--') + 1:]
root, output = map(Path, args)
source = root / 'unity-prototype/Assets/StarRacing/Resources/Vehicle/Cloudline.fbx'
original = source.read_bytes()
output.mkdir(parents=True, exist_ok=True)
levels = []
for level, ratio in [(1, .25), (2, .065)]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    for obj in meshes:
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new('Visual-only LOD collapse', 'DECIMATE')
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.data.update()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    target = output / f'CloudlineLOD{level}.fbx'
    bpy.ops.export_scene.fbx(filepath=str(target), use_selection=True,
        object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True, bake_anim=False, use_mesh_modifiers=True,
        add_leaf_bones=False)
    inventory = {}
    for obj in meshes:
        obj.data.calc_loop_triangles()
        inventory[obj.name] = len(obj.data.loop_triangles)
    levels.append({'level': level, 'ratio': ratio, 'file': target.name,
        'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
        'exported_triangles': sum(inventory.values()), 'meshes': inventory})
assert source.read_bytes() == original, 'canonical FBX changed'
manifest = {'canonical_fbx_sha256': hashlib.sha256(original).hexdigest(),
    'blender': bpy.app.version_string, 'algorithm': 'per-material-collapse-v1',
    'levels': levels, 'unity_import_counts': 'independently audited by affected Editor fixture'}
(output / 'cloudline-lods.json').write_text(json.dumps(manifest, indent=2) + '\n')
print('C05_LODS_EXPORTED', json.dumps(manifest))
