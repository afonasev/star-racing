"""Blender 5.2: bake C05 and batch by material; keep original art source untouched.
blender -b --python tools/art/export_cloudline.py -- /absolute/repo
"""
import bpy,sys,json,hashlib
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]);source=root/'art/candidates/cloudline-c05/Cloudline_C05.blend'
bpy.ops.wm.open_mainfile(filepath=str(source));car=bpy.data.collections['C05 • MODEL']
deps=bpy.context.evaluated_depsgraph_get();groups={}
for obj in list(car.objects):
 if obj.type!='MESH':continue
 ev=obj.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles();name=me.materials[0].name
 vs,fs,ns=groups.setdefault(name,([],[],[]));offset=len(vs)
 vs.extend(tuple(obj.matrix_world@v.co) for v in me.vertices)
 fs.extend(tuple(offset+i for i in t.vertices) for t in me.loop_triangles)
 normal_matrix=obj.matrix_world.to_3x3().inverted().transposed()
 ns.extend(tuple((normal_matrix@me.corner_normals[i].vector).normalized()) for t in me.loop_triangles for i in t.loops);ev.to_mesh_clear()
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
for name,(vs,fs,ns) in groups.items():
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],fs);me.update();ob=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(ob)
 for face in me.polygons:face.use_smooth=True
 me.normals_split_custom_set(ns)
 me.materials.append(bpy.data.materials[name])
 # Preserve evaluated corner normals; batching does not weld or simplify the authored geometry.
for sign in [-1,1]:
 o=bpy.data.objects.new('Exhaust_L' if sign<0 else 'Exhaust_R',None);bpy.context.scene.collection.objects.link(o);o.location=(sign*.74,-2.43,.70)
bpy.ops.object.select_all(action='SELECT')
out=root/'unity-prototype/Assets/StarRacing/Resources/Vehicle';out.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.fbx(filepath=str(out/'Cloudline.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,use_mesh_modifiers=True,add_leaf_bones=False)
(root/'art/candidates/cloudline-c05-runtime.json').write_text(json.dumps({'source_commit':'c62eb884272b778433e7fdb28b0edf293a5fb339','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'mesh_count':len(groups),'unity_import_triangles':121644,'zero_area_triangles_removed_by_unity':668,'triangles':sum(len(f) for v,f,n in groups.values()),'material_names':sorted(groups),'fbx_sha256':hashlib.sha256((out/'Cloudline.fbx').read_bytes()).hexdigest()},indent=2)+'\n')
print('CLOUDLINE_RUNTIME_EXPORT_OK',len(groups),sum(len(f) for v,f,n in groups.values()))
