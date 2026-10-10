"""Export the unchanged STRIDE v3 GLB to a static, eight-material Unity FBX.
Run with Blender --background --factory-startup --python this-file.
"""
import bpy
import hashlib
import json
import re
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root / 'ArtSource/Stride/stride.glb'
out = root / 'unity-prototype/Assets/StarRacing/Resources/Vehicle'
expected = 'd44cc1de6a7255bf4f4807f3177714518d65c2ec04924e65b234cd502c34bb62'
assert hashlib.sha256(source.read_bytes()).hexdigest() == expected, 'Unexpected source revision'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
# Bake world transforms before removing the authored hierarchy. All objects share
# the same origin, so joining/separating cannot move the wheels or body panels.
for obj in meshes:
    matrix = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = matrix
bpy.ops.object.select_all(action='DESELECT')
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.join()
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='MATERIAL')
bpy.ops.object.mode_set(mode='OBJECT')
parts = list(bpy.context.selected_objects)
assert len(parts) == 8, f'Expected eight materials, got {len(parts)}'
for obj in parts:
    used = {p.material_index for p in obj.data.polygons}
    assert len(used) == 1
    mat = obj.data.materials[next(iter(used))]
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    obj.name = mat.name
    obj.data.name = 'STRIDE-' + mat.name
out.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=str(out / 'Stride.fbx'), use_selection=True,
                        object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                        apply_unit_scale=True, bake_anim=False, use_mesh_modifiers=True,
                        add_leaf_bones=False)
# Generate native URP material assets from the actual GLB material definitions.
data = source.read_bytes()
length = struct.unpack_from('<I', data, 12)[0]
gltf = json.loads(data[20:20+length])
template = (out.parent / 'PrototypeLit.mat').read_text()
def srgb(x):
    return 12.92*x if x <= .0031308 else 1.055*x**(1/2.4)-.055
def color(values):
    return '{r: %s, g: %s, b: %s, a: %s}' % tuple(values)
material_dir = out / 'StrideMaterials'
material_dir.mkdir(exist_ok=True)
for material in gltf['materials']:
    pbr = material.get('pbrMetallicRoughness', {})
    base = pbr.get('baseColorFactor', [1,1,1,1])
    base = [srgb(c) for c in base[:3]] + [base[3]]
    emission = material.get('emissiveFactor', [0,0,0])
    strength = material.get('extensions', {}).get('KHR_materials_emissive_strength', {}).get('emissiveStrength', 1)
    emission = [srgb(c) * strength for c in emission] + [1]
    text = template.replace('m_Name: PrototypeLit', 'm_Name: ' + material['name'])
    text = text.replace('m_EnableInstancingVariants: 0', 'm_EnableInstancingVariants: 1')
    text = re.sub(r'- _Metallic: .*', '- _Metallic: ' + str(pbr.get('metallicFactor', 1)), text)
    text = re.sub(r'- _Smoothness: .*', '- _Smoothness: ' + str(1-pbr.get('roughnessFactor', 1)), text)
    for key, value in [('_BaseColor', base), ('_Color', base), ('_EmissionColor', emission)]:
        text = re.sub(r'- ' + key + r': .*', '- ' + key + ': ' + color(value), text)
    if max(emission[:3]) > 0:
        text = text.replace('m_ValidKeywords: []', 'm_ValidKeywords:\n  - _EMISSION')
    (material_dir / (material['name'] + '.mat')).write_text(text)
report = {'source_commit': '8951583', 'source_sha256': expected,
          'renderers': len(parts), 'triangles': sum(len(o.data.polygons) for o in parts),
          'materials': sorted(o.name for o in parts),
          'fbx_sha256': hashlib.sha256((out/'Stride.fbx').read_bytes()).hexdigest()}
(root/'ArtSource/Stride/unity-export.json').write_text(json.dumps(report, indent=2)+'\n')
print('STRIDE_UNITY_EXPORT', json.dumps(report))
