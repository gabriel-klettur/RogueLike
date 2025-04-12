# roguelike_project/systems/systems_manager.py

from roguelike_project.systems.combat.combat_system import CombatSystem
from roguelike_project.systems.effects.effects_system import EffectsManager

class SystemsManager:
    def __init__(self, state):
        self.combat = CombatSystem(state)
        self.effects = EffectsManager(state)

    def update(self):
        self.combat.update()
        self.effects.update()

    def render(self, screen, camera):
        return self.combat.render(screen, camera) + self.effects.render(screen, camera)
