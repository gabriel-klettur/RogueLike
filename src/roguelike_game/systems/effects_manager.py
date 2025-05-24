
# Path: src/roguelike_game/systems/systems_manager.py
from roguelike_game.systems.combat.spells.spells_system import SpellsSystem

from roguelike_game.systems.combat.explosions.explosions_system import ExplosionSystem

class EffectsManager:

    def __init__(self, state, perf_log):     
        self.effects = SpellsSystem(state, perf_log)
        self.explosions = ExplosionSystem(state, perf_log)
        self.state = state
        
    def update(self, clock, screen):        
        self.effects.update(clock, screen)        
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