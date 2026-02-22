import pygame
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark.benchmark import benchmark

# FSM-specific debug subsystems (now under FSM editor debug package)
from .fsm_chase_debug import ChaseDebugSystem
from .fsm_states_debug import StatesDebugRenderSystem

class EntitiesDebugSystem:
    """
    Overlay de depuración del FSM Editor (solo relacionado con FSM):
    - Estados actuales sobre entidades con `NPCState`
    - Radios/visualización de persecución (Aggro/Melee, líneas, bbox)
    Actualiza la capa cada N frames y la blitea cada frame.
    """
    def __init__(self, perf_log=None):
        self.subsystems = [
            ChaseDebugSystem(perf_log),
            StatesDebugRenderSystem(perf_log),
        ]
        self._debug_surface = None
        self.perf_log = perf_log
        self._frame_count = 0
    
    def update(self, world, screen, camera):
        # frame skip counter
        self._frame_count += 1
        if not config.DEBUG_ENTITIES:
            return
        # Inicializar o redimensionar superficie
        w, h = screen.get_size()
        if self._debug_surface is None or self._debug_surface.get_size() != (w, h):
            self._debug_surface = pygame.Surface((w, h), pygame.SRCALPHA)
        # Sólo actualizar overlay cada N frames según frame_skip
        if self._frame_count % config.DEBUG_ENTITIES_FRAME_SKIP == 0:
            self._debug_surface.fill((0, 0, 0, 0))
            for sys in self.subsystems:
                sys.update(world, self._debug_surface, camera)
        # Blitear overlay siempre
        screen.blit(self._debug_surface, (0, 0))
