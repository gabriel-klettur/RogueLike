import time
from roguelike_game.ecs.systems.combat.melee_combat_system import MeleeCombatSystem
from roguelike_game.ecs.components.ai.wants_to_melee import WantsToMelee
from roguelike_game.ecs.components.combat.attack_cooldown import AttackCooldown
from roguelike_engine.utils.benchmark import benchmark

class CombatSystem:
    """
    Wrapper para lógica de combate. Registra eventos de melee y resuelve daño.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.melee_system = MeleeCombatSystem(self.perf_log)

    def perform_melee(self, world, attacker, target):
        """
        Ejecuta un ataque cuerpo a cuerpo: registra intención y establece cooldown.
        """
        # Registrar intención de melee
        world.components['WantsToMelee'][attacker] = WantsToMelee(attacker, target)
        # Establecer cooldown de ataque
        now = time.time()
        weapon = world.components['MeleeWeapon'].get(attacker)
        cd = weapon.cooldown if weapon else 1.0
        world.components['AttackCooldown'][attacker] = AttackCooldown(now + cd)

    @benchmark(lambda self: self.perf_log, "4.2.2.CombatSystem.update")
    def update(self, world, camera=None):
        """
        Debe llamarse cada tick para resolver eventos de combate.
        """
        self.melee_system.update(world, camera)