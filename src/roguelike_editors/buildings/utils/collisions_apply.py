from __future__ import annotations
from typing import Dict
from roguelike_engine.buildings.building import Building
from roguelike_engine.config.config_tiles import TILE_SIZE
from .asset_paths import normalize_asset_path


def apply_collision_for_building(
    b: Building,
    entry: dict,
    collisions_global: Dict,
    collisions_instances: Dict,
    collisions_by_id: Dict,
) -> None:
    """Initialize Building.collision_map respecting collider_scope.

    Rules:
    - scope == 'CU': prefer collisions_by_id -> legacy collisions_instances (spawn_id) -> collisions_global by image_path
    - scope == 'CG' (default): only collisions_global by image_path
    Then apply inline per-instance override if collider_scope == 'CU'.
    """
    _img_path = normalize_asset_path((entry.get("assets") or {}).get("idle"))

    # Select base collision entry according to scope
    coll_entry = None
    try:
        scope = entry.get("collider_scope", "CG")
        if scope == "CU":
            # 1) Per-building-instance collisions (new scheme)
            bid = getattr(b, "id", None) or entry.get("id")
            if bid is not None:
                coll_entry = collisions_by_id.get(str(bid))
            # 2) Legacy per-spawn override (fallback)
            if not coll_entry:
                sid = getattr(b, "spawn_id", None)
                if sid is not None:
                    coll_entry = collisions_instances.get(sid)
            # 3) Global by image_path
            if not coll_entry:
                coll_entry = collisions_global.get(_img_path) or collisions_global.get((_img_path or "").replace("/", "\\"))
        else:
            coll_entry = collisions_global.get(_img_path) or collisions_global.get((_img_path or "").replace("/", "\\"))
    except Exception:
        coll_entry = collisions_global.get(_img_path) or collisions_global.get((_img_path or "").replace("/", "\\"))

    desired_cols = max(1, (b.image.get_width() + TILE_SIZE - 1) // TILE_SIZE)
    desired_rows = max(1, (b.image.get_height() + TILE_SIZE - 1) // TILE_SIZE)

    if coll_entry and "collision" in coll_entry:
        src = [row[:] for row in coll_entry["collision"]]
        cur_rows = len(src)
        cur_cols = len(src[0]) if cur_rows > 0 else 0
        # Normalize rows
        if cur_rows < desired_rows:
            for _ in range(desired_rows - cur_rows):
                src.append(["." for _ in range(cur_cols or desired_cols)])
            cur_rows = desired_rows
        elif cur_rows > desired_rows:
            src = src[:desired_rows]
            cur_rows = desired_rows
        # Normalize cols
        if cur_cols < desired_cols:
            for r in range(cur_rows):
                if cur_cols == 0:
                    src[r] = ["."] * desired_cols
                else:
                    src[r].extend(["."] * (desired_cols - cur_cols))
        elif cur_cols > desired_cols:
            for r in range(cur_rows):
                src[r] = src[r][:desired_cols]
        b.collision_map = src
    else:
        # default empty map sized to image ceil
        w, h = desired_cols, desired_rows
        b.collision_map = [["." for _ in range(w)] for _ in range(h)]

    # Inline override for CU
    try:
        if entry.get("collider_scope", "CG") == "CU":
            ov = entry.get("collision_override")
            if ov and "collision" in ov:
                src = [row[:] for row in ov["collision"]]
                cur_rows = len(src)
                cur_cols = len(src[0]) if cur_rows > 0 else 0
                if cur_rows < desired_rows:
                    for _ in range(desired_rows - cur_rows):
                        src.append(["." for _ in range(cur_cols or desired_cols)])
                    cur_rows = desired_rows
                elif cur_rows > desired_rows:
                    src = src[:desired_rows]
                    cur_rows = desired_rows
                if cur_cols < desired_cols:
                    for r in range(cur_rows):
                        if cur_cols == 0:
                            src[r] = ["."] * desired_cols
                        else:
                            src[r].extend(["."] * (desired_cols - cur_cols))
                elif cur_cols > desired_cols:
                    for r in range(cur_rows):
                        src[r] = src[r][:desired_cols]
                b.collision_map = src
    except Exception:
        pass
