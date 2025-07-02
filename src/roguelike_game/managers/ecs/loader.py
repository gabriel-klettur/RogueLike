"""
Loader de ECS: crea ECSWorld.
"""
from roguelike_game.ecs.core.manager import ECSWorld

class ECSLoader:
    """
    Carga y retorna ECSWorld configurado.
    """
    def load(self, screen, map_manager, buildings, perf_log):
        override_cls = getattr(ECSWorld, 'ECSWorld', None)
        cls = override_cls if callable(override_cls) else ECSWorld
        return cls(screen, map_manager, buildings, perf_log)
