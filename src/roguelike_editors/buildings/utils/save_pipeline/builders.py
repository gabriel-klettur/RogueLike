"""Builders for template and instance payloads."""
from __future__ import annotations

from typing import Dict, Optional

from roguelike_editors.buildings.utils.asset_paths import normalize_asset_path


def build_template_entry(building: object) -> dict:
    """Return a template dictionary for the given building."""

    entry = {
        "assets": {"idle": normalize_asset_path(getattr(building, "image_path", None))},
        "solid": bool(getattr(building, "solid", True)),
        "split_ratio": round(float(getattr(building, "split_ratio", 0.5)), 3),
        "collider_scope": getattr(building, "collider_scope", "CG"),
    }
    original_scale = getattr(building, "original_scale", None)
    if isinstance(original_scale, (list, tuple)):
        entry["original_scale"] = list(original_scale)
    return entry


def build_instance_overrides(building: object) -> Optional[Dict[str, object]]:
    """Return per-instance overrides if any attribute diverges from template."""

    overrides: Dict[str, object] = {}

    try:
        image = getattr(building, "image", None)
        if image is not None:
            overrides["scale"] = [int(image.get_width()), int(image.get_height())]
    except Exception:
        pass

    try:
        if getattr(building, "z_bottom", None) is not None:
            overrides["z_bottom"] = getattr(building, "z_bottom")
        if getattr(building, "z_top", None) is not None:
            overrides["z_top"] = getattr(building, "z_top")
    except Exception:
        pass

    try:
        if getattr(building, "collider_scope", "CG") == "CU" and getattr(building, "collision_map", None):
            overrides["collider_scope"] = "CU"
            collision_map = getattr(building, "collision_map", None)
            try:
                height = len(collision_map) if isinstance(collision_map, list) else 0
                width = max((len(row) for row in collision_map if isinstance(row, list)), default=0)
            except Exception:
                height = 0
                width = 0
            overrides["collision_override"] = {
                "width": int(width),
                "height": int(height),
                "collision": collision_map,
            }
    except Exception:
        pass

    try:
        portal = _build_portal_payload(building)
        if portal is not None:
            overrides["portal"] = portal
    except Exception:
        pass

    return overrides or None


def _build_portal_payload(building: object) -> Optional[Dict[str, object]]:
    dest_world = getattr(building, "portal_dest_world", None)
    dest_zone = getattr(building, "portal_dest_zone", None)
    dest_x = getattr(building, "portal_dest_x", None)
    dest_y = getattr(building, "portal_dest_y", None)

    is_portal = getattr(building, "is_portal", False)
    if not is_portal and all(value is None for value in (dest_world, dest_zone, dest_x, dest_y)):
        return None

    return {
        "dest_world": dest_world,
        "dest_zone": dest_zone,
        "dest_x": int(dest_x) if dest_x is not None else None,
        "dest_y": int(dest_y) if dest_y is not None else None,
    }
