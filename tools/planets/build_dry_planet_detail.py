"""Blender build: orbital-scale geology with matched relief. Stages assets for reviewed installation."""
from pathlib import Path
import bpy, bmesh, math, json
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'ArtSource/DryPlanetDetail'
TEX=OUT/'textures'; TEX.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene; scene.render.engine='CYCLES'
scene.cycles.samples=1; scene.render.bake.margin=12
scene.view_settings.view_transform='AgX'
bpy.ops.mesh.primitive_uv_sphere_add(segments=512,ring_count=256,radius=1)
planet=bpy.context.object;planet.name='Ferron_Orbital_Geology'
for p in planet.data.polygons:p.use_smooth=True
mat=bpy.data.materials.new('Orbital geology source');mat.use_nodes=True;mat.use_fake_user=True
planet.data.materials.append(mat);nt=mat.node_tree;nt.nodes.clear()
def node(kind):return nt.nodes.new(kind)
def link(a,b):nt.links.new(a,b)
def mathn(op,a,b=None):
    n=node('ShaderNodeMath');n.operation=op
    for i,s in enumerate((a,b)):
        if s is None:continue
        if isinstance(s,(int,float)):n.inputs[i].default_value=s
        else:link(s,n.inputs[i])
    return n.outputs[0]
def remap(v,a,b,c=0,d=1):
    n=node('ShaderNodeMapRange');n.clamp=True;n.interpolation_type='SMOOTHSTEP'
    link(v,n.inputs['Value']);n.inputs['From Min'].default_value=a;n.inputs['From Max'].default_value=b
    n.inputs['To Min'].default_value=c;n.inputs['To Max'].default_value=d;return n.outputs[0]
def mix(a,b,f):
    n=node('ShaderNodeMixRGB');link(f,n.inputs[0]) if not isinstance(f,(int,float)) else None
    if isinstance(f,(int,float)):n.inputs[0].default_value=f
    for i,s in enumerate((a,b),1):
        if isinstance(s,tuple):n.inputs[i].default_value=s
        else:link(s,n.inputs[i])
    return n.outputs[0]
def noise(coord,scale,detail):
    n=node('ShaderNodeTexNoise');link(coord,n.inputs['Vector']);n.inputs['Scale'].default_value=scale
    n.inputs['Detail'].default_value=detail;n.inputs['Roughness'].default_value=.72;return n.outputs['Fac']
def scale(color,value):
    n=node('ShaderNodeVectorMath');n.operation='SCALE';link(color,n.inputs[0]);link(value,n.inputs['Scale']);return n.outputs[0]

source=bpy.data.images.load(str(OUT/'orbital-geology.png'));source.pack()
tc=node('ShaderNodeTexCoord');separate=node('ShaderNodeSeparateXYZ');link(tc.outputs['UV'],separate.inputs[0])
u,v=separate.outputs[0],separate.outputs[1]
mirror=node('ShaderNodeCombineXYZ');link(mathn('SUBTRACT',1,u),mirror.inputs[0]);link(v,mirror.inputs[1])
ta=node('ShaderNodeTexImage');ta.image=source;ta.interpolation='Linear';link(tc.outputs['UV'],ta.inputs[0])
tb=node('ShaderNodeTexImage');tb.image=source;tb.interpolation='Linear';link(mirror.outputs[0],tb.inputs[0])
edge=mathn('MINIMUM',u,mathn('SUBTRACT',1,u))
seam=remap(edge,0,.012,.5,0)
pigment=mix(ta.outputs[0],tb.outputs[0],seam)
latitude=mathn('MINIMUM',v,mathn('SUBTRACT',1,v))
polar=remap(latitude,.035,.14,1,0)
# Planar geology on the polar caps avoids equirectangular pinching.
capscale=node('ShaderNodeVectorMath');capscale.operation='MULTIPLY';link(tc.outputs['Object'],capscale.inputs[0]);capscale.inputs[1].default_value=(.55,.55,0)
capuv=node('ShaderNodeVectorMath');capuv.operation='ADD';link(capscale.outputs[0],capuv.inputs[0]);capuv.inputs[1].default_value=(.53,.5,0)
cap=node('ShaderNodeTexImage');cap.image=source;link(capuv.outputs[0],cap.inputs[0])
pigment=mix(pigment,cap.outputs[0],polar)
gray=node('ShaderNodeRGBToBW');link(pigment,gray.inputs[0])
direction=node('ShaderNodeVectorMath');direction.operation='NORMALIZE';link(tc.outputs['Object'],direction.inputs[0])
grain=noise(direction.outputs[0],350,3)
meso=noise(direction.outputs[0],90,4)
color=scale(pigment,remap(grain,0,1,.92,1.08))
height=mathn('ADD',mathn('MULTIPLY',gray.outputs[0],.85),mathn('MULTIPLY',meso,.007))
height=mathn('ADD',height,mathn('MULTIPLY',grain,.0015))
rough=remap(gray.outputs[0],.02,.5,.94,.83)
cavity=remap(gray.outputs[0],.015,.12,.72,1)
bs=node('ShaderNodeBsdfPrincipled');output=node('ShaderNodeOutputMaterial');link(bs.outputs[0],output.inputs[0])
link(color,bs.inputs['Base Color']);link(rough,bs.inputs['Roughness']);bs.inputs['Metallic'].default_value=0
bump=node('ShaderNodeBump');bump.inputs['Distance'].default_value=.0012;bump.inputs['Strength'].default_value=.75
link(height,bump.inputs['Height']);link(bump.outputs[0],bs.inputs['Normal'])
packed=node('ShaderNodeCombineColor');packed.mode='RGB'
for i,s in enumerate((cavity,rough,height)):link(s,packed.inputs[i])
def bake(name,kind,socket=None,srgb=False,floatbuf=False):
    im=bpy.data.images.new(name,4096,2048,alpha=False,float_buffer=floatbuf)
    im.colorspace_settings.name='sRGB' if srgb else 'Non-Color'
    target=node('ShaderNodeTexImage');target.image=im;nt.nodes.active=target
    if socket is not None:
        emit=node('ShaderNodeEmission');link(socket,emit.inputs[0]);link(emit.outputs[0],output.inputs[0])
    bpy.ops.object.bake(type=kind)
    link(bs.outputs[0],output.inputs[0])
    if socket is not None:nt.nodes.remove(emit)
    im.filepath_raw=str(TEX/(name+'.png'));im.file_format='PNG';im.save();nt.nodes.remove(target)
    print('BAKED',name,flush=True);return im
albedo=bake('dry_albedo','EMIT',color,True)
heightmap=bake('dry_height','EMIT',height,floatbuf=True)
pixels=np.empty(4096*2048*4,dtype=np.float32);heightmap.pixels.foreach_get(pixels);pixels=pixels.reshape(2048,4096,4)
uv=planet.data.uv_layers.active.data;vuv={}
for loop in planet.data.loops:vuv.setdefault(loop.vertex_index,tuple(uv[loop.index].uv))
radii=[]
for vert in planet.data.vertices:
    u,v=vuv[vert.index];x=(u%1)*4096-.5;y=max(0,min(2047,v*2048-.5));ix=math.floor(x);iy=math.floor(y);fx=x-ix;fy=y-iy
    h=(pixels[iy,ix%4096,0]*(1-fx)+pixels[iy,(ix+1)%4096,0]*fx)*(1-fy)+(pixels[min(2047,iy+1),ix%4096,0]*(1-fx)+pixels[min(2047,iy+1),(ix+1)%4096,0]*fx)*fy
    radius=max(.995,min(.9998,.996+float(h)*.005));vert.co=vert.co.normalized()*radius;radii.append(radius)
planet.data.update()
normal=bake('dry_normal','NORMAL');surface=bake('dry_surface','EMIT',packed.outputs[0])
render_mat=bpy.data.materials.new('Ferron_Orbital_PBR');render_mat.use_nodes=True
rn=render_mat.node_tree.nodes;rl=render_mat.node_tree.links;rbs=rn.get('Principled BSDF')
nodes=[]
for im,kind in ((albedo,'color'),(normal,'normal'),(surface,'surface')):
    t=rn.new('ShaderNodeTexImage');t.image=im;nodes.append(t)
    if kind=='color':rl.new(t.outputs[0],rbs.inputs['Base Color'])
    elif kind=='normal':n=rn.new('ShaderNodeNormalMap');rl.new(t.outputs[0],n.inputs['Color']);rl.new(n.outputs[0],rbs.inputs['Normal'])
    else:n=rn.new('ShaderNodeSeparateColor');rl.new(t.outputs[0],n.inputs[0]);rl.new(n.outputs[1],rbs.inputs['Roughness'])
rbs.inputs['Metallic'].default_value=0
planet.data.materials.clear();planet.data.materials.append(render_mat)
planet['water']=False;planet['collision_radius']=1.0;planet['detail_source']='Imagegen orbital geology + Blender matched relief'
bpy.ops.object.select_all(action='DESELECT');planet.select_set(True);bpy.context.view_layer.objects.active=planet
for t in nodes:t.image=t.image.copy();t.image.scale(1024,512)
bpy.ops.export_scene.gltf(filepath=str(OUT/'ferron.glb'),export_format='GLB',use_selection=True,export_texcoords=True,export_normals=True,export_tangents=True,export_extras=True,export_cameras=False,export_lights=False)
for t,im in zip(nodes,(albedo,normal,surface)):
    t.image=im
    # Refer to the installed versioned maps; keep the editable source compact.
    # File references are reassigned after the staging preview is rendered.
world=bpy.data.worlds.new('Space');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.003,.003,.004,1);world.node_tree.nodes['Background'].inputs[1].default_value=.02;scene.world=world
sd=bpy.data.lights.new('Sun','SUN');sd.energy=2.65;sd.angle=.0085;sun=bpy.data.objects.new('Sun',sd);scene.collection.objects.link(sun);sun.rotation_euler=Vector((.8,.4,-.35)).to_track_quat('-Z','Y').to_euler()
cd=bpy.data.cameras.new('Orbital camera');cam=bpy.data.objects.new('Orbital camera',cd);scene.collection.objects.link(cam);cam.location=(0,-3.3,.8);cam.rotation_euler=(-cam.location).to_track_quat('-Z','Y').to_euler();cd.lens=48;scene.camera=cam
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(OUT/'blender-preview.png')
bpy.ops.render.render(write_still=True)
for im in (albedo,normal,surface):im.filepath='//../../Assets/Resources/Planets/Dry/'+Path(im.filepath).name
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'dry-planet-detail.blend'),compress=True,relative_remap=True)
bm=bmesh.new();bm.from_mesh(planet.data);bad=sum(not e.is_manifold for e in bm.edges);volume=bm.calc_volume(signed=True);bm.free()
assert bad==0 and volume>0
planet.data.calc_loop_triangles()
report=dict(vertices=len(planet.data.vertices),triangles=len(planet.data.loop_triangles),nonmanifold_edges=bad,radius_min=min(radii),radius_max=max(radii),source_size=list(source.size),map_size=[4096,2048],water=False)
(OUT/'asset-report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
print('COMPLETE',report,flush=True)
