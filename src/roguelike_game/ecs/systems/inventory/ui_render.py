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
from .ui_constants import TABS_LABELS


def measure_tabs_total_width(font: pygame.font.Font, labels: list[str] | None = None, include_close: bool = True) -> tuple[int, int]:
    """Measure total width required to render tabs (and optional close button).

    Returns (total_width_pixels, tab_height_pixels).
    """
    labels = labels or TABS_LABELS
    tab_h = max(font.get_height() + 8, 26)
    total_w = 0
    spacing = max(4, PADDING // 2)
    for i, label in enumerate(labels):
        t = font.render(label, True, TEXT_COLOR)
        w = t.get_width() + 16
        total_w += w
        if i < len(labels) - 1:
            total_w += spacing  # spacing between tabs
    if include_close:
        total_w += 8 + CLOSE_BUTTON_SIZE  # spacing + button size
    return total_w, tab_h


def draw_panel(screen: pygame.Surface, panel_rect: pygame.Rect, font: pygame.font.Font) -> bool:
    """Draws the inventory panel background and border. No internal close button.

    Returns False (kept for backward compatibility with callers).
    """
    pygame.draw.rect(screen, BGCOLOR, panel_rect)
    pygame.draw.rect(screen, BORDER_COLOR, panel_rect, 2)
    return False


def measure_footer_height(font: pygame.font.Font) -> int:
    """Return the vertical space needed for the footer (currency bar)."""
    return max(font.get_height() + 10, 28)


def draw_footer_currency(
    screen: pygame.Surface,
    content_bounds: tuple[int, int],
    top_y: int,
    font: pygame.font.Font,
    amount: int,
    icon: Optional[pygame.Surface],
) -> pygame.Rect:
    """Draw a compact currency pill centered inside content_bounds at top_y.

    Returns the rect occupied by the pill.
    """
    left, right = content_bounds
    height = measure_footer_height(font)
    max_w = max(0, right - left)
    # Prepare text
    txt = str(max(0, int(amount)))
    text_surf = font.render(txt, True, TEXT_COLOR)
    icon_size = max(16, height - 10)
    has_icon = icon is not None
    content_w = (icon_size if has_icon else 0) + (8 if has_icon else 0) + text_surf.get_width()
    pad = 10
    pill_w = min(max_w, content_w + pad * 2)
    x = left + (max_w - pill_w) // 2
    pill_rect = pygame.Rect(x, top_y, pill_w, height)
    # Background and border
    pygame.draw.rect(screen, (65, 65, 65), pill_rect, border_radius=height // 2)
    pygame.draw.rect(screen, BORDER_COLOR, pill_rect, 1, border_radius=height // 2)
    # Draw icon and text
    cx = pill_rect.x + pad
    cy = pill_rect.y + (pill_rect.h - icon_size) // 2
    if has_icon:
        icon_scaled = pygame.transform.smoothscale(icon, (icon_size, icon_size))
        screen.blit(icon_scaled, (cx, cy))
        cx += icon_size + 8
    ty = pill_rect.y + (pill_rect.h - text_surf.get_height()) // 2
    screen.blit(text_surf, (cx, ty))
    return pill_rect


def draw_tabs(
    screen: pygame.Surface,
    panel_rect: pygame.Rect,
    font: pygame.font.Font,
    active_index: int,
    labels: list[str] | None = None,
    content_bounds: tuple[int, int] | None = None,
) -> tuple[list[pygame.Rect], int, pygame.Rect]:
    """Draw a simple tabs bar at the top inside panel.

    Returns (tab_rects_in_screen_coords, tabs_area_height)
    """
    labels = labels or TABS_LABELS
    # Measure tab height and header
    tab_h = max(font.get_height() + 8, 26)
    header_h = font.get_height()
    header_top_gap = max(2, PADDING // 4)
    header_y = panel_rect.y + header_top_gap
    # Draw centered header title (align to content bounds if provided)
    title = "Inventory"
    title_surf = font.render(title, True, TEXT_COLOR)
    if content_bounds is not None:
        cb_left, cb_right = content_bounds
        title_cx = cb_left + (cb_right - cb_left) // 2
    else:
        title_cx = panel_rect.centerx
    title_rect = title_surf.get_rect(center=(title_cx, header_y + header_h // 2))
    pygame.draw.rect(screen, BGCOLOR, title_rect.inflate(8, 2))  # ensure readable over map
    screen.blit(title_surf, title_rect)
    # Tabs y position below the header
    y = header_y + header_h + header_top_gap
    # Close button anchored to the right padding of the panel (computed early to know available width)
    close_rect = pygame.Rect(
        panel_rect.right - PADDING - CLOSE_BUTTON_SIZE,
        y + (tab_h - CLOSE_BUTTON_SIZE) // 2,
        CLOSE_BUTTON_SIZE,
        CLOSE_BUTTON_SIZE,
    )
    # Compute total width of tabs (without close) to optionally center them
    tab_widths: list[int] = []
    total_tabs_w = 0
    spacing = max(4, PADDING // 2)
    for i, label in enumerate(labels):
        t = font.render(label, True, TEXT_COLOR)
        w = t.get_width() + 16
        tab_widths.append(w)
        total_tabs_w += w
        if i < len(labels) - 1:
            total_tabs_w += spacing
    # Determine available horizontal bounds for centering tabs
    panel_left = panel_rect.x + PADDING
    panel_right = close_rect.x - 8
    if content_bounds is not None:
        cb_left, cb_right = content_bounds
        cb_avail = max(0, cb_right - cb_left)
        # If tabs fit into content bounds, use them exactly to align with grid width
        if total_tabs_w <= cb_avail:
            left_bound, right_bound = cb_left, cb_right
        else:
            # Fallback to panel bounds constrained by close button
            left_bound, right_bound = panel_left, panel_right
    else:
        left_bound, right_bound = panel_left, panel_right
    avail = max(0, right_bound - left_bound)
    if total_tabs_w <= avail:
        x = left_bound + (avail - total_tabs_w) // 2
    else:
        x = left_bound
    rects: list[pygame.Rect] = []
    for i, label in enumerate(labels):
        t = font.render(label, True, TEXT_COLOR)
        w = tab_widths[i]
        h = tab_h
        r = pygame.Rect(x, y, w, h)
        bg = (70, 70, 70) if i != active_index else (110, 90, 40)
        pygame.draw.rect(screen, bg, r, border_radius=8)
        pygame.draw.rect(screen, BORDER_COLOR, r, 2, border_radius=8)
        tx = r.x + (r.w - t.get_width()) // 2
        ty = r.y + (r.h - t.get_height()) // 2
        screen.blit(t, (tx, ty))
        rects.append(r)
        x += w + spacing
    # underline separator slightly below tabs for a clean gap, aligned to content bounds
    sep_y = y + tab_h + max(4, PADDING // 2)
    if content_bounds is not None:
        sep_left, sep_right = content_bounds
    else:
        sep_left, sep_right = left_bound, right_bound
    pygame.draw.line(screen, BORDER_COLOR, (sep_left, sep_y), (sep_right, sep_y), 1)
    # Total area height reserved for header + tabs + separator + top padding before grid
    # We add header height and its gap to the previous formula to keep symmetry.
    used_h = (header_h + header_top_gap) + (tab_h + PADDING + PADDING // 2)
    # Close button (already positioned)
    pygame.draw.rect(screen, CLOSE_BUTTON_COLOR, close_rect, border_radius=4)
    t_close = font.render("X", True, TEXT_COLOR)
    t_rect = t_close.get_rect(center=close_rect.center)
    screen.blit(t_close, t_rect)
    return rects, used_h, close_rect


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
        # The caller may have reserved a top_offset by translating panel_rect externally.
        # We keep backward compatibility by not adding a top_offset parameter here: the
        # panel_rect that arrives already includes the reserved space for tabs.
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
