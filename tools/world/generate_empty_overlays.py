"""Generate floor_2-only overlay JSONs for zones currently lacking an overlay.

Reads unity/Valkur/Assets/StreamingAssets/Maps/zones_database.json,
finds entries with overlay=null, generates a 50x50 'floor_2' Ground layer
matching the schema of existing overlays (e.g. zone_150_50.overlay.json),
writes the new file to:
    - unity/Valkur/Assets/StreamingAssets/Maps/{zone}.overlay.json
and updates zones_database.json to point each entry to its new overlay file.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]  # repo root
DB_PATH = ROOT / "unity" / "Valkur" / "Assets" / "StreamingAssets" / "Maps" / "zones_database.json"
UNITY_MAPS = ROOT / "unity" / "Valkur" / "Assets" / "StreamingAssets" / "Maps"

ZONE_W = 50
ZONE_H = 50
DEFAULT_TILE = "floor_2"


def build_overlay() -> dict:
    return {"layers": {"Ground": [[DEFAULT_TILE for _ in range(ZONE_W)] for _ in range(ZONE_H)]}}


def main() -> None:
    db = json.loads(DB_PATH.read_text(encoding="utf-8"))
    null_zones = [z for z in db["zones"] if not z.get("overlay")]
    print(f"Found {len(null_zones)} zones without overlay")

    overlay = build_overlay()
    payload = json.dumps(overlay, indent=2)

    for entry in null_zones:
        name = entry["name"]
        fname = f"{name}.overlay.json"

        unity_path = UNITY_MAPS / fname
        unity_path.write_text(payload, encoding="utf-8")

        entry["overlay"] = fname
        print(f"  + {fname}")

    DB_PATH.write_text(json.dumps(db, indent=2), encoding="utf-8")
    print(f"\nUpdated {DB_PATH.name}")
    print("Done.")


if __name__ == "__main__":
    main()
