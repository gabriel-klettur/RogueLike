import time
from ..components.position import Position
from ..components.combat_stats import CombatStats
from ..components.melee_weapon import MeleeWeapon
from ..components.wants_to_melee import WantsToMelee
from ..components.attack_cooldown import AttackCooldown

class NPCMeleeDecisionSystem:
    """
    Sistema de IA que decide ataques cuerpo a cuerpo para NPCs adyacentes.
    """
    def update(self, world):
        now = time.time()
        for eid in world.get_entities_with(Position, CombatStats):
            # Busca objetivos adyacentes
            for target in world.get_entities_with(Position, CombatStats):
                if eid == target:
                    continue
                pos_a = world.components['Position'][eid]
                pos_b = world.components['Position'][target]
                # Distancia Manhattan = 1
                if abs(pos_a.x - pos_b.x) + abs(pos_a.y - pos_b.y) == 1:
                    cd = world.components['AttackCooldown'].get(eid, AttackCooldown())
                    if now >= cd.next_time:
                        world.components['WantsToMelee'][eid] = WantsToMelee(eid, target)
                        # Determina cooldown del arma o valor por defecto
                        cooldown = world.components['MeleeWeapon'].get(eid).cooldown if eid in world.components['MeleeWeapon'] else 1.0
                        world.components['AttackCooldown'][eid] = AttackCooldown(now + cooldown)
