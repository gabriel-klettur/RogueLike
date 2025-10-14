from __future__ import annotations

import pygame
from typing import Tuple


class InlineTextEditor:
    """Small, reusable inline text editor for UI text fields.

    Manages caret movement, word-wise deletions (Ctrl+Backspace/Delete),
    selection-all toggle, and basic text insertion using pygame KEYDOWN events.
    """

    def __init__(self) -> None:
        self.active: bool = False
        self.text: str = ""
        self.caret: int = 0
        self.select_all: bool = False

    # ---------------- Lifecycle ----------------
    def begin(self, text: str) -> None:
        self.active = True
        self.text = text or ""
        self.caret = len(self.text)
        self.select_all = False

    def end(self) -> None:
        self.active = False
        self.select_all = False

    # ---------------- Input handling ----------------
    def handle_key(self, event: pygame.event.Event) -> None:
        if not self.active:
            return
        if event.type != pygame.KEYDOWN:
            return

        key = event.key
        mods = pygame.key.get_mods()
        ctrl = bool(mods & pygame.KMOD_CTRL)

        if key == pygame.K_BACKSPACE:
            if self.select_all:
                self.text = ""
                self.caret = 0
                self.select_all = False
                return
            if ctrl:
                self._delete_word_left()
            else:
                self._delete_left()
            return

        if key == pygame.K_DELETE:
            if self.select_all:
                self.text = ""
                self.caret = 0
                self.select_all = False
                return
            if ctrl:
                self._delete_word_right()
            else:
                self._delete_right()
            return

        if key in (pygame.K_LEFT, pygame.K_KP_4):
            if self.select_all:
                self.caret = 0
                self.select_all = False
                return
            if ctrl:
                self._move_word_left()
            else:
                self.caret = max(0, self.caret - 1)
            return

        if key in (pygame.K_RIGHT, pygame.K_KP_6):
            if self.select_all:
                self.caret = len(self.text)
                self.select_all = False
                return
            if ctrl:
                self._move_word_right()
            else:
                self.caret = min(len(self.text), self.caret + 1)
            return

        if key == pygame.K_HOME:
            self.caret = 0
            self.select_all = False
            return

        if key == pygame.K_END:
            self.caret = len(self.text)
            self.select_all = False
            return

        ch = getattr(event, "unicode", "") or ""
        if ch and ord(ch) >= 32:
            if self.select_all:
                self.text = ch
                self.caret = len(ch)
                self.select_all = False
            else:
                i = self.caret
                self.text = self.text[:i] + ch + self.text[i:]
                self.caret += len(ch)

    # ---------------- Mouse caret placement ----------------
    def set_caret_from_click(self, font: pygame.font.Font, rect: pygame.Rect, pos: Tuple[int, int]) -> None:
        if not rect.collidepoint(pos):
            return
        rel_x = pos[0] - rect.left - 4
        best_i = 0
        for i in range(1, len(self.text) + 1):
            w, _ = font.size(self.text[:i])
            if w <= rel_x:
                best_i = i
            else:
                break
        self.caret = best_i

    # ---------------- Helpers ----------------
    def _delete_left(self) -> None:
        if self.caret > 0 and self.text:
            i = self.caret
            self.text = self.text[: i - 1] + self.text[i:]
            self.caret -= 1

    def _delete_right(self) -> None:
        i = self.caret
        if i < len(self.text):
            self.text = self.text[:i] + self.text[i + 1 :]

    def _delete_word_left(self) -> None:
        i = self.caret
        j = i
        while j > 0 and self.text[j - 1].isspace():
            j -= 1
        while j > 0 and not self.text[j - 1].isspace():
            j -= 1
        self.text = self.text[:j] + self.text[i:]
        self.caret = j

    def _delete_word_right(self) -> None:
        i = self.caret
        j = i
        while j < len(self.text) and self.text[j].isspace():
            j += 1
        while j < len(self.text) and not self.text[j].isspace():
            j += 1
        self.text = self.text[:i] + self.text[j:]

    def _move_word_left(self) -> None:
        i = self.caret
        j = i
        while j > 0 and self.text[j - 1].isspace():
            j -= 1
        while j > 0 and not self.text[j - 1].isspace():
            j -= 1
        self.caret = j

    def _move_word_right(self) -> None:
        i = self.caret
        j = i
        while j < len(self.text) and self.text[j].isspace():
            j += 1
        while j < len(self.text) and not self.text[j].isspace():
            j += 1
        self.caret = j
