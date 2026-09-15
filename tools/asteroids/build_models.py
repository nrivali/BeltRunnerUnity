"""Blender 5.2: sculpt a deterministic asteroid library, then export three Unity-compatible LODs.
Run from project root: blender --background --python tools/asteroids/build_models.py -- [--only lumpy,boulder]
Outputs are staged in ArtSource/AsteroidsV3/export before integration. Does not overwrite live game assets.
"""
import bpy, bmesh, math, random, json, sys
from pathlib import Path
from mathutils import Vector, noise

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'ArtSource/AsteroidsV3'
(OUT/'export').mkdir(parents=True, exist_ok=True)
(OUT/'renders').mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.view_settings.view_transform = 'AgX'
SHAPES = {
 'lumpy':(1,1,1), 'chunk':(1.1,.9,1), 'potato':(1.45,.85,1), 'shard':(.55,.6,2.1),
 'pancake':(1.35,.42,1.2), 'cratered':(1,1,1), 'cluster':(1,1,1), 'slab':(1.6,.38,1.15),
 'spindle':(.5,.5,2.7), 'bean':(.9,.85,1.9), 'boulder':(1.15,.95,1), 'jagged':(1,1.2,.9),
 'wedge':(1.4,.75,.9), 'hollow':(1,1,1)
}
all_shapes=list(SHAPES)
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
chosen=args[args.index('--only')+1].split(',') if '--only' in args else all_shapes

# Reuse the approved textured PBR surfaces and the Unity repair's new stone maps.
bpy.ops.import_scene.gltf(filepath=str(ROOT/'Assets/Resources/Models/asteroids_lod1.glb'))
stone=bpy.data.materials['Barren_Regolith']
ore=bpy.data.materials['Ore_iron']
stone.use_fake_user=ore.use_fake_user=True
for ob in list(bpy.data.objects): bpy.data.objects.remove(ob,do_unlink=True)
for node in stone.node_tree.nodes:
    if node.type=='TEX_IMAGE' and node.image:
        for suffix in ('albedo','normal','metalrough'):
            if ('regolith_'+suffix) in node.image.name:
                node.image=bpy.data.images.load(str(ROOT/'Assets/Resources/Asteroids'/('regolith_'+suffix+'.png')),check_existing=True)
                node.image.colorspace_settings.name='sRGB' if suffix=='albedo' else 'Non-Color'

def select(ob):
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob

def apply(ob,mod):
    select(ob);bpy.ops.object.modifier_apply(modifier=mod.name)

def n(p, frequency=1):
    return noise.noise(p*frequency,noise_basis='PERLIN_ORIGINAL')

def rand_dir(rng): return Vector(tuple(rng.uniform(-1,1) for _ in range(3))).normalized()

def clean(ob):
    ob.data.validate(clean_customdata=False)
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-7)
    islands=[f for f in bm.faces if all(len(e.link_faces)==1 for e in f.edges)]
    if islands:bmesh.ops.delete(bm,geom=islands,context="FACES")
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context="VERTS")
    boundary=[e for e in bm.edges if e.is_boundary]
    if boundary: bmesh.ops.holes_fill(bm,edges=boundary,sides=8)
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(ob.data);bm.free();ob.data.update()

def sculpt(shape,variant):
    seed=7319+all_shapes.index(shape)*101+variant*1709
    rng=random.Random(seed)
    off=Vector((rng.uniform(13,81),rng.uniform(17,93),rng.uniform(11,79)))
    strata=rand_dir(rng)
    craters=[(rand_dir(rng),rng.uniform(.13,.34),rng.uniform(.045,.14)) for _ in range(13 if shape=='cratered' else 5)]
    # 81,920 evenly distributed faces before sculpting and reduction. Every surface feature is spatially
    # coherent; independent per-vertex jitter is deliberately absent, so the result is not triangular spikes.
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=7,radius=1)
    ob=bpy.context.object
    rough=1.28 if shape=='jagged' else .84 if shape in ('potato','bean','spindle') else 1
    for v in ob.data.vertices:
        d=v.co.normalized(); p=d+off
        macro=.19*n(p,1.8)+.09*n(p,3.8)+.037*n(p,7.4)
        if shape=='cluster': macro+=.16*n(p,3.2)
        base=1+macro
        if shape in ('chunk','slab','boulder'):
            # Rounded, broken blocks rather than an undeformed dodecahedron's broad flat faces.
            base*=pow(abs(d.x)**3.2+abs(d.y)**3.2+abs(d.z)**3.2,-1/3.2)*.87
        if shape=='wedge':base*=.88+.2*d.x
        if shape=='bean':base*=1-.26*(1-d.z*d.z);base+=.10*d.x*d.z
        if shape=='spindle':base*=.76+.24*abs(d.z)
        if shape=='shard':base*=.86+.14*d.z
        for axis,width,depth in craters:
            angle=math.acos(max(-1,min(1,d.dot(axis))))
            if angle<width:base-=depth*pow(1-angle/width,1.4)
            base+=.008*math.exp(-((angle-width)/.028)**2)
        # Broad warped fracture plates, fine chipped edges, and layered ledges are actual displacement.
        warp=p+noise.noise_vector(p*2.7)*.14
        distances,_=noise.voronoi(warp*5.3)
        gap=distances[1]-distances[0]
        cracks=-.065*math.exp(-pow(gap/.072,2))
        fine=noise.noise(warp*14,noise_basis='VORONOI_F2F1')
        cracks-=.018*math.exp(-pow(fine/.09,2))
        layer=d.dot(strata)*8.0+.9*n(p,4.1)
        phase=layer-math.floor(layer)
        ledge=.014*(math.tanh((phase-.47)*9)-2*phase)
        chips=.025*n(p,15)+.012*n(p,31)+.005*n(p,64)
        radius=base+rough*(cracks+ledge+chips)
        v.co=Vector(tuple(d[i]*radius*SHAPES[shape][i] for i in range(3)))
    ob.data.update()
    if shape=='hollow':
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=5,radius=1)
        cut=bpy.context.object
        cut.scale=(.57,1.12,.55) if variant==0 else (.46,1.75,.5)
        cut.location=(0,-.82,.08) if variant==0 else (.03,0,.02)
        for v in cut.data.vertices: v.co*=1+.075*n(v.co+off,7)+.026*n(v.co+off,24)
        select(cut);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        mod=ob.modifiers.new('Excavated cavity','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
        apply(ob,mod);bpy.data.objects.remove(cut,do_unlink=True)
        # Rebuild the boolean intersection as a closed surface before decimation.
        remesh=ob.modifiers.new("Cavity topology", "REMESH");remesh.mode="VOXEL";remesh.voxel_size=.018;remesh.use_smooth_shade=True
        apply(ob,remesh)
    # Input proportions are the game's XYZ; Blender's Z-up maps back through glTF Y-up on export.
    for v in ob.data.vertices:
        x,y,z=v.co;v.co=(x,-z,y)
    clean(ob)
    return ob,seed

def score(p,seed):
    off=Vector((seed*.037,seed*.021,seed*.051))
    warp=.30*n(p+off,2.6)+.11*n(p+off,6.5)
    a=abs(math.sin((p.dot(Vector((.68,.36,.64)))*.92+warp+.38)*math.pi))
    b=abs(math.sin((p.dot(Vector((-.44,.82,.36)))*.68+warp+.79)*math.pi))
    return min(a,b*.92+.19)

def surface(ob,seed,threshold=None):
    if threshold is None:
        entries=sorted((score(f.center,seed),f.area) for f in ob.data.polygons)
        area=sum(a for _,a in entries);walk=0
        for value,a in entries:
            walk+=a
            if walk>=area*.27:threshold=value;break
    positions=[v.co.copy() for v in ob.data.vertices]
    values=[score(p,seed)-threshold for p in positions]
    cuts={};faces=[];ids=[]
    def crossing(a,b):
        key=tuple(sorted((a,b)))
        if key not in cuts:
            k=values[a]/(values[a]-values[b]);cuts[key]=len(positions);positions.append(positions[a].lerp(positions[b],k))
        return cuts[key]
    for face in ob.data.polygons:
        vv=list(face.vertices)
        for part in (0,1):
            poly=[]
            for a,b in zip(vv,vv[1:]+vv[:1]):
                ina=(values[a]<=0) if part else (values[a]>0)
                inb=(values[b]<=0) if part else (values[b]>0)
                if ina:poly.append(a)
                if ina!=inb:poly.append(crossing(a,b))
            for k in range(1,len(poly)-1):faces.append((poly[0],poly[k],poly[k+1]));ids.append(part)
    old=ob.data
    mesh=bpy.data.meshes.new(ob.name+'_surface');mesh.from_pydata(positions,[],faces);mesh.update();ob.data=mesh
    if old.users==0:bpy.data.meshes.remove(old)
    mesh.materials.append(stone);mesh.materials.append(ore)
    uv=mesh.uv_layers.new(name='SurfaceUV')
    for f,material in zip(mesh.polygons,ids):
        f.material_index=material;f.use_smooth=True
        # Fixed charts based on radial direction, not each small displaced face normal. Fine fractures
        # no longer jump between texture projections on neighbouring triangles.
        dominant=max(range(3),key=lambda i:abs(f.center[i]/SHAPES[ob['shape_family']][(0,2,1)[i]]))
        axes=[i for i in range(3) if i!=dominant]
        for li in f.loop_indices:
            p=mesh.vertices[mesh.loops[li].vertex_index].co
            uv.data[li].uv=(p[axes[0]]*.9+(seed%7)*.17,p[axes[1]]*.9+(seed%11)*.13)
    clean(ob)
    # Smooth within fracture plates, split only the genuinely sharp fracture edges.
    bm=bmesh.new();bm.from_mesh(mesh)
    for e in bm.edges:
        if len(e.link_faces)==2:e.smooth=e.calc_face_angle()<math.radians(62)
    bm.to_mesh(mesh);bm.free();mesh.update()
    ob['ore_threshold']=threshold
    ob['ore_surface_fraction']=sum(f.area for f in mesh.polygons if f.material_index==1)/sum(f.area for f in mesh.polygons)
    return threshold

assets=[];report=[]
for shape in chosen:
    for variant in range(2):
        ob,seed=sculpt(shape,variant)
        base=ob.data.copy();threshold=None
        for lod,target in ((0,38000),(1,1400),(2,300)):
            a=ob if lod==0 else bpy.data.objects.new(shape,base.copy())
            if lod:scene.collection.objects.link(a)
            a.name=f'{shape}_{"AB"[variant]}_LOD{lod}'
            a['shape_family']=shape;a['variant']='AB'[variant];a['lod']=lod;a['seed']=seed
            dec=a.modifiers.new('Screen detail budget','DECIMATE');dec.ratio=min(1,target/len(a.data.polygons))
            apply(a,dec);clean(a)
            current=surface(a,seed,threshold)
            if threshold is None:threshold=current
            a.data.name=a.name+'_mesh'
            a.data.calc_loop_triangles()
            bm=bmesh.new();bm.from_mesh(a.data)
            bad=sum(not e.is_manifold for e in bm.edges)
            volume=bm.calc_volume(signed=True);bm.free()
            record=dict(name=a.name,lod=lod,vertices=len(a.data.vertices),triangles=len(a.data.loop_triangles),nonmanifold_edges=bad,volume=volume,ore_surface_fraction=a['ore_surface_fraction'],dimensions=list(a.dimensions))
            report.append(record);assets.append(a)
            assert bad==0 and volume>0,record
        if base.users==0:bpy.data.meshes.remove(base)
        print('BUILT',shape,'AB'[variant],[x['triangles'] for x in report[-3:]],flush=True)

for lod in range(3):
    bpy.ops.object.select_all(action='DESELECT')
    group=[a for a in assets if a['lod']==lod]
    for a in group:a.select_set(True)
    bpy.context.view_layer.objects.active=group[0]
    bpy.ops.export_scene.gltf(filepath=str(OUT/'export'/f'asteroids_lod{lod}.glb'),export_format='GLB',use_selection=True,export_extras=True,export_yup=True,export_tangents=True,export_materials='EXPORT',export_image_format='AUTO',export_cameras=False,export_lights=False)
    print('EXPORTED',lod,flush=True)
(OUT/'mesh-report.json').write_text(json.dumps({'generator':bpy.app.version_string,'models':report},indent=2))

# Lay out the actual meshes for an editable Blender source file, then render a close-up under a simple key.
for i,a in enumerate(assets):
    a.hide_render=True
    a.hide_set(a['lod']!=0)
    a.location=((i//3%7)*4,0,(i//21)*4)
for im in bpy.data.images:
    if im.source=='FILE':im.pack()
hero=next((a for a in assets if a.name.startswith(('boulder_A_LOD0','lumpy_A_LOD0'))),assets[0])
hero.location=(0,0,0);hero.hide_render=False
hero.rotation_euler=(.15,.1,.3)
world=bpy.data.worlds.new('Space');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.08,.11,.16,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.13
key_data=bpy.data.lights.new('Sun','SUN');key_data.energy=3.2;key_data.angle=.035
key=bpy.data.objects.new('Sun',key_data);scene.collection.objects.link(key);key.rotation_euler=(.45,-.5,-.65)
fill_data=bpy.data.lights.new('Reflected fill','AREA');fill_data.energy=60;fill_data.shape='DISK';fill_data.size=4
fill=bpy.data.objects.new('Reflected fill',fill_data);scene.collection.objects.link(fill);fill.location=(2,-3,2)
fill.rotation_euler=(Vector((0,0,0))-fill.location).to_track_quat('-Z','Y').to_euler()
cam_data=bpy.data.cameras.new('Camera');cam=bpy.data.objects.new('Camera',cam_data);scene.collection.objects.link(cam)
cam.location=(3,-4,2.3);cam.rotation_euler=(-cam.location).to_track_quat('-Z','Y').to_euler();cam_data.lens=56;scene.camera=cam
scene.render.resolution_x=1200;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.filepath=str(OUT/'renders/geometry.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'asteroids.blend'),compress=True)
print('DONE',len(assets),'meshes',flush=True)
