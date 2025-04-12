from roguelike_project.systems.combat.managers.projectiles_manager import ProjectilesManager
from roguelike_project.systems.combat.managers.explosions_manager import ExplosionsManager
from roguelike_project.systems.effects.effects_system import EffectsManager
from roguelike_project.utils.benchmark import benchmark

class CombatSystem:
    def __init__(self, state):
        self.state = state

        self.explosions = ExplosionsManager(state)
        self.projectiles = ProjectilesManager(state)
        self.effects = EffectsManager(state)

    def update(self):
        self.projectiles.update()
        self.explosions.update()
        self.effects.update()

    def render(self, screen, camera):
        return (
            self._render_projectiles(screen, camera) +
            self._render_explosions(screen, camera) +
            self._render_effects(screen, camera)
        )
    
    def _render_projectiles(self, screen, camera):
        return self.projectiles.render(screen, camera)
    
    def _render_explosions(self, screen, camera):
        return self.explosions.render(screen, camera)
    
    def _render_effects(self, screen, camera):
        return self.effects.render(screen, camera)
