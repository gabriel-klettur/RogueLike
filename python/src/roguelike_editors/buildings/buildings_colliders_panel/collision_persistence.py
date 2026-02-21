"""Persistence layer for buildings colliders."""
from __future__ import annotations

import json
import logging
import os
from dataclasses import dataclass
from typing import Any, Iterable

import pygame

try:
    # Used to trigger the same reload that F1 performs
    from roguelike_game.config.hot_reload import reload_all_game_data
except Exception:  # pragma: no cover - optional dependency in editor context
    reload_all_game_data = None

try:
    from roguelike_engine.config.config import (
        BUILDINGS_COLLISIONS_DATA_PATH,
        BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
        BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
        BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
    )
except Exception:  # pragma: no cover - defensive fallbacks when config missing
    BUILDINGS_COLLISIONS_DATA_PATH = "data/buildings/buildings_collisions_data.json"
    BUILDINGS_COLLISIONS_BY_IMAGE_PATH = "data/buildings/buildings_collisions_by_image.json"
    BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = "data/buildings/buildings_collisions_by_spawn_id.json"
    BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = (
        "data/buildings/buildings_collisions_by_building_instance_id.json"
    )

from roguelike_editors.buildings.utils.collisions_apply import apply_collisions_to_loaded_buildings
from roguelike_editors.buildings.utils.asset_paths import normalize_asset_path


@dataclass
class CollisionFilePaths:
    """Bundle filesystem locations used by the persistence layer."""

    collisions_data: str = BUILDINGS_COLLISIONS_DATA_PATH
    collisions_by_image: str = BUILDINGS_COLLISIONS_BY_IMAGE_PATH
    collisions_by_spawn: str = BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH
    collisions_by_instance: str = BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH


class CollisionPersistence:
    """Handle saving and propagating collider data to disk and runtime."""

    def __init__(
        self,
        editor_state: Any,
        model: Any,
        logger: logging.Logger,
        paths: CollisionFilePaths | None = None,
    ) -> None:
        self.editor_state = editor_state
        self.model = model
        self.logger = logger
        self.paths = paths or CollisionFilePaths()
        self.ecs_world: Any | None = None
        self.game: Any | None = None

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------
    def save(self, buildings: Iterable[Any], force: bool = False) -> None:
        """Persist collider data for the current stroke and propagate updates."""
        active = getattr(self.model, "active_building", None) or getattr(self.editor_state, "active_building", None)
        scope = getattr(self.editor_state, "collider_scope", "CG")

        self._log_stroke_summary(active, scope, force)
        by_image, by_spawn, by_instance = self._load_existing_data()

        updated_by_image: list[str] = []
        updated_by_instance: list[str] = []

        self._save_global_scope(active, buildings, scope, by_image, updated_by_image)
        self._save_custom_scope(active, scope, by_instance, updated_by_instance)

        self._ensure_output_directory()
        self._write_files(by_image, by_spawn, by_instance)
        self._apply_runtime_updates(
            buildings,
            by_image,
            by_instance,
            updated_by_image,
            updated_by_instance,
        )
        self._flag_dirty()
        self._maybe_rebuild_spatial_index(buildings)
        self._sync_entities_namespace(buildings)
        self._maybe_trigger_hot_reload()
        self._reset_stroke_debug()
        self._log_persistence_summary(updated_by_image, updated_by_instance)

    # ------------------------------------------------------------------
    # Helpers - logging / bookkeeping
    # ------------------------------------------------------------------
    def _log_stroke_summary(self, active: Any, scope: str, force: bool) -> None:
        try:
            cells = int(getattr(self.editor_state, "_colliders_stroke_cells", 0) or 0)
            buildings = getattr(self.editor_state, "_colliders_stroke_buildings", set()) or set()
            self.logger.info(
                "[Colliders][SAVE] scope=%s active_id=%s cells=%s buildings_affected=%s force=%s",
                scope,
                getattr(active, "id", None),
                cells,
                len(buildings),
                force,
            )
        except Exception:  # pragma: no cover
            pass

    def _log_persistence_summary(self, updated_by_image: list[str], updated_by_instance: list[str]) -> None:
        try:
            if updated_by_image:
                sample = ", ".join(updated_by_image[:5])
                more = "" if len(updated_by_image) <= 5 else f" (+{len(updated_by_image) - 5} más)"
                self.logger.info(
                    "[Colliders][CG] Guardadas/mezcladas %s entradas por image_path: %s%s",
                    len(updated_by_image),
                    sample,
                    more,
                )
            if updated_by_instance:
                sample = ", ".join(updated_by_instance[:5])
                more = "" if len(updated_by_instance) <= 5 else f" (+{len(updated_by_instance) - 5} más)"
                self.logger.info(
                    "[Colliders][CU] Guardadas/mezcladas %s entradas por building_instance_id: %s%s",
                    len(updated_by_instance),
                    sample,
                    more,
                )
        except Exception:  # pragma: no cover
            pass

    # ------------------------------------------------------------------
    # Helpers - load / write JSON data
    # ------------------------------------------------------------------
    def _load_existing_data(self) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
        return (
            self._read_dict(self.paths.collisions_by_image),
            self._read_dict(self.paths.collisions_by_spawn),
            self._read_dict(self.paths.collisions_by_instance),
        )

    def _read_dict(self, path: str) -> dict[str, Any]:
        try:
            if os.path.exists(path):
                with open(path, "r", encoding="utf-8") as stream:
                    data = json.load(stream) or {}
                    return data if isinstance(data, dict) else {}
        except Exception:  # pragma: no cover - corrupted files
            return {}
        return {}

    def _write_files(
        self,
        by_image: dict[str, Any],
        by_spawn: dict[str, Any],
        by_instance: dict[str, Any],
    ) -> None:
        try:
            with open(self.paths.collisions_by_image, "w", encoding="utf-8") as stream:
                json.dump(by_image, stream, indent=4)
            with open(self.paths.collisions_by_spawn, "w", encoding="utf-8") as stream:
                json.dump(by_spawn, stream, indent=4)
            with open(self.paths.collisions_by_instance, "w", encoding="utf-8") as stream:
                json.dump(by_instance, stream, indent=4)
        except Exception as exc:  # pragma: no cover - disk issues
            self.logger.error(f"[Colliders] Error escribiendo archivos de colisiones: {exc}")

    def _ensure_output_directory(self) -> None:
        destination = os.path.dirname(self.paths.collisions_by_image) or os.path.dirname(
            self.paths.collisions_data
        )
        os.makedirs(destination, exist_ok=True)

    # ------------------------------------------------------------------
    # Helpers - scope persistence
    # ------------------------------------------------------------------
    def _save_global_scope(
        self,
        active: Any,
        buildings: Iterable[Any],
        scope: str,
        by_image: dict[str, Any],
        updated: list[str],
    ) -> None:
        if scope != "CG":
            return

        target_image_key = self._resolve_image_key(active)
        if target_image_key and getattr(active, "collision_map", None) is not None:
            self._persist_image_entry(active, target_image_key, by_image, updated)
            return

        for building in buildings:
            if getattr(building, "collision_map", None) is None:
                continue
            if getattr(building, "_is_spawner_visual", False) or getattr(building, "spawner_instance_id", None):
                continue
            if target_image_key and normalize_asset_path(getattr(building, "image_path", None)) != target_image_key:
                continue
            key = normalize_asset_path(getattr(building, "image_path", ""))
            if not key:
                continue
            self._persist_image_entry(building, key, by_image, updated)

    def _save_custom_scope(
        self,
        active: Any,
        scope: str,
        by_instance: dict[str, Any],
        updated: list[str],
    ) -> None:
        if scope != "CU" or active is None or getattr(active, "collision_map", None) is None:
            return

        try:
            building_id = getattr(active, "id", None)
            if building_id is None:
                return
            entry = self._serialize_building(active)
            by_instance[str(building_id)] = entry
            updated.append(str(building_id))
        except Exception:  # pragma: no cover
            pass

    def _persist_image_entry(
        self,
        building: Any,
        image_key: str,
        storage: dict[str, Any],
        updated: list[str],
    ) -> None:
        try:
            storage[image_key] = self._serialize_building(building)
            updated.append(image_key)
        except Exception:  # pragma: no cover
            pass

    def _resolve_image_key(self, building: Any) -> str | None:
        if building is None:
            return None
        return normalize_asset_path(getattr(building, "image_path", None))

    def _serialize_building(self, building: Any) -> dict[str, Any]:
        try:
            image_width, image_height = building.image.get_size()
        except Exception:  # pragma: no cover
            image_width, image_height = (0, 0)
        collision_map = getattr(building, "collision_map", None) or []
        width = len(collision_map[0]) if collision_map else 0
        height = len(collision_map)
        return {
            "width": width,
            "height": height,
            "collision": collision_map,
            "grid_ref_size": [int(image_width), int(image_height)],
        }

    # ------------------------------------------------------------------
    # Helpers - runtime application
    # ------------------------------------------------------------------
    def _apply_runtime_updates(
        self,
        buildings: Iterable[Any],
        by_image: dict[str, Any],
        by_instance: dict[str, Any],
        updated_images: list[str],
        updated_instances: list[str],
    ) -> None:
        try:
            applied = apply_collisions_to_loaded_buildings(
                buildings,
                by_image=by_image,
                by_binst=by_instance,
                updated_by_img=updated_images,
                updated_by_inst=updated_instances,
            )
            if applied:
                self.logger.info(f"[Colliders][APPLY] Updated in-memory buildings: {int(applied)}")
        except Exception:  # pragma: no cover
            pass

    def _flag_dirty(self) -> None:
        try:
            setattr(self.editor_state, "colliders_dirty", True)
        except Exception:  # pragma: no cover
            pass

    def _maybe_rebuild_spatial_index(self, buildings: Iterable[Any]) -> None:
        if self.ecs_world is None:
            return

        try:
            self.ecs_world.buildings = buildings
        except Exception:  # pragma: no cover
            pass

        try:
            self.logger.info("[Colliders][SAVE] Rebuilding SpatialIndex immediately via ecs_world in panel")
        except Exception:  # pragma: no cover
            pass

        try:
            setattr(self.ecs_world, "_log_rebuild_info", True)
        except Exception:  # pragma: no cover
            pass

        try:
            self.ecs_world.rebuild_spatial_index()
        except Exception:  # pragma: no cover
            return

        try:
            self.editor_state.colliders_dirty = False
            self.editor_state.last_colliders_rebuild_ms = pygame.time.get_ticks()
        except Exception:  # pragma: no cover
            pass

    def _sync_entities_namespace(self, buildings: Iterable[Any]) -> None:
        if self.game is None or not hasattr(self.game, "entities"):
            return
        try:
            setattr(self.game.entities, "buildings", buildings)
        except Exception:  # pragma: no cover
            pass

    def _maybe_trigger_hot_reload(self) -> None:
        if reload_all_game_data is None or self.game is None:
            return
        try:
            if os.environ.get("RL_FORCE_RELOAD_ON_COLLIDER_SAVE") == "1":
                reload_all_game_data(self.game, force=True)
        except Exception:  # pragma: no cover
            pass

    def _reset_stroke_debug(self) -> None:
        try:
            self.editor_state._colliders_stroke_started = False
            self.editor_state._colliders_stroke_cells = 0
            self.editor_state._colliders_stroke_buildings = set()
        except Exception:  # pragma: no cover
            pass
