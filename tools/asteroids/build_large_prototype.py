"""Blender sculpt and matched 4K bakes for ONE asteroid variant, boulder_A.
Run from the Unity repository: blender -b -t 12 --python tools/asteroids/build_large_prototype.py
Stages source, maps and GLB under ArtSource/LargeRockPrototype; never changes live assets.
"""
import bpy, bmesh, math, json, random
from pathlib import Path
from mathutils import Vector, noise

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'ArtSource/LargeRockPrototype'
OUT.mkdir(parents=True, exist_ok=True)
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

rng = random.Random(916302)
off = Vector((8.2, 17.6, 11.8))
planes = []
for i in range(24):
    z = 1-2*(i+.5)/24
    a = i*2.399963
    v = Vector((math.sqrt(1-z*z)*math.cos(a), math.sqrt(1-z*z)*math.sin(a), z))
    planes.append((v, rng.uniform(.77, .96)))
faults = [(Vector((.83,.12,.55)).normalized(), .19),
          (Vector((-.24,.96,.20)).normalized(), -.13),
          (Vector((.13,-.30,.94)).normalized(), .35)]

# Real cliffs, recesses and angular chips. No separate ore lumps or floating detail geometry.
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=9, radius=1)
high = bpy.context.object
high.name = 'Boulder_Sculpt_1310720_faces'
for v in high.data.vertices:
    d = v.co.normalized()
    r = min(h / max(.0001, d.dot(axis)) for axis, h in planes)
    q = d*r
    p = q+off
    broad = 0
    for axis, offset in faults:
        f = q.dot(axis)+offset+.036*n(p, 4.1)
        # A steep recessed shear joint, with a broken ledge on one bank.
        broad -= .073*math.exp(-pow(f/.029, 4))
        broad += .028*math.tanh(f/.014)
    ds, _ = noise.voronoi((q+noise.noise_vector(p*2.4)*.019)*7.4)
    edge = ds[1]-ds[0]
    chip = -.018*math.exp(-pow(edge/.075, 2))
    chip *= max(.12, min(1, (n(p, 3.3)+.23)*3))
    # Chipped laminae: shallow terraces with sharp edges, not a swollen noise rind.
    level = q.dot(Vector((.25,.92,.31)))*12.5 + .22*n(p, 5)
    phase = level-math.floor(level)
    shelf = .010*(phase-.5-math.tanh((phase-.92)*30)*.5)
    relief = .014*n(p, 11)+.008*n(p, 26)+.0055*n(p, 68)+.0022*n(p, 145)
    # A couple of irregular impact scallops reveal larger broken interior faces.
    for axis, width, depth in [(Vector((.6,-.7,.3)).normalized(),.31,.15),
                                (Vector((-.4,.3,.85)).normalized(),.22,.095)]:
        angle = math.acos(max(-1,min(1,d.dot(axis))))
        if angle < width: broad -= depth*pow(1-(angle/width)**2,.7)
    v.co = Vector((d.x*1.13, d.y*.95, d.z))*(r+broad+chip+shelf+relief)
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
vein=mathn('ADD',mathn('ADD',mathn('MULTIPLY',xyz.outputs[0],.75),xyz.outputs[2]),mathn('MULTIPLY',grain(4),.85))
vein=mathn('SINE',mathn('MULTIPLY',vein,7.5))
mask=ramp(mathn('ADD',mathn('ABSOLUTE',vein),mathn('MULTIPLY',grain(24),.14)),.37,.54,1,0)
# Mineral islands interrupt the broad ribbons without raised nodes.
mask=mathn('MULTIPLY',mask,ramp(macro,.25,.36,0,1))
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

low=bpy.data.objects.new('boulder_A_LOD0',high.data.copy());scene.collection.objects.link(low)
mod=low.modifiers.new('Preserve fractures at playable density','DECIMATE');mod.ratio=60000/len(low.data.polygons);apply(low,mod)
# Remove the primitive's default UVs: bake and export must use the same six unique charts.
while low.data.uv_layers: low.data.uv_layers.remove(low.data.uv_layers[0])
uv=low.data.uv_layers.new(name='UniqueSculptUV')
uv.active_render=True
for f in low.data.polygons:
    c=f.center; axis=max(range(3),key=lambda k:abs(c[k])); positive=c[axis]>=0
    chart=axis*2+(0 if positive else 1); axes=[k for k in range(3) if k!=axis]
    for li in f.loop_indices:
        p=low.data.vertices[low.data.loops[li].vertex_index].co
        # Six unique radial charts, padding protects the borders through the mip chain.
        u=.5+.5*p[axes[0]]/max(.001,abs(p[axis]));v=.5+.5*p[axes[1]]/max(.001,abs(p[axis]))
        uv.data[li].uv=((chart%3+.025+u*.95)/3,(chart//3+.025+v*.95)/2)
low.data.update()
targetMat=bpy.data.materials.new('Boulder bake target');targetMat.use_nodes=True
low.data.materials.clear();low.data.materials.append(targetMat)
target=targetMat.node_tree.nodes.new('ShaderNodeTexImage');targetMat.node_tree.nodes.active=target
maps={}
for suffix, socket in [('normal',None),('albedo',mix.outputs[0]),('stone',stoneTexture.outputs[0]),('surface',surface.outputs[0])]:
    im=bpy.data.images.new('boulder_'+suffix,4096,4096,alpha=False)
    im.colorspace_settings.name='sRGB' if suffix in ('albedo','stone') else 'Non-Color'
    target.image=im
    if socket is None:lk.new(bs.outputs[0],output.inputs[0])
    else:lk.new(socket,emit.inputs[0]);lk.new(emit.outputs[0],output.inputs[0])
    select(low);high.select_set(True)
    bpy.ops.object.bake(type='NORMAL' if suffix=='normal' else 'EMIT')
    im.filepath_raw=str(OUT/('boulder_'+suffix+'.png'));im.file_format='PNG';im.save();im.pack();maps[suffix]=im
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
    ob=bpy.data.objects.new('boulder_A_LOD'+str(lod),low.data.copy());scene.collection.objects.link(ob)
    mod=ob.modifiers.new('Distance simplification','DECIMATE');mod.ratio=count/len(ob.data.polygons);mod.delimit={'UV','SEAM'};apply(ob,mod)
    assets.append(ob)
# Export one material slot. The runtime uses a resource material with these exact unique maps.
runtime=bpy.data.materials.new('Boulder_Sculpt_Surface');runtime.diffuse_color=(.18,.16,.14,1)
report=[]
for lod,ob in enumerate(assets):
    ob.data.materials.clear();ob.data.materials.append(runtime)
    ob['lod']=lod;ob['prototype']='boulder_A';ob['source_sculpt_triangles']=len(high.data.polygons)
    ob.data.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(ob.data)
    bad=sum(not e.is_manifold for e in bm.edges);volume=bm.calc_volume(signed=True);bm.free()
    assert bad==0 and volume>0
    report.append(dict(lod=lod,triangles=len(ob.data.loop_triangles),vertices=len(ob.data.vertices),nonmanifold_edges=bad,volume=volume))
select(low)
for ob in assets:ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(OUT/'boulder_prototype.glb'),export_format='GLB',use_selection=True,export_extras=True,export_yup=True,export_tangents=True,export_materials='EXPORT')
(OUT/'mesh-report.json').write_text(json.dumps(dict(generator=bpy.app.version_string,sculpt_triangles=len(high.data.polygons),texture_resolution=4096,meshes=report),indent=2))
# Keep the high sculpt and all runtime LODs editable, with packed bakes.
for ob in assets:ob.hide_render=True;ob.hide_set(True)
high.hide_render=False
select(high)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'large-boulder.blend'),compress=True)
print('DONE',report,flush=True)
