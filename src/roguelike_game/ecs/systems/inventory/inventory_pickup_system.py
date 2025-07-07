import os
import json

from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.monster_instance_component import MonsterInstanceComponent
from roguelike_engine.config.config_tiles import TILE_SIZE


class InventoryPickupSystem:
    """
    ECS system to handle manual inventory pickup action (click to pickup items on map).
    """
    def __init__(self,
                 active_monster_path: str = os.path.join(os.getcwd(), 'data', 'inventory_monsters.json'),
                 active_player_path: str = os.path.join(os.getcwd(), 'data', 'inventory_player.json'),
                 drop_path: str = os.path.join(os.getcwd(), 'data', 'inventory_map.json')):
        self.active_monster_path = active_monster_path
        self.active_player_path = active_player_path
        self.drop_manager = ItemDropManager(drop_path)

    def update(self, world, *args):
        # Reload drop data from file to sync new drops
        self.drop_manager = ItemDropManager(self.drop_manager.path)
        comps = world.components
        self.world = world
        invs = comps.get('InventoryComponent', {})
        phys_items = comps.get('PhysicalItemComponent', {})
        collectibles = comps.get('CollectibleComponent', {})
        inputs = comps.get('InputComponent', {})
        positions = comps.get('Position', {})

        for eid, inp in inputs.items():
            if not getattr(inp, 'click', False):
                continue
            inv = invs.get(eid)
            player_pos = positions.get(eid)
            if not inv or not player_pos:
                continue
            # Check nearby drops
            for drop_eid, phys in list(phys_items.items()):
                if drop_eid not in collectibles:
                    continue
                drop_pos = positions.get(drop_eid)
                if not drop_pos:
                    continue
                # Simple proximity check
                dx = abs(drop_pos.x - player_pos.x)
                dy = abs(drop_pos.y - player_pos.y)
                if dx <= TILE_SIZE and dy <= TILE_SIZE:
                    # Add to inventory, ignore overflow
                    inv.add(phys.item_id, phys.quantity)
                    # Persist actor inventory
                    self._persist_inventory(eid, inv)
                    # Remove drop from map
                    self.drop_manager.pick_up(phys.drop_id)
                    # Remove entity
                    world.remove_entity(drop_eid)
            # Reset click to avoid repeated pickup
            inp.click = False

    def _persist_inventory(self, eid: int, inv: InventoryComponent):
        inst = self.world.components.get('MonsterInstanceComponent', {}).get(eid)
        if inst:
            key = inst.instance_id
        else:
            key = str(eid)
        # Monster inventory
        try:
            with open(self.active_monster_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            data = {}
        if key in data:
            data[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_monster_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2)
        # Player inventory
        try:
            with open(self.active_player_path, 'r', encoding='utf-8') as f:
                pdata = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            pdata = {}
        if key in pdata:
            pdata[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_player_path, 'w', encoding='utf-8') as f:
                json.dump(pdata, f, indent=2)
