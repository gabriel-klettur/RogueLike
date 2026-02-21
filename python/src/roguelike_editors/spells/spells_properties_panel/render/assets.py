from __future__ import annotations

import logging
import os
from typing import Any

import pygame

from roguelike_engine.utils.loader import load_image
from ..utils.data_map import infer_particle_kind


logger = logging.getLogger(__name__)
LOG_SPELLS_PROPS_DEBUG = (
    os.getenv("RL_SPELLS_PROPS_DEBUG") == "1"
    or os.getenv("RL_SPELLS_VIEW_DEBUG") == "1"
    or os.getenv("RL_SPELLS_EDITOR_DEBUG") == "1"
)
_last_call_log_ts = 0


def _extract_icon_path(data_map_icon: Any) -> str | None:
    """Extract icon path from nested vfx.sprite.path with fallback to flat sprite."""
    icon_path: str | None = None
    try:
        vfx = data_map_icon.get("vfx", {}) if isinstance(data_map_icon, dict) else {}
        if isinstance(vfx, dict):
            spr = vfx.get("sprite", {})
            if isinstance(spr, dict):
                icon_path = spr.get("path")
    except Exception:
        icon_path = None
    if not icon_path and isinstance(data_map_icon, dict):
        icon_path = data_map_icon.get("sprite")
    return icon_path


def render_assets_section(
    screen: pygame.Surface,
    font: pygame.font.Font,
    model: object,
    view_rect: pygame.Rect,
    data_map_icon: Any,
    preview_provider,
    dt_ms: int,
) -> None:
    """Render the assets/particles cell and label into the content view."""
    cell_size = 96
    pad_cell = 8
    cx = view_rect.x + pad_cell
    cy = view_rect.y + pad_cell
    cell_rect = pygame.Rect(cx, cy, cell_size, cell_size)
    setattr(model, "asset_cell_rect", cell_rect)

    pygame.draw.rect(screen, (60, 60, 60), cell_rect)
    pygame.draw.rect(screen, (255, 255, 255), cell_rect, 2)

    # Image or particle preview
    data_map_local = data_map_icon if isinstance(data_map_icon, dict) else {}
    icon_path = _extract_icon_path(data_map_icon)

    drew_preview = False
    if callable(preview_provider):
        try:
            size = (cell_size - 4, cell_size - 4)
            if LOG_SPELLS_PROPS_DEBUG and logger.isEnabledFor(logging.DEBUG):
                global _last_call_log_ts
                now_ms = pygame.time.get_ticks()
                if now_ms - _last_call_log_ts >= 1000:
                    try:
                        logger.debug("[SpellsProps] calling provider size=%s dt_ms=%d", size, dt_ms)
                    except Exception:
                        pass
                    _last_call_log_ts = now_ms
            frame = preview_provider(size, dt_ms)
            fw, fh = frame.get_size()
            dx = cell_rect.x + (cell_size - fw) // 2
            dy = cell_rect.y + (cell_size - fh) // 2
            screen.blit(frame, (dx, dy))
            drew_preview = True
        except Exception:
            drew_preview = False

    if not drew_preview:
        if icon_path:
            try:
                thumb = load_image(str(icon_path), (cell_size - 4, cell_size - 4))
                screen.blit(thumb, (cell_rect.x + 2, cell_rect.y + 2))
            except Exception:
                ph = pygame.Surface((cell_size - 4, cell_size - 4))
                ph.fill((100, 100, 100))
                screen.blit(ph, (cell_rect.x + 2, cell_rect.y + 2))
        else:
            ph = pygame.Surface((cell_size - 4, cell_size - 4))
            ph.fill((40, 40, 40))
            screen.blit(ph, (cell_rect.x + 2, cell_rect.y + 2))

    # Label on the right
    right_label_x = cell_rect.right + 10
    max_label_w = max(0, view_rect.right - right_label_x)
    if drew_preview:
        sys_name = infer_particle_kind(data_map_local)
        label_text = f"Particles: {sys_name}"
    else:
        label_text = f"Asset: {icon_path or ''}"
    label_text = label_text[:128] if max_label_w <= 0 else label_text  # coarse guard
    label_surf = font.render(label_text, True, (220, 220, 220))
    screen.blit(label_surf, (right_label_x, cell_rect.y + 4))
