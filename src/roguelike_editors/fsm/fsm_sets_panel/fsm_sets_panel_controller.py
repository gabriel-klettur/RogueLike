from __future__ import annotations
from typing import Optional, Dict, Any
import logging

from .fsm_sets_panel_model import FsmSetsPanelModel
from .fsm_sets_panel_view import FsmSetsPanelView
from roguelike_editors.fsm.services.fsm_persistence import (
    default_sets_path,
    load_sets,
    save_sets,
)
from roguelike_editors.fsm.services.fsm_id import new_id
from roguelike_editors.fsm.services.fsm_runtime_bridge import reload as bridge_reload

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_sets_panel.controller")


class FsmSetsPanelController:
    def __init__(self, model: Optional[FsmSetsPanelModel] = None, view: Optional[FsmSetsPanelView] = None) -> None:
        self.model = model or FsmSetsPanelModel()
        self.view = view or FsmSetsPanelView()

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        # Consume interactions over panel; update hover/selection of items
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        if not getattr(self.model, 'visible', False):
            return False
        rect = getattr(self.view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()

        # When confirmation modal is visible, only handle its buttons/keys and block other interactions
        if getattr(self.model, 'confirm_visible', False):
            # Mouse clicks inside Yes/No
            if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                # Always consume clicks over the panel while modal shown
                if rect.collidepoint(pos):
                    yes_r = getattr(self.view, 'confirm_yes_rect', None)
                    no_r = getattr(self.view, 'confirm_no_rect', None)
                    if yes_r is not None and yes_r.collidepoint(pos):
                        self._confirm_delete_yes()
                        return True
                    if no_r is not None and no_r.collidepoint(pos):
                        self._confirm_delete_no()
                        return True
                    return True
            # Keyboard shortcuts Y/Enter confirm, N/Escape cancel
            if et == pygame.KEYDOWN:
                key = getattr(event, 'key', None)
                if key in (pygame.K_RETURN, pygame.K_y):
                    self._confirm_delete_yes()
                    return True
                if key in (pygame.K_ESCAPE, pygame.K_n):
                    self._confirm_delete_no()
                    return True
            # Block everything else while modal
            if rect.collidepoint(pos):
                return True
            return False

        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                # Determine hovered button (clone/delete)
                try:
                    buttons: Dict[int, Dict[str, Any]] = getattr(self.view, 'row_button_rects', {}) or {}
                except Exception:
                    buttons = {}
                hb_row: Optional[int] = None
                hb_kind: Optional[str] = None
                for i, rects in buttons.items():
                    try:
                        clone_r = rects.get('clone')
                        del_r = rects.get('delete')
                        if clone_r is not None and clone_r.collidepoint(pos):
                            hb_row, hb_kind = int(i), 'clone'
                            break
                        if del_r is not None and del_r.collidepoint(pos):
                            hb_row, hb_kind = int(i), 'delete'
                            break
                    except Exception:
                        continue
                self.model.hovered_button_row = hb_row
                self.model.hovered_button_kind = hb_kind
                # Hover index based on simple row layout
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(self.model.items):
                    self.model.hovered_index = int(index)
                else:
                    self.model.hovered_index = None
                return True
            else:
                # Clear hover states when outside panel
                self.model.hovered_index = None
                self.model.hovered_button_row = None
                self.model.hovered_button_kind = None
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                # First, check per-row action buttons
                try:
                    buttons: Dict[int, Dict[str, Any]] = getattr(self.view, 'row_button_rects', {}) or {}
                except Exception:
                    buttons = {}
                # Iterate to find any hit
                for i, rects in buttons.items():
                    try:
                        clone_r = rects.get('clone')
                        del_r = rects.get('delete')
                        if clone_r is not None and clone_r.collidepoint(pos):
                            self._clone_row(int(i))
                            return True
                        if del_r is not None and del_r.collidepoint(pos):
                            self._ask_confirm_delete(int(i))
                            return True
                    except Exception:
                        continue
                # Otherwise treat as row selection
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(self.model.items):
                    self.model.selected_index = int(index)
                return True
        if et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            if rect.collidepoint(pos):
                return True
        return False

    # --- Operations -----------------------------------------------------------
    def _refresh_items_from_disk(self) -> None:
        """Reload sets.json and update model.items to match current disk state."""
        try:
            data = load_sets(default_sets_path())
            set_ids = [s.get('id', '?') for s in (data.get('sets') or [])]
            self.model.items = set_ids
        except Exception as ex:
            LOGGER.exception("[SetsPanel] failed to refresh items: %s", ex)

    def _clone_row(self, index: int) -> None:
        try:
            data = load_sets(default_sets_path())
            sets = data.get('sets') or []
            if not (0 <= index < len(sets)):
                return
            src = sets[index]
            # Build new unique id based on original id prefix
            existing_ids = {s.get('id') for s in sets if isinstance(s, dict)}
            base = str(src.get('id') or 'Set')
            new_set_id = new_id(base, set(existing_ids))
            # Deep-ish copy (shallow containers adequate, ids in states/transitions are strings)
            import copy
            dst = copy.deepcopy(src)
            dst['id'] = new_set_id
            # Friendly label
            try:
                label = dst.get('label') or base
                dst['label'] = f"{label} (copy)"
            except Exception:
                pass
            sets.append(dst)
            data['sets'] = sets
            # Persist
            save_sets(data, default_sets_path())
            try:
                bridge_reload()
            except Exception:
                pass
            # Refresh UI list and select new item
            self._refresh_items_from_disk()
            try:
                self.model.selected_index = self.model.items.index(new_set_id)
            except ValueError:
                # Fallback: select last
                self.model.selected_index = max(0, len(self.model.items) - 1)
        except Exception as ex:
            LOGGER.exception("[SetsPanel] clone failed for index=%s: %s", index, ex)

    def _ask_confirm_delete(self, index: int) -> None:
        try:
            if not (0 <= index < len(self.model.items)):
                return
            set_id = self.model.items[index]
            self.model.confirm_visible = True
            self.model.confirm_target_index = int(index)
            self.model.confirm_target_id = set_id
            self.model.confirm_text = f"Delete set '{set_id}'?\nThis action cannot be undone."
        except Exception:
            # Ensure modal not half-open
            self.model.confirm_visible = False
            self.model.confirm_target_index = None
            self.model.confirm_target_id = None
            self.model.confirm_text = ""

    def _confirm_delete_yes(self) -> None:
        target_id = getattr(self.model, 'confirm_target_id', None)
        if not target_id:
            self._confirm_delete_no()
            return
        try:
            data = load_sets(default_sets_path())
            sets = data.get('sets') or []
            # Remove by id
            new_sets = [s for s in sets if s.get('id') != target_id]
            data['sets'] = new_sets
            save_sets(data, default_sets_path())
            try:
                bridge_reload()
            except Exception:
                pass
            # Update UI list and selection
            prev_idx = getattr(self.model, 'selected_index', None)
            self._refresh_items_from_disk()
            # Adjust selection: if deleting the selected, move to previous index
            if prev_idx is not None:
                try:
                    if 0 <= int(prev_idx) < len(self.model.items) and self.model.items[int(prev_idx)] == target_id:
                        new_sel = min(int(prev_idx), max(0, len(self.model.items) - 1))
                        self.model.selected_index = new_sel if self.model.items else None
                    else:
                        # If target was before current selection, shift left by 1
                        try:
                            old_pos = int(prev_idx)
                            # If target existed before, and its original index < old_pos, decrement
                            # Recompute index of target in old list
                            # We don't have the old list here; conservative approach: clamp
                            self.model.selected_index = min(old_pos, max(0, len(self.model.items) - 1)) if self.model.items else None
                        except Exception:
                            pass
                except Exception:
                    self.model.selected_index = self.model.selected_index if self.model.items else None
            else:
                self.model.selected_index = self.model.selected_index if self.model.items else None
        except Exception as ex:
            LOGGER.exception("[SetsPanel] delete failed for set_id=%s: %s", target_id, ex)
        finally:
            self._confirm_delete_no()

    def _confirm_delete_no(self) -> None:
        # Dismiss modal and clear target
        self.model.confirm_visible = False
        self.model.confirm_target_index = None
        self.model.confirm_target_id = None
        self.model.confirm_text = ""


__all__ = ["FsmSetsPanelController"]
