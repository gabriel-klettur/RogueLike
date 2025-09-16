from __future__ import annotations

import pygame
from functools import lru_cache

# Common debug colors
CYAN = (0, 200, 255)
YELLOW = (255, 220, 0)
AMBER = (255, 180, 60)
PINK_OUTLINE = (255, 105, 180)
PINK_FILL = (255, 105, 180, 80)
BLUE_DEBUG = (80, 160, 255)
BLUE_FAINT = (120, 180, 255)
RED_BLOCKED = (255, 80, 80)


@lru_cache(maxsize=8)
def ensure_font(name: str = "Arial", size: int = 14):
    try:
        return pygame.font.SysFont(name, size)
    except Exception:
        return None


def auto_bottom_band_metrics(mask) -> tuple[int, int]:
    """Return (auto_center_x, avg_width) on the bottom band using weighted centroid of opaque pixels.
    Mirrors factory logic so cross matches feet center X.
    """
    try:
        w, h = mask.get_size()
    except Exception:
        return 0, 0
    if w <= 0 or h <= 0:
        return 0, 0
    band_h = max(6, min(max(6, h // 5), 28))
    y_start = h - band_h
    total_weight = 0.0
    sum_x = 0.0
    sum_width = 0.0
    for y in range(h - 1, y_start - 1, -1):
        weight = 1.0 + (y - y_start) * 0.3
        row_count = 0
        for x in range(w):
            if mask.get_at((x, y)):
                sum_x += x * weight
                row_count += 1
        if row_count > 0:
            total_weight += weight * row_count
            sum_width += (row_count * weight)
    if total_weight <= 0.0:
        return w // 2, 0
    cx = int(round(sum_x / total_weight))
    denom = max(1.0, (band_h))
    avg_width = int(round((sum_width / denom)))
    return max(0, min(w - 1, cx)), max(0, avg_width)


def draw_translucent_box(size: tuple[int, int], border_color=(0, 200, 255), bg_rgba=(0, 0, 0, 150)) -> pygame.Surface:
    w, h = size
    box = pygame.Surface((w, h), pygame.SRCALPHA)
    box.fill(bg_rgba)
    pygame.draw.rect(box, border_color, pygame.Rect(0, 0, w, h), width=1)
    return box
