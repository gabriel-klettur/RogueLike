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

        # Obtener centro del sprite del jugador ECS
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        player_pos = comps.get('Position', {}).get(player_eid)
        player_sprite = comps.get('Sprite', {}).get(player_eid)
        if not player_pos or not player_sprite:
            return
        center_x = player_pos.x + player_sprite.image.get_width() / 2
        center_y = player_pos.y + player_sprite.image.get_height() / 2

        # Iterar sobre una copia de ChaseTarget para permitir modificaciones
        for eid, chase in list(comps.get('ChaseTarget', {}).items()):
            # Posición del NPC
            pos = comps.get('Position', {}).get(eid)
            if not pos:
                continue
            # Velocidad de NPC
            speed_cmp = comps.get('MovementSpeed', {}).get(eid)
            speed = speed_cmp.speed if speed_cmp else 0

            # Ajustar origen al centro del sprite del NPC
            npc_sprite = comps.get('Sprite', {}).get(eid)
            if npc_sprite:
                origin_x = pos.x + npc_sprite.image.get_width() / 2
                origin_y = pos.y + npc_sprite.image.get_height() / 2
            else:
                origin_x = pos.x
                origin_y = pos.y

            # Calcular vector bruto hacia el centro del jugador
            dx = center_x - origin_x
            dy = center_y - origin_y

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
