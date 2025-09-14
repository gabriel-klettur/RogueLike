from __future__ import annotations

"""Funciones de foco (hold-to-focus) para el Spawner Editor."""
from types import SimpleNamespace
from typing import Any


def start_hold_focus(controller: Any, x_px: float, y_px: float) -> None:
    """Activa el modo hold-to-focus y centra la cámara en las coordenadas dadas."""
    controller.model.hold_focus_active = True
    controller.model.hold_focus_target_px = (float(x_px), float(y_px))
    # Suprime input de juego
    try:
        world = getattr(getattr(controller, 'game', None), 'ecs', None)
        world = getattr(world, 'ecs_world', None)
        if world is not None and hasattr(world, 'state'):
            setattr(world.state, 'spawner_input_suppressed', True)
            setattr(world.state, 'spawner_hold_focus', True)
    except Exception:
        pass
    # Centra cámara inmediatamente
    try:
        cam = getattr(controller.game, 'camera', None)
        if cam is not None:
            cam.update(SimpleNamespace(x=float(x_px), y=float(y_px)))
    except Exception:
        pass
    # Pulso tutorial
    try:
        setattr(controller.model, 'tutorial_hold_focus_started_pulse', True)
    except Exception:
        pass


essential_return = None  # marcador para linters que exigen símbolo exportable


def end_hold_focus(controller: Any) -> None:
    """Desactiva hold-to-focus y restaura input/cámara del juego."""
    controller.model.hold_focus_active = False
    controller.model.hold_focus_target_px = None
    # Restaura input
    try:
        world = getattr(getattr(controller, 'game', None), 'ecs', None)
        world = getattr(world, 'ecs_world', None)
        if world is not None and hasattr(world, 'state'):
            setattr(world.state, 'spawner_input_suppressed', False)
            setattr(world.state, 'spawner_hold_focus', False)
    except Exception:
        pass
    # Pulso tutorial
    try:
        setattr(controller.model, 'tutorial_hold_focus_ended_pulse', True)
    except Exception:
        pass
