from __future__ import annotations

from dataclasses import dataclass
from typing import Optional
import logging


@dataclass(frozen=True)
class UIState:
    """Snapshot of transient UI state computed from the controller's model/toolbar.

    Centraliza condiciones de visibilidad y flags transitorios para mantener
    `handle_event()` y `render()` simples y consistentes.
    """
    active_tool: Optional[str]
    hold: bool
    placing_active: bool
    manager_visible: bool
    instances_visible: bool


def compute_ui_state(controller) -> UIState:
    """Compute current UI/visibility state based on controller model and toolbar."""
    try:
        active_tool = getattr(getattr(controller, 'spawner_toolbar', None), 'model', None)
        active_tool = getattr(active_tool, 'active_tool', None)
    except Exception:
        active_tool = None
    model = controller.model
    hold = bool(getattr(model, 'hold_focus_active', False))
    placing_active = bool(getattr(model, 'placing_template_id', None))
    manager_visible = bool(model.visible and (active_tool == 'spawner_manager') and not hold)
    instances_visible = bool(
        model.visible
        and (active_tool == 'spawner_instances')
        and not hold
        and not getattr(model, 'add_mode_active', False)
        and not getattr(model, 'remove_mode_active', False)
        and not placing_active
    )
    state = UIState(
        active_tool=active_tool,
        hold=hold,
        placing_active=placing_active,
        manager_visible=manager_visible,
        instances_visible=instances_visible,
    )
    # Log only if state changed vs last logged
    try:
        prev = getattr(controller, '_last_ui_state_log', None)
        curr = (active_tool, hold, placing_active, manager_visible, instances_visible)
        if prev != curr:
            logging.getLogger(__name__).debug(
                "[Spawner.UI] compute_ui_state: active_tool=%s hold=%s placing=%s manager_visible=%s instances_visible=%s",
                active_tool, hold, placing_active, manager_visible, instances_visible,
            )
    except Exception:
        pass
    return state


def apply_ui_state(controller, state: UIState) -> None:
    """Synchronize panels visibility and global flags from a `UIState`."""
    # Gate subpanels by editor visibility
    # Log only if state changed vs last logged, then update marker
    try:
        prev = getattr(controller, '_last_ui_state_log', None)
        curr = (state.active_tool, state.hold, state.placing_active, state.manager_visible, state.instances_visible)
        if prev != curr:
            logging.getLogger(__name__).debug(
                "[Spawner.UI] apply_ui_state: set manager_visible=%s instances_visible=%s",
                state.manager_visible, state.instances_visible,
            )
        setattr(controller, '_last_ui_state_log', curr)
    except Exception:
        pass
    try:
        controller.spawner_manager.set_visible(bool(state.manager_visible))
    except Exception:
        pass
    # Instances panel visibility (also mirrored to its model for view short-circuit)
    try:
        controller.spawner_instances.model.visible = bool(state.instances_visible)
    except Exception:
        pass
    # Instance Toolbar visible whenever the editor is visible
    try:
        controller.instance_toolbar.model.visible = bool(controller.model.visible)
    except Exception:
        pass
    # Instance Properties visibility depends on selection and Instances visibility
    try:
        sel = controller.spawner_instances.get_selected_instance()
        controller.instance_properties.model.visible = bool(state.instances_visible and sel is not None)
    except Exception:
        pass
    # Ensure properties panel binds data whenever it is visible
    try:
        sel = controller.spawner_instances.get_selected_instance()
        if state.instances_visible and sel is not None:
            # Bind only if unset or different id to avoid needless resets
            cur = getattr(controller.instance_properties.model, 'selected_instance', None)
            cur_id = None
            new_id = None
            try:
                if isinstance(cur, dict) and cur.get('id') is not None:
                    cur_id = str(cur.get('id'))
            except Exception:
                cur_id = None
            try:
                if isinstance(sel, dict) and sel.get('id') is not None:
                    new_id = str(sel.get('id'))
            except Exception:
                new_id = None
            if cur is None or cur_id != new_id:
                idx = getattr(getattr(controller.spawner_instances, 'model', None), 'selected_index', None)
                controller.instance_properties.set_instance(sel, index=idx)
    except Exception:
        pass
    # Global world flag so gameplay input is suppressed while editor activity is present
    try:
        world = getattr(getattr(controller, 'game', None), 'ecs', None)
        world = getattr(world, 'ecs_world', None)
        if world is not None and hasattr(world, 'state'):
            setattr(
                world.state,
                'spawner_editor_active',
                bool(controller.model.visible and (
                    state.manager_visible or state.instances_visible or state.placing_active or state.hold
                )),
            )
    except Exception:
        pass


def update_tutorial_pulses(controller, state: UIState) -> None:
    """Emit one-frame tutorial pulses when panels change visibility."""
    try:
        if (
            controller.model.visible and (state.active_tool == 'spawner_instances') and not state.hold
            and not getattr(controller.model, 'add_mode_active', False)
            and not getattr(controller.model, 'remove_mode_active', False)
            and not state.placing_active and not controller._instances_visible_last
        ):
            setattr(controller.model, 'tutorial_instances_open_pulse', True)
    except Exception:
        pass
    try:
        if state.manager_visible and not controller._manager_visible_last:
            setattr(controller.model, 'tutorial_manager_open_pulse', True)
    except Exception:
        pass


def maybe_refresh_instances_on_first_show(controller, state: UIState) -> None:
    """Refresh instances list when the Instances tool becomes visible."""
    if (controller.model.visible and (state.active_tool == 'spawner_instances')) and not controller._instances_visible_last:
        try:
            controller.spawner_instances.refresh_from_disk()
        except Exception:
            pass
