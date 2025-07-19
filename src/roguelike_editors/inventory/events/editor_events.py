import pygame


class InventoryEditorEventHandler:
    """
    Manejador de eventos para el editor de inventario.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.world = controller.world
        self.view = controller.view

    def handle(self, event):
        # Delegar eventos a panel izquierdo
        if self.controller.inventory_panel_event_handler.handle(event):
            return True
        # Delegar eventos a panel de selección de ítems
        if self.controller.item_selection_event_handler.handle(event):
            return True
        # Delegar eventos al panel de ítems (grid, botones)
        if self.controller.grid_event_handler.handle(event):
            return True
            
        return False