from __future__ import annotations

from typing import Optional
import pygame
from roguelike_ui.widgets.text_input import TextInput


class RowEditMixin:
    def begin_edit_row(self, row_index: int) -> None:
        rows = self.get_rows()
        if not (0 <= row_index < len(rows)):
            return
        key, value_str = rows[row_index]
        self.model.editing_key = key
        self.model.editing_row_index = row_index
        # Initialize last edit key with the row key being edited
        try:
            self._last_edit_key = str(key)
        except Exception:
            self._last_edit_key = key
        if getattr(self, '_text_input', None) is None:
            font = pygame.font.SysFont(None, 18)
            self._text_input = TextInput(font)
        self._text_input.activate(value_str, select_all=True)

    def is_editing(self) -> bool:
        return self.model.editing_key is not None and getattr(self, '_text_input', None) is not None and self._text_input.active

    def get_text_input(self) -> Optional[TextInput]:
        return getattr(self, '_text_input', None)

    def commit_edit_if_finished(self) -> bool:
        if self.model.editing_key and getattr(self, '_text_input', None) and not self._text_input.active:
            key_path = self.model.editing_key
            new_text = self._text_input.text
            # Parse new value and apply to selected_instance
            new_value = self._parse_value(new_text, key_path)
            self._apply_edit(key_path, new_value)
            # Persist to spawners_instances.json
            # Remember the changed key path for callbacks
            try:
                self._last_edit_key = str(key_path)
            except Exception:
                self._last_edit_key = key_path
            self._persist_instance()
            # Clear editing state and refresh rows
            self.model.editing_key = None
            self.model.editing_row_index = None
            self._rows = self._flatten_instance()
            return True
        return False
