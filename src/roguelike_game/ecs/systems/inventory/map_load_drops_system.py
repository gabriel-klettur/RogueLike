import os
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.transform.position import Position

class MapLoadDropsSystem:
    """
    Sistema ECS que carga y spawnea ítems en el mapa a partir de inventory_map.json.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        path = os.path.join(os.getcwd(), 'data', 'inventory_map.json')
        self.drop_manager = ItemDropManager(path)
        self._loaded = False

    def update(self, world, camera=None):
        print(f"[MapLoadDropsSystem] update called, loaded={self._loaded}")
        if self._loaded:
            return
        drops_dict = self.drop_manager._data
        # Filtrar drops por zona
        current_zone = world.map_manager.name
        print(f"[MapLoadDropsSystem] current_zone={current_zone}, total_drops={len(drops_dict)}")
        for drop_id, data in drops_dict.items():
            # DEBUG: zone filter disabled for debug
            # if data.get('zone_id') != current_zone:
            #     continue
            item_id = data['item_id']
            quantity = data['quantity']
            zone_id = data.get('zone_id')
            # Convertir coordenadas a píxeles
            if 'tile' in data:
                tx, ty = data['tile']['x'], data['tile']['y']
                px, py = world.map_manager.get_spawn_pixel((tx, ty))
                pos = Position(px, py)
            else:
                coords = data['position']
                pos = Position(coords['x'], coords['y'])
            eid = world.create_entity()
            world.components['PhysicalItemComponent'][eid] = PhysicalItemComponent(
                drop_id, item_id, quantity, zone_id
            )
            world.components['Position'][eid] = pos
            world.components['CollectibleComponent'][eid] = CollectibleComponent()
            print(f"[MapLoadDropsSystem] Spawned drop '{drop_id}' item '{item_id}' at ({pos.x},{pos.y}) zone '{zone_id}' eid={eid}")
        self._loaded = True
