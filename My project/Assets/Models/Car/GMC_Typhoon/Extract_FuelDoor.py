"""
Find side-facing fuel door: thin in X, ~20x25cm, left rear quarter, mid height.
Restore intact car + correct FuelDoor only.
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

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(GLB))
root = bpy.data.objects.get("GMC_TYPHOON_NATIVE::root")
assert root is not None


def delete_by_needles(*needles):
    n = 0
    for o in list(bpy.data.objects):
        if any(k in o.name.lower() for k in needles):
            bpy.data.objects.remove(o, do_unlink=True)
            n += 1
    print("deleted", n)


def delete_meshes_by_material(*needles):
    n = 0
    for o in list(bpy.data.objects):
        if o.type != "MESH":
            continue
        mats = [s.material.name.lower() for s in o.material_slots if s.material]
        if any(any(k in m for k in needles) for m in mats):
            bpy.data.objects.remove(o, do_unlink=True)
            n += 1
    print("deleted_by_material", n)


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
    assert meshes, out_name
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
    print(f"HINGED {out_name} faces={len(joined.data.polygons)}")
    return joined


delete_by_needles(
    "controlarm", "caliper", "gmc_rotor", "rotor_", "rotorlr", "rotorrr",
    "undercarriage", "wiper",
)
delete_meshes_by_material("glass", "blackhole")

make_hinged_part("GMC_TYPHOON_NATIVE::boneHood", "Hood")
make_hinged_part("GMC_TYPHOON_NATIVE::boneDoorLF", "Door_L")
make_hinged_part("GMC_TYPHOON_NATIVE::boneDoorRF", "Door_R")
make_hinged_part("GMC_TYPHOON_NATIVE::root_boneTrunk", "Trunk")

HINGED = {"Hood", "Door_L", "Door_R", "Trunk", "FuelDoor"}
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


def join_static(objs, name):
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
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    mw = joined.matrix_world.copy()
    joined.parent = root
    joined.matrix_world = mw
    print(f"STATIC {name} faces={len(joined.data.polygons)}")
    return joined


for wn in ("Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR"):
    join_static(buckets[wn], wn)
body = join_static(buckets["Body"], "Body")
join_static(buckets["Engine"], "Engine")
join_static(buckets["Interior"], "Interior")
assert body is not None

# Separate Body into loose islands and score for fuel-door shape
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="LOOSE")
bpy.ops.object.mode_set(mode="OBJECT")

parts = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Body")]
print("loose parts", len(parts))

best = None
best_score = -1e9
for o in parts:
    faces = len(o.data.polygons)
    if faces < 30 or faces > 900:
        continue
    mw = o.matrix_world
    cs = [mw @ mathutils.Vector(c) for c in o.bound_box]
    cx = sum(c.x for c in cs) / 8
    cy = sum(c.y for c in cs) / 8
    cz = sum(c.z for c in cs) / 8
    sx = max(c.x for c in cs) - min(c.x for c in cs)
    sy = max(c.y for c in cs) - min(c.y for c in cs)
    sz = max(c.z for c in cs) - min(c.z for c in cs)
    # Side-facing fuel door: thin in X, panel in YZ (Forza: Z=up, Y=longitudinal)
    thin_x = sx < 0.08
    panel_yz = 0.12 < sy < 0.40 and 0.12 < sz < 0.40
    left = cx < -0.55
    height_ok = 0.45 < cz < 1.05
    # rearward but not at bumper extreme — above rear wheel band
    rear_ok = 0.6 < abs(cy) < 1.7
    score = 0
    if thin_x:
        score += 8
    if panel_yz:
        score += 8
    if left:
        score += 3
    if height_ok:
        score += 3
    if rear_ok:
        score += 3
    if 80 <= faces <= 500:
        score += 2
    if score >= 14:
        print(f"CAND {o.name} faces={faces} c=({cx:.2f},{cy:.2f},{cz:.2f}) s=({sx:.3f},{sy:.3f},{sz:.3f}) score={score}")
    if score > best_score:
        best_score = score
        best = o

if best is None or best_score < 14:
    # No separate island — fuel door is welded. Rejoin body and skip FuelDoor.
    print("NO side-facing fuel island — rejoining Body, no FuelDoor")
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    mw = body.matrix_world.copy()
    body.parent = root
    body.matrix_world = mw
    fuel = None
else:
    fuel = best
    fuel.name = "FuelDoor"
    print(f"PICKED FuelDoor faces={len(fuel.data.polygons)} score={best_score}")
    bpy.ops.object.select_all(action="DESELECT")
    fuel.select_set(True)
    bpy.context.view_layer.objects.active = fuel
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    mw = fuel.matrix_world.copy()
    fuel.parent = root
    fuel.matrix_world = mw
    # Rejoin the rest into Body
    rest = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Body")]
    bpy.ops.object.select_all(action="DESELECT")
    for o in rest:
        o.select_set(True)
    bpy.context.view_layer.objects.active = rest[0]
    if len(rest) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    mw = body.matrix_world.copy()
    body.parent = root
    body.matrix_world = mw

keep = {
    "Body", "Engine", "Interior", "Hood", "Door_L", "Door_R", "Trunk",
    "Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR",
}
if fuel is not None:
    keep.add("FuelDoor")

for o in list(root.children):
    if o.type != "MESH" or o.name not in keep:
        bpy.data.objects.remove(o, do_unlink=True)
for o in list(bpy.data.objects):
    if o != root and (o.type != "MESH" or o.name not in keep):
        bpy.data.objects.remove(o, do_unlink=True)

meshes = [o for o in root.children if o.type == "MESH"]
min_z = min((o.matrix_world @ mathutils.Vector(c)).z for o in meshes for c in o.bound_box)
root.location.z -= min_z

print("=== FINAL ===")
for o in sorted(root.children, key=lambda x: x.name):
    print(o.name, "faces", len(o.data.polygons))
    if o.name == "FuelDoor":
        cs = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
        print("  center", tuple(round(sum(c[i] for c in cs)/8, 3) for i in range(3)))
        print("  size", tuple(round(max(c[i] for c in cs)-min(c[i] for c in cs), 3) for i in range(3)))

bpy.ops.object.select_all(action="DESELECT")
root.select_set(True)
for c in root.children:
    c.select_set(True)
bpy.context.view_layer.objects.active = root
if OUT.exists():
    OUT.unlink()
bpy.ops.export_scene.gltf(filepath=str(OUT), export_format="GLB", use_selection=True, export_apply=False)
print("EXPORTED", OUT, OUT.stat().st_size)
shutil.copy2(OUT, OUT_FINAL)
print("COPIED", OUT_FINAL)
if fuel is None:
    print("NOTE: no separable fuel door island in this model")
