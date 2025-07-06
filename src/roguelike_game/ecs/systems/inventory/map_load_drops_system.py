import os
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.map_utils import get_zone_offset
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import DEFAULT_Z
from roguelike_game.ecs.components.item_models import load_items
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale

class MapLoadDropsSystem:
    """
    Sistema ECS que carga y spawnea ítems en el mapa a partir de inventory_map.json.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        path = os.path.join(os.getcwd(), 'data', 'inventory_map.json')
        self.drop_manager = ItemDropManager(path)
        items_path = os.path.join(os.getcwd(), 'data', 'items.json')
        self.items = load_items(items_path)
        self._loaded = False

    def update(self, world, camera=None):
        

        if self._loaded:
            return
        drops_dict = self.drop_manager._data

        for drop_id, data in drops_dict.items():

            item_id = data['item_id']
            quantity = data['quantity']
            zone_id = data.get('zone_id')
            offset_tx, offset_ty = get_zone_offset(zone_id)
            # Convertir coordenadas a píxeles
            if 'tile' in data:
                drop_tx, drop_ty = data['tile']['x'], data['tile']['y']
                global_tx = offset_tx + drop_tx
                global_ty = offset_ty + drop_ty
                px, py = world.map_manager.get_spawn_pixel((global_tx, global_ty))
                pos = Position(px, py)
            else:
                coords = data['position']
                pos = Position(coords['x'] + offset_tx * TILE_SIZE, coords['y'] + offset_ty * TILE_SIZE)
            eid = world.create_entity()
            world.components['PhysicalItemComponent'][eid] = PhysicalItemComponent(
                drop_id, item_id, quantity, zone_id
            )
            world.components['Position'][eid] = pos
            world.components['CollectibleComponent'][eid] = CollectibleComponent()
            # Asignar ZLayer para ordenar renderizado
            model = self.items.get(item_id)
            # Priorizar z_layer definido en inventory_map.json
            layer = data.get('z_layer')
            if layer is None:
                # Si no hay override en el drop, usar z_layer del modelo o DEFAULT_Z
                layer = getattr(model, 'z_layer', None) if model else None
            if layer is None:
                layer = DEFAULT_Z
            world.components['ZLayer'][eid] = ZLayer(layer)
            # Añadir Sprite y Scale para que RendererManager los dibuje en orden Z
            if model:
                # elegir icon_small o fallback icon
                if getattr(model, "icon_small", None):
                    icon_path = model.icon_small
                else:
                    ic = getattr(model, "icon", None)
                    icon_path = ic[0] if isinstance(ic, list) else ic
                if icon_path:
                    world.components["Sprite"][eid] = Sprite(icon_path)
                    world.components["Scale"][eid] = Scale(getattr(model, "scale_map", 1.0))
            print(f"[MapLoadDropsSystem] Spawned drop '{drop_id}' item '{item_id}' at ({pos.x},{pos.y}) zone '{zone_id}' eid={eid}")
        self._loaded = True
