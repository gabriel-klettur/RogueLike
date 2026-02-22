import os
import pygame
from roguelike_game.ecs.components.transform.temp_z_layer import TempZLayer
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.managers.map.item_drop_manager import ItemDropManager

import logging
logger = logging.getLogger(__name__)

class TempZLayerSystem:
    """
    Applies temporary Z-layer overrides and reverts them after TTL.
    Also clears persistence metadata (temp_z_layer) for drops when expired.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.drop_manager = ItemDropManager(os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json'))

    def update(self, world, *args):
        comps = world.components
        temp_store = comps.get('TempZLayer', {})
        z_store = comps.get('ZLayer', {})
        pic_store = comps.get('PhysicalItemComponent', {})
        now = pygame.time.get_ticks()
        expired = []
        for eid, tz in list(temp_store.items()):
            try:
                if now >= tz.expires_at_ms:
                    # Revert ZLayer to base
                    base = tz.base_layer
                    if eid in z_store:
                        z_store[eid] = ZLayer(base)
                    expired.append(eid)
            except Exception as e:
                logger.debug(f" TempZLayerSystem error eid={eid}: {e}")
                expired.append(eid)
        # Cleanup expired temp layers and persistence
        for eid in expired:
            temp_store.pop(eid, None)
            # If it's a persisted drop, remove temp_z_layer metadata from JSON
            pic = pic_store.get(eid)
            if pic is not None:
                try:
                    drop_id = getattr(pic, 'drop_id', None)
                    if drop_id and drop_id in self.drop_manager._data:
                        entry = self.drop_manager._data[drop_id]
                        if 'temp_z_layer' in entry:
                            entry.pop('temp_z_layer', None)
                            self.drop_manager._persist()
                except Exception as e:
                    logger.debug(f" Failed to cleanup temp_z_layer in persistence for eid={eid}: {e}")
