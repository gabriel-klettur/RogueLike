"""
Module: collision_system.py
Handles resolving collisions between moving entities and solid tiles.
"""

# Utility to build a pygame.Rect from a collider description and entity position
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider

class CollisionSystem:
    """
    Sistema que resuelve colisiones para entidades con Position, Velocity y Collider.
    """

    def update(self, world):
        """
        Para cada entidad con Position, Velocity y Collider:
          1. Reconstruye su rectángulo de colisión en base a la posición actual.
          2. Intenta desplazar primero en el eje X; si detecta colisión con un tile sólido,
             anula la componente vx de la velocidad.
          3. Intenta desplazar en el eje Y bajo las mismas reglas, anulando vy si colisiona.
        """
        # Atajos a los mapas de componentes y lista de tiles sólidos
        comps = world.components
        pos_map = comps['Position']
        vel_map = comps['Velocity']
        col_map = comps.get('Collider', {})
        multi_map = comps.get('MultiCollider', {})
        solid_tiles = world.map_manager.solid_tiles

        # Iterar sobre entidades con posición y velocidad, y que tengan collider o multicolider
        for eid in world.get_entities_with('Position', 'Velocity'):
            pos = pos_map[eid]
            vel = vel_map[eid]
            # Preferir collider de cuerpo en MultiCollider (fallback a pies)
            if eid in multi_map:
                colliders = multi_map[eid].colliders
                col = colliders.get('body', colliders.get('feet'))
            else:
                col = col_map.get(eid)
            if col is None:
                continue

            # 1) Obtener rect de colisión en base a feet o collider
            col.rect = build_collider_rect(pos.x, pos.y, col)

            # 2) Resolver movimiento horizontal (eje X)
            if vel.vx != 0:
                # Proyectar rectángulo de colisión a la nueva posición X
                projected_x = col.rect.move(vel.vx, 0)
                # Verificar si colisiona con algún tile sólido
                if not any(projected_x.colliderect(tile.rect) for tile in solid_tiles):
                    # No hay colisión: aplicar movimiento en X
                    pos.x += vel.vx
                    col.rect = projected_x
                else:
                    # Colisión detectada: detener movimiento horizontal
                    vel.vx = 0

            # 3) Resolver movimiento vertical (eje Y)
            if vel.vy != 0:
                # Proyectar rectángulo de colisión a la nueva posición Y
                projected_y = col.rect.move(0, vel.vy)
                # Verificar colisión con tiles sólidos
                if not any(projected_y.colliderect(tile.rect) for tile in solid_tiles):
                    # Sin colisión: aplicar movimiento en Y
                    pos.y += vel.vy
                    col.rect = projected_y
                else:
                    # Colisión detectada: detener movimiento vertical
                    vel.vy = 0
