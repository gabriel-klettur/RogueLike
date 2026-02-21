from __future__ import annotations

"""Funciones de ciclo de vida del Spawner Editor (set_game/toggle_visible)."""
from typing import Any


def set_game(controller: Any, game: Any) -> None:
    """Asocia el `game` al editor y lo propaga a delegados relevantes."""
    controller.game = game
    try:
        controller.events.set_game(game)
    except Exception:
        pass
    try:
        if hasattr(controller, 'instance_properties') and hasattr(controller.instance_properties, 'set_game'):
            controller.instance_properties.set_game(game)
    except Exception:
        pass


def toggle_visible(controller: Any) -> None:
    """Alterna visibilidad y aplica limpieza segura al ocultar."""
    try:
        controller.events.toggle_visible()
    except Exception:
        controller.model.visible = not controller.model.visible
    if not getattr(controller.model, 'visible', False):
        controller.model.hold_focus_active = False
        controller.model.hold_focus_target_px = None
        try:
            world = getattr(getattr(controller, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
            if world is not None and hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', False)
                setattr(world.state, 'spawner_hold_focus', False)
        except Exception:
            pass
        try:
            tb = getattr(getattr(controller, 'spawner_toolbar', None), 'model', None)
            if tb is not None:
                tb.active_tool = None
        except Exception:
            pass
        try:
            controller.spawner_manager.set_visible(False)
        except Exception:
            pass
        try:
            controller.spawner_instances.model.visible = False
        except Exception:
            pass
        try:
            controller.instance_properties.model.visible = False
        except Exception:
            pass
    else:
        # Editor was just opened: default to Instances tool so panels are visible
        try:
            tb = getattr(getattr(controller, 'spawner_toolbar', None), 'model', None)
            if tb is not None and getattr(tb, 'active_tool', None) is None:
                tb.active_tool = 'spawner_instances'
        except Exception:
            pass
