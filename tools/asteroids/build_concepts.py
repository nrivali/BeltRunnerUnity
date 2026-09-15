"""Blender: build all three approved asteroid concepts, preserving Unity's 28 keys and 3 LODs.
Run: blender --background --python tools/asteroids/build_concepts.py [-- --only boulder,slab,cratered]
Outputs are staged in ArtSource/AsteroidsV4/export. Never writes live Unity resources.
"""
import bpy,bmesh,math,random,json,sys
from pathlib import Path
from mathutils import Vector,noise
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'ArtSource/AsteroidsV4'
TEX=OUT/'textures';(OUT/'export').mkdir(parents=True,exist_ok=True);(OUT/'renders').mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.view_settings.view_transform='AgX'
SHAPES={'lumpy':(1,1,1),'chunk':(1.1,.9,1),'potato':(1.45,.85,1),'shard':(.55,.6,2.1),
 'pancake':(1.35,.42,1.2),'cratered':(1,1,1),'cluster':(1,1,1),'slab':(1.6,.38,1.15),
 'spindle':(.5,.5,2.7),'bean':(.9,.85,1.9),'boulder':(1.15,.95,1),'jagged':(1,1.2,.9),
 'wedge':(1.4,.75,.9),'hollow':(1,1,1)}
STYLES={s:('layered' if s in ('shard','pancake','slab','spindle') else 'weathered' if s in ('potato','cratered','cluster','bean','hollow') else 'fractured') for s in SHAPES}
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
chosen=args[args.index('--only')+1].split(',') if '--only' in args else list(SHAPES)

def material(name,kind):
    m=bpy.data.materials.new(name);m.use_nodes=True;nt=m.node_tree;bs=nt.nodes.get('Principled BSDF')
    albedo=nt.nodes.new('ShaderNodeTexImage');albedo.image=bpy.data.images.load(str(TEX/(kind+'_albedo.png')))
    albedo.image.colorspace_settings.name='sRGB';nt.links.new(albedo.outputs['Color'],bs.inputs['Base Color'])
    normal=nt.nodes.new('ShaderNodeTexImage');normal.image=bpy.data.images.load(str(TEX/(kind+'_normal.png')))
    normal.image.colorspace_settings.name='Non-Color';nm=nt.nodes.new('ShaderNodeNormalMap')
    nt.links.new(normal.outputs['Color'],nm.inputs['Color']);nt.links.new(nm.outputs[0],bs.inputs['Normal'])
    packed=nt.nodes.new('ShaderNodeTexImage');packed.image=bpy.data.images.load(str(TEX/(kind+'_metalrough.png')))
    packed.image.colorspace_settings.name='Non-Color';sep=nt.nodes.new('ShaderNodeSeparateColor');sep.mode='RGB'
    nt.links.new(packed.outputs['Color'],sep.inputs[0])
    if kind=='regolith':nt.links.new(sep.outputs[1],bs.inputs['Roughness']);bs.inputs['Metallic'].default_value=0
    else:
        rough=nt.nodes.new('ShaderNodeMath');rough.operation='MULTIPLY';rough.inputs[1].default_value=.30
        nt.links.new(sep.outputs[1],rough.inputs[0]);nt.links.new(rough.outputs[0],bs.inputs['Roughness'])
        nt.links.new(sep.outputs[2],bs.inputs['Metallic'])
    bs.inputs['Emission Strength'].default_value=0
    return m
stone=material('Barren_Regolith','regolith');ore=material('Ore_iron','ore')

def select(ob):
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
def apply(ob,mod):select(ob);bpy.ops.object.modifier_apply(modifier=mod.name)
def n(p,f=1):return noise.noise(p*f,noise_basis='PERLIN_ORIGINAL')
def direction(rng):return Vector(tuple(rng.uniform(-1,1) for _ in range(3))).normalized()
def clean(ob):
    ob.data.validate(clean_customdata=False)
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-7)
    islands=[f for f in bm.faces if all(len(e.link_faces)==1 for e in f.edges)]
    if islands:bmesh.ops.delete(bm,geom=islands,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    edges=[e for e in bm.edges if e.is_boundary]
    if edges:bmesh.ops.holes_fill(bm,edges=edges,sides=8)
    bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    if bm.calc_volume(signed=True)<0:bmesh.ops.reverse_faces(bm,faces=list(bm.faces))
    bm.to_mesh(ob.data);bm.free();ob.data.update()

def context(shape,variant):
    seed=24617+list(SHAPES).index(shape)*173+variant*2029;rng=random.Random(seed)
    off=Vector((rng.uniform(7,23),rng.uniform(7,23),rng.uniform(7,23)))
    # Support planes define a single massive fractured body. They are not raised Voronoi cells.
    planes=[]
    count=20 if STYLES[shape]=='weathered' else 16
    for i in range(count):
        y=1-2*(i+.5)/count;a=i*2.399963+variant*.48
        axis=Vector((math.sqrt(1-y*y)*math.cos(a),y,math.sqrt(1-y*y)*math.sin(a)))
        axis=(axis+direction(rng)*.16).normalized();planes.append((axis,rng.uniform(.77,1.04)))
    strata=Vector((.33,.89,.31))+direction(rng)*.14;strata.normalize()
    faults=[(direction(rng),rng.uniform(-.46,.46),rng.uniform(.045,.09)) for _ in range(3)]
    pits=[(direction(rng),rng.uniform(.06,.19),rng.uniform(.025,.075)) for _ in range(18 if STYLES[shape]=='weathered' else 6)]
    if STYLES[shape]=='weathered':pits.append((Vector((.42,-.83,.27)).normalized(),.48,.34))
    return dict(shape=shape,style=STYLES[shape],seed=seed,off=off,planes=planes,strata=strata,faults=faults,pits=pits)

def unscale(p,c):return Vector((p.x/SHAPES[c['shape']][0],p.z/SHAPES[c['shape']][1],-p.y/SHAPES[c['shape']][2]))
def fault_distance(q,axis,offset,c):return q.dot(axis)+offset+.12*n(q+c['off'],3.4)+.025*n(q+c['off'],10)
def ore_score(p,c):
    q=unscale(p,c)
    distances=[abs(fault_distance(q,a,o,c)) for a,o,_ in (c['faults'][:1] if c['style']=='layered' else c['faults'])]
    distances.append(.085+.13*n(q+c['off'],3.5))
    if c['style']=='layered':distances.append(abs(math.sin((q.dot(c['strata'])*1.8+.25*n(q+c['off'],3))*math.pi))*.13)
    if c['style']=='weathered':distances.append(abs(q.length-.70)*.52+.025)
    return min(distances)+.008*n(q+c['off'],20)

def sculpt(c,variant):
    shape=c['shape'];style=c['style'];off=c['off']
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=7,radius=1);ob=bpy.context.object
    for v in ob.data.vertices:
        d=v.co.normalized()
        radius=min(h/max(.0001,d.dot(axis)) for axis,h in c['planes'])
        if style=='weathered':radius=.78*radius+.22*(.94+.075*n(d+off,2.8))
        if shape=='cluster':radius+=.085*n(d+off,3.1)
        if shape=='bean':radius*=.85+.15*d.z*d.z;radius+=.055*d.x*d.z
        if shape=='spindle':radius*=.78+.22*abs(d.z)
        if shape=='shard':radius*=.9+.1*d.z
        if shape=='wedge':radius*=.9+.17*d.x
        q=d*radius;p=q+off
        # Three major irregular shear faults instead of a uniform gravel-like rind.
        fracture=0
        for axis,offset,depth in c['faults']:
            fd=fault_distance(q,axis,offset,c)
            fracture-=depth*math.exp(-(fd/.055)**2)*(.78+.22*n(p,4))
        # Sparse fine fissures break up broad planes while retaining their large-scale shape.
        ds,_=noise.voronoi((q+noise.noise_vector(p*2.1)*.035)*4.0)
        fine=-.014*math.exp(-((ds[1]-ds[0])/.025)**2)*max(0,min(1,(n(p,3)+.1)*2))
        ledge=0
        if style=='layered':
            layer=q.dot(c['strata'])*3.6+.32*n(p,2.4)
            phase=layer-math.floor(layer)
            bevel=max(0,min(1,(phase-.78)/.22));bevel=bevel*bevel*(3-2*bevel)
            ledge=.10*(phase-.5-bevel)
        crater=0
        for axis,width,depth in c['pits']:
            angle=math.acos(max(-1,min(1,d.dot(axis))))
            if angle<width:crater-=depth*pow(max(0,1-(angle/width)**2),1.5)
        chip=.065*n(p,5.8)+.028*n(p,13)+.012*n(p,31)+.005*n(p,65)
        radius+=fracture+fine+ledge+crater+chip
        v.co=Vector(tuple(d[i]*radius*SHAPES[shape][i] for i in range(3)))
    ob.data.update()
    if shape=='hollow':
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=5,radius=1);cut=bpy.context.object
        cut.scale=(.52,1.1,.53) if variant==0 else (.40,1.65,.48)
        cut.location=(.11,-.80,.06) if variant==0 else (.03,0,.07)
        for v in cut.data.vertices:v.co*=1+.06*n(v.co+off,7)
        select(cut);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        mod=ob.modifiers.new('Weathered cavity','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
        apply(ob,mod);bpy.data.objects.remove(cut,do_unlink=True)
        mod=ob.modifiers.new('Closed cavity topology','REMESH');mod.mode='VOXEL';mod.voxel_size=.016;mod.use_smooth_shade=True;apply(ob,mod)
    for v in ob.data.vertices:
        x,y,z=v.co;v.co=(x,-z,y)
    clean(ob);return ob

def surface(ob,c,threshold):
    if threshold is None:
        entries=sorted((ore_score(f.center,c),f.area) for f in ob.data.polygons)
        total=sum(a for _,a in entries);walk=0
        for value,area in entries:
            walk+=area
            if walk>=total*.245:threshold=value;break
    positions=[v.co.copy() for v in ob.data.vertices];values=[ore_score(p,c)-threshold for p in positions]
    cuts={};faces=[];ids=[]
    def crossing(a,b):
        k=tuple(sorted((a,b)))
        if k not in cuts:
            f=values[a]/(values[a]-values[b]);cuts[k]=len(positions);positions.append(positions[a].lerp(positions[b],f))
        return cuts[k]
    for face in ob.data.polygons:
        vv=list(face.vertices)
        for part in (0,1):
            poly=[]
            for a,b in zip(vv,vv[1:]+vv[:1]):
                ina=values[a]<=0 if part else values[a]>0;inb=values[b]<=0 if part else values[b]>0
                if ina:poly.append(a)
                if ina!=inb:poly.append(crossing(a,b))
            for k in range(1,len(poly)-1):faces.append((poly[0],poly[k],poly[k+1]));ids.append(part)
    old=ob.data;mesh=bpy.data.meshes.new(ob.name+'_mesh');mesh.from_pydata(positions,[],faces);mesh.update();ob.data=mesh
    if old.users==0:bpy.data.meshes.remove(old)
    mesh.materials.append(stone);mesh.materials.append(ore);uv=mesh.uv_layers.new(name='SurfaceUV')
    for f,material in zip(mesh.polygons,ids):
        f.material_index=material;f.use_smooth=True
        # Box charts use the large support face normal: microchips keep a consistent texture direction.
        q=unscale(f.center,c)
        support=max(c['planes'],key=lambda x:q.dot(x[0])/x[1])[0]
        normal=Vector((support.x,-support.z,support.y));dominant=max(range(3),key=lambda i:abs(normal[i]))
        axes=[i for i in range(3) if i!=dominant]
        for li in f.loop_indices:
            p=mesh.vertices[mesh.loops[li].vertex_index].co
            uv.data[li].uv=(p[axes[0]]*1.35+(c['seed']%7)*.19,p[axes[1]]*1.35+(c['seed']%11)*.11)
    clean(ob)
    bm=bmesh.new();bm.from_mesh(mesh)
    for e in bm.edges:
        if len(e.link_faces)==2:e.smooth=e.calc_face_angle()<math.radians(48)
    bm.to_mesh(mesh);bm.free();mesh.update()
    return threshold

assets=[];report=[]
for shape in chosen:
    for variant in range(2):
        c=context(shape,variant);ob=sculpt(c,variant);base=ob.data.copy();threshold=None
        for lod,target in ((0,33000),(1,1300),(2,300)):
            a=ob if lod==0 else bpy.data.objects.new(shape,base.copy())
            if lod:scene.collection.objects.link(a)
            a.name=f'{shape}_{"AB"[variant]}_LOD{lod}'
            for key,value in dict(shape_family=shape,concept=c['style'],variant='AB'[variant],lod=lod,seed=c['seed']).items():a[key]=value
            dec=a.modifiers.new('Distance detail','DECIMATE');dec.ratio=min(1,target/len(a.data.polygons));apply(a,dec);clean(a)
            value=surface(a,c,threshold)
            if threshold is None:threshold=value
            a.data.name=a.name+'_mesh';a.data.calc_loop_triangles()
            assert not a.data.validate(),a.name
            bm=bmesh.new();bm.from_mesh(a.data);bad=sum(not e.is_manifold for e in bm.edges);volume=bm.calc_volume(signed=True);bm.free()
            record=dict(name=a.name,concept=c['style'],lod=lod,triangles=len(a.data.loop_triangles),vertices=len(a.data.vertices),nonmanifold_edges=bad,volume=volume,dimensions=list(a.dimensions),ore_fraction=sum(f.area for f in a.data.polygons if f.material_index==1)/sum(f.area for f in a.data.polygons))
            assert bad==0 and volume>0,record
            report.append(record);assets.append(a)
        if base.users==0:bpy.data.meshes.remove(base)
        print('BUILT',shape,'AB'[variant],c['style'],[r['triangles'] for r in report[-3:]],flush=True)

# Compact embedded fallback textures. Unity uses the full-resolution maps in Resources/Asteroids.
swaps=[]
for mat in (stone,ore):
    for node in mat.node_tree.nodes:
        if node.type=='TEX_IMAGE' and node.image:
            full=node.image;small=full.copy();small.name=full.name+'_fallback';small.scale(512,512)
            small.pack();node.image=small;swaps.append((node,full,small))
for lod in range(3):
    bpy.ops.object.select_all(action='DESELECT');group=[a for a in assets if a['lod']==lod]
    for a in group:a.select_set(True)
    bpy.context.view_layer.objects.active=group[0]
    bpy.ops.export_scene.gltf(filepath=str(OUT/'export'/f'asteroids_lod{lod}.glb'),export_format='GLB',use_selection=True,export_extras=True,export_yup=True,export_tangents=True,export_materials='EXPORT',export_cameras=False,export_lights=False)
for node,full,small in swaps:node.image=full;bpy.data.images.remove(small)
for a in assets:
    assert not a.data.validate(),a.name
    assert len(a.data.polygons)==next(r['triangles'] for r in report if r['name']==a.name)
(OUT/'mesh-report.json').write_text(json.dumps(dict(generator=bpy.app.version_string,models=report),indent=2))

for i,a in enumerate(assets):a.hide_render=True;a.hide_set(a['lod']!=0);a.location=((i//3%7)*4,0,(i//21)*4)
world=bpy.data.worlds.new('Space');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.05,.065,.085,1);world.node_tree.nodes['Background'].inputs[1].default_value=.10
light=bpy.data.lights.new('Sun','SUN');light.energy=3.5;light.angle=.009
sun=bpy.data.objects.new('Sun',light);scene.collection.objects.link(sun);sun.rotation_euler=(.45,-.5,-.65)
fill_data=bpy.data.lights.new('Faint reflected light','AREA');fill_data.energy=15;fill_data.shape='DISK';fill_data.size=4
fill=bpy.data.objects.new('Faint reflected light',fill_data);scene.collection.objects.link(fill);fill.location=(2,-3,2)
fill.rotation_euler=(-fill.location).to_track_quat('-Z','Y').to_euler()
cam_data=bpy.data.cameras.new('Camera');cam=bpy.data.objects.new('Camera',cam_data);scene.collection.objects.link(cam);scene.camera=cam
cam.location=(3,-4,2.3);cam.rotation_euler=(-cam.location).to_track_quat('-Z','Y').to_euler();cam_data.lens=56
scene.render.resolution_x=1200;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
for concept in ('fractured','layered','weathered'):
    hero=next((a for a in assets if a['concept']==concept and a['lod']==0),None)
    if hero is None:continue
    pos=hero.location.copy();hero.location=(0,0,0);hero.hide_render=False
    hero.rotation_euler=(.15,.10,.3)
    scene.render.filepath=str(OUT/'renders'/(concept+'.png'));bpy.ops.render.render(write_still=True)
    hero.location=pos;hero.hide_render=True;hero.rotation_euler=(0,0,0)
hero=assets[0];hero.location=(0,0,0);hero.hide_render=False
for im in bpy.data.images:
    if im.source=='FILE':im.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'asteroids.blend'),compress=True)
print('DONE',len(assets),'meshes',flush=True)
