import pygame
from roguelike_editors.items.editor_controller import ItemEditorController

class ItemsEditorManager:
    """
    Manager para el editor de ítems: delega a ItemEditorController.
    """
    def __init__(self, game):
        self.game = game
        items = game.items
        assets = game.item_assets
        font = game.font
        # Instanciar controlador
        self.controller = ItemEditorController(items, assets, font)
        self.controller.game = game
        self.model = self.controller.model
        # Exponer estado global
        game.state.item_editor_state = self.model

    def handle_event(self, event: pygame.event.Event) -> None:
        """Delegar evento al controlador"""
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        """Delegar renderizado"""
        self.controller.draw(screen)
