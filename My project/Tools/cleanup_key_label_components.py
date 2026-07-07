#!/usr/bin/env python3
"""Remove broken per-TMP key components from _UI.prefab."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PREFAB = ROOT / "Assets" / "Prefabs" / "UI" / "_UI.prefab"

REMOVE_COMPONENT_LINES = [
    "  - component: {fileID: 880200001}\n",
    "  - component: {fileID: 880200002}\n",
    "  - component: {fileID: 880200003}\n",
    "  - component: {fileID: 880200004}\n",
]

BLOCK_STARTS = [
    "--- !u!114 &880200001\n",
    "--- !u!114 &880200002\n",
    "--- !u!114 &880200003\n",
    "--- !u!114 &880200004\n",
]


def remove_block(content: str, start_marker: str) -> str:
    start = content.find(start_marker)
    if start == -1:
        return content
    end = content.find("\n--- !u!", start + 1)
    if end == -1:
        return content[:start]
    return content[:start] + content[end + 1 :]


def main() -> None:
    content = PREFAB.read_text(encoding="utf-8")
    for line in REMOVE_COMPONENT_LINES:
        content = content.replace(line, "")
    for marker in BLOCK_STARTS:
        content = remove_block(content, marker)
    PREFAB.write_text(content, encoding="utf-8")
    print(f"Cleaned {PREFAB.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
