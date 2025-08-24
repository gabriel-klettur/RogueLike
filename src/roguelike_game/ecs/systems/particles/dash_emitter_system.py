import random
import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_game.ecs.utils.collider_utils import build_collider_rect

class DashEmitterSystem:
    """
    ECS system that emits dash trail particles for entities with DashComponent.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        for eid, dash in list(world.components.get('DashComponent', {}).items()):
            pos_cmp = world.components.get('Position', {}).get(eid)
            if not pos_cmp:
                continue
            # Determine feet collider center for spawn
            multi = world.components.get('MultiCollider', {}).get(eid)
            feet_rect = None
            if multi:
                feet = multi.colliders.get('feet')
                if feet:
                    feet_rect = build_collider_rect(pos_cmp.x, pos_cmp.y, feet)
            if feet_rect:
                px, py = feet_rect.center
            else:
                px, py = pos_cmp.x, pos_cmp.y
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
                world.components.setdefault('Position', {})[peid] = Position(
                    px + random.uniform(-5, 5), py + random.uniform(-5, 5)
                )
                world.components.setdefault('ParticleComponent', {})[peid] = ParticleComponent(
                    dx, dy, color, size, lifespan
                )
