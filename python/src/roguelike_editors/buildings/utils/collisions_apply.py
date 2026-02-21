from __future__ import annotations
from typing import Dict, List, Optional, Set
from roguelike_engine.buildings.building import Building
from roguelike_engine.config.config_tiles import TILE_SIZE
from .asset_paths import normalize_asset_path
from roguelike_engine.buildings.services.collisions import resample_collision_map


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

    # New policy: keep saved grid as-is; runtime scales cell size with image.
    # Ignore grid_ref_size for sizing; no resample on apply.

    if coll_entry and "collision" in coll_entry:
        src = [row[:] for row in coll_entry["collision"]]
        if src:
            try:
                desired_rows = max(1, (int(b.image.get_height()) + TILE_SIZE - 1) // TILE_SIZE)
                desired_cols = max(1, (int(b.image.get_width()) + TILE_SIZE - 1) // TILE_SIZE)
                cur_rows = len(src)
                cur_cols = len(src[0]) if cur_rows > 0 else 0
                if cur_rows != desired_rows or cur_cols != desired_cols:
                    b.collision_map = resample_collision_map(src, desired_rows, desired_cols)
                else:
                    b.collision_map = src
            except Exception:
                b.collision_map = src
        else:
            b.collision_map = [["." for _ in range(15)] for _ in range(15)]
    else:
        # Default policy: initialize a standard 15x15 empty grid when missing
        b.collision_map = [["." for _ in range(15)] for _ in range(15)]

    # Inline override for CU
    try:
        if entry.get("collider_scope", "CG") == "CU":
            ov = entry.get("collision_override")
            if ov and "collision" in ov:
                src = [row[:] for row in ov["collision"]]
                if src:
                    try:
                        desired_rows = max(1, (int(b.image.get_height()) + TILE_SIZE - 1) // TILE_SIZE)
                        desired_cols = max(1, (int(b.image.get_width()) + TILE_SIZE - 1) // TILE_SIZE)
                        cur_rows = len(src)
                        cur_cols = len(src[0]) if cur_rows > 0 else 0
                        if cur_rows != desired_rows or cur_cols != desired_cols:
                            b.collision_map = resample_collision_map(src, desired_rows, desired_cols)
                        else:
                            b.collision_map = src
                    except Exception:
                        b.collision_map = src
                else:
                    b.collision_map = b.collision_map
    except Exception:
        pass


def _apply_entry_to_building(b: Building, coll_entry: Dict) -> None:
    """Apply a collision entry (already selected) to a Building, resizing to TILE_SIZE grid.

    This mirrors the normalization logic used during initial assembly.
    """
    # New policy: apply saved grid as-is (no resample). Runtime scales cells.
    src = [row[:] for row in coll_entry.get("collision", [])]
    if src:
        b.collision_map = src
    else:
        # Default 15x15 when entry lacks a collision grid
        b.collision_map = [["." for _ in range(15)] for _ in range(15)]


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
