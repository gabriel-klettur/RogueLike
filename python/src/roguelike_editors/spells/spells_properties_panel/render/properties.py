from __future__ import annotations

import pygame
from typing import List, Tuple


def render_properties_section(
    screen: pygame.Surface,
    font: pygame.font.Font,
    model: object,
    view_rect: pygame.Rect,
    entries: List[Tuple[str, str]],
    active_id: str | None,
    blink_interval_ms: int,
    truncate_text,
) -> List[Tuple[pygame.Rect, str]]:
    """Render the properties list inside view_rect.

    Returns a list of (rect, full_text) for truncated lines to show tooltips.
    Side effects:
      - Sets model.property_entries
      - Sets model.content_height
    """
    truncated_entries: List[Tuple[pygame.Rect, str]] = []
    model.property_entries = []

    max_line_w = view_rect.w
    font_h_local = font.get_height()
    line_h = font_h_local + 2

    # Build text lines
    lines: list[Tuple[str, bool]] = []
    title_text = active_id if active_id else ""
    if title_text:
        lines.append((title_text, True))
    for k, v in entries:
        text_content = f"{k}: {v}"
        lines.append((text_content, False))

    model.content_height = len(lines) * line_h

    y = view_rect.y - getattr(model, "scroll_y", 0)
    for text, is_title in lines:
        if y + line_h < view_rect.y:
            y += line_h
            continue
        if y > view_rect.bottom:
            break
        color = (255, 255, 0) if (is_title or text.startswith("name:")) else (200, 200, 200)
        display_text = truncate_text(font, text, max_line_w)
        txt_surf = font.render(display_text, True, color)
        screen.blit(txt_surf, (view_rect.x, y))
        if not is_title and ": " in text:
            key = text.split(": ", 1)[0]
            line_rect = pygame.Rect(view_rect.x, y, min(txt_surf.get_width(), max_line_w), font_h_local)
            model.property_entries.append((line_rect, key))
            if display_text != text:
                truncated_entries.append((line_rect, text))
        y += line_h

    # Decorations for edit/hover state
    if getattr(model, "editing_property", None):
        for rect_prop, key_prop in getattr(model, "property_entries", []):
            if key_prop == model.editing_property:
                ed_rect = rect_prop.inflate(4, 0)
                pygame.draw.rect(screen, (128, 0, 128), ed_rect, 2)
                t = pygame.time.get_ticks()
                if (t % blink_interval_ms) < (blink_interval_ms // 2):
                    pre = f"{key_prop}: "
                    caret_x = ed_rect.x + font.size(pre + getattr(model, "editing_text", "")[:getattr(model, "editing_cursor", 0)])[0]
                    pygame.draw.line(screen, (255, 255, 255), (caret_x, ed_rect.y), (caret_x, ed_rect.y + font.get_height()), 2)
                break
    else:
        target_key = getattr(model, "hovered_property", None) or getattr(model, "focused_property", None)
        if target_key:
            for rect_prop, key_prop in getattr(model, "property_entries", []):
                if key_prop == target_key:
                    hl_rect = rect_prop.inflate(4, 0)
                    pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                    break

    return truncated_entries
