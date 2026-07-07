#!/usr/bin/env python3
"""Add LocalizedUILabel to main-menu Button prefab instances in Canvas.prefab."""

import random
import re
from pathlib import Path

CANVAS = Path(__file__).resolve().parents[1] / "Assets/UI/Canvas.prefab"
BUTTON_GUID = "4602c755b9f9b8a4fb2045a0eb9e23e6"
TEXT_TMP_GO = 9217095739993712761
LOC_GUID = "dc2fe8fa75870084a8256222b327d96b"

BUTTON_KEYS = {
    "continue": "ui.menu.continue",
    "new game": "ui.menu.new_game",
    "settings": "ui.common.settings",
    "quit game": "ui.common.quit",
    "quit": "ui.common.quit",
    "credits": "ui.menu.credits",
}


def new_id():
    return random.randint(880300001, 880399999)


def norm(s: str) -> str:
    return re.sub(r"\s+", " ", s.strip().lower())


def extract_button_label(mod_block: str) -> str | None:
    for prop in ("m_text", "m_Name"):
        m = re.search(
            rf"target: {{fileID: (?:2445310951551854071|3748187240687083385), guid: {BUTTON_GUID}, type: 3}}\n"
            rf"      propertyPath: {prop}\n"
            r"      value: (.+)",
            mod_block,
        )
        if m:
            return m.group(1).strip()
    return None


def instance_has_loc(mod_block: str) -> bool:
    m = re.search(r"m_AddedComponents:\n((?:    - .*\n)+)", mod_block)
    return bool(m and "9217095739993712761" in m.group(1))


def wire_instance_block(full_block: str, instance_id: str) -> tuple[str, bool]:
    mod_m = re.search(r"  m_Modification:\n(.*?)\n  m_SourcePrefab:", full_block, re.S)
    if not mod_m:
        return full_block, False
    mod_block = mod_m.group(1)

    if instance_has_loc(mod_block):
        return full_block, False

    label = extract_button_label(mod_block)
    if not label:
        return full_block, False

    key = BUTTON_KEYS.get(norm(label))
    if not key:
        print(f"  skip instance {instance_id}: unknown label {label!r}")
        return full_block, False

    go_id = new_id()
    loc_id = new_id()

    new_mod = mod_block.replace(
        "    m_AddedComponents: []",
        f"""    m_AddedComponents:
    - targetCorrespondingSourceObject: {{fileID: {TEXT_TMP_GO}, guid: {BUTTON_GUID}, type: 3}}
      insertIndex: -1
      addedObject: {{fileID: {loc_id}}}""",
        1,
    )

    blocks = f"""--- !u!1 &{go_id} stripped
GameObject:
  m_CorrespondingSourceObject: {{fileID: {TEXT_TMP_GO}, guid: {BUTTON_GUID}, type: 3}}
  m_PrefabInstance: {{fileID: {instance_id}}}
  m_PrefabAsset: {{fileID: 0}}
--- !u!114 &{loc_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go_id}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {LOC_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
  entryKey: {key}
  formatArgs: []
"""
    updated = full_block.replace(mod_block, new_mod, 1) + blocks
    print(f"  wired {label!r} -> {key} (instance {instance_id})")
    return updated, True


def main():
    text = CANVAS.read_text(encoding="utf-8")
    wired = 0

    chunks = re.split(r"(?=--- !u!1001 &)", text)
    out = []
    pattern = rf"^--- !u!1001 &(\d+)\nPrefabInstance:.*?  m_SourcePrefab: {{fileID: 100100000, guid: {BUTTON_GUID}, type: 3}}\n"

    for chunk in chunks:
        m = re.match(pattern, chunk, re.S)
        if not m:
            out.append(chunk)
            continue
        instance_id = m.group(1)
        updated, added = wire_instance_block(chunk, instance_id)
        out.append(updated)
        if added:
            wired += 1

    CANVAS.write_text("".join(out), encoding="utf-8")
    print(f"Wired {wired} menu button(s) in Canvas.prefab")


if __name__ == "__main__":
    main()
