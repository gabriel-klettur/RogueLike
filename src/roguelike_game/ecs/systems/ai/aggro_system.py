from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.ai.chase_target import ChaseTarget
from roguelike_game.ecs.components.core.identity import Faction

class AggroSystem:
    """
    Detecta jugadores en rango y asigna ChaseTarget a NPCs enemigos.
    """

    def update(self, world, camera=None):
        """
        Recorre todas las entidades que tengan Position, AggroRange e Identity.
        Si el jugador está dentro del radio de agresión (AggroRange) de un NPC de facción EVIL,
        le añade o actualiza el componente ChaseTarget para que empiece a perseguirlo.
        Si el jugador sale de ese rango, elimina el ChaseTarget de esa entidad.
        """
        # Intentamos obtener la entidad jugador desde el mundo
        player = getattr(world, "player", None)
        if not player:
            # Si no hay jugador, nada que procesar
            return

        # Posición del jugador en coordenadas del mundo
        px, py = player.x, player.y

        # Para cada entidad con Position, AggroRange e Identity...
        for eid in world.get_entities_with('Position', 'AggroRange', 'Identity'):
            ident = world.components['Identity'][eid]
            # Solo NPCs malvados (EVIL) pueden agredir al jugador
            if ident.faction != Faction.EVIL:
                continue

            # Recuperar posición y rango de aggro del NPC
            pos = world.components['Position'][eid]
            rng = world.components['AggroRange'][eid]

            # Calcular distancia al jugador (cuadrado para evitar sqrt)
            dx = pos.x - px
            dy = pos.y - py
            dist_sq = dx*dx + dy*dy

            # Transformar el radio de tiles a unidades del mundo y elevar al cuadrado
            aggro_radius_sq = (rng.radius * TILE_SIZE) ** 2

            if dist_sq <= aggro_radius_sq:
                # Dentro del rango: asignar ChaseTarget para perseguir al jugador
                world.components['ChaseTarget'][eid] = ChaseTarget(player)
            else:
                # Fuera de rango: eliminar ChaseTarget si existía
                world.components['ChaseTarget'].pop(eid, None)
