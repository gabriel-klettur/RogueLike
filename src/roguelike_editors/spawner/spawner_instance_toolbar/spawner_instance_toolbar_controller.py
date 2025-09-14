from __future__ import annotations

from typing import Optional
import logging

from .spawner_instance_toolbar_model import SpawnerInstanceToolbarModel
from .spawner_instance_toolbar_view import SpawnerInstanceToolbarView
from .spawner_instance_toolbar_events import SpawnerInstanceToolbarEventHandler
from roguelike_editors.spawner.services.persistence import load_instances_json, write_instances_json


class SpawnerInstanceToolbarController:
    def __init__(self,
                 editor_controller,
                 model: Optional[SpawnerInstanceToolbarModel] = None,
                 view: Optional[SpawnerInstanceToolbarView] = None,
                 events: Optional[SpawnerInstanceToolbarEventHandler] = None) -> None:
        self.editor_controller = editor_controller
        self.model = model or SpawnerInstanceToolbarModel()
        self.view = view or SpawnerInstanceToolbarView()
        self.events = events or SpawnerInstanceToolbarEventHandler()

    def render(self, screen, *, anchor=None):
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        consumed = False
        # Ensure toolbar is constructed for hit-testing
        try:
            ensure = getattr(self.view, 'ensure_ready', None)
            if ensure:
                ensure(self.model)
        except AttributeError:
            pass
        toolbar = getattr(self.view, 'toolbar', None)
        if toolbar is not None:
            try:
                consumed = bool(toolbar.handle_event(event)) or consumed
            except AttributeError:
                pass
        consumed = self.events.handle_event(self, event) or consumed
        return consumed

    # Actions -----------------------------------------------------------------
    def on_add_spawner(self) -> None:
        """Enter/exit Add Mode with a simple dropdown of template ids.

        - Populates model.add_templates from spawners_templates.json
        - Toggles blinking on the Add button
        - Does NOT switch main toolbar tool; dropdown is owned by Instance Toolbar
        - Ensures Remove Mode is turned off
        - Suppresses gameplay input while open
        """
        # Toggle behavior
        active = bool(getattr(self.editor_controller.model, 'add_mode_active', False))
        new_state = not active
        # Load template ids when entering
        if new_state:
            try:
                from roguelike_editors.spawner.services.persistence import load_spawners_json
                tpls = load_spawners_json() or []
                ids = [str(t.get('id')) for t in tpls if isinstance(t, dict) and t.get('id')]
                self.model.add_templates = ids
            except (ImportError, OSError, ValueError, TypeError):
                self.model.add_templates = []
        else:
            # Leaving -> clear list
            self.model.add_templates = []
        # Set editor flag and mirror
        self.editor_controller.model.add_mode_active = new_state
        try:
            self.model.add_mode_active = new_state
        except AttributeError:
            pass
        # Tutorial pulse when entering Add Mode
        if new_state:
            try:
                setattr(self.editor_controller.model, 'tutorial_add_mode_on_pulse', True)
            except AttributeError:
                pass
        # Ensure remove mode is OFF when entering add mode
        if new_state:
            try:
                self.editor_controller.model.remove_mode_active = False
                self.model.remove_mode_active = False
                world = getattr(getattr(self.editor_controller, 'game', None), 'ecs', None)
                world = getattr(world, 'ecs_world', None)
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_remove_mode', False)
                    setattr(world.state, 'spawner_remove_candidate_eid', None)
            except AttributeError:
                pass
        # Suppress/re-enable gameplay input
        try:
            world = getattr(getattr(self.editor_controller, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
        except AttributeError:
            world = None
        if world and hasattr(world, 'state'):
            try:
                setattr(world.state, 'spawner_input_suppressed', bool(new_state))
            except AttributeError:
                pass
        # If user toggled OFF Add Mode via the button, bring back the Instances panel
        if not new_state:
            try:
                tb = getattr(self.editor_controller, 'spawner_toolbar', None)
                if tb and getattr(tb, 'model', None) is not None:
                    tb.model.active_tool = 'spawner_list'
            except AttributeError:
                pass

    def on_remove_spawner(self) -> None:
        """Toggle Remove Mode: when active, user can click a spawner center to delete with confirm."""
        # Flip mode
        active = bool(getattr(self.editor_controller.model, 'remove_mode_active', False))
        new_state = not active
        self.editor_controller.model.remove_mode_active = new_state
        try:
            # Mirror into toolbar model so view can blink
            self.model.remove_mode_active = new_state
        except AttributeError:
            pass
        # Tutorial pulse when turning ON Remove Mode
        if new_state:
            try:
                setattr(self.editor_controller.model, 'tutorial_remove_mode_on_pulse', True)
            except AttributeError:
                pass
        # Reflect to ECS world state for render systems
        try:
            world = getattr(getattr(self.editor_controller, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
        except AttributeError:
            world = None
        if world and hasattr(world, 'state'):
            try:
                setattr(world.state, 'spawner_remove_mode', new_state)
                # Clear any prior candidate when toggling
                if not new_state:
                    setattr(world.state, 'spawner_remove_candidate_eid', None)
            except AttributeError:
                pass
        # Leave placement mode if entering remove mode
        if new_state:
            try:
                self.editor_controller.model.placing_template_id = None
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except AttributeError:
                pass
            # Also exit Add Mode if it was active
            try:
                self.editor_controller.model.add_mode_active = False
                self.model.add_mode_active = False
                self.model.add_templates = []
                tb = getattr(self.editor_controller, 'spawner_toolbar', None)
                if tb and getattr(tb, 'model', None) is not None:
                    tb.model.active_tool = None
            except AttributeError:
                pass
        # Clear any pending confirms when turning off, and restore Instances panel
        if not new_state:
            try:
                self.editor_controller.model.pending_delete_confirm = None
            except AttributeError:
                pass
            # Activate 'spawner_list' so instances panel shows again
            try:
                tb = getattr(self.editor_controller, 'spawner_toolbar', None)
                if tb and getattr(tb, 'model', None) is not None:
                    tb.model.active_tool = 'spawner_list'
            except Exception:
                pass

    # Selection from dropdown -------------------------------------------------
    def on_add_template_selected(self, template_id: str) -> None:
        """User picked a template from the Add dropdown."""
        try:
            if template_id:
                # Close dropdown UI, but KEEP blinking (add_mode_active True) until placement ends
                self.model.add_templates = []
                # Begin placement
                self.editor_controller._begin_place_template(str(template_id))
                # Tutorial pulse: template chosen
                try:
                    setattr(self.editor_controller.model, 'tutorial_template_selected_pulse', True)
                except AttributeError:
                    pass
        except (AttributeError, TypeError, ValueError):
            pass


__all__ = ["SpawnerInstanceToolbarController"]
