import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.sphere_magic_shield_component import SphereMagicShieldComponent

class SphereMagicShieldSystem:
    """
    ECS system to update sphere magic shield: follows caster center, pulses radius and expires component.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.SphereMagicShieldSystem.update")
    def update(self, world, camera=None):
        for eid, comp in list(world.components.get('SphereMagicShieldComponent', {}).items()):
            # Follow caster center (eid is the caster key for this component)
            pos_cmp = world.components.get('Position', {}).get(eid)
            if pos_cmp is not None:
                cx, cy = pos_cmp.x, pos_cmp.y
                # If sprite available, center within its bounds
                spr = world.components.get('Sprite', {}).get(eid)
                if spr is not None and getattr(spr, 'image', None) is not None:
                    try:
                        w, h = spr.image.get_size()
                        cx += w / 2
                        cy += h / 2
                    except Exception:
                        pass
                comp.model.x = cx
                comp.model.y = cy

            # Pulse radius
            t = comp.model.elapsed()
            pulse = math.sin(t * 4) * 0.1
            comp.model.radius = int(comp.model.base_radius * (1 + pulse))
            # Remove finished
            if comp.model.is_finished():
                world.components['SphereMagicShieldComponent'].pop(eid, None)
