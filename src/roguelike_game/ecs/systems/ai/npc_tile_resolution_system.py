import logging
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.position_utils import compute_foot_tile

class NPCTileResolutionSystem:
    """
    Sistema que evita que múltiples NPCs compartan el mismo tile de pies.
    Si detecta duplicados en el mismo tile, desplaza cada NPC extra a tiles adyacentes en X.
    """
    def update(self, world, camera=None):
        comps = world.components
        occupancy: dict[tuple[int,int], list[int]] = {}
        # Recolectar tile de pies de cada NPC con ChaseTarget
        for eid in comps.get('ChaseTarget', {}):
            tile = compute_foot_tile(world, eid, TILE_SIZE)
            if tile:
                occupancy.setdefault(tile, []).append(eid)
        # Resolver colisiones de tile
        for tile, eids in occupancy.items():
            if len(eids) > 1:
                for i, eid in enumerate(eids[1:], start=1):
                    pos = comps.get('Position', {}).get(eid)
                    if not pos:
                        continue
                    # Desplazar en X para separarlos
                    offset = TILE_SIZE * i
                    pos.x += offset
                    logging.debug(f"NPC {eid} en {tile} desplazado {offset}px en X para evitar colisión")
