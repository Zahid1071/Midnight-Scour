#!/usr/bin/env python3
"""Point all TMP font/material refs at PixelifySans-VariableFont_wght SDF."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "Assets"

PIXELIFY = "fca6355ee4a564c4ca900e2eeb4a9ad6"
PIXELIFY_MAT = "1517768226830932575"

FONT_ASSET = f"{{fileID: 11400000, guid: {PIXELIFY}, type: 2}}"
FONT_MAT = f"{{fileID: {PIXELIFY_MAT}, guid: {PIXELIFY}, type: 2}}"

# Dead / wrong font assets that should become Pixelify.
REPLACE_FONT_GUIDS = {
    "c682e243242144fca90f6b55861700e1",  # old conflicted Pixelify meta guid
    "9a791362b4f64cb45ae0f10356149ee4",
    "2e498d1c8094910479dc3e1b768306a4",  # Liberation fallback
    "8f586378b4e144a9851e7b34d9b748ee",  # Liberation
    "1191fedcc45f92d449c78de134d7c6b9",  # Cubic_11
    "cdcf5e30d7a8ec249a153f3d2fc4d3e3",  # DotGothic primary refs
}

SCAN_DIRS = [
    ROOT / "Prefabs",
    ROOT / "UI",
    ROOT / "Scripts" / "UI",
    ROOT / "Scenes",
]

TMP_SETTINGS = ROOT / "TextMesh Pro" / "Resources" / "TMP Settings.asset"


def patch_text(content: str) -> tuple[str, int]:
    changes = 0

    content2, n = re.subn(
        r"m_fontAsset: \{fileID: 0\}",
        f"m_fontAsset: {FONT_ASSET}",
        content,
    )
    changes += n
    content = content2

    for guid in REPLACE_FONT_GUIDS:
        pattern = rf"m_fontAsset: {{fileID: 11400000, guid: {guid}, type: 2}}"
        content, n = re.subn(pattern, f"m_fontAsset: {FONT_ASSET}", content)
        changes += n

        pattern = rf"objectReference: {{fileID: 11400000, guid: {guid}, type: 2}}"
        content, n = re.subn(pattern, f"objectReference: {FONT_ASSET}", content)
        changes += n

    # Broken Pixelify material sub-id from an older SDF rebuild.
    content, n = re.subn(
        rf"m_sharedMaterial: {{fileID: 7620476336498253322, guid: {PIXELIFY}, type: 2}}",
        f"m_sharedMaterial: {FONT_MAT}",
        content,
    )
    changes += n

    content, n = re.subn(
        rf"objectReference: {{fileID: 7620476336498253322, guid: {PIXELIFY}, type: 2}}",
        f"objectReference: {FONT_MAT}",
        content,
    )
    changes += n

    for guid in REPLACE_FONT_GUIDS:
        pattern = rf"m_sharedMaterial: {{fileID: -?\d+, guid: {guid}, type: 2}}"
        content, n = re.subn(pattern, f"m_sharedMaterial: {FONT_MAT}", content)
        changes += n

        pattern = rf"objectReference: {{fileID: -?\d+, guid: {guid}, type: 2}}"
        content, n = re.subn(pattern, f"objectReference: {FONT_MAT}", content)
        changes += n

    # Prefab override blocks for sharedMaterial (any stale guid on Pixelify material id)
    for guid in REPLACE_FONT_GUIDS | {"7620476336498253322"}:
        if guid == "7620476336498253322":
            guid_pattern = PIXELIFY
        else:
            guid_pattern = guid
        pattern = (
            rf"(propertyPath: m_sharedMaterial\n\s+value: \n\s+objectReference: )"
            rf"{{fileID: \d+, guid: {guid_pattern}, type: 2}}"
        )
        content, n = re.subn(pattern, rf"\1{FONT_MAT}", content)
        changes += n

    # Liberation default material id
    content, n = re.subn(
        r"m_sharedMaterial: \{fileID: 2180264, guid: 8f586378b4e144a9851e7b34d9b748ee, type: 2\}",
        f"m_sharedMaterial: {FONT_MAT}",
        content,
    )
    changes += n

    return content, changes


def patch_tmp_settings(content: str) -> tuple[str, bool]:
    new = re.sub(
        r"m_defaultFontAsset: \{fileID: 11400000, guid: [0-9a-f]+, type: 2\}",
        f"m_defaultFontAsset: {FONT_ASSET}",
        content,
        count=1,
    )
    return new, new != content


def main() -> None:
    total = 0
    files = 0

    for directory in SCAN_DIRS:
        if not directory.exists():
            continue
        for path in sorted(directory.rglob("*")):
            if path.suffix not in {".prefab", ".unity"}:
                continue
            original = path.read_text(encoding="utf-8")
            updated, count = patch_text(original)
            if count:
                path.write_text(updated, encoding="utf-8")
                total += count
                files += 1
                print(f"patched {path.relative_to(ROOT.parent)} ({count})")

    if TMP_SETTINGS.exists():
        original = TMP_SETTINGS.read_text(encoding="utf-8")
        updated, changed = patch_tmp_settings(original)
        if changed:
            TMP_SETTINGS.write_text(updated, encoding="utf-8")
            print("patched TMP Settings default font")

    print(f"Done. {files} file(s), {total} replacement(s).")


if __name__ == "__main__":
    main()
