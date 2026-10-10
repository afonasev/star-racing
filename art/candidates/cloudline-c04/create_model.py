"""C04: authored interpretation of user-supplied orbital silver/cyan car; Blender 5.2.
Run: blender -b --python create_model.py -- /absolute/output-directory
Coordinates: X right, Y forward, Z up; metres. No external assets needed.
"""
import bpy, math, sys, json
from pathlib import Path
from mathutils import Vector
out=Path(sys.argv[sys.argv.index('--')+1]) if '--' in sys.argv else Path(__file__).parent
out.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for d in list(bpy.data.materials): bpy.data.materials.remove(d)
scene=bpy.context.scene
car=bpy.data.collections.new('C04 • MODEL'); scene.collection.children.link(car)
studio=bpy.data.collections.new('STUDIO • not exported'); scene.collection.children.link(studio)
def mat(name,c,metal=0,rough=.35,emit=0):
 m=bpy.data.materials.new(name); m.diffuse_color=(*c,1);m.use_nodes=True
 m.node_tree.nodes.clear();p=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled');output=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(p.outputs['BSDF'],output.inputs['Surface']);p.inputs['Base Color'].default_value=(*c,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 if emit:p.inputs['Emission Color'].default_value=(*c,1);p.inputs['Emission Strength'].default_value=emit
 return m
white=mat('Liquid_Silver',(.39,.47,.57),.78,.28)
edge=mat('Satin_Titanium',(.25,.29,.33),.82,.25)
carbon=mat('Graphite_Aero',(.018,.025,.032),.48,.3)
glass=mat('Midnight_Glass',(.008,.018,.028),.28,.18)
rubber=mat('Tire_Rubber',(.012,.014,.017),0,.78)
rim=mat('Dark_Forged_Rim',(.075,.09,.115),.90,.26)
brake=mat('Brake_Ceramic',(.12,.14,.16),.65,.65)
amber=mat('Thin_Red_Taillight',(.8,.003,.008),.1,.23,2)
ice=mat('Cyan_Energy',(.001,.20,1),.1,.2,3)
caliper=mat('Titanium_Caliper',(.20,.24,.29),.65,.3)
def link(obj,collection=car):
 for c in list(obj.users_collection):c.objects.unlink(obj)
 collection.objects.link(obj);return obj
def mesh(name,verts,faces,material,bevel=0):
 data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
 obj=bpy.data.objects.new(name,data);car.objects.link(obj);obj.data.materials.append(material)
 # Consistent outward normals on authored solids.
 bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT');obj.select_set(False)
 if bevel:
  mod=obj.modifiers.new('Machined edge','BEVEL');mod.width=bevel*1.7;mod.segments=4
  mod=obj.modifiers.new('Panel normals','WEIGHTED_NORMAL');mod.keep_sharp=True
 return obj
def box(name,loc,scale,material,bevel=.015):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=link(bpy.context.object);o.name=name;o.dimensions=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(material)
 if bevel: m=o.modifiers.new('Edge radius','BEVEL');m.width=bevel;m.segments=5;o.modifiers.new('Normals','WEIGHTED_NORMAL')
 o.select_set(False);return o
def beam(name,a,b,width,depth,material):
 o=box(name,(Vector(a)+Vector(b))/2,(width,depth,(Vector(b)-Vector(a)).length),material,.008);o.rotation_euler=(Vector(b)-Vector(a)).to_track_quat('Z','Y').to_euler();return o
def loft(name,stations,material):
 # each cross-section: y, bottom half width, top half width, bottom z, shoulder z, ridge z
 vs=[]
 for y,wb,wt,zb,zs,zt in stations:
  vs += [(-wb,y,zb),(-wt,y,zs),(-wt*.70,y,zt),(wt*.70,y,zt),(wt,y,zs),(wb,y,zb)]
 fs=[tuple(range(5,-1,-1))]
 for i in range(len(stations)-1):
  for j in range(6):fs.append((i*6+j,i*6+(j+1)%6,(i+1)*6+(j+1)%6,(i+1)*6+j))
 fs.append(tuple(range((len(stations)-1)*6,len(stations)*6)))
 return mesh(name,vs,fs,material,.014)
def plate(name,pts,thickness,material):
 n=len(pts); vs=pts+[(x,y,z-thickness) for x,y,z in pts];return mesh(name,vs,[tuple(range(n)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],material,.008)
def soft_volume(name,stations,material,exponent=.65):
 vs=[];n=48
 for y,w,center,height in stations:
  for i in range(n):
   a=2*math.pi*i/n;c=math.cos(a);t=math.sin(a)
   vs.append((w*math.copysign(abs(c)**exponent,c),y,center+height*math.copysign(abs(t)**exponent,t)))
 fs=[tuple(range(n-1,-1,-1))]
 for j in range(len(stations)-1):
  for i in range(n):fs.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
 fs.append(tuple(range((len(stations)-1)*n,len(stations)*n)))
 o=mesh(name,vs,fs,material)
 for f in o.data.polygons:f.use_smooth=True
 sub=o.modifiers.new('Continuous rounded body','SUBSURF');sub.levels=1;sub.render_levels=1
 return o
def ribbon(name,points,radius,material):
 # Smooth 3D curve, converted to actual mesh before export.
 data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=16;data.bevel_depth=radius;data.bevel_resolution=4
 spline=data.splines.new('BEZIER');spline.bezier_points.add(len(points)-1)
 for bp,co in zip(spline.bezier_points,points):bp.co=co;bp.handle_left_type='AUTO';bp.handle_right_type='AUTO'
 o=bpy.data.objects.new(name,data);car.objects.link(o);data.materials.append(material)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH');o.select_set(False)
 return o
# New reference: low continuous silver body, inset dark canopy, broad open tail.
loft('Low carbon chassis',[(-1.86,.73,.79,.20,.62,.75),(-1.6,.69,.72,.19,.65,.83),(-.5,.65,.75,.19,.69,.82),(.65,.63,.69,.20,.66,.76),(1.8,.65,.70,.22,.65,.72),(2.45,.75,.85,.23,.40,.51)],carbon)
# Smoothed longitudinal interpolation, zero slope at sculpted section stations.
def profile(stations,y):
 for i in range(len(stations)-1):
  if stations[i][0]<=y<=stations[i+1][0]:
   t=(y-stations[i][0])/(stations[i+1][0]-stations[i][0]);t=t*t*(3-2*t)
   return stations[i][1]*(1-t)+stations[i+1][1]*t
 return stations[0][1] if y<stations[0][0] else stations[-1][1]
# One flowing shell per side: shoulder surface and outer skin cut around real wheels.
for sign in [-1,1]:
 vs=[];n=120
 for i in range(n+1):
  y=-2.48+i*4.93/n
  outer=profile([(-2.48,1.19),(-1.55,1.27),(-.6,1.14),(.4,1.08),(1.43,1.22),(2.45,1.02)],y)
  top=profile([(-2.48,.97),(-2.10,1.08),(-1.48,1.155),(-.6,.94),(.35,.86),(1.43,1.125),(2.45,.65)],y)
  inner=profile([(-2.48,.82),(-1.5,.64),(-.4,.71),(.6,.66),(1.5,.61),(2.45,.70)],y)
  lower=.24
  for wheel_y in [-1.48,1.43]:
   delta=abs(y-wheel_y)
   if delta<.554:lower=max(lower,.515+math.sqrt(.554**2-delta**2))
  top=max(top,lower+.105)
  section=[(inner,top-.13),(inner+.07,top-.055),(outer-.16,top),(outer-.045,top-.017),(outer,top-.075),(outer,lower+.025),(outer-.035,lower)]
  vs.extend((sign*x,y,z) for x,z in section)
 fs=[(i*7+j,i*7+j+1,(i+1)*7+j+1,(i+1)*7+j) for i in range(n) for j in range(6)]
 o=mesh('Continuous silver haunch '+str(sign),vs,fs,white)
 for f in o.data.polygons:f.use_smooth=True
 sub=o.modifiers.new('Flowing shoulder','SUBSURF');sub.levels=1;sub.render_levels=1
 solid=o.modifiers.new('Body skin thickness','SOLIDIFY');solid.thickness=.024
 # Engine bay flying buttresses, integrated low into the body.
 points=[(sign*.44,-.68,1.105),(sign*.55,-1.14,1.04),(sign*.69,-1.68,1.01),(sign*.91,-2.18,1.04)]
 # C04 rear cabin panels replace the raised tubular buttresses.
 # Sculpted lower door with recessed black air channel.
 plate('Silver door '+str(sign),[(sign*.73,-.75,.94),(sign*.72,.68,.88),(sign*.93,.83,.52),(sign*.97,-.57,.44)],.045,white)
 mesh('Recessed side intake '+str(sign),[(sign*1.02,-.84,.83),(sign*.91,-.38,.82),(sign*.98,-.20,.38),(sign*1.10,-.74,.36)],[(0,1,2,3)],carbon,.015)
 ribbon('Lower silver sill '+str(sign),[(sign*1.15,-.91,.28),(sign*1.09,-.2,.25),(sign*1.07,.8,.29)],.066,edge)
 ribbon('Cyan sill inset '+str(sign),[(sign*1.152,-.74,.32),(sign*1.127,-.25,.30),(sign*1.095,.43,.32)],.017,ice)
 ribbon('Door seam '+str(sign),[(sign*.765,.58,.91),(sign*.887,.55,.67),(sign*.99,.47,.39)],.006,carbon)
 # Tall curved trailing fender housings. Substantial silver fins wrap the rear wheels.
 tail=[(sign*1.15,-2.495,.23),(sign*1.19,-2.495,.48),(sign*1.22,-2.455,.76),(sign*1.20,-2.34,.99),(sign*1.16,-2.17,1.075)]
 ribbon('Rear edge black recess '+str(sign),tail,.084,carbon)
 ribbon('Cyan vertical rear signature '+str(sign),[(x,y-.105,z) for x,y,z in tail],.016,ice)
 ribbon('Rear outer silver edge '+str(sign),[(x+sign*.075,y+.005,z) for x,y,z in tail],.032,white)
 # Slim red tail strip beneath the broad rear bridge.
 ribbon('Thin red rear light '+str(sign),[(sign*.33,-2.362,.941),(sign*.66,-2.397,.962),(sign*1.07,-2.357,.995)],.009,amber)
# C04: one analytic glass surface with flush roof/pillar patches.
# All skins sample the same function: no separately inflated blobs or tubular rails.
def bezier(values,t):
 return values[0]*(1-t)**3+3*values[1]*(1-t)**2*t+3*values[2]*(1-t)*t*t+values[3]*t**3

def cabin_point(t,u,offset=0):
 y=-1.04+2.05*t
 width=bezier([.45,.73,.70,.48],t)
 base=bezier([.86,.88,.83,.79],t)
 ridge=bezier([.96,1.47,1.27,.82],t)
 crown=max(0,math.cos(math.pi*u/2))**.64
 return (width*u,y,base+(ridge-base)*crown+offset)

def cabin_patch(name,t0,t1,u0,u1,material,offset=0,nt=40,nu=36):
 vs=[cabin_point(t0+(t1-t0)*i/nt,u0+(u1-u0)*j/nu,offset) for i in range(nt+1) for j in range(nu+1)]
 fs=[(i*(nu+1)+j,i*(nu+1)+j+1,(i+1)*(nu+1)+j+1,(i+1)*(nu+1)+j) for i in range(nt) for j in range(nu)]
 obj=mesh(name,vs,fs,material)
 for face in obj.data.polygons:face.use_smooth=True
 solid=obj.modifiers.new('Thin cabin skin','SOLIDIFY');solid.thickness=.004;solid.offset=-1
 return obj
cabin_patch('Cabin continuous glazing',0,1,-1,1,glass)
cabin_patch('Cabin flush roof',.235,.51,-.72,.72,white,.004,18,28)
# Slender flush A/B pillars reach to the beltline, without raised pipes.
for t,name in [(.23,'rear'),(.51,'front')]:
 cabin_patch('Cabin '+name+' cross frame',t-.007,t+.007,-1,1,white,.010,2,72)
for sign in [-1,1]:
 cabin_patch('Cabin flush beltline '+str(sign),0,1,sign*.97,sign,edge,.003,40,2)
 # Flat flying buttress connects the cabin's rear edge to the existing engine deck.
 pts=[(sign*.43,-.78,1.025),(sign*.54,-.88,1.055),(sign*.96,-2.18,1.04),(sign*.79,-2.15,1.018)]
 plate('Rear cabin panel '+str(sign),pts,.026,white)
# Black rear engine basin with two slim cyan accents and ribbed vents.
soft_volume('Engine cover',[(-2.28,.46,.84,.035),(-1.88,.46,.88,.05),(-1.43,.43,.93,.055),(-1.03,.31,1.00,.04)],carbon,.72)
for sign in [-1,1]:
 ribbon('Engine cyan line '+str(sign),[(sign*.14,-1.9,.94),(sign*.16,-1.56,.995),(sign*.18,-1.31,1.021)],.014,ice)
 for i in range(5):
  y=-2.00+i*.125
  box('Engine vent',(sign*.33,y,.94+i*.012),(.16,.039,.018),edge,.008)
# Broad silver tail bridge: a continuous swept blade with an open space below.
verts=[];n=40
for i in range(n+1):
 x=-1.18+2.36*i/n;t=abs(x)/1.18;y=-2.29+.07*(1-t*t);z=1.038+.044*t*t
 verts.extend([(x,y-.12,z),(x,y+.18,z+.018),(x,y+.17,z-.040),(x,y-.13,z-.055)])
faces=[(i*4+j,i*4+(j+1)%4,(i+1)*4+(j+1)%4,(i+1)*4+j) for i in range(n) for j in range(4)]+[(3,2,1,0),tuple(range(n*4,n*4+4))]
o=mesh('Swept full-width rear bridge',verts,faces,white,.018)
for f in o.data.polygons:f.use_smooth=True
# Cyan central panel under the bridge, dark hollow below (no silver rear bumper).
box('Central cyan panel housing',(0,-2.40,.80),(.79,.11,.148),carbon,.054)
box('Central cyan panel',(0,-2.465,.80),(.67,.022,.072),ice,.031)
box('Centre panel separator',(0,-2.481,.80),(.009,.012,.08),edge,.004)
# Open diffuser tunnels and articulated mechanical struts.
plate('Recessed diffuser floor',[(-.80,-2.43,.24),(.80,-2.43,.24),(.69,-1.66,.20),(-.69,-1.66,.20)],.025,carbon)
for x in [-.68,-.34,0,.34,.68]:
 mesh('Deep diffuser vane',[(x-.012,-2.45,.23),(x-.012,-2.23,.63),(x-.012,-1.78,.25),(x+.012,-2.45,.23),(x+.012,-2.23,.63),(x+.012,-1.78,.25)],[(0,1,2),(3,5,4),(0,3,4,1),(1,4,5,2),(2,5,3,0)],carbon,.006)
for sign in [-1,1]:
 beam('Rear suspension link',(sign*.32,-2.29,.59),(sign*.91,-1.91,.32),.046,.046,edge)
 beam('Rear upper suspension',(sign*.28,-2.18,.69),(sign*.85,-1.76,.70),.036,.036,edge)
# Front reconstructed conservatively as a low silver wedge with blended corners.
nose=soft_volume('Low silver nose',[(.76,.59,.70,.11),(1.10,.65,.66,.19),(1.65,.71,.62,.23),(2.10,.86,.56,.22),(2.35,.94,.52,.17),(2.49,.82,.49,.12),(2.55,.54,.485,.06),(2.565,.025,.485,.003)],white,.69)
soft_volume('Front aero lip',[(2.02,.78,.25,.024),(2.36,1.0,.26,.027),(2.54,.87,.265,.021),(2.55,.51,.27,.015),(2.56,.01,.27,.001)],carbon,.72)
for sign in [-1,1]:
 bpy.context.view_layer.update();ev=nose.evaluated_get(bpy.context.evaluated_depsgraph_get());pts=[]
 for x,y in [(sign*.36,2.35),(sign*.51,2.30),(sign*.68,2.20),(sign*.76,2.10)]:
  hit,co,normal,index=ev.ray_cast(Vector((x,y,3)),Vector((0,0,-1)));assert hit
  pts.append((co.x,co.y,co.z+.009))
 ribbon('Front light recess '+str(sign),pts,.026,carbon)
 ribbon('Front cyan blade '+str(sign),[(x,y,z+.021) for x,y,z in pts],.008,ice)
 box('Low side camera '+str(sign),(sign*.79,.53,.895),(.12,.16,.058),edge,.024)
# Wheels: radial profile mesh gives a proper sidewall, tread and rim well.
def revolve(name,x,y,profile,material,n=64):
 vs=[]
 for dx,r in profile:
  for i in range(n):
   a=2*math.pi*i/n;vs.append((x+dx,y+r*math.sin(a),.515+r*math.cos(a)))
 fs=[]
 for j in range(len(profile)):
  for i in range(n):fs.append((j*n+i,j*n+(i+1)%n,((j+1)%len(profile))*n+(i+1)%n,((j+1)%len(profile))*n+i))
 o=mesh(name,vs,fs,material)
 for p in o.data.polygons:p.use_smooth=True
 return o
for s in [-1,1]:
 for y,tag in [(-1.48,'Rear'),(1.43,'Front')]:
  x=s*1.08; parts_before=set(car.objects)
  revolve(tag+' tire '+str(s),x,y,[(-.155,.36),(-.19,.42),(-.18,.48),(-.14,.512),(.14,.512),(.18,.48),(.19,.42),(.155,.36)],rubber)
  ox=x+s*.188
  revolve(tag+' rim lip '+str(s),ox,y,[(-.055,.37),(-.045,.411),(.009,.412),(.020,.39),(.02,.369)],rim)
  revolve(tag+' rim barrel '+str(s),x,y,[(-.14,.365),(.14,.365),(.14,.381),(-.14,.381)],carbon)
  revolve(tag+' brake rotor '+str(s),x+s*.10,y,[(-.012,.08),(-.012,.32),(.012,.32),(.012,.08)],brake,48)
  box(tag+' copper caliper '+str(s),(x+s*.123,y+.23,.54),(.07,.11,.26),caliper,.027)
  for i in range(5):
   a=i*2*math.pi/5
   # Swept paired spokes with open gaps.
   for delta in [-.075,.075]:
    p1=(ox+s*.021,y+math.sin(a+delta)*.083,.515+math.cos(a+delta)*.083)
    p2=(ox-s*.016,y+math.sin(a+.24+delta)*.377,.515+math.cos(a+.24+delta)*.377)
    beam(tag+' split spoke',p1,p2,.038,.038,rim)
  revolve(tag+' hub '+str(s),ox+s*.024,y,[(-.02,.0),(-.02,.096),(.025,.096),(.025,0)],edge,32)
  for i in range(5):
   a=i*2*math.pi/5;box('Lug',(ox+s*.052,y+math.sin(a)*.061,.515+math.cos(a)*.061),(.014,.017,.017),rim,.004)
  # Circumferential tread channels embedded near the outer tire surface.
  for d in [-.085,0,.085]:revolve('Tread channel',x+d,y,[(-.004,.512),(.004,.512),(.004,.514),(-.004,.514)],carbon,64)
  # Local wheel pivot for later steering/spin, no rig applied to game.
  pivot=bpy.data.objects.new('Wheel_'+tag+('_L' if s<0 else '_R'),None);car.objects.link(pivot);pivot.location=(x,y,.515);bpy.context.view_layer.update()
  for obj in set(car.objects)-parts_before-{pivot}:
   mw=obj.matrix_world.copy();obj.parent=pivot;obj.matrix_world=mw
# Bake bevels and triangulation only in exports; keep source modifiers editable.
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
bpy.ops.object.select_all(action='DESELECT')
for o in car.objects:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(out/'Cloudline_C04.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,use_mesh_modifiers=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(out/'Cloudline_C04.glb'),use_selection=True,export_format='GLB',export_apply=True)
# Neutral studio; excluded from exports.
bpy.ops.object.select_all(action='DESELECT')
floor=box('Studio ground',(0,0,-.055),(200,200,.08),mat('Studio floor',(.10,.14,.18),.28,.32),0);link(floor,studio)
def light(name,loc,power,size,color):
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(name,d);studio.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.6))-o.location).to_track_quat('-Z','Y').to_euler()
light('Key softbox',(1,-3,7),1700,5,(.83,.91,1));light('Cool rim',(-4,1,4),1400,4,(.68,.80,1));light('Front fill',(4,5,4),1600,5,(.69,.83,1));light('Tail bounce',(0,-5,2),250,3,(1,1,1))
world=scene.world or bpy.data.worlds.new('World');scene.world=world;world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.16,.20,.27,1);world.node_tree.nodes['Background'].inputs[1].default_value=.35
camdata=bpy.data.cameras.new('Review camera');cam=bpy.data.objects.new('Review camera',camdata);studio.objects.link(cam);scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=1440;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
# Count final evaluated triangles without studio.
deps=bpy.context.evaluated_depsgraph_get();stats={'candidate':'C04','seed':'deterministic authored geometry','units':'metres','source_axis':'+Y forward, +Z up','objects':0,'triangles':0,'materials':[m.name for m in [white,edge,carbon,glass,rubber,rim,brake,amber,ice,caliper]]}
for obj in car.objects:
 if obj.type=='MESH':
  ev=obj.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles();stats['objects']+=1;stats['triangles']+=len(me.loop_triangles);ev.to_mesh_clear()
(out/'model-stats.json').write_text(json.dumps(stats,indent=2))
views={'rear-three-quarter':((6,-8,2.50),(0,-.2,.65),55),'front-three-quarter':((6.5,8,3.0),(0,.1,.65),55),'side':((8.6,0,1.8),(0,0,.64),52),'rear':((0,-8,1.8),(0,-.5,.64),55),'cabin-detail':((3.2,3.6,2.55),(0,-.12,1.02),85)}
for name,(loc,target,lens) in views.items():
 cam.location=loc;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();camdata.lens=lens;scene.render.filepath=str(out/(name+'.png'))
 if name=='rear-three-quarter':bpy.ops.wm.save_as_mainfile(filepath=str(out/'Cloudline_C04.blend'))
 bpy.ops.render.render(write_still=True)
print('C04 COMPLETE',json.dumps(stats))
