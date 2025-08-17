from __future__ import annotations

from typing import Optional
import pygame

from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel
from roguelike_editors.spawner.spawner_editor_events import SpawnerEditorEventHandler
from roguelike_editors.spawner.spawner_editor_view import SpawnerEditorView


class SpawnerEditorController:
    """Coordinator for the Spawner Editor MVC.

    - Holds state in `SpawnerEditorModel`
    - Delegates input to `SpawnerEditorEventHandler`
    - Delegates rendering to `SpawnerEditorView`
    """

    def __init__(self, font: Optional[pygame.font.Font] = None):
        self.model = SpawnerEditorModel()
        self.font = font
        self.game = None  # set via set_game
        # Delegates
        self.events = SpawnerEditorEventHandler(self)
        self.view = SpawnerEditorView(self)

    # Public API ---------------------------------------------------------------
    def set_game(self, game) -> None:
        self.game = game
        # Keep delegate in sync
        try:
            self.events.set_game(game)
        except Exception:
            pass

    def toggle_visible(self) -> None:
        # Delegate to event handler for consistent cleanup
        try:
            self.events.toggle_visible()
        except Exception:
            # Fallback: minimal toggle
            self.model.visible = not self.model.visible

    def handle_event(self, event: pygame.event.Event) -> bool:
        try:
            return self.events.handle_event(event)
        except Exception:
            return False

    def render(self, screen: pygame.Surface) -> None:
        try:
            self.view.render(screen)
        except Exception:
            pass
