import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.left_panel.panel_controller import PanelController
from roguelike_editors.inventory.left_panel.panel_view import PanelView
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel

from .tabs.tabs_event_handler import TabsEventHandler
from .list.list_event_handler import ListEventHandler


class PanelEventHandler:
    """
    Usa PanelView para dibujar y manejar eventos de tabs y lista.
    """
    """
    Controlador de eventos para el panel izquierdo (tabs + listado) que delega a manejadores especializados.
    """
    def __init__(self, editor_controller, controller: PanelController, view: PanelView, model: InventoryPanelModel):
        self.editor_controller = editor_controller
        self.controller = controller
        self.view = view
        self.model = model

        # Inicializar manejadores especializados
        self.tabs_handler = TabsEventHandler(editor_controller, controller, view, model)
        self.list_handler = ListEventHandler(editor_controller, controller, view, model)

    def handle(self, event):
        # Recentrar cámara si estaba en enfoque de monstros
        if self.editor_controller.model.camera_focus_target is not None and event.type in (
            pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL
        ):
            player_eid = getattr(self.editor_controller.world, 'player_entity', None)
            pos_map = self.editor_controller.world.components.get('Position', {})
            if player_eid in pos_map:
                pos = pos_map[player_eid]
                self.editor_controller.game.camera.update(SimpleNamespace(x=pos.x, y=pos.y))
            self.editor_controller.model.camera_focus_target = None
        # Delegar a tabs
        if self.tabs_handler.handle(event):
            return True
        # Delegar a listado
        if self.list_handler.handle(event):
            return True
        # Bloquear hovers dentro del panel
        if event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            if any(rect.collidepoint(mx, my) for rect, _ in self.view.tab_rects) or self.view.panel_rect.collidepoint(mx, my):
                return True
        return False
