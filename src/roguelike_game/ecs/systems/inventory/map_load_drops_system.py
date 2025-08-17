import os
import json
import jsonschema
from pathlib import Path
from jsonschema import Draft7Validator, RefResolver
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.map_utils import get_zone_offset
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.transform.temp_z_layer import TempZLayer
import pygame
from roguelike_engine.config.config_z_layer import DEFAULT_Z
from roguelike_game.ecs.components.item_models import load_items
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale

import logging
logger = logging.getLogger(__name__)

class MapLoadDropsSystem:
    """
    Sistema ECS que carga y spawnea ítems en el mapa a partir de inventory_map.json.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        # Validar esquema de instancias de ítems con Draft7Validator y RefResolver
        schema_path = os.path.join(os.getcwd(), 'schemas', 'items', 'instances.json')
        with open(schema_path, 'r', encoding='utf-8') as sf:
            instances_schema = json.load(sf)
        schema_uri = Path(schema_path).resolve().as_uri()
        resolver = RefResolver(base_uri=schema_uri, referrer=instances_schema)
        validator = Draft7Validator(instances_schema, resolver=resolver)
        with open(path, 'r', encoding='utf-8') as df:
            drops_raw = json.load(df)
        # Flatten nested 'map' key if JSON is wrongly nested
        if isinstance(drops_raw, dict) and 'map' in drops_raw:
            drops_raw = drops_raw['map']
        try:
            validator.validate(drops_raw)
        except jsonschema.ValidationError as e:
            logging.error(f" Schema validation failed: {e.message}. Continuing without drops.")
        # Instanciar el ItemDropManager con datos validados
        self.drop_manager = ItemDropManager(path)
        # Flatten nested 'map' key in drop_manager data if present
        if isinstance(self.drop_manager._data, dict) and 'map' in self.drop_manager._data:
            self.drop_manager._data = self.drop_manager._data['map']
            self.drop_manager._persist()
        if self.drop_manager.path != path:
            self.drop_manager = ItemDropManager(self.drop_manager.path)
        items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        self._initial_path = path
        self._loaded = False

        self._spawned = set()

    def update(self, world, camera=None):
        """
        Spawn new drops from inventory_map.json each frame.
        """
        # Reload drop data from file to sync external changes only for custom drop paths
        if self.drop_manager.path != self._initial_path:
            self.drop_manager = ItemDropManager(self.drop_manager.path)
        # Reload drop data from file to sync external changes

        # Use in-memory drop data en lugar de leer archivo
        drops_dict = self.drop_manager._data or {}

        for drop_id, data in drops_dict.items():
            if drop_id in self._spawned:
                continue

            item_id = data['item_id']
            quantity = data['quantity']
            zone_id = data.get('zone_id')
            offset_tx, offset_ty = get_zone_offset(zone_id)
            # Convertir coordenadas a píxeles
            if 'tile' in data:
                drop_tx, drop_ty = data['tile']['x'], data['tile']['y']
                global_tx = offset_tx + drop_tx
                global_ty = offset_ty + drop_ty
                # Centro del tile en píxeles
                tile_cx = global_tx * TILE_SIZE + TILE_SIZE // 2
                tile_cy = global_ty * TILE_SIZE + TILE_SIZE // 2
                # Posición temporal; se ajustará tras conocer tamaño del sprite escalado
                pos = Position(tile_cx, tile_cy)
            elif 'position' in data:
                coords = data['position']
                pos = Position(coords['x'], coords['y'])
            else:
                raise ValueError(f"Drop '{drop_id}' requiere 'tile' o 'position'")

            eid = world.create_entity()
            world.components['PhysicalItemComponent'][eid] = PhysicalItemComponent(
                drop_id, item_id, quantity, zone_id, created_at=data.get('created_at')
            )
            world.components['Position'][eid] = pos
            world.components['CollectibleComponent'][eid] = CollectibleComponent()

            model = self.items.get(item_id)
            # Use z_layer from data if present, else from model, else DEFAULT_Z
            layer = data.get('z_layer') or getattr(model, 'z_layer', None) or DEFAULT_Z
            world.components['ZLayer'][eid] = ZLayer(layer)
            # Apply temporary z-layer if metadata present
            temp_meta = data.get('temp_z_layer')
            if temp_meta:
                try:
                    temp_layer = int(temp_meta.get('layer', layer))
                    ttl_ms = int(temp_meta.get('ttl_ms', 0))
                    if ttl_ms > 0:
                        now = pygame.time.get_ticks()
                        expires_at = now + ttl_ms
                        # Override current render layer to temp
                        world.components['ZLayer'][eid] = ZLayer(temp_layer)
                        world.components['TempZLayer'][eid] = TempZLayer(temp_layer, layer, expires_at)
                except Exception as e:
                    logger.debug(f" temp_z_layer init failed for drop '{drop_id}': {e}")

            if model:
                icon = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
                if isinstance(icon, list):
                    icon = icon[0]
                if icon:
                    world.components['Sprite'][eid] = Sprite(icon)
                    scale_factor = getattr(model, 'scale_map', 1.0)
                    world.components['Scale'][eid] = Scale(scale_factor)
                    # Si se originó en 'tile', centrar el sprite en el centro del tile
                    if 'tile' in data:
                        try:
                            sprite = world.components['Sprite'][eid]
                            sw = sprite.image.get_width()
                            sh = sprite.image.get_height()
                            final_w = int(sw * scale_factor)
                            final_h = int(sh * scale_factor)
                            # Ajustar Position desde centro a esquina superior izquierda
                            cx, cy = pos.x, pos.y
                            pos.x = cx - final_w // 2
                            pos.y = cy - final_h // 2
                        except Exception as e:
                            logger.debug(f" Centering drop failed for '{drop_id}': {e}")

            logger.debug(f" Spawned drop '{drop_id}' item '{item_id}' at ({pos.x},{pos.y}) zone '{zone_id}' eid={eid}")
            self._spawned.add(drop_id)
            self._loaded = True

