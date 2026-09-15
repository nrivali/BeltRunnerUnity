"""Blender: sculpt and bake the approved dry Ferron concept. Stages assets, never edits live resources."""
from pathlib import Path
import bpy,bmesh,math,random,json
import numpy as np
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'ArtSource/DryPlanetV1'
TEX=OUT/'textures';TEX.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1;scene.render.bake.margin=8
scene.view_settings.view_transform='AgX'
bpy.ops.mesh.primitive_uv_sphere_add(segments=512,ring_count=256,radius=1)
planet=bpy.context.object;planet.name='Ferron_Dry_Terrain'
for f in planet.data.polygons:f.use_smooth=True
mat=bpy.data.materials.new('Dry planet geology source');mat.use_nodes=True;mat.use_fake_user=True
planet.data.materials.append(mat);nt=mat.node_tree;nt.nodes.clear()
def node(kind,label=None):
    n=nt.nodes.new(kind)
    if label:n.label=label
    return n
def link(a,b):nt.links.new(a,b)
def mathn(op,a,b=None):
    m=node('ShaderNodeMath');m.operation=op
    for i,x in enumerate((a,b)):
        if x is None:continue
        if isinstance(x,(int,float)):m.inputs[i].default_value=x
        else:link(x,m.inputs[i])
    return m.outputs[0]
def vector(op,a,b=None,scale=None):
    m=node('ShaderNodeVectorMath');m.operation=op
    for i,x in enumerate((a,b)):
        if x is None:continue
        if isinstance(x,tuple):m.inputs[i].default_value=x
        else:link(x,m.inputs[i])
    if scale is not None:
        if isinstance(scale,(int,float)):m.inputs['Scale'].default_value=scale
        else:link(scale,m.inputs['Scale'])
    return m.outputs['Value'] if op in ('DISTANCE','DOT_PRODUCT','LENGTH') else m.outputs['Vector']
def remap(value,lo,hi,to0=0,to1=1,smooth=True):
    m=node('ShaderNodeMapRange');m.clamp=True;m.interpolation_type='SMOOTHERSTEP' if smooth else 'LINEAR'
    link(value,m.inputs['Value']);m.inputs['From Min'].default_value=lo;m.inputs['From Max'].default_value=hi
    m.inputs['To Min'].default_value=to0;m.inputs['To Max'].default_value=to1
    return m.outputs[0]
def texnoise(coord,scale,detail=4,rough=.7):
    n=node('ShaderNodeTexNoise');link(coord,n.inputs['Vector']);n.inputs['Scale'].default_value=scale
    n.inputs['Detail'].default_value=detail;n.inputs['Roughness'].default_value=rough
    return n
tc=node('ShaderNodeTexCoord');direction=vector('NORMALIZE',tc.outputs['Object'])
warp=texnoise(direction,2.4,4);distort=vector('SCALE',vector('SUBTRACT',warp.outputs['Color'],(.5,.5,.5)),scale=.48)
q=vector('ADD',direction,distort)
continents=texnoise(q,3.2,3).outputs['Fac'];uplift=remap(continents,.42,.55)
ridge=mathn('ABSOLUTE',mathn('SUBTRACT',mathn('MULTIPLY',texnoise(q,16,5).outputs['Fac'],2),1))
ridge=mathn('SUBTRACT',1,ridge)
geology=texnoise(q,45,3).outputs['Fac']
fine=texnoise(direction,220,3).outputs['Fac']
vor=node('ShaderNodeTexVoronoi','Warped branching canyon network');vor.feature='DISTANCE_TO_EDGE';vor.inputs['Scale'].default_value=3.2;link(q,vor.inputs['Vector'])
valley=mathn('SUBTRACT',1,remap(vor.outputs['Distance'],.006,.073))
valley=mathn('MULTIPLY',valley,remap(texnoise(q,2.1,2).outputs['Fac'],.27,.53))
# Terraced plateaus, deeply incised irregular canyons, and finer mountain erosion.
height=mathn('ADD',mathn('MULTIPLY',uplift,.48),mathn('MULTIPLY',ridge,.065))
height=mathn('ADD',height,mathn('MULTIPLY',geology,.028))
height=mathn('SUBTRACT',height,mathn('MULTIPLY',valley,.28))
height=mathn('ADD',height,mathn('MULTIPLY',fine,.008))
# Separate impact structures are radial profiles, never painted circles or repeating craters.
rng=random.Random(61408);crater_profiles=[];impacts=0
axes=[((.18,-.94,.29),.135),((.53,-.78,-.29),.082),((-.72,-.65,.13),.058)]
for _ in range(26):
    axis=Vector(tuple(rng.uniform(-1,1) for i in range(3))).normalized()
    axes.append((tuple(axis),rng.uniform(.019,.075)))
for axis,radius in axes:
    axis=tuple(Vector(axis).normalized());d=vector('DISTANCE',direction,axis)
    t=mathn('DIVIDE',d,radius)
    bowl=mathn('EXPONENT',mathn('MULTIPLY',mathn('POWER',t,4),-2.2))
    rim=mathn('EXPONENT',mathn('MULTIPLY',mathn('POWER',mathn('SUBTRACT',t,1),2),-70))
    peak=mathn('EXPONENT',mathn('MULTIPLY',mathn('POWER',t,2),-55))
    profile=mathn('SUBTRACT',mathn('ADD',mathn('MULTIPLY',rim,.10),mathn('MULTIPLY',peak,.045)),mathn('MULTIPLY',bowl,.15))
    impacts=mathn('ADD',impacts,profile)
    crater_profiles.append(dict(direction=axis,angular_radius=radius))
height=mathn('ADD',height,impacts)
# Material colour tracks major terrain, with the Imagegen tile supplying mineral variation.
palette=node('ShaderNodeValToRGB');ramp=palette.color_ramp
ramp.elements.remove(ramp.elements[1]);ramp.elements[0].position=0;ramp.elements[0].color=(.055,.045,.035,1)
for p,c in [(.24,(.095,.067,.042,1)),(.48,(.24,.135,.068,1)),(.73,(.34,.23,.13,1)),(1,(.47,.36,.23,1))]:ramp.elements.new(p).color=c
link(mathn('ADD',mathn('MULTIPLY',uplift,.75),mathn('MULTIPLY',geology,.25)),palette.inputs[0])
source=bpy.data.images.load(str(OUT/'imagegen-minerals.png'));source.pack()
src=node('ShaderNodeTexImage','Imagegen mineral colour');src.image=source;src.projection='BOX';src.projection_blend=.4
link(vector('SCALE',direction,scale=2.6),src.inputs['Vector'])
gray=node('ShaderNodeRGBToBW');link(src.outputs['Color'],gray.inputs[0])
mineral=remap(gray.outputs[0],.035,.42,.65,1.23)
color=vector('SCALE',palette.outputs['Color'],scale=mineral)
cavity=mathn('SUBTRACT',1,mathn('MULTIPLY',valley,.22))
color=vector('SCALE',color,scale=cavity)
bs=node('ShaderNodeBsdfPrincipled');output=node('ShaderNodeOutputMaterial');link(bs.outputs[0],output.inputs[0])
link(color,bs.inputs['Base Color']);bs.inputs['Metallic'].default_value=0
rough=remap(geology,0,1,.96,.79);link(rough,bs.inputs['Roughness'])
bump=node('ShaderNodeBump','Canyon and crater relief');bump.inputs['Strength'].default_value=1;bump.inputs['Distance'].default_value=.012;link(height,bump.inputs['Height'])
grainbump=node('ShaderNodeBump','Fine mineral grain');grainbump.inputs['Strength'].default_value=.35;grainbump.inputs['Distance'].default_value=.00015
link(gray.outputs[0],grainbump.inputs['Height']);link(bump.outputs[0],grainbump.inputs['Normal']);link(grainbump.outputs[0],bs.inputs['Normal'])
packed=node('ShaderNodeCombineColor');packed.mode='RGB';link(cavity,packed.inputs[0]);link(rough,packed.inputs[1]);link(height,packed.inputs[2])
def bake(name,kind,socket=None,srgb=False,floatbuf=False):
    im=bpy.data.images.new(name,4096,2048,alpha=False,float_buffer=floatbuf);im.use_fake_user=True
    im.colorspace_settings.name='sRGB' if srgb else 'Non-Color'
    target=node('ShaderNodeTexImage');target.image=im;nt.nodes.active=target
    if socket:
        emit=node('ShaderNodeEmission');link(socket,emit.inputs[0]);link(emit.outputs[0],output.inputs[0])
    bpy.ops.object.bake(type=kind)
    link(bs.outputs[0],output.inputs[0])
    if socket:nt.nodes.remove(emit)
    im.filepath_raw=str(TEX/(name+'.png'));im.file_format='PNG';im.save();im.pack();nt.nodes.remove(target)
    print('BAKED',name,flush=True);return im
albedo=bake('dry_albedo','EMIT',color,True)
heightmap=bake('dry_height','EMIT',height,floatbuf=True)
pixels=np.array(heightmap.pixels[:],dtype=np.float32).reshape(2048,4096,4)
uv=planet.data.uv_layers.active.data;vertex_uv={}
for loop in planet.data.loops:vertex_uv.setdefault(loop.vertex_index,tuple(uv[loop.index].uv))
radii=[]
for vert in planet.data.vertices:
    u,v=vertex_uv[vert.index];x=(u%1)*4096-.5;y=max(0,min(2047,v*2048-.5));ix=math.floor(x);iy=math.floor(y);fx=x-ix;fy=y-iy
    h=(pixels[iy,ix%4096,0]*(1-fx)+pixels[iy,(ix+1)%4096,0]*fx)*(1-fy)+(pixels[min(2047,iy+1),ix%4096,0]*(1-fx)+pixels[min(2047,iy+1),(ix+1)%4096,0]*fx)*fy
    # Terrain lies inside the unchanged spherical game collider, avoiding visual fly-throughs.
    radius=max(.995,min(1.0,.9965+float(h)*.004));vert.co=vert.co.normalized()*radius;radii.append(radius)
planet.data.update()
normal=bake('dry_normal','NORMAL')
surface=bake('dry_surface','EMIT',packed.outputs[0])
planet['water']=False;planet['collision_radius']=1.0;planet['radius_min']=min(radii);planet['radius_max']=max(radii)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'geology-source.blend'),compress=True)
# Export compact GLB fallback textures; Unity binds the full 4K maps explicitly.
render_mat=bpy.data.materials.new('Ferron_Dry_PBR');render_mat.use_nodes=True
rn=render_mat.node_tree.nodes;rl=render_mat.node_tree.links;rbs=rn.get('Principled BSDF')
nodes=[]
for im,kind in [(albedo,'color'),(normal,'normal'),(surface,'surface')]:
    t=rn.new('ShaderNodeTexImage');t.image=im;nodes.append(t)
    if kind=='color':rl.new(t.outputs[0],rbs.inputs['Base Color'])
    elif kind=='normal':n=rn.new('ShaderNodeNormalMap');rl.new(t.outputs[0],n.inputs['Color']);rl.new(n.outputs[0],rbs.inputs['Normal'])
    else:s=rn.new('ShaderNodeSeparateColor');rl.new(t.outputs[0],s.inputs[0]);rl.new(s.outputs[1],rbs.inputs['Roughness'])
rbs.inputs['Metallic'].default_value=0
planet.data.materials.clear();planet.data.materials.append(render_mat)
for n in nodes:im=n.image;n.image=im.copy();n.image.scale(1024,512)
bpy.ops.object.select_all(action='DESELECT');planet.select_set(True);bpy.context.view_layer.objects.active=planet
bpy.ops.export_scene.gltf(filepath=str(OUT/'ferron.glb'),export_format='GLB',use_selection=True,export_texcoords=True,export_normals=True,export_tangents=True,export_extras=True,export_cameras=False,export_lights=False)
for n,im in zip(nodes,(albedo,normal,surface)):n.image=im
bm=bmesh.new();bm.from_mesh(planet.data);bad=sum(not e.is_manifold for e in bm.edges);volume=bm.calc_volume(signed=True);bm.free()
assert bad==0 and volume>0
planet.data.calc_loop_triangles()
world=bpy.data.worlds.new('Space');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.003,.003,.004,1);world.node_tree.nodes['Background'].inputs[1].default_value=.02;scene.world=world
sd=bpy.data.lights.new('Sun','SUN');sd.energy=2.65;sd.angle=.0085;sun=bpy.data.objects.new('Sun',sd);scene.collection.objects.link(sun);sun.rotation_euler=Vector((.8,.4,-.35)).to_track_quat('-Z','Y').to_euler()
cd=bpy.data.cameras.new('Orbital camera');cam=bpy.data.objects.new('Orbital camera',cd);scene.collection.objects.link(cam);cam.location=(0,-3.3,.8);cam.rotation_euler=(-cam.location).to_track_quat('-Z','Y').to_euler();cd.lens=48;scene.camera=cam
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(OUT/'blender-preview.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'dry-planet.blend'),compress=True)
bpy.ops.render.render(write_still=True)
report=dict(vertices=len(planet.data.vertices),triangles=len(planet.data.loop_triangles),nonmanifold_edges=bad,volume=volume,radius_min=min(radii),radius_max=max(radii),source_size=list(source.size),textures={im.name:list(im.size) for im in (albedo,normal,surface)},craters=crater_profiles,water=False)
(OUT/'asset-report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
print('COMPLETE',report['triangles'],'triangles',flush=True)
