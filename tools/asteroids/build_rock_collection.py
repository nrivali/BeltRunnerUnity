"""Expand the approved large-rock sculpt with five Blender-authored silhouettes/patterns.
Run: blender -b -t 12 --python tools/asteroids/build_rock_collection.py -- boulder_B
Valid keys are DESIGNS below. Each creates an editable .blend with packed source regolith and linked bakes, three runtime LODs,
and 4K stone/normal/surface maps. Neutral metal reflectance is reconstructed from packed
roughness by SculptRock.shader, saving a fourth texture per design. No game state touched.
"""
import bpy, bmesh, math, json, random, sys
from pathlib import Path
from mathutils import Vector, noise

ROOT = Path(__file__).resolve().parents[2]
# Each design has its own deterministic sculpt, ore topology, and matched LOD atlas.
DESIGNS = {
    'boulder_B': dict(seed=73119, scale=(1.18,.93,1.06), planes=19, fractures=4, chip=7.8, pattern='network'),
    'cratered_A': dict(seed=44278, scale=(1.08,.94,1.04), planes=32, fractures=2, chip=8.4, pattern='pockets'),
    'potato_A': dict(seed=86234, scale=(1.46,.84,1.03), planes=27, fractures=3, chip=6.6, pattern='ribbons'),
    'slab_A': dict(seed=19276, scale=(1.53,1.12,.40), planes=18, fractures=3, chip=9.5, pattern='strata'),
    'shard_A': dict(seed=57203, scale=(.60,1.84,.62), planes=15, fractures=3, chip=7.1, pattern='plates'),
}
key = sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'boulder_B'
cfg = DESIGNS[key]
OUT = ROOT / 'ArtSource/RockCollection'
MAPS = ROOT / 'Assets/Resources/Asteroids/SculptCollection' / key
MODELS = ROOT / 'Assets/Resources/Models'
for folder in (OUT, MAPS, MODELS): folder.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 16
scene.render.bake.margin = 20
scene.render.bake.use_selected_to_active = True
scene.render.bake.cage_extrusion = .10
scene.render.bake.max_ray_distance = .24
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.get_devices()
    gpu = False
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
        gpu |= device.use
    if gpu: scene.cycles.device = 'GPU'
    print('BAKE DEVICE', [(d.name, d.use) for d in prefs.devices], flush=True)
except Exception as e:
    print('CPU bake', e, flush=True)

def select(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def apply(ob, mod):
    select(ob)
    bpy.ops.object.modifier_apply(modifier=mod.name)

def n(p, f): return noise.noise(p*f, noise_basis='PERLIN_ORIGINAL')

rng = random.Random(cfg['seed'])
off = Vector(tuple(rng.uniform(3,30) for _ in range(3)))
scale = Vector(cfg['scale'])
planes = []
for i in range(cfg['planes']):
    z = 1-2*(i+.5)/cfg['planes']; a = i*2.399963 + rng.uniform(-.08,.08)
    axis = Vector((math.sqrt(1-z*z)*math.cos(a), math.sqrt(1-z*z)*math.sin(a), z))
    planes.append((axis,rng.uniform(.78,.98)))
faults = [(Vector(tuple(rng.uniform(-1,1) for _ in range(3))).normalized(),rng.uniform(-.45,.45)) for _ in range(cfg['fractures'])]
craters = [(Vector(tuple(rng.uniform(-1,1) for _ in range(3))).normalized(),rng.uniform(.16,.36),rng.uniform(.08,.16)) for _ in range(9 if key=='cratered_A' else 2)]

bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=9, radius=1)
high=bpy.context.object;high.name=key+'_Sculpt_1310720_faces'
for v in high.data.vertices:
    d=v.co.normalized()
    r=min(h/max(.0001,d.dot(axis)) for axis,h in planes)
    q=d*r;p=q+off
    broad=0
    for axis,offset in faults:
        f=q.dot(axis)+offset+.036*n(p,4.1)
        broad-=.073*math.exp(-pow(f/.029,4))
        broad+=.028*math.tanh(f/.014)
    ds,_=noise.voronoi((q+noise.noise_vector(p*2.4)*.019)*cfg['chip'])
    chip=-.020*math.exp(-pow((ds[1]-ds[0])/.075,2))
    chip*=max(.12,min(1,(n(p,3.3)+.23)*3))
    layerAxis=Vector((.13,.09,1)) if key=='slab_A' else Vector((.25,.92,.31))
    level=q.dot(layerAxis)*12.5+.22*n(p,5)
    phase=level-math.floor(level)
    shelf=(.016 if key=='slab_A' else .010)*(phase-.5-math.tanh((phase-.92)*30)*.5)
    relief=.014*n(p,11)+.008*n(p,26)+.0055*n(p,68)+.0022*n(p,145)
    for axis,width,depth in craters:
        angle=math.acos(max(-1,min(1,d.dot(axis))))
        if angle<width:
            t=angle/width
            broad-=depth*pow(1-t*t,.7)
            broad+=.018*math.exp(-pow((t-.85)/.13,2))
    if key=='potato_A': broad+=.045*n(p,2.4)
    if key=='shard_A': broad-=.05*(d.y+.25)**2
    v.co=Vector((d.x*scale.x,d.y*scale.y,d.z*scale.z))*(r+broad+chip+shelf+relief)
for f in high.data.polygons: f.use_smooth=True
high.data.update()
print('SCULPT', len(high.data.polygons), flush=True)

# Source surface is continuous object-space geology; the resulting maps use unique UV charts.
mat = bpy.data.materials.new('Sculpted basalt with embedded neutral metal')
mat.use_nodes=True
high.data.materials.append(mat)
nt=mat.node_tree; nd=nt.nodes; lk=nt.links
bs=nd.get('Principled BSDF'); output=nd.get('Material Output')
texcoord=nd.new('ShaderNodeTexCoord')
coord=texcoord.outputs['Object']
def mathn(op,a,b=None):
    t=nd.new('ShaderNodeMath'); t.operation=op
    for i,x in enumerate((a,b)):
        if x is None: continue
        if isinstance(x,(int,float)): t.inputs[i].default_value=x
        else: lk.new(x,t.inputs[i])
    return t.outputs[0]
def grain(scale,detail=3):
    t=nd.new('ShaderNodeTexNoise');lk.new(coord,t.inputs['Vector'])
    t.inputs['Scale'].default_value=scale;t.inputs['Detail'].default_value=detail
    t.inputs['Roughness'].default_value=.72
    return t.outputs['Fac']
def ramp(value,lo,hi,a,b):
    t=nd.new('ShaderNodeMapRange');t.clamp=True;t.interpolation_type='SMOOTHERSTEP'
    lk.new(value,t.inputs['Value'])
    for name,x in [('From Min',lo),('From Max',hi),('To Min',a),('To Max',b)]:t.inputs[name].default_value=x
    return t.outputs[0]
macro=grain(4); medium=grain(37); fine=grain(330,2)
xyz=nd.new('ShaderNodeSeparateXYZ');lk.new(coord,xyz.inputs[0])
# Five geologically distinct embedded patterns, continuous across UV chart borders.
warp=nd.new('ShaderNodeVectorMath');warp.operation='ADD'
warpNoise=nd.new('ShaderNodeTexNoise');lk.new(coord,warpNoise.inputs['Vector']);warpNoise.inputs['Scale'].default_value=3.6
warpScale=nd.new('ShaderNodeVectorMath');warpScale.operation='SCALE';warpScale.inputs[3].default_value=.20
lk.new(warpNoise.outputs['Color'],warpScale.inputs[0]);lk.new(coord,warp.inputs[0]);lk.new(warpScale.outputs[0],warp.inputs[1])
cells=nd.new('ShaderNodeTexVoronoi');cells.feature='DISTANCE_TO_EDGE';cells.inputs['Scale'].default_value=3.4
lk.new(warp.outputs[0],cells.inputs['Vector'])
if cfg['pattern']=='network':
    mask=ramp(cells.outputs['Distance'],.035,.080,1,0)
elif cfg['pattern']=='pockets':
    mask=ramp(grain(6.2),.54,.63,0,1)
elif cfg['pattern']=='ribbons':
    vein=mathn('ADD',mathn('ADD',mathn('MULTIPLY',xyz.outputs[0],.47),xyz.outputs[2]),mathn('MULTIPLY',grain(3.1),1.1))
    vein=mathn('ABSOLUTE',mathn('SINE',mathn('MULTIPLY',vein,6.2)))
    mask=ramp(vein,.30,.49,1,0)
elif cfg['pattern']=='strata':
    vein=mathn('ADD',xyz.outputs[2],mathn('ADD',mathn('MULTIPLY',xyz.outputs[0],.075),mathn('MULTIPLY',grain(4.2),.16)))
    vein=mathn('ABSOLUTE',mathn('SINE',mathn('MULTIPLY',vein,31)))
    mask=ramp(vein,.25,.47,1,0)
else:
    mask=mathn('MULTIPLY',ramp(grain(4.8),.47,.56,0,1),ramp(cells.outputs['Distance'],.019,.052,0,1))
mask=mathn('MULTIPLY',mask,ramp(macro,.26,.38,0,1))
stoneValue=mathn('ADD',.075,mathn('ADD',mathn('MULTIPLY',macro,.19),mathn('MULTIPLY',medium,.095)))
stone=nd.new('ShaderNodeCombineColor');stone.mode='RGB'
for i,factor in enumerate((1,.93,.84)):lk.new(mathn('MULTIPLY',stoneValue,factor),stone.inputs[i])
photo=nd.new('ShaderNodeTexImage')
photo.image=bpy.data.images.load(str(ROOT/'ArtSource/AsteroidsV4/textures/regolith_albedo.png'))
photo.image.pack();photo.projection='BOX';photo.projection_blend=.22
mapping=nd.new('ShaderNodeVectorMath');mapping.operation='SCALE';mapping.inputs[3].default_value=1.8
lk.new(coord,mapping.inputs[0]);lk.new(mapping.outputs[0],photo.inputs['Vector'])
stoneTexture=nd.new('ShaderNodeMixRGB');stoneTexture.blend_type='MULTIPLY';stoneTexture.inputs[0].default_value=1
lk.new(photo.outputs[0],stoneTexture.inputs[1]);lk.new(mathn('ADD',.65,mathn('MULTIPLY',macro,.45)),stoneTexture.inputs[2])
metalValue=mathn('ADD',.40,mathn('MULTIPLY',medium,.39))
mix=nd.new('ShaderNodeMixRGB');mix.blend_type='MIX'
lk.new(mask,mix.inputs[0]);lk.new(stoneTexture.outputs[0],mix.inputs[1]);lk.new(metalValue,mix.inputs[2])
lk.new(mix.outputs[0],bs.inputs['Base Color']);lk.new(mask,bs.inputs['Metallic'])
rough=mathn('ADD',.63,mathn('MULTIPLY',medium,.29))
lk.new(rough,bs.inputs['Roughness'])
bump=nd.new('ShaderNodeBump');bump.inputs['Distance'].default_value=.005;bump.inputs['Strength'].default_value=.55
photoHeight=nd.new('ShaderNodeRGBToBW');lk.new(photo.outputs[0],photoHeight.inputs[0])
lk.new(mathn('ADD',photoHeight.outputs[0],mathn('MULTIPLY',fine,.09)),bump.inputs['Height']);lk.new(bump.outputs[0],bs.inputs['Normal'])
ao=nd.new('ShaderNodeAmbientOcclusion');ao.samples=16;ao.inputs['Distance'].default_value=.055
surface=nd.new('ShaderNodeCombineColor');surface.mode='RGB'
lk.new(ao.outputs['AO'],surface.inputs[0]);lk.new(rough,surface.inputs[1]);lk.new(mask,surface.inputs[2])
emit=nd.new('ShaderNodeEmission')

low=bpy.data.objects.new(key+'_LOD0',high.data.copy());scene.collection.objects.link(low)
mod=low.modifiers.new('Preserve fractures at playable density','DECIMATE');mod.ratio=60000/len(low.data.polygons);apply(low,mod)
# Remove the primitive's default UVs: bake and export must use the same six unique charts.
while low.data.uv_layers: low.data.uv_layers.remove(low.data.uv_layers[0])
uv=low.data.uv_layers.new(name='UniqueSculptUV')
uv.active_render=True
# Fit each entire chart, including triangles which cross a cube-face boundary.
# Per-vertex clamping would fold/overlap the UVs on the deepest fractures.
charts=[[] for _ in range(6)]
for f in low.data.polygons:
    c=Vector(tuple(f.center[k]/scale[k] for k in range(3)))
    axis=max(range(3),key=lambda k:abs(c[k]));positive=c[axis]>=0
    chart=axis*2+(0 if positive else 1);axes=[k for k in range(3) if k!=axis]
    for li in f.loop_indices:
        raw=low.data.vertices[low.data.loops[li].vertex_index].co
        p=Vector(tuple(raw[k]/scale[k] for k in range(3)))
        u=.5+.5*p[axes[0]]/max(.001,abs(p[axis]))
        v=.5+.5*p[axes[1]]/max(.001,abs(p[axis]))
        charts[chart].append((li,u,v))
for chart,loops in enumerate(charts):
    umin=min(p[1] for p in loops);umax=max(p[1] for p in loops)
    vmin=min(p[2] for p in loops);vmax=max(p[2] for p in loops)
    for li,u,v in loops:
        u=(u-umin)/(umax-umin);v=(v-vmin)/(vmax-vmin)
        uv.data[li].uv=((chart%3+.025+u*.95)/3,(chart//3+.025+v*.95)/2)
low.data.update()
targetMat=bpy.data.materials.new('Boulder bake target');targetMat.use_nodes=True
low.data.materials.clear();low.data.materials.append(targetMat)
target=targetMat.node_tree.nodes.new('ShaderNodeTexImage');targetMat.node_tree.nodes.active=target
maps={}
for suffix, socket in [('normal',None),('stone',stoneTexture.outputs[0]),('surface',surface.outputs[0])]:
    im=bpy.data.images.new(key+'_'+suffix,4096,4096,alpha=False)
    im.colorspace_settings.name='sRGB' if suffix in ('albedo','stone') else 'Non-Color'
    target.image=im
    if socket is None:lk.new(bs.outputs[0],output.inputs[0])
    else:lk.new(socket,emit.inputs[0]);lk.new(emit.outputs[0],output.inputs[0])
    select(low);high.select_set(True)
    bpy.ops.object.bake(type='NORMAL' if suffix=='normal' else 'EMIT')
    im.filepath_raw=str(MAPS/(key+'_'+suffix+'.png'));im.file_format='PNG';im.save();im.use_fake_user=True;maps[suffix]=im
    print('BAKED',suffix,flush=True)
lk.new(bs.outputs[0],output.inputs[0])
target.image=None

# LODs inherit the same charts; dissolve respects seams so material features do not slide at transitions.
bm=bmesh.new();bm.from_mesh(low.data);uvlayer=bm.loops.layers.uv.active
for edge in bm.edges:
    if len(edge.link_loops)==2:
        a,b=edge.link_loops
        edge.seam=(a[uvlayer].uv-b.link_loop_next[uvlayer].uv).length>.001 or (a.link_loop_next[uvlayer].uv-b[uvlayer].uv).length>.001
bm.to_mesh(low.data);bm.free()
assets=[low]
for lod,count in [(1,3000),(2,720)]:
    ob=bpy.data.objects.new(key+'_LOD'+str(lod),low.data.copy());scene.collection.objects.link(ob)
    mod=ob.modifiers.new('Distance simplification','DECIMATE');mod.ratio=count/len(ob.data.polygons);mod.delimit={'UV','SEAM'};apply(ob,mod)
    assets.append(ob)
# Export one material slot. The runtime uses a resource material with these exact unique maps.
runtime=bpy.data.materials.new('Boulder_Sculpt_Surface');runtime.diffuse_color=(.18,.16,.14,1)
report=[]
for lod,ob in enumerate(assets):
    ob.data.materials.clear();ob.data.materials.append(runtime)
    ob['lod']=lod;ob['design']=key;ob['source_sculpt_triangles']=len(high.data.polygons)
    ob.data.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(ob.data)
    bad=sum(not e.is_manifold for e in bm.edges);volume=bm.calc_volume(signed=True);bm.free()
    assert bad==0 and volume>0
    report.append(dict(lod=lod,triangles=len(ob.data.loop_triangles),vertices=len(ob.data.vertices),nonmanifold_edges=bad,volume=volume))
select(low)
for ob in assets:ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(MODELS/('sculpt_'+key+'.glb')),export_format='GLB',use_selection=True,export_extras=True,export_yup=True,export_tangents=True,export_materials='EXPORT')
(OUT/(key+'-mesh.json')).write_text(json.dumps(dict(design=key,configuration=cfg,generator=bpy.app.version_string,sculpt_triangles=len(high.data.polygons),texture_resolution=4096,meshes=report),indent=2))
# Keep the high sculpt and LODs editable, with bakes linked relative to this repository.
for ob in assets:ob.hide_render=True;ob.hide_set(True)
high.hide_render=False
select(high)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(key+'.blend')),compress=True)
for im in maps.values(): im.filepath=bpy.path.relpath(im.filepath)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(key+'.blend')),compress=True)
print('DONE',report,flush=True)
