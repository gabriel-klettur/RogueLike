"""
Controlador del panel de Add/Remove del Buildings Editor.
"""

from .buildings_add_remove_panel_model import BuildingsAddRemovePanelModel
from .buildings_add_remove_panel_view import BuildingsAddRemovePanelView
from .buildings_add_remove_panel_events import BuildingsAddRemovePanelEventHandler


class BuildingsAddRemovePanelController:
    def __init__(self, state, editor_state, editor_view, editor_manager):
        self.state = state
        self.editor_state = editor_state
        self.editor_view = editor_view
        self.editor_manager = editor_manager

        self.model = BuildingsAddRemovePanelModel()
        self.view = BuildingsAddRemovePanelView(state, editor_state, self.model, editor_view)
        self.events = BuildingsAddRemovePanelEventHandler(state, editor_state, self, self.model)

        # Inyectar referencias necesarias para ToolbarView
        try:
            # ToolbarView necesita un controller con is_active(tool)
            self.view.controller = self
            if hasattr(self.view, 'widget'):
                self.view.widget.controller = self
        except Exception:
            pass

    # Estado
    def is_active(self, tool: str | None = None) -> bool:
        """Compatibilidad dual:
        - sin argumentos → estado visible del panel
        - con argumento → estado de selección del ToolbarView
        """
        if tool is None:
            return bool(getattr(self.model, 'active', False))
        return getattr(self.model, 'active_tool', None) == tool

    def activate(self) -> None:
        self.model.active = True
        self.model.reset_runtime()

    def deactivate(self) -> None:
        self.model.active = False
        self.model.reset_runtime()
        try:
            if hasattr(self.editor_state, 'add_remove_panel_rect'):
                self.editor_state.add_remove_panel_rect = None
        except Exception:
            pass

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    # Integración
    def handle_event(self, event, camera, buildings) -> bool:
        return self.events.handle(event, camera, buildings)

    def render(self, screen) -> None:
        if self.is_active(None):
            self.view.render(screen)

    # API para ToolbarView (resaltar selección)
    # Removed redundant method
