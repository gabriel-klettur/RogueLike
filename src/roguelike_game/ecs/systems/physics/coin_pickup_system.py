import os
from roguelike_game.ecs.components.item_models import load_items
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.managers.map.item_drop_manager import ItemDropManager

import logging
logger = logging.getLogger(__name__)

class CoinPickupSystem:
    """
    Sistema ECS que automáticamente recoge monedas al colisionar con el jugador.
    """
    def __init__(self, perf_log=None, items_path=None):
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        # Gestor de drops en mapa para persistir recogidas
        path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        self.drop_manager = ItemDropManager(path)

    def update(self, world, *args):
        comps = world.components
        positions = comps.get('Position', {})
        phys_items = comps.get('PhysicalItemComponent', {})
        collectibles = comps.get('CollectibleComponent', {})
        invs = comps.get('InventoryComponent', {})
        player_tags = comps.get('PlayerTagComponent', {})

        # Identificar jugador
        player_eid = next(iter(player_tags), None)
        if player_eid is None:
            return
        player_pos = positions.get(player_eid)
        inv = invs.get(player_eid)
        if not player_pos or not inv:
            return

        # Recoger monedas al colisionar
        for eid, phys in list(phys_items.items()):
            if eid not in collectibles:
                continue
            model = self.items.get(phys.item_id)
            if not model:
                continue
            # Auto-recoger solo monedas de oro
            if phys.item_id != 'gold':
                continue
            # Debug
            
            item_pos = positions.get(eid)
            if not item_pos:
                continue
            dx = player_pos.x - item_pos.x
            dy = player_pos.y - item_pos.y
            dist_sq = dx * dx + dy * dy
            # Si está en rango de colisión
            if dist_sq <= TILE_SIZE * TILE_SIZE:
                # Añadir al inventario
                logger.debug(f"[CoinPickupSystem] Player {player_eid} recogió {phys.quantity}x {phys.item_id}")
                inv.add(phys.item_id, phys.quantity)
                # Persistir inventario: invocar el primer sistema que exponga _persist_inventory
                for sys in getattr(world, 'update_systems', []) or []:
                    persist = getattr(sys, '_persist_inventory', None)
                    if callable(persist):
                        persist(player_eid, inv)
                        break
                # Persistir eliminación en inventory_map.json
                self.drop_manager.pick_up(phys.drop_id)
                # Eliminar entidad de moneda
                world.remove_entity(eid)
