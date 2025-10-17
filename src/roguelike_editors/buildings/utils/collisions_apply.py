from __future__ import annotations
from typing import Dict, List, Optional, Set
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


def _apply_entry_to_building(b: Building, coll_entry: Dict) -> None:
    """Apply a collision entry (already selected) to a Building, resizing to TILE_SIZE grid.

    This mirrors the normalization logic used during initial assembly.
    """
    desired_cols = max(1, (b.image.get_width() + TILE_SIZE - 1) // TILE_SIZE)
    desired_rows = max(1, (b.image.get_height() + TILE_SIZE - 1) // TILE_SIZE)
    src = [row[:] for row in coll_entry.get("collision", [])]
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
    # Use setter to invalidate caches
    b.collision_map = src


def apply_collisions_to_loaded_buildings(
    buildings: List[Building],
    by_image: Dict,
    by_binst: Dict,
    updated_by_img: Optional[List[str]] = None,
    updated_by_inst: Optional[List[str]] = None,
) -> int:
    """Apply recent collision JSON updates to the currently loaded Building objects.

    - Updates CU (per-instance) entries whose ids are in updated_by_inst.
    - Updates CG (global by image_path) for buildings whose normalized image_path is in updated_by_img,
      but only when their collider_scope is not 'CU'.
    Returns the number of buildings updated.
    """
    changed = 0
    upd_img: Set[str] = set(normalize_asset_path(p) for p in (updated_by_img or []))
    upd_ids: Set[str] = set(str(x) for x in (updated_by_inst or []))
    for b in buildings:
        try:
            applied = False
            bid = getattr(b, "id", None)
            if bid is not None and str(bid) in upd_ids:
                entry = by_binst.get(str(bid))
                if isinstance(entry, dict) and "collision" in entry:
                    _apply_entry_to_building(b, entry)
                    changed += 1
                    applied = True
            if not applied:
                # Apply CG by image for non-CU buildings
                scope = getattr(b, "collider_scope", "CG")
                if scope != "CU":
                    img_key = normalize_asset_path(getattr(b, "image_path", ""))
                    if img_key in upd_img:
                        entry = by_image.get(img_key) or by_image.get(img_key.replace("/", "\\"))
                        if isinstance(entry, dict) and "collision" in entry:
                            _apply_entry_to_building(b, entry)
                            changed += 1
        except Exception:
            # Best-effort; do not break the loop on anomalies
            pass
    return changed
