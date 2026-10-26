"""Independently reimport each exchange asset; validate actual geometry/hierarchy."""
import bpy, json, math, sys, hashlib
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]);results={}
for ext in ['fbx','glb']:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 path=root/('Vector_V04.'+ext)
 if ext=='fbx':bpy.ops.import_scene.fbx(filepath=str(path))
 else:bpy.ops.import_scene.gltf(filepath=str(path))
 bpy.context.view_layer.update()
 objs=list(bpy.context.scene.objects);meshes=[o for o in objs if o.type=='MESH']
 points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
 assert meshes and points,'Empty export'
 assert all(math.isfinite(x) for p in points for x in p),'Non-finite geometry'
 low=[min(v[i] for v in points) for i in range(3)];high=[max(v[i] for v in points) for i in range(3)];dims=[high[i]-low[i] for i in range(3)]
 assert 2.15<dims[0]<2.65 and 4.8<dims[1]<5.3 and 1.15<dims[2]<1.23,(ext,dims)
 assert -.03<low[2]<.03,(ext,'tyre ground',low[2])
 assert all(o.type not in {'CAMERA','LIGHT'} for o in objs),'Environment leaked into model export'
 assert not any('City tower' in o.name or 'Road inspection' in o.name for o in objs)
 wheels=[o for o in objs if o.name.startswith('Wheel_')]
 assert len(wheels)==4,(ext,'wheel pivots',len(wheels))
 for o in wheels:
  pos=o.matrix_world.translation
  assert abs(abs(pos.x)-.985)<.005 and abs(pos.z-.48)<.005
  assert min(abs(pos.y-1.50),abs(pos.y+1.50))<.005
  assert len(o.children)>30,'Wheel detail missing'
 sockets=[o for o in objs if o.name.startswith('Exhaust_L') or o.name.startswith('Exhaust_R')]
 assert len(sockets)==2
 mats=sorted({m.name.split('.')[0] for o in meshes for m in o.data.materials if m})
 for name in ['PlayerPaint','Smoked_Glass','Performance_Rubber','Red_LED','Forged_Titanium','White_LED','Grille_Titanium']:
  assert name in mats,(ext,name,mats)
 triangles=0
 for o in meshes:o.data.calc_loop_triangles();triangles+=len(o.data.loop_triangles)
 assert triangles>20000,'Unexpectedly empty or reduced geometry'
 results[ext]={'passed':True,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size,'meshes':len(meshes),'triangles':triangles,'bounds_min':low,'bounds_max':high,'dimensions_xyz_m':dims,'materials':mats,'wheel_pivots':len(wheels),'exhaust_sockets':len(sockets)}
# Reopen the authored source and guard the actual defects found during review.
bpy.ops.wm.open_mainfile(filepath=str(root/'Vector_V04.blend'))
bpy.context.view_layer.update()
body=bpy.data.objects['V04 primary body'].evaluated_get(bpy.context.evaluated_depsgraph_get())
hit,co,normal,index=body.ray_cast(Vector((0,1.44,3)),Vector((0,0,-1)))
assert hit and .70<co.z<.90,('Hood must be closed',hit,tuple(co))
for side in [-1,1]:
 hit,co,normal,index=body.ray_cast(Vector((side*.142,-3,.675)),Vector((0,1,0)))
 assert hit and co.y>-2.10,('Exhaust must be hollow through tail',tuple(co))
# Guard actual fascia and quarter-intake depth, not just black surface colour.
for x in [0,.82,-.82]:
 hit,co,normal,index=body.ray_cast(Vector((x,3,.40)),Vector((0,-1,0)))
 assert hit and co.y<2.20,('Front inlet must have shell clearance',x,tuple(co))
for side in [-1,1]:
 hit,co,normal,index=body.ray_cast(Vector((side*3,-.90,.62)),Vector((-side,0,0)))
 assert hit and abs(co.x)<.88,('Side inlet must cut into shell',side,tuple(co))
# The two visible LED strips must not be occluded by their housing.
for side in [-1,1]:
 hit,loc,normal,index,obj,matrix=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),Vector((side*.85,-3,.795)),Vector((0,1,0)))
 assert hit and obj.name.startswith('Narrow chevron LED'),('Rear lamp occluded',side,obj.name if obj else None)
for point,prefix in [((0,-.45,3),'Inset roof carbon'),((.82,1.70,3),'Fender recessed extractor'),((-.82,1.70,3),'Fender recessed extractor')]:
 hit,loc,normal,index,obj,matrix=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),Vector(point),Vector((0,0,-1)))
 assert hit and obj.name.startswith(prefix),('Surface detail occluded',point,obj.name if obj else None)
results['surface_details']={'roof_insert_visible':True,'both_fender_extractors_visible':True}
results['rear_lights']={'both_visible':True}
results['inlets']={'front_shell_clearance':True,'side_shell_depth':True}
results['source']={'hood_closed':True,'two_exhaust_bores_open':True,'editable_body_modifiers':len(bpy.data.objects['V04 primary body'].modifiers)}
(root/'export-validation.json').write_text(json.dumps(results,indent=2))
print('VECTOR EXPORTS PASS',json.dumps(results))
