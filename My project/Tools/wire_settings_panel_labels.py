#!/usr/bin/env python3
"""Patch Settings Panel prefab: add LocalizedUILabel to static setting labels."""

import re
import random
from pathlib import Path

DEFAULT_PREFAB = Path(__file__).resolve().parents[1] / "Assets/Prefabs/UI/Settings  Panel.prefab"
LOC_GUID = "dc2fe8fa75870084a8256222b327d96b"
TMP_GUID = "f4688fdb7df04437aeb418b961361dc5"

SKIP_TEXT = {
    "<", ">", "choose value", "high", "option a", "enabled", "disabled",
    "1920x1080", "sound", "e", "g", "language",
}

ROW_KEYS = {
    "row_autosave": "ui.settings.auto_save",
    "row_resolution": "ui.settings.resolution",
    "row_quality": "ui.settings.quality",
    "row_vsync": "ui.settings.vsync",
    "row_brightness": "ui.settings.brightness",
    "row_full screen": "ui.settings.fullscreen",
    "row_mastervolume": "ui.settings.master_volume",
    "row_master volume": "ui.settings.master_volume",
    "row_music volume": "ui.settings.music_volume",
    "row_musicvolume": "ui.settings.music_volume",
    "row_sfx volume": "ui.settings.sfx_volume",
    "row_sfxvolume": "ui.settings.sfx_volume",
    "row_mousesensitivity": "ui.settings.mouse_sensitivity",
    "row_mouse sensitivity": "ui.settings.mouse_sensitivity",
}

BTN_KEYS = {
    "btn_display": "ui.settings.display",
    "btn_audio": "ui.settings.audio",
    "btn_controls": "ui.settings.controls",
    "btn_gameplay": "ui.settings.gameplay",
}

GO_KEYS = {
    "title": "ui.settings.title",
    "apply button": "ui.common.apply",
    "back button": "ui.common.back",
    "revert to default": "ui.common.default",
    "header": "ui.settings.general",
}

TEXT_KEYS = {
    "settings": "ui.settings.title",
    "display": "ui.settings.display",
    "audio": "ui.settings.audio",
    "controls": "ui.settings.controls",
    "gameplay": "ui.settings.gameplay",
    "general": "ui.settings.general",
    "resolution": "ui.settings.resolution",
    "quality": "ui.settings.quality",
    "brightness": "ui.settings.brightness",
    "fullscreen": "ui.settings.fullscreen",
    "display mode": "ui.settings.display_mode",
    "vsync": "ui.settings.vsync",
    "master volume": "ui.settings.master_volume",
    "music volume": "ui.settings.music_volume",
    "music sound": "ui.settings.music_volume",
    "sfx volume": "ui.settings.sfx_volume",
    "sfx sound": "ui.settings.sfx_volume",
    "mouse sensitivity": "ui.settings.mouse_sensitivity",
    "auto-save": "ui.settings.auto_save",
    "apply": "ui.common.apply",
    "back": "ui.common.back",
    "default": "ui.common.default",
    "defualt": "ui.common.default",
}


def new_id():
    return random.randint(880200001, 880299999)


def norm(s: str) -> str:
    return re.sub(r"\s+", " ", s.replace("'", "").replace('"', "").strip().lower())


def parse_gameobjects(text):
    names = {}
    components = {}
    for m in re.finditer(
        r"--- !u!1 &(\d+)\nGameObject:\n(?:.*?\n)*?  m_Name: (.+)\n(?:.*?\n)*?  m_Component:\n((?:  - component: \{fileID: \d+\}\n)+)",
        text,
    ):
        gid, name, comp_block = m.group(1), m.group(2).strip(), m.group(3)
        names[gid] = name
        components[gid] = re.findall(r"component: \{fileID: (\d+)\}", comp_block)
    return names, components


def parse_transforms(text):
    parent = {}
    rt_by_go = {}
    for m in re.finditer(
        r"--- !u!224 &(\d+)\nRectTransform:\n(?:.*?\n)*?  m_GameObject: \{fileID: (\d+)\}\n(?:.*?\n)*?  m_Father: \{fileID: (\d+)\}",
        text,
    ):
        rt, go, par = m.group(1), m.group(2), m.group(3)
        rt_by_go[go] = rt
        parent[rt] = par
    return parent, rt_by_go


def ancestor_names(go_id, names, parent, rt_by_go):
    chain = []
    rt = rt_by_go.get(go_id)
    seen = set()
    while rt and rt not in seen:
        seen.add(rt)
        par_rt = parent.get(rt, "0")
        if par_rt == "0":
            break
        parent_go = None
        for g, r in rt_by_go.items():
            if r == par_rt:
                parent_go = g
                break
        if parent_go is None:
            break
        chain.append(names.get(parent_go, ""))
        rt = par_rt
    return chain


def extract_m_text(block: str) -> str:
    m = re.search(r"  m_text: (.+)", block)
    if not m:
        return ""
    val = m.group(1)
    if val.startswith("'"):
        start = block.index("  m_text: ") + len("  m_text: ")
        i = start + 1
        out = []
        while i < len(block):
            c = block[i]
            if c == "'":
                if i + 1 < len(block) and block[i + 1] == "'":
                    out.append("'")
                    i += 2
                    continue
                break
            out.append(c)
            i += 1
        return "".join(out)
    return val.strip()


def resolve_key(go_id, text, names, parent, rt_by_go):
    t = norm(text)
    if not t or t in SKIP_TEXT or re.match(r"^\d+x\d+$", t):
        return None

    go_name = names.get(go_id, "").lower()
    if go_name in GO_KEYS:
        return GO_KEYS[go_name]

    chain = [n.lower() for n in ancestor_names(go_id, names, parent, rt_by_go)]
    for n in chain:
        if n in BTN_KEYS:
            return BTN_KEYS[n]
        nk = n.replace("_", " ")
        if nk in ROW_KEYS:
            return ROW_KEYS[nk]

    for n in chain:
        if "settingsselector" in n or n == "dropdown":
            return None

    return TEXT_KEYS.get(t)


def go_has_loc(text, go_id):
    for m in re.finditer(rf"--- !u!114 &(\d+)\nMonoBehaviour:(.*?)(?=\n--- !u!)", text, re.S):
        block = m.group(2)
        if f"m_GameObject: {{fileID: {go_id}}}" in block and LOC_GUID in block:
            return True
    return False


def wire_label(text, go_id, comps, entry_key, tmp_cid):
    if go_has_loc(text, go_id):
        return text, False

    lid = new_id()
    block = f"""--- !u!114 &{lid}
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
  entryKey: {entry_key}
  formatArgs: []
"""
    go_pat = rf"--- !u!1 &{go_id}\nGameObject:\n(?:.*?\n)*?  m_Component:\n((?:  - component: \{{fileID: \d+\}}\n)+)"
    m = re.search(go_pat, text)
    if not m:
        return text, False
    text = text.replace(m.group(0), m.group(0) + f"  - component: {{fileID: {lid}}}\n", 1)

    tmp_pat = rf"--- !u!114 &{tmp_cid}\nMonoBehaviour:\n(?:.*?\n)*?  m_maskOffset:.*?\n"
    tm = re.search(tmp_pat, text)
    if not tm:
        # minimal TMP block from language row - insert after m_text line
        tmp_pat2 = rf"(--- !u!114 &{tmp_cid}\nMonoBehaviour:\n(?:.*?\n)*?  m_VerticalAlignment: \d+\n)"
        tm = re.search(tmp_pat2, text)
        if not tm:
            return text, False
    text = text.replace(tm.group(0), tm.group(0) + block, 1)
    return text, True


def main():
    import sys

    prefab = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PREFAB
    if not prefab.is_absolute():
        prefab = Path(__file__).resolve().parents[1] / prefab
    print(f"Wiring labels in {prefab}")
    text = prefab.read_text(encoding="utf-8")
    names, components = parse_gameobjects(text)
    parent, rt_by_go = parse_transforms(text)

    wired = 0
    seen_go = set()

    for m in re.finditer(r"--- !u!114 &(\d+)\nMonoBehaviour:(.*?)(?=\n--- !u!|\Z)", text, re.S):
        cid, block = m.group(1), m.group(2)
        if TMP_GUID not in block:
            continue
        gm = re.search(r"  m_GameObject: \{fileID: (\d+)\}", block)
        if not gm:
            continue
        go_id = gm.group(1)
        if go_id in seen_go:
            continue
        raw = extract_m_text(block)
        key = resolve_key(go_id, raw, names, parent, rt_by_go)
        if not key:
            continue
        text, added = wire_label(text, go_id, components.get(go_id, []), key, cid)
        if added:
            wired += 1
            seen_go.add(go_id)
            names, components = parse_gameobjects(text)

    prefab.write_text(text, encoding="utf-8")
    print(f"Wired {wired} LocalizedUILabel components.")


if __name__ == "__main__":
    main()
