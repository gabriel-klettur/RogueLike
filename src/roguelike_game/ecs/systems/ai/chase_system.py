from roguelike_game.ecs.components.transform.velocity import Velocity

class ChaseSystem:
    """
    Sistema que mueve NPCs hacia su ChaseTarget.
    """

    def update(self, world, camera=None):
        """
        Para cada entidad con un ChaseTarget activo, calcula la dirección
        hacia su objetivo y ajusta su componente Velocity para moverla.
        """
        comps = world.components

        # Iterar sobre una copia de ChaseTarget para permitir modificaciones
        for eid, chase in list(comps.get('ChaseTarget', {}).items()):
            target = chase.target

            # Verificar que el target tenga coordenadas válidas
            if not hasattr(target, 'x') or not hasattr(target, 'y'):
                # Si el objetivo carece de posición, omitimos esta entidad
                continue

            # Obtener la posición actual de la entidad
            pos = comps['Position'].get(eid)
            if not pos:
                # Si no hay componente Position, no podemos moverla
                continue

            # Determinar la velocidad de movimiento base de la entidad
            speed_cmp = comps['MovementSpeed'].get(eid)
            speed = speed_cmp.speed if speed_cmp else 0

            # Calcular vector bruto hacia el objetivo
            dx = target.x - pos.x
            dy = target.y - pos.y

            # Decidir movimiento principalmente en el eje más grande
            if abs(dx) > abs(dy):
                # Movimiento horizontal
                vx = speed if dx > 0 else -speed
                vy = 0
            else:
                # Movimiento vertical
                vx = 0
                vy = speed if dy > 0 else -speed

            # Asignar o actualizar el componente Velocity
            vel = comps['Velocity'].get(eid)
            if vel:
                vel.vx = vx
                vel.vy = vy
            else:
                # Crear componente Velocity si no existe
                comps['Velocity'][eid] = Velocity(vx, vy)
