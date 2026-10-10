"""Verify C04 changes cabin only; compare evaluated meshes against preserved C03."""
import bpy, json, hashlib, sys
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]);report=root/'preservation-validation.json';report.unlink(missing_ok=True)
allowed_old=('Inset canopy','Canopy surround','Roof silver spine','Silver buttress')
allowed_new=('Cabin ','Rear cabin panel')
def signatures(path,prefix):
 bpy.ops.wm.open_mainfile(filepath=str(path));deps=bpy.context.evaluated_depsgraph_get()
 collection=next(c for c in bpy.data.collections if c.name.endswith('MODEL'))
 result={}
 for obj in collection.objects:
  if obj.type!='MESH' or obj.name.startswith(prefix):continue
  ev=obj.evaluated_get(deps);me=ev.to_mesh()
  data={'vertices':[[round(v,6) for v in vert.co] for vert in me.vertices],'faces':[list(p.vertices) for p in me.polygons],'matrix':[[round(v,6) for v in row] for row in obj.matrix_world]}
  result[obj.name]=hashlib.sha256(json.dumps(data,sort_keys=True).encode()).hexdigest();ev.to_mesh_clear()
 return result
old=signatures(root.parent/'cloudline-c03/Cloudline_C03.blend',allowed_old)
new=signatures(root/'Cloudline_C04.blend',allowed_new)
assert old.keys()==new.keys(),(old.keys()-new.keys(),new.keys()-old.keys())
changed=[n for n in old if old[n]!=new[n]];assert not changed,changed
report.write_text(json.dumps({'passed':True,'unchanged_evaluated_meshes':len(old),'scope':'Only cabin glazing, roof, frame, rear cabin supports and glass material allowed to change; body and wheels match C03','per_mesh_sha256':old},indent=2))
print('PRESERVATION PASSED',len(old),'unchanged evaluated meshes')
