"""Brush logic for the Buildings Colliders panel."""
from __future__ import annotations

from typing import Any, Iterable

import pygame

try:
    from roguelike_engine.config.config_tiles import TILE_SIZE
except Exception:  # pragma: no cover - fallback for editor-only runs
    TILE_SIZE = 32

from roguelike_editors.buildings.utils.asset_paths import normalize_asset_path

DEFAULT_GRID_SIZE = (15, 15)


class CollisionBrush:
    """Handle painting interactions on building collision maps."""

    def __init__(self, editor_state: Any, model: Any, logger: Any) -> None:
        self.editor_state = editor_state
        self.model = model
        self.logger = logger

    def paint(self, camera: Any, buildings: Iterable[Any]) -> bool:
        """Paint at the current mouse position using the active brush choice."""
        if not getattr(self.model, "choice", None):
            return False

        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_x = mouse_x / getattr(camera, "zoom", 1.0) + getattr(camera, "offset_x", 0)
        world_y = mouse_y / getattr(camera, "zoom", 1.0) + getattr(camera, "offset_y", 0)

        building = self._active_building()
        if building is None:
            return False

        if not self._is_mouse_over_building(building, world_x, world_y):
            return False

        try:
            self._ensure_collision_map_initialized(building)
        except Exception:  # pragma: no cover - defensive editor guard
            return False

        row, col = self._resolve_cell(building, world_x, world_y)
        if not self._is_cell_inside_map(building, row, col):
            return False

        collision_map = getattr(building, "collision_map", None)
        prev_value = None
        try:
            prev_value = collision_map[row][col]
        except Exception:  # pragma: no cover - unexpected shape
            pass
        collision_map[row][col] = self.model.choice

        self._record_stroke_stats(building)
        self._mark_tutorial_progress()
        self._invalidate_caches(building)
        self._flag_colliders_dirty()
        self._propagate_global_scope(building, row, col, buildings)
        self._log_cell_change(building, row, col, prev_value)
        return True

    # ------------------------------------------------------------------
    # Helper methods
    # ------------------------------------------------------------------
    def _active_building(self) -> Any | None:
        active = getattr(self.model, "active_building", None)
        if active:
            return active
        return getattr(self.editor_state, "active_building", None)

    def _is_mouse_over_building(self, building: Any, world_x: float, world_y: float) -> bool:
        try:
            rect = pygame.Rect(building.x, building.y, *building.image.get_size())
        except Exception:  # pragma: no cover - building without sprite
            return False
        return rect.collidepoint(world_x, world_y)

    def _ensure_collision_map_initialized(self, building: Any) -> None:
        collision_map = getattr(building, "collision_map", None)
        needs_init = True
        if isinstance(collision_map, list) and collision_map and isinstance(collision_map[0], list):
            rows = len(collision_map)
            cols = len(collision_map[0]) if rows > 0 else 0
            needs_init = rows <= 1 or cols <= 1
        if needs_init:
            width, height = DEFAULT_GRID_SIZE
            building.collision_map = [["." for _ in range(width)] for _ in range(height)]

    def _resolve_cell(self, building: Any, world_x: float, world_y: float) -> tuple[int, int]:
        try:
            rows = len(building.collision_map)
            cols = len(building.collision_map[0]) if rows > 0 else 0
            img_width, img_height = building.image.get_size()
            if rows > 0 and cols > 0 and img_width > 0 and img_height > 0:
                cell_width = max(1.0, img_width / float(cols))
                cell_height = max(1.0, img_height / float(rows))
                col = int((world_x - building.x) / cell_width)
                row = int((world_y - building.y) / cell_height)
            else:
                col = int((world_x - building.x) // TILE_SIZE)
                row = int((world_y - building.y) // TILE_SIZE)
        except Exception:  # pragma: no cover - fallback to tile grid
            col = int((world_x - building.x) // TILE_SIZE)
            row = int((world_y - building.y) // TILE_SIZE)
        return row, col

    def _is_cell_inside_map(self, building: Any, row: int, col: int) -> bool:
        try:
            rows = len(building.collision_map)
            cols = len(building.collision_map[0]) if rows > 0 else 0
        except Exception:  # pragma: no cover
            return False
        return 0 <= row < rows and 0 <= col < cols

    def _record_stroke_stats(self, building: Any) -> None:
        try:
            if not getattr(self.editor_state, "_colliders_stroke_started", False):
                self.editor_state._colliders_stroke_started = True
                self.editor_state._colliders_stroke_cells = 0
                self.editor_state._colliders_stroke_buildings = set()
                scope = getattr(self.editor_state, "collider_scope", getattr(building, "collider_scope", "CG"))
                self.editor_state._colliders_stroke_scope = scope
            self.editor_state._colliders_stroke_cells += 1
            building_id = getattr(building, "id", None)
            if building_id is not None:
                self.editor_state._colliders_stroke_buildings.add(building_id)
        except Exception:  # pragma: no cover - keep editor resilient
            pass

    def _mark_tutorial_progress(self) -> None:
        try:
            setattr(self.editor_state, "tutorial_colliders_painted_pulse", True)
            setattr(self.editor_state, "tutorial_colliders_painted_on_selected_pulse", True)
        except Exception:  # pragma: no cover
            pass

    def _invalidate_caches(self, building: Any) -> None:
        try:
            if hasattr(building, "model"):
                building.model.invalidate_collision_caches()
        except Exception:  # pragma: no cover
            pass

    def _flag_colliders_dirty(self) -> None:
        try:
            setattr(self.editor_state, "colliders_dirty", True)
        except Exception:  # pragma: no cover
            pass

    def _propagate_global_scope(self, building: Any, row: int, col: int, buildings: Iterable[Any]) -> None:
        scope = getattr(self.editor_state, "collider_scope", getattr(building, "collider_scope", "CG"))
        if scope != "CG":
            return

        src_map = getattr(building, "collision_map", None)
        if not src_map:
            return

        rows_ref = len(src_map)
        cols_ref = len(src_map[0]) if rows_ref > 0 else 0
        img_key = normalize_asset_path(getattr(building, "image_path", None))
        for candidate in buildings:
            if candidate is building:
                continue
            candidate_key = normalize_asset_path(getattr(candidate, "image_path", None))
            if not img_key or candidate_key != img_key:
                continue
            if getattr(candidate, "collider_scope", "CG") == "CU":
                continue
            try:
                target_map = getattr(candidate, "collision_map", None)
                rows2 = len(target_map)
                cols2 = len(target_map[0]) if rows2 > 0 else 0
                if rows2 <= 0 or cols2 <= 0:
                    continue
                mapped_row = int(row * rows2 / max(1, rows_ref))
                mapped_col = int(col * cols2 / max(1, cols_ref))
                mapped_row = min(mapped_row, rows2 - 1)
                mapped_col = min(mapped_col, cols2 - 1)
                target_map[mapped_row][mapped_col] = self.model.choice
                self._record_secondary_building(candidate)
                self._invalidate_caches(candidate)
            except Exception:  # pragma: no cover - ignore malformed instances
                continue

    def _record_secondary_building(self, building: Any) -> None:
        try:
            if not getattr(self.editor_state, "_colliders_stroke_started", False):
                self.editor_state._colliders_stroke_started = True
                self.editor_state._colliders_stroke_cells = 0
                self.editor_state._colliders_stroke_buildings = set()
                scope = getattr(self.editor_state, "collider_scope", getattr(building, "collider_scope", "CG"))
                self.editor_state._colliders_stroke_scope = scope
            building_id = getattr(building, "id", None)
            if building_id is not None:
                self.editor_state._colliders_stroke_buildings.add(building_id)
        except Exception:  # pragma: no cover
            pass

    def _log_cell_change(self, building: Any, row: int, col: int, prev_value: Any) -> None:
        try:
            building_id = getattr(building, "id", None)
            self.logger.debug(
                "[Colliders][BRUSH] id=%s row=%s col=%s from=%s to=%s",
                building_id,
                row,
                col,
                prev_value,
                self.model.choice,
            )
        except Exception:  # pragma: no cover
            pass
