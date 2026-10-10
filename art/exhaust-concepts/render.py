"""Offline exhaust concept comparison; Blender preview, not Unity runtime evidence."""
import bpy, math, sys
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2]
out=root/'art/exhaust-concepts'
bpy.ops.wm.open_mainfile(filepath=str(root/'art/candidates/cloudline-c05/Cloudline_C05.blend'))
s=bpy.context.scene
s.render.engine='CYCLES';s.cycles.samples=12;s.cycles.device='GPU'
prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.get_devices()
for device in prefs.devices: device.use=device.type=='METAL'
s.render.resolution_x=960;s.render.resolution_y=620;s.render.resolution_percentage=100
cam=s.camera;cam.location=(5,-10,6);cam.rotation_euler=(Vector((0,-1.1,.5))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=48
def mat(name,c,p):
 m=bpy.data.materials.new(name);m.use_nodes=True
 n=m.node_tree.nodes;n.clear();l=m.node_tree.links
 o=n.new('ShaderNodeOutputMaterial');v=n.new('ShaderNodeVolumePrincipled');v.inputs['Color'].default_value=(*c,1);v.inputs['Emission Color'].default_value=(*c,1)
 tex=n.new('ShaderNodeTexNoise');tex.noise_dimensions='4D';tex.inputs['Scale'].default_value=8;tex.inputs['Detail'].default_value=2;tex.inputs['W'].default_value=1.8
 coord=n.new('ShaderNodeTexCoord');l.new(coord.outputs['Generated'],tex.inputs['Vector'])
 mult=n.new('ShaderNodeMath');mult.operation='MULTIPLY';mult.inputs[1].default_value=p*4;l.new(tex.outputs['Fac'],mult.inputs[0]);l.new(mult.outputs[0],v.inputs['Emission Strength'])
 v.inputs['Density'].default_value=.3;l.new(v.outputs['Volume'],o.inputs['Volume']);return m
warm=mat('orange',(1,.12,.008),4);blue=mat('cyan',(.008,.3,1),5);core=mat('white hot',(.6,.85,1),8)
def jet(x,length,radius,m,phase):
 vs=[];fs=[];rings=32;segs=16
 for j in range(rings+1):
  t=j/rings;r=max(.0005,radius*(1-t)**.7*(.8+.2*math.sin(t*28+phase)))
  for i in range(segs):
   a=i*math.tau/segs;vs.append((x+r*math.cos(a),-2.405-length*t,.7+r*math.sin(a)))
 for j in range(rings):
  for i in range(segs):
   k=j*segs+i;z=j*segs+(i+1)%segs;fs.append((k,z,z+segs,k+segs))
 fs.append(tuple(range(segs-1,-1,-1)));fs.append(tuple(range(rings*segs,(rings+1)*segs)))
 mesh=bpy.data.meshes.new('jet');mesh.from_pydata(vs,[],fs);mesh.materials.append(m);ob=bpy.data.objects.new('concept jet',mesh);s.collection.objects.link(ob);return ob
for variant,gas,nitro,radius in [('1-dense',1.05,2.8,.19),('2-wide',1.3,3.5,.28)]:
 for mode,length in [('gas',gas),('nitro',nitro)]:
  obs=[]
  for x in [-.74,.74]:
   r=radius*(1 if mode=='gas' else 1.25)
   obs += [jet(x,length,r,warm if mode=='gas' else blue,x),jet(x,length*.85,r*.48,core,x)]
  s.render.filepath=str(out/f'{variant}-{mode}.png');bpy.ops.render.render(write_still=True)
  for ob in obs:bpy.data.objects.remove(ob,do_unlink=True)
