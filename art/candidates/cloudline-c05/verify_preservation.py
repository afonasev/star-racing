"""Verify C05 preserves C04 body and cabin; only rear lamps and added exhaust outlets differ."""
import bpy, json, hashlib, sys
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]);report=root/'preservation-validation.json';report.unlink(missing_ok=True)
allowed_old=('Thin red rear light',)
allowed_new=('Thin red rear light','Rear lamp embedded housing','Exhaust outlet','Exhaust dark chamber')
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
old=signatures(root.parent/'cloudline-c04/Cloudline_C04.blend',allowed_old)
new=signatures(root/'Cloudline_C05.blend',allowed_new)
assert old.keys()==new.keys(),(old.keys()-new.keys(),new.keys()-old.keys())
changed=[n for n in old if old[n]!=new[n]];assert not changed,changed
report.write_text(json.dumps({'passed':True,'unchanged_evaluated_meshes':len(old),'scope':'Only attached red lamps and two exhaust outlets added; complete cabin, body and wheels match C04','per_mesh_sha256':old},indent=2))
print('PRESERVATION PASSED',len(old),'unchanged evaluated meshes')
