"""
Render car-part icons into the Midnight Scour item-icon style using Icon Renders.blend.
"""
import bpy
import math
from mathutils import Vector
from pathlib import Path

OUT_DIR = Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Art\Images\Item Icons")
MODELS = [
    {
        "name": "CarBattery",
        "out": "CarBattery_Icon.png",
        "path": Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Models\Car Parts\Battery\car_battery.glb"),
        "fill": 0.68,
        "light_mult": 2.4,
        "rot": (30, 20, -40),
    },
    {
        "name": "JerryCan",
        "out": "JerryCan_Icon.png",
        "path": Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Models\Car Parts\JerryCan\metal_jerrycan_green_2k.fbx"),
        "fill": 0.72,
        "light_mult": 1.35,
        "rot": (20, 10, -50),
    },
    {
        "name": "SparkPlug",
        "out": "SparkPlug_Icon.png",
        "path": Path(r"c:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Models\Car Parts\spark-plug\source\SparkPlug.fbx"),
        "fill": 0.78,
        "light_mult": 2.8,
        "rot": (15, 25, -120),
    },
]

RES = 500
BASE_LIGHT_ENERGY = None


def hide_all_meshes(hide=True):
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_set(hide)
            o.hide_render = hide
            o.hide_viewport = hide


def delete_objects(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        if o is None:
            continue
        try:
            o.hide_set(False)
            o.hide_viewport = False
            o.select_set(True)
        except Exception:
            pass
    if bpy.context.selected_objects:
        bpy.ops.object.delete()


def import_model(path: Path):
    before = set(bpy.data.objects)
    suffix = path.suffix.lower()
    if suffix in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(path))
    elif suffix == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    else:
        raise RuntimeError(f"Unsupported: {path}")
    return [o for o in bpy.data.objects if o not in before]


def mesh_objects(objs):
    return [o for o in objs if o.type == "MESH"]


def world_bounds(objs):
    pts = []
    for o in objs:
        for c in o.bound_box:
            pts.append(o.matrix_world @ Vector(c))
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx, (mn + mx) * 0.5, (mx - mn)


def ensure_root(meshes, name):
    root = bpy.data.objects.new(name + "_Root", None)
    bpy.context.scene.collection.objects.link(root)
    for m in meshes:
        mw = m.matrix_world.copy()
        m.parent = root
        m.matrix_world = mw
    return root


def frame_in_camera(root, meshes, cam, fill=0.72, rot_deg=(25, 15, -35)):
    root.location = (0, 0, 0)
    root.scale = (1, 1, 1)
    root.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()

    mn, mx, center, size = world_bounds(meshes)
    root.location -= center
    bpy.context.view_layer.update()

    root.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.view_layer.update()

    mn, mx, center, size = world_bounds(meshes)
    root.location -= center
    bpy.context.view_layer.update()
    mn, mx, center, size = world_bounds(meshes)

    cam_mw = cam.matrix_world
    forward = cam_mw.to_quaternion() @ Vector((0, 0, -1))
    sensor = cam.data.sensor_width
    lens = cam.data.lens
    fov = 2.0 * math.atan((sensor * 0.5) / lens)
    max_dim = max(size.x, size.y, size.z, 1e-5)
    dist = (max_dim * 0.5) / (math.tan(fov * 0.5) * fill)
    target = cam.location + forward * dist
    root.location += target
    bpy.context.view_layer.update()


def setup_render():
    global BASE_LIGHT_ENERGY
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x = RES
    sc.render.resolution_y = RES
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.image_settings.color_depth = "8"
    light = bpy.data.objects.get("Light")
    if light and light.type == "LIGHT":
        BASE_LIGHT_ENERGY = light.data.energy


def set_light_mult(mult):
    light = bpy.data.objects.get("Light")
    if light and light.type == "LIGHT" and BASE_LIGHT_ENERGY is not None:
        light.data.energy = BASE_LIGHT_ENERGY * mult


def render_to(path: Path):
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    print("WROTE", path)


def main():
    setup_render()
    cam = bpy.context.scene.camera
    assert cam is not None, "No camera"
    hide_all_meshes(True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    for job in MODELS:
        path = job["path"]
        if not path.exists():
            print("MISSING", path)
            continue
        print("===", job["name"], path)
        set_light_mult(job.get("light_mult", 1.0))
        imported = import_model(path)
        meshes = mesh_objects(imported)
        if not meshes:
            print("No meshes imported")
            delete_objects(imported)
            continue

        for m in meshes:
            m.hide_set(False)
            m.hide_render = False
            m.hide_viewport = False

        root = ensure_root(meshes, job["name"])
        frame_in_camera(root, meshes, cam, fill=job.get("fill", 0.7), rot_deg=job.get("rot", (25, 15, -35)))
        render_to(OUT_DIR / job["out"])
        delete_objects([root] + imported)

    set_light_mult(1.0)
    print("DONE")


if __name__ == "__main__":
    main()
