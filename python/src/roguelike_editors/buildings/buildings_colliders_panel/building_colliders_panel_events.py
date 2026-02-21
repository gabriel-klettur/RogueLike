"""Event handler orchestrating the Buildings Colliders panel."""

from __future__ import annotations

import logging
from typing import Any, Iterable

import pygame

from .collision_brush import CollisionBrush
from .collision_persistence import CollisionFilePaths, CollisionPersistence
from .picker_controller import PickerController

# Expose default paths for backwards compatibility with tests/monkeypatching
_DEFAULT_PATHS = CollisionFilePaths()
BUILDINGS_COLLISIONS_DATA_PATH = _DEFAULT_PATHS.collisions_data
BUILDINGS_COLLISIONS_BY_IMAGE_PATH = _DEFAULT_PATHS.collisions_by_image
BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = _DEFAULT_PATHS.collisions_by_spawn
BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = _DEFAULT_PATHS.collisions_by_instance


class BuildingCollidersPanelEventHandler:
    """Coordinate picker actions, brush strokes, and persistence."""

    def __init__(
        self,
        state: Any,
        editor_state: Any,
        model: Any,
        *,
        logger: logging.Logger | None = None,
        brush: CollisionBrush | None = None,
        persistence: CollisionPersistence | None = None,
        picker: PickerController | None = None,
        paths: CollisionFilePaths | None = None,
    ) -> None:
        self.state = state
        self.editor_state = editor_state
        self.model = model
        self.logger = logger or logging.getLogger("buildings.colliders.events")
        effective_paths = paths or CollisionFilePaths(
            collisions_data=BUILDINGS_COLLISIONS_DATA_PATH,
            collisions_by_image=BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
            collisions_by_spawn=BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
            collisions_by_instance=BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
        )
        self.persistence = persistence or CollisionPersistence(editor_state, model, self.logger, paths=effective_paths)
        self.brush = brush or CollisionBrush(editor_state, model, self.logger)
        self.picker = picker or PickerController(editor_state, model, self.persistence, self.logger)

    # ------------------------------------------------------------------
    # Compatibility properties (manager injects ecs_world / game)
    # ------------------------------------------------------------------
    @property
    def ecs_world(self) -> Any | None:
        return self.persistence.ecs_world

    @ecs_world.setter
    def ecs_world(self, value: Any) -> None:
        self.persistence.ecs_world = value

    @property
    def game(self) -> Any | None:
        return self.persistence.game

    @game.setter
    def game(self, value: Any) -> None:
        self.persistence.game = value

    # ------------------------------------------------------------------
    # Event dispatch
    # ------------------------------------------------------------------
    def handle(self, event: pygame.event.Event, camera: Any, buildings: Iterable[Any]) -> bool:
        if not getattr(self.model, "active", False):
            return False

        if event.type == pygame.MOUSEBUTTONDOWN:
            return self._handle_mouse_down(event, camera, buildings)
        if event.type == pygame.MOUSEBUTTONUP:
            return self._handle_mouse_up(event, buildings)
        if event.type == pygame.MOUSEMOTION:
            return self._handle_mouse_motion(event, camera, buildings)
        return False

    # ------------------------------------------------------------------
    # Mouse handlers
    # ------------------------------------------------------------------
    def _handle_mouse_down(
        self,
        event: pygame.event.Event,
        camera: Any,
        buildings: Iterable[Any],
    ) -> bool:
        if self.picker.handle_mouse_down(event, buildings):
            return True
        if event.button == 1 and getattr(self.model, "choice", None):
            self.model.brush_dragging = True
            self.brush.paint(camera, buildings)
            return True
        return False

    def _handle_mouse_up(self, event: pygame.event.Event, buildings: Iterable[Any]) -> bool:
        if self.picker.handle_mouse_up(event):
            return True
        if event.button == 1 and getattr(self.model, "brush_dragging", False):
            self.model.brush_dragging = False
            self._save_collisions(buildings)
            return True
        return False

    def _handle_mouse_motion(
        self,
        event: pygame.event.Event,
        camera: Any,
        buildings: Iterable[Any],
    ) -> bool:
        if self.picker.handle_mouse_motion(event):
            return True
        if getattr(self.model, "brush_dragging", False) and getattr(self.model, "choice", None):
            self.brush.paint(camera, buildings)
            return True
        return False

    # ------------------------------------------------------------------
    # Persistence façade (keeps tests/API compatibility)
    # ------------------------------------------------------------------
    def _save_collisions(self, buildings: Iterable[Any], force: bool = False) -> None:
        self.persistence.save(buildings, force=force)
