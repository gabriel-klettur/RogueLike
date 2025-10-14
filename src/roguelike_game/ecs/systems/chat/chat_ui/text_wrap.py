from __future__ import annotations

from typing import List
import pygame


def split_long_word_small(font: pygame.font.Font, word: str, max_width: int) -> List[str]:
    """Split a single long word to fit within max_width in pixels.

    Keeps characters contiguous, breaking at the closest safe point when the next
    character would exceed the available width.
    """
    if font.size(word)[0] <= max_width:
        return [word]
    out: List[str] = []
    buf = ""
    for ch in word:
        t = buf + ch
        if font.size(t)[0] <= max_width:
            buf = t
        else:
            if buf:
                out.append(buf)
            buf = ch
    if buf:
        out.append(buf)
    return out


def wrap_text_small(font: pygame.font.Font, text: str, max_width: int) -> List[str]:
    """Word-wrap text into lines that do not exceed max_width in pixels.

    Behavior mirrors the previous inline implementation used by chat UI.
    """
    if max_width <= 0:
        return [text]
    words = (text or "").split()
    lines: List[str] = []
    cur = ""
    for w in words:
        add = (cur + (" " if cur else "") + w).strip()
        if font.size(add)[0] <= max_width:
            cur = add
            continue
        if not cur:
            parts = split_long_word_small(font, w, max_width)
            # all but last are final lines
            lines.extend(parts[:-1])
            cur = parts[-1] if parts else w
        else:
            lines.append(cur)
            if font.size(w)[0] <= max_width:
                cur = w
            else:
                parts = split_long_word_small(font, w, max_width)
                lines.extend(parts[:-1])
                cur = parts[-1] if parts else w
    if cur:
        lines.append(cur)
    return lines
