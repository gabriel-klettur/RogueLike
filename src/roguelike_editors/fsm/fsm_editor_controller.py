"""FSM Editor - Main Controller (skeleton)

Orchestrates panels (title, toolbar, sets, graph, properties), global state,
persistence hooks, history, and runtime reload bridge.
"""
from __future__ import annotations
from typing import Optional
import pygame


class FsmEditorController:
    def __init__(self) -> None:
        # Visibility toggled by F12 elsewhere (FMSEventSpy/FMSController integration)
        self.visible: bool = False

        # Lazy-created/plugged submodules. Wired in later phases.
        self.title_controller = None
        self.toolbar_controller = None
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
        # TODO: layout title -> toolbar -> left/center/right panels
        # No-op: Title rendering is handled by FsmEditorEventHandler.render()
        return

    def handle_event(self, event) -> bool:
        if not self.visible:
            return False
        # TODO: delegate to toolbar/sets/graph/properties event handlers
        return False

    # --- Visibility ---
    def toggle_visible(self, flag: Optional[bool] = None) -> None:
        if flag is None:
            self.visible = not self.visible
        else:
            self.visible = bool(flag)


__all__ = ["FsmEditorController"]
