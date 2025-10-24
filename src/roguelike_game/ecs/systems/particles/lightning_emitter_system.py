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
                # Particle parameters: prefer resolver-passed values
                lifespan_frames = getattr(comp, 'particle_lifespan', 1) if hasattr(comp, 'particle_lifespan') else 1
                size = getattr(comp, 'particle_size', 2) if hasattr(comp, 'particle_size') else 2
                palette = getattr(comp, 'colors_palette', None) if hasattr(comp, 'colors_palette') else None
                if isinstance(palette, (list, tuple)) and palette:
                    try:
                        color = random.choice(palette)
                        # clamp to RGB
                        color = tuple(int(max(0, min(255, c))) for c in color[:3])
                    except Exception:
                        color = (random.randint(80, 120), random.randint(180, 230), 255)
                else:
                    color = (random.randint(80, 120), random.randint(180, 230), 255)
                world.components.setdefault('ParticleComponent', {})[pid] = ParticleComponent(
                    0, 0, color, size, lifespan_frames
                )
