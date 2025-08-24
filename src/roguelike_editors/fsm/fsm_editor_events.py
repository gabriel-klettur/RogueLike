"""FSM Editor - Event Router (skeleton)"""
from __future__ import annotations


import pygame
from .fsm_editor_controller import FsmEditorController

# Singleton/lazy controller used by static event/render hooks
_controller: FsmEditorController | None = None

def get_controller() -> FsmEditorController:
    global _controller
    if _controller is None:
        _controller = FsmEditorController()
    return _controller


class FsmEditorEventHandler:
    @staticmethod
    def handle_event(event) -> bool:
        """
        Entry-point used by the engine:
        - Visibility is toggled centrally (managers/core/events.py)
        - If visible, delegate to controller; return True if consumed
        - If not visible, return False
        """
        ctrl = get_controller()
        # Mirror centralized debug flag to controller visibility
        try:
            import roguelike_engine.config.config as cfg
            ctrl.visible = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
        except Exception:
            pass

        if not ctrl.visible:
            return False

        # Delegate to controller (panels will later route internally)
        return bool(ctrl.handle_event(event))

    @staticmethod
    def render(screen) -> None:
        """Optional render entry-point used by the engine loop."""
        ctrl = get_controller()
        # Mirror centralized flag before rendering
        try:
            import roguelike_engine.config.config as cfg
            ctrl.visible = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
        except Exception:
            pass
        if not ctrl.visible:
            return
        # Render FSM Editor via controller (view will handle title)
        try:
            ctrl.render(screen)
        except Exception:
            pass


__all__ = ["FsmEditorEventHandler", "get_controller"]
