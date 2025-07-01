# Path: src/roguelike_game/ecs/systems/combat/spells/dash_system.py
import time
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.dash_component import DashComponent
from roguelike_game.ecs.components.transform.position import Position
import random
import math
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_game.ecs.utils.collider_utils import build_collider_rect

class DashSystem:
    """
    ECS system that moves entities with DashComponent during dash duration.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.DashSystem.update")
    def update(self, world, camera=None):
        now = time.time()
        to_remove = []
        for eid, dash in list(world.components.get('DashComponent', {}).items()):
            delta = now - dash.last_update
            if delta <= 0:
                continue
            pos = world.components.get('Position', {}).get(eid)
            if pos:
                move_dist = dash.speed * delta
                pos.x += dash.dir_x * move_dist
                pos.y += dash.dir_y * move_dist

            dash.last_update = now
            if now >= dash.start_time + dash.duration:
                to_remove.append(eid)
        for eid in to_remove:
            world.components['DashComponent'].pop(eid, None)