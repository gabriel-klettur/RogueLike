# roguelike_project/systems/combat/combat_system.py

from roguelike_project.systems.effects.animations.projectiles_system import ProjectilesManager

class CombatSystem:
    def __init__(self, state):
        self.state = state
        self.projectiles = ProjectilesManager(state)

    def update(self):
        self.projectiles.update()

    def render(self, screen, camera):
        return (                        
            self._render_projectiles(screen, camera)            
        )

    def _render_projectiles(self, screen, camera):
        return self.projectiles.render(screen, camera)


    

