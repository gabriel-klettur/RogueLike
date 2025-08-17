import os
import uuid
import json

from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.map_utils import get_zone_offset
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.input_component import InputComponent


class InventoryDropSystem:
    """
    ECS system to handle manual inventory drop action.
    """
    def __init__(self,
                 active_monster_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_monsters.json'),
                 active_player_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_player.json'),
                 drop_path: str = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')):
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
                    # Create drop on nearest free walkable tile (no-collision by tile)
                    g_tx = int(pos.x // TILE_SIZE)
                    g_ty = int(pos.y // TILE_SIZE)
                    zone_id = get_zone_for_tile(g_tx, g_ty)
                    offx, offy = get_zone_offset(zone_id)
                    # Collect occupied local tiles for this zone
                    occupied = self._collect_occupied_tiles(world, zone_id, offx, offy)
                    map_manager = getattr(world, 'map_manager', None)
                    placed_local = None
                    for cx, cy in self._iter_spiral_tiles(g_tx, g_ty, 12):
                        l_tx, l_ty = cx - offx, cy - offy
                        if (l_tx, l_ty) in occupied:
                            continue
                        if map_manager and not map_manager.is_walkable(cx, cy):
                            continue
                        placed_local = (l_tx, l_ty)
                        break
                    if placed_local is None:
                        placed_local = (g_tx - offx, g_ty - offy)
                    drop_id = str(uuid.uuid4())
                    self.drop_manager.create_drop(
                        drop_id,
                        stack.item_id,
                        stack.quantity,
                        zone_id,
                        tile={'x': placed_local[0], 'y': placed_local[1]}
                    )
                    # Remove from inventory
                    inv.slots[inv.slots.index(stack)] = None
                    # Persist actor inventory
                    self._persist_inventory(eid, inv)
                    break
            inp.drop = False

    def _collect_occupied_tiles(self, world, zone_id: str, offx: int, offy: int):
        occupied = set()
        try:
            drops = self.drop_manager._data or {}
            for _, data in drops.items():
                if data.get('zone_id') != zone_id:
                    continue
                if 'tile' in data:
                    lt = data['tile']
                    occupied.add((int(lt['x']), int(lt['y'])))
                elif 'position' in data:
                    pos = data['position']
                    gtx = int(pos['x'] // TILE_SIZE)
                    gty = int(pos['y'] // TILE_SIZE)
                    occupied.add((gtx - offx, gty - offy))
        except Exception:
            pass
        # Entities already spawned
        comps = getattr(world, 'components', {})
        phys = comps.get('PhysicalItemComponent', {})
        positions = comps.get('Position', {})
        for deid, pic in list(phys.items()):
            try:
                if getattr(pic, 'zone_id', None) != zone_id:
                    continue
                p = positions.get(deid)
                if not p:
                    continue
                gtx = int(p.x // TILE_SIZE)
                gty = int(p.y // TILE_SIZE)
                occupied.add((gtx - offx, gty - offy))
            except Exception:
                continue
        return occupied

    def _iter_spiral_tiles(self, cx: int, cy: int, max_radius: int):
        # r = 0 -> center first
        yield (cx, cy)
        for r in range(1, max_radius + 1):
            x0, x1 = cx - r, cx + r
            y0, y1 = cy - r, cy + r
            for x in range(x0, x1 + 1):
                yield (x, y0)
                yield (x, y1)
            for y in range(y0 + 1, y1):
                yield (x0, y)
                yield (x1, y)

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
