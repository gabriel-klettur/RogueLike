import json
import os
from typing import Any, Dict, Tuple

try:
    from roguelike_engine.config.config import (
        BUILDINGS_COLLISIONS_DATA_PATH,
        BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
        BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
        BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
    )
except Exception:
    # Fallback defaults if config import fails
    BUILDINGS_COLLISIONS_DATA_PATH = os.path.join("data", "buildings", "buildings_collisions_data.json")
    BUILDINGS_COLLISIONS_BY_IMAGE_PATH = os.path.join("data", "buildings", "buildings_collisions_by_image.json")
    BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = os.path.join("data", "buildings", "buildings_collisions_by_spawn_id.json")
    BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = os.path.join("data", "buildings", "buildings_collisions_by_building_instance_id.json")


def read_json(path: str) -> Any:
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as f:
        try:
            return json.load(f)
        except Exception:
            return None


def write_json(path: str, data: Any) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4, ensure_ascii=False)


def merge_dict(dst: Dict[str, Any], src: Dict[str, Any]) -> Dict[str, Any]:
    if not isinstance(src, dict):
        return dst
    for k, v in src.items():
        dst[k] = v
    return dst


def normalize_combined(raw: Any) -> Tuple[Dict[str, Any], Dict[str, Any], Dict[str, Any]]:
    """
    Returns three dicts: (by_image_path, by_spawn_id, by_building_instance_id)

    Supports multiple legacy shapes:
    - {"by_image_path": {...}, "by_spawn_id": {...}, "by_building_instance_id": {...}}
    - flat CG-only mapping: {"assets/path.png": {"collision": ...}}
    - {"global": {...}, "by_building_id": {...}, "instances": {...}}
    """
    by_image: Dict[str, Any] = {}
    by_spawn: Dict[str, Any] = {}
    by_binst: Dict[str, Any] = {}

    if not isinstance(raw, dict):
        return by_image, by_spawn, by_binst

    # Newer combined schema already separated by keys
    if ("by_image_path" in raw) or ("by_spawn_id" in raw) or ("by_building_instance_id" in raw):
        by_image = dict(raw.get("by_image_path", {}) or {})
        by_spawn = dict(raw.get("by_spawn_id", {}) or {})
        by_binst = dict(raw.get("by_building_instance_id", {}) or {})
        return by_image, by_spawn, by_binst

    # Global/instances/by_building_id schema
    if ("global" in raw) or ("instances" in raw) or ("by_building_id" in raw):
        g = raw.get("global", {}) or {}
        by_image = dict(g) if isinstance(g, dict) else {}
        by_binst = dict(raw.get("by_building_id", {}) or {})
        # unsure mapping for 'instances' -> keep as spawn for legacy purposes
        inst = raw.get("instances", {}) or {}
        by_spawn = dict(inst) if isinstance(inst, dict) else {}
        return by_image, by_spawn, by_binst

    # Assume flat mapping of image_path -> collision
    flat = {k: v for k, v in raw.items() if isinstance(k, str)}
    if flat:
        by_image = flat

    return by_image, by_spawn, by_binst


def main() -> int:
    print("[SplitCollisions] Reading combined collisions:", BUILDINGS_COLLISIONS_DATA_PATH)
    raw = read_json(BUILDINGS_COLLISIONS_DATA_PATH) or {}

    by_image_new, by_spawn_new, by_binst_new = normalize_combined(raw)

    # Load existing split files if present to merge
    existing_by_image = read_json(BUILDINGS_COLLISIONS_BY_IMAGE_PATH) or {}
    existing_by_spawn = read_json(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH) or {}
    existing_by_binst = read_json(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH) or {}

    merged_by_image = merge_dict(existing_by_image, by_image_new)
    merged_by_spawn = merge_dict(existing_by_spawn, by_spawn_new)
    merged_by_binst = merge_dict(existing_by_binst, by_binst_new)

    write_json(BUILDINGS_COLLISIONS_BY_IMAGE_PATH, merged_by_image)
    write_json(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH, merged_by_spawn)
    write_json(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH, merged_by_binst)

    print("[SplitCollisions] Wrote:")
    print("  - by_image:", BUILDINGS_COLLISIONS_BY_IMAGE_PATH, f"({len(merged_by_image)} keys)")
    print("  - by_spawn_id:", BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH, f"({len(merged_by_spawn)} keys)")
    print("  - by_building_instance_id:", BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH, f"({len(merged_by_binst)} keys)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
