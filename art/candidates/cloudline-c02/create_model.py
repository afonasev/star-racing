"""C02: authored interpretation of Cloudline menu car; Blender 5.2.
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
car=bpy.data.collections.new('C02 • MODEL'); scene.collection.children.link(car)
studio=bpy.data.collections.new('STUDIO • not exported'); scene.collection.children.link(studio)
def mat(name,c,metal=0,rough=.35,emit=0):
 m=bpy.data.materials.new(name); m.diffuse_color=(*c,1);m.use_nodes=True
 m.node_tree.nodes.clear();p=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled');output=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(p.outputs['BSDF'],output.inputs['Surface']);p.inputs['Base Color'].default_value=(*c,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 if emit:p.inputs['Emission Color'].default_value=(*c,1);p.inputs['Emission Strength'].default_value=emit
 return m
white=mat('Pearl_White',(.83,.87,.90),.58,.24)
edge=mat('Satin_Titanium',(.25,.29,.33),.82,.25)
carbon=mat('Graphite_Aero',(.018,.025,.032),.48,.3)
glass=mat('Midnight_Glass',(.008,.025,.041),.72,.15)
rubber=mat('Tire_Rubber',(.012,.014,.017),0,.78)
rim=mat('Forged_Aluminium',(.47,.53,.58),.9,.22)
brake=mat('Brake_Ceramic',(.12,.14,.16),.65,.65)
amber=mat('Amber_Taillight',(1,.075,.005),.1,.23,3)
ice=mat('Ice_Headlight',(.48,.82,1),.1,.2,4)
caliper=mat('Copper_Caliper',(.65,.17,.025),.65,.3)
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
# Narrow core leaves four genuine wheel openings; low, wide, cab-forward silhouette.
loft('Carbon monocoque',[(-2.38,.79,.83,.28,.67,.78),(-1.75,.75,.82,.25,.77,.94),(-.65,.78,.90,.23,.74,.94),(.6,.72,.83,.23,.67,.83),(1.6,.69,.78,.24,.69,.8),(2.35,.82,.91,.3,.49,.59)],carbon)
# C02 replaces the thin angular wedge with a closed, thick, smoothly rounded nose.
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
nose=soft_volume('Full rounded front',[(.56,.65,.79,.12),(.65,.70,.78,.15),(.95,.74,.72,.27),(1.4,.77,.69,.34),(1.85,.79,.65,.36),(2.1,.94,.64,.34),(2.29,.995,.635,.305),(2.42,.96,.63,.27),(2.51,.87,.62,.205),(2.575,.67,.62,.105),(2.599,.39,.62,.045),(2.60,.015,.62,.003)],white)
def ribbon(name,points,radius,material):
 # Smooth 3D curve, converted to actual mesh before export.
 data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=16;data.bevel_depth=radius;data.bevel_resolution=4
 spline=data.splines.new('BEZIER');spline.bezier_points.add(len(points)-1)
 for bp,co in zip(spline.bezier_points,points):bp.co=co;bp.handle_left_type='AUTO';bp.handle_right_type='AUTO'
 o=bpy.data.objects.new(name,data);car.objects.link(o);data.materials.append(material)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH');o.select_set(False)
 return o
loft('Rear engine spine',[(-2.22,.37,.38,.71,.96,1.02),(-1.4,.4,.43,.81,1.01,1.08),(-.66,.38,.40,.85,1.14,1.18)],carbon)
# Glass canopy, deliberately polygonal and low.
loft('Canopy glass',[(-1.04,.72,.63,.88,1.14,1.22),(-.47,.73,.57,.95,1.38,1.43),(.30,.65,.53,.88,1.34,1.39),(.99,.7,.65,.78,.91,.96)],glass)
plate('Floating white roof',[(-.51,-.50,1.451),(.51,-.50,1.451),(.48,.27,1.412),(-.48,.27,1.412)],.035,white)
for s in [-1,1]:
 # Pillars and rear flying buttresses.
 beam('A pillar', (s*.49,.28,1.40),(s*.71,1.0,.86),.065,.065,white)
 beam('Roof edge', (s*.51,-.49,1.445),(s*.49,.28,1.415),.07,.065,white)
 plate('Flying buttress',[(s*.5,-.48,1.44),(s*.70,-.66,1.33),(s*.96,-1.93,1.02),(s*.71,-1.83,1.03)],.055,white)
 # Arch shell with broad faceted shoulder and a real opening below.
 for y,tag in [(-1.48,'Rear'),(1.43,'Front')]:
  verts=[]; n=40
  for i in range(n+1):
   a=math.pi*i/n
   for x,r in [(s*1.075,.554),(s*1.13,.62),(s*.96,.72),(s*.77,.69)]: verts.append((x,y+math.cos(a)*r,.515+math.sin(a)*r))
  faces=[]
  for i in range(n):
   for j in range(3):faces.append((i*4+j,i*4+j+1,(i+1)*4+j+1,(i+1)*4+j))
  o=mesh(tag+' sculpted fender '+str(s),verts,faces,white)
  for f in o.data.polygons:f.use_smooth=True
  sub=o.modifiers.new('Soft shoulder curvature','SUBSURF');sub.levels=1;sub.render_levels=1
  # Dark inner arch lip.
  verts=[]
  for i in range(n+1):
   a=math.pi*i/n
   for x,r in [(s*1.08,.538),(s*1.085,.556)]:verts.append((x,y+math.cos(a)*r,.515+math.sin(a)*r))
  mesh(tag+' arch trim '+str(s),verts,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(n)],carbon)
 # Door forms an angular recess ahead of rear wheel; white spear along sill.
 plate('Door skin '+str(s),[(s*.90,-.80,.91),(s*.78,.85,.88),(s*.91,.81,.42),(s*1.00,-.62,.40)],.06,white)
 mesh('Side intake '+str(s),[(s*1.005,-.90,.93),(s*.965,-.35,.82),(s*1.027,-.39,.43),(s*1.075,-.94,.41)],[(0,1,2,3)],carbon)
 plate('Sill blade '+str(s),[(s*1.08,-1.01,.29),(s*1.11,-.77,.38),(s*1.02,.95,.31),(s*.81,1.02,.25),(s*.89,-.9,.23)],.055,white)
 beam('Door cutline '+str(s),(s*.927,-.22,.89),(s*.989,-.11,.42),.011,.011,carbon)
 box('Flush handle '+str(s),(s*.935,-.28,.87),(.014,.15,.026),carbon,.004)
 beam('Mirror stem '+str(s),(s*.73,.66,1.01),(s*.99,.61,1.055),.04,.04,carbon)
 box('Mirror '+str(s),(s*1.055,.60,1.07),(.18,.17,.083),carbon,.026)
 # Rear outer haunch down to bumper (behind wheel opening).
 plate('Rear corner '+str(s),[(s*.96,-1.84,1.02),(s*1.05,-2.13,.94),(s*.98,-2.38,.64),(s*.91,-2.28,.31),(s*.76,-2.19,.32),(s*.79,-2.05,.76)],.065,white)
# Tail: hexagonal black lamp pockets, white upper eyebrows, floating illuminated bars.
for s in [-1,1]:
 pts=[(s*.17,-2.385,.95),(s*.56,-2.405,1.11),(s*.89,-2.34,1.05),(s*1.015,-2.28,.85),(s*.91,-2.34,.62),(s*.48,-2.405,.70)]
 mesh('Recessed tail pocket '+str(s),pts,[tuple(range(6))],carbon)
 beam('Tail eyebrow '+str(s),(s*.20,-2.402,.993),(s*.56,-2.41,1.137),.068,.067,white)
 beam('Tail upper cap '+str(s),(s*.56,-2.41,1.137),(s*.9,-2.35,1.078),.068,.067,white)
 beam('Amber signature '+str(s),(s*.28,-2.427,.965),(s*.86,-2.378,.965),.032,.033,amber)
 beam('Amber outer hook '+str(s),(s*.86,-2.378,.965),(s*.929,-2.337,.874),.025,.025,amber)
 beam('Amber lower line '+str(s),(s*.60,-2.408,.922),(s*.895,-2.365,.922),.018,.02,amber)
# Central tail bridge, valance and diffuser with 7 deep fins.
box('Tail bridge',(0,-2.39,.795),(.72,.12,.13),edge,.04)
box('Centre brake strip',(0,-2.459,.846),(.30,.018,.018),amber,.003)
loft('Lower rear valance',[(-2.36,.81,.84,.25,.51,.62),(-2.17,.76,.81,.29,.62,.69)],carbon)
plate('Diffuser ramp',[(-.73,-2.47,.29),(.73,-2.47,.29),(.66,-1.85,.22),(-.66,-1.85,.22)],.05,carbon)
for x in [-.69,-.46,-.23,0,.23,.46,.69]:
 mesh('Diffuser fin',[(x-.016,-2.46,.26),(x-.016,-2.35,.61),(x-.016,-1.86,.28),(x+.016,-2.46,.26),(x+.016,-2.35,.61),(x+.016,-1.86,.28)],[(0,1,2),(3,5,4),(0,3,4,1),(1,4,5,2),(2,5,3,0)],edge,.007)
for s in [-1,1]:
 box('Exhaust '+str(s),(s*.70,-2.405,.55),(.18,.13,.09),edge,.028)
 box('Exhaust void '+str(s),(s*.70,-2.476,.55),(.13,.012,.046),carbon,.016)
 # Thick nose has curved lamp signatures and an inset rounded intake.
 bpy.context.view_layer.update()
 evaluated=nose.evaluated_get(bpy.context.evaluated_depsgraph_get())
 points=[]
 for x,y in [(s*.38,2.40),(s*.54,2.375),(s*.70,2.31),(s*.83,2.22)]:
  hit,co,normal,index=evaluated.ray_cast(Vector((x,y,3)),Vector((0,0,-1)))
  assert hit, ('Headlight outside nose',x,y)
  points.append((co.x,co.y,co.z+.010))
 ribbon('Headlamp housing '+str(s),points,.027,carbon)
 ribbon('Curved headlamp '+str(s),[(x,y,z+.022) for x,y,z in points],.010,ice)
# Rounded lower intake and splitter follow the front volume instead of straight blades.
soft_volume('Rounded lower intake',[(2.49,.55,.465,.085),(2.58,.65,.465,.085),(2.61,.55,.465,.064),(2.615,.01,.465,.002)],carbon,.60)
soft_volume('Rounded front lip',[(1.98,.78,.28,.035),(2.28,.96,.28,.035),(2.45,.95,.28,.032),(2.57,.76,.285,.027),(2.625,.43,.29,.018),(2.63,.01,.29,.002)],carbon,.70)
# Deck vents and nose panel grooves.
for i in range(7):
 y=-1.1-i*.13
 for s in [-1,1]:box('Engine louvre',(s*.24,y,1.11-(i*.012)),(.28,.052,.026),edge,.005)
# C02 omits the knife-edge hood crease for a continuous front surface.
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
  x=s*.96; parts_before=set(car.objects)
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
for obj in car.objects:
 if obj.type=='MESH' and any(m.type=='BEVEL' for m in obj.modifiers):
  for f in obj.data.polygons:f.use_smooth=True
# Bake bevels and triangulation only in exports; keep source modifiers editable.
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
bpy.ops.object.select_all(action='DESELECT')
for o in car.objects:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(out/'Cloudline_C02.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,use_mesh_modifiers=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(out/'Cloudline_C02.glb'),use_selection=True,export_format='GLB',export_apply=True)
# Neutral studio; excluded from exports.
bpy.ops.object.select_all(action='DESELECT')
floor=box('Studio ground',(0,0,-.055),(200,200,.08),mat('Studio floor',(.10,.14,.18),.28,.32),0);link(floor,studio)
def light(name,loc,power,size,color):
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(name,d);studio.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.6))-o.location).to_track_quat('-Z','Y').to_euler()
light('Key softbox',(1,-3,7),1700,5,(.83,.91,1));light('Warm rim',(-4,1,4),1400,4,(1,.79,.56));light('Front fill',(4,5,4),1600,5,(.69,.83,1));light('Tail bounce',(0,-5,2),250,3,(1,1,1))
world=scene.world or bpy.data.worlds.new('World');scene.world=world;world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.16,.20,.27,1);world.node_tree.nodes['Background'].inputs[1].default_value=.35
camdata=bpy.data.cameras.new('Review camera');cam=bpy.data.objects.new('Review camera',camdata);studio.objects.link(cam);scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=1440;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
# Count final evaluated triangles without studio.
deps=bpy.context.evaluated_depsgraph_get();stats={'candidate':'C02','seed':'deterministic authored geometry','units':'metres','source_axis':'+Y forward, +Z up','objects':0,'triangles':0,'materials':[m.name for m in [white,edge,carbon,glass,rubber,rim,brake,amber,ice,caliper]]}
for obj in car.objects:
 if obj.type=='MESH':
  ev=obj.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles();stats['objects']+=1;stats['triangles']+=len(me.loop_triangles);ev.to_mesh_clear()
(out/'model-stats.json').write_text(json.dumps(stats,indent=2))
views={'rear-three-quarter':((6,-8,2.35),(0,-.2,.77),55),'front-three-quarter':((6.5,8,3.4),(0,.1,.72),55),'side':((8,0,2.1),(0,0,.73),52),'rear':((0,-8,2.15),(0,-.5,.75),55)}
for name,(loc,target,lens) in views.items():
 cam.location=loc;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();camdata.lens=lens;scene.render.filepath=str(out/(name+'.png'))
 if name=='rear-three-quarter':bpy.ops.wm.save_as_mainfile(filepath=str(out/'Cloudline_C02.blend'))
 bpy.ops.render.render(write_still=True)
print('C02 COMPLETE',json.dumps(stats))
