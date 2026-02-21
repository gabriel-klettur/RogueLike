from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Optional

import pygame

from .. import resize as rz
from .. import split_drag as split
from ..utils import compute_spawner_handle_rects, find_building_in_world_by_id
from .helpers import reset_selected_building_size
from .mouse_left_common import LeftClickContext


@dataclass
class BuildingHandleInteractions:
    """Coordinates overlay-handle operations (reset, resize, split, delete)."""

    context: LeftClickContext

    def run(self) -> bool:
        self._hydrate_state()
        if self.world_building is None:
            return False
        if self._handle_reset():
            return True
        if self._handle_resize():
            return True
        if self._handle_split():
            return True
        if self._handle_delete():
            return True
        return False

    # ------------------------------------------------------------------
    # State helpers
    # ------------------------------------------------------------------
    def _hydrate_state(self) -> None:
        self.selected_bid: Optional[int] = self.context.get_selected_building_id()
        self.world_building: Any = self._resolve_selected_building(self.selected_bid)
        self.building_under_cursor: Any = self.context.pick_building_under_cursor()
        if self.world_building is None and self.building_under_cursor is not None:
            self.world_building = self.building_under_cursor
        self.view = self.context.view
        self.rect_cache = self._fetch_handle_rects()

    def _resolve_selected_building(self, selected_bid: Optional[int]) -> Any:
        if selected_bid is None:
            return None
        visuals = self.context.visuals
        if visuals is not None:
            building = self.context.guard(
                "visuals._find_building_entity_by_id",
                lambda: visuals._find_building_entity_by_id(int(selected_bid)),
            )
            if building is not None:
                return building
        return find_building_in_world_by_id(self.context.world, int(selected_bid))

    def _fetch_handle_rects(self) -> Dict[str, Optional[pygame.Rect]]:
        delete_rect = getattr(self.view, "_last_selected_delete_rect", None) if self.view is not None else None
        reset_rect = getattr(self.view, "_last_selected_reset_rect", None) if self.view is not None else None
        resize_rect = getattr(self.view, "_last_selected_resize_rect", None) if self.view is not None else None
        split_rect = getattr(self.view, "_last_split_handle_rect", None) if self.view is not None else None
        if None not in (delete_rect, reset_rect, resize_rect):
            return {
                "delete": delete_rect,
                "reset": reset_rect,
                "resize": resize_rect,
                "split": split_rect,
            }
        rects = compute_spawner_handle_rects(self.context.camera, self.world_building)
        return {
            "delete": delete_rect or rects.get("delete"),
            "reset": reset_rect or rects.get("reset"),
            "resize": resize_rect or rects.get("resize"),
            "split": split_rect,
        }

    # ------------------------------------------------------------------
    # Handlers
    # ------------------------------------------------------------------
    def _handle_reset(self) -> bool:
        rect = self.rect_cache.get("reset")
        if rect is not None and pygame.Rect(rect).collidepoint(self.context.mx, self.context.my):
            if reset_selected_building_size(self.context.handler, self.selected_bid):
                return True
        return False

    def _handle_resize(self) -> bool:
        rect = self.rect_cache.get("resize")
        if rect is None or not pygame.Rect(rect).collidepoint(self.context.mx, self.context.my):
            return False
        self._auto_select_target_for_resize()
        started = bool(
            self.context.guard(
                "rz.start_resize",
                lambda: rz.start_resize(self.context.editor_ctx, self.context.event),
                default=False,
            )
        )
        self.context.log_debug(
            "[SpawnerEditor] LMB start resize: sel_bid=%s started=%s",
            self.selected_bid,
            started,
        )
        return started

    def _auto_select_target_for_resize(self) -> None:
        if self.selected_bid is not None or self.building_under_cursor is None:
            return
        if self.context.is_building_hidden(self.building_under_cursor):
            return
        if not self.context.is_same_instance(self.building_under_cursor):
            return
        bid = getattr(self.building_under_cursor, "id", None)
        if bid is None:
            return
        self.context.set_selected_building_id(int(bid))
        self.selected_bid = int(bid)
        self.context.log_debug(
            "[SpawnerEditor] LMB autoselected building on resize click: bid=%s",
            bid,
        )

    def _handle_split(self) -> bool:
        rect = self.rect_cache.get("split")
        if rect is None or not pygame.Rect(rect).collidepoint(self.context.mx, self.context.my):
            return False
        target_bid = self.selected_bid or self._pick_target_bid_for_split()
        if target_bid is None:
            return False
        began = bool(
            self.context.guard(
                "split.begin_split_drag",
                lambda: split.begin_split_drag(self.context.editor_ctx, int(target_bid), self.context.event),
                default=False,
            )
        )
        return began

    def _pick_target_bid_for_split(self) -> Optional[int]:
        visuals = self.context.visuals
        if visuals is None:
            return None
        candidate = self.context.guard(
            "visuals.pick_visual_building_under_cursor",
            lambda: visuals.pick_visual_building_under_cursor(self.context.mx, self.context.my),
        )
        if candidate is None:
            return None
        bid = getattr(candidate, "id", None)
        if bid is None:
            return None
        if self.context.is_building_hidden(candidate) or not self.context.is_same_instance(candidate):
            return None
        self.context.set_selected_building_id(int(bid))
        return int(bid)

    def _handle_delete(self) -> bool:
        rect = self.rect_cache.get("delete")
        if rect is None or not pygame.Rect(rect).collidepoint(self.context.mx, self.context.my):
            return False
        if self.selected_bid is None:
            return False
        return self._delete_selected_building(self.selected_bid)

    def _delete_selected_building(self, bid: int) -> bool:
        from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
            load_buildings_instances as load_buildings,
            write_buildings_instances as write_buildings,
        )
        data = self.context.guard("load_buildings_instances", load_buildings, default=[]) or []
        out = []
        removed = False
        for entry in data:
            try:
                if int(entry.get("id")) == int(bid):
                    removed = True
                    continue
            except Exception:  # noqa: BLE001 - tolerant against data issues
                pass
            out.append(entry)
        if removed:
            self.context.guard("write_buildings_instances", lambda: write_buildings(out))
        self._remove_building_from_visuals(bid)
        self._remove_building_from_world(bid)
        self.context.clear_building_selection()
        return True

    def _remove_building_from_visuals(self, bid: int) -> None:
        from roguelike_editors.spawner.services.persistence import (
            remove_visual_refs_by_building_id,
        )

        self.context.guard(
            "remove_visual_refs_by_building_id",
            lambda: remove_visual_refs_by_building_id(int(bid)),
        )

    def _remove_building_from_world(self, bid: int) -> None:
        visuals = self.context.visuals
        if visuals is not None:
            self.context.guard(
                "visuals._remove_building_entity_by_id",
                lambda: visuals._remove_building_entity_by_id(int(bid)),
            )
