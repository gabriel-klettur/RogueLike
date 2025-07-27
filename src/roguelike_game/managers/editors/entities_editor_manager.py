import pygame
from pathlib import Path
from roguelike_editors.entities.entities_editor_model import EntitiesEditorModel
from roguelike_editors.entities.entities_editor_controller import EntitiesEditorController
from roguelike_editors.entities.entities_editor_events import EntitiesEditorEventHandler

class EntitiesEditorManager:
    """
    Manager para el editor de entidades: orquesta todo el MVC.
    """
    def __init__(self, game):
        self.game = game
        font = game.font
        # Inicializar MVC
        self.model = EntitiesEditorModel(Path('data'))
        self.controller = EntitiesEditorController(self.model, font)
        self.event_handler = EntitiesEditorEventHandler(self.model, self.controller)
        # Registrar estado global
        game.state.entities_editor_state = self.model

    def handle_event(self, event: pygame.event.Event) -> None:
        """
        Delegar eventos al handler MVC (incluye toggle F6/Esc y panning).
        """
        self.event_handler.handle([event], self.game.camera, getattr(self.game, 'map', None))

    def update(self, camera, game_map=None) -> None:
        """
        Actualizar controlador si el editor está activo.
        """
        if self.model.active:
            self.controller.update(camera, game_map)

    def draw(self, screen: pygame.Surface) -> None:
        """
        Renderizar editor si está activo.
        """
        if self.model.active:
            self.controller.render(screen)
