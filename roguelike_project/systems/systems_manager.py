# roguelike_project/systems/systems_manager.py

from roguelike_project.systems.combat.managers.combat_system import CombatSystem

class SystemsManager:
    def __init__(self, state):
        self.combat = CombatSystem(state)

    def update(self):
        self.combat.update()

    def render(self, screen, camera):
        return self.combat.render(screen, camera)
