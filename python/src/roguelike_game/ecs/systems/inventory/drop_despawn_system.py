import os
import time
import logging
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.managers.items.loader import ItemsLoader

logger = logging.getLogger(__name__)

class DropDespawnSystem:
    """
    Removes ground item drops after their per-item lifetime expires.
    Uses ItemModel.despawn_time (seconds). If no despawn_time, the drop persists.
    Persists removals through ItemDropManager so inventory_map.json stays in sync.
    """
    def __init__(self, perf_log=None, items_path=None, drop_path=None):
        self.perf_log = perf_log
        # Load items from SQLite
        self.items, _assets = ItemsLoader().load()
        if drop_path is None:
            drop_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        self.drop_manager = ItemDropManager(drop_path)

    def update(self, world, *args):
        comps = world.components
        phys_items = comps.get('PhysicalItemComponent', {})
        positions = comps.get('Position', {})  # optional for debug logs
        collectibles = comps.get('CollectibleComponent', {})
        now = time.time()

        # Ensure we see external changes (e.g., editor spawns or pickups)
        self.drop_manager = ItemDropManager(self.drop_manager.path)

        for eid, phys in list(phys_items.items()):
            # Only consider actual ground drops that are collectible
            if eid not in collectibles:
                continue
            model = self.items.get(phys.item_id)
            if not model:
                continue
            despawn_time = getattr(model, 'despawn_time', None)
            if not despawn_time or despawn_time <= 0:
                continue
            # Backfill created_at if missing (component and persistence)
            if getattr(phys, 'created_at', None) is None:
                phys.created_at = now
                entry = self.drop_manager._data.get(phys.drop_id)
                if entry is not None and 'created_at' not in entry:
                    entry['created_at'] = now
                    self.drop_manager._persist()
            # Check expiration
            if phys.created_at is None:
                continue
            elapsed = now - phys.created_at
            if elapsed >= despawn_time:
                pos = positions.get(eid)
                logger.debug(f"[DropDespawnSystem] Despawning drop {phys.drop_id} ({phys.item_id} x{phys.quantity}) after {elapsed:.1f}s at {getattr(pos, 'x', '?')},{getattr(pos, 'y', '?')}")
                # Remove from persistence and ECS
                self.drop_manager.pick_up(phys.drop_id)
                world.remove_entity(eid)
