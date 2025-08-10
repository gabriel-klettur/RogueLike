from roguelike_engine.config.config_tiles import TILE_SIZE

import logging
logger = logging.getLogger(__name__)

class TeleportSystem:
    """
    Sistema ECS que detecta colisión jugador↔portal y ejecuta teletransporte.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, *args):
        components = world.components
        positions = components.get('Position', {})
        teleports = components.get('TeleportComponent', {})
        player_tags = components.get('PlayerTagComponent', {})
        if not teleports or not player_tags:
            return
        player_eid = next(iter(player_tags))
        player_pos = positions.get(player_eid)
        if not player_pos:
            return
        for eid, tp in teleports.items():
            item_pos = positions.get(eid)
            if not item_pos:
                continue
            dx = player_pos.x - item_pos.x
            dy = player_pos.y - item_pos.y
            if dx*dx + dy*dy <= TILE_SIZE * TILE_SIZE:
                logger.debug(f"[TeleportSystem] Teleporting player to {tp.dest_map} at ({tp.dest_x}, {tp.dest_y})")
                # TODO: integrar con el gestor de mapas para cambiar de nivel
                break
