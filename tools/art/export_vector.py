"""Bake V05 directly to float32 Unity mesh payload, preserving every corner.
blender -b --python tools/art/export_vector.py -- /absolute/repo
No decimation, normal re-encoding, texture atlas or runtime mesh construction.
"""
import bpy,sys,json,hashlib,struct
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]).resolve();source=root/'art/candidates/vector-v05/Vector_V05.blend'
bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update()
car=bpy.data.collections['VECTOR • canonical car'];deps=bpy.context.evaluated_depsgraph_get()
vertices=[];indices=[];materials={};lookup={};canonical=[];objects=0
# Blender +Y forward/+Z up -> Unity +Z forward/+Y up. Reverse triangle winding.
def unity(v):return (v[0],v[2],v[1])
anchors={o.name:unity(o.matrix_world.translation) for o in car.objects if o.name in ['Exhaust_L','Exhaust_R']};assert len(anchors)==2
for o in car.objects:
 if o.type not in {'MESH','CURVE','FONT'}:continue
 objects+=1;ev=o.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles()
 matrix=o.matrix_world;normal_matrix=matrix.to_3x3().inverted().transposed()
 for tri in me.loop_triangles:
  mat=me.materials[tri.material_index];p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
  color=tuple(p.inputs['Base Color'].default_value)[:3]
  values=(p.inputs['Metallic'].default_value,1-p.inputs['Roughness'].default_value,*color,p.inputs['Emission Strength'].default_value if max(p.inputs['Emission Color'].default_value[:3]) else 0,1.0 if mat.name=='PlayerPaint' else 0.0,p.inputs['Coat Weight'].default_value,1-p.inputs['Coat Roughness'].default_value,0.0)
  materials[mat.name]=values
  for corner in (0,2,1):
   pos=unity(matrix@me.vertices[tri.vertices[corner]].co);normal=unity((normal_matrix@me.corner_normals[tri.loops[corner]].vector).normalized())
   packed=struct.pack('<16f',*pos,*normal,*values);canonical.append(packed)
   if packed not in lookup:lookup[packed]=len(vertices);vertices.append(packed)
   indices.append(lookup[packed])
 ev.to_mesh_clear()
payload=struct.pack('<8sII',b'VECTOR05',len(vertices),len(indices))+b''.join(vertices)+struct.pack('<%dI'%len(indices),*indices)
out=root/'art/candidates/vector-v05/runtime.meshbin';out.write_bytes(payload)
# Independently decode the written representation and compare every indexed corner.
raw=out.read_bytes();magic,nv,ni=struct.unpack_from('<8sII',raw);assert magic==b'VECTOR05'
actual_indices=struct.unpack_from('<%dI'%ni,raw,16+nv*64)
reconstructed=[raw[16+i*64:16+(i+1)*64] for i in actual_indices]
assert reconstructed==canonical,'Triangle/corner/material payload changed'
record={'source_commit':'13b033136133f07326fa636e6a669ad20c98406c','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'source_objects':objects,'renderers':1,'materials':1,'triangles':len(indices)//3,'vertices':len(vertices),'geometry_normals_surface_payload_exact_float32':True,'canonical_corner_sha256':hashlib.sha256(b''.join(canonical)).hexdigest(),'source_surface_payload':materials,'uv_layout':['metallic,smoothness','baseR,baseG','baseB,emission','playerPaint,coat','coatSmoothness,reserved'],'anchors':anchors,'meshbin_sha256':hashlib.sha256(raw).hexdigest(),'limitations':'Unity PBR reference ports surface constants and coat; Blender procedural micro-bump and volumetric glass are engine-specific. No geometry simplification.'}
(root/'art/candidates/vector-v05/runtime-export.json').write_text(json.dumps(record,indent=2)+'\n')
print('VECTOR_LOSSLESS_EXPORT_OK',len(indices)//3,objects,'-> 1 renderer',len(vertices),'vertices')
