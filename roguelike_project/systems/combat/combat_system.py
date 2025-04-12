# roguelike_project/systems/combat/combat_system.py

from roguelike_project.systems.effects.animations.projectiles_system import ProjectilesManager
from roguelike_project.systems.combat.melee.basic_attack import execute_slash_attack

class CombatSystem:
    def __init__(self, state):
        self.state = state
        self.projectiles = ProjectilesManager(state)

    def update(self):
        self.projectiles.update()

    def render(self, screen, camera):
        return self._render_projectiles(screen, camera)

    def _render_projectiles(self, screen, camera):
        return self.projectiles.render(screen, camera)

    # 🎯 Slash básico con animación y lógica funcional
    def perform_slash(self):
        player = self.state.player
        direction = player.movement.last_move_dir
        if direction.length() == 0:
            direction = player.renderer.direction_vector()
        execute_slash_attack(player, direction)
