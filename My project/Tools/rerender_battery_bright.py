"""Brighter battery-only pass using Icon Renders.blend studio."""
import bpy
import math
from mathutils import Vector
from pathlib import Path

OUT = Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Art\Images\Item Icons\CarBattery_Icon.png")
SRC = Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Models\Car Parts\Battery\car_battery.glb")


def hide_meshes(hide=True):
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_set(hide)
            o.hide_render = hide


def world_bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return (mn + mx) * 0.5, (mx - mn)


hide_meshes(True)
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(SRC))
imported = [o for o in bpy.data.objects if o not in before]
meshes = [o for o in imported if o.type == "MESH"]
for m in meshes:
    m.hide_set(False)
    m.hide_render = False

root = bpy.data.objects.new("BattRoot", None)
bpy.context.scene.collection.objects.link(root)
for m in meshes:
    mw = m.matrix_world.copy()
    m.parent = root
    m.matrix_world = mw

center, size = world_bounds(meshes)
root.location -= center
bpy.context.view_layer.update()
root.rotation_euler = (math.radians(28), math.radians(18), math.radians(-38))
bpy.context.view_layer.update()
center, size = world_bounds(meshes)
root.location -= center
bpy.context.view_layer.update()
center, size = world_bounds(meshes)

cam = bpy.context.scene.camera
forward = cam.matrix_world.to_quaternion() @ Vector((0, 0, -1))
fov = 2.0 * math.atan((cam.data.sensor_width * 0.5) / cam.data.lens)
dist = (max(size) * 0.5) / (math.tan(fov * 0.5) * 0.65)
root.location += cam.location + forward * dist

light = bpy.data.objects["Light"]
base = light.data.energy
light.data.energy = base * 3.5
# Soft fill via world
world = bpy.context.scene.world
if world and world.use_nodes:
    bg = next((n for n in world.node_tree.nodes if n.type == "BACKGROUND"), None)
    if bg:
        bg.inputs[1].default_value = max(bg.inputs[1].default_value, 0.35)

sc = bpy.context.scene
sc.render.resolution_x = sc.render.resolution_y = 500
sc.render.film_transparent = True
sc.render.filepath = str(OUT)
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"
bpy.ops.render.render(write_still=True)
print("WROTE", OUT)
light.data.energy = base
