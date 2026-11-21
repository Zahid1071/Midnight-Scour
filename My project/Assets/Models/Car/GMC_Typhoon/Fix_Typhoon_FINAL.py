"""
FINAL Typhoon cleanup for Unity:
- Delete suspension/brakes
- Keep seats (Interior)
- Join split parts
- Set MESH ORIGIN to original bone hinge (so Unity rotate tool pivots correctly)
- No empty hinge chains, no decimate
- Export clean GLB
"""
import bpy
import mathutils
import shutil
from pathlib import Path
from collections import defaultdict

GLB = Path(r"C:\Users\babun\Downloads\gmc-typhoon-1992\source\GMC_Typhoon_1992_FH6_Sketchfab.glb")
OUT = Path(r"C:\Users\babun\Downloads\GMC_Typhoon_GameReady.glb")
OUT_FINAL = Path(
    r"C:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Models\Car\GMC_Typhoon\GMC_Typhoon_GameReady.glb"
)

# Wipe previous
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(GLB))

root = bpy.data.objects.get("GMC_TYPHOON_NATIVE::root")
assert root is not None


def delete_by_needles(*needles):
    n = 0
    for o in list(bpy.data.objects):
        low = o.name.lower()
        if any(k in low for k in needles):
            bpy.data.objects.remove(o, do_unlink=True)
            n += 1
    print("deleted", n, needles)


def mesh_descendants(obj):
    return [c for c in obj.children_recursive if c.type == "MESH"]


def join_selected(name):
    sel = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    if not sel:
        return None
    bpy.context.view_layer.objects.active = sel[0]
    if len(sel) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = name
    return joined


def make_hinged_part(bone_name, out_name):
    bone = bpy.data.objects.get(bone_name)
    assert bone is not None, bone_name
    hinge_world = bone.matrix_world.translation.copy()
    meshes = mesh_descendants(bone)
    assert meshes, "no meshes under " + bone_name

    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.hide_set(False)
        mw = m.matrix_world.copy()
        m.parent = None
        m.matrix_world = mw
        m.select_set(True)

    joined = join_selected(out_name)
    bpy.context.scene.cursor.location = hinge_world
    bpy.ops.object.select_all(action="DESELECT")
    joined.select_set(True)
    bpy.context.view_layer.objects.active = joined
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    mw = joined.matrix_world.copy()
    joined.parent = root
    joined.matrix_world = mw
    print(
        f"HINGED {out_name} pivot={tuple(round(v, 3) for v in joined.matrix_world.translation)} "
        f"faces={len(joined.data.polygons)}"
    )
    return joined


# 1) Kill suspension / brakes / glass / blackhole occlusion cards
delete_by_needles(
    "controlarm", "caliper", "gmc_rotor", "rotor_", "rotorlr", "rotorrr",
    "undercarriage", "wiper",
)


def delete_meshes_by_material(*needles):
    n = 0
    for o in list(bpy.data.objects):
        if o.type != "MESH":
            continue
        mats = [s.material.name.lower() for s in o.material_slots if s.material]
        if any(any(k in m for k in needles) for m in mats):
            bpy.data.objects.remove(o, do_unlink=True)
            n += 1
    print("deleted_by_material", n, needles)


# Keep glass meshes (Unity applies TyphoonHide / TyphoonGlass).
# Only strip Forza blackhole occlusion cards.
delete_meshes_by_material("blackhole")

# 2) Hinged parts — origin = original bone pivot
make_hinged_part("GMC_TYPHOON_NATIVE::boneHood", "Hood")
make_hinged_part("GMC_TYPHOON_NATIVE::boneDoorLF", "Door_L")
make_hinged_part("GMC_TYPHOON_NATIVE::boneDoorRF", "Door_R")
make_hinged_part("GMC_TYPHOON_NATIVE::root_boneTrunk", "Trunk")

# 3) Remaining meshes
HINGED = {"Hood", "Door_L", "Door_R", "Trunk"}

remaining = [o for o in bpy.data.objects if o.type == "MESH" and o.name not in HINGED]
buckets = defaultdict(list)
for o in remaining:
    n = o.name.lower()
    if n.startswith("tyre_lf") or n.startswith("rim_lf"):
        buckets["Wheel_LF"].append(o)
    elif n.startswith("tyre_rf") or n.startswith("rim_rf"):
        buckets["Wheel_RF"].append(o)
    elif n.startswith("tyre_lr") or n.startswith("rim_lr"):
        buckets["Wheel_LR"].append(o)
    elif n.startswith("tyre_rr") or n.startswith("rim_rr"):
        buckets["Wheel_RR"].append(o)
    elif "engine" in n:
        buckets["Engine"].append(o)
    elif any(k in n for k in ("seat", "dash", "floor", "console", "centerstack", "steering", "carpet", "gauge")):
        buckets["Interior"].append(o)
    elif any(k in n for k in ("speaker", "seatbelt")):
        buckets["DELETE"].append(o)
    else:
        buckets["Body"].append(o)

for o in buckets["DELETE"]:
    if o.name in bpy.data.objects:
        bpy.data.objects.remove(o, do_unlink=True)


def join_static(objs, name, origin_mode="geometry"):
    objs = [o for o in objs if o.name in bpy.data.objects]
    if not objs:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.hide_set(False)
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
        o.select_set(True)
    joined = join_selected(name)
    bpy.ops.object.select_all(action="DESELECT")
    joined.select_set(True)
    bpy.context.view_layer.objects.active = joined
    if origin_mode == "geometry":
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    mw = joined.matrix_world.copy()
    joined.parent = root
    joined.matrix_world = mw
    print(f"STATIC {name} faces={len(joined.data.polygons)} origin={tuple(round(v, 3) for v in joined.matrix_world.translation)}")
    return joined


for wn in ("Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR"):
    join_static(buckets[wn], wn)

join_static(buckets["Body"], "Body")
join_static(buckets["Engine"], "Engine")
join_static(buckets["Interior"], "Interior")

# 4) Delete ALL leftover empties / native junk — only keep mesh children of root
keep_meshes = {
    "Body", "Engine", "Interior", "Hood", "Door_L", "Door_R", "Trunk",
    "Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR",
}
for o in list(root.children):
    if o.type != "MESH" or o.name not in keep_meshes:
        bpy.data.objects.remove(o, do_unlink=True)

for o in list(bpy.data.objects):
    if o == root:
        continue
    if o.name in keep_meshes:
        continue
    if o.parent and o.parent.name in keep_meshes:
        continue
    if o.parent == root and o.name in keep_meshes:
        continue
    if "GMC_TYPHOON_NATIVE" in o.name or o.type == "EMPTY" or (
        o.type == "MESH" and o.name not in keep_meshes and o.parent != root
    ):
        if o.name in keep_meshes:
            continue
        try:
            bpy.data.objects.remove(o, do_unlink=True)
        except ReferenceError:
            pass

root.name = "GMC_Typhoon"

# Ground (min Z = 0)
mins_z = 1e9
for o in root.children:
    if o.type != "MESH":
        continue
    for corner in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(corner)
        mins_z = min(mins_z, w.z)
root.location.z -= mins_z

print("=== FINAL ===")
for o in sorted(root.children, key=lambda x: x.name):
    print(
        o.name,
        "pivot",
        tuple(round(v, 3) for v in o.matrix_world.translation),
        "faces",
        len(o.data.polygons) if o.type == "MESH" else 0,
    )

bpy.ops.object.select_all(action="DESELECT")
root.select_set(True)
for o in root.children:
    o.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.gltf(
    filepath=str(OUT),
    use_selection=True,
    export_format="GLB",
    export_apply=False,
    export_texcoords=True,
    export_normals=True,
    export_materials="EXPORT",
    export_yup=True,
    export_extras=False,
)
print("EXPORTED", OUT, OUT.stat().st_size)
OUT_FINAL.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(OUT, OUT_FINAL)
print("COPIED", OUT_FINAL)
