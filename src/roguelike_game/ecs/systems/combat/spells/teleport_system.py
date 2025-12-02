import time
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.abilities.teleport_component import TeleportComponent

class TeleportSystem:
    """
    ECS system to update teleport effect: phase switch, reposition entity, expire component.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        # Only process TeleportComponent instances from the abilities package.
        # The same component key is also used by item teleports, which do not
        # expose a 'model' attribute and are handled by the items.teleport_system.
        for eid, comp in list(world.components.get('TeleportComponent', {}).items()):
            if not isinstance(comp, TeleportComponent):
                continue
            model = getattr(comp, 'model', None)
            if model is None:
                continue
            # switch from 'out' to 'in'
            if model.phase == 'out' and model.should_switch_phase():
                model.phase = 'in'
                model.start_time = time.time()
                # reposition entity
                pos = world.components['Position'].get(eid)
                if pos:
                    end_x, end_y = model.end_pos
                    spr = world.components.get('Sprite', {}).get(eid)
                    if spr is not None and getattr(spr, 'image', None) is not None:
                        try:
                            w, h = spr.image.get_size()
                            end_x -= w / 2
                            end_y -= h / 2
                        except Exception:
                            pass
                    pos.x, pos.y = end_x, end_y
            # remove finished effect
            if model.is_finished():
                world.components['TeleportComponent'].pop(eid, None)
