from __future__ import annotations

from typing import Optional
import pygame

from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel
from roguelike_editors.spawner.spawner_editor_events import SpawnerEditorEventHandler
from roguelike_editors.spawner.spawner_editor_view import SpawnerEditorView
from roguelike_editors.spawner.spawner_title.spawner_title_controller import SpawnerTitleController
from roguelike_editors.spawner.spawner_toolbar.spawner_toolbar_controller import SpawnerToolbarController
from roguelike_editors.spawner.spawner_manager.spawner_manager_controller import SpawnerManagerController
from roguelike_editors.spawner.spawner_list.spawner_list_controller import SpawnerListController


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
        self.title_controller = SpawnerTitleController(self, self.model.title_model, self.font)
        # Toolbar (undo/spawner_manager/redo)
        self.spawner_toolbar = SpawnerToolbarController(self)
        # Spawner lists:
        # - Instances list (data/spawners/instances.json)
        self.spawner_instances = SpawnerListController()
        # - Manager (templates list, data/spawners/spawners.json)
        self.spawner_manager = SpawnerManagerController()
        self._instances_visible_last: bool = False
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
            # Sync visibility with toolbar active tool
            active_tool = getattr(getattr(self, 'spawner_toolbar', None), 'model', None)
            active_tool = getattr(active_tool, 'active_tool', None)
            self.spawner_manager.set_visible(active_tool == 'spawner_manager')
            instances_visible = (active_tool == 'spawner_list')
            # Keep model visible in sync for view short-circuit
            try:
                self.spawner_instances.model.visible = bool(instances_visible)
            except Exception:
                pass
            # Refresh instances list on first show
            if instances_visible and not self._instances_visible_last:
                try:
                    self.spawner_instances.refresh_from_disk()
                except Exception:
                    pass
            self._instances_visible_last = instances_visible
            # Route toolbar first to consume UI interactions
            if hasattr(self, 'spawner_toolbar') and self.spawner_toolbar.handle_event(event):
                return True
            # Manager captures UI next if visible
            if getattr(self.spawner_manager.model, 'visible', False):
                if self.spawner_manager.handle_event(event):
                    return True
            # Instances list captures next if visible
            if instances_visible:
                if self.spawner_instances.handle_event(event):
                    return True
            # Title panel (currently no events, but keep parity with other editors)
            if hasattr(self, 'title_controller') and self.title_controller.handle_event(event):
                return True
            return self.events.handle_event(event)
        except Exception:
            return False

    def render(self, screen: pygame.Surface) -> None:
        try:
            # Sync visibility again before rendering (in case of external toggles)
            active_tool = getattr(getattr(self, 'spawner_toolbar', None), 'model', None)
            active_tool = getattr(active_tool, 'active_tool', None)
            self.spawner_manager.set_visible(active_tool == 'spawner_manager')
            instances_visible = (active_tool == 'spawner_list')
            # Keep model visible in sync for view short-circuit
            try:
                self.spawner_instances.model.visible = bool(instances_visible)
            except Exception:
                pass
            if instances_visible and not self._instances_visible_last:
                try:
                    self.spawner_instances.refresh_from_disk()
                except Exception:
                    pass
            self._instances_visible_last = instances_visible
            self.view.render(screen)
        except Exception:
            pass
