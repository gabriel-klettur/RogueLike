import os
import json
from roguelike_engine.config import config

from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_engine.config.config_tiles import TILE_SIZE

import logging
logger = logging.getLogger(__name__)

class InventoryPickupSystem:
    # Drop IDs to ignore temporarily (editor-created)
    recently_created = set()
    """
    ECS system to handle manual inventory pickup action (click to pickup items on map).
    """
    def __init__(self,
                 active_monster_path: str | None = None,
                 active_player_path: str | None = None,
                 drop_path: str | None = None):
        base = config.DATA_DIR
        self.active_monster_path = active_monster_path or os.path.join(base, 'inventory', 'active', 'inventory_monsters.json')
        self.active_player_path = active_player_path or os.path.join(base, 'inventory', 'active', 'inventory_player.json')
        drop_json = drop_path or os.path.join(base, 'inventory', 'active', 'inventory_map.json')
        self.drop_manager = ItemDropManager(drop_json)

    def update(self, world, *args):
        # Suppress pickup when item editor is open
        if hasattr(world, 'state') and getattr(world.state, 'item_editor_state', None) and world.state.item_editor_state.visible:
            return
            return
        # Suppress pickup when a drop is being dragged
        from roguelike_game.ecs.systems.inventory.drop_drag_system import DropDragSystem
        drag_sys = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, DropDragSystem)), None)
        if drag_sys and (getattr(drag_sys, 'potential_drag_eid', None) is not None or getattr(drag_sys, 'dragging_eid', None) is not None):
            return
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
                # Skip editor-created drops (hex UUID without hyphens)
                if '-' not in phys.drop_id:
                    continue
                if phys.drop_id in self.recently_created:
                    continue
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
                    logger.debug(f"[InventoryPickupSystem][DEBUG] pick_up for drop {phys.drop_id} at drop_pos=({drop_pos.x},{drop_pos.y}), player_pos=({player_pos.x},{player_pos.y}), dx={dx}, dy={dy}")
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
