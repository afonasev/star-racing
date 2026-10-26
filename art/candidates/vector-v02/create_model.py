"""Vector V02: editable curved-surface art candidate. Blender 5.2, metres, +Y forward.
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
paint=material('PlayerPaint',(.89,.53,.025),.65,.28)
p=paint.node_tree.nodes.get('Principled BSDF');p.inputs['Coat Weight'].default_value=.42;p.inputs['Coat Roughness'].default_value=.19
carbon=material('Graphite_Aero',(.022,.027,.035),.30,.36)
black=material('Recess_Black',(.005,.008,.012),.1,.5)
glass=material('Smoked_Glass',(.018,.036,.052),.30,.19)
glass.node_tree.nodes.get('Principled BSDF').inputs['Transmission Weight'].default_value=.32
glass.node_tree.nodes.get('Principled BSDF').inputs['IOR'].default_value=1.46
rubber=material('Performance_Rubber',(.014,.017,.021),.02,.68)
alloy=material('Forged_Titanium',(.22,.25,.30),.85,.26)
rim=material('Wheel_Graphite',(.055,.068,.086),.8,.29)
rotor=material('Ceramic_Rotor',(.15,.17,.19),.6,.52)
caliper=material('Caliper_Copper',(.58,.16,.035),.65,.32)
red=material('Red_LED',(.72,.004,.008),.15,.24,4)
white=material('White_LED',(.77,.9,1),.1,.2,3)
leather=material('Interior_Alcantara',(.014,.019,.026),0,.87)
silver=material('Lettering',(.72,.78,.84),.65,.35)

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

# Dense interpolated surface: smooth geometry in both directions, not only normals.
# Retain the Vector wedge while rounding fender shoulders and longitudinal transitions.
def catmull(a,b,c,d,t):
 return .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
def interpolate(points,steps=8,closed=False):
 pts=[Vector(p) for p in points];out=[];n=len(pts)
 for i in range(n if closed else n-1):
  a=pts[(i-1)%n] if closed else pts[max(0,i-1)]
  b=pts[i];c=pts[(i+1)%n];d=pts[(i+2)%n] if closed else pts[min(n-1,i+2)]
  for j in range(steps):out.append(catmull(a,b,c,d,j/steps))
 if not closed:out.append(pts[-1])
 return out
stations=[(-2.38,.98,.74,.87,.86),(-2.15,1.10,.83,1.01,.97),(-1.48,1.14,1.02,1.09,.985),(-.82,1.08,.79,.96,.90),(-.12,1.015,.70,.87,.83),(.70,1.04,.75,.90,.82),(1.44,1.12,1.02,1.10,.82),(1.95,1.07,.67,.85,.69),(2.36,.99,.50,.64,.59),(2.46,.81,.43,.55,.53)]
# Interpolate each scalar independently (mathutils vectors support at most 4 values).
dense=[]
for k in range(len(stations)-1):
 for j in range(10):
  dense.append([catmull(stations[max(0,k-1)][i],stations[k][i],stations[k+1][i],stations[min(len(stations)-1,k+2)][i],j/10) for i in range(5)])
dense.append(stations[-1]);verts=[]
for y,w,shoulder,upper,center in dense:
 section=[(-w*.90,.24),(-w,shoulder-.12),(-w*.96,shoulder),(-w*.78,upper),(-w*.52,center),(0,center+.012),(w*.52,center),(w*.78,upper),(w*.96,shoulder),(w,shoulder-.12),(w*.90,.24),(0,.235)]
 ring=interpolate(section,6,True)
 verts.extend([(v.x,y,v.y) for v in ring])
n=len(ring);faces=[tuple(range(n-1,-1,-1))]
for k in range(len(dense)-1):
 for i in range(n):faces.append((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i))
faces.append(tuple(range((len(dense)-1)*n,len(dense)*n)))
body=mesh('Monocoque curved body',verts,faces,paint)
for poly in body.data.polygons:poly.use_smooth=len(poly.vertices)==4
# True wheel openings through the shell, with tyres independent of body.
for y in [-1.48,1.44]:
 for side in [-1,1]:
  # Taper the inboard well to the tyre shoulder; a full cylinder cuts the hood.
  rings=[(.68,.38),(.74,.44),(.80,.485),(.86,.51),(1.50,.51)];n=128
  cv=[(side*x,y+r*math.sin(i*2*math.pi/n),.455+r*math.cos(i*2*math.pi/n)) for x,r in rings for i in range(n)]
  cf=[tuple(range(n-1,-1,-1))]+[(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(rings)-1) for i in range(n)]+[tuple(range((len(rings)-1)*n,len(rings)*n))]
  cutter=mesh('temporary shaped wheel well',cv,cf,black)
  mod=body.modifiers.new('Wheel opening','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
  bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
# Cut real exhaust bores through the rear shell.
for side in [-1,1]:
 cutter=cube('temporary exhaust bore',(side*.155,-2.29,.615),(.215,.65,.185),black,.026)
 bpy.context.view_layer.objects.active=cutter
 for mod in list(cutter.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
 mod=body.modifiers.new('Exhaust aperture','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
 bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
# Lower tail aperture exposes a real diffuser, not a painted solid block.
cutter=cube('temporary lower diffuser aperture',(0,-2.29,.24),(1.75,.70,.40),black,.02)
bpy.context.view_layer.objects.active=cutter
for mod in list(cutter.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
mod=body.modifiers.new('Diffuser opening','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
# Split the colour boundary geometrically so the rear insert has a clean edge.
bm=bmesh.new();bm.from_mesh(body.data)
for axis,value in [(1,-2.18),(2,.82)]:
 co=[0,0,0];co[axis]=value;normal=[0,0,0];normal[axis]=1
 bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.00001,plane_co=co,plane_no=normal,clear_inner=False,clear_outer=False)
bm.to_mesh(body.data);bm.free();body.data.update()
body.data.materials.append(carbon)
for f in body.data.polygons:
 if f.center.y < -2.18 and f.center.z < .82:f.material_index=1
mod=body.modifiers.new('Rounded wheel opening and bumper edges','BEVEL');mod.width=.014;mod.segments=4;mod.harden_normals=True
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


# Project dense panel topology onto the curved body; projecting only corners
# makes a flat panel intersect the skin between its vertices.
def surface_panel(name,points,axis,side,mat,offset=.012):
 o=mesh(name,points,[tuple(range(len(points)))],mat)
 bm=bmesh.new();bm.from_mesh(o.data)
 bmesh.ops.triangulate(bm,faces=list(bm.faces))
 bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=12,use_grid_fill=True)
 bm.to_mesh(o.data);bm.free()
 for v in o.data.vertices:v.co=skin_point(v.co,axis,side,offset)
 for f in o.data.polygons:f.use_smooth=True
 o.data.update();return o

# Underbody & sculpted side sills.
cube('Flat undertray',(0,0,.225),(1.88,4.58,.065),carbon,.025)
for s in [-1,1]:
 panel('Blade sill '+str(s),[(s*.95,-.93,.28),(s*1.10,-.85,.26),(s*1.04,.82,.255),(s*.95,.95,.32)],carbon,.055,.008)
 panel('Sill paint insert '+str(s),[(s*1.025,-.86,.32),(s*1.02,.74,.33),(s*.98,.81,.39),(s*.99,-.70,.39)],paint)
 # Door and rear quarter intake sit proud by only a few millimetres.
 surface_panel('Recessed side intake '+str(s),[(s*1.083,-.86,.87),(s*1.10,-1.015,.81),(s*1.104,-.96,.47),(s*1.064,-.73,.39),(s*1.059,-.65,.59)],0,s,black,.009)
 for j in range(5):
  z=.49+j*.06;line('Intake louvre '+str(s),skin_path([(s*1.112,-.98,z),(s*1.08,-.76,z-.035)],0,s),carbon,.010)
 line('Door shutline '+str(s),skin_path([(s*1,.73,.70),(s*1,.65,.40),(s*1,-.67,.389),(s*1,-.79,.60),(s*1,-.52,.875)],0,s),black,.003)
 cube('Flush door release '+str(s),(s*1.037,-.40,.76),(.014,.14,.027),rim,.007)

# Gently cambered windshield, canopy and side glass with explicit surface topology.
def curved_patch(name,corners,mat,bulge=(0,0,0),edge_bow=(0,0,0),nu=28,nv=20):
 a,b,c,d=map(Vector,corners);bow=Vector(bulge);edge=Vector(edge_bow)
 def point(u,v):
  return a.lerp(b,u).lerp(d.lerp(c,u),v)+bow*math.sin(math.pi*u)*math.sin(math.pi*v)+edge*math.sin(math.pi*u)
 vs=[tuple(point(i/nu,j/nv)) for j in range(nv+1) for i in range(nu+1)]
 fs=[(j*(nu+1)+i,j*(nu+1)+i+1,(j+1)*(nu+1)+i+1,(j+1)*(nu+1)+i) for j in range(nv) for i in range(nu)]
 obj=mesh(name,vs,fs,mat)
 for poly in obj.data.polygons:poly.use_smooth=True
 sol=obj.modifiers.new('Curved panel shell','SOLIDIFY');sol.thickness=.012;sol.offset=-1
 return point
wind=curved_patch('Double-curved panoramic windshield',[(-.72,.99,.855),(.72,.99,.855),(.565,.25,1.315),(-.565,.25,1.315)],glass,(0,.035,.035),(0,.018,.027))
roof=curved_patch('Crowned sculpted roof',[(-.565,.25,1.33),(.565,.25,1.33),(.59,-.36,1.355),(-.59,-.36,1.355)],paint,(0,0,.030),(0,0,.035))
back=curved_patch('Curved rear glass',[(-.59,-.39,1.335),(.59,-.39,1.335),(.74,-1.02,.985),(-.74,-1.02,.985)],glass,(0,-.025,.030),(0,0,.025))
for name,fun in [('Windshield',wind),('Roof',roof),('Rear glass',back)]:
 for edge in [0,1]:
  line(name+' transverse seal '+str(edge),[fun(i/32,edge) for i in range(33)],black if name!='Roof' else paint,.009)
for side in [-1,1]:
 sideglass=curved_patch('Convex side glass '+str(side),[(side*.742,.94,.86),(side*.587,.24,1.317),(side*.609,-.36,1.34),(side*.81,-.91,.985)],glass,(side*.028,0,0),(side*.014,0,0))
 for v in [0,1]:line('Cabin frame '+str(side),[sideglass(i/32,v) for i in range(33)],paint,.022)
 for u in [0,1]:line('Cabin rail '+str(side),[sideglass(u,i/32) for i in range(33)],paint,.019)
 line('Window divider '+str(side),[sideglass(i/24,.80) for i in range(25)],carbon,.010)
 sail=curved_patch('Sculpted flying buttress '+str(side),[(side*.63,-.43,1.29),(side*.78,-1.00,1.025),(side*.94,-1.62,1.023),(side*.78,-1.50,1.04)],paint,(side*.018,0,.025),nu=16,nv=12)
 beam('Mirror stalk '+str(side),(side*.79,.67,.90),(side*1.01,.65,1.015),.025,.025,carbon)
 mirror=cube('Rounded mirror shell '+str(side),(side*1.06,.63,1.025),(.17,.23,.083),paint,0)
 bevel_obj(mirror,.036,8)
 cube('Mirror glass '+str(side),(side*1.06,.511,1.025),(.135,.009,.046),alloy,.015)
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

# Hood shutlines and angular front bumper recessed mesh.
for s in [-1,1]:
 line('Hood panel gap '+str(s),skin_path([(s*.69,.95,.849),(s*.57,1.45,.797),(s*.55,1.95,.66),(s*.57,2.32,.569)],2),black,.002)
 surface_panel('Front light housing '+str(s),[(s*.59,2.4,.575),(s*.93,2.4,.575),(s*.93,2.4,.505),(s*.59,2.4,.505)],1,1,black,.014)
 line('Front LED blade '+str(s),[skin_point(p,1,offset=.017) for p in [(s*.63,2.4,.542),(s*.89,2.4,.542)]],white,.013)
 line('Front DRL lower '+str(s),[skin_point(p,1,offset=.018) for p in [(s*.64,2.4,.519),(s*.89,2.4,.519)]],white,.004)
 surface_panel('Corner front intake '+str(s),[(s*.70,2.445,.46),(s*.91,2.385,.49),(s*.94,2.35,.31),(s*.80,2.43,.31)],1,1,black,.012)
 line('Rounded bumper divider '+str(s),skin_path([(s*.65,2.47,.47),(s*.72,2.46,.29)],1),paint,.024)
# Main trapezoid vent and splitter.
panel('Main front grille',[(-.53,2.469,.469),(.53,2.469,.469),(.65,2.485,.255),(-.65,2.485,.255)],black,.03,.008)
panel('Front carbon splitter',[(-1.055,2.35,.244),(-.82,2.53,.244),(.82,2.53,.244),(1.055,2.35,.244),(1.0,2.10,.244),(-1.0,2.10,.244)],carbon,.035,.012)
for x in [-.38,0,.38]:panel('Front splitter upright',[(x,2.49,.25),(x,2.47,.45),(x,2.29,.42),(x,2.22,.25)],carbon,.018,.003)
# Grille slats are actual inset geometry.
for z in [.30,.34,.38,.42]:line('Front mesh horizontal',[(-.49,2.476,z),(.49,2.476,z)],rim,.003)
for x in [i*.055 for i in range(-9,10)]:line('Front mesh vertical',[(x,2.479,.29),(x,2.475,.435)],rim,.002)

# Engine hatch behind cabin, flush louvers, screws and rear lip.
# A tessellated hatch follows the sculpted rear deck.
vs=[];nx=18;ny=24
for j in range(ny+1):
 for i in range(nx+1):vs.append(skin_point((-.46+i/nx*.92,-1.03-j/ny*.86,1),2,1,.008))
fs=[(j*(nx+1)+i,j*(nx+1)+i+1,(j+1)*(nx+1)+i+1,(j+1)*(nx+1)+i) for j in range(ny) for i in range(nx)]
hatch=mesh('Contoured engine hatch',vs,fs,black)
for f in hatch.data.polygons:f.use_smooth=True
for i in range(9):
 y=-1.12-i*.085
 for j in range(16):
  xa=-.435+j*.87/16;xb=xa+.87/16
  pts=[skin_point(q,2,1,.018) for q in [(xa,y,1),(xb,y,1),(xb,y-.042,1),(xa,y-.042,1)]]
  panel('Engine heat extraction louvre',pts,carbon,.012,.002)
for side in [-1,1]:
 line('Deck panel seam '+str(side),skin_path([(side*.52,-1.03,.989),(side*.56,-1.98,.981),(side*.78,-2.19,.959)],2),black,.002)
 for y in [-1.08,-1.90]:
  cylinder('Hatch quarter-turn fastener',skin_point((side*.50,y,1),2,1,.015),.012,.006,alloy,axis='Z',vertices=12)
# Continuous integrated ducktail lip follows the deck from shoulder to shoulder.
vs=[]
for j in range(5):
 for i in range(49):
  x=-1.0+i/48*2;y=-2.12-j/4*.22
  q=Vector(skin_point((x,y,1),2,1,.006));q.z+=.028*(j/4)**2;vs.append(tuple(q))
fs=[(j*49+i,j*49+i+1,(j+1)*49+i+1,(j+1)*49+i) for j in range(4) for i in range(48)]
lip=mesh('Curved integrated tail lip',vs,fs,paint)
for f in lip.data.polygons:f.use_smooth=True
sol=lip.modifiers.new('Lip shell','SOLIDIFY');sol.thickness=.018
# Black tail mask, faceted bumper and distinctive inward red chevrons.
for s in [-1,1]:
 pts=[(x,y-.037,z) for x,y,z in skin_path([(s*.90,-2.402,.766),(s*.73,-2.407,.659),(s*.91,-2.402,.569)],1,-1)]
 line('Lamp recessed socket '+str(s),pts,black,.039)
 line('Lamp bezel '+str(s),pts,alloy,.024)
 line('Red chevron LED '+str(s),[(x,y-.060,z) for x,y,z in pts],red,.016)
 line('Secondary red light '+str(s),[(x,y-.035,z) for x,y,z in skin_path([(s*.98,-2.405,.758),(s*.82,-2.41,.659),(s*.98,-2.405,.576)],1,-1)],red,.006)
# Exhaust squared rings: hollow with thick titanium inner wall and dark depth.
def rounded_rect(cx,cz,w,h,r,n=5):
 pts=[]
 for ox,oz,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
  for i in range(n+1):
   a=math.radians(start+i*90/n);pts.append((cx+ox+r*math.cos(a),cz+oz+r*math.sin(a)))
 return pts
for s in [-1,1]:
 cx=s*.155;outer=rounded_rect(cx,.615,.267,.235,.045);inner=rounded_rect(cx,.615,.210,.180,.035)
 n=len(outer);vs=[(x,y,z) for points,y in [(outer,-2.42),(inner,-2.424),(inner,-2.16)] for x,z in points]
 fs=[]
 for i in range(n):
  j=(i+1)%n;fs.extend([(i,j,n+j,n+i),(n+i,n+j,2*n+j,2*n+i)])
 mesh('Hollow squared titanium exhaust '+str(s),vs,fs,alloy,.003)
 panel('Exhaust deep shadow '+str(s),[(cx-.103,-2.151,.526),(cx+.103,-2.151,.526),(cx+.103,-2.151,.703),(cx-.103,-2.151,.703)],black,.006,0)
 empty('Exhaust_L' if s<0 else 'Exhaust_R',(cx,-2.43,.615))
# Diffuser forms one integrated dark mass rather than a cavity of disconnected pieces.
panel('Rear diffuser ramp',[(-.91,-2.46,.24),(.91,-2.46,.24),(.74,-1.78,.43),(-.74,-1.78,.43)],carbon,.025,.006)
for x in [-.80,-.53,-.265,0,.265,.53,.80]:
 panel('Diffuser strake',[(x,-2.47,.23),(x,-2.41,.49),(x,-1.86,.40),(x,-1.75,.25)],carbon,.018,.005)
line('Rear lower accent',[(-.84,-2.465,.24),(.84,-2.465,.24)],rim,.012)
# Small embossed badge lives on the rear deck; no real manufacturer marks.
def lettering(name,text,loc,size,mat,rotation):
 c=bpy.data.curves.new(name,'FONT');c.body=text;c.align_x='CENTER';c.size=size;c.extrude=.0006;c.space_character=1.3
 o=bpy.data.objects.new(name,c);active.objects.link(o);c.materials.append(mat);o.location=loc;o.rotation_euler=rotation;return o
lettering('VECTOR rear badge','V E C T O R',(0,-2.402,.811),.060,silver,(math.pi/2,0,0))
lettering('Nose mark','V',(0,2.16,.61),.075,alloy,(0,0,math.pi))

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
   shape=[(.075,a-.13),(.31,a+.15),(.337,a+.36),(.315,a+.48),(.105,a+.16)]
   pts=[(ox+s*.016,y+r*math.sin(ang),z+r*math.cos(ang)) for r,ang in shape]
   panel(tag+' directional aero spoke',pts,rim,.025,.006)
   line(tag+' spoke diamond-cut edge',[(ox+s*.024,y+.10*math.sin(a+.05),z+.10*math.cos(a+.05)),(ox+s*.024,y+.325*math.sin(a+.32),z+.325*math.cos(a+.32))],alloy,.007)
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

# Rolled wheel-arch lips, fine hood extractor ribs and detailed projector optics.
for side in [-1,1]:
 for wy in [-1.48,1.44]:
  pts=[]
  for i in range(81):
   a=-math.pi*.60+i/80*math.pi*1.20
   q=(side*1.12,wy+.521*math.sin(a),.455+.521*math.cos(a))
   if q[2]>.30:pts.append(skin_point(q,0,side,.003))
  # No detached lip around unsupported lower parts of the arch.
  pts=[p for p in pts if abs(p[0])<1.20]
  if pts:line('Rolled painted arch edge '+str(side),pts,paint,.008)
 # Paired inset hood heat slots, with actual metal vanes.
 for j in range(3):
  x=side*(.73+j*.035)
  path=skin_path([(x,1.63,.96),(x,1.86,.86)],2)
  line('Hood extractor recess',path,black,.012)
  line('Hood extractor inner vane',[(a,b,c+.004) for a,b,c in path],carbon,.004)
 # Four compact projector cells under each light cover.
 for j in range(4):
  x=side*(.655+j*.061)
  pos=skin_point((x,2.40,.551),1,offset=.020)
  o=cube('Headlamp projector bezel',pos,(.050,.030,.032),alloy,.010)
  cube('Headlamp projector lens',(pos[0],pos[1]+.018,pos[2]),(.034,.009,.016),white,.006)
# Dense hexagonal rear grille sits within the dark tail band.
for row in range(3):
 for col in range(22):
  x=-.58+col*.055+(row%2)*.0275;z=.73+row*.020
  if abs(x)<.62:
   pts=[(x+.021*math.cos(k*math.pi/3),-2.395,z+.012*math.sin(k*math.pi/3)) for k in range(7)]
   line('Rear hex grille cell',pts,rim,.0018)

# Main authored hierarchy.
root=empty('Vector_V02',(0,0,0))
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
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Vector_V02.blend'))
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
bpy.ops.export_scene.fbx(filepath=str(OUT/'Vector_V02.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,use_mesh_modifiers=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(OUT/'Vector_V02.glb'),use_selection=True,export_format='GLB',export_apply=True)
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
 '09-wheel-detail':((3.1,3.3,1.1),(.99,1.43,.52),76)
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
(OUT/'manifest.json').write_text(json.dumps({'model':'Vector V02','baseline':'964bd9f98ae0a226c8bce64452f43b1ec624ad34','axis':'+Y forward, +Z up, metres','triangles':triangles,'render_objects':count,'environment_sources':manifest,'layout':'Representative Blender set; not exact runtime track','views':views,'chase':'ChaseCamera.cs unsplit offsets: 10m behind, focus 1.1m + 4.8m up, target 9m forward; 65 degree vertical FOV from RaceDirector.cs','runtime_integrated':False},indent=2))
selected=['02-rear-three-quarter','01-front-three-quarter'] if DRAFT else list(views)
for name in selected:
 scene.camera=bpy.data.objects[name];rivals.hide_render=name!='07-chase'
 if name=='07-chase':paint.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.018,.12,.75,1);scene.render.resolution_y=int(scene.render.resolution_x*9/16)
 else:paint.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.89,.53,.025,1);scene.render.resolution_y=900 if DRAFT else 1200
 scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
print('VECTOR BUILD COMPLETE',triangles,'triangles',count,'renderable source objects')
