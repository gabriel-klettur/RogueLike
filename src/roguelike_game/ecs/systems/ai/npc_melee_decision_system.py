import time
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.ai.wants_to_melee import WantsToMelee
from roguelike_game.ecs.components.combat.attack_cooldown import AttackCooldown

class NPCMeleeDecisionSystem:
    """
    Sistema de IA que decide ataques cuerpo a cuerpo para NPCs adyacentes.
    """

    def update(self, world, camera=None):
        """
        Recorre todas las entidades con Position y CombatStats para:
        1. Encontrar pares de entidades adyacentes (Manhattan = 1).
        2. Comprobar cooldown de ataque.
        3. En caso de poder atacar, registrar un WantsToMelee y fijar nuevo cooldown.
        """
        # Timestamp actual para comparar con next_time de cooldowns
        now = time.time()

        # Iterar sobre cada posible atacante con posición y estadísticas de combate
        for eid in world.get_entities_with(Position, CombatStats):
            pos_a = world.components['Position'][eid]

            # Para cada posible objetivo (incluyendo otros NPCs y jugador)
            for target in world.get_entities_with(Position, CombatStats):
                if eid == target:
                    # Ignorar atacarse a uno mismo
                    continue

                pos_b = world.components['Position'][target]

                # Calcular distancia Manhattan; solo interesan adyacencias
                if abs(pos_a.x - pos_b.x) + abs(pos_a.y - pos_b.y) != 1:
                    continue

                # Obtener o crear componente AttackCooldown para este atacante
                cd = world.components['AttackCooldown'].get(eid, AttackCooldown())

                # Si el cooldown ha expirado, registrar el ataque
                if now >= cd.next_time:
                    # Señalar la intención de atacar
                    world.components['WantsToMelee'][eid] = WantsToMelee(eid, target)

                    # Determinar duración del cooldown según el arma equipada
                    if eid in world.components['MeleeWeapon']:
                        weapon_cd = world.components['MeleeWeapon'][eid].cooldown
                    else:
                        weapon_cd = 1.0  # Valor por defecto si no hay arma

                    # Actualizar el cooldown para el próximo ataque
                    world.components['AttackCooldown'][eid] = AttackCooldown(now + weapon_cd)
