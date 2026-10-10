"""Run in Blender background to verify both saved exchange assets independently."""
import bpy, json, sys, math
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]); results={}
(root/'export-validation.json').unlink(missing_ok=True)
for suffix in ['fbx','glb']:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 path=root/('Cloudline_C03.'+suffix)
 if suffix=='fbx':bpy.ops.import_scene.fbx(filepath=str(path))
 else:bpy.ops.import_scene.gltf(filepath=str(path))
 bpy.context.view_layer.update()
 objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
 vertices=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
 assert objects and vertices
 assert all(math.isfinite(n) for v in vertices for n in v)
 low=[min(v[a] for v in vertices) for a in range(3)];high=[max(v[a] for v in vertices) for a in range(3)]
 dims=[high[a]-low[a] for a in range(3)]
 assert 2.5<dims[0]<2.9 and 4.8<dims[1]<5.2 and 1.2<dims[2]<1.4,(suffix,dims)
 mats=sorted({m.name for o in objects for m in o.data.materials if m})
 assert len(mats)==10,(suffix,mats)
 assert not any(o.type in {'CAMERA','LIGHT'} for o in bpy.context.scene.objects)
 wheels=[o for o in bpy.context.scene.objects if o.name.startswith('Wheel_')]
 assert len(wheels)==4
 for wheel in wheels:
  points=[o.matrix_world@v.co for o in wheel.children if o.type=='MESH' for v in o.data.vertices]
  center=[(min(v[a] for v in points)+max(v[a] for v in points))/2 for a in range(3)]
  assert abs(abs(center[0])-1.08)<.1 and abs(abs(center[1])-1.455)<.05 and abs(center[2]-.515)<.03,(wheel.name,center)
 triangles=0
 for o in objects:o.data.calc_loop_triangles();triangles+=len(o.data.loop_triangles)
 results[suffix]={'mesh_count':len(objects),'triangles':triangles,'materials':mats,'bounds_min':low,'bounds_max':high,'dimensions_xyz_metres':dims,'wheels':len(wheels),'passed':True}
(root/'export-validation.json').write_text(json.dumps(results,indent=2));print('EXPORT VALIDATION PASSED',json.dumps(results))
