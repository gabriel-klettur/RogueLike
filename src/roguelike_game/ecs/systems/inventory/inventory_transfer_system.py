import os
import json

from roguelike_game.ecs.components.inventory_component import InventoryComponent

import logging
logger = logging.getLogger(__name__)

class InventoryTransferSystem:

    def __init__(self,
                 active_monster_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_monsters.json'),
                 active_player_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_player.json')):
        self.active_monster_path = active_monster_path
        self.active_player_path = active_player_path
        self.world = None

    def update(self, world, *args):
        self.world = world

    def transfer(self, world, item_id: str, quantity: int, source_eid: int, target_eid: int) -> None:
        """
        ECS system to handle item transfers between entities.
        """
        def __init__(self,
            active_monster_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_monsters.json'),
            active_player_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_player.json')):
            self.active_monster_path = active_monster_path
            self.active_player_path = active_player_path

    def update(self, world, *args):
        """No-op update for transfer system"""
        pass

    def transfer(self, world, item_id: str, quantity: int, source_eid: int, target_eid: int) -> None:
        self.world = world
        comps = world.components
        invs = comps.get('InventoryComponent', {})
        source_inv = invs.get(source_eid)
        target_inv = invs.get(target_eid)
        if not source_inv or not target_inv:
            raise ValueError(f"Invalid source or target entity for transfer: {source_eid} -> {target_eid}")
        # Check availability
        if not source_inv.has(item_id, quantity):
            raise ValueError(f"Source eid={source_eid} does not have {quantity}x{item_id}")
        # Perform atomic transfer
        source_inv.remove(item_id, quantity)
        added = target_inv.add(item_id, quantity)
        if not added:
            # Rollback on failure
            source_inv.add(item_id, quantity)
            raise ValueError(f"Transfer failed, target eid={target_eid} has no space for {quantity}x{item_id}")
        # Persist inventories
        self._persist_inventory(source_eid, source_inv)
        self._persist_inventory(target_eid, target_inv)
        # Dispatch event (for UI/logs)
        logger.debug(f"[TransferEvent] Transferred {quantity}x{item_id} from eid={source_eid} to eid={target_eid}")

    def _persist_inventory(self, eid: int, inv: InventoryComponent) -> None:
        inst = self.world.components.get('MonsterInstanceComponent', {}).get(eid)
        if inst:
            key = inst.instance_id
        else:
            key = str(eid)
        key = str(eid)
        # Update monster inventory JSON
        try:
            with open(self.active_monster_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            data = {}
        if key in data:
            data[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_monster_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2)
        # Update player inventory JSON
        try:
            with open(self.active_player_path, 'r', encoding='utf-8') as f:
                pdata = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            pdata = {}
        if key in pdata:
            pdata[key]['slots'] = inv.serialize().get('slots')
            with open(self.active_player_path, 'w', encoding='utf-8') as f:
                json.dump(pdata, f, indent=2)
