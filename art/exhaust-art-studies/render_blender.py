"""CPU offline art-placement studies; not Unity runtime evidence."""
import bpy,math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2];out=root/'art/exhaust-art-studies'
bpy.ops.wm.open_mainfile(filepath=str(root/'art/candidates/cloudline-c05/Cloudline_C05.blend'))
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=12;s.cycles.device='CPU'
s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100
cam=s.camera;cam.location=(4.94,-11.11,4.94);cam.rotation_euler=(Vector((0,-1.48,.65))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.sensor_fit='VERTICAL';cam.data.sensor_height=32;cam.data.lens=16/math.tan(math.radians(17.5))
def material(style,cell):
 m=bpy.data.materials.new(style+str(cell));m.use_nodes=True;n=m.node_tree.nodes;n.clear();l=m.node_tree.links
 coord=n.new('ShaderNodeTexCoord');mapping=n.new('ShaderNodeVectorMath');mapping.operation='MULTIPLY_ADD';mapping.inputs[1].default_value=(.5,1,1);mapping.inputs[2].default_value=(.5*cell,0,0);l.new(coord.outputs['UV'],mapping.inputs[0])
 tex=n.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(out/(style+'.png')),check_existing=True);tex.extension='CLIP';l.new(mapping.outputs[0],tex.inputs['Vector'])
 emission=n.new('ShaderNodeEmission');emission.inputs['Strength'].default_value=1.7;l.new(tex.outputs['Color'],emission.inputs['Color'])
 transparent=n.new('ShaderNodeBsdfTransparent');mix=n.new('ShaderNodeMixShader');alpha=n.new('ShaderNodeMath');alpha.operation='MULTIPLY';alpha.inputs[1].default_value=.82;l.new(tex.outputs['Alpha'],alpha.inputs[0]);l.new(alpha.outputs[0],mix.inputs[0]);l.new(transparent.outputs[0],mix.inputs[1]);l.new(emission.outputs[0],mix.inputs[2]);output=n.new('ShaderNodeOutputMaterial');l.new(mix.outputs[0],output.inputs['Surface']);return m
for style in ['1-natural','2-plasma']:
 for mode,length,width,cell in [('gas',.9881,.36,0),('nitro',2.5919,.46,1)]:
  m=material(style,cell);objs=[]
  for x in [-.74,.74]:
   for i in range(3):
    a=i*math.pi/3;r=Vector((math.cos(a),0,math.sin(a)))*width*.5;base=Vector((x,-2.405,.7));tail=Vector((0,-length,0))
    vs=[base-r,base+r,base-r+tail,base+r+tail];mesh=bpy.data.meshes.new('study sheet');mesh.from_pydata(vs,[],[(0,2,1),(1,2,3)]);mesh.update();uv=mesh.uv_layers.new();coords=[(0,0),(1,0),(0,1),(1,1)]
    for poly in mesh.polygons:
     for index in poly.loop_indices:uv.data[index].uv=coords[mesh.loops[index].vertex_index]
    ob=bpy.data.objects.new('preview only',mesh);s.collection.objects.link(ob);mesh.materials.append(m);ob.visible_shadow=False;objs.append(ob)
  s.render.filepath=str(out/(style+'-'+mode+'-blender.png'));bpy.ops.render.render(write_still=True)
  for ob in objs:bpy.data.objects.remove(ob,do_unlink=True)
print('EXHAUST_ART_BLENDER_STUDIES_OK styles=2 states=gas,nitro')
