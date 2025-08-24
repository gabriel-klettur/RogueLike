import random
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

class LightningEmitterSystem:
    """
    ECS system that emits particles along the lightning path for LightningComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        # For each entity with a LightningComponent, emit particles at each lightning vertex
        for eid, comp in world.components.get('LightningComponent', {}).items():
            model = comp.model
            for x, y in model.points:
                # Add slight jitter for visual variation
                px = x + random.uniform(-2, 2)
                py = y + random.uniform(-2, 2)
                pid = world.create_entity()
                world.components.setdefault('Position', {})[pid] = Position(px, py)
                # Particle is static, lives one frame
                lifespan_frames = 1
                color = (random.randint(80, 120), random.randint(180, 230), 255)
                size = 2
                world.components.setdefault('ParticleComponent', {})[pid] = ParticleComponent(
                    0, 0, color, size, lifespan_frames
                )
