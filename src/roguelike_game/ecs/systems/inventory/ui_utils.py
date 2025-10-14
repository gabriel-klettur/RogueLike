from __future__ import annotations
import pygame
from typing import Optional, Tuple

from .ui_constants import GRID_COLS, GRID_ROWS, PADDING, SLOT_SIZE


def ease_out_cubic(x: float) -> float:
    x = max(0.0, min(1.0, float(x)))
    return 1.0 - (1.0 - x) ** 3


def compute_panel_rect(screen: pygame.Surface, drag_offset: Tuple[int, int]) -> pygame.Rect:
    cols, rows = GRID_COLS, GRID_ROWS
    padding, size = PADDING, SLOT_SIZE
    panel_w = cols * size + (cols + 1) * padding
    panel_h = rows * size + (rows + 1) * padding
    screen_w, screen_h = screen.get_size()
    center_x = (screen_w - panel_w) // 2
    center_y = (screen_h - panel_h) // 2
    x = center_x + drag_offset[0]
    y = center_y + drag_offset[1]
    return pygame.Rect(x, y, panel_w, panel_h)


def compute_slot_rect(panel_rect: pygame.Rect, idx: int) -> pygame.Rect:
    cols = GRID_COLS
    padding, size = PADDING, SLOT_SIZE
    col = idx % cols
    row = idx // cols
    x = panel_rect.x + padding + col * (size + padding)
    y = panel_rect.y + padding + row * (size + padding)
    return pygame.Rect(x, y, size, size)


def idx_from_panel_mouse(panel_rect: pygame.Rect, mouse_pos: Tuple[int, int]) -> Optional[int]:
    if not panel_rect.collidepoint(mouse_pos):
        return None
    rel_x = mouse_pos[0] - panel_rect.x - PADDING
    rel_y = mouse_pos[1] - panel_rect.y - PADDING
    if rel_x < 0 or rel_y < 0:
        return None
    step = SLOT_SIZE + PADDING
    col = int(rel_x // step)
    row = int(rel_y // step)
    idx = row * GRID_COLS + col
    if col < 0 or row < 0 or col >= GRID_COLS or row >= GRID_ROWS:
        return None
    # Confirm exact tile hit
    slot_x = panel_rect.x + PADDING + col * step
    slot_y = panel_rect.y + PADDING + row * step
    slot_rect = pygame.Rect(slot_x, slot_y, SLOT_SIZE, SLOT_SIZE)
    return idx if slot_rect.collidepoint(mouse_pos) else None
