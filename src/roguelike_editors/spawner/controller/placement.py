from __future__ import annotations

"""Lógica de entrada al modo de colocación (placement) de un template."""
from typing import Any


def begin_place_template(controller: Any, template_id: str) -> None:
    """Entra en modo de colocación para el `template_id` indicado."""
    try:
        controller.model.visible = True
        controller.model.placing_template_id = str(template_id)
        # Limpiar hold-to-focus para que la vista no oculte overlays
        try:
            controller.model.hold_focus_active = False
            controller.model.hold_focus_target_px = None
            world = getattr(getattr(controller, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
            if world is not None and hasattr(world, 'state'):
                setattr(world.state, 'spawner_hold_focus', False)
        except Exception:
            pass
        # Ocultar Templates Manager limpiando la herramienta activa
        try:
            tb = getattr(controller, 'spawner_toolbar', None)
            if tb and getattr(tb, 'model', None) is not None:
                tb.model.active_tool = None
        except Exception:
            pass
        # Suprimir input de juego mientras dura placement
        world = getattr(getattr(controller.game, 'ecs', None), 'ecs_world', None)
        if world is not None and hasattr(world, 'state'):
            setattr(world.state, 'spawner_input_suppressed', True)
    except Exception:
        pass
