"""
Module: minimap_update_system.py
ECS system that updates the minimap each frame based on the player's position.
Replaces the procedural _step_minimap logic formerly in update_manager.py.
"""
import logging

logger = logging.getLogger(__name__)


class MinimapUpdateSystem:
    """
    Reads the player entity's Position and delegates to the Minimap facade
    stored on ``world.minimap``.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, camera):
        minimap = getattr(world, 'minimap', None)
        if minimap is None:
            return

        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return

        pos = world.components.get('Position', {}).get(player_eid)
        if pos is None:
            return

        # Gather data the minimap needs
        map_manager = getattr(world, 'map_manager', None)
        tiles = getattr(map_manager, 'tiles_in_region', []) if map_manager else []

        buildings_mgr = getattr(world, 'entities_manager', None)
        buildings_list = getattr(buildings_mgr, 'buildings', None) if buildings_mgr else None
        # Fallback: world.buildings may be the raw list
        if buildings_list is None:
            buildings_list = getattr(world, 'buildings', None)

        minimap.update(
            player_pos=(pos.x, pos.y),
            tiles=tiles,
            buildings=buildings_list,
            world=world,
        )
