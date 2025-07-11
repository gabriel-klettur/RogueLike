import os
import uuid
import json

from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.input_component import InputComponent


class InventoryDropSystem:
    """
    ECS system to handle manual inventory drop action.
    """
    def __init__(self,
                 active_monster_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_monsters.json'),
                 active_player_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_player.json'),
                 drop_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_map.json')):
        self.active_monster_path = active_monster_path
        self.active_player_path = active_player_path
        self.drop_manager = ItemDropManager(drop_path)

    def update(self, world, *args):
        comps = world.components
        invs = comps.get('InventoryComponent', {})
        inputs = comps.get('InputComponent', {})
        positions = comps.get('Position', {})
        # Process drop requests
        for eid, inp in inputs.items():
            if not getattr(inp, 'drop', False):
                continue
            inv = invs.get(eid)
            pos = positions.get(eid)
            if not inv or not pos:
                inp.drop = False
                continue
            # Find first non-empty slot to drop
            for stack in list(inv.slots):
                if stack:
                    # Create drop on map
                    tx = int(pos.x // TILE_SIZE)
                    ty = int(pos.y // TILE_SIZE)
                    zone_id = get_zone_for_tile(tx, ty)
                    drop_id = str(uuid.uuid4())
                    self.drop_manager.create_drop(
                        drop_id,
                        stack.item_id,
                        stack.quantity,
                        zone_id,
                        position={'x': pos.x, 'y': pos.y}
                    )
                    # Remove from inventory
                    inv.slots[inv.slots.index(stack)] = None
                    # Persist actor inventory
                    self._persist_inventory(eid, inv)
                    break
            inp.drop = False

    def _persist_inventory(self, eid: int, inv: InventoryComponent):
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
