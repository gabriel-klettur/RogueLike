from __future__ import annotations
import math
from typing import Dict, List, Optional
import pygame

from .ui_constants import (
    BGCOLOR,
    BORDER_COLOR,
    CLOSE_BUTTON_COLOR,
    TEXT_COLOR,
    SLOT_BG_COLOR,
    SLOT_BORDER_COLOR,
    GRID_COLS,
    GRID_ROWS,
    PADDING,
    SLOT_SIZE,
    CLOSE_BUTTON_SIZE,
    GRAB_PROGRESS_COLOR,
    GRAB_PROGRESS_ALPHA,
    PULSE_BORDER_COLOR,
    PULSE_BASE_ALPHA,
    PULSE_MAX_ALPHA,
    PULSE_BASE_THICKNESS,
    PULSE_MAX_THICKNESS,
    PULSE_FREQ,
    GRAB_SUCCESS_COLOR,
    PULSE_SUCCESS_COLOR,
    INCREASE_COLOR,
    QUANTITY_FLASH_DURATION_MS,
)
from .ui_utils import ease_out_cubic, compute_slot_rect, idx_from_panel_mouse


def draw_panel(screen: pygame.Surface, panel_rect: pygame.Rect, font: pygame.font.Font) -> bool:
    """Draws the inventory panel and close button. Returns True if close was clicked."""
    pygame.draw.rect(screen, BGCOLOR, panel_rect)
    pygame.draw.rect(screen, BORDER_COLOR, panel_rect, 2)

    size = CLOSE_BUTTON_SIZE
    x = panel_rect.x + panel_rect.width - size - PADDING
    y = panel_rect.y + PADDING
    close_rect = pygame.Rect(x, y, size, size)
    pygame.draw.rect(screen, CLOSE_BUTTON_COLOR, close_rect)

    text_surf = font.render("X", True, TEXT_COLOR)
    text_rect = text_surf.get_rect(center=close_rect.center)
    screen.blit(text_surf, text_rect)

    if pygame.mouse.get_pressed()[0] and close_rect.collidepoint(pygame.mouse.get_pos()):
        return True
    return False


def _apply_qty_flash_color(idx: int, base_color, slot_flash: Dict[int, dict]) -> Optional[tuple[int, int, int]]:
    flash = slot_flash.get(idx)
    if not flash:
        return None
    now = pygame.time.get_ticks()
    elapsed = now - int(flash.get('start', 0) or 0)
    if elapsed <= QUANTITY_FLASH_DURATION_MS:
        t = max(0.0, min(1.0, elapsed / float(QUANTITY_FLASH_DURATION_MS)))
        inv = 1.0 - t
        base = flash.get('color', base_color)
        return (
            int(base[0] * inv + TEXT_COLOR[0] * t),
            int(base[1] * inv + TEXT_COLOR[1] * t),
            int(base[2] * inv + TEXT_COLOR[2] * t),
        )
    else:
        slot_flash.pop(idx, None)
        return None


def draw_slots(
    screen: pygame.Surface,
    panel_rect: pygame.Rect,
    slots: List[object],
    icon_surfaces: Dict[str, Optional[pygame.Surface]],
    font: pygame.font.Font,
    slot_flash: Dict[int, dict],
    highlight_idx: Optional[int] = None,
    grab_progress: float = 0.0,
) -> None:
    total_slots = GRID_COLS * GRID_ROWS
    for idx in range(total_slots):
        stack = slots[idx] if idx < len(slots) else None
        slot_rect = compute_slot_rect(panel_rect, idx)
        pygame.draw.rect(screen, SLOT_BG_COLOR, slot_rect)
        pygame.draw.rect(screen, SLOT_BORDER_COLOR, slot_rect, 1)

        if stack:
            surf = icon_surfaces.get(stack.item_id)
            if surf:
                img = pygame.transform.scale(surf, (SLOT_SIZE - 10, SLOT_SIZE - 10))
                screen.blit(img, (slot_rect.x + 5, slot_rect.y + 5))
            qty_color = _apply_qty_flash_color(idx, INCREASE_COLOR, slot_flash) or TEXT_COLOR
            qty_surf = font.render(str(getattr(stack, 'quantity', 0) or 0), True, qty_color)
            qty_rect = qty_surf.get_rect(bottomright=(slot_rect.x + SLOT_SIZE - 5, slot_rect.y + SLOT_SIZE - 5))
            screen.blit(qty_surf, qty_rect)

        # Flash overlay (fill + border)
        flash = slot_flash.get(idx)
        if flash:
            now = pygame.time.get_ticks()
            elapsed = now - int(flash.get('start', 0) or 0)
            if elapsed <= QUANTITY_FLASH_DURATION_MS:
                k = 1.0 - max(0.0, min(1.0, elapsed / float(QUANTITY_FLASH_DURATION_MS)))
                col = flash.get('color', INCREASE_COLOR)
                alpha = int(180 * k)
                overlay = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
                overlay.fill((*col, max(0, alpha // 3)))
                screen.blit(overlay, (slot_rect.x, slot_rect.y))
                border = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
                pygame.draw.rect(border, (*col, alpha), border.get_rect(), 3)
                screen.blit(border, (slot_rect.x, slot_rect.y))
            else:
                slot_flash.pop(idx, None)

        # Hold-to-drag overlay on the highlighted slot
        if highlight_idx is not None and idx == highlight_idx and grab_progress > 0.0:
            p = max(0.0, min(1.0, float(grab_progress)))
            pe = ease_out_cubic(p)
            overlay = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
            overlay.fill((0, 0, 0, 0))
            fill_h = int(SLOT_SIZE * pe)
            fill_rect = pygame.Rect(0, SLOT_SIZE - fill_h, SLOT_SIZE, fill_h)
            done = p >= 1.0 - 1e-6
            base_color = GRAB_SUCCESS_COLOR if done else GRAB_PROGRESS_COLOR
            color = (*base_color, GRAB_PROGRESS_ALPHA)
            pygame.draw.rect(overlay, color, fill_rect)
            screen.blit(overlay, (slot_rect.x, slot_rect.y))

            # Pulsing border synced with progress
            t = pygame.time.get_ticks() / 1000.0
            s = (math.sin(2.0 * math.pi * PULSE_FREQ * t) + 1.0) * 0.5
            pulse_factor = s * pe
            alpha = int(PULSE_BASE_ALPHA + (PULSE_MAX_ALPHA - PULSE_BASE_ALPHA) * pulse_factor)
            thickness = int(PULSE_BASE_THICKNESS + (PULSE_MAX_THICKNESS - PULSE_BASE_THICKNESS) * pulse_factor)
            border_overlay = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
            pulse_color = PULSE_SUCCESS_COLOR if done else PULSE_BORDER_COLOR
            pygame.draw.rect(border_overlay, (*pulse_color, alpha), border_overlay.get_rect(), max(1, thickness))
            screen.blit(border_overlay, (slot_rect.x, slot_rect.y))


def draw_drag_ghost(screen: pygame.Surface, slots, icon_surfaces, drag_idx: Optional[int]) -> None:
    if drag_idx is None:
        return
    stack = slots[drag_idx] if drag_idx < len(slots) else None
    if not stack:
        return
    surf = icon_surfaces.get(stack.item_id)
    if not surf:
        return
    size = SLOT_SIZE - 10
    img = pygame.transform.scale(surf, (size, size))
    ghost = img.copy()
    ghost.set_alpha(150)
    mx, my = pygame.mouse.get_pos()
    screen.blit(ghost, (mx - size // 2, my - size // 2))


def draw_drag_destination_highlight(
    screen: pygame.Surface,
    panel_rect: pygame.Rect,
    drag_idx: int,
    total_slots: int,
) -> None:
    mouse_pos = pygame.mouse.get_pos()
    dst_idx = idx_from_panel_mouse(panel_rect, mouse_pos)
    if dst_idx is None or dst_idx == drag_idx or dst_idx < 0 or dst_idx >= total_slots:
        return
    slot_rect = compute_slot_rect(panel_rect, dst_idx)
    overlay = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
    overlay.fill((*GRAB_SUCCESS_COLOR, 80))
    screen.blit(overlay, (slot_rect.x, slot_rect.y))
    border_overlay = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
    pygame.draw.rect(border_overlay, (*PULSE_SUCCESS_COLOR, 200), border_overlay.get_rect(), 3)
    screen.blit(border_overlay, (slot_rect.x, slot_rect.y))


def draw_map_drop_feedback(
    screen: pygame.Surface,
    panel_rect: pygame.Rect,
    hover_idx: Optional[int],
    hover_start: Optional[int],
    hover_threshold: int = 300,
) -> None:
    if hover_idx is None or hover_start is None:
        return
    col = int(hover_idx % GRID_COLS)
    row = int(hover_idx // GRID_COLS)
    x = panel_rect.x + PADDING + col * (SLOT_SIZE + PADDING)
    y = panel_rect.y + PADDING + row * (SLOT_SIZE + PADDING)

    now_ts = pygame.time.get_ticks()
    p = max(0.0, min(1.0, (now_ts - hover_start) / max(1, hover_threshold)))
    pe = ease_out_cubic(p)

    overlay = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
    overlay.fill((0, 0, 0, 0))
    fill_h = int(SLOT_SIZE * pe)
    fill_rect = pygame.Rect(0, SLOT_SIZE - fill_h, SLOT_SIZE, fill_h)
    done = p >= 0.999
    base_color = GRAB_SUCCESS_COLOR if done else GRAB_PROGRESS_COLOR
    color = (*base_color, GRAB_PROGRESS_ALPHA)
    pygame.draw.rect(overlay, color, fill_rect)
    screen.blit(overlay, (x, y))

    t = now_ts / 1000.0
    s = (math.sin(2.0 * math.pi * PULSE_FREQ * t) + 1.0) * 0.5
    pulse_factor = s * pe
    alpha = int(PULSE_BASE_ALPHA + (PULSE_MAX_ALPHA - PULSE_BASE_ALPHA) * pulse_factor)
    thickness = int(PULSE_BASE_THICKNESS + (PULSE_MAX_THICKNESS - PULSE_BASE_THICKNESS) * pulse_factor)
    border_overlay = pygame.Surface((SLOT_SIZE, SLOT_SIZE), pygame.SRCALPHA)
    pulse_color = PULSE_SUCCESS_COLOR if done else PULSE_BORDER_COLOR
    pygame.draw.rect(border_overlay, (*pulse_color, alpha), border_overlay.get_rect(), max(1, thickness))
    screen.blit(border_overlay, (x, y))


def draw_map_drop_ghost(screen: pygame.Surface, sprite_image: pygame.Surface, scale_factor: float) -> None:
    spr = pygame.transform.rotozoom(sprite_image, 0, scale_factor)
    ghost = spr.copy()
    ghost.set_alpha(150)
    mx, my = pygame.mouse.get_pos()
    rect = ghost.get_rect(center=(mx, my))
    screen.blit(ghost, rect)
