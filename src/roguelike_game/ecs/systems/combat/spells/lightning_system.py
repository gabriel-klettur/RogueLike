import pygame
import random
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent

class LightningSystem:
    """
    Sistema ECS que actualiza los componentes LightningComponent:
    decrementa lifetime y elimina los finalizados.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        to_remove = []
        for eid, comp in world.components.get('LightningComponent', {}).items():
            comp.update()
            if comp.is_finished():
                to_remove.append(eid)
        for eid in to_remove:
            world.components['LightningComponent'].pop(eid, None)