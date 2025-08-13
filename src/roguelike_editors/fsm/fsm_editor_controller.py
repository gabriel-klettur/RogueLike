"""FSM Editor - Main Controller (skeleton)

Orchestrates panels (title, toolbar, sets, graph, properties), global state,
persistence hooks, history, and runtime reload bridge.
"""
from __future__ import annotations
from typing import Optional
import pygame

from .fsm_toolbar.fsm_toolbar_controller import FsmToolbarController

class FsmEditorController:
    def __init__(self) -> None:
        # Visibility toggled by F12 elsewhere (FMSEventSpy/FMSController integration)
        self.visible: bool = False

        # Lazy-created/plugged submodules. Wired in later phases.
        self.title_controller = None
        self.toolbar_controller: Optional[FsmToolbarController] = FsmToolbarController()
        self.sets_panel_controller = None
        self.graph_panel_controller = None
        self.properties_panel_controller = None

        # View/Event handler can be split; keep placeholders for now
        self.view = None
        self.events = None

    # --- Lifecycle ---
    def render(self, screen) -> None:
        if not self.visible:
            return
        # Toolbar (left column, anchored). Returns its rect.
        if self.toolbar_controller:
            self.toolbar_controller.render(screen)
        # TODO: layout title -> toolbar -> left/center/right panels
        # No-op: Title rendering may be handled by a dedicated Title controller/view later
        return

    def handle_event(self, event) -> bool:
        if not self.visible:
            return False
        # Toolbar first, so drag/clicks don't leak to canvas
        if self.toolbar_controller and self.toolbar_controller.handle_event(event):
            return True
        # TODO: delegate to sets/graph/properties event handlers next
        return False

    # --- Visibility ---
    def toggle_visible(self, flag: Optional[bool] = None) -> None:
        if flag is None:
            self.visible = not self.visible
        else:
            self.visible = bool(flag)


__all__ = ["FsmEditorController"]
