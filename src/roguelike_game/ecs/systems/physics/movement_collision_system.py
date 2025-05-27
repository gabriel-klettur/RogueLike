"""
Module: movement_collision_system.py
Handles movement-based collision for entities using their 'feet' collider.
Checks collisions against solid tiles and buildings, resolving movement per axis.
"""

from roguelike_game.ecs.utils.collider_utils import build_collider_rect

class MovementCollisionSystem:
    """
    Sistema que resuelve colisiones de movimiento usando el collider 'feet'.
    Se aplica un test por separado en X y en Y para un movimiento suave y consistente.
    """

    def update(self, world, camera=None):
        """
        Recorre todas las entidades con Position, Velocity y MultiCollider, y para cada una:
          1. Obtiene el collider de 'feet' y lo posiciona en el mundo.
          2. Intenta desplazarlo en el eje X:
             - Mueve el rectángulo del collider.
             - Consulta colisiones broad-phase usando el spatial index.
             - Si no hay colisión, actualiza la posición X de la entidad.
             - Si colisiona, restaura la posición original y anula la velocidad X.
          3. Repite el mismo proceso para el eje Y.
        """
        # 1) Preparar referencias locales para eficiencia
        comps     = world.components
        pos_map   = comps['Position']
        vel_map   = comps['Velocity']
        multi_map = comps['MultiCollider']
        tile_query = world.get_solid_tiles_for_rect  # spatial index query

        # 2) Iterar sobre entidades que puedan moverse y colisionar
        for eid in world.get_entities_with('Position', 'Velocity', 'MultiCollider'):
            pos   = pos_map[eid]
            vel   = vel_map[eid]
            multi = multi_map[eid]

            # 2a) Obtener el collider de los pies; si no existe, saltar
            feet = multi.colliders.get('feet')
            if not feet:
                continue

            # 2b) Sincronizar la posición del rect del collider con la posición actual
            feet.rect = build_collider_rect(pos.x, pos.y, feet)

            # 3) Resolver movimiento en X
            if vel.vx != 0:
                # Guardar la posición original en caso de colisión
                old_x = feet.rect.x
                # Mover el collider en X
                feet.rect.x += vel.vx

                # Test broad-phase + precise: si no colisiona con ningún tile sólido
                nearby = tile_query(feet.rect)
                if feet.rect.collidelist(nearby) == -1:
                    # Movimiento permitido: aplicar al Position
                    pos.x += vel.vx
                else:
                    # Colisión detectada: revertir movimiento del collider y anular velocidad
                    feet.rect.x = old_x
                    vel.vx = 0

            # 4) Resolver movimiento en Y (idéntico al de X)
            if vel.vy != 0:
                old_y = feet.rect.y
                feet.rect.y += vel.vy

                nearby = tile_query(feet.rect)
                if feet.rect.collidelist(nearby) == -1:
                    pos.y += vel.vy
                else:
                    feet.rect.y = old_y
                    vel.vy = 0
