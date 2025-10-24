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
            pts = list(model.points)
            if not pts:
                continue
            emit_rate = max(1, int(getattr(comp, 'particle_emit_rate', 2)))
            speed = float(getattr(comp, 'particle_speed', 0.0) or 0.0)
            dispersion = float(getattr(comp, 'particle_dispersion', 0.0) or 0.0)
            size_min = getattr(comp, 'size_min', None)
            size_max = getattr(comp, 'size_max', None)
            palette = getattr(comp, 'colors_palette', None) if hasattr(comp, 'colors_palette') else None
            lifespan_frames = int(getattr(comp, 'particle_lifespan', 1) or 1)

            for i, (x, y) in enumerate(pts):
                # Base direction along the bolt
                if i < len(pts) - 1:
                    nx, ny = pts[i + 1]
                    dx, dy = (nx - x), (ny - y)
                else:
                    px = pts[i - 1][0] if i > 0 else x + 1
                    py = pts[i - 1][1] if i > 0 else y
                    dx, dy = (x - px), (y - py)
                # Normalize
                length = (dx * dx + dy * dy) ** 0.5 or 1.0
                bdx, bdy = dx / length, dy / length

                for _ in range(emit_rate):
                    # Jitter spawn pos slightly
                    sx = x + random.uniform(-1.5, 1.5)
                    sy = y + random.uniform(-1.5, 1.5)
                    pid = world.create_entity()
                    world.components.setdefault('Position', {})[pid] = Position(sx, sy)

                    # Color
                    if isinstance(palette, (list, tuple)) and palette:
                        try:
                            color = random.choice(palette)
                            color = tuple(int(max(0, min(255, c))) for c in color[:3])
                        except Exception:
                            color = (random.randint(80, 120), random.randint(180, 230), 255)
                    else:
                        color = (random.randint(80, 120), random.randint(180, 230), 255)

                    # Size: random in range if provided, otherwise fixed
                    if isinstance(size_min, int) and isinstance(size_max, int) and size_max >= size_min:
                        size = random.randint(size_min, size_max)
                    else:
                        size = int(getattr(comp, 'particle_size', 2) or 2)

                    # Velocity with angular dispersion around base direction
                    if speed > 0.0:
                        # Rotate base dir by random angle in [-dispersion, dispersion]
                        ang = random.uniform(-dispersion, dispersion)
                        ca = __import__('math').cos(ang)
                        sa = __import__('math').sin(ang)
                        vx = bdx * ca - bdy * sa
                        vy = bdx * sa + bdy * ca
                        dxp, dyp = vx * speed, vy * speed
                    else:
                        dxp, dyp = 0.0, 0.0

                    world.components.setdefault('ParticleComponent', {})[pid] = ParticleComponent(
                        dxp, dyp, color, size, lifespan_frames
                    )
