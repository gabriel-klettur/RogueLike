import os
import uuid
import json

from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.monster_instance_component import MonsterInstanceComponent
from roguelike_game.ecs.components.combat.death_timer import DeathTimer


class DeathDropSystem:
    """
    Sistema ECS que maneja el dropeo de ítems al morir NPCs o Player.
    """
    def __init__(self, perf_log=None,
                 active_monster_path: str = os.path.join(os.getcwd(), 'data', 'inventory_monsters.json'),
                 active_player_path: str = os.path.join(os.getcwd(), 'data', 'inventory_player.json'),
                 drop_path: str = os.path.join(os.getcwd(), 'data', 'inventory_map.json')):
        self.perf_log = perf_log
        self.active_monster_path = active_monster_path
        self.active_player_path = active_player_path
        self.drop_manager = ItemDropManager(drop_path)
        self.processed = set()

    def update(self, world, *args):
        comps = world.components
        self.world = world
        inv_store = comps.get('InventoryComponent', {})
        death_store = comps.get('DeathTimer', {})
        pos_store = comps.get('Position', {})

        # Procesar entidades que acaban de morir
        for eid in list(death_store.keys()):
            if eid in self.processed:
                continue
            inv = inv_store.get(eid)
            pos = pos_store.get(eid)
            if not inv or not pos:
                continue
            # Calcular zona
            tx = int(pos.x // TILE_SIZE)
            ty = int(pos.y // TILE_SIZE)
            zone_id = get_zone_for_tile(tx, ty)
            # Crear drops para cada ItemStack
            for stack in inv.slots:
                if stack:
                    drop_id = str(uuid.uuid4())
                    self.drop_manager.create_drop(
                        drop_id,
                        stack.item_id,
                        stack.quantity,
                        zone_id,
                        position={'x': pos.x, 'y': pos.y}
                    )
            # Vaciar inventario
            inv.slots = [None] * inv.capacity
            # Persistir inventario vacío
            self._persist_inventory(eid, inv)
            self.processed.add(eid)

    def _persist_inventory(self, eid: int, inv: InventoryComponent):
        inst = self.world.components.get('MonsterInstanceComponent', {}).get(eid)
        if inst:
            key = inst.instance_id
        else:
            key = str(eid)
        # Leer y actualizar JSON de monstruos
        try:
            with open(self.active_monster_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            data = {}
        if key in data:
            data[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_monster_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2)
        # Leer y actualizar JSON de jugador
        try:
            with open(self.active_player_path, 'r', encoding='utf-8') as f:
                pdata = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            pdata = {}
        if key in pdata:
            pdata[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_player_path, 'w', encoding='utf-8') as f:
                json.dump(pdata, f, indent=2)
