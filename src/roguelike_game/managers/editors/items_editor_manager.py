import pygame
from roguelike_editors.items.items_editor_controller import ItemsEditorController

class ItemsEditorManager:
    """
    Manager para el editor de ítems: delega a ItemEditorController.
    """
    def __init__(self, game):
        self.game = game
        items = game.items
        assets = game.item_assets
        font = game.font
        # Instanciar controlador orquestador del editor completo (picker + props + instancias)
        self.controller = ItemsEditorController(items, assets, font)
        # Permitir features que requieren acceso al juego (spawn RMB)
        self.controller.set_game(game)
        # Exponer estado global
        self.model = self.controller.model
        game.state.item_editor_state = self.model

    def handle_event(self, event: pygame.event.Event) -> None:
        """Delegar evento al controlador"""
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        """Delegar renderizado"""
        self.controller.draw(screen)

    # Exponer API de visibilidad para el sistema centralizado de toggles
    def show(self) -> None:
        self.controller.show()

    def hide(self) -> None:
        self.controller.hide()

    def toggle(self) -> None:
        self.controller.toggle()
