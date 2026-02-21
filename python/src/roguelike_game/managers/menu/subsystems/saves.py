from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import List, Optional, Tuple

import pygame
from .saves_service import SaveService
from .inline_text_editor import InlineTextEditor
from .saves_input import handle_event as _handle_event

logger = logging.getLogger(__name__)


@dataclass
class SaveEntry:
    path: str
    label: str
    meta: dict


class SaveListManager:
    """Manages the Save/Load list view, inline rename, and delete confirm modal."""

    def __init__(self, game, renderer, screen: pygame.Surface) -> None:
        self.game = game
        self.renderer = renderer
        self.screen = screen
        # Services
        self.service = SaveService(game)
        self.editor = InlineTextEditor()
        # Data
        self.save_entries: List[dict] = []
        self.load_selected: int = 0
        # Layout caches
        self._saves_fixed_panel_size: Optional[Tuple[int, int]] = None
        self._saves_fixed_list_w: Optional[int] = None
        self._saves_fixed_details_w: Optional[int] = None
        self._saves_fixed_screen_size: Optional[Tuple[int, int]] = None
        # Scroll/hover/edit state
        self._saves_row_scroll_offset: int = 0
        self._saves_hovered_idx: Optional[int] = None
        self._saves_hover_details_name: bool = False
        self._saves_editing_name: bool = False
        self._saves_edit_name_text: str = ""
        self._saves_edit_caret: int = 0
        self._saves_select_all_edit: bool = False
        self._prev_key_repeat: Optional[Tuple[int, int]] = None
        self._last_click_time: float = 0.0
        self._last_click_pos: Optional[Tuple[int, int]] = None
        self._saves_hover_load_button: bool = False
        self._saves_hover_delete_button: bool = False
        self._saves_show_confirm_delete: bool = False
        self._saves_hover_confirm_yes: bool = False
        self._saves_hover_confirm_cancel: bool = False

    # ---------------- Public API ----------------
    def enter(self) -> None:
        self.refresh_list()
        self.load_selected = 0
        # Reset state
        self._saves_row_scroll_offset = 0
        self._saves_hovered_idx = None
        self._saves_hover_details_name = False
        self._saves_editing_name = False
        # Prepare fixed layout for current screen size
        self.compute_fixed_layout(self.screen)

    def handle_input(self, event) -> None:
        # Delegate full input processing to dedicated module
        return _handle_event(self, event)

    def draw(self, screen: pygame.Surface, *, panel_top_min: Optional[int], logo_layout) -> pygame.Rect:
        if self._saves_fixed_screen_size != screen.get_size():
            self.compute_fixed_layout(screen)
        items = [e["label"] for e in self.save_entries]
        meta = self.save_entries[self.load_selected]["meta"] if self.save_entries else {}
        detail_lines = self._format_save_details(meta)
        overlay_rect = self.renderer.draw_saves_panel(
            screen,
            selected=self.load_selected,
            items=items,
            detail_lines=detail_lines,
            row_scroll_offset=self._saves_row_scroll_offset,
            hovered_index=self._saves_hovered_idx,
            fixed_panel_size=self._saves_fixed_panel_size,
            fixed_list_width=self._saves_fixed_list_w,
            fixed_details_width=self._saves_fixed_details_w,
            hover_details_name=self._saves_hover_details_name,
            editing_name=self._saves_editing_name,
            edit_name_text=self._saves_edit_name_text,
            caret_pos=self._saves_edit_caret,
            hover_load_button=self._saves_hover_load_button,
            hover_delete_button=self._saves_hover_delete_button,
            select_all_edit=self._saves_select_all_edit,
            panel_top_min=panel_top_min if panel_top_min is not None else None,
        )
        if logo_layout is not None:
            surf, pos, _ = logo_layout
            screen.blit(surf, pos)
        if self._saves_show_confirm_delete:
            name = "-"
            if self.save_entries:
                entry = self.save_entries[self.load_selected]
                name = (entry.get("meta") or {}).get("name") or entry.get("label") or "-"
            lines = ["¿Borrar esta partida?", f"{name}", "Esta acción no se puede deshacer."]
            overlay_rect = self.renderer.draw_confirm_dialog(
                screen,
                lines,
                hover_yes=self._saves_hover_confirm_yes,
                hover_cancel=self._saves_hover_confirm_cancel,
            )
        return overlay_rect

    # ---------------- Internal helpers ----------------
    def refresh_list(self) -> None:
        self.save_entries = self.service.list_saves()

    def compute_fixed_layout(self, screen: pygame.Surface) -> None:
        font = self.renderer.font
        list_max_w = 0
        for e in self.save_entries:
            tw, _ = font.size(e.get("label", ""))
            list_max_w = max(list_max_w, tw)
        details_max_w = 0
        for e in self.save_entries:
            lines = self._format_save_details(e.get("meta") or {})
            for line in lines:
                tw, _ = font.size(line)
                details_max_w = max(details_max_w, tw)
        if not self.save_entries:
            details_max_w = max(details_max_w, font.size("Sin metadatos")[0])
            list_max_w = max(list_max_w, font.size("-")[0])
        col_gap = 32
        w = self.renderer.padding_x * 2 + list_max_w + col_gap + details_max_w + 12
        min_rows = 8
        inner_rows_h = min_rows * self.renderer.line_height + max(0, (min_rows - 1)) * self.renderer.item_gap
        h = self.renderer.padding_y * 2 + inner_rows_h
        sw, sh = screen.get_size()
        w = min(w, int(sw * 0.95))
        h = min(h, int(sh * 0.85))
        self._saves_fixed_panel_size = (w, h)
        self._saves_fixed_list_w = list_max_w
        self._saves_fixed_details_w = details_max_w
        self._saves_fixed_screen_size = (sw, sh)

    def _format_save_details(self, meta: dict) -> List[str]:
        return self.service.format_meta_lines(meta or {})

    def _load_selected_save(self) -> None:
        if not self.save_entries:
            return
        entry = self.save_entries[self.load_selected]
        path = entry["path"]
        # Delegate loading to service (handles all side-effects and logging)
        self.service.load_save(path)

    # ---------------- Inline rename ----------------
    def _begin_edit_save_name(self) -> None:
        if not self.save_entries:
            return
        entry = self.save_entries[self.load_selected]
        current = (entry.get("meta") or {}).get("name") or entry.get("label") or ""
        self._saves_editing_name = True
        # Start inline editor and mirror its state for renderer
        self.editor.begin(str(current))
        self._saves_edit_name_text = self.editor.text
        self._saves_edit_caret = self.editor.caret
        self._saves_select_all_edit = self.editor.select_all
        try:
            self._prev_key_repeat = pygame.key.get_repeat() if hasattr(pygame.key, "get_repeat") else None
            pygame.key.set_repeat(350, 40)
        except Exception:
            pass

    def _set_caret_from_click(self, pos: Tuple[int, int]) -> None:
        try:
            layout = getattr(self.renderer, "last_saves_layout", None)
            if not layout:
                return
            name_rect = layout.get("details_name_rect")
            if not name_rect or not name_rect.collidepoint(pos):
                return
            # Delegate caret placement to inline editor
            self.editor.text = self._saves_edit_name_text
            self.editor.caret = self._saves_edit_caret
            self.editor.set_caret_from_click(self.renderer.font, name_rect, pos)
            self._saves_edit_caret = self.editor.caret
            self._saves_select_all_edit = False
        except Exception:
            pass

    def _commit_save_rename(self) -> None:
        if not self.save_entries:
            self._end_edit_save_name(cancel=True)
            return
        new_name = (self._saves_edit_name_text or "").strip()
        if not new_name:
            self._end_edit_save_name(cancel=True)
            return
        entry = self.save_entries[self.load_selected]
        path = entry.get("path")
        try:
            meta = self.service.rename_save(str(path), new_name)
            entry["label"] = new_name
            entry["meta"] = meta
            self._end_edit_save_name()
            self.compute_fixed_layout(self.screen)
        except Exception as e:
            logger.warning("No se pudo guardar el nuevo nombre del guardado: %s", e)
            self._end_edit_save_name(cancel=True)

    def _end_edit_save_name(self, cancel: bool = False) -> None:
        self._saves_editing_name = False
        self._saves_select_all_edit = False
        try:
            self.editor.end()
        except Exception:
            pass
        try:
            if self._prev_key_repeat and all(isinstance(x, int) for x in self._prev_key_repeat):
                delay, interval = self._prev_key_repeat
                pygame.key.set_repeat(delay, interval)
            else:
                pygame.key.set_repeat(0)
        except Exception:
            pass

    # ---------------- Delete ----------------
    def _confirm_delete_selected_save(self) -> None:
        if not self.save_entries:
            self._saves_show_confirm_delete = False
            return
        idx = self.load_selected
        path = self.save_entries[idx].get("path")
        try:
            if path:
                self.service.delete_save(path)
        except Exception as e:
            logger.warning("No se pudo borrar el guardado %s: %s", path, e)
        self.refresh_list()
        if not self.save_entries:
            self.load_selected = 0
            self._saves_show_confirm_delete = False
            self._saves_hover_confirm_yes = False
            self._saves_hover_confirm_cancel = False
            self._saves_hover_delete_button = False
            self._saves_row_scroll_offset = 0
            self._saves_hovered_idx = None
            self._saves_hover_details_name = False
            self._saves_editing_name = False
            return
        else:
            self.load_selected = min(self.load_selected, len(self.save_entries) - 1)
        self._saves_show_confirm_delete = False
        self._saves_hover_confirm_yes = False
        self._saves_hover_confirm_cancel = False
        self._saves_hover_delete_button = False
        try:
            self.compute_fixed_layout(self.screen)
        except Exception:
            pass
