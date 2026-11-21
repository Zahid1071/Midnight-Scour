import bpy
from pathlib import Path

print("=== SCENE ===")
print("file", bpy.data.filepath)
print("objects:", [o.name for o in bpy.data.objects])
print("cameras:", [o.name for o in bpy.data.objects if o.type == "CAMERA"])
print("lights:", [(o.name, o.data.type if o.data else None, getattr(o.data, "energy", None)) for o in bpy.data.objects if o.type == "LIGHT"])
cam = bpy.context.scene.camera
if cam:
    print("active cam", cam.name, "loc", tuple(cam.location), "rot", tuple(cam.rotation_euler))
    print("lens", cam.data.lens, "sensor", cam.data.sensor_width, "type", cam.data.type)
sc = bpy.context.scene
print("engine", sc.render.engine)
print("res", sc.render.resolution_x, sc.render.resolution_y, "pct", sc.render.resolution_percentage)
print("film transparent", getattr(sc.render, "film_transparent", None))
print("world", sc.world.name if sc.world else None)
if sc.world and sc.world.use_nodes:
    for n in sc.world.node_tree.nodes:
        print(" world node", n.type, n.name)
print("collections:", [c.name for c in bpy.data.collections])
for o in bpy.data.objects:
    if o.type == "MESH":
        print(" mesh", o.name, "hide", o.hide_get(), "hide_render", o.hide_render, "dims", tuple(o.dimensions))
