from __future__ import annotations

import logging
from typing import Optional

from .items_tutorial_panel_model import ItemsTutorialPanelModel
from .items_tutorial_panel_view import ItemsTutorialPanelView
from .items_tutorial_panel_events import ItemsTutorialPanelEventHandler

logger = logging.getLogger("items.tutorial")


class ItemsTutorialPanelController:
    def __init__(self, editor_controller, editor_view) -> None:
        self.editor = editor_controller
        self.editor_view = editor_view
        self.model = ItemsTutorialPanelModel()
        self.view = ItemsTutorialPanelView(editor_controller, self.model)
        self.events = ItemsTutorialPanelEventHandler(self, self.model)
        self._last_step_index: Optional[int] = None

    # State ------------------------------------------------------------------
    def is_active(self) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        self.model.active = True
        self.model.step_index = max(0, int(getattr(self.model, 'step_index', 0) or 0))
        self.model.checklist_done_by_step.clear()
        self._consume_all_pulses(reset_only=True)
        self._last_step_index = self.model.step_index
        # Wire toolbar views for precise highlights
        try:
            self.view.items_toolbar_view = getattr(self.editor, 'items_toolbar_view', None)
            self.view.add_remove_toolbar_view = getattr(self.editor, 'items_add_remove_view', None)
        except Exception:
            pass
        logger.info("[ItemsTutorial] Activated at step %s", self.model.step_index)

    def deactivate(self) -> None:
        self.model.active = False
        self.model.panel_rect = None
        self.model.button_rects.clear()
        self._consume_all_pulses(reset_only=True)
        self._last_step_index = None
        # Clear toolbar toggle if it points to tutorial
        try:
            tbm = getattr(self.editor, 'items_toolbar_model', None)
            if tbm is not None and getattr(tbm, 'active_tool', None) == 'tutorial_items':
                tbm.active_tool = None
        except Exception:
            pass
        logger.info("[ItemsTutorial] Deactivated")

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    # Integration -------------------------------------------------------------
    def handle_event(self, event) -> bool:
        return self.events.handle(event)

    def render(self, screen) -> None:
        if not self.is_active():
            return
        # track step changes
        try:
            cur_idx = int(getattr(self.model, 'step_index', 0) or 0)
        except Exception:
            cur_idx = 0
        if self._last_step_index is None or self._last_step_index != cur_idx:
            try:
                self.on_step_changed(cur_idx)
            except Exception:
                pass
            self._last_step_index = cur_idx
        # Update checklist progress
        try:
            self._update_checklist_progress()
        except Exception:
            pass
        # Refresh toolbar view references
        try:
            self.view.items_toolbar_view = getattr(self.editor, 'items_toolbar_view', None)
            self.view.add_remove_toolbar_view = getattr(self.editor, 'items_add_remove_view', None)
        except Exception:
            pass
        self.view.render(screen)

    def on_step_changed(self, new_idx: int) -> None:
        self.model.checklist_done_by_step[new_idx] = set()
        self._consume_all_pulses(reset_only=True)
        self._last_step_index = new_idx

    # Checklist ---------------------------------------------------------------
    def _update_checklist_progress(self) -> None:
        idx = int(getattr(self.model, 'step_index', 0) or 0)
        steps = getattr(self.model, 'steps', []) or []
        if not steps or idx < 0 or idx >= len(steps):
            return
        step = steps[idx]
        checklist = step.get('checklist', []) or []
        if not checklist:
            return
        done_set = self.model.checklist_done_by_step.get(idx)
        if done_set is None:
            done_set = set()
            self.model.checklist_done_by_step[idx] = done_set
        # Current editor state snapshots
        picker_visible = False
        try:
            picker_visible = bool(getattr(getattr(self.editor, 'picker_controller', None), 'model', None).visible)
        except Exception:
            picker_visible = False
        items_on_map_on = False
        try:
            items_on_map_on = (getattr(self.editor.items_toolbar_model, 'active_tool', None) == 'items_on_map')
        except Exception:
            pass
        # Pulses
        pulses = self._consume_all_pulses()
        # Evaluate
        for it in checklist:
            iid = it.get('id')
            if not iid or iid in done_set:
                continue
            kind = (it.get('condition') or {}).get('kind')
            ok = False
            if kind == 'always':
                ok = True
            elif kind == 'items_on_map_on':
                ok = items_on_map_on or pulses.get('items_on_map_on', False)
            elif kind == 'picker_visible':
                ok = picker_visible
            elif kind == 'add_mode_on':
                ok = pulses.get('add_mode_on', False)
            elif kind == 'spawn_selection':
                ok = pulses.get('spawn_selection', False)
            elif kind == 'item_spawned':
                ok = pulses.get('item_spawned', False)
            elif kind == 'remove_mode_on':
                ok = pulses.get('remove_mode_on', False)
            elif kind == 'item_deleted':
                ok = pulses.get('item_deleted', False)
            elif kind == 'edit_started':
                ok = pulses.get('edit_started', False)
            elif kind == 'properties_saved':
                ok = pulses.get('properties_saved', False)
            elif kind == 'assets_picker_open':
                ok = pulses.get('assets_picker_open', False)
            elif kind == 'asset_changed':
                ok = pulses.get('asset_changed', False)
            elif kind == 'add_system_mode_on':
                ok = pulses.get('add_system_mode_on', False)
            elif kind == 'add_system_confirm':
                ok = pulses.get('add_system_confirm', False)
            if ok:
                done_set.add(iid)

    def _consume_all_pulses(self, *, reset_only: bool = False) -> dict:
        m = getattr(self.editor, 'model', None)
        result = {
            'items_on_map_on': False,
            'add_mode_on': False,
            'spawn_selection': False,
            'item_spawned': False,
            'remove_mode_on': False,
            'item_deleted': False,
            'edit_started': False,
            'properties_saved': False,
            'assets_picker_open': False,
            'asset_changed': False,
            'add_system_mode_on': False,
            'add_system_confirm': False,
        }
        if m is None:
            return result
        for key in list(result.keys()):
            attr = f"tutorial_{key}_pulse"
            try:
                val = bool(getattr(m, attr, False))
                result[key] = val and not reset_only
                setattr(m, attr, False)
            except Exception:
                pass
        return result
