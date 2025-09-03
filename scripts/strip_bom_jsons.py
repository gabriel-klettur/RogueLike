#!/usr/bin/env python3
"""
Strip UTF-8 BOM from all .json files under data/ and schemas/.
Keeps file content and formatting intact by removing only the first 3 bytes if present.
"""
from __future__ import annotations
import sys
from pathlib import Path

BOM = b"\xef\xbb\xbf"


def strip_bom_in_file(path: Path) -> bool:
    try:
        data = path.read_bytes()
    except Exception as exc:
        print(f"[SKIP] {path} (read error: {exc})")
        return False
    if data.startswith(BOM):
        try:
            path.write_bytes(data[len(BOM):])
            print(f"[FIX ] {path} (BOM removed)")
            return True
        except Exception as exc:
            print(f"[FAIL] {path} (write error: {exc})")
            return False
    else:
        # No BOM; nothing to change
        return False


def main(root: Path) -> int:
    targets = [root / "data", root / "schemas"]
    total = 0
    fixed = 0
    for base in targets:
        if not base.exists():
            continue
        for p in base.rglob("*.json"):
            total += 1
            if strip_bom_in_file(p):
                fixed += 1
    print(f"\nSummary: scanned={total}, fixed={fixed}")
    return 0


if __name__ == "__main__":
    project_root = Path(__file__).resolve().parents[1]
    sys.exit(main(project_root))
