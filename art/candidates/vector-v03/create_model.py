"""Vector V03: editable hard-surface art candidate. Blender 5.2, metres, +Y forward.
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
paint=material('PlayerPaint',(.98,.60,.018),.48,.24)
p=paint.node_tree.nodes.get('Principled BSDF');p.inputs['Coat Weight'].default_value=.42;p.inputs['Coat Roughness'].default_value=.19
carbon=material('Graphite_Aero',(.022,.027,.035),.30,.36)
black=material('Recess_Black',(.005,.008,.012),.1,.5)
glass=material('Smoked_Glass',(.012,.020,.029),0,.17)
glass.node_tree.nodes.get('Principled BSDF').inputs['Transmission Weight'].default_value=.25
glass.node_tree.nodes.get('Principled BSDF').inputs['IOR'].default_value=1.46
rubber=material('Performance_Rubber',(.014,.017,.021),.02,.68)
alloy=material('Forged_Titanium',(.22,.25,.30),.85,.26)
rim=material('Wheel_Graphite',(.055,.068,.086),.8,.29)
rotor=material('Ceramic_Rotor',(.065,.073,.085),.55,.48)
caliper=material('Caliper_Copper',(.58,.16,.035),.65,.32)
red=material('Red_LED',(.72,.004,.008),.15,.24,4)
white=material('White_LED',(.77,.9,1),.1,.2,3)
leather=material('Interior_Alcantara',(.014,.019,.026),0,.87)
silver=material('Lettering',(.72,.78,.84),.65,.35)
grille_metal=material('Grille_Titanium',(.13,.155,.18),.45,.38)

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

# Continuous faceted body. Cross sections: y,width,shoulder_z,upper_z,center_z.
stations=[(-2.38,1.075,.88,.965,.945),(-2.15,1.15,.90,1.04,1.015),(-1.86,1.145,.98,1.055,1.005),(-1.60,1.14,1.015,1.08,.995),(-1.34,1.135,1.005,1.075,.985),(-1.08,1.12,.92,1.015,.96),(-.82,1.08,.79,.94,.90),(-.12,1.015,.70,.85,.83),(.70,1.04,.75,.86,.80),(.98,1.065,.88,.965,.80),(1.20,1.09,.99,1.055,.795),(1.48,1.105,1.015,1.07,.79),(1.73,1.11,.94,1.02,.78),(1.95,1.10,.78,.90,.75),(2.20,1.09,.68,.79,.69),(2.38,1.07,.61,.735,.675),(2.48,1.01,.58,.705,.665)]
verts=[]
for y,w,shoulder,upper,center in stations:
 verts.extend([(-w*.90,y,.24),(-w,y,shoulder-.12),(-w*.96,y,shoulder),(-w*.78,y,upper),(-w*.52,y,center),(w*.52,y,center),(w*.78,y,upper),(w*.96,y,shoulder),(w,y,shoulder-.12),(w*.90,y,.24)])
faces=[tuple(range(9,-1,-1))]
for k in range(len(stations)-1):
 for i in range(10):faces.append((k*10+i,k*10+(i+1)%10,(k+1)*10+(i+1)%10,(k+1)*10+i))
faces.append(tuple(range((len(stations)-1)*10,len(stations)*10)))
body=mesh('Monocoque faceted body',verts,faces,paint)
# True wheel openings through the shell, with tyres independent of body.
for y in [-1.48,1.44]:
 for side in [-1,1]:
  # Taper the inboard well to the tyre shoulder; a full cylinder cuts the hood.
  rings=[(.68,.38),(.74,.44),(.80,.485),(.86,.51),(1.50,.51)];n=96
  cv=[(side*x,y+r*math.sin(i*2*math.pi/n),.455+r*math.cos(i*2*math.pi/n)) for x,r in rings for i in range(n)]
  cf=[tuple(range(n-1,-1,-1))]+[(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(rings)-1) for i in range(n)]+[tuple(range((len(rings)-1)*n,len(rings)*n))]
  cutter=mesh('temporary shaped wheel well',cv,cf,black)
  mod=body.modifiers.new('Wheel opening','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
  bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
# Open the actual shell behind the fascia, rather than drawing black patches on it.
def coord(u,v,axis,value):
 return (value,u,v) if axis==0 else ((u,value,v) if axis==1 else (u,v,value))
def cut_prism(name,outline,axis,a,b):
 n=len(outline);vs=[coord(u,v,axis,d) for d in [a,b] for u,v in outline]
 fs=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 cutter=mesh('CUT '+name,vs,fs,black)
 mod=body.modifiers.new(name,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
 bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
cut_prism('Open front lower fascia',[(-1.5,.15),(1.5,.15),(1.5,.655),(-1.5,.655)],1,2.12,3)
cut_prism('Open rear layered fascia',[(-1.5,.15),(1.5,.15),(1.5,.90),(-1.5,.90)],1,-3,-2.045)
side_outline=[(-.98,.90),(-.72,.80),(-.64,.40),(-.85,.34),(-.98,.49)]
# Capture the original faceted body before cutting the side recess. Its rim must
# follow the body, rather than floating on an arbitrary constant-X plane.
side_surfaces={}
for side in [-1,1]:
 outer=[];inner=[];cy=sum(p[0] for p in side_outline)/5;cz=sum(p[1] for p in side_outline)/5
 for scale,arr in [(1,outer),(.80,inner)]:
  outline=[(cy+(y-cy)*scale,cz+(z-cz)*scale) for y,z in side_outline]
  for i,(ya,za) in enumerate(outline):
   yb,zb=outline[(i+1)%len(outline)]
   for j in range(10):
    y=ya+(yb-ya)*j/10;z=za+(zb-za)*j/10
    hit,co,normal,index=body.ray_cast(Vector((side*5,y,z)),Vector((-side,0,0)))
    arr.append((co.x+side*.003,y,z) if hit else (side*.98,y,z))
 side_surfaces[side]=(outer,inner)
for side in [-1,1]:
 cut_prism('Deep side intake',side_outline,0,side*.85,side*1.45)
 lamp_outline=[(side*.51,.645),(side*1.02,.665),(side*1.03,.75),(side*.55,.735)]
 cut_prism('Headlight pocket',lamp_outline,1,2.32,2.65)
bevel_obj(body,.012,3)
bpy.context.view_layer.update()
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
 if grille:honeycomb(name+' perforated insert',deep,axis,back+(front-back)*.72)
 return inner

# Underbody & sculpted side sills.
cube('Flat undertray',(0,0,.225),(1.88,4.58,.065),carbon,.025)
for s in [-1,1]:
 panel('Blade sill '+str(s),[(s*.95,-.93,.28),(s*1.10,-.85,.26),(s*1.04,.82,.255),(s*.95,.95,.32)],carbon,.055,.008)
 panel('Sill paint insert '+str(s),[(s*1.025,-.86,.32),(s*1.02,.74,.33),(s*.98,.81,.39),(s*.99,-.70,.39)],paint)
 # Tall three-dimensional rear-quarter intake, framed like the reference.
 outer,inner=side_surfaces[s];n=len(outer)
 mesh('Body-fitted intake rim '+str(s),outer+inner,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],paint,.003)
 back=[(x-s*.13,y,z) for x,y,z in inner]
 mesh('Side intake tunnel '+str(s),inner+back,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],carbon)
 panel('Side intake deep shadow '+str(s),back,black,.006,0)
 for j in range(6):
  z=.42+j*.055
  beam('Side intake inner blade',(s*.98,-.90,z),(s*.98,-.75,z+.025),.014,.012,carbon)
 line('Door shutline '+str(s),skin_path([(s*1,.73,.70),(s*1,.65,.40),(s*1,-.67,.389),(s*1,-.79,.60),(s*1,-.52,.875)],0,s),black,.003)
 cube('Flush door release '+str(s),(s*1.037,-.40,.76),(.014,.14,.027),rim,.007)

# Trapezoidal cabin: separate glass, frame and roof panels.
# Front screen lower edge y=.96 z=.84, roof front y=.25 z=1.30.
panel('Panoramic windscreen',[(-.72,.99,.845),(.72,.99,.845),(.565,.25,1.305),(-.565,.25,1.305)],glass,.009,.008)
panel('Angular roof substrate',[(-.565,.25,1.32),(.565,.25,1.32),(.59,-.36,1.345),(-.59,-.36,1.345)],carbon,.025,.005)
for side in [-1,1]:
 panel('Roof paint side blade',[(side*.565,.25,1.329),(side*.39,.25,1.329),(side*.405,-.36,1.354),(side*.59,-.36,1.354)],paint,.012,.004)
for y,z in [(.25,1.329),(-.36,1.354)]:cube('Roof transverse painted rail',(0,y,z),(1.13,.055,.022),paint,.004)
panel('Rear screen',[(-.59,-.39,1.325),(.59,-.39,1.325),(.74,-1.02,.973),(-.74,-1.02,.973)],glass,.01,.006)
for s in [-1,1]:
 panel('Side cabin glass '+str(s),[(s*.742,.94,.85),(s*.587,.24,1.307),(s*.609,-.36,1.33),(s*.77,-.91,.979),(s*.875,-.55,.91)],glass,.008,.007)
 beam('Planar A pillar '+str(s),(s*.728,.987,.852),(s*.578,.251,1.318),.042,.027,paint)
 beam('Planar roof rail '+str(s),(s*.578,.251,1.326),(s*.604,-.369,1.347),.035,.026,paint)
 beam('Planar rear sail frame '+str(s),(s*.604,-.369,1.334),(s*.77,-.99,.977),.055,.032,paint)
 line('Glazing lower gasket '+str(s),[(s*.739,.97,.849),(s*.883,-.55,.913),(s*.774,-.94,.976)],black,.012)
 line('Window divider '+str(s),[(s*.608,-.31,1.323),(s*.865,-.48,.929)],carbon,.011)
 panel('Rear flying sail '+str(s),[(s*.63,-.43,1.29),(s*.80,-1.04,1.00),(s*.91,-1.56,1.015),(s*.76,-1.51,1.028)],paint,.02,.007)
 beam('Mirror stalk '+str(s),(s*.79,.67,.90),(s*1.01,.65,1.015),.025,.025,carbon)
 mirror=cube('Angular mirror shell '+str(s),(s*1.06,.63,1.025),(.17,.23,.078),paint,.025)
 cube('Mirror glass '+str(s),(s*1.06,.511,1.025),(.135,.009,.046),alloy,.009)
# A visible simplified interior, actual geometry beneath tinted glazing.
cube('Cabin floor',(0,-.04,.70),(1.30,1.58,.06),leather,.025)
cube('Dashboard',(0,.62,.91),(1.27,.22,.13),leather,.035)
for x in [-.34,.34]:
 cube('Seat cushion',(x,-.13,.82),(.39,.43,.13),leather,.055)
 seat=cube('Bucket seat back',(x,-.36,1.015),(.37,.13,.39),leather,.05);seat.rotation_euler[0]=-.12
 cube('Headrest',(x,-.405,1.20),(.20,.11,.13),leather,.025)
 for s in [-1,1]:line('Seat piping',[(x+s*.175,.04,.87),(x+s*.175,-.28,.90),(x+s*.15,-.30,1.15)],caliper,.007)
# Steering yoke and central instrument display.
line('Steering yoke',[(-.49,.37,1.00),(-.49,.33,1.09),(-.19,.33,1.09),(-.19,.37,1.00),(-.28,.37,.98)],carbon,.018)
cube('Cockpit display',(-.34,.53,1.01),(.25,.024,.10),black,.008)

# Reference-like front: broad twin-segment light units and three deep air apertures.
for side in [-1,1]:
 line('Hood panel gap '+str(side),skin_path([(side*.69,.95,.85),(side*.57,1.45,.80),(side*.55,1.95,.75),(side*.57,2.38,.68)],2),black,.002)
 opening=[(side*.51,.645),(side*1.02,.665),(side*1.03,.75),(side*.55,.735)]
 duct('Recessed headlight '+str(side),opening,1,2.465,2.22,carbon,.86,False)
 for j in range(2):
  xa=.565+j*.212;xb=xa+.19;z=.682+j*.007
  pts=[(side*xa,2.478,z),(side*xb,2.478,z+.007),(side*(xb+.012),2.478,z+.035),(side*(xa+.013),2.478,z+.028)]
  panel('Rectangular projector optic '+str(side),pts,white,.010,.003)
 line('Headlight lower polished reflector '+str(side),[(side*.565,2.472,.662),(side*.99,2.472,.679)],alloy,.004)
 vent=[(side*.585,.58),(side*1.065,.615),(side*1.065,.27),(side*.77,.27)]
 duct('Front side air intake '+str(side),vent,1,2.455,2.16,paint,.78)
 for z in [.34,.385,.43,.475,.52]:
  line('Front inlet shadow vane',[(side*.78,2.21,z),(side*1.015,2.21,z)],carbon,.006)
 # Creased fender cap covers the pocket, tapering into the original shoulder.
 cap=[skin_point((side*.57,2.02,1),2,1,.004),skin_point((side*.83,2.02,1),2,1,.004),skin_point((side*1.065,2.02,1),2,1,.004),(side*1.03,2.465,.758),(side*.80,2.465,.765),(side*.51,2.465,.742)]
 lower_front=skin_point((side*.51,2.465,1),2,1,.002)
 lower_rear=skin_point((side*.57,2.02,1),2,1,.002)
 mesh('Headlight integrated fender cap '+str(side),cap+[lower_front,lower_rear],[(0,1,4,5),(1,2,3,4),(0,5,6,7)],paint,.005)
 panel('Lamp-to-bumper painted belt '+str(side),[(side*.51,2.466,.645),(side*1.04,2.466,.662),(side*1.07,2.455,.615),(side*.585,2.455,.580)],paint,.025,.004)
 panel('Lamp outer shoulder closure '+str(side),[(side*1.03,2.465,.758),(side*1.065,2.455,.615),(side*1.11,2.15,.73),(side*1.065,2.02,.75)],paint,.025,.006)
 # Outer bumper cheek wraps into the shoulder; three large planar facets.
 mesh('Faceted front cheek '+str(side),[(side*1.065,2.455,.615),(side*1.11,2.15,.73),(side*1.13,2.03,.42),(side*1.09,2.26,.25),(side*1.065,2.455,.27)],[(0,1,2),(0,2,3,4)],paint,.009)
 # Narrow triangular shadow vent on one fender facet, like the source concept.
 pts=[skin_point(q,2,1,.008) for q in [(side*.68,1.48,1),(side*.81,1.52,1),(side*.73,1.90,1)]]
 obj=mesh('Angular fender extractor '+str(side),pts,[(0,1,2)],black)
 bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=8,use_grid_fill=True);bm.to_mesh(obj.data);bm.free()
 for v in obj.data.vertices:v.co=skin_point(v.co,2,1,.007)
 obj.data.update()
main_vent=[(-.55,.595),(.55,.595),(.76,.245),(-.76,.245)]
duct('Central front intake',main_vent,1,2.46,2.14,carbon,.92)
panel('Front carbon splitter',[(-1.16,2.30,.226),(-1.09,2.53,.226),(1.09,2.53,.226),(1.16,2.30,.226),(1.08,2.05,.226),(-1.08,2.05,.226)],carbon,.033,.007)
for x in [-.34,.34]:
 panel('Main intake vertical guide',[(x,2.48,.26),(x*.80,2.46,.575),(x*.8,2.17,.56),(x,2.16,.26)],carbon,.016,.003)
for side in [-1,1]:
 line('Splitter fine machined edge',[(side*1.09,2.538,.228),(side*.78,2.538,.228)],alloy,.003)

# Rear engine cover: separate precision facets, a dark aperture and inset slats.
pts=[skin_point(q,2,1,.006) for q in [(-.48,-1.02,1),(.48,-1.02,1),(.52,-1.94,1),(-.52,-1.94,1)]]
panel('Engine hatch black aperture',pts,black,.012,.003)
for i in range(10):
 y=-1.09-i*.08
 pts=[skin_point(q,2,1,.016) for q in [(-.445,y,1),(.445,y,1),(.445,y-.046,1),(-.445,y-.046,1)]]
 panel('Engine extraction louvre',pts,carbon,.012,.003)
for side in [-1,1]:
 line('Deck panel seam '+str(side),skin_path([(side*.53,-1.02,1),(side*.57,-1.96,1),(side*.94,-2.19,1)],2),black,.002)
 for y in [-1.08,-1.90]:cylinder('Hatch quarter-turn fastener',skin_point((side*.50,y,1),2,1,.014),.010,.006,alloy,axis='Z',vertices=12)
# Angular roof-to-deck buttresses and upper rear lip remain body coloured.
mesh('Integrated rear deck wedge',[(-1.08,-2.405,.925),(1.08,-2.405,.925),(1.10,-2.16,1.035),(-1.10,-2.16,1.035),(-1.08,-2.405,.885),(1.08,-2.405,.885),(1.10,-2.16,.93),(-1.10,-2.16,.93)],[(0,1,2,3),(0,4,5,1),(0,3,7,4),(1,5,6,2),(4,7,6,5)],paint,.006)
for side in [-1,1]:
 outline=[(side*.40,.89),(side*.98,.90),(side*1.105,.79),(side*1.045,.635),(side*.50,.65),(side*.62,.765)]
 duct('Rear lamp chamber '+str(side),outline,1,-2.415,-2.14,paint,.86)
 # Broad flat LED chevron buried in a real lamp chamber, not neon tubing.
 pts=[(side*x,-2.445,z) for x,z in [(.93,.856),(.625,.766),(.91,.668),(.95,.694),(.72,.767),(.97,.830)]]
 panel('Inward red LED signature '+str(side),pts,red,.018,.003)
 panel('Rear angular paint bridge '+str(side),[(side*.40,-2.425,.64),(side*1.05,-2.425,.625),(side*1.11,-2.37,.47),(side*.91,-2.43,.44),(side*.39,-2.445,.54)],paint,.035,.009)
 profile=[(side*1.065,-2.405,.91),(side*1.12,-2.01,.94),(side*1.16,-2.01,.80),(side*1.075,-2.01,.36),(side*1.065,-2.36,.24),(side*.97,-2.42,.30),(side*1.04,-2.43,.63)]
 inner=[(x-side*.075,y,z) for x,y,z in profile]
 outer_faces=[(0,1,2,6),(2,3,4,5,6)]
 faces=outer_faces+[tuple(i+7 for i in reversed(f)) for f in outer_faces]+[(i,(i+1)%7,(i+1)%7+7,i+7) for i in range(7)]
 mesh('Rear outer faceted buttress '+str(side),profile+inner,faces,paint,.009)
# Central exhaust island, tilted rim and a thin badge ledge.
duct('Central titanium exhaust island',[(-.39,.80),(.39,.80),(.42,.52),(-.42,.52)],1,-2.42,-2.10,carbon,.92,False)
# Remove the island backing at the two exhaust openings by keeping its shadow behind sockets.
for o in list(car.objects):
 if o.name=='Central titanium exhaust island internal darkness':bpy.data.objects.remove(o,do_unlink=True)
# Exhaust squared rings: hollow with thick titanium inner wall and dark depth.
def rounded_rect(cx,cz,w,h,r,n=5):
 pts=[]
 for ox,oz,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
  for i in range(n+1):
   a=math.radians(start+i*90/n);pts.append((cx+ox+r*math.cos(a),cz+oz+r*math.sin(a)))
 return pts
for s in [-1,1]:
 cx=s*.175;outer=rounded_rect(cx,.655,.300,.255,.035);inner=rounded_rect(cx,.655,.238,.195,.026)
 n=len(outer);vs=[(x,y,z) for points,y in [(outer,-2.465),(inner,-2.469),(inner,-2.16)] for x,z in points]
 fs=[]
 for i in range(n):
  j=(i+1)%n;fs.extend([(i,j,n+j,n+i),(n+i,n+j,2*n+j,2*n+i)])
 mesh('Hollow squared titanium exhaust '+str(s),vs,fs,alloy,.003)
 panel('Exhaust deep shadow '+str(s),[(cx-.118,-2.151,.557),(cx+.118,-2.151,.557),(cx+.118,-2.151,.753),(cx-.118,-2.151,.753)],black,.006,0)
 empty('Exhaust_L' if s<0 else 'Exhaust_R',(cx,-2.48,.655))
# Broad angular diffuser mouth: shaped surround, deep ramp, five substantial vanes.
duct('Rear diffuser mouth',[(-.93,.51),(-.48,.57),(.48,.57),(.93,.51),(1.05,.22),(-1.05,.22)],1,-2.465,-1.88,carbon,.90,False)
panel('Rear diffuser undertray',[(-1.03,-2.49,.22),(1.03,-2.49,.22),(.80,-1.80,.42),(-.80,-1.80,.42)],carbon,.025,.005)
for x in [-.78,-.40,0,.40,.78]:
 panel('Tall diffuser vane',[(x,-2.49,.21),(x*.88,-2.46,.52),(x*.87,-1.91,.44),(x,-1.78,.25)],carbon,.026,.005)
# Small embossed badge lives on the rear deck; no real manufacturer marks.
def lettering(name,text,loc,size,mat,rotation):
 c=bpy.data.curves.new(name,'FONT');c.body=text;c.align_x='CENTER';c.size=size;c.extrude=.0006;c.space_character=1.3
 o=bpy.data.objects.new(name,c);active.objects.link(o);c.materials.append(mat);o.location=loc;o.rotation_euler=rotation;return o
lettering('VECTOR rear badge','V E C T O R',(0,-2.435,.835),.048,silver,(math.pi/2,0,0))
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
# Lower the canopy while retaining straight glass and roof planes.
# Large planar facets are intentional; only millimetre edge radii catch highlights.
bpy.context.view_layer.update()
for o in car.objects:
 if o.type=='MESH':
  inv=o.matrix_world.inverted()
  for v in o.data.vertices:
   co=o.matrix_world@v.co
   if co.z>1.0:co.z=1.0+(co.z-1.0)*.78;v.co=inv@co
  o.data.update()
 elif o.type=='CURVE':
  for spline in o.data.splines:
   for point in spline.points:
    if point.co.z>1.0:point.co.z=1.0+(point.co.z-1.0)*.78

# Main authored hierarchy.
root=empty('Vector_V03',(0,0,0))
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
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Vector_V03.blend'))
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
bpy.ops.export_scene.fbx(filepath=str(OUT/'Vector_V03.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,use_mesh_modifiers=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(OUT/'Vector_V03.glb'),use_selection=True,export_format='GLB',export_apply=True)
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
 '10-reference-front':((6.5,8.5,1.8),(0,0,.64),58),
 '11-reference-rear':((6.5,-8.5,1.8),(0,-.1,.64),58)
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
(OUT/'manifest.json').write_text(json.dumps({'model':'Vector V03','baseline':'b0e960ce0226d9778de7feac2048b9240e8459cc','axis':'+Y forward, +Z up, metres','triangles':triangles,'render_objects':count,'environment_sources':manifest,'layout':'Representative Blender set; not exact runtime track','views':views,'chase':'ChaseCamera.cs unsplit offsets: 10m behind, focus 1.1m + 4.8m up, target 9m forward; 65 degree vertical FOV from RaceDirector.cs','runtime_integrated':False},indent=2))
selected=['10-reference-front','11-reference-rear'] if DRAFT else list(views)
for name in selected:
 scene.camera=bpy.data.objects[name];rivals.hide_render=name!='07-chase'
 if name=='07-chase':paint.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.018,.12,.75,1);scene.render.resolution_y=int(scene.render.resolution_x*9/16)
 else:paint.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.98,.60,.018,1);scene.render.resolution_y=900 if DRAFT else 1200
 scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
print('VECTOR BUILD COMPLETE',triangles,'triangles',count,'renderable source objects')
