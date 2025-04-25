# roguelike_project/systems/systems_manager.py

from roguelike_project.systems.combat.spells.spells_system import SpellsSystem

from roguelike_project.systems.combat.explosions.explosions_system import ExplosionSystem

class SystemsManager:
    def __init__(self, state):        
        self.effects = SpellsSystem(state)
        self.explosions = ExplosionSystem(state)
        

    def update(self):        
        self.effects.update()        
        self.explosions.update()

    def render(self, screen, camera):
        return (        
            self._render_effects(screen, camera) +            
            self._render_explosions(screen, camera)
        )
        
    
    def _render_effects(self, screen, camera):
        return self.effects.render(screen, camera)
    
    def _render_explosions(self, screen, camera):
        return self.explosions.render(screen, camera)