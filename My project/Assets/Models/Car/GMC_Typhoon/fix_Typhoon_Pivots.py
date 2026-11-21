"""
Fix Typhoon: keep original hood/door/trunk bone pivots, keep seats, delete suspension.
Run in Blender: Scripting workspace → Open this file → Run Script
Or: blender --background --python this_file.py  (with the GLB path below)

Exports: GMC_Typhoon_GameReady.glb next to this script.
"""
import bpy
import mathutils
from pathlib import Path
from collections import defaultdict

GLB = Path(r"C:\Users\babun\Downloads\gmc-typhoon-1992\source\GMC_Typhoon_1992_FH6_Sketchfab.glb")
OUT = Path(r"C:\Users\babun\Downloads\GMC_Typhoon_GameReady.glb")
OUT_FINAL = Path(r"C:\Users\babun\OneDrive\Desktop\Midnight Scour\Midnight Scour\My project\Assets\Models\Car\GMC_Typhoon\GMC_Typhoon_GameReady.glb")

# --- fresh import ---
# Remove previous car objects only
for o in list(bpy.data.objects):
    n = o.name
    if (
        n.startswith("GMC")
        or n.startswith("Typhoon")
        or "[" in n
        or n in {
            "Body", "Engine", "Interior", "Hood", "Hood_Hinge", "Hood_Mesh",
            "Door_L", "Door_L_Hinge", "Door_L_Mesh", "Door_R", "Door_R_Hinge", "Door_R_Mesh",
            "Trunk", "Trunk_Hinge", "Trunk_Mesh",
            "Wheel_LF", "Wheel_LF_Mesh", "Wheel_RF", "Wheel_RF_Mesh",
            "Wheel_LR", "Wheel_LR_Mesh", "Wheel_RR", "Wheel_RR_Mesh",
        }
        or n.startswith(("fender", "door", "hood", "body", "tyre", "rim", "engine", "trunk", "glass",
                         "bumper", "wiper", "dash", "seat", "floor", "grille", "skirt", "headlight",
                         "taillight", "control", "exhaust", "Center", "gauge", "speaker", "pillar",
                         "steering", "undercarriage", "wingMirror"))
    ):
        bpy.data.objects.remove(o, do_unlink=True)

bpy.ops.import_scene.gltf(filepath=str(GLB))

NATIVE_ROOT = "GMC_TYPHOON_NATIVE::root"
root = bpy.data.objects.get(NATIVE_ROOT)
assert root, "Native root missing after import"


def mesh_descendants(obj):
    return [c for c in obj.children_recursive if c.type == "MESH"]


def join_meshes_as_child(parent_empty, mesh_name):
    """Join all mesh descendants of parent_empty into one mesh, parented to parent_empty (pivot preserved)."""
    meshes = mesh_descendants(parent_empty)
    meshes = list({m.name: m for m in meshes}.values())
    if not meshes:
        print("NO MESH under", parent_empty.name)
        return None

    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.hide_set(False)
        mw = m.matrix_world.copy()
        m.parent = None
        m.matrix_world = mw
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = mesh_name
    mw = joined.matrix_world.copy()
    joined.parent = parent_empty
    joined.matrix_world = mw
    print(f"JOINED {mesh_name} -> {parent_empty.name}  faces={len(joined.data.polygons)}  "
          f"pivot={tuple(round(v, 3) for v in parent_empty.matrix_world.translation)}")
    return joined


def delete_matching(*needles):
    doomed = []
    for o in list(bpy.data.objects):
        n = o.name.lower()
        if any(k in n for k in needles):
            doomed.append(o)
    for o in doomed:
        bpy.data.objects.remove(o, do_unlink=True)
    print("deleted", len(doomed), needles)


# --- delete suspension / brakes (NOT seats) ---
delete_matching("controlarm", "caliper", "gmc_rotor", "rotor_", "rotorlr", "rotorrr")

# --- hinged parts: keep ORIGINAL bone empties as pivots ---
hood_bone = bpy.data.objects["GMC_TYPHOON_NATIVE::boneHood"]
door_l_bone = bpy.data.objects["GMC_TYPHOON_NATIVE::boneDoorLF"]
door_r_bone = bpy.data.objects["GMC_TYPHOON_NATIVE::boneDoorRF"]
trunk_root = bpy.data.objects["GMC_TYPHOON_NATIVE::root_boneTrunk"]

join_meshes_as_child(hood_bone, "Hood_Mesh")
join_meshes_as_child(door_l_bone, "Door_L_Mesh")
join_meshes_as_child(door_r_bone, "Door_R_Mesh")
join_meshes_as_child(trunk_root, "Trunk_Mesh")

# Rename bone chain to clean names (transforms untouched)
bpy.data.objects["GMC_TYPHOON_NATIVE::root_boneHood"].name = "Hood"
hood_bone.name = "Hood_Hinge"
bpy.data.objects["GMC_TYPHOON_NATIVE::root_boneDoorLF"].name = "Door_L"
door_l_bone.name = "Door_L_Hinge"
bpy.data.objects["GMC_TYPHOON_NATIVE::root_boneDoorRF"].name = "Door_R"
door_r_bone.name = "Door_R_Hinge"
trunk_root.name = "Trunk"
if "GMC_TYPHOON_NATIVE::boneTrunk" in bpy.data.objects:
    bpy.data.objects["GMC_TYPHOON_NATIVE::boneTrunk"].name = "Trunk_Hinge"


def under_named_ancestors(o, names):
    p = o
    while p:
        if p.name in names:
            return True
        p = p.parent
    return False


HINGE_FOLDERS = {"Hood", "Hood_Hinge", "Door_L", "Door_L_Hinge", "Door_R", "Door_R_Hinge", "Trunk", "Trunk_Hinge"}

remaining = [
    o for o in bpy.data.objects
    if o.type == "MESH" and not under_named_ancestors(o, HINGE_FOLDERS)
]

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
        buckets["Interior"].append(o)  # KEEP SEATS
    elif any(k in n for k in ("wiper", "undercarriage", "speaker", "seatbelt")):
        buckets["DELETE"].append(o)
    else:
        buckets["Body"].append(o)

for o in buckets["DELETE"]:
    if o.name in bpy.data.objects:
        bpy.data.objects.remove(o, do_unlink=True)
print("deleted extras", len(buckets["DELETE"]))


def join_list(objs, name):
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
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = name
    return joined


def make_wheel(name, objs):
    joined = join_list(objs, name + "_Mesh")
    if joined is None:
        return None
    mins = mathutils.Vector((1e9, 1e9, 1e9))
    maxs = mathutils.Vector((-1e9, -1e9, -1e9))
    for corner in joined.bound_box:
        w = joined.matrix_world @ mathutils.Vector(corner)
        mins = mathutils.Vector((min(mins.x, w.x), min(mins.y, w.y), min(mins.z, w.z)))
        maxs = mathutils.Vector((max(maxs.x, w.x), max(maxs.y, w.y), max(maxs.z, w.z)))
    center = (mins + maxs) * 0.5
    empty = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(empty)
    empty.empty_display_size = 0.15
    empty.matrix_world = mathutils.Matrix.Translation(center)
    mw = joined.matrix_world.copy()
    joined.parent = empty
    joined.matrix_world = mw
    emw = empty.matrix_world.copy()
    empty.parent = root
    empty.matrix_world = emw
    print("WHEEL", name, tuple(round(v, 3) for v in center))
    return empty


for wn in ("Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR"):
    make_wheel(wn, buckets[wn])

for name, key in (("Body", "Body"), ("Engine", "Engine"), ("Interior", "Interior")):
    joined = join_list(buckets[key], name)
    if joined is None:
        continue
    mw = joined.matrix_world.copy()
    joined.parent = root
    joined.matrix_world = mw
    bpy.ops.object.select_all(action="DESELECT")
    joined.select_set(True)
    bpy.context.view_layer.objects.active = joined
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    print(name, "faces", len(joined.data.polygons))

# Light decimate on heavies only
for name in ("Body", "Engine", "Interior", "Trunk_Mesh"):
    o = bpy.data.objects.get(name)
    if not o or o.type != "MESH":
        continue
    f = len(o.data.polygons)
    if f < 18000:
        continue
    ratio = 0.55 if f > 40000 else 0.7
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    mod = o.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    bpy.ops.object.modifier_apply(modifier="Decimate")
    print("DECIM", name, f, "->", len(o.data.polygons))

# Strip leftover native empties (hubs, hashes) — NOT our named parts
keep = {
    "Body", "Engine", "Interior",
    "Hood", "Hood_Hinge", "Hood_Mesh",
    "Door_L", "Door_L_Hinge", "Door_L_Mesh",
    "Door_R", "Door_R_Hinge", "Door_R_Mesh",
    "Trunk", "Trunk_Hinge", "Trunk_Mesh",
    "Wheel_LF", "Wheel_LF_Mesh", "Wheel_RF", "Wheel_RF_Mesh",
    "Wheel_LR", "Wheel_LR_Mesh", "Wheel_RR", "Wheel_RR_Mesh",
    NATIVE_ROOT, "GMC_Typhoon",
}


def is_car_keep(o):
    if o.name in keep:
        return True
    p = o.parent
    while p:
        if p.name in keep or p.name == NATIVE_ROOT:
            return True
        p = p.parent
    return False


# Rename root first so we don't delete it with the native cleanup
root.name = "GMC_Typhoon"

# Strip leftover Forza empties / IK / spindles — keep only game parts
allowed_children = {
    "Body", "Engine", "Interior",
    "Hood", "Door_L", "Door_R", "Trunk",
    "Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR",
}
for o in list(root.children):
    if o.name not in allowed_children:
        bpy.data.objects.remove(o, do_unlink=True)

# Delete any leftover native empties nested under doors/hood (mirrors already in joined mesh)
for o in list(bpy.data.objects):
    if "GMC_TYPHOON_NATIVE" in o.name:
        bpy.data.objects.remove(o, do_unlink=True)

# Ground
mins_z = 1e9
for o in root.children_recursive:
    if o.type != "MESH":
        continue
    for corner in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(corner)
        mins_z = min(mins_z, w.z)
root.location.z -= mins_z
print("grounded", round(-mins_z, 3))

print("=== HIERARCHY ===")


def dump(obj, d=0):
    print("  " * d + f"{obj.name} [{obj.type}] {tuple(round(v, 3) for v in obj.matrix_world.translation)}")
    for c in obj.children:
        dump(c, d + 1)


dump(root)

# Export WITHOUT applying transforms — preserves pivots
bpy.ops.object.select_all(action="DESELECT")
root.select_set(True)
for o in root.children_recursive:
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
)
print("EXPORTED", OUT, OUT.stat().st_size)
OUT_FINAL.parent.mkdir(parents=True, exist_ok=True)
import shutil
shutil.copy2(OUT, OUT_FINAL)
print("COPIED", OUT_FINAL, OUT_FINAL.stat().st_size)
