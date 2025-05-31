import time
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.fsm.states.ghost_state import GhostState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy

class PlayerGhostSystem:
    """
    Detecta la muerte del jugador y lo cambia a GhostState.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: None, "PlayerGhostSystem.update")
    def update(self, world, camera=None):
        pid = world.player_entity
        hp = world.components['Health'].get(pid)
        if hp and hp.current_hp <= 0:
            ghost_map = world.components.setdefault('IsGhost', {})
            if not ghost_map.get(pid):
                # Marcar jugador como fantasma
                ghost_map[pid] = True
                print(f"[PlayerGhostSystem] Player {pid} HP={hp.current_hp} entra en modo fantasma")
