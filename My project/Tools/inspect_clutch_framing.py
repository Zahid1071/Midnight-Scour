import bpy
from mathutils import Vector

clutch = bpy.data.objects.get("Clutch")
spark = bpy.data.objects.get("SparkPlug")
cam = bpy.context.scene.camera

def info(o):
    if not o:
        return
    bw = [o.matrix_world @ Vector(c) for c in o.bound_box]
    mn = Vector((min(v.x for v in bw), min(v.y for v in bw), min(v.z for v in bw)))
    mx = Vector((max(v.x for v in bw), max(v.y for v in bw), max(v.z for v in bw)))
    center = (mn + mx) * 0.5
    size = mx - mn
    print(o.name, "loc", tuple(o.location), "scale", tuple(o.scale), "rot", tuple(o.rotation_euler))
    print("  world center", tuple(center), "size", tuple(size))
    if cam:
        print("  dist to cam", (center - cam.location).length)

info(clutch)
info(spark)
light = bpy.data.objects.get("Light")
if light:
    print("Light loc", tuple(light.location), "rot", tuple(light.rotation_euler), "energy", light.data.energy, "size", getattr(light.data, "size", None))
