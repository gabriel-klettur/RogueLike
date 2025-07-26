import time
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent

class MagicSpellBarSystem:
    """
    ECS system que actualiza el estado de la barra de progreso de hechizo.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, *args):
        comps = world.components
        bars = comps.get('MagicSpellBarComponent', {})
        now = time.time()
        for eid, bar in bars.items():
            if bar.active:
                # Desactivar barra cuando termine la duración
                if now - bar.start_time >= bar.duration:
                    bar.active = False
