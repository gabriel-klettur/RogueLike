from __future__ import annotations

from typing import Optional, Tuple
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE


def calc_centered_rel(local_tile: Tuple[int, int], tpl_entry: Optional[dict], img_path: Optional[str]) -> Tuple[int, int, Optional[Tuple[int, int]]]:
    """Compute pixel-relative position centered on a tile for a template image.

    Args:
        local_tile: Grid coordinates (tile space) where the visual should be centered.
        tpl_entry: Template metadata dict; may contain 'original_scale'.
        img_path: Optional image path to infer bounding rect when available.

    Returns:
        (rel_x, rel_y, scale) where rel_x/rel_y are pixel offsets relative to the building/world
        origin, and scale is an optional (width, height) tuple.
    """
    rel_x = int(local_tile[0] * TILE_SIZE)
    rel_y = int(local_tile[1] * TILE_SIZE)
    spawn_cx = int(rel_x + (TILE_SIZE // 2))
    spawn_cy = int(rel_y + (TILE_SIZE // 2))
    w = h = None
    try:
        if isinstance(tpl_entry, dict) and isinstance(tpl_entry.get('original_scale'), (list, tuple)):
            oscale = tpl_entry['original_scale']
            if len(oscale) >= 2:
                w, h = int(oscale[0]), int(oscale[1])
    except Exception:
        w = h = None
    br = None
    if img_path:
        try:
            surf = pygame.image.load(img_path)
            if w is not None and h is not None and w > 0 and h > 0:
                surf = pygame.transform.scale(surf, (int(w), int(h)))
            br = surf.get_bounding_rect(min_alpha=1)
        except Exception:
            br = None
            if w is None or h is None:
                try:
                    iw, ih = surf.get_size()  # type: ignore[name-defined]
                    w, h = int(iw), int(ih)
                except Exception:
                    w = h = None
    try:
        if br is not None and br.w > 0 and br.h > 0:
            rel_x = int(spawn_cx - (br.x + br.w // 2))
            rel_y = int(spawn_cy - (br.y + br.h // 2))
        elif w is not None and h is not None and w > 0 and h > 0:
            rel_x = int(spawn_cx - (w // 2))
            rel_y = int(spawn_cy - (h // 2))
    except Exception:
        pass
    scale = (int(w), int(h)) if (w is not None and h is not None and w > 0 and h > 0) else None
    return rel_x, rel_y, scale
