# Path: src/roguelike_game/systems/combat/explosions/explosions_system.py
from roguelike_engine.utils.benchmark import benchmark

class ExplosionSystem:
    def __init__(self, state, perf_log):
        self.state = state
        self.explosions = []
        self.perf_log = perf_log

    def add_explosion(self, explosion_obj):        
        self.explosions.append(explosion_obj)
    
    def update(self):
        self.explosions[:] = [e for e in self.explosions if not e.finished]
        for e in self.explosions:
            e.update()

    @benchmark(lambda self: self.perf_log, "3.6.3 explosions_render")
    def render(self, screen, camera):
        dirty_rects = []
        for e in self.explosions:
            if (d := e.render(screen, camera)):
                dirty_rects.append(d)
        return dirty_rects