# Path: src/roguelike_game/systems/effects/spells/spells_system.py

# Benchmarking
from roguelike_engine.utils.benchmark import benchmark


class SpellsSystem:
    def __init__(self, state, perf_log, ecs_world):
        self.state = state
        self.perf_log = perf_log
        self.ecs_world = ecs_world  # Mundo ECS para colisionar con NPCs

        # Laser continuous
        self.shooting_laser = False
        self.last_laser_time = 0

    # ------------------------------------------------ #
    #                   Spawn methods                  #
    # ------------------------------------------------ #


    # ------------------------------------------------ #
    #                     Update                       #
    # ------------------------------------------------ #
    def update(self, clock, screen):
        pass



    # ------------------------------------------------ #
    #                     Render                       #
    # ------------------------------------------------ #
    @benchmark(lambda self: self.perf_log, "3.6.2 effects_render")
    def render(self, screen, camera):
        dirty_rects = []                  

        return dirty_rects
