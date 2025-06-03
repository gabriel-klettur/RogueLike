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

    @benchmark(lambda self: self.perf_log, "DashSystem.update")
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
                # Emit dash trail particles
                # Determine feet collider center for spawn
                multi = world.components.get('MultiCollider', {}).get(eid)
                feet_rect = None
                if multi:
                    feet = multi.colliders.get('feet')
                    if feet:
                        feet_rect = build_collider_rect(pos.x, pos.y, feet)
                if feet_rect:
                    px, py = feet_rect.center
                else:
                    px, py = pos.x, pos.y
                base_angle = math.degrees(math.atan2(dash.dir_y, dash.dir_x))
                for _ in range(2):
                    angle = math.radians(base_angle + 180 + random.uniform(-30, 30))
                    speed = random.uniform(1, 3)
                    dx = math.cos(angle) * speed
                    dy = math.sin(angle) * speed
                    color = random.choice([(200,200,255),(150,150,255),(255,255,255)])
                    size = random.randint(3,6)
                    lifespan = 15
                    peid = world.create_entity()
                    world.components.setdefault('Position', {})[peid] = Position(px + random.uniform(-5, 5), py + random.uniform(-5, 5))
                    world.components.setdefault('ParticleComponent', {})[peid] = ParticleComponent(dx, dy, color, size, lifespan)
            dash.last_update = now
            if now >= dash.start_time + dash.duration:
                to_remove.append(eid)
        for eid in to_remove:
            world.components['DashComponent'].pop(eid, None)
