from __future__ import annotations

import json
import logging
import os
import uuid
from typing import Any, Optional

import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_z_layer import DEFAULT_Z
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_game.managers.items.loader import ItemsLoader
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
from roguelike_game.managers.map.item_drop_manager import ItemDropManager


class DropService:
    """Operaciones de spawn/delete y utilidades de mundo/cámara para el Items Editor."""

    def __init__(self, controller: Any) -> None:
        self.controller = controller

    # --- Helpers de mundo/cámara ---
    def world_and_camera(self):
        game = getattr(self.controller, 'game', None)
        if not game or not hasattr(game, 'ecs'):
            return None, None
        world = getattr(game.ecs, 'ecs_world', None)
        camera = getattr(game, 'camera', None)
        return world, camera

    def find_drop_entity_at(self, sx: int, sy: int) -> Optional[int]:
        world, camera = self.world_and_camera()
        if not world or not camera:
            return None
        comps = world.components
        hovered = None
        max_layer = -float('inf')
        try:
            for eid in world.get_entities_in_camera(
                camera, 'PhysicalItemComponent', 'Sprite', 'Position', 'ZLayer'
            ):
                pos2 = comps['Position'][eid]
                sprite = comps['Sprite'][eid]
                scale_comp = comps.get('Scale', {}).get(eid)
                scale = scale_comp.scale if scale_comp else 1.0
                w, h = sprite.image.get_size()
                w = int(w * scale * camera.zoom)
                h = int(h * scale * camera.zoom)
                sx2, sy2 = camera.apply((pos2.x, pos2.y))
                rect = pygame.Rect(sx2, sy2, w, h)
                if rect.collidepoint(sx, sy):
                    layer = comps['ZLayer'][eid].layer
                    if layer >= max_layer:
                        hovered = eid
                        max_layer = layer
        except Exception:
            pass
        return hovered

    # --- Spawn/Delete API ---
    def spawn_at_player(self, item_id: str) -> None:
        controller = self.controller
        if not hasattr(controller, 'game') or not hasattr(controller.game, 'ecs'):
            return
        pos = controller.game.ecs.ecs_world.player_position
        if not pos:
            return
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        drop_manager = ItemDropManager(inv_map_path)
        drop_id = uuid.uuid4().hex
        tile_x = int(pos.x) // TILE_SIZE
        tile_y = int(pos.y) // TILE_SIZE
        zone_id = get_zone_for_tile(tile_x, tile_y)
        drop_manager.create_drop(drop_id, item_id, 1, zone_id, position={'x': pos.x, 'y': pos.y})
        InventoryPickupSystem.recently_created.add(drop_id)
        try:
            controller.instances_controller.reload_data()
        except Exception:
            pass

    def spawn_item_at_screen_pos(self, sx: int, sy: int) -> bool:
        controller = self.controller
        if not controller.model.spawn_item_id:
            return False
        world, camera = self.world_and_camera()
        if not world or not camera:
            return False
        wx = sx / camera.zoom + camera.offset_x
        wy = sy / camera.zoom + camera.offset_y
        tile_x = int(wx) // TILE_SIZE
        tile_y = int(wy) // TILE_SIZE
        zone_id = get_zone_for_tile(tile_x, tile_y)
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        drop_manager = ItemDropManager(inv_map_path)
        drop_id = uuid.uuid4().hex
        drop_manager.create_drop(drop_id, controller.model.spawn_item_id, 1, zone_id, position={'x': wx, 'y': wy})
        try:
            InventoryPickupSystem.recently_created.add(drop_id)
        except Exception:
            pass
        try:
            self._spawn_drop_entity_now(world, drop_id, controller.model.spawn_item_id, 1, zone_id, wx, wy)
        except Exception:
            logging.getLogger(__name__).exception("[DropService] immediate spawn failed")
        try:
            controller.instances_controller.reload_data()
        except Exception:
            pass
        return True

    def delete_drop_at_screen_pos(self, sx: int, sy: int) -> bool:
        world, camera = self.world_and_camera()
        if not world or not camera:
            return False
        eid = self.find_drop_entity_at(sx, sy)
        if eid is None:
            return False
        comps = world.components
        phys = comps.get('PhysicalItemComponent', {}).get(eid)
        if not phys:
            return False
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        drop_manager = ItemDropManager(inv_map_path)
        ok = drop_manager.pick_up(phys.drop_id)
        if ok:
            try:
                world.remove_entity(eid)
            except Exception:
                pass
            try:
                self.controller.instances_controller.reload_data()
            except Exception:
                pass
            return True
        return False

    # --- Internals ---
    def _spawn_drop_entity_now(
        self,
        world,
        drop_id: str,
        item_id: str,
        quantity: int,
        zone_id: str,
        x: float,
        y: float,
    ) -> None:
        try:
            mlds = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, MapLoadDropsSystem)), None)
            if mlds:
                mlds._spawned.add(drop_id)
        except Exception:
            pass
        controller = self.controller
        if not hasattr(controller, '_items_models') or not controller._items_models:
            try:
                controller._items_models, _assets = ItemsLoader().load()
            except Exception:
                controller._items_models = {}
        model = controller._items_models.get(item_id)
        eid = world.create_entity()
        world.components['PhysicalItemComponent'][eid] = PhysicalItemComponent(drop_id, item_id, quantity, zone_id)
        world.components['Position'][eid] = Position(x, y)
        world.components['CollectibleComponent'][eid] = CollectibleComponent()
        layer = getattr(model, 'z_layer', None) or DEFAULT_Z
        world.components['ZLayer'][eid] = ZLayer(layer)
        if model:
            icon = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
            if isinstance(icon, list):
                icon = icon[0]
            if icon:
                world.components['Sprite'][eid] = Sprite(icon)
                world.components['Scale'][eid] = Scale(getattr(model, 'scale_map', 1.0))
