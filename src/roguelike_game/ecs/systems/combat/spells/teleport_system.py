import time
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.teleport_component import TeleportComponent

class TeleportSystem:
    """
    ECS system to update teleport effect: phase switch, reposition entity, expire component.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.TeleportSystem.update")
    def update(self, world, camera=None):
        for eid, comp in list(world.components.get('TeleportComponent', {}).items()):
            model = comp.model
            # switch from 'out' to 'in'
            if model.phase == 'out' and model.should_switch_phase():
                model.phase = 'in'
                model.start_time = time.time()
                # reposition entity
                pos = world.components['Position'].get(eid)
                if pos:
                    pos.x, pos.y = model.end_pos
            # remove finished effect
            if model.is_finished():
                world.components['TeleportComponent'].pop(eid, None)
