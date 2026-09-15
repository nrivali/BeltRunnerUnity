"""Bake seamless, multiscale chipped stone relief in Blender; stage outside runtime resources."""
import bpy, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'ArtSource/AsteroidsV4/DetailRefinement'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1
bpy.ops.mesh.primitive_plane_add(size=2)
plane=bpy.context.object
mat=bpy.data.materials.new('Chipped stone relief');mat.use_nodes=True;mat.use_fake_user=True
plane.data.materials.append(mat);nt=mat.node_tree;nodes=nt.nodes;links=nt.links
bs=nodes.get('Principled BSDF');out=nodes.get('Material Output')
def mathnode(op,a,b=None):
    node=nodes.new('ShaderNodeMath');node.operation=op
    for i,x in enumerate((a,b)):
        if x is None:continue
        if isinstance(x,(int,float)):node.inputs[i].default_value=x
        else:links.new(x,node.inputs[i])
    return node.outputs[0]
uv=nodes.new('ShaderNodeTexCoord');xy=nodes.new('ShaderNodeSeparateXYZ');links.new(uv.outputs['UV'],xy.inputs[0])
u=mathnode('MULTIPLY',xy.outputs['X'],2*math.pi);v=mathnode('MULTIPLY',xy.outputs['Y'],2*math.pi)
vec=nodes.new('ShaderNodeCombineXYZ')
for slot,value in zip(vec.inputs,(mathnode('COSINE',u),mathnode('SINE',u),mathnode('COSINE',v))):links.new(value,slot)
w=mathnode('SINE',v)
def grain(scale,detail,rough):
    node=nodes.new('ShaderNodeTexNoise');node.noise_dimensions='4D'
    links.new(vec.outputs[0],node.inputs['Vector']);links.new(w,node.inputs['W'])
    node.inputs['Scale'].default_value=scale;node.inputs['Detail'].default_value=detail;node.inputs['Roughness'].default_value=rough
    return node.outputs['Fac']
macro=grain(2.5,3,.7);chips=grain(9,3,.75);fine=grain(34,2,.72)
vor=nodes.new('ShaderNodeTexVoronoi');vor.voronoi_dimensions='4D';vor.feature='DISTANCE_TO_EDGE'
links.new(vec.outputs[0],vor.inputs['Vector']);links.new(w,vor.inputs['W']);vor.inputs['Scale'].default_value=3.4
edge=nodes.new('ShaderNodeMapRange');edge.interpolation_type='SMOOTHERSTEP';edge.clamp=True
links.new(vor.outputs['Distance'],edge.inputs['Value'])
edge.inputs['From Min'].default_value=.005;edge.inputs['From Max'].default_value=.12
edge.inputs['To Min'].default_value=0;edge.inputs['To Max'].default_value=1
# Angular ridged noise chips interrupt the crack network; small grains are a separate frequency.
ridge=mathnode('ABSOLUTE',mathnode('SUBTRACT',mathnode('MULTIPLY',chips,2),1))
height=mathnode('ADD',mathnode('MULTIPLY',macro,.65),mathnode('MULTIPLY',ridge,.24))
# Interrupt fractures with broad mineral variation instead of outlining every Voronoi cell.
breaks=nodes.new('ShaderNodeMapRange');breaks.interpolation_type='SMOOTHERSTEP';breaks.clamp=True
links.new(macro,breaks.inputs['Value']);breaks.inputs['From Min'].default_value=.35;breaks.inputs['From Max'].default_value=.61
fracture=mathnode('MULTIPLY',mathnode('SUBTRACT',1,edge.outputs[0]),breaks.outputs[0])
height=mathnode('SUBTRACT',height,mathnode('MULTIPLY',fracture,.14))
height=mathnode('ADD',height,mathnode('MULTIPLY',fine,.09))
bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=1;bump.inputs['Distance'].default_value=.16
links.new(height,bump.inputs['Height']);links.new(bump.outputs[0],bs.inputs['Normal'])
bs.inputs['Base Color'].default_value=(.045,.038,.03,1);bs.inputs['Roughness'].default_value=.86
target=nodes.new('ShaderNodeTexImage');emit=nodes.new('ShaderNodeEmission')
surface=nodes.new('ShaderNodeCombineColor');surface.mode='RGB'
# Red: crevice occlusion. Green: neutral mineral variation. Blue: relief height, useful in source.
cavity=mathnode('SUBTRACT',1,mathnode('MULTIPLY',fracture,.38))
variation=mathnode('ADD',.24,mathnode('MULTIPLY',macro,.9))
links.new(cavity,surface.inputs[0]);links.new(variation,surface.inputs[1]);links.new(height,surface.inputs[2])
for suffix in ('normal','surface'):
    im=bpy.data.images.new('rock_detail_'+suffix,2048,2048,alpha=False)
    im.colorspace_settings.name='Non-Color';target.image=im;nodes.active=target
    if suffix=='normal':links.new(bs.outputs[0],out.inputs[0]);bpy.ops.object.bake(type='NORMAL')
    else:links.new(surface.outputs[0],emit.inputs[0]);links.new(emit.outputs[0],out.inputs[0]);bpy.ops.object.bake(type='EMIT')
    im.filepath_raw=str(OUT/('rock_detail_'+suffix+'.png'));im.file_format='PNG';im.save();im.pack()
    print('BAKED',im.filepath_raw,flush=True)
target.image=None;links.new(bs.outputs[0],out.inputs[0])
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'rock-relief.blend'),compress=True)
