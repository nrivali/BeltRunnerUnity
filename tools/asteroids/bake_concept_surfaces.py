"""Blender batch bake of the approved concept's stone grain and exposed metal microfinish.
Stages maps under ArtSource/AsteroidsV4/textures; never overwrites runtime assets.
"""
from pathlib import Path
import bpy, shutil
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'ArtSource/AsteroidsV4'
TEX=OUT/'textures';TEX.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1
scene.render.bake.margin=8
bpy.ops.mesh.primitive_plane_add(size=2)
plane=bpy.context.object

def bake_material(kind):
    mat=bpy.data.materials.new(kind);mat.use_nodes=True;mat.use_fake_user=True
    plane.data.materials.clear();plane.data.materials.append(mat)
    nt=mat.node_tree;bs=nt.nodes.get('Principled BSDF');out=nt.nodes.get('Material Output')
    uv=nt.nodes.new('ShaderNodeTexCoord')
    if kind=='regolith':
        src=nt.nodes.new('ShaderNodeTexImage');src.image=bpy.data.images.load(str(OUT/'imagegen-stone.png'))
        src.image.colorspace_settings.name='sRGB'
        nt.links.new(uv.outputs['UV'],src.inputs['Vector'])
        color=src.outputs['Color'];height=color
        strength=.42;distance=.012
        shutil.copyfile(OUT/'imagegen-stone.png',TEX/'regolith_albedo.png')
    else:
        grain=nt.nodes.new('ShaderNodeTexNoise');grain.inputs['Scale'].default_value=115
        grain.inputs['Detail'].default_value=3;grain.inputs['Roughness'].default_value=.7
        nt.links.new(uv.outputs['UV'],grain.inputs['Vector'])
        ramp=nt.nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].color=(.48,.48,.48,1)
        ramp.color_ramp.elements[1].color=(.84,.84,.84,1)
        nt.links.new(grain.outputs['Fac'],ramp.inputs[0])
        color=ramp.outputs['Color'];height=grain.outputs['Fac']
        strength=.24;distance=.0018
    nt.links.new(color,bs.inputs['Base Color'])
    bump=nt.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=strength;bump.inputs['Distance'].default_value=distance
    nt.links.new(height,bump.inputs['Height']);nt.links.new(bump.outputs['Normal'],bs.inputs['Normal'])
    target=nt.nodes.new('ShaderNodeTexImage');emit=nt.nodes.new('ShaderNodeEmission')
    rough=nt.nodes.new('ShaderNodeMapRange');rough.clamp=True
    rough.inputs['From Min'].default_value=0;rough.inputs['From Max'].default_value=.5 if kind=='regolith' else 1
    rough.inputs['To Min'].default_value=.98 if kind=='regolith' else .85
    rough.inputs['To Max'].default_value=.78 if kind=='regolith' else 1
    nt.links.new(height,rough.inputs['Value'])
    pack=nt.nodes.new('ShaderNodeCombineColor');pack.mode='RGB'
    pack.inputs[0].default_value=1;pack.inputs[2].default_value=0 if kind=='regolith' else 1
    nt.links.new(rough.outputs[0],pack.inputs[1])
    for suffix in (('normal','metalrough') if kind=='regolith' else ('albedo','normal','metalrough')):
        im=bpy.data.images.new(kind+'_'+suffix,width=2048,height=2048,alpha=False)
        im.colorspace_settings.name='sRGB' if suffix=='albedo' else 'Non-Color'
        target.image=im;nt.nodes.active=target
        if suffix=='normal':
            nt.links.new(bs.outputs[0],out.inputs['Surface']);bpy.ops.object.bake(type='NORMAL')
        else:
            nt.links.new(color if suffix=='albedo' else pack.outputs[0],emit.inputs['Color'])
            nt.links.new(emit.outputs[0],out.inputs['Surface']);bpy.ops.object.bake(type='EMIT')
        im.filepath_raw=str(TEX/(kind+'_'+suffix+'.png'));im.file_format='PNG';im.save()
        print('BAKED',im.filepath_raw,flush=True)
    target.image=None
    nt.links.new(bs.outputs[0],out.inputs['Surface'])
    bs.inputs['Metallic'].default_value=0 if kind=='regolith' else .96
    bs.inputs['Roughness'].default_value=.88 if kind=='regolith' else .28

bake_material('regolith');bake_material('ore')
for im in bpy.data.images:
    if im.source=='FILE':im.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'surface-materials.blend'),compress=True)
