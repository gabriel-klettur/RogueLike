import json
import os
from typing import Dict, Any

# Use engine config paths so this script works from project root
try:
    from roguelike_engine.config.config import BUILDINGS_DATA_PATH, BUILDINGS_COLLISIONS_DATA_PATH
except Exception:
    BUILDINGS_DATA_PATH = os.path.join("data", "buildings", "buildings_data.json")
    BUILDINGS_COLLISIONS_DATA_PATH = os.path.join("data", "buildings", "buildings_collisions_data.json")


def read_json(path: str):
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as f:
        try:
            return json.load(f)
        except Exception:
            return None


def ensure_ids(buildings: list[Dict[str, Any]]) -> bool:
    """Assign incremental 'id' to any building missing it. Returns True if modified."""
    changed = False
    try:
        existing = [int(b.get("id")) for b in buildings if str(b.get("id")).isdigit()]
        next_id = (max(existing) + 1) if existing else 1
        for b in buildings:
            bid = b.get("id")
            if bid is None or not str(bid).isdigit():
                b["id"] = next_id
                next_id += 1
                changed = True
    except Exception:
        pass
    return changed


def normalize_collisions(raw: Any) -> Dict[str, Any]:
    if isinstance(raw, dict) and ("global" in raw or "instances" in raw or "by_building_id" in raw):
        return {
            "global": raw.get("global", {}) or {},
            "instances": raw.get("instances", {}) or {},
            "by_building_id": raw.get("by_building_id", {}) or {},
        }
    # legacy
    return {"global": raw if isinstance(raw, dict) else {}, "instances": {}, "by_building_id": {}}


def migrate_by_building_id(buildings: list[Dict[str, Any]], collisions: Dict[str, Any]) -> bool:
    changed = False
    by_id = collisions.setdefault("by_building_id", {})
    global_maps = collisions.get("global", {})

    for b in buildings:
        # skip spawner visuals
        if b.get("spawn_id") is not None:
            continue
        bid = b.get("id")
        if bid is None:
            continue
        k = str(bid)
        if k in by_id:
            continue  # keep existing

        # prefer CU override stored inside building entry
        if b.get("collider_scope", "CG") == "CU":
            ov = b.get("collision_override")
            if isinstance(ov, dict) and "collision" in ov:
                by_id[k] = {
                    "width": int(ov.get("width")) if isinstance(ov.get("width"), int) else len(ov.get("collision", [])[0]) if ov.get("collision") else 0,
                    "height": int(ov.get("height")) if isinstance(ov.get("height"), int) else len(ov.get("collision", [])) if ov.get("collision") else 0,
                    "collision": ov.get("collision", []),
                }
                changed = True
                continue
        # fallback to global by image path (new 'idle' or legacy 'image_path')
        img = b.get("idle") or b.get("image_path")
        if not img:
            continue
        g = global_maps.get(img)
        if isinstance(g, dict) and "collision" in g:
            by_id[k] = {
                "width": int(g.get("width")) if isinstance(g.get("width"), int) else len(g.get("collision", [])[0]) if g.get("collision") else 0,
                "height": int(g.get("height")) if isinstance(g.get("height"), int) else len(g.get("collision", [])) if g.get("collision") else 0,
                "collision": g.get("collision", []),
            }
            changed = True
    return changed


def main():
    print("[Migrate] Loading buildings and collisions...")
    buildings = read_json(BUILDINGS_DATA_PATH)
    if not isinstance(buildings, list):
        print(f"[Migrate] No valid buildings at {BUILDINGS_DATA_PATH}")
        return 1
    collisions_raw = read_json(BUILDINGS_COLLISIONS_DATA_PATH) or {}
    collisions = normalize_collisions(collisions_raw)

    print("[Migrate] Ensuring building IDs...")
    ids_changed = ensure_ids(buildings)

    print("[Migrate] Backfilling collisions by building_id...")
    by_id_changed = migrate_by_building_id(buildings, collisions)

    if not ids_changed and not by_id_changed:
        print("[Migrate] Nothing to change. Up to date.")
        return 0

    os.makedirs(os.path.dirname(BUILDINGS_DATA_PATH), exist_ok=True)
    os.makedirs(os.path.dirname(BUILDINGS_COLLISIONS_DATA_PATH), exist_ok=True)

    # Write outputs
    with open(BUILDINGS_DATA_PATH, "w", encoding="utf-8") as f:
        json.dump(buildings, f, indent=4)
    with open(BUILDINGS_COLLISIONS_DATA_PATH, "w", encoding="utf-8") as f:
        json.dump(collisions, f, indent=4)

    print(f"[Migrate] Done. ids_changed={ids_changed}, by_id_changed={by_id_changed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
