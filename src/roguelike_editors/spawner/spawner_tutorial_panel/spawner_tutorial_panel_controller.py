from __future__ import annotations

import logging
from typing import Optional

from .spawner_tutorial_panel_model import SpawnerTutorialPanelModel
from .spawner_tutorial_panel_view import SpawnerTutorialPanelView
from .spawner_tutorial_panel_events import SpawnerTutorialPanelEventHandler

logger = logging.getLogger("spawner.tutorial")


class SpawnerTutorialPanelController:
    def __init__(self, editor_controller, editor_view) -> None:
        self.editor = editor_controller
        self.editor_view = editor_view
        self.model = SpawnerTutorialPanelModel()
        self.view = SpawnerTutorialPanelView(editor_controller, self.model, editor_view)
        self.events = SpawnerTutorialPanelEventHandler(self, self.model)
        # Step tracking to clear hover/highlights between steps if needed
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
        try:
            # Wire toolbar views for precise highlights
            if hasattr(self.editor, 'spawner_toolbar') and getattr(self.editor.spawner_toolbar, 'view', None) is not None:
                self.view.spawner_toolbar_view = self.editor.spawner_toolbar.view
            if hasattr(self.editor, 'instance_toolbar') and getattr(self.editor.instance_toolbar, 'view', None) is not None:
                self.view.instance_toolbar_view = self.editor.instance_toolbar.view
        except Exception:
            pass
        logger.info("[SpawnerTutorial] Activated at step %s", self.model.step_index)

    def deactivate(self) -> None:
        self.model.active = False
        self.model.panel_rect = None
        self.model.button_rects.clear()
        self._consume_all_pulses(reset_only=True)
        self._last_step_index = None
        # Clear toolbar toggle if it points to tutorial
        try:
            tbm = getattr(getattr(self.editor, 'spawner_toolbar', None), 'model', None)
            if tbm is not None and getattr(tbm, 'active_tool', None) == 'tutorial_spawner':
                tbm.active_tool = None
        except Exception:
            pass
        logger.info("[SpawnerTutorial] Deactivated")

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
        # If external step index changed, reset per-step progress
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
        # Update checklist progress based on editor state and pulses
        try:
            self._update_checklist_progress()
        except Exception:
            pass
        self.view.render(screen)

    def on_step_changed(self, new_idx: int) -> None:
        # Reset per-step completion and consume pulses so user can redo actions consciously
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
        # Consume pulses into local variables (single-use flags)
        pulses = self._consume_all_pulses()
        # Evaluate conditions
        for it in checklist:
            iid = it.get('id')
            if not iid or iid in done_set:
                continue
            kind = (it.get('condition') or {}).get('kind')
            ok = False
            # Map conditions to pulses or editor state
            if kind == 'always':
                ok = True
            elif kind == 'instances_open':
                ok = pulses.get('instances_open', False)
            elif kind == 'manager_open':
                ok = pulses.get('manager_open', False)
            elif kind == 'instance_selected':
                ok = pulses.get('instance_selected', False)
            elif kind == 'hold_focus_started':
                ok = pulses.get('hold_focus_started', False)
            elif kind == 'hold_focus_ended':
                ok = pulses.get('hold_focus_ended', False)
            elif kind == 'add_mode_on':
                ok = pulses.get('add_mode_on', False)
            elif kind == 'template_selected':
                ok = pulses.get('template_selected', False)
            elif kind == 'placement_done':
                ok = pulses.get('placement_done', False)
            elif kind == 'drag_started':
                ok = pulses.get('drag_started', False)
            elif kind == 'persist_drop':
                ok = pulses.get('persist_drop', False)
            elif kind == 'zone_confirm_open':
                ok = pulses.get('zone_confirm_open', False)
            elif kind == 'zone_confirm_yes':
                ok = pulses.get('zone_confirm_yes', False)
            elif kind == 'zone_confirm_no':
                ok = pulses.get('zone_confirm_no', False)
            elif kind == 'remove_mode_on':
                ok = pulses.get('remove_mode_on', False)
            elif kind == 'delete_confirm_open':
                ok = pulses.get('delete_confirm_open', False)
            elif kind == 'delete_done':
                ok = pulses.get('delete_done', False)
            elif kind == 'properties_saved':
                ok = pulses.get('properties_saved', False)
            if ok:
                done_set.add(iid)

    def _consume_all_pulses(self, *, reset_only: bool = False) -> dict:
        """Read and optionally clear all tutorial pulses from editor.model.
        Returns a dict with booleans for current frame consumption.
        """
        m = getattr(self.editor, 'model', None)
        result = {
            'instances_open': False,
            'manager_open': False,
            'instance_selected': False,
            'hold_focus_started': False,
            'hold_focus_ended': False,
            'add_mode_on': False,
            'template_selected': False,
            'placement_done': False,
            'drag_started': False,
            'persist_drop': False,
            'zone_confirm_open': False,
            'zone_confirm_yes': False,
            'zone_confirm_no': False,
            'remove_mode_on': False,
            'delete_confirm_open': False,
            'delete_done': False,
            'properties_saved': False,
        }
        if m is None:
            return result
        for key in list(result.keys()):
            attr = f"tutorial_{key}_pulse"
            try:
                val = bool(getattr(m, attr, False))
                result[key] = val and not reset_only
                # Clear after reading
                setattr(m, attr, False)
            except Exception:
                pass
        return result
