from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.teleport_component import TeleportComponent
from roguelike_game.ecs.systems.rendering.combat.spells.teleport.view import TeleportView

class TeleportRenderSystem:
    """
    ECS system to render teleport effect using TeleportView.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
        dirty_rects = []
        for eid, comp in world.components.get('TeleportComponent', {}).items():
            view = TeleportView(comp.model)
            view.render(screen, camera)
        return dirty_rects
