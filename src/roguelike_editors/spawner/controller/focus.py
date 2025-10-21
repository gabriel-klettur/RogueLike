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
            prev = getattr(controller.model, '_hold_focus_prev_camera', None)
            if not prev:
                snap = {}
                for attr in ('offset_x', 'offset_y', 'zoom'):
                    if hasattr(cam, attr):
                        try:
                            snap[attr] = float(getattr(cam, attr))
                        except Exception:
                            snap[attr] = getattr(cam, attr)
                for attr in ('target', 'follow_target', 'locked', 'lock_x', 'lock_y'):
                    if hasattr(cam, attr):
                        snap[attr] = getattr(cam, attr)
                try:
                    setattr(controller.model, '_hold_focus_prev_camera', snap)
                except Exception:
                    pass
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
    try:
        cam = getattr(controller.game, 'camera', None)
        world = getattr(getattr(controller, 'game', None), 'ecs', None)
        world = getattr(world, 'ecs_world', None)
        did_recentre = False
        if cam is not None and world is not None:
            comps = getattr(world, 'components', {}) or {}
            eid = getattr(world, 'player_entity', None)
            if not isinstance(eid, int):
                tags = comps.get('PlayerTagComponent', {}) or {}
                try:
                    eid = next(iter(tags.keys())) if tags else None
                except Exception:
                    eid = None
            if isinstance(eid, int):
                pos_map = comps.get('Position', {}) or {}
                p = pos_map.get(eid)
                if p is not None and hasattr(p, 'x') and hasattr(p, 'y'):
                    try:
                        zoom = getattr(cam, 'zoom', 1.0) or 1.0
                        cam.offset_x = float(getattr(p, 'x', 0.0)) - (cam.screen_width / (2 * zoom))
                        cam.offset_y = float(getattr(p, 'y', 0.0)) - (cam.screen_height / (2 * zoom))
                        if hasattr(cam, '_snap_offsets_to_pixel_grid'):
                            cam._snap_offsets_to_pixel_grid()
                    except Exception:
                        pass
                    did_recentre = True
        if not did_recentre:
            snap = getattr(controller.model, '_hold_focus_prev_camera', None)
            if cam is not None and isinstance(snap, dict):
                for k, v in snap.items():
                    try:
                        setattr(cam, k, v)
                    except Exception:
                        pass
                try:
                    if hasattr(cam, '_snap_offsets_to_pixel_grid'):
                        cam._snap_offsets_to_pixel_grid()
                except Exception:
                    pass
    except Exception:
        pass
    try:
        setattr(controller.model, '_hold_focus_prev_camera', None)
    except Exception:
        pass
    # Pulso tutorial
    try:
        setattr(controller.model, 'tutorial_hold_focus_ended_pulse', True)
    except Exception:
        pass
