from __future__ import annotations

import pygame


def handle_key(ti, event: pygame.event.Event) -> bool:
    """Process KEYDOWN for the TextInput-like object `ti`.

    Expects attributes: text, cursor, selection_start, selection_end, active, font, blink_interval.
    Mutates `ti` in place. Returns True if consumed.
    """
    mod = event.mod if hasattr(event, 'mod') else 0
    # Enter commits edit (deactivate)
    if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
        ti.active = False
        return True
    # Ctrl+A: select all
    if event.key == pygame.K_a and (mod & pygame.KMOD_CTRL):
        ti.selection_start = 0
        ti.selection_end = len(ti.text)
        ti.cursor = ti.selection_end
        return True
    # Home
    if event.key == pygame.K_HOME:
        new_cursor = 0
        if mod & pygame.KMOD_SHIFT:
            anchor = ti.selection_start
            ti.selection_start = min(anchor, new_cursor)
            ti.selection_end = max(anchor, new_cursor)
        else:
            ti.selection_start = new_cursor
            ti.selection_end = new_cursor
        ti.cursor = new_cursor
        return True
    # End
    if event.key == pygame.K_END:
        new_cursor = len(ti.text)
        if mod & pygame.KMOD_SHIFT:
            anchor = ti.selection_start
            ti.selection_start = min(anchor, new_cursor)
            ti.selection_end = max(anchor, new_cursor)
        else:
            ti.selection_start = new_cursor
            ti.selection_end = new_cursor
        ti.cursor = new_cursor
        return True
    # Left arrow
    if event.key == pygame.K_LEFT:
        new_cursor = max(0, ti.cursor - 1)
        if mod & pygame.KMOD_SHIFT:
            ti.cursor = new_cursor
            ti.selection_end = ti.cursor
        else:
            ti.cursor = new_cursor
            ti.selection_start = ti.cursor
            ti.selection_end = ti.cursor
        return True
    # Right arrow
    if event.key == pygame.K_RIGHT:
        new_cursor = min(len(ti.text), ti.cursor + 1)
        if mod & pygame.KMOD_SHIFT:
            ti.cursor = new_cursor
            ti.selection_end = ti.cursor
        else:
            ti.cursor = new_cursor
            ti.selection_start = ti.cursor
            ti.selection_end = ti.cursor
        return True
    # Backspace
    if event.key == pygame.K_BACKSPACE:
        if ti.selection_start != ti.selection_end:
            i0, i1 = sorted((ti.selection_start, ti.selection_end))
            ti.text = ti.text[:i0] + ti.text[i1:]
            ti.cursor = i0
        elif ti.cursor > 0:
            ti.text = ti.text[:ti.cursor - 1] + ti.text[ti.cursor:]
            ti.cursor -= 1
        ti.selection_start = ti.cursor
        ti.selection_end = ti.cursor
        return True
    # Delete (Supr key)
    if event.key == pygame.K_DELETE:
        if ti.selection_start != ti.selection_end:
            i0, i1 = sorted((ti.selection_start, ti.selection_end))
            ti.text = ti.text[:i0] + ti.text[i1:]
            ti.cursor = i0
        elif ti.cursor < len(ti.text):
            ti.text = ti.text[:ti.cursor] + ti.text[ti.cursor + 1:]
        ti.selection_start = ti.cursor
        ti.selection_end = ti.cursor
        return True
    # Character insertion
    ch = event.unicode
    if ch:
        if ti.selection_start != ti.selection_end:
            i0, i1 = sorted((ti.selection_start, ti.selection_end))
            ti.text = ti.text[:i0] + ch + ti.text[i1:]
            ti.cursor = i0 + len(ch)
        else:
            i = ti.cursor
            ti.text = ti.text[:i] + ch + ti.text[i:]
            ti.cursor += len(ch)
        ti.selection_start = ti.cursor
        ti.selection_end = ti.cursor
    return True


def handle_mouse(ti, event: pygame.event.Event) -> bool:
    """Process primary MOUSEBUTTONDOWN to reposition caret. Returns True if consumed.

    Uses ti.last_rect, ti._wrap_lines, ti._wrap_x, ti._wrap_y, ti._wrap_line_h.
    """
    if getattr(event, 'button', None) != 1:
        return False
    mx, my = event.pos
    if not hasattr(ti, 'last_rect') or not ti.last_rect.collidepoint(mx, my):
        return False
    # If we have wrapping info from last draw_wrapped(), use it
    if getattr(ti, '_wrap_lines', None) and getattr(ti, '_wrap_max_w', 0) > 0:
        rel_x = mx - ti._wrap_x
        rel_y = my - ti._wrap_y
        line_h = ti._wrap_line_h
        line_idx = max(0, min(len(ti._wrap_lines) - 1, rel_y // max(1, line_h)))
        line = ti._wrap_lines[int(line_idx)]
        lx = max(0, int(rel_x))
        # find nearest char within this line
        best_i = line['start']
        best_diff = abs(lx)
        segment = line['text']
        for i in range(1, len(segment) + 1):
            pos = ti.font.size(segment[:i])[0]
            diff = abs(lx - pos)
            if diff < best_diff:
                best_diff = diff
                best_i = line['start'] + i
        ti.cursor = max(line['start'], min(line['end'], best_i))
    else:
        # single-line fallback
        rel_x = mx - ti.last_draw_x
        best_i = 0
        best_diff = abs(rel_x)
        for i in range(1, len(ti.text) + 1):
            pos = ti.font.size(ti.text[:i])[0]
            diff = abs(rel_x - pos)
            if diff < best_diff:
                best_diff = diff
                best_i = i
        ti.cursor = best_i
    ti.selection_start = ti.cursor
    ti.selection_end = ti.cursor
    return True
