from __future__ import annotations

import pygame


def draw_singleline(
    surface: pygame.Surface,
    *,
    font: pygame.font.Font,
    text: str,
    x: int,
    y: int,
    color: tuple[int, int, int],
    selection_start: int,
    selection_end: int,
    cursor: int,
    caret_visible: bool,
) -> pygame.Rect:
    """Draw a single-line text input field with optional selection highlight and caret.

    Returns the bounding rect of the rendered text.
    """
    text_w = font.size(text)[0]
    text_h = font.get_height()
    last_rect = pygame.Rect(x, y, text_w, text_h)

    # selection highlight
    if selection_start < selection_end:
        start_x = x + font.size(text[:selection_start])[0]
        sel_width = font.size(text[selection_start:selection_end])[0]
        sel_rect = pygame.Rect(start_x, y, sel_width, text_h)
        surface.fill((173, 216, 230), sel_rect)

    # render text
    txt_surf = font.render(text, True, color)
    surface.blit(txt_surf, (x, y))

    # caret
    if caret_visible:
        before = font.size(text[:cursor])[0]
        cx = x + before
        cy1 = y
        cy2 = y + text_h
        pygame.draw.line(surface, color, (cx, cy1), (cx, cy2), 1)

    return last_rect
