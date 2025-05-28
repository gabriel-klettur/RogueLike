from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.utils.position_utils import compute_entity_center
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_game.ecs.components.ai.in_combat import InCombat

class ChaseSystem:
    """
    Sistema que mueve NPCs hacia su ChaseTarget.
    """

    @staticmethod
    def compute_centers(world):
        """
        Devuelve el centro del jugador y un dict de centros de NPCs {eid: (x,y)}.
        """
        comps = world.components
        # Obtener posición del jugador
        ppos = world.player_position
        pid = getattr(world, 'player_entity', None)
        pspr = comps.get('Sprite', {}).get(pid)
        if not ppos or not pspr:
            return None, None, {}
        scale_cmp = comps.get('Scale', {}).get(pid)
        vec = compute_entity_center(ppos, pspr, scale_cmp)
        cx, cy = vec.x, vec.y
        origins = {}
        for eid, _ in list(comps.get('ChaseTarget', {}).items()):
            pos = comps.get('Position', {}).get(eid)
            spr = comps.get('Sprite', {}).get(eid)
            if not pos or not spr:
                continue
            scale_cmp2 = comps.get('Scale', {}).get(eid)
            vec2 = compute_entity_center(pos, spr, scale_cmp2)
            ox, oy = vec2.x, vec2.y
            origins[eid] = (ox, oy)
        return cx, cy, origins

    def update(self, world, camera=None):
        """
        Para cada entidad con un ChaseTarget activo, calcula la dirección
        hacia su objetivo y ajusta su componente Velocity para moverla.
        """
        comps = world.components

        # Calcular colisión entre pies de jugador y NPCs
        player_id = getattr(world, 'player_entity', None)
        player_feet_rect = None
        if player_id:
            ppos = comps.get('Position', {}).get(player_id)
            pmulti = comps.get('MultiCollider', {}).get(player_id)
            if ppos and pmulti:
                pfeet = pmulti.colliders.get('feet')
                if pfeet:
                    player_feet_rect = build_collider_rect(ppos.x, ppos.y, pfeet)

        # Usar compute_centers para evitar duplicidad
        center_x, center_y, origins = ChaseSystem.compute_centers(world)
        if not origins:
            return

        # Iterar sobre centros de NPCs
        for eid, (origin_x, origin_y) in origins.items():
            # Si colisionan pies, entrar en modo combate
            if player_feet_rect:
                npos = comps.get('Position', {}).get(eid)
                nmulti = comps.get('MultiCollider', {}).get(eid)
                if npos and nmulti:
                    nfeet = nmulti.colliders.get('feet')
                    if nfeet:
                        npc_feet_rect = build_collider_rect(npos.x, npos.y, nfeet)
                        if npc_feet_rect.colliderect(player_feet_rect):
                            comps['ChaseTarget'].pop(eid, None)
                            comps['InCombat'][eid] = InCombat()
                            vel_cmp = comps.get('Velocity', {}).get(eid)
                            if vel_cmp:
                                vel_cmp.vx = 0
                                vel_cmp.vy = 0
                            continue

            # Posición del NPC
            pos = comps.get('Position', {}).get(eid)
            if not pos:
                continue
            speed_cmp = comps.get('MovementSpeed', {}).get(eid)
            speed = speed_cmp.speed if speed_cmp else 0

            # Vector hacia centro del jugador
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
