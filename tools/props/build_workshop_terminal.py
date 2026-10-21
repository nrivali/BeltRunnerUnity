"""The workshop terminal (2026-10-21): a hard-surface console for the hangar deck, built in Blender from code.

Run: blender -b -P tools/props/build_workshop_terminal.py -- [--preview DIR]
Writes Assets/Resources/Models/workshop_terminal.glb (and, with --preview, a few renders to DIR).

Units are metres, Z up, the front faces -Y (glTF's +Z, the terminal's front in Unity), the origin on the floor under
its middle. The game scales it by Walker's metre (Walker.M). The screen is the node "Screen" with the material
"screen": an emissive UI drawn into a texture here, which the game brightens through the material's emissiveFactor.
"""
import bpy, bmesh, math, sys
from mathutils import Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Resources/Models/workshop_terminal.glb'
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
PREVIEW = Path(argv[argv.index('--preview') + 1]) if '--preview' in argv else None

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---- materials
def hexc(h, a=1.0):
    h = h.lstrip('#')
    s = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in s]
    return (lin[0], lin[1], lin[2], a)

def mat(name, col, metal=0.5, rough=0.45, emit=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bs = m.node_tree.nodes['Principled BSDF']
    bs.inputs['Base Color'].default_value = hexc(col)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    if emit:
        bs.inputs['Emission Color'].default_value = hexc(emit)
        bs.inputs['Emission Strength'].default_value = strength
    return m

M_BODY = mat('body', '#566089', 0.65, 0.42)
M_DARK = mat('dark', '#1f2436', 0.5, 0.6)
M_STEEL = mat('steel', '#a7b0c8', 0.85, 0.3)
M_TRIM = mat('trim', '#c8912e', 0.35, 0.4)
M_RUBBER = mat('rubber', '#14161d', 0.0, 0.85)
M_GLOW_C = mat('glow_cyan', '#0d2a33', 0.0, 0.5, '#5ed3f0', 6.0)
M_GLOW_A = mat('glow_amber', '#3a2608', 0.0, 0.5, '#ffb347', 5.0)
M_KEY = mat('key', '#2c3349', 0.3, 0.55)

def screen_material():
    """The screen's UI, drawn into pixels: a header band, three recipe rows with their ore pips and progress bars, a
    faint grid, corner brackets. Emissive, so it reads as lit in a dim hangar."""
    W, H = 512, 320
    img = bpy.data.images.new('screen_ui', W, H, alpha=False)
    px = [0.0] * (W * H * 4)
    bg = (0.012, 0.045, 0.06)
    def put(x, y, c):
        if 0 <= x < W and 0 <= y < H:
            i = (y * W + x) * 4
            px[i:i + 4] = [c[0], c[1], c[2], 1.0]
    def rect(x0, y0, x1, y1, c):
        for y in range(max(0, y0), min(H, y1)):
            for x in range(max(0, x0), min(W, x1)):
                put(x, y, c)
    rect(0, 0, W, H, bg)
    cy, am, dim = (0.22, 0.75, 0.9), (1.0, 0.62, 0.18), (0.05, 0.16, 0.2)
    for x in range(0, W, 32): rect(x, 0, x + 1, H, dim)            # the grid
    for y in range(0, H, 32): rect(0, y, W, y + 1, dim)
    rect(0, H - 46, W, H - 40, cy)                                  # the header band and its title block
    rect(24, H - 34, 220, H - 14, cy)
    rect(W - 120, H - 34, W - 24, H - 14, am)
    for k in range(3):                                              # the recipe rows
        y0 = H - 92 - k * 74
        rect(24, y0 - 46, W - 24, y0, (0.03, 0.1, 0.13))
        rect(24, y0 - 46, 28, y0, cy)
        rect(40, y0 - 30, 64, y0 - 16, am)                          # the ore pip
        rect(76, y0 - 22, 300, y0 - 16, (0.12, 0.4, 0.48))          # the recipe's name
        fill = [0.8, 0.45, 1.0][k]
        rect(76, y0 - 38, 76 + int(300 * fill), y0 - 32, cy)        # its progress
        rect(76 + int(300 * fill), y0 - 38, 376, y0 - 32, dim)
        rect(W - 112, y0 - 36, W - 40, y0 - 12, am if k == 2 else (0.1, 0.32, 0.38))   # Make
    for (x, y, sx, sy) in ((6, 6, 1, 1), (W - 7, 6, -1, 1), (6, H - 7, 1, -1), (W - 7, H - 7, -1, -1)):   # brackets
        for t in range(18):
            put(x + sx * t, y, cy); put(x, y + sy * t, cy)
    img.pixels = px
    img.pack()
    m = bpy.data.materials.new('screen')
    m.use_nodes = True
    nt = m.node_tree
    bs = nt.nodes['Principled BSDF']
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    nt.links.new(tex.outputs['Color'], bs.inputs['Base Color'])
    nt.links.new(tex.outputs['Color'], bs.inputs['Emission Color'])
    bs.inputs['Emission Strength'].default_value = 2.5
    bs.inputs['Roughness'].default_value = 0.75   # matte: the glossy glass caught the terminal's own light as a hot spot
    bs.inputs['Metallic'].default_value = 0.0
    return m

M_SCREEN = screen_material()

# ---- shapes
def finish(o, m, bevel):
    o.data.materials.append(m)
    if bevel > 0:
        b = o.modifiers.new('bevel', 'BEVEL')
        b.width = bevel
        b.segments = 2
        b.limit_method = 'ANGLE'
    return o

def box(name, size, loc, m, bevel=0.008, rot=(0, 0, 0), parent=None, taper=None):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]; v.co.y *= size[1]; v.co.z *= size[2]
        if taper and v.co.z > 0:   # the top face narrowed: (x, y) factors
            v.co.x *= taper[0]; v.co.y *= taper[1]
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = [math.radians(a) for a in rot]
    if parent: o.parent = parent
    return finish(o, m, bevel)

def cyl(name, r, depth, loc, m, rot=(0, 0, 0), verts=20, parent=None, bevel=0.004):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=depth)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = [math.radians(a) for a in rot]
    if parent: o.parent = parent
    return finish(o, m, bevel)

# the plinth, with an amber band round its top and hazard stripes on its front
box('Plinth', (1.10, 0.80, 0.08), (0, 0, 0.04), M_DARK, 0.01)
for (n, sz, at) in (('F', (1.06, 0.03, 0.012), (0, -0.37, 0.085)), ('B', (1.06, 0.03, 0.012), (0, 0.37, 0.085)), ('L', (0.03, 0.71, 0.012), (-0.515, 0, 0.085)), ('R', (0.03, 0.71, 0.012), (0.515, 0, 0.085))):
    box('PlinthRim' + n, sz, at, M_TRIM, 0.003)
# hazard stripes across the plinth's front: an amber band with dark diagonals on it
box('HazardBand', (1.04, 0.006, 0.05), (0, -0.401, 0.04), M_TRIM, 0.0)
for i in range(14):
    box('Hazard%d' % i, (0.028, 0.008, 0.068), (-0.49 + i * 0.075, -0.403, 0.04), M_RUBBER, 0.0, rot=(0, 40, 0))

# the pedestal, narrowing a little to the top; a recessed front with vents and the fabricator's output hatch
box('Pedestal', (0.72, 0.48, 0.86), (0, 0.04, 0.51), M_BODY, 0.02, taper=(0.86, 0.9))
box('FrontPanel', (0.5, 0.02, 0.44), (0, -0.195, 0.6), M_DARK, 0.006)
for i in range(6):
    box('Vent%d' % i, (0.38, 0.012, 0.022), (0, -0.21, 0.46 + i * 0.05), M_RUBBER, 0.003)
box('HatchFrame', (0.46, 0.04, 0.14), (0, -0.2, 0.22), M_TRIM, 0.008)
box('Hatch', (0.4, 0.03, 0.09), (0, -0.215, 0.22), M_DARK, 0.004)
box('HatchGlow', (0.34, 0.01, 0.018), (0, -0.232, 0.22), M_GLOW_C, 0.0)
for s in (-1, 1):
    box('SidePanel%d' % s, (0.014, 0.34, 0.56), (s * 0.335, 0.04, 0.5), M_STEEL, 0.004, rot=(0, s * -3.5, 0))
    for (y, z) in ((-0.12, 0.26), (0.2, 0.26), (-0.12, 0.74), (0.2, 0.74)):
        cyl('Bolt', 0.014, 0.012, (s * 0.345, y, z), M_DARK, rot=(0, 90, 0), verts=10)
    box('Grip%d' % s, (0.03, 0.03, 0.26), (s * 0.37, -0.1, 0.62), M_RUBBER, 0.008)

# the desk: a sloped slab at waist height, its front edge low, with keys, a trackball and two sliders
desk = box('Desk', (1.2, 0.56, 0.06), (0, -0.12, 0.98), M_BODY, 0.012, rot=(18, 0, 0))
box('DeskLip', (1.2, 0.03, 0.03), (0, -0.29, 0.03), M_TRIM, 0.006, parent=desk)
lit = {(0, 2): M_GLOW_A, (1, 6): M_GLOW_C, (2, 0): M_GLOW_C, (0, 7): M_GLOW_A, (2, 4): M_GLOW_A}
for r in range(3):
    for c in range(9):
        box('Key', (0.062, 0.048, 0.016), (-0.32 + c * 0.072, -0.2 + r * 0.06, 0.036), lit.get((r, c), M_KEY), 0.004, parent=desk)
ring = cyl('BallRing', 0.06, 0.016, (0.44, -0.12, 0.034), M_STEEL, parent=desk, verts=24)
ball = bpy.data.objects.new('Trackball', bpy.data.meshes.new('Trackball'))
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=0.042); bm.to_mesh(ball.data); bm.free()
bpy.context.collection.objects.link(ball); ball.parent = desk; ball.location = (0.44, -0.12, 0.05); ball.data.materials.append(M_DARK)
for i in range(2):
    box('SliderTrack', (0.022, 0.2, 0.008), (-0.47 + i * 0.06, -0.1, 0.032), M_RUBBER, 0.002, parent=desk)
    box('SliderKnob', (0.04, 0.03, 0.02), (-0.47 + i * 0.06, -0.14 + i * 0.07, 0.042), M_TRIM, 0.004, parent=desk)

# the screen on two struts from the pedestal's back, tilted back, in a bezel with a visor and a status bar on top
for s in (-1, 1):
    box('Strut%d' % s, (0.07, 0.07, 0.62), (s * 0.3, 0.2, 1.12), M_STEEL, 0.01)
    box('StrutFoot%d' % s, (0.12, 0.12, 0.05), (s * 0.3, 0.2, 0.83), M_DARK, 0.008)
bezel = box('Bezel', (1.12, 0.07, 0.68), (0, 0.16, 1.58), M_DARK, 0.014, rot=(-12, 0, 0))
box('Visor', (1.14, 0.12, 0.025), (0, -0.04, 0.35), M_BODY, 0.008, rot=(10, 0, 0), parent=bezel)
box('StatusBar', (0.5, 0.012, 0.018), (0, -0.04, 0.305), M_GLOW_A, 0.0, parent=bezel)
box('Mount', (0.72, 0.07, 0.24), (0, 0.065, -0.06), M_STEEL, 0.01, parent=bezel)   # the bracket the struts carry
for s in (-1, 1):
    box('BezelEar%d' % s, (0.05, 0.09, 0.4), (s * 0.58, 0.0, 0.0), M_BODY, 0.01, parent=bezel)
bpy.ops.mesh.primitive_plane_add(size=1.0)
scr = bpy.context.object
scr.name = 'Screen'
scr.scale = (1.0, 0.6, 1.0)
scr.rotation_euler = (math.radians(90), 0, 0)
scr.location = (0, -0.037, 0.0)
scr.parent = bezel
scr.data.materials.append(M_SCREEN)

# the cables: two hoses from the pedestal's back down to the deck, trailing behind
for s in (-1, 1):
    cu = bpy.data.curves.new('Cable', 'CURVE')
    cu.dimensions = '3D'; cu.bevel_depth = 0.022; cu.bevel_resolution = 3
    sp = cu.splines.new('BEZIER'); sp.bezier_points.add(2)
    pts = [(s * 0.18, 0.28, 0.7), (s * 0.22, 0.48, 0.25), (s * 0.3, 0.75, 0.03)]
    for p, co in zip(sp.bezier_points, pts):
        p.co = co; p.handle_left_type = p.handle_right_type = 'AUTO'
    o = bpy.data.objects.new('Cable%d' % s, cu)
    bpy.context.collection.objects.link(o)
    o.data.materials.append(M_RUBBER)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.convert(target='MESH')
    o.select_set(False)
    box('CableClamp%d' % s, (0.07, 0.05, 0.07), (s * 0.18, 0.27, 0.72), M_TRIM, 0.006)

# flat-shaded faces with soft bevels read as machined metal; the trackball and the cables are smooth
for o in bpy.data.objects:
    if o.type == 'MESH' and (o.name.startswith('Trackball') or o.name.startswith('Cable')):
        for poly in o.data.polygons: poly.use_smooth = True

model_objects = [o for o in bpy.data.objects]

# ---- previews
if PREVIEW:
    PREVIEW.mkdir(parents=True, exist_ok=True)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 48
    scene.cycles.device = 'CPU'
    scene.render.resolution_x = 900; scene.render.resolution_y = 900
    world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.02, 0.025, 0.035, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.0
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    bpy.context.object.data.materials.append(mat('floor', '#8b8f98', 0.2, 0.6))
    for (loc, energy, col) in (((-2.5, -3.0, 3.5), 900, (1, 0.93, 0.85)), ((3, -1.5, 2.0), 300, (0.6, 0.85, 1)), ((0.5, 3, 3), 400, (1, 1, 1))):
        L = bpy.data.lights.new('L', 'AREA'); L.energy = energy; L.size = 2.0; L.color = col
        lo = bpy.data.objects.new('L', L); scene.collection.objects.link(lo); lo.location = loc
        lo.rotation_euler = Vector((-loc[0], -loc[1], 1.0 - loc[2])).to_track_quat("-Z", "Y").to_euler()
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.lens = 50
    for name, pos in (('front', (-1.6, -3.0, 1.6)), ('side', (3.2, -1.0, 1.4)), ('back', (1.8, 3.0, 1.9))):
        cam.location = pos
        cam.rotation_euler = Vector((0 - pos[0], 0 - pos[1], 0.9 - pos[2])).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(PREVIEW / ('workshop_terminal_' + name + '.png'))
        bpy.ops.render.render(write_still=True)

# ---- export: the model alone
for o in bpy.data.objects: o.select_set(o in model_objects)
OUT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(OUT), export_format='GLB', use_selection=True, export_apply=True, export_yup=True)
dg = bpy.context.evaluated_depsgraph_get()
tris = 0
for o in model_objects:
    if o.type != 'MESH': continue
    me = o.evaluated_get(dg).to_mesh(); me.calc_loop_triangles(); tris += len(me.loop_triangles); o.evaluated_get(dg).to_mesh_clear()
print('workshop terminal: %d objects, %d triangles -> %s' % (len(model_objects), tris, OUT))
