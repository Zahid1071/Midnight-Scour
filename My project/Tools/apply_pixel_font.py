#!/usr/bin/env python3
"""Replace Liberation Sans with Pixelify Sans in UI prefabs and add UIFontBootstrap to Canvas roots."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "Assets"

LIBERATION = "8f586378b4e144a9851e7b34d9b748ee"
PIXELIFY = "fca6355ee4a564c4ca900e2eeb4a9ad6"
BOOTSTRAP_GUID = "e8d5f13a6b7c4d9e0f1a2b3c4d5e6f7a"

FONT_ASSET = f"{{fileID: 11400000, guid: {PIXELIFY}, type: 2}}"
FONT_MATERIAL = f"{{fileID: 1517768226830932575, guid: {PIXELIFY}, type: 2}}"

PREFAB_DIRS = [
    ROOT / "Prefabs" / "UI",
    ROOT / "UI",
    ROOT / "Scripts" / "UI",
]

CANVAS_BOOTSTRAP = {
    ROOT / "Prefabs" / "UI" / "_UI.prefab": {
        "game_object_id": "446520866954302658",
        "raycaster_id": "7365883517756996203",
        "bootstrap_id": "8804000011140001",
    },
    ROOT / "UI" / "Canvas.prefab": {
        "game_object_id": "2158040579497238262",
        "raycaster_id": "1439312692349282715",
        "bootstrap_id": "8804000021140001",
    },
}


def replace_font_refs(text: str) -> tuple[str, int]:
    count = text.count(LIBERATION)
    if not count:
        return text, 0

    text = text.replace(
        f"{{fileID: 11400000, guid: {LIBERATION}, type: 2}}",
        FONT_ASSET,
    )
    text = text.replace(
        f"{{fileID: 2180264, guid: {LIBERATION}, type: 2}}",
        FONT_MATERIAL,
    )
    return text, count


def add_bootstrap(text: str, cfg: dict) -> tuple[str, bool]:
    if BOOTSTRAP_GUID in text:
        return text, False

    go_id = cfg["game_object_id"]
    bootstrap_id = cfg["bootstrap_id"]
    raycaster_id = cfg["raycaster_id"]

    if f"component: {{fileID: {bootstrap_id}}}" in text:
        return text, False

    go_marker = f"--- !u!1 &{go_id}\nGameObject:"
    go_start = text.find(go_marker)
    if go_start < 0:
        print(f"  WARN: Canvas GameObject {go_id} not found")
        return text, False

    comp_marker = f"  - component: {{fileID: {raycaster_id}}}\n"
    comp_pos = text.find(comp_marker, go_start)
    if comp_pos < 0:
        print(f"  WARN: Raycaster component {raycaster_id} not found on Canvas")
        return text, False

    insert_comp = comp_pos + len(comp_marker)
    text = (
        text[:insert_comp]
        + f"  - component: {{fileID: {bootstrap_id}}}\n"
        + text[insert_comp:]
    )

    raycaster_block = f"--- !u!114 &{raycaster_id}\n"
    ray_pos = text.find(raycaster_block)
    if ray_pos < 0:
        print(f"  WARN: Raycaster block {raycaster_id} not found")
        return text, False

    next_obj = text.find("\n--- !u!", ray_pos + len(raycaster_block))
    if next_obj < 0:
        print(f"  WARN: Could not locate end of raycaster block {raycaster_id}")
        return text, False

    bootstrap_yaml = f"""--- !u!114 &{bootstrap_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go_id}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {BOOTSTRAP_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
  pixelFont: {FONT_ASSET}
  applyOnAwake: 1
  applyOnEnable: 1
"""
    text = text[:next_obj] + bootstrap_yaml + text[next_obj:]
    return text, True


def main() -> None:
    total_replacements = 0
    prefab_count = 0

    for directory in PREFAB_DIRS:
        if not directory.exists():
            continue
        for path in sorted(directory.rglob("*.prefab")):
            original = path.read_text(encoding="utf-8")
            updated, count = replace_font_refs(original)
            if count:
                prefab_count += 1
                total_replacements += count
                path.write_text(updated, encoding="utf-8")
                print(f"fonts: {path.relative_to(ROOT.parent)} ({count} refs)")

    for path, cfg in CANVAS_BOOTSTRAP.items():
        if not path.exists():
            print(f"WARN: missing {path}")
            continue
        original = path.read_text(encoding="utf-8")
        updated, added = add_bootstrap(original, cfg)
        if added:
            path.write_text(updated, encoding="utf-8")
            print(f"bootstrap: {path.relative_to(ROOT.parent)}")

    print(f"Done. Updated {prefab_count} prefab(s), {total_replacements} Liberation reference(s).")


if __name__ == "__main__":
    main()
