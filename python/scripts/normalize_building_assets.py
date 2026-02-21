import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_FILE = ROOT / "data" / "buildings" / "buildings_data.json"


def normalize_asset_path(p: str) -> str:
    try:
        if not p or not isinstance(p, str):
            return p
        q = p.replace("\\", "/")
        while "//" in q:
            q = q.replace("//", "/")
        base, ext = os.path.splitext(q)
        if ext:
            q = f"{base}{ext.lower()}"
        return q
    except Exception:
        return p


def main():
    if not DATA_FILE.exists():
        print(f"File not found: {DATA_FILE}")
        raise SystemExit(1)

    # Use utf-8-sig to gracefully handle files that include a UTF-8 BOM
    with DATA_FILE.open("r", encoding="utf-8-sig") as rf:
        data = json.load(rf)

    modified = 0
    missing = 0
    total = 0

    if not isinstance(data, list):
        print("buildings_data.json is not a list; aborting.")
        raise SystemExit(2)

    for entry in data:
        if not isinstance(entry, dict):
            continue
        total += 1
        assets = entry.get("assets") or {}
        if isinstance(assets, dict):
            idle = assets.get("idle")
            if idle:
                norm = normalize_asset_path(idle)
                if norm != idle:
                    assets["idle"] = norm
                    entry["assets"] = assets
                    modified += 1
            else:
                missing += 1
        else:
            missing += 1

    # Write back only if modified or for deterministic formatting
    with DATA_FILE.open("w", encoding="utf-8") as wf:
        json.dump(data, wf, indent=4)

    print(f"Normalized: {modified} entries | Missing assets.idle: {missing} | Total: {total}")


if __name__ == "__main__":
    main()
