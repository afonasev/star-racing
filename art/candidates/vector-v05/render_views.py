"""Re-render named cameras from the packed review blend without rebuilding geometry.
blender -b Vector_CloudCity_Review.blend --python render_views.py -- OUTPUT [VIEW ...]
"""
import bpy, sys, math
from pathlib import Path
args=sys.argv[sys.argv.index('--')+1:];out=Path(args[0]);out.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene
draft='--draft' in args;clay='--clay' in args
scene.render.resolution_x=1440 if draft else 1920;scene.render.resolution_percentage=100
scene.cycles.samples=24 if draft else 48;scene.cycles.use_denoising=True
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.get_devices()
 for device in prefs.devices:device.use=device.type=='METAL'
 if any(device.type=='METAL' for device in prefs.devices):scene.cycles.device='GPU'
except Exception:pass
names=[a for a in args[1:] if not a.startswith('--')] or sorted(o.name for o in scene.objects if o.type=='CAMERA' and o.name[:2].isdigit())
if clay:
 mat=bpy.data.materials.new('Neutral clay review');mat.diffuse_color=(.45,.47,.50,1);mat.use_nodes=True
 p=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
 if p is None:
  p=mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled');output=next((n for n in mat.node_tree.nodes if n.type=='OUTPUT_MATERIAL'),None) or mat.node_tree.nodes.new('ShaderNodeOutputMaterial');mat.node_tree.links.new(p.outputs['BSDF'],output.inputs['Surface'])
 p.inputs['Base Color'].default_value=(.45,.47,.50,1);p.inputs['Roughness'].default_value=.55
 for obj in bpy.data.collections['VECTOR • canonical car'].objects:
  for slot in obj.material_slots:slot.link='OBJECT';slot.material=mat
for name in names:
 scene.camera=bpy.data.objects[name]
 if name=='07-chase':
  scene.camera.data.sensor_fit='VERTICAL';scene.camera.data.sensor_height=24;scene.camera.data.lens=12/math.tan(math.radians(65/2))
  bpy.data.collections['RIVALS • shared geometry'].hide_render=False
  color=(.018,.12,.75,1);scene.render.resolution_y=int(scene.render.resolution_x*9/16)
 else:
  bpy.data.collections['RIVALS • shared geometry'].hide_render=True
  color=(.98,.60,.018,1);scene.render.resolution_y=int(scene.render.resolution_x*5/8)
 bpy.data.materials['PlayerPaint'].node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=color
 scene.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
