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
        - F12 toggles editor visibility
        - If visible, delegate to controller; return True if consumed
        - If not visible, return False
        """
        ctrl = get_controller()

        if event.type == pygame.KEYDOWN and event.key == pygame.K_F12:
            ctrl.toggle_visible()
            # Mirror gate flag used by debug overlay systems
            try:
                import roguelike_engine.config.config as config
                config.DEBUG_ENTITIES = ctrl.visible
            except Exception:
                pass
            return True

        if not ctrl.visible:
            return False

        # Delegate to controller (panels will later route internally)
        return bool(ctrl.handle_event(event))

    @staticmethod
    def render(screen) -> None:
        """Optional render entry-point used by the engine loop."""
        ctrl = get_controller()
        if not ctrl.visible:
            return
        # Render main FSM Editor panels (toolbar, etc.)
        try:
            ctrl.render(screen)
        except Exception:
            pass
        # Draw FSM Editor Title using reusable TitleBar (same as other editors)
        try:
            from roguelike_editors.fsm.fsm_title.fsm_title_model import FsmTitleModel
            from roguelike_editors.fsm.fsm_title.fsm_title_controller import FsmTitleController
            global _title_ctrl
            try:
                _title_ctrl
            except NameError:
                _title_ctrl = None
            if _title_ctrl is None:
                _title_ctrl = FsmTitleController(editor_state=None, model=FsmTitleModel(), font=None)
            _title_ctrl.render(screen)
        except Exception:
            # Keep optional UI safe
            pass


__all__ = ["FsmEditorEventHandler", "get_controller"]
