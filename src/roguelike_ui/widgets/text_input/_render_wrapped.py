from __future__ import annotations

from typing import List, Dict, Tuple
import pygame

from ._wrap_utils import tokenize, wrap_from_tokens, Token

Line = Dict[str, int | str]


def draw_wrapped_block(
    surface: pygame.Surface,
    *,
    font: pygame.font.Font,
    text: str,
    x: int,
    y: int,
    max_width: int,
    color: Tuple[int, int, int],
    align_bottom: bool,
    selection_start: int,
    selection_end: int,
    cursor: int,
    caret_visible: bool,
) -> tuple[pygame.Rect, List[Line], int, int]:
    """Draw a wrapped text input block with selection and optional caret.

    Returns (last_rect, lines, start_y, line_h) for callers to cache metadata.
    """
    tokens: List[Token] = tokenize(text)
    lines: List[Line] = wrap_from_tokens(font, tokens, max_width)

    line_h = font.get_linesize()
    total_h = line_h * len(lines)
    start_y = y - (total_h - font.get_height()) if align_bottom else y

    # Define the total area covered by the wrapped block
    last_rect = pygame.Rect(x, start_y, max_width, total_h)

    # Selection highlight per line
    i0, i1 = sorted((selection_start, selection_end))
    for li, line in enumerate(lines):
        ly = start_y + li * line_h
        # selection range overlap in this line
        sel_s = max(line['start'], i0)  # type: ignore[index]
        sel_e = min(line['end'], i1)    # type: ignore[index]
        if sel_e > sel_s:
            pre = str(line['text'])[:max(0, sel_s - int(line['start']))]
            mid = str(line['text'])[max(0, sel_s - int(line['start'])):max(0, sel_e - int(line['start']))]
            pre_w = font.size(pre)[0]
            mid_w = font.size(mid)[0]
            sel_rect = pygame.Rect(x + pre_w, ly, mid_w, font.get_height())
            surface.fill((173, 216, 230), sel_rect)

    # Draw text lines
    for li, line in enumerate(lines):
        ly = start_y + li * line_h
        txt_surf = font.render(str(line['text']), True, color)
        surface.blit(txt_surf, (x, ly))

    # Caret blinking
    if caret_visible:
        # Find caret line
        caret_line_idx = 0
        line_obj: Line = lines[-1]
        for li, line in enumerate(lines):
            if int(line['start']) <= cursor <= int(line['end']):  # type: ignore[index]
                caret_line_idx = li
                line_obj = line
                break
        within = max(0, cursor - int(line_obj['start']))  # type: ignore[index]
        cx_off = font.size(str(line_obj['text'])[:within])[0]
        cx = x + cx_off
        cy1 = start_y + caret_line_idx * line_h
        cy2 = cy1 + font.get_height()
        pygame.draw.line(surface, color, (cx, cy1), (cx, cy2), 1)

    return last_rect, lines, start_y, line_h
