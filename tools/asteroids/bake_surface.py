"""Run with Blender --background --python tools/asteroids/bake_surface.py.
Bakes a restrained tangent normal and non-metallic roughness from the Imagegen stone surface.
No mesh silhouettes or ore masks are changed.
"""
from pathlib import Path
import bpy

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT/'ArtSource/Asteroids/imagegen-regolith.png'
OUT = ROOT/'Assets/Resources/Asteroids'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 1
scene.render.bake.margin = 4
bpy.ops.mesh.primitive_plane_add(size=2)
plane = bpy.context.object
mat = bpy.data.materials.new('Fractured carbonaceous regolith')
mat.use_nodes = True
plane.data.materials.append(mat)
nt = mat.node_tree
bs = nt.nodes.get('Principled BSDF')
out = nt.nodes.get('Material Output')
src = nt.nodes.new('ShaderNodeTexImage')
src.image = bpy.data.images.load(str(SOURCE))
src.image.colorspace_settings.name = 'sRGB'
nt.links.new(src.outputs['Color'], bs.inputs['Base Color'])
# The former bake used a 0.095 distance; this restrained relief retains larger stone plates
# without pushing the normals of every grain almost parallel to the surface.
bump = nt.nodes.new('ShaderNodeBump')
bump.inputs['Strength'].default_value = .45
bump.inputs['Distance'].default_value = .025
nt.links.new(src.outputs['Color'], bump.inputs['Height'])
nt.links.new(bump.outputs['Normal'], bs.inputs['Normal'])
target = nt.nodes.new('ShaderNodeTexImage')
emit = nt.nodes.new('ShaderNodeEmission')
for name in ('regolith_normal', 'regolith_metalrough'):
    image = bpy.data.images.new(name, width=2048, height=2048, alpha=False)
    image.colorspace_settings.name = 'Non-Color'
    target.image = image
    nt.nodes.active = target
    if name.endswith('normal'):
        nt.links.new(bs.outputs[0], out.inputs['Surface'])
        bpy.ops.object.bake(type='NORMAL')
    else:
        ramp = nt.nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].position = .015
        ramp.color_ramp.elements[0].color = (.98,.98,.98,1)
        ramp.color_ramp.elements[1].position = .28
        ramp.color_ramp.elements[1].color = (.78,.78,.78,1)
        nt.links.new(src.outputs['Color'],ramp.inputs[0])
        pack = nt.nodes.new('ShaderNodeCombineColor')
        pack.mode = 'RGB'
        pack.inputs[0].default_value = 1
        pack.inputs[2].default_value = 0
        nt.links.new(ramp.outputs['Color'],pack.inputs[1])
        nt.links.new(pack.outputs[0],emit.inputs['Color'])
        nt.links.new(emit.outputs[0],out.inputs['Surface'])
        bpy.ops.object.bake(type='EMIT')
    image.filepath_raw = str(OUT/(name+'.png'))
    image.file_format = 'PNG'
    image.save()
    print('Baked', image.filepath_raw, flush=True)
nt.links.new(bs.outputs[0],out.inputs['Surface'])
target.image = None
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/Asteroids/surface-material.blend'))
bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/Asteroids/surface-material.blend'))
