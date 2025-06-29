# Path: src/roguelike_game/ecs/systems/debug/entities_debug_system.py
import pygame
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.physics.collision_debug_system import CollisionDebugSystem
from roguelike_game.ecs.systems.rendering.hitbox_debug_system import HitboxDebugSystem
from roguelike_game.ecs.systems.core.spawn_debug_system import SpawnDebugSystem
from roguelike_game.ecs.systems.rendering.fsm.chase_debug_system import ChaseDebugSystem
from roguelike_game.ecs.systems.rendering.fsm.states_debug_render_system import StatesDebugRenderSystem
from roguelike_game.ecs.systems.rendering.death_timer_debug_system import DeathTimerDebugSystem

class EntitiesDebugSystem:
    """
    Sistema unificado para debug de entidades (colisiones, hitboxes, estados, spawn y muerte).
    Actualiza toda la capa de debug cada N frames y la blitea cada frame.
    """
    def __init__(self, perf_log=None):
        self.subsystems = [
            CollisionDebugSystem(perf_log),
            HitboxDebugSystem(perf_log),
            SpawnDebugSystem(perf_log),
            ChaseDebugSystem(perf_log),
            StatesDebugRenderSystem(perf_log),
            DeathTimerDebugSystem(perf_log=perf_log),
        ]
        self._debug_surface = None
        self.perf_log = perf_log
        self._frame_count = 0

    @benchmark(lambda self: self.perf_log, "4.2.2. EntitiesDebugSystem.update")
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