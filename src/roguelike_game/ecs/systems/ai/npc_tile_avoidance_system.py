import random
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.ai.tile_avoidance import TileAvoidance
from roguelike_game.ecs.utils.position_utils import compute_foot_tile

class NPCTileAvoidanceSystem:
    """
    Evita que múltiples NPCs compartan el mismo tile de pies.
    Asigna un componente TileAvoidance con dirección aleatoria y aplica movimiento suave
    hasta que cada NPC salga de su tile de origen.
    """
    def update(self, world, camera=None):
        comps = world.components
        occupancy: dict[tuple[int,int], list[int]] = {}
        # Mapear tile de pies a lista de NPCs
        for eid in comps.get('ChaseTarget', {}):
            tile = compute_foot_tile(world, eid, TILE_SIZE)
            if tile:
                occupancy.setdefault(tile, []).append(eid)
        # Asignar TileAvoidance a NPCs en conflicto
        for tile, eids in occupancy.items():
            if len(eids) > 1:
                for eid in eids:
                    if eid not in comps.get('TileAvoidance', {}):
                        dx, dy = TileAvoidance.random_direction()
                        # Obtener velocidad del NPC
                        movement = comps.get('MovementSpeed', {}).get(eid)
                        speed = movement.speed if movement else TILE_SIZE * 0.1
                        comps['TileAvoidance'][eid] = TileAvoidance(dx, dy, speed, tile)
        # Aplicar desplazamiento suave
        for eid, avoid in list(comps.get('TileAvoidance', {}).items()):
            pos = comps.get('Position', {}).get(eid)
            if not pos:
                del comps['TileAvoidance'][eid]
                continue
            pos.x += avoid.dx * avoid.speed
            pos.y += avoid.dy * avoid.speed
            # Si sale del tile original, remover componente
            new_tile = compute_foot_tile(world, eid, TILE_SIZE)
            if new_tile and new_tile != avoid.origin_tile:
                del comps['TileAvoidance'][eid]
