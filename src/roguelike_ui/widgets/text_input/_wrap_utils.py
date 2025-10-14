from __future__ import annotations

from typing import List, Tuple, Dict
import pygame

Token = Tuple[str, int, int]  # (token_text, start_idx, end_idx)
LineInfo = Dict[str, int | str]


def tokenize(text: str) -> List[Token]:
    """Split text into tokens preserving indices. Spaces/newlines kept as tokens.

    Returns a list of (token, start, end) tuples.
    """
    tokens: List[Token] = []
    i = 0
    buf = ''
    buf_start = 0
    while i < len(text):
        ch = text[i]
        if ch.isspace():
            if buf:
                tokens.append((buf, buf_start, buf_start + len(buf)))
                buf = ''
            tokens.append((ch, i, i + 1))
            i += 1
            buf_start = i
            continue
        if not buf:
            buf_start = i
        buf += ch
        i += 1
    if buf:
        tokens.append((buf, buf_start, buf_start + len(buf)))
    return tokens


def wrap_from_tokens(font: pygame.font.Font, tokens: List[Token], max_width: int) -> List[LineInfo]:
    """Build wrapped lines from tokens for a given max_width.

    Each line is a dict: {'text': str, 'start': int, 'end': int}.
    """
    lines: List[LineInfo] = []
    cur_text = ''
    cur_start = 0
    cur_end = 0
    for tok, s, e in tokens:
        proposal = cur_text + tok
        if font.size(proposal)[0] <= max_width or not cur_text:
            if not cur_text:
                cur_start = s
            cur_text = proposal
            cur_end = e
        else:
            lines.append({'text': cur_text, 'start': cur_start, 'end': cur_end})
            cur_text = tok
            cur_start = s
            cur_end = e
    if cur_text:
        lines.append({'text': cur_text, 'start': cur_start, 'end': cur_end})
    if not lines:
        lines = [{'text': '', 'start': 0, 'end': 0}]  # type: ignore[list-item]
    return lines
