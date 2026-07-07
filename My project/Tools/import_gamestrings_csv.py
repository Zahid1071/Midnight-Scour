#!/usr/bin/env python3
"""Sync GameStrings_Import_Fixed.csv into Shared Data + all locale .asset files."""

from __future__ import annotations

import csv
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOC = ROOT / "Assets" / "Localization"
CSV_PATH = LOC / "GameStrings_Import_Fixed.csv"
SHARED_PATH = LOC / "GameStrings Shared Data.asset"

COLUMN_TO_LOCALE = {
    "English(en)": "en",
    "Chinese (Simplified)(zh-Hans)": "zh-Hans",
    "French(fr)": "fr",
    "Japanese(ja)": "ja",
    "Polish(pl)": "pl",
    "Portuguese (Brazil)(pt-BR)": "pt-BR",
    "Russian(ru)": "ru",
    "Turkish(tr)": "tr",
    "Spanish(es-419)": "es-419",
}


def yaml_quote(value: str) -> str:
    if value == "":
        return '""'
    if re.fullmatch(r"[A-Za-z0-9 _./:;,'!?+\-()&]+", value):
        return value
    escaped = (
        value.replace("\\", "\\\\")
        .replace('"', '\\"')
        .replace("\n", "\\n")
        .replace("\r", "")
        .replace("\t", "\\t")
    )
    return f'"{escaped}"'


def render_entry(entry_id: str, localized: str) -> str:
    return (
        f"  - m_Id: {entry_id}\n"
        f"    m_Localized: {yaml_quote(localized)}\n"
        f"    m_Metadata:\n"
        f"      m_Items: []"
    )


def upsert_locale_entry(text: str, entry_id: str, localized: str) -> str:
    block = render_entry(entry_id, localized)
    pattern = (
        rf"  - m_Id: {re.escape(entry_id)}\n"
        rf"    m_Localized: .*?\n"
        rf"    m_Metadata:\n"
        rf"      m_Items: \[\]"
    )
    if re.search(pattern, text, flags=re.DOTALL):
        return re.sub(pattern, block, text, count=1, flags=re.DOTALL)

    insert_at = text.rfind("\n  references:")
    if insert_at == -1:
        raise RuntimeError("Could not find references section in locale asset.")
    return text[:insert_at] + "\n" + block + text[insert_at:]


def load_csv_rows() -> list[dict[str, str]]:
    with CSV_PATH.open(encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def sync_shared_data(rows: list[dict[str, str]]) -> None:
    text = SHARED_PATH.read_text(encoding="utf-8")
    existing_keys = set(re.findall(r"m_Key: (.+)", text))
    insert_pos = text.rfind("  m_Metadata:\n    m_Items: []\n  m_KeyGenerator:")

    new_blocks: list[str] = []
    for row in rows:
        key = row["Key"].strip()
        entry_id = row["Id"].strip()
        if key in existing_keys:
            continue
        new_blocks.append(
            f"  - m_Id: {entry_id}\n"
            f"    m_Key: {key}\n"
            f"    m_Metadata:\n"
            f"      m_Items: []"
        )
        existing_keys.add(key)

    if not new_blocks:
        return

    SHARED_PATH.write_text(
        text[:insert_pos] + "\n".join(new_blocks) + "\n" + text[insert_pos:],
        encoding="utf-8",
    )


def sync_locale_assets(rows: list[dict[str, str]]) -> None:
    for column, locale in COLUMN_TO_LOCALE.items():
        asset_path = LOC / f"GameStrings_{locale}.asset"
        text = asset_path.read_text(encoding="utf-8")
        for row in rows:
            text = upsert_locale_entry(text, row["Id"].strip(), row[column])
        asset_path.write_text(text, encoding="utf-8")
        print(f"Updated {asset_path.name}")


def main() -> None:
    rows = load_csv_rows()
    sync_shared_data(rows)
    sync_locale_assets(rows)
    print(f"Imported {len(rows)} CSV rows into all locales.")


if __name__ == "__main__":
    main()
