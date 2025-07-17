import pygame
from roguelike_editors.inventory.controller.inventory_panel_controller import InventoryPanelController
from roguelike_editors.inventory.view.inventory_panel_view import InventoryPanelView
from roguelike_editors.inventory.model.inventory_panel_model import InventoryPanelModel

class InventoryPanelEventHandler:
    """
    Manejador de eventos para el panel izquierdo (tabs + listado).
    """
    def __init__(self, editor_controller, controller: InventoryPanelController, view: InventoryPanelView, model: InventoryPanelModel):
        self.editor_controller = editor_controller
        self.controller = controller
        self.view = view
        self.model = model

    def handle(self, event):
        # Click izquierdo
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Tabs
            for rect, cat in self.view.tab_rects:
                if rect.collidepoint(mx, my):
                    self.controller.change_category(cat)
                    self.editor_controller.model.current_category = cat
                    return True
            # Listado
            if self.view.panel_rect.collidepoint(mx, my):
                if self.model.current_category == 'monsters':
                    line_h = self.view.font.get_linesize()
                    idx = (my - self.view.panel_rect.y + self.view.scroll_panel.scroll_offset) // line_h
                    items = self.controller.get_items_list()
                    if 0 <= idx < len(items):
                        # Encontrar inicio del grupo
                        start_idx = idx
                        while start_idx > 0 and items[start_idx].startswith(' '):
                            start_idx -= 1
                        raw = items[start_idx].strip()
                        eid = raw.split(' ')[0]
                        self.controller.select_entity(eid)
                        return True
                # Bloquear clic en otras partes del panel de listado
                return True
        # Bloquear hovers dentro del panel
        if event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            if any(rect.collidepoint(mx, my) for rect, _ in self.view.tab_rects) or self.view.panel_rect.collidepoint(mx, my):
                return True
        return False