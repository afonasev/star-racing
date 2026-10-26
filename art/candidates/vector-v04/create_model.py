"""Vector V04: editable hard-surface art candidate. Blender 5.2, metres, +Y forward.
Run blender -b --python create_model.py -- OUTPUT [--draft]
Outputs car-only .blend/.fbx/.glb and a separate environment review scene.
"""
import bpy, bmesh, math, json, sys, random
from pathlib import Path
from mathutils import Vector
OUT=Path(sys.argv[sys.argv.index('--')+1]) if '--' in sys.argv else Path(__file__).parent
ROOT=Path(__file__).resolve().parents[3]
TEX=ROOT/'unity-prototype/Assets/StarRacing/Resources/Environment'
DRAFT='--draft' in sys.argv
CLAY='--clay' in sys.argv
NO_RENDER='--no-render' in sys.argv
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
car=bpy.data.collections.new('VECTOR • canonical car');scene.collection.children.link(car)
env=bpy.data.collections.new('CLOUD CITY • review set');scene.collection.children.link(env)
rivals=bpy.data.collections.new('RIVALS • shared geometry');scene.collection.children.link(rivals)
active=car

def material(name,c,metal=0,rough=.35,emission=0):
 m=bpy.data.materials.new(name);m.diffuse_color=(*c,1);m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 if emission:p.inputs['Emission Color'].default_value=(*c,1);p.inputs['Emission Strength'].default_value=emission
 return m
paint=material('PlayerPaint',(.98,.64,.025),.55,.23)
p=paint.node_tree.nodes.get('Principled BSDF');p.inputs['Coat Weight'].default_value=.42;p.inputs['Coat Roughness'].default_value=.19
carbon=material('Graphite_Aero',(.022,.027,.035),.30,.36)
black=material('Recess_Black',(.005,.008,.012),.1,.5)
glass=material('Smoked_Glass',(.009,.015,.023),.15,.13)
glass.node_tree.nodes.get('Principled BSDF').inputs['Transmission Weight'].default_value=.25
glass.node_tree.nodes.get('Principled BSDF').inputs['IOR'].default_value=1.46
rubber=material('Performance_Rubber',(.014,.017,.021),.02,.68)
alloy=material('Forged_Titanium',(.13,.15,.18),.85,.23)
rim=material('Wheel_Graphite',(.055,.068,.086),.8,.29)
rotor=material('Ceramic_Rotor',(.065,.073,.085),.55,.48)
caliper=material('Caliper_Copper',(.58,.16,.035),.65,.32)
red=material('Red_LED',(.65,.002,.005),.25,.22,2)
white=material('White_LED',(.77,.9,1),.1,.2,3)
leather=material('Interior_Alcantara',(.014,.019,.026),0,.87)
silver=material('Lettering',(.72,.78,.84),.65,.35)
grille_metal=material('Grille_Titanium',(.045,.055,.065),.6,.40)

nt=paint.node_tree;noise=nt.nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=650;noise.inputs['Detail'].default_value=2
bump=nt.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.09;bump.inputs['Distance'].default_value=.00018
nt.links.new(noise.outputs['Fac'],bump.inputs['Height']);nt.links.new(bump.outputs['Normal'],nt.nodes['Principled BSDF'].inputs['Normal'])

# Explicit mesh construction keeps authored planar surfacing and real recesses.
def link(o):
 for c in list(o.users_collection):c.objects.unlink(o)
 active.objects.link(o);return o

def mesh(name,verts,faces,mat,bevel=0):
 data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
 bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
 o=bpy.data.objects.new(name,data);active.objects.link(o);data.materials.append(mat)
 if bevel: bevel_obj(o,bevel)
 return o

def bevel_obj(o,width=.012,segments=3):
 mod=o.modifiers.new('Edge highlight radius','BEVEL');mod.width=width;mod.segments=segments
 mod=o.modifiers.new('Weighted surface normals','WEIGHTED_NORMAL');mod.keep_sharp=True;mod.weight=50

def cube(name,loc,size,mat,bevel=.01):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=link(bpy.context.object);o.name=name;o.dimensions=size
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat)
 if bevel:bevel_obj(o,bevel)
 o.select_set(False);return o

def panel(name,pts,mat,thick=.012,bevel=.004):
 o=mesh(name,pts,[tuple(range(len(pts)))],mat)
 sol=o.modifiers.new('Panel thickness','SOLIDIFY');sol.thickness=thick;sol.offset=-1
 if bevel:bevel_obj(o,bevel,2)
 return o

def line(name,pts,mat,r=.006):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.resolution_u=1;c.bevel_depth=r;c.bevel_resolution=2
 sp=c.splines.new('POLY');sp.points.add(len(pts)-1)
 for p,co in zip(sp.points,pts):p.co=(*co,1)
 o=bpy.data.objects.new(name,c);active.objects.link(o);c.materials.append(mat);return o

def cylinder(name,loc,r,depth,mat,axis='X',vertices=64):
 bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=depth,location=loc)
 o=link(bpy.context.object);o.name=name
 if axis=='X':o.rotation_euler[1]=math.pi/2
 elif axis=='Y':o.rotation_euler[0]=math.pi/2
 o.data.materials.append(mat);o.select_set(False);return o

def beam(name,a,b,width,depth,mat):
 o=cube(name,(Vector(a)+Vector(b))/2,(width,depth,(Vector(a)-Vector(b)).length),mat,.004)
 o.rotation_euler=(Vector(b)-Vector(a)).to_track_quat('Z','Y').to_euler();return o

def empty(name,loc):
 o=bpy.data.objects.new(name,None);active.objects.link(o);o.location=loc;return o

def parent_objects(objects,parent):
 bpy.context.view_layer.update()
 for o in objects:
  matrix=o.matrix_world.copy();o.parent=parent;o.matrix_world=matrix

def coord(u,v,axis,value):
 return (value,u,v) if axis==0 else ((u,value,v) if axis==1 else (u,v,value))
def cut_prism(name,outline,axis,a,b):
 n=len(outline);vs=[coord(u,v,axis,d) for d in [a,b] for u,v in outline]
 fs=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 cutter=mesh('CUT '+name,vs,fs,black)
 mod=body.modifiers.new(name,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
 bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
def skin_point(point,axis,side=1,offset=.006):
 p=Vector(point);start=p.copy();start[axis]=side*5;direction=Vector((0,0,0));direction[axis]=-side
 hit,co,normal,index=body.evaluated_get(bpy.context.evaluated_depsgraph_get()).ray_cast(start,direction)
 if not hit:return tuple(point)
 co[axis]+=side*offset;return tuple(co)
def skin_path(points,axis,side=1):
 result=[]
 for a,b in zip(points,points[1:]):
  for i in range(12):result.append(skin_point(Vector(a).lerp(Vector(b),i/12),axis,side,.005))
 result.append(skin_point(points[-1],axis,side,.005));return result


def polygon_inside(x,y,poly):
 inside=False;j=len(poly)-1
 for i,(xi,yi) in enumerate(poly):
  xj,yj=poly[j]
  if (yi>y)!=(yj>y) and x<(xj-xi)*(y-yi)/(yj-yi)+xi:inside=not inside
  j=i
 return inside

def honeycomb(name,poly,axis,depth,spacing=.042):
 vs=[];fs=[];xmin=min(p[0] for p in poly);xmax=max(p[0] for p in poly);ymin=min(p[1] for p in poly);ymax=max(p[1] for p in poly)
 row=0;y=ymin
 while y<ymax:
  x=xmin+(row%2)*spacing*.5
  while x<xmax:
   if all(polygon_inside(x+spacing*.48*math.cos(k*math.pi/3),y+spacing*.48*math.sin(k*math.pi/3),poly) for k in range(6)):
    start=len(vs)
    for r in [spacing*.45,spacing*.34]:
     for k in range(6):vs.append(coord(x+r*math.cos(k*math.pi/3),y+r*math.sin(k*math.pi/3),axis,depth))
    for k in range(6):fs.append((start+k,start+(k+1)%6,start+(k+1)%6+6,start+k+6))
   x+=spacing
  y+=spacing*.87;row+=1
 return mesh(name,vs,fs,grille_metal)

def duct(name,outline,axis,front,back,mat=paint,inset=.84,grille=True):
 center=Vector((sum(x for x,z in outline)/len(outline),sum(z for x,z in outline)/len(outline)))
 inner=[tuple(center+(Vector(p)-center)*inset) for p in outline]
 deep=[tuple(center+(Vector(p)-center)*inset*.93) for p in outline]
 n=len(outline);vs=[coord(u,v,axis,front) for u,v in outline]+[coord(u,v,axis,front) for u,v in inner]
 bezel=mesh(name+' faceted surround',vs,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],mat,.004)
 sol=bezel.modifiers.new('Surround thickness','SOLIDIFY');sol.thickness=.014
 vs=[coord(u,v,axis,front) for u,v in inner]+[coord(u,v,axis,back) for u,v in deep]
 mesh(name+' deep duct walls',vs,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],carbon,.003)
 panel(name+' internal darkness',[coord(u,v,axis,back) for u,v in deep],black,.006,0)
 if grille:honeycomb(name+' perforated insert',deep,axis,back+(front-back)*.30)
 return inner

# New V04 primary volumes. All dimensions in metres; +Y is forward.
# Wheels are 0.48 m radius and roof approximately 1.18 m. Body width 2.36 m.
# Long creased fender planes replace the V03 wheel-following humps.
stations=[
 (-2.40,1.105,1.040,1.055,1.035),(-2.02,1.175,1.035,1.080,1.035),
 (-1.50,1.18,1.045,1.090,1.025),(-1.12,1.155,1.010,1.065,.995),
 (-.72,1.065,.825,.920,.900),(.05,1.070133,.8635,.9662,.897433),
 (.78,1.075,.900,1.010,.895),(1.32,1.13,1.015,1.065,.885),
 (1.65,1.135,.985,1.045,.860),(2.10,1.12,.815,.850,.765),
 (2.46,1.075,.675,.705,.690)]
verts=[]
for y,w,shoulder,upper,center in stations:
 lower=.36 if -.9<y<.9 else .245
 # Thin waist and inset lower door create sculpted side volume.
 row=[(-w*.80,y,lower),(-w*.96,y,shoulder-.145),(-w,y,shoulder),(-w*.82,y,upper),(-w*.54,y,center),(w*.54,y,center),(w*.82,y,upper),(w,y,shoulder),(w*.96,y,shoulder-.145),(w*.80,y,lower)]
 if y>2.4:
  row=[(x,yy+.05*(1-abs(x)/w),z) for x,yy,z in row]
 verts.extend(row)
faces=[tuple(range(9,-1,-1))]
for k in range(len(stations)-1):
 for i in range(10):faces.append((k*10+i,k*10+(i+1)%10,(k+1)*10+(i+1)%10,(k+1)*10+i))
faces.append(tuple(range((len(stations)-1)*10,len(stations)*10)))
body=mesh('V04 primary body',verts,faces,paint)
# Resolve non-planar loft quads before exact boolean operations.
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
for y in [-1.50,1.50]:
 for side in [-1,1]:
  rings=[(.70,.40),(.78,.465),(.87,.510),(1.50,.510)];n=96
  cv=[(side*x,y+r*math.sin(i*2*math.pi/n),.48+r*math.cos(i*2*math.pi/n)) for x,r in rings for i in range(n)]
  cf=[tuple(range(n-1,-1,-1))]+[(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(rings)-1) for i in range(n)]+[tuple(range((len(rings)-1)*n,len(rings)*n))]
  cutter=mesh('Temporary wheel well',cv,cf,black)
  mod=body.modifiers.new('Wheel opening','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
  bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
cut_prism('Open nose ducts',[(-1.5,.14),(1.5,.14),(1.5,.590),(-1.5,.590)],1,2.10,3)
cut_prism('Open rear fascia',[(-1.5,.14),(1.5,.14),(1.5,1.012),(-1.5,1.012)],1,-3,-2.06)
# Large side intake integrated with broad rear-quarter shoulder.
side_outline=[(-1.13,1.005),(-.65,.858),(-.75,.485),(-1.02,.355),(-1.16,.54)]
side_surfaces={}
for s in [-1,1]:
 outer=[];inner=[];cy=sum(p[0] for p in side_outline)/5;cz=sum(p[1] for p in side_outline)/5
 for scale,arr in [(1,outer),(.91,inner)]:
  outline=[(cy+(y-cy)*scale,cz+(z-cz)*scale) for y,z in side_outline]
  for i,(ya,za) in enumerate(outline):
   yb,zb=outline[(i+1)%len(outline)]
   for j in range(12):
    y=ya+(yb-ya)*j/12;z=za+(zb-za)*j/12
    hit,co,normal,index=body.ray_cast(Vector((s*5,y,z)),Vector((-s,0,0)))
    arr.append((co.x+s*.003,y,z) if hit else (s*1.05,y,z))
 side_surfaces[s]=(outer,inner)
for s in [-1,1]:
 cut_prism('Deep quarter intake',side_outline,0,s*.77,s*1.5)
 cut_prism('Thin headlamp recess',[(s*.60,.602),(s*1.03,.625),(s*1.05,.697),(s*.62,.685)],1,2.24,2.7)
bevel_obj(body,.006,2)
bpy.context.view_layer.update()
# Continuous tapered undertray and deep sculpted rocker panels.
cube('Undertray',(0,0,.235),(1.95,4.50,.07),carbon,.008)
for s in [-1,1]:
 panel('Sculpted carbon rocker',[(s*.83,.95,.37),(s*.99,.81,.245),(s*1.11,-.99,.245),(s*.90,-.88,.405)],carbon,.042,.006)
 panel('Rocker outer blade',[(s*.99,.81,.245),(s*1.07,.79,.24),(s*1.15,-1.01,.24),(s*1.11,-.99,.245)],carbon,.025,.004)
 outer,inner=side_surfaces[s];n=len(outer);deep=[(x-s*.20,y,z) for x,y,z in inner]
 mesh('Quarter intake flush rim',outer+inner,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],carbon,.002)
 mesh('Quarter intake inner walls',inner+deep,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],carbon)
 panel('Quarter intake internal shadow',deep,black,.008,0)
 for j in range(8):
  z=.46+j*.053;beam('Quarter intake cooling louvre',(s*.93,-1.07,z),(s*.93,-.82,z+.015),.014,.013,carbon)
 line('Precision door shutline',skin_path([(s*1,.84,.87),(s*1,.65,.50),(s*1,.48,.39),(s*1,-.67,.40),(s*1,-.57,.87)],0,s),black,.0022)
 # Thin flush handle is projected onto the actual door plane.
 p=skin_point((s*1,-.40,.80),0,s,.004);cube('Flush release',p,(.012,.115,.017),rim,.003)

# Integrated, wider cabin: low roof and long windshield; lower edges flush to body.
panel('V04 windscreen',[(-.845,1.02,.901),(.845,1.02,.901),(.675,-.12,1.240),(-.675,-.12,1.240)],glass,.008,.003)
panel('V04 painted roof',[(-.69,-.11,1.252),(.69,-.11,1.252),(.72,-.82,1.265),(-.72,-.82,1.265)],paint,.018,.004)
panel('Inset roof carbon',[(-.47,-.15,1.254),(.47,-.15,1.254),(.49,-.77,1.267),(-.49,-.77,1.267)],carbon,.006,.002)
panel('Rear engine glazing',[(-.70,-.84,1.245),(.70,-.84,1.245),(.71,-1.47,1.045),(-.71,-1.47,1.045)],glass,.008,.003)
for s in [-1,1]:
 # Painted pillars are contiguous sheets, not bars sitting above glass.
 panel('A pillar integrated ribbon',[(s*.845,1.025,.897),(s*.885,.98,.907),(s*.720,-.115,1.250),(s*.675,-.12,1.240)],paint,.014,.003)
 panel('Side glazing',[(s*.878,.97,.901),(s*.714,-.13,1.240),(s*.743,-.81,1.247),(s*.943,-1.10,1.010),(s*.98,-.66,.924)],glass,.008,.003)
 panel('Side window lower painted rail',[(s*.878,.97,.901),(s*.98,-.66,.924),(s*.943,-1.10,1.010),(s*.95,-1.09,.985),(s*.975,-.66,.905),(s*.88,.96,.887)],paint,.014,.002)
 sail=[(s*.72,-.82,1.265),(s*.943,-1.10,1.010),skin_point((s*1.06,-1.28,1),2,1,.003),skin_point((s*1.00,-1.82,1),2,1,.003),(s*.71,-1.47,1.045),(s*.70,-.84,1.245),(s*.83,-1.23,1.10)]
 mesh('Integrated rear sail volume',sail,[(i,(i+1)%6,6) for i in range(6)],paint,.003)
 line('Window rear divider',[(s*.744,-.73,1.245),(s*.969,-.81,.956)],carbon,.006)
 beam('Mirror slim stalk',(s*.91,.57,.92),(s*1.07,.60,.995),.018,.018,carbon)
 cube('Mirror aero housing',(s*1.10,.62,1.002),(.16,.22,.057),paint,.015)
 cube('Mirror reflective inset',(s*1.10,.510,1.002),(.125,.008,.032),alloy,.003)

panel('Roof rear seam closure',[(-.72,-.82,1.265),(.72,-.82,1.265),(.70,-.84,1.245),(-.70,-.84,1.245)],paint,.009,.002)
# Simple visible cockpit below the glazing.
cube('Cockpit interior',(0,-.22,.78),(1.55,1.85,.13),leather,.02)
cube('Low dash',(0,.70,.88),(1.51,.22,.08),leather,.02)
for x in [-.40,.40]:
 cube('Seat cushion',(x,-.35,.82),(.43,.48,.10),leather,.03)
 o=cube('Seatback',(x,-.64,1.00),(.40,.10,.34),leather,.025);o.rotation_euler[0]=-.12
 for s in [-1,1]:line('Seat seam',[(x+s*.16,-.58,.84),(x+s*.16,-.61,1.11)],caliper,.004)

# Thin slanted nose; integrated headlamp pockets under the actual fender skin.
for s in [-1,1]:
 outline=[(s*.60,.602),(s*1.03,.625),(s*1.05,.697),(s*.62,.685)]
 duct('Flush headlamp chamber',outline,1,2.465,2.25,carbon,.90,False)
 for j in range(2):
  xa=.636+j*.188;xb=xa+.17;z=.629+j*.009
  pts=[(s*xa,2.472,z),(s*xb,2.472,z+.009),(s*(xb+.008),2.472,z+.031),(s*(xa+.008),2.472,z+.022)]
  panel('Paired white projector',pts,white,.007,.002)
 # Nose fascia is swept back below the lamps, not a full-height vertical wall.
 sidevent=[(s*.635,.570),(s*1.065,.602),(s*1.115,.33),(s*.83,.285)]
 duct('Swept front side intake',sidevent,1,2.435,2.12,paint,.88)
 panel('Lamp lower edge',[ (s*.60,2.48,.60),(s*1.06,2.46,.625),(s*1.065,2.435,.602),(s*.635,2.435,.570)],paint,.013,.003)
 panel('Angular outer bumper cheek',[(s*1.065,2.43,.60),(s*1.12,2.10,.81),(s*1.15,2.02,.46),(s*1.11,2.43,.30)],paint,.025,.005)
 # Wedge-shaped recessed extractor along the fender; its border follows the skin.
 outline3=[(s*.72,1.88,1),(s*.85,1.99,1),(s*.89,1.48,1),(s*.84,1.43,1)]
 pts=[skin_point(q,2,1,.003) for q in outline3]
 panel('Fender recessed extractor',pts,black,.008,.002)
 line('Hood diagonal seam',skin_path([(s*.61,2.40,1),(s*.61,1.81,1),(s*.70,1.10,1)],2),black,.0018)
main=[(-.59,.565),(.59,.565),(.80,.245),(-.80,.245)]
duct('Central wedge mouth',main,1,2.49,2.13,carbon,.95)
for x in [-.37,.37]:panel('Front inlet structural vane',[(x,2.51,.24),(x*.77,2.49,.55),(x*.77,2.15,.54),(x,2.15,.25)],carbon,.014,.003)
panel('Swept carbon front splitter',[(-1.17,2.38,.225),(-1.10,2.55,.225),(-.57,2.59,.225),(.57,2.59,.225),(1.10,2.55,.225),(1.17,2.38,.225),(1.04,2.08,.225),(-1.04,2.08,.225)],carbon,.026,.005)

# Wide shoulder-integrated rear fascia, with thin nested lamps.
for s in [-1,1]:
 outline=[(s*.43,.975),(s*1.025,1.005),(s*1.10,.865),(s*1.055,.735),(s*.48,.715),(s*.61,.840)]
 duct('Recessed rear lamp',outline,1,-2.415,-2.09,paint,.88)
 pts=[(s*x,-2.452,z) for x,z in [(.94,.954),(.70,.841),(.91,.756),(.96,.772),(.77,.843),(.985,.933)]]
 panel('Smoked red lamp housing',[(s*x,-2.425,z) for x,z in [(.96,.974),(.65,.841),(.92,.737),(1.0,.764),(.80,.842),(1.02,.944)]],black,.016,.003)
 panel('Narrow chevron LED',pts,red,.009,.002)
 # Diagonal lower bridge and broad shoulder wrapping the light recess.
 panel('Rear lower painted blade',[(s*.40,-2.42,.710),(s*1.05,-2.42,.733),(s*1.105,-2.38,.56),(s*.76,-2.43,.51),(s*.37,-2.43,.61)],paint,.030,.006)
 mesh('Rear quarter wrap',[(s*1.105,-2.405,1.040),(s*1.175,-2.015,1.035),(s*1.17,-2.015,.82),(s*1.12,-2.04,.35),(s*1.08,-2.42,.37),(s*1.04,-2.43,.74)],[(0,1,2,5),(2,3,4,5)],paint,.006)
# Integrated upper tail cross-member follows the silhouette, without a raised plank.
panel('Rear upper flush fascia',[(-1.105,-2.405,1.040),(-.42,-2.405,1.035),(.42,-2.405,1.035),(1.105,-2.405,1.040),(1.05,-2.415,.998),(.43,-2.415,.970),(-.43,-2.415,.970),(-1.05,-2.415,.998)],paint,.014,.003)
# Engine panel lies behind cabin, bounded by the broad rear shoulders.
pts=[skin_point(q,2,1,.006) for q in [(-.59,-1.48,1),(.59,-1.48,1),(.58,-2.12,1),(-.58,-2.12,1)]]
panel('Engine hatch recessed tray',pts,black,.008,.003)
for i in range(7):
 y=-1.53-i*.077;panel('Recessed engine louvre',[skin_point(q,2,1,.014) for q in [(-.55,y,1),(.55,y,1),(.55,y-.043,1),(-.55,y-.043,1)]],carbon,.012,.003)
# Smaller nested exhausts sit inside a dark faceted island.
duct('Exhaust island',[(-.36,.825),(.36,.825),(.40,.560),(-.40,.560)],1,-2.43,-2.08,carbon,.90,False)
for o in list(car.objects):
 if o.name=='Exhaust island internal darkness':bpy.data.objects.remove(o,do_unlink=True)
def rounded_rect(cx,cz,w,h,r,n=5):
 pts=[]
 for ox,oz,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
  for i in range(n+1):
   a=math.radians(start+i*90/n);pts.append((cx+ox+r*math.cos(a),cz+oz+r*math.sin(a)))
 return pts
for s in [-1,1]:
 cx=s*.142;outer=rounded_rect(cx,.675,.235,.205,.035);inner=rounded_rect(cx,.675,.185,.155,.026)
 n=len(outer);vs=[(x,y,z) for points,y in [(outer,-2.465),(inner,-2.469),(inner,-2.16)] for x,z in points]
 fs=[]
 for i in range(n):
  j=(i+1)%n;fs.extend([(i,j,n+j,n+i),(n+i,n+j,2*n+j,2*n+i)])
 mesh('Hollow squared titanium exhaust '+str(s),vs,fs,alloy,.003)
 panel('Exhaust deep shadow '+str(s),[(cx-.092,-2.151,.597),(cx+.092,-2.151,.597),(cx+.092,-2.151,.753),(cx-.092,-2.151,.753)],black,.006,0)
 empty('Exhaust_L' if s<0 else 'Exhaust_R',(cx,-2.48,.675))
# Sculpted carbon bumper: substantial solid planes, not a hanging open box.
mesh('Integrated carbon diffuser shell',[(-1.10,-2.40,.57),(-.39,-2.45,.58),(.39,-2.45,.58),(1.10,-2.40,.57),(-1.14,-2.47,.35),(-.80,-2.49,.245),(.80,-2.49,.245),(1.14,-2.47,.35),(-1.03,-2.02,.42),(-.72,-1.94,.29),(.72,-1.94,.29),(1.03,-2.02,.42)],[(0,1,5,4),(1,2,6,5),(2,3,7,6),(0,4,8),(4,5,9,8),(5,6,10,9),(6,7,11,10),(3,11,7)],carbon,.008)
for x in [-.79,-.40,0,.40,.79]:
 panel('Integrated diffuser strake',[(x,-2.51,.245),(x*.90,-2.47,.505),(x*.86,-1.97,.40),(x,-1.93,.26)],carbon,.020,.004)
def lettering(name,text,loc,size,mat,rotation):
 c=bpy.data.curves.new(name,'FONT');c.body=text;c.align_x='CENTER';c.size=size;c.extrude=.0006;c.space_character=1.3
 o=bpy.data.objects.new(name,c);active.objects.link(o);c.materials.append(mat);o.location=loc;o.rotation_euler=rotation;return o
lettering('VECTOR rear badge','V E C T O R',(0,-2.440,.908),.033,silver,(math.pi/2,0,0))
lettering('Nose mark','V',skin_point((0,2.20,1),2,1,.008),.060,alloy,(0,0,math.pi))

# Detailed four wheels: closed swept tyre profile, directional spokes, drilled rotors.
def revolve(name,x,y,z,profile,mat,n=96):
 vs=[(x+d,y+r*math.sin(2*math.pi*i/n),z+r*math.cos(2*math.pi*i/n)) for d,r in profile for i in range(n)]
 fs=[(j*n+i,j*n+(i+1)%n,((j+1)%len(profile))*n+(i+1)%n,((j+1)%len(profile))*n+i) for j in range(len(profile)) for i in range(n)]
 o=mesh(name,vs,fs,mat)
 for face in o.data.polygons:face.use_smooth=True
 return o
for s in [-1,1]:
 for y,tag in [(-1.48,'Rear'),(1.44,'Front')]:
  before=set(car.objects);x=s*.93;z=.455
  revolve(tag+' tyre',x,y,z,[(-.16,.33),(-.182,.385),(-.172,.43),(-.13,.455),(.13,.455),(.172,.43),(.182,.385),(.16,.33)],rubber)
  ox=x+s*.179
  revolve(tag+' forged rim lip',ox,y,z,[(-.025,.337),(-.018,.359),(.012,.36),(.021,.343),(.012,.327)],alloy)
  revolve(tag+' wheel barrel',x,y,z,[(-.15,.327),(.15,.327),(.15,.339),(-.15,.339)],rim)
  disc=cylinder(tag+' ceramic brake',(x+s*.115,y,z),.29,.018,rotor,vertices=80);bevel_obj(disc,.003,2)
  # Dark drilled wells read through open spokes.
  for ringrad,num in [(.245,22),(.20,18)]:
   for j in range(num):
    a=j*2*math.pi/num
    cylinder(tag+' rotor drilled well',(x+s*.126,y+ringrad*math.sin(a),z+ringrad*math.cos(a)),.007,.002,black,vertices=8)
  cube(tag+' copper brake caliper',(x+s*.13,y+.215,z+.035),(.07,.11,.24),caliper,.022)
  for i in range(5):
   a=i*2*math.pi/5
   shape=[(.075,a-.25),(.31,a+.00),(.342,a+.27),(.337,a+.68),(.12,a+.42)]
   pts=[(ox+s*.016,y+r*math.sin(ang),z+r*math.cos(ang)) for r,ang in shape]
   panel(tag+' directional aero spoke',pts,rim,.034,.007)
   line(tag+' spoke diamond-cut edge',[(ox+s*.024,y+.10*math.sin(a+.05),z+.10*math.cos(a+.05)),(ox+s*.024,y+.325*math.sin(a+.32),z+.325*math.cos(a+.32))],alloy,.004)
  for i in range(5):
   a=i*2*math.pi/5+.62
   line(tag+' small paint accent',[(ox+s*.026,y+.325*math.sin(a),z+.325*math.cos(a)),(ox+s*.026,y+.325*math.sin(a+.085),z+.325*math.cos(a+.085))],paint,.003)
  cylinder(tag+' centerlock',(ox+s*.025,y,z),.073,.025,alloy,vertices=12)
  cylinder(tag+' hub cap',(ox+s*.041,y,z),.047,.014,rim,vertices=32)
  for j in range(5):
   a=j*2*math.pi/5;cylinder(tag+' hub fastener',(ox+s*.052,y+.057*math.sin(a),z+.057*math.cos(a)),.006,.006,black,vertices=6)
  for d in [-.095,-.035,.035,.095]:
   revolve(tag+' continuous tread channel',x+d,y,z,[(-.003,.4555),(.003,.4555),(.003,.457),(-.003,.457)],black,n=96)
  # Fine diagonal shoulder grooves.
  for i in range(48):
   a=i*2*math.pi/48
   for side in [-1,1]:
    line(tag+' shoulder tread',[(x+side*.105,y+.455*math.sin(a),z+.455*math.cos(a)),(x+side*.147,y+.446*math.sin(a+.035),z+.446*math.cos(a+.035))],black,.002)
  for r in [.384,.415]:revolve(tag+' sidewall embossed ring',ox,y,z,[(-.001,r-.002),(.001,r-.002),(.001,r+.002),(-.001,r+.002)],rubber)
  pivot=empty('Wheel_'+tag+('_L' if s<0 else '_R'),(x,y,z))
  parent_objects(set(car.objects)-before-{pivot},pivot)
# V04 larger wheel assemblies and wider track. Preserve each wheel's local hierarchy.
for o in list(car.objects):
 if o.type=='EMPTY' and o.name.startswith('Wheel_'):
  o.location.x=math.copysign(.985,o.location.x);o.location.y=math.copysign(1.50,o.location.y);o.location.z=.48;o.scale=(.48/.455,)*3
# Lower only the cabin's upper envelope, preserving shoulder and wheel geometry.
# A continuous piecewise transform keeps joined window/pillar edges coincident.
bpy.context.view_layer.update()
for o in car.objects:
 if o.type=='MESH':
  inv=o.matrix_world.inverted()
  for v in o.data.vertices:
   co=o.matrix_world@v.co
   if co.z>1.09:co.z=1.09+(co.z-1.09)*.43;v.co=inv@co
  o.data.update()
 elif o.type=='CURVE':
  for sp in o.data.splines:
   for pt in sp.points:
    if pt.co.z>1.09:pt.co.z=1.09+(pt.co.z-1.09)*.43
# Resolve outward roof faces and conform the extractor skin to the faceted hood.
def finish_surface_details():
 for name in ['V04 painted roof','Inset roof carbon']:
  o=bpy.data.objects[name]
  if sum(p.normal.z for p in o.data.polygons)<0:
   bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free();o.data.update()
 for o in list(car.objects):
  if o.name.startswith('Fender recessed extractor'):
   bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=12,use_grid_fill=True);bm.to_mesh(o.data);bm.free()
   for v in o.data.vertices:v.co=skin_point(v.co,2,1,.004)
   for mod in list(o.modifiers):
    if mod.type=='SOLIDIFY':o.modifiers.remove(mod)
   o.data.update()
finish_surface_details()

# Main authored hierarchy.
root=empty('Vector_V04',(0,0,0))
parent_objects([o for o in car.objects if o.parent is None and o!=root],root)
root['design']='04 Vector';root['source_forward']='+Y';root['source_up']='+Z';root['purpose']='offline art candidate; no gameplay integration'

# Save source with editable modelling modifiers. Exports bake a duplicate collection.
bpy.context.view_layer.update()
scene.render.engine='CYCLES';scene.cycles.samples=24 if DRAFT else 48;scene.cycles.use_denoising=True
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.get_devices()
 for device in prefs.devices:device.use=device.type=='METAL'
 if any(device.type=='METAL' for device in prefs.devices):scene.cycles.device='GPU'
except Exception:pass
scene.render.resolution_x=1440 if DRAFT else 1920;scene.render.resolution_y=900 if DRAFT else 1200;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Vector_V04.blend'))
# Convert curves/text on copies; leave authored source intact.
export_col=bpy.data.collections.new('EXPORT TEMP');scene.collection.children.link(export_col)
copies={}
for o in car.objects:
 cp=o.copy()
 if o.data:cp.data=o.data.copy()
 export_col.objects.link(cp);copies[o]=cp
for original,cp in copies.items():
 cp.parent=copies.get(original.parent)

bpy.ops.object.select_all(action='DESELECT')
convertible=[o for o in export_col.objects if o.type in {'CURVE','FONT'}]
for o in convertible:o.select_set(True)
if convertible:
 bpy.context.view_layer.objects.active=convertible[0];bpy.ops.object.convert(target='MESH')
for o in car.objects:o.hide_set(True)
bpy.ops.object.select_all(action='DESELECT')
for o in export_col.objects:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Vector_V04.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,use_mesh_modifiers=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(OUT/'Vector_V04.glb'),use_selection=True,export_format='GLB',export_apply=True)
for o in list(export_col.objects):bpy.data.objects.remove(o,do_unlink=True)
bpy.data.collections.remove(export_col)
for o in car.objects:o.hide_set(False)

# Game decoration review set: actual project textures, authored representative layout.
active=env
manifest=[]
def textured(name,file,base=(1,1,1),rough=.6,scale=1):
 m=material(name,base,.12,rough);nt=m.node_tree;p=nt.nodes.get('Principled BSDF')
 image=bpy.data.images.load(str(TEX/file),check_existing=True);t=nt.nodes.new('ShaderNodeTexImage');t.image=image;t.projection='BOX';t.projection_blend=.06
 tex=nt.nodes.new('ShaderNodeTexCoord');mapping=nt.nodes.new('ShaderNodeVectorMath');mapping.operation='SCALE';mapping.inputs[3].default_value=scale
 nt.links.new(tex.outputs['Object'],mapping.inputs[0]);nt.links.new(mapping.outputs[0],t.inputs['Vector']);nt.links.new(t.outputs['Color'],p.inputs['Base Color'])
 manifest.append(str((TEX/file).relative_to(ROOT)));return m
roadmat=textured('PROJECT • Road panels',Path('Textures/Road.png'),rough=.68,scale=.23)
railmat=material('Teal barrier housing',(.035,.17,.215),.38,.36)
railcap=material('Barrier cap',(.23,.39,.44),.45,.36)
facade=textured('PROJECT • Facade',Path('Textures/Facade.png'),rough=.44,scale=.10)
trim=material('City silver structural trim',(.50,.63,.71),.45,.36)
road=cube('Road inspection straight',(0,40,-.162),(22,160,.32),roadmat,.03)
for s in [-1,1]:
 for j in range(-9,31):
  y=j*4
  cube('Teal track barrier',(s*11,y,.47),(.24,3.96,1.0),railmat,.025)
  cube('Track barrier top rail',(s*11,y,1.01),(.29,3.98,.09),railcap,.016)
  for yy in [y-1.78,y+1.78]:cube('Rail recessed fastening',(s*10.867,yy,.68),(.012,.045,.10),black,.008)
# Distant curved road with teal rails, from the same paneled material.
path=[]
for i in range(91):
 t=i/90;a=t*1.65;path.append((52*(1-math.cos(a)),100+52*math.sin(a),0))
vs=[]
for i,pnt in enumerate(path):
 a=i/90*1.65;dx=math.cos(a);dy=-math.sin(a)
 vs.extend([(pnt[0]+s*11*dx,pnt[1]+s*11*dy,-.025) for s in [-1,1]])
mesh('Sweeping elevated curve',vs,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(90)],roadmat)
for s in [-1,1]:
 vs=[]
 for i,pnt in enumerate(path):
  a=i/90*1.65;x=pnt[0]+s*11*math.cos(a);y=pnt[1]-s*11*math.sin(a);vs.extend([(x,y,0),(x,y,1.05)])
 mesh('Curved barrier',vs,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(90)],railmat)
# Skyline placement deterministic; architectural proportions echo TrackEnvironmentBuilder.
rng=random.Random(404)
for i in range(32):
 s=-1 if i%2==0 else 1;x=s*rng.uniform(28,145);y=rng.uniform(-40,240);h=rng.uniform(45,145);w=rng.uniform(8,15)
 cube('City tower %02d'%i,(x,y,h/2-55),(w,w,h),facade,0)
 cube('Tower cap',(x,y,h-55),(w*1.12,w*1.12,1.0),trim,.06)
 for j in range(1,5):cube('Tower floor band',(x,y,-55+h*j/5),(w*1.015,w*1.015,.25),trim,0)
# Clouds below track: project sky texture on a broad emissive surface, no invented city.
cloud=material('PROJECT • cloud sea',(1,1,1),0,1)
nt=cloud.node_tree;pn=nt.nodes.get('Principled BSDF');im=bpy.data.images.load(str(TEX/'Sky/CloudSea.png'),check_existing=True)
t=nt.nodes.new('ShaderNodeTexImage');t.image=im;nt.links.new(t.outputs['Color'],pn.inputs['Base Color']);nt.links.new(t.outputs['Color'],pn.inputs['Emission Color']);pn.inputs['Emission Strength'].default_value=.18
bpy.ops.mesh.primitive_plane_add(size=1400,location=(0,0,-42));o=link(bpy.context.object);o.name='Cloud sea below elevated circuit';o.data.materials.append(cloud)
manifest.append(str((TEX/'Sky/CloudSea.png').relative_to(ROOT)))
world=bpy.data.worlds.new('Daylight project environment');scene.world=world;world.use_nodes=True
nt=world.node_tree;nt.nodes.clear();background=nt.nodes.new('ShaderNodeBackground');background.inputs['Strength'].default_value=.7;output=nt.nodes.new('ShaderNodeOutputWorld')
envtex=nt.nodes.new('ShaderNodeTexEnvironment');envtex.image=bpy.data.images.load(str(TEX/'Sky/DaylightReflection.exr'),check_existing=True)
nt.links.new(envtex.outputs['Color'],background.inputs['Color'])
camera_sky=nt.nodes.new('ShaderNodeBackground');camera_sky.inputs['Color'].default_value=(.40,.58,.80,1);camera_sky.inputs['Strength'].default_value=.8
pathnode=nt.nodes.new('ShaderNodeLightPath');mix=nt.nodes.new('ShaderNodeMixShader');nt.links.new(pathnode.outputs['Is Camera Ray'],mix.inputs[0]);nt.links.new(background.outputs[0],mix.inputs[1]);nt.links.new(camera_sky.outputs[0],mix.inputs[2]);nt.links.new(mix.outputs[0],output.inputs[0]);manifest.append(str((TEX/'Sky/DaylightReflection.exr').relative_to(ROOT)))
# Soft sun and sky bounce for physically legible surfaces.
d=bpy.data.lights.new('Daylight sun','SUN');d.energy=2.0;d.angle=.09;o=bpy.data.objects.new('Daylight sun',d);env.objects.link(o);o.rotation_euler=(.48,-.48,-.6)
d=bpy.data.lights.new('Sky fill','AREA');d.energy=1100;d.shape='DISK';d.size=12;o=bpy.data.objects.new('Sky fill',d);env.objects.link(o);o.location=(0,-2,9)

# Color rivals share geometry, differ only in PlayerPaint material.
for k,(pos,c) in enumerate([((-4.3,1.5,0),(.09,.65,.025)),((4.0,3.0,0),(.75,.045,.025)),((-3.0,11,0),(.30,.035,.62)),((2.8,17,0),(.025,.58,.69)),((-3.0,24,0),(.95,.54,.018))]):
 variant=paint.copy();variant.name='Rival paint %d'%k;variant.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(*c,1)
 mapping={}
 for src in car.objects:
  obj=src.copy();rivals.objects.link(obj);mapping[src]=obj
  if src.data:
   for idx,slot in enumerate(obj.material_slots):
    if slot.material==paint:slot.link='OBJECT';slot.material=variant
 for src,obj in mapping.items():obj.parent=mapping.get(src.parent)
 mapping[root].location=pos
rivals.hide_render=True
# Camera collection and named views stored in blend.
camdata=bpy.data.cameras.new('Review camera');cam=bpy.data.objects.new('Review camera',camdata);env.objects.link(cam);scene.camera=cam
views={
 '01-front-three-quarter':((6.4,8.2,3.0),(0,0,.65),54),
 '02-rear-three-quarter':((6.4,-8.2,2.85),(0,-.1,.64),54),
 '03-side':((9.1,0,2.15),(0,0,.64),52),
 '04-rear':((0,-8.6,1.90),(0,-.45,.65),57),
 '05-front':((0,8.6,1.90),(0,.25,.66),57),
 '06-top':((5,-5,8.7),(0,0,.4),53),
 '07-chase':((0,-10,5.9),(0,9,1.1),24),
 '08-rear-detail':((2.9,-4.8,1.7),(0,-1.9,.69),62),
 '09-wheel-detail':((3.1,3.3,1.1),(.99,1.43,.52),76),
 '10-reference-front':((-6.8,9.6,2.05),(0,0,.62),71),
 '11-reference-rear':((-6.8,-9.6,1.9),(0,-.1,.62),71)
}
# Persist one review scene with all cameras and packed source images.
for name,(loc,target,lens) in views.items():
 data=camdata.copy();obj=bpy.data.objects.new(name,data);env.objects.link(obj);obj.location=loc;obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler();data.lens=lens
 if name=='07-chase':data.sensor_fit='VERTICAL';data.sensor_height=24;data.lens=12/math.tan(math.radians(65/2))
# Source scene can be reopened without path dependency on project textures.
bpy.ops.file.pack_all()
scene.camera=bpy.data.objects['02-rear-three-quarter']
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Vector_CloudCity_Review.blend'))
# Model metrics count evaluated source, not review set/rivals.
deps=bpy.context.evaluated_depsgraph_get();triangles=0;count=0
for o in car.objects:
 if o.type in {'MESH','CURVE','FONT'}:
  ev=o.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles();triangles+=len(me.loop_triangles);count+=1;ev.to_mesh_clear()
(OUT/'manifest.json').write_text(json.dumps({'model':'Vector V04','baseline':'fb2d71a31099732659dfb0fb8731834e3ced5a71','axis':'+Y forward, +Z up, metres','triangles':triangles,'render_objects':count,'environment_sources':manifest,'layout':'Representative Blender set; not exact runtime track','views':views,'chase':'ChaseCamera.cs unsplit offsets: 10m behind, focus 1.1m + 4.8m up, target 9m forward; 65 degree vertical FOV from RaceDirector.cs','runtime_integrated':False},indent=2))
if CLAY:
 clay=material('Review neutral clay',(.45,.47,.50),.05,.55)
 for o in car.objects:
  if o.type in {'MESH','CURVE','FONT'}:
   for slot in o.material_slots:slot.link='OBJECT';slot.material=clay
selected=[] if NO_RENDER else (['10-reference-front','11-reference-rear','03-side'] if DRAFT else list(views))
for name in selected:
 scene.camera=bpy.data.objects[name];rivals.hide_render=name!='07-chase'
 if name=='07-chase':paint.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.018,.12,.75,1);scene.render.resolution_y=int(scene.render.resolution_x*9/16)
 else:paint.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.98,.60,.018,1);scene.render.resolution_y=900 if DRAFT else 1200
 scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
print('VECTOR BUILD COMPLETE',triangles,'triangles',count,'renderable source objects')
