import math
from ..components.position import Position
from ..components.aggro_range import AggroRange
from ..components.identity import Identity, Faction
from ..components.chase_target import ChaseTarget

class AggroSystem:
    """
    Detecta jugadores en rango y asigna ChaseTarget a NPCs enemigos.
    """
    def update(self, world):
        player = getattr(world, "player", None)
        if not player:
            return
        px, py = player.x, player.y
        from roguelike_engine.config.config_tiles import TILE_SIZE
        for eid in world.get_entities_with('Position', 'AggroRange', 'Identity'):
            ident = world.components['Identity'][eid]
            # Sólo NPCs con facción EVIL persiguen al player
            if ident.faction != Faction.EVIL:
                continue
            pos = world.components['Position'][eid]
            rng = world.components['AggroRange'][eid]
            dx = pos.x - px
            dy = pos.y - py
            if dx*dx + dy*dy <= (rng.radius * TILE_SIZE) ** 2:
                world.components['ChaseTarget'][eid] = ChaseTarget(player)
            else:
                world.components['ChaseTarget'].pop(eid, None)
