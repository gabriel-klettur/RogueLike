import pygame
from roguelike_editors.inventory.editor_controller import InventoryEditorController

class InventoryEditorManager:
    """
    Manager para el editor de inventario: delega a InventoryEditorController.
    """
    def __init__(self, game):
        self.game = game
        world = game.ecs.ecs_world
        self.world = world
        assets = game.item_assets
        font = game.font
        # Instanciar controlador
        self.controller = InventoryEditorController(self.game, world, assets, font)
        self.model = self.controller.model
        # Exponer estado global
        game.state.inventory_editor_state = self.model

    def handle_event(self, event: pygame.event.Event) -> None:
        """Delegar evento al controlador."""
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        """Delegar renderizado."""
        self.controller.draw(screen)
