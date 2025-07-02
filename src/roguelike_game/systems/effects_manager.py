# Path: src/roguelike_game/systems/effects_manager.py

from roguelike_game.systems.effects.explosions.explosions_system import ExplosionSystem

class EffectsManager:

    def __init__(self, state, perf_log, ecs_world):             
        self.explosions = ExplosionSystem(state, perf_log)
        self.state = state
        self.ecs_world = ecs_world
         