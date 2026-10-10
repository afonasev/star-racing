"""Deterministic offline paint/exhaust studies. Run in Blender after create_model.py.
Flames live only in Cloudline_C05_Studies.blend, never FBX/GLB car exports.
"""
import bpy, math, json, sys
from pathlib import Path
from mathutils import Vector
out=Path(sys.argv[sys.argv.index('--')+1])
bpy.ops.wm.open_mainfile(filepath=str(out/'Cloudline_C05.blend'))
scene=bpy.context.scene;cam=scene.camera
scene.cycles.samples=32
prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='METAL';prefs.get_devices()
for device in prefs.devices:device.use=(device.type=='METAL')
scene.cycles.device='GPU'
scene.render.resolution_x=1200;scene.render.resolution_y=830
paint=bpy.data.materials['Liquid_Silver'];bsdf=next(n for n in paint.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
presets={'01-silver':(.39,.47,.57),'02-graphite':(.030,.039,.050),'03-pearl':(.82,.85,.89),'04-crimson':(.40,.008,.016),'05-cobalt':(.008,.055,.45),'06-gold':(.72,.32,.015)}
def render(name):
 scene.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
for name,color in presets.items():
 bsdf.inputs['Base Color'].default_value=(*color,1);paint.diffuse_color=(*color,1)
 render('paint-'+name)
bsdf.inputs['Base Color'].default_value=(*presets['01-silver'],1);paint.diffuse_color=(*presets['01-silver'],1)
effects=bpy.data.collections.new('EXHAUST STUDY • preview only');scene.collection.children.link(effects)
def volume_material(name,color,strength,seed):
 m=bpy.data.materials.new(name);m.use_nodes=True;n=m.node_tree.nodes;n.clear();l=m.node_tree.links
 output=n.new('ShaderNodeOutputMaterial');v=n.new('ShaderNodeVolumePrincipled');v.inputs['Color'].default_value=(*color,1);v.inputs['Emission Color'].default_value=(*color,1)
 tex=n.new('ShaderNodeTexNoise');tex.noise_dimensions='4D';tex.inputs['Scale'].default_value=7;tex.inputs['Detail'].default_value=3;tex.inputs['Roughness'].default_value=.65;tex.inputs['W'].default_value=seed
 coord=n.new('ShaderNodeTexCoord');l.new(coord.outputs['Generated'],tex.inputs['Vector'])
 ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.30;ramp.color_ramp.elements[1].position=.68;l.new(tex.outputs['Fac'],ramp.inputs[0])
 mult=n.new('ShaderNodeMath');mult.operation='MULTIPLY';mult.inputs[1].default_value=strength;l.new(ramp.outputs[0],mult.inputs[0]);l.new(mult.outputs[0],v.inputs['Emission Strength'])
 density=n.new('ShaderNodeMath');density.operation='MULTIPLY';density.inputs[1].default_value=2;l.new(ramp.outputs[0],density.inputs[0]);l.new(density.outputs[0],v.inputs['Density']);l.new(v.outputs['Volume'],output.inputs['Volume'])
 return m
# Closed irregular tapered volumes: short warm throttle plume and longer blue nitro jet.
def plume(name,x,length,radius,material,phase):
 vs=[];rings=48;segments=24
 for j in range(rings+1):
  t=j/rings
  r=radius*((1-t)**.80)*(.66+.56*math.sin(math.pi*min(1,t*2)))
  r=max(.0003,r)
  for i in range(segments):
   a=2*math.pi*i/segments
   rr=r*(1+.13*math.sin(5*a+17*t+phase)+.08*math.sin(9*a-35*t))
   vs.append((x+rr*math.cos(a)+.014*t*math.sin(t*19+phase),-2.405-length*t,.70+rr*math.sin(a)+.02*t*math.sin(t*12)))
 fs=[tuple(range(segments-1,-1,-1))]
 fs += [(j*segments+i,j*segments+(i+1)%segments,(j+1)*segments+(i+1)%segments,(j+1)*segments+i) for j in range(rings) for i in range(segments)]
 fs.append(tuple(range(rings*segments,(rings+1)*segments)))
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(name,me);effects.objects.link(o);me.materials.append(material)
 return o
sets={}
for mode,length,radius,color,strength in [('gas',.55,.075,(1,.12,.012),10),('nitro',1.80,.098,(.008,.22,1),16)]:
 outer=volume_material(mode+' turbulent plume',color,strength,1.8)
 core=volume_material(mode+' hot core',(.25,.65,1) if mode=='gas' else (.48,.86,1),30,3.4)
 objs=[]
 for sign in [-1,1]:
  objs.append(plume(mode+' outer '+str(sign),sign*.74,length,radius,outer,sign*.5))
  objs.append(plume(mode+' core '+str(sign),sign*.74,length*.68,radius*.47,core,sign*.5))
  d=bpy.data.lights.new(mode+' exhaust glow','POINT');d.energy=12 if mode=='gas' else 35;d.color=color;d.shadow_soft_size=.18
  o=bpy.data.objects.new(mode+' exhaust glow '+str(sign),d);effects.objects.link(o);o.location=(sign*.74,-2.65,.70);objs.append(o)
 sets[mode]=objs
 for o in objs:o.hide_render=True
# Both states use the same camera, framing and silver material.
cam.location=(6,-9.7,3.5);cam.rotation_euler=(Vector((0,-.95,.63))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=53
scene.render.resolution_x=1440;scene.render.resolution_y=1000
for mode,objs in sets.items():
 for o in objs:o.hide_render=False
 render('exhaust-'+mode)
 for o in objs:o.hide_render=True
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Cloudline_C05_Studies.blend'))
(out/'studies.json').write_text(json.dumps({'seed':'deterministic geometry and noise W=1.8/3.4','paint_linear_rgb':presets,'exhaust':{'gas':{'length_m':.55,'color':'warm orange with blue core'},'nitro':{'length_m':1.8,'color':'blue/cyan with pale core'}},'effects_exported':False,'gameplay_integrated':False},indent=2))
print('STUDIES COMPLETE')
