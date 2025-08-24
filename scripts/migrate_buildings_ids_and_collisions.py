import json
import os
from typing import Dict, Any, Tuple

# Use engine config paths so this script works from project root
try:
    from roguelike_engine.config.config import (
        BUILDINGS_DATA_PATH,
        BUILDINGS_TEMPLATES_PATH,
        BUILDINGS_INSTANCES_PATH,
        BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
        BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
        BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
        BUILDINGS_COLLISIONS_DATA_PATH,  # legacy fallback
    )
except Exception:
    BUILDINGS_DATA_PATH = os.path.join("data", "buildings", "buildings_data.json")
    BUILDINGS_TEMPLATES_PATH = os.path.join("data", "buildings", "buildings_templates.json")
    BUILDINGS_INSTANCES_PATH = os.path.join("data", "buildings", "buildings_instances.json")
    BUILDINGS_COLLISIONS_BY_IMAGE_PATH = os.path.join("data", "buildings", "buildings_collisions_by_image.json")
    BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = os.path.join("data", "buildings", "buildings_collisions_by_spawn_id.json")
    BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = os.path.join("data", "buildings", "buildings_collisions_by_building_instance_id.json")
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
    """Normalize legacy combined format into a dict with keys: global, instances, by_building_id."""
    if isinstance(raw, dict) and ("global" in raw or "instances" in raw or "by_building_id" in raw):
        return {
            "global": raw.get("global", {}) or {},
            "instances": raw.get("instances", {}) or {},
            "by_building_id": raw.get("by_building_id", {}) or {},
        }
    # legacy flat mapping treated as global by image path
    return {"global": raw if isinstance(raw, dict) else {}, "instances": {}, "by_building_id": {}}


def load_split_collisions() -> Tuple[Dict[str, Any], Dict[str, Any], Dict[str, Any]]:
    """Load split collisions. If all are empty, try legacy combined as fallback.

    Returns (by_image, by_spawn, by_building_instance_id)
    """
    by_image = read_json(BUILDINGS_COLLISIONS_BY_IMAGE_PATH) or {}
    by_spawn = read_json(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH) or {}
    by_binst = read_json(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH) or {}

    if not by_image and not by_spawn and not by_binst:
        legacy = read_json(BUILDINGS_COLLISIONS_DATA_PATH) or {}
        legacy_n = normalize_collisions(legacy)
        by_image = legacy_n.get("global", {}) or {}
        by_spawn = legacy_n.get("instances", {}) or {}
        by_binst = legacy_n.get("by_building_id", {}) or {}

    return by_image, by_spawn, by_binst


def load_buildings_and_templates() -> Tuple[list[Dict[str, Any]] | None, Dict[int, Dict[str, Any]]]:
    """Load buildings list and templates map.

    Preference order for buildings:
    1) BUILDINGS_INSTANCES_PATH (instances schema)
    2) BUILDINGS_DATA_PATH (legacy single list schema)
    Templates are optional but recommended when using instances. Returns a map by template id.
    """
    buildings = read_json(BUILDINGS_INSTANCES_PATH)
    if isinstance(buildings, list):
        templates_list = read_json(BUILDINGS_TEMPLATES_PATH) or []
        tmpl_by_id: Dict[int, Dict[str, Any]] = {}
        if isinstance(templates_list, list):
            for t in templates_list:
                try:
                    tid = int(t.get("id"))
                    tmpl_by_id[tid] = t
                except Exception:
                    continue
        return buildings, tmpl_by_id

    # Fallback legacy
    buildings = read_json(BUILDINGS_DATA_PATH)
    if isinstance(buildings, list):
        return buildings, {}
    return None, {}


def migrate_by_building_id(
    buildings: list[Dict[str, Any]],
    by_image: Dict[str, Any],
    by_binst: Dict[str, Any],
    templates_by_id: Dict[int, Dict[str, Any]] | None = None,
) -> bool:
    changed = False
    by_id = by_binst
    global_maps = by_image

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

        # prefer CU override stored inside building entry (legacy schema)
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

        # fallback to global by image path
        # - instances schema: resolve via template.assets.idle
        # - legacy schema: prefer entry 'idle' or 'image_path' on the building itself
        img = None
        if templates_by_id is not None and isinstance(templates_by_id, dict) and "template_id" in b:
            try:
                tid = int(b.get("template_id"))
                tmpl = templates_by_id.get(tid) or {}
                assets = tmpl.get("assets") or {}
                img = assets.get("idle")
            except Exception:
                img = None
        if not img:
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
    print("[Migrate] Loading buildings (instances or legacy) and split collisions (with legacy fallback)...")
    buildings, templates_by_id = load_buildings_and_templates()
    if not isinstance(buildings, list):
        print(
            f"[Migrate] No valid buildings. Checked instances at {BUILDINGS_INSTANCES_PATH} and legacy at {BUILDINGS_DATA_PATH}"
        )
        return 1

    by_image, by_spawn, by_binst = load_split_collisions()

    # Instances already come with ids; ensure_ids still works if missing
    print("[Migrate] Ensuring building IDs...")
    ids_changed = ensure_ids(buildings)

    print("[Migrate] Backfilling collisions by building_instance_id (CU)...")
    by_id_changed = migrate_by_building_id(buildings, by_image, by_binst, templates_by_id)

    if not ids_changed and not by_id_changed:
        print("[Migrate] Nothing to change. Up to date.")
        return 0

    # Determine where to write buildings list: keep same source path used
    buildings_out_path = (
        BUILDINGS_INSTANCES_PATH if os.path.exists(BUILDINGS_INSTANCES_PATH) else BUILDINGS_DATA_PATH
    )
    os.makedirs(os.path.dirname(buildings_out_path), exist_ok=True)
    os.makedirs(os.path.dirname(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH), exist_ok=True)

    # Write outputs
    with open(buildings_out_path, "w", encoding="utf-8") as f:
        json.dump(buildings, f, indent=4)
    with open(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH, "w", encoding="utf-8") as f:
        json.dump(by_binst, f, indent=4)

    print(
        f"[Migrate] Done. ids_changed={ids_changed}, by_id_changed={by_id_changed}. "
        f"Wrote CU split at {BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
